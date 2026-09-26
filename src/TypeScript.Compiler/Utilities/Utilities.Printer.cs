using System.Globalization;
using System.Text.RegularExpressions;

namespace TypeScript.Compiler;

// ported from: src/compiler/utilities.ts:1062-1064 (isFileLevelUniqueName), :1971-1978 (GetLiteralTextFlags),
//               :1980-2033 (getLiteralText, canUseOriginalText), :2061-2063 (makeIdentifierFromModuleName),
//               :6140-6142 (escapeTemplateSubstitution), :6216-6258 (escapeNonAsciiString, escapeJsxAttributeString).
//
// PORT-NOTE: why these members of `utilities.ts` live in a file of their own, added with the printer's first milestone
// (M7-0): the printer's closure reads exactly these — `getLiteralTextOfNode` (emitter.ts:5269) calls `getLiteralText` and
// the three escapers, and the NameGenerator (emitter.ts:5520/5687) calls `isFileLevelUniqueName` and
// `makeIdentifierFromModuleName` — and no slice that landed before it declared any of them. The repository's rule for that
// is "a member whose faithful body needs a helper nothing has ported brings that helper with it, each with its own
// `ported from` line" (docs/architecture.md §15.9, the same note `Program/Utilities.Program.Nodes.cs` carries). They are
// `utilities.ts` free functions, so they land on the `Utilities` partial — beside `EscapeString` (utilities.ts:6207,
// stated in `Utilities/Utilities.Checker.cs`), which two of them call. `GetLiteralTextFlags` is hand-written in
// `utilities.ts` rather than generated from `types.ts`, so it is stated here rather than in `Model/`.
internal static partial class Utilities
{
    /// <summary>ported from: src/compiler/utilities.ts:1062-1064 — <c>isFileLevelUniqueName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's third parameter is the optional <c>hasGlobalName</c> print handler; the port takes the same
    /// callback, and a null one answers exactly like the reference's <c>undefined</c> (<c>!(hasGlobalName &amp;&amp; …)</c>
    /// is true). The reference's <c>sourceFile.identifiers</c> is a <c>Map&lt;string, string&gt;</c> and the port's slot is
    /// <c>Dictionary&lt;string, string&gt;?</c> (§12.3), whose <c>ContainsKey</c> is the same membership test.
    /// </remarks>
    public static bool IsFileLevelUniqueName(SourceFile sourceFile, string name, Func<string?, bool>? hasGlobalName = null)
        => !(hasGlobalName is not null && hasGlobalName(name)) && !(sourceFile.Identifiers?.ContainsKey(name) ?? false);

    /// <summary>ported from: src/compiler/utilities.ts:2061-2063 — <c>makeIdentifierFromModuleName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>getBaseFileName</c> is the port's <c>Paths.GetBaseFileName</c>. The reference's two global
    /// <c>String.prototype.replace</c> calls become <c>Regex.Replace</c>; <c>/\W/</c> is written out as
    /// <c>[^A-Za-z0-9_]</c> because JS's <c>\w</c> is exactly that ASCII set while .NET's <c>\w</c> also accepts every
    /// Unicode letter and digit, which would keep characters the reference replaces.
    /// </remarks>
    public static string MakeIdentifierFromModuleName(string moduleName)
        => Regex.Replace(Regex.Replace(Paths.GetBaseFileName(moduleName), @"^(\d)", "_$1"), @"[^A-Za-z0-9_]", "_");

    /// <summary>ported from: src/compiler/utilities.ts:6140-6142 — <c>escapeTemplateSubstitution</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's global regexp <c>/\$\{/g</c> matches the two-character sequence <c>${</c> and its
    /// replacement string <c>"\\${"</c> is the literal <c>\${</c> (neither engine's replacement syntax gives <c>$</c> a
    /// meaning before <c>{</c>), so the port is a plain string replacement.
    /// </remarks>
    public static string EscapeTemplateSubstitution(string str)
        => str.Replace("${", "\\${");

    /// <summary>ported from: src/compiler/utilities.ts:6216-6223 — <c>escapeNonAsciiString</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference reuses <c>escapeString</c> and then its module-level <c>/[^\u0000-\u007F]/g</c>; the port
    /// calls <c>Utilities.EscapeString</c> (same member, utilities.ts:6207) and replaces one code unit at a time through
    /// <c>EncodeUtf16EscapeSequence</c> — the same helper <c>escapeString</c>'s own replacement uses, so the escapes agree.
    /// </remarks>
    public static string EscapeNonAsciiString(string s, int? quoteChar = null)
    {
        s = EscapeString(s, quoteChar);
        // Replace non-ASCII characters with '\uNNNN' escapes if any exist.
        // Otherwise just return the original string.
        return NonAsciiCharacters.IsMatch(s)
            ? NonAsciiCharacters.Replace(s, static c => EncodeUtf16EscapeSequence(c.Value[0]))
            : s;
    }

    /// <summary>ported from: src/compiler/utilities.ts:6249-6258 — <c>escapeJsxAttributeString</c>.</summary>
    public static string EscapeJsxAttributeString(string s, int? quoteChar = null)
    {
        var escapedCharsRegExp = quoteChar == CharacterCodes.SingleQuote
            ? JsxSingleQuoteEscapedCharsRegExp
            : JsxDoubleQuoteEscapedCharsRegExp;
        return escapedCharsRegExp.Replace(s, static c => GetJsxAttributeStringReplacement(c.Value));
    }

    /// <summary>ported from: src/compiler/utilities.ts:6225 — <c>nonAsciiCharacters</c>.</summary>
    private static readonly Regex NonAsciiCharacters = new(@"[^\u0000-\u007F]", RegexOptions.Compiled);

    /// <summary>ported from: src/compiler/utilities.ts:6229 — <c>jsxDoubleQuoteEscapedCharsRegExp</c>.</summary>
    private static readonly Regex JsxDoubleQuoteEscapedCharsRegExp = new("[\"\u0000-\u001f\u2028\u2029\u0085]", RegexOptions.Compiled);

    /// <summary>ported from: src/compiler/utilities.ts:6230 — <c>jsxSingleQuoteEscapedCharsRegExp</c>.</summary>
    private static readonly Regex JsxSingleQuoteEscapedCharsRegExp = new("['\u0000-\u001f\u2028\u2029\u0085]", RegexOptions.Compiled);

    /// <summary>ported from: src/compiler/utilities.ts:6231-6234 — <c>jsxEscapedCharsMap</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS builds this from <c>Object.entries</c>; the port spells out the same two entries.
    /// </remarks>
    private static readonly Dictionary<string, string> JsxEscapedCharsMap = new()
    {
        ["\""] = "&quot;",
        ["'"] = "&apos;",
    };

    /// <summary>ported from: src/compiler/utilities.ts:6236-6239 — <c>encodeJsxCharacterEntity</c>.</summary>
    private static string EncodeJsxCharacterEntity(int charCode)
        => "&#x" + charCode.ToString("X", CultureInfo.InvariantCulture) + ";";

    /// <summary>ported from: src/compiler/utilities.ts:6241-6246 — <c>getJsxAttributeStringReplacement</c>.</summary>
    private static string GetJsxAttributeStringReplacement(string c)
    {
        if (c[0] == CharacterCodes.NullCharacter)
        {
            return "&#0;";
        }
        return JsxEscapedCharsMap.TryGetValue(c, out var escaped) ? escaped : EncodeJsxCharacterEntity(c[0]);
    }

    /// <summary>ported from: src/compiler/utilities.ts:1980-2033 — <c>getLiteralText</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's parameter is <c>LiteralLikeNode</c>, whose port face is <c>ILiteralLikeNode</c>; its
    /// <c>canUseOriginalText</c> / <c>getSourceTextOfNodeFromSourceFile</c> / <c>getEmitFlags</c> calls all take a
    /// <c>Node</c>, so the two places that need one cast the interface to the class every implementer of it derives from.
    /// The <c>isUnterminated</c> tests are <c>== true</c> because the port's slot is <c>bool?</c> and the reference only
    /// ever sets it to <c>true</c>. The failure arm's message prints the kind the way the reference does — as its numeric
    /// value (<c>node.kind</c> in a template literal) — not <c>Debug.FormatSyntaxKind</c>'s name.
    /// </remarks>
    public static string GetLiteralText(ILiteralLikeNode node, SourceFile? sourceFile, GetLiteralTextFlags flags)
    {
        // If we don't need to downlevel and we can reach the original source text using
        // the node's parent reference, then simply get the text as it was originally written.
        if (sourceFile is not null && CanUseOriginalText(node, flags))
        {
            return GetSourceTextOfNodeFromSourceFile(sourceFile, (Node)node);
        }

        // If we can't reach the original source text, use the canonical form if it's a number,
        // or a (possibly escaped) quoted form of the original text if it's string-like.
        switch (node.Kind)
        {
            case SyntaxKind.StringLiteral:
                {
                    // PORT-NOTE: the reference picks one of three functions into `const escapeText`; the port's local
                    // states the delegate type because a conditional of method groups has no natural type under `var`.
                    Func<string, int?, string> escapeText = (flags & GetLiteralTextFlags.JsxAttributeEscape) != 0
                        ? EscapeJsxAttributeString
                        : (flags & GetLiteralTextFlags.NeverAsciiEscape) != 0 || (GetEmitFlags((Node)node) & EmitFlags.NoAsciiEscaping) != 0
                            ? EscapeString
                            : EscapeNonAsciiString;
                    if (((StringLiteral)node).SingleQuote == true)
                    {
                        return "'" + escapeText(node.Text!, CharacterCodes.SingleQuote) + "'";
                    }
                    else
                    {
                        return "\"" + escapeText(node.Text!, CharacterCodes.DoubleQuote) + "\"";
                    }
                }
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.TemplateHead:
            case SyntaxKind.TemplateMiddle:
            case SyntaxKind.TemplateTail:
                {
                    // If a NoSubstitutionTemplateLiteral appears to have a substitution in it, the original text
                    // had to include a backslash: `not \${a} substitution`.
                    Func<string, int?, string> escapeText = (flags & GetLiteralTextFlags.NeverAsciiEscape) != 0 || (GetEmitFlags((Node)node) & EmitFlags.NoAsciiEscaping) != 0
                        ? EscapeString
                        : EscapeNonAsciiString;

                    var rawText = ((ITemplateLiteralLikeNode)node).RawText ?? EscapeTemplateSubstitution(escapeText(node.Text!, CharacterCodes.Backtick));
                    switch (node.Kind)
                    {
                        case SyntaxKind.NoSubstitutionTemplateLiteral:
                            return "`" + rawText + "`";
                        case SyntaxKind.TemplateHead:
                            return "`" + rawText + "${";
                        case SyntaxKind.TemplateMiddle:
                            return "}" + rawText + "${";
                        case SyntaxKind.TemplateTail:
                            return "}" + rawText + "`";
                    }
                    break;
                }
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
                return node.Text!;
            case SyntaxKind.RegularExpressionLiteral:
                if ((flags & GetLiteralTextFlags.TerminateUnterminatedLiterals) != 0 && node.IsUnterminated == true)
                {
                    return node.Text! + (node.Text![^1] == CharacterCodes.Backslash ? " /" : "/");
                }
                return node.Text!;
        }

        return Debug.Fail<string>($"Literal kind '{(int)node.Kind}' not accounted for.");
    }

    /// <summary>ported from: src/compiler/utilities.ts:2036-2049 — <c>canUseOriginalText</c>.</summary>
    public static bool CanUseOriginalText(ILiteralLikeNode node, GetLiteralTextFlags flags)
    {
        if (NodeIsSynthesized((Node)node) || ((Node)node).Parent is null || ((flags & GetLiteralTextFlags.TerminateUnterminatedLiterals) != 0 && node.IsUnterminated == true))
        {
            return false;
        }

        if (NodeTests.IsNumericLiteral((Node)node))
        {
            var numericLiteralFlags = ((NumericLiteral)node).NumericLiteralFlags;
            if ((numericLiteralFlags & TokenFlags.IsInvalid) != 0)
            {
                return false;
            }
            if ((numericLiteralFlags & TokenFlags.ContainsSeparator) != 0)
            {
                return (flags & GetLiteralTextFlags.AllowNumericSeparator) != 0;
            }
        }

        return !NodeTests.IsBigIntLiteral((Node)node);
    }
}

/// <summary>ported from: src/compiler/utilities.ts:1971-1978 — <c>GetLiteralTextFlags</c>.</summary>
[Flags]
internal enum GetLiteralTextFlags
{
    None = 0,
    NeverAsciiEscape = 1 << 0,
    JsxAttributeEscape = 1 << 1,
    TerminateUnterminatedLiterals = 1 << 2,
    AllowNumericSeparator = 1 << 3,
}
