namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:13826-13831 (isNonBindableDynamicName)
/// <summary>
/// The remainder of the checker's late-binding block (checker.ts:13778-14053) that no other landed slice declares:
/// <c>isNonBindableDynamicName</c>, the predicate for a dynamic member name that can never be late-bound.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: the other fourteen members of that block are already declared — each with the reference's own line
/// range on its summary — by <c>Checker/Checker.TypeSupport.cs</c> (M6Generics' type-support slice), which owns their
/// definition sites: <c>IsLateBindableName</c>, <c>IsLateBindableIndexSignature</c>, <c>IsLateBindableAST</c>,
/// <c>IsTypeUsableAsIndexSignature</c>, <c>IsLateBoundName</c>, <c>HasLateBindableName</c>,
/// <c>HasLateBindableIndexSignature</c>, <c>HasBindableName</c>, <c>AddDeclarationToLateBoundSymbol</c>,
/// <c>LateBindMember</c>, <c>LateBindIndexSignature</c>, <c>GetResolvedMembersOrExportsOfSymbol</c>,
/// <c>GetMembersOfSymbol</c> and <c>GetLateBoundSymbol</c>. Declaring any of them a second time would be CS0111
/// (duplicate member of the partial class), so this file calls them unqualified instead.
/// </para>
/// <para>
/// PORT-NOTE: the block's <c>MembersOrExportsResolutionKind</c> (checker.ts:1397-1400) is already declared by
/// <c>Checker/CheckerEnums.cs</c>; it is not repeated here either.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    /// <summary>ported from: src/compiler/checker.ts:13826-13831 — <c>isNonBindableDynamicName</c>.</summary>
    /// <remarks>
    /// Indicates whether a declaration name is a dynamic name that cannot be late-bound.
    /// PORT-NOTE: TS's <c>DeclarationName</c> union has no C# anchor, so the parameter is <c>Node</c> — the same choice
    /// <c>Utilities.IsDynamicName</c> and the sibling <c>Checker/Checker.TypeSupport.cs</c> predicates make; the body
    /// is the reference's <c>isDynamicName(node) &amp;&amp; !isLateBindableName(node)</c>, unchanged. Its two call sites,
    /// checker.ts:47329 and checker.ts:52974, are not ported yet.
    /// </remarks>
    internal bool IsNonBindableDynamicName(Node node)
        => Utilities.IsDynamicName(node) && !IsLateBindableName(node);
}
