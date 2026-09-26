namespace TypeScript.Compiler.Tests;

/// <summary>
/// <see cref="ScriptTarget"/> 的命令行名（<c>utilities.ts</c> 的 <c>getNameOfScriptTarget</c>）。
/// </summary>
public class ScriptTargetsTests
{
    // [InlineData] 只能带编译期常量；ScriptTarget 是 internal 枚举，公开的测试方法不能拿它当参数，
    // 所以这里传数值再转回来。
    [Theory]
    [InlineData((int)ScriptTarget.ES3, "es3")]
    [InlineData((int)ScriptTarget.ES5, "es5")]
    // TS 的选项表里 es6 排在 es2015 之前，所以 ES2015 得到的是 es6。
    [InlineData((int)ScriptTarget.ES2015, "es6")]
    [InlineData((int)ScriptTarget.ES2022, "es2022")]
    [InlineData((int)ScriptTarget.ES2025, "es2025")]
    [InlineData((int)ScriptTarget.ESNext, "esnext")]
    public void GetNameOfScriptTarget_ReturnsTheCommandLineName(int target, string expected)
        => Assert.Equal(expected, ScriptTargets.GetNameOfScriptTarget((ScriptTarget)target));

    [Fact]
    public void GetNameOfScriptTarget_ReturnsNullWhenTheTargetHasNoOptionName()
        => Assert.Null(ScriptTargets.GetNameOfScriptTarget(ScriptTarget.JSON));
}
