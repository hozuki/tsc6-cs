using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;

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
    /// JS's unary <c>+</c> (ToNumber) for a string — ECMA-262's <c>StringNumericLiteral</c>.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS writes <c>+s</c> and lets the engine do this; the port's callers hand it two different kinds of
    /// text, so the whole grammar is implemented rather than the subset the scanner needs. The scanner passes a
    /// numeric literal's own text — decimal digit strings, and for a hex literal the <c>0x…</c> text itself
    /// (<c>+tokenValue</c> in <c>checkBigIntSuffix</c>; that method converts the 0b/0o literals with <c>parseInt</c>
    /// first, like the reference). Every other caller passes a <em>name</em> or an arbitrary string literal's value to
    /// a <c>+name</c>-shaped test — <c>isNumericLiteralName</c>, <c>isValidNumberString</c> (checker.ts:26616-26620,
    /// whose <c>+s</c> is over the value of a string literal type: <c>"0o1"</c>, <c>" 1 "</c>, <c>"Infinity"</c> are
    /// all reachable) — so the leading/trailing WhiteSpace and LineTerminator, the empty string being <c>0</c>, the
    /// three infinities and the case-insensitive <em>unsigned</em> radix prefixes all matter. Without them
    /// <c>isValidNumberString("0o1")</c> answered false where <c>+"0o1"</c> is 1, and the checker rejected
    /// <c>numbers("0o1")</c> and <c>numbers("0b1")</c> in
    /// tests/cases/conformance/types/literal/templateLiteralTypesPatterns.ts. A radix-prefixed digit run is folded by
    /// <see cref="ParseInt"/> — the arithmetic the scanner already uses for 0b/0o — except when it fits <c>ulong</c>,
    /// which converts in one step and so rounds exactly as the specification's single rounding does. The decimal half
    /// is validated by <see cref="IsDecimalLiteralRun"/> before <c>double.TryParse</c> supplies its value, because
    /// <c>TryParse</c> accepts the infinity symbol in any casing (<c>"infinity"</c> is <c>NaN</c> to JS).
    /// </remarks>
    public static double ToNumber(string value)
    {
        // ECMA-262 7.1.4.1: WhiteSpace and LineTerminator on either side of the literal are ignored.
        var start = 0;
        var end = value.Length;
        while (start < end && IsJsWhiteSpace(value[start])) start++;
        while (end > start && IsJsWhiteSpace(value[end - 1])) end--;
        var text = value[start..end];

        if (text.Length == 0) return 0d;
        if (text == "Infinity" || text == "+Infinity") return double.PositiveInfinity;
        if (text == "-Infinity") return double.NegativeInfinity;

        // StrNumericLiteral ::: NonDecimalIntegerLiteral — an unsigned `0x`/`0o`/`0b` run. A *signed* one is not a
        // StrNumericLiteral (`+"-0x1"` is NaN in JS), so it falls through to the decimal attempt below and fails.
        if (text.Length > 2 && text[0] == '0')
        {
            var radix = text[1] switch
            {
                'x' or 'X' => 16,
                'o' or 'O' => 8,
                'b' or 'B' => 2,
                _ => 0,
            };

            if (radix != 0)
            {
                var digits = text.AsSpan(2);
                if (!IsRadixDigitRun(digits, radix))
                {
                    return double.NaN;
                }

                // A hex run of at most 16 digits is an exact 64-bit integer, and converting it once keeps the
                // specification's single rounding (`long` would read a high-bit-set run as two's complement and answer
                // a negative number for `0x8000…`); longer runs fold in double arithmetic like ParseInt does.
                return radix == 16 && ulong.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex)
                    ? hex
                    : ParseInt(text[2..], radix);
            }
        }

        return IsDecimalLiteralRun(text) && double.TryParse(
            text,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
            CultureInfo.InvariantCulture,
            out var result)
                ? result
                : double.NaN;
    }

    /// <summary>ECMA-262's WhiteSpace ∪ LineTerminator, the set a <c>StringNumericLiteral</c> trims.</summary>
    /// <remarks>
    /// Not <see cref="char.IsWhiteSpace(char)"/>, which contains U+0085 (NEL) — not whitespace to JS — and omits
    /// U+FEFF (ZWNBSP), which is.
    /// </remarks>
    private static bool IsJsWhiteSpace(char ch)
        => ch is '\t' or '\n' or '\v' or '\f' or '\r' or ' ' or '\u00a0' or '\ufeff' or '\u2028' or '\u2029'
            || char.GetUnicodeCategory(ch) == UnicodeCategory.SpaceSeparator;

    /// <summary>Whether every character is a digit of the given radix — no separators, no sign, at least one digit.</summary>
    private static bool IsRadixDigitRun(ReadOnlySpan<char> digits, int radix)
    {
        foreach (var ch in digits)
        {
            var digit = ch switch
            {
                >= '0' and <= '9' => ch - '0',
                >= 'a' and <= 'f' => (ch - 'a') + 10,
                >= 'A' and <= 'F' => (ch - 'A') + 10,
                _ => -1,
            };

            if (digit < 0 || digit >= radix)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether the text is ECMA-262's <c>StrDecimalLiteral</c> — the decimal half of a <c>StringNumericLiteral</c>.
    /// </summary>
    /// <remarks>
    /// The text has already had its surrounding WhiteSpace trimmed; the digits are ASCII in both languages.
    /// <see cref="double.TryParse(string, NumberStyles, IFormatProvider, out double)"/> cannot stand in for this
    /// check, because it is more permissive than the grammar: it reads the infinity symbol in any casing
    /// (<c>"infinity"</c> is <c>NaN</c> to JS) and its own notion of white space includes U+0085. The work is only
    /// done so that <c>TryParse</c> — which rounds correctly, unlike a hand-rolled accumulator — can produce the value.
    /// </remarks>
    private static bool IsDecimalLiteralRun(ReadOnlySpan<char> text)
    {
        var i = text.Length > 0 && (text[0] == '+' || text[0] == '-') ? 1 : 0;
        var digits = 0;

        while (i < text.Length && text[i] is >= '0' and <= '9')
        {
            digits++;
            i++;
        }

        if (i < text.Length && text[i] == '.')
        {
            i++;
            while (i < text.Length && text[i] is >= '0' and <= '9')
            {
                digits++;
                i++;
            }
        }

        if (digits == 0)
        {
            return false;
        }

        if (i < text.Length && (text[i] == 'e' || text[i] == 'E'))
        {
            i++;
            if (i < text.Length && (text[i] == '+' || text[i] == '-')) i++;

            var exponentDigits = 0;
            while (i < text.Length && text[i] is >= '0' and <= '9')
            {
                exponentDigits++;
                i++;
            }

            if (exponentDigits == 0)
            {
                return false;
            }
        }

        return i == text.Length;
    }

    /// <summary>
    /// JS's <c>parseInt(text, radix)</c> for a text that consists of digits of that radix.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: like <see cref="NumberToString"/> this is not in the reference — TS calls <c>parseInt</c> and lets the
    /// engine do it, and the engine accumulates in double arithmetic: a 500-digit binary literal loses precision and
    /// eventually saturates at <c>Infinity</c> rather than failing. <c>Convert.ToInt32(text, 2)</c> threw
    /// <c>OverflowException</c> on exactly those literals (4 corpus files), so the port folds with the same arithmetic.
    /// The scanner and <see cref="ToNumber"/> hand this method validated digit runs, so the prefix/sign/whitespace
    /// rules of <c>parseInt</c> are not implemented — the first character that is not a digit of the radix stops the
    /// fold, as in the engine.
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
    /// Formats a number the way JavaScript's <c>Number.prototype.toString(radix)</c> does, for an integral value.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: the reference calls <c>(+tokenValue).toString(8)</c> and lets the JavaScript engine do it; the port
    /// has to implement it. The only consumer is scanner.ts:1258's legacy-octal error message, whose argument is
    /// <c>parseInt</c>'s result — an integral number. For an integral value and a power-of-two radix the digits are
    /// exact, so this renders the value's own mantissa/exponent decomposition instead of running a
    /// shortest-round-trip search; a value that is not finite keeps its <see cref="NumberToString"/> spelling
    /// (<c>Infinity</c>), which the engine also answers for a radix.
    /// </remarks>
    public static string NumberToStringRadix(double value, int radix)
    {
        Debug.Assert(radix is >= 2 and <= 36, $"radix {radix} out of range");
        if (!double.IsFinite(value)) return NumberToString(value);
        if (value == 0d) return "0";

        var negative = value < 0d;
        if (negative) value = -value;

        string digits;
        if (value <= 9007199254740992d)
        {
            // Every integer up to 2^53 is exact, so this loop is the exact representation.
            var integer = (ulong)value;
            var buffer = new char[64]; // 2^53 needs 53 digits in base 2
            var index = buffer.Length;
            do
            {
                buffer[--index] = DigitCharacter((int)(integer % (ulong)radix));
                integer /= (ulong)radix;
            }
            while (integer != 0);

            digits = new string(buffer, index, buffer.Length - index);
        }
        else
        {
            // Above 2^53 the value is still integral, and its exact value is mantissa × 2^(exponent - 1075).
            var bits = BitConverter.DoubleToInt64Bits(value);
            var mantissa = (bits & 0xF_FFFF_FFFF_FFFFL) | (1L << 52);
            var shift = (int)((bits >> 52) & 0x7FF) - 1075;
            digits = new string(DigitsOf(new BigInteger((ulong)mantissa) << shift, radix));
        }

        return negative ? "-" + digits : digits;
    }

    private static char DigitCharacter(int digit) => (char)(digit < 10 ? '0' + digit : 'a' + digit - 10);

    private static char[] DigitsOf(BigInteger value, int radix)
    {
        var buffer = new char[1100]; // 2^1024 needs 1025 digits in base 2
        var index = buffer.Length;
        var divisor = new BigInteger(radix);
        while (!value.IsZero)
        {
            value = BigInteger.DivRem(value, divisor, out var remainder);
            buffer[--index] = DigitCharacter((int)remainder);
        }

        return buffer[index..];
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
