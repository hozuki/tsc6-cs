namespace TypeScript.Compiler;

// ported from: src/compiler/expressionToTypeNode.ts:193-196 — reuseNode
// ported from: src/compiler/expressionToTypeNode.ts:198-620 — tryReuseExistingTypeNode and the whole reuse walk
// ported from: src/compiler/expressionToTypeNode.ts:622-663 — the serializeExistingTypeNode family
/// <summary>
/// ported from: src/compiler/expressionToTypeNode.ts:198-620 — <c>tryReuseExistingTypeNode</c> and everything nested in it.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (nesting): TS nests <c>visitExistingNodeTreeSymbols</c>, its worker, the four <c>tryVisit*</c> helpers,
/// <c>visitEachChild</c>, <c>visitNodesWithoutCopyingPositions</c>, <c>getEffectiveDotDotDotForParameter</c>,
/// <c>getNameForJSDocFunctionParameter</c> and <c>rewriteModuleSpecifier</c> inside <c>tryReuseExistingTypeNode</c> so
/// that they can close over its <c>context</c>, <c>visitEachChild</c> and the recovery boundary. C# has no
/// nested-function form that can recurse back into the enclosing frame, so every one of them becomes a private member
/// of the class taking <c>context</c> and the boundary — the same treatment <c>NodeBuilder.Types.cs:664-700</c> records
/// for the checker's own nested helpers.
/// </para>
/// <para>
/// PORT-NOTE (the recovery boundary): TS destructures <c>resolver.createRecoveryBoundary(context)</c> once and the
/// nested functions close over <c>hadError</c>/<c>startRecoveryScope</c>/<c>markError</c>; the port threads the one
/// <see cref="IRecoveryBoundary"/> that call created (<c>NodeBuilder.Signatures.cs:838</c> gives it the reference's
/// four members, with <c>hadError</c> as the property <c>HadError</c>).
/// </para>
/// <para>
/// PORT-NOTE (<c>visitNode</c>): the reference imports <c>visitNode</c> from <c>visitorPublic.ts</c> (148-192), which the
/// port has not landed — that module's <c>visitEachChild</c> is the loud <c>Utilities.VisitEachChildWorker</c> blocker,
/// and its <c>visitNodes</c> is the landed <c>Utilities.VisitNodes</c>. <c>visitNode</c> is the traversal this half of
/// the file cannot start without, so <see cref="VisitNode{T}"/> states it here, faithfully and file-locally (it
/// collides with nothing: the future <c>Utilities</c> member is a different class). <see cref="VisitNodes{T}"/> is the
/// same port of <c>visitorPublic.ts:223-271</c> made generic, because the landed <c>Utilities.VisitNodes</c> is fixed to
/// <c>NodeArray&lt;Node&gt;</c> and C# generic invariance forbids the <c>NodeArray&lt;ITypeNode&gt;</c>/
/// <c>NodeArray&lt;TypeParameterDeclaration&gt;</c> this file visits. Both must be deleted when the <c>visitorPublic.ts</c>
/// slice lands and <c>Utilities.VisitNodes</c> becomes generic.
/// </para>
/// <para>
/// PORT-NOTE (<c>visitEachChild</c>): the reference's local wrapper passes <c>visitNodesWithoutCopyingPositions</c> as the
/// nodes visitor; the port calls the blocker with the same two arguments, so a reuse walk that reaches a kind this file
/// does not special-case throws by name instead of returning a partially visited node.
/// </para>
/// </remarks>
internal sealed partial class SyntacticNodeBuilder
{
    // ---------------------------------------------------------------------------------------------
    // reuseNode (expressionToTypeNode.ts:193-196)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:195-196 — <c>reuseNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares two overloads (one per nullable node type); the port states the implementation overload,
    /// and TS's <c>range: Node | undefined = node</c> default is the same read as the reference's own
    /// <c>range ?? node</c>, so the parameter stays <c>Node?</c> with a <c>null</c> default.
    /// </remarks>
    private T? ReuseNode<T>(ISyntacticTypeNodeBuilderContext context, T? node, Node? range = null) where T : Node
        => node is null
            ? null
            : _resolver.MarkNodeReuse(context, (node.Flags & NodeFlags.Synthesized) != 0 ? node : _factory.CloneNode(node), range ?? node);

    /// <summary>ported from: src/compiler/checker.ts:6413-6415 — <c>resolver.markNodeReuse</c>.</summary>
    /// <remarks>PORT-NOTE: the resolver's slot is generic in a <c>Node</c>, so an <c>ITypeNode</c> argument (the serialized name two call sites hand over) needs the second overload — the class/interface boundary §3 fixes.</remarks>
    private T? MarkNodeReuse<T>(ISyntacticTypeNodeBuilderContext context, T? range, Node? location) where T : Node
        => _resolver.MarkNodeReuse(context, range, location);

    /// <summary>ported from: src/compiler/checker.ts:6413-6415 — <c>resolver.markNodeReuse</c> for an <c>ITypeNode</c> argument.</summary>
    private ITypeNode? MarkNodeReuse(ISyntacticTypeNodeBuilderContext context, ITypeNode? range, Node? location)
        => (ITypeNode?)_resolver.MarkNodeReuse(context, (Node?)range, location);

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:195-196 — <c>reuseNode</c> for an <c>ITypeNode</c> argument.</summary>
    /// <remarks>PORT-NOTE: C# needs a second overload here (and nowhere else) because <c>ImportTypeNode.argument</c> is the interface <c>ITypeNode</c>, which is not a <c>Node</c>; the conversion is the class/interface boundary §3 fixes, and the one caller (<c>updateImportTypeNode</c>'s argument slot) takes an <c>ITypeNode?</c>.</remarks>
    private ITypeNode? ReuseNode(ISyntacticTypeNodeBuilderContext context, ITypeNode? node, Node? range = null)
        => (ITypeNode?)ReuseNode(context, (Node?)node, range);

    // ---------------------------------------------------------------------------------------------
    // tryReuseExistingTypeNode (expressionToTypeNode.ts:198-620)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:198-620 — the nested <c>tryReuseExistingTypeNode</c>.</summary>
    private ITypeNode? TryReuseExistingTypeNodeWorker(ISyntacticTypeNodeBuilderContext context, ITypeNode existing)
    {
        var boundary = CreateRecoveryBoundary(context);
        var transformed = VisitNode(existing, n => VisitExistingNodeTreeSymbols(n!, boundary, context), Utilities.IsTypeNode);
        if (!boundary.FinalizeBoundary())
        {
            return null;
        }
        var existingNode = (Node)existing;
        context.ApproximateLength += existingNode.End - existingNode.Pos;
        return transformed;
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:207-231 — <c>visitExistingNodeTreeSymbols</c>.</summary>
    private Node? VisitExistingNodeTreeSymbols(Node node, IRecoveryBoundary boundary, ISyntacticTypeNodeBuilderContext context)
    {
        // If there was an error in a sibling node bail early, the result will be discarded anyway
        if (boundary.HadError) return node;
        var recover = boundary.StartRecoveryScope();

        var onExitNewScope = Utilities.IsNewScopeNode(node) ? _resolver.EnterNewScope(context, node) : null;
        var result = VisitExistingNodeTreeSymbolsWorker(node, boundary, context);
        onExitNewScope?.Invoke();

        // If there was an error, maybe we can recover by serializing the actual type of the node
        if (boundary.HadError)
        {
            if (Utilities.IsTypeNode(node) && !NodeTests.IsTypePredicateNode(node))
            {
                recover();
                return (Node?)_resolver.SerializeExistingTypeNode(context, (ITypeNode)node, /*addUndefined*/ false);
            }
            return node;
        }
        // We want to clone the subtree, so when we mark it up with __pos and __end in quickfixes,
        //  we don't get odd behavior because of reused nodes. We also need to clone to _remove_
        //  the position information if the node comes from a different file than the one the node builder
        //  is set to build for (even though we are reusing the node structure, the position information
        //  would make the printer print invalid spans for literals and identifiers, and the formatter would
        //  choke on the mismatched positonal spans between a parent and an injected child from another file).
        return result is not null ? _resolver.MarkNodeReuse(context, result, node) : null;
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:233-249 — <c>tryVisitSimpleTypeNode</c>.</summary>
    private ITypeNode? TryVisitSimpleTypeNode(ITypeNode node, IRecoveryBoundary boundary, ISyntacticTypeNodeBuilderContext context)
    {
        var innerNode = Utilities.SkipTypeParentheses(node);
        switch (innerNode.Kind)
        {
            case SyntaxKind.TypeReference:
                return TryVisitTypeReference((TypeReferenceNode)innerNode, boundary, context);
            case SyntaxKind.TypeQuery:
                return TryVisitTypeQuery((TypeQueryNode)innerNode, boundary, context);
            case SyntaxKind.IndexedAccessType:
                return TryVisitIndexedAccess((IndexedAccessTypeNode)innerNode, boundary, context);
            case SyntaxKind.TypeOperator:
                var typeOperatorNode = (TypeOperatorNode)innerNode;
                if (typeOperatorNode.Operator == SyntaxKind.KeyOfKeyword)
                {
                    return TryVisitKeyOf(typeOperatorNode, boundary, context);
                }
                break;
        }
        return VisitNode(node, n => VisitExistingNodeTreeSymbols(n!, boundary, context), Utilities.IsTypeNode);
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:251-257 — <c>tryVisitIndexedAccess</c>.</summary>
    private ITypeNode? TryVisitIndexedAccess(IndexedAccessTypeNode node, IRecoveryBoundary boundary, ISyntacticTypeNodeBuilderContext context)
    {
        var resultObjectType = TryVisitSimpleTypeNode(node.ObjectType!, boundary, context);
        if (resultObjectType is null)
        {
            return null;
        }
        return _factory.UpdateIndexedAccessTypeNode(node, resultObjectType, VisitNode(node.IndexType, n => VisitExistingNodeTreeSymbols(n!, boundary, context), Utilities.IsTypeNode)!);
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:259-266 — <c>tryVisitKeyOf</c>.</summary>
    private ITypeNode? TryVisitKeyOf(TypeOperatorNode node, IRecoveryBoundary boundary, ISyntacticTypeNodeBuilderContext context)
    {
        Debug.AssertEqual(node.Operator, SyntaxKind.KeyOfKeyword);
        var type = TryVisitSimpleTypeNode(node.Type!, boundary, context);
        if (type is null)
        {
            return null;
        }
        return _factory.UpdateTypeOperatorNode(node, type);
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:268-282 — <c>tryVisitTypeQuery</c>.</summary>
    private ITypeNode? TryVisitTypeQuery(TypeQueryNode node, IRecoveryBoundary boundary, ISyntacticTypeNodeBuilderContext context)
    {
        var (introducesError, exprName) = TrackExistingEntityName(context, node.ExprName!);
        if (!introducesError)
        {
            return _factory.UpdateTypeQueryNode(
                node,
                exprName,
                VisitNodes(node.TypeArguments, n => VisitExistingNodeTreeSymbols(n!, boundary, context), Utilities.IsTypeNode));
        }

        var serializedName = _resolver.SerializeTypeName(context, node.ExprName, /*isTypeOf*/ true, null);
        if (serializedName is not null)
        {
            return MarkNodeReuse(context, serializedName, node.ExprName);
        }
        return null;
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:284-304 — <c>tryVisitTypeReference</c>.</summary>
    private ITypeNode? TryVisitTypeReference(TypeReferenceNode node, IRecoveryBoundary boundary, ISyntacticTypeNodeBuilderContext context)
    {
        if (_resolver.CanReuseTypeNode(context, node))
        {
            var (introducesError, newName) = TrackExistingEntityName(context, node.TypeName!);
            var typeArguments = VisitNodes(node.TypeArguments, n => VisitExistingNodeTreeSymbols(n!, boundary, context), Utilities.IsTypeNode);

            if (!introducesError)
            {
                var updated = _factory.UpdateTypeReferenceNode(
                    node,
                    newName,
                    typeArguments);
                return _resolver.MarkNodeReuse(context, updated, node);
            }
            else
            {
                var serializedName = _resolver.SerializeTypeName(context, node.TypeName, /*isTypeOf*/ false, typeArguments);
                if (serializedName is not null)
                {
                    return MarkNodeReuse(context, serializedName, node.TypeName);
                }
            }
        }
        return null;
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:306-576 — <c>visitExistingNodeTreeSymbolsWorker</c>.</summary>
    private Node? VisitExistingNodeTreeSymbolsWorker(Node node, IRecoveryBoundary boundary, ISyntacticTypeNodeBuilderContext context)
    {
        if (NodeTests.IsJSDocTypeExpression(node))
        {
            // Unwrap JSDocTypeExpressions
            return (Node?)VisitNode(((JSDocTypeExpression)node).Type, n => VisitExistingNodeTreeSymbols(n!, boundary, context), Utilities.IsTypeNode);
        }
        // We don't _actually_ support jsdoc namepath types, emit `any` instead
        if (NodeTests.IsJSDocAllType(node) || node.Kind == SyntaxKind.JSDocNamepathType)
        {
            return (Node)_factory.CreateKeywordTypeNode(SyntaxKind.AnyKeyword);
        }
        if (NodeTests.IsJSDocUnknownType(node))
        {
            return (Node)_factory.CreateKeywordTypeNode(SyntaxKind.UnknownKeyword);
        }
        if (NodeTests.IsJSDocNullableType(node))
        {
            return (Node)_factory.CreateUnionTypeNode(new List<ITypeNode> { (ITypeNode)VisitNode(((JSDocNullableType)node).Type, n => VisitExistingNodeTreeSymbols(n!, boundary, context), Utilities.IsTypeNode)!, _factory.CreateLiteralTypeNode(_factory.CreateNull()) });
        }
        if (NodeTests.IsJSDocOptionalType(node))
        {
            return (Node)_factory.CreateUnionTypeNode(new List<ITypeNode> { (ITypeNode)VisitNode(((JSDocOptionalType)node).Type, n => VisitExistingNodeTreeSymbols(n!, boundary, context), Utilities.IsTypeNode)!, _factory.CreateKeywordTypeNode(SyntaxKind.UndefinedKeyword) });
        }
        if (NodeTests.IsJSDocNonNullableType(node))
        {
            return (Node?)VisitNode(((JSDocNonNullableType)node).Type, n => VisitExistingNodeTreeSymbols(n!, boundary, context));
        }
        if (NodeTests.IsJSDocVariadicType(node))
        {
            return _factory.CreateArrayTypeNode(VisitNode(((JSDocVariadicType)node).Type, n => VisitExistingNodeTreeSymbols(n!, boundary, context), Utilities.IsTypeNode)!);
        }
        if (NodeTests.IsJSDocTypeLiteral(node))
        {
            var jsDocTypeLiteral = (JSDocTypeLiteral)node;
            return _factory.CreateTypeLiteralNode(Core.Map<JSDocPropertyLikeTag, ITypeElement>(jsDocTypeLiteral.JsDocPropertyTags, (t, _) =>
            {
                var name = VisitNode(NodeTests.IsIdentifier(t.Name!) ? t.Name! : ((QualifiedName)t.Name!).Right, n => VisitExistingNodeTreeSymbols(n!, boundary, context), NodeTests.IsIdentifier)!;
                var overrideTypeNode = _resolver.GetJsDocPropertyOverride(context, jsDocTypeLiteral, t);

                return _factory.CreatePropertySignature(
                    /*modifiers*/ null,
                    name,
                    t.IsBracketed || t.TypeExpression is not null && NodeTests.IsJSDocOptionalType((Node)t.TypeExpression.Type!) ? (PunctuationToken)_factory.CreateToken(SyntaxKind.QuestionToken) : null,
                    overrideTypeNode ?? (t.TypeExpression is not null ? VisitNode(t.TypeExpression.Type, n => VisitExistingNodeTreeSymbols(n!, boundary, context), Utilities.IsTypeNode) : null) ?? _factory.CreateKeywordTypeNode(SyntaxKind.AnyKeyword));
            }));
        }
        if (NodeTests.IsTypeReferenceNode(node) && NodeTests.IsIdentifier(((TypeReferenceNode)node).TypeName!) && ((Identifier)((TypeReferenceNode)node).TypeName!).EscapedText == "")
        {
            return NodeFactory.SetOriginalNode((Node)_factory.CreateKeywordTypeNode(SyntaxKind.AnyKeyword), node);
        }
        if ((NodeTests.IsExpressionWithTypeArguments(node) || NodeTests.IsTypeReferenceNode(node)) && Utilities.IsJSDocIndexSignature(node))
        {
            var typeArguments = ((INodeWithTypeArguments)node).TypeArguments!;
            return _factory.CreateTypeLiteralNode([_factory.CreateIndexSignature(
                /*modifiers*/ null,
                [_factory.CreateParameterDeclaration(
                    /*modifiers*/ null,
                    /*dotDotDotToken*/ null,
                    "x",
                    /*questionToken*/ null,
                    VisitNode(typeArguments[0], n => VisitExistingNodeTreeSymbols(n!, boundary, context), Utilities.IsTypeNode),
                    /*initializer*/ null)],
                VisitNode(typeArguments[1], n => VisitExistingNodeTreeSymbols(n!, boundary, context), Utilities.IsTypeNode))]);
        }
        if (NodeTests.IsJSDocFunctionType(node))
        {
            var jsDocFunctionType = (JSDocFunctionType)node;
            if (Utilities.IsJSDocConstructSignature(node))
            {
                ITypeNode? newTypeNode = null;
                return _factory.CreateConstructorTypeNode(
                    /*modifiers*/ null,
                    VisitNodes(jsDocFunctionType.TypeParameters, n => VisitExistingNodeTreeSymbols(n!, boundary, context), NodeTests.IsTypeParameterDeclaration),
                    Core.MapDefined<ParameterDeclaration, ParameterDeclaration>(jsDocFunctionType.Parameters, (p, i) =>
                    {
                        if (p.Name is not null && NodeTests.IsIdentifier(p.Name) && ((Identifier)p.Name).EscapedText == "new")
                        {
                            newTypeNode = p.Type;
                            return null;
                        }
                        return _factory.CreateParameterDeclaration(
                            /*modifiers*/ null,
                            GetEffectiveDotDotDotForParameter(p),
                            _resolver.MarkNodeReuse(context, _factory.CreateIdentifier(GetNameForJSDocFunctionParameter(p, i)), p),
                            _factory.CloneNode(p.QuestionToken),
                            VisitNode(p.Type, n => VisitExistingNodeTreeSymbols(n!, boundary, context), Utilities.IsTypeNode),
                            /*initializer*/ null);
                    }),
                    VisitNode(newTypeNode ?? jsDocFunctionType.Type, n => VisitExistingNodeTreeSymbols(n!, boundary, context), Utilities.IsTypeNode) ?? _factory.CreateKeywordTypeNode(SyntaxKind.AnyKeyword));
            }
            else
            {
                return _factory.CreateFunctionTypeNode(
                    VisitNodes(jsDocFunctionType.TypeParameters, n => VisitExistingNodeTreeSymbols(n!, boundary, context), NodeTests.IsTypeParameterDeclaration),
                    Core.Map(jsDocFunctionType.Parameters, (p, i) =>
                        _factory.CreateParameterDeclaration(
                            /*modifiers*/ null,
                            GetEffectiveDotDotDotForParameter(p),
                            _resolver.MarkNodeReuse(context, _factory.CreateIdentifier(GetNameForJSDocFunctionParameter(p, i)), p),
                            _factory.CloneNode(p.QuestionToken),
                            VisitNode(p.Type, n => VisitExistingNodeTreeSymbols(n!, boundary, context), Utilities.IsTypeNode),
                            /*initializer*/ null)),
                    VisitNode(jsDocFunctionType.Type, n => VisitExistingNodeTreeSymbols(n!, boundary, context), Utilities.IsTypeNode) ?? _factory.CreateKeywordTypeNode(SyntaxKind.AnyKeyword));
            }
        }
        if (NodeTests.IsThisTypeNode(node))
        {
            if (_resolver.CanReuseTypeNode(context, (ITypeNode)node))
            {
                return node;
            }
            boundary.MarkError();
            return node;
        }
        if (NodeTests.IsTypeParameterDeclaration(node))
        {
            var typeParameter = (TypeParameterDeclaration)node;
            var (_, newName) = TrackExistingEntityName(context, typeParameter.Name!);
            return _factory.UpdateTypeParameterDeclaration(
                typeParameter,
                VisitNodes(typeParameter.Modifiers, n => VisitExistingNodeTreeSymbols(n!, boundary, context), Utilities.IsModifier),
                // _resolver.MarkNodeReuse(context, TypeParameterToName(GetDeclaredTypeOfSymbol(GetSymbolOfDeclaration(node)), context), node),
                newName,
                VisitNode(typeParameter.Constraint, n => VisitExistingNodeTreeSymbols(n!, boundary, context), Utilities.IsTypeNode),
                VisitNode(typeParameter.Default, n => VisitExistingNodeTreeSymbols(n!, boundary, context), Utilities.IsTypeNode));
        }
        if (NodeTests.IsIndexedAccessTypeNode(node))
        {
            var result = TryVisitIndexedAccess((IndexedAccessTypeNode)node, boundary, context);
            if (result is null)
            {
                boundary.MarkError();
                return node;
            }
            return (Node?)result;
        }

        if (NodeTests.IsTypeReferenceNode(node))
        {
            var result = TryVisitTypeReference((TypeReferenceNode)node, boundary, context);
            if (result is not null)
            {
                return (Node?)result;
            }
            boundary.MarkError();
            return node;
        }
        if (Utilities.IsLiteralImportTypeNode(node))
        {
            var importType = (ImportTypeNode)node;
            // assert keyword in imported attributes is deprecated, so we don't reuse types that contain it
            // Ex: import("pkg", { assert: {} }
            if (importType.Attributes is not null && importType.Attributes.Token == SyntaxKind.AssertKeyword)
            {
                boundary.MarkError();
                return node;
            }
            if (!_resolver.CanReuseTypeNode(context, (ITypeNode)node))
            {
                return (Node?)_resolver.SerializeExistingTypeNode(context, (ITypeNode)node, /*addUndefined*/ false);
            }
            var argument = (LiteralTypeNode)importType.Argument!;
            var specifier = RewriteModuleSpecifier(importType, (StringLiteral)argument.Literal!, context);
            StringLiteral literal = ReferenceEquals(specifier, argument.Literal) ? ReuseNode(context, (StringLiteral)argument.Literal!)! : specifier;
            return _factory.UpdateImportTypeNode(
                importType,
                ReferenceEquals(literal, argument.Literal) ? ReuseNode(context, argument) : _factory.CreateLiteralTypeNode(literal),
                (ImportAttributes?)VisitNode(importType.Attributes, n => VisitExistingNodeTreeSymbols(n!, boundary, context), NodeTests.IsImportAttributes),
                VisitNode(importType.Qualifier, n => VisitExistingNodeTreeSymbols(n!, boundary, context), Utilities.IsEntityName),
                VisitNodes(importType.TypeArguments, n => VisitExistingNodeTreeSymbols(n!, boundary, context), Utilities.IsTypeNode),
                importType.IsTypeOf);
        }
        if (Utilities.IsNamedDeclaration(node) && Utilities.GetNamedDeclarationName(node)!.Kind == SyntaxKind.ComputedPropertyName && !HasLateBindableName(node))
        {
            if (!Utilities.HasDynamicName(node))
            {
                return VisitEachChild(node, boundary, context);
            }
            if (_resolver.ShouldRemoveDeclaration(context, new DynamicNamedDeclarationView((ComputedPropertyName)Utilities.GetNamedDeclarationName(node)!)))
            {
                return null;
            }
        }
        if (
            (Utilities.IsFunctionLike(node) && GetTypeSlotOfFunctionLike(node) is null)
            || (NodeTests.IsPropertyDeclaration(node) && ((PropertyDeclaration)node).Type is null && ((PropertyDeclaration)node).Initializer is null)
            || (NodeTests.IsPropertySignature(node) && ((PropertySignature)node).Type is null && ((PropertySignature)node).Initializer is null)
            || (NodeTests.IsParameter(node) && ((ParameterDeclaration)node).Type is null && ((ParameterDeclaration)node).Initializer is null)
        )
        {
            var visited = VisitEachChild(node, boundary, context);
            if (ReferenceEquals(visited, node))
            {
                visited = _resolver.MarkNodeReuse(context, _factory.CloneNode(node), node);
            }
            // PORT-NOTE: TS's `(visited as Mutable<typeof visited>).type = …` writes the one slot the four guards above
            // proved is present; `Mutable<T>` is typespace-only (§15.9) and the four arms reach it through four different
            // classes, so <see cref="SetTypeOfDeclarationNode"/> states the write per kind.
            SetTypeOfDeclarationNode(visited, _factory.CreateKeywordTypeNode(SyntaxKind.AnyKeyword));
            if (NodeTests.IsParameter(node))
            {
                ((ParameterDeclaration)visited).Modifiers = null;
            }
            return visited;
        }
        if (NodeTests.IsTypeQueryNode(node))
        {
            var result = TryVisitTypeQuery((TypeQueryNode)node, boundary, context);
            if (result is null)
            {
                boundary.MarkError();
                return node;
            }
            return (Node?)result;
        }
        if (NodeTests.IsComputedPropertyName(node) && Utilities.IsEntityNameExpression(((ComputedPropertyName)node).Expression!))
        {
            var computedPropertyName = (ComputedPropertyName)node;
            var (introducesError, result) = TrackExistingEntityName(context, (Node)computedPropertyName.Expression!);
            if (!introducesError)
            {
                return _factory.UpdateComputedPropertyName(computedPropertyName, (Expression)result);
            }
            else
            {
                var computedPropertyNameType = _resolver.SerializeTypeOfExpression(context, computedPropertyName.Expression);
                Node literal;
                if (NodeTests.IsLiteralTypeNode((Node)computedPropertyNameType))
                {
                    literal = ((LiteralTypeNode)computedPropertyNameType).Literal!;
                }
                else
                {
                    var evaluated = _resolver.EvaluateEntityNameExpression((MemberExpression)computedPropertyName.Expression!);
                    Node? literalNode = evaluated.Value is string stringValue ? _factory.CreateStringLiteral(stringValue, /*isSingleQuote*/ null) :
                        evaluated.Value is int intValue ? _factory.CreateNumericLiteral(intValue, /*numericLiteralFlags*/ TokenFlags.None) :
                        null;
                    if (literalNode is null)
                    {
                        if (NodeTests.IsImportTypeNode((Node)computedPropertyNameType))
                        {
                            _resolver.TrackComputedName(context, computedPropertyName.Expression!);
                        }
                        return node;
                    }
                    literal = literalNode;
                }
                if (literal.Kind == SyntaxKind.StringLiteral && ScannerUtilities.IsIdentifierText(((StringLiteral)literal).Text!, Utilities.GetEmitScriptTarget(_options)))
                {
                    return _factory.CreateIdentifier(((StringLiteral)literal).Text);
                }
                if (literal.Kind == SyntaxKind.NumericLiteral && !((NumericLiteral)literal).Text!.StartsWith("-"))
                {
                    return literal;
                }
                return _factory.UpdateComputedPropertyName(computedPropertyName, (Expression)literal);
            }
        }
        if (NodeTests.IsTypePredicateNode(node))
        {
            var typePredicate = (TypePredicateNode)node;
            Node? parameterName;
            if (NodeTests.IsIdentifier(typePredicate.ParameterName!))
            {
                var (introducesError, result) = TrackExistingEntityName(context, typePredicate.ParameterName!);
                // Should not usually happen the only case is when a type predicate comes from a JSDoc type annotation with it's own parameter symbol definition.
                // /** @type {(v: unknown) => v is undefined} */
                // const isUndef = v => v === undefined;
                if (introducesError) boundary.MarkError();
                parameterName = result;
            }
            else
            {
                parameterName = _factory.CloneNode(typePredicate.ParameterName!);
            }
            return _factory.UpdateTypePredicateNode(typePredicate, _factory.CloneNode(typePredicate.AssertsModifier!), parameterName, VisitNode(typePredicate.Type, n => VisitExistingNodeTreeSymbols(n!, boundary, context), Utilities.IsTypeNode));
        }

        if (NodeTests.IsTupleTypeNode(node) || NodeTests.IsTypeLiteralNode(node) || NodeTests.IsMappedTypeNode(node))
        {
            var visited = VisitEachChild(node, boundary, context);
            var clone = _resolver.MarkNodeReuse(context, ReferenceEquals(visited, node) ? _factory.CloneNode(node) : visited, node);
            var flags = Utilities.GetEmitFlags(clone);
            EmitNodes.SetEmitFlags(clone, flags | ((context.Flags & NodeBuilderFlags.MultilineObjectLiterals) != 0 && NodeTests.IsTypeLiteralNode(node) ? EmitFlags.None : EmitFlags.SingleLine));
            return clone;
        }
        if (NodeTests.IsStringLiteral(node) && (context.Flags & NodeBuilderFlags.UseSingleQuotesForStringLiteralType) != 0 && ((StringLiteral)node).SingleQuote != true)
        {
            // PORT-NOTE: TS's `(clone as Mutable<typeof clone>).singleQuote = true` writes the same slot directly here.
            var clone = _factory.CloneNode((StringLiteral)node);
            clone.SingleQuote = true;
            return clone;
        }
        if (NodeTests.IsConditionalTypeNode(node))
        {
            var conditionalType = (ConditionalTypeNode)node;
            var checkType = VisitNode(conditionalType.CheckType, n => VisitExistingNodeTreeSymbols(n!, boundary, context), Utilities.IsTypeNode)!;

            var disposeScope = _resolver.EnterNewScope(context, node);
            var extendType = VisitNode(conditionalType.ExtendsType, n => VisitExistingNodeTreeSymbols(n!, boundary, context), Utilities.IsTypeNode)!;
            var trueType = VisitNode(conditionalType.TrueType, n => VisitExistingNodeTreeSymbols(n!, boundary, context), Utilities.IsTypeNode)!;
            disposeScope();
            var falseType = VisitNode(conditionalType.FalseType, n => VisitExistingNodeTreeSymbols(n!, boundary, context), Utilities.IsTypeNode)!;
            return _factory.UpdateConditionalTypeNode(
                conditionalType,
                checkType,
                extendType,
                trueType,
                falseType);
        }

        if (NodeTests.IsTypeOperatorNode(node))
        {
            var typeOperator = (TypeOperatorNode)node;
            if (typeOperator.Operator == SyntaxKind.UniqueKeyword && typeOperator.Type!.Kind == SyntaxKind.SymbolKeyword)
            {
                if (!_resolver.CanReuseTypeNode(context, (ITypeNode)node))
                {
                    boundary.MarkError();
                    return node;
                }
            }
            else if (typeOperator.Operator == SyntaxKind.KeyOfKeyword)
            {
                var result = TryVisitKeyOf(typeOperator, boundary, context);
                if (result is null)
                {
                    boundary.MarkError();
                    return node;
                }
                return (Node?)result;
            }
        }

        return VisitEachChild(node, boundary, context);
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:580-583 — the nested <c>visitEachChild</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's local wrapper keeps <c>context</c> undefined and only replaces the nodes visitor when
    /// the node comes from another file; <c>Utilities.VisitEachChildWorker</c> is the port's loud, named blocker for
    /// <c>visitorPublic.ts</c>'s <c>visitEachChild</c> (aliased at checker.ts:1134, Layer 6/M7), so a reuse walk that
    /// reaches this arm throws by name instead of returning a partially visited node — the same statement the landed
    /// node builder makes at <c>NodeBuilder.Names.cs:1350</c>.
    /// </remarks>
    private Node VisitEachChild(Node node, IRecoveryBoundary boundary, ISyntacticTypeNodeBuilderContext context)
    {
        var nonlocalNode = context.EnclosingFile is null || !ReferenceEquals(context.EnclosingFile, Utilities.GetSourceFileOfNode(node));
        return Utilities.VisitEachChildWorker(node, n => VisitExistingNodeTreeSymbols(n!, boundary, context), /*context*/ null, nonlocalNode ? VisitNodesWithoutCopyingPositions : null, n => VisitExistingNodeTreeSymbols(n!, boundary, context));
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:585-602 — <c>visitNodesWithoutCopyingPositions</c>.</summary>
    private static NodeArray<Node>? VisitNodesWithoutCopyingPositions(
        NodeArray<Node>? nodes,
        Func<Node, object?> visitor,
        Func<Node, bool>? test = null,
        int? start = null,
        int? count = null)
    {
        var result = Utilities.VisitNodes(nodes, visitor, test, start, count);
        if (result is not null)
        {
            if (result.Pos != -1 || result.End != -1)
            {
                if (ReferenceEquals(result, nodes))
                {
                    result = NodeFactory.Factory.CreateNodeArray<Node>([.. nodes!], nodes!.HasTrailingComma);
                }
                Utilities.SetTextRangePosEnd(result, -1, -1);
            }
        }
        return result;
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:604-607 — <c>getEffectiveDotDotDotForParameter</c>.</summary>
    private static PunctuationToken? GetEffectiveDotDotDotForParameter(ParameterDeclaration p)
        => p.DotDotDotToken ?? (p.Type is not null && NodeTests.IsJSDocVariadicType((Node)p.Type) ? (PunctuationToken)NodeFactory.Factory.CreateToken(SyntaxKind.DotDotDotToken) : null);

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:609-613 — <c>getNameForJSDocFunctionParameter</c>.</summary>
    /// <remarks>Note that `new:T` parameters are not handled, but should be before calling this function.</remarks>
    private static string GetNameForJSDocFunctionParameter(ParameterDeclaration p, int index)
        => p.Name is not null && NodeTests.IsIdentifier(p.Name) && ((Identifier)p.Name).EscapedText == "this" ? "this"
            : GetEffectiveDotDotDotForParameter(p) is not null ? "args"
            : $"arg{index}";

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:615-618 — <c>rewriteModuleSpecifier</c>.</summary>
    private StringLiteral RewriteModuleSpecifier(ImportTypeNode parent, StringLiteral lit, ISyntacticTypeNodeBuilderContext context)
    {
        var newName = _resolver.GetModuleSpecifierOverride(context, parent, lit);
        return newName is not null ? NodeFactory.SetOriginalNode(_factory.CreateStringLiteral(newName, null), lit) : lit;
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:458 — the <c>node.type</c> read of the function-like arm.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads the shared <c>type</c> slot of its <c>SignatureDeclaration</c> union; C# answers the same
    /// question with <c>object?</c> because <c>JSDocSignature</c> is the one function-like kind that does not implement
    /// <see cref="ISignatureDeclarationBase"/> (its slot is a <c>JSDocReturnTag</c>, types.ts:4102) — the test is a
    /// <c>null</c> test, so neither arm needs a conversion.
    /// </remarks>
    private static object? GetTypeSlotOfFunctionLike(Node node)
    {
        switch (node)
        {
            case ISignatureDeclarationBase signature:
                return signature.Type;
            case JSDocSignature jsDocSignature:
                return jsDocSignature.Type;
            default:
                Debug.AssertNever(node, $"not a function-like node: {Debug.FormatSyntaxKind(node.Kind)}");
                return null;
        }
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:466 — <c>(visited as Mutable&lt;typeof visited&gt;).type</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: TS writes the slot through <c>Mutable&lt;typeof visited&gt;</c>, whose narrowed argument is
    /// <c>SignatureDeclaration | PropertyDeclaration | PropertySignature | ParameterDeclaration</c>. C# reaches those
    /// four arms through sixteen classes with no shared settable <c>Type</c> slot
    /// (<c>ISignatureDeclarationBase.Type</c> is get-only), so the write is stated per kind, exactly as the binder
    /// states its own <c>Mutable&lt;…&gt;</c> writes (<c>Binder.Scopes.cs:9-23</c>).
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>JSDocSignature</c> is the one arm of TS's <c>SignatureDeclaration</c> whose slot is not a
    /// <c>TypeNode</c> — types.ts:4102 declares it <c>JSDocReturnTag | undefined</c> — so the keyword type node the
    /// reference writes there has no C# value. Nothing downstream reads that slot on an
    /// <c>expressionToTypeNode</c>-synthesized node (the readers are JSDoc-aware and expect a tag), so the arm is left
    /// alone; see the slice report.
    /// </para>
    /// </remarks>
    private static void SetTypeOfDeclarationNode(Node node, ITypeNode type)
    {
        switch (node.Kind)
        {
            case SyntaxKind.Parameter:
                ((ParameterDeclaration)node).Type = type;
                break;
            case SyntaxKind.PropertyDeclaration:
                ((PropertyDeclaration)node).Type = type;
                break;
            case SyntaxKind.PropertySignature:
                ((PropertySignature)node).Type = type;
                break;
            case SyntaxKind.FunctionDeclaration:
                ((FunctionDeclaration)node).Type = type;
                break;
            case SyntaxKind.FunctionExpression:
                ((FunctionExpression)node).Type = type;
                break;
            case SyntaxKind.ArrowFunction:
                ((ArrowFunction)node).Type = type;
                break;
            case SyntaxKind.MethodDeclaration:
                ((MethodDeclaration)node).Type = type;
                break;
            case SyntaxKind.MethodSignature:
                ((MethodSignature)node).Type = type;
                break;
            case SyntaxKind.CallSignature:
                ((CallSignatureDeclaration)node).Type = type;
                break;
            case SyntaxKind.ConstructSignature:
                ((ConstructSignatureDeclaration)node).Type = type;
                break;
            case SyntaxKind.Constructor:
                ((ConstructorDeclaration)node).Type = type;
                break;
            case SyntaxKind.GetAccessor:
                ((GetAccessorDeclaration)node).Type = type;
                break;
            case SyntaxKind.SetAccessor:
                ((SetAccessorDeclaration)node).Type = type;
                break;
            case SyntaxKind.IndexSignature:
                ((IndexSignatureDeclaration)node).Type = type;
                break;
            case SyntaxKind.FunctionType:
                ((FunctionTypeNode)node).Type = type;
                break;
            case SyntaxKind.ConstructorType:
                ((ConstructorTypeNode)node).Type = type;
                break;
            case SyntaxKind.JSDocFunctionType:
                ((JSDocFunctionType)node).Type = type;
                break;
            case SyntaxKind.JSDocSignature:
                break;
            default:
                Debug.AssertNever(node, $"not a declaration with a writable type slot: {Debug.FormatSyntaxKind(node.Kind)}");
                break;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // serializeExistingTypeNode (expressionToTypeNode.ts:622-663)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:624-635 — <c>serializeExistingTypeNode</c>.</summary>
    private ITypeNode? SerializeExistingTypeNode(ITypeNode? typeNode, ISyntacticTypeNodeBuilderContext context, bool? addUndefined = null)
    {
        if (typeNode is null) return null;
        ITypeNode? result = null;
        if (
            (addUndefined != true || CanAddUndefined(typeNode)) && _resolver.CanReuseTypeNode(context, typeNode)
        )
        {
            result = TryReuseExistingTypeNodeWorker(context, typeNode);
            if (result is not null)
            {
                result = AddUndefinedIfNeeded(result, addUndefined, /*owner*/ null, context);
            }
        }
        return result;
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:637-654 — <c>serializeTypeAnnotationOfDeclaration</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: TS's <c>node</c> is <c>Declaration</c>, which the port's model states as <see cref="IDeclaration"/>;
    /// the calls into the resolver pass the node through that interface.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>useFallback = requiresAddingUndefined !== undefined</c> default is a nullable parameter here
    /// whose <c>null</c> means "the default applies", which is how the port states a JS default the caller may not
    /// distinguish from an explicit <c>false</c> (the reference's own callers only ever omit it).
    /// </para>
    /// </remarks>
    private ITypeNode? SerializeTypeAnnotationOfDeclaration(ITypeNode? declaredType, ISyntacticTypeNodeBuilderContext context, Node node, ISymbol? symbol, bool? requiresAddingUndefined = null, bool? useFallback = null)
    {
        if (declaredType is null) return null;
        if (!_resolver.CanReuseTypeNodeAnnotation(context, (IDeclaration)node, declaredType, symbol, requiresAddingUndefined))
        {
            // If we need to add undefined, can add undefined, and the resolver says we can reuse the type, we reuse the type
            // If we don't know syntactically that we can add the undefined, we will report the fallback below.
            if (requiresAddingUndefined is not true || !_resolver.CanReuseTypeNodeAnnotation(context, (IDeclaration)node, declaredType, symbol, /*requiresAddingUndefined*/ false))
            {
                return null;
            }
        }
        ITypeNode? result = null;
        if (requiresAddingUndefined is not true || CanAddUndefined(declaredType))
        {
            result = SerializeExistingTypeNode(declaredType, context, requiresAddingUndefined);
        }
        if (result is not null || !(useFallback ?? requiresAddingUndefined is not null))
        {
            return result;
        }
        ReportInferenceFallback(context, node);
        return _resolver.SerializeExistingTypeNode(context, declaredType, requiresAddingUndefined) ?? _factory.CreateKeywordTypeNode(SyntaxKind.AnyKeyword);
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:656-663 — <c>serializeExistingTypeNodeWithFallback</c>.</summary>
    private ITypeNode? SerializeExistingTypeNodeWithFallback(ITypeNode? typeNode, ISyntacticTypeNodeBuilderContext context, bool? addUndefined = null, Node? targetNode = null)
    {
        if (typeNode is null) return null;
        var result = SerializeExistingTypeNode(typeNode, context, addUndefined);
        if (result is not null)
        {
            return result;
        }
        ReportInferenceFallback(context, targetNode ?? (Node)typeNode);
        return _resolver.SerializeExistingTypeNode(context, typeNode, addUndefined) ?? _factory.CreateKeywordTypeNode(SyntaxKind.AnyKeyword);
    }

    // ---------------------------------------------------------------------------------------------
    // visitorPublic.ts:148-192 (visitNode) and :223-271 (visitNodes, generic — see the class remarks)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/visitorPublic.ts:148-192 — <c>visitNode</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: <c>visitorPublic.ts</c> (Layer 6) is not ported — the checker's node builder reads
    /// <c>visitEachChild</c> through the loud <c>Utilities.VisitEachChildWorker</c> blocker and calls this member
    /// nowhere else — so this file states the overload the reference uses: every caller passes a <c>Visitor</c> that
    /// answers a node or <c>undefined</c> (never an array) plus the <c>test</c> the reference hands to
    /// <c>Debug.assertNode</c>.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>TIn | TVisited | TOut</c> collapses to the type variable <c>T</c> the caller names, with the
    /// one conversion the model's class/interface split needs (a <c>NodeArray&lt;ITypeNode&gt;</c> element is an
    /// <c>ITypeNode</c>, which is not a <c>Node</c>): §3's boundary, stated once here instead of at ~30 call sites. The
    /// member is private to this class, so it collides with nothing when the <c>visitorPublic.ts</c> slice lands.
    /// </para>
    /// </remarks>
    private static T? VisitNode<T>(T? node, Func<Node, object?> visitor, Func<Node, bool>? test = null, Func<IReadOnlyList<Node>, Node>? lift = null)
    {
        if (node is null)
        {
            // If the input type is undefined, then the output type can be undefined.
            return default;
        }

        var visited = visitor((Node)(object)node);

        Node? visitedNode;
        if (visited is null)
        {
            // If the visited node is undefined, then the visitor must have returned undefined,
            // so the visitor must have been declared as able to return undefined, so TOut must be
            // potentially undefined.
            return default;
        }
        else if (visited is IReadOnlyList<Node> visitedArray)
        {
            visitedNode = (lift ?? ExtractSingleNode)(visitedArray);
        }
        else
        {
            visitedNode = (Node)visited;
        }

        Debug.AssertNode(visitedNode, test);
        return visitedNode is null ? default : (T)(object)visitedNode;
    }

    /// <summary>ported from: src/compiler/visitorPublic.ts:1246-1249 — <c>extractSingleNode</c>.</summary>
    private static Node ExtractSingleNode(IReadOnlyList<Node> nodes)
    {
        Debug.Assert(nodes.Count <= 1, "Too many nodes returned from a visiting function");
        return nodes.Count > 0 ? nodes[0] : null!;
    }

    /// <summary>ported from: src/compiler/visitorPublic.ts:223-271 — <c>visitNodes</c>, generic in the array's element type.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the same member is landed as <c>Utilities.VisitNodes</c> (<c>Checker/Utilities.Checker.cs:665</c>),
    /// fixed to <c>NodeArray&lt;Node&gt;</c>. C# generics are invariant, so that member cannot take the
    /// <c>NodeArray&lt;ITypeNode&gt;</c>/<c>NodeArray&lt;TypeParameterDeclaration&gt;</c>/<c>NodeArray&lt;ModifierToken&gt;</c>
    /// this file visits without materializing a copy — which would lose the reference's "the input array comes back
    /// unchanged when every element is reused" identity and turn every <c>updateX</c> into a rebuild. This generic
    /// sibling keeps the reference's element type and its identity behaviour, and must be deleted (with its callers
    /// re-pointed at <c>Utilities.VisitNodes</c>) when that member is generalized.
    /// </para>
    /// <para>
    /// PORT-NOTE: the element type is unconstrained because a <c>NodeArray&lt;ITypeNode&gt;</c> is not a
    /// <c>NodeArray&lt;Node&gt;</c>; the reads and writes go through the <c>Node</c> the visitor and the factory use,
    /// which is the class/interface conversion §3 fixes (<c>NodeFactory.CopyCustomPrologue</c> states the same one).
    /// </para>
    /// </remarks>
    private static NodeArray<T>? VisitNodes<T>(
        NodeArray<T>? nodes,
        Func<Node, object?>? visitor,
        Func<Node, bool>? test = null,
        int? start = null,
        int? count = null)
        where T : INode
    {
        if (nodes is null)
        {
            // If the input type is undefined, then the output type can be undefined.
            return nodes;
        }

        // Ensure start and count have valid values
        var length = nodes.Count;
        if (start is null || start < 0)
        {
            start = 0;
        }

        if (count is null || count > length - start)
        {
            count = length - start;
        }

        var pos = nodes.Pos;
        var end = nodes.End;
        var hasTrailingComma = nodes.HasTrailingComma;

        var updated = VisitArrayWorker(nodes, visitor, test, start.Value, count.Value);
        if (updated is not null)
        {
            var updatedArray = NodeFactory.Factory.CreateNodeArray(updated, hasTrailingComma);
            Utilities.SetTextRangePosEnd(updatedArray, pos, end);
            return updatedArray;
        }

        // If we are here, updated === undefined. This means that it's still a NodeArray,
        // and also that its contents passed the tests in visitArrayWorker, so has contents
        // of type TOut.
        return nodes;

        static List<T>? VisitArrayWorker(
            NodeArray<T> nodes,
            Func<Node, object?>? visitor,
            Func<Node, bool>? test,
            int start,
            int count)
        {
            List<T>? updated = null;

            var length = nodes.Count;
            if (start > 0 || count < length)
            {
                // If we are not visiting all of the original nodes, we must always create a new array.
                updated = [];
            }

            // Visit each original node.
            for (var i = 0; i < count; i++)
            {
                var node = nodes[i + start];
                var visited = visitor is not null ? visitor((Node)(object)node!) : (object?)node;
                if (updated is not null || visited is null || !ReferenceEquals(visited, node))
                {
                    if (updated is null)
                    {
                        // Ensure we have a copy of `nodes`, up to the current index.
                        updated = [];
                        for (var j = 0; j < i; j++)
                        {
                            updated.Add(nodes[j]!);
                        }
                        AssertEachNode(nodes.Take(i), test);
                    }
                    if (visited is not null)
                    {
                        updated.Add((T)(object)visited);
                    }
                    else
                    {
                        // PORT-NOTE: a `VisitResult` of `undefined` means "reuse this node"; the port's visitor slot is
                        // `object?`, so TS's `undefined` is its `null`.
                        updated.Add(nodes[i + start]!);
                    }
                }
                else if (updated is not null)
                {
                    updated.Add((T)(object)visited!);
                }
            }

            if (updated is not null)
            {
                AssertEachNode(nodes.Skip(start).Take(count), test);
            }

            return updated;

            static void AssertEachNode(IEnumerable<T> nodes, Func<Node, bool>? test)
            {
                foreach (var node in nodes)
                {
                    AssertNode(node, test);
                }
            }

            static void AssertNode(T node, Func<Node, bool>? test)
                => Debug.Assert(test is null || test((Node)(object)node!), "visited node must pass the test");
        }
    }
}
