// `Flags` is the instance property below (TS's `factory.flags`), and it shadows the `Core/Flags.cs` helper class
// inside this type — the same collision the port accepted for the property name.
using CoreFlags = TypeScript.Compiler.Flags;

namespace TypeScript.Compiler;

// ported from: src/compiler/factory/nodeFactory.ts (createNodeFactory)
/// <summary>
/// The node factory: the single entry point for creating and updating nodes.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS builds its factory as an object literal returned from <c>createNodeFactory(flags, baseFactory)</c>
/// and takes the members from that closure, so a call site destructures them (<c>const { createIdentifier } =
/// factory</c>). C# has no free-standing functions over shared state, so the same surface is an instance class and
/// call sites reach it through the instance.
/// <para>
/// PORT-NOTE: <c>baseNodeFactory.ts</c> is not ported. It existed so every compiler instance could get its own set
/// of <c>Node</c> constructors — which is what makes <c>instanceof</c> correct across realms in TS. C# has one
/// <c>Node</c> type per assembly, so <c>new Identifier()</c> is already the right thing (see §15.2).
/// </para>
/// <para>
/// This file grows in batches: the infrastructure first, then the <c>create*</c>/<c>update*</c> families. It picks up
/// <c>INodeFactory</c> only once every member the generated interface declares is present — until then the compiler
/// cannot tell a missing method from an unimplemented one, so the batches are checked by their own tests.
/// </para>
/// </remarks>
internal partial class NodeFactory
{
    private readonly NodeFactoryFlags _flags;

    public NodeFactory(NodeFactoryFlags flags = NodeFactoryFlags.None) => _flags = flags;

    /// <summary>How this factory was configured (see <see cref="NodeFactoryFlags"/>).</summary>
    public NodeFactoryFlags Flags => _flags;

    //
    // Node arrays
    //

    /// <summary>ported from: factory/nodeFactory.ts:1169 — <c>createNodeArray</c>.</summary>
    /// <remarks>
    /// Two behaviours matter to callers and are covered by tests: an array that is already a
    /// <see cref="NodeArray{T}"/> with the same <paramref name="hasTrailingComma"/> comes back as the <em>same
    /// instance</em>, and every array that leaves here has its <c>TransformFlags</c> aggregated over its children.
    /// <para>
    /// PORT-NOTE: TS has two memory-layout tricks that have no C# counterpart. For one to four elements it calls
    /// <c>slice()</c> so the VM can pick a compact representation; for five or more it uses the caller's array
    /// <em>as</em> the <see cref="NodeArray{T}"/> — legal in JS, where properties can be hung on an array. A C#
    /// <see cref="List{T}"/> cannot become a <see cref="NodeArray{T}"/>, so this always allocates and copies.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS also treats <c>core.ts</c>'s shared <c>emptyArray</c> as "no elements" and attaches debug info
    /// to the result in debug builds (<c>Debug.attachNodeArrayDebugInfo</c>). Neither is ported: a C# call site has
    /// no shared empty array to compare against, and the debug info only decorates the object for inspection.
    /// </para>
    /// </remarks>
    public NodeArray<T> CreateNodeArray<T>(List<T>? elements, bool? hasTrailingComma)
        where T : Node
    {
        if (elements is null)
        {
            elements = [];
        }
        else if (elements is NodeArray<T> existing)
        {
            if (hasTrailingComma is null || existing.HasTrailingComma == hasTrailingComma.Value)
            {
                // Ensure the transform flags have been aggregated for this NodeArray.
                if (existing.TransformFlags == TransformFlags.None) AggregateChildrenFlags(existing);
                return existing;
            }

            // This *was* a NodeArray, but the hasTrailingComma option differs: recreate it with the same elements,
            // text range and transform flags, but the updated value.
            return new NodeArray<T>(existing)
            {
                Pos = existing.Pos,
                End = existing.End,
                HasTrailingComma = hasTrailingComma.Value,
                TransformFlags = existing.TransformFlags,
            };
        }

        var array = new NodeArray<T>(elements)
        {
            Pos = -1,
            End = -1,
            HasTrailingComma = hasTrailingComma ?? false,
        };

        AggregateChildrenFlags(array);
        return array;
    }

    /// <summary>ported from: factory/nodeFactory.ts:7137 — <c>asNodeArray</c>.</summary>
    private NodeArray<T>? AsNodeArray<T>(List<T>? array)
        where T : Node => array is null ? null : CreateNodeArray(array, null);

    //
    // Transform flags
    //

    /// <summary>ported from: factory/nodeFactory.ts:7309 — <c>aggregateChildrenFlags</c>.</summary>
    private static void AggregateChildrenFlags<T>(NodeArray<T> children)
        where T : Node
    {
        var subtreeFlags = TransformFlags.None;
        foreach (var child in children)
        {
            subtreeFlags |= PropagateChildFlags(child);
        }

        children.TransformFlags = subtreeFlags;
    }

    /// <summary>ported from: factory/nodeFactory.ts:7299 — <c>propagateChildFlags</c>.</summary>
    private static TransformFlags PropagateChildFlags(Node? child)
    {
        if (child is null) return TransformFlags.None;

        var childFlags = CoreFlags.Clear(child.TransformFlags, GetTransformFlagsSubtreeExclusions(child.Kind));

        // A property name contributes more than its own flags: a computed name makes the whole subtree "contains a
        // computed property name", which is what a property declaration's exclusions are about.
        return child is INamedDeclaration { Name: { } name } && Utilities.IsPropertyName(name)
            ? PropagatePropertyNameFlagsOfChild(name, childFlags)
            : childFlags;
    }

    /// <summary>ported from: factory/nodeFactory.ts:7295 — <c>propagatePropertyNameFlagsOfChild</c>.</summary>
    private static TransformFlags PropagatePropertyNameFlagsOfChild(Node node, TransformFlags transformFlags)
        => transformFlags | (node.TransformFlags & TransformFlags.PropertyNamePropagatingFlags);

    /// <summary>ported from: factory/nodeFactory.ts:7320 — <c>getTransformFlagsSubtreeExclusions</c>.</summary>
    /// <remarks>
    /// The flags returned here are the ones a child <em>stops</em> propagating: a nested function body, for instance,
    /// does not make the enclosing declaration contain a `yield`.
    /// </remarks>
    private static TransformFlags GetTransformFlagsSubtreeExclusions(SyntaxKind kind)
    {
        if (kind >= SyntaxKind.FirstTypeNode && kind <= SyntaxKind.LastTypeNode)
        {
            return TransformFlags.TypeExcludes;
        }

        return kind switch
        {
            SyntaxKind.CallExpression or SyntaxKind.NewExpression or SyntaxKind.ArrayLiteralExpression
                => TransformFlags.ArrayLiteralOrCallOrNewExcludes,
            SyntaxKind.ModuleDeclaration => TransformFlags.ModuleExcludes,
            SyntaxKind.Parameter => TransformFlags.ParameterExcludes,
            SyntaxKind.ArrowFunction => TransformFlags.ArrowFunctionExcludes,
            SyntaxKind.FunctionExpression or SyntaxKind.FunctionDeclaration => TransformFlags.FunctionExcludes,
            SyntaxKind.VariableDeclarationList => TransformFlags.VariableDeclarationListExcludes,
            SyntaxKind.ClassDeclaration or SyntaxKind.ClassExpression => TransformFlags.ClassExcludes,
            SyntaxKind.Constructor => TransformFlags.ConstructorExcludes,
            SyntaxKind.PropertyDeclaration => TransformFlags.PropertyExcludes,
            SyntaxKind.MethodDeclaration or SyntaxKind.GetAccessor or SyntaxKind.SetAccessor
                => TransformFlags.MethodOrAccessorExcludes,
            SyntaxKind.AnyKeyword or SyntaxKind.NumberKeyword or SyntaxKind.BigIntKeyword or SyntaxKind.NeverKeyword
                or SyntaxKind.StringKeyword or SyntaxKind.ObjectKeyword or SyntaxKind.BooleanKeyword
                or SyntaxKind.SymbolKeyword or SyntaxKind.VoidKeyword or SyntaxKind.TypeParameter
                or SyntaxKind.PropertySignature or SyntaxKind.MethodSignature or SyntaxKind.CallSignature
                or SyntaxKind.ConstructSignature or SyntaxKind.IndexSignature or SyntaxKind.InterfaceDeclaration
                or SyntaxKind.TypeAliasDeclaration
                => TransformFlags.TypeExcludes,
            SyntaxKind.ObjectLiteralExpression => TransformFlags.ObjectLiteralExcludes,
            SyntaxKind.CatchClause => TransformFlags.CatchClauseExcludes,
            SyntaxKind.ObjectBindingPattern or SyntaxKind.ArrayBindingPattern => TransformFlags.BindingPatternExcludes,
            SyntaxKind.TypeAssertionExpression or SyntaxKind.SatisfiesExpression or SyntaxKind.AsExpression
                or SyntaxKind.PartiallyEmittedExpression or SyntaxKind.ParenthesizedExpression or SyntaxKind.SuperKeyword
                => TransformFlags.OuterExpressionExcludes,
            SyntaxKind.PropertyAccessExpression or SyntaxKind.ElementAccessExpression
                => TransformFlags.PropertyAccessExcludes,
            _ => TransformFlags.NodeExcludes,
        };
    }
}
