namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:6056-6101 (the SymbolLinks interface)
// ported from: src/compiler/checker.ts:1451-1453 (the SymbolLinks class that gives it a runtime shape)
/// <summary>
/// The checker's per-symbol cache. Equivalent to TS's <c>SymbolLinks</c> (and, at run time, to
/// <c>TransientSymbolLinks</c>/<c>MappedSymbolLinks</c>/<c>ReverseMappedSymbolLinks</c>).
/// </summary>
/// <remarks>
/// PORT-NOTE: TS declares four link interfaces (<c>SymbolLinks</c> types.ts:6058, <c>TransientSymbolLinks</c>
/// :6134, <c>MappedSymbolLinks</c> :6144, <c>ReverseMappedSymbolLinks</c> :6155) but builds every one of them as the
/// same JS object — <c>createSymbol</c> (checker.ts:2636) assigns a plain <c>SymbolLinks</c> and the mapped-type
/// code then writes <c>links.mappedType</c> on it. A C# cast cannot invent a slot, so this one class implements all
/// four generated interfaces and carries their five extra slots; that is what makes the checker's
/// <c>as MappedSymbolLinks</c> casts (checker.ts:14752,14870) succeed instead of throwing.
/// </para>
/// <para>
/// PORT-NOTE: the interface's <c>_symbolLinksBrand</c> (types.ts:6059) is a TS structural-typing device; the
/// generator skips it (§12.3) and the class does not need it.
/// </para>
/// <para>
/// PORT-NOTE: TS reads the optional booleans by <em>presence</em> — <c>links.exportsChecked === undefined</c> means
/// "not computed yet", which is neither <c>true</c> nor <c>false</c> (checker.ts:2901, :5729, :15138 and the other
/// 20-odd sites). Six slots are therefore <c>bool?</c> and must never be collapsed to <c>bool</c>: they are
/// <see cref="Referenced"/>, <see cref="IsDiscriminantProperty"/>, <see cref="ExportsChecked"/>,
/// <see cref="TypeParametersChecked"/>, <see cref="IsDeclarationWithCollidingName"/> and
/// <see cref="IsConstructorDeclaredProperty"/> (docs/architecture.md §5 rule 1, M6-0 model_gaps 3).
/// </para>
/// </remarks>
internal class SymbolLinks : ISymbolLinks, ITransientSymbolLinks, IMappedSymbolLinks, IReverseMappedSymbolLinks
{
    /// <summary>ported from: src/compiler/types.ts:6060 — <c>SymbolLinks.immediateTarget</c>.</summary>
    public ISymbol? ImmediateTarget { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6061 — <c>SymbolLinks.aliasTarget</c>.</summary>
    public ISymbol? AliasTarget { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6062 — <c>SymbolLinks.target</c>.</summary>
    public ISymbol? Target { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6063 — <c>SymbolLinks.type</c>.</summary>
    public IType? Type { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6064 — <c>SymbolLinks.writeType</c>.</summary>
    public IType? WriteType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6065 — <c>SymbolLinks.nameType</c>.</summary>
    public IType? NameType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6066 — <c>SymbolLinks.uniqueESSymbolType</c>.</summary>
    public IType? UniqueESSymbolType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6067 — <c>SymbolLinks.declaredType</c>.</summary>
    public IType? DeclaredType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6068 — <c>SymbolLinks.typeParameters</c>.</summary>
    public List<ITypeParameter>? TypeParameters { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6069 — <c>SymbolLinks.instantiations</c>.</summary>
    public Dictionary<string, IType>? Instantiations { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6070 — <c>SymbolLinks.inferredClassSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS keys this by <c>SymbolId</c> (a number); the port's generated slot is
    /// <c>Dictionary&lt;int, ITransientSymbol&gt;</c> and the key is <c>Utilities.GetSymbolId</c>'s id.
    /// </remarks>
    public Dictionary<int, ITransientSymbol>? InferredClassSymbol { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6071 — <c>SymbolLinks.mapper</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>TypeMapper</c> is a union of four function shapes (<c>TypeMapKind</c>-discriminated), which
    /// C# cannot express in a slot, so the generated interface types it <c>object?</c>. The checker casts it to the
    /// mapper shape its <c>TypeMapKind</c> says it is (checker.ts:12936 <c>links.mapper!.kind</c>); porting that
    /// dispatch belongs to the instantiation slice (M6-2), which may introduce a real mapper class and narrow this
    /// slot then.
    /// </remarks>
    public object? Mapper { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6072 — <c>SymbolLinks.referenced</c>.</summary>
    /// <remarks>PORT-NOTE: tri-state slot: <c>undefined</c> is "not computed", not <c>false</c>.</remarks>
    public bool? Referenced { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6073 — <c>SymbolLinks.containingType</c>.</summary>
    public IUnionOrIntersectionType? ContainingType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6074 — <c>SymbolLinks.leftSpread</c>.</summary>
    public ISymbol? LeftSpread { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6075 — <c>SymbolLinks.rightSpread</c>.</summary>
    public ISymbol? RightSpread { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6076 — <c>SymbolLinks.syntheticOrigin</c>.</summary>
    public ISymbol? SyntheticOrigin { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6077 — <c>SymbolLinks.isDiscriminantProperty</c>.</summary>
    /// <remarks>PORT-NOTE: tri-state slot: <c>undefined</c> is "not computed", not <c>false</c>.</remarks>
    public bool? IsDiscriminantProperty { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6078 — <c>SymbolLinks.resolvedExports</c>.</summary>
    public SymbolTable? ResolvedExports { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6079 — <c>SymbolLinks.resolvedMembers</c>.</summary>
    public SymbolTable? ResolvedMembers { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6080 — <c>SymbolLinks.exportsChecked</c>.</summary>
    /// <remarks>PORT-NOTE: tri-state slot: <c>undefined</c> is "not computed", not <c>false</c>.</remarks>
    public bool? ExportsChecked { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6081 — <c>SymbolLinks.typeParametersChecked</c>.</summary>
    /// <remarks>PORT-NOTE: tri-state slot: <c>undefined</c> is "not computed", not <c>false</c>.</remarks>
    public bool? TypeParametersChecked { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6082 — <c>SymbolLinks.isDeclarationWithCollidingName</c>.</summary>
    /// <remarks>PORT-NOTE: tri-state slot: <c>undefined</c> is "not computed", not <c>false</c>.</remarks>
    public bool? IsDeclarationWithCollidingName { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6083 — <c>SymbolLinks.originatingImport</c>.</summary>
    public Node? OriginatingImport { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6084 — <c>SymbolLinks.lateSymbol</c>.</summary>
    public ISymbol? LateSymbol { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6085 — <c>SymbolLinks.specifierCache</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the key is TS's <c>ModeAwareCacheKey</c> (a branded string built by
    /// <c>modeAwareCacheKey</c>), which the generator degrades to <c>object</c>; the value is the module specifier.
    /// A caller keying it must use the same key object/string the resolver produced.
    /// </remarks>
    public Dictionary<object, string>? SpecifierCache { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6086 — <c>SymbolLinks.extendedContainers</c>.</summary>
    public List<ISymbol>? ExtendedContainers { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6087 — <c>SymbolLinks.extendedContainersByFile</c>.</summary>
    public Dictionary<int, List<ISymbol>>? ExtendedContainersByFile { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6088 — <c>SymbolLinks.variances</c>.</summary>
    public List<VarianceFlags>? Variances { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6089 — <c>SymbolLinks.deferralConstituents</c>.</summary>
    public List<IType>? DeferralConstituents { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6090 — <c>SymbolLinks.deferralWriteConstituents</c>.</summary>
    public List<IType>? DeferralWriteConstituents { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6091 — <c>SymbolLinks.deferralParent</c>.</summary>
    public IType? DeferralParent { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6092 — <c>SymbolLinks.cjsExportMerged</c>.</summary>
    public ISymbol? CjsExportMerged { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6093 — <c>SymbolLinks.typeOnlyDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types this <c>TypeOnlyAliasDeclaration | false</c> — the <c>false</c> is a "already checked, and
    /// it is not type-only" marker, so a single <c>object?</c> slot has to be read as
    /// <c>null</c> = unchecked / <c>false</c>-boxed = checked-not-type-only / a declaration = checked-type-only. The
    /// port keeps the generated <c>object?</c> whose reader (the alias-resolution slice) unwraps it; introducing a
    /// wrapper type is that slice's decision.
    /// </remarks>
    public object? TypeOnlyDeclaration { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6094 — <c>SymbolLinks.typeOnlyExportStarMap</c>.</summary>
    public Dictionary<string, ExportDeclaration>? TypeOnlyExportStarMap { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6095 — <c>SymbolLinks.typeOnlyExportStarName</c>.</summary>
    public string? TypeOnlyExportStarName { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6096 — <c>SymbolLinks.isConstructorDeclaredProperty</c>.</summary>
    /// <remarks>PORT-NOTE: tri-state slot: <c>undefined</c> is "not computed", not <c>false</c>.</remarks>
    public bool? IsConstructorDeclaredProperty { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6097 — <c>SymbolLinks.tupleLabelDeclaration</c>.</summary>
    public Node? TupleLabelDeclaration { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6098 — <c>SymbolLinks.accessibleChainCache</c>.</summary>
    public Dictionary<string, List<ISymbol>>? AccessibleChainCache { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6099 — <c>SymbolLinks.filteredIndexSymbolCache</c>.</summary>
    public Dictionary<string, ISymbol>? FilteredIndexSymbolCache { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6100 — <c>SymbolLinks.requestedExternalEmitHelpers</c>.</summary>
    public ExternalEmitHelpers? RequestedExternalEmitHelpers { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6135 — <c>TransientSymbolLinks.checkFlags</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: every transient symbol's links carry this (checker.ts:2637 <c>symbol.links.checkFlags = checkFlags ||
    /// CheckFlags.None</c>), and the non-transient links object created by <c>getSymbolLinks</c> never has it
    /// assigned. The slot is therefore non-nullable and starts at <c>CheckFlags.None</c>, which is what
    /// <c>getCheckFlags</c> reads.
    /// </remarks>
    public CheckFlags CheckFlags { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6145 — <c>MappedSymbolLinks.mappedType</c>.</summary>
    public IMappedType? MappedType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6146 — <c>MappedSymbolLinks.keyType</c>.</summary>
    public IType? KeyType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6156 — <c>ReverseMappedSymbolLinks.propertyType</c>.</summary>
    public IType? PropertyType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6158 — <c>ReverseMappedSymbolLinks.constraintType</c>.</summary>
    public IIndexType? ConstraintType { get; set; }
}
