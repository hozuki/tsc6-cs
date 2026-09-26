namespace TypeScript.Compiler;

// ported from: src/compiler/expressionToTypeNode.ts:160-171 — the `SyntacticResult` union, its factory and its constants
// ported from: src/compiler/expressionToTypeNode.ts:173-192 — `createSyntacticTypeNodeBuilder` and the object literal it returns
/// <summary>ported from: src/compiler/expressionToTypeNode.ts:160-168 — <c>SyntacticResult</c>.</summary>
/// <remarks>
/// PORT-NOTE: TS's type is a union of three object-literal shapes, which record the same three states as this struct:
/// a built node (whose <c>reportFallback</c> is <c>undefined</c> and therefore never read — every consumer tests the
/// type first), no node and "the fallback was already reported", or no node and "report the fallback". C# states it as a
/// record struct, so the two constants below and the factory (<see cref="SyntacticNodeBuilder.SyntacticResultOf(ITypeNode?)"/>)
/// are the whole of the reference's union.
/// </remarks>
internal readonly record struct SyntacticResult(ITypeNode? Type, bool ReportFallback);

// ported from: src/compiler/expressionToTypeNode.ts:173-1330 — createSyntacticTypeNodeBuilder and everything it closes over
/// <summary>
/// ported from: src/compiler/expressionToTypeNode.ts:173-1330 — <c>createSyntacticTypeNodeBuilder</c>.
/// </summary>
/// <remarks>
/// <para>
/// The syntactic node builder: it turns a declaration, signature or expression that already *has* a syntactic type
/// annotation back into a type node, reusing the annotation when it still means what the checker computed and
/// serializing the checker's type when it does not. Every declaration-emitter path that must not infer a new type goes
/// through it (checker.ts:1552 assigns the one instance to <c>syntacticNodeBuilder</c>).
/// </para>
/// <para>
/// PORT-NOTE (closure → class): TS's <c>createSyntacticTypeNodeBuilder(options, resolver)</c> is a 1,150-line function
/// that returns an object literal of five members and closes over <c>strictNullChecks</c>, <c>resolver</c> and ~100
/// nested functions; C# has no object literal with a distinct type, so the returned object is this class and the
/// closure's state is its two fields. The five members are the generated <see cref="ISyntacticNodeBuilder"/> slots
/// (<c>NodeInterfaces.cs:3285-3291</c>), which are <c>Func</c>-typed there and therefore are wired lazily from the
/// methods that carry the reference's names — the same shape <c>Checker.CreateTypeChecker.cs:224</c> uses for
/// <c>ITypeChecker.GetIndexInfosOfIndexSymbol</c>. Every nested function becomes a private member taking <c>context</c>
/// (the frame it closed over); the reference's <c>SyntacticTypeNodeBuilderContext</c> is the generated
/// <see cref="ISyntacticTypeNodeBuilderContext"/>, which is what the delegate slots pass.
/// </para>
/// <para>
/// PORT-NOTE (the entry points): TS's object literal lists <c>serializeTypeOfDeclaration</c>,
/// <c>serializeReturnTypeForSignature</c>, <c>serializeTypeOfExpression</c>, <c>serializeTypeOfAccessor</c> and an
/// anonymous <c>tryReuseExistingTypeNode</c> wrapper; the four named ones are the functions below, and the wrapper is
/// <see cref="TryReuseExistingTypeNode"/> (which adds the reference's <c>resolver.canReuseTypeNode</c> guard around the
/// nested <see cref="TryReuseExistingTypeNodeWorker"/>).
/// </para>
/// <para>
/// PORT-NOTE (nullable returns): types.ts declares four of the five slots as <c>TypeNode | undefined</c>
/// (types.ts:10661-10667) and the generated delegates say <c>ITypeNode</c>; the implementation members keep the
/// reference's nullability and the slot getter applies the <c>!</c> the generated face implies.
/// </para>
/// <para>
/// PORT-NOTE (<c>visitNode</c>): the reference's <c>visitNode</c> is <c>visitorPublic.ts:148-192</c>'s, which the port
/// has not landed (<c>Utilities.VisitEachChildWorker</c> is the loud blocker for that module); every call in this file
/// goes to the file-local <see cref="VisitNode"/> port instead — see its remarks. <c>visitNodes</c> *is* landed
/// (<c>Utilities.VisitNodes</c>, <c>visitorPublic.ts:223-271</c>) but is fixed to <c>NodeArray&lt;Node&gt;</c>, which C#'s
/// invariant generics cannot feed a <c>NodeArray&lt;ITypeNode&gt;</c>, so this file carries the generic sibling
/// <see cref="VisitNodes{T}"/>; <c>visitEachChild</c> has no port at all and is called through the blocker
/// (<c>Utilities.VisitEachChildWorker</c>), exactly as the landed node builder does
/// (<c>NodeBuilder.Names.cs:1350</c>).
/// </para>
/// </remarks>
internal sealed partial class SyntacticNodeBuilder : ISyntacticNodeBuilder
{
    // ---------------------------------------------------------------------------------------------
    // The closure's state (expressionToTypeNode.ts:175-176)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:176 — <c>strictNullChecks</c>.</summary>
    private readonly bool _strictNullChecks;

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:174 — <c>options</c>.</summary>
    /// <remarks>PORT-NOTE: the factory's <c>options</c> parameter is closed over by <c>getEmitScriptTarget(options)</c> in the computed-property-name arm (expressionToTypeNode.ts:511), so the port keeps the same slot; every other read goes through <c>_strictNullChecks</c>.</remarks>
    private readonly ICompilerOptions _options;

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:175 — <c>resolver</c>.</summary>
    private readonly SyntacticBuilderResolver _resolver;

    /// <summary>ported from: src/compiler/factory/nodeFactory.ts:7407 — <c>factory</c>, the module-level singleton the file imports.</summary>
    /// <remarks>PORT-NOTE: the port keeps one instance (<c>NodeFactory.Factory</c>), the same slot <c>Checker._factory</c> reads (<c>Checker.NodeBuilder.cs:74</c>); it must NOT be <c>FactoryUtilities.ParseNodeFactory</c>.</remarks>
    private static readonly NodeFactory _factory = NodeFactory.Factory;

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:173-177 — <c>createSyntacticTypeNodeBuilder</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's exported function returns the object literal; the port states it as a static factory
    /// over a private constructor so that the one creator (<c>Checker.SyntacticNodeBuilder</c>, checker.ts:1552) reads
    /// exactly like the reference's assignment, and so that nothing else can build a builder without the resolver the
    /// reference demands.
    /// </remarks>
    internal static SyntacticNodeBuilder CreateSyntacticTypeNodeBuilder(ICompilerOptions options, SyntacticBuilderResolver resolver)
        => new(options, resolver);

    private SyntacticNodeBuilder(ICompilerOptions options, SyntacticBuilderResolver resolver)
    {
        _options = options;
        _strictNullChecks = Utilities.GetStrictOptionValue(options, "strictNullChecks");
        _resolver = resolver;
    }

    // ---------------------------------------------------------------------------------------------
    // The object literal's five members (expressionToTypeNode.ts:178-192)
    // ---------------------------------------------------------------------------------------------

    private Func<object?, ISymbol, ISyntacticTypeNodeBuilderContext, ITypeNode>? _serializeTypeOfDeclaration;

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:178 — <c>serializeTypeOfDeclaration</c> (the slot).</summary>
    /// <remarks>PORT-NOTE: the generated slot is a nullable <c>Func</c> with a get/set pair; the getter wires it from the method of the same name on first read, and no caller sets it.</remarks>
    Func<object?, ISymbol, ISyntacticTypeNodeBuilderContext, ITypeNode>? ISyntacticNodeBuilder.SerializeTypeOfDeclaration
    {
        get => _serializeTypeOfDeclaration ??= SerializeTypeOfDeclaration;
        set => _serializeTypeOfDeclaration = value;
    }

    private Func<Node, ISymbol, ISyntacticTypeNodeBuilderContext, ITypeNode>? _serializeReturnTypeForSignature;

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:179 — <c>serializeReturnTypeForSignature</c> (the slot).</summary>
    Func<Node, ISymbol, ISyntacticTypeNodeBuilderContext, ITypeNode>? ISyntacticNodeBuilder.SerializeReturnTypeForSignature
    {
        get => _serializeReturnTypeForSignature ??= SerializeReturnTypeForSignature;
        set => _serializeReturnTypeForSignature = value;
    }

    private Func<Expression, ISyntacticTypeNodeBuilderContext, bool, bool, ITypeNode>? _serializeTypeOfExpression;

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:180 — <c>serializeTypeOfExpression</c> (the slot).</summary>
    Func<Expression, ISyntacticTypeNodeBuilderContext, bool, bool, ITypeNode>? ISyntacticNodeBuilder.SerializeTypeOfExpression
    {
        get => _serializeTypeOfExpression ??= SerializeTypeOfExpression;
        set => _serializeTypeOfExpression = value;
    }

    private Func<ISyntacticTypeNodeBuilderContext, ITypeNode, ITypeNode>? _tryReuseExistingTypeNode;

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:182-188 — <c>tryReuseExistingTypeNode</c> (the slot).</summary>
    Func<ISyntacticTypeNodeBuilderContext, ITypeNode, ITypeNode>? ISyntacticNodeBuilder.TryReuseExistingTypeNode
    {
        get => _tryReuseExistingTypeNode ??= TryReuseExistingTypeNode;
        set => _tryReuseExistingTypeNode = value;
    }

    private Func<Node, ISymbol, ISyntacticTypeNodeBuilderContext, ITypeNode>? _serializeTypeOfAccessor;

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:181 — <c>serializeTypeOfAccessor</c> (the slot).</summary>
    Func<Node, ISymbol, ISyntacticTypeNodeBuilderContext, ITypeNode>? ISyntacticNodeBuilder.SerializeTypeOfAccessor
    {
        get => _serializeTypeOfAccessor ??= SerializeTypeOfAccessor;
        set => _serializeTypeOfAccessor = value;
    }

    // ---------------------------------------------------------------------------------------------
    // syntacticResult (expressionToTypeNode.ts:160-171)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:166-168 — <c>syntacticResult</c>.</summary>
    /// <remarks>PORT-NOTE: TS declares two overloads; the second is the first with <c>reportFallback</c> defaulted, so the port states one member with the default, exactly as the reference's implementation signature does.</remarks>
    private static SyntacticResult SyntacticResultOf(ITypeNode? type, bool reportFallback = true) => new(type, reportFallback);

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:169 — <c>notImplemented</c>.</summary>
    private static readonly SyntacticResult NotImplemented = SyntacticResultOf(/*type*/ null, /*reportFallback*/ false);

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:170 — <c>alreadyReported</c>.</summary>
    private static readonly SyntacticResult AlreadyReported = SyntacticResultOf(/*type*/ null, /*reportFallback*/ false);

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:171 — <c>failed</c>.</summary>
    private static readonly SyntacticResult Failed = SyntacticResultOf(/*type*/ null, /*reportFallback*/ true);

    // ---------------------------------------------------------------------------------------------
    // The five members (expressionToTypeNode.ts:665-739)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:182-188 — the object literal's <c>tryReuseExistingTypeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS inlines this wrapper in the literal and gives the guarded body a second name; the port keeps the
    /// two members the same way (<see cref="TryReuseExistingTypeNodeWorker"/> holds the reference's nested function).
    /// </remarks>
    public ITypeNode TryReuseExistingTypeNode(ISyntacticTypeNodeBuilderContext context, ITypeNode existing)
        => (_resolver.CanReuseTypeNode(context, existing) ? TryReuseExistingTypeNodeWorker(context, existing) : null)!;

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:665-667 — <c>serializeTypeOfAccessor</c>.</summary>
    /// <remarks>PORT-NOTE: the generated slot's <c>accessor</c> is the widened <c>Node</c> (<c>AccessorDeclaration</c> is <c>GetAccessorDeclaration | SetAccessorDeclaration</c> and has no C# face); the reference's <c>symbol</c> is optional, which the slot's <c>ISymbol</c> refines.</remarks>
    public ITypeNode SerializeTypeOfAccessor(Node accessor, ISymbol? symbol, ISyntacticTypeNodeBuilderContext context)
        => (TypeFromAccessor(accessor, symbol, context)
            ?? InferAccessorType(accessor, _resolver.GetAllAccessorDeclarations(accessor), context, symbol))!;

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:669-671 — <c>serializeTypeOfExpression</c>.</summary>
    public ITypeNode SerializeTypeOfExpression(Expression expr, ISyntacticTypeNodeBuilderContext context, bool addUndefined = false, bool preserveLiterals = false)
    {
        var result = TypeFromExpression(expr, context, /*isConstContext*/ false, addUndefined, preserveLiterals);
        return (result.Type ?? InferExpressionType(expr, context, result.ReportFallback))!;
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:673-698 — <c>serializeTypeOfDeclaration</c>.</summary>
    /// <remarks>PORT-NOTE: the generated slot's <c>node</c> is <c>object?</c> (<c>HasInferredType</c> has no C# face); the <c>Kind</c> switch reads it through the <c>Node</c> the reference's callers all pass, which is the class/interface boundary §3 fixes.</remarks>
    public ITypeNode SerializeTypeOfDeclaration(object? node, ISymbol symbol, ISyntacticTypeNodeBuilderContext context)
    {
        var declaration = (Node)node!;
        switch (declaration.Kind)
        {
            case SyntaxKind.Parameter:
            case SyntaxKind.JSDocParameterTag:
                return (TypeFromParameter(declaration, symbol, context))!;
            case SyntaxKind.VariableDeclaration:
                return (TypeFromVariable((VariableDeclaration)declaration, symbol, context))!;
            case SyntaxKind.PropertySignature:
            case SyntaxKind.JSDocPropertyTag:
            case SyntaxKind.PropertyDeclaration:
                return (TypeFromProperty(declaration, symbol, context))!;
            case SyntaxKind.BindingElement:
                return (InferTypeOfDeclaration(declaration, symbol, context))!;
            case SyntaxKind.ExportAssignment:
                return SerializeTypeOfExpression(((ExportAssignment)declaration).Expression!, context, /*addUndefined*/ false, /*preserveLiterals*/ true);
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ElementAccessExpression:
            case SyntaxKind.BinaryExpression:
                return (TypeFromExpandoProperty((Expression)declaration, symbol, context))!;
            case SyntaxKind.PropertyAssignment:
            case SyntaxKind.ShorthandPropertyAssignment:
                return (TypeFromPropertyAssignment(declaration, symbol, context))!;
            default:
                Debug.AssertNever(declaration, $"Node needs to be an inferrable node, found {Debug.FormatSyntaxKind(declaration.Kind)}");
                return null!;
        }
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:700-716 — <c>typeFromPropertyAssignment</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's parameter is <c>PropertyAssignment | ShorthandPropertyAssignment</c>, which has no C# face; both are declarations whose annotation is read through the same members.</remarks>
    private ITypeNode? TypeFromPropertyAssignment(Node node, ISymbol symbol, ISyntacticTypeNodeBuilderContext context)
    {
        var typeAnnotation = Utilities.GetEffectiveTypeAnnotationNode(node);
        ITypeNode? result = null;
        if (typeAnnotation is not null && _resolver.CanReuseTypeNodeAnnotation(context, (IDeclaration)node, typeAnnotation, symbol, null))
        {
            result = SerializeExistingTypeNode(typeAnnotation, context);
        }
        if (result is null && node.Kind == SyntaxKind.PropertyAssignment)
        {
            var initializer = ((PropertyAssignment)node).Initializer!;
            // PORT-NOTE: TS's `(initializer as AsExpression | TypeAssertion).type` is a union read; C# has no union
            // cast, so the two kinds are stated separately (a `TypeAssertion` cast to `AsExpression` would throw).
            var assertionNode = FactoryUtilities.IsJSDocTypeAssertion(initializer) ? FactoryUtilities.GetJSDocTypeAssertionType(initializer) :
                initializer is AsExpression asExpression ? asExpression.Type :
                initializer.Kind == SyntaxKind.TypeAssertionExpression ? ((TypeAssertion)initializer).Type :
                null;

            if (assertionNode is not null && !Utilities.IsConstTypeReference((Node)assertionNode) && _resolver.CanReuseTypeNodeAnnotation(context, (IDeclaration)node, assertionNode, symbol, null))
            {
                result = SerializeExistingTypeNode(assertionNode, context);
            }
        }
        return result ?? InferTypeOfDeclaration(node, symbol, context, /*reportFallback*/ false);
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:718-739 — <c>serializeReturnTypeForSignature</c>.</summary>
    /// <remarks>PORT-NOTE: the generated slot's <c>node</c> is the widened <c>Node</c> (<c>SignatureDeclaration | JSDocSignature</c> has no C# face); the <c>Kind</c> switch is the reference's.</remarks>
    public ITypeNode SerializeReturnTypeForSignature(Node node, ISymbol? symbol, ISyntacticTypeNodeBuilderContext context)
    {
        switch (node.Kind)
        {
            case SyntaxKind.GetAccessor:
                return SerializeTypeOfAccessor(node, symbol, context);
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.ConstructSignature:
            case SyntaxKind.MethodSignature:
            case SyntaxKind.CallSignature:
            case SyntaxKind.Constructor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.IndexSignature:
            case SyntaxKind.FunctionType:
            case SyntaxKind.ConstructorType:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.JSDocFunctionType:
            case SyntaxKind.JSDocSignature:
                return (CreateReturnFromSignature(node, symbol, context))!;
            default:
                Debug.AssertNever(node, $"Node needs to be an inferrable node, found {Debug.FormatSyntaxKind(node.Kind)}");
                return null!;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The infer family (expressionToTypeNode.ts:831-878)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:831-844 — <c>inferTypeOfDeclaration</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>node</c> is <c>HasInferredType | GetAccessorDeclaration | SetAccessorDeclaration</c>; the port widens to <c>Node</c>, which is what the resolver's <c>object?</c> slot and the tracker take.</remarks>
    private ITypeNode? InferTypeOfDeclaration(Node node, ISymbol? symbol, ISyntacticTypeNodeBuilderContext context, bool reportFallback = true)
    {
        if (reportFallback)
        {
            ReportInferenceFallback(context, node);
        }
        if (context.NoInferenceFallback == true)
        {
            return _factory.CreateKeywordTypeNode(SyntaxKind.AnyKeyword);
        }
        return _resolver.SerializeTypeOfDeclaration(context, node, symbol);
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:846-855 — <c>inferExpressionType</c>.</summary>
    private ITypeNode InferExpressionType(Expression node, ISyntacticTypeNodeBuilderContext context, bool reportFallback = true, bool? requiresAddingUndefined = null)
    {
        Debug.Assert(requiresAddingUndefined is not true);
        if (reportFallback)
        {
            ReportInferenceFallback(context, node);
        }
        if (context.NoInferenceFallback == true)
        {
            return _factory.CreateKeywordTypeNode(SyntaxKind.AnyKeyword);
        }
        return (_resolver.SerializeTypeOfExpression(context, node) ?? _factory.CreateKeywordTypeNode(SyntaxKind.AnyKeyword))!;
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:857-865 — <c>inferReturnTypeOfSignatureSignature</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>node</c> is <c>SignatureDeclaration | JSDocSignature</c>; the port widens to <c>Node</c> (no C# face), which is what the resolver's <c>Node?</c> slot takes.</remarks>
    private ITypeNode InferReturnTypeOfSignatureSignature(Node node, ISyntacticTypeNodeBuilderContext context, ISymbol? symbol, bool reportFallback)
    {
        if (reportFallback)
        {
            ReportInferenceFallback(context, node);
        }
        if (context.NoInferenceFallback == true)
        {
            return _factory.CreateKeywordTypeNode(SyntaxKind.AnyKeyword);
        }
        return (_resolver.SerializeReturnTypeForSignature(context, node, symbol) ?? _factory.CreateKeywordTypeNode(SyntaxKind.AnyKeyword))!;
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:867-878 — <c>inferAccessorType</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>node</c> is <c>GetAccessorDeclaration | SetAccessorDeclaration</c>; the port widens to <c>Node</c>.</remarks>
    private ITypeNode? InferAccessorType(Node node, IAllAccessorDeclarations allAccessors, ISyntacticTypeNodeBuilderContext context, ISymbol? symbol, bool reportFallback = true)
    {
        if (node.Kind == SyntaxKind.GetAccessor)
        {
            return CreateReturnFromSignature(node, symbol, context, reportFallback);
        }
        else
        {
            if (reportFallback)
            {
                ReportInferenceFallback(context, node);
            }
            var result = allAccessors.GetAccessor is not null ? CreateReturnFromSignature(allAccessors.GetAccessor, symbol, context, reportFallback) : null;
            return result ?? _resolver.SerializeTypeOfDeclaration(context, node, symbol) ?? _factory.CreateKeywordTypeNode(SyntaxKind.AnyKeyword);
        }
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:880-884 — <c>withNewScope</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>node</c> is <c>IntroducesNewScopeNode | ConditionalTypeNode</c>; the port widens to <c>Node</c>. The reference does not guard the cleanup with <c>try</c>/<c>finally</c>, and neither does the port.</remarks>
    private T WithNewScope<T>(ISyntacticTypeNodeBuilderContext context, Node node, Func<T> fn)
    {
        var cleanup = _resolver.EnterNewScope(context, node);
        var result = fn();
        cleanup();
        return result;
    }

    // ---------------------------------------------------------------------------------------------
    // undefined (expressionToTypeNode.ts:1212-1268)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:1212-1218 — <c>createUndefinedTypeNode</c>.</summary>
    private ITypeNode CreateUndefinedTypeNode()
        => _strictNullChecks
            ? _factory.CreateKeywordTypeNode(SyntaxKind.UndefinedKeyword)
            : _factory.CreateKeywordTypeNode(SyntaxKind.AnyKeyword);

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:1234-1244 — <c>addUndefinedIfNeeded</c>.</summary>
    private ITypeNode AddUndefinedIfNeeded(ITypeNode node, bool? addUndefined, Node? owner, ISyntacticTypeNodeBuilderContext context)
    {
        var parentDeclaration = owner is not null ? Utilities.WalkUpParenthesizedExpressions(owner).Parent : null;
        var optionalDeclaration = parentDeclaration is not null && Utilities.IsDeclaration(parentDeclaration) && Utilities.IsOptionalDeclaration((IDeclaration)parentDeclaration);
        if (!_strictNullChecks || !(addUndefined == true || optionalDeclaration)) return node;
        if (!CanAddUndefined(node))
        {
            ReportInferenceFallback(context, (Node)node);
        }
        if (NodeTests.IsUnionTypeNode((Node)node))
        {
            var types = new List<ITypeNode>(((UnionTypeNode)node).Types!);
            types.Add(_factory.CreateKeywordTypeNode(SyntaxKind.UndefinedKeyword));
            return _factory.CreateUnionTypeNode(types);
        }
        return _factory.CreateUnionTypeNode(new List<ITypeNode> { node, _factory.CreateKeywordTypeNode(SyntaxKind.UndefinedKeyword) });
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:1246-1268 — <c>canAddUndefined</c>.</summary>
    private bool CanAddUndefined(ITypeNode node)
    {
        if (!_strictNullChecks) return true;
        if (
            Utilities.IsKeyword(node.Kind)
            || node.Kind == SyntaxKind.LiteralType
            || node.Kind == SyntaxKind.FunctionType
            || node.Kind == SyntaxKind.ConstructorType
            || node.Kind == SyntaxKind.ArrayType
            || node.Kind == SyntaxKind.TupleType
            || node.Kind == SyntaxKind.TypeLiteral
            || node.Kind == SyntaxKind.TemplateLiteralType
            || node.Kind == SyntaxKind.ThisType
        )
        {
            return true;
        }
        if (node.Kind == SyntaxKind.ParenthesizedType)
        {
            return CanAddUndefined(((ParenthesizedTypeNode)node).Type!);
        }
        if (node.Kind is SyntaxKind.UnionType or SyntaxKind.IntersectionType)
        {
            return Core.Every(((UnionTypeNode)node).Types, (t, _) => CanAddUndefined(t!));
        }
        return false;
    }

    // ---------------------------------------------------------------------------------------------
    // the resolver/tracker boundary (the generated slots the reference's resolver hands back)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:6354-6358 — <c>context.tracker.reportInferenceFallback</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's context carries the tracker as <c>SymbolTracker</c>; the generated
    /// <see cref="ISyntacticTypeNodeBuilderContext.Tracker"/> is <c>object?</c> (the interface is declared outside the
    /// extracted sources), so the port reads it through <see cref="ISymbolTracker"/>, which is what every implementation
    /// of the slot is.
    /// </remarks>
    private static void ReportInferenceFallback(ISyntacticTypeNodeBuilderContext context, Node node)
        => ((ISymbolTracker)context.Tracker!).ReportInferenceFallback(node);

    /// <summary>ported from: src/compiler/checker.ts:6410-6412 — <c>resolver.trackExistingEntityName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's member answers the <c>{ introducesError, node }</c> object literal; C# states that
    /// object as the tuple <c>Checker.TrackExistingEntityName</c> answers (<c>NodeBuilder.Names.cs:1250</c>), which the
    /// generated slot hands back as <c>object?</c>, so the two call sites below unbox it here — one member, so the shape
    /// has a single home.
    /// </remarks>
    private (bool IntroducesError, T Node) TrackExistingEntityName<T>(ISyntacticTypeNodeBuilderContext context, T node) where T : Node
        => ((bool IntroducesError, T Node))_resolver.TrackExistingEntityName(context, node)!;

    /// <summary>ported from: src/compiler/checker.ts:6413-6415 — <c>resolver.hasLateBindableName</c>.</summary>
    /// <remarks>PORT-NOTE: TS's member is a type guard whose narrowed type has no C# name, so the generated slot is <c>object?</c> holding the guard's boolean (<c>SyntacticBuilderResolver.cs:300-302</c>); a consumer reads it as a truth test.</remarks>
    private bool HasLateBindableName(Node node)
        => (bool)_resolver.HasLateBindableName((IDeclaration)node)!;

    /// <summary>ported from: src/compiler/checker.ts:6317-6319 — <c>resolver.createRecoveryBoundary</c>.</summary>
    /// <remarks>PORT-NOTE: the generated slot is <c>object?</c> because the reference's four-member object literal has no C# face; the port boxes <see cref="IRecoveryBoundary"/>, which is the object (<c>NodeBuilder.Signatures.cs:825-855</c>).</remarks>
    private IRecoveryBoundary CreateRecoveryBoundary(ISyntacticTypeNodeBuilderContext context)
        => (IRecoveryBoundary)_resolver.CreateRecoveryBoundary(context)!;
}
