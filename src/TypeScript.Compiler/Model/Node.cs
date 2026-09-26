namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:942-955 (interface Node)
/// <summary>
/// The base of every syntax node.
/// </summary>
/// <remarks>
/// Hand-written rather than generated: it is the root of the class hierarchy the generator emits into, and
/// <see cref="Kind"/> is abstract here so each concrete node can pin it with an expression-bodied override
/// (<c>public override SyntaxKind Kind =&gt; SyntaxKind.Identifier;</c>) — which is also what lets the
/// emitting code switch on a node's kind without a null check.
/// <para>
/// Members that TS distributes across capability interfaces (<c>Declaration.symbol</c>,
/// <c>LocalsContainer.locals</c>, <c>FlowContainer.flowNode</c>, <c>JSDocContainer.jsDoc</c>) are *not*
/// hoisted here: porting decision D1 keeps them on the interfaces so a node only carries what TS says it
/// carries. That costs a repeated field per implementing class, which the generator emits for free.
/// </para>
/// </remarks>
internal abstract class Node : IReadonlyTextRange
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
