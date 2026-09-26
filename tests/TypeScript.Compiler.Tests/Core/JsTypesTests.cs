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
}
