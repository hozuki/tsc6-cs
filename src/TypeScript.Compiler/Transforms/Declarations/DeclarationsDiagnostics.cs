#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/declarations/diagnostics.ts (813 lines) — the whole module:
// 101-109 (GetSymbolAccessibilityDiagnostic), 104-109 (SymbolAccessibilityDiagnostic), 111-137
// (DeclarationDiagnosticProducing), 138-162 (canProduceDiagnostics), 164-236
// (createGetSymbolAccessibilityDiagnosticForNodeName), 238-604 (createGetSymbolAccessibilityDiagnosticForNode),
// 606-812 (createGetIsolatedDeclarationErrors).
//
// PORT-NOTE (the union types): TS states three unions in this module — `DeclarationDiagnosticProducing` (:111-137),
// `WithIsolatedDeclarationDiagnostic` (:660-677) and the `EntityNameOrEntityNameExpression` the last nested helper
// takes — and none of them has a C# face (their members share only `Node`; the model exposes no interface for a
// union, docs/architecture.md §12). The port states each as the member's `Node` parameter plus the guard that
// established the union — `CanProduceDiagnostics` here and `assertType`/`Debug.type` there, which are erased — the
// same treatment `utilities.ts:2297-2312`'s `isLateVisibilityPaintedStatement` records
// (`Utilities/Utilities.Checker.cs:3226`).
/// <summary>
/// ported from: src/compiler/transformers/declarations/diagnostics.ts:101-812 — the declaration emitter's diagnostic
/// surface: the accessibility-diagnostic callback face, the "<c>canProduceDiagnostics</c>" test, the two
/// diagnostic-context factories <c>transformDeclarations</c> installs around every node it visits, and the
/// isolated-declarations error factory.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (home, §15.15): the reference is a module of exported functions; the port lands them on the class named
/// after the module so the call sites read <c>DeclarationsDiagnostics.…</c>, the treatment
/// <see cref="DeclarationsTransform"/> records for its own module. The two module-level helpers the reference gives no
/// home to — <c>getAllAccessorDeclarations</c> (used only by <c>createAccessorTypeError</c> here) and
/// <c>unwrapParenthesizedExpression</c> (used only by <see cref="DeclarationsTransform"/>) — are declared with the
/// module that reads them, as <c>Utilities/Utilities.Checker.cs:2472</c> already does for the first one.
/// </para>
/// <para>
/// PORT-NOTE (the two diagnostic tables): the reference builds <c>relatedSuggestionByDeclarationKind</c> and
/// <c>errorByDeclarationKind</c> inside <c>createGetIsolatedDeclarationErrors</c>, i.e. once per call; the port states
/// them as static tables because they are constants — the same members in the same order, and the two lookups keep the
/// reference's "absent key answers <c>undefined</c>" reading through <see cref="Lookup"/>.
/// </para>
/// <para>
/// PORT-NOTE (the diagnostic node): the reference's <c>DiagnosticWithLocation</c> is the port's
/// <see cref="IDiagnosticWithLocation"/>, whose producer is <see cref="Utilities.CreateDiagnosticForNode"/>; the
/// reference's objects therefore land in the same slot the transform's <c>addDiagnostic</c> takes.
/// </para>
/// </remarks>
internal static class DeclarationsDiagnostics
{
    /// <summary>ported from: src/compiler/transformers/declarations/diagnostics.ts:101 — <c>GetSymbolAccessibilityDiagnostic</c>.</summary>
    /// <remarks>PORT-NOTE: TS types the parameter as the concrete <c>SymbolAccessibilityResult</c> (types.ts:5679);
    /// the port's producers answer the generated <see cref="ISymbolAccessibilityResult"/> and
    /// <see cref="IEmitResolver.IsSymbolAccessible"/> returns it, so the interface is the slot that type-checks.</remarks>
    internal delegate SymbolAccessibilityDiagnostic? GetSymbolAccessibilityDiagnostic(
        ISymbolAccessibilityResult symbolAccessibilityResult);

    // ported from: src/compiler/transformers/declarations/diagnostics.ts:104-109 — <c>SymbolAccessibilityDiagnostic</c>.
    /// <summary>ported from: src/compiler/transformers/declarations/diagnostics.ts:104-109 — <c>SymbolAccessibilityDiagnostic</c>.</summary>
    /// <remarks>PORT-NOTE: TS declares an interface and every producer writes an object literal; the port has no
    /// anonymous object types, so this is the class those producers construct (the treatment
    /// <c>Checker/Stringify.cs</c>'s <c>SymbolAccessibilityResult</c> records for its own literal). <c>typeName</c> is
    /// <c>DeclarationName | QualifiedName</c> there, both of which are <see cref="Node"/> here, so the slot is the base
    /// class.</remarks>
    internal sealed class SymbolAccessibilityDiagnostic
    {
        /// <summary>ported from: src/compiler/transformers/declarations/diagnostics.ts:105 — <c>errorNode</c>.</summary>
        public required Node ErrorNode { get; set; }

        /// <summary>ported from: src/compiler/transformers/declarations/diagnostics.ts:106 — <c>diagnosticMessage</c>.</summary>
        public required DiagnosticMessage DiagnosticMessage { get; set; }

        /// <summary>ported from: src/compiler/transformers/declarations/diagnostics.ts:107 — <c>typeName</c>.</summary>
        public Node? TypeName { get; set; }
    }

    /// <summary>ported from: src/compiler/transformers/declarations/diagnostics.ts:138-162 — <c>canProduceDiagnostics</c>.</summary>
    public static bool CanProduceDiagnostics(Node node)
        => NodeTests.IsVariableDeclaration(node)
            || NodeTests.IsPropertyDeclaration(node)
            || NodeTests.IsPropertySignature(node)
            || NodeTests.IsBindingElement(node)
            || Utilities.IsSetAccessor(node)
            || Utilities.IsGetAccessor(node)
            || NodeTests.IsConstructSignatureDeclaration(node)
            || NodeTests.IsCallSignatureDeclaration(node)
            || NodeTests.IsMethodDeclaration(node)
            || NodeTests.IsMethodSignature(node)
            || NodeTests.IsFunctionDeclaration(node)
            || NodeTests.IsParameter(node)
            || NodeTests.IsTypeParameterDeclaration(node)
            || NodeTests.IsExpressionWithTypeArguments(node)
            || NodeTests.IsImportEqualsDeclaration(node)
            || NodeTests.IsTypeAliasDeclaration(node)
            || NodeTests.IsConstructorDeclaration(node)
            || NodeTests.IsIndexSignatureDeclaration(node)
            || NodeTests.IsPropertyAccessExpression(node)
            || NodeTests.IsElementAccessExpression(node)
            || NodeTests.IsBinaryExpression(node)
            || Utilities.IsJSDocTypeAlias(node);

    /// <summary>ported from: src/compiler/transformers/declarations/diagnostics.ts:164-236 — <c>createGetSymbolAccessibilityDiagnosticForNodeName</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's `node.name`/`node.parent` reads narrow through the union's members
    /// (<c>NamedDeclaration</c>, <c>ClassDeclaration</c>); the port reads them through
    /// <see cref="INamedDeclaration.Name"/> and the class's node test, which state the same slots.</remarks>
    public static GetSymbolAccessibilityDiagnostic? CreateGetSymbolAccessibilityDiagnosticForNodeName(Node node)
    {
        if (Utilities.IsSetAccessor(node) || Utilities.IsGetAccessor(node))
        {
            return GetAccessorNameVisibilityError;
        }
        else if (NodeTests.IsMethodSignature(node) || NodeTests.IsMethodDeclaration(node))
        {
            return GetMethodNameVisibilityError;
        }
        else
        {
            return CreateGetSymbolAccessibilityDiagnosticForNode(node);
        }

        SymbolAccessibilityDiagnostic? GetAccessorNameVisibilityError(ISymbolAccessibilityResult symbolAccessibilityResult)
        {
            var diagnosticMessage = GetAccessorNameVisibilityDiagnosticMessage(symbolAccessibilityResult);
            return diagnosticMessage is not null ? new SymbolAccessibilityDiagnostic
            {
                DiagnosticMessage = diagnosticMessage,
                ErrorNode = node,
                TypeName = ((INamedDeclaration)node).Name,
            } : null;
        }

        DiagnosticMessage? GetAccessorNameVisibilityDiagnosticMessage(ISymbolAccessibilityResult symbolAccessibilityResult)
        {
            if (Utilities.IsStatic(node))
            {
                return symbolAccessibilityResult.ErrorModuleName is not null
                    ? symbolAccessibilityResult.Accessibility == SymbolAccessibility.CannotBeNamed
                        ? Diagnostics.Public_static_property_0_of_exported_class_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named
                        : Diagnostics.Public_static_property_0_of_exported_class_has_or_is_using_name_1_from_private_module_2
                    : Diagnostics.Public_static_property_0_of_exported_class_has_or_is_using_private_name_1;
            }
            else if (node.Parent!.Kind == SyntaxKind.ClassDeclaration)
            {
                return symbolAccessibilityResult.ErrorModuleName is not null
                    ? symbolAccessibilityResult.Accessibility == SymbolAccessibility.CannotBeNamed
                        ? Diagnostics.Public_property_0_of_exported_class_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named
                        : Diagnostics.Public_property_0_of_exported_class_has_or_is_using_name_1_from_private_module_2
                    : Diagnostics.Public_property_0_of_exported_class_has_or_is_using_private_name_1;
            }
            else
            {
                return symbolAccessibilityResult.ErrorModuleName is not null
                    ? Diagnostics.Property_0_of_exported_interface_has_or_is_using_name_1_from_private_module_2
                    : Diagnostics.Property_0_of_exported_interface_has_or_is_using_private_name_1;
            }
        }

        SymbolAccessibilityDiagnostic? GetMethodNameVisibilityError(ISymbolAccessibilityResult symbolAccessibilityResult)
        {
            var diagnosticMessage = GetMethodNameVisibilityDiagnosticMessage(symbolAccessibilityResult);
            return diagnosticMessage is not null ? new SymbolAccessibilityDiagnostic
            {
                DiagnosticMessage = diagnosticMessage,
                ErrorNode = node,
                TypeName = ((INamedDeclaration)node).Name,
            } : null;
        }

        DiagnosticMessage GetMethodNameVisibilityDiagnosticMessage(ISymbolAccessibilityResult symbolAccessibilityResult)
        {
            if (Utilities.IsStatic(node))
            {
                return symbolAccessibilityResult.ErrorModuleName is not null
                    ? symbolAccessibilityResult.Accessibility == SymbolAccessibility.CannotBeNamed
                        ? Diagnostics.Public_static_method_0_of_exported_class_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named
                        : Diagnostics.Public_static_method_0_of_exported_class_has_or_is_using_name_1_from_private_module_2
                    : Diagnostics.Public_static_method_0_of_exported_class_has_or_is_using_private_name_1;
            }
            else if (node.Parent!.Kind == SyntaxKind.ClassDeclaration)
            {
                return symbolAccessibilityResult.ErrorModuleName is not null
                    ? symbolAccessibilityResult.Accessibility == SymbolAccessibility.CannotBeNamed
                        ? Diagnostics.Public_method_0_of_exported_class_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named
                        : Diagnostics.Public_method_0_of_exported_class_has_or_is_using_name_1_from_private_module_2
                    : Diagnostics.Public_method_0_of_exported_class_has_or_is_using_private_name_1;
            }
            else
            {
                return symbolAccessibilityResult.ErrorModuleName is not null
                    ? Diagnostics.Method_0_of_exported_interface_has_or_is_using_name_1_from_private_module_2
                    : Diagnostics.Method_0_of_exported_interface_has_or_is_using_private_name_1;
            }
        }
    }

    /// <summary>ported from: src/compiler/transformers/declarations/diagnostics.ts:238-604 — <c>createGetSymbolAccessibilityDiagnosticForNode</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>Debug.assertNever(node, …)</c> arm answers a message and then falls
    /// through to the nested functions' declarations; the port's <see cref="Debug.AssertNever"/> throws, which is what
    /// the reference's own guard does in a debug build. Its <c>node.parent.kind</c>/<c>node.name</c> reads narrow
    /// through the union, which the port states with <see cref="Node.Parent"/> and
    /// <see cref="INamedDeclaration.Name"/>.</remarks>
    public static GetSymbolAccessibilityDiagnostic CreateGetSymbolAccessibilityDiagnosticForNode(Node node)
    {
        if (NodeTests.IsVariableDeclaration(node)
            || NodeTests.IsPropertyDeclaration(node)
            || NodeTests.IsPropertySignature(node)
            || NodeTests.IsPropertyAccessExpression(node)
            || NodeTests.IsElementAccessExpression(node)
            || NodeTests.IsBinaryExpression(node)
            || NodeTests.IsBindingElement(node)
            || NodeTests.IsConstructorDeclaration(node))
        {
            return GetVariableDeclarationTypeVisibilityError;
        }
        else if (Utilities.IsSetAccessor(node) || Utilities.IsGetAccessor(node))
        {
            return GetAccessorDeclarationTypeVisibilityError;
        }
        else if (NodeTests.IsConstructSignatureDeclaration(node)
            || NodeTests.IsCallSignatureDeclaration(node)
            || NodeTests.IsMethodDeclaration(node)
            || NodeTests.IsMethodSignature(node)
            || NodeTests.IsFunctionDeclaration(node)
            || NodeTests.IsIndexSignatureDeclaration(node))
        {
            return GetReturnTypeVisibilityError;
        }
        else if (NodeTests.IsParameter(node))
        {
            if (Utilities.IsParameterPropertyDeclaration(node, node.Parent!) && Utilities.HasSyntacticModifier(node.Parent!, ModifierFlags.Private))
            {
                return GetVariableDeclarationTypeVisibilityError;
            }
            return GetParameterDeclarationTypeVisibilityError;
        }
        else if (NodeTests.IsTypeParameterDeclaration(node))
        {
            return GetTypeParameterConstraintVisibilityError;
        }
        else if (NodeTests.IsExpressionWithTypeArguments(node))
        {
            return GetHeritageClauseVisibilityError;
        }
        else if (NodeTests.IsImportEqualsDeclaration(node))
        {
            return GetImportEntityNameVisibilityError;
        }
        else if (NodeTests.IsTypeAliasDeclaration(node) || Utilities.IsJSDocTypeAlias(node))
        {
            return GetTypeAliasDeclarationVisibilityError;
        }
        else
        {
            // PORT-NOTE: the reference returns `Debug.assertNever(node, ...)`, which is `never`; the port returns
            // `Debug.Fail<T>` (debug.ts:196-204), the same throw in expression position.
            return Debug.Fail<GetSymbolAccessibilityDiagnostic>($"Attempted to set a declaration diagnostic context for unhandled node kind: {Debug.FormatSyntaxKind(node.Kind)}");
        }

        DiagnosticMessage? GetVariableDeclarationTypeVisibilityDiagnosticMessage(ISymbolAccessibilityResult symbolAccessibilityResult)
        {
            if (node.Kind == SyntaxKind.VariableDeclaration || node.Kind == SyntaxKind.BindingElement)
            {
                return symbolAccessibilityResult.ErrorModuleName is not null
                    ? symbolAccessibilityResult.Accessibility == SymbolAccessibility.CannotBeNamed
                        ? Diagnostics.Exported_variable_0_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named
                        : Diagnostics.Exported_variable_0_has_or_is_using_name_1_from_private_module_2
                    : Diagnostics.Exported_variable_0_has_or_is_using_private_name_1;
            }
            // This check is to ensure we don't report error on constructor parameter property as that error would be reported during parameter emit
            // The only exception here is if the constructor was marked as private. we are not emitting the constructor parameters at all.
            else if (
                node.Kind == SyntaxKind.PropertyDeclaration || node.Kind == SyntaxKind.PropertyAccessExpression || node.Kind == SyntaxKind.ElementAccessExpression || node.Kind == SyntaxKind.BinaryExpression || node.Kind == SyntaxKind.PropertySignature ||
                (node.Kind == SyntaxKind.Parameter && Utilities.HasSyntacticModifier(node.Parent!, ModifierFlags.Private))
            )
            {
                // TODO(jfreeman): Deal with computed properties in error reporting.
                if (Utilities.IsStatic(node))
                {
                    return symbolAccessibilityResult.ErrorModuleName is not null
                        ? symbolAccessibilityResult.Accessibility == SymbolAccessibility.CannotBeNamed
                            ? Diagnostics.Public_static_property_0_of_exported_class_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named
                            : Diagnostics.Public_static_property_0_of_exported_class_has_or_is_using_name_1_from_private_module_2
                        : Diagnostics.Public_static_property_0_of_exported_class_has_or_is_using_private_name_1;
                }
                else if (node.Parent!.Kind == SyntaxKind.ClassDeclaration || node.Kind == SyntaxKind.Parameter)
                {
                    return symbolAccessibilityResult.ErrorModuleName is not null
                        ? symbolAccessibilityResult.Accessibility == SymbolAccessibility.CannotBeNamed
                            ? Diagnostics.Public_property_0_of_exported_class_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named
                            : Diagnostics.Public_property_0_of_exported_class_has_or_is_using_name_1_from_private_module_2
                        : Diagnostics.Public_property_0_of_exported_class_has_or_is_using_private_name_1;
                }
                else
                {
                    // Interfaces cannot have types that cannot be named
                    return symbolAccessibilityResult.ErrorModuleName is not null
                        ? Diagnostics.Property_0_of_exported_interface_has_or_is_using_name_1_from_private_module_2
                        : Diagnostics.Property_0_of_exported_interface_has_or_is_using_private_name_1;
                }
            }
            return null;
        }

        SymbolAccessibilityDiagnostic? GetVariableDeclarationTypeVisibilityError(ISymbolAccessibilityResult symbolAccessibilityResult)
        {
            var diagnosticMessage = GetVariableDeclarationTypeVisibilityDiagnosticMessage(symbolAccessibilityResult);
            return diagnosticMessage is not null ? new SymbolAccessibilityDiagnostic
            {
                DiagnosticMessage = diagnosticMessage,
                ErrorNode = node,
                TypeName = ((INamedDeclaration)node).Name,
            } : null;
        }

        SymbolAccessibilityDiagnostic GetAccessorDeclarationTypeVisibilityError(ISymbolAccessibilityResult symbolAccessibilityResult)
        {
            DiagnosticMessage diagnosticMessage;
            if (node.Kind == SyntaxKind.SetAccessor)
            {
                // Getters can infer the return type from the returned expression, but setters cannot, so the
                // "_from_external_module_1_but_cannot_be_named" case cannot occur.
                if (Utilities.IsStatic(node))
                {
                    diagnosticMessage = symbolAccessibilityResult.ErrorModuleName is not null
                        ? Diagnostics.Parameter_type_of_public_static_setter_0_from_exported_class_has_or_is_using_name_1_from_private_module_2
                        : Diagnostics.Parameter_type_of_public_static_setter_0_from_exported_class_has_or_is_using_private_name_1;
                }
                else
                {
                    diagnosticMessage = symbolAccessibilityResult.ErrorModuleName is not null
                        ? Diagnostics.Parameter_type_of_public_setter_0_from_exported_class_has_or_is_using_name_1_from_private_module_2
                        : Diagnostics.Parameter_type_of_public_setter_0_from_exported_class_has_or_is_using_private_name_1;
                }
            }
            else
            {
                if (Utilities.IsStatic(node))
                {
                    diagnosticMessage = symbolAccessibilityResult.ErrorModuleName is not null
                        ? symbolAccessibilityResult.Accessibility == SymbolAccessibility.CannotBeNamed
                            ? Diagnostics.Return_type_of_public_static_getter_0_from_exported_class_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named
                            : Diagnostics.Return_type_of_public_static_getter_0_from_exported_class_has_or_is_using_name_1_from_private_module_2
                        : Diagnostics.Return_type_of_public_static_getter_0_from_exported_class_has_or_is_using_private_name_1;
                }
                else
                {
                    diagnosticMessage = symbolAccessibilityResult.ErrorModuleName is not null
                        ? symbolAccessibilityResult.Accessibility == SymbolAccessibility.CannotBeNamed
                            ? Diagnostics.Return_type_of_public_getter_0_from_exported_class_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named
                            : Diagnostics.Return_type_of_public_getter_0_from_exported_class_has_or_is_using_name_1_from_private_module_2
                        : Diagnostics.Return_type_of_public_getter_0_from_exported_class_has_or_is_using_private_name_1;
                }
            }
            return new SymbolAccessibilityDiagnostic
            {
                DiagnosticMessage = diagnosticMessage,
                ErrorNode = ((INamedDeclaration)node).Name!,
                TypeName = ((INamedDeclaration)node).Name,
            };
        }

        SymbolAccessibilityDiagnostic GetReturnTypeVisibilityError(ISymbolAccessibilityResult symbolAccessibilityResult)
        {
            DiagnosticMessage diagnosticMessage;
            switch (node.Kind)
            {
                case SyntaxKind.ConstructSignature:
                    // Interfaces cannot have return types that cannot be named
                    diagnosticMessage = symbolAccessibilityResult.ErrorModuleName is not null
                        ? Diagnostics.Return_type_of_constructor_signature_from_exported_interface_has_or_is_using_name_0_from_private_module_1
                        : Diagnostics.Return_type_of_constructor_signature_from_exported_interface_has_or_is_using_private_name_0;
                    break;

                case SyntaxKind.CallSignature:
                    // Interfaces cannot have return types that cannot be named
                    diagnosticMessage = symbolAccessibilityResult.ErrorModuleName is not null
                        ? Diagnostics.Return_type_of_call_signature_from_exported_interface_has_or_is_using_name_0_from_private_module_1
                        : Diagnostics.Return_type_of_call_signature_from_exported_interface_has_or_is_using_private_name_0;
                    break;

                case SyntaxKind.IndexSignature:
                    // Interfaces cannot have return types that cannot be named
                    diagnosticMessage = symbolAccessibilityResult.ErrorModuleName is not null
                        ? Diagnostics.Return_type_of_index_signature_from_exported_interface_has_or_is_using_name_0_from_private_module_1
                        : Diagnostics.Return_type_of_index_signature_from_exported_interface_has_or_is_using_private_name_0;
                    break;

                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.MethodSignature:
                    if (Utilities.IsStatic(node))
                    {
                        diagnosticMessage = symbolAccessibilityResult.ErrorModuleName is not null
                            ? symbolAccessibilityResult.Accessibility == SymbolAccessibility.CannotBeNamed
                                ? Diagnostics.Return_type_of_public_static_method_from_exported_class_has_or_is_using_name_0_from_external_module_1_but_cannot_be_named
                                : Diagnostics.Return_type_of_public_static_method_from_exported_class_has_or_is_using_name_0_from_private_module_1
                            : Diagnostics.Return_type_of_public_static_method_from_exported_class_has_or_is_using_private_name_0;
                    }
                    else if (node.Parent!.Kind == SyntaxKind.ClassDeclaration)
                    {
                        diagnosticMessage = symbolAccessibilityResult.ErrorModuleName is not null
                            ? symbolAccessibilityResult.Accessibility == SymbolAccessibility.CannotBeNamed
                                ? Diagnostics.Return_type_of_public_method_from_exported_class_has_or_is_using_name_0_from_external_module_1_but_cannot_be_named
                                : Diagnostics.Return_type_of_public_method_from_exported_class_has_or_is_using_name_0_from_private_module_1
                            : Diagnostics.Return_type_of_public_method_from_exported_class_has_or_is_using_private_name_0;
                    }
                    else
                    {
                        // Interfaces cannot have return types that cannot be named
                        diagnosticMessage = symbolAccessibilityResult.ErrorModuleName is not null
                            ? Diagnostics.Return_type_of_method_from_exported_interface_has_or_is_using_name_0_from_private_module_1
                            : Diagnostics.Return_type_of_method_from_exported_interface_has_or_is_using_private_name_0;
                    }
                    break;

                case SyntaxKind.FunctionDeclaration:
                    diagnosticMessage = symbolAccessibilityResult.ErrorModuleName is not null
                        ? symbolAccessibilityResult.Accessibility == SymbolAccessibility.CannotBeNamed
                            ? Diagnostics.Return_type_of_exported_function_has_or_is_using_name_0_from_external_module_1_but_cannot_be_named
                            : Diagnostics.Return_type_of_exported_function_has_or_is_using_name_0_from_private_module_1
                        : Diagnostics.Return_type_of_exported_function_has_or_is_using_private_name_0;
                    break;

                default:
                    return Debug.Fail<SymbolAccessibilityDiagnostic>("This is unknown kind for signature: " + (int)node.Kind);
            }

            return new SymbolAccessibilityDiagnostic
            {
                DiagnosticMessage = diagnosticMessage,
                ErrorNode = ((INamedDeclaration)node).Name ?? node,
            };
        }

        SymbolAccessibilityDiagnostic? GetParameterDeclarationTypeVisibilityError(ISymbolAccessibilityResult symbolAccessibilityResult)
        {
            var diagnosticMessage = GetParameterDeclarationTypeVisibilityDiagnosticMessage(symbolAccessibilityResult);
            return diagnosticMessage is not null ? new SymbolAccessibilityDiagnostic
            {
                DiagnosticMessage = diagnosticMessage,
                ErrorNode = node,
                TypeName = ((INamedDeclaration)node).Name,
            } : null;
        }

        DiagnosticMessage? GetParameterDeclarationTypeVisibilityDiagnosticMessage(ISymbolAccessibilityResult symbolAccessibilityResult)
        {
            switch (node.Parent!.Kind)
            {
                case SyntaxKind.Constructor:
                    return symbolAccessibilityResult.ErrorModuleName is not null
                        ? symbolAccessibilityResult.Accessibility == SymbolAccessibility.CannotBeNamed
                            ? Diagnostics.Parameter_0_of_constructor_from_exported_class_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named
                            : Diagnostics.Parameter_0_of_constructor_from_exported_class_has_or_is_using_name_1_from_private_module_2
                        : Diagnostics.Parameter_0_of_constructor_from_exported_class_has_or_is_using_private_name_1;

                case SyntaxKind.ConstructSignature:
                case SyntaxKind.ConstructorType:
                    // Interfaces cannot have parameter types that cannot be named
                    return symbolAccessibilityResult.ErrorModuleName is not null
                        ? Diagnostics.Parameter_0_of_constructor_signature_from_exported_interface_has_or_is_using_name_1_from_private_module_2
                        : Diagnostics.Parameter_0_of_constructor_signature_from_exported_interface_has_or_is_using_private_name_1;

                case SyntaxKind.CallSignature:
                    // Interfaces cannot have parameter types that cannot be named
                    return symbolAccessibilityResult.ErrorModuleName is not null
                        ? Diagnostics.Parameter_0_of_call_signature_from_exported_interface_has_or_is_using_name_1_from_private_module_2
                        : Diagnostics.Parameter_0_of_call_signature_from_exported_interface_has_or_is_using_private_name_1;

                case SyntaxKind.IndexSignature:
                    // Interfaces cannot have parameter types that cannot be named
                    return symbolAccessibilityResult.ErrorModuleName is not null
                        ? Diagnostics.Parameter_0_of_index_signature_from_exported_interface_has_or_is_using_name_1_from_private_module_2
                        : Diagnostics.Parameter_0_of_index_signature_from_exported_interface_has_or_is_using_private_name_1;

                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.MethodSignature:
                    if (Utilities.IsStatic(node.Parent!))
                    {
                        return symbolAccessibilityResult.ErrorModuleName is not null
                            ? symbolAccessibilityResult.Accessibility == SymbolAccessibility.CannotBeNamed
                                ? Diagnostics.Parameter_0_of_public_static_method_from_exported_class_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named
                                : Diagnostics.Parameter_0_of_public_static_method_from_exported_class_has_or_is_using_name_1_from_private_module_2
                            : Diagnostics.Parameter_0_of_public_static_method_from_exported_class_has_or_is_using_private_name_1;
                    }
                    else if (node.Parent!.Parent!.Kind == SyntaxKind.ClassDeclaration)
                    {
                        return symbolAccessibilityResult.ErrorModuleName is not null
                            ? symbolAccessibilityResult.Accessibility == SymbolAccessibility.CannotBeNamed
                                ? Diagnostics.Parameter_0_of_public_method_from_exported_class_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named
                                : Diagnostics.Parameter_0_of_public_method_from_exported_class_has_or_is_using_name_1_from_private_module_2
                            : Diagnostics.Parameter_0_of_public_method_from_exported_class_has_or_is_using_private_name_1;
                    }
                    else
                    {
                        // Interfaces cannot have parameter types that cannot be named
                        return symbolAccessibilityResult.ErrorModuleName is not null
                            ? Diagnostics.Parameter_0_of_method_from_exported_interface_has_or_is_using_name_1_from_private_module_2
                            : Diagnostics.Parameter_0_of_method_from_exported_interface_has_or_is_using_private_name_1;
                    }

                case SyntaxKind.FunctionDeclaration:
                case SyntaxKind.FunctionType:
                    return symbolAccessibilityResult.ErrorModuleName is not null
                        ? symbolAccessibilityResult.Accessibility == SymbolAccessibility.CannotBeNamed
                            ? Diagnostics.Parameter_0_of_exported_function_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named
                            : Diagnostics.Parameter_0_of_exported_function_has_or_is_using_name_1_from_private_module_2
                        : Diagnostics.Parameter_0_of_exported_function_has_or_is_using_private_name_1;
                case SyntaxKind.SetAccessor:
                case SyntaxKind.GetAccessor:
                    return symbolAccessibilityResult.ErrorModuleName is not null
                        ? symbolAccessibilityResult.Accessibility == SymbolAccessibility.CannotBeNamed
                            ? Diagnostics.Parameter_0_of_accessor_has_or_is_using_name_1_from_external_module_2_but_cannot_be_named
                            : Diagnostics.Parameter_0_of_accessor_has_or_is_using_name_1_from_private_module_2
                        : Diagnostics.Parameter_0_of_accessor_has_or_is_using_private_name_1;

                default:
                    return Debug.Fail<DiagnosticMessage?>($"Unknown parent for parameter: {Debug.FormatSyntaxKind(node.Parent!.Kind)}");
            }
        }

        SymbolAccessibilityDiagnostic GetTypeParameterConstraintVisibilityError(ISymbolAccessibilityResult symbolAccessibilityResult)
        {
            _ = symbolAccessibilityResult;
            // Type parameter constraints are named by user so we should always be able to name it
            DiagnosticMessage diagnosticMessage;
            switch (node.Parent!.Kind)
            {
                case SyntaxKind.ClassDeclaration:
                    diagnosticMessage = Diagnostics.Type_parameter_0_of_exported_class_has_or_is_using_private_name_1;
                    break;

                case SyntaxKind.InterfaceDeclaration:
                    diagnosticMessage = Diagnostics.Type_parameter_0_of_exported_interface_has_or_is_using_private_name_1;
                    break;

                case SyntaxKind.MappedType:
                    diagnosticMessage = Diagnostics.Type_parameter_0_of_exported_mapped_object_type_is_using_private_name_1;
                    break;

                case SyntaxKind.ConstructorType:
                case SyntaxKind.ConstructSignature:
                    diagnosticMessage = Diagnostics.Type_parameter_0_of_constructor_signature_from_exported_interface_has_or_is_using_private_name_1;
                    break;

                case SyntaxKind.CallSignature:
                    diagnosticMessage = Diagnostics.Type_parameter_0_of_call_signature_from_exported_interface_has_or_is_using_private_name_1;
                    break;

                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.MethodSignature:
                    if (Utilities.IsStatic(node.Parent!))
                    {
                        diagnosticMessage = Diagnostics.Type_parameter_0_of_public_static_method_from_exported_class_has_or_is_using_private_name_1;
                    }
                    else if (node.Parent!.Parent!.Kind == SyntaxKind.ClassDeclaration)
                    {
                        diagnosticMessage = Diagnostics.Type_parameter_0_of_public_method_from_exported_class_has_or_is_using_private_name_1;
                    }
                    else
                    {
                        diagnosticMessage = Diagnostics.Type_parameter_0_of_method_from_exported_interface_has_or_is_using_private_name_1;
                    }
                    break;

                case SyntaxKind.FunctionType:
                case SyntaxKind.FunctionDeclaration:
                    diagnosticMessage = Diagnostics.Type_parameter_0_of_exported_function_has_or_is_using_private_name_1;
                    break;

                case SyntaxKind.InferType:
                    diagnosticMessage = Diagnostics.Extends_clause_for_inferred_type_0_has_or_is_using_private_name_1;
                    break;

                case SyntaxKind.TypeAliasDeclaration:
                    diagnosticMessage = Diagnostics.Type_parameter_0_of_exported_type_alias_has_or_is_using_private_name_1;
                    break;

                default:
                    return Debug.Fail<SymbolAccessibilityDiagnostic>("This is unknown parent for type parameter: " + (int)node.Parent!.Kind);
            }

            return new SymbolAccessibilityDiagnostic
            {
                DiagnosticMessage = diagnosticMessage,
                ErrorNode = node,
                TypeName = ((INamedDeclaration)node).Name,
            };
        }

        SymbolAccessibilityDiagnostic GetHeritageClauseVisibilityError(ISymbolAccessibilityResult symbolAccessibilityResult)
        {
            _ = symbolAccessibilityResult;
            DiagnosticMessage diagnosticMessage;
            // Heritage clause is written by user so it can always be named
            if (NodeTests.IsClassDeclaration(node.Parent!.Parent!))
            {
                // Class or Interface implemented/extended is inaccessible
                diagnosticMessage = NodeTests.IsHeritageClause(node.Parent!)
                    && ((HeritageClause)node.Parent!).Token == SyntaxKind.ImplementsKeyword
                    ? Diagnostics.Implements_clause_of_exported_class_0_has_or_is_using_private_name_1
                    : ((INamedDeclaration)node.Parent!.Parent!).Name is not null ? Diagnostics.extends_clause_of_exported_class_0_has_or_is_using_private_name_1
                    : Diagnostics.extends_clause_of_exported_class_has_or_is_using_private_name_0;
            }
            else
            {
                // interface is inaccessible
                diagnosticMessage = Diagnostics.extends_clause_of_exported_interface_0_has_or_is_using_private_name_1;
            }

            return new SymbolAccessibilityDiagnostic
            {
                DiagnosticMessage = diagnosticMessage,
                ErrorNode = node,
                TypeName = (Node?)Utilities.GetNameOfDeclaration(node.Parent!.Parent!),
            };
        }

        SymbolAccessibilityDiagnostic GetImportEntityNameVisibilityError(ISymbolAccessibilityResult symbolAccessibilityResult)
        {
            _ = symbolAccessibilityResult;
            return new SymbolAccessibilityDiagnostic
            {
                DiagnosticMessage = Diagnostics.Import_declaration_0_is_using_private_name_1,
                ErrorNode = node,
                TypeName = ((INamedDeclaration)node).Name,
            };
        }

        SymbolAccessibilityDiagnostic GetTypeAliasDeclarationVisibilityError(ISymbolAccessibilityResult symbolAccessibilityResult)
        {
            return new SymbolAccessibilityDiagnostic
            {
                DiagnosticMessage = symbolAccessibilityResult.ErrorModuleName is not null
                    ? Diagnostics.Exported_type_alias_0_has_or_is_using_private_name_1_from_module_2
                    : Diagnostics.Exported_type_alias_0_has_or_is_using_private_name_1,
                ErrorNode = Utilities.IsJSDocTypeAlias(node)
                    ? Debug.CheckDefined(JSDocTypeAliasTypeExpression(node))
                    : (Node)((TypeAliasDeclaration)node).Type!,
                TypeName = Utilities.IsJSDocTypeAlias(node)
                    ? (Node?)Utilities.GetNameOfDeclaration(node)
                    : ((TypeAliasDeclaration)node).Name,
            };
        }
    }

    /// <summary>ported from: src/compiler/transformers/declarations/diagnostics.ts:606-812 — <c>createGetIsolatedDeclarationErrors</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the reference's two lookup tables (<c>:607-621</c>, <c>:622-639</c>) are per-call literals there and
    /// static tables here (see the class remarks); <see cref="Lookup"/> keeps the "absent key answers
    /// <c>undefined</c>" reading the reference's <c>obj[kind]</c> has.
    /// </para>
    /// <para>
    /// PORT-NOTE: the reference's <c>assertType&lt;never&gt;(node)</c> and <c>Debug.type&lt;T&gt;(node)</c> are erased
    /// type-only calls (<c>_namespaces/ts.ts</c>), so the port drops them; the <c>default</c> arm keeps the reference's
    /// fall-through to <c>createExpressionError</c>.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>getAllAccessorDeclarations</c> (<c>utilities.ts:6832-6885</c>) and
    /// <c>unwrapParenthesizedExpression</c> are declared on this class for the reason the class remarks record; the
    /// first is the same body the port states beside <c>classElementOrClassElementParameterIsDecorated</c>
    /// (<c>Utilities/Utilities.Checker.cs:2472</c>), and the second is <c>utilities.ts:12056-12061</c>.
    /// </para>
    /// </remarks>
    public static Func<Node, IDiagnosticWithLocation> CreateGetIsolatedDeclarationErrors(IEmitResolver resolver)
    {
        return GetDiagnostic;

        IDiagnosticWithLocation GetDiagnostic(Node node)
        {
            var heritageClause = Utilities.FindAncestor(node, NodeTests.IsHeritageClause);
            if (heritageClause is not null)
            {
                return Utilities.CreateDiagnosticForNode(node, Diagnostics.Extends_clause_can_t_contain_an_expression_with_isolatedDeclarations);
            }
            if ((Utilities.IsPartOfTypeNode(node) || (node.Parent is not null && NodeTests.IsTypeQueryNode(node.Parent)))
                && (Utilities.IsEntityName(node) || Utilities.IsEntityNameExpression(node)))
            {
                return CreateEntityInTypeNodeError(node);
            }
            switch (node.Kind)
            {
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                    return CreateAccessorTypeError(node);
                case SyntaxKind.ComputedPropertyName:
                case SyntaxKind.ShorthandPropertyAssignment:
                case SyntaxKind.SpreadAssignment:
                    return CreateObjectLiteralError(node);
                case SyntaxKind.ArrayLiteralExpression:
                case SyntaxKind.SpreadElement:
                    return CreateArrayLiteralError(node);
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.ConstructSignature:
                case SyntaxKind.FunctionExpression:
                case SyntaxKind.ArrowFunction:
                case SyntaxKind.FunctionDeclaration:
                    return CreateReturnTypeError(node);
                case SyntaxKind.BindingElement:
                    return CreateBindingElementError(node);
                case SyntaxKind.PropertyDeclaration:
                case SyntaxKind.VariableDeclaration:
                    return CreateVariableOrPropertyError(node);
                case SyntaxKind.Parameter:
                    return CreateParameterError((ParameterDeclaration)node);
                case SyntaxKind.PropertyAssignment:
                    return CreateExpressionError(((PropertyAssignment)node).Initializer!, /*diagnosticMessage*/ null);
                case SyntaxKind.ClassExpression:
                    return CreateClassExpressionError(node);
                default:
                    return CreateExpressionError((Expression)node, /*diagnosticMessage*/ null);
            }
        }

        Node? FindNearestDeclaration(Node node)
        {
            var result = Utilities.FindAncestor(
                node,
                n => NodeTests.IsExportAssignment(n) || Utilities.IsStatement(n) || NodeTests.IsVariableDeclaration(n) || NodeTests.IsPropertyDeclaration(n) || NodeTests.IsParameter(n));
            if (result is null) return null;

            if (NodeTests.IsExportAssignment(result)) return result;

            if (NodeTests.IsReturnStatement(result))
            {
                return Utilities.FindAncestor(result, n => Utilities.IsFunctionLikeDeclaration(n) && !NodeTests.IsConstructorDeclaration(n));
            }
            return Utilities.IsStatement(result) ? null : result;
        }

        Diagnostic CreateAccessorTypeError(Node node)
        {
            var (_, _, getAccessor, setAccessor) = Utilities.GetAllAccessorDeclarations(((IDeclaration)node).Symbol!.Declarations!, node);

            var targetNode = (Node?)(Utilities.IsSetAccessor(node)
                ? ((SetAccessorDeclaration)node).Parameters is { Count: > 0 } parameters ? parameters[0] : null
                : node) ?? node;
            var diag = Utilities.CreateDiagnosticForNode(targetNode, Lookup(s_errorByDeclarationKind, node.Kind)!);

            if (setAccessor is not null)
            {
                Utilities.AddRelatedInfo(diag, Utilities.CreateDiagnosticForNode(setAccessor, Lookup(s_relatedSuggestionByDeclarationKind, setAccessor.Kind)!));
            }
            if (getAccessor is not null)
            {
                Utilities.AddRelatedInfo(diag, Utilities.CreateDiagnosticForNode(getAccessor, Lookup(s_relatedSuggestionByDeclarationKind, getAccessor.Kind)!));
            }
            return diag;
        }

        Diagnostic AddParentDeclarationRelatedInfo(Node node, Diagnostic diag)
        {
            var parentDeclaration = FindNearestDeclaration(node);
            if (parentDeclaration is not null)
            {
                var targetStr = NodeTests.IsExportAssignment(parentDeclaration) || ((INamedDeclaration)parentDeclaration).Name is null
                    ? ""
                    : Utilities.GetTextOfNode(((INamedDeclaration)parentDeclaration).Name!, /*includeTrivia*/ false);
                Utilities.AddRelatedInfo(diag, Utilities.CreateDiagnosticForNode(parentDeclaration, Lookup(s_relatedSuggestionByDeclarationKind, parentDeclaration.Kind)!, targetStr));
            }
            return diag;
        }

        Diagnostic CreateObjectLiteralError(Node node)
        {
            var diag = Utilities.CreateDiagnosticForNode(node, Lookup(s_errorByDeclarationKind, node.Kind)!);
            AddParentDeclarationRelatedInfo(node, diag);
            return diag;
        }

        Diagnostic CreateArrayLiteralError(Node node)
        {
            var diag = Utilities.CreateDiagnosticForNode(node, Lookup(s_errorByDeclarationKind, node.Kind)!);
            AddParentDeclarationRelatedInfo(node, diag);
            return diag;
        }

        Diagnostic CreateReturnTypeError(Node node)
        {
            var diag = Utilities.CreateDiagnosticForNode(node, Lookup(s_errorByDeclarationKind, node.Kind)!);
            AddParentDeclarationRelatedInfo(node, diag);
            Utilities.AddRelatedInfo(diag, Utilities.CreateDiagnosticForNode(node, Lookup(s_relatedSuggestionByDeclarationKind, node.Kind)!));
            return diag;
        }

        Diagnostic CreateBindingElementError(Node node)
            => Utilities.CreateDiagnosticForNode(node, Diagnostics.Binding_elements_can_t_be_exported_directly_with_isolatedDeclarations);

        Diagnostic CreateVariableOrPropertyError(Node node)
        {
            var diag = Utilities.CreateDiagnosticForNode(node, Lookup(s_errorByDeclarationKind, node.Kind)!);
            var targetStr = Utilities.GetTextOfNode(((INamedDeclaration)node).Name!, /*includeTrivia*/ false);
            Utilities.AddRelatedInfo(diag, Utilities.CreateDiagnosticForNode(node, Lookup(s_relatedSuggestionByDeclarationKind, node.Kind)!, targetStr));
            return diag;
        }

        Diagnostic CreateParameterError(ParameterDeclaration node)
        {
            if (Utilities.IsSetAccessor(node.Parent!))
            {
                return CreateAccessorTypeError(node.Parent!);
            }
            var addUndefined = resolver.RequiresAddingImplicitUndefined(node, node.Parent);
            if (!addUndefined && node.Initializer is not null)
            {
                return CreateExpressionError(node.Initializer, /*diagnosticMessage*/ null);
            }
            var message = addUndefined
                ? Diagnostics.Declaration_emit_for_this_parameter_requires_implicitly_adding_undefined_to_its_type_This_is_not_supported_with_isolatedDeclarations
                : Lookup(s_errorByDeclarationKind, node.Kind)!;
            var diag = Utilities.CreateDiagnosticForNode(node, message);
            var targetStr = Utilities.GetTextOfNode(node.Name!, /*includeTrivia*/ false);
            Utilities.AddRelatedInfo(diag, Utilities.CreateDiagnosticForNode(node, Lookup(s_relatedSuggestionByDeclarationKind, node.Kind)!, targetStr));
            return diag;
        }

        Diagnostic CreateClassExpressionError(Node node)
            => CreateExpressionError((Expression)node, Diagnostics.Inference_from_class_expressions_is_not_supported_with_isolatedDeclarations);

        Diagnostic CreateEntityInTypeNodeError(Node node)
        {
            var diag = Utilities.CreateDiagnosticForNode(
                node,
                Diagnostics.Type_containing_private_name_0_can_t_be_used_with_isolatedDeclarations,
                Utilities.GetTextOfNode(node, /*includeTrivia*/ false));
            AddParentDeclarationRelatedInfo(node, diag);
            return diag;
        }

        Diagnostic CreateExpressionError(Expression node, DiagnosticMessage? diagnosticMessage)
        {
            var parentDeclaration = FindNearestDeclaration(node);
            Diagnostic diag;
            if (parentDeclaration is not null)
            {
                var targetStr = NodeTests.IsExportAssignment(parentDeclaration) || ((INamedDeclaration)parentDeclaration).Name is null
                    ? ""
                    : Utilities.GetTextOfNode(((INamedDeclaration)parentDeclaration).Name!, /*includeTrivia*/ false);
                // PORT-NOTE: the reference's callback answers the `"quit"` sentinel for a statement; the port's second
                // `FindAncestor` overload spells that sentinel as a null answer (see Utilities.cs:974).
                Func<Node, bool?> callback = n => NodeTests.IsExportAssignment(n)
                    ? true
                    : Utilities.IsStatement(n)
                        ? null
                        : !NodeTests.IsParenthesizedExpression(n) && !NodeTests.IsTypeAssertionExpression(n) && !NodeTests.IsAsExpression(n);
                var parent = Utilities.FindAncestor(node.Parent, callback);

                if (ReferenceEquals(parentDeclaration, parent))
                {
                    diag = Utilities.CreateDiagnosticForNode(node, diagnosticMessage ?? Lookup(s_errorByDeclarationKind, parentDeclaration.Kind)!);
                    Utilities.AddRelatedInfo(diag, Utilities.CreateDiagnosticForNode(parentDeclaration, Lookup(s_relatedSuggestionByDeclarationKind, parentDeclaration.Kind)!, targetStr));
                }
                else
                {
                    diag = Utilities.CreateDiagnosticForNode(node, diagnosticMessage ?? Diagnostics.Expression_type_can_t_be_inferred_with_isolatedDeclarations);
                    Utilities.AddRelatedInfo(diag, Utilities.CreateDiagnosticForNode(parentDeclaration, Lookup(s_relatedSuggestionByDeclarationKind, parentDeclaration.Kind)!, targetStr));
                    Utilities.AddRelatedInfo(diag, Utilities.CreateDiagnosticForNode(node, Diagnostics.Add_satisfies_and_a_type_assertion_to_this_expression_satisfies_T_as_T_to_make_the_type_explicit));
                }
            }
            else
            {
                diag = Utilities.CreateDiagnosticForNode(node, diagnosticMessage ?? Diagnostics.Expression_type_can_t_be_inferred_with_isolatedDeclarations);
            }
            return diag;
        }

    }

    /// <summary>The <c>node.typeExpression</c> read of <c>getTypeAliasDeclarationVisibilityError</c> (diagnostics.ts:604).</summary>
    /// <remarks>PORT-NOTE: TS's <c>isJSDocTypeAlias</c> guard narrows to the <c>JSDocTypeAlias</c> union
    /// (<c>JSDocTypedefTag | JSDocCallbackTag | JSDocEnumTag</c>), whose <c>typeExpression</c> slots are three different
    /// types; the model gives each class its own (and <c>JSDocTypedefTag</c>'s is the one the parser leaves
    /// <c>Node</c>), so the shared slot is a kind switch, the treatment <c>EmitResolver.QuestionTokenOf</c> records for
    /// the same problem.</remarks>
    private static Node? JSDocTypeAliasTypeExpression(Node node) => node switch
    {
        JSDocTypedefTag tag => tag.TypeExpression,
        JSDocCallbackTag tag => tag.TypeExpression,
        JSDocEnumTag tag => tag.TypeExpression,
        _ => null,
    };

    /// <summary>ported from: src/compiler/transformers/declarations/diagnostics.ts:607-621 — <c>relatedSuggestionByDeclarationKind</c>.</summary>
    private static readonly Dictionary<SyntaxKind, DiagnosticMessage> s_relatedSuggestionByDeclarationKind = new()
    {
        [SyntaxKind.ArrowFunction] = Diagnostics.Add_a_return_type_to_the_function_expression,
        [SyntaxKind.FunctionExpression] = Diagnostics.Add_a_return_type_to_the_function_expression,
        [SyntaxKind.MethodDeclaration] = Diagnostics.Add_a_return_type_to_the_method,
        [SyntaxKind.GetAccessor] = Diagnostics.Add_a_return_type_to_the_get_accessor_declaration,
        [SyntaxKind.SetAccessor] = Diagnostics.Add_a_type_to_parameter_of_the_set_accessor_declaration,
        [SyntaxKind.FunctionDeclaration] = Diagnostics.Add_a_return_type_to_the_function_declaration,
        [SyntaxKind.ConstructSignature] = Diagnostics.Add_a_return_type_to_the_function_declaration,
        [SyntaxKind.Parameter] = Diagnostics.Add_a_type_annotation_to_the_parameter_0,
        [SyntaxKind.VariableDeclaration] = Diagnostics.Add_a_type_annotation_to_the_variable_0,
        [SyntaxKind.PropertyDeclaration] = Diagnostics.Add_a_type_annotation_to_the_property_0,
        [SyntaxKind.PropertySignature] = Diagnostics.Add_a_type_annotation_to_the_property_0,
        [SyntaxKind.ExportAssignment] = Diagnostics.Move_the_expression_in_default_export_to_a_variable_and_add_a_type_annotation_to_it,
    };

    /// <summary>ported from: src/compiler/transformers/declarations/diagnostics.ts:622-639 — <c>errorByDeclarationKind</c>.</summary>
    private static readonly Dictionary<SyntaxKind, DiagnosticMessage> s_errorByDeclarationKind = new()
    {
        [SyntaxKind.FunctionExpression] = Diagnostics.Function_must_have_an_explicit_return_type_annotation_with_isolatedDeclarations,
        [SyntaxKind.FunctionDeclaration] = Diagnostics.Function_must_have_an_explicit_return_type_annotation_with_isolatedDeclarations,
        [SyntaxKind.ArrowFunction] = Diagnostics.Function_must_have_an_explicit_return_type_annotation_with_isolatedDeclarations,
        [SyntaxKind.MethodDeclaration] = Diagnostics.Method_must_have_an_explicit_return_type_annotation_with_isolatedDeclarations,
        [SyntaxKind.ConstructSignature] = Diagnostics.Method_must_have_an_explicit_return_type_annotation_with_isolatedDeclarations,
        [SyntaxKind.GetAccessor] = Diagnostics.At_least_one_accessor_must_have_an_explicit_type_annotation_with_isolatedDeclarations,
        [SyntaxKind.SetAccessor] = Diagnostics.At_least_one_accessor_must_have_an_explicit_type_annotation_with_isolatedDeclarations,
        [SyntaxKind.Parameter] = Diagnostics.Parameter_must_have_an_explicit_type_annotation_with_isolatedDeclarations,
        [SyntaxKind.VariableDeclaration] = Diagnostics.Variable_must_have_an_explicit_type_annotation_with_isolatedDeclarations,
        [SyntaxKind.PropertyDeclaration] = Diagnostics.Property_must_have_an_explicit_type_annotation_with_isolatedDeclarations,
        [SyntaxKind.PropertySignature] = Diagnostics.Property_must_have_an_explicit_type_annotation_with_isolatedDeclarations,
        [SyntaxKind.ComputedPropertyName] = Diagnostics.Computed_property_names_on_class_or_object_literals_cannot_be_inferred_with_isolatedDeclarations,
        [SyntaxKind.SpreadAssignment] = Diagnostics.Objects_that_contain_spread_assignments_can_t_be_inferred_with_isolatedDeclarations,
        [SyntaxKind.ShorthandPropertyAssignment] = Diagnostics.Objects_that_contain_shorthand_properties_can_t_be_inferred_with_isolatedDeclarations,
        [SyntaxKind.ArrayLiteralExpression] = Diagnostics.Only_const_arrays_can_be_inferred_with_isolatedDeclarations,
        [SyntaxKind.ExportAssignment] = Diagnostics.Default_exports_can_t_be_inferred_with_isolatedDeclarations,
        [SyntaxKind.SpreadElement] = Diagnostics.Arrays_with_spread_elements_can_t_inferred_with_isolatedDeclarations,
    };

    /// <summary>The "absent key answers <c>undefined</c>" read of the two tables above.</summary>
    private static DiagnosticMessage? Lookup(Dictionary<SyntaxKind, DiagnosticMessage> table, SyntaxKind kind)
        => table.TryGetValue(kind, out var message) ? message : null;
}
