namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:6958-6961 (TemplateLiteralType), 6963-6966 (StringMappingType), 6975-6979 (SubstitutionType)
// ported from: src/compiler/checker.ts:17240-17251 (getOrCreateSubstitutionType), 19109-19114 (createTemplateLiteralType),
//             19165-19169 (createStringMappingType)
/// <summary>
/// A template literal type <c>`a${T}b`</c> — TS's <c>TemplateLiteralType</c>.
/// </summary>
/// <remarks>
/// PORT-NOTE: created by <c>createTemplateLiteralType</c> (checker.ts:19109-19114) with
/// <c>createType(TypeFlags.TemplateLiteral) as TemplateLiteralType</c>, so the <c>TypeFlags → class</c> dispatch must
/// map <c>TypeFlags.TemplateLiteral</c> here. TS's invariant is that <c>texts</c> has one element more than
/// <c>types</c> and <c>types</c> is never empty; nothing in the class can state that, so the two list slots stay plain
/// and the constructors of the checker keep the invariant.
/// </remarks>
internal partial class TemplateLiteralType : InstantiableType, ITemplateLiteralType
{
    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — TS's one type constructor, forwarded so that the
    /// <c>TypeFlags → class</c> dispatch (M6-2) can construct this class.</summary>
    public TemplateLiteralType(object? checker, TypeFlags flags) : base(checker, flags)
    {
    }

    /// <summary>ported from: src/compiler/types.ts:6959 — <c>TemplateLiteralType.texts</c>.</summary>
    public List<string>? Texts { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6960 — <c>TemplateLiteralType.types</c>.</summary>
    public List<IType>? Types { get; set; }
}

/// <summary>
/// An intrinsic string-mapping type (<c>Uppercase&lt;T&gt;</c> and friends) — TS's <c>StringMappingType</c>.
/// </summary>
/// <remarks>
/// PORT-NOTE: created by <c>createStringMappingType</c> (checker.ts:19165-19169) with
/// <c>createTypeWithSymbol(TypeFlags.StringMapping, symbol) as StringMappingType</c>; <c>symbol</c> comes from
/// <see cref="Type"/>, and <c>type</c> holds the mapped type. <c>getStringMappingType</c> (checker.ts:19116-19155)
/// resolves the literal cases eagerly and only reaches this class for the generic ones, cached in
/// <c>stringMappingTypes</c>.
/// </remarks>
internal partial class StringMappingType : InstantiableType, IStringMappingType
{
    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — TS's one type constructor, forwarded so that the
    /// <c>TypeFlags → class</c> dispatch (M6-2) can construct this class.</summary>
    public StringMappingType(object? checker, TypeFlags flags) : base(checker, flags)
    {
    }

    /// <summary>ported from: src/compiler/types.ts:6965 — <c>StringMappingType.type</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS names this slot <c>type</c> (the mapped operand) while the enclosing class is also a type; C#
    /// allows the member and keeps the generated name, as it does for <see cref="IndexedAccessType.ObjectType"/>.
    /// </remarks>
    public IType? Type { get; set; }
}

/// <summary>
/// A type-parameter substitution created inside a conditional type's true branch — TS's <c>SubstitutionType</c>.
/// </summary>
/// <remarks>
/// PORT-NOTE: created by <c>getOrCreateSubstitutionType</c> (checker.ts:17240-17251) with
/// <c>createType(TypeFlags.Substitution) as SubstitutionType</c> and cached in <c>substitutionTypes</c>; it sets
/// <c>baseType</c>/<c>constraint</c> and leaves <c>objectFlags</c> at zero (the flag is only meaningful for the
/// <c>NoInfer</c> shape, which <c>getNoInferType</c> at checker.ts:17215 marks separately).
/// </remarks>
internal partial class SubstitutionType : InstantiableType, ISubstitutionType
{
    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — TS's one type constructor, forwarded so that the
    /// <c>TypeFlags → class</c> dispatch (M6-2) can construct this class.</summary>
    public SubstitutionType(object? checker, TypeFlags flags) : base(checker, flags)
    {
    }

    /// <summary>ported from: src/compiler/types.ts:6976 — <c>SubstitutionType.objectFlags</c>.</summary>
    public ObjectFlags ObjectFlags { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6977 — <c>SubstitutionType.baseType</c>.</summary>
    public IType? BaseType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6978 — <c>SubstitutionType.constraint</c>.</summary>
    public IType? Constraint { get; set; }
}
