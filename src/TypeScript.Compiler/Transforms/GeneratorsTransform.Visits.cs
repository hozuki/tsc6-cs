#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/generators.ts:552-1278 — the visitors that rewrite the generator syntax.
/// <summary>ported from: src/compiler/transformers/generators.ts:552-1278 — the members of <c>transformGenerators</c>
/// that visit a generator's declarations, statements and yield-containing expressions.</summary>
internal sealed partial class GeneratorsTransformer
{
    // ---------------------------------------------------------------------------------------------
    // generators.ts:552-655 — declarations inside a generator function body
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/generators.ts:552-599 — <c>visitFunctionDeclaration</c>.</summary>
    private IStatement? VisitFunctionDeclaration(FunctionDeclaration node)
    {
        // Currently, we only support generators that were originally async functions.
        if (node.AsteriskToken is not null)
        {
            node = NodeFactory.SetOriginalNode(
                Utilities.SetTextRange(
                    _factory.CreateFunctionDeclaration(
                        node.Modifiers,
                        /*asteriskToken*/ null,
                        node.Name,
                        /*typeParameters*/ null,
                        Utilities.VisitParameterList(node.Parameters, _visitor, _context),
                        /*type*/ null,
                        TransformGeneratorFunctionBody(node.Body!)),
                    node),
                node);
        }
        else
        {
            var savedInGeneratorFunctionBody = _inGeneratorFunctionBody;
            var savedInStatementContainingYield = _inStatementContainingYield;
            _inGeneratorFunctionBody = false;
            _inStatementContainingYield = false;
            node = Utilities.VisitEachChildWorker(node, _visitor, _context);
            _inGeneratorFunctionBody = savedInGeneratorFunctionBody;
            _inStatementContainingYield = savedInStatementContainingYield;
        }

        if (_inGeneratorFunctionBody)
        {
            // Function declarations in a generator function body are hoisted
            // to the top of the lexical scope and elided from the current statement.
            _context.HoistFunctionDeclaration(node);
            return null;
        }
        else
        {
            return node;
        }
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:601-639 — <c>visitFunctionExpression</c>.</summary>
    private Expression VisitFunctionExpression(FunctionExpression node)
    {
        // Currently, we only support generators that were originally async functions.
        if (node.AsteriskToken is not null)
        {
            node = NodeFactory.SetOriginalNode(
                Utilities.SetTextRange(
                    _factory.CreateFunctionExpression(
                        /*modifiers*/ null,
                        /*asteriskToken*/ null,
                        node.Name,
                        /*typeParameters*/ null,
                        Utilities.VisitParameterList(node.Parameters, _visitor, _context),
                        /*type*/ null,
                        TransformGeneratorFunctionBody(node.Body!)),
                    node),
                node);
        }
        else
        {
            var savedInGeneratorFunctionBody = _inGeneratorFunctionBody;
            var savedInStatementContainingYield = _inStatementContainingYield;
            _inGeneratorFunctionBody = false;
            _inStatementContainingYield = false;
            node = Utilities.VisitEachChildWorker(node, _visitor, _context);
            _inGeneratorFunctionBody = savedInGeneratorFunctionBody;
            _inStatementContainingYield = savedInStatementContainingYield;
        }

        return node;
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:641-655 — <c>visitAccessorDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>AccessorDeclaration</c> = <c>GetAccessorDeclaration |
    /// SetAccessorDeclaration</c>, which has no C# base interface narrower than <see cref="Node"/>; the body only
    /// re-visits the node with the two generator flags cleared, so the port takes the <see cref="Node"/> the caller
    /// narrowed by kind. The reference's <c>node = visitEachChild(node, …)</c> reassignment is elided because the result
    /// is returned immediately.
    /// </remarks>
    private Node VisitAccessorDeclaration(Node node)
    {
        var savedInGeneratorFunctionBody = _inGeneratorFunctionBody;
        var savedInStatementContainingYield = _inStatementContainingYield;
        _inGeneratorFunctionBody = false;
        _inStatementContainingYield = false;
        node = Utilities.VisitEachChildWorker(node, _visitor, _context);
        _inGeneratorFunctionBody = savedInGeneratorFunctionBody;
        _inStatementContainingYield = savedInStatementContainingYield;
        return node;
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:657-724 — <c>transformGeneratorFunctionBody</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's local <c>statements</c> shadows the factory-level <c>statements</c> array that
    /// <c>build</c> fills; the port keeps the function's list as a local of this member (the field is
    /// <see cref="_statements"/>) so the two stay apart, exactly as the two JS bindings do.
    /// </remarks>
    private Block TransformGeneratorFunctionBody(Block body)
    {
        // Save existing generator state
        var statements = new List<IStatement>();
        var savedInGeneratorFunctionBody = _inGeneratorFunctionBody;
        var savedInStatementContainingYield = _inStatementContainingYield;
        var savedBlocks = _blocks;
        var savedBlockOffsets = _blockOffsets;
        var savedBlockActions = _blockActions;
        var savedBlockStack = _blockStack;
        var savedLabelOffsets = _labelOffsets;
        var savedLabelExpressions = _labelExpressions;
        var savedNextLabelId = _nextLabelId;
        var savedOperations = _operations;
        var savedOperationArguments = _operationArguments;
        var savedOperationLocations = _operationLocations;
        var savedState = _state;

        // Initialize generator state
        _inGeneratorFunctionBody = true;
        _inStatementContainingYield = false;
        _blocks = null;
        _blockOffsets = null;
        _blockActions = null;
        _blockStack = null;
        _labelOffsets = null;
        _labelExpressions = null;
        _nextLabelId = 1;
        _operations = null;
        _operationArguments = null;
        _operationLocations = null;
        _state = _factory.CreateTempVariable(/*recordTempVariable*/ null, null);

        // Build the generator
        _context.ResumeLexicalEnvironment();

        var statementOffset = _factory.CopyPrologue(body.Statements, statements, /*ensureUseStrict*/ false, _visitor);

        TransformAndEmitStatements(body.Statements!, statementOffset);

        var buildResult = Build();
        TypeScriptTransformSupport.InsertStatementsAfterStandardPrologue(statements, _context.EndLexicalEnvironment());
        statements.Add(_factory.CreateReturnStatement(buildResult));

        // Restore previous generator state
        _inGeneratorFunctionBody = savedInGeneratorFunctionBody;
        _inStatementContainingYield = savedInStatementContainingYield;
        _blocks = savedBlocks;
        _blockOffsets = savedBlockOffsets;
        _blockActions = savedBlockActions;
        _blockStack = savedBlockStack;
        _labelOffsets = savedLabelOffsets;
        _labelExpressions = savedLabelExpressions;
        _nextLabelId = savedNextLabelId;
        _operations = savedOperations;
        _operationArguments = savedOperationArguments;
        _operationLocations = savedOperationLocations;
        _state = savedState;

        return Utilities.SetTextRange(_factory.CreateBlock(statements, body.MultiLine), body);
    }

    // ---------------------------------------------------------------------------------------------
    // generators.ts:726-763 — variable statements
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/generators.ts:726-763 — <c>visitVariableStatement</c>.</summary>
    private IStatement? VisitVariableStatement(VariableStatement node)
    {
        if ((node.TransformFlags & TransformFlags.ContainsYield) != 0)
        {
            TransformAndEmitVariableDeclarationList(node.DeclarationList!);
            return null;
        }
        else
        {
            // Do not hoist custom prologues.
            if ((Utilities.GetEmitFlags(node) & EmitFlags.CustomPrologue) != 0)
            {
                return node;
            }

            foreach (var variable in node.DeclarationList!.Declarations!)
            {
                _hoistVariableDeclaration((Identifier)variable.Name!);
            }

            var variables = TypeScriptTransformSupport.GetInitializedVariables(node.DeclarationList);
            if (variables.Count == 0)
            {
                return null;
            }

            return EmitNodes.SetSourceMapRange(
                _factory.CreateExpressionStatement(
                    _factory.InlineExpressions(Core.Map(variables, (variable, _) => TransformInitializedVariable(variable)))),
                TypeScriptTransformSupport.SourceMapRangeOf(node));
        }
    }

    // ---------------------------------------------------------------------------------------------
    // generators.ts:765-1064 — yield-containing expressions
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/generators.ts:765-780 — <c>visitBinaryExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's default arm is <c>return Debug.assertNever(assoc)</c>, whose call throws and is typed
    /// <c>never</c>; the port's <see cref="Debug.AssertNever(object?, string)"/> returns <c>void</c> and a reader follows
    /// it (the shape <c>Checker/Checker.Check.Source.cs</c> established).
    /// </remarks>
    private Expression VisitBinaryExpression(BinaryExpression node)
    {
        var assoc = Utilities.GetExpressionAssociativity(node);
        switch (assoc)
        {
            case Associativity.Left:
                return VisitLeftAssociativeBinaryExpression(node);
            case Associativity.Right:
                return VisitRightAssociativeBinaryExpression(node);
            default:
                Debug.AssertNever(assoc);
                return null!;
        }
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:782-848 — <c>visitRightAssociativeBinaryExpression</c>.</summary>
    private Expression VisitRightAssociativeBinaryExpression(BinaryExpression node)
    {
        var left = node.Left!;
        var right = node.Right!;
        if (ContainsYield(right))
        {
            Expression target;
            switch (left.Kind)
            {
                case SyntaxKind.PropertyAccessExpression:
                    // [source]
                    //      a.b = yield;
                    //
                    // [intermediate]
                    //  .local _a
                    //      _a = a;
                    //  .yield resumeLabel
                    //  .mark resumeLabel
                    //      _a.b = %sent%;

                    target = _factory.UpdatePropertyAccessExpression(
                        (PropertyAccessExpression)left,
                        CacheExpression(Debug.CheckDefined(Utilities.VisitNode(((PropertyAccessExpression)left).Expression, _visitor, Utilities.IsLeftHandSideExpression))),
                        ((PropertyAccessExpression)left).Name);
                    break;

                case SyntaxKind.ElementAccessExpression:
                    // [source]
                    //      a[b] = yield;
                    //
                    // [intermediate]
                    //  .local _a, _b
                    //      _a = a;
                    //      _b = b;
                    //  .yield resumeLabel
                    //  .mark resumeLabel
                    //      _a[_b] = %sent%;

                    target = _factory.UpdateElementAccessExpression(
                        (ElementAccessExpression)left,
                        CacheExpression(Debug.CheckDefined(Utilities.VisitNode(((ElementAccessExpression)left).Expression, _visitor, Utilities.IsLeftHandSideExpression))),
                        CacheExpression(Debug.CheckDefined(Utilities.VisitNode(((ElementAccessExpression)left).ArgumentExpression, _visitor, Utilities.IsExpression))));
                    break;

                default:
                    target = Debug.CheckDefined(Utilities.VisitNode(left, _visitor, Utilities.IsExpression));
                    break;
            }

            var @operator = node.OperatorToken!.Kind;
            if (TransformationUtilities.IsCompoundAssignment(@operator))
            {
                return Utilities.SetTextRange(
                    _factory.CreateAssignment(
                        target,
                        Utilities.SetTextRange(
                            _factory.CreateBinaryExpression(
                                CacheExpression(target),
                                TransformationUtilities.GetNonAssignmentOperatorForCompoundAssignment(@operator),
                                Debug.CheckDefined(Utilities.VisitNode(right, _visitor, Utilities.IsExpression))),
                            node)),
                    node);
            }
            else
            {
                return _factory.UpdateBinaryExpression(node, target, node.OperatorToken, Debug.CheckDefined(Utilities.VisitNode(right, _visitor, Utilities.IsExpression)));
            }
        }

        return Utilities.VisitEachChildWorker(node, _visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:850-877 — <c>visitLeftAssociativeBinaryExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>isLogicalOperator</c> (utilitiesPublic.ts:1877-1879) has no public port: the one copy is
    /// <c>FactoryUtilities</c>'s private member (Parser/FactoryUtilities.cs:1027, owned by M4d). This is the second
    /// copy of its body in the port, the same treatment <c>Destructuring.AssertEachNode</c> records for
    /// <c>Utilities.AssertEachNode</c>; the batch report asks for one home.
    /// </remarks>
    private Expression VisitLeftAssociativeBinaryExpression(BinaryExpression node)
    {
        if (ContainsYield(node.Right!))
        {
            if (IsLogicalOperator(node.OperatorToken!.Kind))
            {
                return VisitLogicalBinaryExpression(node);
            }
            else if (node.OperatorToken.Kind == SyntaxKind.CommaToken)
            {
                return VisitCommaExpression(node);
            }

            // [source]
            //      a() + (yield) + c()
            //
            // [intermediate]
            //  .local _a
            //      _a = a();
            //  .yield resumeLabel
            //      _a + %sent% + c()

            return _factory.UpdateBinaryExpression(
                node,
                CacheExpression(Debug.CheckDefined(Utilities.VisitNode(node.Left, _visitor, Utilities.IsExpression))),
                node.OperatorToken,
                Debug.CheckDefined(Utilities.VisitNode(node.Right, _visitor, Utilities.IsExpression)));
        }

        return Utilities.VisitEachChildWorker(node, _visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:879-912 — <c>visitCommaExpression</c>.</summary>
    private Expression VisitCommaExpression(BinaryExpression node)
    {
        // [source]
        //      x = a(), yield, b();
        //
        // [intermediate]
        //      a();
        //  .yield resumeLabel
        //  .mark resumeLabel
        //      x = %sent%, b();

        var pendingExpressions = new List<Expression>();
        Visit(node.Left!);
        Visit(node.Right!);
        return _factory.InlineExpressions(pendingExpressions);

        void Visit(Expression node)
        {
            if (NodeTests.IsBinaryExpression(node) && ((BinaryExpression)node).OperatorToken!.Kind == SyntaxKind.CommaToken)
            {
                Visit(((BinaryExpression)node).Left!);
                Visit(((BinaryExpression)node).Right!);
            }
            else
            {
                if (ContainsYield(node) && pendingExpressions.Count > 0)
                {
                    EmitWorker(OpCode.Statement, OperationArguments.ForStatement(_factory.CreateExpressionStatement(_factory.InlineExpressions(pendingExpressions))));
                    pendingExpressions = [];
                }

                pendingExpressions.Add(Debug.CheckDefined(Utilities.VisitNode(node, _visitor, Utilities.IsExpression)));
            }
        }
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:915-936 — <c>visitCommaListExpression</c>.</summary>
    private Expression VisitCommaListExpression(CommaListExpression node)
    {
        // flattened version of `visitCommaExpression`
        var pendingExpressions = new List<Expression>();
        foreach (var elem in node.Elements!)
        {
            if (NodeTests.IsBinaryExpression(elem) && ((BinaryExpression)elem).OperatorToken!.Kind == SyntaxKind.CommaToken)
            {
                pendingExpressions.Add(VisitCommaExpression((BinaryExpression)elem));
            }
            else
            {
                if (ContainsYield(elem) && pendingExpressions.Count > 0)
                {
                    EmitWorker(OpCode.Statement, OperationArguments.ForStatement(_factory.CreateExpressionStatement(_factory.InlineExpressions(pendingExpressions))));
                    pendingExpressions = [];
                }

                pendingExpressions.Add(Debug.CheckDefined(Utilities.VisitNode(elem, _visitor, Utilities.IsExpression)));
            }
        }

        return _factory.InlineExpressions(pendingExpressions);
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:938-989 — <c>visitLogicalBinaryExpression</c>.</summary>
    private Expression VisitLogicalBinaryExpression(BinaryExpression node)
    {
        // Logical binary expressions (`&&` and `||`) are shortcutting expressions and need
        // to be transformed as such:
        //
        // [source]
        //      x = a() && yield;
        //
        // [intermediate]
        //  .local _a
        //      _a = a();
        //  .brfalse resultLabel, (_a)
        //  .yield resumeLabel
        //  .mark resumeLabel
        //      _a = %sent%;
        //  .mark resultLabel
        //      x = _a;

        var resultLabel = DefineLabel();
        var resultLocal = DeclareLocal();

        EmitAssignment(resultLocal, Debug.CheckDefined(Utilities.VisitNode(node.Left, _visitor, Utilities.IsExpression)), node.Left);
        if (node.OperatorToken!.Kind == SyntaxKind.AmpersandAmpersandToken)
        {
            // Logical `&&` shortcuts when the left-hand operand is falsey.
            EmitBreakWhenFalse(resultLabel, resultLocal, /*location*/ node.Left);
        }
        else
        {
            // Logical `||` shortcuts when the left-hand operand is truthy.
            EmitBreakWhenTrue(resultLabel, resultLocal, /*location*/ node.Left);
        }

        EmitAssignment(resultLocal, Debug.CheckDefined(Utilities.VisitNode(node.Right, _visitor, Utilities.IsExpression)), /*location*/ node.Right);
        MarkLabel(resultLabel);
        return resultLocal;
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:991-1029 — <c>visitConditionalExpression</c>.</summary>
    private Expression VisitConditionalExpression(ConditionalExpression node)
    {
        // We only need to perform a specific transformation if a `yield` expression exists
        // in either the `whenTrue` or `whenFalse` branches.
        // A `yield` in the condition will be handled by the normal visitor.
        if (ContainsYield(node.WhenTrue) || ContainsYield(node.WhenFalse))
        {
            var whenFalseLabel = DefineLabel();
            var resultLabel = DefineLabel();
            var resultLocal = DeclareLocal();
            EmitBreakWhenFalse(whenFalseLabel, Debug.CheckDefined(Utilities.VisitNode(node.Condition, _visitor, Utilities.IsExpression)), /*location*/ node.Condition);
            EmitAssignment(resultLocal, Debug.CheckDefined(Utilities.VisitNode(node.WhenTrue, _visitor, Utilities.IsExpression)), /*location*/ node.WhenTrue);
            EmitBreak(resultLabel);
            MarkLabel(whenFalseLabel);
            EmitAssignment(resultLocal, Debug.CheckDefined(Utilities.VisitNode(node.WhenFalse, _visitor, Utilities.IsExpression)), /*location*/ node.WhenFalse);
            MarkLabel(resultLabel);
            return resultLocal;
        }

        return Utilities.VisitEachChildWorker(node, _visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:1031-1060 — <c>visitYieldExpression</c>.</summary>
    private LeftHandSideExpression VisitYieldExpression(YieldExpression node)
    {
        // [source]
        //      x = yield a();
        //
        // [intermediate]
        //  .yield resumeLabel, (a())
        //  .mark resumeLabel
        //      x = %sent%;

        var resumeLabel = DefineLabel();
        var expression = Utilities.VisitNode(node.Expression, _visitor, Utilities.IsExpression);
        if (node.AsteriskToken is not null)
        {
            // NOTE: `expression` must be defined for `yield*`.
            var iterator = (Utilities.GetEmitFlags(node.Expression!) & EmitFlags.Iterator) == 0
                ? Utilities.SetTextRange(_emitHelpers.CreateValuesHelper(expression!), node)
                : expression;
            EmitYieldStar(iterator, /*location*/ node);
        }
        else
        {
            EmitYield(expression, /*location*/ node);
        }

        MarkLabel(resumeLabel);
        return CreateGeneratorResume(/*location*/ node);
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:1062-1064 — <c>visitArrayLiteralExpression</c>.</summary>
    private Expression VisitArrayLiteralExpression(ArrayLiteralExpression node)
        => VisitElements(node.Elements!, /*leadingElement*/ null, /*location*/ null, node.MultiLine);

    /// <summary>ported from: src/compiler/transformers/generators.ts:1073-1135 — <c>visitElements</c>.</summary>
    private Expression VisitElements(NodeArray<Expression> elements, Expression? leadingElement = null, ITextRange? location = null, bool? multiLine = null)
    {
        // [source]
        //      ar = [1, yield, 2];
        //
        // [intermediate]
        //  .local _a
        //      _a = [1];
        //  .yield resumeLabel
        //  .mark resumeLabel
        //      ar = _a.concat([%sent%, 2]);

        var numInitialElements = CountInitialNodesWithoutYield(elements);

        Identifier? temp = null;
        if (numInitialElements > 0)
        {
            temp = DeclareLocal();
            var initialElements = Utilities.VisitNodes(elements, _visitor, Utilities.IsExpression, 0, numInitialElements);
            EmitAssignment(
                temp,
                _factory.CreateArrayLiteralExpression(
                    leadingElement is not null
                        ? [leadingElement!, .. initialElements!]
                        : initialElements,
                    null));
            leadingElement = null;
        }

        var expressions = Core.ReduceLeft(elements, ReduceElement, new List<Expression>(), numInitialElements);
        return temp is not null
            ? _factory.CreateArrayConcatCall(temp, [_factory.CreateArrayLiteralExpression(expressions, multiLine)])
            : Utilities.SetTextRange(
                _factory.CreateArrayLiteralExpression(leadingElement is not null ? [leadingElement!, .. expressions] : expressions, multiLine),
                location);

        List<Expression> ReduceElement(List<Expression> expressions, Expression element, int _)
        {
            if (ContainsYield(element) && expressions.Count > 0)
            {
                var hasAssignedTemp = temp is not null;
                temp ??= DeclareLocal();

                EmitAssignment(
                    temp,
                    hasAssignedTemp
                        ? _factory.CreateArrayConcatCall(
                            temp,
                            [_factory.CreateArrayLiteralExpression(expressions, multiLine)])
                        : _factory.CreateArrayLiteralExpression(
                            leadingElement is not null ? [leadingElement!, .. expressions] : expressions,
                            multiLine));
                leadingElement = null;
                expressions = [];
            }

            expressions.Add(Debug.CheckDefined(Utilities.VisitNode(element, _visitor, Utilities.IsExpression)));
            return expressions;
        }
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:1137-1195 — <c>visitObjectLiteralExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE (the named blocker): the reference's <c>reduceProperty</c> calls
    /// <c>createExpressionForObjectLiteralElementLike(factory, node, property, temp)</c>
    /// (factory/utilities.ts:465-480), which is not ported — <c>FactoryUtilities</c>'s header records the omission, its
    /// blocking dependency being <c>getAllAccessorDeclarations</c> (utilities.ts) — and whose home file is outside this
    /// slice. <see cref="CreateExpressionForObjectLiteralElementLike"/> states the loud failure; everything else in this
    /// member is the reference's.
    /// </remarks>
    private Expression VisitObjectLiteralExpression(ObjectLiteralExpression node)
    {
        // [source]
        //      o = {
        //          a: 1,
        //          b: yield,
        //          c: 2
        //      };
        //
        // [intermediate]
        //  .local _a
        //      _a = {
        //          a: 1
        //      };
        //  .yield resumeLabel
        //  .mark resumeLabel
        //      o = (_a.b = %sent%,
        //          _a.c = 2,
        //          _a);

        var properties = node.Properties!;
        var multiLine = node.MultiLine;
        var numInitialProperties = CountInitialNodesWithoutYield(properties);

        var temp = DeclareLocal();
        EmitAssignment(
            temp,
            _factory.CreateObjectLiteralExpression(
                Utilities.VisitNodes(properties, _visitor, Utilities.IsObjectLiteralElementLike, 0, numInitialProperties),
                multiLine));

        var expressions = Core.ReduceLeft(properties, ReduceProperty, new List<Expression>(), numInitialProperties);
        // TODO(rbuckton): Does this need to be parented?
        expressions.Add(multiLine == true
            ? FactoryUtilities.StartOnNewLine(Utilities.SetParent(Utilities.SetTextRange(_factory.CloneNode(temp), temp), temp.Parent)!)
            : temp);
        return _factory.InlineExpressions(expressions);

        List<Expression> ReduceProperty(List<Expression> expressions, Node property, int _)
        {
            if (ContainsYield(property) && expressions.Count > 0)
            {
                EmitStatement(_factory.CreateExpressionStatement(_factory.InlineExpressions(expressions)));
                expressions = [];
            }

            var expression = CreateExpressionForObjectLiteralElementLike(node, property, temp);
            var visited = Utilities.VisitNode(expression, _visitor, Utilities.IsExpression);
            if (visited is not null)
            {
                if (multiLine == true)
                {
                    FactoryUtilities.StartOnNewLine(visited);
                }
                expressions.Add(visited);
            }
            return expressions;
        }
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:465-480 — <c>createExpressionForObjectLiteralElementLike</c>.</summary>
    /// <remarks>
    /// PORT-NOTE (the named blocker, §15.15): this member's home is <c>factory/utilities.ts</c>, which
    /// <c>FactoryUtilities</c> owns; the copy here is not a port of it but the loud failure its absence forces, named
    /// with the reference line and the blocking dependency. Its four helpers —
    /// <c>createExpressionForAccessorDeclaration</c> (factory/utilities.ts:362-409),
    /// <c>createExpressionForPropertyAssignment</c> (411-422), <c>createExpressionForShorthandPropertyAssignment</c>
    /// (424-435) and <c>createExpressionForMethodDeclaration</c> (437-463) — and
    /// <c>getAllAccessorDeclarations</c> (utilities.ts) land with it.
    /// </remarks>
    private static Expression CreateExpressionForObjectLiteralElementLike(ObjectLiteralExpression node, Node property, Identifier temp)
        => throw new NotSupportedException(
            "createExpressionForObjectLiteralElementLike (factory/utilities.ts:465) is not ported yet: it needs "
            + "getAllAccessorDeclarations from utilities.ts (docs/architecture.md §15.15, M4d/FactoryUtilities).");

    /// <summary>ported from: src/compiler/transformers/generators.ts:1197-1213 — <c>visitElementAccessExpression</c>.</summary>
    private Expression VisitElementAccessExpression(ElementAccessExpression node)
    {
        if (ContainsYield(node.ArgumentExpression))
        {
            // [source]
            //      a = x[yield];
            //
            // [intermediate]
            //  .local _a
            //      _a = x;
            //  .yield resumeLabel
            //  .mark resumeLabel
            //      a = _a[%sent%]

            return _factory.UpdateElementAccessExpression(
                node,
                CacheExpression(Debug.CheckDefined(Utilities.VisitNode(node.Expression, _visitor, Utilities.IsLeftHandSideExpression))),
                Debug.CheckDefined(Utilities.VisitNode(node.ArgumentExpression, _visitor, Utilities.IsExpression)));
        }

        return Utilities.VisitEachChildWorker(node, _visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:1215-1242 — <c>visitCallExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>forEach(node.arguments, containsYield)</c> is only read for its truthiness, which is what
    /// <see cref="Core.Some{T}(IList{T}?, Func{T, int, bool})"/> answers directly (the port's
    /// <see cref="Core.ForEach{T,TU}"/> is constrained to reference-typed results for the same reason).
    /// </remarks>
    private Expression VisitCallExpression(CallExpression node)
    {
        if (!Utilities.IsImportCall(node) && Core.Some(node.Arguments, (argument, _) => ContainsYield(argument)))
        {
            // [source]
            //      a.b(1, yield, 2);
            //
            // [intermediate]
            //  .local _a, _b, _c
            //      _b = (_a = a).b;
            //      _c = [1];
            //  .yield resumeLabel
            //  .mark resumeLabel
            //      _b.apply(_a, _c.concat([%sent%, 2]));
            var binding = _factory.CreateCallBinding(node.Expression, _hoistVariableDeclaration, _languageVersion, /*cacheIdentifiers*/ true);
            return NodeFactory.SetOriginalNode(
                Utilities.SetTextRange(
                    _factory.CreateFunctionApplyCall(
                        CacheExpression(Debug.CheckDefined(Utilities.VisitNode(binding.Target, _visitor, Utilities.IsLeftHandSideExpression))),
                        binding.ThisArg,
                        VisitElements(node.Arguments!)),
                    node),
                node);
        }

        return Utilities.VisitEachChildWorker(node, _visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:1244-1278 — <c>visitNewExpression</c>.</summary>
    private Expression VisitNewExpression(NewExpression node)
    {
        if (Core.Some(node.Arguments, (argument, _) => ContainsYield(argument)))
        {
            // [source]
            //      new a.b(1, yield, 2);
            //
            // [intermediate]
            //  .local _a, _b, _c
            //      _b = (_a = a.b).bind;
            //      _c = [1];
            //  .yield resumeLabel
            //  .mark resumeLabel
            //      new (_b.apply(_a, _c.concat([%sent%, 2])));

            var binding = _factory.CreateCallBinding(_factory.CreatePropertyAccessExpression(node.Expression, "bind"), _hoistVariableDeclaration, null, null);
            return NodeFactory.SetOriginalNode(
                Utilities.SetTextRange(
                    _factory.CreateNewExpression(
                        _factory.CreateFunctionApplyCall(
                            CacheExpression(Debug.CheckDefined(Utilities.VisitNode(binding.Target, _visitor, Utilities.IsExpression))),
                            binding.ThisArg,
                            VisitElements(
                                node.Arguments!,
                                /*leadingElement*/ _factory.CreateVoidZero())),
                        /*typeArguments*/ null,
                        []),
                    node),
                node);
        }

        return Utilities.VisitEachChildWorker(node, _visitor, _context);
    }

    /// <summary>ported from: src/compiler/utilities.ts:7408-7411 — <c>isLogicalOperator</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>transformers/generators.ts</c> imports the exported <c>utilities.ts</c> member, not
    /// <c>factory/utilities.ts</c>'s unexported one (whose body <c>FactoryUtilities</c> states privately, with the NOTE
    /// that only this version includes <c>ExclamationToken</c>). Those two private copies are the second copy of this
    /// body pair in the port, the same treatment <c>Destructuring.AssertEachNode</c> records for
    /// <c>Utilities.AssertEachNode</c>; the batch report asks for one home. (The <c>ExclamationToken</c> arm is inert at
    /// this call site: the operator token belongs to a <c>BinaryExpression</c>.)
    /// </remarks>
    private static bool IsLogicalOperator(SyntaxKind token)
        => IsBinaryLogicalOperator(token) || token == SyntaxKind.ExclamationToken;

    /// <summary>ported from: src/compiler/utilities.ts:7403-7405 — <c>isBinaryLogicalOperator</c>.</summary>
    private static bool IsBinaryLogicalOperator(SyntaxKind token)
        => token == SyntaxKind.BarBarToken || token == SyntaxKind.AmpersandAmpersandToken;
}
