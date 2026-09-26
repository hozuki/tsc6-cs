namespace TypeScript.Compiler.Tests;

/// <summary>
/// scanner 的纯函数部分：行列换算、字符分类、标识符判定、拼写查找。
/// </summary>
/// <remarks>
/// 期望值取自 TS 6.0.3 <c>lib/typescript.js</c> 的同名导出；少数几个（<c>codePointAt</c>、<c>charSize</c>）
/// 没有导出，用的是 ES 规范语义。
/// </remarks>
public class ScannerUtilitiesTests
{
    private const string MixedTerminators = "a\nb\r\nc\u2028d";

    [Fact]
    public void ComputeLineStarts_HandlesAllFourLineTerminators()
    {
        // \n、\r\n 与 \u2028 各算一个换行；\u2029 同理。
        Assert.Equal([0, 2, 5, 7], ScannerUtilities.ComputeLineStarts(MixedTerminators));
        Assert.Equal([0], ScannerUtilities.ComputeLineStarts("abc"));
        Assert.Equal([0, 4], ScannerUtilities.ComputeLineStarts("abc\n"));
    }

    [Fact]
    public void ComputeLineOfPosition_FindsTheLineContainingThePosition()
    {
        var lineStarts = ScannerUtilities.ComputeLineStarts(MixedTerminators);

        Assert.Equal(0, ScannerUtilities.ComputeLineOfPosition(lineStarts, 0));
        Assert.Equal(1, ScannerUtilities.ComputeLineOfPosition(lineStarts, 2));
        Assert.Equal(1, ScannerUtilities.ComputeLineOfPosition(lineStarts, 3));
        Assert.Equal(2, ScannerUtilities.ComputeLineOfPosition(lineStarts, 5));
    }

    [Fact]
    public void ComputeLineAndCharacterOfPosition_IsRelativeToTheLineStart()
    {
        var lineStarts = ScannerUtilities.ComputeLineStarts(MixedTerminators);
        var atFive = ScannerUtilities.ComputeLineAndCharacterOfPosition(lineStarts, 5);

        Assert.Equal(2, atFive.Line);
        Assert.Equal(0, atFive.Character);
    }

    [Fact]
    public void ComputePositionOfLineAndCharacter_IsTheInverse()
    {
        var lineStarts = ScannerUtilities.ComputeLineStarts(MixedTerminators);

        Assert.Equal(2, ScannerUtilities.ComputePositionOfLineAndCharacter(lineStarts, 1, 0));
        Assert.Equal(4, ScannerUtilities.ComputePositionOfLineAndCharacter(lineStarts, 2, -1));
    }

    [Theory]
    [InlineData(0x0A, true)]   // line feed
    [InlineData(0x0D, true)]   // carriage return
    [InlineData(0x2028, true)] // line separator
    [InlineData(0x2029, true)] // paragraph separator
    [InlineData(0x20, false)]  // space
    [InlineData(0x85, false)]  // next line — white space, but not a line terminator
    public void IsLineBreak_OnlyTheFourTerminators(int ch, bool expected)
        => Assert.Equal(expected, ScannerUtilities.IsLineBreak(ch));

    [Theory]
    [InlineData(0x85, true)]
    [InlineData(0x2028, true)]
    [InlineData(0x20, true)]
    [InlineData(0x09, true)]
    [InlineData(0xFEFF, true)] // byte order mark
    [InlineData('a', false)]
    public void IsWhiteSpaceLike_IncludesTheUnicodeSpaces(int ch, bool expected)
        => Assert.Equal(expected, ScannerUtilities.IsWhiteSpaceLike(ch));

    [Fact]
    public void IsIdentifierStart_ChoosesTheUnicodeTableByTarget()
    {
        // é is in the ES5 table, 0x10000 only in the ES2015+ one.
        Assert.True(ScannerUtilities.IsIdentifierStart(0xE9, ScriptTarget.ES3));
        Assert.True(ScannerUtilities.IsIdentifierStart(0xE9, ScriptTarget.ES5));
        Assert.False(ScannerUtilities.IsIdentifierStart(0x10000, ScriptTarget.ES5));
        Assert.True(ScannerUtilities.IsIdentifierStart(0x10000, ScriptTarget.ES2015));

        Assert.True(ScannerUtilities.IsIdentifierStart('a', ScriptTarget.ES5));
        Assert.True(ScannerUtilities.IsIdentifierStart('_', ScriptTarget.ES5));
        Assert.True(ScannerUtilities.IsIdentifierStart('$', ScriptTarget.ES5));
        Assert.False(ScannerUtilities.IsIdentifierStart('1', ScriptTarget.ES5));
    }

    [Fact]
    public void IsIdentifierPart_AllowsDashAndColonOnlyInJsx()
    {
        Assert.False(ScannerUtilities.IsIdentifierPart('-', ScriptTarget.ES5));
        Assert.True(ScannerUtilities.IsIdentifierPart('-', ScriptTarget.ES5, LanguageVariant.JSX));
        Assert.True(ScannerUtilities.IsIdentifierPart(':', ScriptTarget.ES5, LanguageVariant.JSX));
        Assert.True(ScannerUtilities.IsIdentifierPart('1', ScriptTarget.ES5));
    }

    [Fact]
    public void IsIdentifierText_WalksCodePoints()
    {
        Assert.True(ScannerUtilities.IsIdentifierText("abc", ScriptTarget.ES5));
        Assert.False(ScannerUtilities.IsIdentifierText("a b", ScriptTarget.ES5));
        Assert.False(ScannerUtilities.IsIdentifierText("a\u2028b", ScriptTarget.ES5));
        Assert.True(ScannerUtilities.IsIdentifierText("\U00010000x", ScriptTarget.ES2015));
    }

    [Fact]
    public void TokenSpellings_RoundTrip()
    {
        Assert.Equal("=>", ScannerUtilities.TokenToString(SyntaxKind.EqualsGreaterThanToken));
        Assert.Equal(SyntaxKind.EqualsGreaterThanToken, ScannerUtilities.StringToToken("=>"));
        Assert.Equal(SyntaxKind.ConstKeyword, ScannerUtilities.StringToKeyword("const"));

        // A kind with no spelling has no string, and a spelling no token uses is not a token.
        Assert.Null(ScannerUtilities.TokenToString(SyntaxKind.Identifier));
        Assert.Null(ScannerUtilities.StringToToken("not a token"));
        Assert.Null(ScannerUtilities.StringToKeyword("not a keyword"));
    }

    [Fact]
    public void RegularExpressionFlags_RoundTripThroughTheirCharacters()
    {
        Assert.Equal('d', ScannerUtilities.RegularExpressionFlagToCharacterCode(RegularExpressionFlags.HasIndices));
        Assert.Equal(RegularExpressionFlags.Sticky, ScannerUtilities.CharacterCodeToRegularExpressionFlag('y'));
        Assert.Null(ScannerUtilities.CharacterCodeToRegularExpressionFlag('z'));
    }

    [Theory]
    [InlineData("a", false)]
    [InlineData(" /", true)]
    [InlineData("#!x", true)]  // shebang, but only at position 0
    [InlineData("\n", true)]
    [InlineData("<", true)]    // conflict marker
    public void CouldStartTrivia_MatchesWhatSkipTriviaHandles(string text, bool expected)
        => Assert.Equal(expected, ScannerUtilities.CouldStartTrivia(text, 0));

    [Fact]
    public void CodePoints_AreDecodedAndEncoded()
    {
        const string text = "a\U0001F600b";

        Assert.Equal('a', ScannerUtilities.CodePointAt(text, 0));
        Assert.Equal(0x1F600, ScannerUtilities.CodePointAt(text, 1));
        Assert.Equal('b', ScannerUtilities.CodePointAt(text, 3));

        // An unpaired surrogate reads as itself, as in JS (char.ConvertToUtf32 would throw).
        Assert.Equal(0xD83D, ScannerUtilities.CodePointAt("a\uD83D", 1));

        Assert.Equal(2, ScannerUtilities.CharSize(0x1F600));
        Assert.Equal(1, ScannerUtilities.CharSize('a'));
        Assert.Equal(0, ScannerUtilities.CharSize(CharacterCodes.EOF));

        Assert.Equal("\U0001F600", ScannerUtilities.Utf16EncodeAsString(0x1F600));
        Assert.Equal("A", ScannerUtilities.Utf16EncodeAsString(65));
    }
}
