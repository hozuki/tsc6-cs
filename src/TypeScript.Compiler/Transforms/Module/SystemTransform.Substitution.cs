#nullable enable

namespace TypeScript.Compiler;

/// <summary>ported from: src/compiler/transformers/module/system.ts:1758-2050 — the emit notification and substitution
/// hooks of <c>transformSystemModule</c>: the per-file state restoration <c>onEmitNode</c>, and the substitutions that
/// turn imported identifiers into their local aliases, exported assignments into <c>exports(&hellip;)</c> calls, and
/// <c>import.meta</c> into the module body's <c>context</c> object.</summary>
internal sealed partial class SystemTransformer
{
    //
    // Emit Notification
    //

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1769-1794 — <c>onEmitNode</c>.</summary>
    /// <remarks>PORT-NOTE: the four map reads are the port's <c>GetValueOrDefault</c> (the reference's sparse-array
    /// reads, which answer <c>undefined</c> for a missing file) and the <c>delete noSubstitutionMap[id]</c> is
    /// <see cref="Dictionary{TKey,TValue}.Remove(TKey)"/>.</remarks>
    private void OnEmitNode(EmitHint hint, Node node, Action<EmitHint, Node> emitCallback)
    {
        if (node.Kind == SyntaxKind.SourceFile)
        {
            var id = TransformationUtilities.GetOriginalNodeId(node);
            _currentSourceFile = (SourceFile)node;
            _moduleInfo = _moduleInfoMap.GetValueOrDefault(id);
            _exportFunction = _exportFunctionsMap.GetValueOrDefault(id);
            _noSubstitution = _noSubstitutionMap.GetValueOrDefault(id);
            _contextObject = _contextObjectMap.GetValueOrDefault(id);

            if (_noSubstitution is not null)
            {
                _noSubstitutionMap.Remove(id);
            }

            _previousOnEmitNode(hint, node, emitCallback);

            _currentSourceFile = null;
            _moduleInfo = null;
            _exportFunction = null;
            _contextObject = null;
            _noSubstitution = null;
        }
        else
        {
            _previousOnEmitNode(hint, node, emitCallback);
        }
    }

    //
    // Substitutions
    //

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1805-1821 — <c>onSubstituteNode</c>.</summary>
    private Node OnSubstituteNode(EmitHint hint, Node node)
    {
        node = _previousOnSubstituteNode(hint, node);
        if (IsSubstitutionPrevented(node))
        {
            return node;
        }

        if (hint == EmitHint.Expression)
        {
            return SubstituteExpression((Expression)node);
        }
        else if (hint == EmitHint.Unspecified)
        {
            return (Node)SubstituteUnspecified(node)!;
        }

        return node;
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1826-1832 — <c>substituteUnspecified</c>.</summary>
    private object? SubstituteUnspecified(Node node)
        => node.Kind switch
        {
            SyntaxKind.ShorthandPropertyAssignment => SubstituteShorthandPropertyAssignment((ShorthandPropertyAssignment)node),
            _ => node,
        };

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1839-1872 — <c>substituteShorthandPropertyAssignment</c>.</summary>
    /// <remarks>PORT-NOTE: the two <c>importDeclaration.parent</c>/<c>parent?.parent?.parent</c> walks read the model's
    /// <see cref="Node.Parent"/>; the reference's <c>a || b</c> fallback is the port's <c>??</c>.</remarks>
    private object? SubstituteShorthandPropertyAssignment(ShorthandPropertyAssignment node)
    {
        var name = node.Name!;
        if (!Utilities.IsGeneratedIdentifier(name) && !FactoryUtilities.IsLocalName(name))
        {
            var importDeclaration = _resolver.GetReferencedImportDeclaration(name);
            if (importDeclaration is not null)
            {
                if (NodeTests.IsImportClause((Node)importDeclaration))
                {
                    return Utilities.SetTextRange(
                        _factory.CreatePropertyAssignment(
                            _factory.CloneNode(name),
                            _factory.CreatePropertyAccessExpression(
                                _factory.GetGeneratedNameForNode(((Node)importDeclaration).Parent, /*flags*/ null),
                                _factory.CreateIdentifier("default", null, null))),
                        /*location*/ node);
                }
                else if (NodeTests.IsImportSpecifier((Node)importDeclaration))
                {
                    var specifier = (ImportSpecifier)(Node)importDeclaration;
                    var importedName = (Node?)specifier.PropertyName ?? specifier.Name!;
                    var target = _factory.GetGeneratedNameForNode(specifier.Parent?.Parent?.Parent ?? (Node)specifier, /*flags*/ null);
                    Expression propertyExpression = importedName.Kind == SyntaxKind.StringLiteral
                        ? _factory.CreateElementAccessExpression(target, _factory.CloneNode(importedName))
                        : _factory.CreatePropertyAccessExpression(target, _factory.CloneNode(importedName));
                    return Utilities.SetTextRange(
                        _factory.CreatePropertyAssignment(
                            _factory.CloneNode(name),
                            propertyExpression),
                        /*location*/ node);
                }
            }
        }
        return node;
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1879-1891 — <c>substituteExpression</c>.</summary>
    private Expression SubstituteExpression(Expression node)
        => node.Kind switch
        {
            SyntaxKind.Identifier => SubstituteExpressionIdentifier((Identifier)node),
            SyntaxKind.BinaryExpression => SubstituteBinaryExpression((BinaryExpression)node),
            SyntaxKind.MetaProperty => SubstituteMetaProperty((MetaProperty)node),
            _ => node,
        };

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1897-1941 — <c>substituteExpressionIdentifier</c>.</summary>
    /// <remarks>PORT-NOTE: <c>getExternalHelpersModuleName</c> is <see cref="FactoryUtilities.GetExternalHelpersModuleName"/>
    /// and the reference's <c>a || b</c> fallback for the import specifier's owner is the port's <c>??</c>.</remarks>
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

        // When we see an identifier in an expression position that
        // points to an imported symbol, we should substitute a qualified
        // reference to the imported symbol if one is needed.
        //
        // - We do not substitute generated identifiers for any reason.
        // - We do not substitute identifiers tagged with the LocalName flag.
        if (!Utilities.IsGeneratedIdentifier(node) && !FactoryUtilities.IsLocalName(node))
        {
            var importDeclaration = _resolver.GetReferencedImportDeclaration(node);
            if (importDeclaration is not null)
            {
                if (NodeTests.IsImportClause((Node)importDeclaration))
                {
                    return Utilities.SetTextRange(
                        _factory.CreatePropertyAccessExpression(
                            _factory.GetGeneratedNameForNode(((Node)importDeclaration).Parent, /*flags*/ null),
                            _factory.CreateIdentifier("default", null, null)),
                        /*location*/ node);
                }
                else if (NodeTests.IsImportSpecifier((Node)importDeclaration))
                {
                    var specifier = (ImportSpecifier)(Node)importDeclaration;
                    var importedName = (Node?)specifier.PropertyName ?? specifier.Name!;
                    var target = _factory.GetGeneratedNameForNode(specifier.Parent?.Parent?.Parent ?? (Node)specifier, /*flags*/ null);
                    Expression propertyExpression = importedName.Kind == SyntaxKind.StringLiteral
                        ? _factory.CreateElementAccessExpression(target, _factory.CloneNode(importedName))
                        : _factory.CreatePropertyAccessExpression(target, _factory.CloneNode(importedName));
                    return Utilities.SetTextRange(propertyExpression, /*location*/ node);
                }
            }
        }

        return node;
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1946-1970 — <c>substituteBinaryExpression</c>.</summary>
    private Expression SubstituteBinaryExpression(BinaryExpression node)
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
        ) {
            var exportedNames = GetExports((Identifier)node.Left!);
            if (exportedNames is not null)
            {
                // For each additional export of the declaration, apply an export assignment.
                Expression expression = node;
                foreach (var exportName in exportedNames)
                {
                    expression = CreateExportExpression(exportName, PreventSubstitution(expression));
                }

                return expression;
            }
        }

        return node;
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1974-1979 — <c>substituteMetaProperty</c>.</summary>
    private Expression SubstituteMetaProperty(MetaProperty node)
        => IsImportMeta(node)
            ? _factory.CreatePropertyAccessExpression(_contextObject!, _factory.CreateIdentifier("meta", null, null))
            : node;

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1986-2007 — <c>getExports</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE (<c>exportedNames</c>): TS's outer local is <c>Identifier[]</c> and the generated-identifier arm answers
    /// a shadowing <c>ModuleExportName[]</c>, so the inferred union is what the callers read. The port states one
    /// <c>List&lt;Node&gt;</c> for both arms — the union's representation, the same one
    /// <c>createExportExpression</c> takes.
    /// </para>
    /// <para>
    /// PORT-NOTE (<c>exportedBindings</c>): the slot is the sparse array <c>utilities.ts</c> fills by node id;
    /// <see cref="GetExportedBindings"/> is the reader that answers the reference's <c>undefined</c> for a hole or an
    /// out-of-range id (the port's dense <see cref="List{T}"/> would otherwise throw).
    /// </para>
    /// </remarks>
    private List<Node>? GetExports(Identifier name)
    {
        List<Node>? exportedNames = null;
        var valueDeclaration = GetReferencedDeclaration(name);
        if (valueDeclaration is not null)
        {
            var exportContainer = _resolver.GetReferencedExportContainer(name, /*prefixLocals*/ false);
            if (exportContainer is not null && exportContainer.Kind == SyntaxKind.SourceFile)
            {
                exportedNames = (List<Node>?)Core.Append(
                    exportedNames,
                    TransformationUtilities.GetDeclarationName(_factory, (Node)valueDeclaration));
            }

            exportedNames = AddRange(exportedNames, GetExportedBindings(_moduleInfo, (Node)valueDeclaration));
        }
        else if (Utilities.IsGeneratedIdentifier(name) && ModuleTransformSupport.IsFileLevelReservedGeneratedIdentifier(name))
        {
            var exportSpecifiers = _moduleInfo?.ExportSpecifiers.Get(name);
            if (exportSpecifiers is not null)
            {
                var names = new List<Node>();
                foreach (var exportSpecifier in exportSpecifiers)
                {
                    names.Add(exportSpecifier.Name!);
                }
                return names;
            }
        }
        return exportedNames;
    }

    /// <summary>The reader half of the sparse <c>exportedBindings</c> array (<c>transformers/utilities.ts:377-386</c> is
    /// its writer, <c>multiMapSparseArrayAdd</c>).</summary>
    /// <remarks>PORT-NOTE: TS's <c>exportedBindings[id]</c> answers <c>undefined</c> for a hole or an id past the end; the
    /// port's dense <see cref="List{T}"/> needs the bounds test this reader states (a null entry — the writer's hole — is
    /// the same <c>undefined</c>). The answer is <c>IEnumerable&lt;Node&gt;</c> rather than the slot's
    /// <c>List&lt;Identifier&gt;</c> so <see cref="AddRange{T}"/> can append it to the <c>List&lt;Node&gt;</c> the
    /// reference's union-typed <c>exportedNames</c> erases to.</remarks>
    private static IEnumerable<Node>? GetExportedBindings(TransformationUtilities.IExternalModuleInfo? moduleInfo, Node declaration)
    {
        var bindings = moduleInfo?.ExportedBindings;
        if (bindings is null)
        {
            return null;
        }
        var id = TransformationUtilities.GetOriginalNodeId(declaration);
        return id >= 0 && id < bindings.Count ? bindings[id] : null;
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:2010-2032 — <c>getReferencedDeclaration</c>.</summary>
    private IDeclaration? GetReferencedDeclaration(Identifier name)
    {
        if (!Utilities.IsGeneratedIdentifier(name))
        {
            var importDeclaration = _resolver.GetReferencedImportDeclaration(name);
            if (importDeclaration is not null) return importDeclaration;

            var valueDeclaration = _resolver.GetReferencedValueDeclaration(name);
            if (valueDeclaration is not null && GetExportedBindings(_moduleInfo, (Node)valueDeclaration) is not null) return valueDeclaration;

            // An exported namespace or enum may merge with an ambient declaration, which won't show up in
            // .js emit. When that happens, try to find bindings associated with a non-ambient declaration.
            var declarations = _resolver.GetReferencedValueDeclarations(name);
            if (declarations is not null)
            {
                foreach (var declaration in declarations)
                {
                    if (!ReferenceEquals(declaration, valueDeclaration) && GetExportedBindings(_moduleInfo, (Node)declaration) is not null) return declaration;
                }
            }

            return valueDeclaration;
        }
        return null;
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:2036-2040 — <c>preventSubstitution</c>.</summary>
    private T PreventSubstitution<T>(T node)
        where T : Node
    {
        _noSubstitution ??= [];
        _noSubstitution.Add(Utilities.GetNodeId(node));
        return node;
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:2047-2049 — <c>isSubstitutionPrevented</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>node.id &amp;&amp; noSubstitution[node.id]</c> reads the node's own id field
    /// (not <c>getNodeId</c>, which would assign one); the port's <see cref="Node.Id"/> is that field and the nullable
    /// pattern is the <c>node.id</c> truthiness test.</remarks>
    private bool IsSubstitutionPrevented(Node node)
        => _noSubstitution is not null && node.Id is int id && _noSubstitution.Contains(id);

    //
    // Slice-local predicates whose reference homes have no shared port member yet
    //

    /// <summary>ported from: src/compiler/utilities.ts:2717-2722 — <c>isImportMeta</c>.</summary>
    /// <remarks>PORT-NOTE (home): <c>utilities.ts</c>'s <c>isImportMeta</c> is not yet a member of
    /// <see cref="Utilities"/> (the parser's own private copy is <c>parser.ts:500-502</c>), and this slice is its only
    /// reader outside that copy, so the body is stated here. It moves to <see cref="Utilities"/> when that file next gains
    /// a slice; this slice's report names it.</remarks>
    private static bool IsImportMeta(Node node)
        => NodeTests.IsMetaProperty(node)
            && ((MetaProperty)node).KeywordToken == SyntaxKind.ImportKeyword
            && ((MetaProperty)node).Name?.EscapedText == "meta";

    /// <summary>ported from: src/compiler/utilities.ts:2661-2663 — <c>isVarAwaitUsing</c>.</summary>
    /// <remarks>PORT-NOTE (home): as <see cref="IsImportMeta"/>; the printer's own copy is private
    /// (<c>Printer.Statements.cs:2282</c>).</remarks>
    private static bool IsVarAwaitUsing(Node node)
        => (Utilities.GetCombinedNodeFlags(node) & NodeFlags.BlockScoped) == NodeFlags.AwaitUsing;

    /// <summary>ported from: src/compiler/utilities.ts:2669-2671 — <c>isVarUsing</c>.</summary>
    /// <remarks>PORT-NOTE (home): as <see cref="IsVarAwaitUsing"/>.</remarks>
    private static bool IsVarUsing(Node node)
        => (Utilities.GetCombinedNodeFlags(node) & NodeFlags.BlockScoped) == NodeFlags.Using;
}
