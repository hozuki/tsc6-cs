#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/generators.ts:1280-2107 — the transformation into the intermediate
// representation, the substitution hook, and the two local/temporary helpers.
/// <summary>ported from: src/compiler/transformers/generators.ts:1280-2107 — the members of <c>transformGenerators</c>
/// that turn statements into operations (<c>transformAndEmit*</c>), the identifier-substitution hook the renamed catch
/// variables need, and <c>cacheExpression</c>/<c>declareLocal</c>.</summary>
internal sealed partial class GeneratorsTransformer
{
    // ---------------------------------------------------------------------------------------------
    // generators.ts:1280-1354 — statement dispatch
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/generators.ts:1280-1285 — <c>transformAndEmitStatements</c>.</summary>
    private void TransformAndEmitStatements(IReadOnlyList<IStatement> statements, int start = 0)
    {
        var numStatements = statements.Count;
        for (var i = start; i < numStatements; i++)
        {
            TransformAndEmitStatement(statements[i]);
        }
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:1287-1294 — <c>transformAndEmitEmbeddedStatement</c>.</summary>
    private void TransformAndEmitEmbeddedStatement(IStatement node)
    {
        if (NodeTests.IsBlock((Node)node))
        {
            TransformAndEmitStatements(((Block)node).Statements!);
        }
        else
        {
            TransformAndEmitStatement(node);
        }
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:1296-1304 — <c>transformAndEmitStatement</c>.</summary>
    private void TransformAndEmitStatement(IStatement node)
    {
        var savedInStatementContainingYield = _inStatementContainingYield;
        if (!_inStatementContainingYield)
        {
            _inStatementContainingYield = ContainsYield(node);
        }

        TransformAndEmitStatementWorker(node);
        _inStatementContainingYield = savedInStatementContainingYield;
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:1306-1341 — <c>transformAndEmitStatementWorker</c>.</summary>
    private void TransformAndEmitStatementWorker(IStatement node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.Block:
                TransformAndEmitBlock((Block)node);
                return;
            case SyntaxKind.ExpressionStatement:
                TransformAndEmitExpressionStatement((ExpressionStatement)node);
                return;
            case SyntaxKind.IfStatement:
                TransformAndEmitIfStatement((IfStatement)node);
                return;
            case SyntaxKind.DoStatement:
                TransformAndEmitDoStatement((DoStatement)node);
                return;
            case SyntaxKind.WhileStatement:
                TransformAndEmitWhileStatement((WhileStatement)node);
                return;
            case SyntaxKind.ForStatement:
                TransformAndEmitForStatement((ForStatement)node);
                return;
            case SyntaxKind.ForInStatement:
                TransformAndEmitForInStatement((ForInStatement)node);
                return;
            case SyntaxKind.ContinueStatement:
                TransformAndEmitContinueStatement((ContinueStatement)node);
                return;
            case SyntaxKind.BreakStatement:
                TransformAndEmitBreakStatement((BreakStatement)node);
                return;
            case SyntaxKind.ReturnStatement:
                TransformAndEmitReturnStatement((ReturnStatement)node);
                return;
            case SyntaxKind.WithStatement:
                TransformAndEmitWithStatement((WithStatement)node);
                return;
            case SyntaxKind.SwitchStatement:
                TransformAndEmitSwitchStatement((SwitchStatement)node);
                return;
            case SyntaxKind.LabeledStatement:
                TransformAndEmitLabeledStatement((LabeledStatement)node);
                return;
            case SyntaxKind.ThrowStatement:
                TransformAndEmitThrowStatement((ThrowStatement)node);
                return;
            case SyntaxKind.TryStatement:
                TransformAndEmitTryStatement((TryStatement)node);
                return;
            default:
                EmitStatement(Utilities.VisitNode(node, _visitor, Utilities.IsStatement));
                return;
        }
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:1343-1350 — <c>transformAndEmitBlock</c>.</summary>
    private void TransformAndEmitBlock(Block node)
    {
        if (ContainsYield(node))
        {
            TransformAndEmitStatements(node.Statements!);
        }
        else
        {
            EmitStatement(Utilities.VisitNode(node, _visitor, Utilities.IsStatement));
        }
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:1352-1354 — <c>transformAndEmitExpressionStatement</c>.</summary>
    private void TransformAndEmitExpressionStatement(ExpressionStatement node)
        => EmitStatement(Utilities.VisitNode(node, _visitor, Utilities.IsStatement));

    /// <summary>ported from: src/compiler/transformers/generators.ts:1356-1385 — <c>transformAndEmitVariableDeclarationList</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's result is always <c>undefined</c> and neither caller reads it; the port keeps
    /// its declared shape so the two call sites stay as the reference wrote them.</remarks>
    private VariableDeclarationList? TransformAndEmitVariableDeclarationList(VariableDeclarationList node)
    {
        foreach (var variable in node.Declarations!)
        {
            var name = _factory.CloneNode((Identifier)variable.Name!);
            EmitNodes.SetCommentRange(name, variable.Name!);
            _hoistVariableDeclaration(name);
        }

        var variables = TypeScriptTransformSupport.GetInitializedVariables(node);
        var numVariables = variables.Count;
        var variablesWritten = 0;
        var pendingExpressions = new List<Expression>();
        while (variablesWritten < numVariables)
        {
            for (var i = variablesWritten; i < numVariables; i++)
            {
                var variable = variables[i];
                if (ContainsYield(variable.Initializer) && pendingExpressions.Count > 0)
                {
                    break;
                }

                pendingExpressions.Add(TransformInitializedVariable(variable));
            }

            if (pendingExpressions.Count > 0)
            {
                EmitStatement(_factory.CreateExpressionStatement(_factory.InlineExpressions(pendingExpressions)));
                variablesWritten += pendingExpressions.Count;
                pendingExpressions = [];
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:1387-1395 — <c>transformInitializedVariable</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>InitializedVariableDeclaration</c> is a <see cref="VariableDeclaration"/> whose
    /// <c>initializer</c> is required, which is what
    /// <see cref="TypeScriptTransformSupport.GetInitializedVariables"/> answers (that member records the same
    /// narrowing).</remarks>
    private Expression TransformInitializedVariable(VariableDeclaration node)
        => EmitNodes.SetSourceMapRange(
            _factory.CreateAssignment(
                EmitNodes.SetSourceMapRange((Identifier)_factory.CloneNode(node.Name!), TypeScriptTransformSupport.SourceMapRangeOf(node.Name!)),
                Debug.CheckDefined(Utilities.VisitNode(node.Initializer, _visitor, Utilities.IsExpression))),
            TypeScriptTransformSupport.SourceMapRangeOf(node));

    // ---------------------------------------------------------------------------------------------
    // generators.ts:1397-1516 — if/do/while
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/generators.ts:1397-1432 — <c>transformAndEmitIfStatement</c>.</summary>
    private void TransformAndEmitIfStatement(IfStatement node)
    {
        if (ContainsYield(node))
        {
            // [source]
            //      if (x)
            //          /*thenStatement*/
            //      else
            //          /*elseStatement*/
            //
            // [intermediate]
            //  .brfalse elseLabel, (x)
            //      /*thenStatement*/
            //  .br endLabel
            //  .mark elseLabel
            //      /*elseStatement*/
            //  .mark endLabel

            if (ContainsYield(node.ThenStatement) || ContainsYield(node.ElseStatement))
            {
                var endLabel = DefineLabel();
                var elseLabel = node.ElseStatement is not null ? DefineLabel() : (int?)null;
                EmitBreakWhenFalse(node.ElseStatement is not null ? elseLabel!.Value : endLabel, Debug.CheckDefined(Utilities.VisitNode(node.Expression, _visitor, Utilities.IsExpression)), /*location*/ node.Expression);
                TransformAndEmitEmbeddedStatement(node.ThenStatement!);
                if (node.ElseStatement is not null)
                {
                    EmitBreak(endLabel);
                    MarkLabel(elseLabel!.Value);
                    TransformAndEmitEmbeddedStatement(node.ElseStatement);
                }
                MarkLabel(endLabel);
            }
            else
            {
                EmitStatement(Utilities.VisitNode(node, _visitor, Utilities.IsStatement));
            }
        }
        else
        {
            EmitStatement(Utilities.VisitNode(node, _visitor, Utilities.IsStatement));
        }
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:1434-1463 — <c>transformAndEmitDoStatement</c>.</summary>
    private void TransformAndEmitDoStatement(DoStatement node)
    {
        if (ContainsYield(node))
        {
            // [source]
            //      do {
            //          /*body*/
            //      }
            //      while (i < 10);
            //
            // [intermediate]
            //  .loop conditionLabel, endLabel
            //  .mark loopLabel
            //      /*body*/
            //  .mark conditionLabel
            //  .brtrue loopLabel, (i < 10)
            //  .endloop
            //  .mark endLabel

            var conditionLabel = DefineLabel();
            var loopLabel = DefineLabel();
            BeginLoopBlock(/*continueLabel*/ conditionLabel);
            MarkLabel(loopLabel);
            TransformAndEmitEmbeddedStatement(node.Statement!);
            MarkLabel(conditionLabel);
            EmitBreakWhenTrue(loopLabel, Debug.CheckDefined(Utilities.VisitNode(node.Expression, _visitor, Utilities.IsExpression)));
            EndLoopBlock();
        }
        else
        {
            EmitStatement(Utilities.VisitNode(node, _visitor, Utilities.IsStatement));
        }
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:1465-1475 — <c>visitDoStatement</c>.</summary>
    private Node VisitDoStatement(DoStatement node)
    {
        if (_inStatementContainingYield)
        {
            BeginScriptLoopBlock();
            node = Utilities.VisitEachChildWorker(node, _visitor, _context);
            EndLoopBlock();
            return node;
        }
        else
        {
            return Utilities.VisitEachChildWorker(node, _visitor, _context);
        }
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:1477-1504 — <c>transformAndEmitWhileStatement</c>.</summary>
    private void TransformAndEmitWhileStatement(WhileStatement node)
    {
        if (ContainsYield(node))
        {
            // [source]
            //      while (i < 10) {
            //          /*body*/
            //      }
            //
            // [intermediate]
            //  .loop loopLabel, endLabel
            //  .mark loopLabel
            //  .brfalse endLabel, (i < 10)
            //      /*body*/
            //  .br loopLabel
            //  .endloop
            //  .mark endLabel

            var loopLabel = DefineLabel();
            var endLabel = BeginLoopBlock(loopLabel);
            MarkLabel(loopLabel);
            EmitBreakWhenFalse(endLabel, Debug.CheckDefined(Utilities.VisitNode(node.Expression, _visitor, Utilities.IsExpression)));
            TransformAndEmitEmbeddedStatement(node.Statement!);
            EmitBreak(loopLabel);
            EndLoopBlock();
        }
        else
        {
            EmitStatement(Utilities.VisitNode(node, _visitor, Utilities.IsStatement));
        }
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:1506-1516 — <c>visitWhileStatement</c>.</summary>
    private Node VisitWhileStatement(WhileStatement node)
    {
        if (_inStatementContainingYield)
        {
            BeginScriptLoopBlock();
            node = Utilities.VisitEachChildWorker(node, _visitor, _context);
            EndLoopBlock();
            return node;
        }
        else
        {
            return Utilities.VisitEachChildWorker(node, _visitor, _context);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // generators.ts:1518-1740 — for/for-in
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/generators.ts:1518-1582 — <c>transformAndEmitForStatement</c>.</summary>
    private void TransformAndEmitForStatement(ForStatement node)
    {
        if (ContainsYield(node))
        {
            // [source]
            //      for (var i = 0; i < 10; i++) {
            //          /*body*/
            //      }
            //
            // [intermediate]
            //  .local i
            //      i = 0;
            //  .loop incrementLabel, endLoopLabel
            //  .mark conditionLabel
            //  .brfalse endLoopLabel, (i < 10)
            //      /*body*/
            //  .mark incrementLabel
            //      i++;
            //  .br conditionLabel
            //  .endloop
            //  .mark endLoopLabel

            var conditionLabel = DefineLabel();
            var incrementLabel = DefineLabel();
            var endLabel = BeginLoopBlock(incrementLabel);
            if (node.Initializer is not null)
            {
                var initializer = node.Initializer;
                if (NodeTests.IsVariableDeclarationList(initializer))
                {
                    TransformAndEmitVariableDeclarationList((VariableDeclarationList)initializer);
                }
                else
                {
                    EmitStatement(
                        Utilities.SetTextRange(
                            _factory.CreateExpressionStatement(
                                Debug.CheckDefined(Utilities.VisitNode((Expression)initializer, _visitor, Utilities.IsExpression))),
                            initializer));
                }
            }

            MarkLabel(conditionLabel);
            if (node.Condition is not null)
            {
                EmitBreakWhenFalse(endLabel, Debug.CheckDefined(Utilities.VisitNode(node.Condition, _visitor, Utilities.IsExpression)));
            }

            TransformAndEmitEmbeddedStatement(node.Statement!);

            MarkLabel(incrementLabel);
            if (node.Incrementor is not null)
            {
                EmitStatement(
                    Utilities.SetTextRange(
                        _factory.CreateExpressionStatement(
                            Debug.CheckDefined(Utilities.VisitNode(node.Incrementor, _visitor, Utilities.IsExpression))),
                        node.Incrementor));
            }
            EmitBreak(conditionLabel);
            EndLoopBlock();
        }
        else
        {
            EmitStatement(Utilities.VisitNode(node, _visitor, Utilities.IsStatement));
        }
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:1584-1615 — <c>visitForStatement</c>.</summary>
    private ForStatement VisitForStatement(ForStatement node)
    {
        // [source]
        //      for (var x in a) {
        //          /*body*/
        //      }
        //
        // [intermediate]
        //  .local x
        //  .loop
        //      for (x in a) {
        //          /*body*/
        //      }
        //  .endloop

        if (_inStatementContainingYield)
        {
            BeginScriptLoopBlock();
        }

        var initializer = node.Initializer;
        if (initializer is not null && NodeTests.IsVariableDeclarationList(initializer))
        {
            foreach (var variable in ((VariableDeclarationList)initializer).Declarations!)
            {
                _hoistVariableDeclaration((Identifier)variable.Name!);
            }

            var variables = TypeScriptTransformSupport.GetInitializedVariables((VariableDeclarationList)initializer);
            node = _factory.UpdateForStatement(
                node,
                variables.Count > 0
                    ? _factory.InlineExpressions(Core.Map(variables, (variable, _) => TransformInitializedVariable(variable)))
                    : null,
                Utilities.VisitNode(node.Condition, _visitor, Utilities.IsExpression),
                Utilities.VisitNode(node.Incrementor, _visitor, Utilities.IsExpression),
                Utilities.VisitIterationBody(node.Statement!, _visitor, _context));
        }
        else
        {
            node = Utilities.VisitEachChildWorker(node, _visitor, _context);
        }

        if (_inStatementContainingYield)
        {
            EndLoopBlock();
        }

        return node;
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:1617-1703 — <c>transformAndEmitForInStatement</c>.</summary>
    private void TransformAndEmitForInStatement(ForInStatement node)
    {
        if (ContainsYield(node))
        {
            // [source]
            //      for (var p in o) {
            //          /*body*/
            //      }
            //
            // [intermediate]
            //  .local _b, _a, _c, _i
            //      _b = [];
            //      _a = o;
            //      for (_c in _a) _b.push(_c);
            //      _i = 0;
            //  .loop incrementLabel, endLoopLabel
            //  .mark conditionLabel
            //  .brfalse endLoopLabel, (_i < _b.length)
            //      _c = _b[_i];
            //  .brfalse incrementLabel, (_c in _a)
            //      p = _c;
            //      /*body*/
            //  .mark incrementLabel
            //      _c++;
            //  .br conditionLabel
            //  .endloop
            //  .mark endLoopLabel

            var obj = DeclareLocal(); // _a
            var keysArray = DeclareLocal(); // _b
            var key = DeclareLocal(); // _c
            var keysIndex = _factory.CreateLoopVariable(null); // _i
            var initializer = node.Initializer;
            _hoistVariableDeclaration(keysIndex);
            EmitAssignment(obj, Debug.CheckDefined(Utilities.VisitNode(node.Expression, _visitor, Utilities.IsExpression)));
            EmitAssignment(keysArray, _factory.CreateArrayLiteralExpression([], null));

            EmitStatement(
                _factory.CreateForInStatement(
                    key,
                    obj,
                    _factory.CreateExpressionStatement(
                        _factory.CreateCallExpression(
                            _factory.CreatePropertyAccessExpression(keysArray, "push"),
                            /*typeArguments*/ null,
                            [key]))));

            EmitAssignment(keysIndex, _factory.CreateNumericLiteral(0));

            var conditionLabel = DefineLabel();
            var incrementLabel = DefineLabel();
            var endLoopLabel = BeginLoopBlock(incrementLabel);

            MarkLabel(conditionLabel);
            EmitBreakWhenFalse(endLoopLabel, _factory.CreateLessThan(keysIndex, _factory.CreatePropertyAccessExpression(keysArray, "length")));

            EmitAssignment(key, _factory.CreateElementAccessExpression(keysArray, keysIndex));
            EmitBreakWhenFalse(incrementLabel, _factory.CreateBinaryExpression(key, SyntaxKind.InKeyword, obj));

            Expression variable;
            if (NodeTests.IsVariableDeclarationList(initializer!))
            {
                foreach (var declaration in ((VariableDeclarationList)initializer!).Declarations!)
                {
                    _hoistVariableDeclaration((Identifier)declaration.Name!);
                }

                variable = _factory.CloneNode((Identifier)((VariableDeclarationList)initializer!).Declarations![0].Name!);
            }
            else
            {
                variable = Debug.CheckDefined(Utilities.VisitNode((Expression)initializer!, _visitor, Utilities.IsExpression));
                Debug.Assert(Utilities.IsLeftHandSideExpression(variable));
            }

            EmitAssignment(variable, key);
            TransformAndEmitEmbeddedStatement(node.Statement!);

            MarkLabel(incrementLabel);
            EmitStatement(_factory.CreateExpressionStatement(_factory.CreatePostfixIncrement(keysIndex)));

            EmitBreak(conditionLabel);
            EndLoopBlock();
        }
        else
        {
            EmitStatement(Utilities.VisitNode(node, _visitor, Utilities.IsStatement));
        }
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:1705-1740 — <c>visitForInStatement</c>.</summary>
    private ForInStatement VisitForInStatement(ForInStatement node)
    {
        // [source]
        //      for (var x in a) {
        //          /*body*/
        //      }
        //
        // [intermediate]
        //  .local x
        //  .loop
        //      for (x in a) {
        //          /*body*/
        //      }
        //  .endloop

        if (_inStatementContainingYield)
        {
            BeginScriptLoopBlock();
        }

        var initializer = node.Initializer;
        if (NodeTests.IsVariableDeclarationList(initializer!))
        {
            foreach (var variable in ((VariableDeclarationList)initializer!).Declarations!)
            {
                _hoistVariableDeclaration((Identifier)variable.Name!);
            }

            node = _factory.UpdateForInStatement(
                node,
                (Identifier)((VariableDeclarationList)initializer!).Declarations![0].Name!,
                Debug.CheckDefined(Utilities.VisitNode(node.Expression, _visitor, Utilities.IsExpression)),
                Debug.CheckDefined(Utilities.VisitNode(node.Statement, _visitor, Utilities.IsStatement, lifted => (Node)_factory.LiftToBlock([.. lifted]))));
        }
        else
        {
            node = Utilities.VisitEachChildWorker(node, _visitor, _context);
        }

        if (_inStatementContainingYield)
        {
            EndLoopBlock();
        }

        return node;
    }

    // ---------------------------------------------------------------------------------------------
    // generators.ts:1742-1798 — continue/break/return
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/generators.ts:1742-1751 — <c>transformAndEmitContinueStatement</c>.</summary>
    private void TransformAndEmitContinueStatement(ContinueStatement node)
    {
        var label = FindContinueTarget(node.Label is not null ? Utilities.IdText(node.Label) : null);
        if (label > 0)
        {
            EmitBreak(label, /*location*/ node);
        }
        else
        {
            // invalid continue without a containing loop. Leave the node as is, per #17875.
            EmitStatement(node);
        }
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:1753-1762 — <c>visitContinueStatement</c>.</summary>
    private IStatement VisitContinueStatement(ContinueStatement node)
    {
        if (_inStatementContainingYield)
        {
            var label = FindContinueTarget(node.Label is not null ? Utilities.IdText(node.Label) : null);
            if (label > 0)
            {
                return CreateInlineBreak(label, /*location*/ node);
            }
        }

        return Utilities.VisitEachChildWorker(node, _visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:1764-1773 — <c>transformAndEmitBreakStatement</c>.</summary>
    private void TransformAndEmitBreakStatement(BreakStatement node)
    {
        var label = FindBreakTarget(node.Label is not null ? Utilities.IdText(node.Label) : null);
        if (label > 0)
        {
            EmitBreak(label, /*location*/ node);
        }
        else
        {
            // invalid break without a containing loop, switch, or labeled statement. Leave the node as is, per #17875.
            EmitStatement(node);
        }
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:1775-1784 — <c>visitBreakStatement</c>.</summary>
    private IStatement VisitBreakStatement(BreakStatement node)
    {
        if (_inStatementContainingYield)
        {
            var label = FindBreakTarget(node.Label is not null ? Utilities.IdText(node.Label) : null);
            if (label > 0)
            {
                return CreateInlineBreak(label, /*location*/ node);
            }
        }

        return Utilities.VisitEachChildWorker(node, _visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:1786-1791 — <c>transformAndEmitReturnStatement</c>.</summary>
    private void TransformAndEmitReturnStatement(ReturnStatement node)
        => EmitReturn(
            Utilities.VisitNode(node.Expression, _visitor, Utilities.IsExpression),
            /*location*/ node);

    /// <summary>ported from: src/compiler/transformers/generators.ts:1793-1798 — <c>visitReturnStatement</c>.</summary>
    private IStatement VisitReturnStatement(ReturnStatement node)
        => CreateInlineReturn(
            Utilities.VisitNode(node.Expression, _visitor, Utilities.IsExpression),
            /*location*/ node);

    // ---------------------------------------------------------------------------------------------
    // generators.ts:1800-1941 — with/switch
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/generators.ts:1800-1818 — <c>transformAndEmitWithStatement</c>.</summary>
    private void TransformAndEmitWithStatement(WithStatement node)
    {
        if (ContainsYield(node))
        {
            // [source]
            //      with (x) {
            //          /*body*/
            //      }
            //
            // [intermediate]
            //  .with (x)
            //      /*body*/
            //  .endwith
            BeginWithBlock(CacheExpression(Debug.CheckDefined(Utilities.VisitNode(node.Expression, _visitor, Utilities.IsExpression))));
            TransformAndEmitEmbeddedStatement(node.Statement!);
            EndWithBlock();
        }
        else
        {
            EmitStatement(Utilities.VisitNode(node, _visitor, Utilities.IsStatement));
        }
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:1820-1927 — <c>transformAndEmitSwitchStatement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>caseBlock.clauses</c> elements are the union <c>CaseClause | DefaultClause</c>, which has no C#
    /// base interface; the <c>statements</c> slot both halves carry is read through the kind switch at the bottom. TS's
    /// <c>pendingClauses: CaseClause[]</c> is a <c>List&lt;Node&gt;</c> here because
    /// <c>NodeFactory.CreateCaseBlock</c> takes that erased element type (§3) and every element pushed is a
    /// <c>CaseClause</c>.
    /// </remarks>
    private void TransformAndEmitSwitchStatement(SwitchStatement node)
    {
        if (ContainsYield(node.CaseBlock!))
        {
            // [source]
            //      switch (x) {
            //          case a:
            //              /*caseStatements*/
            //          case b:
            //              /*caseStatements*/
            //          default:
            //              /*defaultStatements*/
            //      }
            //
            // [intermediate]
            //  .local _a
            //  .switch endLabel
            //      _a = x;
            //      switch (_a) {
            //          case a:
            //  .br clauseLabels[0]
            //      }
            //      switch (_a) {
            //          case b:
            //  .br clauseLabels[1]
            //      }
            //  .br clauseLabels[2]
            //  .mark clauseLabels[0]
            //      /*caseStatements*/
            //  .mark clauseLabels[1]
            //      /*caseStatements*/
            //  .mark clauseLabels[2]
            //      /*caseStatements*/
            //  .endswitch
            //  .mark endLabel

            var caseBlock = node.CaseBlock!;
            var numClauses = caseBlock.Clauses!.Count;
            var endLabel = BeginSwitchBlock();

            var expression = CacheExpression(Debug.CheckDefined(Utilities.VisitNode(node.Expression, _visitor, Utilities.IsExpression)));

            // Create labels for each clause and find the index of the first default clause.
            var clauseLabels = new List<int>();
            var defaultClauseIndex = -1;
            for (var i = 0; i < numClauses; i++)
            {
                var clause = caseBlock.Clauses[i];
                clauseLabels.Add(DefineLabel());
                if (clause.Kind == SyntaxKind.DefaultClause && defaultClauseIndex == -1)
                {
                    defaultClauseIndex = i;
                }
            }

            // Emit switch statements for each run of case clauses either from the first case
            // clause or the next case clause with a `yield` in its expression, up to the next
            // case clause with a `yield` in its expression.
            var clausesWritten = 0;
            var pendingClauses = new List<Node>();
            while (clausesWritten < numClauses)
            {
                var defaultClausesSkipped = 0;
                for (var i = clausesWritten; i < numClauses; i++)
                {
                    var clause = caseBlock.Clauses[i];
                    if (clause.Kind == SyntaxKind.CaseClause)
                    {
                        if (ContainsYield(((CaseClause)clause).Expression) && pendingClauses.Count > 0)
                        {
                            break;
                        }

                        pendingClauses.Add(
                            _factory.CreateCaseClause(
                                Debug.CheckDefined(Utilities.VisitNode(((CaseClause)clause).Expression, _visitor, Utilities.IsExpression)),
                                [
                                    CreateInlineBreak(clauseLabels[i], /*location*/ ((CaseClause)clause).Expression),
                                ]));
                    }
                    else
                    {
                        defaultClausesSkipped++;
                    }
                }

                if (pendingClauses.Count > 0)
                {
                    EmitStatement(_factory.CreateSwitchStatement(expression, _factory.CreateCaseBlock(pendingClauses)));
                    clausesWritten += pendingClauses.Count;
                    pendingClauses = [];
                }
                if (defaultClausesSkipped > 0)
                {
                    clausesWritten += defaultClausesSkipped;
                    defaultClausesSkipped = 0;
                }
            }

            if (defaultClauseIndex >= 0)
            {
                EmitBreak(clauseLabels[defaultClauseIndex]);
            }
            else
            {
                EmitBreak(endLabel);
            }

            for (var i = 0; i < numClauses; i++)
            {
                MarkLabel(clauseLabels[i]);
                var clause = caseBlock.Clauses[i];
                TransformAndEmitStatements(clause switch
                {
                    CaseClause caseClause => caseClause.Statements!,
                    DefaultClause defaultClause => defaultClause.Statements!,
                    _ => Debug.Fail<NodeArray<IStatement>>($"Unexpected clause kind: {clause.Kind}"),
                });
            }

            EndSwitchBlock();
        }
        else
        {
            EmitStatement(Utilities.VisitNode(node, _visitor, Utilities.IsStatement));
        }
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:1929-1941 — <c>visitSwitchStatement</c>.</summary>
    private Node VisitSwitchStatement(SwitchStatement node)
    {
        if (_inStatementContainingYield)
        {
            BeginScriptSwitchBlock();
        }

        node = Utilities.VisitEachChildWorker(node, _visitor, _context);

        if (_inStatementContainingYield)
        {
            EndSwitchBlock();
        }

        return node;
    }

    // ---------------------------------------------------------------------------------------------
    // generators.ts:1943-2035 — labeled/throw/try
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/generators.ts:1943-1962 — <c>transformAndEmitLabeledStatement</c>.</summary>
    private void TransformAndEmitLabeledStatement(LabeledStatement node)
    {
        if (ContainsYield(node))
        {
            // [source]
            //      x: {
            //          /*body*/
            //      }
            //
            // [intermediate]
            //  .labeled "x", endLabel
            //      /*body*/
            //  .endlabeled
            //  .mark endLabel
            BeginLabeledBlock(Utilities.IdText(node.Label!));
            TransformAndEmitEmbeddedStatement(node.Statement!);
            EndLabeledBlock();
        }
        else
        {
            EmitStatement(Utilities.VisitNode(node, _visitor, Utilities.IsStatement));
        }
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:1964-1976 — <c>visitLabeledStatement</c>.</summary>
    private Node VisitLabeledStatement(LabeledStatement node)
    {
        if (_inStatementContainingYield)
        {
            BeginScriptLabeledBlock(Utilities.IdText(node.Label!));
        }

        node = Utilities.VisitEachChildWorker(node, _visitor, _context);

        if (_inStatementContainingYield)
        {
            EndLabeledBlock();
        }

        return node;
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:1978-1984 — <c>transformAndEmitThrowStatement</c>.</summary>
    private void TransformAndEmitThrowStatement(ThrowStatement node)
    {
        // TODO(rbuckton): `expression` should be required on `throw`.
        EmitThrow(
            Debug.CheckDefined(Utilities.VisitNode(node.Expression ?? _factory.CreateVoidZero(), _visitor, Utilities.IsExpression)),
            /*location*/ node);
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:1986-2035 — <c>transformAndEmitTryStatement</c>.</summary>
    private void TransformAndEmitTryStatement(TryStatement node)
    {
        if (ContainsYield(node))
        {
            // [source]
            //      try {
            //          /*tryBlock*/
            //      }
            //      catch (e) {
            //          /*catchBlock*/
            //      }
            //      finally {
            //          /*finallyBlock*/
            //      }
            //
            // [intermediate]
            //  .local _a
            //  .try tryLabel, catchLabel, finallyLabel, endLabel
            //  .mark tryLabel
            //  .nop
            //      /*tryBlock*/
            //  .br endLabel
            //  .catch
            //  .mark catchLabel
            //      _a = %error%;
            //      /*catchBlock*/
            //  .br endLabel
            //  .finally
            //  .mark finallyLabel
            //      /*finallyBlock*/
            //  .endfinally
            //  .endtry
            //  .mark endLabel

            BeginExceptionBlock();
            TransformAndEmitEmbeddedStatement(node.TryBlock!);
            if (node.CatchClause is not null)
            {
                BeginCatchBlock(node.CatchClause.VariableDeclaration!); // TODO: GH#18217
                TransformAndEmitEmbeddedStatement(node.CatchClause.Block!);
            }

            if (node.FinallyBlock is not null)
            {
                BeginFinallyBlock();
                TransformAndEmitEmbeddedStatement(node.FinallyBlock);
            }

            EndExceptionBlock();
        }
        else
        {
            EmitStatement(Utilities.VisitEachChildWorker(node, _visitor, _context));
        }
    }

    // ---------------------------------------------------------------------------------------------
    // generators.ts:2037-2107 — the two predicates, the substitution hook and the local helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/generators.ts:2037-2039 — <c>containsYield</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is <c>Node | undefined</c>, the interface every node type satisfies there; in
    /// the port the call sites hold both node classes and the <c>IStatement</c> categorisation interface, so the
    /// parameter is <see cref="INode"/> — the capability that both sides share (§12.9).</remarks>
    private static bool ContainsYield(INode? node)
        => node is not null && (node.TransformFlags & TransformFlags.ContainsYield) != 0;

    /// <summary>ported from: src/compiler/transformers/generators.ts:2041-2050 — <c>countInitialNodesWithoutYield</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is the covariant <c>NodeArray&lt;Node&gt;</c>, which a
    /// <c>NodeArray&lt;Expression&gt;</c>/<c>NodeArray&lt;ObjectLiteralElementLike&gt;</c> satisfies there and cannot
    /// here (§12.9), so the port states the element type as a type variable — the same treatment
    /// <c>TransformUtilities.ElideNodes</c> records for the same shapes.</remarks>
    private static int CountInitialNodesWithoutYield<T>(NodeArray<T> nodes)
        where T : INode
    {
        var numNodes = nodes.Count;
        for (var i = 0; i < numNodes; i++)
        {
            if (ContainsYield(nodes[i])) return i;
        }

        return -1;
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:2052-2058 — <c>onSubstituteNode</c>.</summary>
    private Node OnSubstituteNode(EmitHint hint, Node node)
    {
        node = _previousOnSubstituteNode(hint, node);
        if (hint == EmitHint.Expression)
        {
            return SubstituteExpression((Expression)node);
        }
        return node;
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:2060-2065 — <c>substituteExpression</c>.</summary>
    private Expression SubstituteExpression(Expression node)
    {
        if (NodeTests.IsIdentifier(node))
        {
            return SubstituteExpressionIdentifier((Identifier)node);
        }
        return node;
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:2067-2086 — <c>substituteExpressionIdentifier</c>.</summary>
    private Expression SubstituteExpressionIdentifier(Identifier node)
    {
        if (!Utilities.IsGeneratedIdentifier(node) && _renamedCatchVariables is not null && _renamedCatchVariables.ContainsKey(Utilities.IdText(node)))
        {
            var original = Utilities.GetOriginalNode(node);
            if (NodeTests.IsIdentifier(original!) && original!.Parent is not null)
            {
                var declaration = _resolver.GetReferencedValueDeclaration((Identifier)original!);
                if (declaration is not null)
                {
                    var name = ReadRenamedCatchVariableDeclaration(TransformationUtilities.GetOriginalNodeId((Node)declaration));
                    if (name is not null)
                    {
                        // TODO(rbuckton): Does this need to be parented?
                        var clone = Utilities.SetParent(Utilities.SetTextRange(_factory.CloneNode(name), name), name.Parent)!;
                        EmitNodes.SetSourceMapRange(clone, TypeScriptTransformSupport.SourceMapRangeOf(node));
                        EmitNodes.SetCommentRange(clone, node);
                        return clone;
                    }
                }
            }
        }

        return node;
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:2088-2096 — <c>cacheExpression</c>.</summary>
    private Identifier CacheExpression(Expression node)
    {
        if (Utilities.IsGeneratedIdentifier(node) || (Utilities.GetEmitFlags(node) & EmitFlags.HelperName) != 0)
        {
            return (Identifier)node;
        }

        var temp = _factory.CreateTempVariable(_hoistVariableDeclaration, null);
        EmitAssignment(temp, node, /*location*/ node);
        return temp;
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:2098-2107 — <c>declareLocal</c>.</summary>
    private Identifier DeclareLocal(string? name = null)
    {
        var temp = Js.IsTruthy(name)
            ? _factory.CreateUniqueName(name, null)
            : _factory.CreateTempVariable(/*recordTempVariable*/ null, null);
        _hoistVariableDeclaration(temp);
        return temp;
    }

    /// <remarks>PORT-NOTE: the reference writes into the hole-filled <c>renamedCatchVariableDeclarations</c> array at
    /// <c>getOriginalNodeId</c>; this is that sparse write (see the field's remark).</remarks>
    private void WriteRenamedCatchVariableDeclaration(int index, Identifier name)
    {
        _renamedCatchVariableDeclarations ??= [];
        while (_renamedCatchVariableDeclarations.Count <= index) _renamedCatchVariableDeclarations.Add(null);
        _renamedCatchVariableDeclarations[index] = name;
    }

    /// <remarks>PORT-NOTE: the read half of <see cref="WriteRenamedCatchVariableDeclaration"/>; a hole reads as
    /// <c>null</c>, which the reference's <c>undefined</c> reads as here.</remarks>
    private Identifier? ReadRenamedCatchVariableDeclaration(int index)
        => _renamedCatchVariableDeclarations is { } declarations && index < declarations.Count ? declarations[index] : null;
}
