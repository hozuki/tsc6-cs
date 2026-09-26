namespace TypeScript.Compiler;

// ported from: src/compiler/binder.ts:2751-2804 (bind)
// ported from: src/compiler/binder.ts:2806-2820 (bindJSDoc)
// ported from: src/compiler/binder.ts:2822-2844 (updateStrictModeStatementList, isUseStrictPrologueDirective)
/// <summary>
/// The per-node entry point of the walk and the two strict-mode helpers it feeds: TS's <c>bind</c>,
/// <c>bindJSDoc</c>, <c>updateStrictModeStatementList</c> and <c>isUseStrictPrologueDirective</c>.
/// </summary>
/// <remarks>
/// PORT-NOTE: <c>bind</c> calls <c>bindWorker</c> (binder.ts:2783), whose dispatch switch is the dispatch slice's
/// (M5-5); the call is written by the reference name.
/// <para>
/// PORT-NOTE: the <c>tracing</c> guard around <c>tracingPath</c> is kept as <c>Tracing.Current is not null</c>, the
/// shape the tracing slice documents for TS's <c>if (tracing)</c>.
/// </para>
/// <para>
/// PORT-NOTE: TS reaches <c>node.jsDoc</c> through a <c>JSDocContainer</c>-shaped cast and iterates it directly. The
/// port's <c>IJSDocContainer.JsDoc</c> is typed <see cref="IJSDocArray"/>, which carries only the lookup cache, so the
/// list behaviour comes from the concrete <see cref="JSDocArray"/> — the same treatment that type's own remarks
/// describe.
/// </para>
/// </remarks>
internal partial class Binder
{
    /// <summary>ported from: src/compiler/binder.ts:2751-2804 — <c>bind</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the loop over the node hierarchy is where the parenate pointers come from: <c>setParent(node, parent)</c>
    /// runs before anything reads the tree, which is why the port's <c>_parent</c> is a nullable slot.
    /// </remarks>
    internal void Bind(Node? node)
    {
        if (node is null)
        {
            return;
        }

        Utilities.SetParent(node, _parent);
        if (Tracing.Current is not null) node.TracingPath = _file.Path;
        var saveInStrictMode = _inStrictMode;

        // Even though in the AST the jsdoc @typedef node belongs to the current node,
        // its symbol might be in the same scope with the current node's symbol. Consider:
        //
        //     /** @typedef {string | number} MyType */
        //     function foo();
        //
        // Here the current node is "foo", which is a container, but the scope of "MyType" should
        // not be inside "foo". Therefore we always bind @typedef before bind the parent node,
        // and skip binding this tag later when binding all the other jsdoc tags.

        // First we bind declaration nodes to a symbol if possible. We'll both create a symbol
        // and then potentially add the symbol to an appropriate symbol table. Possible
        // destination symbol tables are:
        //
        //  1) The 'exports' table of the current container's symbol.
        //  2) The 'members' table of the current container's symbol.
        //  3) The 'locals' table of the current container.
        //
        // However, not all symbols will end up in any of these tables. 'Anonymous' symbols
        // (like TypeLiterals for example) will not be put in any table.
        BindWorker(node);
        // Then we recurse into the children of the node to bind them as well. For certain
        // symbols we do specialized work when we recurse. For example, we'll keep track of
        // the current 'container' node when it changes. This helps us know which symbol table
        // a local should go into for example. Since terminal nodes are known not to have
        // children, as an optimization we don't process those.
        if (node.Kind > SyntaxKind.LastToken)
        {
            var saveParent = _parent;
            _parent = node;
            var containerFlags = GetContainerFlags(node);
            if (containerFlags == ContainerFlags.None)
            {
                BindChildren(node);
            }
            else
            {
                BindContainer(node, containerFlags);
            }

            _parent = saveParent;
        }
        else
        {
            var saveParent = _parent;
            if (node.Kind == SyntaxKind.EndOfFileToken) _parent = node;
            BindJSDoc(node);
            _parent = saveParent;
        }

        _inStrictMode = saveInStrictMode;
    }

    /// <summary>ported from: src/compiler/binder.ts:2806-2820 — <c>bindJSDoc</c>.</summary>
    private void BindJSDoc(Node node)
    {
        if (Utilities.HasJSDocNodes(node))
        {
            var jsDoc = (JSDocArray)((IJSDocContainer)node).JsDoc!;
            if (Utilities.IsInJSFile(node))
            {
                foreach (var j in jsDoc)
                {
                    Bind(j);
                }
            }
            else
            {
                foreach (var j in jsDoc)
                {
                    Utilities.SetParent(j, node);
                    Utilities.SetParentRecursive(j, /*incremental*/ false);
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:2822-2834 — <c>updateStrictModeStatementList</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS takes <c>NodeArray&lt;Statement&gt;</c>; the port takes the covariant <c>INodeArray&lt;INode&gt;</c>
    /// so every statement list in the model fits, and casts each element to <see cref="Node"/> (see Binder.Children.cs).
    /// </remarks>
    internal void UpdateStrictModeStatementList(INodeArray<INode> statements)
    {
        if (!_inStrictMode)
        {
            foreach (var statement in statements)
            {
                var statementNode = (Node)statement;
                if (!Utilities.IsPrologueDirective(statementNode))
                {
                    return;
                }

                if (IsUseStrictPrologueDirective((ExpressionStatement)statementNode))
                {
                    _inStrictMode = true;
                    return;
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:2838-2844 — <c>isUseStrictPrologueDirective</c>.</summary>
    /// <remarks>Should be called only on prologue directives (isPrologueDirective(node) should be true).</remarks>
    private bool IsUseStrictPrologueDirective(ExpressionStatement node)
    {
        var nodeText = Utilities.GetSourceTextOfNodeFromSourceFile(_file, node.Expression!);

        // Note: the node text must be exactly "use strict" or 'use strict'.  It is not ok for the
        // string to contain unicode escapes (as per ES5).
        return nodeText == "\"use strict\"" || nodeText == "'use strict'";
    }
}
