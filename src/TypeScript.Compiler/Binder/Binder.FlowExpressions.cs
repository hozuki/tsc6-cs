namespace TypeScript.Compiler;

// ported from: src/compiler/binder.ts:1847-2023 (bindLogicalLikeExpression, bindPrefixUnaryExpressionFlow,
//   bindPostfixUnaryExpressionFlow, bindDestructuringAssignmentFlow, createBindBinaryExpressionFlow)
/// <summary>
/// The expression side of the flow graph: the branches a logical or coalescing expression opens, the assignment targets
/// an update or destructuring assignment produces, and the trampoline that walks a binary expression chain on the heap so
/// a deeply nested one cannot exhaust the call stack.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS caches the closure <c>createBindBinaryExpressionFlow()</c> returns in the binder slot
/// <c>bindBinaryExpressionFlow</c> (binder.ts:556) and calls the slot. <c>bindChildren</c>'s dispatch is written by the
/// slot's name (M5-1), so the port keeps <see cref="BindBinaryExpressionFlow"/> as the member the dispatch calls and
/// caches the trampoline it builds in a field instead of a closure slot; the trampoline is built once per binder, exactly
/// as the reference builds it once per binder. Binder.State.cs records the same decision for the slot.
/// <para>
/// PORT-NOTE: TS's inner <c>WorkArea</c> interface is declared inside <c>createBindBinaryExpressionFlow</c>; C# cannot
/// nest a type in a method, so it is the private class below, with the reference's field names. Its two stacks are
/// JavaScript arrays: <c>inStrictModeStack[index] = value</c> past the end *grows* the array, and reading past the end
/// answers <c>undefined</c>. C# lists throw in both cases, so the two accesses go through <see cref="SetStackSlot"/> and
/// <see cref="GetStackSlot"/>, which give the same answers (an entry that was never written reads back as the absent
/// value <c>null</c> — the <c>undefined</c> the reference's <c>!== undefined</c> tests for).
/// </para>
/// <para>
/// PORT-NOTE: the trampoline's result type is <c>object?</c> because the binder's <c>onExit</c> answers nothing (TS's
/// <c>void</c>): the walker requires a <c>TResult</c>, and <c>null</c> is the port's "no result".
/// </para>
/// </remarks>
internal partial class Binder
{
    /// <summary>ported from: src/compiler/binder.ts:556 — <c>bindBinaryExpressionFlow</c> (the cached closure slot).</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>createBinder</c> caches the closure the factory returns in the slot <c>bindBinaryExpressionFlow</c>
    /// and the dispatch calls the slot; the port states that slot as this delegate field, built once per binder like the
    /// reference and invoked by <see cref="BindBinaryExpressionFlow"/> (the member M5-1's dispatch calls). It is built on
    /// first use rather than in the constructor because the trampoline's callbacks are members of this binder.
    /// </remarks>
    private Func<BinaryExpression, object?>? _bindBinaryExpressionFlow;

    /// <summary>ported from: src/compiler/binder.ts:1912-2023 — <c>createBindBinaryExpressionFlow</c>, as called.</summary>
    private void BindBinaryExpressionFlow(BinaryExpression node)
    {
        _bindBinaryExpressionFlow ??= CreateBindBinaryExpressionFlow();
        _bindBinaryExpressionFlow(node);
    }

    /// <summary>ported from: src/compiler/binder.ts:1847-1868 — <c>bindLogicalLikeExpression</c>.</summary>
    /// <remarks>PORT-NOTE: the <c>&amp;&amp;</c> test covers the two kinds the reference names; TS's
    /// <c>operatorToken.kind</c> reads are stated once as a local because the port's token slot is nullable (§12.6).</remarks>
    private void BindLogicalLikeExpression(BinaryExpression node, FlowNode trueTarget, FlowNode falseTarget)
    {
        var preRightLabel = CreateBranchLabel();
        var operatorKind = node.OperatorToken!.Kind;
        if (operatorKind == SyntaxKind.AmpersandAmpersandToken || operatorKind == SyntaxKind.AmpersandAmpersandEqualsToken)
        {
            BindCondition(node.Left, preRightLabel, falseTarget);
        }
        else
        {
            BindCondition(node.Left, trueTarget, preRightLabel);
        }

        _currentFlow = FinishFlowLabel(preRightLabel);
        Bind(node.OperatorToken);

        if (Utilities.IsLogicalOrCoalescingAssignmentOperator(operatorKind))
        {
            DoWithConditionalBranches<Expression?>(n => Bind(n), node.Right, trueTarget, falseTarget);
            BindAssignmentTargetFlow(node.Left!);

            AddAntecedent(trueTarget, CreateFlowCondition(FlowFlags.TrueCondition, _currentFlow!, node));
            AddAntecedent(falseTarget, CreateFlowCondition(FlowFlags.FalseCondition, _currentFlow!, node));
        }
        else
        {
            BindCondition(node.Right, trueTarget, falseTarget);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:1870-1885 — <c>bindPrefixUnaryExpressionFlow</c>.</summary>
    private void BindPrefixUnaryExpressionFlow(PrefixUnaryExpression node)
    {
        if (node.Operator == SyntaxKind.ExclamationToken)
        {
            var saveTrueTarget = _currentTrueTarget;
            _currentTrueTarget = _currentFalseTarget;
            _currentFalseTarget = saveTrueTarget;
            BindEachChild(node);
            _currentFalseTarget = _currentTrueTarget;
            _currentTrueTarget = saveTrueTarget;
        }
        else
        {
            BindEachChild(node);
            if (node.Operator == SyntaxKind.PlusPlusToken || node.Operator == SyntaxKind.MinusMinusToken)
            {
                BindAssignmentTargetFlow(node.Operand!);
            }
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:1887-1892 — <c>bindPostfixUnaryExpressionFlow</c>.</summary>
    private void BindPostfixUnaryExpressionFlow(PostfixUnaryExpression node)
    {
        BindEachChild(node);
        if (node.Operator == SyntaxKind.PlusPlusToken || node.Operator == SyntaxKind.MinusMinusToken)
        {
            BindAssignmentTargetFlow(node.Operand!);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:1894-1910 — <c>bindDestructuringAssignmentFlow</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is the intersection alias <c>DestructuringAssignment = BinaryExpression &amp; {}</c>;
    /// the port states the class the intersection is built on, which is what <c>bindChildren</c> passes.</remarks>
    private void BindDestructuringAssignmentFlow(BinaryExpression node)
    {
        if (_inAssignmentPattern)
        {
            _inAssignmentPattern = false;
            Bind(node.OperatorToken);
            Bind(node.Right);
            _inAssignmentPattern = true;
            Bind(node.Left);
        }
        else
        {
            _inAssignmentPattern = true;
            Bind(node.Left);
            _inAssignmentPattern = false;
            Bind(node.OperatorToken);
            Bind(node.Right);
        }

        BindAssignmentTargetFlow(node.Left!);
    }

    /// <summary>ported from: src/compiler/binder.ts:1912-2023 — <c>createBindBinaryExpressionFlow</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>createBinaryExpressionTrampoline(...)</c> call is kept, with the fold-state parameter
    /// the reference passes as <c>undefined</c>.</remarks>
    private Func<BinaryExpression, object?> CreateBindBinaryExpressionFlow()
        => FactoryUtilities.CreateBinaryExpressionTrampoline<WorkArea, object?>(
            onEnter: OnEnterBinaryExpression,
            onLeft: OnLeftBinaryExpression,
            onOperator: OnOperatorBinaryExpression,
            onRight: OnRightBinaryExpression,
            onExit: OnExitBinaryExpression,
            foldState: null);

    /// <summary>ported from: src/compiler/binder.ts:1922-1964 — <c>onEnter</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>if (state)</c> is the truthiness test for "a state was threaded down"; the port's slot is a
    /// nullable class, so the test is <c>state is not null</c>. TS's <c>hasFlowEffects ||= saveHasFlowEffects</c> has no C#
    /// operator (§10.2), so the same value is stated as the plain or-assignment.
    /// </remarks>
    private WorkArea OnEnterBinaryExpression(BinaryExpression node, WorkArea? state)
    {
        if (state is not null)
        {
            state.StackIndex++;
            // Emulate the work that `bind` does before reaching `bindChildren`. A normal call to
            // `bindBinaryExpressionFlow` will already have done this work.
            Utilities.SetParent(node, _parent);
            var saveInStrictMode = _inStrictMode;
            BindWorker(node);
            var saveParent = _parent;
            _parent = node;
            state.Skip = false;
            SetStackSlot(state.InStrictModeStack, state.StackIndex, saveInStrictMode);
            SetStackSlot(state.ParentStack, state.StackIndex, saveParent);
        }
        else
        {
            state = new WorkArea();
        }

        // PORT-NOTE: the reference's own TODO is kept as a PORT-NOTE rather than a TODO marker:
        // bindLogicalExpression is recursive, so a deeply nested `&&` chain still uses the call stack; for now,
        // since the common cases are chained `+`, leaving it recursive is fine (binder.ts:1961-1963).
        var operatorKind = node.OperatorToken!.Kind;
        if (Utilities.IsLogicalOrCoalescingBinaryOperator(operatorKind)
            || Utilities.IsLogicalOrCoalescingAssignmentOperator(operatorKind))
        {
            if (IsTopLevelLogicalExpression(node))
            {
                var postExpressionLabel = CreateBranchLabel();
                var saveCurrentFlow = _currentFlow;
                var saveHasFlowEffects = _hasFlowEffects;
                _hasFlowEffects = false;
                BindLogicalLikeExpression(node, postExpressionLabel, postExpressionLabel);
                _currentFlow = _hasFlowEffects ? FinishFlowLabel(postExpressionLabel) : saveCurrentFlow;
                _hasFlowEffects = _hasFlowEffects || saveHasFlowEffects;
            }
            else
            {
                BindLogicalLikeExpression(node, _currentTrueTarget!, _currentFalseTarget!);
            }

            state.Skip = true;
        }

        return state;
    }

    /// <summary>ported from: src/compiler/binder.ts:1966-1974 — <c>onLeft</c>.</summary>
    private BinaryExpression? OnLeftBinaryExpression(Expression left, WorkArea state, BinaryExpression node)
    {
        if (!state.Skip)
        {
            var maybeBound = MaybeBindInBinaryExpression(left);
            if (node.OperatorToken!.Kind == SyntaxKind.CommaToken)
            {
                MaybeBindExpressionFlowIfCall(left);
            }

            return maybeBound;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/binder.ts:1976-1980 — <c>onOperator</c>.</summary>
    private void OnOperatorBinaryExpression(Token operatorToken, WorkArea state, BinaryExpression node)
    {
        if (!state.Skip)
        {
            Bind(operatorToken);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:1982-1990 — <c>onRight</c>.</summary>
    private BinaryExpression? OnRightBinaryExpression(Expression right, WorkArea state, BinaryExpression node)
    {
        if (!state.Skip)
        {
            var maybeBound = MaybeBindInBinaryExpression(right);
            if (node.OperatorToken!.Kind == SyntaxKind.CommaToken)
            {
                MaybeBindExpressionFlowIfCall(right);
            }

            return maybeBound;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/binder.ts:1992-2015 — <c>onExit</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>node.left.kind === SyntaxKind.ElementAccessExpression</c> then <c>node.left as
    /// ElementAccessExpression</c> is the class pattern below, and the reference's <c>saved !== undefined</c> tests are the
    /// <c>is not null</c> patterns over the stack slots (a <c>false</c> in the strict-mode stack is restored, as upstream).
    /// </remarks>
    private object? OnExitBinaryExpression(BinaryExpression node, WorkArea state)
    {
        if (!state.Skip)
        {
            var operatorKind = node.OperatorToken!.Kind;
            if (Utilities.IsAssignmentOperator(operatorKind) && !Utilities.IsAssignmentTarget(node))
            {
                BindAssignmentTargetFlow(node.Left!);
                if (operatorKind == SyntaxKind.EqualsToken && node.Left is ElementAccessExpression elementAccessExpression)
                {
                    if (IsNarrowableOperand(elementAccessExpression.Expression!))
                    {
                        _currentFlow = CreateFlowMutation(FlowFlags.ArrayMutation, _currentFlow!, node);
                    }
                }
            }
        }

        var savedInStrictMode = GetStackSlot(state.InStrictModeStack, state.StackIndex);
        var savedParent = GetStackSlot(state.ParentStack, state.StackIndex);
        if (savedInStrictMode is not null)
        {
            _inStrictMode = savedInStrictMode.Value;
        }

        if (savedParent is not null)
        {
            _parent = savedParent;
        }

        state.Skip = false;
        state.StackIndex--;
        return null;
    }

    /// <summary>ported from: src/compiler/binder.ts:2017-2022 — <c>maybeBind</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is non-optional and its <c>node &amp;&amp;</c> test is therefore redundant; the
    /// port keeps the reference's three-part guard with the slot nullable, so the test states "there is a node to test".</remarks>
    private BinaryExpression? MaybeBindInBinaryExpression(Node? node)
    {
        if (node is not null && NodeTests.IsBinaryExpression(node) && !Utilities.IsDestructuringAssignment(node))
        {
            return (BinaryExpression)node;
        }

        Bind(node);
        return null;
    }

    /// <summary>ported from: src/compiler/binder.ts:1914-1919 — the <c>WorkArea</c> state of the walker.</summary>
    /// <remarks>PORT-NOTE: TS declares this interface inside <c>createBindBinaryExpressionFlow</c>; C# has no type
    /// declarations in a method body, so it is this private class. The two stacks hold the reference's initial
    /// <c>[undefined]</c> (one absent entry, so the root frame has nothing to restore).</remarks>
    private sealed class WorkArea
    {
        public int StackIndex;
        public bool Skip;
        public List<bool?> InStrictModeStack = [null];
        public List<Node?> ParentStack = [null];
    }

    /// <summary>The port of the JavaScript array-index assignment in the walker's two stacks.</summary>
    /// <remarks>PORT-NOTE: <c>stack[index] = value</c> past the end grows a JavaScript array; a <c>List&lt;T&gt;</c> throws,
    /// so the missing entries are added first — the reads that would see them (there are none before the matching write)
    /// answer the absent value, which is what <c>undefined</c> reads back as.</remarks>
    private static void SetStackSlot<T>(List<T> stack, int index, T value)
    {
        while (stack.Count <= index)
        {
            stack.Add(default!);
        }

        stack[index] = value;
    }

    /// <summary>The port of reading one of the walker's two stacks.</summary>
    /// <remarks>PORT-NOTE: a JavaScript array read past its end answers <c>undefined</c>; the port answers the absent value
    /// for the same case, which is what the reference's two <c>!== undefined</c> tests distinguish.</remarks>
    private static T? GetStackSlot<T>(List<T?> stack, int index) => index < stack.Count ? stack[index] : default;
}
