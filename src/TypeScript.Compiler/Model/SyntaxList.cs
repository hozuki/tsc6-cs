namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:10097-10105 — interface SyntaxList
/// <summary>
/// The hand-written part of <see cref="SyntaxList"/>.
/// </summary>
/// <remarks>
/// PORT-NOTE: the generator drops every member whose name starts with an underscore, because in <c>types.ts</c> those
/// are almost all structural brands (`_declarationBrand: undefined`) that exist only to make a type nominal.
/// <c>SyntaxList._children</c> is the one exception: it is real state whose whole purpose is to hold the list's
/// children, and <c>nodeChildren.ts</c> stores them here rather than in the per-source-file table precisely because a
/// syntax list exists only to hold them. It is supplied here instead of teaching the generator a special case.
/// <para>
/// The generated part pins <c>Kind</c> to <c>SyntaxKind.SyntaxList</c>; this part adds the field.
/// </para>
/// </remarks>
internal sealed partial class SyntaxList
{
    /// <summary>The children, stored on the node rather than in the <c>NodeChildren</c> table.</summary>
    public List<Node>? _children { get; set; }
}
