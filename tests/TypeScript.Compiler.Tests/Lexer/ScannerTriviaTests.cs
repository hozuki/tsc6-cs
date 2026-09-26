namespace TypeScript.Compiler.Tests;

/// <summary>
/// trivia 与注释范围（<c>scanner.ts:641-1000</c>）。
/// </summary>
/// <remarks>期望值取自 TS 6.0.3 <c>lib/typescript.js</c> 的同名导出。</remarks>
public class ScannerTriviaTests
{
    private const string Leading = "  // lead\n/* multi */\nfoo";

    [Fact]
    public void SkipTrivia_ConsumesWhiteSpaceLineBreaksAndComments()
    {
        // 两个空格、单行注释、多行注释与它们后面的换行都算 trivia。
        Assert.Equal(22, ScannerUtilities.SkipTrivia(Leading, 0));

        Assert.Equal(10, ScannerUtilities.SkipTrivia(Leading, 0, stopAfterLineBreak: true));
        Assert.Equal(2, ScannerUtilities.SkipTrivia(Leading, 0, stopAtComments: true));
    }

    [Fact]
    public void SkipTrivia_LeavesSynthesizedPositionsAlone()
        => Assert.Equal(-1, ScannerUtilities.SkipTrivia("abc", -1));

    [Fact]
    public void SkipTrivia_ConsumesMergeConflictMarkers()
    {
        const string text = "a\n<<<<<<< HEAD\nb\n=======\nc\n>>>>>>> other\nd";

        Assert.Equal(15, ScannerUtilities.SkipTrivia(text, 2));
    }

    [Fact]
    public void GetShebang_StopsAtEveryLineTerminator()
    {
        Assert.Equal("#!/usr/bin/env node", ScannerUtilities.GetShebang("#!/usr/bin/env node\nfoo"));
        Assert.Null(ScannerUtilities.GetShebang("foo"));

        // JS's `.` excludes every line terminator, not just \n.
        Assert.Equal("#!x", ScannerUtilities.GetShebang("#!x\rfoo"));
    }

    [Fact]
    public void GetLeadingCommentRanges_ReportsBothKinds()
    {
        var ranges = ScannerUtilities.GetLeadingCommentRanges(Leading, 0);

        Assert.NotNull(ranges);
        Assert.Equal(2, ranges.Count);

        // A single-line range includes the `//` but not the line break it stops at.
        Assert.Equal(SyntaxKind.SingleLineCommentTrivia, ranges[0].Kind);
        Assert.Equal(2, ranges[0].Pos);
        Assert.Equal(9, ranges[0].End);
        Assert.True(ranges[0].HasTrailingNewLine);

        // A multi-line range includes both delimiters.
        Assert.Equal(SyntaxKind.MultiLineCommentTrivia, ranges[1].Kind);
        Assert.Equal(10, ranges[1].Pos);
        Assert.Equal(21, ranges[1].End);
    }

    [Fact]
    public void GetTrailingCommentRanges_StopsAtTheLineBreak()
    {
        var ranges = ScannerUtilities.GetTrailingCommentRanges("foo // trail\nbar", 3);

        Assert.NotNull(ranges);
        Assert.Single(ranges);
        Assert.Equal(SyntaxKind.SingleLineCommentTrivia, ranges[0].Kind);
        Assert.Equal(4, ranges[0].Pos);
        Assert.Equal(12, ranges[0].End);
        Assert.True(ranges[0].HasTrailingNewLine);
    }

    [Fact]
    public void ForEachLeadingCommentRange_StopsAtTheFirstSetResult()
    {
        var count = 0;

        var result = ScannerUtilities.ForEachLeadingCommentRange(Leading, 0, (_, _, _, _) => ++count == 2);

        Assert.True(result);
        Assert.Equal(2, count);
    }

    [Fact]
    public void GetLeadingCommentRanges_SkipsTheShebang()
    {
        var ranges = ScannerUtilities.GetLeadingCommentRanges("#!/bin/sh\n// c\nfoo", 0);

        Assert.NotNull(ranges);
        Assert.Single(ranges);
        // The range starts after the shebang line, not at it.
        Assert.Equal(10, ranges[0].Pos);
    }
}
