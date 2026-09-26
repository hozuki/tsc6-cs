namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:1486-1600 (the state createTypeChecker opens with)
// ported from: src/compiler/checker.ts:2357-2358 (the two id-indexed link tables)
// ported from: src/compiler/checker.ts:2932-2941 (getSymbolLinks, getNodeLinks)
/// <summary>
/// The type checker — TS's <c>createTypeChecker</c> closure, as an instance class.
/// </summary>
/// <remarks>
/// TS's checker is one 54,000-line closure whose state is a few dozen <c>var</c>s and whose members are a few
/// thousand nested functions; the port gives it the treatment <c>parser.ts</c> and <c>binder.ts</c> already got
/// (§15.10, §15.13): the state becomes instance fields and the members become members of this class, split across
/// partial files by the checker's own sections.
/// <para>
/// The partial files this class is meant to span, and what goes in each (a slice adds to the file named here, not to
/// a new one):
/// <list type="bullet">
/// <item><c>Checker/Checker.cs</c> — this file: the state block, the two link tables, <c>getSymbolLinks</c>/
/// <c>getNodeLinks</c>, and <c>createTypeChecker</c>'s assembly of the <c>TypeChecker</c> object (M6-5/M6-6).</item>
/// <item><c>Checker/Checker.Symbols.cs</c> — <c>createSymbol</c> and the symbol queries (<c>getSymbolLinks</c>'s
/// neighbours: <c>getCheckFlags</c>, <c>getMergedSymbol</c>, <c>resolveSymbol</c>), M6-2.</item>
/// <item><c>Checker/Checker.Types.cs</c> — <c>createType</c> and the type constructors and queries
/// (checker.ts:5517-11573), M6-2.</item>
/// <item><c>Checker/Checker.Relations.cs</c> — the assignability/subtype/identity relation and its three caches
/// (checker.ts:21271-26773), M6-3.</item>
/// <item><c>Checker/Checker.Inference.cs</c> — inference, contextual typing and call resolution
/// (checker.ts:26774-32780, 34740-37809), M6-4.</item>
/// <item><c>Checker/Checker.Check.cs</c> — <c>checkExpression</c> and the declaration checks, diagnostics and
/// <c>checkSourceFile</c> (checker.ts:37810-51554), M6-5.</item>
/// <item><c>Checker/Checker.NodeBuilder.cs</c> — <c>createNodeBuilder</c>, <c>createResolver</c> and the string
/// converters (checker.ts:5980-11323, 51380-54434), M6-6.</item>
/// <item><c>Checker/CheckerEnums.cs</c> — the module-level enums (checker.ts:1157-1450).</item>
/// </list>
/// </para>
/// <para>
/// PORT-NOTE: the state slots and the members TS nests inside <c>createTypeChecker</c> are <c>internal</c> rather
/// than <c>private</c>: they are the checker's state, not any one slice's, and the slices above read and write them
/// from sibling partial files. A <c>private</c> slot no member of the assembly reads is a warning (CS0169/CS0414,
/// IDE0051) and this project treats warnings as errors — the same reason <c>Binder.State.cs</c> states its own state
/// as <c>internal</c>.
/// </para>
/// <para>
/// PORT-NOTE: <c>objectAllocator.getSymbolConstructor()</c>/<c>getTypeConstructor()</c>/<c>getSignatureConstructor()</c>
/// (checker.ts:1509-1511) are not ported (§15.2) — the port has no object allocator, so the <c>Symbol</c>/<c>Type</c>/
/// <c>Signature</c> locals do not exist and <c>createSymbol</c>/<c>createType</c>/<c>createSignature</c> construct
/// <see cref="Symbol"/>/<see cref="Type"/>/<see cref="Signature"/> directly.
/// </para>
/// <para>
/// PORT-NOTE: the state TS creates by calling a factory that this milestone has not ported yet is not declared here
/// rather than declared with a placeholder value, so that nothing can read a silently wrong one. Each has its slot
/// added by the slice that lands its factory, with the reference initializer:
/// <list type="bullet">
/// <item><c>moduleKind = getEmitModuleKind(compilerOptions)</c> (checker.ts:1530),
/// <c>useDefineForClassFields</c> (:1532), <c>emitStandardClassFields</c> (:1533),
/// <c>allowSyntheticDefaultImports</c> (:1534) — the four <c>utilities.ts</c> option helpers the M6 utilities slice
/// ports.</item>
/// <item><c>checkBinaryExpression = createCheckBinaryExpression()</c> (:1549) — M6-5.</item>
/// <item><c>nodeBuilder = createNodeBuilder()</c> (:1551) and
/// <c>syntacticNodeBuilder = createSyntacticTypeNodeBuilder(compilerOptions, nodeBuilder.syntacticBuilderResolver)</c>
/// (:1552) — M6-6 (with <c>expressionToTypeNode.ts</c>).</item>
/// <item><c>evaluate = createEvaluator({ … })</c> (:1553) — the M6 utilities slice
/// (<c>utilities.ts:11316</c>).</item>
/// <item><c>resolveName</c> (:1579) and <c>resolveNameForSymbolSuggestion</c> (:1594), both
/// <c>createNameResolver({ … })</c> — the M6 utilities slice (<c>utilities.ts:11491</c>), whose C# shape that slice
/// decides. The globals they close over (<see cref="_globals"/>, <see cref="_requireSymbol"/>,
/// <see cref="_argumentsSymbol"/>) are already here.</item>
/// </list>
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    /// <summary>ported from: src/compiler/checker.ts:1528 — <c>compilerOptions</c>.</summary>
    internal readonly ICompilerOptions _compilerOptions;

    /// <summary>
    /// Creates the checker for a host.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS states these as <c>var</c>s at the top of <c>createTypeChecker</c> (checker.ts:1490-1592) and
    /// the port folds every initial value it can express today into this constructor, as <c>Binder</c> does with its
    /// own state (<c>Binder.State.cs</c>). The remaining reference initializers either need a factory that is not
    /// ported yet (listed on the class, each naming its slice) or are <c>createSymbol</c> calls (M6-2); those slots
    /// are filled by <c>createTypeChecker</c>'s assembly of the checker, which is M6-5/M6-6's
    /// <c>Checker.cs</c> member.
    /// </remarks>
    internal Checker(ITypeCheckerHost host)
    {
        _compilerOptions = host.GetCompilerOptions();

        // checker.ts:1529, 1531, 1535-1547, 1569-1570
        _languageVersion = Utilities.GetEmitScriptTarget(_compilerOptions);
        _legacyDecorators = Js.IsTruthy(_compilerOptions.ExperimentalDecorators);
        _strictNullChecks = Utilities.GetStrictOptionValue(_compilerOptions, "strictNullChecks");
        _strictFunctionTypes = Utilities.GetStrictOptionValue(_compilerOptions, "strictFunctionTypes");
        _strictBindCallApply = Utilities.GetStrictOptionValue(_compilerOptions, "strictBindCallApply");
        _strictPropertyInitialization = Utilities.GetStrictOptionValue(_compilerOptions, "strictPropertyInitialization");
        _strictBuiltinIteratorReturn = Utilities.GetStrictOptionValue(_compilerOptions, "strictBuiltinIteratorReturn");
        _noImplicitAny = Utilities.GetStrictOptionValue(_compilerOptions, "noImplicitAny");
        _noImplicitThis = Utilities.GetStrictOptionValue(_compilerOptions, "noImplicitThis");
        _useUnknownInCatchVariables = Utilities.GetStrictOptionValue(_compilerOptions, "useUnknownInCatchVariables");
        _exactOptionalPropertyTypes = _compilerOptions.ExactOptionalPropertyTypes;
        _noUncheckedSideEffectImports = _compilerOptions.NoUncheckedSideEffectImports != false;
        _stableTypeOrdering = Js.IsTruthy(_compilerOptions.StableTypeOrdering);
        _isolatedModulesLikeFlagName = Js.IsTruthy(_compilerOptions.VerbatimModuleSyntax) ? "verbatimModuleSyntax" : "isolatedModules";
        _canCollectSymbolAliasAccessabilityData = !Js.IsTruthy(_compilerOptions.VerbatimModuleSyntax);

        // checker.ts:1547 — `stableTypeOrdering ? new Map(host.getSourceFiles().map((file, i) => [file, i])) : undefined`.
        // PORT-NOTE: the key is the source file itself, and TS's Map is keyed by object identity; the port relies on
        // Node not overriding Equals/GetHashCode, which it does not (§12.7).
        if (_stableTypeOrdering)
        {
            _fileIndexMap = new Dictionary<Node, int>();
            var sourceFiles = host.GetSourceFiles();
            for (var i = 0; i < sourceFiles.Count; i++)
            {
                _fileIndexMap[sourceFiles[i]] = i;
            }
        }

        // checker.ts:1558, 1563-1565 — `globals` is created first because `globalThisSymbol` publishes itself into it.
        _globals = Utilities.CreateSymbolTable();
    }

    // ---------------------------------------------------------------------------------------------
    // The lazy diagnostic queue (checker.ts:1490-1494)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:1490 — <c>deferredDiagnosticsCallbacks</c>.</summary>
    internal readonly List<Action> _deferredDiagnosticsCallbacks = [];

    /// <summary>ported from: src/compiler/checker.ts:1492-1494 — <c>addLazyDiagnostic</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's arrow function is stored by the factory functions it is passed to; the port states it as a
    /// member of this class because <c>addLazyDiagnostic(() => …)</c> is called from the checking members (M6-5).
    /// </remarks>
    internal void AddLazyDiagnostic(Action arg)
    {
        _deferredDiagnosticsCallbacks.Add(arg);
    }

    // ---------------------------------------------------------------------------------------------
    // Cancellation and the shared scanner (checker.ts:1505-1507)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:1505 — <c>cancellationToken</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's comment above this slot is the contract for it (checker.ts:1496-1504): cancelling is
    /// not safe mid-check, so the host may only set it while diagnostics are being computed, and a cancelled checker
    /// must be thrown away rather than reused.
    /// </remarks>
    internal ICancellationToken? _cancellationToken;

    /// <summary>ported from: src/compiler/checker.ts:1507 — <c>scanner</c>.</summary>
    internal Scanner? _scanner;

    // ---------------------------------------------------------------------------------------------
    // Counters (checker.ts:1513-1518)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:1513 — <c>typeCount</c>.</summary>
    internal int _typeCount;

    /// <summary>ported from: src/compiler/checker.ts:1514 — <c>symbolCount</c>.</summary>
    internal int _symbolCount;

    /// <summary>ported from: src/compiler/checker.ts:1515 — <c>totalInstantiationCount</c>.</summary>
    internal int _totalInstantiationCount;

    /// <summary>ported from: src/compiler/checker.ts:1516 — <c>instantiationCount</c>.</summary>
    internal int _instantiationCount;

    /// <summary>ported from: src/compiler/checker.ts:1517 — <c>instantiationDepth</c>.</summary>
    internal int _instantiationDepth;

    /// <summary>ported from: src/compiler/checker.ts:1518 — <c>inlineLevel</c>.</summary>
    internal int _inlineLevel;

    // ---------------------------------------------------------------------------------------------
    // The current position of the walk (checker.ts:1519-1523)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:1519 — <c>currentNode</c>.</summary>
    internal Node? _currentNode;

    /// <summary>ported from: src/compiler/checker.ts:1520 — <c>varianceTypeParameter</c>.</summary>
    internal ITypeParameter? _varianceTypeParameter;

    /// <summary>ported from: src/compiler/checker.ts:1521 — <c>isInferencePartiallyBlocked</c>.</summary>
    internal bool _isInferencePartiallyBlocked;

    /// <summary>ported from: src/compiler/checker.ts:1522 — <c>withinUnreachableCode</c>.</summary>
    internal bool _withinUnreachableCode;

    /// <summary>ported from: src/compiler/checker.ts:1523 — <c>reportedUnreachableNodes</c>.</summary>
    internal HashSet<Node>? _reportedUnreachableNodes;

    // ---------------------------------------------------------------------------------------------
    // Tables the checker owns (checker.ts:1525-1526, 1558)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:1525 — <c>emptySymbols</c>.</summary>
    internal readonly SymbolTable _emptySymbols = Utilities.CreateSymbolTable();

    /// <summary>ported from: src/compiler/checker.ts:1526 — <c>arrayVariances</c>.</summary>
    internal readonly List<VarianceFlags> _arrayVariances = [VarianceFlags.Covariant];

    /// <summary>ported from: src/compiler/checker.ts:1558 — <c>globals</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS creates this at :1558, after <c>globalThisSymbol</c> has already been constructed at :1562, and
    /// then publishes that symbol into it at :1563-1565 (<c>globalThisSymbol.exports = globals</c> and
    /// <c>globals.set(globalThisSymbol.escapedName, globalThisSymbol)</c>). The port creates the table in the
    /// constructor instead (there is no ordering to preserve here, only the resulting state), and
    /// <see cref="_globalThisSymbol"/> is filled in by <c>createTypeChecker</c>'s assembly (M6-5/M6-6) together with
    /// that publish pair.
    /// </remarks>
    internal readonly SymbolTable _globals;

    // ---------------------------------------------------------------------------------------------
    // Option-derived flags (checker.ts:1529-1545)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:1529 — <c>languageVersion</c>.</summary>
    internal readonly ScriptTarget _languageVersion;

    /// <summary>ported from: src/compiler/checker.ts:1531 — <c>legacyDecorators</c>.</summary>
    internal readonly bool _legacyDecorators;

    /// <summary>ported from: src/compiler/checker.ts:1535 — <c>strictNullChecks</c>.</summary>
    internal readonly bool _strictNullChecks;

    /// <summary>ported from: src/compiler/checker.ts:1536 — <c>strictFunctionTypes</c>.</summary>
    internal readonly bool _strictFunctionTypes;

    /// <summary>ported from: src/compiler/checker.ts:1537 — <c>strictBindCallApply</c>.</summary>
    internal readonly bool _strictBindCallApply;

    /// <summary>ported from: src/compiler/checker.ts:1538 — <c>strictPropertyInitialization</c>.</summary>
    internal readonly bool _strictPropertyInitialization;

    /// <summary>ported from: src/compiler/checker.ts:1539 — <c>strictBuiltinIteratorReturn</c>.</summary>
    internal readonly bool _strictBuiltinIteratorReturn;

    /// <summary>ported from: src/compiler/checker.ts:1540 — <c>noImplicitAny</c>.</summary>
    internal readonly bool _noImplicitAny;

    /// <summary>ported from: src/compiler/checker.ts:1541 — <c>noImplicitThis</c>.</summary>
    internal readonly bool _noImplicitThis;

    /// <summary>ported from: src/compiler/checker.ts:1542 — <c>useUnknownInCatchVariables</c>.</summary>
    internal readonly bool _useUnknownInCatchVariables;

    /// <summary>ported from: src/compiler/checker.ts:1543 — <c>exactOptionalPropertyTypes</c>.</summary>
    /// <remarks>PORT-NOTE: TS's slot is <c>boolean | undefined</c>; the generated option member is <c>bool?</c>.</remarks>
    internal readonly bool? _exactOptionalPropertyTypes;

    /// <summary>ported from: src/compiler/checker.ts:1544 — <c>noUncheckedSideEffectImports</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>!== false</c> makes the absent option default to <c>true</c>.</remarks>
    internal readonly bool _noUncheckedSideEffectImports;

    /// <summary>ported from: src/compiler/checker.ts:1545 — <c>stableTypeOrdering</c>.</summary>
    internal readonly bool _stableTypeOrdering;

    /// <summary>ported from: src/compiler/checker.ts:1547 — <c>fileIndexMap</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>Map&lt;SourceFile, number&gt;</c> is keyed by identity and read by the stable type-ordering
    /// path only; the port's <see cref="Dictionary{TKey,TValue}"/> with a <see cref="Node"/> key is the same identity
    /// map (Node does not override <c>Equals</c>/<c>GetHashCode</c>).
    /// </remarks>
    internal readonly Dictionary<Node, int>? _fileIndexMap;

    /// <summary>ported from: src/compiler/checker.ts:1569 — <c>isolatedModulesLikeFlagName</c>.</summary>
    internal readonly string _isolatedModulesLikeFlagName;

    /// <summary>ported from: src/compiler/checker.ts:1570 — <c>canCollectSymbolAliasAccessabilityData</c>.</summary>
    internal readonly bool _canCollectSymbolAliasAccessabilityData;

    // ---------------------------------------------------------------------------------------------
    // The three globals the resolver closes over (checker.ts:1559-1568)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:1559 — <c>undefinedSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: assigned by <c>createTypeChecker</c>'s assembly as
    /// <c>CreateSymbol(SymbolFlags.Property, "undefined")</c> with <c>Declarations = []</c> (M6-2's
    /// <c>CreateSymbol</c>).
    /// </remarks>
    internal ISymbol? _undefinedSymbol;

    /// <summary>ported from: src/compiler/checker.ts:1562 — <c>globalThisSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: assigned by <c>createTypeChecker</c>'s assembly as
    /// <c>CreateSymbol(SymbolFlags.Module, "globalThis", CheckFlags.Readonly)</c>, followed by
    /// <c>exports = globals</c>, <c>declarations = []</c> and
    /// <c>globals.set(globalThisSymbol.escapedName, globalThisSymbol)</c> (checker.ts:1562-1565). <c>globals.set</c>'s
    /// key is the escaped name, which a symbol always has — the port's <c>EscapedName</c> is <c>string?</c> and the
    /// table asserts it.
    /// </remarks>
    internal ISymbol? _globalThisSymbol;

    /// <summary>ported from: src/compiler/checker.ts:1567 — <c>argumentsSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: assigned by <c>createTypeChecker</c>'s assembly as
    /// <c>CreateSymbol(SymbolFlags.Property, "arguments")</c> (M6-2).
    /// </remarks>
    internal ISymbol? _argumentsSymbol;

    /// <summary>ported from: src/compiler/checker.ts:1568 — <c>requireSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: assigned by <c>createTypeChecker</c>'s assembly as
    /// <c>CreateSymbol(SymbolFlags.Property, "require")</c> (M6-2).
    /// </remarks>
    internal ISymbol? _requireSymbol;

    // ---------------------------------------------------------------------------------------------
    // Scratch state of the walk (checker.ts:1573-1578)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:1573 — <c>apparentArgumentCount</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: set during <c>getResolvedSignature</c> where services determines an apparent number of arguments
    /// greater than what is actually provided (checker.ts:1572).
    /// </remarks>
    internal int? _apparentArgumentCount;

    /// <summary>ported from: src/compiler/checker.ts:1575 — <c>lastGetCombinedNodeFlagsNode</c>.</summary>
    internal Node? _lastGetCombinedNodeFlagsNode;

    /// <summary>ported from: src/compiler/checker.ts:1576 — <c>lastGetCombinedNodeFlagsResult</c>.</summary>
    internal NodeFlags _lastGetCombinedNodeFlagsResult = NodeFlags.None;

    /// <summary>ported from: src/compiler/checker.ts:1577 — <c>lastGetCombinedModifierFlagsNode</c>.</summary>
    internal IDeclaration? _lastGetCombinedModifierFlagsNode;

    /// <summary>ported from: src/compiler/checker.ts:1578 — <c>lastGetCombinedModifierFlagsResult</c>.</summary>
    internal ModifierFlags _lastGetCombinedModifierFlagsResult = ModifierFlags.None;

    // ---------------------------------------------------------------------------------------------
    // The emit resolver (checker.ts:1550)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:1550 — <c>emitResolver</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS initializes this with <c>createResolver()</c>, whose body the port states as a member of
    /// <c>Checker/Checker.NodeBuilder.cs</c> (M6-6). The slot is declared here because its type is the generated
    /// <see cref="IEmitResolver"/> and because <c>getEmitResolver</c> (the checker's public entry point, M7's
    /// declaration emitter and M6-1's <c>Program.getTypeChecker().getEmitResolver</c>) reads it; the assembly member
    /// assigns it.
    /// </remarks>
    internal IEmitResolver? _emitResolver;

    // ---------------------------------------------------------------------------------------------
    // The link tables (checker.ts:2357-2358)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:2357 — <c>symbolLinks</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>SymbolLinks[]</c> is a sparse array grown by assigning past its end, whose holes read as
    /// <c>undefined</c> (<c>symbolLinks[id] ??= new SymbolLinks()</c>, checker.ts:2935). The port pads a
    /// <c>List</c> with <c>null</c> up to the id, the same hole-punching <c>Parser/EmitNodes.cs</c> uses for its
    /// token-keyed table. An entry is null exactly when TS's is a hole.
    /// </remarks>
    private readonly List<SymbolLinks?> _symbolLinks = [];

    /// <summary>ported from: src/compiler/checker.ts:2358 — <c>nodeLinks</c>.</summary>
    /// <remarks>PORT-NOTE: as <see cref="_symbolLinks"/>; TS's element factory is <c>new (NodeLinks as any)()</c>.</remarks>
    private readonly List<NodeLinks?> _nodeLinks = [];

    /// <summary>ported from: src/compiler/checker.ts:2932-2936 — <c>getSymbolLinks</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's transient arm returns <c>symbol.links</c> typed <c>TransientSymbolLinks</c> — the symbol is
    /// its own links holder. The port reads the same slot through <see cref="ITransientSymbol.Links"/> and asserts it
    /// was assigned, which is the invariant <c>createSymbol</c> establishes for every transient symbol
    /// (checker.ts:2635-2637, M6-2).
    /// </remarks>
    internal ISymbolLinks GetSymbolLinks(ISymbol symbol)
    {
        if ((symbol.Flags & SymbolFlags.Transient) != 0)
        {
            return Debug.CheckDefined(((ITransientSymbol)symbol).Links, "a transient symbol must have its links assigned by createSymbol");
        }

        var id = Utilities.GetSymbolId(symbol);
        while (_symbolLinks.Count <= id)
        {
            _symbolLinks.Add(null);
        }

        return _symbolLinks[id] ??= new SymbolLinks();
    }

    /// <summary>ported from: src/compiler/checker.ts:2938-2941 — <c>getNodeLinks</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the slot is keyed by <see cref="Utilities.GetNodeId"/>, not by the node — TS's reference is
    /// <c>nodeLinks[nodeId] || (nodeLinks[nodeId] = new (NodeLinks as any)())</c>, and <c>Node</c> must not gain a
    /// <c>links</c> slot (M6-0 model_gaps 7).
    /// </remarks>
    internal NodeLinks GetNodeLinks(Node node)
    {
        var nodeId = Utilities.GetNodeId(node);
        while (_nodeLinks.Count <= nodeId)
        {
            _nodeLinks.Add(null);
        }

        return _nodeLinks[nodeId] ??= new NodeLinks();
    }
}
