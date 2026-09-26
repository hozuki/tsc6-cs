namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:6310-6476 — the `syntacticBuilderResolver` object literal
/// <summary>
/// The resolver the checker hands to <c>createSyntacticTypeNodeBuilder</c>: the callbacks
/// <c>expressionToTypeNode.ts</c> asks for when it turns an expression into a type node.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS declares this as an object literal assigned to <c>const syntacticBuilderResolver</c> inside
/// <c>createNodeBuilder</c> (checker.ts:6310); the port has no anonymous object types with a declared interface, so the
/// literal is a class. It is reached through <see cref="Checker.SyntacticBuilderResolver"/>, and
/// <c>Checker._syntacticNodeBuilder</c> is what the M6-5 assembly will hand it to
/// (<c>createSyntacticTypeNodeBuilder(compilerOptions, nodeBuilder.syntacticBuilderResolver)</c>, checker.ts:1552).
/// Landing <c>expressionToTypeNode.ts</c> (1,330 lines, M4f's remaining half) therefore needs no change here: this file
/// supplies the resolver that module has been waiting on.
/// </para>
/// <para>
/// PORT-NOTE: the literal's members that are bare references in TS (<c>evaluateEntityNameExpression</c>,
/// <c>isExpandoFunctionDeclaration</c>, <c>hasLateBindableName</c>, <c>isDefinitelyReferenceToGlobalSymbolObject</c>,
/// <c>getAllAccessorDeclarations</c>, <c>isOptionalParameter</c>) forward to the checker member of the same name; the
/// ones with bodies (eight of them) are ported in full here.
/// </para>
/// <para>
/// PORT-NOTE: TS's other members take <c>SyntacticTypeNodeBuilderContext</c> and cast it down to <c>NodeBuilderContext</c>
/// at the first line (<c>const context = syntacticContext as NodeBuilderContext</c>). The port performs the same cast,
/// spelled <c>(NodeBuilderContext)context!</c>, because the generated slot is the narrower interface and the value is
/// always the checker's own context.
/// </para>
/// </remarks>
internal sealed class SyntacticBuilderResolver : ISyntacticTypeNodeBuilderResolver
{
    private readonly Checker _checker;

    /// <summary>ported from: src/compiler/checker.ts:6310 — the closure over <c>createNodeBuilder</c>.</summary>
    public SyntacticBuilderResolver(Checker checker) => _checker = checker;

    /// <summary>ported from: src/compiler/checker.ts:6311 — <c>isOptionalParameter</c>.</summary>
    public bool IsOptionalParameter(ParameterDeclaration? p) => _checker.IsOptionalParameter(p);

    /// <summary>ported from: src/compiler/checker.ts:6338-6340 — <c>isUndefinedIdentifierExpression</c>.</summary>
    public bool IsUndefinedIdentifierExpression(Identifier? name)
        => ReferenceEquals(_checker.GetSymbolAtLocation(name), _checker._undefinedSymbol);

    /// <summary>ported from: src/compiler/checker.ts:6312 — <c>isExpandoFunctionDeclaration</c>.</summary>
    public bool IsExpandoFunctionDeclaration(Node? name) => _checker.IsExpandoFunctionDeclaration((IDeclaration?)name);

    /// <summary>ported from: src/compiler/checker.ts:6313 — <c>getAllAccessorDeclarations</c>.</summary>
    public IAllAccessorDeclarations GetAllAccessorDeclarations(Node? declaration)
        => _checker.GetAllAccessorDeclarationsForDeclaration(declaration!);

    /// <summary>ported from: src/compiler/checker.ts:6322-6339 — <c>requiresAddingImplicitUndefined</c>.</summary>
    public bool RequiresAddingImplicitUndefined(Node? declaration, ISymbol? symbol, Node? enclosingDeclaration)
    {
        switch (declaration!.Kind)
        {
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.PropertySignature:
            case SyntaxKind.JSDocPropertyTag:
                symbol ??= _checker.GetSymbolOfDeclaration((IDeclaration)declaration);
                var type = _checker.GetTypeOfSymbol(symbol);
                return (symbol.Flags & SymbolFlags.Property) != 0
                    && (symbol.Flags & SymbolFlags.Optional) != 0
                    && Utilities.IsOptionalDeclaration((IDeclaration)declaration)
                    && (_checker.GetSymbolLinks(symbol) as IMappedSymbolLinks)?.MappedType is not null
                    && _checker.ContainsNonMissingUndefinedType(type);
            case SyntaxKind.Parameter:
            case SyntaxKind.JSDocParameterTag:
                return _checker.RequiresAddingImplicitUndefined((ParameterDeclaration)declaration, enclosingDeclaration);
            default:
                // PORT-NOTE: TS's `Debug.assertNever(declaration)` is typed `never`, so the switch needs no code after it;
                // the port's returns void and throws, so this `return false` only satisfies definite assignment.
                Debug.AssertNever(declaration);
                return false;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:6319 — <c>isDefinitelyReferenceToGlobalSymbolObject</c>.</summary>
    public bool IsDefinitelyReferenceToGlobalSymbolObject(Node? node)
        => _checker.IsDefinitelyReferenceToGlobalSymbolObject(node);

    /// <summary>ported from: src/compiler/checker.ts:6341-6343 — <c>isEntityNameVisible</c>.</summary>
    public ISymbolVisibilityResult IsEntityNameVisible(ISyntacticTypeNodeBuilderContext? context, Node? entityName, bool? shouldComputeAliasToMakeVisible)
        => _checker.IsEntityNameVisible(entityName, ((NodeBuilderContext)context!).EnclosingDeclaration, shouldComputeAliasToMakeVisible ?? true);

    /// <summary>ported from: src/compiler/checker.ts:6344-6346 — <c>serializeExistingTypeNode</c>.</summary>
    public ITypeNode? SerializeExistingTypeNode(ISyntacticTypeNodeBuilderContext? context, ITypeNode? node, bool? addUndefined)
        => _checker.SerializeExistingTypeNode((NodeBuilderContext)context!, node!, addUndefined == true);

    /// <summary>ported from: src/compiler/checker.ts:6347-6353 — <c>serializeReturnTypeForSignature</c>.</summary>
    public ITypeNode? SerializeReturnTypeForSignature(ISyntacticTypeNodeBuilderContext? context, Node? signatureDeclaration, ISymbol? symbol)
    {
        var nodeBuilderContext = (NodeBuilderContext)context!;
        var signature = _checker.GetSignatureFromDeclaration(signatureDeclaration!)!;
        symbol ??= _checker.GetSymbolOfDeclaration((IDeclaration)signatureDeclaration!);
        var symbolId = Utilities.GetSymbolId(symbol);
        var returnType = nodeBuilderContext.EnclosingSymbolTypes!.GetValueOrDefault(symbolId)
            ?? _checker.InstantiateType(_checker.GetReturnTypeOfSignature(signature), (TypeMapper?)nodeBuilderContext.Mapper);
        return _checker.SerializeInferredReturnTypeForSignature(nodeBuilderContext, signature, returnType!);
    }

    /// <summary>ported from: src/compiler/checker.ts:6354-6358 — <c>serializeTypeOfExpression</c>.</summary>
    public ITypeNode SerializeTypeOfExpression(ISyntacticTypeNodeBuilderContext? context, Expression? expr)
    {
        var nodeBuilderContext = (NodeBuilderContext)context!;
        var type = _checker.InstantiateType(_checker.GetWidenedType(_checker.GetRegularTypeOfExpression(expr!)), (TypeMapper?)nodeBuilderContext.Mapper);
        return _checker.TypeToTypeNodeHelper(type!, nodeBuilderContext);
    }

    /// <summary>ported from: src/compiler/checker.ts:6359-6375 — <c>serializeTypeOfDeclaration</c>.</summary>
    public ITypeNode? SerializeTypeOfDeclaration(ISyntacticTypeNodeBuilderContext? context, object? node, ISymbol? symbol)
    {
        // Get type of the symbol if this is the valid symbol otherwise get type at location
        var nodeBuilderContext = (NodeBuilderContext)context!;
        symbol ??= _checker.GetSymbolOfDeclaration((IDeclaration)node!);
        var symbolId = Utilities.GetSymbolId(symbol);
        var type = nodeBuilderContext.EnclosingSymbolTypes?.GetValueOrDefault(symbolId);
        if (type is null)
        {
            type = (symbol.Flags & SymbolFlags.Accessor) != 0 && ((Node)node!).Kind == SyntaxKind.SetAccessor
                ? _checker.InstantiateType(_checker.GetWriteTypeOfSymbol(symbol), (TypeMapper?)nodeBuilderContext.Mapper)
                : symbol is not null && (symbol.Flags & (SymbolFlags.TypeLiteral | SymbolFlags.Signature)) == 0
                    ? _checker.InstantiateType(_checker.GetWidenedLiteralType(_checker.GetTypeOfSymbol(symbol)), (TypeMapper?)nodeBuilderContext.Mapper)
                    : _checker._errorType!;
        }
        var addUndefinedForParameter = node is not null
            && (NodeTests.IsParameter((Node)node) || NodeTests.IsJSDocParameterTag((Node)node))
            && _checker.RequiresAddingImplicitUndefined((ParameterDeclaration)node, nodeBuilderContext.EnclosingDeclaration);
        if (addUndefinedForParameter)
        {
            type = _checker.GetOptionalType(type!);
        }
        return _checker.SerializeInferredTypeForDeclaration(symbol!, nodeBuilderContext, type!);
    }

    /// <summary>ported from: src/compiler/checker.ts:6376-6378 — <c>serializeNameOfParameter</c>.</summary>
    public object? SerializeNameOfParameter(ISyntacticTypeNodeBuilderContext? context, ParameterDeclaration? parameter)
        => _checker.ParameterToParameterDeclarationName(_checker.GetSymbolOfDeclaration(parameter!), parameter, (NodeBuilderContext)context!);

    /// <summary>ported from: src/compiler/checker.ts:6379-6385 — <c>serializeEntityName</c>.</summary>
    public Expression? SerializeEntityName(ISyntacticTypeNodeBuilderContext? context, MemberExpression? node)
    {
        var nodeBuilderContext = (NodeBuilderContext)context!;
        var symbol = _checker.GetSymbolAtLocation(node, /*ignoreErrors*/ true);
        if (symbol is null) return null;
        if (!_checker.IsValueSymbolAccessible(symbol, nodeBuilderContext.EnclosingDeclaration)) return null;
        return _checker.SymbolToExpression(symbol, nodeBuilderContext, SymbolFlags.Value | SymbolFlags.ExportValue);
    }

    /// <summary>ported from: src/compiler/checker.ts:6386-6388 — <c>serializeTypeName</c>.</summary>
    public ITypeNode? SerializeTypeName(ISyntacticTypeNodeBuilderContext? context, Node? node, bool? isTypeOf, List<ITypeNode>? typeArguments)
        => _checker.SerializeTypeName((NodeBuilderContext)context!, node!, isTypeOf == true, typeArguments);

    /// <summary>ported from: src/compiler/checker.ts:6389-6395 — <c>getJsDocPropertyOverride</c>.</summary>
    public ITypeNode? GetJsDocPropertyOverride(ISyntacticTypeNodeBuilderContext? context, JSDocTypeLiteral? jsDocTypeLiteral, JSDocPropertyLikeTag? jsDocProperty)
    {
        var nodeBuilderContext = (NodeBuilderContext)context!;
        var name = NodeTests.IsIdentifier(jsDocProperty!.Name!) ? (Identifier)jsDocProperty.Name! : ((QualifiedName)jsDocProperty.Name!).Right;
        var typeViaParent = _checker.GetTypeOfPropertyOfType(_checker.GetTypeFromTypeNode(nodeBuilderContext, jsDocTypeLiteral!), name!.EscapedText);
        var overrideTypeNode = typeViaParent is not null && jsDocProperty.TypeExpression is not null
            && !ReferenceEquals(_checker.GetTypeFromTypeNode(nodeBuilderContext, jsDocProperty.TypeExpression.Type!), typeViaParent)
                ? _checker.TypeToTypeNodeHelper(typeViaParent, nodeBuilderContext)
                : null;
        return overrideTypeNode;
    }

    /// <summary>ported from: src/compiler/checker.ts:6396-6412 — <c>enterNewScope</c>.</summary>
    public Action EnterNewScope(ISyntacticTypeNodeBuilderContext? context, Node? node)
    {
        if (Utilities.IsFunctionLike(node!) || NodeTests.IsJSDocSignature(node!))
        {
            var signature = _checker.GetSignatureFromDeclaration(node!)!;
            return _checker.EnterNewScope((NodeBuilderContext)context!, node, signature.Parameters, signature.TypeParameters);
        }
        else
        {
            var typeParameters = NodeTests.IsConditionalTypeNode(node!)
                ? _checker.GetInferTypeParameters((ConditionalTypeNode)node!)
                : [_checker.GetDeclaredTypeOfTypeParameter(_checker.GetSymbolOfDeclaration(((MappedTypeNode)node!).TypeParameter!))];
            return _checker.EnterNewScope((NodeBuilderContext)context!, node, /*expandedParams*/ null, typeParameters);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:6413-6415 — <c>markNodeReuse</c>.</summary>
    public T MarkNodeReuse<T>(ISyntacticTypeNodeBuilderContext? context, T? range, Node? location) where T : Node
        => _checker.SetTextRange((NodeBuilderContext)context!, range!, location);

    /// <summary>ported from: src/compiler/checker.ts:6413-6415 — <c>trackExistingEntityName</c>.</summary>
    public object? TrackExistingEntityName<T>(ISyntacticTypeNodeBuilderContext? context, T? node) where T : Node
        => _checker.TrackExistingEntityName(node!, (NodeBuilderContext)context!);

    /// <summary>ported from: src/compiler/checker.ts:6413-6415 — <c>trackComputedName</c>.</summary>
    public void TrackComputedName(ISyntacticTypeNodeBuilderContext? context, Node? accessExpression)
    {
        var nodeBuilderContext = (NodeBuilderContext)context!;
        _checker.TrackComputedName(accessExpression!, nodeBuilderContext.EnclosingDeclaration, nodeBuilderContext);
    }

    /// <summary>ported from: src/compiler/checker.ts:6311 — <c>evaluateEntityNameExpression</c>.</summary>
    public IEvaluatorResult<object?> EvaluateEntityNameExpression(MemberExpression? expression)
        => _checker.EvaluateEntityNameExpression(expression!);

    /// <summary>ported from: src/compiler/checker.ts:6416-6445 — <c>getModuleSpecifierOverride</c>.</summary>
    public string? GetModuleSpecifierOverride(ISyntacticTypeNodeBuilderContext? context, ImportTypeNode? parent, StringLiteral? lit)
    {
        var nodeBuilderContext = (NodeBuilderContext)context!;
        if (nodeBuilderContext.Bundled || !ReferenceEquals(nodeBuilderContext.EnclosingFile, Utilities.GetSourceFileOfNode(lit)))
        {
            var name = lit!.Text!;
            var originalName = name;
            var nodeSymbol = _checker.GetNodeLinks(parent!).ResolvedSymbol;
            var meaning = parent!.IsTypeOf == true ? SymbolFlags.Value : SymbolFlags.Type;
            var parentSymbol = nodeSymbol is not null
                && _checker.IsSymbolAccessible(nodeSymbol, nodeBuilderContext.EnclosingDeclaration, meaning, /*shouldComputeAliasesToMakeVisible*/ false).Accessibility == SymbolAccessibility.Accessible
                    ? Core.First(_checker.LookupSymbolChain(nodeSymbol, nodeBuilderContext, meaning, /*yieldModuleSymbol*/ true))
                    : null;
            if (parentSymbol is not null && Utilities.IsExternalModuleSymbol(parentSymbol))
            {
                name = _checker.GetSpecifierForModuleSymbol(parentSymbol!, nodeBuilderContext);
            }
            else
            {
                var targetFile = _checker.GetExternalModuleFileFromDeclaration(parent);
                if (targetFile is not null)
                {
                    name = _checker.GetSpecifierForModuleSymbol(targetFile.Symbol!, nodeBuilderContext);
                }
            }
            if (name.Contains("/node_modules/"))
            {
                nodeBuilderContext.EncounteredError = true;
                // PORT-NOTE: TS tests `context.tracker.reportLikelyUnsafeImportRequiredError` for presence, because its
                // tracker type is structural. The port's ISymbolTracker is closed and its implementation no-ops when
                // there is no presenter, so the call is unconditional (SymbolTrackerImpl.cs documents the same reading).
                nodeBuilderContext.Tracker!.ReportLikelyUnsafeImportRequiredError(name, nodeSymbol is not null ? Utilities.UnescapeLeadingUnderscores(nodeSymbol.EscapedName!) : null);
            }
            if (name != originalName)
            {
                return name;
            }
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:6446-6448 — <c>canReuseTypeNode</c>.</summary>
    public bool CanReuseTypeNode(ISyntacticTypeNodeBuilderContext? context, ITypeNode? existing)
        => _checker.CanReuseTypeNode((NodeBuilderContext)context!, existing!);

    /// <summary>ported from: src/compiler/checker.ts:6449-6478 — <c>canReuseTypeNodeAnnotation</c>.</summary>
    public bool CanReuseTypeNodeAnnotation(ISyntacticTypeNodeBuilderContext? context, IDeclaration? node, ITypeNode? existing, ISymbol? symbol, bool? requiresAddingUndefined)
    {
        var nodeBuilderContext = (NodeBuilderContext)context!;
        if (nodeBuilderContext.EnclosingDeclaration is null) return false;

        symbol ??= _checker.GetSymbolOfDeclaration(node!);
        var symbolId = Utilities.GetSymbolId(symbol);
        var type = nodeBuilderContext.EnclosingSymbolTypes?.GetValueOrDefault(symbolId);
        if (type is null)
        {
            if ((symbol.Flags & SymbolFlags.Accessor) != 0)
            {
                type = ((Node)node!).Kind == SyntaxKind.SetAccessor ? _checker.GetWriteTypeOfSymbol(symbol) : _checker.GetTypeOfAccessors(symbol);
            }
            else if (Utilities.IsValueSignatureDeclaration((Node)node!))
            {
                type = _checker.GetReturnTypeOfSignature(_checker.GetSignatureFromDeclaration((Node)node!)!);
            }
            else
            {
                type = _checker.GetTypeOfSymbol(symbol);
            }
        }
        var annotationType = _checker.GetTypeFromTypeNodeWithoutContext(existing!);
        if (_checker.IsErrorType(annotationType))
        {
            // allow "reusing" type nodes that resolve to error types
            // those can't truly be reused but it prevents cascading errors in isolatedDeclarations
            // for source with errors there is no guarantee to emit correct code anyway
            return true;
        }
        if (requiresAddingUndefined == true && annotationType is not null)
        {
            annotationType = _checker.AddOptionality(annotationType, !NodeTests.IsParameter((Node)node!));
        }
        return annotationType is not null
            && _checker.TypeNodeIsEquivalentToType((Node?)node, type, annotationType)
            && _checker.ExistingTypeNodeIsNotReferenceOrIsReferenceWithCompatibleTypeArgumentCount(existing!, type);
    }

    /// <summary>ported from: src/compiler/checker.ts:6314-6316 — <c>shouldRemoveDeclaration</c>.</summary>
    public bool ShouldRemoveDeclaration(ISyntacticTypeNodeBuilderContext? context, DynamicNamedDeclaration? node)
        => !((((NodeBuilderContext)context!).InternalFlags & InternalNodeBuilderFlags.AllowUnresolvedNames) != 0
            && Utilities.IsEntityNameExpression(node!.Name!.Expression!)
            && (_checker.CheckComputedPropertyName(node.Name).Flags & TypeFlags.Any) != 0);

    /// <summary>ported from: src/compiler/checker.ts:6313 — <c>hasLateBindableName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's member is a type guard (<c>node is LateBoundDeclaration | …</c>), whose narrowed type has no C#
    /// name, so the generated slot is <c>object?</c>. The runtime value is the guard's boolean, and the checker's
    /// <c>HasLateBindableName</c> answers exactly that; a consumer reads it as a truth test.
    /// </remarks>
    public object? HasLateBindableName(IDeclaration? node) => _checker.HasLateBindableName(node!);

    /// <summary>ported from: src/compiler/checker.ts:6317-6319 — <c>createRecoveryBoundary</c>.</summary>
    public object? CreateRecoveryBoundary(ISyntacticTypeNodeBuilderContext? context)
        => _checker.CreateRecoveryBoundary((NodeBuilderContext)context!);
}
