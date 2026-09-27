namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:20538-20571 (instantiateList, instantiateTypes, instantiateSignatures, instantiateIndexInfos)
// ported from: src/compiler/checker.ts:20572-20670 (createTypeMapper … appendTypeMapping)
// ported from: src/compiler/checker.ts:20666-21179 (getRestrictiveTypeParameter … instantiateIndexInfo)
// ported from: src/compiler/checker.ts:2123-2124 (restrictiveMapper, permissiveMapper)
// ported from: src/compiler/checker.ts:2340-2342, 32915-32944 (the active mapper stack)
/// <summary>
/// Instantiation: TS's <c>TypeMapper</c> algebra and <c>instantiateType</c> family — the third segment of M6-2's
/// type-construction slice (checker.ts:16958-21270).
/// </summary>
/// <remarks>
/// <para>
/// Instantiation is where the port's <see cref="TypeMapper"/> (Model/TypeMapper.cs) meets the type system: a mapper is
/// a <c>TypeMapKind</c>-discriminated union of five shapes and <c>getMappedType</c> is the only reader
/// (checker.ts:20576-20608). The whole family — caching in <c>instantiateType</c>'s active-mapper stack, the
/// depth/count guards, the conditional-type and mapped-type instantiation caches — is ported 1:1; the deviations are
/// per-member PORT-NOTEs.
/// </para>
/// <para>
/// PORT-NOTE: TS declares the checker's <c>var</c>s for the mappers and the active-mapper stack inside
/// <c>createTypeChecker</c>; the port states them as instance fields of this class, in this file, because this is
/// their only writer and <c>Checker.cs</c>'s own note defers slots to the slice that lands their factory. The
/// permissive/restrictive mappers are the two <c>makeFunctionTypeMapper</c> calls at checker.ts:2123-2124; the
/// active-mapper stack is <c>pushActiveMapper</c>/<c>popActiveMapper</c>/<c>findActiveMapper</c>/
/// <c>clearActiveMapperCaches</c> (checker.ts:32915-32944), whose declaration site is outside every slice's stated
/// range but whose only readers are <c>instantiateTypeWithAlias</c> (here) and <c>checkSourceFileWorker</c> (M6-5,
/// which calls <see cref="ClearActiveMapperCaches"/>).
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // The mapper instances and the active-mapper stack (checker.ts:2123-2124, 2340-2342)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:2123 — <c>restrictiveMapper</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS creates it in the state block (with <c>makeFunctionTypeMapper</c>, whose <c>getRestrictiveTypeParameter</c>
    /// arm is declared below); the port creates it lazily because the factory is an instance member of this class. The
    /// value is still created exactly once and every read sees the same instance, which is what the identity tests in
    /// <c>findActiveMapper</c> rely on.
    /// </remarks>
    private TypeMapper? _restrictiveMapper;

    /// <summary>ported from: src/compiler/checker.ts:2124 — <c>permissiveMapper</c>.</summary>
    private TypeMapper? _permissiveMapper;

    /// <summary>ported from: src/compiler/checker.ts:2340 — <c>activeTypeMappers</c>.</summary>
    private readonly List<TypeMapper?> _activeTypeMappers = [];

    /// <summary>ported from: src/compiler/checker.ts:2341 — <c>activeTypeMappersCaches</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>Map&lt;string, Type&gt;[]</c> is a sparse array; the port pads a list, as the two id-keyed link tables do (checker.ts:2341, Checker.cs).</remarks>
    private readonly List<Dictionary<string, IType>> _activeTypeMappersCaches = [];

    /// <summary>ported from: src/compiler/checker.ts:2342 — <c>activeTypeMappersCount</c>.</summary>
    /// <remarks>PORT-NOTE: <c>internal</c> rather than <c>private</c> for the reason <c>Checker.cs</c>'s state note gives —
    /// these are the checker's state, not one slice's, and the M6 tests assert on the counter's shape.</remarks>
    internal int _activeTypeMappersCount;

    /// <summary>ported from: src/compiler/checker.ts:2123 — <c>restrictiveMapper</c>.</summary>
    /// <remarks>PORT-NOTE: TS's literal is <c>makeFunctionTypeMapper(t => t.flags &amp; TypeFlags.TypeParameter ? getRestrictiveTypeParameter(t as TypeParameter) : t, …)</c>; the port's arm spells out the <c>as TypeParameter</c> narrowing as an <see cref="ITypeParameter"/> cast.</remarks>
    internal TypeMapper RestrictiveMapper => _restrictiveMapper ??= MakeFunctionTypeMapper(
        t => (t.Flags & TypeFlags.TypeParameter) != 0 ? GetRestrictiveTypeParameter((ITypeParameter)t) : t,
        () => "(restrictive mapper)");

    /// <summary>ported from: src/compiler/checker.ts:2124 — <c>permissiveMapper</c>.</summary>
    internal TypeMapper PermissiveMapper => _permissiveMapper ??= MakeFunctionTypeMapper(
        t => (t.Flags & TypeFlags.TypeParameter) != 0 ? _wildcardType : t,
        () => "(permissive mapper)");

    // ---------------------------------------------------------------------------------------------
    // instantiateList and its three wrappers (checker.ts:20538-20571)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:20540-20556 — <c>instantiateList</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's third overload pair exists only to keep a non-null element list non-null; the port declares the
    /// single implementation and callers assert the non-null case with <c>!</c>, exactly where TS's overload did the
    /// same inference. The early-out (<c>item !== mapped</c>) is the reference's copy-on-write: the list is returned
    /// unchanged when nothing moved.
    /// </remarks>
    internal List<T>? InstantiateList<T>(List<T>? items, TypeMapper mapper, Func<T, TypeMapper, T> instantiator)
        where T : class
    {
        if (items is { Count: > 0 })
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var mapped = instantiator(item, mapper);
                if (!ReferenceEquals(item, mapped))
                {
                    var result = i == 0 ? new List<T>() : items.GetRange(0, i);
                    result.Add(mapped);
                    for (i++; i < items.Count; i++)
                    {
                        result.Add(instantiator(items[i], mapper));
                    }

                    return result;
                }
            }
        }

        return items;
    }

    /// <summary>ported from: src/compiler/checker.ts:20558-20562 — <c>instantiateTypes</c>.</summary>
    internal List<IType>? InstantiateTypes(List<IType>? types, TypeMapper mapper)
        => InstantiateList(types, mapper, (item, mapperArg) => InstantiateType(item, mapperArg)!);

    /// <summary>ported from: src/compiler/checker.ts:20564-20566 — <c>instantiateSignatures</c>.</summary>
    internal List<ISignature>? InstantiateSignatures(List<ISignature>? signatures, TypeMapper mapper)
        => InstantiateList(signatures, mapper, (signature, mapperArg) => InstantiateSignature(signature, mapperArg));

    /// <summary>ported from: src/compiler/checker.ts:20568-20570 — <c>instantiateIndexInfos</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>IndexInfo</c> is an interface (types.ts:7067), which the model spells
    /// <see cref="IIndexInfo"/> and which is the face the model's index-info slots hold
    /// (<c>GetIndexInfosOfType</c> answers <c>List&lt;IIndexInfo&gt;</c>). Both this member and
    /// <see cref="InstantiateIndexInfo"/> are therefore stated over that face, so the reference's
    /// <c>instantiateIndexInfos(getIndexInfosOfType(…), mapper)</c> compiles without a projection.
    /// </remarks>
    internal List<IIndexInfo>? InstantiateIndexInfos(List<IIndexInfo>? indexInfos, TypeMapper mapper)
        => InstantiateList(indexInfos, mapper, InstantiateIndexInfo);

    // ---------------------------------------------------------------------------------------------
    // The five mapper factories and their readers (checker.ts:20572-20670)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:20572-20574 — <c>createTypeMapper</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>targets ? targets[0] : anyType</c> reads the checker's <c>anyType</c>, a field M6-2's type-constructor slice declares (<c>_anyType</c>).</remarks>
    internal TypeMapper CreateTypeMapper(List<ITypeParameter> sources, List<IType>? targets)
        => sources.Count == 1 ? MakeUnaryTypeMapper(sources[0], targets is not null ? targets[0] : _anyType) : MakeArrayTypeMapper(sources, targets);

    /// <summary>ported from: src/compiler/checker.ts:20576-20608 — <c>getMappedType</c>.</summary>
    internal IType GetMappedType(IType type, TypeMapper mapper)
    {
        switch (mapper.Kind)
        {
            case TypeMapKind.Simple:
                return ReferenceEquals(type, mapper.Source) ? mapper.Target! : type;
            case TypeMapKind.Array:
                {
                    var sources = mapper.Sources!;
                    var targets = mapper.Targets;
                    for (var i = 0; i < sources.Count; i++)
                    {
                        if (ReferenceEquals(type, sources[i]))
                        {
                            return targets is not null ? targets[i] : _anyType;
                        }
                    }

                    return type;
                }

            case TypeMapKind.Deferred:
                {
                    var sources = mapper.Sources!;
                    var targets = mapper.TargetFactories!;
                    for (var i = 0; i < sources.Count; i++)
                    {
                        if (ReferenceEquals(type, sources[i]))
                        {
                            return targets[i]();
                        }
                    }

                    return type;
                }

            case TypeMapKind.Function:
                return mapper.Func!(type);
            case TypeMapKind.Composite:
            case TypeMapKind.Merged:
                var t1 = GetMappedType(type, mapper.Mapper1!);
                return !ReferenceEquals(t1, type) && mapper.Kind == TypeMapKind.Composite
                    ? InstantiateType(t1, mapper.Mapper2!)!
                    : GetMappedType(t1, mapper.Mapper2!);
            default:
                return Debug.Fail<IType>("unreachable TypeMapKind");
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:20609-20611 — <c>makeUnaryTypeMapper</c>.</summary>
    /// <remarks>PORT-NOTE: TS wraps this in <c>Debug.attachDebugPrototypeIfDebug</c> (debug.ts:1600); the port has no prototype patching (Model/TypeMapper.cs's note).</remarks>
    internal TypeMapper MakeUnaryTypeMapper(IType source, IType target)
        => new() { Kind = TypeMapKind.Simple, Source = source, Target = target };

    /// <summary>ported from: src/compiler/checker.ts:20613-20615 — <c>makeArrayTypeMapper</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS stores the caller's <c>readonly TypeParameter[]</c> in the mapper's <c>readonly Type[]</c> slot
    /// because a <c>TypeParameter</c> is a <c>Type</c>; C# lists are invariant, so the port copies the list once into
    /// a <c>List&lt;IType&gt;</c>. The elements are the same objects, which is what the identity tests in
    /// <c>getMappedType</c> compare; nothing compares the list itself.
    /// </remarks>
    internal TypeMapper MakeArrayTypeMapper(List<ITypeParameter> sources, List<IType>? targets)
        => new() { Kind = TypeMapKind.Array, Sources = [.. sources], Targets = targets };

    /// <summary>ported from: src/compiler/checker.ts:20617-20619 — <c>makeFunctionTypeMapper</c>.</summary>
    internal TypeMapper MakeFunctionTypeMapper(Func<IType, IType> func, Func<string> debugInfo)
        => new() { Kind = TypeMapKind.Function, Func = func, DebugInfo = Debug.IsDebugging ? debugInfo : null };

    /// <summary>ported from: src/compiler/checker.ts:20621-20623 — <c>makeDeferredTypeMapper</c>.</summary>
    internal TypeMapper MakeDeferredTypeMapper(List<ITypeParameter> sources, List<Func<IType>> targets)
        => new() { Kind = TypeMapKind.Deferred, Sources = [.. sources], TargetFactories = targets };

    /// <summary>ported from: src/compiler/checker.ts:20625-20627 — <c>makeCompositeTypeMapper</c>.</summary>
    internal TypeMapper MakeCompositeTypeMapper(TypeMapKind kind, TypeMapper mapper1, TypeMapper mapper2)
        => new() { Kind = kind, Mapper1 = mapper1, Mapper2 = mapper2 };

    /// <summary>ported from: src/compiler/checker.ts:20629-20631 — <c>createTypeEraser</c>.</summary>
    internal TypeMapper CreateTypeEraser(List<ITypeParameter> sources) => CreateTypeMapper(sources, targets: null);

    /// <summary>ported from: src/compiler/checker.ts:20637-20640 — <c>createBackreferenceMapper</c>.</summary>
    /// <remarks>
    /// Maps forward-references to later type parameters to the unknown type; used during inference when instantiating
    /// type parameter defaults.
    /// </remarks>
    internal TypeMapper CreateBackreferenceMapper(IInferenceContext context, int index)
    {
        var forwardInferences = context.Inferences!.GetRange(index, context.Inferences.Count - index);
        return CreateTypeMapper(
            Core.Map(forwardInferences, (i, _) => i.TypeParameter!)!,
            Core.Map(forwardInferences, (_, _) => _unknownType)!);
    }

    /// <summary>ported from: src/compiler/checker.ts:20646-20648 — <c>createOuterReturnMapper</c>.</summary>
    /// <remarks>Returns a mapper that combines the context's return mapper with one that erases additional type parameters to their inferences at creation time.</remarks>
    internal TypeMapper CreateOuterReturnMapper(IInferenceContext context)
        => (TypeMapper)(context.OuterReturnMapper ??= MergeTypeMappers((TypeMapper?)context.ReturnMapper, (TypeMapper)CloneInferenceContext(context).Mapper!))!;

    /// <summary>ported from: src/compiler/checker.ts:20650-20652 — <c>combineTypeMappers</c>.</summary>
    internal TypeMapper CombineTypeMappers(TypeMapper? mapper1, TypeMapper mapper2)
        => mapper1 is not null ? MakeCompositeTypeMapper(TypeMapKind.Composite, mapper1, mapper2) : mapper2;

    /// <summary>ported from: src/compiler/checker.ts:20654-20656 — <c>mergeTypeMappers</c>.</summary>
    internal TypeMapper MergeTypeMappers(TypeMapper? mapper1, TypeMapper mapper2)
        => mapper1 is not null ? MakeCompositeTypeMapper(TypeMapKind.Merged, mapper1, mapper2) : mapper2;

    /// <summary>ported from: src/compiler/checker.ts:20658-20660 — <c>prependTypeMapping</c>.</summary>
    internal TypeMapper PrependTypeMapping(IType source, IType target, TypeMapper? mapper)
        => mapper is null ? MakeUnaryTypeMapper(source, target) : MakeCompositeTypeMapper(TypeMapKind.Merged, MakeUnaryTypeMapper(source, target), mapper);

    /// <summary>ported from: src/compiler/checker.ts:20662-20664 — <c>appendTypeMapping</c>.</summary>
    internal TypeMapper AppendTypeMapping(TypeMapper? mapper, IType source, IType target)
        => mapper is null ? MakeUnaryTypeMapper(source, target) : MakeCompositeTypeMapper(TypeMapKind.Merged, mapper, MakeUnaryTypeMapper(source, target));

    /// <summary>ported from: src/compiler/checker.ts:20666-20670 — <c>getRestrictiveTypeParameter</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's comma expression <c>(tp.restrictiveInstantiation = createTypeParameter(tp.symbol), … .constraint = noConstraintType, tp.restrictiveInstantiation)</c>
    /// is written as the same three statements in the same order; the two <c>as TypeParameter</c> casts are the port's
    /// nominal spelling of TS's structural narrowing (a scalar of the <c>TypeParameter</c> face is an
    /// <see cref="ITypeParameter"/>).
    /// </remarks>
    internal ITypeParameter GetRestrictiveTypeParameter(ITypeParameter tp)
    {
        if ((tp.Constraint is null && GetConstraintDeclaration(tp) is null) || ReferenceEquals(tp.Constraint, _noConstraintType))
        {
            return tp;
        }

        if (tp.RestrictiveInstantiation is not null)
        {
            return (ITypeParameter)tp.RestrictiveInstantiation;
        }

        var result = CreateTypeParameter(tp.Symbol!);
        tp.RestrictiveInstantiation = result;
        result.Constraint = _noConstraintType;
        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:20672-20676 — <c>cloneTypeParameter</c>.</summary>
    internal ITypeParameter CloneTypeParameter(ITypeParameter typeParameter)
    {
        var result = CreateTypeParameter(typeParameter.Symbol!);
        result.Target = typeParameter;
        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:20678-20680 — <c>instantiateTypePredicate</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads <c>predicate.parameterName</c>/<c>parameterIndex</c> off the union, whose members are
    /// <c>string|undefined</c> for the two <c>Identifier</c> kinds and <c>undefined</c> for the two <c>This</c> kinds;
    /// the port narrows on the kind, as the other readers of the four faces do (Model/TypePredicate.cs).
    /// </remarks>
    internal ITypePredicateBase InstantiateTypePredicate(ITypePredicateBase predicate, TypeMapper mapper)
    {
        string? parameterName = null;
        int? parameterIndex = null;
        if (predicate.Kind is TypePredicateKind.Identifier or TypePredicateKind.AssertsIdentifier)
        {
            var identifierPredicate = (IIdentifierTypePredicate)predicate;
            parameterName = identifierPredicate.ParameterName;
            parameterIndex = identifierPredicate.ParameterIndex;
        }

        return CreateTypePredicate(predicate.Kind, parameterName, parameterIndex, InstantiateType(predicate.Type, mapper));
    }

    /// <summary>ported from: src/compiler/checker.ts:20682-20701 — <c>instantiateSignature</c>.</summary>
    internal ISignature InstantiateSignature(ISignature signature, TypeMapper mapper, bool eraseTypeParameters = false)
    {
        using var allocProbe = AllocationProbe.Enter("Checker.InstantiateSignature");
        List<ITypeParameter>? freshTypeParameters = null;
        if (signature.TypeParameters is not null && !eraseTypeParameters)
        {
            // First create a fresh set of type parameters, then include a mapping from the old to the
            // new type parameters in the mapper function. Finally store this mapper in the new type
            // parameters such that we can use it when instantiating constraints.
            freshTypeParameters = Core.Map(signature.TypeParameters, (typeParameter, _) => CloneTypeParameter(typeParameter))!;
            mapper = CombineTypeMappers(CreateTypeMapper(signature.TypeParameters, [.. freshTypeParameters]), mapper);
            foreach (var tp in freshTypeParameters)
            {
                tp.Mapper = mapper;
            }
        }

        // Don't compute resolvedReturnType and resolvedTypePredicate now,
        // because using `mapper` now could trigger inferences to become fixed. (See `createInferenceContext`.)
        // See GH#17600.
        var result = CreateSignature(
            signature.Declaration,
            freshTypeParameters,
            signature.ThisParameter is not null ? InstantiateSymbol(signature.ThisParameter, mapper) : null,
            InstantiateList(signature.Parameters, mapper, InstantiateSymbol)!,
            resolvedReturnType: null,
            resolvedTypePredicate: null,
            signature.MinArgumentCount,
            signature.Flags & SignatureFlags.PropagatingFlags);
        result.Target = signature;
        result.Mapper = mapper;
        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:20703-20737 — <c>instantiateSymbol</c>.</summary>
    internal ISymbol InstantiateSymbol(ISymbol symbol, TypeMapper mapper)
    {
        var links = GetSymbolLinks(symbol);
        // If the type of the symbol is already resolved, and if that type could not possibly
        // be affected by instantiation, simply return the symbol itself.
        if (links.Type is not null && !CouldContainTypeVariables(links.Type))
        {
            if ((symbol.Flags & SymbolFlags.SetAccessor) == 0)
            {
                return symbol;
            }

            // If we're a setter, check writeType.
            if (links.WriteType is not null && !CouldContainTypeVariables(links.WriteType))
            {
                return symbol;
            }
        }

        if ((GetCheckFlags(symbol) & CheckFlags.Instantiated) != 0)
        {
            // If symbol being instantiated is itself an instantiation, fetch the original target and combine the
            // type mappers. This ensures that original type identities are properly preserved and that aliases
            // always reference a non-alias.
            symbol = links.Target!;
            mapper = CombineTypeMappers((TypeMapper?)links.Mapper, mapper);
        }

        // Keep the flags from the symbol we're instantiating. Mark that it is instantiated, and
        // also transient so that we can just store data on it directly.
        var result = CreateSymbol(
            symbol.Flags,
            symbol.EscapedName!,
            CheckFlags.Instantiated | (GetCheckFlags(symbol) & (CheckFlags.Readonly | CheckFlags.Late | CheckFlags.OptionalParameter | CheckFlags.RestParameter)));
        result.Declarations = symbol.Declarations;
        result.Parent = symbol.Parent;
        var resultLinks = GetSymbolLinks(result);
        resultLinks.Target = symbol;
        resultLinks.Mapper = mapper;
        if (symbol.ValueDeclaration is not null)
        {
            result.ValueDeclaration = symbol.ValueDeclaration;
        }

        if (links.NameType is not null)
        {
            resultLinks.NameType = links.NameType;
        }

        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:20739-20807 — <c>getObjectTypeInstantiation</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>AnonymousType | DeferredTypeReference</c>; the port takes
    /// <see cref="IObjectType"/>, the common face of both (IAnonymousType is an IObjectType and IDeferredTypeReference
    /// derives from ITypeReference, which is one too), and casts to the face each use site needs.
    /// <c>target.instantiations</c> is the single physical slot the <c>DeferredTypeReference</c>/<c>AnonymousType</c>
    /// faces share (Model/ObjectType.cs), read here through the deferred face.
    /// </remarks>
    internal IType GetObjectTypeInstantiation(IObjectType type, TypeMapper mapper, ISymbol? aliasSymbol, List<IType>? aliasTypeArguments)
    {
        var objectFlags = type.ObjectFlags;
        var declaration = (objectFlags & ObjectFlags.Reference) != 0 ? ((ITypeReference)type).Node! :
            (objectFlags & ObjectFlags.InstantiationExpressionType) != 0 ? (Node)((IInstantiationExpressionType)type).Node! :
            (Node)type.Symbol!.Declarations![0];
        var links = GetNodeLinks(declaration);
        var target = (objectFlags & ObjectFlags.Reference) != 0 ? (IObjectType)links.ResolvedType! :
            (objectFlags & ObjectFlags.Instantiated) != 0 ? ((IAnonymousType)type).Target! : type;
        var typeParameters = links.OuterTypeParameters;
        if (typeParameters is null)
        {
            // The first time an anonymous type is instantiated we compute and store a list of the type
            // parameters that are in scope (and therefore potentially referenced). For type literals that
            // aren't the right hand side of a generic type alias declaration we optimize by reducing the
            // set of type parameters to those that are possibly referenced in the literal.
            var outerTypeParameters = GetOuterTypeParameters(declaration, includeThisTypes: true);
            if (IsJSConstructor(declaration))
            {
                var templateTagParameters = GetTypeParametersFromDeclaration(declaration);
                // PORT-NOTE: TS writes `addRange(outerTypeParameters, templateTagParameters)`, whose `to.push` assumes
                // `to` is defined here (a JS constructor always has outer type parameters); the port's
                // Core.AddRange also answers a fresh list when `to` is null, so the two statements below are the
                // reference's result in both cases.
                if (Core.Length(templateTagParameters) > 0)
                {
                    outerTypeParameters ??= [];
                    outerTypeParameters.AddRange(templateTagParameters!);
                }
            }

            typeParameters = outerTypeParameters ?? [];
            // PORT-NOTE: TS types `allDeclarations` from `type.symbol.declarations` (`Declaration[]`) and pushes a
            // `declaration` that is a *type node* for a tuple/reference symbol; only `Node` is read from the list, so the
            // port declares the element type as <see cref="Node"/> rather than casting the type node to `IDeclaration`.
            var allDeclarations = (objectFlags & (ObjectFlags.Reference | ObjectFlags.InstantiationExpressionType)) != 0
                ? new List<Node> { declaration }
                : Core.Map(type.Symbol!.Declarations!, (d, _) => (Node)d)!;
            if ((target.ObjectFlags & (ObjectFlags.Reference | ObjectFlags.InstantiationExpressionType)) != 0
                || (target.Symbol!.Flags & SymbolFlags.Method) != 0
                || (target.Symbol!.Flags & SymbolFlags.TypeLiteral) != 0)
            {
                if (target.AliasTypeArguments is null)
                {
                    // PORT-NOTE: Core.Filter answers the input instance when nothing is filtered out (as TS's `filter`
                    // does) and the declared element type is IList; the copy makes the checker's List slot hold a List
                    // in both arms.
                    var filtered = Core.Filter(typeParameters, tp => Core.Some(allDeclarations, d => IsTypeParameterPossiblyReferenced(tp, d)));
                    if (filtered is not null)
                    {
                        typeParameters = [.. filtered];
                    }
                }
            }

            links.OuterTypeParameters = typeParameters;
        }

        if (typeParameters.Count > 0)
        {
            // We are instantiating an anonymous type that has one or more type parameters in scope. Apply the
            // mapper to the type parameters to produce the effective list of type arguments, and compute the
            // instantiation cache key from the type IDs of the type arguments.
            var combinedMapper = CombineTypeMappers((TypeMapper?)((ITypeReference)type).Mapper, mapper);
            var typeArguments = Core.Map(typeParameters, (t, _) => GetMappedType(t, combinedMapper))!;
            var newAliasSymbol = aliasSymbol ?? type.AliasSymbol;
            var newAliasTypeArguments = aliasSymbol is not null ? aliasTypeArguments : InstantiateTypes(type.AliasTypeArguments, mapper);
            var id = GetTypeListId(typeArguments) + GetAliasId(newAliasSymbol, newAliasTypeArguments);
            var targetInstantiations = ((IDeferredTypeReference)target).Instantiations;
            if (targetInstantiations is null)
            {
                targetInstantiations = new Dictionary<string, IType>();
                ((IDeferredTypeReference)target).Instantiations = targetInstantiations;
                targetInstantiations[GetTypeListId([.. typeParameters]) + GetAliasId(target.AliasSymbol, target.AliasTypeArguments)] = target;
            }

            if (!targetInstantiations.TryGetValue(id, out var result))
            {
                var newMapper = CreateTypeMapper(typeParameters, typeArguments);
                if ((target.ObjectFlags & ObjectFlags.SingleSignatureType) != 0)
                {
                    newMapper = CombineTypeMappers(newMapper, mapper);
                }

                result = (target.ObjectFlags & ObjectFlags.Reference) != 0
                    ? CreateDeferredTypeReference(((ITypeReference)type).Target!, ((ITypeReference)type).Node!, newMapper, newAliasSymbol, newAliasTypeArguments)
                    : (target.ObjectFlags & ObjectFlags.Mapped) != 0
                        ? InstantiateMappedType((IMappedType)target, newMapper, newAliasSymbol, newAliasTypeArguments)
                        : InstantiateAnonymousType((IAnonymousType)target, newMapper, newAliasSymbol, newAliasTypeArguments);
                // Set cached result early in case we recursively invoke instantiation while eagerly computing type variable visibility below
                targetInstantiations[id] = result;
                var resultObjectFlags = GetObjectFlags(result);
                if ((result.Flags & TypeFlags.ObjectFlagsType) != 0 && (resultObjectFlags & ObjectFlags.CouldContainTypeVariablesComputed) == 0)
                {
                    // One of the input type arguments might be or contain the result.
                    var resultCouldContainTypeVariables = Core.Some(typeArguments, CouldContainTypeVariables);
                    if ((GetObjectFlags(result) & ObjectFlags.CouldContainTypeVariablesComputed) == 0)
                    {
                        // If `result` is one of the object types we tried to make (it may not be, due to how
                        // `instantiateMappedType` works), we can carry forward the type variable containment check from
                        // the input type arguments.
                        if ((resultObjectFlags & (ObjectFlags.Mapped | ObjectFlags.Anonymous | ObjectFlags.Reference)) != 0)
                        {
                            OrObjectFlags(result, ObjectFlags.CouldContainTypeVariablesComputed | (resultCouldContainTypeVariables ? ObjectFlags.CouldContainTypeVariables : 0));
                        }
                        // If none of the type arguments for the outer type parameters contain type variables, it follows
                        // that the instantiated type doesn't reference type variables.
                        // Intrinsics have `CouldContainTypeVariablesComputed` pre-set, so this should only cover unions and intersections resulting from `instantiateMappedType`.
                        else
                        {
                            OrObjectFlags(result, resultCouldContainTypeVariables ? 0 : ObjectFlags.CouldContainTypeVariablesComputed);
                        }
                    }
                }
            }

            return result;
        }

        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:20809-20812 — <c>maybeTypeParameterReference</c>.</summary>
    internal static bool MaybeTypeParameterReference(Node node)
    {
        var parent = node.Parent!;
        return !(parent.Kind == SyntaxKind.TypeReference && ((TypeReferenceNode)parent).TypeArguments is not null && ReferenceEquals(node, ((TypeReferenceNode)parent).TypeName) ||
            parent.Kind == SyntaxKind.ImportType && ((ImportTypeNode)parent).TypeArguments is not null && ReferenceEquals(node, ((ImportTypeNode)parent).Qualifier));
    }

    /// <summary>ported from: src/compiler/checker.ts:20814-20860 — <c>isTypeParameterPossiblyReferenced</c>.</summary>
    internal bool IsTypeParameterPossiblyReferenced(ITypeParameter tp, Node node)
    {
        // If the type parameter doesn't have exactly one declaration, if there are intervening statement blocks
        // between the node and the type parameter declaration, if the node contains actual references to the
        // type parameter, or if the node contains type queries that we can't prove couldn't contain references to the type parameter,
        // we consider the type parameter possibly referenced.
        if (tp.Symbol is not null && tp.Symbol.Declarations is { Count: 1 })
        {
            var container = ((Node)tp.Symbol.Declarations[0]).Parent!;
            for (Node? n = node; !ReferenceEquals(n, container); n = n!.Parent)
            {
                if (n is null
                    || n.Kind == SyntaxKind.Block
                    || (n.Kind == SyntaxKind.ConditionalType
                        && ForEachChildTable.ForEachChild<bool>((Node)((ConditionalTypeNode)n).ExtendsType!, child => ContainsReference(child)) == true))
                {
                    return true;
                }
            }

            return ContainsReference(node);
        }

        return true;

        bool ContainsReference(Node nodeToSearch)
        {
            switch (nodeToSearch.Kind)
            {
                case SyntaxKind.ThisType:
                    return tp.IsThisType == true;
                case SyntaxKind.Identifier:
                    if (tp.IsThisType == true
                        || !Utilities.IsPartOfTypeNode(nodeToSearch)
                        || !MaybeTypeParameterReference(nodeToSearch))
                    {
                        return false;
                    }
                    // PORT-NOTE: the reference hands the node to `getTypeFromTypeNodeWorker(node as TypeNode)` — a no-op
                    // re-view, because an identifier used as a type reference is a `TypeNode` only structurally. This
                    // port's `Identifier` does not implement the nominal `ITypeNode` (§3's systemic defect), so the cast
                    // cannot express it. The worker's own `case SyntaxKind.Identifier` arm (checker.ts:20527-20531) reads
                    // only the node's symbol and its declared type — `<see cref="GetSymbolAtLocation"/>` then
                    // `GetDeclaredTypeOfSymbol`, with `_errorType` when there is no symbol; those same slots are read
                    // here so the `===`-equality test against `tp` is the worker's result.
                    var identifierSymbol = GetSymbolAtLocation(nodeToSearch);
                    return ReferenceEquals(identifierSymbol is not null ? GetDeclaredTypeOfSymbol(identifierSymbol) : _errorType, tp);
                case SyntaxKind.TypeQuery:
                    var entityName = ((TypeQueryNode)nodeToSearch).ExprName;
                    var firstIdentifier = Utilities.GetFirstIdentifier(entityName!);
                    if (!Utilities.IsThisIdentifier(firstIdentifier))
                    {
                        // Don't attempt to analyze typeof this.xxx.
                        var firstIdentifierSymbol = GetResolvedSymbol(firstIdentifier);
                        var tpDeclaration = (Node)tp.Symbol!.Declarations![0]; // There is exactly one declaration, otherwise `containsReference` is not called
                        var tpScope = tpDeclaration.Kind == SyntaxKind.TypeParameter ? tpDeclaration.Parent : // Type parameter is a regular type parameter, e.g. foo<T>
                            tp.IsThisType == true ? tpDeclaration : // Type parameter is the this type, and its declaration is the class declaration.
                            null; // Type parameter's declaration was unrecognized, e.g. comes from JSDoc annotation.
                        if (firstIdentifierSymbol.Declarations is not null && tpScope is not null)
                        {
                            return Core.Some(firstIdentifierSymbol.Declarations, idDecl => Utilities.IsNodeDescendantOf((Node)idDecl, tpScope)) ||
                                Core.Some(((TypeQueryNode)nodeToSearch).TypeArguments, t => t is not null && ContainsReference((Node)t));
                        }
                    }

                    return true;
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.MethodSignature:
                    {
                        // PORT-NOTE: TS reads these four slots off the structural `FunctionLikeDeclaration`; the two
                        // classes that can appear here (MethodDeclaration, MethodSignature) share
                        // ISignatureDeclarationBase, and only the declaration form has a `body`.
                        var functionLike = (ISignatureDeclarationBase)nodeToSearch;
                        var body = nodeToSearch is MethodDeclaration method ? method.Body : null;
                        return (functionLike.Type is null && body is not null)
                            || Core.Some(functionLike.TypeParameters, t => ContainsReference(t))
                            || Core.Some(functionLike.Parameters, p => ContainsReference(p))
                            || (functionLike.Type is not null && ContainsReference((Node)functionLike.Type));
                    }
            }

            return ForEachChildTable.ForEachChild<bool>(nodeToSearch, child => ContainsReference(child)) == true;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:20862-20871 — <c>getHomomorphicTypeVariable</c>.</summary>
    internal ITypeParameter? GetHomomorphicTypeVariable(IMappedType type)
    {
        var constraintType = GetConstraintTypeFromMappedType(type);
        if ((constraintType.Flags & TypeFlags.Index) != 0)
        {
            var typeVariable = GetActualTypeVariable(((IIndexType)constraintType).Type!);
            if ((typeVariable.Flags & TypeFlags.TypeParameter) != 0)
            {
                return (ITypeParameter)typeVariable;
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:20873-20916 — <c>instantiateMappedType</c>.</summary>
    internal IType InstantiateMappedType(IMappedType type, TypeMapper mapper, ISymbol? aliasSymbol, List<IType>? aliasTypeArguments)
    {
        // For a homomorphic mapped type { [P in keyof T]: X }, where T is some type variable, the mapping
        // operation depends on T as follows:
        // * If T is a primitive type no mapping is performed and the result is simply T.
        // * If T is a union type we distribute the mapped type over the union.
        // * If T is an array we map to an array where the element type has been transformed.
        // * If T is a tuple we map to a tuple where the element types have been transformed.
        // * If T is an intersection of array or tuple types we map to an intersection of transformed array or tuple types.
        // * Otherwise we map to an object type where the type of each property has been transformed.
        // For example, when T is instantiated to a union type A | B, we produce { [P in keyof A]: X } |
        // { [P in keyof B]: X }, and when T is instantiated to a union type A | undefined, we produce
        // { [P in keyof A]: X } | undefined.
        var typeVariable = GetHomomorphicTypeVariable(type);
        if (typeVariable is not null)
        {
            var mappedTypeVariable = InstantiateType(typeVariable, mapper)!;
            if (!ReferenceEquals(typeVariable, mappedTypeVariable))
            {
                return MapTypeWithAlias(GetReducedType(mappedTypeVariable), InstantiateConstituent, aliasSymbol, aliasTypeArguments)!;
            }
        }

        // If the constraint type of the instantiation is the wildcard type, return the wildcard type.
        return ReferenceEquals(InstantiateType(GetConstraintTypeFromMappedType(type), mapper), _wildcardType)
            ? _wildcardType
            : InstantiateAnonymousType(type, mapper, aliasSymbol, aliasTypeArguments);

        IType InstantiateConstituent(IType t)
        {
            if ((t.Flags & (TypeFlags.AnyOrUnknown | TypeFlags.InstantiableNonPrimitive | TypeFlags.Object | TypeFlags.Intersection)) != 0
                && !ReferenceEquals(t, _wildcardType)
                && !IsErrorType(t))
            {
                if (type.Declaration!.NameType is null)
                {
                    IType? constraint;
                    if (IsArrayType(t) || (t.Flags & TypeFlags.Any) != 0
                        && FindResolutionCycleStartIndex(typeVariable, TypeSystemPropertyName.ImmediateBaseConstraint) < 0
                        && (constraint = GetConstraintOfTypeParameter(typeVariable)) is not null
                        && EveryType(constraint, IsArrayOrTupleType))
                    {
                        return InstantiateMappedArrayType(t, type, PrependTypeMapping(typeVariable, t, mapper));
                    }

                    if (IsTupleType(t))
                    {
                        return InstantiateMappedTupleType((ITupleTypeReference)t, type, typeVariable, mapper);
                    }

                    if (IsArrayOrTupleOrIntersection(t))
                    {
                        return GetIntersectionType(Core.Map(((IUnionOrIntersectionType)t).Types!, (u, _) => InstantiateConstituent(u))!);
                    }
                }

                return InstantiateAnonymousType(type, PrependTypeMapping(typeVariable, t, mapper), aliasSymbol: null, aliasTypeArguments: null);
            }

            return t;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:20918-20920 — <c>getModifiedReadonlyState</c>.</summary>
    internal static bool GetModifiedReadonlyState(bool state, MappedTypeModifiers modifiers)
        => (modifiers & MappedTypeModifiers.IncludeReadonly) != 0
            ? true
            : (modifiers & MappedTypeModifiers.ExcludeReadonly) != 0 ? false : state;

    /// <summary>ported from: src/compiler/checker.ts:20922-20948 — <c>instantiateMappedTupleType</c>.</summary>
    /// <remarks>
    /// We apply the mapped type's template type to each of the fixed part elements. For variadic elements, we
    /// apply the mapped type itself to the variadic element type. For other elements in the variable part of the
    /// tuple, we surround the element type with an array type and apply the mapped type to that. This ensures
    /// that we get sequential property key types for the fixed part of the tuple, and property key type number
    /// for the remaining elements.
    /// </remarks>
    internal IType InstantiateMappedTupleType(ITupleTypeReference tupleType, IMappedType mappedType, IType typeVariable, TypeMapper mapper)
    {
        var tupleTarget = (ITupleType)tupleType.Target!;
        var elementFlags = tupleTarget.ElementFlags!;
        var fixedLength = tupleTarget.FixedLength;
        var fixedMapper = fixedLength > 0 ? PrependTypeMapping(typeVariable, tupleType, mapper) : mapper;
        var newElementTypes = Core.Map(GetElementTypes(tupleType)!, (elementType, i) =>
        {
            var flags = elementFlags[i];
            return i < fixedLength
                ? InstantiateMappedTypeTemplate(mappedType, GetStringLiteralType("" + i), (flags & ElementFlags.Optional) != 0, fixedMapper)
                : (flags & ElementFlags.Variadic) != 0
                    ? InstantiateType(mappedType, PrependTypeMapping(typeVariable, elementType, mapper))!
                    : GetElementTypeOfArrayType(InstantiateType(mappedType, PrependTypeMapping(typeVariable, CreateArrayType(elementType), mapper))!) ?? _unknownType;
        })!;
        var modifiers = GetMappedTypeModifiers(mappedType);
        var newElementFlags = (modifiers & MappedTypeModifiers.IncludeOptional) != 0
            ? Core.Map(elementFlags, (f, _) => (f & ElementFlags.Required) != 0 ? ElementFlags.Optional : f)!
            : (modifiers & MappedTypeModifiers.ExcludeOptional) != 0
                ? Core.Map(elementFlags, (f, _) => (f & ElementFlags.Optional) != 0 ? ElementFlags.Required : f)!
                : elementFlags;
        var newReadonly = GetModifiedReadonlyState(tupleTarget.Readonly, GetMappedTypeModifiers(mappedType));
        return Core.Contains(newElementTypes, _errorType)
            ? _errorType
            : CreateTupleType(newElementTypes, newElementFlags, newReadonly, tupleTarget.LabeledElementDeclarations);
    }

    /// <summary>ported from: src/compiler/checker.ts:20950-20954 — <c>instantiateMappedArrayType</c>.</summary>
    internal IType InstantiateMappedArrayType(IType arrayType, IMappedType mappedType, TypeMapper mapper)
    {
        var elementType = InstantiateMappedTypeTemplate(mappedType, _numberType, isOptional: true, mapper);
        return IsErrorType(elementType)
            ? _errorType
            : CreateArrayType(elementType, GetModifiedReadonlyState(IsReadonlyArrayType(arrayType), GetMappedTypeModifiers(mappedType)));
    }

    /// <summary>ported from: src/compiler/checker.ts:20956-20963 — <c>instantiateMappedTypeTemplate</c>.</summary>
    internal IType InstantiateMappedTypeTemplate(IMappedType type, IType key, bool isOptional, TypeMapper mapper)
    {
        var templateMapper = AppendTypeMapping(mapper, GetTypeParameterFromMappedType(type), key);
        var propType = InstantiateType(GetTemplateTypeFromMappedType((IMappedType)type.Target! ?? type), templateMapper)!;
        var modifiers = GetMappedTypeModifiers(type);
        return _strictNullChecks && (modifiers & MappedTypeModifiers.IncludeOptional) != 0 && !MaybeTypeOfKind(propType, TypeFlags.Undefined | TypeFlags.Void)
            ? GetOptionalType(propType, isProperty: true)
            : _strictNullChecks && (modifiers & MappedTypeModifiers.ExcludeOptional) != 0 && isOptional
                ? GetTypeWithFacts(propType, TypeFacts.NEUndefined)
                : propType;
    }

    /// <summary>ported from: src/compiler/checker.ts:20965-20986 — <c>instantiateAnonymousType</c>.</summary>
    internal IAnonymousType InstantiateAnonymousType(IAnonymousType type, TypeMapper mapper, ISymbol? aliasSymbol, List<IType>? aliasTypeArguments)
    {
        Debug.Assert(type.Symbol is not null, "anonymous type must have symbol to be instantiated");
        var flags = (type.ObjectFlags & ~(ObjectFlags.CouldContainTypeVariablesComputed | ObjectFlags.CouldContainTypeVariables)) | ObjectFlags.Instantiated;
        var result = (IAnonymousType)CreateObjectType(flags, type.Symbol);
        if ((type.ObjectFlags & ObjectFlags.Mapped) != 0)
        {
            var typeAsMapped = (IMappedType)type;
            var resultAsMapped = (IMappedType)result;
            resultAsMapped.Declaration = typeAsMapped.Declaration;
            // C.f. instantiateSignature
            var origTypeParameter = GetTypeParameterFromMappedType(typeAsMapped);
            var freshTypeParameter = CloneTypeParameter(origTypeParameter);
            resultAsMapped.TypeParameter = freshTypeParameter;
            mapper = CombineTypeMappers(MakeUnaryTypeMapper(origTypeParameter, freshTypeParameter), mapper);
            freshTypeParameter.Mapper = mapper;
        }

        if ((type.ObjectFlags & ObjectFlags.InstantiationExpressionType) != 0)
        {
            ((IInstantiationExpressionType)result).Node = ((IInstantiationExpressionType)type).Node;
        }

        result.Target = type;
        result.Mapper = mapper;
        result.AliasSymbol = aliasSymbol ?? type.AliasSymbol;
        result.AliasTypeArguments = aliasSymbol is not null ? aliasTypeArguments : InstantiateTypes(type.AliasTypeArguments, mapper);
        result.ObjectFlags |= result.AliasTypeArguments is not null ? GetPropagatingFlagsOfTypes(result.AliasTypeArguments) : 0;
        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:20988-21012 — <c>getConditionalTypeInstantiation</c>.</summary>
    internal IType GetConditionalTypeInstantiation(IConditionalType type, TypeMapper mapper, bool forConstraint, ISymbol? aliasSymbol, List<IType>? aliasTypeArguments)
    {
        var root = type.Root!;
        if (root.OuterTypeParameters is not null)
        {
            // We are instantiating a conditional type that has one or more type parameters in scope. Apply the
            // mapper to the type parameters to produce the effective list of type arguments, and compute the
            // instantiation cache key from the type IDs of the type arguments.
            var typeArguments = Core.Map(root.OuterTypeParameters, (t, _) => GetMappedType(t, mapper))!;
            var id = (forConstraint ? "C" : "") + GetTypeListId(typeArguments) + GetAliasId(aliasSymbol, aliasTypeArguments);
            root.Instantiations ??= new Dictionary<string, IType>();
            if (!root.Instantiations.TryGetValue(id, out var result))
            {
                var newMapper = CreateTypeMapper(root.OuterTypeParameters, typeArguments);
                var checkType = root.CheckType!;
                var distributionType = root.IsDistributive ? GetReducedType(GetMappedType(checkType, newMapper)) : null;
                // Distributive conditional types are distributed over union types. For example, when the
                // distributive conditional type T extends U ? X : Y is instantiated with A | B for T, the
                // result is (A extends U ? X : Y) | (B extends U ? X : Y).
                result = distributionType is not null && !ReferenceEquals(checkType, distributionType) && (distributionType.Flags & (TypeFlags.Union | TypeFlags.Never)) != 0
                    ? MapTypeWithAlias(distributionType, t => GetConditionalType(root, PrependTypeMapping(checkType, t, newMapper), forConstraint), aliasSymbol, aliasTypeArguments)!
                    : GetConditionalType(root, newMapper, forConstraint, aliasSymbol, aliasTypeArguments);
                root.Instantiations[id] = result;
            }

            return result;
        }

        return type;
    }

    // ---------------------------------------------------------------------------------------------
    // instantiateType and its worker (checker.ts:21014-21131)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:21016-21018 — <c>instantiateType</c>.</summary>
    /// <remarks>PORT-NOTE: TS's two overloads differ only in the nullability of their arguments and result; the port declares the one implementation and the call sites assert non-null exactly where TS's overload was selected.</remarks>
    internal IType? InstantiateType(IType? type, TypeMapper? mapper)
        => type is not null && mapper is not null ? InstantiateTypeWithAlias(type, mapper, aliasSymbol: null, aliasTypeArguments: null) : type;

    /// <summary>ported from: src/compiler/checker.ts:21020-21054 — <c>instantiateTypeWithAlias</c>.</summary>
    internal IType InstantiateTypeWithAlias(IType type, TypeMapper mapper, ISymbol? aliasSymbol, List<IType>? aliasTypeArguments)
    {
        if (!CouldContainTypeVariables(type))
        {
            return type;
        }

        if (_instantiationDepth == 100 || _instantiationCount >= 5000000)
        {
            // We have reached 100 recursive type instantiations, or 5M type instantiations caused by the same statement
            // or expression. There is a very high likelihood we're dealing with a combination of infinite generic types
            // that perpetually generate new type identities, so we stop the recursion here by yielding the error type.
            Tracing.Current?.Instant(
                Tracing.Phase.CheckTypes,
                "instantiateType_DepthLimit",
                TracingArgs.Of(("typeId", type.Id), ("instantiationDepth", _instantiationDepth), ("instantiationCount", _instantiationCount)));
            Error(_currentNode, Diagnostics.Type_instantiation_is_excessively_deep_and_possibly_infinite);
            return _errorType;
        }

        var index = FindActiveMapper(mapper);
        if (index == -1)
        {
            PushActiveMapper(mapper);
        }

        var key = type.Id + GetAliasId(aliasSymbol, aliasTypeArguments);
        var mapperCache = _activeTypeMappersCaches[index != -1 ? index : _activeTypeMappersCount - 1];
        if (mapperCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        _totalInstantiationCount++;
        _instantiationCount++;
        _instantiationDepth++;
        var result = InstantiateTypeWorker(type, mapper, aliasSymbol, aliasTypeArguments);
        if (index == -1)
        {
            PopActiveMapper();
        }
        else
        {
            mapperCache[key] = result;
        }

        _instantiationDepth--;
        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:21056-21130 — <c>instantiateTypeWorker</c>.</summary>
    internal IType InstantiateTypeWorker(IType type, TypeMapper mapper, ISymbol? aliasSymbol, List<IType>? aliasTypeArguments)
    {
        var flags = type.Flags;
        if ((flags & TypeFlags.TypeParameter) != 0)
        {
            return GetMappedType(type, mapper);
        }

        if ((flags & TypeFlags.Object) != 0)
        {
            var objectFlags = ((IObjectType)type).ObjectFlags;
            if ((objectFlags & (ObjectFlags.Reference | ObjectFlags.Anonymous | ObjectFlags.Mapped)) != 0)
            {
                if ((objectFlags & ObjectFlags.Reference) != 0 && ((ITypeReference)type).Node is null)
                {
                    var resolvedTypeArguments = ((ITypeReference)type).ResolvedTypeArguments;
                    var newTypeArguments = InstantiateTypes(resolvedTypeArguments, mapper);
                    return !ReferenceEquals(newTypeArguments, resolvedTypeArguments)
                        ? CreateNormalizedTypeReference(((ITypeReference)type).Target!, newTypeArguments)
                        : type;
                }

                if ((objectFlags & ObjectFlags.ReverseMapped) != 0)
                {
                    return InstantiateReverseMappedType((IReverseMappedType)type, mapper);
                }

                return GetObjectTypeInstantiation((IObjectType)type, mapper, aliasSymbol, aliasTypeArguments);
            }

            return type;
        }

        if ((flags & TypeFlags.UnionOrIntersection) != 0)
        {
            var origin = (flags & TypeFlags.Union) != 0 ? ((IUnionType)type).Origin : null;
            var types = origin is not null && (origin.Flags & TypeFlags.UnionOrIntersection) != 0
                ? ((IUnionOrIntersectionType)origin).Types!
                : ((IUnionOrIntersectionType)type).Types!;
            var newTypes = InstantiateTypes(types, mapper);
            if (ReferenceEquals(newTypes, types) && ReferenceEquals(aliasSymbol, type.AliasSymbol))
            {
                return type;
            }

            var newAliasSymbol = aliasSymbol ?? type.AliasSymbol;
            var newAliasTypeArguments = aliasSymbol is not null ? aliasTypeArguments : InstantiateTypes(type.AliasTypeArguments, mapper);
            return (flags & TypeFlags.Intersection) != 0 || (origin is not null && (origin.Flags & TypeFlags.Intersection) != 0)
                ? GetIntersectionType(newTypes!, IntersectionFlags.None, newAliasSymbol, newAliasTypeArguments)
                : GetUnionType(newTypes!, UnionReduction.Literal, newAliasSymbol, newAliasTypeArguments);
        }

        if ((flags & TypeFlags.Index) != 0)
        {
            return GetIndexType(InstantiateType(((IIndexType)type).Type, mapper)!);
        }

        if ((flags & TypeFlags.TemplateLiteral) != 0)
        {
            return GetTemplateLiteralType(((ITemplateLiteralType)type).Texts!, InstantiateTypes(((ITemplateLiteralType)type).Types, mapper)!);
        }

        if ((flags & TypeFlags.StringMapping) != 0)
        {
            return GetStringMappingType(type.Symbol!, InstantiateType(((IStringMappingType)type).Type, mapper)!);
        }

        if ((flags & TypeFlags.IndexedAccess) != 0)
        {
            var newAliasSymbol = aliasSymbol ?? type.AliasSymbol;
            var newAliasTypeArguments = aliasSymbol is not null ? aliasTypeArguments : InstantiateTypes(type.AliasTypeArguments, mapper);
            var indexedAccess = (IIndexedAccessType)type;
            return GetIndexedAccessType(
                InstantiateType(indexedAccess.ObjectType, mapper)!,
                InstantiateType(indexedAccess.IndexType, mapper)!,
                indexedAccess.AccessFlags,
                accessNode: null,
                newAliasSymbol,
                newAliasTypeArguments);
        }

        if ((flags & TypeFlags.Conditional) != 0)
        {
            var conditional = (IConditionalType)type;
            return GetConditionalTypeInstantiation(
                conditional,
                CombineTypeMappers((TypeMapper?)conditional.Mapper, mapper),
                forConstraint: false,
                aliasSymbol,
                aliasTypeArguments);
        }

        if ((flags & TypeFlags.Substitution) != 0)
        {
            var substitution = (ISubstitutionType)type;
            var newBaseType = InstantiateType(substitution.BaseType, mapper)!;
            if (IsNoInferType(type))
            {
                return GetNoInferType(newBaseType);
            }

            var newConstraint = InstantiateType(substitution.Constraint, mapper)!;
            // A substitution type originates in the true branch of a conditional type and can be resolved
            // to just the base type in the same cases as the conditional type resolves to its true branch
            // (because the base type is then known to satisfy the constraint).
            if ((newBaseType.Flags & TypeFlags.TypeVariable) != 0 && IsGenericType(newConstraint))
            {
                return GetSubstitutionType(newBaseType, newConstraint);
            }

            if ((newConstraint.Flags & TypeFlags.AnyOrUnknown) != 0 || IsTypeAssignableTo(GetRestrictiveInstantiation(newBaseType), GetRestrictiveInstantiation(newConstraint)))
            {
                return newBaseType;
            }

            return (newBaseType.Flags & TypeFlags.TypeVariable) != 0
                ? GetSubstitutionType(newBaseType, newConstraint)
                : GetIntersectionType([newConstraint, newBaseType]);
        }

        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:21132-21150 — <c>instantiateReverseMappedType</c>.</summary>
    internal IType InstantiateReverseMappedType(IReverseMappedType type, TypeMapper mapper)
    {
        var innerMappedType = InstantiateType(type.MappedType, mapper)!;
        if ((GetObjectFlags(innerMappedType) & ObjectFlags.Mapped) == 0)
        {
            return type;
        }

        var innerIndexType = InstantiateType(type.ConstraintType, mapper)!;
        if ((innerIndexType.Flags & TypeFlags.Index) == 0)
        {
            return type;
        }

        var instantiated = InferTypeForHomomorphicMappedType(
            InstantiateType(type.Source, mapper)!,
            (IMappedType)innerMappedType,
            (IIndexType)innerIndexType);
        if (instantiated is not null)
        {
            return instantiated;
        }

        // Nested invocation of `inferTypeForHomomorphicMappedType` or the `source` instantiated into something unmappable.
        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:21152-21155 — <c>getPermissiveInstantiation</c>.</summary>
    internal IType GetPermissiveInstantiation(IType type)
    {
        if ((type.Flags & (TypeFlags.Primitive | TypeFlags.AnyOrUnknown | TypeFlags.Never)) != 0)
        {
            return type;
        }

        return type.PermissiveInstantiation ??= InstantiateType(type, PermissiveMapper)!;
    }

    /// <summary>ported from: src/compiler/checker.ts:21157-21172 — <c>getRestrictiveInstantiation</c>.</summary>
    /// <remarks>
    /// We set <c>restrictiveInstantiation.restrictiveInstantiation</c> so we don't attempt to set the restrictive
    /// instance of a restrictive instance, which is redundant — we'd produce new type identities, but all type
    /// parameters have already been mapped. This also gives us a way to detect restrictive instances upon comparisons
    /// and disable the "distributive constraint" assignability check for them.
    /// </remarks>
    internal IType GetRestrictiveInstantiation(IType type)
    {
        if ((type.Flags & (TypeFlags.Primitive | TypeFlags.AnyOrUnknown | TypeFlags.Never)) != 0)
        {
            return type;
        }

        if (type.RestrictiveInstantiation is not null)
        {
            return type.RestrictiveInstantiation;
        }

        var restrictive = InstantiateType(type, RestrictiveMapper)!;
        type.RestrictiveInstantiation = restrictive;
        restrictive.RestrictiveInstantiation = restrictive;
        return restrictive;
    }

    /// <summary>ported from: src/compiler/checker.ts:21174-21176 — <c>instantiateIndexInfo</c>.</summary>
    /// <remarks>PORT-NOTE: the <c>IIndexInfo</c> face, as <see cref="InstantiateIndexInfos"/> records.</remarks>
    internal IIndexInfo InstantiateIndexInfo(IIndexInfo info, TypeMapper mapper)
        => CreateIndexInfo(info.KeyType!, InstantiateType(info.Type, mapper)!, info.IsReadonly, info.Declaration, info.Components);

    /// <summary>The <c>objectFlags</c> writer of <c>getObjectTypeInstantiation</c> (checker.ts:20793-20804).</summary>
    /// <remarks>
    /// PORT-NOTE: TS writes <c>(result as ObjectFlagsType).objectFlags |= …</c>, where <c>ObjectFlagsType</c>
    /// (types.ts:6596) is the union <c>NullableType | ObjectType | UnionType | IntersectionType</c>. C# has no common
    /// view of those four faces, so the port switches over the interfaces that declare the slot — the same switch
    /// <c>GetObjectFlags</c> reads with (its counterpart, in Checker.Support.cs). The default arm is unreachable
    /// because the caller has already tested <c>result.flags &amp; TypeFlags.ObjectFlagsType</c>.
    /// </remarks>
    private static void OrObjectFlags(IType type, ObjectFlags flags)
    {
        switch (type)
        {
            case IIntrinsicType intrinsicType:
                intrinsicType.ObjectFlags |= flags;
                break;
            case IObjectType objectType:
                objectType.ObjectFlags |= flags;
                break;
            case IUnionOrIntersectionType unionOrIntersectionType:
                unionOrIntersectionType.ObjectFlags |= flags;
                break;
            default:
                Debug.Fail("an ObjectFlagsType must carry an objectFlags slot");
                break;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The active-mapper stack (checker.ts:32915-32944)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:32915-32919 — <c>pushActiveMapper</c>.</summary>
    private void PushActiveMapper(TypeMapper mapper)
    {
        while (_activeTypeMappers.Count <= _activeTypeMappersCount)
        {
            _activeTypeMappers.Add(null);
        }

        while (_activeTypeMappersCaches.Count <= _activeTypeMappersCount)
        {
            _activeTypeMappersCaches.Add([]);
        }

        _activeTypeMappers[_activeTypeMappersCount] = mapper;
        _activeTypeMappersCount++;
    }

    /// <summary>ported from: src/compiler/checker.ts:32921-32926 — <c>popActiveMapper</c>.</summary>
    private void PopActiveMapper()
    {
        _activeTypeMappersCount--;
        // Clear out the popped element's referenced objects.
        _activeTypeMappers[_activeTypeMappersCount] = null;
        _activeTypeMappersCaches[_activeTypeMappersCount].Clear();
    }

    /// <summary>ported from: src/compiler/checker.ts:32928-32934 — <c>findActiveMapper</c>.</summary>
    private int FindActiveMapper(TypeMapper mapper)
    {
        for (var i = _activeTypeMappersCount - 1; i >= 0; i--)
        {
            if (ReferenceEquals(mapper, _activeTypeMappers[i]))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>ported from: src/compiler/checker.ts:32937-32941 — <c>clearActiveMapperCaches</c>.</summary>
    /// <remarks>PORT-NOTE: its only caller is <c>checkSourceFileWorker</c> (checker.ts:51252, M6-5).</remarks>
    internal void ClearActiveMapperCaches()
    {
        for (var i = _activeTypeMappersCount - 1; i >= 0; i--)
        {
            _activeTypeMappersCaches[i].Clear();
        }
    }
}
