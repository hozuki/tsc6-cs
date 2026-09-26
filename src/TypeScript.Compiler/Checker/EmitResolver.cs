namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:51380-51554 — createResolver
/// <summary>
/// The checker's emit resolver — TS's <c>EmitResolver</c>, the interface the declaration emitter (Layer 7) and the
/// services ask about symbols, aliases and declaration visibility.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS declares <c>createResolver()</c> inside <c>createTypeChecker</c> and returns an object literal whose
/// members are either shorthand references to checker functions or small arrow functions that guard them with
/// <c>getParseTreeNode</c>. The port has no anonymous object types, so the literal is this class: the shorthand entries
/// forward to the same checker member, and the arrow bodies are ported in full. <c>Checker.CreateResolver()</c> is the
/// factory and the M6-5 assembly assigns <c>_emitResolver</c> with it (checker.ts:1550).
/// </para>
/// <para>
/// PORT-NOTE: four members of the literal are declared here rather than on <see cref="Checker"/> because their bodies
/// live inside <c>createResolver</c> itself: <c>isValueAliasDeclaration</c>, <c>isReferencedAliasDeclaration</c>,
/// <c>hasNodeCheckFlag</c>, <c>getConstantValue</c>, <c>getEnumMemberValue</c>, <c>markLinkedReferences</c>,
/// <c>getExternalModuleFileFromDeclaration</c>, <c>isLateBound</c>, <c>isBindingCapturedByNode</c>,
/// <c>getDeclarationStatementsForSourceFile</c>, <c>createLateBoundIndexSignatures</c>,
/// <c>symbolToDeclarations</c>, plus <c>isImportRequiredByAugmentation</c>, which the reference declares as a nested
/// function after the literal.
/// </para>
/// <para>
/// PORT-NOTE: TS tests the optional tracker callbacks for presence (<c>tracker.trackSymbol</c>,
/// <c>tracker.reportLikelyUnsafeImportRequiredError</c>) because its <c>SymbolTracker</c> is structural; the port's
/// <see cref="ISymbolTracker"/> is a closed interface, so those tests are null tests on the tracker itself and the
/// implementation no-ops when there is no presenter (the reading <c>SymbolTrackerImpl.cs</c> records).
/// </para>
/// </remarks>
internal sealed class EmitResolver : IEmitResolver
{
    private readonly Checker _checker;

    /// <summary>ported from: src/compiler/checker.ts:51380 — the closure over <c>createTypeChecker</c>.</summary>
    public EmitResolver(Checker checker) => _checker = checker;

    // ---------------------------------------------------------------------------------------------
    // The shorthand entries (checker.ts:51382-51420)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:51382 — <c>getReferencedExportContainer</c>.</summary>
    public Node? GetReferencedExportContainer(Identifier? node, bool? prefixLocals)
        => (Node?)_checker.GetReferencedExportContainer(node, prefixLocals);

    /// <summary>ported from: src/compiler/checker.ts:51383 — <c>getReferencedImportDeclaration</c>.</summary>
    public IDeclaration? GetReferencedImportDeclaration(Identifier? node)
        => _checker.GetReferencedImportDeclaration(node);

    /// <summary>ported from: src/compiler/checker.ts:51384 — <c>getReferencedDeclarationWithCollidingName</c>.</summary>
    public IDeclaration? GetReferencedDeclarationWithCollidingName(Identifier? node)
        => _checker.GetReferencedDeclarationWithCollidingName(node);

    /// <summary>ported from: src/compiler/checker.ts:51385 — <c>isDeclarationWithCollidingName</c>.</summary>
    public bool IsDeclarationWithCollidingName(IDeclaration? node)
        => _checker.IsDeclarationWithCollidingName(node);

    /// <summary>ported from: src/compiler/checker.ts:51386-51390 — <c>isValueAliasDeclaration</c>.</summary>
    public bool IsValueAliasDeclaration(Node? nodeIn)
    {
        var node = Utilities.GetParseTreeNode(nodeIn);
        // Synthesized nodes are always treated like values.
        return node is not null && _checker._canCollectSymbolAliasAccessabilityData ? _checker.IsValueAliasDeclaration(node) : true;
    }

    /// <summary>ported from: src/compiler/checker.ts:51388 — <c>hasGlobalName</c>.</summary>
    public bool HasGlobalName(string? name) => _checker.HasGlobalName(name);

    /// <summary>ported from: src/compiler/checker.ts:51391-51395 — <c>isReferencedAliasDeclaration</c>.</summary>
    public bool IsReferencedAliasDeclaration(Node? nodeIn, bool? checkChildren)
    {
        var node = Utilities.GetParseTreeNode(nodeIn);
        // Synthesized nodes are always treated as referenced.
        return node is not null && _checker._canCollectSymbolAliasAccessabilityData ? _checker.IsReferencedAliasDeclaration(node, checkChildren) : true;
    }

    /// <summary>ported from: src/compiler/checker.ts:51396-51400 — <c>hasNodeCheckFlag</c>.</summary>
    public bool HasNodeCheckFlag(Node? nodeIn, object? flags)
    {
        var node = Utilities.GetParseTreeNode(nodeIn);
        if (node is null) return false;
        return _checker.HasNodeCheckFlag(node, (NodeCheckFlags)flags!);
    }

    /// <summary>ported from: src/compiler/checker.ts:51401 — <c>isTopLevelValueImportEqualsWithEntityName</c>.</summary>
    public bool IsTopLevelValueImportEqualsWithEntityName(ImportEqualsDeclaration? node)
        => _checker.IsTopLevelValueImportEqualsWithEntityName(node!);

    /// <summary>ported from: src/compiler/checker.ts:51402 — <c>isDeclarationVisible</c>.</summary>
    public bool IsDeclarationVisible(Node? node) => _checker.IsDeclarationVisible(node);

    /// <summary>ported from: src/compiler/checker.ts:51403 — <c>isImplementationOfOverload</c>.</summary>
    public bool? IsImplementationOfOverload(Node? node) => _checker.IsImplementationOfOverload(node);

    /// <summary>ported from: src/compiler/checker.ts:51404 — <c>requiresAddingImplicitUndefined</c>.</summary>
    public bool RequiresAddingImplicitUndefined(ParameterDeclaration? node, Node? enclosingDeclaration)
        => _checker.RequiresAddingImplicitUndefined(node!, enclosingDeclaration);

    /// <summary>ported from: src/compiler/checker.ts:51405 — <c>isExpandoFunctionDeclaration</c>.</summary>
    public bool IsExpandoFunctionDeclaration(Node? node) => _checker.IsExpandoFunctionDeclaration((IDeclaration?)node);

    /// <summary>ported from: src/compiler/checker.ts:51406 — <c>getPropertiesOfContainerFunction</c>.</summary>
    public List<ISymbol> GetPropertiesOfContainerFunction(IDeclaration? node)
        => _checker.GetPropertiesOfContainerFunction(node);

    /// <summary>ported from: src/compiler/checker.ts:51407 — <c>createTypeOfDeclaration</c>.</summary>
    public ITypeNode? CreateTypeOfDeclaration(object? declaration, Node? enclosingDeclaration, NodeBuilderFlags flags, InternalNodeBuilderFlags internalFlags, ISymbolTracker? tracker)
        => _checker.CreateTypeOfDeclaration((IDeclaration?)declaration, enclosingDeclaration, flags, internalFlags, tracker);

    /// <summary>ported from: src/compiler/checker.ts:51408 — <c>createReturnTypeOfSignatureDeclaration</c>.</summary>
    public ITypeNode? CreateReturnTypeOfSignatureDeclaration(Node? signatureDeclaration, Node? enclosingDeclaration, NodeBuilderFlags flags, InternalNodeBuilderFlags internalFlags, ISymbolTracker? tracker)
        => _checker.CreateReturnTypeOfSignatureDeclaration(signatureDeclaration, enclosingDeclaration, flags, internalFlags, tracker);

    /// <summary>ported from: src/compiler/checker.ts:51409 — <c>createTypeOfExpression</c>.</summary>
    public ITypeNode? CreateTypeOfExpression(Expression? expr, Node? enclosingDeclaration, NodeBuilderFlags flags, InternalNodeBuilderFlags internalFlags, ISymbolTracker? tracker)
        => _checker.CreateTypeOfExpression(expr, enclosingDeclaration, flags, internalFlags, tracker);

    /// <summary>ported from: src/compiler/checker.ts:51410 — <c>createLiteralConstValue</c>.</summary>
    public Expression CreateLiteralConstValue(Node? node, ISymbolTracker? tracker)
        => _checker.CreateLiteralConstValue(node, tracker);

    /// <summary>ported from: src/compiler/checker.ts:51411 — <c>isSymbolAccessible</c>.</summary>
    public ISymbolAccessibilityResult IsSymbolAccessible(ISymbol? symbol, Node? enclosingDeclaration, SymbolFlags? meaning, bool shouldComputeAliasToMarkVisible)
        => _checker.IsSymbolAccessible(symbol, enclosingDeclaration, meaning, shouldComputeAliasToMarkVisible);

    /// <summary>ported from: src/compiler/checker.ts:51412 — <c>isEntityNameVisible</c>.</summary>
    public ISymbolVisibilityResult IsEntityNameVisible(Node? entityName, Node? enclosingDeclaration)
        => _checker.IsEntityNameVisible(entityName, enclosingDeclaration);

    /// <summary>ported from: src/compiler/checker.ts:51413-51416 — <c>getConstantValue</c>.</summary>
    public object? GetConstantValue(Node? nodeIn)
    {
        var node = Utilities.GetParseTreeNode(nodeIn, _checker.CanHaveConstantValue);
        return node is not null ? _checker.GetConstantValue(node) : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:51417-51420 — <c>getEnumMemberValue</c>.</summary>
    public IEvaluatorResult<object?>? GetEnumMemberValue(EnumMember? nodeIn)
    {
        var node = Utilities.GetParseTreeNode<EnumMember>(nodeIn, NodeTests.IsEnumMember);
        return node is not null ? _checker.GetEnumMemberValue(node) : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:51421 — <c>collectLinkedAliases</c>.</summary>
    public List<Node>? CollectLinkedAliases(MemberExpression? node, bool? setVisibility)
        => _checker.CollectLinkedAliases(node, setVisibility);

    /// <summary>ported from: src/compiler/checker.ts:51422-51425 — <c>markLinkedReferences</c>.</summary>
    public void MarkLinkedReferences(Node? nodeIn)
    {
        var node = Utilities.GetParseTreeNode(nodeIn);
        if (node is not null)
        {
            _checker.MarkLinkedReferences(node, ReferenceHint.Unspecified);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:51426 — <c>getReferencedValueDeclaration</c>.</summary>
    public IDeclaration? GetReferencedValueDeclaration(Identifier? reference)
        => _checker.GetReferencedValueDeclaration(reference);

    /// <summary>ported from: src/compiler/checker.ts:51427 — <c>getReferencedValueDeclarations</c>.</summary>
    public List<IDeclaration>? GetReferencedValueDeclarations(Identifier? reference)
        => _checker.GetReferencedValueDeclarations(reference);

    /// <summary>ported from: src/compiler/checker.ts:51428 — <c>getTypeReferenceSerializationKind</c>.</summary>
    public TypeReferenceSerializationKind GetTypeReferenceSerializationKind(Node? typeName, Node? location)
        => _checker.GetTypeReferenceSerializationKind(typeName, location);

    /// <summary>ported from: src/compiler/checker.ts:51429 — <c>isOptionalParameter</c>.</summary>
    public bool IsOptionalParameter(ParameterDeclaration? node) => _checker.IsOptionalParameter(node);

    /// <summary>ported from: src/compiler/checker.ts:51430 — <c>isArgumentsLocalBinding</c>.</summary>
    public bool IsArgumentsLocalBinding(Identifier? node) => _checker.IsArgumentsLocalBinding(node);

    /// <summary>ported from: src/compiler/checker.ts:51431-51434 — <c>getExternalModuleFileFromDeclaration</c>.</summary>
    public SourceFile? GetExternalModuleFileFromDeclaration(Node? nodeIn)
    {
        var node = Utilities.GetParseTreeNode(nodeIn, Utilities.HasPossibleExternalModuleReference);
        return node is not null ? _checker.GetExternalModuleFileFromDeclaration(node) : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:51435 — <c>isLiteralConstDeclaration</c>.</summary>
    public bool IsLiteralConstDeclaration(Node? node) => _checker.IsLiteralConstDeclaration(node);

    /// <summary>ported from: src/compiler/checker.ts:51436-51440 — <c>isLateBound</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's member is a type guard (<c>nodeIn is LateBoundDeclaration</c>) whose narrowed type has no C#
    /// name, so the generated slot is <c>object?</c>; the runtime value is the guard's boolean.
    /// </remarks>
    public object? IsLateBound(IDeclaration? nodeIn)
    {
        var node = (IDeclaration?)Utilities.GetParseTreeNode((Node?)nodeIn, Utilities.IsDeclaration);
        var symbol = node is not null ? _checker.GetSymbolOfDeclaration(node) : null;
        return symbol is not null && (_checker.GetCheckFlags(symbol) & CheckFlags.Late) != 0;
    }

    /// <summary>ported from: src/compiler/checker.ts:51441 — <c>getJsxFactoryEntity</c>.</summary>
    public Node? GetJsxFactoryEntity(Node? location) => _checker.GetJsxFactoryEntity(location);

    /// <summary>ported from: src/compiler/checker.ts:51442 — <c>getJsxFragmentFactoryEntity</c>.</summary>
    public Node? GetJsxFragmentFactoryEntity(Node? location) => _checker.GetJsxFragmentFactoryEntity(location);

    /// <summary>ported from: src/compiler/checker.ts:51443-51447 — <c>isBindingCapturedByNode</c>.</summary>
    public bool IsBindingCapturedByNode(Node? node, Node? decl)
    {
        var parseNode = Utilities.GetParseTreeNode(node);
        var parseDecl = Utilities.GetParseTreeNode(decl);
        return parseNode is not null && parseDecl is not null
            && (NodeTests.IsVariableDeclaration(parseDecl) || NodeTests.IsBindingElement(parseDecl))
            && _checker.IsBindingCapturedByNode(parseNode, parseDecl);
    }

    /// <summary>ported from: src/compiler/checker.ts:51448-51458 — <c>getDeclarationStatementsForSourceFile</c>.</summary>
    public List<IStatement>? GetDeclarationStatementsForSourceFile(SourceFile? node, NodeBuilderFlags flags, InternalNodeBuilderFlags internalFlags, ISymbolTracker? tracker)
    {
        var n = Utilities.GetParseTreeNode<SourceFile>(node);
        Debug.Assert(n is not null && n.Kind == SyntaxKind.SourceFile, "Non-sourcefile node passed into getDeclarationsForSourceFile");
        var sym = _checker.GetSymbolOfDeclaration(node!);
        if (sym is null)
        {
            return node!.Locals is null ? [] : _checker.SymbolTableToDeclarationStatements(node.Locals, node, flags, internalFlags, tracker);
        }
        _checker.ResolveExternalModuleSymbol(sym); // ensures cjs export assignment is setup
        return sym.Exports is null ? [] : _checker.SymbolTableToDeclarationStatements(sym.Exports, node, flags, internalFlags, tracker);
    }

    /// <summary>ported from: src/compiler/checker.ts:51459 — <c>isImportRequiredByAugmentation</c>.</summary>
    public bool IsImportRequiredByAugmentation(ImportDeclaration? decl)
        => IsImportRequiredByAugmentationWorker(decl!);

    /// <summary>ported from: src/compiler/checker.ts:51460 — <c>isDefinitelyReferenceToGlobalSymbolObject</c>.</summary>
    public bool IsDefinitelyReferenceToGlobalSymbolObject(Node? node)
        => _checker.IsDefinitelyReferenceToGlobalSymbolObject(node);

    /// <summary>ported from: src/compiler/checker.ts:51461-51513 — <c>createLateBoundIndexSignatures</c>.</summary>
    public List<Node>? CreateLateBoundIndexSignatures(Node? cls, Node? enclosingDeclaration, NodeBuilderFlags flags, InternalNodeBuilderFlags internalFlags, ISymbolTracker? tracker)
    {
        var sym = ((IDeclaration)cls!).Symbol!;
        var staticInfos = _checker.GetIndexInfosOfType(_checker.GetTypeOfSymbol(sym));
        var instanceIndexSymbol = _checker.GetIndexSymbol(sym);
        var instanceInfos = instanceIndexSymbol is not null
            ? _checker.GetIndexInfosOfIndexSymbol(instanceIndexSymbol, [.. _checker.GetMembersOfSymbol(sym).Values])
            : null;
        List<Node>? result = null;
        foreach (var infoList in new[] { staticInfos, instanceInfos })
        {
            if (Core.Length(infoList) == 0) continue;
            result ??= [];
            foreach (var info in infoList!)
            {
                if (info.Declaration is not null) continue;
                if (ReferenceEquals(info, _checker._anyBaseTypeIndexInfo)) continue; // inherited, but looks like a late-bound signature because it has no declarations
                if (info.Components is not null)
                {
                    var allComponentComputedNamesSerializable = Core.Every(info.Components, (Node e, int _) =>
                    {
                        var name = ((INamedDeclaration)e).Name;
                        return name is not null
                            && NodeTests.IsComputedPropertyName(name)
                            && Utilities.IsEntityNameExpression(((ComputedPropertyName)name).Expression!)
                            && enclosingDeclaration is not null
                            && _checker.IsEntityNameVisible(((ComputedPropertyName)name).Expression, enclosingDeclaration, /*shouldComputeAliasToMakeVisible*/ false).Accessibility == SymbolAccessibility.Accessible;
                    });
                    if (allComponentComputedNamesSerializable)
                    {
                        // skip late bound props that contribute to the index signature - they'll be preserved via other means
                        var newComponents = new List<Node>();
                        foreach (var candidate in info.Components)
                        {
                            if (!_checker.HasLateBindableName((IDeclaration)candidate)) newComponents.Add(candidate);
                        }
                        foreach (var e in newComponents)
                        {
                            var name = ((INamedDeclaration)e).Name!;
                            TrackComputedName(((ComputedPropertyName)name).Expression!);
                            var modifiers = ReferenceEquals(infoList, staticInfos)
                                ? new List<Node> { _checker._factory.CreateModifier(SyntaxKind.StaticKeyword) }
                                : null;
                            if (info.IsReadonly)
                            {
                                modifiers ??= [];
                                modifiers.Add(_checker._factory.CreateModifier(SyntaxKind.ReadonlyKeyword));
                            }
                            var propertyDeclaration = _checker._factory.CreatePropertyDeclaration(
                                modifiers,
                                name,
                                QuestionTokenOf(e),
                                _checker.TypeToTypeNode(_checker.GetTypeOfSymbol(((IDeclaration)e).Symbol!), enclosingDeclaration, flags, internalFlags, tracker, /*maximumLength*/ null, /*verbosityLevel*/ null, /*out*/ null),
                                /*initializer*/ null);
                            result.Add(propertyDeclaration);
                        }
                        continue;
                    }
                }
                var node = (Node?)_checker.IndexInfoToIndexSignatureDeclaration(info, enclosingDeclaration, flags, internalFlags, tracker);
                if (node is not null && ReferenceEquals(infoList, staticInfos))
                {
                    var signature = (IndexSignatureDeclaration)node;
                    signature.Modifiers ??= [];
                    signature.Modifiers.Insert(0, _checker._factory.CreateModifier(SyntaxKind.StaticKeyword));
                }
                if (node is not null)
                {
                    result.Add(node);
                }
            }
        }
        return result;

        void TrackComputedName(Node accessExpression)
        {
            // PORT-NOTE: TS tests `tracker.trackSymbol` for presence; the port's tracker is a closed interface, so the
            // test is on the tracker itself (see the class remarks).
            if (tracker is null) return;
            // get symbol of the first identifier of the entityName
            var firstIdentifier = Utilities.GetFirstIdentifier(accessExpression)!;
            var name = _checker.ResolveName(firstIdentifier, firstIdentifier.EscapedText!, SymbolFlags.Value | SymbolFlags.ExportValue, /*nameNotFoundMessage*/ null, /*isUse*/ true, /*excludeGlobals*/ false);
            if (name is not null)
            {
                tracker.TrackSymbol(name, enclosingDeclaration, SymbolFlags.Value);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:51518 — <c>symbolToDeclarations</c>.</summary>
    public List<IDeclaration> SymbolToDeclarations(ISymbol? symbol, SymbolFlags meaning, NodeBuilderFlags flags, int? maximumLength, int? verbosityLevel, IWriterContextOut? @out)
        => _checker.SymbolToDeclarations(symbol!, meaning, flags, maximumLength, verbosityLevel, @out);

    // ---------------------------------------------------------------------------------------------
    // The nested helper (checker.ts:51521-51545)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:51521-51545 — <c>isImportRequiredByAugmentation</c>.</summary>
    private bool IsImportRequiredByAugmentationWorker(ImportDeclaration node)
    {
        var file = Utilities.GetSourceFileOfNode(node);
        if (file?.Symbol is null) return false;
        var importTarget = _checker.GetExternalModuleFileFromDeclaration(node);
        if (importTarget is null) return false;
        if (ReferenceEquals(importTarget, file)) return false;
        var exports = _checker.GetExportsOfModuleTable(file.Symbol);
        foreach (var s in exports.Values)
        {
            if (s.MergeId != 0)
            {
                var merged = _checker.GetMergedSymbol(s)!;
                if (merged.Declarations is not null)
                {
                    foreach (var d in merged.Declarations)
                    {
                        var declFile = Utilities.GetSourceFileOfNode((Node)d);
                        if (ReferenceEquals(declFile, importTarget))
                        {
                            return true;
                        }
                    }
                }
            }
        }
        return false;
    }

    /// <summary>
    /// The <c>questionToken</c> read of <c>createLateBoundIndexSignatures</c> (checker.ts:51502).
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS writes <c>(isPropertySignature(e) || isPropertyDeclaration(e) || isMethodSignature(e) ||
    /// isMethodDeclaration(e) || isGetAccessor(e) || isSetAccessor(e)) &amp;&amp; e.questionToken ? … : undefined</c> — the
    /// unions' members are the only ones with a <c>questionToken</c>, so the kind test and the slot read are one test
    /// there. The port's six classes have no common base declaring the slot (they are all direct <see cref="Node"/>
    /// subclasses), so the same question is a type switch.
    /// </remarks>
    private static PunctuationToken? QuestionTokenOf(Node e) => e switch
    {
        PropertySignature signature => signature.QuestionToken,
        PropertyDeclaration declaration => declaration.QuestionToken,
        MethodSignature signature => signature.QuestionToken,
        MethodDeclaration declaration => declaration.QuestionToken,
        GetAccessorDeclaration accessor => accessor.QuestionToken,
        SetAccessorDeclaration accessor => accessor.QuestionToken,
        _ => null,
    };
}
