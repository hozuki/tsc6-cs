using System.Text.RegularExpressions;

namespace TypeScript.Compiler;

// ported from: src/compiler/scanner.ts:42-49, 358-638, 641-1000, 4034-4072
/// <summary>
/// The parts of <c>scanner.ts</c> that are not the scanner itself: spelling lookups, line/character arithmetic,
/// character classification and identifier tests.
/// </summary>
/// <remarks>
/// TS keeps all of this in the same module as <c>createScanner</c>; the port splits it into this class (pure
/// functions) and <see cref="Scanner"/> (the stateful scanner).
/// <para>
/// PORT-NOTE: TS 6.0 spells the <c>CharacterCodes</c> members in camelCase (<c>CharacterCodes.lineFeed</c>); the
/// port's hand-written table is PascalCase (a M1 naming decision), so the constants below are spelled that way.
/// </para>
/// </remarks>
internal static partial class ScannerUtilities
{
    // ---------------------------------------------------------------------------------------------
    // Spelling lookups
    // ---------------------------------------------------------------------------------------------

    private static readonly string?[] s_tokenStrings = BuildTokenStrings();

    private static readonly Dictionary<string, SyntaxKind> s_textToToken =
        ScannerTables.TextToToken.ToDictionary(static entry => entry.Text, static entry => entry.Kind, StringComparer.Ordinal);

    private static readonly Dictionary<string, SyntaxKind> s_textToKeyword =
        ScannerTables.TextToKeyword.ToDictionary(static entry => entry.Text, static entry => entry.Kind, StringComparer.Ordinal);

    private static readonly Dictionary<RegularExpressionFlags, int> s_regExpFlagToCharCode =
        ScannerTables.CharCodeToRegExpFlag.ToDictionary(static entry => entry.Flag, static entry => entry.CharCode);

    private static readonly Dictionary<int, RegularExpressionFlags> s_charCodeToRegExpFlag =
        ScannerTables.CharCodeToRegExpFlag.ToDictionary(static entry => entry.CharCode, static entry => entry.Flag);

    private static string?[] BuildTokenStrings()
    {
        // TS: `makeReverseMap(textToToken)` — an array indexed by token kind. The array is sparse in TS; here it
        // is sized to cover every kind, and a kind no spelling maps to stays null.
        var result = new string?[(int)SyntaxKind.Count + 1];
        foreach (var (text, kind) in ScannerTables.TextToToken)
        {
            result[(int)kind] = text;
        }

        return result;
    }

    // ported from: src/compiler/scanner.ts:42-45
    public static bool TokenIsIdentifierOrKeyword(SyntaxKind token) => token >= SyntaxKind.Identifier;

    // ported from: src/compiler/scanner.ts:47-49
    public static bool TokenIsIdentifierOrKeywordOrGreaterThan(SyntaxKind token)
        => token == SyntaxKind.GreaterThanToken || TokenIsIdentifierOrKeyword(token);

    // ported from: src/compiler/scanner.ts:413-421
    /// <summary>The spelling of a token kind, or <c>null</c> when it has none.</summary>
    public static string? TokenToString(SyntaxKind t) => (int)t < s_tokenStrings.Length ? s_tokenStrings[(int)t] : null;

    // ported from: src/compiler/scanner.ts:419-421
    /// <summary>The token a spelling denotes, or <c>null</c> when no token is spelled that way.</summary>
    public static SyntaxKind? StringToToken(string s) => s_textToToken.TryGetValue(s, out var token) ? token : null;

    /// <summary>ported from: src/compiler/scanner.ts:222 — TS: <c>textToKeyword</c>.</summary>
    public static SyntaxKind? StringToKeyword(string s) => s_textToKeyword.TryGetValue(s, out var token) ? token : null;

    // ported from: src/compiler/scanner.ts:426-428
    public static int? RegularExpressionFlagToCharacterCode(RegularExpressionFlags f) => s_regExpFlagToCharCode.TryGetValue(f, out var code) ? code : null;

    // ported from: src/compiler/scanner.ts:431-433
    public static RegularExpressionFlags? CharacterCodeToRegularExpressionFlag(int ch) => s_charCodeToRegExpFlag.TryGetValue(ch, out var flag) ? flag : null;

    // ---------------------------------------------------------------------------------------------
    // Lines and characters
    // ---------------------------------------------------------------------------------------------

    // ported from: src/compiler/scanner.ts:436-463
    /// <summary>The text offset each line starts at.</summary>
    public static List<int> ComputeLineStarts(string text)
    {
        var result = new List<int>();
        var pos = 0;
        var lineStart = 0;
        while (pos < text.Length)
        {
            var ch = (int)text[pos];
            pos++;
            switch (ch)
            {
                case CharacterCodes.CarriageReturn:
                    if (CharCodeAt(text, pos) == CharacterCodes.LineFeed)
                    {
                        pos++;
                    }

                    goto case CharacterCodes.LineFeed;
                case CharacterCodes.LineFeed:
                    result.Add(lineStart);
                    lineStart = pos;
                    break;
                default:
                    if (ch > CharacterCodes.MaxAsciiCharacter && IsLineBreak(ch))
                    {
                        result.Add(lineStart);
                        lineStart = pos;
                    }

                    break;
            }
        }

        result.Add(lineStart);
        return result;
    }

    // ported from: src/compiler/scanner.ts:465-472
    /// <summary>The offset of a line/character pair, going through the source file's own mapping when it has one.
    /// </summary>
    public static int GetPositionOfLineAndCharacter(ISourceFileLike sourceFile, int line, int character, bool allowEdits = false)
        => ComputePositionOfLineAndCharacter(GetLineStarts(sourceFile), line, character, sourceFile.Text, allowEdits);

    // ported from: src/compiler/scanner.ts:475-500
    public static int ComputePositionOfLineAndCharacter(
        IList<int> lineStarts,
        int line,
        int character,
        string? debugText = null,
        bool allowEdits = false)
    {
        if (line < 0 || line >= lineStarts.Count)
        {
            if (allowEdits)
            {
                // Clamp line to nearest allowable value
                line = line < 0 ? 0 : line >= lineStarts.Count ? lineStarts.Count - 1 : line;
            }
            else
            {
                Debug.Fail(
                    $"Bad line number. Line: {line}, lineStarts.length: {lineStarts.Count} , line map is correct? " +
                    $"{(debugText is not null ? Core.ArrayIsEqualTo(lineStarts, ComputeLineStarts(debugText)) : "unknown")}");
            }
        }

        var res = lineStarts[line] + character;
        if (allowEdits)
        {
            // Clamp to nearest allowable values to allow the underlying text to be edited without crashing
            // (accuracy is lost, instead).
            return res > lineStarts[line + 1] ? lineStarts[line + 1]
                : debugText is not null && res > debugText.Length ? debugText.Length
                : res;
        }

        if (line < lineStarts.Count - 1)
        {
            Debug.Assert(res < lineStarts[line + 1]);
        }
        else if (debugText is not null)
        {
            Debug.Assert(res <= debugText.Length); // Allow single character overflow for trailing newline
        }

        return res;
    }

    // ported from: src/compiler/scanner.ts:503-505
    public static IList<int> GetLineStarts(ISourceFileLike sourceFile)
        => sourceFile.LineMap ?? (sourceFile.LineMap = ComputeLineStarts(sourceFile.Text ?? string.Empty));

    // ported from: src/compiler/scanner.ts:508-514
    public static LineAndCharacter ComputeLineAndCharacterOfPosition(IList<int> lineStarts, int position)
    {
        var lineNumber = ComputeLineOfPosition(lineStarts, position);
        return new LineAndCharacter { Line = lineNumber, Character = position - lineStarts[lineNumber] };
    }

    // ported from: src/compiler/scanner.ts:520-534
    /// <summary>The line a position belongs to.</summary>
    /// <remarks>Assumes the first line starts at 0 and that <paramref name="position"/> is non-negative.</remarks>
    public static int ComputeLineOfPosition(IList<int> lineStarts, int position, int? lowerBound = null)
    {
        var lineNumber = Core.BinarySearch(
            lineStarts,
            position,
            Core.Identity,
            static (a, b) => Core.CompareValues(a, b),
            lowerBound ?? 0);
        if (lineNumber < 0)
        {
            // If the actual position was not found, the binary search returns the 2's-complement of the next
            // line start (for line starts [5, 10, 23, 80] and position 20 it returns -2). The index of the
            // previous line start is therefore one less.
            lineNumber = ~lineNumber - 1;
            Debug.Assert(lineNumber != -1, "position cannot precede the beginning of the file");
        }

        return lineNumber;
    }

    // ported from: src/compiler/scanner.ts:537-546
    public static int GetLinesBetweenPositions(ISourceFileLike sourceFile, int pos1, int pos2)
    {
        if (pos1 == pos2) return 0;
        var lineStarts = GetLineStarts(sourceFile);
        var lower = Math.Min(pos1, pos2);
        var isNegative = lower == pos2;
        var upper = isNegative ? pos1 : pos2;
        var lowerLine = ComputeLineOfPosition(lineStarts, lower);
        var upperLine = ComputeLineOfPosition(lineStarts, upper, lowerLine);
        return isNegative ? lowerLine - upperLine : upperLine - lowerLine;
    }

    // ported from: src/compiler/scanner.ts:548-550
    public static LineAndCharacter GetLineAndCharacterOfPosition(ISourceFileLike sourceFile, int position)
        => ComputeLineAndCharacterOfPosition(GetLineStarts(sourceFile), position);

    // ---------------------------------------------------------------------------------------------
    // Character classification
    // ---------------------------------------------------------------------------------------------

    // ported from: src/compiler/scanner.ts:552-554
    public static bool IsWhiteSpaceLike(int ch) => IsWhiteSpaceSingleLine(ch) || IsLineBreak(ch);

    // ported from: src/compiler/scanner.ts:557-572
    /// <summary>White space that is not a line break. For the other kind, see <see cref="IsWhiteSpaceLike"/>.
    /// </summary>
    public static bool IsWhiteSpaceSingleLine(int ch)
        // Note: nextLine is in the Zs space and should be considered whitespace. It is explicitly not a
        // line break, as it is not in the exact set specified by EcmaScript.
        => ch == CharacterCodes.Space ||
            ch == CharacterCodes.Tab ||
            ch == CharacterCodes.VerticalTab ||
            ch == CharacterCodes.FormFeed ||
            ch == CharacterCodes.NonBreakingSpace ||
            ch == CharacterCodes.NextLine ||
            ch == CharacterCodes.Ogham ||
            (ch >= CharacterCodes.EnQuad && ch <= CharacterCodes.ZeroWidthSpace) ||
            ch == CharacterCodes.NarrowNoBreakSpace ||
            ch == CharacterCodes.MathematicalSpace ||
            ch == CharacterCodes.IdeographicSpace ||
            ch == CharacterCodes.ByteOrderMark;

    // ported from: src/compiler/scanner.ts:574-590
    /// <summary>Whether a character code is one of ES5's four line terminators.</summary>
    /// <remarks>
    /// ES5 7.3, Table 3: line feed, carriage return, line separator and paragraph separator. Other
    /// line-breaking characters are white space but not line terminators.
    /// </remarks>
    public static bool IsLineBreak(int ch)
        => ch == CharacterCodes.LineFeed ||
            ch == CharacterCodes.CarriageReturn ||
            ch == CharacterCodes.LineSeparator ||
            ch == CharacterCodes.ParagraphSeparator;

    // ported from: src/compiler/scanner.ts:592-594
    public static bool IsDigit(int ch) => ch >= CharacterCodes._0 && ch <= CharacterCodes._9;

    // ported from: src/compiler/scanner.ts:596-598
    public static bool IsHexDigit(int ch) => IsDigit(ch) || (ch >= CharacterCodes.A && ch <= CharacterCodes.F) || (ch >= CharacterCodes.a && ch <= CharacterCodes.f);

    // ported from: src/compiler/scanner.ts:600-602
    public static bool IsASCIILetter(int ch) => (ch >= CharacterCodes.A && ch <= CharacterCodes.Z) || (ch >= CharacterCodes.a && ch <= CharacterCodes.z);

    // ported from: src/compiler/scanner.ts:605-607 — ES5 section 6.1.4.
    public static bool IsWordCharacter(int ch) => IsASCIILetter(ch) || IsDigit(ch) || ch == CharacterCodes.Underscore;

    // ported from: src/compiler/scanner.ts:609-611
    public static bool IsOctalDigit(int ch) => ch >= CharacterCodes._0 && ch <= CharacterCodes._7;

    // ported from: src/compiler/scanner.ts:613-638
    /// <summary>Whether the text at <paramref name="pos"/> can start trivia. Keep in sync with
    /// <c>Scanner.SkipTrivia</c>.</summary>
    public static bool CouldStartTrivia(string text, int pos)
    {
        var ch = CharCodeAt(text, pos);
        switch (ch)
        {
            case CharacterCodes.CarriageReturn:
            case CharacterCodes.LineFeed:
            case CharacterCodes.Tab:
            case CharacterCodes.VerticalTab:
            case CharacterCodes.FormFeed:
            case CharacterCodes.Space:
            case CharacterCodes.Slash:
            // starts of normal trivia
            case CharacterCodes.LessThan:
            case CharacterCodes.Bar:
            case CharacterCodes.EqualsSign:
            case CharacterCodes.GreaterThan:
                // Starts of conflict marker trivia
                return true;
            case CharacterCodes.Hash:
                // Only if its the beginning can we have #! trivia
                return pos == 0;
            default:
                return ch > CharacterCodes.MaxAsciiCharacter;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Identifiers
    // ---------------------------------------------------------------------------------------------

    // ported from: src/compiler/scanner.ts:973-976
    public static bool IsIdentifierStart(int ch, ScriptTarget? languageVersion)
        => IsASCIILetter(ch) || ch == CharacterCodes.Dollar || ch == CharacterCodes.Underscore ||
            (ch > CharacterCodes.MaxAsciiCharacter && IsUnicodeIdentifierStart(ch, languageVersion));

    // ported from: src/compiler/scanner.ts:978-984
    public static bool IsIdentifierPart(int ch, ScriptTarget? languageVersion, LanguageVariant identifierVariant = LanguageVariant.Standard)
        => IsWordCharacter(ch) || ch == CharacterCodes.Dollar ||
            // "-" and ":" are valid in JSX identifiers
            (identifierVariant == LanguageVariant.JSX && (ch == CharacterCodes.Minus || ch == CharacterCodes.Colon)) ||
            (ch > CharacterCodes.MaxAsciiCharacter && IsUnicodeIdentifierPart(ch, languageVersion));

    // ported from: src/compiler/scanner.ts:986-999
    public static bool IsIdentifierText(string name, ScriptTarget? languageVersion, LanguageVariant identifierVariant = LanguageVariant.Standard)
    {
        var ch = CodePointAt(name, 0);
        if (!IsIdentifierStart(ch, languageVersion)) return false;

        for (var i = CharSize(ch); i < name.Length; i += CharSize(ch))
        {
            ch = CodePointAt(name, i);
            if (!IsIdentifierPart(ch, languageVersion, identifierVariant)) return false;
        }

        return true;
    }

    // ported from: src/compiler/scanner.ts:389-399
    private static bool IsUnicodeIdentifierStart(int code, ScriptTarget? languageVersion)
        => languageVersion >= ScriptTarget.ES2015
            ? LookupInUnicodeMap(code, ScannerTables.UnicodeESNextIdentifierStart)
            : LookupInUnicodeMap(code, ScannerTables.UnicodeES5IdentifierStart);

    private static bool IsUnicodeIdentifierPart(int code, ScriptTarget? languageVersion)
        => languageVersion >= ScriptTarget.ES2015
            ? LookupInUnicodeMap(code, ScannerTables.UnicodeESNextIdentifierPart)
            : LookupInUnicodeMap(code, ScannerTables.UnicodeES5IdentifierPart);

    // ported from: src/compiler/scanner.ts:358-385
    /// <summary>Searches a range table (pairs of inclusive bounds) for a code point.</summary>
    private static bool LookupInUnicodeMap(int code, IList<int> map)
    {
        // Bail out quickly if it could not possibly be in the map.
        if (code < map[0]) return false;

        var lo = 0;
        var hi = map.Count;
        while (lo + 1 < hi)
        {
            var mid = lo + ((hi - lo) / 2);
            // mid has to be even to catch a range's beginning
            mid -= mid % 2;
            if (map[mid] <= code && code <= map[mid + 1]) return true;

            if (code < map[mid]) hi = mid;
            else lo = mid + 2;
        }

        return false;
    }

    // ---------------------------------------------------------------------------------------------
    // Code points
    // ---------------------------------------------------------------------------------------------

    // ported from: src/compiler/scanner.ts:4034-4037
    /// <summary>The code point starting at <paramref name="i"/>.</summary>
    /// <remarks>
    /// JS's <c>String.prototype.codePointAt</c> returns the value of an unpaired surrogate as-is; .NET's
    /// <c>char.ConvertToUtf32</c> throws for that input, so the pair is detected here.
    /// </remarks>
    public static int CodePointAt(string s, int i)
    {
        var first = s[i];
        if (char.IsHighSurrogate(first) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
        {
            return char.ConvertToUtf32(first, s[i + 1]);
        }

        return first;
    }

    // ported from: src/compiler/scanner.ts:4039-4048
    /// <summary>How many UTF-16 code units a code point occupies in the text.</summary>
    public static int CharSize(int ch)
    {
        if (ch >= 0x10000) return 2;
        if (ch == CharacterCodes.EOF) return 0;
        return 1;
    }

    // ported from: src/compiler/scanner.ts:4050-4066 — derived from 10.1.1 UTF16Encoding of the ES6 spec.
    /// <summary>Encodes a code point as a string.</summary>
    /// <remarks>
    /// PORT-NOTE: TS prefers <c>String.fromCodePoint</c> and falls back to a hand-written encoder; the port uses
    /// <see cref="char.ConvertFromUtf32"/>, which is the same encoding. One difference: .NET rejects the
    /// surrogate range where JS would hand back that lone code unit — every caller here has already validated the
    /// code point, so the difference is not reachable.
    /// </remarks>
    public static string Utf16EncodeAsString(int codePoint) => char.ConvertFromUtf32(codePoint);

    // ---------------------------------------------------------------------------------------------
    // Trivia
    // ---------------------------------------------------------------------------------------------

    // ported from: src/compiler/utilities.ts:10265 — TS: `positionIsSynthesized`
    /// <summary>Whether a position is not a real offset into the text.</summary>
    /// <remarks>TS writes <c>!(pos &gt;= 0)</c>, which is also true for <c>undefined</c> and <c>NaN</c>; with an
    /// <see cref="int"/> the negation covers only the negative case.</remarks>
    public static bool PositionIsSynthesized(int pos) => !(pos >= 0);

    // All conflict markers consist of the same character repeated seven times. If it is a <<<<<<< or >>>>>>>
    // marker then it is also followed by a space.
    /// <remarks>TS writes <c>"<<<<<<<".length</c>; a <c>string</c> member is not a constant expression in C#.</remarks>
    private const int MergeConflictMarkerLength = 7;

    // ported from: src/compiler/scanner.ts:641-734
    /// <summary>
    /// Skips trivia — white space, line breaks and comments — starting at <paramref name="pos"/>.
    /// </summary>
    /// <param name="stopAfterLineBreak">Stop as soon as a line break has been consumed (and its \n, when it was a
    /// \r\n).</param>
    /// <param name="stopAtComments">Stop at a comment instead of consuming it.</param>
    /// <param name="inJSDoc">Whether a leading <c>*</c> counts as trivia.</param>
    public static int SkipTrivia(
        string text,
        int pos,
        bool stopAfterLineBreak = false,
        bool stopAtComments = false,
        bool inJSDoc = false)
    {
        if (PositionIsSynthesized(pos)) return pos;

        var canConsumeStar = false;
        // Keep in sync with CouldStartTrivia
        while (true)
        {
            var ch = CharCodeAt(text, pos);
            switch (ch)
            {
                case CharacterCodes.CarriageReturn:
                    if (CharCodeAt(text, pos + 1) == CharacterCodes.LineFeed) pos++;
                    goto case CharacterCodes.LineFeed;
                case CharacterCodes.LineFeed:
                    pos++;
                    if (stopAfterLineBreak) return pos;
                    canConsumeStar = inJSDoc;
                    continue;
                case CharacterCodes.Tab:
                case CharacterCodes.VerticalTab:
                case CharacterCodes.FormFeed:
                case CharacterCodes.Space:
                    pos++;
                    continue;
                case CharacterCodes.Slash:
                    if (stopAtComments) break;
                    if (CharCodeAt(text, pos + 1) == CharacterCodes.Slash)
                    {
                        pos += 2;
                        while (pos < text.Length)
                        {
                            if (IsLineBreak(CharCodeAt(text, pos))) break;
                            pos++;
                        }

                        canConsumeStar = false;
                        continue;
                    }

                    if (CharCodeAt(text, pos + 1) == CharacterCodes.Asterisk)
                    {
                        pos += 2;
                        while (pos < text.Length)
                        {
                            if (CharCodeAt(text, pos) == CharacterCodes.Asterisk &&
                                CharCodeAt(text, pos + 1) == CharacterCodes.Slash)
                            {
                                pos += 2;
                                break;
                            }

                            pos++;
                        }

                        canConsumeStar = false;
                        continue;
                    }

                    break;

                case CharacterCodes.LessThan:
                case CharacterCodes.Bar:
                case CharacterCodes.EqualsSign:
                case CharacterCodes.GreaterThan:
                    if (IsConflictMarkerTrivia(text, pos))
                    {
                        pos = ScanConflictMarkerTrivia(text, pos);
                        canConsumeStar = false;
                        continue;
                    }

                    break;

                case CharacterCodes.Hash:
                    if (pos == 0 && IsShebangTrivia(text, pos))
                    {
                        pos = ScanShebangTrivia(text, pos);
                        canConsumeStar = false;
                        continue;
                    }

                    break;

                case CharacterCodes.Asterisk:
                    if (canConsumeStar)
                    {
                        pos++;
                        canConsumeStar = false;
                        continue;
                    }

                    break;

                default:
                    if (ch > CharacterCodes.MaxAsciiCharacter && IsWhiteSpaceLike(ch))
                    {
                        pos++;
                        continue;
                    }

                    break;
            }

            return pos;
        }
    }

    // ported from: src/compiler/scanner.ts:740-760
    /// <summary>Whether a merge-conflict marker starts at <paramref name="pos"/> (also used by the scanner).
    /// </summary>
    public static bool IsConflictMarkerTrivia(string text, int pos)
    {
        Debug.Assert(pos >= 0);

        // Conflict markers must be at the start of a line.
        if (pos == 0 || IsLineBreak(CharCodeAt(text, pos - 1)))
        {
            var ch = CharCodeAt(text, pos);

            if (pos + MergeConflictMarkerLength < text.Length)
            {
                for (var i = 0; i < MergeConflictMarkerLength; i++)
                {
                    if (CharCodeAt(text, pos + i) != ch) return false;
                }

                return ch == CharacterCodes.EqualsSign ||
                    CharCodeAt(text, pos + MergeConflictMarkerLength) == CharacterCodes.Space;
            }
        }

        return false;
    }

    // ported from: src/compiler/scanner.ts:762-790
    /// <summary>Consumes a merge-conflict marker, reporting it through <paramref name="error"/>.</summary>
    public static int ScanConflictMarkerTrivia(
        string text,
        int pos,
        Action<DiagnosticMessage, int?, int?>? error = null)
    {
        error?.Invoke(Diagnostics.Merge_conflict_marker_encountered, pos, MergeConflictMarkerLength);

        var ch = CharCodeAt(text, pos);
        var len = text.Length;

        if (ch == CharacterCodes.LessThan || ch == CharacterCodes.GreaterThan)
        {
            while (pos < len && !IsLineBreak(CharCodeAt(text, pos))) pos++;
        }
        else
        {
            Debug.Assert(ch == CharacterCodes.Bar || ch == CharacterCodes.EqualsSign);
            // Consume everything from the start of a ||||||| or ======= marker to the start of the next
            // ======= or >>>>>>> marker.
            while (pos < len)
            {
                var currentChar = CharCodeAt(text, pos);
                if ((currentChar == CharacterCodes.EqualsSign || currentChar == CharacterCodes.GreaterThan) &&
                    currentChar != ch &&
                    IsConflictMarkerTrivia(text, pos))
                {
                    break;
                }

                pos++;
            }
        }

        return pos;
    }

    // ported from: src/compiler/scanner.ts:792
    /// <remarks>
    /// PORT-NOTE: JS's <c>.</c> matches no line terminator at all, while .NET's matches everything but <c>\n</c>,
    /// so the class spells the four terminators out.
    /// </remarks>
    [GeneratedRegex(@"^#![^\n\r\u2028\u2029]*")]
    private static partial Regex ShebangTriviaRegex();

    // ported from: src/compiler/scanner.ts:794-798
    public static bool IsShebangTrivia(string text, int pos)
    {
        // Shebangs check must only be done at the start of the file
        Debug.Assert(pos == 0);
        return ShebangTriviaRegex().IsMatch(text);
    }

    // ported from: src/compiler/scanner.ts:800-804
    public static int ScanShebangTrivia(string text, int pos)
        => pos + ShebangTriviaRegex().Match(text).Value.Length;

    // ported from: src/compiler/scanner.ts:965-971
    /// <summary>The shebang line of a file, when it has one.</summary>
    public static string? GetShebang(string text)
    {
        var match = ShebangTriviaRegex().Match(text);
        return match.Success ? match.Value : null;
    }

    // ---------------------------------------------------------------------------------------------
    // Comment ranges
    // ---------------------------------------------------------------------------------------------

    // ported from: src/compiler/scanner.ts:826-930
    /// <summary>
    /// Invokes <paramref name="cb"/> for each comment range following <paramref name="pos"/>.
    /// </summary>
    /// <remarks>
    /// Single-line ranges include the leading <c>//</c> but not the line break; multi-line ranges include both
    /// delimiters.
    /// </remarks>
    /// <param name="reduce">When true the callback's result is accumulated (like <c>reduceLeft</c>); when false,
    /// iteration stops at the first one that comes back set.</param>
    /// <param name="trailing">When false, white space is skipped up to the first line break and the comments
    /// between there and the next token are reported. When true, the comments between <paramref name="pos"/> and
    /// the next line break are.</param>
    public static U? IterateCommentRanges<T, U>(
        bool reduce,
        string text,
        int pos,
        bool trailing,
        Func<int, int, SyntaxKind, bool, T, U?, U?> cb,
        T state,
        U? initial = default)
    {
        var pendingPos = 0;
        var pendingEnd = 0;
        var pendingKind = SyntaxKind.SingleLineCommentTrivia;
        var pendingHasTrailingNewLine = false;
        var hasPendingCommentRange = false;
        var collecting = trailing;
        var accumulator = initial;
        if (pos == 0)
        {
            collecting = true;
            var shebang = GetShebang(text);
            if (shebang is not null) pos = shebang.Length;
        }

        // TS labels this loop `scan` and leaves it with `break scan`; the label after it is the same exit.
        while (pos >= 0 && pos < text.Length)
        {
            var ch = CharCodeAt(text, pos);
            switch (ch)
            {
                case CharacterCodes.CarriageReturn:
                    if (CharCodeAt(text, pos + 1) == CharacterCodes.LineFeed) pos++;
                    goto case CharacterCodes.LineFeed;
                case CharacterCodes.LineFeed:
                    pos++;
                    if (trailing) goto scanDone;

                    collecting = true;
                    if (hasPendingCommentRange) pendingHasTrailingNewLine = true;
                    continue;
                case CharacterCodes.Tab:
                case CharacterCodes.VerticalTab:
                case CharacterCodes.FormFeed:
                case CharacterCodes.Space:
                    pos++;
                    continue;
                case CharacterCodes.Slash:
                {
                    var nextChar = CharCodeAt(text, pos + 1);
                    var hasTrailingNewLine = false;
                    if (nextChar == CharacterCodes.Slash || nextChar == CharacterCodes.Asterisk)
                    {
                        var kind = nextChar == CharacterCodes.Slash
                            ? SyntaxKind.SingleLineCommentTrivia
                            : SyntaxKind.MultiLineCommentTrivia;
                        var startPos = pos;
                        pos += 2;
                        if (nextChar == CharacterCodes.Slash)
                        {
                            while (pos < text.Length)
                            {
                                if (IsLineBreak(CharCodeAt(text, pos)))
                                {
                                    hasTrailingNewLine = true;
                                    break;
                                }

                                pos++;
                            }
                        }
                        else
                        {
                            while (pos < text.Length)
                            {
                                if (CharCodeAt(text, pos) == CharacterCodes.Asterisk &&
                                    CharCodeAt(text, pos + 1) == CharacterCodes.Slash)
                                {
                                    pos += 2;
                                    break;
                                }

                                pos++;
                            }
                        }

                        if (collecting)
                        {
                            if (hasPendingCommentRange)
                            {
                                accumulator = cb(pendingPos, pendingEnd, pendingKind, pendingHasTrailingNewLine, state, accumulator);

                                // TS tests the accumulator for truthiness; with an unconstrained U the closest
                                // equivalent is "not the default" — null for the reference types this is used
                                // with, false for bool. (The BCL comparer has to be spelled out: this namespace
                                // declares its own EqualityComparer<T> delegate.)
                                if (!reduce &&
                                    !System.Collections.Generic.EqualityComparer<U>.Default.Equals(accumulator, default))
                                {
                                    return accumulator;
                                }
                            }

                            pendingPos = startPos;
                            pendingEnd = pos;
                            pendingKind = kind;
                            pendingHasTrailingNewLine = hasTrailingNewLine;
                            hasPendingCommentRange = true;
                        }

                        continue;
                    }

                    goto scanDone;
                }

                default:
                    if (ch > CharacterCodes.MaxAsciiCharacter && IsWhiteSpaceLike(ch))
                    {
                        if (hasPendingCommentRange && IsLineBreak(ch)) pendingHasTrailingNewLine = true;
                        pos++;
                        continue;
                    }

                    goto scanDone;
            }
        }

    scanDone:
        if (hasPendingCommentRange)
        {
            accumulator = cb(pendingPos, pendingEnd, pendingKind, pendingHasTrailingNewLine, state, accumulator);
        }

        return accumulator;
    }

    // ported from: src/compiler/scanner.ts:932-936
    /// <summary>Invokes the callback for each comment range leading up to the token at
    /// <paramref name="pos"/>.</summary>
    public static U? ForEachLeadingCommentRange<T, U>(
        string text,
        int pos,
        Func<int, int, SyntaxKind, bool, T, U?, U?> cb,
        T state)
        => IterateCommentRanges(reduce: false, text, pos, trailing: false, cb, state);

    /// <summary>ported from: src/compiler/scanner.ts:932-936 — the overload whose callback takes no state.
    /// </summary>
    public static U? ForEachLeadingCommentRange<U>(string text, int pos, Func<int, int, SyntaxKind, bool, U?> cb)
        => IterateCommentRanges<object?, U>(
            reduce: false,
            text,
            pos,
            trailing: false,
            (start, end, kind, hasTrailingNewLine, _, _) => cb(start, end, kind, hasTrailingNewLine),
            state: null);

    // ported from: src/compiler/scanner.ts:938-942
    /// <summary>Invokes the callback for each comment range between <paramref name="pos"/> and the next line
    /// break.</summary>
    public static U? ForEachTrailingCommentRange<T, U>(
        string text,
        int pos,
        Func<int, int, SyntaxKind, bool, T, U?, U?> cb,
        T state)
        => IterateCommentRanges(reduce: false, text, pos, trailing: true, cb, state);

    /// <summary>ported from: src/compiler/scanner.ts:938-942 — the overload whose callback takes no state.
    /// </summary>
    public static U? ForEachTrailingCommentRange<U>(string text, int pos, Func<int, int, SyntaxKind, bool, U?> cb)
        => IterateCommentRanges<object?, U>(
            reduce: false,
            text,
            pos,
            trailing: true,
            (start, end, kind, hasTrailingNewLine, _, _) => cb(start, end, kind, hasTrailingNewLine),
            state: null);

    // ported from: src/compiler/scanner.ts:944-946
    public static U? ReduceEachLeadingCommentRange<T, U>(
        string text,
        int pos,
        Func<int, int, SyntaxKind, bool, T, U?, U?> cb,
        T state,
        U? initial)
        => IterateCommentRanges(reduce: true, text, pos, trailing: false, cb, state, initial);

    // ported from: src/compiler/scanner.ts:948-950
    public static U? ReduceEachTrailingCommentRange<T, U>(
        string text,
        int pos,
        Func<int, int, SyntaxKind, bool, T, U?, U?> cb,
        T state,
        U? initial)
        => IterateCommentRanges(reduce: true, text, pos, trailing: true, cb, state, initial);

    // ported from: src/compiler/scanner.ts:952-955
    private static IList<CommentRange> AppendCommentRange(
        int pos,
        int end,
        SyntaxKind kind,
        bool hasTrailingNewLine,
        object? state,
        IList<CommentRange>? comments)
    {
        comments ??= [];
        comments.Add(new CommentRange
        {
            Kind = kind,
            Pos = pos,
            End = end,
            HasTrailingNewLine = hasTrailingNewLine,
        });

        return comments;
    }

    // ported from: src/compiler/scanner.ts:957-959
    public static IList<CommentRange>? GetLeadingCommentRanges(string text, int pos)
        => ReduceEachLeadingCommentRange<object?, IList<CommentRange>>(
            text,
            pos,
            AppendCommentRange,
            state: null,
            initial: null);

    // ported from: src/compiler/scanner.ts:961-963
    public static IList<CommentRange>? GetTrailingCommentRanges(string text, int pos)
        => ReduceEachTrailingCommentRange<object?, IList<CommentRange>>(
            text,
            pos,
            AppendCommentRange,
            state: null,
            initial: null);

    /// <summary>JS's <c>charCodeAt</c>: a position outside the text reads as <c>NaN</c>, which compares equal to
    /// no character code. The port uses <see cref="CharacterCodes.EOF"/> for the same effect.</summary>
    internal static int CharCodeAt(string text, int pos) => pos >= 0 && pos < text.Length ? text[pos] : CharacterCodes.EOF;
}
