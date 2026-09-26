using System.Text;

namespace TypeScript.Compiler.Tools;

/// <summary>
/// The virtual file system <c>tools/BaselineDiff</c> gives the port, mirroring the one the harness builds in
/// <c>Harness.Compiler.compileFiles</c>: the test's own documents are written under the virtual source folder, and two
/// physical directories are mounted read-only at fixed virtual roots. Everything a baseline prints has those prefixes
/// stripped again (<c>Utils.removeTestPathPrefixes</c> strips <c>/.src/</c>, <c>/.ts/</c> and <c>/.lib/</c>), which is
/// why the roots must be spelled exactly as the harness spells them.
/// </summary>
/// <remarks>
/// <para>
/// The mounts are <c>src/harness/vfsUtil.ts:1573-1580</c>'s: <c>/.ts</c> is where the harness mounts
/// <c>built/local</c> (the compiled default-library files, which the build copies from <c>src/lib</c>; this tool mounts
/// the published <c>lib/</c> directory instead, because the checkout has no <c>built/</c>) and <c>/.lib</c> is
/// <c>tests/lib</c>. The portable <c>ts.sys.readFile</c> strips a UTF-8 BOM before the parser sees the text, so
/// <see cref="ReadFile"/> does too — a BOM left in place would shift every position in a BOM-carrying source file by
/// one and report a divergence that is not in the port.
/// </para>
/// <para>
/// Case sensitivity follows the harness: <c>compileFiles</c> computes
/// <c>useCaseSensitiveFileNames = options.useCaseSensitiveFileNames ?? true</c> and builds the vfs with
/// <c>ignoreCase = !useCaseSensitiveFileNames</c>, so a run is case-sensitive unless the test asked otherwise. That is
/// also what makes a run's result independent of the host OS.
/// </para>
/// <para>
/// PORT-NOTE (<c>readDirectory</c>): the portable sys forwards to <c>ts.matchFiles</c>
/// (src/compiler/utilities.ts:9800-9905), which the port has not taken (its only client is M8's config reader). The
/// listing here is the same walk — extension filter, exclude spec, include spec, depth, and the implicit-glob pruning
/// of directory names — built on the port's own <see cref="Wildcard"/>. It is reached only from the compiler's
/// automatic-type-directive and module-resolution paths, which over a corpus with no <c>node_modules</c> and no
/// <c>@types</c> directory ask empty questions; what is <em>not</em> reproduced is <c>matchFiles</c>' symlink
/// deduplication (the corpus declares no symlinks) and its <c>includeDirectoryPattern</c> pruning (a directory outside
/// the include specs is still listed, so at worst a caller sees a name it then finds no file under).
/// </para>
/// </remarks>
internal sealed class BaselineDiffVirtualFileSystem
{
    /// <summary>src/harness/vfsUtil.ts:25 — <c>builtFolder</c>, the mount point of the default-library files.</summary>
    internal const string BuiltFolder = "/.ts";

    /// <summary>src/harness/vfsUtil.ts:17 — <c>testLibFolder</c>, the mount point of <c>tests/lib</c>.</summary>
    internal const string TestLibFolder = "/.lib";

    /// <summary>src/harness/vfsUtil.ts:30 — <c>srcFolder</c>, the default current directory and the home of the
    /// test's own documents.</summary>
    internal const string SrcFolder = "/.src";

    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);
    private readonly List<(string Prefix, string Root)> _mounts;
    private readonly bool _ignoreCase;

    public BaselineDiffVirtualFileSystem(string libDirectory, string testLibDirectory, bool ignoreCase, string currentDirectory)
    {
        _mounts = [(BuiltFolder, libDirectory), (TestLibFolder, testLibDirectory)];
        _ignoreCase = ignoreCase;
        Cwd = Paths.GetNormalizedAbsolutePath(Paths.NormalizeSlashes(currentDirectory), "/");
    }

    public string Cwd { get; }

    public bool UseCaseSensitiveFileNames => !_ignoreCase;

    /// <summary>Writes one document at its own (possibly relative) unit name, which is what the harness's
    /// <c>fs.writeFileSync(document.file, …)</c> does with the vfs's current directory as the base.</summary>
    public void AddDocument(string unitName, string text) => _files[Canonical(Resolve(unitName))] = text;

    /// <summary>src/harness/vfsUtil.ts:1261-1300's `fs.chdir(cwd)` + `fs.writeFileSync` behaviour for the program's own
    /// writes.</summary>
    public void WriteFile(string path, string text) => _files[Canonical(Resolve(path))] = text;

    public string? ReadFile(string path)
    {
        var absolute = Resolve(path);
        if (_files.TryGetValue(Canonical(absolute), out var text)) return text;
        var physical = PhysicalPath(absolute);
        if (physical is null || !File.Exists(physical)) return null;
        var content = Encoding.UTF8.GetString(File.ReadAllBytes(physical));
        return content.Length > 0 && content[0] == '\uFEFF' ? content[1..] : content;
    }

    public bool FileExists(string path) => ReadFile(path) is not null;

    public bool DirectoryExists(string path)
    {
        var absolute = Resolve(path);
        if (absolute == "/") return true;
        var physical = PhysicalPath(absolute);
        if (physical is not null) return Directory.Exists(physical);
        // A virtual directory is one a document was written under; a file's own path is not one.
        if (_files.ContainsKey(Canonical(absolute))) return false;
        var prefix = Canonical(absolute) + "/";
        foreach (var key in _files.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    public List<string> GetDirectories(string path)
    {
        var absolute = Resolve(path);
        var result = new List<string>();
        foreach (var (name, isDirectory) in EntriesOf(absolute))
        {
            if (isDirectory && !result.Contains(name)) result.Add(name);
        }

        return result;
    }

    /// <summary>The files and directories directly inside one virtual path, in the same two sources of entries
    /// <see cref="GetDirectories"/> reads: the mounted directory's physical children and the virtual documents.</summary>
    private List<(string Name, bool IsDirectory)> EntriesOf(string absolute)
    {
        var result = new List<(string Name, bool IsDirectory)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var physical = PhysicalPath(absolute);
        if (physical is not null && Directory.Exists(physical))
        {
            foreach (var entry in Directory.GetDirectories(physical))
            {
                var name = Path.GetFileName(entry);
                if (seen.Add(name)) result.Add((name, true));
            }

            foreach (var entry in Directory.GetFiles(physical))
            {
                var name = Path.GetFileName(entry);
                if (seen.Add(name)) result.Add((name, false));
            }
        }

        var prefix = Canonical(absolute);
        if (prefix != "/") prefix += "/";
        foreach (var key in _files.Keys)
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var rest = key[prefix.Length..];
            var slash = rest.IndexOf('/');
            var name = slash < 0 ? rest : rest[..slash];
            if (seen.Add(name)) result.Add((name, slash >= 0));
        }

        result.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        return result;
    }

    /// <summary>src/compiler/utilities.ts:9800-9905 — <c>matchFiles</c>'s walk over a directory tree: a file is kept
    /// when its extension is listed and no exclude spec matches it and an include spec does; a directory is descended
    /// into unless its own name is an implicit glob the depth limit has reached.</summary>
    public List<string> ReadDirectory(string rootDir, List<string>? extensions, List<string>? excludes, List<string>? includes, int? depth)
    {
        var root = Resolve(rootDir);
        var includeFileRegexes = Wildcard.GetRegularExpressionsForWildcards(includes, root, Wildcard.Usage.Files)
            ?.Select(pattern => Wildcard.GetRegexFromPattern(pattern, UseCaseSensitiveFileNames))
            .ToList();
        var results = new List<string>();
        VisitDirectory(root, depth);
        results.Sort(StringComparer.Ordinal);
        return results;

        void VisitDirectory(string directory, int? remainingDepth)
        {
            foreach (var (name, isDirectory) in EntriesOf(directory))
            {
                if (name is "." or "..") continue;
                if (Wildcard.MatchesExclude(pathToCheck: directory == "/" ? "/" + name : directory + "/" + name, excludes, UseCaseSensitiveFileNames, Cwd)) continue;
                if (isDirectory)
                {
                    if (remainingDepth == 0 && Wildcard.IsImplicitGlob(name)) continue;
                    VisitDirectory(Paths.CombinePaths(directory, name), remainingDepth - 1);
                }
                else
                {
                    if (extensions is not null && !Paths.FileExtensionIsOneOf(name, extensions)) continue;
                    if (includeFileRegexes is not null && !includeFileRegexes.Any(regex => regex.IsMatch(name))) continue;
                    results.Add(name);
                }
            }
        }
    }

    /// <summary>The physical path a mounted virtual root maps to, or <c>null</c> for a purely virtual path.</summary>
    private string? PhysicalPath(string absolute)
    {
        foreach (var (prefix, root) in _mounts)
        {
            if (absolute != prefix && !absolute.StartsWith(prefix + "/", StringComparison.Ordinal)) continue;
            var rest = absolute[prefix.Length..].TrimStart('/');
            return rest.Length == 0 ? root : Path.Combine(root, rest.Replace('/', Path.DirectorySeparatorChar));
        }

        return null;
    }

    /// <summary>The harness's own path normalization for a read: rooted paths keep their spelling, relative ones are
    /// resolved against the current directory (the vfs's <c>cwd</c>, which <c>fs.chdir</c> set).</summary>
    private string Resolve(string path)
    {
        var normalized = Paths.NormalizeSlashes(path);
        if (!Paths.IsRootedDiskPath(normalized)) normalized = Paths.CombinePaths(Cwd, normalized);
        return Paths.GetNormalizedAbsolutePath(normalized, Cwd);
    }

    private string Canonical(string path) => _ignoreCase ? path.ToLowerInvariant() : path;
}

/// <summary>
/// The system the port's own compiler host runs against: <see cref="BaselineDiffVirtualFileSystem"/> behind
/// <see cref="ISystem"/>. The port's <c>CompilerHost</c> reads its default library location from
/// <c>GetExecutingFilePath</c> (program.ts:453), so the executing path's directory <em>is</em> the harness's
/// <c>defaultLibLocation</c> of <c>/.ts</c> and no host override is needed for the library path.
/// </summary>
internal sealed class BaselineDiffSystem(BaselineDiffVirtualFileSystem vfs) : ISystem
{
    internal BaselineDiffVirtualFileSystem Vfs { get; } = vfs;

    public IList<string> Args => [];

    public string NewLine => "\r\n";

    public bool UseCaseSensitiveFileNames => Vfs.UseCaseSensitiveFileNames;

    public void Write(string s)
    {
    }

    public string? ReadFile(string path, string? encoding = null) => Vfs.ReadFile(path);

    public void WriteFile(string path, string data, bool writeByteOrderMark = false)
        => Vfs.WriteFile(path, writeByteOrderMark ? BaselineDiffHarness.AddUTF8ByteOrderMark(data) : data);

    public string ResolvePath(string path) => path;

    public bool FileExists(string path) => Vfs.FileExists(path);

    public bool DirectoryExists(string path) => Vfs.DirectoryExists(path);

    public void CreateDirectory(string path)
    {
    }

    public string GetExecutingFilePath() => BaselineDiffVirtualFileSystem.BuiltFolder + "/baseline-diff.js";

    public string GetCurrentDirectory() => Vfs.Cwd;

    public IList<string> GetDirectories(string path) => Vfs.GetDirectories(path);

    public IList<string> ReadDirectory(
        string path,
        IList<string>? extensions = null,
        IList<string>? exclude = null,
        IList<string>? include = null,
        int? depth = null)
        => Vfs.ReadDirectory(path, extensions?.ToList(), exclude?.ToList(), include?.ToList(), depth);

    public void Exit(int exitCode = 0)
    {
    }

    public DateTimeOffset? GetModifiedTime(string path) => null;

    public string? GetEnvironmentVariable(string name) => null;
}
