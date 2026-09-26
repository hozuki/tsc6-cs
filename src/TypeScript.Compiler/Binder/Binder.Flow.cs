namespace TypeScript.Compiler;

// ported from: src/compiler/binder.ts:1356-1431 (createBranchLabel through finishFlowLabel)
// ported from: src/compiler/binder.ts:1477-1513 (doWithConditionalBranches, bindCondition, bindIterativeStatement,
//   setContinueTarget)
/// <summary>
/// The flow graph's node factories and the two helpers the statement binders build their branches with: the labels a
/// container opens, the antecedents that reach them, and the conditional branches of an <c>if</c>/loop condition.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS's <c>createFlowNode</c> is a module-level function of <c>binder.ts</c>; the port keeps it next to the
/// class it builds (Model/FlowNode.cs) and these members call <see cref="FlowNode.CreateFlowNode"/>.
/// <para>
/// PORT-NOTE: TS's <c>flow.flags |= …</c> compound assignments are kept as they stand: <c>FlowNode.Flags</c> is a
/// writable slot and <see cref="FlowFlags"/> is a flags enum, so the bit arithmetic is the reference's.
/// </para>
/// <para>
/// PORT-NOTE: TS's <c>label.antecedent</c> is <c>FlowNode[] | undefined</c> and the members here are the only writers
/// (<c>addAntecedent</c> grows the array in place, <c>bindTryStatement</c> assigns the concatenation); the port's slot
/// is <c>object?</c> (Model/FlowNode.cs) and every read casts it back to <c>List&lt;FlowNode&gt;</c>, which is what it
/// holds. An absent or single-node antecedent answers "no array" here, exactly as TS's <c>contains</c> and
/// <c>finishFlowLabel</c> treat <c>undefined</c>.
/// </para>
/// <para>
/// PORT-NOTE: <c>doWithConditionalBranches</c> is generic over the value it binds (<c>Expression</c> for a condition,
/// <c>LeftHandSideExpression</c> for an optional chain) with the action's parameter invariant in TS. The port keeps the
/// generic parameter and passes a lambda at each call site, because the two actions' parameter types differ and
/// <c>bind</c> itself takes <c>Node?</c>.
/// </para>
/// </remarks>
internal partial class Binder
{
    /// <summary>ported from: src/compiler/binder.ts:1356-1358 — <c>createBranchLabel</c>.</summary>
    /// <remarks>PORT-NOTE: TS's cast to <c>FlowLabel</c> is typespace-only — the one flow-node class implements
    /// <see cref="IFlowLabel"/> — so the factory's own return type stands (§12.9).</remarks>
    internal FlowNode CreateBranchLabel() => FlowNode.CreateFlowNode(FlowFlags.BranchLabel, /*node*/ null, /*antecedent*/ null);

    /// <summary>ported from: src/compiler/binder.ts:1360-1362 — <c>createLoopLabel</c>.</summary>
    private FlowNode CreateLoopLabel() => FlowNode.CreateFlowNode(FlowFlags.LoopLabel, /*node*/ null, /*antecedent*/ null);

    /// <summary>ported from: src/compiler/binder.ts:1364-1366 — <c>createReduceLabel</c>.</summary>
    /// <remarks>PORT-NOTE: TS builds the <c>{ target, antecedents }</c> payload as an object literal; the port's
    /// <see cref="FlowReduceLabelData"/> is that literal with a name (Model/FlowNode.cs).</remarks>
    private FlowNode CreateReduceLabel(FlowNode target, List<FlowNode> antecedents, FlowNode antecedent)
        => FlowNode.CreateFlowNode(
            FlowFlags.ReduceLabel,
            new FlowReduceLabelData { Target = target, Antecedents = antecedents },
            antecedent);

    /// <summary>ported from: src/compiler/binder.ts:1368-1371 — <c>setFlowNodeReferenced</c>.</summary>
    private static void SetFlowNodeReferenced(FlowNode flow)
        // On first reference we set the Referenced flag, thereafter we set the Shared flag
        => flow.Flags |= (flow.Flags & FlowFlags.Referenced) != 0 ? FlowFlags.Shared : FlowFlags.Referenced;

    /// <summary>ported from: src/compiler/binder.ts:1373-1378 — <c>addAntecedent</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>(label.antecedent || (label.antecedent = [])).push(antecedent)</c> is "use the array if one
    /// is there, otherwise install one"; the port states the same two steps, and <c>contains</c> is the reference's
    /// reference-equality search over the array (Core.Contains).
    /// </remarks>
    internal void AddAntecedent(FlowNode label, FlowNode antecedent)
    {
        if ((antecedent.Flags & FlowFlags.Unreachable) == 0 && !Core.Contains(label.Antecedent as List<FlowNode>, antecedent))
        {
            var antecedents = label.Antecedent as List<FlowNode>;
            if (antecedents is null)
            {
                antecedents = [];
                label.Antecedent = antecedents;
            }

            antecedents.Add(antecedent);
            SetFlowNodeReferenced(antecedent);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:1380-1399 — <c>createFlowCondition</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS narrows the parameter to <c>FlowFlags.TrueCondition | FlowFlags.FalseCondition</c>; C# has no
    /// literal-union type, so the slot is the enum and the callers pass one of the two members (the same treatment
    /// <c>createFlowMutation</c> records for its two flags).
    /// <para>
    /// PORT-NOTE: TS's <c>!isNullishCoalesce(expression.parent)</c> is a non-optional read of the parent; the model's
    /// parent is nullable, and a node without one is not a nullish coercion, which is what the guard answers.
    /// </para>
    /// </remarks>
    private FlowNode CreateFlowCondition(FlowFlags flags, FlowNode antecedent, Expression? expression)
    {
        if ((antecedent.Flags & FlowFlags.Unreachable) != 0)
        {
            return antecedent;
        }

        if (expression is null)
        {
            return (flags & FlowFlags.TrueCondition) != 0 ? antecedent : _unreachableFlow;
        }

        if (((expression.Kind == SyntaxKind.TrueKeyword && (flags & FlowFlags.FalseCondition) != 0
                || expression.Kind == SyntaxKind.FalseKeyword && (flags & FlowFlags.TrueCondition) != 0)
            && !Utilities.IsExpressionOfOptionalChainRoot(expression)
            && !(expression.Parent is { } parent && Utilities.IsNullishCoalesce(parent))))
        {
            return _unreachableFlow;
        }

        if (!IsNarrowingExpression(expression))
        {
            return antecedent;
        }

        SetFlowNodeReferenced(antecedent);
        return FlowNode.CreateFlowNode(flags, expression, antecedent);
    }

    /// <summary>ported from: src/compiler/binder.ts:1401-1404 — <c>createFlowSwitchClause</c>.</summary>
    private FlowNode CreateFlowSwitchClause(FlowNode antecedent, SwitchStatement switchStatement, int clauseStart, int clauseEnd)
    {
        SetFlowNodeReferenced(antecedent);
        return FlowNode.CreateFlowNode(
            FlowFlags.SwitchClause,
            new FlowSwitchClauseData
            {
                SwitchStatement = switchStatement,
                ClauseStart = clauseStart,
                ClauseEnd = clauseEnd,
            },
            antecedent);
    }

    /// <summary>ported from: src/compiler/binder.ts:1406-1414 — <c>createFlowMutation</c>.</summary>
    /// <remarks>PORT-NOTE: TS types the payload <c>Expression | VariableDeclaration | ArrayBindingElement</c>; the three
    /// have no C# anchor below <see cref="Node"/>, which is the honest upper bound and also the flow node's slot type.</remarks>
    private FlowNode CreateFlowMutation(FlowFlags flags, FlowNode antecedent, Node node)
    {
        SetFlowNodeReferenced(antecedent);
        _hasFlowEffects = true;
        var result = FlowNode.CreateFlowNode(flags, node, antecedent);
        if (_currentExceptionTarget is not null)
        {
            AddAntecedent(_currentExceptionTarget, result);
        }

        return result;
    }

    /// <summary>ported from: src/compiler/binder.ts:1416-1420 — <c>createFlowCall</c>.</summary>
    private FlowNode CreateFlowCall(FlowNode antecedent, CallExpression node)
    {
        SetFlowNodeReferenced(antecedent);
        _hasFlowEffects = true;
        return FlowNode.CreateFlowNode(FlowFlags.Call, node, antecedent);
    }

    /// <summary>ported from: src/compiler/binder.ts:1422-1431 — <c>finishFlowLabel</c>.</summary>
    internal FlowNode FinishFlowLabel(FlowNode flow)
    {
        var antecedents = flow.Antecedent as List<FlowNode>;
        if (antecedents is null)
        {
            return _unreachableFlow;
        }

        if (antecedents.Count == 1)
        {
            return antecedents[0];
        }

        return flow;
    }

    /// <summary>ported from: src/compiler/binder.ts:1477-1485 — <c>doWithConditionalBranches</c>.</summary>
    private void DoWithConditionalBranches<T>(Action<T> action, T value, FlowNode trueTarget, FlowNode falseTarget)
    {
        var savedTrueTarget = _currentTrueTarget;
        var savedFalseTarget = _currentFalseTarget;
        _currentTrueTarget = trueTarget;
        _currentFalseTarget = falseTarget;
        action(value);
        _currentTrueTarget = savedTrueTarget;
        _currentFalseTarget = savedFalseTarget;
    }

    /// <summary>ported from: src/compiler/binder.ts:1487-1493 — <c>bindCondition</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>!node</c> is the truthiness test for "no condition"; the model's slot is nullable,
    /// so the same test is <c>node is null</c>. The three negations are grouped exactly as the reference groups them.
    /// </remarks>
    private void BindCondition(Expression? node, FlowNode trueTarget, FlowNode falseTarget)
    {
        DoWithConditionalBranches<Expression?>(n => Bind(n), node, trueTarget, falseTarget);
        if (node is null
            || !IsLogicalAssignmentExpression(node) && !IsLogicalExpression(node)
                && !(Utilities.IsOptionalChain(node) && Utilities.IsOutermostOptionalChain(node)))
        {
            AddAntecedent(trueTarget, CreateFlowCondition(FlowFlags.TrueCondition, _currentFlow!, node));
            AddAntecedent(falseTarget, CreateFlowCondition(FlowFlags.FalseCondition, _currentFlow!, node));
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:1495-1503 — <c>bindIterativeStatement</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is the <c>Statement</c> union; the port passes the common face
    /// <see cref="IStatement"/> from <c>bindChildren</c>, and <c>bind</c> takes the node class, hence the cast (§12.9).</remarks>
    private void BindIterativeStatement(IStatement node, FlowNode breakTarget, FlowNode continueTarget)
    {
        var saveBreakTarget = _currentBreakTarget;
        var saveContinueTarget = _currentContinueTarget;
        _currentBreakTarget = breakTarget;
        _currentContinueTarget = continueTarget;
        Bind(node as Node);
        _currentBreakTarget = saveBreakTarget;
        _currentContinueTarget = saveContinueTarget;
    }

    /// <summary>ported from: src/compiler/binder.ts:1505-1513 — <c>setContinueTarget</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>node.parent.kind === SyntaxKind.LabeledStatement</c> is a non-optional read; the
    /// model's parent is nullable, so the loop's condition is the pattern that both proves the parent and names it, and
    /// the assignment inside the body asserts the same parent the condition just read.
    /// </remarks>
    private FlowNode SetContinueTarget(Node node, FlowNode target)
    {
        var label = _activeLabelList;
        while (label is not null && node.Parent is { Kind: SyntaxKind.LabeledStatement })
        {
            label.ContinueTarget = target;
            label = label.Next;
            node = node.Parent!;
        }

        return target;
    }
}
