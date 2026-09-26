#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/declarations.ts:606-1004 — the declaration-rewriting helpers the factory's
// closures state: binding-pattern filtering (606-636), `ensureParameter` (638-657),
// `shouldPrintWithInitializer`/`ensureNoInitializer` (659-673), `ensureType` (675-720),
// `isDeclarationAndNotVisible` (722-745), `shouldEmitFunctionProperties` (747-754), `getBindingNameVisible` (756-767),
// `updateParamsList` (769-778), `updateAccessorParamsList` (780-806), `ensureTypeParams` (808-810),
// `isEnclosingDeclaration` (812-821), `checkEntityNameVisibility` (823-826), `preserveJsDoc` (828-833),
// `rewriteModuleSpecifier` (835-847), `transformImportEqualsDeclaration` (849-869), `transformImportDeclaration`
// (871-946), `tryGetResolutionModeOverride` (948-951) and `transformAndReplaceLatePaintedStatements` (953-1004).
internal sealed partial class DeclarationTransformer
{
    /// <summary>ported from: src/compiler/transformers/declarations.ts:606-636 — <c>filterBindingPatternInitializers</c>.</summary>
    /// <remarks>PORT-NOTE: TS's overloaded local <c>visitBindingElement</c> is stated once (its two overloads differ
    /// only in the return type's precision, and the union <c>ArrayBindingElement</c> is <c>Node</c> here); the reference's
    /// <c>elem.dotDotDotToken</c>/<c>elem.propertyName</c>/<c>elem.name</c> reads narrow through <c>BindingElement</c>,
    /// which the kind test above has established.</remarks>
    private Node FilterBindingPatternInitializers(Node name)
    {
        if (name.Kind == SyntaxKind.Identifier)
        {
            return name;
        }
        else
        {
            if (name.Kind == SyntaxKind.ArrayBindingPattern)
            {
                return _factory.UpdateArrayBindingPattern(
                    (ArrayBindingPattern)name,
                    Utilities.VisitNodes(((ArrayBindingPattern)name).Elements, VisitBindingElement, Utilities.IsArrayBindingElement));
            }
            else
            {
                return _factory.UpdateObjectBindingPattern(
                    (ObjectBindingPattern)name,
                    Utilities.VisitNodes(((ObjectBindingPattern)name).Elements, VisitBindingElement, NodeTests.IsBindingElement));
            }
        }

        object? VisitBindingElement(Node elem)
        {
            if (elem.Kind == SyntaxKind.OmittedExpression)
            {
                return elem;
            }
            var element = (BindingElement)elem;
            if (element.PropertyName is not null
                && NodeTests.IsComputedPropertyName(element.PropertyName)
                && Utilities.IsEntityNameExpression(((ComputedPropertyName)element.PropertyName).Expression!))
            {
                CheckEntityNameVisibility(((ComputedPropertyName)element.PropertyName).Expression!, _enclosingDeclaration!);
            }

            return _factory.UpdateBindingElement(
                element,
                element.DotDotDotToken,
                element.PropertyName,
                FilterBindingPatternInitializers(element.Name!),
                /*initializer*/ null);
        }
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:638-657 — <c>ensureParameter</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>oldDiag</c> is declared without an initializer and read only on the path that
    /// assigned it; the port's nullable local takes the same two assignments and the same null-forgiving read.</remarks>
    private ParameterDeclaration EnsureParameter(ParameterDeclaration p, ModifierFlags? modifierMask = null)
    {
        DeclarationsDiagnostics.GetSymbolAccessibilityDiagnostic? oldDiag = null;
        if (!_suppressNewDiagnosticContexts)
        {
            oldDiag = _getSymbolAccessibilityDiagnostic;
            _getSymbolAccessibilityDiagnostic = DeclarationsDiagnostics.CreateGetSymbolAccessibilityDiagnosticForNode(p);
        }
        var newParam = _factory.UpdateParameterDeclaration(
            p,
            DeclarationsTransformSupport.MaskModifiers(_factory, p, modifierMask, /*modifierAdditions*/ null),
            p.DotDotDotToken,
            FilterBindingPatternInitializers(p.Name!),
            _resolver.IsOptionalParameter(p) ? p.QuestionToken ?? (PunctuationToken)_factory.CreateToken(SyntaxKind.QuestionToken) : null,
            EnsureType(p, /*ignorePrivate*/ true), // Ignore private param props, since this type is going straight back into a param
            EnsureNoInitializer(p));
        if (!_suppressNewDiagnosticContexts)
        {
            _getSymbolAccessibilityDiagnostic = oldDiag!;
        }
        return newParam;
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:659-663 — <c>shouldPrintWithInitializer</c>.</summary>
    /// <remarks>PORT-NOTE: TS's guard narrows to <c>CanHaveLiteralInitializer &amp; { initializer: Expression }</c>,
    /// whose C# face is <see cref="DeclarationsTransformSupport.CanHaveLiteralInitializer"/> plus the two tests here;
    /// the <c>initializer</c> read is through the kind switch <see cref="InitializerOf"/>.</remarks>
    private bool ShouldPrintWithInitializer(Node node)
        => DeclarationsTransformSupport.CanHaveLiteralInitializer(node)
            && InitializerOf(node) is not null
            && _resolver.IsLiteralConstDeclaration((Node?)Utilities.GetParseTreeNode(node));

    /// <summary>ported from: src/compiler/transformers/declarations.ts:665-673 — <c>ensureNoInitializer</c>.</summary>
    private Expression? EnsureNoInitializer(Node node)
    {
        if (ShouldPrintWithInitializer(node))
        {
            var unwrappedInitializer = DeclarationsTransformSupport.UnwrapParenthesizedExpression(InitializerOf(node)!);
            if (!DeclarationsTransformSupport.IsPrimitiveLiteralValue(unwrappedInitializer))
            {
                ReportInferenceFallback(node);
            }
            return _resolver.CreateLiteralConstValue(
                (Node?)Utilities.GetParseTreeNode(node, DeclarationsTransformSupport.CanHaveLiteralInitializer),
                _symbolTracker);
        }
        return null;
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:675-720 — <c>ensureType</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the reference's parameter is the union <c>VariableDeclaration | ParameterDeclaration | BindingElement
    /// | PropertyDeclaration | PropertySignature | ExportAssignment | SignatureDeclaration</c>; the port's is
    /// <see cref="Node"/> and its <c>node.type</c> read is the kind switch <see cref="TypeOf"/>. Its
    /// <c>canProduceDiagnostics(node)</c> guard is <see cref="DeclarationsDiagnostics.CanProduceDiagnostics"/>.
    /// </para>
    /// <para>
    /// PORT-NOTE: the reference's <c>Debug.assertNever(node)</c> arm is unreachable for the same reason it is there
    /// (every member of the union is a <c>hasInferredType</c> or a function-like), so the port states that guard; its
    /// <c>visitNode(node.type, visitDeclarationSubtree, isTypeNode)</c> returns the widened <c>Node</c> the generic
    /// walker answers, converted back to the <c>ITypeNode</c> slot the factory takes — the predicate the walker was
    /// given is what establishes it.
    /// </para>
    /// </remarks>
    private ITypeNode? EnsureType(Node node, bool? ignorePrivate = null)
    {
        if (!Js.IsTruthy(ignorePrivate) && Utilities.HasEffectiveModifier(node, ModifierFlags.Private))
        {
            // Private nodes emit no types (except private parameter properties, whose parameter types are actually visible)
            return null;
        }
        if (ShouldPrintWithInitializer(node))
        {
            // Literal const declarations will have an initializer ensured rather than a type
            return null;
        }
        // Should be removed createTypeOfDeclaration will actually now reuse the existing annotation so there is no real need to duplicate type walking
        // Left in for now to minimize diff during syntactic type node builder refactor
        var nodeType = TypeOf(node);
        if (
            !NodeTests.IsExportAssignment(node)
            && !NodeTests.IsBindingElement(node)
            && nodeType is not null
            && (!NodeTests.IsParameter(node) || !_resolver.RequiresAddingImplicitUndefined((ParameterDeclaration)node, _enclosingDeclaration))
        )
        {
            return (ITypeNode?)Utilities.VisitNode((Node?)nodeType, VisitDeclarationSubtree, Utilities.IsTypeNode);
        }

        var oldErrorNameNode = _errorNameNode;
        _errorNameNode = ((INamedDeclaration)node).Name;
        DeclarationsDiagnostics.GetSymbolAccessibilityDiagnostic? oldDiag = null;
        if (!_suppressNewDiagnosticContexts)
        {
            oldDiag = _getSymbolAccessibilityDiagnostic;
            if (DeclarationsDiagnostics.CanProduceDiagnostics(node))
            {
                _getSymbolAccessibilityDiagnostic = DeclarationsDiagnostics.CreateGetSymbolAccessibilityDiagnosticForNode(node);
            }
        }
        ITypeNode? typeNode;
        if (Utilities.HasInferredType(node))
        {
            typeNode = _resolver.CreateTypeOfDeclaration(node, _enclosingDeclaration, DeclarationsTransform.DeclarationEmitNodeBuilderFlags, DeclarationsTransform.DeclarationEmitInternalNodeBuilderFlags, _symbolTracker);
        }
        else if (Utilities.IsFunctionLike(node))
        {
            typeNode = _resolver.CreateReturnTypeOfSignatureDeclaration(node, _enclosingDeclaration, DeclarationsTransform.DeclarationEmitNodeBuilderFlags, DeclarationsTransform.DeclarationEmitInternalNodeBuilderFlags, _symbolTracker);
        }
        else
        {
            Debug.AssertNever(node);
            typeNode = null;
        }

        _errorNameNode = oldErrorNameNode;
        if (!_suppressNewDiagnosticContexts)
        {
            _getSymbolAccessibilityDiagnostic = oldDiag!;
        }
        return typeNode ?? _factory.CreateKeywordTypeNode(SyntaxKind.AnyKeyword);
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:722-745 — <c>isDeclarationAndNotVisible</c>.</summary>
    private bool IsDeclarationAndNotVisible(Node node)
    {
        node = (Node)Utilities.GetParseTreeNode(node)!;
        switch (node.Kind)
        {
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.ModuleDeclaration:
            case SyntaxKind.InterfaceDeclaration:
            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.TypeAliasDeclaration:
            case SyntaxKind.EnumDeclaration:
                return !_resolver.IsDeclarationVisible(node);
            // The following should be doing their own visibility checks based on filtering their members
            case SyntaxKind.VariableDeclaration:
                return !GetBindingNameVisible(node);
            case SyntaxKind.ImportEqualsDeclaration:
            case SyntaxKind.ImportDeclaration:
            case SyntaxKind.ExportDeclaration:
            case SyntaxKind.ExportAssignment:
                return false;
            case SyntaxKind.ClassStaticBlockDeclaration:
                return true;
        }
        return false;
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:747-754 — <c>shouldEmitFunctionProperties</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>declarations?.filter(…)</c> plus <c>indexOf</c> is
    /// <see cref="Core.Filter{T}"/> plus the list's own <c>IndexOf</c>, over the <c>IDeclaration</c> elements the
    /// symbol's <c>Declarations</c> holds.</remarks>
    private static bool ShouldEmitFunctionProperties(FunctionDeclaration input)
    {
        if (input.Body is not null)
        {
            return true;
        }

        var declarations = input.Symbol!.Declarations;
        var overloadSignatures = declarations is null
            ? null
            : Core.Filter(declarations, (IDeclaration decl) => NodeTests.IsFunctionDeclaration((Node)decl) && ((FunctionDeclaration)decl).Body is null);
        return overloadSignatures is null || overloadSignatures.IndexOf(input) == overloadSignatures.Count - 1;
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:756-767 — <c>getBindingNameVisible</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>some(elem.name.elements, getBindingNameVisible)</c> is
    /// <see cref="Core.Some{T}"/> over the same recursive member.</remarks>
    private bool GetBindingNameVisible(Node elem)
    {
        if (NodeTests.IsOmittedExpression(elem))
        {
            return false;
        }
        var name = ((INamedDeclaration)elem).Name!;
        if (Utilities.IsBindingPattern(name))
        {
            // If any child binding pattern element has been marked visible (usually by collect linked aliases), then this is visible
            IEnumerable<Node> elements = name.Kind == SyntaxKind.ArrayBindingPattern
                ? ((ArrayBindingPattern)name).Elements!
                : ((ObjectBindingPattern)name).Elements!;
            return Core.Some([.. elements], (Node element, int _) => GetBindingNameVisible(element));
        }
        else
        {
            return _resolver.IsDeclarationVisible(elem);
        }
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:769-778 — <c>updateParamsList</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>map(params, p =&gt; ensureParameter(p, modifierMask))</c> is stated as the
    /// loop below, because <c>ensureParameter</c> is a member of this class and <see cref="Core.Map{T,TU}"/>'s delegate
    /// would bind it just as well; the `!newParams` arm is kept because it is the reference's own guard.</remarks>
    private NodeArray<ParameterDeclaration> UpdateParamsList(Node node, NodeArray<ParameterDeclaration>? parameters, ModifierFlags? modifierMask = null)
    {
        if (Utilities.HasEffectiveModifier(node, ModifierFlags.Private))
        {
            return _factory.CreateNodeArray<ParameterDeclaration>([], null);
        }
        var newParams = parameters is null ? null : Core.Map(parameters, (ParameterDeclaration p, int _) => EnsureParameter(p, modifierMask));
        if (newParams is null)
        {
            return _factory.CreateNodeArray<ParameterDeclaration>([], null);
        }
        return _factory.CreateNodeArray(newParams, parameters!.HasTrailingComma);
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:780-806 — <c>updateAccessorParamsList</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>append(newParams, newValueParameter)</c> answers the same list when it is
    /// non-null, so the port pushes onto it; TS's <c>emptyArray</c> fallback is a fresh empty <c>NodeArray</c> (§10.2 —
    /// one <c>IList&lt;T&gt;</c> instance cannot serve slots of several element types).</remarks>
    private NodeArray<ParameterDeclaration> UpdateAccessorParamsList(Node input, bool isPrivate)
    {
        List<ParameterDeclaration>? newParams = null;
        if (!isPrivate)
        {
            var thisParameter = Utilities.GetThisParameter(input);
            if (thisParameter is not null)
            {
                newParams = [EnsureParameter(thisParameter)];
            }
        }
        if (Utilities.IsSetAccessor(input))
        {
            ParameterDeclaration? newValueParameter = null;
            if (!isPrivate)
            {
                var valueParameter = Utilities.GetSetAccessorValueParameter((SetAccessorDeclaration)input);
                if (valueParameter is not null)
                {
                    newValueParameter = EnsureParameter(valueParameter);
                }
            }
            if (newValueParameter is null)
            {
                newValueParameter = _factory.CreateParameterDeclaration(
                    /*modifiers*/ null,
                    /*dotDotDotToken*/ null,
                    _factory.CreateIdentifier("value"),
                    /*questionToken*/ null,
                    /*type*/ null,
                    /*initializer*/ null);
            }
            newParams ??= [];
            newParams.Add(newValueParameter);
        }
        return _factory.CreateNodeArray(newParams ?? [], null);
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:808-810 — <c>ensureTypeParams</c>.</summary>
    private NodeArray<TypeParameterDeclaration>? EnsureTypeParams(Node node, NodeArray<TypeParameterDeclaration>? parameters)
        => Utilities.HasEffectiveModifier(node, ModifierFlags.Private)
            ? null
            : Utilities.VisitNodes(parameters, VisitDeclarationSubtree, NodeTests.IsTypeParameterDeclaration);

    /// <summary>ported from: src/compiler/transformers/declarations.ts:812-821 — <c>isEnclosingDeclaration</c>.</summary>
    private static bool IsEnclosingDeclaration(Node node)
        => NodeTests.IsSourceFile(node)
            || NodeTests.IsTypeAliasDeclaration(node)
            || NodeTests.IsModuleDeclaration(node)
            || NodeTests.IsClassDeclaration(node)
            || NodeTests.IsInterfaceDeclaration(node)
            || Utilities.IsFunctionLike(node)
            || NodeTests.IsIndexSignatureDeclaration(node)
            || NodeTests.IsMappedTypeNode(node);

    /// <summary>ported from: src/compiler/transformers/declarations.ts:823-826 — <c>checkEntityNameVisibility</c>.</summary>
    private void CheckEntityNameVisibility(Node entityName, Node enclosingDeclaration)
    {
        var visibilityResult = _resolver.IsEntityNameVisible(entityName, enclosingDeclaration);
        // PORT-NOTE: TS's `isEntityNameVisible` answers `SymbolVisibilityResult` and its caller passes it straight to
        // `handleSymbolAccessibilityError(symbolAccessibilityResult: SymbolAccessibilityResult)`; TS's `errorModuleName`
        // is optional, so the base-shaped object satisfies that type and the pass-through is legal. The port's resolver
        // slot answers the base interface, whose only producer builds the derived `SymbolAccessibilityResult`
        // (Checker.Stringify.cs's `isEntityNameVisible`, the treatment `hasVisibleDeclarations` already records), so the
        // downcast is that same pass-through stated for a model whose interfaces are not structurally compatible.
        HandleSymbolAccessibilityError((ISymbolAccessibilityResult)visibilityResult);
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:828-833 — <c>preserveJsDoc</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>updated.jsDoc = original.jsDoc</c> works for any node because <c>jsDoc</c> is a
    /// structural slot; the port's model declares it on <see cref="IJSDocContainer"/>, and
    /// <see cref="Utilities.HasJSDocNodes"/> is what establishes that both nodes carry one, so the assignment is a
    /// pattern test on the same two nodes (an identical reading — the reference can never write a slot its test did not
    /// find).</remarks>
    private static T PreserveJsDoc<T>(T updated, Node original)
        where T : Node
    {
        if (Utilities.HasJSDocNodes(updated) && Utilities.HasJSDocNodes(original)
            && updated is IJSDocContainer updatedContainer && original is IJSDocContainer originalContainer)
        {
            updatedContainer.JsDoc = originalContainer.JsDoc;
        }
        return EmitNodes.SetCommentRange(updated, EmitNodes.GetCommentRange(original));
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:835-847 — <c>rewriteModuleSpecifier</c>.</summary>
    private Node? RewriteModuleSpecifier(Node parent, Node? input)
    {
        if (input is null) return null; // TODO: GH#18217
        _resultHasExternalModuleIndicator = _resultHasExternalModuleIndicator
            || (parent.Kind != SyntaxKind.ModuleDeclaration && parent.Kind != SyntaxKind.ImportType);
        if (Utilities.IsStringLiteralLike(input))
        {
            if (_isBundledEmit)
            {
                var newName = DeclarationsTransformSupport.GetExternalModuleNameFromDeclaration(_host, _resolver, parent);
                if (newName is not null)
                {
                    return _factory.CreateStringLiteral(newName);
                }
            }
        }
        return input;
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:849-869 — <c>transformImportEqualsDeclaration</c>.</summary>
    private ImportEqualsDeclaration? TransformImportEqualsDeclaration(ImportEqualsDeclaration decl)
    {
        if (!_resolver.IsDeclarationVisible(decl)) return null;
        if (decl.ModuleReference!.Kind == SyntaxKind.ExternalModuleReference)
        {
            // Rewrite external module names if necessary
            var specifier = Utilities.GetExternalModuleImportEqualsDeclarationExpression(decl);
            return _factory.UpdateImportEqualsDeclaration(
                decl,
                decl.Modifiers,
                decl.IsTypeOnly,
                decl.Name,
                _factory.UpdateExternalModuleReference((ExternalModuleReference)decl.ModuleReference, RewriteModuleSpecifier(decl, specifier) as Expression));
        }
        else
        {
            var oldDiag = _getSymbolAccessibilityDiagnostic;
            _getSymbolAccessibilityDiagnostic = DeclarationsDiagnostics.CreateGetSymbolAccessibilityDiagnosticForNode(decl);
            CheckEntityNameVisibility(decl.ModuleReference, _enclosingDeclaration!);
            _getSymbolAccessibilityDiagnostic = oldDiag;
            return decl;
        }
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:871-946 — <c>transformImportDeclaration</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>phaseModifier</c> read compares against <c>SyntaxKind.DeferKeyword</c> and every
    /// <c>importClause.namedBindings</c>/<c>namedBindings.elements</c> read narrows through the union
    /// <c>NamespaceImport | NamedImports</c>, which the two kind tests here establish; the <c>mapDefined</c> over the
    /// named bindings' elements is <see cref="Core.MapDefined{T,TU}"/>.</remarks>
    private ImportDeclaration? TransformImportDeclaration(ImportDeclaration decl)
    {
        if (decl.ImportClause is null)
        {
            // import "mod" - possibly needed for side effects? (global interface patches, module augmentations, etc)
            return _factory.UpdateImportDeclaration(
                decl,
                decl.Modifiers,
                decl.ImportClause,
                RewriteModuleSpecifier(decl, decl.ModuleSpecifier) as Expression,
                TryGetResolutionModeOverride(decl.Attributes));
        }
        var importClause = decl.ImportClause;
        var phaseModifier = importClause.PhaseModifier is SyntaxKind.DeferKeyword ? null : importClause.PhaseModifier;
        // The `importClause` visibility corresponds to the default's visibility.
        var visibleDefaultBinding = importClause.Name is not null && _resolver.IsDeclarationVisible(importClause) ? importClause.Name : null;
        if (importClause.NamedBindings is null)
        {
            // No named bindings (either namespace or list), meaning the import is just default or should be elided
            return visibleDefaultBinding is not null && _factory.UpdateImportDeclaration(
                decl,
                decl.Modifiers,
                _factory.UpdateImportClause(
                    importClause,
                    phaseModifier,
                    visibleDefaultBinding,
                    /*namedBindings*/ null),
                RewriteModuleSpecifier(decl, decl.ModuleSpecifier) as Expression,
                TryGetResolutionModeOverride(decl.Attributes)) is ImportDeclaration updated ? updated : null;
        }
        if (importClause.NamedBindings.Kind == SyntaxKind.NamespaceImport)
        {
            // Namespace import (optionally with visible default)
            var namedBindings = _resolver.IsDeclarationVisible(importClause.NamedBindings) ? importClause.NamedBindings : /*namedBindings*/ null;
            return visibleDefaultBinding is not null || namedBindings is not null ? _factory.UpdateImportDeclaration(
                decl,
                decl.Modifiers,
                _factory.UpdateImportClause(
                    importClause,
                    phaseModifier,
                    visibleDefaultBinding,
                    namedBindings),
                RewriteModuleSpecifier(decl, decl.ModuleSpecifier) as Expression,
                TryGetResolutionModeOverride(decl.Attributes)) : null;
        }
        // Named imports (optionally with visible default)
        var bindingList = Core.MapDefined(
            ((NamedImports)importClause.NamedBindings).Elements!,
            (ImportSpecifier b, int _) => _resolver.IsDeclarationVisible(b) ? b : null);
        if (bindingList.Count > 0 || visibleDefaultBinding is not null)
        {
            return _factory.UpdateImportDeclaration(
                decl,
                decl.Modifiers,
                _factory.UpdateImportClause(
                    importClause,
                    phaseModifier,
                    visibleDefaultBinding,
                    bindingList.Count > 0 ? _factory.UpdateNamedImports((NamedImports)importClause.NamedBindings, bindingList) : null),
                RewriteModuleSpecifier(decl, decl.ModuleSpecifier) as Expression,
                TryGetResolutionModeOverride(decl.Attributes));
        }
        // Augmentation of export depends on import
        if (_resolver.IsImportRequiredByAugmentation(decl))
        {
            if (_isolatedDeclarations)
            {
                _context.AddDiagnostic(Utilities.CreateDiagnosticForNode(decl, Diagnostics.Declaration_emit_for_this_file_requires_preserving_this_import_for_augmentations_This_is_not_supported_with_isolatedDeclarations));
            }
            return _factory.UpdateImportDeclaration(
                decl,
                decl.Modifiers,
                /*importClause*/ null,
                RewriteModuleSpecifier(decl, decl.ModuleSpecifier) as Expression,
                TryGetResolutionModeOverride(decl.Attributes));
        }
        // Nothing visible
        return null;
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:948-951 — <c>tryGetResolutionModeOverride</c>.</summary>
    /// <remarks>PORT-NOTE: <c>getResolutionModeOverride</c> is <see cref="Program.GetResolutionModeOverride"/>, which
    /// answers the <c>ModuleKind?</c> the reference's <c>ResolutionMode</c> union erases to.</remarks>
    private static ImportAttributes? TryGetResolutionModeOverride(ImportAttributes? node)
    {
        var mode = Program.GetResolutionModeOverride(node);
        return node is not null && mode is not null ? node : null;
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:953-1004 — <c>transformAndReplaceLatePaintedStatements</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the reference's <c>visitLateVisibilityMarkedStatements</c> is a nested function here for the same
    /// reason; the two arm tests of its <c>Node | readonly Node[]</c> replacement are the local
    /// <c>AnyReplacement</c> at the end of this member (its own PORT-NOTE records the container question).
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>Debug.fail</c> for a late statement the transformer does not handle is
    /// <see cref="Debug.Fail{T}"/> here (Core/Debug.cs:113-118), which is the same throw in expression position.
    /// </para>
    /// </remarks>
    private NodeArray<IStatement> TransformAndReplaceLatePaintedStatements(NodeArray<IStatement> statements)
    {
        // This is a `while` loop because `handleSymbolAccessibilityError` can see additional import aliases marked as visible during
        // error handling which must now be included in the output and themselves checked for errors.
        // For example:
        // ```
        // module A {
        //   export module Q {}
        //   import B = Q;
        //   import C = B;
        //   export import D = C;
        // }
        // ```
        // In such a scenario, only Q and D are initially visible, but we don't consider imports as private names - instead we say they if they are referenced they must
        // be recorded. So while checking D's visibility we mark C as visible, then we must check C which in turn marks B, completing the chain of
        // dependent imports and allowing a valid declaration file output. Today, this dependent alias marking only happens for internal import aliases.
        while (Js.IsTruthy(Core.Length(_lateMarkedStatements)))
        {
            var i = _lateMarkedStatements![0];
            _lateMarkedStatements.RemoveAt(0);
            if (!Utilities.IsLateVisibilityPaintedStatement(i))
            {
                return Debug.Fail<NodeArray<IStatement>>($"Late replaced statement was found which is not handled by the declaration transformer!: {Debug.FormatSyntaxKind(i.Kind)}");
            }
            var priorNeedsDeclare = _needsDeclare;
            _needsDeclare = i.Parent is not null && NodeTests.IsSourceFile(i.Parent) && !(Parser.IsExternalModule((SourceFile)i.Parent) && _isBundledEmit);
            var result = TransformTopLevelDeclaration(i);
            _needsDeclare = priorNeedsDeclare;
            _lateStatementReplacementMap![TransformationUtilities.GetOriginalNodeId(i)] = result;
        }

        // And lastly, we need to get the final form of all those indetermine import declarations from before and add them to the output list
        // (and remove them from the set to examine for outter declarations)
        return Utilities.VisitNodes(statements, VisitLateVisibilityMarkedStatements, Utilities.IsStatement)!;

        object? VisitLateVisibilityMarkedStatements(Node statement)
        {
            if (Utilities.IsLateVisibilityPaintedStatement(statement))
            {
                var key = TransformationUtilities.GetOriginalNodeId(statement);
                if (_lateStatementReplacementMap!.TryGetValue(key, out var mapped))
                {
                    var result = mapped;
                    _lateStatementReplacementMap.Remove(key);
                    if (result is not null)
                    {
                        if (AnyReplacement(result, n => Utilities.NeedsScopeMarker(n)))
                        {
                            // Top-level declarations in .d.ts files are always considered exported even without a modifier unless there's an export assignment or specifier
                            _needsScopeFixMarker = true;
                        }
                        if (statement.Parent is not null && NodeTests.IsSourceFile(statement.Parent)
                            && AnyReplacement(result, n => Utilities.IsExternalModuleIndicator(n)))
                        {
                            _resultHasExternalModuleIndicator = true;
                        }
                    }
                    return result;
                }
            }
            return statement;
        }

        // PORT-NOTE: the two tests above are the reference's `isArray(result) ? some(result, test) : test(result)` over
        // `Statement | readonly Statement[] | undefined`. The array arm's container is whatever the replacement's
        // `transformTopLevelDeclaration` arm built — an `object?[]` for the export-assignment split
        // (DeclarationsTransform.Statements.cs:466) — so the arm is read with <see cref="Utilities.IsNodeArray"/> (the
        // visitor's own test for a visitor's array answer, Utilities.Visitor.cs:106) and an element loop, which is the
        // general container the `some` call cannot state as one cast.
        bool AnyReplacement(object? result, Func<IStatement, bool> test)
        {
            if (!Utilities.IsNodeArray(result))
            {
                return test((IStatement)result!);
            }
            foreach (var element in (System.Collections.IEnumerable)result!)
            {
                if (test((IStatement)element!)) return true;
            }
            return false;
        }
    }

    /// <summary>The <c>node.type</c> read of <c>ensureType</c> (declarations.ts:692, :703-712).</summary>
    /// <remarks>PORT-NOTE: TS's parameter is the union whose members all carry <c>type</c> (except
    /// <c>ExportAssignment</c>, where the read answers <c>undefined</c>); the model declares the slot on five classes
    /// and on <see cref="ISignatureDeclarationBase"/>, so the shared read is a kind switch — the treatment
    /// <c>EmitResolver.QuestionTokenOf</c> records for the same problem.</remarks>
    private static ITypeNode? TypeOf(Node node) => node switch
    {
        VariableDeclaration declaration => declaration.Type,
        ParameterDeclaration declaration => declaration.Type,
        PropertyDeclaration declaration => declaration.Type,
        PropertySignature signature => signature.Type,
        ISignatureDeclarationBase declaration => declaration.Type,
        _ => null,
    };

    /// <summary>The <c>node.initializer</c> read of <c>shouldPrintWithInitializer</c>/<c>ensureNoInitializer</c>.</summary>
    /// <remarks>PORT-NOTE: the union <c>CanHaveLiteralInitializer</c> (declarations.ts:1890) is the four classes below;
    /// the model declares <c>Initializer</c> on each of them, so the shared read is a kind switch.</remarks>
    private static Expression? InitializerOf(Node node) => node switch
    {
        VariableDeclaration declaration => declaration.Initializer,
        PropertyDeclaration declaration => declaration.Initializer,
        PropertySignature signature => signature.Initializer,
        ParameterDeclaration declaration => declaration.Initializer,
        _ => null,
    };
}
