namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:54279-54327 — interface NodeBuilderContext
/// <summary>
/// The state one node-building pass carries: where the declaration being named lives, the flags it was asked with, the
/// tracker, and the caches that keep generated names stable.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS declares this interface inside <c>checker.ts</c> (it is not in <c>types.ts</c>), so it has no generated
/// counterpart and no interface of its own in the port — this class is the whole declaration. It is written here, in
/// M6-0, because <see cref="SymbolTrackerImpl"/>'s only field of that type is it; the node builder that fills and reads
/// it is M6-6 (<c>Checker/NodeBuilder.cs</c>).
/// </para>
/// <para>
/// PORT-NOTE: the base is the generated <see cref="ISyntacticTypeNodeBuilderContext"/> (types.ts's
/// <c>SyntacticTypeNodeBuilderContext</c>), which already declares <c>enclosingDeclaration</c>, <c>enclosingFile</c>,
/// <c>flags</c>, <c>approximateLength</c>, <c>noInferenceFallback</c> and <c>suppressReportInferenceFallback</c>. TS
/// re-declares five of those here with the same types (checker.ts:54280-54289, :54290, :54305, :54322), so the port
/// inherits them instead of repeating slots.
/// </para>
/// <para>
/// PORT-NOTE: <c>tracker</c> (checker.ts:54292) narrows the base's <c>SymbolTracker</c> to <see cref="SymbolTrackerImpl"/>,
/// so the class-level property carries that type and the base interface's <c>object?</c> view is an explicit
/// implementation over the same slot (§12.2). <c>withContext</c> builds the context literal with
/// <c>tracker: undefined!</c> and assigns the tracker right after, which is why the slot is nullable here.
/// </para>
/// <para>
/// PORT-NOTE: TS marks <c>maxExpansionDepth</c>/<c>maxTruncationLength</c> <c>readonly</c>
/// (checker.ts:54294-54296). C# interfaces cannot express that and the class is created by an object initializer in
/// TS, so the port keeps every slot settable like any other model slot; the two names are only ever assigned when the
/// context is created.
/// </para>
/// <para>
/// PORT-NOTE: <c>trackedSymbols</c> is TS's <c>TrackedSymbol[]</c> tuple array (types.ts:6314); the port names the
/// tuple <see cref="TrackedSymbol"/>. The generated <see cref="ISerializedTypeEntry.TrackedSymbols"/> is
/// <c>List&lt;object?&gt;?</c>, so copying into it projects the boxes.
/// </para>
/// </remarks>
internal sealed class NodeBuilderContext : ISyntacticTypeNodeBuilderContext
{
    /// <summary>PORT-NOTE: the single physical <c>tracker</c> slot; see the class remarks.</summary>
    private object? _tracker;

    /// <summary>ported from: src/compiler/checker.ts:54280 — <c>NodeBuilderContext.enclosingDeclaration</c> (from the base interface).</summary>
    public Node? EnclosingDeclaration { get; set; }

    /// <summary>ported from: src/compiler/checker.ts:54289 — <c>NodeBuilderContext.enclosingFile</c> (from the base interface).</summary>
    public SourceFile? EnclosingFile { get; set; }

    /// <summary>ported from: src/compiler/checker.ts:54290 — <c>NodeBuilderContext.flags</c> (from the base interface).</summary>
    public NodeBuilderFlags Flags { get; set; }

    /// <summary>ported from: src/compiler/checker.ts:54291 — <c>NodeBuilderContext.internalFlags</c>.</summary>
    public InternalNodeBuilderFlags InternalFlags { get; set; }

    /// <summary>ported from: src/compiler/checker.ts:54292 — <c>NodeBuilderContext.tracker</c>.</summary>
    public SymbolTrackerImpl? Tracker
    {
        get => (SymbolTrackerImpl?)_tracker;
        set => _tracker = value;
    }

    /// <summary>ported from: src/compiler/types.ts:10617 — the base interface's <c>tracker</c> slot.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares it <c>Required&lt;Pick&lt;SymbolTracker, "reportInferenceFallback"&gt;&gt;</c>; the generator kept the
    /// slot as <c>object?</c> because <c>Required</c>/<c>Pick</c> have no counterpart (NodeInterfaces.cs:3297 states the same).
    /// Both views read and write the one slot.
    /// </remarks>
    object? ISyntacticTypeNodeBuilderContext.Tracker
    {
        get => Tracker;
        set => Tracker = (SymbolTrackerImpl?)value;
    }

    /// <summary>ported from: src/compiler/checker.ts:54294 — <c>NodeBuilderContext.maxExpansionDepth</c>.</summary>
    public int MaxExpansionDepth { get; set; }

    /// <summary>ported from: src/compiler/checker.ts:54296 — <c>NodeBuilderContext.maxTruncationLength</c>.</summary>
    public int MaxTruncationLength { get; set; }

    /// <summary>ported from: src/compiler/checker.ts:54299 — <c>NodeBuilderContext.encounteredError</c>.</summary>
    public bool EncounteredError { get; set; }

    /// <summary>ported from: src/compiler/checker.ts:54300 — <c>NodeBuilderContext.reportedDiagnostic</c>.</summary>
    public bool ReportedDiagnostic { get; set; }

    /// <summary>ported from: src/compiler/checker.ts:54301 — <c>NodeBuilderContext.trackedSymbols</c>.</summary>
    public List<TrackedSymbol>? TrackedSymbols { get; set; }

    /// <summary>ported from: src/compiler/checker.ts:54302 — <c>NodeBuilderContext.visitedTypes</c>.</summary>
    public HashSet<int>? VisitedTypes { get; set; }

    /// <summary>ported from: src/compiler/checker.ts:54303 — <c>NodeBuilderContext.symbolDepth</c>.</summary>
    public Dictionary<string, int>? SymbolDepth { get; set; }

    /// <summary>ported from: src/compiler/checker.ts:54304 — <c>NodeBuilderContext.inferTypeParameters</c>.</summary>
    public List<ITypeParameter>? InferTypeParameters { get; set; }

    /// <summary>ported from: src/compiler/checker.ts:54305 — <c>NodeBuilderContext.approximateLength</c> (from the base interface).</summary>
    public int ApproximateLength { get; set; }

    /// <summary>ported from: src/compiler/checker.ts:54306 — <c>NodeBuilderContext.truncating</c>.</summary>
    public bool Truncating { get; set; }

    /// <summary>ported from: src/compiler/checker.ts:54307 — <c>NodeBuilderContext.mustCreateTypeParameterSymbolList</c>.</summary>
    public bool MustCreateTypeParameterSymbolList { get; set; }

    /// <summary>ported from: src/compiler/checker.ts:54308 — <c>NodeBuilderContext.typeParameterSymbolList</c>.</summary>
    public HashSet<int>? TypeParameterSymbolList { get; set; }

    /// <summary>ported from: src/compiler/checker.ts:54309 — <c>NodeBuilderContext.mustCreateTypeParametersNamesLookups</c>.</summary>
    public bool MustCreateTypeParametersNamesLookups { get; set; }

    /// <summary>ported from: src/compiler/checker.ts:54310 — <c>NodeBuilderContext.typeParameterNames</c>.</summary>
    public Dictionary<int, Identifier>? TypeParameterNames { get; set; }

    /// <summary>ported from: src/compiler/checker.ts:54311 — <c>NodeBuilderContext.typeParameterNamesByText</c>.</summary>
    public HashSet<string>? TypeParameterNamesByText { get; set; }

    /// <summary>ported from: src/compiler/checker.ts:54312 — <c>NodeBuilderContext.typeParameterNamesByTextNextNameCount</c>.</summary>
    public Dictionary<string, int>? TypeParameterNamesByTextNextNameCount { get; set; }

    /// <summary>ported from: src/compiler/checker.ts:54313 — <c>NodeBuilderContext.usedSymbolNames</c>.</summary>
    public HashSet<string>? UsedSymbolNames { get; set; }

    /// <summary>ported from: src/compiler/checker.ts:54314 — <c>NodeBuilderContext.remappedSymbolNames</c>.</summary>
    public Dictionary<int, string>? RemappedSymbolNames { get; set; }

    /// <summary>ported from: src/compiler/checker.ts:54315 — <c>NodeBuilderContext.remappedSymbolReferences</c>.</summary>
    public Dictionary<int, ISymbol>? RemappedSymbolReferences { get; set; }

    /// <summary>ported from: src/compiler/checker.ts:54316 — <c>NodeBuilderContext.enclosingSymbolTypes</c>.</summary>
    public Dictionary<int, IType>? EnclosingSymbolTypes { get; set; }

    /// <summary>ported from: src/compiler/checker.ts:54317 — <c>NodeBuilderContext.reverseMappedStack</c>.</summary>
    public List<IReverseMappedSymbol>? ReverseMappedStack { get; set; }

    /// <summary>ported from: src/compiler/checker.ts:54318 — <c>NodeBuilderContext.bundled</c>.</summary>
    public bool Bundled { get; set; }

    /// <summary>ported from: src/compiler/checker.ts:54319 — <c>NodeBuilderContext.mapper</c>.</summary>
    public object? Mapper { get; set; }

    /// <summary>ported from: src/compiler/checker.ts:54321 — <c>NodeBuilderContext.depth</c>.</summary>
    public int Depth { get; set; }

    /// <summary>ported from: src/compiler/checker.ts:54322 — <c>NodeBuilderContext.suppressReportInferenceFallback</c> (from the base interface).</summary>
    public bool SuppressReportInferenceFallback { get; set; }

    /// <summary>ported from: src/compiler/checker.ts:54323 — <c>NodeBuilderContext.typeStack</c>.</summary>
    public List<int>? TypeStack { get; set; }

    /// <summary>ported from: src/compiler/checker.ts:54326 — <c>NodeBuilderContext.out</c>.</summary>
    public IWriterContextOut? Out { get; set; }

    /// <summary>ported from: src/compiler/types.ts:10621 — the base interface's <c>noInferenceFallback</c> slot.</summary>
    public bool? NoInferenceFallback { get; set; }
}
