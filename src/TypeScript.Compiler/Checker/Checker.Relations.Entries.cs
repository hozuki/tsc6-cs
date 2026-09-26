namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:2385-2389 (the five relation caches)
// ported from: src/compiler/checker.ts:21271-21901 (isTypeIdenticalTo … checkTypeComparableTo)
//
// The relation *entry points*: the five question-answerers the rest of the checker asks
// (isTypeAssignableTo/isTypeIdenticalTo/isTypeSubtypeOf/…), the three `compareTypes*` adapters that hand
// those questions to `compareSignatures*` as `TypeComparer`s, and the whole elaboration pyramid that turns a
// failed assignability check into a per-property/per-element diagnostic.
//
// The relation *engine* itself (`isTypeRelatedTo`, `checkTypeRelatedTo`, `recursiveTypeRelatedTo`,
// `elaborateNeverIntersection`, …) is a sibling slice: this file calls it and declares nothing of it.
//
// PORT-NOTE: `getRelationCacheSizes` (checker.ts:1616-1621) is deliberately absent — it is one member of the
// `checker` object literal that M6-5 assembles, and it is the only reader of the five caches from outside the
// relation engine. It reads the same five fields declared below.
/// <summary>
/// The relation entry points and the error-elaboration pyramid.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: the reference's <c>Map&lt;string, RelationComparisonResult&gt;</c> is the port's
/// <c>Dictionary&lt;string, RelationComparisonResult&gt;</c> with <c>StringComparer.Ordinal</c>: the keys are the
/// numeric type-id pairs and recursion markers the engine formats itself, so an ordinal comparer is what the
/// reference's string-keyed <c>Map</c> does (docs/architecture.md §5 rule 17).
/// </para>
/// <para>
/// PORT-NOTE: the anonymous <c>{ errors?: Diagnostic[] }</c> object literal the reference passes around as
/// <c>ErrorOutputContainer</c> is <see cref="ErrorOutputContainer"/> — the one concrete implementation of the
/// generated <see cref="IErrorOutputContainer"/> face (types.ts:1177-1180). The reference writes
/// <c>errorOutputContainer || {}</c> wherever it needs a place to catch diagnostics, so the port needs a
/// constructible type, and a diagnostic-collecting container handed in by a caller must be the same type.
/// </para>
/// <para>
/// PORT-NOTE: the elaboration iterators of the reference are generators
/// (<c>IterableIterator&lt;{ errorNode; innerExpression; nameType; errorMessage? }&gt;</c>, checker.ts:21534).
/// The port states the element as the class <see cref="ElaborationElement"/> and the iterator as
/// <c>IEnumerable&lt;ElaborationElement&gt;</c>; the four generators become iterator methods
/// (<c>GenerateJsxAttributes</c>, <c>GenerateJsxChildren</c>, <c>GenerateLimitedTupleElements</c>,
/// <c>GenerateObjectLiteralElements</c>) so laziness is preserved, and
/// <c>elaborateIterableOrArrayLikeTargetElementwise</c>, which drives its iterator manually, keeps the reference's
/// <c>next()</c>-loop shape over an <c>IEnumerator</c>.
/// </para>
/// <para>
/// PORT-NOTE: a sibling slice owns <c>isTypeRelatedTo</c> (checker.ts:22282) and <c>checkTypeRelatedTo</c>
/// (checker.ts:22380), so every call here passes all seven arguments explicitly rather than relying on the
/// optionals the reference declares. The call shape is
/// <c>(source, target, relation, errorNode, headMessage, containingMessageChain, errorOutputContainer)</c>.
/// </para>
/// <para>
/// PORT-NOTE: <c>host.isSourceFileDefaultLibrary</c> (checker.ts:21589, 21597) reads the retained
/// <c>Checker._host</c> slot (Checker/Checker.cs), the port of the <c>host</c> captured by
/// <c>createTypeChecker</c>.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // The five relation caches (checker.ts:2385-2389)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:2385 — <c>subtypeRelation</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares all five as <c>var … = new Map&lt;string, RelationComparisonResult&gt;()</c> and only
    /// ever mutates them in place; the port states them <c>readonly</c> so the engine cannot rebind one by mistake.
    /// </remarks>
    internal readonly Dictionary<string, RelationComparisonResult> _subtypeRelation = new(StringComparer.Ordinal);

    /// <summary>ported from: src/compiler/checker.ts:2386 — <c>strictSubtypeRelation</c>.</summary>
    internal readonly Dictionary<string, RelationComparisonResult> _strictSubtypeRelation = new(StringComparer.Ordinal);

    /// <summary>ported from: src/compiler/checker.ts:2387 — <c>assignableRelation</c>.</summary>
    internal readonly Dictionary<string, RelationComparisonResult> _assignableRelation = new(StringComparer.Ordinal);

    /// <summary>ported from: src/compiler/checker.ts:2388 — <c>comparableRelation</c>.</summary>
    internal readonly Dictionary<string, RelationComparisonResult> _comparableRelation = new(StringComparer.Ordinal);

    /// <summary>ported from: src/compiler/checker.ts:2389 — <c>identityRelation</c>.</summary>
    internal readonly Dictionary<string, RelationComparisonResult> _identityRelation = new(StringComparer.Ordinal);

    // ---------------------------------------------------------------------------------------------
    // The relation queries (checker.ts:21271-21336)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:21271-21273 — <c>isTypeIdenticalTo</c>.</summary>
    internal bool IsTypeIdenticalTo(IType source, IType target)
        => IsTypeRelatedTo(source, target, _identityRelation);

    /// <summary>ported from: src/compiler/checker.ts:21275-21277 — <c>compareTypesIdentical</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference declares this as a two-parameter function and passes it where a
    /// <c>TypeComparer</c> (types.ts:7153, <c>(source, target, reportErrors?) =&gt; Ternary</c>) is expected. TS is
    /// structurally typed, so a two-parameter function satisfies the three-parameter type; C# is not, so the port
    /// declares the third parameter with a default. The parameter is ignored, exactly as TS's arity ignores it.
    /// </remarks>
    internal Ternary CompareTypesIdentical(IType source, IType target, bool reportErrors = false)
        => IsTypeRelatedTo(source, target, _identityRelation) ? Ternary.True : Ternary.False;

    /// <summary>ported from: src/compiler/checker.ts:21279-21281 — <c>compareTypesAssignable</c>.</summary>
    /// <remarks>PORT-NOTE: see <see cref="CompareTypesIdentical"/> for the unused <c>reportErrors</c> parameter.</remarks>
    internal Ternary CompareTypesAssignable(IType source, IType target, bool reportErrors = false)
        => IsTypeRelatedTo(source, target, _assignableRelation) ? Ternary.True : Ternary.False;

    /// <summary>ported from: src/compiler/checker.ts:21283-21285 — <c>compareTypesSubtypeOf</c>.</summary>
    /// <remarks>PORT-NOTE: see <see cref="CompareTypesIdentical"/> for the unused <c>reportErrors</c> parameter.</remarks>
    internal Ternary CompareTypesSubtypeOf(IType source, IType target, bool reportErrors = false)
        => IsTypeRelatedTo(source, target, _subtypeRelation) ? Ternary.True : Ternary.False;

    /// <summary>ported from: src/compiler/checker.ts:21287-21289 — <c>isTypeSubtypeOf</c>.</summary>
    internal bool IsTypeSubtypeOf(IType source, IType target)
        => IsTypeRelatedTo(source, target, _subtypeRelation);

    /// <summary>ported from: src/compiler/checker.ts:21291-21293 — <c>isTypeStrictSubtypeOf</c>.</summary>
    internal bool IsTypeStrictSubtypeOf(IType source, IType target)
        => IsTypeRelatedTo(source, target, _strictSubtypeRelation);

    /// <summary>ported from: src/compiler/checker.ts:21295-21297 — <c>isTypeAssignableTo</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types both parameters as total <c>Type</c>. The port spells them <c>IType?</c> because the
    /// generated <c>ITypeChecker</c> face declares <c>bool IsTypeAssignableTo(IType? source, IType? target)</c>
    /// (Model/NodeInterfaces.cs:3646) and because the landed call sites in the sibling slices pass values whose
    /// static type is nullable; the <c>null</c> guard is unreachable through TS's type system (a relation is never
    /// asked about <c>undefined</c>) and is the port's stand-in for that guarantee, not a new branch.
    /// </remarks>
    internal bool IsTypeAssignableTo(IType? source, IType? target)
        => source is not null && target is not null && IsTypeRelatedTo(source, target, _assignableRelation);

    /// <summary>ported from: src/compiler/checker.ts:21299-21318 — <c>isTypeDerivedFrom</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference reads <c>(source as UnionType).types</c> / <c>(source as IntersectionType).types</c>;
    /// the port narrows through the generated model faces for those classes, which declare the same
    /// <c>types</c> slot on <c>IUnionOrIntersectionType</c> (Model/NodeInterfaces.cs:3767).
    /// </remarks>
    internal bool IsTypeDerivedFrom(IType source, IType target)
        => (source.Flags & TypeFlags.Union) != 0
            ? Core.Every(((IUnionType)source).Types, (t, _) => IsTypeDerivedFrom(t, target))
            : (target.Flags & TypeFlags.Union) != 0
                ? Core.Some(((IUnionType)target).Types, t => IsTypeDerivedFrom(source, t))
                : (source.Flags & TypeFlags.Intersection) != 0
                    ? Core.Some(((IIntersectionType)source).Types, t => IsTypeDerivedFrom(t, target))
                    : (source.Flags & TypeFlags.InstantiableNonPrimitive) != 0
                        ? IsTypeDerivedFrom(GetBaseConstraintOfType(source) ?? _unknownType, target)
                        : IsEmptyAnonymousObjectType(target)
                            ? (source.Flags & (TypeFlags.Object | TypeFlags.NonPrimitive)) != 0
                            : ReferenceEquals(target, _globalObjectType)
                                ? (source.Flags & (TypeFlags.Object | TypeFlags.NonPrimitive)) != 0 && !IsEmptyAnonymousObjectType(source)
                                : ReferenceEquals(target, _globalFunctionType)
                                    ? (source.Flags & TypeFlags.Object) != 0 && IsFunctionObjectType((IObjectType)source)
                                    : HasBaseType(source, GetTargetType(target))
                                        || (IsArrayType(target) && !IsReadonlyArrayType(target) && IsTypeDerivedFrom(source, _globalReadonlyArrayType!));

    /// <summary>ported from: src/compiler/checker.ts:21320-21332 — <c>isTypeComparableTo</c>.</summary>
    /// <remarks>
    /// <para>
    /// This is *not* a bi-directional relationship. If one needs to check both directions for comparability, use a
    /// second call to this function or <see cref="CheckTypeComparableTo"/>.
    /// </para>
    /// <para>
    /// A type S is comparable to a type T if some (but not necessarily all) of the possible values of S are also
    /// possible values of T. It is used to check the types of the left and right sides of the equality and
    /// inequality operators, the types of <c>case</c> clause expressions and their respective <c>switch</c>
    /// expressions, and the type of an expression in a type assertion with the type being asserted.
    /// </para>
    /// </remarks>
    internal bool IsTypeComparableTo(IType source, IType target)
        => IsTypeRelatedTo(source, target, _comparableRelation);

    /// <summary>ported from: src/compiler/checker.ts:21334-21336 — <c>areTypesComparable</c>.</summary>
    internal bool AreTypesComparable(IType type1, IType type2)
        => IsTypeComparableTo(type1, type2) || IsTypeComparableTo(type2, type1);

    // ---------------------------------------------------------------------------------------------
    // The checkers and the elaboration entry points (checker.ts:21338-21369)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:21338-21340 — <c>checkTypeAssignableTo</c>.</summary>
    internal bool CheckTypeAssignableTo(
        IType source,
        IType target,
        Node? errorNode,
        DiagnosticMessage? headMessage = null,
        Func<IDiagnosticMessageChain?>? containingMessageChain = null,
        IErrorOutputContainer? errorOutputObject = null)
        => CheckTypeRelatedTo(source, target, _assignableRelation, errorNode, headMessage, containingMessageChain, errorOutputObject);

    /// <summary>ported from: src/compiler/checker.ts:21342-21348 — <c>checkTypeAssignableToAndOptionallyElaborate</c>.</summary>
    /// <remarks>
    /// Like <see cref="CheckTypeAssignableTo"/>, but if it would issue an error, instead performs structural
    /// comparisons of the types using the given expression node to attempt to issue more specific errors on, for
    /// example, specific object literal properties or tuple members.
    /// </remarks>
    internal bool CheckTypeAssignableToAndOptionallyElaborate(
        IType source,
        IType target,
        Node? errorNode,
        Expression? expr,
        DiagnosticMessage? headMessage = null,
        Func<IDiagnosticMessageChain?>? containingMessageChain = null)
        => CheckTypeRelatedToAndOptionallyElaborate(source, target, _assignableRelation, errorNode, expr, headMessage, containingMessageChain, /*errorOutputContainer*/ null);

    /// <summary>ported from: src/compiler/checker.ts:21350-21365 — <c>checkTypeRelatedToAndOptionallyElaborate</c>.</summary>
    internal bool CheckTypeRelatedToAndOptionallyElaborate(
        IType source,
        IType target,
        Dictionary<string, RelationComparisonResult> relation,
        Node? errorNode,
        Expression? expr,
        DiagnosticMessage? headMessage,
        Func<IDiagnosticMessageChain?>? containingMessageChain,
        IErrorOutputContainer? errorOutputContainer)
    {
        if (IsTypeRelatedTo(source, target, relation))
        {
            return true;
        }

        if (errorNode is null || !ElaborateError(expr, source, target, relation, headMessage, containingMessageChain, errorOutputContainer))
        {
            return CheckTypeRelatedTo(source, target, relation, errorNode, headMessage, containingMessageChain, errorOutputContainer);
        }

        return false;
    }

    // ---------------------------------------------------------------------------------------------
    // Elaboration (checker.ts:21367-21901)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:21367-21369 — <c>isOrHasGenericConditional</c>.</summary>
    internal bool IsOrHasGenericConditional(IType type)
        => (type.Flags & TypeFlags.Conditional) != 0
            || ((type.Flags & TypeFlags.Intersection) != 0 && Core.Some(((IIntersectionType)type).Types, IsOrHasGenericConditional));

    /// <summary>ported from: src/compiler/checker.ts:21371-21413 — <c>elaborateError</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the two <c>AsExpression</c> cases are one C# label with a <c>when</c> clause followed by the two
    /// labels the reference falls through to: <c>case AsExpression when isConstAssertion(node):</c> then
    /// <c>case JsxExpression:</c> / <c>case ParenthesizedExpression:</c>, which is exactly the reference's
    /// <c>case AsExpression: if (!isConstAssertion(node)) break; // fallthrough</c>.
    /// </remarks>
    private bool ElaborateError(
        Expression? node,
        IType source,
        IType target,
        Dictionary<string, RelationComparisonResult> relation,
        DiagnosticMessage? headMessage,
        Func<IDiagnosticMessageChain?>? containingMessageChain,
        IErrorOutputContainer? errorOutputContainer)
    {
        if (node is null || IsOrHasGenericConditional(target))
        {
            return false;
        }

        if (
            !CheckTypeRelatedTo(source, target, relation, /*errorNode*/ null, null, null, null)
            && ElaborateDidYouMeanToCallOrConstruct(node, source, target, relation, headMessage, containingMessageChain, errorOutputContainer)
        )
        {
            return true;
        }

        switch (node.Kind)
        {
            case SyntaxKind.AsExpression when Utilities.IsConstAssertion(node):
            case SyntaxKind.JsxExpression:
            case SyntaxKind.ParenthesizedExpression:
                // PORT-NOTE: the reference's `(node as AsExpression | ParenthesizedExpression | JsxExpression).expression`
                // has no C# union, so the read is a switch over the three classes the case labels license.
                return ElaborateError(node switch
                {
                    AsExpression asExpression => asExpression.Expression,
                    JsxExpression jsxExpression => jsxExpression.Expression,
                    ParenthesizedExpression parenthesizedExpression => parenthesizedExpression.Expression,
                    _ => null,
                }, source, target, relation, headMessage, containingMessageChain, errorOutputContainer);
            case SyntaxKind.BinaryExpression:
                switch (((BinaryExpression)node).OperatorToken!.Kind)
                {
                    case SyntaxKind.EqualsToken:
                    case SyntaxKind.CommaToken:
                        return ElaborateError(((BinaryExpression)node).Right, source, target, relation, headMessage, containingMessageChain, errorOutputContainer);
                }
                break;
            case SyntaxKind.ObjectLiteralExpression:
                return ElaborateObjectLiteral((ObjectLiteralExpression)node, source, target, relation, containingMessageChain, errorOutputContainer);
            case SyntaxKind.ArrayLiteralExpression:
                return ElaborateArrayLiteral((ArrayLiteralExpression)node, source, target, relation, containingMessageChain, errorOutputContainer);
            case SyntaxKind.JsxAttributes:
                return ElaborateJsxComponents((JsxAttributes)node, source, target, relation, containingMessageChain, errorOutputContainer);
            case SyntaxKind.ArrowFunction:
                return ElaborateArrowFunction((ArrowFunction)node, source, target, relation, containingMessageChain, errorOutputContainer);
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:21415-21447 — <c>elaborateDidYouMeanToCallOrConstruct</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>resultObj.errors![length - 1]</c> is <c>resultObj.Errors![^1]</c>, and it is cast to
    /// <see cref="Diagnostic"/> because <c>IErrorOutputContainer.Errors</c> is the generated
    /// <c>List&lt;IDiagnostic&gt;?</c> face while <see cref="Utilities.AddRelatedInfo"/> takes the concrete
    /// <see cref="Diagnostic"/>. Everything this family puts in that list is a <see cref="Diagnostic"/>, so the cast
    /// is the same "the slot holds what we put there" assumption the reference's non-null assertion encodes.
    /// </remarks>
    private bool ElaborateDidYouMeanToCallOrConstruct(
        Expression node,
        IType source,
        IType target,
        Dictionary<string, RelationComparisonResult> relation,
        DiagnosticMessage? headMessage,
        Func<IDiagnosticMessageChain?>? containingMessageChain,
        IErrorOutputContainer? errorOutputContainer)
    {
        var callSignatures = GetSignaturesOfType(source, SignatureKind.Call);
        var constructSignatures = GetSignaturesOfType(source, SignatureKind.Construct);
        foreach (var signatures in new[] { constructSignatures, callSignatures })
        {
            if (
                Core.Some(signatures, s =>
                {
                    var returnType = GetReturnTypeOfSignature(s);
                    return (returnType.Flags & (TypeFlags.Any | TypeFlags.Never)) == 0
                        && CheckTypeRelatedTo(returnType, target, relation, /*errorNode*/ null, null, null, null);
                })
            )
            {
                var resultObj = errorOutputContainer ?? new ErrorOutputContainer();
                CheckTypeAssignableTo(source, target, node, headMessage, containingMessageChain, resultObj);
                var diagnostic = resultObj.Errors![^1];
                Utilities.AddRelatedInfo(
                    (Diagnostic)diagnostic,
                    Utilities.CreateDiagnosticForNode(
                        node,
                        ReferenceEquals(signatures, constructSignatures) ? Diagnostics.Did_you_mean_to_use_new_with_this_expression : Diagnostics.Did_you_mean_to_call_this_expression));
                return true;
            }
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:21449-21512 — <c>elaborateArrowFunction</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>isBlock(node.body)</c> is <c>NodeTests.IsBlock(node.Body!)</c>; <c>isBlock</c> is declared in
    /// utilitiesPublic.ts:2570, which the port keeps on <c>NodeTests</c> (Parser/NodeTests.cs:243) — the home every
    /// other checker slice already calls it from.
    /// </remarks>
    private bool ElaborateArrowFunction(
        ArrowFunction node,
        IType source,
        IType target,
        Dictionary<string, RelationComparisonResult> relation,
        Func<IDiagnosticMessageChain?>? containingMessageChain,
        IErrorOutputContainer? errorOutputContainer)
    {
        // Don't elaborate blocks
        if (NodeTests.IsBlock(node.Body!))
        {
            return false;
        }

        // Or functions with annotated parameter types
        if (Core.Some(node.Parameters, p => Utilities.HasType(p)))
        {
            return false;
        }

        var sourceSig = GetSingleCallSignature(source);
        if (sourceSig is null)
        {
            return false;
        }

        var targetSignatures = GetSignaturesOfType(target, SignatureKind.Call);
        if (Core.Length(targetSignatures) == 0)
        {
            return false;
        }

        var returnExpression = node.Body;
        var sourceReturn = GetReturnTypeOfSignature(sourceSig);
        var targetReturn = GetUnionType(Core.Map(targetSignatures, (s, _) => GetReturnTypeOfSignature(s))!);
        if (!CheckTypeRelatedTo(sourceReturn, targetReturn, relation, /*errorNode*/ null, null, null, null))
        {
            // PORT-NOTE: the reference's `node.body` is the ConciseBody union, and the `isBlock` test above has already
            // excluded the Block arm, so TS's narrowing hands the expression arm to `elaborateError`; C# needs the cast.
            var elaborated = returnExpression is not null && ElaborateError((Expression)returnExpression, sourceReturn, targetReturn, relation, /*headMessage*/ null, containingMessageChain, errorOutputContainer);
            if (elaborated)
            {
                return elaborated;
            }

            var resultObj = errorOutputContainer ?? new ErrorOutputContainer();
            CheckTypeRelatedTo(sourceReturn, targetReturn, relation, returnExpression, /*headMessage*/ null, containingMessageChain, resultObj);
            if (resultObj.Errors is not null)
            {
                if (target.Symbol is not null && Core.Length(target.Symbol.Declarations) != 0)
                {
                    Utilities.AddRelatedInfo(
                        (Diagnostic)resultObj.Errors[^1],
                        Utilities.CreateDiagnosticForNode(
                            (Node)target.Symbol.Declarations![0],
                            Diagnostics.The_expected_type_comes_from_the_return_type_of_this_signature));
                }
                if (
                    (Utilities.GetFunctionFlags(node) & FunctionFlags.Async) == 0
                    // exclude cases where source itself is promisy - this way we don't make a suggestion when relating
                    // an IPromise and a Promise that are slightly different
                    && GetTypeOfPropertyOfType(sourceReturn, "then") is null
                    && CheckTypeRelatedTo(CreatePromiseType(sourceReturn), targetReturn, relation, /*errorNode*/ null, null, null, null)
                )
                {
                    Utilities.AddRelatedInfo(
                        (Diagnostic)resultObj.Errors[^1],
                        Utilities.CreateDiagnosticForNode(
                            node,
                            Diagnostics.Did_you_mean_to_mark_this_function_as_async));
                }
                return true;
            }
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:21514-21525 — <c>getBestMatchIndexedAccessTypeOrUndefined</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS writes <c>getBestMatchingType(source, target as UnionType)</c>; the callee's parameter is
    /// <c>UnionOrIntersectionType</c> (checker.ts:24938), so the port passes the union-or-intersection face the
    /// callee widens to. TS's third parameter defaults to <c>compareTypesAssignable</c>; C# cannot state a method
    /// group as a default value, so the port passes <see cref="CompareTypesAssignable"/> explicitly rather than
    /// depending on the callee's default.
    /// </remarks>
    private IType? GetBestMatchIndexedAccessTypeOrUndefined(IType source, IType target, IType nameType)
    {
        var idx = GetIndexedAccessTypeOrUndefined(target, nameType);
        if (idx is not null)
        {
            return idx;
        }
        if ((target.Flags & TypeFlags.Union) != 0)
        {
            var best = GetBestMatchingType(source, (IUnionOrIntersectionType)target, CompareTypesAssignable);
            if (best is not null)
            {
                return GetIndexedAccessTypeOrUndefined(best, nameType);
            }
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:21527-21532 — <c>checkExpressionForMutableLocationWithContextualType</c>.</summary>
    private IType CheckExpressionForMutableLocationWithContextualType(Expression next, IType sourcePropType)
    {
        PushContextualType(next, sourcePropType, /*isCache*/ false);
        var result = CheckExpressionForMutableLocation(next, CheckMode.Contextual);
        PopContextualType();
        return result;
    }

    /// <summary>
    /// The element of the reference's <c>ElaborationIterator</c> (checker.ts:21534): a node to report an error on,
    /// the expression inside it to recurse into, the name type that selects the target property, and an optional
    /// message to report instead of the generic one.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS states this as an inline object type in a type alias, which C# cannot express; the port states
    /// it as one class. This is the only member of the slice with no direct reference counterpart, because the
    /// reference has no name for it either.
    /// </remarks>
    internal sealed record ElaborationElement(Node ErrorNode, Expression? InnerExpression, IType NameType, DiagnosticMessage? ErrorMessage = null);

    /// <summary>ported from: src/compiler/checker.ts:21535-21614 — <c>elaborateElementwise</c>.</summary>
    /// <remarks>
    /// For every element returned from the iterator, checks that element to issue an error on a property of that
    /// element's type. If that element would issue an error, we first attempt to dive into that element's inner
    /// expression and issue a more specific error by recursing into <c>elaborateError</c>; otherwise, we issue an
    /// error on _every_ element which fails the assignability check.
    /// </remarks>
    private bool ElaborateElementwise(
        IEnumerable<ElaborationElement> iterator,
        IType source,
        IType target,
        Dictionary<string, RelationComparisonResult> relation,
        Func<IDiagnosticMessageChain?>? containingMessageChain,
        IErrorOutputContainer? errorOutputContainer)
    {
        // Assignability failure - check each prop individually, and if that fails, fall back on the bad error span
        var reportedError = false;
        foreach (var value in iterator)
        {
            var prop = value.ErrorNode;
            var next = value.InnerExpression;
            var nameType = value.NameType;
            var errorMessage = value.ErrorMessage;
            var targetPropType = GetBestMatchIndexedAccessTypeOrUndefined(source, target, nameType);
            if (targetPropType is null || (targetPropType.Flags & TypeFlags.IndexedAccess) != 0)
            {
                continue; // Don't elaborate on indexes on generic variables
            }
            var sourcePropType = GetIndexedAccessTypeOrUndefined(source, nameType);
            if (sourcePropType is null)
            {
                continue;
            }

            var propName = GetPropertyNameFromIndex(nameType, /*accessNode*/ null);
            if (!CheckTypeRelatedTo(sourcePropType, targetPropType, relation, /*errorNode*/ null, null, null, null))
            {
                var elaborated = next is not null && ElaborateError(next, sourcePropType, targetPropType, relation, /*headMessage*/ null, containingMessageChain, errorOutputContainer);
                reportedError = true;
                if (!elaborated)
                {
                    // Issue error on the prop itself, since the prop couldn't elaborate the error
                    var resultObj = errorOutputContainer ?? new ErrorOutputContainer();
                    // Use the expression type, if available
                    var specificSource = next is not null ? CheckExpressionForMutableLocationWithContextualType(next, sourcePropType) : sourcePropType;
                    if (_exactOptionalPropertyTypes == true && IsExactOptionalPropertyMismatch(specificSource, targetPropType))
                    {
                        // PORT-NOTE (A″): the message arguments below are deferred thunks (`Diagnostic.DeferArg` / `DiagnosticArgument` in Core/Diagnostic.cs), so the printer-gated `TypeToString` runs on the first read of the message text.
                        var diag = Utilities.CreateDiagnosticForNode(prop, Diagnostics.Type_0_is_not_assignable_to_type_1_with_exactOptionalPropertyTypes_Colon_true_Consider_adding_undefined_to_the_type_of_the_target, Diagnostic.DeferArg(() => TypeToString(specificSource)), Diagnostic.DeferArg(() => TypeToString(targetPropType)));
                        _diagnostics.Add(diag);
                        resultObj.Errors = [diag];
                    }
                    else
                    {
                        // PORT-NOTE: the reference's `!!(propName && …)` is JS truthiness on a `__String`, so an
                        // empty property name takes the false arm; `Js.IsTruthy` keeps that (a plain null test would not).
                        var targetIsOptional = Js.IsTruthy(propName) && ((GetPropertyOfType(target, propName) ?? _unknownSymbol).Flags & SymbolFlags.Optional) != 0;
                        var sourceIsOptional = Js.IsTruthy(propName) && ((GetPropertyOfType(source, propName) ?? _unknownSymbol).Flags & SymbolFlags.Optional) != 0;
                        targetPropType = RemoveMissingType(targetPropType, targetIsOptional);
                        sourcePropType = RemoveMissingType(sourcePropType, targetIsOptional && sourceIsOptional);
                        var result = CheckTypeRelatedTo(specificSource, targetPropType, relation, prop, errorMessage, containingMessageChain, resultObj);
                        if (result && !ReferenceEquals(specificSource, sourcePropType))
                        {
                            // If for whatever reason the expression type doesn't yield an error, make sure we still issue an error on the sourcePropType
                            CheckTypeRelatedTo(sourcePropType, targetPropType, relation, prop, errorMessage, containingMessageChain, resultObj);
                        }
                    }
                    if (resultObj.Errors is not null)
                    {
                        var reportedDiag = resultObj.Errors[^1];
                        var propertyName = Utilities.IsTypeUsableAsPropertyName(nameType) ? Utilities.GetPropertyNameFromType(nameType) : null;
                        var targetProp = propertyName is not null ? GetPropertyOfType(target, propertyName) : null;

                        var issuedElaboration = false;
                        if (targetProp is null)
                        {
                            var indexInfo = GetApplicableIndexInfo(target, nameType);
                            if (indexInfo?.Declaration is not null && !_host.IsSourceFileDefaultLibrary(Utilities.GetSourceFileOfNode(indexInfo.Declaration)))
                            {
                                issuedElaboration = true;
                                Utilities.AddRelatedInfo((Diagnostic)reportedDiag, Utilities.CreateDiagnosticForNode(indexInfo.Declaration, Diagnostics.The_expected_type_comes_from_this_index_signature));
                            }
                        }

                        if (!issuedElaboration && (targetProp is not null && Core.Length(targetProp.Declarations) != 0 || target.Symbol is not null && Core.Length(target.Symbol.Declarations) != 0))
                        {
                            var targetNode = targetProp is not null && Core.Length(targetProp.Declarations) != 0 ? targetProp.Declarations![0] : target.Symbol!.Declarations![0];
                            if (!_host.IsSourceFileDefaultLibrary(Utilities.GetSourceFileOfNode((Node)targetNode)))
                            {
                                Utilities.AddRelatedInfo(
                                    (Diagnostic)reportedDiag,
                                    Utilities.CreateDiagnosticForNode(
                                        (Node)targetNode,
                                        Diagnostics.The_expected_type_comes_from_property_0_which_is_declared_here_on_type_1,
                                        Js.IsTruthy(propertyName) && (nameType.Flags & TypeFlags.UniqueESSymbol) == 0 ? Utilities.UnescapeLeadingUnderscores(propertyName!) : Diagnostic.DeferArg(() => TypeToString(nameType)),
                                        Diagnostic.DeferArg(() => TypeToString(target))));
                            }
                        }
                    }
                }
            }
        }
        return reportedError;
    }

    /// <summary>ported from: src/compiler/checker.ts:21616-21675 — <c>elaborateIterableOrArrayLikeTargetElementwise</c>.</summary>
    /// <remarks>
    /// Assumes <paramref name="target"/> type is assignable to the <c>Iterable</c> type, if <c>Iterable</c> is
    /// defined, or that it's an array or tuple-like type, if <c>Iterable</c> is not defined.
    /// <para>
    /// PORT-NOTE: the reference drives its <c>IterableIterator</c> by hand
    /// (<c>for (let status = iterator.next(); !status.done; status = iterator.next())</c>); the port keeps that
    /// shape over <c>IEnumerator&lt;ElaborationElement&gt;</c> rather than a <c>foreach</c>, so that a generator
    /// method of this slice can be passed in unchanged and only advanced as needed.
    /// </para>
    /// </remarks>
    private bool ElaborateIterableOrArrayLikeTargetElementwise(
        IEnumerable<ElaborationElement> iterator,
        IType source,
        IType target,
        Dictionary<string, RelationComparisonResult> relation,
        Func<IDiagnosticMessageChain?>? containingMessageChain,
        IErrorOutputContainer? errorOutputContainer)
    {
        var tupleOrArrayLikeTargetParts = FilterType(target, IsArrayOrTupleLikeType);
        var nonTupleOrArrayLikeTargetParts = FilterType(target, t => !IsArrayOrTupleLikeType(t));
        // If `nonTupleOrArrayLikeTargetParts` is not `never`, then that should mean `Iterable` is defined.
        var iterationType = !ReferenceEquals(nonTupleOrArrayLikeTargetParts, _neverType)
            ? GetIterationTypeOfIterable(IterationUse.ForOf, IterationTypeKind.Yield, nonTupleOrArrayLikeTargetParts, /*errorNode*/ null)
            : null;

        var reportedError = false;
        using var enumerator = iterator.GetEnumerator();
        while (enumerator.MoveNext())
        {
            var value = enumerator.Current;
            var prop = value.ErrorNode;
            var next = value.InnerExpression;
            var nameType = value.NameType;
            var errorMessage = value.ErrorMessage;
            IType? targetPropType = iterationType;
            var targetIndexedPropType = !ReferenceEquals(tupleOrArrayLikeTargetParts, _neverType) ? GetBestMatchIndexedAccessTypeOrUndefined(source, tupleOrArrayLikeTargetParts, nameType) : null;
            if (targetIndexedPropType is not null && (targetIndexedPropType.Flags & TypeFlags.IndexedAccess) == 0)
            {
                // Don't elaborate on indexes on generic variables
                targetPropType = iterationType is not null ? GetUnionType([iterationType, targetIndexedPropType]) : targetIndexedPropType;
            }
            if (targetPropType is null)
            {
                continue;
            }
            var sourcePropType = GetIndexedAccessTypeOrUndefined(source, nameType);
            if (sourcePropType is null)
            {
                continue;
            }

            var propName = GetPropertyNameFromIndex(nameType, /*accessNode*/ null);
            if (!CheckTypeRelatedTo(sourcePropType, targetPropType, relation, /*errorNode*/ null, null, null, null))
            {
                var elaborated = next is not null && ElaborateError(next, sourcePropType, targetPropType, relation, /*headMessage*/ null, containingMessageChain, errorOutputContainer);
                reportedError = true;
                if (!elaborated)
                {
                    // Issue error on the prop itself, since the prop couldn't elaborate the error
                    var resultObj = errorOutputContainer ?? new ErrorOutputContainer();
                    // Use the expression type, if available
                    var specificSource = next is not null ? CheckExpressionForMutableLocationWithContextualType(next, sourcePropType) : sourcePropType;
                    if (_exactOptionalPropertyTypes == true && IsExactOptionalPropertyMismatch(specificSource, targetPropType))
                    {
                        var diag = Utilities.CreateDiagnosticForNode(prop, Diagnostics.Type_0_is_not_assignable_to_type_1_with_exactOptionalPropertyTypes_Colon_true_Consider_adding_undefined_to_the_type_of_the_target, Diagnostic.DeferArg(() => TypeToString(specificSource)), Diagnostic.DeferArg(() => TypeToString(targetPropType)));
                        _diagnostics.Add(diag);
                        resultObj.Errors = [diag];
                    }
                    else
                    {
                        var targetIsOptional = Js.IsTruthy(propName) && ((GetPropertyOfType(tupleOrArrayLikeTargetParts, propName) ?? _unknownSymbol).Flags & SymbolFlags.Optional) != 0;
                        var sourceIsOptional = Js.IsTruthy(propName) && ((GetPropertyOfType(source, propName) ?? _unknownSymbol).Flags & SymbolFlags.Optional) != 0;
                        targetPropType = RemoveMissingType(targetPropType, targetIsOptional);
                        sourcePropType = RemoveMissingType(sourcePropType, targetIsOptional && sourceIsOptional);
                        var result = CheckTypeRelatedTo(specificSource, targetPropType, relation, prop, errorMessage, containingMessageChain, resultObj);
                        if (result && !ReferenceEquals(specificSource, sourcePropType))
                        {
                            // If for whatever reason the expression type doesn't yield an error, make sure we still issue an error on the sourcePropType
                            CheckTypeRelatedTo(sourcePropType, targetPropType, relation, prop, errorMessage, containingMessageChain, resultObj);
                        }
                    }
                }
            }
        }
        return reportedError;
    }

    /// <summary>ported from: src/compiler/checker.ts:21677-21683 — <c>generateJsxAttributes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS iterates <c>node.properties</c>, whose element type is the
    /// <c>JsxAttribute | JsxSpreadAttribute</c> union. The generated model types the slot as
    /// <c>NodeArray&lt;Node&gt;</c>, so the port tests with <c>NodeTests.IsJsxSpreadAttribute</c> and then casts to
    /// <see cref="JsxAttribute"/> for the two slot reads; the attribute-name text is hoisted into a local because
    /// the reference reads <c>prop.name</c> twice.
    /// </remarks>
    private IEnumerable<ElaborationElement> GenerateJsxAttributes(JsxAttributes node)
    {
        if (Core.Length(node.Properties) == 0)
        {
            yield break;
        }
        foreach (var prop in node.Properties!)
        {
            if (NodeTests.IsJsxSpreadAttribute(prop))
            {
                continue;
            }
            var attribute = (JsxAttribute)prop;
            var nameText = Utilities.GetTextOfJsxAttributeName(attribute.Name!);
            if (IsHyphenatedJsxName(nameText))
            {
                continue;
            }
            yield return new ElaborationElement(attribute.Name!, attribute.Initializer, GetStringLiteralType(nameText));
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:21685-21699 — <c>generateJsxChildren</c>.</summary>
    private IEnumerable<ElaborationElement> GenerateJsxChildren(JsxElement node, Func<DiagnosticMessage> getInvalidTextDiagnostic)
    {
        if (Core.Length(node.Children) == 0)
        {
            yield break;
        }
        var memberOffset = 0;
        for (var i = 0; i < node.Children!.Count; i++)
        {
            var child = node.Children![i];
            var nameType = GetNumberLiteralType(i - memberOffset);
            var elem = GetElaborationElementForJsxChild(child, nameType, getInvalidTextDiagnostic);
            if (elem is not null)
            {
                yield return elem;
            }
            else
            {
                memberOffset++;
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:21701-21720 — <c>getElaborationElementForJsxChild</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>JsxChild</c> union has no C# name, so the parameter is <see cref="Node"/> and
    /// each case casts to the class its kind selected.
    /// <para>
    /// PORT-NOTE: the <c>break</c> in the <c>JsxText</c> case leaves the switch and reaches the end of the reference
    /// function, i.e. it answers <c>undefined</c>; the port writes that as <c>return null</c>. The trailing
    /// <c>return null</c> after the switch is the same fall-out-of-the-switch the reference has, kept so that the
    /// <c>Debug.AssertNever</c> arm is not the only way out of the method.
    /// </para>
    /// </remarks>
    private ElaborationElement? GetElaborationElementForJsxChild(Node child, ILiteralType nameType, Func<DiagnosticMessage> getInvalidTextDiagnostic)
    {
        switch (child.Kind)
        {
            case SyntaxKind.JsxExpression:
                // child is of the type of the expression
                return new ElaborationElement(child, ((JsxExpression)child).Expression, nameType);
            case SyntaxKind.JsxText:
                if (((JsxText)child).ContainsOnlyTriviaWhiteSpaces)
                {
                    break; // Whitespace only jsx text isn't real jsx text
                }
                // child is a string
                return new ElaborationElement(child, null, nameType, getInvalidTextDiagnostic());
            case SyntaxKind.JsxElement:
            case SyntaxKind.JsxSelfClosingElement:
            case SyntaxKind.JsxFragment:
                // child is of type JSX.Element
                return new ElaborationElement(child, (Expression)child, nameType);
            default:
                Debug.AssertNever(child, "Found invalid jsx child");
                return null;
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:21722-21820 — <c>elaborateJsxComponents</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>getInvalidTextualChildDiagnostic</c> is a local function, which is C#'s spelling of the nested
    /// function the reference hoists out of the body; it closes over the same three locals
    /// (<c>invalidTextDiagnostic</c>, <c>node</c>, <c>target</c>).
    /// <para>
    /// PORT-NOTE: <c>{ ...diagnostic, key: "!!ALREADY FORMATTED!!", message: formatMessage(...) }</c> is a fresh
    /// <see cref="DiagnosticMessage"/> built from the table entry's code and category with the formatted text, which
    /// is what the spread does; the message table is a set of frozen instances, so a rewrite has to allocate.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>errorOutputContainer.errors || (errorOutputContainer.errors = [])</c> followed by <c>.push</c> is
    /// the null-coalescing assignment plus <c>Add</c>: an existing empty list is truthy in TS, so only an absent one
    /// is replaced.
    /// </para>
    /// </remarks>
    private bool ElaborateJsxComponents(
        JsxAttributes node,
        IType source,
        IType target,
        Dictionary<string, RelationComparisonResult> relation,
        Func<IDiagnosticMessageChain?>? containingMessageChain,
        IErrorOutputContainer? errorOutputContainer)
    {
        var result = ElaborateElementwise(GenerateJsxAttributes(node), source, target, relation, containingMessageChain, errorOutputContainer);
        DiagnosticMessage? invalidTextDiagnostic = null;
        if (NodeTests.IsJsxOpeningElement(node.Parent!) && NodeTests.IsJsxElement(node.Parent!.Parent!))
        {
            var containingElement = (JsxElement)node.Parent!.Parent!;
            var childPropName = GetJsxElementChildrenPropertyName(GetJsxNamespaceAt(node));
            var childrenPropName = childPropName is null ? "children" : Utilities.UnescapeLeadingUnderscores(childPropName);
            var childrenNameType = GetStringLiteralType(childrenPropName);
            var childrenTargetType = GetIndexedAccessType(target, childrenNameType);
            var validChildren = Utilities.GetSemanticJsxChildren(containingElement.Children!);
            if (Core.Length(validChildren) == 0)
            {
                return result;
            }
            var moreThanOneRealChildren = Core.Length(validChildren) > 1;
            IType arrayLikeTargetParts;
            IType nonArrayLikeTargetParts;
            var iterableType = GetGlobalIterableType(/*reportErrors*/ false);
            if (!ReferenceEquals(iterableType, _emptyGenericType))
            {
                var anyIterable = CreateIterableType(_anyType);
                arrayLikeTargetParts = FilterType(childrenTargetType, t => IsTypeAssignableTo(t, anyIterable));
                nonArrayLikeTargetParts = FilterType(childrenTargetType, t => !IsTypeAssignableTo(t, anyIterable));
            }
            else
            {
                arrayLikeTargetParts = FilterType(childrenTargetType, IsArrayOrTupleLikeType);
                nonArrayLikeTargetParts = FilterType(childrenTargetType, t => !IsArrayOrTupleLikeType(t));
            }
            if (moreThanOneRealChildren)
            {
                if (!ReferenceEquals(arrayLikeTargetParts, _neverType))
                {
                    var realSource = CreateTupleType(CheckJsxChildren(containingElement, CheckMode.Normal));
                    var children = GenerateJsxChildren(containingElement, GetInvalidTextualChildDiagnostic);
                    result = ElaborateIterableOrArrayLikeTargetElementwise(children, realSource, arrayLikeTargetParts, relation, containingMessageChain, errorOutputContainer) || result;
                }
                else if (!IsTypeRelatedTo(GetIndexedAccessType(source, childrenNameType), childrenTargetType, relation))
                {
                    // arity mismatch
                    result = true;
                    var diag = Error(
                        containingElement.OpeningElement!.TagName,
                        Diagnostics.This_JSX_tag_s_0_prop_expects_a_single_child_of_type_1_but_multiple_children_were_provided,
                        childrenPropName,
                        Diagnostic.DeferArg(() => TypeToString(childrenTargetType)));
                    if (errorOutputContainer is not null && errorOutputContainer.SkipLogging == true)
                    {
                        errorOutputContainer.Errors ??= [];
                        errorOutputContainer.Errors.Add(diag);
                    }
                }
            }
            else
            {
                if (!ReferenceEquals(nonArrayLikeTargetParts, _neverType))
                {
                    var child = validChildren[0];
                    var elem = GetElaborationElementForJsxChild(child, childrenNameType, GetInvalidTextualChildDiagnostic);
                    if (elem is not null)
                    {
                        result = ElaborateElementwise([elem], source, target, relation, containingMessageChain, errorOutputContainer) || result;
                    }
                }
                else if (!IsTypeRelatedTo(GetIndexedAccessType(source, childrenNameType), childrenTargetType, relation))
                {
                    // arity mismatch
                    result = true;
                    var diag = Error(
                        containingElement.OpeningElement!.TagName,
                        Diagnostics.This_JSX_tag_s_0_prop_expects_type_1_which_requires_multiple_children_but_only_a_single_child_was_provided,
                        childrenPropName,
                        Diagnostic.DeferArg(() => TypeToString(childrenTargetType)));
                    if (errorOutputContainer is not null && errorOutputContainer.SkipLogging == true)
                    {
                        errorOutputContainer.Errors ??= [];
                        errorOutputContainer.Errors.Add(diag);
                    }
                }
            }
        }

        return result;

        // PORT-NOTE: the reference declares this nested function after its uses (JS hoists function declarations);
        // C# local functions are bound the same way, so the position is kept.
        DiagnosticMessage GetInvalidTextualChildDiagnostic()
        {
            if (invalidTextDiagnostic is null)
            {
                // PORT-NOTE: `node.parent` is narrowed to the JSX opening element by the guard above; the generated
                // model types `Node.parent` as `Node?`, so the read goes through the class the guard selected.
                var tagNameText = Utilities.GetTextOfNode(((JsxOpeningElement)node.Parent!).TagName!);
                var childPropName = GetJsxElementChildrenPropertyName(GetJsxNamespaceAt(node));
                var childrenPropName = childPropName is null ? "children" : Utilities.UnescapeLeadingUnderscores(childPropName);
                var childrenTargetType = GetIndexedAccessType(target, GetStringLiteralType(childrenPropName));
                var diagnostic = Diagnostics._0_components_don_t_accept_text_as_child_elements_Text_in_JSX_has_the_type_string_but_the_expected_type_of_1_is_2;
                invalidTextDiagnostic = new DiagnosticMessage(
                    diagnostic.Code,
                    diagnostic.Category,
                    "!!ALREADY FORMATTED!!",
                    Utilities.FormatMessage(diagnostic, tagNameText, childrenPropName, Diagnostic.DeferArg(() => TypeToString(childrenTargetType))));
            }
            return invalidTextDiagnostic;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:21822-21834 — <c>generateLimitedTupleElements</c>.</summary>
    private IEnumerable<ElaborationElement> GenerateLimitedTupleElements(ArrayLiteralExpression node, IType target)
    {
        var len = Core.Length(node.Elements);
        if (len == 0)
        {
            yield break;
        }
        for (var i = 0; i < len; i++)
        {
            // Skip elements which do not exist in the target - a length error on the tuple overall is likely better than an error on a mismatched index signature
            if (IsTupleLikeType(target) && GetPropertyOfType(target, "" + i) is null)
            {
                continue;
            }
            var elem = node.Elements![i];
            if (NodeTests.IsOmittedExpression(elem))
            {
                continue;
            }
            var nameType = GetNumberLiteralType(i);
            var checkNode = GetEffectiveCheckNode(elem);
            yield return new ElaborationElement(checkNode, checkNode, nameType);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:21836-21857 — <c>elaborateArrayLiteral</c>.</summary>
    private bool ElaborateArrayLiteral(
        ArrayLiteralExpression node,
        IType source,
        IType target,
        Dictionary<string, RelationComparisonResult> relation,
        Func<IDiagnosticMessageChain?>? containingMessageChain,
        IErrorOutputContainer? errorOutputContainer)
    {
        if ((target.Flags & (TypeFlags.Primitive | TypeFlags.Never)) != 0)
        {
            return false;
        }
        if (IsTupleLikeType(source))
        {
            return ElaborateElementwise(GenerateLimitedTupleElements(node, target), source, target, relation, containingMessageChain, errorOutputContainer);
        }

        // recreate a tuple from the elements, if possible
        // Since we're re-doing the expression type, we need to reapply the contextual type
        PushContextualType(node, target, /*isCache*/ false);
        var tupleizedType = CheckArrayLiteral(node, CheckMode.Contextual, /*forceTuple*/ true);
        PopContextualType();
        if (IsTupleLikeType(tupleizedType))
        {
            return ElaborateElementwise(GenerateLimitedTupleElements(node, target), tupleizedType, target, relation, containingMessageChain, errorOutputContainer);
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:21859-21881 — <c>generateObjectLiteralElements</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>ObjectLiteralElementLike</c> union has no C# name; the port tests the spread
    /// case with <c>NodeTests.IsSpreadAssignment</c>, casts to <see cref="IDeclaration"/> for
    /// <c>getSymbolOfDeclaration</c> (the port's member takes the <c>Declaration</c> face), and reads
    /// <c>prop.name</c> through <see cref="INamedDeclaration"/> for the three accessor/method/shorthand cases.
    /// <para>
    /// PORT-NOTE: the reference's <c>if (!type || (type.flags &amp; TypeFlags.Never)) continue</c> is the Never test
    /// alone here, because the port's <c>GetLiteralTypeFromProperty</c> answers <c>neverType</c> for the
    /// <c>undefined</c> arm (Checker/Checker.IndexAccess.cs:271-301).
    /// </para>
    /// </remarks>
    private IEnumerable<ElaborationElement> GenerateObjectLiteralElements(ObjectLiteralExpression node)
    {
        if (Core.Length(node.Properties) == 0)
        {
            yield break;
        }
        foreach (var prop in node.Properties!)
        {
            if (NodeTests.IsSpreadAssignment(prop))
            {
                continue;
            }
            var type = GetLiteralTypeFromProperty(GetSymbolOfDeclaration((IDeclaration)prop), TypeFlags.StringOrNumberLiteralOrUnique);
            if ((type.Flags & TypeFlags.Never) != 0)
            {
                continue;
            }
            switch (prop.Kind)
            {
                case SyntaxKind.SetAccessor:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.ShorthandPropertyAssignment:
                    yield return new ElaborationElement(((INamedDeclaration)prop).Name!, null, type);
                    break;
                case SyntaxKind.PropertyAssignment:
                    yield return new ElaborationElement(
                        ((INamedDeclaration)prop).Name!,
                        ((PropertyAssignment)prop).Initializer,
                        type,
                        Utilities.IsComputedNonLiteralName(((INamedDeclaration)prop).Name!) ? Diagnostics.Type_of_computed_property_s_value_is_0_which_is_not_assignable_to_type_1 : null);
                    break;
                default:
                    Debug.AssertNever(prop);
                    break;
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:21883-21893 — <c>elaborateObjectLiteral</c>.</summary>
    private bool ElaborateObjectLiteral(
        ObjectLiteralExpression node,
        IType source,
        IType target,
        Dictionary<string, RelationComparisonResult> relation,
        Func<IDiagnosticMessageChain?>? containingMessageChain,
        IErrorOutputContainer? errorOutputContainer)
    {
        if ((target.Flags & (TypeFlags.Primitive | TypeFlags.Never)) != 0)
        {
            return false;
        }
        return ElaborateElementwise(GenerateObjectLiteralElements(node), source, target, relation, containingMessageChain, errorOutputContainer);
    }

    /// <summary>ported from: src/compiler/checker.ts:21895-21901 — <c>checkTypeComparableTo</c>.</summary>
    /// <remarks>
    /// This is *not* a bi-directional relationship. If one needs to check both directions for comparability, use a
    /// second call to this function or <see cref="IsTypeComparableTo"/>.
    /// </remarks>
    internal bool CheckTypeComparableTo(
        IType source,
        IType target,
        Node errorNode,
        DiagnosticMessage? headMessage = null,
        Func<IDiagnosticMessageChain?>? containingMessageChain = null)
        => CheckTypeRelatedTo(source, target, _comparableRelation, errorNode, headMessage, containingMessageChain, null);
}

/// <summary>ported from: src/compiler/types.ts:1177-1180 — <c>ErrorOutputContainer</c>.</summary>
/// <remarks>
/// PORT-NOTE: the reference types this as an interface and only ever builds it as an object literal
/// (<c>const resultObj: { errors?: Diagnostic[]; } = errorOutputContainer || {};</c>, checker.ts:21433 and the ten
/// other sites in this slice), so the named type needs one constructible implementation. It is declared beside the
/// elaboration code that constructs it rather than in <c>Model/</c>, which owns only the generated interface.
/// </remarks>
internal sealed class ErrorOutputContainer : IErrorOutputContainer
{
    public List<IDiagnostic>? Errors { get; set; }

    public bool? SkipLogging { get; set; }
}
