namespace TypeScript.Compiler;

// ported from: src/compiler/utilities.ts:7420-7422 (isLogicalOrCoalescingAssignmentExpression)
// ported from: src/compiler/utilities.ts:7425-7432 (isLogicalOrCoalescingBinaryOperator, isLogicalOrCoalescingBinaryExpression)
/// <summary>
/// The three logical-or-coalescing predicates the flow graph needs, siblings of the
/// <c>IsLogicalOrCoalescingAssignmentOperator</c> that already lives in Utilities/Utilities.cs.
/// </summary>
/// <remarks>
/// PORT-NOTE: the family's fourth member (<c>isLogicalOrCoalescingAssignmentOperator</c>, utilities.ts:7413-7415) was
/// ported with the factory's operator helpers and sits in Utilities.cs; these three arrived with the binder's flow slice
/// and are stated in this file of the same class rather than beside their sibling, so the slice's members stay in the
/// files it owns. They are the same kind of leaf utility M5-0's <c>Utilities.Binder.*</c> files hold.
/// <para>
/// PORT-NOTE: TS's <c>isLogicalOrCoalescingBinaryOperator</c> composes <c>isBinaryLogicalOperator</c>
/// (utilities.ts:7403-7405) with the coalescing token. That helper is not ported and is exactly the two logical tokens, so
/// the composition is stated as the three-token test below instead of adding a name no slice owns.
/// </para>
/// </remarks>
internal static partial class Utilities
{
    /// <summary>ported from: src/compiler/utilities.ts:7420-7422 — <c>isLogicalOrCoalescingAssignmentExpression</c>.</summary>
    /// <remarks>PORT-NOTE: TS's type guard narrows the result to <c>AssignmentExpression&lt;…&gt;</c>; C# cannot narrow
    /// through a predicate, so the answer is a <c>bool</c> (the shape every port of a TS predicate here uses).</remarks>
    public static bool IsLogicalOrCoalescingAssignmentExpression(Node node)
        => NodeTests.IsBinaryExpression(node)
            && IsLogicalOrCoalescingAssignmentOperator(((BinaryExpression)node).OperatorToken!.Kind);

    /// <summary>ported from: src/compiler/utilities.ts:7425-7427 — <c>isLogicalOrCoalescingBinaryOperator</c>.</summary>
    public static bool IsLogicalOrCoalescingBinaryOperator(SyntaxKind token)
        => token == SyntaxKind.BarBarToken
            || token == SyntaxKind.AmpersandAmpersandToken
            || token == SyntaxKind.QuestionQuestionToken;

    /// <summary>ported from: src/compiler/utilities.ts:7430-7432 — <c>isLogicalOrCoalescingBinaryExpression</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's non-optional <c>expr.operatorToken.kind</c> read is asserted here; a binary
    /// expression the parser produced always has its operator token.</remarks>
    public static bool IsLogicalOrCoalescingBinaryExpression(Node node)
        => NodeTests.IsBinaryExpression(node)
            && IsLogicalOrCoalescingBinaryOperator(((BinaryExpression)node).OperatorToken!.Kind);
}
