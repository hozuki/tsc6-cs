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
    private readonly HashSet<string> _directories = new(StringComparer.Ordinal);

    public IList<string> Args { get; set; } = [];

    public string NewLine { get; set; } = "\n";

    public bool UseCaseSensitiveFileNames { get; set; } = true;

    /// <summary>Backs <see cref="GetModifiedTime"/>; <c>null</c> models a host that cannot report one.</summary>
    public DateTimeOffset? ModifiedTime { get; set; }

    public List<string> Written { get; } = [];

    /// <summary>Directories <see cref="CreateDirectory"/> was asked for, in order.</summary>
    public List<string> CreatedDirectories { get; } = [];

    /// <summary>When set, <see cref="WriteFile"/> fails unless its directory already exists.</summary>
    public bool RequireDirectoryBeforeWrite { get; set; }

    /// <summary>Environment the host reports; a name that is absent reads as <c>null</c>.</summary>
    public Dictionary<string, string> EnvironmentVariables { get; } = new(StringComparer.Ordinal);

    public void Write(string s) => Written.Add(s);

    /// <summary>In-memory file contents. A path that is absent reads as a missing file.</summary>
    /// <remarks>PORT-NOTE: the fake answered <c>null</c> for every read until the M6-1 slice needed a host that can
    /// actually serve a program's root file; the dictionary is additive and every existing test keeps the empty one.</remarks>
    public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

    public string? ReadFile(string path, string? encoding = null) => Files.GetValueOrDefault(path);

    public void WriteFile(string path, string data, bool writeByteOrderMark = false)
    {
        if (RequireDirectoryBeforeWrite && !DirectoryExists(Paths.GetDirectoryPath(path)))
        {
            throw new IOException("no such directory");
        }

        Written.Add(path);
    }

    public string ResolvePath(string path) => path;

    public bool FileExists(string path) => Files.ContainsKey(path);

    public bool DirectoryExists(string path) => _directories.Contains(path) || Files.Keys.Any(file => file.StartsWith(path, StringComparison.Ordinal));

    public void CreateDirectory(string path)
    {
        CreatedDirectories.Add(path);
        _directories.Add(path);
    }

    public string GetExecutingFilePath() => string.Empty;

    public string GetCurrentDirectory() => string.Empty;

    public IList<string> GetDirectories(string path)
        => Files.Keys
            .Where(file => file.StartsWith(path, StringComparison.Ordinal))
            .Select(file => Paths.GetDirectoryPath(file))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    public IList<string> ReadDirectory(
        string path,
        IList<string>? extensions = null,
        IList<string>? exclude = null,
        IList<string>? include = null,
        int? depth = null) => [];

    public void Exit(int exitCode = 0)
    {
    }

    public string? GetEnvironmentVariable(string name) => EnvironmentVariables.GetValueOrDefault(name);

    public DateTimeOffset? GetModifiedTime(string path) => ModifiedTime;
}
