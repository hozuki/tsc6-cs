namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:962-965 — interface JSDocArray
/// <summary>
/// The JSDoc comments that directly precede a node, plus the cache the JSDoc lookups fill in.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS declares <c>interface JSDocArray extends Array&lt;JSDoc&gt;</c>, so the JSDoc list is a plain array with
/// one extra member. C# has no way to add a member to <see cref="List{T}"/>, so the concrete type derives from
/// <see cref="NodeArray{T}"/> — the same treatment <c>NodeArray</c> itself got (§10.2) — and implements the generated
/// <see cref="IJSDocArray"/> for the extra slot. It is handed out through <c>IJSDocArray?</c> slots
/// (<c>JSDocContainer.jsDoc</c>), because those are generated from the interface; a caller that needs the list
/// behaviour casts to this type. Built by the parser (<c>withJSDoc</c>) and read by the JSDoc utilities.
/// </remarks>
internal sealed class JSDocArray : NodeArray<JSDoc>, IJSDocArray
{
    public JSDocArray()
    {
    }

    public JSDocArray(IEnumerable<JSDoc> items)
        : base(items)
    {
    }

    /// <summary>The tags of this comment as the last JSDoc lookup computed them; null until something asks.</summary>
    public List<JSDocTag>? JsDocCache { get; set; }
}
