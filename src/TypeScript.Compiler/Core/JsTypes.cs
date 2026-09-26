namespace TypeScript.Compiler;

/// <summary>
/// TS 里"没有值"的那几个类型，在 C# 侧各有一个明确的落点 —— 它们不再是 <c>object?</c>。
/// </summary>
/// <remarks>
/// 把这一层分出来，是为了让 <c>object?</c> 在生成物里只剩两种含义：没能收敛成一个类型的联合，以及声明在
/// 被提取的源文件之外的类型（两种都会带注释说明）。见 <c>docs/architecture.md</c> §12.6。
/// <para>
/// <c>IJsType</c> 只是标记，不是"所有 JS 值都必须继承"的基类：TS 的特殊类型彼此没有共同结构，而 C# 的基类
/// 槽位只有一个，将来若有别的 JS 值语义（JS 的 number/string）要表达，这个接口也能容纳。
/// </para>
/// </remarks>
internal interface IJsType
{
}

/// <summary>JS 的 <c>undefined</c>。</summary>
/// <remarks>
/// 它只有一个值，所以这里是单例，构造函数私有 —— 单例语义靠这一点保证，而不是靠类型系统。
/// </remarks>
internal sealed class JsUndefined : IJsType
{
    /// <summary>那个 <c>undefined</c>。</summary>
    public static JsUndefined Value { get; } = new();

    private JsUndefined()
    {
    }

    public override string ToString() => "undefined";
}

/// <summary>TS 的 <c>never</c>：底类型，没有任何值。</summary>
/// <remarks>
/// 构造函数私有，因为它的实例<b>不存在</b>，也永远不会有：这个类型只出现在"此处不可能有值"的位置上
/// （<c>ArrowFunction.name</c> 就是这种 —— 箭头函数没有名字）。它能出现在签名里，但永远不会被赋一个值。
/// </remarks>
internal sealed class JsNever : IJsType
{
    private JsNever()
    {
    }

    public override string ToString() => "never";
}
