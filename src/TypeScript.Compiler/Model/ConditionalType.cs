namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:6927-6937 (ConditionalRoot), 6940-6956 (ConditionalType)
// ported from: src/compiler/checker.ts:19882-19886 (the deferred conditional type), 19960-19970 (the ConditionalRoot literal)
/// <summary>
/// A conditional type <c>T extends U ? X : Y</c> — TS's <c>ConditionalType</c>.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: created by <c>getConditionalType</c> with <c>createType(TypeFlags.Conditional) as ConditionalType</c>
/// (checker.ts:19882-19886), so the <c>TypeFlags → class</c> dispatch has to map <c>TypeFlags.Conditional</c> here.
/// <c>root</c> is shared by every instantiation of the same conditional type.
/// </para>
/// <para>
/// PORT-NOTE: <c>resolvedConstraintOfDistributive</c> is <c>Type | false</c> (types.ts:6951); <c>false</c> is a
/// sentinel that means "computed, and not distributive", and it is why the generator widened the slot to <c>object?</c>.
/// The writer is <c>getConstraintOfDistributiveConditionalType</c> (checker.ts:15218-15246: a type at :15240, the
/// sentinel at :15245), and the reader (:15219-15220) returns it only when it is truthy. The port keeps the boxed
/// sentinel in this <c>object?</c> slot and reads it with <c>as IType</c>: a boxed <c>false</c> yields <c>null</c>, which
/// is exactly TS's <c>|| undefined</c>, while the <c>is not null</c> test that guards it still tells "computed as
/// false" (the boxed sentinel) apart from "not computed yet" (<c>null</c>).
/// </para>
/// <para>
/// PORT-NOTE: <c>mapper</c>/<c>combinedMapper</c> are TS's <c>TypeMapper</c> (a function-union alias), hence <c>object?</c>.
/// </para>
/// </remarks>
internal partial class ConditionalType : InstantiableType, IConditionalType
{
    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — TS's one type constructor, forwarded so that the
    /// <c>TypeFlags → class</c> dispatch (M6-2) can construct this class.</summary>
    public ConditionalType(object? checker, TypeFlags flags) : base(checker, flags)
    {
    }

    /// <summary>ported from: src/compiler/types.ts:6941 — <c>ConditionalType.root</c>.</summary>
    public IConditionalRoot? Root { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6942 — <c>ConditionalType.checkType</c>.</summary>
    public IType? CheckType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6943 — <c>ConditionalType.extendsType</c>.</summary>
    public IType? ExtendsType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6944 — <c>ConditionalType.resolvedTrueType</c>.</summary>
    public IType? ResolvedTrueType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6945 — <c>ConditionalType.resolvedFalseType</c>.</summary>
    public IType? ResolvedFalseType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6947 — <c>ConditionalType.resolvedInferredTrueType</c>.</summary>
    public IType? ResolvedInferredTrueType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6949 — <c>ConditionalType.resolvedDefaultConstraint</c>.</summary>
    public IType? ResolvedDefaultConstraint { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6951 — <c>ConditionalType.resolvedConstraintOfDistributive</c>.</summary>
    public object? ResolvedConstraintOfDistributive { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6953 — <c>ConditionalType.mapper</c>.</summary>
    public object? Mapper { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6955 — <c>ConditionalType.combinedMapper</c>.</summary>
    public object? CombinedMapper { get; set; }
}

/// <summary>ported from: src/compiler/types.ts:6927-6937 — <c>ConditionalRoot</c>.</summary>
/// <remarks>
/// PORT-NOTE: TS builds this as an object literal with every slot named (checker.ts:19960-19970), so the port's class is
/// the same shape with settable slots — no constructor, matching the <c>FlowSwitchClauseData</c> precedent. Its
/// <c>instantiations</c> cache is separate from a type's
/// <see cref="GenericType.Instantiations"/>/<see cref="TypeReference.Instantiations"/> and is written right after the
/// literal is built (checker.ts:19973-19974).
/// </remarks>
internal partial class ConditionalRoot : IConditionalRoot
{
    /// <summary>ported from: src/compiler/types.ts:6928 — <c>ConditionalRoot.node</c>.</summary>
    public ConditionalTypeNode? Node { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6929 — <c>ConditionalRoot.checkType</c>.</summary>
    public IType? CheckType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6930 — <c>ConditionalRoot.extendsType</c>.</summary>
    public IType? ExtendsType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6931 — <c>ConditionalRoot.isDistributive</c>.</summary>
    public bool IsDistributive { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6932 — <c>ConditionalRoot.inferTypeParameters</c>.</summary>
    public List<ITypeParameter>? InferTypeParameters { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6933 — <c>ConditionalRoot.outerTypeParameters</c>.</summary>
    public List<ITypeParameter>? OuterTypeParameters { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6934 — <c>ConditionalRoot.instantiations</c>.</summary>
    public Dictionary<string, IType>? Instantiations { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6935 — <c>ConditionalRoot.aliasSymbol</c>.</summary>
    public ISymbol? AliasSymbol { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6936 — <c>ConditionalRoot.aliasTypeArguments</c>.</summary>
    public List<IType>? AliasTypeArguments { get; set; }
}
