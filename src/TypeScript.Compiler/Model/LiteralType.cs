namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:6482-6484 (LiteralType), 6492-6494 (StringLiteralType), 6496-6498 (NumberLiteralType),
//             6500-6502 (BigIntLiteralType)
// ported from: src/compiler/checker.ts:20276-20281 (createLiteralType)
/// <summary>
/// A string/number/bigint literal type — TS's <c>LiteralType</c>.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS builds all of them through one function, <c>createLiteralType(flags, value, symbol?, regularType?)</c>
/// (checker.ts:20276-20281), which casts the fresh <c>Type</c> to <c>LiteralType</c>. The port's factory dispatches on
/// the literal bit of <c>flags</c> to <see cref="StringLiteralType"/>/<see cref="NumberLiteralType"/>/
/// <see cref="BigIntLiteralType"/> instead, because the checker reads the value back through one of those three faces
/// depending on the same bit (<c>getTemplateStringForType</c>, checker.ts:19101-19107; <c>getTypeFactsWorker</c>,
/// checker.ts:28159) and one C# class cannot be all three. The three classes derive from this one, so the
/// <c>as LiteralType</c> casts the checker also makes (e.g. checker.ts:20286-20288, :20296) are upcasts.
/// </para>
/// <para>
/// PORT-NOTE: the one slot the generated model cannot type precisely is <c>value</c>, whose union
/// <c>string | number | PseudoBigInt</c> the generator widened to <c>object?</c> (types.ts:6483). Each read casts to
/// the member the flags say it is — TS's structural typing did the same at run time, where the slot simply held one
/// of the three.
/// </para>
/// </remarks>
internal partial class LiteralType : Type, ILiteralType, IFreshableType, IStringLiteralType, INumberLiteralType, IBigIntLiteralType
{
    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — TS's one type constructor, forwarded so that the
    /// <c>TypeFlags → class</c> dispatch (M6-2) can construct this class.</summary>
    public LiteralType(object? checker, TypeFlags flags) : base(checker, flags)
    {
    }

    /// <summary>ported from: src/compiler/types.ts:6483 — <c>LiteralType.value</c>.</summary>
    public object? Value { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6471 — <c>FreshableType.freshType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>getFreshTypeOfLiteralType</c> (checker.ts:20283-20293) sets both of the freshable slots lazily and
    /// the fresh type points at itself, exactly as the port's plain slots do.
    /// </remarks>
    public IFreshableType? FreshType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6472 — <c>FreshableType.regularType</c>.</summary>
    public IFreshableType? RegularType { get; set; }
}

/// <summary>ported from: src/compiler/types.ts:6492-6494 — <c>StringLiteralType</c>.</summary>
/// <remarks>
/// PORT-NOTE: <c>value: string</c> (types.ts:6493) narrows the base's union; C# cannot narrow a slot's type in a
/// derived class, so this class adds no member and the reader casts <see cref="LiteralType.Value"/> to
/// <c>string</c> where TS read it through this face.
/// </remarks>
internal partial class StringLiteralType : LiteralType
{
    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — TS's one type constructor, forwarded so that the
    /// <c>TypeFlags → class</c> dispatch (M6-2) can construct this class.</summary>
    public StringLiteralType(object? checker, TypeFlags flags) : base(checker, flags)
    {
    }

}

/// <summary>ported from: src/compiler/types.ts:6496-6498 — <c>NumberLiteralType</c>.</summary>
/// <remarks>
/// PORT-NOTE: as with <see cref="StringLiteralType"/>, the narrowing of <c>value</c> to <c>number</c> (types.ts:6497)
/// lives at the read site, not in the class hierarchy.
/// </remarks>
internal partial class NumberLiteralType : LiteralType
{
    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — TS's one type constructor, forwarded so that the
    /// <c>TypeFlags → class</c> dispatch (M6-2) can construct this class.</summary>
    public NumberLiteralType(object? checker, TypeFlags flags) : base(checker, flags)
    {
    }

}

/// <summary>ported from: src/compiler/types.ts:6500-6502 — <c>BigIntLiteralType</c>.</summary>
/// <remarks>
/// PORT-NOTE: as with <see cref="StringLiteralType"/>, the narrowing of <c>value</c> to <c>PseudoBigInt</c>
/// (types.ts:6501) lives at the read site. <c>isZeroBigInt</c> (checker.ts:25761) is one of them.
/// </remarks>
internal partial class BigIntLiteralType : LiteralType
{
    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — TS's one type constructor, forwarded so that the
    /// <c>TypeFlags → class</c> dispatch (M6-2) can construct this class.</summary>
    public BigIntLiteralType(object? checker, TypeFlags flags) : base(checker, flags)
    {
    }

}
