namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:1958-2046 (getTypeArgumentsForResolvedSignature … getResolvedSignatureWorker)
// ported from: src/compiler/checker.ts:2048-2410 (the state block createTypeChecker opens with)
// ported from: src/compiler/checker.ts:51555-51684 (initializeTypeChecker)
/// <summary>
/// The checker's construction: the resolved-signature wrappers that lie just outside the <c>TypeChecker</c> object
/// literal, the state block <c>createTypeChecker</c> opens with, and <c>initializeTypeChecker</c>, which binds the
/// program's files and fills the global symbol table.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: the reference runs checker.ts:2048-2410 as top-level statements of <c>createTypeChecker</c>, before it
/// builds anything else; the port states that block as <see cref="InitializeState"/> plus the field initializers on
/// the slots below, and <c>Checker.CreateTypeChecker.cs</c> (M6Check) calls that one member and then
/// <see cref="InitializeTypeChecker"/>. <see cref="InitializeState"/> delegates checker.ts:2048-2199 (the type caches,
/// the two symbols and every intrinsic/built-in type) to <c>Checker.Types.cs</c>'s <c>InitializeBuiltinTypes</c>,
/// whose factories those are, and fills the rest itself (:2245-2246, :2344-2346, :2380, :2396-2410). It must run
/// before any member that reads a type — <c>GetUnionType</c> needs the caches during <c>InitializeBuiltinTypes</c>,
/// which is why the caches are declared with their reference initializers rather than left to a call order — and
/// <c>InitializeBuiltinTypes</c> must run exactly once (a second call would create a second set of intrinsic types
/// and shift every type id).
/// </para>
/// <para>
/// PORT-NOTE: a slot of the state block that a sibling slice already declared is not redeclared here; each is named in
/// the region comment where the reference creates it, with its landed owner. The built-in type block (:2078-2199) in
/// particular is landed whole as <c>InitializeBuiltinTypes</c>: re-creating those types here would increment
/// <c>typeCount</c> twice and shift every type id (<c>CreateType</c>, <c>Checker.Types.cs</c>), so it is called out
/// rather than repeated.
/// </para>
/// <para>
/// PORT-NOTE: of the two global slots <c>Checker.Types.cs</c>'s block records as deliberately unported, both are now
/// declared: <c>_globalRegExpType</c> (checker.ts:2265) below in this file, because <c>Checker.Check.cs:153</c> reads
/// it, and <c>_globalThisType</c> (:2266) by the slice that reads it (<c>IType _globalThisType</c>,
/// <c>Checker.Inference.cs:3500</c>); both reference assignments compile.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // The resolved-signature wrappers (checker.ts:1958-2046)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:1958-1961 — <c>getTypeArgumentsForResolvedSignature</c>.</summary>
    internal List<IType>? GetTypeArgumentsForResolvedSignature(ISignature signature)
    {
        if (signature.Mapper is null) return null;
        return InstantiateTypes(TypeListOfParameters((signature.Target ?? signature).TypeParameters), (TypeMapper)signature.Mapper);
    }

    /// <summary>ported from: src/compiler/checker.ts:1963-1983 — <c>getCandidateSignaturesForStringLiteralCompletions</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>Set&lt;Signature&gt;</c> iterates in insertion order and that order is the order the
    /// candidates are offered in; <c>HashSet&lt;T&gt;</c> iterates in bucket order, so the port keeps an ordered
    /// <c>List</c> and deduplicates with <c>Core.PushIfUnique</c>, the same identity-keyed ordered set
    /// <c>Checker.Symbols.cs</c>'s duplicate-location collection uses.
    /// </remarks>
    internal List<ISignature> GetCandidateSignaturesForStringLiteralCompletions(Node call, Node editingArgument)
    {
        List<ISignature> candidatesSet = [];
        List<ISignature> candidates = [];

        // first, get candidates when inference is blocked from the source node.
        RunWithInferenceBlockedFromSourceNode(editingArgument, () =>
        {
            GetResolvedSignatureWorker(call, candidates, /*argumentCount*/ null, CheckMode.Normal);
            return (object?)null;
        });
        foreach (var candidate in candidates)
        {
            Core.PushIfUnique(candidatesSet, candidate);
        }

        // reset candidates for second pass
        candidates.Clear();

        // next, get candidates where the source node is considered for inference.
        RunWithoutResolvedSignatureCaching(editingArgument, () =>
        {
            GetResolvedSignatureWorker(call, candidates, /*argumentCount*/ null, CheckMode.Normal);
            return (object?)null;
        });
        foreach (var candidate in candidates)
        {
            Core.PushIfUnique(candidatesSet, candidate);
        }

        return [.. candidatesSet];
    }

    /// <summary>ported from: src/compiler/checker.ts:1985-2012 — <c>runWithoutResolvedSignatureCaching</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>fn: () =&gt; T</c> becomes <c>Func&lt;T&gt;</c>, the shape
    /// <c>Program</c>'s <c>RunWithCancellationToken&lt;T&gt;</c> uses
    /// (<c>Program/Program.DiagnosticsAggregation.cs:220</c>). <c>isCallLikeOrFunctionLikeExpression</c>
    /// (utilitiesPublic.ts:1964) is a <c>Utilities</c> member that slice has not landed yet; it is called qualified
    /// and reported.
    /// </remarks>
    internal T RunWithoutResolvedSignatureCaching<T>(Node? node, Func<T> fn)
    {
        node = Utilities.FindAncestor(node, Utilities.IsCallLikeOrFunctionLikeExpression);
        if (node is not null)
        {
            // TS infers these two tuples' element types from the `as const` literals it pushes.
            List<(NodeLinks NodeLinks, ISignature? ResolvedSignature)> cachedResolvedSignatures = [];
            List<(ISymbolLinks SymbolLinks, IType? Type)> cachedTypes = [];
            while (node is not null)
            {
                var nodeLinks = GetNodeLinks(node);
                cachedResolvedSignatures.Add((nodeLinks, nodeLinks.ResolvedSignature));
                nodeLinks.ResolvedSignature = null;
                if (Utilities.IsFunctionExpressionOrArrowFunction(node))
                {
                    var symbolLinks = GetSymbolLinks(GetSymbolOfDeclaration((IDeclaration)node));
                    var type = symbolLinks.Type;
                    cachedTypes.Add((symbolLinks, type));
                    symbolLinks.Type = null;
                }
                node = Utilities.FindAncestor(node.Parent, Utilities.IsCallLikeOrFunctionLikeExpression);
            }
            var result = fn();
            foreach (var (nodeLinks, resolvedSignature) in cachedResolvedSignatures)
            {
                nodeLinks.ResolvedSignature = resolvedSignature;
            }
            foreach (var (symbolLinks, type) in cachedTypes)
            {
                symbolLinks.Type = type;
            }
            return result;
        }
        return fn();
    }

    /// <summary>ported from: src/compiler/checker.ts:2014-2038 — <c>runWithInferenceBlockedFromSourceNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>toMarkSkip.do … while (toMarkSkip &amp;&amp; toMarkSkip !== containingCall)</c> walks to the
    /// containing call (or to the end of the tree, since the reference's <c>node!</c> asserts the first step is
    /// safe); the port keeps the same walk with the null-forgiving operator at the dereference.
    /// </remarks>
    internal T RunWithInferenceBlockedFromSourceNode<T>(Node? node, Func<T> fn)
    {
        var containingCall = Utilities.FindAncestor(node, Utilities.IsCallLikeExpression);
        if (containingCall is not null)
        {
            Node? toMarkSkip = node;
            do
            {
                GetNodeLinks(toMarkSkip!).SkipDirectInference = true;
                toMarkSkip = toMarkSkip!.Parent;
            }
            while (toMarkSkip is not null && !ReferenceEquals(toMarkSkip, containingCall));
        }

        _isInferencePartiallyBlocked = true;
        var result = RunWithoutResolvedSignatureCaching(node, fn);
        _isInferencePartiallyBlocked = false;

        if (containingCall is not null)
        {
            Node? toMarkSkip = node;
            do
            {
                GetNodeLinks(toMarkSkip!).SkipDirectInference = null;
                toMarkSkip = toMarkSkip!.Parent;
            }
            while (toMarkSkip is not null && !ReferenceEquals(toMarkSkip, containingCall));
        }

        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:2040-2046 — <c>getResolvedSignatureWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the port's internal <c>getResolvedSignature</c> (<c>Checker.CallResolution.cs:3075</c>) takes
    /// <c>CheckMode?</c> where the reference's third parameter is the optional <c>checkMode?: CheckMode</c>, so the
    /// call below passes this member's <c>CheckMode</c> through the nullable parameter unchanged. The public
    /// <c>ITypeChecker.GetResolvedSignature</c>/<c>GetResolvedSignatureForSignatureHelp</c> surface is
    /// <c>Checker.CreateTypeChecker.cs</c>'s, which forwards to this member.
    /// </remarks>
    internal ISignature? GetResolvedSignatureWorker(Node nodeIn, List<ISignature>? candidatesOutArray, int? argumentCount, CheckMode checkMode)
    {
        var node = Utilities.GetParseTreeNode(nodeIn, Utilities.IsCallLikeExpression);
        _apparentArgumentCount = argumentCount;
        var res = node is null ? null : GetResolvedSignature(node, candidatesOutArray, checkMode);
        _apparentArgumentCount = null;
        return res;
    }

    // ---------------------------------------------------------------------------------------------
    // The state block (checker.ts:2048-2410)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:2048-2410 — the state <c>createTypeChecker</c> opens with.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: what this member does not assign, and why:
    /// </para>
    /// <list type="bullet">
    /// <item>checker.ts:2048-2060 (the type caches <c>tupleTypes</c> … <c>subtypeReductionCache</c>) — each is
    /// declared with its reference initializer by the slice that reads it: <c>Checker.TypeReferences.cs:58</c>
    /// (<c>_tupleTypes</c>), <c>:65</c> (<c>_substitutionTypes</c>), <c>Checker.Unions.cs:66</c>/<c>:74</c>/<c>:81</c>/<c>:90</c>,
    /// <c>Checker.Conditional.cs:49</c>/<c>:59</c>/<c>:66</c>/<c>:69</c>,
    /// <c>Checker.IndexAccess.cs:80</c>/<c>:88</c>/<c>:97</c>. They therefore exist from construction, which the
    /// reference's order also guarantees (they precede every type this block creates).</item>
    /// <item>checker.ts:2061-2076 and :2078-2199 — delegated to <c>InitializeBuiltinTypes</c>
    /// (<c>Checker.Types.cs:365</c>), which this member calls at that point in the reference's order: the four caches
    /// and their two symbols, the intrinsic types, the mappers, the empty/marker/anonymous built-ins, the signatures
    /// and the index infos. It must run exactly once, which is why it is called here and nowhere else — repeating it
    /// would create a second set of intrinsic types and shift every type id. <c>_restrictiveMapper</c>/<c>_permissiveMapper</c>
    /// (:2123-2124) are lazy properties (<c>Checker.Instantiation.cs:61</c>/<c>:66</c>) and <c>_iterationTypesCache</c>,
    /// the two iteration-types resolvers (:2185, :2200, :2216) belong to <c>Checker.Iteration.cs</c>.</item>
    /// <item>checker.ts:2244-2254 and :2256-2299 — <c>_amalgamatedDuplicates</c> (<c>Checker.Symbols.cs:54</c>, reset
    /// by <see cref="InitializeTypeChecker"/> as the reference does at :51562), <c>_patternAmbientModuleAugmentations</c>
    /// (<c>Checker.Symbols.cs:377</c>, which the reference leaves <c>undefined</c> until its first write), the five
    /// globals with landed readers (<c>Checker.Types.cs:255-272</c>), <c>_anyReadonlyArrayType</c> below and the
    /// deferred global slots (<c>Checker.TypeReferences.cs</c>, <c>Checker.Relations.Identical.cs:…</c>) — all declared,
    /// all <c>null</c> until read, which is what the reference's <c>var x: T;</c> means. The exception is the two
    /// reverse-mapped caches, which the reference *creates* at :2245-2246; their slots are declared
    /// <c>null!</c> (<c>Checker.Relations.Identical.cs:42</c>/<c>:2172</c>) and this member is their only writer.</item>
    /// <item>checker.ts:2330-2343, :2348-2358, :2377-2378, :2385-2390 — declared by the slices that read them:
    /// <c>_contextual*</c>/<c>_inferenceContext*</c> (<c>Checker.Inference.cs:60-78</c>), the active-mapper stack
    /// (<c>Checker.Instantiation.cs:54</c>), the resolution stack (<c>Checker.Support.cs</c>), the two link tables and
    /// the merged-symbol list (<c>Checker.cs</c>, <c>Checker.Symbols.cs</c>), the diagnostics collections
    /// (<c>Checker.Diagnostics.cs</c>) and the six relation maps (<c>Checker.Relations.Entries.cs</c>,
    /// <c>Checker.Relations.Matching.cs</c>). <c>_allPotentiallyUnusedIdentifiers</c> (:2319),
    /// <c>_potential*Collisions</c> and <c>_awaitedTypeStack</c> (:2367-2372) are declared below, by this file.</item>
    /// </list>
    /// <para>
    /// The slots whose reference initializer is a literal land as the field initializers below (that is the same
    /// initializer at the same point in the checker's life); the ones that need checker state to build — the two
    /// reverse-mapped caches, the three literal types, <c>typeofType</c> and the extension table — are made here, in
    /// reference order.
    /// </para>
    /// </remarks>
    internal void InitializeState()
    {
        // checker.ts:2048-2199 — the type caches, the two symbols and the whole intrinsic/built-in type block. The
        // factories that build those types live in Checker.Types.cs, so the block is landed there as
        // InitializeBuiltinTypes and invoked here, at its reference position: it must run exactly once, before any
        // member that reads a type. `CreateTypeChecker` calls this member and this one, so it must not call that one a
        // second time — each call creates new intrinsic types and advances `typeCount`, which would shift every type id.
        InitializeBuiltinTypes();

        // checker.ts:2245-2246 — the two reverse-mapped caches, the one pair of the state block whose slots a sibling
        // declares without the reference's initializer (`Checker.Relations.Identical.cs`).
        _reverseMappedCache = new Dictionary<string, IType?>(StringComparer.Ordinal);
        _reverseHomomorphicMappedCache = new Dictionary<string, IType?>(StringComparer.Ordinal);

        // checker.ts:2344-2346
        _emptyStringType = GetStringLiteralType("");
        _zeroType = GetNumberLiteralType(0);
        _zeroBigIntType = GetBigIntLiteralType(new Utilities.PseudoBigIntValue { Negative = false, Base10Value = "0" });

        // checker.ts:2373-2374 — the two reverse-mapping walk stacks. Their slots are declared `null!` by
        // Checker.Relations.Identical.cs:51/54 with no other writer, so the reference's `= []` is made here.
        _reverseMappedSourceStack = [];
        _reverseMappedTargetStack = [];

        // checker.ts:2380
        _typeofType = CreateTypeofType();

        // checker.ts:2396-2410
        _suggestedExtensions = [
            (".mts", ".mjs"),
            (".ts", ".js"),
            (".cts", ".cjs"),
            (".mjs", ".mjs"),
            (".js", ".js"),
            (".cjs", ".cjs"),
            (".tsx", _compilerOptions.Jsx == JsxEmit.Preserve ? ".jsx" : ".js"),
            (".jsx", ".jsx"),
            (".json", ".json"),
        ];
    }

    // ---------------------------------------------------------------------------------------------
    // The ambient-module and unused-identifier caches (checker.ts:2247, 2253, 2319)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:2247 — <c>ambientModulesCache</c>.</summary>
    /// <remarks>PORT-NOTE: <c>getAmbientModules</c> (checker.ts:53581) creates it on first use; a null slot is TS's <c>undefined</c>.</remarks>
    internal List<ISymbol>? _ambientModulesCache;

    /// <summary>ported from: src/compiler/checker.ts:2253 — <c>patternAmbientModules</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's element is the interface <c>PatternAmbientModule</c>, which is the face
    /// <c>Checker.Symbols.cs</c>'s <c>MergeModuleAugmentation</c> reads through
    /// (<c>IPatternAmbientModule module</c>, <c>Checker.Symbols.cs:428</c>); the port therefore states the interface.
    /// <see cref="InitializeTypeChecker"/> grows it with <c>concatenate</c>, exactly as the reference does at :51583.
    /// </remarks>
    internal List<IPatternAmbientModule>? _patternAmbientModules;

    /// <summary>ported from: src/compiler/checker.ts:2319 — <c>allPotentiallyUnusedIdentifiers</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's key is <c>Path</c>, which is a <c>string</c> in the port (<c>SourceFile.Path</c>,
    /// <c>Model/NodeClasses.cs:2102</c>), and its value is the union <c>PotentiallyUnusedIdentifier</c>
    /// (checker.ts:44436), a union of node types the generator widens to <see cref="Node"/> — the same treatment
    /// <c>contextualBindingPatterns</c> gets (<c>Checker.Types.cs:281</c>).
    /// </remarks>
    internal readonly Dictionary<string, List<Node>> _allPotentiallyUnusedIdentifiers = new(StringComparer.Ordinal);

    /// <summary>ported from: src/compiler/checker.ts:2265 — <c>globalRegExpType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: this slot was recorded-not-ported while it had no reader; <c>Checker.Check.cs:153</c>
    /// (<c>checkRegularExpressionLiteral</c>, checker.ts:33295-33303) now reads it, so the state block declares it and
    /// <see cref="InitializeTypeChecker"/> assigns it as the reference does at :51633. The global slots that already had
    /// readers are declared in <c>Checker.Types.cs</c> beside <c>_anyFunctionType</c>; this one lands here because this
    /// file is the state block's owner and the assignment below needs the slot.
    /// </remarks>
    internal ObjectType _globalRegExpType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2269 — <c>anyReadonlyArrayType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>Type</c> is the port's interface face. The slot declaration is this file's — it is the one
    /// of checker.ts:2256-2269 that no sibling had declared — and <see cref="InitializeTypeChecker"/> fills it as the
    /// reference does at :51642 (<c>createTypeFromGenericGlobalType(globalReadonlyArrayType, [anyType])</c>, or
    /// <c>anyArrayType</c> when the lib has no <c>ReadonlyArray</c>); its landed reader is
    /// <c>Checker.Relations.Identical.cs:989</c>.
    /// </remarks>
    internal IType _anyReadonlyArrayType = null!;

    // ---------------------------------------------------------------------------------------------
    // The flow-analysis state (checker.ts:2321-2328, 2359-2372)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:2321 — <c>flowLoopStart</c>.</summary>
    /// <remarks>
    /// PORT-NOTE (the six sparse slots below, <c>flowLoopCaches</c> through <c>flowNodePostSuper</c>): each is a JS
    /// array in the reference, written at a flow-node id (which starts at 1) or at a saved loop index, and a JS array
    /// grows on assignment while the port's <c>List</c> indexer throws past the end. Their element types are therefore
    /// nullable — a slot the reference never wrote reads <c>undefined</c>, which is the port's <c>null</c> — and every
    /// write goes through <c>Checker.Inference.cs</c>'s <c>SetFlowSlot</c>, which fills the gap, with the matching
    /// <c>GetFlowSlot</c> read (the same growth <c>_symbolLinks</c>/<c>_nodeLinks</c> state as a <c>while</c> loop).
    /// </remarks>
    internal int _flowLoopStart;

    /// <summary>ported from: src/compiler/checker.ts:2322 — <c>flowLoopCount</c>.</summary>
    internal int _flowLoopCount;

    /// <summary>ported from: src/compiler/checker.ts:2323 — <c>sharedFlowCount</c>.</summary>
    internal int _sharedFlowCount;

    /// <summary>ported from: src/compiler/checker.ts:2324 — <c>flowAnalysisDisabled</c>.</summary>
    internal bool _flowAnalysisDisabled;

    /// <summary>ported from: src/compiler/checker.ts:2325 — <c>flowInvocationCount</c>.</summary>
    internal int _flowInvocationCount;

    /// <summary>ported from: src/compiler/checker.ts:2326 — <c>lastFlowNode</c>.</summary>
    internal FlowNode? _lastFlowNode;

    /// <summary>ported from: src/compiler/checker.ts:2327 — <c>lastFlowNodeReachable</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares it <c>boolean</c> with no initializer and only reads it once <c>lastFlowNode</c> is set
    /// (checker.ts:28850-28851); the port's <c>bool</c> defaults to <c>false</c>, which is the value its
    /// <c>undefined</c> compares as.
    /// </remarks>
    internal bool _lastFlowNodeReachable;

    /// <summary>ported from: src/compiler/checker.ts:2328 — <c>flowTypeCache</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's slot is a sparse <c>Type[]</c> indexed by node id, so an unwritten index (a hole) and
    /// any index beyond the end read as <c>undefined</c> and a write at a high index simply grows the array. The port
    /// states it as the growing null-filled list the same idiom already uses for <c>NodeLinks</c>/<c>SymbolLinks</c>
    /// (<c>Checker.cs</c>'s <c>GetNodeLinks</c>), and its two readers guard the index and grow on write
    /// (<c>Checker.Check.Expressions.cs</c>'s <c>GetTypeOfExpression</c>).
    /// </remarks>
    internal List<IType?>? _flowTypeCache;

    /// <summary>ported from: src/compiler/checker.ts:2359 — <c>flowLoopCaches</c>.</summary>
    internal readonly List<Dictionary<string, IType>?> _flowLoopCaches = [];

    /// <summary>ported from: src/compiler/checker.ts:2360 — <c>flowLoopNodes</c>.</summary>
    internal readonly List<FlowNode?> _flowLoopNodes = [];

    /// <summary>ported from: src/compiler/checker.ts:2361 — <c>flowLoopKeys</c>.</summary>
    internal readonly List<string?> _flowLoopKeys = [];

    /// <summary>ported from: src/compiler/checker.ts:2362 — <c>flowLoopTypes</c>.</summary>
    internal readonly List<List<IType>?> _flowLoopTypes = [];

    /// <summary>ported from: src/compiler/checker.ts:2363 — <c>sharedFlowNodes</c>.</summary>
    internal readonly List<FlowNode?> _sharedFlowNodes = [];

    /// <summary>ported from: src/compiler/checker.ts:2364 — <c>sharedFlowTypes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's element type is <c>FlowType = Type | IncompleteType</c> (types.ts:4267), a union with no
    /// counterpart in the port's nominal model. Its only writer stores the <c>FlowType</c> its caller computed
    /// (checker.ts:29128, inside <c>getFlowTypeOfReference</c>), so the slot is stated over the checker's
    /// <see cref="FlowType"/> class, which is the port's stand-in for that union (see
    /// <c>Checker.Inference.cs</c>'s note on it).
    /// </remarks>
    internal readonly List<FlowType?> _sharedFlowTypes = [];

    /// <summary>ported from: src/compiler/checker.ts:2365 — <c>flowNodeReachable</c>.</summary>
    internal readonly List<bool?> _flowNodeReachable = [];

    /// <summary>ported from: src/compiler/checker.ts:2366 — <c>flowNodePostSuper</c>.</summary>
    internal readonly List<bool?> _flowNodePostSuper = [];

    /// <summary>ported from: src/compiler/checker.ts:2367 — <c>potentialThisCollisions</c>.</summary>
    /// <remarks>PORT-NOTE: not <c>readonly</c>: the reference replaces it from the node links (checker.ts:49595).</remarks>
    internal List<Node> _potentialThisCollisions = [];

    /// <summary>ported from: src/compiler/checker.ts:2368 — <c>potentialNewTargetCollisions</c>.</summary>
    /// <remarks>PORT-NOTE: as <see cref="_potentialThisCollisions"/> (checker.ts:49596).</remarks>
    internal List<Node> _potentialNewTargetCollisions = [];

    /// <summary>ported from: src/compiler/checker.ts:2369 — <c>potentialWeakMapSetCollisions</c>.</summary>
    /// <remarks>PORT-NOTE: as <see cref="_potentialThisCollisions"/> (checker.ts:49597).</remarks>
    internal List<Node> _potentialWeakMapSetCollisions = [];

    /// <summary>ported from: src/compiler/checker.ts:2370 — <c>potentialReflectCollisions</c>.</summary>
    /// <remarks>PORT-NOTE: as <see cref="_potentialThisCollisions"/> (checker.ts:49598).</remarks>
    internal List<Node> _potentialReflectCollisions = [];

    /// <summary>ported from: src/compiler/checker.ts:2371 — <c>potentialUnusedRenamedBindingElementsInTypes</c>.</summary>
    /// <remarks>PORT-NOTE: as <see cref="_potentialThisCollisions"/> (checker.ts:49599). TS's element is the class <c>BindingElement</c>.</remarks>
    internal List<BindingElement> _potentialUnusedRenamedBindingElementsInTypes = [];

    /// <summary>ported from: src/compiler/checker.ts:2372 — <c>awaitedTypeStack</c>.</summary>
    internal readonly List<int> _awaitedTypeStack = [];

    // ---------------------------------------------------------------------------------------------
    // The literal-type caches, the suggestion counters, <c>typeofType</c> and the JSX names
    // (checker.ts:2344-2346, 2354-2355, 2380-2383, 2396-2410)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:2344 — <c>emptyStringType</c>.</summary>
    internal IStringLiteralType _emptyStringType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2345 — <c>zeroType</c>.</summary>
    internal INumberLiteralType _zeroType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2346 — <c>zeroBigIntType</c>.</summary>
    internal IBigIntLiteralType _zeroBigIntType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2354 — <c>suggestionCount</c>.</summary>
    internal int _suggestionCount;

    /// <summary>ported from: src/compiler/checker.ts:2355 — <c>maximumSuggestionCount</c>.</summary>
    internal int _maximumSuggestionCount = 10;

    /// <summary>ported from: src/compiler/checker.ts:2380 — <c>typeofType</c>.</summary>
    internal IType _typeofType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2382 — <c>_jsxNamespace</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>__String</c> is erased to <c>string</c> port-wide (§2). <c>getJsxNamespace</c>
    /// (checker.ts:2467-2480, the JSX slice) fills it on first use, so a null slot is TS's <c>undefined</c>.
    /// </remarks>
    internal string? _jsxNamespace;

    /// <summary>ported from: src/compiler/checker.ts:2383 — <c>_jsxFactoryEntity</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>EntityName</c> (<c>Identifier | QualifiedName</c>) has no port type; the generator widens
    /// that union to <see cref="Node"/> — the face <c>Binder.Expando.cs:495</c> and <c>Checker.Stringify.cs:337</c>
    /// already take for it — and <c>getJsxNamespace</c> fills the slot with an identifier.
    /// </remarks>
    internal Node? _jsxFactoryEntity;

    /// <summary>ported from: src/compiler/checker.ts:2396-2410 — <c>suggestedExtensions</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>[string, string][]</c> is a named tuple list, the port's convention for tuple elements
    /// (<c>Checker.Relations.Structured.cs:122</c>). It is assigned in <see cref="InitializeState"/> rather than by the
    /// field initializer because the seventh entry reads <c>compilerOptions.jsx</c> and <c>_compilerOptions</c> is set
    /// in the constructor body, after field initializers run.
    /// </remarks>
    internal List<(string ActualExt, string ImportExt)> _suggestedExtensions = null!;

    // ---------------------------------------------------------------------------------------------
    // initializeTypeChecker (checker.ts:51555-51684)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:51555-51684 — <c>initializeTypeChecker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>bindSourceFile(file, compilerOptions)</c> is the binder's landed entry point
    /// (<c>Binder.BindSourceFile</c>, <c>Binder/Binder.State.cs:209</c>), <c>mergeSymbolTable</c> is
    /// <c>Checker.Symbols.cs:352</c> and <c>addUndefinedToGlobalsOrErrorOnRedeclaration</c> is <c>:462</c>, so all
    /// three are called as the port declares them. <c>isExternalOrCommonJsModule</c> and
    /// <c>isGlobalScopeAugmentation</c> are <c>utilities</c> members and are called qualified
    /// (<c>Utilities.Binder.cs:765</c>/<c>:730</c>). The two duplicate-report arms keep the reference's
    /// <c>addRelatedInfo</c> pair (<c>Utilities.AddRelatedInfo</c>, <c>Utilities/Utilities.Parser.cs:557</c>).
    /// </remarks>
    internal void InitializeTypeChecker()
    {
        using var allocProbe = AllocationProbe.Enter("Checker.InitializeTypeChecker");
        // checker.ts:1559-1568 — the four globals the resolver and the name resolver close over, and globalThisSymbol's
        // publish into `globals`. The reference creates them in createTypeChecker's prologue, just before this member
        // runs (checker.ts:1556-1578); `Checker.cs` declares the four slots and its PORT-NOTEs name that prologue as
        // their creator, which does not create them, so they are created here, at the point in the reference's order
        // where the merge loop below (and `addUndefinedToGlobalsOrErrorOnRedeclaration`) needs them and where
        // `globals` already holds globalThisSymbol.
        _undefinedSymbol = CreateSymbol(SymbolFlags.Property, "undefined");
        _undefinedSymbol.Declarations = [];
        _globalThisSymbol = CreateSymbol(SymbolFlags.Module, "globalThis", CheckFlags.Readonly);
        _globalThisSymbol.Exports = _globals;
        _globalThisSymbol.Declarations = [];
        _globals[_globalThisSymbol.EscapedName!] = _globalThisSymbol;
        _argumentsSymbol = CreateSymbol(SymbolFlags.Property, "arguments");
        _requireSymbol = CreateSymbol(SymbolFlags.Property, "require");

        // Bind all source files and propagate errors
        // PORT-NOTE (concurrency): one file's bind is independent of every other file's — each gets its own `Binder`
        // instance (`Binder/State.cs:209`) and writes only into its own file's AST (parents, `symbol`, `locals`,
        // `flowNode`), which is exactly what the reference's loop relies on when it runs it one file at a time. Every
        // process-wide slot the binder touches is reentrant now (§18.3: atomic ids, `AsyncLocal` tracing, atomic
        // performance counters), so the loop fans out; the merge loop right below stays sequential, because it writes
        // the checker's shared `_globals`/`_diagnostics` in file order. Go does the same thing in parallel
        // (`tsc/internal/compiler/program.go:578`, `BindSourceFiles`).
        var filesToBind = _host.GetSourceFiles();
        Parallelism.For(filesToBind.Count, i => Binder.BindSourceFile(filesToBind[i], _compilerOptions));

        _amalgamatedDuplicates = new Dictionary<string, DuplicateInfoForFiles>(StringComparer.Ordinal);

        // Initialize global symbol table
        List<List<MemberExpression>>? augmentations = null;
        foreach (var file in _host.GetSourceFiles())
        {
            if (file.RedirectInfo is not null)
            {
                continue;
            }
            if (!Utilities.IsExternalOrCommonJsModule(file))
            {
                // It is an error for a non-external-module (i.e. script) to declare its own `globalThis`.
                var fileGlobalThisSymbol = file.Locals?.GetValueOrDefault("globalThis");
                if (fileGlobalThisSymbol?.Declarations is not null)
                {
                    foreach (var declaration in fileGlobalThisSymbol.Declarations)
                    {
                        _diagnostics.Add(Utilities.CreateDiagnosticForNode((Node)declaration, Diagnostics.Declaration_name_conflicts_with_built_in_global_identifier_0, "globalThis"));
                    }
                }
                MergeSymbolTable(_globals, file.Locals!);
            }
            if (file.JsGlobalAugmentations is not null)
            {
                MergeSymbolTable(_globals, file.JsGlobalAugmentations);
            }
            if (file.PatternAmbientModules is { Count: > 0 })
            {
                _patternAmbientModules = (List<IPatternAmbientModule>)Core.Concatenate(_patternAmbientModules, file.PatternAmbientModules)!;
            }
            if (file.ModuleAugmentations is { Count: > 0 })
            {
                augmentations ??= [];
                augmentations.Add(file.ModuleAugmentations);
            }
            if (file.Symbol?.GlobalExports is not null)
            {
                // Merge in UMD exports with first-in-wins semantics (see #9771)
                var source = file.Symbol.GlobalExports;
                source.ForEach((sourceSymbol, id) =>
                {
                    if (!_globals.ContainsKey(id))
                    {
                        _globals[id] = sourceSymbol;
                    }
                });
            }
        }

        // We do global augmentations separately from module augmentations (and before creating global types) because they
        //  1. Affect global types. We won't have the correct global types until global augmentations are merged. Also,
        //  2. Module augmentation instantiation requires creating the type of a module, which, in turn, can require
        //     checking for an export or property on the module (if export=) which, in turn, can fall back to the
        //     apparent type of the module - either globalObjectType or globalFunctionType - which wouldn't exist if we
        //     did module augmentations prior to finalizing the global types.
        if (augmentations is not null)
        {
            // merge _global_ module augmentations.
            // this needs to be done after global symbol table is initialized to make sure that all ambient modules are indexed
            foreach (var list in augmentations)
            {
                foreach (var augmentation in list)
                {
                    if (!Utilities.IsGlobalScopeAugmentation((ModuleDeclaration)augmentation.Parent!)) continue;
                    MergeModuleAugmentation(augmentation);
                }
            }
        }

        AddUndefinedToGlobalsOrErrorOnRedeclaration();

        GetSymbolLinks(_undefinedSymbol!).Type = _undefinedWideningType;
        GetSymbolLinks(_argumentsSymbol!).Type = GetGlobalType("IArguments", /*arity*/ 0, /*reportErrors*/ true);
        GetSymbolLinks(_unknownSymbol).Type = _errorType;
        GetSymbolLinks(_globalThisSymbol!).Type = CreateObjectType(ObjectFlags.Anonymous, _globalThisSymbol);

        // Initialize special types
        _globalArrayType = (GenericType)GetGlobalType("Array", /*arity*/ 1, /*reportErrors*/ true)!;
        _globalObjectType = (ObjectType)GetGlobalType("Object", /*arity*/ 0, /*reportErrors*/ true)!;
        _globalFunctionType = (ObjectType)GetGlobalType("Function", /*arity*/ 0, /*reportErrors*/ true)!;
        _globalCallableFunctionType = (_strictBindCallApply ? (ObjectType?)GetGlobalType("CallableFunction", /*arity*/ 0, /*reportErrors*/ true) : null) ?? _globalFunctionType;
        _globalNewableFunctionType = (_strictBindCallApply ? (ObjectType?)GetGlobalType("NewableFunction", /*arity*/ 0, /*reportErrors*/ true) : null) ?? _globalFunctionType;
        _globalStringType = (ObjectType)GetGlobalType("String", /*arity*/ 0, /*reportErrors*/ true)!;
        _globalNumberType = (ObjectType)GetGlobalType("Number", /*arity*/ 0, /*reportErrors*/ true)!;
        _globalBooleanType = (ObjectType)GetGlobalType("Boolean", /*arity*/ 0, /*reportErrors*/ true)!;

        // checker.ts:51633 — getGlobalType("RegExp", 0, true); the slot is declared above in this file.
        _globalRegExpType = (ObjectType)GetGlobalType("RegExp", /*arity*/ 0, /*reportErrors*/ true)!;

        _anyArrayType = (Type)CreateArrayType(_anyType);
        _autoArrayType = (Type)CreateArrayType(_autoType);
        if (ReferenceEquals(_autoArrayType, _emptyObjectType))
        {
            // autoArrayType is used as a marker, so even if global Array type is not defined, it needs to be a unique type
            _autoArrayType = (Type)CreateAnonymousType(/*symbol*/ null, _emptySymbols, [], [], []);
        }

        _globalReadonlyArrayType = GetGlobalTypeOrUndefined("ReadonlyArray", /*arity*/ 1) is { } readonlyArrayType ? (GenericType)readonlyArrayType : _globalArrayType;
        _anyReadonlyArrayType = _globalReadonlyArrayType is not null ? (IType)CreateTypeFromGenericGlobalType(_globalReadonlyArrayType, [_anyType]) : _anyArrayType;

        // PORT-NOTE: `_globalThisType` (checker.ts:2266) is declared by the slice that reads it
        // (`IType _globalThisType`, `Checker.Inference.cs:3500`); the reference's `… as GenericType` cast is
        // unverified upstream (`ThisType` may be absent from the lib), and the null-forgiving operator mirrors that.
        _globalThisType = (GenericType)GetGlobalTypeOrUndefined("ThisType", /*arity*/ 1)!;

        if (augmentations is not null)
        {
            // merge _nonglobal_ module augmentations.
            // this needs to be done after global symbol table is initialized to make sure that all ambient modules are indexed
            foreach (var list in augmentations)
            {
                foreach (var augmentation in list)
                {
                    if (Utilities.IsGlobalScopeAugmentation((ModuleDeclaration)augmentation.Parent!)) continue;
                    MergeModuleAugmentation(augmentation);
                }
            }
        }

        foreach (var (_, duplicateInfo) in _amalgamatedDuplicates!)
        {
            var firstFile = duplicateInfo.FirstFile;
            var secondFile = duplicateInfo.SecondFile;
            var conflictingSymbols = duplicateInfo.ConflictingSymbols;

            // If not many things conflict, issue individual errors
            if (conflictingSymbols.Count < 8)
            {
                foreach (var (symbolName, info) in conflictingSymbols)
                {
                    var message = info.IsBlockScoped ? Diagnostics.Cannot_redeclare_block_scoped_variable_0 : Diagnostics.Duplicate_identifier_0;
                    foreach (var node in info.FirstFileLocations)
                    {
                        AddDuplicateDeclarationError((IDeclaration)node, message, symbolName, DeclarationList(info.SecondFileLocations));
                    }
                    foreach (var node in info.SecondFileLocations)
                    {
                        AddDuplicateDeclarationError((IDeclaration)node, message, symbolName, DeclarationList(info.FirstFileLocations));
                    }
                }
            }
            else
            {
                // Otherwise issue top-level error since the files appear very identical in terms of what they contain
                var list = string.Join(", ", conflictingSymbols.Keys);
                _diagnostics.Add(Utilities.AddRelatedInfo(
                    Utilities.CreateDiagnosticForNode(firstFile, Diagnostics.Definitions_of_the_following_identifiers_conflict_with_those_in_another_file_Colon_0, list),
                    Utilities.CreateDiagnosticForNode(secondFile, Diagnostics.Conflicts_are_in_this_file)));
                _diagnostics.Add(Utilities.AddRelatedInfo(
                    Utilities.CreateDiagnosticForNode(secondFile, Diagnostics.Definitions_of_the_following_identifiers_conflict_with_those_in_another_file_Colon_0, list),
                    Utilities.CreateDiagnosticForNode(firstFile, Diagnostics.Conflicts_are_in_this_file)));
            }
        }

        _amalgamatedDuplicates = null;
    }

    /// <summary>
    /// PORT-NOTE: <c>DuplicateInfoForSymbol</c>'s two location lists are <c>Node</c> because that is the face its
    /// recorder casts to (<c>Checker.Symbols.cs:298-305</c>), while <c>AddDuplicateDeclarationError</c> takes the
    /// <c>IDeclaration</c> face. Every entry is a declaration node, so this projects without changing the sequence.
    /// </summary>
    private static List<IDeclaration> DeclarationList(List<Node> locations)
    {
        var result = new List<IDeclaration>(locations.Count);
        foreach (var location in locations)
        {
            result.Add((IDeclaration)location);
        }
        return result;
    }
}
