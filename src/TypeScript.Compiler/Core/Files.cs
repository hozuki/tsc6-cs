namespace TypeScript.Compiler;

// ported from: src/compiler/utilities.ts:6716-6745, src/compiler/sys.ts:1373-1390
/// <summary>
/// Writing a file after making sure the directories leading to it exist.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS installs this on the host by monkey-patching it
/// (<c>patchWriteFileEnsuringDirectory</c> replaces <c>sys.writeFile</c>). C# cannot replace a member on a live
/// object, so the port keeps the function and lets a call site ask for it explicitly — see
/// <see cref="WriteFileEnsuringDirectories(ISystem, string, string, bool)"/>. Recorded in
/// docs/architecture.md §13.
/// </remarks>
internal static class Files
{
    // ported from: src/compiler/utilities.ts:6716-6726
    /// <summary>Creates every directory leading to <paramref name="directoryPath"/> that does not exist yet.
    /// </summary>
    public static void EnsureDirectoriesExist(
        string directoryPath,
        Action<string> createDirectory,
        Func<string, bool> directoryExists)
    {
        if (directoryPath.Length > Paths.GetRootLength(directoryPath) && !directoryExists(directoryPath))
        {
            var parentDirectory = Paths.GetDirectoryPath(directoryPath);
            EnsureDirectoriesExist(parentDirectory, createDirectory, directoryExists);
            createDirectory(directoryPath);
        }
    }

    // ported from: src/compiler/utilities.ts:6728-6745
    /// <summary>
    /// Writes <paramref name="data"/> to <paramref name="path"/>, falling back to creating the missing
    /// directories (and writing again) when the write fails.
    /// </summary>
    /// <remarks>
    /// PERF: checking for directory existence is expensive. Instead, assume the directory exists and fall back to
    /// creating it if the file write fails.
    /// </remarks>
    public static void WriteFileEnsuringDirectories(
        string path,
        string data,
        bool writeByteOrderMark,
        Action<string, string, bool> writeFile,
        Action<string> createDirectory,
        Func<string, bool> directoryExists)
    {
        try
        {
            writeFile(path, data, writeByteOrderMark);
        }
        catch
        {
            EnsureDirectoriesExist(Paths.GetDirectoryPath(Paths.NormalizePath(path)), createDirectory, directoryExists);
            writeFile(path, data, writeByteOrderMark);
        }
    }

    // ported from: src/compiler/sys.ts:1373-1390
    /// <summary>
    /// Writes a file through <paramref name="system"/>, creating the directories leading to it when the host's
    /// write fails — the behaviour TS installs by patching.
    /// </summary>
    public static void WriteFileEnsuringDirectories(ISystem system, string path, string data, bool writeByteOrderMark = false)
        => WriteFileEnsuringDirectories(
            path,
            data,
            writeByteOrderMark,
            (filePath, fileData, bom) => system.WriteFile(filePath, fileData, bom),
            system.CreateDirectory,
            system.DirectoryExists);
}
