namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:6600-6608 (ObjectType), 6629-6634 (InterfaceTypeWithDeclaredMembers),
//             6646-6657 (TypeReference), 6659-6666 (DeferredTypeReference), 6683-6688 (GenericType),
//             6702-6717 (TupleType), 6719-6721 (TupleTypeReference), 6805-6811 (ResolvedType)
// ported from: src/compiler/types.ts:6612-6623 (InterfaceType)
/// <summary>
/// The object face of a type — TS's <c>ObjectType</c>.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS creates every object type with <c>createObjectType(objectFlags, symbol)</c> /
/// <c>createAnonymousType</c> (checker.ts:5552-5561, :5660-5662) — one runtime object, then viewed through
/// <c>ObjectType</c>, <c>ResolvedType</c>, <c>TypeReference</c>, <c>GenericType</c>, <c>TupleType</c>,
/// <c>AnonymousType</c>, <c>MappedType</c>, … with casts that TS only type-checks. The port keeps TS's runtime shape
/// by chaining this family: the class the factories instantiate is the leaf,
/// <see cref="InstantiationExpressionType"/>, so a cast to any face of the family succeeds exactly where TS's cast
/// would. Declare locals with the face the TS variable had (<c>ObjectType</c>, <c>TypeReference</c>, …); the extra
/// slots the chain carries stay <c>null</c> and are the <c>undefined</c> TS would answer for them.
/// </para>
/// <para>
/// PORT-NOTE: the chain is one class per TS interface, and the order follows TS's <c>extends</c> where there is one
/// (<c>GenericType extends InterfaceType, TypeReference</c>: types.ts:6683). Where TS has siblings
/// (<c>AnonymousType</c>, <c>MappedType</c>, <c>FreshObjectLiteralType</c>, <c>EvolvingArrayType</c>,
/// <c>ReverseMappedType</c>, <c>InstantiationExpressionType</c> all <c>extends ObjectType</c>) the order is free and
/// arbitrary; the factories make it unobservable, because they always instantiate the leaf. The membership is not
/// arbitrary: <c>emptyGenericType = createAnonymousType(…) as ObjectType as GenericType</c> (checker.ts:2154-2155)
/// is an <c>AnonymousType</c> object used as a <c>GenericType</c>, and
/// <c>createAnonymousType(…) as AnonymousType</c> is instantiated with <c>result.target = type</c> (checker.ts:20981).
/// </para>
/// <para>
/// PORT-NOTE: the object slots (<c>members</c>/<c>properties</c>/<c>callSignatures</c>/…, types.ts:6602-6607) are
/// declared on <see cref="UnionOrIntersectionType"/>, because <c>IterableOrIteratorType</c> and
/// <c>PromiseOrAwaitableType</c> are <c>extends ObjectType, UnionType</c> (types.ts:6830, :6841) and force both
/// member sets onto one class.
/// </para>
/// </remarks>
internal partial class ObjectType : UnionOrIntersectionType, IObjectType
{
    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — TS's one type constructor, forwarded so that the
    /// <c>TypeFlags → class</c> dispatch (M6-2) can construct this class.</summary>
    public ObjectType(ITypeChecker checker, TypeFlags flags) : base(checker, flags)
    {
    }

}

/// <summary>ported from: src/compiler/types.ts:6805-6811 — <c>ResolvedType</c>.</summary>
/// <remarks>
/// PORT-NOTE: <c>ResolvedType</c> is an empty interface upstream too — every member comes from
/// <c>ObjectType</c>/<c>UnionOrIntersectionType</c>, which is why the scout's contract could call it a
/// "reduced union/object fiction". <c>setStructuredTypeMembers</c> (checker.ts:5648-5658) casts a freshly created
/// object type to it and fills <c>members</c>/<c>properties</c>/<c>callSignatures</c>/<c>constructSignatures</c>/
/// <c>indexInfos</c> — all of which the base already carries, so the port's cast is a no-op upcast.
/// </remarks>
internal partial class ResolvedType : ObjectType, IResolvedType
{
    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — TS's one type constructor, forwarded so that the
    /// <c>TypeFlags → class</c> dispatch (M6-2) can construct this class.</summary>
    public ResolvedType(ITypeChecker checker, TypeFlags flags) : base(checker, flags)
    {
    }

}

/// <summary>ported from: src/compiler/types.ts:6646-6657 (TypeReference), 6659-6666 (DeferredTypeReference), 6719-6721 (TupleTypeReference).</summary>
/// <remarks>
/// <para>
/// PORT-NOTE: <c>target</c> and <c>instantiations</c> are each <em>one</em> slot in TS that two faces type
/// differently: <c>TypeReference.target: GenericType</c> (types.ts:6647) vs <c>AnonymousType.target: AnonymousType</c>
/// (types.ts:6768), and <c>GenericType.instantiations: Map&lt;string, TypeReference&gt;</c> (types.ts:6685) vs
/// <c>DeferredTypeReference.instantiations</c>/<c>AnonymousType.instantiations: Map&lt;string, Type&gt;</c>
/// (types.ts:6665, :6770). C# has no covariance for interface members, so the port keeps one backing field per slot
/// and reinterprets it per interface (<see cref="TargetSlot"/>/<see cref="InstantiationsSlot"/>); a write and a read
/// through different faces of the same object throw instead of silently using the wrong cache. The
/// <c>AnonymousType</c> views live on <see cref="AnonymousType"/>, the <c>GenericType</c> view on
/// <see cref="GenericType"/>.
/// </para>
/// <para>
/// PORT-NOTE: <c>mapper</c> is TS's <c>TypeMapper</c> — a union of function shapes with no C# counterpart — so the
/// generated slot is <c>object?</c> and each use site casts (§5 rule 2, §12.5).
/// </para>
/// </remarks>
internal partial class TypeReference : ResolvedType, ITypeReference, IDeferredTypeReference, ITupleTypeReference
{
    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — TS's one type constructor, forwarded so that the
    /// <c>TypeFlags → class</c> dispatch (M6-2) can construct this class.</summary>
    public TypeReference(ITypeChecker checker, TypeFlags flags) : base(checker, flags)
    {
    }

    /// <summary>PORT-NOTE: the single physical <c>target</c> slot; see the class remarks.</summary>
    private protected object? TargetSlot;

    /// <summary>PORT-NOTE: the single physical <c>instantiations</c> slot; see the class remarks.</summary>
    private protected object? InstantiationsSlot;

    /// <summary>ported from: src/compiler/types.ts:6647 — <c>TypeReference.target</c>.</summary>
    public IGenericType? Target
    {
        get => (IGenericType?)TargetSlot;
        set => TargetSlot = value;
    }

    /// <summary>ported from: src/compiler/types.ts:6648 — <c>TypeReference.node</c>.</summary>
    public Node? Node { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6650 — <c>TypeReference.mapper</c>.</summary>
    public object? Mapper { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6652 — <c>TypeReference.resolvedTypeArguments</c>.</summary>
    public List<IType>? ResolvedTypeArguments { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6654 — <c>TypeReference.literalType</c>.</summary>
    public ITypeReference? LiteralType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6656 — <c>TypeReference.cachedEquivalentBaseType</c>.</summary>
    public IType? CachedEquivalentBaseType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6665 — <c>DeferredTypeReference.instantiations</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: this is <c>DeferredTypeReference.instantiations</c> and <c>AnonymousType.instantiations</c>
    /// (types.ts:6770) at once — both are <c>Map&lt;string, Type&gt;</c>, so one property satisfies both interfaces.
    /// <c>GenericType.instantiations</c> (types.ts:6685) is <c>Map&lt;string, TypeReference&gt;</c> and gets its own
    /// view on <see cref="GenericType"/>.
    /// </remarks>
    public Dictionary<string, IType>? Instantiations
    {
        get => (Dictionary<string, IType>?)InstantiationsSlot;
        set => InstantiationsSlot = value;
    }
}

/// <summary>ported from: src/compiler/types.ts:6612-6623 — <c>InterfaceType</c>.</summary>
/// <remarks>
/// PORT-NOTE: a class/interface declaration type is created by <c>createObjectType(ObjectFlags.Class | ObjectFlags.Interface, symbol)</c>
/// and then used as the <c>target</c> of type references, i.e. as a <c>GenericType</c>
/// (<c>getDeclaredTypeOfClassOrInterface</c> → <c>createTypeReference(classType as GenericType, …)</c>,
/// checker.ts:11571); that is why <see cref="GenericType"/> derives from this class and not the other way round.
/// </remarks>
internal partial class InterfaceType : TypeReference, IInterfaceType
{
    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — TS's one type constructor, forwarded so that the
    /// <c>TypeFlags → class</c> dispatch (M6-2) can construct this class.</summary>
    public InterfaceType(ITypeChecker checker, TypeFlags flags) : base(checker, flags)
    {
    }

    /// <summary>ported from: src/compiler/types.ts:6613 — <c>InterfaceType.typeParameters</c>.</summary>
    public List<ITypeParameter>? TypeParameters { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6614 — <c>InterfaceType.outerTypeParameters</c>.</summary>
    public List<ITypeParameter>? OuterTypeParameters { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6615 — <c>InterfaceType.localTypeParameters</c>.</summary>
    public List<ITypeParameter>? LocalTypeParameters { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6616 — <c>InterfaceType.thisType</c>.</summary>
    public ITypeParameter? ThisType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6618 — <c>InterfaceType.resolvedBaseConstructorType</c>.</summary>
    public IType? ResolvedBaseConstructorType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6620 — <c>InterfaceType.resolvedBaseTypes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's element type is <c>BaseType = ObjectType | IntersectionType | TypeVariable</c>
    /// (types.ts:6626) — i.e. always a type, written by <c>resolveBaseTypesOfClass</c>/
    /// <c>resolveBaseTypesOfInterface</c> (checker.ts:13317, :13400) and read by <c>getBaseTypes</c>. The generated slot
    /// used to collapse the union to <c>List&lt;Node&gt;?</c> (§12.4's registered degradation); <c>tools/Gen</c> now maps
    /// <c>BaseType</c> to <c>IType</c>, which is the honest upper bound of the union.
    /// </remarks>
    public List<IType>? ResolvedBaseTypes { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6622 — <c>InterfaceType.baseTypesResolved</c>.</summary>
    public bool? BaseTypesResolved { get; set; }
}

/// <summary>ported from: src/compiler/types.ts:6629-6634 — <c>InterfaceTypeWithDeclaredMembers</c>.</summary>
/// <remarks>
/// PORT-NOTE: <c>resolveDeclaredMembers</c> (checker.ts:13754-13767) casts the class/interface type to this face once
/// and fills the four <c>declared*</c> slots; everything else (<c>members</c>, <c>properties</c>, …) stays on the
/// ancestors.
/// </remarks>
internal partial class InterfaceTypeWithDeclaredMembers : InterfaceType, IInterfaceTypeWithDeclaredMembers
{
    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — TS's one type constructor, forwarded so that the
    /// <c>TypeFlags → class</c> dispatch (M6-2) can construct this class.</summary>
    public InterfaceTypeWithDeclaredMembers(ITypeChecker checker, TypeFlags flags) : base(checker, flags)
    {
    }

    /// <summary>ported from: src/compiler/types.ts:6630 — <c>InterfaceTypeWithDeclaredMembers.declaredProperties</c>.</summary>
    public List<ISymbol>? DeclaredProperties { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6631 — <c>InterfaceTypeWithDeclaredMembers.declaredCallSignatures</c>.</summary>
    public List<ISignature>? DeclaredCallSignatures { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6632 — <c>InterfaceTypeWithDeclaredMembers.declaredConstructSignatures</c>.</summary>
    public List<ISignature>? DeclaredConstructSignatures { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6633 — <c>InterfaceTypeWithDeclaredMembers.declaredIndexInfos</c>.</summary>
    public List<IIndexInfo>? DeclaredIndexInfos { get; set; }
}

/// <summary>ported from: src/compiler/types.ts:6683-6688 — <c>GenericType</c>.</summary>
/// <remarks>
/// PORT-NOTE: <c>GenericType extends InterfaceType, TypeReference</c> (types.ts:6683) is the one multi-interface face of
/// this family; the port's single class satisfies both because the chain already reaches <c>InterfaceType</c>
/// (<c>ThisType</c>, <c>TypeParameters</c>, <c>Target</c>, …) through <see cref="InterfaceTypeWithDeclaredMembers"/>.
/// </remarks>
internal partial class GenericType : InterfaceTypeWithDeclaredMembers, IGenericType
{
    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — TS's one type constructor, forwarded so that the
    /// <c>TypeFlags → class</c> dispatch (M6-2) can construct this class.</summary>
    public GenericType(ITypeChecker checker, TypeFlags flags) : base(checker, flags)
    {
    }

    /// <summary>ported from: src/compiler/types.ts:6685 — <c>GenericType.instantiations</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>new</c> is required because <see cref="TypeReference.Instantiations"/> already declares this name
    /// with the element type the other two faces use (<c>IType</c>); this is that same physical slot, read through
    /// <c>GenericType</c>'s face. TS writes it in <c>createTypeReference</c> (checker.ts:16924-16928), on a tuple
    /// target (checker.ts:17911-17913) and on <c>emptyGenericType</c> (checker.ts:2155).
    /// </remarks>
    public new Dictionary<string, ITypeReference>? Instantiations
    {
        get => (Dictionary<string, ITypeReference>?)InstantiationsSlot;
        set => InstantiationsSlot = value;
    }

    /// <summary>ported from: src/compiler/types.ts:6687 — <c>GenericType.variances</c>.</summary>
    public List<VarianceFlags>? Variances { get; set; }
}

/// <summary>ported from: src/compiler/types.ts:6702-6717 — <c>TupleType</c>.</summary>
/// <remarks>
/// PORT-NOTE: TS synthesizes a generic interface type per tuple shape (<c>createTupleTargetType</c>,
/// checker.ts:17876) and then refers to it through a <c>TupleTypeReference</c> (types.ts:6719-6721), which the
/// port's <see cref="TypeReference"/> already implements; the <c>tupleType.target</c> reads the checker makes
/// (<c>instantiateMappedTupleType</c>, checker.ts:14196, :25725) are reads of this face.
/// </remarks>
internal partial class TupleType : GenericType, ITupleType
{
    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — TS's one type constructor, forwarded so that the
    /// <c>TypeFlags → class</c> dispatch (M6-2) can construct this class.</summary>
    public TupleType(ITypeChecker checker, TypeFlags flags) : base(checker, flags)
    {
    }

    /// <summary>ported from: src/compiler/types.ts:6703 — <c>TupleType.elementFlags</c>.</summary>
    public List<ElementFlags>? ElementFlags { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6705 — <c>TupleType.minLength</c>.</summary>
    public int MinLength { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6707 — <c>TupleType.fixedLength</c>.</summary>
    public int FixedLength { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6713 — <c>TupleType.hasRestElement</c>.</summary>
    public bool HasRestElement { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6714 — <c>TupleType.combinedFlags</c>.</summary>
    public ElementFlags CombinedFlags { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6715 — <c>TupleType.readonly</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: C# spells a member and its type alike, so the generated slot is named <see cref="Readonly"/>; the
    /// name in TS is <c>readonly</c> and the semantics are unchanged.
    /// </remarks>
    public bool Readonly { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6716 — <c>TupleType.labeledElementDeclarations</c>.</summary>
    public List<Node>? LabeledElementDeclarations { get; set; }
}
