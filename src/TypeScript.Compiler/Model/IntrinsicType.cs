namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:6459-6463 (IntrinsicType), 6466-6468 (NullableType), 6470-6473 (FreshableType),
//             6476-6477 (FreshableIntrinsicType)
// ported from: src/compiler/checker.ts:5535-5545 (createIntrinsicType)
/// <summary>
/// A built-in non-object type — TS's <c>IntrinsicType</c> (<c>any</c>, <c>unknown</c>, <c>string</c>, <c>never</c>, …).
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS has one runtime <c>Type</c> object here too; the port's <c>createIntrinsicType</c> (M6-2) will cast
/// <c>createType(kind)</c> to this class, which is what the <c>TypeFlags → class</c> dispatch table has to produce for
/// every <c>TypeFlags.Intrinsic</c> kind (types.ts:6380).
/// </para>
/// <para>
/// PORT-NOTE: <c>NullableType</c> (types.ts:6466) only re-declares <c>objectFlags</c>, which
/// <c>IntrinsicType</c> already has, so one class satisfies it — TS's <c>getNullableType</c> (checker.ts:25791) does
/// nothing more than set that flag on an intrinsic type. <c>FreshableIntrinsicType</c> (types.ts:6476) is empty for
/// the same reason: <c>FreshableType</c>'s two slots are declared here.
/// </para>
/// </remarks>
internal partial class IntrinsicType : Type, IIntrinsicType, INullableType, IFreshableType, IFreshableIntrinsicType
{
    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — TS's one type constructor, forwarded so that the
    /// <c>TypeFlags → class</c> dispatch (M6-2) can construct this class.</summary>
    public IntrinsicType(ITypeChecker checker, TypeFlags flags) : base(checker, flags)
    {
    }

    /// <summary>ported from: src/compiler/types.ts:6460 — <c>IntrinsicType.intrinsicName</c>.</summary>
    public string? IntrinsicName { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6461 — <c>IntrinsicType.debugIntrinsicName</c>.</summary>
    public string? DebugIntrinsicName { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6462 — <c>IntrinsicType.objectFlags</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: declared once for <c>IntrinsicType.objectFlags</c> and <c>NullableType.objectFlags</c>
    /// (types.ts:6467), which TS declares twice for the same property.
    /// </remarks>
    public ObjectFlags ObjectFlags { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6471 — <c>FreshableType.freshType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: only the freshable intrinsics have these — the boolean literals, e.g. <c>falseType</c>/
    /// <c>regularFalseType</c> (checker.ts:2097-2100) — but the slot lives on the one intrinsic class; TS answers
    /// <c>undefined</c> for the rest.
    /// </remarks>
    public IFreshableType? FreshType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6472 — <c>FreshableType.regularType</c>.</summary>
    public IFreshableType? RegularType { get; set; }
}

/// <summary>
/// The intrinsic type of the <c>false</c>/<c>true</c> literal kinds — TS's <c>TypeFlags.BooleanLiteral</c> shape.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS has no <c>BooleanLiteralType</c> declaration; the four boolean literal types are built by
/// <c>createIntrinsicType(TypeFlags.BooleanLiteral, "false" | "true", …) as FreshableIntrinsicType</c>
/// (checker.ts:2097-2100). The port gives the kind its own class so the <c>TypeFlags → class</c> dispatch has a name
/// for it; everything it exposes comes from <see cref="IntrinsicType"/>, so it adds the name only. Nothing in the
/// checker casts to it (there is no <c>as BooleanLiteralType</c> upstream) — the casts upstream are to
/// <c>FreshableIntrinsicType</c>/<c>IntrinsicType</c>, both of which the base implements.
/// </para>
/// <para>
/// PORT-NOTE: TS's <c>EnumLiteralType</c> has no class here for the opposite reason: an enum literal type is created
/// with <c>TypeFlags.EnumLiteral | TypeFlags.StringLiteral</c> or <c>| TypeFlags.NumberLiteral</c>
/// (<c>getEnumLiteralType</c>, checker.ts:20324), and the checker then reads it through
/// <c>StringLiteralType</c>/<c>NumberLiteralType</c>/<c>BigIntLiteralType</c> depending on that bit
/// (<c>getTypeFactsWorker</c>, checker.ts:28159); one class cannot
/// be both, so the factory dispatches those flags to the literal classes.
/// </para>
/// </remarks>
internal partial class BooleanLiteralType : IntrinsicType
{
    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — TS's one type constructor, forwarded so that the
    /// <c>TypeFlags → class</c> dispatch (M6-2) can construct this class.</summary>
    public BooleanLiteralType(ITypeChecker checker, TypeFlags flags) : base(checker, flags)
    {
    }

}
