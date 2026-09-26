using System.Runtime.CompilerServices;

namespace TypeScript.Compiler;

// ported from: src/compiler/factory/nodeChildren.ts
/// <summary>
/// The node → children table used by incremental parsing.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS keys two nested <c>WeakMap</c>s (<c>SourceFileLike</c> → node → children) so that neither the
/// source file nor the nodes it maps are kept alive by the table. <see cref="ConditionalWeakTable{TKey,TValue}"/>
/// is the .NET counterpart for the outer level; the inner <c>Dictionary</c> is held by the entry, so it is collected
/// with the key.
/// <para>
/// PORT-NOTE: <c>SyntaxList</c> does not go through the table at all — its children are stored directly on the node
/// (<c>_children</c>), because a syntax list exists only to hold them. TS relies on the same split; the asserts on
/// the list path are kept.
/// </para>
/// <para>
/// PORT-NOTE: TS types the key as <c>SourceFileLike</c> (a structural view of a source file used by incremental
/// parsing). The port takes <see cref="SourceFile"/>: the structural interface would require every member's
/// nullability to line up exactly, and nothing below Layer 3 passes anything else yet.
/// </para>
/// </remarks>
internal static class NodeChildren
{
    private static readonly ConditionalWeakTable<SourceFile, Dictionary<Node, IList<Node>>> s_sourceFileToNodeChildren = new();

    /// <summary>TS returns a shared <c>emptyArray</c> for kinds that cannot have children.</summary>
    private static readonly List<Node> s_emptyArray = [];

    /// <summary>The children of a node, or null when they have not been computed (an empty list if it cannot have any).</summary>
    public static IList<Node>? GetNodeChildren(Node node, SourceFile sourceFile)
    {
        var kind = node.Kind;
        if (!IsNodeKind(kind))
        {
            return s_emptyArray;
        }

        if (kind == SyntaxKind.SyntaxList)
        {
            return ((SyntaxList)node)._children;
        }

        return s_sourceFileToNodeChildren.TryGetValue(sourceFile, out var map) && map.TryGetValue(node, out var children)
            ? children
            : null;
    }

    /// <summary>Records the children computed for a node.</summary>
    public static IList<Node> SetNodeChildren(Node node, SourceFile sourceFile, IList<Node> children)
    {
        // SyntaxList children are always created eagerly while computing the parent's children, so they should never
        // be set here.
        Debug.Assert(node.Kind != SyntaxKind.SyntaxList, "Should not need to re-set the children of a SyntaxList.");

        var map = s_sourceFileToNodeChildren.GetOrCreateValue(sourceFile);
        map[node] = children;
        return children;
    }

    /// <summary>Forgets the children computed for a node.</summary>
    public static void UnsetNodeChildren(Node node, SourceFile origSourceFile)
    {
        // Syntax lists are synthesized and hold their children directly; incremental parsing discards them whole
        // when a change intersects their containing parents.
        Debug.Assert(node.Kind != SyntaxKind.SyntaxList, "Did not expect to unset the children of a SyntaxList.");

        if (s_sourceFileToNodeChildren.TryGetValue(origSourceFile, out var map))
        {
            map.Remove(node);
        }
    }

    /// <summary>Moves the whole table to a new source file (incremental reuse).</summary>
    public static void TransferSourceFileChildren(SourceFile sourceFile, SourceFile targetSourceFile)
    {
        if (s_sourceFileToNodeChildren.TryGetValue(sourceFile, out var map))
        {
            s_sourceFileToNodeChildren.Remove(sourceFile);
            s_sourceFileToNodeChildren.Add(targetSourceFile, map);
        }
    }

    // ported from: src/compiler/utilitiesPublic.ts:1462-1464
    /// <summary>
    /// Whether a kind can have children at all. TS tests <c>kind &gt;= SyntaxKind.FirstNode</c>; unlike the upper
    /// bounds used elsewhere in the port, this one is open-ended on purpose — new node kinds are always appended.
    /// </summary>
    public static bool IsNodeKind(SyntaxKind kind) => kind >= SyntaxKind.FirstNode;
}
