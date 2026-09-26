#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/module/module.ts:2259-2494 — the emit-notification hook, the substitution hooks
// and the exports/searches they rest on.
/// <summary>
/// ported from: src/compiler/transformers/module/module.ts:2259-2494 — <c>onEmitNode</c> (which restores the current
/// file's module info around the emit of a <see cref="SourceFile"/>), the five substitution hooks that rewrite
/// imported/exported identifiers, and <c>getExports</c>.
/// </summary>
/// <remarks>
/// PORT-NOTE (<c>node.id</c>): TS's <c>node.id &amp;&amp; noSubstitution[node.id]</c> is "the node has been assigned an id
/// and that id is in the set"; the port's <see cref="Node.Id"/> is the nullable slot <c>Utilities.GetNodeId</c> fills, so
/// the test is <c>is int id &amp;&amp; id != 0</c> (an id is never 0).
/// </remarks>
internal sealed partial class ModuleTransformer
{
    /// <summary>ported from: src/compiler/transformers/module/module.ts:2259-2282 — <c>onEmitNode</c>.</summary>
    private void OnEmitNode(EmitHint hint, Node node, Action<EmitHint, Node> emitCallback)
    {
        if (node.Kind == SyntaxKind.SourceFile)
        {
            _currentSourceFile = (SourceFile)node;
            _currentModuleInfo = _moduleInfoMap.TryGetValue(TransformationUtilities.GetOriginalNodeId(_currentSourceFile), out var moduleInfo)
                ? moduleInfo
                : null;

            _previousOnEmitNode(hint, node, emitCallback);

            _currentSourceFile = null;
            _currentModuleInfo = null;
        }
        else
        {
            _previousOnEmitNode(hint, node, emitCallback);
        }
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:2284-2304 — <c>onSubstituteNode</c>.</summary>
    private Node OnSubstituteNode(EmitHint hint, Node node)
    {
        node = _previousOnSubstituteNode(hint, node) ?? node;
        if (node.Id is int nodeId && nodeId != 0 && _noSubstitution.Contains(nodeId))
        {
            return node;
        }

        if (hint == EmitHint.Expression)
        {
            return SubstituteExpression((Expression)node);
        }
        else if (NodeTests.IsShorthandPropertyAssignment(node))
        {
            return SubstituteShorthandPropertyAssignment((ShorthandPropertyAssignment)node);
        }

        return node;
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:2306-2324 — <c>substituteShorthandPropertyAssignment</c>.</summary>
    /// <remarks>PORT-NOTE: TS's answer type is <c>ObjectLiteralElementLike</c>; the two members this member builds are
    /// <see cref="PropertyAssignment"/> and the input <see cref="ShorthandPropertyAssignment"/>, so the port's answer is
    /// their common base, <see cref="Node"/>.</remarks>
    private Node SubstituteShorthandPropertyAssignment(ShorthandPropertyAssignment node)
    {
        var name = node.Name!;
        var exportedOrImportedName = SubstituteExpressionIdentifier(name);
        if (!ReferenceEquals(exportedOrImportedName, name))
        {
            // A shorthand property with an assignment initializer is probably part of a
            // destructuring assignment
            if (node.ObjectAssignmentInitializer is not null)
            {
                var initializer = _factory.CreateAssignment(exportedOrImportedName, node.ObjectAssignmentInitializer);
                return Utilities.SetTextRange(_factory.CreatePropertyAssignment(name, initializer), node);
            }
            return Utilities.SetTextRange(_factory.CreatePropertyAssignment(name, exportedOrImportedName), node);
        }
        return node;
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:2326-2339 — <c>substituteExpression</c>.</summary>
    private Node SubstituteExpression(Expression node)
        => node.Kind switch
        {
            SyntaxKind.Identifier => SubstituteExpressionIdentifier((Identifier)node),
            SyntaxKind.CallExpression => SubstituteCallExpression((CallExpression)node),
            SyntaxKind.TaggedTemplateExpression => SubstituteTaggedTemplateExpression((TaggedTemplateExpression)node),
            SyntaxKind.BinaryExpression => SubstituteBinaryExpression((BinaryExpression)node),
            _ => node,
        };

    /// <summary>ported from: src/compiler/transformers/module/module.ts:2341-2353 — <c>substituteCallExpression</c>.</summary>
    private Node SubstituteCallExpression(CallExpression node)
    {
        if (NodeTests.IsIdentifier(node.Expression!))
        {
            var expression = SubstituteExpressionIdentifier((Identifier)node.Expression!);
            _noSubstitution.Add(Utilities.GetNodeId(expression));
            if (!NodeTests.IsIdentifier(expression) && (Utilities.GetEmitFlags(node.Expression!) & EmitFlags.HelperName) == 0)
            {
                return EmitNodes.AddInternalEmitFlags(
                    _factory.UpdateCallExpression(node, expression, /*typeArguments*/ null, node.Arguments),
                    InternalEmitFlags.IndirectCall);
            }
        }
        return node;
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:2355-2373 — <c>substituteTaggedTemplateExpression</c>.</summary>
    private Node SubstituteTaggedTemplateExpression(TaggedTemplateExpression node)
    {
        if (NodeTests.IsIdentifier(node.Tag!))
        {
            var tag = SubstituteExpressionIdentifier((Identifier)node.Tag!);
            _noSubstitution.Add(Utilities.GetNodeId(tag));
            if (!NodeTests.IsIdentifier(tag) && (Utilities.GetEmitFlags(node.Tag!) & EmitFlags.HelperName) == 0)
            {
                return EmitNodes.AddInternalEmitFlags(
                    _factory.UpdateTaggedTemplateExpression(node, tag, /*typeArguments*/ null, node.Template),
                    InternalEmitFlags.IndirectCall);
            }
        }
        return node;
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:2375-2423 — <c>substituteExpressionIdentifier</c>.</summary>
    /// <remarks>PORT-NOTE: TS's guard is
    /// <c>!(isGeneratedIdentifier(node) &amp;&amp; !(node.emitNode.autoGenerate.flags &amp; GeneratedIdentifierFlags.AllowNameSubstitution))</c>,
    /// which the port states as the same "not generated, or generated with the flag" test through
    /// <c>EmitNodes.GetIdentifierAutoGenerate</c>. The <c>importDeclaration.parent?.parent?.parent || importDeclaration</c>
    /// fallback is a node-or-node chain, so the port's <c>??</c> is that same fallback (a nullable <c>Parent</c> walk).</remarks>
    private Expression SubstituteExpressionIdentifier(Identifier node)
    {
        if ((Utilities.GetEmitFlags(node) & EmitFlags.HelperName) != 0)
        {
            var externalHelpersModuleName = FactoryUtilities.GetExternalHelpersModuleName(_currentSourceFile!);
            if (externalHelpersModuleName is not null)
            {
                return _factory.CreatePropertyAccessExpression(externalHelpersModuleName, node);
            }
            return node;
        }
        else if (!(Utilities.IsGeneratedIdentifier(node)
                && (EmitNodes.GetIdentifierAutoGenerate(node)!.Flags & GeneratedIdentifierFlags.AllowNameSubstitution) == 0)
            && !FactoryUtilities.IsLocalName(node))
        {
            var exportContainer = _resolver.GetReferencedExportContainer(node, FactoryUtilities.IsExportName(node));
            if (exportContainer is not null && exportContainer.Kind == SyntaxKind.SourceFile)
            {
                return Utilities.SetTextRange(
                    _factory.CreatePropertyAccessExpression(
                        _factory.CreateIdentifier("exports", null, null),
                        _factory.CloneNode(node)),
                    /*location*/ node);
            }
            var importDeclaration = _resolver.GetReferencedImportDeclaration(node);
            if (importDeclaration is not null)
            {
                var importDeclarationNode = (Node)importDeclaration;
                if (NodeTests.IsImportClause(importDeclarationNode))
                {
                    return Utilities.SetTextRange(
                        _factory.CreatePropertyAccessExpression(
                            _factory.GetGeneratedNameForNode(importDeclarationNode.Parent, null),
                            _factory.CreateIdentifier("default", null, null)),
                        /*location*/ node);
                }
                else if (NodeTests.IsImportSpecifier(importDeclarationNode))
                {
                    var name = (Node?)((ImportSpecifier)importDeclarationNode).PropertyName ?? ((ImportSpecifier)importDeclarationNode).Name!;
                    var parent = importDeclarationNode.Parent;
                    var target = _factory.GetGeneratedNameForNode(
                        parent?.Parent?.Parent ?? importDeclarationNode,
                        null);
                    Expression access = NodeTests.IsStringLiteral(name)
                        ? _factory.CreateElementAccessExpression(target, _factory.CloneNode(name))
                        : _factory.CreatePropertyAccessExpression(target, _factory.CloneNode(name));
                    return Utilities.SetTextRange(access, /*location*/ node);
                }
            }
        }
        return node;
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:2425-2458 — <c>substituteBinaryExpression</c>.</summary>
    private Node SubstituteBinaryExpression(BinaryExpression node)
    {
        // When we see an assignment expression whose left-hand side is an exported symbol,
        // we should ensure all exports of that symbol are updated with the correct value.
        //
        // - We do not substitute generated identifiers unless they are file-level reserved names.
        // - We do not substitute identifiers tagged with the LocalName flag.
        // - We only substitute identifiers that are exported at the top level.
        if (
            Utilities.IsAssignmentOperator(node.OperatorToken!.Kind)
            && NodeTests.IsIdentifier(node.Left!)
            && (!Utilities.IsGeneratedIdentifier(node.Left!) || ModuleTransformSupport.IsFileLevelReservedGeneratedIdentifier(node.Left!))
            && !FactoryUtilities.IsLocalName(node.Left!)
        )
        {
            var exportedNames = GetExports((Identifier)node.Left!);
            if (exportedNames is not null)
            {
                // For each additional export of the declaration, apply an export assignment.
                Expression expression = node;
                foreach (var exportName in exportedNames)
                {
                    // Mark the node to prevent triggering this rule again.
                    _noSubstitution.Add(Utilities.GetNodeId(expression));
                    expression = CreateExportExpression(exportName, expression, /*location*/ node);
                }

                return expression;
            }
        }

        return node;
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:2460-2494 — <c>getExports</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>exportedBindings</c> is a sparse array read as
    /// <c>exportedBindings[getOriginalNodeId(x)]</c>; the port's list is dense (see
    /// <c>TransformationUtilities.CollectExternalModuleInfo</c>), so the read is <see cref="Core.ElementAt{T}"/>, which
    /// is exactly the "hole reads as undefined" rule. <c>arrayFrom(bindingsSet)</c> is the set's own order, which
    /// <see cref="Enumerable.ToList{T}"/> preserves.</remarks>
    private List<Node>? GetExports(Identifier name)
    {
        if (!Utilities.IsGeneratedIdentifier(name))
        {
            var importDeclaration = _resolver.GetReferencedImportDeclaration(name);
            if (importDeclaration is not null)
            {
                // PORT-NOTE: the reference writes `currentModuleInfo?.exportedBindings[…]` (module.ts:2464) — the module
                // info is absent when the substitution hook runs outside the module transform's file scope (the es5
                // downlevel path reaches here), and the reference answers `undefined` then. The port's `!` threw an NRE.
                if (_currentModuleInfo is null) return null;
                var importBindings = Core.ElementAt(_currentModuleInfo.ExportedBindings, TransformationUtilities.GetOriginalNodeId((Node)importDeclaration));
                return importBindings is null ? null : [.. importBindings.Cast<Node>()];
            }

            // An exported namespace or enum may merge with an ambient declaration, which won't show up in .js emit, so
            // we analyze all value exports of a symbol.
            var bindingsSet = new HashSet<Identifier>();
            var declarations = _resolver.GetReferencedValueDeclarations(name);
            if (declarations is not null)
            {
                foreach (var declaration in declarations)
                {
                    var bindings = _currentModuleInfo is null
                        ? null
                        : Core.ElementAt(_currentModuleInfo.ExportedBindings, TransformationUtilities.GetOriginalNodeId((Node)declaration));
                    if (bindings is not null)
                    {
                        foreach (var binding in bindings)
                        {
                            bindingsSet.Add(binding);
                        }
                    }
                }
                if (bindingsSet.Count > 0)
                {
                    return [.. bindingsSet.Cast<Node>()];
                }
            }
        }
        else if (ModuleTransformSupport.IsFileLevelReservedGeneratedIdentifier(name))
        {
            var exportSpecifiers = _currentModuleInfo?.ExportSpecifiers.Get(name);
            if (exportSpecifiers is not null)
            {
                var exportedNames = new List<Node>();
                foreach (var exportSpecifier in exportSpecifiers)
                {
                    exportedNames.Add(exportSpecifier.Name!);
                }
                return exportedNames;
            }
        }
        return null;
    }
}
