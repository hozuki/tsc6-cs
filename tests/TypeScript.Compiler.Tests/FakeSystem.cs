namespace TypeScript.Compiler.Tests;

/// <summary>
/// A minimal in-memory <see cref="ISystem"/> for testing code that depends on the host interface.
/// </summary>
/// <remarks>
/// Only TS's <em>required</em> members are implemented; everything optional comes from the interface's
/// default implementations, which is exactly the split the port relies on (see <see cref="ISystem"/>).
/// </remarks>
internal sealed class FakeSystem : ISystem
{
    public IReadOnlyList<string> Args { get; set; } = [];

    public string NewLine { get; set; } = "\n";

    public bool UseCaseSensitiveFileNames { get; set; } = true;

    /// <summary>Backs <see cref="GetModifiedTime"/>; <c>null</c> models a host that cannot report one.</summary>
    public DateTimeOffset? ModifiedTime { get; set; }

    public List<string> Written { get; } = [];

    public void Write(string s) => Written.Add(s);

    public string? ReadFile(string path, string? encoding = null) => null;

    public void WriteFile(string path, string data, bool writeByteOrderMark = false)
    {
    }

    public string ResolvePath(string path) => path;

    public bool FileExists(string path) => false;

    public bool DirectoryExists(string path) => false;

    public void CreateDirectory(string path)
    {
    }

    public string GetExecutingFilePath() => string.Empty;

    public string GetCurrentDirectory() => string.Empty;

    public IReadOnlyList<string> GetDirectories(string path) => [];

    public IReadOnlyList<string> ReadDirectory(
        string path,
        IReadOnlyList<string>? extensions = null,
        IReadOnlyList<string>? exclude = null,
        IReadOnlyList<string>? include = null,
        int? depth = null) => [];

    public void Exit(int exitCode = 0)
    {
    }

    public string? GetEnvironmentVariable(string name) => null;

    public DateTimeOffset? GetModifiedTime(string path) => ModifiedTime;
}
