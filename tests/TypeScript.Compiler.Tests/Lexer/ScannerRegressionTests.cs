namespace TypeScript.Compiler.Tests;

/// <summary>
/// Hermetic regressions for the scanner defects the M4g P1a differential found (docs/architecture.md §15.11).
/// </summary>
/// <remarks>
/// The differential itself needs the reference checkout; these three do not. Expected values come from node v24 and from
/// TS 6.0.3's <c>lib/typescript.js</c> <c>createScanner</c> for the same inputs, and each test names the failure mode it
/// pins so the next reader can tell why the expectation is what it is.
/// </remarks>
public class ScannerRegressionTests
{
    private static (SyntaxKind Kind, string? Value, List<DiagnosticMessage> Messages) Scan(string text)
    {
        var messages = new List<DiagnosticMessage>();
        var scanner = Scanner.Create(
            ScriptTarget.ESNext,
            skipTrivia: true,
            LanguageVariant.Standard,
            text,
            (message, _, _) => messages.Add(message));
        var kind = scanner.Scan();
        return (kind, scanner.GetTokenValue(), messages);
    }

    [Fact]
    public void ExtendedUnicodeEscape_InTheSurrogateRange_KeepsTheLoneSurrogate()
    {
        // JS's String.fromCodePoint accepts the surrogate range (it only rejects values above 0x10FFFF) and answers that
        // lone code unit, and TS's utf16EncodeAsString goes through the `<= 0xFFFF` branch for it. .NET's
        // char.ConvertFromUtf32 throws for 0xD800-0xDFFF, which crashed 4 corpus files before this test existed.
        var (kind, value, messages) = Scan("\"\\u{D800}\"");

        Assert.Equal(SyntaxKind.StringLiteral, kind);
        Assert.Equal("\uD800", value);
        Assert.Empty(messages);
    }

    [Fact]
    public void ExtendedUnicodeEscape_AboveTheRange_ReportsTheRangeDiagnostic()
    {
        // parseInt("FFFFFFFF", 16) is 4294967295, i.e. above 0x10FFFF, so TS keeps the raw text and reports 1198. A
        // narrowing conversion overflows (or answers -1) and takes the "no digits" branch instead, reporting 1125.
        var (kind, value, messages) = Scan("\"\\u{FFFFFFFF}\"");

        Assert.Equal(SyntaxKind.StringLiteral, kind);
        Assert.Equal("\\u{FFFFFFFF}", value);
        Assert.Contains(messages, static m => m.Code == Diagnostics.An_extended_Unicode_escape_value_must_be_between_0x0_and_0x10FFFF_inclusive.Code);
        Assert.DoesNotContain(messages, static m => m.Code == Diagnostics.Hexadecimal_digit_expected.Code);
    }

    [Fact]
    public void HugeBinaryLiteral_FormatsLikeParseInt()
    {
        // `String(parseInt("1".repeat(600), 2))` in node v24 is exactly this string: parseInt accumulates in double
        // arithmetic, so the value is finite and slightly imprecise, and the parser stores the JS-formatted text as
        // NumericLiteral.Text. Convert.ToInt32 threw OverflowException on the same input (4 corpus files).
        var (kind, value, messages) = Scan("0b" + new string('1', 600));

        Assert.Equal(SyntaxKind.NumericLiteral, kind);
        Assert.Equal("4.149515568880993e+180", value);
        Assert.Empty(messages);
    }
}
