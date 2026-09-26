using System.Globalization;
using System.Text.RegularExpressions;

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/jsx.ts:554-681 (the JSX text, the tag and attribute names, the JSX child
//             expression) and :683-937 (the module-level `entities` map).
/// <summary>
/// The text half of the JSX transform: <c>visitJsxText</c>'s whitespace re-flow and entity decoding, the
/// <c>tagName</c> and attribute-name spellings, the JSX child expression, and the entity table <c>decodeEntities</c>
/// reads.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (the entity regular expression): the reference's <c>/&amp;((#((\d+)|x([\da-fA-F]+)))|(\w+));/g</c> is
/// stated with the ASCII classes JS gives <c>\d</c> and <c>\w</c> spelled out, because .NET's <c>\d</c>/<c>\w</c> are
/// Unicode by default and would admit digits and word characters JS leaves alone. The three groups
/// <c>decodeEntities</c> reads keep the reference's numbering and are named for it.
/// </para>
/// <para>
/// PORT-NOTE (<c>isStringDoubleQuoted</c>): <c>utilities.ts:3917-3919</c> is not ported; its one-line body is stated
/// here from the ported <c>Utilities.GetSourceTextOfNodeFromSourceFile</c>, beside its only reader. Its home is
/// <c>Utilities</c>, next to the landed <c>IsSingleOrDoubleQuote</c>; this slice's report names the move.
/// </para>
/// <para>
/// PORT-NOTE (<c>/^[A-Z_]\w*$/i</c>): <c>getAttributeName</c>'s test is written as the loop below rather than a
/// <see cref="Regex"/>, because .NET's <c>$</c> also matches before a trailing newline and its <c>\w</c> is Unicode —
/// the reference's test is exactly "an ASCII letter or <c>_</c>, then ASCII word characters to the end".
/// </para>
/// <para>
/// PORT-NOTE (<c>String.fromCharCode</c>/<c>String.fromCodePoint</c>): every decoded code point goes through
/// <see cref="ScannerUtilities.Utf16EncodeAsString"/>, the port's already-landed spelling of the reference's
/// <c>utf16EncodeAsString</c> (scanner.ts:4066-4068); a numeric entity above <c>0x10FFFF</c> therefore fails exactly as
/// the reference fails.
/// </para>
/// </remarks>
internal sealed partial class JsxTransformer
{
    // ---------------------------------------------------------------------------------------------
    // jsx.ts:554-643 — the JSX text and the entities
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/jsx.ts:554-557 — <c>visitJsxText</c>.</summary>
    private StringLiteral? VisitJsxText(JsxText node)
    {
        var fixedText = FixupWhitespaceAndDecodeEntities(node.Text!);
        return fixedText is null ? null : _factory.CreateStringLiteral(fixedText);
    }

    /// <summary>ported from: src/compiler/transformers/jsx.ts:574-610 — <c>fixupWhitespaceAndDecodeEntities</c>.</summary>
    /// <remarks>
    /// <para>
    /// JSX trims whitespace at the end and beginning of lines, except that the start/end of a tag is considered a
    /// start/end of a line only if that line is on the same line as the closing tag. See examples in
    /// <c>tests/cases/conformance/jsx/tsxReactEmitWhitespace.tsx</c>.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS reads <c>text.charCodeAt(i)</c>, whose value is the UTF-16 code unit; the port's <c>text[i]</c> is
    /// the same code unit, and both character tests take it as a number. <c>text.substr(start, length)</c> is
    /// <see cref="string.Substring(int,int)"/> and <c>text.substr(start)</c> is the range from <c>start</c>.
    /// </para>
    /// </remarks>
    private static string? FixupWhitespaceAndDecodeEntities(string text)
    {
        string? acc = null;
        // First non-whitespace character on this line.
        var firstNonWhitespace = 0;
        // Last non-whitespace character on this line.
        var lastNonWhitespace = -1;
        // These initial values are special because the first line is:
        // firstNonWhitespace = 0 to indicate that we want leading whitespace,
        // but lastNonWhitespace = -1 as a special flag to indicate that we *don't* include the line if it's all whitespace.

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (ScannerUtilities.IsLineBreak(c))
            {
                // If we've seen any non-whitespace characters on this line, add the 'trim' of the line.
                // (lastNonWhitespace === -1 is a special flag to detect whether the first line is all whitespace.)
                if (firstNonWhitespace != -1 && lastNonWhitespace != -1)
                {
                    acc = AddLineOfJsxText(acc, text.Substring(firstNonWhitespace, lastNonWhitespace - firstNonWhitespace + 1));
                }

                // Reset firstNonWhitespace for the next line.
                // Don't bother to reset lastNonWhitespace because we ignore it if firstNonWhitespace = -1.
                firstNonWhitespace = -1;
            }
            else if (!ScannerUtilities.IsWhiteSpaceSingleLine(c))
            {
                lastNonWhitespace = i;
                if (firstNonWhitespace == -1)
                {
                    firstNonWhitespace = i;
                }
            }
        }

        return firstNonWhitespace != -1
            // Last line had a non-whitespace character. Emit the 'trimLeft', meaning keep trailing whitespace.
            ? AddLineOfJsxText(acc, text[firstNonWhitespace..])
            // Last line was all whitespace, so ignore it
            : acc;
    }

    /// <summary>ported from: src/compiler/transformers/jsx.ts:612-617 — <c>addLineOfJsxText</c>.</summary>
    private static string AddLineOfJsxText(string? acc, string trimmedLine)
    {
        // We do not escape the string here as that is handled by the printer
        // when it emits the literal. We do, however, need to decode JSX entities.
        var decoded = DecodeEntities(trimmedLine);
        return acc is null ? decoded : acc + " " + decoded;
    }

    /// <summary>ported from: src/compiler/transformers/jsx.ts:619-638 — <c>decodeEntities</c>.</summary>
    /// <remarks>
    /// <para>
    /// Replace entities like "&amp;nbsp;", "&amp;#123;", and "&amp;#xDEADBEEF;" with the characters they encode.
    /// See https://en.wikipedia.org/wiki/List_of_XML_and_HTML_character_entity_references
    /// </para>
    /// <para>
    /// PORT-NOTE: the reference's replacer tests the two capture strings for truthiness; the port tests the groups for a
    /// match, which is the same answer (a <c>+</c> repetition never matches empty), and its <c>entities.get(word)</c>
    /// test is the port's <c>ch != 0</c> — no entity in the table is 0, so "found" and "non-zero" coincide.
    /// </para>
    /// </remarks>
    private static string DecodeEntities(string text)
        => JsxEntityRegExp().Replace(text, match =>
        {
            var decimalDigits = match.Groups["decimal"];
            if (decimalDigits.Success)
            {
                return ScannerUtilities.Utf16EncodeAsString(ParseEntityNumber(decimalDigits.Value, 10));
            }

            var hexDigits = match.Groups["hex"];
            if (hexDigits.Success)
            {
                return ScannerUtilities.Utf16EncodeAsString(ParseEntityNumber(hexDigits.Value, 16));
            }

            var ch = s_entities.GetValueOrDefault(match.Groups["word"].Value);
            // If this is not a valid entity, then just use `match` (replace it with itself, i.e. don't replace)
            return ch != 0 ? ScannerUtilities.Utf16EncodeAsString(ch) : match.Value;
        });

    /// <summary>ported from: src/compiler/transformers/jsx.ts:640-643 — <c>tryDecodeEntities</c>.</summary>
    /// <remarks>PORT-NOTE: like <c>decodeEntities</c> but answers <c>null</c> if there were no entities to decode.</remarks>
    private static string? TryDecodeEntities(string text)
    {
        var decoded = DecodeEntities(text);
        return decoded == text ? null : decoded;
    }

    /// <summary>ported from: src/compiler/transformers/jsx.ts:623 — the <c>decodeEntities</c> regular expression.</summary>
    /// <remarks>PORT-NOTE: see the class header for the two ASCII digit/word classes and the three group names.</remarks>
    [GeneratedRegex(@"&((#((?<decimal>[0-9]+)|x(?<hex>[0-9a-fA-F]+)))|(?<word>[A-Za-z0-9_]+));")]
    private static partial Regex JsxEntityRegExp();

    /// <summary>The <c>parseInt(digits, radix)</c> the reference's <c>decodeEntities</c> calls.</summary>
    /// <remarks>
    /// PORT-NOTE: the two digit classes the regular expression admits are ASCII, so these two parses answer the
    /// reference's own values for every entity it survives. A value above <c>0x10FFFF</c> is refused by
    /// <see cref="ScannerUtilities.Utf16EncodeAsString"/> exactly as <c>String.fromCodePoint</c> refuses it, and digits
    /// too long for <see cref="int"/> fail the parse where <c>parseInt</c> would have answered a <c>double</c> the same
    /// refusal rejects — both throw.
    /// </remarks>
    private static int ParseEntityNumber(string digits, int radix)
        => radix == 16
            ? Convert.ToInt32(digits, 16)
            : int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------------------------------------
    // jsx.ts:645-680 — the names and the JSX child expression
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/jsx.ts:645-666 — <c>getTagName</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is the <c>JsxElement | JsxOpeningLikeElement</c> union with no C# type, so the
    /// port takes the <see cref="Node"/> and reads the union's <c>tagName</c> through <see cref="TagNameOf"/>.</remarks>
    private Expression GetTagName(Node node)
    {
        if (node.Kind == SyntaxKind.JsxElement)
        {
            return GetTagName(((JsxElement)node).OpeningElement!);
        }

        var tagName = TagNameOf(node);
        if (NodeTests.IsIdentifier(tagName) && Utilities.IsIntrinsicJsxName(((Identifier)tagName).EscapedText!))
        {
            return _factory.CreateStringLiteral(Utilities.IdText(tagName));
        }

        if (NodeTests.IsJsxNamespacedName(tagName))
        {
            var namespacedName = (JsxNamespacedName)tagName;
            return _factory.CreateStringLiteral(Utilities.IdText(namespacedName.Namespace!) + ":" + Utilities.IdText(namespacedName.Name!));
        }

        return FactoryUtilities.CreateExpressionFromEntityName(_factory, tagName);
    }

    /// <summary>ported from: src/compiler/transformers/jsx.ts:668-675 — <c>getAttributeName</c>.</summary>
    /// <remarks>
    /// Emit an attribute name, which is quoted if it needs to be quoted. Because these emit into an object literal
    /// property name, we don't need to be worried about keywords, just non-identifier characters.
    /// <para>
    /// PORT-NOTE: TS's answer is the <c>StringLiteral | Identifier</c> union; the port answers the <see cref="Node"/>
    /// both are, which is what <c>CreatePropertyAssignment</c>'s <c>name</c> slot takes.
    /// </para>
    /// </remarks>
    private Node GetAttributeName(JsxAttribute node)
    {
        var name = node.Name!;
        if (NodeTests.IsIdentifier(name))
        {
            var text = Utilities.IdText(name);
            return IsIdentifierShapedAttributeName(text) ? name : _factory.CreateStringLiteral(text);
        }

        var namespacedName = (JsxNamespacedName)name;
        return _factory.CreateStringLiteral(Utilities.IdText(namespacedName.Namespace!) + ":" + Utilities.IdText(namespacedName.Name!));
    }

    /// <summary>ported from: src/compiler/transformers/jsx.ts:677-680 — <c>visitJsxExpression</c>.</summary>
    private Expression? VisitJsxExpression(JsxExpression node)
    {
        var expression = Utilities.VisitNode(node.Expression, Visit, Utilities.IsExpression);
        return node.DotDotDotToken is not null ? _factory.CreateSpreadElement(expression!) : expression;
    }

    /// <summary>The reference's <c>/^[A-Z_]\w*$/i</c> over an attribute name, with JS's ASCII <c>\w</c>.</summary>
    private static bool IsIdentifierShapedAttributeName(string text)
    {
        if (text.Length == 0)
        {
            return false;
        }

        var first = text[0];
        if (!((first >= 'A' && first <= 'Z') || (first >= 'a' && first <= 'z') || first == '_'))
        {
            return false;
        }

        for (var i = 1; i < text.Length; i++)
        {
            var c = text[i];
            if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_'))
            {
                return false;
            }
        }

        return true;
    }

    // ---------------------------------------------------------------------------------------------
    // jsx.ts:683-937 — the entities map
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/jsx.ts:683-937 — the module-level <c>entities</c> map.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>new Map(Object.entries({ … }))</c> is keyed by name and read once per
    /// entity reference; the port keeps the same names and code points in a dictionary with an ordinal comparer (the
    /// names are ASCII).</remarks>
    private static readonly Dictionary<string, int> s_entities = new(StringComparer.Ordinal)
    {
        ["quot"] = 0x0022,
        ["amp"] = 0x0026,
        ["apos"] = 0x0027,
        ["lt"] = 0x003C,
        ["gt"] = 0x003E,
        ["nbsp"] = 0x00A0,
        ["iexcl"] = 0x00A1,
        ["cent"] = 0x00A2,
        ["pound"] = 0x00A3,
        ["curren"] = 0x00A4,
        ["yen"] = 0x00A5,
        ["brvbar"] = 0x00A6,
        ["sect"] = 0x00A7,
        ["uml"] = 0x00A8,
        ["copy"] = 0x00A9,
        ["ordf"] = 0x00AA,
        ["laquo"] = 0x00AB,
        ["not"] = 0x00AC,
        ["shy"] = 0x00AD,
        ["reg"] = 0x00AE,
        ["macr"] = 0x00AF,
        ["deg"] = 0x00B0,
        ["plusmn"] = 0x00B1,
        ["sup2"] = 0x00B2,
        ["sup3"] = 0x00B3,
        ["acute"] = 0x00B4,
        ["micro"] = 0x00B5,
        ["para"] = 0x00B6,
        ["middot"] = 0x00B7,
        ["cedil"] = 0x00B8,
        ["sup1"] = 0x00B9,
        ["ordm"] = 0x00BA,
        ["raquo"] = 0x00BB,
        ["frac14"] = 0x00BC,
        ["frac12"] = 0x00BD,
        ["frac34"] = 0x00BE,
        ["iquest"] = 0x00BF,
        ["Agrave"] = 0x00C0,
        ["Aacute"] = 0x00C1,
        ["Acirc"] = 0x00C2,
        ["Atilde"] = 0x00C3,
        ["Auml"] = 0x00C4,
        ["Aring"] = 0x00C5,
        ["AElig"] = 0x00C6,
        ["Ccedil"] = 0x00C7,
        ["Egrave"] = 0x00C8,
        ["Eacute"] = 0x00C9,
        ["Ecirc"] = 0x00CA,
        ["Euml"] = 0x00CB,
        ["Igrave"] = 0x00CC,
        ["Iacute"] = 0x00CD,
        ["Icirc"] = 0x00CE,
        ["Iuml"] = 0x00CF,
        ["ETH"] = 0x00D0,
        ["Ntilde"] = 0x00D1,
        ["Ograve"] = 0x00D2,
        ["Oacute"] = 0x00D3,
        ["Ocirc"] = 0x00D4,
        ["Otilde"] = 0x00D5,
        ["Ouml"] = 0x00D6,
        ["times"] = 0x00D7,
        ["Oslash"] = 0x00D8,
        ["Ugrave"] = 0x00D9,
        ["Uacute"] = 0x00DA,
        ["Ucirc"] = 0x00DB,
        ["Uuml"] = 0x00DC,
        ["Yacute"] = 0x00DD,
        ["THORN"] = 0x00DE,
        ["szlig"] = 0x00DF,
        ["agrave"] = 0x00E0,
        ["aacute"] = 0x00E1,
        ["acirc"] = 0x00E2,
        ["atilde"] = 0x00E3,
        ["auml"] = 0x00E4,
        ["aring"] = 0x00E5,
        ["aelig"] = 0x00E6,
        ["ccedil"] = 0x00E7,
        ["egrave"] = 0x00E8,
        ["eacute"] = 0x00E9,
        ["ecirc"] = 0x00EA,
        ["euml"] = 0x00EB,
        ["igrave"] = 0x00EC,
        ["iacute"] = 0x00ED,
        ["icirc"] = 0x00EE,
        ["iuml"] = 0x00EF,
        ["eth"] = 0x00F0,
        ["ntilde"] = 0x00F1,
        ["ograve"] = 0x00F2,
        ["oacute"] = 0x00F3,
        ["ocirc"] = 0x00F4,
        ["otilde"] = 0x00F5,
        ["ouml"] = 0x00F6,
        ["divide"] = 0x00F7,
        ["oslash"] = 0x00F8,
        ["ugrave"] = 0x00F9,
        ["uacute"] = 0x00FA,
        ["ucirc"] = 0x00FB,
        ["uuml"] = 0x00FC,
        ["yacute"] = 0x00FD,
        ["thorn"] = 0x00FE,
        ["yuml"] = 0x00FF,
        ["OElig"] = 0x0152,
        ["oelig"] = 0x0153,
        ["Scaron"] = 0x0160,
        ["scaron"] = 0x0161,
        ["Yuml"] = 0x0178,
        ["fnof"] = 0x0192,
        ["circ"] = 0x02C6,
        ["tilde"] = 0x02DC,
        ["Alpha"] = 0x0391,
        ["Beta"] = 0x0392,
        ["Gamma"] = 0x0393,
        ["Delta"] = 0x0394,
        ["Epsilon"] = 0x0395,
        ["Zeta"] = 0x0396,
        ["Eta"] = 0x0397,
        ["Theta"] = 0x0398,
        ["Iota"] = 0x0399,
        ["Kappa"] = 0x039A,
        ["Lambda"] = 0x039B,
        ["Mu"] = 0x039C,
        ["Nu"] = 0x039D,
        ["Xi"] = 0x039E,
        ["Omicron"] = 0x039F,
        ["Pi"] = 0x03A0,
        ["Rho"] = 0x03A1,
        ["Sigma"] = 0x03A3,
        ["Tau"] = 0x03A4,
        ["Upsilon"] = 0x03A5,
        ["Phi"] = 0x03A6,
        ["Chi"] = 0x03A7,
        ["Psi"] = 0x03A8,
        ["Omega"] = 0x03A9,
        ["alpha"] = 0x03B1,
        ["beta"] = 0x03B2,
        ["gamma"] = 0x03B3,
        ["delta"] = 0x03B4,
        ["epsilon"] = 0x03B5,
        ["zeta"] = 0x03B6,
        ["eta"] = 0x03B7,
        ["theta"] = 0x03B8,
        ["iota"] = 0x03B9,
        ["kappa"] = 0x03BA,
        ["lambda"] = 0x03BB,
        ["mu"] = 0x03BC,
        ["nu"] = 0x03BD,
        ["xi"] = 0x03BE,
        ["omicron"] = 0x03BF,
        ["pi"] = 0x03C0,
        ["rho"] = 0x03C1,
        ["sigmaf"] = 0x03C2,
        ["sigma"] = 0x03C3,
        ["tau"] = 0x03C4,
        ["upsilon"] = 0x03C5,
        ["phi"] = 0x03C6,
        ["chi"] = 0x03C7,
        ["psi"] = 0x03C8,
        ["omega"] = 0x03C9,
        ["thetasym"] = 0x03D1,
        ["upsih"] = 0x03D2,
        ["piv"] = 0x03D6,
        ["ensp"] = 0x2002,
        ["emsp"] = 0x2003,
        ["thinsp"] = 0x2009,
        ["zwnj"] = 0x200C,
        ["zwj"] = 0x200D,
        ["lrm"] = 0x200E,
        ["rlm"] = 0x200F,
        ["ndash"] = 0x2013,
        ["mdash"] = 0x2014,
        ["lsquo"] = 0x2018,
        ["rsquo"] = 0x2019,
        ["sbquo"] = 0x201A,
        ["ldquo"] = 0x201C,
        ["rdquo"] = 0x201D,
        ["bdquo"] = 0x201E,
        ["dagger"] = 0x2020,
        ["Dagger"] = 0x2021,
        ["bull"] = 0x2022,
        ["hellip"] = 0x2026,
        ["permil"] = 0x2030,
        ["prime"] = 0x2032,
        ["Prime"] = 0x2033,
        ["lsaquo"] = 0x2039,
        ["rsaquo"] = 0x203A,
        ["oline"] = 0x203E,
        ["frasl"] = 0x2044,
        ["euro"] = 0x20AC,
        ["image"] = 0x2111,
        ["weierp"] = 0x2118,
        ["real"] = 0x211C,
        ["trade"] = 0x2122,
        ["alefsym"] = 0x2135,
        ["larr"] = 0x2190,
        ["uarr"] = 0x2191,
        ["rarr"] = 0x2192,
        ["darr"] = 0x2193,
        ["harr"] = 0x2194,
        ["crarr"] = 0x21B5,
        ["lArr"] = 0x21D0,
        ["uArr"] = 0x21D1,
        ["rArr"] = 0x21D2,
        ["dArr"] = 0x21D3,
        ["hArr"] = 0x21D4,
        ["forall"] = 0x2200,
        ["part"] = 0x2202,
        ["exist"] = 0x2203,
        ["empty"] = 0x2205,
        ["nabla"] = 0x2207,
        ["isin"] = 0x2208,
        ["notin"] = 0x2209,
        ["ni"] = 0x220B,
        ["prod"] = 0x220F,
        ["sum"] = 0x2211,
        ["minus"] = 0x2212,
        ["lowast"] = 0x2217,
        ["radic"] = 0x221A,
        ["prop"] = 0x221D,
        ["infin"] = 0x221E,
        ["ang"] = 0x2220,
        ["and"] = 0x2227,
        ["or"] = 0x2228,
        ["cap"] = 0x2229,
        ["cup"] = 0x222A,
        ["int"] = 0x222B,
        ["there4"] = 0x2234,
        ["sim"] = 0x223C,
        ["cong"] = 0x2245,
        ["asymp"] = 0x2248,
        ["ne"] = 0x2260,
        ["equiv"] = 0x2261,
        ["le"] = 0x2264,
        ["ge"] = 0x2265,
        ["sub"] = 0x2282,
        ["sup"] = 0x2283,
        ["nsub"] = 0x2284,
        ["sube"] = 0x2286,
        ["supe"] = 0x2287,
        ["oplus"] = 0x2295,
        ["otimes"] = 0x2297,
        ["perp"] = 0x22A5,
        ["sdot"] = 0x22C5,
        ["lceil"] = 0x2308,
        ["rceil"] = 0x2309,
        ["lfloor"] = 0x230A,
        ["rfloor"] = 0x230B,
        ["lang"] = 0x2329,
        ["rang"] = 0x232A,
        ["loz"] = 0x25CA,
        ["spades"] = 0x2660,
        ["clubs"] = 0x2663,
        ["hearts"] = 0x2665,
        ["diams"] = 0x2666,
    };
}

