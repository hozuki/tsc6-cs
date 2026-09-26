namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:942-955 (interface Node)
/// <summary>
/// The capability every node shares, for the places C# needs one type that both a node class and a
/// categorisation interface can name.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS's <c>Node</c> is an interface, so <c>TypeNode</c> and <c>Statement</c> — interfaces too — are
/// subtypes of it, and a signature can take <c>Node</c> where the caller holds a <c>TypeNode</c>. In the port
/// <c>Node</c> is a class and only the chain a class belongs to becomes one (D1), so the two sides share no type at
/// all: <c>CreateNodeArray&lt;T&gt;</c> could not take a <c>List&lt;ITypeNode&gt;</c>, and neither could
/// <c>PropagateChildFlags</c>. The interfaces on a node chain therefore declare <c>INode</c> as a base (emitted by
/// <c>tools/Gen</c>, see NodeEmitter), and this is the declaration they meet. See docs/architecture.md §12.9.
/// </remarks>
internal interface INode
{
    /// <summary>Which syntactic form this node is (TS: <c>readonly kind: SyntaxKind</c>).</summary>
    SyntaxKind Kind { get; }

    // ported from: types.ts:944 — `readonly flags: NodeFlags`
    NodeFlags Flags { get; set; }

    // ported from: types.ts:946 — `readonly transformFlags: TransformFlags`
    TransformFlags TransformFlags { get; set; }
}

/// <summary>
/// The base of every syntax node.
/// </summary>
/// <remarks>
/// Hand-written rather than generated: it is the root of the class hierarchy the generator emits into, and
/// <see cref="Kind"/> is abstract here so each concrete node can pin it with an expression-bodied override
/// (<c>public override SyntaxKind Kind =&gt; SyntaxKind.Identifier;</c>) — which is also what lets the
/// emitting code switch on a node's kind without a null check.
/// <para>
/// PORT-NOTE: TS declares <c>Node extends ReadonlyTextRange</c> and relies on structural typing where a
/// <c>TextRange</c> is wanted (<c>getSourceMapRange</c> falls back to returning the node itself). C# has no such
/// compatibility, so the class implements both — <see cref="ITextRange"/> carries no members of its own under D1,
/// and <c>Pos</c>/<c>End</c> are writable here anyway.
/// </para>
/// <para>
/// Members that TS distributes across capability interfaces (<c>Declaration.symbol</c>,
/// <c>LocalsContainer.locals</c>, <c>FlowContainer.flowNode</c>, <c>JSDocContainer.jsDoc</c>) are *not*
/// hoisted here: porting decision D1 keeps them on the interfaces so a node only carries what TS says it
/// carries. That costs a repeated field per implementing class, which the generator emits for free.
/// </para>
/// </remarks>
internal abstract class Node : INode, IReadonlyTextRange, ITextRange
{
    /// <summary>Which syntactic form this node is. Pinned by every concrete node.</summary>
    public abstract SyntaxKind Kind { get; }

    // ported from: types.ts:944 — `readonly flags: NodeFlags`
    public NodeFlags Flags { get; set; }

    // ported from: types.ts:945 — `modifierFlagsCache: ModifierFlags`
    public ModifierFlags ModifierFlagsCache { get; set; }

    // ported from: types.ts:946 — `readonly transformFlags: TransformFlags`
    public TransformFlags TransformFlags { get; set; }

    /// <summary>
    /// Unique id, used to look up <c>NodeLinks</c>. Assigned lazily by the checker, so it stays null until
    /// something asks for it — the same laziness TS encodes with <c>id?: NodeId</c>.
    /// </summary>
    public int? Id { get; set; }

    /// <summary>Parent node. Initialized by binding, so it is null until <c>bindSourceFile</c> runs.</summary>
    public Node? Parent { get; set; }

    /// <summary>The original node if this is an updated node (transforms produce copies).</summary>
    public Node? Original { get; set; }

    /// <summary>Associated <see cref="IEmitNode"/>, initialized by transforms.</summary>
    public IEmitNode? EmitNode { get; set; }

    // ported from: types.ts:33-36 (ReadonlyTextRange)
    public int Pos { get; set; }

    public int End { get; set; }

    public override string ToString() => $"{Kind}";
}
