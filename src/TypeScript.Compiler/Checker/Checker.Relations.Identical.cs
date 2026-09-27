using System.Diagnostics.CodeAnalysis;

namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:24902-25004 (typeCouldHaveTopLevelSingletonTypes … hasCommonProperties)
// ported from: src/compiler/checker.ts:25006-25175 (getVariances … getRelationKey)
// ported from: src/compiler/checker.ts:25177-25337 (forEachProperty … getRecursionIdentity)
/// <summary>
/// The identity/relation vocabulary: the marker-type variance computation, the relation cache key, the property
/// walkers that see through synthetic properties, the recursion identities by which the checker recognizes infinitely
/// expanding instantiations, and the structural comparers for properties, signatures and type predicates.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: this file is one slice of the type-relation block (checker.ts:24902-26773). Everything declared here is
/// a member of the <c>Checker</c> partial, because every one of these functions is declared <em>inside</em>
/// <c>createTypeChecker</c> in the reference; members of the <c>Utilities</c> partial (a <c>utilities.ts</c>
/// declaration) are called qualified and never declared here.
/// </para>
/// <para>
/// PORT-NOTE: the reference's nested functions (<c>getTypeReferenceId</c> inside
/// <c>getGenericTypeReferenceRelationKey</c>, <c>getSourceText</c>/<c>addMatch</c> inside
/// <c>inferFromLiteralPartsToTemplateLiteral</c>) are kept as C# local functions, which is the closest C# has to the
/// reference's shape and keeps the reference's own names and closures.
/// </para>
/// <para>
/// PORT-NOTE: the reference's <c>TypeComparer</c> (<c>types.ts:7153</c>, <c>(s, t, reportErrors?) =&gt; Ternary</c>) is
/// declared once by the sibling slice <c>Checker/Checker.Relations.Matching.cs</c> and referenced here; the landed
/// call sites in <c>Checker/Checker.Members.cs</c> pass the <c>CompareTypesIdentical</c>/<c>CompareTypesSubtypeOf</c>
/// method groups, which are <c>TypeComparer</c>-shaped.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // The relation state this slice owns (checker.ts:2245, 2270, 2352, 2373-2375)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:2245 — <c>reverseMappedCache</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>Map&lt;string, Type | undefined&gt;</c> memoizes both a computed type and a negative result;
    /// the port's nullable value does the same (one entry per key, <c>null</c> for "not reverse mappable").
    /// </remarks>
    internal Dictionary<string, IType?> _reverseMappedCache = null!;

    /// <summary>ported from: src/compiler/checker.ts:2270 — <c>deferredGlobalNonNullableTypeAlias</c>.</summary>
    internal ISymbol? _deferredGlobalNonNullableTypeAlias;

    /// <summary>ported from: src/compiler/checker.ts:2352 — <c>inVarianceComputation</c>.</summary>
    internal bool _inVarianceComputation;

    /// <summary>ported from: src/compiler/checker.ts:2373 — <c>reverseMappedSourceStack</c>.</summary>
    internal List<IType> _reverseMappedSourceStack = null!;

    /// <summary>ported from: src/compiler/checker.ts:2374 — <c>reverseMappedTargetStack</c>.</summary>
    internal List<IType> _reverseMappedTargetStack = null!;

    /// <summary>ported from: src/compiler/checker.ts:2375 — <c>reverseExpandingFlags</c>.</summary>
    internal ExpandingFlags _reverseExpandingFlags = ExpandingFlags.None;

    // ---------------------------------------------------------------------------------------------
    // Singleton, exact-optional and best-match helpers (checker.ts:24902-25004)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:24902-24920 — <c>typeCouldHaveTopLevelSingletonTypes</c>.</summary>
    internal bool TypeCouldHaveTopLevelSingletonTypes(IType type)
    {
        // Okay, yes, 'boolean' is a union of 'true | false', but that's not useful
        // in error reporting scenarios. If you need to use this function but that detail matters,
        // feel free to add a flag.
        if ((type.Flags & TypeFlags.Boolean) != 0)
        {
            return false;
        }

        if ((type.Flags & TypeFlags.UnionOrIntersection) != 0)
        {
            return Core.Some(((IUnionOrIntersectionType)type).Types, t => TypeCouldHaveTopLevelSingletonTypes(t));
        }

        if ((type.Flags & TypeFlags.Instantiable) != 0)
        {
            var constraint = GetConstraintOfType(type);
            if (constraint is not null && !ReferenceEquals(constraint, type))
            {
                return TypeCouldHaveTopLevelSingletonTypes(constraint);
            }
        }

        return IsUnitType(type) || (type.Flags & TypeFlags.TemplateLiteral) != 0 || (type.Flags & TypeFlags.StringMapping) != 0;
    }

    /// <summary>ported from: src/compiler/checker.ts:24924-24927 — <c>getExactOptionalUnassignableProperties</c>.</summary>
    internal List<ISymbol> GetExactOptionalUnassignableProperties(IType source, IType target)
    {
        if (IsTupleType(source) && IsTupleType(target))
        {
            return [];
        }

        return [.. Core.Filter(GetPropertiesOfType(target), targetProp => IsExactOptionalPropertyMismatch(GetTypeOfPropertyOfType(source, targetProp.EscapedName), GetTypeOfSymbol(targetProp)))!];
    }

    /// <summary>ported from: src/compiler/checker.ts:24930-24932 — <c>isExactOptionalPropertyMismatch</c>.</summary>
    internal bool IsExactOptionalPropertyMismatch(IType? source, IType? target)
        => source is not null && target is not null && MaybeTypeOfKind(source, TypeFlags.Undefined) && ContainsMissingType(target);

    /// <summary>ported from: src/compiler/checker.ts:24934-24936 — <c>getExactOptionalProperties</c>.</summary>
    internal List<ISymbol> GetExactOptionalProperties(IType type)
        => [.. Core.Filter(GetPropertiesOfType(type), targetProp => ContainsMissingType(GetTypeOfSymbol(targetProp)))!];

    /// <summary>ported from: src/compiler/checker.ts:24938-24943 — <c>getBestMatchingType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's default <c>isRelatedTo = compareTypesAssignable</c> is <c>TypeComparer</c>-shaped, so
    /// the port's optional parameter is a <c>TypeComparer</c> and supplies the landed
    /// <c>CompareTypesAssignable</c> when the caller passes nothing.
    /// </remarks>
    internal IType? GetBestMatchingType(IType source, IUnionOrIntersectionType target, TypeComparer? isRelatedTo = null)
        => FindMatchingDiscriminantType(source, target, isRelatedTo ?? CompareTypesAssignable)
            ?? FindMatchingTypeReferenceOrTypeAliasReference(source, target)
            ?? FindBestTypeForObjectLiteral(source, target)
            ?? FindBestTypeForInvokable(source, target)
            ?? FindMostOverlappyType(source, target);

    /// <summary>ported from: src/compiler/checker.ts:24946-24980 — <c>discriminateTypeByDiscriminableItems</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>boolean | Ternary</c> callback result is tested with <c>!!</c>, i.e. "not False"
    /// (<c>Ternary.False</c> is 0); the port takes the <c>Ternary</c> face and compares against
    /// <c>Ternary.False</c> for the same reason.
    /// </remarks>
    internal IType DiscriminateTypeByDiscriminableItems(IType target, IList<(Func<IType> GetDiscriminatingType, string PropertyName)> discriminators, Func<IType, IType, Ternary> related)
    {
        var types = ((IUnionType)target).Types!;
        var include = new List<Ternary>(types.Count);
        foreach (var t in types)
        {
            include.Add((t.Flags & TypeFlags.Primitive) != 0 || (GetReducedType(t).Flags & TypeFlags.Never) != 0 ? Ternary.False : Ternary.True);
        }

        foreach (var (getDiscriminatingType, propertyName) in discriminators)
        {
            // If the remaining target types include at least one with a matching discriminant, eliminate those that
            // have non-matching discriminants. This ensures that we ignore erroneous discriminators and gradually
            // refine the target set without eliminating every constituent (which would lead to `never`).
            var matched = false;
            for (var i = 0; i < types.Count; i++)
            {
                if (include[i] != Ternary.False)
                {
                    var targetType = GetTypeOfPropertyOrIndexSignatureOfType(types[i], propertyName);
                    if (targetType is not null)
                    {
                        if (SomeType(getDiscriminatingType(), t => related(t, targetType) != Ternary.False))
                        {
                            matched = true;
                        }
                        else
                        {
                            include[i] = Ternary.Maybe;
                        }
                    }
                }
            }

            // Turn each Ternary.Maybe into Ternary.False if there was a match. Otherwise, revert to Ternary.True.
            for (var i = 0; i < types.Count; i++)
            {
                if (include[i] == Ternary.Maybe)
                {
                    include[i] = matched ? Ternary.False : Ternary.True;
                }
            }
        }

        var filteredTypes = new List<IType>();
        for (var i = 0; i < types.Count; i++)
        {
            if (include[i] != Ternary.False)
            {
                filteredTypes.Add(types[i]);
            }
        }

        var filtered = Core.Contains(include, Ternary.False) ? GetUnionType(filteredTypes, UnionReduction.None) : target;
        return (filtered.Flags & TypeFlags.Never) != 0 ? target : filtered;
    }

    /// <summary>
    /// A type is 'weak' if it is an object type with at least one optional property
    /// and no required properties, call/construct signatures or index signatures.
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:24982-24995 — <c>isWeakType</c>.</remarks>
    internal bool IsWeakType(IType type)
    {
        if ((type.Flags & TypeFlags.Object) != 0)
        {
            var resolved = ResolveStructuredTypeMembers((UnionOrIntersectionType)type);
            return Core.Length(resolved.CallSignatures) == 0 && Core.Length(resolved.ConstructSignatures) == 0 && Core.Length(resolved.IndexInfos) == 0 &&
                Core.Length(resolved.Properties) > 0 && Core.Every(resolved.Properties, (p, _) => (p.Flags & SymbolFlags.Optional) != 0);
        }

        if ((type.Flags & TypeFlags.Substitution) != 0)
        {
            return IsWeakType(((ISubstitutionType)type).BaseType!);
        }

        if ((type.Flags & TypeFlags.Intersection) != 0)
        {
            return Core.Every(((IIntersectionType)type).Types, (t, _) => IsWeakType(t));
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:24997-25004 — <c>hasCommonProperties</c>.</summary>
    internal bool HasCommonProperties(IType source, IType target, bool isComparingJsxAttributes)
    {
        foreach (var prop in GetPropertiesOfType(source))
        {
            if (IsKnownProperty(target, prop.EscapedName ?? "", isComparingJsxAttributes))
            {
                return true;
            }
        }

        return false;
    }

    // ---------------------------------------------------------------------------------------------
    // Variance computation (checker.ts:25006-25078)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:25006-25011 — <c>getVariances</c>.</summary>
    internal List<VarianceFlags> GetVariances(IGenericType type)
        // Arrays and tuples are known to be covariant, no need to spend time computing this.
        => ReferenceEquals(type, _globalArrayType) || ReferenceEquals(type, _globalReadonlyArrayType) || (GetObjectFlags(type) & ObjectFlags.Tuple) != 0
            ? _arrayVariances
            : GetVariancesWorker(type.Symbol!, type.TypeParameters);

    /// <summary>ported from: src/compiler/checker.ts:25013-25015 — <c>getAliasVariances</c>.</summary>
    internal List<VarianceFlags> GetAliasVariances(ISymbol symbol)
        => GetVariancesWorker(symbol, GetSymbolLinks(symbol).TypeParameters);

    /// <summary>
    /// Return an array containing the variance of each type parameter. The variance is effectively
    /// a digest of the type comparisons that occur for each type argument when instantiations of the
    /// generic type are structurally compared. We infer the variance information by comparing
    /// instantiations of the generic type for type arguments with known relations. The function
    /// returns the emptyArray singleton when invoked recursively for the given generic type.
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:25022-25078 — <c>getVariancesWorker</c>.</remarks>
    /// <remarks>
    /// PORT-NOTE: the reference pushes a tracing event (<c>tracing?.push(tracing.Phase.CheckTypes, "getVariancesWorker", …)</c>
    /// / <c>tracing?.pop(…)</c>). The port's tracing facade (<c>Core/Tracing.cs</c>) is not wired into the checker —
    /// no landed checker member calls it — so the pair is omitted rather than invented; the observable result
    /// (<c>links.variances</c>) is unchanged.
    /// </remarks>
    internal List<VarianceFlags> GetVariancesWorker(ISymbol symbol, IList<ITypeParameter>? typeParameters = null)
    {
        typeParameters ??= [];
        var links = GetSymbolLinks(symbol);
        if (links.Variances is null)
        {
            var oldVarianceComputation = _inVarianceComputation;
            var saveResolutionStart = _resolutionStart;
            if (!_inVarianceComputation)
            {
                _inVarianceComputation = true;
                _resolutionStart = _resolutionTargets.Count;
            }

            // The empty list is the reference's `emptyArray` recursion signal: a re-entrant call for the same symbol
            // observes it and its caller falls back to VarianceFlags.Covariant (checker.ts:23261).
            links.Variances = [];
            var variances = new List<VarianceFlags>();
            foreach (var tp in typeParameters)
            {
                var modifiers = GetTypeParameterModifiers(tp);
                VarianceFlags? variance = (modifiers & ModifierFlags.Out) != 0
                    ? (modifiers & ModifierFlags.In) != 0 ? VarianceFlags.Invariant : VarianceFlags.Covariant
                    : (modifiers & ModifierFlags.In) != 0 ? VarianceFlags.Contravariant : null;
                if (variance is null)
                {
                    var unmeasurable = false;
                    var unreliable = false;
                    var oldHandler = _outofbandVarianceMarkerHandler;
                    _outofbandVarianceMarkerHandler = onlyUnreliable =>
                    {
                        if (onlyUnreliable)
                        {
                            unreliable = true;
                        }
                        else
                        {
                            unmeasurable = true;
                        }
                    };
                    // We first compare instantiations where the type parameter is replaced with
                    // marker types that have a known subtype relationship. From this we can infer
                    // invariance, covariance, contravariance or bivariance.
                    var typeWithSuper = CreateMarkerType(symbol, tp, _markerSuperType);
                    var typeWithSub = CreateMarkerType(symbol, tp, _markerSubType);
                    variance = (IsTypeAssignableTo(typeWithSub, typeWithSuper) ? VarianceFlags.Covariant : VarianceFlags.Invariant) |
                        (IsTypeAssignableTo(typeWithSuper, typeWithSub) ? VarianceFlags.Contravariant : VarianceFlags.Invariant);
                    // If the instantiations appear to be related bivariantly it may be because the
                    // type parameter is independent (i.e. it isn't witnessed anywhere in the generic
                    // type). To determine this we compare instantiations where the type parameter is
                    // replaced with marker types that are known to be unrelated.
                    if (variance == VarianceFlags.Bivariant && IsTypeAssignableTo(CreateMarkerType(symbol, tp, _markerOtherType), typeWithSuper))
                    {
                        variance = VarianceFlags.Independent;
                    }

                    _outofbandVarianceMarkerHandler = oldHandler;
                    if (unmeasurable || unreliable)
                    {
                        if (unmeasurable)
                        {
                            variance |= VarianceFlags.Unmeasurable;
                        }

                        if (unreliable)
                        {
                            variance |= VarianceFlags.Unreliable;
                        }
                    }
                }

                variances.Add(variance.Value);
            }

            if (!oldVarianceComputation)
            {
                _inVarianceComputation = false;
                _resolutionStart = saveResolutionStart;
            }

            links.Variances = variances;
        }

        return links.Variances;
    }

    /// <summary>ported from: src/compiler/checker.ts:25080-25091 — <c>createMarkerType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference passes <c>getSymbolLinks(symbol).typeParameters!</c> and
    /// <c>(type as GenericType).typeParameters</c> straight into <c>instantiateTypes</c>, whose optional overload answers
    /// <c>undefined</c> for an <c>undefined</c> list (so the second argument reaches <c>getTypeAliasInstantiation</c> /
    /// <c>createTypeReference</c> as <c>undefined</c>). The port's <see cref="InstantiateTypes"/> has the same nullable
    /// shape, so the two slots are passed through as they are: the `!` and the collection spread this used to carry were
    /// the port inventing a check TS does not make, and they threw a <c>NullReferenceException</c> on a generic type whose
    /// declared type carries no type parameters (`[.. null]`) — the crash that stopped a whole-corpus run (docs §17.17,
    /// §18.4f).
    /// </remarks>
    internal IType CreateMarkerType(ISymbol symbol, ITypeParameter source, IType target)
    {
        var mapper = MakeUnaryTypeMapper(source, target);
        var type = GetDeclaredTypeOfSymbol(symbol);
        if (IsErrorType(type))
        {
            return type;
        }

        var result = (symbol.Flags & SymbolFlags.TypeAlias) != 0
            ? GetTypeAliasInstantiation(symbol, InstantiateTypeParameters(GetSymbolLinks(symbol).TypeParameters, mapper))
            : CreateTypeReference((IGenericType)type, InstantiateTypeParameters(((IGenericType)type).TypeParameters, mapper));
        _markerTypes.Add(GetTypeId(result));
        return result;
    }

    /// <summary>
    /// <c>instantiateTypes</c> over a type-parameter list — the form the two <c>createMarkerType</c> arguments need.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: the reference infers <c>T = TypeParameter</c> for its generic <c>instantiateTypes</c> and hands the
    /// <c>TypeParameter[]</c> it answers straight to a <c>readonly Type[]</c> parameter — its arrays are covariant, so
    /// nothing moves. <c>List&lt;T&gt;</c> is invariant, so the port widens to <see cref="IType"/> first and instantiates
    /// after: the same sequence, and the only sound one, because the mapped element is whatever the mapper produces (for
    /// <c>T := string</c> that is a string literal type, not a type parameter) and an <c>InstantiateList&lt;ITypeParameter&gt;</c>
    /// would have cast it back.
    /// <para>
    /// PORT-NOTE (the crash this closes): the port used to spell the widening as a collection spread over the
    /// null-forgiven slot (<c>[.. typeParameters!]</c>), which throws on a generic type whose declared type carries no
    /// type parameters — <c>types/conditional/inferTypes1.ts</c> in the whole-corpus run (docs §18.4f). The reference
    /// passes the slot through and its optional <c>instantiateTypes</c> overload answers <c>undefined</c> for
    /// <c>undefined</c>, which is what the null check here states.
    /// </para>
    /// </remarks>
    private List<IType>? InstantiateTypeParameters(List<ITypeParameter>? typeParameters, TypeMapper mapper)
        => typeParameters is null ? null : InstantiateTypes([.. typeParameters], mapper);

    /// <summary>ported from: src/compiler/checker.ts:25093-25095 — <c>isMarkerType</c>.</summary>
    internal bool IsMarkerType(IType type)
        => _markerTypes.Contains(GetTypeId(type));

    /// <summary>ported from: src/compiler/checker.ts:25097-25099 — <c>getTypeParameterModifiers</c>.</summary>
    internal ModifierFlags GetTypeParameterModifiers(ITypeParameter tp)
        => Core.ReduceLeft(tp.Symbol?.Declarations, (modifiers, d, _) => modifiers | Utilities.GetEffectiveModifierFlags((Node)d), ModifierFlags.None) & (ModifierFlags.In | ModifierFlags.Out | ModifierFlags.Const);

    /// <summary>
    /// Return true if the given type reference has a 'void' type argument for a covariant type parameter.
    /// See comment at call in recursiveTypeRelatedTo for when this case matters.
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:25103-25110 — <c>hasCovariantVoidArgument</c>.</remarks>
    internal bool HasCovariantVoidArgument(IList<IType> typeArguments, IList<VarianceFlags> variances)
    {
        for (var i = 0; i < variances.Count; i++)
        {
            if ((variances[i] & VarianceFlags.VarianceMask) == VarianceFlags.Covariant && (typeArguments[i].Flags & TypeFlags.Void) != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:25112-25114 — <c>isUnconstrainedTypeParameter</c>.</summary>
    internal bool IsUnconstrainedTypeParameter(IType type)
        => (type.Flags & TypeFlags.TypeParameter) != 0 && GetConstraintOfTypeParameter((ITypeParameter)type) is null;

    /// <summary>ported from: src/compiler/checker.ts:25116-25118 — <c>isNonDeferredTypeReference</c>.</summary>
    internal bool IsNonDeferredTypeReference(IType type)
        => (GetObjectFlags(type) & ObjectFlags.Reference) != 0 && ((ITypeReference)type).Node is null;

    /// <summary>ported from: src/compiler/checker.ts:25120-25122 — <c>isTypeReferenceWithGenericArguments</c>.</summary>
    internal bool IsTypeReferenceWithGenericArguments(IType type)
        => IsNonDeferredTypeReference(type) && Core.Some(GetTypeArguments((ITypeReference)type), t => (t.Flags & TypeFlags.TypeParameter) != 0 || IsTypeReferenceWithGenericArguments(t));

    /// <summary>ported from: src/compiler/checker.ts:25124-25161 — <c>getGenericTypeReferenceRelationKey</c>.</summary>
    internal string GetGenericTypeReferenceRelationKey(ITypeReference source, ITypeReference target, string postFix, bool ignoreConstraints)
    {
        var typeParameters = new List<IType>();
        var constraintMarker = "";
        var sourceId = GetTypeReferenceId(source, 0);
        var targetId = GetTypeReferenceId(target, 0);
        return $"{constraintMarker}{sourceId},{targetId}{postFix}";
        // getTypeReferenceId(A<T, number, U>) returns "111=0-12=1"
        // where A.id=111 and number.id=12
        string GetTypeReferenceId(ITypeReference type, int depth = 0)
        {
            var result = "" + type.Target!.Id;
            foreach (var t in GetTypeArguments(type))
            {
                if ((t.Flags & TypeFlags.TypeParameter) != 0)
                {
                    if (ignoreConstraints || IsUnconstrainedTypeParameter(t))
                    {
                        var index = typeParameters.IndexOf(t);
                        if (index < 0)
                        {
                            index = typeParameters.Count;
                            typeParameters.Add(t);
                        }

                        result += "=" + index;
                        continue;
                    }

                    // We mark type references that reference constrained type parameters such that we know to obtain
                    // and look for a "broadest equivalent key" in the cache.
                    constraintMarker = "*";
                }
                else if (depth < 4 && IsTypeReferenceWithGenericArguments(t))
                {
                    result += "<" + GetTypeReferenceId((ITypeReference)t, depth + 1) + ">";
                    continue;
                }

                result += "-" + t.Id;
            }

            return result;
        }
    }

    /// <summary>
    /// To improve caching, the relation key for two generic types uses the target's id plus ids of the type parameters.
    /// For other cases, the types ids are used.
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:25163-25175 — <c>getRelationKey</c>.</remarks>
    internal RelationKey GetRelationKey(IType source, IType target, IntersectionState intersectionState, Dictionary<RelationKey, RelationComparisonResult> relation, bool ignoreConstraints)
    {
        if (ReferenceEquals(relation, _identityRelation) && source.Id > target.Id)
        {
            (source, target) = (target, source);
        }

        // The reference builds a string here (`${source.id},${target.id}${postFix}`) and hashes it; the ordinary pair
        // is two ids and a small state, so it goes into the value key exactly (no formatting, no hashing, no
        // allocation — see RelationKey). The generic branch keeps the reference's structured string inside the key.
        return IsTypeReferenceWithGenericArguments(source) && IsTypeReferenceWithGenericArguments(target)
            ? new RelationKey(0, 0, 0, GetGenericTypeReferenceRelationKey((ITypeReference)source, (ITypeReference)target, intersectionState != IntersectionState.None ? ":" + (int)intersectionState : "", ignoreConstraints))
            : new RelationKey(source.Id, target.Id, (int)intersectionState, null);
    }

    // ---------------------------------------------------------------------------------------------
    // Property walkers and class-heritage helpers (checker.ts:25177-25270)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Invoke the callback for each underlying property symbol of the given symbol and return the first
    /// value that isn't undefined.
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:25177-25191 — <c>forEachProperty</c>.</remarks>
    /// <remarks>
    /// PORT-NOTE: TS's one generic <c>forEachProperty&lt;T&gt;</c> is driven by truthiness; C# has none, so the single
    /// reference member is the two overloads below — the reference-typed one (<c>T extends Type</c> callers) and the
    /// boolean one, whose <c>false</c> is TS's falsy "keep looking". Both keep the reference's name.
    /// </remarks>
    internal T? ForEachProperty<T>(ISymbol prop, Func<ISymbol, T?> callback)
        where T : class
    {
        if ((GetCheckFlags(prop) & CheckFlags.Synthetic) != 0)
        {
            // NOTE: cast to TransientSymbol should be safe because only TransientSymbols can have CheckFlags.Synthetic
            foreach (var t in GetSymbolLinks(prop).ContainingType!.Types!)
            {
                var p = GetPropertyOfType(t, prop.EscapedName);
                var result = p is not null ? ForEachProperty(p, callback) : null;
                if (result is not null)
                {
                    return result;
                }
            }

            return null;
        }

        return callback(prop);
    }

    /// <summary>ported from: src/compiler/checker.ts:25177-25191 — <c>forEachProperty</c> (the boolean face).</summary>
    internal bool ForEachProperty(ISymbol prop, Func<ISymbol, bool> callback)
    {
        if ((GetCheckFlags(prop) & CheckFlags.Synthetic) != 0)
        {
            foreach (var t in GetSymbolLinks(prop).ContainingType!.Types!)
            {
                var p = GetPropertyOfType(t, prop.EscapedName);
                if (p is not null && ForEachProperty(p, callback))
                {
                    return true;
                }
            }

            return false;
        }

        return callback(prop);
    }

    /// <summary>Return the declaring class type of a property or undefined if property not declared in class.</summary>
    /// <remarks>ported from: src/compiler/checker.ts:25193-25196 — <c>getDeclaringClass</c>.</remarks>
    internal InterfaceType? GetDeclaringClass(ISymbol prop)
        => prop.Parent is not null && (prop.Parent.Flags & SymbolFlags.Class) != 0
            ? (InterfaceType)GetDeclaredTypeOfSymbol(GetParentOfSymbol(prop)!)
            : null;

    /// <summary>Return the inherited type of the given property or undefined if property doesn't exist in a base class.</summary>
    /// <remarks>ported from: src/compiler/checker.ts:25198-25204 — <c>getTypeOfPropertyInBaseClass</c>.</remarks>
    internal IType? GetTypeOfPropertyInBaseClass(ISymbol property)
    {
        var classType = GetDeclaringClass(property);
        var baseClassType = classType is not null ? Core.FirstOrUndefined(GetBaseTypes(classType)) : null;
        return baseClassType is not null ? GetTypeOfPropertyOfType(baseClassType, property.EscapedName) : null;
    }

    /// <summary>
    /// Return true if some underlying source property is declared in a class that derives
    /// from the given base class.
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:25206-25212 — <c>isPropertyInClassDerivedFrom</c>.</remarks>
    internal bool IsPropertyInClassDerivedFrom(ISymbol prop, IType? baseClass)
        => ForEachProperty(prop, sp =>
        {
            var sourceClass = GetDeclaringClass(sp);
            return sourceClass is not null && HasBaseType(sourceClass, baseClass);
        });

    /// <summary>Return true if source property is a valid override of protected parts of target property.</summary>
    /// <remarks>ported from: src/compiler/checker.ts:25214-25220 — <c>isValidOverrideOf</c>.</remarks>
    internal bool IsValidOverrideOf(ISymbol sourceProp, ISymbol targetProp)
        => !ForEachProperty(targetProp, tp =>
            (Utilities.GetDeclarationModifierFlagsFromSymbol(tp) & ModifierFlags.Protected) != 0
                ? !IsPropertyInClassDerivedFrom(sourceProp, GetDeclaringClass(tp))
                : false);

    /// <summary>
    /// Return true if the given class derives from each of the declaring classes of the protected
    /// constituents of the given property.
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:25222-25237 — <c>isClassDerivedFromDeclaringClasses</c>.</remarks>
    /// <remarks>
    /// PORT-NOTE: TS's <c>&lt;T extends Type&gt;(checkClass: T, …): T | undefined</c> keeps the caller's type; the port
    /// states the same with a type parameter constrained to the type face.
    /// </remarks>
    internal T? IsClassDerivedFromDeclaringClasses<T>(T checkClass, ISymbol prop, bool writing)
        where T : class, IType
        => ForEachProperty(prop, p =>
            (Utilities.GetDeclarationModifierFlagsFromSymbol(p, writing) & ModifierFlags.Protected) != 0
                ? !HasBaseType(checkClass, GetDeclaringClass(p))
                : false)
            ? null
            : checkClass;

    /// <summary>
    /// Return true if the given type is deeply nested. We consider this to be the case when the given stack contains
    /// maxDepth or more occurrences of types with the same recursion identity as the given type. The recursion identity
    /// provides a shared identity for type instantiations that repeat in some (possibly infinite) pattern.
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:25239-25270 — <c>isDeeplyNestedType</c>.</remarks>
    internal bool IsDeeplyNestedType(IType type, IList<IType> stack, int depth, int maxDepth = 3)
    {
        if (depth >= maxDepth)
        {
            if ((GetObjectFlags(type) & ObjectFlags.InstantiatedMapped) == ObjectFlags.InstantiatedMapped)
            {
                type = GetMappedTargetWithSymbol(type);
            }

            if ((type.Flags & TypeFlags.Intersection) != 0)
            {
                return Core.Some(((IIntersectionType)type).Types, t => IsDeeplyNestedType(t, stack, depth, maxDepth));
            }

            var identity = GetRecursionIdentity(type);
            var count = 0;
            var lastTypeId = 0;
            for (var i = 0; i < depth; i++)
            {
                var t = stack[i];
                if (HasMatchingRecursionIdentity(t, identity))
                {
                    // We only count occurrences with a higher type id than the previous occurrence, since higher
                    // type ids are an indicator of newer instantiations caused by recursion.
                    if (t.Id >= lastTypeId)
                    {
                        count++;
                        if (count >= maxDepth)
                        {
                            return true;
                        }
                    }

                    lastTypeId = t.Id;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Unwrap nested homomorphic mapped types and return the deepest target type that has a symbol. This better
    /// preserves unique type identities for mapped types applied to explicitly written object literals.
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:25272-25282 — <c>getMappedTargetWithSymbol</c>.</remarks>
    internal IType GetMappedTargetWithSymbol(IType type)
    {
        IType? target;
        while (
            (GetObjectFlags(type) & ObjectFlags.InstantiatedMapped) == ObjectFlags.InstantiatedMapped &&
            (target = GetModifiersTypeFromMappedType((IMappedType)type)) is not null &&
            (target.Symbol is not null || (target.Flags & TypeFlags.Intersection) != 0 && Core.Some(((IIntersectionType)target).Types, t => t.Symbol is not null))
        )
        {
            type = target;
        }

        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:25284-25291 — <c>hasMatchingRecursionIdentity</c>.</summary>
    internal bool HasMatchingRecursionIdentity(IType type, object identity)
    {
        if ((GetObjectFlags(type) & ObjectFlags.InstantiatedMapped) == ObjectFlags.InstantiatedMapped)
        {
            type = GetMappedTargetWithSymbol(type);
        }

        if ((type.Flags & TypeFlags.Intersection) != 0)
        {
            return Core.Some(((IIntersectionType)type).Types, t => HasMatchingRecursionIdentity(t, identity));
        }

        return ReferenceEquals(GetRecursionIdentity(type), identity);
    }

    /// <summary>
    /// The recursion identity of a type is an object identity that is shared among multiple instantiations of the type.
    /// We track recursion identities in order to identify deeply nested and possibly infinite type instantiations with
    /// the same origin. The default recursion identity is the object identity of the type, meaning that every type is
    /// unique.
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:25300-25337 — <c>getRecursionIdentity</c>.</remarks>
    internal object GetRecursionIdentity(IType type)
    {
        // Object and array literals are known not to contain recursive references and don't need a recursion identity.
        if ((type.Flags & TypeFlags.Object) != 0 && !IsObjectOrArrayLiteralType(type))
        {
            if ((GetObjectFlags(type) & ObjectFlags.Reference) != 0 && ((ITypeReference)type).Node is not null)
            {
                // Deferred type references are tracked through their associated AST node. This gives us finer
                // granularity than using their associated target because each manifest type reference has a
                // unique AST node.
                return ((ITypeReference)type).Node!;
            }

            if (type.Symbol is not null && !((GetObjectFlags(type) & ObjectFlags.Anonymous) != 0 && (type.Symbol.Flags & SymbolFlags.Class) != 0))
            {
                // We track object types that have a symbol by that symbol (representing the origin of the type), but
                // exclude the static side of a class since it shares its symbol with the instance side.
                return type.Symbol;
            }

            if (IsTupleType(type))
            {
                return ((ITypeReference)type).Target!;
            }
        }

        if ((type.Flags & TypeFlags.TypeParameter) != 0)
        {
            // We use the symbol of the type parameter such that all "fresh" instantiations of that type parameter
            // have the same recursion identity.
            return type.Symbol!;
        }

        if ((type.Flags & TypeFlags.IndexedAccess) != 0)
        {
            // Identity is the leftmost object type in a chain of indexed accesses, eg, in A[P1][P2][P3] it is A.
            do
            {
                type = ((IIndexedAccessType)type).ObjectType!;
            }
            while ((type.Flags & TypeFlags.IndexedAccess) != 0);
            return type;
        }

        if ((type.Flags & TypeFlags.Conditional) != 0)
        {
            // The root object represents the origin of the conditional type
            return ((IConditionalType)type).Root!;
        }

        return type;
    }
    // ---------------------------------------------------------------------------------------------
    // Structural comparers for properties, signatures and type predicates (checker.ts:25338-25464)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:25338-25340 — <c>isPropertyIdenticalTo</c>.</summary>
    internal bool IsPropertyIdenticalTo(ISymbol sourceProp, ISymbol targetProp)
        => CompareProperties(sourceProp, targetProp, CompareTypesIdentical) != Ternary.False;

    /// <summary>ported from: src/compiler/checker.ts:25342-25368 — <c>compareProperties</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference compares <c>getTargetSymbol(a) !== getTargetSymbol(b)</c>; the port's
    /// <c>GetTargetSymbol</c> answers <c>null</c> for a symbol that is not an instantiation, so the <c>?? prop</c>
    /// fallback is TS's own <c>getCheckFlags(s) &amp; CheckFlags.Instantiated ? s.links.target! : s</c> arm.
    /// </remarks>
    internal Ternary CompareProperties(ISymbol sourceProp, ISymbol targetProp, TypeComparer compareTypes)
    {
        // Two members are considered identical when
        // - they are public properties with identical names, optionality, and types,
        // - they are private or protected properties originating in the same declaration and having identical types
        if (ReferenceEquals(sourceProp, targetProp))
        {
            return Ternary.True;
        }

        var sourcePropAccessibility = Utilities.GetDeclarationModifierFlagsFromSymbol(sourceProp) & ModifierFlags.NonPublicAccessibilityModifier;
        var targetPropAccessibility = Utilities.GetDeclarationModifierFlagsFromSymbol(targetProp) & ModifierFlags.NonPublicAccessibilityModifier;
        if (sourcePropAccessibility != targetPropAccessibility)
        {
            return Ternary.False;
        }

        if (sourcePropAccessibility != ModifierFlags.None)
        {
            if (!ReferenceEquals(GetTargetSymbol(sourceProp) ?? sourceProp, GetTargetSymbol(targetProp) ?? targetProp))
            {
                return Ternary.False;
            }
        }
        else
        {
            if ((sourceProp.Flags & SymbolFlags.Optional) != (targetProp.Flags & SymbolFlags.Optional))
            {
                return Ternary.False;
            }
        }

        if (IsReadonlySymbol(sourceProp) != IsReadonlySymbol(targetProp))
        {
            return Ternary.False;
        }

        return compareTypes(GetNonMissingTypeOfSymbol(sourceProp), GetNonMissingTypeOfSymbol(targetProp));
    }

    /// <summary>ported from: src/compiler/checker.ts:25370-25395 — <c>isMatchingSignature</c>.</summary>
    internal bool IsMatchingSignature(ISignature source, ISignature target, bool partialMatch)
    {
        var sourceParameterCount = GetParameterCount(source);
        var targetParameterCount = GetParameterCount(target);
        var sourceMinArgumentCount = GetMinArgumentCount(source);
        var targetMinArgumentCount = GetMinArgumentCount(target);
        var sourceHasRestParameter = HasEffectiveRestParameter(source);
        var targetHasRestParameter = HasEffectiveRestParameter(target);
        // A source signature matches a target signature if the two signatures have the same number of required,
        // optional, and rest parameters.
        if (sourceParameterCount == targetParameterCount && sourceMinArgumentCount == targetMinArgumentCount && sourceHasRestParameter == targetHasRestParameter)
        {
            return true;
        }

        // A source signature partially matches a target signature if the target signature has no fewer required
        // parameters
        if (partialMatch && sourceMinArgumentCount <= targetMinArgumentCount)
        {
            return true;
        }

        return false;
    }

    /// <summary>See signatureRelatedTo, compareSignaturesIdentical.</summary>
    /// <remarks>ported from: src/compiler/checker.ts:25397-25457 — <c>compareSignaturesIdentical</c>.</remarks>
    /// <remarks>
    /// PORT-NOTE: <c>result &amp;= related</c> is the reference's bitwise combination of the three-valued result
    /// (<c>Ternary.True = -1</c>, <c>Maybe = 3</c>, <c>False = 0</c>), which C#'s <c>&amp;</c> on the same enum values
    /// reproduces exactly.
    /// </remarks>
    internal Ternary CompareSignaturesIdentical(ISignature source, ISignature target, bool partialMatch, bool ignoreThisTypes, bool ignoreReturnTypes, TypeComparer compareTypes)
    {
        // TODO (drosen): De-duplicate code between related functions.
        if (ReferenceEquals(source, target))
        {
            return Ternary.True;
        }

        if (!IsMatchingSignature(source, target, partialMatch))
        {
            return Ternary.False;
        }

        // Check that the two signatures have the same number of type parameters.
        if (Core.Length(source.TypeParameters) != Core.Length(target.TypeParameters))
        {
            return Ternary.False;
        }

        // Check that type parameter constraints and defaults match. If they do, instantiate the source
        // signature with the type parameters of the target signature and continue the comparison.
        if (target.TypeParameters is not null)
        {
            var mapper = CreateTypeMapper(source.TypeParameters!, new List<IType>(target.TypeParameters));
            for (var i = 0; i < target.TypeParameters.Count; i++)
            {
                var s = source.TypeParameters![i];
                var t = target.TypeParameters[i];
                if (!(ReferenceEquals(s, t)
                    || compareTypes(InstantiateType(GetConstraintFromTypeParameter(s), mapper) ?? _unknownType, GetConstraintFromTypeParameter(t) ?? _unknownType) != Ternary.False
                        && compareTypes(InstantiateType(GetDefaultFromTypeParameter(s), mapper) ?? _unknownType, GetDefaultFromTypeParameter(t) ?? _unknownType) != Ternary.False))
                {
                    return Ternary.False;
                }
            }

            source = InstantiateSignature(source, mapper, /*eraseTypeParameters*/ true);
        }

        var result = Ternary.True;
        if (!ignoreThisTypes)
        {
            var sourceThisType = GetThisTypeOfSignature(source);
            if (sourceThisType is not null)
            {
                var targetThisType = GetThisTypeOfSignature(target);
                if (targetThisType is not null)
                {
                    var related = compareTypes(sourceThisType, targetThisType);
                    if (related == Ternary.False)
                    {
                        return Ternary.False;
                    }

                    result &= related;
                }
            }
        }

        var targetLen = GetParameterCount(target);
        for (var i = 0; i < targetLen; i++)
        {
            var s = GetTypeAtPosition(source, i);
            var t = GetTypeAtPosition(target, i);
            var related = compareTypes(t, s);
            if (related == Ternary.False)
            {
                return Ternary.False;
            }

            result &= related;
        }

        if (!ignoreReturnTypes)
        {
            var sourceTypePredicate = GetTypePredicateOfSignature(source);
            var targetTypePredicate = GetTypePredicateOfSignature(target);
            result &= sourceTypePredicate is not null || targetTypePredicate is not null
                ? CompareTypePredicatesIdentical(sourceTypePredicate, targetTypePredicate, compareTypes)
                : compareTypes(GetReturnTypeOfSignature(source), GetReturnTypeOfSignature(target));
        }

        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:25459-25464 — <c>compareTypePredicatesIdentical</c>.</summary>
    internal Ternary CompareTypePredicatesIdentical(ITypePredicateBase? source, ITypePredicateBase? target, TypeComparer compareTypes)
        => !(source is not null && target is not null && TypePredicateKindsMatch(source, target)) ? Ternary.False
            : ReferenceEquals(source.Type, target.Type) ? Ternary.True
            : source.Type is not null && target.Type is not null ? compareTypes(source.Type, target.Type)
            : Ternary.False;

    // ---------------------------------------------------------------------------------------------
    // Common supertypes and subtypes, and the array predicates (checker.ts:25466-25545)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:25466-25478 — <c>literalTypesWithSameBaseType</c>.</summary>
    internal bool LiteralTypesWithSameBaseType(List<IType> types)
    {
        IType? commonBaseType = null;
        foreach (var t in types)
        {
            if ((t.Flags & TypeFlags.Never) == 0)
            {
                var baseType = GetBaseTypeOfLiteralType(t);
                commonBaseType ??= baseType;
                if (ReferenceEquals(baseType, t) || !ReferenceEquals(baseType, commonBaseType))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>ported from: src/compiler/checker.ts:25480-25482 — <c>getCombinedTypeFlags</c>.</summary>
    internal static TypeFlags GetCombinedTypeFlags(List<IType> types)
        => Core.ReduceLeft<IType, TypeFlags>(types, (flags, t, _) => flags | ((t.Flags & TypeFlags.Union) != 0 ? GetCombinedTypeFlags(((IUnionType)t).Types!) : t.Flags), (TypeFlags)0);

    /// <summary>ported from: src/compiler/checker.ts:25484-25498 — <c>getCommonSupertype</c>.</summary>
    internal IType GetCommonSupertype(List<IType> types)
    {
        if (types.Count == 1)
        {
            return types[0];
        }

        // Remove nullable types from each of the candidates.
        var primaryTypes = _strictNullChecks ? Core.SameMap(types, (t, _) => FilterType(t, u => (u.Flags & TypeFlags.Nullable) == 0))! : (IList<IType>)types;
        // When the candidate types are all literal types with the same base type, return a union
        // of those literal types. Otherwise, return the leftmost type for which no type to the
        // right is a supertype.
        var superTypeOrUnion = LiteralTypesWithSameBaseType([.. primaryTypes])
            ? GetUnionType([.. primaryTypes])
            : GetSingleCommonSupertype([.. primaryTypes]);
        // Add any nullable types that occurred in the candidates back to the result.
        return ReferenceEquals(primaryTypes, types) ? superTypeOrUnion : GetNullableType(superTypeOrUnion, GetCombinedTypeFlags(types) & TypeFlags.Nullable);
    }

    /// <summary>ported from: src/compiler/checker.ts:25500-25509 — <c>getSingleCommonSupertype</c>.</summary>
    internal IType GetSingleCommonSupertype(List<IType> types)
    {
        // First, find the leftmost type for which no type to the right is a strict supertype, and if that
        // type is a strict supertype of all other candidates, return it. Otherwise, return the leftmost type
        // for which no type to the right is a (regular) supertype.
        var candidate = Core.ReduceLeft(types, (s, t, _) => IsTypeStrictSubtypeOf(s, t) ? t : s)!;
        return Core.Every(types, (t, _) => ReferenceEquals(t, candidate) || IsTypeStrictSubtypeOf(t, candidate))
            ? candidate
            : Core.ReduceLeft(types, (s, t, _) => IsTypeSubtypeOf(s, t) ? t : s)!;
    }

    /// <summary>Return the leftmost type for which no type to the right is a subtype.</summary>
    /// <remarks>ported from: src/compiler/checker.ts:25511-25513 — <c>getCommonSubtype</c>.</remarks>
    internal IType GetCommonSubtype(List<IType> types)
        => Core.ReduceLeft(types, (s, t, _) => IsTypeSubtypeOf(t, s) ? t : s)!;

    /// <summary>ported from: src/compiler/checker.ts:25515-25517 — <c>isArrayType</c>.</summary>
    internal bool IsArrayType(IType type)
        => (GetObjectFlags(type) & ObjectFlags.Reference) != 0
            && (ReferenceEquals(((ITypeReference)type).Target, _globalArrayType) || ReferenceEquals(((ITypeReference)type).Target, _globalReadonlyArrayType));

    /// <summary>ported from: src/compiler/checker.ts:25519-25521 — <c>isReadonlyArrayType</c>.</summary>
    internal bool IsReadonlyArrayType(IType type)
        => (GetObjectFlags(type) & ObjectFlags.Reference) != 0 && ReferenceEquals(((ITypeReference)type).Target, _globalReadonlyArrayType);

    /// <summary>ported from: src/compiler/checker.ts:25523-25525 — <c>isArrayOrTupleType</c>.</summary>
    internal bool IsArrayOrTupleType(IType type)
        => IsArrayType(type) || IsTupleType(type);

    /// <summary>ported from: src/compiler/checker.ts:25527-25529 — <c>isMutableArrayOrTuple</c>.</summary>
    internal bool IsMutableArrayOrTuple(IType type)
        => IsArrayType(type) && !IsReadonlyArrayType(type) || IsTupleType(type) && !((ITupleType)((ITypeReference)type).Target!).Readonly;

    /// <summary>ported from: src/compiler/checker.ts:25531-25533 — <c>getElementTypeOfArrayType</c>.</summary>
    internal IType? GetElementTypeOfArrayType(IType type)
        => IsArrayType(type) ? GetTypeArguments((ITypeReference)type)[0] : null;

    /// <summary>ported from: src/compiler/checker.ts:25535-25539 — <c>isArrayLikeType</c>.</summary>
    internal bool IsArrayLikeType(IType type)
        // A type is array-like if it is a reference to the global Array or global ReadonlyArray type,
        // or if it is not the undefined or null type and if it is assignable to ReadonlyArray<any>
        => IsArrayType(type) || (type.Flags & TypeFlags.Nullable) == 0 && IsTypeAssignableTo(type, _anyReadonlyArrayType);

    /// <summary>ported from: src/compiler/checker.ts:25541-25545 — <c>isMutableArrayLikeType</c>.</summary>
    internal bool IsMutableArrayLikeType(IType type)
        // A type is mutable-array-like if it is a reference to the global Array type, or if it is not the
        // any, undefined or null type and if it is assignable to Array<any>
        => IsMutableArrayOrTuple(type) || (type.Flags & (TypeFlags.Any | TypeFlags.Nullable)) == 0 && IsTypeAssignableTo(type, _anyArrayType);
    // ---------------------------------------------------------------------------------------------
    // Single bases, literal types and the tuple helpers (checker.ts:25547-25760)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:25547-25577 — <c>getSingleBaseForNonAugmentingSubtype</c>.</summary>
    internal IType? GetSingleBaseForNonAugmentingSubtype(IType type)
    {
        if ((GetObjectFlags(type) & ObjectFlags.Reference) == 0 || (GetObjectFlags(((ITypeReference)type).Target!) & ObjectFlags.ClassOrInterface) == 0)
        {
            return null;
        }

        if ((GetObjectFlags(type) & ObjectFlags.IdenticalBaseTypeCalculated) != 0)
        {
            return (GetObjectFlags(type) & ObjectFlags.IdenticalBaseTypeExists) != 0 ? ((ITypeReference)type).CachedEquivalentBaseType : null;
        }

        ((ITypeReference)type).ObjectFlags |= ObjectFlags.IdenticalBaseTypeCalculated;
        var target = (InterfaceType)((ITypeReference)type).Target!;
        if ((GetObjectFlags(target) & ObjectFlags.Class) != 0)
        {
            var baseTypeNode = GetBaseTypeNodeOfClass(target);
            // A base type expression may circularly reference the class itself (e.g. as an argument to function call), so we only
            // check for base types specified as simple qualified names.
            if (baseTypeNode is not null && baseTypeNode.Expression!.Kind != SyntaxKind.Identifier && baseTypeNode.Expression.Kind != SyntaxKind.PropertyAccessExpression)
            {
                return null;
            }
        }

        var bases = GetBaseTypes(target);
        if (bases.Count != 1)
        {
            return null;
        }

        if (GetMembersOfSymbol(type.Symbol!).Count != 0)
        {
            return null; // If the interface has any members, they may subtype members in the base, so we should do a full structural comparison
        }

        var instantiatedBase = Core.Length(target.TypeParameters) == 0
            ? bases[0]
            : InstantiateType(bases[0], CreateTypeMapper(target.TypeParameters!, GetTypeArguments((ITypeReference)type).GetRange(0, target.TypeParameters!.Count)))!;
        if (Core.Length(GetTypeArguments((ITypeReference)type)) > Core.Length(target.TypeParameters))
        {
            instantiatedBase = GetTypeWithThisArgument(instantiatedBase, Core.LastOrUndefined(GetTypeArguments((ITypeReference)type)));
        }

        ((ITypeReference)type).ObjectFlags |= ObjectFlags.IdenticalBaseTypeExists;
        return ((ITypeReference)type).CachedEquivalentBaseType = instantiatedBase;
    }

    /// <summary>ported from: src/compiler/checker.ts:25579-25581 — <c>isEmptyLiteralType</c>.</summary>
    internal bool IsEmptyLiteralType(IType type)
        => _strictNullChecks ? ReferenceEquals(type, _implicitNeverType) : ReferenceEquals(type, _undefinedWideningType);

    /// <summary>ported from: src/compiler/checker.ts:25583-25586 — <c>isEmptyArrayLiteralType</c>.</summary>
    internal bool IsEmptyArrayLiteralType(IType type)
    {
        var elementType = GetElementTypeOfArrayType(type);
        return elementType is not null && IsEmptyLiteralType(elementType);
    }

    /// <summary>ported from: src/compiler/checker.ts:25588-25593 — <c>isTupleLikeType</c>.</summary>
    internal bool IsTupleLikeType(IType type)
    {
        IType? lengthType;
        return IsTupleType(type)
            || GetPropertyOfType(type, "0") is not null
            || IsArrayLikeType(type) && (lengthType = GetTypeOfPropertyOfType(type, "length")) is not null && EveryType(lengthType, t => (t.Flags & TypeFlags.NumberLiteral) != 0);
    }

    /// <summary>ported from: src/compiler/checker.ts:25595-25597 — <c>isArrayOrTupleLikeType</c>.</summary>
    internal bool IsArrayOrTupleLikeType(IType type)
        => IsArrayLikeType(type) || IsTupleLikeType(type);

    /// <summary>ported from: src/compiler/checker.ts:25599-25608 — <c>getTupleElementType</c>.</summary>
    internal IType? GetTupleElementType(IType type, int index)
    {
        var propType = GetTypeOfPropertyOfType(type, "" + index);
        if (propType is not null)
        {
            return propType;
        }

        if (EveryType(type, IsTupleType))
        {
            return GetTupleElementTypeOutOfStartCount(type, index, (_compilerOptions.NoUncheckedIndexedAccess ?? false) ? _undefinedType : null);
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:25610-25612 — <c>isNeitherUnitTypeNorNever</c>.</summary>
    internal bool IsNeitherUnitTypeNorNever(IType type)
        => (type.Flags & (TypeFlags.Unit | TypeFlags.Never)) == 0;

    /// <summary>ported from: src/compiler/checker.ts:25614-25616 — <c>isUnitType</c>.</summary>
    internal bool IsUnitType(IType type)
        => (type.Flags & TypeFlags.Unit) != 0;

    /// <summary>ported from: src/compiler/checker.ts:25618-25623 — <c>isUnitLikeType</c>.</summary>
    internal bool IsUnitLikeType(IType type)
    {
        // Intersections that reduce to 'never' (e.g. 'T & null' where 'T extends {}') are not unit types.
        var t = GetBaseConstraintOrType(type);
        // Scan intersections such that tagged literal types are considered unit types.
        return (t.Flags & TypeFlags.Intersection) != 0 ? Core.Some(((IIntersectionType)t).Types, IsUnitType) : IsUnitType(t);
    }

    /// <summary>ported from: src/compiler/checker.ts:25625-25627 — <c>extractUnitType</c>.</summary>
    internal IType ExtractUnitType(IType type)
        => (type.Flags & TypeFlags.Intersection) != 0 ? Core.Find(((IIntersectionType)type).Types, (t, _) => IsUnitType(t)) ?? type : type;

    /// <summary>ported from: src/compiler/checker.ts:25629-25633 — <c>isLiteralType</c>.</summary>
    internal bool IsLiteralType(IType type)
        => (type.Flags & TypeFlags.Boolean) != 0
            ? true
            : (type.Flags & TypeFlags.Union) != 0
                ? (type.Flags & TypeFlags.EnumLiteral) != 0 || Core.Every(((IUnionType)type).Types, (t, _) => IsUnitType(t))
                : IsUnitType(type);

    /// <summary>ported from: src/compiler/checker.ts:25635-25643 — <c>getBaseTypeOfLiteralType</c>.</summary>
    internal IType GetBaseTypeOfLiteralType(IType type)
        => (type.Flags & TypeFlags.EnumLike) != 0 ? GetBaseTypeOfEnumLikeType(type)
            : (type.Flags & (TypeFlags.StringLiteral | TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) != 0 ? _stringType
            : (type.Flags & TypeFlags.NumberLiteral) != 0 ? _numberType
            : (type.Flags & TypeFlags.BigIntLiteral) != 0 ? _bigintType
            : (type.Flags & TypeFlags.BooleanLiteral) != 0 ? _booleanType
            : (type.Flags & TypeFlags.Union) != 0 ? GetBaseTypeOfLiteralTypeUnion((IUnionType)type)
            : type;

    /// <summary>ported from: src/compiler/checker.ts:25645-25650 — <c>getBaseTypeOfLiteralTypeUnion</c>.</summary>
    internal IType GetBaseTypeOfLiteralTypeUnion(IUnionType type)
    {
        var key = $"B{GetTypeId(type)}";
        return GetCachedType(key) ?? SetCachedType(key, MapType(type, GetBaseTypeOfLiteralType)!);
    }

    /// <summary>
    /// This like getBaseTypeOfLiteralType, but instead treats enum literals as strings/numbers instead
    /// of returning their enum base type (which depends on the types of other literals in the enum).
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:25652-25659 — <c>getBaseTypeOfLiteralTypeForComparison</c>.</remarks>
    internal IType GetBaseTypeOfLiteralTypeForComparison(IType type)
        => (type.Flags & (TypeFlags.StringLiteral | TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) != 0 ? _stringType
            : (type.Flags & (TypeFlags.NumberLiteral | TypeFlags.Enum)) != 0 ? _numberType
            : (type.Flags & TypeFlags.BigIntLiteral) != 0 ? _bigintType
            : (type.Flags & TypeFlags.BooleanLiteral) != 0 ? _booleanType
            : (type.Flags & TypeFlags.Union) != 0 ? MapType(type, GetBaseTypeOfLiteralTypeForComparison)!
            : type;

    /// <summary>ported from: src/compiler/checker.ts:25661-25669 — <c>getWidenedLiteralType</c>.</summary>
    internal IType GetWidenedLiteralType(IType type)
        => (type.Flags & TypeFlags.EnumLike) != 0 && IsFreshLiteralType(type) ? GetBaseTypeOfEnumLikeType(type)
            : (type.Flags & TypeFlags.StringLiteral) != 0 && IsFreshLiteralType(type) ? _stringType
            : (type.Flags & TypeFlags.NumberLiteral) != 0 && IsFreshLiteralType(type) ? _numberType
            : (type.Flags & TypeFlags.BigIntLiteral) != 0 && IsFreshLiteralType(type) ? _bigintType
            : (type.Flags & TypeFlags.BooleanLiteral) != 0 && IsFreshLiteralType(type) ? _booleanType
            : (type.Flags & TypeFlags.Union) != 0 ? MapType(type, GetWidenedLiteralType)!
            : type;

    /// <summary>ported from: src/compiler/checker.ts:25671-25675 — <c>getWidenedUniqueESSymbolType</c>.</summary>
    internal IType GetWidenedUniqueESSymbolType(IType type)
        => (type.Flags & TypeFlags.UniqueESSymbol) != 0 ? _esSymbolType
            : (type.Flags & TypeFlags.Union) != 0 ? MapType(type, GetWidenedUniqueESSymbolType)!
            : type;

    /// <summary>ported from: src/compiler/checker.ts:25677-25682 — <c>getWidenedLiteralLikeTypeForContextualType</c>.</summary>
    internal IType GetWidenedLiteralLikeTypeForContextualType(IType type, IType? contextualType)
    {
        if (!IsLiteralOfContextualType(type, contextualType))
        {
            type = GetWidenedUniqueESSymbolType(GetWidenedLiteralType(type));
        }

        return GetRegularTypeOfLiteralType(type);
    }

    /// <summary>ported from: src/compiler/checker.ts:25684-25692 — <c>getWidenedLiteralLikeTypeForContextualReturnTypeIfNeeded</c>.</summary>
    internal IType? GetWidenedLiteralLikeTypeForContextualReturnTypeIfNeeded(IType? type, IType? contextualSignatureReturnType, bool isAsync)
    {
        if (type is not null && IsUnitType(type))
        {
            var contextualType = contextualSignatureReturnType is null ? null
                : isAsync ? GetPromisedTypeOfPromise(contextualSignatureReturnType)
                : contextualSignatureReturnType;
            type = GetWidenedLiteralLikeTypeForContextualType(type, contextualType);
        }

        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:25694-25705 — <c>getWidenedLiteralLikeTypeForContextualIterationTypeIfNeeded</c>.</summary>
    internal IType? GetWidenedLiteralLikeTypeForContextualIterationTypeIfNeeded(IType? type, IType? contextualSignatureReturnType, IterationTypeKind kind, bool isAsyncGenerator)
    {
        if (type is not null && IsUnitType(type))
        {
            var contextualType = contextualSignatureReturnType is null ? null
                : GetIterationTypeOfGeneratorFunctionReturnType(kind, contextualSignatureReturnType, isAsyncGenerator);
            type = GetWidenedLiteralLikeTypeForContextualType(type, contextualType);
        }

        return type;
    }

    /// <summary>
    /// Check if a Type was written as a tuple type literal.
    /// Prefer using isTupleLikeType() unless the use of `elementTypes`/`getTypeArguments` is required.
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:25707-25709 — <c>isTupleType</c>.</remarks>
    internal bool IsTupleType(IType type)
        => (GetObjectFlags(type) & ObjectFlags.Reference) != 0 && (((ITypeReference)type).Target!.ObjectFlags & ObjectFlags.Tuple) != 0;

    /// <summary>ported from: src/compiler/checker.ts:25711-25713 — <c>isGenericTupleType</c>.</summary>
    internal bool IsGenericTupleType(IType type)
        => IsTupleType(type) && (((ITupleType)((ITypeReference)type).Target!).CombinedFlags & ElementFlags.Variadic) != 0;

    /// <summary>ported from: src/compiler/checker.ts:25715-25717 — <c>isSingleElementGenericTupleType</c>.</summary>
    internal bool IsSingleElementGenericTupleType(IType type)
        => IsGenericTupleType(type) && ((ITupleType)((ITypeReference)type).Target!).ElementFlags!.Count == 1;

    /// <summary>ported from: src/compiler/checker.ts:25719-25721 — <c>getRestTypeOfTupleType</c>.</summary>
    internal IType? GetRestTypeOfTupleType(ITypeReference type)
        => GetElementTypeOfSliceOfTupleType(type, ((ITupleType)type.Target!).FixedLength);

    /// <summary>ported from: src/compiler/checker.ts:25723-25735 — <c>getTupleElementTypeOutOfStartCount</c>.</summary>
    internal IType GetTupleElementTypeOutOfStartCount(IType type, int index, IType? undefinedOrMissingType)
        => MapType(type, t =>
        {
            var tupleType = (ITypeReference)t;
            var restType = GetRestTypeOfTupleType(tupleType);
            if (restType is null)
            {
                return _undefinedType;
            }

            if (undefinedOrMissingType is not null && index >= GetTotalFixedElementCount((ITupleType)tupleType.Target!))
            {
                return GetUnionType([restType, undefinedOrMissingType]);
            }

            return restType;
        })!;

    /// <summary>ported from: src/compiler/checker.ts:25737-25740 — <c>getRestArrayTypeOfTupleType</c>.</summary>
    internal IType? GetRestArrayTypeOfTupleType(ITypeReference type)
    {
        var restType = GetRestTypeOfTupleType(type);
        return restType is not null ? CreateArrayType(restType) : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:25742-25754 — <c>getElementTypeOfSliceOfTupleType</c>.</summary>
    internal IType? GetElementTypeOfSliceOfTupleType(ITypeReference type, int index, int endSkipCount = 0, bool writing = false, bool noReductions = false)
    {
        var length = GetTypeReferenceArity(type) - endSkipCount;
        if (index < length)
        {
            var typeArguments = GetTypeArguments(type);
            var elementTypes = new List<IType>();
            for (var i = index; i < length; i++)
            {
                var t = typeArguments[i];
                elementTypes.Add((((ITupleType)type.Target!).ElementFlags![i] & ElementFlags.Variadic) != 0 ? GetIndexedAccessType(t, _numberType) : t);
            }

            return writing ? GetIntersectionType(elementTypes) : GetUnionType(elementTypes, noReductions ? UnionReduction.None : UnionReduction.Literal);
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:25756-25759 — <c>isTupleTypeStructureMatching</c>.</summary>
    internal bool IsTupleTypeStructureMatching(ITypeReference t1, ITypeReference t2)
    {
        var t1ElementFlags = ((ITupleType)t1.Target!).ElementFlags!;
        var t2ElementFlags = ((ITupleType)t2.Target!).ElementFlags!;
        return GetTypeReferenceArity(t1) == GetTypeReferenceArity(t2)
            && Core.Every(t1ElementFlags, (f, i) => (f & ElementFlags.Variable) == (t2ElementFlags[i] & ElementFlags.Variable));
    }

    /// <summary>ported from: src/compiler/checker.ts:25761-25763 — <c>isZeroBigInt</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the generated model types <c>LiteralType.Value</c> as <c>object?</c> (its TS union is
    /// <c>string | number | PseudoBigInt</c>), so the destructured <c>{ value }</c> read is the cast the model asks for.
    /// </remarks>
    internal static bool IsZeroBigInt(IBigIntLiteralType type)
        => ((IPseudoBigInt)((ILiteralType)type).Value!).Base10Value == "0";

    // ---------------------------------------------------------------------------------------------
    // Definitely-falsy types and the nullable/optional/missing markers (checker.ts:25765-25866)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:25765-25767 — <c>removeDefinitelyFalsyTypes</c>.</summary>
    internal IType RemoveDefinitelyFalsyTypes(IType type)
        => FilterType(type, t => HasTypeFacts(t, TypeFacts.Truthy));

    /// <summary>ported from: src/compiler/checker.ts:25769-25771 — <c>extractDefinitelyFalsyTypes</c>.</summary>
    internal IType ExtractDefinitelyFalsyTypes(IType type)
        => MapType(type, GetDefinitelyFalsyPartOfType)!;

    /// <summary>ported from: src/compiler/checker.ts:25773-25779 — <c>getDefinitelyFalsyPartOfType</c>.</summary>
    internal IType GetDefinitelyFalsyPartOfType(IType type)
        => (type.Flags & TypeFlags.String) != 0 ? _emptyStringType
            : (type.Flags & TypeFlags.Number) != 0 ? _zeroType
            : (type.Flags & TypeFlags.BigInt) != 0 ? _zeroBigIntType
            : ReferenceEquals(type, _regularFalseType) ||
                ReferenceEquals(type, _falseType) ||
                (type.Flags & (TypeFlags.Void | TypeFlags.Undefined | TypeFlags.Null | TypeFlags.AnyOrUnknown)) != 0 ||
                (type.Flags & TypeFlags.StringLiteral) != 0 && (string)((ILiteralType)type).Value! == "" ||
                (type.Flags & TypeFlags.NumberLiteral) != 0 && (double)((ILiteralType)type).Value! == 0 ||
                (type.Flags & TypeFlags.BigIntLiteral) != 0 && IsZeroBigInt((IBigIntLiteralType)type) ? type
            : _neverType;

    /// <summary>
    /// Add undefined or null or both to a type if they are missing.
    /// </summary>
    /// <param name="type">type to add undefined and/or null to if not present</param>
    /// <param name="flags">Either TypeFlags.Undefined or TypeFlags.Null, or both</param>
    /// <remarks>ported from: src/compiler/checker.ts:25791-25797 — <c>getNullableType</c>.</remarks>
    internal IType GetNullableType(IType type, TypeFlags flags)
    {
        var missing = (flags & ~type.Flags) & (TypeFlags.Undefined | TypeFlags.Null);
        return missing == (TypeFlags)0 ? type
            : missing == TypeFlags.Undefined ? GetUnionType([type, _undefinedType])
            : missing == TypeFlags.Null ? GetUnionType([type, _nullType])
            : GetUnionType([type, _undefinedType, _nullType]);
    }

    /// <summary>ported from: src/compiler/checker.ts:25799-25803 — <c>getOptionalType</c>.</summary>
    internal IType GetOptionalType(IType type, bool isProperty = false)
    {
        Debug.Assert(_strictNullChecks);
        var missingOrUndefined = isProperty ? _undefinedOrMissingType : _undefinedType;
        return ReferenceEquals(type, missingOrUndefined) || (type.Flags & TypeFlags.Union) != 0 && ReferenceEquals(((IUnionType)type).Types![0], missingOrUndefined)
            ? type
            : GetUnionType([type, missingOrUndefined]);
    }

    /// <summary>ported from: src/compiler/checker.ts:25805-25812 — <c>getGlobalNonNullableTypeInstantiation</c>.</summary>
    internal IType GetGlobalNonNullableTypeInstantiation(IType type)
    {
        if (_deferredGlobalNonNullableTypeAlias is null)
        {
            _deferredGlobalNonNullableTypeAlias = GetGlobalSymbol("NonNullable", SymbolFlags.TypeAlias, /*diagnostic*/ null) ?? _unknownSymbol;
        }

        return !ReferenceEquals(_deferredGlobalNonNullableTypeAlias, _unknownSymbol)
            ? GetTypeAliasInstantiation(_deferredGlobalNonNullableTypeAlias, [type])
            : GetIntersectionType([type, _emptyObjectType]);
    }

    /// <summary>ported from: src/compiler/checker.ts:25814-25816 — <c>getNonNullableType</c>.</summary>
    internal IType GetNonNullableType(IType type)
        => _strictNullChecks ? GetAdjustedTypeWithFacts(type, TypeFacts.NEUndefinedOrNull) : type;

    /// <summary>ported from: src/compiler/checker.ts:25818-25820 — <c>addOptionalTypeMarker</c>.</summary>
    internal IType AddOptionalTypeMarker(IType type)
        => _strictNullChecks ? GetUnionType([type, _optionalType]) : type;

    /// <summary>ported from: src/compiler/checker.ts:25822-25824 — <c>removeOptionalTypeMarker</c>.</summary>
    internal IType RemoveOptionalTypeMarker(IType type)
        => _strictNullChecks ? RemoveType(type, _optionalType) : type;

    /// <summary>ported from: src/compiler/checker.ts:25826-25828 — <c>propagateOptionalTypeMarker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>OptionalChain</c> is the union <c>OptionalExpression</c>; the port states the node face the
    /// two <c>Utilities</c> predicates take (<c>Node</c>).
    /// </remarks>
    internal IType PropagateOptionalTypeMarker(IType type, Node node, bool wasOptional)
        => wasOptional ? Utilities.IsOutermostOptionalChain(node) ? GetOptionalType(type) : AddOptionalTypeMarker(type) : type;

    /// <summary>ported from: src/compiler/checker.ts:25830-25834 — <c>getOptionalExpressionType</c>.</summary>
    internal IType GetOptionalExpressionType(IType exprType, Node expression)
        => Utilities.IsExpressionOfOptionalChainRoot(expression) ? GetNonNullableType(exprType)
            : Utilities.IsOptionalChain(expression) ? RemoveOptionalTypeMarker(exprType)
            : exprType;

    /// <summary>ported from: src/compiler/checker.ts:25836-25838 — <c>removeMissingType</c>.</summary>
    internal IType RemoveMissingType(IType type, bool isOptional)
        => _exactOptionalPropertyTypes == true && isOptional ? RemoveType(type, _missingType) : type;

    /// <summary>ported from: src/compiler/checker.ts:25840-25842 — <c>containsMissingType</c>.</summary>
    internal bool ContainsMissingType(IType type)
        => ReferenceEquals(type, _missingType) || (type.Flags & TypeFlags.Union) != 0 && ReferenceEquals(((IUnionType)type).Types![0], _missingType);

    /// <summary>ported from: src/compiler/checker.ts:25844-25846 — <c>removeMissingOrUndefinedType</c>.</summary>
    internal IType RemoveMissingOrUndefinedType(IType type)
        => _exactOptionalPropertyTypes == true ? RemoveType(type, _missingType) : GetTypeWithFacts(type, TypeFacts.NEUndefined);

    /// <summary>
    /// Is source potentially coercible to target type under `==`.
    /// Assumes that `source` is a constituent of a union, hence
    /// the boolean literal flag on the LHS, but not on the RHS.
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:25868-25870 — <c>isCoercibleUnderDoubleEquals</c>.</remarks>
    internal static bool IsCoercibleUnderDoubleEquals(IType source, IType target)
        => (source.Flags & (TypeFlags.Number | TypeFlags.String | TypeFlags.BooleanLiteral)) != 0
            && (target.Flags & (TypeFlags.Number | TypeFlags.String | TypeFlags.Boolean)) != 0;

    /// <summary>
    /// Return true if type was inferred from an object literal, written as an object type literal, or is the shape of a module
    /// with no call or construct signatures.
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:25877-25889 — <c>isObjectTypeWithInferableIndex</c>.</remarks>
    internal bool IsObjectTypeWithInferableIndex(IType type)
    {
        var objectFlags = GetObjectFlags(type);
        return (type.Flags & TypeFlags.Intersection) != 0
            ? Core.Every(((IIntersectionType)type).Types, (t, _) => IsObjectTypeWithInferableIndex(t))
            : type.Symbol is not null
                && (type.Symbol.Flags & (SymbolFlags.ObjectLiteral | SymbolFlags.TypeLiteral | SymbolFlags.Enum | SymbolFlags.ValueModule)) != 0
                && (type.Symbol.Flags & SymbolFlags.Class) == 0
                && !TypeHasCallOrConstructSignatures(type)
            || (objectFlags & ObjectFlags.ObjectRestType) != 0
            || (objectFlags & ObjectFlags.ReverseMapped) != 0 && IsObjectTypeWithInferableIndex(((IReverseMappedType)type).Source!);
    }
    // ---------------------------------------------------------------------------------------------
    // Object-literal widening (checker.ts:25891-26064)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:25891-25905 — <c>createSymbolWithType</c>.</summary>
    internal ISymbol CreateSymbolWithType(ISymbol source, IType? type)
    {
        var symbol = CreateSymbol(source.Flags, source.EscapedName!, GetCheckFlags(source) & CheckFlags.Readonly);
        symbol.Declarations = source.Declarations;
        symbol.Parent = source.Parent;
        symbol.Links!.Type = type;
        symbol.Links.Target = source;
        if (source.ValueDeclaration is not null)
        {
            symbol.ValueDeclaration = source.ValueDeclaration;
        }

        var nameType = GetSymbolLinks(source).NameType;
        if (nameType is not null)
        {
            symbol.Links.NameType = nameType;
        }

        return symbol;
    }

    /// <summary>ported from: src/compiler/checker.ts:25907-25915 — <c>transformTypeOfMembers</c>.</summary>
    internal SymbolTable TransformTypeOfMembers(IType type, Func<IType, IType> f)
    {
        var members = Utilities.CreateSymbolTable();
        foreach (var property in GetPropertiesOfObjectType(type))
        {
            var original = GetTypeOfSymbol(property);
            var updated = f(original);
            members[property.EscapedName ?? ""] = ReferenceEquals(updated, original) ? property : CreateSymbolWithType(property, updated);
        }

        return members;
    }

    /// <summary>
    /// If the the provided object literal is subject to the excess properties check,
    /// create a new that is exempt. Recursively mark object literal members as exempt.
    /// Leave signatures alone since they are not subject to the check.
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:25922-25938 — <c>getRegularTypeOfObjectLiteral</c>.</remarks>
    internal IType GetRegularTypeOfObjectLiteral(IType type)
    {
        if (!(IsObjectLiteralType(type) && (GetObjectFlags(type) & ObjectFlags.FreshLiteral) != 0))
        {
            return type;
        }

        var regularType = ((IFreshObjectLiteralType)type).RegularType;
        if (regularType is not null)
        {
            return regularType;
        }

        var resolved = (IResolvedType)type;
        var members = TransformTypeOfMembers(type, GetRegularTypeOfObjectLiteral);
        var regularNew = CreateAnonymousType(resolved.Symbol, members, resolved.CallSignatures ?? [], resolved.ConstructSignatures ?? [], resolved.IndexInfos ?? []);
        regularNew.Flags = resolved.Flags;
        ((IObjectType)regularNew).ObjectFlags |= ((IObjectType)resolved).ObjectFlags & ~ObjectFlags.FreshLiteral;
        ((IFreshObjectLiteralType)type).RegularType = regularNew;
        return regularNew;
    }

    /// <summary>ported from: src/compiler/checker.ts:25940-25942 — <c>createWideningContext</c>.</summary>
    internal IWideningContext CreateWideningContext(IWideningContext? parent, string? propertyName, List<IType>? siblings)
        => new WideningContext { Parent = parent, PropertyName = propertyName, Siblings = siblings, ResolvedProperties = null };

    /// <summary>ported from: src/compiler/checker.ts:25944-25960 — <c>getSiblingsOfContext</c>.</summary>
    internal List<IType> GetSiblingsOfContext(IWideningContext context)
    {
        if (context.Siblings is null)
        {
            var siblings = new List<IType>();
            foreach (var type in GetSiblingsOfContext(context.Parent!))
            {
                if (IsObjectLiteralType(type))
                {
                    var prop = GetPropertyOfObjectType(type, context.PropertyName!);
                    if (prop is not null)
                    {
                        // PORT-NOTE: the port's ForEachType<TU> needs a callback that produces a value where the
                        // reference's forEachType<T> accepted a void arrow; the returned value is ignored here.
                        ForEachType(GetTypeOfSymbol(prop), t => { siblings.Add(t); return t; });
                    }
                }
            }

            context.Siblings = siblings;
        }

        return context.Siblings;
    }

    /// <summary>ported from: src/compiler/checker.ts:25962-25975 — <c>getPropertiesOfContext</c>.</summary>
    internal List<ISymbol> GetPropertiesOfContext(IWideningContext context)
    {
        if (context.ResolvedProperties is null)
        {
            var names = Utilities.CreateSymbolTable();
            foreach (var t in GetSiblingsOfContext(context))
            {
                if (IsObjectLiteralType(t) && (GetObjectFlags(t) & ObjectFlags.ContainsSpread) == 0)
                {
                    foreach (var prop in GetPropertiesOfType(t))
                    {
                        names[prop.EscapedName ?? ""] = prop;
                    }
                }
            }

            context.ResolvedProperties = [.. Core.ArrayFrom(names.Values)];
        }

        return context.ResolvedProperties;
    }

    /// <summary>ported from: src/compiler/checker.ts:25977-25987 — <c>getWidenedProperty</c>.</summary>
    internal ISymbol GetWidenedProperty(ISymbol prop, IWideningContext? context)
    {
        if ((prop.Flags & SymbolFlags.Property) == 0)
        {
            // Since get accessors already widen their return value there is no need to
            // widen accessor based properties here.
            return prop;
        }

        var original = GetTypeOfSymbol(prop);
        var propContext = context is not null ? CreateWideningContext(context, prop.EscapedName, /*siblings*/ null) : null;
        var widened = GetWidenedTypeWithContext(original, propContext);
        return ReferenceEquals(widened, original) ? prop : CreateSymbolWithType(prop, widened);
    }

    /// <summary>ported from: src/compiler/checker.ts:25989-25998 — <c>getUndefinedProperty</c>.</summary>
    internal ISymbol GetUndefinedProperty(ISymbol prop)
    {
        var cached = _undefinedProperties.GetValueOrDefault(prop.EscapedName ?? "");
        if (cached is not null)
        {
            return cached;
        }

        var result = CreateSymbolWithType(prop, _undefinedOrMissingType);
        result.Flags |= SymbolFlags.Optional;
        _undefinedProperties[prop.EscapedName ?? ""] = result;
        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:26000-26015 — <c>getWidenedTypeOfObjectLiteral</c>.</summary>
    internal IType GetWidenedTypeOfObjectLiteral(IType type, IWideningContext? context)
    {
        var members = Utilities.CreateSymbolTable();
        foreach (var prop in GetPropertiesOfObjectType(type))
        {
            members[prop.EscapedName ?? ""] = GetWidenedProperty(prop, context);
        }

        if (context is not null)
        {
            foreach (var prop in GetPropertiesOfContext(context))
            {
                if (!members.ContainsKey(prop.EscapedName ?? ""))
                {
                    members[prop.EscapedName ?? ""] = GetUndefinedProperty(prop);
                }
            }
        }

        var result = CreateAnonymousType(type.Symbol, members, [], [], Core.SameMap(GetIndexInfosOfType(type), (info, _) => CreateIndexInfo(info.KeyType!, GetWidenedType(info.Type!), info.IsReadonly, info.Declaration, info.Components))!);
        result.ObjectFlags |= GetObjectFlags(type) & (ObjectFlags.JSLiteral | ObjectFlags.NonInferrableType); // Retain js literal flag through widening
        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:26017-26019 — <c>getWidenedType</c>.</summary>
    internal IType GetWidenedType(IType type)
        => GetWidenedTypeWithContext(type, /*context*/ null);

    /// <summary>ported from: src/compiler/checker.ts:26021-26063 — <c>getWidenedTypeWithContext</c>.</summary>
    internal IType GetWidenedTypeWithContext(IType type, IWideningContext? context)
    {
        if ((GetObjectFlags(type) & ObjectFlags.RequiresWidening) != 0)
        {
            if (context is null && type.Widened is not null)
            {
                return type.Widened;
            }

            IType? result = null;
            if ((type.Flags & (TypeFlags.Any | TypeFlags.Nullable)) != 0)
            {
                result = _anyType;
            }
            else if (IsObjectLiteralType(type))
            {
                result = GetWidenedTypeOfObjectLiteral(type, context);
            }
            else if ((type.Flags & TypeFlags.Union) != 0)
            {
                var unionContext = context ?? CreateWideningContext(/*parent*/ null, /*propertyName*/ null, ((IUnionType)type).Types);
                var widenedTypes = Core.SameMap(((IUnionType)type).Types, (t, _) => (t.Flags & TypeFlags.Nullable) != 0 ? t : GetWidenedTypeWithContext(t, unionContext))!;
                // Widening an empty object literal transitions from a highly restrictive type to
                // a highly inclusive one. For that reason we perform subtype reduction here if the
                // union includes empty object types (e.g. reducing {} | string to just {}).
                result = GetUnionType([.. widenedTypes], Core.Some(widenedTypes, IsEmptyObjectType) ? UnionReduction.Subtype : UnionReduction.Literal);
            }
            else if ((type.Flags & TypeFlags.Intersection) != 0)
            {
                result = GetIntersectionType([.. Core.SameMap(((IIntersectionType)type).Types, (t, _) => GetWidenedType(t))!]);
            }
            else if (IsArrayOrTupleType(type))
            {
                result = CreateTypeReference(((ITypeReference)type).Target!, (List<IType>)Core.SameMap(GetTypeArguments((ITypeReference)type), (t, _) => GetWidenedType(t))!);
            }

            if (result is not null && context is null)
            {
                type.Widened = result;
            }

            return result ?? type;
        }

        return type;
    }

    /// <summary>
    /// Reports implicit any errors that occur as a result of widening 'null' and 'undefined'
    /// to 'any'. A call to reportWideningErrorsInType is normally accompanied by a call to
    /// getWidenedType. But in some cases getWidenedType is called without reporting errors
    /// (type argument inference is an example).
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:26066-26102 — <c>reportWideningErrorsInType</c>.</remarks>
    /// <remarks>
    /// PORT-NOTE: TS reads <c>d.symbol</c> off a declaration node; the port's <c>Node</c> carries no symbol slot, so
    /// the same read is <c>GetSymbolOfDeclaration(d)</c> (checker.ts:5930's <c>getSymbolOfNode</c>).
    /// </remarks>
    internal bool ReportWideningErrorsInType(IType type)
    {
        var errorReported = false;
        if ((GetObjectFlags(type) & ObjectFlags.ContainsWideningType) != 0)
        {
            if ((type.Flags & TypeFlags.Union) != 0)
            {
                if (Core.Some(((IUnionType)type).Types, IsEmptyObjectType))
                {
                    errorReported = true;
                }
                else
                {
                    foreach (var t in ((IUnionType)type).Types!)
                    {
                        errorReported = errorReported || ReportWideningErrorsInType(t);
                    }
                }
            }
            else if (IsArrayOrTupleType(type))
            {
                foreach (var t in GetTypeArguments((ITypeReference)type))
                {
                    errorReported = errorReported || ReportWideningErrorsInType(t);
                }
            }
            else if (IsObjectLiteralType(type))
            {
                foreach (var p in GetPropertiesOfObjectType(type))
                {
                    var t = GetTypeOfSymbol(p);
                    if ((GetObjectFlags(t) & ObjectFlags.ContainsWideningType) != 0)
                    {
                        errorReported = ReportWideningErrorsInType(t);
                        if (!errorReported)
                        {
                            // we need to account for property types coming from object literal type normalization in unions
                            // PORT-NOTE: TS's `declarations?.find(…)` is a Core.Find call upstream; Core.Find is constrained to
                            // class element types, and a declaration is an interface here, so the scan is written out.
                            IDeclaration? valueDeclaration = null;
                            foreach (var d in p.Declarations ?? [])
                            {
                                if (ReferenceEquals(((Node?)GetSymbolOfDeclaration(d).ValueDeclaration)?.Parent, type.Symbol!.ValueDeclaration))
                                {
                                    valueDeclaration = d;
                                    break;
                                }
                            }

                            if (valueDeclaration is not null)
                            {
                                // PORT-NOTE (A″): printer-gated stringifier arguments are deferred — see Core/Diagnostic.cs's `DiagnosticArgument`.
                                Error((Node)valueDeclaration, Diagnostics.Object_literal_s_property_0_implicitly_has_an_1_type, Diagnostic.DeferArg(() => SymbolToString(p)), Diagnostic.DeferArg(() => TypeToString(GetWidenedType(t))));
                                errorReported = true;
                            }
                        }
                    }
                }
            }
        }

        return errorReported;
    }

    /// <summary>ported from: src/compiler/checker.ts:26104-26181 — <c>reportImplicitAny</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>Declaration</c> face is the port's <c>IDeclaration</c> (an interface), and every
    /// place the reference hands it to a <c>Node</c> parameter the port states the cast explicitly (C# has no
    /// structural typing).
    /// </remarks>
    internal void ReportImplicitAny(IDeclaration declaration, IType type, WideningKind? wideningKind = null)
    {
        var typeAsString = Diagnostic.DeferArg(() => TypeToString(GetWidenedType(type)));
        if (Utilities.IsInJSFile((Node)declaration) && Utilities.IsCheckJsEnabledForFile(Utilities.GetSourceFileOfNode((Node)declaration)!, _compilerOptions) != true)
        {
            // Only report implicit any errors/suggestions in TS and ts-check JS files
            return;
        }

        DiagnosticMessage diagnostic;
        switch (declaration.Kind)
        {
            case SyntaxKind.BinaryExpression:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.PropertySignature:
                diagnostic = _noImplicitAny ? Diagnostics.Member_0_implicitly_has_an_1_type : Diagnostics.Member_0_implicitly_has_an_1_type_but_a_better_type_may_be_inferred_from_usage;
                break;
            case SyntaxKind.Parameter:
                {
                    var param = (ParameterDeclaration)declaration;
                    if (NodeTests.IsIdentifier(param.Name!))
                    {
                        var originalKeywordKind = Utilities.IdentifierToKeywordKind((Identifier)param.Name!);
                        var parameterOwner = (ISignatureDeclarationBase)param.Parent!;
                        if ((NodeTests.IsCallSignatureDeclaration(param.Parent!) || NodeTests.IsMethodSignature(param.Parent!) || NodeTests.IsFunctionTypeNode(param.Parent!))
                            && parameterOwner.Parameters!.Contains(param)
                            && (ResolveName(param, ((Identifier)param.Name!).EscapedText!, SymbolFlags.Type, /*nameNotFoundMessage*/ null, /*isUse*/ true, /*excludeGlobals*/ false) is not null
                                || originalKeywordKind is not null && Utilities.IsTypeNodeKind(originalKeywordKind.Value)))
                        {
                            var newName = "arg" + parameterOwner.Parameters!.IndexOf(param);
                            var typeName = Utilities.DeclarationNameToString(param.Name) + (param.DotDotDotToken is not null ? "[]" : "");
                            ErrorOrSuggestion(_noImplicitAny, (Node)declaration, Diagnostics.Parameter_has_a_name_but_no_type_Did_you_mean_0_Colon_1, newName, typeName);
                            return;
                        }
                    }

                    diagnostic = param.DotDotDotToken is not null
                        ? _noImplicitAny ? Diagnostics.Rest_parameter_0_implicitly_has_an_any_type : Diagnostics.Rest_parameter_0_implicitly_has_an_any_type_but_a_better_type_may_be_inferred_from_usage
                        : _noImplicitAny ? Diagnostics.Parameter_0_implicitly_has_an_1_type : Diagnostics.Parameter_0_implicitly_has_an_1_type_but_a_better_type_may_be_inferred_from_usage;
                    break;
                }

            case SyntaxKind.BindingElement:
                diagnostic = Diagnostics.Binding_element_0_implicitly_has_an_1_type;
                if (!_noImplicitAny)
                {
                    // Don't issue a suggestion for binding elements since the codefix doesn't yet support them.
                    return;
                }

                break;
            case SyntaxKind.JSDocFunctionType:
                Error((Node)declaration, Diagnostics.Function_type_which_lacks_return_type_annotation_implicitly_has_an_0_return_type, typeAsString);
                return;
            case SyntaxKind.JSDocSignature:
                if (_noImplicitAny && NodeTests.IsJSDocOverloadTag(((Node)declaration).Parent!))
                {
                    Error(((JSDocTag)((Node)declaration).Parent!).TagName, Diagnostics.This_overload_implicitly_returns_the_type_0_because_it_lacks_a_return_type_annotation, typeAsString);
                }

                return;
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.MethodSignature:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ArrowFunction:
                if (_noImplicitAny && ((INamedDeclaration)declaration).Name is null)
                {
                    if (wideningKind == WideningKind.GeneratorYield)
                    {
                        Error((Node)declaration, Diagnostics.Generator_implicitly_has_yield_type_0_Consider_supplying_a_return_type_annotation, typeAsString);
                    }
                    else
                    {
                        Error((Node)declaration, Diagnostics.Function_expression_which_lacks_return_type_annotation_implicitly_has_an_0_return_type, typeAsString);
                    }

                    return;
                }

                diagnostic = !_noImplicitAny ? Diagnostics._0_implicitly_has_an_1_return_type_but_a_better_type_may_be_inferred_from_usage
                    : wideningKind == WideningKind.GeneratorYield ? Diagnostics._0_which_lacks_return_type_annotation_implicitly_has_an_1_yield_type
                    : Diagnostics._0_which_lacks_return_type_annotation_implicitly_has_an_1_return_type;
                break;
            case SyntaxKind.MappedType:
                if (_noImplicitAny)
                {
                    Error((Node)declaration, Diagnostics.Mapped_object_type_implicitly_has_an_any_template_type);
                }

                return;
            default:
                diagnostic = _noImplicitAny ? Diagnostics.Variable_0_implicitly_has_an_1_type : Diagnostics.Variable_0_implicitly_has_an_1_type_but_a_better_type_may_be_inferred_from_usage;
                break;
        }

        ErrorOrSuggestion(_noImplicitAny, (Node)declaration, diagnostic, Utilities.DeclarationNameToString(Utilities.GetNameOfDeclaration((Node)declaration) as Node), typeAsString);
    }

    /// <summary>ported from: src/compiler/checker.ts:26183-26207 — <c>shouldReportErrorsFromWideningWithContextualSignature</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>FunctionLikeDeclaration</c> is a union of node faces with no single port interface; the
    /// parameter takes the <c>Node</c> face the two callees (<c>GetContextualSignatureForFunctionLikeDeclaration</c>,
    /// <c>GetFunctionFlags</c>) already take.
    /// </remarks>
    internal bool ShouldReportErrorsFromWideningWithContextualSignature(Node declaration, WideningKind wideningKind)
    {
        var signature = GetContextualSignatureForFunctionLikeDeclaration(declaration);
        if (signature is null)
        {
            return true;
        }

        var returnType = GetReturnTypeOfSignature(signature);
        var flags = Utilities.GetFunctionFlags(declaration);
        switch (wideningKind)
        {
            case WideningKind.FunctionReturn:
                if ((flags & FunctionFlags.Generator) != 0)
                {
                    returnType = GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Return, returnType, (flags & FunctionFlags.Async) != 0) ?? returnType;
                }
                else if ((flags & FunctionFlags.Async) != 0)
                {
                    returnType = GetAwaitedTypeNoAlias(returnType) ?? returnType;
                }

                return IsGenericType(returnType);
            case WideningKind.GeneratorYield:
                var yieldType = GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Yield, returnType, (flags & FunctionFlags.Async) != 0);
                return yieldType is not null && IsGenericType(yieldType);
            case WideningKind.GeneratorNext:
                var nextType = GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Next, returnType, (flags & FunctionFlags.Async) != 0);
                return nextType is not null && IsGenericType(nextType);
            default:
                return false;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:26209-26220 — <c>reportErrorsFromWidening</c>.</summary>
    internal void ReportErrorsFromWidening(IDeclaration declaration, IType type, WideningKind? wideningKind = null)
        => AddLazyDiagnostic(() =>
        {
            if (_noImplicitAny && (GetObjectFlags(type) & ObjectFlags.ContainsWideningType) != 0)
            {
                if (wideningKind is null || Utilities.IsFunctionLikeDeclaration((Node)declaration) && ShouldReportErrorsFromWideningWithContextualSignature((Node)declaration, wideningKind.Value))
                {
                    // Report implicit any error within type if possible, otherwise report error on declaration
                    if (!ReportWideningErrorsInType(type))
                    {
                        ReportImplicitAny(declaration, type, wideningKind);
                    }
                }
            }
        });

    /// <summary>ported from: src/compiler/checker.ts:26222-26242 — <c>applyToParameterTypes</c>.</summary>
    internal void ApplyToParameterTypes(ISignature source, ISignature target, Action<IType, IType> callback)
    {
        var sourceCount = GetParameterCount(source);
        var targetCount = GetParameterCount(target);
        var sourceRestType = GetEffectiveRestType(source);
        var targetRestType = GetEffectiveRestType(target);
        var targetNonRestCount = targetRestType is not null ? targetCount - 1 : targetCount;
        var paramCount = sourceRestType is not null ? targetNonRestCount : Math.Min(sourceCount, targetNonRestCount);
        var sourceThisType = GetThisTypeOfSignature(source);
        if (sourceThisType is not null)
        {
            var targetThisType = GetThisTypeOfSignature(target);
            if (targetThisType is not null)
            {
                callback(sourceThisType, targetThisType);
            }
        }

        for (var i = 0; i < paramCount; i++)
        {
            callback(GetTypeAtPosition(source, i), GetTypeAtPosition(target, i));
        }

        if (targetRestType is not null)
        {
            callback(GetRestTypeAtPosition(source, paramCount, IsConstTypeVariable(targetRestType) && !SomeType(targetRestType, IsMutableArrayLikeType)), targetRestType);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:26244-26257 — <c>applyToReturnTypes</c>.</summary>
    internal void ApplyToReturnTypes(ISignature source, ISignature target, Action<IType, IType> callback)
    {
        var targetTypePredicate = GetTypePredicateOfSignature(target);
        if (targetTypePredicate is not null)
        {
            var sourceTypePredicate = GetTypePredicateOfSignature(source);
            if (sourceTypePredicate is not null && TypePredicateKindsMatch(sourceTypePredicate, targetTypePredicate) && sourceTypePredicate.Type is not null && targetTypePredicate.Type is not null)
            {
                callback(sourceTypePredicate.Type, targetTypePredicate.Type);
                return;
            }
        }

        var targetReturnType = GetReturnTypeOfSignature(target);
        if (CouldContainTypeVariables(targetReturnType))
        {
            callback(GetReturnTypeOfSignature(source), targetReturnType);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The inference context (checker.ts:26259-26424)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:26259-26261 — <c>createInferenceContext</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's optional <c>compareTypes: TypeComparer</c> becomes the nullable
    /// <c>Func&lt;IType, IType, bool, Ternary&gt;</c> slot the generated <c>IInferenceContext.CompareTypes</c> declares
    /// (the same signature as <c>TypeComparer</c>, so a TypeComparer method group converts).
    /// </remarks>
    internal IInferenceContext CreateInferenceContext(List<ITypeParameter> typeParameters, ISignature? signature, InferenceFlags flags, Func<IType, IType, bool, Ternary>? compareTypes = null)
        => CreateInferenceContextWorker([.. Core.Map(typeParameters, (tp, _) => CreateInferenceInfo(tp))!], signature, flags, compareTypes ?? CompareTypesAssignable);

    /// <summary>ported from: src/compiler/checker.ts:26263-26265 — <c>cloneInferenceContext</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's conditional return type <c>InferenceContext | T &amp; undefined</c> says "undefined exactly when
    /// the context is undefined" (<c>T &amp; undefined</c> collapses for a non-optional <c>T</c>, which is why
    /// <c>createOuterReturnMapper</c>, checker.ts:20647, reads the result as non-null). C# has no conditional return
    /// type, so the port states both arms: the parameter is <c>IInferenceContext?</c>, like the reference's, and the
    /// result keeps the reference's contract for the compiler through <see cref="NotNullIfNotNullAttribute"/> — the
    /// non-null call sites still read their argument's arm, while <c>inferTypeArguments</c> (checker.ts:35858 in
    /// <c>Checker.CallResolution.cs</c>, whose <c>outerContext</c> is the possibly-undefined inference context of the
    /// node) gets the reference's undefined back instead of the dereference that used to throw.
    /// </remarks>
    [return: NotNullIfNotNull(nameof(context))]
    internal IInferenceContext? CloneInferenceContext(IInferenceContext? context, InferenceFlags extraFlags = InferenceFlags.None)
        => context is null ? null : CreateInferenceContextWorker([.. Core.Map(context.Inferences!, (inference, _) => CloneInferenceInfo(inference))!], context.Signature, context.Flags | extraFlags, context.CompareTypes!);

    /// <summary>ported from: src/compiler/checker.ts:26267-26279 — <c>createInferenceContextWorker</c>.</summary>
    internal IInferenceContext CreateInferenceContextWorker(List<IInferenceInfo> inferences, ISignature? signature, InferenceFlags flags, Func<IType, IType, bool, Ternary> compareTypes)
    {
        var context = new InferenceContext
        {
            Inferences = inferences,
            Signature = signature,
            Flags = flags,
            CompareTypes = compareTypes,
            Mapper = ReportUnmeasurableMapper, // initialize to a noop mapper so the context object is available, but the underlying object shape is right upon construction
            NonFixingMapper = ReportUnmeasurableMapper,
        };
        context.Mapper = MakeFixingMapperForContext(context);
        context.NonFixingMapper = MakeNonFixingMapperForContext(context);
        return context;
    }

    /// <summary>ported from: src/compiler/checker.ts:26281-26295 — <c>makeFixingMapperForContext</c>.</summary>
    internal TypeMapper MakeFixingMapperForContext(IInferenceContext context)
        => MakeDeferredTypeMapper(
            Core.Map(context.Inferences, (i, _) => i.TypeParameter!)!,
            [.. Core.Map(context.Inferences, (inference, i) => new Func<IType>(() =>
            {
                if (!inference.IsFixed)
                {
                    // Before we commit to a particular inference (and thus lock out any further inferences),
                    // we infer from any intra-expression inference sites we have collected.
                    InferFromIntraExpressionSites(context);
                    ClearCachedInferences(context.Inferences!);
                    inference.IsFixed = true;
                }

                return GetInferredType(context, i);
            }))!]);

    /// <summary>ported from: src/compiler/checker.ts:26297-26304 — <c>makeNonFixingMapperForContext</c>.</summary>
    internal TypeMapper MakeNonFixingMapperForContext(IInferenceContext context)
        => MakeDeferredTypeMapper(
            Core.Map(context.Inferences, (i, _) => i.TypeParameter!)!,
            [.. Core.Map(context.Inferences, (_, i) => new Func<IType>(() => GetInferredType(context, i)))!]);

    /// <summary>ported from: src/compiler/checker.ts:26306-26312 — <c>clearCachedInferences</c>.</summary>
    internal static void ClearCachedInferences(List<IInferenceInfo> inferences)
    {
        foreach (var inference in inferences)
        {
            if (!inference.IsFixed)
            {
                inference.InferredType = null;
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:26314-26316 — <c>addIntraExpressionInferenceSite</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>Expression | MethodDeclaration</c> union has no port interface; the parameter takes the
    /// <c>Node</c> face of the generated <c>IIntraExpressionInferenceSite.Node</c> slot it is stored into.
    /// </remarks>
    internal static void AddIntraExpressionInferenceSite(IInferenceContext context, Node node, IType type)
        => (context.IntraExpressionInferenceSites ??= []).Add(new IntraExpressionInferenceSite { Node = node, Type = type });

    /// <summary>ported from: src/compiler/checker.ts:26331-26343 — <c>inferFromIntraExpressionSites</c>.</summary>
    internal void InferFromIntraExpressionSites(IInferenceContext context)
    {
        if (context.IntraExpressionInferenceSites is not null)
        {
            foreach (var site in context.IntraExpressionInferenceSites)
            {
                var contextualType = site.Node!.Kind == SyntaxKind.MethodDeclaration
                    ? GetContextualTypeForObjectLiteralMethod((MethodDeclaration)site.Node!, ContextFlags.NoConstraints)
                    : GetContextualType((Expression)site.Node!, ContextFlags.NoConstraints);
                if (contextualType is not null)
                {
                    InferTypes(context.Inferences!, site.Type!, contextualType);
                }
            }

            context.IntraExpressionInferenceSites = null;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:26345-26356 — <c>createInferenceInfo</c>.</summary>
    internal static IInferenceInfo CreateInferenceInfo(ITypeParameter typeParameter)
        => new InferenceInfo
        {
            TypeParameter = typeParameter,
            Candidates = null,
            ContraCandidates = null,
            InferredType = null,
            Priority = null,
            TopLevel = true,
            IsFixed = false,
            ImpliedArity = null,
        };

    /// <summary>ported from: src/compiler/checker.ts:26358-26369 — <c>cloneInferenceInfo</c>.</summary>
    internal static IInferenceInfo CloneInferenceInfo(IInferenceInfo inference)
        => new InferenceInfo
        {
            TypeParameter = inference.TypeParameter,
            Candidates = inference.Candidates is not null ? [.. inference.Candidates] : null,
            ContraCandidates = inference.ContraCandidates is not null ? [.. inference.ContraCandidates] : null,
            InferredType = inference.InferredType,
            Priority = inference.Priority,
            TopLevel = inference.TopLevel,
            IsFixed = inference.IsFixed,
            ImpliedArity = inference.ImpliedArity,
        };

    /// <summary>ported from: src/compiler/checker.ts:26371-26376 — <c>cloneInferredPartOfContext</c>.</summary>
    internal IInferenceContext? CloneInferredPartOfContext(IInferenceContext context)
    {
        var inferences = Core.Filter(context.Inferences, HasInferenceCandidates)!;
        return inferences.Count != 0
            ? CreateInferenceContextWorker([.. Core.Map(inferences, (inference, _) => CloneInferenceInfo(inference))!], context.Signature, context.Flags, context.CompareTypes!)
            : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:26378-26380 — <c>getMapperFromContext</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>&lt;T extends InferenceContext | undefined&gt;</c> answers the context's <c>mapper</c> slot for a
    /// context and <c>undefined</c> for an undefined one — the same two arms <see cref="CloneInferenceContext"/> now
    /// states, which is what lets their nesting at checker.ts:35858 answer <c>undefined</c> when the node has no
    /// inference context. The port returns the slot as the <c>TypeMapper</c> face the reference gives it.
    /// </remarks>
    internal static TypeMapper? GetMapperFromContext(IInferenceContext? context)
        => context is not null ? (TypeMapper?)context.Mapper : null;

    /// <summary>ported from: src/compiler/checker.ts:26411-26418 — <c>isTypeParameterAtTopLevel</c>.</summary>
    internal bool IsTypeParameterAtTopLevel(IType type, ITypeParameter tp, int depth = 0)
        => ReferenceEquals(type, tp)
            || (type.Flags & TypeFlags.UnionOrIntersection) != 0 && Core.Some(((IUnionOrIntersectionType)type).Types, t => IsTypeParameterAtTopLevel(t, tp, depth))
            || depth < 3 && (type.Flags & TypeFlags.Conditional) != 0 && (
                IsTypeParameterAtTopLevel(GetTrueTypeFromConditionalType((IConditionalType)type), tp, depth + 1)
                || IsTypeParameterAtTopLevel(GetFalseTypeFromConditionalType((IConditionalType)type), tp, depth + 1));

    /// <summary>ported from: src/compiler/checker.ts:26420-26425 — <c>isTypeParameterAtTopLevelInReturnType</c>.</summary>
    internal bool IsTypeParameterAtTopLevelInReturnType(ISignature signature, ITypeParameter typeParameter)
    {
        var typePredicate = GetTypePredicateOfSignature(signature);
        return typePredicate is not null
            ? typePredicate.Type is not null && IsTypeParameterAtTopLevel(typePredicate.Type, typeParameter)
            : IsTypeParameterAtTopLevel(GetReturnTypeOfSignature(signature), typeParameter);
    }

    // ---------------------------------------------------------------------------------------------
    // The state the widening and inference contexts hang off (checker.ts:2295-2300)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The reference's data-only interfaces (<c>WideningContext</c>, <c>InferenceContext</c>,
    /// <c>InferenceInfo</c>, <c>IntraExpressionInferenceSite</c>) are object literals in TS; C# needs a class.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: these four classes exist only to instantiate the generated data interfaces; every consumer in the
    /// port reads the interface face, so nesting them inside the <c>Checker</c> partial keeps their names from leaking
    /// into the namespace.
    /// </remarks>
    internal sealed class WideningContext : IWideningContext
    {
        public IWideningContext? Parent { get; set; }

        public string? PropertyName { get; set; }

        public List<IType>? Siblings { get; set; }

        public List<ISymbol>? ResolvedProperties { get; set; }
    }

    /// <summary>ported from: src/compiler/types.ts:7118-7130 — <c>InferenceContext</c> (the object literal TS builds in <c>createInferenceContextWorker</c>).</summary>
    internal sealed class InferenceContext : IInferenceContext
    {
        public List<IInferenceInfo>? Inferences { get; set; }

        public ISignature? Signature { get; set; }

        public InferenceFlags Flags { get; set; }

        public Func<IType, IType, bool, Ternary>? CompareTypes { get; set; }

        public object? Mapper { get; set; }

        public object? NonFixingMapper { get; set; }

        public object? ReturnMapper { get; set; }

        public object? OuterReturnMapper { get; set; }

        public List<ITypeParameter>? InferredTypeParameters { get; set; }

        public List<IIntraExpressionInferenceSite>? IntraExpressionInferenceSites { get; set; }
    }

    /// <summary>ported from: src/compiler/types.ts:7132-7142 — <c>InferenceInfo</c>.</summary>
    internal sealed class InferenceInfo : IInferenceInfo
    {
        public ITypeParameter? TypeParameter { get; set; }

        public List<IType>? Candidates { get; set; }

        public List<IType>? ContraCandidates { get; set; }

        public IType? InferredType { get; set; }

        public InferencePriority? Priority { get; set; }

        public bool TopLevel { get; set; }

        public bool IsFixed { get; set; }

        public int? ImpliedArity { get; set; }
    }

    /// <summary>ported from: src/compiler/types.ts:7144-7147 — <c>IntraExpressionInferenceSite</c>.</summary>
    internal sealed class IntraExpressionInferenceSite : IIntraExpressionInferenceSite
    {
        public Node? Node { get; set; }

        public IType? Type { get; set; }
    }
    /// <summary>ported from: src/compiler/checker.ts:2246 — <c>reverseHomomorphicMappedCache</c>.</summary>
    internal Dictionary<string, IType?> _reverseHomomorphicMappedCache = null!;

    // ---------------------------------------------------------------------------------------------
    // Reverse-mapped inference (checker.ts:26427-26565)
    // ---------------------------------------------------------------------------------------------

    /// <summary>Create an object with properties named in the string literal type. Every property has type `any`.</summary>
    /// <remarks>ported from: src/compiler/checker.ts:26427-26450 — <c>createEmptyObjectTypeFromStringLiteral</c>.</remarks>
    internal IType CreateEmptyObjectTypeFromStringLiteral(IType type)
    {
        var members = Utilities.CreateSymbolTable();
        // PORT-NOTE: the port's ForEachType<TU> needs a callback that produces a value where the reference's
        // forEachType<T> accepted a void arrow; the returned symbolic value is ignored here.
        ForEachType(type, t =>
        {
            if ((t.Flags & TypeFlags.StringLiteral) == 0)
            {
                return (ISymbol?)null;
            }

            var name = Utilities.EscapeLeadingUnderscores((string)((ILiteralType)t).Value!);
            var literalProp = CreateSymbol(SymbolFlags.Property, name);
            literalProp.Links!.Type = _anyType;
            if (t.Symbol is not null)
            {
                literalProp.Declarations = t.Symbol.Declarations;
                literalProp.ValueDeclaration = t.Symbol.ValueDeclaration;
            }

            members[name] = literalProp;
            return literalProp;
        });
        var indexInfos = (type.Flags & TypeFlags.String) != 0
            ? new List<IIndexInfo> { CreateIndexInfo(_stringType, _emptyObjectType, /*isReadonly*/ false) }
            : [];
        return CreateAnonymousType(/*symbol*/ null, members, [], [], indexInfos);
    }

    /// <summary>
    /// Infer a suitable input type for a homomorphic mapped type { [P in keyof T]: X }. We construct
    /// an object type with the same set of properties as the source type, where the type of each
    /// property is computed by inferring from the source property type to X for the type
    /// variable T[P] (i.e. we treat the type T[P] as the type variable we're inferring for).
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:26452-26464 — <c>inferTypeForHomomorphicMappedType</c>.</remarks>
    internal IType? InferTypeForHomomorphicMappedType(IType source, IMappedType target, IIndexType constraint)
    {
        var cacheKey = source.Id + "," + target.Id + "," + constraint.Id;
        if (_reverseHomomorphicMappedCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var type = CreateReverseMappedType(source, target, constraint);
        _reverseHomomorphicMappedCache[cacheKey] = type;
        return type;
    }

    /// <summary>
    /// We consider a type to be partially inferable if it isn't marked non-inferable or if it is
    /// an object literal type with at least one property of an inferable type. For example, an object
    /// literal { a: 123, b: x => true } is marked non-inferable because it contains a context sensitive
    /// arrow function, but is considered partially inferable because property 'a' has an inferable type.
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:26466-26470 — <c>isPartiallyInferableType</c>.</remarks>
    internal bool IsPartiallyInferableType(IType type)
        => (GetObjectFlags(type) & ObjectFlags.NonInferrableType) == 0
            || IsObjectLiteralType(type) && Core.Some(GetPropertiesOfType(type), prop => IsPartiallyInferableType(GetTypeOfSymbol(prop)))
            || IsTupleType(type) && Core.Some(GetElementTypes((ITupleTypeReference)type), IsPartiallyInferableType);

    /// <summary>ported from: src/compiler/checker.ts:26472-26504 — <c>createReverseMappedType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS builds the element list with <c>map</c> and then tests <c>every(elementTypes, t =&gt; !!t)</c>;
    /// the port builds the list and returns on the first unmappable element, which is the same test with the
    /// nullability the C# list needs to be typed.
    /// </remarks>
    internal IType? CreateReverseMappedType(IType source, IMappedType target, IIndexType constraint)
    {
        // We consider a source type reverse mappable if it has a string index signature or if
        // it has one or more properties and is of a partially inferable type.
        if (!(GetIndexInfoOfType(source, _stringType) is not null || GetPropertiesOfType(source).Count != 0 && IsPartiallyInferableType(source)))
        {
            return null;
        }

        // For arrays and tuples we infer new arrays and tuples where the reverse mapping has been
        // applied to the element type(s).
        if (IsArrayType(source))
        {
            var elementType = InferReverseMappedType(GetTypeArguments((ITypeReference)source)[0], target, constraint);
            if (elementType is null)
            {
                return null;
            }

            return CreateArrayType(elementType, IsReadonlyArrayType(source));
        }

        if (IsTupleType(source))
        {
            var elementTypes = new List<IType>();
            foreach (var t in GetElementTypes((ITupleTypeReference)source))
            {
                var elementType = InferReverseMappedType(t, target, constraint);
                if (elementType is null)
                {
                    return null;
                }

                elementTypes.Add(elementType);
            }

            var sourceTarget = (ITupleType)((ITypeReference)source).Target!;
            var elementFlags = (GetMappedTypeModifiers(target) & MappedTypeModifiers.IncludeOptional) != 0
                ? (List<ElementFlags>)Core.SameMap(sourceTarget.ElementFlags, (f, _) => (f & ElementFlags.Optional) != 0 ? ElementFlags.Required : f)!
                : sourceTarget.ElementFlags!;
            return CreateTupleType(elementTypes, elementFlags, sourceTarget.Readonly, sourceTarget.LabeledElementDeclarations);
        }

        // For all other object types we infer a new object type where the reverse mapping has been
        // applied to the type of each property.
        var reversed = (ReverseMappedType)CreateObjectType(ObjectFlags.ReverseMapped | ObjectFlags.Anonymous, /*symbol*/ null);
        reversed.Source = source;
        reversed.MappedType = target;
        reversed.ConstraintType = constraint;
        return reversed;
    }

    /// <summary>Reinterprets an indexed access type as a type parameter, without a runtime check.</summary>
    /// <remarks>
    /// PORT-NOTE (deviation): <c>inferReverseMappedTypeWorker</c> below ports
    /// <c>getIndexedAccessType(constraint.type, getTypeParameterFromMappedType(target)) as TypeParameter</c>
    /// (checker.ts:26515), and the value really is that call's cached <c>IndexedAccessType</c> (<c>T[K]</c>): the
    /// reference's own <c>getTemplateTypeFromMappedType</c> is what answers the identical object for the same
    /// <c>target</c>, and <c>getInferenceInfoForType</c> (checker.ts:27115-27124) is built on that identity — it matches
    /// the template type against <c>inference.typeParameter</c> by reference, so an inference whose slot held anything
    /// else would collect no candidates at all. The generated <c>IInferenceInfo.TypeParameter</c> mirrors types.ts:7116
    /// (<c>typeParameter: TypeParameter;</c>) and is therefore <c>ITypeParameter?</c>, while every C# cast — <c>(T)</c>,
    /// <c>as</c> and <c>(T)(object)</c> — checks the runtime type, so none of them can put the indexed access type into
    /// the slot. This is the situation §15.10's defect 7 records for <c>JSDocParser.Reinterpret</c>: the reference's own
    /// unchecked cast is observable, so the port puns it in one named helper instead of changing the answer for input the
    /// reference accepts. This inference is read back only through <c>getTypeFromInference</c> (checker.ts:26519), so the
    /// two reads of its slot are <c>getInferenceInfoForType</c>'s identity match (checker.ts:27118 — the read the pun
    /// exists for) and <c>inferWithPriority</c> (checker.ts:27235, whose target parameter is a <c>Type</c>); it never
    /// reaches <c>getInferredType</c>'s <c>TypeParameter</c>-typed reads (checker.ts:27594-27656) or the context lookup
    /// at checker.ts:35893, which is why the pun stays invisible downstream.
    /// </remarks>
    private static ITypeParameter ReinterpretAsTypeParameter(IType indexedAccessType)
        => System.Runtime.CompilerServices.Unsafe.As<IType, ITypeParameter>(ref indexedAccessType);

    /// <summary>ported from: src/compiler/checker.ts:26514-26520 — <c>inferReverseMappedTypeWorker</c>.</summary>
    internal IType InferReverseMappedTypeWorker(IType sourceType, IMappedType target, IIndexType constraint)
    {
        var typeParameter = ReinterpretAsTypeParameter(GetIndexedAccessType(constraint.Type!, GetTypeParameterFromMappedType(target)));
        var templateType = GetTemplateTypeFromMappedType(target);
        var inference = CreateInferenceInfo(typeParameter);
        InferTypes([inference], sourceType, templateType);
        return GetWidenedType(GetTypeFromInference(inference) ?? _unknownType);
    }

    /// <summary>ported from: src/compiler/checker.ts:26522-26541 — <c>inferReverseMappedType</c>.</summary>
    internal IType? InferReverseMappedType(IType source, IMappedType target, IIndexType constraint)
    {
        var cacheKey = source.Id + "," + target.Id + "," + constraint.Id;
        if (_reverseMappedCache.TryGetValue(cacheKey, out var cached))
        {
            return cached ?? _unknownType;
        }

        _reverseMappedSourceStack.Add(source);
        _reverseMappedTargetStack.Add(target);
        var saveExpandingFlags = _reverseExpandingFlags;
        if (IsDeeplyNestedType(source, _reverseMappedSourceStack, _reverseMappedSourceStack.Count, 2))
        {
            _reverseExpandingFlags |= ExpandingFlags.Source;
        }

        if (IsDeeplyNestedType(target, _reverseMappedTargetStack, _reverseMappedTargetStack.Count, 2))
        {
            _reverseExpandingFlags |= ExpandingFlags.Target;
        }

        IType? type = null;
        if (_reverseExpandingFlags != ExpandingFlags.Both)
        {
            type = InferReverseMappedTypeWorker(source, target, constraint);
        }

        _reverseMappedSourceStack.RemoveAt(_reverseMappedSourceStack.Count - 1);
        _reverseMappedTargetStack.RemoveAt(_reverseMappedTargetStack.Count - 1);
        _reverseExpandingFlags = saveExpandingFlags;
        _reverseMappedCache[cacheKey] = type;
        return type;
    }

    // ---------------------------------------------------------------------------------------------
    // Unmatched properties and definitely-unrelated types (checker.ts:26568-26614)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:26543-26566 — <c>getUnmatchedProperties</c>.</summary>
    internal IEnumerable<ISymbol> GetUnmatchedProperties(IType source, IType target, bool requireOptionalProperties, bool matchDiscriminantProperties)
    {
        var properties = GetPropertiesOfType(target);
        foreach (var targetProp in properties)
        {
            // TODO: remove this when we support static private identifier fields and find other solutions to get privateNamesAndStaticFields test to pass
            if (IsStaticPrivateIdentifierProperty(targetProp))
            {
                continue;
            }

            if (requireOptionalProperties || (targetProp.Flags & SymbolFlags.Optional) == 0 && (GetCheckFlags(targetProp) & CheckFlags.Partial) == 0)
            {
                var sourceProp = GetPropertyOfType(source, targetProp.EscapedName);
                if (sourceProp is null)
                {
                    yield return targetProp;
                }
                else if (matchDiscriminantProperties)
                {
                    var targetType = GetTypeOfSymbol(targetProp);
                    if ((targetType.Flags & TypeFlags.Unit) != 0)
                    {
                        var sourceType = GetTypeOfSymbol(sourceProp);
                        if (!((sourceType.Flags & TypeFlags.Any) != 0 || ReferenceEquals(GetRegularTypeOfLiteralType(sourceType), GetRegularTypeOfLiteralType(targetType))))
                        {
                            yield return targetProp;
                        }
                    }
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:26568-26570 — <c>getUnmatchedProperty</c>.</summary>
    internal ISymbol? GetUnmatchedProperty(IType source, IType target, bool requireOptionalProperties, bool matchDiscriminantProperties)
        => Core.FirstOrUndefinedIterator(GetUnmatchedProperties(source, target, requireOptionalProperties, matchDiscriminantProperties));

    /// <summary>ported from: src/compiler/checker.ts:26572-26575 — <c>tupleTypesDefinitelyUnrelated</c>.</summary>
    internal bool TupleTypesDefinitelyUnrelated(ITupleTypeReference source, ITupleTypeReference target)
    {
        var sourceTarget = (ITupleType)source.Target!;
        var targetTarget = (ITupleType)target.Target!;
        return (targetTarget.CombinedFlags & ElementFlags.Variadic) == 0 && targetTarget.MinLength > sourceTarget.MinLength
            || (targetTarget.CombinedFlags & ElementFlags.Variable) == 0 && ((sourceTarget.CombinedFlags & ElementFlags.Variable) != 0 || targetTarget.FixedLength < sourceTarget.FixedLength);
    }

    /// <summary>ported from: src/compiler/checker.ts:26577-26583 — <c>typesDefinitelyUnrelated</c>.</summary>
    internal bool TypesDefinitelyUnrelated(IType source, IType target)
        // Two tuple types with incompatible arities are definitely unrelated.
        // Two object types that each have a property that is unmatched in the other are definitely unrelated.
        => IsTupleType(source) && IsTupleType(target) ? TupleTypesDefinitelyUnrelated((ITupleTypeReference)source, (ITupleTypeReference)target)
            : GetUnmatchedProperty(source, target, /*requireOptionalProperties*/ false, /*matchDiscriminantProperties*/ true) is not null
                && GetUnmatchedProperty(target, source, /*requireOptionalProperties*/ false, /*matchDiscriminantProperties*/ false) is not null;

    /// <summary>ported from: src/compiler/checker.ts:26585-26589 — <c>getTypeFromInference</c>.</summary>
    internal IType? GetTypeFromInference(IInferenceInfo inference)
        => inference.Candidates is not null ? GetUnionType(inference.Candidates, UnionReduction.Subtype)
            : inference.ContraCandidates is not null ? GetIntersectionType(inference.ContraCandidates)
            : null;

    /// <summary>ported from: src/compiler/checker.ts:26591-26593 — <c>hasSkipDirectInferenceFlag</c>.</summary>
    internal bool HasSkipDirectInferenceFlag(Node node)
        => GetNodeLinks(node).SkipDirectInference == true;

    /// <summary>ported from: src/compiler/checker.ts:26595-26597 — <c>isFromInferenceBlockedSource</c>.</summary>
    internal bool IsFromInferenceBlockedSource(IType type)
        => type.Symbol is not null && Core.Some(type.Symbol.Declarations, d => HasSkipDirectInferenceFlag((Node)d));

    /// <summary>ported from: src/compiler/checker.ts:26599-26614 — <c>templateLiteralTypesDefinitelyUnrelated</c>.</summary>
    internal static bool TemplateLiteralTypesDefinitelyUnrelated(ITemplateLiteralType source, ITemplateLiteralType target)
    {
        // Two template literal types with diffences in their starting or ending text spans are definitely unrelated.
        var sourceTexts = source.Texts!;
        var targetTexts = target.Texts!;
        var sourceStart = sourceTexts[0];
        var targetStart = targetTexts[0];
        var sourceEnd = sourceTexts[sourceTexts.Count - 1];
        var targetEnd = targetTexts[targetTexts.Count - 1];
        var startLen = Math.Min(sourceStart.Length, targetStart.Length);
        var endLen = Math.Min(sourceEnd.Length, targetEnd.Length);
        return sourceStart.Substring(0, startLen) != targetStart.Substring(0, startLen)
            || sourceEnd.Substring(sourceEnd.Length - endLen) != targetEnd.Substring(targetEnd.Length - endLen);
    }
    // ---------------------------------------------------------------------------------------------
    // String and template-literal inference (checker.ts:26616-26772)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Tests whether the provided string can be parsed as a number.
    /// </summary>
    /// <param name="s">The string to test.</param>
    /// <param name="roundTripOnly">Indicates the resulting number matches the input when converted back to a string.</param>
    /// <remarks>ported from: src/compiler/checker.ts:26616-26620 — <c>isValidNumberString</c>.</remarks>
    /// <remarks>
    /// PORT-NOTE: TS's <c>+s</c> and <c>"" + n</c> are JS <c>Number</c>/<c>String</c> coercions; the port uses the
    /// landed <c>Js.ToNumber</c>/<c>Js.NumberToString</c>, which are those two coercions.
    /// </remarks>
    internal static bool IsValidNumberString(string s, bool roundTripOnly)
    {
        if (s == "")
        {
            return false;
        }

        var n = Js.ToNumber(s);
        return double.IsFinite(n) && (!roundTripOnly || Js.NumberToString(n) == s);
    }

    /// <param name="text">a valid bigint string excluding a trailing `n`, but including a possible prefix `-`. Use `isValidBigIntString(text, roundTripOnly)` before calling this function.</param>
    /// <remarks>ported from: src/compiler/checker.ts:26625-26627 — <c>parseBigIntLiteralType</c>.</remarks>
    internal IBigIntLiteralType ParseBigIntLiteralType(string text)
        => GetBigIntLiteralType(Utilities.ParseValidBigInt(text));

    /// <summary>ported from: src/compiler/checker.ts:26629-26653 — <c>isMemberOfStringMapping</c>.</summary>
    internal bool IsMemberOfStringMapping(IType source, IType target)
    {
        if ((target.Flags & TypeFlags.Any) != 0)
        {
            return true;
        }

        if ((target.Flags & (TypeFlags.String | TypeFlags.TemplateLiteral)) != 0)
        {
            return IsTypeAssignableTo(source, target);
        }

        if ((target.Flags & TypeFlags.StringMapping) != 0)
        {
            // We need to see whether applying the same mappings of the target
            // onto the source would produce an identical type *and* that
            // it's compatible with the inner-most non-string-mapped type.
            //
            // The intuition here is that if same mappings don't affect the source at all,
            // and the source is compatible with the unmapped target, then they must
            // still reside in the same domain.
            var mappingStack = new List<ISymbol>();
            while ((target.Flags & TypeFlags.StringMapping) != 0)
            {
                mappingStack.Insert(0, target.Symbol!);
                target = ((IStringMappingType)target).Type!;
            }

            var mappedSource = Core.ReduceLeft(mappingStack, (memo, value, _) => GetStringMappingType(value, memo), source);
            return ReferenceEquals(mappedSource, source) && IsMemberOfStringMapping(source, target);
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:26655-26675 — <c>isValidTypeForTemplateLiteralPlaceholder</c>.</summary>
    internal bool IsValidTypeForTemplateLiteralPlaceholder(IType source, IType target)
    {
        if ((target.Flags & TypeFlags.Intersection) != 0)
        {
            return Core.Every(((IIntersectionType)target).Types, (t, _) => ReferenceEquals(t, _emptyTypeLiteralType) || IsValidTypeForTemplateLiteralPlaceholder(source, t));
        }

        if ((target.Flags & TypeFlags.String) != 0 || IsTypeAssignableTo(source, target))
        {
            return true;
        }

        if ((source.Flags & TypeFlags.StringLiteral) != 0)
        {
            var value = (string)((ILiteralType)source).Value!;
            return (target.Flags & TypeFlags.Number) != 0 && IsValidNumberString(value, /*roundTripOnly*/ false)
                || (target.Flags & TypeFlags.BigInt) != 0 && Utilities.IsValidBigIntString(value, /*roundTripOnly*/ false)
                || (target.Flags & (TypeFlags.BooleanLiteral | TypeFlags.Nullable)) != 0 && value == ((IIntrinsicType)target).IntrinsicName
                || (target.Flags & TypeFlags.StringMapping) != 0 && IsMemberOfStringMapping(source, target)
                || (target.Flags & TypeFlags.TemplateLiteral) != 0 && IsTypeMatchedByTemplateLiteralType(source, (ITemplateLiteralType)target);
        }

        if ((source.Flags & TypeFlags.TemplateLiteral) != 0)
        {
            var texts = ((ITemplateLiteralType)source).Texts!;
            return texts.Count == 2 && texts[0] == "" && texts[1] == "" && IsTypeAssignableTo(((ITemplateLiteralType)source).Types![0], target);
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:26677-26685 — <c>inferTypesFromTemplateLiteralType</c>.</summary>
    internal List<IType>? InferTypesFromTemplateLiteralType(IType source, ITemplateLiteralType target)
        => (source.Flags & TypeFlags.StringLiteral) != 0
            ? InferFromSingleLiteralPart((string)((ILiteralType)source).Value!, target)
            : (source.Flags & TypeFlags.TemplateLiteral) != 0
                ? Core.ArrayIsEqualTo(((ITemplateLiteralType)source).Texts, target.Texts)
                    ? InferFromMatchingTemplateParts((ITemplateLiteralType)source, target)
                    : InferFromLiteralPartsToTemplateLiteral(((ITemplateLiteralType)source).Texts!, ((ITemplateLiteralType)source).Types!, target)
                : null;

    /// <summary>
    /// The reference's <c>inferFromLiteralPartsToTemplateLiteral([value], [], target)</c> — a string literal source.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE (performance, measured): the reference builds a one-element text list and an empty type list for this
    /// case; the call runs ~27.8M times on template-literal-heavy input and those lists cost 64 B per call, which the
    /// shard's allocation counter showed as 3.4 GiB of its 4.9 GiB semantic total (docs §18.4i). The body is the
    /// reference's own single-part path with the list inlined: with one text part <c>lastSourceIndex</c> is 0, so its
    /// <c>getSourceText</c> answers the trimmed text for every index it can be asked about, its search loop advances
    /// past that one part only to answer <c>null</c>, and its <c>addMatch</c> always takes the <c>s === seg</c> arm
    /// (the other arm is what reads <c>sourceTypes</c>, and it needs <c>s !== seg</c>). The guard still answers
    /// <c>null</c> without allocating. Equivalence was verified by running both implementations side by side over a
    /// whole shard — it caught one real difference (the reference's <c>addMatch</c> advances its source *position* as
    /// well as its index, which matters for adjacent empty delimiters) before this landed.
    /// </remarks>
    private List<IType>? InferFromSingleLiteralPart(string sourceText, ITemplateLiteralType target)
    {
        var targetTexts = target.Texts!;
        var lastTargetIndex = targetTexts.Count - 1;
        var targetStartText = targetTexts[0];
        var targetEndText = targetTexts[lastTargetIndex];
        if (
            sourceText.Length < targetStartText.Length + targetEndText.Length ||
            !Core.StartsWith(sourceText, targetStartText) || !Core.EndsWith(sourceText, targetEndText)
        )
        {
            return null;
        }

        var remainingEndText = sourceText.Substring(0, sourceText.Length - targetEndText.Length);
        List<IType>? matches = null;
        var pos = targetStartText.Length;

        void AddMatch(int p)
        {
            matches ??= [];
            matches.Add(GetStringLiteralType(remainingEndText.Substring(pos, p - pos)));
            // The reference's `addMatch` advances its source position as well as its index; skipping it turns adjacent
            // empty delimiters (`` `${A}${B}${C}` ``) into matches the reference never reports.
            pos = p;
        }

        for (var i = 1; i < lastTargetIndex; i++)
        {
            var delim = targetTexts[i];
            if (delim.Length > 0)
            {
                var p = remainingEndText.IndexOf(delim, pos, StringComparison.Ordinal);
                if (p < 0)
                {
                    return null;
                }

                AddMatch(p);
                pos += delim.Length;
            }
            else if (pos < remainingEndText.Length)
            {
                AddMatch(pos + 1);
            }
            else
            {
                return null;
            }
        }

        AddMatch(remainingEndText.Length);
        return matches ?? [];
    }

    /// <summary>
    /// The reference's <c>map(source.types, (s, i) =&gt; isTypeAssignableTo(…) ? s : getStringLikeTypeForType(s))</c>.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE (performance, measured): the reference's callback captures <c>target</c>, so its shape allocates a
    /// closure — and this is the branch a template-literal source takes tens of millions of times on
    /// template-literal-heavy input (docs §18.4i). The loop builds the same list with no closure.
    /// </remarks>
    private List<IType> InferFromMatchingTemplateParts(ITemplateLiteralType source, ITemplateLiteralType target)
    {
        var sourceTypes = source.Types!;
        var targetTypes = target.Types!;
        var result = new List<IType>(sourceTypes.Count);
        for (var i = 0; i < sourceTypes.Count; i++)
        {
            var s = sourceTypes[i];
            result.Add(IsTypeAssignableTo(GetBaseConstraintOrType(s), GetBaseConstraintOrType(targetTypes[i])) ? s : GetStringLikeTypeForType(s));
        }

        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:26687-26690 — <c>isTypeMatchedByTemplateLiteralType</c>.</summary>
    internal bool IsTypeMatchedByTemplateLiteralType(IType source, ITemplateLiteralType target)
    {
        var inferences = InferTypesFromTemplateLiteralType(source, target);
        return inferences is not null && Core.Every(inferences, (r, i) => IsValidTypeForTemplateLiteralPlaceholder(r, target.Types![i]));
    }

    /// <summary>ported from: src/compiler/checker.ts:26692-26694 — <c>getStringLikeTypeForType</c>.</summary>
    internal IType GetStringLikeTypeForType(IType type)
        => (type.Flags & (TypeFlags.Any | TypeFlags.StringLike)) != 0 ? type : GetTemplateLiteralType(["", ""], [type]);

    /// <summary>
    /// This function infers from the text parts and type parts of a source literal to a target template literal. The
    /// number of text parts is always one more than the number of type parts, and a source string literal is treated
    /// as a source with one text part and zero type parts. The function returns an array of inferred string or
    /// template literal types corresponding to the placeholders in the target template literal, or undefined if the
    /// source doesn't match the target.
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:26715-26772 — <c>inferFromLiteralPartsToTemplateLiteral</c>.</remarks>
    /// <remarks>
    /// PORT-NOTE: the two array slices inside <c>addMatch</c> are written as explicit copies (the landed style avoids
    /// <c>System.Linq</c>); the indices are the reference's own.
    /// </remarks>
    internal List<IType>? InferFromLiteralPartsToTemplateLiteral(IList<string> sourceTexts, IList<IType> sourceTypes, ITemplateLiteralType target)
    {
        var lastSourceIndex = sourceTexts.Count - 1;
        var sourceStartText = sourceTexts[0];
        var sourceEndText = sourceTexts[lastSourceIndex];
        var targetTexts = target.Texts!;
        var lastTargetIndex = targetTexts.Count - 1;
        var targetStartText = targetTexts[0];
        var targetEndText = targetTexts[lastTargetIndex];
        if (
            lastSourceIndex == 0 && sourceStartText.Length < targetStartText.Length + targetEndText.Length ||
            !Core.StartsWith(sourceStartText, targetStartText) || !Core.EndsWith(sourceEndText, targetEndText)
        )
        {
            return null;
        }

        var remainingEndText = sourceEndText.Substring(0, sourceEndText.Length - targetEndText.Length);
        var matches = new List<IType>();
        var seg = 0;
        var pos = targetStartText.Length;
        for (var i = 1; i < lastTargetIndex; i++)
        {
            var delim = targetTexts[i];
            if (delim.Length > 0)
            {
                var s = seg;
                var p = pos;
                while (true)
                {
                    p = GetSourceText(s).IndexOf(delim, p, StringComparison.Ordinal);
                    if (p >= 0)
                    {
                        break;
                    }

                    s++;
                    if (s == sourceTexts.Count)
                    {
                        return null;
                    }

                    p = 0;
                }

                AddMatch(s, p);
                pos += delim.Length;
            }
            else if (pos < GetSourceText(seg).Length)
            {
                AddMatch(seg, pos + 1);
            }
            else if (seg < lastSourceIndex)
            {
                AddMatch(seg + 1, 0);
            }
            else
            {
                return null;
            }
        }

        AddMatch(lastSourceIndex, GetSourceText(lastSourceIndex).Length);
        return matches;

        string GetSourceText(int index)
            => index < lastSourceIndex ? sourceTexts[index] : remainingEndText;

        void AddMatch(int s, int p)
        {
            IType matchType;
            if (s == seg)
            {
                matchType = GetStringLiteralType(GetSourceText(s).Substring(pos, p - pos));
            }
            else
            {
                var texts = new List<string> { sourceTexts[seg].Substring(pos) };
                for (var i = seg + 1; i < s; i++)
                {
                    texts.Add(sourceTexts[i]);
                }

                texts.Add(GetSourceText(s).Substring(0, p));
                var types = new List<IType>();
                for (var i = seg; i < s; i++)
                {
                    types.Add(sourceTypes[i]);
                }

                matchType = GetTemplateLiteralType(texts, types);
            }

            matches.Add(matchType);
            seg = s;
            pos = p;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The union-constituent pickers GetBestMatchingType composes (checker.ts:53670-53760)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The reference declares these five at checker.ts:53670-53760, outside the 24902-26773 band, but their only
    /// caller is this file's <c>GetBestMatchingType</c> (checker.ts:24938), so the block was routed here.
    /// </summary>

    /// <summary>ported from: src/compiler/checker.ts:53670-53686 — <c>findMatchingTypeReferenceOrTypeAliasReference</c>.</summary>
    internal IType? FindMatchingTypeReferenceOrTypeAliasReference(IType source, IUnionOrIntersectionType unionTarget)
    {
        var sourceObjectFlags = GetObjectFlags(source);
        if ((sourceObjectFlags & (ObjectFlags.Reference | ObjectFlags.Anonymous)) != 0 && (unionTarget.Flags & TypeFlags.Union) != 0)
        {
            return Core.Find(unionTarget.Types, (target, _) =>
            {
                if ((target.Flags & TypeFlags.Object) != 0)
                {
                    var overlapObjFlags = sourceObjectFlags & GetObjectFlags(target);
                    if ((overlapObjFlags & ObjectFlags.Reference) != 0)
                    {
                        return ReferenceEquals(((ITypeReference)source).Target, ((ITypeReference)target).Target);
                    }

                    if ((overlapObjFlags & ObjectFlags.Anonymous) != 0)
                    {
                        return source.AliasSymbol is not null && ReferenceEquals(source.AliasSymbol, target.AliasSymbol);
                    }
                }

                return false;
            });
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:53688-53692 — <c>findBestTypeForObjectLiteral</c>.</summary>
    internal IType? FindBestTypeForObjectLiteral(IType source, IUnionOrIntersectionType unionTarget)
        => (GetObjectFlags(source) & ObjectFlags.ObjectLiteral) != 0 && SomeType(unionTarget, IsArrayLikeType)
            ? Core.Find(unionTarget.Types, (t, _) => !IsArrayLikeType(t))
            : null;

    /// <summary>ported from: src/compiler/checker.ts:53694-53701 — <c>findBestTypeForInvokable</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's comma expression <c>(signatureKind = SignatureKind.Construct, getSignaturesOfType(…))</c>
    /// is the two-statement form below, which is the same short-circuiting test.
    /// </remarks>
    internal IType? FindBestTypeForInvokable(IType source, IUnionOrIntersectionType unionTarget)
    {
        var signatureKind = SignatureKind.Call;
        var hasSignatures = GetSignaturesOfType(source, signatureKind).Count > 0;
        if (!hasSignatures)
        {
            signatureKind = SignatureKind.Construct;
            hasSignatures = GetSignaturesOfType(source, signatureKind).Count > 0;
        }

        return hasSignatures
            ? Core.Find(unionTarget.Types, (t, _) => GetSignaturesOfType(t, signatureKind).Count > 0)
            : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:53703-53726 — <c>findMostOverlappyType</c>.</summary>
    internal IType? FindMostOverlappyType(IType source, IUnionOrIntersectionType unionTarget)
    {
        IType? bestMatch = null;
        if ((source.Flags & (TypeFlags.Primitive | TypeFlags.InstantiablePrimitive)) == 0)
        {
            var matchingCount = 0;
            foreach (var target in unionTarget.Types!)
            {
                if ((target.Flags & (TypeFlags.Primitive | TypeFlags.InstantiablePrimitive)) == 0)
                {
                    var overlap = GetIntersectionType([GetIndexType(source), GetIndexType(target)]);
                    if ((overlap.Flags & TypeFlags.Index) != 0)
                    {
                        // perfect overlap of keys
                        return target;
                    }
                    else if (IsUnitType(overlap) || (overlap.Flags & TypeFlags.Union) != 0)
                    {
                        // We only want to account for literal types otherwise.
                        // If we have a union of index types, it seems likely that we
                        // needed to elaborate between two generic mapped types anyway.
                        var len = (overlap.Flags & TypeFlags.Union) != 0 ? Core.CountWhere(((IUnionType)overlap).Types, (t, _) => IsUnitType(t)) : 1;
                        if (len >= matchingCount)
                        {
                            bestMatch = target;
                            matchingCount = len;
                        }
                    }
                }
            }
        }

        return bestMatch;
    }

    /// <summary>ported from: src/compiler/checker.ts:53741-53758 — <c>findMatchingDiscriminantType</c>.</summary>
    internal IType? FindMatchingDiscriminantType(IType source, IType target, TypeComparer isRelatedTo)
    {
        if ((target.Flags & TypeFlags.Union) != 0 && (source.Flags & (TypeFlags.Intersection | TypeFlags.Object)) != 0)
        {
            var match = GetMatchingUnionConstituentForType((IUnionType)target, source);
            if (match is not null)
            {
                return match;
            }

            var sourceProperties = GetPropertiesOfType(source);
            if (sourceProperties is not null)
            {
                var sourcePropertiesFiltered = FindDiscriminantProperties(sourceProperties, target);
                if (sourcePropertiesFiltered is not null)
                {
                    var discriminated = DiscriminateTypeByDiscriminableItems(
                        target,
                        [.. Core.Map(sourcePropertiesFiltered, (p, _) => (GetDiscriminatingType: new Func<IType>(() => GetTypeOfSymbol(p)), PropertyName: p.EscapedName ?? ""))!],
                        (s, t) => isRelatedTo(s, t));
                    if (!ReferenceEquals(discriminated, target))
                    {
                        return discriminated;
                    }
                }
            }
        }

        return null;
    }
}
