namespace TypeScript.Compiler.Tests;

/// <summary>
/// sys.ts 里与宿主无关的部分。哈希的参考值刻意用 TS 自身算出来再固定在这里，而不是复述一遍算法 ——
/// 复述只能证明实现和它自己一致。
/// </summary>
[Collection(ProcessWideStateCollection.Name)]
public class SysCoreTests
{
    [Theory]
    [InlineData("", "5381")]
    [InlineData("a", "177670")]
    [InlineData("abc", "193485963")]
    [InlineData("The quick brown fox jumps over the lazy dog", "5180766430")]
    [InlineData(@"D:\Source\VS\TEMP\tsc-cs\src\compiler\sys.ts", "-7689262213")]
    public void GenerateDjb2Hash_MatchesTheTsImplementation(string data, string expected)
        => Assert.Equal(expected, SysCore.GenerateDjb2Hash(data));

    [Fact]
    public void GenerateDjb2Hash_LeavesInt32WithoutLosingTheValue()
    {
        // acc 在 2000 步里有 1894 步离开 int32：<< 每步截断，而随后的 + 不会。
        // 用 int 累加会在这些步骤上截断，得到不同的哈希。
        Assert.Equal("-468096891", SysCore.GenerateDjb2Hash(new string('x', 2000)));
    }

    [Fact]
    public void GenerateDjb2Hash_HandlesSurrogatePairsAsUtf16CodeUnits()
    {
        // JS 的 charCodeAt 返回 UTF-16 code unit，而不是码点；C# 的 string 索引与之相同。
        Assert.Equal(SysCore.GenerateDjb2Hash("\uD83D\uDE00"), SysCore.GenerateDjb2Hash("\uD83D\uDE00"));
        Assert.NotEqual(SysCore.GenerateDjb2Hash("\uD83D"), SysCore.GenerateDjb2Hash("\uDE00"));
    }

    [Theory]
    [InlineData(0d, 0d, FileWatcherEventKind.Created)]
    [InlineData(0d, 1234d, FileWatcherEventKind.Created)]
    [InlineData(1234d, 0d, FileWatcherEventKind.Deleted)]
    [InlineData(1234d, 5678d, FileWatcherEventKind.Changed)]
    public void GetFileWatcherEventKind_ClassifiesByTimestamps(
        double oldTime,
        double newTime,
        FileWatcherEventKind expected)
        => Assert.Equal(expected, SysCore.GetFileWatcherEventKind(oldTime, newTime));

    [Fact]
    public void GetModifiedTime_FallsBackWhenTheHostCannotReport()
    {
        var host = new FakeSystem();
        Assert.Equal(SysCore.MissingFileModifiedTime, SysCore.GetModifiedTime(host, "x.ts"));

        host.ModifiedTime = DateTimeOffset.FromUnixTimeMilliseconds(42);
        Assert.Equal(host.ModifiedTime.Value, SysCore.GetModifiedTime(host, "x.ts"));
    }

    [Fact]
    public void MissingFileModifiedTime_IsTheUnixEpoch()
        => Assert.Equal(DateTimeOffset.UnixEpoch, SysCore.MissingFileModifiedTime);

    [Fact]
    public void SysLog_IsSwappable()
    {
        var restore = new List<string>();
        SysCore.SetSysLog(restore.Add);
        try
        {
            SysCore.SysLog("hello");
            SysCore.SysLog("world");
            Assert.Equal(["hello", "world"], restore);
        }
        finally
        {
            SysCore.SetSysLog(static _ => { });
        }
    }

    [Fact]
    public void IgnoredPaths_MatchesTs()
        => Assert.Equal(["/node_modules/.", "/.git", "/.#"], SysCore.IgnoredPaths);

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(double.NegativeInfinity, 0)]
    [InlineData(5.9d, 5)]
    [InlineData(-5.9d, -5)]
    [InlineData(4294967295d, -1)]
    [InlineData(4294967296d, 0)]
    [InlineData(2147483648d, int.MinValue)]
    public void ToInt32_MatchesJavaScript(double value, int expected)
        => Assert.Equal(expected, Js.ToInt32(value));

    [Theory]
    [InlineData(5180766430d, "5180766430")]
    [InlineData(-4817739329d, "-4817739329")]
    [InlineData(0d, "0")]
    [InlineData(5d, "5")]
    public void NumberToString_FormatsLikeJavaScript(double value, string expected)
        => Assert.Equal(expected, Js.NumberToString(value));

    [Fact]
    public void NumberToString_UsesJavascriptSpellingsForSpecialValues()
    {
        Assert.Equal("NaN", Js.NumberToString(double.NaN));
        Assert.Equal("Infinity", Js.NumberToString(double.PositiveInfinity));
        Assert.Equal("-Infinity", Js.NumberToString(double.NegativeInfinity));

        // JS 的 -0 打印为 "0"（xUnit 视 0d 与 -0d 为同一 InlineData，故放在这里）。
        Assert.Equal("0", Js.NumberToString(-0d));
    }
}
