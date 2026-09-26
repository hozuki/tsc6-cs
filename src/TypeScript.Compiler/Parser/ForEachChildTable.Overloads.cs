namespace TypeScript.Compiler;

// ported from: src/compiler/parser.ts:1249-1255 (the callback shape of forEachChild)
/// <summary>
/// The void-callback form of <c>forEachChild</c>.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS's <c>forEachChild</c> is one member whose callbacks may return anything; C# needs a second
/// overload, because a lambda that returns nothing is an <c>Action</c> and not a <c>Func</c>. The overload
/// forwards to the generated table's generic visitor, so nothing about the kind dispatch is duplicated
/// (§9 rule 8: the hand-written half of a generated type lives in its own, differently-named file).
/// </remarks>
internal static partial class ForEachChildTable
{
    /// <summary>ported from: src/compiler/parser.ts:1249-1255 — <c>forEachChild</c>, void-callback shape.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's callback answers <c>undefined</c> to keep walking and a truthy value to stop; a void
    /// callback has no way to stop, so the adapters answer <c>null</c> for every child — which is the same
    /// "continue" the generic visitor's truthiness test reads. The binder's second call site is exactly this
    /// shape (<c>binder.ts:1093</c> passes <c>bind</c> and <c>bindEach</c>).
    /// </remarks>
    public static void ForEachChild(Node node, Action<Node> cbNode, Action<INodeArray<INode>>? cbNodes = null)
    {
        if (cbNodes is null)
        {
            ForEachChild<object>(
                node,
                child =>
                {
                    cbNode(child);
                    return null;
                });
            return;
        }

        // The guard above is what makes the capture safe: the lambda is created only when cbNodes is non-null, and a
        // parameter cannot be reassigned by the callee's walk.
        var nodesCallback = cbNodes;
        ForEachChild<object>(
            node,
            child =>
            {
                cbNode(child);
                return null;
            },
            nodes =>
            {
                nodesCallback!(nodes);
                return null;
            });
    }
}
