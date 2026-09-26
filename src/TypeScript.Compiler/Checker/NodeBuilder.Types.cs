namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:6760-7363 — the `typeToTypeNode` family inside `createNodeBuilder`.
/// <summary>
/// The type half of the node builder: the <see cref="TypeFlags"/> dispatch that turns a checked type into the
/// <see cref="ITypeNode"/> naming it, the conditional/mapped/anonymous fallbacks that build a re-inlinable spelling of a
/// deferred type, and the cache that keeps a self-referential type from being expanded forever.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS declares all of this inside one 4,860-line <c>createNodeBuilder</c> closure, so it shares that frame's
/// <c>context</c>. C# cannot share a nested function's closure across members: every function here whose only free
/// variable is <c>context</c> — the overwhelming majority — becomes an <c>internal</c> member taking a
/// <c>NodeBuilderContext</c> parameter (§7.5/§7.5's last paragraph), and the two that a caller hands to another
/// function as a callback while closing over this frame stay local functions. Each member's remarks say which and why.
/// </para>
/// <para>
/// PORT-NOTE: TS's <c>Node</c> is an interface and <c>ITypeNode</c>/<c>IDeclaration</c>/… are its subtypes, so a
/// <c>TypeNode</c> flows into anything expecting a <c>Node</c>. Here <c>Node</c> is a class and <c>ITypeNode</c> is an
/// interface that is not a <c>Node</c> (§3's systemic defect), hence the explicit <c>(Node)</c>/<c>(ITypeNode)</c>
/// conversions at the boundary calls (<c>EmitNodes.AddSyntheticLeadingComment</c>, <c>NodeTests.Is*</c>,
/// <c>IAnonymousType</c> targets).
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // typeToTypeNodeHelper / typeToTypeNodeWorker (checker.ts:6760-7039)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:6760-6768 — <c>typeToTypeNodeHelper</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types the result non-nullable while <c>typeToTypeNodeWorker</c> answers <c>undefined!</c> on three
    /// paths (its own TODO at 6768 is GH#18217); §7.5 makes the worker honestly nullable, so this helper carries TS's
    /// <c>!</c> as the C# suppression on the same expression. <c>restoreFlags</c> is the reference's own zero-argument
    /// closure, which the port's <c>SaveRestoreFlags</c> already returns (§7.5).
    /// </para>
    /// <para>
    /// PORT-NOTE: JS's <c>push</c>/<c>pop</c> on the type stack are <c>Add</c>/<c>RemoveAt(Count - 1)</c> here (the
    /// spelling <c>SymbolToDeclarationsWorker</c> uses, <c>Checker.NodeBuilder.cs:342</c>), and TS's optional
    /// <c>type</c> — the parameter is non-nullable in §7.5's signature, so the worker call asserts it exactly where the
    /// reference relies on the guard having established it.
    /// </remarks>
    internal ITypeNode TypeToTypeNodeHelper(IType type, NodeBuilderContext context)
    {
        var restoreFlags = SaveRestoreFlags(context);
        if (type is not null) context.TypeStack!.Add(type.Id);
        var typeNode = TypeToTypeNodeWorker(type!, context);
        if (type is not null) context.TypeStack!.RemoveAt(context.TypeStack!.Count - 1);
        restoreFlags();
        return typeNode!;
    }

    /// <summary>ported from: src/compiler/checker.ts:6769-7039 — <c>typeToTypeNodeWorker</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the reference's leading <c>cancellationToken &amp;&amp; cancellationToken.throwIfCancellationRequested</c>
    /// test guards an optional callback slot; the port's <see cref="ICancellationToken"/> declares
    /// <c>ThrowIfCancellationRequested</c> unconditionally, so the same "is there a token at all" test is the null check
    /// on the chain.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>inTypeAlias</c> is TS's numeric flag read of <c>NodeBuilderFlags.InTypeAlias</c> used only for
    /// truthiness; the port keeps it as <c>bool</c> so that the one test it feeds (<c>!inTypeAlias</c>) stays spelled
    /// the same. TS's <c>undefined!</c> returns (its TODO is GH#18217) are <c>null</c> here, which is what §7.5's
    /// nullable return was declared for.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS declares two locals named <c>name</c> in the TypeParameter/ClassOrInterface arm — the shadowed
    /// type parameter's identifier (6944) and the marker fallback (6969). C# rejects the second at that scope with
    /// CS0136 (a local declared in a nested block is reserved for the whole enclosing block; verified by compiling the
    /// shape), so the fallback is <c>fallbackName</c>. The two <c>Debug.fail</c> tails become <c>Debug.Fail</c> plus a
    /// <c>null</c> return, because the port's <see cref="Debug.Fail"/> answers <c>void</c> where TS's <c>fail</c> is
    /// <c>never</c>.
    /// </para>
    /// </remarks>
    internal ITypeNode? TypeToTypeNodeWorker(IType type, NodeBuilderContext context)
    {
        _cancellationToken?.ThrowIfCancellationRequested();
        var inTypeAlias = (context.Flags & NodeBuilderFlags.InTypeAlias) != 0;
        context.Flags &= ~NodeBuilderFlags.InTypeAlias;
        var expandingEnum = false;

        if (type is null)
        {
            if ((context.Flags & NodeBuilderFlags.AllowEmptyUnionOrIntersection) == 0)
            {
                context.EncounteredError = true;
                return null; // TODO: GH#18217
            }
            context.ApproximateLength += 3;
            return _factory.CreateKeywordTypeNode(SyntaxKind.AnyKeyword);
        }

        if ((context.Flags & NodeBuilderFlags.NoTypeReduction) == 0)
        {
            type = GetReducedType(type);
        }

        if ((type.Flags & TypeFlags.Any) != 0)
        {
            if (type.AliasSymbol is not null)
            {
                return _factory.CreateTypeReferenceNode(SymbolToEntityNameNode(type.AliasSymbol), MapToTypeNodes(type.AliasTypeArguments, context));
            }
            if (ReferenceEquals(type, _unresolvedType))
            {
                return (ITypeNode)EmitNodes.AddSyntheticLeadingComment((Node)_factory.CreateKeywordTypeNode(SyntaxKind.AnyKeyword), SyntaxKind.MultiLineCommentTrivia, "unresolved");
            }
            context.ApproximateLength += 3;
            return _factory.CreateKeywordTypeNode(ReferenceEquals(type, _intrinsicMarkerType) ? SyntaxKind.IntrinsicKeyword : SyntaxKind.AnyKeyword);
        }
        if ((type.Flags & TypeFlags.Unknown) != 0)
        {
            return _factory.CreateKeywordTypeNode(SyntaxKind.UnknownKeyword);
        }
        if ((type.Flags & TypeFlags.String) != 0)
        {
            context.ApproximateLength += 6;
            return _factory.CreateKeywordTypeNode(SyntaxKind.StringKeyword);
        }
        if ((type.Flags & TypeFlags.Number) != 0)
        {
            context.ApproximateLength += 6;
            return _factory.CreateKeywordTypeNode(SyntaxKind.NumberKeyword);
        }
        if ((type.Flags & TypeFlags.BigInt) != 0)
        {
            context.ApproximateLength += 6;
            return _factory.CreateKeywordTypeNode(SyntaxKind.BigIntKeyword);
        }
        if ((type.Flags & TypeFlags.Boolean) != 0 && type.AliasSymbol is null)
        {
            context.ApproximateLength += 7;
            return _factory.CreateKeywordTypeNode(SyntaxKind.BooleanKeyword);
        }
        if ((type.Flags & TypeFlags.EnumLike) != 0)
        {
            if ((type.Symbol!.Flags & SymbolFlags.EnumMember) != 0)
            {
                var parentSymbol = GetParentOfSymbol(type.Symbol!)!;
                var parentName = SymbolToTypeNode(parentSymbol, context, SymbolFlags.Type);
                if (ReferenceEquals(GetDeclaredTypeOfSymbol(parentSymbol), type))
                {
                    return parentName;
                }
                var memberName = Utilities.SymbolName(type.Symbol!);
                if (ScannerUtilities.IsIdentifierText(memberName, ScriptTarget.ESNext))
                {
                    return AppendReferenceToType((Node)parentName, _factory.CreateTypeReferenceNode(memberName, /*typeArguments*/ null));
                }
                if (NodeTests.IsImportTypeNode((Node)parentName))
                {
                    ((ImportTypeNode)parentName).IsTypeOf = true; // mutably update, node is freshly manufactured anyhow
                    return _factory.CreateIndexedAccessTypeNode(parentName, _factory.CreateLiteralTypeNode(_factory.CreateStringLiteral(memberName)));
                }
                else if (NodeTests.IsTypeReferenceNode((Node)parentName))
                {
                    return _factory.CreateIndexedAccessTypeNode(_factory.CreateTypeQueryNode(((TypeReferenceNode)parentName).TypeName, /*typeArguments*/ null), _factory.CreateLiteralTypeNode(_factory.CreateStringLiteral(memberName)));
                }
                Debug.Fail("Unhandled type node kind returned from `symbolToTypeNode`.");
                return null;
            }
            if (!ShouldExpandType(type, context))
            {
                return SymbolToTypeNode(type.Symbol!, context, SymbolFlags.Type);
            }
            else
            {
                expandingEnum = true;
            }
        }
        if ((type.Flags & TypeFlags.StringLiteral) != 0)
        {
            var value = (string)((ILiteralType)type).Value!;
            context.ApproximateLength += value.Length + 2;
            return _factory.CreateLiteralTypeNode(EmitNodes.SetEmitFlags(_factory.CreateStringLiteral(value, (context.Flags & NodeBuilderFlags.UseSingleQuotesForStringLiteralType) != 0), EmitFlags.NoAsciiEscaping));
        }
        if ((type.Flags & TypeFlags.NumberLiteral) != 0)
        {
            var value = (double)((ILiteralType)type).Value!;
            context.ApproximateLength += Js.NumberToString(value).Length;
            return _factory.CreateLiteralTypeNode(value < 0
                ? _factory.CreatePrefixUnaryExpression(SyntaxKind.MinusToken, _factory.CreateNumericLiteral(Js.NumberToString(-value)))
                : _factory.CreateNumericLiteral(Js.NumberToString(value)));
        }
        if ((type.Flags & TypeFlags.BigIntLiteral) != 0)
        {
            var value = (IPseudoBigInt)((ILiteralType)type).Value!;
            context.ApproximateLength += Utilities.PseudoBigIntToString(value).Length + 1;
            return _factory.CreateLiteralTypeNode(_factory.CreateBigIntLiteral(Utilities.PseudoBigIntToString(value) + "n"));
        }
        if ((type.Flags & TypeFlags.BooleanLiteral) != 0)
        {
            var intrinsicName = ((IntrinsicType)type).IntrinsicName!;
            context.ApproximateLength += intrinsicName.Length;
            return _factory.CreateLiteralTypeNode(intrinsicName == "true" ? _factory.CreateTrue() : _factory.CreateFalse());
        }
        if ((type.Flags & TypeFlags.UniqueESSymbol) != 0)
        {
            if ((context.Flags & NodeBuilderFlags.AllowUniqueESSymbolType) == 0)
            {
                if (IsValueSymbolAccessible(type.Symbol!, context.EnclosingDeclaration))
                {
                    context.ApproximateLength += 6;
                    return SymbolToTypeNode(type.Symbol!, context, SymbolFlags.Value);
                }
                context.Tracker?.ReportInaccessibleUniqueSymbolError();
            }
            context.ApproximateLength += 13;
            return _factory.CreateTypeOperatorNode(SyntaxKind.UniqueKeyword, _factory.CreateKeywordTypeNode(SyntaxKind.SymbolKeyword));
        }
        if ((type.Flags & TypeFlags.Void) != 0)
        {
            context.ApproximateLength += 4;
            return _factory.CreateKeywordTypeNode(SyntaxKind.VoidKeyword);
        }
        if ((type.Flags & TypeFlags.Undefined) != 0)
        {
            context.ApproximateLength += 9;
            return _factory.CreateKeywordTypeNode(SyntaxKind.UndefinedKeyword);
        }
        if ((type.Flags & TypeFlags.Null) != 0)
        {
            context.ApproximateLength += 4;
            return _factory.CreateLiteralTypeNode(_factory.CreateNull());
        }
        if ((type.Flags & TypeFlags.Never) != 0)
        {
            context.ApproximateLength += 5;
            return _factory.CreateKeywordTypeNode(SyntaxKind.NeverKeyword);
        }
        if ((type.Flags & TypeFlags.ESSymbol) != 0)
        {
            context.ApproximateLength += 6;
            return _factory.CreateKeywordTypeNode(SyntaxKind.SymbolKeyword);
        }
        if ((type.Flags & TypeFlags.NonPrimitive) != 0)
        {
            context.ApproximateLength += 6;
            return _factory.CreateKeywordTypeNode(SyntaxKind.ObjectKeyword);
        }
        if (Utilities.IsThisTypeParameter(type))
        {
            if ((context.Flags & NodeBuilderFlags.InObjectTypeLiteral) != 0)
            {
                if (!context.EncounteredError && (context.Flags & NodeBuilderFlags.AllowThisInObjectLiteral) == 0)
                {
                    context.EncounteredError = true;
                }
                context.Tracker?.ReportInaccessibleThisError();
            }
            context.ApproximateLength += 4;
            return _factory.CreateThisTypeNode();
        }

        if (!inTypeAlias && type.AliasSymbol is not null && ((context.Flags & NodeBuilderFlags.UseAliasDefinedOutsideCurrentScope) != 0 || IsTypeSymbolAccessible(type.AliasSymbol, context.EnclosingDeclaration)))
        {
            if (!ShouldExpandType(type, context, /*isAlias*/ true))
            {
                var typeArgumentNodes = MapToTypeNodes(type.AliasTypeArguments, context);
                if (IsReservedMemberName(type.AliasSymbol!.EscapedName!) && (type.AliasSymbol.Flags & SymbolFlags.Class) == 0) return _factory.CreateTypeReferenceNode(_factory.CreateIdentifier(""), typeArgumentNodes);
                if (Core.Length(typeArgumentNodes) == 1 && ReferenceEquals(type.AliasSymbol, _globalArrayType.Symbol))
                {
                    return _factory.CreateArrayTypeNode(typeArgumentNodes![0]);
                }
                return SymbolToTypeNode(type.AliasSymbol, context, SymbolFlags.Type, typeArgumentNodes);
            }
            context.Depth += 1;
        }

        var objectFlags = GetObjectFlags(type);

        if ((objectFlags & ObjectFlags.Reference) != 0)
        {
            Debug.Assert((type.Flags & TypeFlags.Object) != 0);
            if (ShouldExpandType(type, context))
            {
                context.Depth += 1;
                return CreateAnonymousTypeNode((TypeReference)type, context, /*forceClassExpansion*/ true, /*forceExpansion*/ true);
            }
            return ((TypeReference)type).Node is not null
                ? VisitAndTransformType(type, t => TypeReferenceToTypeNode((TypeReference)t, context), context)
                : TypeReferenceToTypeNode((TypeReference)type, context);
        }
        if ((type.Flags & TypeFlags.TypeParameter) != 0 || (objectFlags & ObjectFlags.ClassOrInterface) != 0)
        {
            if ((type.Flags & TypeFlags.TypeParameter) != 0 && Core.Contains(context.InferTypeParameters, (ITypeParameter)type))
            {
                context.ApproximateLength += Utilities.SymbolName(type.Symbol!).Length + 6;
                ITypeNode? constraintNode = null;
                var constraint = GetConstraintOfTypeParameter((ITypeParameter)type);
                if (constraint is not null)
                {
                    // If the infer type has a constraint that is not the same as the constraint
                    // we would have normally inferred based on context, we emit the constraint
                    // using `infer T extends ?`. We omit inferred constraints from type references
                    // as they may be elided.
                    var inferredConstraint = GetInferredTypeParameterConstraint((ITypeParameter)type, /*omitTypeReferences*/ true);
                    if (inferredConstraint is null || !IsTypeIdenticalTo(constraint, inferredConstraint))
                    {
                        context.ApproximateLength += 9;
                        constraintNode = constraint is not null ? TypeToTypeNodeHelper(constraint, context) : null;
                    }
                }
                return _factory.CreateInferTypeNode(TypeParameterToDeclarationWithConstraint((ITypeParameter)type, context, constraintNode));
            }
            if ((context.Flags & NodeBuilderFlags.GenerateNamesForShadowedTypeParams) != 0 && (type.Flags & TypeFlags.TypeParameter) != 0)
            {
                var name = TypeParameterToName((ITypeParameter)type, context);
                context.ApproximateLength += Utilities.IdText(name).Length;
                return _factory.CreateTypeReferenceNode(_factory.CreateIdentifier(Utilities.IdText(name)), /*typeArguments*/ null);
            }
            if ((objectFlags & ObjectFlags.ClassOrInterface) != 0 && ShouldExpandType(type, context))
            {
                context.Depth += 1;
                return CreateAnonymousTypeNode((InterfaceType)type, context, /*forceClassExpansion*/ true, /*forceExpansion*/ true);
            }
            // Ignore constraint/default when creating a usage (as opposed to declaration) of a type parameter.
            if (type.Symbol is not null)
            {
                return SymbolToTypeNode(type.Symbol, context, SymbolFlags.Type);
            }
            var fallbackName = (ReferenceEquals(type, _markerSuperTypeForCheck) || ReferenceEquals(type, _markerSubTypeForCheck)) && _varianceTypeParameter?.Symbol is not null
                ? (ReferenceEquals(type, _markerSubTypeForCheck) ? "sub-" : "super-") + Utilities.SymbolName(_varianceTypeParameter!.Symbol!)
                : "?";
            return _factory.CreateTypeReferenceNode(_factory.CreateIdentifier(fallbackName), /*typeArguments*/ null);
        }
        if ((type.Flags & TypeFlags.Union) != 0 && ((UnionType)type).Origin is not null)
        {
            type = ((UnionType)type).Origin!;
        }
        if ((type.Flags & (TypeFlags.Union | TypeFlags.Intersection)) != 0)
        {
            var types = (type.Flags & TypeFlags.Union) != 0
                ? FormatUnionTypes(((UnionType)type).Types!, expandingEnum)
                : ((IntersectionType)type).Types!;
            if (Core.Length(types) == 1)
            {
                return TypeToTypeNodeHelper(types[0], context);
            }
            var typeNodes = MapToTypeNodes(types, context, /*isBareList*/ true);
            if (typeNodes is not null && typeNodes.Count > 0)
            {
                return (type.Flags & TypeFlags.Union) != 0 ? _factory.CreateUnionTypeNode(typeNodes) : _factory.CreateIntersectionTypeNode(typeNodes);
            }
            else
            {
                if (!context.EncounteredError && (context.Flags & NodeBuilderFlags.AllowEmptyUnionOrIntersection) == 0)
                {
                    context.EncounteredError = true;
                }
                return null; // TODO: GH#18217
            }
        }
        if ((objectFlags & (ObjectFlags.Anonymous | ObjectFlags.Mapped)) != 0)
        {
            Debug.Assert((type.Flags & TypeFlags.Object) != 0);
            // The type is an object literal type.
            return CreateAnonymousTypeNode((ObjectType)type, context);
        }
        if ((type.Flags & TypeFlags.Index) != 0)
        {
            var indexedType = ((IndexType)type).Type!;
            context.ApproximateLength += 6;
            var indexTypeNode = TypeToTypeNodeHelper(indexedType, context);
            return _factory.CreateTypeOperatorNode(SyntaxKind.KeyOfKeyword, indexTypeNode);
        }
        if ((type.Flags & TypeFlags.TemplateLiteral) != 0)
        {
            var texts = ((TemplateLiteralType)type).Texts!;
            var types = ((TemplateLiteralType)type).Types!;
            var templateHead = _factory.CreateTemplateHead(texts[0]);
            var templateSpans = _factory.CreateNodeArray(
                Core.Map(types, (t, i) =>
                    _factory.CreateTemplateLiteralTypeSpan(
                        TypeToTypeNodeHelper(t, context),
                        i < types.Count - 1 ? _factory.CreateTemplateMiddle(texts[i + 1]) : _factory.CreateTemplateTail(texts[i + 1]))),
                /*hasTrailingComma*/ null);
            context.ApproximateLength += 2;
            return _factory.CreateTemplateLiteralType(templateHead, templateSpans);
        }
        if ((type.Flags & TypeFlags.StringMapping) != 0)
        {
            var typeNode = TypeToTypeNodeHelper(((StringMappingType)type).Type!, context);
            return SymbolToTypeNode(((StringMappingType)type).Symbol!, context, SymbolFlags.Type, [typeNode]);
        }
        if ((type.Flags & TypeFlags.IndexedAccess) != 0)
        {
            var objectTypeNode = TypeToTypeNodeHelper(((IndexedAccessType)type).ObjectType!, context);
            var indexTypeNode = TypeToTypeNodeHelper(((IndexedAccessType)type).IndexType!, context);
            context.ApproximateLength += 2;
            return _factory.CreateIndexedAccessTypeNode(objectTypeNode, indexTypeNode);
        }
        if ((type.Flags & TypeFlags.Conditional) != 0)
        {
            return VisitAndTransformType(type, t => ConditionalTypeToTypeNode((ConditionalType)t, context), context);
        }
        if ((type.Flags & TypeFlags.Substitution) != 0)
        {
            var typeNode = TypeToTypeNodeHelper(((SubstitutionType)type).BaseType!, context);
            // PORT-NOTE: TS's `isNoInferType(type) && getGlobalTypeSymbol(...)` types as `Symbol | undefined | false`
            // (the `&&` short-circuit's left arm). C# has no falsy value, so the same "both, or nothing" test is the
            // ternary below; the truthiness tests that consume `noInferSymbol` are unchanged.
            var noInferSymbol = IsNoInferType(type) ? GetGlobalTypeSymbol("NoInfer", /*reportErrors*/ false) : null;
            return noInferSymbol is not null ? SymbolToTypeNode(noInferSymbol, context, SymbolFlags.Type, [typeNode]) : typeNode;
        }

        Debug.Fail("Should be unreachable.");
        return null;
    }

    // ---------------------------------------------------------------------------------------------
    // The nested companions of typeToTypeNodeWorker (checker.ts:7041-7109)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:7041-7087 — <c>conditionalTypeToTypeNode</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the reference is nested in <c>typeToTypeNodeWorker</c> and closes over that frame's <c>context</c>
    /// alone, so by §7.5's rule it is a member taking <c>context</c> as a parameter; its one caller (7031) passes it.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS declares <c>saveInferTypeParameters</c>, <c>extendsTypeNode</c>, <c>trueTypeNode</c> and
    /// <c>falseTypeNode</c> twice — once in the shadowed-type-parameters branch (7044-7052), once on the main path
    /// (7078-7082). C# rejects the second declaration at this scope with CS0136 (verified by compiling the shape), so
    /// the main path is wrapped in the bare block below, which is exactly the scope TS's two declarations already
    /// imply; every name and the statement order are unchanged.
    /// </para>
    /// </remarks>
    internal ITypeNode ConditionalTypeToTypeNode(ConditionalType type, NodeBuilderContext context)
    {
        var checkTypeNode = TypeToTypeNodeHelper(type.CheckType!, context);
        context.ApproximateLength += 15;
        if ((context.Flags & NodeBuilderFlags.GenerateNamesForShadowedTypeParams) != 0 && type.Root!.IsDistributive && (type.CheckType!.Flags & TypeFlags.TypeParameter) == 0)
        {
            var newParam = CreateTypeParameter(CreateSymbol(SymbolFlags.TypeParameter, "T"));
            var name = TypeParameterToName(newParam, context);
            var newTypeVariable = _factory.CreateTypeReferenceNode(name, /*typeArguments*/ null);
            context.ApproximateLength += 37; // 15 each for two added conditionals, 7 for an added infer type
            var newMapper = PrependTypeMapping(type.Root!.CheckType!, newParam, (TypeMapper?)type.Mapper);
            var saveInferTypeParameters = context.InferTypeParameters;
            context.InferTypeParameters = type.Root!.InferTypeParameters;
            var extendsTypeNode = TypeToTypeNodeHelper(InstantiateType(type.Root!.ExtendsType!, newMapper)!, context);
            context.InferTypeParameters = saveInferTypeParameters;
            var trueTypeNode = TypeToTypeNodeOrCircularityElision(InstantiateType(GetTypeFromTypeNode(context, type.Root!.Node!.TrueType!), newMapper)!, context);
            var falseTypeNode = TypeToTypeNodeOrCircularityElision(InstantiateType(GetTypeFromTypeNode(context, type.Root!.Node!.FalseType!), newMapper)!, context);

            // outermost conditional makes `T` a type parameter, allowing the inner conditionals to be distributive
            // second conditional makes `T` have `T & checkType` substitution, so it is correctly usable as the checkType
            // inner conditional runs the check the user provided on the check type (distributively) and returns the result
            // checkType extends infer T ? T extends checkType ? T extends extendsType<T> ? trueType<T> : falseType<T> : never : never;
            // this is potentially simplifiable to
            // checkType extends infer T ? T extends checkType & extendsType<T> ? trueType<T> : falseType<T> : never;
            // but that may confuse users who read the output more.
            // On the other hand,
            // checkType extends infer T extends checkType ? T extends extendsType<T> ? trueType<T> : falseType<T> : never;
            // may also work with `infer ... extends ...` in, but would produce declarations only compatible with the latest TS.
            return _factory.CreateConditionalTypeNode(
                checkTypeNode,
                _factory.CreateInferTypeNode(_factory.CreateTypeParameterDeclaration(/*modifiers*/ null, _factory.CloneNode(((TypeReferenceNode)newTypeVariable).TypeName), /*constraint*/ null, /*defaultType*/ null)),
                _factory.CreateConditionalTypeNode(
                    _factory.CreateTypeReferenceNode(_factory.CloneNode(name), /*typeArguments*/ null),
                    TypeToTypeNodeHelper(type.CheckType!, context),
                    _factory.CreateConditionalTypeNode(newTypeVariable, extendsTypeNode, trueTypeNode, falseTypeNode),
                    _factory.CreateKeywordTypeNode(SyntaxKind.NeverKeyword)),
                _factory.CreateKeywordTypeNode(SyntaxKind.NeverKeyword));
        }
        // PORT-NOTE: TS re-declares saveInferTypeParameters/extendsTypeNode/trueTypeNode/falseTypeNode here (7078-7082);
        // C# rejects that at this scope (CS0136), so this bare block is the scope the two sibling declarations imply —
        // see the member's remarks.
        {
            var saveInferTypeParameters = context.InferTypeParameters;
            context.InferTypeParameters = type.Root!.InferTypeParameters;
            var extendsTypeNode = TypeToTypeNodeHelper(type.ExtendsType!, context);
            context.InferTypeParameters = saveInferTypeParameters;
            var trueTypeNode = TypeToTypeNodeOrCircularityElision(GetTrueTypeFromConditionalType(type), context);
            var falseTypeNode = TypeToTypeNodeOrCircularityElision(GetFalseTypeFromConditionalType(type), context);
            return _factory.CreateConditionalTypeNode(checkTypeNode, extendsTypeNode, trueTypeNode, falseTypeNode);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:7088-7101 — <c>typeToTypeNodeOrCircularityElision</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference is nested in <c>typeToTypeNodeWorker</c> and closes over its <c>context</c>; it is a
    /// member here because its callers are the two nodes of <see cref="ConditionalTypeToTypeNode"/> (§7.5 fixes this
    /// signature). <c>context.tracker?.reportCyclicStructureError?.()</c> is the optional-callback chain: the port's
    /// tracker declares every callback unconditionally and is itself nullable, so the two <c>?</c> collapse into the
    /// one on <c>Tracker</c>.
    /// </remarks>
    internal ITypeNode? TypeToTypeNodeOrCircularityElision(IType type, NodeBuilderContext context)
    {
        if ((type.Flags & TypeFlags.Union) != 0)
        {
            if (context.VisitedTypes?.Contains(GetTypeId(type)) == true)
            {
                if ((context.Flags & NodeBuilderFlags.AllowAnonymousIdentifier) == 0)
                {
                    context.EncounteredError = true;
                    context.Tracker?.ReportCyclicStructureError();
                }
                return CreateElidedInformationPlaceholder(context);
            }
            return VisitAndTransformType(type, t => TypeToTypeNodeHelper(t, context), context);
        }
        return TypeToTypeNodeHelper(type, context);
    }

    /// <summary>ported from: src/compiler/checker.ts:7102-7105 — <c>isMappedTypeHomomorphic</c>.</summary>
    internal bool IsMappedTypeHomomorphic(MappedType type) => GetHomomorphicTypeVariable(type) is not null;

    /// <summary>ported from: src/compiler/checker.ts:7106-7109 — <c>isHomomorphicMappedTypeWithNonHomomorphicInstantiation</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>type.target</c> reaches the <c>AnonymousType.target</c> slot through the mapped face; the port
    /// unifies that slot with <c>TypeReference.target</c> in one <c>TargetSlot</c> and exposes it per interface
    /// (<c>Model/ObjectType.cs</c>), so the read is <c>((IAnonymousType)type).Target</c> and the <c>as MappedType</c>
    /// assertion becomes the type pattern that also covers TS's <c>!!</c> truthiness test.
    /// </remarks>
    internal bool IsHomomorphicMappedTypeWithNonHomomorphicInstantiation(MappedType type)
    {
        return ((IAnonymousType)type).Target is MappedType target && IsMappedTypeHomomorphic(target) && !IsMappedTypeHomomorphic(type);
    }

    // ---------------------------------------------------------------------------------------------
    // createMappedTypeNodeFromType (checker.ts:7110-7186)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:7110-7186 — <c>createMappedTypeNodeFromType</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the reference is nested in <c>typeToTypeNodeWorker</c> and closes over its <c>context</c>; §7.5 fixes
    /// it as a member taking <c>context</c>. Its caller outside this file is
    /// <c>CreateTypeNodeFromObjectType</c> (agent B).
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>type.declaration</c> is non-optional in RS but a nullable slot in the port, so the four reads are
    /// asserted once through <c>Declaration!</c> rather than at each token. TS's <c>createToken(...) as ReadonlyKeyword
    /// | PlusToken | MinusToken</c> has no C# counterpart — <c>CreateToken</c> answers the <c>Node</c> base — so the
    /// conversions to <see cref="Token"/>/<see cref="PunctuationToken"/> are explicit, which is the same claim.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>getConstraintOfTypeParameter(...)?.flags! &amp; TypeFlags.Index</c> is JS's "undefined &amp; x is 0",
    /// i.e. false when there is no constraint of a type-parameter constraint; the port spells the whole conjunct as the
    /// pattern test below instead of inventing a <c>TypeFlags.None</c> (the enum has no zero member).
    /// </para>
    /// <para>
    /// PORT-NOTE: the same TS looseness appears twice more in this member. TS passes
    /// <c>getConstraintTypeFromMappedType(type)</c> (declared <c>Type</c>) to <c>getConstraintOfTypeParameter</c>
    /// (<c>TypeParameter</c>), guarded by the <c>&amp; TypeFlags.TypeParameter</c> test immediately to its left — which is
    /// exactly why the port's explicit <c>(ITypeParameter)</c> cast is safe at run time — and <c>makeArrayTypeMapper</c>'s
    /// sources parameter is <c>List&lt;ITypeParameter&gt;</c> (<c>Checker.Instantiation.cs:190</c>) while
    /// <c>getModifiersTypeFromMappedType</c> answers an <c>IType</c> (<c>Checker.Support.cs:271</c>), so that argument is
    /// downcast the same way. TS never checks either element because its structural typing does not; C# lists and
    /// parameters are nominal, so the claim is written down.
    /// </para>
    /// </remarks>
    internal ITypeNode CreateMappedTypeNodeFromType(MappedType type, NodeBuilderContext context)
    {
        Debug.Assert((type.Flags & TypeFlags.Object) != 0);
        Token? readonlyToken = type.Declaration!.ReadonlyToken is not null ? (Token)_factory.CreateToken(type.Declaration!.ReadonlyToken!.Kind) : null;
        PunctuationToken? questionToken = type.Declaration!.QuestionToken is not null ? (PunctuationToken)_factory.CreateToken(type.Declaration!.QuestionToken!.Kind) : null;
        ITypeNode appropriateConstraintTypeNode;
        TypeReferenceNode? newTypeVariable = null;
        var templateType = GetTemplateTypeFromMappedType(type);
        var typeParameter = GetTypeParameterFromMappedType(type);
        // If the mapped type isn't `keyof` constraint-declared, _but_ still has modifiers preserved, and its naive
        // instantiation won't preserve modifiers because its constraint isn't `keyof` constrained, we have work to do
        var needsModifierPreservingWrapper = !IsMappedTypeWithKeyofConstraintDeclaration(type)
            && (GetModifiersTypeFromMappedType(type).Flags & TypeFlags.Unknown) == 0
            && (context.Flags & NodeBuilderFlags.GenerateNamesForShadowedTypeParams) != 0
            && !((GetConstraintTypeFromMappedType(type).Flags & TypeFlags.TypeParameter) != 0
                && GetConstraintOfTypeParameter((ITypeParameter)GetConstraintTypeFromMappedType(type)) is IType constraintOfConstraint
                && (constraintOfConstraint.Flags & TypeFlags.Index) != 0);
        if (IsMappedTypeWithKeyofConstraintDeclaration(type))
        {
            // We have a { [P in keyof T]: X }
            // We do this to ensure we retain the toplevel keyof-ness of the type which may be lost due to keyof
            // distribution during `getConstraintTypeFromMappedType`
            if (IsHomomorphicMappedTypeWithNonHomomorphicInstantiation(type) && (context.Flags & NodeBuilderFlags.GenerateNamesForShadowedTypeParams) != 0)
            {
                var newConstraintParam = CreateTypeParameter(CreateSymbol(SymbolFlags.TypeParameter, "T"));
                var name = TypeParameterToName(newConstraintParam, context);
                var target = (MappedType)((IAnonymousType)type).Target!;
                newTypeVariable = _factory.CreateTypeReferenceNode(name, /*typeArguments*/ null);
                templateType = InstantiateType(
                    GetTemplateTypeFromMappedType(target),
                    MakeArrayTypeMapper(
                        [GetTypeParameterFromMappedType(target), (ITypeParameter)GetModifiersTypeFromMappedType(target)],
                        [typeParameter, newConstraintParam]))!;
            }
            appropriateConstraintTypeNode = _factory.CreateTypeOperatorNode(SyntaxKind.KeyOfKeyword, newTypeVariable ?? TypeToTypeNodeHelper(GetModifiersTypeFromMappedType(type), context));
        }
        else if (needsModifierPreservingWrapper)
        {
            // So, step 1: new type variable
            var newParam = CreateTypeParameter(CreateSymbol(SymbolFlags.TypeParameter, "T"));
            var name = TypeParameterToName(newParam, context);
            newTypeVariable = _factory.CreateTypeReferenceNode(name, /*typeArguments*/ null);
            // step 2: make that new type variable itself the constraint node, making the mapped type `{[K in T_1]: Template}`
            appropriateConstraintTypeNode = newTypeVariable;
        }
        else
        {
            appropriateConstraintTypeNode = TypeToTypeNodeHelper(GetConstraintTypeFromMappedType(type), context);
        }
        var typeParameterNode = TypeParameterToDeclarationWithConstraint(typeParameter, context, appropriateConstraintTypeNode);

        // nameType and templateType nodes have to be in the new scope
        var cleanup = EnterNewScope(context, type.Declaration!, /*expandedParams*/ null, [GetDeclaredTypeOfTypeParameter(GetSymbolOfDeclaration(type.Declaration!.TypeParameter!))]);
        ITypeNode? nameTypeNode = type.Declaration!.NameType is not null ? TypeToTypeNodeHelper(GetNameTypeFromMappedType(type)!, context) : null;
        var templateTypeNode = TypeToTypeNodeHelper(RemoveMissingType(templateType, (GetMappedTypeModifiers(type) & MappedTypeModifiers.IncludeOptional) != 0), context);
        cleanup();

        var mappedTypeNode = _factory.CreateMappedTypeNode(readonlyToken, typeParameterNode, nameTypeNode, questionToken, templateTypeNode, /*members*/ null);
        context.ApproximateLength += 10;
        var result = EmitNodes.SetEmitFlags(mappedTypeNode, EmitFlags.SingleLine);
        if (IsHomomorphicMappedTypeWithNonHomomorphicInstantiation(type) && (context.Flags & NodeBuilderFlags.GenerateNamesForShadowedTypeParams) != 0)
        {
            // homomorphic mapped type with a non-homomorphic naive inlining
            // wrap it with a conditional like `SomeModifiersType extends infer U ? {..the mapped type...} : never` to ensure the resulting
            // type stays homomorphic
            var originalConstraint = InstantiateType(
                GetConstraintOfTypeParameter((ITypeParameter)GetTypeFromTypeNode(context, ((TypeOperatorNode)type.Declaration!.TypeParameter!.Constraint!).Type!)!) ?? _unknownType,
                (TypeMapper?)type.Mapper)!;
            return _factory.CreateConditionalTypeNode(
                TypeToTypeNodeHelper(GetModifiersTypeFromMappedType(type), context),
                _factory.CreateInferTypeNode(_factory.CreateTypeParameterDeclaration(
                    /*modifiers*/ null,
                    _factory.CloneNode(((TypeReferenceNode)newTypeVariable!).TypeName),
                    (originalConstraint.Flags & TypeFlags.Unknown) != 0 ? null : TypeToTypeNodeHelper(originalConstraint, context),
                    /*defaultType*/ null)),
                result,
                _factory.CreateKeywordTypeNode(SyntaxKind.NeverKeyword));
        }
        else if (needsModifierPreservingWrapper)
        {
            // and step 3: once the mapped type is reconstructed, create a `ConstraintType extends infer T_1 extends keyof ModifiersType ? {[K in T_1]: Template} : never`
            // subtly different from the `keyof` constraint case, by including the `keyof` constraint on the `infer` type parameter, it doesn't rely on the constraint type being itself
            // constrained to a `keyof` type to preserve its modifier-preserving behavior. This is all basically because we preserve modifiers for a wider set of mapped types than
            // just homomorphic ones.
            return _factory.CreateConditionalTypeNode(
                TypeToTypeNodeHelper(GetConstraintTypeFromMappedType(type), context),
                _factory.CreateInferTypeNode(_factory.CreateTypeParameterDeclaration(
                    /*modifiers*/ null,
                    _factory.CloneNode(((TypeReferenceNode)newTypeVariable!).TypeName),
                    _factory.CreateTypeOperatorNode(SyntaxKind.KeyOfKeyword, TypeToTypeNodeHelper(GetModifiersTypeFromMappedType(type), context)),
                    /*defaultType*/ null)),
                result,
                _factory.CreateKeywordTypeNode(SyntaxKind.NeverKeyword));
        }
        return result;
    }

    // ---------------------------------------------------------------------------------------------
    // createAnonymousTypeNode (checker.ts:7187-7271)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:7187-7271 — <c>createAnonymousTypeNode</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the reference is nested in <c>typeToTypeNodeWorker</c> and closes over its <c>context</c>; §7.5 fixes
    /// it as a member taking <c>context</c>. Its caller outside this file is <c>CreateTypeNodeFromObjectType</c>
    /// (agent B, checker.ts:7474).
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>SyntacticNodeBuilder.TryReuseExistingTypeNode</c> is a nullable <c>Func</c> slot holding a
    /// <c>(context, ITypeNode)</c> delegate, so the call is the parent's documented
    /// <c>SyntacticNodeBuilder.TryReuseExistingTypeNode!(context, existing)</c> with the reference's <c>existing</c>
    /// (a <c>Node</c> in the port, an <c>ITypeNode</c> by the guard) converted.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>isJSConstructor(symbol.valueDeclaration)</c> accepts an optional node; the port's
    /// <c>IsJSConstructor</c> takes a <c>Node?</c> while the symbol slot is an <c>IDeclaration?</c>, so the same test is
    /// the null check plus an explicit conversion. The class branch's <c>symbol.valueDeclaration &amp;&amp; …</c> chain
    /// becomes the same null test before the <c>&amp;&amp;</c> chain, and the <c>(Node)</c> conversions there are §3's
    /// class/interface boundary.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>shouldWriteTypeOfFunctionSymbol</c> is a local function, not a member: the reference declares it at
    /// the body level of <c>createAnonymousTypeNode</c> and it reads that frame's <c>symbol</c> and <c>typeId</c>, which
    /// no context parameter carries. Because it sits outside the <c>if (symbol)</c> guard, C# does not carry the
    /// enclosing block's null state into it, so its first statement re-reads the captured local through <c>!</c> —
    /// exactly the narrowing the reference's guard establishes for all its readers.
    /// </para>
    /// </remarks>
    internal ITypeNode CreateAnonymousTypeNode(ObjectType type, NodeBuilderContext context, bool forceClassExpansion = false, bool forceExpansion = false)
    {
        var typeId = type.Id;
        var symbol = type.Symbol;
        if (symbol is not null)
        {
            var isInstantiationExpressionType = (GetObjectFlags(type) & ObjectFlags.InstantiationExpressionType) != 0;
            if (isInstantiationExpressionType)
            {
                var instantiationExpressionType = (InstantiationExpressionType)type;
                var existing = instantiationExpressionType.Node;
                //  instantiationExpressionType.node is unreliable for constituents of unions and intersections.
                // declare const Err: typeof ErrImpl & (<T>() => T);
                // type ErrAlias<U> = typeof Err<U>;
                // declare const e: ErrAlias<number>;
                // ErrAlias<number> = typeof Err<number> = typeof ErrImpl & (<number>() => number)
                // The problem is each constituent of the intersection will be associated with typeof Err<number>
                // And when extracting a type for typeof ErrImpl from typeof Err<number> does not make sense.
                if (existing is not null && NodeTests.IsTypeQueryNode(existing) && ReferenceEquals(GetTypeFromTypeNode(context, (ITypeNode)existing), type))
                {
                    var typeNode = SyntacticNodeBuilder.TryReuseExistingTypeNode!(context, (ITypeNode)existing);
                    if (typeNode is not null)
                    {
                        return typeNode;
                    }
                }
                if (context.VisitedTypes?.Contains(typeId) == true)
                {
                    return CreateElidedInformationPlaceholder(context);
                }
                return VisitAndTransformType(type, t => CreateTypeNodeFromObjectType((ObjectType)t, context), context);
            }
            var isInstanceType = IsClassInstanceSide(type) ? SymbolFlags.Type : SymbolFlags.Value;
            if (symbol.ValueDeclaration is not null && IsJSConstructor((Node)symbol.ValueDeclaration))
            {
                // Instance and static types share the same symbol; only add 'typeof' for the static side.
                return SymbolToTypeNode(symbol, context, isInstanceType);
            }
            // Always use 'typeof T' for type of class, enum, and module objects
            else if (
                !forceExpansion &&
                (((symbol.Flags & SymbolFlags.Class) != 0
                        && !forceClassExpansion
                        && GetBaseTypeVariableOfClass(symbol) is null
                        && !(symbol.ValueDeclaration is not null
                            && Utilities.IsClassLike((Node)symbol.ValueDeclaration)
                            && (context.Flags & NodeBuilderFlags.WriteClassExpressionAsTypeLiteral) != 0
                            && (!NodeTests.IsClassDeclaration((Node)symbol.ValueDeclaration)
                                || IsSymbolAccessible(symbol, context.EnclosingDeclaration, isInstanceType, /*shouldComputeAliasToMarkVisible*/ false).Accessibility != SymbolAccessibility.Accessible)))
                    || (symbol.Flags & (SymbolFlags.Enum | SymbolFlags.ValueModule)) != 0
                    || ShouldWriteTypeOfFunctionSymbol())
            )
            {
                if (ShouldExpandType(type, context))
                {
                    context.Depth += 1;
                }
                else
                {
                    return SymbolToTypeNode(symbol, context, isInstanceType);
                }
            }
            if (context.VisitedTypes?.Contains(typeId) == true)
            {
                // If type is an anonymous type literal in a type alias declaration, use type alias name
                var typeAlias = GetTypeAliasForTypeLiteral(type);
                if (typeAlias is not null)
                {
                    // The specified symbol flags need to be reinterpreted as type flags
                    return SymbolToTypeNode(typeAlias, context, SymbolFlags.Type);
                }
                else
                {
                    return CreateElidedInformationPlaceholder(context);
                }
            }
            else
            {
                return VisitAndTransformType(type, t => CreateTypeNodeFromObjectType((ObjectType)t, context), context);
            }
        }
        else
        {
            // Anonymous types without a symbol are never circular.
            return CreateTypeNodeFromObjectType(type, context);
        }

        bool ShouldWriteTypeOfFunctionSymbol()
        {
            var sym = symbol!;
            var isStaticMethodSymbol = (sym.Flags & SymbolFlags.Method) != 0 && // typeof static method
                Core.Some<IDeclaration>(sym.Declarations, declaration => Utilities.IsStatic((Node)declaration) && !IsLateBindableIndexSignature((Node)Utilities.GetNameOfDeclaration((Node)declaration)!));
            var isNonLocalFunctionSymbol = (sym.Flags & SymbolFlags.Function) != 0 &&
                (sym.Parent is not null || // is exported function symbol
                    Core.Some<IDeclaration>(sym.Declarations, declaration => ((Node)declaration).Parent!.Kind == SyntaxKind.SourceFile || ((Node)declaration).Parent!.Kind == SyntaxKind.ModuleBlock));
            if (isStaticMethodSymbol || isNonLocalFunctionSymbol)
            {
                // typeof is allowed only for static/non local functions
                return ((context.Flags & NodeBuilderFlags.UseTypeOfFunction) != 0 || context.VisitedTypes?.Contains(typeId) == true) && // it is type of the symbol uses itself recursively
                    ((context.Flags & NodeBuilderFlags.UseStructuralFallback) == 0 || IsValueSymbolAccessible(sym, context.EnclosingDeclaration)); // And the build is going to succeed without visibility error or there is no structural fallback allowed
            }
            return false;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // visitAndTransformType (checker.ts:7272-7363)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:7272-7363 — <c>visitAndTransformType</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: §7.5's agent-A list does not name this function, so its signature is the one the reference's call
    /// shapes imply: the generic <c>T extends Type</c> collapses to <see cref="IType"/> and the transform callback
    /// becomes a <c>Func&lt;IType, ITypeNode&gt;</c> whose callers cast to the type their arm proved (§7.5's rule that a
    /// nested function closing over <c>context</c> takes it as a parameter). All five call sites are in
    /// <c>NodeBuilder.Types.cs</c>/<c>NodeBuilder.Members.cs</c>'s band and none crosses to another slice.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>context.enclosingDeclaration &amp;&amp; getNodeLinks(...)</c> is JS truthiness over a possibly-undefined
    /// node, hence the ternary. <c>depth</c> is TS's <c>number | undefined</c>; the port keeps it an <c>int</c> because
    /// every read is inside the <c>id</c> guard that assigns it. TS's object literal for the serialized-type entry has
    /// no C# counterpart, so the port names it (<see cref="SerializedTypeEntry"/>, declared at the foot of this file,
    /// PORT-NOTE'd there) and the boxed tracked symbols are projected into the generated slot's
    /// <c>List&lt;object?&gt;</c>.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>deepCloneOrReuseNode</c>/<c>deepCloneOrReuseNodes</c> stay local functions because the reference
    /// hands both straight to <c>visitEachChildWorker</c> (as the visitor and as the <c>nodesVisitor</c>) and they close
    /// over this frame's <c>context</c>; a member form would need a closure adapter at each of those three arguments.
    /// <c>visitEachChildWorker</c> and <c>visitNodes</c> are <c>visitorPublic.ts</c>'s exports (aliased at
    /// checker.ts:1134) and have no port counterpart yet: they are called through <c>Utilities</c>, the partial that
    /// already absorbed the other cross-cutting public modules (core.ts, scanner.ts, factory/utilitiesPublic.ts), and
    /// are listed as referenced-but-undeclared in this slice's report. The visitor's result type follows the port's
    /// landed convention, <c>Func&lt;Node, object?&gt;</c> for TS's <c>VisitResult</c> union
    /// (<c>NodeFactory.CopyCustomPrologue</c>).
    /// </para>
    /// </remarks>
    internal ITypeNode VisitAndTransformType(IType type, Func<IType, ITypeNode> transform, NodeBuilderContext context)
    {
        var typeId = type.Id;
        var isConstructorObject = (GetObjectFlags(type) & ObjectFlags.Anonymous) != 0 && type.Symbol is not null && (type.Symbol.Flags & SymbolFlags.Class) != 0;
        var id = (GetObjectFlags(type) & ObjectFlags.Reference) != 0 && ((TypeReference)type).Node is not null ? "N" + Utilities.GetNodeId(((TypeReference)type).Node!) :
            (type.Flags & TypeFlags.Conditional) != 0 ? "N" + Utilities.GetNodeId(((ConditionalType)type).Root!.Node!) :
            type.Symbol is not null ? (isConstructorObject ? "+" : "") + Utilities.GetSymbolId(type.Symbol) :
            null;
        // Since instantiations of the same anonymous type have the same symbol, tracking symbols instead
        // of types allows us to catch circular references to instantiations of the same anonymous type
        if (context.VisitedTypes is null)
        {
            context.VisitedTypes = [];
        }
        if (id is not null && context.SymbolDepth is null)
        {
            context.SymbolDepth = new();
        }

        // Don't rely on type cache if we're expand a type, because we need to compute `canIncreaseExpansionDepth`.
        var links = context.MaxExpansionDepth >= 0 ? null : context.EnclosingDeclaration is not null ? GetNodeLinks(context.EnclosingDeclaration) : null;
        var key = $"{GetTypeId(type)}|{(int)context.Flags}|{(int)context.InternalFlags}";
        if (links is not null)
        {
            links.SerializedTypes ??= new();
        }
        var cachedResult = links?.SerializedTypes?.GetValueOrDefault(key);
        if (cachedResult is not null)
        {
            // TODO:: check if we instead store late painted statements associated with this?
            if (cachedResult.TrackedSymbols is not null)
            {
                foreach (var trackedSymbol in cachedResult.TrackedSymbols)
                {
                    if (trackedSymbol is TrackedSymbol tracked)
                    {
                        context.Tracker?.TrackSymbol(tracked.Symbol, tracked.EnclosingDeclaration, tracked.Meaning);
                    }
                }
            }
            if (cachedResult.Truncating == true)
            {
                context.Truncating = true;
            }
            context.ApproximateLength += cachedResult.AddedLength;
            return (ITypeNode)DeepCloneOrReuseNode((Node)cachedResult.Node!);
        }

        var depth = 0;
        if (id is not null)
        {
            depth = context.SymbolDepth!.GetValueOrDefault(id);
            if (depth > 10)
            {
                return CreateElidedInformationPlaceholder(context);
            }
            context.SymbolDepth![id] = depth + 1;
        }
        context.VisitedTypes.Add(typeId);
        var prevTrackedSymbols = context.TrackedSymbols;
        context.TrackedSymbols = null;
        var startLength = context.ApproximateLength;
        var result = transform(type);
        var addedLength = context.ApproximateLength - startLength;
        if (!context.ReportedDiagnostic && !context.EncounteredError)
        {
            links?.SerializedTypes?.Add(key, new SerializedTypeEntry
            {
                Node = result,
                Truncating = context.Truncating,
                AddedLength = addedLength,
                TrackedSymbols = context.TrackedSymbols?.Cast<object?>().ToList(),
            });
        }
        context.VisitedTypes.Remove(typeId);
        if (id is not null)
        {
            context.SymbolDepth![id] = depth;
        }
        context.TrackedSymbols = prevTrackedSymbols;
        return result;

        // PORT-NOTE: TS's <c>deepCloneOrReuseNode&lt;T extends Node&gt;(node: T): T</c> erases to the non-generic
        // <c>Node</c>-to-<c>Node</c> form the visitor callback needs; the one caller that wants an <c>ITypeNode</c>
        // (the cached-entry path above) converts the result, which TS gets from the generic inference.
        Node DeepCloneOrReuseNode(Node node)
        {
            if (!Utilities.NodeIsSynthesized(node) && ReferenceEquals(Utilities.GetParseTreeNode(node), node))
            {
                return node;
            }
        // PORT-NOTE: Utilities.VisitEachChildWorker is the loud, named M7 blocker for visitorPublic.ts's
        // visitEachChild (aliased at checker.ts:1134); its owner is the visitorPublic.ts slice (Layer 6/M7).
            return SetTextRange(context, _factory.CloneNode(Utilities.VisitEachChildWorker(node, n => DeepCloneOrReuseNode(n), /*context*/ null, DeepCloneOrReuseNodes, n => DeepCloneOrReuseNode(n))), node);
        }

        // PORT-NOTE: the parameter types follow the visitor convention the port already uses for a visitor's result
        // (<c>Func&lt;Node, object?&gt;</c>); <c>visitNodes</c>'s first parameter is a <c>NodeArray&lt;Node&gt;</c>, the
        // concrete face, so the empty-array copy can read <c>hasTrailingComma</c> off it directly.
        NodeArray<Node>? DeepCloneOrReuseNodes(NodeArray<Node>? nodes, Func<Node, object?> visitor, Func<Node, bool>? test, int? start, int? count)
        {
            if (nodes is not null && nodes.Count == 0)
            {
                // Ensure we explicitly make a copy of an empty array; visitNodes will not do this unless the array has elements,
                // which can lead to us reusing the same empty NodeArray more than once within the same AST during type noding.
                return Utilities.SetTextRange(_factory.CreateNodeArray<Node>(/*elements*/ null, nodes.HasTrailingComma), nodes);
            }
            return Utilities.VisitNodes(nodes, visitor, test, start, count);
        }
    }
}

// ported from: src/compiler/checker.ts:7337-7342 — the `serializedTypes` entry `visitAndTransformType` stores.
/// <summary>
/// One cached result of <c>visitAndTransformType</c>: the node the transform produced, whether the build was truncating
/// while it did, how much it added to the context's approximate length, and the symbols it had to name.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS writes this shape as an object literal against the <c>SerializedTypeEntry</c> interface
/// (<c>types.ts</c>), so the generator emitted the interface (<c>Model/NodeInterfaces.cs:2997</c>, reachable through
/// <c>NodeLinks.SerializedTypes</c>) and no constructible type. The port's precedent for a model object literal is a
/// record beside its interface (<c>Model/TrackedSymbol.cs</c>); this one is declared here because its only producer is
/// <see cref="Checker.VisitAndTransformType"/> and this slice may create no other file — the integration pass may move
/// it to <c>Model/NodeLinks.cs</c> where its slot lives.
/// </remarks>
internal sealed class SerializedTypeEntry : ISerializedTypeEntry
{
    /// <summary>ported from: src/compiler/types.ts:2972 — <c>SerializedTypeEntry.node</c>.</summary>
    public ITypeNode? Node { get; set; }

    /// <summary>ported from: src/compiler/types.ts:2973 — <c>SerializedTypeEntry.truncating</c>.</summary>
    public bool? Truncating { get; set; }

    /// <summary>ported from: src/compiler/types.ts:2974 — <c>SerializedTypeEntry.addedLength</c>.</summary>
    public int AddedLength { get; set; }

    /// <summary>ported from: src/compiler/types.ts:2975 — <c>SerializedTypeEntry.trackedSymbols</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the generated slot is <c>List&lt;object?&gt;?</c> for TS's <c>TrackedSymbol[]</c> tuple array
    /// (<c>Model/TrackedSymbol.cs</c> states the projection); the writer boxes the context's records into it and the
    /// reader unboxes them one by one.
    /// </remarks>
    public List<object?>? TrackedSymbols { get; set; }
}
