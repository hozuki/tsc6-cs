namespace TypeScript.Compiler;

// ported from: src/compiler/binder.ts:3874-3877 — the JSDocImportTag arm of getContainerFlags
/// <summary>
/// The hand-written part of <see cref="JSDocImportTag"/>: the <c>locals</c>/<c>nextContainer</c> slots the binder
/// attaches to the tag when it treats it as a container.
/// </summary>
/// <remarks>
/// PORT-NOTE: <c>types.ts:4139-4145</c> declares <c>JSDocImportTag</c> as a bare <c>JSDocTag</c> — it is not a member of
/// the <c>HasLocals</c> union (types.ts:1519-1549) — yet the binder's own <c>getContainerFlags</c> classifies it
/// <c>IsContainer | IsControlFlowContainer | HasLocals | PropagatesThisKeyword</c> ("treat as a container to prevent
/// using an enclosing effective host, ensuring import bindings are scoped correctly", binder.ts:3874-3877), and
/// <c>bindContainer</c> then writes <c>(container as HasLocals).locals = createSymbolTable()</c> (binder.ts:1003-1007)
/// through an unchecked cast: upstream the property is created on the node object at run time. C# has no dynamic
/// properties, so the two slots are declared here and the generated declaration is completed with
/// <see cref="ILocalsContainer"/> — the hand-written-partial mechanism docs/architecture.md §9.8 records, with
/// <c>Model/SyntaxList.cs</c> as its precedent. Without them the binder throws on any <c>@import</c> JSDoc tag
/// (<c>bindContainer</c>'s <c>(ILocalsContainer)node</c>) instead of scoping the tag's binding.
/// </remarks>
internal partial class JSDocImportTag : ILocalsContainer
{
    /// <summary>ported from: src/compiler/binder.ts:1005 — the locals table <c>bindContainer</c> creates for the tag.</summary>
    public SymbolTable? Locals { get; set; }

    /// <summary>ported from: src/compiler/binder.ts:1006 — the container-chain link.</summary>
    public Node? NextContainer { get; set; }
}
