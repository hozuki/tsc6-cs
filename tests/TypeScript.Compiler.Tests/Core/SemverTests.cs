namespace TypeScript.Compiler.Tests;

// 对应 src/compiler/semver.ts:1-451
public class VersionTests
{
    [Fact]
    public void Parse_ReadsComponentsAndDefaults()
    {
        var version = new Version("1.2.3");
        Assert.Equal(1, version.Major);
        Assert.Equal(2, version.Minor);
        Assert.Equal(3, version.Patch);
        Assert.Empty(version.Prerelease);
        Assert.Empty(version.Build);

        // 允许 X 与 X.Y，缺省部分为 0
        var partial = new Version("1");
        Assert.Equal(1, partial.Major);
        Assert.Equal(0, partial.Minor);
        Assert.Equal(0, partial.Patch);

        var withQualifiers = new Version("1.2.3-alpha.1+build.7");
        Assert.Equal(["alpha", "1"], withQualifiers.Prerelease);
        Assert.Equal(["build", "7"], withQualifiers.Build);
    }

    [Fact]
    public void TryParse_RejectsInvalidText()
    {
        Assert.Null(Version.TryParse("not-a-version"));
        Assert.Null(Version.TryParse("1.2.3-"));
        // 前导零不允许
        Assert.Null(Version.TryParse("01.2.3"));
        Assert.NotNull(Version.TryParse("1.2.3"));
    }

    [Fact]
    public void Zero_IsTheAllZeroVersion()
    {
        Assert.Equal(0, Version.Zero.Major);
        Assert.Equal("0.0.0-0", Version.Zero.ToString());
    }

    [Fact]
    public void ToString_RoundTrips()
    {
        Assert.Equal("1.2.3", new Version("1.2.3").ToString());
        Assert.Equal("1.2.3-alpha.1", new Version("1.2.3-alpha.1").ToString());
        Assert.Equal("1.2.3+build", new Version("1.2.3+build").ToString());
        Assert.Equal("1.2.3-alpha+build", new Version("1.2.3-alpha+build").ToString());
    }

    [Fact]
    public void CompareTo_OrdersBySemverPrecedence()
    {
        Assert.Equal(Comparison.EqualTo, new Version("1.2.3").CompareTo(new Version("1.2.3")));
        Assert.Equal(Comparison.LessThan, new Version("1.2.3").CompareTo(new Version("1.2.4")));
        Assert.Equal(Comparison.GreaterThan, new Version("1.3.0").CompareTo(new Version("1.2.9")));

        // 预发布版本优先级低于正式版本
        Assert.Equal(Comparison.LessThan, new Version("1.0.0-alpha").CompareTo(new Version("1.0.0")));

        // 数字标识符优先级低于非数字标识符
        Assert.Equal(Comparison.LessThan, new Version("1.0.0-1").CompareTo(new Version("1.0.0-alpha")));

        // 标识符相同时，字段更多的预发布版本优先级更高
        Assert.Equal(Comparison.LessThan, new Version("1.0.0-alpha").CompareTo(new Version("1.0.0-alpha.1")));

        // build 元数据不参与比较
        Assert.Equal(Comparison.EqualTo, new Version("1.0.0+a").CompareTo(new Version("1.0.0+b")));

        // undefined 视为更大
        Assert.Equal(Comparison.GreaterThan, new Version("1.0.0").CompareTo(null));
    }

    [Fact]
    public void Increment_AdvancesTheRequestedField()
    {
        var version = new Version("1.2.3");

        Assert.Equal("2.0.0", version.Increment(VersionField.Major).ToString());
        Assert.Equal("1.3.0", version.Increment(VersionField.Minor).ToString());
        Assert.Equal("1.2.4", version.Increment(VersionField.Patch).ToString());
        // 原对象不变
        Assert.Equal("1.2.3", version.ToString());
    }

    [Fact]
    public void With_OverridesOnlyProvidedFields()
    {
        var version = new Version("1.2.3-alpha");

        Assert.Equal("1.2.3-0", version.With(prerelease: "0").ToString());
        Assert.Equal("2.2.3-alpha", version.With(major: 2).ToString());
        Assert.Equal("1.2.3-alpha", version.With().ToString());
    }
}

// 对应 src/compiler/semver.ts:206-451
public class VersionRangeTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.3", true)]
    [InlineData("1.2.3", "1.2.4", false)]
    [InlineData("^1.2.0", "1.3.0", true)]
    [InlineData("^1.2.0", "2.0.0", false)]
    [InlineData("^0.2.0", "0.2.5", true)]
    [InlineData("^0.2.0", "0.3.0", false)]
    [InlineData("~1.2.0", "1.2.9", true)]
    [InlineData("~1.2.0", "1.3.0", false)]
    [InlineData(">=1.2.0 <2.0.0", "1.9.9", true)]
    [InlineData(">=1.2.0 <2.0.0", "2.0.0", false)]
    [InlineData("1.2.x", "1.2.7", true)]
    [InlineData("1.2.x", "1.3.0", false)]
    [InlineData("1.2.0 - 1.4.0", "1.3.0", true)]
    [InlineData("1.2.0 - 1.4.0", "1.5.0", false)]
    [InlineData("1.2.0 || 2.0.0", "2.0.0", true)]
    [InlineData("1.2.0 || 2.0.0", "3.0.0", false)]
    [InlineData("*", "9.9.9", true)]
    public void Test_MatchesNodeSemverSemantics(string range, string version, bool expected)
    {
        Assert.Equal(expected, new VersionRange(range).Test(version));
    }

    [Fact]
    public void EmptyRange_TreatsAsAllVersions()
    {
        Assert.True(new VersionRange("").Test("1.2.3"));
        Assert.Equal("*", new VersionRange("").ToString());
    }

    [Fact]
    public void TryParse_RejectsInvalidSpec()
    {
        Assert.Null(VersionRange.TryParse("not a range"));
        Assert.NotNull(VersionRange.TryParse("^1.2.0"));
    }

    [Fact]
    public void ToString_NormalizesRangeText()
    {
        Assert.Equal(">=1.2.3 <2.0.0", new VersionRange("^1.2.3").ToString());
        // 无操作符的分段被规范化为 "="
        Assert.Equal("=1.2.0 || =2.0.0", new VersionRange("1.2.0 || 2.0.0").ToString());
    }

    [Fact]
    public void Test_AcceptsVersionInstance()
    {
        Assert.True(new VersionRange("^1.0.0").Test(new Version("1.5.0")));
    }
}

// 对应 src/compiler/performanceCore.ts:1-103
public class PerformanceCoreTests
{
    [Fact]
    public void TryGetNativePerformanceHooks_IsNullOnTheClr()
    {
        // CLR 没有 Web Performance User Timings API（见 PerformanceCore 的 PORT-NOTE）
        Assert.Null(PerformanceCore.TryGetNativePerformanceHooks());
    }

    [Fact]
    public void Timestamp_IsMonotonicAndMillisecondScaled()
    {
        var first = PerformanceCore.Timestamp();
        Thread.Sleep(5);
        var second = PerformanceCore.Timestamp();

        Assert.True(second > first, "timestamp must advance");
        // 5ms 的睡眠至少应产生约 1ms 的推进（放宽下界以避免调度抖动导致的偶发失败）
        Assert.True(second - first >= 1, $"expected >= 1ms elapsed, got {second - first}ms");
    }
}
