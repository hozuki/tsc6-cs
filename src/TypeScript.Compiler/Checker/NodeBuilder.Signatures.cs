using System.Text.RegularExpressions;

namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:7977-8437 — the signature/parameter half of `createNodeBuilder`
// ported from: src/compiler/checker.ts:8003-8007 — interface SignatureToSignatureDeclarationOptions
/// <summary>
/// The node builder's signature half: index-signature and signature declarations, the recovery boundary, the scope the
/// parameter builder enters, type-parameter declarations, type predicates and parameter declarations.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS declares all of these inside the 4,860-line <c>createNodeBuilder</c>; the port states them as members of
/// <see cref="Checker"/> (see <c>Checker.NodeBuilder.cs</c>'s class remarks for why the object literal collapses).
/// </para>
/// <para>
/// PORT-NOTE: the members this file calls but does not declare, and their owners: <c>GetExpandedParameters</c>
/// (checker.ts:14187) and <c>IsOptionalParameter</c> (checker.ts:16091) are M6-2's; <c>TypeToTypeNodeHelper</c>
/// (6760) is agent A's; <c>CloneNodeBuilderContext</c> (9021), <c>TypeParameterToName</c> (8829),
/// <c>GetEffectiveParameterDeclaration</c>'s two readers <c>SerializeTypeForDeclaration</c> (9098) and
/// <c>SerializeReturnTypeForSignature</c> (9144), and <c>TrackComputedName</c> (8438) are agent D's;
/// <c>VisitEachChildWorker</c> (checker.ts:1134) is the coordinator's; <c>Utilities.GetNameFromIndexInfo</c>
/// (utilities.ts:2354), <c>Utilities.GetModifiers</c> (utilitiesPublic.ts:1030) and <c>Utilities.IsTransientSymbol</c>
/// (utilities.ts:655) are the utilities slice's.
/// </para>
/// <para>
/// PORT-NOTE: three signature facts of this file that differ from the naive transcription, each also stated at the
/// member: <c>ParameterToParameterDeclarationName</c> returns <c>object?</c>, not the contract's <c>string</c>, because
/// TS's return type is <c>BindingName | string</c> (types.ts:10638) and <c>string</c> would stringify every
/// destructuring parameter name; <c>TypePredicateToTypePredicateNodeHelper</c> takes <c>ITypePredicateBase</c>, the
/// only predicate face the port has; and the signature factory dispatch is a <c>switch</c> expression whose discard arm
/// is <c>Debug.Fail&lt;Node&gt;</c>, because the reference's terminal <c>Debug.assertNever(kind)</c> is <c>void</c> here.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // Index signatures (checker.ts:7977-8008)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:7977-8001 — <c>indexInfoToIndexSignatureDeclarationHelper</c>.</summary>
    internal IndexSignatureDeclaration IndexInfoToIndexSignatureDeclarationHelper(IndexInfo indexInfo, NodeBuilderContext context, ITypeNode? typeNode)
    {
        var name = Utilities.GetNameFromIndexInfo(indexInfo);
        var indexName = Js.IsTruthy(name) ? name : "x";
        var indexerTypeNode = TypeToTypeNodeHelper(indexInfo.KeyType!, context);

        var indexingParameter = _factory.CreateParameterDeclaration(
            /*modifiers*/ null,
            /*dotDotDotToken*/ null,
            indexName,
            /*questionToken*/ null,
            indexerTypeNode,
            /*initializer*/ null);
        if (typeNode is null)
        {
            typeNode = TypeToTypeNodeHelper(indexInfo.Type ?? _anyType, context);
        }
        if (indexInfo.Type is null && (context.Flags & NodeBuilderFlags.AllowEmptyIndexInfoType) == 0)
        {
            context.EncounteredError = true;
        }
        context.ApproximateLength += indexName.Length + 4;
        return _factory.CreateIndexSignature(
            indexInfo.IsReadonly ? [_factory.CreateToken(SyntaxKind.ReadonlyKeyword)] : null,
            [indexingParameter],
            typeNode);
    }

    // ---------------------------------------------------------------------------------------------
    // Signature declarations (checker.ts:8009-8069)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:8009-8068 — <c>signatureToSignatureDeclarationHelper</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the reference dispatches on <paramref name="kind"/> with a chain of conditional expressions whose last
    /// arm is <c>Debug.assertNever(kind)</c> — declared <c>never</c>, so it can stand in value position. The port's
    /// <see cref="Debug.AssertNever"/> is <c>void</c>, so the chain is a <c>switch</c> expression with the same arms
    /// (C# target-types it to <c>Node</c>, which is the member's return type) and
    /// <c>Debug.Fail&lt;Node&gt;($"Illegal value: {kind}")</c> as the discard arm — the same throw, and the same message
    /// text <see cref="Debug.AssertNever"/> itself would produce.
    /// </para>
    /// <para>
    /// PORT-NOTE: the generated factories take the modifiers slot as either <c>List&lt;ModifierToken&gt;?</c> (the
    /// signature and arrow/function-expression kinds, whose model slot is a modifier list) or <c>List&lt;Node&gt;?</c>
    /// (the declaration kinds); TS has one <c>Modifier[]</c> for both, so the port keeps TS's <c>modifiers</c> and
    /// projects it once into <c>nodeModifiers</c>.
    /// </para>
    /// <para>
    /// PORT-NOTE: the JSDoc-overload comment is built with <see cref="Regex"/> where TS uses two literals
    /// (<c>/\r\n|\n|\r/</c> and <c>/^\s+/</c>), and TS's <c>slice(2, -2)</c> — which clamps — becomes a guarded range
    /// expression, because C#'s <c>[2..^2]</c> throws on a string shorter than four characters.
    /// </para>
    /// </remarks>
    internal Node SignatureToSignatureDeclarationHelper(ISignature signature, SyntaxKind kind, NodeBuilderContext context, SignatureToSignatureDeclarationOptions? options = null)
    {
        List<TypeParameterDeclaration>? typeParameters = null;
        List<ITypeNode>? typeArguments = null;

        var expandedParams = Core.ElementAt(GetExpandedParameters(signature, /*skipUnionExpanding*/ true), 0);
        var cleanup = EnterNewScope(context, signature.Declaration, expandedParams, signature.TypeParameters, signature.Parameters, (TypeMapper?)signature.Mapper);
        context.ApproximateLength += 3; // Usually a signature contributes a few more characters than this, but 3 is the minimum

        var target = signature.Target;
        var mapper = (TypeMapper?)signature.Mapper;
        if ((context.Flags & NodeBuilderFlags.WriteTypeArgumentsOfSignature) != 0 && target is not null && mapper is not null && target.TypeParameters is not null)
        {
            var targetTypeParameters = target.TypeParameters;
            typeArguments = Core.Map(targetTypeParameters, (parameter, _) => TypeToTypeNodeHelper(InstantiateType(parameter, mapper)!, context));
        }
        else
        {
            typeParameters = signature.TypeParameters is not null
                ? Core.Map(signature.TypeParameters, (parameter, _) => TypeParameterToDeclaration(parameter, context))
                : null;
        }

        var restoreFlags = SaveRestoreFlags(context);
        context.Flags &= ~NodeBuilderFlags.SuppressAnyReturnType; // SuppressAnyReturnType should only apply to the signature `return` position
        // If the expanded parameter list had a variadic in a non-trailing position, don't expand it
        var parameterSymbols = Core.Some(expandedParams, p => !ReferenceEquals(p, Core.Last(expandedParams!)) && (GetCheckFlags(p) & CheckFlags.RestParameter) != 0)
            ? signature.Parameters
            : expandedParams;
        var parameters = Core.Map(parameterSymbols, (parameter, _) => SymbolToParameterDeclaration(parameter, context, kind == SyntaxKind.Constructor));
        var thisParameter = (context.Flags & NodeBuilderFlags.OmitThisParameter) != 0 ? null : TryGetThisParameterDeclaration(signature, context);
        if (thisParameter is not null)
        {
            parameters!.Insert(0, thisParameter);
        }
        restoreFlags();

        var returnTypeNode = SerializeReturnTypeForSignature(context, signature);

        var modifiers = options?.Modifiers;
        if (kind == SyntaxKind.ConstructorType && (signature.Flags & SignatureFlags.Abstract) != 0)
        {
            var modifierFlags = Utilities.ModifiersToFlags(modifiers);
            modifiers = _factory.CreateModifiersFromModifierFlags(modifierFlags | ModifierFlags.Abstract);
        }
        var nodeModifiers = modifiers is null ? null : Core.Map(modifiers, (modifier, _) => (Node)modifier);

        Node node = kind switch
        {
            SyntaxKind.CallSignature => _factory.CreateCallSignature(typeParameters, parameters, returnTypeNode),
            SyntaxKind.ConstructSignature => _factory.CreateConstructSignature(typeParameters, parameters, returnTypeNode),
            SyntaxKind.MethodSignature => _factory.CreateMethodSignature(modifiers, options?.Name ?? _factory.CreateIdentifier(""), options?.QuestionToken, typeParameters, parameters, returnTypeNode),
            SyntaxKind.MethodDeclaration => _factory.CreateMethodDeclaration(nodeModifiers, /*asteriskToken*/ null, options?.Name ?? _factory.CreateIdentifier(""), /*questionToken*/ null, typeParameters, parameters, returnTypeNode, /*body*/ null),
            SyntaxKind.Constructor => _factory.CreateConstructorDeclaration(nodeModifiers, parameters, /*body*/ null),
            SyntaxKind.GetAccessor => _factory.CreateGetAccessorDeclaration(nodeModifiers, options?.Name ?? _factory.CreateIdentifier(""), parameters, returnTypeNode, /*body*/ null),
            SyntaxKind.SetAccessor => _factory.CreateSetAccessorDeclaration(nodeModifiers, options?.Name ?? _factory.CreateIdentifier(""), parameters, /*body*/ null),
            SyntaxKind.IndexSignature => _factory.CreateIndexSignature(nodeModifiers, parameters, returnTypeNode),
            SyntaxKind.JSDocFunctionType => _factory.CreateJSDocFunctionType(parameters, returnTypeNode),
            SyntaxKind.FunctionType => _factory.CreateFunctionTypeNode(typeParameters, parameters, returnTypeNode ?? _factory.CreateTypeReferenceNode(_factory.CreateIdentifier(""), null)),
            SyntaxKind.ConstructorType => _factory.CreateConstructorTypeNode(modifiers, typeParameters, parameters, returnTypeNode ?? _factory.CreateTypeReferenceNode(_factory.CreateIdentifier(""), null)),
            SyntaxKind.FunctionDeclaration => _factory.CreateFunctionDeclaration(nodeModifiers, /*asteriskToken*/ null, options?.Name is not null ? (Identifier)options.Name : _factory.CreateIdentifier(""), typeParameters, parameters, returnTypeNode, /*body*/ null),
            SyntaxKind.FunctionExpression => _factory.CreateFunctionExpression(modifiers, /*asteriskToken*/ null, options?.Name is not null ? (Identifier)options.Name : _factory.CreateIdentifier(""), typeParameters, parameters, returnTypeNode, _factory.CreateBlock(Core.EmptyArray<IStatement>.Value, false)),
            SyntaxKind.ArrowFunction => _factory.CreateArrowFunction(modifiers, typeParameters, parameters, returnTypeNode, /*equalsGreaterThanToken*/ null, _factory.CreateBlock(Core.EmptyArray<IStatement>.Value, false)),
            _ => Debug.Fail<Node>($"Illegal value: {kind}"),
        };

        if (typeArguments is not null)
        {
            ((ISignatureDeclarationBase)node).TypeArguments = _factory.CreateNodeArray(typeArguments, null);
        }
        var declaration = signature.Declaration;
        if (declaration is not null && declaration.Kind == SyntaxKind.JSDocSignature && declaration.Parent is not null && declaration.Parent.Kind == SyntaxKind.JSDocOverloadTag)
        {
            var text = Utilities.GetTextOfNode(declaration.Parent.Parent!, /*includeTrivia*/ true);
            var body = text.Length >= 4 ? text[2..^2] : "";
            var comment = string.Join("\n", Regex.Split(body, @"\r\n|\n|\r").Select(line => Regex.Replace(line, @"^\s+", " ")));
            EmitNodes.AddSyntheticLeadingComment(node, SyntaxKind.MultiLineCommentTrivia, comment, /*hasTrailingNewLine*/ true);
        }

        cleanup();
        return node;
    }

    // ---------------------------------------------------------------------------------------------
    // The recovery boundary (checker.ts:8070-8156)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:8070-8156 — <c>createRecoveryBoundary</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: TS returns an anonymous object whose four members close over the frame (<c>startRecoveryScope</c>,
    /// <c>finalizeBoundary</c>, <c>markError</c>, <c>hadError</c>); its one consumer is <c>expressionToTypeNode.ts</c>,
    /// which has no port yet, so the port states the object as the interface <see cref="IRecoveryBoundary"/> plus the
    /// private <see cref="RecoveryBoundary"/> class that carries what the closures captured. The coordinator's
    /// <c>SyntacticBuilderResolver.CreateRecoveryBoundary</c> boxes that instance into the generated <c>object?</c> slot
    /// (<c>NodeInterfaces.cs:3338</c>).
    /// </para>
    /// <para>
    /// PORT-NOTE: the tracker TS installs is an object literal that spreads <c>oldTracker.inner</c> and overrides six
    /// report callbacks (through <c>markError</c>) and <c>trackSymbol</c> (which records instead of forwarding) —
    /// <see cref="RecoveryTracker"/>. TS's spread of an interface has no C# form, so the six overridden members go
    /// through the boundary and every other member delegates to <c>oldTracker.inner</c> unchanged.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>cancellationToken &amp;&amp; cancellationToken.throwIfCancellationRequested</c> guards a member
    /// that may be absent; the port's <see cref="ICancellationToken"/> declares it (<c>NodeInterfaces.cs:148</c>), so the
    /// call is conditional on the token alone.
    /// </para>
    /// </remarks>
    internal IRecoveryBoundary CreateRecoveryBoundary(NodeBuilderContext context)
    {
        _cancellationToken?.ThrowIfCancellationRequested();

        var oldTracker = context.Tracker!;
        var oldTrackedSymbols = context.TrackedSymbols;
        context.TrackedSymbols = null;
        var oldEncounteredError = context.EncounteredError;

        var boundary = new RecoveryBoundary(context, oldTracker, oldTrackedSymbols, oldEncounteredError);
        context.Tracker = new SymbolTrackerImpl(context, new RecoveryTracker(oldTracker.Inner, boundary, oldTracker), oldTracker.ModuleResolverHost);
        return boundary;
    }

    // ---------------------------------------------------------------------------------------------
    // The scope a parameter list is named in (checker.ts:8158-8321)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:8158-8320 — <c>enterNewScope</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: TS's second parameter is <c>IntroducesNewScopeNode | ConditionalTypeNode | undefined</c> and is named
    /// <c>declaration</c>; the union has no C# face, so it is the <c>Node?</c> slot the contract pins and is named
    /// <c>node</c> to match every sibling caller.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>bindPattern</c> (8225-8236) and <c>bindElement</c> (8237-8243) are declared inside the
    /// <c>add</c> callback and <c>pushFakeScope</c> (8264-8310) inside the scope guard; the port keeps all three as local
    /// functions in exactly those places, which is what keeps their <c>add</c>/<c>context</c> closure (§5). TS's
    /// <c>forEach(p.elements, …)</c> and the two cleanup loops (<c>8264</c>'s <c>undo</c> at 8305-8308) are plain
    /// <c>foreach</c> loops here because their callbacks never return a truthy value.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>BindingPattern</c> is a union the port splits into <c>ObjectBindingPattern</c> and
    /// <c>ArrayBindingPattern</c> (no common interface), so <c>bindPattern</c> takes <c>Node</c> and reads the elements
    /// through a target-typed switch expression over <c>IEnumerable&lt;Node&gt;?</c> — <c>ObjectBindingPattern.elements</c>
    /// is <c>NodeArray&lt;BindingElement&gt;?</c> and covariant as <c>IEnumerable&lt;Node&gt;</c>.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>Debug.assertOptionalNode(existingFakeScope, isBlock)</c> has no port; the same assertion is
    /// <c>existingFakeScope is null || NodeTests.IsBlock(existingFakeScope)</c>.
    /// </para>
    /// </remarks>
    internal Action EnterNewScope(NodeBuilderContext context, Node? node, IList<ISymbol>? expandedParams = null, IList<ITypeParameter>? typeParameters = null, IList<ISymbol>? originalParameters = null, TypeMapper? mapper = null)
    {
        var cleanupContext = CloneNodeBuilderContext(context);
        // For regular function/method declarations, the enclosing declaration will already be signature.declaration,
        // so this is a no-op, but for arrow functions and function expressions, the enclosing declaration will be
        // the declaration that the arrow function / function expression is assigned to.
        //
        // If the parameters or return type include "typeof globalThis.paramName", using the wrong scope will lead
        // us to believe that we can emit "typeof paramName" instead, even though that would refer to the parameter,
        // not the global. Make sure we are in the right scope by changing the enclosingDeclaration to the function.
        //
        // We can't use the declaration directly; it may be in another file and so we may lose access to symbols
        // accessible to the current enclosing declaration, or gain access to symbols not accessible to the current
        // enclosing declaration. To keep this chain accurate, insert a fake scope into the chain which makes the
        // function's parameters visible.
        Action? cleanupParams = null;
        Action? cleanupTypeParams = null;
        var oldEnclosingDecl = context.EnclosingDeclaration;
        var oldMapper = context.Mapper;
        if (mapper is not null)
        {
            context.Mapper = mapper;
        }
        if (context.EnclosingDeclaration is not null && node is not null)
        {
            // As a performance optimization, reuse the same fake scope within this chain.
            // This is especially needed when we are working on an excessively deep type;
            // if we don't do this, then we spend all of our time adding more and more
            // scopes that need to be searched in isSymbolAccessible later. Since all we
            // really want to do is to mark certain names as unavailable, we can just keep
            // all of the names we're introducing in one large table and push/pop from it as
            // needed; isSymbolAccessible will walk upward and find the closest "fake" scope,
            // which will conveniently report on any and all faked scopes in the chain.
            //
            // It'd likely be better to store this somewhere else for isSymbolAccessible, but
            // since that API _only_ uses the enclosing declaration (and its parents), this is
            // seems like the best way to inject names into that search process.
            //
            // Note that we only check the most immediate enclosingDeclaration; the only place we
            // could potentially add another fake scope into the chain is right here, so we don't
            // traverse all ancestors.
            cleanupParams = !Core.Some(expandedParams) ? null : PushFakeScope(
                "params",
                add =>
                {
                    if (expandedParams is null) return;
                    for (var pIndex = 0; pIndex < expandedParams.Count; pIndex++)
                    {
                        var param = expandedParams[pIndex];
                        var originalParam = Core.ElementAt(originalParameters, pIndex);
                        if (originalParameters is not null && !ReferenceEquals(originalParam, param))
                        {
                            // Can't reference parameters that come from an expansion
                            add(param.EscapedName!, _unknownSymbol);
                            // Can't reference the original expanded parameter either
                            if (originalParam is not null)
                            {
                                add(originalParam.EscapedName!, _unknownSymbol);
                            }
                        }
                        else if (!Core.Some(param.Declarations, (IDeclaration d) =>
                        {
                            if (NodeTests.IsParameter((Node)d) && Utilities.IsBindingPattern(((ParameterDeclaration)d).Name))
                            {
                                BindPattern(((ParameterDeclaration)d).Name!);
                                return true;
                            }
                            return false;
                        }))
                        {
                            add(param.EscapedName!, param);
                        }
                    }

                    void BindPattern(Node p)
                    {
                        IEnumerable<Node>? elements = p switch
                        {
                            ObjectBindingPattern o => o.Elements,
                            ArrayBindingPattern a => a.Elements,
                            _ => null,
                        };
                        if (elements is null) return;
                        foreach (var e in elements)
                        {
                            switch (e.Kind)
                            {
                                case SyntaxKind.OmittedExpression:
                                    break;
                                case SyntaxKind.BindingElement:
                                    BindElement((BindingElement)e);
                                    break;
                                default:
                                    Debug.AssertNever(e);
                                    break;
                            }
                        }
                    }

                    void BindElement(BindingElement e)
                    {
                        if (Utilities.IsBindingPattern(e.Name))
                        {
                            BindPattern(e.Name!);
                            return;
                        }
                        var symbol = GetSymbolOfDeclaration(e);
                        add(symbol.EscapedName!, symbol);
                    }
                });

            if ((context.Flags & NodeBuilderFlags.GenerateNamesForShadowedTypeParams) != 0 && Core.Some(typeParameters))
            {
                cleanupTypeParams = PushFakeScope(
                    "typeParams",
                    add =>
                    {
                        foreach (var typeParam in typeParameters ?? Core.EmptyArray<ITypeParameter>.Value)
                        {
                            var typeParamName = TypeParameterToName(typeParam, context).EscapedText;
                            add(typeParamName!, typeParam.Symbol!);
                        }
                    });
            }

            Action? PushFakeScope(string kind, Action<Action<string, ISymbol>> addAll)
            {
                // We only ever need to look two declarations upward.
                Debug.Assert(context.EnclosingDeclaration is not null);
                Node? existingFakeScope = null;
                if (GetNodeLinks(context.EnclosingDeclaration!).FakeScopeForSignatureDeclaration == kind)
                {
                    existingFakeScope = context.EnclosingDeclaration;
                }
                else if (context.EnclosingDeclaration.Parent is not null && GetNodeLinks(context.EnclosingDeclaration.Parent).FakeScopeForSignatureDeclaration == kind)
                {
                    existingFakeScope = context.EnclosingDeclaration.Parent;
                }
                Debug.Assert(existingFakeScope is null || NodeTests.IsBlock(existingFakeScope));

                var locals = (existingFakeScope as ILocalsContainer)?.Locals ?? Utilities.CreateSymbolTable();
                List<string>? newLocals = null;
                List<(string Name, ISymbol OldSymbol)>? oldLocals = null;
                addAll((name, symbol) =>
                {
                    // Add cleanup information only if we don't own the fake scope
                    if (existingFakeScope is not null)
                    {
                        var oldSymbol = locals.GetValueOrDefault(name);
                        if (oldSymbol is null)
                        {
                            (newLocals ??= []).Add(name);
                        }
                        else
                        {
                            (oldLocals ??= []).Add((name, oldSymbol));
                        }
                    }
                    locals[name] = symbol;
                });

                if (existingFakeScope is null)
                {
                    // Use a Block for this; the type of the node doesn't matter so long as it
                    // has locals, and this is cheaper/easier than using a function-ish Node.
                    var fakeScope = _factory.CreateBlock(Core.EmptyArray<IStatement>.Value, null);
                    GetNodeLinks(fakeScope).FakeScopeForSignatureDeclaration = kind;
                    fakeScope.Locals = locals;

                    Utilities.SetParent(fakeScope, context.EnclosingDeclaration);
                    context.EnclosingDeclaration = fakeScope;
                    return null;
                }

                // We did not create the current scope, so we have to clean it up
                return () =>
                {
                    foreach (var s in newLocals ?? [])
                    {
                        locals.Remove(s);
                    }
                    foreach (var s in oldLocals ?? [])
                    {
                        locals[s.Name] = s.OldSymbol;
                    }
                };
            }
        }

        return () =>
        {
            cleanupParams?.Invoke();
            cleanupTypeParams?.Invoke();
            cleanupContext();
            context.EnclosingDeclaration = oldEnclosingDecl;
            context.Mapper = oldMapper;
        };
    }

    // ---------------------------------------------------------------------------------------------
    // `this` parameters and type parameters (checker.ts:8322-8359)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:8322-8338 — <c>tryGetThisParameterDeclaration</c>.</summary>
    internal ParameterDeclaration? TryGetThisParameterDeclaration(ISignature signature, NodeBuilderContext context)
    {
        if (signature.ThisParameter is not null)
        {
            return SymbolToParameterDeclaration(signature.ThisParameter, context);
        }
        if (signature.Declaration is not null && Utilities.IsInJSFile(signature.Declaration))
        {
            var thisTag = Utilities.GetJSDocThisTag(signature.Declaration);
            if (thisTag is not null && thisTag.TypeExpression is not null)
            {
                return _factory.CreateParameterDeclaration(
                    /*modifiers*/ null,
                    /*dotDotDotToken*/ null,
                    "this",
                    /*questionToken*/ null,
                    TypeToTypeNodeHelper(GetTypeFromTypeNode(context, thisTag.TypeExpression)!, context),
                    /*initializer*/ null);
            }
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:8340-8349 — <c>typeParameterToDeclarationWithConstraint</c>.</summary>
    internal TypeParameterDeclaration TypeParameterToDeclarationWithConstraint(ITypeParameter type, NodeBuilderContext context, ITypeNode? constraintNode)
    {
        var restoreFlags = SaveRestoreFlags(context);
        context.Flags &= ~NodeBuilderFlags.WriteTypeParametersInQualifiedName; // Avoids potential infinite loop when building for a claimspace with a generic
        var modifiers = _factory.CreateModifiersFromModifierFlags(GetTypeParameterModifiers(type));
        var name = TypeParameterToName(type, context);
        var defaultParameter = GetDefaultFromTypeParameter(type);
        var defaultParameterNode = defaultParameter is not null ? TypeToTypeNodeHelper(defaultParameter, context) : null;
        restoreFlags();
        return _factory.CreateTypeParameterDeclaration(modifiers, name, constraintNode, defaultParameterNode);
    }

    /// <summary>ported from: src/compiler/checker.ts:8351-8354 — <c>typeToTypeNodeHelperWithPossibleReusableTypeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>a &amp;&amp; b &amp;&amp; c &amp;&amp; d || e</c> returns the truthy reuse result when the whole
    /// chain holds and falls through to <c>typeToTypeNodeHelper</c> otherwise; the port spells that out as a guard plus
    /// a coalesce, which calls the resolver once instead of twice.
    /// </remarks>
    internal ITypeNode TypeToTypeNodeHelperWithPossibleReusableTypeNode(IType type, ITypeNode? typeNode, NodeBuilderContext context)
    {
        if (!CanPossiblyExpandType(type, context) && typeNode is not null && ReferenceEquals(GetTypeFromTypeNode(context, typeNode), type))
        {
            var reused = SyntacticNodeBuilder.TryReuseExistingTypeNode!(context, typeNode);
            if (reused is not null)
            {
                return reused;
            }
        }
        return TypeToTypeNodeHelper(type, context);
    }

    /// <summary>ported from: src/compiler/checker.ts:8356-8359 — <c>typeParameterToDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's third parameter defaults to the <em>call</em> <c>getConstraintOfTypeParameter(type)</c>; a C#
    /// default parameter cannot be a call, so the default is <c>null</c> and the reference's default is applied on entry.
    /// Every TS call site passes either nothing or <c>undefined</c>, so nothing distinguishes the two.
    /// </remarks>
    internal TypeParameterDeclaration TypeParameterToDeclaration(ITypeParameter type, NodeBuilderContext context, IType? constraint = null)
    {
        constraint ??= GetConstraintOfTypeParameter(type);
        var constraintNode = constraint is not null ? TypeToTypeNodeHelperWithPossibleReusableTypeNode(constraint, GetConstraintDeclaration(type), context) : null;
        return TypeParameterToDeclarationWithConstraint(type, context, constraintNode);
    }

    // ---------------------------------------------------------------------------------------------
    // Type predicates and parameter declarations (checker.ts:8361-8437)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:8361-8370 — <c>typePredicateToTypePredicateNodeHelper</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the contract's parameter type <c>ITypePredicate</c> does not exist in the port; TS's
    /// <c>TypePredicate</c> alias maps onto <c>ITypePredicateBase</c> (<c>Model/TypePredicate.cs</c>'s remarks, and the
    /// generated slot <c>ITypeChecker.TypePredicateToTypePredicateNode</c> takes exactly that face). The
    /// <c>parameterName</c> read narrows to <c>IIdentifierTypePredicate</c> — the variant the two name-carrying kinds
    /// select — because the class implements all four variant faces explicitly.
    /// </remarks>
    internal TypePredicateNode TypePredicateToTypePredicateNodeHelper(ITypePredicateBase typePredicate, NodeBuilderContext context)
    {
        KeywordToken? assertsModifier = typePredicate.Kind is TypePredicateKind.AssertsThis or TypePredicateKind.AssertsIdentifier
            ? (KeywordToken)_factory.CreateToken(SyntaxKind.AssertsKeyword)
            : null;
        object parameterName = typePredicate.Kind is TypePredicateKind.Identifier or TypePredicateKind.AssertsIdentifier
            ? EmitNodes.SetEmitFlags(_factory.CreateIdentifier(((IIdentifierTypePredicate)typePredicate).ParameterName), EmitFlags.NoAsciiEscaping)
            : _factory.CreateThisTypeNode();
        var typeNode = typePredicate.Type is not null ? TypeToTypeNodeHelper(typePredicate.Type, context) : null;
        return _factory.CreateTypePredicateNode(assertsModifier, parameterName, typeNode);
    }

    /// <summary>ported from: src/compiler/checker.ts:8372-8380 — <c>getEffectiveParameterDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS returns <c>ParameterDeclaration | JSDocParameterTag | undefined</c>; the union has no C# face, so it
    /// widens to <c>Node?</c> (§3) — the two classes share no base that carries <c>name</c>, and every reader in this file
    /// narrows through the same switch.
    /// </remarks>
    internal Node? GetEffectiveParameterDeclaration(ISymbol parameterSymbol)
    {
        var parameterDeclaration = Utilities.GetDeclarationOfKind<ParameterDeclaration>(parameterSymbol, SyntaxKind.Parameter);
        if (parameterDeclaration is not null)
        {
            return parameterDeclaration;
        }
        if (!Utilities.IsTransientSymbol(parameterSymbol))
        {
            return Utilities.GetDeclarationOfKind<JSDocParameterTag>(parameterSymbol, SyntaxKind.JSDocParameterTag);
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:8382-8404 — <c>symbolToParameterDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>map(getModifiers(parameterDeclaration), factory.cloneNode)</c> reads the modifiers of the
    /// <c>ParameterDeclaration | JSDocParameterTag</c> union; the port keeps the <c>canHaveModifiers</c> guard (the
    /// JSDoc face has none) and projects the result to the <c>List&lt;Node&gt;?</c> the factory slot takes.
    /// </remarks>
    internal ParameterDeclaration SymbolToParameterDeclaration(ISymbol parameterSymbol, NodeBuilderContext context, bool preserveModifierFlags = false)
    {
        var parameterDeclaration = GetEffectiveParameterDeclaration(parameterSymbol);

        var parameterType = GetTypeOfSymbol(parameterSymbol);
        var parameterTypeNode = SerializeTypeForDeclaration(context, (IDeclaration?)parameterDeclaration, parameterType, parameterSymbol);

        var modifiers = (context.Flags & NodeBuilderFlags.OmitParameterModifiers) == 0
            && preserveModifierFlags
            && parameterDeclaration is not null
            && Utilities.CanHaveModifiers(parameterDeclaration)
            ? Core.Map(Utilities.GetModifiers(parameterDeclaration), (modifier, _) => (Node)_factory.CloneNode(modifier))
            : null;
        var isRest = (parameterDeclaration is not null && Utilities.IsRestParameter(parameterDeclaration))
            || (GetCheckFlags(parameterSymbol) & CheckFlags.RestParameter) != 0;
        var dotDotDotToken = isRest ? (PunctuationToken)_factory.CreateToken(SyntaxKind.DotDotDotToken) : null;
        var name = ParameterToParameterDeclarationName(parameterSymbol, parameterDeclaration, context);
        var isOptional = (parameterDeclaration is not null && IsOptionalParameter(parameterDeclaration))
            || (GetCheckFlags(parameterSymbol) & CheckFlags.OptionalParameter) != 0;
        var questionToken = isOptional ? (PunctuationToken)_factory.CreateToken(SyntaxKind.QuestionToken) : null;
        var parameterNode = _factory.CreateParameterDeclaration(
            modifiers,
            dotDotDotToken,
            name,
            questionToken,
            parameterTypeNode,
            /*initializer*/ null);
        context.ApproximateLength += Utilities.SymbolName(parameterSymbol).Length + 3;
        return parameterNode;
    }

    /// <summary>ported from: src/compiler/checker.ts:8406-8436 — <c>parameterToParameterDeclarationName</c> (with the nested <c>cloneBindingName</c> at 8414-8435 and <c>elideInitializerAndSetEmitFlags</c> at 8416-8434).</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE (a contract defect): §7.5 pins this member's return as <c>string</c>, but TS returns
    /// <c>BindingName | string</c> — the matching <c>serializeNameOfParameter</c> slot is declared exactly that way
    /// (types.ts:10638) — and the only thing the checker does with the result is hand it to
    /// <c>factory.createParameterDeclaration</c>'s <c>name</c>, whose port slot is <c>object?</c>. A <c>string</c> return
    /// would compile only by stringifying every destructuring parameter name, so the port declares the <c>object?</c> face
    /// the generated <c>SerializeNameOfParameter</c> slot uses (<c>NodeInterfaces.cs:3321</c>).
    /// </para>
    /// <para>
    /// PORT-NOTE: the parameter widens to <c>Node?</c> (the <c>ParameterDeclaration | JSDocParameterTag</c> union), and
    /// <c>cloneBindingName</c>'s <c>BindingName</c> likewise widens to <c>Node</c> — the port has no <c>BindingName</c>
    /// type, and the <c>Kind</c> switch above already proved the node is a binding pattern. TS's
    /// <c>return elideInitializerAndSetEmitFlags(node) as BindingName</c> therefore returns the same node unchanged.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>visitEachChildWorker</c> is <c>visitorPublic.ts</c>'s <c>visitEachChild</c> (checker.ts:1134),
    /// landed as <c>Utilities.VisitEachChildWorker</c> with the M7 visitor slice, so this elision path walks the subtree
    /// exactly as the reference does instead of stopping at a blocker.
    /// </para>
    /// </remarks>
    internal object? ParameterToParameterDeclarationName(ISymbol parameterSymbol, Node? parameterDeclaration, NodeBuilderContext context)
    {
        var name = parameterDeclaration switch
        {
            ParameterDeclaration p => p.Name,
            JSDocPropertyLikeTag j => j.Name,
            _ => null,
        };
        return parameterDeclaration is not null && name is not null
            ? name.Kind == SyntaxKind.Identifier
                ? EmitNodes.SetEmitFlags(_factory.CloneNode((Identifier)name), EmitFlags.NoAsciiEscaping)
                : name.Kind == SyntaxKind.QualifiedName
                    ? EmitNodes.SetEmitFlags(_factory.CloneNode(((QualifiedName)name).Right), EmitFlags.NoAsciiEscaping)
                    : CloneBindingName(name)
            : Utilities.SymbolName(parameterSymbol);

        Node CloneBindingName(Node node)
        {
            return ElideInitializerAndSetEmitFlags(node)!;
        }

        Node? ElideInitializerAndSetEmitFlags(Node node)
        {
            if (context.Tracker!.CanTrackSymbol && NodeTests.IsComputedPropertyName(node) && IsLateBindableName(node))
            {
                TrackComputedName(((ComputedPropertyName)node).Expression!, context.EnclosingDeclaration, context);
            }
        // PORT-NOTE: the walker is visitorPublic.ts's visitEachChild (aliased at checker.ts:1134), landed as
        // Utilities.VisitEachChildWorker with the M7 visitor slice; `elideInitializerAndSetEmitFlags` is its token
        // visitor, which is the fifth argument checker.ts:8420 passes.
            var visited = Utilities.VisitEachChildWorker(node, ElideInitializerAndSetEmitFlags, /*context*/ null, null, ElideInitializerAndSetEmitFlags);
            if (NodeTests.IsBindingElement(visited))
            {
                var bindingElement = (BindingElement)visited;
                visited = _factory.UpdateBindingElement(
                    bindingElement,
                    bindingElement.DotDotDotToken,
                    bindingElement.PropertyName,
                    bindingElement.Name,
                    /*initializer*/ null);
            }
            if (!Utilities.NodeIsSynthesized(visited))
            {
                visited = _factory.CloneNode(visited);
            }
            return EmitNodes.SetEmitFlags(visited, EmitFlags.SingleLine | EmitFlags.NoAsciiEscaping);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The recovery boundary's object (checker.ts:8070-8156) and the tracker it installs
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:8070-8156 — the anonymous object <c>createRecoveryBoundary</c> returns.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's object has four members; the port names them as the members of this interface so that
    /// <c>SyntacticBuilderResolver.CreateRecoveryBoundary</c> (and, once it lands, <c>expressionToTypeNode.ts</c>) can
    /// consume them without a second type. <c>hadError: () => hadError</c> is the read-only <see cref="HadError"/>
    /// property.
    /// </remarks>
    private sealed class RecoveryBoundary : IRecoveryBoundary
    {
        private readonly NodeBuilderContext _context;
        private readonly SymbolTrackerImpl _oldTracker;
        private readonly List<TrackedSymbol>? _oldTrackedSymbols;
        private readonly bool _oldEncounteredError;
        private bool _hadError;

        internal RecoveryBoundary(NodeBuilderContext context, SymbolTrackerImpl oldTracker, List<TrackedSymbol>? oldTrackedSymbols, bool oldEncounteredError)
        {
            _context = context;
            _oldTracker = oldTracker;
            _oldTrackedSymbols = oldTrackedSymbols;
            _oldEncounteredError = oldEncounteredError;
        }

        /// <summary>PORT-NOTE: TS's closure variable <c>trackedSymbols</c>, shared with the tracker this boundary installs.</summary>
        internal List<TrackedSymbol>? TrackedSymbols { get; set; }

        /// <summary>PORT-NOTE: TS's closure variable <c>unreportedErrors</c>.</summary>
        internal List<Action>? UnreportedErrors { get; set; }

        /// <summary>ported from: src/compiler/checker.ts:8113 — <c>hadError</c>.</summary>
        public bool HadError => _hadError;

        /// <summary>ported from: src/compiler/checker.ts:8115-8120 — <c>markError</c>.</summary>
        public void MarkError(Action? unreportedError = null)
        {
            _hadError = true;
            if (unreportedError is not null)
            {
                (UnreportedErrors ??= []).Add(unreportedError);
            }
        }

        /// <summary>ported from: src/compiler/checker.ts:8122-8135 — <c>startRecoveryScope</c>.</summary>
        public Action StartRecoveryScope()
        {
            var trackedSymbolsTop = TrackedSymbols?.Count ?? 0;
            var unreportedErrorsTop = UnreportedErrors?.Count ?? 0;
            return () =>
            {
                _hadError = false;
                // Reset the tracked symbols to before the error
                if (TrackedSymbols is not null)
                {
                    TrackedSymbols.RemoveRange(trackedSymbolsTop, TrackedSymbols.Count - trackedSymbolsTop);
                }
                if (UnreportedErrors is not null)
                {
                    UnreportedErrors.RemoveRange(unreportedErrorsTop, UnreportedErrors.Count - unreportedErrorsTop);
                }
            };
        }

        /// <summary>ported from: src/compiler/checker.ts:8137-8155 — <c>finalizeBoundary</c>.</summary>
        public bool FinalizeBoundary()
        {
            _context.Tracker = _oldTracker;
            _context.TrackedSymbols = _oldTrackedSymbols;
            _context.EncounteredError = _oldEncounteredError;

            foreach (var fn in UnreportedErrors ?? [])
            {
                fn();
            }
            if (_hadError)
            {
                return false;
            }
            foreach (var tracked in TrackedSymbols ?? [])
            {
                _context.Tracker!.TrackSymbol(tracked.Symbol, tracked.EnclosingDeclaration, tracked.Meaning);
            }
            return true;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:8082-8107 — the tracker object literal <c>createRecoveryBoundary</c> installs.</summary>
    /// <remarks>
    /// PORT-NOTE: TS spreads <c>oldTracker.inner</c> and overrides nine members. Six report callbacks (and
    /// <c>trackSymbol</c>) defer through the boundary; the remaining four callbacks and <c>moduleResolverHost</c> are the
    /// spread base, so they delegate to the inner tracker unchanged.
    /// </remarks>
    private sealed class RecoveryTracker : ISymbolTracker
    {
        private readonly ISymbolTracker? _inner;
        private readonly RecoveryBoundary _boundary;
        private readonly ISymbolTracker _oldTracker;

        internal RecoveryTracker(ISymbolTracker? inner, RecoveryBoundary boundary, ISymbolTracker oldTracker)
        {
            _inner = inner;
            _boundary = boundary;
            _oldTracker = oldTracker;
            ModuleResolverHost = oldTracker.ModuleResolverHost;
        }

        /// <summary>ported from: src/compiler/checker.ts:8106 — the spread base's <c>moduleResolverHost</c>.</summary>
        public IModuleSpecifierResolutionHost? ModuleResolverHost { get; set; }

        /// <summary>ported from: src/compiler/checker.ts:8102-8105 — the overriding <c>trackSymbol</c>.</summary>
        public bool TrackSymbol(ISymbol? symbol, Node? enclosingDeclaration, SymbolFlags meaning)
        {
            (_boundary.TrackedSymbols ??= []).Add(new TrackedSymbol(symbol, enclosingDeclaration, meaning));
            return false;
        }

        /// <summary>ported from: src/compiler/checker.ts:8084-8086 — the overriding <c>reportCyclicStructureError</c>.</summary>
        public void ReportCyclicStructureError() => _boundary.MarkError(() => _oldTracker.ReportCyclicStructureError());

        /// <summary>ported from: src/compiler/checker.ts:8087-8089 — the overriding <c>reportInaccessibleThisError</c>.</summary>
        public void ReportInaccessibleThisError() => _boundary.MarkError(() => _oldTracker.ReportInaccessibleThisError());

        /// <summary>ported from: src/compiler/checker.ts:8090-8092 — the overriding <c>reportInaccessibleUniqueSymbolError</c>.</summary>
        public void ReportInaccessibleUniqueSymbolError() => _boundary.MarkError(() => _oldTracker.ReportInaccessibleUniqueSymbolError());

        /// <summary>ported from: src/compiler/checker.ts:8093-8095 — the overriding <c>reportLikelyUnsafeImportRequiredError</c>.</summary>
        public void ReportLikelyUnsafeImportRequiredError(string? specifier, string? symbolName) => _boundary.MarkError(() => _oldTracker.ReportLikelyUnsafeImportRequiredError(specifier, symbolName));

        /// <summary>ported from: src/compiler/checker.ts:8096-8098 — the overriding <c>reportNonSerializableProperty</c>.</summary>
        public void ReportNonSerializableProperty(string? propertyName) => _boundary.MarkError(() => _oldTracker.ReportNonSerializableProperty(propertyName));

        /// <summary>ported from: src/compiler/checker.ts:8099-8101 — the overriding <c>reportPrivateInBaseOfClassExpression</c>.</summary>
        public void ReportPrivateInBaseOfClassExpression(string? propertyName) => _boundary.MarkError(() => _oldTracker.ReportPrivateInBaseOfClassExpression(propertyName));

        /// <summary>ported from: src/compiler/checker.ts:8083 — <c>...oldTracker.inner</c>: <c>reportTruncationError</c>.</summary>
        public void ReportTruncationError() => _inner?.ReportTruncationError();

        /// <summary>ported from: src/compiler/checker.ts:8083 — <c>...oldTracker.inner</c>: <c>reportNonlocalAugmentation</c>.</summary>
        public void ReportNonlocalAugmentation(SourceFile? containingFile, ISymbol? parentSymbol, ISymbol? augmentingSymbol) => _inner?.ReportNonlocalAugmentation(containingFile, parentSymbol, augmentingSymbol);

        /// <summary>ported from: src/compiler/checker.ts:8083 — <c>...oldTracker.inner</c>: <c>reportInferenceFallback</c>.</summary>
        public void ReportInferenceFallback(Node? node) => _inner?.ReportInferenceFallback(node);

        /// <summary>ported from: src/compiler/checker.ts:8083 — <c>...oldTracker.inner</c>: <c>pushErrorFallbackNode</c>.</summary>
        public void PushErrorFallbackNode(IDeclaration? node) => _inner?.PushErrorFallbackNode(node);

        /// <summary>ported from: src/compiler/checker.ts:8083 — <c>...oldTracker.inner</c>: <c>popErrorFallbackNode</c>.</summary>
        public void PopErrorFallbackNode() => _inner?.PopErrorFallbackNode();
    }
}

/// <summary>ported from: src/compiler/checker.ts:8070-8156 — the object <c>createRecoveryBoundary</c> returns.</summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS returns an anonymous object type, which has no C# counterpart; the generated consumer slot is
/// <c>object?</c> (<c>NodeInterfaces.cs:3338</c>), so the port names the object's four members here and boxes the one
/// implementation — <c>Checker.RecoveryBoundary</c> — that carries the closures.
/// </para>
/// <para>
/// PORT-NOTE: <c>hadError: () => hadError</c> is a function in TS, so a call site would be <c>boundary.hadError()</c>;
/// the port states it as the read-only property <see cref="HadError"/> and the two consumers of
/// <c>expressionToTypeNode.ts</c> (its <c>if (recoveryBoundary.hadError())</c> sites) read it as a property.
/// </para>
/// </remarks>
internal interface IRecoveryBoundary
{
    bool FinalizeBoundary();

    Action StartRecoveryScope();

    bool HadError { get; }

    void MarkError(Action? unreportedError = null);
}

/// <summary>ported from: src/compiler/checker.ts:8003-8007 — <c>SignatureToSignatureDeclarationOptions</c>.</summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS declares this interface inside <c>createNodeBuilder</c>, so it has no generated counterpart and the port
/// declares it whole. It is a class because TS's call sites build an object literal for it — the resolver's
/// <c>signatureToSignatureDeclaration</c> (checker.ts:6487) and the four <c>AddPropertyToElementList</c> sites
/// (checker.ts:7795/7819/7837).
/// </para>
/// <para>
/// PORT-NOTE: TS's <c>name?: PropertyName</c> is a union with no C# face, so it widens to <c>Node?</c> (§3) — the same
/// face the factory slots it feeds take (<c>object? name</c>). <c>modifiers?: readonly Modifier[]</c> is a
/// <c>Modifier</c> list and stays one; <c>questionToken?: QuestionToken</c> is a single punctuation token.
/// </para>
/// </remarks>
internal sealed class SignatureToSignatureDeclarationOptions
{
    public List<ModifierToken>? Modifiers { get; set; }

    public Node? Name { get; set; }

    public PunctuationToken? QuestionToken { get; set; }
}
