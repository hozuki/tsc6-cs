namespace TypeScript.Compiler;

// ported from: src/compiler/binder.ts:499-507 (the module-level `bindSourceFile`)
// ported from: src/compiler/binder.ts:509-560 (the state `createBinder` opens with)
// ported from: src/compiler/binder.ts:563-569 (createDiagnosticForNode)
// ported from: src/compiler/binder.ts:571-618 (the inner `bindSourceFile`)
// ported from: src/compiler/binder.ts:620-628 (bindInStrictMode)
/// <summary>
/// The binder's per-file state and its entry point — TS's <c>createBinder</c> closure and the module-level
/// <c>bindSourceFile</c> that drives it.
/// </summary>
/// <remarks>
/// TS builds one binder (<c>const binder = createBinder()</c>, binder.ts:499) whose closure is called once per
/// file, and clears the closure's <c>var</c>s again at the end of each call (binder.ts:588-617, the block that
/// exists because the closure outlives the file). The port gives that closure the treatment <c>parser.ts</c> got
/// (§15.10): its state is instance state, one <see cref="Binder"/> is created per file, the <c>var</c>s'
/// initial values are folded into the constructor, and the teardown block has nothing left to do — the instance is
/// unreachable as soon as <see cref="BindSourceFile"/> returns. That also keeps two files from stepping on each
/// other's state, which matters because the tests run in parallel.
/// <para>
/// PORT-NOTE: the state slots and the members TS nests inside <c>createBinder</c> are <c>internal</c> rather than
/// <c>private</c>. They are the binder's state and the binder's walk, not this slice's: the flow graph (M5-2), the
/// JSDoc deferral set (M5-3), the module/expando set (M5-4) and the dispatch switch (M5-5) live in sibling partial
/// files of this class and both read the slots and call the members, so a slot or member that no member of this
/// *slice* reads yet is still not dead — but a <c>private</c> one that no member of the assembly reads is a warning
/// (CS0169/CS0414, IDE0051), and this project treats warnings as errors. Members only this slice calls stay
/// <c>private</c>.
/// </para>
/// <para>
/// PORT-NOTE: <c>objectAllocator.getSymbolConstructor()</c> (binder.ts:579) is not ported (§15.2) — the port has no
/// object allocator, so <c>createSymbol</c> constructs <see cref="Symbol"/> directly and the
/// <c>Symbol</c> slot at binder.ts:553 does not exist.
/// </para>
/// <para>
/// PORT-NOTE: <c>bindBinaryExpressionFlow = createBindBinaryExpressionFlow()</c> (binder.ts:556) is not a slot here:
/// TS caches the closure the factory returns, while the port states <c>createBindBinaryExpressionFlow</c>'s body as
/// a member of this same class (M5-2), so there is nothing to cache.
/// </para>
/// </remarks>
internal partial class Binder
{
    // ---------------------------------------------------------------------------------------------
    // Per-file inputs (TS: the `var file`/`var options`/`var languageVersion` the closure reads everywhere)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/binder.ts:513 — <c>file</c>.</summary>
    internal readonly SourceFile _file;

    /// <summary>ported from: src/compiler/binder.ts:514 — <c>options</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types this <c>CompilerOptions</c>, the concrete object the CLI/config parser builds; the port's
    /// option model is the generated <see cref="ICompilerOptions"/> interface, because the binder only reads options
    /// and the thing that constructs them (M8's config/CLI parser, and the tests' fake) is what needs a concrete type.
    /// The slot is nullable because the readers (<c>getEmitScriptTarget</c>, <c>getAlwaysStrict</c>, and M5-3's
    /// <c>checkStrictModeLabeledStatement</c>) are declared to take <c>ICompilerOptions?</c>.
    /// </remarks>
    internal readonly ICompilerOptions? _options;

    /// <summary>ported from: src/compiler/binder.ts:515 — <c>languageVersion</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: read by <c>checkStrictModeFunctionDeclaration</c> (binder.ts:2696, M5-3); this slice only
    /// initializes it.
    /// </remarks>
    internal readonly ScriptTarget _languageVersion;

    // ---------------------------------------------------------------------------------------------
    // Declaration state (TS: binder.ts:516-524)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/binder.ts:516 — <c>parent</c>.</summary>
    internal Node? _parent;

    /// <summary>ported from: src/compiler/binder.ts:517 — <c>container</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types this <c>IsContainer | EntityNameExpression</c>; both are unions of node types with no C#
    /// anchor, so the slot is the honest upper bound <see cref="Node"/> (§12.9). M5-3/M5-4 assign entity-name
    /// expressions to it; every member of this slice reads only <c>kind</c>, <c>symbol</c> and <c>locals</c> off it.
    /// </remarks>
    internal Node? _container;

    /// <summary>ported from: src/compiler/binder.ts:518 — <c>thisParentContainer</c>.</summary>
    internal Node? _thisParentContainer;

    /// <summary>ported from: src/compiler/binder.ts:519 — <c>blockScopeContainer</c>.</summary>
    internal Node? _blockScopeContainer;

    /// <summary>ported from: src/compiler/binder.ts:520 — <c>lastContainer</c>.</summary>
    /// <remarks>PORT-NOTE: TS types this <c>HasLocals</c>, the port's <see cref="ILocalsContainer"/>.</remarks>
    internal ILocalsContainer? _lastContainer;

    /// <summary>ported from: src/compiler/binder.ts:521 — <c>delayedTypeAliases</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types this <c>(JSDocTypedefTag | JSDocCallbackTag | JSDocEnumTag)[]</c>. The three classes have
    /// a common base in the port (<see cref="JSDocTag"/>), and the two members that fill and drain the list
    /// (<c>bindWorker</c>'s tag cases and <c>delayedBindJSDocTypedefTag</c>, binder.ts:3105/2449) belong to slices
    /// M5-3/M5-5; the slot is stated here as the union's honest upper bound so those slices agree on one field.
    /// </remarks>
    internal List<JSDocTag>? _delayedTypeAliases;

    /// <summary>ported from: src/compiler/binder.ts:522 — <c>seenThisKeyword</c>.</summary>
    internal bool _seenThisKeyword;

    /// <summary>ported from: src/compiler/binder.ts:523 — <c>jsDocImports</c>.</summary>
    internal List<JSDocImportTag>? _jsDocImports;

    // ---------------------------------------------------------------------------------------------
    // Control-flow state (TS: binder.ts:526-538)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/binder.ts:526 — <c>currentFlow</c>.</summary>
    internal FlowNode? _currentFlow;

    /// <summary>ported from: src/compiler/binder.ts:527 — <c>currentBreakTarget</c>.</summary>
    /// <remarks>PORT-NOTE: TS types the three targets <c>FlowLabel | undefined</c>; the port has one flow-node class
    /// (Model/FlowNode.cs), so they are <see cref="FlowNode"/>.</remarks>
    internal FlowNode? _currentBreakTarget;

    /// <summary>ported from: src/compiler/binder.ts:528 — <c>currentContinueTarget</c>.</summary>
    internal FlowNode? _currentContinueTarget;

    /// <summary>ported from: src/compiler/binder.ts:529 — <c>currentReturnTarget</c>.</summary>
    internal FlowNode? _currentReturnTarget;

    /// <summary>ported from: src/compiler/binder.ts:530 — <c>currentTrueTarget</c>.</summary>
    /// <remarks>PORT-NOTE: written and read only by <c>doWithConditionalBranches</c>/<c>bindCondition</c>
    /// (binder.ts:1478-1484, M5-2).</remarks>
    internal FlowNode? _currentTrueTarget;

    /// <summary>ported from: src/compiler/binder.ts:531 — <c>currentFalseTarget</c>.</summary>
    internal FlowNode? _currentFalseTarget;

    /// <summary>ported from: src/compiler/binder.ts:532 — <c>currentExceptionTarget</c>.</summary>
    internal FlowNode? _currentExceptionTarget;

    /// <summary>ported from: src/compiler/binder.ts:533 — <c>preSwitchCaseFlow</c>.</summary>
    /// <remarks>PORT-NOTE: written and read only by <c>bindSwitchStatement</c>/<c>bindCaseBlock</c>
    /// (binder.ts:1721-1768, M5-2).</remarks>
    internal FlowNode? _preSwitchCaseFlow;

    /// <summary>ported from: src/compiler/binder.ts:534 — <c>activeLabelList</c>.</summary>
    internal ActiveLabel? _activeLabelList;

    /// <summary>ported from: src/compiler/binder.ts:535 — <c>hasExplicitReturn</c>.</summary>
    internal bool _hasExplicitReturn;

    /// <summary>ported from: src/compiler/binder.ts:536 — <c>inReturnPosition</c>.</summary>
    internal bool _inReturnPosition;

    /// <summary>ported from: src/compiler/binder.ts:537 — <c>hasFlowEffects</c>.</summary>
    /// <remarks>PORT-NOTE: written and read only by the logical-binary-expression and optional-chain flow binders
    /// (binder.ts:1952-2205, M5-2).</remarks>
    internal bool _hasFlowEffects;

    // ---------------------------------------------------------------------------------------------
    // Emit-helper and strict-mode state (TS: binder.ts:540-551)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/binder.ts:540 — <c>emitFlags</c>.</summary>
    internal NodeFlags _emitFlags;

    /// <summary>ported from: src/compiler/binder.ts:546 — <c>inStrictMode</c>.</summary>
    internal bool _inStrictMode;

    /// <summary>ported from: src/compiler/binder.ts:549 — <c>inAssignmentPattern</c>.</summary>
    internal bool _inAssignmentPattern = false;

    /// <summary>ported from: src/compiler/binder.ts:551 — <c>symbolCount</c>.</summary>
    internal int _symbolCount;

    // ---------------------------------------------------------------------------------------------
    // Per-file results (TS: binder.ts:553-556)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/binder.ts:554 — <c>classifiableNames</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>Set&lt;__String&gt;</c> is <c>HashSet&lt;string&gt;</c>, which is also the declared type of
    /// <c>SourceFile.ClassifiableNames</c> (Model/NodeClasses.cs:2136).
    /// </remarks>
    internal HashSet<string> _classifiableNames = [];

    /// <summary>ported from: src/compiler/binder.ts:555 — <c>unreachableFlow</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>createFlowNode</c> is a module-level function of <c>binder.ts</c>; the port keeps it next to
    /// the class it builds (Model/FlowNode.cs), so the singleton is created through
    /// <see cref="FlowNode.CreateFlowNode"/>.
    /// </remarks>
    internal readonly FlowNode _unreachableFlow = FlowNode.CreateFlowNode(FlowFlags.Unreachable, /*node*/ null, /*antecedent*/ null);

    /// <summary>ported from: src/compiler/binder.ts:571-618 — the inner <c>bindSourceFile</c>, state assignment half.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>bindSourceFile(f, opts)</c> assigns the two inputs and then the derived state; with one
    /// instance per file the assignments that are just "start values" (<c>symbolCount = 0</c>,
    /// <c>classifiableNames = new Set()</c>) are field initializers and what remains is the constructor.
    /// </remarks>
    private Binder(SourceFile file, ICompilerOptions? options)
    {
        _file = file;
        _options = options;
        _languageVersion = Utilities.GetEmitScriptTarget(options);
        _inStrictMode = BindInStrictMode(file, options);
        // PORT-NOTE (performance, measured): the child walk hands the same two callbacks to `forEachChild` on every node,
        // and in C# *evaluating* `Bind` (a method group) or `nodes => BindEach(nodes)` (a lambda that captures `this`)
        // allocates a fresh delegate/closure every time — one or two objects per node of the program. TS pays nothing for
        // this because `bind`/`bindEach` are stable module-level function references. The two delegates are therefore
        // built once per file here; see docs/architecture.md §18.4h for the measured bytes.
        _bind = Bind;
        _bindEach = BindEachNodes;
    }

    /// <summary>The cached <c>bind</c> callback (<c>binder.ts:1079</c>) — see the constructor's note.</summary>
    private readonly Action<Node> _bind;

    /// <summary>The cached <c>bindEach</c> callback (<c>binder.ts:1080</c>) — see the constructor's note.</summary>
    private readonly Action<INodeArray<INode>> _bindEach;

    /// <summary>
    /// <c>BindEach</c> with the signature <c>forEachChild</c>'s <c>cbNodes</c> takes, so the callback can be a cached
    /// delegate instead of a lambda capturing <c>this</c>.
    /// </summary>
    private void BindEachNodes(INodeArray<INode> nodes) => BindEach(nodes);

    /// <summary>ported from: src/compiler/binder.ts:502-507 — <c>bindSourceFile</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>binder</c> singleton is made per call here (see the remarks on this file); the two
    /// <c>performance.mark</c> calls and the <c>performance.measure</c> around the bind are kept, as
    /// <c>parser.ts</c>'s were (M2c).
    /// </remarks>
    public static void BindSourceFile(SourceFile file, ICompilerOptions? options)
    {
        using var allocProbe = AllocationProbe.Enter("Binder.BindSourceFile");
        Performance.Mark("beforeBind");
        new Binder(file, options).Run();
        Performance.Mark("afterBind");
        Performance.Measure("Bind", "beforeBind", "afterBind");
    }

    /// <summary>ported from: src/compiler/binder.ts:571-586 — the inner <c>bindSourceFile</c>, binding half.</summary>
    /// <remarks>
    /// PORT-NOTE: the <c>tracing?.push/pop</c> pair (binder.ts:585-587) is kept; it is a no-op unless tracing is on,
    /// exactly as upstream. The <c>tracing</c> slot and its <c>push</c>/<c>pop</c>/<c>Phase</c> members are the
    /// tracing slice's (Core/Tracing.cs).
    /// <para>
    /// PORT-NOTE: TS clears every state <c>var</c> at binder.ts:588-617 because its binder outlives the file. In the
    /// port the instance is per-file and becomes unreachable here, so the teardown is gone by construction — the one
    /// observable difference is that <see cref="BindSourceFile"/>'s caller cannot read the previous file's state off
    /// a reused binder, which is the point of the change.
    /// </para>
    /// </remarks>
    private void Run()
    {
        // Attach debugging information if necessary
        Debug.AttachFlowNodeDebugInfo(_unreachableFlow);

        if (_file.Locals is null)
        {
            Tracing.Current?.Push(Tracing.Phase.Bind, "bindSourceFile", TracingArgs.Of(("path", _file.Path)), separateBeginAndEnd: true);
            Bind(_file);
            Tracing.Current?.Pop();
            _file.SymbolCount = _symbolCount;
            _file.ClassifiableNames = _classifiableNames;
            // PORT-NOTE: TS drains the two deferred JSDoc collections here (binder.ts:584-585); both members belong to
            // the JSDoc slice (M5-3) and are named as the reference names them.
            DelayedBindJSDocTypedefTag();
            BindJSDocImports();
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:620-628 — <c>bindInStrictMode</c>.</summary>
    private static bool BindInStrictMode(SourceFile file, ICompilerOptions? options)
    {
        if (Utilities.GetAlwaysStrict(options) && !file.IsDeclarationFile)
        {
            // bind in strict mode source files with alwaysStrict option
            return true;
        }
        else
        {
            return Js.IsTruthy(file.ExternalModuleIndicator);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:563-569 — <c>createDiagnosticForNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS writes <c>getSourceFileOfNode(node) || file</c>; both branches are the same type and the port
    /// keeps the fallback as <c>??</c>.
    /// </remarks>
    internal Diagnostic CreateDiagnosticForNode(Node node, DiagnosticMessage message, params object?[] args)
        => Utilities.CreateDiagnosticForNodeInSourceFile(Utilities.GetSourceFileOfNode(node) ?? _file, node, message, args);
}
