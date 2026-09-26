namespace TypeScript.Compiler.Tests;

// 对应 src/compiler/path.ts:1-1115
public class PathsTests
{
    [Theory]
    [InlineData("a", 0)]
    [InlineData("/", 1)]
    [InlineData("/path", 1)]
    [InlineData("c:", 2)]
    [InlineData("c:/", 3)]
    [InlineData("c:\\", 3)]
    [InlineData("c:d", 0)]
    [InlineData("//server/share", 9)]
    [InlineData("file:///path", 8)]
    [InlineData("file://server", 13)]
    [InlineData("http://server", 13)]
    [InlineData("http://server/path", 14)]
    public void GetRootLength_MatchesTsTable(string path, int expected)
    {
        Assert.Equal(expected, Paths.GetRootLength(path));
    }

    [Fact]
    public void PathClassification_MatchesTsSemantics()
    {
        Assert.True(Paths.IsUrl("http://x/y"));
        Assert.True(Paths.IsUrl("file:///c:/x"));
        Assert.False(Paths.IsUrl("/x/y"));

        Assert.True(Paths.IsRootedDiskPath("/x"));
        Assert.True(Paths.IsRootedDiskPath("c:/x"));
        Assert.False(Paths.IsRootedDiskPath("x/y"));

        Assert.True(Paths.IsDiskPathRoot("/"));
        Assert.True(Paths.IsDiskPathRoot("c:/"));
        Assert.False(Paths.IsDiskPathRoot("/x"));

        Assert.True(Paths.PathIsAbsolute("/x"));
        Assert.False(Paths.PathIsAbsolute("./x"));
        Assert.True(Paths.PathIsRelative("./x"));
        Assert.True(Paths.PathIsRelative("../x"));
        Assert.False(Paths.PathIsRelative("x/y"));

        Assert.True(Paths.PathIsBareSpecifier("some-package"));
        Assert.False(Paths.PathIsBareSpecifier("/x"));
        Assert.False(Paths.PathIsBareSpecifier("./x"));
    }

    [Fact]
    public void ExtensionHelpers_MatchTsSemantics()
    {
        Assert.True(Paths.HasExtension("file.ext"));
        Assert.False(Paths.HasExtension("file"));

        Assert.True(Paths.FileExtensionIs("file.ts", ".ts"));
        Assert.False(Paths.FileExtensionIs("file.ts", ".tsx"));
        // 路径必须长于扩展名
        Assert.False(Paths.FileExtensionIs(".ts", ".ts"));

        Assert.True(Paths.FileExtensionIsOneOf("file.ts", [".js", ".ts"]));
        Assert.False(Paths.FileExtensionIsOneOf("file.ts", [".js", ".tsx"]));

        Assert.Equal(".ts", Paths.GetAnyExtensionFromPath("/path/to/file.ts"));
        Assert.Equal(".ts", Paths.GetAnyExtensionFromPath("/path/to/file.ts/"));
        Assert.Equal("", Paths.GetAnyExtensionFromPath("/path/to/file"));
        Assert.Equal("", Paths.GetAnyExtensionFromPath("/path/to.ext/file"));

        Assert.Equal(".ts", Paths.GetAnyExtensionFromPath("/path/to/file.ts", ".ts", ignoreCase: false));
        Assert.Equal("", Paths.GetAnyExtensionFromPath("/path/to/file.js", ".ts", ignoreCase: false));
        Assert.Equal(".TS", Paths.GetAnyExtensionFromPath("/path/to/file.TS", ".ts", ignoreCase: true));
        Assert.Equal("", Paths.GetAnyExtensionFromPath("/path/to/file.TS", ".ts", ignoreCase: false));
    }

    [Theory]
    [InlineData("/path/to/file.ext", "/path/to")]
    [InlineData("/path/to/", "/path")]
    [InlineData("/", "/")]
    [InlineData("c:/path/to/file.ext", "c:/path/to")]
    [InlineData("c:/path/to/", "c:/path")]
    [InlineData("c:/", "c:/")]
    [InlineData("c:", "c:")]
    [InlineData("http://x.org/path/to/file.ext", "http://x.org/path/to")]
    [InlineData("http://x.org/path/to", "http://x.org/path")]
    [InlineData("http://x.org/", "http://x.org/")]
    [InlineData("http://x.org", "http://x.org")]
    public void GetDirectoryPath_MatchesTsTable(string path, string expected)
    {
        Assert.Equal(expected, Paths.GetDirectoryPath(path));
    }

    [Theory]
    [InlineData("/path/to/file.ext", "file.ext")]
    [InlineData("/path/to/", "to")]
    [InlineData("/", "")]
    [InlineData("c:/path/to/file.ext", "file.ext")]
    [InlineData("c:/", "")]
    [InlineData("c:", "")]
    [InlineData("http://x.org/path/to/file.ext", "file.ext")]
    [InlineData("http://x.org", "")]
    [InlineData("file:///path/to/file.ext", "file.ext")]
    [InlineData("file://", "")]
    public void GetBaseFileName_MatchesTsTable(string path, string expected)
    {
        Assert.Equal(expected, Paths.GetBaseFileName(path));
    }

    [Fact]
    public void GetBaseFileName_StripsProvidedExtensions()
    {
        Assert.Equal("file", Paths.GetBaseFileName("/path/to/file.ext", ".ext", ignoreCase: true));
        Assert.Equal("file.js", Paths.GetBaseFileName("/path/to/file.js", ".ext", ignoreCase: true));
        Assert.Equal("file", Paths.GetBaseFileName("/path/to/file.js", [".ext", ".js"], ignoreCase: true));
        Assert.Equal("file.ext", Paths.GetBaseFileName("/path/to/file.ext", ".EXT", ignoreCase: false));
    }

    [Fact]
    public void CombinePaths_MatchesTsTable()
    {
        Assert.Equal("path/to/file.ext", Paths.CombinePaths("path", "to", "file.ext"));
        Assert.Equal("path/dir/../to/file.ext", Paths.CombinePaths("path", "dir", "..", "to", "file.ext"));
        Assert.Equal("/path/to/file.ext", Paths.CombinePaths("/path", "to", "file.ext"));
        // 绝对路径替换之前的部分
        Assert.Equal("/to/file.ext", Paths.CombinePaths("/path", "/to", "file.ext"));
        Assert.Equal("c:/path/to/file.ext", Paths.CombinePaths("c:/path", "to", "file.ext"));
        Assert.Equal("c:/to/file.ext", Paths.CombinePaths("c:/path", "c:/to", "file.ext"));
        // 反斜杠会被规范化为正斜杠
        Assert.Equal("a/b/c", Paths.CombinePaths("a\\b", "c"));
        // 空/undefined 分段被忽略
        Assert.Equal("a/b", Paths.CombinePaths("a", null, "b"));
    }

    [Fact]
    public void ResolvePath_ResolvesDotSegments()
    {
        Assert.Equal("/path/to/file.ext", Paths.ResolvePath("/path", "to", "file.ext"));
        Assert.Equal("/path/to/file.ext/", Paths.ResolvePath("/path", "to", "file.ext/"));
        Assert.Equal("/path/to/file.ext", Paths.ResolvePath("/path", "dir", "..", "to", "file.ext"));
        Assert.Equal("/path/file", Paths.ResolvePath("/path/to/../file"));
    }

    [Fact]
    public void NormalizeSlashes_ConvertsBackslashes()
    {
        Assert.Equal("a/b", Paths.NormalizeSlashes("a\\b"));
        Assert.Equal("a/b", Paths.NormalizeSlashes("a/b"));
    }

    [Fact]
    public void NormalizePath_SimplifiesDotSegments()
    {
        Assert.Equal("/path/file", Paths.NormalizePath("/path/to/../file"));
        Assert.Equal("path/file", Paths.NormalizePath("./path/file"));
        Assert.Equal("/path/to/file", Paths.NormalizePath("/path/to/file"));
        // 尾部分隔符保留
        Assert.Equal("/path/to/", Paths.NormalizePath("/path/to/"));
    }

    [Fact]
    public void GetNormalizedAbsolutePath_PrefixesCurrentDirectory()
    {
        Assert.Equal("/cwd/file", Paths.GetNormalizedAbsolutePath("file", "/cwd"));
        Assert.Equal("/abs/file", Paths.GetNormalizedAbsolutePath("/abs/file", "/cwd"));
        Assert.Equal("/cwd/file", Paths.GetNormalizedAbsolutePath("./file", "/cwd"));
    }

    [Fact]
    public void GetNormalizedAbsolutePathWithoutRoot_DropsRoot()
    {
        Assert.Equal("path/file", Paths.GetNormalizedAbsolutePathWithoutRoot("/path/file", null));
        Assert.Equal("path/file", Paths.GetNormalizedAbsolutePathWithoutRoot("path/file", null));
    }

    [Fact]
    public void PathComponents_RoundTrip()
    {
        Assert.Equal(["/", "path", "to", "file.ext"], Paths.GetPathComponents("/path/to/file.ext"));
        Assert.Equal(["/", "path", "to"], Paths.GetPathComponents("/path/to/"));
        Assert.Equal(["/"], Paths.GetPathComponents("/"));
        Assert.Equal(["c:/", "path", "to"], Paths.GetPathComponents("c:/path/to/"));
        Assert.Equal([""], Paths.GetPathComponents(""));

        Assert.Equal("/path/to/file.ext", Paths.GetPathFromPathComponents(["/", "path", "to", "file.ext"]));
        Assert.Equal("", Paths.GetPathFromPathComponents([]));
    }

    [Fact]
    public void ReducePathComponents_NavigatesDotSegments()
    {
        Assert.Equal(["/", "to"], Paths.ReducePathComponents(["/", "path", "..", "to"]));
        Assert.Equal(["/"], Paths.ReducePathComponents(["/", "path", ".."]));
        // 根之上的 ".." 被丢弃（reduced 只剩根，且根非空 -> continue）
        Assert.Equal(["/"], Paths.ReducePathComponents(["/", ".."]));
        Assert.Equal([], Paths.ReducePathComponents([]));
    }

    [Fact]
    public void TrailingSeparator_AddAndRemove()
    {
        Assert.Equal("/path/to", Paths.RemoveTrailingDirectorySeparator("/path/to/"));
        Assert.Equal("/path/to", Paths.RemoveTrailingDirectorySeparator("/path/to"));
        Assert.Equal("/path/to/", Paths.EnsureTrailingDirectorySeparator("/path/to"));
        Assert.Equal("/path/to/", Paths.EnsureTrailingDirectorySeparator("/path/to/"));
        Assert.True(Paths.HasTrailingDirectorySeparator("/path/"));
        Assert.True(Paths.HasTrailingDirectorySeparator("a\\"));
        Assert.False(Paths.HasTrailingDirectorySeparator("/path"));
    }

    [Fact]
    public void EnsurePathIsNonModuleName_PrefixesDot()
    {
        Assert.Equal("/path/to/file.ext", Paths.EnsurePathIsNonModuleName("/path/to/file.ext"));
        Assert.Equal("./path/to/file.ext", Paths.EnsurePathIsNonModuleName("./path/to/file.ext"));
        Assert.Equal("../path/to/file.ext", Paths.EnsurePathIsNonModuleName("../path/to/file.ext"));
        Assert.Equal("./path/to/file.ext", Paths.EnsurePathIsNonModuleName("path/to/file.ext"));
    }

    [Fact]
    public void ChangeExtension_MatchesTsExamples()
    {
        Assert.Equal("/path/to/file.js", Paths.ChangeAnyExtension("/path/to/file.ext", ".js"));
        Assert.Equal("/path/to/file.js", Paths.ChangeAnyExtension("/path/to/file.ext", ".js", ".ext", ignoreCase: false));
        Assert.Equal("/path/to/file.ext", Paths.ChangeAnyExtension("/path/to/file.ext", ".js", ".ts", ignoreCase: false));
        Assert.Equal("/path/to/file.js", Paths.ChangeAnyExtension("/path/to/file.ext", ".js", [".ext", ".ts"], ignoreCase: false));

        // 前缀 "." 会被自动补上
        Assert.Equal("/path/to/file.js", Paths.ChangeAnyExtension("/path/to/file.ext", "js"));

        // changeFullExtension 从 .d 开始替换，changeAnyExtension 只替换最后一个扩展名
        Assert.Equal("file.d.js", Paths.ChangeAnyExtension("file.d.ts", ".js"));
        Assert.Equal("file.js", Paths.ChangeFullExtension("file.d.ts", ".js"));
        Assert.Equal("file.js", Paths.ChangeFullExtension("file.ts", ".js"));
    }

    [Fact]
    public void GetDeclarationFileExtension_RecognizesDeclarationExtensions()
    {
        Assert.Equal(".d.ts", Paths.GetDeclarationFileExtension("a.d.ts"));
        Assert.Equal(".d.mts", Paths.GetDeclarationFileExtension("a.d.mts"));
        Assert.Equal(".d.cts", Paths.GetDeclarationFileExtension("a.d.cts"));
        Assert.Null(Paths.GetDeclarationFileExtension("a.ts"));
        Assert.Null(Paths.GetDeclarationFileExtension("a.js"));
    }

    [Fact]
    public void ComparePaths_ComparesRootsCaseInsensitively()
    {
        Assert.Equal(Comparison.EqualTo, Paths.ComparePaths("/A/b", "/A/b", ignoreCase: false));
        Assert.Equal(Comparison.LessThan, Paths.ComparePaths("/A/b", "/A/c", ignoreCase: false));
        // 根分段始终大小写不敏感
        Assert.Equal(Comparison.EqualTo, Paths.ComparePaths("/A/b", "/a/b", ignoreCase: true));
        Assert.Equal(Comparison.LessThan, Paths.ComparePathsCaseSensitive("/A/b", "/A/c"));
        Assert.Equal(Comparison.EqualTo, Paths.ComparePathsCaseInsensitive("/A/b", "/a/b"));
    }

    [Fact]
    public void ComparePaths_ReducesRelativeSegments()
    {
        // 含相对分段时走逐段比较，"" 与 "a/.." 归约后相同
        Assert.Equal(Comparison.EqualTo, Paths.ComparePaths("/a/b/..", "/a", ignoreCase: false));
        Assert.Equal(Comparison.EqualTo, Paths.ComparePaths("/a/b/../c", "/a/c", ignoreCase: false));
    }

    [Fact]
    public void ContainsPath_MatchesTsSemantics()
    {
        Assert.True(Paths.ContainsPath("/a/b", "/a/b/c"));
        Assert.True(Paths.ContainsPath("/a/b", "/a/b"));
        Assert.False(Paths.ContainsPath("/a/b", "/a/c"));
        Assert.False(Paths.ContainsPath("/a/b/c", "/a/b"));
        // 根分段大小写不敏感，"a" 分段大小写敏感
        Assert.True(Paths.ContainsPath("/A", "/a/b", ignoreCase: true));
        Assert.False(Paths.ContainsPath("/A", "/a/b", ignoreCase: false));
    }

    [Fact]
    public void StartsWithDirectory_UsesCanonicalNames()
    {
        var identity = Core.CreateGetCanonicalFileName(useCaseSensitiveFileNames: true);
        Assert.True(Paths.StartsWithDirectory("/a/b/c.ts", "/a", identity));
        Assert.True(Paths.StartsWithDirectory("/a/b/c.ts", "/a/b", identity));
        Assert.False(Paths.StartsWithDirectory("/a/b/c.ts", "/b", identity));

        var insensitive = Core.CreateGetCanonicalFileName(useCaseSensitiveFileNames: false);
        Assert.True(Paths.StartsWithDirectory("/A/b/c.ts", "/a", insensitive));
    }

    [Fact]
    public void ForEachAncestorDirectory_WalksUpToRoot()
    {
        var visited = new List<string>();
        var result = Paths.ForEachAncestorDirectory("/a/b/c", dir =>
        {
            visited.Add(dir);
            return dir == "/a" ? "found" : null;
        });

        Assert.Equal("found", result);
        // 回调在 "/a" 返回后即停止，因此 "/" 不会被访问
        Assert.Equal(["/a/b/c", "/a/b", "/a"], visited);

        // 走到根仍未命中则返回 null，且不会死循环
        Assert.Null(Paths.ForEachAncestorDirectory<string>("/a/b", static _ => null));
    }

    [Fact]
    public void IsNodeModulesDirectory_MatchesSuffix()
    {
        Assert.True(Paths.IsNodeModulesDirectory("/a/node_modules"));
        Assert.False(Paths.IsNodeModulesDirectory("/a/node_modules/"));
    }

    [Fact]
    public void ToPath_AppliesCanonicalization()
    {
        var insensitive = Core.CreateGetCanonicalFileName(useCaseSensitiveFileNames: false);
        Assert.Equal("/cwd/file", Paths.ToPath("file", "/cwd", insensitive));
        Assert.Equal("/cwd/file", Paths.ToPath("FILE", "/cwd", insensitive));
    }

    [Fact]
    public void GetRelativePathFromDirectory_MatchesTsSemantics()
    {
        Assert.Equal("c/d.ts", Paths.GetRelativePathFromDirectory("/a/b", "/a/b/c/d.ts", ignoreCase: false));
        Assert.Equal("../c/d.ts", Paths.GetRelativePathFromDirectory("/a/b/x", "/a/b/c/d.ts", ignoreCase: false));
        Assert.Equal("b/c", Paths.GetRelativePathFromDirectory("/a", "/a/b/c", ignoreCase: false));

        var identity = Core.CreateGetCanonicalFileName(useCaseSensitiveFileNames: true);
        Assert.Equal("c/d.ts", Paths.GetRelativePathFromDirectory("/a/b", "/a/b/c/d.ts", identity));
    }

    [Fact]
    public void GetRelativePathToDirectoryOrUrl_CanPrefixFileUrl()
    {
        Assert.Equal("b/c.ts", Paths.GetRelativePathToDirectoryOrUrl("/a", "b/c.ts", "/a", Core.IdentityFileName, isAbsolutePathAnUrl: false));
        // 同一根下结果为相对路径，首分段不是磁盘根 -> 不加 file:// 前缀
        Assert.Equal("b.ts", Paths.GetRelativePathToDirectoryOrUrl("/a", "/a/b.ts", "/a", Core.IdentityFileName, isAbsolutePathAnUrl: true));
        // 跨根时首分段是磁盘根，isAbsolutePathAnUrl 为真时加上 file:/// 前缀
        Assert.Equal("file:///c:/b.ts", Paths.GetRelativePathToDirectoryOrUrl("/a", "c:/b.ts", "/a", Core.IdentityFileName, isAbsolutePathAnUrl: true));
    }

    [Fact]
    public void GetPathComponentsRelativeTo_ReturnsTrailingComponents()
    {
        // 通过 getRelativePathFromDirectory 的公开行为间接验证（它是唯一的调用方）
        Assert.Equal("d.ts", Paths.GetRelativePathFromDirectory("/a/b/c", "/a/b/c/d.ts", ignoreCase: false));
    }
}
