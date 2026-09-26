namespace TypeScript.Compiler.Tests;

// 对应 src/compiler/corePublic.ts:25-47
public class ComparisonTests
{
    [Fact]
    public void Comparison_Values_MatchTs()
    {
        Assert.Equal(-1, (int)Comparison.LessThan);
        Assert.Equal(0, (int)Comparison.EqualTo);
        Assert.Equal(1, (int)Comparison.GreaterThan);
    }

    [Fact]
    public void EqualityComparerAdapter_UsesProvidedEquals()
    {
        var adapter = new EqualityComparerAdapter<string>((a, b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase), s => s.ToUpperInvariant().GetHashCode());

        Assert.True(adapter.Equals("abc", "ABC"));
        Assert.False(adapter.Equals("abc", "abd"));
        Assert.Equal(adapter.GetHashCode("abc"), adapter.GetHashCode("ABC"));
    }

    [Fact]
    public void EqualityComparerAdapter_NullHandling()
    {
        var adapter = new EqualityComparerAdapter<string?>(static (a, b) => a == b);

        Assert.True(adapter.Equals(null, null));
        Assert.False(adapter.Equals(null, "x"));
        Assert.False(adapter.Equals("x", null));
    }

    [Fact]
    public void EqualityComparerAdapter_WorksWithBclDictionary()
    {
        // 大小写不敏感的字典：等价于 TS 中 createMap 传入自定义 EqualityComparer。
        var comparer = new EqualityComparerAdapter<string>(
            static (a, b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase),
            static s => s.ToUpperInvariant().GetHashCode());

        var map = new Dictionary<string, int>(comparer) { ["Foo"] = 1 };

        Assert.True(map.ContainsKey("FOO"));
        Assert.Equal(1, map["foo"]);
    }
}
