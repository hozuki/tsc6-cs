namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:6138-6152 (TransientSymbol and MappedSymbol)
// ported from: src/compiler/types.ts:6161-6164 (ReverseMappedSymbol)
/// <summary>
/// The symbol the checker synthesizes — TS's <c>TransientSymbol</c>, and the mapped/reverse-mapped symbols that are
/// the same object at run time.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS's <c>TransientSymbol extends Symbol { links: TransientSymbolLinks }</c> and its
/// <c>MappedSymbol</c>/<c>ReverseMappedSymbol</c> narrow only that slot. The checker's casts
/// (<c>createSymbol(…) as MappedSymbol</c>, checker.ts:14862) are structural — no object is ever converted — so one
/// C# class implements all three interfaces; a cast to <see cref="IMappedSymbol"/>/<see cref="IReverseMappedSymbol"/>
/// therefore succeeds exactly where it does upstream. The generated interfaces carry no <c>Links</c> member of their
/// own (a narrowing of an inherited member is dropped, NodeEmitter.cs:114-124), so no explicit bridge is needed.
/// </para>
/// <para>
/// PORT-NOTE: <see cref="Links"/> is not a slot of its own: TS has one <c>links</c> property per symbol, and both
/// the <c>Symbol</c> view (<c>ISymbolLinks</c>) and the transient view (<c>ITransientSymbolLinks</c>) must see it.
/// The port therefore narrows the base slot rather than shadowing the storage: the getter reads through to
/// <c>Symbol.Links</c> and the setter writes it. A symbol whose links were assigned as a plain
/// <see cref="ISymbolLinks"/> would fail the getter's cast, which is the invariant <c>createSymbol</c> establishes
/// (checker.ts:2636).
/// </para>
/// </remarks>
internal class TransientSymbol : Symbol, ITransientSymbol, IMappedSymbol, IReverseMappedSymbol
{
    /// <summary>ported from: src/compiler/utilities.ts:8471-8488 — the <c>Symbol</c> constructor.</summary>
    public TransientSymbol(SymbolFlags flags, string name)
        : base(flags, name)
    {
    }

    /// <summary>ported from: src/compiler/types.ts:6140 — <c>TransientSymbol.links</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the storage is <see cref="Symbol.Links"/>'s; this member only narrows the type, which is the read
    /// half of §12.2. <c>getSymbolLinks</c> (checker.ts:2933) reaches the transient half through it.
    /// </remarks>
    public new ITransientSymbolLinks? Links
    {
        get => (ITransientSymbolLinks?)base.Links;
        set => base.Links = value;
    }
}
