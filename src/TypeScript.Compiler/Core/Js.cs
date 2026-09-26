namespace TypeScript.Compiler;

// PORT-NOTE: TS↔C# 语言模型差异 #1（见 docs/architecture.md §5）——JS 的 "truthy" 判定在 C# 中
// 没有对应物（C# 只有 bool）。TS 编译器源码大量依赖 truthy（`if (v)`、`v ? ... : ...`、`??`/`||`），
// 因此这里提供显式的 JS truthiness 语义，供移植代码在需要时调用，避免把 `if (v)` 误译成 `if (v != null)`。
internal static class Js
{
    /// <summary>
    /// Evaluates JavaScript truthiness: <c>null</c>/<c>undefined</c>, <c>false</c>, <c>0</c>, <c>NaN</c>
    /// and the empty string are falsy; everything else (including empty arrays/objects) is truthy.
    /// </summary>
    public static bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        sbyte i => i != 0,
        byte i => i != 0,
        short i => i != 0,
        ushort i => i != 0,
        int i => i != 0,
        uint i => i != 0,
        long i => i != 0,
        ulong i => i != 0,
        float f => f != 0,
        double d => !double.IsNaN(d) && d != 0,
        decimal m => m != 0,
        string s => s.Length != 0,
        _ => true,
    };

    /// <summary>Evaluates JavaScript truthiness for a reference value that is expected to be non-null.</summary>
    public static bool IsTruthy<T>(T? value)
        where T : class
        => value is not null;
}
