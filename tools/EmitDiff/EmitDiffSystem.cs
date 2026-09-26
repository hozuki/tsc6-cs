namespace TypeScript.Compiler.Tools;

/// <summary>
/// The one-file host <c>tools/EmitDiff</c> runs the port against: the same single-file compiler host the node half
/// builds in <c>ts-emit.cjs</c> — the file, and nothing else. No default library and no module resolution, so both
/// sides see a program of exactly one source file. <see cref="WriteFile"/> is a no-op because the emit half captures
/// the output through the callback it hands <c>Program.Emit</c>, exactly as the node half does through
/// <c>program.emit(undefined, writeFile)</c>: the program's own <c>writeFile</c> is only reached when no callback is
/// supplied (program.ts:2598-2637's <c>writeFile: writeFileCallback || writeFile</c>).
/// </summary>
/// <remarks>
/// PORT-NOTE: the port has no <c>sys</c> singleton (§13.5) and no concrete <see cref="ISystem"/> besides the OS one
/// §13 keeps behind the interface, so a tool that needs a program supplies its own — the same reason the test assembly
/// has <c>FakeSystem</c> and <c>tools/BindDiff</c> has its own options object. Only the members the program and its
/// host read / are implemented; the rest answer as "absent", which is what the node half's host does for every path but
/// its own file. <see cref="NewLine"/> is <c>"\n"</c> for the same reason both halves set <c>newLine: LineFeed</c> in
/// their options: the emitted text is compared byte for byte, so no side may take its line terminator from the OS.
/// </remarks>
internal sealed class EmitDiffSystem(string fileName, string text) : ISystem
{
    /// <summary>The corpus-relative path of the one file this host serves, as the program hands it back.</summary>
    private readonly string _fileName = Normalize(fileName);

    // The program normalizes the name before it asks the host (Paths.NormalizePath), and whether that keeps the
    // corpus-relative spelling or roots it is the program's business — this tool compares the two spellings' identity,
    // not their form. Comparing whole relative paths (rather than file names) is what keeps a path in another directory
    // from being mistaken for this file: the node half's host answers true only for its own name either way.
    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('.', '/');

    private bool IsTheFile(string path) => Normalize(path) == _fileName;

    public IList<string> Args => [];

    public string NewLine => "\n";

    public bool UseCaseSensitiveFileNames => true;

    public void Write(string s)
    {
    }

    public string? ReadFile(string path, string? encoding = null) => IsTheFile(path) ? text : null;

    public void WriteFile(string path, string data, bool writeByteOrderMark = false)
    {
    }

    public string ResolvePath(string path) => path;

    public bool FileExists(string path) => IsTheFile(path);

    public bool DirectoryExists(string path) => false;

    public void CreateDirectory(string path)
    {
    }

    /// <summary>The default library's directory is derived from this; the file it names never exists here.</summary>
    public string GetExecutingFilePath() => "/__emitdiff__.js";

    public string GetCurrentDirectory() => "/";

    public IList<string> GetDirectories(string path) => [];

    public IList<string> ReadDirectory(
        string path,
        IList<string>? extensions = null,
        IList<string>? exclude = null,
        IList<string>? include = null,
        int? depth = null)
        => [];

    public void Exit(int exitCode = 0)
    {
    }

    /// <summary>
    /// The file's modified time, which this host does not track.
    /// </summary>
    /// <remarks>
    /// <see cref="IModifiedTimeHost"/> declares this one member (ISystem only defaults <c>SetModifiedTime</c>), and the
    /// port's program asks for it only when a resolution cache exists — which this tool never installs. <c>null</c> is
    /// TS's <c>undefined</c> here, the same answer a host without the member gives.
    /// </remarks>
    public DateTimeOffset? GetModifiedTime(string path) => null;

    public string? GetEnvironmentVariable(string name) => null;
}
