namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:54329-54434 — class SymbolTrackerImpl
/// <summary>
/// The tracker the node builder hands its callers: it forwards every callback to the caller's tracker and records
/// whether a diagnostic was reported — TS's <c>SymbolTrackerImpl</c>.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS's tracker chain unwraps nested <see cref="SymbolTrackerImpl"/> instances in the constructor
/// (checker.ts:54338-54340) so that only the outermost one forwards; the port keeps the same loop. The port's
/// <c>ISymbolTracker</c> is a closed interface — every member of TS's structural tracker type exists on it
/// (<c>NodeInterfaces.cs:3232-3249</c>) — so TS's "does the presenter implement this callback" tests
/// (<c>this.inner?.reportTruncationError</c> and the seven others) are "is there a presenter at all" here, and
/// <c>CanTrackSymbol</c> (TS's <c>!!this.inner?.trackSymbol</c>, checker.ts:54345) is likewise just
/// <c>Inner is not null</c>: a presenter that does not want to track symbols answers <c>false</c> from
/// <see cref="TrackSymbol"/>, which is what the return value is for.
/// </para>
/// <para>
/// PORT-NOTE: TS types <c>moduleResolverHost</c> as
/// <c>ModuleSpecifierResolutionHost &amp; { getCommonSourceDirectory(): string }</c> (checker.ts:54330); the generated
/// <see cref="IModuleSpecifierResolutionHost"/> already carries <c>GetCommonSourceDirectory</c>
/// (<c>NodeInterfaces.cs:1489</c>), so the port's slot needs no extra member.
/// </para>
/// <para>
/// PORT-NOTE: TS's <c>trackSymbol</c> takes a non-optional <c>Symbol</c>; the interface's slot is <c>ISymbol?</c> like
/// every reference slot in the generated model, so the port skips the type-parameter test for a null symbol instead of
/// dereferencing it — TS would throw there.
/// </para>
/// </remarks>
internal sealed class SymbolTrackerImpl : ISymbolTracker
{
    /// <summary>ported from: src/compiler/checker.ts:54337-54346 — the <c>SymbolTrackerImpl</c> constructor.</summary>
    public SymbolTrackerImpl(NodeBuilderContext context, ISymbolTracker? tracker, IModuleSpecifierResolutionHost? moduleResolverHost)
    {
        while (tracker is SymbolTrackerImpl impl)
        {
            tracker = impl.Inner;
        }

        Inner = tracker;
        ModuleResolverHost = moduleResolverHost;
        Context = context;
        CanTrackSymbol = tracker is not null;
    }

    /// <summary>ported from: src/compiler/checker.ts:54330 — <c>SymbolTrackerImpl.moduleResolverHost</c>.</summary>
    public IModuleSpecifierResolutionHost? ModuleResolverHost { get; set; }

    /// <summary>ported from: src/compiler/checker.ts:54331 — <c>SymbolTrackerImpl.context</c>.</summary>
    public NodeBuilderContext Context { get; }

    /// <summary>ported from: src/compiler/checker.ts:54333 — <c>SymbolTrackerImpl.inner</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>readonly</c> field is assigned once in the constructor, so the port has a get-only property.</remarks>
    public ISymbolTracker? Inner { get; }

    /// <summary>ported from: src/compiler/checker.ts:54334 — <c>SymbolTrackerImpl.canTrackSymbol</c>.</summary>
    public bool CanTrackSymbol { get; }

    /// <summary>ported from: src/compiler/checker.ts:54335 — <c>SymbolTrackerImpl.disableTrackSymbol</c>.</summary>
    public bool DisableTrackSymbol { get; set; }

    /// <summary>ported from: src/compiler/checker.ts:54348-54358 — <c>SymbolTrackerImpl.trackSymbol</c>.</summary>
    public bool TrackSymbol(ISymbol? symbol, Node? enclosingDeclaration, SymbolFlags meaning)
    {
        if (Inner is not null && !DisableTrackSymbol)
        {
            if (Inner.TrackSymbol(symbol, enclosingDeclaration, meaning))
            {
                OnDiagnosticReported();
                return true;
            }
            // Skip recording type parameters as they dont contribute to late painted statements
            if (symbol is null || (symbol.Flags & SymbolFlags.TypeParameter) == 0)
            {
                (Context.TrackedSymbols ??= []).Add(new TrackedSymbol(symbol, enclosingDeclaration, meaning));
            }
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:54360-54365 — <c>SymbolTrackerImpl.reportInaccessibleThisError</c>.</summary>
    public void ReportInaccessibleThisError()
    {
        if (Inner is not null)
        {
            OnDiagnosticReported();
            Inner.ReportInaccessibleThisError();
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:54367-54372 — <c>SymbolTrackerImpl.reportPrivateInBaseOfClassExpression</c>.</summary>
    public void ReportPrivateInBaseOfClassExpression(string? propertyName)
    {
        if (Inner is not null)
        {
            OnDiagnosticReported();
            Inner.ReportPrivateInBaseOfClassExpression(propertyName);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:54374-54379 — <c>SymbolTrackerImpl.reportInaccessibleUniqueSymbolError</c>.</summary>
    public void ReportInaccessibleUniqueSymbolError()
    {
        if (Inner is not null)
        {
            OnDiagnosticReported();
            Inner.ReportInaccessibleUniqueSymbolError();
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:54381-54386 — <c>SymbolTrackerImpl.reportCyclicStructureError</c>.</summary>
    public void ReportCyclicStructureError()
    {
        if (Inner is not null)
        {
            OnDiagnosticReported();
            Inner.ReportCyclicStructureError();
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:54388-54393 — <c>SymbolTrackerImpl.reportLikelyUnsafeImportRequiredError</c>.</summary>
    public void ReportLikelyUnsafeImportRequiredError(string? specifier, string? symbolName)
    {
        if (Inner is not null)
        {
            OnDiagnosticReported();
            Inner.ReportLikelyUnsafeImportRequiredError(specifier, symbolName);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:54395-54400 — <c>SymbolTrackerImpl.reportTruncationError</c>.</summary>
    public void ReportTruncationError()
    {
        if (Inner is not null)
        {
            OnDiagnosticReported();
            Inner.ReportTruncationError();
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:54402-54407 — <c>SymbolTrackerImpl.reportNonlocalAugmentation</c>.</summary>
    public void ReportNonlocalAugmentation(SourceFile? containingFile, ISymbol? parentSymbol, ISymbol? augmentingSymbol)
    {
        if (Inner is not null)
        {
            OnDiagnosticReported();
            Inner.ReportNonlocalAugmentation(containingFile, parentSymbol, augmentingSymbol);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:54409-54414 — <c>SymbolTrackerImpl.reportNonSerializableProperty</c>.</summary>
    public void ReportNonSerializableProperty(string? propertyName)
    {
        if (Inner is not null)
        {
            OnDiagnosticReported();
            Inner.ReportNonSerializableProperty(propertyName);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:54416-54418 — <c>SymbolTrackerImpl.onDiagnosticReported</c>.</summary>
    private void OnDiagnosticReported()
    {
        Context.ReportedDiagnostic = true;
    }

    /// <summary>ported from: src/compiler/checker.ts:54420-54425 — <c>SymbolTrackerImpl.reportInferenceFallback</c>.</summary>
    public void ReportInferenceFallback(Node? node)
    {
        if (Inner is not null && !Context.SuppressReportInferenceFallback)
        {
            OnDiagnosticReported();
            Inner.ReportInferenceFallback(node);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:54427-54429 — <c>SymbolTrackerImpl.pushErrorFallbackNode</c>.</summary>
    public void PushErrorFallbackNode(IDeclaration? node)
    {
        Inner?.PushErrorFallbackNode(node);
    }

    /// <summary>ported from: src/compiler/checker.ts:54431-54433 — <c>SymbolTrackerImpl.popErrorFallbackNode</c>.</summary>
    public void PopErrorFallbackNode()
    {
        Inner?.PopErrorFallbackNode();
    }
}
