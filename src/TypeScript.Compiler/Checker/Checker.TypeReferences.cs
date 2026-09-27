using System.Globalization;

namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:16958-18063 — type references (`getTypeFromTypeReference` and its neighbours),
//              the substitution type (`getSubstitutionType`), the global-type getters and the array/tuple machinery.
/// <summary>
/// Type-reference resolution, the substitution type, the global-type getters and the array/tuple type constructors
/// (checker.ts:16958-18063).
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (state): the reference declares the caches these members read at checker.ts:2048-2070 and the
/// forty-three <c>deferredGlobal*</c> slots at :2275-2317. Those lines sit outside every M6 slice's range, so — by the
/// rule that the slice whose members read a cache declares it — <c>tupleTypes</c>, <c>substitutionTypes</c> and the
/// <c>deferredGlobal*</c> family are declared here (their readers are all in this file); <c>unresolvedSymbols</c>
/// (:2069) and <c>errorTypes</c> (:2070) are <em>not</em> — Checker.Types.cs declares those two, and this slice reads
/// them. The module-level <c>intrinsicTypeKinds</c> (:1443-1450) is declared here too:
/// <c>getTypeAliasInstantiation</c> is its only reader.
/// </para>
/// <para>
/// PORT-NOTE (array widening): TS's arrays are covariant, so <c>concatenate(type.target.outerTypeParameters, …)</c>
/// and <c>createTypeMapper(typeParameters, …)</c> hand a <c>TypeParameter[]</c> to a <c>Type[]</c> parameter without a
/// word. C#'s <see cref="IList{T}"/> is invariant, so each of those sites spells the widening
/// <c>[.. list.Cast&lt;IType&gt;()]</c>; the model's <c>TypeParameters</c> slots cannot be widened to
/// <c>List&lt;IType&gt;</c> instead, because every constraint query needs the <c>ITypeParameter</c> face.
/// </para>
/// <para>
/// PORT-NOTE (<c>emptyArray</c>): TS's shared empty array is not ported (see Utilities/Utilities.Parser.cs:552), so
/// every <c>x || emptyArray</c> below is <c>x ?? []</c> and the value is a fresh list. The identity of the empty case
/// is never observed by this slice.
/// </para>
/// <para>
/// PORT-NOTE (<c>NodeWithTypeArguments</c>/<c>TypeReferenceType</c>): TS's structural unions over the type-argument
/// carriers have no C# counterpart, so these parameters take <see cref="Node"/> and read the shared slot through a
/// cast to <see cref="INodeWithTypeArguments"/>. The mixin interface alone cannot be the parameter type:
/// <see cref="INode"/> carries only <c>Kind</c>/<c>Flags</c>/<c>TransformFlags</c>, not <c>parent</c>.
/// </para>
/// <para>
/// PORT-NOTE (<c>TupleTypeReference.target</c>): <c>types.ts:6719-6721</c> narrows that slot to <c>TupleType</c>, which
/// C# cannot do to an inherited member, so the port keeps the parameter as <see cref="ITupleTypeReference"/> and reads
/// the target with an explicit cast to <see cref="ITupleType"/> (the object really is one — the checker only ever
/// reaches these members through <c>isTupleType</c>).
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // The caches this file declares (checker.ts:2048, 2059, 1443-1450)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:2048 — <c>tupleTypes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: declared here although its declaration line is outside this slice's range — and outside every M6
    /// slice's range — because <c>getTupleTargetType</c>/<c>createTupleTargetType</c> (:17854-17930) are its only
    /// readers. TS's <c>Map&lt;string, GenericType&gt;</c> is keyed and iterated by the tuple-shape key only.
    /// </remarks>
    internal readonly Dictionary<string, IGenericType> _tupleTypes = [];

    /// <summary>ported from: src/compiler/checker.ts:2059 — <c>substitutionTypes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: declared here for the same reason as <see cref="_tupleTypes"/> — <c>getOrCreateSubstitutionType</c>
    /// is its only reader. TS's key is <c>`${baseTypeId}&gt;${constraintId}`</c>, a plain string map.
    /// </remarks>
    internal readonly Dictionary<string, ISubstitutionType> _substitutionTypes = [];

    /// <summary>ported from: src/compiler/checker.ts:1443-1450 — <c>intrinsicTypeKinds</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: a module-level <c>const</c> in the reference, so it is checked once rather than per checker; the port
    /// states it as a static field. <c>getTypeAliasInstantiation</c> (:17027) is its only reader.
    /// <see cref="CheckerEnums.cs"/> documents why this data is not part of the enum file.
    /// </remarks>
    internal static readonly Dictionary<string, IntrinsicTypeKind> _intrinsicTypeKinds = new()
    {
        ["Uppercase"] = IntrinsicTypeKind.Uppercase,
        ["Lowercase"] = IntrinsicTypeKind.Lowercase,
        ["Capitalize"] = IntrinsicTypeKind.Capitalize,
        ["Uncapitalize"] = IntrinsicTypeKind.Uncapitalize,
        ["NoInfer"] = IntrinsicTypeKind.NoInfer,
    };

    // ---------------------------------------------------------------------------------------------
    // The deferred global types (checker.ts:2275-2317)
    // ---------------------------------------------------------------------------------------------

    // PORT-NOTE: these forty-three slots are the lazily resolved global types and symbols. They are declared here, not
    // in Checker.cs, because every reader of every one of them is in this slice's range (checker.ts:17453-17709);
    // `deferredGlobalNonNullableTypeAlias` (:2270) is the one exception — its readers are in the relations slice, so
    // it is not declared here. TS declares three of them non-optional (`deferredGlobalTypedPropertyDescriptorType`,
    // `deferredGlobalImportMetaType`, `deferredGlobalImportMetaExpressionType`) because its definite-assignment
    // analysis can prove the `||=`/`if` fills them; the port keeps every slot nullable and asserts at the return.

    /// <summary>ported from: src/compiler/checker.ts:2275 — <c>deferredGlobalESSymbolConstructorSymbol</c>.</summary>
    internal ISymbol? _deferredGlobalESSymbolConstructorSymbol;

    /// <summary>ported from: src/compiler/checker.ts:2276 — <c>deferredGlobalESSymbolConstructorTypeSymbol</c>.</summary>
    internal ISymbol? _deferredGlobalESSymbolConstructorTypeSymbol;

    /// <summary>ported from: src/compiler/checker.ts:2277 — <c>deferredGlobalESSymbolType</c>.</summary>
    internal IObjectType? _deferredGlobalESSymbolType;

    /// <summary>ported from: src/compiler/checker.ts:2278 — <c>deferredGlobalTypedPropertyDescriptorType</c>.</summary>
    internal IGenericType? _deferredGlobalTypedPropertyDescriptorType;

    /// <summary>ported from: src/compiler/checker.ts:2279 — <c>deferredGlobalPromiseType</c>.</summary>
    internal IGenericType? _deferredGlobalPromiseType;

    /// <summary>ported from: src/compiler/checker.ts:2280 — <c>deferredGlobalPromiseLikeType</c>.</summary>
    internal IGenericType? _deferredGlobalPromiseLikeType;

    /// <summary>ported from: src/compiler/checker.ts:2281 — <c>deferredGlobalPromiseConstructorSymbol</c>.</summary>
    internal ISymbol? _deferredGlobalPromiseConstructorSymbol;

    /// <summary>ported from: src/compiler/checker.ts:2282 — <c>deferredGlobalPromiseConstructorLikeType</c>.</summary>
    internal IObjectType? _deferredGlobalPromiseConstructorLikeType;

    /// <summary>ported from: src/compiler/checker.ts:2283 — <c>deferredGlobalIterableType</c>.</summary>
    internal IGenericType? _deferredGlobalIterableType;

    /// <summary>ported from: src/compiler/checker.ts:2284 — <c>deferredGlobalIteratorType</c>.</summary>
    internal IGenericType? _deferredGlobalIteratorType;

    /// <summary>ported from: src/compiler/checker.ts:2285 — <c>deferredGlobalIterableIteratorType</c>.</summary>
    internal IGenericType? _deferredGlobalIterableIteratorType;

    /// <summary>ported from: src/compiler/checker.ts:2286 — <c>deferredGlobalIteratorObjectType</c>.</summary>
    internal IGenericType? _deferredGlobalIteratorObjectType;

    /// <summary>ported from: src/compiler/checker.ts:2287 — <c>deferredGlobalGeneratorType</c>.</summary>
    internal IGenericType? _deferredGlobalGeneratorType;

    /// <summary>ported from: src/compiler/checker.ts:2288 — <c>deferredGlobalIteratorYieldResultType</c>.</summary>
    internal IGenericType? _deferredGlobalIteratorYieldResultType;

    /// <summary>ported from: src/compiler/checker.ts:2289 — <c>deferredGlobalIteratorReturnResultType</c>.</summary>
    internal IGenericType? _deferredGlobalIteratorReturnResultType;

    /// <summary>ported from: src/compiler/checker.ts:2290 — <c>deferredGlobalAsyncIterableType</c>.</summary>
    internal IGenericType? _deferredGlobalAsyncIterableType;

    /// <summary>ported from: src/compiler/checker.ts:2291 — <c>deferredGlobalAsyncIteratorType</c>.</summary>
    internal IGenericType? _deferredGlobalAsyncIteratorType;

    /// <summary>ported from: src/compiler/checker.ts:2292 — <c>deferredGlobalAsyncIterableIteratorType</c>.</summary>
    internal IGenericType? _deferredGlobalAsyncIterableIteratorType;

    /// <summary>ported from: src/compiler/checker.ts:2293 — <c>deferredGlobalBuiltinIteratorTypes</c>.</summary>
    internal List<IGenericType>? _deferredGlobalBuiltinIteratorTypes;

    /// <summary>ported from: src/compiler/checker.ts:2294 — <c>deferredGlobalBuiltinAsyncIteratorTypes</c>.</summary>
    internal List<IGenericType>? _deferredGlobalBuiltinAsyncIteratorTypes;

    /// <summary>ported from: src/compiler/checker.ts:2295 — <c>deferredGlobalAsyncIteratorObjectType</c>.</summary>
    internal IGenericType? _deferredGlobalAsyncIteratorObjectType;

    /// <summary>ported from: src/compiler/checker.ts:2296 — <c>deferredGlobalAsyncGeneratorType</c>.</summary>
    internal IGenericType? _deferredGlobalAsyncGeneratorType;

    /// <summary>ported from: src/compiler/checker.ts:2297 — <c>deferredGlobalTemplateStringsArrayType</c>.</summary>
    internal IObjectType? _deferredGlobalTemplateStringsArrayType;

    /// <summary>ported from: src/compiler/checker.ts:2298 — <c>deferredGlobalImportMetaType</c>.</summary>
    internal IObjectType? _deferredGlobalImportMetaType;

    /// <summary>ported from: src/compiler/checker.ts:2299 — <c>deferredGlobalImportMetaExpressionType</c>.</summary>
    internal IObjectType? _deferredGlobalImportMetaExpressionType;

    /// <summary>ported from: src/compiler/checker.ts:2300 — <c>deferredGlobalImportCallOptionsType</c>.</summary>
    internal IObjectType? _deferredGlobalImportCallOptionsType;

    /// <summary>ported from: src/compiler/checker.ts:2301 — <c>deferredGlobalImportAttributesType</c>.</summary>
    internal IObjectType? _deferredGlobalImportAttributesType;

    /// <summary>ported from: src/compiler/checker.ts:2302 — <c>deferredGlobalDisposableType</c>.</summary>
    internal IObjectType? _deferredGlobalDisposableType;

    /// <summary>ported from: src/compiler/checker.ts:2303 — <c>deferredGlobalAsyncDisposableType</c>.</summary>
    internal IObjectType? _deferredGlobalAsyncDisposableType;

    /// <summary>ported from: src/compiler/checker.ts:2304 — <c>deferredGlobalExtractSymbol</c>.</summary>
    internal ISymbol? _deferredGlobalExtractSymbol;

    /// <summary>ported from: src/compiler/checker.ts:2305 — <c>deferredGlobalOmitSymbol</c>.</summary>
    internal ISymbol? _deferredGlobalOmitSymbol;

    /// <summary>ported from: src/compiler/checker.ts:2306 — <c>deferredGlobalAwaitedSymbol</c>.</summary>
    internal ISymbol? _deferredGlobalAwaitedSymbol;

    /// <summary>ported from: src/compiler/checker.ts:2307 — <c>deferredGlobalBigIntType</c>.</summary>
    internal IObjectType? _deferredGlobalBigIntType;

    /// <summary>ported from: src/compiler/checker.ts:2308 — <c>deferredGlobalNaNSymbol</c>.</summary>
    internal ISymbol? _deferredGlobalNaNSymbol;

    /// <summary>ported from: src/compiler/checker.ts:2309 — <c>deferredGlobalRecordSymbol</c>.</summary>
    internal ISymbol? _deferredGlobalRecordSymbol;

    /// <summary>ported from: src/compiler/checker.ts:2310 — <c>deferredGlobalClassDecoratorContextType</c>.</summary>
    internal IGenericType? _deferredGlobalClassDecoratorContextType;

    /// <summary>ported from: src/compiler/checker.ts:2311 — <c>deferredGlobalClassMethodDecoratorContextType</c>.</summary>
    internal IGenericType? _deferredGlobalClassMethodDecoratorContextType;

    /// <summary>ported from: src/compiler/checker.ts:2311 — <c>deferredGlobalClassGetterDecoratorContextType</c>.</summary>
    /// <remarks>PORT-NOTE: the reference declares it on :2312; the doc line above matches TS's own grouping.</remarks>
    internal IGenericType? _deferredGlobalClassGetterDecoratorContextType;

    /// <summary>ported from: src/compiler/checker.ts:2313 — <c>deferredGlobalClassSetterDecoratorContextType</c>.</summary>
    internal IGenericType? _deferredGlobalClassSetterDecoratorContextType;

    /// <summary>ported from: src/compiler/checker.ts:2314 — <c>deferredGlobalClassAccessorDecoratorContextType</c>.</summary>
    internal IGenericType? _deferredGlobalClassAccessorDecoratorContextType;

    /// <summary>ported from: src/compiler/checker.ts:2315 — <c>deferredGlobalClassAccessorDecoratorTargetType</c>.</summary>
    internal IGenericType? _deferredGlobalClassAccessorDecoratorTargetType;

    /// <summary>ported from: src/compiler/checker.ts:2316 — <c>deferredGlobalClassAccessorDecoratorResultType</c>.</summary>
    internal IGenericType? _deferredGlobalClassAccessorDecoratorResultType;

    /// <summary>ported from: src/compiler/checker.ts:2317 — <c>deferredGlobalClassFieldDecoratorContextType</c>.</summary>
    internal IGenericType? _deferredGlobalClassFieldDecoratorContextType;

    // ---------------------------------------------------------------------------------------------
    // Type reference resolution (checker.ts:16958-17302)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:16958-16981 — <c>getTypeArguments</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's branch result is <c>readonly Type[]</c> because <c>concatenate</c> is generic over two
    /// covariant arrays; the port widens the outer type parameters explicitly (see the class remarks). The resolving
    /// flag pairs with <c>popTypeResolution</c>, exactly as upstream, so the recursive re-entry reports the same
    /// circularity diagnostic.
    /// </remarks>
    internal List<IType> GetTypeArguments(ITypeReference type)
    {
        if (type.ResolvedTypeArguments is null)
        {
            if (!PushTypeResolution(type, TypeSystemPropertyName.ResolvedTypeArguments))
            {
                return Core.Concatenate<IType>(
                    type.Target!.OuterTypeParameters is null ? [] : [.. type.Target!.OuterTypeParameters.Cast<IType>()],
                    Core.Map(type.Target!.LocalTypeParameters, (_, _) => (IType)_errorType)) as List<IType> ?? [];
            }

            var node = type.Node;
            List<IType>? typeArguments = node is null ? [] :
                node.Kind == SyntaxKind.TypeReference
                    ? Core.Concatenate<IType>(
                        type.Target!.OuterTypeParameters is null ? [] : [.. type.Target!.OuterTypeParameters.Cast<IType>()],
                        GetEffectiveTypeArguments((TypeReferenceNode)node, type.Target!.LocalTypeParameters!)) as List<IType>
                    : node.Kind == SyntaxKind.ArrayType
                        ? [GetTypeFromTypeNode(((ArrayTypeNode)node).ElementType!)]
                        : Core.Map(((TupleTypeNode)node).Elements, (e, _) => GetTypeFromTypeNode((ITypeNode)e));
            if (PopTypeResolution())
            {
                type.ResolvedTypeArguments ??= type.Mapper is null ? typeArguments : InstantiateTypes(typeArguments!, (TypeMapper)type.Mapper);
            }
            else
            {
                type.ResolvedTypeArguments ??= Core.Concatenate<IType>(
                    type.Target!.OuterTypeParameters is null ? [] : [.. type.Target!.OuterTypeParameters.Cast<IType>()],
                    Core.Map(type.Target!.LocalTypeParameters, (_, _) => (IType)_errorType) ?? []) as List<IType> ?? [];
                // PORT-NOTE (A″): the argument is a DiagnosticArgument (Core/Diagnostic.cs) so the printer-gated stringifier runs on the first read of the message text.
                Error(
                    type.Node ?? _currentNode,
                    type.Target!.Symbol is not null
                        ? Diagnostics.Type_arguments_for_0_circularly_reference_themselves
                        : Diagnostics.Tuple_type_arguments_circularly_reference_themselves,
                    type.Target!.Symbol is not null ? Diagnostic.DeferArg(() => SymbolToString(type.Target!.Symbol)) : null);
            }
        }
        return type.ResolvedTypeArguments!;
    }

    /// <summary>ported from: src/compiler/checker.ts:16983-16985 — <c>getTypeReferenceArity</c>.</summary>
    internal int GetTypeReferenceArity(ITypeReference type) => Core.Length(type.Target!.TypeParameters);

    /// <summary>
    /// ported from: src/compiler/checker.ts:16990-17025 — <c>getTypeFromClassOrInterfaceReference</c>.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>node as TypeReferenceNode</c> in the deferred arm is a cast the kind test just proved; the
    /// port keeps the same <c>as</c>/cast pair. <c>typeToString</c> is the port's <c>TypeToString</c> checker member
    /// (M6-6's printer-gated stringifier, §15.15 ruling 1).
    /// </remarks>
    internal IType GetTypeFromClassOrInterfaceReference(Node node, ISymbol symbol)
    {
        var type = (IInterfaceType)GetDeclaredTypeOfSymbol(GetMergedSymbol(symbol));
        var typeParameters = type.LocalTypeParameters;
        if (typeParameters is not null)
        {
            var numTypeArguments = Core.Length(((INodeWithTypeArguments)node).TypeArguments);
            var minTypeArgumentCount = GetMinTypeArgumentCount(typeParameters);
            var isJs = Utilities.IsInJSFile(node);
            var isJsImplicitAny = !_noImplicitAny && isJs;
            if (!isJsImplicitAny && (numTypeArguments < minTypeArgumentCount || numTypeArguments > typeParameters.Count))
            {
                var missingAugmentsTag = isJs && NodeTests.IsExpressionWithTypeArguments(node) && !NodeTests.IsJSDocAugmentsTag(Debug.CheckDefined(node.Parent));
                var diag = minTypeArgumentCount == typeParameters.Count
                    ? missingAugmentsTag
                        ? Diagnostics.Expected_0_type_arguments_provide_these_with_an_extends_tag
                        : Diagnostics.Generic_type_0_requires_1_type_argument_s
                    : missingAugmentsTag
                        ? Diagnostics.Expected_0_1_type_arguments_provide_these_with_an_extends_tag
                        : Diagnostics.Generic_type_0_requires_between_1_and_2_type_arguments;

                var typeStr = Diagnostic.DeferArg(() => TypeToString(type, /*enclosingDeclaration*/ null, TypeFormatFlags.WriteArrayAsGenericType));
                Error(node, diag, typeStr, minTypeArgumentCount, typeParameters.Count);
                if (!isJs)
                {
                    // TODO: Adopt same permissive behavior in TS as in JS to reduce follow-on editing experience failures (requires editing fillMissingTypeArguments)
                    return _errorType;
                }
            }
            if (node.Kind == SyntaxKind.TypeReference && IsDeferredTypeReferenceNode(node, Core.Length(((INodeWithTypeArguments)node).TypeArguments) != typeParameters.Count))
            {
                return CreateDeferredTypeReference((IGenericType)type, (TypeReferenceNode)node, /*mapper*/ null);
            }
            // In a type reference, the outer type parameters of the referenced class or interface are automatically
            // supplied as type arguments and the type reference only specifies arguments for the local type parameters
            // of the class or interface.
            var typeArguments = Core.Concatenate<IType>(
                type.OuterTypeParameters is null ? [] : [.. type.OuterTypeParameters.Cast<IType>()],
                FillMissingTypeArguments(TypeArgumentsFromTypeReferenceNode(node), typeParameters, minTypeArgumentCount, isJs)) as List<IType>;
            return CreateTypeReference((IGenericType)type, typeArguments);
        }
        return CheckNoTypeArguments(node, symbol) ? type : _errorType;
    }

    /// <summary>ported from: src/compiler/checker.ts:17027-17048 — <c>getTypeAliasInstantiation</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the <c>intrinsicTypeKinds.get(...)</c> lookup must distinguish "absent" from "Uppercase", whose
    /// value is 0 — hence <c>TryGetValue</c> rather than <c>GetValueOrDefault</c>. <c>instantiateTypeWithAlias</c> and
    /// <c>createTypeMapper</c> are M6Generics' (Checker.Instantiation.cs).
    /// </remarks>
    internal IType GetTypeAliasInstantiation(ISymbol symbol, List<IType>? typeArguments, ISymbol? aliasSymbol = null, List<IType>? aliasTypeArguments = null)
    {
        var type = GetDeclaredTypeOfSymbol(symbol);
        if (ReferenceEquals(type, _intrinsicMarkerType))
        {
            var typeKind = symbol.EscapedName is not null && _intrinsicTypeKinds.TryGetValue(symbol.EscapedName, out var kind)
                ? kind
                : (IntrinsicTypeKind?)null;
            if (typeKind is not null && typeArguments is not null && typeArguments.Count == 1)
            {
                return typeKind == IntrinsicTypeKind.NoInfer ? GetNoInferType(typeArguments[0]) : GetStringMappingType(symbol, typeArguments[0]);
            }
        }
        var links = GetSymbolLinks(symbol);
        // PORT-NOTE (correctness, measured): the reference relies on `links.instantiations` and `links.typeParameters`
        // being initialized by `getDeclaredTypeOfTypeAlias` before anything can ask for an instantiation. A symbol that
        // a *JavaScript* class/assignment merge turned into a transient carrying both declaration kinds can get its
        // `declaredType` stamped by `getDeclaredTypeOfClassOrInterface` first (that arm assigns to the original's links
        // *and* to the merged clone's), after which the alias arm's `if (!links.declaredType)` guard skips the
        // initialization for good — and the variance machinery's `createMarkerType` then dereferences the null
        // dictionary (`types/conditional/inferTypes1.ts` in the whole-corpus run, docs §18.4f). Both slots are
        // initialized here the way the alias arm would have, so the state the reference relies on holds whatever
        // ordered the two arms.
        if (links.TypeParameters is null)
        {
            links.TypeParameters = GetLocalTypeParametersOfClassOrInterfaceOrTypeAlias(symbol);
        }

        links.Instantiations ??= new Dictionary<string, IType>(StringComparer.Ordinal);
        var typeParameters = links.TypeParameters!;
        var id = GetTypeListId(typeArguments) + GetAliasId(aliasSymbol, aliasTypeArguments);
        var instantiation = links.Instantiations.GetValueOrDefault(id);
        if (instantiation is null)
        {
            instantiation = InstantiateTypeWithAlias(
                type,
                CreateTypeMapper(typeParameters, FillMissingTypeArguments(typeArguments, typeParameters, GetMinTypeArgumentCount(typeParameters), Utilities.IsInJSFile((Node?)symbol.ValueDeclaration))),
                aliasSymbol,
                aliasTypeArguments);
            links.Instantiations![id] = instantiation;
        }
        return instantiation;
    }

    /// <summary>ported from: src/compiler/checker.ts:17050-17105 — <c>getTypeFromTypeAliasReference</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS shadows the outer <c>aliasSymbol</c> with a second declaration inside the
    /// <c>isTypeReferenceType</c> arm; C# forbids that shadowing, so the inner one is <c>aliasSymbolFromName</c>.
    /// The error type for an unresolved alias is cached per alias id in <see cref="_errorTypes"/> (declared above).
    /// </remarks>
    internal IType GetTypeFromTypeAliasReference(Node node, ISymbol symbol)
    {
        if ((GetCheckFlags(symbol) & CheckFlags.Unresolved) != 0)
        {
            var typeArguments = TypeArgumentsFromTypeReferenceNode(node);
            var id = GetAliasId(symbol, typeArguments);
            var errorType = _errorTypes.GetValueOrDefault(id);
            if (errorType is null)
            {
                errorType = CreateIntrinsicType(TypeFlags.Any, "error", /*objectFlags*/ ObjectFlags.None, $"alias {id}");
                errorType.AliasSymbol = symbol;
                errorType.AliasTypeArguments = typeArguments;
                _errorTypes[id] = errorType;
            }
            return errorType;
        }
        var type = GetDeclaredTypeOfSymbol(symbol);
        var typeParameters = GetSymbolLinks(symbol).TypeParameters;
        if (typeParameters is not null)
        {
            var numTypeArguments = Core.Length(((INodeWithTypeArguments)node).TypeArguments);
            var minTypeArgumentCount = GetMinTypeArgumentCount(typeParameters);
            if (numTypeArguments < minTypeArgumentCount || numTypeArguments > typeParameters.Count)
            {
                Error(
                    node,
                    minTypeArgumentCount == typeParameters.Count
                        ? Diagnostics.Generic_type_0_requires_1_type_argument_s
                        : Diagnostics.Generic_type_0_requires_between_1_and_2_type_arguments,
                    Diagnostic.DeferArg(() => SymbolToString(symbol)),
                    minTypeArgumentCount,
                    typeParameters.Count);
                return _errorType;
            }
            // We refrain from associating a local type alias with an instantiation of a top-level type alias
            // because the local alias may end up being referenced in an inferred return type where it is not
            // accessible--which in turn may lead to a large structural expansion of the type when generating
            // a .d.ts file. See #43622 for an example.
            var aliasSymbol = GetAliasSymbolForTypeNode(node);
            var newAliasSymbol = aliasSymbol is not null && (IsLocalTypeAlias(symbol) || !IsLocalTypeAlias(aliasSymbol)) ? aliasSymbol : null;
            List<IType>? aliasTypeArguments = null;
            if (newAliasSymbol is not null)
            {
                // PORT-NOTE: `getTypeArgumentsForAliasSymbol` answers `TypeParameter[] | undefined` and TS lets array
                // covariance widen it to the `Type[]` the alias slot wants; C# spells the widening (see the class note).
                var aliasTypeParameters = GetTypeArgumentsForAliasSymbol(newAliasSymbol);
                aliasTypeArguments = aliasTypeParameters is null ? null : [.. aliasTypeParameters.Cast<IType>()];
            }
            else if (Utilities.IsTypeReferenceType(node))
            {
                var aliasSymbolFromName = ResolveTypeReferenceName(node, SymbolFlags.Alias, /*ignoreErrors*/ true);
                // refers to an alias import/export/reexport - by making sure we use the target as an aliasSymbol,
                // we ensure the exported symbol is used to refer to the type when it's reserialized later
                if (!ReferenceEquals(aliasSymbolFromName, _unknownSymbol))
                {
                    var resolved = ResolveAlias(aliasSymbolFromName);
                    if (resolved is not null && (resolved.Flags & SymbolFlags.TypeAlias) != 0)
                    {
                        newAliasSymbol = resolved;
                        aliasTypeArguments = TypeArgumentsFromTypeReferenceNode(node) ?? (typeParameters is not null ? [] : null);
                    }
                }
            }
            return GetTypeAliasInstantiation(symbol, TypeArgumentsFromTypeReferenceNode(node), newAliasSymbol, aliasTypeArguments);
        }
        return CheckNoTypeArguments(node, symbol) ? type : _errorType;
    }

    /// <summary>ported from: src/compiler/checker.ts:17107-17110 — <c>isLocalTypeAlias</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>symbol.declarations?.find(isTypeAlias)</c> passes a <c>Declaration</c> to a lib predicate that
    /// takes <c>Node</c>; the port spells the downcast the checker uses everywhere (<c>(Node)declaration</c>), and
    /// keeps TS's convention of testing a named function through a lambda because C# method groups do not drop
    /// parameters.
    /// </remarks>
    internal bool IsLocalTypeAlias(ISymbol symbol)
    {
        var declaration = Core.Find(symbol.Declarations, (d, _) => Utilities.IsTypeAlias((Node)d));
        return declaration is not null && Utilities.GetContainingFunction((Node)declaration) is not null;
    }

    /// <summary>ported from: src/compiler/checker.ts:17112-17127 — <c>getTypeReferenceName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers the union <c>EntityNameOrEntityNameExpression</c> (a qualified name or a property
    /// access); both arms are <see cref="Node"/> in the port, so the answer stays <see cref="Node"/>?. The
    /// <c>// fall through</c> comment of the reference is kept: an <c>ExpressionWithTypeArguments</c> whose
    /// expression is not a simple entity name falls out of the switch exactly as upstream.
    /// </remarks>
    internal Node? GetTypeReferenceName(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.TypeReference:
                return ((TypeReferenceNode)node).TypeName;
            case SyntaxKind.ExpressionWithTypeArguments:
                // We only support expressions that are simple qualified names. For other
                // expressions this produces undefined.
                var expr = ((ExpressionWithTypeArguments)node).Expression!;
                if (Utilities.IsEntityNameExpression(expr))
                {
                    return expr;
                }
                // fall through;
                break;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:17129-17131 — <c>getSymbolPath</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>symbol.escapedName as string</c> is the port's non-null assertion — a symbol always has a name
    /// (Model/Symbol.cs states the same invariant on <c>EscapedName</c>).
    /// </remarks>
    internal string GetSymbolPath(ISymbol symbol)
        => symbol.Parent is not null ? $"{GetSymbolPath(symbol.Parent)}.{symbol.EscapedName}" : symbol.EscapedName!;

    /// <summary>ported from: src/compiler/checker.ts:17133-17152 — <c>getUnresolvedSymbolForEntityName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>result.links.declaredType</c> reads the links of a symbol <c>createSymbol</c> has just built, so
    /// the port routes it through <c>getSymbolLinks</c> (Checker.cs:2932), which is the same object. TS types the map's
    /// values <c>TransientSymbol</c>, which Checker.Types.cs already declares as <c>ITransientSymbol</c>; the local
    /// keeps that face and widens only at the return.
    /// </remarks>
    internal ISymbol GetUnresolvedSymbolForEntityName(Node name)
    {
        var identifier = name.Kind == SyntaxKind.QualifiedName ? (Node)((QualifiedName)name).Right! :
            name.Kind == SyntaxKind.PropertyAccessExpression ? ((PropertyAccessExpression)name).Name! :
            name;
        var text = ((Identifier)identifier).EscapedText;
        if (Js.IsTruthy(text))
        {
            var parentSymbol = name.Kind == SyntaxKind.QualifiedName ? GetUnresolvedSymbolForEntityName(((QualifiedName)name).Left!) :
                name.Kind == SyntaxKind.PropertyAccessExpression ? GetUnresolvedSymbolForEntityName(((PropertyAccessExpression)name).Expression!) :
                null;
            var path = parentSymbol is not null ? $"{GetSymbolPath(parentSymbol)}.{text}" : text!;
            ITransientSymbol? result = _unresolvedSymbols.GetValueOrDefault(path);
            if (result is null)
            {
                result = CreateSymbol(SymbolFlags.TypeAlias, text!, CheckFlags.Unresolved);
                _unresolvedSymbols[path] = result;
                result.Parent = parentSymbol;
                GetSymbolLinks(result).DeclaredType = _unresolvedType;
            }
            return result;
        }
        return _unknownSymbol;
    }

    /// <summary>ported from: src/compiler/checker.ts:17154-17162 — <c>resolveTypeReferenceName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's answer is never undefined (every arm falls back to <c>unknownSymbol</c>), so the return type is
    /// non-nullable. <c>resolveEntityName</c> (checker.ts:4533) takes the same entity-name union the port holds as
    /// <see cref="Node"/> and stays the only member that can answer undefined.
    /// </remarks>
    internal ISymbol ResolveTypeReferenceName(Node typeReference, SymbolFlags meaning, bool? ignoreErrors = null)
    {
        var name = GetTypeReferenceName(typeReference);
        if (name is null)
        {
            return _unknownSymbol;
        }
        var symbol = ResolveEntityName(name, meaning, ignoreErrors);
        return symbol is not null && !ReferenceEquals(symbol, _unknownSymbol)
            ? symbol
            : Js.IsTruthy(ignoreErrors) ? _unknownSymbol : GetUnresolvedSymbolForEntityName(name);
    }

    /// <summary>ported from: src/compiler/checker.ts:17164-17196 — <c>getTypeReferenceType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>resolveTypeReferenceName(node, SymbolFlags.Type)</c> is called for its diagnostic side effect
    /// only; the port keeps the bare call so the same error is reported once.
    /// </remarks>
    internal IType GetTypeReferenceType(Node node, ISymbol symbol)
    {
        if (ReferenceEquals(symbol, _unknownSymbol))
        {
            return _errorType;
        }
        symbol = GetExpandoSymbol(symbol) ?? symbol;
        if ((symbol.Flags & (SymbolFlags.Class | SymbolFlags.Interface)) != 0)
        {
            return GetTypeFromClassOrInterfaceReference(node, symbol);
        }
        if ((symbol.Flags & SymbolFlags.TypeAlias) != 0)
        {
            return GetTypeFromTypeAliasReference(node, symbol);
        }
        // Get type from reference to named type that cannot be generic (enum or type parameter)
        var res = TryGetDeclaredTypeOfSymbol(symbol);
        if (res is not null)
        {
            return CheckNoTypeArguments(node, symbol) ? GetRegularTypeOfLiteralType(res) : _errorType;
        }
        if ((symbol.Flags & SymbolFlags.Value) != 0 && IsJSDocTypeReference(node))
        {
            var jsdocType = GetTypeFromJSDocValueReference(node, symbol);
            if (jsdocType is not null)
            {
                return jsdocType;
            }
            else
            {
                // Resolve the type reference as a Type for the purpose of reporting errors.
                ResolveTypeReferenceName(node, SymbolFlags.Type);
                return GetTypeOfSymbol(symbol);
            }
        }
        return _errorType;
    }

    /// <summary>ported from: src/compiler/checker.ts:17198-17213 — <c>getTypeFromJSDocValueReference</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's doc comment (a JSDoc TypeReference may name a value, but is resolved as a type
    /// anyway — <c>import('./b').ConstructorFunction</c>) sits above the declaration upstream and is kept here. The
    /// <c>node.kind === SyntaxKind.ImportType</c> arm reads <c>qualifier</c> through the concrete class.
    /// </remarks>
    internal IType? GetTypeFromJSDocValueReference(Node node, ISymbol symbol)
    {
        var links = GetNodeLinks(node);
        if (links.ResolvedJSDocType is null)
        {
            var valueType = GetTypeOfSymbol(symbol);
            var typeType = valueType;
            if (symbol.ValueDeclaration is not null)
            {
                var isImportTypeWithQualifier = node.Kind == SyntaxKind.ImportType && ((ImportTypeNode)node).Qualifier is not null;
                // valueType might not have a symbol, eg, {import('./b').STRING_LITERAL}
                if (valueType.Symbol is not null && !ReferenceEquals(valueType.Symbol, symbol) && isImportTypeWithQualifier)
                {
                    typeType = GetTypeReferenceType(node, valueType.Symbol);
                }
            }
            links.ResolvedJSDocType = typeType;
        }
        return links.ResolvedJSDocType;
    }

    /// <summary>ported from: src/compiler/checker.ts:17215-17217 — <c>getNoInferType</c>.</summary>
    internal IType GetNoInferType(IType type)
        => IsNoInferTargetType(type) ? GetOrCreateSubstitutionType(type, _unknownType) : type;

    /// <summary>ported from: src/compiler/checker.ts:17219-17227 — <c>isNoInferTargetType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>some((type as UnionOrIntersectionType).types, …)</c> becomes <c>Core.Some</c>; the reference's
    /// comment explaining why the walk is deliberately shallow is kept.
    /// </remarks>
    internal bool IsNoInferTargetType(IType type)
        // This is effectively a more conservative and predictable form of couldContainTypeVariables. We want to
        // preserve NoInfer<T> only for types that could contain type variables, but we don't want to exhaustively
        // examine all object type members.
        => (type.Flags & TypeFlags.UnionOrIntersection) != 0 && Core.Some(((IUnionOrIntersectionType)type).Types, t => IsNoInferTargetType(t))
            || (type.Flags & TypeFlags.Substitution) != 0 && !IsNoInferType(type) && IsNoInferTargetType(((ISubstitutionType)type).BaseType!)
            || (type.Flags & TypeFlags.Object) != 0 && !IsEmptyAnonymousObjectType(type)
            || (type.Flags & (TypeFlags.Instantiable & ~TypeFlags.Substitution)) != 0 && !IsPatternLiteralType(type);

    /// <summary>ported from: src/compiler/checker.ts:17229-17232 — <c>isNoInferType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's reference comment (a NoInfer&lt;T&gt; is a substitution type with a TypeFlags.Unknown constraint)
    /// is preserved.
    /// </remarks>
    internal bool IsNoInferType(IType type)
        // A NoInfer<T> type is represented as a substitution type with a TypeFlags.Unknown constraint.
        => (type.Flags & TypeFlags.Substitution) != 0 && (((ISubstitutionType)type).Constraint!.Flags & TypeFlags.Unknown) != 0;

    /// <summary>ported from: src/compiler/checker.ts:17234-17238 — <c>getSubstitutionType</c>.</summary>
    internal IType GetSubstitutionType(IType baseType, IType constraint)
        => (constraint.Flags & TypeFlags.AnyOrUnknown) != 0 || ReferenceEquals(constraint, baseType) || (baseType.Flags & TypeFlags.Any) != 0
            ? baseType
            : GetOrCreateSubstitutionType(baseType, constraint);

    /// <summary>ported from: src/compiler/checker.ts:17240-17251 — <c>getOrCreateSubstitutionType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's key is the template string <c>`${getTypeId(baseType)}&gt;${getTypeId(constraint)}`</c>;
    /// the port keeps the interpolation but spells the number formatting invariant (<c>getTypeId</c> is declared by the
    /// 18064-18857 slice).
    /// </remarks>
    internal ISubstitutionType GetOrCreateSubstitutionType(IType baseType, IType constraint)
    {
        var id = $"{GetTypeId(baseType).ToString(CultureInfo.InvariantCulture)}>{GetTypeId(constraint).ToString(CultureInfo.InvariantCulture)}";
        var cached = _substitutionTypes.GetValueOrDefault(id);
        if (cached is not null)
        {
            return cached;
        }
        var result = (ISubstitutionType)CreateType(TypeFlags.Substitution);
        result.BaseType = baseType;
        result.Constraint = constraint;
        _substitutionTypes[id] = result;
        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:17253-17255 — <c>getSubstitutionIntersection</c>.</summary>
    internal IType GetSubstitutionIntersection(ISubstitutionType substitutionType)
        => IsNoInferType(substitutionType)
            ? substitutionType.BaseType!
            : GetIntersectionType([(IType)substitutionType.Constraint!, substitutionType.BaseType!]);

    /// <summary>ported from: src/compiler/checker.ts:17257-17259 — <c>isUnaryTupleTypeNode</c>.</summary>
    internal bool IsUnaryTupleTypeNode(ITypeNode node)
        => node.Kind == SyntaxKind.TupleType && ((TupleTypeNode)node).Elements!.Count == 1;

    /// <summary>ported from: src/compiler/checker.ts:17261-17265 — <c>getImpliedConstraint</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the recursion reads <c>elements[0]</c> of both nodes, which <c>isUnaryTupleTypeNode</c> just proved
    /// exists; the port keeps the unchecked indexer as upstream does.
    /// </remarks>
    internal IType? GetImpliedConstraint(IType type, ITypeNode checkNode, ITypeNode extendsNode)
        => IsUnaryTupleTypeNode(checkNode) && IsUnaryTupleTypeNode(extendsNode)
            ? GetImpliedConstraint(type, (ITypeNode)((TupleTypeNode)checkNode).Elements![0], (ITypeNode)((TupleTypeNode)extendsNode).Elements![0])
            : ReferenceEquals(GetActualTypeVariable(GetTypeFromTypeNode(checkNode)), GetActualTypeVariable(type))
                ? GetTypeFromTypeNode(extendsNode)
                : null;

    /// <summary>ported from: src/compiler/checker.ts:17267-17302 — <c>getConditionalFlowTypeOfType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the loop's <c>node &amp;&amp; !isStatement(node) &amp;&amp; node.kind !== SyntaxKind.JSDoc</c> is JS
    /// truthiness on a node, so it becomes a null test plus the two reference tests. <c>covariant = !covariant</c>,
    /// <c>append</c> → <c>Core.Append</c>, <c>everyType</c> (checker.ts:28518) stays a checker member. The reference's
    /// two explanatory comments (variance flipping and the homomorphic-mapped-type constraint) are kept in place.
    /// </remarks>
    internal IType GetConditionalFlowTypeOfType(IType type, Node node)
    {
        List<IType>? constraints = null;
        var covariant = true;
        while (node is not null && !Utilities.IsStatement(node) && node.Kind != SyntaxKind.JSDoc)
        {
            var parent = Debug.CheckDefined(node.Parent);
            // only consider variance flipped by parameter locations - `keyof` types would usually be considered variance inverting, but
            // often get used in indexed accesses where they behave sortof invariantly, but our checking is lax
            if (parent.Kind == SyntaxKind.Parameter)
            {
                covariant = !covariant;
            }
            // Always substitute on type parameters, regardless of variance, since even
            // in contravariant positions, they may rely on substituted constraints to be valid
            if ((covariant || (type.Flags & TypeFlags.TypeVariable) != 0) && parent.Kind == SyntaxKind.ConditionalType && ReferenceEquals(node, ((ConditionalTypeNode)parent).TrueType))
            {
                var constraint = GetImpliedConstraint(type, ((ConditionalTypeNode)parent).CheckType!, ((ConditionalTypeNode)parent).ExtendsType!);
                if (constraint is not null)
                {
                    constraints = Core.Append(constraints, constraint) as List<IType>;
                }
            }
            // Given a homomorphic mapped type { [K in keyof T]: XXX }, where T is constrained to an array or tuple type, in the
            // template type XXX, K has an added constraint of number | `${number}`.
            else if ((type.Flags & TypeFlags.TypeParameter) != 0 && parent.Kind == SyntaxKind.MappedType && ((MappedTypeNode)parent).NameType is null && ReferenceEquals(node, ((MappedTypeNode)parent).Type))
            {
                var mappedType = (IMappedType)GetTypeFromTypeNode((ITypeNode)parent);
                if (ReferenceEquals(GetTypeParameterFromMappedType(mappedType), GetActualTypeVariable(type)))
                {
                    var typeParameter = GetHomomorphicTypeVariable(mappedType);
                    if (typeParameter is not null)
                    {
                        var constraint = GetConstraintOfTypeParameter(typeParameter);
                        if (constraint is not null && EveryType(constraint, t => IsArrayOrTupleType(t)))
                        {
                            constraints = Core.Append(constraints, (IType)GetUnionType([(IType)_numberType, _numericStringType])) as List<IType>;
                        }
                    }
                }
            }
            node = parent;
        }
        return constraints is not null ? GetSubstitutionType(type, GetIntersectionType(constraints)) : type;
    }

    // ---------------------------------------------------------------------------------------------
    // Type nodes to types (checker.ts:17304-17451)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:17304-17306 — <c>isJSDocTypeReference</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's type predicate narrows its argument to <c>TypeReferenceNode</c>; C# has no type predicates, so
    /// the callers that rely on the narrowing cast at the use site (see <c>getTypeFromTypeReference</c>).
    /// </remarks>
    internal bool IsJSDocTypeReference(Node node)
        => (node.Flags & NodeFlags.JSDoc) != 0 && (node.Kind == SyntaxKind.TypeReference || node.Kind == SyntaxKind.ImportType);

    /// <summary>ported from: src/compiler/checker.ts:17308-17314 — <c>checkNoTypeArguments</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>node as TypeReferenceNode</c> is applied twice upstream — once for the test and once for the
    /// argument; the port names the cast once. The <c>anon</c> fallback is the module-level constant of
    /// checker.ts:1155, referenced here as <c>_anon</c> — declared once on the checker itself
    /// (<c>Checker/Checker.cs:173</c>), so this file must not declare a second copy.
    /// </remarks>
    internal bool CheckNoTypeArguments(Node node, ISymbol? symbol = null)
    {
        if (((INodeWithTypeArguments)node).TypeArguments is not null)
        {
            var typeName = node is TypeReferenceNode reference ? reference.TypeName : null;
            Error(
                node,
                Diagnostics.Type_0_is_not_generic,
                symbol is not null ? Diagnostic.DeferArg(() => SymbolToString(symbol)) : typeName is not null ? Utilities.DeclarationNameToString(typeName) : _anon);
            return false;
        }
        return true;
    }

    /// <summary>
    /// ported from: src/compiler/checker.ts:17316-17363 — <c>getIntendedTypeFromJSDocTypeReference</c>.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS switches on <c>node.typeName.escapedText</c> after <c>isIdentifier</c> has narrowed the name; the
    /// port uses the equivalent <c>is Identifier</c> pattern, which is also what makes the <c>escapedText</c> read
    /// well-typed. The <c>Object</c> arm's two-element form builds the anonymous index-signature type the JSDoc
    /// <c>Object.&lt;K,V&gt;</c> spelling means.
    /// </remarks>
    internal IType? GetIntendedTypeFromJSDocTypeReference(TypeReferenceNode node)
    {
        if (node.TypeName is Identifier typeName)
        {
            var typeArgs = node.TypeArguments;
            switch (typeName.EscapedText)
            {
                case "String":
                    CheckNoTypeArguments(node);
                    return _stringType;
                case "Number":
                    CheckNoTypeArguments(node);
                    return _numberType;
                case "BigInt":
                    CheckNoTypeArguments(node);
                    return _bigintType;
                case "Boolean":
                    CheckNoTypeArguments(node);
                    return _booleanType;
                case "Void":
                    CheckNoTypeArguments(node);
                    return _voidType;
                case "Undefined":
                    CheckNoTypeArguments(node);
                    return _undefinedType;
                case "Null":
                    CheckNoTypeArguments(node);
                    return _nullType;
                case "Function":
                case "function":
                    CheckNoTypeArguments(node);
                    return _globalFunctionType;
                case "array":
                    return (typeArgs is null || typeArgs.Count == 0) && !_noImplicitAny ? _anyArrayType : null;
                case "promise":
                    return (typeArgs is null || typeArgs.Count == 0) && !_noImplicitAny ? CreatePromiseType(_anyType) : null;
                case "Object":
                    if (typeArgs is not null && typeArgs.Count == 2)
                    {
                        if (Utilities.IsJSDocIndexSignature(node))
                        {
                            var indexed = GetTypeFromTypeNode(typeArgs[0]);
                            var target = GetTypeFromTypeNode(typeArgs[1]);
                            List<IIndexInfo> indexInfo = ReferenceEquals(indexed, _stringType) || ReferenceEquals(indexed, _numberType)
                                ? [CreateIndexInfo(indexed, target, /*isReadonly*/ false)]
                                : [];
                            return CreateAnonymousType(/*symbol*/ null, _emptySymbols, [], [], indexInfo);
                        }
                        return _anyType;
                    }
                    CheckNoTypeArguments(node);
                    return !_noImplicitAny ? _anyType : null;
            }
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:17365-17368 — <c>getTypeFromJSDocNullableTypeNode</c>.</summary>
    internal IType GetTypeFromJSDocNullableTypeNode(JSDocNullableType node)
    {
        var type = GetTypeFromTypeNode(node.Type!);
        return _strictNullChecks ? GetNullableType(type, TypeFlags.Null) : type;
    }

    /// <summary>ported from: src/compiler/checker.ts:17370-17404 — <c>getTypeFromTypeReference</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the <c>const</c> assertion arm relies on TS's <c>isAssertionExpression</c> predicate narrowing
    /// <c>node.parent</c> to <c>AssertionExpression</c>; the port tests the base class and then selects the one
    /// <c>expression</c> slot its two arms (<c>TypeAssertion</c>, <c>AsExpression</c>) each declare, because the union
    /// has no common base in the model. All three resolution passes and both cache writes are kept in the
    /// reference's order.
    /// </remarks>
    internal IType GetTypeFromTypeReference(Node node)
    {
        var links = GetNodeLinks(node);
        if (links.ResolvedType is null)
        {
            // handle LS queries on the `const` in `x as const` by resolving to the type of `x`
            if (Utilities.IsConstTypeReference(node) && node.Parent is Expression parentExpression && Utilities.IsAssertionExpression(parentExpression))
            {
                links.ResolvedSymbol = _unknownSymbol;
                var asserted = parentExpression is TypeAssertion typeAssertion ? typeAssertion.Expression : ((AsExpression)parentExpression).Expression;
                return links.ResolvedType = CheckExpressionCached(asserted!);
            }
            ISymbol? symbol = null;
            IType? type = null;
            var meaning = SymbolFlags.Type;
            if (IsJSDocTypeReference(node))
            {
                type = GetIntendedTypeFromJSDocTypeReference((TypeReferenceNode)node);
                if (type is null)
                {
                    symbol = ResolveTypeReferenceName(node, meaning, /*ignoreErrors*/ true);
                    if (ReferenceEquals(symbol, _unknownSymbol))
                    {
                        symbol = ResolveTypeReferenceName(node, meaning | SymbolFlags.Value);
                    }
                    else
                    {
                        ResolveTypeReferenceName(node, meaning); // Resolve again to mark errors, if any
                    }
                    type = GetTypeReferenceType(node, symbol);
                }
            }
            if (type is null)
            {
                symbol = ResolveTypeReferenceName(node, meaning);
                type = GetTypeReferenceType(node, symbol);
            }
            // Cache both the resolved symbol and the resolved type. The resolved symbol is needed when we check the
            // type reference in checkTypeReferenceNode.
            links.ResolvedSymbol = symbol;
            links.ResolvedType = type;
        }
        return links.ResolvedType;
    }

    /// <summary>ported from: src/compiler/checker.ts:17406-17408 — <c>typeArgumentsFromTypeReferenceNode</c>.</summary>
    internal List<IType>? TypeArgumentsFromTypeReferenceNode(Node node)
        => Core.Map(((INodeWithTypeArguments)node).TypeArguments, (t, _) => GetTypeFromTypeNode(t));

    /// <summary>ported from: src/compiler/checker.ts:17410-17421 — <c>getTypeFromTypeQueryNode</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's three-line specification comment is kept above the body.</remarks>
    internal IType GetTypeFromTypeQueryNode(TypeQueryNode node)
    {
        var links = GetNodeLinks(node);
        if (links.ResolvedType is null)
        {
            // TypeScript 1.0 spec (April 2014): 3.6.3
            // The expression is processed as an identifier expression (section 4.3)
            // or property access expression(section 4.10),
            // the widened type(section 3.9) of which becomes the result.
            var type = CheckExpressionWithTypeArguments(node);
            links.ResolvedType = GetRegularTypeOfLiteralType(GetWidenedType(type));
        }
        return links.ResolvedType;
    }

    /// <summary>ported from: src/compiler/checker.ts:17423-17451 — <c>getTypeOfGlobalSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>getTypeDeclaration</c> is a local function upstream and stays one here (it closes over nothing).
    /// TS passes it possibly-undefined declaration to <c>error</c>, whose port takes <c>Node?</c>; the cast from
    /// <see cref="IDeclaration"/> is the checker's usual one. <c>symbolName</c> is <c>Utilities.SymbolName</c>
    /// (utilitiesPublic.ts:870). <c>emptyGenericType</c> is reached as <c>_emptyGenericType</c>.
    /// </remarks>
    internal IObjectType GetTypeOfGlobalSymbol(ISymbol? symbol, int arity)
    {
        IDeclaration? GetTypeDeclaration(ISymbol s)
        {
            var declarations = s.Declarations;
            if (declarations is not null)
            {
                foreach (var declaration in declarations)
                {
                    switch (declaration.Kind)
                    {
                        case SyntaxKind.ClassDeclaration:
                        case SyntaxKind.InterfaceDeclaration:
                        case SyntaxKind.EnumDeclaration:
                            return declaration;
                    }
                }
            }
            return null;
        }

        if (symbol is null)
        {
            return arity != 0 ? _emptyGenericType : _emptyObjectType;
        }
        var type = GetDeclaredTypeOfSymbol(symbol);
        if ((type.Flags & TypeFlags.Object) == 0)
        {
            Error((Node?)GetTypeDeclaration(symbol), Diagnostics.Global_type_0_must_be_a_class_or_interface_type, Utilities.SymbolName(symbol));
            return arity != 0 ? _emptyGenericType : _emptyObjectType;
        }
        if (Core.Length(((IInterfaceType)type).TypeParameters) != arity)
        {
            Error((Node?)GetTypeDeclaration(symbol), Diagnostics.Global_type_0_must_have_1_type_parameter_s, Utilities.SymbolName(symbol), arity);
            return arity != 0 ? _emptyGenericType : _emptyObjectType;
        }
        return (IObjectType)type;
    }

    // ---------------------------------------------------------------------------------------------
    // The global-type getters (checker.ts:17453-17709)
    // ---------------------------------------------------------------------------------------------

    // PORT-NOTE: every getter below is the reference's `deferredGlobalX ||= resolve(...) || fallback` shape. Where the
    // reference puts the fallback *outside* the assignment — `(deferredX ||= resolve(...)) || emptyX` — the slot keeps
    // the failed attempt's value (`undefined`), so a later call retries it, and a later call that passes
    // `reportErrors: true` therefore *reports* the failure the earlier non-reporting call swallowed. The port states
    // that as `<slot> ??= resolve(...)` followed by `return <slot> ?? fallback` — folding the fallback into the
    // assignment instead (as this region once did) caches the empty type and silently drops that later report.
    // `getGlobalTypedPropertyDescriptorType` is the deliberate exception (author's note, checker.ts:17502).
    //
    // PORT-NOTE: `getGlobalType` has four overloads upstream, picking `ObjectType` for arity 0 and `GenericType` for a
    // variable arity. C# cannot overload on the literal `0`, so the port states one member returning `ObjectType` —
    // the upper bound of both — and a caller that knows its arity is non-zero casts to `IGenericType`. The cast always
    // succeeds: the port's object-type chain instantiates its leaf class, which derives from `GenericType`.

    /// <summary>ported from: src/compiler/checker.ts:17453-17455 — <c>getGlobalValueSymbol</c>.</summary>
    internal ISymbol? GetGlobalValueSymbol(string name, bool reportErrors)
        => GetGlobalSymbol(name, SymbolFlags.Value, reportErrors ? Diagnostics.Cannot_find_global_value_0 : null);

    /// <summary>ported from: src/compiler/checker.ts:17457-17459 — <c>getGlobalTypeSymbol</c>.</summary>
    internal ISymbol? GetGlobalTypeSymbol(string name, bool reportErrors)
        => GetGlobalSymbol(name, SymbolFlags.Type, reportErrors ? Diagnostics.Cannot_find_global_type_0 : null);

    /// <summary>ported from: src/compiler/checker.ts:17461-17474 — <c>getGlobalTypeAliasSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>symbol.declarations &amp;&amp; find(…, isTypeAliasDeclaration)</c> keeps TS's short-circuit and its
    /// checker-side predicate; the found <see cref="IDeclaration"/> reaches <c>error</c> through the usual
    /// <c>(Node)</c> cast.
    /// </remarks>
    internal ISymbol? GetGlobalTypeAliasSymbol(string name, int arity, bool reportErrors)
    {
        var symbol = GetGlobalSymbol(name, SymbolFlags.Type, reportErrors ? Diagnostics.Cannot_find_global_type_0 : null);
        if (symbol is not null)
        {
            // Resolve the declared type of the symbol. This resolves type parameters for the type
            // alias so that we can check arity.
            GetDeclaredTypeOfSymbol(symbol);
            if (Core.Length(GetSymbolLinks(symbol).TypeParameters) != arity)
            {
                var decl = symbol.Declarations is not null ? Core.Find(symbol.Declarations, (d, _) => NodeTests.IsTypeAliasDeclaration((Node)d)) : null;
                Error((Node?)decl, Diagnostics.Global_type_0_must_have_1_type_parameter_s, Utilities.SymbolName(symbol), arity);
                return null;
            }
        }
        return symbol;
    }

    /// <summary>ported from: src/compiler/checker.ts:17476-17479 — <c>getGlobalSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>resolveName</c> is the name resolver the utilities slice builds from <c>createNameResolver</c>
    /// (utilities.ts:11491); its C# shape is that slice's, and this call keeps the reference's six arguments — TS's
    /// comment about <c>isReference</c> being arbitrary is kept.
    /// </remarks>
    internal ISymbol? GetGlobalSymbol(string name, SymbolFlags meaning, DiagnosticMessage? diagnostic)
        // Don't track references for global symbols anyway, so value if `isReference` is arbitrary
        => ResolveName(/*location*/ null, name, meaning, diagnostic, /*isUse*/ false, /*excludeGlobals*/ false);

    /// <summary>ported from: src/compiler/checker.ts:17481-17488 — <c>getGlobalType</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's four declaration-only overloads are collapsed (see the region note).</remarks>
    internal IObjectType? GetGlobalType(string name, int arity, bool reportErrors)
    {
        var symbol = GetGlobalTypeSymbol(name, reportErrors);
        return symbol is not null || reportErrors ? GetTypeOfGlobalSymbol(symbol, arity) : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:17490-17498 — <c>getGlobalBuiltinTypes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's two declaration-only overloads return <c>ObjectType[]</c> and <c>GenericType[]</c> by arity;
    /// the port states one member over <c>GenericType</c>, the face both built-in iterator lists need.
    /// </remarks>
    internal List<IGenericType> GetGlobalBuiltinTypes(IList<string> typeNames, int arity)
    {
        List<IGenericType>? types = null;
        foreach (var typeName in typeNames)
        {
            types = Core.Append(types, (IGenericType?)GetGlobalType(typeName, arity, /*reportErrors*/ false)) as List<IGenericType>;
        }
        return types ?? [];
    }

    /// <summary>ported from: src/compiler/checker.ts:17500-17503 — <c>getGlobalTypedPropertyDescriptorType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference always reports an error here and stores the result so it is not reported more than
    /// once; that comment is kept. This is the one getter whose fallback sits *inside* the assignment
    /// (<c>||= getGlobalType(...) || emptyGenericType</c>) — the reference deliberately caches the empty type so a
    /// failed attempt is not retried; every other getter in this region keeps its fallback outside, which is what lets
    /// a later reporting call retry a failed attempt and report it (see the region note above).
    /// </remarks>
    internal IGenericType GetGlobalTypedPropertyDescriptorType()
    {
        // We always report an error, so store a result in the event we could not resolve the symbol to prevent reporting it multiple times
        _deferredGlobalTypedPropertyDescriptorType ??= (IGenericType?)GetGlobalType("TypedPropertyDescriptor", /*arity*/ 1, /*reportErrors*/ true) ?? _emptyGenericType;
        return _deferredGlobalTypedPropertyDescriptorType!;
    }

    /// <summary>ported from: src/compiler/checker.ts:17505-17508 — <c>getGlobalTemplateStringsArrayType</c>.</summary>
    internal IObjectType GetGlobalTemplateStringsArrayType()
    {
        // We always report an error, so store a result in the event we could not resolve the symbol to prevent reporting it multiple times
        _deferredGlobalTemplateStringsArrayType ??= GetGlobalType("TemplateStringsArray", /*arity*/ 0, /*reportErrors*/ true) ?? _emptyObjectType;
        return _deferredGlobalTemplateStringsArrayType!;
    }

    /// <summary>ported from: src/compiler/checker.ts:17510-17513 — <c>getGlobalImportMetaType</c>.</summary>
    internal IObjectType GetGlobalImportMetaType()
    {
        // We always report an error, so store a result in the event we could not resolve the symbol to prevent reporting it multiple times
        _deferredGlobalImportMetaType ??= GetGlobalType("ImportMeta", /*arity*/ 0, /*reportErrors*/ true) ?? _emptyObjectType;
        return _deferredGlobalImportMetaType!;
    }

    /// <summary>ported from: src/compiler/checker.ts:17515-17531 — <c>getGlobalImportMetaExpressionType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>symbol.members = members</c> and <c>metaPropertySymbol.links.type</c> are the two writes into
    /// freshly built symbols; the port routes the second through <c>getSymbolLinks</c> (the same object
    /// <c>createSymbol</c> installed). TS's <c>if (!deferredGlobalImportMetaExpressionType)</c> is the port's null test.
    /// </remarks>
    internal IObjectType GetGlobalImportMetaExpressionType()
    {
        if (_deferredGlobalImportMetaExpressionType is null)
        {
            // Create a synthetic type `ImportMetaExpression { meta: MetaProperty }`
            var symbol = CreateSymbol(SymbolFlags.None, "ImportMetaExpression");
            var importMetaType = GetGlobalImportMetaType();

            var metaPropertySymbol = CreateSymbol(SymbolFlags.Property, "meta", CheckFlags.Readonly);
            metaPropertySymbol.Parent = symbol;
            GetSymbolLinks(metaPropertySymbol).Type = importMetaType;

            var members = Utilities.CreateSymbolTable([metaPropertySymbol]);
            symbol.Members = members;

            _deferredGlobalImportMetaExpressionType = CreateAnonymousType(symbol, members, [], [], []);
        }
        return _deferredGlobalImportMetaExpressionType;
    }

    /// <summary>ported from: src/compiler/checker.ts:17533-17535 — <c>getGlobalImportCallOptionsType</c>.</summary>
    internal IObjectType GetGlobalImportCallOptionsType(bool reportErrors)
    {
        _deferredGlobalImportCallOptionsType ??= GetGlobalType("ImportCallOptions", /*arity*/ 0, reportErrors);
        return _deferredGlobalImportCallOptionsType ?? _emptyObjectType;
    }

    /// <summary>ported from: src/compiler/checker.ts:17537-17539 — <c>getGlobalImportAttributesType</c>.</summary>
    internal IObjectType GetGlobalImportAttributesType(bool reportErrors)
    {
        _deferredGlobalImportAttributesType ??= GetGlobalType("ImportAttributes", /*arity*/ 0, reportErrors);
        return _deferredGlobalImportAttributesType ?? _emptyObjectType;
    }

    /// <summary>ported from: src/compiler/checker.ts:17541-17543 — <c>getGlobalESSymbolConstructorSymbol</c>.</summary>
    internal ISymbol? GetGlobalESSymbolConstructorSymbol(bool reportErrors)
        => _deferredGlobalESSymbolConstructorSymbol ??= GetGlobalValueSymbol("Symbol", reportErrors);

    /// <summary>ported from: src/compiler/checker.ts:17545-17547 — <c>getGlobalESSymbolConstructorTypeSymbol</c>.</summary>
    internal ISymbol? GetGlobalESSymbolConstructorTypeSymbol(bool reportErrors)
        => _deferredGlobalESSymbolConstructorTypeSymbol ??= GetGlobalTypeSymbol("SymbolConstructor", reportErrors);

    /// <summary>ported from: src/compiler/checker.ts:17549-17551 — <c>getGlobalESSymbolType</c>.</summary>
    internal IObjectType GetGlobalESSymbolType()
    {
        _deferredGlobalESSymbolType ??= GetGlobalType("Symbol", /*arity*/ 0, /*reportErrors*/ false);
        return _deferredGlobalESSymbolType ?? _emptyObjectType;
    }

    /// <summary>ported from: src/compiler/checker.ts:17553-17555 — <c>getGlobalPromiseType</c>.</summary>
    internal IGenericType GetGlobalPromiseType(bool reportErrors)
    {
        _deferredGlobalPromiseType ??= (IGenericType?)GetGlobalType("Promise", /*arity*/ 1, reportErrors);
        return _deferredGlobalPromiseType ?? _emptyGenericType;
    }

    /// <summary>ported from: src/compiler/checker.ts:17557-17559 — <c>getGlobalPromiseLikeType</c>.</summary>
    internal IGenericType GetGlobalPromiseLikeType(bool reportErrors)
    {
        _deferredGlobalPromiseLikeType ??= (IGenericType?)GetGlobalType("PromiseLike", /*arity*/ 1, reportErrors);
        return _deferredGlobalPromiseLikeType ?? _emptyGenericType;
    }

    /// <summary>ported from: src/compiler/checker.ts:17561-17563 — <c>getGlobalPromiseConstructorSymbol</c>.</summary>
    internal ISymbol? GetGlobalPromiseConstructorSymbol(bool reportErrors)
        => _deferredGlobalPromiseConstructorSymbol ??= GetGlobalValueSymbol("Promise", reportErrors);

    /// <summary>ported from: src/compiler/checker.ts:17565-17567 — <c>getGlobalPromiseConstructorLikeType</c>.</summary>
    internal IObjectType GetGlobalPromiseConstructorLikeType(bool reportErrors)
    {
        _deferredGlobalPromiseConstructorLikeType ??= GetGlobalType("PromiseConstructorLike", /*arity*/ 0, reportErrors);
        return _deferredGlobalPromiseConstructorLikeType ?? _emptyObjectType;
    }

    /// <summary>ported from: src/compiler/checker.ts:17569-17571 — <c>getGlobalAsyncIterableType</c>.</summary>
    internal IGenericType GetGlobalAsyncIterableType(bool reportErrors)
    {
        _deferredGlobalAsyncIterableType ??= (IGenericType?)GetGlobalType("AsyncIterable", /*arity*/ 3, reportErrors);
        return _deferredGlobalAsyncIterableType ?? _emptyGenericType;
    }

    /// <summary>ported from: src/compiler/checker.ts:17573-17575 — <c>getGlobalAsyncIteratorType</c>.</summary>
    internal IGenericType GetGlobalAsyncIteratorType(bool reportErrors)
    {
        _deferredGlobalAsyncIteratorType ??= (IGenericType?)GetGlobalType("AsyncIterator", /*arity*/ 3, reportErrors);
        return _deferredGlobalAsyncIteratorType ?? _emptyGenericType;
    }

    /// <summary>ported from: src/compiler/checker.ts:17577-17579 — <c>getGlobalAsyncIterableIteratorType</c>.</summary>
    internal IGenericType GetGlobalAsyncIterableIteratorType(bool reportErrors)
    {
        _deferredGlobalAsyncIterableIteratorType ??= (IGenericType?)GetGlobalType("AsyncIterableIterator", /*arity*/ 3, reportErrors);
        return _deferredGlobalAsyncIterableIteratorType ?? _emptyGenericType;
    }

    /// <summary>ported from: src/compiler/checker.ts:17581-17584 — <c>getGlobalBuiltinAsyncIteratorTypes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's note (the list holds only the built-in async iterator types likely to be encountered
    /// frequently) is kept. The assignment is <c>??=</c>, as upstream.
    /// </remarks>
    internal List<IGenericType> GetGlobalBuiltinAsyncIteratorTypes()
        // NOTE: This list does not include all built-in async iterator types, only those that are likely to be encountered frequently.
        => _deferredGlobalBuiltinAsyncIteratorTypes ??= GetGlobalBuiltinTypes(["ReadableStreamAsyncIterator"], 1);

    /// <summary>ported from: src/compiler/checker.ts:17586-17588 — <c>getGlobalAsyncIteratorObjectType</c>.</summary>
    internal IGenericType GetGlobalAsyncIteratorObjectType(bool reportErrors)
    {
        _deferredGlobalAsyncIteratorObjectType ??= (IGenericType?)GetGlobalType("AsyncIteratorObject", /*arity*/ 3, reportErrors);
        return _deferredGlobalAsyncIteratorObjectType ?? _emptyGenericType;
    }

    /// <summary>ported from: src/compiler/checker.ts:17590-17592 — <c>getGlobalAsyncGeneratorType</c>.</summary>
    internal IGenericType GetGlobalAsyncGeneratorType(bool reportErrors)
    {
        _deferredGlobalAsyncGeneratorType ??= (IGenericType?)GetGlobalType("AsyncGenerator", /*arity*/ 3, reportErrors);
        return _deferredGlobalAsyncGeneratorType ?? _emptyGenericType;
    }

    /// <summary>ported from: src/compiler/checker.ts:17594-17596 — <c>getGlobalIterableType</c>.</summary>
    internal IGenericType GetGlobalIterableType(bool reportErrors)
    {
        _deferredGlobalIterableType ??= (IGenericType?)GetGlobalType("Iterable", /*arity*/ 3, reportErrors);
        return _deferredGlobalIterableType ?? _emptyGenericType;
    }

    /// <summary>ported from: src/compiler/checker.ts:17598-17600 — <c>getGlobalIteratorType</c>.</summary>
    internal IGenericType GetGlobalIteratorType(bool reportErrors)
    {
        _deferredGlobalIteratorType ??= (IGenericType?)GetGlobalType("Iterator", /*arity*/ 3, reportErrors);
        return _deferredGlobalIteratorType ?? _emptyGenericType;
    }

    /// <summary>ported from: src/compiler/checker.ts:17602-17604 — <c>getGlobalIterableIteratorType</c>.</summary>
    internal IGenericType GetGlobalIterableIteratorType(bool reportErrors)
    {
        _deferredGlobalIterableIteratorType ??= (IGenericType?)GetGlobalType("IterableIterator", /*arity*/ 3, reportErrors);
        return _deferredGlobalIterableIteratorType ?? _emptyGenericType;
    }

    /// <summary>ported from: src/compiler/checker.ts:17606-17608 — <c>getBuiltinIteratorReturnType</c>.</summary>
    internal IType GetBuiltinIteratorReturnType() => _strictBuiltinIteratorReturn ? _undefinedType : _anyType;

    /// <summary>ported from: src/compiler/checker.ts:17610-17613 — <c>getGlobalBuiltinIteratorTypes</c>.</summary>
    internal List<IGenericType> GetGlobalBuiltinIteratorTypes()
        // NOTE: This list does not include all built-in iterator types, only those that are likely to be encountered frequently.
        => _deferredGlobalBuiltinIteratorTypes ??= GetGlobalBuiltinTypes(["ArrayIterator", "MapIterator", "SetIterator", "StringIterator"], 1);

    /// <summary>ported from: src/compiler/checker.ts:17615-17617 — <c>getGlobalIteratorObjectType</c>.</summary>
    internal IGenericType GetGlobalIteratorObjectType(bool reportErrors)
    {
        _deferredGlobalIteratorObjectType ??= (IGenericType?)GetGlobalType("IteratorObject", /*arity*/ 3, reportErrors);
        return _deferredGlobalIteratorObjectType ?? _emptyGenericType;
    }

    /// <summary>ported from: src/compiler/checker.ts:17619-17621 — <c>getGlobalGeneratorType</c>.</summary>
    internal IGenericType GetGlobalGeneratorType(bool reportErrors)
    {
        _deferredGlobalGeneratorType ??= (IGenericType?)GetGlobalType("Generator", /*arity*/ 3, reportErrors);
        return _deferredGlobalGeneratorType ?? _emptyGenericType;
    }

    /// <summary>ported from: src/compiler/checker.ts:17623-17625 — <c>getGlobalIteratorYieldResultType</c>.</summary>
    internal IGenericType GetGlobalIteratorYieldResultType(bool reportErrors)
    {
        _deferredGlobalIteratorYieldResultType ??= (IGenericType?)GetGlobalType("IteratorYieldResult", /*arity*/ 1, reportErrors);
        return _deferredGlobalIteratorYieldResultType ?? _emptyGenericType;
    }

    /// <summary>ported from: src/compiler/checker.ts:17627-17629 — <c>getGlobalIteratorReturnResultType</c>.</summary>
    internal IGenericType GetGlobalIteratorReturnResultType(bool reportErrors)
    {
        _deferredGlobalIteratorReturnResultType ??= (IGenericType?)GetGlobalType("IteratorReturnResult", /*arity*/ 1, reportErrors);
        return _deferredGlobalIteratorReturnResultType ?? _emptyGenericType;
    }

    /// <summary>ported from: src/compiler/checker.ts:17631-17633 — <c>getGlobalDisposableType</c>.</summary>
    internal IObjectType GetGlobalDisposableType(bool reportErrors)
    {
        _deferredGlobalDisposableType ??= GetGlobalType("Disposable", /*arity*/ 0, reportErrors);
        return _deferredGlobalDisposableType ?? _emptyObjectType;
    }

    /// <summary>ported from: src/compiler/checker.ts:17635-17637 — <c>getGlobalAsyncDisposableType</c>.</summary>
    internal IObjectType GetGlobalAsyncDisposableType(bool reportErrors)
    {
        _deferredGlobalAsyncDisposableType ??= GetGlobalType("AsyncDisposable", /*arity*/ 0, reportErrors);
        return _deferredGlobalAsyncDisposableType ?? _emptyObjectType;
    }

    /// <summary>ported from: src/compiler/checker.ts:17639-17642 — <c>getGlobalTypeOrUndefined</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>… as GenericType</c> is applied to a possibly-arity-0 result; the port answers the
    /// <see cref="IObjectType"/> face and keeps the same shape, since the cast was already unverified upstream.
    /// </remarks>
    internal IObjectType? GetGlobalTypeOrUndefined(string name, int arity = 0)
    {
        var symbol = GetGlobalSymbol(name, SymbolFlags.Type, /*diagnostic*/ null);
        return symbol is not null ? GetTypeOfGlobalSymbol(symbol, arity) : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:17644-17648 — <c>getGlobalExtractSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS caches <c>unknownSymbol</c> as the "not found" marker and answers undefined for it; the ternary
    /// keeps that, and <c>===</c> on a symbol is <see cref="object.ReferenceEquals"/>.
    /// </remarks>
    internal ISymbol? GetGlobalExtractSymbol()
    {
        // We always report an error, so cache a result in the event we could not resolve the symbol to prevent reporting it multiple times
        _deferredGlobalExtractSymbol ??= GetGlobalTypeAliasSymbol("Extract", /*arity*/ 2, /*reportErrors*/ true) ?? _unknownSymbol;
        return ReferenceEquals(_deferredGlobalExtractSymbol, _unknownSymbol) ? null : _deferredGlobalExtractSymbol;
    }

    /// <summary>ported from: src/compiler/checker.ts:17650-17654 — <c>getGlobalOmitSymbol</c>.</summary>
    internal ISymbol? GetGlobalOmitSymbol()
    {
        // We always report an error, so cache a result in the event we could not resolve the symbol to prevent reporting it multiple times
        _deferredGlobalOmitSymbol ??= GetGlobalTypeAliasSymbol("Omit", /*arity*/ 2, /*reportErrors*/ true) ?? _unknownSymbol;
        return ReferenceEquals(_deferredGlobalOmitSymbol, _unknownSymbol) ? null : _deferredGlobalOmitSymbol;
    }

    /// <summary>ported from: src/compiler/checker.ts:17656-17660 — <c>getGlobalAwaitedSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's note is kept — only a reporting call caches <c>unknownSymbol</c>, so a non-reporting
    /// miss may be retried (and report) later.
    /// </remarks>
    internal ISymbol? GetGlobalAwaitedSymbol(bool reportErrors)
    {
        // Only cache `unknownSymbol` if we are reporting errors so that we don't report the error more than once.
        _deferredGlobalAwaitedSymbol ??= GetGlobalTypeAliasSymbol("Awaited", /*arity*/ 1, reportErrors) ?? (reportErrors ? _unknownSymbol : null);
        return ReferenceEquals(_deferredGlobalAwaitedSymbol, _unknownSymbol) ? null : _deferredGlobalAwaitedSymbol;
    }

    /// <summary>ported from: src/compiler/checker.ts:17662-17664 — <c>getGlobalBigIntType</c>.</summary>
    internal IObjectType GetGlobalBigIntType()
    {
        _deferredGlobalBigIntType ??= GetGlobalType("BigInt", /*arity*/ 0, /*reportErrors*/ false);
        return _deferredGlobalBigIntType ?? _emptyObjectType;
    }

    /// <summary>ported from: src/compiler/checker.ts:17666-17668 — <c>getGlobalClassDecoratorContextType</c>.</summary>
    internal IGenericType GetGlobalClassDecoratorContextType(bool reportErrors)
    {
        _deferredGlobalClassDecoratorContextType ??= (IGenericType?)GetGlobalType("ClassDecoratorContext", /*arity*/ 1, reportErrors);
        return _deferredGlobalClassDecoratorContextType ?? _emptyGenericType;
    }

    /// <summary>
    /// ported from: src/compiler/checker.ts:17670-17672 — <c>getGlobalClassMethodDecoratorContextType</c>.
    /// </summary>
    internal IGenericType GetGlobalClassMethodDecoratorContextType(bool reportErrors)
    {
        _deferredGlobalClassMethodDecoratorContextType ??= (IGenericType?)GetGlobalType("ClassMethodDecoratorContext", /*arity*/ 2, reportErrors);
        return _deferredGlobalClassMethodDecoratorContextType ?? _emptyGenericType;
    }

    /// <summary>
    /// ported from: src/compiler/checker.ts:17674-17676 — <c>getGlobalClassGetterDecoratorContextType</c>.
    /// </summary>
    internal IGenericType GetGlobalClassGetterDecoratorContextType(bool reportErrors)
    {
        _deferredGlobalClassGetterDecoratorContextType ??= (IGenericType?)GetGlobalType("ClassGetterDecoratorContext", /*arity*/ 2, reportErrors);
        return _deferredGlobalClassGetterDecoratorContextType ?? _emptyGenericType;
    }

    /// <summary>
    /// ported from: src/compiler/checker.ts:17678-17680 — <c>getGlobalClassSetterDecoratorContextType</c>.
    /// </summary>
    internal IGenericType GetGlobalClassSetterDecoratorContextType(bool reportErrors)
    {
        _deferredGlobalClassSetterDecoratorContextType ??= (IGenericType?)GetGlobalType("ClassSetterDecoratorContext", /*arity*/ 2, reportErrors);
        return _deferredGlobalClassSetterDecoratorContextType ?? _emptyGenericType;
    }

    /// <summary>
    /// ported from: src/compiler/checker.ts:17682-17684 — <c>getGlobalClassAccessorDecoratorContextType</c>.
    /// </summary>
    internal IGenericType GetGlobalClassAccessorDecoratorContextType(bool reportErrors)
    {
        _deferredGlobalClassAccessorDecoratorContextType ??= (IGenericType?)GetGlobalType("ClassAccessorDecoratorContext", /*arity*/ 2, reportErrors);
        return _deferredGlobalClassAccessorDecoratorContextType ?? _emptyGenericType;
    }

    /// <summary>
    /// ported from: src/compiler/checker.ts:17686-17688 — <c>getGlobalClassAccessorDecoratorTargetType</c>.
    /// </summary>
    internal IGenericType GetGlobalClassAccessorDecoratorTargetType(bool reportErrors)
    {
        _deferredGlobalClassAccessorDecoratorTargetType ??= (IGenericType?)GetGlobalType("ClassAccessorDecoratorTarget", /*arity*/ 2, reportErrors);
        return _deferredGlobalClassAccessorDecoratorTargetType ?? _emptyGenericType;
    }

    /// <summary>
    /// ported from: src/compiler/checker.ts:17690-17692 — <c>getGlobalClassAccessorDecoratorResultType</c>.
    /// </summary>
    internal IGenericType GetGlobalClassAccessorDecoratorResultType(bool reportErrors)
    {
        _deferredGlobalClassAccessorDecoratorResultType ??= (IGenericType?)GetGlobalType("ClassAccessorDecoratorResult", /*arity*/ 2, reportErrors);
        return _deferredGlobalClassAccessorDecoratorResultType ?? _emptyGenericType;
    }

    /// <summary>
    /// ported from: src/compiler/checker.ts:17694-17696 — <c>getGlobalClassFieldDecoratorContextType</c>.
    /// </summary>
    internal IGenericType GetGlobalClassFieldDecoratorContextType(bool reportErrors)
    {
        _deferredGlobalClassFieldDecoratorContextType ??= (IGenericType?)GetGlobalType("ClassFieldDecoratorContext", /*arity*/ 2, reportErrors);
        return _deferredGlobalClassFieldDecoratorContextType ?? _emptyGenericType;
    }

    /// <summary>ported from: src/compiler/checker.ts:17698-17700 — <c>getGlobalNaNSymbol</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>||=</c> here is <c>??=</c> — the resolved value is a symbol or undefined.</remarks>
    internal ISymbol? GetGlobalNaNSymbol()
        => _deferredGlobalNaNSymbol ??= GetGlobalValueSymbol("NaN", /*reportErrors*/ false);

    /// <summary>ported from: src/compiler/checker.ts:17702-17705 — <c>getGlobalRecordSymbol</c>.</summary>
    internal ISymbol? GetGlobalRecordSymbol()
    {
        _deferredGlobalRecordSymbol ??= GetGlobalTypeAliasSymbol("Record", /*arity*/ 2, /*reportErrors*/ true) ?? _unknownSymbol;
        return ReferenceEquals(_deferredGlobalRecordSymbol, _unknownSymbol) ? null : _deferredGlobalRecordSymbol;
    }

    // ---------------------------------------------------------------------------------------------
    // Array and tuple types (checker.ts:17710-18062)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:17710-17712 — <c>createTypeFromGenericGlobalType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's doc comment ("instantiates a global type that is generic with some element type, and returns
    /// that instantiation") is kept on the method summary below. <c>!== emptyGenericType</c> is a reference comparison
    /// upstream and stays one here.
    /// <para>
    /// Instantiates a global type that is generic with some element type, and returns that instantiation.
    /// </para>
    /// </remarks>
    internal IObjectType CreateTypeFromGenericGlobalType(IGenericType genericGlobalType, List<IType> typeArguments)
        => !ReferenceEquals(genericGlobalType, _emptyGenericType)
            ? CreateTypeReference(genericGlobalType, typeArguments)
            : _emptyObjectType;

    /// <summary>ported from: src/compiler/checker.ts:17714-17716 — <c>createTypedPropertyDescriptorType</c>.</summary>
    internal IType CreateTypedPropertyDescriptorType(IType propertyType)
        => CreateTypeFromGenericGlobalType(GetGlobalTypedPropertyDescriptorType(), [propertyType]);

    /// <summary>ported from: src/compiler/checker.ts:17718-17720 — <c>createIterableType</c>.</summary>
    internal IType CreateIterableType(IType iteratedType)
        => CreateTypeFromGenericGlobalType(GetGlobalIterableType(/*reportErrors*/ true), [iteratedType, _voidType, _undefinedType]);

    /// <summary>ported from: src/compiler/checker.ts:17722-17724 — <c>createArrayType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>readonly?: boolean</c> selects between the two global array types by truthiness; the port
    /// states <c>bool?</c> and tests <c>== true</c>, which is the same answer for the absent case.
    /// </remarks>
    internal IObjectType CreateArrayType(IType elementType, bool? @readonly = null)
        => CreateTypeFromGenericGlobalType(@readonly == true ? _globalReadonlyArrayType : _globalArrayType, [elementType]);

    /// <summary>ported from: src/compiler/checker.ts:17726-17739 — <c>getTupleElementFlags</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the two <c>questionToken</c>/<c>dotDotDotToken</c> tests are JS truthiness on optional nodes, so the
    /// port tests them against null.
    /// </remarks>
    internal ElementFlags GetTupleElementFlags(ITypeNode node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.OptionalType:
                return ElementFlags.Optional;
            case SyntaxKind.RestType:
                return GetRestTypeElementFlags(node);
            case SyntaxKind.NamedTupleMember:
                return ((NamedTupleMember)node).QuestionToken is not null
                    ? ElementFlags.Optional
                    : ((NamedTupleMember)node).DotDotDotToken is not null
                        ? GetRestTypeElementFlags(node)
                        : ElementFlags.Required;
            default:
                return ElementFlags.Required;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:17741-17743 — <c>getRestTypeElementFlags</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>RestTypeNode | NamedTupleMember</c>; both classes are type nodes with
    /// a <c>type</c> slot but share no interface, so the port takes <see cref="ITypeNode"/> and selects the slot by
    /// kind — the same treatment a TS union gets elsewhere (§12.5).
    /// </remarks>
    internal ElementFlags GetRestTypeElementFlags(ITypeNode node)
        => GetArrayElementTypeNode(node.Kind == SyntaxKind.NamedTupleMember ? ((NamedTupleMember)node).Type! : ((RestTypeNode)node).Type!) is not null
            ? ElementFlags.Rest
            : ElementFlags.Variadic;

    /// <summary>ported from: src/compiler/checker.ts:17745-17753 — <c>getArrayOrTupleTargetType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>ArrayTypeNode | TupleTypeNode</c>; the port takes
    /// <see cref="ITypeNode"/> and reads the elements through the tuple class, as <c>isReadonlyTypeOperator</c> reads
    /// the parent (which the parser always sets, hence <see cref="Debug.CheckDefined{T}"/>).
    /// </remarks>
    internal IGenericType GetArrayOrTupleTargetType(Node node)
    {
        var @readonly = IsReadonlyTypeOperator(Debug.CheckDefined(node.Parent));
        var elementType = GetArrayElementTypeNode((ITypeNode)node);
        if (elementType is not null)
        {
            return @readonly ? _globalReadonlyArrayType : _globalArrayType;
        }
        var tuple = (TupleTypeNode)node;
        var elementFlags = Core.Map(tuple.Elements, (e, _) => GetTupleElementFlags((ITypeNode)e))!;
        return GetTupleTargetType(elementFlags, @readonly, Core.Map(tuple.Elements, (e, _) => MemberIfLabeledElementDeclaration(e)!)!);
    }

    /// <summary>
    /// ported from: src/compiler/checker.ts:17755-17757 — <c>memberIfLabeledElementDeclaration</c>.
    /// </summary>
    internal Node? MemberIfLabeledElementDeclaration(Node member)
        => NodeTests.IsNamedTupleMember(member) || NodeTests.IsParameter(member) ? member : null;

    /// <summary>ported from: src/compiler/checker.ts:17761-17770 — <c>isDeferredTypeReferenceNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's two-line comment above the function is kept verbatim. TS's <c>hasDefaultTypeArguments</c>
    /// is tested by JS truthiness, so an absent flag is <c>false</c>.
    /// </remarks>
    internal bool IsDeferredTypeReferenceNode(Node node, bool? hasDefaultTypeArguments = null)
        // Return true if the given type reference node is directly aliased or if it needs to be deferred
        // because it is possibly contained in a circular chain of eagerly resolved types.
        => GetAliasSymbolForTypeNode(node) is not null
            || IsResolvedByTypeAlias(node) && (
                node.Kind == SyntaxKind.ArrayType ? MayResolveTypeAlias((Node)((ArrayTypeNode)node).ElementType!) :
                    node.Kind == SyntaxKind.TupleType ? Core.Some(((TupleTypeNode)node).Elements, e => MayResolveTypeAlias((Node)e)) :
                    hasDefaultTypeArguments == true || Core.Some(((INodeWithTypeArguments)node).TypeArguments, t => MayResolveTypeAlias((Node)t)));

    /// <summary>ported from: src/compiler/checker.ts:17772-17792 — <c>isResolvedByTypeAlias</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's comment (the constructs that eagerly resolve their constituents, and why
    /// <c>TypeReference</c> is among them) is kept. The switch has no <c>default</c> arm upstream — falling out of it
    /// answers <c>false</c>, which the port states explicitly.
    /// </remarks>
    internal bool IsResolvedByTypeAlias(Node node)
    {
        // Return true when the given node is transitively contained in type constructs that eagerly
        // resolve their constituent types. We include SyntaxKind.TypeReference because type arguments
        // of type aliases are eagerly resolved.
        var parent = Debug.CheckDefined(node.Parent);
        switch (parent.Kind)
        {
            case SyntaxKind.ParenthesizedType:
            case SyntaxKind.NamedTupleMember:
            case SyntaxKind.TypeReference:
            case SyntaxKind.UnionType:
            case SyntaxKind.IntersectionType:
            case SyntaxKind.IndexedAccessType:
            case SyntaxKind.ConditionalType:
            case SyntaxKind.TypeOperator:
            case SyntaxKind.ArrayType:
            case SyntaxKind.TupleType:
                return IsResolvedByTypeAlias(parent);
            case SyntaxKind.TypeAliasDeclaration:
                return true;
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:17794-17822 — <c>mayResolveTypeAlias</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's opening comment is kept. Its casts to the unions <c>ParenthesizedTypeNode |
    /// OptionalTypeNode | JSDocTypeReferencingNode | NamedTupleMember</c> and <c>UnionOrIntersectionTypeNode</c> read a
    /// slot the classes share but no C# type does, so those two arms select by pattern instead of by cast — the same
    /// treatment the port gives a TS union anywhere else (§12.5).
    /// </remarks>
    internal bool MayResolveTypeAlias(Node node)
    {
        // Return true if resolving the given node (i.e. getTypeFromTypeNode) possibly causes resolution
        // of a type alias.
        switch (node.Kind)
        {
            case SyntaxKind.TypeReference:
                return IsJSDocTypeReference(node) || (ResolveTypeReferenceName(node, SymbolFlags.Type).Flags & SymbolFlags.TypeAlias) != 0;
            case SyntaxKind.TypeQuery:
                return true;
            case SyntaxKind.TypeOperator:
                return ((TypeOperatorNode)node).Operator != SyntaxKind.UniqueKeyword && MayResolveTypeAlias((Node)((TypeOperatorNode)node).Type!);
            case SyntaxKind.ParenthesizedType:
            case SyntaxKind.OptionalType:
            case SyntaxKind.NamedTupleMember:
            case SyntaxKind.JSDocOptionalType:
            case SyntaxKind.JSDocNullableType:
            case SyntaxKind.JSDocNonNullableType:
            case SyntaxKind.JSDocTypeExpression:
                return MayResolveTypeAlias((Node)(node switch
                {
                    ParenthesizedTypeNode parenthesized => parenthesized.Type!,
                    OptionalTypeNode optional => optional.Type!,
                    NamedTupleMember named => named.Type!,
                    JSDocOptionalType jsDocOptional => jsDocOptional.Type!,
                    JSDocNullableType jsDocNullable => jsDocNullable.Type!,
                    JSDocTypeExpression jsDocExpression => jsDocExpression.Type!,
                    _ => ((JSDocNonNullableType)node).Type!,
                }));
            case SyntaxKind.RestType:
                return ((RestTypeNode)node).Type!.Kind != SyntaxKind.ArrayType || MayResolveTypeAlias((Node)((ArrayTypeNode)((RestTypeNode)node).Type!).ElementType!);
            case SyntaxKind.UnionType:
            case SyntaxKind.IntersectionType:
                return Core.Some(node switch
                {
                    UnionTypeNode union => union.Types,
                    _ => ((IntersectionTypeNode)node).Types,
                }, t => MayResolveTypeAlias((Node)t));
            case SyntaxKind.IndexedAccessType:
                return MayResolveTypeAlias((Node)((IndexedAccessTypeNode)node).ObjectType!) || MayResolveTypeAlias((Node)((IndexedAccessTypeNode)node).IndexType!);
            case SyntaxKind.ConditionalType:
                return MayResolveTypeAlias((Node)((ConditionalTypeNode)node).CheckType!) || MayResolveTypeAlias((Node)((ConditionalTypeNode)node).ExtendsType!) ||
                    MayResolveTypeAlias((Node)((ConditionalTypeNode)node).TrueType!) || MayResolveTypeAlias((Node)((ConditionalTypeNode)node).FalseType!);
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:17824-17841 — <c>getTypeFromArrayOrTupleTypeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the target comparison is TS's <c>=== emptyGenericType</c> and stays a reference comparison. The
    /// tuple-element reads go through the tuple class because both kinds meet at <see cref="ITypeNode"/>.
    /// </remarks>
    internal IType GetTypeFromArrayOrTupleTypeNode(ITypeNode node)
    {
        var links = GetNodeLinks((Node)node);
        if (links.ResolvedType is null)
        {
            var target = GetArrayOrTupleTargetType((Node)node);
            if (ReferenceEquals(target, _emptyGenericType))
            {
                links.ResolvedType = _emptyObjectType;
            }
            else if (!(node.Kind == SyntaxKind.TupleType && Core.Some(((TupleTypeNode)node).Elements, e => (GetTupleElementFlags((ITypeNode)e) & ElementFlags.Variadic) != 0))
                && IsDeferredTypeReferenceNode((Node)node))
            {
                links.ResolvedType = node.Kind == SyntaxKind.TupleType && Core.Length(((TupleTypeNode)node).Elements) == 0
                    ? (IType)target
                    : CreateDeferredTypeReference(target, (Node)node, /*mapper*/ null);
            }
            else
            {
                List<IType> elementTypes = node.Kind == SyntaxKind.ArrayType
                    ? [GetTypeFromTypeNode(((ArrayTypeNode)node).ElementType!)]
                    : Core.Map(((TupleTypeNode)node).Elements, (e, _) => GetTypeFromTypeNode((ITypeNode)e))!;
                links.ResolvedType = CreateNormalizedTypeReference(target, elementTypes);
            }
        }
        return links.ResolvedType;
    }

    /// <summary>ported from: src/compiler/checker.ts:17843-17845 — <c>isReadonlyTypeOperator</c>.</summary>
    internal bool IsReadonlyTypeOperator(Node node)
        => NodeTests.IsTypeOperatorNode(node) && ((TypeOperatorNode)node).Operator == SyntaxKind.ReadonlyKeyword;

    /// <summary>ported from: src/compiler/checker.ts:17847-17852 — <c>createTupleType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's fourth parameter defaults to <c>[]</c>; C# default arguments must be constants, so the port
    /// defaults to <c>null</c> and substitutes the empty list — the two are indistinguishable to
    /// <c>getTupleTargetType</c>, which only asks whether any member is set.
    /// </remarks>
    internal IType CreateTupleType(List<IType> elementTypes, List<ElementFlags>? elementFlags = null, bool @readonly = false, List<Node>? namedMemberDeclarations = null)
    {
        var tupleTarget = GetTupleTargetType(elementFlags ?? Core.Map(elementTypes, (_, _) => ElementFlags.Required)!, @readonly, namedMemberDeclarations ?? []);
        return ReferenceEquals(tupleTarget, _emptyGenericType) ? _emptyObjectType :
            elementTypes.Count != 0 ? CreateNormalizedTypeReference(tupleTarget, elementTypes) :
            (IObjectType)tupleTarget;
    }

    /// <summary>ported from: src/compiler/checker.ts:17854-17868 — <c>getTupleTargetType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the cache key is the reference's concatenation of a per-element flag letter, an <c>"R"</c> for
    /// readonly and the labelled members' node ids; <c>map(…).join()</c> is <c>string.Join(",", …)</c> (JS's
    /// parameterless <c>join</c> joins with a comma) and <c>"" + i</c> of a node id is an invariant-culture
    /// <c>ToString</c>. <see cref="_tupleTypes"/> is declared in this file; see its note.
    /// </remarks>
    internal IGenericType GetTupleTargetType(List<ElementFlags> elementFlags, bool @readonly, List<Node> namedMemberDeclarations)
    {
        if (elementFlags.Count == 1 && (elementFlags[0] & ElementFlags.Rest) != 0)
        {
            // [...X[]] is equivalent to just X[]
            return @readonly ? _globalReadonlyArrayType : _globalArrayType;
        }
        var key = string.Join(",", Core.Map(elementFlags, (f, _) =>
                (f & ElementFlags.Required) != 0 ? "#" : (f & ElementFlags.Optional) != 0 ? "?" : (f & ElementFlags.Rest) != 0 ? "." : "*")!)
            + (@readonly ? "R" : "")
            + (Core.Some(namedMemberDeclarations, node => Js.IsTruthy(node))
                ? "," + string.Join(",", Core.Map(namedMemberDeclarations, (node, _) => Js.IsTruthy(node) ? Utilities.GetNodeId(node).ToString(CultureInfo.InvariantCulture) : "_")!)
                : "");
        if (!_tupleTypes.TryGetValue(key, out var type) || type is null)
        {
            type = CreateTupleTargetType(elementFlags, @readonly, namedMemberDeclarations);
            _tupleTypes[key] = type;
        }
        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:17870-17930 — <c>createTupleTargetType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's comment block describing the synthesized tuple interface (and that it has no symbol
    /// and neither do its type parameters) is kept. TS builds <c>typeParameters</c> with <c>new Array(arity)</c> and
    /// then fills every index in order, so a capacity-initialized list is the same array. The three slots TS leaves
    /// <c>undefined</c> (<c>outerTypeParameters</c>, the declared signature lists) are the port's <c>null</c>/empty
    /// list. <c>labeledElementDeclarations</c> keeps TS's possibly-undefined members as null holes (the model types
    /// the slot's elements as non-null; <c>some</c> below tests them for null exactly as upstream).
    /// </remarks>
    internal ITupleType CreateTupleTargetType(List<ElementFlags> elementFlags, bool @readonly, List<Node> namedMemberDeclarations)
    {
        // We represent tuple types as type references to synthesized generic interface types created by
        // this function. The types are of the form:
        //
        //   interface Tuple<T0, T1, T2, ...> extends Array<T0 | T1 | T2 | ...> { 0: T0, 1: T1, 2: T2, ... }
        //
        // Note that the generic type created by this function has no symbol associated with it. The same
        // is true for each of the synthesized type parameters.
        var arity = elementFlags.Count;
        var minLength = Core.CountWhere(elementFlags, (f, _) => (f & (ElementFlags.Required | ElementFlags.Variadic)) != 0);
        List<ITypeParameter>? typeParameters = null;
        var properties = new List<ISymbol>();
        var combinedFlags = (ElementFlags)0;
        if (arity != 0)
        {
            typeParameters = new List<ITypeParameter>(arity);
            for (var i = 0; i < arity; i++)
            {
                var typeParameter = CreateTypeParameter();
                typeParameters.Add(typeParameter);
                var flags = elementFlags[i];
                combinedFlags |= flags;
                if ((combinedFlags & ElementFlags.Variable) == 0)
                {
                    var property = CreateSymbol(
                        SymbolFlags.Property | ((flags & ElementFlags.Optional) != 0 ? SymbolFlags.Optional : SymbolFlags.None),
                        i.ToString(CultureInfo.InvariantCulture),
                        @readonly ? CheckFlags.Readonly : CheckFlags.None);
                    GetSymbolLinks(property).TupleLabelDeclaration = Core.ElementAt(namedMemberDeclarations, i);
                    GetSymbolLinks(property).Type = typeParameter;
                    properties.Add(property);
                }
            }
        }
        var fixedLength = properties.Count;
        var lengthSymbol = CreateSymbol(SymbolFlags.Property, "length", @readonly ? CheckFlags.Readonly : CheckFlags.None);
        if ((combinedFlags & ElementFlags.Variable) != 0)
        {
            GetSymbolLinks(lengthSymbol).Type = _numberType;
        }
        else
        {
            var literalTypes = new List<IType>();
            for (var i = minLength; i <= arity; i++)
            {
                literalTypes.Add(GetNumberLiteralType(i));
            }
            GetSymbolLinks(lengthSymbol).Type = GetUnionType(literalTypes);
        }
        properties.Add(lengthSymbol);
        var type = (TupleType)CreateObjectType(ObjectFlags.Tuple | ObjectFlags.Reference);
        type.TypeParameters = typeParameters;
        type.OuterTypeParameters = null;
        type.LocalTypeParameters = typeParameters;
        type.Instantiations = new Dictionary<string, ITypeReference>();
        type.Instantiations![GetTypeListId(type.TypeParameters is null ? null : [.. type.TypeParameters.Cast<IType>()])] = type;
        type.Target = type;
        type.ResolvedTypeArguments = typeParameters is null ? null : [.. typeParameters.Cast<IType>()];
        type.ThisType = CreateTypeParameter();
        type.ThisType!.IsThisType = true;
        type.ThisType!.Constraint = type;
        type.DeclaredProperties = properties;
        type.DeclaredCallSignatures = [];
        type.DeclaredConstructSignatures = [];
        type.DeclaredIndexInfos = [];
        type.ElementFlags = elementFlags;
        type.MinLength = minLength;
        type.FixedLength = fixedLength;
        type.HasRestElement = (combinedFlags & ElementFlags.Variable) != 0;
        type.CombinedFlags = combinedFlags;
        type.Readonly = @readonly;
        type.LabeledElementDeclarations = namedMemberDeclarations;
        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:17932-17934 — <c>createNormalizedTypeReference</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>target.objectFlags &amp; ObjectFlags.Tuple</c> reads the flag through M6Generics'
    /// <c>GetObjectFlags</c> (utilities.ts:8280) — the reference member is declared in Checker.Support.cs.
    /// </remarks>
    internal IType CreateNormalizedTypeReference(IGenericType target, List<IType>? typeArguments)
        => (GetObjectFlags(target) & ObjectFlags.Tuple) != 0
            ? CreateNormalizedTupleType((ITupleType)target, typeArguments!)
            : CreateTypeReference(target, typeArguments);

    /// <summary>ported from: src/compiler/checker.ts:17936-18026 — <c>createNormalizedTupleType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's comments (the variadic-distribution rewrite, the two legal tuple layouts, the
    /// 10,000-element guard, and the splice of the elements between the first rest and the last optional/rest) are all
    /// kept. <c>addElement</c> is a local function upstream and stays one here, capturing the three indices and the
    /// three accumulating lists exactly as upstream; C# allows it to be called before its declaration, as TS does.
    /// The <c>splice(i + 1, n)</c> calls are <c>RemoveRange</c> (same start, same count).
    /// </remarks>
    internal IType CreateNormalizedTupleType(ITupleType target, List<IType> elementTypes)
    {
        if ((target.CombinedFlags & ElementFlags.NonRequired) == 0)
        {
            // No need to normalize when we only have regular required elements
            return CreateTypeReference(target, elementTypes);
        }
        if ((target.CombinedFlags & ElementFlags.Variadic) != 0)
        {
            // Transform [A, ...(X | Y | Z)] into [A, ...X] | [A, ...Y] | [A, ...Z]
            var unionIndex = Core.FindIndex(elementTypes, (t, i) => (target.ElementFlags![i] & ElementFlags.Variadic) != 0 && (t.Flags & (TypeFlags.Never | TypeFlags.Union)) != 0);
            if (unionIndex >= 0)
            {
                return CheckCrossProductUnion(Core.Map(elementTypes, (t, i) => (target.ElementFlags![i] & ElementFlags.Variadic) != 0 ? t : _unknownType)!)
                    ? MapType(elementTypes[unionIndex], t => CreateNormalizedTupleType(target, (List<IType>)Core.ReplaceElement(elementTypes, unionIndex, t)))!
                    : _errorType;
            }
        }
        // We have optional, rest, or variadic elements that may need normalizing. Normalization ensures that all variadic
        // elements are generic and that the tuple type has one of the following layouts, disregarding variadic elements:
        // (1) Zero or more required elements, followed by zero or more optional elements, followed by zero or one rest element.
        // (2) Zero or more required elements, followed by a rest element, followed by zero or more required elements.
        // In either layout, zero or more generic variadic elements may be present at any location.
        var expandedTypes = new List<IType>();
        var expandedFlags = new List<ElementFlags>();
        var expandedDeclarations = new List<Node>();
        var lastRequiredIndex = -1;
        var firstRestIndex = -1;
        var lastOptionalOrRestIndex = -1;
        for (var i = 0; i < elementTypes.Count; i++)
        {
            var type = elementTypes[i];
            var flags = target.ElementFlags![i];
            if ((flags & ElementFlags.Variadic) != 0)
            {
                if ((type.Flags & TypeFlags.Any) != 0)
                {
                    AddElement(type, ElementFlags.Rest, Core.ElementAt(target.LabeledElementDeclarations, i));
                }
                else if ((type.Flags & TypeFlags.InstantiableNonPrimitive) != 0 || IsGenericMappedType(type))
                {
                    // Generic variadic elements stay as they are.
                    AddElement(type, ElementFlags.Variadic, Core.ElementAt(target.LabeledElementDeclarations, i));
                }
                else if (IsTupleType(type))
                {
                    var tupleTarget = (ITupleType)((ITupleTypeReference)type).Target!;
                    var elements = GetElementTypes((ITupleTypeReference)type);
                    if (elements.Count + expandedTypes.Count >= 10_000)
                    {
                        Error(
                            _currentNode,
                            Utilities.IsPartOfTypeNode(_currentNode!)
                                ? Diagnostics.Type_produces_a_tuple_type_that_is_too_large_to_represent
                                : Diagnostics.Expression_produces_a_tuple_type_that_is_too_large_to_represent);
                        return _errorType;
                    }
                    // Spread variadic elements with tuple types into the resulting tuple.
                    for (var n = 0; n < elements.Count; n++)
                    {
                        AddElement(elements[n], tupleTarget.ElementFlags![n], Core.ElementAt(tupleTarget.LabeledElementDeclarations, n));
                    }
                }
                else
                {
                    // Treat everything else as an array type and create a rest element.
                    var indexType = IsArrayLikeType(type) ? GetIndexTypeOfType(type, _numberType) : null;
                    AddElement(indexType ?? _errorType, ElementFlags.Rest, Core.ElementAt(target.LabeledElementDeclarations, i));
                }
            }
            else
            {
                // Copy other element kinds with no change.
                AddElement(type, flags, Core.ElementAt(target.LabeledElementDeclarations, i));
            }
        }
        // Turn optional elements preceding the last required element into required elements
        for (var i = 0; i < lastRequiredIndex; i++)
        {
            if ((expandedFlags[i] & ElementFlags.Optional) != 0)
            {
                expandedFlags[i] = ElementFlags.Required;
            }
        }
        if (firstRestIndex >= 0 && firstRestIndex < lastOptionalOrRestIndex)
        {
            // Turn elements between first rest and last optional/rest into a single rest element
            expandedTypes[firstRestIndex] = GetUnionType((Core.SameMap(
                Core.Slice(expandedTypes, firstRestIndex, lastOptionalOrRestIndex + 1),
                (t, i) => (expandedFlags[firstRestIndex + i] & ElementFlags.Variadic) != 0 ? GetIndexedAccessType(t, _numberType) : t) as List<IType>)!);
            expandedTypes.RemoveRange(firstRestIndex + 1, lastOptionalOrRestIndex - firstRestIndex);
            expandedFlags.RemoveRange(firstRestIndex + 1, lastOptionalOrRestIndex - firstRestIndex);
            expandedDeclarations.RemoveRange(firstRestIndex + 1, lastOptionalOrRestIndex - firstRestIndex);
        }
        var normalizedTarget = GetTupleTargetType(expandedFlags, target.Readonly, expandedDeclarations);
        return ReferenceEquals(normalizedTarget, _emptyGenericType) ? _emptyObjectType :
            expandedFlags.Count != 0 ? CreateTypeReference(normalizedTarget, expandedTypes) :
            normalizedTarget;

        void AddElement(IType type, ElementFlags flags, Node? declaration)
        {
            if ((flags & ElementFlags.Required) != 0)
            {
                lastRequiredIndex = expandedFlags.Count;
            }
            if ((flags & ElementFlags.Rest) != 0 && firstRestIndex < 0)
            {
                firstRestIndex = expandedFlags.Count;
            }
            if ((flags & (ElementFlags.Optional | ElementFlags.Rest)) != 0)
            {
                lastOptionalOrRestIndex = expandedFlags.Count;
            }
            expandedTypes.Add((flags & ElementFlags.Optional) != 0 ? AddOptionality(type, /*isProperty*/ true) : type);
            expandedFlags.Add(flags);
            expandedDeclarations.Add(declaration!);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:18028-18033 — <c>sliceTupleType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>type.target</c> is narrowed to <c>TupleType</c> upstream by the <c>TupleTypeReference</c>
    /// declaration; C# cannot narrow an inherited slot, so the port casts (the tuple faces always carry a tuple
    /// target — the checker only reaches this through <c>isTupleType</c>). Every <c>.slice(a, b)</c> is
    /// <c>Core.Slice</c>, JS's own semantics.
    /// </remarks>
    internal IType SliceTupleType(ITupleTypeReference type, int index, int endSkipCount = 0)
    {
        var target = (ITupleType)type.Target!;
        var endIndex = GetTypeReferenceArity(type) - endSkipCount;
        return index > target.FixedLength
            ? GetRestArrayTypeOfTupleType(type) ?? CreateTupleType([])
            : CreateTupleType(
                (Core.Slice(GetTypeArguments(type), index, endIndex) as List<IType>)!,
                Core.Slice(target.ElementFlags!, index, endIndex) as List<ElementFlags>,
                /*readonly*/ false,
                target.LabeledElementDeclarations is null ? null : Core.Slice(target.LabeledElementDeclarations, index, endIndex) as List<Node>);
    }

    /// <summary>ported from: src/compiler/checker.ts:18035-18037 — <c>getKnownKeysOfTupleType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>arrayOf(fixedLength, i =&gt; getStringLiteralType("" + i))</c> is <c>Core.ArrayOf</c> with the
    /// invariant-culture rendering of the index; the array-feeding <c>append</c> is <c>Core.Append</c>, which skips a
    /// null value the same way TS's <c>append</c> skips undefined.
    /// </remarks>
    internal IType GetKnownKeysOfTupleType(ITupleTypeReference type)
    {
        var target = (ITupleType)type.Target!;
        return GetUnionType((Core.Append(
            Core.ArrayOf(target.FixedLength, i => (IType)GetStringLiteralType(i.ToString(CultureInfo.InvariantCulture))) as List<IType>,
            GetIndexType(target.Readonly ? _globalReadonlyArrayType : _globalArrayType))! as List<IType>)!);
    }

    /// <summary>ported from: src/compiler/checker.ts:18040-18043 — <c>getStartElementCount</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's one-line comment ("return count of starting consecutive tuple elements of the given
    /// kind(s)") is kept.
    /// </remarks>
    internal int GetStartElementCount(ITupleType type, ElementFlags flags)
    {
        // Return count of starting consecutive tuple elements of the given kind(s)
        var index = Core.FindIndex(type.ElementFlags, (f, _) => (f & flags) == 0);
        return index >= 0 ? index : type.ElementFlags!.Count;
    }

    /// <summary>ported from: src/compiler/checker.ts:18046-18048 — <c>getEndElementCount</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's comment ("return count of ending consecutive tuple elements of the given kind(s)")
    /// sits above <c>getStartElementCount</c> and applies to both; it is kept there.
    /// </remarks>
    internal int GetEndElementCount(ITupleType type, ElementFlags flags)
        => type.ElementFlags!.Count - Core.FindLastIndex(type.ElementFlags, (f, _) => (f & flags) == 0) - 1;

    /// <summary>ported from: src/compiler/checker.ts:18050-18052 — <c>getTotalFixedElementCount</c>.</summary>
    internal int GetTotalFixedElementCount(ITupleType type)
        => type.FixedLength + GetEndElementCount(type, ElementFlags.Fixed);

    /// <summary>ported from: src/compiler/checker.ts:18054-18058 — <c>getElementTypes</c>.</summary>
    internal List<IType> GetElementTypes(ITupleTypeReference type)
    {
        var typeArguments = GetTypeArguments(type);
        var arity = GetTypeReferenceArity(type);
        return typeArguments.Count == arity ? typeArguments : (Core.Slice(typeArguments, 0, arity) as List<IType>)!;
    }

    /// <summary>ported from: src/compiler/checker.ts:18060-18062 — <c>getTypeFromOptionalTypeNode</c>.</summary>
    internal IType GetTypeFromOptionalTypeNode(OptionalTypeNode node)
        => AddOptionality(GetTypeFromTypeNode(node.Type!), /*isProperty*/ true);
}
