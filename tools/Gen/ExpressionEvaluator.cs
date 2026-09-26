namespace TypeScript.Compiler.Tools;

/// <summary>
/// Evaluates and re-renders the value expressions found in TS <c>const enum</c> bodies: identifiers,
/// decimal/hex/binary literals, and the bitwise operators <c>| ^ &amp; &lt;&lt; &gt;&gt; ~</c>.
/// </summary>
/// <remarks>
/// Two jobs, one parse:
/// <list type="bullet">
/// <item><see cref="Evaluate"/> produces the numeric value — needed to pick the enum's underlying type
/// and to detect out-of-range members.</item>
/// <item><see cref="Rewrite"/> produces an equivalent <em>C#</em> constant expression. Keeping the
/// expression (instead of collapsing it to <c>0xFFFF7FFF</c>) keeps the generated enum readable next to
/// the TS source.</item>
/// </list>
/// <para>
/// C# enums have no unary <c>~</c>, and — a subtler point — an enum <em>member initializer</em> must be a
/// constant expression of the <em>underlying type</em>, not of the enum type. So a "clear these bits"
/// expression is rendered as <c>(uint)All &amp; ~(uint)Decorator</c>: every operand is converted down, and
/// the whole thing stays <c>uint</c>. (An expression that merely combines members, like <c>A | B</c>, is
/// fine as-is because C# defines the bitwise operators on enums.)
/// </para>
/// </remarks>
internal static class ExpressionEvaluator
{
    /// <summary>Evaluates a TS enum value expression to its numeric value.</summary>
    public static long Evaluate(string expression, IReadOnlyDictionary<string, long> members)
        => new Parser(expression, members, underlyingType: null, castIdentifiers: false).ParseOr().Value;

    /// <summary>Rewrites a TS enum value expression into an equivalent C# constant expression.</summary>
    /// <param name="underlyingType">The enum's underlying integer type (<c>int</c>/<c>uint</c>/<c>ulong</c>).</param>
    public static string Rewrite(
        string expression,
        IReadOnlyDictionary<string, long> members,
        string underlyingType)
    {
        // `~` is the only operator that forces operands to the underlying type; expressions built purely
        // from `|`/`&`/`^` stay in the enum's own type, which C# accepts in a member initializer.
        var parser = new Parser(expression, members, underlyingType, castIdentifiers: expression.Contains('~'));
        var (value, text) = parser.ParseOr();
        parser.ExpectEnd();

        // Negative results (TS writes `-1` for "all bits") cannot be folded into an unsigned member
        // initializer without an explicit unchecked conversion.
        if (value < 0 && underlyingType is "uint" or "ulong")
        {
            return $"unchecked(({underlyingType}){text})";
        }

        return text;
    }

    private sealed class Parser(
        string text,
        IReadOnlyDictionary<string, long> members,
        string? underlyingType,
        bool castIdentifiers)
    {
        private int _pos;

        /// <summary>Integer suffix applied to literals so they fit the enum's underlying type.</summary>
        private string Suffix => underlyingType switch
        {
            "uint" => "u",
            "ulong" => "ul",
            _ => "",
        };

        public (long Value, string Text) ParseOr()
        {
            var left = ParseXor();
            while (Consume('|'))
            {
                var right = ParseXor();
                left = (left.Value | right.Value, $"{left.Text} | {right.Text}");
            }

            return left;
        }

        private (long Value, string Text) ParseXor()
        {
            var left = ParseAnd();
            while (Consume('^'))
            {
                var right = ParseAnd();
                left = (left.Value ^ right.Value, $"{left.Text} ^ {right.Text}");
            }

            return left;
        }

        private (long Value, string Text) ParseAnd()
        {
            var left = ParseShift();
            while (Consume('&'))
            {
                var right = ParseShift();
                left = (left.Value & right.Value, $"{left.Text} & {right.Text}");
            }

            return left;
        }

        private (long Value, string Text) ParseShift()
        {
            var left = ParseUnary();
            while (true)
            {
                // The shift count must stay an `int` in C# (`uint << uint` does not exist), so the right
                // operand is rendered without any conversion or suffix.
                if (Consume("<<"))
                {
                    var right = ParseCount();
                    left = (left.Value << (int)right.Value, $"{left.Text} << {right.Text}");
                }
                else if (Consume(">>"))
                {
                    var right = ParseCount();
                    left = (left.Value >> (int)right.Value, $"{left.Text} >> {right.Text}");
                }
                else
                {
                    return left;
                }
            }
        }

        /// <summary>Parses a shift count: a plain integer, with neither a suffix nor an identifier cast.</summary>
        private (long Value, string Text) ParseCount()
        {
            SkipWhitespace();
            var start = _pos;
            while (_pos < text.Length && char.IsDigit(text[_pos])) _pos++;
            if (_pos == start) throw Error("expected a numeric shift count");

            var token = text[start.._pos];
            return (long.Parse(token, System.Globalization.CultureInfo.InvariantCulture), token);
        }

        private (long Value, string Text) ParseUnary(bool allowSuffix = true)
        {
            if (Consume('~'))
            {
                var operand = ParseUnary(allowSuffix);
                return (~operand.Value, $"~{operand.Text}");
            }

            if (Consume('-'))
            {
                // A negative literal must stay signed so the caller can wrap it in `unchecked((uint)…)`.
                var operand = ParseUnary(allowSuffix: false);
                return (-operand.Value, $"-{operand.Text}");
            }

            if (Consume('+')) return ParseUnary(allowSuffix);

            return ParsePrimary(allowSuffix);
        }

        private (long Value, string Text) ParsePrimary(bool allowSuffix)
        {
            SkipWhitespace();

            if (Consume('('))
            {
                var inner = ParseOr();
                if (!Consume(')')) throw Error("expected ')'");
                return (inner.Value, $"({inner.Text})");
            }

            var start = _pos;
            while (_pos < text.Length && (char.IsLetterOrDigit(text[_pos]) || text[_pos] is '_' or '$'))
            {
                _pos++;
            }

            if (_pos == start) throw Error("expected a literal or identifier");

            var token = text[start.._pos];
            if (char.IsDigit(token[0]))
            {
                var number = ParseNumber(token);
                return (number, allowSuffix ? token + Suffix : token);
            }

            if (!members.TryGetValue(token, out var value))
            {
                throw Error($"unknown identifier '{token}' (only members of the same enum are known)");
            }

            return (value, castIdentifiers ? $"({underlyingType}){token}" : token);
        }

        private static long ParseNumber(string token)
        {
            if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return Convert.ToInt64(token[2..], 16);
            }

            if (token.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
            {
                return Convert.ToInt64(token[2..], 2);
            }

            if (token.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
            {
                return Convert.ToInt64(token[2..], 8);
            }

            return long.Parse(token, System.Globalization.CultureInfo.InvariantCulture);
        }

        public void ExpectEnd()
        {
            SkipWhitespace();
            if (_pos != text.Length) throw Error($"unexpected trailing text '{text[_pos..]}'");
        }

        private bool Consume(char c)
        {
            SkipWhitespace();
            if (_pos < text.Length && text[_pos] == c)
            {
                _pos++;
                return true;
            }

            return false;
        }

        private bool Consume(string s)
        {
            SkipWhitespace();
            if (_pos + s.Length <= text.Length && text.AsSpan(_pos, s.Length).SequenceEqual(s))
            {
                _pos += s.Length;
                return true;
            }

            return false;
        }

        private void SkipWhitespace()
        {
            while (_pos < text.Length && char.IsWhiteSpace(text[_pos])) _pos++;
        }

        private InvalidOperationException Error(string message)
            => new($"while reading `{text}`: {message} (at offset {_pos})");
    }
}
