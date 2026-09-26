namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:48314-48334 (getFirstNonModuleExportsIdentifier)
// ported from: src/compiler/checker.ts:48336-48380 (checkExternalImportOrExportDeclaration)
// ported from: src/compiler/checker.ts:48382-48392 (checkModuleExportName)
// ported from: src/compiler/checker.ts:48394-48559 (checkAliasSymbol)
// ported from: src/compiler/checker.ts:48561-48589 (resolveAliasWithDeprecationCheck)
// ported from: src/compiler/checker.ts:48591-48604 (checkImportBinding)
// ported from: src/compiler/checker.ts:48606-48656 (checkImportAttributes)
// ported from: src/compiler/checker.ts:48658-48660 (checkImportAttribute)
// ported from: src/compiler/checker.ts:48662-48703 (checkImportDeclaration)
// ported from: src/compiler/checker.ts:48705-48707 (hasTypeJsonImportAttribute)
// ported from: src/compiler/checker.ts:48709-48748 (checkImportEqualsDeclaration)
// ported from: src/compiler/checker.ts:48750-48801 (checkExportDeclaration)
// ported from: src/compiler/checker.ts:48803-48808 (checkGrammarExportDeclaration)
// ported from: src/compiler/checker.ts:48810-48816 (checkGrammarModuleElementContext)
// ported from: src/compiler/checker.ts:48818-48849 (checkExportSpecifier)
// ported from: src/compiler/checker.ts:48851-48996 (checkExportAssignment)
// ported from: src/compiler/checker.ts:48998-49000 (hasExportedMembers)
// ported from: src/compiler/checker.ts:49002-49044 (checkExternalModuleExports)
// ported from: src/compiler/checker.ts:49046-49050 (isDuplicatedCommonJSExport)
// ported from: src/compiler/checker.ts:49052-49062 (checkSourceElement)
// ported from: src/compiler/checker.ts:49064-49276 (checkSourceElementWorker)
// ported from: src/compiler/checker.ts:49278-49335 (checkSourceElementUnreachable)
// ported from: src/compiler/checker.ts:49337-49356 (isSourceElementUnreachable)
// ported from: src/compiler/checker.ts:49358-49366 (checkJSDocCommentWorker)
// ported from: src/compiler/checker.ts:49368-49391 (checkJSDocTypeIsInJsFile)
// ported from: src/compiler/checker.ts:49393-49426 (checkJSDocVariadicType)
// ported from: src/compiler/checker.ts:49428-49460 (getTypeFromJSDocVariadicType)
// ported from: src/compiler/checker.ts:49471-49481 (checkNodeDeferred)
// ported from: src/compiler/checker.ts:49483-49489 (checkDeferredNodes)
// ported from: src/compiler/checker.ts:49491-49545 (checkDeferredNode)
/// <summary>
/// The module-element checking block: the import and export declarations, alias resolution and its deprecation check,
/// the module export table with its duplicate-export rules, and the source-element walk that dispatches every kind to
/// its own checker.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: the reference's node-union parameters that have no C# member are stated as <see cref="Node"/> (the union's
/// upper bound, §7.5) and the arm that is needed is read through a cast: <c>checkAliasSymbol</c>'s
/// <c>AliasDeclarationNode</c>, <c>checkImportBinding</c>'s
/// <c>ImportEqualsDeclaration | ImportClause | NamespaceImport | ImportSpecifier</c> (whose name comes from
/// <see cref="INamedDeclaration"/>, the face all four share), <c>checkImportAttributes</c>'s
/// <c>ImportDeclaration | ExportDeclaration | JSDocImportTag</c> (whose <c>moduleSpecifier</c> and <c>isTypeOnly</c> are
/// read through a switch over the three classes), and <c>checkExternalImportOrExportDeclaration</c>'s
/// <c>ImportDeclaration | ImportEqualsDeclaration | ExportDeclaration</c>. One union is not stated at all:
/// <c>ModuleExportName</c> (<c>Identifier | StringLiteral</c>) is a <see cref="Node"/> parameter wherever it appears.
/// </para>
/// <para>
/// PORT-NOTE: <c>checkSourceElement</c>'s parameter is <c>Node | undefined</c> upstream, and the port declares it
/// <see cref="INode"/>? — the interface face — because the checker's own callers hand it capability-typed nodes
/// (<c>Checker.CallResolution.cs:79</c> passes an <c>ITypeNode</c>); the body casts to <see cref="Node"/> where a
/// class-typed member is called. <c>checkSourceElementWorker</c> takes the class, as does the reference.
/// </para>
/// <para>
/// PORT-NOTE: in <c>checkSourceElementWorker</c>'s dispatch the arms whose target takes a union — <c>SignatureDeclaration</c>,
/// <c>MethodDeclaration | MethodSignature</c>, <c>UnionOrIntersectionTypeNode</c>, <c>AccessorDeclaration</c>, the three
/// <c>JSDocLink*</c> tags, the three JSDoc accessibility tags, and <c>Block | ModuleBlock</c> — pass the node unchanged,
/// because the port widened those unions to <c>Node</c>; in particular a <c>ModuleBlock</c> is not a <c>Block</c> in the
/// port's class chain, so the reference's <c>node as Block</c> would be an invalid cast at run time. Every other arm
/// keeps the reference's concrete cast.
/// </para>
/// <para>
/// PORT-NOTE: the reference's two "pure predicate" uses that the port cannot write the same way are folded, each with a
/// note at its site: <c>isImportOrExportSpecifier</c> (the two-class switch that also supplies the narrowing for
/// <c>propertyName || name</c>) and <c>canHaveStatements</c> (the five-class switch that supplies
/// <c>parent.statements</c>). Both are named in the slice report as missing <c>utilities.ts</c>/<c>utilitiesPublic.ts</c>
/// members.
/// </para>
/// <para>
/// PORT-NOTE: the state this file reads that a sibling owns is <c>_moduleKind</c> (checker.ts:1530's
/// <c>moduleKind</c>, not yet on <see cref="Checker"/>), the switch targets (the <c>check*</c> members of the type,
/// statement, class and JSDoc bands), the <c>checkGrammar*</c> family, and the four <c>ITypeChecker</c>-faced members
/// this file calls by their TS names (<c>getTypeOnlyAliasDeclaration</c>, <c>markLinkedReferences</c>,
/// <c>collectLinkedAliases</c>). Each is listed with its owner in the slice report.
/// </para>
/// <para>
/// PORT-NOTE: <c>getResolutionModeOverride</c> and <c>isExclusivelyTypeOnlyImportOrExport</c> are <c>program.ts</c>
/// members that checker.ts imports; their port home is <see cref="Program"/> (<c>Program.Host.cs</c>), so this file
/// calls them qualified.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // Module names, aliases and import bindings (checker.ts:48314-48604)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:48314-48334 — <c>getFirstNonModuleExportsIdentifier</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>EntityNameOrEntityNameExpression</c> and its switch has no default, so
    /// an unreachable kind falls out answering <c>undefined</c>; the port takes <see cref="Node"/> and states that
    /// fall-through as <see cref="Debug.Fail{T}"/>. The loop bodies read <c>.left</c>/<c>.expression</c> after TS has
    /// narrowed the kind, which the port states with the cast the kind licenses.
    /// </remarks>
    internal Identifier GetFirstNonModuleExportsIdentifier(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.Identifier:
                return (Identifier)node;
            case SyntaxKind.QualifiedName:
                do
                {
                    node = ((QualifiedName)node).Left!;
                }
                while (node.Kind != SyntaxKind.Identifier);
                return (Identifier)node;
            case SyntaxKind.PropertyAccessExpression:
                do
                {
                    if (Utilities.IsModuleExportsAccessExpression(((PropertyAccessExpression)node).Expression!)
                        && !NodeTests.IsPrivateIdentifier((Node)((PropertyAccessExpression)node).Name!))
                    {
                        return (Identifier)((PropertyAccessExpression)node).Name!;
                    }
                    node = (Node)((PropertyAccessExpression)node).Expression!;
                }
                while (node.Kind != SyntaxKind.Identifier);
                return (Identifier)node;
        }

        return Debug.Fail<Identifier>($"getFirstNonModuleExportsIdentifier: unexpected node kind {node.Kind}");
    }

    /// <summary>ported from: src/compiler/checker.ts:48336-48380 — <c>checkExternalImportOrExportDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>node.attributes</c> is read on the union's two members that carry it (the third,
    /// <c>ImportEqualsDeclaration</c>, does not), which the port states as a switch over the two classes — the same
    /// shape <c>Checker.SymbolSupport.cs</c> uses for a union slot.
    /// </remarks>
    internal bool CheckExternalImportOrExportDeclaration(Node node)
    {
        var moduleName = Utilities.GetExternalModuleName(node);
        if (moduleName is null || Utilities.NodeIsMissing(moduleName))
        {
            // Should be a parse error.
            return false;
        }
        if (!NodeTests.IsStringLiteral(moduleName))
        {
            Error(moduleName, Diagnostics.String_literal_expected);
            return false;
        }
        var inAmbientExternalModule = node.Parent!.Kind == SyntaxKind.ModuleBlock && Utilities.IsAmbientModule(node.Parent!.Parent!);
        if (node.Parent!.Kind != SyntaxKind.SourceFile && !inAmbientExternalModule)
        {
            Error(
                moduleName,
                node.Kind == SyntaxKind.ExportDeclaration
                    ? Diagnostics.Export_declarations_are_not_permitted_in_a_namespace
                    : Diagnostics.Import_declarations_in_a_namespace_cannot_reference_a_module);
            return false;
        }
        if (inAmbientExternalModule && Utilities.IsExternalModuleNameRelative(((LiteralExpression)moduleName).Text))
        {
            // we have already reported errors on top level imports/exports in external module augmentations in checkModuleDeclaration
            // no need to do this again.
            if (!IsTopLevelInExternalModuleAugmentation(node))
            {
                // TypeScript 1.0 spec (April 2013): 12.1.6
                // An ExternalImportDeclaration in an AmbientExternalModuleDeclaration may reference
                // other external modules only through top - level external module names.
                // Relative external module names are not permitted.
                Error(node, Diagnostics.Import_or_export_declaration_in_an_ambient_module_declaration_cannot_reference_module_through_relative_module_name);
                return false;
            }
        }
        var attributes = node switch
        {
            ImportDeclaration importDeclaration => importDeclaration.Attributes,
            ExportDeclaration exportDeclaration => exportDeclaration.Attributes,
            _ => null,
        };
        if (!NodeTests.IsImportEqualsDeclaration(node) && attributes is not null)
        {
            var diagnostic = attributes.Token == SyntaxKind.WithKeyword
                ? Diagnostics.Import_attribute_values_must_be_string_literal_expressions
                : Diagnostics.Import_assertion_values_must_be_string_literal_expressions;
            var hasError = false;
            foreach (var attr in attributes.Elements!)
            {
                if (!NodeTests.IsStringLiteral(attr.Value!))
                {
                    hasError = true;
                    Error(attr.Value!, diagnostic);
                }
            }
            return !hasError;
        }
        return true;
    }

    /// <summary>ported from: src/compiler/checker.ts:48382-48392 — <c>checkModuleExportName</c>.</summary>
    internal void CheckModuleExportName(Node? name, bool allowStringLiteral = true)
    {
        if (name is null || name.Kind != SyntaxKind.StringLiteral)
        {
            return;
        }
        if (!allowStringLiteral)
        {
            GrammarErrorOnNode(name, Diagnostics.Identifier_expected);
        }
        else if (_moduleKind is ModuleKind.ES2015 or ModuleKind.ES2020)
        {
            GrammarErrorOnNode(name, Diagnostics.String_literal_import_and_export_names_are_not_supported_when_the_module_flag_is_set_to_es2015_or_es2020);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:48394-48559 — <c>checkAliasSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>errorNode</c> expression is
    /// <c>isImportOrExportSpecifier(node) ? node.propertyName || node.name : isNamedDeclaration(node) ? node.name : node</c>;
    /// the port states it as a switch over the same three cases (the two specifier classes, then the named-declaration
    /// test), which is the same discrimination and supplies the narrowing the reference's
    /// <c>propertyName || name</c> relies on. The two other union reads — the name of the three
    /// <c>ImportClause | ImportSpecifier | ImportEqualsDeclaration</c> arms and the <c>moduleExportNameTextUnescaped</c>
    /// argument — are casts licensed by the switch arm.
    /// </remarks>
    internal void CheckAliasSymbol(Node node)
    {
        var symbol = GetSymbolOfDeclaration((IDeclaration)node);
        var target = ResolveAlias(symbol);

        if (!ReferenceEquals(target, _unknownSymbol))
        {
            // For external modules, `symbol` represents the local symbol for an alias.
            // This local symbol will merge any other local declarations (excluding other aliases)
            // and symbol.flags will contains combined representation for all merged declaration.
            // Based on symbol.flags we can compute a set of excluded meanings (meaning that resolved alias should not have,
            // otherwise it will conflict with some local declaration). Note that in addition to normal flags we include matching SymbolFlags.Export*
            // in order to prevent collisions with declarations that were exported from the current module (they still contribute to local names).
            symbol = GetMergedSymbol(symbol.ExportSymbol ?? symbol)!;

            // A type-only import/export will already have a grammar error in a JS file, so no need to issue more errors within
            if (Utilities.IsInJSFile(node) && (target.Flags & SymbolFlags.Value) == 0 && !Utilities.IsTypeOnlyImportOrExportDeclaration(node))
            {
                var errorNode = node switch
                {
                    ImportSpecifier importSpecifier => (Node?)((Node?)importSpecifier.PropertyName ?? importSpecifier.Name),
                    ExportSpecifier exportSpecifier => (Node?)((Node?)exportSpecifier.PropertyName ?? exportSpecifier.Name),
                    _ => Utilities.IsNamedDeclaration(node) ? Utilities.GetNameOfDeclaration(node) as Node ?? node : node,
                };

                Debug.Assert(node.Kind != SyntaxKind.NamespaceExport);
                if (node.Kind == SyntaxKind.ExportSpecifier)
                {
                    var diag = Error(errorNode, Diagnostics.Types_cannot_appear_in_export_declarations_in_JavaScript_files);
                    var alreadyExportedSymbol = Utilities.GetSourceFileOfNode(node)?.Symbol?.Exports?.GetValueOrDefault(
                        Utilities.ModuleExportNameTextEscaped(errorNode!));
                    if (ReferenceEquals(alreadyExportedSymbol, target))
                    {
                        // PORT-NOTE: the type argument is explicit because `Core.Find`'s `T` carries a `class`
                        // constraint, under which a cast from `T` to `Node` is not a reference conversion; the
                        // reference's `declarations?.find(isJSDocNode)` is the same first-match walk.
                        var exportingDeclaration = Core.Find<IDeclaration>(alreadyExportedSymbol.Declarations, (d, _) => Utilities.IsJSDocNode((Node)d));
                        if (exportingDeclaration is not null)
                        {
                            Utilities.AddRelatedInfo(
                                diag,
                                Utilities.CreateDiagnosticForNode(
                                    (Node)exportingDeclaration,
                                    Diagnostics._0_is_automatically_exported_here,
                                    Utilities.UnescapeLeadingUnderscores(alreadyExportedSymbol.EscapedName!)));
                        }
                    }
                }
                else
                {
                    Debug.Assert(node.Kind != SyntaxKind.VariableDeclaration);
                    var importDeclaration = Utilities.FindAncestor(node, n => NodeTests.IsImportDeclaration(n) || NodeTests.IsImportEqualsDeclaration(n));
                    var moduleSpecifier = (importDeclaration is not null
                        ? Utilities.TryGetModuleSpecifierFromDeclaration(importDeclaration)?.Text
                        : null) ?? "...";
                    var importedIdentifier = Utilities.UnescapeLeadingUnderscores(NodeTests.IsIdentifier(errorNode!)
                        ? ((Identifier)errorNode!).EscapedText!
                        : symbol.EscapedName!);
                    Error(
                        errorNode!,
                        Diagnostics._0_is_a_type_and_cannot_be_imported_in_JavaScript_files_Use_1_in_a_JSDoc_type_annotation,
                        importedIdentifier,
                        $"import(\"{moduleSpecifier}\").{importedIdentifier}");
                }
                return;
            }

            var targetFlags = GetSymbolFlags(target);
            var excludedMeanings = ((symbol.Flags & (SymbolFlags.Value | SymbolFlags.ExportValue)) != 0 ? SymbolFlags.Value : 0)
                | ((symbol.Flags & SymbolFlags.Type) != 0 ? SymbolFlags.Type : 0)
                | ((symbol.Flags & SymbolFlags.Namespace) != 0 ? SymbolFlags.Namespace : 0);
            if ((targetFlags & excludedMeanings) != 0)
            {
                var message = node.Kind == SyntaxKind.ExportSpecifier
                    ? Diagnostics.Export_declaration_conflicts_with_exported_declaration_of_0
                    : Diagnostics.Import_declaration_conflicts_with_local_declaration_of_0;
                Error(node, message, SymbolToString(symbol));
            }
            else if (node.Kind != SyntaxKind.ExportSpecifier)
            {
                // Look at 'compilerOptions.isolatedModules' and not 'getIsolatedModules(...)' (which considers 'verbatimModuleSyntax')
                // here because 'verbatimModuleSyntax' will already have an error for importing a type without 'import type'.
                var appearsValueyToTranspiler = Js.IsTruthy(_compilerOptions.IsolatedModules)
                    && Utilities.FindAncestor(node, n => Utilities.IsTypeOnlyImportOrExportDeclaration(n)) is null;
                if (appearsValueyToTranspiler && (symbol.Flags & (SymbolFlags.Value | SymbolFlags.ExportValue)) != 0)
                {
                    Error(
                        node,
                        Diagnostics.Import_0_conflicts_with_local_value_so_must_be_declared_with_a_type_only_import_when_isolatedModules_is_enabled,
                        SymbolToString(symbol),
                        _isolatedModulesLikeFlagName);
                }
            }

            if (
                Utilities.GetIsolatedModules(_compilerOptions)
                && !Utilities.IsTypeOnlyImportOrExportDeclaration(node)
                && (node.Flags & NodeFlags.Ambient) == 0)
            {
                var typeOnlyAlias = GetTypeOnlyAliasDeclaration(symbol);
                var isType = (targetFlags & SymbolFlags.Value) == 0;
                if (isType || typeOnlyAlias is not null)
                {
                    switch (node.Kind)
                    {
                        case SyntaxKind.ImportClause:
                        case SyntaxKind.ImportSpecifier:
                        case SyntaxKind.ImportEqualsDeclaration:
                        {
                            if (Js.IsTruthy(_compilerOptions.VerbatimModuleSyntax))
                            {
                                Debug.AssertIsDefined(Utilities.GetNameOfDeclaration(node), "An ImportClause with a symbol should have a name");
                                var message = Js.IsTruthy(_compilerOptions.VerbatimModuleSyntax) && Utilities.IsInternalModuleImportEqualsDeclaration(node)
                                    ? Diagnostics.An_import_alias_cannot_resolve_to_a_type_or_type_only_declaration_when_verbatimModuleSyntax_is_enabled
                                    : isType
                                    ? Diagnostics._0_is_a_type_and_must_be_imported_using_a_type_only_import_when_verbatimModuleSyntax_is_enabled
                                    : Diagnostics._0_resolves_to_a_type_only_declaration_and_must_be_imported_using_a_type_only_import_when_verbatimModuleSyntax_is_enabled;
                                var name = Utilities.ModuleExportNameTextUnescaped((node.Kind == SyntaxKind.ImportSpecifier
                                    ? (Node?)((Node?)((ImportSpecifier)node).PropertyName ?? ((ImportSpecifier)node).Name)
                                    : Utilities.GetNameOfDeclaration(node) as Node)!);
                                AddTypeOnlyDeclarationRelatedInfo(
                                    Error(node, message, name),
                                    isType ? null : typeOnlyAlias,
                                    name);
                            }
                            if (isType && node.Kind == SyntaxKind.ImportEqualsDeclaration && Utilities.HasEffectiveModifier(node, ModifierFlags.Export))
                            {
                                Error(node, Diagnostics.Cannot_use_export_import_on_a_type_or_type_only_namespace_when_0_is_enabled, _isolatedModulesLikeFlagName);
                            }
                            break;
                        }

                        case SyntaxKind.ExportSpecifier:
                        {
                            // Don't allow re-exporting an export that will be elided when `--isolatedModules` is set.
                            // The exception is that `import type { A } from './a'; export { A }` is allowed
                            // because single-file analysis can determine that the export should be dropped.
                            if (Js.IsTruthy(_compilerOptions.VerbatimModuleSyntax)
                                || !ReferenceEquals(Utilities.GetSourceFileOfNode(typeOnlyAlias), Utilities.GetSourceFileOfNode(node)))
                            {
                                var name = Utilities.ModuleExportNameTextUnescaped((Node?)((Node?)((ExportSpecifier)node).PropertyName ?? ((ExportSpecifier)node).Name)!);
                                var diagnostic = isType
                                    ? Error(node, Diagnostics.Re_exporting_a_type_when_0_is_enabled_requires_using_export_type, _isolatedModulesLikeFlagName)
                                    : Error(node, Diagnostics._0_resolves_to_a_type_only_declaration_and_must_be_re_exported_using_a_type_only_re_export_when_1_is_enabled, name, _isolatedModulesLikeFlagName);
                                AddTypeOnlyDeclarationRelatedInfo(diagnostic, isType ? null : typeOnlyAlias, name);
                            }
                            break;
                        }
                    }
                }

                if (
                    Js.IsTruthy(_compilerOptions.VerbatimModuleSyntax)
                    && node.Kind != SyntaxKind.ImportEqualsDeclaration
                    && !Utilities.IsInJSFile(node)
                    && _host.GetEmitModuleFormatOfFile(Utilities.GetSourceFileOfNode(node)) == ModuleKind.CommonJS)
                {
                    Error(node, GetVerbatimModuleSyntaxErrorMessage(node));
                }
                else if (
                    _moduleKind == ModuleKind.Preserve
                    && node.Kind != SyntaxKind.ImportEqualsDeclaration
                    && node.Kind != SyntaxKind.VariableDeclaration
                    && _host.GetEmitModuleFormatOfFile(Utilities.GetSourceFileOfNode(node)) == ModuleKind.CommonJS)
                {
                    // In `--module preserve`, ESM input syntax emits ESM output syntax, but there will be times
                    // when we look at the `impliedNodeFormat` of this file and decide it's CommonJS (i.e., currently,
                    // only if the file extension is .cjs/.cts). To avoid that inconsistency, we disallow ESM syntax
                    // in files that are unambiguously CommonJS in this mode.
                    Error(node, Diagnostics.ECMAScript_module_syntax_is_not_allowed_in_a_CommonJS_module_when_module_is_set_to_preserve);
                }

                if (
                    Js.IsTruthy(_compilerOptions.VerbatimModuleSyntax)
                    && !Utilities.IsTypeOnlyImportOrExportDeclaration(node)
                    && (node.Flags & NodeFlags.Ambient) == 0
                    && (targetFlags & SymbolFlags.ConstEnum) != 0)
                {
                    var constEnumDeclaration = target.ValueDeclaration as EnumDeclaration;
                    var redirect = _host.GetRedirectFromOutput(Utilities.GetSourceFileOfNode(constEnumDeclaration)!.ResolvedPath)?.ResolvedRef;
                    if ((constEnumDeclaration!.Flags & NodeFlags.Ambient) != 0
                        && (redirect is null || !Utilities.ShouldPreserveConstEnums(redirect.CommandLine?.Options)))
                    {
                        Error(node, Diagnostics.Cannot_access_ambient_const_enums_when_0_is_enabled, _isolatedModulesLikeFlagName);
                    }
                }
            }

            if (NodeTests.IsImportSpecifier(node))
            {
                var targetSymbol = ResolveAliasWithDeprecationCheck(symbol, node);
                if (IsDeprecatedSymbol(targetSymbol) && targetSymbol.Declarations is not null)
                {
                    AddDeprecatedSuggestion(node, Core.Map(targetSymbol.Declarations, (d, _) => (Node)d)!, targetSymbol.EscapedName!);
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:48561-48589 — <c>resolveAliasWithDeprecationCheck</c>.</summary>
    internal ISymbol ResolveAliasWithDeprecationCheck(ISymbol symbol, Node location)
    {
        if ((symbol.Flags & SymbolFlags.Alias) == 0 || IsDeprecatedSymbol(symbol) || GetDeclarationOfAliasSymbol(symbol) is null)
        {
            return symbol;
        }

        var targetSymbol = ResolveAlias(symbol);
        if (ReferenceEquals(targetSymbol, _unknownSymbol)) return targetSymbol;

        while ((symbol.Flags & SymbolFlags.Alias) != 0)
        {
            var target = GetImmediateAliasedSymbol(symbol);
            if (target is not null)
            {
                if (ReferenceEquals(target, targetSymbol)) break;
                if (target.Declarations is not null && Core.Length(target.Declarations) != 0)
                {
                    if (IsDeprecatedSymbol(target))
                    {
                        AddDeprecatedSuggestion(location, Core.Map(target.Declarations, (d, _) => (Node)d)!, target.EscapedName!);
                        break;
                    }
                    else
                    {
                        if (ReferenceEquals(symbol, targetSymbol)) break;
                        symbol = target;
                    }
                }
            }
            else
            {
                break;
            }
        }
        return targetSymbol;
    }

    /// <summary>ported from: src/compiler/checker.ts:48591-48604 — <c>checkImportBinding</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>ImportEqualsDeclaration | ImportClause | NamespaceImport |
    /// ImportSpecifier</c>, which has no C# member; the port takes <see cref="Node"/> and reads <c>node.name</c> through
    /// <see cref="INamedDeclaration"/>, the face all four classes share, asking for the <see cref="Identifier"/> each of
    /// the four slots holds. The body's <c>node.propertyName</c> is behind the <c>ImportSpecifier</c> kind test.
    /// </remarks>
    internal void CheckImportBinding(Node node)
    {
        CheckCollisionsForDeclarationName(node, ((INamedDeclaration)node).Name as Identifier);
        CheckAliasSymbol(node);
        if (node.Kind == SyntaxKind.ImportSpecifier)
        {
            var nameNode = (Node?)((Node?)((ImportSpecifier)node).PropertyName ?? ((ImportSpecifier)node).Name);
            CheckModuleExportName(nameNode);
            if (
                Utilities.ModuleExportNameIsDefault(nameNode!)
                && Utilities.GetESModuleInterop(_compilerOptions)
                && _host.GetEmitModuleFormatOfFile(Utilities.GetSourceFileOfNode(node)) < ModuleKind.System)
            {
                CheckExternalEmitHelpers(node, ExternalEmitHelpers.ImportDefault);
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Import attributes (checker.ts:48606-48660)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:48606-48656 — <c>checkImportAttributes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>ImportDeclaration | ExportDeclaration | JSDocImportTag</c>; the port
    /// takes <see cref="Node"/> and reads the three slots the body needs (<c>attributes</c>, <c>moduleSpecifier</c>,
    /// <c>isTypeOnly</c>) through switches over those three classes. <c>getResolutionModeOverride</c>'s optional
    /// callback is <c>Action&lt;Node, DiagnosticMessage&gt;</c> in the port (<c>Program.Host.cs:992</c>), so the
    /// reference's <c>validForTypeAttributes ? grammarErrorOnNode : undefined</c> is a statement lambda calling the
    /// same member.
    /// </remarks>
    internal void CheckImportAttributes(Node declaration)
    {
        var node = declaration switch
        {
            ImportDeclaration importDeclaration => importDeclaration.Attributes,
            ExportDeclaration exportDeclaration => exportDeclaration.Attributes,
            JSDocImportTag jsDocImportTag => jsDocImportTag.Attributes,
            _ => null,
        };
        if (node is not null)
        {
            var importAttributesType = GetGlobalImportAttributesType(/*reportErrors*/ true);
            if (!ReferenceEquals(importAttributesType, _emptyObjectType))
            {
                CheckTypeAssignableTo(GetTypeFromImportAttributes(node), GetNullableType(importAttributesType, TypeFlags.Undefined), node);
            }

            var validForTypeAttributes = Program.IsExclusivelyTypeOnlyImportOrExport(declaration);
            var @override = Program.GetResolutionModeOverride(
                node,
                validForTypeAttributes
                    ? (errorNode, message) => { GrammarErrorOnNode(errorNode, message); }
                    : null);
            var isImportAttributes = node.Token == SyntaxKind.WithKeyword;
            if (validForTypeAttributes && @override is not null)
            {
                return; // Other grammar checks do not apply to type-only imports with resolution mode assertions
            }

            if (!Utilities.ModuleSupportsImportAttributes(_moduleKind))
            {
                GrammarErrorOnNode(
                    node,
                    isImportAttributes
                        ? Diagnostics.Import_attributes_are_only_supported_when_the_module_option_is_set_to_esnext_node18_node20_nodenext_or_preserve
                        : Diagnostics.Import_assertions_are_only_supported_when_the_module_option_is_set_to_esnext_node18_node20_nodenext_or_preserve);
                return;
            }

            if (ModuleKind.Node20 <= _moduleKind && _moduleKind <= ModuleKind.NodeNext && !isImportAttributes)
            {
                GrammarErrorOnFirstToken(node, Diagnostics.Import_assertions_have_been_replaced_by_import_attributes_Use_with_instead_of_assert);
                return;
            }

            if (!isImportAttributes && _compilerOptions.IgnoreDeprecations != "6.0")
            {
                GrammarErrorOnFirstToken(node, Diagnostics.Import_assertions_have_been_replaced_by_import_attributes_Use_with_instead_of_assert);
            }

            var moduleSpecifier = declaration switch
            {
                ImportDeclaration importDeclaration => importDeclaration.ModuleSpecifier,
                ExportDeclaration exportDeclaration => exportDeclaration.ModuleSpecifier,
                JSDocImportTag jsDocImportTag => jsDocImportTag.ModuleSpecifier,
                _ => null,
            };
            if (moduleSpecifier is not null && GetEmitSyntaxForModuleSpecifierExpression(moduleSpecifier) == ModuleKind.CommonJS)
            {
                GrammarErrorOnNode(
                    node,
                    isImportAttributes
                        ? Diagnostics.Import_attributes_are_not_allowed_on_statements_that_compile_to_CommonJS_require_calls
                        : Diagnostics.Import_assertions_are_not_allowed_on_statements_that_compile_to_CommonJS_require_calls);
                return;
            }

            var isTypeOnly = declaration switch
            {
                ImportDeclaration importDeclaration => importDeclaration.ImportClause?.IsTypeOnly == true,
                JSDocImportTag => true,
                _ => ((ExportDeclaration)declaration).IsTypeOnly,
            };
            if (isTypeOnly)
            {
                GrammarErrorOnNode(node, isImportAttributes
                    ? Diagnostics.Import_attributes_cannot_be_used_with_type_only_imports_or_exports
                    : Diagnostics.Import_assertions_cannot_be_used_with_type_only_imports_or_exports);
                return;
            }

            if (@override is not null)
            {
                GrammarErrorOnNode(node, Diagnostics.resolution_mode_can_only_be_set_for_type_only_imports);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:48658-48660 — <c>checkImportAttribute</c>.</summary>
    internal IType CheckImportAttribute(ImportAttribute node)
        => GetRegularTypeOfLiteralType(CheckExpressionCached(node.Value!));

    // ---------------------------------------------------------------------------------------------
    // Import and export declarations (checker.ts:48662-48849)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:48662-48703 — <c>checkImportDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>ModuleKind[moduleKind]</c> (the TS enum's reverse mapping, which names the member) is
    /// <c>Enum.GetName(_moduleKind)!</c>, the form <c>Program.Verify.cs:443</c> already uses. The reference's
    /// <c>forEach(…, checkImportBinding)</c> is a <c>foreach</c>, because the port's <c>Core.ForEach</c> answers a value
    /// and the callback here is void.
    /// </remarks>
    internal void CheckImportDeclaration(ImportDeclaration node)
    {
        if (CheckGrammarModuleElementContext(node, Utilities.IsInJSFile(node)
            ? Diagnostics.An_import_declaration_can_only_be_used_at_the_top_level_of_a_module
            : Diagnostics.An_import_declaration_can_only_be_used_at_the_top_level_of_a_namespace_or_module))
        {
            // If we hit an import declaration in an illegal context, just bail out to avoid cascading errors.
            return;
        }
        if (!CheckGrammarModifiers(node) && node.Modifiers is not null)
        {
            GrammarErrorOnFirstToken(node, Diagnostics.An_import_declaration_cannot_have_modifiers);
        }
        if (CheckExternalImportOrExportDeclaration(node))
        {
            ISymbol? resolvedModule = null;
            var importClause = node.ImportClause;
            if (importClause is not null && !CheckGrammarImportClause(importClause))
            {
                if (importClause.Name is not null)
                {
                    CheckImportBinding(importClause);
                }
                if (importClause.NamedBindings is not null)
                {
                    if (importClause.NamedBindings.Kind == SyntaxKind.NamespaceImport)
                    {
                        CheckImportBinding(importClause.NamedBindings);
                        if (_host.GetEmitModuleFormatOfFile(Utilities.GetSourceFileOfNode(node)) < ModuleKind.System && Utilities.GetESModuleInterop(_compilerOptions))
                        {
                            // import * as ns from "foo";
                            CheckExternalEmitHelpers(node, ExternalEmitHelpers.ImportStar);
                        }
                    }
                    else
                    {
                        resolvedModule = ResolveExternalModuleName(node, node.ModuleSpecifier);
                        if (resolvedModule is not null)
                        {
                            foreach (var element in ((NamedImports)importClause.NamedBindings).Elements!)
                            {
                                CheckImportBinding(element);
                            }
                        }
                    }
                }

                if (!importClause.IsTypeOnly
                    && ModuleKind.Node18 <= _moduleKind && _moduleKind <= ModuleKind.NodeNext
                    && IsOnlyImportableAsDefault(node.ModuleSpecifier!, resolvedModule)
                    && !HasTypeJsonImportAttribute(node))
                {
                    // Import attributes/assertions are not allowed in --module node16, so don't suggest adding one
                    Error(
                        node.ModuleSpecifier!,
                        Diagnostics.Importing_a_JSON_file_into_an_ECMAScript_module_requires_a_type_Colon_json_import_attribute_when_module_is_set_to_0,
                        Enum.GetName(_moduleKind)!);
                }
            }
            else if (_noUncheckedSideEffectImports && importClause is null)
            {
                ResolveExternalModuleName(
                    node,
                    node.ModuleSpecifier,
                    /*ignoreErrors*/ null,
                    Diagnostics.Cannot_find_module_or_type_declarations_for_side_effect_import_of_0);
            }
        }
        CheckImportAttributes(node);
    }

    /// <summary>ported from: src/compiler/checker.ts:48705-48707 — <c>hasTypeJsonImportAttribute</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>tryCast(attr.value, isStringLiteralLike)?.text</c> is <c>Core.TryCast</c> over the same two
    /// types, whose result is the <c>LiteralExpression</c> the text is read from.
    /// </remarks>
    internal bool HasTypeJsonImportAttribute(ImportDeclaration node)
        => node.Attributes is not null
            && Core.Some(
                node.Attributes.Elements,
                attr => Utilities.GetTextOfIdentifierOrLiteral(attr.Name!) == "type"
                    && Core.TryCast<LiteralExpression, Expression>(attr.Value, n => Utilities.IsStringLiteralLike(n))?.Text == "json");

    /// <summary>ported from: src/compiler/checker.ts:48709-48748 — <c>checkImportEqualsDeclaration</c>.</summary>
    internal void CheckImportEqualsDeclaration(ImportEqualsDeclaration node)
    {
        if (CheckGrammarModuleElementContext(node, Utilities.IsInJSFile(node)
            ? Diagnostics.An_import_declaration_can_only_be_used_at_the_top_level_of_a_module
            : Diagnostics.An_import_declaration_can_only_be_used_at_the_top_level_of_a_namespace_or_module))
        {
            // If we hit an import declaration in an illegal context, just bail out to avoid cascading errors.
            return;
        }

        CheckGrammarModifiers(node);
        if (Js.IsTruthy(_compilerOptions.ErasableSyntaxOnly) && (node.Flags & NodeFlags.Ambient) == 0)
        {
            Error(node, Diagnostics.This_syntax_is_not_allowed_when_erasableSyntaxOnly_is_enabled);
        }
        if (Utilities.IsInternalModuleImportEqualsDeclaration(node) || CheckExternalImportOrExportDeclaration(node))
        {
            CheckImportBinding(node);
            MarkLinkedReferences(node, ReferenceHint.ExportImportEquals);
            if (node.ModuleReference!.Kind != SyntaxKind.ExternalModuleReference)
            {
                var target = ResolveAlias(GetSymbolOfDeclaration(node));
                if (!ReferenceEquals(target, _unknownSymbol))
                {
                    var targetFlags = GetSymbolFlags(target);
                    if ((targetFlags & SymbolFlags.Value) != 0)
                    {
                        // Target is a value symbol, check that it is not hidden by a local declaration with the same name
                        var moduleName = Utilities.GetFirstIdentifier(node.ModuleReference!);
                        if ((ResolveEntityName(moduleName, SymbolFlags.Value | SymbolFlags.Namespace)!.Flags & SymbolFlags.Namespace) == 0)
                        {
                            Error(moduleName, Diagnostics.Module_0_is_hidden_by_a_local_declaration_with_the_same_name, Utilities.DeclarationNameToString(moduleName));
                        }
                    }
                    if ((targetFlags & SymbolFlags.Type) != 0)
                    {
                        CheckTypeNameIsReserved(node.Name!, Diagnostics.Import_name_cannot_be_0);
                    }
                }
                if (node.IsTypeOnly)
                {
                    GrammarErrorOnNode(node, Diagnostics.An_import_alias_cannot_use_import_type);
                }
            }
            else
            {
                if (ModuleKind.ES2015 <= _moduleKind && _moduleKind <= ModuleKind.ESNext && !node.IsTypeOnly && (node.Flags & NodeFlags.Ambient) == 0)
                {
                    // Import equals declaration cannot be emitted as ESM
                    GrammarErrorOnNode(node, Diagnostics.Import_assignment_cannot_be_used_when_targeting_ECMAScript_modules_Consider_using_import_Asterisk_as_ns_from_mod_import_a_from_mod_import_d_from_mod_or_another_module_format_instead);
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:48750-48801 — <c>checkExportDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>forEach(node.exportClause.elements, checkExportSpecifier)</c> is a
    /// <c>foreach</c> over the <c>NamedExports</c> the <c>isNamespaceExport</c> test has excluded.
    /// </remarks>
    internal void CheckExportDeclaration(ExportDeclaration node)
    {
        if (CheckGrammarModuleElementContext(node, Utilities.IsInJSFile(node)
            ? Diagnostics.An_export_declaration_can_only_be_used_at_the_top_level_of_a_module
            : Diagnostics.An_export_declaration_can_only_be_used_at_the_top_level_of_a_namespace_or_module))
        {
            // If we hit an export in an illegal context, just bail out to avoid cascading errors.
            return;
        }

        if (!CheckGrammarModifiers(node) && Utilities.HasSyntacticModifiers(node))
        {
            GrammarErrorOnFirstToken(node, Diagnostics.An_export_declaration_cannot_have_modifiers);
        }

        CheckGrammarExportDeclaration(node);
        if (node.ModuleSpecifier is null || CheckExternalImportOrExportDeclaration(node))
        {
            if (node.ExportClause is not null && !NodeTests.IsNamespaceExport(node.ExportClause))
            {
                // export { x, y }
                // export { x, y } from "foo"
                foreach (var element in ((NamedExports)node.ExportClause).Elements!)
                {
                    CheckExportSpecifier(element);
                }
                var inAmbientExternalModule = node.Parent!.Kind == SyntaxKind.ModuleBlock && Utilities.IsAmbientModule(node.Parent!.Parent!);
                var inAmbientNamespaceDeclaration = !inAmbientExternalModule && node.Parent!.Kind == SyntaxKind.ModuleBlock
                    && node.ModuleSpecifier is null && (node.Flags & NodeFlags.Ambient) != 0;
                if (node.Parent!.Kind != SyntaxKind.SourceFile && !inAmbientExternalModule && !inAmbientNamespaceDeclaration)
                {
                    Error(node, Diagnostics.Export_declarations_are_not_permitted_in_a_namespace);
                }
            }
            else
            {
                // export * from "foo"
                // export * as ns from "foo";
                var moduleSymbol = ResolveExternalModuleName(node, node.ModuleSpecifier);
                if (moduleSymbol is not null && HasExportAssignmentSymbol(moduleSymbol))
                {
                    Error(node.ModuleSpecifier!, Diagnostics.Module_0_uses_export_and_cannot_be_used_with_export_Asterisk, SymbolToString(moduleSymbol));
                }
                else if (node.ExportClause is not null)
                {
                    CheckAliasSymbol(node.ExportClause);
                    CheckModuleExportName(((NamespaceExport)node.ExportClause).Name);
                }
                if (_host.GetEmitModuleFormatOfFile(Utilities.GetSourceFileOfNode(node)) < ModuleKind.System)
                {
                    if (node.ExportClause is not null)
                    {
                        // export * as ns from "foo";
                        // For ES2015 modules, we emit it as a pair of `import * as a_1 ...; export { a_1 as ns }` and don't need the helper.
                        // We only use the helper here when in esModuleInterop
                        if (Utilities.GetESModuleInterop(_compilerOptions))
                        {
                            CheckExternalEmitHelpers(node, ExternalEmitHelpers.ImportStar);
                        }
                    }
                    else
                    {
                        // export * from "foo"
                        CheckExternalEmitHelpers(node, ExternalEmitHelpers.ExportStar);
                    }
                }
            }
        }
        CheckImportAttributes(node);
    }

    /// <summary>ported from: src/compiler/checker.ts:48803-48808 — <c>checkGrammarExportDeclaration</c>.</summary>
    internal bool CheckGrammarExportDeclaration(ExportDeclaration node)
    {
        if (node.IsTypeOnly && node.ExportClause?.Kind == SyntaxKind.NamedExports)
        {
            return CheckGrammarNamedImportsOrExports(node.ExportClause);
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:48810-48816 — <c>checkGrammarModuleElementContext</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>Statement</c>, whose port face <see cref="IStatement"/> carries no
    /// <c>Parent</c> (D1 keeps the members on the interfaces that declare them), so the body reads the parent through
    /// the single <see cref="Node"/> cast, as <c>Checker.SymbolSupport.cs</c> does for the same reason.
    /// </remarks>
    internal bool CheckGrammarModuleElementContext(IStatement node, DiagnosticMessage errorMessage)
    {
        var parent = ((Node)node).Parent!;
        var isInAppropriateContext = parent.Kind is SyntaxKind.SourceFile or SyntaxKind.ModuleBlock or SyntaxKind.ModuleDeclaration;
        if (!isInAppropriateContext)
        {
            GrammarErrorOnFirstToken((Node)node, errorMessage);
        }
        return !isInAppropriateContext;
    }

    /// <summary>ported from: src/compiler/checker.ts:48818-48849 — <c>checkExportSpecifier</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS spells <c>node.propertyName || node.name</c> three times; since it is a pure expression the port
    /// states it once as <c>nameNode</c>, and the two <c>Identifier</c>-only uses read it through the cast the
    /// <c>StringLiteral</c> test licenses. The reference's <c>exportedName.kind === SyntaxKind.StringLiteral</c> guard
    /// becomes a null-or-string-literal guard, because the port's nullable slots need the absent case stated.
    /// </remarks>
    internal void CheckExportSpecifier(ExportSpecifier node)
    {
        CheckAliasSymbol(node);
        var nameNode = (Node?)((Node?)node.PropertyName ?? node.Name);
        var hasModuleSpecifier = ((ExportDeclaration)node.Parent!.Parent!).ModuleSpecifier is not null;
        CheckModuleExportName(node.PropertyName, hasModuleSpecifier);
        CheckModuleExportName(node.Name);
        if (Utilities.GetEmitDeclarations(_compilerOptions))
        {
            CollectLinkedAliases((MemberExpression?)node.PropertyName ?? node.Name, /*setVisibility*/ true);
        }
        if (!hasModuleSpecifier)
        {
            var exportedName = nameNode;
            if (exportedName is null || exportedName.Kind == SyntaxKind.StringLiteral)
            {
                return; // Skip for invalid syntax like this: export { "x" }
            }
            // find immediate value referenced by exported name (SymbolFlags.Alias is set so we don't chase down aliases)
            var symbol = ResolveName(
                exportedName,
                ((Identifier)exportedName).EscapedText!,
                SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace | SymbolFlags.Alias,
                /*nameNotFoundMessage*/ null,
                /*isUse*/ true,
                /*excludeGlobals*/ false);
            if (symbol is not null && (ReferenceEquals(symbol, _undefinedSymbol) || ReferenceEquals(symbol, _globalThisSymbol)
                || symbol.Declarations is not null && Utilities.IsGlobalSourceFile(GetDeclarationContainer((Node)symbol.Declarations[0]))))
            {
                Error(exportedName, Diagnostics.Cannot_export_0_Only_local_declarations_can_be_exported_from_a_module, Utilities.IdText(exportedName));
            }
            else
            {
                MarkLinkedReferences(node, ReferenceHint.ExportSpecifier);
            }
        }
        else
        {
            if (
                Utilities.GetESModuleInterop(_compilerOptions)
                && _host.GetEmitModuleFormatOfFile(Utilities.GetSourceFileOfNode(node)) < ModuleKind.System
                && Utilities.ModuleExportNameIsDefault(nameNode!))
            {
                CheckExternalEmitHelpers(node, ExternalEmitHelpers.ImportDefault);
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Export assignments and the module export table (checker.ts:48851-49050)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:48851-48996 — <c>checkExportAssignment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>host.getImpliedNodeFormatForEmit</c>'s port slot is <c>object?</c>
    /// (<c>NodeInterfaces.cs:3716</c>, the generated <c>ModuleKind | undefined</c>), so the reference's
    /// <c>=== ModuleKind.ESNext</c> / <c>!== ModuleKind.CommonJS</c> are stated as constant patterns over the boxed
    /// value — <c>Program.Host.cs:450</c> hands out the boxed <c>ModuleKind</c>, and an absent value is <c>null</c>,
    /// which is neither pattern. The container expression keeps the reference's two steps with the <c>?</c> of the
    /// <c>SourceFile</c> arm, and the four <c>node.expression</c> reads carry the non-null assertion the
    /// <c>ExportAssignment</c> model gives them.
    /// </remarks>
    internal void CheckExportAssignment(ExportAssignment node)
    {
        var illegalContextMessage = Js.IsTruthy(node.IsExportEquals)
            ? Diagnostics.An_export_assignment_must_be_at_the_top_level_of_a_file_or_module_declaration
            : Diagnostics.A_default_export_must_be_at_the_top_level_of_a_file_or_module_declaration;
        if (CheckGrammarModuleElementContext(node, illegalContextMessage))
        {
            // If we hit an export assignment in an illegal context, just bail out to avoid cascading errors.
            return;
        }

        if (Js.IsTruthy(_compilerOptions.ErasableSyntaxOnly) && Js.IsTruthy(node.IsExportEquals) && (node.Flags & NodeFlags.Ambient) == 0)
        {
            Error(node, Diagnostics.This_syntax_is_not_allowed_when_erasableSyntaxOnly_is_enabled);
        }
        var container = node.Parent!.Kind == SyntaxKind.SourceFile ? node.Parent! : node.Parent!.Parent!;
        if (container.Kind == SyntaxKind.ModuleDeclaration && !Utilities.IsAmbientModule(container))
        {
            if (Js.IsTruthy(node.IsExportEquals))
            {
                Error(node, Diagnostics.An_export_assignment_cannot_be_used_in_a_namespace);
            }
            else
            {
                Error(node, Diagnostics.A_default_export_can_only_be_used_in_an_ECMAScript_style_module);
            }

            return;
        }
        // Grammar checking
        if (!CheckGrammarModifiers(node) && Utilities.HasEffectiveModifiers(node))
        {
            GrammarErrorOnFirstToken(node, Diagnostics.An_export_assignment_cannot_have_modifiers);
        }

        var typeAnnotationNode = Utilities.GetEffectiveTypeAnnotationNode(node);
        if (typeAnnotationNode is not null)
        {
            CheckTypeAssignableTo(CheckExpressionCached(node.Expression!), GetTypeFromTypeNode(typeAnnotationNode), node.Expression);
        }

        var isIllegalExportDefaultInCJS = !Js.IsTruthy(node.IsExportEquals)
            && (node.Flags & NodeFlags.Ambient) == 0
            && Js.IsTruthy(_compilerOptions.VerbatimModuleSyntax)
            && _host.GetEmitModuleFormatOfFile(Utilities.GetSourceFileOfNode(node)) == ModuleKind.CommonJS;

        if (node.Expression!.Kind == SyntaxKind.Identifier)
        {
            var id = (Identifier)node.Expression;
            var sym = GetExportSymbolOfValueSymbolIfExported(ResolveEntityName(id, SymbolFlags.All, /*ignoreErrors*/ true, /*dontResolveAlias*/ true, node));
            if (sym is not null)
            {
                MarkLinkedReferences(node, ReferenceHint.ExportAssignment);
                var typeOnlyDeclaration = GetTypeOnlyAliasDeclaration(sym, SymbolFlags.Value);
                // If not a value, we're interpreting the identifier as a type export, along the lines of (`export { Id as default }`)
                if ((GetSymbolFlags(sym) & SymbolFlags.Value) != 0)
                {
                    // However if it is a value, we need to check it's being used correctly
                    CheckExpressionCached(id);
                    if (!isIllegalExportDefaultInCJS && (node.Flags & NodeFlags.Ambient) == 0 && Js.IsTruthy(_compilerOptions.VerbatimModuleSyntax) && typeOnlyDeclaration is not null)
                    {
                        Error(
                            id,
                            Js.IsTruthy(node.IsExportEquals)
                                ? Diagnostics.An_export_declaration_must_reference_a_real_value_when_verbatimModuleSyntax_is_enabled_but_0_resolves_to_a_type_only_declaration
                                : Diagnostics.An_export_default_must_reference_a_real_value_when_verbatimModuleSyntax_is_enabled_but_0_resolves_to_a_type_only_declaration,
                            Utilities.IdText(id));
                    }
                }
                else if (!isIllegalExportDefaultInCJS && (node.Flags & NodeFlags.Ambient) == 0 && Js.IsTruthy(_compilerOptions.VerbatimModuleSyntax))
                {
                    Error(
                        id,
                        Js.IsTruthy(node.IsExportEquals)
                            ? Diagnostics.An_export_declaration_must_reference_a_value_when_verbatimModuleSyntax_is_enabled_but_0_only_refers_to_a_type
                            : Diagnostics.An_export_default_must_reference_a_value_when_verbatimModuleSyntax_is_enabled_but_0_only_refers_to_a_type,
                        Utilities.IdText(id));
                }

                if (!isIllegalExportDefaultInCJS && (node.Flags & NodeFlags.Ambient) == 0 && Utilities.GetIsolatedModules(_compilerOptions) && (sym.Flags & SymbolFlags.Value) == 0)
                {
                    var nonLocalMeanings = GetSymbolFlags(sym, /*excludeTypeOnlyMeanings*/ false, /*excludeLocalMeanings*/ true);
                    if (
                        (sym.Flags & SymbolFlags.Alias) != 0
                        && (nonLocalMeanings & SymbolFlags.Type) != 0
                        && (nonLocalMeanings & SymbolFlags.Value) == 0
                        && (typeOnlyDeclaration is null || !ReferenceEquals(Utilities.GetSourceFileOfNode(typeOnlyDeclaration), Utilities.GetSourceFileOfNode(node))))
                    {
                        // import { SomeType } from "./someModule";
                        // export default SomeType; OR
                        // export = SomeType;
                        Error(
                            id,
                            Js.IsTruthy(node.IsExportEquals)
                                ? Diagnostics._0_resolves_to_a_type_and_must_be_marked_type_only_in_this_file_before_re_exporting_when_1_is_enabled_Consider_using_import_type_where_0_is_imported
                                : Diagnostics._0_resolves_to_a_type_and_must_be_marked_type_only_in_this_file_before_re_exporting_when_1_is_enabled_Consider_using_export_type_0_as_default,
                            Utilities.IdText(id),
                            _isolatedModulesLikeFlagName);
                    }
                    else if (typeOnlyDeclaration is not null && !ReferenceEquals(Utilities.GetSourceFileOfNode(typeOnlyDeclaration), Utilities.GetSourceFileOfNode(node)))
                    {
                        // import { SomeTypeOnlyValue } from "./someModule";
                        // export default SomeTypeOnlyValue; OR
                        // export = SomeTypeOnlyValue;
                        AddTypeOnlyDeclarationRelatedInfo(
                            Error(
                                id,
                                Js.IsTruthy(node.IsExportEquals)
                                    ? Diagnostics._0_resolves_to_a_type_only_declaration_and_must_be_marked_type_only_in_this_file_before_re_exporting_when_1_is_enabled_Consider_using_import_type_where_0_is_imported
                                    : Diagnostics._0_resolves_to_a_type_only_declaration_and_must_be_marked_type_only_in_this_file_before_re_exporting_when_1_is_enabled_Consider_using_export_type_0_as_default,
                                Utilities.IdText(id),
                                _isolatedModulesLikeFlagName),
                            typeOnlyDeclaration,
                            Utilities.IdText(id));
                    }
                }
            }
            else
            {
                CheckExpressionCached(id); // doesn't resolve, check as expression to mark as error
            }

            if (Utilities.GetEmitDeclarations(_compilerOptions))
            {
                CollectLinkedAliases(id, /*setVisibility*/ true);
            }
        }
        else
        {
            CheckExpressionCached(node.Expression!);
        }

        if (isIllegalExportDefaultInCJS)
        {
            Error(node, GetVerbatimModuleSyntaxErrorMessage(node));
        }

        CheckExternalModuleExports(container);

        if ((node.Flags & NodeFlags.Ambient) != 0 && !Utilities.IsEntityNameExpression(node.Expression!))
        {
            GrammarErrorOnNode(node.Expression!, Diagnostics.The_expression_of_an_export_assignment_must_be_an_identifier_or_qualified_name_in_an_ambient_context);
        }

        if (Js.IsTruthy(node.IsExportEquals))
        {
            // Forbid export= in esm implementation files, and esm mode declaration files
            if (
                _moduleKind >= ModuleKind.ES2015
                && _moduleKind != ModuleKind.Preserve
                && ((node.Flags & NodeFlags.Ambient) != 0 && _host.GetImpliedNodeFormatForEmit(Utilities.GetSourceFileOfNode(node)) is ModuleKind.ESNext
                    || (node.Flags & NodeFlags.Ambient) == 0 && _host.GetImpliedNodeFormatForEmit(Utilities.GetSourceFileOfNode(node)) is not ModuleKind.CommonJS))
            {
                // export assignment is not supported in es6 modules
                GrammarErrorOnNode(node, Diagnostics.Export_assignment_cannot_be_used_when_targeting_ECMAScript_modules_Consider_using_export_default_or_another_module_format_instead);
            }
            else if (_moduleKind == ModuleKind.System && (node.Flags & NodeFlags.Ambient) == 0)
            {
                // system modules does not support export assignment
                GrammarErrorOnNode(node, Diagnostics.Export_assignment_is_not_supported_when_module_flag_is_system);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:48998-49000 — <c>hasExportedMembers</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>forEachEntry(moduleSymbol.exports!, (_, id) =&gt; id !== "export=")</c> answers the first
    /// truthy mapped value; the port walks the whole <see cref="SymbolTable"/> (the callback has no side effect) and
    /// answers the same question.
    /// </remarks>
    internal bool HasExportedMembers(ISymbol moduleSymbol)
    {
        var hasExportedMembers = false;
        moduleSymbol.Exports!.ForEach((_, id) =>
        {
            if (id != "export=")
            {
                hasExportedMembers = true;
            }
        });
        return hasExportedMembers;
    }

    /// <summary>ported from: src/compiler/checker.ts:49002-49044 — <c>checkExternalModuleExports</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>Symbollinks.exportsChecked</c> tri-state is read as <c>!= true</c>, its
    /// <c>moduleSymbol.exports!.get("export=")</c> is <c>GetValueOrDefault</c> (the port's "maybe" lookup), and
    /// <c>getExportsOfModule</c>'s <c>Symbol[]</c> face is the internal <c>GetExportsOfModuleTable</c> this slice may
    /// call (its remarks record that separation). The callback destructures <c>{ declarations, flags }</c>, which the
    /// port reads off the symbol inside the same callback.
    /// </remarks>
    internal void CheckExternalModuleExports(Node node)
    {
        var moduleSymbol = GetSymbolOfDeclaration((IDeclaration)node);
        var links = GetSymbolLinks(moduleSymbol);
        if (links.ExportsChecked != true)
        {
            var exportEqualsSymbol = moduleSymbol.Exports?.GetValueOrDefault("export=");
            if (exportEqualsSymbol is not null && HasExportedMembers(moduleSymbol))
            {
                var declaration = (Node?)GetDeclarationOfAliasSymbol(exportEqualsSymbol) ?? (Node?)exportEqualsSymbol.ValueDeclaration;
                if (declaration is not null && !IsTopLevelInExternalModuleAugmentation(declaration) && !Utilities.IsInJSFile(declaration))
                {
                    Error(declaration, Diagnostics.An_export_assignment_cannot_be_used_in_a_module_with_other_exported_elements);
                }
            }
            // Checks for export * conflicts
            GetExportsOfModuleTable(moduleSymbol).ForEach((symbol, id) =>
            {
                if (id == "__export")
                {
                    return;
                }
                // ECMA262: 15.2.1.1 It is a Syntax Error if the ExportedNames of ModuleItemList contains any duplicate entries.
                // (TS Exceptions: namespaces, function overloads, enums, and interfaces)
                if ((symbol.Flags & (SymbolFlags.Namespace | SymbolFlags.Enum)) != 0)
                {
                    return;
                }
                var exportedDeclarationsCount = Core.CountWhere(
                    symbol.Declarations,
                    (d, _) => IsNotOverloadAndNotAccessor(d) && !NodeTests.IsInterfaceDeclaration((Node)d));
                if ((symbol.Flags & SymbolFlags.TypeAlias) != 0 && exportedDeclarationsCount <= 2)
                {
                    // it is legal to merge type alias with other values
                    // so count should be either 1 (just type alias) or 2 (type alias + merged value)
                    return;
                }
                if (exportedDeclarationsCount > 1)
                {
                    if (!IsDuplicatedCommonJSExport(symbol.Declarations))
                    {
                        foreach (var declaration in symbol.Declarations!)
                        {
                            if (IsNotOverload(declaration))
                            {
                                _diagnostics.Add(Utilities.CreateDiagnosticForNode(
                                    (Node)declaration,
                                    Diagnostics.Cannot_redeclare_exported_variable_0,
                                    Utilities.UnescapeLeadingUnderscores(id)));
                            }
                        }
                    }
                }
            });
            links.ExportsChecked = true;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:49046-49050 — <c>isDuplicatedCommonJSExport</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>isAccessExpression(d)</c> narrows for <c>d.expression</c>, which the port states as the switch over
    /// the two concrete classes that <c>Core.Every</c>'s predicate reads (<c>Checker.TypeOfSymbols.cs:717</c> uses the
    /// same shape for the same pair).
    /// </remarks>
    internal bool IsDuplicatedCommonJSExport(IList<IDeclaration>? declarations)
        => declarations is not null
            && declarations.Count > 1
            && Core.Every(declarations, (d, _) =>
            {
                var declarationNode = (Node)d;
                if (!Utilities.IsInJSFile(declarationNode) || !Utilities.IsAccessExpression(declarationNode))
                {
                    return false;
                }

                var expression = declarationNode is PropertyAccessExpression propertyAccess
                    ? propertyAccess.Expression!
                    : ((ElementAccessExpression)declarationNode).Expression!;
                return Utilities.IsExportsIdentifier(expression) || Utilities.IsModuleExportsAccessExpression(expression);
            });

    // ---------------------------------------------------------------------------------------------
    // The source-element walk (checker.ts:49052-49366)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:49052-49062 — <c>checkSourceElement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>Node | undefined</c>; the port's is the interface face <see cref="INode"/>
    /// (see the file remarks), and the body casts to <see cref="Node"/> for the three class-typed members it calls.
    /// </remarks>
    internal void CheckSourceElement(INode? node)
    {
        if (node is not null)
        {
            var saveCurrentNode = _currentNode;
            var saveWithinUnreachableCode = _withinUnreachableCode;
            _currentNode = (Node)node;
            _instantiationCount = 0;
            CheckSourceElementWorker((Node)node);
            _currentNode = saveCurrentNode;
            _withinUnreachableCode = saveWithinUnreachableCode;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:49064-49276 — <c>checkSourceElementWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the JSDoc walk's <c>forEach(node.jsDoc, ({ comment, tags }) =&gt; …)</c> is the pattern test
    /// <c>JsDoc is JSDocArray</c> plus two <c>foreach</c> walks, because <see cref="JSDocArray"/> is the concrete list
    /// the parser builds and <see cref="IJSDocArray"/> carries only the tag cache (<c>Utilities.HasJSDocNodes</c> makes
    /// the same test, and <c>forEach</c> over an absent list visits nothing).
    /// </remarks>
    internal void CheckSourceElementWorker(Node node)
    {
        if ((GetNodeCheckFlags(node) & NodeCheckFlags.PartiallyTypeChecked) != 0)
        {
            return;
        }

        if (Utilities.CanHaveJSDoc(node) && ((IJSDocContainer)node).JsDoc is JSDocArray jsDocs)
        {
            foreach (var jsDoc in jsDocs)
            {
                CheckJSDocCommentWorker(jsDoc.Comment);
                foreach (var tag in jsDoc.Tags ?? [])
                {
                    CheckJSDocCommentWorker(tag.Comment);
                    if (Utilities.IsInJSFile(node))
                    {
                        CheckSourceElement(tag);
                    }
                }
            }
        }

        var kind = node.Kind;
        if (_cancellationToken is not null)
        {
            // Only bother checking on a few construct kinds.  We don't want to be excessively
            // hitting the cancellation token on every node we check.
            switch (kind)
            {
                case SyntaxKind.ModuleDeclaration:
                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.InterfaceDeclaration:
                case SyntaxKind.FunctionDeclaration:
                    _cancellationToken.ThrowIfCancellationRequested();
                    break;
            }
        }

        if (_compilerOptions.AllowUnreachableCode != true && !_withinUnreachableCode)
        {
            if (CheckSourceElementUnreachable(node))
            {
                _withinUnreachableCode = true;
            }
        }

        // If editing this, keep `isSourceElement` in utilities up to date.
        switch (kind)
        {
            case SyntaxKind.TypeParameter:
                CheckTypeParameter((TypeParameterDeclaration)node);
                return;
            case SyntaxKind.Parameter:
                CheckParameter((ParameterDeclaration)node);
                return;
            case SyntaxKind.PropertyDeclaration:
                CheckPropertyDeclaration((PropertyDeclaration)node);
                return;
            case SyntaxKind.PropertySignature:
                CheckPropertySignature((PropertySignature)node);
                return;
            case SyntaxKind.ConstructorType:
            case SyntaxKind.FunctionType:
            case SyntaxKind.CallSignature:
            case SyntaxKind.ConstructSignature:
            case SyntaxKind.IndexSignature:
                CheckSignatureDeclaration(node);
                return;
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.MethodSignature:
                CheckMethodDeclaration(node);
                return;
            case SyntaxKind.ClassStaticBlockDeclaration:
                CheckClassStaticBlockDeclaration((ClassStaticBlockDeclaration)node);
                return;
            case SyntaxKind.Constructor:
                CheckConstructorDeclaration((ConstructorDeclaration)node);
                return;
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
                CheckAccessorDeclaration(node);
                return;
            case SyntaxKind.TypeReference:
                CheckTypeReferenceNode((TypeReferenceNode)node);
                return;
            case SyntaxKind.TypePredicate:
                CheckTypePredicate((TypePredicateNode)node);
                return;
            case SyntaxKind.TypeQuery:
                CheckTypeQuery((TypeQueryNode)node);
                return;
            case SyntaxKind.TypeLiteral:
                CheckTypeLiteral((TypeLiteralNode)node);
                return;
            case SyntaxKind.ArrayType:
                CheckArrayType((ArrayTypeNode)node);
                return;
            case SyntaxKind.TupleType:
                CheckTupleType((TupleTypeNode)node);
                return;
            case SyntaxKind.UnionType:
            case SyntaxKind.IntersectionType:
                CheckUnionOrIntersectionType(node);
                return;
            case SyntaxKind.ParenthesizedType:
            case SyntaxKind.OptionalType:
            case SyntaxKind.RestType:
                // PORT-NOTE: the reference reads the union's `.type` after the kind test; the port reads the same slot
                // from the class the case list names.
                CheckSourceElement(node switch
                {
                    ParenthesizedTypeNode parenthesizedType => parenthesizedType.Type,
                    OptionalTypeNode optionalType => optionalType.Type,
                    _ => ((RestTypeNode)node).Type,
                });
                return;
            case SyntaxKind.ThisType:
                CheckThisType((ThisTypeNode)node);
                return;
            case SyntaxKind.TypeOperator:
                CheckTypeOperator((TypeOperatorNode)node);
                return;
            case SyntaxKind.ConditionalType:
                CheckConditionalType((ConditionalTypeNode)node);
                return;
            case SyntaxKind.InferType:
                CheckInferType((InferTypeNode)node);
                return;
            case SyntaxKind.TemplateLiteralType:
                CheckTemplateLiteralType((TemplateLiteralTypeNode)node);
                return;
            case SyntaxKind.ImportType:
                CheckImportType((ImportTypeNode)node);
                return;
            case SyntaxKind.NamedTupleMember:
                CheckNamedTupleMember((NamedTupleMember)node);
                return;
            case SyntaxKind.JSDocAugmentsTag:
                CheckJSDocAugmentsTag((JSDocAugmentsTag)node);
                return;
            case SyntaxKind.JSDocImplementsTag:
                CheckJSDocImplementsTag((JSDocImplementsTag)node);
                return;
            case SyntaxKind.JSDocTypedefTag:
            case SyntaxKind.JSDocCallbackTag:
            case SyntaxKind.JSDocEnumTag:
                // PORT-NOTE: the reference's `node as JSDocTypedefTag` names one member of the callee's union parameter
                // (JSDocTypedefTag | JSDocCallbackTag | JSDocEnumTag); the port passes the node, because the cast is a
                // checked downcast here and a JSDocCallbackTag is not a JSDocTypedefTag.
                CheckJSDocTypeAliasTag(node);
                return;
            case SyntaxKind.JSDocTemplateTag:
                CheckJSDocTemplateTag((JSDocTemplateTag)node);
                return;
            case SyntaxKind.JSDocTypeTag:
                CheckJSDocTypeTag((JSDocTypeTag)node);
                return;
            case SyntaxKind.JSDocLink:
            case SyntaxKind.JSDocLinkCode:
            case SyntaxKind.JSDocLinkPlain:
                CheckJSDocLinkLikeTag(node);
                return;
            case SyntaxKind.JSDocParameterTag:
                CheckJSDocParameterTag((JSDocParameterTag)node);
                return;
            case SyntaxKind.JSDocPropertyTag:
                CheckJSDocPropertyTag((JSDocPropertyTag)node);
                return;
            case SyntaxKind.JSDocFunctionType:
                CheckJSDocFunctionType((JSDocFunctionType)node);
                goto case SyntaxKind.JSDocNonNullableType;
            case SyntaxKind.JSDocNonNullableType:
            case SyntaxKind.JSDocNullableType:
            case SyntaxKind.JSDocAllType:
            case SyntaxKind.JSDocUnknownType:
            case SyntaxKind.JSDocTypeLiteral:
                CheckJSDocTypeIsInJsFile(node);
                ForEachChildTable.ForEachChild<object?>(node, child =>
                {
                    CheckSourceElement(child);
                    return null;
                });
                return;
            case SyntaxKind.JSDocVariadicType:
                CheckJSDocVariadicType((JSDocVariadicType)node);
                return;
            case SyntaxKind.JSDocTypeExpression:
                CheckSourceElement(((JSDocTypeExpression)node).Type);
                return;
            case SyntaxKind.JSDocPublicTag:
            case SyntaxKind.JSDocProtectedTag:
            case SyntaxKind.JSDocPrivateTag:
                CheckJSDocAccessibilityModifiers(node);
                return;
            case SyntaxKind.JSDocSatisfiesTag:
                CheckJSDocSatisfiesTag((JSDocSatisfiesTag)node);
                return;
            case SyntaxKind.JSDocThisTag:
                CheckJSDocThisTag((JSDocThisTag)node);
                return;
            case SyntaxKind.JSDocImportTag:
                CheckJSDocImportTag((JSDocImportTag)node);
                return;
            case SyntaxKind.IndexedAccessType:
                CheckIndexedAccessType((IndexedAccessTypeNode)node);
                return;
            case SyntaxKind.MappedType:
                CheckMappedType((MappedTypeNode)node);
                return;
            case SyntaxKind.FunctionDeclaration:
                CheckFunctionDeclaration((FunctionDeclaration)node);
                return;
            case SyntaxKind.Block:
            case SyntaxKind.ModuleBlock:
                CheckBlock(node);
                return;
            case SyntaxKind.VariableStatement:
                CheckVariableStatement((VariableStatement)node);
                return;
            case SyntaxKind.ExpressionStatement:
                CheckExpressionStatement((ExpressionStatement)node);
                return;
            case SyntaxKind.IfStatement:
                CheckIfStatement((IfStatement)node);
                return;
            case SyntaxKind.DoStatement:
                CheckDoStatement((DoStatement)node);
                return;
            case SyntaxKind.WhileStatement:
                CheckWhileStatement((WhileStatement)node);
                return;
            case SyntaxKind.ForStatement:
                CheckForStatement((ForStatement)node);
                return;
            case SyntaxKind.ForInStatement:
                CheckForInStatement((ForInStatement)node);
                return;
            case SyntaxKind.ForOfStatement:
                CheckForOfStatement((ForOfStatement)node);
                return;
            case SyntaxKind.ContinueStatement:
            case SyntaxKind.BreakStatement:
                // PORT-NOTE: TS casts to the union `BreakOrContinueStatement`, which has no C# member (the two kinds
                // are unrelated classes), so the node is passed and the callee takes it as `Node`/`IStatement`.
                CheckBreakOrContinueStatement(node);
                return;
            case SyntaxKind.ReturnStatement:
                CheckReturnStatement((ReturnStatement)node);
                return;
            case SyntaxKind.WithStatement:
                CheckWithStatement((WithStatement)node);
                return;
            case SyntaxKind.SwitchStatement:
                CheckSwitchStatement((SwitchStatement)node);
                return;
            case SyntaxKind.LabeledStatement:
                CheckLabeledStatement((LabeledStatement)node);
                return;
            case SyntaxKind.ThrowStatement:
                CheckThrowStatement((ThrowStatement)node);
                return;
            case SyntaxKind.TryStatement:
                CheckTryStatement((TryStatement)node);
                return;
            case SyntaxKind.VariableDeclaration:
                CheckVariableDeclaration((VariableDeclaration)node);
                return;
            case SyntaxKind.BindingElement:
                CheckBindingElement((BindingElement)node);
                return;
            case SyntaxKind.ClassDeclaration:
                CheckClassDeclaration((ClassDeclaration)node);
                return;
            case SyntaxKind.InterfaceDeclaration:
                CheckInterfaceDeclaration((InterfaceDeclaration)node);
                return;
            case SyntaxKind.TypeAliasDeclaration:
                CheckTypeAliasDeclaration((TypeAliasDeclaration)node);
                return;
            case SyntaxKind.EnumDeclaration:
                CheckEnumDeclaration((EnumDeclaration)node);
                return;
            case SyntaxKind.EnumMember:
                CheckEnumMember((EnumMember)node);
                return;
            case SyntaxKind.ModuleDeclaration:
                CheckModuleDeclaration((ModuleDeclaration)node);
                return;
            case SyntaxKind.ImportDeclaration:
                CheckImportDeclaration((ImportDeclaration)node);
                return;
            case SyntaxKind.ImportEqualsDeclaration:
                CheckImportEqualsDeclaration((ImportEqualsDeclaration)node);
                return;
            case SyntaxKind.ExportDeclaration:
                CheckExportDeclaration((ExportDeclaration)node);
                return;
            case SyntaxKind.ExportAssignment:
                CheckExportAssignment((ExportAssignment)node);
                return;
            case SyntaxKind.EmptyStatement:
            case SyntaxKind.DebuggerStatement:
                CheckGrammarStatementInAmbientContext(node);
                return;
            case SyntaxKind.MissingDeclaration:
                CheckMissingDeclaration((MissingDeclaration)node);
                return;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:49278-49335 — <c>checkSourceElementUnreachable</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>canHaveStatements(parent)</c> guard narrows for <c>parent.statements</c>, which the
    /// port states as the five-class switch over the kinds that guard admits — the missing
    /// <c>Utilities.CanHaveStatements</c> (utilities.ts:12431) is named in the slice report — and
    /// <c>statements.indexOf(node as Statement)</c> is <c>List.IndexOf</c>.
    /// </remarks>
    internal bool CheckSourceElementUnreachable(Node node)
    {
        if (!Utilities.IsPotentiallyExecutableNode(node))
        {
            return false;
        }

        if (_reportedUnreachableNodes?.Contains(node) == true)
        {
            return true;
        }

        if (!IsSourceElementUnreachable(node))
        {
            return false;
        }

        var reportedUnreachableNodes = _reportedUnreachableNodes ??= [];
        reportedUnreachableNodes.Add(node);

        var sourceFile = Utilities.GetSourceFileOfNode(node);

        var startNode = node;
        var endNode = node;

        var parent = node.Parent!;
        var statements = parent switch
        {
            Block block => block.Statements,
            ModuleBlock moduleBlock => moduleBlock.Statements,
            SourceFile sourceFileNode => sourceFileNode.Statements,
            CaseClause caseClause => caseClause.Statements,
            DefaultClause defaultClause => defaultClause.Statements,
            _ => null,
        };
        if (statements is not null)
        {
            var offset = statements.IndexOf((IStatement)node);
            if (offset >= 0)
            {
                // Scan backwards to find the first unreachable unreported node;
                // this may happen when producing region diagnostics where not all nodes
                // will have been visited.
                var first = offset;
                for (var i = offset - 1; i >= 0; i--)
                {
                    var prevNode = (Node)statements[i];
                    if (!Utilities.IsPotentiallyExecutableNode(prevNode)
                        || reportedUnreachableNodes.Contains(prevNode)
                        || !IsSourceElementUnreachable(prevNode))
                    {
                        break;
                    }
                    first = i;
                    reportedUnreachableNodes.Add(prevNode);
                }

                var last = offset;
                for (var i = offset + 1; i < statements.Count; i++)
                {
                    var nextNode = (Node)statements[i];
                    if (!Utilities.IsPotentiallyExecutableNode(nextNode) || !IsSourceElementUnreachable(nextNode))
                    {
                        break;
                    }
                    last = i;
                    reportedUnreachableNodes.Add(nextNode);
                }

                startNode = (Node)statements[first];
                endNode = (Node)statements[last];
            }
        }

        var start = Utilities.GetTokenPosOfNode(startNode, sourceFile);
        AddErrorOrSuggestion(
            _compilerOptions.AllowUnreachableCode == false,
            Utilities.CreateFileDiagnostic(sourceFile!, start, endNode.End - start, Diagnostics.Unreachable_code_detected));

        return true;
    }

    /// <summary>ported from: src/compiler/checker.ts:49337-49356 — <c>isSourceElementUnreachable</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference reads <c>node.flowNode</c> after <c>canHaveFlowNode(node)</c>; the port reads the slot
    /// through <see cref="IFlowContainer"/>, the same cast <c>Checker.TypeOfSymbols.cs:192</c> makes for the same pair.
    /// </remarks>
    internal bool IsSourceElementUnreachable(Node node)
    {
        // Precondition: isPotentiallyExecutableNode is true
        if ((node.Flags & NodeFlags.Unreachable) != 0)
        {
            // The binder has determined that this code is unreachable.
            // Ignore const enums unless preserveConstEnums is set.
            switch (node.Kind)
            {
                case SyntaxKind.EnumDeclaration:
                    return !Utilities.IsEnumConst((EnumDeclaration)node) || Utilities.ShouldPreserveConstEnums(_compilerOptions);
                case SyntaxKind.ModuleDeclaration:
                    return IsInstantiatedModule((ModuleDeclaration)node, Utilities.ShouldPreserveConstEnums(_compilerOptions));
                default:
                    return true;
            }
        }
        else if (Utilities.CanHaveFlowNode(node) && ((IFlowContainer)node).FlowNode is not null)
        {
            // For code the binder doesn't know is unreachable, use control flow / types.
            return !IsReachableFlowNode(((IFlowContainer)node).FlowNode!);
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:49358-49366 — <c>checkJSDocCommentWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>string | readonly JSDocComment[] | undefined</c> has no C# member; a comment is an
    /// <c>object?</c> and a comment list is a <c>NodeArray&lt;Node&gt;</c> (<c>Parser/JSDocParser.cs</c>'s remarks), so
    /// <c>isArray(node)</c> is the <see cref="NodeArray{T}"/> test.
    /// </remarks>
    internal void CheckJSDocCommentWorker(object? node)
    {
        if (node is NodeArray<Node> comments)
        {
            foreach (var tag in comments)
            {
                if (Utilities.IsJSDocLinkLike(tag))
                {
                    CheckSourceElement(tag);
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:49368-49391 — <c>checkJSDocTypeIsInJsFile</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads <c>node.postfix</c> and <c>node.type</c> structurally on the two-arm union after its guards;
    /// the port reads each slot from the class the kind test names. <c>append([type, undefinedType], node.postfix ?
    /// undefined : nullType)</c> — whose <c>undefined</c> arm appends nothing (core.ts:926-931) — is stated as the two
    /// list literals <c>GetUnionType</c> takes.
    /// </remarks>
    internal void CheckJSDocTypeIsInJsFile(Node node)
    {
        if (!Utilities.IsInJSFile(node))
        {
            if (NodeTests.IsJSDocNonNullableType(node) || NodeTests.IsJSDocNullableType(node))
            {
                var isNonNullable = NodeTests.IsJSDocNonNullableType(node);
                var token = ScannerUtilities.TokenToString(isNonNullable ? SyntaxKind.ExclamationToken : SyntaxKind.QuestionToken);
                var diagnostic = (isNonNullable ? ((JSDocNonNullableType)node).Postfix : ((JSDocNullableType)node).Postfix)
                    ? Diagnostics._0_at_the_end_of_a_type_is_not_valid_TypeScript_syntax_Did_you_mean_to_write_1
                    : Diagnostics._0_at_the_start_of_a_type_is_not_valid_TypeScript_syntax_Did_you_mean_to_write_1;
                var typeNode = isNonNullable ? ((JSDocNonNullableType)node).Type! : ((JSDocNullableType)node).Type!;
                var type = GetTypeFromTypeNode(typeNode);
                GrammarErrorOnNode(
                    node,
                    diagnostic,
                    token,
                    TypeToString(
                        isNonNullable
                            ? type
                            : !(ReferenceEquals(type, _neverType) || ReferenceEquals(type, _voidType))
                                ? GetUnionType(((JSDocNullableType)node).Postfix ? [type, _undefinedType] : [type, _undefinedType, _nullType])
                                : type));
            }
            else
            {
                GrammarErrorOnNode(node, Diagnostics.JSDoc_types_can_only_be_used_inside_documentation_comments);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:49393-49426 — <c>checkJSDocVariadicType</c>.</summary>
    internal void CheckJSDocVariadicType(JSDocVariadicType node)
    {
        CheckJSDocTypeIsInJsFile(node);
        CheckSourceElement(node.Type);

        // Only legal location is in the *last* parameter tag or last parameter of a JSDoc function.
        var parent = node.Parent!;
        if (NodeTests.IsParameter(parent) && NodeTests.IsJSDocFunctionType(parent.Parent!))
        {
            if (!ReferenceEquals(Core.Last(((JSDocFunctionType)parent.Parent!).Parameters!), parent))
            {
                Error(node, Diagnostics.A_rest_parameter_must_be_last_in_a_parameter_list);
            }
            return;
        }

        if (!NodeTests.IsJSDocTypeExpression(parent))
        {
            Error(node, Diagnostics.JSDoc_may_only_appear_in_the_last_parameter_of_a_signature);
        }

        var paramTag = node.Parent!.Parent!;
        if (!NodeTests.IsJSDocParameterTag(paramTag))
        {
            Error(node, Diagnostics.JSDoc_may_only_appear_in_the_last_parameter_of_a_signature);
            return;
        }

        var param = Utilities.GetParameterSymbolFromJSDoc((JSDocParameterTag)paramTag);
        if (param is null)
        {
            // We will error in `checkJSDocParameterTag`.
            return;
        }

        var host = Utilities.GetHostSignatureFromJSDoc(paramTag);
        if (host is null || !ReferenceEquals(Core.Last(host.Parameters!)?.Symbol, param))
        {
            Error(node, Diagnostics.A_rest_parameter_must_be_last_in_a_parameter_list);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:49428-49460 — <c>getTypeFromJSDocVariadicType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads <c>.symbol</c> on the <c>ParameterDeclaration | JSDocParameterTag</c> union that
    /// <c>lastParamDeclaration</c> holds; the port states the two slots as a switch, because only the class carries it.
    /// The callback arm's <c>(paramTag.parent.parent as unknown as JSDocCallbackTag)</c> is the model's
    /// <c>JSDocCallbackTag.TypeExpression.Parameters</c>, the <c>JSDocParameterTag</c> list production types.ts
    /// declares, and <c>lastOrUndefined</c> is <c>Core.LastOrUndefined</c>.
    /// </remarks>
    internal IType GetTypeFromJSDocVariadicType(JSDocVariadicType node)
    {
        var type = GetTypeFromTypeNode(node.Type!);
        var parent = node.Parent!;
        var paramTag = node.Parent!.Parent!;
        if (NodeTests.IsJSDocTypeExpression(node.Parent!) && NodeTests.IsJSDocParameterTag(paramTag))
        {
            // Else we will add a diagnostic, see `checkJSDocVariadicType`.
            var host = Utilities.GetHostSignatureFromJSDoc(paramTag);
            var isCallbackTag = NodeTests.IsJSDocCallbackTag(paramTag.Parent!.Parent!);
            if (host is not null || isCallbackTag)
            {
                // Only return an array type if the corresponding parameter is marked as a rest parameter, or if there
                // are no parameters.
                var lastParamDeclaration = isCallbackTag
                    ? (Node?)Core.LastOrUndefined(((JSDocCallbackTag)paramTag.Parent!.Parent!).TypeExpression?.Parameters)
                    : Core.LastOrUndefined(host!.Parameters);
                var symbol = Utilities.GetParameterSymbolFromJSDoc((JSDocParameterTag)paramTag);
                var lastParamDeclarationSymbol = lastParamDeclaration switch
                {
                    ParameterDeclaration parameterDeclaration => parameterDeclaration.Symbol,
                    JSDocPropertyLikeTag jsDocPropertyLikeTag => jsDocPropertyLikeTag.Symbol,
                    _ => null,
                };
                if (
                    lastParamDeclaration is null ||
                    symbol is not null && ReferenceEquals(lastParamDeclarationSymbol, symbol) && Utilities.IsRestParameter(lastParamDeclaration)
                )
                {
                    return CreateArrayType(type);
                }
            }
        }
        if (NodeTests.IsParameter(parent) && NodeTests.IsJSDocFunctionType(parent.Parent!))
        {
            return CreateArrayType(type);
        }
        return AddOptionality(type);
    }

    // ---------------------------------------------------------------------------------------------
    // The deferred walk and its queue (checker.ts:49462-49545)
    // ---------------------------------------------------------------------------------------------

    // Function and class expression bodies are checked after all statements in the enclosing body. This is
    // to ensure constructs like the following are permitted:
    //     const foo = function () {
    //        const s = foo();
    //        return "hello";
    //     }
    // Here, performing a full type check of the body of the function expression whilst in the process of
    // determining the type of foo would cause foo to be given type any because of the recursive reference.
    // Delaying the type check of the body ensures foo has been assigned a type.
    /// <summary>ported from: src/compiler/checker.ts:49471-49481 — <c>checkNodeDeferred</c>.</summary>
    internal void CheckNodeDeferred(Node node)
    {
        var enclosingFile = Utilities.GetSourceFileOfNode(node)!;
        var links = GetNodeLinks(enclosingFile);
        if ((links.Flags & NodeCheckFlags.TypeChecked) == 0)
        {
            links.DeferredNodes ??= [];
            links.DeferredNodes.Add(node);
        }
        else
        {
            Debug.Assert(links.DeferredNodes is null, "A type-checked file should have no deferred nodes.");
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:49483-49489 — <c>checkDeferredNodes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>Set.forEach</c> visits elements added while it runs; .NET refuses to enumerate a
    /// <c>HashSet&lt;Node&gt;</c> that is being mutated, so the walk is over a copy of the same set — the only
    /// difference is an element deferred *during* the walk, which the reference would visit in the same pass.
    /// </remarks>
    internal void CheckDeferredNodes(SourceFile context)
    {
        var links = GetNodeLinks(context);
        if (links.DeferredNodes is not null)
        {
            foreach (var deferredNode in links.DeferredNodes.ToList())
            {
                CheckDeferredNode(deferredNode);
            }
        }
        links.DeferredNodes = null;
    }

    /// <summary>ported from: src/compiler/checker.ts:49491-49545 — <c>checkDeferredNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>tracing?.push(…, { kind, pos, end, path })</c> is a <c>TracingArgs</c> of those four values; the
    /// generated <c>TracingValue.From</c> overloads take strings, doubles, bools and arg lists, so the three numeric
    /// slots are the <c>double</c> overload and <c>(node as TracingNode).tracingPath</c> is <c>Node.TracingPath</c>, the
    /// slot the binder fills (tracing.ts:373-375, Model/Node.cs).
    /// <para>
    /// PORT-NOTE: the three arms whose target takes a union (<c>resolveUntypedCall</c>'s <c>CallLikeExpression</c>,
    /// <c>checkFunctionExpressionOrObjectLiteralMethodDeferred</c>'s
    /// <c>ArrowFunction | FunctionExpression | MethodDeclaration</c>, <c>checkAccessorDeclaration</c>'s
    /// <c>AccessorDeclaration</c> and <c>checkAssertionDeferred</c>'s <c>JSDocTypeAssertion | AssertionExpression</c>)
    /// pass the node, because the reference's cast names one member of the callee's own union and the port's classes are
    /// unrelated (<c>MethodSignature</c> is not a <c>FunctionExpression</c>). The concrete arms keep the cast.
    /// </para>
    /// </remarks>
    internal void CheckDeferredNode(Node node)
    {
        Tracing.Current?.Push(
            Tracing.Phase.Check,
            "checkDeferredNode",
            TracingArgs.Of(
                ("kind", TracingValue.From((double)node.Kind)),
                ("pos", TracingValue.From((double)node.Pos)),
                ("end", TracingValue.From((double)node.End)),
                ("path", TracingValue.From(node.TracingPath))));
        var saveCurrentNode = _currentNode;
        _currentNode = node;
        _instantiationCount = 0;
        switch (node.Kind)
        {
            case SyntaxKind.CallExpression:
            case SyntaxKind.NewExpression:
            case SyntaxKind.TaggedTemplateExpression:
            case SyntaxKind.Decorator:
            case SyntaxKind.JsxOpeningElement:
                // These node kinds are deferred checked when overload resolution fails
                // To save on work, we ensure the arguments are checked just once, in
                // a deferred way
                ResolveUntypedCall(node);
                break;
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.MethodSignature:
                CheckFunctionExpressionOrObjectLiteralMethodDeferred(node);
                break;
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
                CheckAccessorDeclaration(node);
                break;
            case SyntaxKind.ClassExpression:
                CheckClassExpressionDeferred((ClassExpression)node);
                break;
            case SyntaxKind.TypeParameter:
                CheckTypeParameterDeferred((TypeParameterDeclaration)node);
                break;
            case SyntaxKind.JsxSelfClosingElement:
                CheckJsxSelfClosingElementDeferred((JsxSelfClosingElement)node);
                break;
            case SyntaxKind.JsxElement:
                CheckJsxElementDeferred((JsxElement)node);
                break;
            case SyntaxKind.TypeAssertionExpression:
            case SyntaxKind.AsExpression:
            case SyntaxKind.ParenthesizedExpression:
                CheckAssertionDeferred(node);
                break;
            case SyntaxKind.VoidExpression:
                CheckExpression(((VoidExpression)node).Expression!);
                break;
            case SyntaxKind.BinaryExpression:
                if (Utilities.IsInstanceOfExpression(node))
                {
                    ResolveUntypedCall(node);
                }
                break;
        }
        _currentNode = saveCurrentNode;
        Tracing.Current?.Pop();
    }
}
