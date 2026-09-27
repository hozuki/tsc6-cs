namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:12988-12998 (isReferenceToSomeType)
// ported from: src/compiler/checker.ts:13000-13005 (isReferenceToType)
// ported from: src/compiler/checker.ts:13012-13024 (hasBaseType)
// ported from: src/compiler/checker.ts:13152-13154 (getTypeParametersOfClassOrInterface)
// ported from: src/compiler/checker.ts:13170-13179 (isConstructorType)
// ported from: src/compiler/checker.ts:13186-13190 (getConstructorsForTypeArguments)
// ported from: src/compiler/checker.ts:13192-13196 (getInstantiatedConstructorsForTypeArguments)
// ported from: src/compiler/checker.ts:13253-13273 (getImplementsTypes)
// ported from: src/compiler/checker.ts:13275-13277 (reportCircularBaseType)
// ported from: src/compiler/checker.ts:13312-13315 (getTupleBaseType)
// ported from: src/compiler/checker.ts:13317-13372 (resolveBaseTypesOfClass)
// ported from: src/compiler/checker.ts:13374-13384 (areAllOuterTypeParametersApplied)
// ported from: src/compiler/checker.ts:13387-13398 (isValidBaseType)
// ported from: src/compiler/checker.ts:13400-13429 (resolveBaseTypesOfInterface)
/// <summary>
/// The base-type walk of the checker: the reference predicates the heritage-clause checks read, the constructor
/// lookup a class's <c>extends</c> expression resolves through, and the two resolvers that fill
/// <c>resolvedBaseTypes</c> for a class and for an interface.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (scope): this file is the slice that owns checker.ts:12988-13750, but several members of that range are
/// declared by sibling slices that needed them first and are therefore only <em>called</em> from here, by their exact
/// ported names:
/// <c>GetTargetType</c> (13007-13009), <c>AppendTypeParameters</c> (13029-13034),
/// <c>GetOuterTypeParameters</c> (13037-13108), <c>GetOuterTypeParametersOfClassOrInterface</c> (13111-13127),
/// <c>GetLocalTypeParametersOfClassOrInterfaceOrTypeAlias</c> (13130-13148), <c>IsMixinConstructorType</c>
/// (13158-13168), <c>GetBaseTypeNodeOfClass</c> (13181-13184), <c>GetBaseConstructorTypeOfClass</c> (13206-13251) and
/// <c>GetBaseTypes</c> (13279-13310) in <c>Checker/Checker.SymbolSupport.cs</c>; <c>IsThislessInterface</c>
/// (13438-13460) and <c>IsThislessType</c>/<c>IsThislessTypeParameter</c>/
/// <c>IsThislessVariableLikeDeclaration</c>/<c>IsThislessFunctionLikeDeclaration</c>/<c>IsThisless</c>
/// (13641-13717) in <c>Checker/Checker.TypeSupport.cs</c>; and <c>GetClassLikeDeclarationOfSymbol</c>
/// (utilities.ts:8275-8277) in <c>Checker/Checker.Support.cs</c>. A second declaration of any of them would be a
/// duplicate member of the one <c>Checker</c> partial class.
/// </para>
/// <para>
/// PORT-NOTE (empty-array identity): the reference leans on two module-level sentinel arrays — core.ts's shared
/// <c>emptyArray</c> and utilities.ts:612's <c>resolvingEmptyArray</c> — and distinguishes them by identity
/// (<c>type.resolvedBaseTypes === resolvingEmptyArray</c> at 13364, <c>=== emptyArray</c> at 13410). The port has no
/// shared empty array (its slices write <c>[]</c>, a fresh list, as <c>Checker/Checker.TypeReferences.cs</c> records),
/// so <c>ResolveBaseTypesOfClass</c> keeps a frame-local sentinel for the <c>resolvingEmptyArray</c> test and
/// <c>ResolveBaseTypesOfInterface</c> states the <c>emptyArray</c> test as an emptiness test; both call sites carry the
/// argument that the arm taken, and the list contents, are the reference's.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // Reference predicates and the base-type walk (checker.ts:12988-13024)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:12988-12998 — <c>isReferenceToSomeType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference declares <c>type: Type</c> yet guards <c>type === undefined</c>, because the call sites
    /// hand it the results of error-suppressed global-type lookups
    /// (<c>isReferenceToSomeType(type, resolver.getGlobalBuiltinIteratorTypes())</c>, checker.ts:46124/:46270); the port
    /// states the same possibility as the <c>IType?</c> parameter, which the guard then answers <c>false</c> for.
    /// <c>(type as TypeReference).target === target</c> is TS's object identity test, which is
    /// <see cref="object.ReferenceEquals"/> on the port's unique type objects — the same idiom
    /// <c>Checker.SymbolSupport.cs</c> uses for its <c>getElementTypeOfArrayType(paramType) === anyType</c>.
    /// </remarks>
    internal bool IsReferenceToSomeType(IType? type, IList<IType> targets)
    {
        if (type is null || (GetObjectFlags(type) & ObjectFlags.Reference) == 0)
        {
            return false;
        }

        foreach (var target in targets)
        {
            if (ReferenceEquals(((ITypeReference)type).Target, target))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:13000-13005 — <c>isReferenceToType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: both <c>type !== undefined</c> and <c>target !== undefined</c> are part of the reference's answer,
    /// so both parameters stay nullable and the conjunction is kept in the reference's order.
    /// </remarks>
    internal bool IsReferenceToType(IType? type, IType? target)
        => type is not null
            && target is not null
            && (GetObjectFlags(type) & ObjectFlags.Reference) != 0
            && ReferenceEquals(((ITypeReference)type).Target, target);

    // TODO: GH#18217 If `checkBase` is undefined, we should not call this because this will always return false.
    /// <summary>ported from: src/compiler/checker.ts:13012-13024 — <c>hasBaseType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>check</c> is a hoisted local function that recurses through itself; the port
    /// states the same recursive local function. Its two casts (<c>type as InterfaceType</c>,
    /// <c>type as IntersectionType</c>) are the port's <see cref="IInterfaceType"/> and
    /// <see cref="IIntersectionType"/>, and <c>some(getBaseTypes(target), check)</c> is
    /// <see cref="Core.Some{T}"/> over the same predicate — <c>getBaseTypes</c> answers the <c>BaseType[]</c> the
    /// reference walks.
    /// </remarks>
    internal bool HasBaseType(IType type, IType? checkBase)
    {
        return Check(type);

        bool Check(IType type)
        {
            if ((GetObjectFlags(type) & (ObjectFlags.ClassOrInterface | ObjectFlags.Reference)) != 0)
            {
                var target = (IInterfaceType)GetTargetType(type);
                return ReferenceEquals(target, checkBase) || Core.Some(GetBaseTypes(target), t => Check(t));
            }
            else if ((type.Flags & TypeFlags.Intersection) != 0)
            {
                return Core.Some(((IIntersectionType)type).Types, t => Check(t));
            }

            return false;
        }
    }

    // The full set of type parameters for a generic class or interface type consists of its outer type parameters plus
    // its locally declared type parameters.
    /// <summary>ported from: src/compiler/checker.ts:13152-13154 — <c>getTypeParametersOfClassOrInterface</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <see cref="Core.Concatenate{T}"/> answers the non-empty argument when the other side is empty
    /// (core.ts:672-676) and a fresh list otherwise, so the result is a <see cref="List{T}"/> on every path while the
    /// reference's <c>TypeParameter[] | undefined</c> is expressed by the nullable return; the cast is the one
    /// <c>Checker.Queries.cs</c> writes at its own <c>Core.Concatenate</c> call site.
    /// </remarks>
    internal List<ITypeParameter>? GetTypeParametersOfClassOrInterface(ISymbol symbol)
        => Core.Concatenate(GetOuterTypeParametersOfClassOrInterface(symbol), GetLocalTypeParametersOfClassOrInterfaceOrTypeAlias(symbol)) as List<ITypeParameter>;

    /// <summary>ported from: src/compiler/checker.ts:13170-13179 — <c>isConstructorType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>getBaseConstraintOfType</c> answers a possibly-absent type in the port
    /// (<c>Checker/Checker.Support.cs</c>), so <c>!!constraint</c> is stated as <c>is not null</c>; nothing else in the
    /// member differs from the reference.
    /// </remarks>
    internal bool IsConstructorType(IType type)
    {
        if (GetSignaturesOfType(type, SignatureKind.Construct).Count > 0)
        {
            return true;
        }

        if ((type.Flags & TypeFlags.TypeVariable) != 0)
        {
            var constraint = GetBaseConstraintOfType(type);
            return constraint is not null && IsMixinConstructorType(constraint);
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:13186-13190 — <c>getConstructorsForTypeArguments</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>readonly TypeNode[] | undefined</c> is the port's
    /// <c>IList&lt;ITypeNode&gt;?</c> (a <see cref="NodeArray{T}"/> is a <c>List</c> here, so a heritage clause's
    /// <c>typeArguments</c> passes straight through). <see cref="Core.Filter{T}"/> answers the input instance when
    /// nothing is filtered out — and the input here is <c>getSignaturesOfType</c>'s <c>List&lt;ISignature&gt;</c> — so
    /// the cast is the port's <c>readonly Signature[]</c> face and never a reinterpretation.
    /// </remarks>
    internal List<ISignature> GetConstructorsForTypeArguments(IType type, IList<ITypeNode>? typeArgumentNodes, Node location)
    {
        var typeArgCount = Core.Length(typeArgumentNodes);
        var isJavascript = Utilities.IsInJSFile(location);
        return (List<ISignature>)Core.Filter(
            GetSignaturesOfType(type, SignatureKind.Construct),
            sig => (isJavascript || typeArgCount >= GetMinTypeArgumentCount(sig.TypeParameters)) && typeArgCount <= Core.Length(sig.TypeParameters))!;
    }

    /// <summary>ported from: src/compiler/checker.ts:13192-13196 — <c>getInstantiatedConstructorsForTypeArguments</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>map(typeArgumentNodes, getTypeFromTypeNode)</c> is <see cref="Core.Map{T,TU}"/> with the
    /// reference's function applied per element — the port's <c>GetTypeFromTypeNode</c> takes only the node, so the
    /// index <see cref="Core.Map{T,TU}"/> supplies is discarded. <c>some(sig.typeParameters)</c> is the reference's
    /// predicate-less core <c>some</c>, which <see cref="Core.Some{T}"/> reproduces as "the list is non-empty";
    /// <c>sameMap</c> maps the same element type here, so <see cref="Core.SameMap{T}"/> keeps the reference's
    /// identity-preserving contract, and its possible input instance is <c>getConstructorsForTypeArguments</c>'s
    /// <c>List&lt;ISignature&gt;</c>. The <c>getSignatureInstantiation</c> it calls is landed as
    /// <c>Checker/Checker.TypeSupport.cs:1842</c> with the reference's range 16561-16576.
    /// </remarks>
    internal List<ISignature> GetInstantiatedConstructorsForTypeArguments(IType type, IList<ITypeNode>? typeArgumentNodes, Node location)
    {
        var signatures = GetConstructorsForTypeArguments(type, typeArgumentNodes, location);
        var typeArguments = Core.Map(typeArgumentNodes, (node, _) => GetTypeFromTypeNode(node));
        return (List<ISignature>)Core.SameMap(
            signatures,
            (sig, _) => Core.Some(sig.TypeParameters) ? GetSignatureInstantiation(sig, typeArguments, Utilities.IsInJSFile(location)) : sig)!;
    }

    /// <summary>ported from: src/compiler/checker.ts:13253-13273 — <c>getImplementsTypes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference starts from core.ts's shared <c>emptyArray</c> and switches to a fresh array with the
    /// first accepted type (<c>resolvedImplementsTypes === emptyArray ? [implementsType as ObjectType] :
    /// resolvedImplementsTypes.push(implementsType)</c>); that identity dance exists only to keep the shared array
    /// unmutated. The port's <c>[]</c> is already a per-call mutable list, so the two arms collapse into one
    /// <c>Add</c> with the same contents.
    /// <para>
    /// PORT-NOTE: <c>getEffectiveImplementsTypeNodes</c> is a utilities.ts member (utilities.ts:5214) and is declared by
    /// the utilities slice. Its reference parameter, <c>ClassLikeDeclaration</c>, is reached through the unchecked
    /// <c>declaration as ClassLikeDeclaration</c> on this line, and the declarations it actually receives are every
    /// declaration of the type symbol (a JSDoc <c>@implements</c> tag is legal on a function, variable or property
    /// declaration in a JS file, and that is the arm the reference's own <c>isInJSFile</c> test takes); the port
    /// therefore states the cast as the widened <see cref="Node"/> parameter that member takes, and no cast remains
    /// here.
    /// </para>
    /// </remarks>
    internal List<IType> GetImplementsTypes(IInterfaceType type)
    {
        var resolvedImplementsTypes = new List<IType>();
        if (type.Symbol!.Declarations is not null)
        {
            foreach (var declaration in type.Symbol!.Declarations)
            {
                var implementsTypeNodes = Utilities.GetEffectiveImplementsTypeNodes((Node)declaration);
                if (implementsTypeNodes is null) continue;
                foreach (var node in implementsTypeNodes)
                {
                    var implementsType = GetTypeFromTypeNode(node);
                    if (!IsErrorType(implementsType))
                    {
                        resolvedImplementsTypes.Add(implementsType);
                    }
                }
            }
        }

        return resolvedImplementsTypes;
    }

    /// <summary>ported from: src/compiler/checker.ts:13275-13277 — <c>reportCircularBaseType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>typeToString(type, undefined, TypeFormatFlags.WriteArrayAsGenericType)</c> is the port's
    /// three-argument <c>TypeToString</c> with the absent enclosing declaration stated as <c>null</c>.
    /// </remarks>
    internal void ReportCircularBaseType(Node node, IType type)
    {
        // PORT-NOTE (A″): printer-gated stringifier arguments are deferred — see Core/Diagnostic.cs's `DiagnosticArgument`.
        Error(node, Diagnostics.Type_0_recursively_references_itself_as_a_base_type, Diagnostic.DeferArg(() => TypeToString(type, /*enclosingDeclaration*/ null, TypeFormatFlags.WriteArrayAsGenericType)));
    }

    // ---------------------------------------------------------------------------------------------
    // Resolving the base types (checker.ts:13312-13429)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:13312-13315 — <c>getTupleBaseType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>sameMap(type.typeParameters, …)</c> widens each <c>TypeParameter</c> to a
    /// <c>Type</c>, and the port's <see cref="Core.SameMap{T}"/> cannot carry that widening (its callback is
    /// <c>Func&lt;T, int, T&gt;</c>), so the element types are built with <see cref="Core.Map{T,TU}"/> — a fresh list,
    /// as in <c>Parser/ParenthesizerRules.cs</c> for the same helper. The element objects are unchanged, and the only
    /// consumer, <c>getUnionType</c>, copies or reduces them, so the lost array identity is unobservable. The
    /// reference's <c>elementTypes || emptyArray</c> is stated as the null arm of the same expression, and its inferred
    /// <c>Type</c> return is stated as <see cref="IType"/> (<c>createArrayType</c> answers the object type it built).
    /// </remarks>
    internal IType GetTupleBaseType(ITupleType type)
    {
        var elementTypes = Core.Map(
            type.TypeParameters,
            (t, i) => (type.ElementFlags![i] & ElementFlags.Variadic) != 0 ? GetIndexedAccessType(t, _numberType) : (IType)t);
        return CreateArrayType(GetUnionType(elementTypes ?? []), type.Readonly);
    }

    /// <summary>ported from: src/compiler/checker.ts:13317-13372 — <c>resolveBaseTypesOfClass</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE (the <c>resolvingEmptyArray</c> sentinel): the reference marks the slot with utilities.ts:612's shared
    /// <c>resolvingEmptyArray</c> on entry and later asks whether that very object is still there (13364) to learn that
    /// no nested resolution replaced it, in which case the members it partially instantiated must be dropped. The port
    /// has no shared empty array, so the frame keeps its own sentinel and compares against that; every path that
    /// returns a value assigns a different list (the reference's <c>emptyArray</c>/<c>[reducedBaseType]</c>), so the
    /// identity test decides exactly what the reference's does. Those error arms keep the reference's <c>emptyArray</c>
    /// as the port's <c>[]</c>, the same absent-of-base-types value with a list the caller cannot alias.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>getBaseTypeNodeOfClass(type)!</c> and <c>originalBaseType!</c> keep the reference's asserted
    /// non-null (the guard above returns before the first is read, and the second is read only under the
    /// <c>baseConstructorType.symbol</c> test that produced it). <c>type as ObjectType</c> at
    /// <c>resolveStructuredTypeMembers</c> is the cast <c>Checker/Checker.SymbolSupport.cs</c> states at its own call
    /// site, since the port's resolver takes the object family's base class.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>elaborateNeverIntersection</c> (checker.ts:15871-15882) is landed as
    /// <c>Checker/Checker.Members.cs:928</c> (<c>IDiagnosticMessageChain? (IDiagnosticMessageChain?, IType)</c>), so the
    /// call here is unqualified and binds there, with the reference's <c>undefined</c> error info stated as
    /// <c>null</c>; its nullable result is handed to <c>chainDiagnosticMessages</c>, whose port parameter is
    /// <c>object?</c>.
    /// <c>diagnostics.add</c> is <c>_diagnostics.Add</c>, and <c>createDiagnosticForNodeFromMessageChain</c>'s source
    /// file argument is the <c>Debug.CheckDefined</c> of <c>Utilities.GetSourceFileOfNode</c> — the same shape the
    /// landed diagnostic call sites use.
    /// </para>
    /// </remarks>
    internal void ResolveBaseTypesOfClass(IInterfaceType type)
    {
        var resolvingEmptyArray = new List<IType>();
        type.ResolvedBaseTypes = resolvingEmptyArray;
        var baseConstructorType = GetApparentType(GetBaseConstructorTypeOfClass(type));
        if ((baseConstructorType.Flags & (TypeFlags.Object | TypeFlags.Intersection | TypeFlags.Any)) == 0)
        {
            type.ResolvedBaseTypes = [];
            return;
        }

        var baseTypeNode = GetBaseTypeNodeOfClass(type)!;
        IType baseType;
        var originalBaseType = baseConstructorType.Symbol is not null ? GetDeclaredTypeOfSymbol(baseConstructorType.Symbol) : null;
        if (
            baseConstructorType.Symbol is not null && (baseConstructorType.Symbol.Flags & SymbolFlags.Class) != 0 &&
            AreAllOuterTypeParametersApplied(originalBaseType!)
        )
        {
            // When base constructor type is a class with no captured type arguments we know that the constructors all have the same type parameters as the
            // class and all return the instance type of the class. There is no need for further checks and we can apply the
            // type arguments in the same manner as a type reference to get the same error reporting experience.
            baseType = GetTypeFromClassOrInterfaceReference(baseTypeNode, baseConstructorType.Symbol!);
        }
        else if ((baseConstructorType.Flags & TypeFlags.Any) != 0)
        {
            baseType = baseConstructorType;
        }
        else
        {
            // The class derives from a "class-like" constructor function, check that we have at least one construct signature
            // with a matching number of type parameters and use the return type of the first instantiated signature. Elsewhere
            // we check that all instantiated signatures return the same type.
            var constructors = GetInstantiatedConstructorsForTypeArguments(baseConstructorType, baseTypeNode.TypeArguments, baseTypeNode);
            if (constructors.Count == 0)
            {
                Error(baseTypeNode.Expression!, Diagnostics.No_base_constructor_has_the_specified_number_of_type_arguments);
                type.ResolvedBaseTypes = [];
                return;
            }

            baseType = GetReturnTypeOfSignature(constructors[0]);
        }

        if (IsErrorType(baseType))
        {
            type.ResolvedBaseTypes = [];
            return;
        }

        var reducedBaseType = GetReducedType(baseType);
        if (!IsValidBaseType(reducedBaseType))
        {
            var elaboration = ElaborateNeverIntersection(/*errorInfo*/ null, baseType);
            var diagnostic = Utilities.ChainDiagnosticMessages(elaboration, Diagnostics.Base_constructor_return_type_0_is_not_an_object_type_or_intersection_of_object_types_with_statically_known_members, Diagnostic.DeferArg(() => TypeToString(reducedBaseType)));
            _diagnostics.Add(Utilities.CreateDiagnosticForNodeFromMessageChain(Debug.CheckDefined(Utilities.GetSourceFileOfNode(baseTypeNode.Expression!)), baseTypeNode.Expression!, diagnostic));
            type.ResolvedBaseTypes = [];
            return;
        }

        if (ReferenceEquals(type, reducedBaseType) || HasBaseType(reducedBaseType, type))
        {
            Error((Node?)type.Symbol!.ValueDeclaration, Diagnostics.Type_0_recursively_references_itself_as_a_base_type, Diagnostic.DeferArg(() => TypeToString(type, /*enclosingDeclaration*/ null, TypeFormatFlags.WriteArrayAsGenericType)));
            type.ResolvedBaseTypes = [];
            return;
        }

        if (ReferenceEquals(type.ResolvedBaseTypes, resolvingEmptyArray))
        {
            // Circular reference, likely through instantiation of default parameters
            // (otherwise there'd be an error from hasBaseType) - this is fine, but `.members` should be reset
            // as `getIndexedAccessType` via `instantiateType` via `getTypeFromClassOrInterfaceReference` forces a
            // partial instantiation of the members without the base types fully resolved
            type.Members = null;
        }

        type.ResolvedBaseTypes = [reducedBaseType];
    }

    /// <summary>ported from: src/compiler/checker.ts:13374-13384 — <c>areAllOuterTypeParametersApplied</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's trailing <c>// TODO: GH#18217 Shouldn't this take an InterfaceType?</c> is kept.
    /// Both casts are the port's faces (<see cref="IInterfaceType"/>, <see cref="ITypeReference"/>), and
    /// <c>outerTypeParameters[last].symbol !== typeArguments[last].symbol</c> is compared with
    /// <see cref="object.ReferenceEquals"/> — the port's type and symbol objects are unique per declaration, so TS's
    /// <c>!==</c> on object references is the same test.
    /// </remarks>
    internal bool AreAllOuterTypeParametersApplied(IType type) // TODO: GH#18217 Shouldn't this take an InterfaceType?
    {
        // An unapplied type parameter has its symbol still the same as the matching argument symbol.
        // Since parameters are applied outer-to-inner, only the last outer parameter needs to be checked.
        var outerTypeParameters = ((IInterfaceType)type).OuterTypeParameters;
        if (outerTypeParameters is not null)
        {
            var last = outerTypeParameters.Count - 1;
            var typeArguments = GetTypeArguments((ITypeReference)type);
            return !ReferenceEquals(outerTypeParameters[last].Symbol, typeArguments[last].Symbol);
        }

        return true;
    }

    // A valid base type is `any`, an object type or intersection of object types.
    /// <summary>ported from: src/compiler/checker.ts:13387-13398 — <c>isValidBaseType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's return is the type predicate <c>type is BaseType</c>, which narrows at its call
    /// sites; C# has no such narrowing through a helper, so the port states the same answer as <c>bool</c> — no ported
    /// call site relies on the narrowing (13354 and 47210 only test it, and 13408's caller casts to
    /// <c>ObjectType</c> itself). The reference's <c>every((type as IntersectionType).types, isValidBaseType)</c> is
    /// <see cref="Core.Every{T}"/>, and its comment questioning the <c>!isGenericMappedType</c> arm is kept.
    /// </remarks>
    internal bool IsValidBaseType(IType type)
    {
        if ((type.Flags & TypeFlags.TypeParameter) != 0)
        {
            var constraint = GetBaseConstraintOfType(type);
            if (constraint is not null)
            {
                return IsValidBaseType(constraint);
            }
        }

        // TODO: Given that we allow type parmeters here now, is this `!isGenericMappedType(type)` check really needed?
        // There's no reason a `T` should be allowed while a `Readonly<T>` should not.
        return ((type.Flags & (TypeFlags.Object | TypeFlags.NonPrimitive | TypeFlags.Any)) != 0 && !IsGenericMappedType(type)) ||
            ((type.Flags & TypeFlags.Intersection) != 0 && Core.Every(((IIntersectionType)type).Types, (t, _) => IsValidBaseType(t)));
    }

    /// <summary>ported from: src/compiler/checker.ts:13400-13429 — <c>resolveBaseTypesOfInterface</c>.</summary>
    /// <remarks>
    /// PORT-NOTE (the <c>emptyArray</c> sentinel): the reference states the slot as
    /// <c>type.resolvedBaseTypes || emptyArray</c> and, for each accepted base type, replaces the slot when it is still
    /// core.ts's shared <c>emptyArray</c> and pushes onto it otherwise. That identity test is stated here as an
    /// emptiness test on the list the slot currently holds, re-read per accepted base type as the reference re-reads
    /// the slot. The two agree on the contents of every state that can arise: the shared empty array is only ever
    /// installed on a slot that has recorded no base type (this member's <c>||</c>, and the class resolver's error
    /// arms) and is never mutated, since replacing it is exactly what the first arm does, so "is the shared empty
    /// array" and "holds no base type" coincide; and where the reference's <c>push</c> lands on an *empty* list — a
    /// list an outer, re-entered frame installed as its own sentinel, or utilities.ts:612's <c>resolvingEmptyArray</c>
    /// — the port takes the replacing arm and builds a one-element list instead, which is the same content. TS's shared
    /// empty array is not ported at all (see <c>Checker/Checker.TypeReferences.cs</c>'s PORT-NOTE at its
    /// <c>emptyArray</c> note), so there is no object to test identity against.
    /// <para>
    /// PORT-NOTE: <c>getInterfaceBaseTypeNodes</c> is a utilities.ts member (utilities.ts:5236) declared by the
    /// utilities slice, not here (declared nowhere in the port yet); the reference calls it twice — once as the guard
    /// and once for the walk — and the port keeps both calls. Its ported face takes the reference's
    /// <c>InterfaceDeclaration</c> class, as the sibling call in <c>Checker/Checker.TypeSupport.cs</c> does.
    /// </para>
    /// </remarks>
    internal void ResolveBaseTypesOfInterface(IInterfaceType type)
    {
        type.ResolvedBaseTypes ??= [];
        if (type.Symbol!.Declarations is not null)
        {
            foreach (var declaration in type.Symbol!.Declarations)
            {
                if (declaration.Kind == SyntaxKind.InterfaceDeclaration && Utilities.GetInterfaceBaseTypeNodes((InterfaceDeclaration)declaration) is not null)
                {
                    foreach (var node in Utilities.GetInterfaceBaseTypeNodes((InterfaceDeclaration)declaration)!)
                    {
                        var baseType = GetReducedType(GetTypeFromTypeNode(node));
                        if (!IsErrorType(baseType))
                        {
                            if (IsValidBaseType(baseType))
                            {
                                if (!ReferenceEquals(type, baseType) && !HasBaseType(baseType, type))
                                {
                                    // PORT-NOTE: re-read of the slot, as the reference does at its identity test; the
                                    // slot is the list assigned above or by an earlier accepted base type.
                                    var resolvedBaseTypes = type.ResolvedBaseTypes!;
                                    if (resolvedBaseTypes.Count == 0)
                                    {
                                        type.ResolvedBaseTypes = [baseType];
                                    }
                                    else
                                    {
                                        resolvedBaseTypes.Add(baseType);
                                    }
                                }
                                else
                                {
                                    ReportCircularBaseType((Node)declaration, type);
                                }
                            }
                            else
                            {
                                Error(node, Diagnostics.An_interface_can_only_extend_an_object_type_or_intersection_of_object_types_with_statically_known_members);
                            }
                        }
                    }
                }
            }
        }
    }
}
