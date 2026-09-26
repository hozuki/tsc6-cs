#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/ts.ts:2259-2490 — the import/export members of `transformTypeScript`'s body
// and the namespace-export/emitted-declaration helpers they share.
/// <summary>
/// The import/export half of the TypeScript erase transform (src/compiler/transformers/ts.ts:2259-2490):
/// <c>visitImportDeclaration</c> through <c>visitImportEqualsDeclaration</c>, plus the namespace-export and
/// substitution-enabling helpers the enum/namespace rewrites call.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (<c>ImportPhaseModifierSyntaxKind</c>): the reference passes <c>node.phaseModifier</c> straight through;
/// the model carries it as <c>SyntaxKind?</c> and the factory's slot is <c>object?</c>, so the two agree as they stand.
/// </para>
/// <para>
/// PORT-NOTE (the result unions): TS's <c>VisitResult&lt;Statement | undefined&gt;</c> and
/// <c>NamedImportBindings | undefined</c> are the port's <c>object?</c> and <c>Node?</c> — the erased visit result and
/// the model's node base.
/// </para>
/// <para>
/// PORT-NOTE (<c>isExternalModule</c>): <c>utilitiesPublic.ts</c>'s <c>isExternalModule</c> is
/// <see cref="Parser.IsExternalModule"/>, the same predicate the parser exports (the reference re-exports it from
/// <c>parser.ts</c>).
/// </para>
/// </remarks>
internal sealed partial class TypeScriptTransformer
{
    // ---------------------------------------------------------------------------------------------
    // ts.ts:2259-2340 — imports
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/ts.ts:2259-2286 — <c>visitImportDeclaration</c>.</summary>
    private object? VisitImportDeclaration(ImportDeclaration node)
    {
        if (node.ImportClause is null)
        {
            // Do not elide a side-effect only import declaration.
            //  import "foo";
            return node;
        }
        if (node.ImportClause.IsTypeOnly)
        {
            // Always elide type-only imports
            return null;
        }

        // Elide the declaration if the import clause was elided.
        var importClause = Utilities.VisitNode(node.ImportClause, n => VisitImportClause((ImportClause)n), NodeTests.IsImportClause);
        return importClause is not null
            ? _factory.UpdateImportDeclaration(
                node,
                /*modifiers*/ null,
                importClause,
                node.ModuleSpecifier,
                node.Attributes)
            : null;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:2288-2299 — <c>visitImportClause</c>.</summary>
    /// <remarks>PORT-NOTE: <c>name || namedBindings</c> is a node-or-node test, so the port states it as the two
    /// <c>is not null</c> tests (a node is never falsy in TS either).</remarks>
    private ImportClause? VisitImportClause(ImportClause node)
    {
        Debug.Assert(node.PhaseModifier != SyntaxKind.TypeKeyword);
        // Elide the import clause if we elide both its name and its named bindings.
        var name = ShouldEmitAliasDeclaration(node) ? node.Name : null;
        var namedBindings = Utilities.VisitNode(node.NamedBindings, VisitNamedImportBindings, Utilities.IsNamedImportBindings);
        return name is not null || namedBindings is not null
            ? _factory.UpdateImportClause(node, node.PhaseModifier, name, namedBindings)
            : null;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:2301-2317 — <c>visitNamedImportBindings</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>NamedImportBindings</c> union (namespace import or named imports) has no model
    /// interface, so the parameter is a <see cref="Node"/> and the named-imports arm reads <c>elements</c> off the cast;
    /// the reference's <c>some(elements)</c> is <see cref="Core.Some{T}"/>.</remarks>
    private Node? VisitNamedImportBindings(Node node)
    {
        if (node.Kind == SyntaxKind.NamespaceImport)
        {
            // Elide a namespace import if it is not referenced.
            return ShouldEmitAliasDeclaration(node) ? node : null;
        }
        else
        {
            // Elide named imports if all of its import specifiers are elided and settings allow.
            var allowEmpty = Js.IsTruthy(_compilerOptions.VerbatimModuleSyntax);
            var elements = Utilities.VisitNodes(((NamedImports)node).Elements, VisitImportSpecifier, NodeTests.IsImportSpecifier);
            return allowEmpty || Core.Some(elements) ? _factory.UpdateNamedImports((NamedImports)node, elements) : null;
        }
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:2319-2327 — <c>visitImportSpecifier</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is the <c>NamedImportBindings</c> element union narrowed by
    /// <c>isImportSpecifier</c> at the call site; the port takes the <see cref="Node"/> the visitor slot carries and
    /// casts, as the visit-result contract records.</remarks>
    private ImportSpecifier? VisitImportSpecifier(Node node)
    {
        var specifier = (ImportSpecifier)node;
        return !specifier.IsTypeOnly && ShouldEmitAliasDeclaration(node) ? specifier : null;
    }

    // ---------------------------------------------------------------------------------------------
    // ts.ts:2329-2413 — exports
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/ts.ts:2329-2338 — <c>visitExportAssignment</c>.</summary>
    private object? VisitExportAssignment(ExportAssignment node)
        => Js.IsTruthy(_compilerOptions.VerbatimModuleSyntax) || _resolver.IsValueAliasDeclaration(node)
            ? Utilities.VisitEachChildWorker(node, Visitor, _context)
            : null;

    /// <summary>ported from: src/compiler/transformers/ts.ts:2341-2384 — <c>visitExportDeclaration</c>.</summary>
    private object? VisitExportDeclaration(ExportDeclaration node)
    {
        if (node.IsTypeOnly)
        {
            return null;
        }

        if (node.ExportClause is null || NodeTests.IsNamespaceExport(node.ExportClause))
        {
            // never elide `export <whatever> from <whereever>` declarations -
            // they should be kept for sideffects/untyped exports, even when the
            // type checker doesn't know about any exports
            return _factory.UpdateExportDeclaration(
                node,
                node.Modifiers,
                node.IsTypeOnly,
                node.ExportClause,
                node.ModuleSpecifier,
                node.Attributes);
        }

        // Elide the export declaration if all of its named exports are elided.
        var allowEmpty = Js.IsTruthy(_compilerOptions.VerbatimModuleSyntax);
        var exportClause = Utilities.VisitNode(
            node.ExportClause,
            (bindings) => VisitNamedExportBindings(bindings, allowEmpty),
            Utilities.IsNamedExportBindings);

        return exportClause is not null
            ? _factory.UpdateExportDeclaration(
                node,
                /*modifiers*/ null,
                node.IsTypeOnly,
                exportClause,
                node.ModuleSpecifier,
                node.Attributes)
            : null;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:2386-2390 — <c>visitNamedExports</c>.</summary>
    private NamedExports? VisitNamedExports(NamedExports node, bool allowEmpty)
    {
        // Elide the named exports if all of its export specifiers were elided.
        var elements = Utilities.VisitNodes(node.Elements, VisitExportSpecifier, NodeTests.IsExportSpecifier);
        return allowEmpty || Core.Some(elements) ? _factory.UpdateNamedExports(node, elements) : null;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:2392-2394 — <c>visitNamespaceExports</c>.</summary>
    private NamespaceExport VisitNamespaceExports(NamespaceExport node)
        => _factory.UpdateNamespaceExport(node, Debug.CheckDefined(Utilities.VisitNode(node.Name, Visitor, NodeTests.IsIdentifier)));

    /// <summary>ported from: src/compiler/transformers/ts.ts:2396-2398 — <c>visitNamedExportBindings</c>.</summary>
    private Node? VisitNamedExportBindings(Node node, bool allowEmpty)
        => NodeTests.IsNamespaceExport(node)
            ? VisitNamespaceExports((NamespaceExport)node)
            : VisitNamedExports((NamedExports)node, allowEmpty);

    /// <summary>ported from: src/compiler/transformers/ts.ts:2405-2408 — <c>visitExportSpecifier</c>.</summary>
    private ExportSpecifier? VisitExportSpecifier(Node node)
    {
        var specifier = (ExportSpecifier)node;
        // Elide an export specifier if it does not reference a value.
        return !specifier.IsTypeOnly && (Js.IsTruthy(_compilerOptions.VerbatimModuleSyntax) || _resolver.IsValueAliasDeclaration(node))
            ? specifier
            : null;
    }

    // ---------------------------------------------------------------------------------------------
    // ts.ts:2415-2489 — import equals declarations
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/ts.ts:2415-2427 — <c>shouldEmitImportEqualsDeclaration</c>.</summary>
    /// <remarks>PORT-NOTE: <c>isExternalModule(currentSourceFile)</c> is <see cref="Parser.IsExternalModule"/> over the
    /// current source file the reference's closure holds.</remarks>
    private bool ShouldEmitImportEqualsDeclaration(ImportEqualsDeclaration node)
    {
        // preserve old compiler's behavior: emit 'var' for import declaration (even if we do not consider them referenced) when
        // - current file is not external module
        // - import declaration is top level and target is value imported by entity name
        return ShouldEmitAliasDeclaration(node)
            || (!Parser.IsExternalModule(_currentSourceFile!)
                && _resolver.IsTopLevelValueImportEqualsWithEntityName(node));
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:2429-2489 — <c>visitImportEqualsDeclaration</c>.</summary>
    /// <remarks>PORT-NOTE: <c>createExpressionFromEntityName(factory, node.moduleReference as EntityName)</c> is
    /// <see cref="FactoryUtilities.CreateExpressionFromEntityName"/> (the cast is the reference's own, since
    /// <c>isExternalModuleImportEqualsDeclaration</c> has already excluded the external-module arm); the
    /// <c>[varDecl]</c> declaration list is a one-element <c>List&lt;VariableDeclaration&gt;</c> and
    /// <c>createVariableDeclarationList</c>'s absent flags are the reference's default.</remarks>
    private object? VisitImportEqualsDeclaration(ImportEqualsDeclaration node)
    {
        // Always elide type-only imports
        if (node.IsTypeOnly)
        {
            return null;
        }

        if (Utilities.IsExternalModuleImportEqualsDeclaration(node))
        {
            if (!ShouldEmitAliasDeclaration(node))
            {
                return null;
            }
            return Utilities.VisitEachChildWorker(node, Visitor, _context);
        }

        if (!ShouldEmitImportEqualsDeclaration(node))
        {
            return null;
        }

        var moduleReference = FactoryUtilities.CreateExpressionFromEntityName(_factory, node.ModuleReference!);
        EmitNodes.SetEmitFlags(moduleReference, EmitFlags.NoComments | EmitFlags.NoNestedComments);

        if (IsNamedExternalModuleExport(node) || !IsExportOfNamespace(node))
        {
            //  export var ${name} = ${moduleReference};
            //  var ${name} = ${moduleReference};
            return NodeFactory.SetOriginalNode(
                Utilities.SetTextRange(
                    _factory.CreateVariableStatement(
                        Utilities.VisitNodes(node.Modifiers, ModifierVisitor, Utilities.IsModifier),
                        _factory.CreateVariableDeclarationList(
                            [
                                NodeFactory.SetOriginalNode(
                                    _factory.CreateVariableDeclaration(
                                        node.Name,
                                        /*exclamationToken*/ null,
                                        /*type*/ null,
                                        moduleReference),
                                    node),
                            ])),
                    node),
                node);
        }
        else
        {
            // exports.${name} = ${moduleReference};
            return NodeFactory.SetOriginalNode(
                CreateNamespaceExport(node.Name!, moduleReference, node),
                node);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // ts.ts:2491-2575 — role tests and the namespace-export helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/ts.ts:2491-2498 — <c>isExportOfNamespace</c>.</summary>
    private bool IsExportOfNamespace(Node node)
        => _currentNamespace is not null && Utilities.HasSyntacticModifier(node, ModifierFlags.Export);

    /// <summary>ported from: src/compiler/transformers/ts.ts:2500-2507 — <c>isExternalModuleExport</c>.</summary>
    private bool IsExternalModuleExport(Node node)
        => _currentNamespace is null && Utilities.HasSyntacticModifier(node, ModifierFlags.Export);

    /// <summary>ported from: src/compiler/transformers/ts.ts:2509-2517 — <c>isNamedExternalModuleExport</c>.</summary>
    private bool IsNamedExternalModuleExport(Node node)
        => IsExternalModuleExport(node)
            && !Utilities.HasSyntacticModifier(node, ModifierFlags.Default);

    /// <summary>ported from: src/compiler/transformers/ts.ts:2519-2522 — <c>isDefaultExternalModuleExport</c>.</summary>
    private bool IsDefaultExternalModuleExport(Node node)
        => IsExternalModuleExport(node)
            && Utilities.HasSyntacticModifier(node, ModifierFlags.Default);

    /// <summary>ported from: src/compiler/transformers/ts.ts:2524-2534 — <c>createExportMemberAssignmentStatement</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's two <c>createRange</c> answers are
    /// <see cref="TypeScriptTransformSupport.CreateRange"/> and are adapted by
    /// <see cref="TypeScriptTransformSupport.SourceMapRangeOf"/>; <c>node.name ? node.name.pos : node.pos</c> reads the
    /// declaration's name through <see cref="INamedDeclaration"/>.</remarks>
    private ExpressionStatement CreateExportMemberAssignmentStatement(Node node)
    {
        var expression = _factory.CreateAssignment(
            TypeScriptTransformSupport.GetExternalModuleOrNamespaceExportName(_factory, _currentNamespaceContainerName, node, /*allowComments*/ false, /*allowSourceMaps*/ true),
            TypeScriptTransformSupport.GetLocalName(_factory, node, /*allowComments*/ null, /*allowSourceMaps*/ null));
        EmitNodes.SetSourceMapRange(expression, TypeScriptTransformSupport.SourceMapRangeOf(
            TypeScriptTransformSupport.CreateRange(((INamedDeclaration)node).Name is not null ? ((INamedDeclaration)node).Name!.Pos : node.Pos, node.End)));

        var statement = _factory.CreateExpressionStatement(expression);
        EmitNodes.SetSourceMapRange(statement, TypeScriptTransformSupport.SourceMapRangeOf(TypeScriptTransformSupport.CreateRange(-1, node.End)));
        return statement;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:2536-2538 — <c>addExportMemberAssignment</c>.</summary>
    private void AddExportMemberAssignment(List<Node> statements, Node node)
        => statements.Add(CreateExportMemberAssignmentStatement(node));

    /// <summary>ported from: src/compiler/transformers/ts.ts:2540-2550 — <c>createNamespaceExport</c>.</summary>
    private ExpressionStatement CreateNamespaceExport(Identifier exportName, Expression exportValue, ITextRange? location)
        => Utilities.SetTextRange(
            _factory.CreateExpressionStatement(
                _factory.CreateAssignment(
                    TypeScriptTransformSupport.GetNamespaceMemberName(_factory, _currentNamespaceContainerName!, exportName, /*allowComments*/ false, /*allowSourceMaps*/ true),
                    exportValue)),
            location);

    /// <summary>ported from: src/compiler/transformers/ts.ts:2552-2554 — <c>createNamespaceExportExpression</c>.</summary>
    private Expression CreateNamespaceExportExpression(Identifier exportName, Expression exportValue, ITextRange? location)
        => Utilities.SetTextRange(
            _factory.CreateAssignment(GetNamespaceMemberNameWithSourceMapsAndWithoutComments(exportName), exportValue),
            location);

    /// <summary>ported from: src/compiler/transformers/ts.ts:2556-2558 — <c>getNamespaceMemberNameWithSourceMapsAndWithoutComments</c>.</summary>
    private PropertyAccessExpression GetNamespaceMemberNameWithSourceMapsAndWithoutComments(Identifier name)
        => TypeScriptTransformSupport.GetNamespaceMemberName(_factory, _currentNamespaceContainerName!, name, /*allowComments*/ false, /*allowSourceMaps*/ true);

    /// <summary>ported from: src/compiler/transformers/ts.ts:2563-2567 — <c>getNamespaceParameterName</c>.</summary>
    /// <remarks>PORT-NOTE: <c>setSourceMapRange(name, node.name)</c> takes the declaration's name as the range, which
    /// <see cref="TypeScriptTransformSupport.SourceMapRangeOf"/> adapts.</remarks>
    private Identifier GetNamespaceParameterName(Node node)
    {
        var name = _factory.GetGeneratedNameForNode(node, /*flags*/ null);
        EmitNodes.SetSourceMapRange(name, TypeScriptTransformSupport.SourceMapRangeOf(((INamedDeclaration)node).Name!));
        return name;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:2573-2575 — <c>getNamespaceContainerName</c>.</summary>
    private Identifier GetNamespaceContainerName(Node node)
        => _factory.GetGeneratedNameForNode(node, /*flags*/ null);

    // ---------------------------------------------------------------------------------------------
    // ts.ts:2577-2605 — the two substitution enablers and the two transformed-kind tests
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/ts.ts:2577-2582 — <c>enableSubstitutionForNonQualifiedEnumMembers</c>.</summary>
    private void EnableSubstitutionForNonQualifiedEnumMembers()
    {
        if ((_enabledSubstitutions & TypeScriptSubstitutionFlags.NonQualifiedEnumMembers) == 0)
        {
            _enabledSubstitutions |= TypeScriptSubstitutionFlags.NonQualifiedEnumMembers;
            _context.EnableSubstitution(SyntaxKind.Identifier);
        }
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:2584-2596 — <c>enableSubstitutionForNamespaceExports</c>.</summary>
    private void EnableSubstitutionForNamespaceExports()
    {
        if ((_enabledSubstitutions & TypeScriptSubstitutionFlags.NamespaceExports) == 0)
        {
            _enabledSubstitutions |= TypeScriptSubstitutionFlags.NamespaceExports;

            // We need to enable substitutions for identifiers and shorthand property assignments. This allows us to
            // substitute the names of exported members of a namespace.
            _context.EnableSubstitution(SyntaxKind.Identifier);
            _context.EnableSubstitution(SyntaxKind.ShorthandPropertyAssignment);

            // We need to be notified when entering and exiting namespaces.
            _context.EnableEmitNotification(SyntaxKind.ModuleDeclaration);
        }
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:2598-2600 — <c>isTransformedModuleDeclaration</c>.</summary>
    /// <remarks>PORT-NOTE: <c>getOriginalNode(node)</c>'s non-null assertion is the port's <c>!</c> on the nullable
    /// overload's answer.</remarks>
    private static bool IsTransformedModuleDeclaration(Node node)
        => Utilities.GetOriginalNode(node)!.Kind == SyntaxKind.ModuleDeclaration;

    /// <summary>ported from: src/compiler/transformers/ts.ts:2602-2604 — <c>isTransformedEnumDeclaration</c>.</summary>
    private static bool IsTransformedEnumDeclaration(Node node)
        => Utilities.GetOriginalNode(node)!.Kind == SyntaxKind.EnumDeclaration;
}
