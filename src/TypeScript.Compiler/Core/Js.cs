using System.Diagnostics.CodeAnalysis;
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

    /// <summary>Evaluates JavaScript truthiness for a string: the empty string is falsy.</summary>
    /// <remarks>
    /// This overload exists so that overload resolution for a <c>string</c> argument picks JS semantics rather
    /// than the generic one below (see its note). It also tells the compiler that a true result means the string
    /// is non-empty, which is what the ported <c>if (s)</c> guards rely on.
    /// </remarks>
    public static bool IsTruthy([NotNullWhen(true)] string? value) => !string.IsNullOrEmpty(value);

    /// <summary>Evaluates JavaScript truthiness for a reference value that is expected to be non-null.</summary>
    /// <remarks>
    /// PORT-NOTE: this overload answers "is not null", which is <em>not</em> JS truthiness for every type it can
    /// be bound to. It exists for call sites whose value is a reference the compiler cannot prove non-null. For
    /// anything whose falsy-ness matters, pass an explicit <c>(object?)</c> cast so that
    /// <see cref="IsTruthy(object?)"/> is chosen instead — including for a <c>string</c>, which used to bind
    /// here and made an empty string look truthy.
    /// </remarks>
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
    /// JS's unary <c>+</c> (ToNumber) for the values the scanner hands it.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: the scanner passes decimal digit strings, and — for a hex literal — the <c>0x…</c> text itself
    /// (JS's <c>+</c> understands the radix prefixes; <c>double.TryParse</c> does not). The part of ToNumber the
    /// scanner relies on is that a failed conversion yields <c>NaN</c> rather than throwing. Whitespace trimming
    /// and the empty string being 0 are not reachable from there.
    /// </remarks>
    public static double ToNumber(string value)
    {
        // A hex literal arrives here as its own text (`+tokenValue` in checkBigIntSuffix); the binary and octal
        // prefixes never do, because that method converts those with parseInt first.
        if (value.Length > 2 && value[0] == '0' && (value[1] == 'x' || value[1] == 'X'))
        {
            return long.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex)
                ? hex
                : double.NaN;
        }

        return double.TryParse(
            value,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
            CultureInfo.InvariantCulture,
            out var result)
                ? result
                : double.NaN;
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
