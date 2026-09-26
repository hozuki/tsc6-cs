#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/declarations.ts:313-458 — the symbol-tracker callbacks and the diagnostic
// reporters: `reportExpandoFunctionErrors` (313-325), `reportInferenceFallback` (327-335),
// `handleSymbolAccessibilityError` (337-367), `trackSymbol` (369-373), `reportPrivateInBaseOfClassExpression`
// (375-384), `errorDeclarationNameWithFallback` (386-391), `reportInaccessibleUniqueSymbolError` (393-397),
// `reportCyclicStructureError` (399-403), `reportInaccessibleThisError` (405-409),
// `reportLikelyUnsafeImportRequiredError` (411-420), `reportTruncationError` (422-426), `reportNonlocalAugmentation`
// (428-439), `reportNonSerializableProperty` (441-445) and `transformDeclarationsForJS` (447-458).
internal sealed partial class DeclarationTransformer
{
    /// <summary>ported from: src/compiler/transformers/declarations.ts:313-325 — <c>reportExpandoFunctionErrors</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's parameter is <c>FunctionDeclaration | VariableDeclaration</c>, read by the
    /// two callers that already narrowed; the port takes <see cref="Node"/>. Its
    /// <c>properties.forEach(p =&gt; …)</c> is the loop, and <c>p.valueDeclaration</c> is
    /// <see cref="ISymbol.ValueDeclaration"/>.</remarks>
    private void ReportExpandoFunctionErrors(Node node)
    {
        foreach (var p in _resolver.GetPropertiesOfContainerFunction((IDeclaration)node))
        {
            if (Utilities.IsExpandoPropertyDeclaration(p.ValueDeclaration!))
            {
                var errorTarget = NodeTests.IsBinaryExpression((Node)p.ValueDeclaration!)
                    ? ((BinaryExpression)p.ValueDeclaration!).Left!
                    : (Node)p.ValueDeclaration!;

                _context.AddDiagnostic(Utilities.CreateDiagnosticForNode(
                    errorTarget,
                    Diagnostics.Assigning_properties_to_functions_without_declaring_them_is_not_supported_with_isolatedDeclarations_Add_an_explicit_declaration_for_the_properties_assigned_to_this_function));
            }
        }
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:327-335 — <c>reportInferenceFallback</c>.</summary>
    private void ReportInferenceFallback(Node node)
    {
        if (!_isolatedDeclarations || Utilities.IsSourceFileJS(_currentSourceFile)) return;
        if (!ReferenceEquals(Utilities.GetSourceFileOfNode(node), _currentSourceFile)) return; // Nested error on a declaration in another file - ignore, will be reemitted if file is in the output file set
        if (NodeTests.IsVariableDeclaration(node) && _resolver.IsExpandoFunctionDeclaration(node))
        {
            ReportExpandoFunctionErrors(node);
        }
        else
        {
            _context.AddDiagnostic(_getIsolatedDeclarationError(node));
        }
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:337-367 — <c>handleSymbolAccessibilityError</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the reference's <c>symbolAccessibilityResult.aliasesToMakeVisible</c> is the same list the checker
    /// built (<c>List&lt;Node&gt;</c> is the generated shape of TS's <c>LateVisibilityPaintedStatement[]</c>), so the
    /// first arm shares it and the second pushes into it with <see cref="Core.PushIfUnique{T}"/>.
    /// </para>
    /// <para>
    /// PORT-NOTE: the reference's ternary over <c>errorInfo.typeName</c> selects the argument list; the port states both
    /// argument lists, which is the same three-or-two arguments <c>createDiagnosticForNode</c> receives (TS passes
    /// <c>errorSymbolName!</c>/<c>errorModuleName!</c>, i.e. possibly-undefined values, exactly as the port does).
    /// </para>
    /// </remarks>
    private bool HandleSymbolAccessibilityError(ISymbolAccessibilityResult symbolAccessibilityResult)
    {
        if (symbolAccessibilityResult.Accessibility == SymbolAccessibility.Accessible)
        {
            // Add aliases back onto the possible imports list if they're not there so we can try them again with updated visibility info
            if (symbolAccessibilityResult.AliasesToMakeVisible is not null)
            {
                if (_lateMarkedStatements is null)
                {
                    _lateMarkedStatements = symbolAccessibilityResult.AliasesToMakeVisible;
                }
                else
                {
                    foreach (var @ref in symbolAccessibilityResult.AliasesToMakeVisible)
                    {
                        Core.PushIfUnique(_lateMarkedStatements, @ref);
                    }
                }
            }
            // TODO: Do all these accessibility checks inside/after the first pass in the checker when declarations are enabled, if possible
        }
        // The checker should issue errors on unresolvable names, skip the declaration emit error for using a private/unreachable name for those
        else if (symbolAccessibilityResult.Accessibility != SymbolAccessibility.NotResolved)
        {
            // Report error
            var errorInfo = _getSymbolAccessibilityDiagnostic(symbolAccessibilityResult);
            if (errorInfo is not null)
            {
                if (errorInfo.TypeName is not null)
                {
                    _context.AddDiagnostic(Utilities.CreateDiagnosticForNode(
                        symbolAccessibilityResult.ErrorNode ?? errorInfo.ErrorNode,
                        errorInfo.DiagnosticMessage,
                        Utilities.GetTextOfNode(errorInfo.TypeName),
                        symbolAccessibilityResult.ErrorSymbolName,
                        symbolAccessibilityResult.ErrorModuleName));
                }
                else
                {
                    _context.AddDiagnostic(Utilities.CreateDiagnosticForNode(
                        symbolAccessibilityResult.ErrorNode ?? errorInfo.ErrorNode,
                        errorInfo.DiagnosticMessage,
                        symbolAccessibilityResult.ErrorSymbolName,
                        symbolAccessibilityResult.ErrorModuleName));
                }
                return true;
            }
        }
        return false;
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:369-373 — <c>trackSymbol</c>.</summary>
    private bool TrackSymbol(ISymbol symbol, Node? enclosingDeclaration, SymbolFlags meaning)
    {
        if ((symbol.Flags & SymbolFlags.TypeParameter) != 0) return false;
        var issuedDiagnostic = HandleSymbolAccessibilityError(_resolver.IsSymbolAccessible(symbol, enclosingDeclaration, meaning, /*shouldComputeAliasToMarkVisible*/ true));
        return issuedDiagnostic;
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:375-384 — <c>reportPrivateInBaseOfClassExpression</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>addRelatedInfo(diag, ...related)</c> spreads a conditional array; the port
    /// states the same one-member array as the related-info list of the single <c>AddRelatedInfo</c> call.</remarks>
    private void ReportPrivateInBaseOfClassExpression(string propertyName)
    {
        var errorNode = _errorNameNode ?? _errorFallbackNode;
        if (errorNode is not null)
        {
            var diagnostic = Utilities.CreateDiagnosticForNode(errorNode, Diagnostics.Property_0_of_exported_anonymous_class_type_may_not_be_private_or_protected, propertyName);
            if (NodeTests.IsVariableDeclaration(errorNode.Parent!))
            {
                Utilities.AddRelatedInfo(diagnostic, Utilities.CreateDiagnosticForNode(errorNode, Diagnostics.Add_a_type_annotation_to_the_variable_0, ErrorDeclarationNameWithFallback()));
            }
            _context.AddDiagnostic(diagnostic);
        }
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:386-391 — <c>errorDeclarationNameWithFallback</c>.</summary>
    private string ErrorDeclarationNameWithFallback()
    {
        var fallbackName = _errorFallbackNode is not null ? (Node?)Utilities.GetNameOfDeclaration(_errorFallbackNode) : null;
        if (_errorNameNode is not null)
        {
            return Utilities.DeclarationNameToString(_errorNameNode);
        }
        if (_errorFallbackNode is not null && fallbackName is not null)
        {
            return Utilities.DeclarationNameToString(fallbackName);
        }
        if (_errorFallbackNode is not null && NodeTests.IsExportAssignment(_errorFallbackNode))
        {
            return Js.IsTruthy(((ExportAssignment)_errorFallbackNode).IsExportEquals) ? "export=" : "default";
        }
        return "(Missing)"; // same fallback declarationNameToString uses when node is zero-width (ie, nameless)
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:393-397 — <c>reportInaccessibleUniqueSymbolError</c>.</summary>
    private void ReportInaccessibleUniqueSymbolError()
    {
        var errorNode = _errorNameNode ?? _errorFallbackNode;
        if (errorNode is not null)
        {
            _context.AddDiagnostic(Utilities.CreateDiagnosticForNode(errorNode, Diagnostics.The_inferred_type_of_0_references_an_inaccessible_1_type_A_type_annotation_is_necessary, ErrorDeclarationNameWithFallback(), "unique symbol"));
        }
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:399-403 — <c>reportCyclicStructureError</c>.</summary>
    private void ReportCyclicStructureError()
    {
        var errorNode = _errorNameNode ?? _errorFallbackNode;
        if (errorNode is not null)
        {
            _context.AddDiagnostic(Utilities.CreateDiagnosticForNode(errorNode, Diagnostics.The_inferred_type_of_0_references_a_type_with_a_cyclic_structure_which_cannot_be_trivially_serialized_A_type_annotation_is_necessary, ErrorDeclarationNameWithFallback()));
        }
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:405-409 — <c>reportInaccessibleThisError</c>.</summary>
    private void ReportInaccessibleThisError()
    {
        var errorNode = _errorNameNode ?? _errorFallbackNode;
        if (errorNode is not null)
        {
            _context.AddDiagnostic(Utilities.CreateDiagnosticForNode(errorNode, Diagnostics.The_inferred_type_of_0_references_an_inaccessible_1_type_A_type_annotation_is_necessary, ErrorDeclarationNameWithFallback(), "this"));
        }
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:411-420 — <c>reportLikelyUnsafeImportRequiredError</c>.</summary>
    private void ReportLikelyUnsafeImportRequiredError(string specifier, string? symbolName)
    {
        var errorNode = _errorNameNode ?? _errorFallbackNode;
        if (errorNode is not null)
        {
            if (symbolName is not null)
            {
                _context.AddDiagnostic(Utilities.CreateDiagnosticForNode(errorNode, Diagnostics.The_inferred_type_of_0_cannot_be_named_without_a_reference_to_2_from_1_This_is_likely_not_portable_A_type_annotation_is_necessary, ErrorDeclarationNameWithFallback(), specifier, symbolName));
            }
            else
            {
                _context.AddDiagnostic(Utilities.CreateDiagnosticForNode(errorNode, Diagnostics.The_inferred_type_of_0_cannot_be_named_without_a_reference_to_1_This_is_likely_not_portable_A_type_annotation_is_necessary, ErrorDeclarationNameWithFallback(), specifier));
            }
        }
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:422-426 — <c>reportTruncationError</c>.</summary>
    private void ReportTruncationError()
    {
        var errorNode = _errorNameNode ?? _errorFallbackNode;
        if (errorNode is not null)
        {
            _context.AddDiagnostic(Utilities.CreateDiagnosticForNode(errorNode, Diagnostics.The_inferred_type_of_this_node_exceeds_the_maximum_length_the_compiler_will_serialize_An_explicit_type_annotation_is_needed));
        }
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:428-439 — <c>reportNonlocalAugmentation</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>declarations?.find(…)</c>/<c>filter(…)</c> pair is the
    /// <see cref="Core.Find{T}"/>/<see cref="Core.Filter{T}"/> pair here, over the same <c>IDeclaration</c> elements.</remarks>
    private void ReportNonlocalAugmentation(SourceFile containingFile, ISymbol parentSymbol, ISymbol augmentingSymbol)
    {
        var primaryDeclaration = parentSymbol.Declarations is null
            ? null
            : Core.Find(parentSymbol.Declarations, (IDeclaration d, int _) => ReferenceEquals(Utilities.GetSourceFileOfNode((Node)d), containingFile));
        var augmentingDeclarations = Core.Filter(augmentingSymbol.Declarations, (IDeclaration d) => !ReferenceEquals(Utilities.GetSourceFileOfNode((Node)d), containingFile));
        if (primaryDeclaration is not null && augmentingDeclarations is not null)
        {
            foreach (var augmentations in augmentingDeclarations)
            {
                _context.AddDiagnostic(Utilities.AddRelatedInfo(
                    Utilities.CreateDiagnosticForNode((Node)augmentations, Diagnostics.Declaration_augments_declaration_in_another_file_This_cannot_be_serialized),
                    Utilities.CreateDiagnosticForNode((Node)primaryDeclaration, Diagnostics.This_is_the_declaration_being_augmented_Consider_moving_the_augmenting_declaration_into_the_same_file)));
            }
        }
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:441-445 — <c>reportNonSerializableProperty</c>.</summary>
    private void ReportNonSerializableProperty(string propertyName)
    {
        var errorNode = _errorNameNode ?? _errorFallbackNode;
        if (errorNode is not null)
        {
            _context.AddDiagnostic(Utilities.CreateDiagnosticForNode(errorNode, Diagnostics.The_type_of_this_node_cannot_be_serialized_because_its_property_0_cannot_be_serialized, propertyName));
        }
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:447-458 — <c>transformDeclarationsForJS</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's replacement diagnostic is a conditional object literal; the port's
    /// <c>?</c> selects between the two messages and builds the one <see cref="DeclarationsDiagnostics.SymbolAccessibilityDiagnostic"/>
    /// either way, which is the same object the reference's ternary produces.</remarks>
    private List<IStatement>? TransformDeclarationsForJS(SourceFile sourceFile)
    {
        var oldDiag = _getSymbolAccessibilityDiagnostic;
        _getSymbolAccessibilityDiagnostic = s => s.ErrorNode is not null && DeclarationsDiagnostics.CanProduceDiagnostics(s.ErrorNode)
            ? DeclarationsDiagnostics.CreateGetSymbolAccessibilityDiagnosticForNode(s.ErrorNode)(s)
            : new DeclarationsDiagnostics.SymbolAccessibilityDiagnostic
            {
                DiagnosticMessage = s.ErrorModuleName is not null
                    ? Diagnostics.Declaration_emit_for_this_file_requires_using_private_name_0_from_module_1_An_explicit_type_annotation_may_unblock_declaration_emit
                    : Diagnostics.Declaration_emit_for_this_file_requires_using_private_name_0_An_explicit_type_annotation_may_unblock_declaration_emit,
                ErrorNode = s.ErrorNode ?? sourceFile,
            };
        var result = _resolver.GetDeclarationStatementsForSourceFile(sourceFile, DeclarationsTransform.DeclarationEmitNodeBuilderFlags, DeclarationsTransform.DeclarationEmitInternalNodeBuilderFlags, _symbolTracker);
        _getSymbolAccessibilityDiagnostic = oldDiag;
        return result;
    }
}
