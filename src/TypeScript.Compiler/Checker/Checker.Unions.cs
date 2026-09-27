namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:18064-18857 — the union/intersection construction block
// ported from: src/compiler/checker.ts:2049-2060 — the four caches it owns (see the class remarks)
/// <summary>
/// Union and intersection type construction (checker.ts:18064-18857): the sorted constituent set
/// (<c>containsType</c>/<c>insertType</c>), <c>getUnionType</c> and its reductions, the denormalized origin algebra,
/// and <c>getIntersectionType</c>'s cross-product expansion.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (state): the reference declares the four caches below at checker.ts:2049-2060, inside the state block
/// that <c>createTypeChecker</c> opens with. Those lines are outside every M6 slice's range, so — by the rule that the
/// slice whose members read a cache declares it — <c>unionTypes</c>, <c>unionOfUnionTypes</c>, <c>intersectionTypes</c>
/// and <c>subtypeReductionCache</c> are declared here, because this file is their only reader.
/// </para>
/// <para>
/// PORT-NOTE (ordered map): <c>getIntersectionType</c> derives the constituent order of the intersection it builds
/// from <c>arrayFrom(typeMembershipMap.values())</c>, and a JS <c>Map</c> iterates in insertion order. That order is
/// the intersection type's <c>types</c> array, which reaches display and the <c>.types</c> baseline, so the port
/// spells that one map as the BCL <see cref="OrderedDictionary{TKey,TValue}"/> — the same store
/// <see cref="SymbolTable"/> composes, with the semantics a JS <c>Map</c> has (a new key appends, an update of an
/// existing key keeps its position; the reference's <c>has</c> guard plus <c>set</c> is therefore an append).
/// </para>
/// <para>
/// PORT-NOTE (nullable model slots): the invariants TS encodes structurally are nullable in the port's model, because
/// a class carries the slots of its whole family (see <c>Model/Type.cs</c>'s remarks). Three kinds of read therefore
/// carry <c>!</c>, each justified by the guard directly above it: <c>Types</c> of a union or intersection after a
/// <c>TypeFlags.Union</c>/<c>TypeFlags.Intersection</c> test, a symbol's <c>EscapedName</c> (every symbol has one —
/// the invariant <c>Checker.cs</c>'s <c>globalThisSymbol</c> note states), and <c>NodeLinks.ResolvedType</c> right
/// after the <c>is null</c> guard that fills it, because C#'s flow analysis tracks locals and fields but not
/// properties, so TS's narrowing of that property does not survive the port. The same applies to the port's array
/// helpers, whose result type mirrors TS's <c>readonly T[] | undefined</c>: <c>Core.Map</c>/<c>Core.Filter</c> on a
/// non-null input answer non-null, and the call site asserts what the input guarantees.
/// </para>
/// <para>
/// PORT-NOTE (the boolean union's <c>intrinsicName</c>): <c>getUnionTypeFromSortedList</c> (:18453-18455) additionally
/// writes <c>(type as UnionType &amp; IntrinsicType).intrinsicName = "boolean"</c> onto the union it just created. The
/// port's union object is a <see cref="UnionType"/>, whose family carries no <c>intrinsicName</c> slot
/// (<c>Model/IntrinsicType.cs</c> is a separate class), so this is the one statement of this range the port cannot
/// express. Nothing reads it: every reader of <c>IntrinsicType.intrinsicName</c> is gated on
/// <c>TypeFlags.BooleanLiteral</c> or <c>TypeFlags.Nullable</c> (checker.ts:6865-6866, :19105, :26666, :27329-27331,
/// :53983-53984) and never on the <c>TypeFlags.Boolean</c> a union of the two boolean literal types carries. The
/// <c>type.flags |= TypeFlags.Boolean</c> that accompanies it — the part every reader depends on — is ported.
/// </para>
/// <para>
/// PORT-NOTE (<c>typePredicateKindsMatch</c>): the reference compares <c>a.parameterIndex === b.parameterIndex</c>
/// across the four-variant <c>TypePredicate</c> union, whose two <c>This</c> variants declare that slot
/// <c>undefined</c>. The port keeps one physical slot behind four faces (<c>Model/TypePredicate.cs</c>) and only the
/// face the kind selects may read it, so the comparison spells out the <c>This</c> case (two absent indexes) rather
/// than reading the slot through a face that would unbox it.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // The caches these members own (checker.ts:2049-2060)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:2049 — <c>unionTypes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: declared here although its declaration line is outside this slice's range, because
    /// <c>getUnionTypeFromSortedList</c> (:18432-18459) is its only reader. TS's <c>Map&lt;string, UnionType&gt;</c> is
    /// keyed by <c>getTypeListId(types) + getAliasId(...)</c> and only ever read/written per key.
    /// </remarks>
    internal readonly Dictionary<string, IUnionType> _unionTypes = [];

    /// <summary>ported from: src/compiler/checker.ts:2050 — <c>unionOfUnionTypes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: declared here for the same reason as <see cref="_unionTypes"/> — <c>getUnionType</c>'s two-constituent
    /// fast path (:18313-18333) is its only reader. Its key is <c>`${minId}${infix}${maxId}${aliasId}`</c>, a plain
    /// string key over type ids.
    /// </remarks>
    internal readonly Dictionary<string, IType> _unionOfUnionTypes = [];

    /// <summary>ported from: src/compiler/checker.ts:2051 — <c>intersectionTypes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: declared here for the same reason as <see cref="_unionTypes"/> — <c>getIntersectionType</c>
    /// (:18659-18796) is its only reader.
    /// </remarks>
    internal readonly Dictionary<string, IType> _intersectionTypes = [];

    /// <summary>ported from: src/compiler/checker.ts:2060 — <c>subtypeReductionCache</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: declared here for the same reason as <see cref="_unionTypes"/> — <c>removeSubtypes</c>
    /// (:18122-18199) is its only reader. The cached value is the very array handed in (and handed back), so the slot
    /// states <see cref="List{T}"/> rather than <see cref="IList{T}"/>: <c>removeSubtypes</c> returns it as
    /// <c>Type[] | undefined</c>.
    /// </remarks>
    internal readonly Dictionary<string, List<IType>> _subtypeReductionCache = [];

    // ---------------------------------------------------------------------------------------------
    // The sorted constituent set (checker.ts:18064-18079)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:18064-18066 — <c>getTypeId</c>.</summary>
    internal int GetTypeId(IType type)
    {
        return type.Id;
    }

    /// <summary>ported from: src/compiler/checker.ts:18068-18070 — <c>containsType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>identity</c>/<c>compareValues</c> pair is <c>Core.Identity</c> and the int comparer
    /// <c>(a, b) =&gt; Core.CompareValues(a, b)</c>, the shape <c>Lexer/ScannerUtilities.cs</c>'s
    /// <c>computeLineOfPosition</c> uses for the same call. The <c>stableTypeOrdering</c> arm passes <see cref="CompareTypes"/>,
    /// whose JS-sort <c>int</c> result the port's <c>Comparison</c>-returning <c>Core.Comparer&lt;T&gt;</c> takes through
    /// <c>Core.CompareValues(CompareTypes(a, b), 0)</c>, the adapter <c>getNamedMembers</c> uses (see
    /// Checker/Checker.Check.Sort.cs).
    /// </remarks>
    internal bool ContainsType(List<IType> types, IType type)
    {
        return _stableTypeOrdering
            ? Core.BinarySearch(types, type, Core.Identity, (a, b) => Core.CompareValues(CompareTypes(a, b), 0)) >= 0
            : Core.BinarySearch(types, type, GetTypeId, static (a, b) => Core.CompareValues(a, b)) >= 0;
    }

    /// <summary>ported from: src/compiler/checker.ts:18072-18079 — <c>insertType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>types.splice(~index, 0, type)</c> is <c>List.Insert</c> at the same 2's-complement index.
    /// </remarks>
    internal bool InsertType(List<IType> types, IType type)
    {
        var index = _stableTypeOrdering
            ? Core.BinarySearch(types, type, Core.Identity, (a, b) => Core.CompareValues(CompareTypes(a, b), 0))
            : Core.BinarySearch(types, type, GetTypeId, static (a, b) => Core.CompareValues(a, b));
        if (index < 0)
        {
            types.Insert(~index, type);
            return true;
        }
        return false;
    }

    // ---------------------------------------------------------------------------------------------
    // Building the union constituent set (checker.ts:18081-18120)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:18081-18104 — <c>addTypeToUnion</c>.</summary>
    internal TypeFlags AddTypeToUnion(List<IType> typeSet, TypeFlags includes, IType type)
    {
        var flags = type.Flags;
        // We ignore 'never' types in unions
        if ((flags & TypeFlags.Never) == 0)
        {
            includes |= flags & TypeFlags.IncludesMask;
            if ((flags & TypeFlags.Instantiable) != 0) includes |= TypeFlags.IncludesInstantiable;
            if ((flags & TypeFlags.Intersection) != 0 && (GetObjectFlags(type) & ObjectFlags.IsConstrainedTypeVariable) != 0) includes |= TypeFlags.IncludesConstrainedTypeVariable;
            if (ReferenceEquals(type, _wildcardType)) includes |= TypeFlags.IncludesWildcard;
            if (IsErrorType(type)) includes |= TypeFlags.IncludesError;
            if (!_strictNullChecks && (flags & TypeFlags.Nullable) != 0)
            {
                if ((GetObjectFlags(type) & ObjectFlags.ContainsWideningType) == 0) includes |= TypeFlags.IncludesNonWideningType;
            }
            else
            {
                var len = typeSet.Count;
                var index = _stableTypeOrdering
                    ? Core.BinarySearch(typeSet, type, Core.Identity, (a, b) => Core.CompareValues(CompareTypes(a, b), 0))
                    : (len != 0 && type.Id > typeSet[len - 1].Id ? ~len : Core.BinarySearch(typeSet, type, GetTypeId, static (a, b) => Core.CompareValues(a, b)));
                if (index < 0)
                {
                    typeSet.Insert(~index, type);
                }
            }
        }
        return includes;
    }

    /// <summary>ported from: src/compiler/checker.ts:18106-18120 — <c>addTypesToUnion</c>.</summary>
    internal TypeFlags AddTypesToUnion(List<IType> typeSet, TypeFlags includes, List<IType> types)
    {
        IType? lastType = null;
        foreach (var type in types)
        {
            // We skip the type if it is the same as the last type we processed. This simple test particularly
            // saves a lot of work for large lists of the same union type, such as when resolving `Record<A, B>[A]`,
            // where A and B are large union types.
            if (!ReferenceEquals(type, lastType))
            {
                includes = (type.Flags & TypeFlags.Union) != 0
                    ? AddTypesToUnion(typeSet, includes | (IsNamedUnionType(type) ? TypeFlags.Union : (TypeFlags)0), ((IUnionType)type).Types!)
                    : AddTypeToUnion(typeSet, includes, type);
                lastType = type;
            }
        }
        return includes;
    }

    // ---------------------------------------------------------------------------------------------
    // Subtype reduction (checker.ts:18122-18199)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:18122-18199 — <c>removeSubtypes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>count / (len - i) * len</c> is double arithmetic (JS numbers), and the
    /// <c>&gt; 1000000</c> test it feeds would answer differently if the division truncated first, so the port folds it
    /// in a <c>double</c>.
    /// </remarks>
    internal List<IType>? RemoveSubtypes(List<IType> types, bool hasObjectTypes)
    {
        // [] and [T] immediately reduce to [] and [T] respectively
        if (types.Count < 2)
        {
            return types;
        }

        var id = GetTypeListId(types);
        var match = _subtypeReductionCache.GetValueOrDefault(id);
        if (match is not null)
        {
            return match;
        }

        // We assume that redundant primitive types have already been removed from the types array and that there
        // are no any and unknown types in the array. Thus, the only possible supertypes for primitive types are empty
        // object types, and if none of those are present we can exclude primitive types from the subtype check.
        var hasEmptyObject = hasObjectTypes && Core.Some(types, t => (t.Flags & TypeFlags.Object) != 0 && !IsGenericMappedType(t) && IsEmptyResolvedType(ResolveStructuredTypeMembers((ObjectType)t)));
        var len = types.Count;
        var i = len;
        var count = 0;
        while (i > 0)
        {
            i--;
            var source = types[i];
            if (hasEmptyObject || (source.Flags & TypeFlags.StructuredOrInstantiable) != 0)
            {
                // A type parameter with a union constraint may be a subtype of some union, but not a subtype of the
                // individual constituents of that union. For example, `T extends A | B` is a subtype of `A | B`, but not
                // a subtype of just `A` or just `B`. When we encounter such a type parameter, we therefore check if the
                // type parameter is a subtype of a union of all the other types.
                if ((source.Flags & TypeFlags.TypeParameter) != 0 && (GetBaseConstraintOrType(source).Flags & TypeFlags.Union) != 0)
                {
                    if (IsTypeRelatedTo(source, GetUnionType(Core.Map(types, (t, _) => ReferenceEquals(t, source) ? _neverType : t)!), _strictSubtypeRelation))
                    {
                        Core.OrderedRemoveItemAt(types, i);
                    }
                    continue;
                }
                // Find the first property with a unit type, if any. When constituents have a property by the same name
                // but of a different unit type, we can quickly disqualify them from subtype checks. This helps subtype
                // reduction of large discriminated union types.
                var keyProperty = (source.Flags & (TypeFlags.Object | TypeFlags.Intersection | TypeFlags.InstantiableNonPrimitive)) != 0
                    ? Core.Find(GetPropertiesOfType(source), (p, _) => IsUnitType(GetTypeOfSymbol(p)))
                    : null;
                var keyPropertyType = keyProperty is not null ? GetRegularTypeOfLiteralType(GetTypeOfSymbol(keyProperty)) : null;
                foreach (var target in types)
                {
                    if (!ReferenceEquals(source, target))
                    {
                        if (count == 100000)
                        {
                            // After 100000 subtype checks we estimate the remaining amount of work by assuming the
                            // same ratio of checks per element. If the estimated number of remaining type checks is
                            // greater than 1M we deem the union type too complex to represent. This for example
                            // caps union types at 1000 unique object types.
                            var estimatedCount = ((double)count / (len - i)) * len;
                            if (estimatedCount > 1000000)
                            {
                                Tracing.Current?.Instant(
                                    Tracing.Phase.CheckTypes,
                                    "removeSubtypes_DepthLimit",
                                    TracingArgs.Of(("typeIds", TracingValue.Array(types.Select(t => TracingValue.From(t.Id))))));
                                Error(_currentNode, Diagnostics.Expression_produces_a_union_type_that_is_too_complex_to_represent);
                                return null;
                            }
                        }
                        count++;
                        if (keyProperty is not null && (target.Flags & (TypeFlags.Object | TypeFlags.Intersection | TypeFlags.InstantiableNonPrimitive)) != 0)
                        {
                            var t = GetTypeOfPropertyOfType(target, keyProperty.EscapedName!);
                            if (t is not null && IsUnitType(t) && !ReferenceEquals(GetRegularTypeOfLiteralType(t), keyPropertyType))
                            {
                                continue;
                            }
                        }
                        if (
                            IsTypeRelatedTo(source, target, _strictSubtypeRelation) && (
                                (GetObjectFlags(GetTargetType(source)) & ObjectFlags.Class) == 0 ||
                                (GetObjectFlags(GetTargetType(target)) & ObjectFlags.Class) == 0 ||
                                IsTypeDerivedFrom(source, target)
                            )
                        )
                        {
                            Core.OrderedRemoveItemAt(types, i);
                            break;
                        }
                    }
                }
            }
        }
        _subtypeReductionCache[id] = types;
        return types;
    }

    // ---------------------------------------------------------------------------------------------
    // The union reductions (checker.ts:18201-18298)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:18201-18217 — <c>removeRedundantLiteralTypes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>remove</c> is a chain of <c>&amp;&amp;</c>/<c>||</c> over the *numbers* the mask tests
    /// produce, which the <c>if</c> then reads as truthiness; the port states the same tests as booleans, and the
    /// last term's <c>containsType(types, (t as LiteralType).regularType)</c> passes a <c>FreshableType</c>, which is a
    /// <c>Type</c> upstream and an <c>IType</c> in the port's model.
    /// <para>
    /// PORT-NOTE: that last term's <c>as LiteralType</c> (checker.ts:18212) is another <c>as</c> re-view of an open
    /// object, not a kind test: <c>regularType</c> is declared on <c>FreshableType</c> (types.ts:6472) and
    /// <c>LiteralType extends FreshableType</c> (types.ts:6482) adds only <c>value</c>, so the slot read is the
    /// freshable one. The reference itself reads it through <c>FreshableType</c> under this same guard elsewhere
    /// (<c>getRegularTypeOfLiteralType</c>, checker.ts:20296; <c>getNormalizedType</c>, checker.ts:22320), and the port
    /// must, because the freshable set also holds the boolean literal intrinsics (<c>BooleanLiteralType</c> is an
    /// <c>IntrinsicType</c>, Model/IntrinsicType.cs:76) and the guard is <c>IsFreshLiteralType</c>
    /// (checker.ts:20301-20303), whose own body reads the same face (Checker.Conditional.cs:991); the port's
    /// <c>GetNormalizedType</c> (Checker.Relations.Matching.cs:763) already spells it this way.
    /// </para>
    /// </remarks>
    internal void RemoveRedundantLiteralTypes(List<IType> types, TypeFlags includes, bool reduceVoidUndefined)
    {
        var i = types.Count;
        while (i > 0)
        {
            i--;
            var t = types[i];
            var flags = t.Flags;
            var remove = ((flags & (TypeFlags.StringLiteral | TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) != 0 && (includes & TypeFlags.String) != 0)
                || ((flags & TypeFlags.NumberLiteral) != 0 && (includes & TypeFlags.Number) != 0)
                || ((flags & TypeFlags.BigIntLiteral) != 0 && (includes & TypeFlags.BigInt) != 0)
                || ((flags & TypeFlags.UniqueESSymbol) != 0 && (includes & TypeFlags.ESSymbol) != 0)
                || (reduceVoidUndefined && (flags & TypeFlags.Undefined) != 0 && (includes & TypeFlags.Void) != 0)
                || (IsFreshLiteralType(t) && ContainsType(types, ((IFreshableType)t).RegularType!));
            if (remove)
            {
                Core.OrderedRemoveItemAt(types, i);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:18219-18231 — <c>removeStringLiteralsMatchedByTemplateLiterals</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>filter</c> hands the *input array* back when nothing is filtered out, so the local aliases
    /// <c>types</c> in that case exactly as upstream does; <c>Core.Filter</c> keeps that identity, which is why the
    /// result is not copied here.
    /// </remarks>
    internal void RemoveStringLiteralsMatchedByTemplateLiterals(List<IType> types)
    {
        var templates = Core.Filter(types, IsPatternLiteralType)!;
        if (templates.Count != 0)
        {
            var i = types.Count;
            while (i > 0)
            {
                i--;
                var t = types[i];
                if ((t.Flags & TypeFlags.StringLiteral) != 0 && MatchesAnyTemplate(t, templates))
                {
                    Core.OrderedRemoveItemAt(types, i);
                }
            }
        }
    }

    /// <summary>
    /// The reference's <c>some(templates, template =&gt; isTypeMatchedByTemplateLiteralOrStringMapping(t, template))</c>.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE (performance, measured): the callback captures <c>t</c>, so the reference's shape allocates a closure
    /// per (string literal × template) pair — and this loop sits in the union-construction path that runs tens of
    /// millions of times on template-literal-heavy input (docs §18.4i). The loop states the same "any element
    /// satisfies it" answer with no closure.
    /// </remarks>
    private bool MatchesAnyTemplate(IType t, IList<IType> templates)
    {
        foreach (var template in templates)
        {
            if (IsTypeMatchedByTemplateLiteralOrStringMapping(t, template))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:18233-18237 — <c>isTypeMatchedByTemplateLiteralOrStringMapping</c>.</summary>
    internal bool IsTypeMatchedByTemplateLiteralOrStringMapping(IType type, IType template)
    {
        return (template.Flags & TypeFlags.TemplateLiteral) != 0
            ? IsTypeMatchedByTemplateLiteralType(type, (TemplateLiteralType)template)
            : IsMemberOfStringMapping(type, template);
    }

    /// <summary>ported from: src/compiler/checker.ts:18239-18280 — <c>removeConstrainedTypeVariables</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>typeVariables: TypeVariable[]</c> (<c>TypeParameter | IndexedAccessType</c>) has no C# name;
    /// the list only ever holds the type variable read back out of the intersections' constituent list, so it states
    /// the common face <c>IType</c>. The <c>getBaseConstraintOfType(typeVariable)!</c> is asserted rather than checked
    /// because the intersections this function walks were created by <c>getIntersectionType</c>'s constrained-type-
    /// variable arm (:18757-18772), which marks a type variable only after <c>getBaseConstraintOfType</c> returned a
    /// constraint for it.
    /// </remarks>
    internal void RemoveConstrainedTypeVariables(List<IType> types)
    {
        var typeVariables = new List<IType>();
        // First collect a list of the type variables occurring in constraining intersections.
        foreach (var type in types)
        {
            if ((type.Flags & TypeFlags.Intersection) != 0 && (GetObjectFlags(type) & ObjectFlags.IsConstrainedTypeVariable) != 0)
            {
                var index = (((IIntersectionType)type).Types![0].Flags & TypeFlags.TypeVariable) != 0 ? 0 : 1;
                Core.PushIfUnique(typeVariables, ((IIntersectionType)type).Types![index]);
            }
        }
        // For each type variable, check if the constraining intersections for that type variable fully
        // cover the constraint of the type variable; if so, remove the constraining intersections and
        // substitute the type variable.
        foreach (var typeVariable in typeVariables)
        {
            var primitives = new List<IType>();
            // First collect the primitive types from the constraining intersections.
            foreach (var type in types)
            {
                if ((type.Flags & TypeFlags.Intersection) != 0 && (GetObjectFlags(type) & ObjectFlags.IsConstrainedTypeVariable) != 0)
                {
                    var index = (((IIntersectionType)type).Types![0].Flags & TypeFlags.TypeVariable) != 0 ? 0 : 1;
                    if (ReferenceEquals(((IIntersectionType)type).Types![index], typeVariable))
                    {
                        InsertType(primitives, ((IIntersectionType)type).Types![1 - index]);
                    }
                }
            }
            // If every constituent in the type variable's constraint is covered by an intersection of the type
            // variable and that constituent, remove those intersections and substitute the type variable.
            var constraint = Debug.CheckDefined(GetBaseConstraintOfType(typeVariable), "a constraining intersection's type variable has a base constraint");
            if (EveryType(constraint, t => ContainsType(primitives, t)))
            {
                var i = types.Count;
                while (i > 0)
                {
                    i--;
                    var type = types[i];
                    if ((type.Flags & TypeFlags.Intersection) != 0 && (GetObjectFlags(type) & ObjectFlags.IsConstrainedTypeVariable) != 0)
                    {
                        var index = (((IIntersectionType)type).Types![0].Flags & TypeFlags.TypeVariable) != 0 ? 0 : 1;
                        if (ReferenceEquals(((IIntersectionType)type).Types![index], typeVariable) && ContainsType(primitives, ((IIntersectionType)type).Types![1 - index]))
                        {
                            Core.OrderedRemoveItemAt(types, i);
                        }
                    }
                }
                InsertType(types, typeVariable);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:18282-18284 — <c>isNamedUnionType</c>.</summary>
    internal bool IsNamedUnionType(IType type)
    {
        return (type.Flags & TypeFlags.Union) != 0 && (type.AliasSymbol is not null || ((IUnionType)type).Origin is not null);
    }

    /// <summary>ported from: src/compiler/checker.ts:18286-18298 — <c>addNamedUnions</c>.</summary>
    internal void AddNamedUnions(List<IType> namedUnions, List<IType> types)
    {
        foreach (var t in types)
        {
            if ((t.Flags & TypeFlags.Union) != 0)
            {
                var origin = ((IUnionType)t).Origin;
                if (t.AliasSymbol is not null || (origin is not null && (origin.Flags & TypeFlags.Union) == 0))
                {
                    Core.PushIfUnique(namedUnions, t);
                }
                else if (origin is not null && (origin.Flags & TypeFlags.Union) != 0)
                {
                    AddNamedUnions(namedUnions, ((IUnionType)origin).Types!);
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:18300-18304 — <c>createOriginUnionOrIntersectionType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's body is <c>const result = createOriginType(flags) as UnionOrIntersectionType;
    /// result.types = types;</c> (:18301-18302) — an <c>as</c> re-view of the plain <c>Type</c> that
    /// <c>createOriginType</c> (:5531-5533) built, valid only because TS's objects carry their slots as properties:
    /// the same object then holds <c>types</c>. Here that slot is a member of this family
    /// (Model/UnionOrIntersectionType.cs:47), so the object has to be of the class the flags→class dispatch picks for
    /// these flags (<see cref="CreateTypeInstance"/>) — the only two flags this function is called with are exactly
    /// <c>TypeFlags.Union</c> (:18391 and, through <c>filterType</c>, :28547) and <c>TypeFlags.Intersection</c> (:18786)
    /// — because readers take an origin type at that class: <c>(type as UnionType).types</c> /
    /// <c>(type as IntersectionType).types</c> (checker.ts:6975-6979, NodeBuilder.Types.cs:336-337) and
    /// <c>(origin as IntersectionType).types</c> in the union cache key (checker.ts:18439-18441,
    /// Checker.Unions.cs:689). No id is assigned and <c>typeCount</c> is not advanced, which is what
    /// <c>createOriginType</c> omits as well (checker.ts:5517-5523 gives an id only in <c>createType</c>).
    /// </remarks>
    internal UnionOrIntersectionType CreateOriginUnionOrIntersectionType(TypeFlags flags, List<IType> types)
    {
        var result = (flags & TypeFlags.Intersection) != 0 ? new IntersectionType(this, flags) : (UnionOrIntersectionType)new UnionType(this, flags);
        result.Types = types;
        return result;
    }

    // ---------------------------------------------------------------------------------------------
    // getUnionType (checker.ts:18313-18397)
    // ---------------------------------------------------------------------------------------------

    // We sort and deduplicate the constituent types based on object identity. If the subtypeReduction
    // flag is specified we also reduce the constituent type set to only include types that aren't subtypes
    // of other types. Subtype reduction is expensive for large union types and is possible only when union
    // types are known not to circularly reference themselves (as is the case with union types created by
    // expression constructs such as array literals and the || and ?: operators). Named types can
    // circularly reference themselves and therefore cannot be subtype reduced during their declaration.
    // For example, "type Item = string | (() => Item)" is a named type that circularly references itself.
    /// <summary>ported from: src/compiler/checker.ts:18313-18333 — <c>getUnionType</c>.</summary>
    internal IType GetUnionType(List<IType> types, UnionReduction unionReduction = UnionReduction.Literal, ISymbol? aliasSymbol = null, List<IType>? aliasTypeArguments = null, IType? origin = null)
    {
        if (types.Count == 0)
        {
            return _neverType;
        }
        if (types.Count == 1)
        {
            return types[0];
        }
        // We optimize for the common case of unioning a union type with some other type (such as `undefined`).
        if (types.Count == 2 && origin is null && ((types[0].Flags & TypeFlags.Union) != 0 || (types[1].Flags & TypeFlags.Union) != 0))
        {
            var infix = unionReduction == UnionReduction.None ? "N" : unionReduction == UnionReduction.Subtype ? "S" : "L";
            var index = types[0].Id < types[1].Id ? 0 : 1;
            var id = types[index].Id + infix + types[1 - index].Id + GetAliasId(aliasSymbol, aliasTypeArguments);
            var type = _unionOfUnionTypes.GetValueOrDefault(id);
            if (type is null)
            {
                type = GetUnionTypeWorker(types, unionReduction, aliasSymbol, aliasTypeArguments, /*origin*/ null);
                _unionOfUnionTypes[id] = type;
            }
            return type;
        }
        return GetUnionTypeWorker(types, unionReduction, aliasSymbol, aliasTypeArguments, origin);
    }

    /// <summary>ported from: src/compiler/checker.ts:18335-18397 — <c>getUnionTypeWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares <c>typeSet: Type[] | undefined</c> because the subtype-reduction arm replaces it with
    /// <c>removeSubtypes</c>' possibly-undefined answer; the port keeps that nullable slot and relies on the
    /// null-check that follows the arm, exactly as the reference's <c>if (!typeSet)</c> does. The
    /// <c>insertType</c> loop over <c>namedUnions</c> and the second loop over the same array are TS's two
    /// <c>for</c> loops.
    /// </remarks>
    internal IType GetUnionTypeWorker(List<IType> types, UnionReduction unionReduction, ISymbol? aliasSymbol, List<IType>? aliasTypeArguments, IType? origin)
    {
        List<IType>? typeSet = [];
        var includes = AddTypesToUnion(typeSet, (TypeFlags)0, types);
        if (unionReduction != UnionReduction.None)
        {
            if ((includes & TypeFlags.AnyOrUnknown) != 0)
            {
                return (includes & TypeFlags.Any) != 0
                    ? (includes & TypeFlags.IncludesWildcard) != 0 ? _wildcardType :
                        (includes & TypeFlags.IncludesError) != 0 ? _errorType : _anyType
                    : _unknownType;
            }
            if ((includes & TypeFlags.Undefined) != 0)
            {
                // If type set contains both undefinedType and missingType, remove missingType
                if (typeSet.Count >= 2 && ReferenceEquals(typeSet[0], _undefinedType) && ReferenceEquals(typeSet[1], _missingType))
                {
                    Core.OrderedRemoveItemAt(typeSet, 1);
                }
            }
            if ((includes & (TypeFlags.Enum | TypeFlags.Literal | TypeFlags.UniqueESSymbol | TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) != 0
                || ((includes & TypeFlags.Void) != 0 && (includes & TypeFlags.Undefined) != 0))
            {
                RemoveRedundantLiteralTypes(typeSet, includes, (unionReduction & UnionReduction.Subtype) != 0);
            }
            if ((includes & TypeFlags.StringLiteral) != 0 && (includes & (TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) != 0)
            {
                RemoveStringLiteralsMatchedByTemplateLiterals(typeSet);
            }
            if ((includes & TypeFlags.IncludesConstrainedTypeVariable) != 0)
            {
                RemoveConstrainedTypeVariables(typeSet);
            }
            if (unionReduction == UnionReduction.Subtype)
            {
                typeSet = RemoveSubtypes(typeSet, (includes & TypeFlags.Object) != 0);
                if (typeSet is null)
                {
                    return _errorType;
                }
            }
            if (typeSet.Count == 0)
            {
                return (includes & TypeFlags.Null) != 0
                    ? (includes & TypeFlags.IncludesNonWideningType) != 0 ? _nullType : _nullWideningType
                    : (includes & TypeFlags.Undefined) != 0 ? (includes & TypeFlags.IncludesNonWideningType) != 0 ? _undefinedType : _undefinedWideningType
                    : _neverType;
            }
        }
        if (origin is null && (includes & TypeFlags.Union) != 0)
        {
            var namedUnions = new List<IType>();
            AddNamedUnions(namedUnions, types);
            var reducedTypes = new List<IType>();
            foreach (var t in typeSet)
            {
                if (!Core.Some(namedUnions, union => ContainsType(((IUnionType)union).Types!, t)))
                {
                    reducedTypes.Add(t);
                }
            }
            if (aliasSymbol is null && namedUnions.Count == 1 && reducedTypes.Count == 0)
            {
                return namedUnions[0];
            }
            // We create a denormalized origin type only when the union was created from one or more named unions
            // (unions with alias symbols or origins) and when there is no overlap between those named unions.
            var namedTypesCount = Core.ReduceLeft(namedUnions, (sum, union, _) => sum + ((IUnionType)union).Types!.Count, 0);
            if (namedTypesCount + reducedTypes.Count == typeSet.Count)
            {
                foreach (var t in namedUnions)
                {
                    InsertType(reducedTypes, t);
                }
                origin = CreateOriginUnionOrIntersectionType(TypeFlags.Union, reducedTypes);
            }
        }
        var objectFlags = ((includes & TypeFlags.NotPrimitiveUnion) != 0 ? ObjectFlags.None : ObjectFlags.PrimitiveUnion)
            | ((includes & TypeFlags.Intersection) != 0 ? ObjectFlags.ContainsIntersections : ObjectFlags.None);
        return GetUnionTypeFromSortedList(typeSet, objectFlags, aliasSymbol, aliasTypeArguments, origin);
    }

    // ---------------------------------------------------------------------------------------------
    // Union signatures and the sorted-list constructor (checker.ts:18399-18468)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:18399-18425 — <c>getUnionOrIntersectionTypePredicate</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS pushes <c>pred.type</c> through the narrowing its <c>This</c>/<c>Identifier</c> kind test performs;
    /// the port's slot is <c>IType?</c>, so the invariant is asserted where it is established. The two index/name
    /// arguments are read through the identifier face only when that is the kind — see the class remarks.
    /// </remarks>
    internal ITypePredicateBase? GetUnionOrIntersectionTypePredicate(List<ISignature> signatures, TypeFlags? kind)
    {
        ITypePredicateBase? last = null;
        var types = new List<IType>();
        foreach (var sig in signatures)
        {
            var pred = GetTypePredicateOfSignature(sig);
            if (pred is not null)
            {
                // Constituent type predicates must all have matching kinds. We don't create composite type predicates for assertions.
                if ((pred.Kind != TypePredicateKind.This && pred.Kind != TypePredicateKind.Identifier) || (last is not null && !TypePredicateKindsMatch(last, pred)))
                {
                    return null;
                }
                last = pred;
                types.Add(Debug.CheckDefined(pred.Type, "a `This` or `Identifier` type predicate has a type"));
            }
            else
            {
                // In composite union signatures we permit and ignore signatures with a return type `false`.
                IType? returnType = kind != TypeFlags.Intersection ? GetReturnTypeOfSignature(sig) : null;
                if (!ReferenceEquals(returnType, _falseType) && !ReferenceEquals(returnType, _regularFalseType))
                {
                    return null;
                }
            }
        }
        if (last is null)
        {
            return null;
        }
        var compositeType = GetUnionOrIntersectionType(types, kind);
        var isIdentifierKind = last.Kind == TypePredicateKind.Identifier;
        return CreateTypePredicate(
            last.Kind,
            isIdentifierKind ? ((IIdentifierTypePredicate)last).ParameterName : null,
            isIdentifierKind ? ((IIdentifierTypePredicate)last).ParameterIndex : (int?)null,
            compositeType);
    }

    /// <summary>ported from: src/compiler/checker.ts:18427-18429 — <c>typePredicateKindsMatch</c>.</summary>
    /// <remarks>PORT-NOTE: see the class remarks for why the <c>This</c> arm does not read the physical slot.</remarks>
    internal bool TypePredicateKindsMatch(ITypePredicateBase a, ITypePredicateBase b)
    {
        return a.Kind == b.Kind
            && (a.Kind is TypePredicateKind.This or TypePredicateKind.AssertsThis
                || ((IIdentifierTypePredicate)a).ParameterIndex == ((IIdentifierTypePredicate)b).ParameterIndex);
    }

    // This function assumes the constituent type list is sorted and deduplicated.
    /// <summary>ported from: src/compiler/checker.ts:18432-18459 — <c>getUnionTypeFromSortedList</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the <c>intrinsicName</c> write of the boolean-literal arm is not expressible on the port's union
    /// class and is not read anywhere — see the class remarks.
    /// </remarks>
    internal IType GetUnionTypeFromSortedList(List<IType> types, ObjectFlags precomputedObjectFlags, ISymbol? aliasSymbol = null, List<IType>? aliasTypeArguments = null, IType? origin = null)
    {
        if (types.Count == 0)
        {
            return _neverType;
        }
        if (types.Count == 1)
        {
            return types[0];
        }
        var typeKey = origin is null ? GetTypeListId(types) :
            (origin.Flags & TypeFlags.Union) != 0 ? $"|{GetTypeListId(((IUnionType)origin).Types!)}" :
            (origin.Flags & TypeFlags.Intersection) != 0 ? $"&{GetTypeListId(((IIntersectionType)origin).Types!)}" :
            $"#{((IIndexType)origin).Type!.Id}|{GetTypeListId(types)}"; // origin type id alone is insufficient, as `keyof x` may resolve to multiple WIP values while `x` is still resolving
        var id = typeKey + GetAliasId(aliasSymbol, aliasTypeArguments);
        var type = _unionTypes.GetValueOrDefault(id);
        if (type is null)
        {
            type = (IUnionType)CreateType(TypeFlags.Union);
            type.ObjectFlags = precomputedObjectFlags | GetPropagatingFlagsOfTypes(types, /*excludeKinds*/ TypeFlags.Nullable);
            type.Types = types;
            type.Origin = origin;
            type.AliasSymbol = aliasSymbol;
            type.AliasTypeArguments = aliasTypeArguments;
            if (types.Count == 2 && (types[0].Flags & TypeFlags.BooleanLiteral) != 0 && (types[1].Flags & TypeFlags.BooleanLiteral) != 0)
            {
                type.Flags |= TypeFlags.Boolean;
            }
            _unionTypes[id] = type;
        }
        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:18461-18468 — <c>getTypeFromUnionTypeNode</c>.</summary>
    internal IType GetTypeFromUnionTypeNode(UnionTypeNode node)
    {
        var links = GetNodeLinks(node);
        if (links.ResolvedType is null)
        {
            var aliasSymbol = GetAliasSymbolForTypeNode(node);
            links.ResolvedType = GetUnionType(Core.Map(node.Types, (t, _) => GetTypeFromTypeNode(t))!, UnionReduction.Literal, aliasSymbol, GetTypeArgumentsForAliasSymbol(aliasSymbol));
        }
        return links.ResolvedType!;
    }

    // ---------------------------------------------------------------------------------------------
    // Building the intersection constituent set (checker.ts:18470-18512)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:18470-18505 — <c>addTypeToIntersection</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the key is <c>type.id.toString()</c>, and the map is the ordered one
    /// <c>getIntersectionType</c> passes in — see the class remarks.
    /// </remarks>
    internal TypeFlags AddTypeToIntersection(OrderedDictionary<string, IType> typeSet, TypeFlags includes, IType type)
    {
        var flags = type.Flags;
        if ((flags & TypeFlags.Intersection) != 0)
        {
            return AddTypesToIntersection(typeSet, includes, ((IIntersectionType)type).Types!);
        }
        if (IsEmptyAnonymousObjectType(type))
        {
            if ((includes & TypeFlags.IncludesEmptyObject) == 0)
            {
                includes |= TypeFlags.IncludesEmptyObject;
                typeSet[type.Id.ToString()] = type;
            }
        }
        else
        {
            if ((flags & TypeFlags.AnyOrUnknown) != 0)
            {
                if (ReferenceEquals(type, _wildcardType)) includes |= TypeFlags.IncludesWildcard;
                if (IsErrorType(type)) includes |= TypeFlags.IncludesError;
            }
            else if (_strictNullChecks || (flags & TypeFlags.Nullable) == 0)
            {
                if (ReferenceEquals(type, _missingType))
                {
                    includes |= TypeFlags.IncludesMissingType;
                    type = _undefinedType;
                }
                if (!typeSet.ContainsKey(type.Id.ToString()))
                {
                    if ((type.Flags & TypeFlags.Unit) != 0 && (includes & TypeFlags.Unit) != 0)
                    {
                        // We have seen two distinct unit types which means we should reduce to an
                        // empty intersection. Adding TypeFlags.NonPrimitive causes that to happen.
                        includes |= TypeFlags.NonPrimitive;
                    }
                    typeSet[type.Id.ToString()] = type;
                }
            }
            includes |= flags & TypeFlags.IncludesMask;
        }
        return includes;
    }

    // Add the given types to the given type set. Order is preserved, freshness is removed from literal
    // types, duplicates are removed, and nested types of the given kind are flattened into the set.
    /// <summary>ported from: src/compiler/checker.ts:18507-18512 — <c>addTypesToIntersection</c>.</summary>
    internal TypeFlags AddTypesToIntersection(OrderedDictionary<string, IType> typeSet, TypeFlags includes, List<IType> types)
    {
        foreach (var type in types)
        {
            includes = AddTypeToIntersection(typeSet, includes, GetRegularTypeOfLiteralType(type));
        }
        return includes;
    }

    // ---------------------------------------------------------------------------------------------
    // The intersection reductions (checker.ts:18514-18638)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:18514-18532 — <c>removeRedundantSupertypes</c>.</summary>
    internal void RemoveRedundantSupertypes(List<IType> types, TypeFlags includes)
    {
        var i = types.Count;
        while (i > 0)
        {
            i--;
            var t = types[i];
            var remove = ((t.Flags & TypeFlags.String) != 0 && (includes & (TypeFlags.StringLiteral | TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) != 0)
                || ((t.Flags & TypeFlags.Number) != 0 && (includes & TypeFlags.NumberLiteral) != 0)
                || ((t.Flags & TypeFlags.BigInt) != 0 && (includes & TypeFlags.BigIntLiteral) != 0)
                || ((t.Flags & TypeFlags.ESSymbol) != 0 && (includes & TypeFlags.UniqueESSymbol) != 0)
                || ((t.Flags & TypeFlags.Void) != 0 && (includes & TypeFlags.Undefined) != 0)
                || (IsEmptyAnonymousObjectType(t) && (includes & TypeFlags.DefinitelyNonNullable) != 0);
            if (remove)
            {
                Core.OrderedRemoveItemAt(types, i);
            }
        }
    }

    // Check that the given type has a match in every union. A given type is matched by
    // an identical type, and a literal type is additionally matched by its corresponding
    // primitive type, and missingType is matched by undefinedType (and vice versa).
    /// <summary>ported from: src/compiler/checker.ts:18534-18558 — <c>eachUnionContains</c>.</summary>
    internal bool EachUnionContains(List<IUnionType> unionTypes, IType type)
    {
        foreach (var u in unionTypes)
        {
            if (!ContainsType(u.Types!, type))
            {
                if (ReferenceEquals(type, _missingType))
                {
                    return ContainsType(u.Types!, _undefinedType);
                }
                if (ReferenceEquals(type, _undefinedType))
                {
                    return ContainsType(u.Types!, _missingType);
                }
                IType? primitive = (type.Flags & TypeFlags.StringLiteral) != 0 ? _stringType :
                    (type.Flags & (TypeFlags.Enum | TypeFlags.NumberLiteral)) != 0 ? _numberType :
                    (type.Flags & TypeFlags.BigIntLiteral) != 0 ? _bigintType :
                    (type.Flags & TypeFlags.UniqueESSymbol) != 0 ? _esSymbolType :
                    null;
                if (primitive is null || !ContainsType(u.Types!, primitive))
                {
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>ported from: src/compiler/checker.ts:18560-18579 — <c>extractRedundantTemplateLiterals</c>.</summary>
    /// <remarks>
    /// Returns true if the intersection of the template literals and string literals is the empty set,
    /// for example <c>get${string}</c> &amp; "setX", and should reduce to never.
    /// </remarks>
    internal bool ExtractRedundantTemplateLiterals(List<IType> types)
    {
        var i = types.Count;
        var literals = Core.Filter(types, t => (t.Flags & TypeFlags.StringLiteral) != 0)!;
        while (i > 0)
        {
            i--;
            var t = types[i];
            if ((t.Flags & (TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) == 0) continue;
            foreach (var t2 in literals)
            {
                if (IsTypeSubtypeOf(t2, t))
                {
                    // For example, `get${T}` & "getX" is just "getX", and Lowercase<string> & "foo" is just "foo"
                    Core.OrderedRemoveItemAt(types, i);
                    break;
                }
                else if (IsPatternLiteralType(t))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:18581-18585 — <c>removeFromEach</c>.</summary>
    internal void RemoveFromEach(List<IType> types, TypeFlags flag)
    {
        for (var i = 0; i < types.Count; i++)
        {
            types[i] = FilterType(types[i], t => (t.Flags & flag) == 0);
        }
    }

    // If the given list of types contains more than one union of primitive types, replace the
    // first with a union containing an intersection of those primitive types, then remove the
    // other unions and return true. Otherwise, do nothing and return false.
    /// <summary>ported from: src/compiler/checker.ts:18590-18638 — <c>intersectUnionsOfPrimitiveTypes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>checked</c> array is <c>checkedTypes</c> here, because <c>checked</c> is a C#
    /// keyword. The reference's <c>(unionTypes || (unionTypes = [...]))</c> is <c>??=</c>: a JS array is always
    /// truthy, so the guard only ever tests for <c>undefined</c>.
    /// </remarks>
    internal bool IntersectUnionsOfPrimitiveTypes(List<IType> types)
    {
        List<IUnionType>? unionTypes = null;
        var index = Core.FindIndex(types, (t, _) => (GetObjectFlags(t) & ObjectFlags.PrimitiveUnion) != 0);
        if (index < 0)
        {
            return false;
        }
        var i = index + 1;
        // Remove all but the first union of primitive types and collect them in
        // the unionTypes array.
        while (i < types.Count)
        {
            var t = types[i];
            if ((GetObjectFlags(t) & ObjectFlags.PrimitiveUnion) != 0)
            {
                (unionTypes ??= [(IUnionType)types[index]]).Add((IUnionType)t);
                Core.OrderedRemoveItemAt(types, i);
            }
            else
            {
                i++;
            }
        }
        // Return false if there was only one union of primitive types
        if (unionTypes is null)
        {
            return false;
        }
        // We have more than one union of primitive types, now intersect them. For each
        // type in each union we check if the type is matched in every union and if so
        // we include it in the result.
        var checkedTypes = new List<IType>();
        var result = new List<IType>();
        foreach (var u in unionTypes)
        {
            foreach (var t in u.Types!)
            {
                if (InsertType(checkedTypes, t))
                {
                    if (EachUnionContains(unionTypes, t))
                    {
                        // undefinedType/missingType should always come sorted first so we leverage that here
                        if (ReferenceEquals(t, _undefinedType) && result.Count != 0 && ReferenceEquals(result[0], _missingType))
                        {
                            continue;
                        }
                        if (ReferenceEquals(t, _missingType) && result.Count != 0 && ReferenceEquals(result[0], _undefinedType))
                        {
                            result[0] = _missingType;
                            continue;
                        }
                        InsertType(result, t);
                    }
                }
            }
        }
        // Finally replace the first union with the result
        types[index] = GetUnionTypeFromSortedList(result, ObjectFlags.PrimitiveUnion);
        return true;
    }

    /// <summary>ported from: src/compiler/checker.ts:18640-18648 — <c>createIntersectionType</c>.</summary>
    internal IIntersectionType CreateIntersectionType(List<IType> types, ObjectFlags objectFlags, ISymbol? aliasSymbol = null, List<IType>? aliasTypeArguments = null)
    {
        var result = (IIntersectionType)CreateType(TypeFlags.Intersection);
        result.ObjectFlags = objectFlags | GetPropagatingFlagsOfTypes(types, /*excludeKinds*/ TypeFlags.Nullable);
        result.Types = types;
        result.AliasSymbol = aliasSymbol;
        result.AliasTypeArguments = aliasTypeArguments;
        return result;
    }

    // ---------------------------------------------------------------------------------------------
    // getIntersectionType and the cross product (checker.ts:18659-18840)
    // ---------------------------------------------------------------------------------------------

    // We normalize combinations of intersection and union types based on the distributive property of the '&'
    // operator. Specifically, because X & (A | B) is equivalent to X & A | X & B, we can transform intersection
    // types with union type constituents into equivalent union types with intersection type constituents and
    // effectively ensure that union types are always at the top level in type representations.
    //
    // We do not perform structural deduplication on intersection types. Intersection types are created only by the &
    // type operator and we can't reduce those because we want to support recursive intersection types. For example,
    // a type alias of the form "type List<T> = T & { next: List<T> }" cannot be reduced during its declaration.
    // Also, unlike union types, the order of the constituent types is preserved in order that overload resolution
    // for intersections of types with signatures can be deterministic.
    /// <summary>ported from: src/compiler/checker.ts:18659-18796 — <c>getIntersectionType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>typeMembershipMap</c> is the ordered map of the class remarks, and the constituent list is
    /// <c>arrayFrom(typeMembershipMap.values())</c> mangled into a <see cref="List{T}"/> so that the in-place
    /// <c>typeSet[typeSet.indexOf(undefinedType)] = missingType</c> write and the recursive calls keep TS's array
    /// identity. The <c>flags</c> default and the outer <c>readonly Type[]</c> follow the port's usual array mapping.
    /// </remarks>
    internal IType GetIntersectionType(List<IType> types, IntersectionFlags flags = IntersectionFlags.None, ISymbol? aliasSymbol = null, List<IType>? aliasTypeArguments = null)
    {
        var typeMembershipMap = new OrderedDictionary<string, IType>(StringComparer.Ordinal);
        var includes = AddTypesToIntersection(typeMembershipMap, (TypeFlags)0, types);
        List<IType> typeSet = [.. typeMembershipMap.Values];
        var objectFlags = ObjectFlags.None;
        // An intersection type is considered empty if it contains
        // the type never, or
        // more than one unit type or,
        // an object type and a nullable type (null or undefined), or
        // a string-like type and a type known to be non-string-like, or
        // a number-like type and a type known to be non-number-like, or
        // a symbol-like type and a type known to be non-symbol-like, or
        // a void-like type and a type known to be non-void-like, or
        // a non-primitive type and a type known to be primitive.
        if ((includes & TypeFlags.Never) != 0)
        {
            return Core.Contains(typeSet, _silentNeverType) ? _silentNeverType : _neverType;
        }
        if (
            _strictNullChecks && (includes & TypeFlags.Nullable) != 0 && (includes & (TypeFlags.Object | TypeFlags.NonPrimitive | TypeFlags.IncludesEmptyObject)) != 0 ||
            (includes & TypeFlags.NonPrimitive) != 0 && (includes & (TypeFlags.DisjointDomains & ~TypeFlags.NonPrimitive)) != 0 ||
            (includes & TypeFlags.StringLike) != 0 && (includes & (TypeFlags.DisjointDomains & ~TypeFlags.StringLike)) != 0 ||
            (includes & TypeFlags.NumberLike) != 0 && (includes & (TypeFlags.DisjointDomains & ~TypeFlags.NumberLike)) != 0 ||
            (includes & TypeFlags.BigIntLike) != 0 && (includes & (TypeFlags.DisjointDomains & ~TypeFlags.BigIntLike)) != 0 ||
            (includes & TypeFlags.ESSymbolLike) != 0 && (includes & (TypeFlags.DisjointDomains & ~TypeFlags.ESSymbolLike)) != 0 ||
            (includes & TypeFlags.VoidLike) != 0 && (includes & (TypeFlags.DisjointDomains & ~TypeFlags.VoidLike)) != 0
        )
        {
            return _neverType;
        }
        if ((includes & (TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) != 0 && (includes & TypeFlags.StringLiteral) != 0 && ExtractRedundantTemplateLiterals(typeSet))
        {
            return _neverType;
        }
        if ((includes & TypeFlags.Any) != 0)
        {
            return (includes & TypeFlags.IncludesWildcard) != 0 ? _wildcardType : (includes & TypeFlags.IncludesError) != 0 ? _errorType : _anyType;
        }
        if (!_strictNullChecks && (includes & TypeFlags.Nullable) != 0)
        {
            return (includes & TypeFlags.IncludesEmptyObject) != 0 ? _neverType : (includes & TypeFlags.Undefined) != 0 ? _undefinedType : _nullType;
        }
        if (
            (includes & TypeFlags.String) != 0 && (includes & (TypeFlags.StringLiteral | TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) != 0 ||
            (includes & TypeFlags.Number) != 0 && (includes & TypeFlags.NumberLiteral) != 0 ||
            (includes & TypeFlags.BigInt) != 0 && (includes & TypeFlags.BigIntLiteral) != 0 ||
            (includes & TypeFlags.ESSymbol) != 0 && (includes & TypeFlags.UniqueESSymbol) != 0 ||
            (includes & TypeFlags.Void) != 0 && (includes & TypeFlags.Undefined) != 0 ||
            (includes & TypeFlags.IncludesEmptyObject) != 0 && (includes & TypeFlags.DefinitelyNonNullable) != 0
        )
        {
            if ((flags & IntersectionFlags.NoSupertypeReduction) == 0) RemoveRedundantSupertypes(typeSet, includes);
        }
        if ((includes & TypeFlags.IncludesMissingType) != 0)
        {
            typeSet[typeSet.IndexOf(_undefinedType)] = _missingType;
        }
        if (typeSet.Count == 0)
        {
            return _unknownType;
        }
        if (typeSet.Count == 1)
        {
            return typeSet[0];
        }
        if (typeSet.Count == 2 && (flags & IntersectionFlags.NoConstraintReduction) == 0)
        {
            var typeVarIndex = (typeSet[0].Flags & TypeFlags.TypeVariable) != 0 ? 0 : 1;
            var typeVariable = typeSet[typeVarIndex];
            var primitiveType = typeSet[1 - typeVarIndex];
            if (
                (typeVariable.Flags & TypeFlags.TypeVariable) != 0 &&
                ((primitiveType.Flags & (TypeFlags.Primitive | TypeFlags.NonPrimitive)) != 0 && !IsGenericStringLikeType(primitiveType) || (includes & TypeFlags.IncludesEmptyObject) != 0)
            )
            {
                // We have an intersection T & P or P & T, where T is a type variable and P is a primitive type, the object type, or {}.
                var constraint = GetBaseConstraintOfType(typeVariable);
                // Check that T's constraint is similarly composed of primitive types, the object type, or {}.
                if (constraint is not null && EveryType(constraint, t => (t.Flags & (TypeFlags.Primitive | TypeFlags.NonPrimitive)) != 0 || IsEmptyAnonymousObjectType(t)))
                {
                    // If T's constraint is a subtype of P, simply return T. For example, given `T extends "a" | "b"`,
                    // the intersection `T & string` reduces to just T.
                    if (IsTypeStrictSubtypeOf(constraint, primitiveType))
                    {
                        return typeVariable;
                    }
                    if (!((constraint.Flags & TypeFlags.Union) != 0 && SomeType(constraint, c => IsTypeStrictSubtypeOf(c, primitiveType))))
                    {
                        // No constituent of T's constraint is a subtype of P. If P is also not a subtype of T's constraint,
                        // then the constraint and P are unrelated, and the intersection reduces to never. For example, given
                        // `T extends "a" | "b"`, the intersection `T & number` reduces to never.
                        if (!IsTypeStrictSubtypeOf(primitiveType, constraint))
                        {
                            return _neverType;
                        }
                    }
                    // Some constituent of T's constraint is a subtype of P, or P is a subtype of T's constraint. Thus,
                    // the intersection further constrains the type variable. For example, given `T extends string | number`,
                    // the intersection `T & "a"` is marked as a constrained type variable. Likewise, given `T extends "a" | 1`,
                    // the intersection `T & number` is marked as a constrained type variable.
                    objectFlags = ObjectFlags.IsConstrainedTypeVariable;
                }
            }
        }
        var id = GetTypeListId(typeSet) + ((flags & IntersectionFlags.NoConstraintReduction) != 0 ? "*" : GetAliasId(aliasSymbol, aliasTypeArguments));
        var result = _intersectionTypes.GetValueOrDefault(id);
        if (result is null)
        {
            if ((includes & TypeFlags.Union) != 0)
            {
                if (IntersectUnionsOfPrimitiveTypes(typeSet))
                {
                    // When the intersection creates a reduced set (which might mean that *all* union types have
                    // disappeared), we restart the operation to get a new set of combined flags. Once we have
                    // reduced we'll never reduce again, so this occurs at most once.
                    result = GetIntersectionType(typeSet, flags, aliasSymbol, aliasTypeArguments);
                }
                else if (Core.Every(typeSet, (t, _) => (t.Flags & TypeFlags.Union) != 0 && (((IUnionType)t).Types![0].Flags & TypeFlags.Undefined) != 0))
                {
                    var containedUndefinedType = Core.Some(typeSet, ContainsMissingType) ? _missingType : _undefinedType;
                    RemoveFromEach(typeSet, TypeFlags.Undefined);
                    result = GetUnionType([GetIntersectionType(typeSet, flags), containedUndefinedType], UnionReduction.Literal, aliasSymbol, aliasTypeArguments);
                }
                else if (Core.Every(typeSet, (t, _) => (t.Flags & TypeFlags.Union) != 0 && ((((IUnionType)t).Types![0].Flags & TypeFlags.Null) != 0 || (((IUnionType)t).Types![1].Flags & TypeFlags.Null) != 0)))
                {
                    RemoveFromEach(typeSet, TypeFlags.Null);
                    result = GetUnionType([GetIntersectionType(typeSet, flags), _nullType], UnionReduction.Literal, aliasSymbol, aliasTypeArguments);
                }
                else if (typeSet.Count >= 3 && types.Count > 2)
                {
                    // When we have three or more constituents, more than two inputs (to head off infinite reexpansion), some of which are unions, we employ a "divide and conquer" strategy
                    // where A & B & C & D is processed as (A & B) & (C & D). Since intersections of unions often produce far smaller
                    // unions of intersections than the full cartesian product (due to some intersections becoming `never`), this can
                    // dramatically reduce the overall work.
                    var middle = (int)Math.Floor(typeSet.Count / 2d);
                    result = GetIntersectionType([GetIntersectionType([.. typeSet.Take(middle)], flags), GetIntersectionType([.. typeSet.Skip(middle)], flags)], flags, aliasSymbol, aliasTypeArguments);
                }
                else
                {
                    // We are attempting to construct a type of the form X & (A | B) & (C | D). Transform this into a type of
                    // the form X & A & C | X & A & D | X & B & C | X & B & D. If the estimated size of the resulting union type
                    // exceeds 100000 constituents, report an error.
                    if (!CheckCrossProductUnion(typeSet))
                    {
                        return _errorType;
                    }
                    var constituents = GetCrossProductIntersections(typeSet, flags);
                    // We attach a denormalized origin type when at least one constituent of the cross-product union is an
                    // intersection (i.e. when the intersection didn't just reduce one or more unions to smaller unions) and
                    // the denormalized origin has fewer constituents than the union itself.
                    IType? origin = Core.Some(constituents, t => (t.Flags & TypeFlags.Intersection) != 0) && GetConstituentCountOfTypes(constituents) > GetConstituentCountOfTypes(typeSet)
                        ? CreateOriginUnionOrIntersectionType(TypeFlags.Intersection, typeSet)
                        : null;
                    result = GetUnionType(constituents, UnionReduction.Literal, aliasSymbol, aliasTypeArguments, origin);
                }
            }
            else
            {
                result = CreateIntersectionType(typeSet, objectFlags, aliasSymbol, aliasTypeArguments);
            }
            _intersectionTypes[id] = result;
        }
        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:18798-18800 — <c>getCrossProductUnionSize</c>.</summary>
    internal int GetCrossProductUnionSize(List<IType> types)
    {
        return Core.ReduceLeft(types, (n, t, _) => (t.Flags & TypeFlags.Union) != 0 ? n * ((IUnionType)t).Types!.Count : (t.Flags & TypeFlags.Never) != 0 ? 0 : n, 1);
    }

    /// <summary>ported from: src/compiler/checker.ts:18802-18810 — <c>checkCrossProductUnion</c>.</summary>
    internal bool CheckCrossProductUnion(List<IType> types)
    {
        var size = GetCrossProductUnionSize(types);
        if (size >= 100000)
        {
            Tracing.Current?.Instant(
                Tracing.Phase.CheckTypes,
                "checkCrossProductUnion_DepthLimit",
                TracingArgs.Of(("typeIds", TracingValue.Array(types.Select(t => TracingValue.From(t.Id)))), ("size", TracingValue.From(size))));
            Error(_currentNode, Diagnostics.Expression_produces_a_union_type_that_is_too_complex_to_represent);
            return false;
        }
        return true;
    }

    /// <summary>ported from: src/compiler/checker.ts:18812-18830 — <c>getCrossProductIntersections</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference already walks the cartesian product with an explicit counter rather than recursion; the
    /// port keeps that shape statement for statement, including <c>types.slice()</c> as a fresh
    /// <see cref="List{T}"/> and <c>Math.floor(n / length)</c> as the same floor (both operands are non-negative, so
    /// integer division is the same value — it is spelled out to keep the division visibly a floor).
    /// </remarks>
    internal List<IType> GetCrossProductIntersections(List<IType> types, IntersectionFlags flags)
    {
        var count = GetCrossProductUnionSize(types);
        var intersections = new List<IType>();
        for (var i = 0; i < count; i++)
        {
            var constituents = new List<IType>(types);
            var n = i;
            for (var j = types.Count - 1; j >= 0; j--)
            {
                if ((types[j].Flags & TypeFlags.Union) != 0)
                {
                    var sourceTypes = ((IUnionType)types[j]).Types!;
                    var length = sourceTypes.Count;
                    constituents[j] = sourceTypes[n % length];
                    n = (int)Math.Floor((double)n / length);
                }
            }
            var t = GetIntersectionType(constituents, flags);
            if ((t.Flags & TypeFlags.Never) == 0) intersections.Add(t);
        }
        return intersections;
    }

    /// <summary>ported from: src/compiler/checker.ts:18832-18836 — <c>getConstituentCount</c>.</summary>
    internal int GetConstituentCount(IType type)
    {
        return (type.Flags & TypeFlags.UnionOrIntersection) == 0 || type.AliasSymbol is not null ? 1 :
            (type.Flags & TypeFlags.Union) != 0 && ((IUnionType)type).Origin is not null ? GetConstituentCount(((IUnionType)type).Origin!) :
            GetConstituentCountOfTypes(((IUnionOrIntersectionType)type).Types!);
    }

    /// <summary>ported from: src/compiler/checker.ts:18838-18840 — <c>getConstituentCountOfTypes</c>.</summary>
    internal int GetConstituentCountOfTypes(List<IType> types)
    {
        return Core.ReduceLeft(types, (n, t, _) => n + GetConstituentCount(t), 0);
    }

    /// <summary>ported from: src/compiler/checker.ts:18842-18857 — <c>getTypeFromIntersectionTypeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>types.indexOf(emptyTypeLiteralType)</c> is <c>List.IndexOf</c>, which compares the
    /// <see cref="Type"/> instances by reference exactly as <c>===</c> does (<c>Type</c> declares no <c>Equals</c>).
    /// </remarks>
    internal IType GetTypeFromIntersectionTypeNode(IntersectionTypeNode node)
    {
        var links = GetNodeLinks(node);
        if (links.ResolvedType is null)
        {
            var aliasSymbol = GetAliasSymbolForTypeNode(node);
            var types = Core.Map(node.Types, (t, _) => GetTypeFromTypeNode(t))!;
            // We perform no supertype reduction for X & {} or {} & X, where X is one of string, number, bigint,
            // or a pattern literal template type. This enables union types like "a" | "b" | string & {} or
            // "aa" | "ab" | `a${string}` which preserve the literal types for purposes of statement completion.
            var emptyIndex = types.Count == 2 ? types.IndexOf(_emptyTypeLiteralType) : -1;
            var t = emptyIndex >= 0 ? types[1 - emptyIndex] : _unknownType;
            var noSupertypeReduction = (t.Flags & (TypeFlags.String | TypeFlags.Number | TypeFlags.BigInt)) != 0 || ((t.Flags & TypeFlags.TemplateLiteral) != 0 && IsPatternLiteralType(t));
            links.ResolvedType = GetIntersectionType(types, noSupertypeReduction ? IntersectionFlags.NoSupertypeReduction : IntersectionFlags.None, aliasSymbol, GetTypeArgumentsForAliasSymbol(aliasSymbol));
        }
        return links.ResolvedType!;
    }
}
