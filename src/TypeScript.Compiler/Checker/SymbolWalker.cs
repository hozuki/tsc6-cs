namespace TypeScript.Compiler;

// ported from: src/compiler/symbolWalker.ts
/// <summary>
/// The symbol walker <c>ITypeChecker.GetSymbolWalker</c> (checker.ts:1819-1830) hands out: one walk of a type or a
/// symbol, plus the types and the symbols that walk reached.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS states this as <c>createGetSymbolWalker(...)</c> returning the closure factory <c>getSymbolWalker</c>
/// (symbolWalker.ts:30-44); the port keeps that call shape (<c>Func&lt;Func&lt;ISymbol, bool&gt;, ISymbolWalker&gt;</c>)
/// because the caller applies the factory to <c>accept</c> in one expression. C# has no free functions over shared
/// state, so the closure's ten captured readers and its nested <c>visit*</c> functions become the fields and methods of
/// <see cref="SymbolWalker"/> — the treatment <c>NodeFactory</c> documents for TS's factory object literal.
/// </para>
/// <para>
/// PORT-NOTE: <c>visitedTypes</c>/<c>visitedSymbols</c> are sparse arrays indexed by the type/symbol id in the
/// reference, and <c>getOwnValues</c> projects them for <c>walkType</c>/<c>walkSymbol</c>. The port keeps
/// <c>List&lt;IType?&gt;</c>/<c>List&lt;ISymbol?&gt;</c> with the id as index — the same ascending-id order JS gives
/// integer-keyed properties — and projects the non-null entries at the end of a walk. <c>clear(array)</c> is
/// <see cref="Core.Clear{T}(IList{T})"/>, the port's member for it.
/// </para>
/// <para>
/// PORT-NOTE: TS's result is the anonymous object <c>{ visitedTypes, visitedSymbols }</c>, which the generated
/// <c>ISymbolWalker</c> states as <c>object?</c> (there is no common class for the shape); the port names what it
/// produces <see cref="SymbolWalkerResult"/>, the precedent <c>Checker.SerializedTypeEntry</c> set for a model object
/// literal.
/// </para>
/// <para>
/// PORT-NOTE: <c>(d as any).type</c> in <c>visitSymbol</c> (symbolWalker.ts:207) is a duck-typed read of a
/// declaration's type annotation. The port's model has one class per node kind and no "has a type" face, so
/// <see cref="SymbolWalker.GetTypeAnnotation"/> names every kind that declares a <c>Type</c> slot — exactly the set the
/// reference's unchecked read can see.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    /// <summary>ported from: src/compiler/symbolWalker.ts:30-41 — <c>createGetSymbolWalker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the ten parameters are the checker members the reference passes at checker.ts:1819-1830, in its order,
    /// and each is typed as the port's own member of that name so the call site can pass them as method groups the way
    /// the reference passes functions. TS's default argument (<c>accept: (symbol) =&gt; boolean = () =&gt; true</c>,
    /// symbolWalker.ts:44) is applied by the returned factory, because in the port <c>accept</c> arrives with the walk
    /// rather than with the closure.
    /// </remarks>
    internal static Func<Func<ISymbol, bool>?, ISymbolWalker> CreateGetSymbolWalker(
        Func<ISignature, IType> getRestTypeOfSignature,
        Func<ISignature, ITypePredicateBase?> getTypePredicateOfSignature,
        Func<ISignature?, IType> getReturnTypeOfSignature,
        Func<IInterfaceType, List<IType>> getBaseTypes,
        Func<UnionOrIntersectionType, UnionOrIntersectionType> resolveStructuredTypeMembers,
        Func<ISymbol?, IType> getTypeOfSymbol,
        Func<Identifier, ISymbol> getResolvedSymbol,
        Func<ITypeParameter, IType?> getConstraintOfTypeParameter,
        Func<Node, Identifier> getFirstIdentifier,
        Func<ITypeReference, List<IType>> getTypeArguments)
        => accept => new SymbolWalker(
            accept ?? AlwaysAccept,
            getRestTypeOfSignature,
            getTypePredicateOfSignature,
            getReturnTypeOfSignature,
            getBaseTypes,
            resolveStructuredTypeMembers,
            getTypeOfSymbol,
            getResolvedSymbol,
            getConstraintOfTypeParameter,
            getFirstIdentifier,
            getTypeArguments);

    /// <summary>The reference's <c>accept</c> default argument (symbolWalker.ts:44 — <c>() =&gt; true</c>).</summary>
    private static bool AlwaysAccept(ISymbol symbol) => true;
}

/// <summary>ported from: src/compiler/symbolWalker.ts:44-215 — <c>getSymbolWalker</c> and its nested functions.</summary>
internal sealed class SymbolWalker : ISymbolWalker
{
    private readonly Func<ISymbol, bool> _accept;
    private readonly Func<ISignature, IType> _getRestTypeOfSignature;
    private readonly Func<ISignature, ITypePredicateBase?> _getTypePredicateOfSignature;
    private readonly Func<ISignature?, IType> _getReturnTypeOfSignature;
    private readonly Func<IInterfaceType, List<IType>> _getBaseTypes;
    private readonly Func<UnionOrIntersectionType, UnionOrIntersectionType> _resolveStructuredTypeMembers;
    private readonly Func<ISymbol?, IType> _getTypeOfSymbol;
    private readonly Func<Identifier, ISymbol> _getResolvedSymbol;
    private readonly Func<ITypeParameter, IType?> _getConstraintOfTypeParameter;
    private readonly Func<Node, Identifier> _getFirstIdentifier;
    private readonly Func<ITypeReference, List<IType>> _getTypeArguments;

    /// <summary>The reference's <c>visitedTypes</c> (symbolWalker.ts:45): a sparse array from id to type.</summary>
    private readonly List<IType?> _visitedTypes = [];

    /// <summary>The reference's <c>visitedSymbols</c> (symbolWalker.ts:46): a sparse array from id to symbol.</summary>
    private readonly List<ISymbol?> _visitedSymbols = [];

    /// <summary>Fills the closure state <c>getSymbolWalker</c> captured (symbolWalker.ts:44-46).</summary>
    public SymbolWalker(
        Func<ISymbol, bool> accept,
        Func<ISignature, IType> getRestTypeOfSignature,
        Func<ISignature, ITypePredicateBase?> getTypePredicateOfSignature,
        Func<ISignature?, IType> getReturnTypeOfSignature,
        Func<IInterfaceType, List<IType>> getBaseTypes,
        Func<UnionOrIntersectionType, UnionOrIntersectionType> resolveStructuredTypeMembers,
        Func<ISymbol?, IType> getTypeOfSymbol,
        Func<Identifier, ISymbol> getResolvedSymbol,
        Func<ITypeParameter, IType?> getConstraintOfTypeParameter,
        Func<Node, Identifier> getFirstIdentifier,
        Func<ITypeReference, List<IType>> getTypeArguments)
    {
        _accept = accept;
        _getRestTypeOfSignature = getRestTypeOfSignature;
        _getTypePredicateOfSignature = getTypePredicateOfSignature;
        _getReturnTypeOfSignature = getReturnTypeOfSignature;
        _getBaseTypes = getBaseTypes;
        _resolveStructuredTypeMembers = resolveStructuredTypeMembers;
        _getTypeOfSymbol = getTypeOfSymbol;
        _getResolvedSymbol = getResolvedSymbol;
        _getConstraintOfTypeParameter = getConstraintOfTypeParameter;
        _getFirstIdentifier = getFirstIdentifier;
        _getTypeArguments = getTypeArguments;
    }

    /// <summary>ported from: src/compiler/symbolWalker.ts:49-58 — <c>walkType</c>.</summary>
    public object? WalkType(IType? root)
    {
        try
        {
            VisitType(root);
            return new SymbolWalkerResult { VisitedTypes = OwnTypes(), VisitedSymbols = OwnSymbols() };
        }
        finally
        {
            Core.Clear(_visitedTypes);
            Core.Clear(_visitedSymbols);
        }
    }

    /// <summary>ported from: src/compiler/symbolWalker.ts:59-68 — <c>walkSymbol</c>.</summary>
    public object? WalkSymbol(ISymbol? root)
    {
        try
        {
            VisitSymbol(root);
            return new SymbolWalkerResult { VisitedTypes = OwnTypes(), VisitedSymbols = OwnSymbols() };
        }
        finally
        {
            Core.Clear(_visitedTypes);
            Core.Clear(_visitedSymbols);
        }
    }

    /// <summary>The reference's <c>getOwnValues(visitedTypes)</c> (symbolWalker.ts:52).</summary>
    private List<IType> OwnTypes() => [.. _visitedTypes.OfType<IType>()];

    /// <summary>The reference's <c>getOwnValues(visitedSymbols)</c> (symbolWalker.ts:52).</summary>
    private List<ISymbol> OwnSymbols() => [.. _visitedSymbols.OfType<ISymbol>()];

    /// <summary>The reference's <c>visitedTypes[type.id] = type</c> (symbolWalker.ts:79) on a sparse array.</summary>
    private void SetVisitedType(int id, IType type)
    {
        while (_visitedTypes.Count <= id) _visitedTypes.Add(null);
        _visitedTypes[id] = type;
    }

    /// <summary>The reference's <c>visitedSymbols[symbolId] = symbol</c> (symbolWalker.ts:193) on a sparse array.</summary>
    private void SetVisitedSymbol(int id, ISymbol symbol)
    {
        while (_visitedSymbols.Count <= id) _visitedSymbols.Add(null);
        _visitedSymbols[id] = symbol;
    }

    /// <summary>ported from: src/compiler/symbolWalker.ts:71-115 — <c>visitType</c>.</summary>
    private void VisitType(IType? type)
    {
        if (type is null)
        {
            return;
        }

        if (type.Id < _visitedTypes.Count && _visitedTypes[type.Id] is not null)
        {
            return;
        }
        SetVisitedType(type.Id, type);

        // Reuse visitSymbol to visit the type's symbol,
        //  but be sure to bail on recuring into the type if accept declines the symbol.
        var shouldBail = VisitSymbol(type.Symbol);
        if (shouldBail) return;

        // Visit the type's related types, if any
        if ((type.Flags & TypeFlags.Object) != 0)
        {
            var objectType = (ObjectType)type;
            var objectFlags = objectType.ObjectFlags;
            if ((objectFlags & ObjectFlags.Reference) != 0)
            {
                VisitTypeReference((TypeReference)type);
            }
            if ((objectFlags & ObjectFlags.Mapped) != 0)
            {
                VisitMappedType((MappedType)type);
            }
            if ((objectFlags & (ObjectFlags.Class | ObjectFlags.Interface)) != 0)
            {
                VisitInterfaceType((InterfaceType)type);
            }
            if ((objectFlags & (ObjectFlags.Tuple | ObjectFlags.Anonymous)) != 0)
            {
                VisitObjectType(objectType);
            }
        }
        if ((type.Flags & TypeFlags.TypeParameter) != 0)
        {
            VisitTypeParameter((TypeParameter)type);
        }
        if ((type.Flags & TypeFlags.UnionOrIntersection) != 0)
        {
            VisitUnionOrIntersectionType((UnionOrIntersectionType)type);
        }
        if ((type.Flags & TypeFlags.Index) != 0)
        {
            VisitIndexType((IndexType)type);
        }
        if ((type.Flags & TypeFlags.IndexedAccess) != 0)
        {
            VisitIndexedAccessType((IndexedAccessType)type);
        }
    }

    /// <summary>ported from: src/compiler/symbolWalker.ts:117-120 — <c>visitTypeReference</c>.</summary>
    private void VisitTypeReference(TypeReference type)
    {
        VisitType(type.Target);
        foreach (var typeArgument in _getTypeArguments(type))
        {
            VisitType(typeArgument);
        }
    }

    /// <summary>ported from: src/compiler/symbolWalker.ts:122-124 — <c>visitTypeParameter</c>.</summary>
    private void VisitTypeParameter(TypeParameter type) => VisitType(_getConstraintOfTypeParameter(type));

    /// <summary>ported from: src/compiler/symbolWalker.ts:126-128 — <c>visitUnionOrIntersectionType</c>.</summary>
    private void VisitUnionOrIntersectionType(UnionOrIntersectionType type)
    {
        if (type.Types is not null)
        {
            foreach (var constituent in type.Types)
            {
                VisitType(constituent);
            }
        }
    }

    /// <summary>ported from: src/compiler/symbolWalker.ts:130-132 — <c>visitIndexType</c>.</summary>
    private void VisitIndexType(IndexType type) => VisitType(type.Type);

    /// <summary>ported from: src/compiler/symbolWalker.ts:134-138 — <c>visitIndexedAccessType</c>.</summary>
    private void VisitIndexedAccessType(IndexedAccessType type)
    {
        VisitType(type.ObjectType);
        VisitType(type.IndexType);
        VisitType(type.Constraint);
    }

    /// <summary>ported from: src/compiler/symbolWalker.ts:140-145 — <c>visitMappedType</c>.</summary>
    private void VisitMappedType(MappedType type)
    {
        VisitType(type.TypeParameter);
        VisitType(type.ConstraintType);
        VisitType(type.TemplateType);
        VisitType(type.ModifiersType);
    }

    /// <summary>ported from: src/compiler/symbolWalker.ts:147-159 — <c>visitSignature</c>.</summary>
    private void VisitSignature(ISignature signature)
    {
        var typePredicate = _getTypePredicateOfSignature(signature);
        if (typePredicate is not null)
        {
            VisitType(typePredicate.Type);
        }
        if (signature.TypeParameters is not null)
        {
            foreach (var typeParameter in signature.TypeParameters)
            {
                VisitType(typeParameter);
            }
        }

        if (signature.Parameters is not null)
        {
            foreach (var parameter in signature.Parameters)
            {
                VisitSymbol(parameter);
            }
        }
        VisitType(_getRestTypeOfSignature(signature));
        VisitType(_getReturnTypeOfSignature(signature));
    }

    /// <summary>ported from: src/compiler/symbolWalker.ts:161-166 — <c>visitInterfaceType</c>.</summary>
    private void VisitInterfaceType(InterfaceType interfaceT)
    {
        VisitObjectType(interfaceT);
        if (interfaceT.TypeParameters is not null)
        {
            foreach (var typeParameter in interfaceT.TypeParameters)
            {
                VisitType(typeParameter);
            }
        }
        foreach (var baseType in _getBaseTypes(interfaceT))
        {
            VisitType(baseType);
        }
        VisitType(interfaceT.ThisType);
    }

    /// <summary>ported from: src/compiler/symbolWalker.ts:168-183 — <c>visitObjectType</c>.</summary>
    private void VisitObjectType(ObjectType type)
    {
        var resolved = _resolveStructuredTypeMembers(type);
        if (resolved.IndexInfos is not null)
        {
            foreach (var info in resolved.IndexInfos)
            {
                VisitType(info.KeyType);
                VisitType(info.Type);
            }
        }
        if (resolved.CallSignatures is not null)
        {
            foreach (var signature in resolved.CallSignatures)
            {
                VisitSignature(signature);
            }
        }
        if (resolved.ConstructSignatures is not null)
        {
            foreach (var signature in resolved.ConstructSignatures)
            {
                VisitSignature(signature);
            }
        }
        if (resolved.Properties is not null)
        {
            foreach (var property in resolved.Properties)
            {
                VisitSymbol(property);
            }
        }
    }

    /// <summary>ported from: src/compiler/symbolWalker.ts:185-214 — <c>visitSymbol</c>.</summary>
    private bool VisitSymbol(ISymbol? symbol)
    {
        if (symbol is null)
        {
            return false;
        }
        var symbolId = Utilities.GetSymbolId(symbol);
        if (symbolId < _visitedSymbols.Count && _visitedSymbols[symbolId] is not null)
        {
            return false;
        }
        SetVisitedSymbol(symbolId, symbol);
        if (!_accept(symbol))
        {
            return true;
        }
        var t = _getTypeOfSymbol(symbol);
        VisitType(t); // Should handle members on classes and such
        if (symbol.Exports is not null)
        {
            symbol.Exports.ForEach((exportedSymbol, _) => VisitSymbol(exportedSymbol));
        }
        if (symbol.Declarations is not null)
        {
            foreach (var declaration in symbol.Declarations)
            {
                // Type queries are too far resolved when we just visit the symbol's type
                //  (their type resolved directly to the member deeply referenced)
                // So to get the intervening symbols, we need to check if there's a type
                // query node on any of the symbol's declarations and get symbols there
                if (declaration is Node declarationNode && GetTypeAnnotation(declarationNode) is TypeQueryNode query)
                {
                    var entity = _getResolvedSymbol(_getFirstIdentifier(query.ExprName!));
                    VisitSymbol(entity);
                }
            }
        }
        return false;
    }

    /// <summary>The reference's <c>(declaration as any).type</c> (symbolWalker.ts:207).</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads the <c>type</c> property unchecked, because a declaration carries a type annotation only for
    /// some kinds (<c>Declaration</c> has no such member upstream either). The port's model is nominal, so this switch
    /// names every node kind that declares a <c>Type</c> slot in it — the exact set of nodes the reference's read can
    /// see, including the <c>JSDoc*</c> and type-node kinds that can never appear in
    /// <c>Symbol.Declarations</c>.
    /// </remarks>
    private static ITypeNode? GetTypeAnnotation(Node declaration) => declaration switch
    {
        ArrowFunction n => n.Type,
        AsExpression n => n.Type,
        CallSignatureDeclaration n => n.Type,
        ConstructSignatureDeclaration n => n.Type,
        ConstructorDeclaration n => n.Type,
        FunctionDeclaration n => n.Type,
        FunctionExpression n => n.Type,
        FunctionOrConstructorTypeNodeBase n => n.Type,
        GetAccessorDeclaration n => n.Type,
        IndexSignatureDeclaration n => n.Type,
        JSDocFunctionType n => n.Type,
        JSDocNamepathType n => n.Type,
        JSDocNonNullableType n => n.Type,
        JSDocNullableType n => n.Type,
        JSDocOptionalType n => n.Type,
        JSDocTypeExpression n => n.Type,
        JSDocVariadicType n => n.Type,
        MappedTypeNode n => n.Type,
        MethodDeclaration n => n.Type,
        MethodSignature n => n.Type,
        NamedTupleMember n => n.Type,
        OptionalTypeNode n => n.Type,
        ParenthesizedTypeNode n => n.Type,
        ParameterDeclaration n => n.Type,
        PropertyDeclaration n => n.Type,
        PropertySignature n => n.Type,
        RestTypeNode n => n.Type,
        SatisfiesExpression n => n.Type,
        SetAccessorDeclaration n => n.Type,
        TemplateLiteralTypeSpan n => n.Type,
        TypeAliasDeclaration n => n.Type,
        TypeAssertion n => n.Type,
        TypeOperatorNode n => n.Type,
        TypePredicateNode n => n.Type,
        VariableDeclaration n => n.Type,
        _ => null,
    };
}

/// <summary>ported from: src/compiler/symbolWalker.ts:52 — the <c>{ visitedTypes, visitedSymbols }</c> object literal.</summary>
/// <remarks>
/// PORT-NOTE: TS builds this anonymously in <c>walkType</c>/<c>walkSymbol</c>, and the generated
/// <c>ISymbolWalker</c> can only state the slot as <c>object?</c>; <see cref="Checker.CreateGetSymbolWalker"/>'s
/// remark records why the port names it. Each walk builds a fresh instance, and the arrays are the projection the
/// reference makes with <c>getOwnValues</c> — not the walker's live state, which the <c>finally</c> clears.
/// </remarks>
internal sealed class SymbolWalkerResult
{
    /// <summary>ported from: src/compiler/symbolWalker.ts:52 — <c>visitedTypes</c>.</summary>
    public List<IType>? VisitedTypes { get; set; }

    /// <summary>ported from: src/compiler/symbolWalker.ts:52 — <c>visitedSymbols</c>.</summary>
    public List<ISymbol>? VisitedSymbols { get; set; }
}
