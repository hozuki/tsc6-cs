namespace TypeScript.Compiler;

// ported from: src/compiler/binder.ts:1515-1845 (bindWhileStatement through bindAssignmentTargetFlow)
/// <summary>
/// The statement side of the flow graph: every statement kind <c>bindChildren</c> dispatches to a flow binder gets its
/// branch labels here, and the assignment targets a statement or expression mentions become the flow mutations the
/// checker's control-flow analysis narrows on.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS types the parameters of four of these members by a union that has no C# anchor —
/// <c>bindForInOrForOfStatement(node: ForInOrOfStatement)</c>, <c>bindReturnOrThrow(node: ReturnStatement |
/// ThrowStatement)</c>, <c>bindBreakOrContinueStatement</c>/<c>bindBreakOrContinueFlow(node: BreakOrContinueStatement)</c>.
/// <c>bindChildren</c> (M5-1) passes the narrowest common face each union shares: <see cref="IterationStatement"/> for the
/// iteration statements and <see cref="IStatement"/> for the other two, and the members read the union's own slots back
/// with the class patterns the reference's type narrowing implies.
/// <para>
/// PORT-NOTE: TS's non-optional reads (<c>node.parent.parent</c> in <c>bindVariableDeclarationFlow</c>,
/// <c>node.parent.expression</c> in <c>bindCaseBlock</c>, <c>node.initializer</c> in <c>bindForInOrForOfStatement</c>,
/// <c>node.caseBlock.clauses</c> in <c>bindSwitchStatement</c>) are type-guaranteed upstream. The port keeps each read
/// as written and guards only where the model's slot is nullable and the guard says the same thing TS's type says —
/// each such guard is called out in its own PORT-NOTE.
/// </para>
/// <para>
/// PORT-NOTE: the flow nodes' <c>Antecedent</c> slot is <c>object?</c> (Model/FlowNode.cs) holding a
/// <c>List&lt;FlowNode&gt;</c> whenever a label has an antecedent array; the concatenation <c>bindTryStatement</c>
/// performs is stated as <see cref="ConcatenateAntecedents"/>, which answers a list of that same shape.
/// </para>
/// </remarks>
internal partial class Binder
{
    /// <summary>ported from: src/compiler/binder.ts:1515-1526 — <c>bindWhileStatement</c>.</summary>
    private void BindWhileStatement(WhileStatement node)
    {
        var preWhileLabel = SetContinueTarget(node, CreateLoopLabel());
        var preBodyLabel = CreateBranchLabel();
        var postWhileLabel = CreateBranchLabel();
        AddAntecedent(preWhileLabel, _currentFlow!);
        _currentFlow = preWhileLabel;
        BindCondition(node.Expression, preBodyLabel, postWhileLabel);
        _currentFlow = FinishFlowLabel(preBodyLabel);
        BindIterativeStatement(node.Statement!, postWhileLabel, preWhileLabel);
        AddAntecedent(preWhileLabel, _currentFlow!);
        _currentFlow = FinishFlowLabel(postWhileLabel);
    }

    /// <summary>ported from: src/compiler/binder.ts:1528-1539 — <c>bindDoStatement</c>.</summary>
    private void BindDoStatement(DoStatement node)
    {
        var preDoLabel = CreateLoopLabel();
        var preConditionLabel = SetContinueTarget(node, CreateBranchLabel());
        var postDoLabel = CreateBranchLabel();
        AddAntecedent(preDoLabel, _currentFlow!);
        _currentFlow = preDoLabel;
        BindIterativeStatement(node.Statement!, postDoLabel, preConditionLabel);
        AddAntecedent(preConditionLabel, _currentFlow!);
        _currentFlow = FinishFlowLabel(preConditionLabel);
        BindCondition(node.Expression, preDoLabel, postDoLabel);
        _currentFlow = FinishFlowLabel(postDoLabel);
    }

    /// <summary>ported from: src/compiler/binder.ts:1541-1557 — <c>bindForStatement</c>.</summary>
    private void BindForStatement(ForStatement node)
    {
        var preLoopLabel = SetContinueTarget(node, CreateLoopLabel());
        var preBodyLabel = CreateBranchLabel();
        var preIncrementorLabel = CreateBranchLabel();
        var postLoopLabel = CreateBranchLabel();
        Bind(node.Initializer);
        AddAntecedent(preLoopLabel, _currentFlow!);
        _currentFlow = preLoopLabel;
        BindCondition(node.Condition, preBodyLabel, postLoopLabel);
        _currentFlow = FinishFlowLabel(preBodyLabel);
        BindIterativeStatement(node.Statement!, postLoopLabel, preIncrementorLabel);
        AddAntecedent(preIncrementorLabel, _currentFlow!);
        _currentFlow = FinishFlowLabel(preIncrementorLabel);
        Bind(node.Incrementor);
        AddAntecedent(preLoopLabel, _currentFlow!);
        _currentFlow = FinishFlowLabel(postLoopLabel);
    }

    /// <summary>ported from: src/compiler/binder.ts:1559-1576 — <c>bindForInOrForOfStatement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>bindChildren</c> (M5-1) passes the two kinds' common base <see cref="IterationStatement"/>, so the
    /// union's own slots (<c>initializer</c>, <c>expression</c>, <c>awaitModifier</c>) are read back through the two
    /// class patterns; TS reaches them off the union directly.
    /// <para>
    /// PORT-NOTE: TS's <c>node.initializer.kind !== SyntaxKind.VariableDeclarationList</c> leaves the union's
    /// <c>Expression</c> member; a <c>VariableDeclarationList</c> is not an expression class, so the port states the same
    /// test as the class pattern that also gives <c>bindAssignmentTargetFlow</c> its parameter type.
    /// </para>
    /// </remarks>
    private void BindForInOrForOfStatement(IterationStatement node)
    {
        var forOfStatement = node as ForOfStatement;
        var forInStatement = node as ForInStatement;
        var preLoopLabel = SetContinueTarget(node, CreateLoopLabel());
        var postLoopLabel = CreateBranchLabel();
        Bind(forOfStatement is not null ? forOfStatement.Expression : forInStatement?.Expression);
        AddAntecedent(preLoopLabel, _currentFlow!);
        _currentFlow = preLoopLabel;
        if (forOfStatement is not null)
        {
            Bind(forOfStatement.AwaitModifier);
        }

        AddAntecedent(postLoopLabel, _currentFlow!);
        var initializer = forOfStatement is not null ? forOfStatement.Initializer : forInStatement?.Initializer;
        Bind(initializer);
        if (initializer is Expression assignmentTarget)
        {
            BindAssignmentTargetFlow(assignmentTarget);
        }

        BindIterativeStatement(node.Statement!, postLoopLabel, preLoopLabel);
        AddAntecedent(preLoopLabel, _currentFlow!);
        _currentFlow = FinishFlowLabel(postLoopLabel);
    }

    /// <summary>ported from: src/compiler/binder.ts:1578-1590 — <c>bindIfStatement</c>.</summary>
    private void BindIfStatement(IfStatement node)
    {
        var thenLabel = CreateBranchLabel();
        var elseLabel = CreateBranchLabel();
        var postIfLabel = CreateBranchLabel();
        BindCondition(node.Expression, thenLabel, elseLabel);
        _currentFlow = FinishFlowLabel(thenLabel);
        Bind(node.ThenStatement as Node);
        AddAntecedent(postIfLabel, _currentFlow!);
        _currentFlow = FinishFlowLabel(elseLabel);
        Bind(node.ElseStatement as Node);
        AddAntecedent(postIfLabel, _currentFlow!);
        _currentFlow = FinishFlowLabel(postIfLabel);
    }

    /// <summary>ported from: src/compiler/binder.ts:1592-1605 — <c>bindReturnOrThrow</c>.</summary>
    /// <remarks>PORT-NOTE: <c>bindChildren</c> passes the union's common face <see cref="IStatement"/>, so the two
    /// classes' <c>expression</c> slots are read back through a class pattern; the <c>ReturnStatement</c> test below is
    /// the reference's own <c>node.kind</c> test.</remarks>
    private void BindReturnOrThrow(IStatement node)
    {
        var savedInReturnPosition = _inReturnPosition;
        _inReturnPosition = true;
        Bind(node is ReturnStatement returnStatement ? returnStatement.Expression
            : node is ThrowStatement throwStatement ? throwStatement.Expression
            : null);
        _inReturnPosition = savedInReturnPosition;
        if (node.Kind == SyntaxKind.ReturnStatement)
        {
            _hasExplicitReturn = true;
            if (_currentReturnTarget is not null)
            {
                AddAntecedent(_currentReturnTarget, _currentFlow!);
            }
        }

        _currentFlow = _unreachableFlow;
        _hasFlowEffects = true;
    }

    /// <summary>ported from: src/compiler/binder.ts:1607-1614 — <c>findActiveLabel</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>name</c> is a <c>__String</c> and can never be absent; the model's
    /// <c>Identifier.EscapedText</c> is <c>string?</c>, so an absent name matches no label rather than comparing equal
    /// to another absent one (dictionary-style keys are never null).</remarks>
    private ActiveLabel? FindActiveLabel(string? name)
    {
        if (name is null)
        {
            return null;
        }

        for (var label = _activeLabelList; label is not null; label = label.Next)
        {
            if (label.Name == name)
            {
                return label;
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/binder.ts:1616-1623 — <c>bindBreakOrContinueFlow</c>.</summary>
    /// <remarks>PORT-NOTE: TS narrows the parameter to <c>BreakOrContinueStatement</c>; <c>bindChildren</c> passes
    /// <see cref="IStatement"/>, and the reference's own <c>node.kind</c> test decides which target applies.</remarks>
    private void BindBreakOrContinueFlow(IStatement node, FlowNode? breakTarget, FlowNode? continueTarget)
    {
        var flowLabel = node.Kind == SyntaxKind.BreakStatement ? breakTarget : continueTarget;
        if (flowLabel is not null)
        {
            AddAntecedent(flowLabel, _currentFlow!);
            _currentFlow = _unreachableFlow;
            _hasFlowEffects = true;
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:1625-1637 — <c>bindBreakOrContinueStatement</c>.</summary>
    /// <remarks>PORT-NOTE: see <see cref="BindBreakOrContinueFlow"/> — the union's <c>label</c> slot is read back through
    /// a class pattern.</remarks>
    private void BindBreakOrContinueStatement(IStatement node)
    {
        var label = node is BreakStatement breakStatement ? breakStatement.Label
            : node is ContinueStatement continueStatement ? continueStatement.Label
            : null;
        Bind(label);
        if (label is not null)
        {
            var activeLabel = FindActiveLabel(label.EscapedText);
            if (activeLabel is not null)
            {
                activeLabel.Referenced = true;
                BindBreakOrContinueFlow(node, activeLabel.BreakTarget, activeLabel.ContinueTarget);
            }
        }
        else
        {
            BindBreakOrContinueFlow(node, _currentBreakTarget, _currentContinueTarget);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:1639-1715 — <c>bindTryStatement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference installs the concatenation of the three labels' antecedents on the pre-finally label with
    /// one nested <c>concatenate</c> call and then reads it back for the two <c>ReduceLabel</c> nodes; the port states the
    /// concatenation as <see cref="ConcatenateAntecedents"/> and the two reads as the class patterns that recover the
    /// list (the reference's own truthiness tests — an absent array is the only falsy one).
    /// </remarks>
    private void BindTryStatement(TryStatement node)
    {
        // We conservatively assume that *any* code in the try block can cause an exception, but we only need
        // to track code that causes mutations (because only mutations widen the possible control flow type of
        // a variable). The exceptionLabel is the target label for control flows that result from exceptions.
        // We add all mutation flow nodes as antecedents of this label such that we can analyze them as possible
        // antecedents of the start of catch or finally blocks. Furthermore, we add the current control flow to
        // represent exceptions that occur before any mutations.
        var saveReturnTarget = _currentReturnTarget;
        var saveExceptionTarget = _currentExceptionTarget;
        var normalExitLabel = CreateBranchLabel();
        var returnLabel = CreateBranchLabel();
        var exceptionLabel = CreateBranchLabel();
        if (node.FinallyBlock is not null)
        {
            _currentReturnTarget = returnLabel;
        }

        AddAntecedent(exceptionLabel, _currentFlow!);
        _currentExceptionTarget = exceptionLabel;
        Bind(node.TryBlock);
        AddAntecedent(normalExitLabel, _currentFlow!);
        if (node.CatchClause is not null)
        {
            // Start of catch clause is the target of exceptions from try block.
            _currentFlow = FinishFlowLabel(exceptionLabel);
            // The currentExceptionTarget now represents control flows from exceptions in the catch clause.
            // Effectively, in a try-catch-finally, if an exception occurs in the try block, the catch block
            // acts like a second try block.
            exceptionLabel = CreateBranchLabel();
            AddAntecedent(exceptionLabel, _currentFlow!);
            _currentExceptionTarget = exceptionLabel;
            Bind(node.CatchClause);
            AddAntecedent(normalExitLabel, _currentFlow!);
        }

        _currentReturnTarget = saveReturnTarget;
        _currentExceptionTarget = saveExceptionTarget;
        if (node.FinallyBlock is not null)
        {
            // Possible ways control can reach the finally block:
            // 1) Normal completion of try block of a try-finally or try-catch-finally
            // 2) Normal completion of catch block (following exception in try block) of a try-catch-finally
            // 3) Return in try or catch block of a try-finally or try-catch-finally
            // 4) Exception in try block of a try-finally
            // 5) Exception in catch block of a try-catch-finally
            // When analyzing a control flow graph that starts inside a finally block we want to consider all
            // five possibilities above. However, when analyzing a control flow graph that starts outside (past)
            // the finally block, we only want to consider the first two (if we're past a finally block then it
            // must have completed normally). Likewise, when analyzing a control flow graph from return statements
            // in try or catch blocks in an IIFE, we only want to consider the third. To make this possible, we
            // inject a ReduceLabel node into the control flow graph. This node contains an alternate reduced
            // set of antecedents for the pre-finally label. As control flow analysis passes by a ReduceLabel
            // node, the pre-finally label is temporarily switched to the reduced antecedent set.
            var finallyLabel = CreateBranchLabel();
            finallyLabel.Antecedent = ConcatenateAntecedents(
                ConcatenateAntecedents(normalExitLabel.Antecedent as List<FlowNode>, exceptionLabel.Antecedent as List<FlowNode>),
                returnLabel.Antecedent as List<FlowNode>);
            _currentFlow = finallyLabel;
            Bind(node.FinallyBlock);
            if ((_currentFlow!.Flags & FlowFlags.Unreachable) != 0)
            {
                // If the end of the finally block is unreachable, the end of the entire try statement is unreachable.
                _currentFlow = _unreachableFlow;
            }
            else
            {
                // If we have an IIFE return target and return statements in the try or catch blocks, add a control
                // flow that goes back through the finally block and back through only the return statements.
                if (_currentReturnTarget is not null && returnLabel.Antecedent is List<FlowNode> returnAntecedents)
                {
                    AddAntecedent(_currentReturnTarget, CreateReduceLabel(finallyLabel, returnAntecedents, _currentFlow));
                }

                // If we have an outer exception target (i.e. a containing try-finally or try-catch-finally), add a
                // control flow that goes back through the finally blok and back through each possible exception source.
                if (_currentExceptionTarget is not null && exceptionLabel.Antecedent is List<FlowNode> exceptionAntecedents)
                {
                    AddAntecedent(_currentExceptionTarget, CreateReduceLabel(finallyLabel, exceptionAntecedents, _currentFlow));
                }

                // If the end of the finally block is reachable, but the end of the try and catch blocks are not,
                // convert the current flow to unreachable. For example, 'try { return 1; } finally { ... }' should
                // result in an unreachable current control flow.
                _currentFlow = normalExitLabel.Antecedent is List<FlowNode> normalAntecedents
                    ? CreateReduceLabel(finallyLabel, normalAntecedents, _currentFlow)
                    : _unreachableFlow;
            }
        }
        else
        {
            _currentFlow = FinishFlowLabel(normalExitLabel);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:1717-1737 — <c>bindSwitchStatement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>forEach(node.caseBlock.clauses, c => c.kind === SyntaxKind.DefaultClause)</c> answers the truthy
    /// result or undefined; the port's <c>Core.Some</c> (Core/Core.Arrays.cs) is the same walk with the boolean answer the
    /// reference's <c>!hasDefault</c> tests.
    /// <para>
    /// PORT-NOTE: <c>!postSwitchLabel.antecedent</c> is the reference's truthiness test on the antecedent array; a label's
    /// array is created only by <c>addAntecedent</c>, which never leaves it empty, so the test is exactly "has an array".
    /// </para>
    /// </remarks>
    private void BindSwitchStatement(SwitchStatement node)
    {
        var postSwitchLabel = CreateBranchLabel();
        Bind(node.Expression);
        var saveBreakTarget = _currentBreakTarget;
        var savePreSwitchCaseFlow = _preSwitchCaseFlow;
        _currentBreakTarget = postSwitchLabel;
        _preSwitchCaseFlow = _currentFlow;
        Bind(node.CaseBlock);
        AddAntecedent(postSwitchLabel, _currentFlow!);
        var hasDefault = node.CaseBlock is { } caseBlock
            && Core.Some(caseBlock.Clauses, c => c.Kind == SyntaxKind.DefaultClause);
        // We mark a switch statement as possibly exhaustive if it has no default clause and if all
        // case clauses have unreachable end points (e.g. they all return). Note, we no longer need
        // this property in control flow analysis, it's there only for backwards compatibility.
        node.PossiblyExhaustive = !hasDefault && postSwitchLabel.Antecedent is null;
        if (!hasDefault)
        {
            AddAntecedent(postSwitchLabel, CreateFlowSwitchClause(_preSwitchCaseFlow!, node, /*clauseStart*/ 0, /*clauseEnd*/ 0));
        }

        _currentBreakTarget = saveBreakTarget;
        _preSwitchCaseFlow = savePreSwitchCaseFlow;
        _currentFlow = FinishFlowLabel(postSwitchLabel);
    }

    /// <summary>ported from: src/compiler/binder.ts:1739-1764 — <c>bindCaseBlock</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>node.parent</c> is declared as a <c>SwitchStatement</c> on <c>CaseBlock</c> itself, so
    /// <c>node.parent.expression</c> needs no test; the model's parent is <see cref="Node"/>, so the port states the cast
    /// the reference's type already carries. The clause list is a nullable slot here (§12.9) while TS's is non-optional:
    /// a case block without clauses matches the loop that never runs, which is what the reference's own
    /// <c>clauses.length</c> would do.
    /// <para>
    /// PORT-NOTE: <c>options.noFallthroughCasesInSwitch</c> is a truthiness test upstream; the port's option slot is
    /// <c>bool?</c>, so the test is <c>== true</c> and the whole condition keeps the reference's order.
    /// </para>
    /// </remarks>
    private void BindCaseBlock(CaseBlock node)
    {
        var clauses = node.Clauses;
        if (clauses is null)
        {
            return;
        }

        var switchStatement = (SwitchStatement)node.Parent!;
        var switchExpression = switchStatement.Expression!;
        var isNarrowingSwitch = switchExpression.Kind == SyntaxKind.TrueKeyword || IsNarrowingExpression(switchExpression);
        var fallthroughFlow = _unreachableFlow;

        for (var i = 0; i < clauses.Count; i++)
        {
            var clauseStart = i;
            while (ClauseStatementCount(clauses[i]) == 0 && i + 1 < clauses.Count)
            {
                if (ReferenceEquals(fallthroughFlow, _unreachableFlow))
                {
                    _currentFlow = _preSwitchCaseFlow!;
                }

                Bind(clauses[i]);
                i++;
            }

            var preCaseLabel = CreateBranchLabel();
            AddAntecedent(preCaseLabel, isNarrowingSwitch
                ? CreateFlowSwitchClause(_preSwitchCaseFlow!, switchStatement, clauseStart, i + 1)
                : _preSwitchCaseFlow!);
            AddAntecedent(preCaseLabel, fallthroughFlow);
            _currentFlow = FinishFlowLabel(preCaseLabel);
            var clause = clauses[i];
            Bind(clause);
            fallthroughFlow = _currentFlow!;
            if ((_currentFlow!.Flags & FlowFlags.Unreachable) == 0 && i != clauses.Count - 1 && _options?.NoFallthroughCasesInSwitch == true)
            {
                switch (clause)
                {
                    case CaseClause caseClause:
                        caseClause.FallthroughFlowNode = _currentFlow;
                        break;
                    case DefaultClause defaultClause:
                        defaultClause.FallthroughFlowNode = _currentFlow;
                        break;
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:1766-1772 — <c>bindCaseClause</c>.</summary>
    private void BindCaseClause(CaseClause node)
    {
        var saveCurrentFlow = _currentFlow;
        _currentFlow = _preSwitchCaseFlow!;
        Bind(node.Expression);
        _currentFlow = saveCurrentFlow;
        BindEach(node.Statements);
    }

    /// <summary>ported from: src/compiler/binder.ts:1774-1777 — <c>bindExpressionStatement</c>.</summary>
    private void BindExpressionStatement(ExpressionStatement node)
    {
        Bind(node.Expression);
        MaybeBindExpressionFlowIfCall(node.Expression!);
    }

    /// <summary>ported from: src/compiler/binder.ts:1779-1788 — <c>maybeBindExpressionFlowIfCall</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>node.kind === SyntaxKind.CallExpression</c> then <c>node as CallExpression</c> is the class
    /// pattern below; the <c>super</c> test is the reference's own kind test.
    /// </remarks>
    private void MaybeBindExpressionFlowIfCall(Expression node)
    {
        // A top level or comma expression call expression with a dotted function name and at least one argument
        // is potentially an assertion and is therefore included in the control flow.
        if (node is CallExpression callExpression
            && callExpression.Expression is { } callTarget
            && callTarget.Kind != SyntaxKind.SuperKeyword
            && Utilities.IsDottedName(callTarget))
        {
            _currentFlow = CreateFlowCall(_currentFlow!, callExpression);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:1790-1807 — <c>bindLabeledStatement</c>.</summary>
    private void BindLabeledStatement(LabeledStatement node)
    {
        var postStatementLabel = CreateBranchLabel();
        _activeLabelList = new ActiveLabel
        {
            Next = _activeLabelList,
            Name = node.Label!.EscapedText,
            BreakTarget = postStatementLabel,
            ContinueTarget = null,
            Referenced = false,
        };
        Bind(node.Label);
        Bind(node.Statement as Node);
        if (!_activeLabelList!.Referenced)
        {
            node.Label!.Flags |= NodeFlags.Unreachable;
        }

        _activeLabelList = _activeLabelList.Next;
        AddAntecedent(postStatementLabel, _currentFlow!);
        _currentFlow = FinishFlowLabel(postStatementLabel);
    }

    /// <summary>ported from: src/compiler/binder.ts:1809-1816 — <c>bindDestructuringTargetFlow</c>.</summary>
    private void BindDestructuringTargetFlow(Expression node)
    {
        if (node is BinaryExpression { OperatorToken.Kind: SyntaxKind.EqualsToken } binaryExpression)
        {
            BindAssignmentTargetFlow(binaryExpression.Left!);
        }
        else
        {
            BindAssignmentTargetFlow(node);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:1818-1845 — <c>bindAssignmentTargetFlow</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the three element/property walks are non-optional reads upstream (<c>node.elements</c>,
    /// <c>node.properties</c>); the model's slots are nullable, so each walk is guarded by the pattern that names the list —
    /// an absent list is the empty walk. The three inner kind tests are the reference's, as class patterns.
    /// </remarks>
    private void BindAssignmentTargetFlow(Expression node)
    {
        if (IsNarrowableReference(node))
        {
            _currentFlow = CreateFlowMutation(FlowFlags.Assignment, _currentFlow!, node);
        }
        else if (node is ArrayLiteralExpression { Elements: { } elements })
        {
            foreach (var element in elements)
            {
                if (element is SpreadElement spreadElement)
                {
                    BindAssignmentTargetFlow(spreadElement.Expression!);
                }
                else
                {
                    BindDestructuringTargetFlow(element);
                }
            }
        }
        else if (node is ObjectLiteralExpression { Properties: { } properties })
        {
            foreach (var property in properties)
            {
                if (property is PropertyAssignment propertyAssignment)
                {
                    BindDestructuringTargetFlow(propertyAssignment.Initializer!);
                }
                else if (property is ShorthandPropertyAssignment shorthandPropertyAssignment)
                {
                    BindAssignmentTargetFlow(shorthandPropertyAssignment.Name!);
                }
                else if (property is SpreadAssignment spreadAssignment)
                {
                    BindAssignmentTargetFlow(spreadAssignment.Expression!);
                }
            }
        }
    }

    /// <summary>
    /// The port of TS's <c>concatenate</c> over the antecedent arrays of <c>bindTryStatement</c> (binder.ts:1685).
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>concatenate</c> answers the other array when one side is absent or empty, and a fresh array
    /// otherwise (core.ts:672-676); <c>Core.Concatenate</c> is that function, and it answers one of the two
    /// <c>List&lt;FlowNode&gt;</c> arguments or a new one, which is exactly the shape the antecedent slot holds.
    /// </remarks>
    private static List<FlowNode>? ConcatenateAntecedents(List<FlowNode>? first, List<FlowNode>? second)
        => Core.Concatenate<FlowNode>(first, second) as List<FlowNode>;

    /// <summary>The port of TS's <c>clauses[i].statements.length</c> read in <c>bindCaseBlock</c> (binder.ts:1743).</summary>
    /// <remarks>PORT-NOTE: both clause classes declare <c>statements</c>, so the read is one arm per class — TS reaches it
    /// off the union directly. A node that is neither class (which a case block's clause list cannot contain) has no
    /// statements, which is the empty length the reference's own read answers for an absent list.</remarks>
    private static int ClauseStatementCount(Node clause) => clause switch
    {
        CaseClause caseClause => caseClause.Statements?.Count ?? 0,
        DefaultClause defaultClause => defaultClause.Statements?.Count ?? 0,
        _ => 0,
    };
}
