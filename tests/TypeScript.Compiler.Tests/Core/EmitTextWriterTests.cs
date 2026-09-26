namespace TypeScript.Compiler.Tests;

/// <summary>
/// Pins the Layer 7 text-writer layer (<see cref="Utilities.CreateTextWriter"/>,
/// <see cref="Utilities.GetTrailingSemicolonDeferringWriter"/>) — the reference's <c>createTextWriter</c>
/// (utilities.ts:6298-6408) and its deferring wrapper (utilities.ts:6412-6470), which the printer writes every
/// character through.
/// </summary>
/// <remarks>
/// What the printer depends on and a reader cannot see from the signature: indentation is inserted only at the start
/// of a line, <c>writeLine</c> is a no-op on an empty line unless forced, positions are counted in UTF-16 units and
/// updated for text that itself contains newlines, and the deferring writer holds one pending <c>;</c> back until
/// anything else is written.
/// </remarks>
public class EmitTextWriterTests
{
    [Fact]
    public void WriteText_IndentsOnlyAtTheStartOfALine()
    {
        var writer = Utilities.CreateTextWriter("\n");
        writer.IncreaseIndent();
        writer.WriteKeyword("class");
        writer.WriteSpace(" ");
        writer.Write("C");
        writer.WriteLine(null);

        // At the start of a line again, so the second line is indented too.
        writer.WritePunctuation("{");
        writer.WriteLine(null);

        Assert.Equal("    class C\n    {\n", writer.GetText());
        Assert.Equal(1, writer.GetIndent());
        Assert.Equal(2, writer.GetLine());
        Assert.True(writer.IsAtStartOfLine());

        writer.DecreaseIndent();
        Assert.Equal(0, writer.GetIndent());
    }

    [Fact]
    public void WriteLine_IsANoOpOnAnEmptyLineUnlessForced()
    {
        var writer = Utilities.CreateTextWriter("\n");
        writer.WriteLine(null);                 // nothing written yet: no newline
        Assert.Equal("", writer.GetText());

        writer.WriteLine(true);                 // forced: the reference writes the newline anyway
        writer.Write("a");
        writer.WriteLine(null);
        writer.WriteLine(null);                 // already at the start of a line
        writer.WriteLine(true);
        Assert.Equal("\na\n\n", writer.GetText());
        Assert.Equal(3, writer.GetLine());
    }

    [Fact]
    public void Positions_CountUtf16UnitsAndMultiLineText()
    {
        var writer = Utilities.CreateTextWriter("\n");
        writer.Write("abc");
        Assert.Equal(3, writer.GetTextPos());
        Assert.Equal(3, writer.GetColumn());

        writer.Write("de\nf");
        Assert.Equal(7, writer.GetTextPos());
        // `f` sits one UTF-16 unit past the last line start, so the column is 1 and the writer knows it is not at the
        // start of a line (the reference updates both from the newline positions inside the text just written).
        Assert.Equal(1, writer.GetColumn());
        Assert.False(writer.IsAtStartOfLine());
        Assert.Equal(1, writer.GetLine());

        writer.WriteComment("/* c */");
        Assert.True(writer.HasTrailingComment());
        Assert.False(writer.HasTrailingWhitespace());

        writer.WriteSpace(" ");
        Assert.True(writer.HasTrailingWhitespace());
        Assert.False(writer.HasTrailingComment());   // a real write clears the comment flag
        Assert.Equal("abcde\nf/* c */ ", writer.GetText());
    }

    [Fact]
    public void TrailingSemicolonDeferringWriter_HoldsTheSemicolonUntilSomethingElseIsWritten()
    {
        var inner = Utilities.CreateTextWriter("\n");
        var writer = Utilities.GetTrailingSemicolonDeferringWriter(inner);

        writer.Write("a");
        writer.WriteTrailingSemicolon(";");
        writer.WriteTrailingSemicolon(";");
        Assert.Equal("a", inner.GetText());          // both pending, nothing committed

        writer.WritePunctuation(")");
        Assert.Equal("a;)", inner.GetText());        // one semicolon total, printed before the ')'

        writer.WriteTrailingSemicolon(";");
        writer.WriteLine(null);
        Assert.Equal("a;);\n", inner.GetText());
        Assert.Equal("a;);\n", writer.GetText());    // reads pass through to the wrapped writer
    }
}
