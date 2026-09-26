#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/declarations.ts:1006-1863 — the two visitors the factory hands the walker and
// the top-level declaration rewriter: `visitDeclarationSubtree` (1006-1288), `isPrivateMethodTypeParameter`
// (1290-1292), `visitDeclarationStatements` (1294-1349), `stripExportModifiers` (1351-1360),
// `updateModuleDeclarationAndKeyword` (1362-1385), `transformTopLevelDeclaration` (1387-1758),
// `transformVariableStatement` (1760-1777), `recreateBindingPattern`/`recreateBindingElement` (1779-1797),
// `checkName` (1798-1813), `shouldStripInternal` (1815-1817), `isScopeMarker`/`hasScopeMarker` (1819-1825),
// `ensureModifiers`/`ensureModifierFlags` (1827-1845) and `transformHeritageClauses` (1847-1863).
internal sealed partial class DeclarationTransformer
{
    /// <summary>ported from: src/compiler/transformers/declarations.ts:1006-1288 — <c>visitDeclarationSubtree</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the reference's result type <c>VisitResult&lt;Node | undefined&gt;</c> is <c>object?</c> (a
    /// <see cref="Node"/>, a node list, or <c>null</c>), which is what the walker's <c>Visitor</c> face takes
    /// (Utilities/Utilities.Visitor.cs). Its local <c>cleanup</c> is a local function here so it closes over the same
    /// saved diagnostics state; the reference's <c>cleanup(factory.updateX(...))</c> arms are therefore written
    /// <c>Cleanup(...)</c> and the arms that return without a value are bare <c>return null</c>.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>input.name</c> is read through <see cref="INamedDeclaration"/> (the dynamic-name guard the
    /// reference leans on has no C# face) and the two <c>input.symbol</c> reads through <see cref="IDeclaration"/>.
    /// The <c>default</c> arm's <c>Debug.assertNever(node, …)</c> is <see cref="Debug.Fail{T}"/> — the same throw in
    /// expression position (Core/Debug.cs:113-118) — because the port's switch is a statement whose end must be
    /// reachable only on the paths the reference's own guard already excluded.
    /// </para>
    /// </remarks>
    private object? VisitDeclarationSubtree(Node input)
    {
        if (ShouldStripInternal(input)) return null;
        if (Utilities.IsDeclaration(input))
        {
            if (IsDeclarationAndNotVisible(input)) return null;
            if (Utilities.HasDynamicName(input))
            {
                var dynamicNameExpression = ((ComputedPropertyName)((INamedDeclaration)input).Name!).Expression!;
                if (_isolatedDeclarations)
                {
                    // Classes and object literals usually elide properties with computed names that are not of a literal type
                    // In isolated declarations TSC needs to error on these as we don't know the type in a DTE.
                    if (!_resolver.IsDefinitelyReferenceToGlobalSymbolObject(dynamicNameExpression))
                    {
                        if (input.Parent!.Kind == SyntaxKind.ClassDeclaration || input.Parent.Kind == SyntaxKind.ObjectLiteralExpression)
                        {
                            _context.AddDiagnostic(Utilities.CreateDiagnosticForNode(input, Diagnostics.Computed_property_names_on_class_or_object_literals_cannot_be_inferred_with_isolatedDeclarations));
                            return null;
                        }
                        else if (
                            // Type declarations just need to double-check that the input computed name is an entity name expression
                            (input.Parent!.Kind == SyntaxKind.InterfaceDeclaration || input.Parent.Kind == SyntaxKind.TypeLiteral)
                            && !Utilities.IsEntityNameExpression(dynamicNameExpression)
                        )
                        {
                            _context.AddDiagnostic(Utilities.CreateDiagnosticForNode(input, Diagnostics.Computed_properties_must_be_number_or_string_literals_variables_or_dotted_expressions_with_isolatedDeclarations));
                            return null;
                        }
                    }
                }
                else if (!Js.IsTruthy(_resolver.IsLateBound((IDeclaration?)Utilities.GetParseTreeNode(input)))
                    || !Utilities.IsEntityNameExpression(dynamicNameExpression))
                {
                    return null;
                }
            }
        }

        // Elide implementation signatures from overload sets
        if (Utilities.IsFunctionLike(input) && Js.IsTruthy(_resolver.IsImplementationOfOverload(input))) return null;

        // Elide semicolon class statements
        if (NodeTests.IsSemicolonClassElement(input)) return null;

        Node? previousEnclosingDeclaration = null;
        var isEnclosing = IsEnclosingDeclaration(input);
        if (isEnclosing)
        {
            previousEnclosingDeclaration = _enclosingDeclaration;
            _enclosingDeclaration = input;
        }
        var oldDiag = _getSymbolAccessibilityDiagnostic;

        // Setup diagnostic-related flags before first potential `cleanup` call, otherwise
        // We'd see a TDZ violation at runtime
        var canProduceDiagnostic = DeclarationsDiagnostics.CanProduceDiagnostics(input);
        var oldWithinObjectLiteralType = _suppressNewDiagnosticContexts;
        var shouldEnterSuppressNewDiagnosticsContextContext = (input.Kind == SyntaxKind.TypeLiteral || input.Kind == SyntaxKind.MappedType) && input.Parent!.Kind != SyntaxKind.TypeAliasDeclaration;

        // Emit methods which are private as properties with no type information
        if (NodeTests.IsMethodDeclaration(input) || NodeTests.IsMethodSignature(input))
        {
            if (Utilities.HasEffectiveModifier(input, ModifierFlags.Private))
            {
                var declarations = ((IDeclaration)input).Symbol?.Declarations;
                if (declarations is not null && declarations.Count > 0 && !ReferenceEquals(declarations[0], input)) return null; // Elide all but the first overload
                return Cleanup(_factory.CreatePropertyDeclaration(
                    EnsureModifiers(input),
                    ((INamedDeclaration)input).Name,
                    /*questionOrExclamationToken*/ null,
                    /*type*/ null,
                    /*initializer*/ null));
            }
        }

        if (canProduceDiagnostic && !_suppressNewDiagnosticContexts)
        {
            _getSymbolAccessibilityDiagnostic = DeclarationsDiagnostics.CreateGetSymbolAccessibilityDiagnosticForNode(input);
        }

        if (NodeTests.IsTypeQueryNode(input))
        {
            CheckEntityNameVisibility(((TypeQueryNode)input).ExprName!, _enclosingDeclaration!);
        }

        if (shouldEnterSuppressNewDiagnosticsContextContext)
        {
            // We stop making new diagnostic contexts within object literal types. Unless it's an object type on the RHS of a type alias declaration. Then we do.
            _suppressNewDiagnosticContexts = true;
        }

        if (DeclarationsTransformSupport.IsProcessedComponent(input))
        {
            switch (input.Kind)
            {
                case SyntaxKind.ExpressionWithTypeArguments:
                {
                    var inputNode = (ExpressionWithTypeArguments)input;
                    if (Utilities.IsEntityName(inputNode.Expression!) || Utilities.IsEntityNameExpression(inputNode.Expression!))
                    {
                        CheckEntityNameVisibility(inputNode.Expression!, _enclosingDeclaration!);
                    }
                    var node = Utilities.VisitEachChildWorker(inputNode, VisitDeclarationSubtree, _context);
                    return Cleanup(_factory.UpdateExpressionWithTypeArguments(node, node.Expression, node.TypeArguments));
                }
                case SyntaxKind.TypeReference:
                {
                    var inputNode = (TypeReferenceNode)input;
                    CheckEntityNameVisibility(inputNode.TypeName!, _enclosingDeclaration!);
                    var node = Utilities.VisitEachChildWorker(inputNode, VisitDeclarationSubtree, _context);
                    return Cleanup(_factory.UpdateTypeReferenceNode(node, node.TypeName, node.TypeArguments));
                }
                case SyntaxKind.ConstructSignature:
                {
                    var inputNode = (ConstructSignatureDeclaration)input;
                    return Cleanup(_factory.UpdateConstructSignature(
                        inputNode,
                        EnsureTypeParams(inputNode, inputNode.TypeParameters),
                        UpdateParamsList(inputNode, inputNode.Parameters),
                        EnsureType(inputNode)));
                }
                case SyntaxKind.Constructor:
                {
                    // A constructor declaration may not have a type annotation
                    var inputNode = (ConstructorDeclaration)input;
                    var ctor = _factory.CreateConstructorDeclaration(
                        /*modifiers*/ EnsureModifiers(inputNode),
                        UpdateParamsList(inputNode, inputNode.Parameters, ModifierFlags.None),
                        /*body*/ null);
                    return Cleanup(ctor);
                }
                case SyntaxKind.MethodDeclaration:
                {
                    var inputNode = (MethodDeclaration)input;
                    if (NodeTests.IsPrivateIdentifier(inputNode.Name!))
                    {
                        return Cleanup(/*returnValue*/ null);
                    }
                    var sig = _factory.CreateMethodDeclaration(
                        EnsureModifiers(inputNode),
                        /*asteriskToken*/ null,
                        inputNode.Name,
                        inputNode.QuestionToken,
                        EnsureTypeParams(inputNode, inputNode.TypeParameters),
                        UpdateParamsList(inputNode, inputNode.Parameters),
                        EnsureType(inputNode),
                        /*body*/ null);
                    return Cleanup(sig);
                }
                case SyntaxKind.GetAccessor:
                {
                    var inputNode = (GetAccessorDeclaration)input;
                    if (NodeTests.IsPrivateIdentifier(inputNode.Name!))
                    {
                        return Cleanup(/*returnValue*/ null);
                    }
                    return Cleanup(_factory.UpdateGetAccessorDeclaration(
                        inputNode,
                        EnsureModifiers(inputNode),
                        inputNode.Name,
                        UpdateAccessorParamsList(inputNode, Utilities.HasEffectiveModifier(inputNode, ModifierFlags.Private)),
                        EnsureType(inputNode),
                        /*body*/ null));
                }
                case SyntaxKind.SetAccessor:
                {
                    var inputNode = (SetAccessorDeclaration)input;
                    if (NodeTests.IsPrivateIdentifier(inputNode.Name!))
                    {
                        return Cleanup(/*returnValue*/ null);
                    }
                    return Cleanup(_factory.UpdateSetAccessorDeclaration(
                        inputNode,
                        EnsureModifiers(inputNode),
                        inputNode.Name,
                        UpdateAccessorParamsList(inputNode, Utilities.HasEffectiveModifier(inputNode, ModifierFlags.Private)),
                        /*body*/ null));
                }
                case SyntaxKind.PropertyDeclaration:
                {
                    var inputNode = (PropertyDeclaration)input;
                    if (NodeTests.IsPrivateIdentifier(inputNode.Name!))
                    {
                        return Cleanup(/*returnValue*/ null);
                    }
                    return Cleanup(_factory.UpdatePropertyDeclaration(
                        inputNode,
                        EnsureModifiers(inputNode),
                        inputNode.Name,
                        inputNode.QuestionToken,
                        EnsureType(inputNode),
                        EnsureNoInitializer(inputNode)));
                }
                case SyntaxKind.PropertySignature:
                {
                    var inputNode = (PropertySignature)input;
                    if (NodeTests.IsPrivateIdentifier(inputNode.Name!))
                    {
                        return Cleanup(/*returnValue*/ null);
                    }
                    return Cleanup(_factory.UpdatePropertySignature(
                        inputNode,
                        DeclarationsTransformSupport.AsModifierTokens(EnsureModifiers(inputNode)),
                        inputNode.Name,
                        inputNode.QuestionToken,
                        EnsureType(inputNode)));
                }
                case SyntaxKind.MethodSignature:
                {
                    var inputNode = (MethodSignature)input;
                    if (NodeTests.IsPrivateIdentifier(inputNode.Name!))
                    {
                        return Cleanup(/*returnValue*/ null);
                    }
                    return Cleanup(_factory.UpdateMethodSignature(
                        inputNode,
                        DeclarationsTransformSupport.AsModifierTokens(EnsureModifiers(inputNode)),
                        inputNode.Name,
                        inputNode.QuestionToken,
                        EnsureTypeParams(inputNode, inputNode.TypeParameters),
                        UpdateParamsList(inputNode, inputNode.Parameters),
                        EnsureType(inputNode)));
                }
                case SyntaxKind.CallSignature:
                {
                    var inputNode = (CallSignatureDeclaration)input;
                    return Cleanup(
                        _factory.UpdateCallSignature(
                            inputNode,
                            EnsureTypeParams(inputNode, inputNode.TypeParameters),
                            UpdateParamsList(inputNode, inputNode.Parameters),
                            EnsureType(inputNode)));
                }
                case SyntaxKind.IndexSignature:
                {
                    var inputNode = (IndexSignatureDeclaration)input;
                    return Cleanup(_factory.UpdateIndexSignature(
                        inputNode,
                        EnsureModifiers(inputNode),
                        UpdateParamsList(inputNode, inputNode.Parameters),
                        (ITypeNode?)Utilities.VisitNode((Node?)inputNode.Type, VisitDeclarationSubtree, Utilities.IsTypeNode)
                            ?? _factory.CreateKeywordTypeNode(SyntaxKind.AnyKeyword)));
                }
                case SyntaxKind.VariableDeclaration:
                {
                    var inputNode = (VariableDeclaration)input;
                    if (Utilities.IsBindingPattern(inputNode.Name!))
                    {
                        return RecreateBindingPattern(inputNode.Name!);
                    }
                    shouldEnterSuppressNewDiagnosticsContextContext = true;
                    _suppressNewDiagnosticContexts = true; // Variable declaration types also suppress new diagnostic contexts, provided the contexts wouldn't be made for binding pattern types
                    return Cleanup(_factory.UpdateVariableDeclaration(inputNode, inputNode.Name, /*exclamationToken*/ null, EnsureType(inputNode), EnsureNoInitializer(inputNode)));
                }
                case SyntaxKind.TypeParameter:
                {
                    var inputNode = (TypeParameterDeclaration)input;
                    if (IsPrivateMethodTypeParameter(inputNode) && (inputNode.Default is not null || inputNode.Constraint is not null))
                    {
                        return Cleanup(_factory.UpdateTypeParameterDeclaration(inputNode, inputNode.Modifiers, inputNode.Name, /*constraint*/ null, /*defaultType*/ null));
                    }
                    return Cleanup(Utilities.VisitEachChildWorker(inputNode, VisitDeclarationSubtree, _context));
                }
                case SyntaxKind.ConditionalType:
                {
                    // We have to process conditional types in a special way because for visibility purposes we need to push a new enclosingDeclaration
                    // just for the `infer` types in the true branch. It's an implicit declaration scope that only applies to _part_ of the type.
                    var inputNode = (ConditionalTypeNode)input;
                    var checkType = (ITypeNode?)Utilities.VisitNode((Node?)inputNode.CheckType, VisitDeclarationSubtree, Utilities.IsTypeNode);
                    var extendsType = (ITypeNode?)Utilities.VisitNode((Node?)inputNode.ExtendsType, VisitDeclarationSubtree, Utilities.IsTypeNode);
                    var oldEnclosingDecl = _enclosingDeclaration;
                    _enclosingDeclaration = (Node)inputNode.TrueType!;
                    var trueType = (ITypeNode?)Utilities.VisitNode((Node?)inputNode.TrueType, VisitDeclarationSubtree, Utilities.IsTypeNode);
                    _enclosingDeclaration = oldEnclosingDecl;
                    var falseType = (ITypeNode?)Utilities.VisitNode((Node?)inputNode.FalseType, VisitDeclarationSubtree, Utilities.IsTypeNode);
                    Debug.Assert(checkType is not null);
                    Debug.Assert(extendsType is not null);
                    Debug.Assert(trueType is not null);
                    Debug.Assert(falseType is not null);
                    return Cleanup(_factory.UpdateConditionalTypeNode(inputNode, checkType, extendsType, trueType, falseType));
                }
                case SyntaxKind.FunctionType:
                {
                    var inputNode = (FunctionTypeNode)input;
                    return Cleanup(_factory.UpdateFunctionTypeNode(
                        inputNode,
                        Utilities.VisitNodes(inputNode.TypeParameters, VisitDeclarationSubtree, NodeTests.IsTypeParameterDeclaration),
                        UpdateParamsList(inputNode, inputNode.Parameters),
                        (ITypeNode?)Debug.CheckDefined(Utilities.VisitNode((Node?)inputNode.Type, VisitDeclarationSubtree, Utilities.IsTypeNode))!));
                }
                case SyntaxKind.ConstructorType:
                {
                    var inputNode = (ConstructorTypeNode)input;
                    return Cleanup(_factory.UpdateConstructorTypeNode(
                        inputNode,
                        DeclarationsTransformSupport.AsModifierTokens(EnsureModifiers(inputNode)),
                        Utilities.VisitNodes(inputNode.TypeParameters, VisitDeclarationSubtree, NodeTests.IsTypeParameterDeclaration),
                        UpdateParamsList(inputNode, inputNode.Parameters),
                        (ITypeNode?)Debug.CheckDefined(Utilities.VisitNode((Node?)inputNode.Type, VisitDeclarationSubtree, Utilities.IsTypeNode))!));
                }
                case SyntaxKind.ImportType:
                {
                    var inputNode = (ImportTypeNode)input;
                    if (!Utilities.IsLiteralImportTypeNode(inputNode)) return Cleanup(inputNode);
                    return Cleanup(_factory.UpdateImportTypeNode(
                        inputNode,
                        _factory.UpdateLiteralTypeNode((LiteralTypeNode)inputNode.Argument!, (UpdateExpression?)RewriteModuleSpecifier(inputNode, ((LiteralTypeNode)inputNode.Argument!).Literal)),
                        inputNode.Attributes,
                        inputNode.Qualifier,
                        Utilities.VisitNodes(inputNode.TypeArguments, VisitDeclarationSubtree, Utilities.IsTypeNode),
                        inputNode.IsTypeOf));
                }
                default:
                    // PORT-NOTE: the reference's `Debug.assertNever(input, …)` falls through to the tuple test and the
                    // general walk; the port states the throw in expression position, which is the same behaviour.
                    return Debug.Fail<object?>($"Attempted to process unhandled node kind: {Debug.FormatSyntaxKind(input.Kind)}");
            }
        }

        if (NodeTests.IsTupleTypeNode(input)
            && (GetLineOfPosition(input.Pos) == GetLineOfPosition(input.End)))
        {
            EmitNodes.SetEmitFlags(input, EmitFlags.SingleLine);
        }

        return Cleanup(Utilities.VisitEachChildWorker(input, VisitDeclarationSubtree, _context));

        object? Cleanup(object? returnValue)
        {
            if (returnValue is not null && canProduceDiagnostic && Utilities.HasDynamicName(input))
            {
                CheckName(input);
            }
            if (isEnclosing)
            {
                _enclosingDeclaration = previousEnclosingDeclaration;
            }
            if (canProduceDiagnostic && !_suppressNewDiagnosticContexts)
            {
                _getSymbolAccessibilityDiagnostic = oldDiag;
            }
            if (shouldEnterSuppressNewDiagnosticsContextContext)
            {
                _suppressNewDiagnosticContexts = oldWithinObjectLiteralType;
            }
            if (ReferenceEquals(returnValue, input))
            {
                return returnValue;
            }
            return returnValue is Node node ? NodeFactory.SetOriginalNode(PreserveJsDoc(node, input), input) : returnValue;
        }
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:1283-1285 — the two <c>getLineAndCharacterOfPosition</c> reads.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>getLineAndCharacterOfPosition(sourceFile, pos)</c> (scanner.ts:548-550) is
    /// the memoized <c>lineMap ??= getLineStarts(sourceFile)</c> plus <c>computeLineAndCharacterOfPosition</c>; the
    /// port's <see cref="ScannerUtilities.GetLineAndCharacterOfPosition"/> takes an <c>ISourceFileLike</c>, which
    /// <c>SourceFile</c> is not, so the two steps are inlined with the same memo — the shape
    /// <c>Checker.Check.Grammar.cs:1153-1154</c> already uses.</remarks>
    private int GetLineOfPosition(int pos)
        => ScannerUtilities.ComputeLineAndCharacterOfPosition(
            _currentSourceFile.LineMap ?? (_currentSourceFile.LineMap = ScannerUtilities.ComputeLineStarts(_currentSourceFile.Text ?? "")),
            pos).Line;

    /// <summary>ported from: src/compiler/transformers/declarations.ts:1290-1292 — <c>isPrivateMethodTypeParameter</c>.</summary>
    private static bool IsPrivateMethodTypeParameter(TypeParameterDeclaration node)
        => node.Parent!.Kind == SyntaxKind.MethodDeclaration && Utilities.HasEffectiveModifier(node.Parent, ModifierFlags.Private);

    /// <summary>ported from: src/compiler/transformers/declarations.ts:1294-1349 — <c>visitDeclarationStatements</c>.</summary>
    /// <remarks>PORT-NOTE: the two union arms the reference tests with guards (<c>isExportDeclaration</c>,
    /// <c>isExportAssignment</c>) are the <c>switch</c> cases here, and the <c>input.expression.kind</c> read narrows
    /// through the <c>ExportAssignment</c> case. Its <c>getSymbolAccessibilityDiagnostic = () =&gt; ({ … })</c> literal is
    /// the <see cref="SymbolAccessibilityDiagnostic"/> this module's factory builds.</remarks>
    private object? VisitDeclarationStatements(Node input)
    {
        if (!DeclarationsTransformSupport.IsPreservedDeclarationStatement(input))
        {
            // return undefined for unmatched kinds to omit them from the tree
            return null;
        }
        if (ShouldStripInternal(input)) return null;
        switch (input.Kind)
        {
            case SyntaxKind.ExportDeclaration:
            {
                var exportDeclaration = (ExportDeclaration)input;
                if (NodeTests.IsSourceFile(input.Parent!))
                {
                    _resultHasExternalModuleIndicator = true;
                }
                _resultHasScopeMarker = true;
                // Rewrite external module names if necessary
                return _factory.UpdateExportDeclaration(
                    exportDeclaration,
                    exportDeclaration.Modifiers,
                    exportDeclaration.IsTypeOnly,
                    exportDeclaration.ExportClause,
                    RewriteModuleSpecifier(exportDeclaration, exportDeclaration.ModuleSpecifier) as Expression,
                    TryGetResolutionModeOverride(exportDeclaration.Attributes));
            }
            case SyntaxKind.ExportAssignment:
            {
                var exportAssignment = (ExportAssignment)input;
                // Always visible if the parent node isn't dropped for being not visible
                if (NodeTests.IsSourceFile(input.Parent!))
                {
                    _resultHasExternalModuleIndicator = true;
                }
                _resultHasScopeMarker = true;
                if (exportAssignment.Expression!.Kind == SyntaxKind.Identifier)
                {
                    return input;
                }
                else
                {
                    var newId = _factory.CreateUniqueName("_default", GeneratedIdentifierFlags.Optimistic);
                    _getSymbolAccessibilityDiagnostic = _ => new DeclarationsDiagnostics.SymbolAccessibilityDiagnostic
                    {
                        DiagnosticMessage = Diagnostics.Default_export_of_the_module_has_or_is_using_private_name_0,
                        ErrorNode = exportAssignment,
                    };
                    _errorFallbackNode = exportAssignment;
                    var type = EnsureType(exportAssignment, /*ignorePrivate*/ null);
                    var varDecl = _factory.CreateVariableDeclaration(newId, /*exclamationToken*/ null, type, /*initializer*/ null);
                    _errorFallbackNode = null;
                    var statement = _factory.CreateVariableStatement(
                        _needsDeclare ? [_factory.CreateModifier(SyntaxKind.DeclareKeyword)] : [],
                        _factory.CreateVariableDeclarationList([varDecl], NodeFlags.Const));

                    PreserveJsDoc(statement, exportAssignment);
                    EmitNodes.RemoveAllComments(exportAssignment);
                    return new object?[]
                    {
                        statement,
                        _factory.UpdateExportAssignment(exportAssignment, exportAssignment.Modifiers, newId),
                    };
                }
            }
        }

        var result = TransformTopLevelDeclaration(input);
        // Don't actually transform yet; just leave as original node - will be elided/swapped by late pass
        _lateStatementReplacementMap![TransformationUtilities.GetOriginalNodeId(input)] = result;
        return input;
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:1351-1360 — <c>stripExportModifiers</c>.</summary>
    private Node StripExportModifiers(Node statement)
    {
        if (NodeTests.IsImportEqualsDeclaration(statement) || Utilities.HasEffectiveModifier(statement, ModifierFlags.Default) || !Utilities.CanHaveModifiers(statement))
        {
            // `export import` statements should remain as-is, as imports are _not_ implicitly exported in an ambient namespace
            // Likewise, `export default` classes and the like and just be `default`, so we preserve their `export` modifiers, too
            return statement;
        }

        var modifiers = _factory.CreateModifiersFromModifierFlags(Utilities.GetEffectiveModifierFlags(statement) & (ModifierFlags.All ^ ModifierFlags.Export));
        return _factory.ReplaceModifiers(statement, modifiers);
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:1362-1385 — <c>updateModuleDeclarationAndKeyword</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is the union <c>ModifierLike[] | undefined</c> (the port's
    /// <c>List&lt;Node&gt;?</c>) and <c>ModuleName</c>/<c>ModuleBody</c>, which this model spells
    /// <see cref="MemberExpression"/> and <see cref="Node"/>; the reference's <c>updated.flags</c> read is
    /// <see cref="Node.Flags"/>.</remarks>
    private ModuleDeclaration UpdateModuleDeclarationAndKeyword(
        ModuleDeclaration node,
        List<Node>? modifiers,
        MemberExpression name,
        Node? body)
    {
        var updated = _factory.UpdateModuleDeclaration(node, modifiers, name, body);

        if (Utilities.IsAmbientModule(updated) || (updated.Flags & NodeFlags.Namespace) != 0)
        {
            return updated;
        }

        var fixedNode = _factory.CreateModuleDeclaration(
            updated.Modifiers,
            updated.Name,
            updated.Body,
            updated.Flags | NodeFlags.Namespace);

        NodeFactory.SetOriginalNode(fixedNode, updated);
        Utilities.SetTextRange(fixedNode, updated);

        return fixedNode;
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:1387-1758 — <c>transformTopLevelDeclaration</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the reference's parameter is the union <c>LateVisibilityPaintedStatement</c>, which the port states as
    /// <see cref="Node"/> plus <see cref="Utilities.IsLateVisibilityPaintedStatement"/> at the one call site that has
    /// to establish it (<c>transformAndReplaceLatePaintedStatements</c>); the unhandled kinds reach
    /// <c>Debug.assertNever</c>, the same throw as the reference's.
    /// </para>
    /// <para>
    /// PORT-NOTE: the four <c>cleanup</c> arms and the reference's nested <c>walkBindingPattern</c> are local functions
    /// so they close over the same saved state; the <c>overloadSignatures</c> reads the reference makes on
    /// <c>p.valueDeclaration</c> go through the same <see cref="IDeclaration"/>/kind-test pairs the members file uses.
    /// </para>
    /// </remarks>
    private object? TransformTopLevelDeclaration(Node input)
    {
        if (_lateMarkedStatements is not null)
        {
            while (Core.OrderedRemoveItem(_lateMarkedStatements, input)) { }
        }
        if (ShouldStripInternal(input)) return null;
        switch (input.Kind)
        {
            case SyntaxKind.ImportEqualsDeclaration:
            {
                return TransformImportEqualsDeclaration((ImportEqualsDeclaration)input);
            }
            case SyntaxKind.ImportDeclaration:
            {
                return TransformImportDeclaration((ImportDeclaration)input);
            }
        }
        if (Utilities.IsDeclaration(input) && IsDeclarationAndNotVisible(input)) return null;

        if (DeclarationsTransformSupport.IsJSDocImportTag(input)) return null;

        // Elide implementation signatures from overload sets
        if (Utilities.IsFunctionLike(input) && Js.IsTruthy(_resolver.IsImplementationOfOverload(input))) return null;

        Node? previousEnclosingDeclaration = null;
        var isEnclosing = IsEnclosingDeclaration(input);
        if (isEnclosing)
        {
            previousEnclosingDeclaration = _enclosingDeclaration;
            _enclosingDeclaration = input;
        }

        var canProdiceDiagnostic = DeclarationsDiagnostics.CanProduceDiagnostics(input);
        var oldDiag = _getSymbolAccessibilityDiagnostic;
        if (canProdiceDiagnostic)
        {
            _getSymbolAccessibilityDiagnostic = DeclarationsDiagnostics.CreateGetSymbolAccessibilityDiagnosticForNode(input);
        }

        var previousNeedsDeclare = _needsDeclare;
        switch (input.Kind)
        {
            case SyntaxKind.TypeAliasDeclaration:
            {
                var typeAlias = (TypeAliasDeclaration)input;
                _needsDeclare = false;
                var clean = Cleanup(_factory.UpdateTypeAliasDeclaration(
                    typeAlias,
                    EnsureModifiers(typeAlias),
                    typeAlias.Name,
                    Utilities.VisitNodes(typeAlias.TypeParameters, VisitDeclarationSubtree, NodeTests.IsTypeParameterDeclaration),
                    (ITypeNode?)Debug.CheckDefined(Utilities.VisitNode((Node?)typeAlias.Type, VisitDeclarationSubtree, Utilities.IsTypeNode))!));
                _needsDeclare = previousNeedsDeclare;
                return clean;
            }
            case SyntaxKind.InterfaceDeclaration:
            {
                var interfaceDeclaration = (InterfaceDeclaration)input;
                return Cleanup(_factory.UpdateInterfaceDeclaration(
                    interfaceDeclaration,
                    EnsureModifiers(interfaceDeclaration),
                    interfaceDeclaration.Name,
                    EnsureTypeParams(interfaceDeclaration, interfaceDeclaration.TypeParameters),
                    TransformHeritageClauses(interfaceDeclaration.HeritageClauses),
                    Utilities.VisitNodes(interfaceDeclaration.Members, VisitDeclarationSubtree, Utilities.IsTypeElement)));
            }
            case SyntaxKind.FunctionDeclaration:
            {
                var functionDeclaration = (FunctionDeclaration)input;
                // Generators lose their generator-ness, excepting their return type
                var clean = Cleanup(_factory.UpdateFunctionDeclaration(
                    functionDeclaration,
                    EnsureModifiers(functionDeclaration),
                    /*asteriskToken*/ null,
                    functionDeclaration.Name,
                    EnsureTypeParams(functionDeclaration, functionDeclaration.TypeParameters),
                    UpdateParamsList(functionDeclaration, functionDeclaration.Parameters),
                    EnsureType(functionDeclaration),
                    /*body*/ null));
                if (clean is FunctionDeclaration cleanDeclaration
                    && _resolver.IsExpandoFunctionDeclaration(functionDeclaration)
                    && ShouldEmitFunctionProperties(functionDeclaration))
                {
                    var props = _resolver.GetPropertiesOfContainerFunction(functionDeclaration);

                    if (_isolatedDeclarations)
                    {
                        ReportExpandoFunctionErrors(functionDeclaration);
                    }
                    // Use parseNodeFactory so it is usable as an enclosing declaration
                    var fakespace = FactoryUtilities.ParseNodeFactory.CreateModuleDeclaration(
                        /*modifiers*/ null,
                        cleanDeclaration.Name ?? _factory.CreateIdentifier("_default"),
                        _factory.CreateModuleBlock([]),
                        NodeFlags.Namespace);
                    Utilities.SetParent(fakespace, _enclosingDeclaration!);
                    fakespace.Locals = Utilities.CreateSymbolTable(props);
                    fakespace.Symbol = props[0].Parent!;
                    var exportMappings = new List<(Identifier Generated, string Exported)>();
                    var declarations = Core.MapDefined(props, (ISymbol p, int _) =>
                    {
                        if (!Utilities.IsExpandoPropertyDeclaration(p.ValueDeclaration))
                        {
                            return null; // unique symbol or non-identifier name - omit, since there's no syntax that can preserve it
                        }
                        var nameStr = Utilities.UnescapeLeadingUnderscores(p.EscapedName!);
                        if (!DeclarationsTransformSupport.IsIdentifierText(nameStr, ScriptTarget.ESNext, LanguageVariant.Standard))
                        {
                            return null; // unique symbol or non-identifier name - omit, since there's no syntax that can preserve it
                        }
                        _getSymbolAccessibilityDiagnostic = DeclarationsDiagnostics.CreateGetSymbolAccessibilityDiagnosticForNode((Node)p.ValueDeclaration!);
                        var type = _resolver.CreateTypeOfDeclaration((Node)p.ValueDeclaration!, fakespace, DeclarationsTransform.DeclarationEmitNodeBuilderFlags, DeclarationsTransform.DeclarationEmitInternalNodeBuilderFlags | InternalNodeBuilderFlags.NoSyntacticPrinter, _symbolTracker);
                        _getSymbolAccessibilityDiagnostic = oldDiag;
                        var isNonContextualKeywordName = Utilities.IsStringANonContextualKeyword(nameStr);
                        var name = isNonContextualKeywordName ? _factory.GetGeneratedNameForNode((Node)p.ValueDeclaration!, /*flags*/ null) : _factory.CreateIdentifier(nameStr);
                        if (isNonContextualKeywordName)
                        {
                            exportMappings.Add((name, nameStr));
                        }
                        var varDecl = _factory.CreateVariableDeclaration(name, /*exclamationToken*/ null, type, /*initializer*/ null);
                        return (IStatement)_factory.CreateVariableStatement(
                            isNonContextualKeywordName ? null : [_factory.CreateToken(SyntaxKind.ExportKeyword)],
                            _factory.CreateVariableDeclarationList([varDecl], /*flags*/ null));
                    });
                    if (exportMappings.Count == 0)
                    {
                        declarations = Core.MapDefined(declarations, (IStatement declaration, int _) => (IStatement)_factory.ReplaceModifiers((Node)declaration, ModifierFlags.None));
                    }
                    else
                    {
                        declarations.Add(_factory.CreateExportDeclaration(
                            /*modifiers*/ null,
                            /*isTypeOnly*/ false,
                            _factory.CreateNamedExports(Core.Map(exportMappings, (mapping, _) => _factory.CreateExportSpecifier(
                                /*isTypeOnly*/ false,
                                mapping.Generated,
                                mapping.Exported))),
                            /*moduleSpecifier*/ null,
                            /*attributes*/ null));
                    }
                    var namespaceDecl = _factory.CreateModuleDeclaration(EnsureModifiers(functionDeclaration), functionDeclaration.Name!, _factory.CreateModuleBlock(declarations), NodeFlags.Namespace);
                    if (!Utilities.HasEffectiveModifier(cleanDeclaration, ModifierFlags.Default))
                    {
                        return new object?[] { cleanDeclaration, namespaceDecl };
                    }

                    var modifiers = _factory.CreateModifiersFromModifierFlags((Utilities.GetEffectiveModifierFlags(cleanDeclaration) & ~ModifierFlags.ExportDefault) | ModifierFlags.Ambient);
                    var cleanDeclaration2 = _factory.UpdateFunctionDeclaration(
                        cleanDeclaration,
                        DeclarationsTransformSupport.AsNodeList(modifiers),
                        /*asteriskToken*/ null,
                        cleanDeclaration.Name,
                        cleanDeclaration.TypeParameters,
                        cleanDeclaration.Parameters,
                        cleanDeclaration.Type,
                        /*body*/ null);

                    var namespaceDeclaration = _factory.UpdateModuleDeclaration(
                        namespaceDecl,
                        DeclarationsTransformSupport.AsNodeList(modifiers),
                        namespaceDecl.Name,
                        namespaceDecl.Body);

                    var exportDefaultDeclaration = _factory.CreateExportAssignment(
                        /*modifiers*/ null,
                        /*isExportEquals*/ false,
                        namespaceDecl.Name!);

                    if (NodeTests.IsSourceFile(input.Parent!))
                    {
                        _resultHasExternalModuleIndicator = true;
                    }
                    _resultHasScopeMarker = true;

                    return new object?[] { cleanDeclaration2, namespaceDeclaration, exportDefaultDeclaration };
                }
                else
                {
                    return clean;
                }
            }
            case SyntaxKind.ModuleDeclaration:
            {
                var moduleDeclaration = (ModuleDeclaration)input;
                _needsDeclare = false;
                var inner = moduleDeclaration.Body;
                if (inner is not null && inner.Kind == SyntaxKind.ModuleBlock)
                {
                    var moduleBlock = (ModuleBlock)inner;
                    var oldNeedsScopeFix = _needsScopeFixMarker;
                    var oldHasScopeFix = _resultHasScopeMarker;
                    _resultHasScopeMarker = false;
                    _needsScopeFixMarker = false;
                    var statements = Utilities.VisitNodes(moduleBlock.Statements, VisitDeclarationStatements, Utilities.IsStatement)!;
                    var lateStatements = TransformAndReplaceLatePaintedStatements(statements);
                    if ((input.Flags & NodeFlags.Ambient) != 0)
                    {
                        _needsScopeFixMarker = false; // If it was `declare`'d everything is implicitly exported already, ignore late printed "privates"
                    }
                    // With the final list of statements, there are 3 possibilities:
                    // 1. There's an export assignment or export declaration in the namespace - do nothing
                    // 2. Everything is exported and there are no export assignments or export declarations - strip all export modifiers
                    // 3. Some things are exported, some are not, and there's no marker - add an empty marker
                    if (!Utilities.IsGlobalScopeAugmentation(moduleDeclaration) && !HasScopeMarker(lateStatements) && !_resultHasScopeMarker)
                    {
                        if (_needsScopeFixMarker)
                        {
                            lateStatements = _factory.CreateNodeArray([.. lateStatements, FactoryUtilities.CreateEmptyExports(_factory)], null);
                        }
                        else
                        {
                            lateStatements = Utilities.VisitNodes(lateStatements, StripExportModifiers, Utilities.IsStatement)!;
                        }
                    }
                    var body = _factory.UpdateModuleBlock(moduleBlock, lateStatements);
                    _needsDeclare = previousNeedsDeclare;
                    _needsScopeFixMarker = oldNeedsScopeFix;
                    _resultHasScopeMarker = oldHasScopeFix;
                    var mods = EnsureModifiers(moduleDeclaration);

                    return Cleanup(UpdateModuleDeclarationAndKeyword(
                        moduleDeclaration,
                        mods,
                        Utilities.IsExternalModuleAugmentation(moduleDeclaration)
                            ? (MemberExpression)RewriteModuleSpecifier(moduleDeclaration, moduleDeclaration.Name)!
                            : moduleDeclaration.Name!,
                        body));
                }
                else
                {
                    _needsDeclare = previousNeedsDeclare;
                    var mods = EnsureModifiers(moduleDeclaration);
                    _needsDeclare = false;
                    Utilities.VisitNode(inner, VisitDeclarationStatements);
                    // eagerly transform nested namespaces (the nesting doesn't need any elision or painting done)
                    var id = TransformationUtilities.GetOriginalNodeId(inner!); // TODO: GH#18217
                    _lateStatementReplacementMap!.TryGetValue(id, out var body);
                    _lateStatementReplacementMap.Remove(id);
                    return Cleanup(UpdateModuleDeclarationAndKeyword(
                        moduleDeclaration,
                        mods,
                        moduleDeclaration.Name!,
                        (Node?)body));
                }
            }
            case SyntaxKind.ClassDeclaration:
            {
                var classDeclaration = (ClassDeclaration)input;
                _errorNameNode = classDeclaration.Name;
                _errorFallbackNode = classDeclaration;
                var modifiers = _factory.CreateNodeArray(EnsureModifiers(classDeclaration), null);
                var typeParameters = EnsureTypeParams(classDeclaration, classDeclaration.TypeParameters);
                var ctor = Utilities.GetFirstConstructorWithBody(classDeclaration);
                var parameterProperties = CollectParameterProperties(ctor);
                var hasPrivateIdentifier = Core.Some(classDeclaration.Members, (IClassElement member, int _) => ((INamedDeclaration)member).Name is { } memberName && NodeTests.IsPrivateIdentifier(memberName));
                // When the class has at least one private identifier, create a unique constant identifier to retain the nominal typing behavior
                // Prevents other classes with the same public members from being used in place of the current class
                var privateIdentifier = hasPrivateIdentifier
                    ? new List<PropertyDeclaration>
                    {
                        _factory.CreatePropertyDeclaration(
                            /*modifiers*/ null,
                            _factory.CreatePrivateIdentifier("#private"),
                            /*questionOrExclamationToken*/ null,
                            /*type*/ null,
                            /*initializer*/ null),
                    }
                    : null;
                var lateIndexes = _resolver.CreateLateBoundIndexSignatures(classDeclaration, _enclosingDeclaration, DeclarationsTransform.DeclarationEmitNodeBuilderFlags, DeclarationsTransform.DeclarationEmitInternalNodeBuilderFlags, _symbolTracker);
                var memberNodes = new List<IClassElement>();
                if (privateIdentifier is not null) memberNodes.AddRange(privateIdentifier);
                if (lateIndexes is not null) memberNodes.AddRange(lateIndexes.Cast<IClassElement>());
                if (parameterProperties is not null) memberNodes.AddRange(parameterProperties);
                memberNodes.AddRange(Utilities.VisitNodes(classDeclaration.Members, VisitDeclarationSubtree, Utilities.IsClassElement)!);
                var members = _factory.CreateNodeArray(memberNodes, null);

                var extendsClause = Utilities.GetEffectiveBaseTypeNode(classDeclaration);
                if (extendsClause is not null && !Utilities.IsEntityNameExpression(extendsClause.Expression!) && extendsClause.Expression!.Kind != SyntaxKind.NullKeyword)
                {
                    // We must add a temporary declaration for the extends clause expression

                    var oldId = classDeclaration.Name is not null ? Utilities.UnescapeLeadingUnderscores(classDeclaration.Name.EscapedText!) : "default";
                    var newId = _factory.CreateUniqueName($"{oldId}_base", GeneratedIdentifierFlags.Optimistic);
                    _getSymbolAccessibilityDiagnostic = _ => new DeclarationsDiagnostics.SymbolAccessibilityDiagnostic
                    {
                        DiagnosticMessage = Diagnostics.extends_clause_of_exported_class_0_has_or_is_using_private_name_1,
                        ErrorNode = extendsClause,
                        TypeName = classDeclaration.Name,
                    };
                    var varDecl = _factory.CreateVariableDeclaration(
                        newId,
                        /*exclamationToken*/ null,
                        _resolver.CreateTypeOfExpression(extendsClause.Expression, classDeclaration, DeclarationsTransform.DeclarationEmitNodeBuilderFlags, DeclarationsTransform.DeclarationEmitInternalNodeBuilderFlags, _symbolTracker),
                        /*initializer*/ null);
                    var statement = _factory.CreateVariableStatement(
                        _needsDeclare ? [_factory.CreateModifier(SyntaxKind.DeclareKeyword)] : [],
                        _factory.CreateVariableDeclarationList([varDecl], NodeFlags.Const));
                    var heritageClauses = _factory.CreateNodeArray(Core.Map(classDeclaration.HeritageClauses!, (HeritageClause clause, int _) =>
                    {
                        if (clause.Token == SyntaxKind.ExtendsKeyword)
                        {
                            var oldDiag3 = _getSymbolAccessibilityDiagnostic;
                            _getSymbolAccessibilityDiagnostic = DeclarationsDiagnostics.CreateGetSymbolAccessibilityDiagnosticForNode(clause.Types![0]);
                            var newClause = _factory.UpdateHeritageClause(clause, Core.Map(clause.Types!, (ExpressionWithTypeArguments t, int _) => _factory.UpdateExpressionWithTypeArguments(t, newId, Utilities.VisitNodes(t.TypeArguments, VisitDeclarationSubtree, Utilities.IsTypeNode))));
                            _getSymbolAccessibilityDiagnostic = oldDiag3;
                            return newClause;
                        }
                        return _factory.UpdateHeritageClause(clause, Utilities.VisitNodes(
                            _factory.CreateNodeArray(Core.Filter(clause.Types, (ExpressionWithTypeArguments t) => Utilities.IsEntityNameExpression(t.Expression!) || t.Expression!.Kind == SyntaxKind.NullKeyword), null),
                            VisitDeclarationSubtree,
                            NodeTests.IsExpressionWithTypeArguments));
                    }), null);
                    return new object?[]
                    {
                        statement,
                        Cleanup(_factory.UpdateClassDeclaration(
                            classDeclaration,
                            modifiers,
                            classDeclaration.Name,
                            typeParameters,
                            heritageClauses,
                            members))!,
                    }; // TODO: GH#18217
                }
                else
                {
                    var heritageClauses = TransformHeritageClauses(classDeclaration.HeritageClauses);
                    return Cleanup(_factory.UpdateClassDeclaration(
                        classDeclaration,
                        modifiers,
                        classDeclaration.Name,
                        typeParameters,
                        heritageClauses,
                        members));
                }
            }
            case SyntaxKind.VariableStatement:
            {
                return Cleanup(TransformVariableStatement((VariableStatement)input));
            }
            case SyntaxKind.EnumDeclaration:
            {
                var enumDeclaration = (EnumDeclaration)input;
                return Cleanup(_factory.UpdateEnumDeclaration(
                    enumDeclaration,
                    _factory.CreateNodeArray(EnsureModifiers(enumDeclaration), null),
                    enumDeclaration.Name,
                    _factory.CreateNodeArray(Core.MapDefined(enumDeclaration.Members!, (EnumMember m, int _) =>
                    {
                        if (ShouldStripInternal(m)) return null;
                        // Rewrite enum values to their constants, if available
                        var enumValue = _resolver.GetEnumMemberValue(m);
                        var constValue = enumValue?.Value;
                        if (
                            _isolatedDeclarations && m.Initializer is not null && Js.IsTruthy(enumValue?.HasExternalReferences) &&
                            // This will be its own compiler error instead, so don't report.
                            !NodeTests.IsComputedPropertyName(m.Name!)
                        )
                        {
                            _context.AddDiagnostic(Utilities.CreateDiagnosticForNode(m, Diagnostics.Enum_member_initializers_must_be_computable_without_references_to_external_symbols_with_isolatedDeclarations));
                        }
                        Expression? newInitializer = constValue switch
                        {
                            null => null,
                            string constString => _factory.CreateStringLiteral(constString),
                            int constInt => EnumNumericLiteral(constInt),
                            double constNumber => EnumNumericLiteral(constNumber),
                            _ => null,
                        };
                        return PreserveJsDoc(_factory.UpdateEnumMember(m, m.Name, newInitializer), m);
                    }), null)));
            }
        }
        // Anything left unhandled is an error, so this should be unreachable
        return Debug.Fail<object?>($"Unhandled top-level node in declaration emit: {Debug.FormatSyntaxKind(input.Kind)}");

        object? Cleanup(object? node)
        {
            if (isEnclosing)
            {
                _enclosingDeclaration = previousEnclosingDeclaration;
            }
            if (canProdiceDiagnostic)
            {
                _getSymbolAccessibilityDiagnostic = oldDiag;
            }
            if (input.Kind == SyntaxKind.ModuleDeclaration)
            {
                _needsDeclare = previousNeedsDeclare;
            }
            if (ReferenceEquals(node, input))
            {
                return node;
            }
            _errorFallbackNode = null;
            _errorNameNode = null;
            return node is Node nodeValue ? NodeFactory.SetOriginalNode(PreserveJsDoc(nodeValue, input), input) : node;
        }
    }

    /// <summary>The enum-member literal of <c>transformTopLevelDeclaration</c>'s <c>EnumDeclaration</c> arm (declarations.ts:1720-1725).</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>constValue &lt; 0 ? createPrefixUnaryExpression(MinusToken, createNumericLiteral(-constValue))
    /// : createNumericLiteral(constValue)</c> hands <c>createNumericLiteral</c> a JS number, which that factory writes as
    /// <c>value + ""</c>. The port's <c>NodeFactory.CreateNumericLiteral</c> takes an <c>int</c> or a <c>string</c>
    /// (<c>Parser/NodeFactory.cs:155</c>), so the number-to-text step is stated here: an integral value in the
    /// <c>int</c> range is passed as that <c>int</c>, and anything else as its invariant round-trip text (the same text
    /// for the checker's evaluator values, which are enum member constants).
    /// </remarks>
    private static object EnumNumericLiteralValue(double value)
        => value >= int.MinValue && value <= int.MaxValue && Math.Floor(value) == value
            ? (int)value
            : value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    Expression EnumNumericLiteral(double value) => value < 0
        ? _factory.CreatePrefixUnaryExpression(SyntaxKind.MinusToken, _factory.CreateNumericLiteral(EnumNumericLiteralValue(-value)))
        : _factory.CreateNumericLiteral(EnumNumericLiteralValue(value));

    /// <summary>ported from: src/compiler/transformers/declarations.ts:1760-1777 — <c>transformVariableStatement</c>.</summary>
    /// <remarks>PORT-NOTE: <c>forEach(declarations, getBindingNameVisible)</c> is <see cref="Core.ForEach{T,TU}"/> over
    /// the same member, and the reference's <c>isVarUsing</c>/<c>isVarAwaitUsing</c> are
    /// <see cref="DeclarationsTransformSupport.IsVarUsing"/>/<see cref="DeclarationsTransformSupport.IsVarAwaitUsing"/>.</remarks>
    private VariableStatement? TransformVariableStatement(VariableStatement input)
    {
        var anyVisible = false;
        foreach (var declaration in input.DeclarationList!.Declarations!)
        {
            if (GetBindingNameVisible(declaration)) { anyVisible = true; break; }
        }
        if (!anyVisible) return null;
        var nodes = Utilities.VisitNodes(input.DeclarationList.Declarations, VisitDeclarationSubtree, NodeTests.IsVariableDeclaration)!;
        if (nodes.Count == 0) return null;

        var modifiers = _factory.CreateNodeArray(EnsureModifiers(input), null);
        VariableDeclarationList declList;
        if (DeclarationsTransformSupport.IsVarUsing(input.DeclarationList) || DeclarationsTransformSupport.IsVarAwaitUsing(input.DeclarationList))
        {
            declList = _factory.CreateVariableDeclarationList(nodes, NodeFlags.Const);
            NodeFactory.SetOriginalNode(declList, input.DeclarationList);
            Utilities.SetTextRange(declList, input.DeclarationList);
            EmitNodes.SetCommentRange(declList, input.DeclarationList);
        }
        else
        {
            declList = _factory.UpdateVariableDeclarationList(input.DeclarationList, nodes);
        }
        return _factory.UpdateVariableStatement(input, modifiers, declList);
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:1779-1781 — <c>recreateBindingPattern</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>flatten(mapDefined(d.elements, recreateBindingElement))</c> is the loop
    /// below; <c>d.elements</c> is read through the same kind switch <see cref="FilterBindingPatternInitializers"/>
    /// uses, because the model has no <c>BindingPattern</c> base.</remarks>
    private List<VariableDeclaration> RecreateBindingPattern(Node d)
    {
        var result = new List<VariableDeclaration>();
        IEnumerable<Node> elements = d.Kind == SyntaxKind.ArrayBindingPattern
            ? ((ArrayBindingPattern)d).Elements!
            : ((ObjectBindingPattern)d).Elements!;
        foreach (var element in elements)
        {
            var recreated = RecreateBindingElement(element);
            if (recreated is VariableDeclaration single)
            {
                result.Add(single);
            }
            else if (recreated is List<VariableDeclaration> many)
            {
                result.AddRange(many);
            }
        }
        return result;
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:1783-1797 — <c>recreateBindingElement</c>.</summary>
    private object? RecreateBindingElement(Node e)
    {
        if (e.Kind == SyntaxKind.OmittedExpression)
        {
            return null;
        }
        var name = ((INamedDeclaration)e).Name;
        if (name is not null)
        {
            if (!GetBindingNameVisible(e)) return null;
            if (Utilities.IsBindingPattern(name))
            {
                return RecreateBindingPattern(name);
            }
            else
            {
                return _factory.CreateVariableDeclaration(name, /*exclamationToken*/ null, EnsureType(e), /*initializer*/ null);
            }
        }
        return null;
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:1798-1813 — <c>checkName</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's `node as NamedDeclaration as LateBoundDeclaration` narrows twice; the port
    /// reads the name through <see cref="INamedDeclaration"/> and its `.expression` through the
    /// <see cref="ComputedPropertyName"/> the <c>hasDynamicName</c> guard established. TS's <c>Debug.assert</c> is
    /// <see cref="Debug.Assert"/>.</remarks>
    private void CheckName(Node node)
    {
        DeclarationsDiagnostics.GetSymbolAccessibilityDiagnostic? oldDiag = null;
        if (!_suppressNewDiagnosticContexts)
        {
            oldDiag = _getSymbolAccessibilityDiagnostic;
            _getSymbolAccessibilityDiagnostic = DeclarationsDiagnostics.CreateGetSymbolAccessibilityDiagnosticForNodeName(node)!;
        }
        _errorNameNode = ((INamedDeclaration)node).Name;
        Debug.Assert(Utilities.HasDynamicName(node)); // Should only be called with dynamic names
        var entityName = ((ComputedPropertyName)((INamedDeclaration)node).Name!).Expression!;
        CheckEntityNameVisibility(entityName, _enclosingDeclaration!);
        if (!_suppressNewDiagnosticContexts)
        {
            _getSymbolAccessibilityDiagnostic = oldDiag!;
        }
        _errorNameNode = null;
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:1815-1817 — <c>shouldStripInternal</c>.</summary>
    private bool ShouldStripInternal(Node node)
        => _stripInternal && node is not null && DeclarationsTransformSupport.IsInternalDeclaration(node, _currentSourceFile);

    /// <summary>ported from: src/compiler/transformers/declarations.ts:1819-1821 — <c>isScopeMarker</c>.</summary>
    private static bool IsScopeMarker(Node node)
        => NodeTests.IsExportAssignment(node) || NodeTests.IsExportDeclaration(node);

    /// <summary>ported from: src/compiler/transformers/declarations.ts:1823-1825 — <c>hasScopeMarker</c>.</summary>
    private static bool HasScopeMarker(IList<IStatement> statements)
        => Core.Some(statements, (IStatement statement, int _) => IsScopeMarker((Node)statement));

    /// <summary>ported from: src/compiler/transformers/declarations.ts:1827-1834 — <c>ensureModifiers</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>node.modifiers</c> is one structural slot over <c>T extends HasModifiers</c>; the model
    /// declares the slot on each of the 30 nodes that carry one, so the read is
    /// <see cref="Utilities.ModifiersOf"/> (the same switch the binder records). The reference's
    /// <c>visitArray(node.modifiers, n =&gt; tryCast(n, isModifier), isModifier)</c> is
    /// <see cref="DeclarationsTransformSupport.FilterModifiers"/>, which keeps the array's identity when nothing was
    /// dropped (the reference's <c>visitArray</c> does the same).
    /// </remarks>
    private List<Node>? EnsureModifiers(Node node)
    {
        var currentFlags = Utilities.GetEffectiveModifierFlags(node);
        var newFlags = EnsureModifierFlags(node);
        if (currentFlags == newFlags)
        {
            return DeclarationsTransformSupport.FilterModifiers(node);
        }
        return DeclarationsTransformSupport.AsNodeList(_factory.CreateModifiersFromModifierFlags(newFlags));
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:1836-1845 — <c>ensureModifierFlags</c>.</summary>
    private ModifierFlags EnsureModifierFlags(Node node)
    {
        var mask = ModifierFlags.All ^ (ModifierFlags.Public | ModifierFlags.Async | ModifierFlags.Override); // No async and override modifiers in declaration files
        var additions = (_needsDeclare && !DeclarationsTransformSupport.IsAlwaysType(node)) ? ModifierFlags.Ambient : ModifierFlags.None;
        var parentIsFile = node.Parent is not null && node.Parent.Kind == SyntaxKind.SourceFile;
        if (!parentIsFile || (_isBundledEmit && parentIsFile && Parser.IsExternalModule((SourceFile)node.Parent!)))
        {
            mask ^= ModifierFlags.Ambient;
            additions = ModifierFlags.None;
        }
        return DeclarationsTransformSupport.MaskModifierFlags(node, mask, additions);
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:1587-1636 — the <c>parameterProperties</c> block of <c>transformTopLevelDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>compact(flatMap(ctor.parameters, param =&gt; …))</c> and its nested
    /// <c>walkBindingPattern</c> are stated as the two members here — the flat map's two return shapes (a single
    /// declaration and an array of them) are the <c>object?</c> list the reference's <c>flatMap</c> produces, and
    /// <c>compact</c>'s undefined-drop is the same one the loop makes.
    /// </remarks>
    private List<PropertyDeclaration>? CollectParameterProperties(ConstructorDeclaration? ctor)
    {
        if (ctor is null) return null;
        var oldDiag = _getSymbolAccessibilityDiagnostic;
        var flat = new List<object?>();
        foreach (var param in ctor.Parameters!)
        {
            if (!Utilities.HasSyntacticModifier(param, ModifierFlags.ParameterPropertyModifier) || ShouldStripInternal(param)) continue;
            _getSymbolAccessibilityDiagnostic = DeclarationsDiagnostics.CreateGetSymbolAccessibilityDiagnosticForNode(param);
            if (param.Name!.Kind == SyntaxKind.Identifier)
            {
                flat.Add(PreserveJsDoc(
                    _factory.CreatePropertyDeclaration(
                        EnsureModifiers(param),
                        param.Name,
                        param.QuestionToken,
                        EnsureType(param),
                        EnsureNoInitializer(param)),
                    param));
            }
            else
            {
                // Pattern - this is currently an error, but we emit declarations for it somewhat correctly
                flat.Add(WalkBindingPattern(param.Name, param));
            }
        }
        _getSymbolAccessibilityDiagnostic = oldDiag;
        var result = new List<PropertyDeclaration>();
        foreach (var item in flat)
        {
            if (item is PropertyDeclaration declaration)
            {
                result.Add(declaration);
            }
            else if (item is List<PropertyDeclaration> many)
            {
                result.AddRange(many);
            }
        }
        return result;
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:1611-1628 — <c>walkBindingPattern</c>.</summary>
    /// <remarks>PORT-NOTE: <c>pattern.elements</c> is read through the same kind switch
    /// <see cref="FilterBindingPatternInitializers"/> uses, because the model has no <c>BindingPattern</c> base; the
    /// reference's <c>concatenate(elems, walkBindingPattern(…))</c> is the <c>AddRange</c> arm.</remarks>
    private List<PropertyDeclaration> WalkBindingPattern(Node pattern, ParameterDeclaration param)
    {
        List<PropertyDeclaration>? elems = null;
        IEnumerable<Node> elements = pattern.Kind == SyntaxKind.ArrayBindingPattern
            ? ((ArrayBindingPattern)pattern).Elements!
            : ((ObjectBindingPattern)pattern).Elements!;
        foreach (var element in elements)
        {
            if (NodeTests.IsOmittedExpression(element)) continue;
            var elementName = ((INamedDeclaration)element).Name!;
            if (Utilities.IsBindingPattern(elementName))
            {
                elems ??= [];
                elems.AddRange(WalkBindingPattern(elementName, param));
                continue;
            }
            elems ??= [];
            elems.Add(_factory.CreatePropertyDeclaration(
                EnsureModifiers(param),
                elementName,
                /*questionOrExclamationToken*/ null,
                EnsureType(element),
                /*initializer*/ null));
        }
        return elems ?? [];
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:1847-1863 — <c>transformHeritageClauses</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>filter(map(nodes, …), clause =&gt; clause.types &amp;&amp; !!clause.types.length)</c>
    /// is the <see cref="Core.Filter{T}"/>/<see cref="Core.Map{T,TU}"/> pair here, and its inner
    /// <c>filter(clause.types, …)</c> feeds the same <c>createNodeArray</c>/<c>visitNodes</c> pair.</remarks>
    private NodeArray<HeritageClause> TransformHeritageClauses(NodeArray<HeritageClause>? nodes)
    {
        var mapped = Core.Map(nodes, (HeritageClause clause, int _) =>
            _factory.UpdateHeritageClause(
                clause,
                Utilities.VisitNodes(
                    _factory.CreateNodeArray(
                        Core.Filter(clause.Types, (ExpressionWithTypeArguments t) =>
                            Utilities.IsEntityNameExpression(t.Expression!)
                            || (clause.Token == SyntaxKind.ExtendsKeyword && t.Expression!.Kind == SyntaxKind.NullKeyword)),
                        null),
                    VisitDeclarationSubtree,
                    NodeTests.IsExpressionWithTypeArguments)));
        return _factory.CreateNodeArray(
            Core.Filter(mapped, (HeritageClause clause) => clause.Types is not null && clause.Types.Count > 0),
            null);
    }
}
