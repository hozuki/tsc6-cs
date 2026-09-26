namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:1552 — the `syntacticNodeBuilder` state slot
// ported from: src/compiler/checker.ts:1680-1689 — the `ITypeChecker` members `createNodeBuilder`'s object supplies
// ported from: src/compiler/checker.ts:6476-6495 — the object `createNodeBuilder` returns
// ported from: src/compiler/checker.ts:6498-6759 — the spine every other node-builder member calls
// ported from: src/compiler/checker.ts:54260-54278 — createBasicNodeBuilderModuleSpecifierResolutionHost
/// <summary>
/// The checker's node builder: the members that turn a <c>Type</c>/<c>Symbol</c>/<c>Signature</c> back into a syntax
/// node (or an entity name/expression), for the declaration emitter and the services (M6-6).
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS's <c>createNodeBuilder</c> is a 4,860-line function inside <c>createTypeChecker</c> that returns an
/// object literal; the checker publishes ten of its members as its own (checker.ts:1680-1689). C# has no object
/// literal with a distinct type here and the generated <see cref="ITypeChecker"/> already declares exactly that member
/// set (NodeInterfaces.cs:3546-3559), so the object collapses into this class — there is no <c>NodeBuilder</c> class
/// and no delegation layer. The body of the builder is split across the sibling partial files (<c>NodeBuilder.Types.cs</c>,
/// <c>NodeBuilder.Members.cs</c>, <c>NodeBuilder.Signatures.cs</c>, <c>NodeBuilder.Names.cs</c>,
/// <c>NodeBuilder.Declarations.cs</c>, <c>NodeBuilder.Serialize.cs</c>) exactly as <c>Parser/*</c> splits <c>parser.ts</c>.
/// </para>
/// <para>
/// PORT-NOTE: TS's <c>nodeBuilder</c> is a <c>var</c> in <c>createTypeChecker</c> and the object's members close over
/// the checker, so every nested function reaches the checker's state directly. The port states the same members on the
/// class, which is why they can be called unqualified from any sibling partial and why the two forwarding classes
/// (<see cref="SyntacticBuilderResolver"/>, <see cref="EmitResolver"/>) can reach the ones they need.
/// </para>
/// <para>
/// PORT-NOTE (the printer): <c>symbolToString</c>/<c>signatureToString</c>/<c>typeToString</c>/<c>typePredicateToString</c>
/// (checker.ts:6169-6264, 11175-11187) end in <c>createPrinter*</c> + <c>printer.writeNode</c>, which are declared in
/// <c>emitter.ts:1200-1211</c> — Layer 7, not ported. Per docs/architecture.md §15.15 ruling 1 they land as loud,
/// named blockers, in <c>Checker/Stringify.cs</c>.
/// </para>
/// <para>
/// PORT-NOTE: <c>expressionToTypeNode.ts</c> (1,330 lines, §15.14's remaining half, §15.15's M6-6 row) is not ported
/// yet, so <c>syntacticNodeBuilder = createSyntacticTypeNodeBuilder(compilerOptions, nodeBuilder.syntacticBuilderResolver)</c>
/// (checker.ts:1552) cannot be assembled. The port keeps the slot and guards every read with a loud, named throw
/// (<see cref="SyntacticNodeBuilder"/>) instead of dereferencing a null; the builder's resolver —
/// <see cref="SyntacticBuilderResolver"/>, checker.ts:6310-6476 — is landed here, so landing <c>expressionToTypeNode.ts</c>
/// is now a one-line assignment in M6-5's assembly with no change at any call site.
/// </para>
/// <para>
/// PORT-NOTE: <c>factory</c> (factory/nodeFactory.ts:7407) is the module-level singleton the checker imports; the port
/// keeps one instance (<c>NodeFactory.Factory</c>) and reads it through the <c>_factory</c> field, the same shape
/// <c>Parser</c>/<c>Binder</c> use for their own factory. It must NOT be <c>FactoryUtilities.ParseNodeFactory</c>,
/// which is configured with <c>NodeFactoryFlags.NoParenthesizerRules</c>.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // The node builder's state (checker.ts:1551-1552, factory/nodeFactory.ts:7407)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/factory/nodeFactory.ts:7407 — <c>factory</c>.</summary>
    /// <remarks>See the class remarks for why this is not <c>FactoryUtilities.ParseNodeFactory</c>.</remarks>
    internal readonly NodeFactory _factory = NodeFactory.Factory;

    /// <summary>ported from: src/compiler/checker.ts:1552 — <c>syntacticNodeBuilder</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS assigns it at :1552 and every reader is in the node builder. The port leaves the assignment to
    /// the M6-5 assembly (which also runs :1551's <c>nodeBuilder = createNodeBuilder()</c>, a no-op here because the
    /// builder's members live on this class) because its only creator, <c>createSyntacticTypeNodeBuilder</c>, is
    /// <c>expressionToTypeNode.ts</c> — not ported. Reads go through <see cref="SyntacticNodeBuilder"/>.
    /// </remarks>
    internal ISyntacticNodeBuilder? _syntacticNodeBuilder;

    private SyntacticBuilderResolver? _syntacticBuilderResolver;

    /// <summary>ported from: src/compiler/checker.ts:6310 — <c>syntacticBuilderResolver</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS builds the object literal eagerly inside <c>createNodeBuilder</c> and hands it to
    /// <c>createSyntacticTypeNodeBuilder</c> at :1552. The port builds it on first read so that this slice does not
    /// have to touch <c>Checker</c>'s constructor (which M6-0 landed); the object holds no per-call state, so laziness
    /// is unobservable.
    /// </remarks>
    internal SyntacticBuilderResolver SyntacticBuilderResolver => _syntacticBuilderResolver ??= new SyntacticBuilderResolver(this);

    /// <summary>ported from: src/compiler/checker.ts:1552 — <c>syntacticNodeBuilder</c> as the node builder reads it.</summary>
    /// <remarks>
    /// PORT-NOTE: a loud, named blocker (docs/architecture.md §15.15 ruling 1's sanctioned form, pinned by a test in
    /// <c>CheckerNodeBuilderTests</c>): <c>createSyntacticTypeNodeBuilder</c> lives in <c>expressionToTypeNode.ts</c>,
    /// which is not ported, so dereferencing <c>_syntacticNodeBuilder</c> would be a null dereference at a call site
    /// whose message names nothing. The message names the missing module and its owner instead.
    /// </remarks>
    internal ISyntacticNodeBuilder SyntacticNodeBuilder => _syntacticNodeBuilder
        ?? throw new NotSupportedException(
            "syntacticNodeBuilder (checker.ts:1552) requires createSyntacticTypeNodeBuilder from expressionToTypeNode.ts "
            + "(1,330 lines, not ported — M4f's remaining half). Checker._syntacticNodeBuilder is assigned by the M6-5 "
            + "assembly once it lands; until then the node builder's reuse/elision paths cannot run.");

    // ---------------------------------------------------------------------------------------------
    // The ITypeChecker surface (checker.ts:1680-1689)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:1680 — <c>typeToTypeNode</c>.</summary>
    public ITypeNode? TypeToTypeNode(IType? type, Node? enclosingDeclaration, NodeBuilderFlags? flags)
        => TypeToTypeNode(type!, enclosingDeclaration, flags, null, null, null, null, null);

    /// <summary>ported from: src/compiler/checker.ts:1680 — the <c>internalFlags</c>/<c>tracker</c> overload.</summary>
    public ITypeNode? TypeToTypeNode(IType? type, Node? enclosingDeclaration, NodeBuilderFlags? flags, InternalNodeBuilderFlags? internalFlags, ISymbolTracker? tracker)
        => TypeToTypeNode(type!, enclosingDeclaration, flags, internalFlags, tracker, null, null, null);

    /// <summary>ported from: src/compiler/checker.ts:6481 — the object's <c>typeToTypeNode</c> (with the truncation options).</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares every parameter after <c>flags</c> optional; C# cannot give this overload defaults
    /// because member lookup would then be ambiguous against the five-parameter <see cref="ITypeChecker"/> member, so
    /// the three trailing options are required here and callers pass <c>null</c> where TS passes <c>undefined</c>.
    /// </remarks>
    internal ITypeNode? TypeToTypeNode(IType type, Node? enclosingDeclaration, NodeBuilderFlags? flags, InternalNodeBuilderFlags? internalFlags, ISymbolTracker? tracker, int? maximumLength, int? verbosityLevel, IWriterContextOut? @out)
        => WithContext(enclosingDeclaration, flags, internalFlags, tracker, maximumLength, verbosityLevel, context => TypeToTypeNodeHelper(type, context), @out);

    /// <summary>ported from: src/compiler/checker.ts:6482 — <c>typePredicateToTypePredicateNode</c>.</summary>
    public TypePredicateNode? TypePredicateToTypePredicateNode(ITypePredicateBase? typePredicate, Node? enclosingDeclaration, NodeBuilderFlags? flags, InternalNodeBuilderFlags? internalFlags, ISymbolTracker? tracker)
        => WithContext(enclosingDeclaration, flags, internalFlags, tracker, null, null, context => TypePredicateToTypePredicateNodeHelper((ITypePredicateBase)typePredicate!, context));

    /// <summary>ported from: src/compiler/checker.ts:6486 — <c>indexInfoToIndexSignatureDeclaration</c>.</summary>
    public IndexSignatureDeclaration? IndexInfoToIndexSignatureDeclaration(IIndexInfo? indexInfo, Node? enclosingDeclaration, NodeBuilderFlags? flags)
        => IndexInfoToIndexSignatureDeclaration(indexInfo, enclosingDeclaration, flags, null, null);

    /// <summary>ported from: src/compiler/checker.ts:6486 — the <c>internalFlags</c>/<c>tracker</c> overload.</summary>
    public IndexSignatureDeclaration? IndexInfoToIndexSignatureDeclaration(IIndexInfo? indexInfo, Node? enclosingDeclaration, NodeBuilderFlags? flags, InternalNodeBuilderFlags? internalFlags, ISymbolTracker? tracker)
        => WithContext(enclosingDeclaration, flags, internalFlags, tracker, null, null, context => IndexInfoToIndexSignatureDeclarationHelper((IndexInfo)indexInfo!, context, null));

    /// <summary>ported from: src/compiler/checker.ts:6487 — <c>signatureToSignatureDeclaration</c>.</summary>
    public Node? SignatureToSignatureDeclaration(ISignature? signature, SyntaxKind kind, Node? enclosingDeclaration, NodeBuilderFlags? flags)
        => SignatureToSignatureDeclaration(signature, kind, enclosingDeclaration, flags, null, null, null, null, null);

    /// <summary>ported from: src/compiler/checker.ts:6487 — the <c>internalFlags</c>/<c>tracker</c> overload.</summary>
    public Node? SignatureToSignatureDeclaration(ISignature? signature, SyntaxKind kind, Node? enclosingDeclaration, NodeBuilderFlags? flags, InternalNodeBuilderFlags? internalFlags, ISymbolTracker? tracker)
        => SignatureToSignatureDeclaration(signature, kind, enclosingDeclaration, flags, internalFlags, tracker, null, null, null);

    /// <summary>ported from: src/compiler/checker.ts:6487 — the object's member (with the truncation options).</summary>
    internal Node? SignatureToSignatureDeclaration(ISignature? signature, SyntaxKind kind, Node? enclosingDeclaration, NodeBuilderFlags? flags, InternalNodeBuilderFlags? internalFlags, ISymbolTracker? tracker, int? maximumLength, int? verbosityLevel, IWriterContextOut? @out)
        => WithContext(enclosingDeclaration, flags, internalFlags, tracker, maximumLength, verbosityLevel, context => SignatureToSignatureDeclarationHelper((Signature)signature!, kind, context), @out);

    /// <summary>ported from: src/compiler/checker.ts:6488 — <c>symbolToEntityName</c>.</summary>
    public Node? SymbolToEntityName(ISymbol? symbol, SymbolFlags meaning, Node? enclosingDeclaration, NodeBuilderFlags? flags)
        => SymbolToEntityName(symbol, meaning, enclosingDeclaration, flags, null, null);

    /// <summary>ported from: src/compiler/checker.ts:6488 — the object's member (with <c>internalFlags</c>/<c>tracker</c>).</summary>
    internal Node? SymbolToEntityName(ISymbol? symbol, SymbolFlags meaning, Node? enclosingDeclaration, NodeBuilderFlags? flags, InternalNodeBuilderFlags? internalFlags, ISymbolTracker? tracker)
        => WithContext(enclosingDeclaration, flags, internalFlags, tracker, null, null, context => SymbolToName(symbol!, context, meaning, /*expectsIdentifier*/ false));

    /// <summary>ported from: src/compiler/checker.ts:6489 — <c>symbolToExpression</c>.</summary>
    public Expression? SymbolToExpression(ISymbol? symbol, SymbolFlags meaning, Node? enclosingDeclaration, NodeBuilderFlags? flags)
        => SymbolToExpression(symbol, meaning, enclosingDeclaration, flags, null, null);

    /// <summary>ported from: src/compiler/checker.ts:6489 — the object's member (with <c>internalFlags</c>/<c>tracker</c>).</summary>
    internal Expression? SymbolToExpression(ISymbol? symbol, SymbolFlags meaning, Node? enclosingDeclaration, NodeBuilderFlags? flags, InternalNodeBuilderFlags? internalFlags, ISymbolTracker? tracker)
        => WithContext(enclosingDeclaration, flags, internalFlags, tracker, null, null, context => SymbolToExpression(symbol!, context, meaning));

    /// <summary>ported from: src/compiler/checker.ts:6490 — <c>symbolToTypeParameterDeclarations</c>.</summary>
    public NodeArray<TypeParameterDeclaration>? SymbolToTypeParameterDeclarations(ISymbol? symbol, Node? enclosingDeclaration, NodeBuilderFlags? flags)
        => SymbolToTypeParameterDeclarations(symbol, enclosingDeclaration, flags, null, null);

    /// <summary>ported from: src/compiler/checker.ts:6490 — the object's member (with <c>internalFlags</c>/<c>tracker</c>).</summary>
    internal NodeArray<TypeParameterDeclaration>? SymbolToTypeParameterDeclarations(ISymbol? symbol, Node? enclosingDeclaration, NodeBuilderFlags? flags, InternalNodeBuilderFlags? internalFlags, ISymbolTracker? tracker)
        => WithContext(enclosingDeclaration, flags, internalFlags, tracker, null, null, context => TypeParametersToTypeParameterDeclarations(symbol!, context));

    /// <summary>ported from: src/compiler/checker.ts:6491 — <c>symbolToParameterDeclaration</c>.</summary>
    public ParameterDeclaration? SymbolToParameterDeclaration(ISymbol? symbol, Node? enclosingDeclaration, NodeBuilderFlags? flags)
        => SymbolToParameterDeclaration(symbol, enclosingDeclaration, flags, null, null);

    /// <summary>ported from: src/compiler/checker.ts:6491 — the object's member (with <c>internalFlags</c>/<c>tracker</c>).</summary>
    internal ParameterDeclaration? SymbolToParameterDeclaration(ISymbol? symbol, Node? enclosingDeclaration, NodeBuilderFlags? flags, InternalNodeBuilderFlags? internalFlags, ISymbolTracker? tracker)
        => WithContext(enclosingDeclaration, flags, internalFlags, tracker, null, null, context => SymbolToParameterDeclaration(symbol!, context));

    /// <summary>ported from: src/compiler/checker.ts:6492 — <c>typeParameterToDeclaration</c>.</summary>
    public TypeParameterDeclaration? TypeParameterToDeclaration(ITypeParameter? parameter, Node? enclosingDeclaration, NodeBuilderFlags? flags)
        => TypeParameterToDeclaration(parameter, enclosingDeclaration, flags, null, null, null, null, null);

    /// <summary>ported from: src/compiler/checker.ts:6492 — the object's member (with the truncation options).</summary>
    internal TypeParameterDeclaration? TypeParameterToDeclaration(ITypeParameter? parameter, Node? enclosingDeclaration, NodeBuilderFlags? flags, InternalNodeBuilderFlags? internalFlags, ISymbolTracker? tracker, int? maximumLength, int? verbosityLevel, IWriterContextOut? @out)
        => WithContext(enclosingDeclaration, flags, internalFlags, tracker, maximumLength, verbosityLevel, context => TypeParameterToDeclaration((TypeParameter)parameter!, context), @out);

    /// <summary>ported from: src/compiler/checker.ts:6493 — <c>symbolTableToDeclarationStatements</c>.</summary>
    internal List<IStatement>? SymbolTableToDeclarationStatements(SymbolTable symbolTable, Node? enclosingDeclaration, NodeBuilderFlags? flags, InternalNodeBuilderFlags? internalFlags, ISymbolTracker? tracker)
        => WithContext(enclosingDeclaration, flags, internalFlags, tracker, null, null, context => SymbolTableToDeclarationStatements(symbolTable, context));

    /// <summary>ported from: src/compiler/checker.ts:6494 — <c>symbolToNode</c>.</summary>
    public Node? SymbolToNode(ISymbol? symbol, SymbolFlags meaning, Node? enclosingDeclaration, NodeBuilderFlags? flags, InternalNodeBuilderFlags? internalFlags)
        => SymbolToNode(symbol, meaning, enclosingDeclaration, flags, internalFlags, null);

    /// <summary>ported from: src/compiler/checker.ts:6494 — the object's member (with <c>tracker</c>).</summary>
    internal Node? SymbolToNode(ISymbol? symbol, SymbolFlags meaning, Node? enclosingDeclaration, NodeBuilderFlags? flags, InternalNodeBuilderFlags? internalFlags, ISymbolTracker? tracker)
        => WithContext(enclosingDeclaration, flags, internalFlags, tracker, null, null, context => SymbolToNode(symbol!, context, meaning));

    /// <summary>ported from: src/compiler/checker.ts:6495 — <c>symbolToDeclarations</c>.</summary>
    internal List<IDeclaration> SymbolToDeclarations(ISymbol symbol, SymbolFlags meaning, NodeBuilderFlags flags, int? maximumLength, int? verbosityLevel, IWriterContextOut? @out)
    {
        var nodes = WithContext(
            /*enclosingDeclaration*/ null,
            flags,
            /*internalFlags*/ null,
            /*tracker*/ null,
            maximumLength,
            verbosityLevel,
            context => SymbolToDeclarationsWorker(symbol, context),
            @out);
        return Core.MapDefined<IStatement, IDeclaration>(nodes, (node, _) => node!.Kind switch
        {
            SyntaxKind.ClassDeclaration => (IDeclaration)SimplifyClassDeclaration((ClassDeclaration)node, symbol),
            SyntaxKind.EnumDeclaration => SimplifyModifiers((Node)node, NodeTests.IsEnumDeclaration, symbol),
            SyntaxKind.InterfaceDeclaration => SimplifyInterfaceDeclaration((InterfaceDeclaration)node, symbol, meaning),
            SyntaxKind.ModuleDeclaration => SimplifyModifiers((Node)node, NodeTests.IsModuleDeclaration, symbol),
            _ => null,
        }) ?? [];
    }

    /// <summary>ported from: src/compiler/checker.ts:6483-6485 — the three syntactic-builder forwarders.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's object carries <c>serializeTypeForDeclaration</c>/<c>serializeReturnTypeForSignature</c>/
    /// <c>serializeTypeForExpression</c>, forwarded by <c>createTypeOfDeclaration</c>/… (checker.ts:51187-51218, M6-5).
    /// They delegate to <c>syntacticNodeBuilder</c>, so they are printer-gated in the same sense as the stringifiers:
    /// every read of <see cref="SyntacticNodeBuilder"/> throws by name until <c>expressionToTypeNode.ts</c> lands.
    /// </remarks>
    internal ITypeNode? SerializeTypeForDeclaration(Node? declaration, ISymbol symbol, Node? enclosingDeclaration, NodeBuilderFlags? flags, InternalNodeBuilderFlags? internalFlags, ISymbolTracker? tracker)
        => WithContext(enclosingDeclaration, flags, internalFlags, tracker, null, null, context => SyntacticNodeBuilder.SerializeTypeOfDeclaration!(declaration, symbol, context));

    /// <summary>ported from: src/compiler/checker.ts:6484 — <c>serializeReturnTypeForSignature</c>.</summary>
    internal ITypeNode? SerializeReturnTypeForSignature(Node signatureDeclaration, Node? enclosingDeclaration, NodeBuilderFlags? flags, InternalNodeBuilderFlags? internalFlags, ISymbolTracker? tracker)
        => WithContext(enclosingDeclaration, flags, internalFlags, tracker, null, null, context => SyntacticNodeBuilder.SerializeReturnTypeForSignature!(signatureDeclaration, GetSymbolOfDeclaration((IDeclaration)signatureDeclaration)!, context));

    /// <summary>ported from: src/compiler/checker.ts:6485 — <c>serializeTypeForExpression</c>.</summary>
    internal ITypeNode? SerializeTypeForExpression(Expression expr, Node? enclosingDeclaration, NodeBuilderFlags? flags, InternalNodeBuilderFlags? internalFlags, ISymbolTracker? tracker)
        => WithContext(enclosingDeclaration, flags, internalFlags, tracker, null, null, context => SyntacticNodeBuilder.SerializeTypeOfExpression!(expr, context, /*addUndefined*/ false, /*preserveLiterals*/ false));

    // ---------------------------------------------------------------------------------------------
    // The spine (checker.ts:6498-6759)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:6498-6510 — <c>getTypeFromTypeNode</c> (the node builder's shadowing copy).</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares two overloads plus the implementation because the <c>noMappedTypes</c> flag changes the
    /// return type; the port states one member with a <c>bool</c> flag, which is how every call site in this slice
    /// passes it (<c>false</c>/omitted, or <c>true</c> where TS asks for the possibly-undefined answer).
    /// </remarks>
    internal IType? GetTypeFromTypeNode(NodeBuilderContext context, ITypeNode node, bool noMappedTypes = false)
    {
        var type = GetTypeFromTypeNodeWithoutContext(node);
        if (context.Mapper is null) return type;

        var mappedType = InstantiateType(type, (TypeMapper)context.Mapper);
        return noMappedTypes && !ReferenceEquals(mappedType, type) ? null : mappedType;
    }

    /// <summary>ported from: src/compiler/checker.ts:6517-6539 — <c>setTextRange</c> (the node builder's copy).</summary>
    internal T SetTextRange<T>(NodeBuilderContext context, T range, Node? location) where T : Node
    {
        if (!Utilities.NodeIsSynthesized(range) || (range.Flags & NodeFlags.Synthesized) == 0 || context.EnclosingFile is null || !ReferenceEquals(context.EnclosingFile, Utilities.GetSourceFileOfNode(Utilities.GetOriginalNode(range))))
        {
            range = _factory.CloneNode(range);
        }
        if (ReferenceEquals(range, location)) return range;
        if (location is null)
        {
            return range;
        }
        // Don't overwrite the original node if `range` has an `original` node that points either directly or indirectly to `location`
        var original = range.Original;
        while (original is not null && !ReferenceEquals(original, location))
        {
            original = original.Original;
        }
        if (original is null)
        {
            NodeFactory.SetOriginalNode(range, location);
        }
        // only set positions if range comes from the same file since copying text across files isn't supported by the emitter
        if (context.EnclosingFile is not null && ReferenceEquals(context.EnclosingFile, Utilities.GetSourceFileOfNode(Utilities.GetOriginalNode(location))))
        {
            return Utilities.SetTextRange(range, location);
        }
        return range;
    }

    /// <summary>ported from: src/compiler/checker.ts:6540-6554 — <c>symbolToNode</c>.</summary>
    internal Node SymbolToNode(ISymbol symbol, NodeBuilderContext context, SymbolFlags meaning)
    {
        if ((context.InternalFlags & InternalNodeBuilderFlags.WriteComputedProps) != 0)
        {
            if (symbol.ValueDeclaration is not null)
            {
                var name = Utilities.GetNameOfDeclaration((Node)symbol.ValueDeclaration) as Node;
                if (name is not null && NodeTests.IsComputedPropertyName(name)) return name;
            }
            var nameType = GetSymbolLinks(symbol).NameType;
            if (nameType is not null && (nameType.Flags & (TypeFlags.EnumLiteral | TypeFlags.UniqueESSymbol)) != 0)
            {
                context.EnclosingDeclaration = (Node?)nameType.Symbol!.ValueDeclaration;
                return _factory.CreateComputedPropertyName(SymbolToExpression(nameType.Symbol, context, meaning)!);
            }
        }
        return SymbolToExpression(symbol, context, meaning)!;
    }

    /// <summary>ported from: src/compiler/checker.ts:6582-6599 — <c>simplifyClassDeclaration</c>.</summary>
    private IDeclaration SimplifyClassDeclaration(ClassDeclaration classDecl, ISymbol symbol)
    {
        var classDeclarations = Core.Filter(symbol.Declarations, d => Utilities.IsClassLike((Node)d));
        var originalClassDecl = classDeclarations is not null && classDeclarations.Count > 0 ? classDeclarations[0] : classDecl;
        var modifiers = Utilities.GetEffectiveModifierFlags((Node)originalClassDecl) & ~(ModifierFlags.Export | ModifierFlags.Ambient);
        var isAnonymous = NodeTests.IsClassExpression((Node)originalClassDecl);
        if (isAnonymous)
        {
            classDecl = _factory.UpdateClassDeclaration(
                classDecl,
                classDecl.Modifiers,
                /*name*/ null,
                classDecl.TypeParameters,
                classDecl.HeritageClauses,
                classDecl.Members);
        }
        return _factory.ReplaceModifiers(classDecl, modifiers);
    }

    /// <summary>ported from: src/compiler/checker.ts:6600-6606 — <c>simplifyModifiers</c>.</summary>
    private IDeclaration SimplifyModifiers(Node newDecl, Func<Node, bool> isDeclKind, ISymbol symbol)
    {
        var decls = Core.Filter(symbol.Declarations, d => isDeclKind((Node)d));
        Node declWithModifiers = decls is not null && decls.Count > 0 ? (Node)decls[0] : newDecl;
        var modifiers = Utilities.GetEffectiveModifierFlags((Node)declWithModifiers) & ~(ModifierFlags.Export | ModifierFlags.Ambient);
        return (IDeclaration)_factory.ReplaceModifiers(newDecl, modifiers);
    }

    /// <summary>ported from: src/compiler/checker.ts:6607-6613 — <c>simplifyInterfaceDeclaration</c>.</summary>
    private IDeclaration? SimplifyInterfaceDeclaration(InterfaceDeclaration interfaceDecl, ISymbol symbol, SymbolFlags meaning)
    {
        if ((meaning & SymbolFlags.Interface) == 0)
        {
            return null;
        }
        return SimplifyModifiers(interfaceDecl, NodeTests.IsInterfaceDeclaration, symbol);
    }

    /// <summary>ported from: src/compiler/checker.ts:6614-6625 — <c>symbolToDeclarationsWorker</c>.</summary>
    private List<IStatement> SymbolToDeclarationsWorker(ISymbol symbol, NodeBuilderContext context)
    {
        // We're already expanding the type: set it in the stack to avoid expanding it again.
        var type = GetDeclaredTypeOfSymbol(symbol);
        context.TypeStack!.Add(type.Id);
        context.TypeStack.Add(-1);
        var table = Utilities.CreateSymbolTable([symbol]);
        var statements = SymbolTableToDeclarationStatements(table, context);
        context.TypeStack.RemoveAt(context.TypeStack.Count - 1);
        context.TypeStack.RemoveAt(context.TypeStack.Count - 1);
        return statements;
    }

    /// <summary>ported from: src/compiler/checker.ts:6626-6688 — <c>withContext</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter order and defaults are kept; <c>out</c> is required in C# because callers that omit
    /// it pass <c>undefined</c> explicitly in the reference, and the port prefers an explicit <c>null</c> at the call
    /// site over a second overload set (the node builder has 12 callers).
    /// </remarks>
    internal T? WithContext<T>(
        Node? enclosingDeclaration,
        NodeBuilderFlags? flags,
        InternalNodeBuilderFlags? internalFlags,
        ISymbolTracker? tracker,
        int? maximumLength,
        int? verbosityLevel,
        Func<NodeBuilderContext, T> cb,
        IWriterContextOut? @out = null)
    {
        var moduleResolverHost = tracker?.ModuleResolverHost ?? CreateBasicNodeBuilderModuleSpecifierResolutionHost(_host);
        flags ??= NodeBuilderFlags.None;
        var maxTruncationLength = maximumLength
            ?? ((flags.Value & NodeBuilderFlags.NoTruncation) != 0 ? Utilities.NoTruncationMaximumTruncationLength : Utilities.DefaultMaximumTruncationLength);
        var context = new NodeBuilderContext
        {
            EnclosingDeclaration = enclosingDeclaration,
            EnclosingFile = enclosingDeclaration is not null ? Utilities.GetSourceFileOfNode(enclosingDeclaration) : null,
            Flags = flags.Value,
            InternalFlags = internalFlags ?? InternalNodeBuilderFlags.None,
            MaxTruncationLength = maxTruncationLength,
            MaxExpansionDepth = verbosityLevel ?? -1,
            EncounteredError = false,
            SuppressReportInferenceFallback = false,
            ReportedDiagnostic = false,
            VisitedTypes = null,
            SymbolDepth = null,
            InferTypeParameters = null,
            ApproximateLength = 0,
            TrackedSymbols = null,
            Bundled = _compilerOptions.OutFile is not null && enclosingDeclaration is not null && Utilities.IsExternalOrCommonJsModule(Utilities.GetSourceFileOfNode(enclosingDeclaration)!),
            Truncating = false,
            UsedSymbolNames = null,
            RemappedSymbolNames = null,
            RemappedSymbolReferences = null,
            ReverseMappedStack = null,
            MustCreateTypeParameterSymbolList = true,
            TypeParameterSymbolList = null,
            MustCreateTypeParametersNamesLookups = true,
            TypeParameterNames = null,
            TypeParameterNamesByText = null,
            TypeParameterNamesByTextNextNameCount = null,
            EnclosingSymbolTypes = [],
            Mapper = null,
            Depth = 0,
            TypeStack = [],
            Out = new WriterContextOut(),
        };
        context.Tracker = new SymbolTrackerImpl(context, tracker, moduleResolverHost);
        var resultingNode = cb(context);
        if (context.Truncating && (context.Flags & NodeBuilderFlags.NoTruncation) != 0)
        {
            context.Tracker.ReportTruncationError();
        }
        if (@out is not null)
        {
            @out.CanIncreaseExpansionDepth = context.Out!.CanIncreaseExpansionDepth;
            @out.Truncated = context.Out.Truncated;
        }
        return context.EncounteredError ? default : resultingNode;
    }

    /// <summary>ported from: src/compiler/checker.ts:6689-6702 — <c>addSymbolTypeToContext</c>.</summary>
    internal Action AddSymbolTypeToContext(NodeBuilderContext context, ISymbol symbol, IType type)
    {
        var id = Utilities.GetSymbolId(symbol);
        var oldType = context.EnclosingSymbolTypes!.GetValueOrDefault(id);
        context.EnclosingSymbolTypes![id] = type;
        return Restore;

        void Restore()
        {
            if (oldType is not null)
            {
                context.EnclosingSymbolTypes[id] = oldType;
            }
            else
            {
                context.EnclosingSymbolTypes.Remove(id);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:6703-6716 — <c>saveRestoreFlags</c>.</summary>
    internal Action SaveRestoreFlags(NodeBuilderContext context)
    {
        var flags = context.Flags;
        var internalFlags = context.InternalFlags;
        var depth = context.Depth;

        return Restore;

        void Restore()
        {
            context.Flags = flags;
            context.InternalFlags = internalFlags;
            context.Depth = depth;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:6717-6720 — <c>checkTruncationLengthIfExpanding</c>.</summary>
    internal bool CheckTruncationLengthIfExpanding(NodeBuilderContext context)
        => context.MaxExpansionDepth >= 0 && CheckTruncationLength(context);

    /// <summary>ported from: src/compiler/checker.ts:6721-6728 — <c>checkTruncationLength</c>.</summary>
    internal bool CheckTruncationLength(NodeBuilderContext context)
    {
        if (context.Truncating) return true;
        context.Truncating = context.ApproximateLength > context.MaxTruncationLength;
        return context.Truncating;
    }

    /// <summary>
    /// ported from: src/compiler/checker.ts:6729-6742 — <c>canPossiblyExpandType</c>.
    /// </summary>
    /// <remarks>Returns true if it's possible the type or one of its components can be expanded.</remarks>
    internal bool CanPossiblyExpandType(IType type, NodeBuilderContext context)
    {
        for (var i = 0; i < context.TypeStack!.Count - 1; i++)
        {
            if (context.TypeStack[i] == type.Id)
            {
                return false;
            }
        }
        return context.Depth < context.MaxExpansionDepth
            || (context.Depth == context.MaxExpansionDepth && !context.Out!.CanIncreaseExpansionDepth);
    }

    /// <summary>ported from: src/compiler/checker.ts:6743-6759 — <c>shouldExpandType</c>.</summary>
    /// <remarks>Determines if the input type should be expanded, based on how many layers of names we're allowed to expand.</remarks>
    internal bool ShouldExpandType(IType type, NodeBuilderContext context, bool isAlias = false)
    {
        if (!isAlias && IsLibType(type))
        {
            return false;
        }
        for (var i = 0; i < context.TypeStack!.Count - 1; i++)
        {
            if (context.TypeStack[i] == type.Id)
            {
                return false;
            }
        }
        var result = context.Depth < context.MaxExpansionDepth;
        if (!result)
        {
            // This type would have been expanded if `maxExpansionDepth` was increased.
            context.Out!.CanIncreaseExpansionDepth = true;
        }
        return result;
    }


    /// <summary>ported from: src/compiler/checker.ts:54260-54278 — <c>createBasicNodeBuilderModuleSpecifierResolutionHost</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS builds an object that overrides the host's optional members and guards <c>getCommonSourceDirectory</c>
    /// on the host actually being a <c>Program</c>. The port's <see cref="IModuleSpecifierResolutionHost"/> declares every
    /// member required, so the class forwards the whole set to the host (which always implements it —
    /// <see cref="ITypeCheckerHost"/> derives from it), and <c>getCommonSourceDirectory</c> needs no guard.
    /// </remarks>
    internal IModuleSpecifierResolutionHost CreateBasicNodeBuilderModuleSpecifierResolutionHost(ITypeCheckerHost host)
        => new BasicNodeBuilderModuleSpecifierResolutionHost(host);

    /// <summary>ported from: src/compiler/checker.ts:1550 — <c>createResolver()</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS assigns <c>emitResolver = createResolver()</c> in the state block at :1550, i.e. before
    /// <c>initializeTypeChecker</c>. The port's <c>_emitResolver</c> slot is declared in <c>Checker.cs</c> and is
    /// assigned by M6-5's <c>InitializeState</c>, which must add <c>_emitResolver = CreateResolver();</c> — the class
    /// that factory builds is <see cref="EmitResolver"/>.
    /// </remarks>
    internal IEmitResolver CreateResolver() => new EmitResolver(this);
}

/// <summary>ported from: src/compiler/checker.ts:54260-54278 — the <c>ModuleSpecifierResolutionHost</c> literal.</summary>
/// <remarks>
/// PORT-NOTE: TS's <c>createBasicNodeBuilderModuleSpecifierResolutionHost</c> returns an object literal typed
/// <c>ModuleSpecifierResolutionHost</c> (an interface of largely optional members); the port has no anonymous object
/// types, so the literal is a class. Members TS leaves undefined (<c>directoryExists</c>, <c>realpath</c>,
/// <c>getModuleSpecifierCache</c>, <c>getNearestAncestorDirectoryWithPackageJson</c>, <c>trace</c>,
/// <c>getModuleResolutionCache</c>) are required by the generated interface and are forwarded to the same host, which
/// is the only sensible source for them.
/// </remarks>
internal sealed class BasicNodeBuilderModuleSpecifierResolutionHost : IModuleSpecifierResolutionHost
{
    private readonly ITypeCheckerHost _host;

    /// <summary>ported from: src/compiler/checker.ts:54260 — <c>host</c>.</summary>
    public BasicNodeBuilderModuleSpecifierResolutionHost(ITypeCheckerHost host) => _host = host;

    /// <summary>ported from: src/compiler/checker.ts:54262 — <c>getCommonSourceDirectory</c>.</summary>
    /// <remarks>PORT-NOTE: TS guards on the host being a <c>Program</c> and answers <c>""</c> otherwise; the port's host interface always carries the member.</remarks>
    public string GetCommonSourceDirectory() => ((IModuleSpecifierResolutionHost)_host).GetCommonSourceDirectory();

    /// <summary>ported from: src/compiler/checker.ts:54263 — <c>getCurrentDirectory</c>.</summary>
    public string GetCurrentDirectory() => ((IModuleSpecifierResolutionHost)_host).GetCurrentDirectory();

    /// <summary>ported from: src/compiler/checker.ts:54264 — <c>getSymlinkCache</c>.</summary>
    public object? GetSymlinkCache() => _host.GetSymlinkCache();

    /// <summary>ported from: src/compiler/checker.ts:54265 — <c>getPackageJsonInfoCache</c>.</summary>
    public object? GetPackageJsonInfoCache() => _host.GetPackageJsonInfoCache();

    /// <summary>ported from: src/compiler/checker.ts:54266 — <c>useCaseSensitiveFileNames</c>.</summary>
    public bool UseCaseSensitiveFileNames() => ((IModuleSpecifierResolutionHost)_host).UseCaseSensitiveFileNames();

    /// <summary>ported from: src/compiler/checker.ts:54267 — <c>redirectTargetsMap</c>.</summary>
    public Dictionary<string, List<string>>? RedirectTargetsMap => _host.RedirectTargetsMap;

    /// <summary>ported from: src/compiler/checker.ts:54268 — <c>getRedirectFromSourceFile</c>.</summary>
    public IResolvedRefAndOutputDts? GetRedirectFromSourceFile(string? fileName) => ((IModuleSpecifierResolutionHost)_host).GetRedirectFromSourceFile(fileName);

    /// <summary>ported from: src/compiler/checker.ts:54269 — <c>isSourceOfProjectReferenceRedirect</c>.</summary>
    public bool IsSourceOfProjectReferenceRedirect(string? fileName) => ((IModuleSpecifierResolutionHost)_host).IsSourceOfProjectReferenceRedirect(fileName);

    /// <summary>ported from: src/compiler/checker.ts:54270 — <c>fileExists</c>.</summary>
    public bool FileExists(string? fileName) => _host.FileExists(fileName);

    /// <summary>ported from: src/compiler/checker.ts:54271 — <c>getFileIncludeReasons</c>.</summary>
    public Dictionary<string, List<IFileIncludeReason>> GetFileIncludeReasons() => _host.GetFileIncludeReasons();

    /// <summary>ported from: src/compiler/checker.ts:54272 — <c>readFile</c>.</summary>
    public string? ReadFile(string? fileName) => _host.ReadFile(fileName);

    /// <summary>ported from: src/compiler/checker.ts:54273 — <c>getDefaultResolutionModeForFile</c>.</summary>
    public object? GetDefaultResolutionModeForFile(SourceFile? file) => _host.GetDefaultResolutionModeForFile(file);

    /// <summary>ported from: src/compiler/checker.ts:54274 — <c>getModeForResolutionAtIndex</c>.</summary>
    public object? GetModeForResolutionAtIndex(SourceFile? file, int index) => _host.GetModeForResolutionAtIndex(file, index);

    /// <summary>ported from: src/compiler/checker.ts:54275 — <c>getGlobalTypingsCacheLocation</c>.</summary>
    public string? GetGlobalTypingsCacheLocation() => _host.GetGlobalTypingsCacheLocation();

    /// <summary>PORT-NOTE: TS's literal leaves this undefined; the generated interface requires it, so it forwards.</summary>
    public bool DirectoryExists(string? path) => _host.DirectoryExists(path);

    /// <summary>PORT-NOTE: TS's literal leaves this undefined; the generated interface requires it, so it forwards.</summary>
    public string Realpath(string? path) => _host.Realpath(path);

    /// <summary>PORT-NOTE: TS's literal leaves this undefined; the generated interface requires it, so it forwards.</summary>
    public IModuleSpecifierCache GetModuleSpecifierCache() => _host.GetModuleSpecifierCache();

    /// <summary>PORT-NOTE: TS's literal leaves this undefined; the generated interface requires it, so it forwards.</summary>
    public string? GetNearestAncestorDirectoryWithPackageJson(string? fileName, string? rootDir) => _host.GetNearestAncestorDirectoryWithPackageJson(fileName, rootDir);

    /// <summary>PORT-NOTE: TS's literal leaves this undefined; the generated interface requires it, so it forwards.</summary>
    public object? GetModuleResolutionCache() => _host.GetModuleResolutionCache();

    /// <summary>PORT-NOTE: TS's literal leaves this undefined; the generated interface requires it, so it forwards.</summary>
    public void Trace(string? s) => _host.Trace(s);
}

/// <summary>ported from: src/compiler/checker.ts:6678-6681 — the <c>out</c> object <c>withContext</c> creates.</summary>
/// <remarks>
/// PORT-NOTE: TS's <c>out: { canIncreaseExpansionDepth: false, truncated: false }</c> is an inline literal typed
/// <c>WriterContextOut</c>; the port has no anonymous object types, so the literal is a class.
/// </remarks>
internal sealed class WriterContextOut : IWriterContextOut
{
    /// <summary>ported from: src/compiler/checker.ts:6679 — <c>canIncreaseExpansionDepth</c>.</summary>
    public bool CanIncreaseExpansionDepth { get; set; }

    /// <summary>ported from: src/compiler/checker.ts:6680 — <c>truncated</c>.</summary>
    public bool Truncated { get; set; }
}
