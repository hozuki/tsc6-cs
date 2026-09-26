namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:6212-6215 (the PatternAmbientModule interface)
/// <summary>
/// The concrete half of <see cref="IPatternAmbientModule"/> — the entry the binder appends to
/// <c>SourceFile.patternAmbientModules</c> for an ambient module whose name contains a <c>*</c>.
/// </summary>
/// <remarks>
/// The interface is generated (Model/NodeInterfaces.cs:2505-2511) but, like every interface that does not reach
/// <c>Node</c>, it gets no generated class (§12.4); this is the hand-written half, on the same arrangement as
/// <see cref="Symbol"/> and <see cref="FlowNode"/>. The binder is the only writer today
/// (<c>bindModuleDeclaration</c>, binder.ts:2361-2368), and module resolution is what will read it.
/// <para>
/// PORT-NOTE: the generated interface types <c>pattern</c> as <c>object?</c> because <c>Pattern</c> is declared
/// outside the extracted sources (Model/NodeInterfaces.cs:2507). <c>Pattern</c> exists in the port as
/// <see cref="Core.Pattern"/> (Core/Core.Strings.cs:154), so this class states the slot as that type — TS's own
/// declaration (types.ts:6213) — and implements the interface face explicitly, boxing on its behalf.
/// </para>
/// <para>
/// PORT-NOTE: TS declares both slots non-optional; the interface's <c>symbol</c> is <c>ISymbol?</c> like every other
/// reference slot in the generated model, so the explicit implementation widens to that and the typed property keeps
/// the invariant.
/// </para>
/// </remarks>
internal sealed class PatternAmbientModule : IPatternAmbientModule
{
    /// <summary>ported from: src/compiler/types.ts:6213 — <c>PatternAmbientModule.pattern</c>.</summary>
    public PatternAmbientModule(Core.Pattern pattern, ISymbol symbol)
    {
        Pattern = pattern;
        Symbol = symbol;
    }

    /// <summary>ported from: src/compiler/types.ts:6213 — <c>PatternAmbientModule.pattern</c>.</summary>
    public Core.Pattern Pattern { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6214 — <c>PatternAmbientModule.symbol</c>.</summary>
    public ISymbol Symbol { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6213 — <c>PatternAmbientModule.pattern</c>, boxed.</summary>
    object? IPatternAmbientModule.Pattern
    {
        get => Pattern;
        set => Pattern = (Core.Pattern)value!;
    }

    /// <summary>ported from: src/compiler/types.ts:6214 — <c>PatternAmbientModule.symbol</c>.</summary>
    ISymbol? IPatternAmbientModule.Symbol
    {
        get => Symbol;
        set => Symbol = value!;
    }
}
