using System.Text;

namespace TypeScript.Compiler.Tools;

/// <summary>One member of a TS interface.</summary>
internal sealed record TsMember(
    string Name,
    /// <summary>Raw TS type expression, not yet mapped to C#.</summary>
    string Type,
    bool IsReadonly,
    bool IsOptional,
    string? Doc,
    string? TrailingComment);

/// <summary>A parsed <c>export interface</c> declaration.</summary>
internal sealed class TsInterface
{
    public required string Name { get; init; }

    /// <summary>Generic parameter names, e.g. <c>["TKind"]</c> for <c>Token&lt;TKind&gt;</c>.</summary>
    public required IReadOnlyList<string> TypeParameters { get; init; }

    /// <summary>Raw base type expressions, in source order.</summary>
    public required IReadOnlyList<string> Extends { get; init; }

    public required IReadOnlyList<TsMember> Members { get; init; }

    public required int StartLine { get; init; }

    public required int EndLine { get; init; }

    /// <summary>
    /// True when the interface pins its own <c>kind</c>, e.g. <c>readonly kind: SyntaxKind.Identifier</c>.
    /// Those are the concrete syntax nodes; everything else is a capability or categorisation interface.
    /// </summary>
    public required bool HasConcreteKind { get; init; }

    /// <summary>The pinned <c>SyntaxKind</c> member when <see cref="HasConcreteKind"/>.</summary>
    public required string? ConcreteKind { get; init; }
}

/// <summary>Parses the <c>export interface</c> declarations out of a TS source file.</summary>
/// <remarks>
/// Line-oriented like <see cref="TsEnumParser"/>, but with one extra rule: a member's type expression may
/// span lines (long unions), so member text accumulates until the terminating <c>;</c>. Anything that does
/// not fit the understood shapes aborts generation instead of producing a half-correct model.
/// </remarks>
internal static class TsInterfaceParser
{
    public static List<TsInterface> Parse(string sourceText)
    {
        // TS occasionally closes one declaration and opens the next on the same line
        // (`… } export interface X …`). Split those apart before the line-oriented pass.
        var normalized = s_declarationBoundary.Replace(sourceText.Replace("\r\n", "\n"), "}\n");
        var lines = SplitInlineInterfaces(normalized.Split('\n'));
        var results = new List<TsInterface>();

        for (var i = 0; i < lines.Length; i++)
        {
            var header = TsDeclarations.ParseInterfaceHeader(lines[i]);
            if (header is null) continue;

            var startLine = i;
            string? pendingDoc = null;

            // A doc comment may sit on the lines immediately above the declaration.
            for (var back = startLine - 1; back >= 0 && back >= startLine - 12; back--)
            {
                var previous = lines[back].Trim();
                if (previous.Length == 0) continue;
                if (previous.StartsWith("//", StringComparison.Ordinal)) continue;
                if (previous.EndsWith("*/", StringComparison.Ordinal))
                {
                    // Collect the whole block going upwards.
                    var begin = back;
                    while (begin > 0 && !lines[begin].Contains("/*", StringComparison.Ordinal)) begin--;
                    pendingDoc = TsComments.Clean(string.Join("\n", lines[begin..(back + 1)]));
                }

                break;
            }

            var members = new List<TsMember>();
            var endLine = -1;
            var depth = 0;

            for (i++; i < lines.Length; i++)
            {
                var raw = lines[i];
                var line = raw.Trim();
                if (line.Length == 0) continue;

                if (line.StartsWith("//", StringComparison.Ordinal)) continue;

                if (line.StartsWith("/*", StringComparison.Ordinal))
                {
                    var (doc, remainder) = TsComments.ReadBlock(lines, ref i, raw);
                    line = remainder.Trim();
                    if (line.Length == 0)
                    {
                        pendingDoc = doc;
                        continue;
                    }

                    pendingDoc = doc;
                }

                if (depth == 0 && line == "}")
                {
                    endLine = i;
                    break;
                }

                // Skip members whose type we cannot model (index signatures, call/construct signatures);
                // they exist on a handful of interfaces and are handled by hand.
                if (depth == 0 && (line.StartsWith('[') || line.StartsWith('(') || line.StartsWith("new ")))
                {
                    i = SkipToSemicolon(lines, i, ref depth);
                    pendingDoc = null;
                    continue;
                }

                if (depth == 0 && !LooksLikeMember(line))
                {
                    // e.g. a stray `>` from a multi-line generic base; not a member.
                    continue;
                }

                if (depth != 0 || line.StartsWith('}'))
                {
                    // Continuation of a previous member's type (nested object literal).
                    depth += CountBraces(line);
                    pendingDoc = null;
                    continue;
                }

                var (member, consumedTo) = ReadMember(lines, i, pendingDoc);
                pendingDoc = null;
                if (member is null)
                {
                    continue;
                }

                members.Add(member);
                i = consumedTo;
            }

            if (endLine < 0) endLine = i;

            var concreteKind = members
                .FirstOrDefault(static m => m.Name == "kind" && m.Type.StartsWith("SyntaxKind.", StringComparison.Ordinal))
                ?.Type["SyntaxKind.".Length..];

            results.Add(new TsInterface
            {
                Name = header.Value.Name,
                TypeParameters = header.Value.TypeParameters,
                Extends = header.Value.Extends,
                Members = members,
                StartLine = startLine + 1,
                EndLine = endLine + 1,
                HasConcreteKind = concreteKind is not null,
                ConcreteKind = concreteKind,
            });
        }

        return results;
    }

    /// <summary>
    /// Rewrites one-line interfaces (<c>export interface X extends Y { readonly a: T; }</c>) into the
    /// multi-line shape the parser otherwise assumes, so their single member is not swallowed into the
    /// previous interface's last member type.
    /// </summary>
    private static string[] SplitInlineInterfaces(string[] lines)
    {
        if (!lines.Any(static l => s_inlineInterface.IsMatch(l))) return lines;

        var result = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            var match = s_inlineInterface.Match(line);
            if (!match.Success)
            {
                result.Add(line);
                continue;
            }

            result.Add(match.Groups[1].Value.TrimEnd());
            var body = match.Groups[2].Value.Trim();
            if (body.Length != 0) result.Add("    " + body);
            result.Add("}");
        }

        return [.. result];
    }

    private static readonly System.Text.RegularExpressions.Regex s_inlineInterface = new(
        @"^(\s*export\s+interface\s+[^{]+\{)\s*(.*?)\s*\}\s*$",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex s_declarationBoundary = new(
        @"\}\s+(?=export\s)",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    private static bool LooksLikeMember(string line)
        => line.Length > 0 && (char.IsLetter(line[0]) || line[0] == '_' || line[0] == '$' || line[0] == '"');

    private static (TsMember? Member, int LastLine) ReadMember(string[] lines, int start, string? doc)
    {
        var accumulated = new StringBuilder();
        string? trailing = null;
        var lastLine = start;

        for (var i = start; i < lines.Length; i++)
        {
            var (rawCode, comment) = SplitComment(lines[i]);
            if (comment.Length != 0) trailing = trailing is null ? comment : trailing + " " + comment;

            var code = rawCode.Trim();

            // The member ended on a previous line: TS allows omitting the trailing semicolon, so the only
            // reliable terminator is "the next line is a new member or the closing brace".
            if (accumulated.Length != 0 && (code.Length == 0 || code.StartsWith('}'))) break;

            if (code.Length != 0)
            {
                if (accumulated.Length != 0) accumulated.Append(' ');
                accumulated.Append(code);
                lastLine = i;
            }

            if (code.EndsWith(';')) break;

            var next = NextSignificantLine(lines, i + 1);
            if (next < 0) break;

            var nextCode = lines[next].Trim();
            if (!nextCode.StartsWith('|') && !nextCode.StartsWith('&') && !nextCode.StartsWith(','))
            {
                break;
            }
        }

        var text = accumulated.ToString().TrimEnd(';').Trim();
        if (text.Length == 0) return (null, lastLine);

        var colon = FindTopLevelColon(text);
        if (colon < 0) return (null, lastLine);

        var namePart = text[..colon].Trim();
        var typePart = text[(colon + 1)..].Trim();

        var isReadonly = false;
        if (namePart.StartsWith("readonly ", StringComparison.Ordinal))
        {
            isReadonly = true;
            namePart = namePart["readonly ".Length..].Trim();
        }

        var isOptional = namePart.EndsWith('?');
        if (isOptional) namePart = namePart[..^1].Trim();

        if (namePart.Length == 0) return (null, lastLine);

        return (new TsMember(namePart, typePart, isReadonly, isOptional, doc, trailing), lastLine);
    }

    private static (string Code, string Comment) SplitComment(string line)
    {
        var index = line.IndexOf("//", StringComparison.Ordinal);
        return index < 0 ? (line, "") : (line[..index], line[(index + 2)..].Trim());
    }

    private static int NextSignificantLine(string[] lines, int from)
    {
        for (var i = from; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0) continue;
            if (trimmed.StartsWith("//", StringComparison.Ordinal)) continue;
            return i;
        }

        return -1;
    }

    /// <summary>Finds the <c>:</c> that separates a member name from its type, ignoring nested brackets.</summary>
    private static int FindTopLevelColon(string text)
    {
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '{' or '(' or '[' or '<':
                    depth++;
                    break;
                case '}' or ')' or ']' or '>':
                    depth--;
                    break;
                case ':' when depth == 0:
                    return i;
            }
        }

        return -1;
    }

    private static int CountBraces(string line)
    {
        var depth = 0;
        foreach (var c in line)
        {
            if (c == '{') depth++;
            else if (c == '}') depth--;
        }

        return depth;
    }

    private static int SkipToSemicolon(string[] lines, int start, ref int depth)
    {
        for (var i = start; i < lines.Length; i++)
        {
            depth += CountBraces(lines[i]);
            if (depth <= 0 && lines[i].TrimEnd().EndsWith(';')) return i;
        }

        return start;
    }
}
