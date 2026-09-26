#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/es2015.ts:1846-2253, :2416-2660, :2727-2934 — the parameter/default/rest
// machinery, the function-like transforms and the variable-statement/declaration-list visitors.
/// <summary>
/// The function half of the ES2015 downlevel transform.
/// </summary>
/// <remarks>
/// PORT-NOTE (<c>FunctionLikeDeclaration</c>): the reference's `node: FunctionLikeDeclaration` is the union of every
/// function-like node; the port takes <see cref="Node"/> and reads the shape through
/// <see cref="IFunctionLikeDeclarationBase"/>, casting to <see cref="ArrowFunction"/> where the reference reads
/// <c>equalsGreaterThanToken</c> (a member only that class has).
/// </remarks>
internal sealed partial class ES2015Transformer
{
    /// <summary>ported from: src/compiler/transformers/es2015.ts:1846-1899 — <c>visitParameter</c>.</summary>
    private ParameterDeclaration? VisitParameter(ParameterDeclaration node)
    {
        if (node.DotDotDotToken is not null)
        {
            // rest parameters are elided
            return null;
        }
        else if (Utilities.IsBindingPattern(node.Name))
        {
            // Binding patterns are converted into a generated name and are
            // evaluated inside the function body.
            return NodeFactory.SetOriginalNode(
                Utilities.SetTextRange(
                    _factory.CreateParameterDeclaration(
                        /*modifiers*/ null,
                        /*dotDotDotToken*/ null,
                        _factory.GetGeneratedNameForNode(node, /*flags*/ null),
                        /*questionToken*/ null,
                        /*type*/ null,
                        /*initializer*/ null),
                    /*location*/ node),
                /*original*/ node);
        }
        else if (node.Initializer is not null)
        {
            // Initializers are elided
            return NodeFactory.SetOriginalNode(
                Utilities.SetTextRange(
                    _factory.CreateParameterDeclaration(
                        /*modifiers*/ null,
                        /*dotDotDotToken*/ null,
                        node.Name,
                        /*questionToken*/ null,
                        /*type*/ null,
                        /*initializer*/ null),
                    /*location*/ node),
                /*original*/ node);
        }
        else
        {
            return node;
        }
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1901-1905 — <c>hasDefaultValueOrBindingPattern</c>.</summary>
    private static bool HasDefaultValueOrBindingPattern(ParameterDeclaration node)
        => node.Initializer is not null || Utilities.IsBindingPattern(node.Name);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1913-1935 — <c>addDefaultValueAssignmentsIfNeeded</c>.</summary>
    private bool AddDefaultValueAssignmentsIfNeeded(List<IStatement> statements, Node node)
    {
        var parameters = ((IFunctionLikeDeclarationBase)node).Parameters;
        if (!Core.Some(parameters, HasDefaultValueOrBindingPattern))
        {
            return false;
        }

        var added = false;
        foreach (var parameter in parameters!)
        {
            var name = parameter.Name;
            var initializer = parameter.Initializer;
            var dotDotDotToken = parameter.DotDotDotToken;

            // A rest parameter cannot have a binding pattern or an initializer,
            // so let's just ignore it.
            if (dotDotDotToken is not null)
            {
                continue;
            }

            if (Utilities.IsBindingPattern(name))
            {
                added = InsertDefaultValueAssignmentForBindingPattern(statements, parameter, name!, initializer) || added;
            }
            else if (initializer is not null)
            {
                InsertDefaultValueAssignmentForInitializer(statements, parameter, (Identifier)name!, initializer);
                added = true;
            }
        }
        return added;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1937-1986 — <c>insertDefaultValueAssignmentForBindingPattern</c>.</summary>
    private bool InsertDefaultValueAssignmentForBindingPattern(
        List<IStatement> statements,
        ParameterDeclaration parameter,
        Node name,
        Expression? initializer)
    {
        // In cases where a binding pattern is simply '[]' or '{}',
        // we usually don't want to emit a var declaration; however, in the presence
        // of an initializer, we must emit that expression to preserve side effects.
        if (FactoryUtilities.GetElementsOfBindingOrAssignmentPattern(name).Count > 0)
        {
            ES2015TransformSupport.InsertStatementAfterCustomPrologue(
                statements,
                EmitNodes.SetEmitFlags(
                    _factory.CreateVariableStatement(
                        /*modifiers*/ null,
                        _factory.CreateVariableDeclarationList(
                            Destructuring.FlattenDestructuringBinding(
                                parameter,
                                Visitor,
                                _context,
                                FlattenLevel.All,
                                _factory.GetGeneratedNameForNode(parameter, /*flags*/ null)),
                            null)),
                    EmitFlags.CustomPrologue));
            return true;
        }
        else if (initializer is not null)
        {
            ES2015TransformSupport.InsertStatementAfterCustomPrologue(
                statements,
                EmitNodes.SetEmitFlags(
                    _factory.CreateExpressionStatement(
                        _factory.CreateAssignment(
                            _factory.GetGeneratedNameForNode(parameter, /*flags*/ null),
                            Debug.CheckDefined(Utilities.VisitNode(initializer, Visitor, Utilities.IsExpression)))),
                    EmitFlags.CustomPrologue));
            return true;
        }
        return false;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1988-2027 — <c>insertDefaultValueAssignmentForInitializer</c>.</summary>
    private void InsertDefaultValueAssignmentForInitializer(
        List<IStatement> statements,
        ParameterDeclaration parameter,
        Identifier name,
        Expression initializer)
    {
        initializer = Debug.CheckDefined(Utilities.VisitNode(initializer, Visitor, Utilities.IsExpression));
        var statement = _factory.CreateIfStatement(
            _factory.CreateTypeCheck(_factory.CloneNode(name), "undefined"),
            EmitNodes.SetEmitFlags(
                Utilities.SetTextRange(
                    _factory.CreateBlock(
                        [
                            _factory.CreateExpressionStatement(
                                EmitNodes.SetEmitFlags(
                                    Utilities.SetTextRange(
                                        _factory.CreateAssignment(
                                            // TODO(rbuckton): Does this need to be parented?
                                            EmitNodes.SetEmitFlags(
                                                Utilities.SetParent(Utilities.SetTextRange(_factory.CloneNode(name), name), name.Parent)!,
                                                EmitFlags.NoSourceMap),
                                            EmitNodes.SetEmitFlags(initializer, EmitFlags.NoSourceMap | Utilities.GetEmitFlags(initializer) | EmitFlags.NoComments)),
                                        parameter),
                                    EmitFlags.NoComments)),
                        ],
                        /*multiLine*/ null),
                    parameter),
                EmitFlags.SingleLine | EmitFlags.NoTrailingSourceMap | EmitFlags.NoTokenSourceMaps | EmitFlags.NoComments),
            /*elseStatement*/ null);

        FactoryUtilities.StartOnNewLine(statement);
        Utilities.SetTextRange(statement, parameter);
        EmitNodes.SetEmitFlags(statement, EmitFlags.NoTokenSourceMaps | EmitFlags.NoTrailingSourceMap | EmitFlags.CustomPrologue | EmitFlags.NoComments);
        ES2015TransformSupport.InsertStatementAfterCustomPrologue(statements, statement);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2029-2040 — <c>shouldAddRestParameter</c>.</summary>
    private static bool ShouldAddRestParameter(ParameterDeclaration? node, bool inConstructorWithSynthesizedSuper)
        => node is not null && node.DotDotDotToken is not null && !inConstructorWithSynthesizedSuper;

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2042-2149 — <c>addRestParameterIfNeeded</c>.</summary>
    private bool AddRestParameterIfNeeded(List<IStatement> statements, Node node, bool inConstructorWithSynthesizedSuper)
    {
        var parameters = ((IFunctionLikeDeclarationBase)node).Parameters;
        var prologueStatements = new List<IStatement>();
        var parameter = Core.LastOrUndefined(parameters);
        if (!ShouldAddRestParameter(parameter, inConstructorWithSynthesizedSuper))
        {
            return false;
        }

        // `declarationName` is the name of the local declaration for the parameter.
        // TODO(rbuckton): Does this need to be parented?
        var declarationName = NodeTests.IsIdentifier(parameter!.Name!)
            ? Utilities.SetParent(Utilities.SetTextRange(_factory.CloneNode((Identifier)parameter.Name!), parameter.Name), parameter.Name!.Parent)
            : _factory.CreateTempVariable(/*recordTempVariable*/ null, null);
        EmitNodes.SetEmitFlags(declarationName!, EmitFlags.NoSourceMap);

        // `expressionName` is the name of the parameter used in expressions.
        var expressionName = NodeTests.IsIdentifier(parameter.Name!)
            ? _factory.CloneNode((Identifier)parameter.Name!)
            : declarationName;
        var restIndex = parameters!.Count - 1;
        var temp = _factory.CreateLoopVariable(/*reservedInNestedScopes*/ null);

        // var param = [];
        prologueStatements.Add(
            EmitNodes.SetEmitFlags(
                Utilities.SetTextRange(
                    _factory.CreateVariableStatement(
                        /*modifiers*/ null,
                        _factory.CreateVariableDeclarationList(
                            [
                                _factory.CreateVariableDeclaration(
                                    declarationName,
                                    /*exclamationToken*/ null,
                                    /*type*/ null,
                                    _factory.CreateArrayLiteralExpression([], null)),
                            ],
                            null)),
                    /*location*/ parameter),
                EmitFlags.CustomPrologue));

        // for (var _i = restIndex; _i < arguments.length; _i++) {
        //   param[_i - restIndex] = arguments[_i];
        // }
        var forStatement = _factory.CreateForStatement(
            Utilities.SetTextRange(
                _factory.CreateVariableDeclarationList(
                    [
                        _factory.CreateVariableDeclaration(temp, /*exclamationToken*/ null, /*type*/ null, _factory.CreateNumericLiteral(restIndex)),
                    ],
                    null),
                parameter),
            Utilities.SetTextRange(
                _factory.CreateLessThan(
                    temp,
                    _factory.CreatePropertyAccessExpression(_factory.CreateIdentifier("arguments", null, null), "length")),
                parameter),
            Utilities.SetTextRange(_factory.CreatePostfixIncrement(temp), parameter),
            _factory.CreateBlock(
                [
                    FactoryUtilities.StartOnNewLine(
                        Utilities.SetTextRange(
                            _factory.CreateExpressionStatement(
                                _factory.CreateAssignment(
                                    _factory.CreateElementAccessExpression(
                                        expressionName,
                                        restIndex == 0
                                            ? temp
                                            : _factory.CreateSubtract(temp, _factory.CreateNumericLiteral(restIndex))),
                                    _factory.CreateElementAccessExpression(_factory.CreateIdentifier("arguments", null, null), temp))),
                            /*location*/ parameter)),
                ],
                null));

        EmitNodes.SetEmitFlags(forStatement, EmitFlags.CustomPrologue);
        FactoryUtilities.StartOnNewLine(forStatement);
        prologueStatements.Add(forStatement);

        if (!NodeTests.IsIdentifier(parameter.Name!))
        {
            // do the actual destructuring of the rest parameter if necessary
            prologueStatements.Add(
                EmitNodes.SetEmitFlags(
                    Utilities.SetTextRange(
                        _factory.CreateVariableStatement(
                            /*modifiers*/ null,
                            _factory.CreateVariableDeclarationList(
                                Destructuring.FlattenDestructuringBinding(parameter, Visitor, _context, FlattenLevel.All, expressionName),
                                null)),
                        parameter),
                    EmitFlags.CustomPrologue));
        }

        ES2015TransformSupport.InsertStatementsAfterCustomPrologue(statements, prologueStatements);
        return true;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2151-2157 — <c>insertCaptureThisForNodeIfNeeded</c>.</summary>
    private bool InsertCaptureThisForNodeIfNeeded(List<IStatement> statements, Node node)
    {
        if ((_hierarchyFacts & HierarchyFacts.CapturedLexicalThis) != 0 && node.Kind != SyntaxKind.ArrowFunction)
        {
            InsertCaptureThisForNode(statements, node, _factory.CreateThis());
            return true;
        }
        return false;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2159-2175 — <c>insertCaptureThisForNode</c>.</summary>
    private void InsertCaptureThisForNode(List<IStatement> statements, Node node, Expression? initializer)
    {
        EnableSubstitutionsForCapturedThis();
        var captureThisStatement = _factory.CreateVariableStatement(
            /*modifiers*/ null,
            _factory.CreateVariableDeclarationList(
                [
                    _factory.CreateVariableDeclaration(
                        CreateCapturedThis(),
                        /*exclamationToken*/ null,
                        /*type*/ null,
                        initializer),
                ],
                null));
        EmitNodes.SetEmitFlags(captureThisStatement, EmitFlags.NoComments | EmitFlags.CustomPrologue);
        EmitNodes.SetSourceMapRange(captureThisStatement, TypeScriptTransformSupport.SourceMapRangeOf(node));
        ES2015TransformSupport.InsertStatementAfterCustomPrologue(statements, captureThisStatement);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2177-2252 — <c>insertCaptureNewTargetIfNeeded</c>.</summary>
    private List<IStatement> InsertCaptureNewTargetIfNeeded(List<IStatement> statements, Node node)
    {
        if ((_hierarchyFacts & HierarchyFacts.NewTarget) != 0)
        {
            Expression newTarget;
            switch (node.Kind)
            {
                case SyntaxKind.ArrowFunction:
                    return statements;

                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                    // Methods and accessors cannot be constructors, so 'new.target' will
                    // always return 'undefined'.
                    newTarget = _factory.CreateVoidZero();
                    break;

                case SyntaxKind.Constructor:
                    // Class constructors can only be called with `new`, so `this.constructor`
                    // should be relatively safe to use.
                    newTarget = _factory.CreatePropertyAccessExpression(
                        EmitNodes.SetEmitFlags(_factory.CreateThis(), EmitFlags.NoSubstitution),
                        "constructor");
                    break;

                case SyntaxKind.FunctionDeclaration:
                case SyntaxKind.FunctionExpression:
                    // Functions can be called or constructed, and may have a `this` due to
                    // being a member or when calling an imported function via `other_1.f()`.
                    newTarget = _factory.CreateConditionalExpression(
                        _factory.CreateLogicalAnd(
                            EmitNodes.SetEmitFlags(_factory.CreateThis(), EmitFlags.NoSubstitution),
                            _factory.CreateBinaryExpression(
                                EmitNodes.SetEmitFlags(_factory.CreateThis(), EmitFlags.NoSubstitution),
                                SyntaxKind.InstanceOfKeyword,
                                TypeScriptTransformSupport.GetLocalName(_factory, node, /*allowComments*/ null, /*allowSourceMaps*/ null))),
                        /*questionToken*/ null,
                        _factory.CreatePropertyAccessExpression(
                            EmitNodes.SetEmitFlags(_factory.CreateThis(), EmitFlags.NoSubstitution),
                            "constructor"),
                        /*colonToken*/ null,
                        _factory.CreateVoidZero());
                    break;

                default:
                    return Debug.Fail<List<IStatement>>($"Unexpected node.\r\nNode {Debug.FormatSyntaxKind(node.Kind)} was unexpected.");
            }

            var captureNewTargetStatement = _factory.CreateVariableStatement(
                /*modifiers*/ null,
                _factory.CreateVariableDeclarationList(
                    [
                        _factory.CreateVariableDeclaration(
                            _factory.CreateUniqueName("_newTarget", GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel),
                            /*exclamationToken*/ null,
                            /*type*/ null,
                            newTarget),
                    ],
                    null));

            EmitNodes.SetEmitFlags(captureNewTargetStatement, EmitFlags.NoComments | EmitFlags.CustomPrologue);
            ES2015TransformSupport.InsertStatementAfterCustomPrologue(statements, captureNewTargetStatement);
        }

        return statements;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2416-2447 — <c>visitArrowFunction</c>.</summary>
    private FunctionExpression VisitArrowFunction(ArrowFunction node)
    {
        if ((node.TransformFlags & TransformFlags.ContainsLexicalThis) != 0 && (_hierarchyFacts & HierarchyFacts.StaticInitializer) == 0)
        {
            _hierarchyFacts |= HierarchyFacts.CapturedLexicalThis;
        }

        var savedConvertedLoopState = _convertedLoopState;
        _convertedLoopState = null;
        var ancestorFacts = EnterSubtree(HierarchyFacts.ArrowFunctionExcludes, HierarchyFacts.ArrowFunctionIncludes);
        var func = _factory.CreateFunctionExpression(
            /*modifiers*/ null,
            /*asteriskToken*/ null,
            /*name*/ null,
            /*typeParameters*/ null,
            Utilities.VisitParameterList(node.Parameters, Visitor, _context),
            /*type*/ null,
            TransformFunctionBody(node));
        Utilities.SetTextRange(func, node);
        NodeFactory.SetOriginalNode(func, node);
        EmitNodes.SetEmitFlags(func, EmitFlags.CapturesThis);

        // If an arrow function contains
        ExitSubtree(ancestorFacts, HierarchyFacts.ArrowFunctionSubtreeExcludes, HierarchyFacts.None);

        _convertedLoopState = savedConvertedLoopState;
        return func;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2449-2479 — <c>visitFunctionExpression</c>.</summary>
    private Expression VisitFunctionExpression(FunctionExpression node)
    {
        var ancestorFacts = (Utilities.GetEmitFlags(node) & EmitFlags.AsyncFunctionBody) != 0
            ? EnterSubtree(HierarchyFacts.AsyncFunctionBodyExcludes, HierarchyFacts.AsyncFunctionBodyIncludes)
            : EnterSubtree(HierarchyFacts.FunctionExcludes, HierarchyFacts.FunctionIncludes);
        var savedConvertedLoopState = _convertedLoopState;
        _convertedLoopState = null;

        var parameters = Utilities.VisitParameterList(node.Parameters, Visitor, _context);
        var body = TransformFunctionBody(node);
        var name = (_hierarchyFacts & HierarchyFacts.NewTarget) != 0
            ? TypeScriptTransformSupport.GetLocalName(_factory, node, /*allowComments*/ null, /*allowSourceMaps*/ null)
            : node.Name;

        ExitSubtree(ancestorFacts, HierarchyFacts.FunctionSubtreeExcludes, HierarchyFacts.None);
        _convertedLoopState = savedConvertedLoopState;
        return _factory.UpdateFunctionExpression(
            node,
            /*modifiers*/ null,
            node.AsteriskToken,
            name,
            /*typeParameters*/ null,
            parameters,
            /*type*/ null,
            body);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2481-2510 — <c>visitFunctionDeclaration</c>.</summary>
    private FunctionDeclaration VisitFunctionDeclaration(FunctionDeclaration node)
    {
        var savedConvertedLoopState = _convertedLoopState;
        _convertedLoopState = null;
        var ancestorFacts = EnterSubtree(HierarchyFacts.FunctionExcludes, HierarchyFacts.FunctionIncludes);
        var parameters = Utilities.VisitParameterList(node.Parameters, Visitor, _context);
        var body = TransformFunctionBody(node);
        var name = (_hierarchyFacts & HierarchyFacts.NewTarget) != 0
            ? TypeScriptTransformSupport.GetLocalName(_factory, node, /*allowComments*/ null, /*allowSourceMaps*/ null)
            : node.Name;

        ExitSubtree(ancestorFacts, HierarchyFacts.FunctionSubtreeExcludes, HierarchyFacts.None);
        _convertedLoopState = savedConvertedLoopState;
        return _factory.UpdateFunctionDeclaration(
            node,
            Utilities.VisitNodes(node.Modifiers, Visitor, Utilities.IsModifier),
            node.AsteriskToken,
            name,
            /*typeParameters*/ null,
            parameters,
            /*type*/ null,
            body);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2512-2546 — <c>transformFunctionLikeToExpression</c>.</summary>
    private FunctionExpression TransformFunctionLikeToExpression(Node node, ITextRange? location, Identifier? name, Node? container)
    {
        var savedConvertedLoopState = _convertedLoopState;
        _convertedLoopState = null;
        var ancestorFacts = container is not null && Utilities.IsClassLike(container) && !Utilities.IsStatic(node)
            ? EnterSubtree(HierarchyFacts.FunctionExcludes, HierarchyFacts.FunctionIncludes | HierarchyFacts.NonStaticClassElement)
            : EnterSubtree(HierarchyFacts.FunctionExcludes, HierarchyFacts.FunctionIncludes);
        var parameters = Utilities.VisitParameterList(((IFunctionLikeDeclarationBase)node).Parameters, Visitor, _context);
        var body = TransformFunctionBody(node);
        if ((_hierarchyFacts & HierarchyFacts.NewTarget) != 0
            && name is null
            && (node.Kind == SyntaxKind.FunctionDeclaration || node.Kind == SyntaxKind.FunctionExpression))
        {
            name = _factory.GetGeneratedNameForNode(node, /*flags*/ null);
        }

        ExitSubtree(ancestorFacts, HierarchyFacts.FunctionSubtreeExcludes, HierarchyFacts.None);
        _convertedLoopState = savedConvertedLoopState;
        return NodeFactory.SetOriginalNode(
            Utilities.SetTextRange(
                _factory.CreateFunctionExpression(
                    /*modifiers*/ null,
                    ((IFunctionLikeDeclarationBase)node).AsteriskToken,
                    name,
                    /*typeParameters*/ null,
                    parameters,
                    /*type*/ null,
                    body),
                location),
            /*original*/ node);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2548-2641 — <c>transformFunctionBody</c>.</summary>
    /// <remarks>PORT-NOTE: <c>statementsLocation</c> is TS's <c>TextRange</c>; the port states it as
    /// <see cref="ITextRange"/> so both the body's statement array and the synthetic <c>moveRangeEnd</c> range fit.
    /// </remarks>
    private Block TransformFunctionBody(Node node)
    {
        var multiLine = false; // indicates whether the block *must* be emitted as multiple lines
        var singleLine = false; // indicates whether the block *may* be emitted as a single line
        ITextRange? statementsLocation = null;
        ITextRange? closeBraceLocation = null;

        var prologue = new List<IStatement>();
        var statements = new List<IStatement>();
        var body = ((IFunctionLikeDeclarationBase)node).Body!;
        int? statementOffset = null;

        _context.ResumeLexicalEnvironment();
        if (NodeTests.IsBlock(body))
        {
            // ensureUseStrict is false because no new prologue-directive should be added.
            // addStandardPrologue will put already-existing directives at the beginning of the target statement-array
            statementOffset = _factory.CopyStandardPrologue(((Block)body).Statements, prologue, 0, /*ensureUseStrict*/ false);
            statementOffset = _factory.CopyCustomPrologue(((Block)body).Statements, statements, statementOffset.Value, Visitor, s => Utilities.IsHoistedFunction((Node)s));
            statementOffset = _factory.CopyCustomPrologue(((Block)body).Statements, statements, statementOffset.Value, Visitor, s => Utilities.IsHoistedVariableStatement((Node)s));
        }

        multiLine = AddDefaultValueAssignmentsIfNeeded(statements, node) || multiLine;
        multiLine = AddRestParameterIfNeeded(statements, node, /*inConstructorWithSynthesizedSuper*/ false) || multiLine;

        if (NodeTests.IsBlock(body))
        {
            // addCustomPrologue puts already-existing directives at the beginning of the target statement-array
            statementOffset = _factory.CopyCustomPrologue(((Block)body).Statements, statements, statementOffset!.Value, Visitor, null);

            statementsLocation = ((Block)body).Statements;
            Core.AddRange(statements, Utilities.VisitNodes(((Block)body).Statements, Visitor, Utilities.IsStatement, statementOffset));

            // If the original body was a multi-line block, this must be a multi-line block.
            if (!multiLine && ((Block)body).MultiLine == true)
            {
                multiLine = true;
            }
        }
        else
        {
            Debug.Assert(node.Kind == SyntaxKind.ArrowFunction);

            // To align with the old emitter, we use a synthetic end position on the location
            // for the statement list we synthesize when we down-level an arrow function with
            // an expression function body. This prevents both comments and source maps from
            // being emitted for the end position only.
            statementsLocation = ES2015TransformSupport.MoveRangeEnd(body, -1);

            var equalsGreaterThanToken = ((ArrowFunction)node).EqualsGreaterThanToken;
            if (equalsGreaterThanToken is not null && !Utilities.NodeIsSynthesized(equalsGreaterThanToken) && !Utilities.NodeIsSynthesized(body))
            {
                if (ES2015TransformSupport.RangeEndIsOnSameLineAsRangeStart(equalsGreaterThanToken, body, _currentSourceFile!))
                {
                    singleLine = true;
                }
                else
                {
                    multiLine = true;
                }
            }

            var expression = Utilities.VisitNode((Expression)body, Visitor, Utilities.IsExpression);
            var returnStatement = _factory.CreateReturnStatement(expression);
            Utilities.SetTextRange(returnStatement, body);
            EmitNodes.MoveSyntheticComments(returnStatement, body);
            EmitNodes.SetEmitFlags(returnStatement, EmitFlags.NoTokenSourceMaps | EmitFlags.NoTrailingSourceMap | EmitFlags.NoTrailingComments);
            statements.Add(returnStatement);

            // To align with the source map emit for the old emitter, we set a custom
            // source map location for the close brace.
            closeBraceLocation = body;
        }

        _factory.MergeLexicalEnvironment(prologue, _context.EndLexicalEnvironment());
        InsertCaptureNewTargetIfNeeded(prologue, node);
        InsertCaptureThisForNodeIfNeeded(prologue, node);

        // If we added any final generated statements, this must be a multi-line block
        if (Core.Some(prologue))
        {
            multiLine = true;
        }

        statements.InsertRange(0, prologue);
        if (NodeTests.IsBlock(body) && Core.ArrayIsEqualTo(statements, ((Block)body).Statements))
        {
            // no changes were made, preserve the tree
            return (Block)body;
        }

        var block = _factory.CreateBlock(Utilities.SetTextRange(_factory.CreateNodeArray(statements, null), statementsLocation), multiLine);
        Utilities.SetTextRange(block, ((IFunctionLikeDeclarationBase)node).Body!);
        if (!multiLine && singleLine)
        {
            EmitNodes.SetEmitFlags(block, EmitFlags.SingleLine);
        }

        if (closeBraceLocation is not null)
        {
            EmitNodes.SetTokenSourceMapRange(block, SyntaxKind.CloseBraceToken, TypeScriptTransformSupport.SourceMapRangeOf(closeBraceLocation));
        }

        NodeFactory.SetOriginalNode(block, ((IFunctionLikeDeclarationBase)node).Body);
        return block;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2643-2660 — <c>visitBlock</c>.</summary>
    private Block VisitBlock(Block node, bool isFunctionBody)
    {
        if (isFunctionBody)
        {
            // A function body is not a block scope.
            return Utilities.VisitEachChildWorker(node, Visitor, _context);
        }
        var ancestorFacts = (_hierarchyFacts & HierarchyFacts.IterationStatement) != 0
            ? EnterSubtree(HierarchyFacts.IterationStatementBlockExcludes, HierarchyFacts.IterationStatementBlockIncludes)
            : EnterSubtree(HierarchyFacts.BlockExcludes, HierarchyFacts.BlockIncludes);
        var updated = Utilities.VisitEachChildWorker(node, Visitor, _context);
        ExitSubtree(ancestorFacts, HierarchyFacts.None, HierarchyFacts.None);
        return updated;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2661-2670 — <c>visitExpressionStatement</c>.</summary>
    private IStatement VisitExpressionStatement(ExpressionStatement node)
        => Utilities.VisitEachChildWorker(node, VisitorWithUnusedExpressionResult, _context);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2672-2681 — <c>visitParenthesizedExpression</c>.</summary>
    private ParenthesizedExpression VisitParenthesizedExpression(ParenthesizedExpression node, bool expressionResultIsUnused)
        => Utilities.VisitEachChildWorker(node, expressionResultIsUnused ? VisitorWithUnusedExpressionResult : Visitor, _context);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2683-2707 — <c>visitBinaryExpression</c>.</summary>
    private Expression VisitBinaryExpression(BinaryExpression node, bool expressionResultIsUnused)
    {
        // If we are here it is because this is a destructuring assignment.
        if (Utilities.IsDestructuringAssignment(node))
        {
            return Destructuring.FlattenDestructuringAssignment(
                node,
                Visitor,
                _context,
                FlattenLevel.All,
                !expressionResultIsUnused);
        }
        if (node.OperatorToken!.Kind == SyntaxKind.CommaToken)
        {
            return _factory.UpdateBinaryExpression(
                node,
                Debug.CheckDefined(Utilities.VisitNode(node.Left, VisitorWithUnusedExpressionResult, Utilities.IsExpression)),
                node.OperatorToken,
                Debug.CheckDefined(Utilities.VisitNode(node.Right, expressionResultIsUnused ? VisitorWithUnusedExpressionResult : Visitor, Utilities.IsExpression)));
        }
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2709-2725 — <c>visitCommaListExpression</c>.</summary>
    private Expression VisitCommaListExpression(CommaListExpression node, bool expressionResultIsUnused)
    {
        if (expressionResultIsUnused)
        {
            return Utilities.VisitEachChildWorker(node, VisitorWithUnusedExpressionResult, _context);
        }
        List<Expression>? result = null;
        for (var i = 0; i < node.Elements!.Count; i++)
        {
            var element = node.Elements[i];
            var visited = Utilities.VisitNode(element, i < node.Elements.Count - 1 ? VisitorWithUnusedExpressionResult : Visitor, Utilities.IsExpression);
            if (result is not null || !ReferenceEquals(visited, element))
            {
                result ??= node.Elements.Take(i).ToList();
                Debug.Assert(visited is not null);
                result.Add(visited!);
            }
        }
        var elements = result is not null ? Utilities.SetTextRange(_factory.CreateNodeArray(result, null), node.Elements) : node.Elements;
        return _factory.UpdateCommaListExpression(node, elements);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2727-2731 — <c>isVariableStatementOfTypeScriptClassWrapper</c>.</summary>
    private static bool IsVariableStatementOfTypeScriptClassWrapper(VariableStatement node)
        => node.DeclarationList!.Declarations!.Count == 1
            && node.DeclarationList!.Declarations![0].Initializer is not null
            && (ES2015TransformSupport.GetInternalEmitFlags(node.DeclarationList!.Declarations![0].Initializer!) & InternalEmitFlags.TypeScriptClassWrapper) != 0;

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2733-2778 — <c>visitVariableStatement</c>.</summary>
    private object? VisitVariableStatement(VariableStatement node)
    {
        var ancestorFacts = EnterSubtree(
            HierarchyFacts.None,
            Utilities.HasSyntacticModifier(node, ModifierFlags.Export) ? HierarchyFacts.ExportedVariableStatement : HierarchyFacts.None);
        object? updated;
        if (_convertedLoopState is not null
            && (node.DeclarationList!.Flags & NodeFlags.BlockScoped) == 0
            && !IsVariableStatementOfTypeScriptClassWrapper(node))
        {
            // we are inside a converted loop - hoist variable declarations
            List<Expression>? assignments = null;
            foreach (var decl in node.DeclarationList!.Declarations!)
            {
                HoistVariableDeclarationDeclaredInConvertedLoop(_convertedLoopState, decl);
                if (decl.Initializer is not null)
                {
                    Expression assignment;
                    if (Utilities.IsBindingPattern(decl.Name))
                    {
                        assignment = Destructuring.FlattenDestructuringAssignment(
                            decl,
                            Visitor,
                            _context,
                            FlattenLevel.All);
                    }
                    else
                    {
                        assignment = _factory.CreateBinaryExpression(
                            (Expression)decl.Name!,
                            SyntaxKind.EqualsToken,
                            Debug.CheckDefined(Utilities.VisitNode(decl.Initializer, Visitor, Utilities.IsExpression)));
                        Utilities.SetTextRange(assignment, decl);
                    }

                    assignments = (List<Expression>?)Core.Append(assignments, assignment);
                }
            }
            if (assignments is not null)
            {
                updated = Utilities.SetTextRange(_factory.CreateExpressionStatement(_factory.InlineExpressions(assignments)), node);
            }
            else
            {
                // none of declarations has initializer - the entire variable statement can be deleted
                updated = null;
            }
        }
        else
        {
            updated = Utilities.VisitEachChildWorker(node, Visitor, _context);
        }

        ExitSubtree(ancestorFacts, HierarchyFacts.None, HierarchyFacts.None);
        return updated;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2780-2811 — <c>visitVariableDeclarationList</c>.</summary>
    private VariableDeclarationList VisitVariableDeclarationList(VariableDeclarationList node)
    {
        if ((node.Flags & NodeFlags.BlockScoped) != 0 || (node.TransformFlags & TransformFlags.ContainsBindingPattern) != 0)
        {
            if ((node.Flags & NodeFlags.BlockScoped) != 0)
            {
                EnableSubstitutionsForBlockScopedBindings();
            }

            Func<Node, object?> declarationVisitor = (node.Flags & NodeFlags.Let) != 0
                ? n => VisitVariableDeclarationInLetDeclarationList((VariableDeclaration)n)
                : n => VisitVariableDeclaration((VariableDeclaration)n);

            var declarations = Utilities.VisitNodes(
                node.Declarations,
                declarationVisitor,
                NodeTests.IsVariableDeclaration);

            var declarationList = _factory.CreateVariableDeclarationList(declarations, null);
            NodeFactory.SetOriginalNode(declarationList, node);
            Utilities.SetTextRange(declarationList, node);
            EmitNodes.SetCommentRange(declarationList, node);

            // If the first or last declaration is a binding pattern, we need to modify
            // the source map range for the declaration list.
            if ((node.TransformFlags & TransformFlags.ContainsBindingPattern) != 0
                && (Utilities.IsBindingPattern(node.Declarations![0].Name) || Utilities.IsBindingPattern(Core.Last(node.Declarations).Name)))
            {
                EmitNodes.SetSourceMapRange(declarationList, TypeScriptTransformSupport.SourceMapRangeOf(GetRangeUnion(declarations!.Cast<Node>().ToList())));
            }

            return declarationList;
        }
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2813-2828 — <c>getRangeUnion</c>.</summary>
    private static TextRange GetRangeUnion(IList<Node> declarations)
    {
        // declarations may not be sorted by position.
        // pos should be the minimum* position over all nodes (that's not -1), end should be the maximum end over all nodes.
        var pos = -1;
        var end = -1;
        foreach (var node in declarations)
        {
            pos = pos == -1 ? node.Pos : node.Pos == -1 ? pos : Math.Min(pos, node.Pos);
            end = Math.Max(end, node.End);
        }
        return TypeScriptTransformSupport.CreateRange(pos, end);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2830-2891 — <c>shouldEmitExplicitInitializerForLetDeclaration</c>.</summary>
    private bool ShouldEmitExplicitInitializerForLetDeclaration(VariableDeclaration node)
    {
        var isCapturedInFunction = _resolver.HasNodeCheckFlag(node, NodeCheckFlags.CapturedBlockScopedBinding);
        var isDeclaredInLoop = _resolver.HasNodeCheckFlag(node, NodeCheckFlags.BlockScopedBindingInLoop);
        var emittedAsTopLevel = (_hierarchyFacts & HierarchyFacts.TopLevel) != 0
            || (isCapturedInFunction
                && isDeclaredInLoop
                && (_hierarchyFacts & HierarchyFacts.IterationStatementBlock) != 0);

        var emitExplicitInitializer = !emittedAsTopLevel
            && (_hierarchyFacts & HierarchyFacts.ForInOrForOfStatement) == 0
            && (!_resolver.IsDeclarationWithCollidingName(node)
                || (isDeclaredInLoop
                    && !isCapturedInFunction
                    && (_hierarchyFacts & (HierarchyFacts.ForStatement | HierarchyFacts.ForInOrForOfStatement)) == 0));

        return emitExplicitInitializer;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2893-2909 — <c>visitVariableDeclarationInLetDeclarationList</c>.</summary>
    private VariableDeclaration VisitVariableDeclarationInLetDeclarationList(VariableDeclaration node)
    {
        // For binding pattern names that lack initializers there is no point to emit
        // explicit initializer since downlevel codegen for destructuring will fail
        // in the absence of initializer so all binding elements will say uninitialized
        var name = node.Name;
        if (Utilities.IsBindingPattern(name))
        {
            return (VariableDeclaration)VisitVariableDeclaration(node)!;
        }

        if (node.Initializer is null && ShouldEmitExplicitInitializerForLetDeclaration(node))
        {
            return _factory.UpdateVariableDeclaration(node, node.Name, /*exclamationToken*/ null, /*type*/ null, _factory.CreateVoidZero());
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2914-2933 — <c>visitVariableDeclaration</c>.</summary>
    private object? VisitVariableDeclaration(VariableDeclaration node)
    {
        var ancestorFacts = EnterSubtree(HierarchyFacts.ExportedVariableStatement, HierarchyFacts.None);
        object? updated;
        if (Utilities.IsBindingPattern(node.Name))
        {
            updated = Destructuring.FlattenDestructuringBinding(
                node,
                Visitor,
                _context,
                FlattenLevel.All,
                /*rval*/ null,
                (ancestorFacts & HierarchyFacts.ExportedVariableStatement) != 0);
        }
        else
        {
            updated = Utilities.VisitEachChildWorker(node, Visitor, _context);
        }

        ExitSubtree(ancestorFacts, HierarchyFacts.None, HierarchyFacts.None);
        return updated;
    }
}
