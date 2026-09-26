namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:1610-1997 (the `const checker: TypeChecker = { … }` object literal)
// ported from: src/compiler/checker.ts:2410-2412 (the `initializeTypeChecker()` call and `return checker`)
// ported from: src/compiler/program.ts:107, 2684-2685 (the `createTypeChecker(host)` import and call)
/// <summary>
/// The assembly of the checker: <c>createTypeChecker</c> himself, the <c>TypeChecker</c> object literal's members
/// (the <c>ITypeChecker</c> surface), and the object-shape helpers the literal builds inline.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS's <c>createTypeChecker</c> returns an object literal. In the port the very same members are already
/// instance members of this class (each landed by the slice that owns its reference range), so the literal has no
/// runtime counterpart: the class implements the generated <see cref="ITypeChecker"/> and every member below is the
/// explicit interface implementation of one entry of the literal, delegating to the internal member the reference's
/// shorthand entry names. This is §15.15 ruling 4's delivery shape (<c>Checker : ITypeChecker</c>, the same shape
/// <c>NodeFactory</c> was meant to take for <c>INodeFactory</c>).
/// </para>
/// <para>
/// PORT-NOTE: the literal's entries whose body is *not* a bare forward are ported inside their explicit
/// implementation, exactly as the reference writes them: the <c>getParseTreeNode</c> guards, the
/// <c>escapeLeadingUnderscores</c> adaptations, the <c>CheckMode</c> arguments of the two
/// <c>getResolvedSignature</c> wrappers, and the two members whose body is a whole expression
/// (<c>getAnyAsyncIterableType</c>, <c>getSuggestionDiagnostics</c>, <c>runWithCancellationToken</c>).
/// </para>
/// <para>
/// PORT-NOTE: three entries name something the port has not landed, so they are plain cross-slice references and are
/// listed in the slice report rather than stubbed: <c>createGetSymbolWalker</c> (<c>symbolWalker.ts</c>, 198 lines, not
/// ported in M6), <c>getTypeArgumentsForResolvedSignature</c>'s neighbours (landed in <c>Checker.Initialize.cs</c>),
/// and <c>Utilities.IsPropertyAccessOrQualifiedNameOrImportTypeNode</c> (a <c>utilities.ts</c> predicate with no
/// declaration yet).
/// </para>
/// <para>
/// PORT-NOTE: <c>ITypeChecker.GetIndexInfosOfIndexSymbol</c> is the one member of the generated interface that cannot
/// be implemented: the generator turned the reference's function
/// <c>(indexSymbol: Symbol, siblingSymbols?: Symbol[]) =&gt; IndexInfo[]</c> (types.ts:5083) into the property
/// <c>List&lt;Func&lt;ISymbol, List&lt;ISymbol&gt;, IIndexInfo&gt;&gt;?</c> (<c>Model/NodeInterfaces.cs:3526</c>). A
/// generator defect, reported to the generator owner; until it is fixed the two accessors throw §15.15 ruling 1's
/// loud, named failure and <c>CheckerCreateTypeCheckerTests</c> pins this member as the exact difference between the
/// interface and the class.
/// </para>
/// </remarks>
internal sealed partial class Checker : ITypeChecker
{
    /// <summary>
    /// ported from: src/compiler/checker.ts:1486-2412 — <c>createTypeChecker</c>.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: the reference's body is, in execution order: the option-derived state (already this class's
    /// constructor), the <c>TypeChecker</c> literal's definition (the members of this class), the state block at
    /// :2046-2410 and finally <c>initializeTypeChecker()</c> at :2410. <c>InitializeState</c> is that state block (it
    /// is declared in <c>Checker/Checker.Initialize.cs</c> by the slice that ports it) and
    /// <c>InitializeTypeChecker</c> is the reference's last call.
    /// <para>
    /// The signature is pinned: <c>Program.GetTypeChecker</c> (<c>Program/Program.Graph.cs:196</c>) already calls
    /// <c>Checker.CreateTypeChecker(this)</c>.
    /// </para>
    /// </remarks>
    internal static ITypeChecker CreateTypeChecker(ITypeCheckerHost host)
    {
        var checker = new Checker(host);
        checker.InitializeState();
        checker.InitializeTypeChecker();
        return checker;
    }

    /// <summary>ported from: src/compiler/checker.ts:1616-1621 — <c>getRelationCacheSizes</c>'s return shape.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's anonymous <c>{ assignable; identity; subtype; strictSubtype }</c> object has no counterpart in
    /// the generated model, so <see cref="ITypeChecker.GetRelationCacheSizes"/> is <c>object?</c> on both sides
    /// (<c>Program.IProgram.cs:205-209</c> records the same decision). This record is that shape, so a caller can read
    /// the four sizes without reflection.
    /// </remarks>
    internal sealed record RelationCacheSizes(int Assignable, int Identity, int Subtype, int StrictSubtype);

    // ---------------------------------------------------------------------------------------------
    // The counts (checker.ts:1611-1621)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:1611 — <c>getNodeCount</c>.</summary>
    int ITypeChecker.GetNodeCount()
    {
        var count = 0;
        foreach (var file in _host.GetSourceFiles())
        {
            count += file.NodeCount;
        }
        return count;
    }

    /// <summary>ported from: src/compiler/checker.ts:1612 — <c>getIdentifierCount</c>.</summary>
    int ITypeChecker.GetIdentifierCount()
    {
        var count = 0;
        foreach (var file in _host.GetSourceFiles())
        {
            count += file.IdentifierCount;
        }
        return count;
    }

    /// <summary>ported from: src/compiler/checker.ts:1613 — <c>getSymbolCount</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>reduceLeft(…, symbolCount)</c> seeds the fold with the live counter; the port adds it after the loop, the same value.</remarks>
    int ITypeChecker.GetSymbolCount()
    {
        var count = _symbolCount;
        foreach (var file in _host.GetSourceFiles())
        {
            count += file.SymbolCount;
        }
        return count;
    }

    /// <summary>ported from: src/compiler/checker.ts:1614 — <c>getTypeCount</c>.</summary>
    int ITypeChecker.GetTypeCount() => _typeCount;

    /// <summary>ported from: src/compiler/checker.ts:1615 — <c>getInstantiationCount</c>.</summary>
    int ITypeChecker.GetInstantiationCount() => _totalInstantiationCount;

    /// <summary>ported from: src/compiler/checker.ts:1616-1621 — <c>getRelationCacheSizes</c>.</summary>
    object? ITypeChecker.GetRelationCacheSizes()
        => new RelationCacheSizes(_assignableRelation.Count, _identityRelation.Count, _subtypeRelation.Count, _strictSubtypeRelation.Count);

    // ---------------------------------------------------------------------------------------------
    // The type predicates of the global symbols (checker.ts:1622-1624) and the forwards (1625-1630)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:1622 — <c>isUndefinedSymbol</c>.</summary>
    bool ITypeChecker.IsUndefinedSymbol(ISymbol? symbol) => ReferenceEquals(symbol, _undefinedSymbol);

    /// <summary>ported from: src/compiler/checker.ts:1623 — <c>isArgumentsSymbol</c>.</summary>
    bool ITypeChecker.IsArgumentsSymbol(ISymbol? symbol) => ReferenceEquals(symbol, _argumentsSymbol);

    /// <summary>ported from: src/compiler/checker.ts:1624 — <c>isUnknownSymbol</c>.</summary>
    bool ITypeChecker.IsUnknownSymbol(ISymbol? symbol) => ReferenceEquals(symbol, _unknownSymbol);

    /// <summary>ported from: src/compiler/checker.ts:1625 — <c>getMergedSymbol</c>.</summary>
    ISymbol ITypeChecker.GetMergedSymbol(ISymbol? symbol) => GetMergedSymbol(symbol!)!;

    /// <summary>ported from: src/compiler/checker.ts:1626 — <c>symbolIsValue</c>.</summary>
    bool ITypeChecker.SymbolIsValue(ISymbol? symbol, bool? includeTypeOnlyMembers) => SymbolIsValue(symbol!, includeTypeOnlyMembers);

    /// <summary>ported from: src/compiler/checker.ts:1627 — <c>getDiagnostics</c>.</summary>
    List<IDiagnostic> ITypeChecker.GetDiagnostics(SourceFile? sourceFile, ICancellationToken? cancellationToken, List<Node>? nodesToCheck)
        => [.. GetDiagnostics(sourceFile, cancellationToken, nodesToCheck)];

    /// <summary>ported from: src/compiler/checker.ts:1628 — <c>getGlobalDiagnostics</c>.</summary>
    List<IDiagnostic> ITypeChecker.GetGlobalDiagnostics() => [.. GetGlobalDiagnostics()];

    /// <summary>ported from: src/compiler/checker.ts:1629 — <c>getRecursionIdentity</c>.</summary>
    object? ITypeChecker.GetRecursionIdentity(IType? type) => GetRecursionIdentity(type!);

    /// <summary>ported from: src/compiler/checker.ts:1630 — <c>getUnmatchedProperties</c>.</summary>
    IEnumerable<ISymbol> ITypeChecker.GetUnmatchedProperties(IType? source, IType? target, bool requireOptionalProperties, bool matchDiscriminantProperties)
        => GetUnmatchedProperties(source!, target!, requireOptionalProperties, matchDiscriminantProperties);

    // ---------------------------------------------------------------------------------------------
    // The symbol/type queries whose guard is getParseTreeNode (checker.ts:1631-1655)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:1631-1634 — <c>getTypeOfSymbolAtLocation</c>.</summary>
    IType ITypeChecker.GetTypeOfSymbolAtLocation(ISymbol? symbol, Node? node)
        => Utilities.GetParseTreeNode(node) is { } location ? GetTypeOfSymbolAtLocation(symbol, location) : _errorType;

    /// <summary>ported from: src/compiler/checker.ts:1635 — <c>getTypeOfSymbol</c>.</summary>
    IType ITypeChecker.GetTypeOfSymbol(ISymbol? symbol) => GetTypeOfSymbol(symbol!);

    /// <summary>ported from: src/compiler/checker.ts:1636-1642 — <c>getSymbolsOfParameterPropertyDeclaration</c>.</summary>
    List<ISymbol> ITypeChecker.GetSymbolsOfParameterPropertyDeclaration(ParameterDeclaration? parameter, string? parameterName)
    {
        var symbol = (ParameterDeclaration?)Utilities.GetParseTreeNode(parameter, NodeTests.IsParameter);
        if (symbol is null)
        {
            Debug.Fail("Cannot get symbols of a synthetic parameter that cannot be resolved to a parse-tree node.");
            return [];
        }
        Debug.Assert(Utilities.IsParameterPropertyDeclaration(symbol, symbol.Parent!));
        return GetSymbolsOfParameterPropertyDeclaration(symbol, Utilities.EscapeLeadingUnderscores(parameterName ?? ""));
    }

    /// <summary>ported from: src/compiler/checker.ts:1643 — <c>getDeclaredTypeOfSymbol</c>.</summary>
    IType ITypeChecker.GetDeclaredTypeOfSymbol(ISymbol? symbol) => GetDeclaredTypeOfSymbol(symbol!);

    /// <summary>ported from: src/compiler/checker.ts:1644 — <c>getPropertiesOfType</c>.</summary>
    List<ISymbol> ITypeChecker.GetPropertiesOfType(IType? type) => GetPropertiesOfType(type);

    /// <summary>ported from: src/compiler/checker.ts:1645 — <c>getPropertyOfType</c>.</summary>
    ISymbol? ITypeChecker.GetPropertyOfType(IType? type, string? propertyName)
        => GetPropertyOfType(type, Utilities.EscapeLeadingUnderscores(propertyName ?? ""));

    /// <summary>ported from: src/compiler/checker.ts:1646-1654 — <c>getPrivateIdentifierPropertyOfType</c>.</summary>
    ISymbol? ITypeChecker.GetPrivateIdentifierPropertyOfType(IType? leftType, string? name, Node? location)
    {
        var node = Utilities.GetParseTreeNode(location);
        if (node is null) return null;
        var propName = Utilities.EscapeLeadingUnderscores(name ?? "");
        var lexicallyScopedIdentifier = LookupSymbolForPrivateIdentifierDeclaration(propName, node);
        return lexicallyScopedIdentifier is not null ? GetPrivateIdentifierPropertyOfType(leftType!, lexicallyScopedIdentifier) : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:1655 — <c>getTypeOfPropertyOfType</c>.</summary>
    IType? ITypeChecker.GetTypeOfPropertyOfType(IType? type, string? propertyName)
        => GetTypeOfPropertyOfType(type, Utilities.EscapeLeadingUnderscores(propertyName ?? ""));

    /// <summary>ported from: src/compiler/checker.ts:1656 — <c>getIndexInfoOfType</c>.</summary>
    IIndexInfo? ITypeChecker.GetIndexInfoOfType(IType? type, IndexKind kind)
        => GetIndexInfoOfType(type!, kind == IndexKind.String ? _stringType : _numberType);

    /// <summary>ported from: src/compiler/checker.ts:1657 — <c>getIndexInfosOfType</c>.</summary>
    List<IIndexInfo> ITypeChecker.GetIndexInfosOfType(IType? type) => GetIndexInfosOfType(type);

    /// <summary>ported from: src/compiler/checker.ts:1658 — <c>getIndexInfosOfIndexSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's entry is the forwarding shorthand for the function
    /// <c>getIndexInfosOfIndexSymbol(indexSymbol, siblingSymbols?)</c> (checker.ts:16693). The generated interface
    /// declares the member with a different type (<see cref="ITypeChecker.GetIndexInfosOfIndexSymbol"/>'s remarks on
    /// the class record the defect), so neither accessor can answer; both throw §15.15 ruling 1's loud, named failure
    /// and <c>CheckerCreateTypeCheckerTests</c> pins this name as the interface-minus-class difference.
    /// </remarks>
    List<Func<ISymbol, List<ISymbol>, IIndexInfo>>? ITypeChecker.GetIndexInfosOfIndexSymbol
    {
        get => throw new NotSupportedException("ITypeChecker.GetIndexInfosOfIndexSymbol: the generated member's shape (Model/NodeInterfaces.cs:3526) is not the reference's function (types.ts:5083, checker.ts:1658/16693) — generator defect, reported");
        set => throw new NotSupportedException("ITypeChecker.GetIndexInfosOfIndexSymbol: the generated member's shape (Model/NodeInterfaces.cs:3526) is not the reference's function (types.ts:5083, checker.ts:1658/16693) — generator defect, reported");
    }

    /// <summary>ported from: src/compiler/checker.ts:1659 — <c>getSignaturesOfType</c>.</summary>
    List<ISignature> ITypeChecker.GetSignaturesOfType(IType? type, SignatureKind kind) => GetSignaturesOfType(type, kind);

    /// <summary>ported from: src/compiler/checker.ts:1660 — <c>getIndexTypeOfType</c>.</summary>
    IType? ITypeChecker.GetIndexTypeOfType(IType? type, IndexKind kind)
        => GetIndexTypeOfType(type!, kind == IndexKind.String ? _stringType : _numberType);

    /// <summary>ported from: src/compiler/checker.ts:1661 — <c>getIndexType</c>.</summary>
    IType ITypeChecker.GetIndexType(IType? type) => GetIndexType(type!);

    /// <summary>ported from: src/compiler/checker.ts:1662 — <c>getBaseTypes</c>.</summary>
    List<IType> ITypeChecker.GetBaseTypes(IInterfaceType? type) => GetBaseTypes(type!);

    /// <summary>ported from: src/compiler/checker.ts:1663 — <c>getBaseTypeOfLiteralType</c>.</summary>
    IType ITypeChecker.GetBaseTypeOfLiteralType(IType? type) => GetBaseTypeOfLiteralType(type!);

    /// <summary>ported from: src/compiler/checker.ts:1664 — <c>getWidenedType</c>.</summary>
    IType ITypeChecker.GetWidenedType(IType? type) => GetWidenedType(type!);

    /// <summary>ported from: src/compiler/checker.ts:1665 — <c>getWidenedLiteralType</c>.</summary>
    IType ITypeChecker.GetWidenedLiteralType(IType? type) => GetWidenedLiteralType(type!);

    /// <summary>ported from: src/compiler/checker.ts:1666-1667 — <c>fillMissingTypeArguments</c>.</summary>
    List<IType> ITypeChecker.FillMissingTypeArguments(List<IType>? typeArguments, List<ITypeParameter>? typeParameters, int minTypeArgumentCount, bool isJavaScriptImplicitAny)
        => FillMissingTypeArguments(typeArguments, typeParameters, minTypeArgumentCount, isJavaScriptImplicitAny)!;

    /// <summary>ported from: src/compiler/checker.ts:1668-1671 — <c>getTypeFromTypeNode</c>.</summary>
    IType ITypeChecker.GetTypeFromTypeNode(ITypeNode? node)
        => Utilities.GetParseTreeNode((Node?)node, Utilities.IsTypeNode) is { } typeNode ? GetTypeFromTypeNode((ITypeNode)typeNode) : _errorType;

    /// <summary>ported from: src/compiler/checker.ts:1672 — <c>getParameterType: getTypeAtPosition</c>.</summary>
    IType ITypeChecker.GetParameterType(ISignature? signature, int parameterIndex) => GetTypeAtPosition(signature!, parameterIndex);

    /// <summary>ported from: src/compiler/checker.ts:1673 — <c>getParameterIdentifierInfoAtPosition</c>.</summary>
    /// <remarks>PORT-NOTE: the generated member's return is <c>object?</c> because TS's anonymous
    /// <c>{ parameter; parameterName; isRestParameter }</c> has no counterpart; the port returns the record its own
    /// member declares.</remarks>
    object? ITypeChecker.GetParameterIdentifierInfoAtPosition(ISignature? signature, int parameterIndex)
        => GetParameterIdentifierInfoAtPosition(signature!, parameterIndex);

    /// <summary>ported from: src/compiler/checker.ts:1674 — <c>getPromisedTypeOfPromise</c>.</summary>
    IType? ITypeChecker.GetPromisedTypeOfPromise(IType? promise, Node? errorNode) => GetPromisedTypeOfPromise(promise!, errorNode);

    /// <summary>ported from: src/compiler/checker.ts:1675 — <c>getAwaitedType</c>.</summary>
    IType? ITypeChecker.GetAwaitedType(IType? type) => GetAwaitedType(type!);

    /// <summary>ported from: src/compiler/checker.ts:1676 — <c>getReturnTypeOfSignature</c>.</summary>
    IType ITypeChecker.GetReturnTypeOfSignature(ISignature? signature) => GetReturnTypeOfSignature(signature);

    /// <summary>ported from: src/compiler/checker.ts:1677 — <c>isNullableType</c>.</summary>
    bool ITypeChecker.IsNullableType(IType? type) => IsNullableType(type!);

    /// <summary>ported from: src/compiler/checker.ts:1678 — <c>getNullableType</c>.</summary>
    IType ITypeChecker.GetNullableType(IType? type, TypeFlags flags) => GetNullableType(type!, flags);

    /// <summary>ported from: src/compiler/checker.ts:1679 — <c>getNonNullableType</c>.</summary>
    IType ITypeChecker.GetNonNullableType(IType? type) => GetNonNullableType(type!);

    /// <summary>ported from: src/compiler/checker.ts:1680 — <c>getNonOptionalType: removeOptionalTypeMarker</c>.</summary>
    IType ITypeChecker.GetNonOptionalType(IType? type) => RemoveOptionalTypeMarker(type!);

    /// <summary>ported from: src/compiler/checker.ts:1681 — <c>getTypeArguments</c>.</summary>
    List<IType> ITypeChecker.GetTypeArguments(ITypeReference? type) => GetTypeArguments(type!);

    // ---------------------------------------------------------------------------------------------
    // The node-builder forwards (checker.ts:1682-1699)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:1682 — <c>typeToTypeNode: nodeBuilder.typeToTypeNode</c>.</summary>
    ITypeNode? ITypeChecker.TypeToTypeNode(IType? type, Node? enclosingDeclaration, NodeBuilderFlags? flags)
        => TypeToTypeNode(type, enclosingDeclaration, flags);

    /// <summary>ported from: src/compiler/checker.ts:1682 — <c>typeToTypeNode: nodeBuilder.typeToTypeNode</c> (the tracker overload).</summary>
    ITypeNode? ITypeChecker.TypeToTypeNode(IType? type, Node? enclosingDeclaration, NodeBuilderFlags? flags, InternalNodeBuilderFlags? internalFlags, ISymbolTracker? tracker)
        => TypeToTypeNode(type, enclosingDeclaration, flags, internalFlags, tracker);

    /// <summary>ported from: src/compiler/checker.ts:1683 — <c>typePredicateToTypePredicateNode</c>.</summary>
    TypePredicateNode? ITypeChecker.TypePredicateToTypePredicateNode(ITypePredicateBase? typePredicate, Node? enclosingDeclaration, NodeBuilderFlags? flags, InternalNodeBuilderFlags? internalFlags, ISymbolTracker? tracker)
        => TypePredicateToTypePredicateNode(typePredicate, enclosingDeclaration, flags, internalFlags, tracker);

    /// <summary>ported from: src/compiler/checker.ts:1684 — <c>indexInfoToIndexSignatureDeclaration</c>.</summary>
    IndexSignatureDeclaration? ITypeChecker.IndexInfoToIndexSignatureDeclaration(IIndexInfo? indexInfo, Node? enclosingDeclaration, NodeBuilderFlags? flags)
        => IndexInfoToIndexSignatureDeclaration(indexInfo, enclosingDeclaration, flags);

    /// <summary>ported from: src/compiler/checker.ts:1684 — <c>indexInfoToIndexSignatureDeclaration</c> (the tracker overload).</summary>
    IndexSignatureDeclaration? ITypeChecker.IndexInfoToIndexSignatureDeclaration(IIndexInfo? indexInfo, Node? enclosingDeclaration, NodeBuilderFlags? flags, InternalNodeBuilderFlags? internalFlags, ISymbolTracker? tracker)
        => IndexInfoToIndexSignatureDeclaration(indexInfo, enclosingDeclaration, flags, internalFlags, tracker);

    /// <summary>ported from: src/compiler/checker.ts:1685 — <c>signatureToSignatureDeclaration</c>.</summary>
    Node? ITypeChecker.SignatureToSignatureDeclaration(ISignature? signature, SyntaxKind kind, Node? enclosingDeclaration, NodeBuilderFlags? flags)
        => SignatureToSignatureDeclaration(signature, kind, enclosingDeclaration, flags);

    /// <summary>ported from: src/compiler/checker.ts:1685 — <c>signatureToSignatureDeclaration</c> (the tracker overload).</summary>
    Node? ITypeChecker.SignatureToSignatureDeclaration(ISignature? signature, SyntaxKind kind, Node? enclosingDeclaration, NodeBuilderFlags? flags, InternalNodeBuilderFlags? internalFlags, ISymbolTracker? tracker)
        => SignatureToSignatureDeclaration(signature, kind, enclosingDeclaration, flags, internalFlags, tracker);

    /// <summary>ported from: src/compiler/checker.ts:1686 — <c>symbolToEntityName</c>.</summary>
    Node? ITypeChecker.SymbolToEntityName(ISymbol? symbol, SymbolFlags meaning, Node? enclosingDeclaration, NodeBuilderFlags? flags)
        => SymbolToEntityName(symbol, meaning, enclosingDeclaration, flags);

    /// <summary>ported from: src/compiler/checker.ts:1687 — <c>symbolToExpression</c>.</summary>
    Expression? ITypeChecker.SymbolToExpression(ISymbol? symbol, SymbolFlags meaning, Node? enclosingDeclaration, NodeBuilderFlags? flags)
        => SymbolToExpression(symbol, meaning, enclosingDeclaration, flags);

    /// <summary>ported from: src/compiler/checker.ts:1688 — <c>symbolToNode</c>.</summary>
    Node? ITypeChecker.SymbolToNode(ISymbol? symbol, SymbolFlags meaning, Node? enclosingDeclaration, NodeBuilderFlags? flags, InternalNodeBuilderFlags? internalFlags)
        => SymbolToNode(symbol, meaning, enclosingDeclaration, flags, internalFlags);

    /// <summary>ported from: src/compiler/checker.ts:1689 — <c>symbolToTypeParameterDeclarations</c>.</summary>
    NodeArray<TypeParameterDeclaration>? ITypeChecker.SymbolToTypeParameterDeclarations(ISymbol? symbol, Node? enclosingDeclaration, NodeBuilderFlags? flags)
        => SymbolToTypeParameterDeclarations(symbol, enclosingDeclaration, flags);

    /// <summary>ported from: src/compiler/checker.ts:1690 — <c>symbolToParameterDeclaration</c>.</summary>
    ParameterDeclaration? ITypeChecker.SymbolToParameterDeclaration(ISymbol? symbol, Node? enclosingDeclaration, NodeBuilderFlags? flags)
        => SymbolToParameterDeclaration(symbol, enclosingDeclaration, flags);

    /// <summary>ported from: src/compiler/checker.ts:1691 — <c>typeParameterToDeclaration</c>.</summary>
    TypeParameterDeclaration? ITypeChecker.TypeParameterToDeclaration(ITypeParameter? parameter, Node? enclosingDeclaration, NodeBuilderFlags? flags)
        => TypeParameterToDeclaration(parameter, enclosingDeclaration, flags);

    /// <summary>ported from: src/compiler/checker.ts:1691 — <c>typeParameterToDeclaration</c> (the writer overload).</summary>
    TypeParameterDeclaration? ITypeChecker.TypeParameterToDeclaration(ITypeParameter? parameter, Node? enclosingDeclaration, NodeBuilderFlags? flags, InternalNodeBuilderFlags? internalFlags, ISymbolTracker? tracker, int? maximumLength, int? verbosityLevel, IWriterContextOut? @out)
        => TypeParameterToDeclaration(parameter, enclosingDeclaration, flags, internalFlags, tracker, maximumLength, verbosityLevel, @out);

    // ---------------------------------------------------------------------------------------------
    // The guarded queries the services call (checker.ts:1700-1760)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:1700-1703 — <c>getSymbolsInScope</c>.</summary>
    List<ISymbol> ITypeChecker.GetSymbolsInScope(Node? location, SymbolFlags meaning)
        => Utilities.GetParseTreeNode(location) is { } node ? GetSymbolsInScope(node, meaning) : [];

    /// <summary>ported from: src/compiler/checker.ts:1704-1708 — <c>getSymbolAtLocation</c>.</summary>
    /// <remarks>PORT-NOTE: <c>ignoreErrors: true</c> because any lookups invoked by the API should not cause new errors.</remarks>
    ISymbol? ITypeChecker.GetSymbolAtLocation(Node? node)
        => Utilities.GetParseTreeNode(node) is { } location ? GetSymbolAtLocation(location, /*ignoreErrors*/ true) : null;

    /// <summary>ported from: src/compiler/checker.ts:1709-1712 — <c>getIndexInfosAtLocation</c>.</summary>
    List<IIndexInfo>? ITypeChecker.GetIndexInfosAtLocation(Node? node)
        => Utilities.GetParseTreeNode(node) is { } location ? GetIndexInfosAtLocation(location) : null;

    /// <summary>ported from: src/compiler/checker.ts:1713-1716 — <c>getShorthandAssignmentValueSymbol</c>.</summary>
    ISymbol? ITypeChecker.GetShorthandAssignmentValueSymbol(Node? location)
        => Utilities.GetParseTreeNode(location) is { } node ? GetShorthandAssignmentValueSymbol(node) : null;

    /// <summary>ported from: src/compiler/checker.ts:1717-1720 — <c>getExportSpecifierLocalTargetSymbol</c>.</summary>
    ISymbol? ITypeChecker.GetExportSpecifierLocalTargetSymbol(Node? location)
        => Utilities.GetParseTreeNode(location, NodeTests.IsExportSpecifier) is { } node ? GetExportSpecifierLocalTargetSymbol(node) : null;

    /// <summary>ported from: src/compiler/checker.ts:1721-1723 — <c>getExportSymbolOfSymbol</c>.</summary>
    ISymbol ITypeChecker.GetExportSymbolOfSymbol(ISymbol? symbol) => GetMergedSymbol(symbol!.ExportSymbol ?? symbol)!;

    /// <summary>ported from: src/compiler/checker.ts:1724-1727 — <c>getTypeAtLocation: getTypeOfNode</c>.</summary>
    IType ITypeChecker.GetTypeAtLocation(Node? node)
        => Utilities.GetParseTreeNode(node) is { } location ? GetTypeOfNode(location) : _errorType;

    /// <summary>ported from: src/compiler/checker.ts:1728-1731 — <c>getTypeOfAssignmentPattern</c>.</summary>
    IType ITypeChecker.GetTypeOfAssignmentPattern(MemberExpression? pattern)
        => Utilities.GetParseTreeNode(pattern, Utilities.IsAssignmentPattern) is { } node ? (GetTypeOfAssignmentPattern((MemberExpression)node) ?? _errorType) : _errorType;

    /// <summary>ported from: src/compiler/checker.ts:1732-1735 — <c>getPropertySymbolOfDestructuringAssignment</c>.</summary>
    ISymbol? ITypeChecker.GetPropertySymbolOfDestructuringAssignment(Identifier? location)
        => Utilities.GetParseTreeNode(location, NodeTests.IsIdentifier) is { } node ? GetPropertySymbolOfDestructuringAssignment((Identifier)node) : null;

    // ---------------------------------------------------------------------------------------------
    // The string converters (checker.ts:1736-1757) — the printer-gated half (§15.15 ruling 1)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:1736-1738 — <c>signatureToString</c>.</summary>
    string ITypeChecker.SignatureToString(ISignature? signature, Node? enclosingDeclaration, TypeFormatFlags? flags, SignatureKind? kind)
        => SignatureToString(signature, Utilities.GetParseTreeNode(enclosingDeclaration), flags, kind);

    /// <summary>ported from: src/compiler/checker.ts:1739-1741 — <c>typeToString</c>.</summary>
    string ITypeChecker.TypeToString(IType? type, Node? enclosingDeclaration, TypeFormatFlags? flags)
        => TypeToString(type, Utilities.GetParseTreeNode(enclosingDeclaration), flags);

    /// <summary>ported from: src/compiler/checker.ts:1742-1744 — <c>symbolToString</c>.</summary>
    string ITypeChecker.SymbolToString(ISymbol? symbol, Node? enclosingDeclaration, SymbolFlags? meaning, SymbolFormatFlags? flags)
        => SymbolToString(symbol, Utilities.GetParseTreeNode(enclosingDeclaration), meaning, flags);

    /// <summary>ported from: src/compiler/checker.ts:1745-1747 — <c>typePredicateToString</c>.</summary>
    string ITypeChecker.TypePredicateToString(ITypePredicateBase? predicate, Node? enclosingDeclaration, TypeFormatFlags? flags)
        => TypePredicateToString(predicate, Utilities.GetParseTreeNode(enclosingDeclaration), flags);

    /// <summary>ported from: src/compiler/checker.ts:1748-1750 — <c>writeSignature</c>.</summary>
    string ITypeChecker.WriteSignature(ISignature? signature, Node? enclosingDeclaration, TypeFormatFlags? flags, SignatureKind? kind, IEmitTextWriter? writer, int? maximumLength, int? verbosityLevel, IWriterContextOut? @out)
        => WriteSignature(signature, Utilities.GetParseTreeNode(enclosingDeclaration), flags, kind, writer, maximumLength, verbosityLevel, @out);

    /// <summary>ported from: src/compiler/checker.ts:1751-1753 — <c>writeType</c>.</summary>
    string ITypeChecker.WriteType(IType? type, Node? enclosingDeclaration, TypeFormatFlags? flags, IEmitTextWriter? writer, int? maximumLength, int? verbosityLevel, IWriterContextOut? @out)
        => WriteType(type, Utilities.GetParseTreeNode(enclosingDeclaration), flags, writer, maximumLength, verbosityLevel, @out);

    /// <summary>ported from: src/compiler/checker.ts:1754-1756 — <c>writeSymbol</c>.</summary>
    string ITypeChecker.WriteSymbol(ISymbol? symbol, Node? enclosingDeclaration, SymbolFlags? meaning, SymbolFormatFlags? flags, IEmitTextWriter? writer)
        => WriteSymbol(symbol, Utilities.GetParseTreeNode(enclosingDeclaration), meaning, flags, writer);

    /// <summary>ported from: src/compiler/checker.ts:1757 — <c>writeTypePredicate</c>.</summary>
    string ITypeChecker.WriteTypePredicate(ITypePredicateBase? predicate, Node? enclosingDeclaration, TypeFormatFlags? flags, IEmitTextWriter? writer)
        => WriteTypePredicate(predicate, Utilities.GetParseTreeNode(enclosingDeclaration), flags, writer);

    // ---------------------------------------------------------------------------------------------
    // The forwards of checker.ts:1758-1761 and the contextual-type wrappers (1762-1777)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:1758 — <c>getAugmentedPropertiesOfType</c>.</summary>
    List<ISymbol> ITypeChecker.GetAugmentedPropertiesOfType(IType? type) => GetAugmentedPropertiesOfType(type);

    /// <summary>ported from: src/compiler/checker.ts:1759 — <c>getRootSymbols</c>.</summary>
    List<ISymbol> ITypeChecker.GetRootSymbols(ISymbol? symbol) => GetRootSymbols(symbol!);

    /// <summary>ported from: src/compiler/checker.ts:1760 — <c>getSymbolOfExpando</c>.</summary>
    ISymbol? ITypeChecker.GetSymbolOfExpando(Node? node, bool allowDeclaration) => GetSymbolOfExpando(node!, allowDeclaration);

    /// <summary>ported from: src/compiler/checker.ts:1761-1770 — <c>getContextualType</c>.</summary>
    IType? ITypeChecker.GetContextualType(Expression? node) => ((ITypeChecker)this).GetContextualType(node, /*contextFlags*/ null);

    /// <summary>ported from: src/compiler/checker.ts:1761-1770 — <c>getContextualType</c>.</summary>
    IType? ITypeChecker.GetContextualType(Expression? node, ContextFlags? contextFlags)
    {
        var parseNode = Utilities.GetParseTreeNode(node, Utilities.IsExpression) as Expression;
        if (parseNode is null) return null;
        if (contextFlags.HasValue && (contextFlags.Value & ContextFlags.IgnoreNodeInferences) != 0)
        {
            return RunWithInferenceBlockedFromSourceNode(parseNode, () => GetContextualType(parseNode, contextFlags));
        }
        return GetContextualType(parseNode, contextFlags);
    }

    /// <summary>ported from: src/compiler/checker.ts:1771-1774 — <c>getContextualTypeForObjectLiteralElement</c>.</summary>
    IType? ITypeChecker.GetContextualTypeForObjectLiteralElement(Node? element)
        => Utilities.GetParseTreeNode(element, Utilities.IsObjectLiteralElementLike) is { } node
            ? GetContextualTypeForObjectLiteralElement(node, /*contextFlags*/ null)
            : null;

    /// <summary>ported from: src/compiler/checker.ts:1775-1778 — <c>getContextualTypeForArgumentAtIndex</c>.</summary>
    IType? ITypeChecker.GetContextualTypeForArgumentAtIndex(Node? call, int argIndex)
        => Utilities.GetParseTreeNode(call, Utilities.IsCallLikeExpression) is { } node
            ? GetContextualTypeForArgumentAtIndex(node, argIndex)
            : null;

    /// <summary>ported from: src/compiler/checker.ts:1779-1782 — <c>getContextualTypeForJsxAttribute</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's node test is <c>isJsxAttributeLike</c> (<c>utilities.ts</c>), which has no
    /// declaration in the port; the two kinds it tests are inlined here and the missing <c>Utilities</c> member is
    /// named in the slice report.</remarks>
    IType? ITypeChecker.GetContextualTypeForJsxAttribute(Node? attribute)
        => Utilities.GetParseTreeNode(attribute, static n => n.Kind is SyntaxKind.JsxAttribute or SyntaxKind.JsxSpreadAttribute) is { } node
            ? GetContextualTypeForJsxAttribute(node, /*contextFlags*/ null)
            : null;

    /// <summary>ported from: src/compiler/checker.ts:1783 — <c>isContextSensitive</c>.</summary>
    bool ITypeChecker.IsContextSensitive(Node? node) => IsContextSensitive(node);

    /// <summary>ported from: src/compiler/checker.ts:1784 — <c>getTypeOfPropertyOfContextualType</c>.</summary>
    IType? ITypeChecker.GetTypeOfPropertyOfContextualType(IType? type, string? name) => GetTypeOfPropertyOfContextualType(type!, name!);

    /// <summary>ported from: src/compiler/checker.ts:1785 — <c>getFullyQualifiedName</c>.</summary>
    string ITypeChecker.GetFullyQualifiedName(ISymbol? symbol) => GetFullyQualifiedName(symbol!);

    // ---------------------------------------------------------------------------------------------
    // The resolved-signature wrappers (checker.ts:1786-1790)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:1786 — <c>getResolvedSignature</c>.</summary>
    ISignature? ITypeChecker.GetResolvedSignature(Node? node, List<ISignature>? candidatesOutArray, int? argumentCount)
        => GetResolvedSignatureWorker(node!, candidatesOutArray, argumentCount, CheckMode.Normal);

    /// <summary>ported from: src/compiler/checker.ts:1787 — <c>getCandidateSignaturesForStringLiteralCompletions</c>.</summary>
    List<ISignature> ITypeChecker.GetCandidateSignaturesForStringLiteralCompletions(Node? call, Node? editingArgument)
        => GetCandidateSignaturesForStringLiteralCompletions(call!, editingArgument!);

    /// <summary>ported from: src/compiler/checker.ts:1788 — <c>getResolvedSignatureForSignatureHelp</c>.</summary>
    ISignature? ITypeChecker.GetResolvedSignatureForSignatureHelp(Node? node, List<ISignature>? candidatesOutArray, int? argumentCount)
        => RunWithoutResolvedSignatureCaching(node, () => GetResolvedSignatureWorker(node!, candidatesOutArray, argumentCount, CheckMode.IsForSignatureHelp));

    /// <summary>ported from: src/compiler/checker.ts:1789 — <c>getExpandedParameters</c>.</summary>
    List<List<ISymbol>> ITypeChecker.GetExpandedParameters(ISignature? sig) => [.. GetExpandedParameters(sig!)];

    /// <summary>ported from: src/compiler/checker.ts:1790 — <c>hasEffectiveRestParameter</c>.</summary>
    bool ITypeChecker.HasEffectiveRestParameter(ISignature? sig) => HasEffectiveRestParameter(sig!);

    /// <summary>ported from: src/compiler/checker.ts:1791 — <c>containsArgumentsReference</c>.</summary>
    bool ITypeChecker.ContainsArgumentsReference(Node? declaration) => ContainsArgumentsReference(declaration!);

    /// <summary>ported from: src/compiler/checker.ts:1792-1795 — <c>getConstantValue</c>.</summary>
    object? ITypeChecker.GetConstantValue(Node? node)
        => Utilities.GetParseTreeNode(node, CanHaveConstantValue) is { } constantValueNode ? GetConstantValue(constantValueNode) : null;

    /// <summary>ported from: src/compiler/checker.ts:1796-1799 — <c>isValidPropertyAccess</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's node test <c>isPropertyAccessOrQualifiedNameOrImportTypeNode</c>
    /// (<c>utilities.ts</c>) has no declaration in the port; its three kinds are inlined here and the missing member is
    /// named in the slice report.</remarks>
    bool ITypeChecker.IsValidPropertyAccess(Node? node, string? propertyName)
        => Utilities.GetParseTreeNode(node, static n => NodeTests.IsPropertyAccessExpression(n) || NodeTests.IsQualifiedName(n) || NodeTests.IsImportTypeNode(n)) is { } propertyAccess
            && IsValidPropertyAccess(propertyAccess, Utilities.EscapeLeadingUnderscores(propertyName ?? ""));

    /// <summary>ported from: src/compiler/checker.ts:1800-1803 — <c>isValidPropertyAccessForCompletions</c>.</summary>
    bool ITypeChecker.IsValidPropertyAccessForCompletions(Node? node, IType? type, ISymbol? property)
        => Utilities.GetParseTreeNode(node, NodeTests.IsPropertyAccessExpression) is { } propertyAccess
            && IsValidPropertyAccessForCompletions(propertyAccess, type, property);

    /// <summary>ported from: src/compiler/checker.ts:1804-1807 — <c>getSignatureFromDeclaration</c>.</summary>
    ISignature? ITypeChecker.GetSignatureFromDeclaration(Node? declaration)
        => Utilities.GetParseTreeNode(declaration, Utilities.IsFunctionLike) is { } functionLike ? GetSignatureFromDeclaration(functionLike) : null;

    /// <summary>ported from: src/compiler/checker.ts:1808-1811 — <c>isImplementationOfOverload</c>.</summary>
    bool? ITypeChecker.IsImplementationOfOverload(Node? node)
        => Utilities.GetParseTreeNode(node, Utilities.IsFunctionLike) is { } functionLike ? IsImplementationOfOverload(functionLike) : null;

    // ---------------------------------------------------------------------------------------------
    // The aliases and module surfaces (checker.ts:1812-1833)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:1812 — <c>getImmediateAliasedSymbol</c>.</summary>
    ISymbol? ITypeChecker.GetImmediateAliasedSymbol(ISymbol? symbol) => GetImmediateAliasedSymbol(symbol!);

    /// <summary>ported from: src/compiler/checker.ts:1813 — <c>getAliasedSymbol: resolveAlias</c>.</summary>
    ISymbol ITypeChecker.GetAliasedSymbol(ISymbol? symbol) => ResolveAlias(symbol!);

    /// <summary>ported from: src/compiler/checker.ts:1814 — <c>getEmitResolver</c>.</summary>
    IEmitResolver ITypeChecker.GetEmitResolver(SourceFile? sourceFile, ICancellationToken? cancellationToken, bool? forceDts)
        => GetEmitResolver(sourceFile, cancellationToken, forceDts);

    /// <summary>ported from: src/compiler/checker.ts:1815 — <c>requiresAddingImplicitUndefined</c>.</summary>
    bool ITypeChecker.RequiresAddingImplicitUndefined(Node? parameter, Node? enclosingDeclaration)
        => RequiresAddingImplicitUndefined(parameter!, enclosingDeclaration);

    /// <summary>ported from: src/compiler/checker.ts:1816 — <c>getExportsOfModule: getExportsOfModuleAsArray</c>.</summary>
    List<ISymbol> ITypeChecker.GetExportsOfModule(ISymbol? moduleSymbol) => GetExportsOfModuleAsArray(moduleSymbol);

    /// <summary>ported from: src/compiler/checker.ts:1817 — <c>getExportsAndPropertiesOfModule</c>.</summary>
    List<ISymbol> ITypeChecker.GetExportsAndPropertiesOfModule(ISymbol? moduleSymbol) => GetExportsAndPropertiesOfModule(moduleSymbol);

    /// <summary>ported from: src/compiler/checker.ts:1818 — <c>forEachExportAndPropertyOfModule</c>.</summary>
    void ITypeChecker.ForEachExportAndPropertyOfModule(ISymbol? moduleSymbol, Action<ISymbol, string>? cb)
        => ForEachExportAndPropertyOfModule(moduleSymbol, cb);

    /// <summary>ported from: src/compiler/checker.ts:1819-1830 — <c>getSymbolWalker</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's argument list is <c>createGetSymbolWalker(getRestTypeOfSignature,
    /// getTypePredicateOfSignature, getReturnTypeOfSignature, getBaseTypes, resolveStructuredTypeMembers,
    /// getTypeOfSymbol, getResolvedSymbol, getConstraintOfTypeParameter, getFirstIdentifier, getTypeArguments)</c>,
    /// then the returned factory is applied to <c>accept</c>. <c>symbolWalker.ts</c> (198 lines) is not ported in M6,
    /// so <c>CreateGetSymbolWalker</c> is a cross-slice reference, listed in the report.</remarks>
    ISymbolWalker ITypeChecker.GetSymbolWalker(Func<ISymbol, bool>? accept)
        => CreateGetSymbolWalker(
            GetRestTypeOfSignature,
            GetTypePredicateOfSignature,
            GetReturnTypeOfSignature,
            GetBaseTypes,
            ResolveStructuredTypeMembers,
            GetTypeOfSymbol,
            GetResolvedSymbol,
            GetConstraintOfTypeParameter,
            Utilities.GetFirstIdentifier,
            GetTypeArguments)(accept);

    /// <summary>ported from: src/compiler/checker.ts:1831 — <c>getAmbientModules</c>.</summary>
    List<ISymbol> ITypeChecker.GetAmbientModules() => GetAmbientModules();

    /// <summary>ported from: src/compiler/checker.ts:1832 — <c>getJsxIntrinsicTagNamesAt</c>.</summary>
    List<ISymbol> ITypeChecker.GetJsxIntrinsicTagNamesAt(Node? location) => GetJsxIntrinsicTagNamesAt(location!);

    /// <summary>ported from: src/compiler/checker.ts:1833-1836 — <c>isOptionalParameter</c>.</summary>
    bool ITypeChecker.IsOptionalParameter(ParameterDeclaration? node)
        => Utilities.GetParseTreeNode(node, NodeTests.IsParameter) is { } parameter && IsOptionalParameter(parameter);

    /// <summary>ported from: src/compiler/checker.ts:1837 — <c>tryGetMemberInModuleExports</c>.</summary>
    ISymbol? ITypeChecker.TryGetMemberInModuleExports(string? memberName, ISymbol? moduleSymbol)
        => TryGetMemberInModuleExports(Utilities.EscapeLeadingUnderscores(memberName ?? ""), moduleSymbol);

    /// <summary>ported from: src/compiler/checker.ts:1838 — <c>tryGetMemberInModuleExportsAndProperties</c>.</summary>
    ISymbol? ITypeChecker.TryGetMemberInModuleExportsAndProperties(string? memberName, ISymbol? moduleSymbol)
        => TryGetMemberInModuleExportsAndProperties(Utilities.EscapeLeadingUnderscores(memberName ?? ""), moduleSymbol);

    /// <summary>ported from: src/compiler/checker.ts:1839 — <c>tryFindAmbientModule</c>.</summary>
    ISymbol? ITypeChecker.TryFindAmbientModule(string? moduleName) => TryFindAmbientModule(moduleName ?? "", /*withAugmentations*/ true);

    // ---------------------------------------------------------------------------------------------
    // The type constructors and intrinsic type accessors (checker.ts:1840-1876)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:1840 — <c>getApparentType</c>.</summary>
    IType ITypeChecker.GetApparentType(IType? type) => GetApparentType(type);

    /// <summary>ported from: src/compiler/checker.ts:1841 — <c>getUnionType</c>.</summary>
    IType ITypeChecker.GetUnionType(List<IType>? types, UnionReduction? subtypeReduction) => GetUnionType(types!, subtypeReduction ?? UnionReduction.Literal);

    /// <summary>ported from: src/compiler/checker.ts:1842 — <c>isTypeAssignableTo</c>.</summary>
    bool ITypeChecker.IsTypeAssignableTo(IType? source, IType? target) => IsTypeAssignableTo(source, target);

    /// <summary>ported from: src/compiler/checker.ts:1843 — <c>createAnonymousType</c>.</summary>
    IType ITypeChecker.CreateAnonymousType(ISymbol? symbol, SymbolTable? members, List<ISignature>? callSignatures, List<ISignature>? constructSignatures, List<IIndexInfo>? indexInfos)
        => CreateAnonymousType(symbol, members!, callSignatures!, constructSignatures!, indexInfos!);

    /// <summary>ported from: src/compiler/checker.ts:1844 — <c>createSignature</c>.</summary>
    ISignature ITypeChecker.CreateSignature(Node? declaration, List<ITypeParameter>? typeParameters, ISymbol? thisParameter, List<ISymbol>? parameters, IType? resolvedReturnType, ITypePredicateBase? typePredicate, int minArgumentCount, SignatureFlags flags)
        => CreateSignature(declaration, typeParameters, thisParameter, parameters, resolvedReturnType, typePredicate, minArgumentCount, flags);

    /// <summary>ported from: src/compiler/checker.ts:1845 — <c>createSymbol</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>createSymbol</c> returns a <c>Symbol</c> whose <c>links</c> is assigned so its
    /// transient face is established (checker.ts:2633-2637); the port's <c>CreateSymbol</c> returns the transient
    /// class, so the cast is the identity.</remarks>
    ITransientSymbol ITypeChecker.CreateSymbol(SymbolFlags flags, string? name) => (ITransientSymbol)CreateSymbol(flags, name ?? "");

    /// <summary>ported from: src/compiler/checker.ts:1846 — <c>createIndexInfo</c>.</summary>
    IIndexInfo ITypeChecker.CreateIndexInfo(IType? keyType, IType? type, bool isReadonly, Node? declaration)
        => CreateIndexInfo(keyType!, type!, isReadonly, declaration as IndexSignatureDeclaration);

    /// <summary>ported from: src/compiler/checker.ts:1847 — <c>getAnyType</c>.</summary>
    IType ITypeChecker.GetAnyType() => _anyType;

    /// <summary>ported from: src/compiler/checker.ts:1848 — <c>getStringType</c>.</summary>
    IType ITypeChecker.GetStringType() => _stringType;

    /// <summary>ported from: src/compiler/checker.ts:1849 — <c>getStringLiteralType</c>.</summary>
    IStringLiteralType ITypeChecker.GetStringLiteralType(string? value) => GetStringLiteralType(value!);

    /// <summary>ported from: src/compiler/checker.ts:1850 — <c>getNumberType</c>.</summary>
    IType ITypeChecker.GetNumberType() => _numberType;

    /// <summary>ported from: src/compiler/checker.ts:1851 — <c>getNumberLiteralType</c>.</summary>
    INumberLiteralType ITypeChecker.GetNumberLiteralType(int value) => GetNumberLiteralType(value);

    /// <summary>ported from: src/compiler/checker.ts:1852 — <c>getBigIntType</c>.</summary>
    IType ITypeChecker.GetBigIntType() => _bigintType;

    /// <summary>ported from: src/compiler/checker.ts:1853 — <c>getBigIntLiteralType</c>.</summary>
    IBigIntLiteralType ITypeChecker.GetBigIntLiteralType(IPseudoBigInt? value) => GetBigIntLiteralType(value!);

    /// <summary>ported from: src/compiler/checker.ts:1854 — <c>getUnknownType</c>.</summary>
    IType ITypeChecker.GetUnknownType() => _unknownType;

    /// <summary>ported from: src/compiler/checker.ts:1855 — <c>createPromiseType</c>.</summary>
    IType ITypeChecker.CreatePromiseType(IType? type) => CreatePromiseType(type!);

    /// <summary>ported from: src/compiler/checker.ts:1856 — <c>createArrayType</c>.</summary>
    IType ITypeChecker.CreateArrayType(IType? elementType) => CreateArrayType(elementType!);

    /// <summary>ported from: src/compiler/checker.ts:1857 — <c>getElementTypeOfArrayType</c>.</summary>
    IType? ITypeChecker.GetElementTypeOfArrayType(IType? arrayType) => GetElementTypeOfArrayType(arrayType!);

    /// <summary>ported from: src/compiler/checker.ts:1858 — <c>getBooleanType</c>.</summary>
    IType ITypeChecker.GetBooleanType() => _booleanType;

    /// <summary>ported from: src/compiler/checker.ts:1859 — <c>getFalseType</c>.</summary>
    IType ITypeChecker.GetFalseType(bool? fresh) => fresh == true ? _falseType : _regularFalseType;

    /// <summary>ported from: src/compiler/checker.ts:1859 — <c>getFalseType</c> (the parameterless overload).</summary>
    IType ITypeChecker.GetFalseType() => _regularFalseType;

    /// <summary>ported from: src/compiler/checker.ts:1860 — <c>getTrueType</c>.</summary>
    IType ITypeChecker.GetTrueType(bool? fresh) => fresh == true ? _trueType : _regularTrueType;

    /// <summary>ported from: src/compiler/checker.ts:1860 — <c>getTrueType</c> (the parameterless overload).</summary>
    IType ITypeChecker.GetTrueType() => _regularTrueType;

    /// <summary>ported from: src/compiler/checker.ts:1861 — <c>getVoidType</c>.</summary>
    IType ITypeChecker.GetVoidType() => _voidType;

    /// <summary>ported from: src/compiler/checker.ts:1862 — <c>getUndefinedType</c>.</summary>
    IType ITypeChecker.GetUndefinedType() => _undefinedType;

    /// <summary>ported from: src/compiler/checker.ts:1863 — <c>getNullType</c>.</summary>
    IType ITypeChecker.GetNullType() => _nullType;

    /// <summary>ported from: src/compiler/checker.ts:1864 — <c>getESSymbolType</c>.</summary>
    IType ITypeChecker.GetESSymbolType() => _esSymbolType;

    /// <summary>ported from: src/compiler/checker.ts:1865 — <c>getNeverType</c>.</summary>
    IType ITypeChecker.GetNeverType() => _neverType;

    /// <summary>ported from: src/compiler/checker.ts:1866 — <c>getNonPrimitiveType</c>.</summary>
    IType ITypeChecker.GetNonPrimitiveType() => _nonPrimitiveType;

    /// <summary>ported from: src/compiler/checker.ts:1867 — <c>getOptionalType</c>.</summary>
    IType ITypeChecker.GetOptionalType() => _optionalType;

    /// <summary>ported from: src/compiler/checker.ts:1868 — <c>getPromiseType</c>.</summary>
    IType ITypeChecker.GetPromiseType() => GetGlobalPromiseType(/*reportErrors*/ false);

    /// <summary>ported from: src/compiler/checker.ts:1869 — <c>getPromiseLikeType</c>.</summary>
    IType ITypeChecker.GetPromiseLikeType() => GetGlobalPromiseLikeType(/*reportErrors*/ false);

    /// <summary>ported from: src/compiler/checker.ts:1870-1874 — <c>getAnyAsyncIterableType</c>.</summary>
    IType? ITypeChecker.GetAnyAsyncIterableType()
    {
        var type = GetGlobalAsyncIterableType(/*reportErrors*/ false);
        if (ReferenceEquals(type, _emptyGenericType)) return null;
        return CreateTypeReference(type!, [_anyType, _anyType, _anyType]);
    }

    // ---------------------------------------------------------------------------------------------
    // The remaining predicates (checker.ts:1875-1904)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:1875 — <c>isSymbolAccessible</c>.</summary>
    ISymbolAccessibilityResult ITypeChecker.IsSymbolAccessible(ISymbol? symbol, Node? enclosingDeclaration, SymbolFlags meaning, bool shouldComputeAliasToMarkVisible)
        => IsSymbolAccessible(symbol, enclosingDeclaration, meaning, shouldComputeAliasToMarkVisible);

    /// <summary>ported from: src/compiler/checker.ts:1876 — <c>isArrayType</c>.</summary>
    bool ITypeChecker.IsArrayType(IType? type) => IsArrayType(type!);

    /// <summary>ported from: src/compiler/checker.ts:1877 — <c>isTupleType</c>.</summary>
    bool ITypeChecker.IsTupleType(IType? type) => IsTupleType(type!);

    /// <summary>ported from: src/compiler/checker.ts:1878 — <c>isArrayLikeType</c>.</summary>
    bool ITypeChecker.IsArrayLikeType(IType? type) => IsArrayLikeType(type!);

    /// <summary>ported from: src/compiler/checker.ts:1879 — <c>isEmptyAnonymousObjectType</c>.</summary>
    bool ITypeChecker.IsEmptyAnonymousObjectType(IType? type) => IsEmptyAnonymousObjectType(type!);

    /// <summary>ported from: src/compiler/checker.ts:1880 — <c>isTypeInvalidDueToUnionDiscriminant</c>.</summary>
    bool ITypeChecker.IsTypeInvalidDueToUnionDiscriminant(IType? contextualType, MemberExpression? obj)
        => IsTypeInvalidDueToUnionDiscriminant(contextualType, obj);

    /// <summary>ported from: src/compiler/checker.ts:1881 — <c>getExactOptionalProperties</c>.</summary>
    List<ISymbol> ITypeChecker.GetExactOptionalProperties(IType? type) => GetExactOptionalProperties(type!);

    /// <summary>ported from: src/compiler/checker.ts:1882 — <c>getAllPossiblePropertiesOfTypes</c>.</summary>
    List<ISymbol> ITypeChecker.GetAllPossiblePropertiesOfTypes(List<IType>? type) => GetAllPossiblePropertiesOfTypes(type);

    /// <summary>ported from: src/compiler/checker.ts:1883 — <c>getSuggestedSymbolForNonexistentProperty</c>.</summary>
    ISymbol? ITypeChecker.GetSuggestedSymbolForNonexistentProperty(object? name, IType? containingType)
        => GetSuggestedSymbolForNonexistentProperty(name, containingType);

    /// <summary>ported from: src/compiler/checker.ts:1884 — <c>getSuggestedSymbolForNonexistentJSXAttribute</c>.</summary>
    ISymbol? ITypeChecker.GetSuggestedSymbolForNonexistentJSXAttribute(object? name, IType? containingType)
        => GetSuggestedSymbolForNonexistentJSXAttribute(name, containingType);

    /// <summary>ported from: src/compiler/checker.ts:1885 — <c>getSuggestedSymbolForNonexistentSymbol</c>.</summary>
    ISymbol? ITypeChecker.GetSuggestedSymbolForNonexistentSymbol(Node? location, string? name, SymbolFlags meaning)
        => GetSuggestedSymbolForNonexistentSymbol(location, Utilities.EscapeLeadingUnderscores(name ?? ""), meaning);

    /// <summary>ported from: src/compiler/checker.ts:1886 — <c>getSuggestedSymbolForNonexistentModule</c>.</summary>
    ISymbol? ITypeChecker.GetSuggestedSymbolForNonexistentModule(Identifier? node, ISymbol? target)
        => GetSuggestedSymbolForNonexistentModule(node, target);

    /// <summary>ported from: src/compiler/checker.ts:1887 — <c>getSuggestedSymbolForNonexistentClassMember</c>.</summary>
    ISymbol? ITypeChecker.GetSuggestedSymbolForNonexistentClassMember(string? name, IType? baseType)
        => GetSuggestedSymbolForNonexistentClassMember(name, baseType);

    /// <summary>ported from: src/compiler/checker.ts:1888 — <c>getBaseConstraintOfType</c>.</summary>
    IType? ITypeChecker.GetBaseConstraintOfType(IType? type) => GetBaseConstraintOfType(type!);

    /// <summary>ported from: src/compiler/checker.ts:1889 — <c>getDefaultFromTypeParameter</c>.</summary>
    IType? ITypeChecker.GetDefaultFromTypeParameter(IType? type)
        => type is not null && (type.Flags & TypeFlags.TypeParameter) != 0 ? GetDefaultFromTypeParameter((ITypeParameter)type) : null;

    /// <summary>ported from: src/compiler/checker.ts:1890-1892 — <c>resolveName</c>.</summary>
    ISymbol? ITypeChecker.ResolveName(string? name, Node? location, SymbolFlags meaning, bool excludeGlobals)
        => ResolveName(location, Utilities.EscapeLeadingUnderscores(name ?? ""), meaning, /*nameNotFoundMessage*/ null, /*isUse*/ false, excludeGlobals);

    /// <summary>ported from: src/compiler/checker.ts:1893 — <c>getJsxNamespace</c>.</summary>
    string ITypeChecker.GetJsxNamespace(Node? location) => Utilities.UnescapeLeadingUnderscores(GetJsxNamespace(location));

    /// <summary>ported from: src/compiler/checker.ts:1894-1897 — <c>getJsxFragmentFactory</c>.</summary>
    string? ITypeChecker.GetJsxFragmentFactory(Node? location)
    {
        var jsxFragmentFactory = GetJsxFragmentFactoryEntity(location);
        return jsxFragmentFactory is not null ? Utilities.UnescapeLeadingUnderscores(Utilities.GetFirstIdentifier(jsxFragmentFactory).EscapedText ?? "") : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:1898 — <c>getAccessibleSymbolChain</c>.</summary>
    List<ISymbol>? ITypeChecker.GetAccessibleSymbolChain(ISymbol? symbol, Node? enclosingDeclaration, SymbolFlags meaning, bool useOnlyExternalAliasing)
        => GetAccessibleSymbolChain(symbol, enclosingDeclaration, meaning, useOnlyExternalAliasing);

    /// <summary>ported from: src/compiler/checker.ts:1899 — <c>getTypePredicateOfSignature</c>.</summary>
    ITypePredicateBase? ITypeChecker.GetTypePredicateOfSignature(ISignature? signature) => GetTypePredicateOfSignature(signature!);

    /// <summary>ported from: src/compiler/checker.ts:1900-1903 — <c>resolveExternalModuleName</c>.</summary>
    ISymbol? ITypeChecker.ResolveExternalModuleName(Expression? moduleSpecifier)
        => Utilities.GetParseTreeNode(moduleSpecifier, Utilities.IsExpression) is { } specifier
            ? ResolveExternalModuleName((Expression)specifier, (Expression)specifier, /*ignoreErrors*/ true)
            : null;

    /// <summary>ported from: src/compiler/checker.ts:1904 — <c>resolveExternalModuleSymbol</c>.</summary>
    ISymbol ITypeChecker.ResolveExternalModuleSymbol(ISymbol? symbol) => ResolveExternalModuleSymbol(symbol!)!;

    /// <summary>ported from: src/compiler/checker.ts:1905-1908 — <c>tryGetThisTypeAt</c>.</summary>
    IType? ITypeChecker.TryGetThisTypeAt(Node? node, bool? includeGlobalThis, object? container)
        => Utilities.GetParseTreeNode(node) is { } parseNode ? TryGetThisTypeAt(parseNode, includeGlobalThis, container) : null;

    /// <summary>ported from: src/compiler/checker.ts:1909-1912 — <c>getTypeArgumentConstraint</c>.</summary>
    IType? ITypeChecker.GetTypeArgumentConstraint(ITypeNode? node)
        => Utilities.GetParseTreeNode((Node?)node, Utilities.IsTypeNode) is { } typeNode ? GetTypeArgumentConstraint((ITypeNode)typeNode) : null;

    // ---------------------------------------------------------------------------------------------
    // The service entry points whose body the literal writes out (checker.ts:1913-1951)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:1913-1943 — <c>getSuggestionDiagnostics</c>.</summary>
    List<IDiagnosticWithLocation> ITypeChecker.GetSuggestionDiagnostics(SourceFile? file, ICancellationToken? cancellationToken)
    {
        var sourceFile = (SourceFile?)Utilities.GetParseTreeNode(file, NodeTests.IsSourceFile);
        if (sourceFile is null)
        {
            Debug.Fail("Could not determine parsed source file.");
            return [];
        }

        if (Utilities.SkipTypeChecking(sourceFile, _compilerOptions, _host.IsSourceFileDefaultLibrary, ((ISourceFileMayBeEmittedHost)_host).IsSourceOfProjectReferenceRedirect)) return [];

        var diagnostics = new List<IDiagnosticWithLocation>();
        try
        {
            // Record the cancellation token so it can be checked later on during checkSourceElement.
            // Do this in a finally block so we can ensure that it gets reset back to nothing after this call is done.
            _cancellationToken = cancellationToken;

            // Ensure file is type checked, with _eager_ diagnostic production, so identifiers are registered as
            // potentially unused.
            CheckSourceFileWithEagerDiagnostics(sourceFile);
            Debug.Assert((GetNodeLinks(sourceFile).Flags & NodeCheckFlags.TypeChecked) != 0);

            foreach (var diagnostic in _suggestionDiagnostics.GetDiagnostics(sourceFile.FileName))
            {
                diagnostics.Add(diagnostic);
            }

            CheckUnusedIdentifiers(GetPotentiallyUnusedIdentifiers(sourceFile), (Node containingNode, UnusedKind kind, Diagnostic diagnostic) =>
            {
                // PORT-NOTE: TS builds `{ ...diag, category: Suggestion }` by hand; the port builds the same copy,
                // sharing the message and position and overriding only the category — and sharing the deferred message
                // payload (`Diagnostic.ShareMessageText`) rather than formatting it here.
                var suggestion = new Diagnostic(null, DiagnosticCategory.Suggestion, diagnostic.Code, diagnostic.Start, diagnostic.Length, diagnostic.FileName, diagnostic.File)
                {
                    Source = diagnostic.Source,
                    RelatedInformation = diagnostic.RelatedInformation,
                    ReportsUnnecessary = diagnostic.ReportsUnnecessary,
                    ReportsDeprecated = diagnostic.ReportsDeprecated,
                    SkippedOn = diagnostic.SkippedOn,
                    CanonicalHead = diagnostic.CanonicalHead,
                };
                diagnostic.ShareMessageText(suggestion);
                diagnostics.Add(suggestion);
            });

            return diagnostics;
        }
        finally
        {
            _cancellationToken = null;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:1944-1951 — <c>runWithCancellationToken</c>.</summary>
    T ITypeChecker.RunWithCancellationToken<T>(ICancellationToken? token, Func<ITypeChecker, T>? callback)
    {
        try
        {
            _cancellationToken = token;
            return callback is not null ? callback(this) : default!;
        }
        finally
        {
            _cancellationToken = null;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The tail of the literal (checker.ts:1952-1996)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:1952 — <c>getLocalTypeParametersOfClassOrInterfaceOrTypeAlias</c>.</summary>
    List<ITypeParameter>? ITypeChecker.GetLocalTypeParametersOfClassOrInterfaceOrTypeAlias(ISymbol? symbol)
        => GetLocalTypeParametersOfClassOrInterfaceOrTypeAlias(symbol!);

    /// <summary>ported from: src/compiler/checker.ts:1953 — <c>isDeclarationVisible</c>.</summary>
    bool ITypeChecker.IsDeclarationVisible(Node? node) => IsDeclarationVisible(node);

    /// <summary>ported from: src/compiler/checker.ts:1954 — <c>isPropertyAccessible</c>.</summary>
    bool ITypeChecker.IsPropertyAccessible(Node? node, bool isSuper, bool isWrite, IType? containingType, ISymbol? property)
        => IsPropertyAccessible(node, isSuper, isWrite, containingType, property);

    /// <summary>ported from: src/compiler/checker.ts:1955 — <c>getTypeOnlyAliasDeclaration</c>.</summary>
    Node? ITypeChecker.GetTypeOnlyAliasDeclaration(ISymbol? symbol) => GetTypeOnlyAliasDeclaration(symbol);

    /// <summary>ported from: src/compiler/checker.ts:1956 — <c>getMemberOverrideModifierStatus</c>.</summary>
    MemberOverrideStatus ITypeChecker.GetMemberOverrideModifierStatus(Node? node, IClassElement? member, ISymbol? memberSymbol)
        => GetMemberOverrideModifierStatus((IClassLikeDeclarationBase)node!, member!, memberSymbol!);

    /// <summary>ported from: src/compiler/checker.ts:1957 — <c>isTypeParameterPossiblyReferenced</c>.</summary>
    bool ITypeChecker.IsTypeParameterPossiblyReferenced(ITypeParameter? tp, Node? node) => IsTypeParameterPossiblyReferenced(tp!, node!);

    /// <summary>ported from: src/compiler/checker.ts:1958 — <c>typeHasCallOrConstructSignatures</c>.</summary>
    bool ITypeChecker.TypeHasCallOrConstructSignatures(IType? type) => TypeHasCallOrConstructSignatures(type);

    /// <summary>ported from: src/compiler/checker.ts:1959 — <c>getSymbolFlags</c>.</summary>
    SymbolFlags ITypeChecker.GetSymbolFlags(ISymbol? symbol) => GetSymbolFlags(symbol!);

    /// <summary>ported from: src/compiler/checker.ts:1960-1961 — <c>getTypeArgumentsForResolvedSignature</c>.</summary>
    List<IType>? ITypeChecker.GetTypeArgumentsForResolvedSignature(ISignature? signature) => GetTypeArgumentsForResolvedSignature(signature!);

    /// <summary>ported from: src/compiler/checker.ts:1962 — <c>isLibType</c>.</summary>
    bool ITypeChecker.IsLibType(IType? type) => IsLibType(type);
}
