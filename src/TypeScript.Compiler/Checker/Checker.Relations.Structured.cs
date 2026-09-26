namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:22380-24900 (checkTypeRelatedTo)
/// <summary>
/// The relation block: <c>checkTypeRelatedTo</c> (checker.ts:22380-24900), the driver of the assignable,
/// comparable, identity, subtype and strict-subtype relations, together with the complete chain of helper
/// closures it nests — the error-elaboration pyramid, the union/intersection decomposition, the recursion and
/// maybe-stack guard, and the structural comparison of objects, tuples, mapped types, signatures and index
/// signatures.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (layout): the reference nests ~45 functions inside <c>checkTypeRelatedTo</c> precisely so that they
/// share its closure state — the error-elaboration chain (<c>errorInfo</c>), the skipped-error bookkeeping
/// (<c>lastSkippedInfo</c>, <c>overrideNextErrorInfo</c>, <c>skipParentCounter</c>, <c>incompatibleStack</c>), the
/// maybe-stack (<c>maybeKeys</c>, <c>maybeKeysSet</c>, <c>maybeCount</c>), the two recursion-depth counters and
/// stacks (<c>sourceDepth</c>/<c>sourceStack</c>, <c>targetDepth</c>/<c>targetStack</c>), the expanding flags and
/// the relation budget (<c>relationCount</c>). The port keeps every one of them a C# local function of
/// <see cref="CheckTypeRelatedTo"/>, in the reference's order, which is the only arrangement in which the
/// mutation-heavy error pyramid can be copied literally instead of being threaded through ~45 signatures. The
/// port permits function-level rearrangement (docs/architecture.md §6); this file exercises it to the extreme by
/// keeping the rearrangement *null* — the reference's own nesting is reproduced one to one, so no name gained a
/// "state" parameter and no branch had to be re-expressed.
/// </para>
/// <para>
/// PORT-NOTE (documentation): the one class member this file adds, <see cref="CheckTypeRelatedTo"/>, carries the
/// usual <c>/// &lt;summary&gt;ported from:</c> block. Each lifted nested closure is documented by the same
/// <c>ported from: src/compiler/checker.ts:…</c> text on a <c>//</c> comment line directly above it, because a
/// local function is not a member and the XML forms do not attach to it (and GenerateDocumentationFile is off, so
/// a <c>///</c> comment there would be dropped without a diagnostic). Every nested closure's range is also listed
/// in the slice report.
/// </para>
/// <para>
/// PORT-NOTE (deviation inventory): the reference's <c>let</c> slots that TS leaves <c>undefined</c> until first
/// assignment are declared here with an explicit <c>= null</c> initializer, because a C# local that a local
/// function captures must be definitely assigned before any call site (CS0165) —
/// <c>errorInfo</c>, <c>relatedInfo</c>, <c>maybeKeys</c>, <c>maybeKeysSet</c>, <c>sourceStack</c>,
/// <c>targetStack</c>, <c>lastSkippedInfo</c>, <c>incompatibleStack</c>.
/// </para>
/// <para>
/// PORT-NOTE (cross-slice calls): the relation caches (<c>_assignableRelation</c> … <c>_identityRelation</c>) and
/// <c>GetRelationKey</c>, <c>IsDeeplyNestedType</c>, <c>CompareSignaturesIdentical</c>, <c>GetVariances</c>,
/// <c>GetErasedSignature</c>, <c>CompareProperties</c>, <c>GetBestMatchingType</c>,
/// <c>GetExactOptionalUnassignableProperties</c>, <c>FindMatchingDiscriminantType</c>,
/// <c>FilterPrimitivesIfContainsNonPrimitive</c>, <c>IsTypeSubsetOf</c>, <c>IsWeakType</c>,
/// <c>HasCommonProperties</c>, <c>IsKnownProperty</c>, <c>IsExcessPropertyCheckTarget</c>,
/// <c>GetUnmatchedProperty</c>, <c>ExtractTypesOfKind</c>, <c>GetRegularTypeOfObjectLiteral</c>,
/// <c>CountTypes</c>, <c>FindDiscriminantProperties</c>, <c>GetMatchingUnionConstituentForType</c>,
/// <c>IsMutableArrayOrTuple</c>, <c>GetSingleBaseForNonAugmentingSubtype</c>,
/// <c>GetBaseTypeOfLiteralType</c>, <c>IsLiteralType</c>, <c>TypeCouldHaveTopLevelSingletonTypes</c>,
/// <c>GetSuggestedTypeForNonexistentStringLiteralType</c>, <c>SymbolValueDeclarationIsContextSensitive</c>,
/// <c>GetExactOptionalProperties</c>, <c>GetJsxType</c>, <c>CloneTypeParameter</c>,
/// <c>CreateInferenceContext</c>, <c>InferTypes</c>, <c>GetSuggestionForNonexistentProperty</c>,
/// <c>GetSuggestedSymbolForNonexistentJSXAttribute</c>, <c>IsTypeMatchedByTemplateLiteralType</c>,
/// <c>IsMemberOfStringMapping</c>, <c>TemplateLiteralTypesDefinitelyUnrelated</c>,
/// <c>GetSimplifiedTypeOrConstraint</c>, <c>IsMappedTypeGenericIndexedAccess</c>, <c>ShouldDeferIndexType</c>,
/// <c>GetLiteralTypeFromProperty</c>, <c>IsApplicableIndexType</c>, <c>ShouldDeferIndexType</c>,
/// <c>GetBaseConstraintOrType</c>, <c>GetEffectiveConstraintOfIntersection</c>,
/// <c>GetDefaultConstraintOfConditionalType</c>, <c>GetConstraintOfDistributiveConditionalType</c>,
/// <c>GetTypeOfSymbol</c>, <c>GetSignaturesOfType</c>, <c>GetSignaturesOfStructuredType</c>,
/// <c>GetReturnTypeOfSignature</c>, <c>GetPropertiesOfType</c>, <c>GetPropertyOfType</c>,
/// <c>GetPropertyOfObjectType</c>, <c>GetPropertyOfUnionOrIntersectionType</c>, <c>GetIndexInfosOfType</c>,
/// <c>GetIndexInfoOfType</c>, <c>GetIndexTypeOfType</c>, <c>GetApplicableIndexInfo</c>,
/// <c>GetApplicableIndexInfoForName</c>, <c>GetNonMissingTypeOfSymbol</c>, <c>GetTypeOfPropertyOfType</c>,
/// <c>GetIndexType</c>, <c>GetIndexedAccessType</c>, <c>GetIndexedAccessTypeOrUndefined</c>,
/// <c>GetTypeWithThisArgument</c>, <c>GetObjectFlags</c>, <c>GetCheckFlags</c>, <c>GetDeclarationModifierFlagsFromSymbol</c>,
/// <c>GetPropertiesOfObjectType</c>, <c>AddRelatedInfo</c>, <c>ChainDiagnosticMessages</c>,
/// <c>CreateDiagnosticForNodeFromMessageChain</c>, <c>ConcatenateDiagnosticMessageChains</c>,
/// <c>CreateDiagnosticForNode</c>, <c>GetSourceFileOfNode</c>, <c>FindAncestor</c>,
/// <c>IsObjectLiteralElementLike</c>, <c>IsNamedDeclaration</c>, <c>IsJsxOpeningLikeElement</c>,
/// <c>GetSymbolNameForPrivateIdentifier</c>, <c>AddOptionality</c>, <c>InstantiateType</c>,
/// <c>CreateTypeMapper</c>, <c>AppendTypeMapping</c>, <c>CreateArrayType</c>, <c>GetUnionType</c>,
/// <c>GetIntersectionType</c>, <c>GetApparentType</c>, <c>GetBaseConstraintOfType</c>,
/// <c>GetConstraintOfType</c>, <c>GetTypeArguments</c>, <c>GetTypeReferenceArity</c>,
/// <c>GetStartElementCount</c>, <c>GetEndElementCount</c>, <c>RemoveMissingType</c>,
/// <c>GetPermissiveInstantiation</c>, <c>GetRestrictiveInstantiation</c>,
/// <c>GetTrueTypeFromConditionalType</c>, <c>GetFalseTypeFromConditionalType</c>,
/// <c>IsDistributionDependent</c>, <c>GetVerbatimModuleSyntaxErrorMessage</c>,
/// <c>IsMappedTypeWithKeyofConstraintDeclaration</c>, <c>GetNameTypeFromMappedType</c>,
/// <c>GetConstraintTypeFromMappedType</c>, <c>GetTemplateTypeFromMappedType</c>,
/// <c>GetTypeParameterFromMappedType</c>, <c>GetModifiersTypeFromMappedType</c>,
/// <c>ForEachMappedTypePropertyKeyTypeAndIndexSignatureKeyType</c>, <c>GetMappedTypeModifiers</c>,
/// <c>GetCombinedMappedTypeOptionality</c>, <c>IsPartialMappedType</c>, <c>IsGenericMappedType</c>,
/// <c>IsGenericObjectType</c>, <c>IsNonGenericObjectType</c>, <c>IsGenericTupleType</c>,
/// <c>IsEmptyArrayLiteralType</c>, <c>IsEmptyObjectType</c>, <c>IsErrorType</c>, <c>IsReadonlySymbol</c>,
/// <c>IsValidOverrideOf</c>, <c>GetDeclaringClass</c>, <c>IsJSConstructor</c>, <c>SignatureToString</c>,
/// <c>IsTypeAssignableTo</c>, <c>IsTypeIdenticalTo</c>, <c>GetTypeWithFacts</c>,
/// <c>GetKnownKeysOfTupleType</c>, <c>IsSingleElementGenericTupleType</c>, <c>IsSimpleTypeRelatedTo</c>,
/// <c>GetNormalizedType</c>, <c>IsIgnoredJsxProperty</c>, <c>IsTypeParameterPossiblyReferenced</c>,
/// <c>GetCanonicalDiagnostic</c>, <c>DiagnosticName</c>, <c>IsExcessPropertyCheckTarget</c>,
/// <c>GetNormalizedType</c>, <c>IsMemberOfStringMapping</c> and the intrinsic-type slots are declared elsewhere in
/// the port; this file references them and declares none of them (§15.15 one-name-one-home, and the slice report
/// names each owner).
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    /// <remarks>
    /// PORT-NOTE: types.ts declares <c>CanonicalDiagnostic</c> as the interface
    /// <see cref="ICanonicalDiagnostic"/> and the checker builds it as an object literal
    /// (<c>{ code, messageText }</c>) in the one branch below that needs a constructible instance. The generated
    /// model has no constructible face for it yet, so this file declares a private one — the only writer of
    /// <c>errorInfo.canonicalHead</c> in <c>checkTypeRelatedTo</c> — rather than upgrading
    /// <c>Model/NodeInterfaces.cs</c>, which is generated.
    /// </remarks>
    private sealed class CanonicalDiagnosticHead : ICanonicalDiagnostic
    {
        public int Code { get; set; }

        public string? MessageText { get; set; }
    }

    /// <remarks>
    /// PORT-NOTE: the reference's <c>captureErrorCalculationState</c> returns an anonymous object with the six
    /// slots below and <c>resetErrorInfo</c> writes them back; the port names that object. The tuple slots are
    /// TS's <c>[Type, Type]</c> (<c>lastSkippedInfo</c>) and <c>[DiagnosticMessage, ...DiagnosticArguments]</c>
    /// (<c>incompatibleStack</c>), spelled as value tuples because neither is a named type in types.ts.
    /// </remarks>
    private sealed record ErrorCalculationState(
        IDiagnosticMessageChain? ErrorInfo,
        (IType Source, IType Target)? LastSkippedInfo,
        List<(DiagnosticMessage Message, object?[] Args)>? IncompatibleStack,
        int OverrideNextErrorInfo,
        int SkipParentCounter,
        List<IDiagnosticRelatedInformation>? RelatedInfo);

    /// <summary>ported from: src/compiler/checker.ts:22380-24900 — <c>checkTypeRelatedTo</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: TS's <c>errorNode</c> is <c>Node | undefined</c>; the fourth parameter keeps that optionality
    /// (<c>Node?</c>) and the last three keep the reference's own optionality with <c>null</c> defaults, so both
    /// the four-argument callers (<c>isTypeRelatedTo</c>) and the seven-argument callers compile against one
    /// signature.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>relation</c> is TS's <c>Map&lt;string, RelationComparisonResult&gt;</c>, spelled
    /// <c>Dictionary&lt;string, RelationComparisonResult&gt;</c> with its <c>Count</c>/<c>GetValueOrDefault</c>/
    /// indexer members standing in for <c>size</c>/<c>get</c>/<c>set</c>; the five instances live in
    /// <c>Checker.Relations.Entries.cs</c>.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>errorInfo</c> is <c>IDiagnosticMessageChain?</c> rather than the concrete
    /// <c>DiagnosticMessageChain</c> because the two cross-slice writers it flows through —
    /// <c>ElaborateNeverIntersection</c> (<c>Checker.Members.cs</c>) and
    /// <c>Utilities.CreateDiagnosticForNodeFromMessageChain</c> — take the interface; the concrete
    /// <c>Utilities.ChainDiagnosticMessages</c> result assigns to it unchanged.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>reportIncompatibleStack</c> switches on <c>msg.code</c>; a C# <c>case</c> label must be a
    /// compile-time constant and <c>Diagnostics.X.Code</c> is a property read, so that switch is an
    /// if/else-if chain over <c>msg.Code</c> in the same arm order.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>sourceStack</c>/<c>targetStack</c>/<c>maybeKeys</c> are TS arrays written at a running depth
    /// index (<c>stack[depth] = type</c>) and read back below the current depth by
    /// <c>isDeeplyNestedType</c>/<c>resetMaybeStack</c>. C#'s <c>List&lt;T&gt;</c> indexer cannot assign past the
    /// end, so the three sites go through the local <c>SetAt</c> helper, which appends when the index is the end of
    /// the list and otherwise overwrites — exactly what the JS array does at those sites. The stale entries the
    /// reference leaves above the live depth are still there and still readable, which is what
    /// <c>IsDeeplyNestedType</c> relies on.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>isSimpleTypeRelatedTo(a, b, relation, reportErrors ? reportError : undefined)</c> passes
    /// the possibly-absent reporter as a value. C# cannot name a delegate type here without naming
    /// <c>ErrorReporter</c>, which is declared by <c>Checker.Relations.Matching.cs</c>, so the four sites pass a
    /// target-typed conditional whose non-null arm is a lambda with the reporter's own shape —
    /// behaviourally identical (a reporter that forwards to <c>reportError</c>, or <c>null</c>).
    /// </para>
    /// </remarks>
    internal bool CheckTypeRelatedTo(
        IType source,
        IType target,
        Dictionary<string, RelationComparisonResult> relation,
        Node? errorNode,
        DiagnosticMessage? headMessage = null,
        Func<IDiagnosticMessageChain?>? containingMessageChain = null,
        IErrorOutputContainer? errorOutputContainer = null)
    {
        IDiagnosticMessageChain? errorInfo = null;
        List<IDiagnosticRelatedInformation>? relatedInfo = null;
        List<string>? maybeKeys = null;
        HashSet<string>? maybeKeysSet = null;
        List<IType>? sourceStack = null;
        List<IType>? targetStack = null;
        var maybeCount = 0;
        var sourceDepth = 0;
        var targetDepth = 0;
        var expandingFlags = ExpandingFlags.None;
        var overflow = false;
        var overrideNextErrorInfo = 0; // How many `reportRelationError` calls should be skipped in the elaboration pyramid
        var skipParentCounter = 0; // How many errors should be skipped 'above' in the elaboration pyramid
        (IType Source, IType Target)? lastSkippedInfo = null;
        List<(DiagnosticMessage Message, object?[] Args)>? incompatibleStack = null;
        // In Node.js, the maximum number of elements in a map is 2^24. We limit the number of entries an invocation
        // of checkTypeRelatedTo can add to a relation to 1/8th of its remaining capacity.
        var relationCount = (16_000_000 - relation.Count) >> 3;

        Debug.Assert(!ReferenceEquals(relation, _identityRelation) || errorNode is null, "no error reporting in identity checking");

        var result = IsRelatedTo(source, target, RecursionFlags.Both, /*reportErrors*/ errorNode is not null, headMessage);
        if (incompatibleStack is not null)
        {
            ReportIncompatibleStack();
        }
        if (overflow)
        {
            // Record this relation as having failed such that we don't attempt the overflowing operation again.
            var id = GetRelationKey(source, target, /*intersectionState*/ IntersectionState.None, relation, /*ignoreConstraints*/ false);
            relation[id] = RelationComparisonResult.Failed | (relationCount <= 0 ? RelationComparisonResult.ComplexityOverflow : RelationComparisonResult.StackDepthOverflow);
            Tracing.Current?.Instant(
                Tracing.Phase.CheckTypes,
                "checkTypeRelatedTo_DepthLimit",
                TracingArgs.Of(("sourceId", source.Id), ("targetId", target.Id), ("depth", sourceDepth), ("targetDepth", targetDepth)));
            var message = relationCount <= 0
                ? Diagnostics.Excessive_complexity_comparing_types_0_and_1
                : Diagnostics.Excessive_stack_depth_comparing_types_0_and_1;
            // PORT-NOTE (A″): printer-gated stringifier arguments are deferred — see Core/Diagnostic.cs's `DiagnosticArgument`.
            var diag = Error(errorNode ?? _currentNode, message, Diagnostic.DeferArg(() => TypeToString(source)), Diagnostic.DeferArg(() => TypeToString(target)));
            if (errorOutputContainer is not null)
            {
                (errorOutputContainer.Errors ??= []).Add(diag);
            }
        }
        else if (errorInfo is not null)
        {
            if (containingMessageChain is not null)
            {
                var chain = containingMessageChain();
                if (chain is not null)
                {
                    // PORT-NOTE: `concatenateDiagnosticMessageChains` mutates and is typed on the concrete chain
                    // class; the callback's face is the interface, hence the one cast.
                    Utilities.ConcatenateDiagnosticMessageChains((DiagnosticMessageChain)chain, (DiagnosticMessageChain)errorInfo);
                    errorInfo = chain;
                }
            }

            IList<IDiagnosticRelatedInformation>? relatedInformation = null;
            // Check if we should issue an extra diagnostic to produce a quickfix for a slightly incorrect import statement
            if (headMessage is not null && errorNode is not null && result == Ternary.False && source.Symbol is not null)
            {
                var links = GetSymbolLinks(source.Symbol);
                if (links.OriginatingImport is not null && !Utilities.IsImportCall(links.OriginatingImport))
                {
                    var helpfulRetry = CheckTypeRelatedTo(GetTypeOfSymbol(links.Target!), target, relation, /*errorNode*/ null);
                    if (helpfulRetry)
                    {
                        // Likely an incorrect import. Issue a helpful diagnostic to produce a quickfix to change the import
                        var importDiag = Utilities.CreateDiagnosticForNode(links.OriginatingImport!, Diagnostics.Type_originates_at_this_import_A_namespace_style_import_cannot_be_called_or_constructed_and_will_cause_a_failure_at_runtime_Consider_using_a_default_import_or_import_require_here_instead);
                        relatedInformation = Core.Append(relatedInformation, importDiag); // Cause the error to appear with the error that triggered it
                    }
                }
            }
            var diag = Utilities.CreateDiagnosticForNodeFromMessageChain(Utilities.GetSourceFileOfNode(errorNode!)!, errorNode!, errorInfo, relatedInformation);
            if (relatedInfo is not null)
            {
                Utilities.AddRelatedInfo(diag, [.. relatedInfo]);
            }
            if (errorOutputContainer is not null)
            {
                (errorOutputContainer.Errors ??= []).Add(diag);
            }
            if (errorOutputContainer is null || errorOutputContainer.SkipLogging != true)
            {
                _diagnostics.Add(diag);
            }
        }
        if (errorNode is not null && errorOutputContainer is not null && errorOutputContainer.SkipLogging == true && result == Ternary.False)
        {
            Debug.Assert(errorOutputContainer.Errors is not null, "missed opportunity to interact with error.");
        }

        return result != Ternary.False;

        // PORT-NOTE: `SetAt` models the JS array write at a running depth index; see the member remarks.
        static void SetAt<T>(List<T> list, int index, T value)
        {
            if (index < list.Count)
            {
                list[index] = value;
            }
            else
            {
                list.Add(value);
            }
        }

        // PORT-NOTE: TS spells the accumulation of relation results `result &= related`. Both are `Ternary`, whose
        // members (False 0, Unknown 1, Maybe 3, True -1) are exactly the port's (`Model/Ternary.cs`), so the
        // reference's bitwise-and is `(int)&(int)` — but C# defines no `&` operator on enum types, so every one of
        // the file's ~25 `&=`/`&` sites goes through this helper instead of an operator.
        static Ternary And(Ternary a, Ternary b) => (Ternary)((int)a & (int)b);

        // PORT-NOTE: TS reads tuple fields through `target.target`, whose static type is `TupleType`; the port's
        // `ITypeReference.Target` is the wider `IGenericType`, so every one of the file's fifteen tuple-target reads
        // goes through this one cast instead of fifteen inline casts.
        static ITupleType TupleOf(ITupleTypeReference reference) => (ITupleType)reference.Target!;

        // ported from: src/compiler/checker.ts:22466-22473 — <c>resetErrorInfo</c>
        void ResetErrorInfo(ErrorCalculationState saved)
        {
            errorInfo = saved.ErrorInfo;
            lastSkippedInfo = saved.LastSkippedInfo;
            incompatibleStack = saved.IncompatibleStack;
            overrideNextErrorInfo = saved.OverrideNextErrorInfo;
            skipParentCounter = saved.SkipParentCounter;
            relatedInfo = saved.RelatedInfo;
        }

        // ported from: src/compiler/checker.ts:22475-22484 — <c>captureErrorCalculationState</c>
        ErrorCalculationState CaptureErrorCalculationState()
            => new(
                errorInfo,
                lastSkippedInfo,
                incompatibleStack is null ? null : [.. incompatibleStack],
                overrideNextErrorInfo,
                skipParentCounter,
                relatedInfo is null ? null : [.. relatedInfo]);

        // ported from: src/compiler/checker.ts:22486-22490 — <c>reportIncompatibleError</c>
        void ReportIncompatibleError(DiagnosticMessage message, params object?[] args)
        {
            overrideNextErrorInfo++; // Suppress the next relation error
            lastSkippedInfo = null; // Reset skipped info cache
            (incompatibleStack ??= []).Add((message, args));
        }

        // ported from: src/compiler/checker.ts:22492-22599 — <c>reportIncompatibleStack</c>
        void ReportIncompatibleStack()
        {
            var stack = incompatibleStack ?? [];
            incompatibleStack = null;
            var info = lastSkippedInfo;
            lastSkippedInfo = null;
            if (stack.Count == 1)
            {
                ReportError(stack[0].Message, stack[0].Args);
                if (info is not null)
                {
                    // Actually do the last relation error
                    ReportRelationError(/*message*/ null, info.Value.Source, info.Value.Target);
                }
                return;
            }
            // The first error will be the innermost, while the last will be the outermost - so by popping off the end,
            // we can build from left to right
            var path = "";
            var secondaryRootErrors = new List<(DiagnosticMessage Message, object?[] Args)>();
            while (stack.Count != 0)
            {
                var (msg, args) = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                var code = msg.Code;
                if (code == Diagnostics.Types_of_property_0_are_incompatible.Code)
                {
                    // Parenthesize a `new` if there is one
                    if (path.StartsWith("new ", StringComparison.Ordinal))
                    {
                        path = $"({path})";
                    }
                    var str = "" + args[0];
                    // If leading, just print back the arg (irrespective of if it's a valid identifier)
                    if (path.Length == 0)
                    {
                        path = $"{str}";
                    }
                    // Otherwise write a dotted name if possible
                    else if (ScannerUtilities.IsIdentifierText(str, Utilities.GetEmitScriptTarget(_compilerOptions)))
                    {
                        path = $"{path}.{str}";
                    }
                    // Failing that, check if the name is already a computed name
                    // PORT-NOTE: TS reads `str[0]`/`str[str.length - 1]`, which are `undefined` (and therefore
                    // unequal to the brackets) for the empty string; the port states that guard explicitly.
                    else if (str.Length != 0 && str[0] == '[' && str[^1] == ']')
                    {
                        path = $"{path}{str}";
                    }
                    // And finally write out a computed name as a last resort
                    else
                    {
                        path = $"{path}[{str}]";
                    }
                }
                else if (code == Diagnostics.Call_signature_return_types_0_and_1_are_incompatible.Code
                    || code == Diagnostics.Construct_signature_return_types_0_and_1_are_incompatible.Code
                    || code == Diagnostics.Call_signatures_with_no_arguments_have_incompatible_return_types_0_and_1.Code
                    || code == Diagnostics.Construct_signatures_with_no_arguments_have_incompatible_return_types_0_and_1.Code)
                {
                    if (path.Length == 0)
                    {
                        // Don't flatten signature compatability errors at the start of a chain - instead prefer
                        // to unify (the with no arguments bit is excessive for printback) and print them back
                        var mappedMsg = msg;
                        if (code == Diagnostics.Call_signatures_with_no_arguments_have_incompatible_return_types_0_and_1.Code)
                        {
                            mappedMsg = Diagnostics.Call_signature_return_types_0_and_1_are_incompatible;
                        }
                        else if (code == Diagnostics.Construct_signatures_with_no_arguments_have_incompatible_return_types_0_and_1.Code)
                        {
                            mappedMsg = Diagnostics.Construct_signature_return_types_0_and_1_are_incompatible;
                        }
                        secondaryRootErrors.Insert(0, (mappedMsg, [args[0], args[1]]));
                    }
                    else
                    {
                        var prefix = code == Diagnostics.Construct_signature_return_types_0_and_1_are_incompatible.Code
                            || code == Diagnostics.Construct_signatures_with_no_arguments_have_incompatible_return_types_0_and_1.Code
                                ? "new "
                                : "";
                        var parameters = code == Diagnostics.Call_signatures_with_no_arguments_have_incompatible_return_types_0_and_1.Code
                            || code == Diagnostics.Construct_signatures_with_no_arguments_have_incompatible_return_types_0_and_1.Code
                                ? ""
                                : "...";
                        path = $"{prefix}{path}({parameters})";
                    }
                }
                else if (code == Diagnostics.Type_at_position_0_in_source_is_not_compatible_with_type_at_position_1_in_target.Code)
                {
                    secondaryRootErrors.Insert(0, (Diagnostics.Type_at_position_0_in_source_is_not_compatible_with_type_at_position_1_in_target, [args[0], args[1]]));
                }
                else if (code == Diagnostics.Type_at_positions_0_through_1_in_source_is_not_compatible_with_type_at_position_2_in_target.Code)
                {
                    secondaryRootErrors.Insert(0, (Diagnostics.Type_at_positions_0_through_1_in_source_is_not_compatible_with_type_at_position_2_in_target, [args[0], args[1], args[2]]));
                }
                else
                {
                    Debug.Fail($"Unhandled Diagnostic: {code}");
                    return;
                }
            }
            if (path.Length != 0)
            {
                ReportError(
                    path[^1] == ')'
                        ? Diagnostics.The_types_returned_by_0_are_incompatible_between_these_types
                        : Diagnostics.The_types_of_0_are_incompatible_between_these_types,
                    path);
            }
            else
            {
                // Remove the innermost secondary error as it will duplicate the error already reported by `reportRelationError` on entry
                secondaryRootErrors.RemoveAt(0);
            }
            foreach (var (msg, args) in secondaryRootErrors)
            {
                var originalValue = msg.ElidedInCompatabilityPyramid;
                msg.ElidedInCompatabilityPyramid = false; // Temporarily override elision to ensure error is reported
                ReportError(msg, args);
                msg.ElidedInCompatabilityPyramid = originalValue;
            }
            if (info is not null)
            {
                // Actually do the last relation error
                ReportRelationError(/*message*/ null, info.Value.Source, info.Value.Target);
            }
        }

        // ported from: src/compiler/checker.ts:22601-22611 — <c>reportError</c>
        void ReportError(DiagnosticMessage message, params object?[] args)
        {
            Debug.Assert(errorNode is not null);
            if (incompatibleStack is not null) ReportIncompatibleStack();
            if (message.ElidedInCompatabilityPyramid == true) return;
            if (skipParentCounter == 0)
            {
                errorInfo = Utilities.ChainDiagnosticMessages(errorInfo, message, args);
            }
            else
            {
                skipParentCounter--;
            }
        }

        // ported from: src/compiler/checker.ts:22613-22616 — <c>reportParentSkippedError</c>
        void ReportParentSkippedError(DiagnosticMessage message, params object?[] args)
        {
            ReportError(message, args);
            skipParentCounter++;
        }

        // ported from: src/compiler/checker.ts:22618-22626 — <c>associateRelatedInfo</c>
        void AssociateRelatedInfo(IDiagnosticRelatedInformation info)
        {
            Debug.Assert(errorInfo is not null);
            if (relatedInfo is null)
            {
                relatedInfo = [info];
            }
            else
            {
                relatedInfo.Add(info);
            }
        }

        // ported from: src/compiler/checker.ts:22628-22698 — <c>reportRelationError</c>
        void ReportRelationError(DiagnosticMessage? message, IType source, IType target)
        {
            if (incompatibleStack is not null) ReportIncompatibleStack();
            var (sourceType, targetType) = GetTypeNamesForErrorDisplay(source, target);
            var generalizedSource = source;
            var generalizedSourceType = sourceType;

            // Don't generalize on 'never' - we really want the original type
            // to be displayed for use-cases like 'assertNever'.
            if ((target.Flags & TypeFlags.Never) == 0 && IsLiteralType(source) && !TypeCouldHaveTopLevelSingletonTypes(target))
            {
                generalizedSource = GetBaseTypeOfLiteralType(source);
                Debug.Assert(!IsTypeAssignableTo(generalizedSource, target), "generalized source shouldn't be assignable");
                generalizedSourceType = GetTypeNameForErrorDisplay(generalizedSource);
            }

            // If `target` is of indexed access type (And `source` it is not), we use the object type of `target` for better error reporting
            var targetFlags = (target.Flags & TypeFlags.IndexedAccess) != 0 && (source.Flags & TypeFlags.IndexedAccess) == 0
                ? ((IIndexedAccessType)target).ObjectType!.Flags
                : target.Flags;

            if ((targetFlags & TypeFlags.TypeParameter) != 0 && !ReferenceEquals(target, _markerSuperTypeForCheck) && !ReferenceEquals(target, _markerSubTypeForCheck))
            {
                var constraint = GetBaseConstraintOfType(target);
                var needsOriginalSource = false;
                if (constraint is not null && (IsTypeAssignableTo(generalizedSource, constraint) || (needsOriginalSource = IsTypeAssignableTo(source, constraint))))
                {
                    ReportError(
                        Diagnostics._0_is_assignable_to_the_constraint_of_type_1_but_1_could_be_instantiated_with_a_different_subtype_of_constraint_2,
                        needsOriginalSource ? sourceType : generalizedSourceType,
                        targetType,
                        Diagnostic.DeferArg(() => TypeToString(constraint)));
                }
                else
                {
                    errorInfo = null;
                    ReportError(
                        Diagnostics._0_could_be_instantiated_with_an_arbitrary_type_which_could_be_unrelated_to_1,
                        targetType,
                        generalizedSourceType);
                }
            }

            if (message is null)
            {
                if (ReferenceEquals(relation, _comparableRelation))
                {
                    message = Diagnostics.Type_0_is_not_comparable_to_type_1;
                }
                else if (sourceType == targetType)
                {
                    message = Diagnostics.Type_0_is_not_assignable_to_type_1_Two_different_types_with_this_name_exist_but_they_are_unrelated;
                }
                else if (_exactOptionalPropertyTypes == true && GetExactOptionalUnassignableProperties(source, target).Count != 0)
                {
                    message = Diagnostics.Type_0_is_not_assignable_to_type_1_with_exactOptionalPropertyTypes_Colon_true_Consider_adding_undefined_to_the_types_of_the_target_s_properties;
                }
                else
                {
                    if ((source.Flags & TypeFlags.StringLiteral) != 0 && (target.Flags & TypeFlags.Union) != 0)
                    {
                        var suggestedType = GetSuggestedTypeForNonexistentStringLiteralType((IStringLiteralType)source, (IUnionType)target);
                        if (suggestedType is not null)
                        {
                            ReportError(Diagnostics.Type_0_is_not_assignable_to_type_1_Did_you_mean_2, generalizedSourceType, targetType, Diagnostic.DeferArg(() => TypeToString(suggestedType)));
                            return;
                        }
                    }
                    message = Diagnostics.Type_0_is_not_assignable_to_type_1;
                }
            }
            else if (
                ReferenceEquals(message, Diagnostics.Argument_of_type_0_is_not_assignable_to_parameter_of_type_1)
                && _exactOptionalPropertyTypes == true
                && GetExactOptionalUnassignableProperties(source, target).Count != 0
            )
            {
                message = Diagnostics.Argument_of_type_0_is_not_assignable_to_parameter_of_type_1_with_exactOptionalPropertyTypes_Colon_true_Consider_adding_undefined_to_the_types_of_the_target_s_properties;
            }

            ReportError(message, generalizedSourceType, targetType);
        }

        // ported from: src/compiler/checker.ts:22700-22712 — <c>tryElaborateErrorsForPrimitivesAndObjects</c>
        void TryElaborateErrorsForPrimitivesAndObjects(IType source, IType target)
        {
            var sourceType = SymbolValueDeclarationIsContextSensitive(source.Symbol!) ? Diagnostic.DeferArg(() => TypeToString(source, (Node)source.Symbol!.ValueDeclaration!)) : Diagnostic.DeferArg(() => TypeToString(source));
            var targetType = SymbolValueDeclarationIsContextSensitive(target.Symbol!) ? Diagnostic.DeferArg(() => TypeToString(target, (Node)target.Symbol!.ValueDeclaration!)) : Diagnostic.DeferArg(() => TypeToString(target));

            if (
                (ReferenceEquals(_globalStringType, source) && ReferenceEquals(_stringType, target)) ||
                (ReferenceEquals(_globalNumberType, source) && ReferenceEquals(_numberType, target)) ||
                (ReferenceEquals(_globalBooleanType, source) && ReferenceEquals(_booleanType, target)) ||
                (ReferenceEquals(GetGlobalESSymbolType(), source) && ReferenceEquals(_esSymbolType, target))
            )
            {
                ReportError(Diagnostics._0_is_a_primitive_but_1_is_a_wrapper_object_Prefer_using_0_when_possible, targetType, sourceType);
            }
        }

        // ported from: src/compiler/checker.ts:22720-22747 — <c>tryElaborateArrayLikeErrors</c>
        /// <remarks>
        /// PORT-NOTE: the reference's doc comment ("Try and elaborate array and tuple errors. Returns false if we
        /// have found an elaboration, or we should ignore any other elaborations …") is preserved on the JSdoc of
        /// the reference only; the body below is the four-branch spec it states.
        /// </remarks>
        bool TryElaborateArrayLikeErrors(IType source, IType target, bool reportErrors)
        {
            if (IsTupleType(source))
            {
                if (TupleOf((ITupleTypeReference)source).Readonly && IsMutableArrayOrTuple(target))
                {
                    if (reportErrors)
                    {
                        ReportError(Diagnostics.The_type_0_is_readonly_and_cannot_be_assigned_to_the_mutable_type_1, Diagnostic.DeferArg(() => TypeToString(source)), Diagnostic.DeferArg(() => TypeToString(target)));
                    }
                    return false;
                }
                return IsArrayOrTupleType(target);
            }
            if (IsReadonlyArrayType(source) && IsMutableArrayOrTuple(target))
            {
                if (reportErrors)
                {
                    ReportError(Diagnostics.The_type_0_is_readonly_and_cannot_be_assigned_to_the_mutable_type_1, Diagnostic.DeferArg(() => TypeToString(source)), Diagnostic.DeferArg(() => TypeToString(target)));
                }
                return false;
            }
            if (IsTupleType(target))
            {
                return IsArrayType(source);
            }
            return true;
        }

        // ported from: src/compiler/checker.ts:22749-22751 — <c>isRelatedToWorker</c>
        Ternary IsRelatedToWorker(IType source, IType target, bool reportErrors = false)
            => IsRelatedTo(source, target, RecursionFlags.Both, reportErrors);

        // ported from: src/compiler/checker.ts:22759-22872 — <c>isRelatedTo</c>
        Ternary IsRelatedTo(IType originalSource, IType originalTarget, RecursionFlags recursionFlags = RecursionFlags.Both, bool reportErrors = false, DiagnosticMessage? headMessage = null, IntersectionState intersectionState = IntersectionState.None)
        {
            if (ReferenceEquals(originalSource, originalTarget)) return Ternary.True;

            // Before normalization: if `source` is type an object type, and `target` is primitive,
            // skip all the checks we don't need and just return `isSimpleTypeRelatedTo` result
            if ((originalSource.Flags & TypeFlags.Object) != 0 && (originalTarget.Flags & TypeFlags.Primitive) != 0)
            {
                if (
                    ReferenceEquals(relation, _comparableRelation) && (originalTarget.Flags & TypeFlags.Never) == 0 && IsSimpleTypeRelatedTo(originalTarget, originalSource, relation) ||
                    IsSimpleTypeRelatedTo(originalSource, originalTarget, relation, reportErrors ? (m, a) => ReportError(m, a) : null)
                )
                {
                    return Ternary.True;
                }
                if (reportErrors)
                {
                    ReportErrorResults(originalSource, originalTarget, originalSource, originalTarget, headMessage);
                }
                return Ternary.False;
            }

            // Normalize the source and target types: Turn fresh literal types into regular literal types,
            // turn deferred type references into regular type references, simplify indexed access and
            // conditional types, and resolve substitution types to either the substitution (on the source
            // side) or the type variable (on the target side).
            var source = GetNormalizedType(originalSource, /*writing*/ false);
            var target = GetNormalizedType(originalTarget, /*writing*/ true);

            if (ReferenceEquals(source, target)) return Ternary.True;

            if (ReferenceEquals(relation, _identityRelation))
            {
                if (source.Flags != target.Flags) return Ternary.False;
                if ((source.Flags & TypeFlags.Singleton) != 0) return Ternary.True;
                TraceUnionsOrIntersectionsTooLarge(source, target);
                return RecursiveTypeRelatedTo(source, target, /*reportErrors*/ false, IntersectionState.None, recursionFlags);
            }

            // We fastpath comparing a type parameter to exactly its constraint, as this is _super_ common,
            // and otherwise, for type parameters in large unions, causes us to need to compare the union to itself,
            // as we break down the _target_ union first, _then_ get the source constraint - so for every
            // member of the target, we attempt to find a match in the source. This avoids that in cases where
            // the target is exactly the constraint.
            if ((source.Flags & TypeFlags.TypeParameter) != 0 && ReferenceEquals(GetConstraintOfType(source), target))
            {
                return Ternary.True;
            }

            // See if we're relating a definitely non-nullable type to a union that includes null and/or undefined
            // plus a single non-nullable type. If so, remove null and/or undefined from the target type.
            if ((source.Flags & TypeFlags.DefinitelyNonNullable) != 0 && (target.Flags & TypeFlags.Union) != 0)
            {
                var types = ((IUnionType)target).Types!;
                var candidate = types.Count == 2 && (types[0].Flags & TypeFlags.Nullable) != 0 ? types[1] :
                    types.Count == 3 && (types[0].Flags & TypeFlags.Nullable) != 0 && (types[1].Flags & TypeFlags.Nullable) != 0 ? types[2] :
                    null;
                if (candidate is not null && (candidate.Flags & TypeFlags.Nullable) == 0)
                {
                    target = GetNormalizedType(candidate, /*writing*/ true);
                    if (ReferenceEquals(source, target)) return Ternary.True;
                }
            }

            if (
                ReferenceEquals(relation, _comparableRelation) && (target.Flags & TypeFlags.Never) == 0 && IsSimpleTypeRelatedTo(target, source, relation) ||
                IsSimpleTypeRelatedTo(source, target, relation, reportErrors ? (m, a) => ReportError(m, a) : null)
            ) return Ternary.True;

            if ((source.Flags & TypeFlags.StructuredOrInstantiable) != 0 || (target.Flags & TypeFlags.StructuredOrInstantiable) != 0)
            {
                var isPerformingExcessPropertyChecks = (intersectionState & IntersectionState.Target) == 0 && IsObjectLiteralType(source) && (GetObjectFlags(source) & ObjectFlags.FreshLiteral) != 0;
                if (isPerformingExcessPropertyChecks)
                {
                    if (HasExcessProperties(source, target, reportErrors))
                    {
                        if (reportErrors)
                        {
                            ReportRelationError(headMessage, source, originalTarget.AliasSymbol is not null ? originalTarget : target);
                        }
                        return Ternary.False;
                    }
                }

                var isPerformingCommonPropertyChecks = (!ReferenceEquals(relation, _comparableRelation) || IsUnitType(source)) &&
                    (intersectionState & IntersectionState.Target) == 0 &&
                    (source.Flags & (TypeFlags.Primitive | TypeFlags.Object | TypeFlags.Intersection)) != 0 && !ReferenceEquals(source, _globalObjectType) &&
                    (target.Flags & (TypeFlags.Object | TypeFlags.Intersection)) != 0 && IsWeakType(target) &&
                    (GetPropertiesOfType(source).Count > 0 || TypeHasCallOrConstructSignatures(source));
                var isComparingJsxAttributes = (GetObjectFlags(source) & ObjectFlags.JsxAttributes) != 0;
                if (isPerformingCommonPropertyChecks && !HasCommonProperties(source, target, isComparingJsxAttributes))
                {
                    if (reportErrors)
                    {
                        var sourceString = Diagnostic.DeferArg(() => TypeToString(originalSource.AliasSymbol is not null ? originalSource : source));
                        var targetString = Diagnostic.DeferArg(() => TypeToString(originalTarget.AliasSymbol is not null ? originalTarget : target));
                        var calls = GetSignaturesOfType(source, SignatureKind.Call);
                        var constructs = GetSignaturesOfType(source, SignatureKind.Construct);
                        if (
                            calls.Count > 0 && IsRelatedTo(GetReturnTypeOfSignature(calls[0]), target, RecursionFlags.Source, /*reportErrors*/ false) != Ternary.False ||
                            constructs.Count > 0 && IsRelatedTo(GetReturnTypeOfSignature(constructs[0]), target, RecursionFlags.Source, /*reportErrors*/ false) != Ternary.False
                        )
                        {
                            ReportError(Diagnostics.Value_of_type_0_has_no_properties_in_common_with_type_1_Did_you_mean_to_call_it, sourceString, targetString);
                        }
                        else
                        {
                            ReportError(Diagnostics.Type_0_has_no_properties_in_common_with_type_1, sourceString, targetString);
                        }
                    }
                    return Ternary.False;
                }

                TraceUnionsOrIntersectionsTooLarge(source, target);

                var skipCaching = (source.Flags & TypeFlags.Union) != 0 && ((IUnionType)source).Types!.Count < 4 && (target.Flags & TypeFlags.Union) == 0 ||
                    (target.Flags & TypeFlags.Union) != 0 && ((IUnionType)target).Types!.Count < 4 && (source.Flags & TypeFlags.StructuredOrInstantiable) == 0;
                var nestedResult = skipCaching
                    ? UnionOrIntersectionRelatedTo(source, target, reportErrors, intersectionState)
                    : RecursiveTypeRelatedTo(source, target, reportErrors, intersectionState, recursionFlags);
                if (nestedResult != Ternary.False)
                {
                    return nestedResult;
                }
            }

            if (reportErrors)
            {
                ReportErrorResults(originalSource, originalTarget, source, target, headMessage);
            }
            return Ternary.False;
        }
        // ported from: src/compiler/checker.ts:22874-22939 — <c>reportErrorResults</c>
        void ReportErrorResults(IType originalSource, IType originalTarget, IType source, IType target, DiagnosticMessage? headMessage)
        {
            var sourceHasBase = GetSingleBaseForNonAugmentingSubtype(originalSource) is not null;
            var targetHasBase = GetSingleBaseForNonAugmentingSubtype(originalTarget) is not null;
            source = originalSource.AliasSymbol is not null || sourceHasBase ? originalSource : source;
            target = originalTarget.AliasSymbol is not null || targetHasBase ? originalTarget : target;
            var maybeSuppress = overrideNextErrorInfo > 0;
            if (maybeSuppress)
            {
                overrideNextErrorInfo--;
            }
            if ((source.Flags & TypeFlags.Object) != 0 && (target.Flags & TypeFlags.Object) != 0)
            {
                var currentError = errorInfo;
                TryElaborateArrayLikeErrors(source, target, /*reportErrors*/ true);
                if (!ReferenceEquals(errorInfo, currentError))
                {
                    maybeSuppress = errorInfo is not null;
                }
            }
            if ((source.Flags & TypeFlags.Object) != 0 && (target.Flags & TypeFlags.Primitive) != 0)
            {
                TryElaborateErrorsForPrimitivesAndObjects(source, target);
            }
            else if (source.Symbol is not null && (source.Flags & TypeFlags.Object) != 0 && ReferenceEquals(_globalObjectType, source))
            {
                ReportError(Diagnostics.The_Object_type_is_assignable_to_very_few_other_types_Did_you_mean_to_use_the_any_type_instead);
            }
            else if ((GetObjectFlags(source) & ObjectFlags.JsxAttributes) != 0 && (target.Flags & TypeFlags.Intersection) != 0)
            {
                var targetTypes = ((IIntersectionType)target).Types!;
                // PORT-NOTE: `JsxNames` (checker.ts:54227-54235) is a namespace of `__String` constants, each the
                // literal of its own member name, and is not ported yet; this file references it by name and
                // declares nothing, as with the other cross-slice members.
                var intrinsicAttributes = GetJsxType(JsxNames.IntrinsicAttributes, errorNode);
                var intrinsicClassAttributes = GetJsxType(JsxNames.IntrinsicClassAttributes, errorNode);
                if (
                    !IsErrorType(intrinsicAttributes) && !IsErrorType(intrinsicClassAttributes) &&
                    (Core.Contains(targetTypes, intrinsicAttributes) || Core.Contains(targetTypes, intrinsicClassAttributes))
                )
                {
                    // do not report top error
                    return;
                }
            }
            else
            {
                errorInfo = ElaborateNeverIntersection(errorInfo, originalTarget);
            }
            // Used by, eg, missing property checking to replace the top-level message with a more informative one.
            if (headMessage is null && maybeSuppress)
            {
                // We suppress a call to `reportRelationError` or not depending on the state of the type checker, so
                // we call `reportRelationError` here and then undo its effects to figure out what would be the diagnostic
                // if we hadn't supress it, and save that as a canonical diagnostic for deduplication purposes.
                var savedErrorState = CaptureErrorCalculationState();
                ReportRelationError(headMessage, source, target);
                ICanonicalDiagnostic? canonical = null;
                if (errorInfo is not null && !ReferenceEquals(errorInfo, savedErrorState.ErrorInfo))
                {
                    canonical = new CanonicalDiagnosticHead { Code = errorInfo.Code, MessageText = errorInfo.MessageText };
                }
                ResetErrorInfo(savedErrorState);
                if (canonical is not null && errorInfo is not null)
                {
                    errorInfo.CanonicalHead = canonical;
                }

                lastSkippedInfo = (source, target);
                return;
            }
            ReportRelationError(headMessage, source, target);
            if ((source.Flags & TypeFlags.TypeParameter) != 0 && source.Symbol?.Declarations is { Count: > 0 } declarations && GetConstraintOfType(source) is null)
            {
                var syntheticParam = CloneTypeParameter((ITypeParameter)source);
                syntheticParam.Constraint = InstantiateType(target, MakeUnaryTypeMapper(source, syntheticParam));
                if (HasNonCircularBaseConstraint(syntheticParam))
                {
                    var targetConstraintString = Diagnostic.DeferArg(() => TypeToString(target, (Node)declarations[0]));
                    AssociateRelatedInfo(Utilities.CreateDiagnosticForNode((Node)declarations[0], Diagnostics.This_type_parameter_might_need_an_extends_0_constraint, targetConstraintString));
                }
            }
        }

        // ported from: src/compiler/checker.ts:22941-22968 — <c>traceUnionsOrIntersectionsTooLarge</c>
        void TraceUnionsOrIntersectionsTooLarge(IType source, IType target)
        {
            var tracing = Tracing.Current;
            if (tracing is null)
            {
                return;
            }

            if ((source.Flags & TypeFlags.UnionOrIntersection) != 0 && (target.Flags & TypeFlags.UnionOrIntersection) != 0)
            {
                var sourceUnionOrIntersection = (IUnionOrIntersectionType)source;
                var targetUnionOrIntersection = (IUnionOrIntersectionType)target;

                if ((sourceUnionOrIntersection.ObjectFlags & targetUnionOrIntersection.ObjectFlags & ObjectFlags.PrimitiveUnion) != 0)
                {
                    // There's a fast path for comparing primitive unions
                    return;
                }

                var sourceSize = sourceUnionOrIntersection.Types!.Count;
                var targetSize = targetUnionOrIntersection.Types!.Count;
                if (sourceSize * targetSize > 1E6)
                {
                    tracing.Instant(
                        Tracing.Phase.CheckTypes,
                        "traceUnionsOrIntersectionsTooLarge_DepthLimit",
                        TracingArgs.Of(
                            ("sourceId", source.Id),
                            ("sourceSize", sourceSize),
                            ("targetId", target.Id),
                            ("targetSize", targetSize),
                            ("pos", errorNode?.Pos),
                            ("end", errorNode?.End)));
                }
            }
        }

        // ported from: src/compiler/checker.ts:22970-22978 — <c>getTypeOfPropertyInTypes</c>
        IType GetTypeOfPropertyInTypes(IList<IType> types, string name)
        {
            // PORT-NOTE: TS's `reduceLeft(types, appendPropType, /*initial*/ undefined) || emptyArray` treats
            // "no accumulator yet" as its own state; an empty accumulator is indistinguishable here because
            // `append(undefined, x)` and `append([], x)` both produce `[x]`, so the port starts from `[]`.
            IList<IType> AppendPropType(IList<IType> propTypes, IType type, int _)
            {
                type = GetApparentType(type);
                var prop = (type.Flags & TypeFlags.UnionOrIntersection) != 0 ? GetPropertyOfUnionOrIntersectionType((UnionOrIntersectionType)type, name) : GetPropertyOfObjectType(type, name);
                var propType = prop is not null ? GetTypeOfSymbol(prop) : GetApplicableIndexInfoForName(type, name)?.Type ?? _undefinedType;
                return Core.Append(propTypes, propType)!;
            }

            return GetUnionType([.. Core.ReduceLeft<IType, IList<IType>>(types, AppendPropType, new List<IType>())]);
        }

        // ported from: src/compiler/checker.ts:22980-23060 — <c>hasExcessProperties</c>
        /// <remarks>
        /// PORT-NOTE: the reference's first parameter is <c>FreshObjectLiteralType</c> because its only caller
        /// narrows with <c>isObjectLiteralType(source) &amp;&amp; getObjectFlags(source) &amp; ObjectFlags.FreshLiteral</c>
        /// first; nothing in the body uses a member beyond <c>flags</c>/<c>symbol</c>, so the port takes
        /// <see cref="IType"/> and the caller keeps its <c>is not null</c> test instead of a downcast.
        /// </remarks>
        bool HasExcessProperties(IType source, IType target, bool reportErrors)
        {
            if (!IsExcessPropertyCheckTarget(target) || !_noImplicitAny && (GetObjectFlags(target) & ObjectFlags.JSLiteral) != 0)
            {
                return false; // Disable excess property checks on JS literals to simulate having an implicit "index signature" - but only outside of noImplicitAny
            }
            var isComparingJsxAttributes = (GetObjectFlags(source) & ObjectFlags.JsxAttributes) != 0;
            if (
                (ReferenceEquals(relation, _assignableRelation) || ReferenceEquals(relation, _comparableRelation)) &&
                (IsTypeSubsetOf(_globalObjectType!, target) || (!isComparingJsxAttributes && IsEmptyObjectType(target)))
            )
            {
                return false;
            }
            var reducedTarget = target;
            List<IType>? checkTypes = null;
            if ((target.Flags & TypeFlags.Union) != 0)
            {
                reducedTarget = FindMatchingDiscriminantType(source, target, (s, t, reportErrors) => IsRelatedTo(s, t, RecursionFlags.Both, reportErrors)) ?? FilterPrimitivesIfContainsNonPrimitive((IUnionType)target);
                checkTypes = (reducedTarget.Flags & TypeFlags.Union) != 0 ? ((IUnionType)reducedTarget).Types : [reducedTarget];
            }
            foreach (var prop in GetPropertiesOfType(source))
            {
                if (ShouldCheckAsExcessProperty(prop, source.Symbol!) && !IsIgnoredJsxProperty(source, prop))
                {
                    if (!IsKnownProperty(reducedTarget, prop.EscapedName!, isComparingJsxAttributes))
                    {
                        if (reportErrors)
                        {
                            // Report error in terms of object types in the target as those are the only ones
                            // we check in isKnownProperty.
                            var errorTarget = FilterType(reducedTarget, IsExcessPropertyCheckTarget);
                            // We know *exactly* where things went wrong when comparing the types.
                            // Use this property as the error node as this will be more helpful in
                            // reasoning about what went wrong.
                            if (errorNode is null) return Debug.Fail<bool>();
                            if (NodeTests.IsJsxAttributes(errorNode) || Utilities.IsJsxOpeningLikeElement(errorNode) || Utilities.IsJsxOpeningLikeElement(errorNode.Parent!))
                            {
                                // JsxAttributes has an object-literal flag and undergo same type-assignablity check as normal object-literal.
                                // However, using an object-literal error message will be very confusing to the users so we give different a message.
                                // PORT-NOTE: TS's `isJsxAttribute` type predicate narrows `prop.valueDeclaration` to
                                // `JsxAttribute`, whose `name` slot lives on that class alone (release-6.0's
                                // `JsxAttribute extends Declaration`, not `NamedDeclaration`) — the cast names
                                // <see cref="JsxAttribute"/>, not <see cref="INamedDeclaration"/>.
                                if (prop.ValueDeclaration is not null && NodeTests.IsJsxAttribute((Node)prop.ValueDeclaration) && ReferenceEquals(Utilities.GetSourceFileOfNode(errorNode), Utilities.GetSourceFileOfNode(((JsxAttribute)prop.ValueDeclaration).Name!)))
                                {
                                    // Note that extraneous children (as in `<NoChild>extra</NoChild>`) don't pass this check,
                                    // since `children` is a SyntaxKind.PropertySignature instead of a SyntaxKind.JsxAttribute.
                                    errorNode = ((JsxAttribute)prop.ValueDeclaration).Name!;
                                }
                                var propName = SymbolToString(prop);
                                var suggestionSymbol = GetSuggestedSymbolForNonexistentJSXAttribute(propName, errorTarget);
                                var suggestion = suggestionSymbol is not null ? Diagnostic.DeferArg(() => SymbolToString(suggestionSymbol)) : null;
                                if (suggestion is not null)
                                {
                                    ReportError(Diagnostics.Property_0_does_not_exist_on_type_1_Did_you_mean_2, propName, Diagnostic.DeferArg(() => TypeToString(errorTarget)), suggestion);
                                }
                                else
                                {
                                    ReportError(Diagnostics.Property_0_does_not_exist_on_type_1, propName, Diagnostic.DeferArg(() => TypeToString(errorTarget)));
                                }
                            }
                            else
                            {
                                // use the property's value declaration if the property is assigned inside the literal itself
                                var objectLiteralDeclaration = source.Symbol?.Declarations is { Count: > 0 } symbolDeclarations ? symbolDeclarations[0] : null;
                                string? suggestion = null;
                                if (prop.ValueDeclaration is not null && Utilities.FindAncestor((Node)prop.ValueDeclaration, d => ReferenceEquals(d, objectLiteralDeclaration)) is not null && ReferenceEquals(Utilities.GetSourceFileOfNode((Node)objectLiteralDeclaration!), Utilities.GetSourceFileOfNode(errorNode)))
                                {
                                    var propDeclaration = (IObjectLiteralElement)prop.ValueDeclaration;
                                    Debug.AssertNode((Node)prop.ValueDeclaration, Utilities.IsObjectLiteralElementLike);

                                    var name = propDeclaration.Name!;
                                    errorNode = (Node)name;

                                    if (NodeTests.IsIdentifier((Node)name))
                                    {
                                        suggestion = GetSuggestionForNonexistentProperty(name, errorTarget);
                                    }
                                }
                                if (suggestion is not null)
                                {
                                    ReportParentSkippedError(Diagnostics.Object_literal_may_only_specify_known_properties_but_0_does_not_exist_in_type_1_Did_you_mean_to_write_2, Diagnostic.DeferArg(() => SymbolToString(prop)), Diagnostic.DeferArg(() => TypeToString(errorTarget)), suggestion);
                                }
                                else
                                {
                                    ReportParentSkippedError(Diagnostics.Object_literal_may_only_specify_known_properties_and_0_does_not_exist_in_type_1, Diagnostic.DeferArg(() => SymbolToString(prop)), Diagnostic.DeferArg(() => TypeToString(errorTarget)));
                                }
                            }
                        }
                        return true;
                    }
                    if (checkTypes is not null && IsRelatedTo(GetTypeOfSymbol(prop), GetTypeOfPropertyInTypes(checkTypes, prop.EscapedName!), RecursionFlags.Both, reportErrors) == Ternary.False)
                    {
                        if (reportErrors)
                        {
                            ReportIncompatibleError(Diagnostics.Types_of_property_0_are_incompatible, Diagnostic.DeferArg(() => SymbolToString(prop)));
                        }
                        return true;
                    }
                }
            }
            return false;
        }

        // ported from: src/compiler/checker.ts:23062-23064 — <c>shouldCheckAsExcessProperty</c>
        bool ShouldCheckAsExcessProperty(ISymbol prop, ISymbol container)
            => prop.ValueDeclaration is not null && container.ValueDeclaration is not null && ReferenceEquals(((Node)prop.ValueDeclaration).Parent, container.ValueDeclaration);

        // ported from: src/compiler/checker.ts:23066-23120 — <c>unionOrIntersectionRelatedTo</c>
        Ternary UnionOrIntersectionRelatedTo(IType source, IType target, bool reportErrors, IntersectionState intersectionState)
        {
            // Note that these checks are specifically ordered to produce correct results. In particular,
            // we need to deconstruct unions before intersections (because unions are always at the top),
            // and we need to handle "each" relations before "some" relations for the same kind of type.
            if ((source.Flags & TypeFlags.Union) != 0)
            {
                if ((target.Flags & TypeFlags.Union) != 0)
                {
                    // Intersections of union types are normalized into unions of intersection types, and such normalized
                    // unions can get very large and expensive to relate. The following fast path checks if the source union
                    // originated in an intersection. If so, and if that intersection contains the target type, then we know
                    // the result to be true (for any two types A and B, A & B is related to both A and B).
                    var sourceOrigin = ((IUnionType)source).Origin;
                    if (sourceOrigin is not null && (sourceOrigin.Flags & TypeFlags.Intersection) != 0 && target.AliasSymbol is not null && Core.Contains(((IIntersectionType)sourceOrigin).Types!, target))
                    {
                        return Ternary.True;
                    }
                    // Similarly, in unions of unions the we preserve the original list of unions. This original list is often
                    // much shorter than the normalized result, so we scan it in the following fast path.
                    var targetOrigin = ((IUnionType)target).Origin;
                    if (targetOrigin is not null && (targetOrigin.Flags & TypeFlags.Union) != 0 && source.AliasSymbol is not null && Core.Contains(((IUnionType)targetOrigin).Types!, source))
                    {
                        return Ternary.True;
                    }
                }
                return ReferenceEquals(relation, _comparableRelation) ?
                    SomeTypeRelatedToType((IUnionOrIntersectionType)source, target, reportErrors && (source.Flags & TypeFlags.Primitive) == 0, intersectionState) :
                    EachTypeRelatedToType((IUnionOrIntersectionType)source, target, reportErrors && (source.Flags & TypeFlags.Primitive) == 0, intersectionState);
            }
            if ((target.Flags & TypeFlags.Union) != 0)
            {
                return TypeRelatedToSomeType(GetRegularTypeOfObjectLiteral(source), (IUnionOrIntersectionType)target, reportErrors && (source.Flags & TypeFlags.Primitive) == 0 && (target.Flags & TypeFlags.Primitive) == 0, intersectionState);
            }
            if ((target.Flags & TypeFlags.Intersection) != 0)
            {
                return TypeRelatedToEachType(source, (IIntersectionType)target, reportErrors, IntersectionState.Target);
            }
            // Source is an intersection. For the comparable relation, if the target is a primitive type we hoist the
            // constraints of all non-primitive types in the source into a new intersection. We do this because the
            // intersection may further constrain the constraints of the non-primitive types. For example, given a type
            // parameter 'T extends 1 | 2', the intersection 'T & 1' should be reduced to '1' such that it doesn't
            // appear to be comparable to '2'.
            if (ReferenceEquals(relation, _comparableRelation) && (target.Flags & TypeFlags.Primitive) != 0)
            {
                var constraints = Core.SameMap(((IIntersectionType)source).Types!, (t, _) => (t.Flags & TypeFlags.Instantiable) != 0 ? GetBaseConstraintOfType(t) ?? _unknownType : t);
                if (!ReferenceEquals(constraints, ((IIntersectionType)source).Types))
                {
                    source = GetIntersectionType([.. constraints!]);
                    if ((source.Flags & TypeFlags.Never) != 0)
                    {
                        return Ternary.False;
                    }
                    if ((source.Flags & TypeFlags.Intersection) == 0)
                    {
                        return IsRelatedTo(source, target, RecursionFlags.Source, /*reportErrors*/ false) != Ternary.False ||
                            IsRelatedTo(target, source, RecursionFlags.Source, /*reportErrors*/ false) != Ternary.False
                            ? Ternary.True : Ternary.False;
                    }
                }
            }
            // Check to see if any constituents of the intersection are immediately related to the target.
            // Don't report errors though. Elaborating on whether a source constituent is related to the target is
            // not actually useful and leads to some confusing error messages. Instead, we rely on the caller
            // checking whether the full intersection viewed as an object is related to the target.
            return SomeTypeRelatedToType((IUnionOrIntersectionType)source, target, /*reportErrors*/ false, IntersectionState.Source);
        }

        // ported from: src/compiler/checker.ts:23122-23133 — <c>eachTypeRelatedToSomeType</c>
        Ternary EachTypeRelatedToSomeType(IUnionOrIntersectionType source, IUnionOrIntersectionType target)
        {
            var result = Ternary.True;
            var sourceTypes = source.Types!;
            foreach (var sourceType in sourceTypes)
            {
                var related = TypeRelatedToSomeType(sourceType, target, /*reportErrors*/ false, IntersectionState.None);
                if (related == Ternary.False)
                {
                    return Ternary.False;
                }
                result = And(result, related);
            }
            return result;
        }

        // ported from: src/compiler/checker.ts:23135-23182 — <c>typeRelatedToSomeType</c>
        Ternary TypeRelatedToSomeType(IType source, IUnionOrIntersectionType target, bool reportErrors, IntersectionState intersectionState)
        {
            var targetTypes = target.Types!;
            if ((target.Flags & TypeFlags.Union) != 0)
            {
                if (ContainsType(targetTypes, source))
                {
                    return Ternary.True;
                }
                if (
                    !ReferenceEquals(relation, _comparableRelation) && (GetObjectFlags(target) & ObjectFlags.PrimitiveUnion) != 0 && (source.Flags & TypeFlags.EnumLiteral) == 0 && (
                        (source.Flags & (TypeFlags.StringLiteral | TypeFlags.BooleanLiteral | TypeFlags.BigIntLiteral)) != 0 ||
                        (ReferenceEquals(relation, _subtypeRelation) || ReferenceEquals(relation, _strictSubtypeRelation)) && (source.Flags & TypeFlags.NumberLiteral) != 0
                    )
                )
                {
                    // When relating a literal type to a union of primitive types, we know the relation is false unless
                    // the union contains the base primitive type or the literal type in one of its fresh/regular forms.
                    // We exclude numeric literals for non-subtype relations because numeric literals are assignable to
                    // numeric enum literals with the same value. Similarly, we exclude enum literal types because
                    // identically named enum types are related (see isEnumTypeRelatedTo). We exclude the comparable
                    // relation in entirety because it needs to be checked in both directions.
                    var alternateForm = ReferenceEquals(source, ((IFreshableType)source).RegularType) ? ((IFreshableType)source).FreshType : ((IFreshableType)source).RegularType;
                    var primitive = (source.Flags & TypeFlags.StringLiteral) != 0 ? _stringType :
                        (source.Flags & TypeFlags.NumberLiteral) != 0 ? _numberType :
                        (source.Flags & TypeFlags.BigIntLiteral) != 0 ? _bigintType :
                        null;
                    return primitive is not null && ContainsType(targetTypes, primitive) || alternateForm is not null && ContainsType(targetTypes, alternateForm) ? Ternary.True : Ternary.False;
                }
                var match = GetMatchingUnionConstituentForType((IUnionType)target, source);
                if (match is not null)
                {
                    var related = IsRelatedTo(source, match, RecursionFlags.Target, /*reportErrors*/ false, /*headMessage*/ null, intersectionState);
                    if (related != Ternary.False)
                    {
                        return related;
                    }
                }
            }
            foreach (var type in targetTypes)
            {
                var related = IsRelatedTo(source, type, RecursionFlags.Target, /*reportErrors*/ false, /*headMessage*/ null, intersectionState);
                if (related != Ternary.False)
                {
                    return related;
                }
            }
            if (reportErrors)
            {
                // Elaborate only if we can find a best matching type in the target union
                var bestMatchingType = GetBestMatchingType(source, target, (s, t, reportErrors) => IsRelatedTo(s, t, RecursionFlags.Both, reportErrors));
                if (bestMatchingType is not null)
                {
                    IsRelatedTo(source, bestMatchingType, RecursionFlags.Target, /*reportErrors*/ true, /*headMessage*/ null, intersectionState);
                }
            }
            return Ternary.False;
        }

        // ported from: src/compiler/checker.ts:23184-23195 — <c>typeRelatedToEachType</c>
        Ternary TypeRelatedToEachType(IType source, IIntersectionType target, bool reportErrors, IntersectionState intersectionState)
        {
            var result = Ternary.True;
            var targetTypes = target.Types!;
            foreach (var targetType in targetTypes)
            {
                var related = IsRelatedTo(source, targetType, RecursionFlags.Target, reportErrors, /*headMessage*/ null, intersectionState);
                if (related == Ternary.False)
                {
                    return Ternary.False;
                }
                result = And(result, related);
            }
            return result;
        }

        // ported from: src/compiler/checker.ts:23197-23210 — <c>someTypeRelatedToType</c>
        Ternary SomeTypeRelatedToType(IUnionOrIntersectionType source, IType target, bool reportErrors, IntersectionState intersectionState)
        {
            var sourceTypes = source.Types!;
            if ((source.Flags & TypeFlags.Union) != 0 && ContainsType(sourceTypes, target))
            {
                return Ternary.True;
            }
            var len = sourceTypes.Count;
            for (var i = 0; i < len; i++)
            {
                var related = IsRelatedTo(sourceTypes[i], target, RecursionFlags.Source, reportErrors && i == len - 1, /*headMessage*/ null, intersectionState);
                if (related != Ternary.False)
                {
                    return related;
                }
            }
            return Ternary.False;
        }

        // ported from: src/compiler/checker.ts:23212-23220 — <c>getUndefinedStrippedTargetIfNeeded</c>
        IType GetUndefinedStrippedTargetIfNeeded(IType source, IType target)
        {
            if (
                (source.Flags & TypeFlags.Union) != 0 && (target.Flags & TypeFlags.Union) != 0 &&
                (((IUnionType)source).Types![0].Flags & TypeFlags.Undefined) == 0 && (((IUnionType)target).Types![0].Flags & TypeFlags.Undefined) != 0
            )
            {
                return ExtractTypesOfKind(target, ~TypeFlags.Undefined);
            }
            return target;
        }

        // ported from: src/compiler/checker.ts:23222-23249 — <c>eachTypeRelatedToType</c>
        Ternary EachTypeRelatedToType(IUnionOrIntersectionType source, IType target, bool reportErrors, IntersectionState intersectionState)
        {
            var result = Ternary.True;
            var sourceTypes = source.Types!;
            // We strip `undefined` from the target if the `source` trivially doesn't contain it for our correspondence-checking fastpath
            // since `undefined` is frequently added by optionality and would otherwise spoil a potentially useful correspondence
            var undefinedStrippedTarget = GetUndefinedStrippedTargetIfNeeded(source, target);
            for (var i = 0; i < sourceTypes.Count; i++)
            {
                var sourceType = sourceTypes[i];
                if ((undefinedStrippedTarget.Flags & TypeFlags.Union) != 0 && sourceTypes.Count >= ((IUnionType)undefinedStrippedTarget).Types!.Count && sourceTypes.Count % ((IUnionType)undefinedStrippedTarget).Types!.Count == 0)
                {
                    // many unions are mappings of one another; in such cases, simply comparing members at the same index can shortcut the comparison
                    // such unions will have identical lengths, and their corresponding elements will match up. Another common scenario is where a large
                    // union has a union of objects intersected with it. In such cases, if the input was, eg `("a" | "b" | "c") & (string | boolean | {} | {whatever})`,
                    // the result will have the structure `"a" | "b" | "c" | "a" & {} | "b" & {} | "c" & {} | "a" & {whatever} | "b" & {whatever} | "c" & {whatever}`
                    // - the resulting union has a length which is a multiple of the original union, and the elements correspond modulo the length of the original union
                    var related = IsRelatedTo(sourceType, ((IUnionType)undefinedStrippedTarget).Types![i % ((IUnionType)undefinedStrippedTarget).Types!.Count], RecursionFlags.Both, /*reportErrors*/ false, /*headMessage*/ null, intersectionState);
                    if (related != Ternary.False)
                    {
                        result = And(result, related);
                        continue;
                    }
                }
                var relatedToTarget = IsRelatedTo(sourceType, target, RecursionFlags.Source, reportErrors, /*headMessage*/ null, intersectionState);
                if (relatedToTarget == Ternary.False)
                {
                    return Ternary.False;
                }
                result = And(result, relatedToTarget);
            }
            return result;
        }

        // ported from: src/compiler/checker.ts:23251-23312 — <c>typeArgumentsRelatedTo</c>
        /// <remarks>
        /// PORT-NOTE: TS defaults the first three parameters to the shared <c>emptyArray</c>; C# cannot put optional
        /// parameters before the two required ones, so the port keeps them required and normalizes <c>null</c> to an
        /// empty list on entry — the reference's callers pass <c>undefined</c> through, which is the same event.
        /// </remarks>
        Ternary TypeArgumentsRelatedTo(IList<IType>? sources, IList<IType>? targets, List<VarianceFlags>? variances, bool reportErrors, IntersectionState intersectionState)
        {
            sources ??= [];
            targets ??= [];
            variances ??= [];
            if (sources.Count != targets.Count && ReferenceEquals(relation, _identityRelation))
            {
                return Ternary.False;
            }
            var length = sources.Count <= targets.Count ? sources.Count : targets.Count;
            var result = Ternary.True;
            for (var i = 0; i < length; i++)
            {
                // When variance information isn't available we default to covariance. This happens
                // in the process of computing variance information for recursive types and when
                // comparing 'this' type arguments.
                var varianceFlags = i < variances.Count ? variances[i] : VarianceFlags.Covariant;
                var variance = varianceFlags & VarianceFlags.VarianceMask;
                // We ignore arguments for independent type parameters (because they're never witnessed).
                if (variance != VarianceFlags.Independent)
                {
                    var s = sources[i];
                    var t = targets[i];
                    var related = Ternary.True;
                    if ((varianceFlags & VarianceFlags.Unmeasurable) != 0)
                    {
                        // Even an `Unmeasurable` variance works out without a structural check if the source and target are _identical_.
                        // We can't simply assume invariance, because `Unmeasurable` marks nonlinear relations, for example, a relation tained by
                        // the `-?` modifier in a mapped type (where, no matter how the inputs are related, the outputs still might not be)
                        related = ReferenceEquals(relation, _identityRelation) ? IsRelatedTo(s, t, RecursionFlags.Both, /*reportErrors*/ false) : CompareTypesIdentical(s, t);
                    }
                    else
                    {
                        // Propagate unreliable variance flag
                        if (_inVarianceComputation && (varianceFlags & VarianceFlags.Unreliable) != 0)
                        {
                            InstantiateType(s, _reportUnreliableMapper);
                        }
                        if (variance == VarianceFlags.Covariant)
                        {
                            related = IsRelatedTo(s, t, RecursionFlags.Both, reportErrors, /*headMessage*/ null, intersectionState);
                        }
                        else if (variance == VarianceFlags.Contravariant)
                        {
                            related = IsRelatedTo(t, s, RecursionFlags.Both, reportErrors, /*headMessage*/ null, intersectionState);
                        }
                        else if (variance == VarianceFlags.Bivariant)
                        {
                            // In the bivariant case we first compare contravariantly without reporting
                            // errors. Then, if that doesn't succeed, we compare covariantly with error
                            // reporting. Thus, error elaboration will be based on the the covariant check,
                            // which is generally easier to reason about.
                            related = IsRelatedTo(t, s, RecursionFlags.Both, /*reportErrors*/ false);
                            if (related == Ternary.False)
                            {
                                related = IsRelatedTo(s, t, RecursionFlags.Both, reportErrors, /*headMessage*/ null, intersectionState);
                            }
                        }
                        else
                        {
                            // In the invariant case we first compare covariantly, and only when that
                            // succeeds do we proceed to compare contravariantly. Thus, error elaboration
                            // will typically be based on the covariant check.
                            related = IsRelatedTo(s, t, RecursionFlags.Both, reportErrors, /*headMessage*/ null, intersectionState);
                            if (related != Ternary.False)
                            {
                                related = And(related, IsRelatedTo(t, s, RecursionFlags.Both, reportErrors, /*headMessage*/ null, intersectionState));
                            }
                        }
                    }
                    if (related == Ternary.False)
                    {
                        return Ternary.False;
                    }
                    result = And(result, related);
                }
            }
            return result;
        }

        // ported from: src/compiler/checker.ts:23319-23467 — <c>recursiveTypeRelatedTo</c>
        /// <remarks>
        /// PORT-NOTE: TS tests the cache with <c>entry !== undefined</c>. <c>Dictionary</c> cannot hand back a
        /// <c>null</c> for a struct value, and <c>RelationComparisonResult.None</c> is a value a writer never stores
        /// (both writers OR in <c>Succeeded</c> or <c>Failed</c>), so the <c>TryGetValue</c> form distinguishes
        /// "absent" exactly as the reference does.
        /// </remarks>
        Ternary RecursiveTypeRelatedTo(IType source, IType target, bool reportErrors, IntersectionState intersectionState, RecursionFlags recursionFlags)
        {
            if (overflow)
            {
                return Ternary.False;
            }
            var id = GetRelationKey(source, target, intersectionState, relation, /*ignoreConstraints*/ false);
            if (relation.TryGetValue(id, out var entry))
            {
                if (reportErrors && (entry & RelationComparisonResult.Failed) != 0 && (entry & RelationComparisonResult.Overflow) == 0)
                {
                    // We are elaborating errors and the cached result is a failure not due to a comparison overflow,
                    // so we will do the comparison again to generate an error message.
                }
                else
                {
                    if (_outofbandVarianceMarkerHandler is not null)
                    {
                        // We're in the middle of variance checking - integrate any unmeasurable/unreliable flags from this cached component
                        var saved = entry & RelationComparisonResult.ReportsMask;
                        if ((saved & RelationComparisonResult.ReportsUnmeasurable) != 0)
                        {
                            InstantiateType(source, _reportUnmeasurableMapper);
                        }
                        if ((saved & RelationComparisonResult.ReportsUnreliable) != 0)
                        {
                            InstantiateType(source, _reportUnreliableMapper);
                        }
                    }
                    if (reportErrors && (entry & RelationComparisonResult.Overflow) != 0)
                    {
                        var message = (entry & RelationComparisonResult.ComplexityOverflow) != 0 ?
                            Diagnostics.Excessive_complexity_comparing_types_0_and_1 :
                            Diagnostics.Excessive_stack_depth_comparing_types_0_and_1;
                        ReportError(message, Diagnostic.DeferArg(() => TypeToString(source)), Diagnostic.DeferArg(() => TypeToString(target)));
                        overrideNextErrorInfo++;
                    }
                    return (entry & RelationComparisonResult.Succeeded) != 0 ? Ternary.True : Ternary.False;
                }
            }
            if (relationCount <= 0)
            {
                overflow = true;
                return Ternary.False;
            }
            if (maybeKeys is null)
            {
                maybeKeys = [];
                maybeKeysSet = [];
                sourceStack = [];
                targetStack = [];
            }
            else
            {
                // If source and target are already being compared, consider them related with assumptions
                if (maybeKeysSet!.Contains(id))
                {
                    return Ternary.Maybe;
                }

                // A key that starts with "*" is an indication that we have type references that reference constrained
                // type parameters. For such keys we also check against the key we would have gotten if all type parameters
                // were unconstrained.
                var broadestEquivalentId = id.StartsWith('*') ? GetRelationKey(source, target, intersectionState, relation, /*ignoreConstraints*/ true) : null;
                if (broadestEquivalentId is not null && maybeKeysSet!.Contains(broadestEquivalentId))
                {
                    return Ternary.Maybe;
                }

                if (sourceDepth == 100 || targetDepth == 100)
                {
                    overflow = true;
                    return Ternary.False;
                }
            }
            var maybeStart = maybeCount;
            SetAt(maybeKeys!, maybeCount, id);
            maybeKeysSet!.Add(id);
            maybeCount++;
            var saveExpandingFlags = expandingFlags;
            if ((recursionFlags & RecursionFlags.Source) != 0)
            {
                SetAt(sourceStack!, sourceDepth, source);
                sourceDepth++;
                if ((expandingFlags & ExpandingFlags.Source) == 0 && IsDeeplyNestedType(source, sourceStack!, sourceDepth)) expandingFlags |= ExpandingFlags.Source;
            }
            if ((recursionFlags & RecursionFlags.Target) != 0)
            {
                SetAt(targetStack!, targetDepth, target);
                targetDepth++;
                if ((expandingFlags & ExpandingFlags.Target) == 0 && IsDeeplyNestedType(target, targetStack!, targetDepth)) expandingFlags |= ExpandingFlags.Target;
            }
            Action<bool>? originalHandler = null;
            var propagatingVarianceFlags = RelationComparisonResult.None;
            if (_outofbandVarianceMarkerHandler is not null)
            {
                originalHandler = _outofbandVarianceMarkerHandler;
                _outofbandVarianceMarkerHandler = onlyUnreliable =>
                {
                    propagatingVarianceFlags |= onlyUnreliable ? RelationComparisonResult.ReportsUnreliable : RelationComparisonResult.ReportsUnmeasurable;
                    originalHandler!(onlyUnreliable);
                };
            }

            Ternary result;
            if (expandingFlags == ExpandingFlags.Both)
            {
                Tracing.Current?.Instant(
                    Tracing.Phase.CheckTypes,
                    "recursiveTypeRelatedTo_DepthLimit",
                    TracingArgs.Of(
                        ("sourceId", source.Id),
                        ("sourceIdStack", TracingValue.Array(Core.Map<IType, TracingValue>(sourceStack!, (t, _) => TracingValue.From((double)t.Id)))),
                        ("targetId", target.Id),
                        ("targetIdStack", TracingValue.Array(Core.Map<IType, TracingValue>(targetStack!, (t, _) => TracingValue.From((double)t.Id)))),
                        ("depth", sourceDepth),
                        ("targetDepth", targetDepth)));
                result = Ternary.Maybe;
            }
            else
            {
                Tracing.Current?.Push(Tracing.Phase.CheckTypes, "structuredTypeRelatedTo", TracingArgs.Of(("sourceId", source.Id), ("targetId", target.Id)));
                result = StructuredTypeRelatedTo(source, target, reportErrors, intersectionState);
                Tracing.Current?.Pop();
            }

            if (_outofbandVarianceMarkerHandler is not null)
            {
                _outofbandVarianceMarkerHandler = originalHandler;
            }
            if ((recursionFlags & RecursionFlags.Source) != 0)
            {
                sourceDepth--;
            }
            if ((recursionFlags & RecursionFlags.Target) != 0)
            {
                targetDepth--;
            }
            expandingFlags = saveExpandingFlags;
            if (result != Ternary.False)
            {
                if (result == Ternary.True || (sourceDepth == 0 && targetDepth == 0))
                {
                    if (result == Ternary.True || result == Ternary.Maybe)
                    {
                        // If result is definitely true, record all maybe keys as having succeeded. Also, record Ternary.Maybe
                        // results as having succeeded once we reach depth 0, but never record Ternary.Unknown results.
                        ResetMaybeStack(/*markAllAsSucceeded*/ true);
                    }
                    else
                    {
                        ResetMaybeStack(/*markAllAsSucceeded*/ false);
                    }
                }
                // Note: it's intentional that we don't reset in the else case;
                // we leave them on the stack such that when we hit depth zero
                // above, we can report all of them as successful.
            }
            else
            {
                // A false result goes straight into global cache (when something is false under
                // assumptions it will also be false without assumptions)
                relation[id] = RelationComparisonResult.Failed | propagatingVarianceFlags;
                relationCount--;
                ResetMaybeStack(/*markAllAsSucceeded*/ false);
            }
            return result;

            // ported from: src/compiler/checker.ts:23457-23466 — <c>resetMaybeStack</c>
            void ResetMaybeStack(bool markAllAsSucceeded)
            {
                for (var i = maybeStart; i < maybeCount; i++)
                {
                    maybeKeysSet!.Remove(maybeKeys![i]);
                    if (markAllAsSucceeded)
                    {
                        relation[maybeKeys![i]] = RelationComparisonResult.Succeeded | propagatingVarianceFlags;
                        relationCount--;
                    }
                }
                maybeCount = maybeStart;
            }
        }

        // ported from: src/compiler/checker.ts:23469-23530 — <c>structuredTypeRelatedTo</c>
        Ternary StructuredTypeRelatedTo(IType source, IType target, bool reportErrors, IntersectionState intersectionState)
        {
            var saveErrorInfo = CaptureErrorCalculationState();
            var result = StructuredTypeRelatedToWorker(source, target, reportErrors, intersectionState, saveErrorInfo);
            if (!ReferenceEquals(relation, _identityRelation))
            {
                // The combined constraint of an intersection type is the intersection of the constraints of
                // the constituents. When an intersection type contains instantiable types with union type
                // constraints, there are situations where we need to examine the combined constraint. One is
                // when the target is a union type. Another is when the intersection contains types belonging
                // to one of the disjoint domains. For example, given type variables T and U, each with the
                // constraint 'string | number', the combined constraint of 'T & U' is 'string | number' and
                // we need to check this constraint against a union on the target side. Also, given a type
                // variable V constrained to 'string | number', 'V & number' has a combined constraint of
                // 'string & number | number & number' which reduces to just 'number'.
                // This also handles type parameters, as a type parameter with a union constraint compared against a union
                // needs to have its constraint hoisted into an intersection with said type parameter, this way
                // the type param can be compared with itself in the target (with the influence of its constraint to match other parts)
                // For example, if `T extends 1 | 2` and `U extends 2 | 3` and we compare `T & U` to `T & U & (1 | 2 | 3)`
                if (result == Ternary.False && ((source.Flags & TypeFlags.Intersection) != 0 || (source.Flags & TypeFlags.TypeParameter) != 0 && (target.Flags & TypeFlags.Union) != 0))
                {
                    var constraint = GetEffectiveConstraintOfIntersection((source.Flags & TypeFlags.Intersection) != 0 ? ((IIntersectionType)source).Types! : [source], (target.Flags & TypeFlags.Union) != 0);
                    if (constraint is not null && EveryType(constraint, c => !ReferenceEquals(c, source))) // Skip comparison if expansion contains the source itself
                    {
                        // TODO: Stack errors so we get a pyramid for the "normal" comparison above, _and_ a second for this
                        // PORT-NOTE: the '!' marks the reference's own guard (`constraint && everyType(constraint, ...)`),
                        // which the enclosing `if` states explicitly; C#'s flow analysis does not carry it into the call.
                        result = IsRelatedTo(constraint!, target, RecursionFlags.Source, /*reportErrors*/ false, /*headMessage*/ null, intersectionState);
                    }
                }
                // When the target is an intersection we need an extra property check in order to detect nested excess
                // properties and nested weak types. The following are motivating examples that all should be errors, but
                // aren't without this extra property check:
                //
                //   let obj: { a: { x: string } } & { c: number } = { a: { x: 'hello', y: 2 }, c: 5 };  // Nested excess property
                //
                //   declare let wrong: { a: { y: string } };
                //   let weak: { a?: { x?: number } } & { c?: string } = wrong;  // Nested weak object type
                //
                if (
                    result != Ternary.False && (intersectionState & IntersectionState.Target) == 0 && (target.Flags & TypeFlags.Intersection) != 0 &&
                    !IsGenericObjectType(target) && (source.Flags & (TypeFlags.Object | TypeFlags.Intersection)) != 0
                )
                {
                    result = And(result, PropertiesRelatedTo(source, target, reportErrors, /*excludedProperties*/ null, /*optionalsOnly*/ false, IntersectionState.None));
                    if (result != Ternary.False && IsObjectLiteralType(source) && (GetObjectFlags(source) & ObjectFlags.FreshLiteral) != 0)
                    {
                        result = And(result, IndexSignaturesRelatedTo(source, target, /*sourceIsPrimitive*/ false, reportErrors, IntersectionState.None));
                    }
                }
                // When the source is an intersection we need an extra check of any optional properties in the target to
                // detect possible mismatched property types. For example:
                //
                //   function foo<T extends object>(x: { a?: string }, y: T & { a: boolean }) {
                //     x = y;  // Mismatched property in source intersection
                //   }
                //
                else if (
                    result != Ternary.False && IsNonGenericObjectType(target) && !IsArrayOrTupleType(target) &&
                    (source.Flags & TypeFlags.Intersection) != 0 && (GetApparentType(source).Flags & TypeFlags.StructuredType) != 0 &&
                    !Core.Some(((IIntersectionType)source).Types!, t => ReferenceEquals(t, target) || (GetObjectFlags(t) & ObjectFlags.NonInferrableType) != 0)
                )
                {
                    result = And(result, PropertiesRelatedTo(source, target, reportErrors, /*excludedProperties*/ null, /*optionalsOnly*/ true, intersectionState));
                }
            }
            if (result != Ternary.False)
            {
                ResetErrorInfo(saveErrorInfo);
            }
            return result;
        }

        // ported from: src/compiler/checker.ts:23532-23542 — <c>getApparentMappedTypeKeys</c>
        IType GetApparentMappedTypeKeys(IType nameType, IMappedType targetType)
        {
            var modifiersType = GetApparentType(GetModifiersTypeFromMappedType(targetType));
            var mappedKeys = new List<IType>();
            ForEachMappedTypePropertyKeyTypeAndIndexSignatureKeyType(
                modifiersType,
                TypeFlags.StringOrNumberLiteralOrUnique,
                /*stringsOnly*/ false,
                t => mappedKeys.Add(InstantiateType(nameType, AppendTypeMapping((TypeMapper?)targetType.Mapper, GetTypeParameterFromMappedType(targetType), t))!));
            return GetUnionType(mappedKeys);
        }
        // ported from: src/compiler/checker.ts:23544-24165 — <c>structuredTypeRelatedToWorker</c>
        /// <remarks>
        /// PORT-NOTE: the reference shadows its own <c>result</c> once in the <c>identityRelation</c> branch
        /// (<c>let result = eachTypeRelatedToSomeType(…)</c>); C# forbids a local shadowing an enclosing local
        /// (CS0136), so that one is named <c>identityResult</c>. The same applies to the second
        /// <c>templateType</c> inside the generic-mapped-type branch, named <c>mappedTemplateType</c>.
        /// </remarks>
        Ternary StructuredTypeRelatedToWorker(IType source, IType target, bool reportErrors, IntersectionState intersectionState, ErrorCalculationState saveErrorInfo)
        {
            var result = Ternary.False;
            IDiagnosticMessageChain? originalErrorInfo = null;
            var varianceCheckFailed = false;
            var sourceFlags = source.Flags;
            var targetFlags = target.Flags;
            if (ReferenceEquals(relation, _identityRelation))
            {
                // We've already checked that source.flags and target.flags are identical
                if ((sourceFlags & TypeFlags.UnionOrIntersection) != 0)
                {
                    var identityResult = EachTypeRelatedToSomeType((IUnionOrIntersectionType)source, (IUnionOrIntersectionType)target);
                    if (identityResult != Ternary.False)
                    {
                        identityResult = And(identityResult, EachTypeRelatedToSomeType((IUnionOrIntersectionType)target, (IUnionOrIntersectionType)source));
                    }
                    return identityResult;
                }
                if ((sourceFlags & TypeFlags.Index) != 0)
                {
                    return IsRelatedTo(((IIndexType)source).Type!, ((IIndexType)target).Type!, RecursionFlags.Both, /*reportErrors*/ false);
                }
                if ((sourceFlags & TypeFlags.IndexedAccess) != 0)
                {
                    if ((result = IsRelatedTo(((IIndexedAccessType)source).ObjectType!, ((IIndexedAccessType)target).ObjectType!, RecursionFlags.Both, /*reportErrors*/ false)) != Ternary.False)
                    {
                        if ((result = And(result, IsRelatedTo(((IIndexedAccessType)source).IndexType!, ((IIndexedAccessType)target).IndexType!, RecursionFlags.Both, /*reportErrors*/ false))) != Ternary.False)
                        {
                            return result;
                        }
                    }
                }
                if ((sourceFlags & TypeFlags.Conditional) != 0)
                {
                    if (((IConditionalType)source).Root!.IsDistributive == ((IConditionalType)target).Root!.IsDistributive)
                    {
                        if ((result = IsRelatedTo(((IConditionalType)source).CheckType!, ((IConditionalType)target).CheckType!, RecursionFlags.Both, /*reportErrors*/ false)) != Ternary.False)
                        {
                            if ((result = And(result, IsRelatedTo(((IConditionalType)source).ExtendsType!, ((IConditionalType)target).ExtendsType!, RecursionFlags.Both, /*reportErrors*/ false))) != Ternary.False)
                            {
                                if ((result = And(result, IsRelatedTo(GetTrueTypeFromConditionalType((IConditionalType)source), GetTrueTypeFromConditionalType((IConditionalType)target), RecursionFlags.Both, /*reportErrors*/ false))) != Ternary.False)
                                {
                                    if ((result = And(result, IsRelatedTo(GetFalseTypeFromConditionalType((IConditionalType)source), GetFalseTypeFromConditionalType((IConditionalType)target), RecursionFlags.Both, /*reportErrors*/ false))) != Ternary.False)
                                    {
                                        return result;
                                    }
                                }
                            }
                        }
                    }
                }
                if ((sourceFlags & TypeFlags.Substitution) != 0)
                {
                    if ((result = IsRelatedTo(((ISubstitutionType)source).BaseType!, ((ISubstitutionType)target).BaseType!, RecursionFlags.Both, /*reportErrors*/ false)) != Ternary.False)
                    {
                        if ((result = And(result, IsRelatedTo(((ISubstitutionType)source).Constraint!, ((ISubstitutionType)target).Constraint!, RecursionFlags.Both, /*reportErrors*/ false))) != Ternary.False)
                        {
                            return result;
                        }
                    }
                }
                if ((sourceFlags & TypeFlags.TemplateLiteral) != 0)
                {
                    if (Core.ArrayIsEqualTo(((ITemplateLiteralType)source).Texts, ((ITemplateLiteralType)target).Texts))
                    {
                        var sourceTypes = ((ITemplateLiteralType)source).Types!;
                        var targetTypes = ((ITemplateLiteralType)target).Types!;
                        result = Ternary.True;
                        for (var i = 0; i < sourceTypes.Count; i++)
                        {
                            if ((result = And(result, IsRelatedTo(sourceTypes[i], targetTypes[i], RecursionFlags.Both, /*reportErrors*/ false))) == Ternary.False)
                            {
                                break;
                            }
                        }
                        return result;
                    }
                }
                if ((sourceFlags & TypeFlags.StringMapping) != 0)
                {
                    if (ReferenceEquals(((IStringMappingType)source).Symbol, ((IStringMappingType)target).Symbol))
                    {
                        return IsRelatedTo(((IStringMappingType)source).Type!, ((IStringMappingType)target).Type!, RecursionFlags.Both, /*reportErrors*/ false);
                    }
                }
                if ((sourceFlags & TypeFlags.Object) == 0)
                {
                    return Ternary.False;
                }
            }
            else if ((sourceFlags & TypeFlags.UnionOrIntersection) != 0 || (targetFlags & TypeFlags.UnionOrIntersection) != 0)
            {
                if ((result = UnionOrIntersectionRelatedTo(source, target, reportErrors, intersectionState)) != Ternary.False)
                {
                    return result;
                }
                // The ordered decomposition above doesn't handle all cases. Specifically, we also need to handle:
                // Source is instantiable (e.g. source has union or intersection constraint).
                // Source is an object, target is a union (e.g. { a, b: boolean } <=> { a, b: true } | { a, b: false }).
                // Source is an intersection, target is an object (e.g. { a } & { b } <=> { a, b }).
                // Source is an intersection, target is a union (e.g. { a } & { b: boolean } <=> { a, b: true } | { a, b: false }).
                // Source is an intersection, target instantiable (e.g. string & { tag } <=> T["a"] constrained to string & { tag }).
                if (
                    // PORT-NOTE: the reference is the single negation of a three-way *disjunction*
                    // (checker.ts:23621-23627). Folding the three cases into one `sourceFlags & (…)` mask cannot express
                    // it: the second case tests `targetFlags & Union` and the third `targetFlags & (Object | Union |
                    // Instantiable)`, neither of which is a mask on `sourceFlags` — C#'s `&` binding tighter than `|`
                    // would also annihilate the third disjunct, since `TypeFlags.Intersection` is not one of the
                    // `Object | Union | Instantiable` bits. The three tests are therefore spelled out separately, which
                    // is the reference's `!(A || (B && C) || (D && E))` under De Morgan.
                    (sourceFlags & TypeFlags.Instantiable) == 0
                    && ((sourceFlags & TypeFlags.Object) == 0 || (targetFlags & TypeFlags.Union) == 0)
                    && ((sourceFlags & TypeFlags.Intersection) == 0 || (targetFlags & (TypeFlags.Object | TypeFlags.Union | TypeFlags.Instantiable)) == 0)
                )
                {
                    return Ternary.False;
                }
            }

            // We limit alias variance probing to only object and conditional types since their alias behavior
            // is more predictable than other, interned types, which may or may not have an alias depending on
            // the order in which things were checked.
            if (
                (sourceFlags & (TypeFlags.Object | TypeFlags.Conditional)) != 0 && source.AliasSymbol is not null && source.AliasTypeArguments is not null &&
                ReferenceEquals(source.AliasSymbol, target.AliasSymbol) && !(IsMarkerType(source) || IsMarkerType(target))
            )
            {
                var variances = GetAliasVariances(source.AliasSymbol);
                if (variances.Count == 0)
                {
                    return Ternary.Unknown;
                }
                var parameters = GetSymbolLinks(source.AliasSymbol).TypeParameters!;
                var minParams = GetMinTypeArgumentCount(parameters);
                var sourceTypes = FillMissingTypeArguments(source.AliasTypeArguments, parameters, minParams, Utilities.IsInJSFile((Node)source.AliasSymbol.ValueDeclaration!));
                var targetTypes = FillMissingTypeArguments(target.AliasTypeArguments!, parameters, minParams, Utilities.IsInJSFile((Node)source.AliasSymbol.ValueDeclaration!));
                var varianceResult = RelateVariances(sourceTypes, targetTypes, variances, intersectionState);
                if (varianceResult is not null)
                {
                    return varianceResult.Value;
                }
            }

            // For a generic type T and a type U that is assignable to T, [...U] is assignable to T, U is assignable to readonly [...T],
            // and U is assignable to [...T] when U is constrained to a mutable array or tuple type.
            if (
                IsSingleElementGenericTupleType(source) && !TupleOf((ITupleTypeReference)source).Readonly && (result = IsRelatedTo(GetTypeArguments((ITypeReference)source)[0], target, RecursionFlags.Source)) != Ternary.False ||
                IsSingleElementGenericTupleType(target) && (TupleOf((ITupleTypeReference)target).Readonly || IsMutableArrayOrTuple(GetBaseConstraintOfType(source) ?? source)) && (result = IsRelatedTo(source, GetTypeArguments((ITypeReference)target)[0], RecursionFlags.Target)) != Ternary.False
            )
            {
                return result;
            }

            if ((targetFlags & TypeFlags.TypeParameter) != 0)
            {
                // A source type { [P in Q]: X } is related to a target type T if keyof T is related to Q and X is related to T[Q].
                if ((GetObjectFlags(source) & ObjectFlags.Mapped) != 0 && ((IMappedType)source).Declaration!.NameType is null && IsRelatedTo(GetIndexType(target), GetConstraintTypeFromMappedType((IMappedType)source), RecursionFlags.Both) != Ternary.False)
                {
                    if ((GetMappedTypeModifiers((IMappedType)source) & MappedTypeModifiers.IncludeOptional) == 0)
                    {
                        var templateType = GetTemplateTypeFromMappedType((IMappedType)source);
                        var indexedAccessType = GetIndexedAccessType(target, GetTypeParameterFromMappedType((IMappedType)source));
                        if ((result = IsRelatedTo(templateType, indexedAccessType, RecursionFlags.Both, reportErrors)) != Ternary.False)
                        {
                            return result;
                        }
                    }
                }
                if (ReferenceEquals(relation, _comparableRelation) && (sourceFlags & TypeFlags.TypeParameter) != 0)
                {
                    // This is a carve-out in comparability to essentially forbid comparing a type parameter
                    // with another type parameter unless one extends the other. (Remember: comparability is mostly bidirectional!)
                    var typeParameterConstraint = GetConstraintOfTypeParameter((ITypeParameter)source);
                    if (typeParameterConstraint is not null)
                    {
                        while (typeParameterConstraint is not null && SomeType(typeParameterConstraint, c => (c.Flags & TypeFlags.TypeParameter) != 0))
                        {
                            // PORT-NOTE: '!' for the reference's `constraint && someType(constraint, ...)` guard, which
                            // the enclosing `while` states explicitly.
                            if ((result = IsRelatedTo(typeParameterConstraint!, target, RecursionFlags.Source, /*reportErrors*/ false)) != Ternary.False)
                            {
                                return result;
                            }
                            typeParameterConstraint = GetConstraintOfTypeParameter((ITypeParameter)typeParameterConstraint!);
                        }
                    }
                    return Ternary.False;
                }
            }
            else if ((targetFlags & TypeFlags.Index) != 0)
            {
                var targetType = ((IIndexType)target).Type!;
                // A keyof S is related to a keyof T if T is related to S.
                if ((sourceFlags & TypeFlags.Index) != 0)
                {
                    if ((result = IsRelatedTo(targetType, ((IIndexType)source).Type!, RecursionFlags.Both, /*reportErrors*/ false)) != Ternary.False)
                    {
                        return result;
                    }
                }
                if (IsTupleType(targetType))
                {
                    // An index type can have a tuple type target when the tuple type contains variadic elements.
                    // Check if the source is related to the known keys of the tuple type.
                    if ((result = IsRelatedTo(source, GetKnownKeysOfTupleType((ITupleTypeReference)targetType), RecursionFlags.Target, reportErrors)) != Ternary.False)
                    {
                        return result;
                    }
                }
                else
                {
                    // A type S is assignable to keyof T if S is assignable to keyof C, where C is the
                    // simplified form of T or, if T doesn't simplify, the constraint of T.
                    var simplifiedConstraint = GetSimplifiedTypeOrConstraint(targetType);
                    if (simplifiedConstraint is not null)
                    {
                        // We require Ternary.True here such that circular constraints don't cause
                        // false positives. For example, given 'T extends { [K in keyof T]: string }',
                        // 'keyof T' has itself as its constraint and produces a Ternary.Maybe when
                        // related to other types.
                        if (IsRelatedTo(source, GetIndexType(simplifiedConstraint, ((IIndexType)target).IndexFlags | IndexFlags.NoReducibleCheck), RecursionFlags.Target, reportErrors) == Ternary.True)
                        {
                            return Ternary.True;
                        }
                    }
                    else if (IsGenericMappedType(targetType))
                    {
                        // generic mapped types that don't simplify or have a constraint still have a very simple set of keys we can compare against
                        // - their nameType or constraintType.
                        // In many ways, this comparison is a deferred version of what `getIndexTypeForMappedType` does to actually resolve the keys for _non_-generic types
                        var nameType = GetNameTypeFromMappedType((IMappedType)targetType);
                        var constraintType = GetConstraintTypeFromMappedType((IMappedType)targetType);
                        IType? targetKeys;
                        if (nameType is not null && IsMappedTypeWithKeyofConstraintDeclaration((IMappedType)targetType))
                        {
                            // we need to get the apparent mappings and union them with the generic mappings, since some properties may be
                            // missing from the `constraintType` which will otherwise be mapped in the object
                            var mappedKeys = GetApparentMappedTypeKeys(nameType, (IMappedType)targetType);
                            // We still need to include the non-apparent (and thus still generic) keys in the target side of the comparison (in case they're in the source side)
                            targetKeys = GetUnionType([mappedKeys, nameType]);
                        }
                        else
                        {
                            targetKeys = nameType ?? constraintType;
                        }
                        if (IsRelatedTo(source, targetKeys!, RecursionFlags.Target, reportErrors) == Ternary.True)
                        {
                            return Ternary.True;
                        }
                    }
                }
            }
            else if ((targetFlags & TypeFlags.IndexedAccess) != 0)
            {
                if ((sourceFlags & TypeFlags.IndexedAccess) != 0)
                {
                    // Relate components directly before falling back to constraint relationships
                    // A type S[K] is related to a type T[J] if S is related to T and K is related to J.
                    if ((result = IsRelatedTo(((IIndexedAccessType)source).ObjectType!, ((IIndexedAccessType)target).ObjectType!, RecursionFlags.Both, reportErrors)) != Ternary.False)
                    {
                        result = And(result, IsRelatedTo(((IIndexedAccessType)source).IndexType!, ((IIndexedAccessType)target).IndexType!, RecursionFlags.Both, reportErrors));
                    }
                    if (result != Ternary.False)
                    {
                        return result;
                    }
                    if (reportErrors)
                    {
                        originalErrorInfo = errorInfo;
                    }
                }
                // A type S is related to a type T[K] if S is related to C, where C is the base
                // constraint of T[K] for writing.
                if (ReferenceEquals(relation, _assignableRelation) || ReferenceEquals(relation, _comparableRelation))
                {
                    var objectType = ((IIndexedAccessType)target).ObjectType!;
                    var indexType = ((IIndexedAccessType)target).IndexType!;
                    var baseObjectType = GetBaseConstraintOfType(objectType) ?? objectType;
                    var baseIndexType = GetBaseConstraintOfType(indexType) ?? indexType;
                    if (!IsGenericObjectType(baseObjectType) && !IsGenericIndexType(baseIndexType))
                    {
                        var accessFlags = AccessFlags.Writing | (!ReferenceEquals(baseObjectType, objectType) ? AccessFlags.NoIndexSignatures : AccessFlags.None);
                        var constraint = GetIndexedAccessTypeOrUndefined(baseObjectType, baseIndexType, accessFlags);
                        if (constraint is not null)
                        {
                            if (reportErrors && originalErrorInfo is not null)
                            {
                                // create a new chain for the constraint error
                                ResetErrorInfo(saveErrorInfo);
                            }
                            if ((result = IsRelatedTo(source, constraint, RecursionFlags.Target, reportErrors, /*headMessage*/ null, intersectionState)) != Ternary.False)
                            {
                                return result;
                            }
                            // prefer the shorter chain of the constraint comparison chain, and the direct comparison chain
                            if (reportErrors && originalErrorInfo is not null && errorInfo is not null)
                            {
                                errorInfo = CountMessageChainBreadth([originalErrorInfo]) <= CountMessageChainBreadth([errorInfo]) ? originalErrorInfo : errorInfo;
                            }
                        }
                    }
                }
                if (reportErrors)
                {
                    originalErrorInfo = null;
                }
            }
            else if (IsGenericMappedType(target) && !ReferenceEquals(relation, _identityRelation))
            {
                // Check if source type `S` is related to target type `{ [P in Q]: T }` or `{ [P in Q as R]: T}`.
                var keysRemapped = ((IMappedType)target).Declaration!.NameType is not null;
                var templateType = GetTemplateTypeFromMappedType((IMappedType)target);
                var modifiers = GetMappedTypeModifiers((IMappedType)target);
                if ((modifiers & MappedTypeModifiers.ExcludeOptional) == 0)
                {
                    // If the mapped type has shape `{ [P in Q]: T[P] }`,
                    // source `S` is related to target if `T` = `S`, i.e. `S` is related to `{ [P in Q]: S[P] }`.
                    if (
                        !keysRemapped && (templateType.Flags & TypeFlags.IndexedAccess) != 0 && ReferenceEquals(((IIndexedAccessType)templateType).ObjectType, source) &&
                        ReferenceEquals(((IIndexedAccessType)templateType).IndexType, GetTypeParameterFromMappedType((IMappedType)target))
                    )
                    {
                        return Ternary.True;
                    }
                    if (!IsGenericMappedType(source))
                    {
                        // If target has shape `{ [P in Q as R]: T}`, then its keys have type `R`.
                        // If target has shape `{ [P in Q]: T }`, then its keys have type `Q`.
                        var targetKeys = keysRemapped ? GetNameTypeFromMappedType((IMappedType)target)! : GetConstraintTypeFromMappedType((IMappedType)target);
                        // Type of the keys of source type `S`, i.e. `keyof S`.
                        var sourceKeys = GetIndexType(source, IndexFlags.NoIndexSignatures);
                        var includeOptional = (modifiers & MappedTypeModifiers.IncludeOptional) != 0;
                        var filteredByApplicability = includeOptional ? IntersectTypes(targetKeys, sourceKeys) : null;
                        // A source type `S` is related to a target type `{ [P in Q]: T }` if `Q` is related to `keyof S` and `S[Q]` is related to `T`.
                        // A source type `S` is related to a target type `{ [P in Q as R]: T }` if `R` is related to `keyof S` and `S[R]` is related to `T.
                        // A source type `S` is related to a target type `{ [P in Q]?: T }` if some constituent `Q'` of `Q` is related to `keyof S` and `S[Q']` is related to `T`.
                        // A source type `S` is related to a target type `{ [P in Q as R]?: T }` if some constituent `R'` of `R` is related to `keyof S` and `S[R']` is related to `T`.
                        if (
                            includeOptional
                                ? (filteredByApplicability!.Flags & TypeFlags.Never) == 0
                                : IsRelatedTo(targetKeys, sourceKeys, RecursionFlags.Both) != Ternary.False
                        )
                        {
                            var mappedTemplateType = GetTemplateTypeFromMappedType((IMappedType)target);
                            var typeParameter = GetTypeParameterFromMappedType((IMappedType)target);

                            // Fastpath: When the template type has the form `Obj[P]` where `P` is the mapped type parameter, directly compare source `S` with `Obj`
                            // to avoid creating the (potentially very large) number of new intermediate types made by manufacturing `S[P]`.
                            var nonNullComponent = ExtractTypesOfKind(mappedTemplateType, ~TypeFlags.Nullable);
                            if (!keysRemapped && (nonNullComponent.Flags & TypeFlags.IndexedAccess) != 0 && ReferenceEquals(((IIndexedAccessType)nonNullComponent).IndexType, typeParameter))
                            {
                                if ((result = IsRelatedTo(source, ((IIndexedAccessType)nonNullComponent).ObjectType!, RecursionFlags.Target, reportErrors)) != Ternary.False)
                                {
                                    return result;
                                }
                            }
                            else
                            {
                                // We need to compare the type of a property on the source type `S` to the type of the same property on the target type,
                                // so we need to construct an indexing type representing a property, and then use indexing type to index the source type for comparison.

                                // If the target type has shape `{ [P in Q]: T }`, then a property of the target has type `P`.
                                // If the target type has shape `{ [P in Q]?: T }`, then a property of the target has type `P`,
                                // but the property is optional, so we only want to compare properties `P` that are common between `keyof S` and `Q`.
                                // If the target type has shape `{ [P in Q as R]: T }`, then a property of the target has type `R`.
                                // If the target type has shape `{ [P in Q as R]?: T }`, then a property of the target has type `R`,
                                // but the property is optional, so we only want to compare properties `R` that are common between `keyof S` and `R`.
                                var indexingType = keysRemapped
                                    ? filteredByApplicability ?? targetKeys
                                    : filteredByApplicability is not null
                                    ? GetIntersectionType([filteredByApplicability, typeParameter])
                                    : typeParameter;
                                var indexedAccessType = GetIndexedAccessType(source, indexingType);
                                // Compare `S[indexingType]` to `T`, where `T` is the type of a property of the target type.
                                if ((result = IsRelatedTo(indexedAccessType, mappedTemplateType, RecursionFlags.Both, reportErrors)) != Ternary.False)
                                {
                                    return result;
                                }
                            }
                        }
                        originalErrorInfo = errorInfo;
                        ResetErrorInfo(saveErrorInfo);
                    }
                }
            }
            else if ((targetFlags & TypeFlags.Conditional) != 0)
            {
                // If we reach 10 levels of nesting for the same conditional type, assume it is an infinitely expanding recursive
                // conditional type and bail out with a Ternary.Maybe result.
                if (IsDeeplyNestedType(target, targetStack!, targetDepth, 10))
                {
                    return Ternary.Maybe;
                }
                var c = (IConditionalType)target;
                // We check for a relationship to a conditional type target only when the conditional type has no
                // 'infer' positions, is not distributive or is distributive but doesn't reference the check type
                // parameter in either of the result types, and the source isn't an instantiation of the same
                // conditional type (as happens when computing variance).
                if (c.Root!.InferTypeParameters is null && !IsDistributionDependent(c.Root) && !((source.Flags & TypeFlags.Conditional) != 0 && ReferenceEquals(((IConditionalType)source).Root, c.Root)))
                {
                    // Check if the conditional is always true or always false but still deferred for distribution purposes.
                    var skipTrue = !IsTypeAssignableTo(GetPermissiveInstantiation(c.CheckType!), GetPermissiveInstantiation(c.ExtendsType!));
                    var skipFalse = !skipTrue && IsTypeAssignableTo(GetRestrictiveInstantiation(c.CheckType!), GetRestrictiveInstantiation(c.ExtendsType!));
                    // TODO: Find a nice way to include potential conditional type breakdowns in error output, if they seem good (they usually don't)
                    if ((result = skipTrue ? Ternary.True : IsRelatedTo(source, GetTrueTypeFromConditionalType(c), RecursionFlags.Target, /*reportErrors*/ false, /*headMessage*/ null, intersectionState)) != Ternary.False)
                    {
                        result = And(result, skipFalse ? Ternary.True : IsRelatedTo(source, GetFalseTypeFromConditionalType(c), RecursionFlags.Target, /*reportErrors*/ false, /*headMessage*/ null, intersectionState));
                        if (result != Ternary.False)
                        {
                            return result;
                        }
                    }
                }
            }
            else if ((targetFlags & TypeFlags.TemplateLiteral) != 0)
            {
                if ((sourceFlags & TypeFlags.TemplateLiteral) != 0)
                {
                    if (ReferenceEquals(relation, _comparableRelation))
                    {
                        return TemplateLiteralTypesDefinitelyUnrelated((ITemplateLiteralType)source, (ITemplateLiteralType)target) ? Ternary.False : Ternary.True;
                    }
                    // Report unreliable variance for type variables referenced in template literal type placeholders.
                    // For example, `foo-${number}` is related to `foo-${string}` even though number isn't related to string.
                    InstantiateType(source, _reportUnreliableMapper);
                }
                if (IsTypeMatchedByTemplateLiteralType(source, (ITemplateLiteralType)target))
                {
                    return Ternary.True;
                }
            }
            else if ((target.Flags & TypeFlags.StringMapping) != 0)
            {
                if ((source.Flags & TypeFlags.StringMapping) == 0)
                {
                    if (IsMemberOfStringMapping(source, target))
                    {
                        return Ternary.True;
                    }
                }
            }

            if ((sourceFlags & TypeFlags.TypeVariable) != 0)
            {
                // IndexedAccess comparisons are handled above in the `targetFlags & TypeFlage.IndexedAccess` branch
                if (!((sourceFlags & TypeFlags.IndexedAccess) != 0 && (targetFlags & TypeFlags.IndexedAccess) != 0))
                {
                    var constraint = GetConstraintOfType(source) ?? _unknownType;
                    // hi-speed no-this-instantiation check (less accurate, but avoids costly `this`-instantiation when the constraint will suffice), see #28231 for report on why this is needed
                    if ((result = IsRelatedTo(constraint, target, RecursionFlags.Source, /*reportErrors*/ false, /*headMessage*/ null, intersectionState)) != Ternary.False)
                    {
                        return result;
                    }
                    // slower, fuller, this-instantiated check (necessary when comparing raw `this` types from base classes), see `subclassWithPolymorphicThisIsAssignable.ts` test for example
                    else if ((result = IsRelatedTo(GetTypeWithThisArgument(constraint, source), target, RecursionFlags.Source, reportErrors && !ReferenceEquals(constraint, _unknownType) && (targetFlags & sourceFlags & TypeFlags.TypeParameter) == 0, /*headMessage*/ null, intersectionState)) != Ternary.False)
                    {
                        return result;
                    }
                    if (IsMappedTypeGenericIndexedAccess(source))
                    {
                        // For an indexed access type { [P in K]: E}[X], above we have already explored an instantiation of E with X
                        // substituted for P. We also want to explore type { [P in K]: E }[C], where C is the constraint of X.
                        var indexConstraint = GetConstraintOfType(((IIndexedAccessType)source).IndexType!);
                        if (indexConstraint is not null)
                        {
                            if ((result = IsRelatedTo(GetIndexedAccessType(((IIndexedAccessType)source).ObjectType!, indexConstraint), target, RecursionFlags.Source, reportErrors)) != Ternary.False)
                            {
                                return result;
                            }
                        }
                    }
                }
            }
            else if ((sourceFlags & TypeFlags.Index) != 0)
            {
                var isDeferredMappedIndex = ShouldDeferIndexType(((IIndexType)source).Type!, ((IIndexType)source).IndexFlags) && (GetObjectFlags(((IIndexType)source).Type!) & ObjectFlags.Mapped) != 0;
                if ((result = IsRelatedTo(_stringNumberSymbolType, target, RecursionFlags.Source, reportErrors && !isDeferredMappedIndex)) != Ternary.False)
                {
                    return result;
                }
                if (isDeferredMappedIndex)
                {
                    var mappedType = (IMappedType)((IIndexType)source).Type!;
                    var nameType = GetNameTypeFromMappedType(mappedType);
                    // Unlike on the target side, on the source side we do *not* include the generic part of the `nameType`, since that comes from a
                    // (potentially anonymous) mapped type local type parameter, so that'd never assign outside the mapped type body, but we still want to
                    // allow assignments of index types of identical (or similar enough) mapped types.
                    // eg, `keyof {[X in keyof A]: Obj[X]}` should be assignable to `keyof {[Y in keyof A]: Tup[Y]}` because both map over the same set of keys (`keyof A`).
                    // Without this source-side breakdown, a `keyof {[X in keyof A]: Obj[X]}` style type won't be assignable to anything except itself, which is much too strict.
                    var sourceMappedKeys = nameType is not null && IsMappedTypeWithKeyofConstraintDeclaration(mappedType) ? GetApparentMappedTypeKeys(nameType, mappedType) : nameType ?? GetConstraintTypeFromMappedType(mappedType);
                    if ((result = IsRelatedTo(sourceMappedKeys, target, RecursionFlags.Source, reportErrors)) != Ternary.False)
                    {
                        return result;
                    }
                }
            }
            else if ((sourceFlags & TypeFlags.TemplateLiteral) != 0 && (targetFlags & TypeFlags.Object) == 0)
            {
                if ((targetFlags & TypeFlags.TemplateLiteral) == 0)
                {
                    var templateConstraint = GetBaseConstraintOfType(source);
                    if (templateConstraint is not null && !ReferenceEquals(templateConstraint, source) && (result = IsRelatedTo(templateConstraint, target, RecursionFlags.Source, reportErrors)) != Ternary.False)
                    {
                        return result;
                    }
                }
            }
            else if ((sourceFlags & TypeFlags.StringMapping) != 0)
            {
                if ((targetFlags & TypeFlags.StringMapping) != 0)
                {
                    if (!ReferenceEquals(((IStringMappingType)source).Symbol, ((IStringMappingType)target).Symbol))
                    {
                        return Ternary.False;
                    }
                    if ((result = IsRelatedTo(((IStringMappingType)source).Type!, ((IStringMappingType)target).Type!, RecursionFlags.Both, reportErrors)) != Ternary.False)
                    {
                        return result;
                    }
                }
                else
                {
                    var stringMappingConstraint = GetBaseConstraintOfType(source);
                    if (stringMappingConstraint is not null && (result = IsRelatedTo(stringMappingConstraint, target, RecursionFlags.Source, reportErrors)) != Ternary.False)
                    {
                        return result;
                    }
                }
            }
            else if ((sourceFlags & TypeFlags.Conditional) != 0)
            {
                // If we reach 10 levels of nesting for the same conditional type, assume it is an infinitely expanding recursive
                // conditional type and bail out with a Ternary.Maybe result.
                if (IsDeeplyNestedType(source, sourceStack!, sourceDepth, 10))
                {
                    return Ternary.Maybe;
                }
                if ((targetFlags & TypeFlags.Conditional) != 0)
                {
                    // Two conditional types 'T1 extends U1 ? X1 : Y1' and 'T2 extends U2 ? X2 : Y2' are related if
                    // one of T1 and T2 is related to the other, U1 and U2 are identical types, X1 is related to X2,
                    // and Y1 is related to Y2.
                    var sourceParams = ((IConditionalType)source).Root!.InferTypeParameters;
                    var sourceExtends = ((IConditionalType)source).ExtendsType!;
                    TypeMapper? mapper = null;
                    if (sourceParams is not null)
                    {
                        // If the source has infer type parameters, we instantiate them in the context of the target
                        var ctx = CreateInferenceContext(sourceParams, /*signature*/ null, InferenceFlags.None, IsRelatedToWorker);
                        InferTypes(ctx.Inferences!, ((IConditionalType)target).ExtendsType!, sourceExtends, InferencePriority.NoConstraints | InferencePriority.AlwaysStrict);
                        sourceExtends = InstantiateType(sourceExtends, (TypeMapper?)ctx.Mapper)!;
                        mapper = (TypeMapper?)ctx.Mapper;
                    }
                    if (
                        IsTypeIdenticalTo(sourceExtends, ((IConditionalType)target).ExtendsType!) &&
                        (IsRelatedTo(((IConditionalType)source).CheckType!, ((IConditionalType)target).CheckType!, RecursionFlags.Both) != Ternary.False || IsRelatedTo(((IConditionalType)target).CheckType!, ((IConditionalType)source).CheckType!, RecursionFlags.Both) != Ternary.False)
                    )
                    {
                        if ((result = IsRelatedTo(InstantiateType(GetTrueTypeFromConditionalType((IConditionalType)source), mapper)!, GetTrueTypeFromConditionalType((IConditionalType)target), RecursionFlags.Both, reportErrors)) != Ternary.False)
                        {
                            result = And(result, IsRelatedTo(GetFalseTypeFromConditionalType((IConditionalType)source), GetFalseTypeFromConditionalType((IConditionalType)target), RecursionFlags.Both, reportErrors));
                        }
                        if (result != Ternary.False)
                        {
                            return result;
                        }
                    }
                }
                // conditionals can be related to one another via normal constraint, as, eg, `A extends B ? O : never` should be assignable to `O`
                // when `O` is a conditional (`never` is trivially assignable to `O`, as is `O`!).
                var defaultConstraint = GetDefaultConstraintOfConditionalType((IConditionalType)source);
                if (defaultConstraint is not null)
                {
                    if ((result = IsRelatedTo(defaultConstraint, target, RecursionFlags.Source, reportErrors)) != Ternary.False)
                    {
                        return result;
                    }
                }
                // conditionals aren't related to one another via distributive constraint as it is much too inaccurate and allows way
                // more assignments than are desirable (since it maps the source check type to its constraint, it loses information).
                var distributiveConstraint = (targetFlags & TypeFlags.Conditional) == 0 && HasNonCircularBaseConstraint(source) ? GetConstraintOfDistributiveConditionalType((IConditionalType)source) : null;
                if (distributiveConstraint is not null)
                {
                    ResetErrorInfo(saveErrorInfo);
                    if ((result = IsRelatedTo(distributiveConstraint, target, RecursionFlags.Source, reportErrors)) != Ternary.False)
                    {
                        return result;
                    }
                }
            }
            else
            {
                // An empty object type is related to any mapped type that includes a '?' modifier.
                if (!ReferenceEquals(relation, _subtypeRelation) && !ReferenceEquals(relation, _strictSubtypeRelation) && IsPartialMappedType(target) && IsEmptyObjectType(source))
                {
                    return Ternary.True;
                }
                if (IsGenericMappedType(target))
                {
                    if (IsGenericMappedType(source))
                    {
                        if ((result = MappedTypeRelatedTo((IMappedType)source, (IMappedType)target, reportErrors)) != Ternary.False)
                        {
                            return result;
                        }
                    }
                    return Ternary.False;
                }
                var sourceIsPrimitive = (sourceFlags & TypeFlags.Primitive) != 0;
                if (!ReferenceEquals(relation, _identityRelation))
                {
                    source = GetApparentType(source);
                    sourceFlags = source.Flags;
                }
                else if (IsGenericMappedType(source))
                {
                    return Ternary.False;
                }
                if (
                    (GetObjectFlags(source) & ObjectFlags.Reference) != 0 && (GetObjectFlags(target) & ObjectFlags.Reference) != 0 && ReferenceEquals(((ITypeReference)source).Target, ((ITypeReference)target).Target) &&
                    !IsTupleType(source) && !(IsMarkerType(source) || IsMarkerType(target))
                )
                {
                    // When strictNullChecks is disabled, the element type of the empty array literal is undefinedWideningType,
                    // and an empty array literal wouldn't be assignable to a `never[]` without this check.
                    if (IsEmptyArrayLiteralType(source))
                    {
                        return Ternary.True;
                    }
                    // We have type references to the same generic type, and the type references are not marker
                    // type references (which are intended by be compared structurally). Obtain the variance
                    // information for the type parameters and relate the type arguments accordingly.
                    var variances = GetVariances(((ITypeReference)source).Target!);
                    // We return Ternary.Maybe for a recursive invocation of getVariances (signalled by emptyArray). This
                    // effectively means we measure variance only from type parameter occurrences that aren't nested in
                    // recursive instantiations of the generic type.
                    if (variances.Count == 0)
                    {
                        return Ternary.Unknown;
                    }
                    var varianceResult = RelateVariances(GetTypeArguments((ITypeReference)source), GetTypeArguments((ITypeReference)target), variances, intersectionState);
                    if (varianceResult is not null)
                    {
                        return varianceResult.Value;
                    }
                }
                else if (IsReadonlyArrayType(target) ? EveryType(source, IsArrayOrTupleType) : IsArrayType(target) && EveryType(source, t => IsTupleType(t) && !TupleOf((ITupleTypeReference)t).Readonly))
                {
                    if (!ReferenceEquals(relation, _identityRelation))
                    {
                        return IsRelatedTo(GetIndexTypeOfType(source, _numberType) ?? _anyType, GetIndexTypeOfType(target, _numberType) ?? _anyType, RecursionFlags.Both, reportErrors);
                    }
                    else
                    {
                        // By flags alone, we know that the `target` is a readonly array while the source is a normal array or tuple
                        // or `target` is an array and source is a tuple - in both cases the types cannot be identical, by construction
                        return Ternary.False;
                    }
                }
                else if (IsGenericTupleType(source) && IsTupleType(target) && !IsGenericTupleType(target))
                {
                    var constraint = GetBaseConstraintOrType(source);
                    if (!ReferenceEquals(constraint, source))
                    {
                        return IsRelatedTo(constraint, target, RecursionFlags.Source, reportErrors);
                    }
                }
                // A fresh empty object type is never a subtype of a non-empty object type. This ensures fresh({}) <: { [x: string]: xxx }
                // but not vice-versa. Without this rule, those types would be mutual subtypes.
                else if ((ReferenceEquals(relation, _subtypeRelation) || ReferenceEquals(relation, _strictSubtypeRelation)) && IsEmptyObjectType(target) && (GetObjectFlags(target) & ObjectFlags.FreshLiteral) != 0 && !IsEmptyObjectType(source))
                {
                    return Ternary.False;
                }
                // Even if relationship doesn't hold for unions, intersections, or generic type references,
                // it may hold in a structural comparison.
                // In a check of the form X = A & B, we will have previously checked if A relates to X or B relates
                // to X. Failing both of those we want to check if the aggregation of A and B's members structurally
                // relates to X. Thus, we include intersection types on the source side here.
                if ((sourceFlags & (TypeFlags.Object | TypeFlags.Intersection)) != 0 && (targetFlags & TypeFlags.Object) != 0)
                {
                    // Report structural errors only if we haven't reported any errors yet
                    var reportStructuralErrors = reportErrors && ReferenceEquals(errorInfo, saveErrorInfo.ErrorInfo) && !sourceIsPrimitive;
                    result = PropertiesRelatedTo(source, target, reportStructuralErrors, /*excludedProperties*/ null, /*optionalsOnly*/ false, intersectionState);
                    if (result != Ternary.False)
                    {
                        result = And(result, SignaturesRelatedTo(source, target, SignatureKind.Call, reportStructuralErrors, intersectionState));
                        if (result != Ternary.False)
                        {
                            result = And(result, SignaturesRelatedTo(source, target, SignatureKind.Construct, reportStructuralErrors, intersectionState));
                            if (result != Ternary.False)
                            {
                                result = And(result, IndexSignaturesRelatedTo(source, target, sourceIsPrimitive, reportStructuralErrors, intersectionState));
                            }
                        }
                    }
                    if (varianceCheckFailed && result != Ternary.False)
                    {
                        errorInfo = originalErrorInfo ?? errorInfo ?? saveErrorInfo.ErrorInfo; // Use variance error (there is no structural one) and return false
                    }
                    else if (result != Ternary.False)
                    {
                        return result;
                    }
                }
                // If S is an object type and T is a discriminated union, S may be related to T if
                // there exists a constituent of T for every combination of the discriminants of S
                // with respect to T. We do not report errors here, as we will use the existing
                // error result from checking each constituent of the union.
                if ((sourceFlags & (TypeFlags.Object | TypeFlags.Intersection)) != 0 && (targetFlags & TypeFlags.Union) != 0)
                {
                    var objectOnlyTarget = ExtractTypesOfKind(target, TypeFlags.Object | TypeFlags.Intersection | TypeFlags.Substitution);
                    if ((objectOnlyTarget.Flags & TypeFlags.Union) != 0)
                    {
                        var discriminatedResult = TypeRelatedToDiscriminatedType(source, (IUnionType)objectOnlyTarget);
                        if (discriminatedResult != Ternary.False)
                        {
                            return discriminatedResult;
                        }
                    }
                }
            }
            return Ternary.False;

            // ported from: src/compiler/checker.ts:24119-24122 — <c>countMessageChainBreadth</c>
            int CountMessageChainBreadth(IList<IDiagnosticMessageChain>? info)
            {
                if (info is null) return 0;
                return Core.ReduceLeft(info, (value, chain, _) => value + 1 + CountMessageChainBreadth(chain.Next), 0);
            }

            // ported from: src/compiler/checker.ts:24124-24164 — <c>relateVariances</c>
            Ternary? RelateVariances(IList<IType>? sourceTypeArguments, IList<IType>? targetTypeArguments, List<VarianceFlags>? variances, IntersectionState intersectionState)
            {
                if ((result = TypeArgumentsRelatedTo(sourceTypeArguments, targetTypeArguments, variances, reportErrors, intersectionState)) != Ternary.False)
                {
                    return result;
                }
                if (Core.Some(variances, v => (v & VarianceFlags.AllowsStructuralFallback) != 0))
                {
                    // If some type parameter was `Unmeasurable` or `Unreliable`, and we couldn't pass by assuming it was identical, then we
                    // have to allow a structural fallback check
                    // We elide the variance-based error elaborations, since those might not be too helpful, since we'll potentially
                    // be assuming identity of the type parameter.
                    originalErrorInfo = null;
                    ResetErrorInfo(saveErrorInfo);
                    return null;
                }
                var allowStructuralFallback = targetTypeArguments is not null && HasCovariantVoidArgument(targetTypeArguments, variances!);
                varianceCheckFailed = !allowStructuralFallback;
                // The type arguments did not relate appropriately, but it may be because we have no variance
                // information (in which case typeArgumentsRelatedTo defaulted to covariance for all type
                // arguments). It might also be the case that the target type has a 'void' type argument for
                // a covariant type parameter that is only used in return positions within the generic type
                // (in which case any type argument is permitted on the source side). In those cases we proceed
                // with a structural comparison. Otherwise, we know for certain the instantiations aren't
                // related and we can return here.
                if (variances is { Count: > 0 } && !allowStructuralFallback)
                {
                    // In some cases generic types that are covariant in regular type checking mode become
                    // invariant in --strictFunctionTypes mode because one or more type parameters are used in
                    // both co- and contravariant positions. In order to make it easier to diagnose *why* such
                    // types are invariant, if any of the type parameters are invariant we reset the reported
                    // errors and instead force a structural comparison (which will include elaborations that
                    // reveal the reason).
                    // We can switch on `reportErrors` here, since varianceCheckFailed guarantees we return `False`,
                    // we can return `False` early here to skip calculating the structural error message we don't need.
                    if (varianceCheckFailed && !(reportErrors && Core.Some(variances, v => (v & VarianceFlags.VarianceMask) == VarianceFlags.Invariant)))
                    {
                        return Ternary.False;
                    }
                    // We remember the original error information so we can restore it in case the structural
                    // comparison unexpectedly succeeds. This can happen when the structural comparison result
                    // is a Ternary.Maybe for example caused by the recursion depth limiter.
                    originalErrorInfo = errorInfo;
                    ResetErrorInfo(saveErrorInfo);
                }
                return null;
            }
        }
        // A type [P in S]: X is related to a type [Q in T]: Y if T is related to S and X' is
        // related to Y, where X' is an instantiation of X in which P is replaced with Q. Notice
        // that S and T are contra-variant whereas X and Y are co-variant.
        // ported from: src/compiler/checker.ts:24170-24185 — <c>mappedTypeRelatedTo</c>
        Ternary MappedTypeRelatedTo(IMappedType source, IMappedType target, bool reportErrors)
        {
            var modifiersRelated = ReferenceEquals(relation, _comparableRelation) || (ReferenceEquals(relation, _identityRelation) ? GetMappedTypeModifiers(source) == GetMappedTypeModifiers(target) :
                GetCombinedMappedTypeOptionality(source) <= GetCombinedMappedTypeOptionality(target));
            if (modifiersRelated)
            {
                var mappedResult = Ternary.False;
                var targetConstraint = GetConstraintTypeFromMappedType(target);
                var sourceConstraint = InstantiateType(GetConstraintTypeFromMappedType(source), GetCombinedMappedTypeOptionality(source) < 0 ? _reportUnmeasurableMapper : _reportUnreliableMapper)!;
                if ((mappedResult = IsRelatedTo(targetConstraint, sourceConstraint, RecursionFlags.Both, reportErrors)) != Ternary.False)
                {
                    var mapper = CreateTypeMapper([GetTypeParameterFromMappedType(source)], [GetTypeParameterFromMappedType(target)]);
                    if (ReferenceEquals(InstantiateType(GetNameTypeFromMappedType(source), mapper), InstantiateType(GetNameTypeFromMappedType(target), mapper)))
                    {
                        return And(mappedResult, IsRelatedTo(InstantiateType(GetTemplateTypeFromMappedType(source), mapper)!, GetTemplateTypeFromMappedType(target), RecursionFlags.Both, reportErrors));
                    }
                }
            }
            return Ternary.False;
        }

        // ported from: src/compiler/checker.ts:24187-24281 — <c>typeRelatedToDiscriminatedType</c>
        /// <remarks>
        /// PORT-NOTE: the reference labels its inner loop <c>outer</c> and leaves it with <c>continue outer</c>;
        /// C# has no labeled <c>continue</c>, so the two escapes are <c>goto continueOuter</c> to a label at the end
        /// of the outer loop body — the same edge, expressed in the only form C# has.
        /// </remarks>
        Ternary TypeRelatedToDiscriminatedType(IType source, IUnionType target)
        {
            // 1. Generate the combinations of discriminant properties & types 'source' can satisfy.
            //    a. If the number of combinations is above a set limit, the comparison is too complex.
            // 2. Filter 'target' to the subset of types whose discriminants exist in the matrix.
            //    a. If 'target' does not satisfy all discriminants in the matrix, 'source' is not related.
            // 3. For each type in the filtered 'target', determine if all non-discriminant properties of
            //    'target' are related to a property in 'source'.
            //
            // NOTE: See ~/tests/cases/conformance/types/typeRelationships/assignmentCompatibility/assignmentCompatWithDiscriminatedUnion.ts
            //       for examples.

            var sourceProperties = GetPropertiesOfType(source);
            var sourcePropertiesFiltered = FindDiscriminantProperties(sourceProperties, target);
            if (sourcePropertiesFiltered is null) return Ternary.False;

            // Though we could compute the number of combinations as we generate
            // the matrix, this would incur additional memory overhead due to
            // array allocations. To reduce this overhead, we first compute
            // the number of combinations to ensure we will not surpass our
            // fixed limit before incurring the cost of any allocations:
            var numCombinations = 1;
            foreach (var sourceProperty in sourcePropertiesFiltered)
            {
                numCombinations *= CountTypes(GetNonMissingTypeOfSymbol(sourceProperty));
                if (numCombinations > 25)
                {
                    // We've reached the complexity limit.
                    Tracing.Current?.Instant(
                        Tracing.Phase.CheckTypes,
                        "typeRelatedToDiscriminatedType_DepthLimit",
                        TracingArgs.Of(("sourceId", source.Id), ("targetId", target.Id), ("numCombinations", numCombinations)));
                    return Ternary.False;
                }
            }

            // Compute the set of types for each discriminant property.
            var sourceDiscriminantTypes = new List<IList<IType>>(sourcePropertiesFiltered.Count);
            var excludedProperties = new HashSet<string>();
            for (var i = 0; i < sourcePropertiesFiltered.Count; i++)
            {
                var sourceProperty = sourcePropertiesFiltered[i];
                var sourcePropertyType = GetNonMissingTypeOfSymbol(sourceProperty);
                sourceDiscriminantTypes.Add((sourcePropertyType.Flags & TypeFlags.Union) != 0
                    ? ((IUnionType)sourcePropertyType).Types!
                    : [sourcePropertyType]);
                excludedProperties.Add(sourceProperty.EscapedName!);
            }

            // Match each combination of the cartesian product of discriminant properties to one or more
            // constituents of 'target'. If any combination does not have a match then 'source' is not relatable.
            var discriminantCombinations = Core.CartesianProduct(sourceDiscriminantTypes);
            var matchingTypes = new List<IType>();
            foreach (var combination in discriminantCombinations)
            {
                var hasMatch = false;
                foreach (var type in target.Types!)
                {
                    for (var i = 0; i < sourcePropertiesFiltered.Count; i++)
                    {
                        var sourceProperty = sourcePropertiesFiltered[i];
                        var targetProperty = GetPropertyOfType(type, sourceProperty.EscapedName);
                        if (targetProperty is null) goto continueOuter;
                        if (ReferenceEquals(sourceProperty, targetProperty)) continue;
                        // We compare the source property to the target in the context of a single discriminant type.
                        var related = PropertyRelatedTo(source, target, sourceProperty, targetProperty, _ => combination[i], /*reportErrors*/ false, IntersectionState.None, /*skipOptional*/ _strictNullChecks || ReferenceEquals(relation, _comparableRelation));
                        // If the target property could not be found, or if the properties were not related,
                        // then this constituent is not a match.
                        if (related == Ternary.False)
                        {
                            goto continueOuter;
                        }
                    }
                    Core.PushIfUnique(matchingTypes, type, Core.EquateValues);
                    hasMatch = true;
                    continueOuter: ;
                }
                if (!hasMatch)
                {
                    // We failed to match any type for this combination.
                    return Ternary.False;
                }
            }

            // Compare the remaining non-discriminant properties of each match.
            var discriminatedResult = Ternary.True;
            foreach (var type in matchingTypes)
            {
                discriminatedResult = And(discriminatedResult, PropertiesRelatedTo(source, type, /*reportErrors*/ false, excludedProperties, /*optionalsOnly*/ false, IntersectionState.None));
                if (discriminatedResult != Ternary.False)
                {
                    discriminatedResult = And(discriminatedResult, SignaturesRelatedTo(source, type, SignatureKind.Call, /*reportErrors*/ false, IntersectionState.None));
                    if (discriminatedResult != Ternary.False)
                    {
                        discriminatedResult = And(discriminatedResult, SignaturesRelatedTo(source, type, SignatureKind.Construct, /*reportErrors*/ false, IntersectionState.None));
                        if (discriminatedResult != Ternary.False && !(IsTupleType(source) && IsTupleType(type)))
                        {
                            // Comparing numeric index types when both `source` and `type` are tuples is unnecessary as the
                            // element types should be sufficiently covered by `propertiesRelatedTo`. It also causes problems
                            // with index type assignability as the types for the excluded discriminants are still included
                            // in the index type.
                            discriminatedResult = And(discriminatedResult, IndexSignaturesRelatedTo(source, type, /*sourceIsPrimitive*/ false, /*reportErrors*/ false, IntersectionState.None));
                        }
                    }
                }
                if (discriminatedResult == Ternary.False)
                {
                    return discriminatedResult;
                }
            }
            return discriminatedResult;
        }

        // ported from: src/compiler/checker.ts:24283-24297 — <c>excludeProperties</c>
        IList<ISymbol> ExcludeProperties(IList<ISymbol> properties, HashSet<string>? excludedProperties)
        {
            if (excludedProperties is null || properties.Count == 0) return properties;
            List<ISymbol>? result = null;
            for (var i = 0; i < properties.Count; i++)
            {
                if (!excludedProperties.Contains(properties[i].EscapedName!))
                {
                    if (result is not null)
                    {
                        result.Add(properties[i]);
                    }
                }
                else if (result is null)
                {
                    result = [];
                    for (var j = 0; j < i; j++)
                    {
                        result.Add(properties[j]);
                    }
                }
            }
            return result ?? properties;
        }

        // ported from: src/compiler/checker.ts:24299-24308 — <c>isPropertySymbolTypeRelated</c>
        Ternary IsPropertySymbolTypeRelated(ISymbol sourceProp, ISymbol targetProp, Func<ISymbol, IType> getTypeOfSourceProperty, bool reportErrors, IntersectionState intersectionState)
        {
            var targetIsOptional = _strictNullChecks && (GetCheckFlags(targetProp) & CheckFlags.Partial) != 0;
            var effectiveTarget = AddOptionality(GetNonMissingTypeOfSymbol(targetProp), /*isProperty*/ false, targetIsOptional);
            // source could resolve to `any` and that's not related to `unknown` target under strict subtype relation
            if ((effectiveTarget.Flags & (ReferenceEquals(relation, _strictSubtypeRelation) ? TypeFlags.Any : TypeFlags.AnyOrUnknown)) != 0)
            {
                return Ternary.True;
            }
            var effectiveSource = getTypeOfSourceProperty(sourceProp);
            return IsRelatedTo(effectiveSource, effectiveTarget, RecursionFlags.Both, reportErrors, /*headMessage*/ null, intersectionState);
        }

        // ported from: src/compiler/checker.ts:24310-24376 — <c>propertyRelatedTo</c>
        Ternary PropertyRelatedTo(IType source, IType target, ISymbol sourceProp, ISymbol targetProp, Func<ISymbol, IType> getTypeOfSourceProperty, bool reportErrors, IntersectionState intersectionState, bool skipOptional)
        {
            var sourcePropFlags = Utilities.GetDeclarationModifierFlagsFromSymbol(sourceProp);
            var targetPropFlags = Utilities.GetDeclarationModifierFlagsFromSymbol(targetProp);
            if ((sourcePropFlags & ModifierFlags.Private) != 0 || (targetPropFlags & ModifierFlags.Private) != 0)
            {
                if (!ReferenceEquals(sourceProp.ValueDeclaration, targetProp.ValueDeclaration))
                {
                    if (reportErrors)
                    {
                        if ((sourcePropFlags & ModifierFlags.Private) != 0 && (targetPropFlags & ModifierFlags.Private) != 0)
                        {
                            ReportError(Diagnostics.Types_have_separate_declarations_of_a_private_property_0, Diagnostic.DeferArg(() => SymbolToString(targetProp)));
                        }
                        else
                        {
                            ReportError(Diagnostics.Property_0_is_private_in_type_1_but_not_in_type_2, Diagnostic.DeferArg(() => SymbolToString(targetProp)), Diagnostic.DeferArg(() => TypeToString((sourcePropFlags & ModifierFlags.Private) != 0 ? source : target)), Diagnostic.DeferArg(() => TypeToString((sourcePropFlags & ModifierFlags.Private) != 0 ? target : source)));
                        }
                    }
                    return Ternary.False;
                }
            }
            else if ((targetPropFlags & ModifierFlags.Protected) != 0)
            {
                if (!IsValidOverrideOf(sourceProp, targetProp))
                {
                    if (reportErrors)
                    {
                        ReportError(Diagnostics.Property_0_is_protected_but_type_1_is_not_a_class_derived_from_2, Diagnostic.DeferArg(() => SymbolToString(targetProp)), Diagnostic.DeferArg(() => TypeToString(GetDeclaringClass(sourceProp) ?? source)), Diagnostic.DeferArg(() => TypeToString(GetDeclaringClass(targetProp) ?? target)));
                    }
                    return Ternary.False;
                }
            }
            else if ((sourcePropFlags & ModifierFlags.Protected) != 0)
            {
                if (reportErrors)
                {
                    ReportError(Diagnostics.Property_0_is_protected_in_type_1_but_public_in_type_2, Diagnostic.DeferArg(() => SymbolToString(targetProp)), Diagnostic.DeferArg(() => TypeToString(source)), Diagnostic.DeferArg(() => TypeToString(target)));
                }
                return Ternary.False;
            }

            // Ensure {readonly a: whatever} is not a subtype of {a: whatever},
            // while {a: whatever} is a subtype of {readonly a: whatever}.
            // This ensures the subtype relationship is ordered, and preventing declaration order
            // from deciding which type "wins" in union subtype reduction.
            // They're still assignable to one another, since `readonly` doesn't affect assignability.
            // This is only applied during the strictSubtypeRelation -- currently used in subtype reduction
            if (
                ReferenceEquals(relation, _strictSubtypeRelation) &&
                IsReadonlySymbol(sourceProp) && !IsReadonlySymbol(targetProp)
            )
            {
                return Ternary.False;
            }
            // If the target comes from a partial union prop, allow `undefined` in the target type
            var related = IsPropertySymbolTypeRelated(sourceProp, targetProp, getTypeOfSourceProperty, reportErrors, intersectionState);
            if (related == Ternary.False)
            {
                if (reportErrors)
                {
                    ReportIncompatibleError(Diagnostics.Types_of_property_0_are_incompatible, Diagnostic.DeferArg(() => SymbolToString(targetProp)));
                }
                return Ternary.False;
            }
            // When checking for comparability, be more lenient with optional properties.
            if (!skipOptional && (sourceProp.Flags & SymbolFlags.Optional) != 0 && (targetProp.Flags & SymbolFlags.ClassMember) != 0 && (targetProp.Flags & SymbolFlags.Optional) == 0)
            {
                // TypeScript 1.0 spec (April 2014): 3.8.3
                // S is a subtype of a type T, and T is a supertype of S if ...
                // S' and T are object types and, for each member M in T..
                // M is a property and S' contains a property N where
                // if M is a required property, N is also a required property
                // (M - property in T)
                // (N - property in S)
                if (reportErrors)
                {
                    ReportError(Diagnostics.Property_0_is_optional_in_type_1_but_required_in_type_2, Diagnostic.DeferArg(() => SymbolToString(targetProp)), Diagnostic.DeferArg(() => TypeToString(source)), Diagnostic.DeferArg(() => TypeToString(target)));
                }
                return Ternary.False;
            }
            return related;
        }

        // ported from: src/compiler/checker.ts:24378-24431 — <c>reportUnmatchedProperty</c>
        void ReportUnmatchedProperty(IType source, IType target, ISymbol unmatchedProperty, bool requireOptionalProperties)
        {
            var shouldSkipElaboration = false;
            // give specific error in case where private names have the same description
            if (
                unmatchedProperty.ValueDeclaration is not null
                && Utilities.IsNamedDeclaration((Node)unmatchedProperty.ValueDeclaration)
                && NodeTests.IsPrivateIdentifier((Node)((INamedDeclaration)unmatchedProperty.ValueDeclaration).Name!)
                && source.Symbol is not null
                && (source.Symbol.Flags & SymbolFlags.Class) != 0
            )
            {
                var privateIdentifierDescription = ((PrivateIdentifier)((INamedDeclaration)unmatchedProperty.ValueDeclaration).Name!).EscapedText!;
                var symbolTableKey = Utilities.GetSymbolNameForPrivateIdentifier(source.Symbol, privateIdentifierDescription);
                if (symbolTableKey is not null && GetPropertyOfType(source, symbolTableKey) is not null)
                {
                    // PORT-NOTE (deviation): the reference reads the two names with `factory.getDeclarationName` and
                    // then `sourceName.escapedText`. The port has no `getDeclarationName` on its factory
                    // (`FactoryUtilities.ParseNodeFactory`, factory/nodeFactory.ts is that slice's), but it does have
                    // `Utilities.GetNameOfDeclaration` — utilities.ts's own `getNameOfDeclaration`, which answers the
                    // same `DeclarationName | undefined` for a declaration node and is what the binder uses for this
                    // read — so the port calls that instead of opening a second gap. The following `.escapedText` read
                    // is `Utilities.GetEscapedTextOfIdentifierOrLiteral`, TS's helper for exactly that read.
                    var declaringName = Utilities.GetNameOfDeclaration((Node)source.Symbol.ValueDeclaration!);
                    var declaringTargetName = Utilities.GetNameOfDeclaration((Node)target.Symbol!.ValueDeclaration!);
                    ReportError(
                        Diagnostics.Property_0_in_type_1_refers_to_a_different_member_that_cannot_be_accessed_from_within_type_2,
                        DiagnosticName(privateIdentifierDescription),
                        DiagnosticName(Utilities.GetEscapedTextOfIdentifierOrLiteral((Node)declaringName!) == "" ? _anon : declaringName!),
                        DiagnosticName(Utilities.GetEscapedTextOfIdentifierOrLiteral((Node)declaringTargetName!) == "" ? _anon : declaringTargetName!));
                    return;
                }
            }
            var props = Core.ArrayFrom(GetUnmatchedProperties(source, target, requireOptionalProperties, /*matchDiscriminantProperties*/ false));
            if (
                headMessage is null || (headMessage.Code != Diagnostics.Class_0_incorrectly_implements_interface_1.Code &&
                    headMessage.Code != Diagnostics.Class_0_incorrectly_implements_class_1_Did_you_mean_to_extend_1_and_inherit_its_members_as_a_subclass.Code)
            )
            {
                shouldSkipElaboration = true; // Retain top-level error for interface implementing issues, otherwise omit it
            }
            if (props.Count == 1)
            {
                var propName = Diagnostic.DeferArg(() => SymbolToString(unmatchedProperty, /*enclosingDeclaration*/ null, SymbolFlags.None, SymbolFormatFlags.AllowAnyNodeKind | SymbolFormatFlags.WriteComputedProps));
                var (sourceName, targetName) = GetTypeNamesForErrorDisplay(source, target);
                ReportError(Diagnostics.Property_0_is_missing_in_type_1_but_required_in_type_2, propName, sourceName, targetName);
                if (Core.Length(unmatchedProperty.Declarations) != 0)
                {
                    AssociateRelatedInfo(Utilities.CreateDiagnosticForNode((Node)unmatchedProperty.Declarations![0], Diagnostics._0_is_declared_here, propName));
                }
                if (shouldSkipElaboration && errorInfo is not null)
                {
                    overrideNextErrorInfo++;
                }
            }
            else if (TryElaborateArrayLikeErrors(source, target, /*reportErrors*/ false))
            {
                if (props.Count > 5) // arbitrary cutoff for too-long list form
                {
                    ReportError(Diagnostics.Type_0_is_missing_the_following_properties_from_type_1_Colon_2_and_3_more, Diagnostic.DeferArg(() => TypeToString(source)), Diagnostic.DeferArg(() => TypeToString(target)), string.Join(", ", props.Take(4).Select(p => SymbolToString(p))), props.Count - 4);
                }
                else
                {
                    ReportError(Diagnostics.Type_0_is_missing_the_following_properties_from_type_1_Colon_2, Diagnostic.DeferArg(() => TypeToString(source)), Diagnostic.DeferArg(() => TypeToString(target)), string.Join(", ", props.Select(p => SymbolToString(p))));
                }
                if (shouldSkipElaboration && errorInfo is not null)
                {
                    overrideNextErrorInfo++;
                }
            }
            // No array like or unmatched property error - just issue top level error (errorInfo = undefined)
        }

        // ported from: src/compiler/checker.ts:24433-24576 — <c>propertiesRelatedTo</c>
        Ternary PropertiesRelatedTo(IType source, IType target, bool reportErrors, HashSet<string>? excludedProperties, bool optionalsOnly, IntersectionState intersectionState)
        {
            if (ReferenceEquals(relation, _identityRelation))
            {
                return PropertiesIdenticalTo(source, target, excludedProperties);
            }
            var result = Ternary.True;
            if (IsTupleType(target))
            {
                if (IsArrayOrTupleType(source))
                {
                    if (!TupleOf((ITupleTypeReference)target).Readonly && (IsReadonlyArrayType(source) || IsTupleType(source) && TupleOf((ITupleTypeReference)source).Readonly))
                    {
                        return Ternary.False;
                    }
                    var sourceArity = GetTypeReferenceArity((ITypeReference)source);
                    var targetArity = GetTypeReferenceArity((ITypeReference)target);
                    var sourceRestFlag = IsTupleType(source) ? TupleOf((ITupleTypeReference)source).CombinedFlags & ElementFlags.Rest : ElementFlags.Rest;
                    var targetHasRestElement = (TupleOf((ITupleTypeReference)target).CombinedFlags & ElementFlags.Variable) != 0;
                    var sourceMinLength = IsTupleType(source) ? TupleOf((ITupleTypeReference)source).MinLength : 0;
                    var targetMinLength = TupleOf((ITupleTypeReference)target).MinLength;
                    if ((sourceRestFlag & ElementFlags.Rest) == 0 && sourceArity < targetMinLength)
                    {
                        if (reportErrors)
                        {
                            ReportError(Diagnostics.Source_has_0_element_s_but_target_requires_1, sourceArity, targetMinLength);
                        }
                        return Ternary.False;
                    }
                    if (!targetHasRestElement && targetArity < sourceMinLength)
                    {
                        if (reportErrors)
                        {
                            ReportError(Diagnostics.Source_has_0_element_s_but_target_allows_only_1, sourceMinLength, targetArity);
                        }
                        return Ternary.False;
                    }
                    if (!targetHasRestElement && ((sourceRestFlag & ElementFlags.Rest) != 0 || targetArity < sourceArity))
                    {
                        if (reportErrors)
                        {
                            if (sourceMinLength < targetMinLength)
                            {
                                ReportError(Diagnostics.Target_requires_0_element_s_but_source_may_have_fewer, targetMinLength);
                            }
                            else
                            {
                                ReportError(Diagnostics.Target_allows_only_0_element_s_but_source_may_have_more, targetArity);
                            }
                        }
                        return Ternary.False;
                    }
                    var sourceTypeArguments = GetTypeArguments((ITypeReference)source);
                    var targetTypeArguments = GetTypeArguments((ITypeReference)target);
                    var targetStartCount = GetStartElementCount((ITupleType)TupleOf((ITupleTypeReference)target), ElementFlags.NonRest);
                    var targetEndCount = GetEndElementCount((ITupleType)TupleOf((ITupleTypeReference)target), ElementFlags.NonRest);
                    var canExcludeDiscriminants = excludedProperties is not null;
                    for (var sourcePosition = 0; sourcePosition < sourceArity; sourcePosition++)
                    {
                        var sourceFlags = IsTupleType(source) ? TupleOf((ITupleTypeReference)source).ElementFlags![sourcePosition] : ElementFlags.Rest;
                        var sourcePositionFromEnd = sourceArity - 1 - sourcePosition;

                        var targetPosition = targetHasRestElement && sourcePosition >= targetStartCount
                            ? targetArity - 1 - Math.Min(sourcePositionFromEnd, targetEndCount)
                            : sourcePosition;

                        var targetFlags = TupleOf((ITupleTypeReference)target).ElementFlags![targetPosition];

                        if ((targetFlags & ElementFlags.Variadic) != 0 && (sourceFlags & ElementFlags.Variadic) == 0)
                        {
                            if (reportErrors)
                            {
                                ReportError(Diagnostics.Source_provides_no_match_for_variadic_element_at_position_0_in_target, targetPosition);
                            }
                            return Ternary.False;
                        }
                        if ((sourceFlags & ElementFlags.Variadic) != 0 && (targetFlags & ElementFlags.Variable) == 0)
                        {
                            if (reportErrors)
                            {
                                ReportError(Diagnostics.Variadic_element_at_position_0_in_source_does_not_match_element_at_position_1_in_target, sourcePosition, targetPosition);
                            }
                            return Ternary.False;
                        }
                        if ((targetFlags & ElementFlags.Required) != 0 && (sourceFlags & ElementFlags.Required) == 0)
                        {
                            if (reportErrors)
                            {
                                ReportError(Diagnostics.Source_provides_no_match_for_required_element_at_position_0_in_target, targetPosition);
                            }
                            return Ternary.False;
                        }
                        // We can only exclude discriminant properties if we have not yet encountered a variable-length element.
                        if (canExcludeDiscriminants)
                        {
                            if ((sourceFlags & ElementFlags.Variable) != 0 || (targetFlags & ElementFlags.Variable) != 0)
                            {
                                canExcludeDiscriminants = false;
                            }
                            if (canExcludeDiscriminants && excludedProperties!.Contains(sourcePosition.ToString()))
                            {
                                continue;
                            }
                        }

                        var sourceType = RemoveMissingType(sourceTypeArguments[sourcePosition], (sourceFlags & targetFlags & ElementFlags.Optional) != 0);
                        var targetType = targetTypeArguments[targetPosition];

                        var targetCheckType = (sourceFlags & ElementFlags.Variadic) != 0 && (targetFlags & ElementFlags.Rest) != 0 ? CreateArrayType(targetType) :
                            RemoveMissingType(targetType, (targetFlags & ElementFlags.Optional) != 0);
                        var related = IsRelatedTo(sourceType, targetCheckType, RecursionFlags.Both, reportErrors, /*headMessage*/ null, intersectionState);
                        if (related == Ternary.False)
                        {
                            if (reportErrors && (targetArity > 1 || sourceArity > 1))
                            {
                                if (targetHasRestElement && sourcePosition >= targetStartCount && sourcePositionFromEnd >= targetEndCount && targetStartCount != sourceArity - targetEndCount - 1)
                                {
                                    ReportIncompatibleError(Diagnostics.Type_at_positions_0_through_1_in_source_is_not_compatible_with_type_at_position_2_in_target, targetStartCount, sourceArity - targetEndCount - 1, targetPosition);
                                }
                                else
                                {
                                    ReportIncompatibleError(Diagnostics.Type_at_position_0_in_source_is_not_compatible_with_type_at_position_1_in_target, sourcePosition, targetPosition);
                                }
                            }
                            return Ternary.False;
                        }
                        result = And(result, related);
                    }
                    return result;
                }
                if ((TupleOf((ITupleTypeReference)target).CombinedFlags & ElementFlags.Variable) != 0)
                {
                    return Ternary.False;
                }
            }
            var requireOptionalProperties = (ReferenceEquals(relation, _subtypeRelation) || ReferenceEquals(relation, _strictSubtypeRelation)) && !IsObjectLiteralType(source) && !IsEmptyArrayLiteralType(source) && !IsTupleType(source);
            var unmatchedProperty = GetUnmatchedProperty(source, target, requireOptionalProperties, /*matchDiscriminantProperties*/ false);
            if (unmatchedProperty is not null)
            {
                if (reportErrors && ShouldReportUnmatchedPropertyError(source, target))
                {
                    ReportUnmatchedProperty(source, target, unmatchedProperty, requireOptionalProperties);
                }
                return Ternary.False;
            }
            if (IsObjectLiteralType(target))
            {
                foreach (var sourceProp in ExcludeProperties(GetPropertiesOfType(source), excludedProperties))
                {
                    if (GetPropertyOfObjectType(target, sourceProp.EscapedName!) is null)
                    {
                        if (reportErrors)
                        {
                            ReportError(Diagnostics.Property_0_does_not_exist_on_type_1, Diagnostic.DeferArg(() => SymbolToString(sourceProp)), Diagnostic.DeferArg(() => TypeToString(target)));
                        }
                        return Ternary.False;
                    }
                }
            }
            // We only call this for union target types when we're attempting to do excess property checking - in those cases, we want to get _all possible props_
            // from the target union, across all members
            var properties = GetPropertiesOfType(target);
            var numericNamesOnly = IsTupleType(source) && IsTupleType(target);
            foreach (var targetProp in ExcludeProperties(properties, excludedProperties))
            {
                var name = targetProp.EscapedName!;
                if ((targetProp.Flags & SymbolFlags.Prototype) == 0 && (!numericNamesOnly || Utilities.IsNumericLiteralName(name) || name == "length") && (!optionalsOnly || (targetProp.Flags & SymbolFlags.Optional) != 0))
                {
                    var sourceProp = GetPropertyOfType(source, name);
                    if (sourceProp is not null && !ReferenceEquals(sourceProp, targetProp))
                    {
                        var related = PropertyRelatedTo(source, target, sourceProp, targetProp, GetNonMissingTypeOfSymbol, reportErrors, intersectionState, ReferenceEquals(relation, _comparableRelation));
                        if (related == Ternary.False)
                        {
                            return Ternary.False;
                        }
                        result = And(result, related);
                    }
                }
            }
            return result;
        }

        // ported from: src/compiler/checker.ts:24578-24600 — <c>propertiesIdenticalTo</c>
        Ternary PropertiesIdenticalTo(IType source, IType target, HashSet<string>? excludedProperties)
        {
            if ((source.Flags & TypeFlags.Object) == 0 || (target.Flags & TypeFlags.Object) == 0)
            {
                return Ternary.False;
            }
            var sourceProperties = ExcludeProperties(GetPropertiesOfObjectType(source), excludedProperties);
            var targetProperties = ExcludeProperties(GetPropertiesOfObjectType(target), excludedProperties);
            if (sourceProperties.Count != targetProperties.Count)
            {
                return Ternary.False;
            }
            var result = Ternary.True;
            foreach (var sourceProp in sourceProperties)
            {
                var targetProp = GetPropertyOfObjectType(target, sourceProp.EscapedName!);
                if (targetProp is null)
                {
                    return Ternary.False;
                }
                var related = CompareProperties(sourceProp, targetProp, (s, t, reportErrors) => IsRelatedTo(s, t, RecursionFlags.Both, reportErrors));
                if (related == Ternary.False)
                {
                    return Ternary.False;
                }
                result = And(result, related);
            }
            return result;
        }
        // ported from: src/compiler/checker.ts:24602-24709 — <c>signaturesRelatedTo</c>
        /// <remarks>
        /// PORT-NOTE: the reference leaves its multi-signature search with <c>continue outer</c>; C# has no labeled
        /// <c>continue</c>, so the port carries the same edge as a <c>matched</c> flag plus <c>continue</c>.
        /// </remarks>
        Ternary SignaturesRelatedTo(IType source, IType target, SignatureKind kind, bool reportErrors, IntersectionState intersectionState)
        {
            if (ReferenceEquals(relation, _identityRelation))
            {
                return SignaturesIdenticalTo(source, target, kind);
            }
            // With respect to signatures, the anyFunctionType wildcard is a subtype of every other function type.
            if (ReferenceEquals(source, _anyFunctionType))
            {
                return Ternary.True;
            }
            if (ReferenceEquals(target, _anyFunctionType))
            {
                return Ternary.False;
            }

            var sourceIsJSConstructor = source.Symbol is not null && IsJSConstructor((Node)source.Symbol.ValueDeclaration!);
            var targetIsJSConstructor = target.Symbol is not null && IsJSConstructor((Node)target.Symbol.ValueDeclaration!);

            var sourceSignatures = GetSignaturesOfType(
                source,
                sourceIsJSConstructor && kind == SignatureKind.Construct ?
                    SignatureKind.Call : kind);
            var targetSignatures = GetSignaturesOfType(
                target,
                targetIsJSConstructor && kind == SignatureKind.Construct ?
                    SignatureKind.Call : kind);

            if (kind == SignatureKind.Construct && sourceSignatures.Count != 0 && targetSignatures.Count != 0)
            {
                var sourceIsAbstract = (sourceSignatures[0].Flags & SignatureFlags.Abstract) != 0;
                var targetIsAbstract = (targetSignatures[0].Flags & SignatureFlags.Abstract) != 0;
                if (sourceIsAbstract && !targetIsAbstract)
                {
                    // An abstract constructor type is not assignable to a non-abstract constructor type
                    // as it would otherwise be possible to new an abstract class. Note that the assignability
                    // check we perform for an extends clause excludes construct signatures from the target,
                    // so this check never proceeds.
                    if (reportErrors)
                    {
                        ReportError(Diagnostics.Cannot_assign_an_abstract_constructor_type_to_a_non_abstract_constructor_type);
                    }
                    return Ternary.False;
                }
                if (!ConstructorVisibilitiesAreCompatible(sourceSignatures[0], targetSignatures[0], reportErrors))
                {
                    return Ternary.False;
                }
            }

            var result = Ternary.True;
            Func<ISignature, ISignature, Action<IType, IType>> incompatibleReporter = kind == SignatureKind.Construct ? ReportIncompatibleConstructSignatureReturn : ReportIncompatibleCallSignatureReturn;
            var sourceObjectFlags = GetObjectFlags(source);
            var targetObjectFlags = GetObjectFlags(target);
            if (
                (sourceObjectFlags & ObjectFlags.Instantiated) != 0 && (targetObjectFlags & ObjectFlags.Instantiated) != 0 && ReferenceEquals(source.Symbol, target.Symbol) ||
                (sourceObjectFlags & ObjectFlags.Reference) != 0 && (targetObjectFlags & ObjectFlags.Reference) != 0 && ReferenceEquals(((ITypeReference)source).Target, ((ITypeReference)target).Target)
            )
            {
                // We have instantiations of the same anonymous type (which typically will be the type of a
                // method). Simply do a pairwise comparison of the signatures in the two signature lists instead
                // of the much more expensive N * M comparison matrix we explore below. We erase type parameters
                // as they are known to always be the same.
                Debug.AssertEqual(sourceSignatures.Count, targetSignatures.Count);
                for (var i = 0; i < targetSignatures.Count; i++)
                {
                    var related = SignatureRelatedTo(sourceSignatures[i], targetSignatures[i], /*erase*/ true, reportErrors, intersectionState, incompatibleReporter(sourceSignatures[i], targetSignatures[i]));
                    if (related == Ternary.False)
                    {
                        return Ternary.False;
                    }
                    result = And(result, related);
                }
            }
            else if (sourceSignatures.Count == 1 && targetSignatures.Count == 1)
            {
                // For simple functions (functions with a single signature) we only erase type parameters for
                // the comparable relation. Otherwise, if the source signature is generic, we instantiate it
                // in the context of the target signature before checking the relationship. Ideally we'd do
                // this regardless of the number of signatures, but the potential costs are prohibitive due
                // to the quadratic nature of the logic below.
                var eraseGenerics = ReferenceEquals(relation, _comparableRelation);
                var sourceSignature = sourceSignatures[0];
                var targetSignature = targetSignatures[0];
                result = SignatureRelatedTo(sourceSignature, targetSignature, eraseGenerics, reportErrors, intersectionState, incompatibleReporter(sourceSignature, targetSignature));
                if (
                    result == Ternary.False && reportErrors && kind == SignatureKind.Construct && (sourceObjectFlags & targetObjectFlags) != 0 &&
                    (targetSignature.Declaration?.Kind == SyntaxKind.Constructor || sourceSignature.Declaration?.Kind == SyntaxKind.Constructor)
                )
                {
                    DiagnosticArgument ConstructSignatureToString(ISignature signature) => Diagnostic.DeferArg(() => SignatureToString(signature, /*enclosingDeclaration*/ null, TypeFormatFlags.WriteArrowStyleSignature, kind));
                    ReportError(Diagnostics.Type_0_is_not_assignable_to_type_1, ConstructSignatureToString(sourceSignature), ConstructSignatureToString(targetSignature));
                    ReportError(Diagnostics.Types_of_construct_signatures_are_incompatible);
                    return result;
                }
            }
            else
            {
                foreach (var t in targetSignatures)
                {
                    var saveErrorInfo = CaptureErrorCalculationState();
                    // Only elaborate errors from the first failure
                    var shouldElaborateErrors = reportErrors;
                    var matched = false;
                    foreach (var s in sourceSignatures)
                    {
                        var related = SignatureRelatedTo(s, t, /*erase*/ true, shouldElaborateErrors, intersectionState, incompatibleReporter(s, t));
                        if (related != Ternary.False)
                        {
                            result = And(result, related);
                            ResetErrorInfo(saveErrorInfo);
                            matched = true;
                            break;
                        }
                        shouldElaborateErrors = false;
                    }
                    if (matched) continue;
                    if (shouldElaborateErrors)
                    {
                        ReportError(Diagnostics.Type_0_provides_no_match_for_the_signature_1, Diagnostic.DeferArg(() => TypeToString(source)), Diagnostic.DeferArg(() => SignatureToString(t, /*enclosingDeclaration*/ null, /*flags*/ null, kind)));
                    }
                    return Ternary.False;
                }
            }
            return result;
        }

        // ported from: src/compiler/checker.ts:24711-24725 — <c>shouldReportUnmatchedPropertyError</c>
        bool ShouldReportUnmatchedPropertyError(IType source, IType target)
        {
            var typeCallSignatures = GetSignaturesOfStructuredType(source, SignatureKind.Call);
            var typeConstructSignatures = GetSignaturesOfStructuredType(source, SignatureKind.Construct);
            var typeProperties = GetPropertiesOfObjectType(source);
            if ((typeCallSignatures.Count != 0 || typeConstructSignatures.Count != 0) && typeProperties.Count == 0)
            {
                if (
                    (GetSignaturesOfType(target, SignatureKind.Call).Count != 0 && typeCallSignatures.Count != 0) ||
                    (GetSignaturesOfType(target, SignatureKind.Construct).Count != 0 && typeConstructSignatures.Count != 0)
                )
                {
                    return true; // target has similar signature kinds to source, still focus on the unmatched property
                }
                return false;
            }
            return true;
        }

        // ported from: src/compiler/checker.ts:24727-24732 — <c>reportIncompatibleCallSignatureReturn</c>
        Action<IType, IType> ReportIncompatibleCallSignatureReturn(ISignature siga, ISignature sigb)
        {
            if (siga.Parameters!.Count == 0 && sigb.Parameters!.Count == 0)
            {
                return (source, target) => ReportIncompatibleError(Diagnostics.Call_signatures_with_no_arguments_have_incompatible_return_types_0_and_1, Diagnostic.DeferArg(() => TypeToString(source)), Diagnostic.DeferArg(() => TypeToString(target)));
            }
            return (source, target) => ReportIncompatibleError(Diagnostics.Call_signature_return_types_0_and_1_are_incompatible, Diagnostic.DeferArg(() => TypeToString(source)), Diagnostic.DeferArg(() => TypeToString(target)));
        }

        // ported from: src/compiler/checker.ts:24734-24739 — <c>reportIncompatibleConstructSignatureReturn</c>
        Action<IType, IType> ReportIncompatibleConstructSignatureReturn(ISignature siga, ISignature sigb)
        {
            if (siga.Parameters!.Count == 0 && sigb.Parameters!.Count == 0)
            {
                return (source, target) => ReportIncompatibleError(Diagnostics.Construct_signatures_with_no_arguments_have_incompatible_return_types_0_and_1, Diagnostic.DeferArg(() => TypeToString(source)), Diagnostic.DeferArg(() => TypeToString(target)));
            }
            return (source, target) => ReportIncompatibleError(Diagnostics.Construct_signature_return_types_0_and_1_are_incompatible, Diagnostic.DeferArg(() => TypeToString(source)), Diagnostic.DeferArg(() => TypeToString(target)));
        }

        /// <summary>
        /// See signatureAssignableTo, compareSignaturesIdentical
        /// ported from: src/compiler/checker.ts:24744-24752 — <c>signatureRelatedTo</c>.
        /// </summary>
        Ternary SignatureRelatedTo(ISignature source, ISignature target, bool erase, bool reportErrors, IntersectionState intersectionState, Action<IType, IType> incompatibleReporter)
        {
            var checkMode = ReferenceEquals(relation, _subtypeRelation) ? SignatureCheckMode.StrictTopSignature :
                ReferenceEquals(relation, _strictSubtypeRelation) ? SignatureCheckMode.StrictTopSignature | SignatureCheckMode.StrictArity :
                SignatureCheckMode.None;
            return CompareSignaturesRelated(erase ? GetErasedSignature(source) : source, erase ? GetErasedSignature(target) : target, checkMode, reportErrors, ReportError, incompatibleReporter, IsRelatedToWorker, _reportUnreliableMapper);

            // ported from: src/compiler/checker.ts:24749-24751 — <c>isRelatedToWorker</c>
            Ternary IsRelatedToWorker(IType source, IType target, bool reportErrors = false)
                => IsRelatedTo(source, target, RecursionFlags.Both, reportErrors, /*headMessage*/ null, intersectionState);
        }

        // ported from: src/compiler/checker.ts:24754-24769 — <c>signaturesIdenticalTo</c>
        Ternary SignaturesIdenticalTo(IType source, IType target, SignatureKind kind)
        {
            var sourceSignatures = GetSignaturesOfType(source, kind);
            var targetSignatures = GetSignaturesOfType(target, kind);
            if (sourceSignatures.Count != targetSignatures.Count)
            {
                return Ternary.False;
            }
            var result = Ternary.True;
            for (var i = 0; i < sourceSignatures.Count; i++)
            {
                var related = CompareSignaturesIdentical(sourceSignatures[i], targetSignatures[i], /*partialMatch*/ false, /*ignoreThisTypes*/ false, /*ignoreReturnTypes*/ false, (s, t, reportErrors) => IsRelatedTo(s, t, RecursionFlags.Both, reportErrors));
                if (related == Ternary.False)
                {
                    return Ternary.False;
                }
                result = And(result, related);
            }
            return result;
        }

        // ported from: src/compiler/checker.ts:24771-24805 — <c>membersRelatedToIndexInfo</c>
        Ternary MembersRelatedToIndexInfo(IType source, IIndexInfo targetInfo, bool reportErrors, IntersectionState intersectionState)
        {
            var result = Ternary.True;
            var keyType = targetInfo.KeyType!;
            var props = (source.Flags & TypeFlags.Intersection) != 0 ? GetPropertiesOfUnionOrIntersectionType((UnionOrIntersectionType)source) : GetPropertiesOfObjectType(source);
            foreach (var prop in props)
            {
                // Skip over ignored JSX and symbol-named members
                if (IsIgnoredJsxProperty(source, prop))
                {
                    continue;
                }
                if (IsApplicableIndexType(GetLiteralTypeFromProperty(prop, TypeFlags.StringOrNumberLiteralOrUnique), keyType))
                {
                    var propType = GetNonMissingTypeOfSymbol(prop);
                    var type = _exactOptionalPropertyTypes == true || (propType.Flags & TypeFlags.Undefined) != 0 || ReferenceEquals(keyType, _numberType) || (prop.Flags & SymbolFlags.Optional) == 0
                        ? propType
                        : GetTypeWithFacts(propType, TypeFacts.NEUndefined);
                    var related = IsRelatedTo(type, targetInfo.Type!, RecursionFlags.Both, reportErrors, /*headMessage*/ null, intersectionState);
                    if (related == Ternary.False)
                    {
                        if (reportErrors)
                        {
                            ReportError(Diagnostics.Property_0_is_incompatible_with_index_signature, Diagnostic.DeferArg(() => SymbolToString(prop)));
                        }
                        return Ternary.False;
                    }
                    result = And(result, related);
                }
            }
            foreach (var info in GetIndexInfosOfType(source))
            {
                if (IsApplicableIndexType(info.KeyType!, keyType))
                {
                    var related = IndexInfoRelatedTo(info, targetInfo, reportErrors, intersectionState);
                    if (related == Ternary.False)
                    {
                        return Ternary.False;
                    }
                    result = And(result, related);
                }
            }
            return result;
        }

        // ported from: src/compiler/checker.ts:24807-24818 — <c>indexInfoRelatedTo</c>
        Ternary IndexInfoRelatedTo(IIndexInfo sourceInfo, IIndexInfo targetInfo, bool reportErrors, IntersectionState intersectionState)
        {
            var related = IsRelatedTo(sourceInfo.Type!, targetInfo.Type!, RecursionFlags.Both, reportErrors, /*headMessage*/ null, intersectionState);
            if (related == Ternary.False && reportErrors)
            {
                if (ReferenceEquals(sourceInfo.KeyType, targetInfo.KeyType))
                {
                    ReportError(Diagnostics._0_index_signatures_are_incompatible, Diagnostic.DeferArg(() => TypeToString(sourceInfo.KeyType!)));
                }
                else
                {
                    ReportError(Diagnostics._0_and_1_index_signatures_are_incompatible, Diagnostic.DeferArg(() => TypeToString(sourceInfo.KeyType!)), Diagnostic.DeferArg(() => TypeToString(targetInfo.KeyType!)));
                }
            }
            return related;
        }

        // ported from: src/compiler/checker.ts:24820-24837 — <c>indexSignaturesRelatedTo</c>
        Ternary IndexSignaturesRelatedTo(IType source, IType target, bool sourceIsPrimitive, bool reportErrors, IntersectionState intersectionState)
        {
            if (ReferenceEquals(relation, _identityRelation))
            {
                return IndexSignaturesIdenticalTo(source, target);
            }
            var indexInfos = GetIndexInfosOfType(target);
            var targetHasStringIndex = Core.Some(indexInfos, info => ReferenceEquals(info.KeyType, _stringType));
            var result = Ternary.True;
            foreach (var targetInfo in indexInfos)
            {
                var related = !ReferenceEquals(relation, _strictSubtypeRelation) && !sourceIsPrimitive && targetHasStringIndex && (targetInfo.Type!.Flags & TypeFlags.Any) != 0 ? Ternary.True :
                    IsGenericMappedType(source) && targetHasStringIndex ? IsRelatedTo(GetTemplateTypeFromMappedType((IMappedType)source), targetInfo.Type!, RecursionFlags.Both, reportErrors) :
                    TypeRelatedToIndexInfo(source, targetInfo, reportErrors, intersectionState);
                if (related == Ternary.False)
                {
                    return Ternary.False;
                }
                result = And(result, related);
            }
            return result;
        }

        // ported from: src/compiler/checker.ts:24839-24854 — <c>typeRelatedToIndexInfo</c>
        Ternary TypeRelatedToIndexInfo(IType source, IIndexInfo targetInfo, bool reportErrors, IntersectionState intersectionState)
        {
            var sourceInfo = GetApplicableIndexInfo(source, targetInfo.KeyType!);
            if (sourceInfo is not null)
            {
                return IndexInfoRelatedTo(sourceInfo, targetInfo, reportErrors, intersectionState);
            }
            // Intersection constituents are never considered to have an inferred index signature. Also, in the strict subtype relation,
            // only fresh object literals are considered to have inferred index signatures. This ensures { [x: string]: xxx } <: {} but
            // not vice-versa. Without this rule, those types would be mutual strict subtypes.
            if ((intersectionState & IntersectionState.Source) == 0 && (!ReferenceEquals(relation, _strictSubtypeRelation) || (GetObjectFlags(source) & ObjectFlags.FreshLiteral) != 0) && IsObjectTypeWithInferableIndex(source))
            {
                return MembersRelatedToIndexInfo(source, targetInfo, reportErrors, intersectionState);
            }
            if (reportErrors)
            {
                ReportError(Diagnostics.Index_signature_for_type_0_is_missing_in_type_1, Diagnostic.DeferArg(() => TypeToString(targetInfo.KeyType!)), Diagnostic.DeferArg(() => TypeToString(source)));
            }
            return Ternary.False;
        }

        // ported from: src/compiler/checker.ts:24856-24869 — <c>indexSignaturesIdenticalTo</c>
        Ternary IndexSignaturesIdenticalTo(IType source, IType target)
        {
            var sourceInfos = GetIndexInfosOfType(source);
            var targetInfos = GetIndexInfosOfType(target);
            if (sourceInfos.Count != targetInfos.Count)
            {
                return Ternary.False;
            }
            foreach (var targetInfo in targetInfos)
            {
                var sourceInfo = GetIndexInfoOfType(source, targetInfo.KeyType!);
                if (!(sourceInfo is not null && IsRelatedTo(sourceInfo.Type!, targetInfo.Type!, RecursionFlags.Both) != Ternary.False && sourceInfo.IsReadonly == targetInfo.IsReadonly))
                {
                    return Ternary.False;
                }
            }
            return Ternary.True;
        }

        // ported from: src/compiler/checker.ts:24871-24899 — <c>constructorVisibilitiesAreCompatible</c>
        bool ConstructorVisibilitiesAreCompatible(ISignature sourceSignature, ISignature targetSignature, bool reportErrors)
        {
            if (sourceSignature.Declaration is null || targetSignature.Declaration is null)
            {
                return true;
            }

            var sourceAccessibility = Utilities.GetSelectedEffectiveModifierFlags(sourceSignature.Declaration, ModifierFlags.NonPublicAccessibilityModifier);
            var targetAccessibility = Utilities.GetSelectedEffectiveModifierFlags(targetSignature.Declaration, ModifierFlags.NonPublicAccessibilityModifier);

            // A public, protected and private signature is assignable to a private signature.
            if (targetAccessibility == ModifierFlags.Private)
            {
                return true;
            }

            // A public and protected signature is assignable to a protected signature.
            if (targetAccessibility == ModifierFlags.Protected && sourceAccessibility != ModifierFlags.Private)
            {
                return true;
            }

            // Only a public signature is assignable to public signature.
            if (targetAccessibility != ModifierFlags.Protected && sourceAccessibility == ModifierFlags.None)
            {
                return true;
            }

            if (reportErrors)
            {
                ReportError(Diagnostics.Cannot_assign_a_0_constructor_type_to_a_1_constructor_type, VisibilityToString(sourceAccessibility), VisibilityToString(targetAccessibility));
            }

            return false;
        }
    }
}
