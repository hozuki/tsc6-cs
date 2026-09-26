namespace TypeScript.Compiler.Tests;

/// <summary>
/// 大小写折叠：TS 写的是 JS 的 <c>toUpperCase</c>/<c>toLowerCase</c>，而 V8 用的是 Unicode 的 full case
/// mapping（<c>"ß".toUpperCase() === "SS"</c>）。移植版通过 ICU 拿同一张表；ICU 不可用时回退到 .NET 的
/// invariant 映射并在 stderr 上警告一次。
/// </summary>
public class IcuCaseFoldingTests
{
    [Fact]
    public void FullCaseMapping_MatchesV8WhereItDiffersFromDotnet()
    {
        if (!IcuCaseFolding.IsAvailable)
        {
            // 回退路径：退化成 .NET 的 invariant 映射，因此这些字符上与 V8 不一致（已警告过）。
            Assert.False(Core.EquateStringsCaseInsensitive("ss", "ß"));
            Assert.Equal("ß".ToUpperInvariant(), IcuCaseFolding.ToUpper("ß"));
            return;
        }

        Assert.Equal("SS", IcuCaseFolding.ToUpper("ß"));
        Assert.Equal("STRASSE", IcuCaseFolding.ToUpper("Straße"));
        Assert.Equal("ß", IcuCaseFolding.ToLower("ẞ"));

        // İ (U+0130) lower-cases to two code points — which is why the port maps whole strings, not code points.
        Assert.Equal("\u0069\u0307", IcuCaseFolding.ToLower("İ"));
    }

    [Fact]
    public void EquateStringsCaseInsensitive_FollowsV8WhenIcuIsAvailable()
    {
        // V8: "ss".toUpperCase() === "ß".toUpperCase() → true
        Assert.Equal(IcuCaseFolding.IsAvailable, Core.EquateStringsCaseInsensitive("ss", "ß"));

        Assert.True(Core.EquateStringsCaseInsensitive("abc", "ABC"));
        Assert.False(Core.EquateStringsCaseInsensitive("abc", "abd"));
        Assert.True(Core.EquateStringsCaseInsensitive(null, null));
        Assert.False(Core.EquateStringsCaseInsensitive("a", null));
    }

    [Fact]
    public void CompareStringsCaseInsensitive_OrdersByFoldedCodeUnits()
    {
        Assert.Equal(Comparison.EqualTo, Core.CompareStringsCaseInsensitive("abc", "ABC"));
        Assert.Equal(Comparison.LessThan, Core.CompareStringsCaseInsensitive("ABC", "abd"));
        Assert.Equal(Comparison.GreaterThan, Core.CompareStringsCaseInsensitive("abd", "ABC"));
    }

    [Fact]
    public void CaseConversion_LeavesOrdinaryTextAlone()
    {
        Assert.Equal("ABC", IcuCaseFolding.ToUpper("abc"));
        Assert.Equal("abc", IcuCaseFolding.ToLower("ABC"));
        Assert.Equal("", IcuCaseFolding.ToUpper(""));
        Assert.Equal("", IcuCaseFolding.ToLower(""));
        Assert.Equal("ABC123", IcuCaseFolding.ToUpper("abc123"));
    }

    [Fact]
    public void FileNameLowerCase_GoesThroughTheSameMapping()
    {
        // core.ts's toFileNameLowerCase is a toLowerCase in disguise; the sharp s is the visible difference.
        Assert.Equal(IcuCaseFolding.ToLower("Straße"), Core.ToFileNameLowerCase("Straße"));
    }
}
