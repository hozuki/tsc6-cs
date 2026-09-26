namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:7364-7976 — the object/type-reference half of createNodeBuilder
/// <summary>
/// The node builder's object-type half: the type-literal (or anonymous/mapped) node for a resolved object type, the
/// type-reference node for a class, interface or tuple reference, and the element list — properties, accessors, index
/// signatures — those are built from. TS's <c>createTypeNodeFromObjectType</c> through <c>typesAreSameReference</c>
/// (checker.ts:7364-7976).
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: every member here is a nested function of TS's <c>createNodeBuilder</c>. Three of them
/// (<c>createTypeNodeFromObjectType</c>, <c>typeReferenceToTypeNode</c>, <c>createTypeNodesFromResolvedType</c>) the
/// reference declares with no <c>context</c> parameter because they close over <c>typeToTypeNodeWorker</c>'s frame; C#
/// local-function capture cannot cross members, so the port takes the context as an added parameter and every caller
/// passes the frame's own context (§7.5). The rest already take it in the reference.
/// </para>
/// <para>
/// PORT-NOTE: the node interfaces (<see cref="ITypeNode"/>, <see cref="ITypeElement"/>, <see cref="IDeclaration"/>) are
/// interfaces while <see cref="Node"/> and the concrete node classes are classes, so every hand-off between the two
/// worlds carries the explicit cast TS's structural typing hides (docs/architecture.md §12.5; contract §3). Where the
/// generated factory widened a <c>NodeArray&lt;TypeNode&gt;</c> slot to <c>List&lt;Node&gt;</c>
/// (<c>CreateTupleTypeNode</c>) or the emit-node slot to <c>NodeArray&lt;Node&gt;</c>
/// (<c>SetIdentifierTypeArguments</c>), the projection is spelled out and PORT-NOTE'd at the call site.
/// </para>
/// <para>
/// PORT-NOTE: <c>indexInfoToObjectComputedNamesOrSignatureDeclaration</c> answers the TS union
/// <c>[IndexSignatureDeclaration] | PropertySignature[]</c> (checker.ts:7616) — the array of elements its caller spreads
/// into the element list. The port returns that array as <c>IList&lt;ITypeElement&gt;</c>, which is the exact node set
/// with no tuple fiction, and the caller does <c>AddRange</c>. §7.5 lists an <c>ITypeNode</c> return for this member,
/// which cannot represent either arm and is corrected here (see the report).
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // Object types, type references and the reference access stack (checker.ts:7364-7615)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:7364-7419 — <c>createTypeNodeFromObjectType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>context</c> is an added parameter — the reference closes over <c>typeToTypeNodeWorker</c>'s
    /// <c>context</c>.
    /// PORT-NOTE: <c>(type as MappedType).containsError</c> is JS truthiness on an optional boolean; the port spells the
    /// same test <c>== true</c>.
    /// PORT-NOTE: <c>CreateTypeLiteralNode</c>'s generated slot is <c>List&lt;ITypeElement&gt;?</c> while
    /// <c>CreateTypeNodesFromResolvedType</c> answers <c>IList&lt;ITypeElement&gt;?</c> (§7.5 pins that); the list the
    /// helper built is the same instance, so the cast is a view, not a copy.
    /// </remarks>
    internal ITypeNode CreateTypeNodeFromObjectType(ObjectType type, NodeBuilderContext context)
    {
        if (IsGenericMappedType(type) || ((MappedType)type).ContainsError == true)
        {
            return CreateMappedTypeNodeFromType((MappedType)type, context);
        }

        var resolved = ResolveStructuredTypeMembers(type);
        if (Core.Length(resolved.Properties) == 0 && Core.Length(resolved.IndexInfos) == 0)
        {
            if (Core.Length(resolved.CallSignatures) == 0 && Core.Length(resolved.ConstructSignatures) == 0)
            {
                context.ApproximateLength += 2;
                return EmitNodes.SetEmitFlags(_factory.CreateTypeLiteralNode(null), EmitFlags.SingleLine);
            }

            if (Core.Length(resolved.CallSignatures) == 1 && Core.Length(resolved.ConstructSignatures) == 0)
            {
                var signature = resolved.CallSignatures![0];
                var signatureNode = (FunctionTypeNode)SignatureToSignatureDeclarationHelper(signature, SyntaxKind.FunctionType, context);
                return signatureNode;
            }

            if (Core.Length(resolved.ConstructSignatures) == 1 && Core.Length(resolved.CallSignatures) == 0)
            {
                var signature = resolved.ConstructSignatures![0];
                var signatureNode = (ConstructorTypeNode)SignatureToSignatureDeclarationHelper(signature, SyntaxKind.ConstructorType, context);
                return signatureNode;
            }
        }

        var abstractSignatures = Core.Filter(resolved.ConstructSignatures, signature => (signature.Flags & SignatureFlags.Abstract) != 0);
        if (Core.Some(abstractSignatures))
        {
            var types = Core.Map<ISignature, IType>(abstractSignatures, (signature, _) => GetOrCreateTypeFromSignature(signature))!;
            // count the number of type elements excluding abstract constructors
            var typeElementCount = Core.Length(resolved.CallSignatures)
                + (Core.Length(resolved.ConstructSignatures) - Core.Length(abstractSignatures))
                + Core.Length(resolved.IndexInfos)
                // exclude `prototype` when writing a class expression as a type literal, as per
                // the logic in `createTypeNodesFromResolvedType`.
                + ((context.Flags & NodeBuilderFlags.WriteClassExpressionAsTypeLiteral) != 0
                    ? Core.CountWhere(resolved.Properties, (ISymbol p, int _) => (p.Flags & SymbolFlags.Prototype) == 0)
                    : Core.Length(resolved.Properties));
            // don't include an empty object literal if there were no other static-side
            // properties to write, i.e. `abstract class C { }` becomes `abstract new () => {}`
            // and not `(abstract new () => {}) & {}`
            if (typeElementCount != 0)
            {
                // create a copy of the object type without any abstract construct signatures.
                types.Add(GetResolvedTypeWithoutAbstractConstructSignatures((ResolvedType)resolved));
            }
            return TypeToTypeNodeHelper(GetIntersectionType(types), context);
        }

        var restoreFlags = SaveRestoreFlags(context);
        context.Flags |= NodeBuilderFlags.InObjectTypeLiteral;
        var members = CreateTypeNodesFromResolvedType((ResolvedType)resolved, context);
        restoreFlags();
        var typeLiteralNode = _factory.CreateTypeLiteralNode((List<ITypeElement>?)members);
        context.ApproximateLength += 2;
        EmitNodes.SetEmitFlags(typeLiteralNode, (context.Flags & NodeBuilderFlags.MultilineObjectLiterals) != 0 ? EmitFlags.None : EmitFlags.SingleLine);
        return typeLiteralNode;
    }

    /// <summary>ported from: src/compiler/checker.ts:7420-7544 — <c>typeReferenceToTypeNode</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: <c>context</c> is an added parameter — the reference closes over <c>typeToTypeNodeWorker</c>'s
    /// <c>context</c>.
    /// </para>
    /// <para>
    /// PORT-NOTE: the reference's <c>return undefined! as TypeNode</c> at the end of the empty-tuple arm is kept as
    /// <c>return null!</c> with the reference's own TODO: the arm runs only after <c>context.encounteredError</c> was
    /// already true or the caller allowed empty tuples, exactly as upstream.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>sameMap</c> mutates its input and hands it back when no element changed, otherwise a fresh list —
    /// both are <c>List&lt;IType&gt;</c> at run time, so the port keeps the reference's mutating assignment.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>rangeEquals(outerTypeParameters, typeArguments, start, i)</c> compares two lists of different
    /// element types (<c>ITypeParameter</c> vs <c>IType</c>), which the invariant <c>Core.RangeEquals&lt;T&gt;</c> cannot
    /// express; the port keeps the same element-wise loop with the same <c>Core.EquateValues</c> the ported
    /// <c>RangeEquals</c> (core.ts:1050-1058) uses.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>createTupleTypeNode</c>'s generated slot is <c>List&lt;Node&gt;?</c> (the generator widened
    /// <c>TypeNode[]</c> to <c>Node[]</c>), so the tuple elements — <c>ITypeNode</c>-typed, and always concrete node
    /// classes — are projected into the <c>Node</c>-typed array the slot accepts. The reference hands its own array
    /// straight on.
    /// </para>
    /// <para>
    /// PORT-NOTE: C# forbids two locals of one name in nested scopes (CS0136), so the reference's two
    /// <c>restoreFlags</c> locals are <c>restoreFlags</c> (the outer-type-argument group) and <c>restoreFinalFlags</c>
    /// (the final reference); both are <c>saveRestoreFlags</c>/<c>SaveRestoreFlags</c>.
    /// </para>
    /// </remarks>
    internal ITypeNode TypeReferenceToTypeNode(TypeReference type, NodeBuilderContext context)
    {
        var typeArguments = GetTypeArguments(type);
        if (ReferenceEquals(type.Target, _globalArrayType) || ReferenceEquals(type.Target, _globalReadonlyArrayType))
        {
            if ((context.Flags & NodeBuilderFlags.WriteArrayAsGenericType) != 0)
            {
                var typeArgumentNode = TypeToTypeNodeHelper(typeArguments[0], context);
                return _factory.CreateTypeReferenceNode(ReferenceEquals(type.Target, _globalArrayType) ? "Array" : "ReadonlyArray", [typeArgumentNode]);
            }
            var elementType = TypeToTypeNodeHelper(typeArguments[0], context);
            var arrayType = _factory.CreateArrayTypeNode(elementType);
            return ReferenceEquals(type.Target, _globalArrayType) ? arrayType : _factory.CreateTypeOperatorNode(SyntaxKind.ReadonlyKeyword, arrayType);
        }
        else if ((type.Target!.ObjectFlags & ObjectFlags.Tuple) != 0)
        {
            typeArguments = (List<IType>)Core.SameMap(typeArguments, (t, i) => RemoveMissingType(t, (((ITupleType)type.Target!).ElementFlags![i] & ElementFlags.Optional) != 0))!;
            if (typeArguments.Count > 0)
            {
                var arity = GetTypeReferenceArity(type);
                var tupleConstituentNodes = MapToTypeNodes(Core.Slice(typeArguments, 0, arity), context);
                if (tupleConstituentNodes is not null)
                {
                    var labeledElementDeclarations = ((ITupleType)type.Target!).LabeledElementDeclarations;
                    for (var i = 0; i < tupleConstituentNodes.Count; i++)
                    {
                        var flags = ((ITupleType)type.Target!).ElementFlags![i];
                        var labeledElementDeclaration = labeledElementDeclarations is not null && i < labeledElementDeclarations.Count ? labeledElementDeclarations[i] : null;

                        if (labeledElementDeclaration is not null)
                        {
                            tupleConstituentNodes[i] = _factory.CreateNamedTupleMember(
                                (flags & ElementFlags.Variable) != 0 ? (PunctuationToken)_factory.CreateToken(SyntaxKind.DotDotDotToken) : null,
                                _factory.CreateIdentifier(Utilities.UnescapeLeadingUnderscores(GetTupleElementLabel(labeledElementDeclaration))),
                                (flags & ElementFlags.Optional) != 0 ? (PunctuationToken)_factory.CreateToken(SyntaxKind.QuestionToken) : null,
                                (flags & ElementFlags.Rest) != 0 ? _factory.CreateArrayTypeNode(tupleConstituentNodes[i]) :
                                    tupleConstituentNodes[i]);
                        }
                        else
                        {
                            tupleConstituentNodes[i] = (flags & ElementFlags.Variable) != 0 ? _factory.CreateRestTypeNode((flags & ElementFlags.Rest) != 0 ? _factory.CreateArrayTypeNode(tupleConstituentNodes[i]) : tupleConstituentNodes[i]) :
                                (flags & ElementFlags.Optional) != 0 ? _factory.CreateOptionalTypeNode(tupleConstituentNodes[i]) :
                                tupleConstituentNodes[i];
                        }
                    }
                    var tupleTypeNode = EmitNodes.SetEmitFlags(_factory.CreateTupleTypeNode(AsNodeElements(tupleConstituentNodes)), EmitFlags.SingleLine);
                    return ((ITupleType)type.Target!).Readonly ? _factory.CreateTypeOperatorNode(SyntaxKind.ReadonlyKeyword, tupleTypeNode) : tupleTypeNode;
                }
            }
            if (context.EncounteredError || (context.Flags & NodeBuilderFlags.AllowEmptyTuple) != 0)
            {
                var tupleTypeNode = EmitNodes.SetEmitFlags(_factory.CreateTupleTypeNode([]), EmitFlags.SingleLine);
                return ((ITupleType)type.Target!).Readonly ? _factory.CreateTypeOperatorNode(SyntaxKind.ReadonlyKeyword, tupleTypeNode) : tupleTypeNode;
            }
            context.EncounteredError = true;
            return null!; // TODO: GH#18217
        }
        else if (
            (context.Flags & NodeBuilderFlags.WriteClassExpressionAsTypeLiteral) != 0 &&
            type.Symbol!.ValueDeclaration is not null &&
            Utilities.IsClassLike((Node)type.Symbol!.ValueDeclaration) &&
            !IsValueSymbolAccessible(type.Symbol!, context.EnclosingDeclaration))
        {
            return CreateAnonymousTypeNode(type, context);
        }
        else
        {
            var outerTypeParameters = type.Target!.OuterTypeParameters;
            var i = 0;
            Node? resultType = null;
            if (outerTypeParameters is not null)
            {
                var length = outerTypeParameters.Count;
                while (i < length)
                {
                    // Find group of type arguments for type parameters with the same declaring container.
                    var start = i;
                    var parent = GetParentSymbolOfTypeParameter(outerTypeParameters[i])!;
                    do
                    {
                        i++;
                    }
                    while (i < length && ReferenceEquals(GetParentSymbolOfTypeParameter(outerTypeParameters[i]), parent));

                    // When type parameters are their own type arguments for the whole group (i.e. we have
                    // the default outer type arguments), we don't show the group.
                    var usesDefaultOuterTypeArguments = true;
                    for (var k = start; k < i; k++)
                    {
                        if (!Core.EquateValues<object?>(outerTypeParameters[k], typeArguments[k]))
                        {
                            usesDefaultOuterTypeArguments = false;
                            break;
                        }
                    }
                    if (!usesDefaultOuterTypeArguments)
                    {
                        var typeArgumentSlice = MapToTypeNodes(Core.Slice(typeArguments, start, i), context);
                        var restoreFlags = SaveRestoreFlags(context);
                        context.Flags |= NodeBuilderFlags.ForbidIndexedAccessSymbolReferences;
                        var refNode = SymbolToTypeNode(parent, context, SymbolFlags.Type, typeArgumentSlice);
                        restoreFlags();
                        resultType = resultType is null ? (Node)refNode : (Node)AppendReferenceToType(resultType, (TypeReferenceNode)refNode);
                    }
                }
            }
            List<ITypeNode>? typeArgumentNodes = null;
            if (typeArguments.Count > 0)
            {
                var typeParameterCount = 0;
                var targetTypeParameters = type.Target!.TypeParameters;
                if (targetTypeParameters is not null)
                {
                    typeParameterCount = Math.Min(targetTypeParameters.Count, typeArguments.Count);

                    // Maybe we should do this for more types, but for now we only elide type arguments that are
                    // identical to their associated type parameters' defaults for `Iterable`, `IterableIterator`,
                    // `AsyncIterable`, and `AsyncIterableIterator` to provide backwards-compatible .d.ts emit due
                    // to each now having three type parameters instead of only one.
                    if (
                        IsReferenceToType(type, GetGlobalIterableType(/*reportErrors*/ false)) ||
                        IsReferenceToType(type, GetGlobalIterableIteratorType(/*reportErrors*/ false)) ||
                        IsReferenceToType(type, GetGlobalAsyncIterableType(/*reportErrors*/ false)) ||
                        IsReferenceToType(type, GetGlobalAsyncIterableIteratorType(/*reportErrors*/ false))
                    )
                    {
                        if (
                            type.Node is null || !NodeTests.IsTypeReferenceNode(type.Node) || ((TypeReferenceNode)type.Node).TypeArguments is null ||
                            ((TypeReferenceNode)type.Node).TypeArguments!.Count < typeParameterCount
                        )
                        {
                            while (typeParameterCount > 0)
                            {
                                var typeArgument = typeArguments[typeParameterCount - 1];
                                var typeParameter = targetTypeParameters[typeParameterCount - 1];
                                var defaultType = GetDefaultFromTypeParameter(typeParameter);
                                if (defaultType is null || !IsTypeIdenticalTo(typeArgument, defaultType))
                                {
                                    break;
                                }
                                typeParameterCount--;
                            }
                        }
                    }
                }

                typeArgumentNodes = MapToTypeNodes(Core.Slice(typeArguments, i, typeParameterCount), context);
            }
            var restoreFinalFlags = SaveRestoreFlags(context);
            context.Flags |= NodeBuilderFlags.ForbidIndexedAccessSymbolReferences;
            var finalRef = SymbolToTypeNode(type.Symbol!, context, SymbolFlags.Type, typeArgumentNodes);
            restoreFinalFlags();
            return resultType is null ? finalRef : AppendReferenceToType(resultType, (TypeReferenceNode)finalRef);
        }

        // PORT-NOTE: the projection <c>createTupleTypeNode</c>'s widened slot needs (see the method remarks). A local
        // function, because the reference has no member for it and this is its only user.
        static NodeArray<Node> AsNodeElements(List<ITypeNode> elements)
        {
            var result = new NodeArray<Node>(elements.Count);
            foreach (var element in elements)
            {
                result.Add((Node)element);
            }

            return result;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:7545-7604 — <c>appendReferenceToType</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: TS returns <c>TypeReferenceNode | ImportTypeNode</c>, both <see cref="ITypeNode"/> implementations, so
    /// the port declares <c>ITypeNode</c> (the caller keeps the same <c>isImportTypeNode</c> split internally).
    /// </para>
    /// <para>
    /// PORT-NOTE: the emit-node slot for an identifier's type arguments is <c>NodeArray&lt;Node&gt;</c>
    /// (EmitNodes.cs:387) while the reference's array is a <c>NodeArray&lt;TypeNode&gt;</c>, so the array handed to
    /// <c>setIdentifierTypeArguments</c> is the same elements in a <c>Node</c>-typed array; the identity test that guards
    /// the clone still compares the reference's own array, which means a repeat visit can re-clone where upstream would
    /// not — the cloned node is identical either way.
    /// </para>
    /// </remarks>
    internal ITypeNode AppendReferenceToType(Node root, TypeReferenceNode reference)
    {
        if (NodeTests.IsImportTypeNode(root))
        {
            var importRoot = (ImportTypeNode)root;
            // first shift type arguments
            var typeArguments = importRoot.TypeArguments;
            Node? qualifier = importRoot.Qualifier;
            if (qualifier is not null)
            {
                if (NodeTests.IsIdentifier(qualifier))
                {
                    if (!ReferenceEquals(typeArguments, EmitNodes.GetIdentifierTypeArguments(qualifier)))
                    {
                        qualifier = EmitNodes.SetIdentifierTypeArguments(_factory.CloneNode((Identifier)qualifier), AsNodeTypeArguments(typeArguments));
                    }
                }
                else
                {
                    if (!ReferenceEquals(typeArguments, EmitNodes.GetIdentifierTypeArguments(((QualifiedName)qualifier).Right!)))
                    {
                        qualifier = _factory.UpdateQualifiedName(
                            (QualifiedName)qualifier,
                            ((QualifiedName)qualifier).Left,
                            EmitNodes.SetIdentifierTypeArguments(_factory.CloneNode(((QualifiedName)qualifier).Right!), AsNodeTypeArguments(typeArguments)));
                    }
                }
            }
            typeArguments = reference.TypeArguments;
            // then move qualifiers
            var ids = GetAccessStack(reference);
            foreach (var id in ids)
            {
                qualifier = qualifier is not null ? _factory.CreateQualifiedName(qualifier, id) : id;
            }
            return _factory.UpdateImportTypeNode(
                importRoot,
                importRoot.Argument,
                importRoot.Attributes,
                qualifier,
                typeArguments,
                importRoot.IsTypeOf);
        }
        else
        {
            var rootReference = (TypeReferenceNode)root;
            // first shift type arguments
            var typeArguments = rootReference.TypeArguments;
            var typeName = rootReference.TypeName;
            if (NodeTests.IsIdentifier(typeName!))
            {
                if (!ReferenceEquals(typeArguments, EmitNodes.GetIdentifierTypeArguments(typeName!)))
                {
                    typeName = EmitNodes.SetIdentifierTypeArguments(_factory.CloneNode((Identifier)typeName!), AsNodeTypeArguments(typeArguments));
                }
            }
            else
            {
                var qualifiedName = (QualifiedName)typeName!;
                if (!ReferenceEquals(typeArguments, EmitNodes.GetIdentifierTypeArguments(qualifiedName.Right!)))
                {
                    typeName = _factory.UpdateQualifiedName(qualifiedName, qualifiedName.Left, EmitNodes.SetIdentifierTypeArguments(_factory.CloneNode(qualifiedName.Right!), AsNodeTypeArguments(typeArguments)));
                }
            }
            typeArguments = reference.TypeArguments;
            // then move qualifiers
            var ids = GetAccessStack(reference);
            foreach (var id in ids)
            {
                typeName = _factory.CreateQualifiedName(typeName, id);
            }
            return _factory.UpdateTypeReferenceNode(
                rootReference,
                typeName,
                typeArguments);
        }

        // PORT-NOTE: the projection the reference does not need — see the method remarks. A local function, because the
        // reference has no member for it and both branches above are the only users.
        NodeArray<Node>? AsNodeTypeArguments(NodeArray<ITypeNode>? arguments)
        {
            if (arguments is null)
            {
                return null;
            }

            var result = new NodeArray<Node>(arguments.Count);
            foreach (var argument in arguments)
            {
                result.Add((Node)argument);
            }

            return result;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:7605-7615 — <c>getAccessStack</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>state</c> starts as <c>ref.typeName</c> (an <c>EntityName</c>, which has no port
    /// type) and is narrowed to <c>Identifier</c>/<c>QualifiedName</c> by the loop's guard; the port holds it as
    /// <see cref="Node"/> and casts where the guard has proven the shape. <c>ids.unshift</c> is
    /// <c>List.Insert(0, …)</c>, the same prepend.
    /// </remarks>
    internal List<Identifier> GetAccessStack(TypeReferenceNode reference)
    {
        var state = reference.TypeName!;
        var ids = new List<Identifier>();
        while (!NodeTests.IsIdentifier(state))
        {
            ids.Insert(0, ((QualifiedName)state).Right!);
            state = ((QualifiedName)state).Left!;
        }
        ids.Insert(0, (Identifier)state);
        return ids;
    }

    // ---------------------------------------------------------------------------------------------
    // The element list (checker.ts:7616-7895)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:7616-7646 — <c>indexInfoToObjectComputedNamesOrSignatureDeclaration</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the reference answers <c>[IndexSignatureDeclaration] | PropertySignature[]</c> — the array its caller
    /// spreads into the element list — so the port answers <c>IList&lt;ITypeElement&gt;</c>, the same nodes in one list;
    /// §7.5's <c>ITypeNode</c> return for this member cannot represent either arm and is corrected here.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>IndexInfo.components</c> is the intersection
    /// <c>(ClassElement | ObjectLiteralElement) &amp; { name: ComputedPropertyName }</c> (types.ts:7065), which the port's
    /// model carries as <c>List&lt;Node&gt;</c>; <c>name</c> is read through <see cref="INamedDeclaration"/> and
    /// <c>symbol</c> through <see cref="IDeclaration"/>, which every element class implements.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>(isPropertySignature(e) || … ) &amp;&amp; e.questionToken</c> is a guard over six declaration kinds
    /// followed by a token read. The port reads each kind's own <c>QuestionToken</c>, because
    /// <c>PropertyDeclaration</c>/<c>MethodDeclaration</c> are class elements and not <see cref="ITypeElement"/>s in the
    /// generated model (so the interface view is not available for them). The test is the same conjunction.
    /// PORT-NOTE: <c>typeNode || typeToTypeNodeHelper(…)</c> is a truthiness test on a node, i.e. <c>??</c>.
    /// </para>
    /// </remarks>
    internal IList<ITypeElement> IndexInfoToObjectComputedNamesOrSignatureDeclaration(IndexInfo indexInfo, NodeBuilderContext context, ITypeNode? typeNode)
    {
        if (indexInfo.Components is not null)
        {
            // Index info is derived from object or class computed property names (plus explicit named members) - we can clone those instead of writing out the result computed index signature
            var allComponentComputedNamesSerializable = Core.Every(indexInfo.Components, (e, _) =>
            {
                var name = ((INamedDeclaration)e).Name;
                return name is not null && NodeTests.IsComputedPropertyName(name) && Utilities.IsEntityNameExpression(((ComputedPropertyName)name).Expression!) && context.EnclosingDeclaration is not null && IsEntityNameVisible(((ComputedPropertyName)name).Expression!, context.EnclosingDeclaration, /*shouldComputeAliasToMakeVisible*/ false)?.Accessibility == SymbolAccessibility.Accessible;
            });
            if (allComponentComputedNamesSerializable)
            {
                // Only use computed name serialization form if all components are visible and take the `a.b.c` form
                var newComponents = Core.Filter(indexInfo.Components, e => !HasLateBindableName((IDeclaration)e));
                return Core.Map(newComponents, (Node e, int _) =>
                {
                    // Still need to track visibility even if we've already checked it to paint references as used
                    TrackComputedName(((ComputedPropertyName)((INamedDeclaration)e).Name!).Expression!, context.EnclosingDeclaration, context);
                    return (ITypeElement)SetTextRange(
                        context,
                        _factory.CreatePropertySignature(
                            indexInfo.IsReadonly ? [_factory.CreateModifier(SyntaxKind.ReadonlyKeyword)] : null,
                            ((INamedDeclaration)e).Name,
                            HasQuestionToken(e) ? (PunctuationToken)_factory.CreateToken(SyntaxKind.QuestionToken) : null,
                            typeNode ?? TypeToTypeNodeHelper(GetTypeOfSymbol(((IDeclaration)e).Symbol!), context)),
                        e);
                })!;
            }
        }
        return [IndexInfoToIndexSignatureDeclarationHelper(indexInfo, context, typeNode)];

        // PORT-NOTE: see the remarks; the six-kind guard of the reference, per concrete class.
        static bool HasQuestionToken(Node element) =>
            (NodeTests.IsPropertySignature(element) && ((PropertySignature)element).QuestionToken is not null)
            || (NodeTests.IsPropertyDeclaration(element) && ((PropertyDeclaration)element).QuestionToken is not null)
            || (NodeTests.IsMethodSignature(element) && ((MethodSignature)element).QuestionToken is not null)
            || (NodeTests.IsMethodDeclaration(element) && ((MethodDeclaration)element).QuestionToken is not null)
            || (NodeTests.IsGetAccessorDeclaration(element) && ((GetAccessorDeclaration)element).QuestionToken is not null)
            || (NodeTests.IsSetAccessorDeclaration(element) && ((SetAccessorDeclaration)element).QuestionToken is not null);
    }

    /// <summary>ported from: src/compiler/checker.ts:7647-7709 — <c>createTypeNodesFromResolvedType</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: <c>context</c> is an added parameter — the reference closes over <c>typeToTypeNodeWorker</c>'s
    /// <c>context</c>.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>context.tracker.reportPrivateInBaseOfClassExpression</c> is guarded in the reference by the
    /// callback's own truthiness, which is always true here: the node builder's tracker is a
    /// <see cref="SymbolTrackerImpl"/>, whose member always exists and no-ops when there is no presenter
    /// (SymbolTrackerImpl.cs). The port calls it directly.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>addSyntheticTrailingComment</c>'s constraint is the <see cref="Node"/> class while the element is
    /// <see cref="ITypeElement"/>-typed (the same object), so the call is viewed twice; <c>typeElements.pop()</c> is
    /// <c>Core.Last</c> plus the same removal.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>resolvedType.indexInfos</c> is the model's <c>List&lt;IIndexInfo&gt;</c> slot, while the helper takes
    /// the <see cref="IndexInfo"/> class (§7.5), so each element is cast to the class the model really holds.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>context.typeStack.push(-1)</c>/<c>…pop()</c> are JS <c>Array</c> methods; the port's slot is
    /// <c>List&lt;int&gt;</c>, so they are <c>Add(-1)</c> and <c>RemoveAt(Count - 1)</c> — the same push/pop the
    /// coordinator's <c>SymbolTableToDeclarationStatements</c> uses (Checker.NodeBuilder.cs:342-347) and the same
    /// mutation.
    /// </para>
    /// </remarks>
    internal IList<ITypeElement>? CreateTypeNodesFromResolvedType(ResolvedType resolvedType, NodeBuilderContext context)
    {
        if (CheckTruncationLength(context))
        {
            context.Out!.Truncated = true;
            if ((context.Flags & NodeBuilderFlags.NoTruncation) != 0)
            {
                return [(ITypeElement)EmitNodes.AddSyntheticTrailingComment((Node)_factory.CreateNotEmittedTypeElement(), SyntaxKind.MultiLineCommentTrivia, "elided")];
            }
            return [_factory.CreatePropertySignature(/*modifiers*/ null, "...", /*questionToken*/ null, /*type*/ null)];
        }
        context.TypeStack!.Add(-1);
        var typeElements = new List<ITypeElement>();
        foreach (var signature in resolvedType.CallSignatures!)
        {
            typeElements.Add((CallSignatureDeclaration)SignatureToSignatureDeclarationHelper(signature, SyntaxKind.CallSignature, context));
        }
        foreach (var signature in resolvedType.ConstructSignatures!)
        {
            if ((signature.Flags & SignatureFlags.Abstract) != 0)
            {
                continue;
            }
            typeElements.Add((ConstructSignatureDeclaration)SignatureToSignatureDeclarationHelper(signature, SyntaxKind.ConstructSignature, context));
        }
        foreach (var info in resolvedType.IndexInfos!)
        {
            typeElements.AddRange(IndexInfoToObjectComputedNamesOrSignatureDeclaration((IndexInfo)info, context, (resolvedType.ObjectFlags & ObjectFlags.ReverseMapped) != 0 ? CreateElidedInformationPlaceholder(context) : null));
        }

        var properties = resolvedType.Properties;
        if (properties is null)
        {
            context.TypeStack!.RemoveAt(context.TypeStack!.Count - 1);
            return typeElements;
        }

        var i = 0;
        foreach (var propertySymbol in properties)
        {
            if (IsExpanding(context) && (propertySymbol.Flags & SymbolFlags.Prototype) != 0)
            {
                continue;
            }
            i++;
            if ((context.Flags & NodeBuilderFlags.WriteClassExpressionAsTypeLiteral) != 0)
            {
                if ((propertySymbol.Flags & SymbolFlags.Prototype) != 0)
                {
                    continue;
                }
                if ((Utilities.GetDeclarationModifierFlagsFromSymbol(propertySymbol) & (ModifierFlags.Private | ModifierFlags.Protected)) != 0)
                {
                    context.Tracker!.ReportPrivateInBaseOfClassExpression(Utilities.UnescapeLeadingUnderscores(propertySymbol.EscapedName!));
                }
                if (Utilities.IsPrivateIdentifierSymbol(propertySymbol))
                {
                    context.Tracker!.ReportPrivateInBaseOfClassExpression(Utilities.IdText((Node)((INamedDeclaration)propertySymbol.ValueDeclaration!).Name!));
                }
            }
            if (CheckTruncationLength(context) && i + 2 < properties.Count - 1)
            {
                context.Out!.Truncated = true;
                if ((context.Flags & NodeBuilderFlags.NoTruncation) != 0)
                {
                    var typeElement = Core.Last(typeElements);
                    typeElements.RemoveAt(typeElements.Count - 1);
                    typeElements.Add((ITypeElement)EmitNodes.AddSyntheticTrailingComment((Node)typeElement, SyntaxKind.MultiLineCommentTrivia, $"... {properties.Count - i} more elided ..."));
                }
                else
                {
                    typeElements.Add(_factory.CreatePropertySignature(/*modifiers*/ null, $"... {properties.Count - i} more ...", /*questionToken*/ null, /*type*/ null));
                }
                AddPropertyToElementList(properties[properties.Count - 1], context, typeElements);
                break;
            }
            AddPropertyToElementList(propertySymbol, context, typeElements);
        }
        context.TypeStack!.RemoveAt(context.TypeStack!.Count - 1);
        return typeElements.Count != 0 ? typeElements : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:7710-7717 — <c>createElidedInformationPlaceholder</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>addSyntheticLeadingComment</c> takes a <see cref="Node"/> while the factory answers
    /// <see cref="ITypeNode"/>; the token it builds is a <c>KeywordTypeToken</c>, which implements both
    /// (NodeFactory.cs's <c>CreateKeywordTypeNode</c> casts the same token to <c>ITypeNode</c>).
    /// </remarks>
    internal ITypeNode CreateElidedInformationPlaceholder(NodeBuilderContext context)
    {
        context.ApproximateLength += 3;
        if ((context.Flags & NodeBuilderFlags.NoTruncation) == 0)
        {
            return _factory.CreateTypeReferenceNode(_factory.CreateIdentifier("..."), /*typeArguments*/ null);
        }
        return (ITypeNode)EmitNodes.AddSyntheticLeadingComment((Node)_factory.CreateKeywordTypeNode(SyntaxKind.AnyKeyword), SyntaxKind.MultiLineCommentTrivia, "elided");
    }

    /// <summary>ported from: src/compiler/checker.ts:7718-7752 — <c>shouldUsePlaceholderForProperty</c> (with its nested <c>isDeeplyNestedReverseMappedTypeProperty</c>, 7739-7752).</summary>
    /// <remarks>
    /// Use placeholders for reverse mapped types we've either
    /// (1) already descended into, or
    /// (2) are nested reverse mappings within a mapping over a non-anonymous type, or
    /// (3) are deeply nested properties that originate from the same mapped type.
    /// Condition (2) is a restriction mostly just to
    /// reduce the blowup in printback size from doing, eg, a deep reverse mapping over `Window`.
    /// Since anonymous types usually come from expressions, this allows us to preserve the output
    /// for deep mappings which likely come from expressions, while truncating those parts which
    /// come from mappings over library functions.
    /// Condition (3) limits printing of possibly infinitely deep reverse mapped types.
    /// <para>
    /// PORT-NOTE: the reference's nested <c>isDeeplyNestedReverseMappedTypeProperty</c> (checker.ts:7739-7752) is a C#
    /// local function — it closes over <c>context</c>, <c>depth</c> and <c>propertySymbol</c>, all frame state.
    /// <c>symbol.links.propertyType</c>/<c>symbol.links.mappedType</c> are the reverse-mapped face of the one links class
    /// (Model/SymbolLinks.cs), read through <see cref="IReverseMappedSymbolLinks"/> as the landed checker code does.
    /// <c>contains</c>/<c>context.reverseMappedStack?.[0]</c> are <c>Core.Contains</c>/<c>Core.ElementAt</c>.
    /// </para>
    /// </remarks>
    internal bool ShouldUsePlaceholderForProperty(ISymbol propertySymbol, NodeBuilderContext context)
    {
        var depth = 3;
        return (GetCheckFlags(propertySymbol) & CheckFlags.ReverseMapped) != 0
            && (
                Core.Contains(context.ReverseMappedStack, (IReverseMappedSymbol)propertySymbol)
                || (
                    Core.ElementAt(context.ReverseMappedStack, 0) is not null
                    && (GetObjectFlags(((IReverseMappedSymbolLinks)GetSymbolLinks(Core.Last(context.ReverseMappedStack!))).PropertyType!) & ObjectFlags.Anonymous) == 0
                )
                || IsDeeplyNestedReverseMappedTypeProperty()
            );

        bool IsDeeplyNestedReverseMappedTypeProperty()
        {
            if (Core.Length(context.ReverseMappedStack) < depth)
            {
                return false;
            }
            for (var i = 0; i < depth; i++)
            {
                var prop = context.ReverseMappedStack![context.ReverseMappedStack!.Count - 1 - i];
                if (!ReferenceEquals(((IReverseMappedSymbolLinks)GetSymbolLinks(prop)).MappedType!.Symbol, ((IReverseMappedSymbolLinks)GetSymbolLinks(propertySymbol)).MappedType!.Symbol))
                {
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:7753-7887 — <c>addPropertyToElementList</c> (with its nested <c>preserveCommentsOn</c>, 7872-7887).</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: <c>typeElements</c> is <c>List&lt;ITypeElement&gt;</c> — the list <c>CreateTypeNodesFromResolvedType</c>
    /// fills; the reference's <c>TypeElement[]</c> is the same array.
    /// </para>
    /// <para>
    /// PORT-NOTE: the reference's nested <c>preserveCommentsOn</c> (checker.ts:7872-7887) is a C# local function: it
    /// closes over <c>propertySymbol</c> and the frame's <c>context</c>. Its <c>propertySymbol.declarations?.find(…)</c>
    /// guard is <c>Core.Find</c> with the kind test, and <c>setCommentRange</c> is this class's own
    /// <see cref="SetCommentRange"/>.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>context.enclosingDeclaration</c> is a <see cref="Node"/> slot while a declaration is
    /// <see cref="IDeclaration"/>; <c>valueDeclaration || declarations?.[0] || saveEnclosingDeclaration</c> is a
    /// null-coalescing chain over the three, with the declarations' element taken by <c>Core.ElementAt</c>.
    /// <c>context.tracker.reportNonSerializableProperty</c> comes from the builder's
    /// <see cref="SymbolTrackerImpl"/>, so it is called directly.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>factory.createToken(ReadonlyKeyword)</c> is <c>CreateModifier</c> here: the port's modifier slots are
    /// <c>List&lt;ModifierToken&gt;</c> (NodeFactory.cs:275 documents that the token class follows the kind, so
    /// <c>createToken</c> would answer the same <c>ModifierToken</c>). The reference's <c>propertyType ?</c> test is a
    /// truthiness test on a type object, which is always true, so only that arm is kept; its <c>any</c> fallback lives
    /// inside the callee.
    /// </para>
    /// </remarks>
    internal void AddPropertyToElementList(ISymbol propertySymbol, NodeBuilderContext context, List<ITypeElement> typeElements)
    {
        var propertyIsReverseMapped = (GetCheckFlags(propertySymbol) & CheckFlags.ReverseMapped) != 0;
        var propertyType = ShouldUsePlaceholderForProperty(propertySymbol, context)
            ? _anyType
            : GetNonMissingTypeOfSymbol(propertySymbol);
        var saveEnclosingDeclaration = context.EnclosingDeclaration;
        context.EnclosingDeclaration = null;
        if (context.Tracker!.CanTrackSymbol && IsLateBoundName(propertySymbol.EscapedName!))
        {
            if (propertySymbol.Declarations is not null)
            {
                var decl = Core.First(propertySymbol.Declarations);
                if (HasLateBindableName(decl))
                {
                    if (NodeTests.IsBinaryExpression((Node)decl))
                    {
                        var name = Utilities.GetNameOfDeclaration((Node)decl);
                        if (name is not null && NodeTests.IsElementAccessExpression((Node)name) && Utilities.IsPropertyAccessEntityNameExpression(((ElementAccessExpression)name).ArgumentExpression!))
                        {
                            TrackComputedName(((ElementAccessExpression)name).ArgumentExpression!, saveEnclosingDeclaration, context);
                        }
                    }
                    else
                    {
                        TrackComputedName(((ComputedPropertyName)((INamedDeclaration)decl).Name!).Expression!, saveEnclosingDeclaration, context);
                    }
                }
            }
            else
            {
                context.Tracker.ReportNonSerializableProperty(SymbolToString(propertySymbol));
            }
        }
        context.EnclosingDeclaration = (Node?)propertySymbol.ValueDeclaration ?? (Node?)Core.ElementAt(propertySymbol.Declarations, 0) ?? saveEnclosingDeclaration;
        var propertyName = GetPropertyNameNodeForSymbol(propertySymbol, context);
        context.EnclosingDeclaration = saveEnclosingDeclaration;
        context.ApproximateLength += Utilities.SymbolName(propertySymbol).Length + 1;

        if ((propertySymbol.Flags & SymbolFlags.Accessor) != 0)
        {
            var writeType = GetWriteTypeOfSymbol(propertySymbol);
            if (!IsErrorType(propertyType) && !IsErrorType(writeType))
            {
                var symbolMapper = (TypeMapper?)GetSymbolLinks(propertySymbol).Mapper;
                var propDeclaration = Utilities.GetDeclarationOfKind<PropertyDeclaration>(propertySymbol, SyntaxKind.PropertyDeclaration);
                if (!ReferenceEquals(propertyType, writeType) || (propertySymbol.Parent!.Flags & SymbolFlags.Class) != 0 && propDeclaration is null)
                {
                    var getterDeclaration = Utilities.GetDeclarationOfKind<GetAccessorDeclaration>(propertySymbol, SyntaxKind.GetAccessor);
                    if (getterDeclaration is not null)
                    {
                        var getterSignature = GetSignatureFromDeclaration(getterDeclaration);
                        typeElements.Add(
                            SetCommentRange(
                                context,
                                (GetAccessorDeclaration)SignatureToSignatureDeclarationHelper(symbolMapper is not null ? InstantiateSignature(getterSignature, symbolMapper) : getterSignature, SyntaxKind.GetAccessor, context, new SignatureToSignatureDeclarationOptions { Name = propertyName }),
                                getterDeclaration));
                    }
                    var setterDeclaration = Utilities.GetDeclarationOfKind<SetAccessorDeclaration>(propertySymbol, SyntaxKind.SetAccessor);
                    if (setterDeclaration is not null)
                    {
                        var setterSignature = GetSignatureFromDeclaration(setterDeclaration);
                        typeElements.Add(
                            SetCommentRange(
                                context,
                                (SetAccessorDeclaration)SignatureToSignatureDeclarationHelper(symbolMapper is not null ? InstantiateSignature(setterSignature, symbolMapper) : setterSignature, SyntaxKind.SetAccessor, context, new SignatureToSignatureDeclarationOptions { Name = propertyName }),
                                setterDeclaration));
                    }
                    return;
                }
                if ((propertySymbol.Parent!.Flags & SymbolFlags.Class) != 0 && propDeclaration is not null && Core.Find(propDeclaration.Modifiers, (Node m, int _) => NodeTests.IsAccessorModifier(m)) is not null)
                {
                    var fakeGetterSignature = CreateSignature(/*declaration*/ null, /*typeParameters*/ null, /*thisParameter*/ null, Core.EmptyArray<ISymbol>.Value, propertyType, /*resolvedTypePredicate*/ null, 0, SignatureFlags.None);
                    typeElements.Add(
                        SetCommentRange(
                            context,
                            (GetAccessorDeclaration)SignatureToSignatureDeclarationHelper(fakeGetterSignature, SyntaxKind.GetAccessor, context, new SignatureToSignatureDeclarationOptions { Name = propertyName }),
                            propDeclaration));
                    var setterParam = CreateSymbol(SymbolFlags.FunctionScopedVariable, "arg");
                    GetSymbolLinks(setterParam).Type = writeType;
                    var fakeSetterSignature = CreateSignature(/*declaration*/ null, /*typeParameters*/ null, /*thisParameter*/ null, [setterParam], _voidType, /*resolvedTypePredicate*/ null, 0, SignatureFlags.None);
                    typeElements.Add(
                        (SetAccessorDeclaration)SignatureToSignatureDeclarationHelper(fakeSetterSignature, SyntaxKind.SetAccessor, context, new SignatureToSignatureDeclarationOptions { Name = propertyName }));
                    return;
                }
            }
        }

        var optionalToken = (propertySymbol.Flags & SymbolFlags.Optional) != 0 ? (PunctuationToken)_factory.CreateToken(SyntaxKind.QuestionToken) : null;
        if ((propertySymbol.Flags & (SymbolFlags.Function | SymbolFlags.Method)) != 0 && Core.Length(GetPropertiesOfObjectType(propertyType)) == 0 && !IsReadonlySymbol(propertySymbol))
        {
            var signatures = GetSignaturesOfType(FilterType(propertyType, t => (t.Flags & TypeFlags.Undefined) == 0), SignatureKind.Call);
            foreach (var signature in signatures)
            {
                var methodDeclaration = (MethodSignature)SignatureToSignatureDeclarationHelper(signature, SyntaxKind.MethodSignature, context, new SignatureToSignatureDeclarationOptions { Name = propertyName, QuestionToken = optionalToken });
                typeElements.Add(PreserveCommentsOn(methodDeclaration, (Node?)signature.Declaration ?? (Node?)propertySymbol.ValueDeclaration));
            }
            if (signatures.Count != 0 || optionalToken is null)
            {
                return;
            }
        }
        ITypeNode propertyTypeNode;
        if (ShouldUsePlaceholderForProperty(propertySymbol, context))
        {
            propertyTypeNode = CreateElidedInformationPlaceholder(context);
        }
        else
        {
            if (propertyIsReverseMapped)
            {
                context.ReverseMappedStack ??= [];
                context.ReverseMappedStack.Add((IReverseMappedSymbol)propertySymbol);
            }
            propertyTypeNode = SerializeTypeForDeclaration(context, /*declaration*/ null, propertyType, propertySymbol)!;
            if (propertyIsReverseMapped)
            {
                context.ReverseMappedStack!.RemoveAt(context.ReverseMappedStack!.Count - 1);
            }
        }

        List<ModifierToken>? modifiers = IsReadonlySymbol(propertySymbol) ? [_factory.CreateModifier(SyntaxKind.ReadonlyKeyword)] : null;
        if (modifiers is not null)
        {
            context.ApproximateLength += 9;
        }
        var propertySignature = _factory.CreatePropertySignature(
            modifiers,
            propertyName,
            optionalToken,
            propertyTypeNode);

        typeElements.Add(PreserveCommentsOn(propertySignature, (Node?)propertySymbol.ValueDeclaration));

        T PreserveCommentsOn<T>(T node, Node? range)
            where T : Node
        {
            var jsdocPropertyTag = Core.Find(propertySymbol.Declarations, (IDeclaration d, int _) => d.Kind == SyntaxKind.JSDocPropertyTag) as JSDocPropertyTag;
            if (jsdocPropertyTag is not null)
            {
                var commentText = Utilities.GetTextOfJSDocComment(jsdocPropertyTag.Comment);
                if (Js.IsTruthy(commentText))
                {
                    EmitNodes.SetSyntheticLeadingComments(node, [new SynthesizedComment { Kind = SyntaxKind.MultiLineCommentTrivia, Text = "*\n * " + commentText.Replace("\n", "\n * ") + "\n ", Pos = -1, End = -1, HasTrailingNewLine = true }]);
                }
            }
            else if (range is not null)
            {
                // Copy comments to node for declaration emit
                SetCommentRange(context, node, range);
            }
            return node;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:7888-7895 — <c>setCommentRange</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>setCommentRangeWorker</c> is the module-level <c>setCommentRange</c> of
    /// <c>factory/emitNode.ts</c> (checker.ts:1003 imports it under that alias), which the port declares as
    /// <c>EmitNodes.SetCommentRange</c>; the <c>range</c> node is a <see cref="ITextRange"/> through
    /// <see cref="Node"/>.
    /// </remarks>
    internal T SetCommentRange<T>(NodeBuilderContext context, T node, Node range)
        where T : Node
    {
        if (context.EnclosingFile is not null && ReferenceEquals(context.EnclosingFile, Utilities.GetSourceFileOfNode(range)))
        {
            // Copy comments to node for declaration emit
            return EmitNodes.SetCommentRange(node, range);
        }
        return node;
    }

    // ---------------------------------------------------------------------------------------------
    // The type-node list (checker.ts:7896-7976)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:7896-7970 — <c>mapToTypeNodes</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the reference answers <c>TypeNode[] | undefined</c>; the port answers <c>List&lt;ITypeNode&gt;?</c> —
    /// the exact list the factory slots (<c>CreateTypeReferenceNode</c>, <c>CreateUnionTypeNode</c>,
    /// <c>CreateIntersectionTypeNode</c>, <c>CreateArrayTypeNode</c>) take. §7.5 lists an <c>ITypeNode</c> return for
    /// this member, which cannot express a list and is corrected here.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>some(types)</c> absorbs an undefined array (core.ts's <c>some</c>), so the port states the non-null
    /// test explicitly — the same result, and it is what lets the body read <c>types</c> without the reference's
    /// implicit flow typing.
    /// </para>
    /// <para>
    /// PORT-NOTE: the truncation arms build <c>addSyntheticLeadingComment(factory.createKeywordTypeNode(AnyKeyword), …)</c>,
    /// a keyword-type node. The port's helper is constrained to the <see cref="Node"/> class while the factory answers
    /// <see cref="ITypeNode"/>, so the same object is viewed twice; the first arm and the list-swap arm are therefore
    /// written as plain conditionals instead of a conditional nested in an array literal. Which arm runs, and the order
    /// the nodes are built in, are the reference's.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>seenNames</c> is keyed by the <c>__String</c> escaped name, which is a plain <c>string</c> in the
    /// port, and its value is the reference's <c>[Type, number]</c> tuple as a named value tuple; its
    /// <c>forEach</c> is an iteration of <c>Core.MultiMap.Values</c>, and <c>arrayIsHomogeneous</c> is the module-level
    /// helper of utilities.ts (referenced, not declared).
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>isIdentifierTypeReference</c> narrows the node to a type reference with an identifier name; the
    /// port's helper answers <c>bool</c>, so the two reads behind it carry the casts the guard proved.
    /// </para>
    /// </remarks>
    internal List<ITypeNode>? MapToTypeNodes(IList<IType>? types, NodeBuilderContext context, bool isBareList = false)
    {
        if (types is not null && Core.Some(types))
        {
            if (CheckTruncationLength(context))
            {
                context.Out!.Truncated = true;
                if (!isBareList)
                {
                    if ((context.Flags & NodeBuilderFlags.NoTruncation) != 0)
                    {
                        return [(ITypeNode)EmitNodes.AddSyntheticLeadingComment((Node)_factory.CreateKeywordTypeNode(SyntaxKind.AnyKeyword), SyntaxKind.MultiLineCommentTrivia, "elided")];
                    }
                    return [_factory.CreateTypeReferenceNode("...", /*typeArguments*/ null)];
                }
                else if (types.Count > 2)
                {
                    var first = TypeToTypeNodeHelper(types[0], context);
                    ITypeNode elided = (context.Flags & NodeBuilderFlags.NoTruncation) != 0
                        ? (ITypeNode)EmitNodes.AddSyntheticLeadingComment((Node)_factory.CreateKeywordTypeNode(SyntaxKind.AnyKeyword), SyntaxKind.MultiLineCommentTrivia, $"... {types.Count - 2} more elided ...")
                        : _factory.CreateTypeReferenceNode($"... {types.Count - 2} more ...", /*typeArguments*/ null);
                    var last = TypeToTypeNodeHelper(types[types.Count - 1], context);
                    return [first, elided, last];
                }
            }
            var mayHaveNameCollisions = (context.Flags & NodeBuilderFlags.UseFullyQualifiedType) == 0;
            // Map from type reference identifier text to [type, index in `result` where the type node is]
            Core.MultiMap<string, (IType Type, int ResultIndex)>? seenNames = mayHaveNameCollisions ? Core.CreateMultiMap<string, (IType Type, int ResultIndex)>() : null;
            var result = new List<ITypeNode>();
            var i = 0;
            foreach (var type in types)
            {
                i++;
                if (CheckTruncationLength(context) && i + 2 < types.Count - 1)
                {
                    context.Out!.Truncated = true;
                    if ((context.Flags & NodeBuilderFlags.NoTruncation) != 0)
                    {
                        result.Add((ITypeNode)EmitNodes.AddSyntheticLeadingComment((Node)_factory.CreateKeywordTypeNode(SyntaxKind.AnyKeyword), SyntaxKind.MultiLineCommentTrivia, $"... {types.Count - i} more elided ..."));
                    }
                    else
                    {
                        result.Add(_factory.CreateTypeReferenceNode($"... {types.Count - i} more ...", /*typeArguments*/ null));
                    }
                    var typeNode = TypeToTypeNodeHelper(types[types.Count - 1], context);
                    if (typeNode is not null)
                    {
                        result.Add(typeNode);
                    }
                    break;
                }
                context.ApproximateLength += 2; // Account for whitespace + separator
                var node = TypeToTypeNodeHelper(type, context);
                if (node is not null)
                {
                    result.Add(node);
                    if (seenNames is not null && Utilities.IsIdentifierTypeReference((Node)node))
                    {
                        var typeName = ((TypeReferenceNode)node).TypeName!;
                        seenNames.Add(((Identifier)typeName).EscapedText!, (type, result.Count - 1));
                    }
                }
            }

            if (seenNames is not null)
            {
                // To avoid printing types like `[Foo, Foo]` or `Bar & Bar` where
                // occurrences of the same name actually come from different
                // namespaces, go through the single-identifier type reference nodes
                // we just generated, and see if any names were generated more than
                // once while referring to different types. If so, regenerate the
                // type node for each entry by that name with the
                // `UseFullyQualifiedType` flag enabled.
                var restoreFlags = SaveRestoreFlags(context);
                context.Flags |= NodeBuilderFlags.UseFullyQualifiedType;
                foreach (var sameNamedTypes in seenNames.Values)
                {
                    if (!Utilities.ArrayIsHomogeneous(sameNamedTypes, (a, b) => TypesAreSameReference(a.Type, b.Type)))
                    {
                        foreach (var (type, resultIndex) in sameNamedTypes)
                        {
                            result[resultIndex] = TypeToTypeNodeHelper(type, context);
                        }
                    }
                }
                restoreFlags();
            }

            return result;
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:7971-7976 — <c>typesAreSameReference</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>!!a.symbol &amp;&amp; a.symbol === b.symbol</c> is a presence test plus identity; the port keeps both
    /// with <c>ReferenceEquals</c>, so a null symbol on both sides is not a match (as in the reference).
    /// </remarks>
    internal bool TypesAreSameReference(IType a, IType b)
    {
        return ReferenceEquals(a, b)
            || a.Symbol is not null && ReferenceEquals(a.Symbol, b.Symbol)
            || a.AliasSymbol is not null && ReferenceEquals(a.AliasSymbol, b.AliasSymbol);
    }
}
