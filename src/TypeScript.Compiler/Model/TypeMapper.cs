namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:7086-7092 (TypeMapper)
// ported from: src/compiler/checker.ts:20572-20636 (createTypeMapper, getMappedType, makeUnaryTypeMapper,
//              makeArrayTypeMapper, makeFunctionTypeMapper, makeDeferredTypeMapper, makeCompositeTypeMapper)
/// <summary>
/// The instantiation map the checker applies to a type — TS's <c>TypeMapper</c>.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS declares <c>TypeMapper</c> as a union of five anonymous object types discriminated by
/// <c>kind</c> (types.ts:7086-7092), so the generated model has no C# name for it and every slot that holds one is
/// <c>object?</c> (<c>Signature.Mapper</c>, <c>SymbolLinks.Mapper</c>, <c>ObjectType.Mapper</c>,
/// <c>ConditionalType.Mapper</c>, …). The port follows the <see cref="TypePredicate"/> precedent — one class carrying
/// the slots of every variant, with <see cref="Kind"/> as the discriminator; a slot a variant does not use stays
/// <c>null</c>, which is the <c>undefined</c> TS's object would answer for it. The five <c>make*TypeMapper</c>
/// factories in <c>Checker/Checker.Instantiation.cs</c> are the only constructors, exactly as upstream.
/// </para>
/// <para>
/// PORT-NOTE: TS's <c>Deferred</c> variant types <c>targets</c> as <c>(() =&gt; Type)[]</c> while the <c>Array</c> and
/// <c>Simple</c> variants use plain types; C# cannot give one slot two element types, so the deferred targets get
/// their own slot, <see cref="TargetFactories"/>, and <see cref="Targets"/> stays the plain-type slot.
/// <c>getMappedType</c> reads the one its <see cref="Kind"/> selects (checker.ts:20576-20608).
/// </para>
/// <para>
/// PORT-NOTE: <c>Debug.attachDebugPrototypeIfDebug(...)</c> (debug.ts:1600-1604) installs the upstream
/// <c>getTypeMapperKindName</c> helper onto the object for the debugger; the port has no prototype patching, so the
/// factories build this class directly and only the <c>Debug.isDebugging</c> test for
/// <see cref="DebugInfo"/> is kept (checker.ts:20617-20619).
/// </para>
/// </remarks>
internal sealed class TypeMapper
{
    /// <summary>ported from: src/compiler/types.ts:7087 — the discriminators of the five variants.</summary>
    public TypeMapKind Kind { get; set; }

    // ---- TypeMapKind.Simple (types.ts:7087) ------------------------------------------------

    /// <summary>ported from: src/compiler/types.ts:7087 — <c>source</c>.</summary>
    public IType? Source { get; set; }

    /// <summary>ported from: src/compiler/types.ts:7087 — <c>target</c>.</summary>
    public IType? Target { get; set; }

    // ---- TypeMapKind.Array / TypeMapKind.Deferred (types.ts:7088-7089) ---------------------

    /// <summary>ported from: src/compiler/types.ts:7088 — <c>sources</c>.</summary>
    public List<IType>? Sources { get; set; }

    /// <summary>ported from: src/compiler/types.ts:7088 — <c>targets</c> of the <c>Array</c> variant.</summary>
    public List<IType>? Targets { get; set; }

    /// <summary>ported from: src/compiler/types.ts:7089 — <c>targets</c> of the <c>Deferred</c> variant.</summary>
    public List<Func<IType>>? TargetFactories { get; set; }

    // ---- TypeMapKind.Function (types.ts:7090) ---------------------------------------------

    /// <summary>ported from: src/compiler/types.ts:7090 — <c>func</c>.</summary>
    public Func<IType, IType>? Func { get; set; }

    /// <summary>ported from: src/compiler/types.ts:7090 — <c>debugInfo</c>.</summary>
    public Func<string>? DebugInfo { get; set; }

    // ---- TypeMapKind.Composite | TypeMapKind.Merged (types.ts:7091) -----------------------

    /// <summary>ported from: src/compiler/types.ts:7091 — <c>mapper1</c>.</summary>
    public TypeMapper? Mapper1 { get; set; }

    /// <summary>ported from: src/compiler/types.ts:7091 — <c>mapper2</c>.</summary>
    public TypeMapper? Mapper2 { get; set; }
}
