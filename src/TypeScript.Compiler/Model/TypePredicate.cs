namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:5690-5723 (TypePredicateBase, the four variant interfaces, and the TypePredicate union)
// ported from: src/compiler/checker.ts:16121-16123 (createTypePredicate)
/// <summary>
/// A type predicate (<c>x is T</c>, <c>asserts x is T</c>) — TS's <c>TypePredicate</c>.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS declares four variant interfaces and a union over them; every runtime value is the one object literal
/// <c>createTypePredicate(kind, parameterName, parameterIndex, type)</c> builds (checker.ts:16121-16123), and the
/// variants differ only in the declared types of <c>parameterName</c>/<c>parameterIndex</c> (<c>string</c>/<c>number</c>
/// for the two <c>Identifier</c> variants, <c>undefined</c> for the two <c>This</c> variants). The port follows the
/// <see cref="FlowNode"/> precedent — one class implementing all four, with <c>kind</c> as the discriminator — and its
/// constructor is that literal: the three payload arguments and the kind, in the same order.
/// </para>
/// <para>
/// PORT-NOTE: C# cannot implement one member twice with different types, and the faces disagree, so every
/// <c>parameterName</c>/<c>parameterIndex</c> access is an explicit interface implementation over one physical slot
/// each. Only the face the predicate's <c>Kind</c> selects is ever used — <c>getTypePredicateOfSignature</c>
/// (checker.ts:16424) and the readers in <c>typePredicateToString</c>/<c>signatureToString</c> switch on <c>kind</c>
/// first — and for the <c>This</c> kinds the slot holds <c>null</c>, which is TS's <c>undefined</c>.
/// </para>
/// <para>
/// PORT-NOTE: the generated <c>Signature.ResolvedTypePredicate</c> and the five <c>ITypeChecker</c> members that take a
/// predicate are <c>ITypePredicateBase?</c> (<c>tools/Gen/TsTypeMapper.cs</c> maps TS's <c>TypePredicate</c> alias onto that
/// interface). This class is the only implementation, so it satisfies those slots directly; callers cast the slot to the
/// variant face the kind selects, exactly where TS's own code narrowed its discriminated union.
/// </para>
/// </remarks>
internal sealed class TypePredicate : ITypePredicateBase, IIdentifierTypePredicate, IAssertsIdentifierTypePredicate, IThisTypePredicate, IAssertsThisTypePredicate
{
    /// <summary>PORT-NOTE: the single physical <c>parameterName</c> slot; see the class remarks.</summary>
    private object? _parameterName;

    /// <summary>PORT-NOTE: the single physical <c>parameterIndex</c> slot; see the class remarks.</summary>
    private object? _parameterIndex;

    /// <summary>ported from: src/compiler/checker.ts:16121-16123 — <c>createTypePredicate</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's factory takes <c>parameterIndex: number | undefined</c> and stores it as-is; the port's
    /// constructor does the same, and the <c>Identifier</c> faces unbox it (they are only reachable when the kind says
    /// a number was supplied — TS's four call sites pass a number exactly for those two kinds).
    /// </remarks>
    public TypePredicate(TypePredicateKind kind, string? parameterName, int? parameterIndex, IType? type)
    {
        Kind = kind;
        Type = type;
        _parameterName = parameterName;
        _parameterIndex = parameterIndex;
    }

    /// <summary>ported from: src/compiler/types.ts:5691 — <c>TypePredicate.kind</c>.</summary>
    public TypePredicateKind Kind { get; set; }

    /// <summary>ported from: src/compiler/types.ts:5692 — <c>TypePredicate.type</c>.</summary>
    public IType? Type { get; set; }

    /// <summary>ported from: src/compiler/types.ts:5704 — <c>IdentifierTypePredicate.parameterName</c>.</summary>
    string? IIdentifierTypePredicate.ParameterName
    {
        get => (string?)_parameterName;
        set => _parameterName = value;
    }

    /// <summary>ported from: src/compiler/types.ts:5705 — <c>IdentifierTypePredicate.parameterIndex</c>.</summary>
    int IIdentifierTypePredicate.ParameterIndex
    {
        get => (int)_parameterIndex!;
        set => _parameterIndex = value;
    }

    /// <summary>ported from: src/compiler/types.ts:5718 — <c>AssertsIdentifierTypePredicate.parameterName</c>.</summary>
    string? IAssertsIdentifierTypePredicate.ParameterName
    {
        get => (string?)_parameterName;
        set => _parameterName = value;
    }

    /// <summary>ported from: src/compiler/types.ts:5719 — <c>AssertsIdentifierTypePredicate.parameterIndex</c>.</summary>
    int IAssertsIdentifierTypePredicate.ParameterIndex
    {
        get => (int)_parameterIndex!;
        set => _parameterIndex = value;
    }

    /// <summary>ported from: src/compiler/types.ts:5697 — <c>ThisTypePredicate.parameterName</c>.</summary>
    JsUndefined? IThisTypePredicate.ParameterName
    {
        get => (JsUndefined?)_parameterName;
        set => _parameterName = value;
    }

    /// <summary>ported from: src/compiler/types.ts:5698 — <c>ThisTypePredicate.parameterIndex</c>.</summary>
    JsUndefined? IThisTypePredicate.ParameterIndex
    {
        get => (JsUndefined?)_parameterIndex;
        set => _parameterIndex = value;
    }

    /// <summary>ported from: src/compiler/types.ts:5711 — <c>AssertsThisTypePredicate.parameterName</c>.</summary>
    JsUndefined? IAssertsThisTypePredicate.ParameterName
    {
        get => (JsUndefined?)_parameterName;
        set => _parameterName = value;
    }

    /// <summary>ported from: src/compiler/types.ts:5712 — <c>AssertsThisTypePredicate.parameterIndex</c>.</summary>
    JsUndefined? IAssertsThisTypePredicate.ParameterIndex
    {
        get => (JsUndefined?)_parameterIndex;
        set => _parameterIndex = value;
    }
}
