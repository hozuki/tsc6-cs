namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:1313-1322 (typeofNEFacts)
// ported from: src/compiler/checker.ts:2061-2116 (the tables and intrinsic types createTypeChecker opens with)
// ported from: src/compiler/checker.ts:2123-2147 (the type mappers built in that block)
// ported from: src/compiler/checker.ts:2149-2199 (the empty/anonymous built-ins, the markers, the signatures, the index infos and the iteration types)
// ported from: src/compiler/checker.ts:2427-2434 (getCachedType, setCachedType)
// ported from: src/compiler/checker.ts:5510-5515 (symbolIsValue)
// ported from: src/compiler/checker.ts:5517-5573 (createType … createTypeParameter)
// ported from: src/compiler/checker.ts:5575-5676 (isReservedMemberName … getResolvedTypeWithoutAbstractConstructSignatures)
// ported from: src/compiler/checker.ts:53821-53860 (compareSymbols, compareNodes — landed here, see their remarks)
// ported from: src/compiler/checker.ts:16683-16685 (createIndexInfo)
// ported from: src/compiler/checker.ts:45842-45865 (createIterationTypes)
/// <summary>
/// The type constructors — every type the checker ever builds goes through <see cref="CreateType"/> — together with
/// the built-in type slots the rest of the checker reads and the symbol-table shape helpers the constructors need.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS's <c>createType(flags)</c> is <c>new Type(checker, flags)</c> — one runtime class for every shape,
/// distinguished afterwards by <c>as</c> casts that its structural type system always allows. C# is nominal, so
/// <see cref="CreateType"/> is the <c>TypeFlags → class</c> dispatch over M6-0's classes
/// (<c>Model/Type.cs</c>, <c>Model/ObjectType.cs</c>, …) and every cast TS performs on a type therefore still
/// succeeds, because each class carries its family's slots and each family's leaf is the class the factory builds
/// (the contract M6-0's <c>Model/Type.cs</c> remarks state). The dispatch order is the one the flag combinations
/// force: <c>EnumLiteral | StringLiteral</c> has to reach the literal class, <c>BooleanLiteral</c> the boolean one,
/// and the plain <c>Intrinsic</c> mask last, because it includes both of those bits.
/// </para>
/// <para>
/// PORT-NOTE: <c>createOriginType</c> deliberately does <em>not</em> dispatch — TS builds a bare <c>Type</c> there and
/// the port does the same, so an origin type is never mistaken for one of the shaped classes.
/// </para>
/// <para>
/// PORT-NOTE: every constructor here passes <c>this</c> for the <c>Type</c>/<c>Signature</c> constructor's
/// <c>checker</c> argument, exactly as the reference passes its <c>checker</c> literal. That requires
/// <see cref="Checker"/> to implement <see cref="ITypeChecker"/> — the <c>TypeChecker</c> object literal at
/// checker.ts:1610 is <c>createTypeChecker</c>'s return value, i.e. the assembly member M6-5/M6-6 lands (it is what
/// <c>Program.getTypeChecker()</c> hands back). Until that declaration exists these constructors do not convert
/// <c>this</c>; see the slice report.
/// </para>
/// <para>
/// PORT-NOTE: the built-in type slots (<c>checker.ts:2061-2199</c>) are state of the checker, but the factories that
/// give them their values are this file's, so M6-0's contract puts the slots here: they are declared as fields and
/// assigned by <see cref="InitializeBuiltinTypes"/>, which <c>createTypeChecker</c>'s assembly must call before it
/// builds any other type (checker.ts:2076 is where the reference does it, right after the option flags and the two
/// link tables). The iteration-types cache and the two iteration-types resolvers, the permissive/restrictive mappers
/// and the <c>global*Type</c> slots are <em>not</em> here: the first three belong to the generics slice (M6Generics)
/// and the global slots are filled by <c>initializeTypeChecker</c> (M6-5).
/// </para>
/// <para>
/// PORT-NOTE: <c>createSymbol</c> is called from this block (checker.ts:2067-2068) and lives in
/// <c>Checker.Symbols.cs</c>, the same split the reference has between a function's declaration and its use.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // The state these constructors hang off (checker.ts:2061-2076)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:2061 — <c>decoratorContextOverrideTypeCache</c>.</summary>
    internal Dictionary<string, IType> _decoratorContextOverrideTypeCache = null!;

    /// <summary>ported from: src/compiler/checker.ts:2062 — <c>cachedTypes</c>.</summary>
    internal Dictionary<string, IType> _cachedTypes = null!;

    /// <summary>ported from: src/compiler/checker.ts:2063 — <c>evolvingArrayTypes</c>.</summary>
    internal List<IEvolvingArrayType> _evolvingArrayTypes = null!;

    /// <summary>ported from: src/compiler/checker.ts:2064 — <c>undefinedProperties</c>.</summary>
    internal SymbolTable _undefinedProperties = null!;

    /// <summary>ported from: src/compiler/checker.ts:2065 — <c>markerTypes</c>.</summary>
    internal HashSet<int> _markerTypes = null!;

    /// <summary>ported from: src/compiler/checker.ts:2067 — <c>unknownSymbol</c>.</summary>
    internal ISymbol _unknownSymbol = null!;

    /// <summary>ported from: src/compiler/checker.ts:2068 — <c>resolvingSymbol</c>.</summary>
    internal ISymbol _resolvingSymbol = null!;

    /// <summary>ported from: src/compiler/checker.ts:2069 — <c>unresolvedSymbols</c>.</summary>
    internal Dictionary<string, ITransientSymbol> _unresolvedSymbols = null!;

    /// <summary>ported from: src/compiler/checker.ts:2070 — <c>errorTypes</c>.</summary>
    internal Dictionary<string, IType> _errorTypes = null!;

    /// <summary>ported from: src/compiler/checker.ts:2076 — <c>seenIntrinsicNames</c>.</summary>
    internal HashSet<string> _seenIntrinsicNames = null!;

    // ---------------------------------------------------------------------------------------------
    // The built-in types (checker.ts:2078-2199)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:2078 — <c>anyType</c>.</summary>
    internal IType _anyType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2079 — <c>autoType</c>.</summary>
    internal IType _autoType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2080 — <c>wildcardType</c>.</summary>
    internal IType _wildcardType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2081 — <c>blockedStringType</c>.</summary>
    internal IType _blockedStringType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2082 — <c>errorType</c>.</summary>
    internal IType _errorType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2083 — <c>unresolvedType</c>.</summary>
    internal IType _unresolvedType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2084 — <c>nonInferrableAnyType</c>.</summary>
    internal IType _nonInferrableAnyType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2085 — <c>intrinsicMarkerType</c>.</summary>
    internal IType _intrinsicMarkerType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2086 — <c>unknownType</c>.</summary>
    internal IType _unknownType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2087 — <c>undefinedType</c>.</summary>
    internal IType _undefinedType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2088 — <c>undefinedWideningType</c>.</summary>
    internal IType _undefinedWideningType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2089 — <c>missingType</c>.</summary>
    internal IType _missingType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2090 — <c>undefinedOrMissingType</c>.</summary>
    internal IType _undefinedOrMissingType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2091 — <c>optionalType</c>.</summary>
    internal IType _optionalType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2092 — <c>nullType</c>.</summary>
    internal IType _nullType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2093 — <c>nullWideningType</c>.</summary>
    internal IType _nullWideningType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2094 — <c>stringType</c>.</summary>
    internal IType _stringType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2095 — <c>numberType</c>.</summary>
    internal IType _numberType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2096 — <c>bigintType</c>.</summary>
    internal IType _bigintType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2097 — <c>falseType</c>.</summary>
    internal IFreshableIntrinsicType _falseType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2098 — <c>regularFalseType</c>.</summary>
    internal IFreshableIntrinsicType _regularFalseType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2099 — <c>trueType</c>.</summary>
    internal IFreshableIntrinsicType _trueType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2100 — <c>regularTrueType</c>.</summary>
    internal IFreshableIntrinsicType _regularTrueType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2109 — <c>booleanType</c>.</summary>
    internal IType _booleanType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2110 — <c>esSymbolType</c>.</summary>
    internal IType _esSymbolType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2111 — <c>voidType</c>.</summary>
    internal IType _voidType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2112 — <c>neverType</c>.</summary>
    internal IType _neverType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2113 — <c>silentNeverType</c>.</summary>
    internal IType _silentNeverType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2114 — <c>implicitNeverType</c>.</summary>
    internal IType _implicitNeverType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2115 — <c>unreachableNeverType</c>.</summary>
    internal IType _unreachableNeverType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2116 — <c>nonPrimitiveType</c>.</summary>
    internal IType _nonPrimitiveType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2117 — <c>stringOrNumberType</c>.</summary>
    internal IType _stringOrNumberType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2118 — <c>stringNumberSymbolType</c>.</summary>
    internal IType _stringNumberSymbolType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2119 — <c>numberOrBigIntType</c>.</summary>
    internal IType _numberOrBigIntType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2120 — <c>templateConstraintType</c>.</summary>
    internal IUnionType _templateConstraintType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2121 — <c>numericStringType</c> (the <c>${number}</c> type).</summary>
    internal IType _numericStringType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2130 — <c>uniqueLiteralType</c>.</summary>
    internal IType _uniqueLiteralType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2131 — <c>uniqueLiteralMapper</c>.</summary>
    internal TypeMapper _uniqueLiteralMapper = null!;

    /// <summary>ported from: src/compiler/checker.ts:2132 — <c>outofbandVarianceMarkerHandler</c>.</summary>
    internal Action<bool>? _outofbandVarianceMarkerHandler;

    /// <summary>ported from: src/compiler/checker.ts:2133 — <c>reportUnreliableMapper</c>.</summary>
    internal TypeMapper _reportUnreliableMapper = null!;

    /// <summary>ported from: src/compiler/checker.ts:2141 — <c>reportUnmeasurableMapper</c>.</summary>
    internal TypeMapper _reportUnmeasurableMapper = null!;

    /// <summary>ported from: src/compiler/checker.ts:2149 — <c>emptyObjectType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: typed as the object family's class rather than <c>IResolvedType</c>: <c>IObjectType</c> and
    /// <c>IUnionOrIntersectionType</c> both declare <c>objectFlags</c>, so writing it through the interface would be an
    /// ambiguity (CS0229).
    /// </remarks>
    internal ResolvedType _emptyObjectType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2150 — <c>emptyJsxObjectType</c>.</summary>
    internal ResolvedType _emptyJsxObjectType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2152 — <c>emptyFreshJsxObjectType</c>.</summary>
    internal ResolvedType _emptyFreshJsxObjectType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2156 — <c>emptyTypeLiteralSymbol</c>.</summary>
    internal ISymbol _emptyTypeLiteralSymbol = null!;

    /// <summary>ported from: src/compiler/checker.ts:2158 — <c>emptyTypeLiteralType</c>.</summary>
    internal ResolvedType _emptyTypeLiteralType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2160 — <c>unknownEmptyObjectType</c>.</summary>
    internal ResolvedType _unknownEmptyObjectType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2161 — <c>unknownUnionType</c>.</summary>
    internal IType _unknownUnionType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2163 — <c>emptyGenericType</c>.</summary>
    internal IGenericType _emptyGenericType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2166 — <c>anyFunctionType</c>.</summary>
    internal ResolvedType _anyFunctionType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2171 — <c>noConstraintType</c>.</summary>
    internal ResolvedType _noConstraintType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2172 — <c>circularConstraintType</c>.</summary>
    internal ResolvedType _circularConstraintType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2173 — <c>resolvingDefaultType</c>.</summary>
    internal ResolvedType _resolvingDefaultType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2175 — <c>markerSuperType</c>.</summary>
    internal ITypeParameter _markerSuperType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2176 — <c>markerSubType</c>.</summary>
    internal ITypeParameter _markerSubType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2178 — <c>markerOtherType</c>.</summary>
    internal ITypeParameter _markerOtherType = null!;

    /// <summary>ported from: src/compiler/checker.ts:2180 — <c>markerSuperTypeForCheck</c>.</summary>
    internal ITypeParameter _markerSuperTypeForCheck = null!;

    /// <summary>ported from: src/compiler/checker.ts:2181 — <c>markerSubTypeForCheck</c>.</summary>
    internal ITypeParameter _markerSubTypeForCheck = null!;

    /// <summary>ported from: src/compiler/checker.ts:2184 — <c>noTypePredicate</c>.</summary>
    internal ITypePredicateBase _noTypePredicate = null!;

    /// <summary>ported from: src/compiler/checker.ts:2186 — <c>anySignature</c>.</summary>
    internal ISignature _anySignature = null!;

    /// <summary>ported from: src/compiler/checker.ts:2188 — <c>unknownSignature</c>.</summary>
    internal ISignature _unknownSignature = null!;

    /// <summary>ported from: src/compiler/checker.ts:2189 — <c>resolvingSignature</c>.</summary>
    internal ISignature _resolvingSignature = null!;

    /// <summary>ported from: src/compiler/checker.ts:2190 — <c>silentNeverSignature</c>.</summary>
    internal ISignature _silentNeverSignature = null!;

    /// <summary>ported from: src/compiler/checker.ts:2192 — <c>enumNumberIndexInfo</c>.</summary>
    internal IIndexInfo _enumNumberIndexInfo = null!;

    /// <summary>ported from: src/compiler/checker.ts:2193 — <c>anyBaseTypeIndexInfo</c>.</summary>
    internal IIndexInfo _anyBaseTypeIndexInfo = null!;

    /// <summary>ported from: src/compiler/checker.ts:2187 — <c>noIterationTypes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's sentinel is an object literal whose three getters call <c>Debug.fail</c>; it is only ever
    /// compared by identity before any member is read (<c>Model/IterationTypes.cs</c> states the same), so the port
    /// builds it with null payloads.
    /// </remarks>
    internal IIterationTypes _noIterationTypes = null!;

    /// <summary>ported from: src/compiler/checker.ts:2198-2199 — <c>anyIterationTypes</c>.</summary>
    internal IIterationTypes _anyIterationTypes = null!;

    // ported from: src/compiler/checker.ts:1313-1322 — `typeofNEFacts`.
    /// <summary>
    /// The <c>typeof</c> facts of the eight names <c>typeof x</c> can produce, in the reference's insertion order
    /// (which <see cref="CreateTypeofType"/> turns into the <c>"string" | "number" | …</c> union).
    /// </summary>
    internal static readonly Dictionary<string, TypeFacts> s_typeofNEFacts = new(StringComparer.Ordinal)
    {
        ["string"] = TypeFacts.TypeofNEString,
        ["number"] = TypeFacts.TypeofNENumber,
        ["bigint"] = TypeFacts.TypeofNEBigInt,
        ["boolean"] = TypeFacts.TypeofNEBoolean,
        ["symbol"] = TypeFacts.TypeofNESymbol,
        ["undefined"] = TypeFacts.NEUndefined,
        ["object"] = TypeFacts.TypeofNEObject,
        ["function"] = TypeFacts.TypeofNEFunction,
    };

    /// <summary>
    /// ported from: src/compiler/checker.ts:2061-2199 — the built-in type block <c>createTypeChecker</c> opens with.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS runs this block as the top-level statements of <c>createTypeChecker</c>; the port states it as a
    /// member because the state it fills is instance state (M6-0's <c>Checker.cs</c> remarks). It must be the first
    /// thing <c>createTypeChecker</c>'s assembly calls after the option flags and the two link tables, because
    /// everything the checker builds afterwards reads these slots.
    /// </remarks>
    internal void InitializeBuiltinTypes()
    {
        // checker.ts:2061-2076
        _decoratorContextOverrideTypeCache = new Dictionary<string, IType>(StringComparer.Ordinal);
        _cachedTypes = new Dictionary<string, IType>(StringComparer.Ordinal);
        _evolvingArrayTypes = [];
        _undefinedProperties = Utilities.CreateSymbolTable();
        _markerTypes = [];
        _unknownSymbol = CreateSymbol(SymbolFlags.Property, "unknown");
        _resolvingSymbol = CreateSymbol(SymbolFlags.None, InternalSymbolName.Resolving.Value);
        _unresolvedSymbols = new Dictionary<string, ITransientSymbol>(StringComparer.Ordinal);
        _errorTypes = new Dictionary<string, IType>(StringComparer.Ordinal);
        _seenIntrinsicNames = new HashSet<string>(StringComparer.Ordinal);

        // checker.ts:2078-2116 — the intrinsics, in the reference's order: the ids are observable in a union's
        // constituent order, which is why the reference creates `undefined` and `null` before anything that can
        // contain them.
        _anyType = CreateIntrinsicType(TypeFlags.Any, "any");
        _autoType = CreateIntrinsicType(TypeFlags.Any, "any", ObjectFlags.NonInferrableType, "auto");
        _wildcardType = CreateIntrinsicType(TypeFlags.Any, "any", /*objectFlags*/ ObjectFlags.None, "wildcard");
        _blockedStringType = CreateIntrinsicType(TypeFlags.Any, "any", /*objectFlags*/ ObjectFlags.None, "blocked string");
        _errorType = CreateIntrinsicType(TypeFlags.Any, "error");
        _unresolvedType = CreateIntrinsicType(TypeFlags.Any, "unresolved");
        _nonInferrableAnyType = CreateIntrinsicType(TypeFlags.Any, "any", ObjectFlags.ContainsWideningType, "non-inferrable");
        _intrinsicMarkerType = CreateIntrinsicType(TypeFlags.Any, "intrinsic");
        _unknownType = CreateIntrinsicType(TypeFlags.Unknown, "unknown");
        _undefinedType = CreateIntrinsicType(TypeFlags.Undefined, "undefined");
        _undefinedWideningType = _strictNullChecks ? _undefinedType : CreateIntrinsicType(TypeFlags.Undefined, "undefined", ObjectFlags.ContainsWideningType, "widening");
        _missingType = CreateIntrinsicType(TypeFlags.Undefined, "undefined", /*objectFlags*/ ObjectFlags.None, "missing");
        _undefinedOrMissingType = _exactOptionalPropertyTypes == true ? _missingType : _undefinedType;
        _optionalType = CreateIntrinsicType(TypeFlags.Undefined, "undefined", /*objectFlags*/ ObjectFlags.None, "optional");
        _nullType = CreateIntrinsicType(TypeFlags.Null, "null");
        _nullWideningType = _strictNullChecks ? _nullType : CreateIntrinsicType(TypeFlags.Null, "null", ObjectFlags.ContainsWideningType, "widening");
        _stringType = CreateIntrinsicType(TypeFlags.String, "string");
        _numberType = CreateIntrinsicType(TypeFlags.Number, "number");
        _bigintType = CreateIntrinsicType(TypeFlags.BigInt, "bigint");
        _falseType = CreateIntrinsicType(TypeFlags.BooleanLiteral, "false", /*objectFlags*/ ObjectFlags.None, "fresh");
        _regularFalseType = CreateIntrinsicType(TypeFlags.BooleanLiteral, "false");
        _trueType = CreateIntrinsicType(TypeFlags.BooleanLiteral, "true", /*objectFlags*/ ObjectFlags.None, "fresh");
        _regularTrueType = CreateIntrinsicType(TypeFlags.BooleanLiteral, "true");
        _trueType.RegularType = _regularTrueType;
        _trueType.FreshType = _trueType;
        _regularTrueType.RegularType = _regularTrueType;
        _regularTrueType.FreshType = _trueType;
        _falseType.RegularType = _regularFalseType;
        _falseType.FreshType = _falseType;
        _regularFalseType.RegularType = _regularFalseType;
        _regularFalseType.FreshType = _falseType;
        _booleanType = GetUnionType([_regularFalseType, _regularTrueType]);
        _esSymbolType = CreateIntrinsicType(TypeFlags.ESSymbol, "symbol");
        _voidType = CreateIntrinsicType(TypeFlags.Void, "void");
        _neverType = CreateIntrinsicType(TypeFlags.Never, "never");
        _silentNeverType = CreateIntrinsicType(TypeFlags.Never, "never", ObjectFlags.NonInferrableType, "silent");
        _implicitNeverType = CreateIntrinsicType(TypeFlags.Never, "never", /*objectFlags*/ ObjectFlags.None, "implicit");
        _unreachableNeverType = CreateIntrinsicType(TypeFlags.Never, "never", /*objectFlags*/ ObjectFlags.None, "unreachable");
        _nonPrimitiveType = CreateIntrinsicType(TypeFlags.NonPrimitive, "object");
        _stringOrNumberType = GetUnionType([_stringType, _numberType]);
        _stringNumberSymbolType = GetUnionType([_stringType, _numberType, _esSymbolType]);
        _numberOrBigIntType = GetUnionType([_numberType, _bigintType]);
        _templateConstraintType = (IUnionType)GetUnionType([_stringType, _numberType, _booleanType, _bigintType, _nullType, _undefinedType]);
        _numericStringType = GetTemplateLiteralType(["", ""], [_numberType]); // The `${number}` type

        // checker.ts:2130-2147 — the unique-literal type and the three inspectors. PORT-NOTE: `makeFunctionTypeMapper`
        // is the generics slice's (M6Generics); the slots are here because they are built in this block.
        _uniqueLiteralType = CreateIntrinsicType(TypeFlags.Never, "never", /*objectFlags*/ ObjectFlags.None, "unique literal");
        _uniqueLiteralMapper = MakeFunctionTypeMapper(t => (t.Flags & TypeFlags.TypeParameter) != 0 ? _uniqueLiteralType : t, () => "(unique literal mapper)");
        _reportUnreliableMapper = MakeFunctionTypeMapper(t =>
        {
            if (_outofbandVarianceMarkerHandler is not null && (ReferenceEquals(t, _markerSuperType) || ReferenceEquals(t, _markerSubType) || ReferenceEquals(t, _markerOtherType)))
            {
                _outofbandVarianceMarkerHandler(true);
            }

            return t;
        }, () => "(unmeasurable reporter)");
        _reportUnmeasurableMapper = MakeFunctionTypeMapper(t =>
        {
            if (_outofbandVarianceMarkerHandler is not null && (ReferenceEquals(t, _markerSuperType) || ReferenceEquals(t, _markerSubType) || ReferenceEquals(t, _markerOtherType)))
            {
                _outofbandVarianceMarkerHandler(false);
            }

            return t;
        }, () => "(unreliable reporter)");

        // checker.ts:2149-2168
        _emptyObjectType = CreateAnonymousType(/*symbol*/ null, _emptySymbols, [], [], []);
        _emptyJsxObjectType = CreateAnonymousType(/*symbol*/ null, _emptySymbols, [], [], []);
        _emptyJsxObjectType.ObjectFlags |= ObjectFlags.JsxAttributes;
        _emptyFreshJsxObjectType = CreateAnonymousType(/*symbol*/ null, _emptySymbols, [], [], []);
        _emptyFreshJsxObjectType.ObjectFlags |= ObjectFlags.JsxAttributes | ObjectFlags.FreshLiteral | ObjectFlags.ObjectLiteral | ObjectFlags.ContainsObjectOrArrayLiteral;
        _emptyTypeLiteralSymbol = CreateSymbol(SymbolFlags.TypeLiteral, InternalSymbolName.Type.Value);
        _emptyTypeLiteralSymbol.Members = Utilities.CreateSymbolTable();
        _emptyTypeLiteralType = CreateAnonymousType(_emptyTypeLiteralSymbol, _emptySymbols, [], [], []);
        _unknownEmptyObjectType = CreateAnonymousType(/*symbol*/ null, _emptySymbols, [], [], []);
        _unknownUnionType = _strictNullChecks ? GetUnionType([_undefinedType, _nullType, _unknownEmptyObjectType]) : _unknownType;
        _emptyGenericType = (IGenericType)CreateAnonymousType(/*symbol*/ null, _emptySymbols, [], [], []);
        _emptyGenericType.Instantiations = new Dictionary<string, ITypeReference>(StringComparer.Ordinal);
        _anyFunctionType = CreateAnonymousType(/*symbol*/ null, _emptySymbols, [], [], []);
        // The anyFunctionType contains the anyFunctionType by definition. The flag is further propagated
        // in getPropagatingFlagsOfTypes, and it is checked in inferFromTypes.
        _anyFunctionType.ObjectFlags |= ObjectFlags.NonInferrableType;
        _noConstraintType = CreateAnonymousType(/*symbol*/ null, _emptySymbols, [], [], []);
        _circularConstraintType = CreateAnonymousType(/*symbol*/ null, _emptySymbols, [], [], []);
        _resolvingDefaultType = CreateAnonymousType(/*symbol*/ null, _emptySymbols, [], [], []);

        // checker.ts:2175-2182
        _markerSuperType = CreateTypeParameter();
        _markerSubType = CreateTypeParameter();
        _markerSubType.Constraint = _markerSuperType;
        _markerOtherType = CreateTypeParameter();
        _markerSuperTypeForCheck = CreateTypeParameter();
        _markerSubTypeForCheck = CreateTypeParameter();
        _markerSubTypeForCheck.Constraint = _markerSuperTypeForCheck;

        // checker.ts:2184-2193
        _noTypePredicate = new TypePredicate(TypePredicateKind.Identifier, "<<unresolved>>", 0, _anyType);
        _anySignature = CreateSignature(/*declaration*/ null, /*typeParameters*/ null, /*thisParameter*/ null, [], _anyType, /*resolvedTypePredicate*/ null, 0, SignatureFlags.None);
        _unknownSignature = CreateSignature(/*declaration*/ null, /*typeParameters*/ null, /*thisParameter*/ null, [], _errorType, /*resolvedTypePredicate*/ null, 0, SignatureFlags.None);
        _resolvingSignature = CreateSignature(/*declaration*/ null, /*typeParameters*/ null, /*thisParameter*/ null, [], _anyType, /*resolvedTypePredicate*/ null, 0, SignatureFlags.None);
        _silentNeverSignature = CreateSignature(/*declaration*/ null, /*typeParameters*/ null, /*thisParameter*/ null, [], _silentNeverType, /*resolvedTypePredicate*/ null, 0, SignatureFlags.None);
        _enumNumberIndexInfo = CreateIndexInfo(_numberType, _stringType, /*isReadonly*/ true);
        _anyBaseTypeIndexInfo = CreateIndexInfo(_stringType, _anyType, /*isReadonly*/ false);

        // checker.ts:2185-2199 — PORT-NOTE: the iteration-types cache and the two resolvers belong to the generics
        // slice (M6Generics); only the two sentinels are built here.
        _noIterationTypes = new IterationTypes(null, null, null);
        _anyIterationTypes = CreateIterationTypes(_anyType, _anyType, _anyType);
    }

    // ---------------------------------------------------------------------------------------------
    // Cached types (checker.ts:2427-2434)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:2427-2429 — <c>getCachedType</c>.</summary>
    internal IType? GetCachedType(string? key) => key is null ? null : _cachedTypes.GetValueOrDefault(key);

    /// <summary>ported from: src/compiler/checker.ts:2431-2434 — <c>setCachedType</c>.</summary>
    internal IType SetCachedType(string? key, IType type)
    {
        if (key is not null) _cachedTypes[key] = type;
        return type;
    }

    // ---------------------------------------------------------------------------------------------
    // The type constructors (checker.ts:5510-5573)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:5510-5515 — <c>symbolIsValue</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the body calls <c>getSymbolFlags</c> (checker.ts:4351, the alias/type-only block), which is not this
    /// slice's; see the report's "referenced but not declared" list.
    /// </remarks>
    internal bool SymbolIsValue(ISymbol symbol, bool? includeTypeOnlyMembers = null)
        => (symbol.Flags & SymbolFlags.Value) != 0
            || ((symbol.Flags & SymbolFlags.Alias) != 0 && (GetSymbolFlags(symbol, !(includeTypeOnlyMembers ?? false)) & SymbolFlags.Value) != 0);

    /// <summary>ported from: src/compiler/checker.ts:5517-5523 — <c>createType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: this is the <c>TypeFlags → class</c> dispatch the whole checker relies on (see the class remarks).
    /// TS's <c>new Type(checker, flags)</c> becomes <see cref="CreateTypeInstance"/>; the id is assigned from
    /// <c>typeCount</c> in the reference's order (increment, then assign), which is what makes ids 1-based and
    /// contiguous — <c>tools/TypeDiff</c>'s numbering and <see cref="CreateOriginType"/> both depend on it.
    /// </remarks>
    internal Type CreateType(TypeFlags flags)
    {
        var result = CreateTypeInstance(flags);
        _typeCount++;
        result.Id = _typeCount;
        Tracing.Current?.RecordType(result);
        return result;
    }

    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — the one type constructor, dispatched by flags.</summary>
    private Type CreateTypeInstance(TypeFlags flags)
    {
        if ((flags & TypeFlags.BooleanLiteral) != 0) return new BooleanLiteralType(this, flags);
        if ((flags & TypeFlags.Object) != 0) return new InstantiationExpressionType(this, flags);
        if ((flags & TypeFlags.Union) != 0) return new UnionType(this, flags);
        if ((flags & TypeFlags.Intersection) != 0) return new IntersectionType(this, flags);
        if ((flags & TypeFlags.Enum) != 0) return new EnumType(this, flags);
        if ((flags & TypeFlags.StringLiteral) != 0) return new StringLiteralType(this, flags);
        if ((flags & TypeFlags.NumberLiteral) != 0) return new NumberLiteralType(this, flags);
        if ((flags & TypeFlags.BigIntLiteral) != 0) return new BigIntLiteralType(this, flags);
        if ((flags & TypeFlags.EnumLiteral) != 0) return new LiteralType(this, flags);
        if ((flags & TypeFlags.UniqueESSymbol) != 0) return new UniqueESSymbolType(this, flags);
        if ((flags & TypeFlags.TypeParameter) != 0) return new TypeParameter(this, flags);
        if ((flags & TypeFlags.Index) != 0) return new IndexType(this, flags);
        if ((flags & TypeFlags.IndexedAccess) != 0) return new IndexedAccessType(this, flags);
        if ((flags & TypeFlags.Conditional) != 0) return new ConditionalType(this, flags);
        if ((flags & TypeFlags.Substitution) != 0) return new SubstitutionType(this, flags);
        if ((flags & TypeFlags.TemplateLiteral) != 0) return new TemplateLiteralType(this, flags);
        if ((flags & TypeFlags.StringMapping) != 0) return new StringMappingType(this, flags);
        if ((flags & TypeFlags.Intrinsic) != 0) return new IntrinsicType(this, flags);
        return new Type(this, flags);
    }

    /// <summary>ported from: src/compiler/checker.ts:5525-5529 — <c>createTypeWithSymbol</c>.</summary>
    internal Type CreateTypeWithSymbol(TypeFlags flags, ISymbol symbol)
    {
        var result = CreateType(flags);
        result.Symbol = symbol;
        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:5531-5533 — <c>createOriginType</c>.</summary>
    /// <remarks>PORT-NOTE: deliberately a bare <see cref="Type"/>, as upstream; it does not go through the dispatch.</remarks>
    internal Type CreateOriginType(TypeFlags flags) => new(this, flags);

    /// <summary>ported from: src/compiler/checker.ts:5535-5542 — <c>createIntrinsicType</c>.</summary>
    internal IntrinsicType CreateIntrinsicType(TypeFlags kind, string intrinsicName, ObjectFlags objectFlags = ObjectFlags.None, string? debugIntrinsicName = null)
    {
        CheckIntrinsicName(intrinsicName, debugIntrinsicName);
        var type = (IntrinsicType)CreateType(kind);
        type.IntrinsicName = intrinsicName;
        type.DebugIntrinsicName = debugIntrinsicName;
        type.ObjectFlags = objectFlags | ObjectFlags.CouldContainTypeVariablesComputed | ObjectFlags.IsGenericTypeComputed | ObjectFlags.IsUnknownLikeUnionComputed | ObjectFlags.IsNeverIntersectionComputed;
        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:5544-5550 — <c>checkIntrinsicName</c>.</summary>
    private void CheckIntrinsicName(string name, string? debug)
    {
        var key = $"{name},{debug ?? ""}";
        if (!_seenIntrinsicNames.Add(key))
        {
            Debug.Fail($"Duplicate intrinsic type name {name}{(debug is not null ? $" ({debug})" : "")}; you may need to pass a name to createIntrinsicType.");
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:5552-5561 — <c>createObjectType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's generic <c>createObjectType&lt;T extends ObjectType&gt;</c> lets the caller name the face; the
    /// port returns <see cref="ObjectType"/> and the call sites downcast, which is the same instruction with the face
    /// written where TS inferred it. The instance is the object family's leaf (<c>InstantiationExpressionType</c>), so
    /// every such downcast succeeds (M6-0's <c>Model/ObjectType.cs</c> remarks).
    /// </remarks>
    internal ObjectType CreateObjectType(ObjectFlags objectFlags, ISymbol? symbol = null)
    {
        var type = (ObjectType)(symbol is null ? CreateType(TypeFlags.Object) : CreateTypeWithSymbol(TypeFlags.Object, symbol));
        type.ObjectFlags = objectFlags;
        type.Members = null;
        type.Properties = null;
        type.CallSignatures = null;
        type.ConstructSignatures = null;
        type.IndexInfos = null;
        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:5563-5565 — <c>createTypeofType</c>.</summary>
    internal IType CreateTypeofType()
    {
        var keys = new List<string>(s_typeofNEFacts.Keys);
        // checker.ts:5564 — `[...typeofNEFacts.keys()].sort()` under stableTypeOrdering, the insertion order otherwise.
        IList<string> names = _stableTypeOrdering ? Core.ToSorted(keys, (a, b) => Core.CompareStringsCaseSensitive(a, b))! : keys;
        return GetUnionType(Core.Map(names, (string name, int _) => GetStringLiteralType(name)));
    }

    /// <summary>ported from: src/compiler/checker.ts:5567-5569 — <c>createTypeParameter</c>.</summary>
    internal TypeParameter CreateTypeParameter(ISymbol? symbol = null)
        => (TypeParameter)(symbol is null ? CreateType(TypeFlags.TypeParameter) : CreateTypeWithSymbol(TypeFlags.TypeParameter, symbol));

    // ---------------------------------------------------------------------------------------------
    // The named members of a symbol table (checker.ts:5575-5676)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:5571-5580 — <c>isReservedMemberName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads <c>charCodeAt(2)</c>, which answers <c>NaN</c> past the end of the string and therefore
    /// compares unequal to all three characters — i.e. the name <c>"__"</c> is reserved. <c>ScannerUtilities.CharCodeAt</c>
    /// answers <c>CharacterCodes.EOF</c> there, which is unequal to all three too, so the two agree.
    /// </remarks>
    internal static bool IsReservedMemberName(string name)
        => ScannerUtilities.CharCodeAt(name, 0) == '_'
            && ScannerUtilities.CharCodeAt(name, 1) == '_'
            && ScannerUtilities.CharCodeAt(name, 2) != '_'
            && ScannerUtilities.CharCodeAt(name, 2) != '@'
            && ScannerUtilities.CharCodeAt(name, 2) != '#';

    /// <summary>ported from: src/compiler/checker.ts:5583-5625 — <c>getNamedMembers</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's tail is <c>concatenate(contained, nonContained) ?? emptyArray</c>; the port states the same
    /// three cases directly so the result is a <c>List</c> without a cast.
    /// </remarks>
    internal List<ISymbol> GetNamedMembers(SymbolTable members, ISymbol? container)
    {
        if (!_stableTypeOrdering)
        {
            List<ISymbol>? flat = null;
            members.ForEach((symbol, id) =>
            {
                if (IsNamedMember(symbol, id))
                {
                    (flat ??= []).Add(symbol);
                }
            });
            return flat ?? [];
        }

        if (members.Count == 0)
        {
            return [];
        }

        // For classes and interfaces, we store explicitly declared members ahead of inherited members. This ensures we
        // process explicitly declared members first in type relations, which is beneficial because explicitly declared
        // members are more likely to contain discriminating differences (typescript-go#1968).
        List<ISymbol>? contained = null;
        if (container is not null && (container.Flags & (SymbolFlags.Class | SymbolFlags.Interface)) != 0)
        {
            members.ForEach((symbol, id) =>
            {
                if (IsNamedMember(symbol, id) && IsDeclarationContainedBy(symbol, container))
                {
                    (contained ??= []).Add(symbol);
                }
            });
        }

        List<ISymbol>? nonContained = null;
        members.ForEach((symbol, id) =>
        {
            if (IsNamedMember(symbol, id) && (container is null || (container.Flags & (SymbolFlags.Class | SymbolFlags.Interface)) == 0 || !IsDeclarationContainedBy(symbol, container)))
            {
                (nonContained ??= []).Add(symbol);
            }
        });

        contained?.Sort(CompareSymbols);
        nonContained?.Sort(CompareSymbols);
        if (contained is null) return nonContained ?? [];
        if (nonContained is null) return contained;
        return [.. contained, .. nonContained];

        static bool IsDeclarationContainedBy(ISymbol symbol, ISymbol container)
        {
            var declaration = symbol.ValueDeclaration;
            if (declaration is not null && container.Declarations is not null)
            {
                foreach (var d in container.Declarations)
                {
                    // PORT-NOTE: TS nests `containedBy` inside this function; the port states its one comparison inline,
                    // because a second static local function cannot be called from a static one (CS8422).
                    var a = (Node)declaration;
                    var b = (Node)d;
                    if (b.Pos <= a.Pos && b.End >= a.End)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:5638-5640 — <c>isNamedMember</c>.</summary>
    internal bool IsNamedMember(ISymbol member, string escapedName)
        => !IsReservedMemberName(escapedName) && SymbolIsValue(member);

    /// <summary>ported from: src/compiler/checker.ts:5642-5647 — <c>getNamedOrIndexSignatureMembers</c>.</summary>
    internal List<ISymbol> GetNamedOrIndexSignatureMembers(SymbolTable members, ISymbol symbol)
    {
        var result = GetNamedMembers(members, symbol);
        var index = GetIndexSymbolFromSymbolTable(members);
        return index is not null ? [.. result, index] : result;
    }

    /// <summary>ported from: src/compiler/checker.ts:5649-5662 — <c>setStructuredTypeMembers</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS casts to <c>ResolvedType</c> because its one runtime object is always structurally that; in the
    /// port <c>ResolvedType</c> is a class of the <em>object</em> family only, and this function is also called for
    /// unions and intersections (<c>resolveUnionTypeMembers</c>). The five slots it writes are declared on
    /// <see cref="UnionOrIntersectionType"/> (M6-0, forced there by <c>IterableOrIteratorType</c>), which is what makes
    /// the write legal for every caller; the object-family callers downcast the result where the reference's
    /// declaration says <c>ResolvedType</c>.
    /// </remarks>
    internal UnionOrIntersectionType SetStructuredTypeMembers(UnionOrIntersectionType type, SymbolTable members, IList<ISignature> callSignatures, IList<ISignature> constructSignatures, IList<IIndexInfo> indexInfos)
    {
        type.Members = members;
        type.Properties = [];
        // PORT-NOTE: the four list slots are stored by reference, exactly as the reference does; the generated slots are
        // `List<T>?` while the parameters are the `IList` face, so a list that is not already a `List<T>` is copied
        // once. Every caller in the port passes a `List<T>`.
        type.CallSignatures = callSignatures as List<ISignature> ?? [.. callSignatures];
        type.ConstructSignatures = constructSignatures as List<ISignature> ?? [.. constructSignatures];
        type.IndexInfos = indexInfos as List<IIndexInfo> ?? [.. indexInfos];
        // This can loop back to getPropertyOfType() which would crash if `callSignatures` & `constructSignatures` are not initialized.
        if (members != _emptySymbols) type.Properties = GetNamedMembers(members, type.Symbol);
        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:5660-5662 — <c>createAnonymousType</c>.</summary>
    internal ResolvedType CreateAnonymousType(ISymbol? symbol, SymbolTable members, IList<ISignature> callSignatures, IList<ISignature> constructSignatures, IList<IIndexInfo> indexInfos)
        => (ResolvedType)SetStructuredTypeMembers(CreateObjectType(ObjectFlags.Anonymous, symbol), members, callSignatures, constructSignatures, indexInfos);

    /// <summary>ported from: src/compiler/checker.ts:5664-5676 — <c>getResolvedTypeWithoutAbstractConstructSignatures</c>.</summary>
    internal ResolvedType GetResolvedTypeWithoutAbstractConstructSignatures(ResolvedType type)
    {
        if (Core.Length(type.ConstructSignatures) == 0) return type;
        if (type.ObjectTypeWithoutAbstractConstructSignatures is not null) return (ResolvedType)type.ObjectTypeWithoutAbstractConstructSignatures;
        var constructSignatures = Core.Filter(type.ConstructSignatures, signature => (signature.Flags & SignatureFlags.Abstract) == 0);
        if (ReferenceEquals(type.ConstructSignatures, constructSignatures)) return type;
        var typeCopy = CreateAnonymousType(
            type.Symbol,
            type.Members ?? _emptySymbols,
            type.CallSignatures ?? [],
            Core.Some(constructSignatures) ? constructSignatures! : [],
            type.IndexInfos ?? []);
        type.ObjectTypeWithoutAbstractConstructSignatures = typeCopy;
        typeCopy.ObjectTypeWithoutAbstractConstructSignatures = typeCopy;
        return typeCopy;
    }

    // ---------------------------------------------------------------------------------------------
    // The symbol ordering the member list of a class or interface uses under stableTypeOrdering
    // (checker.ts:53821-53860)
    // ---------------------------------------------------------------------------------------------

    // PORT-NOTE: <c>compareSymbols</c> and <c>compareNodes</c> live outside this slice's ranges (they belong to the
    // block around <c>initializeTypeChecker</c>), but <c>getNamedMembers</c>' stableTypeOrdering arm is their first
    // caller and the source of the member order the rest of the checker sees, so they are landed here rather than
    // referenced. They are <c>private</c>: M6-5's members of this same class can call them, and no other file needs
    // them. TS returns a JS sort number; the port returns the same ordering as an <c>int</c>, which is what
    // <see cref="List{T}.Sort(Comparison{T})"/> takes.

    /// <summary>ported from: src/compiler/checker.ts:53821-53833 — <c>compareSymbols</c>.</summary>
    private int CompareSymbols(ISymbol? s1, ISymbol? s2)
    {
        if (ReferenceEquals(s1, s2)) return 0;
        if (s1 is null) return 1;
        if (s2 is null) return -1;
        if (Core.Length(s1.Declarations) != 0 && Core.Length(s2.Declarations) != 0)
        {
            var r = CompareNodes((Node)s1.Declarations![0], (Node)s2.Declarations![0]);
            if (r != 0) return r;
        }
        else if (Core.Length(s1.Declarations) != 0)
        {
            return -1;
        }
        else if (Core.Length(s2.Declarations) != 0)
        {
            return 1;
        }

        var nameResult = (int)Core.CompareComparableValues(s1.EscapedName, s2.EscapedName);
        if (nameResult != 0) return nameResult;
        return Utilities.GetSymbolId(s1) - Utilities.GetSymbolId(s2);
    }

    /// <summary>ported from: src/compiler/checker.ts:53835-53847 — <c>compareNodes</c>.</summary>
    private int CompareNodes(Node? n1, Node? n2)
    {
        if (ReferenceEquals(n1, n2)) return 0;
        if (n1 is null) return 1;
        if (n2 is null) return -1;
        var s1 = Utilities.GetSourceFileOfNode(n1);
        var s2 = Utilities.GetSourceFileOfNode(n2);
        if (!ReferenceEquals(s1, s2))
        {
            var f1 = _fileIndexMap![s1!];
            var f2 = _fileIndexMap![s2!];
            // Order by index of file in the containing program
            return f1 - f2;
        }

        // In the same file, order by source position
        return n1.Pos - n2.Pos;
    }

    // ---------------------------------------------------------------------------------------------
    // Index infos and iteration types (checker.ts:16683-16685, 45842-45865)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:16683-16685 — <c>createIndexInfo</c>.</summary>
    internal IndexInfo CreateIndexInfo(IType keyType, IType type, bool isReadonly, IndexSignatureDeclaration? declaration = null, IList<Node>? components = null)
        => new()
        {
            KeyType = keyType,
            Type = type,
            IsReadonly = isReadonly,
            Declaration = declaration,
            Components = components is null ? null : [.. components],
        };

    /// <summary>ported from: src/compiler/checker.ts:45842-45865 — <c>createIterationTypes</c>.</summary>
    internal IterationTypes CreateIterationTypes(IType yieldType = null!, IType returnType = null!, IType nextType = null!)
    {
        // `yieldType` and `returnType` are defaulted to `neverType` they each will be combined
        // via `getUnionType` when merging iteration types. `nextType` is defined as `unknownType`
        // as it is combined via `getIntersectionType` when merging iteration types.
        yieldType ??= _neverType;
        returnType ??= _neverType;
        nextType ??= _unknownType;

        // Use the cache only for intrinsic types to keep it small as they are likely to be
        // more frequently created (i.e. `Iterator<number, void, unknown>`). Iteration types
        // are also cached on the type they are requested for, so we shouldn't need to maintain
        // the cache for less-frequently used types.
        if ((yieldType.Flags & TypeFlags.Intrinsic) != 0
            && (returnType.Flags & (TypeFlags.Any | TypeFlags.Never | TypeFlags.Unknown | TypeFlags.Void | TypeFlags.Undefined)) != 0
            && (nextType.Flags & (TypeFlags.Any | TypeFlags.Never | TypeFlags.Unknown | TypeFlags.Void | TypeFlags.Undefined)) != 0)
        {
            var id = GetTypeListId([yieldType, returnType, nextType]);
            var iterationTypes = _iterationTypesCache.GetValueOrDefault(id);
            if (iterationTypes is null)
            {
                iterationTypes = new IterationTypes(yieldType, returnType, nextType);
                _iterationTypesCache[id] = iterationTypes;
            }

            return iterationTypes;
        }

        return new IterationTypes(yieldType, returnType, nextType);
    }
}
