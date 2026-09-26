namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:6853-6860 (InstantiableType), 6864-6881 (TypeParameter),
//             6900-6908 (IndexedAccessType), 6921-6925 (IndexType)
// ported from: src/compiler/checker.ts:5567-5569 (createTypeParameter), 18858-18870 (createIndexType, createOriginIndexType),
//             19171-19179 (createIndexedAccessType), 20672-20676 (cloneTypeParameter)
/// <summary>
/// The base of the "instantiable" types — TS's <c>InstantiableType</c>.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS's runtime object is a plain <c>Type</c> again (<c>createType(TypeFlags.Index)</c> and friends); the
/// port needs a class the checker can cast to, and every instantiable face declares the same three slots, so one C#
/// base carries them: <see cref="TypeParameter"/>, <see cref="IndexType"/>, <see cref="IndexedAccessType"/>,
/// <see cref="ConditionalType"/>, <see cref="SubstitutionType"/>, <see cref="TemplateLiteralType"/> and
/// <see cref="StringMappingType"/> all derive from it.
/// </para>
/// <para>
/// PORT-NOTE: unlike the object family, these classes are <em>not</em> a chain: TS's casts among them are all guarded
/// by a <c>TypeFlags</c> test (<c>(type as TypeParameter)</c> under <c>flags &amp; TypeFlags.TypeParameter</c>,
/// checker.ts:17226), so a chain would only hide a wrong dispatch. TS's two alias unions over them have no C# name —
/// <c>TypeVariable = TypeParameter | IndexedAccessType</c> (types.ts:6910) is used twice as a cast whose only purpose
/// is to type a call that already takes <c>Type</c> (checker.ts:22931, :23898), and
/// <c>InstantiableType | UnionOrIntersectionType</c> (types.ts:6922) is a parameter of
/// <c>createIndexType</c>/<c>getIndexTypeForGenericType</c> (checker.ts:18858, :18987) whose port takes <c>Type</c>.
/// </para>
/// </remarks>
internal partial class InstantiableType : Type, IInstantiableType
{
    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — TS's one type constructor, forwarded so that the
    /// <c>TypeFlags → class</c> dispatch (M6-2) can construct this class.</summary>
    public InstantiableType(ITypeChecker checker, TypeFlags flags) : base(checker, flags)
    {
    }

    /// <summary>ported from: src/compiler/types.ts:6855 — <c>InstantiableType.resolvedBaseConstraint</c>.</summary>
    public IType? ResolvedBaseConstraint { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6857 — <c>InstantiableType.resolvedIndexType</c>.</summary>
    public IIndexType? ResolvedIndexType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6859 — <c>InstantiableType.resolvedStringIndexType</c>.</summary>
    public IIndexType? ResolvedStringIndexType { get; set; }
}

/// <summary>ported from: src/compiler/types.ts:6864-6881 — <c>TypeParameter</c>.</summary>
/// <remarks>
/// PORT-NOTE: <c>createTypeParameter</c> (checker.ts:5567-5569) is <c>createTypeWithSymbol(TypeFlags.TypeParameter, symbol) as TypeParameter</c>
/// and sets nothing else; <c>cloneTypeParameter</c> (checker.ts:20672-20676) then copies <c>target</c>.
/// <c>isThisType</c> is read by <c>isThisTypeParameter</c> and written for the synthesized <c>this</c> type.
/// </remarks>
internal partial class TypeParameter : InstantiableType, ITypeParameter
{
    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — TS's one type constructor, forwarded so that the
    /// <c>TypeFlags → class</c> dispatch (M6-2) can construct this class.</summary>
    public TypeParameter(ITypeChecker checker, TypeFlags flags) : base(checker, flags)
    {
    }

    /// <summary>ported from: src/compiler/types.ts:6870 — <c>TypeParameter.constraint</c>.</summary>
    /// <remarks>PORT-NOTE: TS's comment says to retrieve it with <c>getConstraintFromTypeParameter</c> rather than reading the slot.</remarks>
    public IType? Constraint { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6872 — <c>TypeParameter.default</c>.</summary>
    public IType? Default { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6874 — <c>TypeParameter.target</c>.</summary>
    public ITypeParameter? Target { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6876 — <c>TypeParameter.mapper</c>.</summary>
    public object? Mapper { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6878 — <c>TypeParameter.isThisType</c>.</summary>
    public bool? IsThisType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6880 — <c>TypeParameter.resolvedDefaultType</c>.</summary>
    public IType? ResolvedDefaultType { get; set; }
}

/// <summary>ported from: src/compiler/types.ts:6921-6925 — <c>IndexType</c> (<c>keyof T</c>).</summary>
/// <remarks>
/// PORT-NOTE: created by <c>createIndexType</c>/<c>createOriginIndexType</c> (checker.ts:18858-18870) with
/// <c>createType(TypeFlags.Index) as IndexType</c>, so the <c>TypeFlags → class</c> dispatch must map
/// <c>TypeFlags.Index</c> here.
/// </remarks>
internal partial class IndexType : InstantiableType, IIndexType
{
    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — TS's one type constructor, forwarded so that the
    /// <c>TypeFlags → class</c> dispatch (M6-2) can construct this class.</summary>
    public IndexType(ITypeChecker checker, TypeFlags flags) : base(checker, flags)
    {
    }

    /// <summary>ported from: src/compiler/types.ts:6922 — <c>IndexType.type</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types this <c>InstantiableType | UnionOrIntersectionType</c>, i.e. always a type, and
    /// <c>createIndexType</c> assigns exactly that (checker.ts:18859-18861). The generated slot used to collapse the
    /// union to <c>Node?</c> (§12.4's registered degradation — neither member is a class chain step); <c>tools/Gen</c>
    /// now maps the union to <c>IType</c>, the honest upper bound. <c>getIndexTypeOfType</c> and the tracing hook that
    /// reads <c>(type as IndexType).type.id</c> (Core/Tracing.cs:504) need the <c>Id</c> of a type here.
    /// </remarks>
    public IType? Type { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6924 — <c>IndexType.indexFlags</c>.</summary>
    public IndexFlags IndexFlags { get; set; }
}

/// <summary>ported from: src/compiler/types.ts:6900-6908 — <c>IndexedAccessType</c> (<c>T[K]</c>).</summary>
/// <remarks>
/// PORT-NOTE: created by <c>createIndexedAccessType</c> (checker.ts:19171-19179) with
/// <c>createType(TypeFlags.IndexedAccess) as IndexedAccessType</c>; <c>accessFlags</c> is initialized to
/// <c>AccessFlags.None</c> by the caller chain (<c>getIndexedAccessTypeOrUndefined</c>) and only ever holds
/// <c>AccessFlags.Persistent</c>.
/// </remarks>
internal partial class IndexedAccessType : InstantiableType, IIndexedAccessType
{
    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — TS's one type constructor, forwarded so that the
    /// <c>TypeFlags → class</c> dispatch (M6-2) can construct this class.</summary>
    public IndexedAccessType(ITypeChecker checker, TypeFlags flags) : base(checker, flags)
    {
    }

    /// <summary>ported from: src/compiler/types.ts:6901 — <c>IndexedAccessType.objectType</c>.</summary>
    public IType? ObjectType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6902 — <c>IndexedAccessType.indexType</c>.</summary>
    public IType? IndexType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6904 — <c>IndexedAccessType.accessFlags</c>.</summary>
    public AccessFlags AccessFlags { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6905 — <c>IndexedAccessType.constraint</c>.</summary>
    public IType? Constraint { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6906 — <c>IndexedAccessType.simplifiedForReading</c>.</summary>
    public IType? SimplifiedForReading { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6907 — <c>IndexedAccessType.simplifiedForWriting</c>.</summary>
    public IType? SimplifiedForWriting { get; set; }
}
