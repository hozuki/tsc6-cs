namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:13721-13752 (createInstantiatedSymbolTable, addInheritedMembers, isStaticPrivateIdentifierProperty)
// ported from: src/compiler/checker.ts:13753-13777 (resolveDeclaredMembers)
// ported from: src/compiler/checker.ts:14055-14123 (getTypeWithThisArgument, resolveObjectTypeMembers, resolveClassOrInterfaceMembers, resolveTypeReferenceMembers)
// ported from: src/compiler/checker.ts:14124-14570 (createSignature … resolveIntersectionTypeMembers)
// ported from: src/compiler/checker.ts:15022-15057 (resolveStructuredTypeMembers)
// ported from: src/compiler/checker.ts:15799-15803 (getPropertyOfUnionOrIntersectionType)
/// <summary>
/// The members of a structured type: how a class, interface, type reference, union, intersection or anonymous type
/// gets its <c>properties</c>/<c>callSignatures</c>/<c>constructSignatures</c>/<c>indexInfos</c>, and the signature
/// constructors and union-signature combiners those members are made of.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: the reference's <c>StructuredType</c> is the union <c>ObjectType | UnionType | IntersectionType</c>; the
/// port uses <see cref="UnionOrIntersectionType"/>, which is the nearest common class of all three and already carries
/// the five slots (<c>Model/UnionOrIntersectionType.cs</c>), so the <c>as ResolvedType</c> casts the reference performs
/// on the way in and out are no-ops here.
/// </para>
/// <para>
/// PORT-NOTE: this file calls members of other slices by the reference's names — <c>CreateTypeMapper</c>,
/// <c>GetTypeArguments</c>, <c>CreateTypeReference</c>, <c>InstantiateType</c>, <c>InstantiateSymbol</c>,
/// <c>InstantiateSignatures</c>, <c>InstantiateIndexInfos</c> and <c>GetIndexSymbol</c>/<c>GetIndexInfosOfSymbol</c> are
/// M6Generics' or M6-5's, and every one of them is listed in the slice report.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // Declared members (checker.ts:13721-13777)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:13721-13728 — <c>createInstantiatedSymbolTable</c>.</summary>
    /// <remarks>
    /// The <c>mappingThisOnly</c> flag indicates that the only type parameter being mapped is "this". When the flag is
    /// true, we check symbols to see if we can quickly conclude they are free of "this" references, thus needing no
    /// instantiation.
    /// </remarks>
    internal SymbolTable CreateInstantiatedSymbolTable(IList<ISymbol> symbols, TypeMapper mapper, bool mappingThisOnly)
    {
        var result = Utilities.CreateSymbolTable();
        foreach (var symbol in symbols)
        {
            result[symbol.EscapedName ?? ""] = mappingThisOnly && IsThisless(symbol) ? symbol : InstantiateSymbol(symbol, mapper);
        }

        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:13729-13746 — <c>addInheritedMembers</c>.</summary>
    internal void AddInheritedMembers(SymbolTable symbols, IList<ISymbol> baseSymbols)
    {
        foreach (var baseSymbol in baseSymbols)
        {
            if (IsStaticPrivateIdentifierProperty(baseSymbol))
            {
                continue;
            }

            var derived = symbols.GetValueOrDefault(baseSymbol.EscapedName ?? "");
            if (derived is null
                // non-constructor/static-block assignment declarations are ignored here; they're not treated as overrides
                || (derived.ValueDeclaration is not null
                    && NodeTests.IsBinaryExpression((Node)derived.ValueDeclaration)
                    && !IsConstructorDeclaredProperty(derived)
                    && Utilities.GetContainingClassStaticBlock((Node)derived.ValueDeclaration!) is null))
            {
                symbols[baseSymbol.EscapedName ?? ""] = baseSymbol;
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:13749-13751 — <c>isStaticPrivateIdentifierProperty</c>.</summary>
    private static bool IsStaticPrivateIdentifierProperty(ISymbol s)
        => s.ValueDeclaration is not null
            && Utilities.IsPrivateIdentifierClassElementDeclaration((Node)s.ValueDeclaration)
            && Utilities.IsStatic((Node)s.ValueDeclaration!);

    /// <summary>ported from: src/compiler/checker.ts:13753-13767 — <c>resolveDeclaredMembers</c>.</summary>
    internal InterfaceTypeWithDeclaredMembers ResolveDeclaredMembers(InterfaceType type)
    {
        var declared = (InterfaceTypeWithDeclaredMembers)type;
        if (declared.DeclaredProperties is null)
        {
            var symbol = type.Symbol!;
            var members = GetMembersOfSymbol(symbol);
            declared.DeclaredProperties = GetNamedMembers(members, symbol);
            // Start with signatures at empty array in case of recursive types
            declared.DeclaredCallSignatures = [];
            declared.DeclaredConstructSignatures = [];
            declared.DeclaredIndexInfos = [];

            declared.DeclaredCallSignatures = GetSignaturesOfSymbol(members.GetValueOrDefault(InternalSymbolName.Call.Value));
            declared.DeclaredConstructSignatures = GetSignaturesOfSymbol(members.GetValueOrDefault(InternalSymbolName.New.Value));
            declared.DeclaredIndexInfos = GetIndexInfosOfSymbol(symbol);
        }

        return declared;
    }

    // ---------------------------------------------------------------------------------------------
    // The members of an object type (checker.ts:14055-14123)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:14055-14066 — <c>getTypeWithThisArgument</c>.</summary>
    internal IType GetTypeWithThisArgument(IType type, IType? thisArgument = null, bool? needApparentType = null)
    {
        if ((GetObjectFlags(type) & ObjectFlags.Reference) != 0)
        {
            var reference = (ITypeReference)type;
            var target = reference.Target!;
            var typeArguments = GetTypeArguments(reference);
            return Core.Length(target.TypeParameters) == Core.Length(typeArguments)
                ? CreateTypeReference(target, Core.Concatenate(typeArguments, [thisArgument ?? target.ThisType!])!)
                : type;
        }
        else if ((type.Flags & TypeFlags.Intersection) != 0)
        {
            var types = Core.SameMap(((IIntersectionType)type).Types, (t, _) => GetTypeWithThisArgument(t, thisArgument, needApparentType));
            return !ReferenceEquals(types, ((IIntersectionType)type).Types) ? GetIntersectionType(types!) : type;
        }

        return needApparentType == true ? GetApparentType(type) : type;
    }

    /// <summary>ported from: src/compiler/checker.ts:14068-14110 — <c>resolveObjectTypeMembers</c>.</summary>
    internal void ResolveObjectTypeMembers(ObjectType type, InterfaceTypeWithDeclaredMembers source, IList<ITypeParameter> typeParameters, IList<IType> typeArguments)
    {
        TypeMapper? mapper = null;
        SymbolTable members;
        IList<ISignature> callSignatures;
        IList<ISignature> constructSignatures;
        IList<IIndexInfo> indexInfos;
        // PORT-NOTE: TS's `rangeEquals(typeParameters, typeArguments, 0, typeParameters.length)` relies on its
        // structural typing to accept a `TypeParameter[]` where a `Type[]` is expected; the port states the same loop
        // over the wider face, which is also what `equateValues` compares (reference equality for objects).
        var typeParametersAreTypeArguments = true;
        for (var i = 0; i < typeParameters.Count; i++)
        {
            if (!ReferenceEquals(typeParameters[i], typeArguments[i]))
            {
                typeParametersAreTypeArguments = false;
                break;
            }
        }

        if (typeParametersAreTypeArguments)
        {
            members = source.Symbol is not null ? GetMembersOfSymbol(source.Symbol) : CreateSymbolTableFromSymbols(source.DeclaredProperties!);
            callSignatures = source.DeclaredCallSignatures!;
            constructSignatures = source.DeclaredConstructSignatures!;
            indexInfos = source.DeclaredIndexInfos!;
        }
        else
        {
            mapper = CreateTypeMapper(typeParameters, typeArguments);
            members = CreateInstantiatedSymbolTable(source.DeclaredProperties!, mapper, /*mappingThisOnly*/ typeParameters.Count == 1);
            callSignatures = InstantiateSignatures(source.DeclaredCallSignatures!, mapper);
            constructSignatures = InstantiateSignatures(source.DeclaredConstructSignatures!, mapper);
            indexInfos = InstantiateIndexInfos(source.DeclaredIndexInfos!, mapper);
        }

        var baseTypes = GetBaseTypes(source);
        if (baseTypes.Count != 0)
        {
            if (source.Symbol is not null && ReferenceEquals(members, GetMembersOfSymbol(source.Symbol)))
            {
                var symbolTable = Utilities.CreateSymbolTable();
                foreach (var property in source.DeclaredProperties!)
                {
                    symbolTable[property.EscapedName ?? ""] = property;
                }

                // copy index signature symbol as well (for quickinfo)
                var sourceIndex = GetIndexSymbol(source.Symbol);
                if (sourceIndex is not null)
                {
                    symbolTable[InternalSymbolName.Index.Value] = sourceIndex;
                }

                members = symbolTable;
            }

            SetStructuredTypeMembers(type, members, callSignatures, constructSignatures, indexInfos);
            var thisArgument = Core.LastOrUndefined(typeArguments);
            foreach (var baseType in baseTypes)
            {
                var instantiatedBaseType = thisArgument is not null ? GetTypeWithThisArgument(InstantiateType(baseType, mapper), thisArgument) : baseType;
                AddInheritedMembers(members, GetPropertiesOfType(instantiatedBaseType));
                callSignatures = Core.Concatenate(callSignatures, GetSignaturesOfType(instantiatedBaseType, SignatureKind.Call))!;
                constructSignatures = Core.Concatenate(constructSignatures, GetSignaturesOfType(instantiatedBaseType, SignatureKind.Construct))!;
                var inheritedIndexInfos = !ReferenceEquals(instantiatedBaseType, _anyType) ? GetIndexInfosOfType(instantiatedBaseType) : [_anyBaseTypeIndexInfo];
                indexInfos = Core.Concatenate(indexInfos, Core.Filter(inheritedIndexInfos, info => FindIndexInfo(indexInfos, info.KeyType!) is null))!;
            }
        }

        SetStructuredTypeMembers(type, members, callSignatures, constructSignatures, indexInfos);
    }

    /// <summary>ported from: src/compiler/checker.ts:14112-14114 — <c>resolveClassOrInterfaceMembers</c>.</summary>
    internal void ResolveClassOrInterfaceMembers(InterfaceType type)
        => ResolveObjectTypeMembers(type, ResolveDeclaredMembers(type), [], []);

    /// <summary>ported from: src/compiler/checker.ts:14116-14122 — <c>resolveTypeReferenceMembers</c>.</summary>
    internal void ResolveTypeReferenceMembers(TypeReference type)
    {
        var source = ResolveDeclaredMembers((InterfaceType)type.Target!);
        var typeParameters = Core.Concatenate(source.TypeParameters, [source.ThisType!])!;
        var typeArguments = GetTypeArguments(type);
        var paddedTypeArguments = typeArguments.Count == typeParameters.Count ? typeArguments : Core.Concatenate(typeArguments, [type])!;
        ResolveObjectTypeMembers(type, source, typeParameters, paddedTypeArguments);
    }

    // ---------------------------------------------------------------------------------------------
    // Signatures (checker.ts:14124-14570)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:14124-14146 — <c>createSignature</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference assigns the three array slots by reference; the generated <c>Signature</c> slots are
    /// <c>List&lt;…&gt;?</c> and this member's parameters are the <c>IList</c> faces, so the two list slots are copied
    /// (<c>[.. parameters]</c>). No caller compares a signature's parameter list against an array it passed in.
    /// </remarks>
    internal Signature CreateSignature(
        Node? declaration,
        List<ITypeParameter>? typeParameters,
        ISymbol? thisParameter,
        List<ISymbol>? parameters,
        IType? resolvedReturnType,
        ITypePredicateBase? resolvedTypePredicate,
        int minArgumentCount,
        SignatureFlags flags)
    {
        var sig = new Signature(this, flags);
        sig.Declaration = declaration;
        sig.TypeParameters = typeParameters;
        sig.Parameters = parameters;
        sig.ThisParameter = thisParameter;
        sig.ResolvedReturnType = resolvedReturnType;
        sig.ResolvedTypePredicate = resolvedTypePredicate;
        sig.MinArgumentCount = minArgumentCount;
        sig.ResolvedMinArgumentCount = null;
        sig.Target = null;
        sig.Mapper = null;
        sig.CompositeSignatures = null;
        sig.CompositeKind = null;
        return sig;
    }

    /// <summary>ported from: src/compiler/checker.ts:14150-14157 — <c>cloneSignature</c>.</summary>
    internal Signature CloneSignature(ISignature sig)
        => CloneSignature((Signature)sig);

    /// <summary>ported from: src/compiler/checker.ts:14150-14157 — <c>cloneSignature</c>.</summary>
    internal Signature CloneSignature(Signature sig)
    {
        var result = CreateSignature(sig.Declaration, sig.TypeParameters, sig.ThisParameter, sig.Parameters ?? [], /*resolvedReturnType*/ null, /*resolvedTypePredicate*/ null, sig.MinArgumentCount, sig.Flags & SignatureFlags.PropagatingFlags);
        result.Target = sig.Target;
        result.Mapper = sig.Mapper;
        result.CompositeSignatures = sig.CompositeSignatures;
        result.CompositeKind = sig.CompositeKind;
        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:14159-14166 — <c>createUnionSignature</c>.</summary>
    internal Signature CreateUnionSignature(ISignature signature, IList<ISignature> unionSignatures)
    {
        var result = CloneSignature(signature);
        result.CompositeSignatures = [.. unionSignatures];
        result.CompositeKind = TypeFlags.Union;
        result.Target = null;
        result.Mapper = null;
        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:14168-14178 — <c>getOptionalCallSignature</c>.</summary>
    internal Signature GetOptionalCallSignature(Signature signature, SignatureFlags callChainFlags)
    {
        if ((signature.Flags & SignatureFlags.CallChainFlags) == callChainFlags)
        {
            return signature;
        }

        if (signature.OptionalCallSignatureCache is not Dictionary<string, Signature> cache)
        {
            cache = new Dictionary<string, Signature>(StringComparer.Ordinal);
            signature.OptionalCallSignatureCache = cache;
        }

        var key = callChainFlags == SignatureFlags.IsInnerCallChain ? "inner" : "outer";
        return cache.GetValueOrDefault(key) ?? (cache[key] = CreateOptionalCallSignature(signature, callChainFlags));
    }

    /// <summary>ported from: src/compiler/checker.ts:14180-14185 — <c>createOptionalCallSignature</c>.</summary>
    internal Signature CreateOptionalCallSignature(Signature signature, SignatureFlags callChainFlags)
    {
        Debug.Assert(callChainFlags == SignatureFlags.IsInnerCallChain || callChainFlags == SignatureFlags.IsOuterCallChain, "An optional call signature can either be for an inner call chain or an outer call chain, but not both.");
        var result = CloneSignature(signature);
        result.Flags |= callChainFlags;
        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:14187-14200 — <c>getExpandedParameters</c>.</summary>
    internal IList<List<ISymbol>> GetExpandedParameters(ISignature sig, bool? skipUnionExpanding = null)
    {
        if (SignatureHasRestParameter(sig))
        {
            var restIndex = sig.Parameters!.Count - 1;
            var restSymbol = sig.Parameters[restIndex];
            var restType = GetTypeOfSymbol(restSymbol);
            if (IsTupleType(restType))
            {
                return [ExpandSignatureParametersWithTupleMembers((ITupleTypeReference)restType, restIndex, restSymbol)];
            }
            else if (skipUnionExpanding != true && (restType.Flags & TypeFlags.Union) != 0 && Core.Every(((IUnionType)restType).Types, (IType t, int _) => IsTupleType(t)))
            {
                return Core.Map(((IUnionType)restType).Types, (t, _) => ExpandSignatureParametersWithTupleMembers((ITupleTypeReference)t, restIndex, restSymbol))!;
            }
        }

        return [sig.Parameters!];

        List<ISymbol> ExpandSignatureParametersWithTupleMembers(ITupleTypeReference restType, int restIndex, ISymbol restSymbol)
        {
            var elementTypes = GetTypeArguments(restType);
            var associatedNames = GetUniqAssociatedNamesFromTupleType(restType, restSymbol);
            var restParams = Core.Map<IType, ISymbol>(elementTypes, (t, i) =>
            {
                // Lookup the label from the individual tuple passed in before falling back to the signature `rest` parameter name
                var name = associatedNames is not null && associatedNames[i] is not null ? associatedNames[i] : GetParameterNameAtPosition(sig, restIndex + i, restType);
                var flags = ((ITupleType)restType.Target!).ElementFlags![i];
                var checkFlags = (flags & ElementFlags.Variable) != 0 ? CheckFlags.RestParameter
                    : (flags & ElementFlags.Optional) != 0 ? CheckFlags.OptionalParameter : CheckFlags.None;
                var symbol = CreateSymbol(SymbolFlags.FunctionScopedVariable, name, checkFlags);
                GetSymbolLinks(symbol).Type = (flags & ElementFlags.Rest) != 0 ? CreateArrayType(t) : t;
                return symbol;
            })!;
            return [.. sig.Parameters!.Take(restIndex), .. restParams];

            List<string>? GetUniqAssociatedNamesFromTupleType(ITupleTypeReference type, ISymbol restSymbol)
            {
                var names = Core.Map<Node, string>(((ITupleType)type.Target!).LabeledElementDeclarations!, (labeledElement, i) => GetTupleElementLabel((Node?)labeledElement, i, ((ITupleType)type.Target!).ElementFlags![i], restSymbol));
                if (names is not null)
                {
                    var duplicates = new List<int>();
                    var uniqueNames = new HashSet<string>(StringComparer.Ordinal);
                    for (var i = 0; i < names.Count; i++)
                    {
                        var name = names[i];
                        if (!uniqueNames.Add(name!))
                        {
                            duplicates.Add(i);
                        }
                    }

                    var counters = new Dictionary<string, int>(StringComparer.Ordinal);
                    foreach (var i in duplicates)
                    {
                        var counter = counters.GetValueOrDefault(names[i]!) != 0 ? counters[names[i]!] : 1;
                        string name;
                        while (!uniqueNames.Add(name = $"{names[i]}_{counter}"))
                        {
                            counter++;
                        }

                        names[i] = name;
                        counters[name] = counter + 1;
                    }
                }

                return names;
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:14244-14269 — <c>getDefaultConstructSignatures</c>.</summary>
    internal List<Signature> GetDefaultConstructSignatures(InterfaceType classType)
    {
        var baseConstructorType = GetBaseConstructorTypeOfClass(classType);
        var baseSignatures = GetSignaturesOfType(baseConstructorType, SignatureKind.Construct);
        var declaration = GetClassLikeDeclarationOfSymbol(classType.Symbol!);
        var isAbstract = declaration is not null && Utilities.HasSyntacticModifier(declaration, ModifierFlags.Abstract);
        if (baseSignatures.Count == 0)
        {
            return [CreateSignature(/*declaration*/ null, classType.LocalTypeParameters, /*thisParameter*/ null, [], classType, /*resolvedTypePredicate*/ null, 0, isAbstract ? SignatureFlags.Abstract : SignatureFlags.None)];
        }

        var baseTypeNode = GetBaseTypeNodeOfClass(classType)!;
        var isJavaScript = Utilities.IsInJSFile(baseTypeNode);
        var typeArguments = TypeArgumentsFromTypeReferenceNode(baseTypeNode);
        var typeArgCount = Core.Length(typeArguments);
        var result = new List<Signature>();
        foreach (var baseSig in baseSignatures)
        {
            var minTypeArgumentCount = GetMinTypeArgumentCount(baseSig.TypeParameters);
            var typeParamCount = Core.Length(baseSig.TypeParameters);
            if (isJavaScript || (typeArgCount >= minTypeArgumentCount && typeArgCount <= typeParamCount))
            {
                var sig = typeParamCount != 0
                    ? CreateSignatureInstantiation(baseSig, FillMissingTypeArguments(typeArguments!, baseSig.TypeParameters, minTypeArgumentCount, isJavaScript))
                    : CloneSignature(baseSig);
                sig.TypeParameters = classType.LocalTypeParameters;
                sig.ResolvedReturnType = classType;
                sig.Flags = isAbstract ? sig.Flags | SignatureFlags.Abstract : sig.Flags & ~SignatureFlags.Abstract;
                result.Add(sig);
            }
        }

        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:14271-14277 — <c>findMatchingSignature</c>.</summary>
    internal ISignature? FindMatchingSignature(IList<ISignature> signatureList, ISignature signature, bool partialMatch, bool ignoreThisTypes, bool ignoreReturnTypes)
    {
        foreach (var s in signatureList)
        {
            if (CompareSignaturesIdentical(s, signature, partialMatch, ignoreThisTypes, ignoreReturnTypes, partialMatch ? CompareTypesSubtypeOf : CompareTypesIdentical))
            {
                return s;
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:14279-14311 — <c>findMatchingSignatures</c>.</summary>
    internal List<ISignature>? FindMatchingSignatures(IList<List<ISignature>> signatureLists, ISignature signature, int listIndex)
    {
        if (signature.TypeParameters is not null)
        {
            // We require an exact match for generic signatures, so we only return signatures from the first
            // signature list and only if they have exact matches in the other signature lists.
            if (listIndex > 0)
            {
                return null;
            }

            for (var i = 1; i < signatureLists.Count; i++)
            {
                if (FindMatchingSignature(signatureLists[i], signature, /*partialMatch*/ false, /*ignoreThisTypes*/ false, /*ignoreReturnTypes*/ false) is null)
                {
                    return null;
                }
            }

            return [signature];
        }

        List<ISignature>? result = null;
        for (var i = 0; i < signatureLists.Count; i++)
        {
            // Allow matching non-generic signatures to have excess parameters (as a fallback if exact parameter match is
            // not found) and different return types. Prefer matching this types if possible.
            var match = i == listIndex
                ? signature
                : FindMatchingSignature(signatureLists[i], signature, /*partialMatch*/ false, /*ignoreThisTypes*/ false, /*ignoreReturnTypes*/ true)
                    ?? FindMatchingSignature(signatureLists[i], signature, /*partialMatch*/ true, /*ignoreThisTypes*/ false, /*ignoreReturnTypes*/ true);
            if (match is null)
            {
                return null;
            }

            result = (List<ISignature>)Core.AppendIfUnique(result, match);
        }

        return result;
    }

    /// <summary>
    /// ported from: src/compiler/checker.ts:14313-14363 — <c>getUnionSignatures</c>.
    /// </summary>
    /// <remarks>
    /// The signatures of a union type are those signatures that are present in each of the constituent types. Generic
    /// signatures must match exactly, but non-generic signatures are allowed to have extra optional parameters and may
    /// differ in return types. When signatures differ in return types, the resulting return type is the union of the
    /// constituent return types.
    /// </remarks>
    internal List<ISignature> GetUnionSignatures(IList<List<ISignature>> signatureLists)
    {
        List<ISignature>? result = null;
        var indexWithLengthOverOne = -2; // -2 = unset, -1 = multiple overload sets, {index} = the one overload set
        for (var i = 0; i < signatureLists.Count; i++)
        {
            if (signatureLists[i].Count == 0) return [];
            if (signatureLists[i].Count > 1)
            {
                indexWithLengthOverOne = indexWithLengthOverOne == -2 ? i : -1; // -1 is a signal there are multiple overload sets
            }

            foreach (var signature in signatureLists[i])
            {
                // Only process signatures with parameter lists that aren't already in the result list
                if (result is null || FindMatchingSignature(result, signature, /*partialMatch*/ false, /*ignoreThisTypes*/ false, /*ignoreReturnTypes*/ true) is null)
                {
                    var unionSignatures = FindMatchingSignatures(signatureLists, signature, i);
                    if (unionSignatures is not null)
                    {
                        var s = signature;
                        // Union the result types when more than one signature matches
                        if (unionSignatures.Count > 1)
                        {
                            var thisParameter = signature.ThisParameter;
                            var firstThisParameterOfUnionSignatures = Core.ForEach(unionSignatures, (ISignature sig, int _) => sig.ThisParameter);
                            if (firstThisParameterOfUnionSignatures is not null)
                            {
                                var thisType = GetIntersectionType(Core.MapDefined(unionSignatures, (ISignature sig, int _) => sig.ThisParameter is not null ? GetTypeOfSymbol(sig.ThisParameter) : null));
                                thisParameter = CreateSymbolWithType(firstThisParameterOfUnionSignatures, thisType);
                            }

                            s = CreateUnionSignature(signature, unionSignatures);
                            s.ThisParameter = thisParameter;
                        }

                        (result ??= []).Add(s);
                    }
                }
            }
        }

        if (Core.Length(result) == 0 && indexWithLengthOverOne != -1)
        {
            // No sufficiently similar signature existed to subsume all the other signatures in the union - time to see if
            // we can make a single signature that handles all over them. We only do this when there are overloads in only
            // one constituent. (Overloads are conditional in nature and having overloads in multiple constituents would
            // necessitate making a power set of signatures from the type, whose ordering would be non-obvious)
            var masterList = signatureLists[indexWithLengthOverOne != -2 ? indexWithLengthOverOne : 0];
            List<ISignature>? results = [.. masterList];
            foreach (var signatures in signatureLists)
            {
                if (!ReferenceEquals(signatures, masterList))
                {
                    var signature = signatures[0];
                    Debug.Assert(signature is not null, "getUnionSignatures bails early on empty signature lists and should not have empty lists on second pass");
                    results = signature.TypeParameters is not null && Core.Some(results, s => s.TypeParameters is not null && !CompareTypeParametersIdentical(signature.TypeParameters, s.TypeParameters))
                        ? null
                        : Core.Map<ISignature, ISignature>(results, (ISignature sig, int _) => CombineSignaturesOfUnionMembers(sig, signature));
                    if (results is null)
                    {
                        break;
                    }
                }
            }

            result = results;
        }

        return result ?? [];
    }

    /// <summary>ported from: src/compiler/checker.ts:14365-14387 — <c>compareTypeParametersIdentical</c>.</summary>
    internal bool CompareTypeParametersIdentical(IList<ITypeParameter>? sourceParams, IList<ITypeParameter>? targetParams)
    {
        if (Core.Length(sourceParams) != Core.Length(targetParams))
        {
            return false;
        }

        if (sourceParams is null || targetParams is null)
        {
            return true;
        }

        var mapper = CreateTypeMapper(targetParams, sourceParams);
        for (var i = 0; i < sourceParams.Count; i++)
        {
            var source = sourceParams[i];
            var target = targetParams[i];
            if (ReferenceEquals(source, target)) continue;
            // We instantiate the target type parameter constraints into the source types so we can recognize
            // `<T, U extends T>` as the same as `<A, B extends A>`
            if (!IsTypeIdenticalTo(GetConstraintFromTypeParameter(source) ?? _unknownType, InstantiateType(GetConstraintFromTypeParameter(target) ?? _unknownType, mapper))) return false;
            // We don't compare defaults - we just use the type parameter defaults from the first signature that seems to
            // match.
        }

        return true;
    }

    /// <summary>ported from: src/compiler/checker.ts:14389-14398 — <c>combineUnionThisParam</c>.</summary>
    internal ISymbol? CombineUnionThisParam(ISymbol? left, ISymbol? right, TypeMapper? mapper)
    {
        if (left is null || right is null)
        {
            return left ?? right;
        }

        // A signature `this` type might be a read or a write position... It's very possible that it should be invariant
        // and we should refuse to merge signatures if there are `this` types and they do not match. However, so as to be
        // permissive when calling, for now, we'll intersect the `this` types just like we do for param types in union
        // signatures.
        var thisType = GetIntersectionType([GetTypeOfSymbol(left), InstantiateType(GetTypeOfSymbol(right), mapper)]);
        return CreateSymbolWithType(left, thisType);
    }

    /// <summary>ported from: src/compiler/checker.ts:14400-14445 — <c>combineUnionParameters</c>.</summary>
    internal List<ISymbol> CombineUnionParameters(ISignature left, ISignature right, TypeMapper? mapper)
    {
        var leftCount = GetParameterCount(left);
        var rightCount = GetParameterCount(right);
        var longest = leftCount >= rightCount ? left : right;
        var shorter = ReferenceEquals(longest, left) ? right : left;
        var longestCount = ReferenceEquals(longest, left) ? leftCount : rightCount;
        var eitherHasEffectiveRest = HasEffectiveRestParameter(left) || HasEffectiveRestParameter(right);
        var needsExtraRestElement = eitherHasEffectiveRest && !HasEffectiveRestParameter(longest);
        var parameters = new List<ISymbol>(new ISymbol[longestCount + (needsExtraRestElement ? 1 : 0)]);
        for (var i = 0; i < longestCount; i++)
        {
            var longestParamType = TryGetTypeAtPosition(longest, i)!;
            if (ReferenceEquals(longest, right))
            {
                longestParamType = InstantiateType(longestParamType, mapper);
            }

            var shorterParamType = TryGetTypeAtPosition(shorter, i) ?? _unknownType;
            if (ReferenceEquals(shorter, right))
            {
                shorterParamType = InstantiateType(shorterParamType, mapper);
            }

            var unionParamType = GetIntersectionType([longestParamType, shorterParamType]);
            var isRestParam = eitherHasEffectiveRest && !needsExtraRestElement && i == longestCount - 1;
            var isOptional = i >= GetMinArgumentCount(longest) && i >= GetMinArgumentCount(shorter);
            var leftName = i >= leftCount ? null : GetParameterNameAtPosition(left, i);
            var rightName = i >= rightCount ? null : GetParameterNameAtPosition(right, i);

            var paramName = leftName == rightName ? leftName
                : leftName is null ? rightName
                : rightName is null ? leftName
                : null;
            var paramSymbol = CreateSymbol(
                SymbolFlags.FunctionScopedVariable | (isOptional && !isRestParam ? SymbolFlags.Optional : SymbolFlags.None),
                paramName ?? $"arg{i}",
                isRestParam ? CheckFlags.RestParameter : isOptional ? CheckFlags.OptionalParameter : CheckFlags.None);
            GetSymbolLinks(paramSymbol).Type = isRestParam ? CreateArrayType(unionParamType) : unionParamType;
            parameters[i] = paramSymbol;
        }

        if (needsExtraRestElement)
        {
            var restParamSymbol = CreateSymbol(SymbolFlags.FunctionScopedVariable, "args", CheckFlags.RestParameter);
            GetSymbolLinks(restParamSymbol).Type = CreateArrayType(GetTypeAtPosition(shorter, longestCount));
            if (ReferenceEquals(shorter, right))
            {
                GetSymbolLinks(restParamSymbol).Type = InstantiateType(GetSymbolLinks(restParamSymbol).Type, mapper);
            }

            parameters[longestCount] = restParamSymbol;
        }

        return parameters;
    }

    /// <summary>ported from: src/compiler/checker.ts:14447-14482 — <c>combineSignaturesOfUnionMembers</c>.</summary>
    internal Signature CombineSignaturesOfUnionMembers(ISignature left, ISignature right)
    {
        var typeParams = left.TypeParameters ?? right.TypeParameters;
        TypeMapper? paramMapper = null;
        if (left.TypeParameters is not null && right.TypeParameters is not null)
        {
            paramMapper = CreateTypeMapper(right.TypeParameters, left.TypeParameters);
            // We just use the type parameter defaults from the first signature
        }

        var flags = (left.Flags | right.Flags) & (SignatureFlags.PropagatingFlags & ~SignatureFlags.HasRestParameter);
        var declaration = left.Declaration;
        var parameters = CombineUnionParameters(left, right, paramMapper);
        var lastParam = Core.LastOrUndefined(parameters);
        if (lastParam is not null && (GetCheckFlags(lastParam) & CheckFlags.RestParameter) != 0)
        {
            flags |= SignatureFlags.HasRestParameter;
        }

        var thisParam = CombineUnionThisParam(left.ThisParameter, right.ThisParameter, paramMapper);
        var minArgCount = Math.Max(left.MinArgumentCount, right.MinArgumentCount);
        var result = CreateSignature(
            declaration,
            typeParams,
            thisParam,
            parameters,
            /*resolvedReturnType*/ null,
            /*resolvedTypePredicate*/ null,
            minArgCount,
            flags);
        result.CompositeKind = TypeFlags.Union;
        result.CompositeSignatures = Core.Concatenate(left.CompositeKind != TypeFlags.Intersection && left.CompositeSignatures is not null ? left.CompositeSignatures : [left], [right]) is List<ISignature> list ? list : [.. left.CompositeSignatures ?? [], right];
        if (paramMapper is not null)
        {
            result.Mapper = left.CompositeKind != TypeFlags.Intersection && left.Mapper is not null && left.CompositeSignatures is not null ? CombineTypeMappers(left.Mapper, paramMapper) : paramMapper;
        }
        else if (left.CompositeKind != TypeFlags.Intersection && left.Mapper is not null && left.CompositeSignatures is not null)
        {
            result.Mapper = left.Mapper;
        }

        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:14484-14497 — <c>getUnionIndexInfos</c>.</summary>
    internal List<IIndexInfo> GetUnionIndexInfos(IList<IType> types)
    {
        var sourceInfos = GetIndexInfosOfType(types[0]);
        if (sourceInfos.Count != 0)
        {
            var result = new List<IIndexInfo>();
            foreach (var info in sourceInfos)
            {
                var indexType = info.KeyType!;
                if (Core.Every(types, (IType t, int _) => GetIndexInfoOfType(t, indexType) is not null))
                {
                    result.Add(CreateIndexInfo(indexType, GetUnionType(Core.Map(types, (t, _) => GetIndexTypeOfType(t, indexType)!)!), Core.Some(types, (IType t) => GetIndexInfoOfType(t, indexType)!.IsReadonly)));
                }
            }

            return result;
        }

        return [];
    }

    /// <summary>ported from: src/compiler/checker.ts:14499-14506 — <c>resolveUnionTypeMembers</c>.</summary>
    internal void ResolveUnionTypeMembers(UnionType type)
    {
        // The members and properties collections are empty for union types. To get all properties of a union
        // type use getPropertiesOfType (only the language service uses this).
        var callSignatures = GetUnionSignatures(Core.Map(type.Types, (t, _) => t == _globalFunctionType ? [_unknownSignature] : GetSignaturesOfType(t, SignatureKind.Call))!);
        var constructSignatures = GetUnionSignatures(Core.Map(type.Types, (t, _) => GetSignaturesOfType(t, SignatureKind.Construct))!);
        var indexInfos = GetUnionIndexInfos(type.Types!);
        SetStructuredTypeMembers(type, _emptySymbols, callSignatures, constructSignatures, indexInfos);
    }

    /// <summary>ported from: src/compiler/checker.ts:14508-14512 — <c>intersectTypes</c>.</summary>
    internal IType? IntersectTypes(IType? type1, IType? type2)
        => type1 is null ? type2 : type2 is null ? type1 : GetIntersectionType([type1, type2]);

    /// <summary>ported from: src/compiler/checker.ts:14514-14522 — <c>findMixins</c>.</summary>
    internal List<bool> FindMixins(IList<IType> types)
    {
        var constructorTypeCount = Core.CountWhere(types, (t, _) => GetSignaturesOfType(t, SignatureKind.Construct).Count > 0);
        var mixinFlags = Core.Map(types, (t, _) => IsMixinConstructorType(t))!;
        if (constructorTypeCount > 0 && constructorTypeCount == Core.CountWhere(mixinFlags, (b, _) => b))
        {
            var firstMixinIndex = mixinFlags.IndexOf(true);
            mixinFlags[firstMixinIndex] = false;
        }

        return mixinFlags;
    }

    /// <summary>ported from: src/compiler/checker.ts:14524-14535 — <c>includeMixinType</c>.</summary>
    internal IType IncludeMixinType(IType type, IList<IType> types, IList<bool> mixinFlags, int index)
    {
        var mixedTypes = new List<IType>();
        for (var i = 0; i < types.Count; i++)
        {
            if (i == index)
            {
                mixedTypes.Add(type);
            }
            else if (mixinFlags[i])
            {
                mixedTypes.Add(GetReturnTypeOfSignature(GetSignaturesOfType(types[i], SignatureKind.Construct)[0]));
            }
        }

        return GetIntersectionType(mixedTypes);
    }

    /// <summary>ported from: src/compiler/checker.ts:14537-14568 — <c>resolveIntersectionTypeMembers</c>.</summary>
    internal void ResolveIntersectionTypeMembers(IntersectionType type)
    {
        // The members and properties collections are empty for intersection types. To get all properties of an
        // intersection type use getPropertiesOfType (only the language service uses this).
        List<ISignature>? callSignatures = null;
        List<ISignature>? constructSignatures = null;
        List<IIndexInfo>? indexInfos = null;
        var types = type.Types!;
        var mixinFlags = FindMixins(types);
        var mixinCount = Core.CountWhere(mixinFlags, (b, _) => b);
        for (var i = 0; i < types.Count; i++)
        {
            var t = type.Types![i];
            // When an intersection type contains mixin constructor types, the construct signatures from
            // those types are discarded and their return types are mixed into the return types of all
            // other construct signatures in the intersection type. For example, the intersection type
            // '{ new(...args: any[]) => A } & { new(s: string) => B }' has a single construct signature
            // 'new(s: string) => A & B'.
            if (!mixinFlags[i])
            {
                var signatures = GetSignaturesOfType(t, SignatureKind.Construct);
                if (signatures.Count != 0 && mixinCount > 0)
                {
                    signatures = Core.Map(signatures, (ISignature s, int _) =>
                    {
                        var clone = CloneSignature(s);
                        clone.ResolvedReturnType = IncludeMixinType(GetReturnTypeOfSignature(s), types, mixinFlags, i);
                        return (ISignature)clone;
                    })!;
                }

                constructSignatures = AppendSignatures(constructSignatures, signatures);
            }

            callSignatures = AppendSignatures(callSignatures, GetSignaturesOfType(t, SignatureKind.Call));
            indexInfos = Core.ReduceLeft(GetIndexInfosOfType(t), (infos, newInfo, _) => AppendIndexInfo(infos, newInfo, /*union*/ false), indexInfos);
        }

        SetStructuredTypeMembers(type, _emptySymbols, callSignatures ?? [], constructSignatures ?? [], indexInfos ?? []);
    }

    /// <summary>ported from: src/compiler/checker.ts:14570-14577 — <c>appendSignatures</c>.</summary>
    internal List<ISignature>? AppendSignatures(List<ISignature>? signatures, IList<ISignature> newSignatures)
    {
        foreach (var sig in newSignatures)
        {
            if (signatures is null || Core.Every(signatures, (ISignature s, int _) => !CompareSignaturesIdentical(s, sig, /*partialMatch*/ false, /*ignoreThisTypes*/ false, /*ignoreReturnTypes*/ false, CompareTypesIdentical)))
            {
                signatures = (List<ISignature>?)Core.Append(signatures, sig);
            }
        }

        return signatures;
    }

    /// <summary>ported from: src/compiler/checker.ts:14579-14593 — <c>appendIndexInfo</c>.</summary>
    internal List<IIndexInfo>? AppendIndexInfo(List<IIndexInfo>? indexInfos, IIndexInfo newInfo, bool union)
    {
        if (indexInfos is not null)
        {
            for (var i = 0; i < indexInfos.Count; i++)
            {
                var info = indexInfos[i];
                if (ReferenceEquals(info.KeyType, newInfo.KeyType))
                {
                    indexInfos[i] = CreateIndexInfo(info.KeyType!, union ? GetUnionType([info.Type!, newInfo.Type!]) : GetIntersectionType([info.Type!, newInfo.Type!]), union ? info.IsReadonly || newInfo.IsReadonly : info.IsReadonly && newInfo.IsReadonly);
                    return indexInfos;
                }
            }
        }

        return (List<IIndexInfo>?)Core.Append(indexInfos, newInfo);
    }

    // ---------------------------------------------------------------------------------------------
    // Resolving a structured type (checker.ts:15022-15057, 15799-15803)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:15022-15055 — <c>resolveStructuredTypeMembers</c>.</summary>
    internal UnionOrIntersectionType ResolveStructuredTypeMembers(UnionOrIntersectionType type)
    {
        if (type.Members is null)
        {
            if ((type.Flags & TypeFlags.Object) != 0)
            {
                if ((type.ObjectFlags & ObjectFlags.Reference) != 0)
                {
                    ResolveTypeReferenceMembers((TypeReference)type);
                }
                else if ((type.ObjectFlags & ObjectFlags.ClassOrInterface) != 0)
                {
                    ResolveClassOrInterfaceMembers((InterfaceType)type);
                }
                else if ((type.ObjectFlags & ObjectFlags.ReverseMapped) != 0)
                {
                    ResolveReverseMappedTypeMembers((ReverseMappedType)type);
                }
                else if ((type.ObjectFlags & ObjectFlags.Anonymous) != 0)
                {
                    ResolveAnonymousTypeMembers((AnonymousType)type);
                }
                else if ((type.ObjectFlags & ObjectFlags.Mapped) != 0)
                {
                    ResolveMappedTypeMembers((MappedType)type);
                }
                else
                {
                    Debug.Fail("Unhandled object type " + Debug.FormatObjectFlags(type.ObjectFlags));
                }
            }
            else if ((type.Flags & TypeFlags.Union) != 0)
            {
                ResolveUnionTypeMembers((UnionType)type);
            }
            else if ((type.Flags & TypeFlags.Intersection) != 0)
            {
                ResolveIntersectionTypeMembers((IntersectionType)type);
            }
            else
            {
                Debug.Fail("Unhandled type " + Debug.FormatTypeFlags(type.Flags));
            }
        }

        return type;
    }

    /// <summary>ported from: src/compiler/utility.ts:644-652 — <c>createSymbolTable(symbols)</c> seeded from a list.</summary>
    /// <remarks>
    /// PORT-NOTE: TS calls <c>createSymbolTable(source.declaredProperties)</c> (checker.ts:14076) to seed a table from
    /// an array of symbols keyed by <c>escapedName</c>; the port's <see cref="SymbolTable"/> has no such constructor
    /// (M6-0 kept the BCL shape plus a copy constructor), so this states the same seeding here.
    /// </remarks>
    private static SymbolTable CreateSymbolTableFromSymbols(IList<ISymbol> symbols)
    {
        var result = Utilities.CreateSymbolTable();
        foreach (var symbol in symbols)
        {
            result[symbol.EscapedName ?? ""] = symbol;
        }

        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:15799-15803 — <c>getPropertyOfUnionOrIntersectionType</c>.</summary>
    internal ISymbol? GetPropertyOfUnionOrIntersectionType(UnionOrIntersectionType type, string name, bool? skipObjectFunctionPropertyAugment = null)
    {
        var property = GetUnionOrIntersectionProperty(type, name, skipObjectFunctionPropertyAugment);
        // We need to filter out partial properties in union types
        return property is not null && (GetCheckFlags(property) & CheckFlags.ReadPartial) == 0 ? property : null;
    }
}
