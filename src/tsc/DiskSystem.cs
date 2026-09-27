using TypeScript.Compiler;

namespace TypeScript.Tsc;

// ported from: src/compiler/sys.ts's `getNodeSystem` — the parts a command-line run needs, over the CLR's file system
/// <summary>
/// The <see cref="ISystem"/> the <c>tsc</c> front end runs against: the real file system, the real environment and the
/// real console.
/// </summary>
/// <remarks>
/// PORT-NOTE: the reference's <c>getNodeSystem</c> (sys.ts:1468-1953) is not ported line by line (docs/architecture.md
/// §13.2): it is written against Node's <c>fs</c>/<c>os</c>/<c>process</c>/<c>child_process</c> and its
/// <c>readDirectory</c> re-implements directory walking in JavaScript for case-insensitive hosts, none of which has a
/// counterpart here. What is ported is the *contract* (<see cref="ISystem"/>, the policy layer) and the pieces of the
/// Node binding a compiler run actually reaches — read/write files, resolve paths, report modification times, print to
/// the console. <c>getNodeSystem</c>'s watchers, its CPU profiler and its <c>require</c> are absent by design: watch
/// mode is the rest of M8, and this front end does not claim to be tsc yet (see <c>Program.cs</c>'s PORT-NOTE).
/// <para>
/// PORT-NOTE: the default library is resolved from <c>getExecutingFilePath()</c>'s directory in the reference, which
/// is the directory of <c>tsc.js</c> — i.e. the shipped <c>lib/</c> next to the compiler. The port's compiler assembly
/// ships no libraries, so the directory is a constructor argument and the front end defaults it (see
/// <c>Program.ResolveLibDirectory</c>).
/// </para>
/// </remarks>
internal sealed class DiskSystem(string libDir) : ISystem
{
    /// <summary>The directory <see cref="GetExecutingFilePath"/> lives in, i.e. where the default libraries are.</summary>
    private readonly string _libDirectory = Paths.NormalizePath(libDir);

    public IList<string> Args => [];

    /// <remarks>PORT-NOTE: the reference asks the platform (<c>os.EOL</c>); so does this one, and the port's diagnostic
    /// formatter takes the newline from the host exactly as <c>formatDiagnostic</c> does.</remarks>
    public string NewLine => Environment.NewLine;

    public bool UseCaseSensitiveFileNames => false;

    public void Write(string s) => Console.Out.Write(s);

    public string? ReadFile(string path, string? encoding = null)
    {
        try
        {
            return File.ReadAllText(ToDiskPath(path));
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException)
        {
            // The same answers a `File.Exists` check would give: missing, or present but not readable for this process.
            // Asking the file system once rather than twice is the fix tools/ProjectBench measured (docs §18.4m).
            return null;
        }
    }

    /// <summary>Writes an emitted file, creating its directory the way <c>writeFileEnsuringDirectories</c> does.</summary>
    public void WriteFile(string path, string data, bool writeByteOrderMark = false)
    {
        var full = ToDiskPath(path);
        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(full, data, new System.Text.UTF8Encoding(writeByteOrderMark));
    }

    public string ResolvePath(string path) => Paths.NormalizePath(ToDiskPath(path)).Replace('\\', '/');

    public bool FileExists(string path) => File.Exists(ToDiskPath(path));

    public bool DirectoryExists(string path) => Directory.Exists(ToDiskPath(path));

    public void CreateDirectory(string path) => Directory.CreateDirectory(ToDiskPath(path));

    public string GetExecutingFilePath() => _libDirectory + "/tsc.js";

    public string GetCurrentDirectory() => Paths.NormalizePath(Directory.GetCurrentDirectory()) + "/";

    public IList<string> GetDirectories(string path)
        => Directory.Exists(ToDiskPath(path))
            ? [.. Directory.GetDirectories(ToDiskPath(path)).Select(d => Paths.NormalizePath(d))]
            : [];

    /// <remarks>PORT-NOTE: `exclude`/`include`/`depth` are ignored, as they are in <c>getNodeSystem</c>'s
    /// <c>readDirectory</c> when the host is not case-insensitive — the compiler's own directory scans go through
    /// <c>matchFiles</c> instead.</remarks>
    public IList<string> ReadDirectory(
        string path,
        IList<string>? extensions = null,
        IList<string>? exclude = null,
        IList<string>? include = null,
        int? depth = null)
        => Directory.Exists(ToDiskPath(path))
            ? [.. Directory.GetFiles(ToDiskPath(path))
                .Where(f => extensions is null || extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Select(f => Paths.NormalizePath(f))]
            : [];

    public void Exit(int exitCode = 0) => Environment.Exit(exitCode);

    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    /// <summary>The file's size, or <c>null</c> when the file is not there — <c>sys.getFileSize</c>.</summary>
    public long? GetFileSize(string path) => File.Exists(ToDiskPath(path)) ? new FileInfo(ToDiskPath(path)).Length : null;

    public DateTimeOffset? GetModifiedTime(string path)
        => File.Exists(ToDiskPath(path)) ? File.GetLastWriteTimeUtc(ToDiskPath(path)) : null;

    /// <summary>Maps the program's normalized (forward-slash) path onto a path the CLR can open.</summary>
    private static string ToDiskPath(string path) => path.Replace('/', Path.DirectorySeparatorChar);
}
