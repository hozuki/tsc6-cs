namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:2330-2333 (the contextual-type stacks)
// ported from: src/compiler/checker.ts:2336-2338 (the inference-context stacks)
// ported from: src/compiler/checker.ts:26774-27770 (inference and the two name-resolution diagnostic helpers)
// ported from: src/compiler/checker.ts:28510-28624 (the generic type combinators)
// ported from: src/compiler/checker.ts:31960-33261 (contextual typing)
// ported from: src/compiler/checker.ts:53812-53819 (sortSymbolsIfTSGoCompat)
/// <summary>
/// Inference (<c>inferTypes</c>), contextual typing (<c>getContextualType</c> and the stacks it walks) and the generic
/// type combinators those two use.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: the six stacks the reference declares in its state block (checker.ts:2330-2338) are declared here, for
/// the reason <c>Checker.Conditional.cs</c> states for the four literal-type maps: the state block is outside every M6
/// slice's range and the slice that reads a slot declares it. They are read only by this file's members and by
/// <c>Checker.CallResolution.cs</c> (through <c>GetContextualType</c>/<c>GetInferenceContext</c>).
/// <c>contextualBindingPatterns</c> (:2334) and the active-mapper stack (:2340-2342) are <em>not</em> re-declared: they
/// are already landed (<c>Checker.Types.cs:281</c>, <c>Checker.Instantiation.cs:50-57</c>, with the four active-mapper
/// members at <c>Checker.Instantiation.cs:1083-1129</c>).
/// </para>
/// <para>
/// PORT-NOTE: <c>inferTypes</c>' two dozen nested functions are C# local functions (the precedent is
/// <c>getConditionalType</c>'s <c>canTailRecurse</c>, <c>Checker.Conditional.cs</c>): they mutate the enclosing
/// <c>priority</c>/<c>contravariant</c>/<c>bivariant</c>/<c>propagationType</c>/<c>inferencePriority</c>/<c>visited</c>/
/// <c>sourceStack</c>/<c>targetStack</c>/<c>expandingFlags</c> state, and <c>inferFromTypes</c> is called at :26781
/// before its own declaration at :26784 — C# local functions are in scope for their whole block, so the reference's
/// control flow (including the call before the declaration) is preserved verbatim.
/// </para>
/// <para>
/// PORT-NOTE: everything this file calls but does not declare belongs to another slice and is referenced only:
/// <c>FilterType</c>'s callees <c>GetUnionTypeFromSortedList</c>/<c>CreateOriginUnionOrIntersectionType</c> and
/// <c>MaybeTypeOfKind</c>/<c>IsPatternLiteralType</c> (Checker.Unions.cs and M6-2), the relations family
/// <c>GetVariances</c>/<c>GetAliasVariances</c>/<c>IsDeeplyNestedType</c>/<c>GetCommonSupertype</c>/
/// <c>GetCommonSubtype</c>/<c>IsArrayType</c>/<c>IsArrayOrTupleType</c>/<c>IsTupleType</c>/<c>IsTupleLikeType</c>/
/// <c>RemoveMissingType</c>/<c>IsObjectTypeWithInferableIndex</c>/<c>IsTupleTypeStructureMatching</c>/
/// <c>GetElementTypeOfSliceOfTupleType</c>/<c>ApplyToParameterTypes</c>/<c>ApplyToReturnTypes</c>/
/// <c>ClearCachedInferences</c>/<c>IsTypeParameterAtTopLevel(InReturnType)</c>/
/// <c>CreateEmptyObjectTypeFromStringLiteral</c>/<c>InferTypeForHomomorphicMappedType</c>/
/// <c>TypesDefinitelyUnrelated</c>/<c>GetTypeFromInference</c>/<c>IsFromInferenceBlockedSource</c>/
/// <c>IsValidNumberString</c>/<c>IsValidBigIntString</c>/<c>ParseBigIntLiteralType</c>/
/// <c>InferTypesFromTemplateLiteralType</c>/<c>IsTypeMatchedByTemplateLiteralType</c> (Checker.Relations.*, M6-3),
/// <c>Utilities.PseudoBigIntToString</c>/<c>Utilities.UsesWildcardTypes</c> (utilities.ts, M6Utilities) and the JSX
/// contextual helpers of the 33262-34328 block (M6Check).
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // The contextual-type and inference-context stacks (checker.ts:2330-2338)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:2330 — <c>contextualTypeNodes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS grows the array with <c>push</c> and truncates it by assigning <c>length</c>; the port grows the
    /// <c>List&lt;T&gt;</c> with <c>Add</c> and truncates it with <c>RemoveAt(Count - 1)</c> at the pop sites
    /// (<c>PopContextualType</c>/<c>PopInferenceContext</c>).
    /// </remarks>
    internal readonly List<Node> _contextualTypeNodes = [];

    /// <summary>ported from: src/compiler/checker.ts:2331 — <c>contextualTypes</c>.</summary>
    internal readonly List<IType?> _contextualTypes = [];

    /// <summary>ported from: src/compiler/checker.ts:2332 — <c>contextualIsCache</c>.</summary>
    internal readonly List<bool> _contextualIsCache = [];

    /// <summary>ported from: src/compiler/checker.ts:2333 — <c>contextualTypeCount</c>.</summary>
    internal int _contextualTypeCount;

    /// <summary>ported from: src/compiler/checker.ts:2336 — <c>inferenceContextNodes</c>.</summary>
    internal readonly List<Node> _inferenceContextNodes = [];

    /// <summary>ported from: src/compiler/checker.ts:2337 — <c>inferenceContexts</c>.</summary>
    internal readonly List<IInferenceContext?> _inferenceContexts = [];

    /// <summary>ported from: src/compiler/checker.ts:2338 — <c>inferenceContextCount</c>.</summary>
    internal int _inferenceContextCount;

    // ---------------------------------------------------------------------------------------------
    // The generic type combinators (checker.ts:28510-28624)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:28510-28512 — <c>forEachType</c>.</summary>
    internal TU? ForEachType<TU>(IType type, Func<IType, TU?> f)
        where TU : class
        => (type.Flags & TypeFlags.Union) != 0 ? Core.ForEach(((IUnionType)type).Types, (t, _) => f(t)) : f(type);

    /// <summary>ported from: src/compiler/checker.ts:28510-28512 — <c>forEachType</c>'s value-less use.</summary>
    /// <remarks>
    /// PORT-NOTE: TS has one generic <c>forEachType&lt;T&gt;(type, f): T | undefined</c> and calls it with void
    /// callbacks as well; C# cannot infer a type argument from a void lambda body, so the port states that use as this
    /// overload over the same traversal (the generic member above stays the only implementation).
    /// </remarks>
    internal void ForEachType(IType type, Action<IType> f)
        => ForEachType<object>(type, t => { f(t); return null; });

    /// <summary>ported from: src/compiler/checker.ts:28514-28516 — <c>someType</c>.</summary>
    internal bool SomeType(IType type, Func<IType, bool> f)
        => (type.Flags & TypeFlags.Union) != 0 ? Core.Some(((IUnionType)type).Types, t => f(t)) : f(type);

    /// <summary>ported from: src/compiler/checker.ts:28518-28520 — <c>everyType</c>.</summary>
    internal bool EveryType(IType type, Func<IType, bool> f)
        => (type.Flags & TypeFlags.Union) != 0 ? Core.Every(((IUnionType)type).Types, (t, _) => f(t)) : f(type);

    /// <summary>ported from: src/compiler/checker.ts:28522-28524 — <c>everyContainedType</c>.</summary>
    internal bool EveryContainedType(IType type, Func<IType, bool> f)
        => (type.Flags & TypeFlags.UnionOrIntersection) != 0 ? Core.Every(((IUnionOrIntersectionType)type).Types, (t, _) => f(t)) : f(type);

    /// <summary>ported from: src/compiler/checker.ts:28526-28555 — <c>filterType</c>.</summary>
    internal IType FilterType(IType type, Func<IType, bool> f)
    {
        if ((type.Flags & TypeFlags.Union) != 0)
        {
            var types = ((IUnionType)type).Types!;
            var filtered = (List<IType>)Core.Filter(types, f)!;
            // TS's `filter` answers the input array itself when nothing was filtered; Core.Filter keeps that contract.
            if (ReferenceEquals(filtered, types))
            {
                return type;
            }
            var origin = ((IUnionType)type).Origin;
            IType? newOrigin = null;
            if (origin is not null && (origin.Flags & TypeFlags.Union) != 0)
            {
                // If the origin type is a (denormalized) union type, filter its non-union constituents. If that ends
                // up removing a smaller number of types than in the normalized constituent set (meaning some of the
                // filtered types are within nested unions in the origin), then we can't construct a new origin type.
                // Otherwise, if we have exactly one type left in the origin set, return that as the filtered type.
                // Otherwise, construct a new filtered origin type.
                var originTypes = ((IUnionType)origin).Types!;
                var originFiltered = (List<IType>)Core.Filter(originTypes, t => (t.Flags & TypeFlags.Union) != 0 || f(t))!;
                if (originTypes.Count - originFiltered.Count == types.Count - filtered.Count)
                {
                    if (originFiltered.Count == 1)
                    {
                        return originFiltered[0];
                    }
                    newOrigin = CreateOriginUnionOrIntersectionType(TypeFlags.Union, originFiltered);
                }
            }
            // filtering could remove intersections so `ContainsIntersections` might be forwarded "incorrectly"
            // it is purely an optimization hint so there is no harm in accidentally forwarding it
            return GetUnionTypeFromSortedList(
                filtered,
                ((IUnionType)type).ObjectFlags & (ObjectFlags.PrimitiveUnion | ObjectFlags.ContainsIntersections),
                /*aliasSymbol*/ null,
                /*aliasTypeArguments*/ null,
                newOrigin);
        }
        return (type.Flags & TypeFlags.Never) != 0 || f(type) ? type : _neverType;
    }

    /// <summary>ported from: src/compiler/checker.ts:28557-28559 — <c>removeType</c>.</summary>
    internal IType RemoveType(IType type, IType targetType)
        => FilterType(type, t => !ReferenceEquals(t, targetType));

    /// <summary>ported from: src/compiler/checker.ts:28561-28563 — <c>countTypes</c>.</summary>
    internal int CountTypes(IType type)
        => (type.Flags & TypeFlags.Union) != 0 ? ((IUnionType)type).Types!.Count : 1;

    /// <summary>ported from: src/compiler/checker.ts:28568-28594 — <c>mapType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's two overloads (a <c>Type</c>-returning and a <c>Type | undefined</c>-returning one)
    /// collapse into this one method whose mapper may answer null; the first overload is the second with a mapper that
    /// never answers <c>undefined</c>, and TS's <c>undefined</c> is the port's <c>null</c>.
    /// </remarks>
    internal IType? MapType(IType type, Func<IType, IType?> mapper, bool noReductions = false)
    {
        if ((type.Flags & TypeFlags.Never) != 0)
        {
            return type;
        }
        if ((type.Flags & TypeFlags.Union) == 0)
        {
            return mapper(type);
        }
        var origin = ((IUnionType)type).Origin;
        var types = origin is not null && (origin.Flags & TypeFlags.Union) != 0 ? ((IUnionType)origin).Types! : ((IUnionType)type).Types!;
        List<IType>? mappedTypes = null;
        var changed = false;
        foreach (var t in types)
        {
            var mapped = (t.Flags & TypeFlags.Union) != 0 ? MapType(t, mapper, noReductions) : mapper(t);
            changed |= !ReferenceEquals(t, mapped);
            if (mapped is not null)
            {
                mappedTypes ??= [];
                mappedTypes.Add(mapped);
            }
        }
        return changed
            ? mappedTypes is not null ? GetUnionType(mappedTypes, noReductions ? UnionReduction.None : UnionReduction.Literal) : null
            : type;
    }

    /// <summary>ported from: src/compiler/checker.ts:28596-28600 — <c>mapTypeWithAlias</c>.</summary>
    internal IType? MapTypeWithAlias(IType type, Func<IType, IType> mapper, ISymbol? aliasSymbol, IList<IType>? aliasTypeArguments)
        => (type.Flags & TypeFlags.Union) != 0 && aliasSymbol is not null
            ? GetUnionType(Core.Map(((IUnionType)type).Types, (t, _) => mapper(t))!, UnionReduction.Literal, aliasSymbol, aliasTypeArguments as List<IType>)
            : MapType(type, mapper);

    /// <summary>ported from: src/compiler/checker.ts:28602-28604 — <c>extractTypesOfKind</c>.</summary>
    internal IType ExtractTypesOfKind(IType type, TypeFlags kind)
        => FilterType(type, t => (t.Flags & kind) != 0);

    /// <summary>ported from: src/compiler/checker.ts:28611-28623 — <c>replacePrimitivesWithLiterals</c>.</summary>
    internal IType ReplacePrimitivesWithLiterals(IType typeWithPrimitives, IType typeWithLiterals)
    {
        if (
            MaybeTypeOfKind(typeWithPrimitives, TypeFlags.String | TypeFlags.TemplateLiteral | TypeFlags.Number | TypeFlags.BigInt) &&
            MaybeTypeOfKind(typeWithLiterals, TypeFlags.StringLiteral | TypeFlags.TemplateLiteral | TypeFlags.StringMapping | TypeFlags.NumberLiteral | TypeFlags.BigIntLiteral)
        )
        {
            return MapType(typeWithPrimitives, t =>
                (t.Flags & TypeFlags.String) != 0 ? ExtractTypesOfKind(typeWithLiterals, TypeFlags.String | TypeFlags.StringLiteral | TypeFlags.TemplateLiteral | TypeFlags.StringMapping) :
                    IsPatternLiteralType(t) && !MaybeTypeOfKind(typeWithLiterals, TypeFlags.String | TypeFlags.TemplateLiteral | TypeFlags.StringMapping) ? ExtractTypesOfKind(typeWithLiterals, TypeFlags.StringLiteral) :
                    (t.Flags & TypeFlags.Number) != 0 ? ExtractTypesOfKind(typeWithLiterals, TypeFlags.Number | TypeFlags.NumberLiteral) :
                    (t.Flags & TypeFlags.BigInt) != 0 ? ExtractTypesOfKind(typeWithLiterals, TypeFlags.BigInt | TypeFlags.BigIntLiteral) : t)!;
        }
        return typeWithPrimitives;
    }

    // ---------------------------------------------------------------------------------------------
    // Inference (checker.ts:26774-27690)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:26774-27547 — <c>inferTypes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>inferencePriority</c> is a <c>number</c> seeded with <c>InferencePriority.MaxValue</c> and
    /// folded with <c>Math.min</c> against values that include <c>InferencePriority.Circularity</c> (-1), so the port
    /// keeps it an <c>int</c> and casts the <see cref="InferencePriority"/> parameter at each use instead of routing the
    /// arithmetic through the enum.
    /// </remarks>
    internal void InferTypes(List<IInferenceInfo> inferences, IType originalSource, IType originalTarget, InferencePriority priority = InferencePriority.None, bool contravariant = false)
    {
        var bivariant = false;
        IType? propagationType = null;
        var inferencePriority = (int)InferencePriority.MaxValue;
        Dictionary<string, int>? visited = null;
        List<IType>? sourceStack = null;
        List<IType>? targetStack = null;
        var expandingFlags = ExpandingFlags.None;
        InferFromTypes(originalSource, originalTarget);

        void InferFromTypes(IType source, IType target)
        {
            if (!CouldContainTypeVariables(target) || IsNoInferType(target))
            {
                return;
            }
            if (ReferenceEquals(source, _wildcardType) || ReferenceEquals(source, _blockedStringType))
            {
                // We are inferring from an 'any' type. We want to infer this type for every type parameter
                // referenced in the target type, so we record it as the propagation type and infer from the
                // target to itself. Then, as we find candidates we substitute the propagation type.
                var savePropagationType = propagationType;
                propagationType = source;
                InferFromTypes(target, target);
                propagationType = savePropagationType;
                return;
            }
            if (source.AliasSymbol is not null && ReferenceEquals(source.AliasSymbol, target.AliasSymbol))
            {
                if (source.AliasTypeArguments is not null)
                {
                    // Source and target are types originating in the same generic type alias declaration.
                    // Simply infer from source type arguments to target type arguments, with defaults applied.
                    var parameters = GetSymbolLinks(source.AliasSymbol).TypeParameters!;
                    var minParams = GetMinTypeArgumentCount(parameters);
                    var sourceTypes = FillMissingTypeArguments(source.AliasTypeArguments, parameters, minParams, Utilities.IsInJSFile((Node?)source.AliasSymbol.ValueDeclaration));
                    var targetTypes = FillMissingTypeArguments(target.AliasTypeArguments!, parameters, minParams, Utilities.IsInJSFile((Node?)source.AliasSymbol.ValueDeclaration));
                    InferFromTypeArguments(sourceTypes!, targetTypes!, GetAliasVariances(source.AliasSymbol));
                }
                // And if there weren't any type arguments, there's no reason to run inference as the types must be the same.
                return;
            }
            if (ReferenceEquals(source, target) && (source.Flags & TypeFlags.UnionOrIntersection) != 0)
            {
                // When source and target are the same union or intersection type, just relate each constituent
                // type to itself.
                foreach (var t in ((IUnionOrIntersectionType)source).Types!)
                {
                    InferFromTypes(t, t);
                }
                return;
            }
            if ((target.Flags & TypeFlags.Union) != 0)
            {
                // First, infer between identically matching source and target constituents and remove the
                // matching types.
                var (tempSources, tempTargets) = InferFromMatchingTypes(
                    (source.Flags & TypeFlags.Union) != 0 ? ((IUnionType)source).Types! : new List<IType> { source },
                    ((IUnionType)target).Types!,
                    IsTypeOrBaseIdenticalTo);
                // Next, infer between closely matching source and target constituents and remove
                // the matching types. Types closely match when they are instantiations of the same
                // object type or instantiations of the same type alias.
                var (sources, targets) = InferFromMatchingTypes(tempSources, tempTargets, IsTypeCloselyMatchedBy);
                if (targets.Count == 0)
                {
                    return;
                }
                target = GetUnionType(targets);
                if (sources.Count == 0)
                {
                    // All source constituents have been matched and there is nothing further to infer from.
                    // However, simply making no inferences is undesirable because it could ultimately mean
                    // inferring a type parameter constraint. Instead, make a lower priority inference from
                    // the full source to whatever remains in the target. For example, when inferring from
                    // string to 'string | T', make a lower priority inference of string for T.
                    InferWithPriority(source, target, InferencePriority.NakedTypeVariable);
                    return;
                }
                source = GetUnionType(sources);
            }
            else if ((target.Flags & TypeFlags.Intersection) != 0 && !Core.Every(((IIntersectionType)target).Types, (t, _) => IsNonGenericObjectType(t)))
            {
                // We reduce intersection types unless they're simple combinations of object types. For example,
                // when inferring from 'string[] & { extra: any }' to 'string[] & T' we want to remove string[] and
                // infer { extra: any } for T. But when inferring to 'string[] & Iterable<T>' we want to keep the
                // string[] on the source side and infer string for T.
                if ((source.Flags & TypeFlags.Union) == 0)
                {
                    // Infer between identically matching source and target constituents and remove the matching types.
                    var (matchedSources, matchedTargets) = InferFromMatchingTypes(
                        (source.Flags & TypeFlags.Intersection) != 0 ? ((IIntersectionType)source).Types! : new List<IType> { source },
                        ((IIntersectionType)target).Types!,
                        IsTypeIdenticalTo);
                    if (matchedSources.Count == 0 || matchedTargets.Count == 0)
                    {
                        return;
                    }
                    source = GetIntersectionType(matchedSources);
                    target = GetIntersectionType(matchedTargets);
                }
            }
            if ((target.Flags & (TypeFlags.IndexedAccess | TypeFlags.Substitution)) != 0)
            {
                if (IsNoInferType(target))
                {
                    return;
                }
                target = GetActualTypeVariable(target);
            }
            if ((target.Flags & TypeFlags.TypeVariable) != 0)
            {
                // Skip inference if the source is "blocked", which is used by the language service to
                // prevent inference on nodes currently being edited.
                if (IsFromInferenceBlockedSource(source))
                {
                    return;
                }
                var inference = GetInferenceInfoForType(target);
                if (inference is not null)
                {
                    // If target is a type parameter, make an inference, unless the source type contains
                    // a "non-inferrable" type. Types with this flag set are markers used to prevent inference.
                    //
                    // For example:
                    //     - anyFunctionType is a wildcard type that's used to avoid contextually typing functions;
                    //       it's internal, so should not be exposed to the user by adding it as a candidate.
                    //     - autoType (and autoArrayType) is a special "any" used in control flow; like anyFunctionType,
                    //       it's internal and should not be observable.
                    //     - silentNeverType is returned by getInferredType when instantiating a generic function for
                    //       inference (and a type variable has no mapping).
                    //
                    // This flag is infectious; if we produce Box<never> (where never is silentNeverType), Box<never> is
                    // also non-inferrable.
                    //
                    // As a special case, also ignore nonInferrableAnyType, which is a special form of the any type
                    // used as a stand-in for binding elements when they are being inferred.
                    if ((GetObjectFlags(source) & ObjectFlags.NonInferrableType) != 0 || ReferenceEquals(source, _nonInferrableAnyType))
                    {
                        return;
                    }
                    if (!inference.IsFixed)
                    {
                        var candidate = propagationType ?? source;
                        if (ReferenceEquals(candidate, _blockedStringType))
                        {
                            return;
                        }
                        if (inference.Priority is null || (int)priority < (int)inference.Priority.Value)
                        {
                            inference.Candidates = null;
                            inference.ContraCandidates = null;
                            inference.TopLevel = true;
                            inference.Priority = priority;
                        }
                        if (priority == inference.Priority!.Value)
                        {
                            // We make contravariant inferences only if we are in a pure contravariant position,
                            // i.e. only if we have not descended into a bivariant position.
                            if (contravariant && !bivariant)
                            {
                                if (!Core.Contains(inference.ContraCandidates, candidate))
                                {
                                    inference.ContraCandidates = (List<IType>)Core.Append(inference.ContraCandidates, candidate)!;
                                    ClearCachedInferences(inferences);
                                }
                            }
                            else if (!Core.Contains(inference.Candidates, candidate))
                            {
                                inference.Candidates = (List<IType>)Core.Append(inference.Candidates, candidate)!;
                                ClearCachedInferences(inferences);
                            }
                        }
                        if (((int)priority & (int)InferencePriority.ReturnType) == 0 && (target.Flags & TypeFlags.TypeParameter) != 0 && inference.TopLevel && !IsTypeParameterAtTopLevel(originalTarget, (ITypeParameter)target))
                        {
                            inference.TopLevel = false;
                            ClearCachedInferences(inferences);
                        }
                    }
                    inferencePriority = Math.Min(inferencePriority, (int)priority);
                    return;
                }
                // Infer to the simplified version of an indexed access, if possible, to (hopefully) expose more bare type parameters to the inference engine
                var simplified = GetSimplifiedType(target, /*writing*/ false);
                if (!ReferenceEquals(simplified, target))
                {
                    InferFromTypes(source, simplified);
                }
                else if ((target.Flags & TypeFlags.IndexedAccess) != 0)
                {
                    var indexType = GetSimplifiedType(((IIndexedAccessType)target).IndexType!, /*writing*/ false);
                    // Generally simplifications of instantiable indexes are avoided to keep relationship checking correct, however if our target is an access, we can consider
                    // that key of that access to be "instantiated", since we're looking to find the infernce goal in any way we can.
                    if ((indexType.Flags & TypeFlags.Instantiable) != 0)
                    {
                        var distributed = DistributeIndexOverObjectType(GetSimplifiedType(((IIndexedAccessType)target).ObjectType!, /*writing*/ false), indexType, /*writing*/ false);
                        if (distributed is not null && !ReferenceEquals(distributed, target))
                        {
                            InferFromTypes(source, distributed);
                        }
                    }
                }
            }
            if (
                (GetObjectFlags(source) & ObjectFlags.Reference) != 0 && (GetObjectFlags(target) & ObjectFlags.Reference) != 0 && (
                    ReferenceEquals(((ITypeReference)source).Target, ((ITypeReference)target).Target) || IsArrayType(source) && IsArrayType(target)
                ) &&
                !(((ITypeReference)source).Node is not null && ((ITypeReference)target).Node is not null)
            )
            {
                // If source and target are references to the same generic type, infer from type arguments
                InferFromTypeArguments(GetTypeArguments((ITypeReference)source), GetTypeArguments((ITypeReference)target), GetVariances((IGenericType)((ITypeReference)source).Target!));
            }
            else if ((source.Flags & TypeFlags.Index) != 0 && (target.Flags & TypeFlags.Index) != 0)
            {
                InferFromContravariantTypes(((IIndexType)source).Type!, ((IIndexType)target).Type!);
            }
            else if ((IsLiteralType(source) || (source.Flags & TypeFlags.String) != 0) && (target.Flags & TypeFlags.Index) != 0)
            {
                var empty = CreateEmptyObjectTypeFromStringLiteral(source);
                InferFromContravariantTypesWithPriority(empty, ((IIndexType)target).Type!, InferencePriority.LiteralKeyof);
            }
            else if ((source.Flags & TypeFlags.IndexedAccess) != 0 && (target.Flags & TypeFlags.IndexedAccess) != 0)
            {
                InferFromTypes(((IIndexedAccessType)source).ObjectType!, ((IIndexedAccessType)target).ObjectType!);
                InferFromTypes(((IIndexedAccessType)source).IndexType!, ((IIndexedAccessType)target).IndexType!);
            }
            else if ((source.Flags & TypeFlags.StringMapping) != 0 && (target.Flags & TypeFlags.StringMapping) != 0)
            {
                if (ReferenceEquals(((IStringMappingType)source).Symbol, ((IStringMappingType)target).Symbol))
                {
                    InferFromTypes(((IStringMappingType)source).Type!, ((IStringMappingType)target).Type!);
                }
            }
            else if ((source.Flags & TypeFlags.Substitution) != 0)
            {
                InferFromTypes(((ISubstitutionType)source).BaseType!, target);
                // Make substitute inference at a lower priority
                InferWithPriority(GetSubstitutionIntersection((ISubstitutionType)source), target, InferencePriority.SubstituteSource);
            }
            else if ((target.Flags & TypeFlags.Conditional) != 0)
            {
                InvokeOnce(source, (IConditionalType)target, InferToConditionalType);
            }
            else if ((target.Flags & TypeFlags.UnionOrIntersection) != 0)
            {
                InferToMultipleTypes(source, ((IUnionOrIntersectionType)target).Types!, target.Flags);
            }
            else if ((source.Flags & TypeFlags.Union) != 0)
            {
                // Source is a union or intersection type, infer from each constituent type
                var sourceTypes = ((IUnionOrIntersectionType)source).Types!;
                foreach (var sourceType in sourceTypes)
                {
                    InferFromTypes(sourceType, target);
                }
            }
            else if ((target.Flags & TypeFlags.TemplateLiteral) != 0)
            {
                InferToTemplateLiteralType(source, (ITemplateLiteralType)target);
            }
            else
            {
                source = GetReducedType(source);
                if (IsGenericMappedType(source) && IsGenericMappedType(target))
                {
                    InvokeOnce((IMappedType)source, (IMappedType)target, InferFromGenericMappedTypes);
                }
                // The reference's parentheses make this a `(priority & NoConstraints) && (source.flags & …)` test; the port
                // states the flag test on the int slot, which is why the two casts are here and nowhere else.
                if (!(((int)priority & (int)InferencePriority.NoConstraints) != 0 && (source.Flags & (TypeFlags.Intersection | TypeFlags.Instantiable)) != 0))
                {
                    var apparentSource = GetApparentType(source);
                    // getApparentType can return _any_ type, since an indexed access or conditional may simplify to any other type.
                    // If that occurs and it doesn't simplify to an object or intersection, we'll need to restart `inferFromTypes`
                    // with the simplified source.
                    if (!ReferenceEquals(apparentSource, source) && (apparentSource.Flags & (TypeFlags.Object | TypeFlags.Intersection)) == 0)
                    {
                        InferFromTypes(apparentSource, target);
                        return;
                    }
                    source = apparentSource;
                }
                if ((source.Flags & (TypeFlags.Object | TypeFlags.Intersection)) != 0)
                {
                    InvokeOnce(source, target, InferFromObjectTypes);
                }
            }
        }

        void InferWithPriority(IType source, IType target, InferencePriority newPriority)
        {
            var savePriority = priority;
            priority |= newPriority;
            InferFromTypes(source, target);
            priority = savePriority;
        }

        void InferFromContravariantTypesWithPriority(IType source, IType target, InferencePriority newPriority)
        {
            var savePriority = priority;
            priority |= newPriority;
            InferFromContravariantTypes(source, target);
            priority = savePriority;
        }

        void InferToMultipleTypesWithPriority(IType source, List<IType> targets, TypeFlags targetFlags, InferencePriority newPriority)
        {
            var savePriority = priority;
            priority |= newPriority;
            InferToMultipleTypes(source, targets, targetFlags);
            priority = savePriority;
        }

        // Ensure an inference action is performed only once for the given source and target types.
        // This includes two things:
        // Avoiding inferring between the same pair of source and target types,
        // and avoiding circularly inferring between source and target types.
        // For an example of the last, consider if we are inferring between source type
        // `type Deep<T> = { next: Deep<Deep<T>> }` and target type `type Loop<U> = { next: Loop<U> }`.
        // We would then infer between the types of the `next` property: `Deep<Deep<T>>` = `{ next: Deep<Deep<Deep<T>>> }` and `Loop<U>` = `{ next: Loop<U> }`.
        // We will then infer again between the types of the `next` property:
        // `Deep<Deep<Deep<T>>>` and `Loop<U>`, and so on, such that we would be forever inferring
        // between instantiations of the same types `Deep` and `Loop`.
        // In particular, we would be inferring from increasingly deep instantiations of `Deep` to `Loop`,
        // such that we would go on inferring forever, even though we would never infer
        // between the same pair of types.
        void InvokeOnce<Source, Target>(Source source, Target target, Action<Source, Target> action)
            where Source : IType
            where Target : IType
        {
            var key = source.Id + "," + target.Id;
            int? status = visited is not null && visited.TryGetValue(key, out var value) ? value : null;
            if (status is not null)
            {
                inferencePriority = Math.Min(inferencePriority, status.Value);
                return;
            }
            (visited ??= [])[key] = (int)InferencePriority.Circularity;
            var saveInferencePriority = inferencePriority;
            inferencePriority = (int)InferencePriority.MaxValue;
            // We stop inferring and report a circularity if we encounter duplicate recursion identities on both
            // the source side and the target side.
            var saveExpandingFlags = expandingFlags;
            (sourceStack ??= []).Add(source);
            (targetStack ??= []).Add(target);
            if (IsDeeplyNestedType(source, sourceStack, sourceStack.Count, 2)) expandingFlags |= ExpandingFlags.Source;
            if (IsDeeplyNestedType(target, targetStack, targetStack.Count, 2)) expandingFlags |= ExpandingFlags.Target;
            if (expandingFlags != ExpandingFlags.Both)
            {
                action(source, target);
            }
            else
            {
                inferencePriority = (int)InferencePriority.Circularity;
            }
            targetStack.RemoveAt(targetStack.Count - 1);
            sourceStack.RemoveAt(sourceStack.Count - 1);
            expandingFlags = saveExpandingFlags;
            visited[key] = inferencePriority;
            inferencePriority = Math.Min(inferencePriority, saveInferencePriority);
        }

        (List<IType> sources, List<IType> targets) InferFromMatchingTypes(List<IType> sources, List<IType> targets, Func<IType, IType, bool> matches)
        {
            List<IType>? matchedSources = null;
            List<IType>? matchedTargets = null;
            foreach (var t in targets)
            {
                foreach (var s in sources)
                {
                    if (matches(s, t))
                    {
                        InferFromTypes(s, t);
                        matchedSources = (List<IType>?)Core.AppendIfUnique(matchedSources, s);
                        matchedTargets = (List<IType>?)Core.AppendIfUnique(matchedTargets, t);
                    }
                }
            }
            return (
                matchedSources is not null ? (List<IType>)Core.Filter(sources, t => !Core.Contains(matchedSources, t))! : sources,
                matchedTargets is not null ? (List<IType>)Core.Filter(targets, t => !Core.Contains(matchedTargets, t))! : targets);
        }

        void InferFromTypeArguments(IList<IType> sourceTypes, IList<IType> targetTypes, IList<VarianceFlags> variances)
        {
            var count = sourceTypes.Count < targetTypes.Count ? sourceTypes.Count : targetTypes.Count;
            for (var i = 0; i < count; i++)
            {
                if (i < variances.Count && (variances[i] & VarianceFlags.VarianceMask) == VarianceFlags.Contravariant)
                {
                    InferFromContravariantTypes(sourceTypes[i], targetTypes[i]);
                }
                else
                {
                    InferFromTypes(sourceTypes[i], targetTypes[i]);
                }
            }
        }

        void InferFromContravariantTypes(IType source, IType target)
        {
            contravariant = !contravariant;
            InferFromTypes(source, target);
            contravariant = !contravariant;
        }

        void InferFromContravariantTypesIfStrictFunctionTypes(IType source, IType target)
        {
            if (_strictFunctionTypes || ((int)priority & (int)InferencePriority.AlwaysStrict) != 0)
            {
                InferFromContravariantTypes(source, target);
            }
            else
            {
                InferFromTypes(source, target);
            }
        }

        IInferenceInfo? GetInferenceInfoForType(IType type)
        {
            if ((type.Flags & TypeFlags.TypeVariable) != 0)
            {
                foreach (var inference in inferences)
                {
                    if (ReferenceEquals(type, inference.TypeParameter))
                    {
                        return inference;
                    }
                }
            }
            return null;
        }

        IType? GetSingleTypeVariableFromIntersectionTypes(List<IType> types)
        {
            IType? typeVariable = null;
            foreach (var type in types)
            {
                var t = (type.Flags & TypeFlags.Intersection) != 0
                    ? Core.Find(((IIntersectionType)type).Types, (t, _) => GetInferenceInfoForType(t) is not null)
                    : null;
                if (t is null || typeVariable is not null && !ReferenceEquals(t, typeVariable))
                {
                    return null;
                }
                typeVariable = t;
            }
            return typeVariable;
        }

        void InferToMultipleTypes(IType source, List<IType> targets, TypeFlags targetFlags)
        {
            var typeVariableCount = 0;
            if ((targetFlags & TypeFlags.Union) != 0)
            {
                IType? nakedTypeVariable = null;
                var sources = (source.Flags & TypeFlags.Union) != 0 ? ((IUnionType)source).Types! : new List<IType> { source };
                var matched = new bool[sources.Count];
                var inferenceCircularity = false;
                // First infer to types that are not naked type variables. For each source type we
                // track whether inferences were made from that particular type to some target with
                // equal priority (i.e. of equal quality) to what we would infer for a naked type
                // parameter.
                foreach (var t in targets)
                {
                    if (GetInferenceInfoForType(t) is not null)
                    {
                        nakedTypeVariable = t;
                        typeVariableCount++;
                    }
                    else
                    {
                        for (var i = 0; i < sources.Count; i++)
                        {
                            var saveInferencePriority = inferencePriority;
                            inferencePriority = (int)InferencePriority.MaxValue;
                            InferFromTypes(sources[i], t);
                            if (inferencePriority == (int)priority) matched[i] = true;
                            inferenceCircularity = inferenceCircularity || inferencePriority == (int)InferencePriority.Circularity;
                            inferencePriority = Math.Min(inferencePriority, saveInferencePriority);
                        }
                    }
                }
                if (typeVariableCount == 0)
                {
                    // If every target is an intersection of types containing a single naked type variable,
                    // make a lower priority inference to that type variable. This handles inferring from
                    // 'A | B' to 'T & (X | Y)' where we want to infer 'A | B' for T.
                    var intersectionTypeVariable = GetSingleTypeVariableFromIntersectionTypes(targets);
                    if (intersectionTypeVariable is not null)
                    {
                        InferWithPriority(source, intersectionTypeVariable, InferencePriority.NakedTypeVariable);
                    }
                    return;
                }
                // If the target has a single naked type variable and no inference circularities were
                // encountered above (meaning we explored the types fully), create a union of the source
                // types from which no inferences have been made so far and infer from that union to the
                // naked type variable.
                if (typeVariableCount == 1 && !inferenceCircularity)
                {
                    List<IType> unmatched = [];
                    for (var i = 0; i < sources.Count; i++)
                    {
                        if (!matched[i])
                        {
                            unmatched.Add(sources[i]);
                        }
                    }
                    if (unmatched.Count != 0)
                    {
                        InferFromTypes(GetUnionType(unmatched), nakedTypeVariable!);
                        return;
                    }
                }
            }
            else
            {
                // We infer from types that are not naked type variables first so that inferences we
                // make from nested naked type variables and given slightly higher priority by virtue
                // of being first in the candidates array.
                foreach (var t in targets)
                {
                    if (GetInferenceInfoForType(t) is not null)
                    {
                        typeVariableCount++;
                    }
                    else
                    {
                        InferFromTypes(source, t);
                    }
                }
            }
            // Inferences directly to naked type variables are given lower priority as they are
            // less specific. For example, when inferring from Promise<string> to T | Promise<T>,
            // we want to infer string for T, not Promise<string> | string. For intersection types
            // we only infer to single naked type variables.
            if ((targetFlags & TypeFlags.Intersection) != 0 ? typeVariableCount == 1 : typeVariableCount > 0)
            {
                foreach (var t in targets)
                {
                    if (GetInferenceInfoForType(t) is not null)
                    {
                        InferWithPriority(source, t, InferencePriority.NakedTypeVariable);
                    }
                }
            }
        }

        bool InferToMappedType(IType source, IMappedType target, IType constraintType)
        {
            if ((constraintType.Flags & (TypeFlags.Union | TypeFlags.Intersection)) != 0)
            {
                var result = false;
                foreach (var type in ((IUnionOrIntersectionType)constraintType).Types!)
                {
                    result = InferToMappedType(source, target, type) || result;
                }
                return result;
            }
            if ((constraintType.Flags & TypeFlags.Index) != 0)
            {
                // We're inferring from some source type S to a homomorphic mapped type { [P in keyof T]: X },
                // where T is a type variable. Use inferTypeForHomomorphicMappedType to infer a suitable source
                // type and then make a secondary inference from that type to T. We make a secondary inference
                // such that direct inferences to T get priority over inferences to Partial<T>, for example.
                var inference = GetInferenceInfoForType(((IIndexType)constraintType).Type!);
                if (inference is not null && !inference.IsFixed && !IsFromInferenceBlockedSource(source))
                {
                    var inferredType = InferTypeForHomomorphicMappedType(source, target, (IIndexType)constraintType);
                    if (inferredType is not null)
                    {
                        // We assign a lower priority to inferences made from types containing non-inferrable
                        // types because we may only have a partial result (i.e. we may have failed to make
                        // reverse inferences for some properties).
                        InferWithPriority(
                            inferredType,
                            inference.TypeParameter!,
                            (GetObjectFlags(source) & ObjectFlags.NonInferrableType) != 0
                                ? InferencePriority.PartialHomomorphicMappedType
                                : InferencePriority.HomomorphicMappedType);
                    }
                }
                return true;
            }
            if ((constraintType.Flags & TypeFlags.TypeParameter) != 0)
            {
                // We're inferring from some source type S to a mapped type { [P in K]: X }, where K is a type
                // parameter. First infer from 'keyof S' to K.
                InferWithPriority(GetIndexType(source, /*indexFlags*/ source.Pattern is not null ? IndexFlags.NoIndexSignatures : IndexFlags.None), constraintType, InferencePriority.MappedTypeConstraint);
                // If K is constrained to a type C, also infer to C. Thus, for a mapped type { [P in K]: X },
                // where K extends keyof T, we make the same inferences as for a homomorphic mapped type
                // { [P in keyof T]: X }. This enables us to make meaningful inferences when the target is a
                // Pick<T, K>.
                var extendedConstraint = GetConstraintOfType(constraintType);
                if (extendedConstraint is not null && InferToMappedType(source, target, extendedConstraint))
                {
                    return true;
                }
                // If no inferences can be made to K's constraint, infer from a union of the property types
                // in the source to the template type X.
                var propTypes = Core.Map(GetPropertiesOfType(source), (p, _) => GetTypeOfSymbol(p));
                var indexTypes = Core.Map(GetIndexInfosOfType(source), (info, _) => !ReferenceEquals(info, _enumNumberIndexInfo) ? info.Type! : _neverType);
                InferFromTypes(GetUnionType((List<IType>)Core.Concatenate(propTypes, indexTypes)!), GetTemplateTypeFromMappedType(target));
                return true;
            }
            return false;
        }

        void InferToConditionalType(IType source, IConditionalType target)
        {
            if ((source.Flags & TypeFlags.Conditional) != 0)
            {
                InferFromTypes(((IConditionalType)source).CheckType!, target.CheckType!);
                InferFromTypes(((IConditionalType)source).ExtendsType!, target.ExtendsType!);
                InferFromTypes(GetTrueTypeFromConditionalType((IConditionalType)source), GetTrueTypeFromConditionalType(target));
                InferFromTypes(GetFalseTypeFromConditionalType((IConditionalType)source), GetFalseTypeFromConditionalType(target));
            }
            else
            {
                var targetTypes = new List<IType> { GetTrueTypeFromConditionalType(target), GetFalseTypeFromConditionalType(target) };
                InferToMultipleTypesWithPriority(source, targetTypes, target.Flags, contravariant ? InferencePriority.ContravariantConditional : InferencePriority.None);
            }
        }

        void InferToTemplateLiteralType(IType source, ITemplateLiteralType target)
        {
            var matches = InferTypesFromTemplateLiteralType(source, target);
            var types = target.Types!;
            // When the target template literal contains only placeholders (meaning that inference is intended to extract
            // single characters and remainder strings) and inference fails to produce matches, we want to infer 'never' for
            // each placeholder such that instantiation with the inferred value(s) produces 'never', a type for which an
            // assignment check will fail. If we make no inferences, we'll likely end up with the constraint 'string' which,
            // upon instantiation, would collapse all the placeholders to just 'string', and an assignment check might
            // succeed. That would be a pointless and confusing outcome.
            if (matches is not null || Core.Every(target.Texts, (s, _) => s.Length == 0))
            {
                for (var i = 0; i < types.Count; i++)
                {
                    var sourceType = matches is not null ? matches[i] : _neverType;
                    var targetType = types[i];

                    // If we are inferring from a string literal type to a type variable whose constraint includes one of the
                    // allowed template literal placeholder types, infer from a literal type corresponding to the constraint.
                    if ((sourceType.Flags & TypeFlags.StringLiteral) != 0 && (targetType.Flags & TypeFlags.TypeVariable) != 0)
                    {
                        var inferenceContext = GetInferenceInfoForType(targetType);
                        var constraint = inferenceContext is not null ? GetBaseConstraintOfType(inferenceContext.TypeParameter!) : null;
                        if (constraint is not null && !IsTypeAny(constraint))
                        {
                            var constraintTypes = (constraint.Flags & TypeFlags.Union) != 0 ? ((IUnionType)constraint).Types! : new List<IType> { constraint };
                            var allTypeFlags = Core.ReduceLeft<IType, TypeFlags>(constraintTypes, (flags, t, _) => flags | t.Flags, (TypeFlags)0);

                            // If the constraint contains `string`, we don't need to look for a more preferred type
                            if ((allTypeFlags & TypeFlags.String) == 0)
                            {
                                var str = (string)((IStringLiteralType)sourceType).Value!;
                                var numberValue = Js.ToNumber(str);

                                // If the type contains `number` or a number literal and the string isn't a valid number, exclude numbers
                                if ((allTypeFlags & TypeFlags.NumberLike) != 0 && !IsValidNumberString(str, /*roundTripOnly*/ true))
                                {
                                    allTypeFlags &= ~TypeFlags.NumberLike;
                                }

                                // If the type contains `bigint` or a bigint literal and the string isn't a valid bigint, exclude bigints
                                if ((allTypeFlags & TypeFlags.BigIntLike) != 0 && !Utilities.IsValidBigIntString(str, /*roundTripOnly*/ true))
                                {
                                    allTypeFlags &= ~TypeFlags.BigIntLike;
                                }

                                // for each type in the constraint, find the highest priority matching type
                                var matchingType = Core.ReduceLeft(constraintTypes, (left, right, _) =>
                                    (right.Flags & allTypeFlags) == 0 ? left :
                                        (left.Flags & TypeFlags.String) != 0 ? left : (right.Flags & TypeFlags.String) != 0 ? sourceType :
                                        (left.Flags & TypeFlags.TemplateLiteral) != 0 ? left : (right.Flags & TypeFlags.TemplateLiteral) != 0 && IsTypeMatchedByTemplateLiteralType(sourceType, (ITemplateLiteralType)right) ? sourceType :
                                        (left.Flags & TypeFlags.StringMapping) != 0 ? left : (right.Flags & TypeFlags.StringMapping) != 0 && str == ApplyStringMapping(right.Symbol!, str) ? sourceType :
                                        (left.Flags & TypeFlags.StringLiteral) != 0 ? left : (right.Flags & TypeFlags.StringLiteral) != 0 && (string)((IStringLiteralType)right).Value! == str ? right :
                                        (left.Flags & TypeFlags.Number) != 0 ? left : (right.Flags & TypeFlags.Number) != 0 ? GetNumberLiteralType(numberValue) :
                                        (left.Flags & TypeFlags.Enum) != 0 ? left : (right.Flags & TypeFlags.Enum) != 0 ? GetNumberLiteralType(numberValue) :
                                        (left.Flags & TypeFlags.NumberLiteral) != 0 ? left : (right.Flags & TypeFlags.NumberLiteral) != 0 && (double)((INumberLiteralType)right).Value! == numberValue ? right :
                                        (left.Flags & TypeFlags.BigInt) != 0 ? left : (right.Flags & TypeFlags.BigInt) != 0 ? ParseBigIntLiteralType(str) :
                                        (left.Flags & TypeFlags.BigIntLiteral) != 0 ? left : (right.Flags & TypeFlags.BigIntLiteral) != 0 && Utilities.PseudoBigIntToString((IPseudoBigInt)((IBigIntLiteralType)right).Value!) == str ? right :
                                        (left.Flags & TypeFlags.Boolean) != 0 ? left : (right.Flags & TypeFlags.Boolean) != 0 ? str == "true" ? _trueType : str == "false" ? _falseType : _booleanType :
                                        (left.Flags & TypeFlags.BooleanLiteral) != 0 ? left : (right.Flags & TypeFlags.BooleanLiteral) != 0 && ((IIntrinsicType)right).IntrinsicName == str ? right :
                                        (left.Flags & TypeFlags.Undefined) != 0 ? left : (right.Flags & TypeFlags.Undefined) != 0 && ((IIntrinsicType)right).IntrinsicName == str ? right :
                                        (left.Flags & TypeFlags.Null) != 0 ? left : (right.Flags & TypeFlags.Null) != 0 && ((IIntrinsicType)right).IntrinsicName == str ? right :
                                        left, _neverType)!;

                                if ((matchingType.Flags & TypeFlags.Never) == 0)
                                {
                                    InferFromTypes(matchingType, targetType);
                                    continue;
                                }
                            }
                        }
                    }

                    InferFromTypes(sourceType, targetType);
                }
            }
        }

        void InferFromGenericMappedTypes(IMappedType source, IMappedType target)
        {
            // The source and target types are generic types { [P in S]: X } and { [P in T]: Y }, so we infer
            // from S to T and from X to Y.
            InferFromTypes(GetConstraintTypeFromMappedType(source), GetConstraintTypeFromMappedType(target));
            InferFromTypes(GetTemplateTypeFromMappedType(source), GetTemplateTypeFromMappedType(target));
            var sourceNameType = GetNameTypeFromMappedType(source);
            var targetNameType = GetNameTypeFromMappedType(target);
            if (sourceNameType is not null && targetNameType is not null) InferFromTypes(sourceNameType, targetNameType);
        }

        void InferFromObjectTypes(IType source, IType target)
        {
            if (
                (GetObjectFlags(source) & ObjectFlags.Reference) != 0 && (GetObjectFlags(target) & ObjectFlags.Reference) != 0 && (
                    ReferenceEquals(((ITypeReference)source).Target, ((ITypeReference)target).Target) || IsArrayType(source) && IsArrayType(target)
                )
            )
            {
                // If source and target are references to the same generic type, infer from type arguments
                InferFromTypeArguments(GetTypeArguments((ITypeReference)source), GetTypeArguments((ITypeReference)target), GetVariances((IGenericType)((ITypeReference)source).Target!));
                return;
            }
            if (IsGenericMappedType(source) && IsGenericMappedType(target))
            {
                InferFromGenericMappedTypes((IMappedType)source, (IMappedType)target);
            }
            if ((GetObjectFlags(target) & ObjectFlags.Mapped) != 0 && ((IMappedType)target).Declaration!.NameType is null)
            {
                var constraintType = GetConstraintTypeFromMappedType((IMappedType)target);
                if (InferToMappedType(source, (IMappedType)target, constraintType))
                {
                    return;
                }
            }
            // Infer from the members of source and target only if the two types are possibly related
            if (!TypesDefinitelyUnrelated(source, target))
            {
                if (IsArrayOrTupleType(source))
                {
                    if (IsTupleType(target))
                    {
                        var sourceArity = GetTypeReferenceArity((ITypeReference)source);
                        var targetArity = GetTypeReferenceArity((ITypeReference)target);
                        var elementTypes = GetTypeArguments((ITypeReference)target);
                        var elementFlags = ((ITupleType)((ITypeReference)target).Target!).ElementFlags!;
                        // When source and target are tuple types with the same structure (fixed, variadic, and rest are matched
                        // to the same kind in each position), simply infer between the element types.
                        if (IsTupleType(source) && IsTupleTypeStructureMatching((ITypeReference)source, (ITypeReference)target))
                        {
                            for (var i = 0; i < targetArity; i++)
                            {
                                InferFromTypes(GetTypeArguments((ITypeReference)source)[i], elementTypes[i]);
                            }
                            return;
                        }
                        var sourceTuple = (ITupleType)((ITypeReference)source).Target!;
                        var targetTuple = (ITupleType)((ITypeReference)target).Target!;
                        var startLength = IsTupleType(source) ? Math.Min(sourceTuple.FixedLength, targetTuple.FixedLength) : 0;
                        var endLength = Math.Min(IsTupleType(source) ? GetEndElementCount(sourceTuple, ElementFlags.Fixed) : 0, (targetTuple.CombinedFlags & ElementFlags.Variable) != 0 ? GetEndElementCount(targetTuple, ElementFlags.Fixed) : 0);
                        // Infer between starting fixed elements.
                        for (var i = 0; i < startLength; i++)
                        {
                            InferFromTypes(GetTypeArguments((ITypeReference)source)[i], elementTypes[i]);
                        }
                        if (!IsTupleType(source) || sourceArity - startLength - endLength == 1 && (sourceTuple.ElementFlags![startLength] & ElementFlags.Rest) != 0)
                        {
                            // Single rest element remains in source, infer from that to every element in target
                            var restType = GetTypeArguments((ITypeReference)source)[startLength];
                            for (var i = startLength; i < targetArity - endLength; i++)
                            {
                                InferFromTypes((elementFlags[i] & ElementFlags.Variadic) != 0 ? CreateArrayType(restType) : restType, elementTypes[i]);
                            }
                        }
                        else
                        {
                            var middleLength = targetArity - startLength - endLength;
                            if (middleLength == 2)
                            {
                                if ((elementFlags[startLength] & elementFlags[startLength + 1] & ElementFlags.Variadic) != 0)
                                {
                                    // Middle of target is [...T, ...U] and source is tuple type
                                    var targetInfo = GetInferenceInfoForType(elementTypes[startLength]);
                                    if (targetInfo is not null && targetInfo.ImpliedArity is not null)
                                    {
                                        // Infer slices from source based on implied arity of T.
                                        InferFromTypes(SliceTupleType((ITupleTypeReference)source, startLength, endLength + sourceArity - targetInfo.ImpliedArity.Value), elementTypes[startLength]);
                                        InferFromTypes(SliceTupleType((ITupleTypeReference)source, startLength + targetInfo.ImpliedArity.Value, endLength), elementTypes[startLength + 1]);
                                    }
                                }
                                else if ((elementFlags[startLength] & ElementFlags.Variadic) != 0 && (elementFlags[startLength + 1] & ElementFlags.Rest) != 0)
                                {
                                    // Middle of target is [...T, ...rest] and source is tuple type
                                    // if T is constrained by a fixed-size tuple we might be able to use its arity to infer T
                                    var param = GetInferenceInfoForType(elementTypes[startLength])?.TypeParameter;
                                    var constraint = param is not null ? GetBaseConstraintOfType(param) : null;
                                    if (constraint is not null && IsTupleType(constraint) && (((ITupleType)((ITypeReference)constraint).Target!).CombinedFlags & ElementFlags.Variable) == 0)
                                    {
                                        var impliedArity = ((ITupleType)((ITypeReference)constraint).Target!).FixedLength;
                                        InferFromTypes(SliceTupleType((ITupleTypeReference)source, startLength, sourceArity - (startLength + impliedArity)), elementTypes[startLength]);
                                        InferFromTypes(GetElementTypeOfSliceOfTupleType((ITypeReference)source, startLength + impliedArity, endLength)!, elementTypes[startLength + 1]);
                                    }
                                }
                                else if ((elementFlags[startLength] & ElementFlags.Rest) != 0 && (elementFlags[startLength + 1] & ElementFlags.Variadic) != 0)
                                {
                                    // Middle of target is [...rest, ...T] and source is tuple type
                                    // if T is constrained by a fixed-size tuple we might be able to use its arity to infer T
                                    var param = GetInferenceInfoForType(elementTypes[startLength + 1])?.TypeParameter;
                                    var constraint = param is not null ? GetBaseConstraintOfType(param) : null;
                                    if (constraint is not null && IsTupleType(constraint) && (((ITupleType)((ITypeReference)constraint).Target!).CombinedFlags & ElementFlags.Variable) == 0)
                                    {
                                        var impliedArity = ((ITupleType)((ITypeReference)constraint).Target!).FixedLength;
                                        var endIndex = sourceArity - GetEndElementCount(targetTuple, ElementFlags.Fixed);
                                        var startIndex = endIndex - impliedArity;
                                        var sourceArguments = GetTypeArguments((ITypeReference)source);
                                        var sourceElementFlags = sourceTuple.ElementFlags!;
                                        var labeledDeclarations = sourceTuple.LabeledElementDeclarations;
                                        var trailingSlice = CreateTupleType(
                                            sourceArguments.GetRange(startIndex, endIndex - startIndex),
                                            sourceElementFlags.GetRange(startIndex, endIndex - startIndex),
                                            /*readonly*/ false,
                                            labeledDeclarations is not null ? labeledDeclarations.GetRange(startIndex, endIndex - startIndex) : null);

                                        InferFromTypes(GetElementTypeOfSliceOfTupleType((ITypeReference)source, startLength, endLength + impliedArity)!, elementTypes[startLength]);
                                        InferFromTypes(trailingSlice, elementTypes[startLength + 1]);
                                    }
                                }
                            }
                            else if (middleLength == 1 && (elementFlags[startLength] & ElementFlags.Variadic) != 0)
                            {
                                // Middle of target is exactly one variadic element. Infer the slice between the fixed parts in the source.
                                // If target ends in optional element(s), make a lower priority a speculative inference.
                                var endsInOptional = (targetTuple.ElementFlags![targetArity - 1] & ElementFlags.Optional) != 0;
                                var sourceSlice = SliceTupleType((ITupleTypeReference)source, startLength, endLength);
                                InferWithPriority(sourceSlice, elementTypes[startLength], endsInOptional ? InferencePriority.SpeculativeTuple : InferencePriority.None);
                            }
                            else if (middleLength == 1 && (elementFlags[startLength] & ElementFlags.Rest) != 0)
                            {
                                // Middle of target is exactly one rest element. If middle of source is not empty, infer union of middle element types.
                                var restType = GetElementTypeOfSliceOfTupleType((ITypeReference)source, startLength, endLength);
                                if (restType is not null)
                                {
                                    InferFromTypes(restType, elementTypes[startLength]);
                                }
                            }
                        }
                        // Infer between ending fixed elements
                        for (var i = 0; i < endLength; i++)
                        {
                            InferFromTypes(GetTypeArguments((ITypeReference)source)[sourceArity - i - 1], elementTypes[targetArity - i - 1]);
                        }
                        return;
                    }
                    if (IsArrayType(target))
                    {
                        InferFromIndexTypes(source, target);
                        return;
                    }
                }
                InferFromProperties(source, target);
                InferFromSignatures(source, target, SignatureKind.Call);
                InferFromSignatures(source, target, SignatureKind.Construct);
                InferFromIndexTypes(source, target);
            }
        }

        void InferFromProperties(IType source, IType target)
        {
            var properties = GetPropertiesOfObjectType(target);
            foreach (var targetProp in properties)
            {
                var sourceProp = GetPropertyOfType(source, targetProp.EscapedName!);
                if (sourceProp is not null && !Core.Some(sourceProp.Declarations, d => HasSkipDirectInferenceFlag((Node)d)))
                {
                    InferFromTypes(
                        RemoveMissingType(GetTypeOfSymbol(sourceProp), (sourceProp.Flags & SymbolFlags.Optional) != 0),
                        RemoveMissingType(GetTypeOfSymbol(targetProp), (targetProp.Flags & SymbolFlags.Optional) != 0));
                }
            }
        }

        void InferFromSignatures(IType source, IType target, SignatureKind kind)
        {
            var sourceSignatures = GetSignaturesOfType(source, kind);
            var sourceLen = sourceSignatures.Count;
            if (sourceLen > 0)
            {
                // We match source and target signatures from the bottom up, and if the source has fewer signatures
                // than the target, we infer from the first source signature to the excess target signatures.
                var targetSignatures = GetSignaturesOfType(target, kind);
                var targetLen = targetSignatures.Count;
                for (var i = 0; i < targetLen; i++)
                {
                    var sourceIndex = Math.Max(sourceLen - targetLen + i, 0);
                    InferFromSignature(GetBaseSignature(sourceSignatures[sourceIndex]), GetErasedSignature(targetSignatures[i]));
                }
            }
        }

        void InferFromSignature(ISignature source, ISignature target)
        {
            if ((source.Flags & SignatureFlags.IsNonInferrable) == 0)
            {
                var saveBivariant = bivariant;
                var kind = target.Declaration is not null ? target.Declaration.Kind : SyntaxKind.Unknown;
                // Once we descend into a bivariant signature we remain bivariant for all nested inferences
                bivariant = bivariant || kind == SyntaxKind.MethodDeclaration || kind == SyntaxKind.MethodSignature || kind == SyntaxKind.Constructor;
                ApplyToParameterTypes(source, target, InferFromContravariantTypesIfStrictFunctionTypes);
                bivariant = saveBivariant;
            }
            ApplyToReturnTypes(source, target, InferFromTypes);
        }

        void InferFromIndexTypes(IType source, IType target)
        {
            // Inferences across mapped type index signatures are pretty much the same a inferences to homomorphic variables
            var priorityForIndexTypes = (GetObjectFlags(source) & GetObjectFlags(target) & ObjectFlags.Mapped) != 0 ? InferencePriority.HomomorphicMappedType : InferencePriority.None;
            var indexInfos = GetIndexInfosOfType(target);
            if (IsObjectTypeWithInferableIndex(source))
            {
                foreach (var targetInfo in indexInfos)
                {
                    List<IType> propTypes = [];
                    foreach (var prop in GetPropertiesOfType(source))
                    {
                        if (IsApplicableIndexType(GetLiteralTypeFromProperty(prop, TypeFlags.StringOrNumberLiteralOrUnique), targetInfo.KeyType!))
                        {
                            var propType = GetTypeOfSymbol(prop);
                            propTypes.Add((prop.Flags & SymbolFlags.Optional) != 0 ? RemoveMissingOrUndefinedType(propType) : propType);
                        }
                    }
                    foreach (var info in GetIndexInfosOfType(source))
                    {
                        if (IsApplicableIndexType(info.KeyType!, targetInfo.KeyType!))
                        {
                            propTypes.Add(info.Type!);
                        }
                    }
                    if (propTypes.Count != 0)
                    {
                        InferWithPriority(GetUnionType(propTypes), targetInfo.Type!, priorityForIndexTypes);
                    }
                }
            }
            foreach (var targetInfo in indexInfos)
            {
                var sourceInfo = GetApplicableIndexInfo(source, targetInfo.KeyType!);
                if (sourceInfo is not null)
                {
                    InferWithPriority(sourceInfo.Type!, targetInfo.Type!, priorityForIndexTypes);
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:27549-27552 — <c>isTypeOrBaseIdenticalTo</c>.</summary>
    internal bool IsTypeOrBaseIdenticalTo(IType s, IType t)
        => ReferenceEquals(t, _missingType)
            ? ReferenceEquals(s, t)
            : IsTypeIdenticalTo(s, t)
                || ((t.Flags & TypeFlags.String) != 0 && (s.Flags & TypeFlags.StringLiteral) != 0
                    || (t.Flags & TypeFlags.Number) != 0 && (s.Flags & TypeFlags.NumberLiteral) != 0);

    /// <summary>ported from: src/compiler/checker.ts:27554-27557 — <c>isTypeCloselyMatchedBy</c>.</summary>
    internal bool IsTypeCloselyMatchedBy(IType s, IType t)
        => (s.Flags & TypeFlags.Object) != 0 && (t.Flags & TypeFlags.Object) != 0 && s.Symbol is not null && ReferenceEquals(s.Symbol, t.Symbol)
            || s.AliasSymbol is not null && s.AliasTypeArguments is not null && ReferenceEquals(s.AliasSymbol, t.AliasSymbol);

    /// <summary>ported from: src/compiler/checker.ts:27559-27562 — <c>hasPrimitiveConstraint</c>.</summary>
    internal bool HasPrimitiveConstraint(ITypeParameter type)
    {
        var constraint = GetConstraintOfTypeParameter(type);
        return constraint is not null && MaybeTypeOfKind(
            (constraint.Flags & TypeFlags.Conditional) != 0 ? GetDefaultConstraintOfConditionalType((IConditionalType)constraint) : constraint,
            TypeFlags.Primitive | TypeFlags.Index | TypeFlags.TemplateLiteral | TypeFlags.StringMapping);
    }

    /// <summary>ported from: src/compiler/checker.ts:27564-27566 — <c>isObjectLiteralType</c>.</summary>
    internal bool IsObjectLiteralType(IType type)
        => (GetObjectFlags(type) & ObjectFlags.ObjectLiteral) != 0;

    /// <summary>ported from: src/compiler/checker.ts:27568-27570 — <c>isObjectOrArrayLiteralType</c>.</summary>
    internal bool IsObjectOrArrayLiteralType(IType type)
        => (GetObjectFlags(type) & (ObjectFlags.ObjectLiteral | ObjectFlags.ArrayLiteral)) != 0;

    /// <summary>ported from: src/compiler/checker.ts:27572-27581 — <c>unionObjectAndArrayLiteralCandidates</c>.</summary>
    internal List<IType> UnionObjectAndArrayLiteralCandidates(List<IType> candidates)
    {
        if (candidates.Count > 1)
        {
            var objectLiterals = Core.Filter(candidates, IsObjectOrArrayLiteralType);
            if (Core.Length(objectLiterals) != 0)
            {
                var literalsType = GetUnionType((List<IType>)objectLiterals!, UnionReduction.Subtype);
                return [.. Core.Filter(candidates, t => !IsObjectOrArrayLiteralType(t))!, literalsType];
            }
        }
        return candidates;
    }

    /// <summary>ported from: src/compiler/checker.ts:27583-27585 — <c>getContravariantInference</c>.</summary>
    internal IType GetContravariantInference(IInferenceInfo inference)
        => ((int)inference.Priority!.Value & (int)InferencePriority.PriorityImpliesCombination) != 0
            ? GetIntersectionType(inference.ContraCandidates!)
            : GetCommonSubtype(inference.ContraCandidates!);

    /// <summary>ported from: src/compiler/checker.ts:27587-27606 — <c>getCovariantInference</c>.</summary>
    internal IType GetCovariantInference(IInferenceInfo inference, ISignature signature)
    {
        // Extract all object and array literal types and replace them with a single widened and normalized type.
        var candidates = UnionObjectAndArrayLiteralCandidates(inference.Candidates!);
        // We widen inferred literal types if
        // all inferences were made to top-level occurrences of the type parameter, and
        // the type parameter has no constraint or its constraint includes no primitive or literal types, and
        // the type parameter was fixed during inference or does not occur at top-level in the return type.
        var primitiveConstraint = HasPrimitiveConstraint(inference.TypeParameter!) || IsConstTypeVariable(inference.TypeParameter!);
        var widenLiteralTypes = !primitiveConstraint && inference.TopLevel &&
            (inference.IsFixed || !IsTypeParameterAtTopLevelInReturnType(signature, inference.TypeParameter!));
        var baseCandidates = primitiveConstraint ? (List<IType>)Core.SameMap(candidates, (t, _) => GetRegularTypeOfLiteralType(t))!
            : widenLiteralTypes ? (List<IType>)Core.SameMap(candidates, (t, _) => GetWidenedLiteralType(t))!
            : candidates;
        // If all inferences were made from a position that implies a combined result, infer a union type.
        // Otherwise, infer a common supertype.
        var unwidenedType = ((int)inference.Priority!.Value & (int)InferencePriority.PriorityImpliesCombination) != 0
            ? GetUnionType(baseCandidates, UnionReduction.Subtype)
            : GetCommonSupertype(baseCandidates);
        return GetWidenedType(unwidenedType);
    }

    /// <summary>ported from: src/compiler/checker.ts:27608-27678 — <c>getInferredType</c>.</summary>
    internal IType GetInferredType(IInferenceContext context, int index)
    {
        var inference = context.Inferences![index];
        if (inference.InferredType is null)
        {
            IType? inferredType = null;
            IType? fallbackType = null;
            if (context.Signature is not null)
            {
                var inferredCovariantType = inference.Candidates is not null ? GetCovariantInference(inference, context.Signature) : null;
                var inferredContravariantType = inference.ContraCandidates is not null ? GetContravariantInference(inference) : null;
                if (inferredCovariantType is not null || inferredContravariantType is not null)
                {
                    // If we have both co- and contra-variant inferences, we prefer the co-variant inference if it is not 'never',
                    // all co-variant inferences are assignable to it (i.e. it isn't one of a conflicting set of candidates), it is
                    // assignable to some contra-variant inference, and no other type parameter is constrained to this type parameter
                    // and has inferences that would conflict. Otherwise, we prefer the contra-variant inference.
                    // Similarly ignore co-variant `any` inference when both are available as almost everything is assignable to it
                    // and it would spoil the overall inference.
                    var preferCovariantType = inferredCovariantType is not null && (inferredContravariantType is null ||
                        (inferredCovariantType.Flags & (TypeFlags.Never | TypeFlags.Any)) == 0 &&
                            Core.Some(inference.ContraCandidates, t => IsTypeAssignableTo(inferredCovariantType, t)) &&
                            Core.Every(context.Inferences, (other, _) =>
                                !ReferenceEquals(other, inference) && !ReferenceEquals(GetConstraintOfTypeParameter(other.TypeParameter!), inference.TypeParameter)
                                    || Core.Every(other.Candidates, (t, _) => IsTypeAssignableTo(t, inferredCovariantType))));
                    inferredType = preferCovariantType ? inferredCovariantType : inferredContravariantType;
                    fallbackType = preferCovariantType ? inferredContravariantType : inferredCovariantType;
                }
                else if ((context.Flags & InferenceFlags.NoDefault) != 0)
                {
                    // We use silentNeverType as the wildcard that signals no inferences.
                    inferredType = _silentNeverType;
                }
                else
                {
                    // Infer either the default or the empty object type when no inferences were
                    // made. It is important to remember that in this case, inference still
                    // succeeds, meaning there is no error for not having inference candidates. An
                    // inference error only occurs when there are *conflicting* candidates, i.e.
                    // candidates with no common supertype.
                    var defaultType = GetDefaultFromTypeParameter(inference.TypeParameter!);
                    if (defaultType is not null)
                    {
                        // Instantiate the default type. Any forward reference to a type
                        // parameter should be instantiated to the empty object type.
                        inferredType = InstantiateType(defaultType, MergeTypeMappers(CreateBackreferenceMapper(context, index), (TypeMapper)context.NonFixingMapper!));
                    }
                }
            }
            else
            {
                inferredType = GetTypeFromInference(inference);
            }

            inference.InferredType = inferredType ?? GetDefaultTypeArgumentType((context.Flags & InferenceFlags.AnyDefault) != 0);

            var constraint = GetConstraintOfTypeParameter(inference.TypeParameter!);
            if (constraint is not null)
            {
                var instantiatedConstraint = InstantiateType(constraint, (TypeMapper?)context.NonFixingMapper);
                if (inferredType is not null)
                {
                    var constraintWithThis = GetTypeWithThisArgument(instantiatedConstraint!, inferredType);
                    if (context.CompareTypes!(inferredType, constraintWithThis, false) == Ternary.False)
                    {
                        // If we have a pure return type inference, we may succeed by removing constituents of the inferred type
                        // that aren't assignable to the constraint type (pure return type inferences are speculation anyway).
                        var filteredByConstraint = inference.Priority == InferencePriority.ReturnType
                            ? FilterType(inferredType, t => context.CompareTypes!(t, constraintWithThis, false) != Ternary.False)
                            : _neverType;
                        inferredType = (filteredByConstraint.Flags & TypeFlags.Never) == 0 ? filteredByConstraint : null;
                    }
                }
                if (inferredType is null)
                {
                    // If the fallback type satisfies the constraint, we pick it. Otherwise, we pick the constraint.
                    inferredType = fallbackType is not null && context.CompareTypes!(fallbackType, GetTypeWithThisArgument(instantiatedConstraint!, fallbackType), false) != Ternary.False
                        ? fallbackType
                        : instantiatedConstraint;
                }
                inference.InferredType = inferredType;
            }
            ClearActiveMapperCaches();
        }

        return inference.InferredType!;
    }

    /// <summary>ported from: src/compiler/checker.ts:27680-27682 — <c>getDefaultTypeArgumentType</c>.</summary>
    internal IType GetDefaultTypeArgumentType(bool isInJavaScriptFile)
        => isInJavaScriptFile ? _anyType : _unknownType;

    /// <summary>ported from: src/compiler/checker.ts:27684-27690 — <c>getInferredTypes</c>.</summary>
    internal List<IType> GetInferredTypes(IInferenceContext context)
    {
        List<IType> result = [];
        for (var i = 0; i < context.Inferences!.Count; i++)
        {
            result.Add(GetInferredType(context, i));
        }
        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:27694-27755 — <c>getCannotFindNameDiagnosticForName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>switch</c> over <c>node.escapedText</c> with fall-through groups is a C# <c>switch</c> over the
    /// same string with the same grouped <c>case</c> labels; the one fall-through (the <c>"await"</c> arm into
    /// <c>default</c>) is written as an explicit <c>goto default</c>, which is the C# construct with that meaning.
    /// </remarks>
    internal DiagnosticMessage GetCannotFindNameDiagnosticForName(Identifier node)
    {
        switch (node.EscapedText)
        {
            case "document":
            case "console":
                return Diagnostics.Cannot_find_name_0_Do_you_need_to_change_your_target_library_Try_changing_the_lib_compiler_option_to_include_dom;
            case "$":
                return Utilities.UsesWildcardTypes(_compilerOptions)
                    ? Diagnostics.Cannot_find_name_0_Do_you_need_to_install_type_definitions_for_jQuery_Try_npm_i_save_dev_types_Slashjquery
                    : Diagnostics.Cannot_find_name_0_Do_you_need_to_install_type_definitions_for_jQuery_Try_npm_i_save_dev_types_Slashjquery_and_then_add_jquery_to_the_types_field_in_your_tsconfig;
            case "beforeEach":
            case "describe":
            case "suite":
            case "it":
            case "test":
                return Utilities.UsesWildcardTypes(_compilerOptions)
                    ? Diagnostics.Cannot_find_name_0_Do_you_need_to_install_type_definitions_for_a_test_runner_Try_npm_i_save_dev_types_Slashjest_or_npm_i_save_dev_types_Slashmocha
                    : Diagnostics.Cannot_find_name_0_Do_you_need_to_install_type_definitions_for_a_test_runner_Try_npm_i_save_dev_types_Slashjest_or_npm_i_save_dev_types_Slashmocha_and_then_add_jest_or_mocha_to_the_types_field_in_your_tsconfig;
            case "process":
            case "require":
            case "Buffer":
            case "module":
            case "NodeJS":
                return Utilities.UsesWildcardTypes(_compilerOptions)
                    ? Diagnostics.Cannot_find_name_0_Do_you_need_to_install_type_definitions_for_node_Try_npm_i_save_dev_types_Slashnode
                    : Diagnostics.Cannot_find_name_0_Do_you_need_to_install_type_definitions_for_node_Try_npm_i_save_dev_types_Slashnode_and_then_add_node_to_the_types_field_in_your_tsconfig;
            case "Bun":
                return Utilities.UsesWildcardTypes(_compilerOptions)
                    ? Diagnostics.Cannot_find_name_0_Do_you_need_to_install_type_definitions_for_Bun_Try_npm_i_save_dev_types_Slashbun
                    : Diagnostics.Cannot_find_name_0_Do_you_need_to_install_type_definitions_for_Bun_Try_npm_i_save_dev_types_Slashbun_and_then_add_bun_to_the_types_field_in_your_tsconfig;
            case "Map":
            case "Set":
            case "Promise":
            case "Symbol":
            case "WeakMap":
            case "WeakSet":
            case "Iterator":
            case "AsyncIterator":
            case "SharedArrayBuffer":
            case "Atomics":
            case "AsyncIterable":
            case "AsyncIterableIterator":
            case "AsyncGenerator":
            case "AsyncGeneratorFunction":
            case "BigInt":
            case "Reflect":
            case "BigInt64Array":
            case "BigUint64Array":
                return Diagnostics.Cannot_find_name_0_Do_you_need_to_change_your_target_library_Try_changing_the_lib_compiler_option_to_1_or_later;
            case "await":
                if (NodeTests.IsCallExpression(node.Parent!))
                {
                    return Diagnostics.Cannot_find_name_0_Did_you_mean_to_write_this_in_an_async_function;
                }
                goto default;
            default:
                if (node.Parent!.Kind == SyntaxKind.ShorthandPropertyAssignment)
                {
                    return Diagnostics.No_value_exists_in_scope_for_the_shorthand_property_0_Either_declare_one_or_provide_an_initializer;
                }
                else
                {
                    return Diagnostics.Cannot_find_name_0;
                }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:27757-27769 — <c>getCannotResolveModuleNameErrorForSpecificModule</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>nodeCoreModules</c> (utilities.ts:12165) is unlanded — its two inputs are read by
    /// <c>Program.Graph.cs:125/:130</c> but declared nowhere — so the predicate is called as
    /// <c>Utilities.NodeCoreModules</c>, which is where its reference declaration site puts it (M6Utilities lands it).
    /// The reference's parameter is <c>Expression</c>, whose port face is the <c>Expression</c> class.
    /// </remarks>
    internal DiagnosticMessage? GetCannotResolveModuleNameErrorForSpecificModule(Expression? moduleName)
    {
        if (moduleName is not null && moduleName.Kind == SyntaxKind.StringLiteral)
        {
            if (Utilities.NodeCoreModules.Contains(((StringLiteral)moduleName).Text!))
            {
                if (Utilities.UsesWildcardTypes(_compilerOptions))
                {
                    return Diagnostics.Cannot_find_name_0_Do_you_need_to_install_type_definitions_for_node_Try_npm_i_save_dev_types_Slashnode;
                }
                else
                {
                    return Diagnostics.Cannot_find_name_0_Do_you_need_to_install_type_definitions_for_node_Try_npm_i_save_dev_types_Slashnode_and_then_add_node_to_the_types_field_in_your_tsconfig;
                }
            }
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:53812-53819 — <c>sortSymbolsIfTSGoCompat</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's two overloads collapse into this one member (the port's <c>null</c> is TS's
    /// <c>undefined</c>), and its in-place <c>array.sort(compareSymbols)</c> becomes <c>Core.StableSort</c> — TS's
    /// <c>Array.prototype.sort</c> is stable since ES2019 while the BCL's <c>List.Sort</c> is not — with the
    /// <c>Core.CompareValues(x, 0)</c> adapter that turns the JS-style <c>int</c> comparator into the port's
    /// <see cref="Comparer{T}"/> delegate (docs/architecture.md §15.15).
    /// </remarks>
    internal List<ISymbol>? SortSymbolsIfTSGoCompat(List<ISymbol>? array)
    {
        if (_stableTypeOrdering && array is not null)
        {
            Core.StableSort(array, (a, b) => Core.CompareValues(CompareSymbols(a, b), 0));
            return array;
        }
        return array;
    }

    // ---------------------------------------------------------------------------------------------
    // Contextual typing (checker.ts:31850-33261)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:31850-31856 — <c>getContainingObjectLiteral</c>.</summary>
    internal ObjectLiteralExpression? GetContainingObjectLiteral(Node func)
        => (func.Kind == SyntaxKind.MethodDeclaration
                || func.Kind == SyntaxKind.GetAccessor
                || func.Kind == SyntaxKind.SetAccessor) && func.Parent!.Kind == SyntaxKind.ObjectLiteralExpression ? (ObjectLiteralExpression)func.Parent! :
            func.Kind == SyntaxKind.FunctionExpression && func.Parent!.Kind == SyntaxKind.PropertyAssignment ? (ObjectLiteralExpression)func.Parent!.Parent! :
            null;

    /// <summary>ported from: src/compiler/checker.ts:31858-31860 — <c>getThisTypeArgument</c>.</summary>
    internal IType? GetThisTypeArgument(IType type)
        => (GetObjectFlags(type) & ObjectFlags.Reference) != 0 && ReferenceEquals(((ITypeReference)type).Target, _globalThisType)
            ? GetTypeArguments((ITypeReference)type)[0]
            : null;

    /// <summary>ported from: src/compiler/checker.ts:31862-31866 — <c>getThisTypeFromContextualType</c>.</summary>
    internal IType? GetThisTypeFromContextualType(IType type)
        => MapType(type, t => (t.Flags & TypeFlags.Intersection) != 0
            ? Core.ForEach(((IIntersectionType)t).Types, (u, _) => GetThisTypeArgument(u))
            : GetThisTypeArgument(t));

    /// <summary>ported from: src/compiler/checker.ts:31868-31882 — <c>getThisTypeOfObjectLiteralFromContextualType</c>.</summary>
    internal IType? GetThisTypeOfObjectLiteralFromContextualType(ObjectLiteralExpression containingLiteral, IType? contextualType)
    {
        var literal = containingLiteral;
        var type = contextualType;
        while (type is not null)
        {
            var thisType = GetThisTypeFromContextualType(type);
            if (thisType is not null)
            {
                return thisType;
            }
            if (literal.Parent!.Kind != SyntaxKind.PropertyAssignment)
            {
                break;
            }
            literal = (ObjectLiteralExpression)literal.Parent!.Parent!;
            type = GetApparentTypeOfContextualType(literal, /*contextFlags*/ null);
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:31884-31934 — <c>getContextualThisParameterType</c>.</summary>
    internal IType? GetContextualThisParameterType(Node func)
    {
        if (func.Kind == SyntaxKind.ArrowFunction)
        {
            return null;
        }
        if (IsContextSensitiveFunctionOrObjectLiteralMethod(func))
        {
            var contextualSignature = GetContextualSignature(func);
            if (contextualSignature is not null)
            {
                var thisParameter = contextualSignature.ThisParameter;
                if (thisParameter is not null)
                {
                    return GetTypeOfSymbol(thisParameter);
                }
            }
        }
        var inJs = Utilities.IsInJSFile(func);
        if (_noImplicitThis || inJs)
        {
            var containingLiteral = GetContainingObjectLiteral(func);
            if (containingLiteral is not null)
            {
                // We have an object literal method. Check if the containing object literal has a contextual type
                // that includes a ThisType<T>. If so, T is the contextual type for 'this'. We continue looking in
                // any directly enclosing object literals.
                var contextualType = GetApparentTypeOfContextualType(containingLiteral, /*contextFlags*/ null);
                var thisType = GetThisTypeOfObjectLiteralFromContextualType(containingLiteral, contextualType);
                if (thisType is not null)
                {
                    return InstantiateType(thisType, GetMapperFromContext(GetInferenceContext(containingLiteral)));
                }
                // There was no contextual ThisType<T> for the containing object literal, so the contextual type
                // for 'this' is the non-null form of the contextual type for the containing object literal or
                // the type of the object literal itself.
                return GetWidenedType(contextualType is not null ? GetNonNullableType(contextualType) : CheckExpressionCached(containingLiteral));
            }
            // In an assignment of the form 'obj.xxx = function(...)' or 'obj[xxx] = function(...)', the
            // contextual type for 'this' is 'obj'.
            var parent = Utilities.WalkUpParenthesizedExpressions(func.Parent!);
            if (Utilities.IsAssignmentExpression(parent))
            {
                var target = ((BinaryExpression)parent).Left;
                if (Utilities.IsAccessExpression(target!))
                {
                    // TS reads `target.expression` off the AccessExpression union; the port's two shapes carry it as
                    // the same slot, and the chain subclasses derive from them, so the switch covers all four kinds.
                    Expression expression = target switch
                    {
                        PropertyAccessExpression access => access.Expression!,
                        ElementAccessExpression access => access.Expression!,
                        _ => Debug.Fail<Expression>("an access expression must have an expression"),
                    };
                    // Don't contextually type `this` as `exports` in `exports.Point = function(x, y) { this.x = x; this.y = y; }`
                    if (inJs && NodeTests.IsIdentifier(expression))
                    {
                        var sourceFile = Utilities.GetSourceFileOfNode(parent);
                        if (sourceFile!.CommonJsModuleIndicator is not null && ReferenceEquals(GetResolvedSymbol((Identifier)expression), sourceFile.Symbol))
                        {
                            return null;
                        }
                    }

                    return GetWidenedType(CheckExpressionCached(expression));
                }
            }
        }
        return null;
    }

    // Return contextual type of parameter or undefined if no contextual type is available
    /// <summary>ported from: src/compiler/checker.ts:31937-31965 — <c>getContextuallyTypedParameterType</c>.</summary>
    internal IType? GetContextuallyTypedParameterType(ParameterDeclaration parameter)
    {
        var func = parameter.Parent!;
        if (!IsContextSensitiveFunctionOrObjectLiteralMethod(func))
        {
            return null;
        }
        var iife = Utilities.GetImmediatelyInvokedFunctionExpression(func);
        if (iife is not null)
        {
            var args = GetEffectiveCallArguments(iife);
            var indexOfParameter = ((ISignatureDeclarationBase)func).Parameters!.IndexOf(parameter);
            if (parameter.DotDotDotToken is not null)
            {
                return GetSpreadArgumentType(args, indexOfParameter, args.Count, _anyType, /*context*/ null, CheckMode.Normal);
            }
            var links = GetNodeLinks(iife);
            var cached = links.ResolvedSignature;
            links.ResolvedSignature = _anySignature;
            var type = indexOfParameter < args.Count
                ? GetWidenedLiteralType(CheckExpression(args[indexOfParameter]))
                : parameter.Initializer is not null ? null : _undefinedWideningType;
            links.ResolvedSignature = cached;
            return type;
        }
        var contextualSignature = GetContextualSignature(func);
        if (contextualSignature is not null)
        {
            var index = ((ISignatureDeclarationBase)func).Parameters!.IndexOf(parameter) - (Utilities.GetThisParameter(func) is not null ? 1 : 0);
            return parameter.DotDotDotToken is not null && ReferenceEquals(Core.LastOrUndefined(((ISignatureDeclarationBase)func).Parameters!), parameter)
                ? GetRestTypeAtPosition(contextualSignature, index)
                : TryGetTypeAtPosition(contextualSignature, index);
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:31967-31983 — <c>getContextualTypeForVariableLikeDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>VariableLikeDeclaration</c> union is the port's <c>Node</c> face: the three members it names are
    /// the three <c>switch</c> arms below, and each arm casts to the class whose member it reads.
    /// </remarks>
    internal IType? GetContextualTypeForVariableLikeDeclaration(Node declaration, ContextFlags? contextFlags)
    {
        var typeNode = Utilities.GetEffectiveTypeAnnotationNode(declaration) ?? (Utilities.IsInJSFile(declaration) ? Utilities.TryGetJSDocSatisfiesTypeNode(declaration) : null);
        if (typeNode is not null)
        {
            return GetTypeFromTypeNode(typeNode);
        }
        switch (declaration.Kind)
        {
            case SyntaxKind.Parameter:
                return GetContextuallyTypedParameterType((ParameterDeclaration)declaration);
            case SyntaxKind.BindingElement:
                return GetContextualTypeForBindingElement((BindingElement)declaration, contextFlags);
            case SyntaxKind.PropertyDeclaration:
                if (Utilities.IsStatic(declaration))
                {
                    return GetContextualTypeForStaticPropertyDeclaration((PropertyDeclaration)declaration, contextFlags);
                }
                // By default, do nothing and return undefined - only the above cases have context implied by a parent
                break;
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:31985-32001 — <c>getContextualTypeForBindingElement</c>.</summary>
    internal IType? GetContextualTypeForBindingElement(BindingElement declaration, ContextFlags? contextFlags)
    {
        var parent = declaration.Parent!.Parent!;
        Node name = declaration.PropertyName ?? declaration.Name!;
        var parentType = GetContextualTypeForVariableLikeDeclaration(parent, contextFlags)
            ?? (parent.Kind != SyntaxKind.BindingElement && Utilities.HasInitializer(parent)
                ? CheckDeclarationInitializer(parent, declaration.DotDotDotToken is not null ? CheckMode.RestBindingElement : CheckMode.Normal)
                : null);
        if (parentType is null || Utilities.IsBindingPattern(name) || Utilities.IsComputedNonLiteralName(name)) return null;
        var parentName = Utilities.GetNameOfDeclaration(parent);
        if (parentName is Node parentNameNode && parentNameNode.Kind == SyntaxKind.ArrayBindingPattern)
        {
            var elements = ((ArrayBindingPattern)parentNameNode).Elements;
            var index = elements is not null ? elements.IndexOf(declaration) : -1;
            if (index < 0) return null;
            return GetContextualTypeForElementExpression(parentType, index);
        }
        var nameType = GetLiteralTypeFromPropertyName(name);
        if (Utilities.IsTypeUsableAsPropertyName(nameType))
        {
            var text = Utilities.GetPropertyNameFromType(nameType);
            return GetTypeOfPropertyOfType(parentType, text);
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:32003-32007 — <c>getContextualTypeForStaticPropertyDeclaration</c>.</summary>
    internal IType? GetContextualTypeForStaticPropertyDeclaration(PropertyDeclaration declaration, ContextFlags? contextFlags)
    {
        var parentType = Utilities.IsExpression(declaration.Parent!) ? GetContextualType((Expression)declaration.Parent!, contextFlags) : null;
        if (parentType is null) return null;
        return GetTypeOfPropertyOfContextualType(parentType, GetSymbolOfDeclaration(declaration).EscapedName!);
    }

    // In a variable, parameter or property declaration with a type annotation,
    //   the contextual type of an initializer expression is the type of the variable, parameter or property.
    // Otherwise, in a parameter declaration of a contextually typed function expression,
    //   the contextual type of an initializer expression is the contextual type of the parameter.
    // Otherwise, in a variable or parameter declaration with a binding pattern name,
    //   the contextual type of an initializer expression is the type implied by the binding pattern.
    // Otherwise, in a binding pattern inside a variable or parameter declaration,
    //   the contextual type of an initializer expression is the type annotation of the containing declaration, if present.
    /// <summary>ported from: src/compiler/checker.ts:32017-32029 — <c>getContextualTypeForInitializerExpression</c>.</summary>
    internal IType? GetContextualTypeForInitializerExpression(Expression node, ContextFlags? contextFlags)
    {
        var declaration = node.Parent!;
        Expression? initializer = declaration switch
        {
            VariableDeclaration d => d.Initializer,
            ParameterDeclaration d => d.Initializer,
            PropertyDeclaration d => d.Initializer,
            PropertySignature d => d.Initializer,
            BindingElement d => d.Initializer,
            _ => null,
        };
        if (initializer is not null && ReferenceEquals(node, initializer))
        {
            var result = GetContextualTypeForVariableLikeDeclaration(declaration, contextFlags);
            if (result is not null)
            {
                return result;
            }
            var name = Utilities.GetNameOfDeclaration(declaration);
            var nameIsNonEmptyBindingPattern = name switch
            {
                ArrayBindingPattern arrayPattern => arrayPattern.Elements!.Count > 0,
                ObjectBindingPattern objectPattern => objectPattern.Elements!.Count > 0,
                _ => false,
            };
            if ((contextFlags.GetValueOrDefault() & ContextFlags.SkipBindingPatterns) == 0 && nameIsNonEmptyBindingPattern)
            {
                return GetTypeFromBindingPattern((Node)name!, /*includePatternInType*/ true, /*reportErrors*/ false);
            }
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:32031-32060 — <c>getContextualTypeForReturnExpression</c>.</summary>
    internal IType? GetContextualTypeForReturnExpression(Expression node, ContextFlags? contextFlags)
    {
        var func = Utilities.GetContainingFunction(node);
        if (func is not null)
        {
            var funcNode = (Node)func;
            var contextualReturnType = GetContextualReturnType(funcNode, contextFlags);
            if (contextualReturnType is not null)
            {
                var functionFlags = Utilities.GetFunctionFlags(funcNode);
                if ((functionFlags & FunctionFlags.Generator) != 0)
                {
                    // Generator or AsyncGenerator function
                    var isAsyncGenerator = (functionFlags & FunctionFlags.Async) != 0;
                    if ((contextualReturnType.Flags & TypeFlags.Union) != 0)
                    {
                        contextualReturnType = FilterType(contextualReturnType, type => GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Return, type, isAsyncGenerator) is not null);
                    }
                    var iterationReturnType = GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Return, contextualReturnType, (functionFlags & FunctionFlags.Async) != 0);
                    if (iterationReturnType is null)
                    {
                        return null;
                    }
                    contextualReturnType = iterationReturnType;
                    // falls through to unwrap Promise for AsyncGenerators
                }

                if ((functionFlags & FunctionFlags.Async) != 0)
                {
                    // Async function or AsyncGenerator function
                    // Get the awaited type without the `Awaited<T>` alias
                    var contextualAwaitedType = MapType(contextualReturnType, t => GetAwaitedTypeNoAlias(t));
                    return contextualAwaitedType is not null ? GetUnionType([contextualAwaitedType, CreatePromiseLikeType(contextualAwaitedType)]) : null;
                }

                return contextualReturnType; // Regular function or Generator function
            }
        }
        return null;
    }

    // In a typed function call, an argument or substitution expression is contextually typed by the type of the corresponding parameter.
    /// <summary>ported from: src/compiler/checker.ts:32160-32164 — <c>getContextualTypeForArgument</c>.</summary>
    internal IType? GetContextualTypeForArgument(Node callTarget, Expression arg)
    {
        var args = GetEffectiveCallArguments(callTarget);
        var argIndex = args.IndexOf(arg); // -1 for e.g. the expression of a CallExpression, or the tag of a TaggedTemplateExpression
        return argIndex == -1 ? null : GetContextualTypeForArgumentAtIndex(callTarget, argIndex);
    }

    /// <summary>ported from: src/compiler/checker.ts:32166-32184 — <c>getContextualTypeForArgumentAtIndex</c>.</summary>
    internal IType GetContextualTypeForArgumentAtIndex(Node callTarget, int argIndex)
    {
        if (Utilities.IsImportCall(callTarget))
        {
            return argIndex == 0 ? _stringType :
                argIndex == 1 ? GetGlobalImportCallOptionsType(/*reportErrors*/ false) :
                _anyType;
        }

        // If we're already in the process of resolving the given signature, don't resolve again as
        // that could cause infinite recursion. Instead, return anySignature.
        var signature = ReferenceEquals(GetNodeLinks(callTarget).ResolvedSignature, _resolvingSignature) ? _resolvingSignature : GetResolvedSignature(callTarget);

        if (Utilities.IsJsxOpeningLikeElement(callTarget) && argIndex == 0)
        {
            return GetEffectiveFirstArgumentForJsxSignature(signature, callTarget);
        }
        var restIndex = signature.Parameters!.Count - 1;
        return SignatureHasRestParameter(signature) && argIndex >= restIndex
            ? GetIndexedAccessType(GetTypeOfSymbol(signature.Parameters![restIndex]), GetNumberLiteralType(argIndex - restIndex), AccessFlags.Contextual)
            : GetTypeAtPosition(signature, argIndex);
    }

    /// <summary>ported from: src/compiler/checker.ts:32186-32189 — <c>getContextualTypeForDecorator</c>.</summary>
    internal IType? GetContextualTypeForDecorator(Decorator decorator)
    {
        var signature = GetDecoratorCallSignature(decorator);
        return signature is not null ? GetOrCreateTypeFromSignature(signature) : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:32191-32197 — <c>getContextualTypeForSubstitutionExpression</c>.</summary>
    internal IType? GetContextualTypeForSubstitutionExpression(TemplateExpression template, Expression substitutionExpression)
    {
        if (template.Parent!.Kind == SyntaxKind.TaggedTemplateExpression)
        {
            return GetContextualTypeForArgument(template.Parent!, substitutionExpression);
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:32199-32224 — <c>getContextualTypeForBinaryOperand</c>.</summary>
    internal IType? GetContextualTypeForBinaryOperand(Expression node, ContextFlags? contextFlags)
    {
        var binaryExpression = (BinaryExpression)node.Parent!;
        var left = binaryExpression.Left;
        var right = binaryExpression.Right;
        switch (binaryExpression.OperatorToken!.Kind)
        {
            case SyntaxKind.EqualsToken:
            case SyntaxKind.AmpersandAmpersandEqualsToken:
            case SyntaxKind.BarBarEqualsToken:
            case SyntaxKind.QuestionQuestionEqualsToken:
                return ReferenceEquals(node, right) ? GetContextualTypeForAssignmentDeclaration(binaryExpression) : null;
            case SyntaxKind.BarBarToken:
            case SyntaxKind.QuestionQuestionToken:
                // When an || expression has a contextual type, the operands are contextually typed by that type, except
                // when that type originates in a binding pattern, the right operand is contextually typed by the type of
                // the left operand. When an || expression has no contextual type, the right operand is contextually typed
                // by the type of the left operand, except for the special case of Javascript declarations of the form
                // `namespace.prop = namespace.prop || {}`.
                var type = GetContextualType(binaryExpression, contextFlags);
                return ReferenceEquals(node, right) && (type is not null && type.Pattern is not null || type is null && !Js.IsTruthy(Utilities.IsDefaultedExpandoInitializer(binaryExpression)))
                    ? GetTypeOfExpression(left!)
                    : type;
            case SyntaxKind.AmpersandAmpersandToken:
            case SyntaxKind.CommaToken:
                return ReferenceEquals(node, right) ? GetContextualType(binaryExpression, contextFlags) : null;
            default:
                return null;
        }
    }

    /// <summary>
    /// ported from: src/compiler/checker.ts:32230-32255 — <c>getSymbolForExpression</c>.
    /// </summary>
    /// <remarks>
    /// Try to find a resolved symbol for an expression without also resolving its type, as
    /// getSymbolAtLocation would (as that could be reentrant into contextual typing)
    /// </remarks>
    internal ISymbol? GetSymbolForExpression(Expression e)
    {
        if (Utilities.CanHaveSymbol(e) && ((IDeclaration)e).Symbol is not null)
        {
            return ((IDeclaration)e).Symbol;
        }
        if (NodeTests.IsIdentifier(e))
        {
            return GetResolvedSymbol((Identifier)e);
        }
        if (NodeTests.IsPropertyAccessExpression(e))
        {
            var access = (PropertyAccessExpression)e;
            var name = access.Name!;
            var lhsType = GetTypeOfExpression(access.Expression!);
            return NodeTests.IsPrivateIdentifier(name)
                ? TryGetPrivateIdentifierPropertyOfType(lhsType, (PrivateIdentifier)name)
                : GetPropertyOfType(lhsType, Utilities.GetEscapedTextOfIdentifierOrLiteral(name));
        }
        if (NodeTests.IsElementAccessExpression(e))
        {
            var access = (ElementAccessExpression)e;
            var propType = CheckExpressionCached(access.ArgumentExpression!);
            if (!Utilities.IsTypeUsableAsPropertyName(propType))
            {
                return null;
            }
            var lhsType = GetTypeOfExpression(access.Expression!);
            return GetPropertyOfType(lhsType, Utilities.GetPropertyNameFromType(propType));
        }
        return null;

        ISymbol? TryGetPrivateIdentifierPropertyOfType(IType type, PrivateIdentifier id)
        {
            var lexicallyScopedSymbol = LookupSymbolForPrivateIdentifierDeclaration(id.EscapedText!, id);
            return lexicallyScopedSymbol is not null ? GetPrivateIdentifierPropertyOfType(type, lexicallyScopedSymbol) : null;
        }
    }

    // In an assignment expression, the right operand is contextually typed by the type of the left operand.
    // Don't do this for assignment declarations unless there is a type tag on the assignment, to avoid circularity from checking the right operand.
    /// <summary>ported from: src/compiler/checker.ts:32259-32330 — <c>getContextualTypeForAssignmentDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>case</c> label is a <c>const</c> declaration (<c>const kind = getAssignmentDeclarationKind(...)</c>
    /// guards the switch, and the <c>None</c>/<c>ThisProperty</c> arm declares <c>lhsSymbol</c>/<c>decl</c>); C# scopes
    /// declarations per <c>switch</c> section, so each declaration is repeated in the section that reads it.
    /// </remarks>
    internal IType? GetContextualTypeForAssignmentDeclaration(BinaryExpression binaryExpression)
    {
        var kind = Utilities.GetAssignmentDeclarationKind(binaryExpression);
        switch (kind)
        {
            case AssignmentDeclarationKind.None:
            case AssignmentDeclarationKind.ThisProperty:
                var lhsSymbol = GetSymbolForExpression(binaryExpression.Left!);
                var decl = lhsSymbol?.ValueDeclaration;
                // Unannotated, uninitialized property declarations have a type implied by their usage in the constructor.
                // We avoid calling back into `getTypeOfExpression` and reentering contextual typing to avoid a bogus circularity error in that case.
                if (decl is not null && (NodeTests.IsPropertyDeclaration((Node)decl) || NodeTests.IsPropertySignature((Node)decl)))
                {
                    var overallAnnotation = Utilities.GetEffectiveTypeAnnotationNode((Node)decl);
                    if (overallAnnotation is not null)
                    {
                        var annotatedType = InstantiateType(GetTypeFromTypeNode(overallAnnotation), (TypeMapper?)GetSymbolLinks(lhsSymbol!).Mapper);
                        if (annotatedType is not null)
                        {
                            return annotatedType;
                        }
                    }
                    if (NodeTests.IsPropertyDeclaration((Node)decl))
                    {
                        return ((PropertyDeclaration)decl).Initializer is not null ? GetTypeOfExpression(binaryExpression.Left!) : null;
                    }
                    return null;
                }
                if (kind == AssignmentDeclarationKind.None)
                {
                    return GetTypeOfExpression(binaryExpression.Left!);
                }
                return GetContextualTypeForThisPropertyAssignment(binaryExpression);
            case AssignmentDeclarationKind.Property:
                if (IsPossiblyAliasedThisProperty(binaryExpression, kind))
                {
                    return GetContextualTypeForThisPropertyAssignment(binaryExpression);
                }
                // If `binaryExpression.left` was assigned a symbol, then this is a new declaration; otherwise it is an assignment to an existing declaration.
                // See `bindStaticPropertyAssignment` in `binder.ts`.
                else if (!Utilities.CanHaveSymbol(binaryExpression.Left!) || ((IDeclaration)binaryExpression.Left!).Symbol is null)
                {
                    return GetTypeOfExpression(binaryExpression.Left!);
                }
                else
                {
                    var declaration = ((IDeclaration)binaryExpression.Left!).Symbol!.ValueDeclaration;
                    if (declaration is null)
                    {
                        return null;
                    }
                    var lhs = (PropertyAccessExpression)binaryExpression.Left!;
                    var annotation = Utilities.GetEffectiveTypeAnnotationNode((Node)declaration);
                    if (annotation is not null)
                    {
                        return GetTypeFromTypeNode(annotation);
                    }
                    else if (NodeTests.IsIdentifier(lhs.Expression!))
                    {
                        var id = (Identifier)lhs.Expression!;
                        var parentSymbol = ResolveName(id, id.EscapedText!, SymbolFlags.Value, /*nameNotFoundMessage*/ null, /*isUse*/ true, /*excludeGlobals*/ false);
                        if (parentSymbol is not null)
                        {
                            var parentDeclaration = parentSymbol.ValueDeclaration;
                            var annotated = parentDeclaration is not null ? Utilities.GetEffectiveTypeAnnotationNode((Node)parentDeclaration) : null;
                            if (annotated is not null)
                            {
                                var nameStr = Utilities.GetElementOrPropertyAccessName(lhs);
                                if (nameStr is not null)
                                {
                                    return GetTypeOfPropertyOfContextualType(GetTypeFromTypeNode(annotated), nameStr);
                                }
                            }
                            return null;
                        }
                    }
                    return Utilities.IsInJSFile((Node)declaration) || ReferenceEquals(declaration, binaryExpression.Left) ? null : GetTypeOfExpression(binaryExpression.Left!);
                }
            case AssignmentDeclarationKind.ExportsProperty:
            case AssignmentDeclarationKind.Prototype:
            case AssignmentDeclarationKind.PrototypeProperty:
            case AssignmentDeclarationKind.ModuleExports:
                IDeclaration? valueDeclaration = null;
                if (kind != AssignmentDeclarationKind.ModuleExports)
                {
                    valueDeclaration = Utilities.CanHaveSymbol(binaryExpression.Left!) ? ((IDeclaration)binaryExpression.Left!).Symbol?.ValueDeclaration : null;
                }
                valueDeclaration ??= ((IDeclaration)binaryExpression).Symbol?.ValueDeclaration;
                var valueAnnotation = valueDeclaration is not null ? Utilities.GetEffectiveTypeAnnotationNode((Node)valueDeclaration) : null;
                return valueAnnotation is not null ? GetTypeFromTypeNode(valueAnnotation) : null;
            case AssignmentDeclarationKind.ObjectDefinePropertyValue:
            case AssignmentDeclarationKind.ObjectDefinePropertyExports:
            case AssignmentDeclarationKind.ObjectDefinePrototypeProperty:
                return Debug.Fail<IType?>("Does not apply");
            default:
                Debug.AssertNever(kind);
                return null;
        }
    }

    /// <summary>
    /// The <c>target.expression</c> read of TS's <c>AccessExpression</c> union (checker.ts:31920, 32355, 32336).
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS's union is <c>PropertyAccessExpression | ElementAccessExpression</c> plus their two chain
    /// subclasses, all four of which carry the same <c>expression</c> slot; the port's two base shapes carry it as the
    /// same-named slot and the chains derive from them, so this switch is the union read.
    /// </remarks>
    private static Expression GetAccessExpressionOperand(Node target) => target switch
    {
        PropertyAccessExpression access => access.Expression!,
        ElementAccessExpression access => access.Expression!,
        _ => Debug.Fail<Expression>("an access expression must have an expression"),
    };

    /// <summary>
    /// The <c>(node as ParenthesizedExpression | NonNullExpression | SatisfiesExpression).expression</c> read of TS's
    /// unions at checker.ts:27807, 27842 and 27866.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: the three classes share no type below <see cref="Expression"/>, so the read is this kind switch — the
    /// same treatment <c>Binder/Binder.Narrowing.cs</c>'s <c>GetInnerExpression</c> records. The default arm is a
    /// programming error, not an answer TS can reach: every caller has established the kind first.
    /// </remarks>
    private static Expression GetInnerExpression(Node node) => node switch
    {
        ParenthesizedExpression parenthesized => parenthesized.Expression!,
        NonNullExpression nonNull => nonNull.Expression!,
        _ => ((SatisfiesExpression)node).Expression!,
    };

    /// <summary>ported from: src/compiler/checker.ts:27957-27965 — <c>optionalChainContainsReference</c>'s read.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>OptionalChain</c> is <c>CallChain | PropertyAccessChain | ElementAccessChain | NonNullChain</c>
    /// (types.ts:3086-3090) and its <c>expression</c> is the one slot all four carry. The port has no union, so the
    /// four faces are spelled out here, exactly where TS's structural access made the same read.
    /// </remarks>
    private static Expression GetOptionalChainOperand(Node target) => target switch
    {
        CallExpression call => call.Expression!,
        PropertyAccessExpression access => access.Expression!,
        ElementAccessExpression access => access.Expression!,
        _ => ((NonNullExpression)target).Expression!,
    };

    /// <summary>ported from: src/compiler/checker.ts:28353-28355 — <c>getAssignedTypeOfPropertyAssignment</c>'s read.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>PropertyAssignment | ShorthandPropertyAssignment</c> both carry <c>name</c>; one C# switch
    /// states that fact without inventing a <c>NamedDeclaration</c> type (it does not exist in the port).
    /// <para>
    /// PORT-NOTE: named for the object-literal member it reads, not <c>GetNamedDeclarationName</c> —
    /// <c>Utilities</c> already has a member by that name (the port's stand-in for TS's duck-typed
    /// <c>(node as NamedDeclaration).name</c> read at utilitiesPublic.ts:946-948) and §15.15's "one name, one owner, one
    /// home" forbids a second, differently-shaped member of the same name.
    /// </para>
    /// </remarks>
    private static Node GetObjectLiteralMemberName(Node node) => node switch
    {
        PropertyAssignment property => property.Name!,
        ShorthandPropertyAssignment shorthand => shorthand.Name!,
        _ => Debug.Fail<Node>("an object literal member with a name was expected"),
    };

    /// <summary>ported from: src/compiler/checker.ts:28841-28846 — <c>reportFlowControlError</c>'s <c>block.statements</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>StatementContainer</c> is a structural type; the port states the three implementors
    /// (<c>Block</c>, <c>ModuleBlock</c>, <c>SourceFile</c>) it is reached through, all of which declare
    /// <c>statements</c> as the same slot.
    /// </remarks>
    /// <summary>ported from: src/compiler/checker.ts:30763-30811 — <c>markDecoratorAliasReferenced</c>'s <c>node.modifiers</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>HasDecorators.modifiers</c> is a structural slot on the seven decorateable kinds; the port
    /// declares <c>modifiers</c> per class, so the union is stated here as the same switch the reference's implicit
    /// narrowing performed.
    /// </remarks>
    private static NodeArray<Node>? GetModifiersOfNode(Node node) => node switch
    {
        ClassDeclaration declaration => declaration.Modifiers,
        ClassExpression expression => expression.Modifiers,
        PropertyDeclaration declaration => declaration.Modifiers,
        MethodDeclaration declaration => declaration.Modifiers,
        GetAccessorDeclaration declaration => declaration.Modifiers,
        SetAccessorDeclaration declaration => declaration.Modifiers,
        ParameterDeclaration declaration => declaration.Modifiers,
        _ => null,
    };

    /// <summary>ported from: src/compiler/checker.ts:28841-28846 — <c>reportFlowControlError</c>'s <c>block.statements</c>.</summary>
    private static NodeArray<IStatement> GetStatementsOfBlock(Node block) => block switch
    {
        SourceFile file => file.Statements!,
        ModuleBlock moduleBlock => moduleBlock.Statements!,
        _ => ((Block)block).Statements!,
    };

    /// <summary>ported from: src/compiler/checker.ts:32332-32342 — <c>isPossiblyAliasedThisProperty</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's default parameter value (<c>kind = getAssignmentDeclarationKind(declaration)</c>) is a
    /// call, which C# default arguments cannot express, so the parameter is nullable and filled in on entry.
    /// </remarks>
    internal bool IsPossiblyAliasedThisProperty(BinaryExpression declaration, AssignmentDeclarationKind? kind = null)
    {
        kind ??= Utilities.GetAssignmentDeclarationKind(declaration);
        if (kind == AssignmentDeclarationKind.ThisProperty)
        {
            return true;
        }
        if (!Utilities.IsInJSFile(declaration) || kind != AssignmentDeclarationKind.Property || !NodeTests.IsIdentifier(GetAccessExpressionOperand(declaration.Left!)))
        {
            return false;
        }
        var name = ((Identifier)GetAccessExpressionOperand(declaration.Left!)).EscapedText!;
        var symbol = ResolveName(declaration.Left, name, SymbolFlags.Value, /*nameNotFoundMessage*/ null, /*isUse*/ true, /*excludeGlobals*/ true);
        return Utilities.IsThisInitializedDeclaration(symbol?.ValueDeclaration is not null ? (Node)symbol.ValueDeclaration : null);
    }

    /// <summary>ported from: src/compiler/checker.ts:32344-32362 — <c>getContextualTypeForThisPropertyAssignment</c>.</summary>
    internal IType? GetContextualTypeForThisPropertyAssignment(BinaryExpression binaryExpression)
    {
        if (((IDeclaration)binaryExpression).Symbol is null) return GetTypeOfExpression(binaryExpression.Left!);
        if (((IDeclaration)binaryExpression).Symbol!.ValueDeclaration is not null)
        {
            var annotated = Utilities.GetEffectiveTypeAnnotationNode((Node)((IDeclaration)binaryExpression).Symbol!.ValueDeclaration!);
            if (annotated is not null)
            {
                var type = GetTypeFromTypeNode(annotated);
                if (type is not null)
                {
                    return type;
                }
            }
        }
        var thisAccess = binaryExpression.Left!;
        if (!Utilities.IsObjectLiteralMethod(Utilities.GetThisContainer(GetAccessExpressionOperand(thisAccess), /*includeArrowFunctions*/ false, /*includeClassComputedPropertyName*/ false)))
        {
            return null;
        }
        var thisType = CheckThisExpression(GetAccessExpressionOperand(thisAccess));
        var nameStr = Utilities.GetElementOrPropertyAccessName(thisAccess);
        return nameStr is not null && GetTypeOfPropertyOfContextualType(thisType, nameStr) is { } propertyType ? propertyType : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:32364-32366 — <c>isCircularMappedProperty</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads <c>(symbol as MappedSymbol).links.type</c>. Every mapped symbol is transient and its links
    /// are the symbol's own, so <c>GetSymbolLinks</c> answers the same slot (the invariant <c>CreateSymbol</c>
    /// establishes for transient symbols).
    /// </remarks>
    internal bool IsCircularMappedProperty(ISymbol symbol)
        => (GetCheckFlags(symbol) & CheckFlags.Mapped) != 0 && GetSymbolLinks(symbol).Type is null && FindResolutionCycleStartIndex(symbol, TypeSystemPropertyName.Type) >= 0;

    /// <summary>ported from: src/compiler/checker.ts:32368-32379 — <c>isExcludedMappedPropertyName</c>.</summary>
    internal bool IsExcludedMappedPropertyName(IType constraint, IType propertyNameType)
    {
        if ((constraint.Flags & TypeFlags.Conditional) != 0)
        {
            var type = (IConditionalType)constraint;
            return (GetReducedType(GetTrueTypeFromConditionalType(type)).Flags & TypeFlags.Never) != 0
                && ReferenceEquals(GetActualTypeVariable(GetFalseTypeFromConditionalType(type)), GetActualTypeVariable(type.CheckType!))
                && IsTypeAssignableTo(propertyNameType, type.ExtendsType!);
        }
        if ((constraint.Flags & TypeFlags.Intersection) != 0)
        {
            return Core.Some(((IIntersectionType)constraint).Types, t => IsExcludedMappedPropertyName(t, propertyNameType));
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:32381-32428 — <c>getTypeOfPropertyOfContextualType</c>.</summary>
    internal IType? GetTypeOfPropertyOfContextualType(IType type, string name, IType? nameType = null)
        => MapType(type, t =>
        {
            if ((t.Flags & TypeFlags.Intersection) != 0)
            {
                List<IType>? types = null;
                List<IType>? indexInfoCandidates = null;
                var ignoreIndexInfos = false;
                foreach (var constituentType in ((IIntersectionType)t).Types!)
                {
                    if ((constituentType.Flags & TypeFlags.Object) == 0)
                    {
                        continue;
                    }
                    if (IsGenericMappedType(constituentType) && GetMappedTypeNameTypeKind((IMappedType)constituentType) != MappedTypeNameTypeKind.Remapping)
                    {
                        var substitutedType = GetIndexedMappedTypeSubstitutedTypeOfContextualType((IMappedType)constituentType, name, nameType);
                        types = AppendContextualPropertyTypeConstituent(types, substitutedType);
                        continue;
                    }
                    var propertyType = GetTypeOfConcretePropertyOfContextualType(constituentType, name);
                    if (propertyType is null)
                    {
                        if (!ignoreIndexInfos)
                        {
                            indexInfoCandidates = (List<IType>?)Core.Append(indexInfoCandidates, constituentType);
                        }
                        continue;
                    }
                    ignoreIndexInfos = true;
                    indexInfoCandidates = null;
                    types = AppendContextualPropertyTypeConstituent(types, propertyType);
                }
                if (indexInfoCandidates is not null)
                {
                    foreach (var candidate in indexInfoCandidates)
                    {
                        var indexInfoType = GetTypeFromIndexInfosOfContextualType(candidate, name, nameType);
                        types = AppendContextualPropertyTypeConstituent(types, indexInfoType);
                    }
                }
                if (types is null)
                {
                    return null;
                }
                if (types.Count == 1)
                {
                    return types[0];
                }
                return GetIntersectionType(types);
            }
            if ((t.Flags & TypeFlags.Object) == 0)
            {
                return null;
            }
            return IsGenericMappedType(t) && GetMappedTypeNameTypeKind((IMappedType)t) != MappedTypeNameTypeKind.Remapping
                ? GetIndexedMappedTypeSubstitutedTypeOfContextualType((IMappedType)t, name, nameType)
                : GetTypeOfConcretePropertyOfContextualType(t, name) ?? GetTypeFromIndexInfosOfContextualType(t, name, nameType);
        }, /*noReductions*/ true);

    /// <summary>ported from: src/compiler/checker.ts:32430-32434 — <c>appendContextualPropertyTypeConstituent</c>.</summary>
    internal List<IType>? AppendContextualPropertyTypeConstituent(List<IType>? types, IType? type)
        // any doesn't provide any contextual information but could spoil the overall result by nullifying contextual information provided by other intersection constituents
        // so it gets replaced with `unknown` as `T & unknown` is just `T` and all types computed based on the contextual information provided by other constituens are still assignable to any
        => type is not null ? (List<IType>?)Core.Append(types, (type.Flags & TypeFlags.Any) != 0 ? _unknownType : type) : types;

    /// <summary>ported from: src/compiler/checker.ts:32436-32448 — <c>getIndexedMappedTypeSubstitutedTypeOfContextualType</c>.</summary>
    internal IType? GetIndexedMappedTypeSubstitutedTypeOfContextualType(IMappedType type, string name, IType? nameType)
    {
        var propertyNameType = nameType ?? GetStringLiteralType(Utilities.UnescapeLeadingUnderscores(name));
        var constraint = GetConstraintTypeFromMappedType(type);
        // special case for conditional types pretending to be negated types
        if (type.NameType is not null && IsExcludedMappedPropertyName(type.NameType, propertyNameType) || IsExcludedMappedPropertyName(constraint, propertyNameType))
        {
            return null;
        }
        var constraintOfConstraint = GetBaseConstraintOfType(constraint) ?? constraint;
        if (!IsTypeAssignableTo(propertyNameType, constraintOfConstraint))
        {
            return null;
        }
        return SubstituteIndexedMappedType(type, propertyNameType);
    }

    /// <summary>ported from: src/compiler/checker.ts:32450-32456 — <c>getTypeOfConcretePropertyOfContextualType</c>.</summary>
    internal IType? GetTypeOfConcretePropertyOfContextualType(IType type, string name)
    {
        var prop = GetPropertyOfType(type, name);
        if (prop is null || IsCircularMappedProperty(prop))
        {
            return null;
        }
        return RemoveMissingType(GetTypeOfSymbol(prop), (prop.Flags & SymbolFlags.Optional) != 0);
    }

    /// <summary>ported from: src/compiler/checker.ts:32458-32466 — <c>getTypeFromIndexInfosOfContextualType</c>.</summary>
    internal IType? GetTypeFromIndexInfosOfContextualType(IType type, string name, IType? nameType)
    {
        if (IsTupleType(type) && Utilities.IsNumericLiteralName(name) && Js.ToNumber(name) >= 0)
        {
            var restType = GetElementTypeOfSliceOfTupleType((ITypeReference)type, ((ITupleType)((ITypeReference)type).Target!).FixedLength, /*endSkipCount*/ 0, /*writing*/ false, /*noReductions*/ true);
            if (restType is not null)
            {
                return restType;
            }
        }
        return FindApplicableIndexInfo(GetIndexInfosOfStructuredType(type), nameType ?? GetStringLiteralType(Utilities.UnescapeLeadingUnderscores(name)))?.Type;
    }

    // In an object literal contextually typed by a type T, the contextual type of a property assignment is the type of
    // the matching property in T, if one exists. Otherwise, it is the type of the numeric index signature in T, if one
    // exists. Otherwise, it is the type of the string index signature in T, if one exists.
    /// <summary>ported from: src/compiler/checker.ts:32471-32478 — <c>getContextualTypeForObjectLiteralMethod</c>.</summary>
    internal IType? GetContextualTypeForObjectLiteralMethod(MethodDeclaration node, ContextFlags? contextFlags)
    {
        Debug.Assert(Utilities.IsObjectLiteralMethod(node));
        if ((node.Flags & NodeFlags.InWithStatement) != 0)
        {
            // We cannot answer semantic questions within a with block, do not proceed any further
            return null;
        }
        return GetContextualTypeForObjectLiteralElement(node, contextFlags);
    }

    /// <summary>ported from: src/compiler/checker.ts:32480-32512 — <c>getContextualTypeForObjectLiteralElement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>ObjectLiteralElementLike</c> union is the port's <c>Node</c> face: every member read below is
    /// either on <c>Node</c> or behind a kind test the reference already performs.
    /// </remarks>
    internal IType? GetContextualTypeForObjectLiteralElement(Node element, ContextFlags? contextFlags)
    {
        var objectLiteral = (ObjectLiteralExpression)element.Parent!;
        var propertyAssignmentType = NodeTests.IsPropertyAssignment(element) && GetContextualTypeForVariableLikeDeclaration(element, contextFlags) is { } variableLikeType ? variableLikeType : null;
        if (propertyAssignmentType is not null)
        {
            return propertyAssignmentType;
        }
        var type = GetApparentTypeOfContextualType(objectLiteral, contextFlags);
        if (type is not null)
        {
            if (HasBindableName((IDeclaration)element))
            {
                // For a (non-symbol) computed property, there is no reason to look up the name
                // in the type. It will just be "__computed", which does not appear in any
                // SymbolTable.
                var symbol = GetSymbolOfDeclaration((IDeclaration)element);
                return GetTypeOfPropertyOfContextualType(type, symbol.EscapedName!, GetSymbolLinks(symbol).NameType);
            }
            if (Utilities.HasDynamicName(element))
            {
                var elementNameForComputed = Utilities.GetNameOfDeclaration(element);
                if (elementNameForComputed is Node nameNode && NodeTests.IsComputedPropertyName(nameNode))
                {
                    var exprType = CheckExpression(((ComputedPropertyName)nameNode).Expression!);
                    var propType = Utilities.IsTypeUsableAsPropertyName(exprType) ? GetTypeOfPropertyOfContextualType(type, Utilities.GetPropertyNameFromType(exprType)) : null;
                    if (propType is not null)
                    {
                        return propType;
                    }
                }
            }
            var elementName = Utilities.GetNameOfDeclaration(element);
            if (elementName is Node elementNameNode)
            {
                var nameType = GetLiteralTypeFromPropertyName(elementNameNode);
                // We avoid calling getApplicableIndexInfo here because it performs potentially expensive intersection reduction.
                return MapType(type, t => FindApplicableIndexInfo(GetIndexInfosOfStructuredType(t), nameType)?.Type, /*noReductions*/ true);
            }
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:32514-32523 — <c>getSpreadIndices</c>.</summary>
    internal (int? first, int? last) GetSpreadIndices(IList<Expression> elements)
    {
        int? first = null;
        int? last = null;
        for (var i = 0; i < elements.Count; i++)
        {
            if (NodeTests.IsSpreadElement(elements[i]))
            {
                first ??= i;
                last = i;
            }
        }
        return (first, last);
    }

    /// <summary>ported from: src/compiler/checker.ts:32525-32550 — <c>getContextualTypeForElementExpression</c>.</summary>
    internal IType? GetContextualTypeForElementExpression(IType? type, int index, int? length = null, int? firstSpreadIndex = null, int? lastSpreadIndex = null)
        => type is not null ? MapType(type, t =>
        {
            if (IsTupleType(t))
            {
                var tupleTarget = (ITupleType)((ITypeReference)t).Target!;
                // If index is before any spread element and within the fixed part of the contextual tuple type, return
                // the type of the contextual tuple element.
                if ((firstSpreadIndex is null || index < firstSpreadIndex) && index < tupleTarget.FixedLength)
                {
                    return RemoveMissingType(GetTypeArguments((ITypeReference)t)[index], (tupleTarget.ElementFlags![index] & ElementFlags.Optional) != 0);
                }
                // When the length is known and the index is after all spread elements we compute the offset from the element
                // to the end and the number of ending fixed elements in the contextual tuple type.
                var offset = length is not null && (lastSpreadIndex is null || index > lastSpreadIndex) ? length.Value - index : 0;
                var fixedEndLength = offset > 0 && (tupleTarget.CombinedFlags & ElementFlags.Variable) != 0 ? GetEndElementCount(tupleTarget, ElementFlags.Fixed) : 0;
                // If the offset is within the ending fixed part of the contextual tuple type, return the type of the contextual
                // tuple element.
                if (offset > 0 && offset <= fixedEndLength)
                {
                    return GetTypeArguments((ITypeReference)t)[GetTypeReferenceArity((ITypeReference)t) - offset];
                }
                // Return a union of the possible contextual element types with no subtype reduction.
                return GetElementTypeOfSliceOfTupleType(
                    (ITypeReference)t,
                    firstSpreadIndex is null ? tupleTarget.FixedLength : Math.Min(tupleTarget.FixedLength, firstSpreadIndex.Value),
                    length is null || lastSpreadIndex is null ? fixedEndLength : Math.Min(fixedEndLength, length.Value - lastSpreadIndex.Value),
                    /*writing*/ false,
                    /*noReductions*/ true);
            }
            // If element index is known and a contextual property with that name exists, return it. Otherwise return the
            // iterated or element type of the contextual type.
            return (firstSpreadIndex is null || index < firstSpreadIndex) && GetTypeOfPropertyOfContextualType(t, "" + index) is { } propertyType
                ? propertyType
                : GetIteratedTypeOrElementType(IterationUse.Element, t, _undefinedType, /*errorNode*/ null, /*checkAssignability*/ false);
        }, /*noReductions*/ true) : null;

    // In a contextually typed conditional expression, the true/false expressions are contextually typed by the same type.
    /// <summary>ported from: src/compiler/checker.ts:32553-32556 — <c>getContextualTypeForConditionalOperand</c>.</summary>
    internal IType? GetContextualTypeForConditionalOperand(Expression node, ContextFlags? contextFlags)
    {
        var conditional = (ConditionalExpression)node.Parent!;
        return ReferenceEquals(node, conditional.WhenTrue) || ReferenceEquals(node, conditional.WhenFalse) ? GetContextualType(conditional, contextFlags) : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:32558-32576 — <c>getContextualTypeForChildJsxExpression</c>.</summary>
    internal IType? GetContextualTypeForChildJsxExpression(JsxElement node, Node child, ContextFlags? contextFlags)
    {
        var attributesType = GetApparentTypeOfContextualType(node.OpeningElement!.Attributes!, contextFlags);
        // JSX expression is in children of JSX Element, we will look for an "children" attribute (we get the name from JSX.ElementAttributesProperty)
        var jsxChildrenPropertyName = GetJsxElementChildrenPropertyName(GetJsxNamespaceAt(node));
        if (!(attributesType is not null && !IsTypeAny(attributesType) && jsxChildrenPropertyName is not null && jsxChildrenPropertyName != ""))
        {
            return null;
        }
        var realChildren = Utilities.GetSemanticJsxChildren(node.Children!);
        var childIndex = realChildren.IndexOf(child);
        var childFieldType = GetTypeOfPropertyOfContextualType(attributesType, jsxChildrenPropertyName);
        return childFieldType is null ? null : (realChildren.Count == 1 ? childFieldType : MapType(childFieldType, t =>
        {
            if (IsArrayLikeType(t))
            {
                return GetIndexedAccessType(t, GetNumberLiteralType(childIndex));
            }
            else
            {
                return t;
            }
        }, /*noReductions*/ true));
    }

    /// <summary>ported from: src/compiler/checker.ts:32578-32585 — <c>getContextualTypeForJsxExpression</c>.</summary>
    internal IType? GetContextualTypeForJsxExpression(JsxExpression node, ContextFlags? contextFlags)
    {
        var exprParent = node.Parent!;
        return Utilities.IsJsxAttributeLike(exprParent)
            ? GetContextualType(node, contextFlags)
            : NodeTests.IsJsxElement(exprParent)
                ? GetContextualTypeForChildJsxExpression((JsxElement)exprParent, node, contextFlags)
                : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:32587-32601 — <c>getContextualTypeForJsxAttribute</c>.</summary>
    internal IType? GetContextualTypeForJsxAttribute(Node attribute, ContextFlags? contextFlags)
    {
        // When we trying to resolve JsxOpeningLikeElement as a stateless function element, we will already give its attributes a contextual type
        // which is a type of the parameter of the signature we are trying out.
        // If there is no contextual type (e.g. we are trying to resolve stateful component), get attributes type from resolving element's tagName
        if (NodeTests.IsJsxAttribute(attribute))
        {
            var attributesType = GetApparentTypeOfContextualType(attribute.Parent!, contextFlags);
            if (attributesType is null || IsTypeAny(attributesType))
            {
                return null;
            }
            return GetTypeOfPropertyOfContextualType(attributesType, Utilities.GetEscapedTextOfJsxAttributeName((Node)Utilities.GetNameOfDeclaration(attribute)!));
        }
        else
        {
            return GetContextualType((Expression)attribute.Parent!, contextFlags);
        }
    }

    // Return true if the given expression is possibly a discriminant value. We limit the kinds of
    // expressions we check to those that don't depend on their contextual type in order not to cause
    // recursive (and possibly infinite) invocations of getContextualType.
    /// <summary>ported from: src/compiler/checker.ts:32606-32626 — <c>isPossiblyDiscriminantValue</c>.</summary>
    internal bool IsPossiblyDiscriminantValue(Expression node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.StringLiteral:
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.TemplateExpression:
            case SyntaxKind.TrueKeyword:
            case SyntaxKind.FalseKeyword:
            case SyntaxKind.NullKeyword:
            case SyntaxKind.Identifier:
            case SyntaxKind.UndefinedKeyword:
                return true;
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ParenthesizedExpression:
                var expression = node switch
                {
                    PropertyAccessExpression access => access.Expression!,
                    ParenthesizedExpression parenthesized => parenthesized.Expression!,
                    _ => Debug.Fail<Expression>("not a discriminant expression kind"),
                };
                return IsPossiblyDiscriminantValue(expression);
            case SyntaxKind.JsxExpression:
                var jsxExpression = ((JsxExpression)node).Expression;
                return jsxExpression is null || IsPossiblyDiscriminantValue(jsxExpression);
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:32628-32658 — <c>discriminateContextualTypeByObjectMembers</c>.</summary>
    internal IType DiscriminateContextualTypeByObjectMembers(ObjectLiteralExpression node, IUnionType contextualType)
    {
        var key = $"D{Utilities.GetNodeId(node)},{GetTypeId(contextualType)}";
        return GetCachedType(key) ?? SetCachedType(
            key,
            GetMatchingUnionConstituentForObjectLiteral(contextualType, node) ?? DiscriminateTypeByDiscriminableItems(
                contextualType,
                Core.Concatenate(
                    Core.Map(
                        Core.Filter(node.Properties, p =>
                        {
                            if (((IDeclaration)p).Symbol is null)
                            {
                                return false;
                            }
                            if (p.Kind == SyntaxKind.PropertyAssignment)
                            {
                                return IsPossiblyDiscriminantValue(((PropertyAssignment)p).Initializer!) && IsDiscriminantProperty(contextualType, ((IDeclaration)p).Symbol!.EscapedName!);
                            }
                            if (p.Kind == SyntaxKind.ShorthandPropertyAssignment)
                            {
                                return IsDiscriminantProperty(contextualType, ((IDeclaration)p).Symbol!.EscapedName!);
                            }
                            return false;
                        }),
                        (prop, _) => ((Func<IType>)(() => GetContextFreeTypeOfExpression(prop.Kind == SyntaxKind.PropertyAssignment ? ((PropertyAssignment)prop).Initializer! : (Expression)((INamedDeclaration)prop).Name!)), ((IDeclaration)prop).Symbol!.EscapedName!)),
                    Core.Map(
                        Core.Filter(GetPropertiesOfType(contextualType), s => (s.Flags & SymbolFlags.Optional) != 0 && ((IDeclaration)node).Symbol?.Members is not null && !((IDeclaration)node).Symbol!.Members!.ContainsKey(s.EscapedName!) && IsDiscriminantProperty(contextualType, s.EscapedName!)),
                        (s, _) => ((Func<IType>)(() => _undefinedType), s.EscapedName!)))!,
                (source, target) => CompareTypesAssignable(source, target)));
    }

    /// <summary>ported from: src/compiler/checker.ts:32660-32691 — <c>discriminateContextualTypeByJSXAttributes</c>.</summary>
    internal IType DiscriminateContextualTypeByJSXAttributes(JsxAttributes node, IUnionType contextualType)
    {
        var key = $"D{Utilities.GetNodeId(node)},{GetTypeId(contextualType)}";
        var cached = GetCachedType(key);
        if (cached is not null) return cached;
        var jsxChildrenPropertyName = GetJsxElementChildrenPropertyName(GetJsxNamespaceAt(node));
        return SetCachedType(
            key,
            DiscriminateTypeByDiscriminableItems(
                contextualType,
                Core.Concatenate(
                    Core.Map(
                        Core.Filter(node.Properties, p => ((IDeclaration)p).Symbol is not null && p.Kind == SyntaxKind.JsxAttribute && IsDiscriminantProperty(contextualType, ((IDeclaration)p).Symbol!.EscapedName!) && (((JsxAttribute)p).Initializer is null || IsPossiblyDiscriminantValue(((JsxAttribute)p).Initializer!))),
                        (prop, _) => ((Func<IType>)(() => ((JsxAttribute)prop).Initializer is null ? _trueType : GetContextFreeTypeOfExpression(((JsxAttribute)prop).Initializer!)), ((IDeclaration)prop).Symbol!.EscapedName!)),
                    Core.Map(
                        Core.Filter(GetPropertiesOfType(contextualType), s =>
                        {
                            if ((s.Flags & SymbolFlags.Optional) == 0 || ((IDeclaration)node).Symbol?.Members is null)
                            {
                                return false;
                            }
                            var element = node.Parent!.Parent!;
                            if (s.EscapedName == jsxChildrenPropertyName && NodeTests.IsJsxElement(element) && Utilities.GetSemanticJsxChildren(((JsxElement)element).Children!).Count != 0)
                            {
                                return false;
                            }
                            return !((IDeclaration)node).Symbol!.Members!.ContainsKey(s.EscapedName!) && IsDiscriminantProperty(contextualType, s.EscapedName!);
                        }),
                        (s, _) => ((Func<IType>)(() => _undefinedType), s.EscapedName!)))!,
                (source, target) => CompareTypesAssignable(source, target)));
    }

    // Return the contextual type for a given expression node. During overload resolution, a contextual type may temporarily
    // be "pushed" onto a node using the contextualType property.
    /// <summary>ported from: src/compiler/checker.ts:32695-32714 — <c>getApparentTypeOfContextualType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>Expression | MethodDeclaration</c>; the port states it as <c>Node</c> and
    /// keeps the reference's two kind tests (<c>isObjectLiteralMethod</c>, <c>isObjectLiteralExpression</c>,
    /// <c>isJsxAttributes</c>) where it needs them.
    /// </remarks>
    internal IType? GetApparentTypeOfContextualType(Node node, ContextFlags? contextFlags)
    {
        var contextualType = Utilities.IsObjectLiteralMethod(node)
            ? GetContextualTypeForObjectLiteralMethod((MethodDeclaration)node, contextFlags)
            : GetContextualType((Expression)node, contextFlags);
        var instantiatedType = InstantiateContextualType(contextualType, node, contextFlags);
        if (instantiatedType is not null && !(contextFlags is not null && (contextFlags.Value & ContextFlags.NoConstraints) != 0 && (instantiatedType.Flags & TypeFlags.TypeVariable) != 0))
        {
            var apparentType = MapType(
                instantiatedType,
                // When obtaining apparent type of *contextual* type we don't want to get apparent type of mapped types.
                // That would evaluate mapped types with array or tuple type constraints too eagerly
                // and thus it would prevent `getTypeOfPropertyOfContextualType` from obtaining per-position contextual type for elements of array literal expressions.
                // Apparent type of other mapped types is already the mapped type itself so we can just avoid calling `getApparentType` here for all mapped types.
                t => (GetObjectFlags(t) & ObjectFlags.Mapped) != 0 ? t : GetApparentType(t),
                /*noReductions*/ true)!;
            return (apparentType.Flags & TypeFlags.Union) != 0 && NodeTests.IsObjectLiteralExpression(node) ? DiscriminateContextualTypeByObjectMembers((ObjectLiteralExpression)node, (IUnionType)apparentType) :
                (apparentType.Flags & TypeFlags.Union) != 0 && NodeTests.IsJsxAttributes(node) ? DiscriminateContextualTypeByJSXAttributes((JsxAttributes)node, (IUnionType)apparentType) :
                apparentType;
        }
        return null;
    }

    // If the given contextual type contains instantiable types and if a mapper representing
    // return type inferences is available, instantiate those types using that mapper.
    /// <summary>ported from: src/compiler/checker.ts:32718-32746 — <c>instantiateContextualType</c>.</summary>
    internal IType? InstantiateContextualType(IType? contextualType, Node node, ContextFlags? contextFlags)
    {
        if (contextualType is not null && MaybeTypeOfKind(contextualType, TypeFlags.Instantiable))
        {
            var inferenceContext = GetInferenceContext(node);
            // If no inferences have been made, and none of the type parameters for which we are inferring
            // specify default types, nothing is gained from instantiating as type parameters would just be
            // replaced with their constraints similar to the apparent type.
            if (inferenceContext is not null && (contextFlags.GetValueOrDefault() & ContextFlags.Signature) != 0 && Core.Some(inferenceContext.Inferences, HasInferenceCandidatesOrDefault))
            {
                // For contextual signatures we incorporate all inferences made so far, e.g. from return
                // types as well as arguments to the left in a function call.
                var type = InstantiateInstantiableTypes(contextualType!, (TypeMapper)inferenceContext.NonFixingMapper!);
                if ((type.Flags & TypeFlags.AnyOrUnknown) == 0)
                {
                    return type;
                }
            }
            if (inferenceContext?.ReturnMapper is not null)
            {
                // For other purposes (e.g. determining whether to produce literal types) we only
                // incorporate inferences made from the return type in a function call. We remove
                // the 'boolean' type from the contextual type such that contextually typed boolean
                // literals actually end up widening to 'boolean' (see #48363).
                var type = InstantiateInstantiableTypes(contextualType!, (TypeMapper)inferenceContext.ReturnMapper!);
                if ((type.Flags & TypeFlags.AnyOrUnknown) == 0)
                {
                    return (type.Flags & TypeFlags.Union) != 0 && ContainsType(((IUnionType)type).Types!, _regularFalseType) && ContainsType(((IUnionType)type).Types!, _regularTrueType)
                        ? FilterType(type, t => !ReferenceEquals(t, _regularFalseType) && !ReferenceEquals(t, _regularTrueType))
                        : type;
                }
            }
        }
        return contextualType;
    }

    // This function is similar to instantiateType, except that (a) it only instantiates types that
    // are classified as instantiable (i.e. it doesn't instantiate object types), and (b) it performs
    // no reductions on instantiated union types.
    /// <summary>ported from: src/compiler/checker.ts:32751-32762 — <c>instantiateInstantiableTypes</c>.</summary>
    internal IType InstantiateInstantiableTypes(IType type, TypeMapper mapper)
    {
        if ((type.Flags & TypeFlags.Instantiable) != 0)
        {
            return InstantiateType(type, mapper)!;
        }
        if ((type.Flags & TypeFlags.Union) != 0)
        {
            return GetUnionType(Core.Map(((IUnionType)type).Types, (t, _) => InstantiateInstantiableTypes(t, mapper))!, UnionReduction.None);
        }
        if ((type.Flags & TypeFlags.Intersection) != 0)
        {
            return GetIntersectionType(Core.Map(((IIntersectionType)type).Types, (t, _) => InstantiateInstantiableTypes(t, mapper))!);
        }
        return type;
    }

    /**
     * Whoa! Do you really want to use this function?
     *
     * Unless you're trying to get the *non-apparent* type for a
     * value-literal type or you're authoring relevant portions of this algorithm,
     * you probably meant to use 'getApparentTypeOfContextualType'.
     * Otherwise this may not be very useful.
     *
     * In cases where you *are* working on this function, you should understand
     * when it is appropriate to use 'getContextualType' and 'getApparentTypeOfContextualType'.
     *
     *   - Use 'getContextualType' when you are simply going to propagate the result to the expression.
     *   - Use 'getApparentTypeOfContextualType' when you're going to need the members of the type.
     *
     * @param node the expression whose contextual type will be returned.
     * @returns the contextual type of an expression.
     */
    /// <summary>ported from: src/compiler/checker.ts:32781-32865 — <c>getContextualType</c>.</summary>
    internal IType? GetContextualType(Expression node, ContextFlags? contextFlags)
    {
        if ((node.Flags & NodeFlags.InWithStatement) != 0)
        {
            // We cannot answer semantic questions within a with block, do not proceed any further
            return null;
        }
        // Cached contextual types are obtained with no ContextFlags, so we can only consult them for
        // requests with no ContextFlags.
        var index = FindContextualNode(node, /*includeCaches*/ contextFlags is null);
        if (index >= 0)
        {
            return _contextualTypes[index];
        }
        var parent = node.Parent!;
        switch (parent.Kind)
        {
            case SyntaxKind.VariableDeclaration:
            case SyntaxKind.Parameter:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.PropertySignature:
            case SyntaxKind.BindingElement:
                return GetContextualTypeForInitializerExpression(node, contextFlags);
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.ReturnStatement:
                return GetContextualTypeForReturnExpression(node, contextFlags);
            case SyntaxKind.YieldExpression:
                return GetContextualTypeForYieldOperand((YieldExpression)parent, contextFlags);
            case SyntaxKind.AwaitExpression:
                return GetContextualTypeForAwaitOperand((AwaitExpression)parent, contextFlags);
            case SyntaxKind.CallExpression:
            case SyntaxKind.NewExpression:
                return GetContextualTypeForArgument(parent, node);
            case SyntaxKind.Decorator:
                return GetContextualTypeForDecorator((Decorator)parent);
            case SyntaxKind.TypeAssertionExpression:
            case SyntaxKind.AsExpression:
                var assertionType = parent switch
                {
                    TypeAssertion assertion => assertion.Type,
                    AsExpression assertion => assertion.Type,
                    _ => null,
                };
                return Utilities.IsConstTypeReference((Node)assertionType!) ? GetContextualType((Expression)parent, contextFlags) : GetTypeFromTypeNode(assertionType);
            case SyntaxKind.BinaryExpression:
                return GetContextualTypeForBinaryOperand(node, contextFlags);
            case SyntaxKind.PropertyAssignment:
            case SyntaxKind.ShorthandPropertyAssignment:
                return GetContextualTypeForObjectLiteralElement(parent, contextFlags);
            case SyntaxKind.SpreadAssignment:
                return GetContextualType((Expression)parent.Parent!, contextFlags);
            case SyntaxKind.ArrayLiteralExpression:
                var arrayLiteral = (ArrayLiteralExpression)parent;
                var type = GetApparentTypeOfContextualType(arrayLiteral, contextFlags);
                var elementIndex = arrayLiteral.Elements!.IndexOf(node);
                var links = GetNodeLinks(arrayLiteral);
                links.SpreadIndices ??= GetSpreadIndices(arrayLiteral.Elements!);
                var spreadIndices = ((int? first, int? last))links.SpreadIndices;
                return GetContextualTypeForElementExpression(type, elementIndex, arrayLiteral.Elements!.Count, spreadIndices.first, spreadIndices.last);
            case SyntaxKind.ConditionalExpression:
                return GetContextualTypeForConditionalOperand(node, contextFlags);
            case SyntaxKind.TemplateSpan:
                Debug.Assert(parent.Parent!.Kind == SyntaxKind.TemplateExpression);
                return GetContextualTypeForSubstitutionExpression((TemplateExpression)parent.Parent!, node);
            case SyntaxKind.ParenthesizedExpression:
                if (Utilities.IsInJSFile(parent))
                {
                    if (Utilities.IsJSDocSatisfiesExpression((Node)parent))
                    {
                        return GetTypeFromTypeNode(Utilities.GetJSDocSatisfiesExpressionType((Node)parent));
                    }
                    // Like in `checkParenthesizedExpression`, an `/** @type {xyz} */` comment before a parenthesized expression acts as a type cast.
                    var typeTag = Utilities.GetJSDocTypeTag((Node)parent);
                    if (typeTag is not null && !Utilities.IsConstTypeReference((Node)typeTag.TypeExpression!.Type!))
                    {
                        return GetTypeFromTypeNode(typeTag.TypeExpression!.Type!);
                    }
                }
                return GetContextualType((Expression)parent, contextFlags);
            case SyntaxKind.NonNullExpression:
                return GetContextualType((Expression)parent, contextFlags);
            case SyntaxKind.SatisfiesExpression:
                return GetTypeFromTypeNode(((SatisfiesExpression)parent).Type!);
            case SyntaxKind.ExportAssignment:
                return TryGetTypeFromEffectiveTypeNode(parent);
            case SyntaxKind.JsxExpression:
                return GetContextualTypeForJsxExpression((JsxExpression)parent, contextFlags);
            case SyntaxKind.JsxAttribute:
            case SyntaxKind.JsxSpreadAttribute:
                return GetContextualTypeForJsxAttribute(parent, contextFlags);
            case SyntaxKind.JsxOpeningElement:
            case SyntaxKind.JsxSelfClosingElement:
                return GetContextualJsxElementAttributesType(parent, contextFlags);
            case SyntaxKind.ImportAttribute:
                return GetContextualImportAttributeType((ImportAttribute)parent);
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:32867-32869 — <c>pushCachedContextualType</c>.</summary>
    internal void PushCachedContextualType(Expression node)
        => PushContextualType(node, GetContextualType(node, /*contextFlags*/ null), /*isCache*/ true);

    /// <summary>ported from: src/compiler/checker.ts:32871-32876 — <c>pushContextualType</c>.</summary>
    internal void PushContextualType(Expression node, IType? type, bool isCache)
    {
        _contextualTypeNodes.Add(node);
        _contextualTypes.Add(type);
        _contextualIsCache.Add(isCache);
        _contextualTypeCount++;
    }

    /// <summary>ported from: src/compiler/checker.ts:32878-32884 — <c>popContextualType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS truncates the three arrays by writing past their new end (<c>contextualTypeNodes[contextualTypeCount] = undefined!</c>);
    /// the port removes the tail element of each list instead, which is the same end state (§15.13's list convention).
    /// </remarks>
    internal void PopContextualType()
    {
        _contextualTypeCount--;
        // Clear out the popped element's referenced objects.
        _contextualTypeNodes.RemoveAt(_contextualTypeCount);
        _contextualTypes.RemoveAt(_contextualTypeCount);
        _contextualIsCache.RemoveAt(_contextualTypeCount);
    }

    /// <summary>ported from: src/compiler/checker.ts:32886-32893 — <c>findContextualNode</c>.</summary>
    internal int FindContextualNode(Node node, bool includeCaches)
    {
        for (var i = _contextualTypeCount - 1; i >= 0; i--)
        {
            if (ReferenceEquals(node, _contextualTypeNodes[i]) && (includeCaches || !_contextualIsCache[i]))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>ported from: src/compiler/checker.ts:32895-32899 — <c>pushInferenceContext</c>.</summary>
    internal void PushInferenceContext(Node node, IInferenceContext? inferenceContext)
    {
        _inferenceContextNodes.Add(node);
        _inferenceContexts.Add(inferenceContext);
        _inferenceContextCount++;
    }

    /// <summary>ported from: src/compiler/checker.ts:32901-32905 — <c>popInferenceContext</c>.</summary>
    internal void PopInferenceContext()
    {
        _inferenceContextCount--;
        _inferenceContextNodes.RemoveAt(_inferenceContextCount);
        _inferenceContexts.RemoveAt(_inferenceContextCount);
    }

    /// <summary>ported from: src/compiler/checker.ts:32907-32913 — <c>getInferenceContext</c>.</summary>
    internal IInferenceContext? GetInferenceContext(Node node)
    {
        for (var i = _inferenceContextCount - 1; i >= 0; i--)
        {
            if (Utilities.IsNodeDescendantOf(node, _inferenceContextNodes[i]))
            {
                return _inferenceContexts[i];
            }
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:32943-32945 — <c>getContextualImportAttributeType</c>.</summary>
    internal IType? GetContextualImportAttributeType(ImportAttribute node)
        => GetTypeOfPropertyOfContextualType(GetGlobalImportAttributesType(/*reportErrors*/ false), Utilities.GetNameFromImportAttribute(node));

    /// <summary>ported from: src/compiler/checker.ts:32947-32960 — <c>getContextualJsxElementAttributesType</c>.</summary>
    internal IType? GetContextualJsxElementAttributesType(Node node, ContextFlags? contextFlags)
    {
        if (NodeTests.IsJsxOpeningElement(node) && contextFlags != ContextFlags.IgnoreNodeInferences)
        {
            var index = FindContextualNode(node.Parent!, /*includeCaches*/ contextFlags is null);
            if (index >= 0)
            {
                // Contextually applied type is moved from attributes up to the outer jsx attributes so when walking up from the children they get hit
                // _However_ to hit them from the _attributes_ we must look for them here; otherwise we'll used the declared type
                // (as below) instead!
                return _contextualTypes[index];
            }
        }
        return GetContextualTypeForArgumentAtIndex(node, 0);
    }

    /// <summary>ported from: src/compiler/checker.ts:32062-32069 — <c>getContextualTypeForAwaitOperand</c>.</summary>
    internal IType? GetContextualTypeForAwaitOperand(AwaitExpression node, ContextFlags? contextFlags)
    {
        var contextualType = GetContextualType(node, contextFlags);
        if (contextualType is not null)
        {
            var contextualAwaitedType = GetAwaitedTypeNoAlias(contextualType);
            return contextualAwaitedType is not null ? GetUnionType([contextualAwaitedType, CreatePromiseLikeType(contextualAwaitedType)]) : null;
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:32071-32098 — <c>getContextualTypeForYieldOperand</c>.</summary>
    internal IType? GetContextualTypeForYieldOperand(YieldExpression node, ContextFlags? contextFlags)
    {
        var func = Utilities.GetContainingFunction(node);
        if (func is not null)
        {
            var funcNode = (Node)func;
            var functionFlags = Utilities.GetFunctionFlags(funcNode);
            var contextualReturnType = GetContextualReturnType(funcNode, contextFlags);
            if (contextualReturnType is not null)
            {
                var isAsyncGenerator = (functionFlags & FunctionFlags.Async) != 0;
                if (node.AsteriskToken is null && (contextualReturnType.Flags & TypeFlags.Union) != 0)
                {
                    contextualReturnType = FilterType(contextualReturnType, type => GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Return, type, isAsyncGenerator) is not null);
                }
                if (node.AsteriskToken is not null)
                {
                    var iterationTypes = GetIterationTypesOfGeneratorFunctionReturnType(contextualReturnType, isAsyncGenerator);
                    var yieldType = iterationTypes?.YieldType ?? _silentNeverType;
                    var returnType = GetContextualType(node, contextFlags) ?? _silentNeverType;
                    var nextType = iterationTypes?.NextType ?? _unknownType;
                    var generatorType = CreateGeneratorType(yieldType, returnType, nextType, /*isAsyncGenerator*/ false);
                    if (isAsyncGenerator)
                    {
                        var asyncGeneratorType = CreateGeneratorType(yieldType, returnType, nextType, /*isAsyncGenerator*/ true);
                        return GetUnionType([generatorType, asyncGeneratorType]);
                    }
                    return generatorType;
                }
                return GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Yield, contextualReturnType, isAsyncGenerator);
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:32100-32114 — <c>isInParameterInitializerBeforeContainingFunction</c>.</summary>
    internal bool IsInParameterInitializerBeforeContainingFunction(Node node)
    {
        var inBindingInitializer = false;
        while (node.Parent is not null && !Utilities.IsFunctionLike(node.Parent))
        {
            if (NodeTests.IsParameter(node.Parent) && (inBindingInitializer || ReferenceEquals(((ParameterDeclaration)node.Parent).Initializer, node)))
            {
                return true;
            }
            if (NodeTests.IsBindingElement(node.Parent) && ReferenceEquals(((BindingElement)node.Parent).Initializer, node))
            {
                inBindingInitializer = true;
            }

            node = node.Parent;
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:32116-32125 — <c>getContextualIterationType</c>.</summary>
    internal IType? GetContextualIterationType(IterationTypeKind kind, Node functionDecl)
    {
        var isAsync = (Utilities.GetFunctionFlags(functionDecl) & FunctionFlags.Async) != 0;
        var contextualReturnType = GetContextualReturnType(functionDecl, /*contextFlags*/ null);
        if (contextualReturnType is not null)
        {
            return GetIterationTypeOfGeneratorFunctionReturnType(kind, contextualReturnType, isAsync) ?? null;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:32127-32157 — <c>getContextualReturnType</c>.</summary>
    internal IType? GetContextualReturnType(Node functionDecl, ContextFlags? contextFlags)
    {
        // If the containing function has a return type annotation, is a constructor, or is a get accessor whose
        // corresponding set accessor has a type annotation, return statements in the function are contextually typed
        var returnType = GetReturnTypeFromAnnotation(functionDecl);
        if (returnType is not null)
        {
            return returnType;
        }
        // Otherwise, if the containing function is contextually typed by a function type with exactly one call signature
        // and that call signature is non-generic, return statements are contextually typed by the return type of the signature
        var signature = GetContextualSignatureForFunctionLikeDeclaration(functionDecl);
        if (signature is not null && !IsResolvingReturnTypeOfSignature(signature))
        {
            var returnTypeOfSignature = GetReturnTypeOfSignature(signature);
            var functionFlags = Utilities.GetFunctionFlags(functionDecl);
            if ((functionFlags & FunctionFlags.Generator) != 0)
            {
                return FilterType(returnTypeOfSignature, t =>
                    (t.Flags & (TypeFlags.AnyOrUnknown | TypeFlags.Void | TypeFlags.InstantiableNonPrimitive)) != 0
                        || CheckGeneratorInstantiationAssignabilityToReturnType(t, functionFlags, /*errorNode*/ null));
            }
            if ((functionFlags & FunctionFlags.Async) != 0)
            {
                return FilterType(returnTypeOfSignature, t =>
                    (t.Flags & (TypeFlags.AnyOrUnknown | TypeFlags.Void | TypeFlags.InstantiableNonPrimitive)) != 0
                        || GetAwaitedTypeOfPromise(t) is not null);
            }
            return returnTypeOfSignature;
        }
        var iife = Utilities.GetImmediatelyInvokedFunctionExpression(functionDecl);
        if (iife is not null)
        {
            return GetContextualType(iife, contextFlags);
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:27771-27785 — <c>getResolvedSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>resolveName(node, node, …)</c> passes the identifier as its own name argument; the port's
    /// <see cref="ResolveName(Node?, string, SymbolFlags, DiagnosticMessage?, bool, bool)"/> takes the name as a
    /// <c>string</c>, so it is the identifier's escaped text.
    /// </remarks>
    internal ISymbol GetResolvedSymbol(Identifier node)
    {
        var links = GetNodeLinks(node);
        if (links.ResolvedSymbol is null)
        {
            links.ResolvedSymbol = !Utilities.NodeIsMissing(node)
                && ResolveName(
                    node,
                    node.EscapedText!,
                    SymbolFlags.Value | SymbolFlags.ExportValue,
                    GetCannotFindNameDiagnosticForName(node),
                    !Utilities.IsWriteOnlyAccess(node),
                    /*excludeGlobals*/ false) is { } resolvedSymbol
                ? resolvedSymbol
                : _unknownSymbol;
        }
        return links.ResolvedSymbol;
    }

    /// <summary>ported from: src/compiler/checker.ts:27787-27789 — <c>isInAmbientOrTypeNode</c>.</summary>
    internal bool IsInAmbientOrTypeNode(Node node)
        => (node.Flags & NodeFlags.Ambient) != 0 || Utilities.FindAncestor(node, n => NodeTests.IsInterfaceDeclaration(n) || NodeTests.IsTypeAliasDeclaration(n) || NodeTests.IsTypeLiteralNode(n)) is not null;

    /// <summary>ported from: src/compiler/checker.ts:32960-32965 — <c>getEffectiveFirstArgumentForJsxSignature</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>node</c> is <c>JsxCallLike</c> and the second arm hands it to
    /// <c>getJsxPropsTypeFromClassType</c>, whose parameter is <c>JsxOpeningLikeElement</c> — TS narrows the union by
    /// the <c>isJsxOpeningFragment</c> test the first arm states. C# cannot narrow between the two classes, so the
    /// value stays a <see cref="Node"/> and the callee reads the slots it needs per class; the fragment never reaches
    /// it (the first arm already answered).
    /// </remarks>
    internal IType GetEffectiveFirstArgumentForJsxSignature(ISignature signature, Node node)
        => NodeTests.IsJsxOpeningFragment(node) || GetJsxReferenceKind(node) != JsxReferenceKind.Component
            ? GetJsxPropsTypeFromCallSignature(signature, node)
            : GetJsxPropsTypeFromClassType(signature, node);

    /// <summary>ported from: src/compiler/checker.ts:32966-32975 — <c>getJsxPropsTypeFromCallSignature</c>.</summary>
    internal IType GetJsxPropsTypeFromCallSignature(ISignature sig, Node context)
    {
        var propsType = GetTypeOfFirstParameterOfSignatureWithFallback(sig, _unknownType);
        // PORT-NOTE: the reference leaves `propsType` a bare `Type` and reassigns it from `intersectTypes`, whose
        // C# face is nullable; the `!` at the return is the invariant the reference's type checker kept.
        propsType = GetJsxManagedAttributesFromLocatedAttributes(context, GetJsxNamespaceAt(context)!, propsType);
        var intrinsicAttribs = GetJsxType(JsxNames.IntrinsicAttributes, context);
        if (!IsErrorType(intrinsicAttribs))
        {
            propsType = IntersectTypes(intrinsicAttribs, propsType);
        }
        return propsType!;
    }

    /// <summary>ported from: src/compiler/checker.ts:32976-33000 — <c>getJsxPropsTypeForSignatureFromMember</c>.</summary>
    internal IType? GetJsxPropsTypeForSignatureFromMember(ISignature sig, string forcedLookupLocation)
    {
        if (sig.CompositeSignatures is not null)
        {
            // JSX Elements using the legacy `props`-field based lookup (eg, react class components) need to treat the `props` member as an input
            // instead of an output position when resolving the signature. We need to go back to the input signatures of the composite signature,
            // get the type of `props` on each return type individually, and then _intersect them_, rather than union them (as would normally occur
            // for a union signature). It's an unfortunate quirk of looking in the output of the signature for the type we want to use for the input.
            // The default behavior of `getTypeOfFirstParameterOfSignatureWithFallback` when no `props` member name is defined is much more sane.
            List<IType> results = [];
            foreach (var signature in sig.CompositeSignatures)
            {
                var instance = GetReturnTypeOfSignature(signature);
                if (IsTypeAny(instance))
                {
                    return instance;
                }
                var propType = GetTypeOfPropertyOfType(instance, forcedLookupLocation);
                if (propType is null)
                {
                    return null;
                }
                results.Add(propType);
            }
            return GetIntersectionType(results); // Same result for both union and intersection signatures
        }
        var instanceType = GetReturnTypeOfSignature(sig);
        return IsTypeAny(instanceType) ? instanceType : GetTypeOfPropertyOfType(instanceType, forcedLookupLocation);
    }

    /// <summary>ported from: src/compiler/checker.ts:33001-33019 — <c>getStaticTypeOfReferencedJsxConstructor</c>.</summary>
    internal IType GetStaticTypeOfReferencedJsxConstructor(Node context)
    {
        if (NodeTests.IsJsxOpeningFragment(context)) return GetJSXFragmentType((JsxOpeningFragment)context);
        var tagName = context switch
        {
            JsxOpeningElement opening => (Node?)opening.TagName,
            JsxSelfClosingElement selfClosing => selfClosing.TagName,
            _ => Debug.Fail<Node?>("a jsx opening-like element must have a tag name"),
        };
        if (IsJsxIntrinsicTagName(tagName!))
        {
            var result = GetIntrinsicAttributesTypeFromJsxOpeningLikeElement(context);
            var fakeSignature = CreateSignatureForJSXIntrinsic(context, result);
            return GetOrCreateTypeFromSignature(fakeSignature);
        }
        var tagType = CheckExpressionCached((Expression)tagName!);
        if ((tagType.Flags & TypeFlags.StringLiteral) != 0)
        {
            var result = GetIntrinsicAttributesTypeFromStringLiteralType((IStringLiteralType)tagType, context);
            if (result is null)
            {
                return _errorType;
            }
            var fakeSignature = CreateSignatureForJSXIntrinsic(context, result);
            return GetOrCreateTypeFromSignature(fakeSignature);
        }
        return tagType;
    }

    /// <summary>ported from: src/compiler/checker.ts:33020-33031 — <c>getJsxManagedAttributesFromLocatedAttributes</c>.</summary>
    internal IType GetJsxManagedAttributesFromLocatedAttributes(Node context, ISymbol ns, IType attributesType)
    {
        var managedSym = GetJsxLibraryManagedAttributes(ns);
        if (managedSym is not null)
        {
            var ctorType = GetStaticTypeOfReferencedJsxConstructor(context);
            var result = InstantiateAliasOrInterfaceWithDefaults(managedSym, Utilities.IsInJSFile(context), ctorType, attributesType);
            if (result is not null)
            {
                return result;
            }
        }
        return attributesType;
    }

    /// <summary>ported from: src/compiler/checker.ts:33032-33084 — <c>getJsxPropsTypeFromClassType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>context</c> is <c>JsxOpeningLikeElement</c>
    /// (<c>JsxOpeningElement | JsxSelfClosingElement</c>, types.ts:3199-3201), whose one slot this body reads is
    /// <c>attributes</c>; the port's two classes share no type below <see cref="Node"/>, so the parameter is
    /// <see cref="Node"/> and the attributes read is the same cast pair every landed JSX caller uses
    /// (<c>Checker/Checker.CallResolution.cs</c>).
    /// </remarks>
    internal IType GetJsxPropsTypeFromClassType(ISignature sig, Node context)
    {
        var ns = GetJsxNamespaceAt(context);
        var forcedLookupLocation = GetJsxElementPropertiesName(ns);
        var attributesType = forcedLookupLocation is null
            // If there is no type ElementAttributesProperty, return the type of the first parameter of the signature, which should be the props type
            ? GetTypeOfFirstParameterOfSignatureWithFallback(sig, _unknownType)
            : forcedLookupLocation == ""
                // If there is no e.g. 'props' member in ElementAttributesProperty, use the element class type instead
                ? GetReturnTypeOfSignature(sig)
                // Otherwise get the type of the property on the signature return type
                : GetJsxPropsTypeForSignatureFromMember(sig, forcedLookupLocation);

        if (attributesType is null)
        {
            // There is no property named 'props' on this instance type
            if (forcedLookupLocation is not null && forcedLookupLocation != ""
                && ((context as JsxOpeningElement)?.Attributes ?? (context as JsxSelfClosingElement)?.Attributes)!.Properties!.Count != 0)
            {
                Error(context, Diagnostics.JSX_element_class_does_not_support_attributes_because_it_does_not_have_a_0_property, Utilities.UnescapeLeadingUnderscores(forcedLookupLocation));
            }
            return _unknownType;
        }

        attributesType = GetJsxManagedAttributesFromLocatedAttributes(context, ns!, attributesType);

        if (IsTypeAny(attributesType))
        {
            // Props is of type 'any' or unknown
            return attributesType;
        }
        else
        {
            // Normal case -- add in IntrinsicClassElements<T> and IntrinsicElements
            var apparentAttributesType = attributesType;
            var intrinsicClassAttribs = GetJsxType(JsxNames.IntrinsicClassAttributes, context);
            if (!IsErrorType(intrinsicClassAttribs))
            {
                var typeParams = GetLocalTypeParametersOfClassOrInterfaceOrTypeAlias(intrinsicClassAttribs.Symbol!);
                var hostClassType = GetReturnTypeOfSignature(sig);
                IType libraryManagedAttributeType;
                if (typeParams is not null)
                {
                    // apply JSX.IntrinsicClassElements<hostClassType, ...>
                    var inferredArgs = FillMissingTypeArguments([hostClassType], typeParams, GetMinTypeArgumentCount(typeParams), Utilities.IsInJSFile(context));
                    libraryManagedAttributeType = InstantiateType(intrinsicClassAttribs, CreateTypeMapper(typeParams, inferredArgs))!;
                }
                // or JSX.IntrinsicClassElements has no generics.
                else libraryManagedAttributeType = intrinsicClassAttribs;
                apparentAttributesType = IntersectTypes(libraryManagedAttributeType, apparentAttributesType);
            }

            var intrinsicAttribs = GetJsxType(JsxNames.IntrinsicAttributes, context);
            if (!IsErrorType(intrinsicAttribs))
            {
                apparentAttributesType = IntersectTypes(intrinsicAttribs, apparentAttributesType);
            }

            return apparentAttributesType!;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:33085-33095 — <c>getIntersectedSignatures</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference folds with <c>reduceLeft</c>, whose callback may answer <c>undefined</c>; the port's
    /// <c>Core.ReduceLeft</c> delegate is <c>Func&lt;T, T, int, T&gt;</c> (no null in the function type), so the fold is
    /// written out with the same seed (the first element) and the same three-way choice.
    /// </remarks>
    internal ISignature? GetIntersectedSignatures(IList<ISignature> signatures)
    {
        if (!Utilities.GetStrictOptionValue(_compilerOptions, "noImplicitAny"))
        {
            return null;
        }
        ISignature? result = null;
        var seeded = false;
        foreach (var signature in signatures)
        {
            if (!seeded)
            {
                result = signature;
                seeded = true;
            }
            else if (result is null)
            {
                // stays null
            }
            else if (ReferenceEquals(result, signature))
            {
                // stays the same
            }
            else if (CompareTypeParametersIdentical(result.TypeParameters, signature.TypeParameters))
            {
                result = CombineSignaturesOfIntersectionMembers(result, signature);
            }
            else
            {
                result = null;
            }
        }
        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:33097-33106 — <c>combineIntersectionThisParam</c>.</summary>
    internal ISymbol? CombineIntersectionThisParam(ISymbol? left, ISymbol? right, TypeMapper? mapper)
    {
        if (left is null || right is null)
        {
            return left ?? right;
        }
        var thisType = GetUnionType([GetTypeOfSymbol(left), InstantiateType(GetTypeOfSymbol(right), mapper)!]);
        return CreateSymbolWithType(left, thisType);
    }

    /// <summary>ported from: src/compiler/checker.ts:33108-33153 — <c>combineIntersectionParameters</c>.</summary>
    internal List<ISymbol> CombineIntersectionParameters(ISignature left, ISignature right, TypeMapper? mapper)
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
                longestParamType = InstantiateType(longestParamType, mapper)!;
            }
            var shorterParamType = TryGetTypeAtPosition(shorter, i) ?? _unknownType;
            if (ReferenceEquals(shorter, right))
            {
                shorterParamType = InstantiateType(shorterParamType, mapper)!;
            }
            var unionParamType = GetUnionType([longestParamType, shorterParamType]);
            var isRestParam = eitherHasEffectiveRest && !needsExtraRestElement && i == longestCount - 1;
            var isOptional = i >= GetMinArgumentCount(longest) && i >= GetMinArgumentCount(shorter);
            var leftName = i >= leftCount ? null : GetParameterNameAtPosition(left, i);
            var rightName = i >= rightCount ? null : GetParameterNameAtPosition(right, i);

            var paramName = leftName == rightName ? leftName :
                leftName is null ? rightName :
                rightName is null ? leftName :
                null;
            var paramSymbol = CreateSymbol(
                SymbolFlags.FunctionScopedVariable | (isOptional && !isRestParam ? SymbolFlags.Optional : 0),
                paramName ?? "arg" + i,
                isRestParam ? CheckFlags.RestParameter : isOptional ? CheckFlags.OptionalParameter : 0);
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

    /// <summary>ported from: src/compiler/checker.ts:33155-33187 — <c>combineSignaturesOfIntersectionMembers</c>.</summary>
    internal ISignature CombineSignaturesOfIntersectionMembers(ISignature left, ISignature right)
    {
        var typeParameters = left.TypeParameters ?? right.TypeParameters;
        TypeMapper? paramMapper = null;
        if (left.TypeParameters is not null && right.TypeParameters is not null)
        {
            paramMapper = CreateTypeMapper(right.TypeParameters, new List<IType>(left.TypeParameters));
            // We just use the type parameter defaults from the first signature
        }
        var flags = (left.Flags | right.Flags) & (SignatureFlags.PropagatingFlags & ~SignatureFlags.HasRestParameter);
        var declaration = left.Declaration;
        var parameters = CombineIntersectionParameters(left, right, paramMapper);
        var lastParam = Core.LastOrUndefined(parameters);
        if (lastParam is not null && (GetCheckFlags(lastParam) & CheckFlags.RestParameter) != 0)
        {
            flags |= SignatureFlags.HasRestParameter;
        }
        var thisParam = CombineIntersectionThisParam(left.ThisParameter, right.ThisParameter, paramMapper);
        var minArgumentCount = Math.Max(left.MinArgumentCount, right.MinArgumentCount);
        var result = CreateSignature(
            declaration,
            typeParameters,
            thisParam,
            parameters,
            /*resolvedReturnType*/ null,
            /*resolvedTypePredicate*/ null,
            minArgumentCount,
            flags);
        result.CompositeKind = TypeFlags.Intersection;
        result.CompositeSignatures = Core.Concatenate(
            (left.CompositeKind == TypeFlags.Intersection && left.CompositeSignatures is not null ? left.CompositeSignatures : new List<ISignature> { left }),
            new List<ISignature> { right })!.ToList();
        if (paramMapper is not null)
        {
            result.Mapper = left.CompositeKind == TypeFlags.Intersection && left.Mapper is not null && left.CompositeSignatures is not null
                ? CombineTypeMappers((TypeMapper)left.Mapper, paramMapper)
                : paramMapper;
        }
        return result;
    }

    // If the given type is an object or union type with a single signature, and if that signature has at
    // least as many parameters as the given function, return the signature. Otherwise return undefined.
    /// <summary>ported from: src/compiler/checker.ts:33191-33195 — <c>getContextualCallSignature</c>.</summary>
    internal ISignature? GetContextualCallSignature(IType type, Node node)
    {
        var signatures = GetSignaturesOfType(type, SignatureKind.Call);
        var applicableByArity = Core.Filter(signatures, s => !IsAritySmaller(s, node))!;
        return applicableByArity.Count == 1 ? applicableByArity[0] : GetIntersectedSignatures(applicableByArity);
    }

    /// <summary>ported from: src/compiler/checker.ts:33198-33210 — <c>isAritySmaller</c>.</summary>
    internal bool IsAritySmaller(ISignature signature, Node target)
    {
        var targetParameterCount = 0;
        var targetParameters = ((ISignatureDeclarationBase)target).Parameters!;
        for (; targetParameterCount < targetParameters.Count; targetParameterCount++)
        {
            var param = targetParameters[targetParameterCount];
            if (param.Initializer is not null || param.QuestionToken is not null || param.DotDotDotToken is not null || Utilities.IsJSDocOptionalParameter(param))
            {
                break;
            }
        }
        if (targetParameters.Count != 0 && Utilities.ParameterIsThisKeyword(targetParameters[0]))
        {
            targetParameterCount--;
        }
        return !HasEffectiveRestParameter(signature) && GetParameterCount(signature) < targetParameterCount;
    }

    /// <summary>ported from: src/compiler/checker.ts:33212-33217 — <c>getContextualSignatureForFunctionLikeDeclaration</c>.</summary>
    internal ISignature? GetContextualSignatureForFunctionLikeDeclaration(Node node)
        // Only function expressions, arrow functions, and object literal methods are contextually typed.
        => Utilities.IsFunctionExpressionOrArrowFunction(node) || Utilities.IsObjectLiteralMethod(node)
            ? GetContextualSignature(node)
            : null;

    // Return the contextual signature for a given expression node. A contextual type provides a
    // contextual signature if it has a single call signature and if that call signature is non-generic.
    // If the contextual type is a union type, get the signature from each type possible and if they are
    // all identical ignoring their return type, the result is same signature but with return type as
    // union type of return types from these signatures
    /// <summary>ported from: src/compiler/checker.ts:33224-33260 — <c>getContextualSignature</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>FunctionExpression | ArrowFunction | MethodDeclaration</c>; the port
    /// takes <c>Node</c> and keeps the reference's own assertion.
    /// </remarks>
    internal ISignature? GetContextualSignature(Node node)
    {
        Debug.Assert(node.Kind != SyntaxKind.MethodDeclaration || Utilities.IsObjectLiteralMethod(node));
        var typeTagSignature = GetSignatureOfTypeTag(node);
        if (typeTagSignature is not null)
        {
            return typeTagSignature;
        }
        var type = GetApparentTypeOfContextualType(node, ContextFlags.Signature);
        if (type is null)
        {
            return null;
        }
        if ((type.Flags & TypeFlags.Union) == 0)
        {
            return GetContextualCallSignature(type, node);
        }
        List<ISignature>? signatureList = null;
        var types = ((IUnionType)type).Types!;
        foreach (var current in types)
        {
            var signature = GetContextualCallSignature(current, node);
            if (signature is not null)
            {
                if (signatureList is null)
                {
                    // This signature will contribute to contextual union signature
                    signatureList = [signature];
                }
                else if (CompareSignaturesIdentical(signatureList[0], signature, /*partialMatch*/ false, /*ignoreThisTypes*/ true, /*ignoreReturnTypes*/ true, CompareTypesIdentical) == Ternary.False)
                {
                    // Signatures aren't identical, do not use
                    return null;
                }
                else
                {
                    // Use this signature for contextual union signature
                    signatureList.Add(signature);
                }
            }
        }
        // Result is union of signatures collected (return type is union of return types of this signature set)
        if (signatureList is not null)
        {
            return signatureList.Count == 1 ? signatureList[0] : CreateUnionSignature(signatureList[0], signatureList);
        }
        return null;
    }

    // ---------------------------------------------------------------------------------------------
    // Discriminant properties (checker.ts:27967-28073)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:27967-27980 — <c>isDiscriminantProperty</c>.</summary>
    internal bool IsDiscriminantProperty(IType? type, string name)
    {
        if (type is not null && (type.Flags & TypeFlags.Union) != 0)
        {
            var prop = GetUnionOrIntersectionProperty((UnionOrIntersectionType)type, name);
            if (prop is not null && (GetCheckFlags(prop) & CheckFlags.SyntheticProperty) != 0)
            {
                // NOTE: cast to TransientSymbol should be safe because only TransientSymbols can have CheckFlags.SyntheticProperty
                var links = GetSymbolLinks(prop);
                links.IsDiscriminantProperty ??= (GetCheckFlags(prop) & CheckFlags.Discriminant) == CheckFlags.Discriminant && !IsGenericType(GetTypeOfSymbol(prop));
                return links.IsDiscriminantProperty.Value;
            }
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:27982-27994 — <c>findDiscriminantProperties</c>.</summary>
    internal List<ISymbol>? FindDiscriminantProperties(List<ISymbol> sourceProperties, IType target)
    {
        List<ISymbol>? result = null;
        foreach (var sourceProperty in sourceProperties)
        {
            if (IsDiscriminantProperty(target, sourceProperty.EscapedName!))
            {
                if (result is not null)
                {
                    result.Add(sourceProperty);
                    continue;
                }
                result = [sourceProperty];
            }
        }
        return result;
    }

    // Given a set of constituent types and a property name, create and return a map keyed by the literal
    // types of the property by that name in each constituent type. No map is returned if some key property
    // has a non-literal type or if less than 10 or less than 50% of the constituents have a unique key.
    // Entries with duplicate keys have unknownType as the value.
    /// <summary>ported from: src/compiler/checker.ts:28000-28025 — <c>mapTypesByKeyProperty</c>.</summary>
    internal Dictionary<int, IType>? MapTypesByKeyProperty(List<IType> types, string name)
    {
        Dictionary<int, IType> map = [];
        var count = 0;
        foreach (var type in types)
        {
            if ((type.Flags & (TypeFlags.Object | TypeFlags.Intersection | TypeFlags.InstantiableNonPrimitive)) != 0)
            {
                var discriminant = GetTypeOfPropertyOfType(type, name);
                if (discriminant is null || !IsLiteralType(discriminant))
                {
                    return null;
                }
                var duplicate = false;
                ForEachType<object>(discriminant, t =>
                {
                    var id = GetTypeId(GetRegularTypeOfLiteralType(t));
                    var existing = map.GetValueOrDefault(id);
                    if (existing is null)
                    {
                        map[id] = type;
                    }
                    else if (!ReferenceEquals(existing, _unknownType))
                    {
                        map[id] = _unknownType;
                        duplicate = true;
                    }
                    return null;
                });
                if (!duplicate) count++;
            }
        }
        return count >= 10 && count * 2 >= types.Count ? map : null;
    }

    // Return the name of a discriminant property for which it was possible and feasible to construct a map of
    // constituent types keyed by the literal types of the property by that name in each constituent type.
    /// <summary>ported from: src/compiler/checker.ts:28029-28050 — <c>getKeyPropertyName</c>.</summary>
    internal string? GetKeyPropertyName(IUnionType unionType)
    {
        var types = unionType.Types!;
        // We only construct maps for unions with many non-primitive constituents.
        if (
            types.Count < 10 || (GetObjectFlags(unionType) & ObjectFlags.PrimitiveUnion) != 0 ||
            Core.CountWhere(types, (t, _) => (t.Flags & (TypeFlags.Object | TypeFlags.InstantiableNonPrimitive)) != 0) < 10
        )
        {
            return null;
        }
        if (unionType.KeyPropertyName is null)
        {
            // The candidate key property name is the name of the first property with a unit type in one of the
            // constituent types.
            var keyPropertyName = Core.ForEach(types, (t, _) =>
                (t.Flags & (TypeFlags.Object | TypeFlags.InstantiableNonPrimitive)) != 0
                    ? Core.ForEach(GetPropertiesOfType(t), (p, _) => IsUnitType(GetTypeOfSymbol(p)) ? p.EscapedName : null)
                    : null);
            var mapByKeyProperty = keyPropertyName is not null ? MapTypesByKeyProperty(types, keyPropertyName) : null;
            unionType.KeyPropertyName = mapByKeyProperty is not null ? keyPropertyName : "";
            unionType.ConstituentMap = mapByKeyProperty;
        }
        return unionType.KeyPropertyName!.Length != 0 ? unionType.KeyPropertyName : null;
    }

    // Given a union type for which getKeyPropertyName returned a non-undefined result, return the constituent
    // that corresponds to the given key type for that property name.
    /// <summary>ported from: src/compiler/checker.ts:28054-28057 — <c>getConstituentTypeForKeyType</c>.</summary>
    internal IType? GetConstituentTypeForKeyType(IUnionType unionType, IType keyType)
    {
        var result = unionType.ConstituentMap?.GetValueOrDefault(GetTypeId(GetRegularTypeOfLiteralType(keyType)));
        return !ReferenceEquals(result, _unknownType) ? result : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:28059-28063 — <c>getMatchingUnionConstituentForType</c>.</summary>
    internal IType? GetMatchingUnionConstituentForType(IUnionType unionType, IType type)
    {
        var keyPropertyName = GetKeyPropertyName(unionType);
        var propType = keyPropertyName is not null ? GetTypeOfPropertyOfType(type, keyPropertyName) : null;
        return propType is not null ? GetConstituentTypeForKeyType(unionType, propType) : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:28065-28072 — <c>getMatchingUnionConstituentForObjectLiteral</c>.</summary>
    internal IType? GetMatchingUnionConstituentForObjectLiteral(IUnionType unionType, ObjectLiteralExpression node)
    {
        var keyPropertyName = GetKeyPropertyName(unionType);
        var propNode = keyPropertyName is not null
            ? Core.Find(node.Properties, (p, _) =>
                ((IDeclaration)p).Symbol is not null
                && p.Kind == SyntaxKind.PropertyAssignment
                && ((IDeclaration)p).Symbol!.EscapedName == keyPropertyName
                && IsPossiblyDiscriminantValue(((PropertyAssignment)p).Initializer!))
            : null;
        var propType = propNode is not null ? GetContextFreeTypeOfExpression(((PropertyAssignment)propNode).Initializer!) : null;
        return propType is not null ? GetConstituentTypeForKeyType(unionType, propType) : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:2280 — <c>globalThisType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference assigns this in <c>initializeTypeChecker</c> (checker.ts:51649, M6-5's assembly). The
    /// slot is declared here because this file is its first (and only) reader — <c>getThisTypeArgument</c> (:31859) —
    /// the same reasoning the six stacks above use; <c>_globalRegExpType</c> stays unported because it still has none.
    /// </remarks>
    internal IType _globalThisType = null!;

    // =============================================================================================
    // The flow-analysis band (checker.ts:27795-32780) and its two dependencies outside it
    //
    // PORT-NOTE: this block lands the reference's members in reference order, so a reader can walk
    // checker.ts and this file side by side. Everything here is a member of the reference's
    // createTypeChecker closure and is called unqualified, exactly as upstream.
    // =============================================================================================

    /// <summary>ported from: src/compiler/checker.ts:1172 — <c>nextFlowId</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares this counter at module scope next to <c>nextNodeId</c> (checker.ts:1170), so it is
    /// shared by every checker instance. The port states it as a static field for the same reason
    /// <c>Utilities.GetNodeId</c> does, and keeps TS's initial value of <c>1</c>.
    /// </remarks>
    private static int s_nextFlowId = 1;

    /// <summary>ported from: src/compiler/types.ts:4259-4267 — <c>IncompleteType</c> and the <c>FlowType</c> union.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>FlowType = Type | IncompleteType</c> is a union with no C# counterpart. The reference
    /// discriminates it by <c>flags === 0</c> (<c>isIncomplete</c>, checker.ts:28625) and reads the payload as
    /// <c>flowType.type</c> (<c>getTypeFromFlowType</c>, checker.ts:28629). The port states the union as this one
    /// class: <see cref="Incomplete"/> carries TS's <c>flags === 0</c> test and <see cref="Type"/> carries the
    /// payload. Both arms are represented the same way here, which is what lets <c>getTypeAtFlowNode</c> and the
    /// <c>narrowType</c> family thread one slot type through, and it is why <c>Checker.Initialize.cs</c>'s
    /// <c>_sharedFlowTypes</c> (checker.ts:2364) is stated over this class rather than over <see cref="IType"/>.
    /// </remarks>
    internal sealed class FlowType
    {
        /// <summary>ported from: src/compiler/types.ts:4266 — <c>IncompleteType.flags</c> (always <c>0</c>).</summary>
        public TypeFlags Flags { get; }

        /// <summary>ported from: src/compiler/types.ts:4267 — <c>IncompleteType.type</c>.</summary>
        public IType Type { get; }

        /// <summary>ported from: src/compiler/types.ts:4260-4267 — the two arms of the union.</summary>
        public FlowType(IType type, bool incomplete)
        {
            Type = type;
            Flags = incomplete ? (TypeFlags)0 : type.Flags;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:28625-28627 — <c>isIncomplete</c>.</summary>
    internal static bool IsIncomplete(FlowType flowType) => flowType.Flags == 0;

    /// <summary>ported from: src/compiler/checker.ts:28629-28631 — <c>getTypeFromFlowType</c>.</summary>
    internal static IType GetTypeFromFlowType(FlowType flowType) => flowType.Type;

    /// <summary>ported from: src/compiler/checker.ts:28633-28635 — <c>createFlowType</c>.</summary>
    internal FlowType CreateFlowType(IType type, bool incomplete)
        => new(incomplete ? ((type.Flags & TypeFlags.Never) != 0 ? _silentNeverType : type) : type, incomplete);

    /// <summary>ported from: src/compiler/checker.ts:28095-28101 — <c>getFlowNodeId</c>.</summary>
    internal int GetFlowNodeId(FlowNode flow)
    {
        if (flow.Id <= 0)
        {
            flow.Id = s_nextFlowId;
            s_nextFlowId++;
        }
        return flow.Id;
    }

    /// <summary>PORT-NOTE: TS's flow caches (checker.ts:2359-2366) are JS arrays written at a flow-node id or at a
    /// saved loop index; JS grows an array on assignment and the port's <c>List</c> indexer throws past the end, so
    /// every such write grows the list with the element's absent value first — the <c>undefined</c> a reference slot
    /// would read as. This is the same <c>while</c> growth <c>_symbolLinks</c>/<c>_nodeLinks</c> state
    /// (<c>Checker.cs:456</c>).</summary>
    internal static void SetFlowSlot<T>(List<T?> slots, int index, T value)
    {
        while (slots.Count <= index)
        {
            slots.Add(default);
        }

        slots[index] = value;
    }

    /// <summary>PORT-NOTE: the read of a flow-cache slot — the reference's <c>array[index]</c>, which answers
    /// <c>undefined</c> for an index the array never reached. See <see cref="SetFlowSlot"/>.</summary>
    internal static T? GetFlowSlot<T>(List<T?> slots, int index) => index < slots.Count ? slots[index] : default;

    /// <summary>ported from: src/compiler/checker.ts:28103-28113 — <c>typeMaybeAssignableTo</c>.</summary>
    private bool TypeMaybeAssignableTo(IType source, IType target)
    {
        if ((source.Flags & TypeFlags.Union) == 0)
        {
            return IsTypeAssignableTo(source, target);
        }
        foreach (var t in ((IUnionType)source).Types!)
        {
            if (IsTypeAssignableTo(t, target))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:28118-28127 — <c>getAssignmentReducedType</c>.</summary>
    internal IType GetAssignmentReducedType(IUnionType declaredType, IType assignedType)
    {
        if (ReferenceEquals(declaredType, assignedType))
        {
            return declaredType;
        }
        if ((assignedType.Flags & TypeFlags.Never) != 0)
        {
            return assignedType;
        }
        var key = $"A{GetTypeId(declaredType)},{GetTypeId(assignedType)}";
        return GetCachedType(key) ?? SetCachedType(key, GetAssignmentReducedTypeWorker(declaredType, assignedType));
    }

    /// <summary>ported from: src/compiler/checker.ts:28129-28138 — <c>getAssignmentReducedTypeWorker</c>.</summary>
    private IType GetAssignmentReducedTypeWorker(IUnionType declaredType, IType assignedType)
    {
        var filteredType = FilterType(declaredType, t => TypeMaybeAssignableTo(assignedType, t));
        // Ensure that we narrow to fresh types if the assignment is a fresh boolean literal type.
        var reducedType = (assignedType.Flags & TypeFlags.BooleanLiteral) != 0 && IsFreshLiteralType(assignedType)
            ? MapType(filteredType, GetFreshTypeOfLiteralType)!
            : filteredType;
        // Our crude heuristic produces an invalid result in some cases: see GH#26130.
        // For now, when that happens, we give up and don't narrow at all.  (This also
        // means we'll never narrow for erroneous assignments where the assigned type
        // is not assignable to the declared type.)
        return IsTypeAssignableTo(assignedType, reducedType) ? reducedType : declaredType;
    }

    /// <summary>ported from: src/compiler/checker.ts:28140-28149 — <c>isFunctionObjectType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS passes the union <c>ObjectType | UnionOrIntersectionType</c> to <c>resolveStructuredTypeMembers</c>;
    /// the port's one concrete class for that union is <c>UnionOrIntersectionType</c> (ObjectType derives from it —
    /// §12.3, Model/ObjectType.cs), which is why the call casts through it.
    /// </remarks>
    internal bool IsFunctionObjectType(IObjectType type)
    {
        if ((GetObjectFlags(type) & ObjectFlags.EvolvingArray) != 0)
        {
            return false;
        }
        // We do a quick check for a "bind" property before performing the more expensive subtype
        // check. This gives us a quicker out in the common case where an object type is not a function.
        var resolved = ResolveStructuredTypeMembers((UnionOrIntersectionType)type);
        return resolved.CallSignatures!.Count != 0 || resolved.ConstructSignatures!.Count != 0
            || resolved.Members!.TryGetValue("bind", out _) && IsTypeSubtypeOf(type, _globalFunctionType);
    }

    /// <summary>ported from: src/compiler/checker.ts:28143-28153 — <c>getTypeFacts</c>.</summary>
    internal TypeFacts GetTypeFacts(IType type, TypeFacts mask) => GetTypeFactsWorker(type, mask) & mask;

    /// <summary>ported from: src/compiler/checker.ts:28155-28157 — <c>hasTypeFacts</c>.</summary>
    internal bool HasTypeFacts(IType type, TypeFacts mask) => GetTypeFacts(type, mask) != TypeFacts.None;

    /// <summary>ported from: src/compiler/checker.ts:28159-28241 — <c>getTypeFactsWorker</c>.</summary>
    private TypeFacts GetTypeFactsWorker(IType type, TypeFacts callerOnlyNeeds)
    {
        if ((type.Flags & (TypeFlags.Intersection | TypeFlags.Instantiable)) != 0)
        {
            type = GetBaseConstraintOfType(type) ?? _unknownType;
        }
        var flags = type.Flags;
        if ((flags & (TypeFlags.String | TypeFlags.StringMapping)) != 0)
        {
            return _strictNullChecks ? TypeFacts.StringStrictFacts : TypeFacts.StringFacts;
        }
        if ((flags & (TypeFlags.StringLiteral | TypeFlags.TemplateLiteral)) != 0)
        {
            var isEmpty = (flags & TypeFlags.StringLiteral) != 0 && (string)((ILiteralType)type).Value! == "";
            return _strictNullChecks
                ? isEmpty ? TypeFacts.EmptyStringStrictFacts : TypeFacts.NonEmptyStringStrictFacts
                : isEmpty ? TypeFacts.EmptyStringFacts : TypeFacts.NonEmptyStringFacts;
        }
        if ((flags & (TypeFlags.Number | TypeFlags.Enum)) != 0)
        {
            return _strictNullChecks ? TypeFacts.NumberStrictFacts : TypeFacts.NumberFacts;
        }
        if ((flags & TypeFlags.NumberLiteral) != 0)
        {
            var isZero = (double)((ILiteralType)type).Value! == 0;
            return _strictNullChecks
                ? isZero ? TypeFacts.ZeroNumberStrictFacts : TypeFacts.NonZeroNumberStrictFacts
                : isZero ? TypeFacts.ZeroNumberFacts : TypeFacts.NonZeroNumberFacts;
        }
        if ((flags & TypeFlags.BigInt) != 0)
        {
            return _strictNullChecks ? TypeFacts.BigIntStrictFacts : TypeFacts.BigIntFacts;
        }
        if ((flags & TypeFlags.BigIntLiteral) != 0)
        {
            var isZero = IsZeroBigInt((IBigIntLiteralType)type);
            return _strictNullChecks
                ? isZero ? TypeFacts.ZeroBigIntStrictFacts : TypeFacts.NonZeroBigIntStrictFacts
                : isZero ? TypeFacts.ZeroBigIntFacts : TypeFacts.NonZeroBigIntFacts;
        }
        if ((flags & TypeFlags.Boolean) != 0)
        {
            return _strictNullChecks ? TypeFacts.BooleanStrictFacts : TypeFacts.BooleanFacts;
        }
        if ((flags & TypeFlags.BooleanLike) != 0)
        {
            var isFalse = ReferenceEquals(type, _falseType) || ReferenceEquals(type, _regularFalseType);
            return _strictNullChecks
                ? isFalse ? TypeFacts.FalseStrictFacts : TypeFacts.TrueStrictFacts
                : isFalse ? TypeFacts.FalseFacts : TypeFacts.TrueFacts;
        }
        if ((flags & TypeFlags.Object) != 0)
        {
            var possibleFacts = _strictNullChecks
                ? TypeFacts.EmptyObjectStrictFacts | TypeFacts.FunctionStrictFacts | TypeFacts.ObjectStrictFacts
                : TypeFacts.EmptyObjectFacts | TypeFacts.FunctionFacts | TypeFacts.ObjectFacts;

            if ((callerOnlyNeeds & possibleFacts) == TypeFacts.None)
            {
                // If the caller doesn't care about any of the facts that we could possibly produce,
                // return zero so we can skip resolving members.
                return TypeFacts.None;
            }

            if ((GetObjectFlags(type) & ObjectFlags.Anonymous) != 0 && IsEmptyObjectType(type))
            {
                return _strictNullChecks ? TypeFacts.EmptyObjectStrictFacts : TypeFacts.EmptyObjectFacts;
            }
            if (IsFunctionObjectType((IObjectType)type))
            {
                return _strictNullChecks ? TypeFacts.FunctionStrictFacts : TypeFacts.FunctionFacts;
            }
            return _strictNullChecks ? TypeFacts.ObjectStrictFacts : TypeFacts.ObjectFacts;
        }
        if ((flags & TypeFlags.Void) != 0)
        {
            return TypeFacts.VoidFacts;
        }
        if ((flags & TypeFlags.Undefined) != 0)
        {
            return TypeFacts.UndefinedFacts;
        }
        if ((flags & TypeFlags.Null) != 0)
        {
            return TypeFacts.NullFacts;
        }
        if ((flags & TypeFlags.ESSymbolLike) != 0)
        {
            return _strictNullChecks ? TypeFacts.SymbolStrictFacts : TypeFacts.SymbolFacts;
        }
        if ((flags & TypeFlags.NonPrimitive) != 0)
        {
            return _strictNullChecks ? TypeFacts.ObjectStrictFacts : TypeFacts.ObjectFacts;
        }
        if ((flags & TypeFlags.Never) != 0)
        {
            return TypeFacts.None;
        }
        if ((flags & TypeFlags.Union) != 0)
        {
            return Core.ReduceLeft(((IUnionType)type).Types!, (facts, t, _) => facts | GetTypeFactsWorker(t, callerOnlyNeeds), TypeFacts.None);
        }
        if ((flags & TypeFlags.Intersection) != 0)
        {
            return GetIntersectionTypeFacts((IIntersectionType)type, callerOnlyNeeds);
        }
        return TypeFacts.UnknownFacts;
    }

    /// <summary>ported from: src/compiler/checker.ts:28243-28259 — <c>getIntersectionTypeFacts</c>.</summary>
    private TypeFacts GetIntersectionTypeFacts(IIntersectionType type, TypeFacts callerOnlyNeeds)
    {
        // When an intersection contains a primitive type we ignore object type constituents as they are
        // presumably type tags. For example, in string & { __kind__: "name" } we ignore the object type.
        var ignoreObjects = MaybeTypeOfKind(type, TypeFlags.Primitive);
        // When computing the type facts of an intersection type, certain type facts are computed as `and`
        // and others are computed as `or`.
        var oredFacts = TypeFacts.None;
        var andedFacts = TypeFacts.All;
        foreach (var t in type.Types!)
        {
            if (!(ignoreObjects && (t.Flags & TypeFlags.Object) != 0))
            {
                var f = GetTypeFactsWorker(t, callerOnlyNeeds);
                oredFacts |= f;
                andedFacts &= f;
            }
        }
        return oredFacts & TypeFacts.OrFactsMask | andedFacts & TypeFacts.AndFactsMask;
    }

    /// <summary>ported from: src/compiler/checker.ts:28261-28263 — <c>getTypeWithFacts</c>.</summary>
    internal IType GetTypeWithFacts(IType type, TypeFacts include) => FilterType(type, t => HasTypeFacts(t, include));

    /// <summary>ported from: src/compiler/checker.ts:28268-28282 — <c>getAdjustedTypeWithFacts</c>.</summary>
    internal IType GetAdjustedTypeWithFacts(IType type, TypeFacts facts)
    {
        var reduced = RecombineUnknownType(GetTypeWithFacts(_strictNullChecks && (type.Flags & TypeFlags.Unknown) != 0 ? _unknownUnionType : type, facts));
        if (_strictNullChecks)
        {
            switch (facts)
            {
                case TypeFacts.NEUndefined:
                    return RemoveNullableByIntersection(reduced, TypeFacts.EQUndefined, TypeFacts.EQNull, TypeFacts.IsNull, _nullType);
                case TypeFacts.NENull:
                    return RemoveNullableByIntersection(reduced, TypeFacts.EQNull, TypeFacts.EQUndefined, TypeFacts.IsUndefined, _undefinedType);
                case TypeFacts.NEUndefinedOrNull:
                case TypeFacts.Truthy:
                    return MapType(reduced, t => HasTypeFacts(t, TypeFacts.EQUndefinedOrNull) ? GetGlobalNonNullableTypeInstantiation(t) : t)!;
            }
        }
        return reduced;
    }

    /// <summary>ported from: src/compiler/checker.ts:28284-28296 — <c>removeNullableByIntersection</c>.</summary>
    private IType RemoveNullableByIntersection(IType type, TypeFacts targetFacts, TypeFacts otherFacts, TypeFacts otherIncludesFacts, IType otherType)
    {
        var facts = GetTypeFacts(type, TypeFacts.EQUndefined | TypeFacts.EQNull | TypeFacts.IsUndefined | TypeFacts.IsNull);
        // Simply return the type if it never compares equal to the target nullable.
        if ((facts & targetFacts) == TypeFacts.None)
        {
            return type;
        }
        // By default we intersect with a union of {} and the opposite nullable.
        var emptyAndOtherUnion = GetUnionType([_emptyObjectType, otherType]);
        // For each constituent type that can compare equal to the target nullable, intersect with the above union
        // if the type doesn't already include the opppsite nullable and the constituent can compare equal to the
        // opposite nullable; otherwise, just intersect with {}.
        return MapType(type, t => HasTypeFacts(t, targetFacts)
            ? GetIntersectionType([t, (facts & otherIncludesFacts) == TypeFacts.None && HasTypeFacts(t, otherFacts) ? emptyAndOtherUnion : _emptyObjectType])
            : t)!;
    }

    /// <summary>ported from: src/compiler/checker.ts:28298-28300 — <c>recombineUnknownType</c>.</summary>
    private IType RecombineUnknownType(IType type) => ReferenceEquals(type, _unknownUnionType) ? _unknownType : type;

    /// <summary>ported from: src/compiler/checker.ts:28321-28326 — <c>includeUndefinedInIndexSignature</c>.</summary>
    internal IType? IncludeUndefinedInIndexSignature(IType? type)
    {
        if (type is null)
        {
            return type;
        }
        return Js.IsTruthy(_compilerOptions.NoUncheckedIndexedAccess)
            ? GetUnionType([type, _missingType])
            : type;
    }

    /// <summary>ported from: src/compiler/checker.ts:27910-27913 — <c>tryGetNameFromType</c>.</summary>
    internal string? TryGetNameFromType(IType type)
    {
        // PORT-NOTE: TS's `"" + value` on the StringOrNumberLiteral arm splits into the two literal faces the port
        // reads the value through (§4.3), the same split `GetTemplateStringForType` makes.
        return (type.Flags & TypeFlags.UniqueESSymbol) != 0 ? ((IUniqueESSymbolType)type).EscapedName :
            (type.Flags & TypeFlags.StringOrNumberLiteral) != 0
                ? Utilities.EscapeLeadingUnderscores((type.Flags & TypeFlags.StringLiteral) != 0
                    ? (string)((ILiteralType)type).Value!
                    : Js.NumberToString((double)((ILiteralType)type).Value!))
                : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:27915-27918 — <c>tryGetElementAccessExpressionName</c>.</summary>
    private string? TryGetElementAccessExpressionName(ElementAccessExpression node)
    {
        return Utilities.IsStringOrNumericLiteralLike(node.ArgumentExpression!)
            ? Utilities.EscapeLeadingUnderscores(((LiteralExpression)node.ArgumentExpression!).Text ?? "")
            : Utilities.IsEntityNameExpression(node.ArgumentExpression!)
                ? TryGetNameFromEntityNameExpression(node.ArgumentExpression!)
                : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:27920-27945 — <c>tryGetNameFromEntityNameExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>EntityNameOrEntityNameExpression</c> union is <see cref="Node"/> here (§12.5), and
    /// <c>isBindingPattern(declaration.parent)</c> narrows the same way.
    /// </remarks>
    private string? TryGetNameFromEntityNameExpression(Node node)
    {
        var symbol = ResolveEntityName(node, SymbolFlags.Value, /*ignoreErrors*/ true);
        if (symbol is null || !(IsConstantVariable(symbol) || (symbol.Flags & SymbolFlags.EnumMember) != 0))
        {
            return null;
        }

        var declaration = symbol.ValueDeclaration;
        if (declaration is null)
        {
            return null;
        }

        var type = TryGetTypeFromEffectiveTypeNode((Node)declaration);
        if (type is not null)
        {
            var name = TryGetNameFromType(type);
            if (name is not null)
            {
                return name;
            }
        }
        if (Utilities.HasOnlyExpressionInitializer((Node)declaration) && IsBlockScopedNameDeclaredBeforeUse(declaration, node))
        {
            var initializer = Utilities.GetEffectiveInitializer((Node)declaration);
            if (initializer is not null)
            {
                var initializerType = Utilities.IsBindingPattern(((Node)declaration).Parent)
                    ? GetTypeForBindingElement((BindingElement)declaration)
                    : GetTypeOfExpression(initializer);
                return TryGetNameFromType(initializerType!);
            }
            if (NodeTests.IsEnumMember((Node)declaration))
            {
                return Utilities.GetTextOfPropertyName(((EnumMember)declaration).Name!);
            }
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:27947-27955 — <c>containsMatchingReference</c>.</summary>
    private bool ContainsMatchingReference(Node source, Node target)
    {
        while (Utilities.IsAccessExpression(source))
        {
            source = GetAccessExpressionOperand(source);
            if (IsMatchingReference(source, target))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:27957-27965 — <c>optionalChainContainsReference</c>.</summary>
    private bool OptionalChainContainsReference(Node source, Node target)
    {
        while (Utilities.IsOptionalChain(source))
        {
            source = GetOptionalChainOperand(source);
            if (IsMatchingReference(source, target))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:28074-28076 — <c>isOrContainsMatchingReference</c>.</summary>
    private bool IsOrContainsMatchingReference(Node source, Node target)
        => IsMatchingReference(source, target) || ContainsMatchingReference(source, target);

    /// <summary>ported from: src/compiler/checker.ts:28078-28093 — <c>hasMatchingArgument</c>.</summary>
    internal bool HasMatchingArgument(CallExpression expression, Node reference)
    {
        if (expression.Arguments is not null)
        {
            foreach (var argument in expression.Arguments)
            {
                if (IsOrContainsMatchingReference(reference, argument) || OptionalChainContainsReference(argument, reference))
                {
                    return true;
                }
            }
        }
        if (
            expression.Expression!.Kind == SyntaxKind.PropertyAccessExpression &&
            IsOrContainsMatchingReference(reference, ((PropertyAccessExpression)expression.Expression).Expression!)
        )
        {
            return true;
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:27795-27836 — <c>getFlowCacheKey</c>.</summary>
    private string? GetFlowCacheKey(Node node, IType declaredType, IType initialType, Node? flowContainer)
    {
        switch (node.Kind)
        {
            case SyntaxKind.Identifier:
                if (!Utilities.IsThisInTypeQuery(node))
                {
                    var symbol = GetResolvedSymbol((Identifier)node);
                    return !ReferenceEquals(symbol, _unknownSymbol)
                        ? $"{(flowContainer is not null ? Utilities.GetNodeId(flowContainer).ToString() : "-1")}|{GetTypeId(declaredType)}|{GetTypeId(initialType)}|{Utilities.GetSymbolId(symbol)}"
                        : null;
                }
                // falls through
                goto case SyntaxKind.ThisKeyword;
            case SyntaxKind.ThisKeyword:
                return $"0|{(flowContainer is not null ? Utilities.GetNodeId(flowContainer).ToString() : "-1")}|{GetTypeId(declaredType)}|{GetTypeId(initialType)}";
            case SyntaxKind.NonNullExpression:
            case SyntaxKind.ParenthesizedExpression:
                return GetFlowCacheKey(GetInnerExpression(node), declaredType, initialType, flowContainer);
            case SyntaxKind.QualifiedName:
                var left = GetFlowCacheKey(((QualifiedName)node).Left!, declaredType, initialType, flowContainer);
                return left is not null ? $"{left}.{((QualifiedName)node).Right!.EscapedText}" : null;
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ElementAccessExpression:
                var propName = GetAccessedPropertyName(node);
                if (propName is not null)
                {
                    var key = GetFlowCacheKey(GetAccessExpressionOperand(node), declaredType, initialType, flowContainer);
                    return key is not null ? $"{key}.{propName}" : null;
                }
                if (NodeTests.IsElementAccessExpression(node) && NodeTests.IsIdentifier(((ElementAccessExpression)node).ArgumentExpression!))
                {
                    var symbol = GetResolvedSymbol((Identifier)((ElementAccessExpression)node).ArgumentExpression!);
                    if (IsConstantVariable(symbol) || IsParameterOrMutableLocalVariable(symbol) && !IsSymbolAssigned(symbol))
                    {
                        var key = GetFlowCacheKey(GetAccessExpressionOperand(node), declaredType, initialType, flowContainer);
                        return key is not null ? $"{key}.@{Utilities.GetSymbolId(symbol)}" : null;
                    }
                }
                break;
            case SyntaxKind.ObjectBindingPattern:
            case SyntaxKind.ArrayBindingPattern:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.MethodDeclaration:
                // Handle pseudo-references originating in getNarrowedTypeOfSymbol.
                return $"{Utilities.GetNodeId(node)}#{GetTypeId(declaredType)}";
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:27838-27891 — <c>isMatchingReference</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>AccessExpression</c> union is <see cref="Node"/> here (§12.5) and every reader re-tests the
    /// kind before casting, exactly where the reference's helpers narrowed.
    /// </remarks>
    internal bool IsMatchingReference(Node source, Node target)
    {
        switch (target.Kind)
        {
            case SyntaxKind.ParenthesizedExpression:
            case SyntaxKind.NonNullExpression:
                return IsMatchingReference(source, GetInnerExpression(target));
            case SyntaxKind.BinaryExpression:
                return (Utilities.IsAssignmentExpression(target, /*excludeCompoundAssignment*/ false) && IsMatchingReference(source, ((BinaryExpression)target).Left!)) ||
                    (NodeTests.IsBinaryExpression(target) && ((BinaryExpression)target).OperatorToken!.Kind == SyntaxKind.CommaToken && IsMatchingReference(source, ((BinaryExpression)target).Right!));
        }
        switch (source.Kind)
        {
            case SyntaxKind.MetaProperty:
                return target.Kind == SyntaxKind.MetaProperty
                    && ((MetaProperty)source).KeywordToken == ((MetaProperty)target).KeywordToken
                    && Utilities.IdText(((MetaProperty)source).Name!) == Utilities.IdText(((MetaProperty)target).Name!);
            case SyntaxKind.Identifier:
            case SyntaxKind.PrivateIdentifier:
                return Utilities.IsThisInTypeQuery(source) ?
                    target.Kind == SyntaxKind.ThisKeyword :
                    target.Kind == SyntaxKind.Identifier && ReferenceEquals(GetResolvedSymbol((Identifier)source), GetResolvedSymbol((Identifier)target)) ||
                    (NodeTests.IsVariableDeclaration(target) || NodeTests.IsBindingElement(target)) &&
                        ReferenceEquals(GetExportSymbolOfValueSymbolIfExported(GetResolvedSymbol((Identifier)source)), GetSymbolOfDeclaration((IDeclaration)target));
            case SyntaxKind.ThisKeyword:
                return target.Kind == SyntaxKind.ThisKeyword;
            case SyntaxKind.SuperKeyword:
                return target.Kind == SyntaxKind.SuperKeyword;
            case SyntaxKind.NonNullExpression:
            case SyntaxKind.ParenthesizedExpression:
            case SyntaxKind.SatisfiesExpression:
                return IsMatchingReference(GetInnerExpression(source), target);
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ElementAccessExpression:
                var sourcePropertyName = GetAccessedPropertyName(source);
                if (sourcePropertyName is not null)
                {
                    var targetPropertyName = Utilities.IsAccessExpression(target) ? GetAccessedPropertyName(target) : null;
                    if (targetPropertyName is not null)
                    {
                        return targetPropertyName == sourcePropertyName && IsMatchingReference(GetAccessExpressionOperand(source), GetAccessExpressionOperand(target));
                    }
                }
                if (NodeTests.IsElementAccessExpression(source) && NodeTests.IsElementAccessExpression(target)
                    && NodeTests.IsIdentifier(((ElementAccessExpression)source).ArgumentExpression!) && NodeTests.IsIdentifier(((ElementAccessExpression)target).ArgumentExpression!))
                {
                    var symbol = GetResolvedSymbol((Identifier)((ElementAccessExpression)source).ArgumentExpression!);
                    if (ReferenceEquals(symbol, GetResolvedSymbol((Identifier)((ElementAccessExpression)target).ArgumentExpression!))
                        && (IsConstantVariable(symbol) || IsParameterOrMutableLocalVariable(symbol) && !IsSymbolAssigned(symbol)))
                    {
                        return IsMatchingReference(((ElementAccessExpression)source).Expression!, ((ElementAccessExpression)target).Expression!);
                    }
                }
                break;
            case SyntaxKind.QualifiedName:
                return Utilities.IsAccessExpression(target) &&
                    ((QualifiedName)source).Right!.EscapedText == GetAccessedPropertyName(target) &&
                    IsMatchingReference(((QualifiedName)source).Left!, GetAccessExpressionOperand(target));
            case SyntaxKind.BinaryExpression:
                return NodeTests.IsBinaryExpression(source) && ((BinaryExpression)source).OperatorToken!.Kind == SyntaxKind.CommaToken && IsMatchingReference(((BinaryExpression)source).Right!, target);
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:27893-27908 — <c>getAccessedPropertyName</c>.</summary>
    internal string? GetAccessedPropertyName(Node access)
    {
        if (NodeTests.IsPropertyAccessExpression(access))
        {
            return Utilities.IdText(((PropertyAccessExpression)access).Name!);
        }
        if (NodeTests.IsElementAccessExpression(access))
        {
            return TryGetElementAccessExpressionName((ElementAccessExpression)access);
        }
        if (NodeTests.IsBindingElement(access))
        {
            var name = GetDestructuringPropertyName(access);
            return name is not null ? Utilities.EscapeLeadingUnderscores(name) : null;
        }
        if (NodeTests.IsParameter(access))
        {
            // PORT-NOTE: TS's `access.parent.parameters.indexOf(access)` reads the parameter list off the declaring
            // function; the port's `Node.Parent` is nullable and typed `Node`, so the function face is named.
            return "" + ((IFunctionLikeDeclarationBase)((ParameterDeclaration)access).Parent!).Parameters!.IndexOf((ParameterDeclaration)access);
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:28395-28401 — <c>getTypeOfInitializer</c>.</summary>
    internal IType GetTypeOfInitializer(Expression node)
    {
        // Return the cached type if one is available. If the type of the variable was inferred
        // from its initializer, we'll already have cached the type. Otherwise we compute it now
        // without caching such that transient types are reflected.
        var links = GetNodeLinks(node);
        return links.ResolvedType ?? GetTypeOfExpression(node);
    }

    /// <summary>ported from: src/compiler/checker.ts:28302-28306 — <c>getTypeWithDefault</c>.</summary>
    private IType GetTypeWithDefault(IType type, Expression? defaultExpression)
    {
        return defaultExpression is not null
            ? GetUnionType([GetNonUndefinedType(type), GetTypeOfExpression(defaultExpression)])
            : type;
    }

    /// <summary>ported from: src/compiler/checker.ts:28308-28313 — <c>getTypeOfDestructuredProperty</c>.</summary>
    private IType GetTypeOfDestructuredProperty(IType type, Node name)
    {
        var nameType = GetLiteralTypeFromPropertyName(name);
        if (!Utilities.IsTypeUsableAsPropertyName(nameType))
        {
            return _errorType;
        }
        var text = Utilities.GetPropertyNameFromType(nameType);
        return GetTypeOfPropertyOfType(type, text) ?? IncludeUndefinedInIndexSignature(GetApplicableIndexInfoForName(type, text)?.Type) ?? _errorType;
    }

    /// <summary>ported from: src/compiler/checker.ts:28315-28319 — <c>getTypeOfDestructuredArrayElement</c>.</summary>
    private IType GetTypeOfDestructuredArrayElement(IType type, int index)
    {
        return EveryType(type, IsTupleLikeType) && GetTupleElementType(type, index) is { } tupleElement
            ? tupleElement
            : IncludeUndefinedInIndexSignature(CheckIteratedTypeOrElementType(IterationUse.Destructuring, type, _undefinedType, /*errorNode*/ null)) ?? _errorType;
    }

    /// <summary>ported from: src/compiler/checker.ts:28328-28330 — <c>getTypeOfDestructuredSpreadExpression</c>.</summary>
    private IType GetTypeOfDestructuredSpreadExpression(IType type)
        => CreateArrayType(CheckIteratedTypeOrElementType(IterationUse.Destructuring, type, _undefinedType, /*errorNode*/ null) ?? _errorType);

    /// <summary>ported from: src/compiler/checker.ts:28384-28393 — <c>getInitialTypeOfBindingElement</c>.</summary>
    private IType GetInitialTypeOfBindingElement(BindingElement node)
    {
        var pattern = (Node)node.Parent!;
        var parentType = GetInitialType((Node)pattern.Parent!);
        var type = pattern.Kind == SyntaxKind.ObjectBindingPattern
            ? GetTypeOfDestructuredProperty(parentType, node.PropertyName ?? node.Name!)
            : node.DotDotDotToken is null
                ? GetTypeOfDestructuredArrayElement(parentType, ((ArrayBindingPattern)pattern).Elements!.IndexOf(node))
                : GetTypeOfDestructuredSpreadExpression(parentType);
        return GetTypeWithDefault(type, node.Initializer!);
    }

    /// <summary>ported from: src/compiler/checker.ts:28403-28414 — <c>getInitialTypeOfVariableDeclaration</c>.</summary>
    private IType GetInitialTypeOfVariableDeclaration(VariableDeclaration node)
    {
        if (node.Initializer is not null)
        {
            return GetTypeOfInitializer(node.Initializer);
        }
        if (node.Parent!.Parent!.Kind == SyntaxKind.ForInStatement)
        {
            return _stringType;
        }
        if (node.Parent.Parent.Kind == SyntaxKind.ForOfStatement)
        {
            return CheckRightHandSideOfForOf((ForOfStatement)node.Parent.Parent) ?? _errorType;
        }
        return _errorType;
    }

    /// <summary>ported from: src/compiler/checker.ts:28416-28420 — <c>getInitialType</c>.</summary>
    private IType GetInitialType(Node node)
    {
        return node.Kind == SyntaxKind.VariableDeclaration
            ? GetInitialTypeOfVariableDeclaration((VariableDeclaration)node)
            : GetInitialTypeOfBindingElement((BindingElement)node);
    }

    /// <summary>ported from: src/compiler/checker.ts:28422-28427 — <c>isEmptyArrayAssignment</c>.</summary>
    private bool IsEmptyArrayAssignment(Node node)
    {
        return node.Kind == SyntaxKind.VariableDeclaration && ((VariableDeclaration)node).Initializer is not null
                && IsEmptyArrayLiteral(((VariableDeclaration)node).Initializer!)
            || node.Kind != SyntaxKind.BindingElement && node.Parent!.Kind == SyntaxKind.BinaryExpression
                && IsEmptyArrayLiteral(((BinaryExpression)node.Parent).Right!);
    }

    /// <summary>ported from: src/compiler/checker.ts:28332-28338 — <c>getAssignedTypeOfBinaryExpression</c>.</summary>
    private IType GetAssignedTypeOfBinaryExpression(BinaryExpression node)
    {
        var isDestructuringDefaultAssignment = node.Parent!.Kind == SyntaxKind.ArrayLiteralExpression && IsDestructuringAssignmentTarget(node.Parent)
            || node.Parent.Kind == SyntaxKind.PropertyAssignment && IsDestructuringAssignmentTarget(node.Parent.Parent!);
        return isDestructuringDefaultAssignment
            ? GetTypeWithDefault(GetAssignedType(node), node.Right!)
            : GetTypeOfExpression(node.Right!);
    }

    /// <summary>ported from: src/compiler/checker.ts:28340-28343 — <c>isDestructuringAssignmentTarget</c>.</summary>
    private bool IsDestructuringAssignmentTarget(Node parent)
        => parent.Parent!.Kind == SyntaxKind.BinaryExpression && ((BinaryExpression)parent.Parent).Left == parent
            || parent.Parent.Kind == SyntaxKind.ForOfStatement && ((ForOfStatement)parent.Parent).Initializer == parent;

    /// <summary>ported from: src/compiler/checker.ts:28345-28347 — <c>getAssignedTypeOfArrayLiteralElement</c>.</summary>
    private IType GetAssignedTypeOfArrayLiteralElement(ArrayLiteralExpression node, Expression element)
        => GetTypeOfDestructuredArrayElement(GetAssignedType(node), node.Elements!.IndexOf(element));

    /// <summary>ported from: src/compiler/checker.ts:28349-28351 — <c>getAssignedTypeOfSpreadExpression</c>.</summary>
    private IType GetAssignedTypeOfSpreadExpression(SpreadElement node)
        => GetTypeOfDestructuredSpreadExpression(GetAssignedType((ArrayLiteralExpression)node.Parent!));

    /// <summary>ported from: src/compiler/checker.ts:28353-28355 — <c>getAssignedTypeOfPropertyAssignment</c>.</summary>
    private IType GetAssignedTypeOfPropertyAssignment(Node node)
        => GetTypeOfDestructuredProperty(GetAssignedType((Node)node.Parent!), GetObjectLiteralMemberName(node));

    /// <summary>ported from: src/compiler/checker.ts:28357-28359 — <c>getAssignedTypeOfShorthandPropertyAssignment</c>.</summary>
    private IType GetAssignedTypeOfShorthandPropertyAssignment(ShorthandPropertyAssignment node)
        => GetTypeWithDefault(GetAssignedTypeOfPropertyAssignment(node), node.ObjectAssignmentInitializer!);

    /// <summary>ported from: src/compiler/checker.ts:28361-28382 — <c>getAssignedType</c>.</summary>
    private IType GetAssignedType(Node node)
    {
        var parent = (Node)node.Parent!;
        switch (parent.Kind)
        {
            case SyntaxKind.ForInStatement:
                return _stringType;
            case SyntaxKind.ForOfStatement:
                return CheckRightHandSideOfForOf((ForOfStatement)parent) ?? _errorType;
            case SyntaxKind.BinaryExpression:
                return GetAssignedTypeOfBinaryExpression((BinaryExpression)parent);
            case SyntaxKind.DeleteExpression:
                return _undefinedType;
            case SyntaxKind.ArrayLiteralExpression:
                return GetAssignedTypeOfArrayLiteralElement((ArrayLiteralExpression)parent, (Expression)node);
            case SyntaxKind.SpreadElement:
                return GetAssignedTypeOfSpreadExpression((SpreadElement)parent);
            case SyntaxKind.PropertyAssignment:
                return GetAssignedTypeOfPropertyAssignment((PropertyAssignment)parent);
            case SyntaxKind.ShorthandPropertyAssignment:
                return GetAssignedTypeOfShorthandPropertyAssignment((ShorthandPropertyAssignment)parent);
        }
        return _errorType;
    }

    /// <summary>ported from: src/compiler/checker.ts:28429-28445 — <c>getReferenceCandidate</c>.</summary>
    internal Expression GetReferenceCandidate(Expression node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.ParenthesizedExpression:
                return GetReferenceCandidate(((ParenthesizedExpression)node).Expression!);
            case SyntaxKind.BinaryExpression:
                switch (((BinaryExpression)node).OperatorToken!.Kind)
                {
                    case SyntaxKind.EqualsToken:
                    case SyntaxKind.BarBarEqualsToken:
                    case SyntaxKind.AmpersandAmpersandEqualsToken:
                    case SyntaxKind.QuestionQuestionEqualsToken:
                        return GetReferenceCandidate(((BinaryExpression)node).Left!);
                    case SyntaxKind.CommaToken:
                        return GetReferenceCandidate(((BinaryExpression)node).Right!);
                }
                break;
        }
        return node;
    }

    /// <summary>ported from: src/compiler/checker.ts:28447-28453 — <c>getReferenceRoot</c>.</summary>
    private Node GetReferenceRoot(Node node)
    {
        var parent = (Node)node.Parent!;
        return parent.Kind == SyntaxKind.ParenthesizedExpression ||
                parent.Kind == SyntaxKind.BinaryExpression && ((BinaryExpression)parent).OperatorToken!.Kind == SyntaxKind.EqualsToken && ((BinaryExpression)parent).Left == node ||
                parent.Kind == SyntaxKind.BinaryExpression && ((BinaryExpression)parent).OperatorToken!.Kind == SyntaxKind.CommaToken && ((BinaryExpression)parent).Right == node
            ? GetReferenceRoot(parent)
            : node;
    }

    /// <summary>ported from: src/compiler/checker.ts:28455-28460 — <c>getTypeOfSwitchClause</c>.</summary>
    private IType GetTypeOfSwitchClause(Node clause)
    {
        if (clause.Kind == SyntaxKind.CaseClause)
        {
            return GetRegularTypeOfLiteralType(GetTypeOfExpression(((CaseClause)clause).Expression!));
        }
        return _neverType;
    }

    /// <summary>ported from: src/compiler/checker.ts:28462-28471 — <c>getSwitchClauseTypes</c>.</summary>
    internal List<IType> GetSwitchClauseTypes(SwitchStatement switchStatement)
    {
        var links = GetNodeLinks(switchStatement);
        if (links.SwitchTypes is null)
        {
            links.SwitchTypes = [];
            foreach (var clause in switchStatement.CaseBlock!.Clauses!)
            {
                links.SwitchTypes.Add(GetTypeOfSwitchClause(clause));
            }
        }
        return links.SwitchTypes;
    }

    /// <summary>ported from: src/compiler/checker.ts:28475-28485 — <c>getSwitchClauseTypeOfWitnesses</c>.</summary>
    internal List<string?>? GetSwitchClauseTypeOfWitnesses(SwitchStatement switchStatement)
    {
        if (Core.Some(switchStatement.CaseBlock!.Clauses!, clause => clause.Kind == SyntaxKind.CaseClause && !Utilities.IsStringLiteralLike(((CaseClause)clause).Expression!)))
        {
            return null;
        }
        var witnesses = new List<string?>();
        foreach (var clause in switchStatement.CaseBlock.Clauses!)
        {
            var text = clause.Kind == SyntaxKind.CaseClause ? ((LiteralExpression)((CaseClause)clause).Expression!).Text : null;
            witnesses.Add(text is not null && !Core.Contains(witnesses, text) ? text : null);
        }
        return witnesses;
    }

    /// <summary>ported from: src/compiler/checker.ts:28487-28489 — <c>eachTypeContainedIn</c>.</summary>
    internal bool EachTypeContainedIn(IType source, List<IType> types)
    {
        return (source.Flags & TypeFlags.Union) != 0
            ? !Core.Some(((IUnionType)source).Types!, t => !Core.Contains(types, t))
            : Core.Contains(types, source);
    }

    /// <summary>ported from: src/compiler/checker.ts:28491-28493 — <c>isTypeSubsetOf</c>.</summary>
    private bool IsTypeSubsetOf(IType source, IType target)
    {
        return ReferenceEquals(source, target) || (source.Flags & TypeFlags.Never) != 0
            || (target.Flags & TypeFlags.Union) != 0 && IsTypeSubsetOfUnion(source, (IUnionType)target);
    }

    /// <summary>ported from: src/compiler/checker.ts:28495-28508 — <c>isTypeSubsetOfUnion</c>.</summary>
    private bool IsTypeSubsetOfUnion(IType source, IUnionType target)
    {
        if ((source.Flags & TypeFlags.Union) != 0)
        {
            foreach (var t in ((IUnionType)source).Types!)
            {
                if (!ContainsType(target.Types!, t))
                {
                    return false;
                }
            }
            return true;
        }
        if ((source.Flags & TypeFlags.EnumLike) != 0 && ReferenceEquals(GetBaseTypeOfEnumLikeType(source), target))
        {
            return true;
        }
        return ContainsType(target.Types!, source);
    }

    /// <summary>ported from: src/compiler/checker.ts:28641-28645 — <c>createEvolvingArrayType</c>.</summary>
    private IEvolvingArrayType CreateEvolvingArrayType(IType elementType)
    {
        var result = (IEvolvingArrayType)CreateObjectType(ObjectFlags.EvolvingArray);
        result.ElementType = elementType;
        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:28647-28649 — <c>getEvolvingArrayType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>evolvingArrayTypes[elementType.id]</c> is a sparse-array read in the reference: an id the table has
    /// not reached yet is <c>undefined</c>, and the write extends the array, filling the gap with holes. The port's
    /// <c>List</c> is grown on write and the read is guarded, which is the same two answers without the throw a list
    /// index would raise — the same treatment <c>Checker/Checker.Check.Expressions.cs</c>'s <c>GetTypeOfExpression</c>
    /// records for <c>flowTypeCache</c> (checker.ts:41691/41701).
    /// </remarks>
    private IEvolvingArrayType GetEvolvingArrayType(IType elementType)
    {
        var id = elementType.Id;
        var existing = id < _evolvingArrayTypes.Count ? _evolvingArrayTypes[id] : null;
        if (existing is not null)
        {
            return existing;
        }
        var created = CreateEvolvingArrayType(elementType);
        while (_evolvingArrayTypes.Count <= id)
        {
            _evolvingArrayTypes.Add(null!);
        }
        _evolvingArrayTypes[id] = created;
        return created;
    }

    /// <summary>ported from: src/compiler/checker.ts:28654-28657 — <c>addEvolvingArrayElementType</c>.</summary>
    private IEvolvingArrayType AddEvolvingArrayElementType(IEvolvingArrayType evolvingArrayType, Expression node)
    {
        var elementType = GetRegularTypeOfObjectLiteral(GetBaseTypeOfLiteralType(GetContextFreeTypeOfExpression(node)));
        return IsTypeSubsetOf(elementType, evolvingArrayType.ElementType!)
            ? evolvingArrayType
            : GetEvolvingArrayType(GetUnionType([evolvingArrayType.ElementType!, elementType]));
    }

    /// <summary>ported from: src/compiler/checker.ts:28659-28667 — <c>createFinalArrayType</c>.</summary>
    private IType CreateFinalArrayType(IType elementType)
    {
        return (elementType.Flags & TypeFlags.Never) != 0
            ? _autoArrayType
            : CreateArrayType(
                (elementType.Flags & TypeFlags.Union) != 0
                    ? GetUnionType(((IUnionType)elementType).Types!, UnionReduction.Subtype)
                    : elementType);
    }

    /// <summary>ported from: src/compiler/checker.ts:28670-28672 — <c>getFinalArrayType</c>.</summary>
    private IType GetFinalArrayType(IEvolvingArrayType evolvingArrayType)
        => evolvingArrayType.FinalArrayType ?? (evolvingArrayType.FinalArrayType = CreateFinalArrayType(evolvingArrayType.ElementType!));

    /// <summary>ported from: src/compiler/checker.ts:28674-28676 — <c>finalizeEvolvingArrayType</c>.</summary>
    private IType FinalizeEvolvingArrayType(IType type)
        => (GetObjectFlags(type) & ObjectFlags.EvolvingArray) != 0 ? GetFinalArrayType((IEvolvingArrayType)type) : type;

    /// <summary>ported from: src/compiler/checker.ts:28678-28680 — <c>getElementTypeOfEvolvingArrayType</c>.</summary>
    private IType GetElementTypeOfEvolvingArrayType(IType type)
        => (type.Flags & TypeFlags.Object) != 0 && (GetObjectFlags(type) & ObjectFlags.EvolvingArray) != 0
            ? ((IEvolvingArrayType)type).ElementType!
            : _neverType;

    /// <summary>ported from: src/compiler/checker.ts:28682-28693 — <c>isEvolvingArrayTypeList</c>.</summary>
    private bool IsEvolvingArrayTypeList(List<IType> types)
    {
        var hasEvolvingArrayType = false;
        foreach (var t in types)
        {
            if ((t.Flags & TypeFlags.Never) == 0)
            {
                if ((GetObjectFlags(t) & ObjectFlags.EvolvingArray) == 0)
                {
                    return false;
                }
                hasEvolvingArrayType = true;
            }
        }
        return hasEvolvingArrayType;
    }

    /// <summary>ported from: src/compiler/checker.ts:28697-28714 — <c>isEvolvingArrayOperationTarget</c>.</summary>
    private bool IsEvolvingArrayOperationTarget(Node node)
    {
        var root = GetReferenceRoot(node);
        var parent = (Node)root.Parent!;
        var isLengthPushOrUnshift = NodeTests.IsPropertyAccessExpression(parent) && (
            Utilities.IdText(((PropertyAccessExpression)parent).Name!) == "length" ||
            parent.Parent!.Kind == SyntaxKind.CallExpression
                && NodeTests.IsIdentifier(((PropertyAccessExpression)parent).Name!)
                && Utilities.IsPushOrUnshiftIdentifier((Identifier)((PropertyAccessExpression)parent).Name!)
        );
        var isElementAssignment = parent.Kind == SyntaxKind.ElementAccessExpression &&
            ((ElementAccessExpression)parent).Expression == root &&
            parent.Parent!.Kind == SyntaxKind.BinaryExpression &&
            ((BinaryExpression)parent.Parent).OperatorToken!.Kind == SyntaxKind.EqualsToken &&
            ((BinaryExpression)parent.Parent).Left == parent &&
            !Utilities.IsAssignmentTarget((BinaryExpression)parent.Parent) &&
            IsTypeAssignableToKind(GetTypeOfExpression(((ElementAccessExpression)parent).ArgumentExpression!), TypeFlags.NumberLike);
        return isLengthPushOrUnshift || isElementAssignment;
    }

    /// <summary>ported from: src/compiler/checker.ts:28716-28720 — <c>isDeclarationWithExplicitTypeAnnotation</c>.</summary>
    private bool IsDeclarationWithExplicitTypeAnnotation(IDeclaration node)
    {
        return (NodeTests.IsVariableDeclaration((Node)node) || NodeTests.IsPropertyDeclaration((Node)node) || NodeTests.IsPropertySignature((Node)node) || NodeTests.IsParameter((Node)node)) &&
            !!(Utilities.GetEffectiveTypeAnnotationNode((Node)node) is not null ||
                Utilities.IsInJSFile((Node)node) && Utilities.HasInitializer((Node)node) && ((VariableDeclaration)node).Initializer is not null
                    && Utilities.IsFunctionExpressionOrArrowFunction(((VariableDeclaration)node).Initializer!)
                    && Utilities.GetEffectiveReturnTypeNode(((VariableDeclaration)node).Initializer!) is not null);
    }

    /// <summary>ported from: src/compiler/checker.ts:28722-28752 — <c>getExplicitTypeOfSymbol</c>.</summary>
    private IType? GetExplicitTypeOfSymbol(ISymbol symbol, Diagnostic? diagnostic = null)
    {
        symbol = ResolveSymbol(symbol)!;
        if ((symbol.Flags & (SymbolFlags.Function | SymbolFlags.Method | SymbolFlags.Class | SymbolFlags.ValueModule)) != 0)
        {
            return GetTypeOfSymbol(symbol);
        }
        if ((symbol.Flags & (SymbolFlags.Variable | SymbolFlags.Property)) != 0)
        {
            if ((GetCheckFlags(symbol) & CheckFlags.Mapped) != 0)
            {
                var origin = GetSymbolLinks(symbol).SyntheticOrigin;
                if (origin is not null && GetExplicitTypeOfSymbol(origin) is not null)
                {
                    return GetTypeOfSymbol(symbol);
                }
            }
            var declaration = symbol.ValueDeclaration;
            if (declaration is not null)
            {
                if (IsDeclarationWithExplicitTypeAnnotation(declaration))
                {
                    return GetTypeOfSymbol(symbol);
                }
                if (NodeTests.IsVariableDeclaration((Node)declaration) && ((VariableDeclaration)declaration).Parent!.Parent!.Kind == SyntaxKind.ForOfStatement)
                {
                    var statement = (ForOfStatement)((VariableDeclaration)declaration).Parent!.Parent!;
                    var expressionType = GetTypeOfDottedName(statement.Expression!, /*diagnostic*/ null);
                    if (expressionType is not null)
                    {
                        var use = statement.AwaitModifier is not null ? IterationUse.ForAwaitOf : IterationUse.ForOf;
                        return CheckIteratedTypeOrElementType(use, expressionType, _undefinedType, /*errorNode*/ null);
                    }
                }
                if (diagnostic is not null)
                {
                    // PORT-NOTE (A″): printer-gated stringifier arguments are wrapped in Diagnostic.DeferArg (Core/Diagnostic.cs, DiagnosticArgument) so the M7 gate is reached only by a reader that asks for the message text.
                    Utilities.AddRelatedInfo(diagnostic, Utilities.CreateDiagnosticForNode((Node)declaration, Diagnostics._0_needs_an_explicit_type_annotation, Diagnostic.DeferArg(() => SymbolToString(symbol))));
                }
            }
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:28828-28831 — <c>hasTypePredicateOrNeverReturnType</c>.</summary>
    internal bool HasTypePredicateOrNeverReturnType(ISignature signature)
    {
        return GetTypePredicateOfSignature(signature) is not null ||
            signature.Declaration is not null && ((GetReturnTypeFromAnnotation((Node)signature.Declaration) ?? _unknownType).Flags & TypeFlags.Never) != 0;
    }

    /// <summary>ported from: src/compiler/checker.ts:28833-28839 — <c>getTypePredicateArgument</c>.</summary>
    private Node? GetTypePredicateArgument(ITypePredicateBase predicate, CallExpression callExpression)
    {
        if (predicate.Kind is TypePredicateKind.Identifier or TypePredicateKind.AssertsIdentifier)
        {
            return callExpression.Arguments![((IIdentifierTypePredicate)predicate).ParameterIndex];
        }
        var invokedExpression = Utilities.SkipParentheses(callExpression.Expression!, /*excludeJSDocTypeAssertions*/ false);
        return Utilities.IsAccessExpression(invokedExpression) ? Utilities.SkipParentheses(GetAccessExpressionOperand(invokedExpression), /*excludeJSDocTypeAssertions*/ false) : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:28841-28846 — <c>reportFlowControlError</c>.</summary>
    private void ReportFlowControlError(Node node)
    {
        var block = (Node)Utilities.FindAncestor(node, Utilities.IsFunctionOrModuleBlock)!;
        var sourceFile = Utilities.GetSourceFileOfNode(node)!;
        var span = Utilities.GetSpanOfTokenAtPosition(sourceFile, GetStatementsOfBlock(block).Pos);
        _diagnostics.Add(Utilities.CreateFileDiagnostic(sourceFile, span.Start, span.Length, Diagnostics.The_containing_function_or_module_body_is_too_large_for_control_flow_analysis));
    }

    /// <summary>ported from: src/compiler/checker.ts:28848-28853 — <c>isReachableFlowNode</c>.</summary>
    internal bool IsReachableFlowNode(FlowNode flow)
    {
        var result = IsReachableFlowNodeWorker(flow, /*noCacheCheck*/ false);
        _lastFlowNode = flow;
        _lastFlowNodeReachable = result;
        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:28855-28861 — <c>isFalseExpression</c>.</summary>
    private bool IsFalseExpression(Expression expr)
    {
        var node = Utilities.SkipParentheses(expr, /*excludeJSDocTypeAssertions*/ true);
        return node.Kind == SyntaxKind.FalseKeyword || node.Kind == SyntaxKind.BinaryExpression && (
                    ((BinaryExpression)node).OperatorToken!.Kind == SyntaxKind.AmpersandAmpersandToken && (IsFalseExpression(((BinaryExpression)node).Left!) || IsFalseExpression(((BinaryExpression)node).Right!)) ||
                    ((BinaryExpression)node).OperatorToken!.Kind == SyntaxKind.BarBarToken && IsFalseExpression(((BinaryExpression)node).Left!) && IsFalseExpression(((BinaryExpression)node).Right!)
                );
    }

    /// <summary>ported from: src/compiler/checker.ts:28863-28931 — <c>isReachableFlowNodeWorker</c>.</summary>
    private bool IsReachableFlowNodeWorker(FlowNode flow, bool noCacheCheck)
    {
        while (true)
        {
            if (ReferenceEquals(flow, _lastFlowNode))
            {
                return _lastFlowNodeReachable;
            }
            var flags = flow.Flags;
            if ((flags & FlowFlags.Shared) != 0)
            {
                if (!noCacheCheck)
                {
                    var id = GetFlowNodeId(flow);
                    var reachable = GetFlowSlot(_flowNodeReachable, id);
                    if (reachable is not null)
                    {
                        return reachable.Value;
                    }
                    var computed = IsReachableFlowNodeWorker(flow, /*noCacheCheck*/ true);
                    SetFlowSlot(_flowNodeReachable, id, computed);
                    return computed;
                }
                noCacheCheck = false;
            }
            if ((flags & (FlowFlags.Assignment | FlowFlags.Condition | FlowFlags.ArrayMutation)) != 0)
            {
                flow = (FlowNode)((FlowNode)flow).Antecedent!;
            }
            else if ((flags & FlowFlags.Call) != 0)
            {
                var signature = GetEffectsSignature((Node)flow.Node!);
                if (signature is not null)
                {
                    var predicate = GetTypePredicateOfSignature(signature);
                    if (predicate is not null && predicate.Kind == TypePredicateKind.AssertsIdentifier && predicate.Type is null)
                    {
                        var predicateArgument = ((CallExpression)flow.Node!).Arguments![((IIdentifierTypePredicate)predicate).ParameterIndex];
                        if (predicateArgument is not null && IsFalseExpression(predicateArgument))
                        {
                            return false;
                        }
                    }
                    if ((GetReturnTypeOfSignature(signature).Flags & TypeFlags.Never) != 0)
                    {
                        return false;
                    }
                }
                flow = (FlowNode)((FlowNode)flow).Antecedent!;
            }
            else if ((flags & FlowFlags.BranchLabel) != 0)
            {
                // A branching point is reachable if any branch is reachable.
                return Core.Some((List<FlowNode>)((FlowNode)flow).Antecedent!, f => IsReachableFlowNodeWorker(f, /*noCacheCheck*/ false));
            }
            else if ((flags & FlowFlags.LoopLabel) != 0)
            {
                var antecedents = (List<FlowNode>?)((FlowNode)flow).Antecedent;
                if (antecedents is null || antecedents.Count == 0)
                {
                    return false;
                }
                // A loop is reachable if the control flow path that leads to the top is reachable.
                flow = antecedents[0];
            }
            else if ((flags & FlowFlags.SwitchClause) != 0)
            {
                // The control flow path representing an unmatched value in a switch statement with
                // no default clause is unreachable if the switch statement is exhaustive.
                var data = (FlowSwitchClauseData)((FlowNode)flow).Node!;
                if (data.ClauseStart == data.ClauseEnd && IsExhaustiveSwitchStatement(data.SwitchStatement!))
                {
                    return false;
                }
                flow = (FlowNode)((FlowNode)flow).Antecedent!;
            }
            else if ((flags & FlowFlags.ReduceLabel) != 0)
            {
                // Cache is unreliable once we start adjusting labels
                _lastFlowNode = null;
                var data = (FlowReduceLabelData)flow.Node!;
                var target = (FlowNode)data.Target!;
                var saveAntecedents = target.Antecedent;
                target.Antecedent = data.Antecedents;
                var result = IsReachableFlowNodeWorker((FlowNode)flow.Antecedent!, /*noCacheCheck*/ false);
                target.Antecedent = saveAntecedents;
                return result;
            }
            else
            {
                return (flags & FlowFlags.Unreachable) == 0;
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:28978-29002 — <c>isConstantReference</c>.</summary>
    internal bool IsConstantReference(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.ThisKeyword:
                return true;
            case SyntaxKind.Identifier:
                if (!Utilities.IsThisInTypeQuery(node))
                {
                    var symbol = GetResolvedSymbol((Identifier)node);
                    return IsConstantVariable(symbol)
                        || IsParameterOrMutableLocalVariable(symbol) && !IsSymbolAssigned(symbol)
                        || symbol.ValueDeclaration is not null && NodeTests.IsFunctionExpression((Node)symbol.ValueDeclaration);
                }
                break;
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ElementAccessExpression:
                // The resolvedSymbol property is initialized by checkPropertyAccess or checkElementAccess before we get here.
                return IsConstantReference(GetAccessExpressionOperand(node)) && IsReadonlySymbol(GetNodeLinks(node).ResolvedSymbol ?? _unknownSymbol);
            case SyntaxKind.ObjectBindingPattern:
            case SyntaxKind.ArrayBindingPattern:
                var rootDeclaration = Utilities.GetRootDeclaration((Node)node.Parent!);
                return NodeTests.IsParameter(rootDeclaration) || Utilities.IsCatchClauseVariableDeclaration(rootDeclaration)
                    ? !IsSomeSymbolAssigned(rootDeclaration)
                    : NodeTests.IsVariableDeclaration(rootDeclaration) && IsVarConstLike(rootDeclaration);
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:29004-30222 — <c>getFlowTypeOfReference</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE (the defaults, checker.ts:29004): TS declares <c>initialType = declaredType</c> and
    /// <c>flowNode = tryCast(reference, canHaveFlowNode)?.flowNode</c>. A JS default applies to an explicitly passed
    /// <c>undefined</c> as well as to an omitted argument, so both are stated here as the nullable parameter plus
    /// <c>??=</c>, which is exactly that rule; the two callers that pass a <c>location.flowNode</c> that may be
    /// <c>undefined</c> (checker.ts:30962, 31001) therefore keep the reference's behaviour.
    /// </para>
    /// <para>
    /// PORT-NOTE (the nested functions): TS's nested function declarations are C# local functions (names capitalised).
    /// The reference's mutable locals — <c>key</c>, <c>isKeySet</c>, <c>flowDepth</c> and <c>sharedFlowStart</c> — are
    /// captured by them, so the flow-type cache and the depth/loop accounting keep the reference's single shared
    /// state.
    /// </para>
    /// <para>
    /// PORT-NOTE (<c>Complete</c>): TS's <c>FlowType = Type | IncompleteType</c> (types.ts:4267) makes a bare
    /// <c>Type</c> a <c>FlowType</c>, so the reference's <c>FlowType</c>-typed positions return bare <c>Type</c>s and
    /// let them flow unwrapped (<c>type = initialType</c>, <c>return declaredType</c>, …). The port's
    /// <see cref="FlowType"/> is a class, so those positions wrap the value through this local function. The wrap is
    /// observationally the same: <c>Flags</c> becomes the type's own flags, which are never <c>0</c> — the incomplete
    /// marker — so <see cref="IsIncomplete"/> answers what the reference's <c>x.flags === 0</c> answered, and
    /// <see cref="GetTypeFromFlowType"/> hands back the same type.
    /// </para>
    /// </remarks>
    internal IType GetFlowTypeOfReference(Node reference, IType declaredType, IType? initialType = null, Node? flowContainer = null, FlowNode? flowNode = null)
    {
        initialType ??= declaredType;
        flowNode ??= Utilities.CanHaveFlowNode(reference) ? ((IFlowContainer)reference).FlowNode : null;
        string? key = null;
        var isKeySet = false;
        var flowDepth = 0;
        if (_flowAnalysisDisabled)
        {
            return _errorType;
        }
        if (flowNode is null)
        {
            return declaredType;
        }
        _flowInvocationCount++;
        var sharedFlowStart = _sharedFlowCount;
        var evolvedType = GetTypeFromFlowType(GetTypeAtFlowNode(flowNode));
        _sharedFlowCount = sharedFlowStart;
        // When the reference is 'x' in an 'x.length', 'x.push(value)', 'x.unshift(value)' or x[n] = value' operation,
        // we give type 'any[]' to 'x' instead of using the type determined by control flow analysis such that operations
        // on empty arrays are possible without implicit any errors and new element types can be inferred without
        // type mismatch errors.
        var resultType = (GetObjectFlags(evolvedType) & ObjectFlags.EvolvingArray) != 0 && IsEvolvingArrayOperationTarget(reference) ? _autoArrayType : FinalizeEvolvingArrayType(evolvedType);
        if (ReferenceEquals(resultType, _unreachableNeverType) || reference.Parent is not null && reference.Parent.Kind == SyntaxKind.NonNullExpression && (resultType.Flags & TypeFlags.Never) == 0 && (GetTypeWithFacts(resultType, TypeFacts.NEUndefinedOrNull).Flags & TypeFlags.Never) != 0)
        {
            return declaredType;
        }
        return resultType;

        static FlowType Complete(IType type) => new(type, /*incomplete*/ false);

        string? GetOrSetCacheKey()
        {
            if (isKeySet)
            {
                return key;
            }

            isKeySet = true;
            return key = GetFlowCacheKey(reference, declaredType, initialType, flowContainer);
        }

        FlowType GetTypeAtFlowNode(FlowNode flow)
        {
            if (flowDepth == 2000)
            {
                // We have made 2000 recursive invocations. To avoid overflowing the call stack we report an error
                // and disable further control flow analysis in the containing function or module body.
                Tracing.Current?.Instant(
                    Tracing.Phase.CheckTypes,
                    "getTypeAtFlowNode_DepthLimit",
                    TracingArgs.Of(("flowId", flow.Id)));
                _flowAnalysisDisabled = true;
                ReportFlowControlError(reference);
                return Complete(_errorType);
            }

            flowDepth++;
            FlowNode? sharedFlow = null;
            while (true)
            {
                var flags = flow.Flags;
                if ((flags & FlowFlags.Shared) != 0)
                {
                    // We cache results of flow type resolution for shared nodes that were previously visited in
                    // the same getFlowTypeOfReference invocation. A node is considered shared when it is the
                    // antecedent of more than one node.
                    for (var i = sharedFlowStart; i < _sharedFlowCount; i++)
                    {
                        if (ReferenceEquals(GetFlowSlot(_sharedFlowNodes, i), flow))
                        {
                            flowDepth--;
                            return GetFlowSlot(_sharedFlowTypes, i)!;
                        }
                    }

                    sharedFlow = flow;
                }

                FlowType? type;
                if ((flags & FlowFlags.Assignment) != 0)
                {
                    type = GetTypeAtFlowAssignment(flow);
                    if (type is null)
                    {
                        flow = (FlowNode)flow.Antecedent!;
                        continue;
                    }
                }
                else if ((flags & FlowFlags.Call) != 0)
                {
                    type = GetTypeAtFlowCall(flow);
                    if (type is null)
                    {
                        flow = (FlowNode)flow.Antecedent!;
                        continue;
                    }
                }
                else if ((flags & FlowFlags.Condition) != 0)
                {
                    type = GetTypeAtFlowCondition(flow);
                }
                else if ((flags & FlowFlags.SwitchClause) != 0)
                {
                    type = GetTypeAtSwitchClause(flow);
                }
                else if ((flags & FlowFlags.Label) != 0)
                {
                    if (((List<FlowNode>)flow.Antecedent!).Count == 1)
                    {
                        flow = ((List<FlowNode>)flow.Antecedent!)[0];
                        continue;
                    }

                    type = (flags & FlowFlags.BranchLabel) != 0
                        ? GetTypeAtFlowBranchLabel(flow)
                        : GetTypeAtFlowLoopLabel(flow);
                }
                else if ((flags & FlowFlags.ArrayMutation) != 0)
                {
                    type = GetTypeAtFlowArrayMutation(flow);
                    if (type is null)
                    {
                        flow = (FlowNode)flow.Antecedent!;
                        continue;
                    }
                }
                else if ((flags & FlowFlags.ReduceLabel) != 0)
                {
                    var data = (FlowReduceLabelData)flow.Node!;
                    var target = (FlowNode)data.Target!;
                    var saveAntecedents = target.Antecedent;
                    target.Antecedent = data.Antecedents;
                    type = GetTypeAtFlowNode((FlowNode)flow.Antecedent!);
                    target.Antecedent = saveAntecedents;
                }
                else if ((flags & FlowFlags.Start) != 0)
                {
                    // Check if we should continue with the control flow of the containing function.
                    var container = flow.Node as Node;
                    if (
                        container is not null && !ReferenceEquals(container, flowContainer) &&
                        reference.Kind != SyntaxKind.PropertyAccessExpression &&
                        reference.Kind != SyntaxKind.ElementAccessExpression &&
                        !(reference.Kind == SyntaxKind.ThisKeyword && container.Kind != SyntaxKind.ArrowFunction)
                    )
                    {
                        flow = ((IFlowContainer)container).FlowNode!;
                        continue;
                    }

                    // At the top of the flow we have the initial type.
                    type = Complete(initialType);
                }
                else
                {
                    // Unreachable code errors are reported in the binding phase. Here we
                    // simply return the non-auto declared type to reduce follow-on errors.
                    type = Complete(ConvertAutoToAny(declaredType));
                }

                if (sharedFlow is not null)
                {
                    // Record visited node and the associated type in the cache.
                    SetFlowSlot(_sharedFlowNodes, _sharedFlowCount, sharedFlow);
                    SetFlowSlot(_sharedFlowTypes, _sharedFlowCount, type);
                    _sharedFlowCount++;
                }

                flowDepth--;
                return type;
            }
        }

        IType GetInitialOrAssignedType(FlowNode flow)
        {
            var node = (Node)flow.Node!;
            return GetNarrowableTypeForReference(
                node.Kind is SyntaxKind.VariableDeclaration or SyntaxKind.BindingElement
                    ? GetInitialType(node)
                    : GetAssignedType(node),
                reference);
        }

        FlowType? GetTypeAtFlowAssignment(FlowNode flow)
        {
            var node = (Node)flow.Node!;
            // Assignments only narrow the computed type if the declared type is a union type. Thus, we
            // only need to evaluate the assigned type if the declared type is a union type.
            if (IsMatchingReference(reference, node))
            {
                if (!IsReachableFlowNode(flow))
                {
                    return Complete(_unreachableNeverType);
                }

                if (Utilities.GetAssignmentTargetKind(node) == AssignmentKind.Compound)
                {
                    var flowType = GetTypeAtFlowNode((FlowNode)flow.Antecedent!);
                    return CreateFlowType(GetBaseTypeOfLiteralType(GetTypeFromFlowType(flowType)), IsIncomplete(flowType));
                }

                if (ReferenceEquals(declaredType, _autoType) || ReferenceEquals(declaredType, _autoArrayType))
                {
                    if (IsEmptyArrayAssignment(node))
                    {
                        return Complete(GetEvolvingArrayType(_neverType));
                    }

                    var assignedType = GetWidenedLiteralType(GetInitialOrAssignedType(flow));
                    return Complete(IsTypeAssignableTo(assignedType, declaredType) ? assignedType : _anyArrayType);
                }

                var t = Utilities.IsInCompoundLikeAssignment(node) ? GetBaseTypeOfLiteralType(declaredType) : declaredType;
                if ((t.Flags & TypeFlags.Union) != 0)
                {
                    return Complete(GetAssignmentReducedType((IUnionType)t, GetInitialOrAssignedType(flow)));
                }

                return Complete(t);
            }

            // We didn't have a direct match. However, if the reference is a dotted name, this
            // may be an assignment to a left hand part of the reference. For example, for a
            // reference 'x.y.z', we may be at an assignment to 'x.y' or 'x'. In that case,
            // return the declared type.
            if (ContainsMatchingReference(reference, node))
            {
                if (!IsReachableFlowNode(flow))
                {
                    return Complete(_unreachableNeverType);
                }

                // A matching dotted name might also be an expando property on a function *expression*,
                // in which case we continue control flow analysis back to the function's declaration
                if (NodeTests.IsVariableDeclaration(node) && (Utilities.IsInJSFile(node) || IsVarConstLike(node)))
                {
                    var init = Utilities.GetDeclaredExpandoInitializer(node);
                    if (init is not null && init.Kind is SyntaxKind.FunctionExpression or SyntaxKind.ArrowFunction)
                    {
                        return GetTypeAtFlowNode((FlowNode)flow.Antecedent!);
                    }
                }

                return Complete(declaredType);
            }

            // for (const _ in ref) acts as a nonnull on ref
            if (
                NodeTests.IsVariableDeclaration(node) &&
                node.Parent!.Parent!.Kind == SyntaxKind.ForInStatement &&
                (IsMatchingReference(reference, ((ForInStatement)node.Parent!.Parent!).Expression!) || OptionalChainContainsReference(((ForInStatement)node.Parent!.Parent!).Expression!, reference))
            )
            {
                return Complete(GetNonNullableTypeIfNeeded(FinalizeEvolvingArrayType(GetTypeFromFlowType(GetTypeAtFlowNode((FlowNode)flow.Antecedent!)))));
            }

            // Assignment doesn't affect reference
            return null;
        }

        IType NarrowTypeByAssertion(IType type, Expression expr)
        {
            var node = Utilities.SkipParentheses(expr, /*excludeJSDocTypeAssertions*/ true);
            if (node.Kind == SyntaxKind.FalseKeyword)
            {
                return _unreachableNeverType;
            }

            if (node.Kind == SyntaxKind.BinaryExpression)
            {
                if (((BinaryExpression)node).OperatorToken!.Kind == SyntaxKind.AmpersandAmpersandToken)
                {
                    return NarrowTypeByAssertion(NarrowTypeByAssertion(type, ((BinaryExpression)node).Left!), ((BinaryExpression)node).Right!);
                }

                if (((BinaryExpression)node).OperatorToken!.Kind == SyntaxKind.BarBarToken)
                {
                    return GetUnionType([NarrowTypeByAssertion(type, ((BinaryExpression)node).Left!), NarrowTypeByAssertion(type, ((BinaryExpression)node).Right!)]);
                }
            }

            return NarrowType(type, node, /*assumeTrue*/ true);
        }

        FlowType? GetTypeAtFlowCall(FlowNode flow)
        {
            var call = (CallExpression)flow.Node!;
            var signature = GetEffectsSignature(call);
            if (signature is not null)
            {
                var predicate = GetTypePredicateOfSignature(signature);
                if (predicate is not null && (predicate.Kind == TypePredicateKind.AssertsThis || predicate.Kind == TypePredicateKind.AssertsIdentifier))
                {
                    var flowType = GetTypeAtFlowNode((FlowNode)flow.Antecedent!);
                    var type = FinalizeEvolvingArrayType(GetTypeFromFlowType(flowType));
                    var narrowedType = predicate.Type is not null ? NarrowTypeByTypePredicate(type, predicate, call, /*assumeTrue*/ true) :
                        predicate.Kind == TypePredicateKind.AssertsIdentifier && ((IAssertsIdentifierTypePredicate)predicate).ParameterIndex >= 0 && ((IAssertsIdentifierTypePredicate)predicate).ParameterIndex < call.Arguments!.Count ? NarrowTypeByAssertion(type, call.Arguments![((IAssertsIdentifierTypePredicate)predicate).ParameterIndex]) :
                        type;
                    return ReferenceEquals(narrowedType, type) ? flowType : CreateFlowType(narrowedType, IsIncomplete(flowType));
                }

                if ((GetReturnTypeOfSignature(signature).Flags & TypeFlags.Never) != 0)
                {
                    return Complete(_unreachableNeverType);
                }
            }

            return null;
        }

        FlowType? GetTypeAtFlowArrayMutation(FlowNode flow)
        {
            if (ReferenceEquals(declaredType, _autoType) || ReferenceEquals(declaredType, _autoArrayType))
            {
                var node = (Node)flow.Node!;
                var expr = node.Kind == SyntaxKind.CallExpression
                    ? ((PropertyAccessExpression)((CallExpression)node).Expression!).Expression!
                    : ((ElementAccessExpression)((BinaryExpression)node).Left!).Expression!;
                if (IsMatchingReference(reference, GetReferenceCandidate(expr)))
                {
                    var flowType = GetTypeAtFlowNode((FlowNode)flow.Antecedent!);
                    var type = GetTypeFromFlowType(flowType);
                    if ((GetObjectFlags(type) & ObjectFlags.EvolvingArray) != 0)
                    {
                        var evolvedType = (IEvolvingArrayType)type;
                        if (node.Kind == SyntaxKind.CallExpression)
                        {
                            foreach (var arg in ((CallExpression)node).Arguments!)
                            {
                                evolvedType = AddEvolvingArrayElementType(evolvedType, arg);
                            }
                        }
                        else
                        {
                            // We must get the context free expression type so as to not recur in an uncached fashion on the LHS (which causes exponential blowup in compile time)
                            var indexType = GetContextFreeTypeOfExpression(((ElementAccessExpression)((BinaryExpression)node).Left!).ArgumentExpression!);
                            if (IsTypeAssignableToKind(indexType, TypeFlags.NumberLike))
                            {
                                evolvedType = AddEvolvingArrayElementType(evolvedType, ((BinaryExpression)node).Right!);
                            }
                        }

                        return ReferenceEquals(evolvedType, type) ? flowType : CreateFlowType(evolvedType, IsIncomplete(flowType));
                    }

                    return flowType;
                }
            }

            return null;
        }

        FlowType GetTypeAtFlowCondition(FlowNode flow)
        {
            var flowType = GetTypeAtFlowNode((FlowNode)flow.Antecedent!);
            var type = GetTypeFromFlowType(flowType);
            if ((type.Flags & TypeFlags.Never) != 0)
            {
                return flowType;
            }

            // If we have an antecedent type (meaning we're reachable in some way), we first
            // attempt to narrow the antecedent type. If that produces the never type, and if
            // the antecedent type is incomplete (i.e. a transient type in a loop), then we
            // take the type guard as an indication that control *could* reach here once we
            // have the complete type. We proceed by switching to the silent never type which
            // doesn't report errors when operators are applied to it. Note that this is the
            // *only* place a silent never type is ever generated.
            var assumeTrue = (flow.Flags & FlowFlags.TrueCondition) != 0;
            var nonEvolvingType = FinalizeEvolvingArrayType(type);
            var narrowedType = NarrowType(nonEvolvingType, (Expression)flow.Node!, assumeTrue);
            if (ReferenceEquals(narrowedType, nonEvolvingType))
            {
                return flowType;
            }

            return CreateFlowType(narrowedType, IsIncomplete(flowType));
        }

        FlowType GetTypeAtSwitchClause(FlowNode flow)
        {
            var data = (FlowSwitchClauseData)flow.Node!;
            var expr = Utilities.SkipParentheses(data.SwitchStatement!.Expression!);
            var flowType = GetTypeAtFlowNode((FlowNode)flow.Antecedent!);
            var type = GetTypeFromFlowType(flowType);
            if (IsMatchingReference(reference, expr))
            {
                type = NarrowTypeBySwitchOnDiscriminant(type, data);
            }
            else if (expr.Kind == SyntaxKind.TypeOfExpression && IsMatchingReference(reference, ((TypeOfExpression)expr).Expression!))
            {
                type = NarrowTypeBySwitchOnTypeOf(type, data);
            }
            else if (expr.Kind == SyntaxKind.TrueKeyword)
            {
                type = NarrowTypeBySwitchOnTrue(type, data);
            }
            else
            {
                if (_strictNullChecks)
                {
                    if (OptionalChainContainsReference(expr, reference))
                    {
                        type = NarrowTypeBySwitchOptionalChainContainment(type, data, t => (t.Flags & (TypeFlags.Undefined | TypeFlags.Never)) == 0);
                    }
                    else if (expr.Kind == SyntaxKind.TypeOfExpression && OptionalChainContainsReference(((TypeOfExpression)expr).Expression!, reference))
                    {
                        type = NarrowTypeBySwitchOptionalChainContainment(type, data, t => (t.Flags & TypeFlags.Never) == 0 && !((t.Flags & TypeFlags.StringLiteral) != 0 && (string)((ILiteralType)t).Value! == "undefined"));
                    }
                }

                var access = GetDiscriminantPropertyAccess(expr, type);
                if (access is not null)
                {
                    type = NarrowTypeBySwitchOnDiscriminantProperty(type, access, data);
                }
            }

            return CreateFlowType(type, IsIncomplete(flowType));
        }

        FlowType GetTypeAtFlowBranchLabel(FlowNode flow)
        {
            var antecedentTypes = new List<IType>();
            var subtypeReduction = false;
            var seenIncomplete = false;
            FlowNode? bypassFlow = null;
            foreach (var antecedent in (List<FlowNode>)flow.Antecedent!)
            {
                if (bypassFlow is null && (antecedent.Flags & FlowFlags.SwitchClause) != 0 && ((FlowSwitchClauseData)antecedent.Node!).ClauseStart == ((FlowSwitchClauseData)antecedent.Node!).ClauseEnd)
                {
                    // The antecedent is the bypass branch of a potentially exhaustive switch statement.
                    bypassFlow = antecedent;
                    continue;
                }

                var flowType = GetTypeAtFlowNode(antecedent);
                var type = GetTypeFromFlowType(flowType);
                // If the type at a particular antecedent path is the declared type and the
                // reference is known to always be assigned (i.e. when declared and initial types
                // are the same), there is no reason to process more antecedents since the only
                // possible outcome is subtypes that will be removed in the final union type anyway.
                if (ReferenceEquals(type, declaredType) && ReferenceEquals(declaredType, initialType))
                {
                    return Complete(type);
                }

                Core.PushIfUnique(antecedentTypes, type);
                // If an antecedent type is not a subset of the declared type, we need to perform
                // subtype reduction. This happens when a "foreign" type is injected into the control
                // flow using the instanceof operator or a user defined type predicate.
                if (!IsTypeSubsetOf(type, initialType))
                {
                    subtypeReduction = true;
                }

                if (IsIncomplete(flowType))
                {
                    seenIncomplete = true;
                }
            }

            if (bypassFlow is not null)
            {
                var bypassData = (FlowSwitchClauseData)bypassFlow.Node!;
                var flowType = GetTypeAtFlowNode(bypassFlow);
                var type = GetTypeFromFlowType(flowType);
                // If the bypass flow contributes a type we haven't seen yet and the switch statement
                // isn't exhaustive, process the bypass flow type. Since exhaustiveness checks increase
                // the risk of circularities, we only want to perform them when they make a difference.
                if ((type.Flags & TypeFlags.Never) == 0 && !Core.Contains(antecedentTypes, type) && !IsExhaustiveSwitchStatement(bypassData.SwitchStatement!))
                {
                    if (ReferenceEquals(type, declaredType) && ReferenceEquals(declaredType, initialType))
                    {
                        return Complete(type);
                    }

                    antecedentTypes.Add(type);
                    if (!IsTypeSubsetOf(type, initialType))
                    {
                        subtypeReduction = true;
                    }

                    if (IsIncomplete(flowType))
                    {
                        seenIncomplete = true;
                    }
                }
            }

            return CreateFlowType(GetUnionOrEvolvingArrayType(antecedentTypes, subtypeReduction ? UnionReduction.Subtype : UnionReduction.Literal), seenIncomplete);
        }

        FlowType GetTypeAtFlowLoopLabel(FlowNode flow)
        {
            // If we have previously computed the control flow type for the reference at
            // this flow loop junction, return the cached type.
            var id = GetFlowNodeId(flow);
            var cache = GetFlowSlot(_flowLoopCaches, id);
            if (cache is null)
            {
                cache = new Dictionary<string, IType>(StringComparer.Ordinal);
                SetFlowSlot(_flowLoopCaches, id, cache);
            }

            var cacheKey = GetOrSetCacheKey();
            if (cacheKey is null)
            {
                // No cache key is generated when binding patterns are in unnarrowable situations
                return Complete(declaredType);
            }

            var cached = cache.GetValueOrDefault(cacheKey);
            if (cached is not null)
            {
                return Complete(cached);
            }

            // If this flow loop junction and reference are already being processed, return
            // the union of the types computed for each branch so far, marked as incomplete.
            // It is possible to see an empty array in cases where loops are nested and the
            // back edge of the outer loop reaches an inner loop that is already being analyzed.
            // In such cases we restart the analysis of the inner loop, which will then see
            // a non-empty in-process array for the outer loop and eventually terminate because
            // the first antecedent of a loop junction is always the non-looping control flow
            // path that leads to the top.
            for (var i = _flowLoopStart; i < _flowLoopCount; i++)
            {
                if (ReferenceEquals(GetFlowSlot(_flowLoopNodes, i), flow) && GetFlowSlot(_flowLoopKeys, i) == cacheKey && GetFlowSlot(_flowLoopTypes, i)!.Count != 0)
                {
                    return CreateFlowType(GetUnionOrEvolvingArrayType(GetFlowSlot(_flowLoopTypes, i)!, UnionReduction.Literal), /*incomplete*/ true);
                }
            }

            // Add the flow loop junction and reference to the in-process stack and analyze
            // each antecedent code path.
            var antecedentTypes = new List<IType>();
            var subtypeReduction = false;
            FlowType? firstAntecedentType = null;
            foreach (var antecedent in (List<FlowNode>)flow.Antecedent!)
            {
                FlowType flowType;
                if (firstAntecedentType is null)
                {
                    // The first antecedent of a loop junction is always the non-looping control
                    // flow path that leads to the top.
                    flowType = firstAntecedentType = GetTypeAtFlowNode(antecedent);
                }
                else
                {
                    // All but the first antecedent are the looping control flow paths that lead
                    // back to the loop junction. We track these on the flow loop stack.
                    SetFlowSlot(_flowLoopNodes, _flowLoopCount, flow);
                    SetFlowSlot(_flowLoopKeys, _flowLoopCount, cacheKey);
                    SetFlowSlot(_flowLoopTypes, _flowLoopCount, antecedentTypes);
                    _flowLoopCount++;
                    var saveFlowTypeCache = _flowTypeCache;
                    _flowTypeCache = null;
                    flowType = GetTypeAtFlowNode(antecedent);
                    _flowTypeCache = saveFlowTypeCache;
                    _flowLoopCount--;
                    // If we see a value appear in the cache it is a sign that control flow analysis
                    // was restarted and completed by checkExpressionCached. We can simply pick up
                    // the resulting type and bail out.
                    var cachedAgain = cache.GetValueOrDefault(cacheKey);
                    if (cachedAgain is not null)
                    {
                        return Complete(cachedAgain);
                    }
                }

                var type = GetTypeFromFlowType(flowType);
                Core.PushIfUnique(antecedentTypes, type);
                // If an antecedent type is not a subset of the declared type, we need to perform
                // subtype reduction. This happens when a "foreign" type is injected into the control
                // flow using the instanceof operator or a user defined type predicate.
                if (!IsTypeSubsetOf(type, initialType))
                {
                    subtypeReduction = true;
                }

                // If the type at a particular antecedent path is the declared type there is no
                // reason to process more antecedents since the only possible outcome is subtypes
                // that will be removed in the final union type anyway.
                if (ReferenceEquals(type, declaredType))
                {
                    break;
                }
            }

            // The result is incomplete if the first antecedent (the non-looping control flow path)
            // is incomplete.
            var result = GetUnionOrEvolvingArrayType(antecedentTypes, subtypeReduction ? UnionReduction.Subtype : UnionReduction.Literal);
            if (IsIncomplete(firstAntecedentType!))
            {
                return CreateFlowType(result, /*incomplete*/ true);
            }

            cache[cacheKey] = result;
            return Complete(result);
        }

        // At flow control branch or loop junctions, if the type along every antecedent code path
        // is an evolving array type, we construct a combined evolving array type. Otherwise we
        // finalize all evolving array types.
        IType GetUnionOrEvolvingArrayType(List<IType> types, UnionReduction subtypeReduction)
        {
            if (IsEvolvingArrayTypeList(types))
            {
                return GetEvolvingArrayType(GetUnionType(TypeList(Core.Map(types, (t, _) => GetElementTypeOfEvolvingArrayType(t)))));
            }

            var result = RecombineUnknownType(GetUnionType(TypeList(Core.SameMap(types, (t, _) => FinalizeEvolvingArrayType(t))), subtypeReduction));
            if (!ReferenceEquals(result, declaredType) && (result.Flags & TypeFlags.Union) != 0 && (declaredType.Flags & TypeFlags.Union) != 0 && Core.ArrayIsEqualTo(((IUnionType)result).Types, ((IUnionType)declaredType).Types))
            {
                return declaredType;
            }

            return result;
        }

        Node? GetCandidateDiscriminantPropertyAccess(Expression expr)
        {
            if (Utilities.IsBindingPattern(reference) || Utilities.IsFunctionExpressionOrArrowFunction(reference) || Utilities.IsObjectLiteralMethod(reference))
            {
                // When the reference is a binding pattern or function or arrow expression, we are narrowing a pesudo-reference in
                // getNarrowedTypeOfSymbol. An identifier for a destructuring variable declared in the same binding pattern or
                // parameter declared in the same parameter list is a candidate.
                if (NodeTests.IsIdentifier(expr))
                {
                    var symbol = GetResolvedSymbol((Identifier)expr);
                    var declaration = GetExportSymbolOfValueSymbolIfExported(symbol).ValueDeclaration;
                    // PORT-NOTE: TS reads `<declaration>.initializer`/`.dotDotDotToken` off the `BindingElement |
                    // ParameterDeclaration` union; the port's two classes share no base, so the two reads are this
                    // kind switch (the same treatment `Binder.Narrowing.cs`'s `GetInnerExpression` records).
                    var uninitialized = declaration is not null && declaration switch
                    {
                        BindingElement bindingElement => bindingElement.Initializer is null && bindingElement.DotDotDotToken is null,
                        ParameterDeclaration parameter => parameter.Initializer is null && parameter.DotDotDotToken is null,
                        _ => false,
                    };
                    if (uninitialized && ReferenceEquals(reference, ((Node)declaration!).Parent))
                    {
                        // PORT-NOTE: the reference returns `declaration` here, and its inferred return type for this
                        // local function is the union `AccessExpression | BindingElement | ParameterDeclaration` — only
                        // the first of the three is an `Expression` in TS. The port's return type is therefore `Node`
                        // (its erasure of that union), not the `Expression` this used to assert.
                        return (Node)declaration!;
                    }
                }
            }
            else if (Utilities.IsAccessExpression(expr))
            {
                // An access expression is a candidate if the reference matches the left hand expression.
                if (IsMatchingReference(reference, Utilities.GetAccessExpressionExpression(expr)))
                {
                    return expr;
                }
            }
            else if (NodeTests.IsIdentifier(expr))
            {
                var symbol = GetResolvedSymbol((Identifier)expr);
                if (IsConstantVariable(symbol))
                {
                    var declaration = symbol.ValueDeclaration!;
                    var initializer = GetCandidateVariableDeclarationInitializer((Node)declaration);
                    // Given 'const x = obj.kind', allow 'x' as an alias for 'obj.kind'
                    if (initializer is not null && Utilities.IsAccessExpression(initializer) && IsMatchingReference(reference, Utilities.GetAccessExpressionExpression(initializer)))
                    {
                        return initializer;
                    }

                    // Given 'const { kind: x } = obj', allow 'x' as an alias for 'obj.kind'
                    if (NodeTests.IsBindingElement((Node)declaration) && ((BindingElement)declaration).Initializer is null)
                    {
                        initializer = GetCandidateVariableDeclarationInitializer(((Node)declaration).Parent!.Parent!);
                        if (initializer is not null && (NodeTests.IsIdentifier(initializer) || Utilities.IsAccessExpression(initializer)) && IsMatchingReference(reference, initializer))
                        {
                            return (Node)declaration;
                        }
                    }
                }
            }

            return null;

            static Expression? GetCandidateVariableDeclarationInitializer(Node node)
                => NodeTests.IsVariableDeclaration(node) && ((VariableDeclaration)node).Type is null && ((VariableDeclaration)node).Initializer is not null
                    ? Utilities.SkipParentheses(((VariableDeclaration)node).Initializer!)
                    : null;
        }

        Node? GetDiscriminantPropertyAccess(Expression expr, IType computedType)
        {
            // As long as the computed type is a subset of the declared type, we use the full declared type to detect
            // a discriminant property. In cases where the computed type isn't a subset, e.g because of a preceding type
            // predicate narrowing, we use the actual computed type.
            if ((declaredType.Flags & TypeFlags.Union) != 0 || (computedType.Flags & TypeFlags.Union) != 0)
            {
                var access = GetCandidateDiscriminantPropertyAccess(expr);
                if (access is not null)
                {
                    var name = GetAccessedPropertyName(access);
                    if (name is not null)
                    {
                        var type = (declaredType.Flags & TypeFlags.Union) != 0 && IsTypeSubsetOf(computedType, declaredType) ? declaredType : computedType;
                        if (IsDiscriminantProperty(type, name))
                        {
                            return access;
                        }
                    }
                }
            }

            return null;
        }

        IType NarrowTypeByDiscriminant(IType type, Node access, Func<IType, IType> narrowType)
        {
            var propName = GetAccessedPropertyName(access);
            if (propName is null)
            {
                return type;
            }

            var optionalChain = Utilities.IsOptionalChain(access);
            var removeNullable = _strictNullChecks && (optionalChain || Utilities.IsNonNullAccess(access)) && MaybeTypeOfKind(type, TypeFlags.Nullable);
            IType? propType = GetTypeOfPropertyOfType(removeNullable ? GetTypeWithFacts(type, TypeFacts.NEUndefinedOrNull) : type, propName);
            if (propType is null)
            {
                return type;
            }

            var propTypeForNarrowing = removeNullable && optionalChain ? GetOptionalType(propType) : propType;
            var narrowedPropType = narrowType(propTypeForNarrowing);
            return FilterType(type, t =>
            {
                var discriminantType = GetTypeOfPropertyOrIndexSignatureOfType(t, propName) ?? _unknownType;
                return (discriminantType.Flags & TypeFlags.Never) == 0 && (narrowedPropType.Flags & TypeFlags.Never) == 0 && AreTypesComparable(narrowedPropType, discriminantType);
            });
        }

        IType NarrowTypeByDiscriminantProperty(IType type, Node access, SyntaxKind @operator, Expression value, bool assumeTrue)
        {
            if ((@operator == SyntaxKind.EqualsEqualsEqualsToken || @operator == SyntaxKind.ExclamationEqualsEqualsToken) && (type.Flags & TypeFlags.Union) != 0)
            {
                var keyPropertyName = GetKeyPropertyName((IUnionType)type);
                if (keyPropertyName is not null && keyPropertyName == GetAccessedPropertyName(access))
                {
                    var candidate = GetConstituentTypeForKeyType((IUnionType)type, GetTypeOfExpression(value));
                    if (candidate is not null)
                    {
                        return @operator == (assumeTrue ? SyntaxKind.EqualsEqualsEqualsToken : SyntaxKind.ExclamationEqualsEqualsToken) ? candidate :
                            IsUnitType(GetTypeOfPropertyOfType(candidate, keyPropertyName) ?? _unknownType) ? RemoveType(type, candidate) :
                            type;
                    }
                }
            }

            return NarrowTypeByDiscriminant(type, access, t => NarrowTypeByEquality(t, @operator, value, assumeTrue));
        }

        IType NarrowTypeBySwitchOnDiscriminantProperty(IType type, Node access, FlowSwitchClauseData data)
        {
            if (data.ClauseStart < data.ClauseEnd && (type.Flags & TypeFlags.Union) != 0 && GetKeyPropertyName((IUnionType)type) == GetAccessedPropertyName(access))
            {
                var clauseTypes = Core.Slice(GetSwitchClauseTypes(data.SwitchStatement!), data.ClauseStart, data.ClauseEnd);
                var candidate = GetUnionType(TypeList(Core.Map(clauseTypes, (t, _) => GetConstituentTypeForKeyType((IUnionType)type, t) ?? _unknownType)));
                if (!ReferenceEquals(candidate, _unknownType))
                {
                    return candidate;
                }
            }

            return NarrowTypeByDiscriminant(type, access, t => NarrowTypeBySwitchOnDiscriminant(t, data));
        }

        IType NarrowTypeByTruthiness(IType type, Expression expr, bool assumeTrue)
        {
            if (IsMatchingReference(reference, expr))
            {
                return GetAdjustedTypeWithFacts(type, assumeTrue ? TypeFacts.Truthy : TypeFacts.Falsy);
            }

            if (_strictNullChecks && assumeTrue && OptionalChainContainsReference(expr, reference))
            {
                type = GetAdjustedTypeWithFacts(type, TypeFacts.NEUndefinedOrNull);
            }

            var access = GetDiscriminantPropertyAccess(expr, type);
            if (access is not null)
            {
                return NarrowTypeByDiscriminant(type, access, t => GetTypeWithFacts(t, assumeTrue ? TypeFacts.Truthy : TypeFacts.Falsy));
            }

            return type;
        }

        bool IsTypePresencePossible(IType type, string propName, bool assumeTrue)
        {
            var prop = GetPropertyOfType(type, propName);
            return prop is not null
                ? (prop.Flags & SymbolFlags.Optional) != 0 || (GetCheckFlags(prop) & CheckFlags.Partial) != 0 || assumeTrue
                : GetApplicableIndexInfoForName(type, propName) is not null || !assumeTrue;
        }

        IType NarrowTypeByInKeyword(IType type, IType nameType, bool assumeTrue)
        {
            var name = Utilities.GetPropertyNameFromType(nameType);
            var isKnownProperty = SomeType(type, t => IsTypePresencePossible(t, name, /*assumeTrue*/ true));
            if (isKnownProperty)
            {
                // If the check is for a known property (i.e. a property declared in some constituent of
                // the target type), we filter the target type by presence of absence of the property.
                return FilterType(type, t => IsTypePresencePossible(t, name, assumeTrue));
            }

            if (assumeTrue)
            {
                // If the check is for an unknown property, we intersect the target type with `Record<X, unknown>`,
                // where X is the name of the property.
                var recordSymbol = GetGlobalRecordSymbol();
                if (recordSymbol is not null)
                {
                    return GetIntersectionType([type, GetTypeAliasInstantiation(recordSymbol, [nameType, _unknownType])]);
                }
            }

            return type;
        }

        IType NarrowTypeByBooleanComparison(IType type, Expression expr, Node @bool, SyntaxKind @operator, bool assumeTrue)
        {
            assumeTrue = (assumeTrue != (@bool.Kind == SyntaxKind.TrueKeyword)) != (@operator != SyntaxKind.ExclamationEqualsEqualsToken && @operator != SyntaxKind.ExclamationEqualsToken);
            return NarrowType(type, expr, assumeTrue);
        }

        IType NarrowTypeByBinaryExpression(IType type, BinaryExpression expr, bool assumeTrue)
        {
            switch (expr.OperatorToken!.Kind)
            {
                case SyntaxKind.EqualsToken:
                case SyntaxKind.BarBarEqualsToken:
                case SyntaxKind.AmpersandAmpersandEqualsToken:
                case SyntaxKind.QuestionQuestionEqualsToken:
                    return NarrowTypeByTruthiness(NarrowType(type, expr.Right!, assumeTrue), expr.Left!, assumeTrue);
                case SyntaxKind.EqualsEqualsToken:
                case SyntaxKind.ExclamationEqualsToken:
                case SyntaxKind.EqualsEqualsEqualsToken:
                case SyntaxKind.ExclamationEqualsEqualsToken:
                {
                    var @operator = expr.OperatorToken.Kind;
                    var left = GetReferenceCandidate(expr.Left!);
                    var right = GetReferenceCandidate(expr.Right!);
                    if (left.Kind == SyntaxKind.TypeOfExpression && Utilities.IsStringLiteralLike(right))
                    {
                        return NarrowTypeByTypeof(type, (TypeOfExpression)left, @operator, (LiteralExpression)right, assumeTrue);
                    }

                    if (right.Kind == SyntaxKind.TypeOfExpression && Utilities.IsStringLiteralLike(left))
                    {
                        return NarrowTypeByTypeof(type, (TypeOfExpression)right, @operator, (LiteralExpression)left, assumeTrue);
                    }

                    if (IsMatchingReference(reference, left))
                    {
                        return NarrowTypeByEquality(type, @operator, right, assumeTrue);
                    }

                    if (IsMatchingReference(reference, right))
                    {
                        return NarrowTypeByEquality(type, @operator, left, assumeTrue);
                    }

                    if (_strictNullChecks)
                    {
                        if (OptionalChainContainsReference(left, reference))
                        {
                            type = NarrowTypeByOptionalChainContainment(type, @operator, right, assumeTrue);
                        }
                        else if (OptionalChainContainsReference(right, reference))
                        {
                            type = NarrowTypeByOptionalChainContainment(type, @operator, left, assumeTrue);
                        }
                    }

                    var leftAccess = GetDiscriminantPropertyAccess(left, type);
                    if (leftAccess is not null)
                    {
                        return NarrowTypeByDiscriminantProperty(type, leftAccess, @operator, right, assumeTrue);
                    }

                    var rightAccess = GetDiscriminantPropertyAccess(right, type);
                    if (rightAccess is not null)
                    {
                        return NarrowTypeByDiscriminantProperty(type, rightAccess, @operator, left, assumeTrue);
                    }

                    if (IsMatchingConstructorReference(left))
                    {
                        return NarrowTypeByConstructor(type, @operator, right, assumeTrue);
                    }

                    if (IsMatchingConstructorReference(right))
                    {
                        return NarrowTypeByConstructor(type, @operator, left, assumeTrue);
                    }

                    if (Utilities.IsBooleanLiteral(right) && !Utilities.IsAccessExpression(left))
                    {
                        return NarrowTypeByBooleanComparison(type, left, right, @operator, assumeTrue);
                    }

                    if (Utilities.IsBooleanLiteral(left) && !Utilities.IsAccessExpression(right))
                    {
                        return NarrowTypeByBooleanComparison(type, right, left, @operator, assumeTrue);
                    }

                    break;
                }
                case SyntaxKind.InstanceOfKeyword:
                    return NarrowTypeByInstanceof(type, (BinaryExpression)expr, assumeTrue);
                case SyntaxKind.InKeyword:
                {
                    if (NodeTests.IsPrivateIdentifier(expr.Left!))
                    {
                        return NarrowTypeByPrivateIdentifierInInExpression(type, expr, assumeTrue);
                    }

                    var target = GetReferenceCandidate(expr.Right!);
                    if (ContainsMissingType(type) && Utilities.IsAccessExpression(reference) && IsMatchingReference(Utilities.GetAccessExpressionExpression((Expression)reference), target))
                    {
                        var leftType = GetTypeOfExpression(expr.Left!);
                        if (Utilities.IsTypeUsableAsPropertyName(leftType) && GetAccessedPropertyName(reference) == Utilities.GetPropertyNameFromType(leftType))
                        {
                            return GetTypeWithFacts(type, assumeTrue ? TypeFacts.NEUndefined : TypeFacts.EQUndefined);
                        }
                    }

                    if (IsMatchingReference(reference, target))
                    {
                        var leftType = GetTypeOfExpression(expr.Left!);
                        if (Utilities.IsTypeUsableAsPropertyName(leftType))
                        {
                            return NarrowTypeByInKeyword(type, leftType, assumeTrue);
                        }
                    }

                    break;
                }
                case SyntaxKind.CommaToken:
                    return NarrowType(type, expr.Right!, assumeTrue);
                // Ordinarily we won't see && and || expressions in control flow analysis because the Binder breaks those
                // expressions down to individual conditional control flows. However, we may encounter them when analyzing
                // aliased conditional expressions.
                case SyntaxKind.AmpersandAmpersandToken:
                    return assumeTrue
                        ? NarrowType(NarrowType(type, expr.Left!, /*assumeTrue*/ true), expr.Right!, /*assumeTrue*/ true)
                        : GetUnionType([NarrowType(type, expr.Left!, /*assumeTrue*/ false), NarrowType(type, expr.Right!, /*assumeTrue*/ false)]);
                case SyntaxKind.BarBarToken:
                    return assumeTrue
                        ? GetUnionType([NarrowType(type, expr.Left!, /*assumeTrue*/ true), NarrowType(type, expr.Right!, /*assumeTrue*/ true)])
                        : NarrowType(NarrowType(type, expr.Left!, /*assumeTrue*/ false), expr.Right!, /*assumeTrue*/ false);
            }

            return type;
        }

        IType NarrowTypeByPrivateIdentifierInInExpression(IType type, BinaryExpression expr, bool assumeTrue)
        {
            var target = GetReferenceCandidate(expr.Right!);
            if (!IsMatchingReference(reference, target))
            {
                return type;
            }

            Debug.AssertNode(expr.Left, NodeTests.IsPrivateIdentifier);
            var symbol = GetSymbolForPrivateIdentifierExpression((PrivateIdentifier)expr.Left!);
            if (symbol is null)
            {
                return type;
            }

            var classSymbol = symbol.Parent!;
            var targetType = Utilities.HasStaticModifier((Node)Debug.CheckDefined(symbol.ValueDeclaration, "should always have a declaration"))
                ? (IType)GetTypeOfSymbol(classSymbol)
                : GetDeclaredTypeOfSymbol(classSymbol);
            return GetNarrowedType(type, targetType, assumeTrue, /*checkDerived*/ true);
        }

        IType NarrowTypeByOptionalChainContainment(IType type, SyntaxKind @operator, Expression value, bool assumeTrue)
        {
            // We are in a branch of obj?.foo === value (or any one of the other equality operators). We narrow obj as follows:
            // When operator is === and type of value excludes undefined, null and undefined is removed from type of obj in true branch.
            // When operator is !== and type of value excludes undefined, null and undefined is removed from type of obj in false branch.
            // When operator is == and type of value excludes null and undefined, null and undefined is removed from type of obj in true branch.
            // When operator is != and type of value excludes null and undefined, null and undefined is removed from type of obj in false branch.
            // When operator is === and type of value is undefined, null and undefined is removed from type of obj in false branch.
            // When operator is !== and type of value is undefined, null and undefined is removed from type of obj in true branch.
            // When operator is == and type of value is null or undefined, null and undefined is removed from type of obj in false branch.
            // When operator is != and type of value is null or undefined, null and undefined is removed from type of obj in true branch.
            var equalsOperator = @operator == SyntaxKind.EqualsEqualsToken || @operator == SyntaxKind.EqualsEqualsEqualsToken;
            var nullableFlags = @operator == SyntaxKind.EqualsEqualsToken || @operator == SyntaxKind.ExclamationEqualsToken ? TypeFlags.Nullable : TypeFlags.Undefined;
            var valueType = GetTypeOfExpression(value);
            // Note that we include any and unknown in the exclusion test because their domain includes null and undefined.
            var removeNullable = equalsOperator != assumeTrue && EveryType(valueType, t => (t.Flags & nullableFlags) != 0) ||
                equalsOperator == assumeTrue && EveryType(valueType, t => (t.Flags & (TypeFlags.AnyOrUnknown | nullableFlags)) == 0);
            return removeNullable ? GetAdjustedTypeWithFacts(type, TypeFacts.NEUndefinedOrNull) : type;
        }

        IType NarrowTypeByEquality(IType type, SyntaxKind @operator, Expression value, bool assumeTrue)
        {
            if ((type.Flags & TypeFlags.Any) != 0)
            {
                return type;
            }

            if (@operator == SyntaxKind.ExclamationEqualsToken || @operator == SyntaxKind.ExclamationEqualsEqualsToken)
            {
                assumeTrue = !assumeTrue;
            }

            var valueType = GetTypeOfExpression(value);
            var doubleEquals = @operator == SyntaxKind.EqualsEqualsToken || @operator == SyntaxKind.ExclamationEqualsToken;
            if ((valueType.Flags & TypeFlags.Nullable) != 0)
            {
                if (!_strictNullChecks)
                {
                    return type;
                }

                var facts = doubleEquals
                    ? assumeTrue ? TypeFacts.EQUndefinedOrNull : TypeFacts.NEUndefinedOrNull
                    : (valueType.Flags & TypeFlags.Null) != 0
                        ? assumeTrue ? TypeFacts.EQNull : TypeFacts.NENull
                        : assumeTrue ? TypeFacts.EQUndefined : TypeFacts.NEUndefined;
                return GetAdjustedTypeWithFacts(type, facts);
            }

            if (assumeTrue)
            {
                if (!doubleEquals && ((type.Flags & TypeFlags.Unknown) != 0 || SomeType(type, IsEmptyAnonymousObjectType)))
                {
                    if ((valueType.Flags & (TypeFlags.Primitive | TypeFlags.NonPrimitive)) != 0 || IsEmptyAnonymousObjectType(valueType))
                    {
                        return valueType;
                    }

                    if ((valueType.Flags & TypeFlags.Object) != 0)
                    {
                        return _nonPrimitiveType;
                    }
                }

                var filteredType = FilterType(type, t => AreTypesComparable(t, valueType) || doubleEquals && IsCoercibleUnderDoubleEquals(t, valueType));
                return ReplacePrimitivesWithLiterals(filteredType, valueType);
            }

            if (IsUnitType(valueType))
            {
                return FilterType(type, t => !(IsUnitLikeType(t) && AreTypesComparable(t, valueType)));
            }

            return type;
        }

        IType NarrowTypeByTypeof(IType type, TypeOfExpression typeOfExpr, SyntaxKind @operator, LiteralExpression literal, bool assumeTrue)
        {
            // We have '==', '!=', '===', or !==' operator with 'typeof xxx' and string literal operands
            if (@operator == SyntaxKind.ExclamationEqualsToken || @operator == SyntaxKind.ExclamationEqualsEqualsToken)
            {
                assumeTrue = !assumeTrue;
            }

            var target = GetReferenceCandidate(typeOfExpr.Expression!);
            if (!IsMatchingReference(reference, target))
            {
                if (_strictNullChecks && OptionalChainContainsReference(target, reference) && assumeTrue == (literal.Text != "undefined"))
                {
                    type = GetAdjustedTypeWithFacts(type, TypeFacts.NEUndefinedOrNull);
                }

                var propertyAccess = GetDiscriminantPropertyAccess(target, type);
                if (propertyAccess is not null)
                {
                    return NarrowTypeByDiscriminant(type, propertyAccess, t => NarrowTypeByLiteralExpression(t, literal, assumeTrue));
                }

                return type;
            }

            return NarrowTypeByLiteralExpression(type, literal, assumeTrue);
        }

        IType NarrowTypeByLiteralExpression(IType type, LiteralExpression literal, bool assumeTrue)
        {
            return assumeTrue
                ? NarrowTypeByTypeName(type, literal.Text!)
                : GetAdjustedTypeWithFacts(type, s_typeofNEFacts.TryGetValue(literal.Text!, out var facts) && facts != TypeFacts.None ? facts : TypeFacts.TypeofNEHostObject);
        }

        IType NarrowTypeBySwitchOptionalChainContainment(IType type, FlowSwitchClauseData data, Func<IType, bool> clauseCheck)
        {
            var everyClauseChecks = data.ClauseStart != data.ClauseEnd && Core.Every(Core.Slice(GetSwitchClauseTypes(data.SwitchStatement!), data.ClauseStart, data.ClauseEnd), (t, _) => clauseCheck(t));
            return everyClauseChecks ? GetTypeWithFacts(type, TypeFacts.NEUndefinedOrNull) : type;
        }

        IType NarrowTypeBySwitchOnDiscriminant(IType type, FlowSwitchClauseData data)
        {
            // We only narrow if all case expressions specify
            // values with unit types, except for the case where
            // `type` is unknown. In this instance we map object
            // types to the nonPrimitive type and narrow with that.
            var switchTypes = GetSwitchClauseTypes(data.SwitchStatement!);
            if (switchTypes.Count == 0)
            {
                return type;
            }

            var clauseTypes = Core.Slice(switchTypes, data.ClauseStart, data.ClauseEnd);
            var hasDefaultClause = data.ClauseStart == data.ClauseEnd || Core.Contains(clauseTypes, _neverType);
            if ((type.Flags & TypeFlags.Unknown) != 0 && !hasDefaultClause)
            {
                List<IType>? groundClauseTypes = null;
                for (var i = 0; i < clauseTypes.Count; i += 1)
                {
                    var t = clauseTypes[i];
                    if ((t.Flags & (TypeFlags.Primitive | TypeFlags.NonPrimitive)) != 0)
                    {
                        if (groundClauseTypes is not null)
                        {
                            groundClauseTypes.Add(t);
                        }
                    }
                    else if ((t.Flags & TypeFlags.Object) != 0)
                    {
                        if (groundClauseTypes is null)
                        {
                            groundClauseTypes = TypeList(Core.Slice(clauseTypes, 0, i));
                        }

                        groundClauseTypes.Add(_nonPrimitiveType);
                    }
                    else
                    {
                        return type;
                    }
                }

                return GetUnionType(groundClauseTypes ?? TypeList(clauseTypes));
            }

            var discriminantType = GetUnionType(TypeList(clauseTypes));
            var caseType = (discriminantType.Flags & TypeFlags.Never) != 0 ? _neverType :
                ReplacePrimitivesWithLiterals(FilterType(type, t => AreTypesComparable(discriminantType, t)), discriminantType);
            if (!hasDefaultClause)
            {
                return caseType;
            }

            var defaultType = FilterType(type, t => !(IsUnitLikeType(t) && Core.Contains(switchTypes, (t.Flags & TypeFlags.Undefined) != 0 ? _undefinedType : GetRegularTypeOfLiteralType(ExtractUnitType(t)), (t1, t2) => IsUnitType(t1) && AreTypesComparable(t1, t2))));
            return (caseType.Flags & TypeFlags.Never) != 0 ? defaultType : GetUnionType([caseType, defaultType]);
        }

        IType NarrowTypeByTypeName(IType type, string typeName)
        {
            switch (typeName)
            {
                case "string":
                    return NarrowTypeByTypeFacts(type, _stringType, TypeFacts.TypeofEQString);
                case "number":
                    return NarrowTypeByTypeFacts(type, _numberType, TypeFacts.TypeofEQNumber);
                case "bigint":
                    return NarrowTypeByTypeFacts(type, _bigintType, TypeFacts.TypeofEQBigInt);
                case "boolean":
                    return NarrowTypeByTypeFacts(type, _booleanType, TypeFacts.TypeofEQBoolean);
                case "symbol":
                    return NarrowTypeByTypeFacts(type, _esSymbolType, TypeFacts.TypeofEQSymbol);
                case "object":
                    return (type.Flags & TypeFlags.Any) != 0 ? type : GetUnionType([NarrowTypeByTypeFacts(type, _nonPrimitiveType, TypeFacts.TypeofEQObject), NarrowTypeByTypeFacts(type, _nullType, TypeFacts.EQNull)]);
                case "function":
                    return (type.Flags & TypeFlags.Any) != 0 ? type : NarrowTypeByTypeFacts(type, _globalFunctionType, TypeFacts.TypeofEQFunction);
                case "undefined":
                    return NarrowTypeByTypeFacts(type, _undefinedType, TypeFacts.EQUndefined);
            }

            return NarrowTypeByTypeFacts(type, _nonPrimitiveType, TypeFacts.TypeofEQHostObject);
        }

        IType NarrowTypeByTypeFacts(IType type, IType impliedType, TypeFacts facts)
        {
            return MapType(type, t =>
                // We first check if a constituent is a subtype of the implied type. If so, we either keep or eliminate
                // the constituent based on its type facts. We use the strict subtype relation because it treats `object`
                // as a subtype of `{}`, and we need the type facts check because function types are subtypes of `object`,
                // but are classified as "function" according to `typeof`.
                IsTypeRelatedTo(t, impliedType, _strictSubtypeRelation) ? HasTypeFacts(t, facts) ? t : _neverType :
                    // We next check if the consituent is a supertype of the implied type. If so, we substitute the implied
                    // type. This handles top types like `unknown` and `{}`, and supertypes like `{ toString(): string }`.
                    IsTypeSubtypeOf(impliedType, t) ? impliedType :
                    // Neither the constituent nor the implied type is a subtype of the other, however their domains may still
                    // overlap. For example, an unconstrained type parameter and type `string`. If the type facts indicate
                    // possible overlap, we form an intersection. Otherwise, we eliminate the constituent.
                    HasTypeFacts(t, facts) ? GetIntersectionType([t, impliedType]) :
                    _neverType)!;
        }

        IType NarrowTypeBySwitchOnTypeOf(IType type, FlowSwitchClauseData data)
        {
            var witnesses = GetSwitchClauseTypeOfWitnesses(data.SwitchStatement!);
            if (witnesses is null)
            {
                return type;
            }

            // Equal start and end denotes implicit fallthrough; undefined marks explicit default clause.
            var defaultIndex = Core.FindIndex(data.SwitchStatement!.CaseBlock!.Clauses!, (clause, _) => clause.Kind == SyntaxKind.DefaultClause);
            var hasDefaultClause = data.ClauseStart == data.ClauseEnd || defaultIndex >= data.ClauseStart && defaultIndex < data.ClauseEnd;
            if (hasDefaultClause)
            {
                // In the default clause we filter constituents down to those that are not-equal to all handled cases.
                var notEqualFacts = GetNotEqualFactsFromTypeofSwitch(data.ClauseStart, data.ClauseEnd, witnesses);
                return FilterType(type, t => GetTypeFacts(t, notEqualFacts) == notEqualFacts);
            }

            // In the non-default cause we create a union of the type narrowed by each of the listed cases.
            var clauseWitnesses = Core.Slice(witnesses, data.ClauseStart, data.ClauseEnd);
            return GetUnionType(TypeList(Core.Map(clauseWitnesses, (text, _) => text is not null ? NarrowTypeByTypeName(type, text) : _neverType)));
        }

        IType NarrowTypeBySwitchOnTrue(IType type, FlowSwitchClauseData data)
        {
            var clauses = data.SwitchStatement!.CaseBlock!.Clauses!;
            var defaultIndex = Core.FindIndex(clauses, (clause, _) => clause.Kind == SyntaxKind.DefaultClause);
            var hasDefaultClause = data.ClauseStart == data.ClauseEnd || defaultIndex >= data.ClauseStart && defaultIndex < data.ClauseEnd;

            // First, narrow away all of the cases that preceded this set of cases.
            for (var i = 0; i < data.ClauseStart; i++)
            {
                var clause = clauses[i];
                if (clause.Kind == SyntaxKind.CaseClause)
                {
                    type = NarrowType(type, ((CaseClause)clause).Expression!, /*assumeTrue*/ false);
                }
            }

            // If our current set has a default, then none the other cases were hit either.
            // There's no point in narrowing by the the other cases in the set, since we can
            // get here through other paths.
            if (hasDefaultClause)
            {
                for (var i = data.ClauseEnd; i < clauses.Count; i++)
                {
                    var clause = clauses[i];
                    if (clause.Kind == SyntaxKind.CaseClause)
                    {
                        type = NarrowType(type, ((CaseClause)clause).Expression!, /*assumeTrue*/ false);
                    }
                }

                return type;
            }

            // Now, narrow based on the cases in this set.
            var clauseSlice = Core.Slice(clauses, data.ClauseStart, data.ClauseEnd);
            return GetUnionType(TypeList(Core.Map(clauseSlice, (clause, _) => clause.Kind == SyntaxKind.CaseClause ? NarrowType(type, ((CaseClause)clause).Expression!, /*assumeTrue*/ true) : _neverType)));
        }

        bool IsMatchingConstructorReference(Expression expr)
        {
            return (NodeTests.IsPropertyAccessExpression(expr) && Utilities.IdText(((PropertyAccessExpression)expr).Name!) == "constructor" ||
                NodeTests.IsElementAccessExpression(expr) && Utilities.IsStringLiteralLike(((ElementAccessExpression)expr).ArgumentExpression!) && ((LiteralExpression)((ElementAccessExpression)expr).ArgumentExpression!).Text == "constructor") &&
                IsMatchingReference(reference, Utilities.GetAccessExpressionExpression(expr));
        }

        IType NarrowTypeByConstructor(IType type, SyntaxKind @operator, Expression identifier, bool assumeTrue)
        {
            // Do not narrow when checking inequality.
            if (assumeTrue ? @operator != SyntaxKind.EqualsEqualsToken && @operator != SyntaxKind.EqualsEqualsEqualsToken : @operator != SyntaxKind.ExclamationEqualsToken && @operator != SyntaxKind.ExclamationEqualsEqualsToken)
            {
                return type;
            }

            // Get the type of the constructor identifier expression, if it is not a function then do not narrow.
            var identifierType = GetTypeOfExpression(identifier);
            if (!IsFunctionType(identifierType) && !IsConstructorType(identifierType))
            {
                return type;
            }

            // Get the prototype property of the type identifier so we can find out its type.
            var prototypeProperty = GetPropertyOfType(identifierType, "prototype");
            if (prototypeProperty is null)
            {
                return type;
            }

            // Get the type of the prototype, if it is undefined, or the global `Object` or `Function` types then do not narrow.
            var prototypeType = GetTypeOfSymbol(prototypeProperty);
            var candidate = !IsTypeAny(prototypeType) ? prototypeType : null;
            if (candidate is null || ReferenceEquals(candidate, _globalObjectType) || ReferenceEquals(candidate, _globalFunctionType))
            {
                return type;
            }

            // If the type that is being narrowed is `any` then just return the `candidate` type since every type is a subtype of `any`.
            if (IsTypeAny(type))
            {
                return candidate;
            }

            // Filter out types that are not considered to be "constructed by" the `candidate` type.
            return FilterType(type, t => IsConstructedBy(t, candidate));

            // If either the source or target type are a class type then we need to check that they are the same exact type.
            // This is because you may have a class `A` that defines some set of properties, and another class `B`
            // that defines the same set of properties as class `A`, in that case they are structurally the same
            // type, but when you do something like `instanceOfA.constructor === B` it will return false.
            bool IsConstructedBy(IType source, IType target)
            {
                if (
                    (source.Flags & TypeFlags.Object) != 0 && (GetObjectFlags(source) & ObjectFlags.Class) != 0 ||
                    (target.Flags & TypeFlags.Object) != 0 && (GetObjectFlags(target) & ObjectFlags.Class) != 0
                )
                {
                    return ReferenceEquals(source.Symbol, target.Symbol);
                }

                // For all other types just check that the `source` type is a subtype of the `target` type.
                return IsTypeSubtypeOf(source, target);
            }
        }

        IType NarrowTypeByInstanceof(IType type, BinaryExpression expr, bool assumeTrue)
        {
            var left = GetReferenceCandidate(expr.Left!);
            if (!IsMatchingReference(reference, left))
            {
                if (assumeTrue && _strictNullChecks && OptionalChainContainsReference(left, reference))
                {
                    return GetAdjustedTypeWithFacts(type, TypeFacts.NEUndefinedOrNull);
                }

                return type;
            }

            var right = expr.Right!;
            var rightType = GetTypeOfExpression(right);
            if (!IsTypeDerivedFrom(rightType, _globalObjectType))
            {
                return type;
            }

            // if the right-hand side has an object type with a custom `[Symbol.hasInstance]` method, and that method
            // has a type predicate, use the type predicate to perform narrowing. This allows normal `object` types to
            // participate in `instanceof`, as per Step 2 of https://tc39.es/ecma262/#sec-instanceofoperator.
            var signature = GetEffectsSignature(expr);
            var predicate = signature is not null ? GetTypePredicateOfSignature(signature) : null;
            if (predicate is not null && predicate.Kind == TypePredicateKind.Identifier && ((IIdentifierTypePredicate)predicate).ParameterIndex == 0)
            {
                return GetNarrowedType(type, predicate.Type!, assumeTrue, /*checkDerived*/ true);
            }

            if (!IsTypeDerivedFrom(rightType, _globalFunctionType))
            {
                return type;
            }

            var instanceType = MapType(rightType, GetInstanceType)!;
            // Don't narrow from `any` if the target type is exactly `Object` or `Function`, and narrow
            // in the false branch only if the target is a non-empty object type.
            if (
                IsTypeAny(type) && (ReferenceEquals(instanceType, _globalObjectType) || ReferenceEquals(instanceType, _globalFunctionType)) ||
                !assumeTrue && !((instanceType.Flags & TypeFlags.Object) != 0 && !IsEmptyAnonymousObjectType(instanceType))
            )
            {
                return type;
            }

            return GetNarrowedType(type, instanceType, assumeTrue, /*checkDerived*/ true);
        }

        IType GetInstanceType(IType constructorType)
        {
            var prototypePropertyType = GetTypeOfPropertyOfType(constructorType, "prototype");
            if (prototypePropertyType is not null && !IsTypeAny(prototypePropertyType))
            {
                return prototypePropertyType;
            }

            var constructSignatures = GetSignaturesOfType(constructorType, SignatureKind.Construct);
            if (constructSignatures.Count != 0)
            {
                return GetUnionType(TypeList(Core.Map(constructSignatures, (signature, _) => GetReturnTypeOfSignature(GetErasedSignature(signature)))));
            }

            // We use the empty object type to indicate we don't know the type of objects created by
            // this constructor function.
            return _emptyObjectType;
        }

        IType GetNarrowedType(IType type, IType candidate, bool assumeTrue, bool checkDerived)
        {
            var narrowedKey = (type.Flags & TypeFlags.Union) != 0 ? $"N{GetTypeId(type)},{GetTypeId(candidate)},{(assumeTrue ? 1 : 0) | (checkDerived ? 2 : 0)}" : null;
            return GetCachedType(narrowedKey) ?? SetCachedType(narrowedKey, GetNarrowedTypeWorker(type, candidate, assumeTrue, checkDerived));
        }

        IType GetNarrowedTypeWorker(IType type, IType candidate, bool assumeTrue, bool checkDerived)
        {
            if (!assumeTrue)
            {
                if (ReferenceEquals(type, candidate))
                {
                    return _neverType;
                }

                if (checkDerived)
                {
                    return FilterType(type, t => !IsTypeDerivedFrom(t, candidate));
                }

                type = (type.Flags & TypeFlags.Unknown) != 0 ? _unknownUnionType : type;
                var trueType = GetNarrowedType(type, candidate, /*assumeTrue*/ true, /*checkDerived*/ false);
                return RecombineUnknownType(FilterType(type, t => !IsTypeSubsetOf(t, trueType)));
            }

            if ((type.Flags & TypeFlags.AnyOrUnknown) != 0)
            {
                return candidate;
            }

            if (ReferenceEquals(type, candidate))
            {
                return candidate;
            }

            // We first attempt to filter the current type, narrowing constituents as appropriate and removing
            // constituents that are unrelated to the candidate.
            Func<IType, IType, bool> isRelated = checkDerived ? IsTypeDerivedFrom : IsTypeSubtypeOf;
            var keyPropertyName = (type.Flags & TypeFlags.Union) != 0 ? GetKeyPropertyName((IUnionType)type) : null;
            var narrowedType = MapType(candidate, c =>
            {
                // If a discriminant property is available, use that to reduce the type.
                var discriminant = keyPropertyName is not null ? GetTypeOfPropertyOfType(c, keyPropertyName) : null;
                var matching = discriminant is not null ? GetConstituentTypeForKeyType((IUnionType)type, discriminant) : null;
                // For each constituent t in the current type, if t and and c are directly related, pick the most
                // specific of the two. When t and c are related in both directions, we prefer c for type predicates
                // because that is the asserted type, but t for `instanceof` because generics aren't reflected in
                // prototype object types.
                var directlyRelated = MapType(
                    matching
 ?? type,
                    checkDerived
                        ? t => IsTypeDerivedFrom(t, c) ? t : IsTypeDerivedFrom(c, t) ? c : _neverType
                        : t => IsTypeStrictSubtypeOf(t, c) ? t : IsTypeStrictSubtypeOf(c, t) ? c : IsTypeSubtypeOf(t, c) ? t : IsTypeSubtypeOf(c, t) ? c : _neverType)!;
                // If no constituents are directly related, create intersections for any generic constituents that
                // are related by constraint.
                return (directlyRelated.Flags & TypeFlags.Never) != 0
                    ? MapType(type, t => MaybeTypeOfKind(t, TypeFlags.Instantiable) && isRelated(c, GetBaseConstraintOfType(t) ?? _unknownType) ? GetIntersectionType([t, c]) : _neverType)!
                    : directlyRelated;
            })!;
            // If filtering produced a non-empty type, return that. Otherwise, pick the most specific of the two
            // based on assignability, or as a last resort produce an intersection.
            return (narrowedType.Flags & TypeFlags.Never) == 0 ? narrowedType :
                IsTypeSubtypeOf(candidate, type) ? candidate :
                IsTypeAssignableTo(type, candidate) ? type :
                IsTypeAssignableTo(candidate, type) ? candidate :
                GetIntersectionType([type, candidate]);
        }

        IType NarrowTypeByCallExpression(IType type, CallExpression callExpression, bool assumeTrue)
        {
            if (HasMatchingArgument(callExpression, reference))
            {
                var signature = assumeTrue || !Utilities.IsCallChain(callExpression) ? GetEffectsSignature(callExpression) : null;
                var predicate = signature is not null ? GetTypePredicateOfSignature(signature) : null;
                if (predicate is not null && (predicate.Kind == TypePredicateKind.This || predicate.Kind == TypePredicateKind.Identifier))
                {
                    return NarrowTypeByTypePredicate(type, predicate, callExpression, assumeTrue);
                }
            }

            if (ContainsMissingType(type) && Utilities.IsAccessExpression(reference) && NodeTests.IsPropertyAccessExpression(callExpression.Expression!))
            {
                var callAccess = (PropertyAccessExpression)callExpression.Expression!;
                if (
                    IsMatchingReference(Utilities.GetAccessExpressionExpression((Expression)reference), GetReferenceCandidate(callAccess.Expression!)) &&
                    NodeTests.IsIdentifier(callAccess.Name!) && ((Identifier)callAccess.Name!).EscapedText == "hasOwnProperty" && callExpression.Arguments!.Count == 1
                )
                {
                    var argument = callExpression.Arguments![0];
                    if (Utilities.IsStringLiteralLike(argument) && GetAccessedPropertyName(reference) == Utilities.EscapeLeadingUnderscores(((LiteralExpression)argument).Text!))
                    {
                        return GetTypeWithFacts(type, assumeTrue ? TypeFacts.NEUndefined : TypeFacts.EQUndefined);
                    }
                }
            }

            return type;
        }

        IType NarrowTypeByTypePredicate(IType type, ITypePredicateBase predicate, CallExpression callExpression, bool assumeTrue)
        {
            // Don't narrow from 'any' if the predicate type is exactly 'Object' or 'Function'
            if (predicate.Type is not null && !(IsTypeAny(type) && (ReferenceEquals(predicate.Type, _globalObjectType) || ReferenceEquals(predicate.Type, _globalFunctionType))))
            {
                var predicateArgument = GetTypePredicateArgument(predicate, callExpression);
                if (predicateArgument is not null)
                {
                    if (IsMatchingReference(reference, predicateArgument))
                    {
                        return GetNarrowedType(type, predicate.Type, assumeTrue, /*checkDerived*/ false);
                    }

                    if (
                        _strictNullChecks && OptionalChainContainsReference(predicateArgument, reference) &&
                        (
                            assumeTrue && !HasTypeFacts(predicate.Type, TypeFacts.EQUndefined) ||
                            !assumeTrue && EveryType(predicate.Type, IsNullableType)
                        )
                    )
                    {
                        type = GetAdjustedTypeWithFacts(type, TypeFacts.NEUndefinedOrNull);
                    }

                    var access = GetDiscriminantPropertyAccess((Expression)predicateArgument, type);
                    if (access is not null)
                    {
                        return NarrowTypeByDiscriminant(type, access, t => GetNarrowedType(t, predicate.Type, assumeTrue, /*checkDerived*/ false));
                    }
                }
            }

            return type;
        }

        // Narrow the given type based on the given expression having the assumed boolean value. The returned type
        // will be a subtype or the same type as the argument.
        IType NarrowType(IType type, Expression expr, bool assumeTrue)
        {
            // for `a?.b`, we emulate a synthetic `a !== null && a !== undefined` condition for `a`
            if (
                Utilities.IsExpressionOfOptionalChainRoot(expr) ||
                NodeTests.IsBinaryExpression(expr.Parent!) && (((BinaryExpression)expr.Parent!).OperatorToken!.Kind == SyntaxKind.QuestionQuestionToken || ((BinaryExpression)expr.Parent!).OperatorToken!.Kind == SyntaxKind.QuestionQuestionEqualsToken) && ReferenceEquals(((BinaryExpression)expr.Parent!).Left, expr)
            )
            {
                return NarrowTypeByOptionality(type, expr, assumeTrue);
            }

            switch (expr.Kind)
            {
                case SyntaxKind.Identifier:
                    // When narrowing a reference to a const variable, non-assigned parameter, or readonly property, we inline
                    // up to five levels of aliased conditional expressions that are themselves declared as const variables.
                    if (!IsMatchingReference(reference, expr) && _inlineLevel < 5)
                    {
                        var symbol = GetResolvedSymbol((Identifier)expr);
                        if (IsConstantVariable(symbol))
                        {
                            var declaration = symbol.ValueDeclaration;
                            if (declaration is not null && NodeTests.IsVariableDeclaration((Node)declaration) && ((VariableDeclaration)declaration).Type is null && ((VariableDeclaration)declaration).Initializer is not null && IsConstantReference(reference))
                            {
                                _inlineLevel++;
                                var result = NarrowType(type, ((VariableDeclaration)declaration).Initializer!, assumeTrue);
                                _inlineLevel--;
                                return result;
                            }
                        }
                    }

                    // falls through
                    goto case SyntaxKind.ThisKeyword;
                case SyntaxKind.ThisKeyword:
                case SyntaxKind.SuperKeyword:
                case SyntaxKind.PropertyAccessExpression:
                case SyntaxKind.ElementAccessExpression:
                    return NarrowTypeByTruthiness(type, expr, assumeTrue);
                case SyntaxKind.CallExpression:
                    return NarrowTypeByCallExpression(type, (CallExpression)expr, assumeTrue);
                case SyntaxKind.ParenthesizedExpression:
                case SyntaxKind.NonNullExpression:
                case SyntaxKind.SatisfiesExpression:
                    // PORT-NOTE: TS's `(expr as ParenthesizedExpression | NonNullExpression | SatisfiesExpression).expression`
                    // reads one member of a three-member union; the port's three classes share no base, so the read is
                    // this kind switch (the same treatment `Binder.Narrowing.cs`'s `GetInnerExpression` records).
                    return NarrowType(type, expr switch
                    {
                        ParenthesizedExpression parenthesized => parenthesized.Expression!,
                        NonNullExpression nonNull => nonNull.Expression!,
                        _ => ((SatisfiesExpression)expr).Expression!,
                    }, assumeTrue);
                case SyntaxKind.BinaryExpression:
                    return NarrowTypeByBinaryExpression(type, (BinaryExpression)expr, assumeTrue);
                case SyntaxKind.PrefixUnaryExpression:
                    if (((PrefixUnaryExpression)expr).Operator == SyntaxKind.ExclamationToken)
                    {
                        return NarrowType(type, ((PrefixUnaryExpression)expr).Operand!, !assumeTrue);
                    }

                    break;
            }

            return type;
        }

        IType NarrowTypeByOptionality(IType type, Expression expr, bool assumePresent)
        {
            if (IsMatchingReference(reference, expr))
            {
                return GetAdjustedTypeWithFacts(type, assumePresent ? TypeFacts.NEUndefinedOrNull : TypeFacts.EQUndefinedOrNull);
            }

            var access = GetDiscriminantPropertyAccess(expr, type);
            if (access is not null)
            {
                return NarrowTypeByDiscriminant(type, access, t => GetTypeWithFacts(t, assumePresent ? TypeFacts.NEUndefinedOrNull : TypeFacts.EQUndefinedOrNull));
            }

            return type;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:30265-30270 — <c>isSymbolAssignedDefinitely</c>.</summary>
    private bool IsSymbolAssignedDefinitely(ISymbol symbol)
    {
        if (symbol.LastAssignmentPos is not null)
        {
            return symbol.LastAssignmentPos < 0;
        }
        return IsSymbolAssigned(symbol) && symbol.LastAssignmentPos is not null && symbol.LastAssignmentPos < 0;
    }

    /// <summary>ported from: src/compiler/checker.ts:30273-30275 — <c>isSymbolAssigned</c>.</summary>
    internal bool IsSymbolAssigned(ISymbol symbol) => !IsPastLastAssignment(symbol, /*location*/ null);

    /// <summary>ported from: src/compiler/checker.ts:30279-30292 — <c>isPastLastAssignment</c>.</summary>
    private bool IsPastLastAssignment(ISymbol symbol, Node? location)
    {
        var parent = Utilities.FindAncestor((Node?)symbol.ValueDeclaration, IsFunctionOrSourceFile);
        if (parent is null)
        {
            return false;
        }
        var links = GetNodeLinks(parent);
        if ((links.Flags & NodeCheckFlags.AssignmentsMarked) == 0)
        {
            links.Flags |= NodeCheckFlags.AssignmentsMarked;
            if (!HasParentWithAssignmentsMarked(parent))
            {
                MarkNodeAssignments(parent);
            }
        }
        return symbol.LastAssignmentPos is null or 0 || location is not null && Math.Abs(symbol.LastAssignmentPos.Value) < location.Pos;
    }

    /// <summary>ported from: src/compiler/checker.ts:30295-30298 — <c>isSomeSymbolAssigned</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's parameter is a <see cref="Node"/> whose <c>Debug.assert</c> establishes
    /// <c>VariableDeclaration | ParameterDeclaration</c> and whose body reads the <c>name</c> slot both classes carry;
    /// the port's two classes share no type below <see cref="Node"/>, so the read is this cast pair (the same
    /// treatment the landed JSX callers give that union's <c>attributes</c>).
    /// </remarks>
    private bool IsSomeSymbolAssigned(Node rootDeclaration)
    {
        Debug.Assert(NodeTests.IsVariableDeclaration(rootDeclaration) || NodeTests.IsParameter(rootDeclaration));
        return IsSomeSymbolAssignedWorker(((rootDeclaration as VariableDeclaration)?.Name ?? (rootDeclaration as ParameterDeclaration)?.Name)!);
    }

    /// <summary>ported from: src/compiler/checker.ts:30300-30306 — <c>isSomeSymbolAssignedWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>BindingName = Identifier | BindingPattern</c>; the port's binding patterns share no base
    /// class (Model/NodeClasses.cs), so the array-pattern arm narrows by kind and reads <c>elements</c> off the two
    /// faces exactly as the reference's structural access did.
    /// </remarks>
    private bool IsSomeSymbolAssignedWorker(Node node)
    {
        if (node.Kind == SyntaxKind.Identifier)
        {
            return IsSymbolAssigned(GetSymbolOfDeclaration((IDeclaration)node.Parent!));
        }

        return node.Kind == SyntaxKind.ObjectBindingPattern
            ? Core.Some(((ObjectBindingPattern)node).Elements!, e => IsSomeSymbolAssignedWorker(e.Name!))
            : Core.Some(((ArrayBindingPattern)node).Elements!, e => e.Kind != SyntaxKind.OmittedExpression && IsSomeSymbolAssignedWorker(((BindingElement)e).Name!));
    }

    /// <summary>ported from: src/compiler/checker.ts:30308-30310 — <c>hasParentWithAssignmentsMarked</c>.</summary>
    private bool HasParentWithAssignmentsMarked(Node node)
        => Utilities.FindAncestor((Node?)node.Parent, node => IsFunctionOrSourceFile(node) && (GetNodeLinks(node).Flags & NodeCheckFlags.AssignmentsMarked) != 0) is not null;

    /// <summary>ported from: src/compiler/checker.ts:30312-30314 — <c>isFunctionOrSourceFile</c>.</summary>
    private bool IsFunctionOrSourceFile(Node node) => Utilities.IsFunctionLike(node) || NodeTests.IsSourceFile(node);

    /// <summary>ported from: src/compiler/checker.ts:30322-30361 — <c>markNodeAssignments</c>.</summary>
    private void MarkNodeAssignments(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.Identifier:
                var assigmentTarget = Utilities.GetAssignmentTargetKind(node);
                if (assigmentTarget != AssignmentKind.None)
                {
                    var symbol = GetResolvedSymbol((Identifier)node);
                    var hasDefiniteAssignment = assigmentTarget == AssignmentKind.Definite || (symbol.LastAssignmentPos is not null && symbol.LastAssignmentPos < 0);
                    if (IsParameterOrMutableLocalVariable(symbol))
                    {
                        if (symbol.LastAssignmentPos is null || Math.Abs(symbol.LastAssignmentPos.Value) != int.MaxValue)
                        {
                            var referencingFunction = Utilities.FindAncestor(node, IsFunctionOrSourceFile);
                            var declaringFunction = Utilities.FindAncestor((Node?)symbol.ValueDeclaration, IsFunctionOrSourceFile);
                            symbol.LastAssignmentPos = ReferenceEquals(referencingFunction, declaringFunction)
                                ? ExtendAssignmentPosition(node, (Node)symbol.ValueDeclaration!)
                                : int.MaxValue;
                        }
                        if (hasDefiniteAssignment && symbol.LastAssignmentPos > 0)
                        {
                            symbol.LastAssignmentPos *= -1;
                        }
                    }
                }
                return;
            case SyntaxKind.ExportSpecifier:
                var exportDeclaration = (ExportDeclaration)((ExportSpecifier)node).Parent!.Parent!;
                var name = ((ExportSpecifier)node).PropertyName ?? ((ExportSpecifier)node).Name!;
                if (!Js.IsTruthy(((ExportSpecifier)node).IsTypeOnly) && !Js.IsTruthy(exportDeclaration.IsTypeOnly) && exportDeclaration.ModuleSpecifier is null && name.Kind != SyntaxKind.StringLiteral)
                {
                    var symbol = ResolveEntityName(name, SymbolFlags.Value, /*ignoreErrors*/ true, /*dontResolveAlias*/ true);
                    if (symbol is not null && IsParameterOrMutableLocalVariable(symbol))
                    {
                        var sign = symbol.LastAssignmentPos is not null && symbol.LastAssignmentPos < 0 ? -1 : 1;
                        symbol.LastAssignmentPos = sign * int.MaxValue;
                    }
                }
                return;
            case SyntaxKind.InterfaceDeclaration:
            case SyntaxKind.TypeAliasDeclaration:
            case SyntaxKind.EnumDeclaration:
                return;
        }
        if (Utilities.IsTypeNode(node))
        {
            return;
        }
        ForEachChildTable.ForEachChild(node, MarkNodeAssignments);
    }

    /// <summary>ported from: src/compiler/checker.ts:30366-30387 — <c>extendAssignmentPosition</c>.</summary>
    private static int ExtendAssignmentPosition(Node node, Node declaration)
    {
        var pos = node.Pos;
        while (node is not null && node.Pos > declaration.Pos)
        {
            switch (node.Kind)
            {
                case SyntaxKind.VariableStatement:
                case SyntaxKind.ExpressionStatement:
                case SyntaxKind.IfStatement:
                case SyntaxKind.DoStatement:
                case SyntaxKind.WhileStatement:
                case SyntaxKind.ForStatement:
                case SyntaxKind.ForInStatement:
                case SyntaxKind.ForOfStatement:
                case SyntaxKind.WithStatement:
                case SyntaxKind.SwitchStatement:
                case SyntaxKind.TryStatement:
                case SyntaxKind.ClassDeclaration:
                    pos = node.End;
                    break;
            }
            node = node.Parent!;
        }
        return pos;
    }

    /// <summary>ported from: src/compiler/checker.ts:30389-30391 — <c>isConstantVariable</c>.</summary>
    internal bool IsConstantVariable(ISymbol symbol)
        => (symbol.Flags & SymbolFlags.Variable) != 0 && (GetDeclarationNodeFlagsFromSymbol(symbol) & NodeFlags.Constant) != 0;

    /// <summary>ported from: src/compiler/checker.ts:30393-30400 — <c>isParameterOrMutableLocalVariable</c>.</summary>
    private bool IsParameterOrMutableLocalVariable(ISymbol symbol)
    {
        // Return true if symbol is a parameter, a catch clause variable, or a mutable local variable
        var declaration = symbol.ValueDeclaration is not null ? Utilities.GetRootDeclaration((Node)symbol.ValueDeclaration) : null;
        return declaration is not null && (
            NodeTests.IsParameter(declaration) ||
            NodeTests.IsVariableDeclaration(declaration) && (NodeTests.IsCatchClause(declaration.Parent!) || IsMutableLocalVariableDeclaration((VariableDeclaration)declaration))
        );
    }

    /// <summary>ported from: src/compiler/checker.ts:30402-30408 — <c>isMutableLocalVariableDeclaration</c>.</summary>
    private static bool IsMutableLocalVariableDeclaration(VariableDeclaration declaration)
    {
        // Return true if symbol is a non-exported and non-global `let` variable
        return (declaration.Parent!.Flags & NodeFlags.Let) != 0 && !(
            (Utilities.GetCombinedModifierFlags(declaration) & ModifierFlags.Export) != 0 ||
            declaration.Parent.Parent!.Kind == SyntaxKind.VariableStatement && Utilities.IsGlobalSourceFile(declaration.Parent.Parent.Parent!)
        );
    }

    /// <summary>ported from: src/compiler/checker.ts:30410-30431 — <c>parameterInitializerContainsUndefined</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the two <c>reportCircularityError</c> calls of the reference's failure paths go through the same
    /// member the rest of the port uses (<c>ReportCircularityError</c>), and the linked slot is the same tri-state
    /// <c>bool?</c> slot TS spells <c>boolean | undefined</c>.
    /// </remarks>
    private bool ParameterInitializerContainsUndefined(ParameterDeclaration declaration)
    {
        var links = GetNodeLinks(declaration);

        if (links.ParameterInitializerContainsUndefined is null)
        {
            if (!PushTypeResolution(declaration, TypeSystemPropertyName.ParameterInitializerContainsUndefined))
            {
                ReportCircularityError(declaration.Symbol!);
                return true;
            }

            var containsUndefined = (GetTypeFacts(CheckDeclarationInitializer(declaration, CheckMode.Normal), TypeFacts.IsUndefined) & TypeFacts.IsUndefined) != 0;

            if (!PopTypeResolution())
            {
                ReportCircularityError(declaration.Symbol!);
                return true;
            }

            links.ParameterInitializerContainsUndefined = containsUndefined;
        }

        return links.ParameterInitializerContainsUndefined.Value;
    }

    /// <summary>ported from: src/compiler/checker.ts:30433-30441 — <c>removeOptionalityFromDeclaredType</c>.</summary>
    private IType RemoveOptionalityFromDeclaredType(IType declaredType, IDeclaration declarationIn)
    {
        // PORT-NOTE: TS's parameter is the `VariableLikeDeclaration` union; the port's callers hold an `IDeclaration`
        // (the symbol's value declaration), so the slot stays `IDeclaration` and the two reads the body makes go
        // through the node it actually holds.
        var declaration = (Node)declarationIn;
        var removeUndefined = _strictNullChecks &&
            declaration.Kind == SyntaxKind.Parameter &&
            ((ParameterDeclaration)declaration).Initializer is not null &&
            HasTypeFacts(declaredType, TypeFacts.IsUndefined) &&
            !ParameterInitializerContainsUndefined((ParameterDeclaration)declaration);

        return removeUndefined ? GetTypeWithFacts(declaredType, TypeFacts.NEUndefined) : declaredType;
    }

    /// <summary>ported from: src/compiler/checker.ts:30443-30454 — <c>isConstraintPosition</c>.</summary>
    private bool IsConstraintPosition(IType type, Node node)
    {
        var parent = node.Parent!;
        // In an element access obj[x], we consider obj to be in a constraint position, except when obj is of
        // a generic type without a nullable constraint and x is a generic type. This is because when both obj
        // and x are of generic types T and K, we want the resulting type to be T[K].
        return parent.Kind == SyntaxKind.PropertyAccessExpression ||
            parent.Kind == SyntaxKind.QualifiedName ||
            parent.Kind == SyntaxKind.CallExpression && ((CallExpression)parent).Expression == node ||
            parent.Kind == SyntaxKind.NewExpression && ((NewExpression)parent).Expression == node ||
            parent.Kind == SyntaxKind.ElementAccessExpression && ((ElementAccessExpression)parent).Expression == node &&
                !(SomeType(type, IsGenericTypeWithoutNullableConstraint) && IsGenericIndexType(GetTypeOfExpression(((ElementAccessExpression)parent).ArgumentExpression!)));
    }

    /// <summary>ported from: src/compiler/checker.ts:30456-30460 — <c>isGenericTypeWithUnionConstraint</c>.</summary>
    private bool IsGenericTypeWithUnionConstraint(IType type)
    {
        return (type.Flags & TypeFlags.Intersection) != 0
            ? Core.Some(((IIntersectionType)type).Types!, IsGenericTypeWithUnionConstraint)
            : (type.Flags & TypeFlags.Instantiable) != 0 && (GetBaseConstraintOrType(type).Flags & (TypeFlags.Nullable | TypeFlags.Union)) != 0;
    }

    /// <summary>ported from: src/compiler/checker.ts:30462-30466 — <c>isGenericTypeWithoutNullableConstraint</c>.</summary>
    private bool IsGenericTypeWithoutNullableConstraint(IType type)
    {
        return (type.Flags & TypeFlags.Intersection) != 0
            ? Core.Some(((IIntersectionType)type).Types!, IsGenericTypeWithoutNullableConstraint)
            : (type.Flags & TypeFlags.Instantiable) != 0 && !MaybeTypeOfKind(GetBaseConstraintOrType(type), TypeFlags.Nullable);
    }

    /// <summary>ported from: src/compiler/checker.ts:30468-30479 — <c>hasContextualTypeWithNoGenericTypes</c>.</summary>
    private bool HasContextualTypeWithNoGenericTypes(Node node, CheckMode? checkMode)
    {
        // Computing the contextual type for a child of a JSX element involves resolving the type of the
        // element's tag name, so we exclude that here to avoid circularities.
        // If check mode has `CheckMode.RestBindingElement`, we skip binding pattern contextual types,
        // as we want the type of a rest element to be generic when possible.
        // PORT-NOTE: TS's `&&` chain evaluates to the last operand (the contextual type); C# states the chain as
        // the condition of the ternary that yields it.
        var contextualType = (NodeTests.IsIdentifier(node) || NodeTests.IsPropertyAccessExpression(node) || NodeTests.IsElementAccessExpression(node)) &&
            !((NodeTests.IsJsxOpeningElement(node.Parent!) || NodeTests.IsJsxSelfClosingElement(node.Parent!)) && ((JsxOpeningElement)node.Parent!).TagName == node)
            ? (checkMode is not null && (checkMode & CheckMode.RestBindingElement) != 0
                ? GetContextualType((Expression)node, ContextFlags.SkipBindingPatterns)
                : GetContextualType((Expression)node, /*contextFlags*/ null))
            : null;
        return contextualType is not null && !IsGenericType(contextualType);
    }

    /// <summary>ported from: src/compiler/checker.ts:30481-30496 — <c>getNarrowableTypeForReference</c>.</summary>
    private IType GetNarrowableTypeForReference(IType type, Node reference, CheckMode? checkMode = null)
    {
        if (IsNoInferType(type))
        {
            type = ((ISubstitutionType)type).BaseType!;
        }
        // When the type of a reference is or contains an instantiable type with a union type constraint, and
        // when the reference is in a constraint position (where it is known we'll obtain the apparent type) or
        // has a contextual type containing no top-level instantiables (meaning constraints will determine
        // assignability), we substitute constraints for all instantiables in the type of the reference to give
        // control flow analysis an opportunity to narrow it further.
        var substituteConstraints = !(checkMode is not null && (checkMode & CheckMode.Inferential) != 0) &&
            SomeType(type, IsGenericTypeWithUnionConstraint) &&
            (IsConstraintPosition(type, reference) || HasContextualTypeWithNoGenericTypes(reference, checkMode));
        return substituteConstraints ? MapType(type, GetBaseConstraintOrType)! : type;
    }

    /// <summary>ported from: src/compiler/checker.ts:30498-30512 — <c>isExportOrExportExpression</c>.</summary>
    private bool IsExportOrExportExpression(Node location)
    {
        return Utilities.FindAncestor(location, n =>
        {
            var parent = n.Parent;
            if (parent is null)
            {
                return null;
            }
            if (NodeTests.IsExportAssignment(parent))
            {
                return ((ExportAssignment)parent).Expression == n && Utilities.IsEntityNameExpression(n);
            }
            if (NodeTests.IsExportSpecifier(parent))
            {
                return ((ExportSpecifier)parent).Name == n || ((ExportSpecifier)parent).PropertyName == n;
            }
            return false;
        }) is not null;
    }

    /// <summary>ported from: src/compiler/checker.ts:34756-34761 — <c>isMethodAccessForCall</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>node.parent.expression === node</c> reads one member of the
    /// <c>CallExpression | NewExpression</c> union that <c>isCallOrNewExpression</c> has just established; the port's
    /// two classes are siblings below <see cref="Node"/> and share no type, so the read is this kind switch, exactly
    /// where the reference's structural access made it.
    /// </remarks>
    private static bool IsMethodAccessForCall(Node node)
    {
        while (node.Parent!.Kind == SyntaxKind.ParenthesizedExpression)
        {
            node = node.Parent;
        }
        var parent = node.Parent!;
        if (!Utilities.IsCallOrNewExpression(parent))
        {
            return false;
        }
        return parent switch
        {
            CallExpression call => call.Expression == node,
            _ => ((NewExpression)parent).Expression == node,
        };
    }

    /// <summary>ported from: src/compiler/checker.ts:30526-30612 — <c>markLinkedReferences</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference states nine overloads whose bodies are this general one; C# optional parameters state the
    /// same set of call shapes (the landed call sites pass the location and the hint, and <c>EmitResolver</c>'s forwarder
    /// passes <c>ReferenceHint.Unspecified</c>), so there is one member here, with the reference's own defaults.
    /// </remarks>
    internal void MarkLinkedReferences(Node location, ReferenceHint hint = ReferenceHint.Unspecified, ISymbol? propSymbol = null, IType? parentType = null)
    {
        if (!_canCollectSymbolAliasAccessabilityData)
        {
            return;
        }
        if ((location.Flags & NodeFlags.Ambient) != 0 && !NodeTests.IsPropertySignature(location) && !NodeTests.IsPropertyDeclaration(location))
        {
            // References within types and declaration files are never going to contribute to retaining a JS import,
            // except for properties (which can be decorated).
            return;
        }
        switch (hint)
        {
            case ReferenceHint.Identifier:
                MarkIdentifierAliasReferenced((Identifier)location);
                return;
            case ReferenceHint.Property:
                MarkPropertyAliasReferenced(location, propSymbol, parentType);
                return;
            case ReferenceHint.ExportAssignment:
                MarkExportAssignmentAliasReferenced((ExportAssignment)location);
                return;
            case ReferenceHint.Jsx:
                MarkJsxAliasReferenced(location);
                return;
            case ReferenceHint.AsyncFunction:
                MarkAsyncFunctionAliasReferenced(location);
                return;
            case ReferenceHint.ExportImportEquals:
                MarkImportEqualsAliasReferenced((ImportEqualsDeclaration)location);
                return;
            case ReferenceHint.ExportSpecifier:
                MarkExportSpecifierAliasReferenced((ExportSpecifier)location);
                return;
            case ReferenceHint.Decorator:
                MarkDecoratorAliasReferenced(location);
                return;
            case ReferenceHint.Unspecified:
                {
                    // Identifiers in expression contexts are emitted, so we need to follow their referenced aliases and
                    // mark them as used. Some non-expression identifiers are also treated as expression identifiers for
                    // this purpose, eg, `a` in `b = {a}` or `q` in `import r = q`. This is the exception, rather than the
                    // rule - most non-expression identifiers are declaration names.
                    if (NodeTests.IsIdentifier(location)
                        && (Utilities.IsExpressionNode(location) || NodeTests.IsShorthandPropertyAssignment(location.Parent!)
                            || NodeTests.IsImportEqualsDeclaration(location.Parent!) && ((ImportEqualsDeclaration)location.Parent!).ModuleReference == location)
                        && ShouldMarkIdentifierAliasReferenced((Identifier)location))
                    {
                        if (Utilities.IsPropertyAccessOrQualifiedName(location.Parent!))
                        {
                            // Only mark the LHS (the RHS is a property lookup)
                            var left = NodeTests.IsPropertyAccessExpression(location.Parent!) ? ((PropertyAccessExpression)location.Parent!).Expression : ((QualifiedName)location.Parent!).Left;
                            if (left != location) return;
                        }
                        MarkIdentifierAliasReferenced((Identifier)location);
                        return;
                    }
                    if (Utilities.IsPropertyAccessOrQualifiedName(location))
                    {
                        Node topProp = location;
                        while (Utilities.IsPropertyAccessOrQualifiedName(topProp))
                        {
                            if (Utilities.IsPartOfTypeNode(topProp)) return;
                            topProp = topProp.Parent!;
                        }
                        MarkPropertyAliasReferenced(location, null, null);
                        return;
                    }
                    if (NodeTests.IsExportAssignment(location))
                    {
                        MarkExportAssignmentAliasReferenced((ExportAssignment)location);
                        return;
                    }
                    if (Utilities.IsJsxOpeningLikeElement(location) || NodeTests.IsJsxOpeningFragment(location))
                    {
                        MarkJsxAliasReferenced(location);
                        return;
                    }
                    if (NodeTests.IsImportEqualsDeclaration(location))
                    {
                        if (Utilities.IsInternalModuleImportEqualsDeclaration(location) || CheckExternalImportOrExportDeclaration(location))
                        {
                            MarkImportEqualsAliasReferenced((ImportEqualsDeclaration)location);
                        }
                        return;
                    }
                    if (NodeTests.IsExportSpecifier(location))
                    {
                        MarkExportSpecifierAliasReferenced((ExportSpecifier)location);
                        return;
                    }
                    if (Utilities.IsFunctionLike(location) || NodeTests.IsMethodSignature(location))
                    {
                        MarkAsyncFunctionAliasReferenced(location);
                        // Might be decorated, fall through to decorator final case
                    }
                    if (!Js.IsTruthy(_compilerOptions.EmitDecoratorMetadata))
                    {
                        return;
                    }
                    if (!Utilities.CanHaveDecorators(location) || !Utilities.HasDecorators(location) || GetModifiersOfNode(location) is null
                        || !Utilities.NodeCanBeDecorated(_legacyDecorators, location, location.Parent!, location.Parent!.Parent!))
                    {
                        return;
                    }

                    MarkDecoratorAliasReferenced(location);
                    return;
                }
            default:
                Debug.AssertNever(hint, $"Unhandled reference hint: {hint}");
                return;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:30614-30619 — <c>markIdentifierAliasReferenced</c>.</summary>
    private void MarkIdentifierAliasReferenced(Identifier location)
    {
        var symbol = GetResolvedSymbol(location);
        if (symbol is not null && !ReferenceEquals(symbol, _argumentsSymbol) && !ReferenceEquals(symbol, _unknownSymbol) && !Utilities.IsThisInTypeQuery(location))
        {
            MarkAliasReferenced(symbol, location);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:30621-30664 — <c>markPropertyAliasReferenced</c>.</summary>
    private void MarkPropertyAliasReferenced(Node location, ISymbol? propSymbol = null, IType? parentType = null)
    {
        Node left = NodeTests.IsPropertyAccessExpression(location) ? ((PropertyAccessExpression)location).Expression! : ((QualifiedName)location).Left!;
        if (Utilities.IsThisIdentifier(left) || !NodeTests.IsIdentifier(left))
        {
            return;
        }
        var parentSymbol = GetResolvedSymbol((Identifier)left);
        if (parentSymbol is null || ReferenceEquals(parentSymbol, _unknownSymbol))
        {
            return;
        }
        // In `Foo.Bar.Baz`, 'Foo' is not referenced if 'Bar' is a const enum or a module containing only const enums.
        // `Foo` is also not referenced in `enum FooCopy { Bar = Foo.Bar }`, because the enum member value gets inlined
        // here even if `Foo` is not a const enum.
        //
        // The exceptions are:
        //   1. if 'isolatedModules' is enabled, because the const enum value will not be inlined, and
        //   2. if 'preserveConstEnums' is enabled and the expression is itself an export, e.g. `export = Foo.Bar.Baz`.
        //
        // The property lookup is deferred as much as possible, in as many situations as possible, to avoid alias marking
        // pulling on types/symbols it doesn't strictly need to.
        if (Utilities.GetIsolatedModules(_compilerOptions) || (Utilities.ShouldPreserveConstEnums(_compilerOptions) && IsExportOrExportExpression(location)))
        {
            MarkAliasReferenced(parentSymbol, location);
            return;
        }
        // Hereafter, this relies on type checking - but every check prior to this only used symbol information
        var leftType = parentType ?? CheckExpressionCached(left);
        if (IsTypeAny(leftType) || ReferenceEquals(leftType, _silentNeverType))
        {
            MarkAliasReferenced(parentSymbol, location);
            return;
        }
        var prop = propSymbol;
        if (prop is null && parentType is null)
        {
            Node right = NodeTests.IsPropertyAccessExpression(location) ? ((PropertyAccessExpression)location).Name! : ((QualifiedName)location).Right!;
            var lexicallyScopedSymbol = NodeTests.IsPrivateIdentifier(right) ? LookupSymbolForPrivateIdentifierDeclaration(((PrivateIdentifier)right).EscapedText!, right) : null;
            var assignmentKind = Utilities.GetAssignmentTargetKind(location);
            var apparentType = GetApparentType(assignmentKind != AssignmentKind.None || IsMethodAccessForCall(location) ? GetWidenedType(leftType) : leftType);
            prop = NodeTests.IsPrivateIdentifier(right)
                ? lexicallyScopedSymbol is not null ? GetPrivateIdentifierPropertyOfType(apparentType, lexicallyScopedSymbol) : null
                : GetPropertyOfType(apparentType, ((Identifier)right).EscapedText!);
        }
        if (!(prop is not null && (IsConstEnumOrConstEnumOnlyModule(prop) || (prop.Flags & SymbolFlags.EnumMember) != 0 && location.Parent!.Kind == SyntaxKind.EnumMember)))
        {
            MarkAliasReferenced(parentSymbol, location);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:30666-30674 — <c>markExportAssignmentAliasReferenced</c>.</summary>
    private void MarkExportAssignmentAliasReferenced(ExportAssignment location)
    {
        if (NodeTests.IsIdentifier(location.Expression!))
        {
            var id = (Identifier)location.Expression!;
            var sym = GetExportSymbolOfValueSymbolIfExported(ResolveEntityName(id, SymbolFlags.All, /*ignoreErrors*/ true, /*dontResolveAlias*/ true, location));
            if (sym is not null)
            {
                MarkAliasReferenced(sym, id);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:30676-30725 — <c>markJsxAliasReferenced</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>isJsxOpeningLikeElement(node) ? node.tagName : node</c> reads <c>tagName</c> off
    /// the <c>JsxOpeningElement | JsxSelfClosingElement</c> union (types.ts:3199-3201, tested by
    /// utilitiesPublic.ts:2489-2493); the port's two classes share no type below <see cref="Node"/>, so the read is the
    /// same pair of casts the landed <c>Checker/Checker.CallResolution.cs</c> uses for that union's <c>attributes</c>
    /// slot. The parameter stays <see cref="Node"/>, which is what <c>CheckJsxOpeningLikeElementOrOpeningFragment</c>
    /// records for the union plus <c>JsxOpeningFragment</c>.
    /// </remarks>
    private void MarkJsxAliasReferenced(Node node)
    {
        if (GetJsxNamespaceContainerForImplicitImport(node) is not null)
        {
            return;
        }
        // The reactNamespace/jsxFactory's root symbol should be marked as 'used' so we don't incorrectly elide its import.
        // And if there is no reactNamespace/jsxFactory's symbol in scope when targeting React emit, we should issue an error.
        var jsxFactoryRefErr = _diagnostics is not null && _compilerOptions.Jsx == JsxEmit.React ? Diagnostics.This_JSX_tag_requires_0_to_be_in_scope_but_it_could_not_be_found : null;
        var jsxFactoryNamespace = GetJsxNamespace(node);
        Node jsxFactoryLocation = Utilities.IsJsxOpeningLikeElement(node)
            ? ((node as JsxOpeningElement)?.TagName ?? (node as JsxSelfClosingElement)?.TagName)!
            : node;
        var shouldFactoryRefErr = _compilerOptions.Jsx != JsxEmit.Preserve && _compilerOptions.Jsx != JsxEmit.ReactNative;

        // #38720/60122, allow null as jsxFragmentFactory
        ISymbol? jsxFactorySym = null;
        if (!(NodeTests.IsJsxOpeningFragment(node) && jsxFactoryNamespace == "null"))
        {
            jsxFactorySym = ResolveName(
                jsxFactoryLocation,
                jsxFactoryNamespace,
                shouldFactoryRefErr ? SymbolFlags.Value : SymbolFlags.Value & ~SymbolFlags.Enum,
                jsxFactoryRefErr,
                /*isUse*/ true,
                /*excludeGlobals*/ false);
        }

        if (jsxFactorySym is not null)
        {
            // Mark local symbol as referenced here because it might not have been marked
            // if jsx emit was not jsxFactory as there wont be error being emitted
            jsxFactorySym.IsReferenced = SymbolFlags.All;

            // If react/jsxFactory symbol is alias, mark it as refereced
            if (_canCollectSymbolAliasAccessabilityData && (jsxFactorySym.Flags & SymbolFlags.Alias) != 0 && GetTypeOnlyAliasDeclaration(jsxFactorySym) is null)
            {
                MarkAliasSymbolAsReferenced(jsxFactorySym);
            }
        }

        // if JsxFragment, additionally mark jsx pragma as referenced, since `getJsxNamespace` above would have resolved
        // to only the fragment factory if they are distinct
        if (NodeTests.IsJsxOpeningFragment(node))
        {
            var file = Utilities.GetSourceFileOfNode(node);
            var entity = file is not null ? GetJsxFactoryEntity(file) : null;
            if (entity is not null)
            {
                var localJsxNamespace = Utilities.GetFirstIdentifier(entity).EscapedText!;
                ResolveName(
                    jsxFactoryLocation,
                    localJsxNamespace,
                    shouldFactoryRefErr ? SymbolFlags.Value : SymbolFlags.Value & ~SymbolFlags.Enum,
                    jsxFactoryRefErr,
                    /*isUse*/ true,
                    /*excludeGlobals*/ false);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:30727-30734 — <c>markAsyncFunctionAliasReferenced</c>.</summary>
    private void MarkAsyncFunctionAliasReferenced(Node location)
    {
        if (_languageVersion < ScriptTarget.ES2015)
        {
            if ((Utilities.GetFunctionFlags(location) & FunctionFlags.Async) != 0)
            {
                var returnTypeNode = Utilities.GetEffectiveReturnTypeNode(location);
                MarkTypeNodeAsReferenced(returnTypeNode);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:30736-30740 — <c>markImportEqualsAliasReferenced</c>.</summary>
    private void MarkImportEqualsAliasReferenced(ImportEqualsDeclaration location)
    {
        if (Utilities.HasSyntacticModifier(location, ModifierFlags.Export))
        {
            MarkExportAsReferenced(location);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:30742-30761 — <c>markExportSpecifierAliasReferenced</c>.</summary>
    private void MarkExportSpecifierAliasReferenced(ExportSpecifier location)
    {
        var exportDeclaration = (ExportDeclaration)location.Parent!.Parent!;
        if (exportDeclaration.ModuleSpecifier is null && !Js.IsTruthy(location.IsTypeOnly) && !Js.IsTruthy(exportDeclaration.IsTypeOnly))
        {
            var exportedName = location.PropertyName ?? location.Name!;
            if (exportedName.Kind == SyntaxKind.StringLiteral)
            {
                return; // Skip for invalid syntax like this: export { "x" }
            }
            var symbol = ResolveName(exportedName, ((Identifier)exportedName).EscapedText!, SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace | SymbolFlags.Alias, /*nameNotFoundMessage*/ null, /*isUse*/ true, /*excludeGlobals*/ false);
            if (symbol is not null && (ReferenceEquals(symbol, _undefinedSymbol) || ReferenceEquals(symbol, _globalThisSymbol)
                || symbol.Declarations is not null && Utilities.IsGlobalSourceFile((Node)GetDeclarationContainer((Node)symbol.Declarations[0]))))
            {
                // Do nothing, non-local symbol
            }
            else
            {
                var target = symbol is not null ? ((symbol.Flags & SymbolFlags.Alias) != 0 ? ResolveAlias(symbol) : symbol) : null;
                if (target is null || (GetSymbolFlags(target) & SymbolFlags.Value) != 0)
                {
                    MarkExportAsReferenced(location); // marks export as used
                    MarkIdentifierAliasReferenced((Identifier)exportedName); // marks target of export as used
                }
            }
            return;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:30763-30811 — <c>markDecoratorAliasReferenced</c>.</summary>
    private void MarkDecoratorAliasReferenced(Node node)
    {
        if (_compilerOptions.EmitDecoratorMetadata == true)
        {
            Node? firstDecorator = Core.Find(GetModifiersOfNode(node), (n, _) => NodeTests.IsDecorator(n));
            if (firstDecorator is null)
            {
                return;
            }

            CheckExternalEmitHelpers(firstDecorator, ExternalEmitHelpers.Metadata);

            // we only need to perform these checks if we are emitting serialized type metadata for the target of a decorator.
            switch (node.Kind)
            {
                case SyntaxKind.ClassDeclaration:
                    var constructor = Utilities.GetFirstConstructorWithBody(node);
                    if (constructor is not null)
                    {
                        foreach (var parameter in constructor.Parameters!)
                        {
                            MarkDecoratorMedataDataTypeNodeAsReferenced(GetParameterTypeNodeForDecoratorCheck(parameter));
                        }
                    }
                    break;

                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                    var otherKind = node.Kind == SyntaxKind.GetAccessor ? SyntaxKind.SetAccessor : SyntaxKind.GetAccessor;
                    var otherAccessor = Utilities.GetDeclarationOfKind(GetSymbolOfDeclaration((IDeclaration)node), otherKind);
                    MarkDecoratorMedataDataTypeNodeAsReferenced(GetAnnotatedAccessorTypeNode((Node)node) ?? (otherAccessor is not null ? GetAnnotatedAccessorTypeNode((Node)otherAccessor) : null));
                    break;
                case SyntaxKind.MethodDeclaration:
                    foreach (var parameter in ((MethodDeclaration)node).Parameters!)
                    {
                        MarkDecoratorMedataDataTypeNodeAsReferenced(GetParameterTypeNodeForDecoratorCheck(parameter));
                    }

                    MarkDecoratorMedataDataTypeNodeAsReferenced(Utilities.GetEffectiveReturnTypeNode(node));
                    break;

                case SyntaxKind.PropertyDeclaration:
                    MarkDecoratorMedataDataTypeNodeAsReferenced(Utilities.GetEffectiveTypeAnnotationNode(node));
                    break;

                case SyntaxKind.Parameter:
                    MarkDecoratorMedataDataTypeNodeAsReferenced(GetParameterTypeNodeForDecoratorCheck((ParameterDeclaration)node));
                    var containingSignature = (IFunctionLikeDeclarationBase)node.Parent!;
                    foreach (var parameter in containingSignature.Parameters!)
                    {
                        MarkDecoratorMedataDataTypeNodeAsReferenced(GetParameterTypeNodeForDecoratorCheck(parameter));
                    }
                    MarkDecoratorMedataDataTypeNodeAsReferenced(Utilities.GetEffectiveReturnTypeNode((Node)containingSignature));
                    break;
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:30813-30832 — <c>markAliasReferenced</c>.</summary>
    private void MarkAliasReferenced(ISymbol symbol, Node location)
    {
        if (!_canCollectSymbolAliasAccessabilityData)
        {
            return;
        }
        if (IsNonLocalAlias(symbol, /*excludes*/ SymbolFlags.Value) && !Utilities.IsInTypeQuery(location))
        {
            var target = ResolveAlias(symbol);
            if ((GetSymbolFlags(symbol, /*excludeTypeOnlyMeanings*/ true) & (SymbolFlags.Value | SymbolFlags.ExportValue)) != 0)
            {
                // An alias resolving to a const enum cannot be elided if (1) 'isolatedModules' is enabled
                // (because the const enum value will not be inlined), or if (2) the alias is an export
                // of a const enum declaration that will be preserved.
                if (
                    Utilities.GetIsolatedModules(_compilerOptions) ||
                    Utilities.ShouldPreserveConstEnums(_compilerOptions) && IsExportOrExportExpression(location) ||
                    !IsConstEnumOrConstEnumOnlyModule(GetExportSymbolOfValueSymbolIfExported(target))
                )
                {
                    MarkAliasSymbolAsReferenced(symbol);
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:30837-30855 — <c>markAliasSymbolAsReferenced</c>.</summary>
    private void MarkAliasSymbolAsReferenced(ISymbol symbol)
    {
        Debug.Assert(_canCollectSymbolAliasAccessabilityData);
        var links = GetSymbolLinks(symbol);
        if (links.Referenced != true)
        {
            links.Referenced = true;
            var node = GetDeclarationOfAliasSymbol(symbol);
            if (node is null)
            {
                Debug.Fail();
                return;
            }
            // We defer checking of the reference of an `import =` until the import itself is referenced,
            // This way a chain of imports can be elided if ultimately the final input is only used in a type
            // position.
            if (Utilities.IsInternalModuleImportEqualsDeclaration((Node)node))
            {
                if ((GetSymbolFlags(ResolveSymbol(symbol)!) & SymbolFlags.Value) != 0)
                {
                    // import foo = <symbol>
                    var left = Utilities.GetFirstIdentifier(((ImportEqualsDeclaration)node).ModuleReference!);
                    MarkIdentifierAliasReferenced(left);
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:30857-30868 — <c>markExportAsReferenced</c>.</summary>
    private void MarkExportAsReferenced(Node node)
    {
        var symbol = GetSymbolOfDeclaration((IDeclaration)node)!;
        var target = ResolveAlias(symbol);
        if (target is not null)
        {
            var markAlias = ReferenceEquals(target, _unknownSymbol) ||
                ((GetSymbolFlags(symbol, /*excludeTypeOnlyMeanings*/ true) & SymbolFlags.Value) != 0 && !IsConstEnumOrConstEnumOnlyModule(target));

            if (markAlias)
            {
                MarkAliasSymbolAsReferenced(symbol);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:30870-30899 — <c>markEntityNameOrEntityExpressionAsReference</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>EntityNameOrEntityNameExpression</c> union is <see cref="Node"/> in the port (§12.5), and
    /// <c>declarations || emptyArray</c> is stated as the nullable list <c>Core.Find</c> admits rather than materializing
    /// TS's shared empty array literal.
    /// </remarks>
    private void MarkEntityNameOrEntityExpressionAsReference(Node? typeName, bool forDecoratorMetadata)
    {
        if (typeName is null) return;

        var rootName = Utilities.GetFirstIdentifier(typeName);
        var meaning = (typeName.Kind == SyntaxKind.Identifier ? SymbolFlags.Type : SymbolFlags.Namespace) | SymbolFlags.Alias;
        var rootSymbol = ResolveName(rootName, rootName.EscapedText!, meaning, /*nameNotFoundMessage*/ null, /*isUse*/ true, /*excludeGlobals*/ false);
        if (rootSymbol is not null && (rootSymbol.Flags & SymbolFlags.Alias) != 0)
        {
            if (_canCollectSymbolAliasAccessabilityData
                && SymbolIsValue(rootSymbol)
                && !IsConstEnumOrConstEnumOnlyModule(ResolveAlias(rootSymbol))
                && GetTypeOnlyAliasDeclaration(rootSymbol) is null)
            {
                MarkAliasSymbolAsReferenced(rootSymbol);
            }
            else if (forDecoratorMetadata
                && Utilities.GetIsolatedModules(_compilerOptions)
                && Utilities.GetEmitModuleKind(_compilerOptions) >= ModuleKind.ES2015
                && !SymbolIsValue(rootSymbol)
                && !Core.Some(rootSymbol.Declarations, (IDeclaration d, int _) => Utilities.IsTypeOnlyImportOrExportDeclaration((Node)d)))
            {
                var diag = Error(typeName, Diagnostics.A_type_referenced_in_a_decorated_signature_must_be_imported_with_import_type_or_a_namespace_import_when_isolatedModules_and_emitDecoratorMetadata_are_enabled);
                var aliasDeclaration = Core.Find(rootSymbol.Declarations, (IDeclaration d, int _) => IsAliasSymbolDeclaration((Node)d));
                if (aliasDeclaration is not null)
                {
                    Utilities.AddRelatedInfo(diag, Utilities.CreateDiagnosticForNode((Node)aliasDeclaration, Diagnostics._0_was_imported_here, Utilities.IdText(rootName)));
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:30905-30907 — <c>markTypeNodeAsReferenced</c>.</summary>
    private void MarkTypeNodeAsReferenced(ITypeNode? node)
        => MarkEntityNameOrEntityExpressionAsReference(node is not null ? Utilities.GetEntityNameFromTypeNode(node) : null, /*forDecoratorMetadata*/ false);

    /// <summary>ported from: src/compiler/checker.ts:30916-30921 — <c>markDecoratorMedataDataTypeNodeAsReferenced</c>.</summary>
    private void MarkDecoratorMedataDataTypeNodeAsReferenced(ITypeNode? node)
    {
        var entityName = GetEntityNameForDecoratorMetadata((Node?)node);
        if (entityName is not null && Utilities.IsEntityName(entityName))
        {
            MarkEntityNameOrEntityExpressionAsReference((Node)entityName, /*forDecoratorMetadata*/ true);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:28758-28790 — <c>getTypeOfDottedName</c>.</summary>
    private IType? GetTypeOfDottedName(Expression node, Diagnostic? diagnostic)
    {
        if ((node.Flags & NodeFlags.InWithStatement) == 0)
        {
            switch (node.Kind)
            {
                case SyntaxKind.Identifier:
                    var symbol = GetExportSymbolOfValueSymbolIfExported(GetResolvedSymbol((Identifier)node));
                    return GetExplicitTypeOfSymbol(symbol, diagnostic);
                case SyntaxKind.ThisKeyword:
                    return GetExplicitThisType(node);
                case SyntaxKind.SuperKeyword:
                    return CheckSuperExpression(node);
                case SyntaxKind.PropertyAccessExpression:
                    {
                        var type = GetTypeOfDottedName(((PropertyAccessExpression)node).Expression!, diagnostic);
                        if (type is not null)
                        {
                            var name = ((PropertyAccessExpression)node).Name!;
                            ISymbol? prop;
                            if (NodeTests.IsPrivateIdentifier(name))
                            {
                                if (type.Symbol is null)
                                {
                                    return null;
                                }
                                prop = GetPropertyOfType(type, Utilities.GetSymbolNameForPrivateIdentifier(type.Symbol, ((PrivateIdentifier)name).EscapedText!));
                            }
                            else
                            {
                                prop = GetPropertyOfType(type, ((Identifier)name).EscapedText!);
                            }
                            return prop is not null ? GetExplicitTypeOfSymbol(prop, diagnostic) : null;
                        }
                        return null;
                    }
                case SyntaxKind.ParenthesizedExpression:
                    return GetTypeOfDottedName(((ParenthesizedExpression)node).Expression!, diagnostic);
            }
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:28792-28826 — <c>getEffectsSignature</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter union <c>CallExpression | InstanceofExpression</c> is <see cref="Node"/> here and the
    /// body narrows with the reference's own <c>isBinaryExpression</c> test. The <c>effectsSignature</c> slot is a plain
    /// reference slot whose <c>null</c> is TS's <c>undefined</c>, and <c>unknownSignature</c> is the same sentinel value.
    /// </remarks>
    internal ISignature? GetEffectsSignature(Node node)
    {
        var links = GetNodeLinks(node);
        var signature = links.EffectsSignature;
        if (signature is null)
        {
            // A call expression parented by an expression statement is a potential assertion. Other call
            // expressions are potential type predicate function calls. In order to avoid triggering
            // circularities in control flow analysis, we use getTypeOfDottedName when resolving the call
            // target expression of an assertion.
            IType? funcType = null;
            if (NodeTests.IsBinaryExpression(node))
            {
                var rightType = CheckNonNullExpression(((BinaryExpression)node).Right!);
                funcType = GetSymbolHasInstanceMethodOfObjectType(rightType);
            }
            else if (node.Parent!.Kind == SyntaxKind.ExpressionStatement)
            {
                funcType = GetTypeOfDottedName(((CallExpression)node).Expression!, /*diagnostic*/ null);
            }
            else if (((CallExpression)node).Expression!.Kind != SyntaxKind.SuperKeyword)
            {
                if (Utilities.IsOptionalChain(node))
                {
                    funcType = CheckNonNullType(
                        GetOptionalExpressionType(CheckExpression(((CallExpression)node).Expression!), ((CallExpression)node).Expression!),
                        ((CallExpression)node).Expression!);
                }
                else
                {
                    funcType = CheckNonNullExpression(((CallExpression)node).Expression!);
                }
            }
            var signatures = GetSignaturesOfType(funcType is not null ? GetApparentType(funcType) : _unknownType, SignatureKind.Call);
            var candidate = signatures.Count == 1 && signatures[0].TypeParameters is null ? signatures[0] :
                Core.Some(signatures, HasTypePredicateOrNeverReturnType) ? GetResolvedSignature(node) :
                null;
            signature = links.EffectsSignature = candidate is not null && HasTypePredicateOrNeverReturnType(candidate) ? candidate : _unknownSignature;
        }
        return ReferenceEquals(signature, _unknownSignature) ? null : signature;
    }

    /// <summary>ported from: src/compiler/checker.ts:30256-30262 — <c>getControlFlowContainer</c>.</summary>
    internal Node GetControlFlowContainer(Node node)
    {
        return Utilities.FindAncestor(node.Parent, n =>
            Utilities.IsFunctionLike(n) && Utilities.GetImmediatelyInvokedFunctionExpression(n) is null ||
            n.Kind == SyntaxKind.ModuleBlock ||
            n.Kind == SyntaxKind.SourceFile ||
            n.Kind == SyntaxKind.PropertyDeclaration)!;
    }

    /// <summary>ported from: src/compiler/checker.ts:30223-30254 — <c>getTypeOfSymbolAtLocation</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>location.parent</c> is nullable here, and the two helpers the reference gets from
    /// <c>utilities.ts</c> (<c>isRightSideOfQualifiedNameOrPropertyAccess</c>, <c>isRightSideOfAccessExpression</c>,
    /// <c>isWriteAccess</c>, <c>isDeclarationName</c>, <c>isSetAccessor</c>) are <c>Utilities</c>' members in the port.
    /// </remarks>
    internal IType GetTypeOfSymbolAtLocation(ISymbol? symbolIn, Node location)
    {
        // PORT-NOTE: the generated bridge (`Checker.CreateTypeChecker.cs:162-163`) forwards the interface's nullable
        // `symbol` slot; TS's own parameter is non-nullable, so the reference's reads are kept and the slot asserted once.
        var symbol = GetExportSymbolOfValueSymbolIfExported(symbolIn!);

        // If we have an identifier or a property access at the given location, if the location is
        // an dotted name expression, and if the location is not an assignment target, obtain the type
        // of the expression (which will reflect control flow analysis). If the expression indeed
        // resolved to the given symbol, return the narrowed type.
        if (location.Kind == SyntaxKind.Identifier || location.Kind == SyntaxKind.PrivateIdentifier)
        {
            if (Utilities.IsRightSideOfQualifiedNameOrPropertyAccess(location))
            {
                location = location.Parent!;
            }
            if (Utilities.IsExpressionNode(location) && (!Utilities.IsAssignmentTarget(location) || Utilities.IsWriteAccess(location)))
            {
                var type = RemoveOptionalTypeMarker(
                    Utilities.IsWriteAccess(location) && location.Kind == SyntaxKind.PropertyAccessExpression
                        ? CheckPropertyAccessExpression((PropertyAccessExpression)location, /*checkMode*/ null, /*writeOnly*/ true)
                        : GetTypeOfExpression(location));
                if (ReferenceEquals(GetExportSymbolOfValueSymbolIfExported(GetNodeLinks(location).ResolvedSymbol!), symbol))
                {
                    return type;
                }
            }
        }
        if (Utilities.IsDeclarationName(location) && Utilities.IsSetAccessor(location.Parent!) && GetAnnotatedAccessorTypeNode(location.Parent!) is not null)
        {
            return GetWriteTypeOfAccessors(((IDeclaration)location.Parent!).Symbol!);
        }
        // The location isn't a reference to the given symbol, meaning we're being asked
        // a hypothetical question of what type the symbol would have if there was a reference
        // to it at the given location. Since we have no control flow information for the
        // hypothetical reference (control flow information is created and attached by the
        // binder), we simply return the declared type of the symbol.
        return Utilities.IsRightSideOfAccessExpression(location) && Utilities.IsWriteAccess(location.Parent!)
            ? GetWriteTypeOfSymbol(symbol)
            : GetNonMissingTypeOfSymbol(symbol);
    }

    /// <summary>ported from: src/compiler/checker.ts:28935-28976 — <c>isPostSuperFlowNode</c>.</summary>
    private bool IsPostSuperFlowNode(FlowNode flow, bool noCacheCheck)
    {
        while (true)
        {
            var flags = flow.Flags;
            if ((flags & FlowFlags.Shared) != 0)
            {
                if (!noCacheCheck)
                {
                    var id = GetFlowNodeId(flow);
                    var postSuper = GetFlowSlot(_flowNodePostSuper, id);
                    if (postSuper is not null)
                    {
                        return postSuper.Value;
                    }
                    var computed = IsPostSuperFlowNode(flow, /*noCacheCheck*/ true);
                    SetFlowSlot(_flowNodePostSuper, id, computed);
                    return computed;
                }
                noCacheCheck = false;
            }
            if ((flags & (FlowFlags.Assignment | FlowFlags.Condition | FlowFlags.ArrayMutation | FlowFlags.SwitchClause)) != 0)
            {
                flow = (FlowNode)flow.Antecedent!;
            }
            else if ((flags & FlowFlags.Call) != 0)
            {
                if (((CallExpression)flow.Node!).Expression!.Kind == SyntaxKind.SuperKeyword)
                {
                    return true;
                }
                flow = (FlowNode)flow.Antecedent!;
            }
            else if ((flags & FlowFlags.BranchLabel) != 0)
            {
                // A branching point is post-super if every branch is post-super.
                return Core.Every((List<FlowNode>)flow.Antecedent!, (f, _) => IsPostSuperFlowNode(f, /*noCacheCheck*/ false));
            }
            else if ((flags & FlowFlags.LoopLabel) != 0)
            {
                // A loop is post-super if the control flow path that leads to the top is post-super.
                flow = ((List<FlowNode>)flow.Antecedent!)[0];
            }
            else if ((flags & FlowFlags.ReduceLabel) != 0)
            {
                var data = (FlowReduceLabelData)flow.Node!;
                var target = (FlowNode)data.Target!;
                var saveAntecedents = target.Antecedent;
                target.Antecedent = data.Antecedents;
                var result = IsPostSuperFlowNode((FlowNode)flow.Antecedent!, /*noCacheCheck*/ false);
                target.Antecedent = saveAntecedents;
                return result;
            }
            else
            {
                // Unreachable nodes are considered post-super to silence errors
                return (flags & FlowFlags.Unreachable) != 0;
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:31230-31235 — <c>isSameScopedBindingElement</c>.</summary>
    private static bool IsSameScopedBindingElement(Identifier node, Node declaration)
    {
        if (NodeTests.IsBindingElement(declaration))
        {
            var bindingElement = Utilities.FindAncestor(node, NodeTests.IsBindingElement);
            return bindingElement is not null && ReferenceEquals(Utilities.GetRootDeclaration(bindingElement), Utilities.GetRootDeclaration(declaration));
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:31237-31254 — <c>shouldMarkIdentifierAliasReferenced</c>.</summary>
    private static bool ShouldMarkIdentifierAliasReferenced(Identifier node)
    {
        var parent = node.Parent;
        if (parent is not null)
        {
            // A property access expression LHS? checkPropertyAccessExpression will handle that.
            if (NodeTests.IsPropertyAccessExpression(parent) && ((PropertyAccessExpression)parent).Expression == node)
            {
                return false;
            }
            // Next two check for an identifier inside a type only export.
            if (NodeTests.IsExportSpecifier(parent) && Js.IsTruthy(((ExportSpecifier)parent).IsTypeOnly))
            {
                return false;
            }
            var greatGrandparent = parent.Parent?.Parent;
            if (greatGrandparent is not null && NodeTests.IsExportDeclaration(greatGrandparent) && Js.IsTruthy(((ExportDeclaration)greatGrandparent).IsTypeOnly))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>ported from: src/compiler/checker.ts:31256-31261 — <c>isInsideFunctionOrInstancePropertyInitializer</c>.</summary>
    private static bool IsInsideFunctionOrInstancePropertyInitializer(Node node, Node threshold)
    {
        return Utilities.FindAncestor(node, n =>
            ReferenceEquals(n, threshold) ? null : Utilities.IsFunctionLike(n) || (
                n.Parent is not null && NodeTests.IsPropertyDeclaration(n.Parent) && !Utilities.HasStaticModifier(n.Parent) && ((PropertyDeclaration)n.Parent).Initializer == n
            ) ? true : false) is not null;
    }

    /// <summary>ported from: src/compiler/checker.ts:31263-31265 — <c>getPartOfForStatementContainingNode</c>.</summary>
    private static Node? GetPartOfForStatementContainingNode(Node node, ForStatement container)
    {
        return Utilities.FindAncestor(node, n =>
            ReferenceEquals(n, container) ? null
            : ReferenceEquals(n, container.Initializer) || ReferenceEquals(n, container.Condition) || ReferenceEquals(n, container.Incrementor) || ReferenceEquals(n, container.Statement) ? true : false);
    }

    /// <summary>ported from: src/compiler/checker.ts:31267-31269 — <c>getEnclosingIterationStatement</c>.</summary>
    internal Node? GetEnclosingIterationStatement(Node node)
    {
        return Utilities.FindAncestor(node, n => !Utilities.NodeStartsNewLexicalEnvironment(n) && Utilities.IsIterationStatement(n, /*lookInLabeledStatements*/ false) ? true : (bool?)null);
    }

    /// <summary>ported from: src/compiler/checker.ts:31271-31333 — <c>checkNestedBlockScopedBinding</c>.</summary>
    private void CheckNestedBlockScopedBinding(Identifier node, ISymbol symbol)
    {
        if (
            _languageVersion >= ScriptTarget.ES2015 ||
            (symbol.Flags & (SymbolFlags.BlockScopedVariable | SymbolFlags.Class)) == 0 ||
            symbol.ValueDeclaration is null ||
            NodeTests.IsSourceFile((Node)symbol.ValueDeclaration) ||
            ((Node)symbol.ValueDeclaration).Parent!.Kind == SyntaxKind.CatchClause
        )
        {
            return;
        }

        // 1. walk from the use site up to the declaration and check
        // if there is anything function like between declaration and use-site (is binding/class is captured in function).
        // 2. walk from the declaration up to the boundary of lexical environment and check
        // if there is an iteration statement in between declaration and boundary (is binding/class declared inside iteration statement)

        var container = Utilities.GetEnclosingBlockScopeContainer((Node)symbol.ValueDeclaration);
        var isCaptured = IsInsideFunctionOrInstancePropertyInitializer(node, container);

        var enclosingIterationStatement = GetEnclosingIterationStatement(container);
        if (enclosingIterationStatement is not null)
        {
            if (isCaptured)
            {
                // mark iteration statement as containing block-scoped binding captured in some function
                var capturesBlockScopeBindingInLoopBody = true;
                if (NodeTests.IsForStatement(container))
                {
                    var varDeclList = Utilities.GetAncestor((Node)symbol.ValueDeclaration, SyntaxKind.VariableDeclarationList);
                    if (varDeclList is not null && varDeclList.Parent == container)
                    {
                        var part = GetPartOfForStatementContainingNode(node.Parent!, (ForStatement)container);
                        if (part is not null)
                        {
                            var links = GetNodeLinks(part);
                            links.Flags |= NodeCheckFlags.ContainsCapturedBlockScopeBinding;

                            var capturedBindings = links.CapturedBlockScopeBindings ?? (links.CapturedBlockScopeBindings = []);
                            Core.PushIfUnique(capturedBindings, symbol);

                            if (ReferenceEquals(part, ((ForStatement)container).Initializer))
                            {
                                capturesBlockScopeBindingInLoopBody = false; // Initializer is outside of loop body
                            }
                        }
                    }
                }
                if (capturesBlockScopeBindingInLoopBody)
                {
                    GetNodeLinks(enclosingIterationStatement).Flags |= NodeCheckFlags.LoopWithCapturedBlockScopedBinding;
                }
            }

            // mark variables that are declared in loop initializer and reassigned inside the body of ForStatement.
            // if body of ForStatement will be converted to function then we'll need a extra machinery to propagate reassigned values back.
            if (NodeTests.IsForStatement(container))
            {
                var varDeclList = Utilities.GetAncestor((Node)symbol.ValueDeclaration, SyntaxKind.VariableDeclarationList);
                if (varDeclList is not null && varDeclList.Parent == container && IsAssignedInBodyOfForStatement(node, (ForStatement)container))
                {
                    GetNodeLinks((Node)symbol.ValueDeclaration).Flags |= NodeCheckFlags.NeedsLoopOutParameter;
                }
            }

            // set 'declared inside loop' bit on the block-scoped binding
            GetNodeLinks((Node)symbol.ValueDeclaration).Flags |= NodeCheckFlags.BlockScopedBindingInLoop;
        }

        if (isCaptured)
        {
            GetNodeLinks((Node)symbol.ValueDeclaration).Flags |= NodeCheckFlags.CapturedBlockScopedBinding;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:31335-31338 — <c>isBindingCapturedByNode</c>.</summary>
    internal bool IsBindingCapturedByNode(Node? node, Node? decl)
    {
        var links = node is not null ? GetNodeLinks(node) : null;
        return links is not null && decl is not null && Core.Contains(links.CapturedBlockScopeBindings, GetSymbolOfDeclaration((IDeclaration)decl));
    }

    /// <summary>ported from: src/compiler/checker.ts:31340-31364 — <c>isAssignedInBodyOfForStatement</c>.</summary>
    private static bool IsAssignedInBodyOfForStatement(Identifier node, ForStatement container)
    {
        // skip parenthesized nodes
        Node current = node;
        while (current.Parent!.Kind == SyntaxKind.ParenthesizedExpression)
        {
            current = current.Parent;
        }

        // check if node is used as LHS in some assignment expression
        var isAssigned = false;
        if (Utilities.IsAssignmentTarget(current))
        {
            isAssigned = true;
        }
        else if (current.Parent!.Kind == SyntaxKind.PrefixUnaryExpression || current.Parent.Kind == SyntaxKind.PostfixUnaryExpression)
        {
            isAssigned = current.Parent.Kind == SyntaxKind.PrefixUnaryExpression
                ? ((PrefixUnaryExpression)current.Parent).Operator is SyntaxKind.PlusPlusToken or SyntaxKind.MinusMinusToken
                : ((PostfixUnaryExpression)current.Parent).Operator is SyntaxKind.PlusPlusToken or SyntaxKind.MinusMinusToken;
        }

        if (!isAssigned)
        {
            return false;
        }

        // at this point we know that node is the target of assignment
        // now check that modification happens inside the statement part of the ForStatement
        return Utilities.FindAncestor(current, n => ReferenceEquals(n, container) ? null : ReferenceEquals(n, container.Statement) ? true : false) is not null;
    }

    /// <summary>ported from: src/compiler/checker.ts:31366-31375 — <c>captureLexicalThis</c>.</summary>
    private void CaptureLexicalThis(Node node, Node container)
    {
        GetNodeLinks(node).Flags |= NodeCheckFlags.LexicalThis;
        if (container.Kind == SyntaxKind.PropertyDeclaration || container.Kind == SyntaxKind.Constructor)
        {
            var classNode = container.Parent!;
            GetNodeLinks(classNode).Flags |= NodeCheckFlags.CaptureThis;
        }
        else
        {
            GetNodeLinks(container).Flags |= NodeCheckFlags.CaptureThis;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:31377-31381 — <c>findFirstSuperCall</c>.</summary>
    private static Node? FindFirstSuperCall(Node node)
    {
        return Utilities.IsSuperCall(node) ? node :
            Utilities.IsFunctionLike(node) ? null :
            ForEachChildTable.ForEachChild<object?>(node, FindFirstSuperCall) as Node;
    }

    /// <summary>ported from: src/compiler/checker.ts:31388-31394 — <c>classDeclarationExtendsNull</c>.</summary>
    private bool ClassDeclarationExtendsNull(Node classDecl)
    {
        var classSymbol = GetSymbolOfDeclaration((IDeclaration)classDecl);
        var classInstanceType = (IInterfaceType)GetDeclaredTypeOfSymbol(classSymbol);
        var baseConstructorType = GetBaseConstructorTypeOfClass(classInstanceType);

        return ReferenceEquals(baseConstructorType, _nullWideningType);
    }

    /// <summary>ported from: src/compiler/checker.ts:31396-31407 — <c>checkThisBeforeSuper</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>container.parent as ClassDeclaration</c> is the <c>ClassLikeDeclaration</c> union
    /// (<c>ClassDeclaration | ClassExpression</c>): <c>getClassExtendsHeritageElement</c> and
    /// <c>classDeclarationExtendsNull</c> both take that union, and a class expression's constructor reaches here with a
    /// <c>ClassExpression</c> parent. The port declares both callees over <see cref="Node"/>, so the read is the parent
    /// itself; TS's cast never narrowed anything either.
    /// </remarks>
    private void CheckThisBeforeSuper(Node node, Node container, DiagnosticMessage diagnosticMessage)
    {
        var containingClassDecl = container.Parent!;
        var baseTypeNode = Utilities.GetClassExtendsHeritageElement(containingClassDecl);

        // If a containing class does not have extends clause or the class extends null
        // skip checking whether super statement is called before "this" accessing.
        if (baseTypeNode is not null && !ClassDeclarationExtendsNull(containingClassDecl))
        {
            if (Utilities.CanHaveFlowNode(node) && ((IFlowContainer)node).FlowNode is { } flowNode && !IsPostSuperFlowNode(flowNode, /*noCacheCheck*/ false))
            {
                Error(node, diagnosticMessage);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:31409-31416 — <c>checkThisInStaticClassFieldInitializerInDecoratedClass</c>.</summary>
    private void CheckThisInStaticClassFieldInitializerInDecoratedClass(Node thisExpression, Node container)
    {
        if (
            NodeTests.IsPropertyDeclaration(container) && Utilities.HasStaticModifier(container) && _legacyDecorators &&
            ((PropertyDeclaration)container).Initializer is { } initializer && Utilities.TextRangeContainsPositionInclusive(initializer, thisExpression.Pos)
            && Utilities.HasDecorators(container.Parent!)
        )
        {
            Error(thisExpression, Diagnostics.Cannot_use_this_in_a_static_property_initializer_of_a_decorated_class);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:31418-31486 — <c>checkThisExpression</c>.</summary>
    internal IType CheckThisExpression(Node node)
    {
        var isNodeInTypeQuery = Utilities.IsInTypeQuery(node);
        // Stop at the first arrow function so that we can
        // tell whether 'this' needs to be captured.
        var container = Utilities.GetThisContainer(node, /*includeArrowFunctions*/ true, /*includeClassComputedPropertyName*/ true);
        var capturedByArrowFunction = false;
        var thisInComputedPropertyName = false;

        if (container.Kind == SyntaxKind.Constructor)
        {
            CheckThisBeforeSuper(node, container, Diagnostics.super_must_be_called_before_accessing_this_in_the_constructor_of_a_derived_class);
        }

        while (true)
        {
            // Now skip arrow functions to get the "real" owner of 'this'.
            if (container.Kind == SyntaxKind.ArrowFunction)
            {
                container = Utilities.GetThisContainer(container, /*includeArrowFunctions*/ false, !thisInComputedPropertyName);
                capturedByArrowFunction = true;
            }

            if (container.Kind == SyntaxKind.ComputedPropertyName)
            {
                container = Utilities.GetThisContainer(container, !capturedByArrowFunction, /*includeClassComputedPropertyName*/ false);
                thisInComputedPropertyName = true;
                continue;
            }

            break;
        }

        CheckThisInStaticClassFieldInitializerInDecoratedClass(node, container);
        if (thisInComputedPropertyName)
        {
            Error(node, Diagnostics.this_cannot_be_referenced_in_a_computed_property_name);
        }
        else
        {
            switch (container.Kind)
            {
                case SyntaxKind.ModuleDeclaration:
                    Error(node, Diagnostics.this_cannot_be_referenced_in_a_module_or_namespace_body);
                    // do not return here so in case if lexical this is captured - it will be reflected in flags on NodeLinks
                    break;
                case SyntaxKind.EnumDeclaration:
                    Error(node, Diagnostics.this_cannot_be_referenced_in_current_location);
                    // do not return here so in case if lexical this is captured - it will be reflected in flags on NodeLinks
                    break;
            }
        }

        // When targeting es6, mark that we'll need to capture `this` in its lexically bound scope.
        if (!isNodeInTypeQuery && capturedByArrowFunction && _languageVersion < ScriptTarget.ES2015)
        {
            CaptureLexicalThis(node, container);
        }

        var type = TryGetThisTypeAt(node, /*includeGlobalThis*/ true, container);
        if (_noImplicitThis)
        {
            var globalThisType = GetTypeOfSymbol(_globalThisSymbol!);
            if (ReferenceEquals(type, globalThisType) && capturedByArrowFunction)
            {
                Error(node, Diagnostics.The_containing_arrow_function_captures_the_global_value_of_this);
            }
            else if (type is null)
            {
                // With noImplicitThis, functions may not reference 'this' if it has type 'any'
                var diag = Error(node, Diagnostics.this_implicitly_has_type_any_because_it_does_not_have_a_type_annotation);
                if (!NodeTests.IsSourceFile(container))
                {
                    var outsideThis = TryGetThisTypeAt(container);
                    if (outsideThis is not null && !ReferenceEquals(outsideThis, globalThisType))
                    {
                        Utilities.AddRelatedInfo(diag, Utilities.CreateDiagnosticForNode(container, Diagnostics.An_outer_value_of_this_is_shadowed_by_this_container));
                    }
                }
            }
        }
        return type ?? _anyType;
    }

    /// <summary>ported from: src/compiler/checker.ts:31488-31536 — <c>tryGetThisTypeAt</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>container</c> parameter is the union <c>Node</c> with a computed default; the port's
    /// generated interface spells the slot <c>object?</c> (the bridge at <c>Checker.CreateTypeChecker.cs:805</c> forwards
    /// it verbatim), so the parameter is <c>object?</c> and a non-<c>Node</c> value means "compute the default", which is
    /// the same call shape as the reference's omitted argument.
    /// </remarks>
    internal IType? TryGetThisTypeAt(Node node, bool? includeGlobalThis = null, object? containerIn = null)
    {
        var includeGlobal = includeGlobalThis ?? true;
        var container = containerIn as Node ?? Utilities.GetThisContainer(node, /*includeArrowFunctions*/ false, /*includeClassComputedPropertyName*/ false);
        var isInJS = Utilities.IsInJSFile(node);
        if (
            Utilities.IsFunctionLike(container) &&
            (!IsInParameterInitializerBeforeContainingFunction(node) || Utilities.GetThisParameter(container) is not null)
        )
        {
            var thisType = GetThisTypeOfDeclaration(container) ?? (isInJS ? GetTypeForThisExpressionFromJSDoc(container) : null);
            // Note: a parameter initializer should refer to class-this unless function-this is explicitly annotated.
            // If this is a function in a JS file, it might be a class method.
            if (thisType is null)
            {
                var className = GetClassNameFromPrototypeMethod(container);
                if (isInJS && className is not null)
                {
                    var classSymbol = CheckExpression(className).Symbol;
                    if (classSymbol is not null && classSymbol.Members is not null && (classSymbol.Flags & SymbolFlags.Function) != 0)
                    {
                        thisType = ((InterfaceType)GetDeclaredTypeOfSymbol(classSymbol)).ThisType;
                    }
                }
                else if (IsJSConstructor(container))
                {
                    thisType = ((InterfaceType)GetDeclaredTypeOfSymbol(GetMergedSymbol(((IDeclaration)container).Symbol!)!)).ThisType;
                }
                thisType ??= GetContextualThisParameterType(container);
            }

            if (thisType is not null)
            {
                return GetFlowTypeOfReference(node, thisType);
            }
        }

        if (Utilities.IsClassLike(container.Parent!))
        {
            var symbol = GetSymbolOfDeclaration((IDeclaration)container.Parent!);
            var type = Utilities.IsStatic(container) ? GetTypeOfSymbol(symbol) : ((InterfaceType)GetDeclaredTypeOfSymbol(symbol)).ThisType!;
            return GetFlowTypeOfReference(node, type);
        }

        if (NodeTests.IsSourceFile(container))
        {
            // look up in the source file's locals or exports
            var sourceFile = (SourceFile)container;
            if (sourceFile.CommonJsModuleIndicator is not null)
            {
                var fileSymbol = GetSymbolOfDeclaration((IDeclaration)container);
                return fileSymbol is not null ? GetTypeOfSymbol(fileSymbol) : null;
            }
            else if (sourceFile.ExternalModuleIndicator is not null)
            {
                // TODO: Maybe issue a better error than 'object is possibly undefined'
                return _undefinedType;
            }
            else if (includeGlobal)
            {
                return GetTypeOfSymbol(_globalThisSymbol!);
            }
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:31538-31550 — <c>getExplicitThisType</c>.</summary>
    private IType? GetExplicitThisType(Expression node)
    {
        var container = Utilities.GetThisContainer(node, /*includeArrowFunctions*/ false, /*includeClassComputedPropertyName*/ false);
        if (Utilities.IsFunctionLike(container))
        {
            var signature = GetSignatureFromDeclaration(container);
            if (signature.ThisParameter is not null)
            {
                return GetExplicitTypeOfSymbol(signature.ThisParameter);
            }
        }
        if (Utilities.IsClassLike(container.Parent!))
        {
            var symbol = GetSymbolOfDeclaration((IDeclaration)container.Parent!);
            return Utilities.IsStatic(container) ? GetTypeOfSymbol(symbol) : ((InterfaceType)GetDeclaredTypeOfSymbol(symbol)).ThisType!;
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:31552-31613 — <c>getClassNameFromPrototypeMethod</c>.</summary>
    private Expression? GetClassNameFromPrototypeMethod(Node container)
    {
        // Check if it's the RHS of a x.prototype.y = function [name]() { .... }
        if (
            container.Kind == SyntaxKind.FunctionExpression &&
            NodeTests.IsBinaryExpression(container.Parent!) &&
            Utilities.GetAssignmentDeclarationKind(container.Parent!) == AssignmentDeclarationKind.PrototypeProperty
        )
        {
            // Get the 'x' of 'x.prototype.y = container'
            return (Expression)((PropertyAccessExpression)((BinaryExpression)container.Parent!).Left!).Expression!;
        }
        // x.prototype = { method() { } }
        else if (
            container.Kind == SyntaxKind.MethodDeclaration &&
            container.Parent!.Kind == SyntaxKind.ObjectLiteralExpression &&
            NodeTests.IsBinaryExpression(container.Parent.Parent!) &&
            Utilities.GetAssignmentDeclarationKind(container.Parent.Parent!) == AssignmentDeclarationKind.Prototype
        )
        {
            return ((PropertyAccessExpression)((BinaryExpression)container.Parent.Parent!).Left!).Expression!;
        }
        // x.prototype = { method: function() { } }
        else if (
            container.Kind == SyntaxKind.FunctionExpression &&
            container.Parent!.Kind == SyntaxKind.PropertyAssignment &&
            container.Parent.Parent!.Kind == SyntaxKind.ObjectLiteralExpression &&
            NodeTests.IsBinaryExpression(container.Parent.Parent.Parent!) &&
            Utilities.GetAssignmentDeclarationKind(container.Parent.Parent.Parent!) == AssignmentDeclarationKind.Prototype
        )
        {
            return ((PropertyAccessExpression)((BinaryExpression)container.Parent.Parent.Parent!).Left!).Expression!;
        }
        // Object.defineProperty(x, "method", { value: function() { } });
        // Object.defineProperty(x, "method", { set: (x: () => void) => void });
        // Object.defineProperty(x, "method", { get: () => function() { }) });
        else if (
            container.Kind == SyntaxKind.FunctionExpression &&
            NodeTests.IsPropertyAssignment(container.Parent!) &&
            NodeTests.IsIdentifier(((PropertyAssignment)container.Parent!).Name!)
            && (Utilities.IdText(((PropertyAssignment)container.Parent!).Name!) == "value" || Utilities.IdText(((PropertyAssignment)container.Parent!).Name!) == "get" || Utilities.IdText(((PropertyAssignment)container.Parent!).Name!) == "set")
            && container.Parent.Parent!.Kind == SyntaxKind.ObjectLiteralExpression
            && container.Parent.Parent.Parent!.Kind == SyntaxKind.CallExpression
            && ((CallExpression)container.Parent.Parent.Parent!).Arguments![2] == container.Parent.Parent
            && Utilities.GetAssignmentDeclarationKind(container.Parent.Parent.Parent!) == AssignmentDeclarationKind.ObjectDefinePrototypeProperty
        )
        {
            return ((PropertyAccessExpression)((CallExpression)container.Parent.Parent.Parent!).Arguments![0]).Expression!;
        }
        // Object.defineProperty(x, "method", { value() { } });
        // Object.defineProperty(x, "method", { set(x: () => void) {} });
        // Object.defineProperty(x, "method", { get() { return () => {} } });
        else if (
            NodeTests.IsMethodDeclaration(container) &&
            NodeTests.IsIdentifier(((MethodDeclaration)container).Name!)
            && (Utilities.IdText(((MethodDeclaration)container).Name!) == "value" || Utilities.IdText(((MethodDeclaration)container).Name!) == "get" || Utilities.IdText(((MethodDeclaration)container).Name!) == "set")
            && container.Parent!.Kind == SyntaxKind.ObjectLiteralExpression
            && container.Parent.Parent!.Kind == SyntaxKind.CallExpression
            && ((CallExpression)container.Parent.Parent!).Arguments![2] == container.Parent
            && Utilities.GetAssignmentDeclarationKind(container.Parent.Parent!) == AssignmentDeclarationKind.ObjectDefinePrototypeProperty
        )
        {
            return ((PropertyAccessExpression)((CallExpression)container.Parent.Parent!).Arguments![0]).Expression!;
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:31615-31624 — <c>getTypeForThisExpressionFromJSDoc</c>.</summary>
    private IType? GetTypeForThisExpressionFromJSDoc(Node node)
    {
        var thisTag = Utilities.GetJSDocThisTag(node);
        if (thisTag is not null && thisTag.TypeExpression is not null)
        {
            return GetTypeFromTypeNode(thisTag.TypeExpression);
        }
        var signature = GetSignatureOfTypeTag(node);
        if (signature is not null)
        {
            return GetThisTypeOfSignature(signature);
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:31626-31628 — <c>isInConstructorArgumentInitializer</c>.</summary>
    private static bool IsInConstructorArgumentInitializer(Node node, Node constructorDecl)
    {
        return Utilities.FindAncestor(node, n => Utilities.IsFunctionLike(n) ? null : n.Kind == SyntaxKind.Parameter && n.Parent == constructorDecl ? true : false) is not null;
    }

    /// <summary>ported from: src/compiler/checker.ts:31630-31848 — <c>checkSuperExpression</c>.</summary>
    private IType CheckSuperExpression(Node node)
    {
        var isCallExpression = node.Parent!.Kind == SyntaxKind.CallExpression && ((CallExpression)node.Parent).Expression == node;

        var immediateContainer = Utilities.GetSuperContainer(node, /*stopOnFunctions*/ true);
        var container = immediateContainer;
        var needToCaptureLexicalThis = false;
        var inAsyncFunction = false;

        // adjust the container reference in case if super is used inside arrow functions with arbitrarily deep nesting
        if (!isCallExpression)
        {
            while (container is not null && container.Kind == SyntaxKind.ArrowFunction)
            {
                if (Utilities.HasSyntacticModifier(container, ModifierFlags.Async)) inAsyncFunction = true;
                container = Utilities.GetSuperContainer(container, /*stopOnFunctions*/ true);
                needToCaptureLexicalThis = _languageVersion < ScriptTarget.ES2015;
            }
            if (container is not null && Utilities.HasSyntacticModifier(container, ModifierFlags.Async)) inAsyncFunction = true;
        }

        var nodeCheckFlag = NodeCheckFlags.None;

        if (container is null || !IsLegalUsageOfSuperExpression(container))
        {
            // issue more specific error if super is used in computed property name
            // class A { foo() { return "1" }}
            // class B {
            //     [super.foo()]() {}
            // }
            var current = Utilities.FindAncestor(node, n => ReferenceEquals(n, container) ? null : n.Kind == SyntaxKind.ComputedPropertyName ? true : false);
            if (current is not null && current.Kind == SyntaxKind.ComputedPropertyName)
            {
                Error(node, Diagnostics.super_cannot_be_referenced_in_a_computed_property_name);
            }
            else if (isCallExpression)
            {
                Error(node, Diagnostics.Super_calls_are_not_permitted_outside_constructors_or_in_nested_functions_inside_constructors);
            }
            else if (container is null || container.Parent is null || !(Utilities.IsClassLike(container.Parent) || container.Parent.Kind == SyntaxKind.ObjectLiteralExpression))
            {
                Error(node, Diagnostics.super_can_only_be_referenced_in_members_of_derived_classes_or_object_literal_expressions);
            }
            else
            {
                Error(node, Diagnostics.super_property_access_is_permitted_only_in_a_constructor_member_function_or_member_accessor_of_a_derived_class);
            }
            return _errorType;
        }

        if (!isCallExpression && immediateContainer!.Kind == SyntaxKind.Constructor)
        {
            CheckThisBeforeSuper(node, container, Diagnostics.super_must_be_called_before_accessing_a_property_of_super_in_the_constructor_of_a_derived_class);
        }

        if (Utilities.IsStatic(container) || isCallExpression)
        {
            nodeCheckFlag = NodeCheckFlags.SuperStatic;
            if (
                !isCallExpression &&
                _languageVersion >= ScriptTarget.ES2015 && _languageVersion <= ScriptTarget.ES2021 &&
                (NodeTests.IsPropertyDeclaration(container) || NodeTests.IsClassStaticBlockDeclaration(container))
            )
            {
                // for `super.x` or `super[x]` in a static initializer, mark all enclosing
                // block scope containers so that we can report potential collisions with
                // `Reflect`.
                Utilities.ForEachEnclosingBlockScopeContainer(node.Parent!, current =>
                {
                    if (!NodeTests.IsSourceFile(current) || Utilities.IsExternalOrCommonJsModule((SourceFile)current))
                    {
                        GetNodeLinks(current).Flags |= NodeCheckFlags.ContainsSuperPropertyInStaticInitializer;
                    }
                });
            }
        }
        else
        {
            nodeCheckFlag = NodeCheckFlags.SuperInstance;
        }

        GetNodeLinks(node).Flags |= nodeCheckFlag;

        // Due to how we emit async functions, we need to specialize the emit for an async method that contains a
        // `super` reference: the body of an async function is emitted inside a generator function, which cannot
        // reference `super`, so the reference is captured by a helper emitted outside the generator.
        if (container.Kind == SyntaxKind.MethodDeclaration && inAsyncFunction)
        {
            if (Utilities.IsSuperProperty(node.Parent!) && Utilities.IsAssignmentTarget(node.Parent!))
            {
                GetNodeLinks(container).Flags |= NodeCheckFlags.MethodWithSuperPropertyAssignmentInAsync;
            }
            else
            {
                GetNodeLinks(container).Flags |= NodeCheckFlags.MethodWithSuperPropertyAccessInAsync;
            }
        }

        if (needToCaptureLexicalThis)
        {
            // call expressions are allowed only in constructors so they should always capture correct 'this'
            // super property access expressions can also appear in arrow functions -
            // in this case they should also use correct lexical this
            CaptureLexicalThis(node.Parent!, container);
        }

        if (container.Parent!.Kind == SyntaxKind.ObjectLiteralExpression)
        {
            if (_languageVersion < ScriptTarget.ES2015)
            {
                Error(node, Diagnostics.super_is_only_allowed_in_members_of_object_literal_expressions_when_option_target_is_ES2015_or_higher);
                return _errorType;
            }
            else
            {
                // for object literal assume that type of 'super' is 'any'
                return _anyType;
            }
        }

        // at this point the only legal case for parent is ClassLikeDeclaration
        // PORT-NOTE: TS's `ClassLikeDeclaration` union has no port counterpart (§12.5); its two faces are
        // ClassDeclaration and ClassExpression, and only the parent slot of the class is read here.
        var classLikeDeclaration = container.Parent!;
        if (Utilities.GetClassExtendsHeritageElement(classLikeDeclaration) is null)
        {
            Error(node, Diagnostics.super_can_only_be_referenced_in_a_derived_class);
            return _errorType;
        }

        if (ClassDeclarationExtendsNull(classLikeDeclaration))
        {
            return isCallExpression ? _errorType : _nullWideningType;
        }

        var classType = (InterfaceType)GetDeclaredTypeOfSymbol(GetSymbolOfDeclaration((IDeclaration)classLikeDeclaration));
        var baseTypes = GetBaseTypes(classType);
        var baseClassType = baseTypes.Count > 0 ? baseTypes[0] : null;
        if (baseClassType is null)
        {
            return _errorType;
        }

        if (container.Kind == SyntaxKind.Constructor && IsInConstructorArgumentInitializer(node, container))
        {
            // issue custom error message for super property access in constructor arguments (to be aligned with old compiler)
            Error(node, Diagnostics.super_cannot_be_referenced_in_constructor_arguments);
            return _errorType;
        }

        return nodeCheckFlag == NodeCheckFlags.SuperStatic
            ? GetBaseConstructorTypeOfClass(classType)
            : GetTypeWithThisArgument(baseClassType, classType.ThisType);

        /// <summary>ported from: src/compiler/checker.ts:31812-31847 — <c>isLegalUsageOfSuperExpression</c>.</summary>
        /// <remarks>
        /// PORT-NOTE: TS declares this as a function nested in <c>checkSuperExpression</c> so it closes over
        /// <c>isCallExpression</c>; the port keeps it a local function for the same closure (C# local functions are in
        /// scope for the whole block, matching the reference's hoisting).
        /// </remarks>
        bool IsLegalUsageOfSuperExpression(Node container)
        {
            if (isCallExpression)
            {
                // TS 1.0 SPEC (April 2014): 4.8.1
                // Super calls are only permitted in constructors of derived classes
                return container.Kind == SyntaxKind.Constructor;
            }
            else
            {
                // TS 1.0 SPEC (April 2014)
                // 'super' property access is allowed
                // - In a constructor, instance member function, instance member accessor, or instance member variable initializer where this references a derived class instance
                // - In a static member function or static member accessor

                // topmost container must be something that is directly nested in the class declaration\object literal expression
                if (Utilities.IsClassLike(container.Parent!) || container.Parent!.Kind == SyntaxKind.ObjectLiteralExpression)
                {
                    if (Utilities.IsStatic(container))
                    {
                        return container.Kind == SyntaxKind.MethodDeclaration ||
                            container.Kind == SyntaxKind.MethodSignature ||
                            container.Kind == SyntaxKind.GetAccessor ||
                            container.Kind == SyntaxKind.SetAccessor ||
                            container.Kind == SyntaxKind.PropertyDeclaration ||
                            container.Kind == SyntaxKind.ClassStaticBlockDeclaration;
                    }
                    else
                    {
                        return container.Kind == SyntaxKind.MethodDeclaration ||
                            container.Kind == SyntaxKind.MethodSignature ||
                            container.Kind == SyntaxKind.GetAccessor ||
                            container.Kind == SyntaxKind.SetAccessor ||
                            container.Kind == SyntaxKind.PropertyDeclaration ||
                            container.Kind == SyntaxKind.PropertySignature ||
                            container.Kind == SyntaxKind.Constructor;
                    }
                }
            }

            return false;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:11723-11734 — <c>getDestructuringPropertyName</c>'s <c>elements</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>BindingPattern</c> union (types.ts) has no port alias; the two classes' <c>elements</c> slots are
    /// reached through this switch, which is the same read TS performed structurally. TS compares the slot's
    /// <c>length</c>, and the port's <c>NodeArray&lt;BindingElement&gt;</c> cannot be viewed as
    /// <c>NodeArray&lt;Node&gt;</c> (the generic is invariant), so the helper answers the count.
    /// </remarks>
    private static int GetBindingPatternElementCount(Node pattern)
        => pattern.Kind == SyntaxKind.ObjectBindingPattern
            ? ((ObjectBindingPattern)pattern).Elements!.Count
            : ((ArrayBindingPattern)pattern).Elements!.Count;

    /// <summary>ported from: src/compiler/checker.ts:30923-31010 — <c>getNarrowedTypeOfSymbol</c>.</summary>
    private IType GetNarrowedTypeOfSymbol(ISymbol symbol, Identifier location)
    {
        var type = GetTypeOfSymbol(symbol);
        var declaration = symbol.ValueDeclaration;
        if (declaration is not null)
        {
            // If we have a non-rest binding element with no initializer declared as a const variable or a const-like
            // parameter (a parameter for which there are no assignments in the function body), and if the parent type
            // for the destructuring is a union type, one or more of the binding elements may represent discriminant
            // properties, and we want the effects of conditional checks on such discriminants to affect the types of
            // other binding elements from the same destructuring. Consider:
            //
            //   type Action =
            //       | { kind: 'A', payload: number }
            //       | { kind: 'B', payload: string };
            //
            //   function f({ kind, payload }: Action) {
            //       if (kind === 'A') {
            //           payload.toFixed();
            //       }
            //       if (kind === 'B') {
            //           payload.toUpperCase();
            //       }
            //   }
            //
            // Above, we want the conditional checks on 'kind' to affect the type of 'payload'. To facilitate this, we use
            // the binding pattern AST instance for '{ kind, payload }' as a pseudo-reference and narrow this reference
            // as if it occurred in the specified location. We then recompute the narrowed binding element type by
            // destructuring from the narrowed parent type.
            var declarationNode = (Node)declaration;
            if (NodeTests.IsBindingElement(declarationNode) && ((BindingElement)declarationNode).Initializer is null
                && ((BindingElement)declarationNode).DotDotDotToken is null && GetBindingPatternElementCount(declarationNode.Parent!) >= 2)
            {
                var parent = declarationNode.Parent!.Parent!;
                var rootDeclaration = Utilities.GetRootDeclaration(parent);
                if (rootDeclaration.Kind == SyntaxKind.VariableDeclaration && (GetCombinedNodeFlagsCached(rootDeclaration) & NodeFlags.Constant) != 0
                    || rootDeclaration.Kind == SyntaxKind.Parameter)
                {
                    var links = GetNodeLinks(parent);
                    if ((links.Flags & NodeCheckFlags.InCheckIdentifier) == 0)
                    {
                        links.Flags |= NodeCheckFlags.InCheckIdentifier;
                        var parentType = GetTypeForBindingElementParent(parent, CheckMode.Normal);
                        var parentTypeConstraint = parentType is not null ? MapType(parentType, GetBaseConstraintOrType) : null;
                        links.Flags &= ~NodeCheckFlags.InCheckIdentifier;
                        if (parentTypeConstraint is not null && (parentTypeConstraint.Flags & TypeFlags.Union) != 0
                            && !(rootDeclaration.Kind == SyntaxKind.Parameter && IsSomeSymbolAssigned(rootDeclaration)))
                        {
                            var pattern = declarationNode.Parent!;
                            var narrowedType = GetFlowTypeOfReference(pattern, parentTypeConstraint, parentTypeConstraint, /*flowContainer*/ null, location.FlowNode);
                            if ((narrowedType.Flags & TypeFlags.Never) != 0)
                            {
                                return _neverType;
                            }
                            // Destructurings are validated against the parent type elsewhere. Here we disable tuple bounds
                            // checks because the narrowed type may have lower arity than the full parent type. For example,
                            // for the declaration [x, y]: [1, 2] | [3], we may have narrowed the parent type to just [3].
                            return GetBindingElementTypeFromParentType((BindingElement)declarationNode, narrowedType, /*noTupleBoundsCheck*/ true);
                        }
                    }
                }
            }
            // If we have a const-like parameter with no type annotation or initializer, and if the parameter is contextually
            // typed by a signature with a single rest parameter of a union of tuple types, one or more of the parameters may
            // represent discriminant tuple elements, and we want the effects of conditional checks on such discriminants to
            // affect the types of other parameters in the same parameter list. Consider:
            //
            //   type Action = [kind: 'A', payload: number] | [kind: 'B', payload: string];
            //
            //   const f: (...args: Action) => void = (kind, payload) => {
            //       if (kind === 'A') {
            //           payload.toFixed();
            //       }
            //       if (kind === 'B') {
            //           payload.toUpperCase();
            //       }
            //   }
            //
            // Above, we want the conditional checks on 'kind' to affect the type of 'payload'. To facilitate this, we use
            // the arrow function AST node for '(kind, payload) => ...' as a pseudo-reference and narrow this reference as
            // if it occurred in the specified location. We then recompute the narrowed parameter type by indexing into the
            // narrowed tuple type.
            if (NodeTests.IsParameter(declarationNode) && ((ParameterDeclaration)declarationNode).Type is null
                && ((ParameterDeclaration)declarationNode).Initializer is null && ((ParameterDeclaration)declarationNode).DotDotDotToken is null)
            {
                var func = declarationNode.Parent!;
                if (((IFunctionLikeDeclarationBase)func).Parameters!.Count >= 2 && IsContextSensitiveFunctionOrObjectLiteralMethod(func))
                {
                    var contextualSignature = GetContextualSignature(func);
                    if (contextualSignature is not null && contextualSignature.Parameters!.Count == 1 && SignatureHasRestParameter(contextualSignature))
                    {
                        var restType = GetReducedApparentType(InstantiateType(GetTypeOfSymbol(contextualSignature.Parameters![0]), GetInferenceContext(func)?.NonFixingMapper as TypeMapper)!);
                        if ((restType.Flags & TypeFlags.Union) != 0 && EveryType(restType, IsTupleType) && !Core.Some(((IFunctionLikeDeclarationBase)func).Parameters!, IsSomeSymbolAssigned))
                        {
                            var narrowedType = GetFlowTypeOfReference(func, restType, restType, /*flowContainer*/ null, location.FlowNode);
                            var index = ((IFunctionLikeDeclarationBase)func).Parameters!.IndexOf((ParameterDeclaration)declarationNode) - (Utilities.GetThisParameter(func) is not null ? 1 : 0);
                            return GetIndexedAccessType(narrowedType, GetNumberLiteralType(index));
                        }
                    }
                }
            }
        }
        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:31016-31079 — <c>checkIdentifierCalculateNodeCheckFlags</c>.</summary>
    private void CheckIdentifierCalculateNodeCheckFlags(Identifier node, ISymbol symbol)
    {
        if (Utilities.IsThisInTypeQuery(node)) return;

        // As noted in ECMAScript 6 language spec, arrow functions never have an arguments objects.
        // Although in down-level emit of arrow function, we emit it using function expression which means that
        // arguments objects will be bound to the inner object; emitting arrow function natively in ES6, arguments objects
        // will be bound to non-arrow function that contain this arrow function. This results in inconsistent behavior.
        // To avoid that we will give an error to users if they use arguments objects in arrow function so that they
        // can explicitly bound arguments objects
        if (ReferenceEquals(symbol, _argumentsSymbol))
        {
            if (IsInPropertyInitializerOrClassStaticBlock(node, /*ignoreArrowFunctions*/ true))
            {
                Error(node, Diagnostics.arguments_cannot_be_referenced_in_property_initializers_or_class_static_initialization_blocks);
                return;
            }

            var container = Utilities.GetContainingFunction(node);
            if (container is not null)
            {
                if (_languageVersion < ScriptTarget.ES2015)
                {
                    if (container.Kind == SyntaxKind.ArrowFunction)
                    {
                        Error(node, Diagnostics.The_arguments_object_cannot_be_referenced_in_an_arrow_function_in_ES5_Consider_using_a_standard_function_expression);
                    }
                    else if (Utilities.HasSyntacticModifier(container, ModifierFlags.Async))
                    {
                        Error(node, Diagnostics.The_arguments_object_cannot_be_referenced_in_an_async_function_or_method_in_ES5_Consider_using_a_standard_function_or_method);
                    }
                }

                GetNodeLinks(container).Flags |= NodeCheckFlags.CaptureArguments;
                while (container is not null && NodeTests.IsArrowFunction(container))
                {
                    container = Utilities.GetContainingFunction(container);
                    if (container is not null)
                    {
                        GetNodeLinks(container).Flags |= NodeCheckFlags.CaptureArguments;
                    }
                }
            }
            return;
        }

        var localOrExportSymbol = GetExportSymbolOfValueSymbolIfExported(symbol);
        var targetSymbol = ResolveAliasWithDeprecationCheck(localOrExportSymbol, node);
        if (IsDeprecatedSymbol(targetSymbol) && IsUncalledFunctionReference(node, targetSymbol) && targetSymbol.Declarations is not null)
        {
            AddDeprecatedSuggestion(node, Core.Map(localOrExportSymbol.Declarations, (d, _) => (Node)d)!, node.EscapedText!);
        }

        var declaration = localOrExportSymbol.ValueDeclaration;
        if (declaration is not null && (localOrExportSymbol.Flags & SymbolFlags.Class) != 0)
        {
            // When we downlevel classes we may emit some code outside of the class body. Due to the fact the
            // class name is double-bound, we must ensure we mark references to the class name so that we can
            // emit an alias to the class later.
            var declarationNode = (Node)declaration;
            if (Utilities.IsClassLike(declarationNode) && ((INamedDeclaration)declarationNode).Name != node)
            {
                var container = Utilities.GetThisContainer(node, /*includeArrowFunctions*/ false, /*includeClassComputedPropertyName*/ false);
                while (container.Kind != SyntaxKind.SourceFile && container.Parent != declarationNode)
                {
                    container = Utilities.GetThisContainer(container, /*includeArrowFunctions*/ false, /*includeClassComputedPropertyName*/ false);
                }

                if (container.Kind != SyntaxKind.SourceFile)
                {
                    GetNodeLinks(declarationNode).Flags |= NodeCheckFlags.ContainsConstructorReference;
                    GetNodeLinks(container).Flags |= NodeCheckFlags.ContainsConstructorReference;
                    GetNodeLinks(node).Flags |= NodeCheckFlags.ConstructorReference;
                }
            }
        }

        CheckNestedBlockScopedBinding(node, symbol);
    }

    /// <summary>ported from: src/compiler/checker.ts:31081-31228 — <c>checkIdentifier</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>VariableLikeDeclaration</c> union (passed to <c>removeOptionalityFromDeclaredType</c>) is
    /// <see cref="Node"/> in the port, and <c>autoType</c>/<c>autoArrayType</c> are reference comparisons against the same
    /// two members the reference compares against.
    /// </remarks>
    internal IType CheckIdentifier(Identifier node, CheckMode? checkMode)
    {
        if (Utilities.IsThisInTypeQuery(node))
        {
            return CheckThisExpression(node);
        }

        var symbol = GetResolvedSymbol(node);
        if (ReferenceEquals(symbol, _unknownSymbol))
        {
            return _errorType;
        }

        CheckIdentifierCalculateNodeCheckFlags(node, symbol);

        if (ReferenceEquals(symbol, _argumentsSymbol))
        {
            if (IsInPropertyInitializerOrClassStaticBlock(node, /*ignoreArrowFunctions*/ true))
            {
                return _errorType;
            }
            return GetTypeOfSymbol(symbol);
        }

        if (ShouldMarkIdentifierAliasReferenced(node))
        {
            MarkLinkedReferences(node, ReferenceHint.Identifier);
        }

        var localOrExportSymbol = GetExportSymbolOfValueSymbolIfExported(symbol);
        var declaration = localOrExportSymbol.ValueDeclaration;
        var immediateDeclaration = declaration;

        // If the identifier is declared in a binding pattern for which we're currently computing the implied type and the
        // reference occurs with the same binding pattern, return the non-inferrable any type. This for example occurs in
        // 'const [a, b = a + 1] = [2]' when we're computing the contextual type for the array literal '[2]'.
        if (declaration is not null && declaration.Kind == SyntaxKind.BindingElement && Core.Contains(_contextualBindingPatterns, ((Node)declaration).Parent!)
            && Utilities.FindAncestor(node, parent => ReferenceEquals(parent, ((Node)declaration).Parent!) ? true : false) is not null)
        {
            return _nonInferrableAnyType;
        }

        var type = GetNarrowedTypeOfSymbol(localOrExportSymbol, node);
        var assignmentKind = Utilities.GetAssignmentTargetKind(node);

        if (assignmentKind != AssignmentKind.None)
        {
            if (
                (localOrExportSymbol.Flags & SymbolFlags.Variable) == 0 &&
                !(Utilities.IsInJSFile(node) && (localOrExportSymbol.Flags & SymbolFlags.ValueModule) != 0)
            )
            {
                var assignmentError = (localOrExportSymbol.Flags & SymbolFlags.Enum) != 0 ? Diagnostics.Cannot_assign_to_0_because_it_is_an_enum
                    : (localOrExportSymbol.Flags & SymbolFlags.Class) != 0 ? Diagnostics.Cannot_assign_to_0_because_it_is_a_class
                    : (localOrExportSymbol.Flags & SymbolFlags.Module) != 0 ? Diagnostics.Cannot_assign_to_0_because_it_is_a_namespace
                    : (localOrExportSymbol.Flags & SymbolFlags.Function) != 0 ? Diagnostics.Cannot_assign_to_0_because_it_is_a_function
                    : (localOrExportSymbol.Flags & SymbolFlags.Alias) != 0 ? Diagnostics.Cannot_assign_to_0_because_it_is_an_import
                    : Diagnostics.Cannot_assign_to_0_because_it_is_not_a_variable;

                Error(node, assignmentError, Diagnostic.DeferArg(() => SymbolToString(symbol)));
                return _errorType;
            }
            if (IsReadonlySymbol(localOrExportSymbol))
            {
                if ((localOrExportSymbol.Flags & SymbolFlags.Variable) != 0)
                {
                    Error(node, Diagnostics.Cannot_assign_to_0_because_it_is_a_constant, Diagnostic.DeferArg(() => SymbolToString(symbol)));
                }
                else
                {
                    Error(node, Diagnostics.Cannot_assign_to_0_because_it_is_a_read_only_property, Diagnostic.DeferArg(() => SymbolToString(symbol)));
                }
                return _errorType;
            }
        }

        var isAlias = (localOrExportSymbol.Flags & SymbolFlags.Alias) != 0;

        // We only narrow variables and parameters occurring in a non-assignment position. For all other
        // entities we simply return the declared type.
        if ((localOrExportSymbol.Flags & SymbolFlags.Variable) != 0)
        {
            if (assignmentKind == AssignmentKind.Definite)
            {
                return Utilities.IsInCompoundLikeAssignment(node) ? GetBaseTypeOfLiteralType(type) : type;
            }
        }
        else if (isAlias)
        {
            declaration = GetDeclarationOfAliasSymbol(symbol);
        }
        else
        {
            return type;
        }

        if (declaration is null)
        {
            return type;
        }

        type = GetNarrowableTypeForReference(type, node, checkMode);

        // The declaration container is the innermost function that encloses the declaration of the variable
        // or parameter. The flow container is the innermost function starting with which we analyze the control
        // flow graph to determine the control flow based type.
        var isParameter = Utilities.GetRootDeclaration((Node)declaration).Kind == SyntaxKind.Parameter;
        var declarationContainer = GetControlFlowContainer((Node)declaration);
        var flowContainer = GetControlFlowContainer(node);
        var isOuterVariable = !ReferenceEquals(flowContainer, declarationContainer);
        var isSpreadDestructuringAssignmentTarget = node.Parent is not null && node.Parent.Parent is not null
            && NodeTests.IsSpreadAssignment(node.Parent) && IsDestructuringAssignmentTarget(node.Parent.Parent);
        var isModuleExports = (symbol.Flags & SymbolFlags.ModuleExports) != 0;
        var typeIsAutomatic = ReferenceEquals(type, _autoType) || ReferenceEquals(type, _autoArrayType);
        var isAutomaticTypeInNonNull = typeIsAutomatic && node.Parent!.Kind == SyntaxKind.NonNullExpression;
        // When the control flow originates in a function expression, arrow function, method, or accessor, and
        // we are referencing a closed-over const variable or parameter or mutable local variable past its last
        // assignment, we extend the origin of the control flow analysis to include the immediately enclosing
        // control flow container.
        while (
            !ReferenceEquals(flowContainer, declarationContainer) && (
                flowContainer.Kind == SyntaxKind.FunctionExpression ||
                flowContainer.Kind == SyntaxKind.ArrowFunction ||
                Utilities.IsObjectLiteralOrClassExpressionMethodOrAccessor(flowContainer)
            ) && (
                IsConstantVariable(localOrExportSymbol) && !ReferenceEquals(type, _autoArrayType) ||
                IsParameterOrMutableLocalVariable(localOrExportSymbol) && IsPastLastAssignment(localOrExportSymbol, node)
            )
        )
        {
            flowContainer = GetControlFlowContainer(flowContainer);
        }
        // We only look for uninitialized variables in strict null checking mode, and only when we can analyze
        // the entire control flow graph from the variable's declaration (i.e. when the flow container and
        // declaration container are the same).
        var isNeverInitialized = immediateDeclaration is not null && NodeTests.IsVariableDeclaration((Node)immediateDeclaration)
            && !Utilities.IsForInOrOfStatement(((Node)immediateDeclaration).Parent!.Parent!)
            && ((VariableDeclaration)immediateDeclaration).Initializer is null && ((VariableDeclaration)immediateDeclaration).ExclamationToken is null
            && IsMutableLocalVariableDeclaration((VariableDeclaration)immediateDeclaration) && !IsSymbolAssignedDefinitely(symbol);
        var assumeInitialized = isParameter || isAlias ||
            (isOuterVariable && !isNeverInitialized) ||
            isSpreadDestructuringAssignmentTarget || isModuleExports || IsSameScopedBindingElement(node, (Node)declaration) ||
            !ReferenceEquals(type, _autoType) && !ReferenceEquals(type, _autoArrayType) && (!_strictNullChecks || (type.Flags & (TypeFlags.AnyOrUnknown | TypeFlags.Void)) != 0 ||
                    Utilities.IsInTypeQuery(node) || IsInAmbientOrTypeNode(node) || node.Parent!.Kind == SyntaxKind.ExportSpecifier) ||
            node.Parent!.Kind == SyntaxKind.NonNullExpression ||
            ((Node)declaration).Kind == SyntaxKind.VariableDeclaration && ((VariableDeclaration)declaration).ExclamationToken is not null ||
            (((Node)declaration).Flags & NodeFlags.Ambient) != 0;
        var initialType = isAutomaticTypeInNonNull ? _undefinedType :
            assumeInitialized ? (isParameter ? RemoveOptionalityFromDeclaredType(type, declaration) : type) :
            typeIsAutomatic ? _undefinedType : GetOptionalType(type);
        var flowType = isAutomaticTypeInNonNull ? GetNonNullableType(GetFlowTypeOfReference(node, type, initialType, flowContainer)) :
            GetFlowTypeOfReference(node, type, initialType, flowContainer);
        // A variable is considered uninitialized when it is possible to analyze the entire control flow graph
        // from declaration to use, and when the variable's declared type doesn't include undefined but the
        // control flow based type does include undefined.
        if (!IsEvolvingArrayOperationTarget(node) && (ReferenceEquals(type, _autoType) || ReferenceEquals(type, _autoArrayType)))
        {
            if (ReferenceEquals(flowType, _autoType) || ReferenceEquals(flowType, _autoArrayType))
            {
                if (_noImplicitAny)
                {
                    Error(Utilities.GetNameOfDeclaration((Node)declaration) as Node, Diagnostics.Variable_0_implicitly_has_type_1_in_some_locations_where_its_type_cannot_be_determined, Diagnostic.DeferArg(() => SymbolToString(symbol)), Diagnostic.DeferArg(() => TypeToString(flowType)));
                    Error(node, Diagnostics.Variable_0_implicitly_has_an_1_type, Diagnostic.DeferArg(() => SymbolToString(symbol)), Diagnostic.DeferArg(() => TypeToString(flowType)));
                }
                return ConvertAutoToAny(flowType);
            }
        }
        else if (!assumeInitialized && !ContainsUndefinedType(type) && ContainsUndefinedType(flowType))
        {
            Error(node, Diagnostics.Variable_0_is_used_before_being_assigned, Diagnostic.DeferArg(() => SymbolToString(symbol)));
            // Return the declared type to reduce follow-on errors
            return type;
        }
        return assignmentKind != AssignmentKind.None ? GetBaseTypeOfLiteralType(flowType) : flowType;
    }

    /// <summary>ported from: src/compiler/checker.ts:34740-34743 — <c>checkPropertyAccessExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>checkPropertyAccessChain(node as PropertyAccessChain, checkMode)</c> is a no-op
    /// re-view — <c>PropertyAccessChain extends PropertyAccessExpression</c> (types.ts:2981-2984), so the same runtime
    /// object is handed on. The port models the two as a class and its abstract subclass, and a plain
    /// <see cref="PropertyAccessExpression"/> carrying <c>NodeFlags.OptionalChain</c> is exactly the value that reaches
    /// here, so <c>CheckPropertyAccessChain</c> takes the shape whose slots it reads (<c>expression</c>, <c>name</c>);
    /// the flag test is the reference's and is unchanged.
    /// </remarks>
    internal IType CheckPropertyAccessExpression(PropertyAccessExpression node, CheckMode? checkMode, bool writeOnly = false)
    {
        return (node.Flags & NodeFlags.OptionalChain) != 0
            ? CheckPropertyAccessChain(node, checkMode)
            : CheckPropertyAccessExpressionOrQualifiedName(node, node.Expression!, CheckNonNullExpression(node.Expression!), node.Name!, checkMode, writeOnly);
    }

    /// <summary>ported from: src/compiler/checker.ts:34745-34749 — <c>checkPropertyAccessChain</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's parameter is <c>PropertyAccessChain</c>, the subclass of the very class
    /// <c>checkPropertyAccessExpression</c> receives; every slot read below (<c>expression</c>, <c>name</c>) is declared on
    /// <see cref="PropertyAccessExpression"/>, so the parameter states the shape actually read rather than the nominal
    /// subclass TS's cast only re-viewed.
    /// </remarks>
    private IType CheckPropertyAccessChain(PropertyAccessExpression node, CheckMode? checkMode)
    {
        var leftType = CheckExpression(node.Expression!);
        var nonOptionalType = GetOptionalExpressionType(leftType, node.Expression!);
        return PropagateOptionalTypeMarker(CheckPropertyAccessExpressionOrQualifiedName(node, node.Expression!, CheckNonNullType(nonOptionalType, node.Expression!), node.Name!, checkMode), node, !ReferenceEquals(nonOptionalType, leftType));
    }

    /// <summary>ported from: src/compiler/checker.ts:34751-34754 — <c>checkQualifiedName</c>.</summary>
    internal IType CheckQualifiedName(QualifiedName node, CheckMode? checkMode)
    {
        var leftType = Utilities.IsPartOfTypeQuery(node) && Utilities.IsThisIdentifier(node.Left!)
            ? CheckNonNullType(CheckThisExpression(node.Left!), node.Left!)
            : CheckNonNullExpression(node.Left!);
        return CheckPropertyAccessExpressionOrQualifiedName(node, node.Left!, leftType, node.Right!, checkMode);
    }

    /// <summary>ported from: src/compiler/checker.ts:34764-34773 — <c>lookupSymbolForPrivateIdentifierDeclaration</c>.</summary>
    internal ISymbol? LookupSymbolForPrivateIdentifierDeclaration(string propName, Node location)
    {
        for (var containingClass = Utilities.GetContainingClassExcludingClassDecorators(location); containingClass is not null; containingClass = Utilities.GetContainingClass(containingClass))
        {
            var symbol = ((IDeclaration)containingClass).Symbol!;
            var name = Utilities.GetSymbolNameForPrivateIdentifier(symbol, propName);
            var prop = symbol.Members is not null && symbol.Members.TryGetValue(name, out var member) ? member
                : symbol.Exports is not null && symbol.Exports.TryGetValue(name, out var export) ? export
                : null;
            if (prop is not null)
            {
                return prop;
            }
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:34775-34792 — <c>checkGrammarPrivateIdentifierExpression</c>.</summary>
    private bool CheckGrammarPrivateIdentifierExpression(PrivateIdentifier privId)
    {
        if (Utilities.GetContainingClass(privId) is null)
        {
            return GrammarErrorOnNode(privId, Diagnostics.Private_identifiers_are_not_allowed_outside_class_bodies);
        }

        if (!NodeTests.IsForInStatement(privId.Parent!))
        {
            if (!Utilities.IsExpressionNode(privId))
            {
                return GrammarErrorOnNode(privId, Diagnostics.Private_identifiers_are_only_allowed_in_class_bodies_and_may_only_be_used_as_part_of_a_class_member_declaration_property_access_or_on_the_left_hand_side_of_an_in_expression);
            }

            var isInOperation = NodeTests.IsBinaryExpression(privId.Parent!) && ((BinaryExpression)privId.Parent!).OperatorToken!.Kind == SyntaxKind.InKeyword;
            if (GetSymbolForPrivateIdentifierExpression(privId) is null && !isInOperation)
            {
                return GrammarErrorOnNode(privId, Diagnostics.Cannot_find_name_0, Utilities.IdText(privId));
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:34794-34801 — <c>checkPrivateIdentifierExpression</c>.</summary>
    private IType CheckPrivateIdentifierExpression(PrivateIdentifier privId)
    {
        CheckGrammarPrivateIdentifierExpression(privId);
        var symbol = GetSymbolForPrivateIdentifierExpression(privId);
        if (symbol is not null)
        {
            MarkPropertyAsReferenced(symbol, /*nodeForCheckWriteOnly*/ null, /*isSelfTypeAccess*/ false);
        }
        return _anyType;
    }

    /// <summary>ported from: src/compiler/checker.ts:34803-34813 — <c>getSymbolForPrivateIdentifierExpression</c>.</summary>
    private ISymbol? GetSymbolForPrivateIdentifierExpression(PrivateIdentifier privId)
    {
        if (!Utilities.IsExpressionNode(privId))
        {
            return null;
        }

        var links = GetNodeLinks(privId);
        if (links.ResolvedSymbol is null)
        {
            links.ResolvedSymbol = LookupSymbolForPrivateIdentifierDeclaration(privId.EscapedText!, privId);
        }
        return links.ResolvedSymbol;
    }

    /// <summary>ported from: src/compiler/checker.ts:34815-34817 — <c>getPrivateIdentifierPropertyOfType</c>.</summary>
    internal ISymbol? GetPrivateIdentifierPropertyOfType(IType leftType, ISymbol lexicallyScopedIdentifier)
        => GetPropertyOfType(leftType, lexicallyScopedIdentifier.EscapedName!);

    /// <summary>ported from: src/compiler/checker.ts:34880-34883 — <c>isThisPropertyAccessInConstructor</c>.</summary>
    private bool IsThisPropertyAccessInConstructor(Node node, ISymbol prop)
    {
        return (IsConstructorDeclaredProperty(prop) || Utilities.IsThisProperty(node) && IsAutoTypedProperty(prop))
            && ReferenceEquals(Utilities.GetThisContainer(node, /*includeArrowFunctions*/ true, /*includeClassComputedPropertyName*/ false), GetDeclaringConstructor(prop));
    }

    /// <summary>ported from: src/compiler/checker.ts:35013-35028 — <c>isUncheckedJSSuggestion</c>.</summary>
    private bool IsUncheckedJSSuggestion(Node? node, ISymbol? suggestion, bool excludeClasses)
    {
        var file = Utilities.GetSourceFileOfNode(node);
        if (file is not null)
        {
            if (_compilerOptions.CheckJs is null && file.CheckJsDirective is null && (file.ScriptKind == ScriptKind.JS || file.ScriptKind == ScriptKind.JSX))
            {
                Node? declarationFile = null;
                foreach (var declaration in suggestion!.Declarations!)
                {
                    declarationFile = Utilities.GetSourceFileOfNode((Node)declaration);
                    if (declarationFile is not null) break;
                }
                var suggestionHasNoExtendsOrDecorators = suggestion?.ValueDeclaration is null
                    || !Utilities.IsClassLike((Node)suggestion.ValueDeclaration)
                    || ((IClassLikeDeclarationBase)suggestion.ValueDeclaration).HeritageClauses?.Count > 0
                    || Utilities.ClassOrConstructorParameterIsDecorated(/*useLegacyDecorators*/ false, (Node)suggestion.ValueDeclaration);
                return !(!ReferenceEquals(file, declarationFile) && declarationFile is not null && Utilities.IsGlobalSourceFile(declarationFile))
                    && !(excludeClasses && suggestion is not null && (suggestion.Flags & SymbolFlags.Class) != 0 && suggestionHasNoExtendsOrDecorators)
                    && !(node is not null && excludeClasses && NodeTests.IsPropertyAccessExpression(node) && ((PropertyAccessExpression)node).Expression!.Kind == SyntaxKind.ThisKeyword && suggestionHasNoExtendsOrDecorators);
            }
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:35215-35218 — <c>typeHasStaticProperty</c>.</summary>
    private bool TypeHasStaticProperty(string propName, IType containingType)
    {
        var prop = containingType.Symbol is not null ? GetPropertyOfType(GetTypeOfSymbol(containingType.Symbol), propName) : null;
        return prop is not null && prop.ValueDeclaration is not null && Utilities.IsStatic((Node)prop.ValueDeclaration);
    }

    /// <summary>ported from: src/compiler/checker.ts:35244-35246 — <c>getSuggestedSymbolForNonexistentClassMember</c>.</summary>
    internal ISymbol? GetSuggestedSymbolForNonexistentClassMember(string? name, IType? baseType)
        => name is null || baseType is null ? null : GetSpellingSuggestionForName(name, GetPropertiesOfType(baseType), SymbolFlags.ClassMember);

    /// <summary>ported from: src/compiler/checker.ts:35248-35258 — <c>getSuggestedSymbolForNonexistentProperty</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>Identifier | PrivateIdentifier | string</c> is the interface's <c>object?</c> here, and the
    /// <c>typeof name !== "string"</c> test is the port's pattern match.
    /// </remarks>
    internal ISymbol? GetSuggestedSymbolForNonexistentProperty(object? name, IType? containingType)
    {
        if (containingType is null) return null;
        var props = GetPropertiesOfType(containingType);
        var nameText = name as string;
        if (name is Node nameNode)
        {
            var parent = nameNode.Parent;
            if (parent is not null && NodeTests.IsPropertyAccessExpression(parent))
            {
                props = [.. Core.Filter(props!, prop => IsValidPropertyAccessForCompletions((PropertyAccessExpression)parent, containingType, prop))!];
            }
            nameText = Utilities.IdText(nameNode);
        }
        return GetSpellingSuggestionForName(nameText!, props!, SymbolFlags.Value);
    }

    /// <summary>ported from: src/compiler/checker.ts:35260-35267 — <c>getSuggestedSymbolForNonexistentJSXAttribute</c>.</summary>
    internal ISymbol? GetSuggestedSymbolForNonexistentJSXAttribute(object? name, IType? containingType)
    {
        if (containingType is null) return null;
        var strName = name as string ?? Utilities.IdText((Node)name!);
        var properties = GetPropertiesOfType(containingType);
        var jsxSpecific = strName == "for" ? Core.Find(properties, (x, _) => Utilities.SymbolName(x) == "htmlFor")
            : strName == "class" ? Core.Find(properties, (x, _) => Utilities.SymbolName(x) == "className")
            : null;
        return jsxSpecific ?? GetSpellingSuggestionForName(strName, properties!, SymbolFlags.Value);
    }

    /// <summary>ported from: src/compiler/checker.ts:35269-35272 — <c>getSuggestionForNonexistentProperty</c>.</summary>
    internal string? GetSuggestionForNonexistentProperty(object? name, IType? containingType)
    {
        var suggestion = GetSuggestedSymbolForNonexistentProperty(name, containingType);
        return suggestion is not null ? Utilities.SymbolName(suggestion) : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:35274-35292 — <c>getSuggestionForSymbolNameLookup</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: this is the <c>lookup</c> option of the checker's second <c>createNameResolver</c> call
    /// (checker.ts:1594), which <see cref="ResolveNameForSymbolSuggestion"/> selects
    /// (<c>Checker.SymbolSupport.cs</c>). TS's <c>symbols === globals</c> identity test is
    /// <c>ReferenceEquals(symbols, _globals)</c>; <c>arrayFrom(symbols.values())</c> is <c>symbols.Values.ToList()</c>;
    /// the primitives list is <c>mapDefined</c> over the six names with <c>capitalize(s)</c> inlined as
    /// <c>char.ToUpperInvariant(s[0]) + s.Substring(1)</c> (TS's <c>toUpperCase</c> has no locale); and
    /// <c>concatenate(primitives, candidates)</c> is the collection expression, which builds the same fresh list.
    /// </remarks>
    private ISymbol? GetSuggestionForSymbolNameLookup(SymbolTable symbols, string name, SymbolFlags meaning)
    {
        var symbol = GetSymbol(symbols, name, meaning);
        // Sometimes the symbol is found when location is a return type of a function: `typeof x` and `x` is declared in the body of the function
        // So the table *contains* `x` but `x` isn't actually in scope.
        // However, resolveNameHelper will continue and call this callback again, so we'll eventually get a correct suggestion.
        if (symbol is not null) return symbol;
        var candidates = symbols.Values.ToList();
        if (ReferenceEquals(symbols, _globals))
        {
            var primitives = Core.MapDefined<string, ISymbol>(
                ["string", "number", "boolean", "object", "bigint", "symbol"],
                (string s, int _) => symbols.ContainsKey(char.ToUpperInvariant(s[0]) + s.Substring(1))
                    ? CreateSymbol(SymbolFlags.TypeAlias, s)
                    : null);
            candidates = [.. primitives, .. candidates];
        }
        SortSymbolsIfTSGoCompat(candidates);
        return GetSpellingSuggestionForName(Utilities.UnescapeLeadingUnderscores(name), candidates, meaning);
    }

    /// <summary>ported from: src/compiler/checker.ts:35293-35297 — <c>getSuggestedSymbolForNonexistentSymbol</c>.</summary>
    internal ISymbol? GetSuggestedSymbolForNonexistentSymbol(Node? location, string? outerName, SymbolFlags meaning)
    {
        Debug.Assert(outerName is not null, "outername should always be defined");
        return ResolveNameForSymbolSuggestion(location, outerName!, meaning, /*nameNotFoundMessage*/ null, /*isUse*/ false, /*excludeGlobals*/ false);
    }

    /// <summary>ported from: src/compiler/checker.ts:35299-35301 — <c>getSuggestedSymbolForNonexistentModule</c>.</summary>
    internal ISymbol? GetSuggestedSymbolForNonexistentModule(Identifier? name, ISymbol? targetModule)
    {
        if (name is null || targetModule is null) return null;
        return targetModule.Exports is not null
            ? GetSpellingSuggestionForName(Utilities.IdText(name), SortSymbolsIfTSGoCompat(GetExportsOfModuleAsArray(targetModule)!)!, SymbolFlags.ModuleMember)
            : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:35305-35312 — <c>getSuggestionForNonexistentIndexSignature</c>'s <c>hasProp</c>.</summary>
    private bool HasPropForNonexistentIndexSignature(IType objectType, IType keyedType, string name)
    {
        var prop = GetPropertyOfObjectType(objectType, name);
        if (prop is not null)
        {
            var s = GetSingleCallSignature(GetTypeOfSymbol(prop));
            return s is not null && GetMinArgumentCount(s) >= 1 && IsTypeAssignableTo(keyedType, GetTypeAtPosition(s, 0));
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:35303-35328 — <c>getSuggestionForNonexistentIndexSignature</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's nested <c>hasProp</c> closes over <c>objectType</c>/<c>keyedType</c>; the port states it as the
    /// private helper above with those two slots explicit, which is the same call shape.
    /// </remarks>
    private string? GetSuggestionForNonexistentIndexSignature(IType objectType, ElementAccessExpression expr, IType keyedType)
    {
        var suggestedMethod = Utilities.IsAssignmentTarget(expr) ? "set" : "get";
        if (!HasPropForNonexistentIndexSignature(objectType, keyedType, suggestedMethod))
        {
            return null;
        }

        var suggestion = Utilities.TryGetPropertyAccessOrIdentifierToString(expr.Expression!);
        if (suggestion is null)
        {
            suggestion = suggestedMethod;
        }
        else
        {
            suggestion += "." + suggestedMethod;
        }

        return suggestion;
    }

    /// <summary>ported from: src/compiler/checker.ts:35330-35333 — <c>getSuggestedTypeForNonexistentStringLiteralType</c>.</summary>
    internal IStringLiteralType? GetSuggestedTypeForNonexistentStringLiteralType(IStringLiteralType source, IUnionType target)
    {
        var candidates = Core.Filter(target.Types!, type => (type.Flags & TypeFlags.StringLiteral) != 0);
        return Core.GetSpellingSuggestion((string)source.Value!, candidates!, type => (string)((ILiteralType)type).Value!) as IStringLiteralType;
    }

    /// <summary>ported from: src/compiler/checker.ts:35350-35372 — <c>getSpellingSuggestionForName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's nested <c>getCandidateName</c> is a C# local function here (same closure over <c>meaning</c>), and
    /// <c>startsWith</c> is the port's <c>Js.StartsWith</c> — both keep the reference's <c>'"'</c> test.
    /// </remarks>
    private ISymbol? GetSpellingSuggestionForName(string name, IList<ISymbol> symbols, SymbolFlags meaning)
    {
        return Core.GetSpellingSuggestion(name, symbols, GetCandidateName);

        string? GetCandidateName(ISymbol candidate)
        {
            var candidateName = Utilities.SymbolName(candidate);
            if (candidateName.StartsWith('"'))
            {
                return null;
            }

            if ((candidate.Flags & meaning) != 0)
            {
                return candidateName;
            }

            if ((candidate.Flags & SymbolFlags.Alias) != 0)
            {
                var alias = TryResolveAlias(candidate);
                if (alias is not null && (alias.Flags & meaning) != 0)
                {
                    return candidateName;
                }
            }

            return null;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:35374-35396 — <c>markPropertyAsReferenced</c>.</summary>
    private void MarkPropertyAsReferenced(ISymbol prop, Node? nodeForCheckWriteOnly, bool isSelfTypeAccess)
    {
        var valueDeclaration = (prop.Flags & SymbolFlags.ClassMember) != 0 ? prop.ValueDeclaration : null;
        if (valueDeclaration is null)
        {
            return;
        }
        var hasPrivateModifier = Utilities.HasEffectiveModifier((Node)valueDeclaration, ModifierFlags.Private);
        var hasPrivateIdentifier = prop.ValueDeclaration is not null && Utilities.IsNamedDeclaration((Node)prop.ValueDeclaration) && NodeTests.IsPrivateIdentifier(((INamedDeclaration)prop.ValueDeclaration).Name!);
        if (!hasPrivateModifier && !hasPrivateIdentifier)
        {
            return;
        }
        if (nodeForCheckWriteOnly is not null && Utilities.IsWriteOnlyAccess(nodeForCheckWriteOnly) && (prop.Flags & SymbolFlags.SetAccessor) == 0)
        {
            return;
        }
        if (isSelfTypeAccess)
        {
            // Find any FunctionLikeDeclaration because those create a new 'this' binding. But this should only matter for methods (or getters/setters).
            var containingMethod = Utilities.FindAncestor(nodeForCheckWriteOnly, Utilities.IsFunctionLike);
            if (containingMethod is not null && ReferenceEquals(((IDeclaration)containingMethod).Symbol, prop))
            {
                return;
            }
        }

        var target = (GetCheckFlags(prop) & CheckFlags.Instantiated) != 0 ? GetSymbolLinks(prop).Target! : prop;
        target.IsReferenced = SymbolFlags.All;
    }

    /// <summary>ported from: src/compiler/checker.ts:35398-35401 — <c>isSelfTypeAccess</c>.</summary>
    private bool IsSelfTypeAccess(Node name, ISymbol? parent)
    {
        return name.Kind == SyntaxKind.ThisKeyword
            || parent is not null && Utilities.IsEntityNameExpression(name) && ReferenceEquals(parent, GetResolvedSymbol(Utilities.GetFirstIdentifier(name)));
    }

    /// <summary>ported from: src/compiler/checker.ts:35403-35412 — <c>isValidPropertyAccess</c>.</summary>
    internal bool IsValidPropertyAccess(Node node, string propertyName)
    {
        switch (node.Kind)
        {
            case SyntaxKind.PropertyAccessExpression:
                return IsValidPropertyAccessWithType(node, ((PropertyAccessExpression)node).Expression!.Kind == SyntaxKind.SuperKeyword, propertyName, GetWidenedType(CheckExpression(((PropertyAccessExpression)node).Expression!)));
            case SyntaxKind.QualifiedName:
                return IsValidPropertyAccessWithType(node, /*isSuper*/ false, propertyName, GetWidenedType(CheckExpression(((QualifiedName)node).Left!)));
            case SyntaxKind.ImportType:
                return IsValidPropertyAccessWithType(node, /*isSuper*/ false, propertyName, GetTypeFromTypeNode((ITypeNode)node));
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:35424-35427 — <c>isValidPropertyAccessForCompletions</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the generated bridge (`Checker.CreateTypeChecker.cs:522-524`) forwards nullable slots; TS's own
    /// parameters are non-nullable, so they are asserted once here.
    /// </remarks>
    internal bool IsValidPropertyAccessForCompletions(Node? nodeIn, IType? typeIn, ISymbol? propertyIn)
    {
        var node = nodeIn!;
        var type = typeIn!;
        var property = propertyIn!;
        return IsPropertyAccessible(node, node.Kind == SyntaxKind.PropertyAccessExpression && ((PropertyAccessExpression)node).Expression!.Kind == SyntaxKind.SuperKeyword, /*isWrite*/ false, type, property);
        // Previously we validated the 'this' type of methods but this adversely affected performance. See #31377 for more context.
    }

    /// <summary>ported from: src/compiler/checker.ts:35429-35442 — <c>isValidPropertyAccessWithType</c>.</summary>
    private bool IsValidPropertyAccessWithType(Node node, bool isSuper, string propertyName, IType type)
    {
        // Short-circuiting for improved performance.
        if (IsTypeAny(type))
        {
            return true;
        }

        var prop = GetPropertyOfType(type, propertyName);
        return prop is not null && IsPropertyAccessible(node, isSuper, /*isWrite*/ false, type, prop);
    }

    /// <summary>ported from: src/compiler/checker.ts:35496-35498 — <c>hasNumericPropertyNames</c>.</summary>
    private bool HasNumericPropertyNames(IType type)
    {
        return GetIndexInfosOfType(type).Count == 1 && GetIndexInfoOfType(type, _numberType) is not null;
    }

    /// <summary>ported from: src/compiler/checker.ts:35479-35491 — <c>getForInVariableSymbol</c>.</summary>
    private ISymbol? GetForInVariableSymbol(ForInStatement node)
    {
        var initializer = node.Initializer!;
        if (initializer.Kind == SyntaxKind.VariableDeclarationList)
        {
            var variable = ((VariableDeclarationList)initializer).Declarations!.Count > 0 ? ((VariableDeclarationList)initializer).Declarations![0] : null;
            if (variable is not null && !Utilities.IsBindingPattern(variable.Name))
            {
                return GetSymbolOfDeclaration(variable);
            }
        }
        else if (initializer.Kind == SyntaxKind.Identifier)
        {
            return GetResolvedSymbol((Identifier)initializer);
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:35504-35526 — <c>isForInVariableForNumericPropertyNames</c>.</summary>
    private bool IsForInVariableForNumericPropertyNames(Expression expr)
    {
        var e = Utilities.SkipParentheses(expr);
        if (e.Kind == SyntaxKind.Identifier)
        {
            var symbol = GetResolvedSymbol((Identifier)e);
            if ((symbol.Flags & SymbolFlags.Variable) != 0)
            {
                Node child = expr;
                var node = (Node?)expr.Parent;
                while (node is not null)
                {
                    if (
                        node.Kind == SyntaxKind.ForInStatement &&
                        ReferenceEquals(child, ((ForInStatement)node).Statement) &&
                        ReferenceEquals(GetForInVariableSymbol((ForInStatement)node), symbol) &&
                        HasNumericPropertyNames(GetTypeOfExpression(((ForInStatement)node).Expression!))
                    )
                    {
                        return true;
                    }
                    child = node;
                    node = node.Parent;
                }
            }
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:35528-35531 — <c>checkIndexedAccess</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>checkElementAccessChain(node as ElementAccessChain, checkMode)</c> is a no-op
    /// re-view — <c>ElementAccessChain extends ElementAccessExpression</c> (types.ts:3009-3011) — so the same runtime
    /// object is handed on. The port models the two as a class and its abstract subclass, and a plain
    /// <see cref="ElementAccessExpression"/> carrying <c>NodeFlags.OptionalChain</c> is exactly the value that reaches
    /// here; <c>CheckElementAccessChain</c> therefore takes the shape whose slots it reads. The flag test is the
    /// reference's and is unchanged.
    /// </remarks>
    internal IType CheckIndexedAccess(ElementAccessExpression node, CheckMode? checkMode)
    {
        return (node.Flags & NodeFlags.OptionalChain) != 0
            ? CheckElementAccessChain(node, checkMode)
            : CheckElementAccessExpression(node, CheckNonNullExpression(node.Expression!), checkMode);
    }

    /// <summary>ported from: src/compiler/checker.ts:35533-35537 — <c>checkElementAccessChain</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's parameter is <c>ElementAccessChain</c>, the subclass of the very class
    /// <c>checkIndexedAccess</c> receives; every slot read below (<c>expression</c>) is declared on
    /// <see cref="ElementAccessExpression"/>, so the parameter states the shape actually read rather than the nominal
    /// subclass TS's cast only re-viewed.
    /// </remarks>
    private IType CheckElementAccessChain(ElementAccessExpression node, CheckMode? checkMode)
    {
        var exprType = CheckExpression(node.Expression!);
        var nonOptionalType = GetOptionalExpressionType(exprType, node.Expression!);
        return PropagateOptionalTypeMarker(CheckElementAccessExpression(node, CheckNonNullType(nonOptionalType, node.Expression!), checkMode), node, !ReferenceEquals(nonOptionalType, exprType));
    }

    /// <summary>ported from: src/compiler/checker.ts:35227-35242 — <c>getSuggestedLibForNonExistentProperty</c>.</summary>
    private string? GetSuggestedLibForNonExistentProperty(string missingProperty, IType containingType)
    {
        var container = GetApparentType(containingType).Symbol;
        if (container is null)
        {
            return null;
        }
        var containingTypeName = Utilities.SymbolName(container);
        var allFeatures = Utilities.GetScriptTargetFeatures();
        if (allFeatures.TryGetValue(containingTypeName, out var typeFeatures))
        {
            foreach (var pair in typeFeatures)
            {
                if (Core.Contains(pair.Value, missingProperty))
                {
                    return pair.Key;
                }
            }
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:35209-35213 — <c>containerSeemsToBeEmptyDomElement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the <c>lib</c> option is the nullable list the port models the same way, and the regular expression is
    /// the Js engine's <c>test</c>.
    /// </remarks>
    private bool ContainerSeemsToBeEmptyDomElement(IType containingType)
    {
        return _compilerOptions.Lib is not null && !Core.Contains(_compilerOptions.Lib, "lib.dom.d.ts")
            && EveryContainedType(containingType, type => type.Symbol is not null && new System.Text.RegularExpressions.Regex("^(?:EventTarget|Node|(?:HTML[a-zA-Z]*)?Element)$").IsMatch(Utilities.UnescapeLeadingUnderscores(type.Symbol.EscapedName!)))
            && IsEmptyObjectType(containingType);
    }

    /// <summary>ported from: src/compiler/checker.ts:34819-34878 — <c>checkPrivateIdentifierPropertyAccess</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>anon</c> (checker.ts:1154) is the module-level <c>"(anonymous)"</c> constant; the port has no slot for
    /// it, so the literal is spelled where the reference reads it, with the same spelling.
    /// </remarks>
    private bool CheckPrivateIdentifierPropertyAccess(IType leftType, PrivateIdentifier right, ISymbol? lexicallyScopedIdentifier)
    {
        // Either the identifier could not be looked up in the lexical scope OR the lexically scoped identifier did not exist on the type.
        // Find a private identifier with the same description on the type.
        ISymbol? propertyOnType = null;
        var properties = GetPropertiesOfType(leftType);
        if (properties is not null)
        {
            foreach (var symbol in properties)
            {
                var decl = symbol.ValueDeclaration;
                if (decl is not null && Utilities.IsNamedDeclaration((Node)decl) && ((INamedDeclaration)decl).Name is PrivateIdentifier privateName
                    && privateName.EscapedText == right.EscapedText)
                {
                    propertyOnType = symbol;
                    break;
                }
            }
        }
        var diagName = DiagnosticName(right);
        if (propertyOnType is not null)
        {
            var typeValueDecl = (Node)Debug.CheckDefined(propertyOnType.ValueDeclaration);
            var typeClass = Debug.CheckDefined(Utilities.GetContainingClass(typeValueDecl));
            // We found a private identifier property with the same description.
            // Either:
            // - There is a lexically scoped private identifier AND it shadows the one we found on the type.
            // - It is an attempt to access the private identifier outside of the class.
            if (lexicallyScopedIdentifier?.ValueDeclaration is not null)
            {
                var lexicalValueDecl = (Node)lexicallyScopedIdentifier.ValueDeclaration;
                var lexicalClass = Utilities.GetContainingClass(lexicalValueDecl);
                Debug.Assert(lexicalClass is not null);
                if (Utilities.FindAncestor(lexicalClass, n => ReferenceEquals(typeClass, n) ? true : false) is not null)
                {
                    var diagnostic = Error(
                        right,
                        Diagnostics.The_property_0_cannot_be_accessed_on_type_1_within_this_class_because_it_is_shadowed_by_another_private_identifier_with_the_same_spelling,
                        diagName,
                        Diagnostic.DeferArg(() => TypeToString(leftType)));

                    Utilities.AddRelatedInfo(
                        diagnostic,
                        Utilities.CreateDiagnosticForNode(lexicalValueDecl, Diagnostics.The_shadowing_declaration_of_0_is_defined_here, diagName),
                        Utilities.CreateDiagnosticForNode(typeValueDecl, Diagnostics.The_declaration_of_0_that_you_probably_intended_to_use_is_defined_here, diagName));
                    return true;
                }
            }
            Error(
                right,
                Diagnostics.Property_0_is_not_accessible_outside_class_1_because_it_has_a_private_identifier,
                diagName,
                DiagnosticName(((INamedDeclaration)typeClass).Name is Node typeClassName ? (object)typeClassName : "(anonymous)"));
            return true;
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:34885-35004 — <c>checkPropertyAccessExpressionOrQualifiedName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>LanguageFeatureMinimumTarget</c> (types.ts:8488) has no port counterpart — the same note
    /// <c>Checker.Check.Classes.cs</c> states — so the two downlevel tests compare against the targets the table holds:
    /// <c>PrivateNamesAndClassStaticBlocks</c> is <c>ScriptTarget.ES2022</c> and <c>ClassAndClassElementDecorators</c> is
    /// <c>ScriptTarget.ESNext</c>.
    /// <para>
    /// PORT-NOTE: the parameter's <c>right</c> is TS's <c>Identifier | PrivateIdentifier</c> union, and every
    /// <c>right.escapedText</c> read is the *escaped* name — <c>getApplicableIndexInfoForName</c> (checker.ts:16052)
    /// tests <c>isLateBoundName</c> and unescapes it itself, so reading it with <c>idText</c> instead would lose
    /// <c>__@</c>-style late-bound names and split them from <c>__</c>-prefixed ones. The port's read of that slot for
    /// both classes is <see cref="Utilities.GetEscapedTextOfIdentifierOrLiteral"/>.
    /// </para>
    /// </remarks>
    private IType CheckPropertyAccessExpressionOrQualifiedName(Node node, Node left, IType leftType, Node right, CheckMode? checkMode, bool writeOnly = false)
    {
        var parentSymbol = GetNodeLinks(left).ResolvedSymbol;
        var assignmentKind = Utilities.GetAssignmentTargetKind(node);
        var apparentType = GetApparentType(assignmentKind != AssignmentKind.None || IsMethodAccessForCall(node) ? GetWidenedType(leftType) : leftType);
        var isAnyLike = IsTypeAny(apparentType) || ReferenceEquals(apparentType, _silentNeverType);
        ISymbol? prop;
        if (NodeTests.IsPrivateIdentifier(right))
        {
            var rightId = (PrivateIdentifier)right;
            if (
                _languageVersion < ScriptTarget.ES2022 ||
                _languageVersion < ScriptTarget.ESNext ||
                !Js.IsTruthy(_useDefineForClassFields)
            )
            {
                if (assignmentKind != AssignmentKind.None)
                {
                    CheckExternalEmitHelpers(node, ExternalEmitHelpers.ClassPrivateFieldSet);
                }
                if (assignmentKind != AssignmentKind.Definite)
                {
                    CheckExternalEmitHelpers(node, ExternalEmitHelpers.ClassPrivateFieldGet);
                }
            }

            var lexicallyScopedSymbol = LookupSymbolForPrivateIdentifierDeclaration(rightId.EscapedText!, rightId);
            if (assignmentKind != AssignmentKind.None && lexicallyScopedSymbol is not null && lexicallyScopedSymbol.ValueDeclaration is not null
                && NodeTests.IsMethodDeclaration((Node)lexicallyScopedSymbol.ValueDeclaration))
            {
                GrammarErrorOnNode(rightId, Diagnostics.Cannot_assign_to_private_method_0_Private_methods_are_not_writable, Utilities.IdText(rightId));
            }
            if (isAnyLike)
            {
                if (lexicallyScopedSymbol is not null)
                {
                    return IsErrorType(apparentType) ? _errorType : apparentType;
                }
                if (Utilities.GetContainingClassExcludingClassDecorators(rightId) is null)
                {
                    GrammarErrorOnNode(rightId, Diagnostics.Private_identifiers_are_not_allowed_outside_class_bodies);
                    return _anyType;
                }
            }

            prop = lexicallyScopedSymbol is not null ? GetPrivateIdentifierPropertyOfType(leftType, lexicallyScopedSymbol) : null;
            if (prop is null)
            {
                // Check for private-identifier-specific shadowing and lexical-scoping errors.
                if (CheckPrivateIdentifierPropertyAccess(leftType, rightId, lexicallyScopedSymbol))
                {
                    return _errorType;
                }
                var containingClass = Utilities.GetContainingClassExcludingClassDecorators(rightId);
                if (containingClass is not null && Utilities.IsPlainJsFile(Utilities.GetSourceFileOfNode(containingClass), _compilerOptions.CheckJs))
                {
                    GrammarErrorOnNode(rightId, Diagnostics.Private_field_0_must_be_declared_in_an_enclosing_class, Utilities.IdText(rightId));
                }
            }
            else
            {
                var isSetonlyAccessor = (prop.Flags & SymbolFlags.SetAccessor) != 0 && (prop.Flags & SymbolFlags.GetAccessor) == 0;
                if (isSetonlyAccessor && assignmentKind != AssignmentKind.Definite)
                {
                    Error(node, Diagnostics.Private_accessor_was_defined_without_a_getter);
                }
            }
        }
        else
        {
            if (isAnyLike)
            {
                if (NodeTests.IsIdentifier(left) && parentSymbol is not null)
                {
                    MarkLinkedReferences(node, ReferenceHint.Property, /*propSymbol*/ null, leftType);
                }
                return IsErrorType(apparentType) ? _errorType : apparentType;
            }
            prop = GetPropertyOfType(apparentType, ((Identifier)right).EscapedText!, /*skipObjectFunctionPropertyAugment*/ IsConstEnumObjectType(apparentType), /*includeTypeOnlyMembers*/ node.Kind == SyntaxKind.QualifiedName);
        }
        MarkLinkedReferences(node, ReferenceHint.Property, prop, leftType);

        IType propType;
        if (prop is null)
        {
            var indexInfo = !NodeTests.IsPrivateIdentifier(right) && (assignmentKind == AssignmentKind.None || !IsGenericObjectType(leftType) || Utilities.IsThisTypeParameter(leftType))
                ? GetApplicableIndexInfoForName(apparentType, Utilities.GetEscapedTextOfIdentifierOrLiteral(right))
                : null;
            if (!(indexInfo is not null && indexInfo.Type is not null))
            {
                var isUncheckedJS = IsUncheckedJSSuggestion(node, leftType.Symbol, /*excludeClasses*/ true);
                if (!isUncheckedJS && IsJSLiteralType(leftType))
                {
                    return _anyType;
                }
                if (ReferenceEquals(leftType.Symbol, _globalThisSymbol))
                {
                    var rightEscapedName = Utilities.IdText(right);
                    if (_globalThisSymbol!.Exports is { } globalThisExports && globalThisExports.TryGetValue(rightEscapedName, out var globalThisProp) && (globalThisProp.Flags & SymbolFlags.BlockScoped) != 0)
                    {
                        Error(right, Diagnostics.Property_0_does_not_exist_on_type_1, Utilities.UnescapeLeadingUnderscores(rightEscapedName), Diagnostic.DeferArg(() => TypeToString(leftType)));
                    }
                    else if (_noImplicitAny)
                    {
                        Error(right, Diagnostics.Element_implicitly_has_an_any_type_because_type_0_has_no_index_signature, Diagnostic.DeferArg(() => TypeToString(leftType)));
                    }
                    return _anyType;
                }
                if (rightEscapedNameCheck(right) && !CheckAndReportErrorForExtendingInterface(node))
                {
                    ReportNonexistentProperty(right, Utilities.IsThisTypeParameter(leftType) ? apparentType : leftType, isUncheckedJS);
                }
                return _errorType;
            }
            if (Js.IsTruthy(indexInfo.IsReadonly) && (Utilities.IsAssignmentTarget(node) || Utilities.IsDeleteTarget(node)))
            {
                Error(node, Diagnostics.Index_signature_in_type_0_only_permits_reading, Diagnostic.DeferArg(() => TypeToString(apparentType)));
            }

            propType = indexInfo.Type!;
            if (Js.IsTruthy(_compilerOptions.NoUncheckedIndexedAccess) && Utilities.GetAssignmentTargetKind(node) != AssignmentKind.Definite)
            {
                propType = GetUnionType([propType, _missingType]);
            }
            if (Js.IsTruthy(_compilerOptions.NoPropertyAccessFromIndexSignature) && NodeTests.IsPropertyAccessExpression(node))
            {
                Error(right, Diagnostics.Property_0_comes_from_an_index_signature_so_it_must_be_accessed_with_0, Utilities.UnescapeLeadingUnderscores(Utilities.IdText(right)));
            }
            if (indexInfo.Declaration is not null && IsDeprecatedDeclaration(indexInfo.Declaration))
            {
                AddDeprecatedSuggestion(right, [(Node)indexInfo.Declaration], Utilities.IdText(right));
            }
        }
        else
        {
            var targetPropSymbol = ResolveAliasWithDeprecationCheck(prop, right);
            if (IsDeprecatedSymbol(targetPropSymbol) && IsUncalledFunctionReference(node, targetPropSymbol) && targetPropSymbol.Declarations is not null)
            {
                AddDeprecatedSuggestion(right, Core.Map(targetPropSymbol.Declarations, (d, _) => (Node)d)!, Utilities.IdText(right));
            }
            CheckPropertyNotUsedBeforeDeclaration(prop, node, right);
            MarkPropertyAsReferenced(prop, node, IsSelfTypeAccess(left, parentSymbol));
            GetNodeLinks(node).ResolvedSymbol = prop;
            CheckPropertyAccessibility(node, left.Kind == SyntaxKind.SuperKeyword, Utilities.IsWriteAccess(node), apparentType, prop);
            // PORT-NOTE: checker.ts:34995 asserts `node as Expression` before this call. The callee's parameter is
            // `Expression` while `node` here is `PropertyAccessExpression | QualifiedName`, and a qualified name is not
            // an `Expression` — TS permits the assertion because the union overlaps `Expression`, but it is not a
            // re-view the port can make. The callee's first line answers `false` for `AssignmentKind.None`, and a
            // qualified name's `getAssignmentTargetKind` is always `None` (its parent is a `QualifiedName` or a
            // `TypeQuery`, and neither is one of the five kinds `getAssignmentTarget`, utilities.ts:4854-4897, walks
            // through or returns), so this kind test reproduces the reference's own answer for the member the port
            // cannot hand on.
            if (node is Expression nodeAsExpression && IsAssignmentToReadonlyEntity(nodeAsExpression, prop, assignmentKind))
            {
                Error(right, Diagnostics.Cannot_assign_to_0_because_it_is_a_read_only_property, Utilities.IdText(right));
                return _errorType;
            }

            propType = IsThisPropertyAccessInConstructor(node, prop) ? _autoType
                : writeOnly || Utilities.IsWriteOnlyAccess(node) ? GetWriteTypeOfSymbol(prop)
                : GetTypeOfSymbol(prop);
        }

        return GetFlowTypeOfAccessExpression(node, prop, propType, right, checkMode);

        static bool rightEscapedNameCheck(Node right) => Utilities.IdText(right).Length != 0;
    }

    /// <summary>ported from: src/compiler/checker.ts:4322-4329 — <c>tryResolveAlias</c>.</summary>
    private ISymbol? TryResolveAlias(ISymbol symbol)
    {
        var links = GetSymbolLinks(symbol);
        if (!ReferenceEquals(links.AliasTarget, _resolvingSymbol))
        {
            return ResolveAlias(symbol);
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:16117-16119 — <c>isOptionalPropertyDeclaration</c>.</summary>
    private static bool IsOptionalPropertyDeclaration(Node node)
        => NodeTests.IsPropertyDeclaration(node) && !Utilities.HasAccessorModifier(node) && ((PropertyDeclaration)node).QuestionToken is not null;

    /// <summary>ported from: src/compiler/checker.ts:35115-35132 — <c>isInPropertyInitializerOrClassStaticBlock</c>.</summary>
    private static bool IsInPropertyInitializerOrClassStaticBlock(Node node, bool ignoreArrowFunctions = false)
    {
        return Utilities.FindAncestor(node, node =>
        {
            switch (node.Kind)
            {
                case SyntaxKind.PropertyDeclaration:
                case SyntaxKind.ClassStaticBlockDeclaration:
                    return true;
                case SyntaxKind.TypeQuery:
                case SyntaxKind.JsxClosingElement: // already reported in JsxOpeningElement
                    return null;
                case SyntaxKind.ArrowFunction:
                    return ignoreArrowFunctions ? false : null;
                case SyntaxKind.Block:
                    return Utilities.IsFunctionLike(node.Parent!) && node.Parent!.Kind != SyntaxKind.ArrowFunction ? null : false;
                default:
                    return false;
            }
        }) is not null;
    }

    /// <summary>ported from: src/compiler/checker.ts:35138-35147 — <c>isPropertyDeclaredInAncestorClass</c>.</summary>
    private bool IsPropertyDeclaredInAncestorClass(ISymbol prop)
    {
        if (prop.Parent is not null && (prop.Parent.Flags & SymbolFlags.Class) != 0)
        {
            var baseTypes = GetBaseTypes((IInterfaceType)GetDeclaredTypeOfSymbol(prop.Parent));
            if (baseTypes.Count > 0)
            {
                var superProperty = GetPropertyOfType(baseTypes[0], prop.EscapedName!);
                return superProperty is not null && superProperty.ValueDeclaration is not null;
            }
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:35083-35113 — <c>checkPropertyNotUsedBeforeDeclaration</c>.</summary>
    private void CheckPropertyNotUsedBeforeDeclaration(ISymbol prop, Node node, Node right)
    {
        var valueDeclaration = prop.ValueDeclaration;
        if (valueDeclaration is null || Js.IsTruthy(Utilities.GetSourceFileOfNode(node)?.IsDeclarationFile))
        {
            return;
        }

        Diagnostic? diagnosticMessage = null;
        var declarationName = Utilities.IdText(right);
        var valueDeclarationNode = (Node)valueDeclaration;
        if (
            IsInPropertyInitializerOrClassStaticBlock(node)
            && !IsOptionalPropertyDeclaration(valueDeclarationNode)
            && !(Utilities.IsAccessExpression(node) && Utilities.IsAccessExpression(node.Parent!))
            && !IsBlockScopedNameDeclaredBeforeUse(valueDeclaration, right)
            && !(NodeTests.IsMethodDeclaration(valueDeclarationNode) && (GetCombinedModifierFlagsCached(valueDeclaration) & ModifierFlags.Static) != 0)
            && (Js.IsTruthy(_useDefineForClassFields) || !IsPropertyDeclaredInAncestorClass(prop))
        )
        {
            diagnosticMessage = Error(right, Diagnostics.Property_0_is_used_before_its_initialization, declarationName);
        }
        else if (
            valueDeclarationNode.Kind == SyntaxKind.ClassDeclaration &&
            node.Parent!.Kind != SyntaxKind.TypeReference &&
            (valueDeclarationNode.Flags & NodeFlags.Ambient) == 0 &&
            !IsBlockScopedNameDeclaredBeforeUse(valueDeclaration, right)
        )
        {
            diagnosticMessage = Error(right, Diagnostics.Class_0_used_before_its_declaration, declarationName);
        }

        if (diagnosticMessage is not null)
        {
            Utilities.AddRelatedInfo(diagnosticMessage, Utilities.CreateDiagnosticForNode(valueDeclarationNode, Diagnostics._0_is_declared_here, declarationName));
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:35030-35081 — <c>getFlowTypeOfAccessExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the parameters are TS's <c>ElementAccessExpression | PropertyAccessExpression | QualifiedName</c> and
    /// <c>Identifier | PrivateIdentifier</c> unions; the one slot the body reads off them is
    /// <c>node.expression</c> after <c>isAccessExpression</c> established the kind, which is exactly the union read
    /// <see cref="GetAccessExpressionOperand"/> states (the node itself is a <see cref="Node"/>, as it is at every
    /// landed call site of this method).
    /// </remarks>
    private IType GetFlowTypeOfAccessExpression(Node node, ISymbol? prop, IType propType, Node errorNode, CheckMode? checkMode)
    {
        // Only compute control flow type if this is a property access expression that isn't an
        // assignment target, and the referenced property was declared as a variable, property,
        // accessor, or optional method.
        var assignmentKind = Utilities.GetAssignmentTargetKind(node);
        if (assignmentKind == AssignmentKind.Definite)
        {
            return RemoveMissingType(propType, prop is not null && (prop.Flags & SymbolFlags.Optional) != 0);
        }
        if (
            prop is not null
            && (prop.Flags & (SymbolFlags.Variable | SymbolFlags.Property | SymbolFlags.Accessor)) == 0
            && !((prop.Flags & SymbolFlags.Method) != 0 && (propType.Flags & TypeFlags.Union) != 0)
            && !IsDuplicatedCommonJSExport(prop.Declarations)
        )
        {
            return propType;
        }
        if (ReferenceEquals(propType, _autoType))
        {
            return GetFlowTypeOfProperty(node, prop);
        }
        propType = GetNarrowableTypeForReference(propType, node, checkMode);
        // If strict null checks and strict property initialization checks are enabled, if we have
        // a this.xxx property access, if the property is an instance property without an initializer,
        // and if we are in a constructor of the same class as the property declaration, assume that
        // the property is uninitialized at the top of the control flow.
        var assumeUninitialized = false;
        if (_strictNullChecks && _strictPropertyInitialization && Utilities.IsAccessExpression(node) && GetAccessExpressionOperand(node).Kind == SyntaxKind.ThisKeyword)
        {
            var declaration = prop?.ValueDeclaration is not null ? (Node)prop.ValueDeclaration : null;
            if (declaration is not null && IsPropertyWithoutInitializer(declaration))
            {
                if (!Utilities.IsStatic(declaration))
                {
                    var flowContainer = GetControlFlowContainer(node);
                    if (flowContainer.Kind == SyntaxKind.Constructor && flowContainer.Parent == declaration.Parent && (declaration.Flags & NodeFlags.Ambient) == 0)
                    {
                        assumeUninitialized = true;
                    }
                }
            }
        }
        else if (
            _strictNullChecks && prop is not null && prop.ValueDeclaration is not null &&
            NodeTests.IsPropertyAccessExpression((Node)prop.ValueDeclaration) &&
            Utilities.GetAssignmentDeclarationPropertyAccessKind((Node)prop.ValueDeclaration) != AssignmentDeclarationKind.None &&
            ReferenceEquals(GetControlFlowContainer(node), GetControlFlowContainer((Node)prop.ValueDeclaration))
        )
        {
            assumeUninitialized = true;
        }
        var flowType = GetFlowTypeOfReference(node, propType, assumeUninitialized ? GetOptionalType(propType) : propType);
        if (assumeUninitialized && !ContainsUndefinedType(propType) && ContainsUndefinedType(flowType))
        {
            Error(errorNode, Diagnostics.Property_0_is_used_before_being_assigned, Diagnostic.DeferArg(() => SymbolToString(prop!)));
            // Return the declared type to reduce follow-on errors
            return propType;
        }
        return assignmentKind != AssignmentKind.None ? GetBaseTypeOfLiteralType(flowType) : flowType;
    }

    /// <summary>ported from: src/compiler/checker.ts:35149-35207 — <c>reportNonexistentProperty</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's parameter is the <c>Identifier | PrivateIdentifier</c> union and every
    /// <c>propNode.escapedText</c> read below is the *escaped* name (the same slot
    /// <c>getApplicableIndexInfoForName</c> unescapes itself, and <c>typeHasStaticProperty</c> compares verbatim); the
    /// port's read of it for both classes is <see cref="Utilities.GetEscapedTextOfIdentifierOrLiteral"/>, hoisted here
    /// because the reference reads it on four paths.
    /// </remarks>
    private void ReportNonexistentProperty(Node propNode, IType containingType, bool isUncheckedJS)
    {
        var links = GetNodeLinks(propNode);
        var cache = links.NonExistentPropCheckCache ??= [];
        var key = $"{GetTypeId(containingType)}|{isUncheckedJS}";
        if (cache.Contains(key))
        {
            return;
        }
        cache.Add(key);
        var propEscapedText = Utilities.GetEscapedTextOfIdentifierOrLiteral(propNode);
        IDiagnosticMessageChain? errorInfo = null;
        Diagnostic? relatedInfo = null;
        if (!NodeTests.IsPrivateIdentifier(propNode) && (containingType.Flags & TypeFlags.Union) != 0 && (containingType.Flags & TypeFlags.Primitive) == 0)
        {
            foreach (var subtype in ((IUnionType)containingType).Types!)
            {
                if (GetPropertyOfType(subtype, propEscapedText) is null && GetApplicableIndexInfoForName(subtype, propEscapedText) is null)
                {
                    errorInfo = Utilities.ChainDiagnosticMessages(errorInfo, Diagnostics.Property_0_does_not_exist_on_type_1, Utilities.DeclarationNameToString(propNode), Diagnostic.DeferArg(() => TypeToString(subtype)));
                    break;
                }
            }
        }
        if (TypeHasStaticProperty(propEscapedText, containingType))
        {
            var propName = Utilities.DeclarationNameToString(propNode);
            // PORT-NOTE (A″): one memoised thunk for both arguments — the reference computes `typeToString` once here.
            var typeName = Diagnostic.DeferArg(() => TypeToString(containingType));
            errorInfo = Utilities.ChainDiagnosticMessages(errorInfo, Diagnostics.Property_0_does_not_exist_on_type_1_Did_you_mean_to_access_the_static_member_2_instead, propName, typeName, Diagnostic.DeferArg(() => typeName.Resolve()! + "." + propName));
        }
        else
        {
            var promisedType = GetPromisedTypeOfPromise(containingType);
            if (promisedType is not null && GetPropertyOfType(promisedType, propEscapedText) is not null)
            {
                errorInfo = Utilities.ChainDiagnosticMessages(errorInfo, Diagnostics.Property_0_does_not_exist_on_type_1, Utilities.DeclarationNameToString(propNode), Diagnostic.DeferArg(() => TypeToString(containingType)));
                relatedInfo = Utilities.CreateDiagnosticForNode(propNode, Diagnostics.Did_you_forget_to_use_await);
            }
            else
            {
                var missingProperty = Utilities.DeclarationNameToString(propNode);
                var container = Diagnostic.DeferArg(() => TypeToString(containingType));
                var libSuggestion = GetSuggestedLibForNonExistentProperty(missingProperty, containingType);
                if (libSuggestion is not null)
                {
                    errorInfo = Utilities.ChainDiagnosticMessages(errorInfo, Diagnostics.Property_0_does_not_exist_on_type_1_Do_you_need_to_change_your_target_library_Try_changing_the_lib_compiler_option_to_2_or_later, missingProperty, container, libSuggestion);
                }
                else
                {
                    var suggestion = GetSuggestedSymbolForNonexistentProperty(propNode, containingType);
                    if (suggestion is not null)
                    {
                        var suggestedName = Utilities.SymbolName(suggestion);
                        var message = isUncheckedJS ? Diagnostics.Property_0_may_not_exist_on_type_1_Did_you_mean_2 : Diagnostics.Property_0_does_not_exist_on_type_1_Did_you_mean_2;
                        errorInfo = Utilities.ChainDiagnosticMessages(errorInfo, message, missingProperty, container, suggestedName);
                        relatedInfo = suggestion.ValueDeclaration is not null ? Utilities.CreateDiagnosticForNode((Node)suggestion.ValueDeclaration, Diagnostics._0_is_declared_here, suggestedName) : null;
                    }
                    else
                    {
                        var diagnostic = ContainerSeemsToBeEmptyDomElement(containingType)
                            ? Diagnostics.Property_0_does_not_exist_on_type_1_Try_changing_the_lib_compiler_option_to_include_dom
                            : Diagnostics.Property_0_does_not_exist_on_type_1;
                        errorInfo = Utilities.ChainDiagnosticMessages(ElaborateNeverIntersection(errorInfo, containingType), diagnostic, missingProperty, container);
                    }
                }
            }
        }
        var resultDiagnostic = Utilities.CreateDiagnosticForNodeFromMessageChain(Utilities.GetSourceFileOfNode(propNode)!, propNode, errorInfo!);
        if (relatedInfo is not null)
        {
            Utilities.AddRelatedInfo(resultDiagnostic, relatedInfo);
        }
        AddErrorOrSuggestion(!isUncheckedJS || errorInfo!.Code != Diagnostics.Property_0_may_not_exist_on_type_1_Did_you_mean_2.Code, resultDiagnostic);
    }

    /// <summary>ported from: src/compiler/checker.ts:35454-35474 — <c>isPropertyAccessible</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the generated bridge (`Checker.CreateTypeChecker.cs:893-894`) forwards nullable slots; TS's own
    /// parameters are non-nullable, so they are asserted once here and the reference's reads are kept.
    /// </remarks>
    internal bool IsPropertyAccessible(Node? nodeIn, bool isSuper, bool isWrite, IType? containingTypeIn, ISymbol? propertyIn)
    {
        var node = nodeIn!;
        var containingType = containingTypeIn!;
        var property = propertyIn!;
        // Short-circuiting for improved performance.
        if (IsTypeAny(containingType))
        {
            return true;
        }

        // A #private property access in an optional chain is an error dealt with by the parser.
        // The checker does not check for it, so we need to do our own check here.
        if (property.ValueDeclaration is not null && Utilities.IsPrivateIdentifierClassElementDeclaration((Node)property.ValueDeclaration))
        {
            var declClass = Utilities.GetContainingClass((Node)property.ValueDeclaration);
            return !Utilities.IsOptionalChain(node) && Utilities.FindAncestor(node, parent => ReferenceEquals(parent, declClass) ? true : false) is not null;
        }

        return CheckPropertyAccessibilityAtLocation(node, isSuper, isWrite, containingType, property, /*errorNode*/ null);
    }

    /// <summary>ported from: src/compiler/checker.ts:35539-35567 — <c>checkElementAccessExpression</c>.</summary>
    private IType CheckElementAccessExpression(ElementAccessExpression node, IType exprType, CheckMode? checkMode)
    {
        var objectType = Utilities.GetAssignmentTargetKind(node) != AssignmentKind.None || IsMethodAccessForCall(node) ? GetWidenedType(exprType) : exprType;
        var indexExpression = node.ArgumentExpression!;
        var indexType = CheckExpression(indexExpression);

        if (IsErrorType(objectType) || ReferenceEquals(objectType, _silentNeverType))
        {
            return objectType;
        }

        if (IsConstEnumObjectType(objectType) && !Utilities.IsStringLiteralLike(indexExpression))
        {
            Error(indexExpression, Diagnostics.A_const_enum_member_can_only_be_accessed_using_a_string_literal);
            return _errorType;
        }

        var effectiveIndexType = IsForInVariableForNumericPropertyNames(indexExpression) ? _numberType : indexType;
        var assignmentTargetKind = Utilities.GetAssignmentTargetKind(node);
        AccessFlags accessFlags;
        if (assignmentTargetKind == AssignmentKind.None)
        {
            accessFlags = AccessFlags.ExpressionPosition;
        }
        else
        {
            accessFlags = AccessFlags.Writing | (IsGenericObjectType(objectType) && !Utilities.IsThisTypeParameter(objectType) ? AccessFlags.NoIndexSignatures : 0);
            if (assignmentTargetKind == AssignmentKind.Compound)
            {
                accessFlags |= AccessFlags.ExpressionPosition;
            }
        }
        var indexedAccessType = GetIndexedAccessTypeOrUndefined(objectType, effectiveIndexType, accessFlags, node) ?? _errorType;
        return CheckIndexedAccessIndexType(GetFlowTypeOfAccessExpression(node, GetNodeLinks(node).ResolvedSymbol, indexedAccessType, indexExpression, checkMode), node);
    }

}


