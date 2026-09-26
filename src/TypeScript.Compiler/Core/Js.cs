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

    /// <summary>Evaluates JavaScript truthiness for a boolean, which is already the answer.</summary>
    /// <remarks>
    /// This overload exists so a condition that <em>is</em> a comparison does not box — and so the generator can
    /// wrap every condition uniformly with <c>Js.IsTruthy(…)</c> without having to know what the operand's type is.
    /// </remarks>
    public static bool IsTruthy(bool value) => value;

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
    /// JS's <c>parseInt(text, radix)</c> for a text that consists of digits of that radix.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: like <see cref="NumberToString"/> this is not in the reference — TS calls <c>parseInt</c> and lets the
    /// engine do it, and the engine accumulates in double arithmetic: a 500-digit binary literal loses precision and
    /// eventually saturates at <c>Infinity</c> rather than failing. <c>Convert.ToInt32(text, 2)</c> threw
    /// <c>OverflowException</c> on exactly those literals (4 corpus files), so the port folds with the same arithmetic.
    /// The scanner hands this method validated digit runs, so the prefix/sign/whitespace rules of <c>parseInt</c> are
    /// not implemented — the first character that is not a digit of the radix stops the fold, as in the engine.
    /// </remarks>
    public static double ParseInt(string text, int radix)
    {
        var value = 0d;
        foreach (var ch in text)
        {
            var digit = ch switch
            {
                >= '0' and <= '9' => ch - '0',
                >= 'a' and <= 'z' => ch - 'a' + 10,
                >= 'A' and <= 'Z' => ch - 'A' + 10,
                _ => -1,
            };

            if (digit < 0 || digit >= radix) break;
            value = (value * radix) + digit;
        }

        return value;
    }

    /// <summary>
    /// Formats a number the way JavaScript's parameterless <c>Number.prototype.toString()</c> does.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: the reference implementation does not contain this function — it calls <c>String(value)</c> and lets
    /// the JavaScript engine implement ECMA-262's <c>Number::toString(10)</c>. The port has to implement it, and the
    /// part that matters is not the digit string (.NET's <c>"R"</c> is also shortest-round-tripping) but the four
    /// rendering shapes and their thresholds: JS switches to the exponent form only when the decimal point sits past
    /// the 21st digit or before the 6th, writes the exponent lower-case with an explicit sign and no padding, and pads
    /// with zeros inside the mantissa/leading positions otherwise. The parser stores this string as
    /// <c>NumericLiteral.Text</c>, so getting it wrong changes the AST (`1E+25` instead of `1e+25`,
    /// `8.14E-05` instead of `0.0000814`) — 7 corpus files, found by the P1a differential run.
    /// </remarks>
    public static string NumberToString(double value)
    {
        if (double.IsNaN(value)) return "NaN";
        if (value == 0d) return "0";
        if (value < 0d) return "-" + NumberToString(-value);
        if (double.IsPositiveInfinity(value)) return "Infinity";

        var (digits, n) = ShortestDecimalDigits(value);
        var k = digits.Length;

        // ECMA-262 6.1.6.1: let s be `digits` (k of them) and n the position of the decimal point, so that the value is
        // s x 10^(n-k). The four shapes below are the standard's own cases.
        if (k <= n && n <= 21) return digits + new string('0', n - k);
        if (0 < n && n <= 21) return digits[..n] + "." + digits[n..];
        if (-6 < n && n <= 0) return "0." + new string('0', -n) + digits;

        var exponent = n - 1;
        var mantissa = k == 1 ? digits : digits[..1] + "." + digits[1..];
        return mantissa + "e" + (exponent < 0 ? "-" : "+") + Math.Abs(exponent).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The shortest round-tripping decimal digit string of a positive finite double together with ECMA-262's
    /// <c>n</c>: the number of digits between the decimal point and the first significant digit, counted from the
    /// point (so <c>12345</c> has n = 5, <c>0.0000814</c> has n = -4).
    /// </summary>
    private static (string Digits, int N) ShortestDecimalDigits(double value)
    {
        var text = value.ToString("R", CultureInfo.InvariantCulture);

        var e = text.IndexOf('E');
        var exponent = e >= 0 ? int.Parse(text[(e + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture) : 0;
        var mantissa = e >= 0 ? text[..e] : text;

        var dot = mantissa.IndexOf('.');
        var raw = dot >= 0 ? mantissa.Remove(dot, 1) : mantissa;
        var dotPos = dot >= 0 ? dot : raw.Length;

        var first = 0;
        while (first < raw.Length && raw[first] == '0') first++;
        var end = raw.Length;
        while (end > first + 1 && raw[end - 1] == '0') end--;

        return (raw[first..end], dotPos + exponent - first);
    }
}
