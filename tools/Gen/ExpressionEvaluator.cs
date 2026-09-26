namespace TypeScript.Compiler.Tools;

/// <summary>
/// Evaluates the value expressions found in TS <c>const enum</c> bodies: identifiers, decimal/hex/binary
/// literals, and the bitwise operators <c>| ^ &amp; &lt;&lt; &gt;&gt; ~</c>.
/// </summary>
/// <remarks>
/// PORT-NOTE: JS bitwise operators coerce operands to 32-bit integers. Evaluation here uses
/// <see cref="long"/> and normalises the result with <c>&amp; 0xFFFFFFFF</c>, which reproduces the 32-bit
/// result for the "set/clear a flag" shapes the compiler's flag enums use (e.g. <c>All &amp; ~Decorator</c>).
/// </remarks>
internal static class ExpressionEvaluator
{
    public static long Evaluate(string expression, IReadOnlyDictionary<string, long> members)
    {
        var parser = new Parser(expression, members);
        var value = parser.ParseOr();
        parser.ExpectEnd();
        return value & 0xFFFFFFFFL;
    }

    private sealed class Parser(string text, IReadOnlyDictionary<string, long> members)
    {
        private int _pos;

        public long ParseOr()
        {
            var left = ParseXor();
            while (Consume('|'))
            {
                left |= ParseXor();
            }

            return left;
        }

        private long ParseXor()
        {
            var left = ParseAnd();
            while (Consume('^'))
            {
                left ^= ParseAnd();
            }

            return left;
        }

        private long ParseAnd()
        {
            var left = ParseShift();
            while (Consume('&'))
            {
                left &= ParseShift();
            }

            return left;
        }

        private long ParseShift()
        {
            var left = ParseUnary();
            while (true)
            {
                if (Consume("<<"))
                {
                    left <<= (int)ParseUnary();
                }
                else if (Consume(">>"))
                {
                    left >>= (int)ParseUnary();
                }
                else
                {
                    return left;
                }
            }
        }

        private long ParseUnary()
        {
            if (Consume('~')) return ~ParseUnary();
            if (Consume('-')) return -ParseUnary();
            if (Consume('+')) return ParseUnary();
            return ParsePrimary();
        }

        private long ParsePrimary()
        {
            SkipWhitespace();

            if (Consume('('))
            {
                var inner = ParseOr();
                if (!Consume(')')) throw Error("expected ')'");
                return inner;
            }

            var start = _pos;
            while (_pos < text.Length && (char.IsLetterOrDigit(text[_pos]) || text[_pos] is '_' or '$'))
            {
                _pos++;
            }

            if (_pos == start) throw Error("expected a literal or identifier");

            var token = text[start.._pos];
            if (char.IsDigit(token[0])) return ParseNumber(token);
            if (!members.TryGetValue(token, out var value))
            {
                throw Error($"unknown identifier '{token}' (only members of the same enum are known)");
            }

            return value;
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
            => new($"while evaluating `{text}`: {message} (at offset {_pos})");
    }
}
