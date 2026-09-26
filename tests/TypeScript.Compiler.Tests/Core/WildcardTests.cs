namespace TypeScript.Compiler.Tests;

/// <summary>
/// wildcard/exclude 规则：把 include/exclude 规格串编译成正则，以及建立在其上的排除匹配。
/// </summary>
/// <remarks>
/// 期望值直接取自 TS 6.0.3 的 <c>lib/typescript.js</c>：每组输入都对其调用
/// <c>ts.getRegularExpressionForWildcard(specs, "/project", usage)</c> / <c>ts.matchesExclude(...)</c>，
/// 输出原样抄成常量。所以这里比对的是"与上游逐字符相同"，而不是"看起来合理"。
/// <para>
/// 这些函数在 TS 中位于 <c>utilities.ts</c> / <c>commandLineParser.ts</c>（移植的 Layer 5/8），
/// 提前到 <c>Core</c> 是因为 <c>sys.ts</c> 的目录 watcher 需要它们，见 <c>Wildcard</c> 的类注释。
/// </para>
/// </remarks>
public class WildcardTests
{
    private const string ExcludedPackages = "(?!(?:node_modules|bower_components|jspm_packages)(?:/|$))";

    // ts.getRegularExpressionForWildcard(["**/*.ts"], "/project", "files")
    private const string ExpectedFilesDoubleStarTs =
        """^(?:(?:/project(?:/(?!(?:node_modules|bower_components|jspm_packages)(?:/|$))[^/.][^/]*)*?/(?!(?:node_modules|bower_components|jspm_packages)(?:/|$))(?:[^./](?:[^./]|(?:\.(?!min\.js$))?)*)?\.ts))$""";

    // ts.getRegularExpressionForWildcard(["foo"], "/project", "files")
    private const string ExpectedFilesImplicitGlobFoo =
        """^(?:(?:/project/foo(?:/(?!(?:node_modules|bower_components|jspm_packages)(?:/|$))[^/.][^/]*)*?/(?!(?:node_modules|bower_components|jspm_packages)(?:/|$))(?:[^./](?:[^./]|(?:\.(?!min\.js$))?)*)?))$""";

    // ts.getRegularExpressionForWildcard(["*.ts"], "/project", "directories")
    private const string ExpectedDirectoriesTs =
        """^(?:(?:(?:(?:/project(?:/(?!(?:node_modules|bower_components|jspm_packages)(?:/|$))(?:[^./][^/]*)?\.ts)?)?)?))$""";

    [Fact]
    public void RegularExpressionForWildcard_ReproducesUpstream()
    {
        Assert.Equal(ExpectedFilesDoubleStarTs, Wildcard.GetRegularExpressionForWildcard(["**/*.ts"], "/project", Wildcard.Usage.Files));
        Assert.Equal(ExpectedFilesImplicitGlobFoo, Wildcard.GetRegularExpressionForWildcard(["foo"], "/project", Wildcard.Usage.Files));
        Assert.Equal(ExpectedDirectoriesTs, Wildcard.GetRegularExpressionForWildcard(["*.ts"], "/project", Wildcard.Usage.Directories));

        // An "exclude" pattern may end in ** (an include must not: it would match nothing).
        Assert.Equal(
            "^(?:(?:/project/src(?:/.+?)?))(?:$|/)",
            Wildcard.GetRegularExpressionForWildcard(["src/**"], "/project", Wildcard.Usage.Exclude));
        Assert.Equal(
            "^(?:(?:/project(?:/.+?)?/[^/]*\\.ts))(?:$|/)",
            Wildcard.GetRegularExpressionForWildcard(["**/*.ts"], "/project", Wildcard.Usage.Exclude));
        Assert.Equal(
            "^(?:(?:/project(?:/.+?)?/node_modules(?:/.+?)?))(?:$|/)",
            Wildcard.GetRegularExpressionForWildcard(["**/node_modules/**"], "/project", Wildcard.Usage.Exclude));
    }

    [Fact]
    public void RegularExpressionForWildcard_DropsSpecsThatMatchNothing()
    {
        foreach (var usage in new[] { Wildcard.Usage.Files, Wildcard.Usage.Directories })
        {
            // TS returns undefined when the last component is ** and the spec is not an exclude.
            Assert.Null(Wildcard.GetRegularExpressionForWildcard(["**"], "/project", usage));
            Assert.Null(Wildcard.GetRegularExpressionForWildcard(["src/**"], "/project", usage));
        }

        Assert.Null(Wildcard.GetRegularExpressionForWildcard(null, "/project", Wildcard.Usage.Files));
        Assert.Null(Wildcard.GetRegularExpressionForWildcard([], "/project", Wildcard.Usage.Files));
    }

    [Fact]
    public void RegularExpressionForWildcard_SkipsEmptySpecs()
    {
        // TS: flatMap(specs, spec => spec && …) — the empty spec contributes nothing.
        Assert.Equal(
            Wildcard.GetRegularExpressionForWildcard(["*.ts"], "/project", Wildcard.Usage.Files),
            Wildcard.GetRegularExpressionForWildcard(["", "*.ts"], "/project", Wildcard.Usage.Files));
    }

    [Fact]
    public void ImplicitGlob_RequiresAnExtensionlessComponentWithoutGlobCharacters()
    {
        Assert.True(Wildcard.IsImplicitGlob("foo"));
        Assert.False(Wildcard.IsImplicitGlob("foo.ts"));
        Assert.False(Wildcard.IsImplicitGlob("*"));
        Assert.False(Wildcard.IsImplicitGlob("a?b"));
        Assert.False(Wildcard.IsImplicitGlob("a.b"));
    }

    /// <summary>
    /// ts.matchesExclude(pathToCheck, specs, useCaseSensitiveFileNames, currentDirectory).
    /// </summary>
    [Theory]
    // A directory spec also excludes everything under it.
    [InlineData("/project/src/a.ts", "src", true, true)]
    [InlineData("/project/dist/a.js", "dist", true, true)]
    [InlineData("/project/src", "src", true, true)]
    // The trailing "?(?:$|/)" is what makes "src" match the directory itself and its contents.
    [InlineData("/project/a/b/c.ts", "**/b", true, true)]
    [InlineData("/project/src/*.ts", "src/*.ts", true, true)]
    [InlineData("/project/src/a.ts", "src/**/*.ts", true, true)]
    [InlineData("/project/src/a.ts", "./src", true, true)]
    [InlineData("/project/node_modules/a.ts", "**/node_modules", true, true)]
    // Exclude patterns are matched against the whole path, not a suffix of it.
    [InlineData("/project/src/a.ts", "*.ts", true, false)]
    [InlineData("/project/src/a.ts", "**/*.js", true, false)]
    [InlineData("/project/src/a.ts", "other", true, false)]
    [InlineData("/project/src/a.ts", null, true, false)]
    // Case sensitivity follows the file system's.
    [InlineData("/project/SRC/a.ts", "src", false, true)]
    [InlineData("/project/SRC/a.ts", "src", true, false)]
    public void MatchesExclude_ReproducesUpstream(string pathToCheck, string? spec, bool useCaseSensitiveFileNames, bool expected)
    {
        IReadOnlyList<string>? specs = spec is null ? null : [spec];
        Assert.Equal(expected, Wildcard.MatchesExclude(pathToCheck, specs, useCaseSensitiveFileNames, "/project"));
    }

    [Fact]
    public void MatchesExclude_IgnoresSpecsWithDotDotAfterARecursiveWildcard()
    {
        // ts.matchesExclude("/project/a/../b/c.ts", ["**/../b"], true, "/project") === false — the spec is
        // filtered out by invalidDotDotAfterRecursiveWildcard before it ever becomes a pattern.
        Assert.False(Wildcard.MatchesExclude("/project/a/../b/c.ts", ["**/../b"], true, "/project"));
    }

    [Fact]
    public void InvalidDotDotAfterRecursiveWildcard_OnlyLooksAfterTheWildcard()
    {
        Assert.True(Wildcard.InvalidDotDotAfterRecursiveWildcard("**/../b"));
        Assert.True(Wildcard.InvalidDotDotAfterRecursiveWildcard("a/**/b/../c"));
        // The trailing "/.." counts as "after the wildcard" too.
        Assert.True(Wildcard.InvalidDotDotAfterRecursiveWildcard("**/.."));

        // No recursive wildcard at all: there is nothing to be after, so the spec survives (even a stray "..").
        Assert.False(Wildcard.InvalidDotDotAfterRecursiveWildcard("foo/../bar"));
        Assert.False(Wildcard.InvalidDotDotAfterRecursiveWildcard("a/b/../c"));
        Assert.False(Wildcard.InvalidDotDotAfterRecursiveWildcard("**/foo"));
    }

    [Fact]
    public void RegexFromPattern_IsCaseInsensitiveOnlyWhenTheFileSystemIs()
    {
        var sensitive = Wildcard.GetRegexFromPattern("^a$", useCaseSensitiveFileNames: true);
        var insensitive = Wildcard.GetRegexFromPattern("^a$", useCaseSensitiveFileNames: false);

        Assert.Matches(sensitive, "a");
        Assert.DoesNotMatch(sensitive, "A");
        Assert.Matches(insensitive, "A");
    }

    [Fact]
    public void EscapedReservedCharacters_KeepAsciiOnlyWordSemantics()
    {
        // JS's \w is ASCII-only; the port must not treat é as a word character and skip escaping it.
        var pattern = Wildcard.GetSubPatternFromSpec("caf\u00e9", "/project", Wildcard.Usage.Files);

        Assert.NotNull(pattern);
        Assert.Contains(@"\é", pattern, StringComparison.Ordinal);
        Assert.Contains(ExcludedPackages, pattern, StringComparison.Ordinal);
    }
}
