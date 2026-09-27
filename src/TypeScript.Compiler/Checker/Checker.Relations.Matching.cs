using System.Globalization;

namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:2390 (enumRelation)
// ported from: src/compiler/checker.ts:21903-22378 (isSignatureAssignableTo … getNormalizedTupleType)
/// <summary>
/// The matching half of the checker's type-relation block: how two signatures, two type predicates and two "simple"
/// types are compared, the cheap structural predicates the relation consults before it recurses into members, and the
/// normalizer it runs each side through.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: the two function-type aliases the reference uses for its comparison callbacks become the nested delegates
/// <see cref="ErrorReporter"/> (checker.ts:21907) and <see cref="TypeComparer"/> (types.ts:7153). C# has no variadic
/// delegate, so the reference's <c>...args: DiagnosticArguments</c> (<c>(string | number)[]</c>, types.ts:7285) is a
/// single <c>params object?[]</c> parameter and <c>DiagnosticArguments</c> itself is not carried over.
/// </para>
/// <para>
/// PORT-NOTE: every JS truthiness test on a <see cref="Ternary"/> is written as <c>!= Ternary.False</c> because the
/// enum's zero is <c>False</c> (types.ts:7145-7150: <c>True = -1</c>, <c>Unknown = 1</c>, <c>Maybe = 3</c>), which is
/// exactly the falsy value JS tests. Where the reference relies on <c>A &amp;&amp; B || C</c> to fall through — the
/// left operand is either a flag test's <c>0</c> or <c>Ternary.False</c> — the port states that two-step test directly
/// so each <c>compareTypes</c> call still runs at most once.
/// </para>
/// <para>
/// PORT-NOTE: this file declares the members no slice had declared yet and calls the rest by the reference's names. The
/// five relation caches (<c>_subtypeRelation</c>, <c>_strictSubtypeRelation</c>, <c>_assignableRelation</c>,
/// <c>_comparableRelation</c>, <c>_identityRelation</c>) and <c>CheckTypeRelatedTo</c> are
/// <c>Checker.Relations.Entries.cs</c>'s; <c>GetErasedSignature</c>, <c>GetCanonicalSignature</c>,
/// <c>InstantiateSignatureInContextOf</c>, <c>GetNonArrayRestType</c>, <c>GetRestOrAnyTypeAtPosition</c>,
/// <c>IsInstantiatedGenericParameter</c>, <c>IsResolvingReturnTypeOfSignature</c>, <c>GetTypeFacts</c>,
/// <c>GetSingleCallSignature</c>, <c>GetSingleBaseForNonAugmentingSubtype</c>, <c>GetRelationKey</c>,
/// <c>IsGenericTupleType</c>, <c>IsHyphenatedJsxName</c>, <c>GetNonNullableType</c>, <c>SignatureToString</c>,
/// <c>TypePredicateToString</c>, <c>TypeToString</c>, <c>GetEnumMemberValue</c> and the signature-parameter helpers
/// (<c>GetParameterCount</c>, <c>GetMinArgumentCount</c>, <c>HasEffectiveRestParameter</c>,
/// <c>SignatureHasRestParameter</c>, <c>GetTypeOfParameter</c>, <c>TryGetTypeAtPosition</c>,
/// <c>GetParameterNameAtPosition</c>, <c>IsArrayType</c>) belong to the slices that own their reference ranges, and
/// <c>Utilities.EscapeString</c>, <c>Utilities.IsIdentifierTypePredicate</c> and
/// <c>Utilities.IsThisTypePredicate</c> to the utilities.ts slice. Each is listed with its owner in the slice report.
/// </para>
/// <para>
/// PORT-NOTE: the reference reaches <c>type.flags</c> on every type, <c>source.symbol</c> on an enum, and a
/// <c>ResolvedType</c>/<c>UnionType</c>/<c>TypeReference</c>/<c>SubstitutionType</c> view behind a flags test with an
/// <c>as</c> cast that its structural typing never checks. The port keeps the same flags tests as the guard and states
/// the cast in C# terms — the generated interfaces where only slots are read, the object family's class
/// (<see cref="UnionOrIntersectionType"/>, <see cref="ResolvedType"/>) where the reference cast to a class.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    /// <summary>ported from: src/compiler/checker.ts:2390 — <c>enumRelation</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference declares it among <c>createTypeChecker</c>'s locals, which the port keeps as
    /// <c>_</c>-prefixed fields (<c>Checker.Types.cs</c>); this one is declared here because
    /// <see cref="IsEnumTypeRelatedTo"/> is its only reader in the whole checker. String keys are compared ordinally,
    /// which is what the JS <c>Map</c> does.
    /// </remarks>
    internal readonly Dictionary<string, RelationComparisonResult> _enumRelation = new(StringComparer.Ordinal);

    /// <summary>ported from: src/compiler/checker.ts:21907 — <c>ErrorReporter</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>ErrorReporter = (message: DiagnosticMessage, ...args: DiagnosticArguments) =&gt; void</c>
    /// becomes this delegate; the spread argument list is the one array parameter, so a method group reporting errors
    /// (the reference's nested <c>reportError</c>, checker.ts:22601) converts to it as
    /// <c>void ReportError(DiagnosticMessage message, params object?[] args)</c>.
    /// </remarks>
    internal delegate void ErrorReporter(DiagnosticMessage message, params object?[] args);

    /// <summary>ported from: src/compiler/types.ts:7153 — <c>TypeComparer</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>TypeComparer = (s: Type, t: Type, reportErrors?: boolean) =&gt; Ternary</c> is a types.ts
    /// alias the checker leans on for every "compare these two types" callback. C# delegates are nominal, so the alias
    /// needs one shared name; it lives here because <see cref="CompareSignaturesRelated"/> is its heaviest user. The
    /// members passed as one (<c>CompareTypesAssignable</c> and friends, <c>Checker.Relations.Entries.cs</c>) take the
    /// optional third parameter so their method groups convert to this signature.
    /// </remarks>
    internal delegate Ternary TypeComparer(IType source, IType target, bool reportErrors = false);

    // ---------------------------------------------------------------------------------------------
    // Signature and type-predicate comparison (checker.ts:21903-22111)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:21903-21905 — <c>isSignatureAssignableTo</c>.</summary>
    internal bool IsSignatureAssignableTo(ISignature source, ISignature target, bool ignoreReturnTypes)
        => CompareSignaturesRelated(
            source,
            target,
            ignoreReturnTypes ? SignatureCheckMode.IgnoreReturnTypes : SignatureCheckMode.None,
            /*reportErrors*/ false,
            /*errorReporter*/ null,
            /*incompatibleErrorReporter*/ null,
            CompareTypesAssignable,
            /*reportUnreliableMarkers*/ null) != Ternary.False;

    /// <summary>ported from: src/compiler/checker.ts:21909-21919 — <c>isTopSignature</c>.</summary>
    /// <remarks>
    /// Returns true if <c>s</c> is <c>(...args: A) =&gt; R</c> where <c>A</c> is <c>any</c>, <c>any[]</c>,
    /// <c>never</c>, or <c>never[]</c>, and <c>R</c> is <c>any</c> or <c>unknown</c>.
    /// <para>
    /// PORT-NOTE: <c>isArrayType(paramType)</c> is a type guard upstream, so the <c>getTypeArguments(paramType)</c> that
    /// follows it needs no cast there; the port states the same narrowing with the <c>ITypeReference</c> cast the
    /// reference's guard implied.
    /// </para>
    /// </remarks>
    internal bool IsTopSignature(ISignature s)
    {
        if (s.TypeParameters is null
            && (s.ThisParameter is null || IsTypeAny(GetTypeOfParameter(s.ThisParameter!)))
            && Core.Length(s.Parameters) == 1
            && SignatureHasRestParameter(s))
        {
            var paramType = GetTypeOfParameter(s.Parameters![0]);
            var restType = IsArrayType(paramType) ? GetTypeArguments((ITypeReference)paramType)[0] : paramType;
            return (restType.Flags & (TypeFlags.Any | TypeFlags.Never)) != 0
                && (GetReturnTypeOfSignature(s).Flags & TypeFlags.AnyOrUnknown) != 0;
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:21921-22059 — <c>compareSignaturesRelated</c>.</summary>
    /// <remarks>
    /// See <c>structuredTypeRelatedTo</c>'s signature half (<c>Checker.Relations.Structured.cs</c>) and
    /// <c>CompareSignaturesIdentical</c> (<c>Checker.Relations.Identical.cs</c>).
    /// <para>
    /// PORT-NOTE: TS's <c>errorReporter!(…)</c> reports through a possibly-absent callback; the port keeps the
    /// null-forgiving invocation (<c>errorReporter!(…)</c>) exactly where the reference guards the call with
    /// <c>reportErrors</c> or an <c>errorReporter</c> test, so the thrown-on-null contract is the reference's.
    /// </para>
    /// </remarks>
    internal Ternary CompareSignaturesRelated(
        ISignature source,
        ISignature target,
        SignatureCheckMode checkMode,
        bool reportErrors,
        ErrorReporter? errorReporter,
        Action<IType, IType>? incompatibleErrorReporter,
        TypeComparer compareTypes,
        TypeMapper? reportUnreliableMarkers)
    {
        // TODO (drosen): De-duplicate code between related functions.
        if (ReferenceEquals(source, target))
        {
            return Ternary.True;
        }

        if (!((checkMode & SignatureCheckMode.StrictTopSignature) != 0 && IsTopSignature(source)) && IsTopSignature(target))
        {
            return Ternary.True;
        }
        if ((checkMode & SignatureCheckMode.StrictTopSignature) != 0 && IsTopSignature(source) && !IsTopSignature(target))
        {
            return Ternary.False;
        }

        var targetCount = GetParameterCount(target);
        var sourceHasMoreParameters = !HasEffectiveRestParameter(target) &&
            ((checkMode & SignatureCheckMode.StrictArity) != 0
                ? HasEffectiveRestParameter(source) || GetParameterCount(source) > targetCount
                : GetMinArgumentCount(source) > targetCount);
        if (sourceHasMoreParameters)
        {
            if (reportErrors && (checkMode & SignatureCheckMode.StrictArity) == 0)
            {
                // the second condition should be redundant, because there is no error reporting when comparing signatures by strict arity
                // since it is only done for subtype reduction
                errorReporter!(Diagnostics.Target_signature_provides_too_few_arguments_Expected_0_or_more_but_got_1, GetMinArgumentCount(source), targetCount);
            }
            return Ternary.False;
        }

        if (source.TypeParameters is not null && !ReferenceEquals(source.TypeParameters, target.TypeParameters))
        {
            target = GetCanonicalSignature(target);
            source = InstantiateSignatureInContextOf(source, target, /*inferenceContext*/ null, compareTypes);
        }

        var sourceCount = GetParameterCount(source);
        var sourceRestType = GetNonArrayRestType(source);
        var targetRestType = GetNonArrayRestType(target);
        if (sourceRestType is not null || targetRestType is not null)
        {
            // PORT-NOTE: TS's `void instantiateType(...)` discards the instantiation; the port keeps the call for its
            // side effects (resolving the rest type's instantiation) and discards the result with a discard.
            _ = InstantiateType(sourceRestType ?? targetRestType, reportUnreliableMarkers);
        }

        var kind = target.Declaration is not null ? target.Declaration.Kind : SyntaxKind.Unknown;
        var strictVariance = (checkMode & SignatureCheckMode.Callback) == 0 && _strictFunctionTypes
            && kind != SyntaxKind.MethodDeclaration
            && kind != SyntaxKind.MethodSignature
            && kind != SyntaxKind.Constructor;
        var result = Ternary.True;

        var sourceThisType = GetThisTypeOfSignature(source);
        if (sourceThisType is not null && !ReferenceEquals(sourceThisType, _voidType))
        {
            var targetThisType = GetThisTypeOfSignature(target);
            if (targetThisType is not null)
            {
                // void sources are assignable to anything.
                var bivariantThis = !strictVariance
                    ? compareTypes(sourceThisType, targetThisType, /*reportErrors*/ false)
                    : Ternary.False;
                var related = bivariantThis != Ternary.False ? bivariantThis : compareTypes(targetThisType, sourceThisType, reportErrors);
                if (related == Ternary.False)
                {
                    if (reportErrors)
                    {
                        errorReporter!(Diagnostics.The_this_types_of_each_signature_are_incompatible);
                    }
                    return Ternary.False;
                }
                result &= related;
            }
        }

        var paramCount = sourceRestType is not null || targetRestType is not null ? Math.Min(sourceCount, targetCount) : Math.Max(sourceCount, targetCount);
        var restIndex = sourceRestType is not null || targetRestType is not null ? paramCount - 1 : -1;

        for (var i = 0; i < paramCount; i++)
        {
            var sourceType = i == restIndex ? GetRestOrAnyTypeAtPosition(source, i) : TryGetTypeAtPosition(source, i);
            var targetType = i == restIndex ? GetRestOrAnyTypeAtPosition(target, i) : TryGetTypeAtPosition(target, i);
            if (sourceType is not null && targetType is not null && (!ReferenceEquals(sourceType, targetType) || (checkMode & SignatureCheckMode.StrictArity) != 0))
            {
                // In order to ensure that any generic type Foo<T> is at least co-variant with respect to T no matter
                // how Foo uses T, we need to relate parameters bi-variantly (given that parameters are input positions,
                // they naturally relate only contra-variantly). However, if the source and target parameters both have
                // function types with a single call signature, we know we are relating two callback parameters. In
                // that case it is sufficient to only relate the parameters of the signatures co-variantly because,
                // similar to return values, callback parameters are output positions. This means that a Promise<T>,
                // where T is used only in callback parameter positions, will be co-variant (as opposed to bi-variant)
                // with respect to T.
                var sourceSig = (checkMode & SignatureCheckMode.Callback) != 0 || IsInstantiatedGenericParameter(source, i)
                    ? null
                    : GetSingleCallSignature(GetNonNullableType(sourceType));
                var targetSig = (checkMode & SignatureCheckMode.Callback) != 0 || IsInstantiatedGenericParameter(target, i)
                    ? null
                    : GetSingleCallSignature(GetNonNullableType(targetType));
                var callbacks = sourceSig is not null && targetSig is not null
                    && GetTypePredicateOfSignature(sourceSig) is null
                    && GetTypePredicateOfSignature(targetSig) is null
                    && GetTypeFacts(sourceType, TypeFacts.IsUndefinedOrNull) == GetTypeFacts(targetType, TypeFacts.IsUndefinedOrNull);
                Ternary related;
                if (callbacks)
                {
                    related = CompareSignaturesRelated(
                        targetSig!,
                        sourceSig!,
                        (checkMode & SignatureCheckMode.StrictArity) | (strictVariance ? SignatureCheckMode.StrictCallback : SignatureCheckMode.BivariantCallback),
                        reportErrors,
                        errorReporter,
                        incompatibleErrorReporter,
                        compareTypes,
                        reportUnreliableMarkers);
                }
                else
                {
                    var bivariantParams = (checkMode & SignatureCheckMode.Callback) == 0 && !strictVariance
                        ? compareTypes(sourceType, targetType, /*reportErrors*/ false)
                        : Ternary.False;
                    related = bivariantParams != Ternary.False ? bivariantParams : compareTypes(targetType, sourceType, reportErrors);
                }

                // With strict arity, (x: number | undefined) => void is a subtype of (x?: number | undefined) => void
                if (related != Ternary.False
                    && (checkMode & SignatureCheckMode.StrictArity) != 0
                    && i >= GetMinArgumentCount(source) && i < GetMinArgumentCount(target)
                    && compareTypes(sourceType, targetType, /*reportErrors*/ false) != Ternary.False)
                {
                    related = Ternary.False;
                }
                if (related == Ternary.False)
                {
                    if (reportErrors)
                    {
                        errorReporter!(
                            Diagnostics.Types_of_parameters_0_and_1_are_incompatible,
                            Utilities.UnescapeLeadingUnderscores(GetParameterNameAtPosition(source, i)!),
                            Utilities.UnescapeLeadingUnderscores(GetParameterNameAtPosition(target, i)!));
                    }
                    return Ternary.False;
                }
                result &= related;
            }
        }

        if ((checkMode & SignatureCheckMode.IgnoreReturnTypes) == 0)
        {
            // If a signature resolution is already in-flight, skip issuing a circularity error
            // here and just use the `any` type directly
            // PORT-NOTE: TS reads `target.declaration` twice and relies on the `target.declaration &&` test to keep it
            // non-null; C# loses that state across the IsJSConstructor call, so the second read states the same
            // guarantee with `!` on the node.
            var targetReturnType = IsResolvingReturnTypeOfSignature(target) ? _anyType
                : target.Declaration is not null && IsJSConstructor(target.Declaration)
                    ? GetDeclaredTypeOfClassOrInterface(GetMergedSymbol(((IDeclaration)target.Declaration!).Symbol!)!)
                    : GetReturnTypeOfSignature(target);
            if (ReferenceEquals(targetReturnType, _voidType) || ReferenceEquals(targetReturnType, _anyType))
            {
                return result;
            }
            var sourceReturnType = IsResolvingReturnTypeOfSignature(source) ? _anyType
                : source.Declaration is not null && IsJSConstructor(source.Declaration)
                    ? GetDeclaredTypeOfClassOrInterface(GetMergedSymbol(((IDeclaration)source.Declaration!).Symbol!)!)
                    : GetReturnTypeOfSignature(source);

            // The following block preserves behavior forbidding boolean returning functions from being assignable to type guard returning functions
            var targetTypePredicate = GetTypePredicateOfSignature(target);
            if (targetTypePredicate is not null)
            {
                var sourceTypePredicate = GetTypePredicateOfSignature(source);
                if (sourceTypePredicate is not null)
                {
                    result &= CompareTypePredicateRelatedTo(sourceTypePredicate, targetTypePredicate, reportErrors, errorReporter, compareTypes);
                }
                else if (Utilities.IsIdentifierTypePredicate(targetTypePredicate) || Utilities.IsThisTypePredicate(targetTypePredicate))
                {
                    if (reportErrors)
                    {
                        // PORT-NOTE (A″): printer-gated stringifier arguments are wrapped in Diagnostic.DeferArg (Core/Diagnostic.cs, DiagnosticArgument) so the M7 gate is reached only by a reader that asks for the message text.
                        errorReporter!(Diagnostics.Signature_0_must_be_a_type_predicate, Diagnostic.DeferArg(() => SignatureToString(source, /*enclosingDeclaration*/ null, /*flags*/ null, /*kind*/ null)));
                    }
                    return Ternary.False;
                }
            }
            else
            {
                // When relating callback signatures, we still need to relate return types bi-variantly as otherwise
                // the containing type wouldn't be co-variant. For example, interface Foo<T> { add(cb: () => T): void }
                // wouldn't be co-variant for T without this rule.
                var bivariantReturn = (checkMode & SignatureCheckMode.BivariantCallback) != 0
                    ? compareTypes(targetReturnType, sourceReturnType, /*reportErrors*/ false)
                    : Ternary.False;
                result &= bivariantReturn != Ternary.False ? bivariantReturn : compareTypes(sourceReturnType, targetReturnType, reportErrors);
                if (result == Ternary.False && reportErrors && incompatibleErrorReporter is not null)
                {
                    incompatibleErrorReporter(sourceReturnType, targetReturnType);
                }
            }
        }

        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:22061-22093 — <c>compareTypePredicateRelatedTo</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference reads <c>source.parameterName</c>/<c>parameterIndex</c> off the <c>TypePredicate</c>
    /// union after testing <c>kind</c>; the port narrows to the <c>Identifier</c> face the kind selects, as every other
    /// reader of the four predicate faces does (Model/TypePredicate.cs).
    /// </remarks>
    internal Ternary CompareTypePredicateRelatedTo(
        ITypePredicateBase source,
        ITypePredicateBase target,
        bool reportErrors,
        ErrorReporter? errorReporter,
        TypeComparer compareTypes)
    {
        if (source.Kind != target.Kind)
        {
            if (reportErrors)
            {
                errorReporter!(Diagnostics.A_this_based_type_guard_is_not_compatible_with_a_parameter_based_type_guard);
                errorReporter!(
                    Diagnostics.Type_predicate_0_is_not_assignable_to_1,
                    Diagnostic.DeferArg(() => TypePredicateToString(source, /*enclosingDeclaration*/ null, /*flags*/ null)),
                    Diagnostic.DeferArg(() => TypePredicateToString(target, /*enclosingDeclaration*/ null, /*flags*/ null)));
            }
            return Ternary.False;
        }

        if (source.Kind is TypePredicateKind.Identifier or TypePredicateKind.AssertsIdentifier)
        {
            if (((IIdentifierTypePredicate)source).ParameterIndex != ((IIdentifierTypePredicate)target).ParameterIndex)
            {
                if (reportErrors)
                {
                    errorReporter!(
                        Diagnostics.Parameter_0_is_not_in_the_same_position_as_parameter_1,
                        ((IIdentifierTypePredicate)source).ParameterName,
                        ((IIdentifierTypePredicate)target).ParameterName);
                    errorReporter!(
                        Diagnostics.Type_predicate_0_is_not_assignable_to_1,
                        Diagnostic.DeferArg(() => TypePredicateToString(source, /*enclosingDeclaration*/ null, /*flags*/ null)),
                        Diagnostic.DeferArg(() => TypePredicateToString(target, /*enclosingDeclaration*/ null, /*flags*/ null)));
                }
                return Ternary.False;
            }
        }

        var related = ReferenceEquals(source.Type, target.Type) ? Ternary.True
            : source.Type is not null && target.Type is not null ? compareTypes(source.Type, target.Type, reportErrors)
            : Ternary.False;
        if (related == Ternary.False && reportErrors)
        {
            errorReporter!(
                Diagnostics.Type_predicate_0_is_not_assignable_to_1,
                Diagnostic.DeferArg(() => TypePredicateToString(source, /*enclosingDeclaration*/ null, /*flags*/ null)),
                Diagnostic.DeferArg(() => TypePredicateToString(target, /*enclosingDeclaration*/ null, /*flags*/ null)));
        }
        return related;
    }

    /// <summary>ported from: src/compiler/checker.ts:22095-22111 — <c>isImplementationCompatibleWithOverload</c>.</summary>
    internal bool IsImplementationCompatibleWithOverload(ISignature implementation, ISignature overload)
    {
        var erasedSource = GetErasedSignature(implementation);
        var erasedTarget = GetErasedSignature(overload);

        // First see if the return types are compatible in either direction.
        var sourceReturnType = GetReturnTypeOfSignature(erasedSource);
        var targetReturnType = GetReturnTypeOfSignature(erasedTarget);
        if (ReferenceEquals(targetReturnType, _voidType)
            || IsTypeRelatedTo(targetReturnType, sourceReturnType, _assignableRelation)
            || IsTypeRelatedTo(sourceReturnType, targetReturnType, _assignableRelation))
        {
            return IsSignatureAssignableTo(erasedSource, erasedTarget, /*ignoreReturnTypes*/ true);
        }

        return false;
    }

    // ---------------------------------------------------------------------------------------------
    // The structural predicates the relation consults (checker.ts:22113-22280)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:22113-22119 — <c>isEmptyResolvedType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference reads the four resolved slots through <c>ResolvedType</c>; the port takes the family
    /// base <see cref="UnionOrIntersectionType"/> instead, because that is what its call shapes hand it —
    /// <c>resolveStructuredTypeMembers</c> returns that base (<c>Checker.Members.cs:856</c>) and the landed call site
    /// <c>Checker.Unions.cs:215</c> passes it straight through, while this file's own
    /// <c>isEmptyObjectType</c>/<c>isEmptyAnonymousObjectType</c> pass the <see cref="ResolvedType"/> class (a
    /// <see cref="UnionOrIntersectionType"/>, so the conversion is an upcast). Every slot the reference reads is
    /// declared on that base, an absent list reads <c>null</c>, and <see cref="Core.Length{T}"/> gives the reference's
    /// <c>length === 0</c> for both the absent and the empty array.
    /// </remarks>
    internal bool IsEmptyResolvedType(UnionOrIntersectionType t)
        => !ReferenceEquals(t, _anyFunctionType)
            && Core.Length(t.Properties) == 0
            && Core.Length(t.CallSignatures) == 0
            && Core.Length(t.ConstructSignatures) == 0
            && Core.Length(t.IndexInfos) == 0;

    /// <summary>ported from: src/compiler/checker.ts:22121-22127 — <c>isEmptyObjectType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>resolveStructuredTypeMembers</c> (checker.ts:15022) takes the reference's
    /// <c>StructuredType</c> union and returns <c>ResolvedType</c>; its ported form
    /// (<c>Checker.Members.cs:849</c>) takes and returns the family base <see cref="UnionOrIntersectionType"/>, so the
    /// two casts below are that signature's, and the object family's leaf classes are all <see cref="ResolvedType"/>
    /// as the reference's cast assumed.
    /// </para>
    /// </remarks>
    internal bool IsEmptyObjectType(IType type)
        => (type.Flags & TypeFlags.Object) != 0
            ? !IsGenericMappedType(type) && IsEmptyResolvedType(ResolveStructuredTypeMembers((UnionOrIntersectionType)type))
            : (type.Flags & TypeFlags.NonPrimitive) != 0 ? true
            : (type.Flags & TypeFlags.Union) != 0 ? Core.Some(((IUnionType)type).Types, IsEmptyObjectType)
            : (type.Flags & TypeFlags.Intersection) != 0 ? Core.Every(((IIntersectionType)type).Types, (t, _) => IsEmptyObjectType(t))
            : false;

    /// <summary>ported from: src/compiler/checker.ts:22129-22134 — <c>isEmptyAnonymousObjectType</c>.</summary>
    internal bool IsEmptyAnonymousObjectType(IType type)
        => (GetObjectFlags(type) & ObjectFlags.Anonymous) != 0
            && ((((ResolvedType)type).Members is not null && IsEmptyResolvedType((ResolvedType)type))
                || (type.Symbol is not null
                    && (type.Symbol.Flags & SymbolFlags.TypeLiteral) != 0
                    && GetMembersOfSymbol(type.Symbol).Count == 0));

    /// <summary>ported from: src/compiler/checker.ts:22136-22146 — <c>isUnknownLikeUnionType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS writes the conditional flag into the union's <c>objectFlags</c> slot with <c>| (… ? flag : 0)</c>
    /// because its flags are plain numbers; the port's <see cref="ObjectFlags"/> is an enum, so the absent arm is
    /// <see cref="ObjectFlags.None"/>.
    /// </remarks>
    internal bool IsUnknownLikeUnionType(IType type)
    {
        if (_strictNullChecks && (type.Flags & TypeFlags.Union) != 0)
        {
            var unionType = (IUnionType)type;
            if ((unionType.ObjectFlags & ObjectFlags.IsUnknownLikeUnionComputed) == 0)
            {
                var types = unionType.Types!;
                unionType.ObjectFlags |= ObjectFlags.IsUnknownLikeUnionComputed
                    | (types.Count >= 3
                        && (types[0].Flags & TypeFlags.Undefined) != 0
                        && (types[1].Flags & TypeFlags.Null) != 0
                        && Core.Some(types, IsEmptyAnonymousObjectType)
                            ? ObjectFlags.IsUnknownLikeUnion
                            : ObjectFlags.None);
            }
            return (unionType.ObjectFlags & ObjectFlags.IsUnknownLikeUnion) != 0;
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:22148-22150 — <c>containsUndefinedType</c>.</summary>
    /// <remarks>PORT-NOTE: written as a static member because it reads none of the checker's state, as the port's other pure predicates do.</remarks>
    internal static bool ContainsUndefinedType(IType type)
        => (((type.Flags & TypeFlags.Union) != 0 ? ((IUnionType)type).Types![0] : type).Flags & TypeFlags.Undefined) != 0;

    /// <summary>ported from: src/compiler/checker.ts:22152-22155 — <c>containsNonMissingUndefinedType</c>.</summary>
    internal bool ContainsNonMissingUndefinedType(IType type)
    {
        var candidate = (type.Flags & TypeFlags.Union) != 0 ? ((IUnionType)type).Types![0] : type;
        return (candidate.Flags & TypeFlags.Undefined) != 0 && !ReferenceEquals(candidate, _missingType);
    }

    /// <summary>ported from: src/compiler/checker.ts:22157-22161 — <c>isStringIndexSignatureOnlyType</c>.</summary>
    internal bool IsStringIndexSignatureOnlyType(IType type)
        => ((type.Flags & TypeFlags.Object) != 0
                && !IsGenericMappedType(type)
                && GetPropertiesOfType(type).Count == 0
                && GetIndexInfosOfType(type).Count == 1
                && GetIndexInfoOfType(type, _stringType) is not null)
            || ((type.Flags & TypeFlags.UnionOrIntersection) != 0
                && Core.Every(((IUnionOrIntersectionType)type).Types, (t, _) => IsStringIndexSignatureOnlyType(t)));

    /// <summary>ported from: src/compiler/checker.ts:22163-22226 — <c>isEnumTypeRelatedTo</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the relation cache is keyed by <c>getSymbolId(sourceSymbol) + "," + getSymbolId(targetSymbol)</c>,
    /// i.e. JS's number-to-string conversion; the port spells that decimal conversion out with the invariant culture,
    /// the same treatment <c>GetTypeListId</c> records for its type-id keys.
    /// <para>
    /// PORT-NOTE: <c>enumRelation.get(id)</c> is <c>TryGetValue</c> here because the map's value is the
    /// <see cref="RelationComparisonResult"/> enum, whose default is <c>None</c> — a present entry can only be told
    /// from an absent one by the lookup's own result. The reference's <c>!== undefined</c> test is that same test.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>!==</c>/<c>===</c> on the two member values (a <c>string | number</c> pair held in
    /// <c>object?</c> slots here) is <c>Core.EquateValues</c>, which compares the boxed values rather than their
    /// references; <c>typeof x === "string"</c> is the port's <c>is string</c>, and the reference's aliased-condition
    /// narrowing is made explicit by the casts. The escaped diagnostic arguments are
    /// <c>escapeString</c> (utilities.ts:6207) and are called qualified.
    /// </para>
    /// </remarks>
    internal bool IsEnumTypeRelatedTo(ISymbol source, ISymbol target, ErrorReporter? errorReporter = null)
    {
        var sourceSymbol = (source.Flags & SymbolFlags.EnumMember) != 0 ? GetParentOfSymbol(source)! : source;
        var targetSymbol = (target.Flags & SymbolFlags.EnumMember) != 0 ? GetParentOfSymbol(target)! : target;
        if (ReferenceEquals(sourceSymbol, targetSymbol))
        {
            return true;
        }
        if (sourceSymbol.EscapedName != targetSymbol.EscapedName
            || (sourceSymbol.Flags & SymbolFlags.RegularEnum) == 0
            || (targetSymbol.Flags & SymbolFlags.RegularEnum) == 0)
        {
            return false;
        }

        var id = Utilities.GetSymbolId(sourceSymbol).ToString(CultureInfo.InvariantCulture) + ","
            + Utilities.GetSymbolId(targetSymbol).ToString(CultureInfo.InvariantCulture);
        if (_enumRelation.TryGetValue(id, out var entry)
            && !((entry & RelationComparisonResult.Failed) != 0 && errorReporter is not null))
        {
            return (entry & RelationComparisonResult.Succeeded) != 0;
        }

        var targetEnumType = GetTypeOfSymbol(targetSymbol);
        foreach (var sourceProperty in GetPropertiesOfType(GetTypeOfSymbol(sourceSymbol)))
        {
            if ((sourceProperty.Flags & SymbolFlags.EnumMember) != 0)
            {
                var targetProperty = GetPropertyOfType(targetEnumType, sourceProperty.EscapedName);
                if (targetProperty is null || (targetProperty.Flags & SymbolFlags.EnumMember) == 0)
                {
                    if (errorReporter is not null)
                    {
                        errorReporter(
                            Diagnostics.Property_0_is_missing_in_type_1,
                            Utilities.SymbolName(sourceProperty),
                            Diagnostic.DeferArg(() => TypeToString(GetDeclaredTypeOfSymbol(targetSymbol), /*enclosingDeclaration*/ null, TypeFormatFlags.UseFullyQualifiedType)));
                    }
                    _enumRelation[id] = RelationComparisonResult.Failed;
                    return false;
                }

                var sourceValue = GetEnumMemberValue((EnumMember)Utilities.GetDeclarationOfKind(sourceProperty, SyntaxKind.EnumMember)!)!.Value;
                var targetValue = GetEnumMemberValue((EnumMember)Utilities.GetDeclarationOfKind(targetProperty, SyntaxKind.EnumMember)!)!.Value;
                if (!Core.EquateValues(sourceValue, targetValue))
                {
                    var sourceIsString = sourceValue is string;
                    var targetIsString = targetValue is string;

                    // If we have 2 enums with *known* values that differ, they are incompatible.
                    if (sourceValue is not null && targetValue is not null)
                    {
                        if (errorReporter is not null)
                        {
                            var escapedSource = sourceIsString ? $"\"{Utilities.EscapeString((string)sourceValue!)}\"" : sourceValue;
                            var escapedTarget = targetIsString ? $"\"{Utilities.EscapeString((string)targetValue!)}\"" : targetValue;
                            errorReporter(
                                Diagnostics.Each_declaration_of_0_1_differs_in_its_value_where_2_was_expected_but_3_was_given,
                                Utilities.SymbolName(targetSymbol),
                                Utilities.SymbolName(targetProperty),
                                escapedTarget,
                                escapedSource);
                        }
                        _enumRelation[id] = RelationComparisonResult.Failed;
                        return false;
                    }

                    // At this point we know that at least one of the values is 'undefined'.
                    // This may mean that we have an opaque member from an ambient enum declaration,
                    // or that we were not able to calculate it (which is basically an error).
                    //
                    // Either way, we can assume that it's numeric.
                    // If the other is a string, we have a mismatch in types.
                    if (sourceIsString || targetIsString)
                    {
                        if (errorReporter is not null)
                        {
                            var knownStringValue = sourceValue ?? targetValue;
                            Debug.Assert(knownStringValue is string);
                            var escapedValue = $"\"{Utilities.EscapeString((string)knownStringValue!)}\"";
                            errorReporter(
                                Diagnostics.One_value_of_0_1_is_the_string_2_and_the_other_is_assumed_to_be_an_unknown_numeric_value,
                                Utilities.SymbolName(targetSymbol),
                                Utilities.SymbolName(targetProperty),
                                escapedValue);
                        }
                        _enumRelation[id] = RelationComparisonResult.Failed;
                        return false;
                    }
                }
            }
        }
        _enumRelation[id] = RelationComparisonResult.Succeeded;
        return true;
    }

    /// <summary>ported from: src/compiler/checker.ts:22228-22280 — <c>isSimpleTypeRelatedTo</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>source</c>/<c>target</c> are read through the literal and enum faces behind their flags tests;
    /// the two value comparisons the reference performs with <c>===</c> (on <c>StringLiteralType.value</c> and
    /// <c>NumberLiteralType.value</c>, both held in <c>object?</c> slots here) are <c>Core.EquateValues</c> so the
    /// boxed numeric values compare by value rather than by reference.
    /// </remarks>
    internal bool IsSimpleTypeRelatedTo(IType source, IType target, Dictionary<string, RelationComparisonResult> relation, ErrorReporter? errorReporter = null)
    {
        var s = source.Flags;
        var t = target.Flags;
        if ((t & TypeFlags.Any) != 0 || (s & TypeFlags.Never) != 0 || ReferenceEquals(source, _wildcardType)) return true;
        if ((t & TypeFlags.Unknown) != 0 && !(ReferenceEquals(relation, _strictSubtypeRelation) && (s & TypeFlags.Any) != 0)) return true;
        if ((t & TypeFlags.Never) != 0) return false;
        if ((s & TypeFlags.StringLike) != 0 && (t & TypeFlags.String) != 0) return true;
        if ((s & TypeFlags.StringLiteral) != 0 && (s & TypeFlags.EnumLiteral) != 0
            && (t & TypeFlags.StringLiteral) != 0 && (t & TypeFlags.EnumLiteral) == 0
            && Core.EquateValues(((IStringLiteralType)source).Value, ((IStringLiteralType)target).Value)) return true;
        if ((s & TypeFlags.NumberLike) != 0 && (t & TypeFlags.Number) != 0) return true;
        if ((s & TypeFlags.NumberLiteral) != 0 && (s & TypeFlags.EnumLiteral) != 0
            && (t & TypeFlags.NumberLiteral) != 0 && (t & TypeFlags.EnumLiteral) == 0
            && Core.EquateValues(((INumberLiteralType)source).Value, ((INumberLiteralType)target).Value)) return true;
        if ((s & TypeFlags.BigIntLike) != 0 && (t & TypeFlags.BigInt) != 0) return true;
        if ((s & TypeFlags.BooleanLike) != 0 && (t & TypeFlags.Boolean) != 0) return true;
        if ((s & TypeFlags.ESSymbolLike) != 0 && (t & TypeFlags.ESSymbol) != 0) return true;
        if ((s & TypeFlags.Enum) != 0 && (t & TypeFlags.Enum) != 0
            && source.Symbol!.EscapedName == target.Symbol!.EscapedName
            && IsEnumTypeRelatedTo(source.Symbol!, target.Symbol!, errorReporter)) return true;
        if ((s & TypeFlags.EnumLiteral) != 0 && (t & TypeFlags.EnumLiteral) != 0)
        {
            if ((s & TypeFlags.Union) != 0 && (t & TypeFlags.Union) != 0 && IsEnumTypeRelatedTo(source.Symbol!, target.Symbol!, errorReporter)) return true;
            if ((s & TypeFlags.Literal) != 0 && (t & TypeFlags.Literal) != 0
                && Core.EquateValues(((ILiteralType)source).Value, ((ILiteralType)target).Value)
                && IsEnumTypeRelatedTo(source.Symbol!, target.Symbol!, errorReporter)) return true;
        }
        // In non-strictNullChecks mode, `undefined` and `null` are assignable to anything except `never`.
        // Since unions and intersections may reduce to `never`, we exclude them here.
        if ((s & TypeFlags.Undefined) != 0
            && ((!_strictNullChecks && (t & TypeFlags.UnionOrIntersection) == 0) || (t & (TypeFlags.Undefined | TypeFlags.Void)) != 0)) return true;
        if ((s & TypeFlags.Null) != 0
            && ((!_strictNullChecks && (t & TypeFlags.UnionOrIntersection) == 0) || (t & TypeFlags.Null) != 0)) return true;
        if ((s & TypeFlags.Object) != 0 && (t & TypeFlags.NonPrimitive) != 0
            && !(ReferenceEquals(relation, _strictSubtypeRelation)
                && IsEmptyAnonymousObjectType(source)
                && (GetObjectFlags(source) & ObjectFlags.FreshLiteral) == 0)) return true;
        if (ReferenceEquals(relation, _assignableRelation) || ReferenceEquals(relation, _comparableRelation))
        {
            if ((s & TypeFlags.Any) != 0) return true;
            // Type number is assignable to any computed numeric enum type or any numeric enum literal type, and
            // a numeric literal type is assignable any computed numeric enum type or any numeric enum literal type
            // with a matching value. These rules exist such that enums can be used for bit-flag purposes.
            if ((s & TypeFlags.Number) != 0 && ((t & TypeFlags.Enum) != 0 || ((t & TypeFlags.NumberLiteral) != 0 && (t & TypeFlags.EnumLiteral) != 0))) return true;
            if ((s & TypeFlags.NumberLiteral) != 0 && (s & TypeFlags.EnumLiteral) == 0
                && ((t & TypeFlags.Enum) != 0
                    || ((t & TypeFlags.NumberLiteral) != 0 && (t & TypeFlags.EnumLiteral) != 0
                        && Core.EquateValues(((INumberLiteralType)source).Value, ((INumberLiteralType)target).Value)))) return true;
            // Anything is assignable to a union containing undefined, null, and {}
            if (IsUnknownLikeUnionType(target)) return true;
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:22282-22312 — <c>isTypeRelatedTo</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the relation map is read with <c>TryGetValue</c> rather than <c>get</c> plus an
    /// <c> !== undefined</c> test, because a <see cref="RelationComparisonResult"/> value is an enum whose default is
    /// <c>None</c> and an absent entry therefore has no distinguishable value of its own.
    /// </remarks>
    internal bool IsTypeRelatedTo(IType source, IType target, Dictionary<string, RelationComparisonResult> relation)
    {
        using var allocProbe = AllocationProbe.Enter("Checker.IsTypeRelatedTo");
        if (IsFreshLiteralType(source))
        {
            source = ((IFreshableType)source).RegularType!;
        }
        if (IsFreshLiteralType(target))
        {
            target = ((IFreshableType)target).RegularType!;
        }
        if (ReferenceEquals(source, target))
        {
            return true;
        }
        if (!ReferenceEquals(relation, _identityRelation))
        {
            if ((ReferenceEquals(relation, _comparableRelation) && (target.Flags & TypeFlags.Never) == 0 && IsSimpleTypeRelatedTo(target, source, relation))
                || IsSimpleTypeRelatedTo(source, target, relation))
            {
                return true;
            }
        }
        else if (((source.Flags | target.Flags) & (TypeFlags.UnionOrIntersection | TypeFlags.IndexedAccess | TypeFlags.Conditional | TypeFlags.Substitution)) == 0)
        {
            // We have excluded types that may simplify to other forms, so types must have identical flags
            if (source.Flags != target.Flags) return false;
            if ((source.Flags & TypeFlags.Singleton) != 0) return true;
        }
        if ((source.Flags & TypeFlags.Object) != 0 && (target.Flags & TypeFlags.Object) != 0)
        {
            if (relation.TryGetValue(GetRelationKey(source, target, IntersectionState.None, relation, /*ignoreConstraints*/ false), out var related))
            {
                return (related & RelationComparisonResult.Succeeded) != 0;
            }
        }
        if ((source.Flags & TypeFlags.StructuredOrInstantiable) != 0 || (target.Flags & TypeFlags.StructuredOrInstantiable) != 0)
        {
            return CheckTypeRelatedTo(source, target, relation, /*errorNode*/ null);
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:22314-22316 — <c>isIgnoredJsxProperty</c>.</summary>
    internal bool IsIgnoredJsxProperty(IType source, ISymbol sourceProp)
        => (GetObjectFlags(source) & ObjectFlags.JsxAttributes) != 0 && IsHyphenatedJsxName(sourceProp.EscapedName!);

    // ---------------------------------------------------------------------------------------------
    // The normalizer (checker.ts:22318-22378)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:22318-22330 — <c>getNormalizedType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's chain of <c>as</c> casts is guarded by the same flags tests here
    /// (<c>isFreshLiteralType</c>, <c>isGenericTupleType</c>, the <c>Reference</c> object flag,
    /// <c>UnionOrIntersection</c>, <c>Substitution</c>, <c>Simplifiable</c>); the port states each cast explicitly, as
    /// the family classes make them checks rather than the no-ops TS's structural typing turned them into.
    /// </remarks>
    internal IType GetNormalizedType(IType type, bool writing)
    {
        while (true)
        {
            var t = IsFreshLiteralType(type) ? ((IFreshableType)type).RegularType!
                : IsGenericTupleType(type) ? GetNormalizedTupleType((ITupleTypeReference)type, writing)
                : (GetObjectFlags(type) & ObjectFlags.Reference) != 0
                    ? ((ITypeReference)type).Node is not null
                        ? CreateTypeReference(((ITypeReference)type).Target!, GetTypeArguments((ITypeReference)type))
                        : (GetSingleBaseForNonAugmentingSubtype(type) ?? type)
                : (type.Flags & TypeFlags.UnionOrIntersection) != 0 ? GetNormalizedUnionOrIntersectionType((UnionOrIntersectionType)type, writing)
                : (type.Flags & TypeFlags.Substitution) != 0 ? (writing ? ((ISubstitutionType)type).BaseType! : GetSubstitutionIntersection((ISubstitutionType)type))
                : (type.Flags & TypeFlags.Simplifiable) != 0 ? GetSimplifiedType(type, writing)
                : type;
            if (ReferenceEquals(t, type)) return t;
            type = t;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:22332-22350 — <c>getNormalizedUnionOrIntersectionType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>sameMap</c> answers the input list when nothing changed (core.ts:350-366), which is what the
    /// reference's <c>normalizedTypes !== type.types</c> test reads; the port keeps that identity test and converts the
    /// mapped list back to the <c>List&lt;IType&gt;</c> the intersection factory takes.
    /// </remarks>
    internal IType GetNormalizedUnionOrIntersectionType(UnionOrIntersectionType type, bool writing)
    {
        var reduced = GetReducedType(type);
        if (!ReferenceEquals(reduced, type))
        {
            return reduced;
        }
        if ((type.Flags & TypeFlags.Intersection) != 0 && ShouldNormalizeIntersection((IntersectionType)type))
        {
            // Normalization handles cases like
            // Partial<T>[K] & ({} | null) ==>
            // Partial<T>[K] & {} | Partial<T>[K] & null ==>
            // (T[K] | undefined) & {} | (T[K] | undefined) & null ==>
            // T[K] & {} | undefined & {} | T[K] & null | undefined & null ==>
            // T[K] & {} | T[K] & null
            var normalizedTypes = Core.SameMap(type.Types, (t, _) => GetNormalizedType(t, writing));
            if (!ReferenceEquals(normalizedTypes, type.Types))
            {
                return GetIntersectionType(TypeList(normalizedTypes));
            }
        }
        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:22352-22361 — <c>shouldNormalizeIntersection</c>.</summary>
    internal bool ShouldNormalizeIntersection(IIntersectionType type)
    {
        var hasInstantiable = false;
        var hasNullableOrEmpty = false;
        foreach (var t in type.Types!)
        {
            // PORT-NOTE: TS 5.2's `hasInstantiable ||= …` short-circuiting assignment has no C# counterpart (`||=` is
            // not a C# operator), so the port states the same short-circuit as an assignment.
            hasInstantiable = hasInstantiable || (t.Flags & TypeFlags.Instantiable) != 0;
            hasNullableOrEmpty = hasNullableOrEmpty || (t.Flags & TypeFlags.Nullable) != 0 || IsEmptyAnonymousObjectType(t);
            if (hasInstantiable && hasNullableOrEmpty) return true;
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:22363-22368 — <c>getNormalizedTupleType</c>.</summary>
    internal IType GetNormalizedTupleType(ITupleTypeReference type, bool writing)
    {
        var elements = GetElementTypes(type);
        var normalizedElements = Core.SameMap(elements, (t, _) => (t.Flags & TypeFlags.Simplifiable) != 0 ? GetSimplifiedType(t, writing) : t);
        return !ReferenceEquals(elements, normalizedElements)
            ? CreateNormalizedTupleType((ITupleType)type.Target!, TypeList(normalizedElements))
            : type;
    }
}
