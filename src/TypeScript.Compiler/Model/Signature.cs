namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:7019-7058 (Signature)
// ported from: src/compiler/utilities.ts:8497-8503 (the Signature constructor)
/// <summary>
/// One signature the checker resolves for a callable declaration — TS's <c>Signature</c> class (M6-0).
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: like <see cref="Type"/>, TS declares <c>Signature</c> as an interface plus a merged constructor
/// function (<c>utilities.ts:8497</c>) that the checker reaches through
/// <c>objectAllocator.getSignatureConstructor()</c>. The port has no <c>objectAllocator</c> (§15.2), so this class is
/// both halves: <see cref="ISignature"/> supplies the slots, this class the constructor.
/// </para>
/// <para>
/// PORT-NOTE: <c>createSignature</c> (checker.ts:14124-14149) sets every slot explicitly right after construction;
/// the constructor itself only sets <c>flags</c> and (while debugging) <c>checker</c>. The port writes the same thing in
/// the same place — the checker's <c>CreateSignature</c> — rather than moving the initializers into this constructor.
/// </para>
/// <para>
/// PORT-NOTE: <c>mapper</c> and <c>optionalCallSignatureCache</c> (TS's <c>{ inner?: Signature, outer?: Signature }</c>)
/// have no C# counterpart in the generated model, so those slots stay <c>object?</c> and the checker casts exactly where
/// TS's structural typing supplied the type for free (§5 rule 2). <c>TypeMapper</c> is a function-union alias, hence
/// <c>object?</c> on every slot that holds one. <c>resolvedTypePredicate</c> is the one that no longer needs that
/// treatment: it is <c>ITypePredicateBase?</c>, implemented by <see cref="TypePredicate"/>.
/// </para>
/// </remarks>
internal partial class Signature : ISignature
{
    /// <summary>ported from: src/compiler/utilities.ts:8497-8503 — the <c>Signature</c> constructor.</summary>
    /// <remarks>
    /// PORT-NOTE: unlike <see cref="Type"/>'s constructor, this one assigns <c>checker</c> only under
    /// <c>Debug.isDebugging</c> (<c>utilities.ts:8500</c>), without consulting tracing.
    /// </remarks>
    public Signature(ITypeChecker checker, SignatureFlags flags)
    {
        Flags = flags;
        if (Debug.IsDebugging)
        {
            Checker = checker;
        }
    }

    /// <summary>ported from: src/compiler/types.ts:7020 — <c>Signature.flags</c>.</summary>
    public SignatureFlags Flags { get; set; }

    /// <summary>ported from: src/compiler/types.ts:7021 — <c>Signature.checker</c>.</summary>
    public ITypeChecker? Checker { get; set; }

    /// <summary>ported from: src/compiler/types.ts:7022 — <c>Signature.declaration</c>.</summary>
    public Node? Declaration { get; set; }

    /// <summary>ported from: src/compiler/types.ts:7023 — <c>Signature.typeParameters</c>.</summary>
    public List<ITypeParameter>? TypeParameters { get; set; }

    /// <summary>ported from: src/compiler/types.ts:7024 — <c>Signature.parameters</c>.</summary>
    public List<ISymbol>? Parameters { get; set; }

    /// <summary>ported from: src/compiler/types.ts:7025 — <c>Signature.thisParameter</c>.</summary>
    public ISymbol? ThisParameter { get; set; }

    /// <summary>ported from: src/compiler/types.ts:7028 — <c>Signature.resolvedReturnType</c>.</summary>
    public IType? ResolvedReturnType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:7033 — <c>Signature.resolvedTypePredicate</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types this <c>TypePredicate</c> — the union of the four <c>*TypePredicate</c> interfaces
    /// (types.ts:5723). The generated slot used to collapse the union to <c>Node?</c> (§12.4); <c>tools/Gen</c> now maps
    /// every member of the union through <see cref="TypePredicate"/>, the one class that implements all four
    /// variants, and the slot is that interface. <c>getTypePredicateOfSignature</c> (checker.ts:16424) writes it and
    /// <c>noTypePredicate</c> (checker.ts:2175) is stored here too — an ordinary <c>Identifier</c> predicate — which is
    /// why <c>null</c> means "not yet computed" while the sentinel object means "there is no predicate".
    /// </remarks>
    public ITypePredicateBase? ResolvedTypePredicate { get; set; }

    /// <summary>ported from: src/compiler/types.ts:7035 — <c>Signature.minArgumentCount</c>.</summary>
    public int MinArgumentCount { get; set; }

    /// <summary>ported from: src/compiler/types.ts:7037 — <c>Signature.resolvedMinArgumentCount</c>.</summary>
    public int? ResolvedMinArgumentCount { get; set; }

    /// <summary>ported from: src/compiler/types.ts:7039 — <c>Signature.target</c>.</summary>
    public ISignature? Target { get; set; }

    /// <summary>ported from: src/compiler/types.ts:7041 — <c>Signature.mapper</c>.</summary>
    public object? Mapper { get; set; }

    /// <summary>ported from: src/compiler/types.ts:7043 — <c>Signature.compositeSignatures</c>.</summary>
    public List<ISignature>? CompositeSignatures { get; set; }

    /// <summary>ported from: src/compiler/types.ts:7045 — <c>Signature.compositeKind</c>.</summary>
    public TypeFlags? CompositeKind { get; set; }

    /// <summary>ported from: src/compiler/types.ts:7047 — <c>Signature.erasedSignatureCache</c>.</summary>
    public ISignature? ErasedSignatureCache { get; set; }

    /// <summary>ported from: src/compiler/types.ts:7049 — <c>Signature.canonicalSignatureCache</c>.</summary>
    public ISignature? CanonicalSignatureCache { get; set; }

    /// <summary>ported from: src/compiler/types.ts:7051 — <c>Signature.baseSignatureCache</c>.</summary>
    public ISignature? BaseSignatureCache { get; set; }

    /// <summary>ported from: src/compiler/types.ts:7053 — <c>Signature.optionalCallSignatureCache</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's anonymous object type <c>{ inner?: Signature, outer?: Signature }</c> has no C# counterpart in
    /// the generated model, so the slot stays <c>object?</c>. <c>getOptionalCallSignature</c> (checker.ts:14182-14193)
    /// keys it with the strings <c>"inner"</c>/<c>"outer"</c>, so the value the checker stores is a
    /// <c>Dictionary&lt;string, ISignature&gt;</c> and each read casts to it at the use site.
    /// </remarks>
    public object? OptionalCallSignatureCache { get; set; }

    /// <summary>ported from: src/compiler/types.ts:7055 — <c>Signature.isolatedSignatureType</c>.</summary>
    public IObjectType? IsolatedSignatureType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:7057 — <c>Signature.instantiations</c>.</summary>
    public Dictionary<string, ISignature>? Instantiations { get; set; }
}
