namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:6505-6506 (EnumType), 6470-6473 (FreshableType)
// ported from: src/compiler/checker.ts:13579-13587 (createComputedEnumType)
/// <summary>
/// The type of an enum without a literal value — TS's <c>EnumType</c>.
/// </summary>
/// <remarks>
/// PORT-NOTE: created in pairs by <c>createComputedEnumType</c> (checker.ts:13579-13587) with
/// <c>createTypeWithSymbol(TypeFlags.Enum, symbol) as EnumType</c>; the regular and fresh members point at each other
/// through <see cref="RegularType"/>/<see cref="FreshType"/>, exactly as upstream, and the function returns the regular
/// one. <c>getDeclaredTypeOfEnum</c> (checker.ts:13544-13577) is the other writer — for an enum whose members are all
/// literals it takes the union path and flips <c>TypeFlags.EnumLiteral</c> on that union instead, which is why this
/// class deliberately does not derive from <see cref="UnionOrIntersectionType"/>.
/// </remarks>
internal partial class EnumType : Type, IEnumType, IFreshableType
{
    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — TS's one type constructor, forwarded so that the
    /// <c>TypeFlags → class</c> dispatch (M6-2) can construct this class.</summary>
    public EnumType(object? checker, TypeFlags flags) : base(checker, flags)
    {
    }

    /// <summary>ported from: src/compiler/types.ts:6471 — <c>FreshableType.freshType</c>.</summary>
    public IFreshableType? FreshType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6472 — <c>FreshableType.regularType</c>.</summary>
    public IFreshableType? RegularType { get; set; }
}
