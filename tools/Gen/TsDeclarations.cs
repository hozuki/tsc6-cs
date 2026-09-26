using System.Text.RegularExpressions;

namespace TypeScript.Compiler.Tools;

/// <summary>The pieces of an <c>export interface X&lt;T&gt; extends A, B {</c> header.</summary>
internal readonly record struct InterfaceHeader(
    string Name,
    IReadOnlyList<string> TypeParameters,
    IReadOnlyList<string> Extends);

/// <summary>The pieces of an <c>export type X&lt;T&gt; = …;</c> declaration.</summary>
internal readonly record struct TypeAliasHeader(
    string Name,
    IReadOnlyList<string> TypeParameters,
    string Value);

/// <summary>Recognises the TS declaration headers the generators care about.</summary>
internal static class TsDeclarations
{
    private static readonly Regex s_interface = new(
        @"^export\s+interface\s+(\w+)\s*(?:<([^>]*)>)?\s*(?:extends\s+(.+?))?\s*\{$",
        RegexOptions.Compiled);

    public static InterfaceHeader? ParseInterfaceHeader(string rawLine)
    {
        var line = rawLine.Trim();
        if (!line.StartsWith("export interface ", StringComparison.Ordinal)) return null;

        var match = s_interface.Match(line);
        if (!match.Success) return null;

        return new InterfaceHeader(
            match.Groups[1].Value,
            SplitTypeParameters(match.Groups[2].Success ? match.Groups[2].Value : null),
            SplitList(match.Groups[3].Success ? match.Groups[3].Value : null));
    }

    /// <summary>
    /// Matches a single-line <c>export type X = …;</c>. Multi-line aliases (long unions) are returned with
    /// whatever was on the header line and are expected to be reassembled by the caller.
    /// </summary>
    /// <remarks>
    /// Parsed by hand rather than by regex: a generic parameter list may contain nested angle brackets
    /// (<c>export type A&lt;T extends Array&lt;Y&gt;&gt; = …</c>), which a <c>[^&gt;]*</c> pattern truncates
    /// and silently drops the alias.
    /// </remarks>
    public static TypeAliasHeader? ParseTypeAliasHeader(string rawLine)
    {
        const string prefix = "export type ";
        var line = rawLine.Trim();
        if (!line.StartsWith(prefix, StringComparison.Ordinal)) return null;

        var rest = line[prefix.Length..];

        var nameEnd = 0;
        while (nameEnd < rest.Length && (char.IsLetterOrDigit(rest[nameEnd]) || rest[nameEnd] == '_'))
        {
            nameEnd++;
        }

        if (nameEnd == 0) return null;

        var name = rest[..nameEnd];
        rest = rest[nameEnd..].TrimStart();

        var typeParameters = new List<string>();
        if (rest.StartsWith('<'))
        {
            var depth = 0;
            var end = -1;
            for (var i = 0; i < rest.Length; i++)
            {
                if (rest[i] == '<') depth++;
                else if (rest[i] == '>' && --depth == 0)
                {
                    end = i;
                    break;
                }
            }

            if (end < 0) return null;

            typeParameters = SplitTypeParameters(rest[1..end]);
            rest = rest[(end + 1)..].TrimStart();
        }

        if (!rest.StartsWith('=')) return null;

        return new TypeAliasHeader(name, typeParameters, rest[1..].Trim());
    }

    /// <summary>Splits <c>"TKind extends SyntaxKind"</c> into the bare parameter names.</summary>
    private static List<string> SplitTypeParameters(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];

        return SplitList(raw)
            .Select(static p =>
            {
                var name = p;
                var extendsAt = name.IndexOf(" extends ", StringComparison.Ordinal);
                if (extendsAt >= 0) name = name[..extendsAt];
                var defaultAt = name.IndexOf('=');
                if (defaultAt >= 0) name = name[..defaultAt];
                return name.Trim();
            })
            .Where(static p => p.Length != 0)
            .ToList();
    }

    /// <summary>Splits a comma-separated list, ignoring commas nested in <c>&lt;&gt;</c>, <c>()</c>, <c>{}</c> or <c>[]</c>.</summary>
    public static List<string> SplitList(string? raw)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(raw)) return result;

        var depth = 0;
        var start = 0;
        for (var i = 0; i < raw.Length; i++)
        {
            switch (raw[i])
            {
                case '<' or '(' or '[' or '{':
                    depth++;
                    break;
                case '>' or ')' or ']' or '}':
                    depth--;
                    break;
                case ',' when depth == 0:
                    Add(raw[start..i]);
                    start = i + 1;
                    break;
            }
        }

        Add(raw[start..]);
        return result;

        void Add(string piece)
        {
            var trimmed = piece.Trim();
            if (trimmed.Length != 0) result.Add(trimmed);
        }
    }
}
