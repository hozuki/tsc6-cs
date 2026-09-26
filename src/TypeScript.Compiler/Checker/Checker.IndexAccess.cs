namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:18858-19731 (createIndexType … getTypeFromIndexedAccessTypeNode)
// ported from: src/compiler/checker.ts:2056-2058 (indexedAccessTypes, templateLiteralTypes, stringMappingTypes)
/// <summary>
/// The index-type block of the checker — TS's <c>keyof</c> (<c>TypeFlags.Index</c>), template literal
/// (<c>TypeFlags.TemplateLiteral</c>), intrinsic string-mapping (<c>TypeFlags.StringMapping</c>) and indexed access
/// (<c>TypeFlags.IndexedAccess</c>) machinery (checker.ts:18858-19731).
/// </summary>
/// <remarks>
/// <para>
/// The four families in this file share one theme: they are all <em>deferred</em> views of a key set or a member type.
/// <c>getIndexType</c> answers <c>keyof T</c> (eager for a concrete <c>T</c>, cached on the type as an
/// <c>IndexType</c> when <c>T</c> can still be instantiated), <c>getTemplateLiteralType</c>/
/// <c>getStringMappingType</c> fold literal text and mappings over instantiations, and
/// <c>getIndexedAccessType</c> either resolves <c>T[K]</c> to the selected property's type or defers it as an
/// <c>IndexedAccessType</c>.
/// </para>
/// <para>
/// PORT-NOTE (<c>readonly T[]</c>/<c>Map</c>): TS's <c>readonly Type[]</c> parameters are <c>List&lt;IType&gt;</c> and
/// <c>readonly string[]</c> are <c>List&lt;string&gt;</c> (§5's collection mapping); TS's array combinators
/// (<c>map</c>, <c>some</c>, <c>every</c>, <c>findIndex</c>, <c>contains</c>, <c>slice</c>, <c>replaceElement</c>) are
/// the port's <c>Core</c> statics with an extra index parameter on the callback, and a <c>!</c> follows them where
/// TS's result is a total array and <c>Core</c>'s is <c>List&lt;T&gt;?</c>. TS's type combinators on <c>Type</c>
/// (<c>mapType</c>, <c>filterType</c>, <c>forEachType</c>, <c>everyType</c>, <c>someType</c>) are the checker members
/// <c>MapType</c>/<c>FilterType</c>/… (M6Unions/M6Generics).
/// </para>
/// <para>
/// PORT-NOTE (interfaces, not classes): a TS type face is a structural interface upstream, so every parameter and
/// every <c>as</c> cast in this file uses the model's interface — <c>IIndexType</c>, <c>IIndexedAccessType</c>,
/// <c>ITemplateLiteralType</c>, <c>IStringMappingType</c>, <c>IConditionalType</c>, <c>IMappedType</c>,
/// <c>ISubstitutionType</c>, <c>IUnionType</c>, <c>IIntersectionType</c>, <c>IUnionOrIntersectionType</c>,
/// <c>ILiteralType</c>, <c>IUniqueESSymbolType</c> — exactly as the instantiation slice does
/// (<c>Checker.Instantiation.cs</c>). A node union (<c>PropertyName | JsxAttributeName | ElementAccessExpression | …</c>)
/// collapses to <c>Node</c> (§12.5), and a node-kind test is <c>NodeTests.IsXxx</c> (§12.9).
/// </para>
/// <para>
/// PORT-NOTE (nested functions): TS declares <c>addMemberForKeyType</c>, <c>addSpans</c> and
/// <c>errorIfWritingToReadonlyIndex</c> as nested functions that close over the enclosing function's locals — and
/// <c>addSpans</c> even shadows <c>texts</c>/<c>types</c>. The port keeps them as <em>local functions</em> in place,
/// which is the same closure and the same shadowing (C# permits the shadowing for a local function's parameters, as
/// <c>Parser.Declarations.cs</c> does elsewhere in the port); turning them into members would have to thread the
/// captured locals through parameters.
/// </para>
/// <para>
/// PORT-NOTE (truthiness): a JS truthiness test on a <c>Type</c>/<c>Symbol</c>/node value is
/// <c>is not null</c>/<c>is { } x</c> where the value is used afterwards — a non-null object is always truthy, so the
/// two are the same test — and <c>Js.IsTruthy(…)</c> where the value is only tested. Tests on a <c>string</c>,
/// <c>number</c>, or a flags enum are always <c>Js.IsTruthy(…)</c>/<c>!= 0</c>, where falsiness has content.
/// </para>
/// <para>
/// PORT-NOTE (generated slots): the model's node and type slots are nullable where TS's are total (the generator
/// cannot express TS's non-optional members), so a read TS's own types make total carries <c>!</c> — the same
/// treatment the binder and parser give them (<c>Binder.Expando.cs:194</c>), with the invariant named at each site.
/// A read whose invariant is not local is <c>Debug.CheckDefined</c>.
/// </para>
/// <para>
/// PORT-NOTE (string case folding): TS's <c>str.toUpperCase()</c>/<c>toLowerCase()</c> are
/// <see cref="IcuCaseFolding.ToUpper"/>/<see cref="IcuCaseFolding.ToLower"/> (the port's Unicode full-case-mapping
/// entry points), <c>str.charAt(0)</c> is a one-code-unit substring, <c>str.slice(1)</c> is a range, and the JS
/// string coercion <c>"" + number</c> is <see cref="Js.NumberToString"/>.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // The three caches these members own (checker.ts:2056-2058)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:2056 — <c>indexedAccessTypes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: declared here although its declaration line lies outside this slice's range — and outside every M6
    /// slice's range, since the state block at :2049-2064 belongs to no slice — because
    /// <c>getIndexedAccessTypeOrUndefined</c> (:19682-19687) is its only reader and writer. That is the same ruling
    /// <c>Checker.TypeReferences.cs</c> records for <c>_tupleTypes</c>. TS's <c>Map&lt;string, IndexedAccessType&gt;</c>
    /// is keyed by the <c>`${objectType.id},${indexType.id},${persistentAccessFlags}${aliasId}`</c> string built at the
    /// deferral site and is never iterated, so §12.3's plain <c>Dictionary</c> mapping (not the ordered
    /// <c>SymbolTable</c> one) carries it exactly.
    /// </remarks>
    internal readonly Dictionary<string, IIndexedAccessType> _indexedAccessTypes = [];

    /// <summary>ported from: src/compiler/checker.ts:2057 — <c>templateLiteralTypes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: declared here for the same reason as <see cref="_indexedAccessTypes"/> —
    /// <c>getTemplateLiteralType</c> (:19069-19073) is its only reader. The key is the
    /// <c>`${typeListId}|${textLengths}|${texts}`</c> string built there; lookups never depend on map order.
    /// </remarks>
    internal readonly Dictionary<string, ITemplateLiteralType> _templateLiteralTypes = [];

    /// <summary>ported from: src/compiler/checker.ts:2058 — <c>stringMappingTypes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: declared here for the same reason as <see cref="_indexedAccessTypes"/> —
    /// <c>getStringMappingTypeForGenericType</c> (:19157-19161) is its only reader. The key is
    /// <c>`${getSymbolId(symbol)},${getTypeId(type)}`</c>, i.e. object identities spelled as ids, exactly what TS's
    /// identity-keyed <c>Map</c> holds.
    /// </remarks>
    internal readonly Dictionary<string, IStringMappingType> _stringMappingTypes = [];

    // ---------------------------------------------------------------------------------------------
    // The index types (checker.ts:18858-19005)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:18858-18863 — <c>createIndexType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>InstantiableType | UnionOrIntersectionType</c> (types.ts:6922), whose
    /// two faces are siblings here; the port takes <see cref="IType"/> and stores it in the <c>IIndexType</c> slot
    /// (<c>IndexType.type</c> is <c>IType</c> in the model — see <c>Model/InstantiableType.cs</c>).
    /// </remarks>
    internal IIndexType CreateIndexType(IType type, IndexFlags indexFlags)
    {
        var result = (IIndexType)CreateType(TypeFlags.Index);
        result.Type = type;
        result.IndexFlags = indexFlags;
        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:18865-18869 — <c>createOriginIndexType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's body is <c>const result = createOriginType(TypeFlags.Index) as IndexType;
    /// result.type = type;</c> (:18866-18867) — again an <c>as</c> re-view of the plain <c>Type</c>
    /// <c>createOriginType</c> (:5531-5533) built, so the object must here be the class the flags→class dispatch picks
    /// for <c>TypeFlags.Index</c> (<see cref="CreateTypeInstance"/>): its sole caller passes the origin as the
    /// <c>origin</c> of a union (checker.ts:18968, Checker.IndexAccess.cs:328), where the union cache key reads
    /// <c>(origin as IndexType).type.id</c> (checker.ts:18441, Checker.Unions.cs:690). <c>createOriginType</c> leaves
    /// <c>id</c> at 0 (:5517-5523 gives an id only in <c>createType</c>), so an origin index type is not a key into any
    /// id-keyed map and never enters <c>typeCount</c>. <c>indexFlags</c> is not written, as upstream
    /// (:18865-18869): the slot keeps its default, and TS's absent property reads as 0 in the bit tests
    /// (<c>IndexFlags.None</c>, Model/IndexFlags.cs:15). The union parameter is widened as in
    /// <see cref="CreateIndexType"/>.
    /// </remarks>
    internal IIndexType CreateOriginIndexType(IType type)
    {
        var result = new IndexType(this, TypeFlags.Index);
        result.Type = type;
        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:18871-18875 — <c>getIndexTypeForGenericType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>InstantiableType | UnionOrIntersectionType</c> and both faces declare the same
    /// two cache slots (<c>resolvedIndexType</c>/<c>resolvedStringIndexType</c>, types.ts:6734-6736 and :6857-6859).
    /// C# has no union and the two classes are siblings, so the port matches on the two interfaces that carry the
    /// slots. Every TS call site passes a type parameter, an index/conditional/substitution/template-literal/
    /// string-mapping type, a mapped type, or a union/intersection, so one arm always matches; the fallthrough is the
    /// only way TS's own casts could be violated, hence <c>Debug.Fail</c> rather than a silent <c>null</c>.
    /// </remarks>
    internal IIndexType GetIndexTypeForGenericType(IType type, IndexFlags indexFlags)
    {
        var stringsOnly = (indexFlags & IndexFlags.StringsOnly) != 0;
        if (type is IInstantiableType instantiable)
        {
            return stringsOnly
                ? instantiable.ResolvedStringIndexType ??= CreateIndexType(type, IndexFlags.StringsOnly)
                : instantiable.ResolvedIndexType ??= CreateIndexType(type, IndexFlags.None);
        }
        if (type is IUnionOrIntersectionType unionOrIntersection)
        {
            return stringsOnly
                ? unionOrIntersection.ResolvedStringIndexType ??= CreateIndexType(type, IndexFlags.StringsOnly)
                : unionOrIntersection.ResolvedIndexType ??= CreateIndexType(type, IndexFlags.None);
        }
        return Debug.Fail<IIndexType>("getIndexTypeForGenericType's type must be an InstantiableType or a UnionOrIntersectionType");
    }

    /// <summary>ported from: src/compiler/checker.ts:18884-18926 — <c>getIndexTypeForMappedType</c>.</summary>
    /// <remarks>
    /// <para>
    /// This roughly mirrors <c>resolveMappedTypeMembers</c> in the nongeneric case, except that it only reports the
    /// union of the keys it computed rather than manufacturing the properties: fetching the <c>constraintType</c>
    /// outright would ignore the mappings, and mapping the <c>constraintType</c> directly would ignore that mapped
    /// types map <em>properties</em> rather than keys (thus dropping subtype reduction in the constraint type when one
    /// is possible).
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>getMappedTypeNameTypeKind</c>'s sibling readers are outside this slice, so
    /// <c>GetTypeParameterFromMappedType</c>/<c>GetConstraintTypeFromMappedType</c>/<c>GetNameTypeFromMappedType</c>/
    /// <c>GetModifiersTypeFromMappedType</c> are the integrator's (Checker.Support.cs) and
    /// <c>ForEachType</c>/<c>ForEachMappedTypePropertyKeyTypeAndIndexSignatureKeyType</c>/<c>FilterType</c>/
    /// <c>GetLowerBoundOfKeyType</c>/<c>InstantiateType</c>/<c>AppendTypeMapping</c>/<c>GetTypeListId</c> are sibling
    /// references; this file declares none of them. The nested callback is a <c>Action&lt;IType&gt;</c>, the shape TS's
    /// <c>(keyType: Type) =&gt; void</c> has once a C# port drops <c>forEachType</c>'s generic return; the two
    /// <c>forEachType</c> call sites adapt it with <c>ForEachType&lt;object&gt;(…, t =&gt; { …; return null; })</c>, the shape
    /// the port's other void callbacks use, because <c>ForEachType&lt;TU&gt;</c> takes a <c>Func&lt;IType, TU?&gt;</c>.
    /// </para>
    /// </remarks>
    internal IType GetIndexTypeForMappedType(IMappedType type, IndexFlags indexFlags)
    {
        var typeParameter = GetTypeParameterFromMappedType(type);
        var constraintType = GetConstraintTypeFromMappedType(type);
        var nameType = GetNameTypeFromMappedType(type.Target as IMappedType ?? type);
        if (nameType is null && (indexFlags & IndexFlags.NoIndexSignatures) == 0)
        {
            // no mapping and no filtering required, just quickly bail to returning the constraint in the common case
            return constraintType;
        }
        var keyTypes = new List<IType>();
        // Calling getApparentType on the `T` of a `keyof T` in the constraint type of a generic mapped type can
        // trigger a circularity. For example, `T extends { [P in keyof T & string as Captitalize<P>]: any }` is
        // a circular definition. For this reason, we only eagerly manifest the keys if the constraint is non-generic.
        if (IsGenericIndexType(constraintType))
        {
            if (IsMappedTypeWithKeyofConstraintDeclaration(type))
            {
                // We have a generic index and a homomorphic mapping and a key remapping - we need to defer
                // the whole `keyof whatever` for later since it's not safe to resolve the shape of modifier type.
                return GetIndexTypeForGenericType(type, indexFlags);
            }
            // Include the generic component in the resulting type.
            ForEachType<object>(constraintType, t => { AddMemberForKeyType(t); return null; });
        }
        else if (IsMappedTypeWithKeyofConstraintDeclaration(type))
        {
            var modifiersType = GetApparentType(GetModifiersTypeFromMappedType(type)); // The 'T' in 'keyof T'
            ForEachMappedTypePropertyKeyTypeAndIndexSignatureKeyType(modifiersType, TypeFlags.StringOrNumberLiteralOrUnique, (indexFlags & IndexFlags.StringsOnly) != 0, AddMemberForKeyType);
        }
        else
        {
            ForEachType<object>(GetLowerBoundOfKeyType(constraintType), t => { AddMemberForKeyType(t); return null; });
        }
        // We had to pick apart the constraintType to potentially map/filter it - compare the final resulting list with the
        // original constraintType, so we can return the union that preserves aliases/origin data if possible.
        var result = (indexFlags & IndexFlags.NoIndexSignatures) != 0
            ? FilterType(GetUnionType(keyTypes), t => (t.Flags & (TypeFlags.Any | TypeFlags.String)) == 0)
            : GetUnionType(keyTypes);
        if ((result.Flags & TypeFlags.Union) != 0 && (constraintType.Flags & TypeFlags.Union) != 0
            && GetTypeListId(((IUnionType)result).Types) == GetTypeListId(((IUnionType)constraintType).Types))
        {
            return constraintType;
        }
        return result;

        void AddMemberForKeyType(IType keyType)
        {
            var propNameType = nameType is not null
                ? InstantiateType(nameType, AppendTypeMapping((TypeMapper?)type.Mapper, typeParameter, keyType))!
                : keyType;
            // `keyof` currently always returns `string | number` for concrete `string` index signatures - the below ternary keeps that behavior for mapped types
            // See `getLiteralTypeFromProperties` where there's a similar ternary to cause the same behavior.
            keyTypes.Add(ReferenceEquals(propNameType, _stringType) ? _stringOrNumberType : propNameType);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:18928-18946 — <c>getLiteralTypeFromPropertyName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter union <c>PropertyName | JsxAttributeName</c> collapses to <see cref="Node"/> (§12.5);
    /// the node tests are <see cref="NodeTests"/>'s and the name helpers are <c>Utilities</c>' (utilities.ts:5397).
    /// </remarks>
    internal IType GetLiteralTypeFromPropertyName(Node name)
    {
        if (NodeTests.IsPrivateIdentifier(name))
        {
            return _neverType;
        }
        if (NodeTests.IsNumericLiteral(name))
        {
            return GetRegularTypeOfLiteralType(CheckExpression(name));
        }
        if (NodeTests.IsComputedPropertyName(name))
        {
            return GetRegularTypeOfLiteralType(CheckComputedPropertyName((ComputedPropertyName)name));
        }
        var propertyName = Utilities.GetPropertyNameForPropertyNameNode(name);
        if (propertyName is not null)
        {
            return GetStringLiteralType(Utilities.UnescapeLeadingUnderscores(propertyName));
        }
        if (Utilities.IsExpression(name))
        {
            return GetRegularTypeOfLiteralType(CheckExpression(name));
        }
        return _neverType;
    }

    /// <summary>ported from: src/compiler/checker.ts:18948-18961 — <c>getLiteralTypeFromProperty</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>name &amp;&amp; getLiteralTypeFromPropertyName(name) || (!isKnownSymbol(prop) ? … : undefined)</c>
    /// chain is written as the same three branches in the same order — the call is hoisted into <c>nameType</c> so it
    /// runs once, because a returned <c>Type</c> (including <c>neverType</c>) is truthy and therefore always short-circuits
    /// the fallback exactly as it does upstream.
    /// </remarks>
    internal IType GetLiteralTypeFromProperty(ISymbol prop, TypeFlags include, bool includeNonPublic = false)
    {
        if (includeNonPublic || (Utilities.GetDeclarationModifierFlagsFromSymbol(prop) & ModifierFlags.NonPublicAccessibilityModifier) == 0)
        {
            var type = GetSymbolLinks(GetLateBoundSymbol(prop)).NameType;
            if (type is null)
            {
                var name = Utilities.GetNameOfDeclaration(prop.ValueDeclaration as Node) as Node;
                IType? nameType = null;
                if (name is not null)
                {
                    nameType = GetLiteralTypeFromPropertyName(name);
                }
                type = prop.EscapedName == InternalSymbolName.Default.Value
                    ? GetStringLiteralType("default")
                    : name is not null
                        ? nameType
                        : Utilities.IsKnownSymbol(prop)
                            ? null
                            : GetStringLiteralType(Utilities.SymbolName(prop));
            }
            if (type is not null && (type.Flags & include) != 0)
            {
                return type;
            }
        }
        return _neverType;
    }

    /// <summary>ported from: src/compiler/checker.ts:18963-18965 — <c>isKeyTypeIncluded</c>.</summary>
    internal bool IsKeyTypeIncluded(IType keyType, TypeFlags include)
    {
        return (keyType.Flags & include) != 0
            || ((keyType.Flags & TypeFlags.Intersection) != 0 && Core.Some(((IIntersectionType)keyType).Types, t => IsKeyTypeIncluded(t, include)));
    }

    /// <summary>ported from: src/compiler/checker.ts:18967-18974 — <c>getLiteralTypeFromProperties</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>concatenate(propertyTypes, indexKeyTypes)</c> is the collection expression
    /// <c>[.. propertyTypes, .. indexKeyTypes]</c>: both are total lists (the <c>map</c>s cannot miss for a
    /// non-<c>null</c> input) and the result is a fresh list, as <c>concatenate</c>'s is. The identity tests are
    /// <c>ReferenceEquals</c> — <c>enumNumberIndexInfo</c>/<c>stringType</c> are canonical objects, which is exactly
    /// what TS's <c>===</c> compares.
    /// </remarks>
    internal IType GetLiteralTypeFromProperties(IType type, TypeFlags include, bool includeOrigin)
    {
        var origin = includeOrigin && (((GetObjectFlags(type) & (ObjectFlags.ClassOrInterface | ObjectFlags.Reference)) != 0) || type.AliasSymbol is not null)
            ? CreateOriginIndexType(type)
            : null;
        var propertyTypes = Debug.CheckDefined(Core.Map(GetPropertiesOfType(type), (prop, _) => GetLiteralTypeFromProperty(prop, include)));
        var indexKeyTypes = Debug.CheckDefined(Core.Map(GetIndexInfosOfType(type), (info, _) =>
            !ReferenceEquals(info, _enumNumberIndexInfo) && IsKeyTypeIncluded(info.KeyType!, include) ?
                ReferenceEquals(info.KeyType, _stringType) && (include & TypeFlags.Number) != 0 ? _stringOrNumberType : info.KeyType! :
                _neverType));
        return GetUnionType([.. propertyTypes, .. indexKeyTypes], UnionReduction.Literal, /*aliasSymbol*/ null, /*aliasTypeArguments*/ null, origin);
    }

    /// <summary>ported from: src/compiler/checker.ts:18976-18982 — <c>shouldDeferIndexType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>&amp;&amp;</c>/<c>||</c> chain of numbers and booleans is written as the same tests with
    /// explicit flag comparisons; <c>&&</c> binds tighter than <c>||</c> in both languages, so the operand grouping is
    /// the reference's.
    /// </remarks>
    internal bool ShouldDeferIndexType(IType type, IndexFlags indexFlags = IndexFlags.None)
    {
        return (type.Flags & TypeFlags.InstantiableNonPrimitive) != 0 ||
            IsGenericTupleType(type) ||
            IsGenericMappedType(type) && GetNameTypeFromMappedType((IMappedType)type) is not null ||
            (type.Flags & TypeFlags.Union) != 0 && (indexFlags & IndexFlags.NoReducibleCheck) == 0 && IsGenericReducibleType(type) ||
            (type.Flags & TypeFlags.Intersection) != 0 && MaybeTypeOfKind(type, TypeFlags.Instantiable) && Core.Some(((IIntersectionType)type).Types, IsEmptyAnonymousObjectType);
    }

    /// <summary>ported from: src/compiler/checker.ts:18984-18995 — <c>getIndexType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's nested conditional expression is written as the same tests in the same order, each
    /// returning; the last arm's flag expression keeps its two ternaries, with TS's <c>0</c> spelled
    /// <c>TypeFlags.None</c> so the <c>|</c> stays an enum-or (§14).
    /// </remarks>
    internal IType GetIndexType(IType type, IndexFlags indexFlags = IndexFlags.None)
    {
        type = GetReducedType(type);
        if (IsNoInferType(type))
        {
            return GetNoInferType(GetIndexType(((ISubstitutionType)type).BaseType!, indexFlags));
        }
        if (ShouldDeferIndexType(type, indexFlags))
        {
            return GetIndexTypeForGenericType(type, indexFlags);
        }
        if ((type.Flags & TypeFlags.Union) != 0)
        {
            return GetIntersectionType(Debug.CheckDefined(Core.Map(((IUnionType)type).Types, (t, _) => GetIndexType(t, indexFlags))));
        }
        if ((type.Flags & TypeFlags.Intersection) != 0)
        {
            return GetUnionType(Debug.CheckDefined(Core.Map(((IIntersectionType)type).Types, (t, _) => GetIndexType(t, indexFlags))));
        }
        if ((GetObjectFlags(type) & ObjectFlags.Mapped) != 0)
        {
            return GetIndexTypeForMappedType((IMappedType)type, indexFlags);
        }
        if (ReferenceEquals(type, _wildcardType))
        {
            return _wildcardType;
        }
        if ((type.Flags & TypeFlags.Unknown) != 0)
        {
            return _neverType;
        }
        if ((type.Flags & (TypeFlags.Any | TypeFlags.Never)) != 0)
        {
            return _stringNumberSymbolType;
        }
        var include = ((indexFlags & IndexFlags.NoIndexSignatures) != 0 ? TypeFlags.StringLiteral : TypeFlags.StringLike)
            | ((indexFlags & IndexFlags.StringsOnly) != 0 ? 0 : TypeFlags.NumberLike | TypeFlags.ESSymbolLike);
        return GetLiteralTypeFromProperties(type, include, indexFlags == IndexFlags.None);
    }

    /// <summary>ported from: src/compiler/checker.ts:18997-19000 — <c>getExtractStringType</c>.</summary>
    internal IType GetExtractStringType(IType type)
    {
        var extractTypeAlias = GetGlobalExtractSymbol();
        return extractTypeAlias is not null
            ? GetTypeAliasInstantiation(extractTypeAlias, [type, _stringType])
            : _stringType;
    }

    /// <summary>ported from: src/compiler/checker.ts:19002-19005 — <c>getIndexTypeOrString</c>.</summary>
    internal IType GetIndexTypeOrString(IType type)
    {
        var indexType = GetExtractStringType(GetIndexType(type));
        return (indexType.Flags & TypeFlags.Never) != 0 ? _stringType : indexType;
    }

    // ---------------------------------------------------------------------------------------------
    // Template literal and string mapping types (checker.ts:19007-19187)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:19007-19027 — <c>getTypeFromTypeOperatorNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>links.resolvedType</c> is read as <c>is null</c> — a missing cache slot, which is the hole TS
    /// tests with <c>!</c> — and the final return is <c>Debug.CheckDefined</c> because the initializer's totality is
    /// guaranteed by the switch but not provable by C#.
    /// </remarks>
    internal IType GetTypeFromTypeOperatorNode(TypeOperatorNode node)
    {
        var links = GetNodeLinks(node);
        if (links.ResolvedType is null)
        {
            switch (node.Operator)
            {
                case SyntaxKind.KeyOfKeyword:
                    links.ResolvedType = GetIndexType(GetTypeFromTypeNode(node.Type!));
                    break;
                case SyntaxKind.UniqueKeyword:
                    links.ResolvedType = node.Type!.Kind == SyntaxKind.SymbolKeyword
                        ? GetESSymbolLikeTypeForNode(Utilities.WalkUpParenthesizedTypes(node.Parent!))
                        : _errorType;
                    break;
                case SyntaxKind.ReadonlyKeyword:
                    links.ResolvedType = GetTypeFromTypeNode(node.Type!);
                    break;
                default:
                    Debug.AssertNever(node.Operator);
                    break;
            }
        }
        return Debug.CheckDefined(links.ResolvedType);
    }

    /// <summary>ported from: src/compiler/checker.ts:19029-19038 — <c>getTypeFromTemplateTypeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: a <c>TemplateLiteralTypeSpan.literal</c> is a <c>TemplateMiddle | TemplateTail</c> upstream
    /// (types.ts:6980-6984) but the generated slot is <see cref="Node"/>, so <c>.text</c> is read through
    /// <see cref="ILiteralLikeNode"/> — the face that declares it.
    /// </remarks>
    internal IType GetTypeFromTemplateTypeNode(TemplateLiteralTypeNode node)
    {
        var links = GetNodeLinks(node);
        if (links.ResolvedType is null)
        {
            links.ResolvedType = GetTemplateLiteralType(
                [node.Head!.Text!, .. Core.Map(node.TemplateSpans, (span, _) => ((ILiteralLikeNode)span.Literal!).Text!)!],
                Core.Map(node.TemplateSpans, (span, _) => GetTypeFromTypeNode(span.Type!))!);
        }
        return Debug.CheckDefined(links.ResolvedType);
    }

    /// <summary>ported from: src/compiler/checker.ts:19040-19099 — <c>getTemplateLiteralType</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: TS's <c>map(newTexts, t =&gt; t.length).join(",")</c> is <c>string.Join(",", Core.Map(…))</c> — the
    /// same separator and the same element order — and <c>newTexts.join("")</c> is <c>string.Join("", newTexts)</c>.
    /// The cache is <see cref="_templateLiteralTypes"/>, whose PORT-NOTE records the key.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>addSpans</c> is a local function with the same name-shadowed <c>texts</c>/<c>types</c> parameters
    /// as upstream and mutates the captured <c>text</c>; <c>getTemplateStringForType(t) || ""</c> is <c>?? ""</c>, which
    /// is the same value for the <c>string</c> that function can return.
    /// </para>
    /// </remarks>
    internal IType GetTemplateLiteralType(List<string> texts, List<IType> types)
    {
        var unionIndex = Core.FindIndex(types, (t, _) => (t.Flags & (TypeFlags.Never | TypeFlags.Union)) != 0);
        if (unionIndex >= 0)
        {
            return CheckCrossProductUnion(types) ?
                MapType(types[unionIndex], t => GetTemplateLiteralType(texts, (List<IType>)Core.ReplaceElement(types, unionIndex, t)))! :
                _errorType;
        }
        if (Core.Contains(types, _wildcardType))
        {
            return _wildcardType;
        }
        var newTypes = new List<IType>();
        var newTexts = new List<string>();
        var text = texts[0];
        if (!AddSpans(texts, types))
        {
            return _stringType;
        }
        if (newTypes.Count == 0)
        {
            return GetStringLiteralType(text);
        }
        newTexts.Add(text);
        if (Core.Every(newTexts, (t, _) => t == ""))
        {
            if (Core.Every(newTypes, (t, _) => (t.Flags & TypeFlags.String) != 0))
            {
                return _stringType;
            }
            // Normalize `${Mapping<xxx>}` into Mapping<xxx>
            if (newTypes.Count == 1 && IsPatternLiteralType(newTypes[0]))
            {
                return newTypes[0];
            }
        }
        var id = $"{GetTypeListId(newTypes)}|{string.Join(",", Core.Map(newTexts, (t, _) => t.Length)!)}|{string.Join("", newTexts)}";
        if (!_templateLiteralTypes.TryGetValue(id, out var type))
        {
            type = CreateTemplateLiteralType(newTexts, newTypes);
            _templateLiteralTypes[id] = type;
        }
        return type;

        bool AddSpans(List<string> texts, List<IType> types)
        {
            for (var i = 0; i < types.Count; i++)
            {
                var t = types[i];
                if ((t.Flags & (TypeFlags.Literal | TypeFlags.Null | TypeFlags.Undefined)) != 0)
                {
                    text += GetTemplateStringForType(t) ?? "";
                    text += texts[i + 1];
                }
                else if ((t.Flags & TypeFlags.TemplateLiteral) != 0)
                {
                    var templateType = (ITemplateLiteralType)t;
                    text += templateType.Texts![0];
                    if (!AddSpans(templateType.Texts!, templateType.Types!)) return false;
                    text += texts[i + 1];
                }
                else if (IsGenericIndexType(t) || IsPatternLiteralPlaceholderType(t))
                {
                    newTypes.Add(t);
                    newTexts.Add(text);
                    text = texts[i + 1];
                }
                else
                {
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:19101-19107 — <c>getTemplateStringForType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the number arm's JS coercion <c>"" + value</c> is <see cref="Js.NumberToString"/> and the bigint
    /// arm's is <c>Utilities.PseudoBigIntToString</c> (utilities.ts:10529); the two primitive arms read the literal's
    /// <c>value</c> through <see cref="ILiteralType"/> (the generated slot is <c>object?</c>, §4.3) and the boolean/
    /// nullable arm reads <c>intrinsicName</c> off <see cref="IIntrinsicType"/>.
    /// </remarks>
    internal string? GetTemplateStringForType(IType type)
    {
        return (type.Flags & TypeFlags.StringLiteral) != 0 ? (string)((ILiteralType)type).Value! :
            (type.Flags & TypeFlags.NumberLiteral) != 0 ? Js.NumberToString((double)((ILiteralType)type).Value!) :
            (type.Flags & TypeFlags.BigIntLiteral) != 0 ? Utilities.PseudoBigIntToString((IPseudoBigInt)((ILiteralType)type).Value!) :
            (type.Flags & (TypeFlags.BooleanLiteral | TypeFlags.Nullable)) != 0 ? ((IIntrinsicType)type).IntrinsicName :
            null;
    }

    /// <summary>ported from: src/compiler/checker.ts:19109-19114 — <c>createTemplateLiteralType</c>.</summary>
    internal ITemplateLiteralType CreateTemplateLiteralType(List<string> texts, List<IType> types)
    {
        var type = (ITemplateLiteralType)CreateType(TypeFlags.TemplateLiteral);
        type.Texts = texts;
        type.Types = types;
        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:19116-19126 — <c>getStringMappingType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS spreads the tuple <c>applyTemplateStringMapping(…)</c> into the two parameters of
    /// <c>getTemplateLiteralType</c> (:19119); the port destructures it in the same order. <c>symbol === type.symbol</c>
    /// is the identity test <c>ReferenceEquals(symbol, type.Symbol)</c>.
    /// </remarks>
    internal IType GetStringMappingType(ISymbol symbol, IType type)
    {
        if ((type.Flags & (TypeFlags.Union | TypeFlags.Never)) != 0)
        {
            return MapType(type, t => GetStringMappingType(symbol, t))!;
        }
        if ((type.Flags & TypeFlags.StringLiteral) != 0)
        {
            return GetStringLiteralType(ApplyStringMapping(symbol, (string)((ILiteralType)type).Value!));
        }
        if ((type.Flags & TypeFlags.TemplateLiteral) != 0)
        {
            var templateType = (ITemplateLiteralType)type;
            var (mappedTexts, mappedTypes) = ApplyTemplateStringMapping(symbol, templateType.Texts!, templateType.Types!);
            return GetTemplateLiteralType(mappedTexts, mappedTypes);
        }
        // Mapping<Mapping<T>> === Mapping<T>
        if ((type.Flags & TypeFlags.StringMapping) != 0 && ReferenceEquals(symbol, type.Symbol))
        {
            return type;
        }
        if ((type.Flags & (TypeFlags.Any | TypeFlags.String | TypeFlags.StringMapping)) != 0 || IsGenericIndexType(type))
        {
            return GetStringMappingTypeForGenericType(symbol, type);
        }
        // This handles Mapping<`${number}`> and Mapping<`${bigint}`>
        return IsPatternLiteralPlaceholderType(type)
            ? GetStringMappingTypeForGenericType(symbol, GetTemplateLiteralType(["", ""], [type]))
            : type;
    }

    /// <summary>ported from: src/compiler/checker.ts:19128-19140 — <c>applyStringMapping</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the name lookup is <c>TryGetValue</c> rather than <c>GetValueOrDefault</c> because
    /// <c>Uppercase</c> is 0 and "absent" must stay distinct from it — the same note
    /// <c>Checker.TypeReferences.cs</c> records for <c>getTypeAliasInstantiation</c>'s identical lookup.
    /// <c>str.charAt(0).toUpperCase() + str.slice(1)</c> is <c>ChangeFirstCase</c>: JS's <c>charAt(0)</c> of the empty
    /// string is <c>""</c>, so the empty string maps to itself, and <c>slice(1)</c> of a one-code-unit string is
    /// <c>""</c> — which the range <c>str[1..]</c> also is.
    /// </remarks>
    internal string ApplyStringMapping(ISymbol symbol, string str)
    {
        var kind = symbol.EscapedName is not null && _intrinsicTypeKinds.TryGetValue(symbol.EscapedName, out var value)
            ? value
            : (IntrinsicTypeKind?)null;
        switch (kind)
        {
            case IntrinsicTypeKind.Uppercase:
                return IcuCaseFolding.ToUpper(str);
            case IntrinsicTypeKind.Lowercase:
                return IcuCaseFolding.ToLower(str);
            case IntrinsicTypeKind.Capitalize:
                return ChangeFirstCase(str, upper: true);
            case IntrinsicTypeKind.Uncapitalize:
                return ChangeFirstCase(str, upper: false);
        }
        return str;

        static string ChangeFirstCase(string str, bool upper)
            => str.Length == 0
                ? str
                : (upper ? IcuCaseFolding.ToUpper(str[..1]) : IcuCaseFolding.ToLower(str[..1])) + str[1..];
    }

    /// <summary>ported from: src/compiler/checker.ts:19142-19154 — <c>applyTemplateStringMapping</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's return type is the tuple <c>[texts: readonly string[], types: readonly Type[]]</c>; the port
    /// keeps a C# tuple with the same two positions, because the only caller (<c>getStringMappingType</c>) spreads the
    /// tuple straight into <c>getTemplateLiteralType</c>. The capitalize branches build
    /// <c>[first, ...rest]</c> with a collection expression over <c>Core.Slice</c> — a fresh list, as
    /// <c>[...texts.slice(1)]</c> is — and pass the original list through untouched on the empty-prefix arm, exactly
    /// as the reference does.
    /// </remarks>
    internal (List<string> Texts, List<IType> Types) ApplyTemplateStringMapping(ISymbol symbol, List<string> texts, List<IType> types)
    {
        var kind = symbol.EscapedName is not null && _intrinsicTypeKinds.TryGetValue(symbol.EscapedName, out var value)
            ? value
            : (IntrinsicTypeKind?)null;
        switch (kind)
        {
            case IntrinsicTypeKind.Uppercase:
                return (Core.Map(texts, (t, _) => IcuCaseFolding.ToUpper(t))!, Core.Map(types, (t, _) => GetStringMappingType(symbol, t))!);
            case IntrinsicTypeKind.Lowercase:
                return (Core.Map(texts, (t, _) => IcuCaseFolding.ToLower(t))!, Core.Map(types, (t, _) => GetStringMappingType(symbol, t))!);
            case IntrinsicTypeKind.Capitalize:
                return (
                    texts[0] == "" ? texts : [ChangeFirstCase(texts[0], upper: true), .. Core.Slice(texts, 1, texts.Count)],
                    texts[0] == "" ? [GetStringMappingType(symbol, types[0]), .. Core.Slice(types, 1, types.Count)] : types);
            case IntrinsicTypeKind.Uncapitalize:
                return (
                    texts[0] == "" ? texts : [ChangeFirstCase(texts[0], upper: false), .. Core.Slice(texts, 1, texts.Count)],
                    texts[0] == "" ? [GetStringMappingType(symbol, types[0]), .. Core.Slice(types, 1, types.Count)] : types);
        }
        return (texts, types);

        static string ChangeFirstCase(string str, bool upper)
            => str.Length == 0
                ? str
                : (upper ? IcuCaseFolding.ToUpper(str[..1]) : IcuCaseFolding.ToLower(str[..1])) + str[1..];
    }

    /// <summary>ported from: src/compiler/checker.ts:19156-19163 — <c>getStringMappingTypeForGenericType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the key is the id pair TS builds with a template literal; both ids are per-object counters assigned
    /// by the checker (<c>Utilities.GetSymbolId</c>, <c>getTypeId</c>), so the dictionary's string keys reproduce the
    /// reference's object-identity cache exactly. The cache is <see cref="_stringMappingTypes"/>.
    /// </remarks>
    internal IType GetStringMappingTypeForGenericType(ISymbol symbol, IType type)
    {
        var id = $"{Utilities.GetSymbolId(symbol)},{GetTypeId(type)}";
        if (!_stringMappingTypes.TryGetValue(id, out var result))
        {
            result = CreateStringMappingType(symbol, type);
            _stringMappingTypes[id] = result;
        }
        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:19165-19169 — <c>createStringMappingType</c>.</summary>
    internal IStringMappingType CreateStringMappingType(ISymbol symbol, IType type)
    {
        var result = (IStringMappingType)CreateTypeWithSymbol(TypeFlags.StringMapping, symbol);
        result.Type = type;
        return result;
    }

    // ---------------------------------------------------------------------------------------------
    // Indexed access types (checker.ts:19171-19435)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:19171-19179 — <c>createIndexedAccessType</c>.</summary>
    internal IIndexedAccessType CreateIndexedAccessType(IType objectType, IType indexType, AccessFlags accessFlags, ISymbol? aliasSymbol, List<IType>? aliasTypeArguments)
    {
        var type = (IIndexedAccessType)CreateType(TypeFlags.IndexedAccess);
        type.ObjectType = objectType;
        type.IndexType = indexType;
        type.AccessFlags = accessFlags;
        type.AliasSymbol = aliasSymbol;
        type.AliasTypeArguments = aliasTypeArguments;
        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:19190-19208 — <c>isJSLiteralType</c>.</summary>
    /// <remarks>
    /// Returns whether a type is, or consists of, a JSLiteral object type. In addition to objects that are directly
    /// literals, unions every element of which is a JSLiteral, intersections at least one element of which is, and
    /// instantiable types constrained to a JSLiteral should all count as literals and not print errors on access or
    /// assignment of possibly existing properties. This mirrors the behavior of the index-signature propagation, to
    /// which it behaves similarly (but does not affect assignability or inference).
    /// </remarks>
    internal bool IsJSLiteralType(IType type)
    {
        if (_noImplicitAny)
        {
            return false; // Flag is meaningless under `noImplicitAny` mode
        }
        if ((GetObjectFlags(type) & ObjectFlags.JSLiteral) != 0)
        {
            return true;
        }
        if ((type.Flags & TypeFlags.Union) != 0)
        {
            return Core.Every(((IUnionType)type).Types, (t, _) => IsJSLiteralType(t));
        }
        if ((type.Flags & TypeFlags.Intersection) != 0)
        {
            return Core.Some(((IIntersectionType)type).Types, IsJSLiteralType);
        }
        if ((type.Flags & TypeFlags.Instantiable) != 0)
        {
            var constraint = GetResolvedBaseConstraint(type);
            return !ReferenceEquals(constraint, type) && IsJSLiteralType(constraint);
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:19210-19217 — <c>getPropertyNameFromIndex</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the access-node union (<c>PropertyName | ObjectBindingPattern | ArrayBindingPattern |
    /// IndexedAccessTypeNode | ElementAccessExpression | SyntheticExpression</c>) is <see cref="Node"/> (§12.5);
    /// <c>isTypeUsableAsPropertyName</c>/<c>getPropertyNameFromType</c>/<c>isPropertyName</c>/
    /// <c>getPropertyNameForPropertyNameNode</c> are <c>Utilities</c>' (utilities.ts:11162, :11170, utilitiesPublic.ts:1657,
    /// utilities.ts:5397).
    /// </remarks>
    internal string? GetPropertyNameFromIndex(IType indexType, Node? accessNode)
    {
        return Utilities.IsTypeUsableAsPropertyName(indexType) ?
            Utilities.GetPropertyNameFromType(indexType) :
            accessNode is not null && Utilities.IsPropertyName(accessNode) ?
                // late bound names are handled in the first branch, so here we only need to handle normal names
                Utilities.GetPropertyNameForPropertyNameNode(accessNode) :
                null;
    }

    /// <summary>ported from: src/compiler/checker.ts:19219-19228 — <c>isUncalledFunctionReference</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types the ancestor walk's result as <c>Node</c> because <c>node.parent</c> is non-optional
    /// upstream; the port's <see cref="Node.Parent"/> is nullable, so the fallback keeps TS's <c>||</c> shape and the
    /// reads assert the bound node's parent, which every call site has (a checked reference is always in a tree).
    /// <c>isCallOrNewExpression</c> narrows to <c>CallExpression | NewExpression</c>, and the port's
    /// <c>NewExpression</c> derives from <c>CallExpression</c>, so the narrowest cast is to the latter.
    /// </remarks>
    internal bool IsUncalledFunctionReference(Node node, ISymbol symbol)
    {
        if ((symbol.Flags & (SymbolFlags.Function | SymbolFlags.Method)) != 0)
        {
            var parent = Utilities.FindAncestor(node.Parent, n => !Utilities.IsAccessExpression(n)) ?? node.Parent;
            if (Utilities.IsCallLikeExpression(parent!))
            {
                return Utilities.IsCallOrNewExpression(parent!) && NodeTests.IsIdentifier(node) && HasMatchingArgument((CallExpression)parent!, node);
            }
            return Core.Every(symbol.Declarations, (d, _) => !Utilities.IsFunctionLike((Node)d) || IsDeprecatedDeclaration(d));
        }
        return true;
    }

    /// <summary>ported from: src/compiler/checker.ts:19230-19428 — <c>getPropertyTypeForIndexType</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: TS's <c>accessNode &amp;&amp; accessNode.kind === SyntaxKind.ElementAccessExpression ? accessNode :
    /// undefined</c> is the same kind test plus a cast — the port never relies on a node class implying a kind — and
    /// the parameter union collapses to <see cref="Node"/>. <c>propName</c> stays <c>string?</c> throughout, which is
    /// why the second half re-tests it exactly where the reference does.
    /// </para>
    /// <para>
    /// PORT-NOTE: the two ways of reporting (the <c>Error</c> single point for the checks this function owns, and
    /// <c>_diagnostics.Add(Utilities.CreateDiagnosticForNode(…))</c> for the ones the reference queues without the
    /// checker's error state) are kept as upstream; the nested <c>errorIfWritingToReadonlyIndex</c> is a local
    /// function with the same captures.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>"" + value</c> of a literal type is <c>Js.NumberToString</c> for the number case and the string
    /// itself otherwise; <c>getTypeReferenceArity(objectType)</c>/<c>getTotalFixedElementCount(objectType.target)</c>
    /// take the narrowed faces (<c>ITypeReference</c>/<c>ITupleType</c>) because the generated
    /// <c>ITupleTypeReference</c> adds no narrowed target slot.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>+propName</c> is <see cref="Js.ToNumber"/> here, and the <c>number</c> it produces is
    /// narrowed to <c>int</c> for <c>getTupleElementTypeOutOfStartCount</c> — the only thing that function does with
    /// it is <c>index &gt;= getTotalFixedElementCount(…)</c>, and truncation cannot change that comparison against an
    /// integral count (<c>isNumericLiteralName</c> already rejected a name that is not a number).
    /// </para>
    /// </remarks>
    internal IType? GetPropertyTypeForIndexType(IType originalObjectType, IType objectType, IType indexType, IType fullIndexType, Node? accessNode, AccessFlags accessFlags)
    {
        var accessExpression = accessNode is not null && accessNode.Kind == SyntaxKind.ElementAccessExpression ? (ElementAccessExpression)accessNode : null;
        var propName = accessNode is not null && NodeTests.IsPrivateIdentifier(accessNode) ? null : GetPropertyNameFromIndex(indexType, accessNode);

        if (propName is not null)
        {
            if ((accessFlags & AccessFlags.Contextual) != 0)
            {
                return GetTypeOfPropertyOfContextualType(objectType, propName) ?? _anyType;
            }
            var prop = GetPropertyOfType(objectType, propName);
            if (prop is not null)
            {
                if ((accessFlags & AccessFlags.ReportDeprecated) != 0 && accessNode is not null && prop.Declarations is not null && IsDeprecatedSymbol(prop) && IsUncalledFunctionReference(accessNode, prop))
                {
                    var deprecatedNode = accessExpression?.ArgumentExpression ?? (NodeTests.IsIndexedAccessTypeNode(accessNode) ? (Node)((IndexedAccessTypeNode)accessNode).IndexType! : accessNode);
                    // PORT-NOTE: TS passes `prop.declarations` (a `Declaration[]`) to `addDeprecatedSuggestion(location,
                    // declarations: Node[], …)`; the port's member takes `IList<Node>` and a symbol's declarations are
                    // `List<IDeclaration>`, so the projection is explicit. It allocates once, on the deprecated-property
                    // path only.
                    AddDeprecatedSuggestion(deprecatedNode, Core.Map(prop.Declarations, (d, _) => (Node)d)!, propName);
                }
                if (accessExpression is not null)
                {
                    MarkPropertyAsReferenced(prop, accessExpression, IsSelfTypeAccess(accessExpression.Expression!, objectType.Symbol));
                    if (IsAssignmentToReadonlyEntity(accessExpression, prop, Utilities.GetAssignmentTargetKind(accessExpression)))
                    {
                        // PORT-NOTE (A″): the argument is a DiagnosticArgument (Core/Diagnostic.cs) so the printer-gated stringifier runs on the first read of the message text.
                        Error(accessExpression.ArgumentExpression, Diagnostics.Cannot_assign_to_0_because_it_is_a_read_only_property, Diagnostic.DeferArg(() => SymbolToString(prop)));
                        return null;
                    }
                    if ((accessFlags & AccessFlags.CacheSymbol) != 0)
                    {
                        // TS: `getNodeLinks(accessNode!)` — CacheSymbol is only ever set by an element-access site.
                        GetNodeLinks(accessNode!).ResolvedSymbol = prop;
                    }
                    if (IsThisPropertyAccessInConstructor(accessExpression, prop))
                    {
                        return _autoType;
                    }
                }
                var propType = (accessFlags & AccessFlags.Writing) != 0 ? GetWriteTypeOfSymbol(prop) : GetTypeOfSymbol(prop);
                return accessExpression is not null && Utilities.GetAssignmentTargetKind(accessExpression) != AssignmentKind.Definite
                    ? GetFlowTypeOfReference(accessExpression, propType)
                    : accessNode is not null && NodeTests.IsIndexedAccessTypeNode(accessNode) && ContainsMissingType(propType)
                        ? GetUnionType([propType, _undefinedType])
                        : propType;
            }
            if (EveryType(objectType, IsTupleType) && Utilities.IsNumericLiteralName(propName))
            {
                var index = Js.ToNumber(propName);
                if (accessNode is not null && EveryType(objectType, t => (((ITupleType)((ITupleTypeReference)t).Target!).CombinedFlags & ElementFlags.Variable) == 0) && (accessFlags & AccessFlags.AllowMissing) == 0)
                {
                    var indexNode = GetIndexNodeForAccessExpression(accessNode);
                    if (IsTupleType(objectType))
                    {
                        if (index < 0)
                        {
                            Error(indexNode, Diagnostics.A_tuple_type_cannot_be_indexed_with_a_negative_value);
                            return _undefinedType;
                        }
                        Error(indexNode, Diagnostics.Tuple_type_0_of_length_1_has_no_element_at_index_2, Diagnostic.DeferArg(() => TypeToString(objectType)), GetTypeReferenceArity((ITypeReference)objectType), Utilities.UnescapeLeadingUnderscores(propName));
                    }
                    else
                    {
                        Error(indexNode, Diagnostics.Property_0_does_not_exist_on_type_1, Utilities.UnescapeLeadingUnderscores(propName), Diagnostic.DeferArg(() => TypeToString(objectType)));
                    }
                }
                if (index >= 0)
                {
                    ErrorIfWritingToReadonlyIndex(GetIndexInfoOfType(objectType, _numberType));
                    return GetTupleElementTypeOutOfStartCount(objectType, (int)index, (accessFlags & AccessFlags.IncludeUndefined) != 0 ? _missingType : null);
                }
            }
        }
        if ((indexType.Flags & TypeFlags.Nullable) == 0 && IsTypeAssignableToKind(indexType, TypeFlags.StringLike | TypeFlags.NumberLike | TypeFlags.ESSymbolLike))
        {
            if ((objectType.Flags & (TypeFlags.Any | TypeFlags.Never)) != 0)
            {
                return objectType;
            }
            // If no index signature is applicable, we default to the string index signature. In effect, this means the string
            // index signature applies even when accessing with a symbol-like type.
            var indexInfo = GetApplicableIndexInfo(objectType, indexType) ?? GetIndexInfoOfType(objectType, _stringType);
            if (indexInfo is not null)
            {
                if ((accessFlags & AccessFlags.NoIndexSignatures) != 0 && !ReferenceEquals(indexInfo.KeyType, _numberType))
                {
                    if (accessExpression is not null)
                    {
                        if ((accessFlags & AccessFlags.Writing) != 0)
                        {
                            Error(accessExpression, Diagnostics.Type_0_is_generic_and_can_only_be_indexed_for_reading, Diagnostic.DeferArg(() => TypeToString(originalObjectType)));
                        }
                        else
                        {
                            Error(accessExpression, Diagnostics.Type_0_cannot_be_used_to_index_type_1, Diagnostic.DeferArg(() => TypeToString(indexType)), Diagnostic.DeferArg(() => TypeToString(originalObjectType)));
                        }
                    }
                    return null;
                }
                if (accessNode is not null && ReferenceEquals(indexInfo.KeyType, _stringType) && !IsTypeAssignableToKind(indexType, TypeFlags.String | TypeFlags.Number))
                {
                    var indexNode = GetIndexNodeForAccessExpression(accessNode);
                    Error(indexNode, Diagnostics.Type_0_cannot_be_used_as_an_index_type, Diagnostic.DeferArg(() => TypeToString(indexType)));
                    return (accessFlags & AccessFlags.IncludeUndefined) != 0 ? GetUnionType([indexInfo.Type!, _missingType]) : indexInfo.Type!;
                }
                ErrorIfWritingToReadonlyIndex(indexInfo);
                // When accessing an enum object with its own type,
                // e.g. E[E.A] for enum E { A }, undefined shouldn't
                // be included in the result type
                if (
                    (accessFlags & AccessFlags.IncludeUndefined) != 0 &&
                    !(objectType.Symbol is { } objectSymbol &&
                        (objectSymbol.Flags & (SymbolFlags.RegularEnum | SymbolFlags.ConstEnum)) != 0 &&
                        indexType.Symbol is { } indexSymbol &&
                        (indexType.Flags & TypeFlags.EnumLiteral) != 0 &&
                        ReferenceEquals(GetParentOfSymbol(indexSymbol), objectSymbol))
                )
                {
                    return GetUnionType([indexInfo.Type!, _missingType]);
                }
                return indexInfo.Type!;
            }
            if ((indexType.Flags & TypeFlags.Never) != 0)
            {
                return _neverType;
            }
            if (IsJSLiteralType(objectType))
            {
                return _anyType;
            }
            if (accessExpression is not null && !IsConstEnumObjectType(objectType))
            {
                if (IsObjectLiteralType(objectType))
                {
                    if (_noImplicitAny && (indexType.Flags & (TypeFlags.StringLiteral | TypeFlags.NumberLiteral)) != 0)
                    {
                        _diagnostics.Add(Utilities.CreateDiagnosticForNode(accessExpression, Diagnostics.Property_0_does_not_exist_on_type_1, (string)((ILiteralType)indexType).Value!, Diagnostic.DeferArg(() => TypeToString(objectType))));
                        return _undefinedType;
                    }
                    else if ((indexType.Flags & (TypeFlags.Number | TypeFlags.String)) != 0)
                    {
                        var types = Core.Map(((IResolvedType)objectType).Properties, (property, _) => GetTypeOfSymbol(property));
                        return GetUnionType([.. Debug.CheckDefined(types), _undefinedType]);
                    }
                }

                // PORT-NOTE: TS's `globalThisSymbol.exports!.has(propName) && … .get(propName)!.flags & …` performs two
                // table lookups; the port's `TryGetValue` performs one and yields the same symbol for the second test.
                if (_globalThisSymbol is { } globalThisSymbol && ReferenceEquals(objectType.Symbol, globalThisSymbol) && propName is not null
                    && globalThisSymbol.Exports is { } globalThisExports && globalThisExports.TryGetValue(propName, out var globalThisSymbolOfName)
                    && (globalThisSymbolOfName.Flags & SymbolFlags.BlockScoped) != 0)
                {
                    Error(accessExpression, Diagnostics.Property_0_does_not_exist_on_type_1, Utilities.UnescapeLeadingUnderscores(propName), Diagnostic.DeferArg(() => TypeToString(objectType)));
                }
                else if (_noImplicitAny && (accessFlags & AccessFlags.SuppressNoImplicitAnyError) == 0)
                {
                    if (propName is not null && TypeHasStaticProperty(propName, objectType))
                    {
                        // PORT-NOTE (A″): one memoised thunk for both arguments, so the reference's single
                        // `typeToString` call stays single (the second argument builds its text from the first).
                        var typeName = Diagnostic.DeferArg(() => TypeToString(objectType));
                        Error(accessExpression, Diagnostics.Property_0_does_not_exist_on_type_1_Did_you_mean_to_access_the_static_member_2_instead, propName, typeName, Diagnostic.DeferArg(() => typeName.Resolve()! + "[" + Utilities.GetTextOfNode(accessExpression.ArgumentExpression!) + "]"));
                    }
                    else if (GetIndexTypeOfType(objectType, _numberType) is not null)
                    {
                        Error(accessExpression.ArgumentExpression, Diagnostics.Element_implicitly_has_an_any_type_because_index_expression_is_not_of_type_number);
                    }
                    else
                    {
                        string? suggestion = null;
                        if (propName is not null && Js.IsTruthy(suggestion = GetSuggestionForNonexistentProperty(propName, objectType)))
                        {
                            if (suggestion is not null)
                            {
                                Error(accessExpression.ArgumentExpression, Diagnostics.Property_0_does_not_exist_on_type_1_Did_you_mean_2, propName, Diagnostic.DeferArg(() => TypeToString(objectType)), suggestion);
                            }
                        }
                        else
                        {
                            var indexSuggestion = GetSuggestionForNonexistentIndexSignature(objectType, accessExpression, indexType);
                            if (indexSuggestion is not null)
                            {
                                Error(accessExpression, Diagnostics.Element_implicitly_has_an_any_type_because_type_0_has_no_index_signature_Did_you_mean_to_call_1, Diagnostic.DeferArg(() => TypeToString(objectType)), indexSuggestion);
                            }
                            else
                            {
                                DiagnosticMessageChain? errorInfo = null;
                                if ((indexType.Flags & TypeFlags.EnumLiteral) != 0)
                                {
                                    errorInfo = Utilities.ChainDiagnosticMessages(/*details*/ null, Diagnostics.Property_0_does_not_exist_on_type_1, Diagnostic.DeferArg(() => "[" + TypeToString(indexType) + "]"), Diagnostic.DeferArg(() => TypeToString(objectType)));
                                }
                                else if ((indexType.Flags & TypeFlags.UniqueESSymbol) != 0)
                                {
                                    var symbolName = GetFullyQualifiedName(((IUniqueESSymbolType)indexType).Symbol!, accessExpression);
                                    errorInfo = Utilities.ChainDiagnosticMessages(/*details*/ null, Diagnostics.Property_0_does_not_exist_on_type_1, "[" + symbolName + "]", Diagnostic.DeferArg(() => TypeToString(objectType)));
                                }
                                else if ((indexType.Flags & TypeFlags.StringLiteral) != 0)
                                {
                                    errorInfo = Utilities.ChainDiagnosticMessages(/*details*/ null, Diagnostics.Property_0_does_not_exist_on_type_1, (string)((ILiteralType)indexType).Value!, Diagnostic.DeferArg(() => TypeToString(objectType)));
                                }
                                else if ((indexType.Flags & TypeFlags.NumberLiteral) != 0)
                                {
                                    errorInfo = Utilities.ChainDiagnosticMessages(/*details*/ null, Diagnostics.Property_0_does_not_exist_on_type_1, Js.NumberToString((double)((ILiteralType)indexType).Value!), Diagnostic.DeferArg(() => TypeToString(objectType)));
                                }
                                else if ((indexType.Flags & (TypeFlags.Number | TypeFlags.String)) != 0)
                                {
                                    errorInfo = Utilities.ChainDiagnosticMessages(/*details*/ null, Diagnostics.No_index_signature_with_a_parameter_of_type_0_was_found_on_type_1, Diagnostic.DeferArg(() => TypeToString(indexType)), Diagnostic.DeferArg(() => TypeToString(objectType)));
                                }

                                errorInfo = Utilities.ChainDiagnosticMessages(
                                    errorInfo,
                                    Diagnostics.Element_implicitly_has_an_any_type_because_expression_of_type_0_can_t_be_used_to_index_type_1,
                                    Diagnostic.DeferArg(() => TypeToString(fullIndexType)),
                                    Diagnostic.DeferArg(() => TypeToString(objectType)));
                                _diagnostics.Add(Utilities.CreateDiagnosticForNodeFromMessageChain(Debug.CheckDefined(Utilities.GetSourceFileOfNode(accessExpression)), accessExpression, errorInfo));
                            }
                        }
                    }
                }
                return null;
            }
        }
        if ((accessFlags & AccessFlags.AllowMissing) != 0 && IsObjectLiteralType(objectType))
        {
            return _undefinedType;
        }
        if (IsJSLiteralType(objectType))
        {
            return _anyType;
        }
        if (accessNode is not null)
        {
            var indexNode = GetIndexNodeForAccessExpression(accessNode);
            if (indexNode.Kind != SyntaxKind.BigIntLiteral && (indexType.Flags & (TypeFlags.StringLiteral | TypeFlags.NumberLiteral)) != 0)
            {
                var literalValue = ((ILiteralType)indexType).Value;
                Error(indexNode, Diagnostics.Property_0_does_not_exist_on_type_1, literalValue is double number ? Js.NumberToString(number) : literalValue, Diagnostic.DeferArg(() => TypeToString(objectType)));
            }
            else if ((indexType.Flags & (TypeFlags.String | TypeFlags.Number)) != 0)
            {
                Error(indexNode, Diagnostics.Type_0_has_no_matching_index_signature_for_type_1, Diagnostic.DeferArg(() => TypeToString(objectType)), Diagnostic.DeferArg(() => TypeToString(indexType)));
            }
            else
            {
                object typeString = indexNode.Kind == SyntaxKind.BigIntLiteral ? "bigint" : Diagnostic.DeferArg(() => TypeToString(indexType));
                Error(indexNode, Diagnostics.Type_0_cannot_be_used_as_an_index_type, typeString);
            }
        }
        if (IsTypeAny(indexType))
        {
            return indexType;
        }
        return null;

        void ErrorIfWritingToReadonlyIndex(IIndexInfo? indexInfo)
        {
            if (indexInfo is not null && indexInfo.IsReadonly && accessExpression is not null
                && (Utilities.IsAssignmentTarget(accessExpression) || Utilities.IsDeleteTarget(accessExpression)))
            {
                Error(accessExpression, Diagnostics.Index_signature_in_type_0_only_permits_reading, Diagnostic.DeferArg(() => TypeToString(objectType)));
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:19430-19435 — <c>getIndexNodeForAccessExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the parameter (and the answer) is the union of every node an access site can be, which collapses to
    /// <see cref="Node"/> (§12.5); each arm keeps the reference's kind test and then reads that kind's own slot —
    /// <c>argumentExpression</c>, <c>indexType</c>, <c>expression</c> — through the class that declares it. All three
    /// slots are total in TS and are read with <c>!</c> under this file's note on generated slots.
    /// </remarks>
    internal Node GetIndexNodeForAccessExpression(Node accessNode)
    {
        return accessNode.Kind == SyntaxKind.ElementAccessExpression ? ((ElementAccessExpression)accessNode).ArgumentExpression! :
            accessNode.Kind == SyntaxKind.IndexedAccessType ? (Node)((IndexedAccessTypeNode)accessNode).IndexType! :
            accessNode.Kind == SyntaxKind.ComputedPropertyName ? ((ComputedPropertyName)accessNode).Expression! :
            accessNode;
    }

    /// <summary>ported from: src/compiler/checker.ts:19437-19453 — <c>isPatternLiteralPlaceholderType</c>.</summary>
    internal bool IsPatternLiteralPlaceholderType(IType type)
    {
        if ((type.Flags & TypeFlags.Intersection) != 0)
        {
            // Return true if the intersection consists of one or more placeholders and zero or
            // more object type tags.
            var seenPlaceholder = false;
            foreach (var t in ((IIntersectionType)type).Types!)
            {
                if ((t.Flags & (TypeFlags.Literal | TypeFlags.Nullable)) != 0 || IsPatternLiteralPlaceholderType(t))
                {
                    seenPlaceholder = true;
                }
                else if ((t.Flags & TypeFlags.Object) == 0)
                {
                    return false;
                }
            }
            return seenPlaceholder;
        }
        return (type.Flags & (TypeFlags.Any | TypeFlags.String | TypeFlags.Number | TypeFlags.BigInt)) != 0 || IsPatternLiteralType(type);
    }

    /// <summary>ported from: src/compiler/checker.ts:19455-19460 — <c>isPatternLiteralType</c>.</summary>
    /// <remarks>
    /// A pattern literal type is a template literal or a string mapping type that contains only non-generic pattern
    /// literal placeholders.
    /// </remarks>
    internal bool IsPatternLiteralType(IType type)
    {
        return (type.Flags & TypeFlags.TemplateLiteral) != 0 && Core.Every(((ITemplateLiteralType)type).Types, (t, _) => IsPatternLiteralPlaceholderType(t)) ||
            (type.Flags & TypeFlags.StringMapping) != 0 && IsPatternLiteralPlaceholderType(((IStringMappingType)type).Type!);
    }

    /// <summary>ported from: src/compiler/checker.ts:19462-19464 — <c>isGenericStringLikeType</c>.</summary>
    internal bool IsGenericStringLikeType(IType type)
        => (type.Flags & (TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) != 0 && !IsPatternLiteralType(type);

    /// <summary>ported from: src/compiler/checker.ts:19466-19468 — <c>isGenericType</c>.</summary>
    internal bool IsGenericType(IType type) => GetGenericObjectFlags(type) != 0;

    /// <summary>ported from: src/compiler/checker.ts:19470-19472 — <c>isGenericObjectType</c>.</summary>
    internal bool IsGenericObjectType(IType type) => (GetGenericObjectFlags(type) & ObjectFlags.IsGenericObjectType) != 0;

    /// <summary>ported from: src/compiler/checker.ts:19474-19476 — <c>isGenericIndexType</c>.</summary>
    internal bool IsGenericIndexType(IType type) => (GetGenericObjectFlags(type) & ObjectFlags.IsGenericIndexType) != 0;

    /// <summary>ported from: src/compiler/checker.ts:19478-19495 — <c>getGenericObjectFlags</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: this release computes the two generic flags into the type's <em>own</em> <c>objectFlags</c> and guards
    /// them with <c>ObjectFlags.IsGenericTypeComputed</c> (:19480-19494) — release-6.0 has no <c>genericObjectFlags</c>
    /// WeakMap (the identifier appears nowhere in checker.ts), so the port needs no cache field: the slots are
    /// <c>IUnionOrIntersectionType.ObjectFlags</c> and <c>ISubstitutionType.ObjectFlags</c>, both already on the model.
    /// TS's <c>reduceLeft(types, (flags, t) =&gt; flags | …)</c> is the port's <c>Core.ReduceLeft</c>, whose callback
    /// takes the index TS's does not (ignored here) and whose initial value is <c>ObjectFlags.None</c> for TS's
    /// <c>0</c>.
    /// </remarks>
    internal ObjectFlags GetGenericObjectFlags(IType type)
    {
        if ((type.Flags & TypeFlags.UnionOrIntersection) != 0)
        {
            var typeAsUnionOrIntersection = (IUnionOrIntersectionType)type;
            if ((typeAsUnionOrIntersection.ObjectFlags & ObjectFlags.IsGenericTypeComputed) == 0)
            {
                typeAsUnionOrIntersection.ObjectFlags |= ObjectFlags.IsGenericTypeComputed
                    | Core.ReduceLeft(typeAsUnionOrIntersection.Types, (flags, t, _) => flags | GetGenericObjectFlags(t), ObjectFlags.None);
            }
            return typeAsUnionOrIntersection.ObjectFlags & ObjectFlags.IsGenericType;
        }
        if ((type.Flags & TypeFlags.Substitution) != 0)
        {
            var substitution = (ISubstitutionType)type;
            if ((substitution.ObjectFlags & ObjectFlags.IsGenericTypeComputed) == 0)
            {
                substitution.ObjectFlags |= ObjectFlags.IsGenericTypeComputed
                    | GetGenericObjectFlags(substitution.BaseType!)
                    | GetGenericObjectFlags(substitution.Constraint!);
            }
            return substitution.ObjectFlags & ObjectFlags.IsGenericType;
        }
        return ((type.Flags & TypeFlags.InstantiableNonPrimitive) != 0 || IsGenericMappedType(type) || IsGenericTupleType(type) ? ObjectFlags.IsGenericObjectType : ObjectFlags.None)
            | ((type.Flags & (TypeFlags.InstantiableNonPrimitive | TypeFlags.Index)) != 0 || IsGenericStringLikeType(type) ? ObjectFlags.IsGenericIndexType : ObjectFlags.None);
    }

    /// <summary>ported from: src/compiler/checker.ts:19497-19502 — <c>getSimplifiedType</c>.</summary>
    internal IType GetSimplifiedType(IType type, bool writing)
    {
        return (type.Flags & TypeFlags.IndexedAccess) != 0 ? GetSimplifiedIndexedAccessType((IIndexedAccessType)type, writing) :
            (type.Flags & TypeFlags.Conditional) != 0 ? GetSimplifiedConditionalType((IConditionalType)type, writing) :
            (type.Flags & TypeFlags.Index) != 0 ? GetSimplifiedIndexType((IIndexType)type) :
            type;
    }

    /// <summary>ported from: src/compiler/checker.ts:19504-19512 — <c>distributeIndexOverObjectType</c>.</summary>
    /// <remarks>
    /// <c>(T | U)[K] -&gt; T[K] | U[K]</c> (reading), <c>(T | U)[K] -&gt; T[K] &amp; U[K]</c> (writing),
    /// <c>(T &amp; U)[K] -&gt; T[K] &amp; U[K]</c>.
    /// </remarks>
    internal IType? DistributeIndexOverObjectType(IType objectType, IType indexType, bool writing)
    {
        if ((objectType.Flags & TypeFlags.Union) != 0 || ((objectType.Flags & TypeFlags.Intersection) != 0 && !ShouldDeferIndexType(objectType)))
        {
            var types = Debug.CheckDefined(Core.Map(((IUnionOrIntersectionType)objectType).Types, (t, _) => GetSimplifiedType(GetIndexedAccessType(t, indexType), writing)));
            return (objectType.Flags & TypeFlags.Intersection) != 0 || writing ? GetIntersectionType(types) : GetUnionType(types);
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:19514-19521 — <c>distributeObjectOverIndexType</c>.</summary>
    /// <remarks>
    /// <c>T[A | B] -&gt; T[A] | T[B]</c> (reading), <c>T[A | B] -&gt; T[A] &amp; T[B]</c> (writing).
    /// </remarks>
    internal IType? DistributeObjectOverIndexType(IType objectType, IType indexType, bool writing)
    {
        if ((indexType.Flags & TypeFlags.Union) != 0)
        {
            var types = Debug.CheckDefined(Core.Map(((IUnionType)indexType).Types, (t, _) => GetSimplifiedType(GetIndexedAccessType(objectType, t), writing)));
            return writing ? GetIntersectionType(types) : GetUnionType(types);
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:19526-19573 — <c>getSimplifiedIndexedAccessType</c>.</summary>
    /// <remarks>
    /// <para>
    /// Transform an indexed access to a simpler form, if possible. Return the simpler form, or the type itself if no
    /// transformation is possible. The writing flag indicates that the type is the target of an assignment.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS selects the cache slot by name (<c>const cache = writing ? "simplifiedForWriting" :
    /// "simplifiedForReading"</c>, then <c>type[cache]</c>, :19527-19531). C# cannot index an object by string, so the
    /// two local functions read and write the two generated slots (<c>types.ts:6906-6907</c>) in the same order; the
    /// <c>circularConstraintType</c> sentinel and every early return are the reference's. <c>objectType.target</c> is
    /// read through <c>ITupleType</c> for the reason the file remarks give (the generated <c>ITupleTypeReference</c>
    /// adds no narrowed target slot).
    /// </para>
    /// </remarks>
    internal IType GetSimplifiedIndexedAccessType(IIndexedAccessType type, bool writing)
    {
        IType? GetCached() => writing ? type.SimplifiedForWriting : type.SimplifiedForReading;
        void SetCached(IType value)
        {
            if (writing)
            {
                type.SimplifiedForWriting = value;
            }
            else
            {
                type.SimplifiedForReading = value;
            }
        }

        var cached = GetCached();
        if (cached is not null)
        {
            return ReferenceEquals(cached, _circularConstraintType) ? type : cached;
        }
        SetCached(_circularConstraintType);
        // We recursively simplify the object type as it may in turn be an indexed access type. For example, with
        // '{ [P in T]: { [Q in U]: number } }[T][U]' we want to first simplify the inner indexed access type.
        var objectType = GetSimplifiedType(type.ObjectType!, writing);
        var indexType = GetSimplifiedType(type.IndexType!, writing);
        // T[A | B] -> T[A] | T[B] (reading)
        // T[A | B] -> T[A] & T[B] (writing)
        var distributedOverIndex = DistributeObjectOverIndexType(objectType, indexType, writing);
        if (distributedOverIndex is not null)
        {
            SetCached(distributedOverIndex);
            return distributedOverIndex;
        }
        // Only do the inner distributions if the index can no longer be instantiated to cause index distribution again
        if ((indexType.Flags & TypeFlags.Instantiable) == 0)
        {
            // (T | U)[K] -> T[K] | U[K] (reading)
            // (T | U)[K] -> T[K] & U[K] (writing)
            // (T & U)[K] -> T[K] & U[K]
            var distributedOverObject = DistributeIndexOverObjectType(objectType, indexType, writing);
            if (distributedOverObject is not null)
            {
                SetCached(distributedOverObject);
                return distributedOverObject;
            }
        }
        // So ultimately (reading):
        // ((A & B) | C)[K1 | K2] -> ((A & B) | C)[K1] | ((A & B) | C)[K2] -> (A & B)[K1] | C[K1] | (A & B)[K2] | C[K2] -> (A[K1] & B[K1]) | C[K1] | (A[K2] & B[K2]) | C[K2]

        // A generic tuple type indexed by a number exists only when the index type doesn't select a
        // fixed element. We simplify to either the combined type of all elements (when the index type
        // the actual number type) or to the combined type of all non-fixed elements.
        if (IsGenericTupleType(objectType) && (indexType.Flags & TypeFlags.NumberLike) != 0)
        {
            var elementType = GetElementTypeOfSliceOfTupleType(
                (ITypeReference)objectType,
                (indexType.Flags & TypeFlags.Number) != 0 ? 0 : ((ITupleType)((ITupleTypeReference)objectType).Target!).FixedLength,
                /*endSkipCount*/ 0,
                writing);
            if (elementType is not null)
            {
                SetCached(elementType);
                return elementType;
            }
        }
        // If the object type is a mapped type { [P in K]: E }, where K is generic, or { [P in K as N]: E }, where
        // K is generic and N is assignable to P, instantiate E using a mapper that substitutes the index type for P.
        // For example, for an index access { [P in K]: Box<T[P]> }[X], we construct the type Box<T[X]>.
        if (IsGenericMappedType(objectType))
        {
            var mappedType = (IMappedType)objectType;
            if (GetMappedTypeNameTypeKind(mappedType) != MappedTypeNameTypeKind.Remapping)
            {
                var simplified = MapType(SubstituteIndexedMappedType(mappedType, type.IndexType!), t => GetSimplifiedType(t, writing))!;
                SetCached(simplified);
                return simplified;
            }
        }
        SetCached(type);
        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:19575-19598 — <c>getSimplifiedConditionalType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>getTrueTypeFromConditionalType</c>/<c>getFalseTypeFromConditionalType</c> return <c>Type</c> in TS
    /// (their <c>||=</c> is filled from an instantiation), so the port's reads are <c>IType</c>; the two branches keep
    /// the reference's tests in its order, with <c>isTypeAssignableTo</c> the relations slice's member.
    /// </remarks>
    internal IType GetSimplifiedConditionalType(IConditionalType type, bool writing)
    {
        var checkType = type.CheckType!;
        var extendsType = type.ExtendsType!;
        var trueType = GetTrueTypeFromConditionalType(type);
        var falseType = GetFalseTypeFromConditionalType(type);
        // Simplifications for types of the form `T extends U ? T : never` and `T extends U ? never : T`.
        if ((falseType.Flags & TypeFlags.Never) != 0 && ReferenceEquals(GetActualTypeVariable(trueType), GetActualTypeVariable(checkType)))
        {
            if ((checkType.Flags & TypeFlags.Any) != 0 || IsTypeAssignableTo(GetRestrictiveInstantiation(checkType), GetRestrictiveInstantiation(extendsType))) // Always true
            {
                return GetSimplifiedType(trueType, writing);
            }
            else if (IsIntersectionEmpty(checkType, extendsType)) // Always false
            {
                return _neverType;
            }
        }
        else if ((trueType.Flags & TypeFlags.Never) != 0 && ReferenceEquals(GetActualTypeVariable(falseType), GetActualTypeVariable(checkType)))
        {
            if ((checkType.Flags & TypeFlags.Any) == 0 && IsTypeAssignableTo(GetRestrictiveInstantiation(checkType), GetRestrictiveInstantiation(extendsType))) // Always true
            {
                return _neverType;
            }
            else if ((checkType.Flags & TypeFlags.Any) != 0 || IsIntersectionEmpty(checkType, extendsType)) // Always false
            {
                return GetSimplifiedType(falseType, writing);
            }
        }
        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:19600-19605 — <c>getSimplifiedIndexType</c>.</summary>
    internal IType GetSimplifiedIndexType(IIndexType type)
    {
        var indexedType = type.Type!;
        if (IsGenericMappedType(indexedType))
        {
            var mappedType = (IMappedType)indexedType;
            if (GetNameTypeFromMappedType(mappedType) is not null && !IsMappedTypeWithKeyofConstraintDeclaration(mappedType))
            {
                return GetIndexTypeForMappedType(mappedType, IndexFlags.None);
            }
        }
        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:19610-19612 — <c>isIntersectionEmpty</c>.</summary>
    /// <remarks>Invokes the union simplification logic to determine whether an intersection is considered empty as a union constituent.</remarks>
    internal bool IsIntersectionEmpty(IType type1, IType type2)
        // PORT-NOTE: TS's `intersectTypes(type1, type2)` overload is total for two `Type`s; the port's single member
        // (Checker.Members.cs) answers `IType?`, so the call asserts what the reference's signature promises.
        => (GetUnionType([IntersectTypes(type1, type2)!, _neverType]).Flags & TypeFlags.Never) != 0;

    /// <summary>ported from: src/compiler/checker.ts:19619-19627 — <c>substituteIndexedMappedType</c>.</summary>
    /// <remarks>
    /// Given an indexed access on a mapped type of the form { [P in K]: E }[X], returns an instantiation of E where P
    /// is replaced with X. Since this simplification doesn't account for mapped type modifiers, 'undefined' is added to
    /// the resulting type if the mapped type includes a '?' modifier or if the modifiers type indicates that some
    /// properties are optional; if the modifiers type is generic, optionality is conservatively estimated by
    /// recursively looking for mapped types that include '?' modifiers.
    /// <para>
    /// PORT-NOTE: <c>objectType.target as MappedType || objectType</c> is <c>objectType.Target as IMappedType ??
    /// objectType</c> — a null target is TS's falsy arm; <c>combineTypeMappers</c> takes a non-null second mapper
    /// (Checker.Instantiation.cs) and the first is the mapped type's own, which is null where TS's is <c>undefined</c>.
    /// </para>
    /// </remarks>
    internal IType SubstituteIndexedMappedType(IMappedType objectType, IType index)
    {
        var mapper = CreateTypeMapper([GetTypeParameterFromMappedType(objectType)], [index]);
        var templateMapper = CombineTypeMappers((TypeMapper?)objectType.Mapper, mapper);
        var instantiatedTemplateType = InstantiateType(GetTemplateTypeFromMappedType(objectType.Target as IMappedType ?? objectType), templateMapper)!;
        var isOptional = GetMappedTypeOptionality(objectType) > 0 || (IsGenericType(objectType)
            ? GetCombinedMappedTypeOptionality(GetModifiersTypeFromMappedType(objectType)) > 0
            : CouldAccessOptionalProperty(objectType, index));
        return AddOptionality(instantiatedTemplateType, /*isProperty*/ true, isOptional);
    }

    /// <summary>ported from: src/compiler/checker.ts:19630-19635 — <c>couldAccessOptionalProperty</c>.</summary>
    /// <remarks>Returns true if an indexed access with the given object and index types could access an optional property.</remarks>
    internal bool CouldAccessOptionalProperty(IType objectType, IType indexType)
    {
        var indexConstraint = GetBaseConstraintOfType(indexType);
        return indexConstraint is not null && Core.Some(GetPropertiesOfType(objectType), p =>
            (p.Flags & SymbolFlags.Optional) != 0 &&
            IsTypeAssignableTo(GetLiteralTypeFromProperty(p, TypeFlags.StringOrNumberLiteralOrUnique), indexConstraint));
    }

    /// <summary>ported from: src/compiler/checker.ts:19637-19639 — <c>getIndexedAccessType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>|| (accessNode ? errorType : unknownType)</c> is <c>?? (accessNode is not null ? … : …)</c>:
    /// the left operand is a <c>Type</c> or <c>undefined</c> and a <c>Type</c> is always truthy, so the two coincide.
    /// </remarks>
    internal IType GetIndexedAccessType(IType objectType, IType indexType, AccessFlags accessFlags = AccessFlags.None, Node? accessNode = null, ISymbol? aliasSymbol = null, List<IType>? aliasTypeArguments = null)
        => GetIndexedAccessTypeOrUndefined(objectType, indexType, accessFlags, accessNode, aliasSymbol, aliasTypeArguments)
            ?? (accessNode is not null ? _errorType : _unknownType);

    /// <summary>ported from: src/compiler/checker.ts:19641-19652 — <c>indexTypeLessThan</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>+propName</c> is JS's ToNumber and is <see cref="Js.ToNumber"/> here; the comparison against
    /// <c>limit</c> is the reference's, on the <c>double</c> JS produces.
    /// </remarks>
    internal bool IndexTypeLessThan(IType indexType, int limit)
    {
        return EveryType(indexType, t =>
        {
            if ((t.Flags & TypeFlags.StringOrNumberLiteral) != 0)
            {
                var propName = Utilities.GetPropertyNameFromType(t);
                if (Utilities.IsNumericLiteralName(propName))
                {
                    var index = Js.ToNumber(propName);
                    return index >= 0 && index < limit;
                }
            }
            return false;
        });
    }

    /// <summary>ported from: src/compiler/checker.ts:19654-19720 — <c>getIndexedAccessTypeOrUndefined</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the deferred-operation id is <c>objectType.id + "," + indexType.id + "," + persistentAccessFlags +
    /// getAliasId(…)</c>; the port spells the flags as <c>(int)persistentAccessFlags</c> so the concatenation is JS's
    /// numeric coercion rather than the enum's member name (§5 rule 14). The cache is
    /// <see cref="_indexedAccessTypes"/>, whose PORT-NOTE records the key and the map semantics.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>compilerOptions.noUncheckedIndexedAccess</c> is this checker's
    /// <c>_compilerOptions.NoUncheckedIndexedAccess</c> (a <c>bool?</c> option, tested for truthiness);
    /// <c>objectType.symbol &amp;&amp; …</c> chains keep the reference's tests in the reference's order;
    /// <c>getTotalFixedElementCount(objectType.target)</c> reads the tuple target through <c>ITupleType</c> because the
    /// generated <c>ITupleTypeReference</c> adds no narrowed target slot; and <c>AccessFlags.CacheSymbol |
    /// AccessFlags.ReportDeprecated</c> is the reference's flag pair.
    /// </para>
    /// </remarks>
    internal IType? GetIndexedAccessTypeOrUndefined(IType objectType, IType indexType, AccessFlags accessFlags = AccessFlags.None, Node? accessNode = null, ISymbol? aliasSymbol = null, List<IType>? aliasTypeArguments = null)
    {
        if (ReferenceEquals(objectType, _wildcardType) || ReferenceEquals(indexType, _wildcardType))
        {
            return _wildcardType;
        }
        objectType = GetReducedType(objectType);
        // If the object type has a string index signature and no other members we know that the result will
        // always be the type of that index signature and we can simplify accordingly.
        if (IsStringIndexSignatureOnlyType(objectType) && (indexType.Flags & TypeFlags.Nullable) == 0 && IsTypeAssignableToKind(indexType, TypeFlags.String | TypeFlags.Number))
        {
            indexType = _stringType;
        }
        // In noUncheckedIndexedAccess mode, indexed access operations that occur in an expression in a read position and resolve to
        // an index signature have 'undefined' included in their type.
        if (Js.IsTruthy(_compilerOptions.NoUncheckedIndexedAccess) && (accessFlags & AccessFlags.ExpressionPosition) != 0)
        {
            accessFlags |= AccessFlags.IncludeUndefined;
        }
        // If the index type is generic, or if the object type is generic and doesn't originate in an expression and
        // the operation isn't exclusively indexing the fixed (non-variadic) portion of a tuple type, we are performing
        // a higher-order index access where we cannot meaningfully access the properties of the object type. Note that
        // for a generic T and a non-generic K, we eagerly resolve T[K] if it originates in an expression. This is to
        // preserve backwards compatibility. For example, an element access 'this["foo"]' has always been resolved
        // eagerly using the constraint type of 'this' at the given location.
        if (
            IsGenericIndexType(indexType) || (accessNode is not null && accessNode.Kind != SyntaxKind.IndexedAccessType ?
                IsGenericTupleType(objectType) && !IndexTypeLessThan(indexType, GetTotalFixedElementCount((ITupleType)((ITupleTypeReference)objectType).Target!)) :
                IsGenericObjectType(objectType) && !(IsTupleType(objectType) && IndexTypeLessThan(indexType, GetTotalFixedElementCount((ITupleType)((ITupleTypeReference)objectType).Target!))) || IsGenericReducibleType(objectType))
        )
        {
            if ((objectType.Flags & TypeFlags.AnyOrUnknown) != 0)
            {
                return objectType;
            }
            // Defer the operation by creating an indexed access type.
            var persistentAccessFlags = accessFlags & AccessFlags.Persistent;
            var id = objectType.Id + "," + indexType.Id + "," + (int)persistentAccessFlags + GetAliasId(aliasSymbol, aliasTypeArguments);
            if (!_indexedAccessTypes.TryGetValue(id, out var cached))
            {
                cached = CreateIndexedAccessType(objectType, indexType, persistentAccessFlags, aliasSymbol, aliasTypeArguments);
                _indexedAccessTypes[id] = cached;
            }
            return cached;
        }
        // In the following we resolve T[K] to the type of the property in T selected by K.
        // We treat boolean as different from other unions to improve errors;
        // skipping straight to getPropertyTypeForIndexType gives errors with 'boolean' instead of 'true'.
        var apparentObjectType = GetReducedApparentType(objectType);
        if ((indexType.Flags & TypeFlags.Union) != 0 && (indexType.Flags & TypeFlags.Boolean) == 0)
        {
            var propTypes = new List<IType>();
            var wasMissingProp = false;
            foreach (var t in ((IUnionType)indexType).Types!)
            {
                var propType = GetPropertyTypeForIndexType(objectType, apparentObjectType, t, indexType, accessNode, accessFlags | (wasMissingProp ? AccessFlags.SuppressNoImplicitAnyError : AccessFlags.None));
                if (propType is not null)
                {
                    propTypes.Add(propType);
                }
                else if (accessNode is null)
                {
                    // If there's no error node, we can immeditely stop, since error reporting is off
                    return null;
                }
                else
                {
                    // Otherwise we set a flag and return at the end of the loop so we still mark all errors
                    wasMissingProp = true;
                }
            }
            if (wasMissingProp)
            {
                return null;
            }
            return (accessFlags & AccessFlags.Writing) != 0
                ? GetIntersectionType(propTypes, IntersectionFlags.None, aliasSymbol, aliasTypeArguments)
                : GetUnionType(propTypes, UnionReduction.Literal, aliasSymbol, aliasTypeArguments);
        }
        return GetPropertyTypeForIndexType(objectType, apparentObjectType, indexType, indexType, accessNode, accessFlags | AccessFlags.CacheSymbol | AccessFlags.ReportDeprecated);
    }

    /// <summary>ported from: src/compiler/checker.ts:19722-19731 — <c>getTypeFromIndexedAccessTypeNode</c>.</summary>
    internal IType GetTypeFromIndexedAccessTypeNode(IndexedAccessTypeNode node)
    {
        var links = GetNodeLinks(node);
        if (links.ResolvedType is null)
        {
            var objectType = GetTypeFromTypeNode(node.ObjectType!);
            var indexType = GetTypeFromTypeNode(node.IndexType!);
            var potentialAlias = GetAliasSymbolForTypeNode(node);
            links.ResolvedType = GetIndexedAccessType(objectType, indexType, AccessFlags.None, node, potentialAlias, GetTypeArgumentsForAliasSymbol(potentialAlias));
        }
        return Debug.CheckDefined(links.ResolvedType);
    }
}
