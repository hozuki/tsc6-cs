namespace TypeScript.Compiler.Tests;

// 对应 src/compiler/core.ts:1937-2485 的比较与字符串工具
public class CoreComparisonTests
{
    [Fact]
    public void CompareStringsCaseSensitive_OrdersByUtf16CodeUnit()
    {
        // JS 的 < 按 UTF-16 码元排序：'B'(0x42) < 'a'(0x61)。
        Assert.Equal(Comparison.LessThan, Core.CompareStringsCaseSensitive("B", "a"));
        Assert.Equal(Comparison.GreaterThan, Core.CompareStringsCaseSensitive("a", "B"));
        Assert.Equal(Comparison.EqualTo, Core.CompareStringsCaseSensitive("a", "a"));

        // 与大小写不敏感比较不同：后者把两边都转大写，"a" 对 "A" 相等。
        Assert.Equal(Comparison.EqualTo, Core.CompareStringsCaseInsensitive("a", "A"));
    }

    [Fact]
    public void CompareStringsCaseSensitive_NullIsLessThanAnyString()
    {
        // TS: a === undefined -> LessThan, b === undefined -> GreaterThan
        Assert.Equal(Comparison.LessThan, Core.CompareStringsCaseSensitive(null, "a"));
        Assert.Equal(Comparison.GreaterThan, Core.CompareStringsCaseSensitive("a", null));
        Assert.Equal(Comparison.EqualTo, Core.CompareStringsCaseSensitive(null, null));
    }

    [Fact]
    public void CompareStringsCaseInsensitiveEslintCompatible_LowercasesInsteadOfUppercasing()
    {
        // "_"(0x5F) 与 "a"(0x61)：大写形式 "_" 对 "A"，下划线仍然更小；
        // 这条测试固定住“用 toLowerCase”这一实现选择。
        Assert.Equal(Comparison.LessThan, Core.CompareStringsCaseInsensitiveEslintCompatible("_", "a"));
        Assert.Equal(Comparison.EqualTo, Core.CompareStringsCaseInsensitiveEslintCompatible("Abc", "aBC"));
    }

    [Fact]
    public void EquateStringsCaseInsensitive_UsesUppercaseFolding()
    {
        Assert.True(Core.EquateStringsCaseInsensitive("ABC", "abc"));
        Assert.False(Core.EquateStringsCaseInsensitive("abc", "abd"));
        Assert.True(Core.EquateStringsCaseInsensitive(null, null));
        Assert.False(Core.EquateStringsCaseInsensitive(null, "a"));

        // PORT-NOTE: TS 用“转大写”而非“转小写”正是为了这个用例 —— "ẞ"(U+1E9E) 的大写是自身，
        // 不会与 "ß" 折叠到一起。
        Assert.False(Core.EquateStringsCaseInsensitive("ẞ", "ß"));

        // JS 的 toUpperCase 对 "ß" 做 full case mapping（"SS"），所以 TS 里 "ss" == "ß" 成立。移植版通过
        // ICU 拿同一张表；ICU 不可用时回退到 .NET 的 invariant 映射，这一条就会变成 false（见 IcuCaseFolding）。
        Assert.Equal(IcuCaseFolding.IsAvailable, Core.EquateStringsCaseInsensitive("ss", "ß"));
    }

    [Fact]
    public void CompareComparableValues_HandlesNullOrdering()
    {
        Assert.Equal(Comparison.LessThan, Core.CompareComparableValues(1, 2));
        Assert.Equal(Comparison.GreaterThan, Core.CompareComparableValues(2, 1));
        Assert.Equal(Comparison.EqualTo, Core.CompareComparableValues(1, 1));
        Assert.Equal(Comparison.LessThan, Core.CompareComparableValues((int?)null, 1));
    }

    [Fact]
    public void CompareBooleans_TrueIsGreaterThanFalse()
    {
        Assert.Equal(Comparison.GreaterThan, Core.CompareBooleans(true, false));
        Assert.Equal(Comparison.LessThan, Core.CompareBooleans(false, true));
        Assert.Equal(Comparison.EqualTo, Core.CompareBooleans(true, true));
    }

    [Fact]
    public void GetStringComparer_SelectsCaseSensitivity()
    {
        Assert.Equal(Comparison.LessThan, Core.GetStringComparer(false)("B", "a"));
        Assert.Equal(Comparison.EqualTo, Core.GetStringComparer(true)("B", "b"));
    }

    [Fact]
    public void CompareStringsCaseSensitiveUI_TracksUILocale()
    {
        // UI 比较器是惰性创建并在 locale 变化时失效的；这里只验证它可用于排序且行为稳定。
        Core.SetUILocale("en-US");
        Assert.Equal(Comparison.EqualTo, Core.CompareStringsCaseSensitiveUI("a", "a"));
        Assert.Equal(Comparison.LessThan, Core.CompareStringsCaseSensitiveUI("a", "b"));

        Core.SetUILocale(null);
        Assert.Equal(Comparison.LessThan, Core.CompareStringsCaseSensitiveUI("a", "b"));
    }

    [Fact]
    public void GetSpellingSuggestion_FindsCloseMatch()
    {
        var candidates = new[] { "apple", "banana", "cherry" };

        Assert.Equal("apple", Core.GetSpellingSuggestion("aple", candidates, static c => c));
        Assert.Equal("banana", Core.GetSpellingSuggestion("bananna", candidates, static c => c));
        Assert.Null(Core.GetSpellingSuggestion("zzzzzzzz", candidates, static c => c));
    }

    [Fact]
    public void GetSpellingSuggestion_SkipsExactAndTooShortMatches()
    {
        var candidates = new[] { "apple", "ab" };

        // 完全相同的名字不作为建议返回
        Assert.Null(Core.GetSpellingSuggestion("apple", candidates, static c => c));
        // 长度 < 3 的候选只在大小写不同时才考虑："ab" 与 "AB" 大小写不同，应被考虑
        Assert.Equal("ab", Core.GetSpellingSuggestion("AB", candidates, static c => c));
        // "ab" 与方法名长度差超过阈值（name.Length=9 -> 上限 3，|9-2|=7）
        Assert.Null(Core.GetSpellingSuggestion("someThing", candidates, static c => c));
    }
}

// 对应 src/compiler/core.ts:1835-1888, 2247-2485 的字符串工具
public class CoreStringsTests
{
    [Fact]
    public void EndsWithAndStartsWith_RespectCaseSensitivity()
    {
        Assert.True(Core.EndsWith("foo.ts", ".ts"));
        Assert.False(Core.EndsWith("foo.ts", ".TS"));
        Assert.True(Core.EndsWith("foo.TS", ".ts", ignoreCase: true));
        Assert.True(Core.StartsWith("./foo", "./"));
        Assert.False(Core.StartsWith("./foo", "./FOO"));
        Assert.True(Core.StartsWith("./FOO", "./foo", ignoreCase: true));
    }

    [Fact]
    public void RemoveSuffixAndPrefix_MatchTsSemantics()
    {
        Assert.Equal("foo", Core.RemoveSuffix("foo.ts", ".ts"));
        Assert.Equal("foo.ts", Core.RemoveSuffix("foo.ts", ".js"));
        Assert.Equal("foo", Core.TryRemoveSuffix("foo.ts", ".ts"));
        Assert.Null(Core.TryRemoveSuffix("foo.ts", ".js"));
        Assert.Equal("foo", Core.RemovePrefix("./foo", "./"));
        Assert.Equal("foo", Core.TryRemovePrefix("./foo", "./"));
        Assert.Null(Core.TryRemovePrefix("./foo", "../"));
    }

    [Fact]
    public void TryRemovePrefix_UsesCanonicalFileName()
    {
        var lowerCase = Core.CreateGetCanonicalFileName(useCaseSensitiveFileNames: false);
        Assert.Equal("FOO", Core.TryRemovePrefix("fooFOO", "foo", lowerCase));
    }

    [Fact]
    public void RemoveMinAndVersionNumbers_StripsMinAndVersionSuffixes()
    {
        Assert.Equal("jquery", Core.RemoveMinAndVersionNumbers("jquery-min.4.2.3"));
        Assert.Equal("jquery", Core.RemoveMinAndVersionNumbers("jquery-1.2.3"));
        Assert.Equal("jquery", Core.RemoveMinAndVersionNumbers("jquery"));
        // 末尾不是 "min"/版本号模式时原样返回：TS 是自尾向头扫描，遇到 's' 立即停止
        Assert.Equal("jquery.min.js", Core.RemoveMinAndVersionNumbers("jquery.min.js"));
        Assert.Equal("a.b", Core.RemoveMinAndVersionNumbers("a.b"));
    }

    [Fact]
    public void ToFileNameLowerCase_LeavesSpecialCharactersAlone()
    {
        Assert.Equal("abc", Core.ToFileNameLowerCase("ABC"));
        // \u0130（带点的 I）有独立的“小写后仍可大写”的特性，必须保持原样
        Assert.Equal("a\u0130b", Core.ToFileNameLowerCase("A\u0130B"));
        // 普通字符仍然折叠
        Assert.Equal("a/z", Core.ToFileNameLowerCase("A/Z"));
    }

    [Fact]
    public void CreateGetCanonicalFileName_IdentityWhenCaseSensitive()
    {
        var sensitive = Core.CreateGetCanonicalFileName(useCaseSensitiveFileNames: true);
        Assert.Equal("ABC", sensitive("ABC"));

        var insensitive = Core.CreateGetCanonicalFileName(useCaseSensitiveFileNames: false);
        Assert.Equal("abc", insensitive("ABC"));
    }

    [Fact]
    public void Pattern_MatchesPrefixAndSuffix()
    {
        var pattern = new Core.Pattern("foo", "baz");

        Assert.Equal("foo*baz", Core.PatternText(pattern));
        Assert.True(Core.IsPatternMatch(pattern, "foobarbaz"));
        Assert.True(Core.IsPatternMatch(pattern, "foobaz"));
        Assert.False(Core.IsPatternMatch(pattern, "foobar"));
        Assert.False(Core.IsPatternMatch(pattern, "barbaz"));
        Assert.Equal("bar", Core.MatchedText(pattern, "foobarbaz"));
    }

    [Fact]
    public void FindBestPatternMatch_PrefersLongestPrefix()
    {
        var prefixes = new[] { "", "src/", "other/" };

        var best = Core.FindBestPatternMatch(prefixes, static p => new Core.Pattern(p, ".ts"), "src/foo.ts");
        Assert.Equal("src/", best);
    }

    [Fact]
    public void AndOrNot_ComposePredicates()
    {
        Func<int, bool> isPositive = static x => x > 0;
        Func<int, bool> isEven = static x => x % 2 == 0;

        Assert.True(Core.And(isPositive, isEven)(4));
        Assert.False(Core.And(isPositive, isEven)(3));
        Assert.True(Core.Or(isPositive, isEven)(3));
        Assert.False(Core.Or(isPositive, isEven)(-3));
        Assert.True(Core.Not(isPositive)(-1));
    }
}
