namespace TypeScript.Compiler;

// ported from: src/compiler/binder.ts:1242-1354 (isNarrowingExpression through isNarrowableOperand)
// ported from: src/compiler/binder.ts:1433-1475 (isStatementCondition, isLogicalExpression,
//   isLogicalAssignmentExpression, isTopLevelLogicalExpression)
/// <summary>
/// The questions the flow graph asks about an expression before it narrows a reference: whether the expression is a
/// narrowing candidate at all, whether it is a reference the flow graph tracks mutations of, and whether it is the
/// condition of a statement (in which case the logical expression itself does not get its own branch labels).
/// </summary>
/// <remarks>
/// PORT-NOTE: TS declares these as nested functions of <c>createBinder</c>, but none of them reads or writes the
/// binder's state — they are pure queries over the (already parented) tree — so they are <c>static</c> members of the
/// file's class, the same treatment M5-1 gave <c>getContainerFlags</c> and <c>lookupSymbolForName</c>.
/// <para>
/// PORT-NOTE: TS's type guards (<c>expr is PropertyAccessExpression</c>, <c>node.parent.expression === node</c>) both
/// narrow *and* prove a member read; C# narrows through <c>is</c> patterns but a union's <c>expression</c> member has
/// no common type below <see cref="Expression"/>, so every such read goes through <see cref="GetInnerExpression"/> or
/// <see cref="GetChainExpression"/>, which are the port's stand-ins for TS's unchecked union reads. The default arms
/// of those two are programming errors, not answers TS can reach — every caller has established the kind first.
/// </para>
/// <para>
/// PORT-NOTE: TS's parent pointers are non-optional, so a read such as <c>node.parent.kind</c> would throw on a
/// node that was never parented; the model's <c>Parent</c> is nullable (§12.9) and the two members that read it
/// (<see cref="IsStatementCondition"/>, <see cref="IsTopLevelLogicalExpression"/>) answer the same way they would
/// for a kind that has no case.
/// </para>
/// </remarks>
internal partial class Binder
{
    /// <summary>ported from: src/compiler/binder.ts:1242-1267 — <c>isNarrowingExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the <c>ParenthesizedExpression</c> arm falls through to the <c>NonNullExpression</c> arm in TS; C#
    /// has no fall-through, so the same jump is stated with <c>goto case</c> — the two arms read the same
    /// <c>expression</c> member through <see cref="GetInnerExpression"/>.
    /// </remarks>
    private static bool IsNarrowingExpression(Expression expr)
    {
        switch (expr.Kind)
        {
            case SyntaxKind.Identifier:
            case SyntaxKind.ThisKeyword:
                return true;
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ElementAccessExpression:
                return ContainsNarrowableReference(expr);
            case SyntaxKind.CallExpression:
                return HasNarrowableArgument((CallExpression)expr);
            case SyntaxKind.ParenthesizedExpression:
                if (FactoryUtilities.IsJSDocTypeAssertion(expr))
                {
                    return false;
                }

                // falls through
                goto case SyntaxKind.NonNullExpression;
            case SyntaxKind.NonNullExpression:
                return IsNarrowingExpression(GetInnerExpression(expr));
            case SyntaxKind.BinaryExpression:
                return IsNarrowingBinaryExpression((BinaryExpression)expr);
            case SyntaxKind.PrefixUnaryExpression:
                var prefixUnaryExpression = (PrefixUnaryExpression)expr;
                return prefixUnaryExpression.Operator == SyntaxKind.ExclamationToken
                    && IsNarrowingExpression(prefixUnaryExpression.Operand!);
            case SyntaxKind.TypeOfExpression:
                return IsNarrowingExpression(((TypeOfExpression)expr).Expression!);
        }

        return false;
    }

    /// <summary>ported from: src/compiler/binder.ts:1269-1288 — <c>isNarrowableReference</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: this member is <c>internal</c> rather than <c>private</c> because it is the one member of this file
    /// called from another slice: the <c>PropertyAccessExpression</c>/<c>ElementAccessExpression</c> arm of
    /// <c>bindWorker</c> (binder.ts:2886, M5-5) asks it whether the access is a reference the flow graph tracks.
    /// </remarks>
    internal static bool IsNarrowableReference(Expression expr)
    {
        switch (expr.Kind)
        {
            case SyntaxKind.Identifier:
            case SyntaxKind.ThisKeyword:
            case SyntaxKind.SuperKeyword:
            case SyntaxKind.MetaProperty:
                return true;
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ParenthesizedExpression:
            case SyntaxKind.NonNullExpression:
                return IsNarrowableReference(GetInnerExpression(expr));
            case SyntaxKind.ElementAccessExpression:
                var elementAccessExpression = (ElementAccessExpression)expr;
                return (Utilities.IsStringOrNumericLiteralLike(elementAccessExpression.ArgumentExpression!)
                        || Utilities.IsEntityNameExpression(elementAccessExpression.ArgumentExpression!))
                    && IsNarrowableReference(elementAccessExpression.Expression!);
            case SyntaxKind.BinaryExpression:
                var binaryExpression = (BinaryExpression)expr;
                var operatorKind = binaryExpression.OperatorToken!.Kind;
                return operatorKind == SyntaxKind.CommaToken && IsNarrowableReference(binaryExpression.Right!)
                    || Utilities.IsAssignmentOperator(operatorKind) && Utilities.IsLeftHandSideExpression(binaryExpression.Left!);
        }

        return false;
    }

    /// <summary>ported from: src/compiler/binder.ts:1290-1292 — <c>containsNarrowableReference</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>expr.expression</c> is a read off the <c>OptionalChain</c> union; it goes through
    /// <see cref="GetChainExpression"/> here, and the <c>isOptionalChain</c> test is what proves the kind.
    /// </remarks>
    private static bool ContainsNarrowableReference(Expression expr)
        => IsNarrowableReference(expr)
            || Utilities.IsOptionalChain(expr) && ContainsNarrowableReference(GetChainExpression(expr));

    /// <summary>ported from: src/compiler/binder.ts:1294-1309 — <c>hasNarrowableArgument</c>.</summary>
    /// <remarks>PORT-NOTE: TS declares no return type and answers <c>true | undefined</c>; the three <c>return true</c>
    /// arms and the final <c>false</c> are the same answer, and the two callers test it as a boolean.</remarks>
    private static bool HasNarrowableArgument(CallExpression expr)
    {
        if (expr.Arguments is { } arguments)
        {
            foreach (var argument in arguments)
            {
                if (ContainsNarrowableReference(argument))
                {
                    return true;
                }
            }
        }

        if (expr.Expression is PropertyAccessExpression propertyAccessExpression
            && ContainsNarrowableReference(propertyAccessExpression.Expression!))
        {
            return true;
        }

        return false;
    }

    /// <summary>ported from: src/compiler/binder.ts:1311-1313 — <c>isNarrowingTypeofOperands</c>.</summary>
    private static bool IsNarrowingTypeofOperands(Expression expr1, Expression expr2)
        => NodeTests.IsTypeOfExpression(expr1)
            && IsNarrowableOperand(((TypeOfExpression)expr1).Expression!)
            && Utilities.IsStringLiteralLike(expr2);

    /// <summary>ported from: src/compiler/binder.ts:1315-1339 — <c>isNarrowingBinaryExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>skipParentheses</c> calls are non-optional reads of <c>expr.left</c>/<c>expr.right</c>;
    /// the model's slots are nullable, so the reads are asserted — a binary expression the parser produced always has
    /// both sides, exactly as TS's type says.
    /// </remarks>
    private static bool IsNarrowingBinaryExpression(BinaryExpression expr)
    {
        switch (expr.OperatorToken!.Kind)
        {
            case SyntaxKind.EqualsToken:
            case SyntaxKind.BarBarEqualsToken:
            case SyntaxKind.AmpersandAmpersandEqualsToken:
            case SyntaxKind.QuestionQuestionEqualsToken:
                return ContainsNarrowableReference(expr.Left!);
            case SyntaxKind.EqualsEqualsToken:
            case SyntaxKind.ExclamationEqualsToken:
            case SyntaxKind.EqualsEqualsEqualsToken:
            case SyntaxKind.ExclamationEqualsEqualsToken:
                var left = Utilities.SkipParentheses(expr.Left!);
                var right = Utilities.SkipParentheses(expr.Right!);
                return IsNarrowableOperand(left) || IsNarrowableOperand(right)
                    || IsNarrowingTypeofOperands(right, left) || IsNarrowingTypeofOperands(left, right)
                    || (Utilities.IsBooleanLiteral(right) && IsNarrowingExpression(left)
                        || Utilities.IsBooleanLiteral(left) && IsNarrowingExpression(right));
            case SyntaxKind.InstanceOfKeyword:
                return IsNarrowableOperand(expr.Left!);
            case SyntaxKind.InKeyword:
                return IsNarrowingExpression(expr.Right!);
            case SyntaxKind.CommaToken:
                return IsNarrowingExpression(expr.Right!);
        }

        return false;
    }

    /// <summary>ported from: src/compiler/binder.ts:1341-1354 — <c>isNarrowableOperand</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the <c>BinaryExpression</c> arm's inner switch has no <c>default</c> in TS, so any other operator
    /// falls out of the switch to the final <c>containsNarrowableReference</c>; the C# <c>break</c> states that jump.
    /// </remarks>
    private static bool IsNarrowableOperand(Expression expr)
    {
        switch (expr.Kind)
        {
            case SyntaxKind.ParenthesizedExpression:
                return IsNarrowableOperand(((ParenthesizedExpression)expr).Expression!);
            case SyntaxKind.BinaryExpression:
                var binaryExpression = (BinaryExpression)expr;
                switch (binaryExpression.OperatorToken!.Kind)
                {
                    case SyntaxKind.EqualsToken:
                        return IsNarrowableOperand(binaryExpression.Left!);
                    case SyntaxKind.CommaToken:
                        return IsNarrowableOperand(binaryExpression.Right!);
                }

                break;
        }

        return ContainsNarrowableReference(expr);
    }

    /// <summary>ported from: src/compiler/binder.ts:1433-1445 — <c>isStatementCondition</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS switches on <c>node.parent.kind</c> and then reads the statement's <c>expression</c> (or
    /// <c>condition</c>); the port states each arm as the class pattern that both narrows the parent and names the
    /// slot. A node without a parent (which TS's non-optional <c>parent</c> says cannot happen) matches no arm.
    /// </remarks>
    private static bool IsStatementCondition(Node node) => node.Parent switch
    {
        IfStatement parent => ReferenceEquals(parent.Expression, node),
        WhileStatement parent => ReferenceEquals(parent.Expression, node),
        DoStatement parent => ReferenceEquals(parent.Expression, node),
        ForStatement parent => ReferenceEquals(parent.Condition, node),
        ConditionalExpression parent => ReferenceEquals(parent.Condition, node),
        _ => false,
    };

    /// <summary>ported from: src/compiler/binder.ts:1447-1459 — <c>isLogicalExpression</c>.</summary>
    private static bool IsLogicalExpression(Node node)
    {
        while (true)
        {
            if (node is ParenthesizedExpression parenthesizedExpression)
            {
                node = parenthesizedExpression.Expression!;
            }
            else if (node is PrefixUnaryExpression { Operator: SyntaxKind.ExclamationToken } prefixUnaryExpression)
            {
                node = prefixUnaryExpression.Operand!;
            }
            else
            {
                return Utilities.IsLogicalOrCoalescingBinaryExpression(node);
            }
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:1461-1463 — <c>isLogicalAssignmentExpression</c>.</summary>
    private static bool IsLogicalAssignmentExpression(Node node)
        => Utilities.IsLogicalOrCoalescingAssignmentExpression(Utilities.SkipParentheses(node));

    /// <summary>ported from: src/compiler/binder.ts:1465-1475 — <c>isTopLevelLogicalExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>node.parent.expression === node</c> is the optional-chain union read and goes
    /// through <see cref="GetChainExpression"/> here; <c>isOptionalChain</c> is what proves the kind.
    /// </remarks>
    private static bool IsTopLevelLogicalExpression(Node node)
    {
        while (node.Parent is { } parent
            && (NodeTests.IsParenthesizedExpression(parent)
                || parent is PrefixUnaryExpression { Operator: SyntaxKind.ExclamationToken }))
        {
            node = parent;
        }

        var nodeParent = node.Parent;
        return !IsStatementCondition(node)
            && !(nodeParent is not null && IsLogicalExpression(nodeParent))
            && !(nodeParent is not null && Utilities.IsOptionalChain(nodeParent) && ReferenceEquals(GetChainExpression(nodeParent), node));
    }

    /// <summary>The port of TS's <c>(expr as PropertyAccessExpression | ParenthesizedExpression | NonNullExpression).expression</c> read.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's union has one <c>expression</c> member of the same type, so the read type-checks without any
    /// runtime test; the port's three classes share no type below <see cref="Expression"/>, so the read is this kind
    /// switch — the same treatment <c>Utilities.GetAccessExpressionExpression</c> records for the access union. The
    /// default arm is a programming error, not an answer TS can reach: every caller has established the kind first.
    /// </remarks>
    private static Expression GetInnerExpression(Node node) => node switch
    {
        PropertyAccessExpression propertyAccessExpression => propertyAccessExpression.Expression!,
        ParenthesizedExpression parenthesizedExpression => parenthesizedExpression.Expression!,
        NonNullExpression nonNullExpression => nonNullExpression.Expression!,
        _ => throw new InvalidOperationException("unreachable: the node has no expression member"),
    };

    /// <summary>The port of TS's <c>(node as OptionalChain).expression</c> read.</summary>
    /// <remarks>
    /// PORT-NOTE: the <c>OptionalChain</c> union (types.ts) has one <c>expression</c> member; the port's four shapes
    /// share no type below <see cref="Expression"/>, so the read is this kind switch. The two access shapes go through
    /// <see cref="Utilities.GetAccessExpressionExpression"/>, which is the same read for the access union and is
    /// <c>public</c> for exactly this reason; the other two shapes of <c>isOptionalChain</c> are the one-line reads TS
    /// inlines. As above, the default arm is a programming error — every caller has established the kind.
    /// </remarks>
    private static Expression GetChainExpression(Node optionalChain) => optionalChain switch
    {
        PropertyAccessExpression or ElementAccessExpression => Utilities.GetAccessExpressionExpression(optionalChain),
        CallExpression callExpression => callExpression.Expression!,
        NonNullExpression nonNullExpression => nonNullExpression.Expression!,
        _ => throw new InvalidOperationException("unreachable: the node is not an optional chain"),
    };
}
