using System.Text;

namespace TypeScript.Compiler.Tools;

/// <summary>One member of a TS <c>const enum</c>.</summary>
internal sealed record TsEnumMember(
    string Name,
    /// <summary>The raw value expression as written in TS, or <c>null</c> for implicit increment.</summary>
    string? ValueExpression,
    /// <summary>Doc text from a leading <c>/** … */</c> (without the delimiters).</summary>
    string? Doc,
    /// <summary>Text after a trailing <c>//</c>.</summary>
    string? TrailingComment)
{
    /// <summary>Evaluated numeric value.</summary>
    public long Value { get; init; }

    /// <summary>True when the value expression is a bare reference to another member.</summary>
    public bool IsAlias { get; init; }
}

/// <summary>A parsed TS <c>const enum</c> with evaluated member values.</summary>
internal sealed class TsEnum
{
    public required string Name { get; init; }

    /// <summary>1-based line of the <c>export const enum …</c> header, for provenance comments.</summary>
    public required int StartLine { get; init; }

    /// <summary>1-based line of the closing brace.</summary>
    public required int EndLine { get; init; }

    public required List<TsEnumMember> Members { get; init; }

    public long MaxValue => Members.Count == 0 ? 0 : Members.Max(static m => m.Value);
}

/// <summary>
/// Parses <c>export const enum NAME { … }</c> from a TS source file and evaluates member values.
/// </summary>
/// <remarks>
/// Deliberately narrow: it understands the shapes TS uses for the data tables we generate (member name,
/// optional <c>=</c> with a value expression, leading <c>/** … */</c> doc, trailing <c>//</c>). Anything
/// outside that shape fails loudly rather than producing a half-correct table.
/// </remarks>
internal static class TsEnumParser
{
    public static TsEnum Parse(string sourceText, string enumName)
    {
        var lines = sourceText.Replace("\r\n", "\n").Split('\n');
        var header = $"export const enum {enumName}";

        var headerLine = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith(header, StringComparison.Ordinal)) continue;
            var after = lines[i].AsSpan(header.Length).TrimStart();
            if (after.Length == 0 || after[0] == '{')
            {
                headerLine = i;
                break;
            }
        }

        if (headerLine < 0)
        {
            throw new InvalidOperationException($"Could not find `{header}` in the TS source.");
        }

        // The body is flat (none of the enums we generate nest braces), so the first line whose trimmed
        // content is exactly "}" terminates it.
        var closeLine = -1;
        for (var i = headerLine + 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "}")
            {
                closeLine = i;
                break;
            }
        }

        if (closeLine < 0)
        {
            throw new InvalidOperationException($"Could not find the closing brace of `{enumName}`.");
        }

        var members = new List<TsEnumMember>();
        var values = new Dictionary<string, long>(StringComparer.Ordinal);
        long nextImplicit = 0;
        string? pendingDoc = null;

        for (var i = headerLine + 1; i < closeLine; i++)
        {
            var raw = lines[i];
            var line = raw.Trim();
            if (line.Length == 0) continue;

            // Whole-line `//` comments carry no information we keep.
            if (line.StartsWith("//", StringComparison.Ordinal)) continue;

            // A block comment may be standalone, may share the line with its member, and may span lines.
            if (line.StartsWith("/*", StringComparison.Ordinal))
            {
                var (doc, remainder) = ReadBlockComment(lines, ref i, closeLine, raw);
                line = remainder.Trim();
                if (line.Length == 0)
                {
                    pendingDoc = doc;
                    continue;
                }

                pendingDoc = doc;
            }

            // A member's value expression may span several physical lines (e.g. NodeCheckFlags.LazyFlags is
            // one long `A | B | …` chain). Accumulate until the logical member is terminated by a comma,
            // then take it apart.
            var accumulated = new StringBuilder();
            string? trailing = null;
            var current = line;
            while (true)
            {
                var commentIndex = current.IndexOf("//", StringComparison.Ordinal);
                string code;
                if (commentIndex >= 0)
                {
                    var comment = current[(commentIndex + 2)..].Trim();
                    if (comment.Length != 0)
                    {
                        trailing = trailing is null ? comment : trailing + " " + comment;
                    }

                    code = current[..commentIndex].Trim();
                }
                else
                {
                    code = current.Trim();
                }

                if (accumulated.Length != 0 && code.Length != 0) accumulated.Append(' ');
                accumulated.Append(code);

                if (code.EndsWith(',')) break;
                if (i + 1 >= closeLine) break; // last member, no trailing comma

                i++;
                current = lines[i];
            }

            var memberText = accumulated.ToString().TrimEnd(',').Trim();
            if (memberText.Length == 0) continue;

            var eq = memberText.IndexOf('=');
            string name;
            string? valueExpression;
            if (eq < 0)
            {
                name = memberText;
                valueExpression = null;
            }
            else
            {
                name = memberText[..eq].Trim();
                valueExpression = memberText[(eq + 1)..].Trim();
            }

            if (!IsIdentifier(name))
            {
                throw new InvalidOperationException(
                    $"Unsupported member syntax at {enumName} line {i + 1}: {raw.Trim()}");
            }

            long value;
            var isAlias = false;
            if (valueExpression is null)
            {
                value = nextImplicit;
            }
            else
            {
                value = ExpressionEvaluator.Evaluate(valueExpression, values);
                isAlias = IsIdentifier(valueExpression);
            }

            nextImplicit = value + 1;
            values[name] = value;
            members.Add(new TsEnumMember(name, valueExpression, pendingDoc, trailing)
            {
                Value = value,
                IsAlias = isAlias,
            });
            pendingDoc = null;
        }

        return new TsEnum
        {
            Name = enumName,
            StartLine = headerLine + 1,
            EndLine = closeLine + 1,
            Members = members,
        };
    }

    /// <summary>
    /// Reads a <c>/* … */</c> block starting at line <paramref name="i"/> (which contains the opener),
    /// advancing <paramref name="i"/> past the line holding the terminator.
    /// </summary>
    private static (string Doc, string Remainder) ReadBlockComment(string[] lines, ref int i, int limit, string firstLine)
    {
        var sb = new StringBuilder();
        sb.Append(firstLine);

        while (true)
        {
            var end = lines[i].IndexOf("*/", StringComparison.Ordinal);
            if (end >= 0)
            {
                var remainder = lines[i][(end + 2)..];
                return (CleanDoc(sb.ToString()), remainder);
            }

            if (i >= limit)
            {
                throw new InvalidOperationException($"Unterminated block comment starting at line {i + 1}.");
            }

            i++;
            sb.Append('\n').Append(lines[i]);
        }
    }

    /// <summary>Strips the delimiters and decorative asterisks from a doc block, collapsing it to one line.</summary>
    private static string CleanDoc(string block)
    {
        var body = block;

        var start = body.IndexOf("/*", StringComparison.Ordinal);
        if (start >= 0) body = body[(start + 2)..];

        var end = body.IndexOf("*/", StringComparison.Ordinal);
        if (end >= 0) body = body[..end];

        return string.Join(
            " ",
            body.Split('\n')
                .Select(static l => l.Trim().TrimStart('*').Trim())
                .Where(static l => l.Length != 0));
    }

    private static bool IsIdentifier(string text)
    {
        if (text.Length == 0) return false;
        if (!char.IsLetter(text[0]) && text[0] != '_' && text[0] != '$') return false;
        return text.All(static c => char.IsLetterOrDigit(c) || c is '_' or '$');
    }
}
