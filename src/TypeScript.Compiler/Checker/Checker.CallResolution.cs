namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:35569-37809 — the call- and signature-resolution block
//   (callLikeExpressionMayHaveTypeArguments … getAssignedJSPrototype, stopping before checkCallExpression).
/// <summary>
/// Call resolution: the candidate-signature reordering, the arity and type-argument-arity checks, the effective
/// argument lists (<c>getEffectiveCallArguments</c>/<c>getEffectiveDecoratorArguments</c>), the applicability error
/// (<c>getSignatureApplicabilityError</c>, including the JSX call-like variant), <c>resolveCall</c>'s overload
/// resolution with its error reporting, the per-kind resolvers <c>resolveSignature</c> dispatches to
/// (<c>resolveCallExpression</c>, <c>resolveNewExpression</c>, <c>resolveTaggedTemplateExpression</c>,
/// <c>resolveDecorator</c>, <c>resolveJsxOpeningLikeElement</c>, <c>resolveInstanceofExpression</c>),
/// <c>getResolvedSignature</c>'s caching, and the JavaScript constructor/expando/prototype helpers below it.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: the reference's <c>CallLikeExpression</c> (<c>CallExpression | NewExpression |
/// TaggedTemplateExpression | Decorator | JsxOpeningElement | JsxSelfClosingElement | JsxOpeningFragment |
/// BinaryExpression</c> for <c>instanceof</c>), <c>JsxCallLike</c> and <c>JsxOpeningLikeElement</c> are union type
/// aliases. The port's generated model (<c>Model/NodeClasses.cs</c>, <c>Model/NodeInterfaces.cs</c>) materializes a
/// capability interface only for unions the generator could name from a TypeScript interface; none of these three has
/// one, and <c>Model/**</c> is generated. So every union-typed parameter here is <see cref="Node"/> and every
/// kind-specific slot is read after the reference's own kind test, with an explicit cast where the port's node model
/// declares the slot on one arm only (<c>Model/**</c> classes are not structurally compatible: a
/// <c>JsxOpeningElement</c> is not a <c>CallExpression</c>, and <c>ITypeNode</c>/<c>IDeclaration</c> are interfaces
/// that a <see cref="Node"/> parameter does not accept without a cast).
/// </para>
/// <para>
/// PORT-NOTE: <c>getResolvedSignatureWorker</c> (checker.ts:2037-2042) — the public wrapper that sets and clears
/// <c>apparentArgumentCount</c> around the internal <c>getResolvedSignature</c> — lies outside this band, so this file
/// only *reads* <see cref="_apparentArgumentCount"/> in <see cref="PickLongestCandidateSignature"/> (checker.ts:36953)
/// and does not declare the wrapper; it is the one member the public
/// <c>ITypeChecker.GetResolvedSignature(node, candidatesOutArray, argumentCount)</c> surface still needs.
/// </para>
/// <para>
/// PORT-NOTE: the checker.ts members this file calls but does not declare, each with its reference range, so the
/// slice that owns it can close it: <c>checkExpression</c> (41753), <c>checkExpressionCached</c> (41335),
/// <c>checkExpressionWithContextualType</c> (41315), <c>checkNonNullExpression</c> (34639),
/// <c>checkNonNullTypeWithReporter</c> (34692), <c>reportCannotInvokePossiblyNullOrUndefinedError</c> (34682),
/// <c>checkSuperExpression</c> (31630), <c>getJsxNamespace</c> (2436), <c>getSymbolAtLocation</c> (50190),
/// <c>getTypeOfNode</c> (50382), <c>hasInferenceCandidates</c> (41596), <c>isContextSensitive</c> (21180),
/// <c>signatureHasLiteralTypes</c> (54256), <c>skippedGenericFunction</c> (41587),
/// <c>tryGetRestTypeOfSignature</c> (16552), <c>typeHasCallOrConstructSignatures</c> (50563);
/// plus the <c>utilities.ts</c>/<c>utilitiesPublic.ts</c> members <c>isSuperCall</c> (utilities.ts:2701),
/// <c>getSelectedEffectiveModifierFlags</c> (utilities.ts:7241), <c>isRightSideOfInstanceofExpression</c>
/// (utilities.ts:7592), <c>minAndMax</c> (utilities.ts:10362) and <c>isJsxCallLike</c> (utilitiesPublic.ts:2495).
/// <c>isNodeWithinClass</c>, <c>checkIteratedTypeOrElementType</c>, <c>getAwaitedType</c> and
/// <c>getAwaitedTypeOfPromise</c> are landed by the M6Check slice and are ordinary qualified/unqualified calls
/// here.
/// </para>
/// <para>
/// PORT-NOTE: <c>getDiagnosticForCallNode</c>'s <c>message</c> parameter is TS's
/// <c>DiagnosticMessage | DiagnosticMessageChain</c> union; the port takes <c>object</c> and tests for
/// <see cref="IDiagnosticMessageChain"/>, the same treatment <c>Checker.Diagnostics.cs</c>'s
/// <c>errorOrSuggestion</c> gives the same union. Its return type is <see cref="Diagnostic"/> — the port's single
/// diagnostic class implements both <c>IDiagnosticWithDetachedLocation</c> and <c>IDiagnosticWithLocation</c>
/// (<c>Core/Diagnostic.cs:86</c>), so TS's <c>DiagnosticWithLocation</c> has no separate counterpart.
/// </para>
/// <para>
/// PORT-NOTE: the reference builds the arrays of this block with <c>Array#splice</c>/<c>Array#slice</c> and spreads;
/// the port uses <c>List&lt;T&gt;.Insert</c>/<c>Core.Slice</c>/collection expressions, which have the same values. No
/// member of this band sorts, so no <c>Core.StableSort</c> call is needed (the reference's only ordering here is the
/// splice index <see cref="ReorderCandidates"/> computes, and the position-first ordering of
/// <c>Core.InsertSorted</c>/<c>Utilities.CreateDiagnosticForNodeArray</c>'s callers).
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // Candidate reordering and the arity checks (checker.ts:35569-35762)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:35569-35571 — <c>callLikeExpressionMayHaveTypeArguments</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to the <c>CallLikeExpression</c> arms that carry <c>typeArguments</c>; the port's
    /// model has no <c>ICallLikeExpression</c> union interface, so it answers <c>bool</c> over <see cref="Node"/> (see
    /// the class remarks). <c>utilities.ts</c>'s <c>isCallOrNewExpression</c> is a <c>Utilities</c> member here.
    /// </remarks>
    internal bool CallLikeExpressionMayHaveTypeArguments(Node node)
        => Utilities.IsCallOrNewExpression(node) || NodeTests.IsTaggedTemplateExpression(node) || Utilities.IsJsxOpeningLikeElement(node);

    /// <summary>ported from: src/compiler/checker.ts:35573-35595 — <c>resolveUntypedCall</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>forEach(node.typeArguments, checkSourceElement)</c> reads the union slot through the guard above;
    /// the port spells the same narrowing as a switch over the five arms <c>callLikeExpressionMayHaveTypeArguments</c>
    /// admits, and the <c>argument =&gt; checkExpression(argument)</c> callback keeps <see cref="Core.ForEach"/>'s
    /// callback shape.
    /// </remarks>
    internal ISignature ResolveUntypedCall(Node node)
    {
        if (CallLikeExpressionMayHaveTypeArguments(node))
        {
            // Check type arguments even though we will give an error that untyped calls may not accept type arguments.
            // This gets us diagnostics for the type arguments and marks them as referenced.
            Core.ForEach(TypeArgumentsOfUnion(node), (ITypeNode typeArgument, int _) =>
            {
                CheckSourceElement(typeArgument);
                return (object?)null;
            });
        }

        if (node.Kind == SyntaxKind.TaggedTemplateExpression)
        {
            CheckExpression(((TaggedTemplateExpression)node).Template!);
        }
        else if (Utilities.IsJsxOpeningLikeElement(node))
        {
            CheckExpression(((node as JsxOpeningElement)?.Attributes ?? (node as JsxSelfClosingElement)?.Attributes)!);
        }
        else if (NodeTests.IsBinaryExpression(node))
        {
            CheckExpression(((BinaryExpression)node).Left!);
        }
        else if (Utilities.IsCallOrNewExpression(node))
        {
            Core.ForEach(ArgumentsOfUnion(node), (Expression argument, int _) =>
            {
                CheckExpression(argument);
                return (object?)null;
            });
        }

        return _anySignature;
    }

    /// <summary>ported from: src/compiler/checker.ts:35597-35600 — <c>resolveErrorCall</c>.</summary>
    internal ISignature ResolveErrorCall(Node node)
    {
        ResolveUntypedCall(node);
        return _unknownSignature;
    }

    /// <summary>ported from: src/compiler/checker.ts:35610-35654 — <c>reorderCandidates</c>.</summary>
    /// <remarks>
    /// <para>
    /// Re-order candidate signatures into the result array. Assumes the result array to be empty. The candidate list
    /// orders groups in reverse, but within a group signatures are kept in declaration order. A nit here is that we
    /// reorder only signatures that belong to the same symbol, so the order in which inherited signatures are processed
    /// is still preserved.
    /// </para>
    /// <para>
    /// PORT-NOTE: the reference's <c>lastSymbol || symbol === lastSymbol</c> and <c>lastParent &amp;&amp; parent ===
    /// lastParent</c> read <c>undefined</c> as "no previous declaration"; the port compares with
    /// <see cref="object.ReferenceEquals(object?, object?)"/>, which is the same relation over <c>null</c> and the
    /// identity TS's <c>===</c> gives symbols and nodes. <c>result.splice(spliceIndex, 0, x)</c> becomes
    /// <c>result.Insert(spliceIndex, x)</c> (no sort is involved, so the stable-sort rule does not apply here).
    /// <c>callChainFlags ? … : …</c> tests TS truthiness of a numeric enum, which the port states as
    /// <c>!= 0</c>.
    /// </para>
    /// </remarks>
    internal void ReorderCandidates(IList<ISignature> signatures, List<ISignature> result, SignatureFlags callChainFlags)
    {
        // interface A { (x: string): void }
        // interface B extends A { (x: 'foo'): string }
        // const b: B;
        // b('foo') // <- here overloads should be processed as [(x:'foo'): string, (x: string): void]
        Node? lastParent = null;
        ISymbol? lastSymbol = null;
        var cutoffIndex = 0;
        int? index = null;
        var specializedIndex = -1;
        int spliceIndex;
        Debug.Assert(result.Count == 0);
        foreach (var signature in signatures)
        {
            var symbol = signature.Declaration is not null ? GetSymbolOfDeclaration((IDeclaration)signature.Declaration) : null;
            var parent = signature.Declaration?.Parent;
            if (lastSymbol is null || ReferenceEquals(symbol, lastSymbol))
            {
                if (lastParent is not null && ReferenceEquals(parent, lastParent))
                {
                    index = index!.Value + 1;
                }
                else
                {
                    lastParent = parent;
                    index = cutoffIndex;
                }
            }
            else
            {
                // current declaration belongs to a different symbol
                // set cutoffIndex so re-orderings in the future won't change result set from 0 to cutoffIndex
                index = cutoffIndex = result.Count;
                lastParent = parent;
            }

            lastSymbol = symbol;

            // specialized signatures always need to be placed before non-specialized signatures regardless
            // of the cutoff position; see GH#1133
            if (SignatureHasLiteralTypes(signature))
            {
                specializedIndex++;
                spliceIndex = specializedIndex;
                // The cutoff index always needs to be greater than or equal to the specialized signature index
                // in order to prevent non-specialized signatures from being added before a specialized
                // signature.
                cutoffIndex++;
            }
            else
            {
                spliceIndex = index!.Value;
            }

            result.Insert(spliceIndex, callChainFlags != 0 ? GetOptionalCallSignature((Signature)signature, callChainFlags) : signature);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:35656-35658 — <c>isSpreadArgument</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows <c>Expression | undefined</c> to the spread forms; the port answers <c>bool</c>
    /// (the callers only test it), and reads <c>(arg as SyntheticExpression).isSpread</c> as <c>== true</c> because the
    /// generated slot is <c>bool?</c>.
    /// </remarks>
    internal static bool IsSpreadArgument(Expression? arg)
        => arg is not null && (arg.Kind == SyntaxKind.SpreadElement || arg.Kind == SyntaxKind.SyntheticExpression && ((SyntheticExpression)arg).IsSpread == true);

    /// <summary>ported from: src/compiler/checker.ts:35660-35662 — <c>getSpreadArgumentIndex</c>.</summary>
    internal static int GetSpreadArgumentIndex(IList<Expression> args)
        => Core.FindIndex(args, (Expression arg, int _) => IsSpreadArgument(arg));

    /// <summary>ported from: src/compiler/checker.ts:35664-35666 — <c>acceptsVoid</c>.</summary>
    internal static bool AcceptsVoid(IType t) => (t.Flags & TypeFlags.Void) != 0;

    /// <summary>ported from: src/compiler/checker.ts:35668-35670 — <c>acceptsVoidUndefinedUnknownOrAny</c>.</summary>
    internal static bool AcceptsVoidUndefinedUnknownOrAny(IType t)
        => (t.Flags & (TypeFlags.Void | TypeFlags.Undefined | TypeFlags.Unknown | TypeFlags.Any)) != 0;

    /// <summary>ported from: src/compiler/checker.ts:35672-35747 — <c>hasCorrectArity</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference reads <c>node.template</c>, <c>node.attributes</c> and <c>node.arguments</c> from the
    /// union after its own <c>node.kind</c>/<c>isJsxOpeningLikeElement</c>/<c>isCallOrNewExpression</c> tests; the port
    /// keeps each test and reads the slot with a cast to the arm the test admitted (<c>as</c> + <c>?.</c> for the two
    /// JSX opener classes, whose <c>attributes</c> slot is declared on both). The template span's <c>literal</c> is the
    /// union <c>TemplateMiddle | TemplateTail</c> in the reference, whose <c>isUnterminated</c> comes from
    /// <c>LiteralLikeNode</c>, and the port's <see cref="ILiteralLikeNode"/> is that same face (<c>TemplateTail</c> is
    /// not a <c>LiteralExpression</c>). <c>node.arguments.hasTrailingComma</c>
    /// is <c>NodeArray.HasTrailingComma</c>. <c>Math.min</c> is <see cref="Math.Min(int, int)"/>.
    /// </remarks>
    internal bool HasCorrectArity(Node node, IList<Expression> args, ISignature signature, bool signatureHelpTrailingComma = false)
    {
        if (NodeTests.IsJsxOpeningFragment(node)) return true;

        int argCount;
        var callIsIncomplete = false; // In incomplete call we want to be lenient when we have too few arguments
        var effectiveParameterCount = GetParameterCount(signature);
        var effectiveMinimumArguments = GetMinArgumentCount(signature);

        if (node.Kind == SyntaxKind.TaggedTemplateExpression)
        {
            var template = ((TaggedTemplateExpression)node).Template!;
            argCount = args.Count;
            if (template.Kind == SyntaxKind.TemplateExpression)
            {
                // If a tagged template expression lacks a tail literal, the call is incomplete.
                // Specifically, a template only can end in a TemplateTail or a Missing literal.
                var lastSpan = Core.Last(((TemplateExpression)template).TemplateSpans!); // we should always have at least one span.
                callIsIncomplete = Utilities.NodeIsMissing(lastSpan.Literal!) || ((ILiteralLikeNode)lastSpan.Literal!).IsUnterminated == true;
            }
            else
            {
                // If the template didn't end in a backtick, or its beginning occurred right prior to EOF,
                // then this might actually turn out to be a TemplateHead in the future;
                // so we consider the call to be incomplete.
                var templateLiteral = (LiteralExpression)template;
                Debug.Assert(templateLiteral.Kind == SyntaxKind.NoSubstitutionTemplateLiteral);
                callIsIncomplete = templateLiteral.IsUnterminated == true;
            }
        }
        else if (node.Kind == SyntaxKind.Decorator)
        {
            argCount = GetDecoratorArgumentCount((Decorator)node, signature);
        }
        else if (NodeTests.IsBinaryExpression(node))
        {
            argCount = 1;
        }
        else if (Utilities.IsJsxOpeningLikeElement(node))
        {
            var attributes = ((node as JsxOpeningElement)?.Attributes ?? (node as JsxSelfClosingElement)?.Attributes)!;
            callIsIncomplete = attributes.End == node.End;
            if (callIsIncomplete)
            {
                return true;
            }
            argCount = effectiveMinimumArguments == 0 ? args.Count : 1;
            effectiveParameterCount = args.Count == 0 ? effectiveParameterCount : 1; // class may have argumentless ctor functions - still resolve ctor and compare vs props member type
            effectiveMinimumArguments = Math.Min(effectiveMinimumArguments, 1); // sfc may specify context argument - handled by framework and not typechecked
        }
        else if (ArgumentsOfUnion(node) is null)
        {
            // This only happens when we have something of the form: 'new C'
            Debug.Assert(node.Kind == SyntaxKind.NewExpression);
            return GetMinArgumentCount(signature) == 0;
        }
        else
        {
            argCount = signatureHelpTrailingComma ? args.Count + 1 : args.Count;

            // If we are missing the close parenthesis, the call is incomplete.
            callIsIncomplete = ArgumentsOfUnion(node)!.End == node.End;

            // If a spread argument is present, check that it corresponds to a rest parameter or at least that it's in the valid range.
            var spreadArgIndex = GetSpreadArgumentIndex(args);
            if (spreadArgIndex >= 0)
            {
                return spreadArgIndex >= GetMinArgumentCount(signature) && (HasEffectiveRestParameter(signature) || spreadArgIndex < GetParameterCount(signature));
            }
        }

        // Too many arguments implies incorrect arity.
        if (!HasEffectiveRestParameter(signature) && argCount > effectiveParameterCount)
        {
            return false;
        }

        // If the call is incomplete, we should skip the lower bound check.
        // JSX signatures can have extra parameters provided by the library which we don't check
        if (callIsIncomplete || argCount >= effectiveMinimumArguments)
        {
            return true;
        }
        for (var i = argCount; i < effectiveMinimumArguments; i++)
        {
            var type = GetTypeAtPosition(signature, i);
            if ((FilterType(type, Utilities.IsInJSFile(node) && !_strictNullChecks ? AcceptsVoidUndefinedUnknownOrAny : AcceptsVoid).Flags & TypeFlags.Never) != 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>ported from: src/compiler/checker.ts:35749-35756 — <c>hasCorrectTypeArgumentArity</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>!some(typeArguments)</c> is TS truthiness of a possibly-empty <c>NodeArray</c>; the port tests the
    /// length, as landed files do for the same pattern.
    /// </remarks>
    internal static bool HasCorrectTypeArgumentArity(ISignature signature, NodeArray<ITypeNode>? typeArguments)
    {
        // If the user supplied type arguments, but the number of type arguments does not match
        // the declared number of type parameters, the call has an incorrect arity.
        var numTypeParameters = Core.Length(signature.TypeParameters);
        var minTypeArgumentCount = GetMinTypeArgumentCount(signature.TypeParameters);
        return typeArguments is null || typeArguments.Count == 0 ||
            (typeArguments.Count >= minTypeArgumentCount && typeArguments.Count <= numTypeParameters);
    }

    /// <summary>ported from: src/compiler/checker.ts:35758-35762 — <c>isInstantiatedGenericParameter</c>.</summary>
    internal bool IsInstantiatedGenericParameter(ISignature signature, int pos)
    {
        IType? type;
        return signature.Target is not null && (type = TryGetTypeAtPosition(signature.Target, pos)) is not null && IsGenericType(type);
    }

    // ---------------------------------------------------------------------------------------------
    // The single-signature and contextual-instantiation helpers (checker.ts:35764-35825)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:35764-35766 — <c>getSingleCallSignature</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>/*allowMembers*/</c> argument is <c>false</c>, which the port passes explicitly because C# has no comments-as-arguments.</remarks>
    internal ISignature? GetSingleCallSignature(IType type)
        => GetSingleSignature(type, SignatureKind.Call, /*allowMembers*/ false);

    /// <summary>ported from: src/compiler/checker.ts:35768-35771 — <c>getSingleCallOrConstructSignature</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>a || b</c> returns the second call's signature when the first is <c>undefined</c>; the port
    /// keeps that with <c>??</c> over the two nullable results.
    /// </remarks>
    internal ISignature? GetSingleCallOrConstructSignature(IType type)
        => GetSingleSignature(type, SignatureKind.Call, /*allowMembers*/ false)
            ?? GetSingleSignature(type, SignatureKind.Construct, /*allowMembers*/ false);

    /// <summary>ported from: src/compiler/checker.ts:35773-35787 — <c>getSingleSignature</c>.</summary>
    internal ISignature? GetSingleSignature(IType type, SignatureKind kind, bool allowMembers)
    {
        if ((type.Flags & TypeFlags.Object) != 0)
        {
            // PORT-NOTE: TS's structured-type parameter is the `ObjectType | UnionOrIntersectionType` union; the port's
            // `ObjectType` derives from `UnionOrIntersectionType`, which is the class the landed resolver takes (the
            // same cast every other caller writes), and the resolved faces keep their lists nullable, so the counts go
            // through Core.Length.
            var resolved = ResolveStructuredTypeMembers((UnionOrIntersectionType)type);
            if (allowMembers || Core.Length(resolved.Properties) == 0 && Core.Length(resolved.IndexInfos) == 0)
            {
                if (kind == SignatureKind.Call && Core.Length(resolved.CallSignatures) == 1 && Core.Length(resolved.ConstructSignatures) == 0)
                {
                    return resolved.CallSignatures![0];
                }

                if (kind == SignatureKind.Construct && Core.Length(resolved.ConstructSignatures) == 1 && Core.Length(resolved.CallSignatures) == 0)
                {
                    return resolved.ConstructSignatures![0];
                }
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:35789-35807 — <c>instantiateSignatureInContextOf</c>.</summary>
    /// <remarks>
    /// <para>
    /// Instantiate a generic signature in the context of a non-generic signature (section 3.8.5 in TypeScript spec).
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>getTypeParametersForMapper(signature)!</c> is the reference's non-null assertion on a query whose
    /// answer is guaranteed by the caller; the port states it as <c>!</c> per the port's nullability rule for a slot
    /// the reference dereferences directly. <c>mapper</c> is TS's <c>TypeMapper | undefined</c>, represented as
    /// <see cref="TypeMapper"/>?; <c>restType.flags &amp; TypeFlags.TypeParameter</c> is <c>!= 0</c>.
    /// <c>applyToParameterTypes</c>/<c>applyToReturnTypes</c> are checker members outside this band.
    /// </para>
    /// </remarks>
    internal ISignature InstantiateSignatureInContextOf(ISignature signature, ISignature contextualSignature, IInferenceContext? inferenceContext = null, TypeComparer? compareTypes = null)
    {
        var context = CreateInferenceContext(GetTypeParametersForMapper(signature)!, signature, InferenceFlags.None, compareTypes is null ? null : (source, target, reportErrors) => compareTypes(source, target, reportErrors));
        // We clone the inferenceContext to avoid fixing. For example, when the source signature is <T>(x: T) => T[] and
        // the contextual signature is (...args: A) => B, we want to infer the element type of A's constraint (say 'any')
        // for T but leave it possible to later infer '[any]' back to A.
        var restType = GetEffectiveRestType(contextualSignature);
        // PORT-NOTE: TS's `inferenceContext && (cond ? a : b)` is truthiness over the context and then over the
        // mapper slot; the port states it as the same two tests.
        object? mapper = inferenceContext is null
            ? null
            : restType is not null && (restType.Flags & TypeFlags.TypeParameter) != 0 ? inferenceContext.NonFixingMapper : inferenceContext.Mapper;
        var sourceSignature = mapper is not null ? InstantiateSignature(contextualSignature, (TypeMapper)mapper) : contextualSignature;
        ApplyToParameterTypes(sourceSignature, signature, (source, target) =>
        {
            // Type parameters from outer context referenced by source type are fixed by instantiation of the source type
            InferTypes(context.Inferences!, source, target);
        });
        if (inferenceContext is null)
        {
            ApplyToReturnTypes(contextualSignature, signature, (source, target) =>
            {
                InferTypes(context.Inferences!, source, target, InferencePriority.ReturnType);
            });
        }

        return GetSignatureInstantiation(signature, GetInferredTypes(context), Utilities.IsInJSFile(contextualSignature.Declaration!), inferenceContext?.InferredTypeParameters);
    }

    /// <summary>ported from: src/compiler/checker.ts:35809-35814 — <c>inferJsxTypeArguments</c>.</summary>
    internal List<IType> InferJsxTypeArguments(Node node, ISignature signature, CheckMode checkMode, IInferenceContext context)
    {
        var paramType = GetEffectiveFirstArgumentForJsxSignature(signature, node);
        var checkAttrType = CheckExpressionWithContextualType(((node as JsxOpeningElement)?.Attributes ?? (node as JsxSelfClosingElement)?.Attributes)!, paramType, context, checkMode);
        InferTypes(context.Inferences!, checkAttrType, paramType);
        return GetInferredTypes(context);
    }

    /// <summary>ported from: src/compiler/checker.ts:35816-35825 — <c>getThisArgumentType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS conditionally returns <c>thisArgumentType</c> whose type is narrowed by
    /// <c>isRightSideOfInstanceofExpression</c>; the port keeps the reference's chain of conditional expressions.
    /// <c>thisArgumentNode.parent</c> is examined only in the arms where TS's narrowing guarantees a parent, so the
    /// port keeps the reference's direct reads with <c>!</c>.
    /// </remarks>
    internal IType GetThisArgumentType(Expression? thisArgumentNode)
    {
        if (thisArgumentNode is null)
        {
            return _voidType;
        }

        var thisArgumentType = CheckExpression(thisArgumentNode);
        return Utilities.IsRightSideOfInstanceofExpression(thisArgumentNode) ? thisArgumentType :
            Utilities.IsOptionalChainRoot(thisArgumentNode.Parent!) ? GetNonNullableType(thisArgumentType) :
            Utilities.IsOptionalChain(thisArgumentNode.Parent!) ? RemoveOptionalTypeMarker(thisArgumentType) :
            thisArgumentType;
    }

    /// <summary>ported from: src/compiler/checker.ts:35827-35917 — <c>inferTypeArguments</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: <c>every(signature.typeParameters, p =&gt; !!getDefaultFromTypeParameter(p))</c> is
    /// <see cref="Core.Every{T}"/> over a nullable list; <c>getContextualType(node, flags)</c> is
    /// <c>GetContextualType(node, ContextFlags)</c>. The two nested callbacks that the reference writes as arrow
    /// functions keep that shape here. <c>getSpreadArgumentType(args, argCount, args.length, restType, context,
    /// checkMode)</c> keeps the reference's argument order.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>info.impliedArity = findIndex(args, isSpreadArgument, argCount) &lt; 0 ? args.length - argCount :
    /// undefined</c> assigns TS's <c>number | undefined</c>; <see cref="IInferenceInfo.ImpliedArity"/> is <c>int?</c>.
    /// </para>
    /// </remarks>
    internal List<IType> InferTypeArguments(Node node, ISignature signature, IList<Expression> args, CheckMode checkMode, IInferenceContext context)
    {
        using var allocProbe = AllocationProbe.Enter("Checker.InferTypeArguments");
        if (Utilities.IsJsxOpeningLikeElement(node))
        {
            return InferJsxTypeArguments(node, signature, checkMode, context);
        }

        // If a contextual type is available, infer from that type to the return type of the call expression. For
        // example, given a 'function wrap<T, U>(cb: (x: T) => U): (x: T) => U' and a call expression
        // 'let f: (x: string) => number = wrap(s => s.length)', we infer from the declared type of 'f' to the
        // return type of 'wrap'.
        if (node.Kind != SyntaxKind.Decorator && node.Kind != SyntaxKind.BinaryExpression)
        {
            var skipBindingPatterns = Core.Every(signature.TypeParameters, (ITypeParameter p, int _) => GetDefaultFromTypeParameter(p) is not null);
            var contextualType = GetContextualType((Expression)node, skipBindingPatterns ? ContextFlags.SkipBindingPatterns : ContextFlags.None);
            if (contextualType is not null)
            {
                var inferenceTargetType = GetReturnTypeOfSignature(signature);
                if (CouldContainTypeVariables(inferenceTargetType))
                {
                    var outerContext = GetInferenceContext(node);
                    var isFromBindingPattern = !skipBindingPatterns && GetContextualType((Expression)node, ContextFlags.SkipBindingPatterns) != contextualType;
                    // A return type inference from a binding pattern can be used in instantiating the contextual
                    // type of an argument later in inference, but cannot stand on its own as the final return type.
                    if (!isFromBindingPattern)
                    {
                        // We clone the inference context to avoid disturbing a resolution in progress for an
                        // outer call expression. Effectively we just want a snapshot of whatever has been
                        // inferred for any outer call expression so far.
                        var outerMapper = GetMapperFromContext(CloneInferenceContext(outerContext!, InferenceFlags.NoDefault));
                        var instantiatedType = InstantiateType(contextualType, outerMapper);
                        // If the contextual type is a generic function type with a single call signature, we
                        // instantiate the type with its own type parameters and type arguments.
                        var contextualSignature = GetSingleCallSignature(instantiatedType!);
                        var inferenceSourceType = contextualSignature is not null && contextualSignature.TypeParameters is not null
                            // PORT-NOTE: TS passes `contextualSignature.typeParameters` (a TypeParameter[], covariant to
                            // the helper's `readonly Type[]`); C# lists are invariant, so the port copies the same
                            // elements into the List<IType> the landed helper takes.
                            ? GetOrCreateTypeFromSignature(GetSignatureInstantiationWithoutFillingInTypeArguments(contextualSignature, [.. contextualSignature.TypeParameters.Cast<IType>()]))
                            : instantiatedType;
                        // Inferences made from return types have lower priority than all other inferences.
                        InferTypes(context.Inferences!, inferenceSourceType!, inferenceTargetType, InferencePriority.ReturnType);
                    }

                    // Create a type mapper for instantiating generic contextual types using the inferences made
                    // from the return type. We need a separate inference pass here because (a) instantiation of
                    // the source type uses the outer context's return mapper (which excludes inferences made from
                    // outer arguments), and (b) we don't want any further inferences going into this context.
                    // We use `createOuterReturnMapper` to ensure that all occurrences of outer type parameters are
                    // replaced with inferences produced from the outer return type or preceding outer arguments.
                    // This protects against circular inferences, i.e. avoiding situations where inferences reference
                    // type parameters for which the inferences are being made.
                    var returnContext = CreateInferenceContext(signature.TypeParameters!, signature, context.Flags);
                    var returnSourceType = InstantiateType(contextualType, outerContext is not null ? CreateOuterReturnMapper(outerContext) : null);
                    InferTypes(returnContext.Inferences!, returnSourceType!, inferenceTargetType);
                    context.ReturnMapper = Core.Some(returnContext.Inferences!, info => HasInferenceCandidates(info)) ? GetMapperFromContext(CloneInferredPartOfContext(returnContext)) : null;
                }
            }
        }

        var restType = GetNonArrayRestType(signature);
        var argCount = restType is not null ? Math.Min(GetParameterCount(signature) - 1, args.Count) : args.Count;
        if (restType is not null && (restType.Flags & TypeFlags.TypeParameter) != 0)
        {
            var info = Core.Find(context.Inferences!, (IInferenceInfo info, int _) => ReferenceEquals(info.TypeParameter, restType));
            if (info is not null)
            {
                info.ImpliedArity = Core.FindIndex(args, (Expression arg, int _) => IsSpreadArgument(arg), argCount) < 0 ? args.Count - argCount : null;
            }
        }

        var thisType = GetThisTypeOfSignature(signature);
        if (thisType is not null && CouldContainTypeVariables(thisType))
        {
            var thisArgumentNode = GetThisArgumentOfCall(node);
            InferTypes(context.Inferences!, GetThisArgumentType(thisArgumentNode), thisType);
        }

        for (var i = 0; i < argCount; i++)
        {
            var arg = args[i];
            if (arg.Kind != SyntaxKind.OmittedExpression)
            {
                var paramType = GetTypeAtPosition(signature, i);
                if (CouldContainTypeVariables(paramType))
                {
                    var argType = CheckExpressionWithContextualType(arg, paramType, context, checkMode);
                    InferTypes(context.Inferences!, argType, paramType);
                }
            }
        }

        if (restType is not null && CouldContainTypeVariables(restType))
        {
            var spreadType = GetSpreadArgumentType(args, argCount, args.Count, restType, context, checkMode);
            InferTypes(context.Inferences!, spreadType, restType);
        }

        return GetInferredTypes(context);
    }

    /// <summary>ported from: src/compiler/checker.ts:35924-35929 — <c>getMutableArrayOrTupleType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's nested ternary keeps the exact type of the <c>TypeFlags.Any</c>/<c>isMutableArrayOrTuple</c> arm
    /// (<c>type</c>); the port's <see cref="Core.MapType"/> overload for a plain mapper keeps it too.
    /// <c>createTupleType([type], [ElementFlags.Variadic])</c> builds the single-element variadic tuple with a list
    /// expression.
    /// </remarks>
    internal IType GetMutableArrayOrTupleType(IType type)
        => (type.Flags & TypeFlags.Union) != 0 ? MapType(type, GetMutableArrayOrTupleType)! :
            (type.Flags & TypeFlags.Any) != 0 || IsMutableArrayOrTuple(GetBaseConstraintOfType(type) ?? type) ? type :
            IsTupleType(type) ? CreateTupleType(GetElementTypes((ITupleTypeReference)type), ((ITupleType)((ITupleTypeReference)type).Target!).ElementFlags, /*readonly*/ false, ((ITupleType)((ITupleTypeReference)type).Target!).LabeledElementDeclarations) :
            CreateTupleType([type], [ElementFlags.Variadic]);

    /// <summary>ported from: src/compiler/checker.ts:35931-35982 — <c>getSpreadArgumentType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>arg.kind === SyntaxKind.SyntheticExpression ? (arg as SyntheticExpression).type
    /// : …</c> reads the synthetic argument's type slot; the port keeps the same ternary with the cast.
    /// <c>types</c>/<c>flags</c>/<c>names</c> are untyped arrays in the reference and become
    /// <c>List&lt;IType&gt;</c>/<c>List&lt;ElementFlags&gt;</c>/<c>List&lt;Node?&gt;</c>, matching
    /// <see cref="Checker.CreateTupleType"/>. <c>someType(restType, isMutableArrayLikeType)</c> is the M6-4
    /// <c>SomeType</c> helper.
    /// </remarks>
    internal IType GetSpreadArgumentType(IList<Expression> args, int index, int argCount, IType restType, IInferenceContext? context, CheckMode checkMode)
    {
        var inConstContext = IsConstTypeVariable(restType);

        if (index >= argCount - 1)
        {
            // PORT-NOTE: the reference's `args[argCount - 1]` is the falsy `undefined` when that slot does not exist —
            // an empty argument list (`args[-1]`) or an `argCount` past the end — and `isSpreadArgument` rejects it, so
            // the tail analysis below is skipped. The port's List indexer throws instead, so the missing element is
            // read as null, the same "there is no argument here" answer.
            Expression? arg = argCount >= 1 && argCount <= args.Count ? args[argCount - 1] : null;
            if (arg is not null && IsSpreadArgument(arg))
            {
                // We are inferring from a spread expression in the last argument position, i.e. both the parameter
                // and the argument are ...x forms.
                var spreadType = arg.Kind == SyntaxKind.SyntheticExpression
                    ? ((SyntheticExpression)arg).Type!
                    : CheckExpressionWithContextualType(((SpreadElement)arg).Expression!, restType, context, checkMode);

                if (IsArrayLikeType(spreadType))
                {
                    return GetMutableArrayOrTupleType(spreadType);
                }

                return CreateArrayType(CheckIteratedTypeOrElementType(IterationUse.Spread, spreadType, _undefinedType, arg.Kind == SyntaxKind.SpreadElement ? ((SpreadElement)arg).Expression! : arg), inConstContext);
            }
        }

        var types = new List<IType>();
        var flags = new List<ElementFlags>();
        var names = new List<Node>();
        for (var i = index; i < argCount; i++)
        {
            var arg = args[i];
            if (IsSpreadArgument(arg))
            {
                var spreadType = arg.Kind == SyntaxKind.SyntheticExpression ? ((SyntheticExpression)arg).Type! : CheckExpression(((SpreadElement)arg).Expression!);
                if (IsArrayLikeType(spreadType))
                {
                    types.Add(spreadType);
                    flags.Add(ElementFlags.Variadic);
                }
                else
                {
                    types.Add(CheckIteratedTypeOrElementType(IterationUse.Spread, spreadType, _undefinedType, arg.Kind == SyntaxKind.SpreadElement ? ((SpreadElement)arg).Expression! : arg));
                    flags.Add(ElementFlags.Rest);
                }
            }
            else
            {
                var contextualType = IsTupleType(restType) ?
                    GetContextualTypeForElementExpression(restType, i - index, argCount - index) ?? _unknownType :
                    GetIndexedAccessType(restType, GetNumberLiteralType(i - index), AccessFlags.Contextual);
                var argType = CheckExpressionWithContextualType(arg, contextualType, context, checkMode);
                var hasPrimitiveContextualType = inConstContext || MaybeTypeOfKind(contextualType, TypeFlags.Primitive | TypeFlags.Index | TypeFlags.TemplateLiteral | TypeFlags.StringMapping);
                types.Add(hasPrimitiveContextualType ? GetRegularTypeOfLiteralType(argType) : GetWidenedLiteralType(argType));
                flags.Add(ElementFlags.Required);
            }

            if (arg.Kind == SyntaxKind.SyntheticExpression && ((SyntheticExpression)arg).TupleNameSource is not null)
            {
                names.Add(((SyntheticExpression)arg).TupleNameSource!);
            }
            else
            {
                // PORT-NOTE: the reference pushes `undefined` as a positional hole in the named-member array
                // (`CreateTupleType` reads it by index); the port's slot is a non-nullable List<Node>, so the hole is
                // written as a null entry rather than dropped from the list.
                names.Add(null!);
            }
        }

        return CreateTupleType(types, flags, inConstContext && !SomeType(restType, IsMutableArrayLikeType), names);
    }

    /// <summary>ported from: src/compiler/checker.ts:35984-36013 — <c>checkTypeArguments</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: <c>fillMissingTypeArguments(map(typeArgumentNodes, getTypeFromTypeNode), …)</c> maps a
    /// <c>readonly TypeNode[]</c> through <c>getTypeFromTypeNode</c>; <see cref="Core.Map{T,TU}"/> is the port's
    /// helper, and the elements are <see cref="ITypeNode"/>, which <see cref="Checker.GetTypeFromTypeNode"/> accepts.
    /// </para>
    /// <para>
    /// PORT-NOTE: the reference's lazily built <c>mapper</c> and the closure <c>errorInfo</c> keep their shape; the
    /// closure's <c>details</c> is <c>undefined</c>, stated as the <c>null</c> the port's
    /// <c>ChainDiagnosticMessages</c> takes.
    /// </para>
    /// </remarks>
    internal List<IType>? CheckTypeArguments(ISignature signature, IList<ITypeNode> typeArgumentNodes, bool reportErrors, DiagnosticMessage? headMessage = null)
    {
        var isJavascript = Utilities.IsInJSFile(signature.Declaration!);
        var typeParameters = signature.TypeParameters!;
        var typeArgumentTypes = FillMissingTypeArguments(Core.Map(typeArgumentNodes, (ITypeNode node, int _) => GetTypeFromTypeNode(node))!, typeParameters, GetMinTypeArgumentCount(typeParameters), isJavascript);
        TypeMapper? mapper = null;
        for (var i = 0; i < typeArgumentNodes.Count; i++)
        {
            Debug.Assert(typeParameters[i] is not null, "Should not call checkTypeArguments with too many type arguments");
            var constraint = GetConstraintOfTypeParameter(typeParameters[i]);
            if (constraint is not null)
            {
                Func<IDiagnosticMessageChain?>? errorInfo = reportErrors && headMessage is not null
                    ? () => Utilities.ChainDiagnosticMessages(/*details*/ null, Diagnostics.Type_0_does_not_satisfy_the_constraint_1)
                    : null;
                var typeArgumentHeadMessage = headMessage ?? Diagnostics.Type_0_does_not_satisfy_the_constraint_1;
                mapper ??= CreateTypeMapper(typeParameters, typeArgumentTypes!);
                var typeArgument = typeArgumentTypes![i];
                if (
                    !CheckTypeAssignableTo(
                        typeArgument,
                        GetTypeWithThisArgument(InstantiateType(constraint, mapper)!, typeArgument),
                        reportErrors ? (Node)typeArgumentNodes[i] : null,
                        typeArgumentHeadMessage,
                        errorInfo)
                )
                {
                    return null;
                }
            }
        }

        return typeArgumentTypes;
    }

    /// <summary>ported from: src/compiler/checker.ts:36015-36027 — <c>getJsxReferenceKind</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>node.tagName</c> is read off the union after <c>isJsxIntrinsicTagName</c> (M6-5's helper) decides;
    /// the slot is declared on both JSX opener classes, so the port reads it with the <c>as</c>+<c>?.</c> form used
    /// throughout this file.
    /// </remarks>
    internal JsxReferenceKind GetJsxReferenceKind(Node node)
    {
        var tagName = (Node?)((node as JsxOpeningElement)?.TagName ?? (node as JsxSelfClosingElement)?.TagName) ?? node;
        if (IsJsxIntrinsicTagName(tagName))
        {
            return JsxReferenceKind.Mixed;
        }

        var tagType = GetApparentType(CheckExpression((Expression)tagName));
        if (Core.Length(GetSignaturesOfType(tagType, SignatureKind.Construct)) != 0)
        {
            return JsxReferenceKind.Component;
        }

        if (Core.Length(GetSignaturesOfType(tagType, SignatureKind.Call)) != 0)
        {
            return JsxReferenceKind.Function;
        }

        return JsxReferenceKind.Mixed;
    }

    /// <summary>ported from: src/compiler/checker.ts:36035-36141 — <c>checkApplicableSignatureForJsxCallLikeElement</c>.</summary>
    /// <remarks>
    /// <para>
    /// Check if the given signature can possibly be a signature called by the JSX opening-like element.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>relation</c> is TS's <c>Map&lt;string, RelationComparisonResult&gt;</c>, the port's
    /// <c>Dictionary&lt;string, RelationComparisonResult&gt;</c> (<c>Checker.Relations.Entries.cs</c>'s
    /// <c>_assignableRelation</c>). <c>containingMessageChain</c> is a callback returning
    /// <c>DiagnosticMessageChain | undefined</c>; the port types it <c>Func&lt;IDiagnosticMessageChain?&gt;?</c>.
    /// <c>errorOutputContainer.errors || (errorOutputContainer.errors = [])</c> keeps the reference's lazy list with
    /// <c>??=</c>. <c>checkMode &amp; CheckMode.SkipContextSensitive</c> is a flags test, stated as
    /// <c>(checkMode &amp; …) != 0</c>. <c>getJsxNamespaceContainerForImplicitImport</c>, <c>getJsxFactoryEntity</c>,
    /// <c>createJsxAttributesTypeFromAttributesProperty</c> and <c>entityNameToString</c> are checker members outside
    /// this band (M6-5/utilities).
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>absoluteMinArgCount</c>/<c>maxParamCount</c> compare against <c>Infinity</c>; the port uses
    /// <c>double.PositiveInfinity</c> and the widest integer type the comparisons need (<c>double</c>), because the
    /// reference mixes <c>Infinity</c> with parameter counts.
    /// </para>
    /// </remarks>
    internal bool CheckApplicableSignatureForJsxCallLikeElement(
        Node node,
        ISignature signature,
        Dictionary<RelationKey, RelationComparisonResult> relation,
        CheckMode checkMode,
        bool reportErrors,
        Func<IDiagnosticMessageChain?>? containingMessageChain,
        IErrorOutputContainer errorOutputContainer)
    {
        // Stateless function components can have maximum of three arguments: "props", "context", and "updater".
        // However "context" and "updater" are implicit and can't be specify by users. Only the first parameter, props,
        // can be specified by users through attributes property.
        var paramType = GetEffectiveFirstArgumentForJsxSignature(signature, node);
        var attributesType = NodeTests.IsJsxOpeningFragment(node) ? CreateJsxAttributesTypeFromAttributesProperty(node) : CheckExpressionWithContextualType(((node as JsxOpeningElement)?.Attributes ?? (node as JsxSelfClosingElement)?.Attributes)!, paramType, /*inferenceContext*/ null, checkMode);
        var checkAttributesType = (checkMode & CheckMode.SkipContextSensitive) != 0 ? GetRegularTypeOfObjectLiteral(attributesType) : attributesType;
        var jsxTagName = (Node?)((node as JsxOpeningElement)?.TagName ?? (node as JsxSelfClosingElement)?.TagName);
        var jsxAttributes = (Expression?)((node as JsxOpeningElement)?.Attributes ?? (node as JsxSelfClosingElement)?.Attributes);
        return CheckTagNameDoesNotExpectTooManyArguments() && CheckTypeRelatedToAndOptionallyElaborate(
            checkAttributesType,
            paramType,
            relation,
            reportErrors ? NodeTests.IsJsxOpeningFragment(node) ? node : jsxTagName : null,
            NodeTests.IsJsxOpeningFragment(node) ? null : jsxAttributes,
            /*headMessage*/ null,
            containingMessageChain,
            errorOutputContainer);

        // PORT-NOTE: the reference declares this as a nested function and captures `node`, `reportErrors` and
        // `errorOutputContainer`; the port states it as the same kind of nested function (a C# local function), so the
        // captured names and the `return false` path keep the reference's shape.
        bool CheckTagNameDoesNotExpectTooManyArguments()
        {
            if (GetJsxNamespaceContainerForImplicitImport(node) is not null)
            {
                return true; // factory is implicitly jsx/jsxdev - assume it fits the bill, since we don't strongly look for the jsx/jsxs/jsxDEV factory APIs anywhere else (at least not yet)
            }

            // We assume fragments have the correct arity since the node does not have attributes
            var tagType = (NodeTests.IsJsxOpeningElement(node) || NodeTests.IsJsxSelfClosingElement(node)) && !(IsJsxIntrinsicTagName(jsxTagName!) || NodeTests.IsJsxNamespacedName(jsxTagName!)) ? CheckExpression((Expression)jsxTagName!) : null;
            if (tagType is null)
            {
                return true;
            }
            var tagCallSignatures = GetSignaturesOfType(tagType, SignatureKind.Call);
            if (Core.Length(tagCallSignatures) == 0)
            {
                return true;
            }
            var factoryEntity = GetJsxFactoryEntity(node);
            if (factoryEntity is null)
            {
                return true;
            }
            var factorySymbol = ResolveEntityName((Node)factoryEntity, SymbolFlags.Value, /*ignoreErrors*/ true, /*dontResolveAlias*/ false, node);
            if (factorySymbol is null)
            {
                return true;
            }

            var factoryType = GetTypeOfSymbol(factorySymbol);
            var callSignatures = GetSignaturesOfType(factoryType, SignatureKind.Call);
            if (Core.Length(callSignatures) == 0)
            {
                return true;
            }

            var hasFirstParamSignatures = false;
            var maxParamCount = 0;
            // Check that _some_ first parameter expects a FC-like thing, and that some overload of the SFC expects an acceptable number of arguments
            foreach (var sig in callSignatures)
            {
                var firstparam = GetTypeAtPosition(sig, 0);
                var signaturesOfParam = GetSignaturesOfType(firstparam, SignatureKind.Call);
                if (Core.Length(signaturesOfParam) == 0) continue;
                foreach (var paramSig in signaturesOfParam)
                {
                    hasFirstParamSignatures = true;
                    if (HasEffectiveRestParameter(paramSig))
                    {
                        return true; // some signature has a rest param, so function components can have an arbitrary number of arguments
                    }
                    var paramCount = GetParameterCount(paramSig);
                    if (paramCount > maxParamCount)
                    {
                        maxParamCount = paramCount;
                    }
                }
            }
            if (!hasFirstParamSignatures)
            {
                // Not a single signature had a first parameter which expected a signature - for back compat, and
                // to guard against generic factories which won't have signatures directly, do not error
                return true;
            }
            var absoluteMinArgCount = double.PositiveInfinity;
            foreach (var tagSig in tagCallSignatures)
            {
                int tagRequiredArgCount = GetMinArgumentCount(tagSig);
                if (tagRequiredArgCount < absoluteMinArgCount)
                {
                    absoluteMinArgCount = tagRequiredArgCount;
                }
            }
            if (absoluteMinArgCount <= maxParamCount)
            {
                return true; // some signature accepts the number of arguments the function component provides
            }

            if (reportErrors)
            {
                // We will not report errors in this function for fragments, since we do not check them in this function
                var tagName = jsxTagName!;
                var diag = Utilities.CreateDiagnosticForNode(tagName, Diagnostics.Tag_0_expects_at_least_1_arguments_but_the_JSX_factory_2_provides_at_most_3, Utilities.EntityNameToString(tagName), absoluteMinArgCount, Utilities.EntityNameToString(factoryEntity), maxParamCount);
                var tagNameDeclaration = GetSymbolAtLocation(tagName)?.ValueDeclaration;
                if (tagNameDeclaration is not null)
                {
                    Utilities.AddRelatedInfo(diag, Utilities.CreateDiagnosticForNode((Node)tagNameDeclaration, Diagnostics._0_is_declared_here, Utilities.EntityNameToString(tagName)));
                }
                if (errorOutputContainer.SkipLogging == true)
                {
                    (errorOutputContainer.Errors ??= []).Add(diag);
                }
                if (errorOutputContainer.SkipLogging != true)
                {
                    _diagnostics.Add(diag);
                }
            }
            return false;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:36143-36148 — <c>getEffectiveCheckNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>skipOuterExpressions</c> (factory/utilities.ts:654-661) has an
    /// <c>Expression</c>-preserving overload; the port's <c>FactoryUtilities.SkipOuterExpressions</c> is the
    /// <see cref="Node"/>-returning one, so the result is cast back to the <see cref="Expression"/> the reference's
    /// return type promises (the skipped forms of an expression are expressions).
    /// </remarks>
    internal static Expression GetEffectiveCheckNode(Expression argument)
    {
        var flags = Utilities.IsInJSFile(argument)
            ? OuterExpressionKinds.Parentheses | OuterExpressionKinds.Satisfies | OuterExpressionKinds.ExcludeJSDocTypeAssertion
            : OuterExpressionKinds.Parentheses | OuterExpressionKinds.Satisfies;
        return (Expression)FactoryUtilities.SkipOuterExpressions(argument, flags);
    }

    /// <summary>ported from: src/compiler/checker.ts:36150-36228 — <c>getSignatureApplicabilityError</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: TS's return type is <c>readonly Diagnostic[] | undefined</c> and every element it puts in
    /// <c>errorOutputContainer.errors</c> is a <see cref="Diagnostic"/>; the port's container slot is the generated
    /// <c>List&lt;IDiagnostic&gt;?</c> face, so <see cref="CollectedErrors"/> unwraps it back to the concrete type the
    /// reference's array holds (the same "the slot holds what we put there" cast
    /// <c>Checker.Relations.Entries.cs</c> uses for the same slot). <c>errorOutputContainer.errors || emptyArray</c>
    /// keeps the reference's shared empty array as <c>Core.EmptyArray&lt;Diagnostic&gt;.Value</c>.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>isJsxCallLike</c>, <c>getAwaitedTypeOfPromise</c>, <c>checkTypeRelatedTo</c> and
    /// <c>checkTypeRelatedToAndOptionallyElaborate</c> are checker members outside this band (utilitiesPublic.ts and
    /// the relations slice). <c>maybeAddMissingAwaitInfo</c> is the reference's nested function, kept as a local
    /// function; <c>errorNode</c> stays <c>Node?</c> because the reference passes <c>undefined</c> when
    /// <c>reportErrors</c> is false.
    /// </para>
    /// </remarks>
    internal List<Diagnostic>? GetSignatureApplicabilityError(
        Node node,
        IList<Expression> args,
        ISignature signature,
        Dictionary<RelationKey, RelationComparisonResult> relation,
        CheckMode checkMode,
        bool reportErrors,
        Func<IDiagnosticMessageChain?>? containingMessageChain)
    {
        IErrorOutputContainer errorOutputContainer = new ErrorOutputContainer { Errors = null, SkipLogging = true };
        if (Utilities.IsJsxCallLike(node))
        {
            if (!CheckApplicableSignatureForJsxCallLikeElement(node, signature, relation, checkMode, reportErrors, containingMessageChain, errorOutputContainer))
            {
                Debug.Assert(!reportErrors || errorOutputContainer.Errors is not null, "jsx should have errors when reporting errors");
                return CollectedErrors();
            }
            return null;
        }
        var thisType = GetThisTypeOfSignature(signature);
        if (thisType is not null && !ReferenceEquals(thisType, _voidType) && !(NodeTests.IsNewExpression(node) || NodeTests.IsCallExpression(node) && Utilities.IsSuperProperty(((CallExpression)node).Expression!)))
        {
            // If the called expression is not of the form `x.f` or `x["f"]`, then sourceType = voidType
            // If the signature's 'this' type is voidType, then the check is skipped -- anything is compatible.
            // If the expression is a new expression or super call expression, then the check is skipped.
            var thisArgumentNode = GetThisArgumentOfCall(node);
            var thisArgumentType = GetThisArgumentType(thisArgumentNode);
            var errorNode = reportErrors ? (Node?)(thisArgumentNode ?? node) : null;
            var headMessage = Diagnostics.The_this_context_of_type_0_is_not_assignable_to_method_s_this_of_type_1;
            if (!CheckTypeRelatedTo(thisArgumentType, thisType!, relation, errorNode, headMessage, containingMessageChain, errorOutputContainer))
            {
                Debug.Assert(!reportErrors || errorOutputContainer.Errors is not null, "this parameter should have errors when reporting errors");
                return CollectedErrors();
            }
        }
        var argumentHeadMessage = Diagnostics.Argument_of_type_0_is_not_assignable_to_parameter_of_type_1;
        var restType = GetNonArrayRestType(signature);
        var argCount = restType is not null ? Math.Min(GetParameterCount(signature) - 1, args.Count) : args.Count;
        for (var i = 0; i < argCount; i++)
        {
            var arg = args[i];
            if (arg.Kind != SyntaxKind.OmittedExpression)
            {
                var paramType = GetTypeAtPosition(signature, i);
                var argType = CheckExpressionWithContextualType(arg, paramType, /*inferenceContext*/ null, checkMode);
                // If one or more arguments are still excluded (as indicated by CheckMode.SkipContextSensitive),
                // we obtain the regular type of any object literal arguments because we may not have inferred complete
                // parameter types yet and therefore excess property checks may yield false positives (see #17041).
                var checkArgType = (checkMode & CheckMode.SkipContextSensitive) != 0 ? GetRegularTypeOfObjectLiteral(argType) : argType;
                var effectiveCheckArgumentNode = GetEffectiveCheckNode(arg);
                if (!CheckTypeRelatedToAndOptionallyElaborate(checkArgType, paramType, relation, reportErrors ? effectiveCheckArgumentNode : null, effectiveCheckArgumentNode, argumentHeadMessage, containingMessageChain, errorOutputContainer))
                {
                    Debug.Assert(!reportErrors || errorOutputContainer.Errors is not null, "parameter should have errors when reporting errors");
                    MaybeAddMissingAwaitInfo(arg, checkArgType, paramType);
                    return CollectedErrors();
                }
            }
        }
        if (restType is not null)
        {
            var spreadType = GetSpreadArgumentType(args, argCount, args.Count, restType, /*context*/ null, checkMode);
            var restArgCount = args.Count - argCount;
            var errorNode = !reportErrors ? null :
                restArgCount == 0 ? node :
                restArgCount == 1 ? (Node?)GetEffectiveCheckNode(args[argCount]) :
                Utilities.SetTextRangePosEnd(CreateSyntheticExpression(node, spreadType), args[argCount].Pos, args[^1].End);
            if (!CheckTypeRelatedTo(spreadType, restType, relation, errorNode, argumentHeadMessage, /*containingMessageChain*/ null, errorOutputContainer))
            {
                Debug.Assert(!reportErrors || errorOutputContainer.Errors is not null, "rest parameter should have errors when reporting errors");
                MaybeAddMissingAwaitInfo(errorNode, spreadType, restType);
                return CollectedErrors();
            }
        }
        return null;

        // PORT-NOTE: port-only unwrapping of the container slot (see the member remarks); the reference returns the
        // container's own array here, so the elements are handed on as the concrete Diagnostic the callers require
        // (`DiagnosticCollection.Add` takes the class).
        List<Diagnostic> CollectedErrors()
            => errorOutputContainer.Errors is null
                ? Core.EmptyArray<Diagnostic>.Value
                : [.. errorOutputContainer.Errors.Cast<Diagnostic>()];

        void MaybeAddMissingAwaitInfo(Node? errorNode, IType source, IType target)
        {
            if (errorNode is not null && reportErrors && errorOutputContainer.Errors is not null && errorOutputContainer.Errors.Count > 0)
            {
                // Bail if target is Promise-like---something else is wrong
                if (GetAwaitedTypeOfPromise(target) is not null)
                {
                    return;
                }
                var awaitedTypeOfSource = GetAwaitedTypeOfPromise(source);
                if (awaitedTypeOfSource is not null && IsTypeRelatedTo(awaitedTypeOfSource, target, relation))
                {
                    Utilities.AddRelatedInfo((Diagnostic)errorOutputContainer.Errors[0], Utilities.CreateDiagnosticForNode(errorNode, Diagnostics.Did_you_forget_to_use_await));
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:36233-36248 — <c>getThisArgumentOfCall</c>.</summary>
    /// <remarks>
    /// Returns the this argument in calls like x.f(...) and x[f](...). Undefined otherwise.
    /// PORT-NOTE: the reference reads <c>node.right</c>/<c>node.expression</c>/<c>node.tag</c> off the union after its
    /// kind tests; the port reads the same slots through the arm's own class. <c>legacyDecorators</c> is the
    /// <see cref="_legacyDecorators"/> field.
    /// </remarks>
    internal Expression? GetThisArgumentOfCall(Node node)
    {
        if (node.Kind == SyntaxKind.BinaryExpression)
        {
            return ((BinaryExpression)node).Right;
        }

        var expression = node.Kind == SyntaxKind.CallExpression ? ((CallExpression)node).Expression :
            node.Kind == SyntaxKind.TaggedTemplateExpression ? ((TaggedTemplateExpression)node).Tag :
            node.Kind == SyntaxKind.Decorator && !_legacyDecorators ? ((Decorator)node).Expression :
            null;
        if (expression is not null)
        {
            var callee = FactoryUtilities.SkipOuterExpressions(expression);
            if (Utilities.IsAccessExpression(callee))
            {
                // PORT-NOTE: isAccessExpression narrows to PropertyAccessExpression | ElementAccessExpression in TS;
                // both port classes declare `Expression`, so the port reads it off the arm the kind selects.
                return callee is PropertyAccessExpression propertyAccess ? propertyAccess.Expression : ((ElementAccessExpression)callee).Expression;
            }
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:36250-36255 — <c>createSyntheticExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>parseNodeFactory</c> (checker.ts:36251, parser.ts:441) is the checker's parenthesizer-free
    /// factory, which the port declares as <c>FactoryUtilities.ParseNodeFactory</c>;
    /// <c>setTextRangeWorker</c> is the checker's alias of <c>setTextRange</c> (checker.ts:1010), i.e.
    /// <c>Utilities.SetTextRange</c>. <c>tupleNameSource</c> is TS's
    /// <c>ParameterDeclaration | NamedTupleMember</c>; both are <see cref="Node"/>s and the slot is written as
    /// <c>Node?</c>.
    /// </remarks>
    internal SyntheticExpression CreateSyntheticExpression(Node parent, IType type, bool? isSpread = null, Node? tupleNameSource = null)
    {
        var result = FactoryUtilities.ParseNodeFactory.CreateSyntheticExpression(type, isSpread, tupleNameSource);
        Utilities.SetTextRange(result, parent);
        Utilities.SetParent(result, parent);
        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:36260-36308 — <c>getEffectiveCallArguments</c>.</summary>
    /// <remarks>
    /// <para>
    /// Returns the effective arguments for an expression that works like a function invocation.
    /// </para>
    /// <para>
    /// PORT-NOTE: the reference returns a JS array it builds with <c>[...]</c>, <c>args.slice(...)</c> and
    /// <c>emptyArray</c>; the port returns <c>List&lt;Expression&gt;</c>, builds the literal cases with collection
    /// expressions (a fresh list each time, as in the reference) and hands back
    /// <c>Core.EmptyArray&lt;Expression&gt;.Value</c> for the reference's shared <c>emptyArray</c>. The
    /// <c>flowLoopCount ? checkExpression : checkExpressionCached</c> test reads the flow-loop counters, the state
    /// slots the reference declares at checker.ts:1539-1540 (<c>_flowLoopStart</c>/<c>_flowLoopCount</c> in the port).
    /// </para>
    /// </remarks>
    internal List<Expression> GetEffectiveCallArguments(Node node)
    {
        if (NodeTests.IsJsxOpeningFragment(node))
        {
            // This attributes Type does not include a children property yet, the same way a fragment created with <React.Fragment> does not at this stage
            return [CreateSyntheticExpression(node, _emptyFreshJsxObjectType)];
        }

        if (node.Kind == SyntaxKind.TaggedTemplateExpression)
        {
            var template = ((TaggedTemplateExpression)node).Template!;
            var args = new List<Expression> { CreateSyntheticExpression(template, GetGlobalTemplateStringsArrayType()) };
            if (template.Kind == SyntaxKind.TemplateExpression)
            {
                Core.ForEach(((TemplateExpression)template).TemplateSpans, (TemplateSpan span, int _) =>
                {
                    args.Add(span.Expression!);
                    return (object?)null;
                });
            }
            return args;
        }
        if (node.Kind == SyntaxKind.Decorator)
        {
            return GetEffectiveDecoratorArguments((Decorator)node);
        }
        if (NodeTests.IsBinaryExpression(node))
        {
            return [((BinaryExpression)node).Left!];
        }
        if (Utilities.IsJsxOpeningLikeElement(node))
        {
            var jsxAttributes = ((node as JsxOpeningElement)?.Attributes ?? (node as JsxSelfClosingElement)?.Attributes)!;
            var openingElement = node as JsxOpeningElement;
            return Core.Length(jsxAttributes.Properties) > 0 || (openingElement is not null && openingElement.Parent is JsxElement parentElement && Core.Length(parentElement.Children) > 0)
                ? [jsxAttributes]
                : Core.EmptyArray<Expression>.Value;
        }
        var effectiveArgsSource = ArgumentsOfUnion(node) ?? Core.EmptyArray<Expression>.Value;
        var spreadIndex = GetSpreadArgumentIndex(effectiveArgsSource);
        if (spreadIndex >= 0)
        {
            // Create synthetic arguments from spreads of tuple types.
            var effectiveArgs = new List<Expression>();
            for (var i = 0; i < spreadIndex; i++)
            {
                effectiveArgs.Add(effectiveArgsSource[i]);
            }
            for (var i = spreadIndex; i < effectiveArgsSource.Count; i++)
            {
                var arg = effectiveArgsSource[i];
                // We can call checkExpressionCached because spread expressions never have a contextual type.
                // PORT-NOTE: the reference's `arg.kind === SyntaxKind.SpreadElement && (flowLoopCount ? checkExpression(…) :
                // checkExpressionCached(…))` is the falsy `false` for a non-spread argument, so the `spreadType && isTupleType(spreadType)`
                // test below is false and the argument is pushed unchanged. A C# `?:` cannot produce `false | Type`, so the port states
                // that same value as a null type for the non-spread arm.
                IType? spreadType = arg.Kind == SyntaxKind.SpreadElement
                    ? (_flowLoopCount != 0 ? CheckExpression(((SpreadElement)arg).Expression!) : CheckExpressionCached(((SpreadElement)arg).Expression!))
                    : null;
                if (spreadType is not null && IsTupleType(spreadType))
                {
                    Core.ForEach(GetElementTypes((ITupleTypeReference)spreadType), (IType t, int i) =>
                    {
                        var elementFlags = ((ITupleType)((ITupleTypeReference)spreadType).Target!).ElementFlags![i];
                        var labeledElementDeclarations = ((ITupleType)((ITupleTypeReference)spreadType).Target!).LabeledElementDeclarations;
                        // PORT-NOTE: the reference reads `target.labeledElementDeclarations?.[i]`, whose value is
                        // `undefined` whenever the tuple target carries no labels at all (the reference's
                        // `createTupleType` default of `[]`, which the port's `CreateTupleType` also passes) or fewer
                        // labels than elements — a JS array read past the end. The port's List indexer throws
                        // instead, so the missing label is read as null.
                        var syntheticArg = CreateSyntheticExpression(arg, (elementFlags & ElementFlags.Rest) != 0 ? CreateArrayType(t) : t, (elementFlags & ElementFlags.Variable) != 0, i < Core.Length(labeledElementDeclarations) ? labeledElementDeclarations![i] : null);
                        effectiveArgs.Add(syntheticArg);
                        return (object?)null;
                    });
                }
                else
                {
                    effectiveArgs.Add(arg);
                }
            }
            return effectiveArgs;
        }
        return effectiveArgsSource;
    }

    /// <summary>ported from: src/compiler/checker.ts:36313-36325 — <c>getEffectiveDecoratorArguments</c>.</summary>
    /// <remarks>
    /// Returns the synthetic argument list for a decorator invocation.
    /// PORT-NOTE: <c>Debug.fail()</c> is TS's never-returning failure; the port states the same with
    /// <see cref="Debug.Fail{T}(string?)"/> so the member still returns a <c>List&lt;Expression&gt;</c>.
    /// <c>getDecoratorCallSignature</c> is a checker member outside this band.
    /// </remarks>
    internal List<Expression> GetEffectiveDecoratorArguments(Decorator node)
    {
        var expr = node.Expression!;
        var signature = GetDecoratorCallSignature(node);
        if (signature is not null)
        {
            var args = new List<Expression>();
            foreach (var param in signature.Parameters!)
            {
                var type = GetTypeOfSymbol(param);
                args.Add(CreateSyntheticExpression(expr, type));
            }
            return args;
        }
        return Debug.Fail<List<Expression>>();
    }

    /// <summary>ported from: src/compiler/checker.ts:36330-36335 — <c>getDecoratorArgumentCount</c>.</summary>
    /// <remarks>
    /// Returns the argument count for a decorator node that works like a function invocation.
    /// PORT-NOTE: <c>compilerOptions.experimentalDecorators</c> is <c>_compilerOptions.ExperimentalDecorators</c>
    /// (<c>bool?</c>, tested for truthiness) and <c>Math.min/Math.max</c> are <see cref="Math.Min(int, int)"/> and
    /// <see cref="Math.Max(int, int)"/>.
    /// </remarks>
    internal int GetDecoratorArgumentCount(Decorator node, ISignature signature)
        => _compilerOptions.ExperimentalDecorators == true
            ? GetLegacyDecoratorArgumentCount(node, signature)
            // Allow the runtime to oversupply arguments to an ES decorator as long as there's at least one parameter.
            : Math.Min(Math.Max(GetParameterCount(signature), 1), 2);

    /// <summary>ported from: src/compiler/checker.ts:36340-36357 — <c>getLegacyDecoratorArgumentCount</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the <c>switch (node.parent.kind)</c> reads the parent's kind; the port switches on
    /// <c>node.Parent!.Kind</c> and keeps the reference's <c>Debug.fail()</c> default as
    /// <see cref="Debug.Fail{T}(string?)"/>. <c>hasAccessorModifier</c> is a utilities member.
    /// </remarks>
    internal int GetLegacyDecoratorArgumentCount(Decorator node, ISignature signature)
    {
        switch (node.Parent!.Kind)
        {
            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.ClassExpression:
                return 1;
            case SyntaxKind.PropertyDeclaration:
                return Utilities.HasAccessorModifier(node.Parent!) ? 3 : 2;
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
                // For decorators with only two parameters we supply only two arguments
                return signature.Parameters!.Count <= 2 ? 2 : 3;
            case SyntaxKind.Parameter:
                return 3;
            default:
                return Debug.Fail<int>();
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:36359-36363 — <c>getDiagnosticSpanForCallNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS returns an anonymous <c>{ start, length, sourceFile }</c> object that its two callers destructure
    /// (<c>getDiagnosticForCallNode</c> :36366 and <c>invocationError</c> :37370); the port states it as the named
    /// tuple that carries the same three values.
    /// </remarks>
    internal (SourceFile SourceFile, int Start, int Length) GetDiagnosticSpanForCallNode(CallExpression node)
    {
        var sourceFile = Utilities.GetSourceFileOfNode(node)!;
        var span = Utilities.GetErrorSpanForNode(sourceFile, NodeTests.IsPropertyAccessExpression(node.Expression!) ? (Node)((PropertyAccessExpression)node.Expression!).Name! : node.Expression!);
        return (sourceFile, span.Start, span.Length);
    }

    /// <summary>ported from: src/compiler/checker.ts:36365-36379 — <c>getDiagnosticForCallNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>message</c> is TS's <c>DiagnosticMessage | DiagnosticMessageChain</c>; the port takes
    /// <c>object</c> and discriminates on <see cref="IDiagnosticMessageChain"/>, the same treatment
    /// <c>Checker.Diagnostics.cs</c>'s <c>errorOrSuggestion</c> gives the same union. The return type is
    /// <see cref="Diagnostic"/>: TS's <c>DiagnosticWithLocation</c> has no separate counterpart here
    /// (<c>Core/Diagnostic.cs</c>'s single class implements both diagnostic faces).
    /// </remarks>
    internal Diagnostic GetDiagnosticForCallNode(Node node, object message, params object?[] args)
    {
        if (NodeTests.IsCallExpression(node))
        {
            var (sourceFile, start, length) = GetDiagnosticSpanForCallNode((CallExpression)node);
            if (message is not IDiagnosticMessageChain)
            {
                return Utilities.CreateFileDiagnostic(sourceFile, start, length, (DiagnosticMessage)message, args);
            }
            return Utilities.CreateDiagnosticForFileFromMessageChain(sourceFile, (IDiagnosticMessageChain)message);
        }
        else
        {
            if (message is not IDiagnosticMessageChain)
            {
                return Utilities.CreateDiagnosticForNode(node, (DiagnosticMessage)message, args);
            }
            return Utilities.CreateDiagnosticForNodeFromMessageChain(Utilities.GetSourceFileOfNode(node)!, node, (IDiagnosticMessageChain)message);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:36381-36392 — <c>getErrorNodeForCallNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the three narrowed reads of the union's slots keep the reference's own kind tests; the final
    /// <c>return callLike</c> is the union fallback, i.e. the <see cref="Node"/> parameter itself.
    /// </remarks>
    internal Node GetErrorNodeForCallNode(Node callLike)
    {
        if (Utilities.IsCallOrNewExpression(callLike))
        {
            var expression = callLike is CallExpression call ? call.Expression! : (Expression)((NewExpression)callLike).Expression!;
            return NodeTests.IsPropertyAccessExpression(expression) ? (Node)((PropertyAccessExpression)expression).Name! : expression;
        }
        if (NodeTests.IsTaggedTemplateExpression(callLike))
        {
            var tag = (Expression)((TaggedTemplateExpression)callLike).Tag!;
            return NodeTests.IsPropertyAccessExpression(tag) ? (Node)((PropertyAccessExpression)tag).Name! : tag;
        }
        if (Utilities.IsJsxOpeningLikeElement(callLike))
        {
            return (Node)((callLike as JsxOpeningElement)?.TagName ?? (callLike as JsxSelfClosingElement)?.TagName)!;
        }
        return callLike;
    }

    /// <summary>ported from: src/compiler/checker.ts:36394-36408 — <c>isPromiseResolveArityError</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>node.expression.escapedText</c> is read off the <c>Identifier</c> the <c>isIdentifier</c> test
    /// admitted; <c>resolveName</c> is the checker's name resolver (utilities.ts:11491, a checker member in the port)
    /// and <c>getSymbolAtLocation</c>/<c>getGlobalPromiseConstructorSymbol</c> keep their unqualified call shapes.
    /// The reference's <c>decl.parent.parent.expression</c> chain is read through the same classes the five
    /// <c>is*</c> tests admitted (<c>ParameterDeclaration</c>, <c>FunctionExpression</c>/<c>ArrowFunction</c>,
    /// <c>NewExpression</c>), with the port's casts because the model's classes do not narrow structurally.
    /// </remarks>
    internal bool IsPromiseResolveArityError(Node node)
    {
        if (!NodeTests.IsCallExpression(node) || !NodeTests.IsIdentifier(((CallExpression)node).Expression!)) return false;

        var identifier = (Identifier)((CallExpression)node).Expression!;
        // PORT-NOTE: the port's ResolveName carries the resolver's sixth parameter (excludeGlobals), which the
        // reference's `resolveName` closure omits; the call passes `false`, the value its own default path uses.
        var symbol = ResolveName(identifier, identifier.EscapedText!, SymbolFlags.Value, /*nameNotFoundMessage*/ null, /*isUse*/ false, /*excludeGlobals*/ false);
        var decl = symbol?.ValueDeclaration;
        if (decl is null || !NodeTests.IsParameter((Node)decl) || !Utilities.IsFunctionExpressionOrArrowFunction((Node)((ParameterDeclaration)decl).Parent!) || !NodeTests.IsNewExpression((Node)((ParameterDeclaration)decl).Parent!.Parent!) || !NodeTests.IsIdentifier((Node)((NewExpression)((ParameterDeclaration)decl).Parent!.Parent!).Expression!))
        {
            return false;
        }

        var globalPromiseSymbol = GetGlobalPromiseConstructorSymbol(/*reportErrors*/ false);
        if (globalPromiseSymbol is null) return false;

        var constructorSymbol = GetSymbolAtLocation((Node)((NewExpression)((ParameterDeclaration)decl).Parent!.Parent!).Expression!, /*ignoreErrors*/ true);
        return ReferenceEquals(constructorSymbol, globalPromiseSymbol);
    }

    /// <summary>ported from: src/compiler/checker.ts:36410-36495 — <c>getArgumentArityError</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: <c>min</c>/<c>max</c>/<c>maxBelow</c>/<c>minAbove</c> are <c>number</c>s initialized from
    /// <c>±Infinity</c>; the port uses <c>double</c> and <c>double.PositiveInfinity</c>/<c>double.NegativeInfinity</c>. The
    /// <c>parameterRange</c> string concatenation (<c>min + "-" + max</c>) is the interpolation
    /// <c>$"{min}-{max}"</c>, and a lone count interpolates the same way JS's <c>+</c> does for these finite values.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>DiagnosticAndArguments</c> is TS's <c>[DiagnosticMessage, ...DiagnosticArguments]</c> tuple; the
    /// port states it as an <c>object?[]</c> whose first element is the message, which is how
    /// <see cref="Utilities.CreateDiagnosticForNode"/> takes its <c>params</c> arguments. The too-long branch's
    /// <c>factory.createNodeArray(args.slice(max))</c> (checker.ts:36481, factory/nodeFactory.ts:7407, i.e.
    /// <see cref="NodeFactory.Factory"/>) relies on array covariance to reach
    /// <c>createDiagnosticForNodeArray(…, NodeArray&lt;Node&gt;, …)</c>; the port copies the excess arguments into a
    /// <c>NodeArray&lt;Node&gt;</c> because C# generics are invariant — only the array's range is read, so the copy is
    /// unobservable (the reference's array is a fresh copy too).
    /// </para>
    /// </remarks>
    internal Diagnostic GetArgumentArityError(Node node, IList<ISignature> signatures, IList<Expression> args, DiagnosticMessage? headMessage = null)
    {
        var spreadIndex = GetSpreadArgumentIndex(args);
        if (spreadIndex > -1)
        {
            return Utilities.CreateDiagnosticForNode(args[spreadIndex], Diagnostics.A_spread_argument_must_either_have_a_tuple_type_or_be_passed_to_a_rest_parameter);
        }
        var min = double.PositiveInfinity; // smallest parameter count
        var max = double.NegativeInfinity; // largest parameter count
        var maxBelow = double.NegativeInfinity; // largest parameter count that is smaller than the number of arguments
        var minAbove = double.PositiveInfinity; // smallest parameter count that is larger than the number of arguments

        ISignature? closestSignature = null;
        foreach (var sig in signatures)
        {
            var minParameter = GetMinArgumentCount(sig);
            var maxParameter = GetParameterCount(sig);
            // smallest/largest parameter counts
            if (minParameter < min)
            {
                min = minParameter;
                closestSignature = sig;
            }
            max = Math.Max(max, maxParameter);
            // shortest parameter count *longer than the call*/longest parameter count *shorter than the call*
            if (minParameter < args.Count && minParameter > maxBelow) maxBelow = minParameter;
            if (args.Count < maxParameter && maxParameter < minAbove) minAbove = maxParameter;
        }
        var hasRestParameter = Core.Some(signatures, HasEffectiveRestParameter);
        // PORT-NOTE: TS's `parameterRange` is a `number | string` (a single count stays a number); the port states
        // every arm as the string the diagnostic interpolates and the one numeric test below as the same text, so the
        // value the message sees is unchanged.
        var parameterRange = hasRestParameter ? $"{min}"
            : min < max ? $"{min}-{max}"
            : $"{min}";
        var isVoidPromiseError = !hasRestParameter && parameterRange == $"{1}" && args.Count == 0 && IsPromiseResolveArityError(node);
        if (isVoidPromiseError && Utilities.IsInJSFile(node))
        {
            return GetDiagnosticForCallNode(node, Diagnostics.Expected_1_argument_but_got_0_new_Promise_needs_a_JSDoc_hint_to_produce_a_resolve_that_can_be_called_without_arguments);
        }
        var error = NodeTests.IsDecorator(node) ?
            hasRestParameter ? Diagnostics.The_runtime_will_invoke_the_decorator_with_1_arguments_but_the_decorator_expects_at_least_0 :
                Diagnostics.The_runtime_will_invoke_the_decorator_with_1_arguments_but_the_decorator_expects_0 :
            hasRestParameter ? Diagnostics.Expected_at_least_0_arguments_but_got_1 :
            isVoidPromiseError ? Diagnostics.Expected_0_arguments_but_got_1_Did_you_forget_to_include_void_in_your_type_argument_to_Promise :
            Diagnostics.Expected_0_arguments_but_got_1;

        if (min < args.Count && args.Count < max)
        {
            // between min and max, but with no matching overload
            if (headMessage is not null)
            {
                var chain = Utilities.ChainDiagnosticMessages(/*details*/ null, Diagnostics.No_overload_expects_0_arguments_but_overloads_do_exist_that_expect_either_1_or_2_arguments, args.Count, maxBelow, minAbove);
                chain = Utilities.ChainDiagnosticMessages(chain, headMessage);
                return GetDiagnosticForCallNode(node, chain);
            }
            return GetDiagnosticForCallNode(node, Diagnostics.No_overload_expects_0_arguments_but_overloads_do_exist_that_expect_either_1_or_2_arguments, args.Count, maxBelow, minAbove);
        }
        else if (args.Count < min)
        {
            // too short: put the error span on the call expression, not any of the args
            Diagnostic diagnostic;
            if (headMessage is not null)
            {
                var chain = Utilities.ChainDiagnosticMessages(/*details*/ null, error, parameterRange, args.Count);
                chain = Utilities.ChainDiagnosticMessages(chain, headMessage);
                diagnostic = GetDiagnosticForCallNode(node, chain);
            }
            else
            {
                diagnostic = GetDiagnosticForCallNode(node, error, parameterRange, args.Count);
            }
            // PORT-NOTE: TS reads `closestSignature?.declaration?.parameters[closestSignature.thisParameter ? args.length + 1 : args.length]`
            // off the `SignatureDeclaration | JSDocSignature` union, whose arms declare different parameter element types
            // (a JSDoc signature's are JSDocParameterTags); the port reads each arm's own list and keeps the reference's
            // out-of-range read as an explicit bound test, exactly as the `thisParameter` index above is stated.
            var parameterIndex = closestSignature?.ThisParameter is not null ? args.Count + 1 : args.Count;
            Node? parameter = closestSignature?.Declaration switch
            {
                ISignatureDeclarationBase declaration when declaration.Parameters is not null && parameterIndex < declaration.Parameters.Count
                    => declaration.Parameters[parameterIndex],
                JSDocSignature jsDocSignature when jsDocSignature.Parameters is not null && parameterIndex < jsDocSignature.Parameters.Count
                    => jsDocSignature.Parameters[parameterIndex],
                _ => null,
            };
            if (parameter is not null)
            {
                // PORT-NOTE: the reference reads `parameter.name` off that same union; the port names its two arms (a JSDoc
                // parameter tag is a JSDocPropertyLikeTag), since neither common base declares the slot.
                var parameterName = parameter switch
                {
                    ParameterDeclaration parameterDeclaration => parameterDeclaration.Name,
                    JSDocPropertyLikeTag propertyLikeTag => propertyLikeTag.Name,
                    _ => null,
                };
                object?[] messageAndArgs = Utilities.IsBindingPattern(parameterName) ? [Diagnostics.An_argument_matching_this_binding_pattern_was_not_provided]
                    : Utilities.IsRestParameter(parameter) ? [Diagnostics.Arguments_for_the_rest_parameter_0_were_not_provided, Utilities.IdText(Utilities.GetFirstIdentifier(parameterName!))]
                    : [Diagnostics.An_argument_for_0_was_not_provided, parameterName is null ? args.Count : Utilities.IdText(Utilities.GetFirstIdentifier(parameterName))];
                var parameterError = Utilities.CreateDiagnosticForNode(parameter, (DiagnosticMessage)messageAndArgs[0]!, messageAndArgs[1..]);
                return Utilities.AddRelatedInfo(diagnostic, parameterError);
            }
            return diagnostic;
        }
        else
        {
            // too long; error goes on the excess parameters
            var excessArguments = new List<Node>();
            for (var i = (int)max; i < args.Count; i++)
            {
                excessArguments.Add(args[i]);
            }
            var errorSpan = NodeFactory.Factory.CreateNodeArray(excessArguments, /*hasTrailingComma*/ null);
            var pos = Core.First(errorSpan).Pos;
            var end = Core.Last(errorSpan).End;
            if (end == pos)
            {
                end++;
            }
            Utilities.SetTextRangePosEnd(errorSpan, pos, end);
            if (headMessage is not null)
            {
                var chain = Utilities.ChainDiagnosticMessages(/*details*/ null, error, parameterRange, args.Count);
                chain = Utilities.ChainDiagnosticMessages(chain, headMessage);
                return Utilities.CreateDiagnosticForNodeArrayFromMessageChain(Utilities.GetSourceFileOfNode(node)!, errorSpan, chain);
            }
            return Utilities.CreateDiagnosticForNodeArray(Utilities.GetSourceFileOfNode(node)!, errorSpan, error, parameterRange, args.Count);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:36497-36538 — <c>getTypeArgumentArityError</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the four calls whose span is the type-argument list go to
    /// <see cref="Utilities.CreateDiagnosticForNodeArray"/>, whose parameter is the class
    /// <c>NodeArray&lt;Node&gt;</c> while the reference passes its <c>NodeArray&lt;TypeNode&gt;</c> (covariance makes
    /// that work in TS and not in C#); the port copies the list once, preserving the range, because only the range is
    /// read. The <c>-Infinity</c>/<c>Infinity</c> sentinels are <c>double</c>s, and <c>min + "-" + max</c> is the
    /// interpolation <c>$"{min}-{max}"</c>.
    /// </remarks>
    internal Diagnostic GetTypeArgumentArityError(Node node, IList<ISignature> signatures, NodeArray<ITypeNode> typeArguments, DiagnosticMessage? headMessage = null)
    {
        var argCount = typeArguments.Count;
        // PORT-NOTE: the copy described in the remarks; the reference passes `typeArguments` itself.
        var diagnosticNodes = new NodeArray<Node> { Pos = typeArguments.Pos, End = typeArguments.End };
        foreach (var typeArgument in typeArguments)
        {
            diagnosticNodes.Add((Node)typeArgument);
        }
        // No overloads exist
        if (signatures.Count == 1)
        {
            var sig = signatures[0];
            var min = GetMinTypeArgumentCount(sig.TypeParameters);
            var max = Core.Length(sig.TypeParameters);
            if (headMessage is not null)
            {
                var chain = Utilities.ChainDiagnosticMessages(/*details*/ null, Diagnostics.Expected_0_type_arguments_but_got_1, min < max ? $"{min}-{max}" : min, argCount);
                chain = Utilities.ChainDiagnosticMessages(chain, headMessage);
                return Utilities.CreateDiagnosticForNodeArrayFromMessageChain(Utilities.GetSourceFileOfNode(node)!, diagnosticNodes, chain);
            }
            return Utilities.CreateDiagnosticForNodeArray(Utilities.GetSourceFileOfNode(node)!, diagnosticNodes, Diagnostics.Expected_0_type_arguments_but_got_1, min < max ? $"{min}-{max}" : min, argCount);
        }
        // Overloads exist
        var belowArgCount = double.NegativeInfinity;
        var aboveArgCount = double.PositiveInfinity;
        foreach (var sig in signatures)
        {
            var min = GetMinTypeArgumentCount(sig.TypeParameters);
            var max = Core.Length(sig.TypeParameters);
            if (min > argCount)
            {
                aboveArgCount = Math.Min(aboveArgCount, min);
            }
            else if (max < argCount)
            {
                belowArgCount = Math.Max(belowArgCount, max);
            }
        }
        if (belowArgCount != double.NegativeInfinity && aboveArgCount != double.PositiveInfinity)
        {
            if (headMessage is not null)
            {
                var chain = Utilities.ChainDiagnosticMessages(/*details*/ null, Diagnostics.No_overload_expects_0_type_arguments_but_overloads_do_exist_that_expect_either_1_or_2_type_arguments, argCount, belowArgCount, aboveArgCount);
                chain = Utilities.ChainDiagnosticMessages(chain, headMessage);
                return Utilities.CreateDiagnosticForNodeArrayFromMessageChain(Utilities.GetSourceFileOfNode(node)!, diagnosticNodes, chain);
            }
            return Utilities.CreateDiagnosticForNodeArray(Utilities.GetSourceFileOfNode(node)!, diagnosticNodes, Diagnostics.No_overload_expects_0_type_arguments_but_overloads_do_exist_that_expect_either_1_or_2_type_arguments, argCount, belowArgCount, aboveArgCount);
        }
        if (headMessage is not null)
        {
            var chain = Utilities.ChainDiagnosticMessages(/*details*/ null, Diagnostics.Expected_0_type_arguments_but_got_1, belowArgCount == double.NegativeInfinity ? aboveArgCount : belowArgCount, argCount);
            chain = Utilities.ChainDiagnosticMessages(chain, headMessage);
            return Utilities.CreateDiagnosticForNodeArrayFromMessageChain(Utilities.GetSourceFileOfNode(node)!, diagnosticNodes, chain);
        }
        return Utilities.CreateDiagnosticForNodeArray(Utilities.GetSourceFileOfNode(node)!, diagnosticNodes, Diagnostics.Expected_0_type_arguments_but_got_1, belowArgCount == double.NegativeInfinity ? aboveArgCount : belowArgCount, argCount);
    }

    /// <summary>ported from: src/compiler/checker.ts:36540-36875 — <c>resolveCall</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the reference's captured variables (<c>candidatesForArgumentError</c>,
    /// <c>candidateForArgumentArityError</c>, <c>candidateForTypeArgumentError</c>, <c>result</c>,
    /// <c>argCheckMode</c>) are mutated by the two nested functions; the port states them as locals and the nested
    /// functions as C# local functions, which capture and mutate the same locals. <c>reportErrors</c> is TS truthiness
    /// of <c>!isInferencePartiallyBlocked &amp;&amp; !candidatesOutArray</c> over the
    /// <see cref="_isInferencePartiallyBlocked"/> field.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>typeArguments</c> is read off the union after the reference's own four tests
    /// (<c>!isDecorator &amp;&amp; !isInstanceof &amp;&amp; !isSuperCall(node) &amp;&amp; !isJsxOpenFragment</c>); the
    /// super test reads <c>(node as CallExpression).expression.kind</c>, whose arm is guaranteed by those tests.
    /// <c>some(typeArguments)</c> is <c>Core.Some</c>'s length test, <c>some(args, isContextSensitive)</c> is
    /// <c>Core.Some</c> with the predicate, and the relations are the checker's
    /// <c>_subtypeRelation</c>/<c>_assignableRelation</c> tables.
    /// </para>
    /// <para>
    /// PORT-NOTE: the error-reporting block's <c>flatten(allDiagnostics)</c> becomes
    /// <c>Core.Flatten&lt;Diagnostic&gt;</c> over the list-of-lists (a <c>List&lt;List&lt;Diagnostic&gt;&gt;</c> is not
    /// an <c>IList&lt;object?&gt;</c>), and the <c>[...flatMap(...)]</c> spread that guarantees a fresh mutable array
    /// becomes the explicit accumulation loop, which does the same. The hand-built diagnostic object literal becomes
    /// the port's <see cref="Diagnostic"/> construction with the same four values and the related list attached.
    /// </para>
    /// </remarks>
    internal ISignature ResolveCall(Node node, IList<ISignature> signatures, List<ISignature>? candidatesOutArray, CheckMode checkMode, SignatureFlags callChainFlags, DiagnosticMessage? headMessage = null)
    {
        using var allocProbe = AllocationProbe.Enter("Checker.ResolveCall");
        var isTaggedTemplate = node.Kind == SyntaxKind.TaggedTemplateExpression;
        var isDecorator = node.Kind == SyntaxKind.Decorator;
        var isJsxOpeningOrSelfClosingElement = Utilities.IsJsxOpeningLikeElement(node);
        var isJsxOpenFragment = NodeTests.IsJsxOpeningFragment(node);
        var isInstanceof = node.Kind == SyntaxKind.BinaryExpression;
        var reportErrors = !_isInferencePartiallyBlocked && candidatesOutArray is null;

        // The following variables are captured and modified by calls to chooseOverload.
        // If overload resolution or type argument inference fails, we want to report the
        // best error possible. The best error is one which says that an argument was not
        // assignable to a parameter. This implies that everything else about the overload
        // was fine. So if there is any overload that is only incorrect because of an
        // argument, we will report an error on that one.
        //
        //     function foo(s: string): void;
        //     function foo(n: number): void; // Report argument error on this overload
        //     function foo(): void;
        //     foo(true);
        //
        // If none of the overloads even made it that far, there are two possibilities.
        // There was a problem with type arguments for some overload, in which case
        // report an error on that. Or none of the overloads even had correct arity,
        // in which case give an arity error.
        //
        //     function foo<T extends string>(x: T): void; // Report type argument error
        //     function foo(): void;
        //     foo<number>(0);
        //
        List<ISignature>? candidatesForArgumentError = null;
        ISignature? candidateForArgumentArityError = null;
        ISignature? candidateForTypeArgumentError = null;
        ISignature? result = null;
        var argCheckMode = CheckMode.Normal;

        List<ISignature> candidates;
        NodeArray<ITypeNode>? typeArguments = null;
        if (!isDecorator && !isInstanceof && !Utilities.IsSuperCall(node) && !isJsxOpenFragment)
        {
            typeArguments = TypeArgumentsOfUnion(node);

            // We already perform checking on the type arguments on the class declaration itself.
            if (isTaggedTemplate || isJsxOpeningOrSelfClosingElement || ((node as CallExpression)?.Expression ?? (node as NewExpression)?.Expression)!.Kind != SyntaxKind.SuperKeyword)
            {
                Core.ForEach(typeArguments, (ITypeNode typeArgument, int _) =>
                {
                    CheckSourceElement(typeArgument);
                    return (object?)null;
                });
            }
        }

        candidates = candidatesOutArray ?? [];
        // reorderCandidates fills up the candidates array directly
        ReorderCandidates(signatures, candidates, callChainFlags);
        if (!isJsxOpenFragment)
        {
            if (candidates.Count == 0)
            {
                if (reportErrors)
                {
                    _diagnostics.Add(GetDiagnosticForCallNode(node, Diagnostics.Call_target_does_not_contain_any_signatures));
                }
                return ResolveErrorCall(node);
            }
        }
        var args = GetEffectiveCallArguments(node);

        // The excludeArgument array contains true for each context sensitive argument (an argument
        // is context sensitive it is susceptible to a one-time permanent contextual typing).
        //
        // The idea is that we will perform type argument inference & assignability checking once
        // without using the susceptible parameters that are functions, and once more for those
        // parameters, contextually typing each as we go along.
        //
        // For a tagged template, then the first argument be 'undefined' if necessary because it
        // represents a TemplateStringsArray.
        //
        // For a decorator, no arguments are susceptible to contextual typing due to the fact
        // decorators are applied to a declaration by the emitter, and not to an expression.
        var isSingleNonGenericCandidate = candidates.Count == 1 && candidates[0].TypeParameters is null;
        if (!isDecorator && !isSingleNonGenericCandidate && Core.Some(args, arg => IsContextSensitive(arg)))
        {
            argCheckMode = CheckMode.SkipContextSensitive;
        }

        // If we are in signature help, a trailing comma indicates that we intend to provide another argument,
        // so we will only accept overloads with arity at least 1 higher than the current number of provided arguments.
        var signatureHelpTrailingComma = (checkMode & CheckMode.IsForSignatureHelp) != 0 && node.Kind == SyntaxKind.CallExpression && ((CallExpression)node).Arguments!.HasTrailingComma;

        // Section 4.12.1:
        // if the candidate list contains one or more signatures for which the type of each argument
        // expression is a subtype of each corresponding parameter type, the return type of the first
        // of those signatures becomes the return type of the function call.
        // Otherwise, the return type of the first signature in the candidate list becomes the return
        // type of the function call.
        //
        // Whether the call is an error is determined by assignability of the arguments. The subtype pass
        // is just important for choosing the best signature. So in the case where there is only one
        // signature, the subtype pass is useless. So skipping it is an optimization.
        if (candidates.Count > 1)
        {
            result = ChooseOverload(candidates, _subtypeRelation, isSingleNonGenericCandidate, signatureHelpTrailingComma);
        }
        if (result is null)
        {
            result = ChooseOverload(candidates, _assignableRelation, isSingleNonGenericCandidate, signatureHelpTrailingComma);
        }
        var links = GetNodeLinks(node);
        if (links.ResolvedSignature is not null && !ReferenceEquals(links.ResolvedSignature, _resolvingSignature) && candidatesOutArray is null)
        {
            // There are 2 situations in which it's good to preemptively return the cached result here:
            //
            // 1. if the signature resolution originated on a node that itself depends on the contextual type
            // then it's possible that the resolved signature might not be the same as the one that would be computed in source order
            // since resolving such signature leads to resolving the potential outer signature, its arguments and thus the very same signature
            // it's possible that this inner resolution sets the resolvedSignature first.
            // In such a case we ignore the local result and reuse the correct one that was cached.
            //
            // 2. In certain circular-like situations it's possible that the compiler reentries this function for the same node.
            // It's possible to resolve the inner call against preemptively set empty members (for example in `resolveAnonymousTypeMembers`) of some type.
            // When that happens the compiler might report an error for that inner call but at the same time it might end up resolving the actual members of the other type.
            // This in turn creates a situation in which the outer call fails in `getSignatureApplicabilityError` due to a cached `RelationComparisonResult.Failed`
            // but when the compiler tries to report that error (in the code below) it also tries to elaborate it and that can succeed as types would be related against the *resolved* members of the other type.
            // This can hit `No error for last overload signature` assert but since that error was already reported when the inner call failed we can skip this step altogether here by returning the cached signature early.
            Debug.Assert(links.ResolvedSignature is not null);
            return links.ResolvedSignature;
        }
        if (result is not null)
        {
            return result;
        }
        result = GetCandidateForOverloadFailure(node, candidates, args, candidatesOutArray is not null, checkMode);
        // Preemptively cache the result; getResolvedSignature will do this after we return, but
        // we need to ensure that the result is present for the error checks below so that if
        // this signature is encountered again, we handle the circularity (rather than producing a
        // different result which may produce no errors and assert). Callers of getResolvedSignature
        // don't hit this issue because they only observe this result after it's had a chance to
        // be cached, but the error reporting code below executes before getResolvedSignature sets
        // resolvedSignature.
        links.ResolvedSignature = result;

        // No signatures were applicable. Now report errors based on the last applicable signature with
        // no arguments excluded from assignability checks.
        // If candidate is undefined, it means that no candidates had a suitable arity. In that case,
        // skip the checkApplicableSignature check.
        if (reportErrors)
        {
            // If the call expression is a synthetic call to a `[Symbol.hasInstance]` method then we will produce a head
            // message when reporting diagnostics that explains how we got to `right[Symbol.hasInstance](left)` from
            // `left instanceof right`, as it pertains to "Argument" related messages reported for the call.
            if (headMessage is null && isInstanceof)
            {
                headMessage = Diagnostics.The_left_hand_side_of_an_instanceof_expression_must_be_assignable_to_the_first_argument_of_the_right_hand_side_s_Symbol_hasInstance_method;
            }
            if (candidatesForArgumentError is not null)
            {
                if (candidatesForArgumentError.Count == 1 || candidatesForArgumentError.Count > 3)
                {
                    var lastCandidate = candidatesForArgumentError[^1];
                    IDiagnosticMessageChain? chain = null;
                    if (candidatesForArgumentError.Count > 3)
                    {
                        chain = Utilities.ChainDiagnosticMessages(chain, Diagnostics.The_last_overload_gave_the_following_error);
                        chain = Utilities.ChainDiagnosticMessages(chain, Diagnostics.No_overload_matches_this_call);
                    }
                    if (headMessage is not null)
                    {
                        chain = Utilities.ChainDiagnosticMessages(chain, headMessage);
                    }
                    var diags = GetSignatureApplicabilityError(node, args, lastCandidate, _assignableRelation, CheckMode.Normal, /*reportErrors*/ true, () => chain);
                    if (diags is not null)
                    {
                        foreach (var d in diags)
                        {
                            if (lastCandidate.Declaration is not null && candidatesForArgumentError.Count > 3)
                            {
                                Utilities.AddRelatedInfo(d, Utilities.CreateDiagnosticForNode(lastCandidate.Declaration, Diagnostics.The_last_overload_is_declared_here));
                            }
                            AddImplementationSuccessElaboration(lastCandidate, d);
                            _diagnostics.Add(d);
                        }
                    }
                    else
                    {
                        Debug.Fail("No error for last overload signature");
                    }
                }
                else
                {
                    var allDiagnostics = new List<List<Diagnostic>>();
                    var max = 0;
                    var min = int.MaxValue;
                    var minIndex = 0;
                    var i = 0;
                    foreach (var c in candidatesForArgumentError)
                    {
                        // PORT-NOTE: the reference's `chain2` body calls `signatureToString(c)` eagerly, and
                        // `checkTypeRelatedTo` invokes that chain while it reports the overload failure — so the printer
                        // runs, and instantiating the candidate's parameter types advances `typeCount`. Deferring this
                        // argument (Core/Diagnostic.cs's `DiagnosticArgument`, ruling A″) would leave those creations out
                        // entirely, so it is computed at the reference's own point of use.
                        Func<IDiagnosticMessageChain?> chain = () => Utilities.ChainDiagnosticMessages(/*details*/ null, Diagnostics.Overload_0_of_1_2_gave_the_following_error, i + 1, candidates.Count, SignatureToString(c));
                        var diags = GetSignatureApplicabilityError(node, args, c, _assignableRelation, CheckMode.Normal, /*reportErrors*/ true, chain);
                        if (diags is not null)
                        {
                            if (diags.Count <= min)
                            {
                                min = diags.Count;
                                minIndex = i;
                            }
                            max = Math.Max(max, diags.Count);
                            allDiagnostics.Add(diags);
                        }
                        else
                        {
                            Debug.Fail("No error for 3 or fewer overload signatures");
                        }
                        i++;
                    }

                    var overloadDiagnostics = max > 1 ? allDiagnostics[minIndex] : Core.Flatten<Diagnostic>(allDiagnostics.Cast<object?>().ToList());
                    Debug.Assert(overloadDiagnostics.Count > 0, "No errors reported for 3 or fewer overload signatures");
                    var overloadChain = Utilities.ChainDiagnosticMessages(
                        Core.Map(overloadDiagnostics, (Diagnostic d, int _) => Utilities.CreateDiagnosticMessageChainFromDiagnostic(d)),
                        Diagnostics.No_overload_matches_this_call);
                    if (headMessage is not null)
                    {
                        overloadChain = Utilities.ChainDiagnosticMessages(overloadChain, headMessage);
                    }
                    // The below is a spread to guarantee we get a new (mutable) array - our `flatMap` helper tries to do "smart" optimizations where it reuses input
                    // arrays and the emptyArray singleton where possible, which is decidedly not what we want while we're still constructing this diagnostic
                    var related = new List<IDiagnosticRelatedInformation>();
                    foreach (var d in overloadDiagnostics)
                    {
                        if (d.RelatedInformation is not null)
                        {
                            related.AddRange(d.RelatedInformation);
                        }
                    }
                    Diagnostic diag;
                    if (Core.Every(overloadDiagnostics, (Diagnostic d, int _) => d.Start == overloadDiagnostics[0].Start && d.Length == overloadDiagnostics[0].Length && ReferenceEquals(d.File, overloadDiagnostics[0].File)))
                    {
                        diag = new Diagnostic(overloadChain, overloadChain.Category, overloadChain.Code, overloadDiagnostics[0].Start, overloadDiagnostics[0].Length, file: overloadDiagnostics[0].File)
                        {
                            RelatedInformation = related,
                        };
                    }
                    else
                    {
                        diag = Utilities.CreateDiagnosticForNodeFromMessageChain(Utilities.GetSourceFileOfNode(node)!, GetErrorNodeForCallNode(node), overloadChain, related);
                    }
                    AddImplementationSuccessElaboration(candidatesForArgumentError[0], diag);
                    _diagnostics.Add(diag);
                }
            }
            else if (candidateForArgumentArityError is not null)
            {
                _diagnostics.Add(GetArgumentArityError(node, [candidateForArgumentArityError], args, headMessage));
            }
            else if (candidateForTypeArgumentError is not null)
            {
                CheckTypeArguments(candidateForTypeArgumentError, TypeArgumentsOfUnion(node)!, /*reportErrors*/ true, headMessage);
            }
            else if (!isJsxOpenFragment)
            {
                var signaturesWithCorrectTypeArgumentArity = Core.Filter(signatures, s => HasCorrectTypeArgumentArity(s, typeArguments)) ?? Core.EmptyArray<ISignature>.Value;
                if (signaturesWithCorrectTypeArgumentArity.Count == 0)
                {
                    _diagnostics.Add(GetTypeArgumentArityError(node, signatures, typeArguments!, headMessage));
                }
                else
                {
                    _diagnostics.Add(GetArgumentArityError(node, signaturesWithCorrectTypeArgumentArity, args, headMessage));
                }
            }
        }

        return result;

        // PORT-NOTE: the reference declares this as a nested function and reads/writes the captured
        // candidatesForArgumentError/candidateForArgumentArityError/candidateForTypeArgumentError locals around its
        // chooseOverload call; the port states it as the same kind of local function. `implDecl` is cast to the
        // IFunctionLikeDeclarationBase face the reference's isFunctionLikeDeclaration test admits, and
        // `getSignatureFromDeclaration` keeps its unqualified call shape.
        void AddImplementationSuccessElaboration(ISignature failed, Diagnostic diagnostic)
        {
            var oldCandidatesForArgumentError = candidatesForArgumentError;
            var oldCandidateForArgumentArityError = candidateForArgumentArityError;
            var oldCandidateForTypeArgumentError = candidateForTypeArgumentError;

            var failedSignatureDeclarations = failed.Declaration is not null && ((IDeclaration)failed.Declaration).Symbol is not null
                ? ((IDeclaration)failed.Declaration).Symbol!.Declarations ?? Core.EmptyArray<IDeclaration>.Value
                : Core.EmptyArray<IDeclaration>.Value;
            var isOverload = failedSignatureDeclarations.Count > 1;
            var implDecl = isOverload ? Core.Find(failedSignatureDeclarations, (IDeclaration d, int _) => Utilities.IsFunctionLikeDeclaration((Node)d) && Utilities.NodeIsPresent(((IFunctionLikeDeclarationBase)d).Body)) : null;
            if (implDecl is not null)
            {
                var candidate = GetSignatureFromDeclaration((Node)implDecl);
                var isSingleNonGenericImplCandidate = candidate.TypeParameters is null;
                if (ChooseOverload([candidate], _assignableRelation, isSingleNonGenericImplCandidate) is not null)
                {
                    Utilities.AddRelatedInfo(diagnostic, Utilities.CreateDiagnosticForNode((Node)implDecl, Diagnostics.The_call_would_have_succeeded_against_this_implementation_but_implementation_signatures_of_overloads_are_not_externally_visible));
                }
            }

            candidatesForArgumentError = oldCandidatesForArgumentError;
            candidateForArgumentArityError = oldCandidateForArgumentArityError;
            candidateForTypeArgumentError = oldCandidateForTypeArgumentError;
        }

        // PORT-NOTE: the reference's nested chooseOverload; it mutates the three captured candidate slots above.
        // `argCheckMode` is a flags enum, so the reference's `if (argCheckMode)` truthiness is `!= 0`.
        ISignature? ChooseOverload(List<ISignature> candidates, Dictionary<RelationKey, RelationComparisonResult> relation, bool isSingleNonGenericCandidate, bool signatureHelpTrailingComma = false)
        {
            using var allocProbe = AllocationProbe.Enter("Checker.ChooseOverload");
            candidatesForArgumentError = null;
            candidateForArgumentArityError = null;
            candidateForTypeArgumentError = null;

            if (isSingleNonGenericCandidate)
            {
                var candidate = candidates[0];
                if (Core.Some(typeArguments) || !HasCorrectArity(node, args, candidate, signatureHelpTrailingComma))
                {
                    return null;
                }
                if (GetSignatureApplicabilityError(node, args, candidate, relation, CheckMode.Normal, /*reportErrors*/ false, /*containingMessageChain*/ null) is not null)
                {
                    candidatesForArgumentError = [candidate];
                    return null;
                }
                return candidate;
            }

            for (var candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
            {
                var candidate = candidates[candidateIndex];
                if (!HasCorrectTypeArgumentArity(candidate, typeArguments) || !HasCorrectArity(node, args, candidate, signatureHelpTrailingComma))
                {
                    continue;
                }

                ISignature checkCandidate;
                IInferenceContext? inferenceContext = null;

                if (candidate.TypeParameters is not null)
                {
                    List<IType>? typeArgumentTypes;
                    if (Core.Some(typeArguments))
                    {
                        typeArgumentTypes = CheckTypeArguments(candidate, typeArguments!, /*reportErrors*/ false);
                        if (typeArgumentTypes is null)
                        {
                            candidateForTypeArgumentError = candidate;
                            continue;
                        }
                    }
                    else
                    {
                        inferenceContext = CreateInferenceContext(candidate.TypeParameters, candidate, /*flags*/ Utilities.IsInJSFile(node) ? InferenceFlags.AnyDefault : InferenceFlags.None);
                        typeArgumentTypes = InferTypeArguments(node, candidate, args, argCheckMode | CheckMode.SkipGenericFunctions, inferenceContext);
                        argCheckMode |= (inferenceContext.Flags & InferenceFlags.SkippedGenericFunction) != 0 ? CheckMode.SkipGenericFunctions : CheckMode.Normal;
                    }
                    checkCandidate = GetSignatureInstantiation(candidate, typeArgumentTypes, Utilities.IsInJSFile(candidate.Declaration), inferenceContext?.InferredTypeParameters);
                    // If the original signature has a generic rest type, instantiation may produce a
                    // signature with different arity and we need to perform another arity check.
                    if (GetNonArrayRestType(candidate) is not null && !HasCorrectArity(node, args, checkCandidate, signatureHelpTrailingComma))
                    {
                        candidateForArgumentArityError = checkCandidate;
                        continue;
                    }
                }
                else
                {
                    checkCandidate = candidate;
                }
                if (GetSignatureApplicabilityError(node, args, checkCandidate, relation, argCheckMode, /*reportErrors*/ false, /*containingMessageChain*/ null) is not null)
                {
                    // Give preference to error candidates that have no rest parameters (as they are more specific)
                    (candidatesForArgumentError ??= []).Add(checkCandidate);
                    continue;
                }
                if (argCheckMode != 0)
                {
                    // If one or more context sensitive arguments were excluded, we start including
                    // them now (and keeping do so for any subsequent candidates) and perform a second
                    // round of type inference and applicability checking for this particular candidate.
                    argCheckMode = CheckMode.Normal;
                    if (inferenceContext is not null)
                    {
                        var typeArgumentTypes = InferTypeArguments(node, candidate, args, argCheckMode, inferenceContext);
                        checkCandidate = GetSignatureInstantiation(candidate, typeArgumentTypes, Utilities.IsInJSFile(candidate.Declaration), inferenceContext.InferredTypeParameters);
                        // If the original signature has a generic rest type, instantiation may produce a
                        // signature with different arity and we need to perform another arity check.
                        if (GetNonArrayRestType(candidate) is not null && !HasCorrectArity(node, args, checkCandidate, signatureHelpTrailingComma))
                        {
                            candidateForArgumentArityError = checkCandidate;
                            continue;
                        }
                    }
                    if (GetSignatureApplicabilityError(node, args, checkCandidate, relation, argCheckMode, /*reportErrors*/ false, /*containingMessageChain*/ null) is not null)
                    {
                        // Give preference to error candidates that have no rest parameters (as they are more specific)
                        (candidatesForArgumentError ??= []).Add(checkCandidate);
                        continue;
                    }
                }
                candidates[candidateIndex] = checkCandidate;
                return checkCandidate;
            }

            return null;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:36877-36892 — <c>getCandidateForOverloadFailure</c>.</summary>
    /// <remarks>
    /// No signature was applicable. We have already reported the errors for the invalid signature.
    /// PORT-NOTE: <c>candidates.some(c =&gt; !!c.typeParameters)</c> is <c>Core.Some</c> with the slot test, and
    /// <c>checkNodeDeferred</c> is a checker member outside this band.
    /// </remarks>
    internal ISignature GetCandidateForOverloadFailure(
        Node node,
        List<ISignature> candidates,
        IList<Expression> args,
        bool hasCandidatesOutArray,
        CheckMode checkMode)
    {
        Debug.Assert(candidates.Count > 0); // Else should not have called this.
        CheckNodeDeferred(node);
        // Normally we will combine overloads. Skip this if they have type parameters since that's hard to combine.
        // Don't do this if there is a `candidatesOutArray`,
        // because then we want the chosen best candidate to be one of the overloads, not a combination.
        return hasCandidatesOutArray || candidates.Count == 1 || Core.Some(candidates, c => c.TypeParameters is not null)
            ? PickLongestCandidateSignature(node, candidates, args, checkMode)
            : CreateUnionOfSignaturesForOverloadFailure(candidates);
    }

    /// <summary>ported from: src/compiler/checker.ts:36894-36930 — <c>createUnionOfSignaturesForOverloadFailure</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>minAndMax(candidates, getNumNonRestParameters)</c> is a <c>utilities.ts</c> member
    /// (utilities.ts:10362-10372) that no slice has landed yet; the port calls
    /// <c>Utilities.MinAndMax</c> with the same mapper and destructures its <c>{ min, max }</c> result, so the two
    /// counts read the same. <c>mapDefined</c>/<c>map</c> are <c>Core.MapDefined</c>/<c>Core.Map</c>; the reference's
    /// <c>createSignature</c> call keeps its argument order, with <c>typeParameters</c> stated as the <c>null</c> the
    /// comment justifies.
    /// </remarks>
    internal ISignature CreateUnionOfSignaturesForOverloadFailure(IList<ISignature> candidates)
    {
        var thisParameters = Core.MapDefined(candidates, (ISignature c, int _) => c.ThisParameter);
        ISymbol? thisParameter = null;
        if (thisParameters.Count > 0)
        {
            thisParameter = CreateCombinedSymbolFromTypes(thisParameters, Core.Map(thisParameters, (ISymbol p, int _) => GetTypeOfParameter(p))!);
        }
        var (minArgumentCount, maxNonRestParam) = Utilities.MinAndMax(candidates, (ISignature s) => GetNumNonRestParameters(s));
        var parameters = new List<ISymbol>();
        for (var i = 0; i < maxNonRestParam; i++)
        {
            var symbols = Core.MapDefined(candidates, (ISignature s, int _) =>
                SignatureHasRestParameter(s)
                    ? i < s.Parameters!.Count - 1 ? s.Parameters[i] : Core.Last(s.Parameters)
                    : i < s.Parameters!.Count ? s.Parameters[i] : null);
            Debug.Assert(symbols.Count != 0);
            parameters.Add(CreateCombinedSymbolFromTypes(symbols, Core.MapDefined(candidates, (ISignature candidate, int _) => TryGetTypeAtPosition(candidate, i))));
        }
        var restParameterSymbols = Core.MapDefined(candidates, (ISignature c, int _) => SignatureHasRestParameter(c) ? Core.Last(c.Parameters!) : null);
        var flags = SignatureFlags.IsSignatureCandidateForOverloadFailure;
        if (restParameterSymbols.Count != 0)
        {
            var type = CreateArrayType(GetUnionType(Core.MapDefined(candidates, (ISignature c, int _) => TryGetRestTypeOfSignature(c)), UnionReduction.Subtype));
            parameters.Add(CreateCombinedSymbolForOverloadFailure(restParameterSymbols, type));
            flags |= SignatureFlags.HasRestParameter;
        }
        if (Core.Some(candidates, SignatureHasLiteralTypes))
        {
            flags |= SignatureFlags.HasLiteralTypes;
        }
        return CreateSignature(
            candidates[0].Declaration,
            /*typeParameters*/ null, // Before calling this we tested for `!candidates.some(c => !!c.typeParameters)`.
            thisParameter,
            parameters,
            /*resolvedReturnType*/ GetIntersectionType(Core.Map(candidates, (ISignature c, int _) => GetReturnTypeOfSignature(c))!),
            /*resolvedTypePredicate*/ null,
            minArgumentCount,
            flags);
    }

    /// <summary>ported from: src/compiler/checker.ts:36932-36935 — <c>getNumNonRestParameters</c>.</summary>
    internal static int GetNumNonRestParameters(ISignature signature)
    {
        var numParams = signature.Parameters!.Count;
        return SignatureHasRestParameter(signature) ? numParams - 1 : numParams;
    }

    /// <summary>ported from: src/compiler/checker.ts:36937-36939 — <c>createCombinedSymbolFromTypes</c>.</summary>
    internal ISymbol CreateCombinedSymbolFromTypes(IList<ISymbol> sources, List<IType> types)
        => CreateCombinedSymbolForOverloadFailure(sources, GetUnionType(types, UnionReduction.Subtype));

    /// <summary>ported from: src/compiler/checker.ts:36941-36944 — <c>createCombinedSymbolForOverloadFailure</c>.</summary>
    /// <remarks>
    /// This function is currently only used for erroneous overloads, so it's good enough to just use the first source.
    /// PORT-NOTE: <c>first(sources)</c> is <see cref="Core.First{T}"/> and <c>createSymbolWithType</c> is a checker
    /// member outside this band.
    /// </remarks>
    internal ISymbol CreateCombinedSymbolForOverloadFailure(IList<ISymbol> sources, IType type)
        => CreateSymbolWithType(Core.First(sources), type);

    /// <summary>ported from: src/compiler/checker.ts:36946-36966 — <c>pickLongestCandidateSignature</c>.</summary>
    /// <remarks>
    /// Pick the longest signature. This way we can get a contextual type for cases like:
    /// <code>declare function f(a: { xa: number; xb: number; }, b: number); f({ |</code>
    /// Also, use explicitly-supplied type arguments if they are provided, so we can get a contextual signature in
    /// cases like: <code>declare function f&lt;T&gt;(k: keyof T); f&lt;Foo&gt;("</code>
    /// PORT-NOTE: <c>apparentArgumentCount === undefined ? args.length : apparentArgumentCount</c> is the reference's
    /// <c>undefined</c>-sentinel read of the slot the public wrapper in <c>getResolvedSignatureWorker</c> sets; the port
    /// reads <see cref="_apparentArgumentCount"/> as <c>int?</c> with <c>??</c>. The type-argument read goes through
    /// <c>callLikeExpressionMayHaveTypeArguments</c>'s guard, i.e. <see cref="TypeArgumentsOfUnion"/>.
    /// </remarks>
    internal ISignature PickLongestCandidateSignature(Node node, List<ISignature> candidates, IList<Expression> args, CheckMode checkMode)
    {
        // Pick the longest signature. This way we can get a contextual type for cases like:
        //     declare function f(a: { xa: number; xb: number; }, b: number);
        //     f({ |
        // Also, use explicitly-supplied type arguments if they are provided, so we can get a contextual signature in cases like:
        //     declare function f<T>(k: keyof T);
        //     f<Foo>("
        var bestIndex = GetLongestCandidateIndex(candidates, _apparentArgumentCount ?? args.Count);
        var candidate = candidates[bestIndex];
        var typeParameters = candidate.TypeParameters;
        if (typeParameters is null)
        {
            return candidate;
        }

        IList<ITypeNode>? typeArgumentNodes = CallLikeExpressionMayHaveTypeArguments(node) ? TypeArgumentsOfUnion(node) : null;
        var instantiated = typeArgumentNodes is not null
            ? CreateSignatureInstantiation(candidate, GetTypeArgumentsFromNodes(typeArgumentNodes, typeParameters, Utilities.IsInJSFile(node)))
            : InferSignatureInstantiationForOverloadFailure(node, typeParameters, candidate, args, checkMode);
        candidates[bestIndex] = instantiated;
        return instantiated;
    }

    /// <summary>ported from: src/compiler/checker.ts:36968-36977 — <c>getTypeArgumentsFromNodes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's result is <c>readonly Type[]</c> built by <c>map</c> (a fresh mutable array);
    /// <see cref="CreateSignatureInstantiation"/> takes <c>List&lt;IType&gt;?</c>, so the port states the same array as
    /// <c>List&lt;IType&gt;</c>. <c>pop</c>/<c>push</c> are <c>RemoveAt(^1)</c>/<c>Add</c>.
    /// </remarks>
    internal List<IType> GetTypeArgumentsFromNodes(IList<ITypeNode> typeArgumentNodes, List<ITypeParameter> typeParameters, bool isJs)
    {
        var typeArguments = Core.Map(typeArgumentNodes, (ITypeNode node, int _) => GetTypeOfNode((Node)node))!;
        while (typeArguments.Count > typeParameters.Count)
        {
            typeArguments.RemoveAt(typeArguments.Count - 1);
        }
        while (typeArguments.Count < typeParameters.Count)
        {
            typeArguments.Add(GetDefaultFromTypeParameter(typeParameters[typeArguments.Count]) ?? GetConstraintOfTypeParameter(typeParameters[typeArguments.Count]) ?? GetDefaultTypeArgumentType(isJs));
        }
        return typeArguments;
    }

    /// <summary>ported from: src/compiler/checker.ts:36979-36983 — <c>inferSignatureInstantiationForOverloadFailure</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>flags</c> argument is <c>isInJSFile(node) ? InferenceFlags.AnyDefault :
    /// InferenceFlags.None</c> and the check mode ORs <c>SkipContextSensitive | SkipGenericFunctions</c>, both kept
    /// as written.
    /// </remarks>
    internal ISignature InferSignatureInstantiationForOverloadFailure(Node node, List<ITypeParameter> typeParameters, ISignature candidate, IList<Expression> args, CheckMode checkMode)
    {
        var inferenceContext = CreateInferenceContext(typeParameters, candidate, /*flags*/ Utilities.IsInJSFile(node) ? InferenceFlags.AnyDefault : InferenceFlags.None);
        var typeArgumentTypes = InferTypeArguments(node, candidate, args, checkMode | CheckMode.SkipContextSensitive | CheckMode.SkipGenericFunctions, inferenceContext);
        return CreateSignatureInstantiation(candidate, typeArgumentTypes);
    }

    /// <summary>ported from: src/compiler/checker.ts:36985-37002 — <c>getLongestCandidateIndex</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>-1</c> answers (no candidate with a rest parameter or enough parameters) are kept
    /// as the <c>-1</c> the caller indexes with, exactly as the reference does.
    /// </remarks>
    internal int GetLongestCandidateIndex(IList<ISignature> candidates, int argsCount)
    {
        var maxParamsIndex = -1;
        var maxParams = -1;

        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            var paramCount = GetParameterCount(candidate);
            if (HasEffectiveRestParameter(candidate) || paramCount >= argsCount)
            {
                return i;
            }
            if (paramCount > maxParams)
            {
                maxParams = paramCount;
                maxParamsIndex = i;
            }
        }

        return maxParamsIndex;
    }

    /// <summary>ported from: src/compiler/checker.ts:37004-37114 — <c>resolveCallExpression</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the reference reads <c>node.expression</c>/<c>node.arguments</c>/<c>node.typeArguments</c> off the
    /// <c>CallExpression</c> the resolver's own parameter type names; the port keeps that parameter type
    /// (<c>CallExpression</c>), so no union narrowing is needed here. <c>getContainingClass</c>,
    /// <c>checkSuperExpression</c>, <c>checkNonNullExpression</c>, <c>checkNonNullTypeWithReporter</c>,
    /// <c>reportCannotInvokePossiblyNullOrUndefinedError</c> and <c>skippedGenericFunction</c> are checker members
    /// outside this band and keep their unqualified call shapes; <c>silentNeverType</c>/<c>silentNeverSignature</c>
    /// are the <see cref="_silentNeverType"/>/<see cref="_silentNeverSignature"/> fields.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>isLineBreak(text.charCodeAt(skipTrivia(...) - 1))</c> has no direct port: C# strings have no
    /// <c>charCodeAt</c> and the port's <c>CharCodeAt</c> is a private member of <c>Utilities.Parser.cs</c>, so the
    /// same test is a bounds check plus <c>ScannerUtilities.IsLineBreak</c> on the character (TS's out-of-range
    /// <c>charCodeAt</c> yields <c>NaN</c>, which <c>isLineBreak</c> rejects).
    /// </para>
    /// </remarks>
    internal ISignature ResolveCallExpression(CallExpression node, List<ISignature>? candidatesOutArray, CheckMode checkMode)
    {
        if (node.Expression!.Kind == SyntaxKind.SuperKeyword)
        {
            var superType = CheckSuperExpression(node.Expression!);
            if (IsTypeAny(superType))
            {
                Core.ForEach(node.Arguments, (Expression arg, int _) =>
                {
                    CheckExpression(arg); // Still visit arguments so they get marked for visibility, etc
                    return (object?)null;
                });
                return _anySignature;
            }
            if (!IsErrorType(superType))
            {
                // In super call, the candidate signatures are the matching arity signatures of the base constructor function instantiated
                // with the type arguments specified in the extends clause.
                var baseTypeNode = Utilities.GetEffectiveBaseTypeNode(Utilities.GetContainingClass(node));
                if (baseTypeNode is not null)
                {
                    var baseConstructors = GetInstantiatedConstructorsForTypeArguments(superType, baseTypeNode.TypeArguments, baseTypeNode);
                    return ResolveCall(node, baseConstructors, candidatesOutArray, checkMode, SignatureFlags.None);
                }
            }
            return ResolveUntypedCall(node);
        }

        SignatureFlags callChainFlags;
        var funcType = CheckExpression(node.Expression!);
        if (Utilities.IsCallChain(node))
        {
            var nonOptionalType = GetOptionalExpressionType(funcType, node.Expression!);
            callChainFlags = ReferenceEquals(nonOptionalType, funcType) ? SignatureFlags.None :
                Utilities.IsOutermostOptionalChain(node) ? SignatureFlags.IsOuterCallChain :
                SignatureFlags.IsInnerCallChain;
            funcType = nonOptionalType;
        }
        else
        {
            callChainFlags = SignatureFlags.None;
        }

        funcType = CheckNonNullTypeWithReporter(
            funcType,
            node.Expression!,
            ReportCannotInvokePossiblyNullOrUndefinedError);

        if (ReferenceEquals(funcType, _silentNeverType))
        {
            return _silentNeverSignature;
        }

        var apparentType = GetApparentType(funcType);
        if (IsErrorType(apparentType))
        {
            // Another error has already been reported
            return ResolveErrorCall(node);
        }

        // Technically, this signatures list may be incomplete. We are taking the apparent type,
        // but we are not including call signatures that may have been added to the Object or
        // Function interface, since they have none by default. This is a bit of a leap of faith
        // that the user will not add any.
        var callSignatures = GetSignaturesOfType(apparentType, SignatureKind.Call);
        var numConstructSignatures = GetSignaturesOfType(apparentType, SignatureKind.Construct).Count;
        var funcTypeString = Diagnostic.DeferArg(() => TypeToString(funcType));

        // TS 1.0 Spec: 4.12
        // In an untyped function call no TypeArgs are permitted, Args can be any argument list, no contextual
        // types are provided for the argument expressions, and the result is always of type Any.
        if (IsUntypedFunctionCall(funcType, apparentType, callSignatures.Count, numConstructSignatures))
        {
            // The unknownType indicates that an error already occurred (and was reported).  No
            // need to report another error in this case.
            if (!IsErrorType(funcType) && node.TypeArguments is not null)
            {
                Error(node, Diagnostics.Untyped_function_calls_may_not_accept_type_arguments);
            }
            return ResolveUntypedCall(node);
        }
        // If FuncExpr's apparent type(section 3.8.1) is a function type, the call is a typed function call.
        // TypeScript employs overload resolution in typed function calls in order to support functions
        // with multiple call signatures.
        if (callSignatures.Count == 0)
        {
            if (numConstructSignatures != 0)
            {
                Error(node, Diagnostics.Value_of_type_0_is_not_callable_Did_you_mean_to_include_new, funcTypeString);
            }
            else
            {
                IDiagnosticRelatedInformation? relatedInformation = null;
                if (Core.Length(node.Arguments) == 1)
                {
                    var text = Utilities.GetSourceFileOfNode(node)!.Text!;
                    var lineBreakPos = ScannerUtilities.SkipTrivia(text, node.Expression!.End, /*stopAfterLineBreak*/ true) - 1;
                    // PORT-NOTE: see the member remarks on charCodeAt.
                    if ((uint)lineBreakPos < (uint)text.Length && ScannerUtilities.IsLineBreak(text[lineBreakPos]))
                    {
                        relatedInformation = Utilities.CreateDiagnosticForNode(node.Expression!, Diagnostics.Are_you_missing_a_semicolon);
                    }
                }
                InvocationError(node.Expression!, apparentType, SignatureKind.Call, relatedInformation);
            }
            return ResolveErrorCall(node);
        }
        // When a call to a generic function is an argument to an outer call to a generic function for which
        // inference is in process, we have a choice to make. If the inner call relies on inferences made from
        // its contextual type to its return type, deferring the inner call processing allows the best possible
        // contextual type to accumulate. But if the outer call relies on inferences made from the return type of
        // the inner call, the inner call should be processed early. There's no sure way to know which choice is
        // right (only a full unification algorithm can determine that), so we resort to the following heuristic:
        // If no type arguments are specified in the inner call and at least one call signature is generic and
        // returns a function type, we choose to defer processing. This narrowly permits function composition
        // operators to flow inferences through return types, but otherwise processes calls right away. We
        // use the resolvingSignature singleton to indicate that we deferred processing. This result will be
        // propagated out and eventually turned into silentNeverType (a type that is assignable to anything and
        // from which we never make inferences).
        if ((checkMode & CheckMode.SkipGenericFunctions) != 0 && node.TypeArguments is null && Core.Some(callSignatures, IsGenericFunctionReturningFunction))
        {
            SkippedGenericFunction(node, checkMode);
            return _resolvingSignature;
        }
        // If the function is explicitly marked with `@class`, then it must be constructed.
        if (Core.Some(callSignatures, sig => Utilities.IsInJSFile(sig.Declaration) && Utilities.GetJSDocClassTag((Node)sig.Declaration!) is not null))
        {
            Error(node, Diagnostics.Value_of_type_0_is_not_callable_Did_you_mean_to_include_new, funcTypeString);
            return ResolveErrorCall(node);
        }

        return ResolveCall(node, callSignatures, candidatesOutArray, checkMode, callChainFlags);
    }

    /// <summary>ported from: src/compiler/checker.ts:37116-37118 — <c>isGenericFunctionReturningFunction</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>!!(signature.typeParameters &amp;&amp; …)</c> is the conjunction of the two tests, stated as C#'s
    /// <c>&amp;&amp;</c> over a null test and a predicate call.
    /// </remarks>
    internal bool IsGenericFunctionReturningFunction(ISignature signature)
        => signature.TypeParameters is not null && IsFunctionType(GetReturnTypeOfSignature(signature));

    /// <summary>ported from: src/compiler/checker.ts:37125-37129 — <c>isUntypedFunctionCall</c>.</summary>
    /// <remarks>
    /// TS 1.0 spec: 4.12 — If FuncExpr is of type Any, or of an object type that has no call or construct signatures
    /// but is a subtype of the Function interface, the call is an untyped function call.
    /// PORT-NOTE: <c>numCallSignatures</c>/<c>numConstructSignatures</c> are numbers; the reference's
    /// <c>!numCallSignatures</c> tests are <c>== 0</c> here, and <c>globalFunctionType</c> is
    /// <see cref="_globalFunctionType"/>.
    /// </remarks>
    internal bool IsUntypedFunctionCall(IType funcType, IType apparentFuncType, int numCallSignatures, int numConstructSignatures)
    {
        // We exclude union types because we may have a union of function types that happen to have no common signatures.
        return IsTypeAny(funcType) || IsTypeAny(apparentFuncType) && (funcType.Flags & TypeFlags.TypeParameter) != 0 ||
            numCallSignatures == 0 && numConstructSignatures == 0 && (apparentFuncType.Flags & TypeFlags.Union) == 0 && (GetReducedType(apparentFuncType).Flags & TypeFlags.Never) == 0 && IsTypeAssignableTo(funcType, _globalFunctionType);
    }

    /// <summary>ported from: src/compiler/checker.ts:37131-37204 — <c>resolveNewExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>expressionType.symbol &amp;&amp; getClassLikeDeclarationOfSymbol(expressionType.symbol)</c> is read as
    /// the same null-tested call; <c>getClassLikeDeclarationOfSymbol</c> is a <c>Utilities</c> member the port
    /// declares on <see cref="Checker"/> (see <c>Checker.Support.cs</c>), and its answer is the
    /// <see cref="IClassLikeDeclarationBase"/> face <c>hasSyntacticModifier</c> needs, so the port casts it.
    /// <c>silentNeverType</c>/<c>silentNeverSignature</c>/<c>noImplicitAny</c> are the
    /// <see cref="_silentNeverType"/>/<see cref="_silentNeverSignature"/>/<see cref="_noImplicitAny"/> fields.
    /// </remarks>
    internal ISignature ResolveNewExpression(NewExpression node, List<ISignature>? candidatesOutArray, CheckMode checkMode)
    {
        var expressionType = CheckNonNullExpression(node.Expression!);
        if (ReferenceEquals(expressionType, _silentNeverType))
        {
            return _silentNeverSignature;
        }

        // If expressionType's apparent type(section 3.8.1) is an object type with one or
        // more construct signatures, the expression is processed in the same manner as a
        // function call, but using the construct signatures as the initial set of candidate
        // signatures for overload resolution. The result type of the function call becomes
        // the result type of the operation.
        expressionType = GetApparentType(expressionType);
        if (IsErrorType(expressionType))
        {
            // Another error has already been reported
            return ResolveErrorCall(node);
        }

        // TS 1.0 spec: 4.11
        // If expressionType is of type Any, Args can be any argument
        // list and the result of the operation is of type Any.
        if (IsTypeAny(expressionType))
        {
            if (node.TypeArguments is not null)
            {
                Error(node, Diagnostics.Untyped_function_calls_may_not_accept_type_arguments);
            }
            return ResolveUntypedCall(node);
        }

        // Technically, this signatures list may be incomplete. We are taking the apparent type,
        // but we are not including construct signatures that may have been added to the Object or
        // Function interface, since they have none by default. This is a bit of a leap of faith
        // that the user will not add any.
        var constructSignatures = GetSignaturesOfType(expressionType, SignatureKind.Construct);
        if (constructSignatures.Count != 0)
        {
            if (!IsConstructorAccessible(node, constructSignatures[0]))
            {
                return ResolveErrorCall(node);
            }
            // If the expression is a class of abstract type, or an abstract construct signature,
            // then it cannot be instantiated.
            // In the case of a merged class-module or class-interface declaration,
            // only the class declaration node will have the Abstract flag set.
            if (SomeSignature(constructSignatures, signature => (signature.Flags & SignatureFlags.Abstract) != 0))
            {
                Error(node, Diagnostics.Cannot_create_an_instance_of_an_abstract_class);
                return ResolveErrorCall(node);
            }
            var valueDecl = expressionType.Symbol is not null ? Utilities.GetClassLikeDeclarationOfSymbol(expressionType.Symbol) : null;
            if (valueDecl is not null && Utilities.HasSyntacticModifier(valueDecl, ModifierFlags.Abstract))
            {
                Error(node, Diagnostics.Cannot_create_an_instance_of_an_abstract_class);
                return ResolveErrorCall(node);
            }

            return ResolveCall(node, constructSignatures, candidatesOutArray, checkMode, SignatureFlags.None);
        }

        // If expressionType's apparent type is an object type with no construct signatures but
        // one or more call signatures, the expression is processed as a function call. A compile-time
        // error occurs if the result of the function call is not Void. The type of the result of the
        // operation is Any. It is an error to have a Void this type.
        var callSignatures = GetSignaturesOfType(expressionType, SignatureKind.Call);
        if (callSignatures.Count != 0)
        {
            var signature = ResolveCall(node, callSignatures, candidatesOutArray, checkMode, SignatureFlags.None);
            if (!_noImplicitAny)
            {
                if (signature.Declaration is not null && !IsJSConstructor(signature.Declaration) && !ReferenceEquals(GetReturnTypeOfSignature(signature), _voidType))
                {
                    Error(node, Diagnostics.Only_a_void_function_can_be_called_with_the_new_keyword);
                }
                if (ReferenceEquals(GetThisTypeOfSignature(signature), _voidType))
                {
                    Error(node, Diagnostics.A_function_that_is_called_with_the_new_keyword_cannot_have_a_this_type_that_is_void);
                }
            }
            return signature;
        }

        InvocationError(node.Expression!, expressionType, SignatureKind.Construct);
        return ResolveErrorCall(node);
    }

    /// <summary>ported from: src/compiler/checker.ts:37206-37211 — <c>someSignature</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>Signature | readonly Signature[]</c>, discriminated by
    /// <c>isArray</c>; the port takes <c>object</c> and tests for the <c>IList&lt;ISignature&gt;</c> face (the
    /// treatment <c>Checker.Diagnostics.cs</c> gives <c>DiagnosticMessage | DiagnosticMessageChain</c>).
    /// <c>compositeKind</c>/<c>compositeSignatures</c> are the <c>ISignature</c> slots of the same names.
    /// </remarks>
    internal bool SomeSignature(object signatures, Func<ISignature, bool> f)
    {
        if (signatures is IList<ISignature> signatureList)
        {
            return Core.Some(signatureList, signature => SomeSignature(signature, f));
        }
        var signature = (ISignature)signatures;
        return signature.CompositeKind == TypeFlags.Union ? Core.Some(signature.CompositeSignatures, candidate => f(candidate)) : f(signature);
    }

    /// <summary>ported from: src/compiler/checker.ts:37213-37243 — <c>typeHasProtectedAccessibleBase</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>firstBase as IntersectionType</c> is the <see cref="IUnionOrIntersectionType"/> face that
    /// declares <c>types</c>, and <c>intersectionMember as InterfaceType</c> is the <see cref="IInterfaceType"/> face
    /// this recursive call takes. <c>!mixinFlags[i]</c> is <c>!mixinFlags[i]</c> over the <c>List&lt;bool&gt;</c>
    /// <c>findMixins</c> answers.
    /// </remarks>
    internal bool TypeHasProtectedAccessibleBase(ISymbol target, IInterfaceType type)
    {
        var baseTypes = GetBaseTypes(type);
        if (Core.Length(baseTypes) == 0)
        {
            return false;
        }
        var firstBase = baseTypes[0];
        if ((firstBase.Flags & TypeFlags.Intersection) != 0)
        {
            var types = ((IUnionOrIntersectionType)firstBase).Types!;
            var mixinFlags = FindMixins(types);
            var i = 0;
            foreach (var intersectionMember in types)
            {
                // We want to ignore mixin ctors
                if (!mixinFlags[i])
                {
                    if ((GetObjectFlags(intersectionMember) & (ObjectFlags.Class | ObjectFlags.Interface)) != 0)
                    {
                        if (ReferenceEquals(intersectionMember.Symbol, target))
                        {
                            return true;
                        }
                        if (TypeHasProtectedAccessibleBase(target, (IInterfaceType)intersectionMember))
                        {
                            return true;
                        }
                    }
                }
                i++;
            }
            return false;
        }
        if (ReferenceEquals(firstBase.Symbol, target))
        {
            return true;
        }
        return TypeHasProtectedAccessibleBase(target, (IInterfaceType)firstBase);
    }

    /// <summary>ported from: src/compiler/checker.ts:37245-37280 — <c>isConstructorAccessible</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>declaration.parent.symbol</c> is read through the <see cref="IDeclaration"/> face that declares
    /// <c>symbol</c> (the port's <c>Node</c> does not), and <c>getSelectedEffectiveModifierFlags</c> is a
    /// utilitiesPublic.ts member, i.e. <c>Utilities.GetSelectedEffectiveModifierFlags</c>. <c>signature</c> is
    /// declared <c>ISignature?</c> because the reference tests it for <c>undefined</c> before its first read;
    /// <c>getDeclaredTypeOfSymbol</c> answers the <see cref="IInterfaceType"/> face the reference's
    /// <c>as InterfaceType</c> cast names.
    /// </remarks>
    internal bool IsConstructorAccessible(NewExpression node, ISignature? signature)
    {
        if (signature is null || signature.Declaration is null)
        {
            return true;
        }

        var declaration = signature.Declaration;
        var modifiers = Utilities.GetSelectedEffectiveModifierFlags(declaration, ModifierFlags.NonPublicAccessibilityModifier);

        // (1) Public constructors and (2) constructor functions are always accessible.
        if (modifiers == ModifierFlags.None || declaration.Kind != SyntaxKind.Constructor)
        {
            return true;
        }

        var declaringClassSymbol = ((IDeclaration)declaration.Parent!).Symbol!;
        var declaringClassDeclaration = (IClassLikeDeclarationBase)Utilities.GetClassLikeDeclarationOfSymbol(declaringClassSymbol)!;
        var declaringClass = (IInterfaceType)GetDeclaredTypeOfSymbol(declaringClassSymbol);

        // A private or protected constructor can only be instantiated within its own class (or a subclass, for protected)
        if (!IsNodeWithinClass(node, declaringClassDeclaration))
        {
            var containingClass = Utilities.GetContainingClass(node);
            if (containingClass is not null && (modifiers & ModifierFlags.Protected) != 0)
            {
                var containingType = GetTypeOfNode(containingClass);
                if (TypeHasProtectedAccessibleBase(declaringClassSymbol, (IInterfaceType)containingType))
                {
                    return true;
                }
            }
            var declaringClassString = Diagnostic.DeferArg(() => TypeToString(declaringClass));
            if ((modifiers & ModifierFlags.Private) != 0)
            {
                Error(node, Diagnostics.Constructor_of_class_0_is_private_and_only_accessible_within_the_class_declaration, declaringClassString);
            }
            if ((modifiers & ModifierFlags.Protected) != 0)
            {
                Error(node, Diagnostics.Constructor_of_class_0_is_protected_and_only_accessible_within_the_class_declaration, declaringClassString);
            }
            return false;
        }

        return true;
    }

    /// <summary>ported from: src/compiler/checker.ts:37282-37365 — <c>invocationErrorDetails</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference returns the anonymous <c>{ messageChain, relatedMessage }</c> object; the port states
    /// it as the named tuple the one caller destructures. <c>const { resolvedSymbol } = getNodeLinks(errorTarget)</c>
    /// is the <c>NodeLinks.ResolvedSymbol</c> read, and <c>typeToString</c> is the checker's own
    /// <see cref="TypeToString(IType?, Node?, TypeFormatFlags?)"/> (no enclosing declaration, the reference's call
    /// shape). The union branch reads <c>apparentType.types</c> through the
    /// <see cref="IUnionOrIntersectionType"/> face its <c>TypeFlags.Union</c> test admits.
    /// </remarks>
    internal (IDiagnosticMessageChain MessageChain, DiagnosticMessage? RelatedMessage) InvocationErrorDetails(Node errorTarget, IType apparentType, SignatureKind kind)
    {
        IDiagnosticMessageChain? errorInfo = null;
        var isCall = kind == SignatureKind.Call;
        var awaitedType = GetAwaitedType(apparentType);
        var maybeMissingAwait = awaitedType is not null && GetSignaturesOfType(awaitedType, kind).Count > 0;
        var apparentTypeString = Diagnostic.DeferArg(() => TypeToString(apparentType));
        if ((apparentType.Flags & TypeFlags.Union) != 0)
        {
            var types = ((IUnionOrIntersectionType)apparentType).Types!;
            var hasSignatures = false;
            foreach (var constituent in types)
            {
                var signatures = GetSignaturesOfType(constituent, kind);
                if (signatures.Count != 0)
                {
                    hasSignatures = true;
                    if (errorInfo is not null)
                    {
                        // Bail early if we already have an error, no chance of "No constituent of type is callable"
                        break;
                    }
                }
                else
                {
                    // Error on the first non callable constituent only
                    if (errorInfo is null)
                    {
                        errorInfo = Utilities.ChainDiagnosticMessages(
                            errorInfo,
                            isCall ?
                                Diagnostics.Type_0_has_no_call_signatures :
                                Diagnostics.Type_0_has_no_construct_signatures,
                            Diagnostic.DeferArg(() => TypeToString(constituent)));
                        errorInfo = Utilities.ChainDiagnosticMessages(
                            errorInfo,
                            isCall ?
                                Diagnostics.Not_all_constituents_of_type_0_are_callable :
                                Diagnostics.Not_all_constituents_of_type_0_are_constructable,
                            apparentTypeString);
                    }
                    if (hasSignatures)
                    {
                        // Bail early if we already found a siganture, no chance of "No constituent of type is callable"
                        break;
                    }
                }
            }
            if (!hasSignatures)
            {
                errorInfo = Utilities.ChainDiagnosticMessages(
                    /*details*/ null,
                    isCall ?
                        Diagnostics.No_constituent_of_type_0_is_callable :
                        Diagnostics.No_constituent_of_type_0_is_constructable,
                    apparentTypeString);
            }
            if (errorInfo is null)
            {
                errorInfo = Utilities.ChainDiagnosticMessages(
                    errorInfo,
                    isCall ?
                        Diagnostics.Each_member_of_the_union_type_0_has_signatures_but_none_of_those_signatures_are_compatible_with_each_other :
                        Diagnostics.Each_member_of_the_union_type_0_has_construct_signatures_but_none_of_those_signatures_are_compatible_with_each_other,
                    apparentTypeString);
            }
        }
        else
        {
            errorInfo = Utilities.ChainDiagnosticMessages(
                errorInfo,
                isCall ?
                    Diagnostics.Type_0_has_no_call_signatures :
                    Diagnostics.Type_0_has_no_construct_signatures,
                apparentTypeString);
        }

        var headMessage = isCall ? Diagnostics.This_expression_is_not_callable : Diagnostics.This_expression_is_not_constructable;

        // Diagnose get accessors incorrectly called as functions
        if (NodeTests.IsCallExpression(errorTarget.Parent!) && Core.Length(((CallExpression)errorTarget.Parent!).Arguments) == 0)
        {
            var resolvedSymbol = GetNodeLinks(errorTarget).ResolvedSymbol;
            if (resolvedSymbol is not null && (resolvedSymbol.Flags & SymbolFlags.GetAccessor) != 0)
            {
                headMessage = Diagnostics.This_expression_is_not_callable_because_it_is_a_get_accessor_Did_you_mean_to_use_it_without;
            }
        }

        return (
            Utilities.ChainDiagnosticMessages(errorInfo, headMessage),
            maybeMissingAwait ? Diagnostics.Did_you_forget_to_use_await : null);
    }

    /// <summary>ported from: src/compiler/checker.ts:37367-37380 — <c>invocationError</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the tuple destructuring names the reference's two slots; the diagnostic's <c>start</c>/<c>length</c>
    /// writes are the <see cref="Diagnostic.Start"/>/<see cref="Diagnostic.Length"/> slots.
    /// <c>invocationErrorRecovery</c> receives either the diagnostic or the diagnostic with the related information
    /// already attached, exactly as the reference's <c>addRelatedInfo(...) : diagnostic</c> expression does.
    /// </remarks>
    internal void InvocationError(Node errorTarget, IType apparentType, SignatureKind kind, IDiagnosticRelatedInformation? relatedInformation = null)
    {
        var (messageChain, relatedInfo) = InvocationErrorDetails(errorTarget, apparentType, kind);
        var diagnostic = Utilities.CreateDiagnosticForNodeFromMessageChain(Utilities.GetSourceFileOfNode(errorTarget)!, errorTarget, messageChain);
        if (relatedInfo is not null)
        {
            Utilities.AddRelatedInfo(diagnostic, Utilities.CreateDiagnosticForNode(errorTarget, relatedInfo));
        }
        if (NodeTests.IsCallExpression(errorTarget.Parent!))
        {
            var (_, start, length) = GetDiagnosticSpanForCallNode((CallExpression)errorTarget.Parent!);
            diagnostic.Start = start;
            diagnostic.Length = length;
        }
        _diagnostics.Add(diagnostic);
        InvocationErrorRecovery(apparentType, kind, relatedInformation is not null ? Utilities.AddRelatedInfo(diagnostic, relatedInformation) : diagnostic);
    }

    /// <summary>ported from: src/compiler/checker.ts:37382-37395 — <c>invocationErrorRecovery</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>originatingImport</c> is the <c>SymbolLinks.OriginatingImport</c> slot read through
    /// <see cref="GetSymbolLinks"/>; <c>isImportCall</c> is a utilities member (also declared on the port's
    /// <c>Utilities</c>); the <c>!sigs || !sigs.length</c> guard is <c>Core.Length(sigs) == 0</c> because the port's
    /// <c>GetSignaturesOfType</c> never answers <c>null</c>.
    /// </remarks>
    internal void InvocationErrorRecovery(IType apparentType, SignatureKind kind, Diagnostic diagnostic)
    {
        if (apparentType.Symbol is null)
        {
            return;
        }
        var importNode = GetSymbolLinks(apparentType.Symbol).OriginatingImport;
        // Create a diagnostic on the originating import if possible onto which we can attach a quickfix
        //  An import call expression cannot be rewritten into another form to correct the error - the only solution is to use `.default` at the use-site
        if (importNode is not null && !Utilities.IsImportCall(importNode))
        {
            var sigs = GetSignaturesOfType(GetTypeOfSymbol(GetSymbolLinks(apparentType.Symbol).Target!), kind);
            if (Core.Length(sigs) == 0) return;

            Utilities.AddRelatedInfo(diagnostic, Utilities.CreateDiagnosticForNode(importNode, Diagnostics.Type_originates_at_this_import_A_namespace_style_import_cannot_be_called_or_constructed_and_will_cause_a_failure_at_runtime_Consider_using_a_default_import_or_import_require_here_instead));
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:37397-37425 — <c>resolveTaggedTemplateExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>isArrayLiteralExpression(node.parent)</c> reads the node's parent through the
    /// <c>NodeTests</c> kind test, as the reference does.
    /// </remarks>
    internal ISignature ResolveTaggedTemplateExpression(TaggedTemplateExpression node, List<ISignature>? candidatesOutArray, CheckMode checkMode)
    {
        var tagType = CheckExpression(node.Tag!);
        var apparentType = GetApparentType(tagType);
        if (IsErrorType(apparentType))
        {
            // Another error has already been reported
            return ResolveErrorCall(node);
        }

        var callSignatures = GetSignaturesOfType(apparentType, SignatureKind.Call);
        var numConstructSignatures = GetSignaturesOfType(apparentType, SignatureKind.Construct).Count;

        if (IsUntypedFunctionCall(tagType, apparentType, callSignatures.Count, numConstructSignatures))
        {
            return ResolveUntypedCall(node);
        }

        if (callSignatures.Count == 0)
        {
            if (NodeTests.IsArrayLiteralExpression(node.Parent!))
            {
                var diagnostic = Utilities.CreateDiagnosticForNode(node.Tag!, Diagnostics.It_is_likely_that_you_are_missing_a_comma_to_separate_these_two_template_expressions_They_form_a_tagged_template_expression_which_cannot_be_invoked);
                _diagnostics.Add(diagnostic);
                return ResolveErrorCall(node);
            }

            InvocationError(node.Tag!, apparentType, SignatureKind.Call);
            return ResolveErrorCall(node);
        }

        return ResolveCall(node, callSignatures, candidatesOutArray, checkMode, SignatureFlags.None);
    }

    /// <summary>ported from: src/compiler/checker.ts:37430-37450 — <c>getDiagnosticHeadMessageForDecoratorResolution</c>.</summary>
    /// <remarks>
    /// Gets the localized diagnostic head message to use for errors when resolving a decorator as a call expression.
    /// PORT-NOTE: the <c>switch (node.parent.kind)</c> reads the parent's kind and the reference's <c>Debug.fail()</c>
    /// default is <see cref="Debug.Fail{T}(string?)"/>, which is the never-returning form of the same member.
    /// </remarks>
    internal DiagnosticMessage GetDiagnosticHeadMessageForDecoratorResolution(Decorator node)
    {
        switch (node.Parent!.Kind)
        {
            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.ClassExpression:
                return Diagnostics.Unable_to_resolve_signature_of_class_decorator_when_called_as_an_expression;

            case SyntaxKind.Parameter:
                return Diagnostics.Unable_to_resolve_signature_of_parameter_decorator_when_called_as_an_expression;

            case SyntaxKind.PropertyDeclaration:
                return Diagnostics.Unable_to_resolve_signature_of_property_decorator_when_called_as_an_expression;

            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
                return Diagnostics.Unable_to_resolve_signature_of_method_decorator_when_called_as_an_expression;

            default:
                return Debug.Fail<DiagnosticMessage>();
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:37455-37488 — <c>resolveDecorator</c>.</summary>
    /// <remarks>
    /// Resolves a decorator as if it were a call expression.
    /// PORT-NOTE: <c>invocationErrorDetails</c>' result is the tuple this file declares, so its two slots are read by
    /// name; <c>getTextOfNode</c> is <c>Utilities.GetTextOfNode</c> and <c>isParenthesizedExpression</c> is the
    /// <c>NodeTests</c> kind test.
    /// </remarks>
    internal ISignature ResolveDecorator(Decorator node, List<ISignature>? candidatesOutArray, CheckMode checkMode)
    {
        var funcType = CheckExpression(node.Expression!);
        var apparentType = GetApparentType(funcType);
        if (IsErrorType(apparentType))
        {
            return ResolveErrorCall(node);
        }

        var callSignatures = GetSignaturesOfType(apparentType, SignatureKind.Call);
        var numConstructSignatures = GetSignaturesOfType(apparentType, SignatureKind.Construct).Count;
        if (IsUntypedFunctionCall(funcType, apparentType, callSignatures.Count, numConstructSignatures))
        {
            return ResolveUntypedCall(node);
        }

        if (IsPotentiallyUncalledDecorator(node, callSignatures) && !NodeTests.IsParenthesizedExpression(node.Expression!))
        {
            var nodeStr = Utilities.GetTextOfNode(node.Expression!, /*includeTrivia*/ false);
            Error(node, Diagnostics._0_accepts_too_few_arguments_to_be_used_as_a_decorator_here_Did_you_mean_to_call_it_first_and_write_0, nodeStr);
            return ResolveErrorCall(node);
        }

        var headMessage = GetDiagnosticHeadMessageForDecoratorResolution(node);
        if (callSignatures.Count == 0)
        {
            var errorDetails = InvocationErrorDetails(node.Expression!, apparentType, SignatureKind.Call);
            var messageChain = Utilities.ChainDiagnosticMessages(errorDetails.MessageChain, headMessage);
            var diag = Utilities.CreateDiagnosticForNodeFromMessageChain(Utilities.GetSourceFileOfNode(node.Expression!)!, node.Expression!, messageChain);
            if (errorDetails.RelatedMessage is not null)
            {
                Utilities.AddRelatedInfo(diag, Utilities.CreateDiagnosticForNode(node.Expression!, errorDetails.RelatedMessage));
            }
            _diagnostics.Add(diag);
            InvocationErrorRecovery(apparentType, SignatureKind.Call, diag);
            return ResolveErrorCall(node);
        }

        return ResolveCall(node, callSignatures, candidatesOutArray, checkMode, SignatureFlags.None, headMessage);
    }

    /// <summary>ported from: src/compiler/checker.ts:37490-37510 — <c>createSignatureForJSXIntrinsic</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>nodeBuilder</c>'s two members are the port's own accessors with the same names
    /// (<c>Checker.NodeBuilder.cs</c>); <c>factory</c> is the checker's module-level factory, the port's
    /// <see cref="_factory"/> (<c>NodeFactory.Factory</c>, whose generated <c>create*</c> members take the reference's
    /// argument order). <c>parameterSymbol.links.type = result</c> is written through
    /// <see cref="GetSymbolLinks"/> because the port assigns the symbol's <c>type</c> slot the same way. <c>Symbol</c>
    /// is the port's <c>ISymbol</c> and <c>JsxNames.Element</c> is a checker member outside this band.
    /// </remarks>
    internal ISignature CreateSignatureForJSXIntrinsic(Node node, IType result)
    {
        var ns = GetJsxNamespaceAt(node);
        var exports = ns is not null ? GetExportsOfSymbol(ns) : null;
        // We fake up a SFC signature for each intrinsic, however a more specific per-element signature drawn from the JSX declaration
        // file would probably be preferable.
        var typeSymbol = exports is not null ? GetSymbol(exports, JsxNames.Element, SymbolFlags.Type) : null;
        var returnNode = typeSymbol is not null ? SymbolToEntityName(typeSymbol, SymbolFlags.Type, node, null, null, null) : null;
        var declaration = _factory.CreateFunctionTypeNode(
            /*typeParameters*/ null,
            [_factory.CreateParameterDeclaration(
                /*modifiers*/ null,
                /*dotDotDotToken*/ null,
                "props",
                /*questionToken*/ null,
                TypeToTypeNode(result, node, null, null, null, null, null, null),
                /*initializer*/ null)],
            returnNode is not null ? _factory.CreateTypeReferenceNode(returnNode, /*typeArguments*/ null) : _factory.CreateKeywordTypeNode(SyntaxKind.AnyKeyword));
        var parameterSymbol = CreateSymbol(SymbolFlags.FunctionScopedVariable, "props");
        GetSymbolLinks(parameterSymbol).Type = result;
        return CreateSignature(
            declaration,
            /*typeParameters*/ null,
            /*thisParameter*/ null,
            [parameterSymbol],
            typeSymbol is not null ? GetDeclaredTypeOfSymbol(typeSymbol) : _errorType,
            /*resolvedTypePredicate*/ null,
            1,
            SignatureFlags.None);
    }

    /// <summary>ported from: src/compiler/checker.ts:37512-37542 — <c>getJSXFragmentType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>getNodeLinks(getSourceFileOfNode(node))</c> is a <c>SourceFile</c>'s node links, whose
    /// <c>JsxFragmentType</c> slot the reference tests against <c>undefined</c> before caching into it (the port's slot
    /// is <c>IType?</c>, so the same <c>null</c> test). The reference's <c>diagnostics ? … : undefined</c> guard on
    /// <c>jsxFactoryRefErr</c> is kept (the port's <c>_diagnostics</c> is never null, as in the reference, where
    /// <c>diagnostics</c> is a <c>createDiagnosticCollection()</c> result). <c>compilerOptions</c> is
    /// <see cref="_compilerOptions"/>; <c>resolveName</c>/<c>resolveAlias</c>/<c>getExportsOfSymbol</c>/<c>getSymbol</c>
    /// are checker members and <c>ReactNames.Fragment</c> is declared by M6-5.
    /// </remarks>
    internal IType GetJSXFragmentType(JsxOpeningFragment node)
    {
        // An opening fragment is required in order for `getJsxNamespace` to give the fragment factory
        var sourceFileLinks = GetNodeLinks(Utilities.GetSourceFileOfNode(node)!);
        if (sourceFileLinks.JsxFragmentType is not null) return sourceFileLinks.JsxFragmentType;

        var jsxFragmentFactoryName = GetJsxNamespace(node);

        // #38720/60122, allow null as jsxFragmentFactory
        var shouldResolveFactoryReference = (_compilerOptions.Jsx == JsxEmit.React || _compilerOptions.JsxFragmentFactory is not null) && jsxFragmentFactoryName != "null";
        if (!shouldResolveFactoryReference) return sourceFileLinks.JsxFragmentType = _anyType;

        var shouldModuleRefErr = _compilerOptions.Jsx != JsxEmit.Preserve && _compilerOptions.Jsx != JsxEmit.ReactNative;
        DiagnosticMessage? jsxFactoryRefErr = _diagnostics is not null ? Diagnostics.Using_JSX_fragments_requires_fragment_factory_0_to_be_in_scope_but_it_could_not_be_found : null;
        var jsxFactorySymbol = GetJsxNamespaceContainerForImplicitImport(node) ??
            ResolveName(
                node,
                jsxFragmentFactoryName,
                shouldModuleRefErr ? SymbolFlags.Value : SymbolFlags.Value & ~SymbolFlags.Enum,
                /*nameNotFoundMessage*/ jsxFactoryRefErr,
                /*isUse*/ true,
                /*excludeGlobals*/ false);

        if (jsxFactorySymbol is null) return sourceFileLinks.JsxFragmentType = _errorType;
        if (jsxFactorySymbol.EscapedName == ReactNames.Fragment) return sourceFileLinks.JsxFragmentType = GetTypeOfSymbol(jsxFactorySymbol);

        var resolvedAlias = (jsxFactorySymbol.Flags & SymbolFlags.Alias) == 0 ? jsxFactorySymbol : ResolveAlias(jsxFactorySymbol)!;
        var reactExports = jsxFactorySymbol is not null ? GetExportsOfSymbol(resolvedAlias) : null;
        var typeSymbol = reactExports is not null ? GetSymbol(reactExports, ReactNames.Fragment, SymbolFlags.BlockScopedVariable) : null;
        var type = typeSymbol is not null ? GetTypeOfSymbol(typeSymbol) : null;
        return sourceFileLinks.JsxFragmentType = type is null ? _errorType : type;
    }

    /// <summary>ported from: src/compiler/checker.ts:37544-37584 — <c>resolveJsxOpeningLikeElement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>JsxCallLike</c> is written as <see cref="Node"/> (see the class remarks) and the two
    /// <c>tagName</c>/<c>attributes</c> slots are read through the arm the reference's tests selected. The
    /// <c>createDiagnosticForNodeArray</c> call on <c>node.typeArguments</c> copies the type-argument list into the
    /// <c>NodeArray&lt;Node&gt;</c> the port's <c>Utilities</c> member takes, preserving its range (see
    /// <see cref="GetTypeArgumentArityError"/>). <c>getIntrinsicAttributesTypeFromJsxOpeningLikeElement</c>,
    /// <c>getUninstantiatedJsxSignaturesOfType</c> and <c>isJsxIntrinsicTagName</c> are M6-5's members.
    /// </remarks>
    internal ISignature ResolveJsxOpeningLikeElement(Node node, List<ISignature>? candidatesOutArray, CheckMode checkMode)
    {
        var isJsxOpenFragment = NodeTests.IsJsxOpeningFragment(node);
        var tagName = isJsxOpenFragment ? null : (Node?)((node as JsxOpeningElement)?.TagName ?? (node as JsxSelfClosingElement)?.TagName);
        var jsxAttributes = (Expression?)((node as JsxOpeningElement)?.Attributes ?? (node as JsxSelfClosingElement)?.Attributes);
        IType exprTypes;
        if (!isJsxOpenFragment)
        {
            if (IsJsxIntrinsicTagName(tagName!))
            {
                var result = GetIntrinsicAttributesTypeFromJsxOpeningLikeElement(node);
                var fakeSignature = CreateSignatureForJSXIntrinsic(node, result);
                CheckTypeAssignableToAndOptionallyElaborate(CheckExpressionWithContextualType(jsxAttributes!, GetEffectiveFirstArgumentForJsxSignature(fakeSignature, node), /*inferenceContext*/ null, CheckMode.Normal), result, tagName, jsxAttributes);
                if (Core.Length(TypeArgumentsOfUnion(node)) != 0)
                {
                    var typeArgumentNodes = TypeArgumentsOfUnion(node)!;
                    Core.ForEach(typeArgumentNodes, (ITypeNode typeArgument, int _) =>
                    {
                        CheckSourceElement(typeArgument);
                        return (object?)null;
                    });
                    var diagnosticNodes = new NodeArray<Node> { Pos = typeArgumentNodes.Pos, End = typeArgumentNodes.End };
                    foreach (var typeArgument in typeArgumentNodes)
                    {
                        diagnosticNodes.Add((Node)typeArgument);
                    }
                    _diagnostics.Add(Utilities.CreateDiagnosticForNodeArray(Utilities.GetSourceFileOfNode(node)!, diagnosticNodes, Diagnostics.Expected_0_type_arguments_but_got_1, 0, Core.Length(typeArgumentNodes)));
                }
                return fakeSignature;
            }
            exprTypes = CheckExpression((Expression)tagName!);
        }
        else
        {
            exprTypes = GetJSXFragmentType((JsxOpeningFragment)node);
        }
        var apparentType = GetApparentType(exprTypes);
        if (IsErrorType(apparentType))
        {
            return ResolveErrorCall(node);
        }
        var signatures = GetUninstantiatedJsxSignaturesOfType(exprTypes, node);
        if (IsUntypedFunctionCall(exprTypes, apparentType, signatures.Count, /*constructSignatures*/ 0))
        {
            return ResolveUntypedCall(node);
        }

        if (signatures.Count == 0)
        {
            // We found no signatures at all, which is an error
            if (isJsxOpenFragment)
            {
                Error(node, Diagnostics.JSX_element_type_0_does_not_have_any_construct_or_call_signatures, Utilities.GetTextOfNode(node));
            }
            else
            {
                Error(tagName!, Diagnostics.JSX_element_type_0_does_not_have_any_construct_or_call_signatures, Utilities.GetTextOfNode(tagName!));
            }
            return ResolveErrorCall(node);
        }

        return ResolveCall(node, signatures, candidatesOutArray, checkMode, SignatureFlags.None);
    }

    /// <summary>ported from: src/compiler/checker.ts:37586-37617 — <c>resolveInstanceofExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>node.right</c> is read off the <c>BinaryExpression</c> the resolver's parameter kind
    /// (<c>InstanceofExpression</c> is a <c>BinaryExpression</c> with the <c>instanceof</c> operator). The reference's
    /// comment on the two branches is preserved.
    /// </remarks>
    internal ISignature ResolveInstanceofExpression(Node node, List<ISignature>? candidatesOutArray, CheckMode checkMode)
    {
        // if rightType is an object type with a custom `[Symbol.hasInstance]` method, then it is potentially
        // valid on the right-hand side of the `instanceof` operator. This allows normal `object` types to
        // participate in `instanceof`, as per Step 2 of https://tc39.es/ecma262/#sec-instanceofoperator.
        var right = (Expression)((BinaryExpression)node).Right!;
        var rightType = CheckExpression(right);
        if (!IsTypeAny(rightType))
        {
            var hasInstanceMethodType = GetSymbolHasInstanceMethodOfObjectType(rightType);
            if (hasInstanceMethodType is not null)
            {
                var apparentType = GetApparentType(hasInstanceMethodType);
                if (IsErrorType(apparentType))
                {
                    return ResolveErrorCall(node);
                }

                var callSignatures = GetSignaturesOfType(apparentType, SignatureKind.Call);
                var constructSignatures = GetSignaturesOfType(apparentType, SignatureKind.Construct);
                if (IsUntypedFunctionCall(hasInstanceMethodType, apparentType, callSignatures.Count, constructSignatures.Count))
                {
                    return ResolveUntypedCall(node);
                }

                if (callSignatures.Count != 0)
                {
                    return ResolveCall(node, callSignatures, candidatesOutArray, checkMode, SignatureFlags.None);
                }
            }
            // NOTE: do not raise error if right is unknown as related error was already reported
            else if (!(TypeHasCallOrConstructSignatures(rightType) || IsTypeSubtypeOf(rightType, _globalFunctionType)))
            {
                Error(right, Diagnostics.The_right_hand_side_of_an_instanceof_expression_must_be_either_of_type_any_a_class_function_or_other_type_assignable_to_the_Function_interface_type_or_an_object_type_with_a_Symbol_hasInstance_method);
                return ResolveErrorCall(node);
            }
        }
        // fall back to a default signature
        return _anySignature;
    }

    /// <summary>ported from: src/compiler/checker.ts:37624-37629 — <c>isPotentiallyUncalledDecorator</c>.</summary>
    /// <remarks>
    /// Sometimes, we have a decorator that could accept zero arguments, but is receiving too many arguments as part of
    /// the decorator invocation. In those cases, a user may have meant to *call* the expression before using it as a
    /// decorator.
    /// PORT-NOTE: TS's <c>signatures.length &amp;&amp; every(…)</c> answers the number <c>0</c> for an empty list (falsy);
    /// the port states the same test as <c>Count != 0 &amp;&amp; …</c> and reads <c>minArgumentCount</c>/<c>parameters</c>
    /// off the signature with the reference's slot names.
    /// </remarks>
    internal bool IsPotentiallyUncalledDecorator(Decorator decorator, IList<ISignature> signatures)
        => signatures.Count != 0 && Core.Every(signatures, (ISignature signature, int _) =>
            signature.MinArgumentCount == 0 &&
            !SignatureHasRestParameter(signature) &&
            signature.Parameters!.Count < GetDecoratorArgumentCount(decorator, signature));

    /// <summary>ported from: src/compiler/checker.ts:37631-37649 — <c>resolveSignature</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the union parameter is <see cref="Node"/> and each arm is cast to the resolver's own parameter type
    /// (the same kind tests the reference's <c>switch (node.kind)</c> performs); the reference's
    /// <c>Debug.assertNever</c> is followed by the port's explicit throw because C# needs a return on that path.
    /// </remarks>
    internal ISignature ResolveSignature(Node node, List<ISignature>? candidatesOutArray, CheckMode checkMode)
    {
        switch (node.Kind)
        {
            case SyntaxKind.CallExpression:
                return ResolveCallExpression((CallExpression)node, candidatesOutArray, checkMode);
            case SyntaxKind.NewExpression:
                return ResolveNewExpression((NewExpression)node, candidatesOutArray, checkMode);
            case SyntaxKind.TaggedTemplateExpression:
                return ResolveTaggedTemplateExpression((TaggedTemplateExpression)node, candidatesOutArray, checkMode);
            case SyntaxKind.Decorator:
                return ResolveDecorator((Decorator)node, candidatesOutArray, checkMode);
            case SyntaxKind.JsxOpeningFragment:
            case SyntaxKind.JsxOpeningElement:
            case SyntaxKind.JsxSelfClosingElement:
                return ResolveJsxOpeningLikeElement(node, candidatesOutArray, checkMode);
            case SyntaxKind.BinaryExpression:
                return ResolveInstanceofExpression(node, candidatesOutArray, checkMode);
        }
        Debug.AssertNever(node, "Branch in 'resolveSignature' should be unreachable.");
        throw new InvalidOperationException("unreachable");
    }

    /// <summary>ported from: src/compiler/checker.ts:37658-37690 — <c>getResolvedSignature</c>.</summary>
    /// <remarks>
    /// <para>
    /// Resolve a signature of a given call-like expression.
    /// </para>
    /// <para>
    /// PORT-NOTE: the reference's <c>links.resolvedSignature</c> is a tri-state slot — unset, the
    /// <c>resolvingSignature</c> sentinel, or a real signature — and the port keeps exactly that: the slot is
    /// <c>ISignature?</c>, the sentinel is <see cref="_resolvingSignature"/> and every comparison is by reference, as
    /// the reference's <c>!==</c> is. No extra nullability distinction is needed. The reference's
    /// <c>saveResolutionStart</c>/<c>resolutionStart</c> pair is <see cref="_resolutionStart"/>, and
    /// <c>flowLoopStart === flowLoopCount</c> is the control-flow-analysis test the checker's state block holds
    /// (checker.ts:1539-1540).
    /// </para>
    /// <para>
    /// PORT-NOTE: the reference's public wrapper — <c>getResolvedSignatureWorker</c> (checker.ts:2037-2042), which sets
    /// <c>apparentArgumentCount</c> to the caller's argument count and clears it after the call — lies outside this
    /// band, so this member only reads <see cref="_apparentArgumentCount"/> (through
    /// <see cref="PickLongestCandidateSignature"/>); the wrapper is reported as the one missing piece of the public
    /// <c>ITypeChecker.GetResolvedSignature(node, candidatesOutArray, argumentCount)</c> surface.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>checkMode || CheckMode.Normal</c> is written <c>checkMode ?? CheckMode.Normal</c>; the two agree
    /// because <c>CheckMode.Normal</c> is <c>0</c>, the only falsy value of the enum.
    /// </para>
    /// </remarks>
    internal ISignature GetResolvedSignature(Node node, List<ISignature>? candidatesOutArray = null, CheckMode? checkMode = null)
    {
        using var allocProbe = AllocationProbe.Enter("Checker.GetResolvedSignature");
        var links = GetNodeLinks(node);
        // If getResolvedSignature has already been called, we will have cached the resolvedSignature.
        // However, it is possible that either candidatesOutArray was not passed in the first time,
        // or that a different candidatesOutArray was passed in. Therefore, we need to redo the work
        // to correctly fill the candidatesOutArray.
        var cached = links.ResolvedSignature;
        if (cached is not null && !ReferenceEquals(cached, _resolvingSignature) && candidatesOutArray is null)
        {
            return cached;
        }
        var saveResolutionStart = _resolutionStart;
        if (cached is null)
        {
            // If we haven't already done so, temporarily reset the resolution stack. This allows us to
            // handle "inverted" situations where, for example, an API client asks for the type of a symbol
            // containined in a function call argument whose contextual type depends on the symbol itself
            // through resolution of the containing function call. By resetting the resolution stack we'll
            // retry the symbol type resolution with the resolvingSignature marker in place to suppress
            // the contextual type circularity.
            _resolutionStart = _resolutionTargets.Count;
        }
        links.ResolvedSignature = _resolvingSignature;
        var result = ResolveSignature(node, candidatesOutArray, checkMode ?? CheckMode.Normal);
        _resolutionStart = saveResolutionStart;
        // When CheckMode.SkipGenericFunctions is set we use resolvingSignature to indicate that call
        // resolution should be deferred.
        if (!ReferenceEquals(result, _resolvingSignature))
        {
            // If signature resolution originated in control flow type analysis (for example to compute the
            // assigned type in a flow assignment) we don't cache the result as it may be based on temporary
            // types from the control flow analysis.
            links.ResolvedSignature = _flowLoopStart == _flowLoopCount ? result : cached;
        }
        return result;
    }

    // PORT-NOTE: src/compiler/checker.ts:37692-37715 (<c>isJSConstructor</c>) is called from
    // <see cref="ResolveNewExpression"/> but is NOT declared here: the port already declares it on
    // <c>Checker.Support.cs</c> (whose file header names that range, and whose member answers the same
    // <c>Node?</c> parameter), and a second copy would be a CS0111 on the partial class.

    /// <summary>ported from: src/compiler/checker.ts:37717-37736 — <c>mergeJSSymbols</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>isTransientSymbol(target) ? target : cloneSymbol(target)</c> keeps the reference's guard, and the
    /// result is cast to <see cref="ITransientSymbol"/> because the <c>SymbolLinks.InferredClassSymbol</c> table the
    /// reference's <c>Map</c> corresponds to is keyed by symbol id and valued by that face. <c>exports</c>/<c>members</c>
    /// are the <c>SymbolTable</c> slot, so <c>?.size</c> is <c>is not null &amp;&amp; Count &gt; 0</c> and
    /// <c>createSymbolTable()</c> is <c>Utilities.CreateSymbolTable</c>.
    /// </remarks>
    internal ISymbol? MergeJSSymbols(ISymbol target, ISymbol? source)
    {
        if (source is not null)
        {
            var links = GetSymbolLinks(source);
            if (links.InferredClassSymbol is null || !links.InferredClassSymbol.ContainsKey(Utilities.GetSymbolId(target)))
            {
                var inferred = (ITransientSymbol)(Utilities.IsTransientSymbol(target) ? target : CloneSymbol(target));
                inferred.Exports ??= Utilities.CreateSymbolTable();
                inferred.Members ??= Utilities.CreateSymbolTable();
                inferred.Flags |= source.Flags & SymbolFlags.Class;
                if (source.Exports is not null && source.Exports.Count > 0)
                {
                    MergeSymbolTable(inferred.Exports, source.Exports);
                }
                if (source.Members is not null && source.Members.Count > 0)
                {
                    MergeSymbolTable(inferred.Members, source.Members);
                }
                (links.InferredClassSymbol ??= []).Add(Utilities.GetSymbolId(inferred), inferred);
                return inferred;
            }
            return links.InferredClassSymbol[Utilities.GetSymbolId(target)];
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:37738-37743 — <c>getAssignedClassSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>assignmentSymbol?.exports?.get("prototype")</c> is the <c>SymbolTable</c>'s lookup; the port reads
    /// it with <c>TryGetValue</c> because the table's indexer is only defined for present keys. The <c>Declaration</c>
    /// parameter is the generated <see cref="IDeclaration"/> face and is passed to the two <see cref="Node"/>-taking
    /// members with a cast.
    /// </remarks>
    internal ISymbol? GetAssignedClassSymbol(IDeclaration decl)
    {
        var assignmentSymbol = GetSymbolOfExpando((Node)decl, /*allowDeclaration*/ true);
        ISymbol? prototype = null;
        if (assignmentSymbol?.Exports is not null)
        {
            assignmentSymbol.Exports.TryGetValue("prototype", out prototype);
        }
        var init = prototype?.ValueDeclaration is not null ? GetAssignedJSPrototype((Node)prototype.ValueDeclaration!) : null;
        return init is not null ? GetSymbolOfDeclaration((IDeclaration)init) : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:37745-37789 — <c>getSymbolOfExpando</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>name</c> is TS's <c>Expression | BindingName</c> union, written as <see cref="Node"/> (see the
    /// class remarks) and read back through the casts the reference's narrowing provides. <c>node.parent.operatorToken</c>
    /// is the <c>OperatorToken</c> slot of the <c>BinaryExpression</c> the guard admitted, and the reference's
    /// <c>getExpandoInitializer(node, isPrototypeAccess(name))</c> null test is the port's <c>is null</c> over the same
    /// call.
    /// </remarks>
    internal ISymbol? GetSymbolOfExpando(Node node, bool allowDeclaration)
    {
        if (node.Parent is null)
        {
            return null;
        }
        Node? name = null;
        Node? decl = null;
        if (NodeTests.IsVariableDeclaration(node.Parent!) && ((VariableDeclaration)node.Parent).Initializer == node)
        {
            if (!Utilities.IsInJSFile(node) && !(IsVarConstLike((VariableDeclaration)node.Parent) && Utilities.IsFunctionLikeDeclaration(node)))
            {
                return null;
            }
            name = (Node)((VariableDeclaration)node.Parent).Name!;
            decl = node.Parent;
        }
        else if (NodeTests.IsBinaryExpression(node.Parent))
        {
            var parentNode = (BinaryExpression)node.Parent;
            var parentNodeOperator = parentNode.OperatorToken!.Kind;
            if (parentNodeOperator == SyntaxKind.EqualsToken && (allowDeclaration || parentNode.Right == node))
            {
                name = (Node)parentNode.Left!;
                decl = name;
            }
            else if (parentNodeOperator == SyntaxKind.BarBarToken || parentNodeOperator == SyntaxKind.QuestionQuestionToken)
            {
                if (NodeTests.IsVariableDeclaration(parentNode.Parent!) && ((VariableDeclaration)parentNode.Parent!).Initializer == parentNode)
                {
                    name = (Node)((VariableDeclaration)parentNode.Parent!).Name!;
                    decl = parentNode.Parent;
                }
                else if (NodeTests.IsBinaryExpression(parentNode.Parent!) && ((BinaryExpression)parentNode.Parent!).OperatorToken!.Kind == SyntaxKind.EqualsToken && (allowDeclaration || ((BinaryExpression)parentNode.Parent!).Right == parentNode))
                {
                    name = (Node)((BinaryExpression)parentNode.Parent!).Left!;
                    decl = name;
                }

                if (name is null || !Utilities.IsBindableStaticNameExpression(name) || !Utilities.IsSameEntityName((Expression)name, parentNode.Left!))
                {
                    return null;
                }
            }
        }
        else if (allowDeclaration && NodeTests.IsFunctionDeclaration(node))
        {
            name = ((FunctionDeclaration)node).Name;
            decl = node;
        }

        if (decl is null || name is null || (!allowDeclaration && Utilities.GetExpandoInitializer(node, Utilities.IsPrototypeAccess(name)) is null))
        {
            return null;
        }
        return GetSymbolOfNode(decl);
    }

    /// <summary>ported from: src/compiler/checker.ts:37791-37803 — <c>getAssignedJSPrototype</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's answer is the union <c>false | ObjectLiteralExpression</c> ("no prototype assignment" /
    /// "the object literal"); the port states it as <c>ObjectLiteralExpression?</c>, whose <c>null</c> is the
    /// reference's <c>false</c> and <c>undefined</c> alike — the one caller
    /// (<see cref="GetAssignedClassSymbol"/>) only tests truthiness. The <c>while</c> loop walks
    /// <c>parent</c> through property accesses by reassigning the same slot, as the reference does.
    /// </remarks>
    internal ObjectLiteralExpression? GetAssignedJSPrototype(Node node)
    {
        if (node.Parent is null)
        {
            return null;
        }
        Node? parent = node.Parent;
        while (parent is not null && parent.Kind == SyntaxKind.PropertyAccessExpression)
        {
            parent = parent.Parent;
        }
        if (parent is not null && NodeTests.IsBinaryExpression(parent) && Utilities.IsPrototypeAccess(((BinaryExpression)parent).Left!) && ((BinaryExpression)parent).OperatorToken!.Kind == SyntaxKind.EqualsToken)
        {
            var right = Utilities.GetInitializerOfBinaryExpression((BinaryExpression)parent);
            return NodeTests.IsObjectLiteralExpression(right) ? (ObjectLiteralExpression)right : null;
        }
        return null;
    }

    // ---------------------------------------------------------------------------------------------
    // The union slots this file reads through a kind guard
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:35577, :36578, :36755, :36960 and :37552-:37554 — the <c>node.typeArguments</c> reads.</summary>
    /// <remarks>
    /// PORT-NOTE: this accessor has no reference counterpart. TS reads <c>node.typeArguments</c> off the
    /// <c>CallLikeExpression</c> union, whose object types answer the slot structurally; the port's node classes
    /// declare it on five separate classes and the union has no capability interface (see the class remarks), so the
    /// reference's reads — <c>resolveUntypedCall</c> (:35577), <c>resolveCall</c> (:36578 and the
    /// <c>candidateForTypeArgumentError</c> branch at :36755), <c>pickLongestCandidateSignature</c> (:36960, through
    /// <c>callLikeExpressionMayHaveTypeArguments</c>) and <c>resolveJsxOpeningLikeElement</c> (:37552-:37554) — go
    /// through this switch. It answers the arm's own array (no copy) and <c>null</c> where the reference sees
    /// <c>undefined</c>.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>JsxSelfClosingElement.typeArguments</c> is types.ts:3250's own slot, and every reference read above
    /// reaches it: <c>callLikeExpressionMayHaveTypeArguments</c> (:35569-35571) admits the kind through
    /// <c>isJsxOpeningLikeElement</c> (the guard's type predicate names only <c>JsxOpeningElement</c> — a
    /// declaration-vs-body mismatch in the reference, and the body is what runs), the :36755 cast's
    /// <c>JsxOpeningLikeElement</c> is types.ts:3199-3201's two-element union, and <c>resolveJsxOpeningLikeElement</c>
    /// takes <c>JsxCallLike</c>. So the self-closing arm belongs to the mapping, not to a port-only extra.
    /// </remarks>
    internal static NodeArray<ITypeNode>? TypeArgumentsOfUnion(Node node) => node switch
    {
        CallExpression call => call.TypeArguments,
        NewExpression @new => @new.TypeArguments,
        TaggedTemplateExpression tagged => tagged.TypeArguments,
        JsxOpeningElement jsx => jsx.TypeArguments,
        JsxSelfClosingElement jsx => jsx.TypeArguments,
        _ => null,
    };

    /// <summary>ported from: src/compiler/checker.ts:35585 and :36294 — the <c>node.arguments</c> reads.</summary>
    /// <remarks>
    /// PORT-NOTE: port-only for the same reason as <see cref="TypeArgumentsOfUnion"/>: the reference reads
    /// <c>node.arguments</c> off the union, whose two declaring classes are <c>CallExpression</c> and
    /// <c>NewExpression</c> (a missing list stays <c>null</c>, which is how the reference's <c>!node.arguments</c> test
    /// of the <c>new C</c> case sees <c>undefined</c>).
    /// </remarks>
    internal static NodeArray<Expression>? ArgumentsOfUnion(Node node) => node switch
    {
        CallExpression call => call.Arguments,
        NewExpression @new => @new.Arguments,
        _ => null,
    };
}
