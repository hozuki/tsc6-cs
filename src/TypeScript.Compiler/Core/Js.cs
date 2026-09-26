using System.Globalization;

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

    /// <summary>
    /// Converts a number the way JavaScript's <c>ToInt32</c> does.
    /// </summary>
    /// <remarks>
    /// This is the exact semantics behind JS's bitwise operators: <c>NaN</c> and the infinities become
    /// <c>0</c>, everything else is truncated toward zero and then reduced modulo 2^32 into the signed
    /// 32-bit range. A plain <c>(int)value</c> cast is <em>not</em> equivalent — for out-of-range inputs
    /// .NET saturates rather than wrapping.
    /// </remarks>
    public static int ToInt32(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return 0;

        var modulo = Math.Truncate(value) % 4294967296d; // 2^32
        if (modulo < 0) modulo += 4294967296d;
        if (modulo >= 2147483648d) modulo -= 4294967296d; // 2^31
        return (int)modulo;
    }

    /// <summary>
    /// Formats a number the way JavaScript's parameterless <c>Number.prototype.toString()</c> does.
    /// </summary>
    /// <remarks>
    /// The difference that matters for ported code: JS renders integral values in plain decimal, while
    /// C#'s default <c>double.ToString()</c> switches to scientific notation well before that. The port
    /// therefore formats any integral value within the safe-integer range as a decimal integer.
    /// </remarks>
    public static string NumberToString(double value)
    {
        if (double.IsNaN(value)) return "NaN";
        if (double.IsPositiveInfinity(value)) return "Infinity";
        if (double.IsNegativeInfinity(value)) return "-Infinity";
        if (value == 0d) return "0";

        // 2^53: past this the double no longer represents every integer, so JS uses its exponent form too.
        const double MaxSafeInteger = 9007199254740992d;
        if (value >= -MaxSafeInteger && value <= MaxSafeInteger && Math.Floor(value) == value)
        {
            return ((long)value).ToString(CultureInfo.InvariantCulture);
        }

        return value.ToString("R", CultureInfo.InvariantCulture);
    }
}
