namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:19733-20537 (mapped types, conditional types, spread types, literal types and
//              the type-node dispatch)
// ported from: src/compiler/checker.ts:2052-2055 (the four literal-type maps)
/// <summary>
/// The type-node entry points and what they build: <c>getTypeFromTypeNode</c> and its worker's per-kind dispatch, the
/// mapped-type and conditional-type constructors, the spread-type merge, and the literal-type factories with the four
/// caches they are the only readers of.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: the four literal-type maps of the state block (checker.ts:2052-2055) are declared here. Their only
/// readers are the four factories below, and the state block itself is outside every M6 slice's range, so the slice
/// that reads a cache declares it (the same reasoning <c>Checker.Types.cs</c> uses for the type slots). The rest of
/// that block belongs to the slices that read it: <c>_tupleTypes</c>/<c>_substitutionTypes</c> to M6TypeRefs,
/// <c>_indexedAccessTypes</c>/<c>_templateLiteralTypes</c>/<c>_stringMappingTypes</c> to M6IndexAccess and the
/// union/intersection family to M6Unions.
/// </para>
/// <para>
/// PORT-NOTE: node parameters follow the model's mapping (§12.9): TS's <c>TypeNode</c> is <see cref="ITypeNode"/>; a
/// union of node classes is the capability interface they all implement (<c>getTypeFromTypeReference</c>,
/// <c>getTypeFromArrayOrTupleTypeNode</c>) or, when they share none, <c>Node</c> (<c>getTypeFromThisTypeNode</c>,
/// <c>getIdentifierChain</c>); a member only one member of such a union declares is read after a <c>Kind</c> test and
/// an explicit cast, exactly where TS's structural typing supplied the member for free.
/// </para>
/// <para>
/// PORT-NOTE: the generated node/type slots are all nullable while TS declares most of them required, so a slot the
/// reference dereferences directly is stated with <c>!</c> — the same handling the M5 binder uses for
/// <c>symbol.EscapedName!</c> — rather than with <c>Debug.CheckDefined</c>, which is kept for the two places where the
/// value is not immediately dereferenced and the invariant is the point of the code
/// (<see cref="GetTypeFromTypeNode"/>, and <c>CreateLiteralType</c>'s <c>symbol!</c> which the reference writes
/// itself).
/// </para>
/// <para>
/// PORT-NOTE: <c>getConditionalType</c>'s inner <c>canTailRecurse</c> is a local function. It mutates the enclosing
/// <c>root</c>/<c>mapper</c>/<c>aliasSymbol</c>/<c>aliasTypeArguments</c>/<c>tailCount</c> — C# allows a local function
/// to capture and mutate a parameter, so the reference's control flow (including the call before the declaration) is
/// preserved verbatim.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // The four literal-type maps (checker.ts:2052-2055)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:2052 — <c>stringLiteralTypes</c>.</summary>
    internal readonly Dictionary<string, IStringLiteralType> _stringLiteralTypes = [];

    /// <summary>ported from: src/compiler/checker.ts:2053 — <c>numberLiteralTypes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS keys this map by the number itself, so the port's key is <c>double</c> (TS's <c>number</c> is a
    /// double). <see cref="Dictionary{TKey,TValue}"/> compares keys with <c>Double.Equals</c>, which — like the JS
    /// <c>Map</c>'s SameValueZero — answers one key for <c>-0.0</c> and <c>0.0</c>. (It would separate two <c>NaN</c>
    /// keys where SameValueZero unifies them, but no caller reaches these factories with a NaN: the values are literal
    /// node values and enum member values.)
    /// </remarks>
    internal readonly Dictionary<double, INumberLiteralType> _numberLiteralTypes = [];

    /// <summary>ported from: src/compiler/checker.ts:2054 — <c>bigIntLiteralTypes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS keys this by <c>pseudoBigIntToString(value)</c>, i.e. a string; <see cref="IPseudoBigInt"/> is not
    /// a value type in the port, so the string key is what the factory computes below.
    /// </remarks>
    internal readonly Dictionary<string, IBigIntLiteralType> _bigIntLiteralTypes = [];

    /// <summary>ported from: src/compiler/checker.ts:2055 — <c>enumLiteralTypes</c>.</summary>
    internal readonly Dictionary<string, ILiteralType> _enumLiteralTypes = [];

    // ---------------------------------------------------------------------------------------------
    // Mapped types (checker.ts:19733-19747)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:19733-19746 — <c>getTypeFromMappedTypeNode</c>.</summary>
    internal IType GetTypeFromMappedTypeNode(MappedTypeNode node)
    {
        var links = GetNodeLinks(node);
        if (links.ResolvedType is null)
        {
            var type = (MappedType)CreateObjectType(ObjectFlags.Mapped, node.Symbol);
            type.Declaration = node;
            type.AliasSymbol = GetAliasSymbolForTypeNode(node);
            type.AliasTypeArguments = GetTypeArgumentsForAliasSymbol(type.AliasSymbol);
            links.ResolvedType = type;
            // Eagerly resolve the constraint type which forces an error if the constraint type circularly
            // references itself through one or more type aliases.
            GetConstraintTypeFromMappedType(type);
        }

        return links.ResolvedType;
    }

    // ---------------------------------------------------------------------------------------------
    // The two type predicates the conditional-type resolution reads (checker.ts:19748-19770)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:19748-19761 — <c>getActualTypeVariable</c>.</summary>
    internal IType GetActualTypeVariable(IType type)
    {
        if ((type.Flags & TypeFlags.Substitution) != 0)
        {
            return GetActualTypeVariable(((SubstitutionType)type).BaseType!);
        }

        if ((type.Flags & TypeFlags.IndexedAccess) != 0 &&
            ((((IndexedAccessType)type).ObjectType!.Flags & TypeFlags.Substitution) != 0 ||
             (((IndexedAccessType)type).IndexType!.Flags & TypeFlags.Substitution) != 0))
        {
            return GetIndexedAccessType(
                GetActualTypeVariable(((IndexedAccessType)type).ObjectType!),
                GetActualTypeVariable(((IndexedAccessType)type).IndexType!));
        }

        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:19763-19766 — <c>isSimpleTupleType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>isNamedTupleMember(e) &amp;&amp; !!(e.questionToken || e.dotDotDotToken)</c> is a guard plus the
    /// truthiness of a node; the port keeps the guard as <see cref="NodeTests.IsNamedTupleMember"/> and reads the two
    /// tokens through a pattern test, which is the same <c>Kind</c> test the reference's guard performs.
    /// </remarks>
    internal bool IsSimpleTupleType(ITypeNode node)
    {
        var tupleNode = (Node)node;
        return NodeTests.IsTupleTypeNode(tupleNode) && Core.Length(((TupleTypeNode)tupleNode).Elements) > 0 &&
            !Core.Some<Node>(((TupleTypeNode)tupleNode).Elements, e =>
                NodeTests.IsOptionalTypeNode(e) || NodeTests.IsRestTypeNode(e) ||
                e is NamedTupleMember { QuestionToken: not null } or NamedTupleMember { DotDotDotToken: not null });
    }

    /// <summary>ported from: src/compiler/checker.ts:19768-19770 — <c>isDeferredType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>isTupleType</c> narrows <c>type</c> to <c>TupleTypeReference</c> upstream; C# cannot narrow through
    /// a predicate, so <c>getElementTypes</c> takes the cast the guard proved (§12.9).
    /// </remarks>
    internal bool IsDeferredType(IType type, bool checkTuples)
    {
        return IsGenericType(type) ||
            checkTuples && IsTupleType(type) && Core.Some(GetElementTypes((ITupleTypeReference)type), t => IsGenericType(t));
    }

    // ---------------------------------------------------------------------------------------------
    // Conditional types (checker.ts:19772-19950)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:19772-19919 — <c>getConditionalType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>let result;</c> is implicitly <c>any</c>; the port declares the <c>IType result</c> every arm
    /// assigns before its <c>break</c>, which is what makes the definite-assignment analysis accept the loop's exit.
    /// <c>combinedMapper || mapper</c> and <c>extraTypes || (extraTypes = [])</c> are object truthiness, i.e. <c>??</c>
    /// and <c>??=</c>. <c>append(extraTypes, result)</c> keeps <c>Core.Append</c>'s in-place push, i.e. the same array
    /// the reference extends. The instantiation caches of <c>root.instantiations</c> are the reference's own, keyed by
    /// <c>getTypeListId</c>.
    /// </remarks>
    internal IType GetConditionalType(
        IConditionalRoot root,
        TypeMapper? mapper,
        bool forConstraint,
        ISymbol? aliasSymbol = null,
        List<IType>? aliasTypeArguments = null)
    {
        IType result;
        List<IType>? extraTypes = null;
        var tailCount = 0;
        // We loop here for an immediately nested conditional type in the false position, effectively treating
        // types of the form 'A extends B ? X : C extends D ? Y : E extends F ? Z : ...' as a single construct for
        // purposes of resolution. We also loop here when resolution of a conditional type ends in resolution of
        // another (or, through recursion, possibly the same) conditional type. In the potentially tail-recursive
        // cases we increment the tail recursion counter and stop after 1000 iterations.
        while (true)
        {
            if (tailCount == 1000)
            {
                Error(_currentNode, Diagnostics.Type_instantiation_is_excessively_deep_and_possibly_infinite);
                return _errorType;
            }

            var checkType = InstantiateType(GetActualTypeVariable(root.CheckType!), mapper)!;
            var extendsType = InstantiateType(root.ExtendsType!, mapper)!;
            if (ReferenceEquals(checkType, _errorType) || ReferenceEquals(extendsType, _errorType))
            {
                return _errorType;
            }

            if (ReferenceEquals(checkType, _wildcardType) || ReferenceEquals(extendsType, _wildcardType))
            {
                return _wildcardType;
            }

            var node = root.Node!;
            var checkTypeNode = Utilities.SkipTypeParentheses(node.CheckType!);
            var extendsTypeNode = Utilities.SkipTypeParentheses(node.ExtendsType!);
            // When the check and extends types are simple tuple types of the same arity, we defer resolution of the
            // conditional type when any tuple elements are generic. This is such that non-distributable conditional
            // types can be written `[X] extends [Y] ? ...` and be deferred similarly to `X extends Y ? ...`.
            var checkTuples = IsSimpleTupleType(checkTypeNode) && IsSimpleTupleType(extendsTypeNode) &&
                Core.Length(((TupleTypeNode)checkTypeNode).Elements) == Core.Length(((TupleTypeNode)extendsTypeNode).Elements);
            var checkTypeDeferred = IsDeferredType(checkType, checkTuples);
            TypeMapper? combinedMapper = null;
            if (root.InferTypeParameters is not null)
            {
                // When we're looking at making an inference for an infer type, when we get its constraint, it'll automagically be
                // instantiated with the context, so it doesn't need the mapper for the inference context - however the constraint
                // may refer to another _root_, _uncloned_ `infer` type parameter [1], or to something mapped by `mapper` [2].
                // [1] Eg, if we have `Foo<T, U extends T>` and `Foo<number, infer B>` - `B` is constrained to `T`, which, in turn, has been instantiated
                // as `number`
                // Conversely, if we have `Foo<infer A, infer B>`, `B` is still constrained to `T` and `T` is instantiated as `A`
                // [2] Eg, if we have `Foo<T, U extends T>` and `Foo<Q, infer B>` where `Q` is mapped by `mapper` into `number` - `B` is constrained to `T`
                // which is in turn instantiated as `Q`, which is in turn instantiated as `number`.
                // So we need to:
                //    * combine `context.nonFixingMapper` with `mapper` so their constraints can be instantiated in the context of `mapper` (otherwise they'd only get inference context information)
                //    * incorporate all of the component mappers into the combined mapper for the true and false members
                // This means we have two mappers that need applying:
                //    * The original `mapper` used to create this conditional
                //    * The mapper that maps the infer type parameter to its inference result (`context.mapper`)
                var context = CreateInferenceContext(root.InferTypeParameters, /*signature*/ null, InferenceFlags.None);
                if (mapper is not null)
                {
                    context.NonFixingMapper = CombineTypeMappers((TypeMapper?)context.NonFixingMapper, mapper);
                }

                if (!checkTypeDeferred)
                {
                    // We don't want inferences from constraints as they may cause us to eagerly resolve the
                    // conditional type instead of deferring resolution. Also, we always want strict function
                    // types rules (i.e. proper contravariance) for inferences.
                    InferTypes(context.Inferences!, checkType, extendsType, InferencePriority.NoConstraints | InferencePriority.AlwaysStrict);
                }

                // It's possible for 'infer T' type paramteters to be given uninstantiated constraints when the
                // those type parameters are used in type references (see getInferredTypeParameterConstraint). For
                // that reason we need context.mapper to be first in the combined mapper. See #42636 for examples.
                combinedMapper = mapper is not null
                    ? CombineTypeMappers((TypeMapper?)context.Mapper, mapper)
                    : (TypeMapper?)context.Mapper;
            }

            // Instantiate the extends type including inferences for 'infer T' type parameters
            var inferredExtendsType = combinedMapper is not null
                ? InstantiateType(root.ExtendsType!, combinedMapper)!
                : extendsType;
            // We attempt to resolve the conditional type only when the check and extends types are non-generic
            if (!checkTypeDeferred && !IsDeferredType(inferredExtendsType, checkTuples))
            {
                // Return falseType for a definitely false extends check. We check an instantiations of the two
                // types with type parameters mapped to the wildcard type, the most permissive instantiations
                // possible (the wildcard type is assignable to and from all types). If those are not related,
                // then no instantiations will be and we can just return the false branch type.
                if ((inferredExtendsType.Flags & TypeFlags.AnyOrUnknown) == 0 &&
                    ((checkType.Flags & TypeFlags.Any) != 0 ||
                     !IsTypeAssignableTo(GetPermissiveInstantiation(checkType), GetPermissiveInstantiation(inferredExtendsType))))
                {
                    // Return union of trueType and falseType for 'any' since it matches anything. Furthermore, for a
                    // distributive conditional type applied to the constraint of a type variable, include trueType if
                    // there are possible values of the check type that are also possible values of the extends type.
                    // We use a reverse assignability check as it is less expensive than the comparable relationship
                    // and avoids false positives of a non-empty intersection check.
                    if ((checkType.Flags & TypeFlags.Any) != 0 ||
                        forConstraint && (inferredExtendsType.Flags & TypeFlags.Never) == 0 &&
                        SomeType(GetPermissiveInstantiation(inferredExtendsType), t => IsTypeAssignableTo(t, GetPermissiveInstantiation(checkType))))
                    {
                        (extraTypes ??= []).Add(InstantiateType(GetTypeFromTypeNode(node.TrueType), combinedMapper ?? mapper)!);
                    }

                    // If falseType is an immediately nested conditional type that isn't distributive or has an
                    // identical checkType, switch to that type and loop.
                    var falseType = GetTypeFromTypeNode(node.FalseType);
                    if ((falseType.Flags & TypeFlags.Conditional) != 0)
                    {
                        var newRoot = ((ConditionalType)falseType).Root!;
                        if (ReferenceEquals(newRoot.Node!.Parent, root.Node) && (!newRoot.IsDistributive || ReferenceEquals(newRoot.CheckType, root.CheckType)))
                        {
                            root = newRoot;
                            continue;
                        }

                        if (CanTailRecurse(falseType, mapper))
                        {
                            continue;
                        }
                    }

                    result = InstantiateType(falseType, mapper)!;
                    break;
                }

                // Return trueType for a definitely true extends check. We check instantiations of the two
                // types with type parameters mapped to their restrictive form, i.e. a form of the type parameter
                // that has no constraint. This ensures that, for example, the type
                //   type Foo<T extends { x: any }> = T extends { x: string } ? string : number
                // doesn't immediately resolve to 'string' instead of being deferred.
                if ((inferredExtendsType.Flags & TypeFlags.AnyOrUnknown) != 0 ||
                    IsTypeAssignableTo(GetRestrictiveInstantiation(checkType), GetRestrictiveInstantiation(inferredExtendsType)))
                {
                    var trueType = GetTypeFromTypeNode(node.TrueType);
                    var trueMapper = combinedMapper ?? mapper;
                    if (CanTailRecurse(trueType, trueMapper))
                    {
                        continue;
                    }

                    result = InstantiateType(trueType, trueMapper)!;
                    break;
                }
            }

            // Return a deferred type for a check that is neither definitely true nor definitely false
            var deferred = (ConditionalType)CreateType(TypeFlags.Conditional);
            deferred.Root = root;
            deferred.CheckType = InstantiateType(root.CheckType!, mapper);
            deferred.ExtendsType = InstantiateType(root.ExtendsType!, mapper);
            deferred.Mapper = mapper;
            deferred.CombinedMapper = combinedMapper;
            deferred.AliasSymbol = aliasSymbol ?? root.AliasSymbol;
            deferred.AliasTypeArguments = aliasSymbol is not null ? aliasTypeArguments : InstantiateTypes(root.AliasTypeArguments, mapper!); // TODO: GH#18217
            result = deferred;
            break;
        }

        return extraTypes is not null ? GetUnionType((List<IType>)Core.Append(extraTypes, result)!) : result;

        // We tail-recurse for generic conditional types that (a) have not already been evaluated and cached, and
        // (b) are non distributive, have a check type that is unaffected by instantiation, or have a non-union check
        // type. Note that recursion is possible only through aliased conditional types, so we only increment the tail
        // recursion counter for those.
        bool CanTailRecurse(IType newType, TypeMapper? newMapper)
        {
            if ((newType.Flags & TypeFlags.Conditional) != 0 && newMapper is not null)
            {
                var newRoot = ((ConditionalType)newType).Root!;
                if (newRoot.OuterTypeParameters is not null)
                {
                    var typeParamMapper = CombineTypeMappers((TypeMapper?)((ConditionalType)newType).Mapper, newMapper);
                    var typeArguments = Core.Map(newRoot.OuterTypeParameters, (t, _) => GetMappedType(t, typeParamMapper))!;
                    var newRootMapper = CreateTypeMapper(newRoot.OuterTypeParameters, typeArguments);
                    var newCheckType = newRoot.IsDistributive ? GetMappedType(newRoot.CheckType!, newRootMapper) : null;
                    if (newCheckType is null || ReferenceEquals(newCheckType, newRoot.CheckType) || (newCheckType.Flags & (TypeFlags.Union | TypeFlags.Never)) == 0)
                    {
                        root = newRoot;
                        mapper = newRootMapper;
                        aliasSymbol = null;
                        aliasTypeArguments = null;
                        if (newRoot.AliasSymbol is not null)
                        {
                            tailCount++;
                        }

                        return true;
                    }
                }
            }

            return false;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:19921-19923 — <c>getTrueTypeFromConditionalType</c>.</summary>
    internal IType GetTrueTypeFromConditionalType(IConditionalType type)
        => type.ResolvedTrueType ??= InstantiateType(GetTypeFromTypeNode(type.Root!.Node!.TrueType), (TypeMapper?)type.Mapper)!;

    /// <summary>ported from: src/compiler/checker.ts:19925-19927 — <c>getFalseTypeFromConditionalType</c>.</summary>
    internal IType GetFalseTypeFromConditionalType(IConditionalType type)
        => type.ResolvedFalseType ??= InstantiateType(GetTypeFromTypeNode(type.Root!.Node!.FalseType), (TypeMapper?)type.Mapper)!;

    /// <summary>ported from: src/compiler/checker.ts:19929-19931 — <c>getInferredTrueTypeFromConditionalType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>type.combinedMapper ? … : …</c> is a null test on the mapper (an object, hence truthy), so the port
    /// reads the slot as <c>object?</c> and casts it, as every mapper slot is read.
    /// </remarks>
    internal IType GetInferredTrueTypeFromConditionalType(IConditionalType type)
        => type.ResolvedInferredTrueType ??= type.CombinedMapper is not null
            ? InstantiateType(GetTypeFromTypeNode(type.Root!.Node!.TrueType), (TypeMapper)type.CombinedMapper)!
            : GetTrueTypeFromConditionalType(type);

    /// <summary>ported from: src/compiler/checker.ts:19933-19943 — <c>getInferTypeParameters</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>append(result, getDeclaredTypeOfSymbol(symbol))</c> relies on array covariance (the value is a
    /// <c>Type</c>, the array a <c>TypeParameter[]</c>); the port states the downcast the guard
    /// (<c>SymbolFlags.TypeParameter</c>) proves, and <c>SymbolTable.ForEach</c> takes the key as its second argument.
    /// </remarks>
    internal List<ITypeParameter>? GetInferTypeParameters(ConditionalTypeNode node)
    {
        List<ITypeParameter>? result = null;
        if (node.Locals is not null)
        {
            node.Locals.ForEach((symbol, _) =>
            {
                if ((symbol.Flags & SymbolFlags.TypeParameter) != 0)
                {
                    result = (List<ITypeParameter>)Core.Append(result, (ITypeParameter)GetDeclaredTypeOfSymbol(symbol))!;
                }
            });
        }

        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:19945-19950 — <c>isDistributionDependent</c>.</summary>
    /// <remarks>PORT-NOTE: <c>root.checkType as TypeParameter</c> is the cast the distributive-root invariant implies.</remarks>
    internal bool IsDistributionDependent(IConditionalRoot root)
    {
        var node = root.Node!;
        return root.IsDistributive && (
            IsTypeParameterPossiblyReferenced((ITypeParameter)root.CheckType!, (Node)node.TrueType!) ||
            IsTypeParameterPossiblyReferenced((ITypeParameter)root.CheckType!, (Node)node.FalseType!));
    }

    /// <summary>ported from: src/compiler/checker.ts:19952-19978 — <c>getTypeFromConditionalTypeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>getTypeListId</c> takes <c>Type[]</c> while <c>outerTypeParameters</c> is a <c>TypeParameter[]</c>;
    /// TS relies on array covariance, so the port maps the list to its element type (the ids are all the id reads need).
    /// The instantiation cache is created and seeded in the reference's order, which is what makes a conditional type
    /// with outer type parameters reachable through <c>getConditionalTypeInstantiation</c> (M6Generics).
    /// </remarks>
    internal IType GetTypeFromConditionalTypeNode(ConditionalTypeNode node)
    {
        var links = GetNodeLinks(node);
        if (links.ResolvedType is null)
        {
            var checkType = GetTypeFromTypeNode(node.CheckType);
            var aliasSymbol = GetAliasSymbolForTypeNode(node);
            var aliasTypeArguments = GetTypeArgumentsForAliasSymbol(aliasSymbol);
            var allOuterTypeParameters = GetOuterTypeParameters(node, /*includeThisTypes*/ true);
            var outerTypeParameters = aliasTypeArguments is not null
                ? allOuterTypeParameters
                : Core.Filter(allOuterTypeParameters, tp => IsTypeParameterPossiblyReferenced(tp, node)) as List<ITypeParameter>;
            var root = new ConditionalRoot
            {
                Node = node,
                CheckType = checkType,
                ExtendsType = GetTypeFromTypeNode(node.ExtendsType),
                IsDistributive = (checkType.Flags & TypeFlags.TypeParameter) != 0,
                InferTypeParameters = GetInferTypeParameters(node),
                OuterTypeParameters = outerTypeParameters,
                Instantiations = null,
                AliasSymbol = aliasSymbol,
                AliasTypeArguments = aliasTypeArguments,
            };
            links.ResolvedType = GetConditionalType(root, /*mapper*/ null, /*forConstraint*/ false);
            if (outerTypeParameters is not null)
            {
                root.Instantiations = [];
                root.Instantiations[GetTypeListId(Core.Map(outerTypeParameters, (tp, _) => (IType)tp)!)] = links.ResolvedType;
            }
        }

        return links.ResolvedType;
    }

    /// <summary>ported from: src/compiler/checker.ts:19980-19986 — <c>getTypeFromInferTypeNode</c>.</summary>
    internal IType GetTypeFromInferTypeNode(InferTypeNode node)
    {
        var links = GetNodeLinks(node);
        if (links.ResolvedType is null)
        {
            links.ResolvedType = GetDeclaredTypeOfTypeParameter(GetSymbolOfDeclaration(node.TypeParameter!));
        }

        return links.ResolvedType;
    }

    // ---------------------------------------------------------------------------------------------
    // Import types (checker.ts:19988-20067)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:19988-19995 — <c>getIdentifierChain</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>EntityName</c> is <c>Identifier | QualifiedName</c>, which share no C# class, so the parameter
    /// is <c>Node</c> and the non-identifier arm reads the qualified name's two halves through the cast the
    /// <c>isIdentifier</c> test leaves. <c>append</c> keeps <c>Core.Append</c>'s in-place push.
    /// </remarks>
    internal List<Identifier> GetIdentifierChain(Node node)
    {
        if (NodeTests.IsIdentifier(node))
        {
            return [(Identifier)node];
        }

        var qualifiedName = (QualifiedName)node;
        return (List<Identifier>)Core.Append(GetIdentifierChain(qualifiedName.Left!), qualifiedName.Right!)!;
    }

    /// <summary>ported from: src/compiler/checker.ts:19997-20056 — <c>getTypeFromImportTypeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>isLiteralImportTypeNode</c> narrows the argument to <c>LiteralTypeNode &amp; { literal: StringLiteral }</c>
    /// upstream; C# cannot narrow through a predicate, so the argument and its literal are read through the two casts
    /// that guard proves. <c>nameStack.shift()</c> is stated as an index loop (the stack is only read here), so the
    /// reference's <c>nameStack.length</c> test — the length <em>after</em> the shift — is <c>i + 1 &lt; nameStack.Count</c>.
    /// <c>!!exports?.get(...)</c> is a non-null test on the looked-up symbol, and the escape-equals flag is JS-file
    /// only.
    /// </remarks>
    internal IType GetTypeFromImportTypeNode(ImportTypeNode node)
    {
        var links = GetNodeLinks(node);
        if (links.ResolvedType is null)
        {
            if (!Utilities.IsLiteralImportTypeNode(node))
            {
                Error(node.Argument as Node, Diagnostics.String_literal_expected);
                links.ResolvedSymbol = _unknownSymbol;
                return links.ResolvedType = _errorType;
            }

            var targetMeaning = node.IsTypeOf
                ? SymbolFlags.Value
                : (node.Flags & NodeFlags.JSDoc) != 0 ? SymbolFlags.Value | SymbolFlags.Type : SymbolFlags.Type;
            // TODO: Future work: support unions/generics/whatever via a deferred import-type
            var innerModuleSymbol = ResolveExternalModuleName(node, ((LiteralTypeNode)node.Argument!).Literal!);
            if (innerModuleSymbol is null)
            {
                links.ResolvedSymbol = _unknownSymbol;
                return links.ResolvedType = _errorType;
            }

            var isExportEquals = innerModuleSymbol.Exports is not null && innerModuleSymbol.Exports.ContainsKey(InternalSymbolName.ExportEquals.Value);
            var moduleSymbol = ResolveExternalModuleSymbol(innerModuleSymbol, /*dontResolveAlias*/ false)!;
            if (!Utilities.NodeIsMissing(node.Qualifier))
            {
                var nameStack = GetIdentifierChain(node.Qualifier!);
                var currentNamespace = moduleSymbol;
                for (var i = 0; i < nameStack.Count; i++)
                {
                    var current = nameStack[i];
                    var meaning = i + 1 < nameStack.Count ? SymbolFlags.Namespace : targetMeaning;
                    // typeof a.b.c is normally resolved using `checkExpression` which in turn defers to `checkQualifiedName`
                    // That, in turn, ultimately uses `getPropertyOfType` on the type of the symbol, which differs slightly from
                    // the `exports` lookup process that only looks up namespace members which is used for most type references
                    var mergedResolvedSymbol = GetMergedSymbol(ResolveSymbol(currentNamespace))!;
                    var symbolFromVariable = node.IsTypeOf || Utilities.IsInJSFile(node) && isExportEquals
                        ? GetPropertyOfType(GetTypeOfSymbol(mergedResolvedSymbol), current.EscapedText!, /*skipObjectFunctionPropertyAugment*/ false, /*includeTypeOnlyMembers*/ true)
                        : null;
                    var symbolFromModule = node.IsTypeOf ? null : GetSymbol(GetExportsOfSymbol(mergedResolvedSymbol), current.EscapedText!, meaning);
                    var next = symbolFromModule ?? symbolFromVariable;
                    if (next is null)
                    {
                        Error(current, Diagnostics.Namespace_0_has_no_exported_member_1, GetFullyQualifiedName(currentNamespace), Utilities.DeclarationNameToString(current));
                        return links.ResolvedType = _errorType;
                    }

                    GetNodeLinks(current).ResolvedSymbol = next;
                    GetNodeLinks(current.Parent!).ResolvedSymbol = next;
                    currentNamespace = next;
                }

                links.ResolvedType = ResolveImportSymbolType(node, links, currentNamespace, targetMeaning);
            }
            else
            {
                if ((moduleSymbol.Flags & targetMeaning) != 0)
                {
                    links.ResolvedType = ResolveImportSymbolType(node, links, moduleSymbol, targetMeaning);
                }
                else
                {
                    var errorMessage = targetMeaning == SymbolFlags.Value
                        ? Diagnostics.Module_0_does_not_refer_to_a_value_but_is_used_as_a_value_here
                        : Diagnostics.Module_0_does_not_refer_to_a_type_but_is_used_as_a_type_here_Did_you_mean_typeof_import_0;

                    Error(node, errorMessage, ((StringLiteral)((LiteralTypeNode)node.Argument!).Literal!).Text);

                    links.ResolvedSymbol = _unknownSymbol;
                    links.ResolvedType = _errorType;
                }
            }
        }

        return links.ResolvedType;
    }

    /// <summary>ported from: src/compiler/checker.ts:20058-20067 — <c>resolveImportSymbolType</c>.</summary>
    internal IType ResolveImportSymbolType(ImportTypeNode node, NodeLinks links, ISymbol symbol, SymbolFlags meaning)
    {
        var resolvedSymbol = ResolveSymbol(symbol);
        links.ResolvedSymbol = resolvedSymbol;
        if (meaning == SymbolFlags.Value)
        {
            return GetInstantiationExpressionType(GetTypeOfSymbol(symbol), node); // intentionally doesn't use resolved symbol so type is cached as expected on the alias
        }

        return GetTypeReferenceType(node, resolvedSymbol!); // getTypeReferenceType doesn't handle aliases - it must get the resolved symbol
    }

    // ---------------------------------------------------------------------------------------------
    // Anonymous types, spread types and their index infos (checker.ts:20069-20274)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:20069-20088 — <c>getTypeFromTypeLiteralOrFunctionOrConstructorTypeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the parameter is TS's union of five node classes, all of which are type nodes, so the port's parameter
    /// is <see cref="ITypeNode"/> and the declaration face is read through <see cref="IDeclaration"/> (every member of
    /// the union is one). <c>type = createArrayType(type)</c> assigns a <c>TypeReference</c> (an object type) back to the
    /// <c>ObjectType</c> local, which C# needs spelled as the downcast.
    /// </remarks>
    internal IType GetTypeFromTypeLiteralOrFunctionOrConstructorTypeNode(ITypeNode node)
    {
        var nodeAsNode = (Node)node;
        var links = GetNodeLinks(nodeAsNode);
        if (links.ResolvedType is null)
        {
            // Deferred resolution of members is handled by resolveObjectTypeMembers
            var aliasSymbol = GetAliasSymbolForTypeNode(nodeAsNode);
            var symbol = ((IDeclaration)node).Symbol;
            if (symbol is null || GetMembersOfSymbol(symbol).Count == 0 && aliasSymbol is null)
            {
                links.ResolvedType = _emptyTypeLiteralType;
            }
            else
            {
                var type = CreateObjectType(ObjectFlags.Anonymous, symbol);
                type.AliasSymbol = aliasSymbol;
                type.AliasTypeArguments = GetTypeArgumentsForAliasSymbol(aliasSymbol);
                if (NodeTests.IsJSDocTypeLiteral(nodeAsNode) && ((JSDocTypeLiteral)nodeAsNode).IsArrayType)
                {
                    type = (ObjectType)CreateArrayType(type);
                }

                links.ResolvedType = type;
            }
        }

        return links.ResolvedType;
    }

    /// <summary>ported from: src/compiler/checker.ts:20090-20096 — <c>getAliasSymbolForTypeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>isTypeAlias</c> narrows the host to an alias-like declaration upstream; the port keeps the
    /// predicate as <see cref="Utilities.IsTypeAlias"/>'s <c>bool</c> and passes the host to
    /// <c>getSymbolOfDeclaration</c>'s <see cref="IDeclaration"/> parameter with the cast that predicate implies.
    /// </remarks>
    internal ISymbol? GetAliasSymbolForTypeNode(Node node)
    {
        var host = node.Parent!;
        while (NodeTests.IsParenthesizedTypeNode(host) || NodeTests.IsJSDocTypeExpression(host) ||
            NodeTests.IsTypeOperatorNode(host) && ((TypeOperatorNode)host).Operator == SyntaxKind.ReadonlyKeyword)
        {
            host = host.Parent!;
        }

        return Utilities.IsTypeAlias(host) ? GetSymbolOfDeclaration((IDeclaration)host) : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:20098-20100 — <c>getTypeArgumentsForAliasSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>getLocalTypeParametersOfClassOrInterfaceOrTypeAlias</c> answers <c>TypeParameter[]</c> and its
    /// result is used everywhere as the <c>Type[]</c> it is (array covariance); C# lists are invariant, so the port
    /// declares the callers' <c>List&lt;IType&gt;</c> and maps the elements up — the same values, in the same order, which
    /// is all every consumer (the alias-type-argument slots and the instantiation cache keys) reads.
    /// </remarks>
    internal List<IType>? GetTypeArgumentsForAliasSymbol(ISymbol? symbol)
        => symbol is not null
            ? Core.Map(GetLocalTypeParametersOfClassOrInterfaceOrTypeAlias(symbol), (tp, _) => (IType)tp)
            : null;

    /// <summary>ported from: src/compiler/checker.ts:20102-20104 — <c>isNonGenericObjectType</c>.</summary>
    internal bool IsNonGenericObjectType(IType type)
        => (type.Flags & TypeFlags.Object) != 0 && !IsGenericMappedType(type);

    /// <summary>ported from: src/compiler/checker.ts:20106-20108 — <c>isEmptyObjectTypeOrSpreadsIntoEmptyObject</c>.</summary>
    internal bool IsEmptyObjectTypeOrSpreadsIntoEmptyObject(IType type)
        => IsEmptyObjectType(type) ||
            (type.Flags & (TypeFlags.Null | TypeFlags.Undefined | TypeFlags.BooleanLike | TypeFlags.NumberLike |
                TypeFlags.BigIntLike | TypeFlags.StringLike | TypeFlags.EnumLike | TypeFlags.NonPrimitive | TypeFlags.Index)) != 0;

    /// <summary>ported from: src/compiler/checker.ts:20110-20149 — <c>tryMergeUnionOfObjectTypeAndEmptyObject</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>every</c>/<c>find</c>/<c>some</c> on arrays are <c>Core.Every</c>/<c>Core.Find</c> (whose predicates
    /// take the index) and the object identity tests are <c>ReferenceEquals</c>. <c>append</c>-free: the members table is
    /// the checker's ordered <see cref="SymbolTable"/>, whose indexer is TS's <c>set</c>. The nested
    /// <c>getAnonymousPartialType</c> is a local function, like the reference's.
    /// </remarks>
    internal IType TryMergeUnionOfObjectTypeAndEmptyObject(IType type, bool @readonly)
    {
        if ((type.Flags & TypeFlags.Union) == 0)
        {
            return type;
        }

        var types = ((UnionType)type).Types!;
        if (Core.Every(types, (t, _) => IsEmptyObjectTypeOrSpreadsIntoEmptyObject(t)))
        {
            return Core.Find(types, (t, _) => IsEmptyObjectType(t)) ?? _emptyObjectType;
        }

        var firstType = Core.Find(types, (t, _) => !IsEmptyObjectTypeOrSpreadsIntoEmptyObject(t));
        if (firstType is null)
        {
            return type;
        }

        var secondType = Core.Find(types, (t, _) => !ReferenceEquals(t, firstType) && !IsEmptyObjectTypeOrSpreadsIntoEmptyObject(t));
        if (secondType is not null)
        {
            return type;
        }

        return GetAnonymousPartialType(firstType);

        IType GetAnonymousPartialType(IType type)
        {
            // gets the type as if it had been spread, but where everything in the spread is made optional
            var members = Utilities.CreateSymbolTable();
            foreach (var prop in GetPropertiesOfType(type))
            {
                if ((Utilities.GetDeclarationModifierFlagsFromSymbol(prop) & (ModifierFlags.Private | ModifierFlags.Protected)) != 0)
                {
                    // do nothing, skip privates
                }
                else if (IsSpreadableProperty(prop))
                {
                    var isSetonlyAccessor = (prop.Flags & SymbolFlags.SetAccessor) != 0 && (prop.Flags & SymbolFlags.GetAccessor) == 0;
                    var flags = SymbolFlags.Property | SymbolFlags.Optional;
                    var result = CreateSymbol(flags, prop.EscapedName!, GetIsLateCheckFlag(prop) | (@readonly ? CheckFlags.Readonly : 0));
                    GetSymbolLinks(result).Type = isSetonlyAccessor ? _undefinedType : AddOptionality(GetTypeOfSymbol(prop), /*isProperty*/ true);
                    result.Declarations = prop.Declarations;
                    GetSymbolLinks(result).NameType = GetSymbolLinks(prop).NameType;
                    GetSymbolLinks(result).SyntheticOrigin = prop;
                    members[prop.EscapedName!] = result;
                }
            }

            var spread = CreateAnonymousType(type.Symbol, members, [], [], GetIndexInfosOfType(type));
            spread.ObjectFlags |= ObjectFlags.ObjectLiteral | ObjectFlags.ContainsObjectOrArrayLiteral;
            return spread;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:20156-20249 — <c>getSpreadType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's leading comment is kept below. <c>mapType</c> is <c>MapType</c> (whose mapper is a
    /// <c>Func&lt;IType, IType&gt;</c>); <c>types.slice(0, types.length - 1)</c> is
    /// <c>List.GetRange</c>, the port's registered mapping for <c>slice</c>; <c>emptyArray</c> is a fresh empty
    /// <c>List</c> per argument, since the port has no shared empty array (its call-signature and construct-signature
    /// lists are never mutated by the callers); <c>skippedPrivateMembers</c> is a name set, for which the port compares
    /// ordinally.
    /// </remarks>
    internal IType GetSpreadType(IType left, IType right, ISymbol? symbol, ObjectFlags objectFlags, bool @readonly)
    {
        // Since the source of spread types are object literals, which are not binary,
        // this function should be called in a left folding style, with left = previous result of getSpreadType
        // and right = the new element to be spread.
        if ((left.Flags & TypeFlags.Any) != 0 || (right.Flags & TypeFlags.Any) != 0)
        {
            return _anyType;
        }

        if ((left.Flags & TypeFlags.Unknown) != 0 || (right.Flags & TypeFlags.Unknown) != 0)
        {
            return _unknownType;
        }

        if ((left.Flags & TypeFlags.Never) != 0)
        {
            return right;
        }

        if ((right.Flags & TypeFlags.Never) != 0)
        {
            return left;
        }

        left = TryMergeUnionOfObjectTypeAndEmptyObject(left, @readonly);
        if ((left.Flags & TypeFlags.Union) != 0)
        {
            return CheckCrossProductUnion([left, right])
                ? MapType(left, t => GetSpreadType(t, right, symbol, objectFlags, @readonly))!
                : _errorType;
        }

        right = TryMergeUnionOfObjectTypeAndEmptyObject(right, @readonly);
        if ((right.Flags & TypeFlags.Union) != 0)
        {
            return CheckCrossProductUnion([left, right])
                ? MapType(right, t => GetSpreadType(left, t, symbol, objectFlags, @readonly))!
                : _errorType;
        }

        if ((right.Flags & (TypeFlags.BooleanLike | TypeFlags.NumberLike | TypeFlags.BigIntLike | TypeFlags.StringLike |
                TypeFlags.EnumLike | TypeFlags.NonPrimitive | TypeFlags.Index)) != 0)
        {
            return left;
        }

        if (IsGenericObjectType(left) || IsGenericObjectType(right))
        {
            if (IsEmptyObjectType(left))
            {
                return right;
            }

            // When the left type is an intersection, we may need to merge the last constituent of the
            // intersection with the right type. For example when the left type is 'T & { a: string }'
            // and the right type is '{ b: string }' we produce 'T & { a: string, b: string }'.
            if ((left.Flags & TypeFlags.Intersection) != 0)
            {
                var types = ((IntersectionType)left).Types!;
                var lastLeft = types[types.Count - 1];
                if (IsNonGenericObjectType(lastLeft) && IsNonGenericObjectType(right))
                {
                    return GetIntersectionType((List<IType>)Core.Concatenate(
                        types.GetRange(0, types.Count - 1),
                        [GetSpreadType(lastLeft, right, symbol, objectFlags, @readonly)])!);
                }
            }

            return GetIntersectionType([left, right]);
        }

        var members = Utilities.CreateSymbolTable();
        var skippedPrivateMembers = new HashSet<string>(StringComparer.Ordinal);
        var indexInfos = ReferenceEquals(left, _emptyObjectType) ? GetIndexInfosOfType(right) : GetUnionIndexInfos([left, right]);

        foreach (var rightProp in GetPropertiesOfType(right))
        {
            if ((Utilities.GetDeclarationModifierFlagsFromSymbol(rightProp) & (ModifierFlags.Private | ModifierFlags.Protected)) != 0)
            {
                skippedPrivateMembers.Add(rightProp.EscapedName!);
            }
            else if (IsSpreadableProperty(rightProp))
            {
                members[rightProp.EscapedName!] = GetSpreadSymbol(rightProp, @readonly);
            }
        }

        foreach (var leftProp in GetPropertiesOfType(left))
        {
            if (skippedPrivateMembers.Contains(leftProp.EscapedName!) || !IsSpreadableProperty(leftProp))
            {
                continue;
            }

            if (members.ContainsKey(leftProp.EscapedName!))
            {
                var rightProp = members[leftProp.EscapedName!];
                var rightType = GetTypeOfSymbol(rightProp);
                if ((rightProp.Flags & SymbolFlags.Optional) != 0)
                {
                    var declarations = Core.Concatenate(leftProp.Declarations, rightProp.Declarations) as List<IDeclaration>;
                    var flags = SymbolFlags.Property | (leftProp.Flags & SymbolFlags.Optional);
                    var result = CreateSymbol(flags, leftProp.EscapedName!);
                    // Optimization: avoid calculating the union type if spreading into the exact same type.
                    // This is common, e.g. spreading one options bag into another where the bags have the
                    // same type, or have properties which overlap. If the unions are large, it may turn out
                    // to be expensive to perform subtype reduction.
                    var leftType = GetTypeOfSymbol(leftProp);
                    var leftTypeWithoutUndefined = RemoveMissingOrUndefinedType(leftType);
                    var rightTypeWithoutUndefined = RemoveMissingOrUndefinedType(rightType);
                    GetSymbolLinks(result).Type = ReferenceEquals(leftTypeWithoutUndefined, rightTypeWithoutUndefined)
                        ? leftType
                        : GetUnionType([leftType, rightTypeWithoutUndefined], UnionReduction.Subtype);
                    GetSymbolLinks(result).LeftSpread = leftProp;
                    GetSymbolLinks(result).RightSpread = rightProp;
                    result.Declarations = declarations;
                    GetSymbolLinks(result).NameType = GetSymbolLinks(leftProp).NameType;
                    members[leftProp.EscapedName!] = result;
                }
            }
            else
            {
                members[leftProp.EscapedName!] = GetSpreadSymbol(leftProp, @readonly);
            }
        }

        var spread = CreateAnonymousType(
            symbol,
            members,
            [],
            [],
            Core.SameMap(indexInfos, (info, _) => GetIndexInfoWithReadonly(info, @readonly))!);
        spread.ObjectFlags |= ObjectFlags.ObjectLiteral | ObjectFlags.ContainsObjectOrArrayLiteral | ObjectFlags.ContainsSpread | objectFlags;
        return spread;
    }

    /// <summary>We approximate own properties as non-methods plus methods that are inside the object literal</summary>
    /// <summary>ported from: src/compiler/checker.ts:20252-20256 — <c>isSpreadableProperty</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>some(prop.declarations, isPrivateIdentifierClassElementDeclaration)</c> and
    /// <c>prop.declarations?.some(…)</c> are both <c>Core.Some</c>, whose <c>null</c> array answers <c>false</c> — which is
    /// what the reference's <c>?.</c> plus <c>!</c> computes. The declarations are declarations, so the predicate's
    /// <c>Node</c> parameter takes the cast the model needs.
    /// </remarks>
    internal bool IsSpreadableProperty(ISymbol prop)
    {
        return !Core.Some(prop.Declarations, decl => Utilities.IsPrivateIdentifierClassElementDeclaration(decl as Node)) &&
            ((prop.Flags & (SymbolFlags.Method | SymbolFlags.GetAccessor | SymbolFlags.SetAccessor)) == 0 ||
                !Core.Some(prop.Declarations, decl => Utilities.IsClassLike(((Node)decl).Parent!)));
    }

    /// <summary>ported from: src/compiler/checker.ts:20258-20270 — <c>getSpreadSymbol</c>.</summary>
    internal ISymbol GetSpreadSymbol(ISymbol prop, bool @readonly)
    {
        var isSetonlyAccessor = (prop.Flags & SymbolFlags.SetAccessor) != 0 && (prop.Flags & SymbolFlags.GetAccessor) == 0;
        if (!isSetonlyAccessor && @readonly == IsReadonlySymbol(prop))
        {
            return prop;
        }

        var flags = SymbolFlags.Property | (prop.Flags & SymbolFlags.Optional);
        var result = CreateSymbol(flags, prop.EscapedName!, GetIsLateCheckFlag(prop) | (@readonly ? CheckFlags.Readonly : 0));
        GetSymbolLinks(result).Type = isSetonlyAccessor ? _undefinedType : GetTypeOfSymbol(prop);
        result.Declarations = prop.Declarations;
        GetSymbolLinks(result).NameType = GetSymbolLinks(prop).NameType;
        GetSymbolLinks(result).SyntheticOrigin = prop;
        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:20272-20274 — <c>getIndexInfoWithReadonly</c>.</summary>
    internal IIndexInfo GetIndexInfoWithReadonly(IIndexInfo info, bool @readonly)
        => info.IsReadonly != @readonly
            ? CreateIndexInfo(info.KeyType!, info.Type!, @readonly, info.Declaration, info.Components)
            : info;

    // ---------------------------------------------------------------------------------------------
    // Literal types and their caches (checker.ts:20276-20341)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:20276-20281 — <c>createLiteralType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>value: string | number | PseudoBigInt</c> is <c>object</c> here (the three box into one slot);
    /// it is also the one place the reference passes a value that may be absent: the fresh boolean literal types are
    /// derived from the freshable intrinsics (checker.ts:2097-2100), whose <c>value</c> is <c>undefined</c>, so the
    /// parameter is nullable and the slot keeps that. <c>regularType</c> is declared <c>LiteralType</c> upstream but
    /// receives an <c>IntrinsicType</c> at that same call site, hence the runtime union <see cref="IFreshableType"/> —
    /// the interface both families implement. The concrete class is chosen by <c>createTypeWithSymbol</c>'s
    /// <c>TypeFlags → class</c> dispatch (<c>Model/LiteralType.cs</c>).
    /// </remarks>
    internal LiteralType CreateLiteralType(TypeFlags flags, object? value, ISymbol? symbol = null, IFreshableType? regularType = null)
    {
        var type = (LiteralType)CreateTypeWithSymbol(flags, symbol!);
        type.Value = value;
        type.RegularType = regularType ?? type;
        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:20283-20293 — <c>getFreshTypeOfLiteralType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>(type as LiteralType).value</c>/<c>(type as LiteralType).symbol</c> are no-ops at run time —
    /// the freshable slot also holds intrinsics (<c>regularFalseType</c>), which are not literal types — so the value is
    /// read through the <see cref="ILiteralType"/> face only when the type is one, and the regular type is passed as the
    /// <see cref="IFreshableType"/> both families implement. Both freshable slots are set, as the reference's two
    /// assignments do.
    /// </remarks>
    internal IType GetFreshTypeOfLiteralType(IType type)
    {
        if ((type.Flags & TypeFlags.Freshable) != 0)
        {
            var freshable = (IFreshableType)type;
            if (freshable.FreshType is null)
            {
                var literal = type as ILiteralType;
                var freshType = CreateLiteralType(type.Flags, literal?.Value, type.Symbol, freshable);
                freshType.FreshType = freshType;
                freshable.FreshType = freshType;
            }

            return freshable.FreshType;
        }

        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:20295-20299 — <c>getRegularTypeOfLiteralType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the union arm's <c>mapType(type, getRegularTypeOfLiteralType) as UnionType</c> maps every constituent
    /// (M6Type2's <c>MapType</c> builds a union from a union) and caches it on the union's own <c>regularType</c> slot;
    /// the cache write is the reference's <c>|| (… = …)</c>.
    /// </remarks>
    internal IType GetRegularTypeOfLiteralType(IType type)
    {
        if ((type.Flags & TypeFlags.Freshable) != 0)
        {
            return ((IFreshableType)type).RegularType!;
        }

        if ((type.Flags & TypeFlags.Union) != 0)
        {
            var union = (UnionType)type;
            return union.RegularType ??= (IUnionType)MapType(union, t => GetRegularTypeOfLiteralType(t))!;
        }

        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:20301-20303 — <c>isFreshLiteralType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the <c>freshType</c> slot is read through the freshable face because the freshable set also holds the
    /// boolean literal intrinsics (<c>BooleanLiteralType</c> is an <c>IntrinsicType</c>), exactly as the reference's
    /// no-op cast does.
    /// </remarks>
    internal bool IsFreshLiteralType(IType type)
        => (type.Flags & TypeFlags.Freshable) != 0 && ReferenceEquals(((IFreshableType)type).FreshType, type);

    // ---------------------------------------------------------------------------------------------
    // The four literal-type factories and their caches (checker.ts:20305-20330)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:20305-20309 — <c>getStringLiteralType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>get(v) || (set(v, type = create(v)), type)</c> is the port's "lookup, fill on miss" form — the
    /// comma expression's assignment is what the miss branch performs — which is also what makes the returned slot
    /// non-null without a cast.
    /// </remarks>
    internal IStringLiteralType GetStringLiteralType(string value)
    {
        if (!_stringLiteralTypes.TryGetValue(value, out var type))
        {
            type = (StringLiteralType)CreateLiteralType(TypeFlags.StringLiteral, value);
            _stringLiteralTypes[value] = type;
        }

        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:20311-20315 — <c>getNumberLiteralType</c>.</summary>
    internal INumberLiteralType GetNumberLiteralType(double value)
    {
        if (!_numberLiteralTypes.TryGetValue(value, out var type))
        {
            type = (NumberLiteralType)CreateLiteralType(TypeFlags.NumberLiteral, value);
            _numberLiteralTypes[value] = type;
        }

        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:20317-20322 — <c>getBigIntLiteralType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the key is TS's <c>pseudoBigIntToString(value)</c> (utilities.ts:10529) — the digit string the port
    /// reaches through <c>Utilities</c> — because <see cref="IPseudoBigInt"/> is not a value type here and the JS
    /// <c>Map</c> key would otherwise have to be the object identity JS does not use either.
    /// </remarks>
    internal IBigIntLiteralType GetBigIntLiteralType(IPseudoBigInt value)
    {
        var key = Utilities.PseudoBigIntToString(value);
        if (!_bigIntLiteralTypes.TryGetValue(key, out var type))
        {
            type = (BigIntLiteralType)CreateLiteralType(TypeFlags.BigIntLiteral, value);
            _bigIntLiteralTypes[key] = type;
        }

        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:20324-20330 — <c>getEnumLiteralType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>value: string | number</c> is <c>object</c> here — the caller hands it
    /// <c>getEnumMemberValue(member).value</c>, whose slot is <c>object?</c> — and the key's <c>${value}</c> is JS's
    /// number-to-string conversion, which the port states as <c>Js.NumberToString</c> (the boxed <c>ToString</c> would
    /// follow the current culture and .NET's formatting instead of ECMAScript's).
    /// </remarks>
    internal ILiteralType GetEnumLiteralType(object value, int enumId, ISymbol symbol)
    {
        var isString = value is string;
        var valueText = isString
            ? (string)value
            : value switch
            {
                double number => Js.NumberToString(number),
                int number => Js.NumberToString(number),
                _ => value.ToString() ?? "",
            };
        var key = $"{enumId}{(isString ? "@" : "#")}{valueText}";
        var flags = TypeFlags.EnumLiteral | (isString ? TypeFlags.StringLiteral : TypeFlags.NumberLiteral);
        if (!_enumLiteralTypes.TryGetValue(key, out var type))
        {
            type = CreateLiteralType(flags, value, symbol);
            _enumLiteralTypes[key] = type;
        }

        return type;
    }

    // ---------------------------------------------------------------------------------------------
    // Literal, unique-symbol and this types (checker.ts:20332-20430)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:20332-20341 — <c>getTypeFromLiteralTypeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the literal is the node's own required slot, hence the two <c>!</c>; <c>checkExpression</c> is M6-5's
    /// member and its result feeds the regular-type query, as upstream.
    /// </remarks>
    internal IType GetTypeFromLiteralTypeNode(LiteralTypeNode node)
    {
        if (node.Literal!.Kind == SyntaxKind.NullKeyword)
        {
            return _nullType;
        }

        var links = GetNodeLinks(node);
        if (links.ResolvedType is null)
        {
            links.ResolvedType = GetRegularTypeOfLiteralType(CheckExpression(node.Literal!));
        }

        return links.ResolvedType;
    }

    /// <summary>ported from: src/compiler/checker.ts:20343-20347 — <c>createUniqueESSymbolType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS builds the escaped name from the symbol's own name and id and casts it to <c>__String</c>; the port's
    /// slot is a plain <c>string</c>, and the two <c>!</c> are the synthetic symbol's required ones.
    /// </remarks>
    internal UniqueESSymbolType CreateUniqueESSymbolType(ISymbol symbol)
    {
        var type = (UniqueESSymbolType)CreateTypeWithSymbol(TypeFlags.UniqueESSymbol, symbol);
        type.EscapedName = $"__@{type.Symbol!.EscapedName}@{Utilities.GetSymbolId(type.Symbol!)}";
        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:20349-20364 — <c>getESSymbolLikeTypeForNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>links.uniqueESSymbolType || (links.uniqueESSymbolType = …)</c> is the port's <c>??=</c>, and
    /// <c>node = getSingleVariableOfVariableStatement(host) || host</c> is the same "first non-null" chain spelled with
    /// <c>??</c>. <c>isValidESSymbolDeclaration</c> answers <c>bool</c> instead of narrowing, so the common-JS-export arm
    /// reads <c>left</c> through the cast the reference writes itself (<c>node as BinaryExpression</c>).
    /// </remarks>
    internal IType GetESSymbolLikeTypeForNode(Node node)
    {
        if (Utilities.IsInJSFile(node) && NodeTests.IsJSDocTypeExpression(node))
        {
            var host = Utilities.GetJSDocHost(node);
            if (host is not null)
            {
                node = Utilities.GetSingleVariableOfVariableStatement(host) ?? host;
            }
        }

        if (Utilities.IsValidESSymbolDeclaration(node))
        {
            var symbol = Utilities.IsCommonJsExportPropertyAssignment(node)
                ? GetSymbolOfNode(((BinaryExpression)node).Left!)
                : GetSymbolOfNode(node);
            if (symbol is not null)
            {
                var links = GetSymbolLinks(symbol);
                return links.UniqueESSymbolType ??= CreateUniqueESSymbolType(symbol);
            }
        }

        return _esSymbolType;
    }

    /// <summary>ported from: src/compiler/checker.ts:20366-20394 — <c>getThisType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the three <c>getDeclaredTypeOfClassOrInterface(…).thisType!</c> reads are the reference's own
    /// assertions and are kept as <c>!</c>. The JSDoc host is a <c>SignatureDeclaration</c> upstream while the port's
    /// helper answers <see cref="ISignatureDeclarationBase"/>?, so the function-expression test reads it as the node
    /// class — the same cast <c>Utilities.GetHostSignatureFromJSDoc</c>'s callers make (§12.9). The constructor's
    /// <c>body</c> is read from the class that declares it after the reference's own <c>isJSConstructor</c>/<c>isConstructorDeclaration</c>
    /// guards. <c>getSymbolOfDeclaration</c>'s parameter is the declaration face, which the class-like parent and the
    /// constructor container both are.
    /// </remarks>
    internal IType GetThisType(Node node)
    {
        var container = Utilities.GetThisContainer(node, /*includeArrowFunctions*/ false, /*includeClassComputedPropertyName*/ false);
        var parent = container.Parent;
        if (parent is not null && (Utilities.IsClassLike(parent) || parent.Kind == SyntaxKind.InterfaceDeclaration))
        {
            if (!Utilities.IsStatic(container) &&
                (!NodeTests.IsConstructorDeclaration(container) ||
                 Utilities.IsNodeDescendantOf(node, ((ConstructorDeclaration)container).Body)))
            {
                return GetDeclaredTypeOfClassOrInterface(GetSymbolOfDeclaration((IDeclaration)parent)).ThisType!;
            }
        }

        // inside x.prototype = { ... }
        if (parent is not null && NodeTests.IsObjectLiteralExpression(parent) &&
            parent.Parent is BinaryExpression prototypeAssignment &&
            Utilities.GetAssignmentDeclarationKind(prototypeAssignment) == AssignmentDeclarationKind.Prototype)
        {
            return GetDeclaredTypeOfClassOrInterface(GetSymbolOfNode(prototypeAssignment.Left!)!.Parent!).ThisType!;
        }

        // /** @return {this} */
        // x.prototype.m = function() { ... }
        var host = (node.Flags & NodeFlags.JSDoc) != 0 ? Utilities.GetHostSignatureFromJSDoc(node) : null;
        if (host is not null && NodeTests.IsFunctionExpression((Node)host) &&
            ((Node)host).Parent is BinaryExpression hostAssignment &&
            Utilities.GetAssignmentDeclarationKind(hostAssignment) == AssignmentDeclarationKind.PrototypeProperty)
        {
            return GetDeclaredTypeOfClassOrInterface(GetSymbolOfNode(hostAssignment.Left!)!.Parent!).ThisType!;
        }

        // inside constructor function C() { ... }
        if (IsJSConstructor(container) &&
            Utilities.IsNodeDescendantOf(node, container is IFunctionLikeDeclarationBase jsConstructor ? jsConstructor.Body : null))
        {
            return GetDeclaredTypeOfClassOrInterface(GetSymbolOfDeclaration((IDeclaration)container)).ThisType!;
        }

        Error(node, Diagnostics.A_this_type_is_available_only_in_a_non_static_member_of_a_class_or_interface);
        return _errorType;
    }

    /// <summary>ported from: src/compiler/checker.ts:20396-20402 — <c>getTypeFromThisTypeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>ThisExpression | ThisTypeNode</c>, whose members share no port interface
    /// (one is an expression, the other a type node), so the common base <c>Node</c> is the parameter type.
    /// </remarks>
    internal IType GetTypeFromThisTypeNode(Node node)
    {
        var links = GetNodeLinks(node);
        if (links.ResolvedType is null)
        {
            links.ResolvedType = GetThisType(node);
        }

        return links.ResolvedType;
    }

    /// <summary>ported from: src/compiler/checker.ts:20404-20406 — <c>getTypeFromRestTypeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the union <c>RestTypeNode | NamedTupleMember</c> is a type-node union, so the parameter is
    /// <see cref="ITypeNode"/> and the member that declares <c>type</c> is selected by the kind test the union's two
    /// shapes require; <c>getArrayElementTypeNode(node.type) || node.type</c> is <c>??</c> on the same two nodes.
    /// </remarks>
    internal IType GetTypeFromRestTypeNode(ITypeNode node)
    {
        var type = node.Kind == SyntaxKind.NamedTupleMember ? ((NamedTupleMember)node).Type : ((RestTypeNode)node).Type;
        return GetTypeFromTypeNode(GetArrayElementTypeNode(type!) ?? type);
    }

    /// <summary>ported from: src/compiler/checker.ts:20408-20424 — <c>getArrayElementTypeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is a <c>TypeNode</c> that the tuple arm reassigns to <c>elements[0]</c>; the port's
    /// element type is the node it casts to. The reference's <c>break</c> — leaving the tuple arm when it is not a
    /// one-element tuple — is the <c>if</c> around the two tests, and the absent result is <c>null</c>.
    /// </remarks>
    internal ITypeNode? GetArrayElementTypeNode(ITypeNode node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.ParenthesizedType:
                return GetArrayElementTypeNode(((ParenthesizedTypeNode)node).Type!);
            case SyntaxKind.TupleType:
                var elements = ((TupleTypeNode)node).Elements;
                if (elements is not null && elements.Count == 1)
                {
                    node = (ITypeNode)elements[0];
                    if (node.Kind == SyntaxKind.RestType ||
                        node.Kind == SyntaxKind.NamedTupleMember && ((NamedTupleMember)node).DotDotDotToken is not null)
                    {
                        var elementType = node.Kind == SyntaxKind.RestType
                            ? ((RestTypeNode)node).Type
                            : ((NamedTupleMember)node).Type;
                        return GetArrayElementTypeNode(elementType!);
                    }
                }

                break;
            case SyntaxKind.ArrayType:
                return ((ArrayTypeNode)node).ElementType;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:20426-20430 — <c>getTypeFromNamedTupleTypeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>links.resolvedType || (links.resolvedType = …)</c> is the port's <c>??=</c>; the question token's
    /// truthiness is the non-null test <c>addOptionality</c>'s third argument receives.
    /// </remarks>
    internal IType GetTypeFromNamedTupleTypeNode(NamedTupleMember node)
    {
        var links = GetNodeLinks(node);
        return links.ResolvedType ??= node.DotDotDotToken is not null
            ? GetTypeFromRestTypeNode(node)
            : AddOptionality(GetTypeFromTypeNode(node.Type), /*isProperty*/ true, node.QuestionToken is not null);
    }

    // ---------------------------------------------------------------------------------------------
    // The type-node entry point and its dispatch (checker.ts:20432-20537)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:20432-20434 — <c>getTypeFromTypeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares <c>node: TypeNode</c> non-optional; the port keeps the nullability of the checker's
    /// published face (<c>ITypeChecker.GetTypeFromTypeNode(ITypeNode? node)</c>, types.ts) so that the eventual
    /// <c>ITypeChecker</c> assembly needs no adapter, and states the invariant the reference relies on with
    /// <c>Debug.CheckDefined</c> — the same treatment <c>getSymbolLinks</c> gives a transient symbol's links. The cast is
    /// §12.9's: the worker and the flow-type query both take the node class while the parameter is the capability
    /// interface.
    /// </remarks>
    internal IType GetTypeFromTypeNode(ITypeNode? node)
    {
        var typeNode = Debug.CheckDefined(node);
        return GetConditionalFlowTypeOfType(GetTypeFromTypeNodeWorker((Node)typeNode), (Node)typeNode);
    }

    /// <summary>The <c>isPartOfTypeNode</c> arm of <c>getTypeOfNode</c> (checker.ts:50394-50396).</summary>
    /// <remarks>
    /// PORT-NOTE: the reference reaches <c>getTypeFromTypeNode(node as TypeNode)</c> from <c>isPartOfTypeNode</c>
    /// (utilities.ts:2788), whose admitted kinds include tokens and entity names the <c>TypeNode</c> union does not
    /// contain — <c>getTypeFromTypeNodeWorker</c>'s own comment records them (checker.ts:20530-20532) and its switch is
    /// written for exactly those kinds. The port's <c>ITypeNode</c> is a marker interface (Model/NodeInterfaces.cs:3731)
    /// that <c>Token</c>, <c>Identifier</c>, <c>QualifiedName</c> and <c>PropertyAccessExpression</c> do not implement,
    /// so this arm hands the node to the worker as the class they all are; the kind test is what proves each arm.
    /// </remarks>
    internal IType GetTypeFromTypeNodeForPartOfTypeNode(Node node)
        => GetConditionalFlowTypeOfType(GetTypeFromTypeNodeWorker(node), node);

    /// <summary>ported from: src/compiler/checker.ts:20436-20536 — <c>getTypeFromTypeNodeWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: every case of the reference's switch is here, in its order. The node-class arms are casts (the switch's
    /// kind test is what proves them), the arms whose reference parameter is a union of type nodes pass the node on
    /// unchanged (their callees take <see cref="ITypeNode"/> — the kind test proves the marker for those kinds), the
    /// <c>getTypeFromThisTypeNode</c> arm passes the common base <c>Node</c>, and the last three kinds — the ones the
    /// reference's own comment calls out as not being type nodes, reached here because <c>isPartOfTypeNode</c> accepts
    /// them — are read as a node so that <c>getSymbolAtLocation</c> takes them. The parameter is the base class rather
    /// than <see cref="ITypeNode"/> for the same reason: the reference's own comment says the guard admits kinds no
    /// <c>TypeNode</c> is. <c>objectKeyword</c>'s JS-file test is the flag test the reference performs, and
    /// <c>!noImplicitAny</c> is the checker's option field.
    /// </remarks>
    internal IType GetTypeFromTypeNodeWorker(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.AnyKeyword:
            case SyntaxKind.JSDocAllType:
            case SyntaxKind.JSDocUnknownType:
                return _anyType;
            case SyntaxKind.UnknownKeyword:
                return _unknownType;
            case SyntaxKind.StringKeyword:
                return _stringType;
            case SyntaxKind.NumberKeyword:
                return _numberType;
            case SyntaxKind.BigIntKeyword:
                return _bigintType;
            case SyntaxKind.BooleanKeyword:
                return _booleanType;
            case SyntaxKind.SymbolKeyword:
                return _esSymbolType;
            case SyntaxKind.VoidKeyword:
                return _voidType;
            case SyntaxKind.UndefinedKeyword:
                return _undefinedType;
            case SyntaxKind.NullKeyword:
                // TODO(rbuckton): `NullKeyword` is no longer a `TypeNode`, but we defensively allow it here because of incorrect casts in the Language Service.
                return _nullType;
            case SyntaxKind.NeverKeyword:
                return _neverType;
            case SyntaxKind.ObjectKeyword:
                return (node.Flags & NodeFlags.JavaScriptFile) != 0 && !_noImplicitAny ? _anyType : _nonPrimitiveType;
            case SyntaxKind.IntrinsicKeyword:
                return _intrinsicMarkerType;
            case SyntaxKind.ThisType:
            case SyntaxKind.ThisKeyword:
                // TODO(rbuckton): `ThisKeyword` is no longer a `TypeNode`, but we defensively allow it here because of incorrect casts in the Language Service and because of `isPartOfTypeNode`.
                return GetTypeFromThisTypeNode((Node)node);
            case SyntaxKind.LiteralType:
                return GetTypeFromLiteralTypeNode((LiteralTypeNode)node);
            case SyntaxKind.TypeReference:
                return GetTypeFromTypeReference((Node)node);
            case SyntaxKind.TypePredicate:
                return ((TypePredicateNode)node).AssertsModifier is not null ? _voidType : _booleanType;
            case SyntaxKind.ExpressionWithTypeArguments:
                return GetTypeFromTypeReference((Node)node);
            case SyntaxKind.TypeQuery:
                return GetTypeFromTypeQueryNode((TypeQueryNode)node);
            case SyntaxKind.ArrayType:
            case SyntaxKind.TupleType:
                return GetTypeFromArrayOrTupleTypeNode((ITypeNode)node);
            case SyntaxKind.OptionalType:
                return GetTypeFromOptionalTypeNode((OptionalTypeNode)node);
            case SyntaxKind.UnionType:
                return GetTypeFromUnionTypeNode((UnionTypeNode)node);
            case SyntaxKind.IntersectionType:
                return GetTypeFromIntersectionTypeNode((IntersectionTypeNode)node);
            case SyntaxKind.JSDocNullableType:
                return GetTypeFromJSDocNullableTypeNode((JSDocNullableType)node);
            case SyntaxKind.JSDocOptionalType:
                return AddOptionality(GetTypeFromTypeNode(((JSDocOptionalType)node).Type));
            case SyntaxKind.NamedTupleMember:
                return GetTypeFromNamedTupleTypeNode((NamedTupleMember)node);
            case SyntaxKind.ParenthesizedType:
            case SyntaxKind.JSDocNonNullableType:
            case SyntaxKind.JSDocTypeExpression:
                return GetTypeFromTypeNode(((ParenthesizedTypeNode)node).Type);
            case SyntaxKind.RestType:
                return GetTypeFromRestTypeNode((ITypeNode)node);
            case SyntaxKind.JSDocVariadicType:
                return GetTypeFromJSDocVariadicType((JSDocVariadicType)node);
            case SyntaxKind.FunctionType:
            case SyntaxKind.ConstructorType:
            case SyntaxKind.TypeLiteral:
            case SyntaxKind.JSDocTypeLiteral:
            case SyntaxKind.JSDocFunctionType:
            case SyntaxKind.JSDocSignature:
                return GetTypeFromTypeLiteralOrFunctionOrConstructorTypeNode((ITypeNode)node);
            case SyntaxKind.TypeOperator:
                return GetTypeFromTypeOperatorNode((TypeOperatorNode)node);
            case SyntaxKind.IndexedAccessType:
                return GetTypeFromIndexedAccessTypeNode((IndexedAccessTypeNode)node);
            case SyntaxKind.MappedType:
                return GetTypeFromMappedTypeNode((MappedTypeNode)node);
            case SyntaxKind.ConditionalType:
                return GetTypeFromConditionalTypeNode((ConditionalTypeNode)node);
            case SyntaxKind.InferType:
                return GetTypeFromInferTypeNode((InferTypeNode)node);
            case SyntaxKind.TemplateLiteralType:
                return GetTypeFromTemplateTypeNode((TemplateLiteralTypeNode)node);
            case SyntaxKind.ImportType:
                return GetTypeFromImportTypeNode((ImportTypeNode)node);
            // This function assumes that an identifier, qualified name, or property access expression is a type expression
            // Callers should first ensure this by calling `isPartOfTypeNode`
            // TODO(rbuckton): These aren't valid TypeNodes, but we treat them as such because of `isPartOfTypeNode`, which returns `true` for things that aren't `TypeNode`s.
            case SyntaxKind.Identifier:
            case SyntaxKind.QualifiedName:
            case SyntaxKind.PropertyAccessExpression:
                var symbol = GetSymbolAtLocation((Node)node);
                return symbol is not null ? GetDeclaredTypeOfSymbol(symbol) : _errorType;
            default:
                return _errorType;
        }
    }
}