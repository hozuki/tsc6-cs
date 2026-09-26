namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:6767-6771 (AnonymousType), 6774-6776 (InstantiationExpressionType),
//             6779-6788 (MappedType), 6790-6793 (EvolvingArrayType), 6796-6800 (ReverseMappedType),
//             6817-6819 (FreshObjectLiteralType)
/// <summary>
/// An instantiated (or freshly created) anonymous object type — TS's <c>AnonymousType</c>.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: the leaf of the object family's chain is <see cref="InstantiationExpressionType"/>, which this class
/// derives from, so every object type the factories build is an <c>AnonymousType</c> instance. That is TS's shape too:
/// <c>createAnonymousType</c> (checker.ts:5660-5662) is <c>createObjectType(ObjectFlags.Anonymous, symbol)</c> plus
/// <c>setStructuredTypeMembers</c>, and <c>createObjectType</c> serves every object face.
/// </para>
/// <para>
/// PORT-NOTE: <c>target</c> (types.ts:6768) narrows <c>TypeReference.target</c> (types.ts:6647) from
/// <c>GenericType</c> to <c>AnonymousType</c>; the port bridges it to the single <see cref="TypeReference.TargetSlot"/>
/// so a write through either face is visible to the other, exactly as upstream's one property is.
/// <c>mapper</c> (types.ts:6769) and <c>instantiations</c> (types.ts:6770) are already the inherited
/// <see cref="TypeReference.Mapper"/>/<see cref="TypeReference.Instantiations"/>, whose types are identical.
/// </para>
/// </remarks>
internal partial class AnonymousType : TupleType, IAnonymousType
{
    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — TS's one type constructor, forwarded so that the
    /// <c>TypeFlags → class</c> dispatch (M6-2) can construct this class.</summary>
    public AnonymousType(object? checker, TypeFlags flags) : base(checker, flags)
    {
    }

    /// <summary>ported from: src/compiler/types.ts:6768 — <c>AnonymousType.target</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: explicit implementation, because <c>TypeReference</c> already implements the same name as
    /// <c>IGenericType?</c> and C# has no covariance for interface members. Both read and write the one
    /// <see cref="TypeReference.TargetSlot"/>; <c>instantiateAnonymousType</c> (checker.ts:20980) writes an
    /// <c>AnonymousType</c> there and <c>getObjectTypeInstantiation</c> (checker.ts:20739) reads it back as one, which is
    /// the direction the cast in the getter has to cover.
    /// </remarks>
    IAnonymousType? IAnonymousType.Target
    {
        get => (IAnonymousType?)TargetSlot;
        set => TargetSlot = value;
    }
}

/// <summary>ported from: src/compiler/types.ts:6779-6788 — <c>MappedType</c>.</summary>
/// <remarks>
/// PORT-NOTE: <c>getTypeFromMappedTypeNode</c> builds it as <c>createObjectType(ObjectFlags.Mapped, node.symbol) as MappedType</c>
/// (checker.ts:19736-19737); the instance is the family's leaf, so that cast is an upcast in the port.
/// </remarks>
internal partial class MappedType : AnonymousType, IMappedType
{
    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — TS's one type constructor, forwarded so that the
    /// <c>TypeFlags → class</c> dispatch (M6-2) can construct this class.</summary>
    public MappedType(object? checker, TypeFlags flags) : base(checker, flags)
    {
    }

    /// <summary>ported from: src/compiler/types.ts:6780 — <c>MappedType.declaration</c>.</summary>
    public MappedTypeNode? Declaration { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6781 — <c>MappedType.typeParameter</c>.</summary>
    public ITypeParameter? TypeParameter { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6782 — <c>MappedType.constraintType</c>.</summary>
    public IType? ConstraintType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6783 — <c>MappedType.nameType</c>.</summary>
    public IType? NameType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6784 — <c>MappedType.templateType</c>.</summary>
    public IType? TemplateType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6785 — <c>MappedType.modifiersType</c>.</summary>
    public IType? ModifiersType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6786 — <c>MappedType.resolvedApparentType</c>.</summary>
    public IType? ResolvedApparentType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6787 — <c>MappedType.containsError</c>.</summary>
    public bool? ContainsError { get; set; }
}

/// <summary>ported from: src/compiler/types.ts:6817-6819 — <c>FreshObjectLiteralType</c>.</summary>
/// <remarks>
/// PORT-NOTE: TS creates the fresh object literal type with <c>createObjectType(ObjectFlags.ObjectLiteral | ObjectFlags.FreshLiteral, …)</c>
/// and casts it to this face; <c>getRegularTypeOfObjectLiteral</c> (checker.ts:25922) reads/writes
/// <c>regularType</c>. <c>RegularType</c> here is a different slot from <c>FreshableType.regularType</c>
/// (types.ts:6472) — those live on the literal/intrinsic family.
/// </remarks>
internal partial class FreshObjectLiteralType : MappedType, IFreshObjectLiteralType
{
    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — TS's one type constructor, forwarded so that the
    /// <c>TypeFlags → class</c> dispatch (M6-2) can construct this class.</summary>
    public FreshObjectLiteralType(object? checker, TypeFlags flags) : base(checker, flags)
    {
    }

    /// <summary>ported from: src/compiler/types.ts:6818 — <c>FreshObjectLiteralType.regularType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>new</c> is required because <c>UnionType.regularType</c> (types.ts:6745, the inherited
    /// <see cref="UnionOrIntersectionType.RegularType"/>) is a different property that happens to share the name — TS
    /// declares it on <c>UnionType</c> and this one on <c>FreshObjectLiteralType</c>, and no object is both at run time
    /// (<c>getRegularTypeOfObjectLiteral</c> reads this one only under <c>ObjectFlags.FreshLiteral</c>,
    /// checker.ts:25922-25923).
    /// </remarks>
    public new IResolvedType? RegularType { get; set; }
}

/// <summary>ported from: src/compiler/types.ts:6790-6793 — <c>EvolvingArrayType</c>.</summary>
/// <remarks>
/// PORT-NOTE: created by <c>createEvolvingArrayType</c> (checker.ts:28641-28645) as
/// <c>createObjectType(ObjectFlags.EvolvingArray)</c> and cast to this face; the type never escapes
/// <c>getFlowTypeOfReference</c> (checker.ts:28637-28640).
/// </remarks>
internal partial class EvolvingArrayType : FreshObjectLiteralType, IEvolvingArrayType
{
    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — TS's one type constructor, forwarded so that the
    /// <c>TypeFlags → class</c> dispatch (M6-2) can construct this class.</summary>
    public EvolvingArrayType(object? checker, TypeFlags flags) : base(checker, flags)
    {
    }

    /// <summary>ported from: src/compiler/types.ts:6791 — <c>EvolvingArrayType.elementType</c>.</summary>
    public IType? ElementType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6792 — <c>EvolvingArrayType.finalArrayType</c>.</summary>
    public IType? FinalArrayType { get; set; }
}

/// <summary>ported from: src/compiler/types.ts:6796-6800 — <c>ReverseMappedType</c>.</summary>
/// <remarks>
/// PORT-NOTE: created by <c>createReverseMappedType</c> as
/// <c>createObjectType(ObjectFlags.ReverseMapped | ObjectFlags.Anonymous, undefined)</c> and cast to this face
/// (checker.ts:26499); <c>inferFromObjectTypes</c>
/// pushes properties of it onto <c>context.reverseMappedStack</c> (checker.ts:7851).
/// </remarks>
internal partial class ReverseMappedType : EvolvingArrayType, IReverseMappedType
{
    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — TS's one type constructor, forwarded so that the
    /// <c>TypeFlags → class</c> dispatch (M6-2) can construct this class.</summary>
    public ReverseMappedType(object? checker, TypeFlags flags) : base(checker, flags)
    {
    }

    /// <summary>ported from: src/compiler/types.ts:6797 — <c>ReverseMappedType.source</c>.</summary>
    public IType? Source { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6798 — <c>ReverseMappedType.mappedType</c>.</summary>
    public IMappedType? MappedType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6799 — <c>ReverseMappedType.constraintType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>IndexType</c> — an interface <see cref="IndexType"/> implements — and a different property
    /// from <see cref="MappedType.ConstraintType"/> (types.ts:6782), which holds the mapped type's own constraint. The
    /// two share a name and no object is both at run time (<c>createReverseMappedType</c> creates this shape with
    /// <c>ObjectFlags.ReverseMapped</c>, checker.ts:26499), so <c>new</c> is the faithful spelling.
    /// </remarks>
    public new IIndexType? ConstraintType { get; set; }
}

/// <summary>ported from: src/compiler/types.ts:6774-6776 — <c>InstantiationExpressionType</c>.</summary>
/// <remarks>
/// <para>
/// PORT-NOTE: this is the leaf of the object family's chain — the class <c>createObjectType</c>/<c>createAnonymousType</c>
/// instantiate (see <see cref="ObjectType"/>). It is named after the most derived face TS declares, which is what keeps
/// every <c>as</c> cast the checker performs on an object type a successful up- or down-cast here.
/// </para>
/// <para>
/// PORT-NOTE: <c>node</c> narrows <c>TypeReference.node</c> (types.ts:6648,
/// <c>TypeReferenceNode | ArrayTypeNode | TupleTypeNode</c>) to <c>NodeWithTypeArguments</c> (types.ts:6775); the
/// port bridges the two faces to the single <see cref="TypeReference.Node"/> slot, and the getter's cast is exactly
/// the narrowing TS's structural typing did — <c>getObjectTypeInstantiation</c> (checker.ts:20741) and
/// <c>instantiateAnonymousType</c> (checker.ts:20978) only ever read it back where the node is one.
/// </para>
/// </remarks>
internal partial class InstantiationExpressionType : ReverseMappedType, IInstantiationExpressionType
{
    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — TS's one type constructor, forwarded so that the
    /// <c>TypeFlags → class</c> dispatch (M6-2) can construct this class.</summary>
    public InstantiationExpressionType(object? checker, TypeFlags flags) : base(checker, flags)
    {
    }

    /// <summary>ported from: src/compiler/types.ts:6775 — <c>InstantiationExpressionType.node</c>.</summary>
    INodeWithTypeArguments? IInstantiationExpressionType.Node
    {
        get => (INodeWithTypeArguments?)Node;
        set => Node = (Node?)value;
    }
}
