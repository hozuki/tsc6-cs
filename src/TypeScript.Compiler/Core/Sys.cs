namespace TypeScript.Compiler;

// ported from: src/compiler/sys.ts:80-84
/// <summary>Identifies what changed about a watched file.</summary>
/// <remarks>
/// Public because it is part of the host contract that callers observe through
/// <see cref="FileWatcherCallback"/>; TS likewise exports it.
/// </remarks>
public enum FileWatcherEventKind
{
    Created,
    Changed,
    Deleted,
}

// ported from: src/compiler/sys.ts:95-99
/// <summary>
/// How often a polling-based watcher should poll. Unused by native OS watchers.
/// </summary>
internal enum PollingInterval
{
    High = 2000,
    Medium = 500,
    Low = 250,
}

// ported from: src/compiler/sys.ts:86-87
internal delegate void FileWatcherCallback(string fileName, FileWatcherEventKind eventKind, DateTimeOffset? modifiedTime = null);

internal delegate void DirectoryWatcherCallback(string fileName);

// ported from: src/compiler/sys.ts:101-104
/// <summary>Watch one file. The host supplies this; <see cref="SysWatch"/> builds policy on top of it.</summary>
internal delegate IFileWatcher HostWatchFile(
    string fileName,
    FileWatcherCallback callback,
    PollingInterval pollingInterval,
    IWatchOptions? options);

/// <summary>Watch one directory. The host supplies this; <see cref="SysWatch"/> builds policy on top of it.</summary>
internal delegate IFileWatcher HostWatchDirectory(
    string directoryName,
    DirectoryWatcherCallback callback,
    bool recursive,
    IWatchOptions? options);

// ported from: src/compiler/sys.ts:1453-1455
internal interface IFileWatcher
{
    void Close();
}

// ported from: src/compiler/sys.ts:1457-1459
internal interface IDirectoryWatcher : IFileWatcher
{
    int ReferenceCount { get; set; }
}

// ported from: src/compiler/sys.ts:901-960
/// <summary>Whether a path names a file or a directory.</summary>
internal enum FileSystemEntryKind
{
    File,
    Directory,
}

// ported from: src/compiler/utilities.ts:9781-9784
/// <summary>The files and subdirectories directly inside one directory.</summary>
internal interface IFileSystemEntries
{
    IReadOnlyList<string> Files { get; }

    IReadOnlyList<string> Directories { get; }
}

// ported from: src/compiler/sys.ts:107-112
/// <summary>A host that can report a file's modification time.</summary>
/// <remarks>
/// TS spells this as the structural type <c>{ getModifiedTime: NonNullable&lt;System["getModifiedTime"]&gt; }</c>
/// and passes whatever happens to satisfy it; C# needs the shape named.
/// </remarks>
internal interface IModifiedTimeHost
{
    DateTimeOffset? GetModifiedTime(string fileName);
}

// ported from: src/compiler/sys.ts:1395-1451
/// <summary>
/// The host interface the compiler runs against — file system, environment and terminal.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS marks roughly a third of these as optional and callers guard with <c>sys.foo?.()</c>.
/// C# has no optional interface members, so the same distinction is carried two ways: methods whose
/// absence is meaningful return <c>null</c> (mirroring <c>undefined</c>), and the rest get a default
/// interface implementation so that a host only overrides what it actually supports.
/// <para>
/// PORT-NOTE: TS's sibling type is named <c>System</c>, which in C# would collide with the root
/// namespace; the port uses <see cref="ISystem"/> instead.
/// </para>
/// </remarks>
internal interface ISystem : IModifiedTimeHost
{
    /// <summary>Command-line arguments, excluding the executable.</summary>
    IReadOnlyList<string> Args { get; }

    string NewLine { get; }

    bool UseCaseSensitiveFileNames { get; }

    void Write(string s);

    /// <param name="encoding">A <c>BufferEncoding</c> name; <c>null</c> means UTF-8.</param>
    string? ReadFile(string path, string? encoding = null);

    void WriteFile(string path, string data, bool writeByteOrderMark = false);

    string ResolvePath(string path);

    bool FileExists(string path);

    bool DirectoryExists(string path);

    void CreateDirectory(string path);

    string GetExecutingFilePath();

    string GetCurrentDirectory();

    IReadOnlyList<string> GetDirectories(string path);

    IReadOnlyList<string> ReadDirectory(
        string path,
        IReadOnlyList<string>? extensions = null,
        IReadOnlyList<string>? exclude = null,
        IReadOnlyList<string>? include = null,
        int? depth = null);

    void Exit(int exitCode = 0);

    // ---- Optional members. ----

    bool WriteOutputIsTty() => false;

    int GetWidthOfTerminal() => 0;

    long? GetFileSize(string path) => null;

    /// <returns><c>null</c> when the host cannot watch, mirroring TS's <c>sys.watchFile?.()</c>.</returns>
    IFileWatcher? WatchFile(
        string path,
        FileWatcherCallback callback,
        PollingInterval pollingInterval,
        IWatchOptions? options = null) => null;

    /// <returns><c>null</c> when the host cannot watch, mirroring TS's <c>sys.watchDirectory?.()</c>.</returns>
    IFileWatcher? WatchDirectory(
        string path,
        DirectoryWatcherCallback callback,
        bool recursive = false,
        IWatchOptions? options = null) => null;

    bool PreferNonRecursiveWatch => false;

    /// <summary>Reads a file's modified time, or <c>null</c> when the host cannot.</summary>
    /// <remarks>Takes part in <see cref="IModifiedTimeHost"/>: TS declares <c>getModifiedTime?</c> optional and
    /// callers guard for <c>undefined</c>, which C# expresses as a default implementation returning
    /// <c>null</c>.</remarks>
    new DateTimeOffset? GetModifiedTime(string path) => null;

    void SetModifiedTime(string path, DateTimeOffset time)
    {
    }

    void DeleteFile(string path)
    {
    }

    /// <summary>A good implementation is Node's <c>crypto.createHash</c>.</summary>
    string? CreateHash(string data) => null;

    /// <summary>Must be cryptographically secure — Node's <c>crypto.createHash("sha256")</c>.</summary>
    string? CreateSHA256Hash(string data) => null;

    long? GetMemoryUsage() => null;

    bool EnableCpuProfiler(string path, Action continuation) => false;

    bool DisableCpuProfiler(Action continuation) => false;

    bool CpuProfilingEnabled() => false;

    /// <summary>Resolves symlinks and normalises the result. Defaults to the path itself.</summary>
    string Realpath(string path) => path;

    string? GetEnvironmentVariable(string name);

    void TryEnableSourceMapsForHost()
    {
    }

    IFileSystemEntries? GetAccessibleFileSystemEntries(string path) => null;

    bool DebugMode => false;

    /// <returns>An opaque handle for <see cref="ClearTimeout"/>.</returns>
    object? SetTimeout(Action callback, int ms) => null;

    void ClearTimeout(object? timeoutId)
    {
    }

    void ClearScreen()
    {
    }

    void SetBlocking()
    {
    }

    string? Base64Decode(string input) => null;

    string? Base64Encode(string input) => null;

    /// <summary>Loads a module by name, for host-supplied plugins such as source map support.</summary>
    object? Require(string baseDir, string moduleName) => null;

    DateTimeOffset Now() => DateTimeOffset.Now;

    bool StoreSignatureInfo => false;
}
