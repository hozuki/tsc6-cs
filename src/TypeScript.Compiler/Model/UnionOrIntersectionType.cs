namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:6723-6739 (UnionOrIntersectionType), 6741-6754 (UnionType), 6756-6761 (IntersectionType)
// ported from: src/compiler/types.ts:6600-6608 (ObjectType), 6822-6826 (IterationTypes), 6830-6838 (IterableOrIteratorType),
//             6841-6845 (PromiseOrAwaitableType), 6848-6851 (SyntheticDefaultModuleType)
/// <summary>
/// The base of the union/intersection family — TS's <c>UnionOrIntersectionType</c>.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS's runtime object for a union or intersection is a plain <c>Type</c> viewed through this interface.
/// The port needs a class the checker can cast to, so <see cref="UnionType"/> and <see cref="IntersectionType"/>
/// derive from it, and the object family derives from it too (see <see cref="ObjectType"/>): TS writes
/// <c>(type as UnionOrIntersectionType).resolvedIndexType</c> on types that are unions, intersections and objects
/// alike (<c>getIndexType</c>, <c>getBaseConstraint</c>), and only a shared base makes that cast a no-op.
/// </para>
/// <para>
/// PORT-NOTE: the class also carries <c>IObjectType</c>'s slots and <c>IUnionType</c>'s, because
/// <c>IterableOrIteratorType</c> (types.ts:6830) is declared <c>extends ObjectType, UnionType</c> and
/// <c>PromiseOrAwaitableType</c> (types.ts:6841) likewise; <c>getCachedIterationTypes</c> (checker.ts:45899-45905)
/// and <c>getPromisedTypeOfPromise</c> (checker.ts:43511) cast arbitrary object/union/intersection types to them.
/// The generated <c>IIterableOrIteratorType</c> therefore forces both member sets onto this one class. TS answers
/// <c>undefined</c> for the slots a given object does not carry; the port's <c>null</c> is the same thing.
/// </para>
/// <para>
/// PORT-NOTE: TS's <c>StructuredType = ObjectType | UnionType | IntersectionType</c> (types.ts:6763) has no C# name, but
/// this class is the nearest common type of all three, so a member TS types by that union takes
/// <see cref="UnionOrIntersectionType"/> here (<c>resolveStructuredTypeMembers</c>, checker.ts:5648, is the one that
/// matters). Likewise <c>IterableOrIteratorType</c> is not a class of its own: the interface is on this base, which is
/// what makes <c>getCachedIterationTypes</c>'s cast succeed for objects, unions and intersections alike.
/// </para>
/// <para>
/// PORT-NOTE: <c>IterableOrIteratorType</c>'s and <c>PromiseOrAwaitableType</c>'s slots are caches only —
/// every reader recomputes when they are absent (checker.ts:45956, :43512-43519) — so a type that the checker views
/// through one of them but that is not an instance of this family (a type parameter, say) loses only the cache.
/// </para>
/// </remarks>
internal partial class UnionOrIntersectionType : Type, IUnionOrIntersectionType, IUnionType, IObjectType, IIterableOrIteratorType, IPromiseOrAwaitableType, ISyntheticDefaultModuleType
{
    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — TS's one type constructor, forwarded so that the
    /// <c>TypeFlags → class</c> dispatch (M6-2) can construct this class.</summary>
    public UnionOrIntersectionType(ITypeChecker checker, TypeFlags flags) : base(checker, flags)
    {
    }

    /// <summary>ported from: src/compiler/types.ts:6724 — <c>UnionOrIntersectionType.types</c>.</summary>
    public List<IType>? Types { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6726 — <c>UnionOrIntersectionType.objectFlags</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: declared here once for both this interface and <c>ObjectType</c> (types.ts:6601) — the two slots are
    /// the same property upstream (checker.ts:20793 writes it through <c>ObjectFlagsType</c> on a union).
    /// </remarks>
    public ObjectFlags ObjectFlags { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6728 — <c>UnionOrIntersectionType.propertyCache</c>.</summary>
    public SymbolTable? PropertyCache { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6730 — <c>UnionOrIntersectionType.propertyCacheWithoutObjectFunctionPropertyAugment</c>.</summary>
    public SymbolTable? PropertyCacheWithoutObjectFunctionPropertyAugment { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6732 — <c>UnionOrIntersectionType.resolvedProperties</c>.</summary>
    public List<ISymbol>? ResolvedProperties { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6734 — <c>UnionOrIntersectionType.resolvedIndexType</c>.</summary>
    public IIndexType? ResolvedIndexType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6736 — <c>UnionOrIntersectionType.resolvedStringIndexType</c>.</summary>
    public IIndexType? ResolvedStringIndexType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6738 — <c>UnionOrIntersectionType.resolvedBaseConstraint</c>.</summary>
    public IType? ResolvedBaseConstraint { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6743 — <c>UnionType.resolvedReducedType</c>.</summary>
    public IType? ResolvedReducedType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6745 — <c>UnionType.regularType</c>.</summary>
    public IUnionType? RegularType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6747 — <c>UnionType.origin</c>.</summary>
    public IType? Origin { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6749 — <c>UnionType.keyPropertyName</c>.</summary>
    public string? KeyPropertyName { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6751 — <c>UnionType.constituentMap</c>.</summary>
    public Dictionary<int, IType>? ConstituentMap { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6753 — <c>UnionType.arrayFallbackSignatures</c>.</summary>
    public List<ISignature>? ArrayFallbackSignatures { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6602 — <c>ObjectType.members</c>.</summary>
    public SymbolTable? Members { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6603 — <c>ObjectType.properties</c>.</summary>
    public List<ISymbol>? Properties { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6604 — <c>ObjectType.callSignatures</c>.</summary>
    public List<ISignature>? CallSignatures { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6605 — <c>ObjectType.constructSignatures</c>.</summary>
    public List<ISignature>? ConstructSignatures { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6606 — <c>ObjectType.indexInfos</c>.</summary>
    public List<IIndexInfo>? IndexInfos { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6607 — <c>ObjectType.objectTypeWithoutAbstractConstructSignatures</c>.</summary>
    public IObjectType? ObjectTypeWithoutAbstractConstructSignatures { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6831 — <c>IterableOrIteratorType.iterationTypesOfGeneratorReturnType</c>.</summary>
    public IIterationTypes? IterationTypesOfGeneratorReturnType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6832 — <c>IterableOrIteratorType.iterationTypesOfAsyncGeneratorReturnType</c>.</summary>
    public IIterationTypes? IterationTypesOfAsyncGeneratorReturnType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6833 — <c>IterableOrIteratorType.iterationTypesOfIterable</c>.</summary>
    public IIterationTypes? IterationTypesOfIterable { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6834 — <c>IterableOrIteratorType.iterationTypesOfIterator</c>.</summary>
    public IIterationTypes? IterationTypesOfIterator { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6835 — <c>IterableOrIteratorType.iterationTypesOfAsyncIterable</c>.</summary>
    public IIterationTypes? IterationTypesOfAsyncIterable { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6836 — <c>IterableOrIteratorType.iterationTypesOfAsyncIterator</c>.</summary>
    public IIterationTypes? IterationTypesOfAsyncIterator { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6837 — <c>IterableOrIteratorType.iterationTypesOfIteratorResult</c>.</summary>
    public IIterationTypes? IterationTypesOfIteratorResult { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6842 — <c>PromiseOrAwaitableType.promiseTypeOfPromiseConstructor</c>.</summary>
    public IType? PromiseTypeOfPromiseConstructor { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6843 — <c>PromiseOrAwaitableType.promisedTypeOfPromise</c>.</summary>
    public IType? PromisedTypeOfPromise { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6844 — <c>PromiseOrAwaitableType.awaitedTypeOfType</c>.</summary>
    public IType? AwaitedTypeOfType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6849 — <c>SyntheticDefaultModuleType.syntheticType</c>.</summary>
    public IType? SyntheticType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6850 — <c>SyntheticDefaultModuleType.defaultOnlyType</c>.</summary>
    public IType? DefaultOnlyType { get; set; }
}

/// <summary>ported from: src/compiler/types.ts:6741-6754 — <c>UnionType</c>.</summary>
/// <remarks>
/// PORT-NOTE: TS's runtime object for a union is a plain <c>Type</c> created by <c>createType(TypeFlags.Union)</c>
/// (checker.ts:18446) and viewed as <c>UnionType</c>. Every slot this face declares lives on
/// <see cref="UnionOrIntersectionType"/> because <c>IterableOrIteratorType</c>/<c>PromiseOrAwaitableType</c> force
/// them there, so the class adds nothing but the name the checker's casts use.
/// </remarks>
internal partial class UnionType : UnionOrIntersectionType
{
    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — TS's one type constructor, forwarded so that the
    /// <c>TypeFlags → class</c> dispatch (M6-2) can construct this class.</summary>
    public UnionType(ITypeChecker checker, TypeFlags flags) : base(checker, flags)
    {
    }

}

/// <summary>ported from: src/compiler/types.ts:6756-6761 — <c>IntersectionType</c>.</summary>
/// <remarks>
/// PORT-NOTE: same shape as <see cref="UnionType"/> — the shared slots are on <see cref="UnionOrIntersectionType"/>
/// so that the object family can be cast to <c>UnionOrIntersectionType</c> (see that class's remarks).
/// </remarks>
internal partial class IntersectionType : UnionOrIntersectionType, IIntersectionType
{
    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — TS's one type constructor, forwarded so that the
    /// <c>TypeFlags → class</c> dispatch (M6-2) can construct this class.</summary>
    public IntersectionType(ITypeChecker checker, TypeFlags flags) : base(checker, flags)
    {
    }

    /// <summary>ported from: src/compiler/types.ts:6758 — <c>IntersectionType.resolvedApparentType</c>.</summary>
    public IType? ResolvedApparentType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6760 — <c>IntersectionType.uniqueLiteralFilledInstantiation</c>.</summary>
    public IType? UniqueLiteralFilledInstantiation { get; set; }
}
