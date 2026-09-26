namespace TypeScript.Compiler.Tests;

/// <summary>
/// M5-0 补上的 <c>Debug.formatEnum</c>/<c>formatSyntaxKind</c>/<c>formatFlowFlags</c> 的语义。
/// </summary>
/// <remarks>
/// TS 在运行时遍历枚举对象的成员，所以别名（<c>FirstAssignment = EqualsToken</c>）与「没有 0 成员的标志枚举」
/// 各有确定答案；C# 侧靠反射读字段，声明顺序就是 TS 的遍历顺序。这两点正是会悄悄答错的边界，因此钉在这里。
/// </remarks>
public class DebugFormatTests
{
    [Theory]
    [InlineData((int)SyntaxKind.Identifier, "Identifier")]
    [InlineData((int)SyntaxKind.EndOfFileToken, "EndOfFileToken")]
    // An alias formats as the member declared first (`EqualsToken` precedes `FirstAssignment` in SyntaxKind).
    [InlineData((int)SyntaxKind.FirstAssignment, "EqualsToken")]
    public void FormatSyntaxKind_NamesTheMember(int kind, string expected)
    {
        Assert.Equal(expected, Debug.FormatSyntaxKind((SyntaxKind)kind));
    }

    [Fact]
    public void FormatSyntaxKind_TreatsAnAbsentKindAsZero()
    {
        // TS's `formatEnum(value = 0, …)` default fires for `undefined`, and SyntaxKind's 0 member is `Unknown`
        // (debug.ts:444-446).
        Assert.Equal("Unknown", Debug.FormatSyntaxKind(null));
    }

    [Fact]
    public void FormatSyntaxKind_FallsBackToTheNumberForAValueNoMemberHas()
    {
        Assert.Equal("999999", Debug.FormatSyntaxKind((SyntaxKind)999999));
    }

    [Fact]
    public void FormatFlowFlags_JoinsTheCoveringMembersInValueOrder()
    {
        Assert.Equal("Start", Debug.FormatFlowFlags(FlowFlags.Start));
        Assert.Equal("Start|Shared", Debug.FormatFlowFlags(FlowFlags.Start | FlowFlags.Shared));

        // A composite member participates when it fits the value exactly, exactly as TS's loop does (debug.ts:399-411):
        // `Label` is `BranchLabel | LoopLabel`, and it is reached only after both of its parts were already pushed.
        Assert.Equal("BranchLabel|LoopLabel|Label", Debug.FormatFlowFlags(FlowFlags.Label));
    }

    [Fact]
    public void FormatFlowFlags_AnswersZeroLiterally_ForAnEnumWithNoZeroMember()
    {
        // TS's zero branch prints the 0 member's name only when one exists; FlowFlags' first member is `Unreachable = 1`
        // (debug.ts:389-421).
        Assert.Equal("0", Debug.FormatFlowFlags(null));
        Assert.Equal("0", Debug.FormatFlowFlags((FlowFlags)0));
    }
}
