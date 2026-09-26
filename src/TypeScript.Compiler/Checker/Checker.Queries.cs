namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:11575-11601 (getTypeOfPropertyOfType … isErrorType)
// ported from: src/compiler/checker.ts:12839-13000 (getTypeOfEnumMember … getNonMissingTypeOfSymbol)
// ported from: src/compiler/checker.ts:13463-13640 (getDeclaredTypeOfClassOrInterface … tryGetDeclaredTypeOfSymbol)
// ported from: src/compiler/checker.ts:15058-15100 (getPropertiesOfObjectType … getPropertiesOfType)
// ported from: src/compiler/checker.ts:15503-15570 (getApparentTypeOfMappedType … getApparentType)
// ported from: src/compiler/checker.ts:15893-16032 (getPropertyOfType … getIndexInfosOfType)
// ported from: src/compiler/checker.ts:16366-16408 (getSignaturesOfSymbol)
// ported from: src/compiler/checker.ts:16473-16543 (getReturnTypeOfSignature, getReturnTypeFromAnnotation)
/// <summary>
/// The query block: what the properties, signatures, index infos and declared type of a symbol or type are.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: the reference's query functions take <c>Type</c> and cast to the face the flags say they have; the port
/// keeps that in every case where the cast is a downcast of M6-0's class chain (so it succeeds for the object family's
/// leaf) and narrows through <see cref="IUnionOrIntersectionType"/>/<see cref="ITypeReference"/> faces otherwise.
/// </para>
/// <para>
/// PORT-NOTE: this file calls a large number of members of the sibling slices and of the checker blocks this slice does
/// not own — <c>GetReducedApparentType</c>, <c>GetUnionOrIntersectionProperty</c>, <c>GetIndexInfoOfType</c>,
/// <c>GetIndexTypeOfType</c>, <c>EveryType</c>/<c>MapType</c>/<c>SomeType</c>, <c>GetTypeFromTypeNode</c>,
/// <c>InstantiateType</c>, <c>GetTypeListId</c>, <c>PushTypeResolution</c>/<c>PopTypeResolution</c>,
/// <c>SymbolToString</c>, the <c>global*Type</c> slots and the <c>getTypeOf*</c> workers that need flow analysis. Each
/// is listed with its owner in the slice report.
/// </para>
/// <para>
/// PORT-NOTE: the three <c>arrayFallbackSignatures</c> steps of <c>getSignaturesOfType</c> write
/// <c>UnionType.arrayFallbackSignatures</c>, which the generated model already provides
/// (<c>Model/UnionOrIntersectionType.cs</c>), so no cast is needed there.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // The properties of a type (checker.ts:11575-11601, 15058-15100, 15893-15960)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:11575-11582 — <c>getTypeOfPropertyOfType</c>.</summary>
    internal IType? GetTypeOfPropertyOfType(IType? type, string? propertyName)
    {
        var prop = GetPropertyOfType(type, propertyName);
        return prop is not null ? GetTypeOfSymbol(prop) : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:11584-11589 — <c>getTypeOfPropertyOrIndexSignatureOfType</c>.</summary>
    internal IType? GetTypeOfPropertyOrIndexSignatureOfType(IType type, string name)
    {
        var prop = GetPropertyOfType(type, name);
        return prop is not null ? GetTypeOfSymbol(prop) : GetIndexTypeOfType(type, _stringType);
    }

    /// <summary>ported from: src/compiler/checker.ts:11591-11593 — <c>isTypeAny</c>.</summary>
    internal static bool IsTypeAny(IType? type) => type is not null && (type.Flags & TypeFlags.Any) != 0;

    /// <summary>ported from: src/compiler/checker.ts:11595-11599 — <c>isErrorType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's second arm is <c>!!(type.flags &amp; TypeFlags.Any &amp;&amp; type.aliasSymbol)</c> — the *only*
    /// <c>any</c> types carrying an alias symbol are the ones
    /// <c>getTypeFromTypeAliasReference</c> (checker.ts:17050-17062) manufactures for a reference to an unresolved
    /// symbol, and those must behave like <c>errorType</c>. C# states the two parts as its own conjunction over the
    /// nullable <c>AliasSymbol</c> slot.
    /// </remarks>
    internal bool IsErrorType(IType type)
        => ReferenceEquals(type, _errorType) || (type.Flags & TypeFlags.Any) != 0 && type.AliasSymbol is not null;

    /// <summary>Return properties of an object type or an empty array for other types.</summary>
    /// <remarks>ported from: src/compiler/checker.ts:15058-15063 — <c>getPropertiesOfObjectType</c>.</remarks>
    internal List<ISymbol> GetPropertiesOfObjectType(IType type)
        => (type.Flags & TypeFlags.Object) != 0
            ? ResolveStructuredTypeMembers((UnionOrIntersectionType)type).Properties ?? []
            : [];

    /// <summary>
    /// If the given type is an object type and that type has a property by the given name,
    /// return the symbol for that property. Otherwise return undefined.
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:15068-15076 — <c>getPropertyOfObjectType</c>.</remarks>
    internal ISymbol? GetPropertyOfObjectType(IType type, string name)
    {
        if ((type.Flags & TypeFlags.Object) != 0)
        {
            var resolved = ResolveStructuredTypeMembers((UnionOrIntersectionType)type);
            var symbol = resolved.Members?.GetValueOrDefault(name);
            if (symbol is not null && SymbolIsValue(symbol))
            {
                return symbol;
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:15078-15099 — <c>getPropertiesOfUnionOrIntersectionType</c>.</summary>
    internal List<ISymbol> GetPropertiesOfUnionOrIntersectionType(UnionOrIntersectionType type)
    {
        if (type.ResolvedProperties is null)
        {
            var members = Utilities.CreateSymbolTable();
            foreach (var current in type.Types!)
            {
                foreach (var prop in GetPropertiesOfType(current))
                {
                    if (!members.ContainsKey(prop.EscapedName ?? ""))
                    {
                        var combinedProp = GetPropertyOfUnionOrIntersectionType(type, prop.EscapedName ?? "", /*skipObjectFunctionPropertyAugment*/ (type.Flags & TypeFlags.Intersection) != 0);
                        if (combinedProp is not null)
                        {
                            members[prop.EscapedName ?? ""] = combinedProp;
                        }
                    }
                }

                // The properties of a union type are those that are present in all constituent types, so
                // we only need to check the properties of the first type without index signature
                if ((type.Flags & TypeFlags.Union) != 0 && GetIndexInfosOfType(current).Count == 0)
                {
                    break;
                }
            }

            type.ResolvedProperties = GetNamedMembers(members, type.Symbol);
        }

        return type.ResolvedProperties;
    }

    /// <summary>ported from: src/compiler/checker.ts:15101-15106 — <c>getPropertiesOfType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's first statement, <c>type = getReducedApparentType(type)</c>, is what makes a
    /// <c>this</c> type (a <c>TypeFlags.TypeParameter</c>) or another instantiable answer for the properties of the type
    /// it stands for — the apparent type is the class instance type, whose properties the caller then sees.
    /// </remarks>
    internal List<ISymbol> GetPropertiesOfType(IType? type)
    {
        type = GetReducedApparentType(type!);
        return (type.Flags & TypeFlags.UnionOrIntersection) != 0
            ? GetPropertiesOfUnionOrIntersectionType((UnionOrIntersectionType)type)
            : GetPropertiesOfObjectType(type);
    }

    /// <summary>
    /// Return the symbol for the property with the given name in the given type. Creates synthetic union properties when
    /// necessary, maps primitive types and type parameters are to their apparent types, and augments with properties from
    /// Object and Function as appropriate.
    /// </summary>
    /// <param name="type">a type to look up property from</param>
    /// <param name="name">a name of property to look up in a given type</param>
    /// <remarks>ported from: src/compiler/checker.ts:15893-15934 — <c>getPropertyOfType</c>.</remarks>
    internal ISymbol? GetPropertyOfType(IType? type, string? propertyName, bool? skipObjectFunctionPropertyAugment = null, bool? includeTypeOnlyMembers = null)
    {
        type = GetReducedApparentType(type!);
        var name = propertyName!;
        if ((type.Flags & TypeFlags.Object) != 0)
        {
            var resolved = ResolveStructuredTypeMembers((UnionOrIntersectionType)type);
            var symbol = resolved.Members?.GetValueOrDefault(name);
            if (symbol is not null && includeTypeOnlyMembers != true && type.Symbol is not null && (type.Symbol.Flags & SymbolFlags.ValueModule) != 0 && GetSymbolLinks(type.Symbol).TypeOnlyExportStarMap?.ContainsKey(name) == true)
            {
                // If this is the type of a module, `resolved.members.get(name)` might have effectively skipped over
                // an `export type * from './foo'`, leaving `symbolIsValue` unable to see that the symbol is being
                // viewed through a type-only export.
                return null;
            }

            if (symbol is not null && SymbolIsValue(symbol, includeTypeOnlyMembers))
            {
                return symbol;
            }

            if (skipObjectFunctionPropertyAugment == true) return null;
            var functionType = ReferenceEquals(resolved, _anyFunctionType) ? _globalFunctionType :
                Core.Length(resolved.CallSignatures) != 0 ? _globalCallableFunctionType :
                Core.Length(resolved.ConstructSignatures) != 0 ? _globalNewableFunctionType :
                null;
            if (functionType is not null)
            {
                var functionSymbol = GetPropertyOfObjectType(functionType, name);
                if (functionSymbol is not null)
                {
                    return functionSymbol;
                }
            }

            return GetPropertyOfObjectType(_globalObjectType, name);
        }

        if ((type.Flags & TypeFlags.Intersection) != 0)
        {
            var prop = GetPropertyOfUnionOrIntersectionType((UnionOrIntersectionType)type, name, /*skipObjectFunctionPropertyAugment*/ true);
            if (prop is not null)
            {
                return prop;
            }

            if (skipObjectFunctionPropertyAugment != true)
            {
                return GetPropertyOfUnionOrIntersectionType((UnionOrIntersectionType)type, name, skipObjectFunctionPropertyAugment);
            }

            return null;
        }

        if ((type.Flags & TypeFlags.Union) != 0)
        {
            return GetPropertyOfUnionOrIntersectionType((UnionOrIntersectionType)type, name, skipObjectFunctionPropertyAugment);
        }

        return null;
    }

    // ---------------------------------------------------------------------------------------------
    // Signatures and index infos (checker.ts:15936-16032)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:15936-15942 — <c>getSignaturesOfStructuredType</c>.</summary>
    internal List<ISignature> GetSignaturesOfStructuredType(IType type, SignatureKind kind)
    {
        if ((type.Flags & TypeFlags.StructuredType) != 0)
        {
            var resolved = ResolveStructuredTypeMembers((UnionOrIntersectionType)type);
            return kind == SignatureKind.Call ? resolved.CallSignatures ?? [] : resolved.ConstructSignatures ?? [];
        }

        return [];
    }

    /// <summary>
    /// Return the signatures of the given kind in the given type. Creates synthetic union signatures when necessary and
    /// maps primitive types and type parameters are to their apparent types.
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:15948-15964 — <c>getSignaturesOfType</c>.</remarks>
    internal List<ISignature> GetSignaturesOfType(IType? type, SignatureKind kind)
    {
        var result = GetSignaturesOfStructuredType(GetReducedApparentType(type!), kind);
        if (kind == SignatureKind.Call && Core.Length(result) == 0 && (type!.Flags & TypeFlags.Union) != 0)
        {
            var unionType = (UnionType)type;
            if (unionType.ArrayFallbackSignatures is not null)
            {
                return unionType.ArrayFallbackSignatures;
            }

            // If the union is all different instantiations of a member of the global array type...
            // PORT-NOTE: TS writes this as `(memberName ? memberName === t.symbol.escapedName : (memberName = …, true))`
            // inside the `some`-style predicate; C# has no common type between `(string, bool)` and `bool`, so the same
            // control flow is a block-bodied lambda.
            string? memberName = null;
            if (EveryType(type, t =>
            {
                if (t.Symbol?.Parent is null || !IsArrayOrTupleSymbol(t.Symbol.Parent)) return false;
                if (memberName is null)
                {
                    memberName = t.Symbol.EscapedName;
                    return true;
                }

                return memberName == t.Symbol.EscapedName;
            }))
            {
                // Transform the type from `(A[] | B[])["member"]` to `(A | B)[]["member"]` (since we pretend array is covariant anyway)
                var arrayArg = MapType(type, t => GetMappedType(_globalArrayType.TypeParameters![0], (TypeMapper?)((AnonymousType)t).Mapper!));
                var arrayType = CreateArrayType(arrayArg!, SomeType(type, (IType t) => IsReadonlyArraySymbol(t.Symbol?.Parent)));
                unionType.ArrayFallbackSignatures = GetSignaturesOfType(GetTypeOfPropertyOfType(arrayType, memberName!)!, kind);
                return unionType.ArrayFallbackSignatures;
            }

            unionType.ArrayFallbackSignatures = result;
        }

        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:16020-16026 — <c>getIndexInfosOfStructuredType</c>.</summary>
    internal List<IIndexInfo> GetIndexInfosOfStructuredType(IType type)
    {
        if ((type.Flags & TypeFlags.StructuredType) != 0)
        {
            var resolved = ResolveStructuredTypeMembers((UnionOrIntersectionType)type);
            return resolved.IndexInfos ?? [];
        }

        return [];
    }

    /// <summary>ported from: src/compiler/checker.ts:16028-16030 — <c>getIndexInfosOfType</c>.</summary>
    internal List<IIndexInfo> GetIndexInfosOfType(IType? type)
        => GetIndexInfosOfStructuredType(GetReducedApparentType(type!));

    // PORT-NOTE: `findIndexInfo` (checker.ts:15981) is declared by M6Generics' `Checker/Checker.Support.cs`; this file
    // only calls it (from `resolveObjectTypeMembers`).

    // ---------------------------------------------------------------------------------------------
    // The signatures of a declaration (checker.ts:16366-16408)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:16366-16408 — <c>getSignaturesOfSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>(decl as FunctionLikeDeclaration).body</c> re-views the <c>SignatureDeclaration</c>
    /// union (the shape <c>isFunctionLike</c> narrows to), and its signature-only members — <c>CallSignature</c>,
    /// <c>ConstructSignature</c>, <c>MethodSignature</c>, <c>IndexSignature</c>, the function/constructor type nodes,
    /// the JSDoc signatures — carry no <c>body</c> slot, so the reference observes <c>undefined</c> for them. The port's
    /// <see cref="IFunctionLikeDeclarationBase"/> is implemented by exactly the members that do carry the slot, so the
    /// test is a match on that face; the reference's cast was erased and would throw for the other kinds.
    /// </remarks>
    internal List<ISignature> GetSignaturesOfSymbol(ISymbol? symbol)
    {
        if (symbol is null || symbol.Declarations is null) return [];
        var result = new List<ISignature>();
        for (var i = 0; i < symbol.Declarations.Count; i++)
        {
            var decl = symbol.Declarations[i];
            if (!Utilities.IsFunctionLike((Node)decl)) continue;
            // Don't include signature if node is the implementation of an overloaded function. A node is considered
            // an implementation node if it has a body and the previous node is of the same kind and immediately
            // precedes the implementation node (i.e. has the same parent and ends where the implementation starts).
            // Reference (checker.ts:16373): `(decl as FunctionLikeDeclaration).body` — only the function-like kinds of
            // the narrowed union own the slot; the rest answer `undefined`.
            if (i > 0 && (decl as IFunctionLikeDeclarationBase)?.Body is not null)
            {
                var previous = symbol.Declarations[i - 1];
                if (ReferenceEquals(((Node)decl).Parent, ((Node)previous).Parent) && ((Node)decl).Kind == ((Node)previous).Kind && ((Node)decl).Pos == ((Node)previous).End)
                {
                    continue;
                }
            }

            if (Utilities.IsInJSFile((Node)decl) && ((IJSDocContainer)decl).JsDoc is not null)
            {
                var tags = Utilities.GetJSDocOverloadTags((Node)decl);
                if (Core.Length(tags) != 0)
                {
                    foreach (var tag in tags)
                    {
                        var jsDocSignature = tag.TypeExpression!;
                        if (jsDocSignature.Type is null && !NodeTests.IsConstructorDeclaration((Node)decl))
                        {
                            ReportImplicitAny(jsDocSignature, _anyType);
                        }

                        result.Add(GetSignatureFromDeclaration((Node)jsDocSignature));
                    }

                    continue;
                }
            }

            // If this is a function or method declaration, get the signature from the @type tag for the sake of optional parameters.
            // Exclude contextually-typed kinds because we already apply the @type tag to the context, plus applying it here to the initializer would supress checks that the two are compatible.
            result.Add(
                (!Utilities.IsFunctionExpressionOrArrowFunction((Node)decl)
                    && !Utilities.IsObjectLiteralMethod((Node)decl)
                    && GetSignatureOfTypeTag((Node)decl) is { } tagSignature) ? tagSignature : GetSignatureFromDeclaration((Node)decl));
        }

        return result;
    }

    // ---------------------------------------------------------------------------------------------
    // The return type of a signature (checker.ts:16473-16543)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:16473-16516 — <c>getReturnTypeOfSignature</c>.</summary>
    internal IType GetReturnTypeOfSignature(ISignature? signature)
    {
        var sig = signature!;
        if (sig.ResolvedReturnType is null)
        {
            if (!PushTypeResolution(sig, TypeSystemPropertyName.ResolvedReturnType))
            {
                return _errorType;
            }

            // PORT-NOTE: TS passes `signature.mapper` (a `TypeMapper`) straight through; the generated `Signature.mapper`
            // slot is `object?` because `TypeMapper` is a function-union alias, so the cast is written here.
            var type = sig.Target is not null ? InstantiateType(GetReturnTypeOfSignature(sig.Target), (TypeMapper?)sig.Mapper)
                : sig.CompositeSignatures is not null ? InstantiateType(GetUnionOrIntersectionType(Core.Map(sig.CompositeSignatures, (ISignature s, int _) => GetReturnTypeOfSignature(s))!, sig.CompositeKind!.Value, UnionReduction.Subtype), (TypeMapper?)sig.Mapper)
                : GetReturnTypeFromAnnotation(sig.Declaration!)
                // Reference (checker.ts:16477): `nodeIsMissing((signature.declaration as FunctionLikeDeclaration).body)
                // ? anyType : getReturnTypeFromBody(signature.declaration as FunctionLikeDeclaration)` — the cast is a
                // no-op re-view, so a declaration kind without a `body` slot (MethodSignature, ConstructSignature, the
                // type nodes, a JSDoc signature) reads `undefined` there and is "missing"; the two calls below stay on
                // the declaration itself, exactly as the reference's erased cast hands the same node on.
                ?? (Utilities.NodeIsMissing((sig.Declaration as IFunctionLikeDeclarationBase)?.Body) ? _anyType : GetReturnTypeFromBody(sig.Declaration!));
            if ((sig.Flags & SignatureFlags.IsInnerCallChain) != 0)
            {
                type = AddOptionalTypeMarker(type!);
            }
            else if ((sig.Flags & SignatureFlags.IsOuterCallChain) != 0)
            {
                type = GetOptionalType(type!);
            }

            if (!PopTypeResolution())
            {
                if (sig.Declaration is not null)
                {
                    var typeNode = Utilities.GetEffectiveReturnTypeNode(sig.Declaration);
                    if (typeNode is not null)
                    {
                        // PORT-NOTE: `typeNode` is the `ITypeNode` face and `Error` takes `Node?`; TS's structural typing
                        // performs this upcast silently (Main's interface→Node finding), C# needs the cast.
                        Error((Node)typeNode, Diagnostics.Return_type_annotation_circularly_references_itself);
                    }
                    else if (_noImplicitAny)
                    {
                        var declaration = (IDeclaration)sig.Declaration;
                        var name = Utilities.GetNameOfDeclaration((Node)declaration);
                        if (name is not null)
                        {
                            Error((Node?)name, Diagnostics._0_implicitly_has_return_type_any_because_it_does_not_have_a_return_type_annotation_and_is_referenced_directly_or_indirectly_in_one_of_its_return_expressions, Utilities.DeclarationNameToString((Node)name));
                        }
                        else
                        {
                            Error((Node)declaration, Diagnostics.Function_implicitly_has_return_type_any_because_it_does_not_have_a_return_type_annotation_and_is_referenced_directly_or_indirectly_in_one_of_its_return_expressions);
                        }
                    }
                }

                type = _anyType;
            }

            // PORT-NOTE: the `!` here and on `links.DeclaredType` below is for the value of an *unresolved* call: until
            // `InstantiateType`/`GetTypeFromTypeNode` land (M6Generics) the conditional's type is an error type and the
            // compiler cannot see that it is never null.
            sig.ResolvedReturnType ??= type!;
        }

        return sig.ResolvedReturnType!;
    }

    /// <summary>ported from: src/compiler/checker.ts:16518-16543 — <c>getReturnTypeFromAnnotation</c>.</summary>
    internal IType GetReturnTypeFromAnnotation(Node declaration)
    {
        if (declaration.Kind == SyntaxKind.Constructor)
        {
            // PORT-NOTE: the reference's `declaration.parent as ClassDeclaration` (checker.ts:16513) is an unchecked
            // assertion; a constructor's parent is a class declaration *or* a class expression, and only `.symbol` is
            // read, so the cast names <see cref="IClassLikeDeclarationBase"/> — the shape both kinds share.
            return GetDeclaredTypeOfClassOrInterface(GetMergedSymbol(((IClassLikeDeclarationBase)declaration.Parent!).Symbol)!);
        }

        var typeNode = Utilities.GetEffectiveReturnTypeNode(declaration);
        if (NodeTests.IsJSDocSignature(declaration))
        {
            var root = Utilities.GetJSDocRoot(declaration);
            if (root is not null && NodeTests.IsConstructorDeclaration(root.Parent!) && typeNode is null)
            {
                // PORT-NOTE: same unchecked `as ClassDeclaration` (checker.ts:16519) as above; a JSDoc-rooted
                // constructor's parent is a class declaration or class expression.
                return GetDeclaredTypeOfClassOrInterface(GetMergedSymbol(((IClassLikeDeclarationBase)root.Parent!.Parent!).Symbol)!);
            }
        }

        if (Utilities.IsJSDocConstructSignature(declaration))
        {
            return GetTypeFromTypeNode(((ParameterDeclaration)((ISignatureDeclarationBase)declaration).Parameters![0]).Type!);
        }

        if (typeNode is not null)
        {
            return GetTypeFromTypeNode(typeNode);
        }

        if (declaration.Kind == SyntaxKind.GetAccessor && HasBindableName((IDeclaration)declaration))
        {
            var jsDocType = Utilities.IsInJSFile(declaration) ? GetTypeForDeclarationFromJSDocComment(declaration) : null;
            if (jsDocType is not null)
            {
                return jsDocType;
            }

            // PORT-NOTE: `getDeclarationOfKind` is a utilities.ts member (utilities.ts:10694-10702); its one declaration sits
            // in a `Utilities` partial (Checker/Checker.Support.cs:1858), so the call is qualified.
            var setter = Utilities.GetDeclarationOfKind<Node>(GetSymbolOfDeclaration((IDeclaration)declaration), SyntaxKind.SetAccessor);
            var setterType = GetAnnotatedAccessorType(setter);
            if (setterType is not null)
            {
                return setterType;
            }
        }

        return GetReturnTypeOfTypeTag(declaration)!;
    }

    // ---------------------------------------------------------------------------------------------
    // Apparent types (checker.ts:15542-15556)
    // ---------------------------------------------------------------------------------------------

    // PORT-NOTE: `getApparentTypeOfMappedType` (15503), `getResolvedApparentTypeOfMappedType` (15507) and
    // `getApparentTypeOfIntersectionType` (15448) are declared by M6Generics' `Checker/Checker.Support.cs` (their
    // parameters are the `IMappedType`/`IIntersectionType` faces), so this file only calls them.

    /// <summary>
    /// For a type parameter, return the base constraint of the type parameter. For the string, number,
    /// boolean, and symbol primitive types, return the corresponding object types. Otherwise return the
    /// type itself.
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:15542-15556 — <c>getApparentType</c>.</remarks>
    internal IType GetApparentType(IType? type)
    {
        var t = (type!.Flags & TypeFlags.Instantiable) != 0 ? GetBaseConstraintOfType(type) ?? _unknownType : type;
        var objectFlags = GetObjectFlags(t);
        return (objectFlags & ObjectFlags.Mapped) != 0 ? GetApparentTypeOfMappedType((IMappedType)t)
            : (objectFlags & ObjectFlags.Reference) != 0 && !ReferenceEquals(t, type) ? GetTypeWithThisArgument(t, type)
            : (t.Flags & TypeFlags.Intersection) != 0 ? GetApparentTypeOfIntersectionType((IIntersectionType)t, type)
            : (t.Flags & TypeFlags.StringLike) != 0 ? _globalStringType
            : (t.Flags & TypeFlags.NumberLike) != 0 ? _globalNumberType
            : (t.Flags & TypeFlags.BigIntLike) != 0 ? GetGlobalBigIntType()
            : (t.Flags & TypeFlags.BooleanLike) != 0 ? _globalBooleanType
            : (t.Flags & TypeFlags.ESSymbolLike) != 0 ? GetGlobalESSymbolType()
            : (t.Flags & TypeFlags.NonPrimitive) != 0 ? _emptyObjectType
            : (t.Flags & TypeFlags.Index) != 0 ? _stringNumberSymbolType
            : (t.Flags & TypeFlags.Unknown) != 0 && !_strictNullChecks ? _emptyObjectType
            : t;
    }

    // ---------------------------------------------------------------------------------------------
    // The type of a symbol (checker.ts:12839-13000)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:12839-12842 — <c>getTypeOfEnumMember</c>.</summary>
    internal IType GetTypeOfEnumMember(ISymbol symbol)
    {
        var links = GetSymbolLinks(symbol);
        return links.Type ??= GetDeclaredTypeOfEnumMember(symbol);
    }

    /// <summary>ported from: src/compiler/checker.ts:12873-12876 — <c>getTypeOfInstantiatedSymbol</c>.</summary>
    internal IType GetTypeOfInstantiatedSymbol(ISymbol symbol)
    {
        var links = GetSymbolLinks(symbol);
        return links.Type = links.Type ?? InstantiateType(GetTypeOfSymbol(links.Target!), (TypeMapper?)links.Mapper)!;
    }

    /// <summary>ported from: src/compiler/checker.ts:12878-12881 — <c>getWriteTypeOfInstantiatedSymbol</c>.</summary>
    internal IType GetWriteTypeOfInstantiatedSymbol(ISymbol symbol)
    {
        var links = GetSymbolLinks(symbol);
        return links.WriteType = links.WriteType ?? InstantiateType(GetWriteTypeOfSymbol(links.Target!), (TypeMapper?)links.Mapper)!;
    }

    /// <summary>ported from: src/compiler/checker.ts:12908-12916 — <c>getTypeOfSymbolWithDeferredType</c>.</summary>
    internal IType GetTypeOfSymbolWithDeferredType(ISymbol symbol)
    {
        var links = GetSymbolLinks(symbol);
        if (links.Type is null)
        {
            Debug.AssertIsDefined(links.DeferralParent);
            Debug.AssertIsDefined(links.DeferralConstituents);
            links.Type = (links.DeferralParent!.Flags & TypeFlags.Union) != 0 ? GetUnionType(links.DeferralConstituents!) : GetIntersectionType(links.DeferralConstituents!);
        }

        return links.Type!;
    }

    /// <summary>ported from: src/compiler/checker.ts:12918-12927 — <c>getWriteTypeOfSymbolWithDeferredType</c>.</summary>
    internal IType? GetWriteTypeOfSymbolWithDeferredType(ISymbol symbol)
    {
        var links = GetSymbolLinks(symbol);
        if (links.WriteType is null && links.DeferralWriteConstituents is not null)
        {
            Debug.AssertIsDefined(links.DeferralParent);
            Debug.AssertIsDefined(links.DeferralConstituents);
            links.WriteType = (links.DeferralParent!.Flags & TypeFlags.Union) != 0 ? GetUnionType(links.DeferralWriteConstituents) : GetIntersectionType(links.DeferralWriteConstituents);
        }

        return links.WriteType;
    }

    /// <summary>
    /// Distinct write types come only from set accessors, but synthetic union and intersection
    /// properties deriving from set accessors will either pre-compute or defer the union or
    /// intersection of the writeTypes of their constituents.
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:12933-12949 — <c>getWriteTypeOfSymbol</c>.</remarks>
    internal IType GetWriteTypeOfSymbol(ISymbol symbol)
    {
        var checkFlags = GetCheckFlags(symbol);
        if ((checkFlags & CheckFlags.SyntheticProperty) != 0)
        {
            return (checkFlags & CheckFlags.DeferredType) != 0
                ? GetWriteTypeOfSymbolWithDeferredType(symbol) ?? GetTypeOfSymbolWithDeferredType(symbol)
                // NOTE: cast to TransientSymbol should be safe because only TransientSymbols can have CheckFlags.SyntheticProperty
                : ((ITransientSymbol)symbol).Links!.WriteType ?? ((ITransientSymbol)symbol).Links!.Type!;
        }

        if ((symbol.Flags & SymbolFlags.Property) != 0)
        {
            return RemoveMissingType(GetTypeOfSymbol(symbol), (symbol.Flags & SymbolFlags.Optional) != 0);
        }

        if ((symbol.Flags & SymbolFlags.Accessor) != 0)
        {
            return (checkFlags & CheckFlags.Instantiated) != 0
                ? GetWriteTypeOfInstantiatedSymbol(symbol)
                : GetWriteTypeOfAccessors(symbol);
        }

        return GetTypeOfSymbol(symbol);
    }

    /// <summary>ported from: src/compiler/checker.ts:12952-12982 — <c>getTypeOfSymbol</c>.</summary>
    internal IType GetTypeOfSymbol(ISymbol? symbol)
    {
        var checkFlags = GetCheckFlags(symbol!);
        if ((checkFlags & CheckFlags.DeferredType) != 0)
        {
            return GetTypeOfSymbolWithDeferredType(symbol!);
        }

        if ((checkFlags & CheckFlags.Instantiated) != 0)
        {
            return GetTypeOfInstantiatedSymbol(symbol!);
        }

        if ((checkFlags & CheckFlags.Mapped) != 0)
        {
            return GetTypeOfMappedSymbol((IMappedSymbol)symbol!);
        }

        if ((checkFlags & CheckFlags.ReverseMapped) != 0)
        {
            return GetTypeOfReverseMappedSymbol((IReverseMappedSymbol)symbol!);
        }

        if ((symbol!.Flags & (SymbolFlags.Variable | SymbolFlags.Property)) != 0)
        {
            return GetTypeOfVariableOrParameterOrProperty(symbol);
        }

        if ((symbol.Flags & (SymbolFlags.Function | SymbolFlags.Method | SymbolFlags.Class | SymbolFlags.Enum | SymbolFlags.ValueModule)) != 0)
        {
            return GetTypeOfFuncClassEnumModule(symbol);
        }

        if ((symbol.Flags & SymbolFlags.EnumMember) != 0)
        {
            return GetTypeOfEnumMember(symbol);
        }

        if ((symbol.Flags & SymbolFlags.Accessor) != 0)
        {
            return GetTypeOfAccessors(symbol);
        }

        if ((symbol.Flags & SymbolFlags.Alias) != 0)
        {
            return GetTypeOfAlias(symbol);
        }

        return _errorType;
    }

    /// <summary>ported from: src/compiler/checker.ts:12984-12986 — <c>getNonMissingTypeOfSymbol</c>.</summary>
    internal IType GetNonMissingTypeOfSymbol(ISymbol symbol)
        => RemoveMissingType(GetTypeOfSymbol(symbol), (symbol.Flags & SymbolFlags.Optional) != 0);

    // ---------------------------------------------------------------------------------------------
    // The declared type of a symbol (checker.ts:13463-13640)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:13463-13498 — <c>getDeclaredTypeOfClassOrInterface</c>.</summary>
    internal InterfaceType GetDeclaredTypeOfClassOrInterface(ISymbol symbol)
    {
        var links = GetSymbolLinks(symbol);
        var originalLinks = links;
        if (links.DeclaredType is null)
        {
            var kind = (symbol.Flags & SymbolFlags.Class) != 0 ? ObjectFlags.Class : ObjectFlags.Interface;
            var merged = MergeJSSymbols(symbol, symbol.ValueDeclaration is not null ? GetAssignedClassSymbol((IDeclaration)symbol.ValueDeclaration) : null);
            if (merged is not null)
            {
                // note:we overwrite links because we just cloned the symbol
                symbol = merged;
                links = GetSymbolLinks(merged);
            }

            var type = (InterfaceType)CreateObjectType(kind, symbol);
            originalLinks.DeclaredType = type;
            links.DeclaredType = type;
            var outerTypeParameters = GetOuterTypeParametersOfClassOrInterface(symbol);
            var localTypeParameters = GetLocalTypeParametersOfClassOrInterfaceOrTypeAlias(symbol);
            // A class or interface is generic if it has type parameters or a "this" type. We always give classes a "this" type
            // because it is not feasible to analyze all members to determine if the "this" type escapes the class (in particular,
            // property types inferred from initializers and method return types inferred from return statements are very hard
            // to exhaustively analyze). We give interfaces a "this" type if we can't definitely determine that they are free of
            // "this" references.
            if (outerTypeParameters is not null || localTypeParameters is not null || kind == ObjectFlags.Class || !IsThislessInterface(symbol))
            {
                type.ObjectFlags |= ObjectFlags.Reference;
                type.TypeParameters = Core.Concatenate(outerTypeParameters, localTypeParameters) is List<ITypeParameter> typeParameters ? typeParameters : [.. outerTypeParameters ?? [], .. localTypeParameters ?? []];
                type.OuterTypeParameters = outerTypeParameters is null ? null : [.. outerTypeParameters];
                type.LocalTypeParameters = localTypeParameters is null ? null : [.. localTypeParameters];
                ((IGenericType)type).Instantiations = new Dictionary<string, ITypeReference>(StringComparer.Ordinal);
                ((IGenericType)type).Instantiations![GetTypeListId(TypeListOfParameters(type.TypeParameters))] = type;
                ((IGenericType)type).Target = (IGenericType)type;
                ((IGenericType)type).ResolvedTypeArguments = [.. type.TypeParameters!.Select(parameter => (IType)parameter)];
                type.ThisType = CreateTypeParameter(symbol);
                type.ThisType.IsThisType = true;
                type.ThisType.Constraint = type;
            }
        }

        return (InterfaceType)links.DeclaredType!;
    }

    /// <summary>ported from: src/compiler/checker.ts:13500-13538 — <c>getDeclaredTypeOfTypeAlias</c>.</summary>
    internal IType GetDeclaredTypeOfTypeAlias(ISymbol symbol)
    {
        var links = GetSymbolLinks(symbol);
        if (links.DeclaredType is null)
        {
            // Note that we use the links object as the target here because the symbol object is used as the unique
            // identity for resolution of the 'type' property in SymbolLinks.
            if (!PushTypeResolution(symbol, TypeSystemPropertyName.DeclaredType))
            {
                return _errorType;
            }

            var declaration = Debug.CheckDefined(Core.Find(symbol.Declarations, (IDeclaration d, int _) => NodeTests.IsTypeAliasDeclaration((Node)d)), "Type alias symbol with no valid declaration found");
            // PORT-NOTE: TS reads `declaration.typeExpression` off the `JSDocTypeAlias` union; the port names the three
            // tags the predicate accepts and takes each one's own slot (JSDocTypedefTag's is a plain `Node?`).
            var typeNode = Utilities.IsJSDocTypeAlias((Node)declaration!)
                ? (ITypeNode?)(declaration switch
                {
                    JSDocTypedefTag typedef => typedef.TypeExpression,
                    JSDocEnumTag enumTag => enumTag.TypeExpression,
                    JSDocCallbackTag callback => callback.TypeExpression,
                    _ => null,
                })
                : ((TypeAliasDeclaration)declaration!).Type;
            // If typeNode is missing, we will error in checkJSDocTypedefTag.
            var type = typeNode is not null ? GetTypeFromTypeNode(typeNode) : _errorType;
            if (PopTypeResolution())
            {
                var typeParameters = GetLocalTypeParametersOfClassOrInterfaceOrTypeAlias(symbol);
                if (typeParameters is not null)
                {
                    // Initialize the instantiation cache for generic type aliases. The declared type corresponds to
                    // an instantiation of the type alias with the type parameters supplied as type arguments.
                    links.TypeParameters = typeParameters;
                    GetSymbolLinks(symbol).TypeParameters = typeParameters;
                    links.Instantiations = new Dictionary<string, IType>(StringComparer.Ordinal);
                    links.Instantiations[GetTypeListId(TypeListOfParameters(typeParameters))] = type;
                }

                if (ReferenceEquals(type, _intrinsicMarkerType) && symbol.EscapedName == "BuiltinIteratorReturn")
                {
                    type = GetBuiltinIteratorReturnType();
                }
            }
            else
            {
                type = _errorType;
                if (((Node)declaration!).Kind == SyntaxKind.JSDocEnumTag)
                {
                    // PORT-NOTE (A″): printer-gated stringifier arguments are deferred — see Core/Diagnostic.cs's `DiagnosticArgument`.
                    Error((Node?)((JSDocEnumTag)declaration!).TypeExpression!.Type, Diagnostics.Type_alias_0_circularly_references_itself, Diagnostic.DeferArg(() => SymbolToString(symbol)));
                }
                else
                {
                    var errorNode = (Node)declaration!;
                    if (Utilities.IsNamedDeclaration(errorNode))
                    {
                        errorNode = Utilities.GetNameOfDeclaration(errorNode) as Node ?? errorNode;
                    }

                    Error(errorNode, Diagnostics.Type_alias_0_circularly_references_itself, Diagnostic.DeferArg(() => SymbolToString(symbol)));
                }
            }

            links.DeclaredType ??= type!;
        }

        return links.DeclaredType!;
    }

    /// <summary>ported from: src/compiler/checker.ts:13540-13542 — <c>getBaseTypeOfEnumLikeType</c>.</summary>
    internal IType GetBaseTypeOfEnumLikeType(IType type)
        => (type.Flags & TypeFlags.EnumLike) != 0 && (type.Symbol!.Flags & SymbolFlags.EnumMember) != 0 ? GetDeclaredTypeOfSymbol(GetParentOfSymbol(type.Symbol)!) : type;

    /// <summary>ported from: src/compiler/checker.ts:13544-13577 — <c>getDeclaredTypeOfEnum</c>.</summary>
    internal IType GetDeclaredTypeOfEnum(ISymbol symbol)
    {
        var links = GetSymbolLinks(symbol);
        if (links.DeclaredType is null)
        {
            var memberTypeList = new List<IType>();
            if (symbol.Declarations is not null)
            {
                foreach (var declaration in symbol.Declarations)
                {
                    if (((Node)declaration).Kind == SyntaxKind.EnumDeclaration)
                    {
                        foreach (var member in ((EnumDeclaration)declaration).Members!)
                        {
                            if (HasBindableName((IDeclaration)member))
                            {
                                var memberSymbol = GetSymbolOfDeclaration((IDeclaration)member);
                                var value = GetEnumMemberValue(member).Value;
                                var memberType = GetFreshTypeOfLiteralType(
                                    value is not null
                                        ? GetEnumLiteralType(value, Utilities.GetSymbolId(symbol), memberSymbol)
                                        : CreateComputedEnumType(memberSymbol));
                                GetSymbolLinks(memberSymbol).DeclaredType = memberType;
                                memberTypeList.Add(GetRegularTypeOfLiteralType(memberType));
                            }
                        }
                    }
                }
            }

            var enumType = memberTypeList.Count != 0
                ? GetUnionType(memberTypeList, UnionReduction.Literal, symbol, /*aliasTypeArguments*/ null)
                : CreateComputedEnumType(symbol);
            if ((enumType.Flags & TypeFlags.Union) != 0)
            {
                enumType.Flags |= TypeFlags.EnumLiteral;
                enumType.Symbol = symbol;
            }

            links.DeclaredType = enumType;
        }

        return links.DeclaredType;
    }

    /// <summary>ported from: src/compiler/checker.ts:13579-13587 — <c>createComputedEnumType</c>.</summary>
    internal IType CreateComputedEnumType(ISymbol symbol)
    {
        var regularType = (EnumType)CreateTypeWithSymbol(TypeFlags.Enum, symbol);
        var freshType = (EnumType)CreateTypeWithSymbol(TypeFlags.Enum, symbol);
        regularType.RegularType = regularType;
        regularType.FreshType = freshType;
        freshType.RegularType = regularType;
        freshType.FreshType = freshType;
        return regularType;
    }

    /// <summary>ported from: src/compiler/checker.ts:13589-13598 — <c>getDeclaredTypeOfEnumMember</c>.</summary>
    internal IType GetDeclaredTypeOfEnumMember(ISymbol symbol)
    {
        var links = GetSymbolLinks(symbol);
        if (links.DeclaredType is null)
        {
            var enumType = GetDeclaredTypeOfEnum(GetParentOfSymbol(symbol)!);
            links.DeclaredType ??= enumType;
        }

        return links.DeclaredType;
    }

    /// <summary>ported from: src/compiler/checker.ts:13600-13603 — <c>getDeclaredTypeOfTypeParameter</c>.</summary>
    internal ITypeParameter GetDeclaredTypeOfTypeParameter(ISymbol symbol)
    {
        var links = GetSymbolLinks(symbol);
        return (ITypeParameter)(links.DeclaredType ??= CreateTypeParameter(symbol));
    }

    /// <summary>ported from: src/compiler/checker.ts:13605-13608 — <c>getDeclaredTypeOfAlias</c>.</summary>
    internal IType GetDeclaredTypeOfAlias(ISymbol symbol)
    {
        var links = GetSymbolLinks(symbol);
        return links.DeclaredType ??= GetDeclaredTypeOfSymbol(ResolveAlias(symbol));
    }

    /// <summary>ported from: src/compiler/checker.ts:13610-13612 — <c>getDeclaredTypeOfSymbol</c>.</summary>
    internal IType GetDeclaredTypeOfSymbol(ISymbol? symbol)
        => TryGetDeclaredTypeOfSymbol(symbol!) ?? _errorType;

    /// <summary>ported from: src/compiler/checker.ts:13614-13638 — <c>tryGetDeclaredTypeOfSymbol</c>.</summary>
    internal IType? TryGetDeclaredTypeOfSymbol(ISymbol symbol)
    {
        if ((symbol.Flags & (SymbolFlags.Class | SymbolFlags.Interface)) != 0)
        {
            return GetDeclaredTypeOfClassOrInterface(symbol);
        }

        if ((symbol.Flags & SymbolFlags.TypeAlias) != 0)
        {
            return GetDeclaredTypeOfTypeAlias(symbol);
        }

        if ((symbol.Flags & SymbolFlags.TypeParameter) != 0)
        {
            return GetDeclaredTypeOfTypeParameter(symbol);
        }

        if ((symbol.Flags & SymbolFlags.Enum) != 0)
        {
            return GetDeclaredTypeOfEnum(symbol);
        }

        if ((symbol.Flags & SymbolFlags.EnumMember) != 0)
        {
            return GetDeclaredTypeOfEnumMember(symbol);
        }

        if ((symbol.Flags & SymbolFlags.Alias) != 0)
        {
            return GetDeclaredTypeOfAlias(symbol);
        }

        return null;
    }
}
