using System.Text.RegularExpressions;

namespace TypeScript.Compiler;

/// <summary>ported from: src/compiler/scanner.ts:39 — TS: <c>ErrorCallback</c>.</summary>
internal delegate void ErrorCallback(DiagnosticMessage message, int length, object? arg0 = null);

// ported from: src/compiler/scanner.ts:1001-1012
/// <summary>What <c>scanEscapeSequence</c> should report and allow.</summary>
/// <remarks>PORT-NOTE: TS declares this <c>const enum</c>; the composite members are spelled out with their real
/// values rather than re-derived, since <c>AllowExtendedUnicodeEscape</c> is <c>String | AnyUnicodeMode</c> and not
/// a bit of its own.</remarks>
[Flags]
internal enum EscapeSequenceScanningFlags
{
    None = 0,
    String = 1 << 0,
    ReportErrors = 1 << 1,
    RegularExpression = 1 << 2,
    AnnexB = 1 << 3,
    AnyUnicodeMode = 1 << 4,
    AtomEscape = 1 << 5,
    ReportInvalidEscapeErrors = RegularExpression | ReportErrors,
    AllowExtendedUnicodeEscape = String | AnyUnicodeMode,
}

// ported from: src/compiler/scanner.ts:1014-1019
/// <summary>The kind of set operation a <c>v</c>-flag character class is built from.</summary>
internal enum ClassSetExpressionType
{
    Unknown = 0,
    ClassUnion = 1,
    ClassIntersection = 2,
    ClassSubtraction = 3,
}

// ported from: src/compiler/scanner.ts:51-132, 1022-1169, 3985-4031
/// <summary>
/// Turns text into a token stream.
/// </summary>
/// <remarks>
/// TS builds this with <c>createScanner</c>, which closes over its state; the port uses a class whose fields are
/// that state, so the methods read like the TS ones without the closure indirection.
/// <para>
/// PORT-NOTE: <c>getTokenValue()</c> is <c>undefined</c> until a scan runs in TS, so it maps to a nullable string
/// here; everything else TS initializes eagerly.
/// </para>
/// <para>
/// PORT-NOTE: TS's optional <c>__debugShowCurrentPositionInText</c> property (only defined when the compiler is
/// running under a debugger) is not ported — it exists to make a JS debugger's watch window readable.
/// </para>
/// </remarks>
internal sealed partial class Scanner
{
    // ---- State, one field per closure variable of createScanner. ----

    private ScriptTarget _languageVersion;
    private readonly bool _skipTrivia;
    private LanguageVariant _languageVariant;

    /// <summary>The text being scanned.</summary>
    private string _text = "";

    /// <summary>Current position (the end of the current token's text).</summary>
    private int _pos;

    /// <summary>End of the text, i.e. the exclusive bound <see cref="_pos"/> is clamped to.</summary>
    private int _end;

    /// <summary>Start of the white space before the current token.</summary>
    private int _fullStartPos;

    /// <summary>Start of the current token's text.</summary>
    private int _tokenStart;

    private SyntaxKind _token;
    private string? _tokenValue;
    private TokenFlags _tokenFlags;

    private IList<ICommentDirective>? _commentDirectives;
    private int _skipJsDocLeadingAsterisks;
    private ScriptKind _scriptKind = ScriptKind.Unknown;
    private JSDocParsingMode _jsDocParsingMode = JSDocParsingMode.ParseAll;
    private ErrorCallback? _onError;

    private Scanner(ScriptTarget languageVersion, bool skipTrivia, LanguageVariant languageVariant, ErrorCallback? onError)
    {
        _languageVersion = languageVersion;
        _skipTrivia = skipTrivia;
        _languageVariant = languageVariant;
        _onError = onError;
    }

    // ported from: src/compiler/scanner.ts:1022-1124
    /// <summary>Creates a scanner over <paramref name="textInitial"/>, optionally limited to a sub-range.</summary>
    public static Scanner Create(
        ScriptTarget languageVersion,
        bool skipTrivia,
        LanguageVariant languageVariant = LanguageVariant.Standard,
        string? textInitial = null,
        ErrorCallback? onError = null,
        int? start = null,
        int? length = null)
    {
        var scanner = new Scanner(languageVersion, skipTrivia, languageVariant, onError);
        scanner.SetText(textInitial, start, length);
        return scanner;
    }

    // ---- Public contract (Scanner, scanner.ts:51-132). ----

    public int GetTokenFullStart() => _fullStartPos;

    public int GetStartPos() => _fullStartPos;

    public int GetTokenEnd() => _pos;

    public int GetTextPos() => _pos;

    public SyntaxKind GetToken() => _token;

    public int GetTokenStart() => _tokenStart;

    public int GetTokenPos() => _tokenStart;

    public string GetTokenText() => _text[_tokenStart.._pos];

    public string? GetTokenValue() => _tokenValue;

    public bool HasUnicodeEscape() => (_tokenFlags & TokenFlags.UnicodeEscape) != 0;

    public bool HasExtendedUnicodeEscape() => (_tokenFlags & TokenFlags.ExtendedUnicodeEscape) != 0;

    public bool HasPrecedingLineBreak() => (_tokenFlags & TokenFlags.PrecedingLineBreak) != 0;

    public bool HasPrecedingJSDocComment() => (_tokenFlags & TokenFlags.PrecedingJSDocComment) != 0;

    public bool HasPrecedingJSDocLeadingAsterisks() => (_tokenFlags & TokenFlags.PrecedingJSDocLeadingAsterisks) != 0;

    public bool IsIdentifier() => _token == SyntaxKind.Identifier || _token > SyntaxKind.LastReservedWord;

    public bool IsReservedWord() => _token >= SyntaxKind.FirstReservedWord && _token <= SyntaxKind.LastReservedWord;

    public bool IsUnterminated() => (_tokenFlags & TokenFlags.Unterminated) != 0;

    public IList<ICommentDirective>? GetCommentDirectives() => _commentDirectives;

    public TokenFlags GetNumericLiteralFlags() => _tokenFlags & TokenFlags.NumericLiteralFlags;

    public TokenFlags GetTokenFlags() => _tokenFlags;

    public string GetText() => _text;

    public void ClearCommentDirectives() => _commentDirectives = null;

    /// <summary>Points the scanner at new text, optionally limited to <paramref name="start"/> +
    /// <paramref name="length"/>.</summary>
    public void SetText(string? newText, int? start, int? length)
    {
        _text = newText ?? "";
        _end = length is null ? _text.Length : start!.Value + length.Value;
        ResetTokenState(start ?? 0);
    }

    public void SetOnError(ErrorCallback? errorCallback) => _onError = errorCallback;

    public void SetScriptTarget(ScriptTarget scriptTarget) => _languageVersion = scriptTarget;

    public void SetLanguageVariant(LanguageVariant variant) => _languageVariant = variant;

    public void SetScriptKind(ScriptKind kind) => _scriptKind = kind;

    public void SetJSDocParsingMode(JSDocParsingMode kind) => _jsDocParsingMode = kind;

    public void ResetTokenState(int position)
    {
        Debug.Assert(position >= 0);
        _pos = position;
        _fullStartPos = position;
        _tokenStart = position;
        _token = SyntaxKind.Unknown;
        _tokenValue = null;
        _tokenFlags = TokenFlags.None;
    }

    public void SetSkipJsDocLeadingAsterisks(bool skip) => _skipJsDocLeadingAsterisks += skip ? 1 : -1;

    // ---- Position helpers (scanner.ts:1131-1169). ----

    /// <summary>The code point at <paramref name="pos"/>, which must be inside the text.</summary>
    private int CodePointUnchecked(int pos) => ScannerUtilities.CodePointAt(_text, pos);

    /// <summary>The code point at <paramref name="pos"/>, or <see cref="CharacterCodes.EOF"/> outside the text.
    /// </summary>
    private int CodePointChecked(int pos) => pos >= 0 && pos < _end ? CodePointUnchecked(pos) : CharacterCodes.EOF;

    /// <summary>The char code at <paramref name="pos"/>, which must be inside the text.</summary>
    private int CharCodeUnchecked(int pos) => ScannerUtilities.CharCodeAt(_text, pos);

    /// <summary>The char code at <paramref name="pos"/>, or <see cref="CharacterCodes.EOF"/> outside the text.
    /// </summary>
    private int CharCodeChecked(int pos) => pos >= 0 && pos < _end ? CharCodeUnchecked(pos) : CharacterCodes.EOF;

    // ported from: src/compiler/scanner.ts:1160-1169
    /// <summary>Reports a diagnostic at <paramref name="errPos"/> without moving the scan position.</summary>
    /// <remarks>
    /// TS declares this with two overloads (a message-only form and a position/length form); C# folds them into
    /// one signature with defaults.
    /// </remarks>
    private void Error(DiagnosticMessage message, int? errPos = null, int? length = null, object? arg0 = null)
    {
        if (_onError is null) return;

        var oldPos = _pos;
        _pos = errPos ?? _pos;
        _onError(message, length ?? 0, arg0);
        _pos = oldPos;
    }

    /// <summary>JS's <c>"" + +s</c>: parse the text as a number, then render it the way JS would.</summary>
    /// <remarks>
    /// Every input here is a digit fragment the scanner itself produced (optionally with separators already
    /// removed), so the only part of JS's <c>ToNumber</c> that matters is that a failed parse yields <c>NaN</c>
    /// rather than throwing.
    /// </remarks>
    private static string ToNumberString(string s)
        => Js.NumberToString(
            double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
                ? value
                : double.NaN);

    // ---- Numbers (scanner.ts:1171-1420, 1869-1889). ----

    // ported from: src/compiler/scanner.ts:1171-1211
    private string ScanNumberFragment()
    {
        var start = _pos;
        var allowSeparator = false;
        var isPreviousTokenSeparator = false;
        var result = "";
        while (true)
        {
            var ch = CharCodeUnchecked(_pos);
            if (ch == CharacterCodes.Underscore)
            {
                _tokenFlags |= TokenFlags.ContainsSeparator;
                if (allowSeparator)
                {
                    allowSeparator = false;
                    isPreviousTokenSeparator = true;
                    result += _text[start.._pos];
                }
                else
                {
                    _tokenFlags |= TokenFlags.ContainsInvalidSeparator;
                    if (isPreviousTokenSeparator)
                    {
                        Error(Diagnostics.Multiple_consecutive_numeric_separators_are_not_permitted, _pos, 1);
                    }
                    else
                    {
                        Error(Diagnostics.Numeric_separators_are_not_allowed_here, _pos, 1);
                    }
                }

                _pos++;
                start = _pos;
                continue;
            }

            if (ScannerUtilities.IsDigit(ch))
            {
                allowSeparator = true;
                isPreviousTokenSeparator = false;
                _pos++;
                continue;
            }

            break;
        }

        if (CharCodeUnchecked(_pos - 1) == CharacterCodes.Underscore)
        {
            _tokenFlags |= TokenFlags.ContainsInvalidSeparator;
            Error(Diagnostics.Numeric_separators_are_not_allowed_here, _pos - 1, 1);
        }

        return result + _text[start.._pos];
    }

    // ported from: src/compiler/scanner.ts:1213-1324
    //
    // Extract from Section 12.9.3:
    //   NumericLiteral ::= DecimalLiteral | DecimalBigIntegerLiteral | NonDecimalIntegerLiteral 'n'?
    //                    | LegacyOctalIntegerLiteral
    //   DecimalBigIntegerLiteral ::= '0n' | [1-9] DecimalDigits? 'n' | [1-9] '_' DecimalDigits 'n'
    //   DecimalLiteral ::= DecimalIntegerLiteral '.' DecimalDigits? ExponentPart?
    //                    | '.' DecimalDigits ExponentPart? | DecimalIntegerLiteral ExponentPart?
    //   DecimalIntegerLiteral ::= '0' | [1-9] '_'? DecimalDigits | NonOctalDecimalIntegerLiteral
    //   LegacyOctalIntegerLiteral ::= '0' [0-7]+
    //   NonOctalDecimalIntegerLiteral ::= '0' [0-7]* [89] [0-9]*
    private SyntaxKind ScanNumber()
    {
        var start = _pos;
        string mainFragment;
        if (CharCodeUnchecked(_pos) == CharacterCodes._0)
        {
            _pos++;
            if (CharCodeUnchecked(_pos) == CharacterCodes.Underscore)
            {
                _tokenFlags |= TokenFlags.ContainsSeparator | TokenFlags.ContainsInvalidSeparator;
                Error(Diagnostics.Numeric_separators_are_not_allowed_here, _pos, 1);

                // treat it as a normal number literal
                _pos--;
                mainFragment = ScanNumberFragment();
            }

            // Separators are not allowed in the below cases
            else if (!ScanDigits())
            {
                // NonOctalDecimalIntegerLiteral, emit error later. Separators in decimal and exponent parts are
                // still allowed according to the spec.
                _tokenFlags |= TokenFlags.ContainsLeadingZero;
                mainFragment = ToNumberString(_tokenValue ?? "");
            }
            else if (!Js.IsTruthy(_tokenValue))
            {
                // a single zero
                mainFragment = "0";
            }
            else
            {
                // LegacyOctalIntegerLiteral
                _tokenValue = Convert.ToInt32(_tokenValue, 8).ToString(System.Globalization.CultureInfo.InvariantCulture);
                _tokenFlags |= TokenFlags.Octal;
                var withMinus = _token == SyntaxKind.MinusToken;
                var literal = (withMinus ? "-" : "") + "0o" + Convert.ToString(Convert.ToInt32(_tokenValue), 8);
                if (withMinus) start--;
                Error(Diagnostics.Octal_literals_are_not_allowed_Use_the_syntax_0, start, _pos - start, literal);
                return SyntaxKind.NumericLiteral;
            }
        }
        else
        {
            mainFragment = ScanNumberFragment();
        }

        string? decimalFragment = null;
        string? scientificFragment = null;
        if (CharCodeUnchecked(_pos) == CharacterCodes.Dot)
        {
            _pos++;
            decimalFragment = ScanNumberFragment();
        }

        var end = _pos;
        if (CharCodeUnchecked(_pos) == CharacterCodes.E || CharCodeUnchecked(_pos) == CharacterCodes.e)
        {
            _pos++;
            _tokenFlags |= TokenFlags.Scientific;
            if (CharCodeUnchecked(_pos) == CharacterCodes.Plus || CharCodeUnchecked(_pos) == CharacterCodes.Minus) _pos++;
            var preNumericPart = _pos;
            var finalFragment = ScanNumberFragment();
            if (!Js.IsTruthy(finalFragment))
            {
                Error(Diagnostics.Digit_expected);
            }
            else
            {
                scientificFragment = _text[end..preNumericPart] + finalFragment;
                end = _pos;
            }
        }

        string result;
        if ((_tokenFlags & TokenFlags.ContainsSeparator) != 0)
        {
            result = mainFragment;
            if (decimalFragment is not null) result += "." + decimalFragment;
            if (scientificFragment is not null) result += scientificFragment;
        }
        else
        {
            // No need to use all the fragments; no separator removal is needed.
            result = _text[start..end];
        }

        if ((_tokenFlags & TokenFlags.ContainsLeadingZero) != 0)
        {
            Error(Diagnostics.Decimals_with_leading_zeros_are_not_allowed, start, end - start);
            // if a literal has a leading zero, it must not be bigint
            _tokenValue = ToNumberString(result);
            return SyntaxKind.NumericLiteral;
        }

        if (decimalFragment is not null || (_tokenFlags & TokenFlags.Scientific) != 0)
        {
            CheckForIdentifierStartAfterNumericLiteral(
                start,
                decimalFragment is null && (_tokenFlags & TokenFlags.Scientific) != 0);

            // if value is not an integer, it can be safely coerced to a number
            _tokenValue = ToNumberString(result);
            return SyntaxKind.NumericLiteral;
        }

        _tokenValue = result;
        var type = CheckBigIntSuffix(); // if value is an integer, check whether it is a bigint
        CheckForIdentifierStartAfterNumericLiteral(start);
        return type;
    }

    // ported from: src/compiler/scanner.ts:1326-1346
    private void CheckForIdentifierStartAfterNumericLiteral(int numericStart, bool isScientific = false)
    {
        if (!ScannerUtilities.IsIdentifierStart(CodePointUnchecked(_pos), _languageVersion)) return;

        var identifierStart = _pos;
        var length = ScanIdentifierParts().Length;

        if (length == 1 && _text[identifierStart] == 'n')
        {
            if (isScientific)
            {
                Error(Diagnostics.A_bigint_literal_cannot_use_exponential_notation, numericStart, identifierStart - numericStart + 1);
            }
            else
            {
                Error(Diagnostics.A_bigint_literal_must_be_an_integer, numericStart, identifierStart - numericStart + 1);
            }
        }
        else
        {
            Error(Diagnostics.An_identifier_or_keyword_cannot_immediately_follow_a_numeric_literal, identifierStart, length);
            _pos = identifierStart;
        }
    }

    // ported from: src/compiler/scanner.ts:1348-1359
    /// <summary>Scans the digits after a leading <c>0</c>, reporting whether they are all octal.</summary>
    private bool ScanDigits()
    {
        var start = _pos;
        var isOctal = true;
        while (ScannerUtilities.IsDigit(CharCodeChecked(_pos)))
        {
            if (!ScannerUtilities.IsOctalDigit(CharCodeUnchecked(_pos))) isOctal = false;
            _pos++;
        }

        _tokenValue = _text[start.._pos];
        return isOctal;
    }

    // ported from: src/compiler/scanner.ts:1365-1368
    /// <summary>Scans exactly <paramref name="count"/> hexadecimal digits, or -1 when they are unavailable.
    /// </summary>
    private int ScanExactNumberOfHexDigits(int count, bool canHaveSeparators)
    {
        var valueString = ScanHexDigits(count, scanAsManyAsPossible: false, canHaveSeparators);
        return Js.IsTruthy(valueString) ? Convert.ToInt32(valueString, 16) : -1;
    }

    // ported from: src/compiler/scanner.ts:1374-1376
    /// <summary>Scans as many hexadecimal digits as are available, or "" when fewer than
    /// <paramref name="count"/> are there.</summary>
    private string ScanMinimumNumberOfHexDigits(int count, bool canHaveSeparators)
        => ScanHexDigits(count, scanAsManyAsPossible: true, canHaveSeparators);

    // ported from: src/compiler/scanner.ts:1378-1420
    private string ScanHexDigits(int minCount, bool scanAsManyAsPossible, bool canHaveSeparators)
    {
        var valueChars = new List<int>();
        var allowSeparator = false;
        var isPreviousTokenSeparator = false;
        while (valueChars.Count < minCount || scanAsManyAsPossible)
        {
            var ch = CharCodeUnchecked(_pos);
            if (canHaveSeparators && ch == CharacterCodes.Underscore)
            {
                _tokenFlags |= TokenFlags.ContainsSeparator;
                if (allowSeparator)
                {
                    allowSeparator = false;
                    isPreviousTokenSeparator = true;
                }
                else if (isPreviousTokenSeparator)
                {
                    Error(Diagnostics.Multiple_consecutive_numeric_separators_are_not_permitted, _pos, 1);
                }
                else
                {
                    Error(Diagnostics.Numeric_separators_are_not_allowed_here, _pos, 1);
                }

                _pos++;
                continue;
            }

            allowSeparator = canHaveSeparators;
            if (ch >= CharacterCodes.A && ch <= CharacterCodes.F)
            {
                ch += CharacterCodes.a - CharacterCodes.A; // standardize hex literals to lowercase
            }
            else if (!((ch >= CharacterCodes._0 && ch <= CharacterCodes._9) ||
                (ch >= CharacterCodes.a && ch <= CharacterCodes.f)))
            {
                break;
            }

            valueChars.Add(ch);
            _pos++;
            isPreviousTokenSeparator = false;
        }

        if (valueChars.Count < minCount) valueChars.Clear();

        if (CharCodeUnchecked(_pos - 1) == CharacterCodes.Underscore)
        {
            Error(Diagnostics.Numeric_separators_are_not_allowed_here, _pos - 1, 1);
        }

        return new string(valueChars.Select(static c => (char)c).ToArray());
    }

    // ---- Strings, templates and escape sequences (scanner.ts:1422-1780, 1781-1889). ----

    // ported from: src/compiler/scanner.ts:1422-1456
    private string ScanString(bool jsxAttributeString = false)
    {
        var quote = CharCodeUnchecked(_pos);
        _pos++;
        var result = "";
        var start = _pos;
        while (true)
        {
            if (_pos >= _end)
            {
                result += _text[start.._pos];
                _tokenFlags |= TokenFlags.Unterminated;
                Error(Diagnostics.Unterminated_string_literal);
                break;
            }

            var ch = CharCodeUnchecked(_pos);
            if (ch == quote)
            {
                result += _text[start.._pos];
                _pos++;
                break;
            }

            if (ch == CharacterCodes.Backslash && !jsxAttributeString)
            {
                result += _text[start.._pos];
                result += ScanEscapeSequence(EscapeSequenceScanningFlags.String | EscapeSequenceScanningFlags.ReportErrors);
                start = _pos;
                continue;
            }

            if ((ch == CharacterCodes.LineFeed || ch == CharacterCodes.CarriageReturn) && !jsxAttributeString)
            {
                result += _text[start.._pos];
                _tokenFlags |= TokenFlags.Unterminated;
                Error(Diagnostics.Unterminated_string_literal);
                break;
            }

            _pos++;
        }

        return result;
    }

    // ported from: src/compiler/scanner.ts:1458-1527
    /// <summary>
    /// Sets the current token value and returns a <c>NoSubstitutionTemplateLiteral</c> or one component of a
    /// template expression.
    /// </summary>
    private SyntaxKind ScanTemplateAndSetTokenValue(bool shouldEmitInvalidEscapeError)
    {
        var startedWithBacktick = CharCodeUnchecked(_pos) == CharacterCodes.Backtick;

        _pos++;
        var start = _pos;
        var contents = "";
        SyntaxKind resultingToken;

        while (true)
        {
            if (_pos >= _end)
            {
                contents += _text[start.._pos];
                _tokenFlags |= TokenFlags.Unterminated;
                Error(Diagnostics.Unterminated_template_literal);
                resultingToken = startedWithBacktick
                    ? SyntaxKind.NoSubstitutionTemplateLiteral
                    : SyntaxKind.TemplateTail;
                break;
            }

            var currChar = CharCodeUnchecked(_pos);

            // '`'
            if (currChar == CharacterCodes.Backtick)
            {
                contents += _text[start.._pos];
                _pos++;
                resultingToken = startedWithBacktick
                    ? SyntaxKind.NoSubstitutionTemplateLiteral
                    : SyntaxKind.TemplateTail;
                break;
            }

            // '${'
            if (currChar == CharacterCodes.Dollar && _pos + 1 < _end && CharCodeUnchecked(_pos + 1) == CharacterCodes.OpenBrace)
            {
                contents += _text[start.._pos];
                _pos += 2;
                resultingToken = startedWithBacktick ? SyntaxKind.TemplateHead : SyntaxKind.TemplateMiddle;
                break;
            }

            // Escape character
            if (currChar == CharacterCodes.Backslash)
            {
                contents += _text[start.._pos];
                contents += ScanEscapeSequence(
                    EscapeSequenceScanningFlags.String |
                    (shouldEmitInvalidEscapeError ? EscapeSequenceScanningFlags.ReportErrors : EscapeSequenceScanningFlags.None));
                start = _pos;
                continue;
            }

            // Speculated ECMAScript 6 Spec 11.8.6.1:
            // <CR><LF> and <CR> LineTerminatorSequences are normalized to <LF> for Template Values.
            if (currChar == CharacterCodes.CarriageReturn)
            {
                contents += _text[start.._pos];
                _pos++;

                if (_pos < _end && CharCodeUnchecked(_pos) == CharacterCodes.LineFeed) _pos++;

                contents += "\n";
                start = _pos;
                continue;
            }

            _pos++;
        }

        _tokenValue = contents;
        return resultingToken;
    }

    // ported from: src/compiler/scanner.ts:1529-1706
    //
    // Extract from Section A.1:
    //   EscapeSequence ::= CharacterEscapeSequence | 0 (?![0-9]) | LegacyOctalEscapeSequence
    //                    | NonOctalDecimalEscapeSequence | HexEscapeSequence | UnicodeEscapeSequence
    //   LegacyOctalEscapeSequence ::= '0' (?=[89]) | [1-7] (?![0-7]) | [0-3] [0-7] [0-7]? | [4-7] [0-7]
    //   NonOctalDecimalEscapeSequence ::= [89]
    private string ScanEscapeSequence(EscapeSequenceScanningFlags flags)
    {
        var start = _pos;
        _pos++;
        if (_pos >= _end)
        {
            Error(Diagnostics.Unexpected_end_of_text);
            return "";
        }

        var ch = CharCodeUnchecked(_pos);
        _pos++;
        switch (ch)
        {
            case CharacterCodes._0:
                // Although '0' preceding any digit is treated as LegacyOctalEscapeSequence,
                // '\08' should separately be interpreted as '\0' + '8'.
                if (_pos >= _end || !ScannerUtilities.IsDigit(CharCodeUnchecked(_pos))) return "\0";
                goto case CharacterCodes._1;

            case CharacterCodes._1:
            case CharacterCodes._2:
            case CharacterCodes._3:
                // '\1', '\17', '\177'
                if (_pos < _end && ScannerUtilities.IsOctalDigit(CharCodeUnchecked(_pos))) _pos++;
                goto case CharacterCodes._4;

            case CharacterCodes._4:
            case CharacterCodes._5:
            case CharacterCodes._6:
            case CharacterCodes._7:
                // '\4', '\47' but not '\477'
                if (_pos < _end && ScannerUtilities.IsOctalDigit(CharCodeUnchecked(_pos))) _pos++;

                _tokenFlags |= TokenFlags.ContainsInvalidEscape;
                if ((flags & EscapeSequenceScanningFlags.ReportInvalidEscapeErrors) != 0)
                {
                    var code = Convert.ToInt32(_text[(start + 1).._pos], 8);
                    if ((flags & EscapeSequenceScanningFlags.RegularExpression) != 0 &&
                        (flags & EscapeSequenceScanningFlags.AtomEscape) == 0 &&
                        ch != CharacterCodes._0)
                    {
                        Error(
                            Diagnostics.Octal_escape_sequences_and_backreferences_are_not_allowed_in_a_character_class_If_this_was_intended_as_an_escape_sequence_use_the_syntax_0_instead,
                            start,
                            _pos - start,
                            "\\x" + code.ToString("x2"));
                    }
                    else
                    {
                        Error(
                            Diagnostics.Octal_escape_sequences_are_not_allowed_Use_the_syntax_0,
                            start,
                            _pos - start,
                            "\\x" + code.ToString("x2"));
                    }

                    return ((char)code).ToString();
                }

                return _text[start.._pos];

            case CharacterCodes._8:
            case CharacterCodes._9:
                // the invalid '\8' and '\9'
                _tokenFlags |= TokenFlags.ContainsInvalidEscape;
                if ((flags & EscapeSequenceScanningFlags.ReportInvalidEscapeErrors) != 0)
                {
                    if ((flags & EscapeSequenceScanningFlags.RegularExpression) != 0 &&
                        (flags & EscapeSequenceScanningFlags.AtomEscape) == 0)
                    {
                        Error(Diagnostics.Decimal_escape_sequences_and_backreferences_are_not_allowed_in_a_character_class, start, _pos - start);
                    }
                    else
                    {
                        Error(Diagnostics.Escape_sequence_0_is_not_allowed, start, _pos - start, _text[start.._pos]);
                    }

                    return ((char)ch).ToString();
                }

                return _text[start.._pos];

            case CharacterCodes.b: return "\b";
            case CharacterCodes.t: return "\t";
            case CharacterCodes.n: return "\n";
            case CharacterCodes.v: return "\v";
            case CharacterCodes.f: return "\f";
            case CharacterCodes.r: return "\r";
            case CharacterCodes.SingleQuote: return "'";
            case CharacterCodes.DoubleQuote: return "\"";

            case CharacterCodes.u:
                if (_pos < _end && CharCodeUnchecked(_pos) == CharacterCodes.OpenBrace)
                {
                    // '\u{DDDDDD}'
                    _pos -= 2;
                    var result = ScanExtendedUnicodeEscape((flags & EscapeSequenceScanningFlags.ReportInvalidEscapeErrors) != 0);
                    if ((flags & EscapeSequenceScanningFlags.AllowExtendedUnicodeEscape) == 0)
                    {
                        _tokenFlags |= TokenFlags.ContainsInvalidEscape;
                        if ((flags & EscapeSequenceScanningFlags.ReportInvalidEscapeErrors) != 0)
                        {
                            Error(
                                Diagnostics.Unicode_escape_sequences_are_only_available_when_the_Unicode_u_flag_or_the_Unicode_Sets_v_flag_is_set,
                                start,
                                _pos - start);
                        }
                    }

                    return result;
                }

                // '\uDDDD'
                for (; _pos < start + 6; _pos++)
                {
                    if (!(_pos < _end && ScannerUtilities.IsHexDigit(CharCodeUnchecked(_pos)))
                        )
                    {
                        _tokenFlags |= TokenFlags.ContainsInvalidEscape;
                        if ((flags & EscapeSequenceScanningFlags.ReportInvalidEscapeErrors) != 0)
                        {
                            Error(Diagnostics.Hexadecimal_digit_expected);
                        }

                        return _text[start.._pos];
                    }
                }

                _tokenFlags |= TokenFlags.UnicodeEscape;
                var escapedValue = Convert.ToInt32(_text[(start + 2).._pos], 16);
                var escapedValueString = ((char)escapedValue).ToString();
                if ((flags & EscapeSequenceScanningFlags.AnyUnicodeMode) != 0 &&
                    escapedValue >= 0xD800 && escapedValue <= 0xDBFF &&
                    _pos + 6 < _end &&
                    _text[_pos..(_pos + 2)] == "\\u" &&
                    CharCodeUnchecked(_pos + 2) != CharacterCodes.OpenBrace)
                {
                    // For regular expressions in any Unicode mode, \u HexLeadSurrogate \u HexTrailSurrogate is
                    // treated as a single character for the purpose of determining whether a character class
                    // range is out of order — https://tc39.es/ecma262/#prod-RegExpUnicodeEscapeSequence
                    var nextStart = _pos;
                    var nextPos = _pos + 2;
                    for (; nextPos < nextStart + 6; nextPos++)
                    {
                        if (!ScannerUtilities.IsHexDigit(CharCodeUnchecked(nextPos)))
                        {
                            // leave the error to the next call
                            return escapedValueString;
                        }
                    }

                    var nextEscapedValue = Convert.ToInt32(_text[(nextStart + 2)..nextPos], 16);
                    if (nextEscapedValue >= 0xDC00 && nextEscapedValue <= 0xDFFF)
                    {
                        _pos = nextPos;
                        return escapedValueString + ((char)nextEscapedValue).ToString();
                    }
                }

                return escapedValueString;

            case CharacterCodes.x:
                // '\xDD'
                for (; _pos < start + 4; _pos++)
                {
                    if (!(_pos < _end && ScannerUtilities.IsHexDigit(CharCodeUnchecked(_pos))))
                    {
                        _tokenFlags |= TokenFlags.ContainsInvalidEscape;
                        if ((flags & EscapeSequenceScanningFlags.ReportInvalidEscapeErrors) != 0)
                        {
                            Error(Diagnostics.Hexadecimal_digit_expected);
                        }

                        return _text[start.._pos];
                    }
                }

                _tokenFlags |= TokenFlags.HexEscape;
                return ((char)Convert.ToInt32(_text[(start + 2).._pos], 16)).ToString();

            // When encountering a LineContinuation (a backslash and a line terminator sequence), the line
            // terminator is interpreted to be "the empty code unit sequence".
            case CharacterCodes.CarriageReturn:
                if (_pos < _end && CharCodeUnchecked(_pos) == CharacterCodes.LineFeed) _pos++;
                goto case CharacterCodes.LineFeed;

            case CharacterCodes.LineFeed:
            case CharacterCodes.LineSeparator:
            case CharacterCodes.ParagraphSeparator:
                return "";

            default:
                if ((flags & EscapeSequenceScanningFlags.AnyUnicodeMode) != 0 ||
                    ((flags & EscapeSequenceScanningFlags.RegularExpression) != 0 &&
                        (flags & EscapeSequenceScanningFlags.AnnexB) == 0 &&
                        ScannerUtilities.IsIdentifierPart(ch, _languageVersion)))
                {
                    Error(Diagnostics.This_character_cannot_be_escaped_in_a_regular_expression, _pos - 2, 2);
                }

                return ((char)ch).ToString();
        }
    }

    // ported from: src/compiler/scanner.ts:1708-1754
    private string ScanExtendedUnicodeEscape(bool shouldEmitInvalidEscapeError)
    {
        var start = _pos;
        _pos += 3;
        var escapedStart = _pos;
        var escapedValueString = ScanMinimumNumberOfHexDigits(1, canHaveSeparators: false);
        var escapedValue = Js.IsTruthy(escapedValueString) ? Convert.ToInt32(escapedValueString, 16) : -1;
        var isInvalidExtendedEscape = false;

        // Validate the value of the digit
        if (escapedValue < 0)
        {
            if (shouldEmitInvalidEscapeError) Error(Diagnostics.Hexadecimal_digit_expected);
            isInvalidExtendedEscape = true;
        }
        else if (escapedValue > 0x10FFFF)
        {
            if (shouldEmitInvalidEscapeError)
            {
                Error(Diagnostics.An_extended_Unicode_escape_value_must_be_between_0x0_and_0x10FFFF_inclusive, escapedStart, _pos - escapedStart);
            }

            isInvalidExtendedEscape = true;
        }

        if (_pos >= _end)
        {
            if (shouldEmitInvalidEscapeError) Error(Diagnostics.Unexpected_end_of_text);
            isInvalidExtendedEscape = true;
        }
        else if (CharCodeUnchecked(_pos) == CharacterCodes.CloseBrace)
        {
            // Only swallow the following character up if it's a '}'.
            _pos++;
        }
        else
        {
            if (shouldEmitInvalidEscapeError) Error(Diagnostics.Unterminated_Unicode_escape_sequence);
            isInvalidExtendedEscape = true;
        }

        if (isInvalidExtendedEscape)
        {
            _tokenFlags |= TokenFlags.ContainsInvalidEscape;
            return _text[start.._pos];
        }

        _tokenFlags |= TokenFlags.ExtendedUnicodeEscape;
        return ScannerUtilities.Utf16EncodeAsString(escapedValue);
    }

    // ported from: src/compiler/scanner.ts:1756-1767
    /// <summary>
    /// The current character is known to be a backslash: checks for a <c>\uXXXX</c> escape and returns its value,
    /// or -1 when there is no valid escape here.
    /// </summary>
    private int PeekUnicodeEscape()
    {
        if (_pos + 5 < _end && CharCodeUnchecked(_pos + 1) == CharacterCodes.u)
        {
            var start = _pos;
            _pos += 2;
            var value = ScanExactNumberOfHexDigits(4, canHaveSeparators: false);
            _pos = start;
            return value;
        }

        return -1;
    }

    // ported from: src/compiler/scanner.ts:1769-1779
    private int PeekExtendedUnicodeEscape()
    {
        if (CodePointUnchecked(_pos + 1) == CharacterCodes.u && CodePointUnchecked(_pos + 2) == CharacterCodes.OpenBrace)
        {
            var start = _pos;
            _pos += 3;
            var escapedValueString = ScanMinimumNumberOfHexDigits(1, canHaveSeparators: false);
            var escapedValue = Js.IsTruthy(escapedValueString) ? Convert.ToInt32(escapedValueString, 16) : -1;
            _pos = start;
            return escapedValue;
        }

        return -1;
    }

    // ported from: src/compiler/scanner.ts:1781-1813
    private string ScanIdentifierParts()
    {
        var result = "";
        var start = _pos;
        while (_pos < _end)
        {
            var ch = CodePointUnchecked(_pos);
            if (ScannerUtilities.IsIdentifierPart(ch, _languageVersion))
            {
                _pos += ScannerUtilities.CharSize(ch);
            }
            else if (ch == CharacterCodes.Backslash)
            {
                ch = PeekExtendedUnicodeEscape();
                if (ch >= 0 && ScannerUtilities.IsIdentifierPart(ch, _languageVersion))
                {
                    result += ScanExtendedUnicodeEscape(shouldEmitInvalidEscapeError: true);
                    start = _pos;
                    continue;
                }

                ch = PeekUnicodeEscape();
                if (!(ch >= 0 && ScannerUtilities.IsIdentifierPart(ch, _languageVersion)))
                {
                    break;
                }

                _tokenFlags |= TokenFlags.UnicodeEscape;
                result += _text[start.._pos];
                result += ScannerUtilities.Utf16EncodeAsString(ch);

                // Valid Unicode escape is always six characters
                _pos += 6;
                start = _pos;
            }
            else
            {
                break;
            }
        }

        result += _text[start.._pos];
        return result;
    }

    // ported from: src/compiler/scanner.ts:1815-1828
    /// <summary>Classifies the current <c>tokenValue</c> as a keyword or an identifier.</summary>
    private SyntaxKind GetIdentifierToken()
    {
        // Reserved words are between 2 and 12 characters long and start with a lowercase letter
        var len = (_tokenValue ?? "").Length;
        if (len >= 2 && len <= 12)
        {
            var ch = (_tokenValue ?? "")[0];
            if (ch >= CharacterCodes.a && ch <= CharacterCodes.z)
            {
                var keyword = ScannerUtilities.StringToKeyword(_tokenValue!);
                if (keyword is not null) return _token = keyword.Value;
            }
        }

        return _token = SyntaxKind.Identifier;
    }

    // ported from: src/compiler/scanner.ts:1830-1867
    private string ScanBinaryOrOctalDigits(int @base)
    {
        var value = "";

        // For counting the number of digits: a valid binaryIntegerLiteral must have at least one binary digit
        // following B or b, and an octalIntegerLiteral at least one octal digit following o or O.
        var separatorAllowed = false;
        var isPreviousTokenSeparator = false;
        while (true)
        {
            var ch = CharCodeUnchecked(_pos);

            // Numeric separators are allowed anywhere within a numeric literal, except not at the beginning,
            // or following another separator.
            if (ch == CharacterCodes.Underscore)
            {
                _tokenFlags |= TokenFlags.ContainsSeparator;
                if (separatorAllowed)
                {
                    separatorAllowed = false;
                    isPreviousTokenSeparator = true;
                }
                else if (isPreviousTokenSeparator)
                {
                    Error(Diagnostics.Multiple_consecutive_numeric_separators_are_not_permitted, _pos, 1);
                }
                else
                {
                    Error(Diagnostics.Numeric_separators_are_not_allowed_here, _pos, 1);
                }

                _pos++;
                continue;
            }

            separatorAllowed = true;
            if (!ScannerUtilities.IsDigit(ch) || ch - CharacterCodes._0 >= @base) break;

            value += _text[_pos];
            _pos++;
            isPreviousTokenSeparator = false;
        }

        if (CharCodeUnchecked(_pos - 1) == CharacterCodes.Underscore)
        {
            // Literal ends with underscore - not allowed
            Error(Diagnostics.Numeric_separators_are_not_allowed_here, _pos - 1, 1);
        }

        return value;
    }

    // ported from: src/compiler/scanner.ts:1869-1889
    private SyntaxKind CheckBigIntSuffix()
    {
        if (CharCodeUnchecked(_pos) == CharacterCodes.n)
        {
            _tokenValue = (_tokenValue ?? "") + "n";

            // Use base 10 instead of base 2 or base 8 for shorter literals
            if ((_tokenFlags & TokenFlags.BinaryOrOctalSpecifier) != 0)
            {
                _tokenValue = PseudoBigInt.Parse(_tokenValue) + "n";
            }

            _pos++;
            return SyntaxKind.BigIntLiteral;
        }

        // not a bigint, so can convert to number in simplified form. Number() may not support 0b or 0o, so use
        // parseInt() instead.
        var text = _tokenValue ?? "";
        var numericValue = (_tokenFlags & TokenFlags.BinarySpecifier) != 0
            ? Convert.ToInt32(text[2..], 2)   // skip "0b"
            : (_tokenFlags & TokenFlags.OctalSpecifier) != 0
                ? Convert.ToInt32(text[2..], 8) // skip "0o"
                : Js.ToNumber(text);

        _tokenValue = Js.NumberToString(numericValue);
        return SyntaxKind.NumericLiteral;
    }

    // ---- Comment directives (scanner.ts:349-356, 3618-3657). ----

    // ported from: src/compiler/scanner.ts:349
    [GeneratedRegex(@"^//\/?\s*@(ts-expect-error|ts-ignore)")]
    private static partial Regex CommentDirectiveRegExSingleLine();

    // ported from: src/compiler/scanner.ts:354
    [GeneratedRegex(@"^(?:/|\*)*\s*@(ts-expect-error|ts-ignore)")]
    private static partial Regex CommentDirectiveRegExMultiLine();

    // ported from: src/compiler/scanner.ts:356
    /// <remarks>PORT-NOTE: JS's <c>/i</c> does not consult the ambient culture, hence CultureInvariant.</remarks>
    [GeneratedRegex(@"@(?:see|link)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JsDocSeeOrLink();

    // ported from: src/compiler/scanner.ts:3618-3637
    private IList<ICommentDirective>? AppendIfCommentDirective(
        IList<ICommentDirective>? commentDirectives,
        string text,
        Regex commentDirectiveRegEx,
        int lineStart)
    {
        var type = GetDirectiveFromComment(text.TrimStart(), commentDirectiveRegEx);
        if (type is null) return commentDirectives;

        return Core.Append(
            commentDirectives,
            new CommentDirective
            {
                Range = new TextRange { Pos = lineStart, End = _pos },
                Type = type.Value,
            });
    }

    // ported from: src/compiler/scanner.ts:3638-3657
    private static CommentDirectiveType? GetDirectiveFromComment(string text, Regex commentDirectiveRegEx)
    {
        var match = commentDirectiveRegEx.Match(text);
        if (!match.Success) return null;

        return match.Groups[1].Value switch
        {
            "ts-expect-error" => CommentDirectiveType.ExpectError,
            "ts-ignore" => CommentDirectiveType.Ignore,
            _ => null,
        };
    }

    // ---- Scanning (scanner.ts:1891-2436). ----

    // ported from: src/compiler/scanner.ts:1891-2388
    /// <summary>Advances to the next token and returns it.</summary>
    public SyntaxKind Scan()
    {
        _fullStartPos = _pos;
        _tokenFlags = TokenFlags.None;
        while (true)
        {
            _tokenStart = _pos;
            if (_pos >= _end) return _token = SyntaxKind.EndOfFileToken;

            var ch = CodePointUnchecked(_pos);
            if (_pos == 0)
            {
                // Special handling for shebang
                if (ch == CharacterCodes.Hash && ScannerUtilities.IsShebangTrivia(_text, _pos))
                {
                    _pos = ScannerUtilities.ScanShebangTrivia(_text, _pos);
                    if (_skipTrivia) continue;

                    return _token = SyntaxKind.ShebangTrivia;
                }
            }

            switch (ch)
            {
                case CharacterCodes.LineFeed:
                case CharacterCodes.CarriageReturn:
                    _tokenFlags |= TokenFlags.PrecedingLineBreak;
                    if (_skipTrivia)
                    {
                        _pos++;
                        continue;
                    }

                    if (ch == CharacterCodes.CarriageReturn && _pos + 1 < _end && CharCodeUnchecked(_pos + 1) == CharacterCodes.LineFeed)
                    {
                        // consume both CR and LF
                        _pos += 2;
                    }
                    else
                    {
                        _pos++;
                    }

                    return _token = SyntaxKind.NewLineTrivia;

                case CharacterCodes.Tab:
                case CharacterCodes.VerticalTab:
                case CharacterCodes.FormFeed:
                case CharacterCodes.Space:
                case CharacterCodes.NonBreakingSpace:
                case CharacterCodes.Ogham:
                case CharacterCodes.EnQuad:
                case CharacterCodes.EmQuad:
                case CharacterCodes.EnSpace:
                case CharacterCodes.EmSpace:
                case CharacterCodes.ThreePerEmSpace:
                case CharacterCodes.FourPerEmSpace:
                case CharacterCodes.SixPerEmSpace:
                case CharacterCodes.FigureSpace:
                case CharacterCodes.PunctuationSpace:
                case CharacterCodes.ThinSpace:
                case CharacterCodes.HairSpace:
                case CharacterCodes.ZeroWidthSpace:
                case CharacterCodes.NarrowNoBreakSpace:
                case CharacterCodes.MathematicalSpace:
                case CharacterCodes.IdeographicSpace:
                case CharacterCodes.ByteOrderMark:
                    if (_skipTrivia)
                    {
                        _pos++;
                        continue;
                    }

                    while (_pos < _end && ScannerUtilities.IsWhiteSpaceSingleLine(CharCodeUnchecked(_pos))) _pos++;
                    return _token = SyntaxKind.WhitespaceTrivia;

                case CharacterCodes.Exclamation:
                    if (CharCodeUnchecked(_pos + 1) == CharacterCodes.EqualsSign)
                    {
                        if (CharCodeUnchecked(_pos + 2) == CharacterCodes.EqualsSign)
                        {
                            _pos += 3;
                            return _token = SyntaxKind.ExclamationEqualsEqualsToken;
                        }

                        _pos += 2;
                        return _token = SyntaxKind.ExclamationEqualsToken;
                    }

                    _pos++;
                    return _token = SyntaxKind.ExclamationToken;

                case CharacterCodes.DoubleQuote:
                case CharacterCodes.SingleQuote:
                    _tokenValue = ScanString();
                    return _token = SyntaxKind.StringLiteral;

                case CharacterCodes.Backtick:
                    return _token = ScanTemplateAndSetTokenValue(shouldEmitInvalidEscapeError: false);

                case CharacterCodes.Percent:
                    if (CharCodeUnchecked(_pos + 1) == CharacterCodes.EqualsSign)
                    {
                        _pos += 2;
                        return _token = SyntaxKind.PercentEqualsToken;
                    }

                    _pos++;
                    return _token = SyntaxKind.PercentToken;

                case CharacterCodes.Ampersand:
                    if (CharCodeUnchecked(_pos + 1) == CharacterCodes.Ampersand)
                    {
                        if (CharCodeUnchecked(_pos + 2) == CharacterCodes.EqualsSign)
                        {
                            _pos += 3;
                            return _token = SyntaxKind.AmpersandAmpersandEqualsToken;
                        }

                        _pos += 2;
                        return _token = SyntaxKind.AmpersandAmpersandToken;
                    }

                    if (CharCodeUnchecked(_pos + 1) == CharacterCodes.EqualsSign)
                    {
                        _pos += 2;
                        return _token = SyntaxKind.AmpersandEqualsToken;
                    }

                    _pos++;
                    return _token = SyntaxKind.AmpersandToken;

                case CharacterCodes.OpenParen:
                    _pos++;
                    return _token = SyntaxKind.OpenParenToken;

                case CharacterCodes.CloseParen:
                    _pos++;
                    return _token = SyntaxKind.CloseParenToken;

                case CharacterCodes.Asterisk:
                    if (CharCodeUnchecked(_pos + 1) == CharacterCodes.EqualsSign)
                    {
                        _pos += 2;
                        return _token = SyntaxKind.AsteriskEqualsToken;
                    }

                    if (CharCodeUnchecked(_pos + 1) == CharacterCodes.Asterisk)
                    {
                        if (CharCodeUnchecked(_pos + 2) == CharacterCodes.EqualsSign)
                        {
                            _pos += 3;
                            return _token = SyntaxKind.AsteriskAsteriskEqualsToken;
                        }

                        _pos += 2;
                        return _token = SyntaxKind.AsteriskAsteriskToken;
                    }

                    _pos++;
                    if (_skipJsDocLeadingAsterisks != 0 &&
                        (_tokenFlags & TokenFlags.PrecedingJSDocLeadingAsterisks) == 0 &&
                        (_tokenFlags & TokenFlags.PrecedingLineBreak) != 0)
                    {
                        // decoration at the start of a JSDoc comment line
                        _tokenFlags |= TokenFlags.PrecedingJSDocLeadingAsterisks;
                        continue;
                    }

                    return _token = SyntaxKind.AsteriskToken;

                case CharacterCodes.Plus:
                    if (CharCodeUnchecked(_pos + 1) == CharacterCodes.Plus)
                    {
                        _pos += 2;
                        return _token = SyntaxKind.PlusPlusToken;
                    }

                    if (CharCodeUnchecked(_pos + 1) == CharacterCodes.EqualsSign)
                    {
                        _pos += 2;
                        return _token = SyntaxKind.PlusEqualsToken;
                    }

                    _pos++;
                    return _token = SyntaxKind.PlusToken;

                case CharacterCodes.Comma:
                    _pos++;
                    return _token = SyntaxKind.CommaToken;

                case CharacterCodes.Minus:
                    if (CharCodeUnchecked(_pos + 1) == CharacterCodes.Minus)
                    {
                        _pos += 2;
                        return _token = SyntaxKind.MinusMinusToken;
                    }

                    if (CharCodeUnchecked(_pos + 1) == CharacterCodes.EqualsSign)
                    {
                        _pos += 2;
                        return _token = SyntaxKind.MinusEqualsToken;
                    }

                    _pos++;
                    return _token = SyntaxKind.MinusToken;

                case CharacterCodes.Dot:
                    if (ScannerUtilities.IsDigit(CharCodeUnchecked(_pos + 1)))
                    {
                        ScanNumber();
                        return _token = SyntaxKind.NumericLiteral;
                    }

                    if (CharCodeUnchecked(_pos + 1) == CharacterCodes.Dot && CharCodeUnchecked(_pos + 2) == CharacterCodes.Dot)
                    {
                        _pos += 3;
                        return _token = SyntaxKind.DotDotDotToken;
                    }

                    _pos++;
                    return _token = SyntaxKind.DotToken;

                case CharacterCodes.Slash:
                    // Single-line comment
                    if (CharCodeUnchecked(_pos + 1) == CharacterCodes.Slash)
                    {
                        _pos += 2;
                        while (_pos < _end)
                        {
                            if (ScannerUtilities.IsLineBreak(CharCodeUnchecked(_pos))) break;
                            _pos++;
                        }

                        _commentDirectives = AppendIfCommentDirective(
                            _commentDirectives,
                            _text[_tokenStart.._pos],
                            CommentDirectiveRegExSingleLine(),
                            _tokenStart);

                        if (_skipTrivia) continue;

                        return _token = SyntaxKind.SingleLineCommentTrivia;
                    }

                    // Multi-line comment
                    if (CharCodeUnchecked(_pos + 1) == CharacterCodes.Asterisk)
                    {
                        _pos += 2;
                        var isJSDoc = CharCodeUnchecked(_pos) == CharacterCodes.Asterisk &&
                            CharCodeUnchecked(_pos + 1) != CharacterCodes.Slash;

                        var commentClosed = false;
                        var lastLineStart = _tokenStart;
                        while (_pos < _end)
                        {
                            var commentCh = CharCodeUnchecked(_pos);

                            if (commentCh == CharacterCodes.Asterisk && CharCodeUnchecked(_pos + 1) == CharacterCodes.Slash)
                            {
                                _pos += 2;
                                commentClosed = true;
                                break;
                            }

                            _pos++;

                            if (ScannerUtilities.IsLineBreak(commentCh))
                            {
                                lastLineStart = _pos;
                                _tokenFlags |= TokenFlags.PrecedingLineBreak;
                            }
                        }

                        if (isJSDoc && ShouldParseJSDoc()) _tokenFlags |= TokenFlags.PrecedingJSDocComment;

                        _commentDirectives = AppendIfCommentDirective(
                            _commentDirectives,
                            _text[lastLineStart.._pos],
                            CommentDirectiveRegExMultiLine(),
                            lastLineStart);

                        if (!commentClosed) Error(Diagnostics.Asterisk_Slash_expected);

                        if (_skipTrivia) continue;

                        if (!commentClosed) _tokenFlags |= TokenFlags.Unterminated;
                        return _token = SyntaxKind.MultiLineCommentTrivia;
                    }

                    if (CharCodeUnchecked(_pos + 1) == CharacterCodes.EqualsSign)
                    {
                        _pos += 2;
                        return _token = SyntaxKind.SlashEqualsToken;
                    }

                    _pos++;
                    return _token = SyntaxKind.SlashToken;

                case CharacterCodes._0:
                    if (_pos + 2 < _end && (CharCodeUnchecked(_pos + 1) == CharacterCodes.X || CharCodeUnchecked(_pos + 1) == CharacterCodes.x))
                    {
                        _pos += 2;
                        _tokenValue = ScanMinimumNumberOfHexDigits(1, canHaveSeparators: true);
                        if (!Js.IsTruthy(_tokenValue))
                        {
                            Error(Diagnostics.Hexadecimal_digit_expected);
                            _tokenValue = "0";
                        }

                        _tokenValue = "0x" + _tokenValue;
                        _tokenFlags |= TokenFlags.HexSpecifier;
                        return _token = CheckBigIntSuffix();
                    }

                    if (_pos + 2 < _end && (CharCodeUnchecked(_pos + 1) == CharacterCodes.B || CharCodeUnchecked(_pos + 1) == CharacterCodes.b))
                    {
                        _pos += 2;
                        _tokenValue = ScanBinaryOrOctalDigits(2);
                        if (!Js.IsTruthy(_tokenValue))
                        {
                            Error(Diagnostics.Binary_digit_expected);
                            _tokenValue = "0";
                        }

                        _tokenValue = "0b" + _tokenValue;
                        _tokenFlags |= TokenFlags.BinarySpecifier;
                        return _token = CheckBigIntSuffix();
                    }

                    if (_pos + 2 < _end && (CharCodeUnchecked(_pos + 1) == CharacterCodes.O || CharCodeUnchecked(_pos + 1) == CharacterCodes.o))
                    {
                        _pos += 2;
                        _tokenValue = ScanBinaryOrOctalDigits(8);
                        if (!Js.IsTruthy(_tokenValue))
                        {
                            Error(Diagnostics.Octal_digit_expected);
                            _tokenValue = "0";
                        }

                        _tokenValue = "0o" + _tokenValue;
                        _tokenFlags |= TokenFlags.OctalSpecifier;
                        return _token = CheckBigIntSuffix();
                    }

                    goto case CharacterCodes._1;

                case CharacterCodes._1:
                case CharacterCodes._2:
                case CharacterCodes._3:
                case CharacterCodes._4:
                case CharacterCodes._5:
                case CharacterCodes._6:
                case CharacterCodes._7:
                case CharacterCodes._8:
                case CharacterCodes._9:
                    return _token = ScanNumber();

                case CharacterCodes.Colon:
                    _pos++;
                    return _token = SyntaxKind.ColonToken;

                case CharacterCodes.Semicolon:
                    _pos++;
                    return _token = SyntaxKind.SemicolonToken;

                case CharacterCodes.LessThan:
                    if (ScannerUtilities.IsConflictMarkerTrivia(_text, _pos))
                    {
                        _pos = ScannerUtilities.ScanConflictMarkerTrivia(_text, _pos, (message, errPos, length) => Error(message, errPos, length));
                        if (_skipTrivia) continue;

                        return _token = SyntaxKind.ConflictMarkerTrivia;
                    }

                    if (CharCodeUnchecked(_pos + 1) == CharacterCodes.LessThan)
                    {
                        if (CharCodeUnchecked(_pos + 2) == CharacterCodes.EqualsSign)
                        {
                            _pos += 3;
                            return _token = SyntaxKind.LessThanLessThanEqualsToken;
                        }

                        _pos += 2;
                        return _token = SyntaxKind.LessThanLessThanToken;
                    }

                    if (CharCodeUnchecked(_pos + 1) == CharacterCodes.EqualsSign)
                    {
                        _pos += 2;
                        return _token = SyntaxKind.LessThanEqualsToken;
                    }

                    if (_languageVariant == LanguageVariant.JSX &&
                        CharCodeUnchecked(_pos + 1) == CharacterCodes.Slash &&
                        CharCodeUnchecked(_pos + 2) != CharacterCodes.Asterisk)
                    {
                        _pos += 2;
                        return _token = SyntaxKind.LessThanSlashToken;
                    }

                    _pos++;
                    return _token = SyntaxKind.LessThanToken;

                case CharacterCodes.EqualsSign:
                    if (ScannerUtilities.IsConflictMarkerTrivia(_text, _pos))
                    {
                        _pos = ScannerUtilities.ScanConflictMarkerTrivia(_text, _pos, (message, errPos, length) => Error(message, errPos, length));
                        if (_skipTrivia) continue;

                        return _token = SyntaxKind.ConflictMarkerTrivia;
                    }

                    if (CharCodeUnchecked(_pos + 1) == CharacterCodes.EqualsSign)
                    {
                        if (CharCodeUnchecked(_pos + 2) == CharacterCodes.EqualsSign)
                        {
                            _pos += 3;
                            return _token = SyntaxKind.EqualsEqualsEqualsToken;
                        }

                        _pos += 2;
                        return _token = SyntaxKind.EqualsEqualsToken;
                    }

                    if (CharCodeUnchecked(_pos + 1) == CharacterCodes.GreaterThan)
                    {
                        _pos += 2;
                        return _token = SyntaxKind.EqualsGreaterThanToken;
                    }

                    _pos++;
                    return _token = SyntaxKind.EqualsToken;

                case CharacterCodes.GreaterThan:
                    if (ScannerUtilities.IsConflictMarkerTrivia(_text, _pos))
                    {
                        _pos = ScannerUtilities.ScanConflictMarkerTrivia(_text, _pos, (message, errPos, length) => Error(message, errPos, length));
                        if (_skipTrivia) continue;

                        return _token = SyntaxKind.ConflictMarkerTrivia;
                    }

                    _pos++;
                    return _token = SyntaxKind.GreaterThanToken;

                case CharacterCodes.Question:
                    if (CharCodeUnchecked(_pos + 1) == CharacterCodes.Dot &&
                        !ScannerUtilities.IsDigit(CharCodeUnchecked(_pos + 2)))
                    {
                        _pos += 2;
                        return _token = SyntaxKind.QuestionDotToken;
                    }

                    if (CharCodeUnchecked(_pos + 1) == CharacterCodes.Question)
                    {
                        if (CharCodeUnchecked(_pos + 2) == CharacterCodes.EqualsSign)
                        {
                            _pos += 3;
                            return _token = SyntaxKind.QuestionQuestionEqualsToken;
                        }

                        _pos += 2;
                        return _token = SyntaxKind.QuestionQuestionToken;
                    }

                    _pos++;
                    return _token = SyntaxKind.QuestionToken;

                case CharacterCodes.OpenBracket:
                    _pos++;
                    return _token = SyntaxKind.OpenBracketToken;

                case CharacterCodes.CloseBracket:
                    _pos++;
                    return _token = SyntaxKind.CloseBracketToken;

                case CharacterCodes.Caret:
                    if (CharCodeUnchecked(_pos + 1) == CharacterCodes.EqualsSign)
                    {
                        _pos += 2;
                        return _token = SyntaxKind.CaretEqualsToken;
                    }

                    _pos++;
                    return _token = SyntaxKind.CaretToken;

                case CharacterCodes.OpenBrace:
                    _pos++;
                    return _token = SyntaxKind.OpenBraceToken;

                case CharacterCodes.Bar:
                    if (ScannerUtilities.IsConflictMarkerTrivia(_text, _pos))
                    {
                        _pos = ScannerUtilities.ScanConflictMarkerTrivia(_text, _pos, (message, errPos, length) => Error(message, errPos, length));
                        if (_skipTrivia) continue;

                        return _token = SyntaxKind.ConflictMarkerTrivia;
                    }

                    if (CharCodeUnchecked(_pos + 1) == CharacterCodes.Bar)
                    {
                        if (CharCodeUnchecked(_pos + 2) == CharacterCodes.EqualsSign)
                        {
                            _pos += 3;
                            return _token = SyntaxKind.BarBarEqualsToken;
                        }

                        _pos += 2;
                        return _token = SyntaxKind.BarBarToken;
                    }

                    if (CharCodeUnchecked(_pos + 1) == CharacterCodes.EqualsSign)
                    {
                        _pos += 2;
                        return _token = SyntaxKind.BarEqualsToken;
                    }

                    _pos++;
                    return _token = SyntaxKind.BarToken;

                case CharacterCodes.CloseBrace:
                    _pos++;
                    return _token = SyntaxKind.CloseBraceToken;

                case CharacterCodes.Tilde:
                    _pos++;
                    return _token = SyntaxKind.TildeToken;

                case CharacterCodes.At:
                    _pos++;
                    return _token = SyntaxKind.AtToken;

                case CharacterCodes.Backslash:
                {
                    var extendedCookedChar = PeekExtendedUnicodeEscape();
                    if (extendedCookedChar >= 0 && ScannerUtilities.IsIdentifierStart(extendedCookedChar, _languageVersion))
                    {
                        _tokenValue = ScanExtendedUnicodeEscape(shouldEmitInvalidEscapeError: true) + ScanIdentifierParts();
                        return _token = GetIdentifierToken();
                    }

                    var cookedChar = PeekUnicodeEscape();
                    if (cookedChar >= 0 && ScannerUtilities.IsIdentifierStart(cookedChar, _languageVersion))
                    {
                        _pos += 6;
                        _tokenFlags |= TokenFlags.UnicodeEscape;
                        _tokenValue = ((char)cookedChar).ToString() + ScanIdentifierParts();
                        return _token = GetIdentifierToken();
                    }

                    Error(Diagnostics.Invalid_character);
                    _pos++;
                    return _token = SyntaxKind.Unknown;
                }

                case CharacterCodes.Hash:
                {
                    if (_pos != 0 && _pos + 1 < _text.Length && _text[_pos + 1] == '!')
                    {
                        Error(Diagnostics.can_only_be_used_at_the_start_of_a_file, _pos, 2);
                        _pos++;
                        return _token = SyntaxKind.Unknown;
                    }

                    var charAfterHash = CodePointUnchecked(_pos + 1);
                    if (charAfterHash == CharacterCodes.Backslash)
                    {
                        _pos++;
                        var extendedCookedChar = PeekExtendedUnicodeEscape();
                        if (extendedCookedChar >= 0 && ScannerUtilities.IsIdentifierStart(extendedCookedChar, _languageVersion))
                        {
                            _tokenValue = "#" + ScanExtendedUnicodeEscape(shouldEmitInvalidEscapeError: true) + ScanIdentifierParts();
                            return _token = SyntaxKind.PrivateIdentifier;
                        }

                        var cookedChar = PeekUnicodeEscape();
                        if (cookedChar >= 0 && ScannerUtilities.IsIdentifierStart(cookedChar, _languageVersion))
                        {
                            _pos += 6;
                            _tokenFlags |= TokenFlags.UnicodeEscape;
                            _tokenValue = "#" + ((char)cookedChar).ToString() + ScanIdentifierParts();
                            return _token = SyntaxKind.PrivateIdentifier;
                        }

                        _pos--;
                    }

                    if (ScannerUtilities.IsIdentifierStart(charAfterHash, _languageVersion))
                    {
                        _pos++;

                        // We rely on scanIdentifier's behaviour and adjust the token kind afterwards: it starts at
                        // tokenStart, which still includes the '#', so the '#' ends up prepended for us.
                        ScanIdentifier(charAfterHash, _languageVersion);
                    }
                    else
                    {
                        _tokenValue = "#";
                        Error(Diagnostics.Invalid_character, _pos, ScannerUtilities.CharSize(ch));
                        _pos++;
                    }

                    return _token = SyntaxKind.PrivateIdentifier;
                }

                case CharacterCodes.ReplacementCharacter:
                    Error(Diagnostics.File_appears_to_be_binary, 0, 0);
                    _pos = _end;
                    return _token = SyntaxKind.NonTextFileMarkerTrivia;

                default:
                {
                    var identifierKind = ScanIdentifier(ch, _languageVersion);
                    if (identifierKind is not null) return _token = identifierKind.Value;

                    if (ScannerUtilities.IsWhiteSpaceSingleLine(ch))
                    {
                        _pos += ScannerUtilities.CharSize(ch);
                        continue;
                    }

                    if (ScannerUtilities.IsLineBreak(ch))
                    {
                        _tokenFlags |= TokenFlags.PrecedingLineBreak;
                        _pos += ScannerUtilities.CharSize(ch);
                        continue;
                    }

                    var size = ScannerUtilities.CharSize(ch);
                    Error(Diagnostics.Invalid_character, _pos, size);
                    _pos += size;
                    return _token = SyntaxKind.Unknown;
                }
            }
        }
    }

    // ported from: src/compiler/scanner.ts:2390-2410
    private bool ShouldParseJSDoc()
    {
        switch (_jsDocParsingMode)
        {
            case JSDocParsingMode.ParseAll:
                return true;
            case JSDocParsingMode.ParseNone:
                return false;
        }

        if (_scriptKind != ScriptKind.TS && _scriptKind != ScriptKind.TSX)
        {
            // If outside of TS, we need JSDoc to get any type info.
            return true;
        }

        if (_jsDocParsingMode == JSDocParsingMode.ParseForTypeInfo)
        {
            // If we're in TS, but we don't need to produce reliable errors,
            // we don't need to parse to find @see or @link.
            return false;
        }

        return JsDocSeeOrLink().IsMatch(_text[_fullStartPos.._pos]);
    }

    // ported from: src/compiler/scanner.ts:2412-2423
    public SyntaxKind ReScanInvalidIdentifier()
    {
        Debug.Assert(
            _token == SyntaxKind.Unknown,
            "'reScanInvalidIdentifier' should only be called when the current token is 'SyntaxKind.Unknown'.");

        _pos = _tokenStart = _fullStartPos;
        _tokenFlags = 0;
        var ch = CodePointUnchecked(_pos);
        var identifierKind = ScanIdentifier(ch, ScriptTarget.ESNext);
        if (identifierKind is not null) return _token = identifierKind.Value;

        _pos += ScannerUtilities.CharSize(ch);
        return _token; // Still `SyntaxKind.Unknown`
    }

    // ported from: src/compiler/scanner.ts:2425-2436
    private SyntaxKind? ScanIdentifier(int startCharacter, ScriptTarget languageVersion)
    {
        var ch = startCharacter;
        if (ScannerUtilities.IsIdentifierStart(ch, languageVersion))
        {
            _pos += ScannerUtilities.CharSize(ch);
            while (_pos < _end && ScannerUtilities.IsIdentifierPart(ch = CodePointUnchecked(_pos), languageVersion))
            {
                _pos += ScannerUtilities.CharSize(ch);
            }

            _tokenValue = _text[_tokenStart.._pos];
            if (ch == CharacterCodes.Backslash) _tokenValue += ScanIdentifierParts();
            return GetIdentifierToken();
        }

        return null;
    }
}
