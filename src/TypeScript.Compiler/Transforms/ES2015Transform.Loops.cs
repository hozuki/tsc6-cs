#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/es2015.ts:2935-3310, :3375-4092 — labels, the iteration-statement visitors
// and the converted-loop machinery that preserves per-iteration block scopes.
/// <summary>
/// The loop half of the ES2015 downlevel transform: the block-scoped-binding-in-a-closure rewrite.
/// </summary>
/// <remarks>
/// PORT-NOTE (the three structural <c>ForStatement</c> subtypes at es2015.ts:3375-3389): the reference declares
/// <c>ForStatementWithConvertibleInitializer</c>/<c>…Condition</c>/<c>…Incrementor</c> so its type guards narrow a
/// member to non-<c>undefined</c>; the port's guards keep the same tests over <see cref="ForStatement"/> and the
/// readers cast where they need the narrowed member.
/// </remarks>
internal sealed partial class ES2015Transformer
{
    /// <summary>ported from: src/compiler/transformers/es2015.ts:2935-2937 — <c>recordLabel</c>.</summary>
    private void RecordLabel(LabeledStatement node)
        => _convertedLoopState!.Labels![Utilities.IdText(node.Label!)] = true;

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2939-2941 — <c>resetLabel</c>.</summary>
    private void ResetLabel(LabeledStatement node)
        => _convertedLoopState!.Labels![Utilities.IdText(node.Label!)] = false;

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2943-2951 — <c>visitLabeledStatement</c>.</summary>
    private object? VisitLabeledStatement(LabeledStatement node)
    {
        if (_convertedLoopState is not null && _convertedLoopState.Labels is null)
        {
            _convertedLoopState.Labels = new Dictionary<string, bool>();
        }
        var statement = ES2015TransformSupport.UnwrapInnermostStatementOfLabel(
            node,
            _convertedLoopState is not null ? RecordLabel : null);
        return Utilities.IsIterationStatement((Node)statement, /*lookInLabeledStatements*/ false)
            ? VisitIterationStatement((IterationStatement)statement, node)
            : _factory.RestoreEnclosingLabel(
                Utilities.VisitNode(statement, Visitor, Utilities.IsStatement, _liftToBlock)
                    ?? (IStatement)Utilities.SetTextRange(_factory.CreateEmptyStatement(), (Node)statement),
                node,
                _convertedLoopState is not null ? ResetLabel : null);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2953-2965 — <c>visitIterationStatement</c>.</summary>
    private object? VisitIterationStatement(IterationStatement node, LabeledStatement outermostLabeledStatement)
    {
        switch (node.Kind)
        {
            case SyntaxKind.DoStatement:
            case SyntaxKind.WhileStatement:
                return VisitDoOrWhileStatement(node, outermostLabeledStatement);
            case SyntaxKind.ForStatement:
                return VisitForStatement((ForStatement)node, outermostLabeledStatement);
            case SyntaxKind.ForInStatement:
                return VisitForInStatement((ForInStatement)node, outermostLabeledStatement);
            case SyntaxKind.ForOfStatement:
                return VisitForOfStatement((ForOfStatement)node, outermostLabeledStatement);
            default:
                return Debug.Fail<object?>($"Unexpected node.\r\nNode {Debug.FormatSyntaxKind(node.Kind)} was unexpected.");
        }
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2967-2972 — <c>visitIterationStatementWithFacts</c>.</summary>
    private object? VisitIterationStatementWithFacts<T>(
        HierarchyFacts excludeFacts,
        HierarchyFacts includeFacts,
        T node,
        LabeledStatement? outermostLabeledStatement,
        LoopConverter<T>? convert = null)
        where T : IterationStatement
    {
        var ancestorFacts = EnterSubtree(excludeFacts, includeFacts);
        var updated = ConvertIterationStatementBodyIfNecessary(node, outermostLabeledStatement, ancestorFacts, convert);
        ExitSubtree(ancestorFacts, HierarchyFacts.None, HierarchyFacts.None);
        return updated;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2974-2981 — <c>visitDoOrWhileStatement</c>.</summary>
    private object? VisitDoOrWhileStatement(IterationStatement node, LabeledStatement? outermostLabeledStatement)
        => VisitIterationStatementWithFacts(
            HierarchyFacts.DoOrWhileStatementExcludes,
            HierarchyFacts.DoOrWhileStatementIncludes,
            node,
            outermostLabeledStatement);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2983-2990 — <c>visitForStatement</c>.</summary>
    private object? VisitForStatement(ForStatement node, LabeledStatement? outermostLabeledStatement)
        => VisitIterationStatementWithFacts(
            HierarchyFacts.ForStatementExcludes,
            HierarchyFacts.ForStatementIncludes,
            node,
            outermostLabeledStatement);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2992-3000 — <c>visitEachChildOfForStatement</c>.</summary>
    private ForStatement VisitEachChildOfForStatement(ForStatement node)
        => _factory.UpdateForStatement(
            node,
            Utilities.VisitNode(node.Initializer, VisitorWithUnusedExpressionResult, Utilities.IsForInitializer),
            Utilities.VisitNode(node.Condition, Visitor, Utilities.IsExpression),
            Utilities.VisitNode(node.Incrementor, VisitorWithUnusedExpressionResult, Utilities.IsExpression),
            Debug.CheckDefined(Utilities.VisitNode(node.Statement, Visitor, Utilities.IsStatement, _liftToBlock)));

    /// <summary>ported from: src/compiler/transformers/es2015.ts:3002-3009 — <c>visitForInStatement</c>.</summary>
    private object? VisitForInStatement(ForInStatement node, LabeledStatement? outermostLabeledStatement)
        => VisitIterationStatementWithFacts(
            HierarchyFacts.ForInOrForOfStatementExcludes,
            HierarchyFacts.ForInOrForOfStatementIncludes,
            node,
            outermostLabeledStatement);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:3011-3019 — <c>visitForOfStatement</c>.</summary>
    private object? VisitForOfStatement(ForOfStatement node, LabeledStatement? outermostLabeledStatement)
    {
        // PORT-NOTE: the reference hands `convertForOfStatementForArray`'s three-parameter member straight to the
        // four-slot <c>LoopConverter</c> (TS drops the extra argument); C# has no such arity conversion, so the
        // adapter states the unused `ancestorFacts` slot explicitly.
        LoopConverter<ForOfStatement> convert = Js.IsTruthy(_compilerOptions.DownlevelIteration)
            ? ConvertForOfStatementForIterable
            : (node, outermostLabeledStatement, convertedLoopBodyStatements, _) =>
                ConvertForOfStatementForArray(node, outermostLabeledStatement, convertedLoopBodyStatements);
        return VisitIterationStatementWithFacts(
            HierarchyFacts.ForInOrForOfStatementExcludes,
            HierarchyFacts.ForInOrForOfStatementIncludes,
            node,
            outermostLabeledStatement,
            convert);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:3021-3109 — <c>convertForOfStatementHead</c>.</summary>
    private IStatement ConvertForOfStatementHead(ForOfStatement node, Expression boundValue, List<IStatement>? convertedLoopBodyStatements)
    {
        var statements = new List<IStatement>();
        var initializer = node.Initializer!;
        if (NodeTests.IsVariableDeclarationList(initializer))
        {
            var list = (VariableDeclarationList)initializer;
            if ((list.Flags & NodeFlags.BlockScoped) != 0)
            {
                EnableSubstitutionsForBlockScopedBindings();
            }

            var firstOriginalDeclaration = Core.FirstOrUndefined(list.Declarations);
            if (firstOriginalDeclaration is not null && Utilities.IsBindingPattern(firstOriginalDeclaration.Name))
            {
                // This works whether the declaration is a var, let, or const.
                // It will use rhsIterationValue _a[_i] as the initializer.
                var declarations = Destructuring.FlattenDestructuringBinding(
                    firstOriginalDeclaration,
                    Visitor,
                    _context,
                    FlattenLevel.All,
                    boundValue);

                var declarationList = Utilities.SetTextRange(_factory.CreateVariableDeclarationList(declarations, null), node.Initializer);
                NodeFactory.SetOriginalNode(declarationList, node.Initializer);

                // Adjust the source map range for the first declaration to align with the old
                // emitter.
                EmitNodes.SetSourceMapRange(declarationList, TypeScriptTransformSupport.SourceMapRangeOf(
                    TypeScriptTransformSupport.CreateRange(declarations[0].Pos, Core.Last(declarations).End)));

                statements.Add(
                    _factory.CreateVariableStatement(
                        /*modifiers*/ null,
                        declarationList));
            }
            else
            {
                // The following call does not include the initializer, so we have
                // to emit it separately.
                statements.Add(
                    Utilities.SetTextRange(
                        _factory.CreateVariableStatement(
                            /*modifiers*/ null,
                            NodeFactory.SetOriginalNode(
                                Utilities.SetTextRange(
                                    _factory.CreateVariableDeclarationList(
                                        [
                                            _factory.CreateVariableDeclaration(
                                                firstOriginalDeclaration is not null
                                                    ? firstOriginalDeclaration.Name
                                                    : _factory.CreateTempVariable(/*recordTempVariable*/ null, null),
                                                /*exclamationToken*/ null,
                                                /*type*/ null,
                                                boundValue),
                                        ],
                                        null),
                                    TypeScriptTransformSupport.MoveRangePos(initializer, -1)),
                                initializer)),
                        ES2015TransformSupport.MoveRangeEnd(initializer, -1)));
            }
        }
        else
        {
            // Initializer is an expression. Emit the expression in the body, so that it's
            // evaluated on every iteration.
            var assignment = _factory.CreateAssignment((Expression)initializer, boundValue);
            if (Utilities.IsDestructuringAssignment(assignment))
            {
                statements.Add(_factory.CreateExpressionStatement(VisitBinaryExpression(assignment, /*expressionResultIsUnused*/ true)));
            }
            else
            {
                Utilities.SetTextRangeEnd(assignment, initializer.End);
                statements.Add(Utilities.SetTextRange(
                    _factory.CreateExpressionStatement(Debug.CheckDefined(Utilities.VisitNode(assignment, Visitor, Utilities.IsExpression))),
                    ES2015TransformSupport.MoveRangeEnd(initializer, -1)));
            }
        }

        if (convertedLoopBodyStatements is not null)
        {
            return CreateSyntheticBlockForConvertedStatements((List<IStatement>)Core.AddRange(statements, convertedLoopBodyStatements)!);
        }
        else
        {
            var statement = Utilities.VisitNode(node.Statement, Visitor, Utilities.IsStatement, _liftToBlock);
            Debug.Assert(statement is not null);
            if (NodeTests.IsBlock((Node)statement!))
            {
                return _factory.UpdateBlock(
                    (Block)statement!,
                    Utilities.SetTextRange(
                        _factory.CreateNodeArray(Core.Concatenate(statements, ((Block)statement!).Statements), null),
                        ((Block)statement!).Statements));
            }
            else
            {
                statements.Add(statement!);
                return CreateSyntheticBlockForConvertedStatements(statements);
            }
        }
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:3111-3119 — <c>createSyntheticBlockForConvertedStatements</c>.</summary>
    private Block CreateSyntheticBlockForConvertedStatements(List<IStatement> statements)
        => EmitNodes.SetEmitFlags(
            _factory.CreateBlock(
                _factory.CreateNodeArray(statements, null),
                /*multiLine*/ true),
            EmitFlags.NoSourceMap | EmitFlags.NoTokenSourceMaps);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:3121-3190 — <c>convertForOfStatementForArray</c>.</summary>
    private IStatement ConvertForOfStatementForArray(ForOfStatement node, LabeledStatement? outermostLabeledStatement, List<IStatement>? convertedLoopBodyStatements)
    {
        // The following ES6 code:
        //
        //    for (let v of expr) { }
        //
        // should be emitted as
        //
        //    for (var _i = 0, _a = expr; _i < _a.length; _i++) {
        //        var v = _a[_i];
        //    }
        //
        // where _a and _i are temps emitted to capture the RHS and the counter,
        // respectively.
        // When the left hand side is an expression instead of a let declaration,
        // the "let v" is not emitted.
        // When the left hand side is a let/const, the v is renamed if there is
        // another v in scope.
        // Note that all assignments to the LHS are emitted in the body, including
        // all destructuring.
        // Note also that because an extra statement is needed to assign to the LHS,
        // for-of bodies are always emitted as blocks.

        var expression = Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression);
        Debug.Assert(expression is not null);

        // In the case where the user wrote an identifier as the RHS, like this:
        //
        //     for (let v of arr) { }
        //
        // we don't want to emit a temporary variable for the RHS, just use it directly.
        var counter = _factory.CreateLoopVariable(/*reservedInNestedScopes*/ null);
        var rhsReference = NodeTests.IsIdentifier(expression!)
            ? _factory.GetGeneratedNameForNode(expression, /*flags*/ null)
            : _factory.CreateTempVariable(/*recordTempVariable*/ null, null);

        // The old emitter does not emit source maps for the expression
        EmitNodes.SetEmitFlags(expression!, EmitFlags.NoSourceMap | Utilities.GetEmitFlags(expression!));

        var forStatement = Utilities.SetTextRange(
            _factory.CreateForStatement(
                /*initializer*/ EmitNodes.SetEmitFlags(
                    Utilities.SetTextRange(
                        _factory.CreateVariableDeclarationList(
                            [
                                Utilities.SetTextRange(
                                    _factory.CreateVariableDeclaration(counter, /*exclamationToken*/ null, /*type*/ null, _factory.CreateNumericLiteral(0)),
                                    TypeScriptTransformSupport.MoveRangePos(node.Expression!, -1)),
                                Utilities.SetTextRange(
                                    _factory.CreateVariableDeclaration(rhsReference, /*exclamationToken*/ null, /*type*/ null, expression),
                                    node.Expression),
                            ],
                            null),
                        node.Expression),
                    EmitFlags.NoHoisting),
                /*condition*/ Utilities.SetTextRange(
                    _factory.CreateLessThan(
                        counter,
                        _factory.CreatePropertyAccessExpression(rhsReference, "length")),
                    node.Expression),
                /*incrementor*/ Utilities.SetTextRange(_factory.CreatePostfixIncrement(counter), node.Expression),
                /*statement*/ ConvertForOfStatementHead(
                    node,
                    _factory.CreateElementAccessExpression(rhsReference, counter),
                    convertedLoopBodyStatements)),
            /*location*/ node);

        // Disable trailing source maps for the OpenParenToken to align source map emit with the old emitter.
        EmitNodes.SetEmitFlags(forStatement, EmitFlags.NoTokenTrailingSourceMaps);
        Utilities.SetTextRange(forStatement, node);
        return _factory.RestoreEnclosingLabel(
            forStatement,
            outermostLabeledStatement,
            _convertedLoopState is not null ? ResetLabel : null);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:3192-3308 — <c>convertForOfStatementForIterable</c>.</summary>
    private IStatement ConvertForOfStatementForIterable(
        ForOfStatement node,
        LabeledStatement? outermostLabeledStatement,
        List<IStatement>? convertedLoopBodyStatements,
        HierarchyFacts ancestorFacts)
    {
        var expression = Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression);
        Debug.Assert(expression is not null);
        var iterator = NodeTests.IsIdentifier(expression!)
            ? _factory.GetGeneratedNameForNode(expression, /*flags*/ null)
            : _factory.CreateTempVariable(/*recordTempVariable*/ null, null);
        var result = NodeTests.IsIdentifier(expression!)
            ? _factory.GetGeneratedNameForNode(iterator, /*flags*/ null)
            : _factory.CreateTempVariable(/*recordTempVariable*/ null, null);
        var errorRecord = _factory.CreateUniqueName("e", /*flags*/ null);
        var catchVariable = _factory.GetGeneratedNameForNode(errorRecord, /*flags*/ null);
        var returnMethod = _factory.CreateTempVariable(/*recordTempVariable*/ null, null);
        var values = Utilities.SetTextRange(_emitHelpers.CreateValuesHelper(expression), node.Expression);
        var next = _factory.CreateCallExpression(
            _factory.CreatePropertyAccessExpression(iterator, "next"),
            /*typeArguments*/ null,
            []);

        _context.HoistVariableDeclaration(errorRecord);
        _context.HoistVariableDeclaration(returnMethod);

        // if we are enclosed in an outer loop ensure we reset 'errorRecord' per each iteration
        var initializer = (ancestorFacts & HierarchyFacts.IterationContainer) != 0
            ? _factory.InlineExpressions([_factory.CreateAssignment(errorRecord, _factory.CreateVoidZero()), values])
            : values;

        var forStatement = EmitNodes.SetEmitFlags(
            Utilities.SetTextRange(
                _factory.CreateForStatement(
                    /*initializer*/ EmitNodes.SetEmitFlags(
                        Utilities.SetTextRange(
                            _factory.CreateVariableDeclarationList(
                                [
                                    Utilities.SetTextRange(
                                        _factory.CreateVariableDeclaration(iterator, /*exclamationToken*/ null, /*type*/ null, initializer),
                                        node.Expression),
                                    _factory.CreateVariableDeclaration(result, /*exclamationToken*/ null, /*type*/ null, next),
                                ],
                                null),
                            node.Expression),
                        EmitFlags.NoHoisting),
                    /*condition*/ _factory.CreateLogicalNot(_factory.CreatePropertyAccessExpression(result, "done")),
                    /*incrementor*/ _factory.CreateAssignment(result, next),
                    /*statement*/ ConvertForOfStatementHead(
                        node,
                        _factory.CreatePropertyAccessExpression(result, "value"),
                        convertedLoopBodyStatements)),
                /*location*/ node),
            EmitFlags.NoTokenTrailingSourceMaps);

        return _factory.CreateTryStatement(
            _factory.CreateBlock(
                [
                    _factory.RestoreEnclosingLabel(
                        forStatement,
                        outermostLabeledStatement,
                        _convertedLoopState is not null ? ResetLabel : null),
                ],
                null),
            _factory.CreateCatchClause(
                _factory.CreateVariableDeclaration(catchVariable, null, null, null),
                EmitNodes.SetEmitFlags(
                    _factory.CreateBlock(
                        [
                            _factory.CreateExpressionStatement(
                                _factory.CreateAssignment(
                                    errorRecord,
                                    _factory.CreateObjectLiteralExpression(
                                        [
                                            _factory.CreatePropertyAssignment("error", catchVariable),
                                        ],
                                        null))),
                        ],
                        null),
                    EmitFlags.SingleLine)),
            _factory.CreateBlock(
                [
                    _factory.CreateTryStatement(
                        /*tryBlock*/ _factory.CreateBlock(
                            [
                                EmitNodes.SetEmitFlags(
                                    _factory.CreateIfStatement(
                                        _factory.CreateLogicalAnd(
                                            _factory.CreateLogicalAnd(
                                                result,
                                                _factory.CreateLogicalNot(
                                                    _factory.CreatePropertyAccessExpression(result, "done"))),
                                            _factory.CreateAssignment(
                                                returnMethod,
                                                _factory.CreatePropertyAccessExpression(iterator, "return"))),
                                        _factory.CreateExpressionStatement(
                                            _factory.CreateFunctionCallCall(returnMethod, iterator, [])),
                                        /*elseStatement*/ null),
                                    EmitFlags.SingleLine),
                            ],
                            null),
                        /*catchClause*/ null,
                        /*finallyBlock*/ EmitNodes.SetEmitFlags(
                            _factory.CreateBlock(
                                [
                                    EmitNodes.SetEmitFlags(
                                        _factory.CreateIfStatement(
                                            errorRecord,
                                            _factory.CreateThrowStatement(
                                                _factory.CreatePropertyAccessExpression(errorRecord, "error")),
                                        /*elseStatement*/ null),
                                        EmitFlags.SingleLine),
                                ],
                                null),
                            EmitFlags.SingleLine)),
                ],
                null));
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:3375-3377 — <c>shouldConvertPartOfIterationStatement</c>.</summary>
    private bool ShouldConvertPartOfIterationStatement(Node node)
        => _resolver.HasNodeCheckFlag(node, NodeCheckFlags.ContainsCapturedBlockScopeBinding);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:3379-3381 — <c>shouldConvertInitializerOfForStatement</c>.</summary>
    private bool ShouldConvertInitializerOfForStatement(IterationStatement node)
        => NodeTests.IsForStatement(node)
            && ((ForStatement)node).Initializer is not null
            && ShouldConvertPartOfIterationStatement(((ForStatement)node).Initializer!);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:3383-3385 — <c>shouldConvertConditionOfForStatement</c>.</summary>
    private bool ShouldConvertConditionOfForStatement(IterationStatement node)
        => NodeTests.IsForStatement(node)
            && ((ForStatement)node).Condition is not null
            && ShouldConvertPartOfIterationStatement(((ForStatement)node).Condition!);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:3387-3389 — <c>shouldConvertIncrementorOfForStatement</c>.</summary>
    private bool ShouldConvertIncrementorOfForStatement(IterationStatement node)
        => NodeTests.IsForStatement(node)
            && ((ForStatement)node).Incrementor is not null
            && ShouldConvertPartOfIterationStatement(((ForStatement)node).Incrementor!);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:3391-3394 — <c>shouldConvertIterationStatement</c>.</summary>
    private bool ShouldConvertIterationStatement(IterationStatement node)
        => ShouldConvertBodyOfIterationStatement(node)
            || ShouldConvertInitializerOfForStatement(node);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:3396-3398 — <c>shouldConvertBodyOfIterationStatement</c>.</summary>
    private bool ShouldConvertBodyOfIterationStatement(IterationStatement node)
        => _resolver.HasNodeCheckFlag(node, NodeCheckFlags.LoopWithCapturedBlockScopedBinding);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:3403-3422 — <c>hoistVariableDeclarationDeclaredInConvertedLoop</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's local <c>visit</c> over <c>Identifier | BindingPattern</c> is a local function
    /// here; <c>node.elements</c> is read through
    /// <see cref="FactoryUtilities.GetElementsOfBindingOrAssignmentPattern"/> since the two binding-pattern classes
    /// share no base (the same treatment <c>transformers/utilities.ts</c> records).</remarks>
    private static void HoistVariableDeclarationDeclaredInConvertedLoop(ConvertedLoopState state, VariableDeclaration node)
    {
        state.HoistedLocalVariables ??= [];

        void VisitName(Node name)
        {
            if (name.Kind == SyntaxKind.Identifier)
            {
                state.HoistedLocalVariables!.Add((Identifier)name);
            }
            else
            {
                foreach (var element in FactoryUtilities.GetElementsOfBindingOrAssignmentPattern(name))
                {
                    if (!NodeTests.IsOmittedExpression(element))
                    {
                        VisitName(((BindingElement)element).Name!);
                    }
                }
            }
        }

        VisitName(node.Name!);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:3424-3485 — <c>convertIterationStatementBodyIfNecessary</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>saveAllowedNonLabeledJumps: Jump | undefined</c> is <c>Jump?</c>, restored through
    /// <see cref="Jump.None"/> for the unset case.</remarks>
    private object? ConvertIterationStatementBodyIfNecessary<T>(
        T node,
        LabeledStatement? outermostLabeledStatement,
        HierarchyFacts ancestorFacts,
        LoopConverter<T>? convert = null)
        where T : IterationStatement
    {
        if (!ShouldConvertIterationStatement(node))
        {
            Jump? saveAllowedNonLabeledJumps = null;
            if (_convertedLoopState is not null)
            {
                // we get here if we are trying to emit normal loop loop inside converted loop
                // set allowedNonLabeledJumps to Break | Continue to mark that break\continue inside the loop should be emitted as is
                saveAllowedNonLabeledJumps = _convertedLoopState.AllowedNonLabeledJumps;
                _convertedLoopState.AllowedNonLabeledJumps = Jump.Break | Jump.Continue;
            }

            var result = convert is not null
                ? convert(node, outermostLabeledStatement, /*convertedLoopBodyStatements*/ null, ancestorFacts)
                : _factory.RestoreEnclosingLabel(
                    NodeTests.IsForStatement(node)
                        ? VisitEachChildOfForStatement((ForStatement)(object)node)
                        : (IStatement)Utilities.VisitEachChildWorker(node, Visitor, _context),
                    outermostLabeledStatement,
                    _convertedLoopState is not null ? ResetLabel : null);

            if (_convertedLoopState is not null)
            {
                _convertedLoopState.AllowedNonLabeledJumps = saveAllowedNonLabeledJumps ?? Jump.None;
            }
            return result;
        }

        var currentState = CreateConvertedLoopState(node);
        var statements = new List<IStatement>();

        var outerConvertedLoopState = _convertedLoopState;
        _convertedLoopState = currentState;

        var initializerFunction = ShouldConvertInitializerOfForStatement(node)
            ? CreateFunctionForInitializerOfForStatement((ForStatement)(object)node, currentState)
            : null;
        var bodyFunction = ShouldConvertBodyOfIterationStatement(node)
            ? CreateFunctionForBodyOfIterationStatement(node, currentState, outerConvertedLoopState)
            : null;

        _convertedLoopState = outerConvertedLoopState;

        if (initializerFunction is not null) statements.Add(initializerFunction.FunctionDeclaration);
        if (bodyFunction is not null) statements.Add(bodyFunction.FunctionDeclaration);

        AddExtraDeclarationsForConvertedLoop(statements, currentState, outerConvertedLoopState);

        if (initializerFunction is not null)
        {
            statements.Add(GenerateCallToConvertedLoopInitializer(initializerFunction.FunctionName, initializerFunction.ContainsYield));
        }

        IStatement loop;
        if (bodyFunction is not null)
        {
            if (convert is not null)
            {
                loop = convert(node, outermostLabeledStatement, bodyFunction.Part, ancestorFacts);
            }
            else
            {
                var clone = ConvertIterationStatementCore(node, initializerFunction, _factory.CreateBlock(bodyFunction.Part, /*multiLine*/ true));
                loop = _factory.RestoreEnclosingLabel(clone, outermostLabeledStatement, _convertedLoopState is not null ? ResetLabel : null);
            }
        }
        else
        {
            var clone = ConvertIterationStatementCore(
                node,
                initializerFunction,
                Debug.CheckDefined(Utilities.VisitNode(node.Statement, Visitor, Utilities.IsStatement, _liftToBlock)));
            loop = _factory.RestoreEnclosingLabel(clone, outermostLabeledStatement, _convertedLoopState is not null ? ResetLabel : null);
        }

        statements.Add(loop);
        return statements;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:3487-3502 — <c>convertIterationStatementCore</c>.</summary>
    private IStatement ConvertIterationStatementCore(
        IterationStatement node,
        IterationStatementPartFunction<VariableDeclarationList>? initializerFunction,
        IStatement convertedLoopBody)
    {
        switch (node.Kind)
        {
            case SyntaxKind.ForStatement:
                return ConvertForStatement((ForStatement)node, initializerFunction, convertedLoopBody);
            case SyntaxKind.ForInStatement:
                return ConvertForInStatement((ForInStatement)node, convertedLoopBody);
            case SyntaxKind.ForOfStatement:
                return ConvertForOfStatement((ForOfStatement)node, convertedLoopBody);
            case SyntaxKind.DoStatement:
                return ConvertDoStatement((DoStatement)node, convertedLoopBody);
            case SyntaxKind.WhileStatement:
                return ConvertWhileStatement((WhileStatement)node, convertedLoopBody);
            default:
                return Debug.Fail<IStatement>($"IterationStatement expected.\r\nNode {Debug.FormatSyntaxKind(node.Kind)} was unexpected.");
        }
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:3504-3514 — <c>convertForStatement</c>.</summary>
    private ForStatement ConvertForStatement(
        ForStatement node,
        IterationStatementPartFunction<VariableDeclarationList>? initializerFunction,
        IStatement convertedLoopBody)
    {
        var shouldConvertCondition = node.Condition is not null && ShouldConvertPartOfIterationStatement(node.Condition);
        var shouldConvertIncrementor = shouldConvertCondition || (node.Incrementor is not null && ShouldConvertPartOfIterationStatement(node.Incrementor));
        return _factory.UpdateForStatement(
            node,
            Utilities.VisitNode(initializerFunction is not null ? initializerFunction.Part : node.Initializer, VisitorWithUnusedExpressionResult, Utilities.IsForInitializer),
            Utilities.VisitNode(shouldConvertCondition ? null : node.Condition, Visitor, Utilities.IsExpression),
            Utilities.VisitNode(shouldConvertIncrementor ? null : node.Incrementor, VisitorWithUnusedExpressionResult, Utilities.IsExpression),
            convertedLoopBody);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:3516-3524 — <c>convertForOfStatement</c>.</summary>
    private ForOfStatement ConvertForOfStatement(ForOfStatement node, IStatement convertedLoopBody)
        => _factory.UpdateForOfStatement(
            node,
            /*awaitModifier*/ null,
            Debug.CheckDefined(Utilities.VisitNode(node.Initializer, Visitor, Utilities.IsForInitializer)),
            Debug.CheckDefined(Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression)),
            convertedLoopBody);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:3526-3533 — <c>convertForInStatement</c>.</summary>
    private ForInStatement ConvertForInStatement(ForInStatement node, IStatement convertedLoopBody)
        => _factory.UpdateForInStatement(
            node,
            Debug.CheckDefined(Utilities.VisitNode(node.Initializer, Visitor, Utilities.IsForInitializer)),
            Debug.CheckDefined(Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression)),
            convertedLoopBody);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:3535-3541 — <c>convertDoStatement</c>.</summary>
    private DoStatement ConvertDoStatement(DoStatement node, IStatement convertedLoopBody)
        => _factory.UpdateDoStatement(
            node,
            convertedLoopBody,
            Debug.CheckDefined(Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression)));

    /// <summary>ported from: src/compiler/transformers/es2015.ts:3543-3549 — <c>convertWhileStatement</c>.</summary>
    private WhileStatement ConvertWhileStatement(WhileStatement node, IStatement convertedLoopBody)
        => _factory.UpdateWhileStatement(
            node,
            Debug.CheckDefined(Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression)),
            convertedLoopBody);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:3551-3598 — <c>createConvertedLoopState</c>.</summary>
    private ConvertedLoopState CreateConvertedLoopState(IterationStatement node)
    {
        VariableDeclarationList? loopInitializer = null;
        switch (node.Kind)
        {
            case SyntaxKind.ForStatement:
            case SyntaxKind.ForInStatement:
            case SyntaxKind.ForOfStatement:
                var initializer = node switch
                {
                    ForStatement forStatement => forStatement.Initializer,
                    ForInStatement forInStatement => forInStatement.Initializer,
                    _ => ((ForOfStatement)node).Initializer,
                };
                if (initializer is not null && initializer.Kind == SyntaxKind.VariableDeclarationList)
                {
                    loopInitializer = (VariableDeclarationList)initializer;
                }
                break;
        }

        // variables that will be passed to the loop as parameters
        var loopParameters = new List<ParameterDeclaration>();
        // variables declared in the loop initializer that will be changed inside the loop
        var loopOutParameters = new List<LoopOutParameter>();
        if (loopInitializer is not null && (Utilities.GetCombinedNodeFlags(loopInitializer) & NodeFlags.BlockScoped) != 0)
        {
            var hasCapturedBindingsInForHead = ShouldConvertInitializerOfForStatement(node)
                || ShouldConvertConditionOfForStatement(node)
                || ShouldConvertIncrementorOfForStatement(node);
            foreach (var decl in loopInitializer.Declarations!)
            {
                ProcessLoopVariableDeclaration(node, decl, loopParameters, loopOutParameters, hasCapturedBindingsInForHead);
            }
        }

        var currentState = new ConvertedLoopState { LoopParameters = loopParameters, LoopOutParameters = loopOutParameters };
        if (_convertedLoopState is not null)
        {
            // convertedOuterLoopState !== undefined means that this converted loop is nested in another converted loop.
            // if outer converted loop has already accumulated some state - pass it through
            if (_convertedLoopState.ArgumentsName is not null)
            {
                // outer loop has already used 'arguments' so we've already have some name to alias it
                // use the same name in all nested loops
                currentState.ArgumentsName = _convertedLoopState.ArgumentsName;
            }
            if (_convertedLoopState.ThisName is not null)
            {
                // outer loop has already used 'this' so we've already have some name to alias it
                // use the same name in all nested loops
                currentState.ThisName = _convertedLoopState.ThisName;
            }
            if (_convertedLoopState.HoistedLocalVariables is not null)
            {
                // we've already collected some non-block scoped variable declarations in enclosing loop
                // use the same storage in nested loop
                currentState.HoistedLocalVariables = _convertedLoopState.HoistedLocalVariables;
            }
        }
        return currentState;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:3600-3692 — <c>addExtraDeclarationsForConvertedLoop</c>.</summary>
    private void AddExtraDeclarationsForConvertedLoop(List<IStatement> statements, ConvertedLoopState state, ConvertedLoopState? outerState)
    {
        List<VariableDeclaration>? extraVariableDeclarations = null;
        // propagate state from the inner loop to the outer loop if necessary
        if (state.ArgumentsName is not null)
        {
            // if alias for arguments is set
            if (outerState is not null)
            {
                // pass it to outer converted loop
                outerState.ArgumentsName = state.ArgumentsName;
            }
            else
            {
                // this is top level converted loop and we need to create an alias for 'arguments' object
                (extraVariableDeclarations ??= []).Add(
                    _factory.CreateVariableDeclaration(
                        state.ArgumentsName,
                        /*exclamationToken*/ null,
                        /*type*/ null,
                        _factory.CreateIdentifier("arguments", null, null)));
            }
        }

        if (state.ThisName is not null)
        {
            // if alias for this is set
            if (outerState is not null)
            {
                // pass it to outer converted loop
                outerState.ThisName = state.ThisName;
            }
            else
            {
                // this is top level converted loop so we need to create an alias for 'this' here
                // NOTE:
                // if converted loops were all nested in arrow function then we'll always emit '_this' so convertedLoopState.thisName will not be set.
                // If it is set this means that all nested loops are not nested in arrow function and it is safe to capture 'this'.
                (extraVariableDeclarations ??= []).Add(
                    _factory.CreateVariableDeclaration(
                        state.ThisName,
                        /*exclamationToken*/ null,
                        /*type*/ null,
                        _factory.CreateIdentifier("this", null, null)));
            }
        }

        if (state.HoistedLocalVariables is not null)
        {
            // if hoistedLocalVariables !== undefined this means that we've possibly collected some variable declarations to be hoisted later
            if (outerState is not null)
            {
                // pass them to outer converted loop
                outerState.HoistedLocalVariables = state.HoistedLocalVariables;
            }
            else
            {
                extraVariableDeclarations ??= [];
                // hoist collected variable declarations
                foreach (var identifier in state.HoistedLocalVariables)
                {
                    extraVariableDeclarations.Add(_factory.CreateVariableDeclaration(identifier, null, null, null));
                }
            }
        }

        // add extra variables to hold out parameters if necessary
        if (state.LoopOutParameters.Count != 0)
        {
            extraVariableDeclarations ??= [];
            foreach (var outParam in state.LoopOutParameters)
            {
                extraVariableDeclarations.Add(_factory.CreateVariableDeclaration(outParam.OutParamName, null, null, null));
            }
        }

        if (state.ConditionVariable is not null)
        {
            extraVariableDeclarations ??= [];
            extraVariableDeclarations.Add(
                _factory.CreateVariableDeclaration(state.ConditionVariable, /*exclamationToken*/ null, /*type*/ null, _factory.CreateFalse()));
        }

        // create variable statement to hold all introduced variable declarations
        if (extraVariableDeclarations is not null)
        {
            statements.Add(_factory.CreateVariableStatement(
                /*modifiers*/ null,
                _factory.CreateVariableDeclarationList(extraVariableDeclarations, null)));
        }
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:3694-3696 — <c>createOutVariable</c>.</summary>
    private VariableDeclaration CreateOutVariable(LoopOutParameter p)
        => _factory.CreateVariableDeclaration(p.OriginalName, /*exclamationToken*/ null, /*type*/ null, p.OutParamName);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:3704-3775 — <c>createFunctionForInitializerOfForStatement</c>.</summary>
    private IterationStatementPartFunction<VariableDeclarationList> CreateFunctionForInitializerOfForStatement(
        ForStatement node,
        ConvertedLoopState currentState)
    {
        var functionName = _factory.CreateUniqueName("_loop_init", /*flags*/ null);

        var initializer = node.Initializer!;
        var containsYield = (initializer.TransformFlags & TransformFlags.ContainsYield) != 0;
        var emitFlags = EmitFlags.None;
        if (currentState.ContainsLexicalThis) emitFlags |= EmitFlags.CapturesThis;
        if (containsYield && (_hierarchyFacts & HierarchyFacts.AsyncFunctionBody) != 0) emitFlags |= EmitFlags.AsyncFunctionBody;

        var statements = new List<IStatement>();
        statements.Add(_factory.CreateVariableStatement(/*modifiers*/ null, initializer));
        CopyOutParameters(currentState.LoopOutParameters, LoopOutParameterFlags.Initializer, CopyDirection.ToOutParameter, statements);

        // This transforms the following ES2015 syntax:
        //
        //  for (let i = (setImmediate(() => console.log(i)), 0); i < 2; i++) {
        //      // loop body
        //  }
        //
        // Into the following ES5 syntax:
        //
        //  var _loop_init_1 = function () {
        //      var i = (setImmediate(() => console.log(i)), 0);
        //      out_i_1 = i;
        //  };
        //  var out_i_1;
        //  _loop_init_1();
        //  for (var i = out_i_1; i < 2; i++) {
        //      // loop body
        //  }
        //
        // Which prevents mutations to `i` in the per-iteration environment of the body
        // from affecting the initial value for `i` outside of the per-iteration environment.

        var functionDeclaration = _factory.CreateVariableStatement(
            /*modifiers*/ null,
            EmitNodes.SetEmitFlags(
                _factory.CreateVariableDeclarationList(
                    [
                        _factory.CreateVariableDeclaration(
                            functionName,
                            /*exclamationToken*/ null,
                            /*type*/ null,
                            EmitNodes.SetEmitFlags(
                                _factory.CreateFunctionExpression(
                                    /*modifiers*/ null,
                                    containsYield ? (PunctuationToken)_factory.CreateToken(SyntaxKind.AsteriskToken) : null,
                                    /*name*/ null,
                                    /*typeParameters*/ null,
                                    /*parameters*/ null,
                                    /*type*/ null,
                                    Debug.CheckDefined(Utilities.VisitNode(
                                        _factory.CreateBlock(statements, /*multiLine*/ true),
                                        Visitor,
                                        NodeTests.IsBlock))),
                                emitFlags)),
                    ],
                    null),
                EmitFlags.NoHoisting));

        var part = _factory.CreateVariableDeclarationList(Core.Map(currentState.LoopOutParameters, (p, _) => CreateOutVariable(p)), null);
        return new IterationStatementPartFunction<VariableDeclarationList>
        {
            FunctionName = functionName,
            ContainsYield = containsYield,
            FunctionDeclaration = functionDeclaration,
            Part = part,
        };
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:3777-3911 — <c>createFunctionForBodyOfIterationStatement</c>.</summary>
    private IterationStatementPartFunction<List<IStatement>> CreateFunctionForBodyOfIterationStatement(
        IterationStatement node,
        ConvertedLoopState currentState,
        ConvertedLoopState? outerState)
    {
        var functionName = _factory.CreateUniqueName("_loop", /*flags*/ null);
        _context.StartLexicalEnvironment();
        var statement = Utilities.VisitNode(node.Statement, Visitor, Utilities.IsStatement, _liftToBlock);
        var lexicalEnvironment = _context.EndLexicalEnvironment();

        var statements = new List<IStatement>();
        if (ShouldConvertConditionOfForStatement(node) || ShouldConvertIncrementorOfForStatement(node))
        {
            // If a block-scoped variable declared in the initializer of `node` is captured in
            // the condition or incrementor, we must move the condition and incrementor into
            // the body of the for loop.
            //
            // This transforms the following ES2015 syntax:
            //
            //  for (let i = 0; setImmediate(() => console.log(i)), i < 2; setImmediate(() => console.log(i)), i++) {
            //      // loop body
            //  }
            //
            // Into the following ES5 syntax:
            //
            //  var _loop_1 = function (i) {
            //      if (inc_1)
            //          setImmediate(() => console.log(i)), i++;
            //      else
            //          inc_1 = true;
            //      if (!(setImmediate(() => console.log(i)), i < 2))
            //          return out_i_1 = i, "break";
            //      // loop body
            //      out_i_1 = i;
            //  }
            //  var out_i_1, inc_1 = false;
            //  for (var i = 0;;) {
            //      var state_1 = _loop_1(i);
            //      i = out_i_1;
            //      if (state_1 === "break")
            //          break;
            //  }
            //
            // Which prevents mutations to `i` in the per-iteration environment of the body
            // from affecting the value of `i` in the previous per-iteration environment.
            //
            // Note that the incrementor of a `for` loop is evaluated in a *new* per-iteration
            // environment that is carried over to the next iteration of the loop. As a result,
            // we must indicate whether this is the first evaluation of the loop body so that
            // we only evaluate the incrementor on subsequent evaluations.

            currentState.ConditionVariable = _factory.CreateUniqueName("inc", /*flags*/ null);
            if (((ForStatement)node).Incrementor is not null)
            {
                statements.Add(_factory.CreateIfStatement(
                    currentState.ConditionVariable,
                    _factory.CreateExpressionStatement(Debug.CheckDefined(Utilities.VisitNode(((ForStatement)node).Incrementor, Visitor, Utilities.IsExpression))),
                    _factory.CreateExpressionStatement(_factory.CreateAssignment(currentState.ConditionVariable, _factory.CreateTrue()))));
            }
            else
            {
                statements.Add(_factory.CreateIfStatement(
                    _factory.CreateLogicalNot(currentState.ConditionVariable),
                    _factory.CreateExpressionStatement(_factory.CreateAssignment(currentState.ConditionVariable, _factory.CreateTrue())),
                    /*elseStatement*/ null));
            }

            if (ShouldConvertConditionOfForStatement(node))
            {
                statements.Add(_factory.CreateIfStatement(
                    _factory.CreatePrefixUnaryExpression(
                        SyntaxKind.ExclamationToken,
                        Debug.CheckDefined(Utilities.VisitNode(((ForStatement)node).Condition, Visitor, Utilities.IsExpression))),
                    Debug.CheckDefined(Utilities.VisitNode(_factory.CreateBreakStatement(null), Visitor, Utilities.IsStatement)),
                    /*elseStatement*/ null));
            }
        }

        Debug.Assert(statement is not null);
        if (NodeTests.IsBlock((Node)statement!))
        {
            Core.AddRange(statements, ((Block)statement!).Statements);
        }
        else
        {
            statements.Add(statement!);
        }

        CopyOutParameters(currentState.LoopOutParameters, LoopOutParameterFlags.Body, CopyDirection.ToOutParameter, statements);
        TypeScriptTransformSupport.InsertStatementsAfterStandardPrologue(statements, lexicalEnvironment);

        var loopBody = _factory.CreateBlock(statements, /*multiLine*/ true);
        if (NodeTests.IsBlock((Node)statement!)) NodeFactory.SetOriginalNode(loopBody, (Node)statement);

        var containsYield = (node.Statement!.TransformFlags & TransformFlags.ContainsYield) != 0;

        var emitFlags = EmitFlags.ReuseTempVariableScope;
        if (currentState.ContainsLexicalThis) emitFlags |= EmitFlags.CapturesThis;
        if (containsYield && (_hierarchyFacts & HierarchyFacts.AsyncFunctionBody) != 0) emitFlags |= EmitFlags.AsyncFunctionBody;

        // This transforms the following ES2015 syntax (in addition to other variations):
        //
        //  for (let i = 0; i < 2; i++) {
        //      setImmediate(() => console.log(i));
        //  }
        //
        // Into the following ES5 syntax:
        //
        //  var _loop_1 = function (i) {
        //      setImmediate(() => console.log(i));
        //  };
        //  for (var i = 0; i < 2; i++) {
        //      _loop_1(i);
        //  }

        var functionDeclaration = _factory.CreateVariableStatement(
            /*modifiers*/ null,
            EmitNodes.SetEmitFlags(
                _factory.CreateVariableDeclarationList(
                    [
                        _factory.CreateVariableDeclaration(
                            functionName,
                            /*exclamationToken*/ null,
                            /*type*/ null,
                            EmitNodes.SetEmitFlags(
                                _factory.CreateFunctionExpression(
                                    /*modifiers*/ null,
                                    containsYield ? (PunctuationToken)_factory.CreateToken(SyntaxKind.AsteriskToken) : null,
                                    /*name*/ null,
                                    /*typeParameters*/ null,
                                    currentState.LoopParameters,
                                    /*type*/ null,
                                    loopBody),
                                emitFlags)),
                    ],
                    null),
                EmitFlags.NoHoisting));

        var part = GenerateCallToConvertedLoop(functionName, currentState, outerState, containsYield);
        return new IterationStatementPartFunction<List<IStatement>>
        {
            FunctionName = functionName,
            ContainsYield = containsYield,
            FunctionDeclaration = functionDeclaration,
            Part = part,
        };
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:3913-3917 — <c>copyOutParameter</c>.</summary>
    private BinaryExpression CopyOutParameter(LoopOutParameter outParam, CopyDirection copyDirection)
    {
        var source = copyDirection == CopyDirection.ToOriginal ? outParam.OutParamName : outParam.OriginalName;
        var target = copyDirection == CopyDirection.ToOriginal ? outParam.OriginalName : outParam.OutParamName;
        return _factory.CreateBinaryExpression(target, SyntaxKind.EqualsToken, source);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:3919-3925 — <c>copyOutParameters</c>.</summary>
    private void CopyOutParameters(
        List<LoopOutParameter> outParams,
        LoopOutParameterFlags partFlags,
        CopyDirection copyDirection,
        List<IStatement> statements)
    {
        foreach (var outParam in outParams)
        {
            if ((outParam.Flags & partFlags) != 0)
            {
                statements.Add(_factory.CreateExpressionStatement(CopyOutParameter(outParam, copyDirection)));
            }
        }
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:3927-3936 — <c>generateCallToConvertedLoopInitializer</c>.</summary>
    private IStatement GenerateCallToConvertedLoopInitializer(Identifier initFunctionExpressionName, bool containsYield)
    {
        var call = _factory.CreateCallExpression(initFunctionExpressionName, /*typeArguments*/ null, []);
        var callResult = containsYield
            ? _factory.CreateYieldExpression(
                (PunctuationToken)_factory.CreateToken(SyntaxKind.AsteriskToken),
                EmitNodes.SetEmitFlags(call, EmitFlags.Iterator))
            : (Expression)call;
        return _factory.CreateExpressionStatement(callResult);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:3938-4011 — <c>generateCallToConvertedLoop</c>.</summary>
    private List<IStatement> GenerateCallToConvertedLoop(
        Identifier loopFunctionExpressionName,
        ConvertedLoopState state,
        ConvertedLoopState? outerState,
        bool containsYield)
    {
        var statements = new List<IStatement>();
        // loop is considered simple if it does not have any return statements or break\continue that transfer control outside of the loop
        // simple loops are emitted as just 'loop()';
        // NOTE: if loop uses only 'continue' it still will be emitted as simple loop
        var isSimpleLoop = (state.NonLocalJumps & ~Jump.Continue) == 0
            && state.LabeledNonLocalBreaks is null
            && state.LabeledNonLocalContinues is null;

        var call = _factory.CreateCallExpression(
            loopFunctionExpressionName,
            /*typeArguments*/ null,
            Core.Map(state.LoopParameters, (p, _) => (Expression)p.Name!));
        var callResult = containsYield
            ? _factory.CreateYieldExpression(
                (PunctuationToken)_factory.CreateToken(SyntaxKind.AsteriskToken),
                EmitNodes.SetEmitFlags(call, EmitFlags.Iterator))
            : (Expression)call;
        if (isSimpleLoop)
        {
            statements.Add(_factory.CreateExpressionStatement(callResult));
            CopyOutParameters(state.LoopOutParameters, LoopOutParameterFlags.Body, CopyDirection.ToOriginal, statements);
        }
        else
        {
            var loopResultName = _factory.CreateUniqueName("state", /*flags*/ null);
            var stateVariable = _factory.CreateVariableStatement(
                /*modifiers*/ null,
                _factory.CreateVariableDeclarationList(
                    [_factory.CreateVariableDeclaration(loopResultName, /*exclamationToken*/ null, /*type*/ null, callResult)],
                    null));
            statements.Add(stateVariable);
            CopyOutParameters(state.LoopOutParameters, LoopOutParameterFlags.Body, CopyDirection.ToOriginal, statements);

            if ((state.NonLocalJumps & Jump.Return) != 0)
            {
                IStatement returnStatement;
                if (outerState is not null)
                {
                    outerState.NonLocalJumps |= Jump.Return;
                    returnStatement = _factory.CreateReturnStatement(loopResultName);
                }
                else
                {
                    returnStatement = _factory.CreateReturnStatement(_factory.CreatePropertyAccessExpression(loopResultName, "value"));
                }
                statements.Add(
                    _factory.CreateIfStatement(
                        _factory.CreateTypeCheck(loopResultName, "object"),
                        returnStatement,
                        /*elseStatement*/ null));
            }

            if ((state.NonLocalJumps & Jump.Break) != 0)
            {
                statements.Add(
                    _factory.CreateIfStatement(
                        _factory.CreateStrictEquality(
                            loopResultName,
                            _factory.CreateStringLiteral("break", null, null)),
                        _factory.CreateBreakStatement(null),
                        /*elseStatement*/ null));
            }

            if (state.LabeledNonLocalBreaks is not null || state.LabeledNonLocalContinues is not null)
            {
                var caseClauses = new List<Node>();
                ProcessLabeledJumps(state.LabeledNonLocalBreaks!, /*isBreak*/ true, loopResultName, outerState, caseClauses);
                ProcessLabeledJumps(state.LabeledNonLocalContinues!, /*isBreak*/ false, loopResultName, outerState, caseClauses);
                statements.Add(
                    _factory.CreateSwitchStatement(
                        loopResultName,
                        _factory.CreateCaseBlock(caseClauses)));
            }
        }
        return statements;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4013-4026 — <c>setLabeledJump</c>.</summary>
    private static void SetLabeledJump(ConvertedLoopState state, bool isBreak, string labelText, string labelMarker)
    {
        if (isBreak)
        {
            state.LabeledNonLocalBreaks ??= new Dictionary<string, string>();
            state.LabeledNonLocalBreaks[labelText] = labelMarker;
        }
        else
        {
            state.LabeledNonLocalContinues ??= new Dictionary<string, string>();
            state.LabeledNonLocalContinues[labelText] = labelMarker;
        }
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4028-4047 — <c>processLabeledJumps</c>.</summary>
    private void ProcessLabeledJumps(
        Dictionary<string, string> table,
        bool isBreak,
        Identifier loopResultName,
        ConvertedLoopState? outerLoop,
        List<Node> caseClauses)
    {
        foreach (var pair in table)
        {
            var labelText = pair.Key;
            var labelMarker = pair.Value;
            var statements = new List<IStatement>();
            // if there are no outer converted loop or outer label in question is located inside outer converted loop
            // then emit labeled break\continue
            // otherwise propagate pair 'label -> marker' to outer converted loop and emit 'return labelMarker' so outer loop can later decide what to do
            if (outerLoop is null || (outerLoop.Labels is not null && outerLoop.Labels.ContainsKey(labelText)))
            {
                var label = _factory.CreateIdentifier(labelText, null, null);
                statements.Add(isBreak ? _factory.CreateBreakStatement(label) : _factory.CreateContinueStatement(label));
            }
            else
            {
                SetLabeledJump(outerLoop, isBreak, labelText, labelMarker);
                statements.Add(_factory.CreateReturnStatement(loopResultName));
            }
            caseClauses.Add(_factory.CreateCaseClause(_factory.CreateStringLiteral(labelMarker, null, null), statements));
        }
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4049-4090 — <c>processLoopVariableDeclaration</c>.</summary>
    private void ProcessLoopVariableDeclaration(
        IterationStatement container,
        Node decl,
        List<ParameterDeclaration> loopParameters,
        List<LoopOutParameter> loopOutParameters,
        bool hasCapturedBindingsInForHead)
    {
        var name = ((INamedDeclaration)decl).Name!;
        if (Utilities.IsBindingPattern(name))
        {
            foreach (var element in FactoryUtilities.GetElementsOfBindingOrAssignmentPattern(name))
            {
                if (!NodeTests.IsOmittedExpression(element))
                {
                    ProcessLoopVariableDeclaration(container, element, loopParameters, loopOutParameters, hasCapturedBindingsInForHead);
                }
            }
        }
        else
        {
            loopParameters.Add(_factory.CreateParameterDeclaration(/*modifiers*/ null, /*dotDotDotToken*/ null, name, /*questionToken*/ null, /*type*/ null, /*initializer*/ null));
            var needsOutParam = _resolver.HasNodeCheckFlag(decl, NodeCheckFlags.NeedsLoopOutParameter);
            if (needsOutParam || hasCapturedBindingsInForHead)
            {
                var outParamName = _factory.CreateUniqueName("out_" + Utilities.IdText(name), /*flags*/ null);
                var flags = LoopOutParameterFlags.None;
                if (needsOutParam)
                {
                    flags |= LoopOutParameterFlags.Body;
                }
                if (NodeTests.IsForStatement(container))
                {
                    var forStatement = (ForStatement)container;
                    if (forStatement.Initializer is not null && _resolver.IsBindingCapturedByNode(forStatement.Initializer, decl))
                    {
                        flags |= LoopOutParameterFlags.Initializer;
                    }
                    if (forStatement.Condition is not null && _resolver.IsBindingCapturedByNode(forStatement.Condition, decl)
                        || forStatement.Incrementor is not null && _resolver.IsBindingCapturedByNode(forStatement.Incrementor, decl))
                    {
                        flags |= LoopOutParameterFlags.Body;
                    }
                }
                loopOutParameters.Add(new LoopOutParameter { Flags = flags, OriginalName = (Identifier)name, OutParamName = outParamName });
            }
        }
    }
}
