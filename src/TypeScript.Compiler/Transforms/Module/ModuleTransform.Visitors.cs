#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/module/module.ts:710-1407 — the visitors, the dynamic-import shims and the
// three `createImportCallExpression*` builders.
/// <summary>
/// ported from: src/compiler/transformers/module/module.ts:710-1407 — the top-level/nested/worker visitors, the
/// destructuring and iteration rewrites they perform, and the AMD/UMD/CommonJS spellings of a dynamic import.
/// </summary>
/// <remarks>
/// PORT-NOTE (the visitor face): TS's <c>visitor</c>/<c>discardedValueVisitor</c> are the <c>Visitor</c> function type,
/// which the port spells <c>Func&lt;Node, object?&gt;</c>, and <c>VisitResult&lt;T&gt;</c> is
/// <c>T | readonly Node[] | undefined</c>, which the port's callers discriminate by <c>is</c> tests on the result.
/// </remarks>
internal sealed partial class ModuleTransformer
{
    /// <summary>ported from: src/compiler/transformers/module/module.ts:710-727 — <c>topLevelVisitor</c>.</summary>
    private object? TopLevelVisitor(Node node)
        => node.Kind switch
        {
            SyntaxKind.ImportDeclaration => VisitTopLevelImportDeclaration((ImportDeclaration)node),
            SyntaxKind.ImportEqualsDeclaration => VisitTopLevelImportEqualsDeclaration((ImportEqualsDeclaration)node),
            SyntaxKind.ExportDeclaration => VisitTopLevelExportDeclaration((ExportDeclaration)node),
            SyntaxKind.ExportAssignment => VisitTopLevelExportAssignment((ExportAssignment)node),
            _ => TopLevelNestedVisitor(node),
        };

    /// <summary>ported from: src/compiler/transformers/module/module.ts:734-793 — <c>topLevelNestedVisitor</c>.</summary>
    private object? TopLevelNestedVisitor(Node node)
        => node.Kind switch
        {
            SyntaxKind.VariableStatement => VisitVariableStatement((VariableStatement)node),
            SyntaxKind.FunctionDeclaration => VisitFunctionDeclaration((FunctionDeclaration)node),
            SyntaxKind.ClassDeclaration => VisitClassDeclaration((ClassDeclaration)node),
            SyntaxKind.ForStatement => VisitForStatement((ForStatement)node, /*isTopLevel*/ true),
            SyntaxKind.ForInStatement => VisitForInStatement((ForInStatement)node),
            SyntaxKind.ForOfStatement => VisitForOfStatement((ForOfStatement)node),
            SyntaxKind.DoStatement => VisitDoStatement((DoStatement)node),
            SyntaxKind.WhileStatement => VisitWhileStatement((WhileStatement)node),
            SyntaxKind.LabeledStatement => VisitLabeledStatement((LabeledStatement)node),
            SyntaxKind.WithStatement => VisitWithStatement((WithStatement)node),
            SyntaxKind.IfStatement => VisitIfStatement((IfStatement)node),
            SyntaxKind.SwitchStatement => VisitSwitchStatement((SwitchStatement)node),
            SyntaxKind.CaseBlock => VisitCaseBlock((CaseBlock)node),
            SyntaxKind.CaseClause => VisitCaseClause((CaseClause)node),
            SyntaxKind.DefaultClause => VisitDefaultClause((DefaultClause)node),
            SyntaxKind.TryStatement => VisitTryStatement((TryStatement)node),
            SyntaxKind.CatchClause => VisitCatchClause((CatchClause)node),
            SyntaxKind.Block => VisitBlock((Block)node),
            _ => Visitor(node),
        };

    /// <summary>ported from: src/compiler/transformers/module/module.ts:795-834 — <c>visitorWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>SyntaxKind.CallExpression</c> arm declares <c>const needsRewrite</c> inside the switch and
    /// falls through to the shared <c>visitEachChild</c> at the end when the call is neither a rewritten import call nor
    /// a shimmed one — the port's <c>case</c> block keeps the same shape with the local hoisted to the arm's own block.
    /// </remarks>
    private object? VisitorWorker(Node node, bool valueIsDiscarded)
    {
        // This visitor does not need to descend into the tree if there is no dynamic import, destructuring assignment, or update expression
        // as export/import statements are only transformed at the top level of a file.
        if ((node.TransformFlags & (TransformFlags.ContainsDynamicImport | TransformFlags.ContainsDestructuringAssignment | TransformFlags.ContainsUpdateExpressionForIdentifier)) == 0
            && !Core.Some(_importsAndRequiresToRewriteOrShim))
        {
            return node;
        }

        switch (node.Kind)
        {
            case SyntaxKind.ForStatement:
                return VisitForStatement((ForStatement)node, /*isTopLevel*/ false);
            case SyntaxKind.ExpressionStatement:
                return VisitExpressionStatement((ExpressionStatement)node);
            case SyntaxKind.ParenthesizedExpression:
                return VisitParenthesizedExpression((ParenthesizedExpression)node, valueIsDiscarded);
            case SyntaxKind.PartiallyEmittedExpression:
                return VisitPartiallyEmittedExpression((PartiallyEmittedExpression)node, valueIsDiscarded);
            case SyntaxKind.CallExpression:
            {
                var needsRewrite = ReferenceEquals(node, Core.FirstOrUndefined(_importsAndRequiresToRewriteOrShim));
                if (needsRewrite)
                {
                    _importsAndRequiresToRewriteOrShim!.RemoveAt(0);
                }
                if (Utilities.IsImportCall(node) && _host.ShouldTransformImportCall(_currentSourceFile))
                {
                    return VisitImportCallExpression((CallExpression)node, needsRewrite);
                }
                else if (needsRewrite)
                {
                    return ShimOrRewriteImportOrRequireCall((CallExpression)node);
                }
                break;
            }
            case SyntaxKind.BinaryExpression:
                if (Utilities.IsDestructuringAssignment(node))
                {
                    return VisitDestructuringAssignment((BinaryExpression)node, valueIsDiscarded);
                }
                break;
            case SyntaxKind.PrefixUnaryExpression:
            case SyntaxKind.PostfixUnaryExpression:
                return VisitPreOrPostfixUnaryExpression(node, valueIsDiscarded);
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:836-838 — <c>visitor</c>.</summary>
    private object? Visitor(Node node) => VisitorWorker(node, /*valueIsDiscarded*/ false);

    /// <summary>ported from: src/compiler/transformers/module/module.ts:840-842 — <c>discardedValueVisitor</c>.</summary>
    private object? DiscardedValueVisitor(Node node) => VisitorWorker(node, /*valueIsDiscarded*/ true);

    /// <summary>ported from: src/compiler/transformers/module/module.ts:844-888 — <c>destructuringNeedsFlattening</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>Debug.assertNever(elem, "Unhandled object member kind")</c> is <see cref="Debug.Fail{T}"/> with the
    /// same message, and <c>length(getExports(node))</c> is <see cref="Core.Length{T}"/> over the nullable answer (a
    /// missing answer reads as length 0, which is the reference's <c>undefined.length</c>-free test).
    /// </remarks>
    private bool DestructuringNeedsFlattening(Expression node)
    {
        if (NodeTests.IsObjectLiteralExpression(node))
        {
            foreach (var elem in ((ObjectLiteralExpression)node).Properties!)
            {
                switch (elem.Kind)
                {
                    case SyntaxKind.PropertyAssignment:
                        if (DestructuringNeedsFlattening(((PropertyAssignment)elem).Initializer!))
                        {
                            return true;
                        }
                        break;
                    case SyntaxKind.ShorthandPropertyAssignment:
                        if (DestructuringNeedsFlattening(((ShorthandPropertyAssignment)elem).Name!))
                        {
                            return true;
                        }
                        break;
                    case SyntaxKind.SpreadAssignment:
                        if (DestructuringNeedsFlattening(((SpreadAssignment)elem).Expression!))
                        {
                            return true;
                        }
                        break;
                    case SyntaxKind.MethodDeclaration:
                    case SyntaxKind.GetAccessor:
                    case SyntaxKind.SetAccessor:
                        return false;
                    default:
                        Debug.Fail<object?>("Unhandled object member kind");
                        break;
                }
            }
        }
        else if (NodeTests.IsArrayLiteralExpression(node))
        {
            foreach (var elem in ((ArrayLiteralExpression)node).Elements!)
            {
                if (NodeTests.IsSpreadElement(elem))
                {
                    if (DestructuringNeedsFlattening((Expression)((SpreadElement)elem).Expression!))
                    {
                        return true;
                    }
                }
                else if (DestructuringNeedsFlattening((Expression)elem))
                {
                    return true;
                }
            }
        }
        else if (NodeTests.IsIdentifier(node))
        {
            return Core.Length(GetExports((Identifier)node)) > (FactoryUtilities.IsExportName(node) ? 1 : 0);
        }
        return false;
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:890-895 — <c>visitDestructuringAssignment</c>.</summary>
    private object? VisitDestructuringAssignment(BinaryExpression node, bool valueIsDiscarded)
        => DestructuringNeedsFlattening((Expression)node.Left!)
            ? Destructuring.FlattenDestructuringAssignment(
                node,
                Visitor,
                _context,
                FlattenLevel.All,
                !valueIsDiscarded,
                CreateAllExportExpressions)
            : Utilities.VisitEachChildWorker(node, Visitor, _context);

    /// <summary>ported from: src/compiler/transformers/module/module.ts:897-925 — <c>visitForStatement</c>.</summary>
    private object? VisitForStatement(ForStatement node, bool isTopLevel)
    {
        if (
            isTopLevel && node.Initializer is not null
            && NodeTests.IsVariableDeclarationList(node.Initializer!)
            && (((VariableDeclarationList)node.Initializer!).Flags & NodeFlags.BlockScoped) == 0
        )
        {
            var exportStatements = AppendExportsOfVariableDeclarationList(/*statements*/ null, (VariableDeclarationList)node.Initializer, /*isForInOrOfInitializer*/ false);
            if (exportStatements is not null)
            {
                var statements = new List<IStatement>();
                var varDeclList = Utilities.VisitNode(node.Initializer, DiscardedValueVisitor, NodeTests.IsVariableDeclarationList);
                var varStatement = _factory.CreateVariableStatement(/*modifiers*/ null, varDeclList);
                statements.Add(varStatement);
                Core.AddRange(statements, exportStatements);

                var condition = Utilities.VisitNode(node.Condition, Visitor, Utilities.IsExpression);
                var incrementor = Utilities.VisitNode(node.Incrementor, DiscardedValueVisitor, Utilities.IsExpression);
                var body = Utilities.VisitIterationBody(node.Statement!, isTopLevel ? TopLevelNestedVisitor : Visitor, _context);
                statements.Add(_factory.UpdateForStatement(node, /*initializer*/ null, condition, incrementor, body));
                return statements;
            }
        }
        return _factory.UpdateForStatement(
            node,
            Utilities.VisitNode(node.Initializer, DiscardedValueVisitor, Utilities.IsForInitializer),
            Utilities.VisitNode(node.Condition, Visitor, Utilities.IsExpression),
            Utilities.VisitNode(node.Incrementor, DiscardedValueVisitor, Utilities.IsExpression),
            Utilities.VisitIterationBody(node.Statement!, isTopLevel ? TopLevelNestedVisitor : Visitor, _context));
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:932-951 — <c>visitForInStatement</c>.</summary>
    private object? VisitForInStatement(ForInStatement node)
    {
        if (NodeTests.IsVariableDeclarationList(node.Initializer!) && (((VariableDeclarationList)node.Initializer!).Flags & NodeFlags.BlockScoped) == 0)
        {
            var exportStatements = AppendExportsOfVariableDeclarationList(/*statements*/ null, (VariableDeclarationList)node.Initializer!, /*isForInOrOfInitializer*/ true);
            if (Core.Some(exportStatements))
            {
                var initializer = Utilities.VisitNode(node.Initializer, DiscardedValueVisitor, Utilities.IsForInitializer);
                var expression = Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression);
                var body = Utilities.VisitIterationBody(node.Statement!, TopLevelNestedVisitor, _context);
                var mergedBody = NodeTests.IsBlock((Node)body)
                    ? _factory.UpdateBlock((Block)body, [.. exportStatements!, .. ((Block)body).Statements!])
                    : _factory.CreateBlock([.. exportStatements!, body], /*multiLine*/ true);
                return _factory.UpdateForInStatement(node, initializer, expression, mergedBody);
            }
        }
        return _factory.UpdateForInStatement(
            node,
            Utilities.VisitNode(node.Initializer, DiscardedValueVisitor, Utilities.IsForInitializer),
            Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression),
            Utilities.VisitIterationBody(node.Statement!, TopLevelNestedVisitor, _context));
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:958-978 — <c>visitForOfStatement</c>.</summary>
    private object? VisitForOfStatement(ForOfStatement node)
    {
        if (NodeTests.IsVariableDeclarationList(node.Initializer!) && (((VariableDeclarationList)node.Initializer!).Flags & NodeFlags.BlockScoped) == 0)
        {
            var exportStatements = AppendExportsOfVariableDeclarationList(/*statements*/ null, (VariableDeclarationList)node.Initializer!, /*isForInOrOfInitializer*/ true);
            var initializer = Utilities.VisitNode(node.Initializer, DiscardedValueVisitor, Utilities.IsForInitializer);
            var expression = Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression);
            var body = Utilities.VisitIterationBody(node.Statement!, TopLevelNestedVisitor, _context);
            if (Core.Some(exportStatements))
            {
                body = NodeTests.IsBlock((Node)body)
                    ? _factory.UpdateBlock((Block)body, [.. exportStatements!, .. ((Block)body).Statements!])
                    : _factory.CreateBlock([.. exportStatements!, body], /*multiLine*/ true);
            }
            return _factory.UpdateForOfStatement(node, node.AwaitModifier, initializer, expression, body);
        }
        return _factory.UpdateForOfStatement(
            node,
            node.AwaitModifier,
            Utilities.VisitNode(node.Initializer, DiscardedValueVisitor, Utilities.IsForInitializer),
            Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression),
            Utilities.VisitIterationBody(node.Statement!, TopLevelNestedVisitor, _context));
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:985-991 — <c>visitDoStatement</c>.</summary>
    private DoStatement VisitDoStatement(DoStatement node)
        => _factory.UpdateDoStatement(
            node,
            Utilities.VisitIterationBody(node.Statement!, TopLevelNestedVisitor, _context),
            Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression));

    /// <summary>ported from: src/compiler/transformers/module/module.ts:998-1004 — <c>visitWhileStatement</c>.</summary>
    private WhileStatement VisitWhileStatement(WhileStatement node)
        => _factory.UpdateWhileStatement(
            node,
            Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression),
            Utilities.VisitIterationBody(node.Statement!, TopLevelNestedVisitor, _context));

    /// <summary>ported from: src/compiler/transformers/module/module.ts:1011-1017 — <c>visitLabeledStatement</c>.</summary>
    private LabeledStatement VisitLabeledStatement(LabeledStatement node)
        => _factory.UpdateLabeledStatement(
            node,
            node.Label,
            Utilities.VisitNode(node.Statement, TopLevelNestedVisitor, Utilities.IsStatement, _liftToBlock)
                ?? Utilities.SetTextRange(_factory.CreateEmptyStatement(), (ITextRange?)node.Statement));

    /// <summary>ported from: src/compiler/transformers/module/module.ts:1024-1030 — <c>visitWithStatement</c>.</summary>
    private WithStatement VisitWithStatement(WithStatement node)
        => _factory.UpdateWithStatement(
            node,
            Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression),
            Debug.CheckDefined(Utilities.VisitNode(node.Statement, TopLevelNestedVisitor, Utilities.IsStatement, _liftToBlock)));

    /// <summary>ported from: src/compiler/transformers/module/module.ts:1037-1044 — <c>visitIfStatement</c>.</summary>
    private IfStatement VisitIfStatement(IfStatement node)
        => _factory.UpdateIfStatement(
            node,
            Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression),
            Utilities.VisitNode(node.ThenStatement, TopLevelNestedVisitor, Utilities.IsStatement, _liftToBlock) ?? _factory.CreateBlock([], null),
            Utilities.VisitNode(node.ElseStatement, TopLevelNestedVisitor, Utilities.IsStatement, _liftToBlock));

    /// <summary>ported from: src/compiler/transformers/module/module.ts:1051-1057 — <c>visitSwitchStatement</c>.</summary>
    private SwitchStatement VisitSwitchStatement(SwitchStatement node)
        => _factory.UpdateSwitchStatement(
            node,
            Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression),
            Debug.CheckDefined(Utilities.VisitNode(node.CaseBlock, TopLevelNestedVisitor, NodeTests.IsCaseBlock)));

    /// <summary>ported from: src/compiler/transformers/module/module.ts:1064-1069 — <c>visitCaseBlock</c>.</summary>
    private CaseBlock VisitCaseBlock(CaseBlock node)
        => _factory.UpdateCaseBlock(
            node,
            Utilities.VisitNodes(node.Clauses, TopLevelNestedVisitor, Utilities.IsCaseOrDefaultClause));

    /// <summary>ported from: src/compiler/transformers/module/module.ts:1076-1082 — <c>visitCaseClause</c>.</summary>
    private CaseClause VisitCaseClause(CaseClause node)
        => _factory.UpdateCaseClause(
            node,
            Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression),
            Utilities.VisitNodes(node.Statements, TopLevelNestedVisitor, Utilities.IsStatement));

    /// <summary>ported from: src/compiler/transformers/module/module.ts:1089-1091 — <c>visitDefaultClause</c>.</summary>
    private DefaultClause VisitDefaultClause(DefaultClause node)
        => Utilities.VisitEachChildWorker(node, TopLevelNestedVisitor, _context);

    /// <summary>ported from: src/compiler/transformers/module/module.ts:1098-1100 — <c>visitTryStatement</c>.</summary>
    private TryStatement VisitTryStatement(TryStatement node)
        => Utilities.VisitEachChildWorker(node, TopLevelNestedVisitor, _context);

    /// <summary>ported from: src/compiler/transformers/module/module.ts:1107-1113 — <c>visitCatchClause</c>.</summary>
    private CatchClause VisitCatchClause(CatchClause node)
        => _factory.UpdateCatchClause(
            node,
            node.VariableDeclaration,
            Debug.CheckDefined(Utilities.VisitNode(node.Block, TopLevelNestedVisitor, NodeTests.IsBlock)));

    /// <summary>ported from: src/compiler/transformers/module/module.ts:1120-1123 — <c>visitBlock</c>.</summary>
    private Block VisitBlock(Block node) => Utilities.VisitEachChildWorker(node, TopLevelNestedVisitor, _context);

    /// <summary>ported from: src/compiler/transformers/module/module.ts:1125-1130 — <c>visitExpressionStatement</c>.</summary>
    private ExpressionStatement VisitExpressionStatement(ExpressionStatement node)
        => _factory.UpdateExpressionStatement(
            node,
            Utilities.VisitNode(node.Expression, DiscardedValueVisitor, Utilities.IsExpression));

    /// <summary>ported from: src/compiler/transformers/module/module.ts:1132-1134 — <c>visitParenthesizedExpression</c>.</summary>
    private ParenthesizedExpression VisitParenthesizedExpression(ParenthesizedExpression node, bool valueIsDiscarded)
        => _factory.UpdateParenthesizedExpression(
            node,
            Utilities.VisitNode(node.Expression, valueIsDiscarded ? DiscardedValueVisitor : Visitor, Utilities.IsExpression));

    /// <summary>ported from: src/compiler/transformers/module/module.ts:1136-1138 — <c>visitPartiallyEmittedExpression</c>.</summary>
    private PartiallyEmittedExpression VisitPartiallyEmittedExpression(PartiallyEmittedExpression node, bool valueIsDiscarded)
        => _factory.UpdatePartiallyEmittedExpression(
            node,
            Utilities.VisitNode(node.Expression, valueIsDiscarded ? DiscardedValueVisitor : Visitor, Utilities.IsExpression));

    /// <summary>ported from: src/compiler/transformers/module/module.ts:1140-1190 — <c>visitPreOrPostfixUnaryExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>PrefixUnaryExpression | PostfixUnaryExpression</c> union, whose
    /// <c>operator</c> slot the two classes declare separately and whose <c>operand</c> slots have different (wider and
    /// narrower) types; the port reads both by kind, the treatment <c>GeneratorsTransform</c> records for the same union.
    /// </remarks>
    private object? VisitPreOrPostfixUnaryExpression(Node node, bool valueIsDiscarded)
    {
        // When we see a prefix or postfix increment expression whose operand is an exported
        // symbol, we should ensure all exports of that symbol are updated with the correct
        // value.
        //
        // - We do not transform generated identifiers for any reason.
        // - We do not transform identifiers tagged with the LocalName flag.
        // - We do not transform identifiers that were originally the name of an enum or
        //   namespace due to how they are transformed in TypeScript.
        // - We only transform identifiers that are exported at the top level.
        var isPrefix = NodeTests.IsPrefixUnaryExpression(node);
        var @operator = isPrefix ? ((PrefixUnaryExpression)node).Operator : ((PostfixUnaryExpression)node).Operator;
        Expression? operand = isPrefix ? ((PrefixUnaryExpression)node).Operand : ((PostfixUnaryExpression)node).Operand;
        if (
            (@operator == SyntaxKind.PlusPlusToken || @operator == SyntaxKind.MinusMinusToken)
            && operand is not null
            && NodeTests.IsIdentifier(operand)
            && !Utilities.IsGeneratedIdentifier(operand)
            && !FactoryUtilities.IsLocalName(operand)
            && !ModuleTransformSupport.IsDeclarationNameOfEnumOrNamespace((Identifier)operand)
        )
        {
            var exportedNames = GetExports((Identifier)operand);
            if (exportedNames is not null)
            {
                Identifier? temp = null;
                Expression expression = Utilities.VisitNode(operand, Visitor, Utilities.IsExpression)!;
                if (isPrefix)
                {
                    expression = _factory.UpdatePrefixUnaryExpression((PrefixUnaryExpression)node, expression);
                }
                else
                {
                    expression = _factory.UpdatePostfixUnaryExpression((PostfixUnaryExpression)node, expression);
                    if (!valueIsDiscarded)
                    {
                        temp = _factory.CreateTempVariable(_context.HoistVariableDeclaration, null);
                        expression = _factory.CreateAssignment(temp, expression);
                        Utilities.SetTextRange(expression, node);
                    }
                    expression = _factory.CreateComma(expression, _factory.CloneNode(operand));
                    Utilities.SetTextRange(expression, node);
                }

                foreach (var exportName in exportedNames)
                {
                    _noSubstitution.Add(Utilities.GetNodeId(expression));
                    expression = CreateExportExpression(exportName, expression);
                    Utilities.SetTextRange(expression, node);
                }

                if (temp is not null)
                {
                    _noSubstitution.Add(Utilities.GetNodeId(expression));
                    expression = _factory.CreateComma(expression, temp);
                    Utilities.SetTextRange(expression, node);
                }
                return expression;
            }
        }
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:1192-1206 — <c>shimOrRewriteImportOrRequireCall</c>.</summary>
    private CallExpression ShimOrRewriteImportOrRequireCall(CallExpression node)
        => _factory.UpdateCallExpression(
            node,
            node.Expression,
            /*typeArguments*/ null,
            Utilities.VisitNodes(
                node.Arguments,
                arg =>
                {
                    if (ReferenceEquals(arg, node.Arguments![0]))
                    {
                        return Utilities.IsStringLiteralLike(arg)
                            ? TransformationUtilities.RewriteModuleSpecifier((Expression)arg, _compilerOptions)
                            : _emitHelpers.CreateRewriteRelativeImportExtensionsHelper((Expression)arg);
                    }
                    return Visitor(arg);
                },
                Utilities.IsExpression));

    /// <summary>ported from: src/compiler/transformers/module/module.ts:1208-1230 — <c>visitImportCallExpression</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>ImportCall</c> is the <c>CallExpression</c> the <c>isImportCall</c> test narrows to, so
    /// the port's parameter is the class. The <c>?? factory.createVoidZero()</c> of the UMD arm and the nested
    /// three-way conditional that picks the argument keep the reference's shape.</remarks>
    private Expression VisitImportCallExpression(CallExpression node, bool rewriteOrShim)
    {
        if (_moduleKind == ModuleKind.None && _languageVersion >= ScriptTarget.ES2020)
        {
            return Utilities.VisitEachChildWorker(node, Visitor, _context);
        }
        var externalModuleName = ModuleTransformSupport.GetExternalModuleNameLiteral(_factory, node, _currentSourceFile!, _host, _resolver, _compilerOptions);
        var firstArgument = Utilities.VisitNode(Core.FirstOrUndefined(node.Arguments), Visitor, Utilities.IsExpression);
        // Only use the external module name if it differs from the first argument. This allows us to preserve the quote style of the argument on output.
        Expression? argument = externalModuleName is not null
            && (firstArgument is null
                || !NodeTests.IsStringLiteral(firstArgument)
                || ((StringLiteral)firstArgument).Text != externalModuleName.Text)
            ? externalModuleName
            : firstArgument is not null && rewriteOrShim
                ? NodeTests.IsStringLiteral(firstArgument)
                    ? TransformationUtilities.RewriteModuleSpecifier(firstArgument, _compilerOptions)
                    : _emitHelpers.CreateRewriteRelativeImportExtensionsHelper(firstArgument)
                : firstArgument;
        var containsLexicalThis = (node.TransformFlags & TransformFlags.ContainsLexicalThis) != 0;
        return _compilerOptions.Module switch
        {
            ModuleKind.AMD => CreateImportCallExpressionAMD(argument, containsLexicalThis),
            ModuleKind.UMD => CreateImportCallExpressionUMD(argument ?? _factory.CreateVoidZero(), containsLexicalThis),
            _ => CreateImportCallExpressionCommonJS(argument),
        };
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:1232-1270 — <c>createImportCallExpressionUMD</c>.</summary>
    private Expression CreateImportCallExpressionUMD(Expression arg, bool containsLexicalThis)
    {
        // (function (factory) {
        //      ... (regular UMD)
        // }
        // })(function (require, exports, useSyncRequire) {
        //      "use strict";
        //      Object.defineProperty(exports, "__esModule", { value: true });
        //      var __syncRequire = typeof module === "object" && typeof module.exports === "object";
        //      var __resolved = new Promise(function (resolve) { resolve(); });
        //      .....
        //      __syncRequire
        //          ? __resolved.then(function () { return require(x); }) /*CommonJs Require*/
        //          : new Promise(function (_a, _b) { require([x], _a, _b); }); /*Amd Require*/
        // });
        _needUMDDynamicImportHelper = true;
        if (TransformationUtilities.IsSimpleCopiableExpression(arg))
        {
            Expression argClone = Utilities.IsGeneratedIdentifier(arg)
                ? arg
                : NodeTests.IsStringLiteral(arg)
                    ? _factory.CreateStringLiteralFromNode(arg, /*isSingleQuote*/ null)
                    : EmitNodes.SetEmitFlags(Utilities.SetTextRange(_factory.CloneNode(arg), arg), EmitFlags.NoComments);
            return _factory.CreateConditionalExpression(
                /*condition*/ _factory.CreateIdentifier("__syncRequire", null, null),
                /*questionToken*/ null,
                /*whenTrue*/ CreateImportCallExpressionCommonJS(arg),
                /*colonToken*/ null,
                /*whenFalse*/ CreateImportCallExpressionAMD(argClone, containsLexicalThis));
        }
        else
        {
            var temp = _factory.CreateTempVariable(_context.HoistVariableDeclaration, null);
            return _factory.CreateComma(
                _factory.CreateAssignment(temp, arg),
                _factory.CreateConditionalExpression(
                    /*condition*/ _factory.CreateIdentifier("__syncRequire", null, null),
                    /*questionToken*/ null,
                    /*whenTrue*/ CreateImportCallExpressionCommonJS(temp, /*isInlineable*/ true),
                    /*colonToken*/ null,
                    /*whenFalse*/ CreateImportCallExpressionAMD(temp, containsLexicalThis)));
        }
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:1272-1330 — <c>createImportCallExpressionAMD</c>.</summary>
    private Expression CreateImportCallExpressionAMD(Expression? arg, bool containsLexicalThis)
    {
        // import("./blah")
        // emit as
        // define(["require", "exports", "blah"], function (require, exports) {
        //     ...
        //     new Promise(function (_a, _b) { require([x], _a, _b); }); /*Amd Require*/
        // });
        var resolve = _factory.CreateUniqueName("resolve", null);
        var reject = _factory.CreateUniqueName("reject", null);
        List<ParameterDeclaration> parameters =
        [
            _factory.CreateParameterDeclaration(/*modifiers*/ null, /*dotDotDotToken*/ null, /*name*/ resolve, null, null, null),
            _factory.CreateParameterDeclaration(/*modifiers*/ null, /*dotDotDotToken*/ null, /*name*/ reject, null, null, null),
        ];
        var body = _factory.CreateBlock(
            [
                _factory.CreateExpressionStatement(
                    _factory.CreateCallExpression(
                        _factory.CreateIdentifier("require", null, null),
                        /*typeArguments*/ null,
                        [
                            _factory.CreateArrayLiteralExpression([arg ?? _factory.CreateOmittedExpression()], null),
                            resolve,
                            reject,
                        ])),
            ],
            /*multiLine*/ null);

        Expression func;
        if (_languageVersion >= ScriptTarget.ES2015)
        {
            func = _factory.CreateArrowFunction(
                /*modifiers*/ null,
                /*typeParameters*/ null,
                parameters,
                /*type*/ null,
                /*equalsGreaterThanToken*/ null,
                body);
        }
        else
        {
            func = _factory.CreateFunctionExpression(
                /*modifiers*/ null,
                /*asteriskToken*/ null,
                /*name*/ null,
                /*typeParameters*/ null,
                parameters,
                /*type*/ null,
                body);

            // if there is a lexical 'this' in the import call arguments, ensure we indicate
            // that this new function expression indicates it captures 'this' so that the
            // es2015 transformer will properly substitute 'this' with '_this'.
            if (containsLexicalThis)
            {
                EmitNodes.SetEmitFlags(func, EmitFlags.CapturesThis);
            }
        }

        var promise = _factory.CreateNewExpression(_factory.CreateIdentifier("Promise", null, null), /*typeArguments*/ null, [func]);
        if (Utilities.GetESModuleInterop(_compilerOptions))
        {
            return _factory.CreateCallExpression(
                _factory.CreatePropertyAccessExpression(promise, _factory.CreateIdentifier("then", null, null)),
                /*typeArguments*/ null,
                [_emitHelpers.CreateImportStarCallbackHelper()]);
        }
        return promise;
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:1332-1407 — <c>createImportCallExpressionCommonJS</c>.</summary>
    private Expression CreateImportCallExpressionCommonJS(Expression? arg, bool? isInlineable = null)
    {
        // import(x)
        // emit as
        // Promise.resolve(`${x}`).then((s) => require(s)) /*CommonJs Require*/
        // We have to wrap require in then callback so that require is done in asynchronously
        // if we simply do require in resolve callback in Promise constructor. We will execute the loading immediately
        // If the arg is not inlineable, we have to evaluate and ToString() it in the current scope
        // Otherwise, we inline it in require() so that it's statically analyzable
        var needSyncEval = arg is not null && !TransformationUtilities.IsSimpleInlineableExpression(arg) && !Js.IsTruthy(isInlineable);

        var promiseResolveCall = _factory.CreateCallExpression(
            _factory.CreatePropertyAccessExpression(_factory.CreateIdentifier("Promise", null, null), "resolve"),
            /*typeArguments*/ null,
            /*argumentsArray*/ needSyncEval
                ? _languageVersion >= ScriptTarget.ES2015
                    ?
                    [
                        _factory.CreateTemplateExpression(
                            _factory.CreateTemplateHead("", null, null),
                            [
                                _factory.CreateTemplateSpan(arg, _factory.CreateTemplateTail("", null, null)),
                            ]),
                    ]
                    :
                    [
                        _factory.CreateCallExpression(
                            _factory.CreatePropertyAccessExpression(_factory.CreateStringLiteral("", null, null), "concat"),
                            /*typeArguments*/ null,
                            [arg!]),
                    ]
                : []);

        Expression requireCall = _factory.CreateCallExpression(
            _factory.CreateIdentifier("require", null, null),
            /*typeArguments*/ null,
            needSyncEval
                ? [_factory.CreateIdentifier("s", null, null)]
                : arg is not null ? [arg] : []);
        if (Utilities.GetESModuleInterop(_compilerOptions))
        {
            requireCall = _emitHelpers.CreateImportStarHelper(requireCall);
        }

        List<ParameterDeclaration> parameters = needSyncEval
            ?
            [
                _factory.CreateParameterDeclaration(
                    /*modifiers*/ null,
                    /*dotDotDotToken*/ null,
                    /*name*/ "s",
                    null,
                    null,
                    null),
            ]
            : [];

        Expression func;
        if (_languageVersion >= ScriptTarget.ES2015)
        {
            func = _factory.CreateArrowFunction(
                /*modifiers*/ null,
                /*typeParameters*/ null,
                /*parameters*/ parameters,
                /*type*/ null,
                /*equalsGreaterThanToken*/ null,
                requireCall);
        }
        else
        {
            func = _factory.CreateFunctionExpression(
                /*modifiers*/ null,
                /*asteriskToken*/ null,
                /*name*/ null,
                /*typeParameters*/ null,
                /*parameters*/ parameters,
                /*type*/ null,
                _factory.CreateBlock([_factory.CreateReturnStatement(requireCall)], null));
        }

        var downleveledImport = _factory.CreateCallExpression(
            _factory.CreatePropertyAccessExpression(promiseResolveCall, "then"),
            /*typeArguments*/ null,
            [func]);

        return downleveledImport;
    }
}
