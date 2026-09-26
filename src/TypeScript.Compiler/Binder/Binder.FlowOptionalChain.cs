namespace TypeScript.Compiler;

// ported from: src/compiler/binder.ts:2145-2254 (bindOptionalExpression through bindCallExpressionFlow)
/// <summary>
/// The flow binders of the optional chain and the three expression shapes that can open one: an optional chain is bound
/// as if it were a logical expression, and an access or a call that carries the <c>OptionalChain</c> flag routes here
/// instead of the default child walk.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS types <c>OptionalChain</c> in three of these members (<c>bindOptionalChainRest</c>,
/// <c>bindOptionalChain</c>, <c>bindOptionalChainFlow</c>). The union is
/// <c>PropertyAccessChain | ElementAccessChain | CallChain | NonNullChain</c>, which in the model is the four classes
/// plus the <c>OptionalChain</c> flag — the port's <c>IsOptionalChain</c> answers with that flag and those four kinds
/// (Utilities/Utilities.cs) — and their common base is <see cref="LeftHandSideExpression"/>, which is therefore the
/// parameter type for the two members the dispatch calls and the honest upper bound for the rest. The union's own
/// <c>expression</c> member is read through <see cref="GetChainExpression"/>, and <c>bindOptionalChainRest</c>'s switch
/// on the kind is stated as the class patterns below, because <c>questionDotToken</c>, <c>name</c>,
/// <c>argumentExpression</c>, <c>typeArguments</c> and <c>arguments</c> live on different classes.
/// </remarks>
internal partial class Binder
{
    /// <summary>ported from: src/compiler/binder.ts:2145-2151 — <c>bindOptionalExpression</c>.</summary>
    private void BindOptionalExpression(Expression node, FlowNode trueTarget, FlowNode falseTarget)
    {
        DoWithConditionalBranches<Expression>(n => Bind(n), node, trueTarget, falseTarget);
        if (!Utilities.IsOptionalChain(node) || Utilities.IsOutermostOptionalChain(node))
        {
            AddAntecedent(trueTarget, CreateFlowCondition(FlowFlags.TrueCondition, _currentFlow!, node));
            AddAntecedent(falseTarget, CreateFlowCondition(FlowFlags.FalseCondition, _currentFlow!, node));
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:2153-2169 — <c>bindOptionalChainRest</c>.</summary>
    private void BindOptionalChainRest(Node node)
    {
        switch (node)
        {
            case PropertyAccessExpression propertyAccessExpression:
                Bind(propertyAccessExpression.QuestionDotToken);
                Bind(propertyAccessExpression.Name);
                break;
            case ElementAccessExpression elementAccessExpression:
                Bind(elementAccessExpression.QuestionDotToken);
                Bind(elementAccessExpression.ArgumentExpression);
                break;
            case CallExpression callExpression:
                Bind(callExpression.QuestionDotToken);
                BindEach(callExpression.TypeArguments);
                BindEach(callExpression.Arguments);
                break;
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:2171-2193 — <c>bindOptionalChain</c>.</summary>
    /// <remarks>
    /// For an optional chain, we emulate the behavior of a logical expression:
    /// <code>
    /// a?.b         -&gt; a &amp;&amp; a.b
    /// a?.b.c       -&gt; a &amp;&amp; a.b.c
    /// a?.b?.c      -&gt; a &amp;&amp; a.b &amp;&amp; a.b.c
    /// a?.[x = 1]   -&gt; a &amp;&amp; a[x = 1]
    /// </code>
    /// To do this we descend through the chain until we reach the root of a chain (the expression with a <c>?.</c>) and
    /// build its CFA graph as if it were the first condition (<c>a &amp;&amp; ...</c>). Then we bind the rest of the node as
    /// part of the "true" branch, and continue to do so as we ascend back up to the outermost chain node. We then treat the
    /// entire node as the right side of the expression.
    /// <para>
    /// PORT-NOTE: TS's <c>preChainLabel || trueTarget</c> is a truthiness test on a value that is only ever a flow label or
    /// absent, so the port states it as <c>??</c> (§5 row 1).
    /// </para>
    /// </remarks>
    private void BindOptionalChain(LeftHandSideExpression node, FlowNode trueTarget, FlowNode falseTarget)
    {
        var preChainLabel = Utilities.IsOptionalChainRoot(node) ? CreateBranchLabel() : null;
        BindOptionalExpression(GetChainExpression(node), preChainLabel ?? trueTarget, falseTarget);
        if (preChainLabel is not null)
        {
            _currentFlow = FinishFlowLabel(preChainLabel);
        }

        DoWithConditionalBranches<LeftHandSideExpression>(n => BindOptionalChainRest(n), node, trueTarget, falseTarget);
        if (Utilities.IsOutermostOptionalChain(node))
        {
            AddAntecedent(trueTarget, CreateFlowCondition(FlowFlags.TrueCondition, _currentFlow!, node));
            AddAntecedent(falseTarget, CreateFlowCondition(FlowFlags.FalseCondition, _currentFlow!, node));
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:2195-2207 — <c>bindOptionalChainFlow</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>hasFlowEffects ||= saveHasFlowEffects</c> has no C# operator (§10.2); unlike
    /// <c>bindConditionalExpressionFlow</c>, the reference does not clear <c>hasFlowEffects</c> before
    /// <c>bindOptionalChain</c> runs, and the port keeps that.</remarks>
    private void BindOptionalChainFlow(LeftHandSideExpression node)
    {
        if (IsTopLevelLogicalExpression(node))
        {
            var postExpressionLabel = CreateBranchLabel();
            var saveCurrentFlow = _currentFlow;
            var saveHasFlowEffects = _hasFlowEffects;
            BindOptionalChain(node, postExpressionLabel, postExpressionLabel);
            _currentFlow = _hasFlowEffects ? FinishFlowLabel(postExpressionLabel) : saveCurrentFlow;
            _hasFlowEffects = _hasFlowEffects || saveHasFlowEffects;
        }
        else
        {
            BindOptionalChain(node, _currentTrueTarget!, _currentFalseTarget!);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:2209-2216 — <c>bindNonNullExpressionFlow</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is <c>NonNullExpression | NonNullChain</c>; the chain view is not instantiated
    /// (the node is the class plus the <c>OptionalChain</c> flag), so <see cref="NonNullExpression"/> is the union's
    /// honest face — the same decision <c>Utilities.IsOptionalChainRoot</c> records.</remarks>
    private void BindNonNullExpressionFlow(NonNullExpression node)
    {
        if (Utilities.IsOptionalChain(node))
        {
            BindOptionalChainFlow(node);
        }
        else
        {
            BindEachChild(node);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:2218-2225 — <c>bindAccessExpressionFlow</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is <c>AccessExpression | PropertyAccessChain | ElementAccessChain</c>; both chain
    /// views are the access classes plus the <c>OptionalChain</c> flag, so <see cref="MemberExpression"/> — the access
    /// union's common base — is what <c>bindChildren</c> passes and what the member declares.</remarks>
    private void BindAccessExpressionFlow(MemberExpression node)
    {
        if (Utilities.IsOptionalChain(node))
        {
            BindOptionalChainFlow(node);
        }
        else
        {
            BindEachChild(node);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:2227-2254 — <c>bindCallExpressionFlow</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>CallExpression | CallChain</c>; the chain view is the class plus the
    /// <c>OptionalChain</c> flag, so <see cref="CallExpression"/> is the union's honest face.
    /// <para>
    /// PORT-NOTE: the reference's two <c>node.expression</c> reads are non-optional; the model's slot is nullable, so the
    /// <c>super</c> test is a pattern and the trailing property-access read is a class pattern that also proves the kind
    /// — the same two tests the reference writes.
    /// </para>
    /// </remarks>
    private void BindCallExpressionFlow(CallExpression node)
    {
        if (Utilities.IsOptionalChain(node))
        {
            BindOptionalChainFlow(node);
        }
        else
        {
            // If the target of the call expression is a function expression or arrow function we have
            // an immediately invoked function expression (IIFE). Initialize the flowNode property to
            // the current control flow (which includes evaluation of the IIFE arguments).
            var expression = Utilities.SkipParentheses(node.Expression!);
            if (expression.Kind == SyntaxKind.FunctionExpression || expression.Kind == SyntaxKind.ArrowFunction)
            {
                BindEach(node.TypeArguments);
                BindEach(node.Arguments);
                Bind(node.Expression);
            }
            else
            {
                BindEachChild(node);
                if (node.Expression is { Kind: SyntaxKind.SuperKeyword })
                {
                    _currentFlow = CreateFlowCall(_currentFlow!, node);
                }
            }
        }

        if (node.Expression is PropertyAccessExpression propertyAccessExpression
            && propertyAccessExpression.Name is Identifier name
            && IsNarrowableOperand(propertyAccessExpression.Expression!)
            && Utilities.IsPushOrUnshiftIdentifier(name))
        {
            _currentFlow = CreateFlowMutation(FlowFlags.ArrayMutation, _currentFlow!, node);
        }
    }
}
