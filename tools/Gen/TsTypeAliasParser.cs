using System.Text;

namespace TypeScript.Compiler.Tools;

/// <summary>A parsed <c>export type X = …;</c> declaration.</summary>
internal sealed class TsTypeAlias
{
    public required string Name { get; init; }

    public required IReadOnlyList<string> TypeParameters { get; init; }

    /// <summary>The raw type expression, with line breaks folded into single spaces.</summary>
    public required string Value { get; init; }

    public required int StartLine { get; init; }

    public required int EndLine { get; init; }
}

/// <summary>Parses the <c>export type</c> aliases out of a TS source file.</summary>
/// <remarks>
/// Aliases are routinely written as long multi-line unions, so the value accumulates until a
/// <c>;</c> that sits at bracket depth zero. Single-line aliases on a shared line
/// (<c>… }; export type Y = …;</c>) are split apart first, like the interface parser does.
/// </remarks>
internal static class TsTypeAliasParser
{
    public static List<TsTypeAlias> Parse(string sourceText)
    {
        var normalized = s_declarationBoundary.Replace(sourceText.Replace("\r\n", "\n"), ";\n");
        var lines = normalized.Split('\n');
        var results = new List<TsTypeAlias>();

        for (var i = 0; i < lines.Length; i++)
        {
            var header = TsDeclarations.ParseTypeAliasHeader(lines[i]);
            if (header is null) continue;

            var startLine = i;
            var value = header.Value.Value;
            var endLine = i;

            while (!IsTerminated(value) && i + 1 < lines.Length)
            {
                var next = TsComments.StripLineComment(lines[i + 1]).Trim();
                if (next.Length == 0)
                {
                    i++;
                    continue;
                }

                // TS allows omitting the trailing `;` on a type alias, so a "balanced and the next line is
                // not a continuation" test is needed as well — otherwise one unterminated alias swallows
                // every declaration that follows it.
                if (IsBalanced(value) && next[0] is not ('|' or '&' or ','))
                {
                    break;
                }

                i++;
                value += " " + next;
                endLine = i;
            }

            results.Add(new TsTypeAlias
            {
                Name = header.Value.Name,
                TypeParameters = header.Value.TypeParameters,
                Value = value.TrimEnd(';').Trim(),
                StartLine = startLine + 1,
                EndLine = endLine + 1,
            });
        }

        return results;
    }

    /// <summary>True once the accumulated text is a complete, bracket-balanced alias body.</summary>
    private static bool IsTerminated(string text) => text.TrimEnd().EndsWith(';') && IsBalanced(text);

    private static bool IsBalanced(string text)
    {
        var depth = 0;
        foreach (var c in text)
        {
            switch (c)
            {
                case '<' or '(' or '[' or '{':
                    depth++;
                    break;
                case '>' or ')' or ']' or '}':
                    depth--;
                    break;
            }
        }

        return depth <= 0;
    }

    private static readonly System.Text.RegularExpressions.Regex s_declarationBoundary = new(
        @";\s+(?=export\s)",
        System.Text.RegularExpressions.RegexOptions.Compiled);
}
