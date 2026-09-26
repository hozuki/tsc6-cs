using System.Reflection;

namespace TypeScript.Compiler.Tests;

/// <summary>
/// TS 里"没有值"的那两个类型在 C# 侧的落点（docs/architecture.md §12.6）：<c>JsUndefined</c> 是单例，
/// <c>JsNever</c> 没有实例 —— 后者正是"此处永远不会有值"的字面表达。
/// </summary>
public class JsTypesTests
{
    [Fact]
    public void JsUndefined_IsASingleton()
    {
        Assert.Same(JsUndefined.Value, JsUndefined.Value);
        Assert.Equal("undefined", JsUndefined.Value.ToString());
    }

    [Fact]
    public void JsUndefined_CannotBeConstructed()
    {
        Assert.Empty(PublicConstructors(typeof(JsUndefined)));
    }

    [Fact]
    public void JsNever_HasNoInstance() => Assert.Empty(PublicConstructors(typeof(JsNever)));

    [Fact]
    public void Both_AreJsTypes()
    {
        Assert.IsAssignableFrom<IJsType>(JsUndefined.Value);
        Assert.Contains(typeof(IJsType), typeof(JsNever).GetInterfaces());
    }

    private static ConstructorInfo[] PublicConstructors(Type type)
        => type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

    [Fact]
    public void NumberToString_MatchesJavaScript()
    {
        // Ground truth from node v24: `String(value)` for each pair. The parser stores this string as
        // NumericLiteral.Text, so a difference here changes the AST (see docs/architecture.md §15.11).
        (double Value, string Text)[] cases =
        [
            (1e25, "1e+25"),
            (1.2e35, "1.2e+35"),
            (8.14e-5, "0.0000814"),
            (1.2345678912345678e53, "1.2345678912345678e+53"),
            (6.02e23, "6.02e+23"),
            (1e-10, "1e-10"),
            (1e21, "1e+21"),
            (1e20, "100000000000000000000"),
            (0.0001, "0.0001"),
            (12345d, "12345"),
            (1e-7, "1e-7"),
            (5e-324, "5e-324"),
            (double.MaxValue, "1.7976931348623157e+308"),
            (0.1, "0.1"),
            (1500d, "1500"),
            (1.05, "1.05"),
            (-2.5e-8, "-2.5e-8"),
            (100d, "100"),
            (0d, "0"),
            (-0d, "0"),
            (double.NaN, "NaN"),
            (double.PositiveInfinity, "Infinity"),
            (double.NegativeInfinity, "-Infinity"),
        ];

        foreach (var (value, text) in cases)
        {
            Assert.Equal(text, Js.NumberToString(value));
        }
    }
}
