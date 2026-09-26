namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:41596-43055 (hasInferenceCandidates … checkNamedTupleMember)
/// <summary>
/// The expression and declaration checking surface of the checker: <c>checkExpression</c> and its worker, the
/// inference bookkeeping the call-resolution block consults, and the <c>check*</c> members the declaration walker
/// calls for type parameters, parameters, signatures, class members and type nodes.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS's <c>checkExpression</c>/<c>checkExpressionWorker</c> take the union <c>Expression | QualifiedName</c>.
/// No narrower C# type is common to both arms (the port's <c>QualifiedName</c> is not an <c>Expression</c>), and the
/// landed callers already pass a <see cref="Node"/>-typed name (<c>Checker.IndexAccess.cs</c>,
/// <c>GetLiteralTypeFromPropertyName</c>), so the parameter is <see cref="Node"/> — the same anchorless-union
/// convention <c>Utilities.GetFirstIdentifier</c> and the optional-chain helpers of <c>Utilities.Binder.cs</c> use.
/// TS's overloads collapse into the optional <c>checkMode</c>/<c>forceTuple</c> parameters of the one member.
/// </para>
/// <para>
/// PORT-NOTE: the <c>CallChain</c> parameter of <c>getReturnTypeOfSingleNonGenericSignatureOfCallChain</c> is the
/// <see cref="CallExpression"/> class: the port's chain views (<c>CallChain</c>, <c>PropertyAccessChain</c>,
/// <c>ElementAccessChain</c>) are abstract views the factory never instantiates, and <c>Utilities.IsCallChain</c> —
/// the guard at the only call site — admits exactly a call expression carrying <c>NodeFlags.OptionalChain</c>
/// (<c>Utilities.Binder.cs</c> records the same ruling for the optional-chain predicates).
/// </para>
/// <para>
/// PORT-NOTE: TS's <c>tracing?.push</c>/<c>tracing?.pop</c> pair becomes <c>Tracing.Current?.Push</c>/<c>Pop</c>
/// (<c>Core/Tracing.cs</c>), and the <c>(node as TracingNode).tracingPath</c> read becomes
/// <see cref="Node.TracingPath"/>, the slot the port's binder writes (<c>Model/Node.cs</c>).
/// </para>
/// <para>
/// PORT-NOTE: <c>LanguageFeatureMinimumTarget</c> (types.ts:8489-8524) is not ported. The entries this file reads are
/// written as the <c>ScriptTarget</c> constants that table maps them to — <c>Generators</c> = ES2015,
/// <c>AsyncFunctions</c> = ES2017, <c>AsyncGenerators</c> = ES2018, <c>PrivateNamesAndClassStaticBlocks</c> = ES2022,
/// <c>ClassAndClassElementDecorators</c> = ESNext — which is how <c>Lexer/Scanner.cs</c> already inlines the
/// regular-expression-flag entries of the same table. The two members that read it say so individually.
/// </para>
/// <para>
/// PORT-NOTE: TS spells several unions the port has no type for (<c>BindingPattern</c>, <c>SignatureDeclaration</c>,
/// <c>EntityNameOrEntityNameExpression</c>, <c>AssertionExpression</c>, <c>CallChain</c>), so those parameters are
/// <see cref="Node"/> and every member read is a kind-first class cast — the ruling <c>Utilities.IsSuperProperty</c>
/// records for the same problem. The exceptions are the unions an instantiated class already anchors: a
/// <c>CallChain</c> is a <see cref="CallExpression"/>, an <c>AssertionExpression</c> is a kind switch over
/// <see cref="TypeAssertion"/>/<see cref="AsExpression"/>.
/// </para>
/// <para>
/// PORT-NOTE: this band is landed against a tree in which the members it calls from sibling slices are still absent,
/// so several calls are red by design; each one is named with its owner in the slice report. No member here is
/// stubbed, and no name owned by another band is declared.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // The inference bookkeeping (checker.ts:41596-41663)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:41596-41598 — <c>hasInferenceCandidates</c>.</summary>
    internal static bool HasInferenceCandidates(IInferenceInfo info)
        => info.Candidates is not null || info.ContraCandidates is not null;

    /// <summary>ported from: src/compiler/checker.ts:41600-41602 — <c>hasInferenceCandidatesOrDefault</c>.</summary>
    internal static bool HasInferenceCandidatesOrDefault(IInferenceInfo info)
        => info.Candidates is not null || info.ContraCandidates is not null || HasTypeParameterDefault(info.TypeParameter!);

    /// <summary>ported from: src/compiler/checker.ts:41604-41611 — <c>hasOverlappingInferences</c>.</summary>
    internal static bool HasOverlappingInferences(List<IInferenceInfo> a, List<IInferenceInfo> b)
    {
        for (var i = 0; i < a.Count; i++)
        {
            if (HasInferenceCandidates(a[i]) && HasInferenceCandidates(b[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:41613-41619 — <c>mergeInferences</c>.</summary>
    internal static void MergeInferences(List<IInferenceInfo> target, List<IInferenceInfo> source)
    {
        for (var i = 0; i < target.Count; i++)
        {
            if (!HasInferenceCandidates(target[i]) && HasInferenceCandidates(source[i]))
            {
                target[i] = source[i];
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:41621-41647 — <c>getUniqueTypeParameters</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>concatenate</c>/<c>append</c> return fresh arrays; the port's <c>Core.Concatenate</c> and
    /// <c>Core.Append</c> answer an <c>IList&lt;T&gt;</c> whose runtime type is the <c>List&lt;T&gt;</c> the loop keeps
    /// appending to, which is why the two locals carry the cast <c>Core.Append</c>'s callers already use elsewhere
    /// (<c>Checker.Inference.cs:389</c>).
    /// </remarks>
    internal List<ITypeParameter> GetUniqueTypeParameters(IInferenceContext context, List<ITypeParameter> typeParameters)
    {
        var result = new List<ITypeParameter>();
        List<ITypeParameter>? oldTypeParameters = null;
        List<ITypeParameter>? newTypeParameters = null;
        foreach (var tp in typeParameters)
        {
            var name = tp.Symbol!.EscapedName!;
            if (HasTypeParameterByName(context.InferredTypeParameters, name) || HasTypeParameterByName(result, name))
            {
                var newName = GetUniqueTypeParameterName(Core.Concatenate(context.InferredTypeParameters, result)!, name);
                var symbol = CreateSymbol(SymbolFlags.TypeParameter, newName);
                var newTypeParameter = CreateTypeParameter(symbol);
                newTypeParameter.Target = tp;
                oldTypeParameters = (List<ITypeParameter>)Core.Append(oldTypeParameters, tp)!;
                newTypeParameters = (List<ITypeParameter>)Core.Append(newTypeParameters, newTypeParameter)!;
                result.Add(newTypeParameter);
            }
            else
            {
                result.Add(tp);
            }
        }

        if (newTypeParameters is not null)
        {
            var mapper = CreateTypeMapper(oldTypeParameters!, new List<IType>(newTypeParameters));
            foreach (var tp in newTypeParameters)
            {
                tp.Mapper = mapper;
            }
        }

        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:41649-41651 — <c>hasTypeParameterByName</c>.</summary>
    internal static bool HasTypeParameterByName(IList<ITypeParameter>? typeParameters, string name)
        => Core.Some(typeParameters, tp => tp.Symbol!.EscapedName == name);

    /// <summary>ported from: src/compiler/checker.ts:41653-41663 — <c>getUniqueTypeParameterName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>baseName.charCodeAt(len - 1)</c> is the port's <c>string</c> indexer, whose <c>char</c>
    /// compares against <c>CharacterCodes</c>' <c>int</c> constants as the rest of the port already does
    /// (<c>Core/Core.Strings.cs</c>); <c>s + index as __String</c> is <c>s + index</c>, a <c>string</c>.
    /// </remarks>
    internal static string GetUniqueTypeParameterName(IList<ITypeParameter> typeParameters, string baseName)
    {
        var len = baseName.Length;
        while (len > 1 && baseName[len - 1] >= CharacterCodes._0 && baseName[len - 1] <= CharacterCodes._9)
        {
            len--;
        }

        var s = baseName[..len];
        for (var index = 1; ; index++)
        {
            var augmentedName = s + index;
            if (!HasTypeParameterByName(typeParameters, augmentedName))
            {
                return augmentedName;
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:41665-41670 — <c>getReturnTypeOfSingleNonGenericCallSignature</c>.</summary>
    internal IType? GetReturnTypeOfSingleNonGenericCallSignature(IType funcType)
    {
        var signature = GetSingleCallSignature(funcType);
        if (signature is not null && signature.TypeParameters is null)
        {
            return GetReturnTypeOfSignature(signature);
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:41672-41677 — <c>getReturnTypeOfSingleNonGenericSignatureOfCallChain</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: see the class remarks for why the <c>CallChain</c> union is the <see cref="CallExpression"/> class
    /// here; <c>nonOptionalType !== funcType</c> is reference identity, the reference's own object comparison.
    /// </remarks>
    internal IType? GetReturnTypeOfSingleNonGenericSignatureOfCallChain(CallExpression expr)
    {
        var funcType = CheckExpression(expr.Expression!);
        var nonOptionalType = GetOptionalExpressionType(funcType, expr.Expression!);
        var returnType = GetReturnTypeOfSingleNonGenericCallSignature(funcType);
        return returnType is not null ? PropagateOptionalTypeMarker(returnType, expr, !ReferenceEquals(nonOptionalType, funcType)) : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:41683-41705 — <c>getTypeOfExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>flowTypeCache</c> (checker.ts:2328) and <c>flowInvocationCount</c> (checker.ts:2325) are the
    /// flow-analysis block's state and are not landed yet; the port names them <c>_flowTypeCache</c>/
    /// <c>_flowInvocationCount</c> after the class's slot convention, and both are named in the slice report. The
    /// <c>cache[nodeId] = type</c> write is what makes the slot a node-id-indexed table rather than a
    /// <c>List&lt;T&gt;</c> of one entry per node position.
    /// <para>
    /// PORT-NOTE: the reference declares <c>node: Expression</c> but is called with a <c>QualifiedName</c> — the two
    /// casts at checker.ts:50394 (<c>node as Expression</c>) and checker.ts:41750 are type-level lies its own
    /// <c>checkExpression(node: Expression | QualifiedName)</c> (checker.ts:41737) admits, and
    /// <c>isExpressionNode</c> (utilities.ts:3677-3680) answers true for a qualified name under a type query. The port
    /// has no such lie available: <see cref="QualifiedName"/> is not an <see cref="Expression"/>, so the parameter is
    /// <see cref="Node"/> and the body reads kind-guarded slots, the shape <c>GAVEUP</c> at
    /// <c>Checker.Check.Source.cs:1440</c> used to trip on (InvalidCastException, 21 corpus files).
    /// </para>
    /// </remarks>
    internal IType GetTypeOfExpression(Node node)
    {
        // Don't bother caching types that require no flow analysis and are quick to compute.
        var quickType = GetQuickTypeOfExpression(node);
        if (quickType is not null)
        {
            return quickType;
        }

        // If a type has been cached for the node, return it.
        if ((node.Flags & NodeFlags.TypeCached) != 0 && _flowTypeCache is not null)
        {
            // PORT-NOTE: `flowTypeCache[getNodeId(node)]` is a sparse-array read in the reference (checker.ts:41691):
            // an index the cache has not reached is `undefined`, not a failure. The port's list is grown on write below
            // and the read is guarded here, which is the same two answers without the throw a list index would raise.
            var nodeId = Utilities.GetNodeId(node);
            var cachedType = nodeId < _flowTypeCache.Count ? _flowTypeCache[nodeId] : null;
            if (cachedType is not null)
            {
                return cachedType;
            }
        }

        var startInvocationCount = _flowInvocationCount;
        var type = CheckExpression(node, CheckMode.TypeOnly);
        // If control flow analysis was required to determine the type, it is worth caching.
        if (_flowInvocationCount != startInvocationCount)
        {
            var cache = _flowTypeCache ??= [];
            // checker.ts:41701 — `cache[getNodeId(node)] = type` on a JS array extends it, filling the gap with holes;
            // the port's list fills with nulls, which is what the guarded read above reports.
            var nodeId = Utilities.GetNodeId(node);
            while (cache.Count <= nodeId)
            {
                cache.Add(null);
            }

            cache[nodeId] = type;
            Utilities.SetNodeFlags(node, node.Flags | NodeFlags.TypeCached);
        }

        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:41707-41733 — <c>getQuickTypeOfExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>getJSDocTypeAssertionType</c> belongs to the factory utilities beside the landed
    /// <c>IsJSDocTypeAssertion</c> (<c>Parser/FactoryUtilities.cs:509</c>, factory/utilities.ts:624) and is not ported
    /// yet, so <c>FactoryUtilities.GetJSDocTypeAssertionType</c> is red until that slice lands it.
    /// <c>isSymbolOrSymbolForCall</c> (checker.ts:37915) is a sibling checker member, called unqualified.
    /// <para>
    /// PORT-NOTE: <c>node: Expression</c> is widened to <see cref="Node"/> by the same argument
    /// <see cref="GetTypeOfExpression"/> records (checker.ts:41707).
    /// </para>
    /// </remarks>
    internal IType? GetQuickTypeOfExpression(Node node)
    {
        var expr = Utilities.SkipParentheses(node, /*excludeJSDocTypeAssertions*/ true);
        if (FactoryUtilities.IsJSDocTypeAssertion(expr))
        {
            var type = FactoryUtilities.GetJSDocTypeAssertionType(expr);
            if (!Utilities.IsConstTypeReference((Node)type))
            {
                return GetTypeFromTypeNode(type);
            }
        }

        expr = Utilities.SkipParentheses(node);
        if (NodeTests.IsAwaitExpression(expr))
        {
            var type = GetQuickTypeOfExpression(((AwaitExpression)expr).Expression!);
            return type is not null ? GetAwaitedType(type) : null;
        }

        // Optimize for the common case of a call to a function with a single non-generic call
        // signature where we can just fetch the return type without checking the arguments.
        if (NodeTests.IsCallExpression(expr) && ((CallExpression)expr).Expression!.Kind != SyntaxKind.SuperKeyword && !Utilities.IsRequireCall(expr, /*requireStringLiteralLikeArgument*/ true) && !IsSymbolOrSymbolForCall(expr) && !Utilities.IsImportCall(expr))
        {
            return Utilities.IsCallChain(expr)
                ? GetReturnTypeOfSingleNonGenericSignatureOfCallChain((CallExpression)expr)
                : GetReturnTypeOfSingleNonGenericCallSignature(CheckNonNullExpression(((CallExpression)expr).Expression!));
        }
        else if (Utilities.IsAssertionExpression(expr) && !Utilities.IsConstTypeReference((Node)AssertionExpressionType(expr)))
        {
            return GetTypeFromTypeNode(AssertionExpressionType(expr));
        }
        else if (Utilities.IsLiteralExpression(node) || Utilities.IsBooleanLiteral(node))
        {
            return CheckExpression(node);
        }

        return null;
    }

    /// <summary>The <c>type</c> a <c>TypeAssertionExpression</c> or an <c>AsExpression</c> carries.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads <c>expr.type</c> off the <c>AssertionExpression</c> union, whose two arms both declare the
    /// member; the port has no such anchor, so the read is the kind switch <c>Utilities.IsSuperProperty</c> uses for
    /// the same shape (and the two arms are the whole of <c>Utilities.IsAssertionExpression</c>'s answer, which is why
    /// the second arm is a cast rather than a default).
    /// </remarks>
    private static ITypeNode AssertionExpressionType(Node expr)
        => expr.Kind == SyntaxKind.TypeAssertionExpression ? ((TypeAssertion)expr).Type! : ((AsExpression)expr).Type!;

    /// <summary>ported from: src/compiler/checker.ts:41742-41751 — <c>getContextFreeTypeOfExpression</c>.</summary>
    internal IType GetContextFreeTypeOfExpression(Expression node)
    {
        var links = GetNodeLinks(node);
        if (links.ContextFreeType is not null)
        {
            return links.ContextFreeType;
        }

        PushContextualType(node, _anyType, /*isCache*/ false);
        var type = links.ContextFreeType = CheckExpression(node, CheckMode.SkipContextSensitive);
        PopContextualType();
        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:41753-41766 — <c>checkExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>node: Expression | QualifiedName</c> is the <see cref="Node"/> the class remarks explain;
    /// <c>checkMode</c>/<c>forceTuple</c> are TS's optional parameters of the one merged member (the <c>!s</c> are the
    /// reference's own non-null assertions on the calls whose answers TS types as non-optional).
    /// </remarks>
    internal IType CheckExpression(Node node, CheckMode checkMode = CheckMode.Normal, bool forceTuple = false)
    {
        Tracing.Current?.Push(
            Tracing.Phase.Check,
            "checkExpression",
            TracingArgs.Of(
                ("kind", (int)node.Kind),
                ("pos", node.Pos),
                ("end", node.End),
                ("path", node.TracingPath)));
        var saveCurrentNode = _currentNode;
        _currentNode = node;
        _instantiationCount = 0;
        var uninstantiatedType = CheckExpressionWorker(node, checkMode, forceTuple);
        var type = InstantiateTypeWithSingleGenericCallSignature(node, uninstantiatedType, checkMode)!;
        if (IsConstEnumObjectType(type))
        {
            CheckConstEnumAccess(node, type);
        }

        _currentNode = saveCurrentNode;
        Tracing.Current?.Pop();
        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:41768-41806 — <c>checkConstEnumAccess</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the <c>ok</c> expression reads <c>.expression</c> through the kind test that precedes each read
    /// rather than through TS's <c>PropertyAccessExpression | ElementAccessExpression</c> union cast — the ruling
    /// <c>Utilities.IsSuperProperty</c> records for exactly this code. <c>isValidTypeOnlyAliasUseSite</c>
    /// (utilities.ts:10579) is <c>Utilities.IsValidTypeOnlyAliasUseSite</c>, landed.
    /// </remarks>
    internal void CheckConstEnumAccess(Node node, IType type)
    {
        // enum object type for const enums are only permitted in:
        // - 'left' in property access
        // - 'object' in indexed access
        // - target in rhs of import statement
        var parent = node.Parent!;
        var ok =
            (parent.Kind == SyntaxKind.PropertyAccessExpression && ReferenceEquals(((PropertyAccessExpression)parent).Expression, node))
            || (parent.Kind == SyntaxKind.ElementAccessExpression && ReferenceEquals(((ElementAccessExpression)parent).Expression, node))
            || ((node.Kind == SyntaxKind.Identifier || node.Kind == SyntaxKind.QualifiedName)
                    && IsInRightSideOfImportOrExportAssignment(node)
                || (parent.Kind == SyntaxKind.TypeQuery && ReferenceEquals(((TypeQueryNode)parent).ExprName, node)))
            || (parent.Kind == SyntaxKind.ExportSpecifier); // We allow reexporting const enums

        if (!ok)
        {
            Error(node, Diagnostics.const_enums_can_only_be_used_in_property_or_index_access_expressions_or_the_right_hand_side_of_an_import_declaration_or_export_assignment_or_type_query);
        }

        // --verbatimModuleSyntax only gets checked here when the enum usage does not
        // resolve to an import, because imports of ambient const enums get checked
        // separately in 'checkAliasSymbol'.
        if (
            Js.IsTruthy(_compilerOptions.IsolatedModules)
            || Js.IsTruthy(_compilerOptions.VerbatimModuleSyntax)
                && ok
                && ResolveName(
                    node,
                    Utilities.GetFirstIdentifier(node).EscapedText!,
                    SymbolFlags.Alias,
                    /*nameNotFoundMessage*/ null,
                    /*isUse*/ false,
                    /*excludeGlobals*/ true) is null
        )
        {
            Debug.Assert((type.Symbol!.Flags & SymbolFlags.ConstEnum) != 0);
            var constEnumDeclaration = (EnumDeclaration)type.Symbol.ValueDeclaration!;
            var redirect = _host.GetRedirectFromOutput(Utilities.GetSourceFileOfNode(constEnumDeclaration)!.ResolvedPath)?.ResolvedRef;
            if ((constEnumDeclaration.Flags & NodeFlags.Ambient) != 0 && !Utilities.IsValidTypeOnlyAliasUseSite(node) && (redirect is null || !Utilities.ShouldPreserveConstEnums(redirect.CommandLine!.Options)))
            {
                Error(node, Diagnostics.Cannot_access_ambient_const_enums_when_0_is_enabled, _isolatedModulesLikeFlagName);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:41808-41818 — <c>checkParenthesizedExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>getJSDocSatisfiesExpressionType</c> (utilities.ts:11116) is
    /// <c>Utilities.GetJSDocSatisfiesExpressionType</c> and <c>isJSDocTypeAssertion</c> is
    /// <c>FactoryUtilities.IsJSDocTypeAssertion</c>; <c>hasJSDocNodes</c>, <c>isJSDocSatisfiesExpression</c>,
    /// <c>checkSatisfiesExpressionWorker</c> and <c>checkAssertionWorker</c> belong to earlier bands of the checker and
    /// are called unqualified. The <c>checkMode ?? CheckMode.Normal</c> is TS's <c>undefined</c> check mode, which
    /// every consumer of <c>CheckMode</c> reads through a bit test that treats as <c>Normal</c>.
    /// </remarks>
    internal IType CheckParenthesizedExpression(ParenthesizedExpression node, CheckMode? checkMode = null)
    {
        if (Utilities.HasJSDocNodes(node))
        {
            if (Utilities.IsJSDocSatisfiesExpression(node))
            {
                return CheckSatisfiesExpressionWorker(node.Expression!, Utilities.GetJSDocSatisfiesExpressionType(node), checkMode);
            }

            if (FactoryUtilities.IsJSDocTypeAssertion(node))
            {
                return CheckAssertionWorker(node, checkMode);
            }
        }

        return CheckExpression(node.Expression!, checkMode ?? CheckMode.Normal);
    }

    /// <summary>ported from: src/compiler/checker.ts:41820-41940 — <c>checkExpressionWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>fall through</c> from <c>CallExpression</c> to <c>NewExpression</c> is
    /// <c>goto case</c>; the <c>Debug.fail</c> arm is <c>Debug.Fail</c>, which the port marks
    /// <c>[DoesNotReturn]</c>, so C# accepts it as a whole arm (the trailing <c>break</c> states the exit of that arm
    /// that TS's <c>never</c> makes structural). Every <c>case</c> order and every cast is the reference's.
    /// </remarks>
    internal IType CheckExpressionWorker(Node node, CheckMode? checkMode = null, bool forceTuple = false)
    {
        var kind = node.Kind;
        if (_cancellationToken is not null)
        {
            // Only bother checking on a few construct kinds.  We don't want to be excessively
            // hitting the cancellation token on every node we check.
            switch (kind)
            {
                case SyntaxKind.ClassExpression:
                case SyntaxKind.FunctionExpression:
                case SyntaxKind.ArrowFunction:
                    _cancellationToken.ThrowIfCancellationRequested();
                    break;
            }
        }

        switch (kind)
        {
            case SyntaxKind.Identifier:
                return CheckIdentifier((Identifier)node, checkMode);
            case SyntaxKind.PrivateIdentifier:
                return CheckPrivateIdentifierExpression((PrivateIdentifier)node);
            case SyntaxKind.ThisKeyword:
                return CheckThisExpression(node);
            case SyntaxKind.SuperKeyword:
                return CheckSuperExpression(node);
            case SyntaxKind.NullKeyword:
                return _nullWideningType;
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.StringLiteral:
                return HasSkipDirectInferenceFlag(node)
                    ? _blockedStringType
                    : GetFreshTypeOfLiteralType(GetStringLiteralType(((LiteralExpression)node).Text!));
            case SyntaxKind.NumericLiteral:
                CheckGrammarNumericLiteral((NumericLiteral)node);
                return GetFreshTypeOfLiteralType(GetNumberLiteralType(Js.ToNumber(((NumericLiteral)node).Text!)));
            case SyntaxKind.BigIntLiteral:
                CheckGrammarBigIntLiteral((BigIntLiteral)node);
                return GetFreshTypeOfLiteralType(GetBigIntLiteralType(new Utilities.PseudoBigIntValue
                {
                    Negative = false,
                    Base10Value = PseudoBigInt.Parse(((BigIntLiteral)node).Text!),
                }));
            case SyntaxKind.TrueKeyword:
                return _trueType;
            case SyntaxKind.FalseKeyword:
                return _falseType;
            case SyntaxKind.TemplateExpression:
                return CheckTemplateExpression((TemplateExpression)node);
            case SyntaxKind.RegularExpressionLiteral:
                return CheckRegularExpressionLiteral((RegularExpressionLiteral)node);
            case SyntaxKind.ArrayLiteralExpression:
                return CheckArrayLiteral((ArrayLiteralExpression)node, checkMode, forceTuple);
            case SyntaxKind.ObjectLiteralExpression:
                return CheckObjectLiteral((ObjectLiteralExpression)node, checkMode);
            case SyntaxKind.PropertyAccessExpression:
                return CheckPropertyAccessExpression((PropertyAccessExpression)node, checkMode);
            case SyntaxKind.QualifiedName:
                return CheckQualifiedName((QualifiedName)node, checkMode);
            case SyntaxKind.ElementAccessExpression:
                return CheckIndexedAccess((ElementAccessExpression)node, checkMode);
            case SyntaxKind.CallExpression:
                if (Utilities.IsImportCall(node))
                {
                    return CheckImportCallExpression((CallExpression)node);
                }

                // falls through
                goto case SyntaxKind.NewExpression;
            case SyntaxKind.NewExpression:
                // Reference (checker.ts:37810): `checkCallExpression(node: CallExpression | NewExpression, checkMode)`.
                // `NewExpression extends PrimaryExpression` and `CallExpression extends LeftHandSideExpression` in TS,
                // so the reference's cast is structural: the two kinds share `expression`/`typeArguments`/`arguments`
                // and `questionDotToken` reads `undefined` on a new-expression. C# cannot cast between the sibling
                // classes, so the node is handed to the same union-typed entry point, which reads those slots per class.
                return CheckCallExpression(node, checkMode);
            case SyntaxKind.TaggedTemplateExpression:
                return CheckTaggedTemplateExpression((TaggedTemplateExpression)node);
            case SyntaxKind.ParenthesizedExpression:
                return CheckParenthesizedExpression((ParenthesizedExpression)node, checkMode);
            case SyntaxKind.ClassExpression:
                return CheckClassExpression((ClassExpression)node);
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ArrowFunction:
                return CheckFunctionExpressionOrObjectLiteralMethod(node, checkMode ?? CheckMode.Normal);
            case SyntaxKind.TypeOfExpression:
                return CheckTypeOfExpression((TypeOfExpression)node);
            case SyntaxKind.TypeAssertionExpression:
            case SyntaxKind.AsExpression:
                return CheckAssertion(node, checkMode);
            case SyntaxKind.NonNullExpression:
                return CheckNonNullAssertion((NonNullExpression)node);
            case SyntaxKind.ExpressionWithTypeArguments:
                return CheckExpressionWithTypeArguments((ExpressionWithTypeArguments)node);
            case SyntaxKind.SatisfiesExpression:
                return CheckSatisfiesExpression((SatisfiesExpression)node);
            case SyntaxKind.MetaProperty:
                return CheckMetaProperty((MetaProperty)node);
            case SyntaxKind.DeleteExpression:
                return CheckDeleteExpression((DeleteExpression)node);
            case SyntaxKind.VoidExpression:
                return CheckVoidExpression((VoidExpression)node);
            case SyntaxKind.AwaitExpression:
                return CheckAwaitExpression((AwaitExpression)node);
            case SyntaxKind.PrefixUnaryExpression:
                return CheckPrefixUnaryExpression((PrefixUnaryExpression)node);
            case SyntaxKind.PostfixUnaryExpression:
                return CheckPostfixUnaryExpression((PostfixUnaryExpression)node);
            case SyntaxKind.BinaryExpression:
                return CheckBinaryExpression((BinaryExpression)node, checkMode ?? CheckMode.Normal);
            case SyntaxKind.ConditionalExpression:
                return CheckConditionalExpression((ConditionalExpression)node, checkMode ?? CheckMode.Normal);
            case SyntaxKind.SpreadElement:
                return CheckSpreadExpression((SpreadElement)node, checkMode);
            case SyntaxKind.OmittedExpression:
                return _undefinedWideningType;
            case SyntaxKind.YieldExpression:
                return CheckYieldExpression((YieldExpression)node);
            case SyntaxKind.SyntheticExpression:
                return CheckSyntheticExpression((SyntheticExpression)node);
            case SyntaxKind.JsxExpression:
                return CheckJsxExpression((JsxExpression)node, checkMode);
            case SyntaxKind.JsxElement:
                return CheckJsxElement((JsxElement)node, checkMode);
            case SyntaxKind.JsxSelfClosingElement:
                return CheckJsxSelfClosingElement((JsxSelfClosingElement)node, checkMode);
            case SyntaxKind.JsxFragment:
                return CheckJsxFragment((JsxFragment)node);
            case SyntaxKind.JsxAttributes:
                return CheckJsxAttributes((JsxAttributes)node, checkMode);
            case SyntaxKind.JsxOpeningElement:
                Debug.Fail("Shouldn't ever directly check a JsxOpeningElement");
                break;
        }

        return _errorType;
    }

    // ---------------------------------------------------------------------------------------------
    // DECLARATION AND STATEMENT TYPE CHECKING (checker.ts:41944-42666)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:41944-41966 — <c>checkTypeParameter</c>.</summary>
    internal void CheckTypeParameter(TypeParameterDeclaration node)
    {
        // Grammar Checking
        CheckGrammarModifiers(node);
        if (node.Expression is not null)
        {
            GrammarErrorOnFirstToken(node.Expression, Diagnostics.Type_expected);
        }

        CheckSourceElement(node.Constraint);
        CheckSourceElement(node.Default);
        var typeParameter = GetDeclaredTypeOfTypeParameter(GetSymbolOfDeclaration(node));
        // Resolve base constraint to reveal circularity errors
        GetBaseConstraintOfType(typeParameter);
        if (!HasNonCircularTypeParameterDefault(typeParameter))
        {
            // PORT-NOTE (A″): the argument is a DiagnosticArgument (Core/Diagnostic.cs) so the printer-gated stringifier runs on the first read of the message text.
            Error((Node?)node.Default, Diagnostics.Type_parameter_0_has_a_circular_default, Diagnostic.DeferArg(() => TypeToString(typeParameter)));
        }

        var constraintType = GetConstraintOfTypeParameter(typeParameter);
        var defaultType = GetDefaultFromTypeParameter(typeParameter);
        if (constraintType is not null && defaultType is not null)
        {
            CheckTypeAssignableTo(
                defaultType,
                GetTypeWithThisArgument(InstantiateType(constraintType, MakeUnaryTypeMapper(typeParameter, defaultType))!, defaultType),
                (Node?)node.Default,
                Diagnostics.Type_0_does_not_satisfy_the_constraint_1);
        }

        CheckNodeDeferred(node);
        AddLazyDiagnostic(() => CheckTypeNameIsReserved(node.Name!, Diagnostics.Type_parameter_name_cannot_be_0));
    }

    /// <summary>ported from: src/compiler/checker.ts:41968-41989 — <c>checkTypeParameterDeferred</c>.</summary>
    internal void CheckTypeParameterDeferred(TypeParameterDeclaration node)
    {
        if (NodeTests.IsInterfaceDeclaration(node.Parent!) || Utilities.IsClassLike(node.Parent!) || NodeTests.IsTypeAliasDeclaration(node.Parent!))
        {
            var typeParameter = GetDeclaredTypeOfTypeParameter(GetSymbolOfDeclaration(node));
            var modifiers = GetTypeParameterModifiers(typeParameter) & (ModifierFlags.In | ModifierFlags.Out);
            if (modifiers != 0)
            {
                var symbol = GetSymbolOfDeclaration((IDeclaration)node.Parent!);
                if (NodeTests.IsTypeAliasDeclaration(node.Parent!) && (GetObjectFlags(GetDeclaredTypeOfSymbol(symbol)) & (ObjectFlags.Anonymous | ObjectFlags.Mapped)) == 0)
                {
                    Error(node, Diagnostics.Variance_annotations_are_only_supported_in_type_aliases_for_object_function_constructor_and_mapped_types);
                }
                else if (modifiers == ModifierFlags.In || modifiers == ModifierFlags.Out)
                {
                    Tracing.Current?.Push(
                        Tracing.Phase.CheckTypes,
                        "checkTypeParameterDeferred",
                        TracingArgs.Of(
                            ("parent", GetTypeId(GetDeclaredTypeOfSymbol(symbol))),
                            ("id", GetTypeId(typeParameter))));
                    var source = CreateMarkerType(symbol, typeParameter, modifiers == ModifierFlags.Out ? _markerSubTypeForCheck : _markerSuperTypeForCheck);
                    var target = CreateMarkerType(symbol, typeParameter, modifiers == ModifierFlags.Out ? _markerSuperTypeForCheck : _markerSubTypeForCheck);
                    var saveVarianceTypeParameter = _varianceTypeParameter;
                    _varianceTypeParameter = typeParameter;
                    CheckTypeAssignableTo(source, target, node, Diagnostics.Type_0_is_not_assignable_to_type_1_as_implied_by_variance_annotation);
                    _varianceTypeParameter = saveVarianceTypeParameter;
                    Tracing.Current?.Pop();
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:41991-42034 — <c>checkParameter</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>getContainingFunction</c> is <c>Utilities.GetContainingFunction</c> (utilities.ts:3138) and
    /// answers <see cref="Node"/>; the reference's <c>FunctionLikeDeclaration</c> view is read through
    /// <see cref="ISignatureDeclarationBase"/> for <c>parameters</c> and <see cref="IFunctionLikeDeclarationBase"/> for
    /// <c>body</c> — with the constructor arm reading <see cref="ConstructorDeclaration.Body"/> directly, so a
    /// signature in the parameter's parent chain (which is one of the seven kinds that carry parameters but not a
    /// body) cannot make the read throw. <c>isOptionalDeclaration</c> (utilities.ts:11088) is
    /// <c>Utilities.IsOptionalDeclaration</c> over the <see cref="IDeclaration"/> face of the parameter.
    /// </remarks>
    internal void CheckParameter(ParameterDeclaration node)
    {
        // Grammar checking
        // It is a SyntaxError if the Identifier "eval" or the Identifier "arguments" occurs as the
        // Identifier in a PropertySetParameterList of a PropertyAssignment that is contained in strict code
        // or if its FunctionBody is strict code(11.1.5).
        CheckGrammarModifiers(node);

        CheckVariableLikeDeclaration(node);
        var func = Utilities.GetContainingFunction(node)!;
        if (Utilities.HasSyntacticModifier(node, ModifierFlags.ParameterPropertyModifier))
        {
            if (Js.IsTruthy(_compilerOptions.ErasableSyntaxOnly))
            {
                Error(node, Diagnostics.This_syntax_is_not_allowed_when_erasableSyntaxOnly_is_enabled);
            }

            if (!(func.Kind == SyntaxKind.Constructor && Utilities.NodeIsPresent(((ConstructorDeclaration)func).Body)))
            {
                Error(node, Diagnostics.A_parameter_property_is_only_allowed_in_a_constructor_implementation);
            }

            if (func.Kind == SyntaxKind.Constructor && NodeTests.IsIdentifier(node.Name!) && ((Identifier)node.Name!).EscapedText == "constructor")
            {
                Error(node.Name, Diagnostics.constructor_cannot_be_used_as_a_parameter_property_name);
            }
        }

        if (node.Initializer is null && Utilities.IsOptionalDeclaration(node) && Utilities.IsBindingPattern(node.Name!) && func is IFunctionLikeDeclarationBase { Body: not null })
        {
            Error(node, Diagnostics.A_binding_pattern_parameter_cannot_be_optional_in_an_implementation_signature);
        }

        if (node.Name is not null && NodeTests.IsIdentifier(node.Name) && (((Identifier)node.Name).EscapedText == "this" || ((Identifier)node.Name).EscapedText == "new"))
        {
            if (((ISignatureDeclarationBase)func).Parameters!.IndexOf(node) != 0)
            {
                Error(node, Diagnostics.A_0_parameter_must_be_the_first_parameter, ((Identifier)node.Name).EscapedText);
            }

            if (func.Kind == SyntaxKind.Constructor || func.Kind == SyntaxKind.ConstructSignature || func.Kind == SyntaxKind.ConstructorType)
            {
                Error(node, Diagnostics.A_constructor_cannot_have_a_this_parameter);
            }

            if (func.Kind == SyntaxKind.ArrowFunction)
            {
                Error(node, Diagnostics.An_arrow_function_cannot_have_a_this_parameter);
            }

            if (func.Kind == SyntaxKind.GetAccessor || func.Kind == SyntaxKind.SetAccessor)
            {
                Error(node, Diagnostics.get_and_set_accessors_cannot_declare_this_parameters);
            }
        }

        // Only check rest parameter type if it's not a binding pattern. Since binding patterns are
        // not allowed in a rest parameter, we already have an error from checkGrammarParameterList.
        if (node.DotDotDotToken is not null && !Utilities.IsBindingPattern(node.Name!) && !IsTypeAssignableTo(GetReducedType(GetTypeOfSymbol(node.Symbol)), _anyReadonlyArrayType))
        {
            Error(node, Diagnostics.A_rest_parameter_must_be_of_an_array_type);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:42036-42081 — <c>checkTypePredicate</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>getTypePredicateParent</c> answers the <see cref="Node"/> TS types as
    /// <c>SignatureDeclaration</c>, so <c>parameters</c> is read through <see cref="ISignatureDeclarationBase"/>;
    /// <c>parameterIndex</c>/<c>parameterName</c> are the identifier arms of the predicate union, reached by the same
    /// cast to <see cref="IIdentifierTypePredicate"/> the reference's own narrowing gives it (the port has one
    /// predicate class implementing all four faces — <c>Model/TypePredicate.cs</c>). The leading error is the landed
    /// <c>Utilities.ChainDiagnosticMessages</c>.
    /// </remarks>
    internal void CheckTypePredicate(TypePredicateNode node)
    {
        var parent = GetTypePredicateParent(node);
        if (parent is null)
        {
            // The parent must not be valid.
            Error(node, Diagnostics.A_type_predicate_is_only_allowed_in_return_type_position_for_functions_and_methods);
            return;
        }

        var signature = GetSignatureFromDeclaration(parent);
        var typePredicate = GetTypePredicateOfSignature(signature);
        if (typePredicate is null)
        {
            return;
        }

        CheckSourceElement(node.Type);

        var parameterName = node.ParameterName;
        if (typePredicate.Kind != TypePredicateKind.This && typePredicate.Kind != TypePredicateKind.AssertsThis)
        {
            var identifierPredicate = (IIdentifierTypePredicate)typePredicate;
            if (identifierPredicate.ParameterIndex >= 0)
            {
                if (SignatureHasRestParameter(signature) && identifierPredicate.ParameterIndex == signature.Parameters!.Count - 1)
                {
                    Error(parameterName, Diagnostics.A_type_predicate_cannot_reference_a_rest_parameter);
                }
                else
                {
                    if (typePredicate.Type is not null)
                    {
                        Func<IDiagnosticMessageChain?> leadingError = () => Utilities.ChainDiagnosticMessages(/*details*/ null, Diagnostics.A_type_predicate_s_type_must_be_assignable_to_its_parameter_s_type);
                        CheckTypeAssignableTo(typePredicate.Type, GetTypeOfSymbol(signature.Parameters![identifierPredicate.ParameterIndex]), (Node?)node.Type, /*headMessage*/ null, leadingError);
                    }
                }
            }
            else if (parameterName is not null)
            {
                var hasReportedError = false;
                foreach (var parameter in ((ISignatureDeclarationBase)parent).Parameters!)
                {
                    if (
                        Utilities.IsBindingPattern(parameter.Name) &&
                        CheckIfTypePredicateVariableIsDeclaredInBindingPattern(parameter.Name!, parameterName, identifierPredicate.ParameterName!)
                    )
                    {
                        hasReportedError = true;
                        break;
                    }
                }

                if (!hasReportedError)
                {
                    Error(node.ParameterName, Diagnostics.Cannot_find_parameter_0, identifierPredicate.ParameterName);
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:42083-42097 — <c>getTypePredicateParent</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers the <c>SignatureDeclaration</c> union, which has no C# anchor, so the port answers
    /// <see cref="Node"/>; <c>type</c> is read through <see cref="ISignatureDeclarationBase"/>, the interface all
    /// seven kinds of that union implement.
    /// </remarks>
    internal static Node? GetTypePredicateParent(Node node)
    {
        var parent = node.Parent!;
        switch (parent.Kind)
        {
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.CallSignature:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.FunctionType:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.MethodSignature:
                if (ReferenceEquals(node, ((ISignatureDeclarationBase)parent).Type))
                {
                    return parent;
                }

                break;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:42099-42126 — <c>checkIfTypePredicateVariableIsDeclaredInBindingPattern</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>BindingPattern</c> parameter is a union of <see cref="ArrayBindingPattern"/> and
    /// <see cref="ObjectBindingPattern"/> with no C# anchor (there is no <c>BindingPattern</c> class in the port), so
    /// the parameter is <see cref="Node"/> and <c>elements</c> is read through the two arms explicitly.
    /// </remarks>
    internal bool CheckIfTypePredicateVariableIsDeclaredInBindingPattern(
        Node pattern,
        Node predicateVariableNode,
        string predicateVariableName)
    {
        IEnumerable<Node> elements = pattern.Kind == SyntaxKind.ArrayBindingPattern
            ? ((ArrayBindingPattern)pattern).Elements!
            : ((ObjectBindingPattern)pattern).Elements!;
        foreach (var element in elements)
        {
            if (NodeTests.IsOmittedExpression(element))
            {
                continue;
            }

            var name = ((BindingElement)element).Name!;
            if (name.Kind == SyntaxKind.Identifier && ((Identifier)name).EscapedText == predicateVariableName)
            {
                Error(predicateVariableNode, Diagnostics.A_type_predicate_cannot_reference_element_0_in_a_binding_pattern, predicateVariableName);
                return true;
            }
            else if (name.Kind == SyntaxKind.ArrayBindingPattern || name.Kind == SyntaxKind.ObjectBindingPattern)
            {
                if (
                    CheckIfTypePredicateVariableIsDeclaredInBindingPattern(
                        name,
                        predicateVariableNode,
                        predicateVariableName)
                )
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:42128-42220 — <c>checkSignatureDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>SignatureDeclaration</c> union is a <see cref="Node"/> here, and the two members it carries
    /// (<c>parameters</c>, <c>type</c>) are read through <see cref="ISignatureDeclarationBase"/>. The
    /// <c>LanguageFeatureMinimumTarget</c> comparisons are the <c>ScriptTarget</c> constants the class remarks name.
    /// <c>getEffectiveTypeParameterDeclarations</c>, <c>checkUnmatchedJSDocParameters</c>,
    /// <c>checkCollisionWithArgumentsInGeneratedCode</c>, <c>registerForUnusedIdentifiersCheck</c> and
    /// <c>checkAsyncFunctionReturnType</c> are siblings' members in earlier bands of the checker.
    /// </remarks>
    internal void CheckSignatureDeclaration(Node node)
    {
        // Grammar checking
        if (node.Kind == SyntaxKind.IndexSignature)
        {
            CheckGrammarIndexSignature(node);
        }
        // TODO (yuisu): Remove this check in else-if when SyntaxKind.Construct is moved and ambient context is handled
        else if (
            node.Kind == SyntaxKind.FunctionType || node.Kind == SyntaxKind.FunctionDeclaration || node.Kind == SyntaxKind.ConstructorType ||
            node.Kind == SyntaxKind.CallSignature || node.Kind == SyntaxKind.Constructor ||
            node.Kind == SyntaxKind.ConstructSignature
        )
        {
            CheckGrammarFunctionLikeDeclaration(node);
        }

        var functionFlags = Utilities.GetFunctionFlags(node);
        if ((functionFlags & FunctionFlags.Invalid) == 0)
        {
            // Async generators prior to ES2018 require the __await and __asyncGenerator helpers
            if ((functionFlags & FunctionFlags.AsyncGenerator) == FunctionFlags.AsyncGenerator && _languageVersion < ScriptTarget.ES2018)
            {
                CheckExternalEmitHelpers(node, ExternalEmitHelpers.AsyncGeneratorIncludes);
            }

            // Async functions prior to ES2017 require the __awaiter helper
            if ((functionFlags & FunctionFlags.AsyncGenerator) == FunctionFlags.Async && _languageVersion < ScriptTarget.ES2017)
            {
                CheckExternalEmitHelpers(node, ExternalEmitHelpers.Awaiter);
            }

            // Generator functions, Async functions, and Async Generator functions prior to
            // ES2015 require the __generator helper
            if ((functionFlags & FunctionFlags.AsyncGenerator) != FunctionFlags.Normal && _languageVersion < ScriptTarget.ES2015)
            {
                CheckExternalEmitHelpers(node, ExternalEmitHelpers.Generator);
            }
        }

        CheckTypeParameters(Utilities.GetEffectiveTypeParameterDeclarations(node));
        CheckUnmatchedJSDocParameters((ISignatureDeclarationBase)node);

        foreach (var parameter in ((ISignatureDeclarationBase)node).Parameters!)
        {
            CheckParameter(parameter);
        }

        // TODO(rbuckton): Should we start checking JSDoc types?
        if (((ISignatureDeclarationBase)node).Type is not null)
        {
            CheckSourceElement(((ISignatureDeclarationBase)node).Type);
        }

        AddLazyDiagnostic(CheckSignatureDeclarationDiagnostics);

        void CheckSignatureDeclarationDiagnostics()
        {
            CheckCollisionWithArgumentsInGeneratedCode(node);

            var returnTypeNode = Utilities.GetEffectiveReturnTypeNode(node);
            var returnTypeErrorLocation = returnTypeNode;

            if (Utilities.IsInJSFile(node))
            {
                var typeTag = Utilities.GetJSDocTypeTag(node);
                if (typeTag is not null && typeTag.TypeExpression is not null && NodeTests.IsTypeReferenceNode((Node)typeTag.TypeExpression.Type!))
                {
                    var signature = GetSingleCallSignature(GetTypeFromTypeNode(typeTag.TypeExpression.Type));
                    if (signature is not null && signature.Declaration is not null)
                    {
                        returnTypeNode = Utilities.GetEffectiveReturnTypeNode(signature.Declaration);
                        returnTypeErrorLocation = typeTag.TypeExpression.Type;
                    }
                }
            }

            if (_noImplicitAny && returnTypeNode is null)
            {
                switch (node.Kind)
                {
                    case SyntaxKind.ConstructSignature:
                        Error(node, Diagnostics.Construct_signature_which_lacks_return_type_annotation_implicitly_has_an_any_return_type);
                        break;
                    case SyntaxKind.CallSignature:
                        Error(node, Diagnostics.Call_signature_which_lacks_return_type_annotation_implicitly_has_an_any_return_type);
                        break;
                }
            }

            if (returnTypeNode is not null && returnTypeErrorLocation is not null)
            {
                // PORT-NOTE: TS shadows this member's own 'functionFlags' here; C# forbids a local function
                // shadowing an enclosing local (CS0136), so the diagnostic's copy carries its own name.
                var diagnosticFunctionFlags = Utilities.GetFunctionFlags(node);
                if ((diagnosticFunctionFlags & (FunctionFlags.Invalid | FunctionFlags.Generator)) == FunctionFlags.Generator)
                {
                    var returnType = GetTypeFromTypeNode(returnTypeNode);
                    if (ReferenceEquals(returnType, _voidType))
                    {
                        Error((Node?)returnTypeErrorLocation, Diagnostics.A_generator_cannot_have_a_void_type_annotation);
                    }
                    else
                    {
                        CheckGeneratorInstantiationAssignabilityToReturnType(returnType, functionFlags, returnTypeErrorLocation);
                    }
                }
                else if ((diagnosticFunctionFlags & FunctionFlags.AsyncGenerator) == FunctionFlags.Async)
                {
                    CheckAsyncFunctionReturnType(node, returnTypeNode, returnTypeErrorLocation);
                }
            }

            if (node.Kind != SyntaxKind.IndexSignature && node.Kind != SyntaxKind.JSDocFunctionType)
            {
                RegisterForUnusedIdentifiersCheck(node);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:42222-42235 — <c>checkGeneratorInstantiationAssignabilityToReturnType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>getIterationTypeOfGeneratorFunctionReturnType</c> (checker.ts:46482) and
    /// <c>createGeneratorType</c> (checker.ts:39299) belong to sibling bands and are called unqualified; the
    /// <c>|| anyType</c>/<c>|| unknownType</c> fallbacks are the port's <c>??</c> over their nullable answers.
    /// </remarks>
    internal bool CheckGeneratorInstantiationAssignabilityToReturnType(IType returnType, FunctionFlags functionFlags, ITypeNode? errorNode = null)
    {
        // Naively, one could check that Generator<any, any, any> is assignable to the return type annotation.
        // However, that would not catch the error in the following case.
        //
        //    interface BadGenerator extends Iterable<number>, Iterator<string> { }
        //    function* g(): BadGenerator { } // Iterable and Iterator have different types!
        //
        var generatorYieldType = GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Yield, returnType, (functionFlags & FunctionFlags.Async) != 0) ?? _anyType;
        var generatorReturnType = GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Return, returnType, (functionFlags & FunctionFlags.Async) != 0) ?? generatorYieldType;
        var generatorNextType = GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Next, returnType, (functionFlags & FunctionFlags.Async) != 0) ?? _unknownType;
        var generatorInstantiation = CreateGeneratorType(generatorYieldType, generatorReturnType, generatorNextType, (functionFlags & FunctionFlags.Async) != 0);

        return CheckTypeAssignableTo(generatorInstantiation, returnType, (Node?)errorNode);
    }

    /// <summary>ported from: src/compiler/checker.ts:42237-42313 — <c>checkClassForDuplicateDeclarations</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>ClassLikeDeclaration</c> union is a <see cref="Node"/>, <c>members</c> is read through
    /// <see cref="IClassLikeDeclarationBase"/> and each member through <see cref="INamedDeclaration"/> — the same
    /// member walk <c>Checker.Check.Classes.cs</c> uses. The three name tables are
    /// <see cref="Dictionary{TKey,TValue}"/>s keyed by the erased <c>__String</c>, which is a <c>string</c> here, and
    /// <c>DeclarationMeaning</c>'s absent <c>0</c> case is the C# literal zero (implicitly convertible to any enum).
    /// </remarks>
    internal void CheckClassForDuplicateDeclarations(Node node)
    {
        var instanceNames = new Dictionary<string, DeclarationMeaning>();
        var staticNames = new Dictionary<string, DeclarationMeaning>();
        // instance and static private identifiers share the same scope
        var privateIdentifiers = new Dictionary<string, DeclarationMeaning>();
        foreach (var member in ((IClassLikeDeclarationBase)node).Members!)
        {
            if (member.Kind == SyntaxKind.Constructor)
            {
                foreach (var param in ((ConstructorDeclaration)member).Parameters!)
                {
                    if (Utilities.IsParameterPropertyDeclaration(param, (Node)member) && !Utilities.IsBindingPattern(param.Name!))
                    {
                        AddName(instanceNames, param.Name!, ((Identifier)param.Name!).EscapedText!, DeclarationMeaning.GetOrSetAccessor);
                    }
                }
            }
            else
            {
                var isStaticMember = Utilities.IsStatic((Node)member);
                var name = ((INamedDeclaration)member).Name;
                if (name is null)
                {
                    continue;
                }

                var isPrivate = NodeTests.IsPrivateIdentifier(name);
                var privateStaticFlags = isPrivate && isStaticMember ? DeclarationMeaning.PrivateStatic : 0;
                var names = isPrivate
                    ? privateIdentifiers
                    : isStaticMember
                        ? staticNames
                        : instanceNames;

                var memberName = name is not null ? GetEffectivePropertyNameForPropertyNameNode(name) : null;
                if (memberName is not null)
                {
                    switch (member.Kind)
                    {
                        case SyntaxKind.GetAccessor:
                            AddName(names, name!, memberName, DeclarationMeaning.GetAccessor | privateStaticFlags);
                            break;

                        case SyntaxKind.SetAccessor:
                            AddName(names, name!, memberName, DeclarationMeaning.SetAccessor | privateStaticFlags);
                            break;

                        case SyntaxKind.PropertyDeclaration:
                            AddName(names, name!, memberName, DeclarationMeaning.GetOrSetAccessor | privateStaticFlags);
                            break;

                        case SyntaxKind.MethodDeclaration:
                            AddName(names, name!, memberName, DeclarationMeaning.Method | privateStaticFlags);
                            break;
                    }
                }
            }
        }

        void AddName(Dictionary<string, DeclarationMeaning> names, Node location, string name, DeclarationMeaning meaning)
        {
            if (names.TryGetValue(name, out var prev))
            {
                // For private identifiers, do not allow mixing of static and instance members with the same name
                if ((prev & DeclarationMeaning.PrivateStatic) != (meaning & DeclarationMeaning.PrivateStatic))
                {
                    Error(location, Diagnostics.Duplicate_identifier_0_Static_and_instance_elements_cannot_share_the_same_private_name, Utilities.GetTextOfNode(location));
                }
                else
                {
                    var prevIsMethod = (prev & DeclarationMeaning.Method) != 0;
                    var isMethod = (meaning & DeclarationMeaning.Method) != 0;
                    if (prevIsMethod || isMethod)
                    {
                        if (prevIsMethod != isMethod)
                        {
                            Error(location, Diagnostics.Duplicate_identifier_0, Utilities.GetTextOfNode(location));
                        }

                        // If this is a method/method duplication is might be an overload, so this will be handled when overloads are considered
                    }
                    else if ((prev & meaning & ~DeclarationMeaning.PrivateStatic) != 0)
                    {
                        Error(location, Diagnostics.Duplicate_identifier_0, Utilities.GetTextOfNode(location));
                    }
                    else
                    {
                        names[name] = prev | meaning;
                    }
                }
            }
            else
            {
                names[name] = meaning;
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:42326-42349 — <c>checkClassForStaticPropertyNameConflicts</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>case "name": … if (useDefineForClassFields) break; // fall through</c> is C#'s
    /// <c>if (condition) break; goto case "prototype";</c>; the slot is the class's <c>_useDefineForClassFields</c>
    /// (checker.ts:1532), the class remarks' naming convention for the option-derived state.
    /// </remarks>
    internal void CheckClassForStaticPropertyNameConflicts(Node node)
    {
        foreach (var member in ((IClassLikeDeclarationBase)node).Members!)
        {
            var memberNameNode = ((INamedDeclaration)member).Name;
            var isStaticMember = Utilities.IsStatic((Node)member);
            if (isStaticMember && memberNameNode is not null)
            {
                var memberName = GetEffectivePropertyNameForPropertyNameNode(memberNameNode);
                switch (memberName)
                {
                    case "name":
                    case "length":
                    case "caller":
                    case "arguments":
                        if (_useDefineForClassFields)
                        {
                            break;
                        }

                        // fall through
                        goto case "prototype";
                    case "prototype":
                        var message = Diagnostics.Static_property_0_conflicts_with_built_in_property_Function_0_of_constructor_function_1;
                        var className = GetNameOfSymbolAsWritten(GetSymbolOfDeclaration((IDeclaration)node));
                        Error(memberNameNode, message, memberName, className);
                        break;
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:42351-42378 — <c>checkObjectTypeForDuplicateDeclarations</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>TypeLiteralNode | InterfaceDeclaration</c> parameter is a <see cref="Node"/>; <c>members</c>
    /// is read through <c>GetTypeElementMembers</c> below, the kind switch the port needs because TS's union declares the
    /// slot on both arms and no C# interface carries it for both. The two entries
    /// <c>stringLiteral</c>/<c>numericLiteral</c> read <c>.text</c> off the literal, which both classes carry.
    /// </remarks>
    internal void CheckObjectTypeForDuplicateDeclarations(Node node)
    {
        var names = new Dictionary<string, bool>();
        foreach (var member in GetTypeElementMembers(node))
        {
            if (member.Kind == SyntaxKind.PropertySignature)
            {
                string memberName;
                var name = ((INamedDeclaration)member).Name!;
                switch (name.Kind)
                {
                    case SyntaxKind.StringLiteral:
                    case SyntaxKind.NumericLiteral:
                        memberName = ((LiteralExpression)name).Text!;
                        break;
                    case SyntaxKind.Identifier:
                        memberName = Utilities.IdText(name);
                        break;
                    default:
                        continue;
                }

                if (names.TryGetValue(memberName, out var seen) && seen)
                {
                    Error((Node?)Utilities.GetNameOfDeclaration((Node)((IDeclaration)member).Symbol!.ValueDeclaration!), Diagnostics.Duplicate_identifier_0, memberName);
                    Error(((INamedDeclaration)member).Name, Diagnostics.Duplicate_identifier_0, memberName);
                }
                else
                {
                    names[memberName] = true;
                }
            }
        }
    }

    /// <summary>The <c>members</c> of a type literal or an interface declaration.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reaches <c>.members</c> on the <c>TypeLiteralNode | InterfaceDeclaration</c> union, whose two
    /// arms declare the slot with the same type; the port has the two class slots but no shared interface, so the read
    /// is this kind switch (the form <c>Utilities.IsSuperProperty</c> records for such unions).
    /// </remarks>
    private static NodeArray<ITypeElement> GetTypeElementMembers(Node node)
        => node.Kind == SyntaxKind.InterfaceDeclaration
            ? ((InterfaceDeclaration)node).Members!
            : ((TypeLiteralNode)node).Members!;

    /// <summary>ported from: src/compiler/checker.ts:42380-42420 — <c>checkTypeForDuplicateIndexSignatures</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>ClassLikeDeclaration | InterfaceDeclaration | TypeLiteralNode</c> parameter is a
    /// <see cref="Node"/>; the map's value is TS's object literal <c>{ type, declarations }</c>, written as the port's
    /// tuple (the shape the port uses for object literals with no model of their own). <c>forEachType</c> is the
    /// sibling type-utility member (<c>Checker.Inference.cs</c>).
    /// </remarks>
    internal void CheckTypeForDuplicateIndexSignatures(Node node)
    {
        if (node.Kind == SyntaxKind.InterfaceDeclaration)
        {
            var nodeSymbol = GetSymbolOfDeclaration((IDeclaration)node);
            // in case of merging interface declaration it is possible that we'll enter this check procedure several times for every declaration
            // to prevent this run check only for the first declaration of a given kind
            if (nodeSymbol.Declarations is not null && nodeSymbol.Declarations.Count > 0 && !ReferenceEquals(nodeSymbol.Declarations[0], node))
            {
                return;
            }
        }

        // TypeScript 1.0 spec (April 2014)
        // 3.7.4: An object type can contain at most one string index signature and one numeric index signature.
        // 8.5: A class declaration can have at most one string index member declaration and one numeric index member declaration
        var indexSymbol = GetIndexSymbol(GetSymbolOfDeclaration((IDeclaration)node));
        if (indexSymbol?.Declarations is not null)
        {
            var indexSignatureMap = new Dictionary<int, (IType Type, List<IndexSignatureDeclaration> Declarations)>();
            foreach (var declaration in indexSymbol.Declarations)
            {
                if (NodeTests.IsIndexSignatureDeclaration((Node)declaration))
                {
                    var indexSignature = (IndexSignatureDeclaration)declaration;
                    if (indexSignature.Parameters!.Count == 1 && indexSignature.Parameters[0].Type is not null)
                    {
                        ForEachType<object>(GetTypeFromTypeNode(indexSignature.Parameters[0].Type), type =>
                        {
                            if (indexSignatureMap.TryGetValue(GetTypeId(type), out var entry))
                            {
                                entry.Declarations.Add(indexSignature);
                            }
                            else
                            {
                                indexSignatureMap[GetTypeId(type)] = (type, [indexSignature]);
                            }

                            return null;
                        });
                    }
                }

                // Do nothing for late-bound index signatures: allow these to duplicate one another and explicit indexes
            }

            foreach (var entry in indexSignatureMap.Values)
            {
                if (entry.Declarations.Count > 1)
                {
                    foreach (var declaration in entry.Declarations)
                    {
                        Error(declaration, Diagnostics.Duplicate_index_signature_for_type_0, Diagnostic.DeferArg(() => TypeToString(entry.Type)));
                    }
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:42422-42433 — <c>checkPropertyDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>PropertyDeclaration | PropertySignature</c> is a <see cref="Node"/>; <c>name</c> is read
    /// through <see cref="INamedDeclaration"/> and <c>initializer</c> through the <see cref="PropertyDeclaration"/>
    /// arm the kind test has already selected.
    /// </remarks>
    internal void CheckPropertyDeclaration(Node node)
    {
        // Grammar checking
        if (!CheckGrammarModifiers(node) && !CheckGrammarProperty(node))
        {
            CheckGrammarComputedPropertyName(((INamedDeclaration)node).Name!);
        }

        CheckVariableLikeDeclaration(node);

        SetNodeLinksForPrivateIdentifierScope(node);

        // property signatures already report "initializer not allowed in ambient context" elsewhere
        if (Utilities.HasSyntacticModifier(node, ModifierFlags.Abstract) && node.Kind == SyntaxKind.PropertyDeclaration && ((PropertyDeclaration)node).Initializer is not null)
        {
            Error(node, Diagnostics.Property_0_cannot_have_an_initializer_because_it_is_marked_abstract, Utilities.DeclarationNameToString(((INamedDeclaration)node).Name));
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:42435-42440 — <c>checkPropertySignature</c>.</summary>
    internal void CheckPropertySignature(PropertySignature node)
    {
        if (NodeTests.IsPrivateIdentifier(node.Name!))
        {
            Error(node, Diagnostics.Private_identifiers_are_not_allowed_outside_class_bodies);
        }

        CheckPropertyDeclaration(node);
    }

    /// <summary>ported from: src/compiler/checker.ts:42442-42464 — <c>checkMethodDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>MethodDeclaration | MethodSignature</c> is a <see cref="Node"/>; <c>asteriskToken</c> and
    /// <c>body</c> are read through the <see cref="MethodDeclaration"/> arm after the reference's own
    /// <c>isMethodDeclaration</c>/kind test. <c>getContainingClass</c> is <c>Utilities.GetContainingClass</c>.
    /// </remarks>
    internal void CheckMethodDeclaration(Node node)
    {
        // Grammar checking
        if (!CheckGrammarMethod(node))
        {
            CheckGrammarComputedPropertyName(((INamedDeclaration)node).Name!);
        }

        if (NodeTests.IsMethodDeclaration(node) && ((MethodDeclaration)node).AsteriskToken is not null && NodeTests.IsIdentifier(((INamedDeclaration)node).Name!) && Utilities.IdText(((INamedDeclaration)node).Name!) == "constructor")
        {
            Error(((INamedDeclaration)node).Name, Diagnostics.Class_constructor_may_not_be_a_generator);
        }

        // Grammar checking for modifiers is done inside the function checkGrammarFunctionLikeDeclaration
        CheckFunctionOrMethodDeclaration(node);

        // method signatures already report "implementation not allowed in ambient context" elsewhere
        if (Utilities.HasSyntacticModifier(node, ModifierFlags.Abstract) && node.Kind == SyntaxKind.MethodDeclaration && ((MethodDeclaration)node).Body is not null)
        {
            Error(node, Diagnostics.Method_0_cannot_have_an_implementation_because_it_is_marked_abstract, Utilities.DeclarationNameToString(((INamedDeclaration)node).Name));
        }

        // Private named methods are only allowed in class declarations
        if (NodeTests.IsPrivateIdentifier(((INamedDeclaration)node).Name!) && Utilities.GetContainingClass(node) is null)
        {
            Error(node, Diagnostics.Private_identifiers_are_not_allowed_outside_class_bodies);
        }

        SetNodeLinksForPrivateIdentifierScope(node);
    }

    /// <summary>ported from: src/compiler/checker.ts:42466-42489 — <c>setNodeLinksForPrivateIdentifierScope</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's five-way declaration union is a <see cref="Node"/> and <c>name</c> is read through
    /// <see cref="INamedDeclaration"/>. The two <c>LanguageFeatureMinimumTarget</c> comparisons are the
    /// <c>ScriptTarget</c> constants the class remarks name (ES2022 for names-and-static-blocks, ESNext for the
    /// decorators entry), and <c>useDefineForClassFields</c> is the class's <c>_useDefineForClassFields</c> slot.
    /// </remarks>
    internal void SetNodeLinksForPrivateIdentifierScope(Node node)
    {
        if (NodeTests.IsPrivateIdentifier(((INamedDeclaration)node).Name!))
        {
            if (
                _languageVersion < ScriptTarget.ES2022 ||
                _languageVersion < ScriptTarget.ESNext ||
                !_useDefineForClassFields
            )
            {
                for (var lexicalScope = Utilities.GetEnclosingBlockScopeContainer(node); lexicalScope is not null; lexicalScope = Utilities.GetEnclosingBlockScopeContainer(lexicalScope))
                {
                    GetNodeLinks(lexicalScope).Flags |= NodeCheckFlags.ContainsClassWithPrivateIdentifiers;
                }

                // If this is a private element in a class expression inside the body of a loop,
                // then we must use a block-scoped binding to store the additional variables required
                // to transform private elements.
                if (NodeTests.IsClassExpression(node.Parent!))
                {
                    var enclosingIterationStatement = GetEnclosingIterationStatement(node.Parent!);
                    if (enclosingIterationStatement is not null)
                    {
                        GetNodeLinks(((INamedDeclaration)node).Name!).Flags |= NodeCheckFlags.BlockScopedBindingInLoop;
                        GetNodeLinks(enclosingIterationStatement).Flags |= NodeCheckFlags.LoopWithCapturedBlockScopedBinding;
                    }
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:42491-42495 — <c>checkClassStaticBlockDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>forEachChild</c> is <c>ForEachChildTable.ForEachChild</c>, the port's child walk
    /// (<c>Parser/ForEachChildTable.cs</c>); the callback answers <c>object?</c> and the walk stops on a truthy answer
    /// exactly as TS's does, which is why the callback states <c>null</c>.
    /// </remarks>
    internal void CheckClassStaticBlockDeclaration(ClassStaticBlockDeclaration node)
    {
        CheckGrammarModifiers(node);

        ForEachChildTable.ForEachChild<object>(node, child =>
        {
            CheckSourceElement(child);
            return null;
        });
    }

    /// <summary>ported from: src/compiler/checker.ts:42497-42587 — <c>checkConstructorDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's two nested functions are the port's local functions, declared in the reference's order after
    /// the same early <c>return</c> (a local function is a declaration, so it is not unreachable code).
    /// <c>getClassExtendsHeritageElement</c>, <c>captureLexicalThis</c>, <c>classDeclarationExtendsNull</c> and
    /// <c>findFirstSuperCall</c> are earlier checker members called unqualified, and <c>skipOuterExpressions</c> is
    /// <c>FactoryUtilities.SkipOuterExpressions</c>.
    /// </remarks>
    internal void CheckConstructorDeclaration(ConstructorDeclaration node)
    {
        // Grammar check on signature of constructor and modifier of the constructor is done in checkSignatureDeclaration function.
        CheckSignatureDeclaration(node);
        // Grammar check for checking only related to constructorDeclaration
        if (!CheckGrammarConstructorTypeParameters(node))
        {
            CheckGrammarConstructorTypeAnnotation(node);
        }

        CheckSourceElement(node.Body);

        var symbol = GetSymbolOfDeclaration(node);
        var firstDeclaration = Utilities.GetDeclarationOfKind(symbol, node.Kind);

        // Only type check the symbol once
        if (ReferenceEquals(node, firstDeclaration))
        {
            CheckFunctionOrConstructorSymbol(symbol);
        }

        // exit early in the case of signature - super checks are not relevant to them
        if (Utilities.NodeIsMissing(node.Body))
        {
            return;
        }

        AddLazyDiagnostic(CheckConstructorDeclarationDiagnostics);

        return;

        bool IsInstancePropertyWithInitializerOrPrivateIdentifierProperty(Node n)
        {
            if (Utilities.IsPrivateIdentifierClassElementDeclaration(n))
            {
                return true;
            }

            return n.Kind == SyntaxKind.PropertyDeclaration
                && !Utilities.IsStatic(n)
                && ((PropertyDeclaration)n).Initializer is not null;
        }

        void CheckConstructorDeclarationDiagnostics()
        {
            // TS 1.0 spec (April 2014): 8.3.2
            // Constructors of classes with no extends clause may not contain super calls, whereas
            // constructors of derived classes must contain at least one super call somewhere in their function body.
            var containingClassDecl = node.Parent!;
            if (Utilities.GetClassExtendsHeritageElement(containingClassDecl) is not null)
            {
                CaptureLexicalThis(node.Parent!, containingClassDecl);
                var classExtendsNull = ClassDeclarationExtendsNull(containingClassDecl);
                var superCall = FindFirstSuperCall(node.Body!);
                if (superCall is not null)
                {
                    if (classExtendsNull)
                    {
                        Error(superCall, Diagnostics.A_constructor_cannot_contain_a_super_call_when_its_class_extends_null);
                    }

                    // A super call must be root-level in a constructor if both of the following are true:
                    // - The containing class is a derived class.
                    // - The constructor declares parameter properties
                    //   or the containing class declares instance member variables with initializers.
                    var superCallShouldBeRootLevel = !_emitStandardClassFields
                        && (Core.Some(((IClassLikeDeclarationBase)node.Parent!).Members, member => IsInstancePropertyWithInitializerOrPrivateIdentifierProperty((Node)member))
                            || Core.Some(node.Parameters, parameter => Utilities.HasSyntacticModifier(parameter, ModifierFlags.ParameterPropertyModifier)));

                    if (superCallShouldBeRootLevel)
                    {
                        // Until we have better flow analysis, it is an error to place the super call within any kind of block or conditional
                        // See GH #8277
                        if (!SuperCallIsRootLevelInConstructor(superCall, node.Body!))
                        {
                            Error(superCall, Diagnostics.A_super_call_must_be_a_root_level_statement_within_a_constructor_of_a_derived_class_that_contains_initialized_properties_parameter_properties_or_private_identifiers);
                        }
                        // Skip past any prologue directives to check statements for referring to 'super' or 'this' before a super call
                        else
                        {
                            ExpressionStatement? superCallStatement = null;

                            foreach (var statement in node.Body!.Statements!)
                            {
                                var statementNode = (Node)statement;
                                if (NodeTests.IsExpressionStatement(statementNode) && Utilities.IsSuperCall(FactoryUtilities.SkipOuterExpressions(((ExpressionStatement)statementNode).Expression!)))
                                {
                                    superCallStatement = (ExpressionStatement)statementNode;
                                    break;
                                }

                                if (NodeImmediatelyReferencesSuperOrThis(statementNode))
                                {
                                    break;
                                }
                            }

                            // Until we have better flow analysis, it is an error to place the super call within any kind of block or conditional
                            // See GH #8277
                            if (superCallStatement is null)
                            {
                                Error(node, Diagnostics.A_super_call_must_be_the_first_statement_in_the_constructor_to_refer_to_super_or_this_when_a_derived_class_contains_initialized_properties_parameter_properties_or_private_identifiers);
                            }
                        }
                    }
                }
                else if (!classExtendsNull)
                {
                    Error(node, Diagnostics.Constructors_for_derived_classes_must_contain_a_super_call);
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:42589-42592 — <c>superCallIsRootLevelInConstructor</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>walkUpParenthesizedExpressions</c> is <c>Utilities.WalkUpParenthesizedExpressions</c>
    /// (utilities.ts), which answers <see cref="Node"/>.
    /// </remarks>
    internal static bool SuperCallIsRootLevelInConstructor(Node superCall, Block body)
    {
        var superCallParent = Utilities.WalkUpParenthesizedExpressions(superCall.Parent!);
        return NodeTests.IsExpressionStatement(superCallParent) && ReferenceEquals(superCallParent.Parent, body);
    }

    /// <summary>ported from: src/compiler/checker.ts:42594-42604 — <c>nodeImmediatelyReferencesSuperOrThis</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>forEachChild</c> is <c>ForEachChildTable.ForEachChild&lt;bool&gt;</c>, and TS's
    /// <c>!!forEachChild(...)</c> is the port's <c>== true</c> — the answer of a walk that stopped on a truthy child
    /// (<c>Parser/ForEachChildTable.cs</c>). <c>isThisContainerOrFunctionBlock</c> (checker.ts:41073) is an earlier
    /// checker member.
    /// </remarks>
    internal static bool NodeImmediatelyReferencesSuperOrThis(Node node)
    {
        if (node.Kind == SyntaxKind.SuperKeyword || node.Kind == SyntaxKind.ThisKeyword)
        {
            return true;
        }

        if (Utilities.IsThisContainerOrFunctionBlock(node))
        {
            return false;
        }

        return ForEachChildTable.ForEachChild<bool>(node, child => NodeImmediatelyReferencesSuperOrThis(child)) == true;
    }

    /// <summary>ported from: src/compiler/checker.ts:42606-42662 — <c>checkAccessorDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>AccessorDeclaration</c> union is a <see cref="Node"/> here; the two declarations the
    /// diagnostic reads are asked for with the concrete class each kind selects
    /// (<see cref="GetAccessorDeclaration"/>/<see cref="SetAccessorDeclaration"/>), and <c>body</c> is read through
    /// <see cref="IFunctionLikeDeclarationBase"/>. <c>getEffectiveModifierFlags</c> (utilities.ts:7276) is
    /// <c>Utilities.GetEffectiveModifierFlags</c> and <c>hasBindableName</c> is the landed <c>HasBindableName</c>.
    /// </remarks>
    internal void CheckAccessorDeclaration(Node node)
    {
        if (NodeTests.IsIdentifier(((INamedDeclaration)node).Name!) && Utilities.IdText(((INamedDeclaration)node).Name!) == "constructor" && Utilities.IsClassLike(node.Parent!))
        {
            Error(((INamedDeclaration)node).Name, Diagnostics.Class_constructor_may_not_be_an_accessor);
        }

        AddLazyDiagnostic(CheckAccessorDeclarationDiagnostics);
        CheckSourceElement(((IFunctionLikeDeclarationBase)node).Body);
        SetNodeLinksForPrivateIdentifierScope(node);

        void CheckAccessorDeclarationDiagnostics()
        {
            // Grammar checking accessors
            if (!CheckGrammarFunctionLikeDeclaration(node) && !CheckGrammarAccessor(node))
            {
                CheckGrammarComputedPropertyName(((INamedDeclaration)node).Name!);
            }

            CheckDecorators(node);
            CheckSignatureDeclaration(node);
            if (node.Kind == SyntaxKind.GetAccessor)
            {
                if ((node.Flags & NodeFlags.Ambient) == 0 && Utilities.NodeIsPresent(((IFunctionLikeDeclarationBase)node).Body) && (node.Flags & NodeFlags.HasImplicitReturn) != 0)
                {
                    if ((node.Flags & NodeFlags.HasExplicitReturn) == 0)
                    {
                        Error(((INamedDeclaration)node).Name, Diagnostics.A_get_accessor_must_return_a_value);
                    }
                }
            }

            // Do not use hasDynamicName here, because that returns false for well known symbols.
            // We want to perform checkComputedPropertyName for all computed properties, including
            // well known symbols.
            if (((INamedDeclaration)node).Name!.Kind == SyntaxKind.ComputedPropertyName)
            {
                CheckComputedPropertyName((ComputedPropertyName)((INamedDeclaration)node).Name!);
            }

            if (HasBindableName((IDeclaration)node))
            {
                // TypeScript 1.0 spec (April 2014): 8.4.3
                // Accessors for the same member name must specify the same accessibility.
                var symbol = GetSymbolOfDeclaration((IDeclaration)node);
                var getter = Utilities.GetDeclarationOfKind<GetAccessorDeclaration>(symbol, SyntaxKind.GetAccessor);
                var setter = Utilities.GetDeclarationOfKind<SetAccessorDeclaration>(symbol, SyntaxKind.SetAccessor);
                if (getter is not null && setter is not null && (GetNodeCheckFlags(getter) & NodeCheckFlags.TypeChecked) == 0)
                {
                    GetNodeLinks(getter).Flags |= NodeCheckFlags.TypeChecked;
                    var getterFlags = Utilities.GetEffectiveModifierFlags(getter);
                    var setterFlags = Utilities.GetEffectiveModifierFlags(setter);
                    if ((getterFlags & ModifierFlags.Abstract) != (setterFlags & ModifierFlags.Abstract))
                    {
                        Error(getter.Name, Diagnostics.Accessors_must_both_be_abstract_or_non_abstract);
                        Error(setter.Name, Diagnostics.Accessors_must_both_be_abstract_or_non_abstract);
                    }

                    if (
                        ((getterFlags & ModifierFlags.Protected) != 0 && (setterFlags & (ModifierFlags.Protected | ModifierFlags.Private)) == 0) ||
                        ((getterFlags & ModifierFlags.Private) != 0 && (setterFlags & ModifierFlags.Private) == 0)
                    )
                    {
                        Error(getter.Name, Diagnostics.A_get_accessor_must_be_at_least_as_accessible_as_the_setter);
                        Error(setter.Name, Diagnostics.A_get_accessor_must_be_at_least_as_accessible_as_the_setter);
                    }
                }
            }

            var returnType = GetTypeOfAccessors(GetSymbolOfDeclaration((IDeclaration)node));
            if (node.Kind == SyntaxKind.GetAccessor)
            {
                CheckAllCodePathsInNonVoidFunctionReturnOrThrow(node, returnType);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:42664-42666 — <c>checkMissingDeclaration</c>.</summary>
    internal void CheckMissingDeclaration(Node node)
    {
        CheckDecorators(node);
    }

    // ---------------------------------------------------------------------------------------------
    // The type-node checks (checker.ts:42668-43055)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:42668-42673 — <c>getEffectiveTypeArgumentAtIndex</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>TypeReferenceNode | ExpressionWithTypeArguments</c> parameter is a <see cref="Node"/>, and
    /// <c>typeArguments</c> is read through <see cref="INodeWithTypeArguments"/> — the interface the port generated for
    /// that slot, which both arms (and <c>TypeQueryNode</c>) implement.
    /// </remarks>
    internal IType GetEffectiveTypeArgumentAtIndex(Node node, List<ITypeParameter> typeParameters, int index)
    {
        var typeArguments = ((INodeWithTypeArguments)node).TypeArguments;
        if (typeArguments is not null && index < typeArguments.Count)
        {
            return GetTypeFromTypeNode(typeArguments[index]);
        }

        return GetEffectiveTypeArguments(node, typeParameters)[index];
    }

    /// <summary>ported from: src/compiler/checker.ts:42675-42677 — <c>getEffectiveTypeArguments</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>fillMissingTypeArguments</c> is the landed sibling member of this class (checker.ts:16130), whose
    /// answer is <c>List&lt;IType&gt;?</c>; the reference's <c>map(node.typeArguments!, getTypeFromTypeNode)</c> is
    /// <c>Core.Map</c> over the <see cref="INodeWithTypeArguments"/> slot with the index-taking callback the port's
    /// <c>Map</c> requires.
    /// </remarks>
    internal List<IType> GetEffectiveTypeArguments(Node node, List<ITypeParameter> typeParameters)
        => FillMissingTypeArguments(
            Core.Map(((INodeWithTypeArguments)node).TypeArguments, (typeArgument, _) => GetTypeFromTypeNode(typeArgument))!,
            typeParameters,
            GetMinTypeArgumentCount(typeParameters),
            Utilities.IsInJSFile(node))!;

    /// <summary>ported from: src/compiler/checker.ts:42679-42699 — <c>checkTypeArgumentConstraints</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>typeArguments</c> local is <c>Type[] | undefined</c> and doubles as the "did I compute them
    /// yet" flag (<c>if (!typeArguments)</c>); the port keeps the same <c>null</c> test over a
    /// <c>List&lt;IType&gt;?</c>. The <c>node.typeArguments![i]</c> read is again the
    /// <see cref="INodeWithTypeArguments"/> slot.
    /// </remarks>
    internal bool CheckTypeArgumentConstraints(Node node, List<ITypeParameter> typeParameters)
    {
        List<IType>? typeArguments = null;
        TypeMapper? mapper = null;
        var result = true;
        for (var i = 0; i < typeParameters.Count; i++)
        {
            var constraint = GetConstraintOfTypeParameter(typeParameters[i]);
            if (constraint is not null)
            {
                if (typeArguments is null)
                {
                    typeArguments = GetEffectiveTypeArguments(node, typeParameters);
                    mapper = CreateTypeMapper(typeParameters, typeArguments);
                }

                // PORT-NOTE: the reference's `node.typeArguments![i]` (checker.ts:42693) is an array read JS answers with
                // `undefined` when the node supplies fewer type arguments than the declaration has parameters — the case
                // in which `getEffectiveTypeArguments` returns the supplied list unpadded; the arity error itself is
                // reported elsewhere. The port states that absent slot as a null error node, which
                // `checkTypeAssignableTo` reads as "check the relation, report nothing here".
                var errorNode = ((INodeWithTypeArguments)node).TypeArguments is { } typeArgumentNodes && i < typeArgumentNodes.Count
                    ? (Node)typeArgumentNodes[i]
                    : null;
                result = result && CheckTypeAssignableTo(
                    typeArguments[i],
                    InstantiateType(constraint, mapper)!,
                    errorNode,
                    Diagnostics.Type_0_does_not_satisfy_the_constraint_1);
            }
        }

        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:42701-42707 — <c>getTypeParametersForTypeAndSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's mixed <c>&amp;&amp;</c>/<c>||</c> chain is the port's nested conditional (the flags
    /// test and the symbol-links test both have to hold before the links' answer wins), and
    /// <c>(type as TypeReference).target.localTypeParameters</c> is the same read through
    /// <see cref="ITypeReference"/>/<see cref="IGenericType"/>.
    /// </remarks>
    internal List<ITypeParameter>? GetTypeParametersForTypeAndSymbol(IType type, ISymbol symbol)
    {
        if (!IsErrorType(type))
        {
            var links = GetSymbolLinks(symbol);
            return (symbol.Flags & SymbolFlags.TypeAlias) != 0 && links.TypeParameters is not null
                ? links.TypeParameters
                : (GetObjectFlags(type) & ObjectFlags.Reference) != 0
                    ? ((ITypeReference)type).Target!.LocalTypeParameters
                    : null;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:42709-42718 — <c>getTypeParametersForTypeReferenceOrImport</c>.</summary>
    internal List<ITypeParameter>? GetTypeParametersForTypeReferenceOrImport(Node node)
    {
        var type = GetTypeFromTypeNode((ITypeNode)node);
        if (!IsErrorType(type))
        {
            var symbol = GetNodeLinks(node).ResolvedSymbol;
            if (symbol is not null)
            {
                return GetTypeParametersForTypeAndSymbol(type, symbol);
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:42723-42752 — <c>getUninstantiatedSignatures</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>CallLikeExpression</c> union is a <see cref="Node"/> here; each arm reads the slot that arm
    /// declares (<c>expression</c>, <c>tagName</c>, <c>tag</c>) through its own class after the kind test, and the
    /// <c>isJsxIntrinsicTagName</c> guard is the sibling checker member of this slice's JSX half (checker.ts:33802).
    /// The reference's switch is exhaustive over that union and falls off its end; C# needs a terminator, and the empty
    /// list is the answer the reference's own <c>BinaryExpression</c>/<c>JsxOpeningFragment</c> arm gives.
    /// </remarks>
    internal List<ISignature> GetUninstantiatedSignatures(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.CallExpression:
            case SyntaxKind.Decorator:
                return GetSignaturesOfType(
                    GetTypeOfExpression(((CallExpression)node).Expression!),
                    SignatureKind.Call);
            case SyntaxKind.NewExpression:
                return GetSignaturesOfType(
                    GetTypeOfExpression(((NewExpression)node).Expression!),
                    SignatureKind.Construct);
            case SyntaxKind.JsxSelfClosingElement:
            case SyntaxKind.JsxOpeningElement:
                if (IsJsxIntrinsicTagName(((JsxSelfClosingElement)node).TagName!))
                {
                    return [];
                }

                return GetSignaturesOfType(
                    GetTypeOfExpression(((JsxSelfClosingElement)node).TagName!),
                    SignatureKind.Call);
            case SyntaxKind.TaggedTemplateExpression:
                return GetSignaturesOfType(
                    GetTypeOfExpression(((TaggedTemplateExpression)node).Tag!),
                    SignatureKind.Call);
            case SyntaxKind.BinaryExpression:
            case SyntaxKind.JsxOpeningFragment:
                return [];
        }

        return [];
    }

    /// <summary>ported from: src/compiler/checker.ts:42754-42763 — <c>getTypeParameterConstraintForPositionAcrossSignatures</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>flatMap</c> callback answers <c>[]</c>/<c>[x]</c>; <c>Core.FlatMap</c> takes
    /// <c>object?</c> and the port's <c>null</c> is its "nothing to add" (the same falsy test), with the
    /// <c>signature.typeParameters?.[position]</c> optional read kept as it is.
    /// </remarks>
    internal IType GetTypeParameterConstraintForPositionAcrossSignatures(List<ISignature> signatures, int position)
    {
        var relevantTypeParameterConstraints = Core.FlatMap<ISignature, IType>(signatures, (signature, _) =>
        {
            var relevantTypeParameter = signature.TypeParameters?[position];
            if (relevantTypeParameter is null)
            {
                return null;
            }

            var relevantConstraint = GetConstraintOfTypeParameter(relevantTypeParameter);
            if (relevantConstraint is null)
            {
                return null;
            }

            return (object?)new List<IType> { relevantConstraint };
        });
        return GetUnionType((List<IType>)relevantTypeParameterConstraints);
    }

    /// <summary>ported from: src/compiler/checker.ts:42765-42776 — <c>checkTypeReferenceNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>isInJSDoc</c> (utilities.ts:3819) is <c>Utilities.IsInJSDoc</c> and <c>scanTokenAtPosition</c>
    /// (utilities.ts:2519) is <c>Utilities.ScanTokenAtPosition</c>; <c>skipTrivia</c> is
    /// <c>ScannerUtilities.SkipTrivia</c> (<c>Lexer/ScannerUtilities.cs</c>) and <c>grammarErrorAtPos</c> is the
    /// earlier checker member. The <c>typeName</c> reads are the <see cref="TypeReferenceNode"/> arm the kind test
    /// selected, and <c>typeArguments</c> is the <see cref="INodeWithTypeArguments"/> slot.
    /// </remarks>
    internal void CheckTypeReferenceNode(Node node)
    {
        CheckGrammarTypeArguments(node, ((INodeWithTypeArguments)node).TypeArguments);
        if (node.Kind == SyntaxKind.TypeReference && !Utilities.IsInJSFile(node) && !Utilities.IsInJSDoc(node) && ((INodeWithTypeArguments)node).TypeArguments is not null && ((TypeReferenceNode)node).TypeName!.End != ((INodeWithTypeArguments)node).TypeArguments!.Pos)
        {
            // If there was a token between the type name and the type arguments, check if it was a DotToken
            var sourceFile = Utilities.GetSourceFileOfNode(node)!;
            if (Utilities.ScanTokenAtPosition(sourceFile, ((TypeReferenceNode)node).TypeName!.End) == SyntaxKind.DotToken)
            {
                GrammarErrorAtPos(node, ScannerUtilities.SkipTrivia(sourceFile.Text!, ((TypeReferenceNode)node).TypeName!.End), 1, Diagnostics.JSDoc_types_can_only_be_used_inside_documentation_comments);
            }
        }

        Core.ForEach(((INodeWithTypeArguments)node).TypeArguments, (ITypeNode typeArgument, int _) =>
        {
            CheckSourceElement(typeArgument);
            return (object?)null;
        });
        CheckTypeReferenceOrImport(node);
    }

    /// <summary>ported from: src/compiler/checker.ts:42778-42800 — <c>checkTypeReferenceOrImport</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>TypeReferenceNode | ExpressionWithTypeArguments | ImportTypeNode</c> is a
    /// <see cref="Node"/>; the deprecated-declaration scan is the port's <c>Core.Map(..., (d, _) =&gt; (Node)d)</c>
    /// idiom for handing a <c>List&lt;IDeclaration&gt;</c> to <c>AddDeprecatedSuggestion</c>'s
    /// <c>IList&lt;Node&gt;</c> (<c>Checker.Check.Modules.cs:412</c>).
    /// </remarks>
    internal void CheckTypeReferenceOrImport(Node node)
    {
        var type = GetTypeFromTypeNode((ITypeNode)node);
        if (!IsErrorType(type))
        {
            if (((INodeWithTypeArguments)node).TypeArguments is not null)
            {
                AddLazyDiagnostic(() =>
                {
                    var typeParameters = GetTypeParametersForTypeReferenceOrImport(node);
                    if (typeParameters is not null)
                    {
                        CheckTypeArgumentConstraints(node, typeParameters);
                    }
                });
            }

            var symbol = GetNodeLinks(node).ResolvedSymbol;
            if (symbol is not null)
            {
                if (Core.Some(symbol.Declarations, declaration => Utilities.IsTypeDeclaration((Node)declaration) && (((Node)declaration).Flags & NodeFlags.Deprecated) != 0))
                {
                    AddDeprecatedSuggestion(
                        GetDeprecatedSuggestionNode(node),
                        Core.Map(symbol.Declarations, (declaration, _) => (Node)declaration)!,
                        symbol.EscapedName!);
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:42802-42856 — <c>getTypeArgumentConstraint</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>typeArgumentPosition</c> is <c>number | undefined</c> and its <c>!== undefined</c> test is
    /// the port's <c>is not null</c> over <c>int?</c>; <c>hasTypeArguments</c> (utilities.ts:4837) is
    /// <c>Utilities.HasTypeArguments</c>, <c>isTypeReferenceType</c> (utilitiesPublic.ts:2597) is
    /// <c>Utilities.IsTypeReferenceType</c> and <c>isCallLikeExpression</c> (utilitiesPublic.ts:1968) is
    /// <c>Utilities.IsCallLikeExpression</c>. The <c>typeArguments.indexOf(node)</c> read is the
    /// <see cref="INodeWithTypeArguments"/> slot (the <c>Array.isArray</c> half of the reference's test is the
    /// non-null pattern on that slot), and <c>isDecorator</c>/<c>isExpressionWithTypeArguments</c>/
    /// <c>isExpressionStatement</c> are the port's node tests.
    /// </remarks>
    internal IType? GetTypeArgumentConstraint(ITypeNode node)
    {
        int? typeArgumentPosition = null;
        var parent = ((Node)node).Parent!;
        if (Utilities.HasTypeArguments(parent) && parent is INodeWithTypeArguments { TypeArguments: not null })
        {
            typeArgumentPosition = ((INodeWithTypeArguments)parent).TypeArguments!.IndexOf(node);
        }

        if (typeArgumentPosition is not null)
        {
            // The node could be a type argument of a call, an instantiation expression, a decorator, or a
            // generic type instantiation.
            if (Utilities.IsCallLikeExpression(parent))
            {
                return GetTypeParameterConstraintForPositionAcrossSignatures(
                    GetUninstantiatedSignatures(parent),
                    typeArgumentPosition.Value);
            }

            if (NodeTests.IsDecorator(parent.Parent!))
            {
                return GetTypeParameterConstraintForPositionAcrossSignatures(
                    GetUninstantiatedSignatures(parent.Parent!),
                    typeArgumentPosition.Value);
            }

            if (NodeTests.IsExpressionWithTypeArguments(parent) && NodeTests.IsExpressionStatement(parent.Parent!))
            {
                var uninstantiatedType = CheckExpression(((ExpressionWithTypeArguments)parent).Expression!);

                var callConstraint = GetTypeParameterConstraintForPositionAcrossSignatures(
                    GetSignaturesOfType(uninstantiatedType, SignatureKind.Call),
                    typeArgumentPosition.Value);
                var constructConstraint = GetTypeParameterConstraintForPositionAcrossSignatures(
                    GetSignaturesOfType(uninstantiatedType, SignatureKind.Construct),
                    typeArgumentPosition.Value);

                // An instantiation expression instantiates both call and construct signatures, so
                // if both exist type arguments must be assignable to both constraints.
                if ((constructConstraint.Flags & TypeFlags.Never) != 0)
                {
                    return callConstraint;
                }

                if ((callConstraint.Flags & TypeFlags.Never) != 0)
                {
                    return constructConstraint;
                }

                return GetIntersectionType([callConstraint, constructConstraint]);
            }

            if (Utilities.IsTypeReferenceType(parent))
            {
                var typeParameters = GetTypeParametersForTypeReferenceOrImport(parent);
                if (typeParameters is null)
                {
                    return null;
                }

                var relevantTypeParameter = typeParameters[typeArgumentPosition.Value];
                var constraint = GetConstraintOfTypeParameter(relevantTypeParameter);
                return constraint is not null
                    ? InstantiateType(
                        constraint,
                        CreateTypeMapper(typeParameters, GetEffectiveTypeArguments(parent, typeParameters)))
                    : null;
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:42858-42860 — <c>checkTypeQuery</c>.</summary>
    internal void CheckTypeQuery(TypeQueryNode node)
    {
        GetTypeFromTypeQueryNode(node);
    }

    /// <summary>ported from: src/compiler/checker.ts:42862-42872 — <c>checkTypeLiteral</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's nested function is the port's local function; <c>checkIndexConstraints</c> is the
    /// earlier checker member and the <c>type.symbol</c> read is the type model's non-optional slot.
    /// </remarks>
    internal void CheckTypeLiteral(TypeLiteralNode node)
    {
        Core.ForEach(node.Members, (ITypeElement member, int _) =>
        {
            CheckSourceElement((Node)member);
            return (object?)null;
        });
        AddLazyDiagnostic(CheckTypeLiteralDiagnostics);

        void CheckTypeLiteralDiagnostics()
        {
            var type = GetTypeFromTypeLiteralOrFunctionOrConstructorTypeNode(node);
            CheckIndexConstraints(type, type.Symbol!);
            CheckTypeForDuplicateIndexSignatures(node);
            CheckObjectTypeForDuplicateDeclarations(node);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:42874-42876 — <c>checkArrayType</c>.</summary>
    internal void CheckArrayType(ArrayTypeNode node)
    {
        CheckSourceElement(node.ElementType);
    }

    /// <summary>ported from: src/compiler/checker.ts:42878-42914 — <c>checkTupleType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>node.elements</c> are <see cref="Node"/>s, so <c>getTupleElementFlags</c> takes the node cast to
    /// <see cref="ITypeNode"/>; the reference's <c>(e as RestTypeNode | NamedTupleMember).type</c> union read is the
    /// kind switch inline, and <c>type.target.combinedFlags</c> is the same read through
    /// <see cref="ITupleType"/> — with the parentheses C# needs around a bitwise test against a constant.
    /// </remarks>
    internal void CheckTupleType(TupleTypeNode node)
    {
        var seenOptionalElement = false;
        var seenRestElement = false;
        foreach (var e in node.Elements!)
        {
            var flags = GetTupleElementFlags((ITypeNode)e);
            if ((flags & ElementFlags.Variadic) != 0)
            {
                var type = GetTypeFromTypeNode(e.Kind == SyntaxKind.RestType ? ((RestTypeNode)e).Type! : ((NamedTupleMember)e).Type!);
                if (!IsArrayLikeType(type))
                {
                    Error(e, Diagnostics.A_rest_element_type_must_be_an_array_type);
                    break;
                }

                if (IsArrayType(type) || (IsTupleType(type) && ((((ITupleType)((ITypeReference)type).Target!).CombinedFlags & ElementFlags.Rest) != 0)))
                {
                    flags |= ElementFlags.Rest;
                }
            }

            if ((flags & ElementFlags.Rest) != 0)
            {
                if (seenRestElement)
                {
                    GrammarErrorOnNode(e, Diagnostics.A_rest_element_cannot_follow_another_rest_element);
                    break;
                }

                seenRestElement = true;
            }
            else if ((flags & ElementFlags.Optional) != 0)
            {
                if (seenRestElement)
                {
                    GrammarErrorOnNode(e, Diagnostics.An_optional_element_cannot_follow_a_rest_element);
                    break;
                }

                seenOptionalElement = true;
            }
            else if ((flags & ElementFlags.Required) != 0 && seenOptionalElement)
            {
                GrammarErrorOnNode(e, Diagnostics.A_required_element_cannot_follow_an_optional_element);
                break;
            }
        }

        Core.ForEach(node.Elements, (Node element, int _) =>
        {
            CheckSourceElement(element);
            return (object?)null;
        });
        GetTypeFromTypeNode(node);
    }

    /// <summary>ported from: src/compiler/checker.ts:42916-42919 — <c>checkUnionOrIntersectionType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>UnionOrIntersectionTypeNode</c> union, which has no C# type — the port's
    /// faces are <see cref="UnionTypeNode"/> and <see cref="IntersectionTypeNode"/> — so the parameter is a
    /// <see cref="Node"/> and <c>types</c> is read through the arm the kind test selects (the ruling
    /// <c>Utilities.IsSuperProperty</c> records for such a union).
    /// </remarks>
    internal void CheckUnionOrIntersectionType(Node node)
    {
        var types = node.Kind == SyntaxKind.UnionType ? ((UnionTypeNode)node).Types : ((IntersectionTypeNode)node).Types;
        Core.ForEach(types, (ITypeNode type, int _) =>
        {
            CheckSourceElement(type);
            return (object?)null;
        });
        GetTypeFromTypeNode((ITypeNode)node);
    }

    /// <summary>ported from: src/compiler/checker.ts:42921-42950 — <c>checkIndexedAccessIndexType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>accessNode: IndexedAccessTypeNode | ElementAccessExpression</c> is a <see cref="Node"/>
    /// (the one member read, <c>kind</c>, is on the base), and the two <c>(type as IndexedAccessType)</c> reads are the
    /// port's <see cref="IIndexedAccessType"/> face. <c>everyType</c> and <c>forEachType</c> are the sibling
    /// type-utility members (<c>checker.ts:28518</c>/<c>:28510</c>), whose callback shapes are the reference's
    /// (<c>t =&gt; boolean</c> and <c>t =&gt; T | undefined</c>). <c>isGenericObjectType</c> (checker.ts:19470) is an
    /// earlier checker member, <c>getDeclarationModifierFlagsFromSymbol</c> is <c>Utilities.</c>'s, and
    /// <c>unescapeLeadingUnderscores</c> is <c>Utilities.UnescapeLeadingUnderscores</c>.
    /// </remarks>
    internal IType CheckIndexedAccessIndexType(IType type, Node accessNode)
    {
        if ((type.Flags & TypeFlags.IndexedAccess) == 0)
        {
            return type;
        }

        // Check if the index type is assignable to 'keyof T' for the object type.
        var objectType = ((IIndexedAccessType)type).ObjectType!;
        var indexType = ((IIndexedAccessType)type).IndexType!;
        var hasNumberIndexInfo = GetIndexInfoOfType(objectType, _numberType) is not null;
        if (EveryType(indexType, t => IsTypeAssignableTo(t, GetIndexType(objectType, IndexFlags.None)) || (hasNumberIndexInfo && IsApplicableIndexType(t, _numberType))))
        {
            if (
                accessNode.Kind == SyntaxKind.ElementAccessExpression && Utilities.IsAssignmentTarget(accessNode) &&
                (GetObjectFlags(objectType) & ObjectFlags.Mapped) != 0 && (GetMappedTypeModifiers((IMappedType)objectType) & MappedTypeModifiers.IncludeReadonly) != 0
            )
            {
                Error(accessNode, Diagnostics.Index_signature_in_type_0_only_permits_reading, Diagnostic.DeferArg(() => TypeToString(objectType)));
            }

            return type;
        }

        if (IsGenericObjectType(objectType))
        {
            var propertyName = GetPropertyNameFromIndex(indexType, accessNode);
            if (propertyName is not null)
            {
                var propertySymbol = ForEachType(GetApparentType(objectType), t => GetPropertyOfType(t, propertyName));
                if (propertySymbol is not null && (Utilities.GetDeclarationModifierFlagsFromSymbol(propertySymbol) & ModifierFlags.NonPublicAccessibilityModifier) != 0)
                {
                    Error(accessNode, Diagnostics.Private_or_protected_member_0_cannot_be_accessed_on_a_type_parameter, Utilities.UnescapeLeadingUnderscores(propertyName));
                    return _errorType;
                }
            }
        }

        Error(accessNode, Diagnostics.Type_0_cannot_be_used_to_index_type_1, Diagnostic.DeferArg(() => TypeToString(indexType)), Diagnostic.DeferArg(() => TypeToString(objectType)));
        return _errorType;
    }

    /// <summary>ported from: src/compiler/checker.ts:42952-42956 — <c>checkIndexedAccessType</c>.</summary>
    internal void CheckIndexedAccessType(IndexedAccessTypeNode node)
    {
        CheckSourceElement(node.ObjectType);
        CheckSourceElement(node.IndexType);
        CheckIndexedAccessIndexType(GetTypeFromIndexedAccessTypeNode(node), node);
    }

    /// <summary>ported from: src/compiler/checker.ts:42958-42977 — <c>checkMappedType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>reportImplicitAny</c>'s landed signature takes an <see cref="IDeclaration"/> and
    /// <see cref="MappedTypeNode"/> implements it, so the reference's bare <c>node</c> is cast; the mapped-type reads
    /// are the <see cref="IMappedType"/> face and <c>getEffectiveConstraintOfTypeParameter</c> is
    /// <c>Utilities.GetEffectiveConstraintOfTypeParameter</c> (checker.ts:16772).
    /// </remarks>
    internal void CheckMappedType(MappedTypeNode node)
    {
        CheckGrammarMappedType(node);
        CheckSourceElement(node.TypeParameter);
        CheckSourceElement(node.NameType);
        CheckSourceElement(node.Type);

        if (node.Type is null)
        {
            ReportImplicitAny((IDeclaration)node, _anyType);
        }

        var type = (IMappedType)GetTypeFromMappedTypeNode(node);
        var nameType = GetNameTypeFromMappedType(type);
        if (nameType is not null)
        {
            CheckTypeAssignableTo(nameType, _stringNumberSymbolType, (Node?)node.NameType);
        }
        else
        {
            var constraintType = GetConstraintTypeFromMappedType(type);
            CheckTypeAssignableTo(constraintType, _stringNumberSymbolType, (Node?)Utilities.GetEffectiveConstraintOfTypeParameter(node.TypeParameter!));
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:42979-42983 — <c>checkGrammarMappedType</c>.</summary>
    /// <remarks>PORT-NOTE: <c>node.members?.length</c> is the port's <c>Core.Length</c> over the optional list.</remarks>
    internal bool CheckGrammarMappedType(MappedTypeNode node)
    {
        if (Core.Length(node.Members) != 0)
        {
            return GrammarErrorOnNode((Node)node.Members![0], Diagnostics.A_mapped_type_may_not_declare_properties_or_methods);
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:42985-42987 — <c>checkThisType</c>.</summary>
    internal void CheckThisType(ThisTypeNode node)
    {
        GetTypeFromThisTypeNode(node);
    }

    /// <summary>ported from: src/compiler/checker.ts:42989-42992 — <c>checkTypeOperator</c>.</summary>
    internal void CheckTypeOperator(TypeOperatorNode node)
    {
        CheckGrammarTypeOperatorNode(node);
        CheckSourceElement(node.Type);
    }

    /// <summary>ported from: src/compiler/checker.ts:42994-42996 — <c>checkConditionalType</c>.</summary>
    internal void CheckConditionalType(ConditionalTypeNode node)
    {
        ForEachChildTable.ForEachChild<object>(node, child =>
        {
            CheckSourceElement(child);
            return null;
        });
    }

    /// <summary>ported from: src/compiler/checker.ts:42998-43020 — <c>checkInferType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>findAncestor</c> is <c>Utilities.FindAncestor</c> (utilities.ts:970) and its predicate takes a
    /// <see cref="Node"/> (the reference's <c>n.parent &amp;&amp; …</c> is the port's <c>n.Parent is not null &amp;&amp;
    /// …</c>); <c>getDeclarationsOfKind</c> (utilities.ts:639) is <c>Utilities.GetDeclarationsOfKind&lt;T&gt;</c> and
    /// <c>typeParametersChecked</c> is <see cref="ISymbolLinks.TypeParametersChecked"/>. <c>areTypeParametersIdentical</c>
    /// (checker.ts:46982) is an earlier checker member, called with the reference's three arguments.
    /// </remarks>
    internal void CheckInferType(InferTypeNode node)
    {
        if (Utilities.FindAncestor(node, n => n.Parent is not null && n.Parent.Kind == SyntaxKind.ConditionalType && ReferenceEquals(((ConditionalTypeNode)n.Parent).ExtendsType, n)) is null)
        {
            GrammarErrorOnNode(node, Diagnostics.infer_declarations_are_only_permitted_in_the_extends_clause_of_a_conditional_type);
        }

        CheckSourceElement(node.TypeParameter);
        var symbol = GetSymbolOfDeclaration(node.TypeParameter!);
        if (symbol.Declarations is not null && symbol.Declarations.Count > 1)
        {
            var links = GetSymbolLinks(symbol);
            if (links.TypeParametersChecked != true)
            {
                links.TypeParametersChecked = true;
                var typeParameter = GetDeclaredTypeOfTypeParameter(symbol);
                var declarations = Utilities.GetDeclarationsOfKind<TypeParameterDeclaration>(symbol, SyntaxKind.TypeParameter);
                if (!AreTypeParametersIdentical(declarations, [typeParameter], declaration => [declaration]))
                {
                    // Report an error on every conflicting declaration.
                    var name = Diagnostic.DeferArg(() => SymbolToString(symbol));
                    foreach (var declaration in declarations)
                    {
                        Error(declaration.Name, Diagnostics.All_declarations_of_0_must_have_identical_constraints, name);
                    }
                }
            }
        }

        RegisterForUnusedIdentifiersCheck(node);
    }

    /// <summary>ported from: src/compiler/checker.ts:43022-43029 — <c>checkTemplateLiteralType</c>.</summary>
    /// <remarks>PORT-NOTE: the constraint the spans are checked against is the class's <c>_templateConstraintType</c>
    /// slot (checker.ts:1566).</remarks>
    internal void CheckTemplateLiteralType(TemplateLiteralTypeNode node)
    {
        foreach (var span in node.TemplateSpans!)
        {
            CheckSourceElement(span.Type);
            var type = GetTypeFromTypeNode(span.Type);
            CheckTypeAssignableTo(type, _templateConstraintType, (Node?)span.Type);
        }

        GetTypeFromTypeNode(node);
    }

    /// <summary>ported from: src/compiler/checker.ts:43031-43041 — <c>checkImportType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>getResolutionModeOverride</c> is the static <c>CompilerHost.GetResolutionModeOverride</c>
    /// (program.ts:992, <c>Program/Program.Host.cs:1207</c>), the port's home for that module-level function, and its
    /// <c>grammarErrorOnNode</c> parameter is stated as the lambda the port's <c>GrammarErrorOnNode</c> (which carries
    /// the reference's trailing <c>arg0</c>) needs to satisfy <c>Action&lt;Node, DiagnosticMessage&gt;</c>.
    /// </remarks>
    internal void CheckImportType(ImportTypeNode node)
    {
        CheckSourceElement(node.Argument);

        if (node.Attributes is not null)
        {
            if (node.Attributes.Token != SyntaxKind.WithKeyword && _compilerOptions.IgnoreDeprecations != "6.0")
            {
                GrammarErrorOnFirstToken(node.Attributes, Diagnostics.Import_assertions_have_been_replaced_by_import_attributes_Use_with_instead_of_assert);
            }

            Program.GetResolutionModeOverride(node.Attributes, (grammarNode, message) => GrammarErrorOnNode(grammarNode, message));
        }

        CheckTypeReferenceOrImport(node);
    }

    /// <summary>ported from: src/compiler/checker.ts:43043-43055 — <c>checkNamedTupleMember</c>.</summary>
    internal void CheckNamedTupleMember(NamedTupleMember node)
    {
        if (node.DotDotDotToken is not null && node.QuestionToken is not null)
        {
            GrammarErrorOnNode(node, Diagnostics.A_tuple_member_cannot_be_both_optional_and_rest);
        }

        if (node.Type!.Kind == SyntaxKind.OptionalType)
        {
            GrammarErrorOnNode((Node?)node.Type, Diagnostics.A_labeled_tuple_element_is_declared_as_optional_with_a_question_mark_after_the_name_and_before_the_colon_rather_than_after_the_type);
        }

        if (node.Type.Kind == SyntaxKind.RestType)
        {
            GrammarErrorOnNode((Node?)node.Type, Diagnostics.A_labeled_tuple_element_is_declared_as_rest_with_a_before_the_name_rather_than_before_the_type);
        }

        CheckSourceElement(node.Type);
        GetTypeFromTypeNode(node);
    }
}
