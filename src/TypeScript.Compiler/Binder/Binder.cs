namespace TypeScript.Compiler;

// ported from: src/compiler/binder.ts:330-463 (getModuleInstanceState and its three helpers)
// ported from: src/compiler/binder.ts:3788-3813 (isExportsOrModuleExportsOrAlias)
// ported from: src/compiler/binder.ts:3814-3903 (getContainerFlags)
// ported from: src/compiler/binder.ts:3905-3916 (lookupSymbolForName)
/// <summary>
/// The members of TS's <c>binder.ts</c> that live outside <c>createBinder</c>.
/// </summary>
/// <remarks>
/// TS declares these as module-level functions of <c>binder.ts</c> — they answer questions about a tree that is
/// already parented, and none of them reads the binder's own state. The port keeps them together here as static
/// members of the file's class, which is also where the M5-1 batch adds the <c>createBinder</c> half as another
/// partial (state becomes instance fields, per §15.10's treatment of <c>parser.ts</c>).
/// <para>
/// PORT-NOTE: TS's <c>getModuleInstanceState</c> is the entry point the checker calls
/// (<c>checker.ts:51558</c> calls <c>bindSourceFile</c>, and module instantiation asks this), so it and its
/// helpers keep TS's visibility: the entry point is <c>public</c> (assembly-internal), the rest <c>private</c>.
/// </para>
/// <para>
/// PORT-NOTE: <c>getModuleInstanceStateWorker</c> walks children with <c>forEachChild(node, cb)</c> whose callback
/// answers <c>undefined</c> to continue and <c>true</c> to stop. The port passes <c>bool?</c> to the generated
/// table's generic visitor and returns <c>null</c> for the former, which is the same truthiness test the table
/// applies.
/// </para>
/// </remarks>
internal partial class Binder
{
    /// <summary>ported from: src/compiler/binder.ts:330-337 — <c>getModuleInstanceState</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS writes <c>node.body &amp;&amp; !node.body.parent</c>; the port's <c>ModuleDeclaration.Body</c> is a
    /// nullable slot, so the test is a property pattern that also binds the body for the two <c>setParent</c> calls
    /// that must run before the parent chain is walked.
    /// </remarks>
    public static ModuleInstanceState GetModuleInstanceState(ModuleDeclaration node, Dictionary<int, ModuleInstanceState?>? visited = null)
    {
        if (node.Body is { Parent: null } body)
        {
            // getModuleInstanceStateForAliasTarget needs to walk up the parent chain, so parent pointers must be set on this tree already
            Utilities.SetParent(body, node);
            Utilities.SetParentRecursive(body, /*incremental*/ false);
        }

        return node.Body is not null ? GetModuleInstanceStateCached(node.Body, visited) : ModuleInstanceState.Instantiated;
    }

    /// <summary>ported from: src/compiler/binder.ts:339-348 — <c>getModuleInstanceStateCached</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>visited.get(nodeId) || ModuleInstanceState.NonInstantiated</c> is a truthiness test, but
    /// the enum's zero is exactly the fallback (<c>NonInstantiated = 0</c>), so <c>??</c> answers the same value: a
    /// stored <c>undefined</c> marks a node still being visited, and the answer for it is <c>NonInstantiated</c>.
    /// </remarks>
    private static ModuleInstanceState GetModuleInstanceStateCached(Node node, Dictionary<int, ModuleInstanceState?>? visited = null)
    {
        visited ??= new Dictionary<int, ModuleInstanceState?>();

        var nodeId = Utilities.GetNodeId(node);
        if (visited.ContainsKey(nodeId))
        {
            return visited[nodeId] ?? ModuleInstanceState.NonInstantiated;
        }

        visited[nodeId] = null;
        var result = GetModuleInstanceStateWorker(node, visited);
        visited[nodeId] = result;
        return result;
    }

    /// <summary>ported from: src/compiler/binder.ts:350-420 — <c>getModuleInstanceStateWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the <c>ExportDeclaration</c> branch reads <c>exportClause.kind === SyntaxKind.NamedExports</c>
    /// followed by <c>exportClause.elements</c>; the port's slot is <c>Node?</c> (TS's union has two members) so the
    /// kind test becomes a class pattern that binds the elements.
    /// </remarks>
    private static ModuleInstanceState GetModuleInstanceStateWorker(Node node, Dictionary<int, ModuleInstanceState?> visited)
    {
        // A module is uninstantiated if it contains only
        switch (node.Kind)
        {
            // 1. interface declarations, type alias declarations
            case SyntaxKind.InterfaceDeclaration:
            case SyntaxKind.TypeAliasDeclaration:
                return ModuleInstanceState.NonInstantiated;

            // 2. const enum declarations
            case SyntaxKind.EnumDeclaration:
                if (Utilities.IsEnumConst((EnumDeclaration)node))
                {
                    return ModuleInstanceState.ConstEnumOnly;
                }

                break;

            // 3. non-exported import declarations
            case SyntaxKind.ImportDeclaration:
            case SyntaxKind.ImportEqualsDeclaration:
                if (!Utilities.HasSyntacticModifier(node, ModifierFlags.Export))
                {
                    return ModuleInstanceState.NonInstantiated;
                }

                break;

            // 4. Export alias declarations pointing at only uninstantiated modules or things uninstantiated modules contain
            case SyntaxKind.ExportDeclaration:
            {
                var exportDeclaration = (ExportDeclaration)node;
                if (exportDeclaration.ModuleSpecifier is null && exportDeclaration.ExportClause is NamedExports { Elements: { } elements })
                {
                    var state = ModuleInstanceState.NonInstantiated;
                    foreach (var specifier in elements)
                    {
                        var specifierState = GetModuleInstanceStateForAliasTarget(specifier, visited);
                        if (specifierState > state)
                        {
                            state = specifierState;
                        }

                        if (state == ModuleInstanceState.Instantiated)
                        {
                            return state;
                        }
                    }

                    return state;
                }

                break;
            }

            // 5. other uninstantiated module declarations.
            case SyntaxKind.ModuleBlock:
            {
                var state = ModuleInstanceState.NonInstantiated;
                ForEachChildTable.ForEachChild<object>(node, n =>
                {
                    var childState = GetModuleInstanceStateCached(n, visited);
                    switch (childState)
                    {
                        case ModuleInstanceState.NonInstantiated:
                            // child is non-instantiated - continue searching
                            return null;
                        case ModuleInstanceState.ConstEnumOnly:
                            // child is const enum only - record state and continue searching
                            state = ModuleInstanceState.ConstEnumOnly;
                            return null;
                        case ModuleInstanceState.Instantiated:
                            // child is instantiated - record state and stop
                            state = ModuleInstanceState.Instantiated;
                            return true;
                        default:
                            Debug.AssertNever(childState);
                            return null;
                    }
                });
                return state;
            }

            case SyntaxKind.ModuleDeclaration:
                return GetModuleInstanceState((ModuleDeclaration)node, visited);

            case SyntaxKind.Identifier:
                // Only jsdoc typedef definition can exist in jsdoc namespace, and it should
                // be considered the same as type alias
                if ((node.Flags & NodeFlags.IdentifierIsInJSDocNamespace) != 0)
                {
                    return ModuleInstanceState.NonInstantiated;
                }

                break;
        }

        return ModuleInstanceState.Instantiated;
    }

    /// <summary>ported from: src/compiler/binder.ts:422-463 — <c>getModuleInstanceStateForAliasTarget</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads <c>p.statements</c> after narrowing <c>p</c> to <c>Block | ModuleBlock | SourceFile</c>
    /// with three guards; C# has no structural union, so the three slots are read by a pattern switch and a
    /// <c>null</c> means "not a statement container" — which is where TS's guards sent the walk too.
    /// <para>
    /// PORT-NOTE: the elements of the port's <c>NodeArray&lt;IStatement&gt;</c> are the mixin interface
    /// <c>IStatement</c>, while the helpers below take the <see cref="Node"/> class; every statement is in fact a
    /// node class, so the cast is the port's equivalent of TS's structural narrowing (§12.9).
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>specifier.propertyName || specifier.name</c> is non-nullable; the port's slots are
    /// nullable, so a specifier missing both answers <c>Instantiated</c> like the non-identifier case instead of
    /// dereferencing <c>undefined</c>.
    /// </para>
    /// </remarks>
    private static ModuleInstanceState GetModuleInstanceStateForAliasTarget(ExportSpecifier specifier, Dictionary<int, ModuleInstanceState?> visited)
    {
        var name = specifier.PropertyName ?? specifier.Name;
        if (name is null || name.Kind != SyntaxKind.Identifier)
        {
            return ModuleInstanceState.Instantiated; // Skip for invalid syntax like this: export { "x" }
        }

        var p = specifier.Parent;
        while (p is not null)
        {
            var statements = p switch
            {
                Block block => block.Statements,
                ModuleBlock moduleBlock => moduleBlock.Statements,
                SourceFile sourceFile => sourceFile.Statements,
                _ => null,
            };

            if (statements is not null)
            {
                ModuleInstanceState? found = null;
                foreach (var statement in statements)
                {
                    var statementNode = (Node)statement;
                    if (Utilities.NodeHasName(statementNode, (Identifier)name))
                    {
                        if (statementNode.Parent is null)
                        {
                            Utilities.SetParent(statementNode, p);
                            Utilities.SetParentRecursive(statementNode, /*incremental*/ false);
                        }

                        var state = GetModuleInstanceStateCached(statementNode, visited);
                        if (found is null || state > found.Value)
                        {
                            found = state;
                        }

                        if (found == ModuleInstanceState.Instantiated)
                        {
                            return found.Value;
                        }

                        if (statementNode.Kind == SyntaxKind.ImportEqualsDeclaration)
                        {
                            // Treat re-exports of import aliases as instantiated,
                            // since they're ambiguous. This is consistent with
                            // `export import x = mod.x` being treated as instantiated:
                            //   import x = mod.x;
                            //   export { x };
                            found = ModuleInstanceState.Instantiated;
                        }
                    }
                }

                if (found is not null)
                {
                    return found.Value;
                }
            }

            p = p.Parent;
        }

        return ModuleInstanceState.Instantiated; // Couldn't locate, assume could refer to a value
    }

    /// <summary>ported from: src/compiler/binder.ts:3814-3903 — <c>getContainerFlags</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the accessor/method group falls through to <c>Constructor</c> in TS; C# has no implicit
    /// fall-through, so the shared tail is reached with <c>goto case</c>, which is the same jump without a second
    /// copy of the flag set.
    /// </remarks>
    public static ContainerFlags GetContainerFlags(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.ClassExpression:
            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.EnumDeclaration:
            case SyntaxKind.ObjectLiteralExpression:
            case SyntaxKind.TypeLiteral:
            case SyntaxKind.JSDocTypeLiteral:
            case SyntaxKind.JsxAttributes:
                return ContainerFlags.IsContainer;

            case SyntaxKind.InterfaceDeclaration:
                return ContainerFlags.IsContainer | ContainerFlags.IsInterface;

            case SyntaxKind.ModuleDeclaration:
            case SyntaxKind.TypeAliasDeclaration:
            case SyntaxKind.MappedType:
            case SyntaxKind.IndexSignature:
                return ContainerFlags.IsContainer | ContainerFlags.HasLocals;

            case SyntaxKind.SourceFile:
                return ContainerFlags.IsContainer | ContainerFlags.IsControlFlowContainer | ContainerFlags.HasLocals;

            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.MethodDeclaration:
                if (Utilities.IsObjectLiteralOrClassExpressionMethodOrAccessor(node))
                {
                    return ContainerFlags.IsContainer | ContainerFlags.IsControlFlowContainer | ContainerFlags.HasLocals | ContainerFlags.IsFunctionLike | ContainerFlags.IsObjectLiteralOrClassExpressionMethodOrAccessor;
                }

                // falls through
                goto case SyntaxKind.Constructor;

            case SyntaxKind.Constructor:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.ClassStaticBlockDeclaration:
                return ContainerFlags.IsContainer | ContainerFlags.IsControlFlowContainer | ContainerFlags.HasLocals | ContainerFlags.IsFunctionLike;

            case SyntaxKind.MethodSignature:
            case SyntaxKind.CallSignature:
            case SyntaxKind.JSDocSignature:
            case SyntaxKind.JSDocFunctionType:
            case SyntaxKind.FunctionType:
            case SyntaxKind.ConstructSignature:
            case SyntaxKind.ConstructorType:
                return ContainerFlags.IsContainer | ContainerFlags.IsControlFlowContainer | ContainerFlags.HasLocals | ContainerFlags.IsFunctionLike | ContainerFlags.PropagatesThisKeyword;

            case SyntaxKind.JSDocImportTag:
                // treat as a container to prevent using an enclosing effective host, ensuring import bindings are scoped correctly
                return ContainerFlags.IsContainer | ContainerFlags.IsControlFlowContainer | ContainerFlags.HasLocals | ContainerFlags.PropagatesThisKeyword;

            case SyntaxKind.FunctionExpression:
                return ContainerFlags.IsContainer | ContainerFlags.IsControlFlowContainer | ContainerFlags.HasLocals | ContainerFlags.IsFunctionLike | ContainerFlags.IsFunctionExpression;

            case SyntaxKind.ArrowFunction:
                return ContainerFlags.IsContainer | ContainerFlags.IsControlFlowContainer | ContainerFlags.HasLocals | ContainerFlags.IsFunctionLike | ContainerFlags.IsFunctionExpression | ContainerFlags.PropagatesThisKeyword;

            case SyntaxKind.ModuleBlock:
                return ContainerFlags.IsControlFlowContainer;

            case SyntaxKind.PropertyDeclaration:
                // PORT-NOTE: TS tests the initializer's truthiness; a node is truthy exactly when it is present.
                return ((PropertyDeclaration)node).Initializer is not null ? ContainerFlags.IsControlFlowContainer : ContainerFlags.None;

            case SyntaxKind.CatchClause:
            case SyntaxKind.ForStatement:
            case SyntaxKind.ForInStatement:
            case SyntaxKind.ForOfStatement:
            case SyntaxKind.CaseBlock:
                return ContainerFlags.IsBlockScopedContainer | ContainerFlags.HasLocals;

            case SyntaxKind.Block:
                // do not treat blocks directly inside a function as a block-scoped-container.
                // Locals that reside in this block should go to the function locals. Otherwise 'x'
                // would not appear to be a redeclaration of a block scoped local in the following
                // example:
                //
                //      function foo() {
                //          var x;
                //          let x;
                //      }
                //
                // If we placed 'var x' into the function locals and 'let x' into the locals of
                // the block, then there would be no collision.
                //
                // By not creating a new block-scoped-container here, we ensure that both 'var x'
                // and 'let x' go into the Function-container's locals, and we do get a collision
                // conflict.
                return Utilities.IsFunctionLike(node.Parent)
                    || (node.Parent is { } parent && NodeTests.IsClassStaticBlockDeclaration(parent))
                    ? ContainerFlags.None
                    : ContainerFlags.IsBlockScopedContainer | ContainerFlags.HasLocals;
        }

        return ContainerFlags.None;
    }

    /// <summary>ported from: src/compiler/binder.ts:3788-3813 — <c>isExportsOrModuleExportsOrAlias</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>isAssignmentExpression</c> narrows <c>init</c> to <c>BinaryExpression</c>; C# cannot
    /// narrow through a predicate, so the branch is completed with a class pattern. The initializer's <c>left</c> and
    /// <c>right</c> are non-optional upstream and nullable slots here (§12.6), hence the assertions.
    /// </remarks>
    public static bool IsExportsOrModuleExportsOrAlias(SourceFile sourceFile, Expression node)
    {
        var i = 0;
        var q = Core.CreateQueue<Expression>();
        q.Enqueue(node);
        while (!q.IsEmpty && i < 100)
        {
            i++;
            node = q.Dequeue();
            if (Utilities.IsExportsIdentifier(node) || Utilities.IsModuleExportsAccessExpression(node))
            {
                return true;
            }
            else if (node is Identifier identifier)
            {
                var symbol = LookupSymbolForName(sourceFile, identifier.EscapedText);
                if (symbol?.ValueDeclaration is VariableDeclaration declaration && declaration.Initializer is { } init)
                {
                    q.Enqueue(init);
                    if (Utilities.IsAssignmentExpression(init, /*excludeCompoundAssignment*/ true) && init is BinaryExpression binary)
                    {
                        q.Enqueue(binary.Left!);
                        q.Enqueue(binary.Right!);
                    }
                }
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/binder.ts:3905-3916 — <c>lookupSymbolForName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>tryCast(container, canHaveLocals)</c> is a type-guard cast; the port's capability
    /// interfaces make the same test a pattern, and a node without locals simply has no answer.
    /// <para>
    /// PORT-NOTE: <c>name</c> is <c>__String</c> upstream and can never be absent; the port's
    /// <c>Identifier.EscapedText</c> is <c>string?</c>, so the parameter is nullable and an absent name finds
    /// nothing (dictionary keys are never null).
    /// </para>
    /// </remarks>
    private static ISymbol? LookupSymbolForName(Node container, string? name)
    {
        if (name is null)
        {
            return null;
        }

        ISymbol? local = null;
        if (container is ILocalsContainer { Locals: { } locals } && locals.TryGetValue(name, out var found))
        {
            local = found;
        }

        if (local is not null)
        {
            return local.ExportSymbol ?? local;
        }

        if (container is SourceFile { JsGlobalAugmentations: { } jsGlobalAugmentations } && jsGlobalAugmentations.TryGetValue(name, out var global))
        {
            return global;
        }

        if (Utilities.CanHaveSymbol(container) && container is IDeclaration declaration)
        {
            return declaration.Symbol?.Exports is { } exports && exports.TryGetValue(name, out var exported) ? exported : null;
        }

        return null;
    }
}
