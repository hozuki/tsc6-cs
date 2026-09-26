#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/es2018.ts:1-1486 — the whole ES2018 downlevel transform.
/// <summary>
/// ported from: src/compiler/transformers/es2018.ts:154-1486 — <c>transformES2018</c>: the transformer that rewrites
/// object rest/spread, async generators, <c>for await&hellip;of</c>, the object-rest parameters of a function signature
/// and the object-rest patterns of a <c>catch</c> clause, and that substitutes <c>super</c> accesses inside async
/// methods.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (the shape): as <c>ES2015Transform</c> records for <c>es2015.ts</c>, the reference exports one function
/// whose body is a table of closures over the factory's locals; the port states the factory as
/// <see cref="ES2018Transform.TransformES2018"/> and the closures as the members of
/// <see cref="ES2018Transformer"/> (<c>ES2018Transform.cs</c>, plus the substitution hooks in
/// <c>ES2018Transform.Substitution.cs</c>).
/// </para>
/// <para>
/// PORT-NOTE (<c>HierarchyFacts</c>): the reference states this const enum at <c>transformers/es2018.ts:119-152</c> with
/// its own members; the landed <c>ES2015Transform.cs</c> already holds the namespace-level name for <c>es2015.ts</c>'s
/// <em>different</em> flag set, so the port states this one nested in <see cref="ES2018Transformer"/> — the two
/// reference modules keep them apart by being separate modules, and nothing but this transform's own members reads this
/// one.
/// </para>
/// <para>
/// PORT-NOTE (<c>createSuperAccessVariableStatement</c>): the reference imports it from <c>./es2017.js</c>
/// (es2018.ts:26); the port calls <see cref="ES2017Transform.CreateSuperAccessVariableStatement"/>, the same member the
/// ES2017 transform itself calls.
/// </para>
/// </remarks>
internal static class ES2018Transform
{
    /// <summary>ported from: src/compiler/transformers/es2018.ts:154-1486 — <c>transformES2018</c>.</summary>
    public static Func<object?, object?> TransformES2018(ITransformerContext context)
        => new ES2018Transformer(context).TransformSourceFileOrBundle;
}

// ported from: src/compiler/transformers/es2018.ts:78-82 — the const enum `ESNextSubstitutionFlags`.
/// <summary>ported from: src/compiler/transformers/es2018.ts:78-82 — <c>ESNextSubstitutionFlags</c>.</summary>
/// <remarks>PORT-NOTE: the reference declares this const enum in <c>transformers/es2018.ts</c> under this name (it is the
/// file the former <c>esnext.ts</c> split left its substitution flags with); the port states it beside the transform
/// with the same members in the same order.</remarks>
[Flags]
internal enum ESNextSubstitutionFlags
{
    /// <summary><c>ESNextSubstitutionFlags.None</c>.</summary>
    None = 0,

    /// <summary><c>AsyncMethodsWithSuper</c> — enables substitutions for async methods with <c>super</c> calls.</summary>
    AsyncMethodsWithSuper = 1 << 0,
}

/// <summary>ported from: src/compiler/transformers/es2018.ts:155-1485 — the body of <c>transformES2018</c>.</summary>
/// <remarks>
/// PORT-NOTE (the state): the reference's <c>exportedVariableStatement</c>, <c>enabledSubstitutions</c>,
/// <c>enclosingFunctionFlags</c>, <c>parametersWithPrecedingObjectRestOrSpread</c>,
/// <c>enclosingSuperContainerFlags</c>, <c>hierarchyFacts</c>, <c>currentSourceFile</c>,
/// <c>taggedTemplateStringDeclarations</c>, <c>capturedSuperProperties</c>, <c>hasSuperElementAccess</c> and
/// <c>substitutedSuperAccessors</c> are the factory function's locals that the closures mutate; they are this class's
/// fields.
/// </remarks>
internal sealed partial class ES2018Transformer
{
    // ported from: src/compiler/transformers/es2018.ts:119-152 — the const enum `HierarchyFacts`.
    /// <summary>ported from: src/compiler/transformers/es2018.ts:119-152 — <c>HierarchyFacts</c>.</summary>
    /// <remarks>PORT-NOTE: see the file header; the reference's composite members (<c>AncestorFactsMask</c> and the
    /// per-kind include/exclude masks) are stated by the same expressions, which are all constant in C#.</remarks>
    [Flags]
    internal enum HierarchyFacts
    {
        /// <summary><c>HierarchyFacts.None</c>.</summary>
        None = 0,

        //
        // Ancestor facts
        //

        /// <summary><c>HasLexicalThis</c>.</summary>
        HasLexicalThis = 1 << 0,

        /// <summary><c>IterationContainer</c>.</summary>
        IterationContainer = 1 << 1,

        //
        // Ancestor masks
        //

        /// <summary><c>AncestorFactsMask</c>.</summary>
        AncestorFactsMask = (IterationContainer << 1) - 1,

        /// <summary><c>SourceFileIncludes</c>.</summary>
        SourceFileIncludes = HasLexicalThis,

        /// <summary><c>SourceFileExcludes</c>.</summary>
        SourceFileExcludes = IterationContainer,

        /// <summary><c>StrictModeSourceFileIncludes</c>.</summary>
        StrictModeSourceFileIncludes = None,

        /// <summary><c>ClassOrFunctionIncludes</c>.</summary>
        ClassOrFunctionIncludes = HasLexicalThis,

        /// <summary><c>ClassOrFunctionExcludes</c>.</summary>
        ClassOrFunctionExcludes = IterationContainer,

        /// <summary><c>ArrowFunctionIncludes</c>.</summary>
        ArrowFunctionIncludes = None,

        /// <summary><c>ArrowFunctionExcludes</c>.</summary>
        ArrowFunctionExcludes = ClassOrFunctionExcludes,

        /// <summary><c>IterationStatementIncludes</c>.</summary>
        IterationStatementIncludes = IterationContainer,

        /// <summary><c>IterationStatementExcludes</c>.</summary>
        IterationStatementExcludes = None,
    }

    private readonly ITransformerContext _context;
    private readonly NodeFactory _factory;
    private readonly IEmitHelperFactory _emitHelpers;
    private readonly IEmitResolver _resolver;
    private readonly ICompilerOptions _compilerOptions;
    private readonly ScriptTarget _languageVersion;
    private readonly Func<EmitHint, Node, Node> _previousOnSubstituteNode;
    private readonly Action<EmitHint, Node, Action<EmitHint, Node>> _previousOnEmitNode;

    /// <summary>ported from: src/compiler/transformers/es2018.ts:182 — <c>chainBundle(context, transformSourceFile)</c>.</summary>
    private readonly Func<Node, Node> _transformSourceFile;

    /// <summary>ported from: src/compiler/transformers/es2018.ts:474 — <c>factory.liftToBlock</c>.</summary>
    /// <remarks>PORT-NOTE: the port's visitor <c>lift</c> slot is <c>Func&lt;IReadOnlyList&lt;Node&gt;, Node?&gt;</c> while
    /// <c>NodeFactory.LiftToBlock</c> takes a <c>List&lt;Node&gt;</c>; this is that adapter, built once.</remarks>
    private readonly Func<IReadOnlyList<Node>, Node?> _liftToBlock;

    /// <summary>ported from: src/compiler/transformers/es2018.ts:166 — <c>exportedVariableStatement</c>.</summary>
    private bool _exportedVariableStatement;

    /// <summary>ported from: src/compiler/transformers/es2018.ts:167 — <c>enabledSubstitutions</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's comment records that this is persisted between each SourceFile
    /// transformation and is not reset; it is a field for that reason.</remarks>
    private ESNextSubstitutionFlags _enabledSubstitutions = ESNextSubstitutionFlags.None;

    /// <summary>ported from: src/compiler/transformers/es2018.ts:168 — <c>enclosingFunctionFlags</c>.</summary>
    private FunctionFlags _enclosingFunctionFlags;

    /// <summary>ported from: src/compiler/transformers/es2018.ts:169 — <c>parametersWithPrecedingObjectRestOrSpread</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>Set&lt;ParameterDeclaration&gt;</c> is only tested for membership here, so a
    /// <c>HashSet&lt;ParameterDeclaration&gt;</c> is that set exactly.</remarks>
    private HashSet<ParameterDeclaration>? _parametersWithPrecedingObjectRestOrSpread;

    /// <summary>ported from: src/compiler/transformers/es2018.ts:170 — <c>enclosingSuperContainerFlags</c>.</summary>
    private NodeCheckFlags _enclosingSuperContainerFlags;

    /// <summary>ported from: src/compiler/transformers/es2018.ts:171 — <c>hierarchyFacts</c>.</summary>
    private HierarchyFacts _hierarchyFacts = HierarchyFacts.None;

    /// <summary>ported from: src/compiler/transformers/es2018.ts:173 — <c>currentSourceFile</c>.</summary>
    private SourceFile? _currentSourceFile;

    /// <summary>ported from: src/compiler/transformers/es2018.ts:174 — <c>taggedTemplateStringDeclarations</c>.</summary>
    private List<VariableDeclaration>? _taggedTemplateStringDeclarations;

    /// <summary>ported from: src/compiler/transformers/es2018.ts:177 — <c>capturedSuperProperties</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>Set&lt;__String&gt;</c> is iterated by
    /// <see cref="ES2017Transform.CreateSuperAccessVariableStatement"/> in insertion order, which is the emitted
    /// accessors' order; see <see cref="ES2017TransformSupport.OrderedSet{T}"/>.</remarks>
    private ES2017TransformSupport.OrderedSet<string>? _capturedSuperProperties;

    /// <summary>ported from: src/compiler/transformers/es2018.ts:179 — <c>hasSuperElementAccess</c>.</summary>
    private bool _hasSuperElementAccess;

    /// <summary>ported from: src/compiler/transformers/es2018.ts:181 — <c>substitutedSuperAccessors</c>.</summary>
    /// <remarks>PORT-NOTE: as <c>ES2017Transformer</c>'s field of the same name — a grown <c>List&lt;bool&gt;</c> with the
    /// reference's two readings.</remarks>
    private readonly List<bool> _substitutedSuperAccessors = [];

    /// <summary>ported from: src/compiler/transformers/es2018.ts:154-186 — the factory's prologue.</summary>
    public ES2018Transformer(ITransformerContext context)
    {
        _context = context;
        _factory = context.Factory;
        _emitHelpers = context.GetEmitHelperFactory();
        _resolver = context.GetEmitResolver();
        _compilerOptions = context.GetCompilerOptions();
        _languageVersion = Utilities.GetEmitScriptTarget(_compilerOptions);
        _liftToBlock = nodes => (Node)_factory.LiftToBlock(nodes as List<Node> ?? new List<Node>(nodes));
        _transformSourceFile = TransformationUtilities.ChainBundle(context, TransformSourceFile);

        _previousOnEmitNode = context.OnEmitNode!;
        _previousOnSubstituteNode = context.OnSubstituteNode!;
        context.OnEmitNode = OnEmitNode;
        context.OnSubstituteNode = OnSubstituteNode;
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:182 — the returned
    /// <c>chainBundle(context, transformSourceFile)</c>.</summary>
    public object? TransformSourceFileOrBundle(object? node) => _transformSourceFile((Node)node!);

    /// <summary>ported from: src/compiler/transformers/es2018.ts:192-194 — <c>affectsSubtree</c>.</summary>
    private bool AffectsSubtree(HierarchyFacts excludeFacts, HierarchyFacts includeFacts)
        => _hierarchyFacts != (_hierarchyFacts & ~excludeFacts | includeFacts);

    /// <summary>ported from: src/compiler/transformers/es2018.ts:196-205 — <c>enterSubtree</c>.</summary>
    private HierarchyFacts EnterSubtree(HierarchyFacts excludeFacts, HierarchyFacts includeFacts)
    {
        var ancestorFacts = _hierarchyFacts;
        _hierarchyFacts = (_hierarchyFacts & ~excludeFacts | includeFacts) & HierarchyFacts.AncestorFactsMask;
        return ancestorFacts;
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:207-214 — <c>exitSubtree</c>.</summary>
    private void ExitSubtree(HierarchyFacts ancestorFacts) => _hierarchyFacts = ancestorFacts;

    /// <summary>ported from: src/compiler/transformers/es2018.ts:216-221 — <c>recordTaggedTemplateString</c>.</summary>
    private void RecordTaggedTemplateString(Identifier temp)
        => _taggedTemplateStringDeclarations = (List<VariableDeclaration>?)Core.Append(
            _taggedTemplateStringDeclarations,
            _factory.CreateVariableDeclaration(temp, /*exclamationToken*/ null, /*type*/ null, /*initializer*/ null));

    /// <summary>ported from: src/compiler/transformers/es2018.ts:223-235 — <c>transformSourceFile</c>.</summary>
    private SourceFile TransformSourceFile(SourceFile node)
    {
        if (node.IsDeclarationFile)
        {
            return node;
        }

        _currentSourceFile = node;
        var visited = VisitSourceFile(node);
        EmitNodes.AddEmitHelpers(visited, _context.ReadEmitHelpers());

        _currentSourceFile = null;
        _taggedTemplateStringDeclarations = null;
        return visited;
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:237-239 — <c>visitor</c>.</summary>
    private object? Visitor(Node node) => VisitorWorker(node, /*expressionResultIsUnused*/ false);

    /// <summary>ported from: src/compiler/transformers/es2018.ts:241-243 — <c>visitorWithUnusedExpressionResult</c>.</summary>
    private object? VisitorWithUnusedExpressionResult(Node node) => VisitorWorker(node, /*expressionResultIsUnused*/ true);

    /// <summary>ported from: src/compiler/transformers/es2018.ts:245-250 — <c>visitorNoAsyncModifier</c>.</summary>
    private object? VisitorNoAsyncModifier(Node node)
        => node.Kind == SyntaxKind.AsyncKeyword ? null : node;

    /// <summary>ported from: src/compiler/transformers/es2018.ts:252-260 — <c>doWithHierarchyFacts</c>.</summary>
    private TOut DoWithHierarchyFacts<TIn, TOut>(Func<TIn, TOut> cb, TIn value, HierarchyFacts excludeFacts, HierarchyFacts includeFacts)
    {
        if (AffectsSubtree(excludeFacts, includeFacts))
        {
            var ancestorFacts = EnterSubtree(excludeFacts, includeFacts);
            var result = cb(value);
            ExitSubtree(ancestorFacts);
            return result;
        }
        return cb(value);
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:262-264 — <c>visitDefault</c>.</summary>
    private object? VisitDefault(Node node) => Utilities.VisitEachChildWorker(node, Visitor, _context);

    /// <summary>ported from: src/compiler/transformers/es2018.ts:270-393 — <c>visitorWorker</c>.</summary>
    private object? VisitorWorker(Node node, bool expressionResultIsUnused)
    {
        if ((node.TransformFlags & TransformFlags.ContainsES2018) == 0)
        {
            return node;
        }
        switch (node.Kind)
        {
            case SyntaxKind.AwaitExpression:
                return VisitAwaitExpression((AwaitExpression)node);
            case SyntaxKind.YieldExpression:
                return VisitYieldExpression((YieldExpression)node);
            case SyntaxKind.ReturnStatement:
                return VisitReturnStatement((ReturnStatement)node);
            case SyntaxKind.LabeledStatement:
                return VisitLabeledStatement((LabeledStatement)node);
            case SyntaxKind.ObjectLiteralExpression:
                return VisitObjectLiteralExpression((ObjectLiteralExpression)node);
            case SyntaxKind.BinaryExpression:
                return VisitBinaryExpression((BinaryExpression)node, expressionResultIsUnused);
            case SyntaxKind.CommaListExpression:
                return VisitCommaListExpression((CommaListExpression)node, expressionResultIsUnused);
            case SyntaxKind.CatchClause:
                return VisitCatchClause((CatchClause)node);
            case SyntaxKind.VariableStatement:
                return VisitVariableStatement((VariableStatement)node);
            case SyntaxKind.VariableDeclaration:
                return VisitVariableDeclaration((VariableDeclaration)node);
            case SyntaxKind.DoStatement:
            case SyntaxKind.WhileStatement:
            case SyntaxKind.ForInStatement:
                return DoWithHierarchyFacts(
                    VisitDefault,
                    node,
                    HierarchyFacts.IterationStatementExcludes,
                    HierarchyFacts.IterationStatementIncludes);
            case SyntaxKind.ForOfStatement:
                return VisitForOfStatement((ForOfStatement)node, /*outermostLabeledStatement*/ null);
            case SyntaxKind.ForStatement:
                return DoWithHierarchyFacts(
                    VisitForStatement,
                    (ForStatement)node,
                    HierarchyFacts.IterationStatementExcludes,
                    HierarchyFacts.IterationStatementIncludes);
            case SyntaxKind.VoidExpression:
                return VisitVoidExpression((VoidExpression)node);
            case SyntaxKind.Constructor:
                return DoWithHierarchyFacts(
                    VisitConstructorDeclaration,
                    (ConstructorDeclaration)node,
                    HierarchyFacts.ClassOrFunctionExcludes,
                    HierarchyFacts.ClassOrFunctionIncludes);
            case SyntaxKind.MethodDeclaration:
                return DoWithHierarchyFacts(
                    VisitMethodDeclaration,
                    (MethodDeclaration)node,
                    HierarchyFacts.ClassOrFunctionExcludes,
                    HierarchyFacts.ClassOrFunctionIncludes);
            case SyntaxKind.GetAccessor:
                return DoWithHierarchyFacts(
                    VisitGetAccessorDeclaration,
                    (GetAccessorDeclaration)node,
                    HierarchyFacts.ClassOrFunctionExcludes,
                    HierarchyFacts.ClassOrFunctionIncludes);
            case SyntaxKind.SetAccessor:
                return DoWithHierarchyFacts(
                    VisitSetAccessorDeclaration,
                    (SetAccessorDeclaration)node,
                    HierarchyFacts.ClassOrFunctionExcludes,
                    HierarchyFacts.ClassOrFunctionIncludes);
            case SyntaxKind.FunctionDeclaration:
                return DoWithHierarchyFacts(
                    VisitFunctionDeclaration,
                    (FunctionDeclaration)node,
                    HierarchyFacts.ClassOrFunctionExcludes,
                    HierarchyFacts.ClassOrFunctionIncludes);
            case SyntaxKind.FunctionExpression:
                return DoWithHierarchyFacts(
                    VisitFunctionExpression,
                    (FunctionExpression)node,
                    HierarchyFacts.ClassOrFunctionExcludes,
                    HierarchyFacts.ClassOrFunctionIncludes);
            case SyntaxKind.ArrowFunction:
                return DoWithHierarchyFacts(
                    VisitArrowFunction,
                    (ArrowFunction)node,
                    HierarchyFacts.ArrowFunctionExcludes,
                    HierarchyFacts.ArrowFunctionIncludes);
            case SyntaxKind.Parameter:
                return VisitParameter((ParameterDeclaration)node);
            case SyntaxKind.ExpressionStatement:
                return VisitExpressionStatement((ExpressionStatement)node);
            case SyntaxKind.ParenthesizedExpression:
                return VisitParenthesizedExpression((ParenthesizedExpression)node, expressionResultIsUnused);
            case SyntaxKind.TaggedTemplateExpression:
                return VisitTaggedTemplateExpression((TaggedTemplateExpression)node);
            case SyntaxKind.PropertyAccessExpression:
                if (_capturedSuperProperties is not null && NodeTests.IsPropertyAccessExpression(node) && ((PropertyAccessExpression)node).Expression!.Kind == SyntaxKind.SuperKeyword)
                {
                    _capturedSuperProperties.Add(((Identifier)((PropertyAccessExpression)node).Name!).EscapedText!);
                }
                return Utilities.VisitEachChildWorker(node, Visitor, _context);
            case SyntaxKind.ElementAccessExpression:
                if (_capturedSuperProperties is not null && ((ElementAccessExpression)node).Expression!.Kind == SyntaxKind.SuperKeyword)
                {
                    _hasSuperElementAccess = true;
                }
                return Utilities.VisitEachChildWorker(node, Visitor, _context);
            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.ClassExpression:
                return DoWithHierarchyFacts(
                    VisitDefault,
                    node,
                    HierarchyFacts.ClassOrFunctionExcludes,
                    HierarchyFacts.ClassOrFunctionIncludes);
            default:
                return Utilities.VisitEachChildWorker(node, Visitor, _context);
        }
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:395-406 — <c>visitAwaitExpression</c>.</summary>
    private Expression VisitAwaitExpression(AwaitExpression node)
    {
        if ((_enclosingFunctionFlags & FunctionFlags.Async) != 0 && (_enclosingFunctionFlags & FunctionFlags.Generator) != 0)
        {
            return NodeFactory.SetOriginalNode(
                Utilities.SetTextRange(
                    _factory.CreateYieldExpression(
                        /*asteriskToken*/ null,
                        _emitHelpers.CreateAwaitHelper(Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression)!)),
                    /*location*/ node),
                node);
        }
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:408-456 — <c>visitYieldExpression</c>.</summary>
    private object? VisitYieldExpression(YieldExpression node)
    {
        if ((_enclosingFunctionFlags & FunctionFlags.Async) != 0 && (_enclosingFunctionFlags & FunctionFlags.Generator) != 0)
        {
            if (node.AsteriskToken is not null)
            {
                var expression = Debug.CheckDefined(Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression));

                return NodeFactory.SetOriginalNode(
                    Utilities.SetTextRange(
                        _factory.CreateYieldExpression(
                            /*asteriskToken*/ null,
                            _emitHelpers.CreateAwaitHelper(
                                _factory.UpdateYieldExpression(
                                    node,
                                    node.AsteriskToken,
                                    Utilities.SetTextRange(
                                        _emitHelpers.CreateAsyncDelegatorHelper(
                                            Utilities.SetTextRange(
                                                _emitHelpers.CreateAsyncValuesHelper(expression),
                                                expression)),
                                        expression)))),
                        node),
                    node);
            }

            return NodeFactory.SetOriginalNode(
                Utilities.SetTextRange(
                    _factory.CreateYieldExpression(
                        /*asteriskToken*/ null,
                        CreateDownlevelAwait(
                            node.Expression is not null
                                ? Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression)!
                                : _factory.CreateVoidZero())),
                    node),
                node);
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:458-469 — <c>visitReturnStatement</c>.</summary>
    private ReturnStatement VisitReturnStatement(ReturnStatement node)
    {
        if ((_enclosingFunctionFlags & FunctionFlags.Async) != 0 && (_enclosingFunctionFlags & FunctionFlags.Generator) != 0)
        {
            return _factory.UpdateReturnStatement(
                node,
                CreateDownlevelAwait(
                    node.Expression is not null
                        ? Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression)!
                        : _factory.CreateVoidZero()));
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:471-480 — <c>visitLabeledStatement</c>.</summary>
    private object? VisitLabeledStatement(LabeledStatement node)
    {
        if ((_enclosingFunctionFlags & FunctionFlags.Async) != 0)
        {
            var statement = ES2015TransformSupport.UnwrapInnermostStatementOfLabel(node);
            if (statement.Kind == SyntaxKind.ForOfStatement && ((ForOfStatement)statement).AwaitModifier is not null)
            {
                return VisitForOfStatement((ForOfStatement)statement, node);
            }
            return _factory.RestoreEnclosingLabel(Utilities.VisitNode(statement, Visitor, Utilities.IsStatement, _liftToBlock), node, /*afterRestoreLabelCallback*/ null);
        }
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:482-508 — <c>chunkObjectLiteralElements</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>readonly ObjectLiteralElementLike[]</c> is the port's
    /// <c>IList&lt;Node&gt;</c> (<c>ObjectLiteralExpression.Properties</c>' element type), and its
    /// <c>objects: Expression[]</c> is a <c>List&lt;Expression&gt;</c>.</remarks>
    private List<Expression> ChunkObjectLiteralElements(IList<Node> elements)
    {
        List<Node>? chunkObject = null;
        var objects = new List<Expression>();
        foreach (var e in elements)
        {
            if (e.Kind == SyntaxKind.SpreadAssignment)
            {
                if (chunkObject is not null)
                {
                    objects.Add(_factory.CreateObjectLiteralExpression(chunkObject, /*multiLine*/ null));
                    chunkObject = null;
                }
                var target = ((SpreadAssignment)e).Expression;
                objects.Add(Utilities.VisitNode(target, Visitor, Utilities.IsExpression)!);
            }
            else
            {
                Node appended = e.Kind == SyntaxKind.PropertyAssignment
                    ? _factory.CreatePropertyAssignment(
                        ((PropertyAssignment)e).Name,
                        Utilities.VisitNode(((PropertyAssignment)e).Initializer, Visitor, Utilities.IsExpression))
                    : Utilities.VisitNode(e, Visitor, Utilities.IsObjectLiteralElementLike)!;
                chunkObject = (List<Node>?)Core.Append(chunkObject, appended);
            }
        }
        if (chunkObject is not null)
        {
            objects.Add(_factory.CreateObjectLiteralExpression(chunkObject, /*multiLine*/ null));
        }

        return objects;
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:510-549 — <c>visitObjectLiteralExpression</c>.</summary>
    /// <remarks>
    /// <para>spread elements emit like so: non-spread elements are chunked together into object literals, and then all
    /// are passed to <c>__assign</c>: <c>{ a, ...o, b } =&gt; __assign(__assign({a}, o), {b});</c>. If the first element
    /// is a spread element, then the first argument to <c>__assign</c> is <c>{}</c>:
    /// <c>{ ...o, a, b, ...o2 } =&gt; __assign(__assign(__assign({}, o), {a, b}), o2)</c></para>
    /// <para>
    /// We cannot call <c>__assign</c> with more than two elements, since any element could cause side effects. For
    /// example:
    /// </para>
    /// <code>
    /// var k = { a: 1, b: 2 };
    /// var o = { a: 3, ...k, b: k.a++ };
    /// // expected: { a: 1, b: 1 }
    /// </code>
    /// <para>
    /// If we translate the above to <c>__assign({ a: 3 }, k, { b: k.a++ })</c>, the <c>k.a++</c> will evaluate before
    /// <c>k</c> is spread and we end up with <c>{ a: 2, b: 1 }</c>. This also occurs for spread elements, not just
    /// property assignments:
    /// </para>
    /// <code>
    /// var k = { a: 1, get b() { l = { z: 9 }; return 2; } };
    /// var l = { c: 3 };
    /// var o = { ...k, ...l };
    /// // expected: { a: 1, b: 2, z: 9 }
    /// </code>
    /// <para>
    /// If we translate the above to <c>__assign({}, k, l)</c>, the <c>l</c> will evaluate before <c>k</c> is spread and
    /// we end up with <c>{ a: 1, b: 2, c: 3 }</c>.
    /// </para>
    /// </remarks>
    private Expression VisitObjectLiteralExpression(ObjectLiteralExpression node)
    {
        if ((node.TransformFlags & TransformFlags.ContainsObjectRestOrSpread) != 0)
        {
            var objects = ChunkObjectLiteralElements(node.Properties!);
            if (objects.Count != 0 && objects[0].Kind != SyntaxKind.ObjectLiteralExpression)
            {
                objects.Insert(0, _factory.CreateObjectLiteralExpression(/*properties*/ null, /*multiLine*/ null));
            }
            var expression = objects[0];
            if (objects.Count > 1)
            {
                for (var i = 1; i < objects.Count; i++)
                {
                    expression = _emitHelpers.CreateAssignHelper([expression, objects[i]]);
                }
                return expression;
            }
            else
            {
                return _emitHelpers.CreateAssignHelper(objects);
            }
        }
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:551-553 — <c>visitExpressionStatement</c>.</summary>
    private ExpressionStatement VisitExpressionStatement(ExpressionStatement node)
        => Utilities.VisitEachChildWorker(node, VisitorWithUnusedExpressionResult, _context);

    /// <summary>ported from: src/compiler/transformers/es2018.ts:559-561 — <c>visitParenthesizedExpression</c>.</summary>
    private ParenthesizedExpression VisitParenthesizedExpression(ParenthesizedExpression node, bool expressionResultIsUnused)
        => Utilities.VisitEachChildWorker(node, expressionResultIsUnused ? VisitorWithUnusedExpressionResult : Visitor, _context);

    /// <summary>ported from: src/compiler/transformers/es2018.ts:563-581 — <c>visitSourceFile</c>.</summary>
    private SourceFile VisitSourceFile(SourceFile node)
    {
        var ancestorFacts = EnterSubtree(
            HierarchyFacts.SourceFileExcludes,
            Utilities.IsEffectiveStrictModeSourceFile(node, _compilerOptions)
                ? HierarchyFacts.StrictModeSourceFileIncludes
                : HierarchyFacts.SourceFileIncludes);
        _exportedVariableStatement = false;
        var visited = Utilities.VisitEachChildWorker(node, Visitor, _context);
        var statement = Core.Concatenate(
            visited.Statements,
            _taggedTemplateStringDeclarations is not null
                ? new List<IStatement>
                {
                    _factory.CreateVariableStatement(
                        /*modifiers*/ null,
                        _factory.CreateVariableDeclarationList(_taggedTemplateStringDeclarations, /*flags*/ null)),
                }
                : null);
        var result = _factory.UpdateSourceFile(
            visited,
            Utilities.SetTextRange(_factory.CreateNodeArray(statement, /*hasTrailingComma*/ null), node.Statements!),
            /*isDeclarationFile*/ null,
            /*referencedFiles*/ null,
            /*typeReferenceDirectives*/ null,
            /*hasNoDefaultLib*/ null,
            /*libReferenceDirectives*/ null);
        ExitSubtree(ancestorFacts);
        return result;
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:583-591 — <c>visitTaggedTemplateExpression</c>.</summary>
    private LeftHandSideExpression VisitTaggedTemplateExpression(TaggedTemplateExpression node)
        => TaggedTemplate.ProcessTaggedTemplateExpression(
            _context,
            node,
            Visitor,
            _currentSourceFile!,
            RecordTaggedTemplateString,
            ProcessLevel.LiftRestriction);

    /// <summary>ported from: src/compiler/transformers/es2018.ts:601-624 — <c>visitBinaryExpression</c>.</summary>
    private Expression VisitBinaryExpression(BinaryExpression node, bool expressionResultIsUnused)
    {
        if (Utilities.IsDestructuringAssignment(node) && FactoryUtilities.ContainsObjectRestOrSpread(node.Left!))
        {
            return Destructuring.FlattenDestructuringAssignment(
                node,
                Visitor,
                _context,
                FlattenLevel.ObjectRest,
                !expressionResultIsUnused);
        }
        if (node.OperatorToken!.Kind == SyntaxKind.CommaToken)
        {
            return _factory.UpdateBinaryExpression(
                node,
                Utilities.VisitNode(node.Left, VisitorWithUnusedExpressionResult, Utilities.IsExpression),
                node.OperatorToken,
                Utilities.VisitNode(node.Right, expressionResultIsUnused ? VisitorWithUnusedExpressionResult : Visitor, Utilities.IsExpression));
        }
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:626-641 — <c>visitCommaListExpression</c>.</summary>
    private Expression VisitCommaListExpression(CommaListExpression node, bool expressionResultIsUnused)
    {
        if (expressionResultIsUnused)
        {
            return Utilities.VisitEachChildWorker(node, VisitorWithUnusedExpressionResult, _context);
        }
        var elements = node.Elements!;
        List<Expression>? result = null;
        for (var i = 0; i < elements.Count; i++)
        {
            var element = elements[i];
            var visited = Utilities.VisitNode(element, i < elements.Count - 1 ? VisitorWithUnusedExpressionResult : Visitor, Utilities.IsExpression)!;
            if (result is not null || !ReferenceEquals(visited, element))
            {
                result ??= elements.GetRange(0, i);
                result.Add(visited);
            }
        }
        var updatedElements = result is not null
            ? Utilities.SetTextRange(_factory.CreateNodeArray(result, /*hasTrailingComma*/ null), elements)
            : elements;
        return _factory.UpdateCommaListExpression(node, updatedElements);
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:643-666 — <c>visitCatchClause</c>.</summary>
    private CatchClause VisitCatchClause(CatchClause node)
    {
        if (
            node.VariableDeclaration is not null
            && Utilities.IsBindingPattern(node.VariableDeclaration.Name!)
            && (node.VariableDeclaration.Name!.TransformFlags & TransformFlags.ContainsObjectRestOrSpread) != 0)
        {
            var name = _factory.GetGeneratedNameForNode(node.VariableDeclaration.Name, /*flags*/ null);
            var updatedDecl = _factory.UpdateVariableDeclaration(
                node.VariableDeclaration,
                node.VariableDeclaration.Name,
                /*exclamationToken*/ null,
                /*type*/ null,
                name);
            var visitedBindings = Destructuring.FlattenDestructuringBinding(updatedDecl, Visitor, _context, FlattenLevel.ObjectRest);
            var block = Utilities.VisitNode(node.Block, Visitor, NodeTests.IsBlock)!;
            if (Core.Some(visitedBindings))
            {
                block = _factory.UpdateBlock(block, [
                    _factory.CreateVariableStatement(/*modifiers*/ null, visitedBindings),
                    .. block.Statements!,
                ]);
            }
            return _factory.UpdateCatchClause(
                node,
                _factory.UpdateVariableDeclaration(
                    node.VariableDeclaration,
                    name,
                    /*exclamationToken*/ null,
                    /*type*/ null,
                    /*initializer*/ null),
                block);
        }
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:668-682 — <c>visitVariableStatement</c>.</summary>
    private object? VisitVariableStatement(VariableStatement node)
    {
        if (Utilities.HasSyntacticModifier(node, ModifierFlags.Export))
        {
            var savedExportedVariableStatement = _exportedVariableStatement;
            _exportedVariableStatement = true;
            var visited = Utilities.VisitEachChildWorker(node, Visitor, _context);
            _exportedVariableStatement = savedExportedVariableStatement;
            return visited;
        }
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:684-693 — <c>visitVariableDeclaration</c>.</summary>
    private object? VisitVariableDeclaration(VariableDeclaration node)
    {
        if (_exportedVariableStatement)
        {
            var savedExportedVariableStatement = _exportedVariableStatement;
            _exportedVariableStatement = false;
            var visited = VisitVariableDeclarationWorker(node, /*exportedVariableStatement*/ true);
            _exportedVariableStatement = savedExportedVariableStatement;
            return visited;
        }
        return VisitVariableDeclarationWorker(node, /*exportedVariableStatement*/ false);
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:695-708 — <c>visitVariableDeclarationWorker</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's sixth positional argument is <c>flattenDestructuringBinding</c>'s
    /// <c>hoistTempVariables</c> slot, which this call fills with <c>exportedVariableStatement</c> — the port passes the
    /// same value to the same slot.</remarks>
    private object? VisitVariableDeclarationWorker(VariableDeclaration node, bool exportedVariableStatement)
    {
        // If we are here it is because the name contains a binding pattern with a rest somewhere in it.
        if (Utilities.IsBindingPattern(node.Name!) && (node.Name!.TransformFlags & TransformFlags.ContainsObjectRestOrSpread) != 0)
        {
            return Destructuring.FlattenDestructuringBinding(
                node,
                Visitor,
                _context,
                FlattenLevel.ObjectRest,
                /*rval*/ null,
                exportedVariableStatement);
        }
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:710-718 — <c>visitForStatement</c>.</summary>
    private IStatement VisitForStatement(ForStatement node)
        => _factory.UpdateForStatement(
            node,
            Utilities.VisitNode(node.Initializer, VisitorWithUnusedExpressionResult, Utilities.IsForInitializer),
            Utilities.VisitNode(node.Condition, Visitor, Utilities.IsExpression),
            Utilities.VisitNode(node.Incrementor, VisitorWithUnusedExpressionResult, Utilities.IsExpression),
            Utilities.VisitIterationBody(node.Statement!, Visitor, _context));

    /// <summary>ported from: src/compiler/transformers/es2018.ts:720-722 — <c>visitVoidExpression</c>.</summary>
    private VoidExpression VisitVoidExpression(VoidExpression node)
        => Utilities.VisitEachChildWorker(node, VisitorWithUnusedExpressionResult, _context);

    /// <summary>ported from: src/compiler/transformers/es2018.ts:729-742 — <c>visitForOfStatement</c>.</summary>
    private IStatement VisitForOfStatement(ForOfStatement node, LabeledStatement? outermostLabeledStatement)
    {
        var ancestorFacts = EnterSubtree(HierarchyFacts.IterationStatementExcludes, HierarchyFacts.IterationStatementIncludes);
        if (
            (node.Initializer!.TransformFlags & TransformFlags.ContainsObjectRestOrSpread) != 0
            || Utilities.IsAssignmentPattern(node.Initializer) && FactoryUtilities.ContainsObjectRestOrSpread(node.Initializer))
        {
            node = TransformForOfStatementWithObjectRest(node);
        }
        var result = node.AwaitModifier is not null
            ? TransformForAwaitOfStatement(node, outermostLabeledStatement, ancestorFacts)
            : _factory.RestoreEnclosingLabel(Utilities.VisitEachChildWorker(node, Visitor, _context), outermostLabeledStatement, /*afterRestoreLabelCallback*/ null);
        ExitSubtree(ancestorFacts);
        return result;
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:744-784 — <c>transformForOfStatementWithObjectRest</c>.</summary>
    private ForOfStatement TransformForOfStatementWithObjectRest(ForOfStatement node)
    {
        var initializerWithoutParens = Utilities.SkipParentheses(node.Initializer!);
        if (NodeTests.IsVariableDeclarationList(initializerWithoutParens) || Utilities.IsAssignmentPattern(initializerWithoutParens))
        {
            ITextRange? bodyLocation = null;
            ITextRange? statementsLocation = null;
            var temp = _factory.CreateTempVariable(/*recordTempVariable*/ null, /*reservedInNestedScopes*/ null);
            var statements = new List<IStatement> { FactoryUtilities.CreateForOfBindingStatement(_factory, initializerWithoutParens, temp) };
            if (NodeTests.IsBlock((Node)node.Statement!))
            {
                var statementBlock = (Block)node.Statement!;
                Core.AddRange(statements, statementBlock.Statements);
                bodyLocation = statementBlock;
                statementsLocation = statementBlock.Statements;
            }
            else if (node.Statement is not null)
            {
                statements.Add(node.Statement);
                bodyLocation = (ITextRange)(Node)node.Statement;
                statementsLocation = bodyLocation;
            }
            return _factory.UpdateForOfStatement(
                node,
                node.AwaitModifier,
                Utilities.SetTextRange(
                    _factory.CreateVariableDeclarationList(
                        [
                            Utilities.SetTextRange(_factory.CreateVariableDeclaration(temp, /*exclamationToken*/ null, /*type*/ null, /*initializer*/ null), node.Initializer!),
                        ],
                        NodeFlags.Let),
                    node.Initializer),
                node.Expression,
                Utilities.SetTextRange(
                    _factory.CreateBlock(
                        Utilities.SetTextRange(_factory.CreateNodeArray(statements, /*hasTrailingComma*/ null), statementsLocation),
                        /*multiLine*/ true),
                    bodyLocation));
        }
        return node;
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:786-819 — <c>convertForOfStatementHead</c>.</summary>
    private Block ConvertForOfStatementHead(ForOfStatement node, Expression boundValue, Identifier nonUserCode)
    {
        var value = _factory.CreateTempVariable(_context.HoistVariableDeclaration, /*reservedInNestedScopes*/ null);
        var iteratorValueExpression = _factory.CreateAssignment(value, boundValue);
        var iteratorValueStatement = _factory.CreateExpressionStatement(iteratorValueExpression);
        EmitNodes.SetSourceMapRange(iteratorValueStatement, TypeScriptTransformSupport.SourceMapRangeOf(node.Expression!));

        var exitNonUserCodeExpression = _factory.CreateAssignment(nonUserCode, _factory.CreateFalse());
        var exitNonUserCodeStatement = _factory.CreateExpressionStatement(exitNonUserCodeExpression);
        EmitNodes.SetSourceMapRange(exitNonUserCodeStatement, TypeScriptTransformSupport.SourceMapRangeOf(node.Expression!));

        var statements = new List<IStatement> { iteratorValueStatement, exitNonUserCodeStatement };
        var binding = FactoryUtilities.CreateForOfBindingStatement(_factory, node.Initializer!, value);
        statements.Add(Utilities.VisitNode(binding, Visitor, Utilities.IsStatement)!);

        ITextRange? bodyLocation = null;
        ITextRange? statementsLocation = null;
        var statement = Utilities.VisitIterationBody(node.Statement!, Visitor, _context);
        if (NodeTests.IsBlock((Node)statement))
        {
            var statementBlock = (Block)statement;
            Core.AddRange(statements, statementBlock.Statements);
            bodyLocation = statementBlock;
            statementsLocation = statementBlock.Statements;
        }
        else
        {
            statements.Add(statement);
        }

        return Utilities.SetTextRange(
            _factory.CreateBlock(
                Utilities.SetTextRange(_factory.CreateNodeArray(statements, /*hasTrailingComma*/ null), statementsLocation),
                /*multiLine*/ true),
            bodyLocation);
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:821-825 — <c>createDownlevelAwait</c>.</summary>
    private Expression CreateDownlevelAwait(Expression expression)
        => (_enclosingFunctionFlags & FunctionFlags.Generator) != 0
            ? _factory.CreateYieldExpression(/*asteriskToken*/ null, _emitHelpers.CreateAwaitHelper(expression))
            : _factory.CreateAwaitExpression(expression);

    /// <summary>ported from: src/compiler/transformers/es2018.ts:827-939 — <c>transformForAwaitOfStatement</c>.</summary>
    private IStatement TransformForAwaitOfStatement(ForOfStatement node, LabeledStatement? outermostLabeledStatement, HierarchyFacts ancestorFacts)
    {
        var expression = Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression)!;
        var iterator = NodeTests.IsIdentifier(expression) ? _factory.GetGeneratedNameForNode(expression, /*flags*/ null) : _factory.CreateTempVariable(/*recordTempVariable*/ null, /*reservedInNestedScopes*/ null);
        var result = NodeTests.IsIdentifier(expression) ? _factory.GetGeneratedNameForNode(iterator, /*flags*/ null) : _factory.CreateTempVariable(/*recordTempVariable*/ null, /*reservedInNestedScopes*/ null);
        var nonUserCode = _factory.CreateTempVariable(/*recordTempVariable*/ null, /*reservedInNestedScopes*/ null);
        var done = _factory.CreateTempVariable(_context.HoistVariableDeclaration, /*reservedInNestedScopes*/ null);
        var errorRecord = _factory.CreateUniqueName("e", /*flags*/ null);
        var catchVariable = _factory.GetGeneratedNameForNode(errorRecord, /*flags*/ null);
        var returnMethod = _factory.CreateTempVariable(/*recordTempVariable*/ null, /*reservedInNestedScopes*/ null);
        var callValues = Utilities.SetTextRange(_emitHelpers.CreateAsyncValuesHelper(expression), node.Expression!);
        var callNext = _factory.CreateCallExpression(_factory.CreatePropertyAccessExpression(iterator, "next"), /*typeArguments*/ null, []);
        var getDone = _factory.CreatePropertyAccessExpression(result, "done");
        var getValue = _factory.CreatePropertyAccessExpression(result, "value");
        var callReturn = _factory.CreateFunctionCallCall(returnMethod, iterator, []);

        _context.HoistVariableDeclaration(errorRecord);
        _context.HoistVariableDeclaration(returnMethod);

        // if we are enclosed in an outer loop ensure we reset 'errorRecord' per each iteration
        var initializer = (ancestorFacts & HierarchyFacts.IterationContainer) != 0
            ? _factory.InlineExpressions([_factory.CreateAssignment(errorRecord, _factory.CreateVoidZero()), callValues])
            : callValues;

        var forStatement = EmitNodes.SetEmitFlags(
            Utilities.SetTextRange(
                _factory.CreateForStatement(
                    /*initializer*/ EmitNodes.SetEmitFlags(
                        Utilities.SetTextRange(
                            _factory.CreateVariableDeclarationList(
                                [
                                    _factory.CreateVariableDeclaration(nonUserCode, /*exclamationToken*/ null, /*type*/ null, _factory.CreateTrue()),
                                    Utilities.SetTextRange(_factory.CreateVariableDeclaration(iterator, /*exclamationToken*/ null, /*type*/ null, initializer), node.Expression!),
                                    _factory.CreateVariableDeclaration(result, /*exclamationToken*/ null, /*type*/ null, /*initializer*/ null),
                                ],
                                /*flags*/ null),
                            node.Expression!),
                        EmitFlags.NoHoisting),
                    /*condition*/ _factory.InlineExpressions([
                        _factory.CreateAssignment(result, CreateDownlevelAwait(callNext)),
                        _factory.CreateAssignment(done, getDone),
                        _factory.CreateLogicalNot(done),
                    ]),
                    /*incrementor*/ _factory.CreateAssignment(nonUserCode, _factory.CreateTrue()),
                    /*statement*/ ConvertForOfStatementHead(node, getValue, nonUserCode)),
                /*location*/ node),
            EmitFlags.NoTokenTrailingSourceMaps);
        NodeFactory.SetOriginalNode(forStatement, node);

        return _factory.CreateTryStatement(
            _factory.CreateBlock([
                _factory.RestoreEnclosingLabel(
                    forStatement,
                    outermostLabeledStatement,
                    /*afterRestoreLabelCallback*/ null),
            ], /*multiLine*/ null),
            _factory.CreateCatchClause(
                _factory.CreateVariableDeclaration(catchVariable, /*exclamationToken*/ null, /*type*/ null, /*initializer*/ null),
                EmitNodes.SetEmitFlags(
                    _factory.CreateBlock([
                        _factory.CreateExpressionStatement(
                            _factory.CreateAssignment(
                                errorRecord,
                                _factory.CreateObjectLiteralExpression([
                                    _factory.CreatePropertyAssignment("error", catchVariable),
                                ], /*multiLine*/ null))),
                    ], /*multiLine*/ null),
                    EmitFlags.SingleLine)),
            _factory.CreateBlock([
                _factory.CreateTryStatement(
                    /*tryBlock*/ _factory.CreateBlock([
                        EmitNodes.SetEmitFlags(
                            _factory.CreateIfStatement(
                                _factory.CreateLogicalAnd(
                                    _factory.CreateLogicalAnd(
                                        _factory.CreateLogicalNot(nonUserCode),
                                        _factory.CreateLogicalNot(done)),
                                    _factory.CreateAssignment(
                                        returnMethod,
                                        _factory.CreatePropertyAccessExpression(iterator, "return"))),
                                _factory.CreateExpressionStatement(CreateDownlevelAwait(callReturn)),
                                /*elseStatement*/ null),
                            EmitFlags.SingleLine),
                    ], /*multiLine*/ null),
                    /*catchClause*/ null,
                    /*finallyBlock*/ EmitNodes.SetEmitFlags(
                        _factory.CreateBlock([
                            EmitNodes.SetEmitFlags(
                                _factory.CreateIfStatement(
                                    errorRecord,
                                    _factory.CreateThrowStatement(
                                        _factory.CreatePropertyAccessExpression(errorRecord, "error")),
                                    /*elseStatement*/ null),
                                EmitFlags.SingleLine),
                        ], /*multiLine*/ null),
                        EmitFlags.SingleLine)),
            ], /*multiLine*/ null));
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:941-944 — <c>parameterVisitor</c>.</summary>
    private object? ParameterVisitor(Node node)
    {
        Debug.AssertNode(node, NodeTests.IsParameter);
        return VisitParameter((ParameterDeclaration)node);
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:946-972 — <c>visitParameter</c>.</summary>
    private ParameterDeclaration VisitParameter(ParameterDeclaration node)
    {
        if (_parametersWithPrecedingObjectRestOrSpread?.Contains(node) == true)
        {
            return _factory.UpdateParameterDeclaration(
                node,
                /*modifiers*/ null,
                node.DotDotDotToken,
                Utilities.IsBindingPattern(node.Name!) ? _factory.GetGeneratedNameForNode(node, /*flags*/ null) : node.Name,
                /*questionToken*/ null,
                /*type*/ null,
                /*initializer*/ null);
        }
        if ((node.TransformFlags & TransformFlags.ContainsObjectRestOrSpread) != 0)
        {
            // Binding patterns are converted into a generated name and are
            // evaluated inside the function body.
            return _factory.UpdateParameterDeclaration(
                node,
                /*modifiers*/ null,
                node.DotDotDotToken,
                _factory.GetGeneratedNameForNode(node, /*flags*/ null),
                /*questionToken*/ null,
                /*type*/ null,
                Utilities.VisitNode(node.Initializer, Visitor, Utilities.IsExpression));
        }
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:974-985 — <c>collectParametersWithPrecedingObjectRestOrSpread</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's parameter is the <c>SignatureDeclaration</c> union; the port takes the node and
    /// reads its parameter list through <c>ISignatureDeclarationBase</c>.</remarks>
    private HashSet<ParameterDeclaration>? CollectParametersWithPrecedingObjectRestOrSpread(Node node)
    {
        HashSet<ParameterDeclaration>? parameters = null;
        foreach (var parameter in ((ISignatureDeclarationBase)node).Parameters!)
        {
            if (parameters is not null)
            {
                parameters.Add(parameter);
            }
            else if ((parameter.TransformFlags & TransformFlags.ContainsObjectRestOrSpread) != 0)
            {
                parameters = [];
            }
        }
        return parameters;
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:987-1001 — <c>visitConstructorDeclaration</c>.</summary>
    private ConstructorDeclaration VisitConstructorDeclaration(ConstructorDeclaration node)
    {
        var savedEnclosingFunctionFlags = _enclosingFunctionFlags;
        var savedParametersWithPrecedingObjectRestOrSpread = _parametersWithPrecedingObjectRestOrSpread;
        _enclosingFunctionFlags = Utilities.GetFunctionFlags(node);
        _parametersWithPrecedingObjectRestOrSpread = CollectParametersWithPrecedingObjectRestOrSpread(node);
        var updated = _factory.UpdateConstructorDeclaration(
            node,
            node.Modifiers,
            Utilities.VisitParameterList(node.Parameters, ParameterVisitor, _context),
            (Block)TransformFunctionBody(node));
        _enclosingFunctionFlags = savedEnclosingFunctionFlags;
        _parametersWithPrecedingObjectRestOrSpread = savedParametersWithPrecedingObjectRestOrSpread;
        return updated;
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:1003-1019 — <c>visitGetAccessorDeclaration</c>.</summary>
    private GetAccessorDeclaration VisitGetAccessorDeclaration(GetAccessorDeclaration node)
    {
        var savedEnclosingFunctionFlags = _enclosingFunctionFlags;
        var savedParametersWithPrecedingObjectRestOrSpread = _parametersWithPrecedingObjectRestOrSpread;
        _enclosingFunctionFlags = Utilities.GetFunctionFlags(node);
        _parametersWithPrecedingObjectRestOrSpread = CollectParametersWithPrecedingObjectRestOrSpread(node);
        var updated = _factory.UpdateGetAccessorDeclaration(
            node,
            node.Modifiers,
            Utilities.VisitNode(node.Name, Visitor, Utilities.IsPropertyName),
            Utilities.VisitParameterList(node.Parameters, ParameterVisitor, _context),
            /*type*/ null,
            (Block)TransformFunctionBody(node));
        _enclosingFunctionFlags = savedEnclosingFunctionFlags;
        _parametersWithPrecedingObjectRestOrSpread = savedParametersWithPrecedingObjectRestOrSpread;
        return updated;
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:1021-1036 — <c>visitSetAccessorDeclaration</c>.</summary>
    private SetAccessorDeclaration VisitSetAccessorDeclaration(SetAccessorDeclaration node)
    {
        var savedEnclosingFunctionFlags = _enclosingFunctionFlags;
        var savedParametersWithPrecedingObjectRestOrSpread = _parametersWithPrecedingObjectRestOrSpread;
        _enclosingFunctionFlags = Utilities.GetFunctionFlags(node);
        _parametersWithPrecedingObjectRestOrSpread = CollectParametersWithPrecedingObjectRestOrSpread(node);
        var updated = _factory.UpdateSetAccessorDeclaration(
            node,
            node.Modifiers,
            Utilities.VisitNode(node.Name, Visitor, Utilities.IsPropertyName),
            Utilities.VisitParameterList(node.Parameters, ParameterVisitor, _context),
            (Block)TransformFunctionBody(node));
        _enclosingFunctionFlags = savedEnclosingFunctionFlags;
        _parametersWithPrecedingObjectRestOrSpread = savedParametersWithPrecedingObjectRestOrSpread;
        return updated;
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:1038-1065 — <c>visitMethodDeclaration</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>visitNode(/*node*/ undefined, visitor, isQuestionToken)</c> is the port's
    /// same call with the absent node stated as a typed <c>null</c> (the question token is always elided).</remarks>
    private MethodDeclaration VisitMethodDeclaration(MethodDeclaration node)
    {
        var savedEnclosingFunctionFlags = _enclosingFunctionFlags;
        var savedParametersWithPrecedingObjectRestOrSpread = _parametersWithPrecedingObjectRestOrSpread;
        _enclosingFunctionFlags = Utilities.GetFunctionFlags(node);
        _parametersWithPrecedingObjectRestOrSpread = CollectParametersWithPrecedingObjectRestOrSpread(node);
        var updated = _factory.UpdateMethodDeclaration(
            node,
            (_enclosingFunctionFlags & FunctionFlags.Generator) != 0
                ? Utilities.VisitNodes(node.Modifiers, VisitorNoAsyncModifier, Utilities.IsModifierLike)
                : node.Modifiers,
            (_enclosingFunctionFlags & FunctionFlags.Async) != 0
                ? null
                : node.AsteriskToken,
            Utilities.VisitNode(node.Name, Visitor, Utilities.IsPropertyName),
            Utilities.VisitNode((PunctuationToken?)null, Visitor, NodeTests.IsQuestionToken),
            /*typeParameters*/ null,
            (_enclosingFunctionFlags & FunctionFlags.Async) != 0 && (_enclosingFunctionFlags & FunctionFlags.Generator) != 0
                ? TransformAsyncGeneratorFunctionParameterList(node)
                : Utilities.VisitParameterList(node.Parameters, ParameterVisitor, _context),
            /*type*/ null,
            (_enclosingFunctionFlags & FunctionFlags.Async) != 0 && (_enclosingFunctionFlags & FunctionFlags.Generator) != 0
                ? TransformAsyncGeneratorFunctionBody(node)
                : (Block)TransformFunctionBody(node));
        _enclosingFunctionFlags = savedEnclosingFunctionFlags;
        _parametersWithPrecedingObjectRestOrSpread = savedParametersWithPrecedingObjectRestOrSpread;
        return updated;
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:1067-1093 — <c>visitFunctionDeclaration</c>.</summary>
    private FunctionDeclaration VisitFunctionDeclaration(FunctionDeclaration node)
    {
        var savedEnclosingFunctionFlags = _enclosingFunctionFlags;
        var savedParametersWithPrecedingObjectRestOrSpread = _parametersWithPrecedingObjectRestOrSpread;
        _enclosingFunctionFlags = Utilities.GetFunctionFlags(node);
        _parametersWithPrecedingObjectRestOrSpread = CollectParametersWithPrecedingObjectRestOrSpread(node);
        var updated = _factory.UpdateFunctionDeclaration(
            node,
            (_enclosingFunctionFlags & FunctionFlags.Generator) != 0
                ? Utilities.VisitNodes(node.Modifiers, VisitorNoAsyncModifier, Utilities.IsModifier)
                : node.Modifiers,
            (_enclosingFunctionFlags & FunctionFlags.Async) != 0
                ? null
                : node.AsteriskToken,
            node.Name,
            /*typeParameters*/ null,
            (_enclosingFunctionFlags & FunctionFlags.Async) != 0 && (_enclosingFunctionFlags & FunctionFlags.Generator) != 0
                ? TransformAsyncGeneratorFunctionParameterList(node)
                : Utilities.VisitParameterList(node.Parameters, ParameterVisitor, _context),
            /*type*/ null,
            (_enclosingFunctionFlags & FunctionFlags.Async) != 0 && (_enclosingFunctionFlags & FunctionFlags.Generator) != 0
                ? TransformAsyncGeneratorFunctionBody(node)
                : (Block)TransformFunctionBody(node));
        _enclosingFunctionFlags = savedEnclosingFunctionFlags;
        _parametersWithPrecedingObjectRestOrSpread = savedParametersWithPrecedingObjectRestOrSpread;
        return updated;
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:1095-1112 — <c>visitArrowFunction</c>.</summary>
    private ArrowFunction VisitArrowFunction(ArrowFunction node)
    {
        var savedEnclosingFunctionFlags = _enclosingFunctionFlags;
        var savedParametersWithPrecedingObjectRestOrSpread = _parametersWithPrecedingObjectRestOrSpread;
        _enclosingFunctionFlags = Utilities.GetFunctionFlags(node);
        _parametersWithPrecedingObjectRestOrSpread = CollectParametersWithPrecedingObjectRestOrSpread(node);
        var updated = _factory.UpdateArrowFunction(
            node,
            node.Modifiers,
            /*typeParameters*/ null,
            Utilities.VisitParameterList(node.Parameters, ParameterVisitor, _context),
            /*type*/ null,
            node.EqualsGreaterThanToken,
            TransformFunctionBody(node));
        _enclosingFunctionFlags = savedEnclosingFunctionFlags;
        _parametersWithPrecedingObjectRestOrSpread = savedParametersWithPrecedingObjectRestOrSpread;
        return updated;
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:1114-1140 — <c>visitFunctionExpression</c>.</summary>
    private FunctionExpression VisitFunctionExpression(FunctionExpression node)
    {
        var savedEnclosingFunctionFlags = _enclosingFunctionFlags;
        var savedParametersWithPrecedingObjectRestOrSpread = _parametersWithPrecedingObjectRestOrSpread;
        _enclosingFunctionFlags = Utilities.GetFunctionFlags(node);
        _parametersWithPrecedingObjectRestOrSpread = CollectParametersWithPrecedingObjectRestOrSpread(node);
        var updated = _factory.UpdateFunctionExpression(
            node,
            (_enclosingFunctionFlags & FunctionFlags.Generator) != 0
                ? Utilities.VisitNodes(node.Modifiers, VisitorNoAsyncModifier, Utilities.IsModifier)
                : node.Modifiers,
            (_enclosingFunctionFlags & FunctionFlags.Async) != 0
                ? null
                : node.AsteriskToken,
            node.Name,
            /*typeParameters*/ null,
            (_enclosingFunctionFlags & FunctionFlags.Async) != 0 && (_enclosingFunctionFlags & FunctionFlags.Generator) != 0
                ? TransformAsyncGeneratorFunctionParameterList(node)
                : Utilities.VisitParameterList(node.Parameters, ParameterVisitor, _context),
            /*type*/ null,
            (_enclosingFunctionFlags & FunctionFlags.Async) != 0 && (_enclosingFunctionFlags & FunctionFlags.Generator) != 0
                ? TransformAsyncGeneratorFunctionBody(node)
                : (Block)TransformFunctionBody(node));
        _enclosingFunctionFlags = savedEnclosingFunctionFlags;
        _parametersWithPrecedingObjectRestOrSpread = savedParametersWithPrecedingObjectRestOrSpread;
        return updated;
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:1142-1162 — <c>transformAsyncGeneratorFunctionParameterList</c>.</summary>
    private NodeArray<ParameterDeclaration> TransformAsyncGeneratorFunctionParameterList(Node node)
    {
        var parameters = ((ISignatureDeclarationBase)node).Parameters!;
        if (TransformationUtilities.IsSimpleParameterList(parameters))
        {
            return Utilities.VisitParameterList(parameters, Visitor, _context)!;
        }
        // Add fixed parameters to preserve the function's `length` property.
        var newParameters = new List<ParameterDeclaration>();
        foreach (var parameter in parameters)
        {
            if (parameter.Initializer is not null || parameter.DotDotDotToken is not null)
            {
                break;
            }
            var newParameter = _factory.CreateParameterDeclaration(
                /*modifiers*/ null,
                /*dotDotDotToken*/ null,
                _factory.GetGeneratedNameForNode(parameter.Name, GeneratedIdentifierFlags.ReservedInNestedScopes),
                /*questionToken*/ null,
                /*type*/ null,
                /*initializer*/ null);
            newParameters.Add(newParameter);
        }
        var newParametersArray = _factory.CreateNodeArray(newParameters, /*hasTrailingComma*/ null);
        Utilities.SetTextRange(newParametersArray, parameters);
        return newParametersArray;
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:1164-1218 — <c>transformAsyncGeneratorFunctionBody</c>.</summary>
    private Block TransformAsyncGeneratorFunctionBody(Node node)
    {
        var parameters = ((ISignatureDeclarationBase)node).Parameters!;
        var innerParameters = !TransformationUtilities.IsSimpleParameterList(parameters)
            ? Utilities.VisitParameterList(parameters, Visitor, _context)
            : null;
        _context.ResumeLexicalEnvironment();

        var savedCapturedSuperProperties = _capturedSuperProperties;
        var savedHasSuperElementAccess = _hasSuperElementAccess;
        _capturedSuperProperties = [];
        _hasSuperElementAccess = false;

        var outerStatements = new List<IStatement>();
        var body = (Block)((IFunctionLikeDeclarationBase)node).Body!;
        var asyncBody = _factory.UpdateBlock(body, Utilities.VisitNodes(body.Statements, Visitor, Utilities.IsStatement));
        asyncBody = _factory.UpdateBlock(asyncBody, _factory.MergeLexicalEnvironment(asyncBody.Statements, AppendObjectRestAssignmentsIfNeeded(_context.EndLexicalEnvironment(), node)));

        var returnStatement = _factory.CreateReturnStatement(
            _emitHelpers.CreateAsyncGeneratorHelper(
                _factory.CreateFunctionExpression(
                    /*modifiers*/ null,
                    (PunctuationToken)_factory.CreateToken(SyntaxKind.AsteriskToken),
                    ((INamedDeclaration)node).Name is not null
                        ? _factory.GetGeneratedNameForNode(((INamedDeclaration)node).Name, /*flags*/ null)
                        : null,
                    /*typeParameters*/ null,
                    innerParameters ?? [],
                    /*type*/ null,
                    asyncBody),
                (_hierarchyFacts & HierarchyFacts.HasLexicalThis) != 0));

        // Minor optimization, emit `_super` helper to capture `super` access in an arrow.
        // This step isn't needed if we eventually transform this to ES5.
        var emitSuperHelpers = _languageVersion >= ScriptTarget.ES2015
            && (_resolver.HasNodeCheckFlag(node, NodeCheckFlags.MethodWithSuperPropertyAssignmentInAsync) || _resolver.HasNodeCheckFlag(node, NodeCheckFlags.MethodWithSuperPropertyAccessInAsync));
        if (emitSuperHelpers)
        {
            EnableSubstitutionForAsyncMethodsWithSuper();
            var variableStatement = ES2017Transform.CreateSuperAccessVariableStatement(_factory, _resolver, node, _capturedSuperProperties);
            SetSubstitutedSuperAccessor(Utilities.GetNodeId(variableStatement), true);
            TypeScriptTransformSupport.InsertStatementsAfterStandardPrologue(outerStatements, [variableStatement]);
        }

        outerStatements.Add(returnStatement);

        var block = _factory.UpdateBlock(body, outerStatements);
        if (emitSuperHelpers && _hasSuperElementAccess)
        {
            if (_resolver.HasNodeCheckFlag(node, NodeCheckFlags.MethodWithSuperPropertyAssignmentInAsync))
            {
                EmitNodes.AddEmitHelper(block, EmitHelpers.AdvancedAsyncSuperHelper);
            }
            else if (_resolver.HasNodeCheckFlag(node, NodeCheckFlags.MethodWithSuperPropertyAccessInAsync))
            {
                EmitNodes.AddEmitHelper(block, EmitHelpers.AsyncSuperHelper);
            }
        }

        _capturedSuperProperties = savedCapturedSuperProperties;
        _hasSuperElementAccess = savedHasSuperElementAccess;

        return block;
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:1220-1240 — <c>transformFunctionBody</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's two overloads (a <c>FunctionBody</c> for the
    /// method/accessor/function/constructor arms and a <c>ConciseBody</c> for the arrow arm) are stated once here,
    /// answering the <see cref="Node"/> both answer; the non-arrow callers narrow it to the <c>Block</c> their
    /// <c>update*</c> slot takes.</remarks>
    private Node TransformFunctionBody(Node node)
    {
        _context.ResumeLexicalEnvironment();
        var statementOffset = 0;
        var statements = new List<IStatement>();
        var body = Utilities.VisitNode(((IFunctionLikeDeclarationBase)node).Body, Visitor, Utilities.IsConciseBody)
            ?? (Node)_factory.CreateBlock(/*statements*/ null, /*multiLine*/ null);
        if (NodeTests.IsBlock(body))
        {
            statementOffset = _factory.CopyPrologue(((Block)body).Statements, statements, /*ensureUseStrict*/ false, Visitor);
        }
        Core.AddRange(statements, AppendObjectRestAssignmentsIfNeeded(/*statements*/ null, node));

        var leadingStatements = _context.EndLexicalEnvironment();
        if (statementOffset > 0 || Core.Some(statements) || Core.Some(leadingStatements))
        {
            var block = _factory.Converters!.ConvertToFunctionBlock(body, /*multiLine*/ true);
            TypeScriptTransformSupport.InsertStatementsAfterStandardPrologue(statements, leadingStatements);
            Core.AddRange(statements, block.Statements, statementOffset);
            return _factory.UpdateBlock(block, Utilities.SetTextRange(_factory.CreateNodeArray(statements, /*hasTrailingComma*/ null), block.Statements));
        }
        return body;
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:1242-1330 — <c>appendObjectRestAssignmentsIfNeeded</c>.</summary>
    private List<IStatement>? AppendObjectRestAssignmentsIfNeeded(List<IStatement>? statements, Node node)
    {
        var containsPrecedingObjectRestOrSpread = false;
        foreach (var parameter in ((ISignatureDeclarationBase)node).Parameters!)
        {
            if (containsPrecedingObjectRestOrSpread)
            {
                var name = parameter.Name!;
                if (Utilities.IsBindingPattern(name))
                {
                    // In cases where a binding pattern is simply '[]' or '{}',
                    // we usually don't want to emit a var declaration; however, in the presence
                    // of an initializer, we must emit that expression to preserve side effects.
                    //
                    // NOTE: see `insertDefaultValueAssignmentForBindingPattern` in es2015.ts
                    if (ES2017TransformSupport.BindingPatternElements(name)!.Any())
                    {
                        var declarations = Destructuring.FlattenDestructuringBinding(
                            parameter,
                            Visitor,
                            _context,
                            FlattenLevel.All,
                            _factory.GetGeneratedNameForNode(parameter, /*flags*/ null));
                        if (Core.Some(declarations))
                        {
                            var declarationList = _factory.CreateVariableDeclarationList(declarations, /*flags*/ null);
                            var statement = _factory.CreateVariableStatement(/*modifiers*/ null, declarationList);
                            EmitNodes.SetEmitFlags(statement, EmitFlags.CustomPrologue);
                            statements = (List<IStatement>?)Core.Append(statements, statement);
                        }
                    }
                    else if (parameter.Initializer is not null)
                    {
                        var generatedName = _factory.GetGeneratedNameForNode(parameter, /*flags*/ null);
                        var initializer = Utilities.VisitNode(parameter.Initializer, Visitor, Utilities.IsExpression)!;
                        var assignment = _factory.CreateAssignment(generatedName, initializer);
                        var statement = _factory.CreateExpressionStatement(assignment);
                        EmitNodes.SetEmitFlags(statement, EmitFlags.CustomPrologue);
                        statements = (List<IStatement>?)Core.Append(statements, statement);
                    }
                }
                else if (parameter.Initializer is not null)
                {
                    // Converts a parameter initializer into a function body statement, i.e.:
                    //
                    //  function f(x = 1) { }
                    //
                    // becomes
                    //
                    //  function f(x) {
                    //    if (typeof x === "undefined") { x = 1; }
                    //  }

                    var clonedName = _factory.CloneNode(name);
                    Utilities.SetTextRange(clonedName, name);
                    EmitNodes.SetEmitFlags(clonedName, EmitFlags.NoSourceMap);

                    var parameterInitializer = Utilities.VisitNode(parameter.Initializer, Visitor, Utilities.IsExpression)!;
                    EmitNodes.AddEmitFlags(parameterInitializer, EmitFlags.NoSourceMap | EmitFlags.NoComments);

                    var assignment = _factory.CreateAssignment((Expression)clonedName, parameterInitializer);
                    Utilities.SetTextRange(assignment, parameter);
                    EmitNodes.SetEmitFlags(assignment, EmitFlags.NoComments);

                    var block = _factory.CreateBlock(
                        [_factory.CreateExpressionStatement(assignment)],
                        /*multiLine*/ null);
                    Utilities.SetTextRange(block, parameter);
                    EmitNodes.SetEmitFlags(block, EmitFlags.SingleLine | EmitFlags.NoTrailingSourceMap | EmitFlags.NoTokenSourceMaps | EmitFlags.NoComments);

                    var typeCheck = _factory.CreateTypeCheck((Expression)_factory.CloneNode(name), "undefined");
                    var statement = _factory.CreateIfStatement(typeCheck, block, /*elseStatement*/ null);
                    FactoryUtilities.StartOnNewLine(statement);
                    Utilities.SetTextRange(statement, parameter);
                    EmitNodes.SetEmitFlags(statement, EmitFlags.NoTokenSourceMaps | EmitFlags.NoTrailingSourceMap | EmitFlags.CustomPrologue | EmitFlags.NoComments);
                    statements = (List<IStatement>?)Core.Append(statements, statement);
                }
            }
            else if ((parameter.TransformFlags & TransformFlags.ContainsObjectRestOrSpread) != 0)
            {
                containsPrecedingObjectRestOrSpread = true;
                var declarations = Destructuring.FlattenDestructuringBinding(
                    parameter,
                    Visitor,
                    _context,
                    FlattenLevel.ObjectRest,
                    _factory.GetGeneratedNameForNode(parameter, /*flags*/ null),
                    /*hoistTempVariables*/ false,
                    /*skipInitializer*/ true);
                if (Core.Some(declarations))
                {
                    var declarationList = _factory.CreateVariableDeclarationList(declarations, /*flags*/ null);
                    var statement = _factory.CreateVariableStatement(/*modifiers*/ null, declarationList);
                    EmitNodes.SetEmitFlags(statement, EmitFlags.CustomPrologue);
                    statements = (List<IStatement>?)Core.Append(statements, statement);
                }
            }
        }
        return statements;
    }

    /// <summary>TS's <c>substitutedSuperAccessors[getNodeId(node)] = true</c>; see the field's note.</summary>
    private void SetSubstitutedSuperAccessor(int nodeId, bool value)
    {
        while (_substitutedSuperAccessors.Count <= nodeId)
        {
            _substitutedSuperAccessors.Add(false);
        }
        _substitutedSuperAccessors[nodeId] = value;
    }

    /// <summary>The read half of TS's <c>substitutedSuperAccessors[getNodeId(node)]</c>; see the field's note.</summary>
    private bool IsSubstitutedSuperAccessor(int nodeId)
        => nodeId < _substitutedSuperAccessors.Count && _substitutedSuperAccessors[nodeId];


}
