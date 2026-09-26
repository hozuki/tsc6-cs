using System.Globalization;
using System.Text;

namespace TypeScript.Compiler.Tools;

/// <summary>Decodes TS string literals and re-emits them as C# literals.</summary>
/// <remarks>
/// The two languages do not share escape syntax: C#'s <c>\x</c> is variable-length while JS's is exactly
/// two digits, and JS accepts <c>\'</c> and <c>\0</c> in ways C# does not. Instead of copying the raw text
/// (which would silently change values), the literal is decoded to its actual characters and re-encoded
/// with C# escapes. An escape this generator does not understand aborts generation rather than guessing.
/// </remarks>
internal static class TsStringLiteral
{
    public static bool TryParse(string expression, out string value)
    {
        value = string.Empty;
        if (expression.Length < 2) return false;

        var quote = expression[0];
        if (quote != '"' && quote != '\'') return false;
        if (expression[^1] != quote) return false;

        var sb = new StringBuilder(expression.Length);
        for (var i = 1; i < expression.Length - 1; i++)
        {
            var c = expression[i];
            if (c != '\\')
            {
                sb.Append(c);
                continue;
            }

            i++;
            if (i >= expression.Length - 1) return false;

            switch (expression[i])
            {
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'v': sb.Append('\v'); break;
                case '0': sb.Append('\0'); break;
                case '\\': sb.Append('\\'); break;
                case '\'': sb.Append('\''); break;
                case '"': sb.Append('"'); break;
                case 'u':
                    if (i + 4 >= expression.Length) return false;
                    if (!ushort.TryParse(expression.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var u))
                    {
                        return false;
                    }

                    sb.Append((char)u);
                    i += 4;
                    break;
                case 'x':
                    if (i + 2 >= expression.Length) return false;
                    if (!byte.TryParse(expression.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var x))
                    {
                        return false;
                    }

                    sb.Append((char)x);
                    i += 2;
                    break;
                default:
                    // Unknown escape: fail loudly rather than emit a wrong value.
                    return false;
            }
        }

        value = sb.ToString();
        return true;
    }

    public static string ToCSharp(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\0': sb.Append("\\0"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (char.IsControl(c))
                    {
                        sb.Append($"\\u{(int)c:X4}");
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        return sb.Append('"').ToString();
    }
}
