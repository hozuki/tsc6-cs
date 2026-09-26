namespace TypeScript.Compiler.Tests;

/// <summary>
/// 写文件前补建目录（<c>utilities.ts</c> 的 <c>writeFileEnsuringDirectories</c>），以及 TS 通过猴子补丁装到
/// 宿主上的那份行为。
/// </summary>
public class FilesTests
{
    [Fact]
    public void EnsureDirectoriesExist_CreatesMissingParentsEndingWithTheLeaf()
    {
        var existing = new HashSet<string>(StringComparer.Ordinal) { "/" };
        var created = new List<string>();

        Files.EnsureDirectoriesExist(
            "/a/b/c",
            path =>
            {
                created.Add(path);
                existing.Add(path);
            },
            path => existing.Contains(path));

        Assert.Equal(["/a", "/a/b", "/a/b/c"], created);
    }

    [Fact]
    public void EnsureDirectoriesExist_IgnoresPathAtOrAboveTheRoot()
    {
        var created = new List<string>();

        Files.EnsureDirectoriesExist("/", path => created.Add(path), _ => false);
        Files.EnsureDirectoriesExist("", path => created.Add(path), _ => false);

        Assert.Empty(created);
    }

    [Fact]
    public void EnsureDirectoriesExist_LeavesExistingDirectoriesAlone()
    {
        var existing = new HashSet<string>(StringComparer.Ordinal) { "/", "/a" };
        var created = new List<string>();

        Files.EnsureDirectoriesExist("/a", path => created.Add(path), path => existing.Contains(path));

        Assert.Empty(created);
    }

    [Fact]
    public void WriteFileEnsuringDirectories_WritesDirectlyWhenTheDirectoryExists()
    {
        var written = new List<string>();
        var created = new List<string>();

        Files.WriteFileEnsuringDirectories(
            "/out/file.js",
            "data",
            writeByteOrderMark: false,
            (path, _, _) => written.Add(path),
            path => created.Add(path),
            _ => true);

        Assert.Equal(["/out/file.js"], written);
        Assert.Empty(created);
    }

    [Fact]
    public void WriteFileEnsuringDirectories_RetriesAfterCreatingTheDirectory()
    {
        var existing = new HashSet<string>(StringComparer.Ordinal) { "/" };
        var created = new List<string>();
        var written = new List<string>();

        Files.WriteFileEnsuringDirectories(
            "/out/deep/file.js",
            "data",
            writeByteOrderMark: false,
            (path, _, _) =>
            {
                if (!existing.Contains(Paths.GetDirectoryPath(path)))
                {
                    throw new IOException("no such directory");
                }

                written.Add(path);
            },
            path =>
            {
                created.Add(path);
                existing.Add(path);
            },
            path => existing.Contains(path));

        Assert.Equal(["/out", "/out/deep"], created);
        Assert.Equal(["/out/deep/file.js"], written);
    }

    [Fact]
    public void WriteFileEnsuringDirectories_GoesThroughTheSystemHost()
    {
        var system = new FakeSystem { RequireDirectoryBeforeWrite = true };

        Files.WriteFileEnsuringDirectories(system, "/out/file.js", "data");

        // The host refuses the first write, so the port creates the directory and writes again.
        Assert.Equal(["/out/file.js"], system.Written);
        Assert.Equal(["/out"], system.CreatedDirectories);
    }
}
