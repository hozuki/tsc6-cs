namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:39601-39639 (checkFunctionExpressionOrObjectLiteralMethod)
// ported from: src/compiler/checker.ts:39641-39690 (contextuallyCheckFunctionExpressionOrObjectLiteralMethod)
// ported from: src/compiler/checker.ts:39692-39725 (checkFunctionExpressionOrObjectLiteralMethodDeferred)
// ported from: src/compiler/checker.ts:39727-39738 (checkArithmeticOperandType)
// ported from: src/compiler/checker.ts:39740-39769 (isReadonlyAssignmentDeclaration)
// ported from: src/compiler/checker.ts:39771-39785 (isReadonlySymbol)
// ported from: src/compiler/checker.ts:39787-39831 (isAssignmentToReadonlyEntity)
// ported from: src/compiler/checker.ts:39833-39845 (checkReferenceExpression)
// ported from: src/compiler/checker.ts:39847-39868 (checkDeleteExpression)
// ported from: src/compiler/checker.ts:39870-39879 (checkDeleteExpressionMustBeOptional)
// ported from: src/compiler/checker.ts:39881-39884 (checkTypeOfExpression)
// ported from: src/compiler/checker.ts:39886-39889 (checkVoidExpression)
// ported from: src/compiler/checker.ts:39891-39972 (checkAwaitGrammar)
// ported from: src/compiler/checker.ts:39974-39983 (checkAwaitExpression)
// ported from: src/compiler/checker.ts:39985-40042 (checkPrefixUnaryExpression)
// ported from: src/compiler/checker.ts:40044-40063 (checkPostfixUnaryExpression)
// ported from: src/compiler/checker.ts:40065-40073 (getUnaryResultType)
// ported from: src/compiler/checker.ts:40075-40082 (maybeTypeOfKindConsideringBaseConstraint)
// ported from: src/compiler/checker.ts:40086-40099 (maybeTypeOfKind)
// ported from: src/compiler/checker.ts:40101-40118 (isTypeAssignableToKind)
// ported from: src/compiler/checker.ts:40120-40124 (allTypesAssignableToKind)
// ported from: src/compiler/checker.ts:40126-40128 (isConstEnumObjectType)
// ported from: src/compiler/checker.ts:40130-40132 (isConstEnumSymbol)
// ported from: src/compiler/checker.ts:40137-40148 (getSymbolHasInstanceMethodOfObjectType)
// ported from: src/compiler/checker.ts:40150-40187 (checkInstanceOfExpression)
// ported from: src/compiler/checker.ts:40189-40191 (hasEmptyObjectIntersection)
// ported from: src/compiler/checker.ts:40193-40228 (checkInExpression)
// ported from: src/compiler/checker.ts:40230-40239 (checkObjectLiteralAssignment)
// ported from: src/compiler/checker.ts:40242-40284 (checkObjectLiteralDestructuringPropertyAssignment)
// ported from: src/compiler/checker.ts:40286-40304 (checkArrayLiteralAssignment)
// ported from: src/compiler/checker.ts:40306-40341 (checkArrayLiteralDestructuringElementAssignment)
// ported from: src/compiler/checker.ts:40343-40379 (checkDestructuringAssignment)
// ported from: src/compiler/checker.ts:40381-40397 (checkReferenceAssignment)
// ported from: src/compiler/checker.ts:40407-40464 (isSideEffectFree)
// ported from: src/compiler/checker.ts:40466-40468 (isTypeEqualityComparableTo)
// ported from: src/compiler/checker.ts:40470-40612 (createCheckBinaryExpression)
// ported from: src/compiler/checker.ts:40614-40637 (checkNullishCoalesceOperands)
// ported from: src/compiler/checker.ts:40639-40650 (checkNullishCoalesceOperandLeft)
// ported from: src/compiler/checker.ts:40652-40693 (getSyntacticNullishnessSemantics)
// ported from: src/compiler/checker.ts:40697-40712 (checkBinaryLikeExpression)
// ported from: src/compiler/checker.ts:40714-41178 (checkBinaryLikeExpressionWorker)
// ported from: src/compiler/checker.ts:41180-41190 (getBaseTypesIfUnrelated)
// ported from: src/compiler/checker.ts:41192-41266 (checkYieldExpression)
// ported from: src/compiler/checker.ts:41268-41274 (checkConditionalExpression)
// ported from: src/compiler/checker.ts:41276-41280 (isTemplateLiteralContext)
// ported from: src/compiler/checker.ts:41282-41301 (checkTemplateExpression)
// ported from: src/compiler/checker.ts:41303-41306 (isTemplateLiteralContextualType)
// ported from: src/compiler/checker.ts:41308-41313 (getContextNode)
// ported from: src/compiler/checker.ts:41315-41333 (checkExpressionWithContextualType)
// ported from: src/compiler/checker.ts:41335-41353 (checkExpressionCached)
// ported from: src/compiler/checker.ts:41355-41360 (isTypeAssertion)
// ported from: src/compiler/checker.ts:41362-41386 (checkDeclarationInitializer)
// ported from: src/compiler/checker.ts:41388-41413 (padObjectLiteralType)
// ported from: src/compiler/checker.ts:41415-41418 (getPropertyNameFromBindingElement)
// ported from: src/compiler/checker.ts:41420-41438 (padTupleType)
// ported from: src/compiler/checker.ts:41440-41453 (widenTypeInferredFromInitializer)
// ported from: src/compiler/checker.ts:41455-41457 (getWidenedLiteralTypeForInitializer)
// ported from: src/compiler/checker.ts:41459-41485 (isLiteralOfContextualType)
// ported from: src/compiler/checker.ts:41487-41494 (isConstContext)
// ported from: src/compiler/checker.ts:41496-41501 (checkExpressionForMutableLocation)
// ported from: src/compiler/checker.ts:41503-41512 (checkPropertyAssignment)
// ported from: src/compiler/checker.ts:41514-41527 (checkObjectLiteralMethod)
// ported from: src/compiler/checker.ts:41529-41585 (instantiateTypeWithSingleGenericCallSignature)
// ported from: src/compiler/checker.ts:41587-41594 (skippedGenericFunction)
// ported from: src/compiler/transformers/utilities.ts:490-493 (isCompoundAssignment)
/// <summary>
/// The operator block: function expressions and object literal methods, the unary and binary operator checks, the
/// destructuring-assignment walkers, and the expression-checking entry points (caching, contextual typing, mutable
/// locations, declaration initializers).
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (<c>checkBinaryExpression</c>): TS creates the trampoline at checker.ts:1549
/// (<c>var checkBinaryExpression = createCheckBinaryExpression()</c>) and calls it from 40365 and 41915. The port keeps
/// the same shape — <see cref="CreateCheckBinaryExpression"/> returns the <c>Func&lt;BinaryExpression, CheckMode?,
/// IType&gt;</c> the reference's closure is, and the slot <c>_checkBinaryExpression</c> holds it — but fills the slot on
/// the first call instead of at construction, because the state block that owns checker.ts:1549 does not exist yet
/// (<c>Checker/Checker.cs</c>'s class note assigns that slot to this slice). <see cref="CheckBinaryExpression"/> is the
/// callable the sibling slices use.
/// </para>
/// <para>
/// PORT-NOTE (node parameters): the reference's unions (<c>FunctionExpression | ArrowFunction | MethodDeclaration</c>,
/// <c>Expression | ShorthandPropertyAssignment</c>, <c>AccessExpression</c>, <c>HasExpressionInitializer</c>,
/// <c>Declaration</c>) have no C# counterpart, so a parameter of that shape is <c>Node</c> here and the body narrows
/// with the same kind tests the reference relies on the type system for. Where the reference declares one concrete node
/// class the port keeps that class only if no landed call site passes a wider node; the three members the landed slices
/// already call with a <c>Node</c> (<see cref="CheckPropertyAssignment"/>, <see cref="CheckObjectLiteralMethod"/>,
/// <see cref="CheckExpressionForMutableLocation"/>) take <c>Node</c> for that reason.
/// </para>
/// <para>
/// PORT-NOTE (<c>LanguageFeatureMinimumTarget</c>): the reference compares <c>languageVersion</c> against
/// <c>LanguageFeatureMinimumTarget.&lt;feature&gt;</c> (types.ts:8488). That table has no port counterpart, so each of
/// the five comparisons below is written against the target the table holds and names the entry it stands in for:
/// <c>DestructuringAssignment</c>/<c>Generators</c> are <c>ScriptTarget.ES2015</c>, <c>AsyncGenerators</c> and
/// <c>ObjectSpreadRest</c> are <c>ScriptTarget.ES2018</c>, <c>PrivateNamesAndClassStaticBlocks</c> is
/// <c>ScriptTarget.ES2022</c> and <c>ClassAndClassElementDecorators</c> is <c>ScriptTarget.ESNext</c> — the same
/// substitution <c>Checker.Check.Classes.cs</c> records.
/// </para>
/// <para>
/// PORT-NOTE (<c>isCompoundAssignment</c>): its reference home is <c>transformers/utilities.ts</c> (490-493), a module
/// no port slice owns and no port file ports. The checker is its only caller in the reference, so it is declared here
/// (it is declared nowhere in the port today, so this is not a second copy) and <see cref="IsCompoundAssignment"/> is
/// reported with that reference range.
/// </para>
/// <para>
/// PORT-NOTE: this file calls members of sibling slices that do not exist yet — the cross-slice red state — and reads
/// nine slots that are not declared in the port today: <c>_moduleKind</c> (checker.ts:1530),
/// <c>_useDefineForClassFields</c> (:1532) and <c>_evaluate</c> (:1553), all three assigned by the M6 utilities slice
/// with their reference initializers (<c>Checker/Checker.cs</c>'s class note), plus this file's own
/// <c>_checkBinaryExpression</c> (:1549). Each is listed in the slice report.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // Function expressions and object literal methods (checker.ts:39601-39725)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:39601-39639 — <c>checkFunctionExpressionOrObjectLiteralMethod</c>.</summary>
    internal IType CheckFunctionExpressionOrObjectLiteralMethod(Node node, CheckMode checkMode = CheckMode.Normal)
    {
        Debug.Assert(node.Kind != SyntaxKind.MethodDeclaration || Utilities.IsObjectLiteralMethod(node));

        CheckNodeDeferred(node);

        if (NodeTests.IsFunctionExpression(node))
        {
            CheckCollisionsForDeclarationName(node, (Identifier?)((INamedDeclaration)node).Name);
        }

        // The identityMapper object is used to indicate that function expressions are wildcards
        if ((checkMode & CheckMode.SkipContextSensitive) != 0 && IsContextSensitive(node))
        {
            // Skip parameters, return signature with return type that retains noncontextual parts so inferences can still be drawn in an early stage
            if (Utilities.GetEffectiveReturnTypeNode(node) is null && !Utilities.HasContextSensitiveParameters(node))
            {
                // Return plain anyFunctionType if there is no possibility we'll make inferences from the return type
                var contextualSignature = GetContextualSignature(node);
                if (contextualSignature is not null && CouldContainTypeVariables(GetReturnTypeOfSignature(contextualSignature)))
                {
                    var links = GetNodeLinks(node);
                    if (links.ContextFreeType is not null)
                    {
                        return links.ContextFreeType;
                    }
                    var returnType = GetReturnTypeFromBody(node, checkMode);
                    var returnOnlySignature = CreateSignature(/*declaration*/ null, /*typeParameters*/ null, /*thisParameter*/ null, [], returnType, /*resolvedTypePredicate*/ null, 0, SignatureFlags.IsNonInferrable);
                    var returnOnlyType = CreateAnonymousType(GetSymbolOfNode(node), _emptySymbols, [returnOnlySignature], [], []);
                    returnOnlyType.ObjectFlags |= ObjectFlags.NonInferrableType;
                    return links.ContextFreeType = returnOnlyType;
                }
            }
            return _anyFunctionType;
        }

        // Grammar checking
        var hasGrammarError = CheckGrammarFunctionLikeDeclaration(node);
        if (!hasGrammarError && node.Kind == SyntaxKind.FunctionExpression)
        {
            CheckGrammarForGenerator(node);
        }

        ContextuallyCheckFunctionExpressionOrObjectLiteralMethod(node, checkMode);

        return GetTypeOfSymbol(GetSymbolOfDeclaration((IDeclaration)node)!);
    }

    /// <summary>ported from: src/compiler/checker.ts:39641-39690 — <c>contextuallyCheckFunctionExpressionOrObjectLiteralMethod</c>.</summary>
    internal void ContextuallyCheckFunctionExpressionOrObjectLiteralMethod(Node node, CheckMode checkMode = CheckMode.Normal)
    {
        var links = GetNodeLinks(node);
        // Check if function expression is contextually typed and assign parameter types if so.
        if ((links.Flags & NodeCheckFlags.ContextChecked) == 0)
        {
            var contextualSignature = GetContextualSignature(node);
            // If a type check is started at a function expression that is an argument of a function call, obtaining the
            // contextual type may recursively get back to here during overload resolution of the call. If so, we will have
            // already assigned contextual types.
            if ((links.Flags & NodeCheckFlags.ContextChecked) == 0)
            {
                links.Flags |= NodeCheckFlags.ContextChecked;
                var signature = Core.FirstOrUndefined(GetSignaturesOfType(GetTypeOfSymbol(GetSymbolOfDeclaration((IDeclaration)node)!), SignatureKind.Call));
                if (signature is null)
                {
                    return;
                }
                if (IsContextSensitive(node))
                {
                    if (contextualSignature is not null)
                    {
                        var inferenceContext = GetInferenceContext(node);
                        ISignature? instantiatedContextualSignature = null;
                        if ((checkMode & CheckMode.Inferential) != 0)
                        {
                            InferFromAnnotatedParametersAndReturn(signature, contextualSignature, inferenceContext!);
                            var restType = GetEffectiveRestType(contextualSignature);
                            if (restType is not null && (restType.Flags & TypeFlags.TypeParameter) != 0)
                            {
                                instantiatedContextualSignature = InstantiateSignature(contextualSignature, (TypeMapper)inferenceContext!.NonFixingMapper!);
                            }
                        }
                        instantiatedContextualSignature ??= inferenceContext is not null
                            ? InstantiateSignature(contextualSignature, (TypeMapper)inferenceContext.Mapper!)
                            : contextualSignature;
                        AssignContextualParameterTypes(signature, instantiatedContextualSignature);
                    }
                    else
                    {
                        // Force resolution of all parameter types such that the absence of a contextual type is consistently reflected.
                        AssignNonContextualParameterTypes(signature);
                    }
                }
                else if (contextualSignature is not null
                    && ((ISignatureDeclarationBase)node).TypeParameters is null
                    && contextualSignature.Parameters!.Count > (((ISignatureDeclarationBase)node).Parameters?.Count ?? 0))
                {
                    var inferenceContext = GetInferenceContext(node);
                    if ((checkMode & CheckMode.Inferential) != 0)
                    {
                        InferFromAnnotatedParametersAndReturn(signature, contextualSignature, inferenceContext!);
                    }
                }
                if (contextualSignature is not null && GetReturnTypeFromAnnotation(node) is null && signature.ResolvedReturnType is null)
                {
                    var returnType = GetReturnTypeFromBody(node, checkMode);
                    if (signature.ResolvedReturnType is null)
                    {
                        signature.ResolvedReturnType = returnType;
                    }
                }
                CheckSignatureDeclaration(node);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:39692-39725 — <c>checkFunctionExpressionOrObjectLiteralMethodDeferred</c>.</summary>
    internal void CheckFunctionExpressionOrObjectLiteralMethodDeferred(Node node)
    {
        Debug.Assert(node.Kind != SyntaxKind.MethodDeclaration || Utilities.IsObjectLiteralMethod(node));

        var functionFlags = Utilities.GetFunctionFlags(node);
        var returnType = GetReturnTypeFromAnnotation(node);
        CheckAllCodePathsInNonVoidFunctionReturnOrThrow(node, returnType);

        if (((IFunctionLikeDeclarationBase)node).Body is { } body)
        {
            if (Utilities.GetEffectiveReturnTypeNode(node) is null)
            {
                // There are some checks that are only performed in getReturnTypeFromBody, that may produce errors
                // we need. An example is the noImplicitAny errors resulting from widening the return expression
                // of a function. Because checking of function expression bodies is deferred, there was never an
                // appropriate time to do this during the main walk of the file (see the comment at the top of
                // checkFunctionExpressionBodies). So it must be done now.
                GetReturnTypeOfSignature(GetSignatureFromDeclaration(node)!);
            }

            if (body.Kind == SyntaxKind.Block)
            {
                CheckSourceElement(body);
            }
            else
            {
                // From within an async function you can return either a non-promise value or a promise. Any
                // Promise/A+ compatible implementation will always assimilate any foreign promise, so we
                // should not be checking assignability of a promise to the return type. Instead, we need to
                // check assignability of the awaited type of the expression body against the promised type of
                // its return type annotation.
                var exprType = CheckExpression(body);
                var returnOrPromisedType = returnType is not null ? UnwrapReturnType(returnType, functionFlags) : null;
                if (returnOrPromisedType is not null)
                {
                    CheckReturnExpression(node, returnOrPromisedType, body, (Expression)body, exprType);
                }
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Operand and reference checks (checker.ts:39727-39889)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:39727-39738 — <c>checkArithmeticOperandType</c>.</summary>
    internal bool CheckArithmeticOperandType(Node operand, IType type, DiagnosticMessage diagnostic, bool isAwaitValid = false)
    {
        if (!IsTypeAssignableTo(type, _numberOrBigIntType))
        {
            var awaitedType = isAwaitValid ? GetAwaitedTypeOfPromise(type) : null;
            ErrorAndMaybeSuggestAwait(
                operand,
                awaitedType is not null && IsTypeAssignableTo(awaitedType, _numberOrBigIntType),
                diagnostic);
            return false;
        }
        return true;
    }

    /// <summary>ported from: src/compiler/checker.ts:39740-39769 — <c>isReadonlyAssignmentDeclaration</c>.</summary>
    internal bool IsReadonlyAssignmentDeclaration(Node d)
    {
        if (!NodeTests.IsCallExpression(d))
        {
            return false;
        }
        if (!Utilities.IsBindableObjectDefinePropertyCall((CallExpression)d))
        {
            return false;
        }
        var objectLitType = CheckExpressionCached(((CallExpression)d).Arguments![2]);
        var valueType = GetTypeOfPropertyOfType(objectLitType, "value");
        if (valueType is not null)
        {
            var writableProp = GetPropertyOfType(objectLitType, "writable");
            var writableType = writableProp is not null ? GetTypeOfSymbol(writableProp) : null;
            if (writableType is null || ReferenceEquals(writableType, _falseType) || ReferenceEquals(writableType, _regularFalseType))
            {
                return true;
            }
            // We include this definition whereupon we walk back and check the type at the declaration because
            // The usual definition of `Object.defineProperty` will _not_ cause literal types to be preserved in the
            // argument types, should the type be contextualized by the call itself.
            if (writableProp is not null && writableProp.ValueDeclaration is not null && NodeTests.IsPropertyAssignment((Node)writableProp.ValueDeclaration))
            {
                var initializer = ((PropertyAssignment)writableProp.ValueDeclaration).Initializer!;
                var rawOriginalType = CheckExpression(initializer);
                if (ReferenceEquals(rawOriginalType, _falseType) || ReferenceEquals(rawOriginalType, _regularFalseType))
                {
                    return true;
                }
            }
            return false;
        }
        var setProp = GetPropertyOfType(objectLitType, "set");
        return setProp is null;
    }

    /// <summary>ported from: src/compiler/checker.ts:39771-39785 — <c>isReadonlySymbol</c>.</summary>
    internal bool IsReadonlySymbol(ISymbol symbol)
    {
        // The following symbols are considered read-only:
        // Properties with a 'readonly' modifier
        // Variables declared with 'const'
        // Get accessors without matching set accessors
        // Enum members
        // Object.defineProperty assignments with writable false or no setter
        // Unions and intersections of the above (unions and intersections eagerly set isReadonly on creation)
        return (GetCheckFlags(symbol) & CheckFlags.Readonly) != 0
            || (symbol.Flags & SymbolFlags.Property) != 0 && (Utilities.GetDeclarationModifierFlagsFromSymbol(symbol) & ModifierFlags.Readonly) != 0
            || (symbol.Flags & SymbolFlags.Variable) != 0 && (GetDeclarationNodeFlagsFromSymbol(symbol) & NodeFlags.Constant) != 0
            || (symbol.Flags & SymbolFlags.Accessor) != 0 && (symbol.Flags & SymbolFlags.SetAccessor) == 0
            || (symbol.Flags & SymbolFlags.EnumMember) != 0
            || Core.Some(symbol.Declarations!, d => IsReadonlyAssignmentDeclaration((Node)d));
    }

    /// <summary>ported from: src/compiler/checker.ts:39787-39831 — <c>isAssignmentToReadonlyEntity</c>.</summary>
    internal bool IsAssignmentToReadonlyEntity(Expression expr, ISymbol symbol, AssignmentKind assignmentKind)
    {
        if (assignmentKind == AssignmentKind.None)
        {
            // no assigment means it doesn't matter whether the entity is readonly
            return false;
        }
        if (IsReadonlySymbol(symbol))
        {
            // Allow assignments to readonly properties within constructors of the same class declaration.
            if (
                (symbol.Flags & SymbolFlags.Property) != 0
                && Utilities.IsAccessExpression(expr)
                && Utilities.GetAccessExpressionExpression(expr).Kind == SyntaxKind.ThisKeyword
            )
            {
                // Look for if this is the constructor for the class that `symbol` is a property of.
                var ctor = GetControlFlowContainer(expr);
                if (!(ctor is not null && (ctor.Kind == SyntaxKind.Constructor || IsJSConstructor(ctor))))
                {
                    return true;
                }
                if (symbol.ValueDeclaration is not null)
                {
                    var isAssignmentDeclaration = NodeTests.IsBinaryExpression((Node)symbol.ValueDeclaration);
                    var isLocalPropertyDeclaration = ReferenceEquals(ctor!.Parent, ((Node)symbol.ValueDeclaration!).Parent);
                    var isLocalParameterProperty = ReferenceEquals(ctor, ((Node)symbol.ValueDeclaration!).Parent);
                    var isLocalThisPropertyAssignment = isAssignmentDeclaration && ReferenceEquals(symbol.Parent?.ValueDeclaration, ctor.Parent);
                    var isLocalThisPropertyAssignmentConstructorFunction = isAssignmentDeclaration && ReferenceEquals(symbol.Parent?.ValueDeclaration, ctor);
                    var isWriteableSymbol = isLocalPropertyDeclaration
                        || isLocalParameterProperty
                        || isLocalThisPropertyAssignment
                        || isLocalThisPropertyAssignmentConstructorFunction;
                    return !isWriteableSymbol;
                }
            }
            return true;
        }
        if (Utilities.IsAccessExpression(expr))
        {
            // references through namespace import should be readonly
            var node = Utilities.SkipParentheses(Utilities.GetAccessExpressionExpression(expr));
            if (NodeTests.IsIdentifier(node))
            {
                var aliasSymbol = GetNodeLinks(node).ResolvedSymbol!;
                if ((aliasSymbol.Flags & SymbolFlags.Alias) != 0)
                {
                    var declaration = GetDeclarationOfAliasSymbol(aliasSymbol);
                    return declaration is not null && declaration.Kind == SyntaxKind.NamespaceImport;
                }
            }
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:39833-39845 — <c>checkReferenceExpression</c>.</summary>
    internal bool CheckReferenceExpression(Expression expr, DiagnosticMessage invalidReferenceMessage, DiagnosticMessage invalidOptionalChainMessage)
    {
        // References are combinations of identifiers, parentheses, and property accesses.
        var node = FactoryUtilities.SkipOuterExpressions(expr, OuterExpressionKinds.Assertions | OuterExpressionKinds.Parentheses);
        if (node.Kind != SyntaxKind.Identifier && !Utilities.IsAccessExpression(node))
        {
            Error(expr, invalidReferenceMessage);
            return false;
        }
        if ((node.Flags & NodeFlags.OptionalChain) != 0)
        {
            Error(expr, invalidOptionalChainMessage);
            return false;
        }
        return true;
    }

    /// <summary>ported from: src/compiler/checker.ts:39847-39868 — <c>checkDeleteExpression</c>.</summary>
    internal IType CheckDeleteExpression(DeleteExpression node)
    {
        CheckExpression(node.Expression!);
        var expr = Utilities.SkipParentheses((Node)node.Expression!);
        if (!Utilities.IsAccessExpression(expr))
        {
            Error(expr, Diagnostics.The_operand_of_a_delete_operator_must_be_a_property_reference);
            return _booleanType;
        }
        if (NodeTests.IsPropertyAccessExpression(expr) && NodeTests.IsPrivateIdentifier(((PropertyAccessExpression)expr).Name!))
        {
            Error(expr, Diagnostics.The_operand_of_a_delete_operator_cannot_be_a_private_identifier);
        }
        var links = GetNodeLinks(expr);
        var symbol = GetExportSymbolOfValueSymbolIfExported(links.ResolvedSymbol);
        if (symbol is not null)
        {
            if (IsReadonlySymbol(symbol))
            {
                Error(expr, Diagnostics.The_operand_of_a_delete_operator_cannot_be_a_read_only_property);
            }
            else
            {
                CheckDeleteExpressionMustBeOptional(expr, symbol);
            }
        }
        return _booleanType;
    }

    /// <summary>ported from: src/compiler/checker.ts:39870-39879 — <c>checkDeleteExpressionMustBeOptional</c>.</summary>
    internal void CheckDeleteExpressionMustBeOptional(Node expr, ISymbol symbol)
    {
        var type = GetTypeOfSymbol(symbol);
        if (
            _strictNullChecks
            && (type.Flags & (TypeFlags.AnyOrUnknown | TypeFlags.Never)) == 0
            && !(_exactOptionalPropertyTypes == true ? (symbol.Flags & SymbolFlags.Optional) != 0 : HasTypeFacts(type, TypeFacts.IsUndefined))
        )
        {
            Error(expr, Diagnostics.The_operand_of_a_delete_operator_must_be_optional);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:39881-39884 — <c>checkTypeOfExpression</c>.</summary>
    internal IType CheckTypeOfExpression(TypeOfExpression node)
    {
        CheckExpression(node.Expression!);
        return _typeofType;
    }

    /// <summary>ported from: src/compiler/checker.ts:39886-39889 — <c>checkVoidExpression</c>.</summary>
    internal IType CheckVoidExpression(VoidExpression node)
    {
        CheckNodeDeferred(node);
        return _undefinedWideningType;
    }

    /// <summary>ported from: src/compiler/checker.ts:39891-39972 — <c>checkAwaitGrammar</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>AwaitExpression | VariableDeclarationList</c> union, which the port takes as
    /// <c>Node</c> and discriminates with <c>NodeTests.IsAwaitExpression</c>. The two <c>// fallthrough</c> arms of the
    /// module-kind switch are the port's <c>goto case</c>/<c>goto default</c>, which is C#'s spelling for the same
    /// fallthrough, and each case that declares its own <c>message</c> is braced because a C# switch section's locals
    /// share one scope.
    /// </remarks>
    internal bool CheckAwaitGrammar(Node node)
    {
        // Grammar checking
        var hasError = false;
        var container = Utilities.GetContainingFunctionOrClassStaticBlock(node);
        if (container is not null && NodeTests.IsClassStaticBlockDeclaration(container))
        {
            // NOTE: We report this regardless as to whether there are parse diagnostics.
            var staticBlockMessage = NodeTests.IsAwaitExpression(node) ? Diagnostics.await_expression_cannot_be_used_inside_a_class_static_block :
                Diagnostics.await_using_statements_cannot_be_used_inside_a_class_static_block;
            Error(node, staticBlockMessage);
            hasError = true;
        }
        else if ((node.Flags & NodeFlags.AwaitContext) == 0)
        {
            if (Utilities.IsInTopLevelContext(node))
            {
                var sourceFile = Utilities.GetSourceFileOfNode(node)!;
                if (!HasParseDiagnostics(sourceFile!))
                {
                    TextSpan? span = null;
                    if (!Utilities.IsEffectiveExternalModule(sourceFile, _compilerOptions))
                    {
                        span ??= Utilities.GetSpanOfTokenAtPosition(sourceFile, node.Pos);
                        var notModuleMessage = NodeTests.IsAwaitExpression(node) ? Diagnostics.await_expressions_are_only_allowed_at_the_top_level_of_a_file_when_that_file_is_a_module_but_this_file_has_no_imports_or_exports_Consider_adding_an_empty_export_to_make_this_file_a_module :
                            Diagnostics.await_using_statements_are_only_allowed_at_the_top_level_of_a_file_when_that_file_is_a_module_but_this_file_has_no_imports_or_exports_Consider_adding_an_empty_export_to_make_this_file_a_module;
                        _diagnostics.Add(Utilities.CreateFileDiagnostic(sourceFile, span!.Start, span.Length, notModuleMessage));
                        hasError = true;
                    }
                    switch (_moduleKind)
                    {
                        case ModuleKind.Node16:
                        case ModuleKind.Node18:
                        case ModuleKind.Node20:
                        case ModuleKind.NodeNext:
                            if (sourceFile.ImpliedNodeFormat is ModuleKind.CommonJS)
                            {
                                span ??= Utilities.GetSpanOfTokenAtPosition(sourceFile, node.Pos);
                                _diagnostics.Add(
                                    Utilities.CreateFileDiagnostic(sourceFile, span!.Start, span.Length, Diagnostics.The_current_file_is_a_CommonJS_module_and_cannot_use_await_at_the_top_level));
                                hasError = true;
                                break;
                            }
                            goto case ModuleKind.ES2022;
                        case ModuleKind.ES2022:
                        case ModuleKind.ESNext:
                        case ModuleKind.Preserve:
                        case ModuleKind.System:
                            if (_languageVersion >= ScriptTarget.ES2017)
                            {
                                break;
                            }
                            goto default;
                        default:
                        {
                            span ??= Utilities.GetSpanOfTokenAtPosition(sourceFile, node.Pos);
                            var topLevelMessage = NodeTests.IsAwaitExpression(node) ? Diagnostics.Top_level_await_expressions_are_only_allowed_when_the_module_option_is_set_to_es2022_esnext_system_node16_node18_node20_nodenext_or_preserve_and_the_target_option_is_set_to_es2017_or_higher :
                                Diagnostics.Top_level_await_using_statements_are_only_allowed_when_the_module_option_is_set_to_es2022_esnext_system_node16_node18_node20_nodenext_or_preserve_and_the_target_option_is_set_to_es2017_or_higher;
                            _diagnostics.Add(Utilities.CreateFileDiagnostic(sourceFile, span!.Start, span.Length, topLevelMessage));
                            hasError = true;
                            break;
                        }
                    }
                }
            }
            else
            {
                // use of 'await' in non-async function
                var sourceFile = Utilities.GetSourceFileOfNode(node)!;
                if (!HasParseDiagnostics(sourceFile!))
                {
                    var span = Utilities.GetSpanOfTokenAtPosition(sourceFile, node.Pos);
                    var message = NodeTests.IsAwaitExpression(node) ? Diagnostics.await_expressions_are_only_allowed_within_async_functions_and_at_the_top_levels_of_modules :
                        Diagnostics.await_using_statements_are_only_allowed_within_async_functions_and_at_the_top_levels_of_modules;
                    var diagnostic = Utilities.CreateFileDiagnostic(sourceFile, span.Start, span.Length, message);
                    if (container is not null && container.Kind != SyntaxKind.Constructor && (Utilities.GetFunctionFlags(container) & FunctionFlags.Async) == 0)
                    {
                        var relatedInfo = Utilities.CreateDiagnosticForNode(container, Diagnostics.Did_you_mean_to_mark_this_function_as_async);
                        Utilities.AddRelatedInfo(diagnostic, relatedInfo);
                    }
                    _diagnostics.Add(diagnostic);
                    hasError = true;
                }
            }
        }

        if (NodeTests.IsAwaitExpression(node) && IsInParameterInitializerBeforeContainingFunction(node))
        {
            // NOTE: We report this regardless as to whether there are parse diagnostics.
            Error(node, Diagnostics.await_expressions_cannot_be_used_in_a_parameter_initializer);
            hasError = true;
        }

        return hasError;
    }

    /// <summary>ported from: src/compiler/checker.ts:39974-39983 — <c>checkAwaitExpression</c>.</summary>
    internal IType CheckAwaitExpression(AwaitExpression node)
    {
        AddLazyDiagnostic(() => CheckAwaitGrammar(node));

        var operandType = CheckExpression(node.Expression!);
        var awaitedType = CheckAwaitedType(operandType, /*withAlias*/ true, node, Diagnostics.Type_of_await_operand_must_either_be_a_valid_promise_or_must_not_contain_a_callable_then_member);
        if (ReferenceEquals(awaitedType, operandType) && !IsErrorType(awaitedType) && (operandType.Flags & TypeFlags.AnyOrUnknown) == 0)
        {
            AddErrorOrSuggestion(/*isError*/ false, Utilities.CreateDiagnosticForNode(node, Diagnostics.await_has_no_effect_on_the_type_of_this_expression));
        }
        return awaitedType;
    }

    /// <summary>ported from: src/compiler/checker.ts:39985-40042 — <c>checkPrefixUnaryExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>-(node.operand as NumericLiteral).text</c> and <c>+…text</c> are JS's unary coercion
    /// of a digit string, which is <c>Js.ToNumber</c>. The <c>getBigIntLiteralType({ negative: true, base10Value: … })</c>
    /// object literal is <c>Utilities.PseudoBigIntValue</c>, the port's declared face for <c>IPseudoBigInt</c>.
    /// </remarks>
    internal IType CheckPrefixUnaryExpression(PrefixUnaryExpression node)
    {
        var operandType = CheckExpression(node.Operand!);
        if (ReferenceEquals(operandType, _silentNeverType))
        {
            return _silentNeverType;
        }
        switch (node.Operand!.Kind)
        {
            case SyntaxKind.NumericLiteral:
                switch (node.Operator)
                {
                    case SyntaxKind.MinusToken:
                        return GetFreshTypeOfLiteralType(GetNumberLiteralType(-Js.ToNumber(((NumericLiteral)node.Operand).Text!)));
                    case SyntaxKind.PlusToken:
                        return GetFreshTypeOfLiteralType(GetNumberLiteralType(Js.ToNumber(((NumericLiteral)node.Operand).Text!)));
                }
                break;
            case SyntaxKind.BigIntLiteral:
                if (node.Operator == SyntaxKind.MinusToken)
                {
                    return GetFreshTypeOfLiteralType(GetBigIntLiteralType(new Utilities.PseudoBigIntValue
                    {
                        Negative = true,
                        Base10Value = PseudoBigInt.Parse(((BigIntLiteral)node.Operand).Text!),
                    }));
                }
                break;
        }
        switch (node.Operator)
        {
            case SyntaxKind.PlusToken:
            case SyntaxKind.MinusToken:
            case SyntaxKind.TildeToken:
                CheckNonNullType(operandType, node.Operand);
                if (MaybeTypeOfKindConsideringBaseConstraint(operandType, TypeFlags.ESSymbolLike))
                {
                    Error(node.Operand, Diagnostics.The_0_operator_cannot_be_applied_to_type_symbol, ScannerUtilities.TokenToString(node.Operator));
                }
                if (node.Operator == SyntaxKind.PlusToken)
                {
                    if (MaybeTypeOfKindConsideringBaseConstraint(operandType, TypeFlags.BigIntLike))
                    {
                        Error(node.Operand, Diagnostics.Operator_0_cannot_be_applied_to_type_1, ScannerUtilities.TokenToString(node.Operator), TypeToString(GetBaseTypeOfLiteralType(operandType)));
                    }
                    return _numberType;
                }
                return GetUnaryResultType(operandType);
            case SyntaxKind.ExclamationToken:
            {
                CheckTruthinessOfType(operandType, node.Operand);
                var facts = GetTypeFacts(operandType, TypeFacts.Truthy | TypeFacts.Falsy);
                return facts == TypeFacts.Truthy ? _falseType :
                    facts == TypeFacts.Falsy ? _trueType :
                    _booleanType;
            }
            case SyntaxKind.PlusPlusToken:
            case SyntaxKind.MinusMinusToken:
            {
                var ok = CheckArithmeticOperandType(node.Operand!, CheckNonNullType(operandType, node.Operand!), Diagnostics.An_arithmetic_operand_must_be_of_type_any_number_bigint_or_an_enum_type);
                if (ok)
                {
                    // run check only if former checks succeeded to avoid reporting cascading errors
                    CheckReferenceExpression(
                        node.Operand!,
                        Diagnostics.The_operand_of_an_increment_or_decrement_operator_must_be_a_variable_or_a_property_access,
                        Diagnostics.The_operand_of_an_increment_or_decrement_operator_may_not_be_an_optional_property_access);
                }
                return GetUnaryResultType(operandType);
            }
        }
        return _errorType;
    }

    // ---------------------------------------------------------------------------------------------
    // Unary results and the type-kind predicates (checker.ts:40044-40148)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:40044-40063 — <c>checkPostfixUnaryExpression</c>.</summary>
    internal IType CheckPostfixUnaryExpression(PostfixUnaryExpression node)
    {
        var operandType = CheckExpression(node.Operand!);
        if (ReferenceEquals(operandType, _silentNeverType))
        {
            return _silentNeverType;
        }
        var ok = CheckArithmeticOperandType(
            node.Operand!,
            CheckNonNullType(operandType, node.Operand!),
            Diagnostics.An_arithmetic_operand_must_be_of_type_any_number_bigint_or_an_enum_type);
        if (ok)
        {
            // run check only if former checks succeeded to avoid reporting cascading errors
            CheckReferenceExpression(
                node.Operand!,
                Diagnostics.The_operand_of_an_increment_or_decrement_operator_must_be_a_variable_or_a_property_access,
                Diagnostics.The_operand_of_an_increment_or_decrement_operator_may_not_be_an_optional_property_access);
        }
        return GetUnaryResultType(operandType);
    }

    /// <summary>ported from: src/compiler/checker.ts:40065-40073 — <c>getUnaryResultType</c>.</summary>
    internal IType GetUnaryResultType(IType operandType)
    {
        if (MaybeTypeOfKind(operandType, TypeFlags.BigIntLike))
        {
            return IsTypeAssignableToKind(operandType, TypeFlags.AnyOrUnknown) || MaybeTypeOfKind(operandType, TypeFlags.NumberLike)
                ? _numberOrBigIntType
                : _bigintType;
        }
        // If it's not a bigint type, implicit coercion will result in a number
        return _numberType;
    }

    /// <summary>ported from: src/compiler/checker.ts:40075-40082 — <c>maybeTypeOfKindConsideringBaseConstraint</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>getBaseConstraintOrType</c> answers a total <c>Type</c>, so its <c>!!baseConstraint &amp;&amp;</c>
    /// is a truthiness test the port states as the null test of the answered slot.
    /// </remarks>
    internal bool MaybeTypeOfKindConsideringBaseConstraint(IType type, TypeFlags kind)
    {
        if (MaybeTypeOfKind(type, kind))
        {
            return true;
        }

        var baseConstraint = GetBaseConstraintOrType(type);
        return baseConstraint is not null && MaybeTypeOfKind(baseConstraint, kind);
    }

    /// <summary>
    /// ported from: src/compiler/checker.ts:40086-40099 — <c>maybeTypeOfKind</c>.
    /// </summary>
    /// <remarks>
    /// Return true if type might be of the given kind. A union or intersection type might be of a given
    /// kind if at least one constituent type is of the given kind.
    /// </remarks>
    internal bool MaybeTypeOfKind(IType type, TypeFlags kind)
    {
        if ((type.Flags & kind) != 0)
        {
            return true;
        }
        if ((type.Flags & TypeFlags.UnionOrIntersection) != 0)
        {
            var types = ((IUnionOrIntersectionType)type).Types!;
            foreach (var t in types)
            {
                if (MaybeTypeOfKind(t, kind))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:40101-40118 — <c>isTypeAssignableToKind</c>.</summary>
    internal bool IsTypeAssignableToKind(IType source, TypeFlags kind, bool strict = false)
    {
        if ((source.Flags & kind) != 0)
        {
            return true;
        }
        if (strict && (source.Flags & (TypeFlags.AnyOrUnknown | TypeFlags.Void | TypeFlags.Undefined | TypeFlags.Null)) != 0)
        {
            return false;
        }
        return (kind & TypeFlags.NumberLike) != 0 && IsTypeAssignableTo(source, _numberType)
            || (kind & TypeFlags.BigIntLike) != 0 && IsTypeAssignableTo(source, _bigintType)
            || (kind & TypeFlags.StringLike) != 0 && IsTypeAssignableTo(source, _stringType)
            || (kind & TypeFlags.BooleanLike) != 0 && IsTypeAssignableTo(source, _booleanType)
            || (kind & TypeFlags.Void) != 0 && IsTypeAssignableTo(source, _voidType)
            || (kind & TypeFlags.Never) != 0 && IsTypeAssignableTo(source, _neverType)
            || (kind & TypeFlags.Null) != 0 && IsTypeAssignableTo(source, _nullType)
            || (kind & TypeFlags.Undefined) != 0 && IsTypeAssignableTo(source, _undefinedType)
            || (kind & TypeFlags.ESSymbol) != 0 && IsTypeAssignableTo(source, _esSymbolType)
            || (kind & TypeFlags.NonPrimitive) != 0 && IsTypeAssignableTo(source, _nonPrimitiveType);
    }

    /// <summary>ported from: src/compiler/checker.ts:40120-40124 — <c>allTypesAssignableToKind</c>.</summary>
    internal bool AllTypesAssignableToKind(IType source, TypeFlags kind, bool strict = false)
        => (source.Flags & TypeFlags.Union) != 0
            ? Core.Every(((IUnionType)source).Types!, (IType subType, int _) => AllTypesAssignableToKind(subType, kind, strict))
            : IsTypeAssignableToKind(source, kind, strict);

    /// <summary>ported from: src/compiler/checker.ts:40126-40128 — <c>isConstEnumObjectType</c>.</summary>
    internal bool IsConstEnumObjectType(IType type)
        => (GetObjectFlags(type) & ObjectFlags.Anonymous) != 0 && type.Symbol is not null && IsConstEnumSymbol(type.Symbol);

    /// <summary>ported from: src/compiler/checker.ts:40130-40132 — <c>isConstEnumSymbol</c>.</summary>
    internal static bool IsConstEnumSymbol(ISymbol symbol) => (symbol.Flags & SymbolFlags.ConstEnum) != 0;

    /// <summary>ported from: src/compiler/checker.ts:40137-40148 — <c>getSymbolHasInstanceMethodOfObjectType</c>.</summary>
    /// <remarks>Get the type of the `[Symbol.hasInstance]` method of an object type.</remarks>
    internal IType? GetSymbolHasInstanceMethodOfObjectType(IType type)
    {
        var hasInstancePropertyName = GetPropertyNameForKnownSymbolName("hasInstance");
        if (AllTypesAssignableToKind(type, TypeFlags.NonPrimitive))
        {
            var hasInstanceProperty = GetPropertyOfType(type, hasInstancePropertyName);
            if (hasInstanceProperty is not null)
            {
                var hasInstancePropertyType = GetTypeOfSymbol(hasInstanceProperty);
                if (Core.Length(GetSignaturesOfType(hasInstancePropertyType, SignatureKind.Call)) != 0)
                {
                    return hasInstancePropertyType;
                }
            }
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:40150-40187 — <c>checkInstanceOfExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>getResolvedSignature(left.parent, undefined, checkMode)</c> is called with a
    /// <c>Node</c> and the port's null <c>candidatesOutArray</c>, because <c>left.parent</c> is the
    /// <c>InstanceofExpression</c> the assertion below pins and every member of that union is a call-like node.
    /// </remarks>
    internal IType CheckInstanceOfExpression(Expression left, Expression right, IType leftType, IType rightType, CheckMode checkMode = CheckMode.Normal)
    {
        if (ReferenceEquals(leftType, _silentNeverType) || ReferenceEquals(rightType, _silentNeverType))
        {
            return _silentNeverType;
        }

        // TypeScript 1.0 spec (April 2014): 4.15.4
        // The instanceof operator requires the left operand to be of type Any, an object type, or a type parameter type,
        // and the right operand to be of type Any, a subtype of the 'Function' interface type, or have a call or construct signature.
        // The result is always of the Boolean primitive type.
        // NOTE: do not raise error if leftType is unknown as related error was already reported
        if (
            !IsTypeAny(leftType)
            && AllTypesAssignableToKind(leftType, TypeFlags.Primitive)
        )
        {
            Error(left, Diagnostics.The_left_hand_side_of_an_instanceof_expression_must_be_of_type_any_an_object_type_or_a_type_parameter);
        }

        Debug.Assert(Utilities.IsInstanceOfExpression(left.Parent!));
        var signature = GetResolvedSignature(left.Parent!, /*candidatesOutArray*/ null, checkMode);
        if (ReferenceEquals(signature, _resolvingSignature))
        {
            // CheckMode.SkipGenericFunctions is enabled and this is a call to a generic function that
            // returns a function type. We defer checking and return silentNeverType.
            return _silentNeverType;
        }

        // If rightType has a `[Symbol.hasInstance]` method that is not `(value: unknown) => boolean`, we
        // must check the expression as if it were a call to `right[Symbol.hasInstance](left)`. The call to
        // `getResolvedSignature`, below, will check that leftType is assignable to the type of the first
        // parameter.
        var returnType = GetReturnTypeOfSignature(signature);

        // We also verify that the return type of the `[Symbol.hasInstance]` method is assignable to
        // `boolean`. According to the spec, the runtime will actually perform `ToBoolean` on the result,
        // but this is more type-safe.
        CheckTypeAssignableTo(returnType, _booleanType, right, Diagnostics.An_object_s_Symbol_hasInstance_method_must_return_a_boolean_value_for_it_to_be_used_on_the_right_hand_side_of_an_instanceof_expression);

        return _booleanType;
    }

    /// <summary>ported from: src/compiler/checker.ts:40189-40191 — <c>hasEmptyObjectIntersection</c>.</summary>
    internal bool HasEmptyObjectIntersection(IType type)
        => SomeType(type, t => ReferenceEquals(t, _unknownEmptyObjectType) || (t.Flags & TypeFlags.Intersection) != 0 && IsEmptyAnonymousObjectType(GetBaseConstraintOrType(t)));

    /// <summary>ported from: src/compiler/checker.ts:40193-40228 — <c>checkInExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>languageVersion &lt; LanguageFeatureMinimumTarget.PrivateNamesAndClassStaticBlocks</c> and
    /// <c>&lt; …ClassAndClassElementDecorators</c> are written against <c>ScriptTarget.ES2022</c> and
    /// <c>ScriptTarget.ESNext</c>, the targets that table holds (types.ts:8488).
    /// </remarks>
    internal IType CheckInExpression(Expression left, Expression right, IType leftType, IType rightType)
    {
        if (ReferenceEquals(leftType, _silentNeverType) || ReferenceEquals(rightType, _silentNeverType))
        {
            return _silentNeverType;
        }
        if (NodeTests.IsPrivateIdentifier(left))
        {
            if (
                _languageVersion < ScriptTarget.ES2022 // LanguageFeatureMinimumTarget.PrivateNamesAndClassStaticBlocks
                || _languageVersion < ScriptTarget.ESNext // LanguageFeatureMinimumTarget.ClassAndClassElementDecorators
                || !_useDefineForClassFields
            )
            {
                CheckExternalEmitHelpers(left, ExternalEmitHelpers.ClassPrivateFieldIn);
            }
            // Unlike in 'checkPrivateIdentifierExpression' we now have access to the RHS type
            // which provides us with the opportunity to emit more detailed errors
            if (GetNodeLinks(left).ResolvedSymbol is null && Utilities.GetContainingClass(left) is not null)
            {
                var isUncheckedJS = IsUncheckedJSSuggestion(left, rightType.Symbol, /*excludeClasses*/ true);
                ReportNonexistentProperty(left, rightType, isUncheckedJS);
            }
        }
        else
        {
            // The type of the lef operand must be assignable to string, number, or symbol.
            CheckTypeAssignableTo(CheckNonNullType(leftType, left), _stringNumberSymbolType, left);
        }
        // The type of the right operand must be assignable to 'object'.
        if (CheckTypeAssignableTo(CheckNonNullType(rightType, right), _nonPrimitiveType, right))
        {
            // The {} type is assignable to the object type, yet {} might represent a primitive type. Here we
            // detect and error on {} that results from narrowing the unknown type, as well as intersections
            // that include {} (we know that the other types in such intersections are assignable to object
            // since we already checked for that).
            if (HasEmptyObjectIntersection(rightType))
            {
                Error(right, Diagnostics.Type_0_may_represent_a_primitive_value_which_is_not_permitted_as_the_right_operand_of_the_in_operator, TypeToString(rightType));
            }
        }
        // The result is always of the Boolean primitive type.
        return _booleanType;
    }

    // ---------------------------------------------------------------------------------------------
    // Destructuring assignment (checker.ts:40230-40397)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:40230-40239 — <c>checkObjectLiteralAssignment</c>.</summary>
    internal IType CheckObjectLiteralAssignment(ObjectLiteralExpression node, IType sourceType, bool? rightIsThis = null)
    {
        var properties = node.Properties!;
        if (_strictNullChecks && properties.Count == 0)
        {
            return CheckNonNullType(sourceType, node);
        }
        for (var i = 0; i < properties.Count; i++)
        {
            CheckObjectLiteralDestructuringPropertyAssignment(node, sourceType, i, properties, rightIsThis);
        }
        return sourceType;
    }

    /// <summary>ported from: src/compiler/checker.ts:40242-40284 — <c>checkObjectLiteralDestructuringPropertyAssignment</c>.</summary>
    /// <remarks>
    /// Note: If property cannot be a SpreadAssignment, then allProperties does not need to be provided
    /// <para>
    /// PORT-NOTE: <c>rightIsThis</c> stays the tri-state the reference's defaulted parameter is (<c>undefined</c> is
    /// JS-falsy there), read as <c>rightIsThis == true</c> at the one place the reference passes it on. The property
    /// element is the interface <c>IObjectLiteralElement</c>, so each read through a node-typed slot takes the cast
    /// the port's model needs.
    /// </para>
    /// </remarks>
    internal IType? CheckObjectLiteralDestructuringPropertyAssignment(
        ObjectLiteralExpression node,
        IType objectLiteralType,
        int propertyIndex,
        NodeArray<Node>? allProperties = null,
        bool? rightIsThis = null)
    {
        var properties = node.Properties!;
        var property = properties[propertyIndex];
        if (property.Kind == SyntaxKind.PropertyAssignment || property.Kind == SyntaxKind.ShorthandPropertyAssignment)
        {
            var name = ((INamedDeclaration)property).Name!;
            var exprType = GetLiteralTypeFromPropertyName(name);
            if (Utilities.IsTypeUsableAsPropertyName(exprType))
            {
                var text = Utilities.GetPropertyNameFromType(exprType);
                var prop = GetPropertyOfType(objectLiteralType, text);
                if (prop is not null)
                {
                    MarkPropertyAsReferenced(prop, property, rightIsThis == true);
                    CheckPropertyAccessibility(property, /*isSuper*/ false, /*writing*/ true, objectLiteralType, prop);
                }
            }
            var elementType = GetIndexedAccessType(objectLiteralType, exprType, AccessFlags.ExpressionPosition | (HasDefaultValue(property) ? AccessFlags.AllowMissing : 0), name);
            var type = GetFlowTypeOfDestructuring(property, elementType);
            return CheckDestructuringAssignment(property.Kind == SyntaxKind.ShorthandPropertyAssignment ? property : ((PropertyAssignment)property).Initializer!, type);
        }
        else if (property.Kind == SyntaxKind.SpreadAssignment)
        {
            if (propertyIndex < properties.Count - 1)
            {
                Error(property, Diagnostics.A_rest_element_must_be_last_in_a_destructuring_pattern);
            }
            else
            {
                if (_languageVersion < ScriptTarget.ES2018) // LanguageFeatureMinimumTarget.ObjectSpreadRest
                {
                    CheckExternalEmitHelpers(property, ExternalEmitHelpers.Rest);
                }
                var nonRestNames = new List<Node>();
                if (allProperties is not null)
                {
                    foreach (var otherProperty in allProperties)
                    {
                        if (!NodeTests.IsSpreadAssignment(otherProperty))
                        {
                            nonRestNames.Add(((INamedDeclaration)otherProperty).Name!);
                        }
                    }
                }
                var type = GetRestType(objectLiteralType, nonRestNames, objectLiteralType.Symbol);
                CheckGrammarForDisallowedTrailingComma(allProperties, Diagnostics.A_rest_parameter_or_binding_pattern_may_not_have_a_trailing_comma);
                return CheckDestructuringAssignment(((SpreadAssignment)property).Expression!, type);
            }
        }
        else
        {
            Error(property, Diagnostics.Property_assignment_expected);
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:40286-40304 — <c>checkArrayLiteralAssignment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>languageVersion &lt; LanguageFeatureMinimumTarget.DestructuringAssignment</c> is written against
    /// <c>ScriptTarget.ES2015</c>, the target that table holds (types.ts:8488).
    /// </remarks>
    internal IType CheckArrayLiteralAssignment(ArrayLiteralExpression node, IType sourceType, CheckMode checkMode = CheckMode.Normal)
    {
        var elements = node.Elements!;
        if (_languageVersion < ScriptTarget.ES2015 /* LanguageFeatureMinimumTarget.DestructuringAssignment */ && _compilerOptions.DownlevelIteration == true)
        {
            CheckExternalEmitHelpers(node, ExternalEmitHelpers.Read);
        }
        // This elementType will be used if the specific property corresponding to this index is not
        // present (aka the tuple element property). This call also checks that the parentType is in
        // fact an iterable or array (depending on target language).
        var possiblyOutOfBoundsType = CheckIteratedTypeOrElementType(IterationUse.Destructuring | IterationUse.PossiblyOutOfBounds, sourceType, _undefinedType, node) ?? _errorType;
        IType? inBoundsType = _compilerOptions.NoUncheckedIndexedAccess == true ? null : possiblyOutOfBoundsType;
        for (var i = 0; i < elements.Count; i++)
        {
            var type = possiblyOutOfBoundsType;
            if (elements[i].Kind == SyntaxKind.SpreadElement)
            {
                type = inBoundsType = inBoundsType ?? (CheckIteratedTypeOrElementType(IterationUse.Destructuring, sourceType, _undefinedType, node) ?? _errorType);
            }
            CheckArrayLiteralDestructuringElementAssignment(node, sourceType, i, type, checkMode);
        }
        return sourceType;
    }

    /// <summary>ported from: src/compiler/checker.ts:40306-40341 — <c>checkArrayLiteralDestructuringElementAssignment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference shadows its <c>elementType</c> parameter with a local of the same name inside the
    /// array-like arm; the port names that local <c>assignedElementType</c> because C# forbids the shadowing, and the
    /// value is the same expression in every arm.
    /// </remarks>
    internal IType? CheckArrayLiteralDestructuringElementAssignment(
        ArrayLiteralExpression node,
        IType sourceType,
        int elementIndex,
        IType elementType,
        CheckMode checkMode = CheckMode.Normal)
    {
        var elements = node.Elements!;
        var element = elements[elementIndex];
        if (element.Kind != SyntaxKind.OmittedExpression)
        {
            if (element.Kind != SyntaxKind.SpreadElement)
            {
                var indexType = GetNumberLiteralType(elementIndex);
                if (IsArrayLikeType(sourceType))
                {
                    // We create a synthetic expression so that getIndexedAccessType doesn't get confused
                    // when the element is a SyntaxKind.ElementAccessExpression.
                    var accessFlags = AccessFlags.ExpressionPosition | (HasDefaultValue(element) ? AccessFlags.AllowMissing : 0);
                    var assignedElementType = GetIndexedAccessTypeOrUndefined(sourceType, indexType, accessFlags, CreateSyntheticExpression(element, indexType)) ?? _errorType;
                    var assignedType = HasDefaultValue(element) ? GetTypeWithFacts(assignedElementType, TypeFacts.NEUndefined) : assignedElementType;
                    var type = GetFlowTypeOfDestructuring(element, assignedType);
                    return CheckDestructuringAssignment(element, type, checkMode);
                }
                return CheckDestructuringAssignment(element, elementType, checkMode);
            }
            if (elementIndex < elements.Count - 1)
            {
                Error(element, Diagnostics.A_rest_element_must_be_last_in_a_destructuring_pattern);
            }
            else
            {
                var restExpression = ((SpreadElement)element).Expression!;
                if (restExpression.Kind == SyntaxKind.BinaryExpression && ((BinaryExpression)restExpression).OperatorToken!.Kind == SyntaxKind.EqualsToken)
                {
                    Error(((BinaryExpression)restExpression).OperatorToken, Diagnostics.A_rest_element_cannot_have_an_initializer);
                }
                else
                {
                    CheckGrammarForDisallowedTrailingComma(node.Elements, Diagnostics.A_rest_parameter_or_binding_pattern_may_not_have_a_trailing_comma);
                    var type = EveryType(sourceType, IsTupleType) ?
                        MapType(sourceType, t => SliceTupleType((ITupleTypeReference)t, elementIndex))! :
                        CreateArrayType(elementType);
                    return CheckDestructuringAssignment(restExpression, type, checkMode);
                }
            }
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:40343-40379 — <c>checkDestructuringAssignment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>Expression | ShorthandPropertyAssignment</c> union, taken as <c>Node</c>
    /// here; the <c>target</c> local is the reference's <c>Expression</c>, read out of the node once the shorthand arm
    /// has handled its two rewrites.
    /// </remarks>
    internal IType CheckDestructuringAssignment(Node exprOrAssignment, IType sourceType, CheckMode checkMode = CheckMode.Normal, bool? rightIsThis = null)
    {
        Expression target;
        if (exprOrAssignment.Kind == SyntaxKind.ShorthandPropertyAssignment)
        {
            var prop = (ShorthandPropertyAssignment)exprOrAssignment;
            if (prop.ObjectAssignmentInitializer is not null)
            {
                // In strict null checking mode, if a default value of a non-undefined type is specified, remove
                // undefined from the final type.
                if (
                    _strictNullChecks
                    && !HasTypeFacts(CheckExpression(prop.ObjectAssignmentInitializer), TypeFacts.IsUndefined)
                )
                {
                    sourceType = GetTypeWithFacts(sourceType, TypeFacts.NEUndefined);
                }
                CheckBinaryLikeExpression(prop.Name!, prop.EqualsToken!, prop.ObjectAssignmentInitializer, checkMode);
            }
            target = prop.Name!;
        }
        else
        {
            target = (Expression)exprOrAssignment;
        }

        if (target.Kind == SyntaxKind.BinaryExpression && ((BinaryExpression)target).OperatorToken!.Kind == SyntaxKind.EqualsToken)
        {
            CheckBinaryExpression((BinaryExpression)target, checkMode);
            target = ((BinaryExpression)target).Left!;
            // A default value is specified, so remove undefined from the final type.
            if (_strictNullChecks)
            {
                sourceType = GetTypeWithFacts(sourceType, TypeFacts.NEUndefined);
            }
        }
        if (target.Kind == SyntaxKind.ObjectLiteralExpression)
        {
            return CheckObjectLiteralAssignment((ObjectLiteralExpression)target, sourceType, rightIsThis);
        }
        if (target.Kind == SyntaxKind.ArrayLiteralExpression)
        {
            return CheckArrayLiteralAssignment((ArrayLiteralExpression)target, sourceType, checkMode);
        }
        return CheckReferenceAssignment(target, sourceType, checkMode);
    }

    /// <summary>ported from: src/compiler/checker.ts:40381-40397 — <c>checkReferenceAssignment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference shadows its own <c>error</c> helper with the two message locals; the port keeps those
    /// names because neither member calls <c>Error</c> afterwards.
    /// </remarks>
    internal IType CheckReferenceAssignment(Expression target, IType sourceType, CheckMode checkMode = CheckMode.Normal)
    {
        var targetType = CheckExpression(target, checkMode);
        var error = target.Parent!.Kind == SyntaxKind.SpreadAssignment ?
            Diagnostics.The_target_of_an_object_rest_assignment_must_be_a_variable_or_a_property_access :
            Diagnostics.The_left_hand_side_of_an_assignment_expression_must_be_a_variable_or_a_property_access;
        var optionalError = target.Parent!.Kind == SyntaxKind.SpreadAssignment ?
            Diagnostics.The_target_of_an_object_rest_assignment_may_not_be_an_optional_property_access :
            Diagnostics.The_left_hand_side_of_an_assignment_expression_may_not_be_an_optional_property_access;
        if (CheckReferenceExpression(target, error, optionalError))
        {
            CheckTypeAssignableToAndOptionallyElaborate(sourceType, targetType, target, target);
        }
        if (Utilities.IsPrivateIdentifierPropertyAccessExpression(target))
        {
            // NOTE: we do not limit this to LanguageFeatureTargets.PrivateNames as some other feature downleveling still requires this.
            CheckExternalEmitHelpers(target.Parent!, ExternalEmitHelpers.ClassPrivateFieldSet);
        }
        return sourceType;
    }

    // ---------------------------------------------------------------------------------------------
    // The side-effect and comparability predicates (checker.ts:40407-40468)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:40407-40464 — <c>isSideEffectFree</c>.</summary>
    /// <remarks>
    /// This is a *shallow* check: An expression is side-effect-free if the
    /// evaluation of the expression *itself* cannot produce side effects.
    /// For example, x++ / 3 is side-effect free because the / operator
    /// does not have side effects.
    /// The intent is to "smell test" an expression for correctness in positions where
    /// its value is discarded (e.g. the left side of the comma operator).
    /// </remarks>
    internal bool IsSideEffectFree(Node node)
    {
        node = Utilities.SkipParentheses(node);
        switch (node.Kind)
        {
            case SyntaxKind.Identifier:
            case SyntaxKind.StringLiteral:
            case SyntaxKind.RegularExpressionLiteral:
            case SyntaxKind.TaggedTemplateExpression:
            case SyntaxKind.TemplateExpression:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
            case SyntaxKind.TrueKeyword:
            case SyntaxKind.FalseKeyword:
            case SyntaxKind.NullKeyword:
            case SyntaxKind.UndefinedKeyword:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ClassExpression:
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.ArrayLiteralExpression:
            case SyntaxKind.ObjectLiteralExpression:
            case SyntaxKind.TypeOfExpression:
            case SyntaxKind.NonNullExpression:
            case SyntaxKind.JsxSelfClosingElement:
            case SyntaxKind.JsxElement:
                return true;

            case SyntaxKind.ConditionalExpression:
                return IsSideEffectFree(((ConditionalExpression)node).WhenTrue!)
                    && IsSideEffectFree(((ConditionalExpression)node).WhenFalse!);

            case SyntaxKind.BinaryExpression:
                if (Utilities.IsAssignmentOperator(((BinaryExpression)node).OperatorToken!.Kind))
                {
                    return false;
                }
                return IsSideEffectFree(((BinaryExpression)node).Left!)
                    && IsSideEffectFree(((BinaryExpression)node).Right!);

            case SyntaxKind.PrefixUnaryExpression:
            case SyntaxKind.PostfixUnaryExpression:
                // Unary operators ~, !, +, and - have no side effects.
                // The rest do.
                switch (((PrefixUnaryExpression)node).Operator)
                {
                    case SyntaxKind.ExclamationToken:
                    case SyntaxKind.PlusToken:
                    case SyntaxKind.MinusToken:
                    case SyntaxKind.TildeToken:
                        return true;
                }
                return false;

            // Some forms listed here for clarity
            case SyntaxKind.VoidExpression: // Explicit opt-out
            case SyntaxKind.TypeAssertionExpression: // Not SEF, but can produce useful type warnings
            case SyntaxKind.AsExpression: // Not SEF, but can produce useful type warnings
            default:
                return false;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:40466-40468 — <c>isTypeEqualityComparableTo</c>.</summary>
    internal bool IsTypeEqualityComparableTo(IType source, IType target)
        => (target.Flags & TypeFlags.Nullable) != 0 || IsTypeComparableTo(source, target);

    // ---------------------------------------------------------------------------------------------
    // The binary-expression walk (checker.ts:40470-40712)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// ported from: src/compiler/checker.ts:40470-40612 — <c>createCheckBinaryExpression</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the reference's factory closes over nine local functions and hands them to
    /// <c>createBinaryExpressionTrampoline</c> (factory/utilities.ts:1464). The port states the same closure as this
    /// factory's own local functions — C# local functions may be declared after the <c>return</c> that uses them — and
    /// the trampoline as <c>FactoryUtilities.CreateBinaryExpressionTrampoline</c>, whose three type arguments are the
    /// outer state (<c>CheckMode</c>), the work area and the result. The returned callable is the reference's
    /// <c>(node, checkMode) =&gt; { const result = trampoline(node, checkMode); Debug.assertIsDefined(result); return
    /// result; }</c>.
    /// </para>
    /// <para>
    /// PORT-NOTE: the reference's <c>foldState</c> takes an unused <c>"left" | "right"</c> argument; the port passes it
    /// as the unused <c>bool</c> the trampoline's delegate shape needs and names it <c>_side</c>, the fleet's
    /// discard-parameter convention. <c>checkMode</c> reaches <c>CheckExpression</c> as
    /// <c>checkMode ?? CheckMode.Normal</c> because the port's checking members take the non-nullable enum whose zero is
    /// <c>Normal</c>.
    /// </para>
    /// </remarks>
    internal Func<BinaryExpression, CheckMode?, IType> CreateCheckBinaryExpression()
    {
        var trampoline = FactoryUtilities.CreateBinaryExpressionTrampoline<CheckMode?, WorkArea, IType?>(OnEnter, OnLeft, OnOperator, OnRight, OnExit, FoldState);

        return (node, checkMode) =>
        {
            var result = trampoline(node, checkMode);
            Debug.AssertIsDefined(result);
            return result!;
        };

        WorkArea OnEnter(BinaryExpression node, WorkArea? state, CheckMode? checkMode)
        {
            if (state is not null)
            {
                state.StackIndex++;
                state.Skip = false;
                SetLeftType(state, /*type*/ null);
                SetLastResult(state, /*type*/ null);
            }
            else
            {
                state = new WorkArea
                {
                    CheckMode = checkMode,
                    Skip = false,
                    StackIndex = 0,
                    TypeStack = [null, null],
                };
            }

            if (Utilities.IsInJSFile(node) && Utilities.GetAssignedExpandoInitializer(node) is not null)
            {
                state.Skip = true;
                SetLastResult(state, CheckExpression(node.Right!, checkMode ?? CheckMode.Normal));
                return state;
            }

            CheckNullishCoalesceOperands(node);

            var @operator = node.OperatorToken!.Kind;
            if (@operator == SyntaxKind.EqualsToken && (node.Left!.Kind == SyntaxKind.ObjectLiteralExpression || node.Left.Kind == SyntaxKind.ArrayLiteralExpression))
            {
                state.Skip = true;
                SetLastResult(state, CheckDestructuringAssignment(node.Left, CheckExpression(node.Right!, checkMode ?? CheckMode.Normal), checkMode ?? CheckMode.Normal, node.Right!.Kind == SyntaxKind.ThisKeyword));
                return state;
            }

            return state;
        }

        BinaryExpression? OnLeft(Expression left, WorkArea state, BinaryExpression _node)
        {
            if (!state.Skip)
            {
                return MaybeCheckExpression(state, left);
            }
            return null;
        }

        void OnOperator(Token operatorToken, WorkArea state, BinaryExpression node)
        {
            if (!state.Skip)
            {
                var leftType = GetLastResult(state);
                Debug.AssertIsDefined(leftType);
                SetLeftType(state, leftType);
                SetLastResult(state, /*type*/ null);
                var @operator = operatorToken.Kind;
                if (Utilities.IsLogicalOrCoalescingBinaryOperator(@operator))
                {
                    var parent = node.Parent!;
                    while (parent.Kind == SyntaxKind.ParenthesizedExpression || Utilities.IsLogicalOrCoalescingBinaryExpression(parent))
                    {
                        parent = parent.Parent!;
                    }
                    if (@operator == SyntaxKind.AmpersandAmpersandToken || NodeTests.IsIfStatement(parent))
                    {
                        CheckTestingKnownTruthyCallableOrAwaitableOrEnumMemberType(node.Left!, leftType!, NodeTests.IsIfStatement(parent) ? (Node)((IfStatement)parent).ThenStatement! : null);
                    }
                    if (Utilities.IsBinaryLogicalOperator(@operator))
                    {
                        CheckTruthinessOfType(leftType!, node.Left!);
                    }
                }
            }
        }

        BinaryExpression? OnRight(Expression right, WorkArea state, BinaryExpression _node)
        {
            if (!state.Skip)
            {
                return MaybeCheckExpression(state, right);
            }
            return null;
        }

        IType? OnExit(BinaryExpression node, WorkArea state)
        {
            IType? result;
            if (state.Skip)
            {
                result = GetLastResult(state);
            }
            else
            {
                var leftType = GetLeftType(state);
                Debug.AssertIsDefined(leftType);

                var rightType = GetLastResult(state);
                Debug.AssertIsDefined(rightType);

                result = CheckBinaryLikeExpressionWorker(node.Left!, node.OperatorToken!, node.Right!, leftType!, rightType!, state.CheckMode ?? CheckMode.Normal, node);
            }

            state.Skip = false;
            SetLeftType(state, /*type*/ null);
            SetLastResult(state, /*type*/ null);
            state.StackIndex--;
            return result!;
        }

        WorkArea FoldState(WorkArea state, IType? result, FactoryUtilities.BinaryExpressionSide _side)
        {
            SetLastResult(state, result);
            return state;
        }

        BinaryExpression? MaybeCheckExpression(WorkArea state, Expression node)
        {
            if (NodeTests.IsBinaryExpression(node))
            {
                return (BinaryExpression)node;
            }
            SetLastResult(state, CheckExpression(node, state.CheckMode ?? CheckMode.Normal));
            return null;
        }

        IType? GetLeftType(WorkArea state) => state.TypeStack[state.StackIndex];

        void SetLeftType(WorkArea state, IType? type) => state.TypeStack[state.StackIndex] = type;

        IType? GetLastResult(WorkArea state) => state.TypeStack[state.StackIndex + 1];

        void SetLastResult(WorkArea state, IType? type) =>
            // To reduce overhead, reuse the next stack entry to store the
            // last result. This avoids the overhead of an additional property
            // on `WorkArea` and reuses empty stack entries as we walk back up
            // the stack.
            state.TypeStack[state.StackIndex + 1] = type;
    }

    /// <summary>ported from: src/compiler/checker.ts:40471-40481 — <c>createCheckBinaryExpression</c>'s <c>WorkArea</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference declares this interface inside the factory; C# cannot nest a type in a method, so it is
    /// a nested class of this partial with the reference's four slots and names.
    /// </remarks>
    private sealed class WorkArea
    {
        public CheckMode? CheckMode { get; init; }

        public bool Skip { get; set; }

        public int StackIndex { get; set; }

        /// <summary>
        /// Holds the types from the left-side of an expression from [0..stackIndex].
        /// Holds the type of the result at stackIndex+1. This allows us to reuse existing stack entries
        /// and avoid storing an extra property on the object (i.e., `lastResult`).
        /// </summary>
        public List<IType?> TypeStack { get; set; } = [];
    }

    /// <summary>ported from: src/compiler/checker.ts:1549 — <c>checkBinaryExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS assigns this slot at checker construction (<c>var checkBinaryExpression =
    /// createCheckBinaryExpression()</c>); the port fills it on the first call, which is the same value and keeps the
    /// trampoline out of the constructor <c>Checker.cs</c> owns.
    /// </remarks>
    private Func<BinaryExpression, CheckMode?, IType>? _checkBinaryExpression;

    /// <summary>ported from: src/compiler/checker.ts:40365, :41915 — <c>checkBinaryExpression</c>'s callable.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's slot is a function value read at two call sites; the port keeps the slot private and
    /// states the callable as this member so the sibling slices call <c>CheckBinaryExpression(node, checkMode)</c>.
    /// </remarks>
    internal IType CheckBinaryExpression(BinaryExpression node, CheckMode checkMode = CheckMode.Normal)
        => (_checkBinaryExpression ??= CreateCheckBinaryExpression())(node, checkMode);

    /// <summary>ported from: src/compiler/transformers/utilities.ts:490-493 — <c>isCompoundAssignment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: its reference home is <c>transformers/utilities.ts</c>, a module no port slice owns and no port file
    /// ports; the checker is its only caller and this is the first declaration of the name in the port, so it is ported
    /// here and reported with that reference range.
    /// </remarks>
    internal static bool IsCompoundAssignment(SyntaxKind kind)
        => kind >= SyntaxKind.FirstCompoundAssignment && kind <= SyntaxKind.LastCompoundAssignment;

    /// <summary>ported from: src/compiler/checker.ts:40614-40637 — <c>checkNullishCoalesceOperands</c>.</summary>
    internal void CheckNullishCoalesceOperands(BinaryExpression node)
    {
        if (node.OperatorToken!.Kind != SyntaxKind.QuestionQuestionToken)
        {
            return;
        }
        if (NodeTests.IsBinaryExpression(node.Parent!))
        {
            var left = ((BinaryExpression)node.Parent!).Left!;
            var operatorToken = ((BinaryExpression)node.Parent!).OperatorToken!;
            if (NodeTests.IsBinaryExpression(left) && operatorToken.Kind == SyntaxKind.BarBarToken)
            {
                GrammarErrorOnNode(left, Diagnostics._0_and_1_operations_cannot_be_mixed_without_parentheses, ScannerUtilities.TokenToString(SyntaxKind.QuestionQuestionToken), ScannerUtilities.TokenToString(operatorToken.Kind));
            }
        }
        else if (NodeTests.IsBinaryExpression(node.Left!))
        {
            var operatorToken = ((BinaryExpression)node.Left!).OperatorToken!;
            if (operatorToken.Kind == SyntaxKind.BarBarToken || operatorToken.Kind == SyntaxKind.AmpersandAmpersandToken)
            {
                GrammarErrorOnNode(node.Left, Diagnostics._0_and_1_operations_cannot_be_mixed_without_parentheses, ScannerUtilities.TokenToString(operatorToken.Kind), ScannerUtilities.TokenToString(SyntaxKind.QuestionQuestionToken));
            }
        }
        else if (NodeTests.IsBinaryExpression(node.Right!))
        {
            var operatorToken = ((BinaryExpression)node.Right!).OperatorToken!;
            if (operatorToken.Kind == SyntaxKind.AmpersandAmpersandToken)
            {
                GrammarErrorOnNode(node.Right, Diagnostics._0_and_1_operations_cannot_be_mixed_without_parentheses, ScannerUtilities.TokenToString(SyntaxKind.QuestionQuestionToken), ScannerUtilities.TokenToString(operatorToken.Kind));
            }
        }
        CheckNullishCoalesceOperandLeft(node);
    }

    /// <summary>ported from: src/compiler/checker.ts:40639-40650 — <c>checkNullishCoalesceOperandLeft</c>.</summary>
    internal void CheckNullishCoalesceOperandLeft(BinaryExpression node)
    {
        var leftTarget = FactoryUtilities.SkipOuterExpressions(node.Left!, OuterExpressionKinds.All);
        var nullishSemantics = GetSyntacticNullishnessSemantics(leftTarget);
        if (nullishSemantics != PredicateSemantics.Sometimes)
        {
            if (nullishSemantics == PredicateSemantics.Always)
            {
                Error(leftTarget, Diagnostics.This_expression_is_always_nullish);
            }
            else
            {
                Error(leftTarget, Diagnostics.Right_operand_of_is_unreachable_because_the_left_operand_is_never_nullish);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:40652-40693 — <c>getSyntacticNullishnessSemantics</c>.</summary>
    internal PredicateSemantics GetSyntacticNullishnessSemantics(Node node)
    {
        node = FactoryUtilities.SkipOuterExpressions(node);
        switch (node.Kind)
        {
            case SyntaxKind.AwaitExpression:
            case SyntaxKind.CallExpression:
            case SyntaxKind.TaggedTemplateExpression:
            case SyntaxKind.ElementAccessExpression:
            case SyntaxKind.MetaProperty:
            case SyntaxKind.NewExpression:
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.YieldExpression:
            case SyntaxKind.ThisKeyword:
                return PredicateSemantics.Sometimes;
            case SyntaxKind.BinaryExpression:
                // List of operators that can produce null/undefined:
                // = ??= ?? || ||= && &&=
                switch (((BinaryExpression)node).OperatorToken!.Kind)
                {
                    case SyntaxKind.BarBarToken:
                    case SyntaxKind.BarBarEqualsToken:
                    case SyntaxKind.AmpersandAmpersandToken:
                    case SyntaxKind.AmpersandAmpersandEqualsToken:
                        return PredicateSemantics.Sometimes;
                    // For these operator kinds, the right operand is effectively controlling
                    case SyntaxKind.CommaToken:
                    case SyntaxKind.EqualsToken:
                    case SyntaxKind.QuestionQuestionToken:
                    case SyntaxKind.QuestionQuestionEqualsToken:
                        return GetSyntacticNullishnessSemantics(((BinaryExpression)node).Right!);
                }
                return PredicateSemantics.Never;
            case SyntaxKind.ConditionalExpression:
                return GetSyntacticNullishnessSemantics(((ConditionalExpression)node).WhenTrue!) | GetSyntacticNullishnessSemantics(((ConditionalExpression)node).WhenFalse!);
            case SyntaxKind.NullKeyword:
                return PredicateSemantics.Always;
            case SyntaxKind.Identifier:
                if (ReferenceEquals(GetResolvedSymbol((Identifier)node), _undefinedSymbol))
                {
                    return PredicateSemantics.Always;
                }
                return PredicateSemantics.Sometimes;
        }
        return PredicateSemantics.Never;
    }

    /// <summary>ported from: src/compiler/checker.ts:40697-40712 — <c>checkBinaryLikeExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's second parameter is <c>BinaryOperatorToken</c>; the port's <c>BinaryExpression.OperatorToken</c>
    /// and <c>ShorthandPropertyAssignment.EqualsToken</c> are the <c>Token</c> base class and its
    /// <c>PunctuationToken</c> subclass, so the parameter is <c>Token</c> and every operator read is its <c>Kind</c>.
    /// <para>
    /// Note that this and `checkBinaryExpression` above should behave mostly the same, except this elides some
    /// expression-wide checks and does not use a work stack to fold nested binary expressions into the same callstack frame
    /// </para>
    /// </remarks>
    internal IType CheckBinaryLikeExpression(Expression left, Token operatorToken, Expression right, CheckMode checkMode = CheckMode.Normal, Node? errorNode = null)
    {
        var @operator = operatorToken.Kind;
        if (@operator == SyntaxKind.EqualsToken && (left.Kind == SyntaxKind.ObjectLiteralExpression || left.Kind == SyntaxKind.ArrayLiteralExpression))
        {
            return CheckDestructuringAssignment(left, CheckExpression(right, checkMode), checkMode, right.Kind == SyntaxKind.ThisKeyword);
        }
        IType leftType;
        if (Utilities.IsBinaryLogicalOperator(@operator))
        {
            leftType = CheckTruthinessExpression(left, checkMode);
        }
        else
        {
            leftType = CheckExpression(left, checkMode);
        }

        var rightType = CheckExpression(right, checkMode);
        return CheckBinaryLikeExpressionWorker(left, operatorToken, right, leftType, rightType, checkMode, errorNode);
    }

    /// <summary>ported from: src/compiler/checker.ts:40714-41178 — <c>checkBinaryLikeExpressionWorker</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the reference's second parameter is <c>BinaryOperatorToken</c>; the port takes the <c>Token</c> base
    /// class its <c>OperatorToken</c> slots hold, and each operator read is its <c>Kind</c>. TS's
    /// <c>suggestedOperator</c> is <c>PunctuationSyntaxKind | undefined</c>, which the port states as <c>SyntaxKind?</c>
    /// because the generated enums have no punctuation-only kind.
    /// </para>
    /// <para>
    /// PORT-NOTE: the reference's two boolean-taking local helpers (<c>reportOperatorError</c>,
    /// <c>reportOperatorErrorUnless</c>) are typed as functions here rather than as <c>TypeComparer</c>-shaped
    /// delegates, because every argument the reference passes them returns <c>boolean</c> and the port's
    /// <c>TypeComparer</c> is the <c>Ternary</c>-returning relation delegate.
    /// </para>
    /// <para>
    /// PORT-NOTE: the reference's <c>evaluate(right).value</c> reads the checker's evaluator slot (checker.ts:1553, the
    /// M6 utilities slice's <c>createEvaluator</c> port) and ts's number formatting of the shift count is
    /// <c>Js.NumberToString</c>, the port's mapping for a JS number handed to a diagnostic.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>textSpanContainsPosition(diag, start)</c> (utilitiesPublic.ts:356) relies on
    /// <c>DiagnosticWithLocation</c> being structurally a <c>TextSpan</c>; the port's <c>Diagnostic.Start</c>/<c>Length</c>
    /// are nullable and not an <c>ITextSpan</c>, so the comma arm states the same two comparisons directly.
    /// </para>
    /// </remarks>
    internal IType CheckBinaryLikeExpressionWorker(
        Expression left,
        Token operatorToken,
        Expression right,
        IType leftType,
        IType rightType,
        CheckMode checkMode = CheckMode.Normal,
        Node? errorNode = null)
    {
        var @operator = operatorToken.Kind;
        switch (@operator)
        {
            case SyntaxKind.AsteriskToken:
            case SyntaxKind.AsteriskAsteriskToken:
            case SyntaxKind.AsteriskEqualsToken:
            case SyntaxKind.AsteriskAsteriskEqualsToken:
            case SyntaxKind.SlashToken:
            case SyntaxKind.SlashEqualsToken:
            case SyntaxKind.PercentToken:
            case SyntaxKind.PercentEqualsToken:
            case SyntaxKind.MinusToken:
            case SyntaxKind.MinusEqualsToken:
            case SyntaxKind.LessThanLessThanToken:
            case SyntaxKind.LessThanLessThanEqualsToken:
            case SyntaxKind.GreaterThanGreaterThanToken:
            case SyntaxKind.GreaterThanGreaterThanEqualsToken:
            case SyntaxKind.GreaterThanGreaterThanGreaterThanToken:
            case SyntaxKind.GreaterThanGreaterThanGreaterThanEqualsToken:
            case SyntaxKind.BarToken:
            case SyntaxKind.BarEqualsToken:
            case SyntaxKind.CaretToken:
            case SyntaxKind.CaretEqualsToken:
            case SyntaxKind.AmpersandToken:
            case SyntaxKind.AmpersandEqualsToken:
            {
                if (ReferenceEquals(leftType, _silentNeverType) || ReferenceEquals(rightType, _silentNeverType))
                {
                    return _silentNeverType;
                }

                leftType = CheckNonNullType(leftType, left);
                rightType = CheckNonNullType(rightType, right);

                SyntaxKind? suggestedOperator;
                // if a user tries to apply a bitwise operator to 2 boolean operands
                // try and return them a helpful suggestion
                if (
                    (leftType.Flags & TypeFlags.BooleanLike) != 0
                    && (rightType.Flags & TypeFlags.BooleanLike) != 0
                    && (suggestedOperator = GetSuggestedBooleanOperator(operatorToken.Kind)) is not null
                )
                {
                    Error(errorNode ?? operatorToken, Diagnostics.The_0_operator_is_not_allowed_for_boolean_types_Consider_using_1_instead, ScannerUtilities.TokenToString(operatorToken.Kind), ScannerUtilities.TokenToString(suggestedOperator.Value));
                    return _numberType;
                }
                else
                {
                    // otherwise just check each operand separately and report errors as normal
                    var leftOk = CheckArithmeticOperandType(left, leftType, Diagnostics.The_left_hand_side_of_an_arithmetic_operation_must_be_of_type_any_number_bigint_or_an_enum_type, /*isAwaitValid*/ true);
                    var rightOk = CheckArithmeticOperandType(right, rightType, Diagnostics.The_right_hand_side_of_an_arithmetic_operation_must_be_of_type_any_number_bigint_or_an_enum_type, /*isAwaitValid*/ true);
                    IType resultType;
                    // If both are any or unknown, allow operation; assume it will resolve to number
                    if (
                        IsTypeAssignableToKind(leftType, TypeFlags.AnyOrUnknown) && IsTypeAssignableToKind(rightType, TypeFlags.AnyOrUnknown)
                        // Or, if neither could be bigint, implicit coercion results in a number result
                        || !(MaybeTypeOfKind(leftType, TypeFlags.BigIntLike) || MaybeTypeOfKind(rightType, TypeFlags.BigIntLike))
                    )
                    {
                        resultType = _numberType;
                    }
                    // At least one is assignable to bigint, so check that both are
                    else if (BothAreBigIntLike(leftType, rightType))
                    {
                        switch (@operator)
                        {
                            case SyntaxKind.GreaterThanGreaterThanGreaterThanToken:
                            case SyntaxKind.GreaterThanGreaterThanGreaterThanEqualsToken:
                                ReportOperatorError();
                                break;
                            case SyntaxKind.AsteriskAsteriskToken:
                            case SyntaxKind.AsteriskAsteriskEqualsToken:
                                if (_languageVersion < ScriptTarget.ES2016)
                                {
                                    Error(errorNode, Diagnostics.Exponentiation_cannot_be_performed_on_bigint_values_unless_the_target_option_is_set_to_es2016_or_later);
                                }
                                break;
                        }
                        resultType = _bigintType;
                    }
                    // Exactly one of leftType/rightType is assignable to bigint
                    else
                    {
                        ReportOperatorError(BothAreBigIntLike);
                        resultType = _errorType;
                    }
                    if (leftOk && rightOk)
                    {
                        CheckAssignmentOperator(resultType);
                        switch (@operator)
                        {
                            case SyntaxKind.LessThanLessThanToken:
                            case SyntaxKind.LessThanLessThanEqualsToken:
                            case SyntaxKind.GreaterThanGreaterThanToken:
                            case SyntaxKind.GreaterThanGreaterThanEqualsToken:
                            case SyntaxKind.GreaterThanGreaterThanGreaterThanToken:
                            case SyntaxKind.GreaterThanGreaterThanGreaterThanEqualsToken:
                            {
                                var rhsEval = _evaluate(right);
                                if (rhsEval.Value is double rhsValue && Math.Abs(rhsValue) >= 32)
                                {
                                    ErrorOrSuggestion(
                                        NodeTests.IsEnumMember(Utilities.WalkUpParenthesizedExpressions(right.Parent!.Parent!)), // elevate from suggestion to error within an enum member
                                        errorNode ?? operatorToken,
                                        Diagnostics.This_operation_can_be_simplified_This_shift_is_identical_to_0_1_2,
                                        Utilities.GetTextOfNode(left),
                                        ScannerUtilities.TokenToString(@operator),
                                        Js.NumberToString(rhsValue % 32));
                                }
                                break;
                            }
                            default:
                                break;
                        }
                    }
                    return resultType;
                }
            }
            case SyntaxKind.PlusToken:
            case SyntaxKind.PlusEqualsToken:
            {
                if (ReferenceEquals(leftType, _silentNeverType) || ReferenceEquals(rightType, _silentNeverType))
                {
                    return _silentNeverType;
                }

                if (!IsTypeAssignableToKind(leftType, TypeFlags.StringLike) && !IsTypeAssignableToKind(rightType, TypeFlags.StringLike))
                {
                    leftType = CheckNonNullType(leftType, left);
                    rightType = CheckNonNullType(rightType, right);
                }

                IType? resultType = null;
                if (IsTypeAssignableToKind(leftType, TypeFlags.NumberLike, /*strict*/ true) && IsTypeAssignableToKind(rightType, TypeFlags.NumberLike, /*strict*/ true))
                {
                    // Operands of an enum type are treated as having the primitive type Number.
                    // If both operands are of the Number primitive type, the result is of the Number primitive type.
                    resultType = _numberType;
                }
                else if (IsTypeAssignableToKind(leftType, TypeFlags.BigIntLike, /*strict*/ true) && IsTypeAssignableToKind(rightType, TypeFlags.BigIntLike, /*strict*/ true))
                {
                    // If both operands are of the BigInt primitive type, the result is of the BigInt primitive type.
                    resultType = _bigintType;
                }
                else if (IsTypeAssignableToKind(leftType, TypeFlags.StringLike, /*strict*/ true) || IsTypeAssignableToKind(rightType, TypeFlags.StringLike, /*strict*/ true))
                {
                    // If one or both operands are of the String primitive type, the result is of the String primitive type.
                    resultType = _stringType;
                }
                else if (IsTypeAny(leftType) || IsTypeAny(rightType))
                {
                    // Otherwise, the result is of type Any.
                    // NOTE: unknown type here denotes error type. Old compiler treated this case as any type so do we.
                    resultType = IsErrorType(leftType) || IsErrorType(rightType) ? _errorType : _anyType;
                }

                // Symbols are not allowed at all in arithmetic expressions
                if (resultType is not null && !CheckForDisallowedESSymbolOperand(@operator))
                {
                    return resultType;
                }

                if (resultType is null)
                {
                    // Types that have a reasonably good chance of being a valid operand type.
                    // If both types have an awaited type of one of these, we'll assume the user
                    // might be missing an await without doing an exhaustive check that inserting
                    // await(s) will actually be a completely valid binary expression.
                    var closeEnoughKind = TypeFlags.NumberLike | TypeFlags.BigIntLike | TypeFlags.StringLike | TypeFlags.AnyOrUnknown;
                    ReportOperatorError((left, right) =>
                        IsTypeAssignableToKind(left, closeEnoughKind) &&
                        IsTypeAssignableToKind(right, closeEnoughKind));
                    return _anyType;
                }

                if (@operator == SyntaxKind.PlusEqualsToken)
                {
                    CheckAssignmentOperator(resultType);
                }
                return resultType;
            }
            case SyntaxKind.LessThanToken:
            case SyntaxKind.GreaterThanToken:
            case SyntaxKind.LessThanEqualsToken:
            case SyntaxKind.GreaterThanEqualsToken:
                if (CheckForDisallowedESSymbolOperand(@operator))
                {
                    leftType = GetBaseTypeOfLiteralTypeForComparison(CheckNonNullType(leftType, left));
                    rightType = GetBaseTypeOfLiteralTypeForComparison(CheckNonNullType(rightType, right));
                    ReportOperatorErrorUnless((left, right) =>
                    {
                        if (IsTypeAny(left) || IsTypeAny(right))
                        {
                            return true;
                        }
                        var leftAssignableToNumber = IsTypeAssignableTo(left, _numberOrBigIntType);
                        var rightAssignableToNumber = IsTypeAssignableTo(right, _numberOrBigIntType);
                        return leftAssignableToNumber && rightAssignableToNumber
                            || !leftAssignableToNumber && !rightAssignableToNumber && AreTypesComparable(left, right);
                    });
                }
                return _booleanType;
            case SyntaxKind.EqualsEqualsToken:
            case SyntaxKind.ExclamationEqualsToken:
            case SyntaxKind.EqualsEqualsEqualsToken:
            case SyntaxKind.ExclamationEqualsEqualsToken:
                // We suppress errors in CheckMode.TypeOnly (meaning the invocation came from getTypeOfExpression). During
                // control flow analysis it is possible for operands to temporarily have narrower types, and those narrower
                // types may cause the operands to not be comparable. We don't want such errors reported (see #46475).
                if ((checkMode & CheckMode.TypeOnly) == 0)
                {
                    if (
                        (Utilities.IsLiteralExpressionOfObject(left) || Utilities.IsLiteralExpressionOfObject(right))
                        // only report for === and !== in JS, not == or !=
                        && (!Utilities.IsInJSFile(left) || (@operator == SyntaxKind.EqualsEqualsEqualsToken || @operator == SyntaxKind.ExclamationEqualsEqualsToken))
                    )
                    {
                        var eqType = @operator == SyntaxKind.EqualsEqualsToken || @operator == SyntaxKind.EqualsEqualsEqualsToken;
                        Error(errorNode, Diagnostics.This_condition_will_always_return_0_since_JavaScript_compares_objects_by_reference_not_value, eqType ? "false" : "true");
                    }
                    CheckNaNEquality(errorNode, @operator, left, right);
                    ReportOperatorErrorUnless((left, right) => IsTypeEqualityComparableTo(left, right) || IsTypeEqualityComparableTo(right, left));
                }
                return _booleanType;
            case SyntaxKind.InstanceOfKeyword:
                return CheckInstanceOfExpression(left, right, leftType, rightType, checkMode);
            case SyntaxKind.InKeyword:
                return CheckInExpression(left, right, leftType, rightType);
            case SyntaxKind.AmpersandAmpersandToken:
            case SyntaxKind.AmpersandAmpersandEqualsToken:
            {
                var resultType = HasTypeFacts(leftType, TypeFacts.Truthy) ?
                    GetUnionType([ExtractDefinitelyFalsyTypes(_strictNullChecks ? leftType : GetBaseTypeOfLiteralType(rightType)), rightType]) :
                    leftType;
                if (@operator == SyntaxKind.AmpersandAmpersandEqualsToken)
                {
                    CheckAssignmentOperator(rightType);
                }
                return resultType;
            }
            case SyntaxKind.BarBarToken:
            case SyntaxKind.BarBarEqualsToken:
            {
                var resultType = HasTypeFacts(leftType, TypeFacts.Falsy) ?
                    GetUnionType([GetNonNullableType(RemoveDefinitelyFalsyTypes(leftType)), rightType], UnionReduction.Subtype) :
                    leftType;
                if (@operator == SyntaxKind.BarBarEqualsToken)
                {
                    CheckAssignmentOperator(rightType);
                }
                return resultType;
            }
            case SyntaxKind.QuestionQuestionToken:
            case SyntaxKind.QuestionQuestionEqualsToken:
            {
                var resultType = HasTypeFacts(leftType, TypeFacts.EQUndefinedOrNull) ?
                    GetUnionType([GetNonNullableType(leftType), rightType], UnionReduction.Subtype) :
                    leftType;
                if (@operator == SyntaxKind.QuestionQuestionEqualsToken)
                {
                    CheckAssignmentOperator(rightType);
                }
                return resultType;
            }
            case SyntaxKind.EqualsToken:
            {
                var declKind = NodeTests.IsBinaryExpression(left.Parent!) ? Utilities.GetAssignmentDeclarationKind(left.Parent!) : AssignmentDeclarationKind.None;
                CheckAssignmentDeclaration(declKind, rightType);
                if (IsAssignmentDeclaration(declKind))
                {
                    if (
                        (rightType.Flags & TypeFlags.Object) == 0
                        || declKind != AssignmentDeclarationKind.ModuleExports
                            && declKind != AssignmentDeclarationKind.Prototype
                            && !IsEmptyObjectType(rightType)
                            && !IsFunctionObjectType((IObjectType)rightType)
                            && (GetObjectFlags(rightType) & ObjectFlags.Class) == 0
                    )
                    {
                        // don't check assignability of module.exports=, C.prototype=, or expando types because they will necessarily be incomplete
                        CheckAssignmentOperator(rightType);
                    }
                    return leftType;
                }
                else
                {
                    CheckAssignmentOperator(rightType);
                    return rightType;
                }
            }
            case SyntaxKind.CommaToken:
                if (_compilerOptions.AllowUnreachableCode != true && IsSideEffectFree(left) && !IsIndirectCall((BinaryExpression)left.Parent!))
                {
                    var sf = Utilities.GetSourceFileOfNode(left)!;
                    var sourceText = sf.Text!;
                    var start = ScannerUtilities.SkipTrivia(sourceText, left.Pos);
                    var isInDiag2657 = Core.Some(sf.ParseDiagnostics!, diag =>
                    {
                        if (diag.Code != Diagnostics.JSX_expressions_must_have_one_parent_element.Code) return false;
                        return diag.Start is int diagStart && diag.Length is int diagLength
                            && start >= diagStart && start < diagStart + diagLength;
                    });
                    if (!isInDiag2657) Error(left, Diagnostics.Left_side_of_comma_operator_is_unused_and_has_no_side_effects);
                }
                return rightType;

            default:
                return Debug.Fail<IType>();
        }

        bool BothAreBigIntLike(IType left, IType right)
            => IsTypeAssignableToKind(left, TypeFlags.BigIntLike) && IsTypeAssignableToKind(right, TypeFlags.BigIntLike);

        void CheckAssignmentDeclaration(AssignmentDeclarationKind kind, IType rightType)
        {
            if (kind == AssignmentDeclarationKind.ModuleExports)
            {
                foreach (var prop in GetPropertiesOfObjectType(rightType))
                {
                    var propType = GetTypeOfSymbol(prop);
                    if (propType.Symbol is not null && (propType.Symbol.Flags & SymbolFlags.Class) != 0)
                    {
                        var name = prop.EscapedName;
                        var symbol = ResolveName((Node)prop.ValueDeclaration!, name!, SymbolFlags.Type, /*nameNotFoundMessage*/ null, /*isUse*/ false, /*excludeGlobals*/ false);
                        if (symbol?.Declarations is not null && Core.Some(symbol.Declarations, d => NodeTests.IsJSDocTypedefTag((Node)d)))
                        {
                            AddDuplicateDeclarationErrorsForSymbols(symbol, Diagnostics.Duplicate_identifier_0, Utilities.UnescapeLeadingUnderscores(name!), prop);
                            AddDuplicateDeclarationErrorsForSymbols(prop, Diagnostics.Duplicate_identifier_0, Utilities.UnescapeLeadingUnderscores(name!), symbol);
                        }
                    }
                }
            }
        }

        // Return true for "indirect calls", (i.e. `(0, x.f)(...)` or `(0, eval)(...)`), which prevents passing `this`.
        bool IsIndirectCall(BinaryExpression node)
            => node.Parent!.Kind == SyntaxKind.ParenthesizedExpression
                && NodeTests.IsNumericLiteral(node.Left!)
                && ((NumericLiteral)node.Left!).Text == "0"
                && (NodeTests.IsCallExpression(node.Parent!.Parent!) && ReferenceEquals(((CallExpression)node.Parent!.Parent!).Expression, node.Parent) || node.Parent!.Parent!.Kind == SyntaxKind.TaggedTemplateExpression)
                // special-case for "eval" because it's the only non-access case where an indirect call actually affects behavior.
                && (Utilities.IsAccessExpression(node.Right!) || NodeTests.IsIdentifier(node.Right!) && ((Identifier)node.Right!).EscapedText == "eval");

        // Return true if there was no error, false if there was an error.
        bool CheckForDisallowedESSymbolOperand(SyntaxKind @operator)
        {
            var offendingSymbolOperand = MaybeTypeOfKindConsideringBaseConstraint(leftType, TypeFlags.ESSymbolLike) ? left :
                MaybeTypeOfKindConsideringBaseConstraint(rightType, TypeFlags.ESSymbolLike) ? right :
                null;

            if (offendingSymbolOperand is not null)
            {
                Error(offendingSymbolOperand, Diagnostics.The_0_operator_cannot_be_applied_to_type_symbol, ScannerUtilities.TokenToString(@operator));
                return false;
            }

            return true;
        }

        SyntaxKind? GetSuggestedBooleanOperator(SyntaxKind @operator)
            => @operator switch
            {
                SyntaxKind.BarToken or SyntaxKind.BarEqualsToken => SyntaxKind.BarBarToken,
                SyntaxKind.CaretToken or SyntaxKind.CaretEqualsToken => SyntaxKind.ExclamationEqualsEqualsToken,
                SyntaxKind.AmpersandToken or SyntaxKind.AmpersandEqualsToken => SyntaxKind.AmpersandAmpersandToken,
                _ => null,
            };

        void CheckAssignmentOperator(IType valueType)
        {
            if (Utilities.IsAssignmentOperator(@operator))
            {
                AddLazyDiagnostic(CheckAssignmentOperatorWorker);
            }

            void CheckAssignmentOperatorWorker()
            {
                var assigneeType = leftType;

                // getters can be a subtype of setters, so to check for assignability we use the setter's type instead
                if (IsCompoundAssignment(operatorToken.Kind) && left.Kind == SyntaxKind.PropertyAccessExpression)
                {
                    assigneeType = CheckPropertyAccessExpression((PropertyAccessExpression)left, /*checkMode*/ CheckMode.Normal, /*writeOnly*/ true);
                }

                // TypeScript 1.0 spec (April 2014): 4.17
                // An assignment of the form
                //    VarExpr = ValueExpr
                // requires VarExpr to be classified as a reference
                // A compound assignment furthermore requires VarExpr to be classified as a reference (section 4.1)
                // and the type of the non-compound operation to be assignable to the type of VarExpr.

                if (CheckReferenceExpression(left, Diagnostics.The_left_hand_side_of_an_assignment_expression_must_be_a_variable_or_a_property_access, Diagnostics.The_left_hand_side_of_an_assignment_expression_may_not_be_an_optional_property_access))
                {
                    DiagnosticMessage? headMessage = null;
                    if (_exactOptionalPropertyTypes == true && NodeTests.IsPropertyAccessExpression(left) && MaybeTypeOfKind(valueType, TypeFlags.Undefined))
                    {
                        var target = GetTypeOfPropertyOfType(GetTypeOfExpression(((PropertyAccessExpression)left).Expression!), Utilities.IdText(((PropertyAccessExpression)left).Name!));
                        if (IsExactOptionalPropertyMismatch(valueType, target))
                        {
                            headMessage = Diagnostics.Type_0_is_not_assignable_to_type_1_with_exactOptionalPropertyTypes_Colon_true_Consider_adding_undefined_to_the_type_of_the_target;
                        }
                    }
                    // to avoid cascading errors check assignability only if 'isReference' check succeeded and no errors were reported
                    CheckTypeAssignableToAndOptionallyElaborate(valueType, assigneeType, left, right, headMessage);
                }
            }
        }

        bool IsAssignmentDeclaration(AssignmentDeclarationKind kind)
        {
            switch (kind)
            {
                case AssignmentDeclarationKind.ModuleExports:
                    return true;
                case AssignmentDeclarationKind.ExportsProperty:
                case AssignmentDeclarationKind.Property:
                case AssignmentDeclarationKind.Prototype:
                case AssignmentDeclarationKind.PrototypeProperty:
                case AssignmentDeclarationKind.ThisProperty:
                {
                    var symbol = GetSymbolOfNode(left);
                    var init = Utilities.GetAssignedExpandoInitializer(right);
                    return init is not null && NodeTests.IsObjectLiteralExpression(init)
                        && (symbol?.Exports?.Count ?? 0) > 0;
                }
                default:
                    return false;
            }
        }

        /// <summary>Returns true if an error is reported</summary>
        bool ReportOperatorErrorUnless(Func<IType, IType, bool> typesAreCompatible)
        {
            if (!typesAreCompatible(leftType, rightType))
            {
                ReportOperatorError(typesAreCompatible);
                return true;
            }
            return false;
        }

        void ReportOperatorError(Func<IType, IType, bool>? isRelated = null)
        {
            var wouldWorkWithAwait = false;
            var errNode = errorNode ?? operatorToken;
            if (isRelated is not null)
            {
                var awaitedLeftType = GetAwaitedTypeNoAlias(leftType);
                var awaitedRightType = GetAwaitedTypeNoAlias(rightType);
                wouldWorkWithAwait = !(ReferenceEquals(awaitedLeftType, leftType) && ReferenceEquals(awaitedRightType, rightType))
                    && awaitedLeftType is not null && awaitedRightType is not null
                    && isRelated(awaitedLeftType, awaitedRightType);
            }

            var effectiveLeft = leftType;
            var effectiveRight = rightType;
            if (!wouldWorkWithAwait && isRelated is not null)
            {
                (effectiveLeft, effectiveRight) = GetBaseTypesIfUnrelated(leftType, rightType, isRelated);
            }
            var (leftStr, rightStr) = GetTypeNamesForErrorDisplay(effectiveLeft, effectiveRight);
            if (TryGiveBetterPrimaryError(errNode, wouldWorkWithAwait, leftStr, rightStr) is null)
            {
                ErrorAndMaybeSuggestAwait(
                    errNode,
                    wouldWorkWithAwait,
                    Diagnostics.Operator_0_cannot_be_applied_to_types_1_and_2,
                    ScannerUtilities.TokenToString(operatorToken.Kind),
                    leftStr,
                    rightStr);
            }
        }

        Diagnostic? TryGiveBetterPrimaryError(Node errNode, bool maybeMissingAwait, string leftStr, string rightStr)
        {
            switch (operatorToken.Kind)
            {
                case SyntaxKind.EqualsEqualsEqualsToken:
                case SyntaxKind.EqualsEqualsToken:
                case SyntaxKind.ExclamationEqualsEqualsToken:
                case SyntaxKind.ExclamationEqualsToken:
                    return ErrorAndMaybeSuggestAwait(
                        errNode,
                        maybeMissingAwait,
                        Diagnostics.This_comparison_appears_to_be_unintentional_because_the_types_0_and_1_have_no_overlap,
                        leftStr,
                        rightStr);
                default:
                    return null;
            }
        }

        void CheckNaNEquality(Node? errorNode, SyntaxKind @operator, Expression left, Expression right)
        {
            var isLeftNaN = IsGlobalNaN(Utilities.SkipParentheses(left));
            var isRightNaN = IsGlobalNaN(Utilities.SkipParentheses(right));
            if (isLeftNaN || isRightNaN)
            {
                var err = Error(errorNode, Diagnostics.This_condition_will_always_return_0, ScannerUtilities.TokenToString(@operator == SyntaxKind.EqualsEqualsEqualsToken || @operator == SyntaxKind.EqualsEqualsToken ? SyntaxKind.FalseKeyword : SyntaxKind.TrueKeyword));
                if (isLeftNaN && isRightNaN) return;
                var operatorString = @operator == SyntaxKind.ExclamationEqualsEqualsToken || @operator == SyntaxKind.ExclamationEqualsToken ? ScannerUtilities.TokenToString(SyntaxKind.ExclamationToken) : "";
                var location = isLeftNaN ? right : left;
                var expression = Utilities.SkipParentheses(location);
                Utilities.AddRelatedInfo(err, Utilities.CreateDiagnosticForNode(location, Diagnostics.Did_you_mean_0, $"{operatorString}Number.isNaN({(Utilities.IsEntityNameExpression(expression) ? Utilities.EntityNameToString(expression) : "...")})"));
            }
        }

        bool IsGlobalNaN(Expression expr)
        {
            if (NodeTests.IsIdentifier(expr) && ((Identifier)expr).EscapedText == "NaN")
            {
                var globalNaNSymbol = GetGlobalNaNSymbol();
                return globalNaNSymbol is not null && ReferenceEquals(globalNaNSymbol, GetResolvedSymbol((Identifier)expr));
            }
            return false;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:41180-41190 — <c>getBaseTypesIfUnrelated</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers the two-element tuple <c>[Type, Type]</c>; the port states it as a value tuple, the shape
    /// the checking block's other tuple-returning members use.
    /// </remarks>
    internal (IType, IType) GetBaseTypesIfUnrelated(IType leftType, IType rightType, Func<IType, IType, bool> isRelated)
    {
        var effectiveLeft = leftType;
        var effectiveRight = rightType;
        var leftBase = GetBaseTypeOfLiteralType(leftType);
        var rightBase = GetBaseTypeOfLiteralType(rightType);
        if (!isRelated(leftBase, rightBase))
        {
            effectiveLeft = leftBase;
            effectiveRight = rightBase;
        }
        return (effectiveLeft, effectiveRight);
    }

    /// <summary>ported from: src/compiler/checker.ts:41192-41266 — <c>checkYieldExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>languageVersion &lt; LanguageFeatureMinimumTarget.AsyncGenerators</c> and <c>&lt; …Generators</c>
    /// are written against <c>ScriptTarget.ES2018</c> and <c>ScriptTarget.ES2015</c>, the targets that table holds
    /// (types.ts:8488). The reference's <c>iterationTypes &amp;&amp; iterationTypes.yieldType || anyType</c> is the port's
    /// null-coalescing form of the same two tests.
    /// </remarks>
    internal IType CheckYieldExpression(YieldExpression node)
    {
        AddLazyDiagnostic(CheckYieldExpressionGrammar);

        var func = Utilities.GetContainingFunction(node);
        if (func is null) return _anyType;
        var functionFlags = Utilities.GetFunctionFlags(func);

        if ((functionFlags & FunctionFlags.Generator) == 0)
        {
            // If the user's code is syntactically correct, the func should always have a star. After all, we are in a yield context.
            return _anyType;
        }

        var isAsync = (functionFlags & FunctionFlags.Async) != 0;
        if (node.AsteriskToken is not null)
        {
            // Async generator functions prior to ES2018 require the __await, __asyncDelegator,
            // and __asyncValues helpers
            if (isAsync && _languageVersion < ScriptTarget.ES2018) // LanguageFeatureMinimumTarget.AsyncGenerators
            {
                CheckExternalEmitHelpers(node, ExternalEmitHelpers.AsyncDelegatorIncludes);
            }

            // Generator functions prior to ES2015 require the __values helper
            if (!isAsync && _languageVersion < ScriptTarget.ES2015 && _compilerOptions.DownlevelIteration == true) // LanguageFeatureMinimumTarget.Generators
            {
                CheckExternalEmitHelpers(node, ExternalEmitHelpers.Values);
            }
        }

        // There is no point in doing an assignability check if the function
        // has no explicit return type because the return type is directly computed
        // from the yield expressions.
        var returnType = GetReturnTypeFromAnnotation(func);
        if (returnType is not null && (returnType.Flags & TypeFlags.Union) != 0)
        {
            returnType = FilterType(returnType, t => CheckGeneratorInstantiationAssignabilityToReturnType(t, functionFlags, /*errorNode*/ null));
        }
        var iterationTypes = returnType is not null ? GetIterationTypesOfGeneratorFunctionReturnType(returnType, isAsync) : null;
        var signatureYieldType = iterationTypes?.YieldType ?? _anyType;
        var signatureNextType = iterationTypes?.NextType ?? _anyType;
        var yieldExpressionType = node.Expression is not null ? CheckExpression(node.Expression) : _undefinedWideningType;
        var yieldedType = GetYieldedTypeOfYieldExpression(node, yieldExpressionType, signatureNextType, isAsync);
        if (returnType is not null && yieldedType is not null)
        {
            CheckTypeAssignableToAndOptionallyElaborate(yieldedType, signatureYieldType, node.Expression ?? node, node.Expression);
        }

        if (node.AsteriskToken is not null)
        {
            var use = isAsync ? IterationUse.AsyncYieldStar : IterationUse.YieldStar;
            return GetIterationTypeOfIterable(use, IterationTypeKind.Return, yieldExpressionType, node.Expression) ?? _anyType;
        }
        else if (returnType is not null)
        {
            return GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Next, returnType, isAsync) ?? _anyType;
        }
        var type = GetContextualIterationType(IterationTypeKind.Next, func);
        if (type is null)
        {
            type = _anyType;
            AddLazyDiagnostic(() =>
            {
                if (_noImplicitAny && !Utilities.ExpressionResultIsUnused(node))
                {
                    var contextualType = GetContextualType(node, /*contextFlags*/ null);
                    if (contextualType is null || IsTypeAny(contextualType))
                    {
                        Error(node, Diagnostics.yield_expression_implicitly_results_in_an_any_type_because_its_containing_generator_lacks_a_return_type_annotation);
                    }
                }
            });
        }
        return type;

        void CheckYieldExpressionGrammar()
        {
            if ((node.Flags & NodeFlags.YieldContext) == 0)
            {
                GrammarErrorOnFirstToken(node, Diagnostics.A_yield_expression_is_only_allowed_in_a_generator_body);
            }

            if (IsInParameterInitializerBeforeContainingFunction(node))
            {
                Error(node, Diagnostics.yield_expressions_cannot_be_used_in_a_parameter_initializer);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:41268-41274 — <c>checkConditionalExpression</c>.</summary>
    internal IType CheckConditionalExpression(ConditionalExpression node, CheckMode checkMode = CheckMode.Normal)
    {
        var type = CheckTruthinessExpression(node.Condition!, checkMode);
        CheckTestingKnownTruthyCallableOrAwaitableOrEnumMemberType(node.Condition!, type, node.WhenTrue);
        var type1 = CheckExpression(node.WhenTrue!, checkMode);
        var type2 = CheckExpression(node.WhenFalse!, checkMode);
        return GetUnionType([type1, type2], UnionReduction.Subtype);
    }

    /// <summary>ported from: src/compiler/checker.ts:41276-41280 — <c>isTemplateLiteralContext</c>.</summary>
    internal bool IsTemplateLiteralContext(Node node)
    {
        var parent = node.Parent!;
        return NodeTests.IsParenthesizedExpression(parent) && IsTemplateLiteralContext(parent)
            || NodeTests.IsElementAccessExpression(parent) && ReferenceEquals(((ElementAccessExpression)parent).ArgumentExpression, node);
    }

    /// <summary>ported from: src/compiler/checker.ts:41282-41301 — <c>checkTemplateExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>[node.head.text]</c> is a new array and <c>const types = []</c> an untyped one; the port
    /// states them as the two lists the calls need. <c>evaluate(node).value</c> is the evaluator slot (checker.ts:1553)
    /// and its truthiness is JS's — an empty string is falsy — which the port states as <c>Js.IsTruthy</c>.
    /// </remarks>
    internal IType CheckTemplateExpression(TemplateExpression node)
    {
        var texts = new List<string> { node.Head!.Text! };
        var types = new List<IType>();
        foreach (var span in node.TemplateSpans!)
        {
            var type = CheckExpression(span.Expression!);
            if (MaybeTypeOfKindConsideringBaseConstraint(type, TypeFlags.ESSymbolLike))
            {
                Error(span.Expression, Diagnostics.Implicit_conversion_of_a_symbol_to_a_string_will_fail_at_runtime_Consider_wrapping_this_expression_in_String);
            }
            texts.Add(((ITemplateLiteralLikeNode)span.Literal!).Text!);
            types.Add(IsTypeAssignableTo(type, _templateConstraintType) ? type : _stringType);
        }

        string? evaluated = null;
        if (node.Parent!.Kind != SyntaxKind.TaggedTemplateExpression)
        {
            var evaluatedValue = _evaluate(node).Value;
            if (Js.IsTruthy(evaluatedValue)) evaluated = evaluatedValue!.ToString();
        }
        if (evaluated is not null)
        {
            return GetFreshTypeOfLiteralType(GetStringLiteralType(evaluated));
        }
        if (IsConstContext(node) || IsTemplateLiteralContext(node) || SomeType(GetContextualType(node, /*contextFlags*/ null) ?? _unknownType, IsTemplateLiteralContextualType))
        {
            return GetTemplateLiteralType(texts, types);
        }
        return _stringType;
    }

    /// <summary>ported from: src/compiler/checker.ts:41303-41306 — <c>isTemplateLiteralContextualType</c>.</summary>
    internal bool IsTemplateLiteralContextualType(IType type)
        => (type.Flags & (TypeFlags.StringLiteral | TypeFlags.TemplateLiteral)) != 0
            || (type.Flags & TypeFlags.InstantiableNonPrimitive) != 0
                && MaybeTypeOfKind(GetBaseConstraintOfType(type) ?? _unknownType, TypeFlags.StringLike);

    /// <summary>ported from: src/compiler/checker.ts:41308-41313 — <c>getContextNode</c>.</summary>
    internal Expression GetContextNode(Expression node)
    {
        if (NodeTests.IsJsxAttributes(node) && !NodeTests.IsJsxSelfClosingElement(node.Parent!))
        {
            return (Expression)node.Parent!.Parent!; // Needs to be the root JsxElement, so it encompasses the attributes _and_ the children (which are essentially part of the attributes)
        }
        return node;
    }

    /// <summary>ported from: src/compiler/checker.ts:41315-41333 — <c>checkExpressionWithContextualType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>node: Expression</c> is <c>Node</c> here because the landed JSX slices pass an attributes node;
    /// the <c>checkMode | CheckMode.Contextual | (inferenceContext ? CheckMode.Inferential : 0)</c> flag expression is
    /// the reference's.
    /// </remarks>
    internal IType CheckExpressionWithContextualType(Node node, IType contextualType, IInferenceContext? inferenceContext, CheckMode checkMode)
    {
        var contextNode = GetContextNode((Expression)node);
        PushContextualType(contextNode, contextualType, /*isCache*/ false);
        PushInferenceContext(contextNode, inferenceContext);
        var type = CheckExpression(node, checkMode | CheckMode.Contextual | (inferenceContext is not null ? CheckMode.Inferential : 0));
        // In CheckMode.Inferential we collect intra-expression inference sites to process before fixing any type
        // parameters. This information is no longer needed after the call to checkExpression.
        if (inferenceContext is not null && inferenceContext.IntraExpressionInferenceSites is not null)
        {
            inferenceContext.IntraExpressionInferenceSites = null;
        }
        // We strip literal freshness when an appropriate contextual type is present such that contextually typed
        // literals always preserve their literal types (otherwise they might widen during type inference). An alternative
        // here would be to not mark contextually typed literals as fresh in the first place.
        var result = MaybeTypeOfKind(type, TypeFlags.Literal)
            && IsLiteralOfContextualType(type, InstantiateContextualType(contextualType, node, /*contextFlags*/ null))
            ? GetRegularTypeOfLiteralType(type) : type;
        PopInferenceContext();
        PopContextualType();
        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:41335-41353 — <c>checkExpressionCached</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>if (checkMode)</c> tests the optional enum whose zero is <c>CheckMode.Normal</c>; the port
    /// states it as <c>checkMode != CheckMode.Normal</c>. The three flow slots are the landed
    /// <c>_flowLoopStart</c>/<c>_flowLoopCount</c>/<c>_flowTypeCache</c>.
    /// </remarks>
    internal IType CheckExpressionCached(Node node, CheckMode checkMode = CheckMode.Normal)
    {
        if (checkMode != CheckMode.Normal)
        {
            return CheckExpression(node, checkMode);
        }
        var links = GetNodeLinks(node);
        if (links.ResolvedType is null)
        {
            // When computing a type that we're going to cache, we need to ignore any ongoing control flow
            // analysis because variables may have transient types in indeterminable states. Moving flowLoopStart
            // to the top of the stack ensures all transient types are computed from a known point.
            var saveFlowLoopStart = _flowLoopStart;
            var saveFlowTypeCache = _flowTypeCache;
            _flowLoopStart = _flowLoopCount;
            _flowTypeCache = null;
            links.ResolvedType = CheckExpression(node, checkMode);
            _flowTypeCache = saveFlowTypeCache;
            _flowLoopStart = saveFlowLoopStart;
        }
        return links.ResolvedType!;
    }

    /// <summary>ported from: src/compiler/checker.ts:41355-41360 — <c>isTypeAssertion</c>.</summary>
    internal bool IsTypeAssertion(Expression node)
    {
        node = Utilities.SkipParentheses(node, /*excludeJSDocTypeAssertions*/ true);
        return node.Kind == SyntaxKind.TypeAssertionExpression
            || node.Kind == SyntaxKind.AsExpression
            || FactoryUtilities.IsJSDocTypeAssertion(node);
    }

    /// <summary>ported from: src/compiler/checker.ts:41362-41386 — <c>checkDeclarationInitializer</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>HasExpressionInitializer</c> is a union of declaration node classes with no C# counterpart,
    /// taken as <c>Node</c> (§12.5), and its <c>checkMode || CheckMode.Normal</c> is a no-op here because
    /// <c>CheckMode.Normal</c> is zero.
    /// </remarks>
    internal IType CheckDeclarationInitializer(Node declaration, CheckMode checkMode, IType? contextualType = null)
    {
        var initializer = Utilities.GetEffectiveInitializer(declaration)!;
        if (Utilities.IsInJSFile(declaration))
        {
            var typeNode = Utilities.TryGetJSDocSatisfiesTypeNode(declaration);
            if (typeNode is not null)
            {
                return CheckSatisfiesExpressionWorker(initializer, typeNode, checkMode);
            }
        }
        var type = GetQuickTypeOfExpression(initializer) ?? (contextualType is not null
            ? CheckExpressionWithContextualType(initializer, contextualType, /*inferenceContext*/ null, checkMode)
            : CheckExpressionCached(initializer, checkMode));
        if (NodeTests.IsParameter(NodeTests.IsBindingElement(declaration) ? Utilities.WalkUpBindingElementsAndPatterns((BindingElement)declaration) : declaration))
        {
            var name = ((INamedDeclaration)declaration).Name!;
            if (name.Kind == SyntaxKind.ObjectBindingPattern && IsObjectLiteralType(type))
            {
                return PadObjectLiteralType((IObjectType)type, (ObjectBindingPattern)name);
            }
            if (name.Kind == SyntaxKind.ArrayBindingPattern && IsTupleType(type))
            {
                return PadTupleType((ITupleTypeReference)type, (ArrayBindingPattern)name);
            }
        }
        return type;
    }

    // ---------------------------------------------------------------------------------------------
    // Padding and widening for declaration initializers (checker.ts:41388-41485)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:41388-41413 — <c>padObjectLiteralType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>createSymbol(…)…links.type</c> is the port's <c>GetSymbolLinks(symbol).Type</c>, and the
    /// reference's <c>append(missingElements, e)</c> is <c>Core.Append</c>, whose answer is the covariant
    /// <c>IList&lt;T&gt;</c> the port casts back to the list the loop keeps.
    /// </remarks>
    internal IType PadObjectLiteralType(IObjectType type, ObjectBindingPattern pattern)
    {
        List<BindingElement>? missingElements = null;
        foreach (var e in pattern.Elements!)
        {
            if (e.Initializer is not null)
            {
                var name = GetPropertyNameFromBindingElement(e);
                if (name is not null && GetPropertyOfType(type, name) is null)
                {
                    missingElements = Core.Append(missingElements, e) as List<BindingElement>;
                }
            }
        }
        if (missingElements is null)
        {
            return type;
        }
        var members = Utilities.CreateSymbolTable();
        foreach (var prop in GetPropertiesOfObjectType(type))
        {
            members[prop.EscapedName!] = prop;
        }
        foreach (var e in missingElements)
        {
            var symbol = CreateSymbol(SymbolFlags.Property | SymbolFlags.Optional, GetPropertyNameFromBindingElement(e)!);
            GetSymbolLinks(symbol).Type = GetTypeFromBindingElement(e, /*includePatternInType*/ false, /*reportErrors*/ false);
            members[symbol.EscapedName!] = symbol;
        }
        var result = CreateAnonymousType(type.Symbol, members, [], [], GetIndexInfosOfType(type));
        result.ObjectFlags = type.ObjectFlags;
        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:41415-41418 — <c>getPropertyNameFromBindingElement</c>.</summary>
    internal string? GetPropertyNameFromBindingElement(BindingElement e)
    {
        var exprType = GetLiteralTypeFromPropertyName(e.PropertyName ?? e.Name!);
        return Utilities.IsTypeUsableAsPropertyName(exprType) ? Utilities.GetPropertyNameFromType(exprType) : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:41420-41438 — <c>padTupleType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads <c>type.target.combinedFlags</c>/<c>elementFlags</c>/<c>readonly</c> through
    /// <c>TupleTypeReference.target: TupleType</c>; the port's <c>ITypeReference.Target</c> is the wider
    /// <c>IGenericType</c>, so the tuple face is cast once at the top. <c>getElementTypes(type).slice()</c> and
    /// <c>target.elementFlags.slice()</c> are the two copied lists the reference mutates.
    /// </remarks>
    internal IType PadTupleType(ITupleTypeReference type, ArrayBindingPattern pattern)
    {
        var target = (ITupleType)type.Target!;
        if ((target.CombinedFlags & ElementFlags.Variable) != 0 || GetTypeReferenceArity(type) >= pattern.Elements!.Count)
        {
            return type;
        }
        var patternElements = pattern.Elements!;
        var elementTypes = new List<IType>(GetElementTypes(type));
        var elementFlags = new List<ElementFlags>(target.ElementFlags!);
        for (var i = GetTypeReferenceArity(type); i < patternElements.Count; i++)
        {
            var e = patternElements[i];
            if (i < patternElements.Count - 1 || !(e.Kind == SyntaxKind.BindingElement && ((BindingElement)e).DotDotDotToken is not null))
            {
                elementTypes.Add(!NodeTests.IsOmittedExpression(e) && HasDefaultValue(e) ? GetTypeFromBindingElement((BindingElement)e, /*includePatternInType*/ false, /*reportErrors*/ false) : _anyType);
                elementFlags.Add(ElementFlags.Optional);
                if (!NodeTests.IsOmittedExpression(e) && !HasDefaultValue(e))
                {
                    ReportImplicitAny((IDeclaration)e, _anyType);
                }
            }
        }
        return CreateTupleType(elementTypes, elementFlags, target.Readonly);
    }

    /// <summary>ported from: src/compiler/checker.ts:41440-41453 — <c>widenTypeInferredFromInitializer</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>HasExpressionInitializer</c> union, taken as <c>Node</c>; the two
    /// <c>reportImplicitAny</c> calls take the declaration face the port's landed member declares.
    /// </remarks>
    internal IType WidenTypeInferredFromInitializer(Node declaration, IType type)
    {
        var widened = GetWidenedLiteralTypeForInitializer(declaration, type);
        if (Utilities.IsInJSFile(declaration))
        {
            if (IsEmptyLiteralType(widened))
            {
                ReportImplicitAny((IDeclaration)declaration, _anyType);
                return _anyType;
            }
            else if (IsEmptyArrayLiteralType(widened))
            {
                ReportImplicitAny((IDeclaration)declaration, _anyArrayType);
                return _anyArrayType;
            }
        }
        return widened;
    }

    /// <summary>ported from: src/compiler/checker.ts:41455-41457 — <c>getWidenedLiteralTypeForInitializer</c>.</summary>
    internal IType GetWidenedLiteralTypeForInitializer(Node declaration, IType type)
        => (GetCombinedNodeFlagsCached(declaration) & NodeFlags.Constant) != 0 || Utilities.IsDeclarationReadonly((IDeclaration)declaration)
            ? type
            : GetWidenedLiteralType(type);

    /// <summary>ported from: src/compiler/checker.ts:41459-41485 — <c>isLiteralOfContextualType</c>.</summary>
    internal bool IsLiteralOfContextualType(IType candidateType, IType? contextualType)
    {
        if (contextualType is not null)
        {
            if ((contextualType.Flags & TypeFlags.UnionOrIntersection) != 0)
            {
                var types = ((IUnionType)contextualType).Types!;
                return Core.Some(types, t => IsLiteralOfContextualType(candidateType, t));
            }
            if ((contextualType.Flags & TypeFlags.InstantiableNonPrimitive) != 0)
            {
                // If the contextual type is a type variable constrained to a primitive type, consider
                // this a literal context for literals of that primitive type. For example, given a
                // type parameter 'T extends string', infer string literal types for T.
                var constraint = GetBaseConstraintOfType(contextualType) ?? _unknownType;
                return MaybeTypeOfKind(constraint, TypeFlags.String) && MaybeTypeOfKind(candidateType, TypeFlags.StringLiteral)
                    || MaybeTypeOfKind(constraint, TypeFlags.Number) && MaybeTypeOfKind(candidateType, TypeFlags.NumberLiteral)
                    || MaybeTypeOfKind(constraint, TypeFlags.BigInt) && MaybeTypeOfKind(candidateType, TypeFlags.BigIntLiteral)
                    || MaybeTypeOfKind(constraint, TypeFlags.ESSymbol) && MaybeTypeOfKind(candidateType, TypeFlags.UniqueESSymbol)
                    || IsLiteralOfContextualType(candidateType, constraint);
            }
            // If the contextual type is a literal of a particular primitive type, we consider this a
            // literal context for all literals of that primitive type.
            return (contextualType.Flags & (TypeFlags.StringLiteral | TypeFlags.Index | TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) != 0 && MaybeTypeOfKind(candidateType, TypeFlags.StringLiteral)
                || (contextualType.Flags & TypeFlags.NumberLiteral) != 0 && MaybeTypeOfKind(candidateType, TypeFlags.NumberLiteral)
                || (contextualType.Flags & TypeFlags.BigIntLiteral) != 0 && MaybeTypeOfKind(candidateType, TypeFlags.BigIntLiteral)
                || (contextualType.Flags & TypeFlags.BooleanLiteral) != 0 && MaybeTypeOfKind(candidateType, TypeFlags.BooleanLiteral)
                || (contextualType.Flags & TypeFlags.UniqueESSymbol) != 0 && MaybeTypeOfKind(candidateType, TypeFlags.UniqueESSymbol);
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:41487-41494 — <c>isConstContext</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>parent.type</c> over the <c>AssertionExpression</c> union is written as the kind switch the
    /// port's inference block uses for that same read (there is no common base class for the three assertion classes),
    /// and the two <c>isConstContext(parent)</c> recursions take <c>Node</c> because the reference passes
    /// <c>parent</c>/<c>parent.parent</c>, which its <c>Expression</c> parameter accepts only through the narrowing its
    /// type guards perform.
    /// </remarks>
    internal bool IsConstContext(Node node)
    {
        var parent = node.Parent!;
        var assertionType = parent switch
        {
            TypeAssertion assertion => assertion.Type,
            AsExpression assertion => assertion.Type,
            _ => null,
        };
        return Utilities.IsAssertionExpression(parent) && Utilities.IsConstTypeReference((Node)assertionType!)
            || FactoryUtilities.IsJSDocTypeAssertion(parent) && Utilities.IsConstTypeReference((Node)FactoryUtilities.GetJSDocTypeAssertionType(parent)!)
            || IsValidConstAssertionArgument(node) && IsConstTypeVariable(GetContextualType((Expression)node, ContextFlags.None))
            || (NodeTests.IsParenthesizedExpression(parent) || NodeTests.IsArrayLiteralExpression(parent) || NodeTests.IsSpreadElement(parent)) && IsConstContext(parent)
            || (NodeTests.IsPropertyAssignment(parent) || NodeTests.IsShorthandPropertyAssignment(parent) || NodeTests.IsTemplateSpan(parent)) && IsConstContext(parent.Parent!);
    }

    /// <summary>ported from: src/compiler/checker.ts:41496-41501 — <c>checkExpressionForMutableLocation</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>Expression</c> and its <c>checkMode: CheckMode | undefined</c> is required; the
    /// port takes <c>Node</c> because a landed caller passes a shorthand assignment's name, and the mode as the
    /// non-nullable enum whose zero is the reference's <c>undefined</c>.
    /// </remarks>
    internal IType CheckExpressionForMutableLocation(Node node, CheckMode checkMode, bool forceTuple = false)
    {
        var type = CheckExpression(node, checkMode, forceTuple);
        return IsConstContext(node) || Utilities.IsCommonJsExportedExpression(node) ? GetRegularTypeOfLiteralType(type)
            : IsTypeAssertion((Expression)node) ? type
            : GetWidenedLiteralLikeTypeForContextualType(type, InstantiateContextualType(GetContextualType((Expression)node, /*contextFlags*/ null), node, /*contextFlags*/ null));
    }

    /// <summary>ported from: src/compiler/checker.ts:41503-41512 — <c>checkPropertyAssignment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>PropertyAssignment</c>; the port takes <c>Node</c> because the landed caller
    /// passes the declaration it holds as a <c>Node</c>, and reads the name and initializer through those two faces.
    /// </remarks>
    internal IType CheckPropertyAssignment(Node node, CheckMode checkMode = CheckMode.Normal)
    {
        // Do not use hasDynamicName here, because that returns false for well known symbols.
        // We want to perform checkComputedPropertyName for all computed properties, including
        // well known symbols.
        var name = ((INamedDeclaration)node).Name!;
        if (name.Kind == SyntaxKind.ComputedPropertyName)
        {
            CheckComputedPropertyName((ComputedPropertyName)name);
        }

        return CheckExpressionForMutableLocation(((PropertyAssignment)node).Initializer!, checkMode);
    }

    /// <summary>ported from: src/compiler/checker.ts:41514-41527 — <c>checkObjectLiteralMethod</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is <c>MethodDeclaration</c>; the port takes <c>Node</c> for the same reason <see cref="CheckPropertyAssignment"/> does.</remarks>
    internal IType CheckObjectLiteralMethod(Node node, CheckMode checkMode = CheckMode.Normal)
    {
        // Grammar checking
        CheckGrammarMethod(node);

        // Do not use hasDynamicName here, because that returns false for well known symbols.
        // We want to perform checkComputedPropertyName for all computed properties, including
        // well known symbols.
        var name = ((INamedDeclaration)node).Name!;
        if (name.Kind == SyntaxKind.ComputedPropertyName)
        {
            CheckComputedPropertyName((ComputedPropertyName)name);
        }

        var uninstantiatedType = CheckFunctionExpressionOrObjectLiteralMethod(node, checkMode);
        return InstantiateTypeWithSingleGenericCallSignature(node, uninstantiatedType, checkMode);
    }

    /// <summary>ported from: src/compiler/checker.ts:41529-41585 — <c>instantiateTypeWithSingleGenericCallSignature</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter union (<c>Expression | MethodDeclaration | QualifiedName</c>) is <c>Node</c> here, the
    /// upper bound the port's model gives a node union. The two <c>getInferenceContext(node)!</c> assertions are the
    /// reference's own: reaching them means the expression is an argument of a generic call being inferred, so the
    /// context exists. <c>context.inferredTypeParameters = concatenate(…)</c> is the landed
    /// <c>Core.Concatenate(…) as List&lt;ITypeParameter&gt;</c> assignment shape, and
    /// <c>getSignatureInstantiationWithoutFillingInTypeArguments</c> — whose port declares <c>List&lt;IType&gt;</c> while the
    /// reference passes the <c>TypeParameter[]</c> it just built — is called with a spread of that list
    /// (<c>[.. uniqueTypeParameters]</c>), the same contents in the list type the declaration needs.
    /// </remarks>
    internal IType InstantiateTypeWithSingleGenericCallSignature(Node node, IType type, CheckMode checkMode = CheckMode.Normal)
    {
        if ((checkMode & (CheckMode.Inferential | CheckMode.SkipGenericFunctions)) != 0)
        {
            var callSignature = GetSingleSignature(type, SignatureKind.Call, /*allowMembers*/ true);
            var constructSignature = GetSingleSignature(type, SignatureKind.Construct, /*allowMembers*/ true);
            var signature = callSignature ?? constructSignature;
            if (signature is not null && signature.TypeParameters is not null)
            {
                var contextualType = GetApparentTypeOfContextualType((Expression)node, ContextFlags.NoConstraints);
                if (contextualType is not null)
                {
                    var contextualSignature = GetSingleSignature(GetNonNullableType(contextualType), callSignature is not null ? SignatureKind.Call : SignatureKind.Construct, /*allowMembers*/ false);
                    if (contextualSignature is not null && contextualSignature.TypeParameters is null)
                    {
                        if ((checkMode & CheckMode.SkipGenericFunctions) != 0)
                        {
                            SkippedGenericFunction(node, checkMode);
                            return _anyFunctionType;
                        }
                        var context = GetInferenceContext(node)!;
                        // We have an expression that is an argument of a generic function for which we are performing
                        // type argument inference. The expression is of a function type with a single generic call
                        // signature and a contextual function type with a single non-generic call signature. Now check
                        // if the outer function returns a function type with a single non-generic call signature and
                        // if some of the outer function type parameters have no inferences so far. If so, we can
                        // potentially add inferred type parameters to the outer function return type.
                        var returnType = context.Signature is not null ? GetReturnTypeOfSignature(context.Signature) : null;
                        var returnSignature = returnType is not null ? GetSingleCallOrConstructSignature(returnType) : null;
                        if (returnSignature is not null && returnSignature.TypeParameters is null && !Core.Every(context.Inferences!, (info, _) => HasInferenceCandidates(info)))
                        {
                            // Instantiate the signature with its own type parameters as type arguments, possibly
                            // renaming the type parameters to ensure they have unique names.
                            var uniqueTypeParameters = GetUniqueTypeParameters(context, signature.TypeParameters);
                            var instantiatedSignature = GetSignatureInstantiationWithoutFillingInTypeArguments(signature, [.. uniqueTypeParameters]);
                            // Infer from the parameters of the instantiated signature to the parameters of the
                            // contextual signature starting with an empty set of inference candidates.
                            var inferences = Core.Map<IInferenceInfo, IInferenceInfo>(context.Inferences!, (info, _) => CreateInferenceInfo(info.TypeParameter!))!;
                            ApplyToParameterTypes(instantiatedSignature, contextualSignature, (source, target) =>
                            {
                                InferTypes(inferences, source, target, /*priority*/ InferencePriority.None, /*contravariant*/ true);
                            });
                            if (Core.Some(inferences, HasInferenceCandidates))
                            {
                                // We have inference candidates, indicating that one or more type parameters are referenced
                                // in the parameter types of the contextual signature. Now also infer from the return type.
                                ApplyToReturnTypes(instantiatedSignature, contextualSignature, (source, target) =>
                                {
                                    InferTypes(inferences, source, target);
                                });
                                // If the type parameters for which we produced candidates do not have any inferences yet,
                                // we adopt the new inference candidates and add the type parameters of the expression type
                                // to the set of inferred type parameters for the outer function return type.
                                if (!HasOverlappingInferences(context.Inferences!, inferences))
                                {
                                    MergeInferences(context.Inferences!, inferences);
                                    context.InferredTypeParameters = Core.Concatenate(context.InferredTypeParameters, uniqueTypeParameters) as List<ITypeParameter>;
                                    return GetOrCreateTypeFromSignature(instantiatedSignature);
                                }
                            }
                        }
                        return GetOrCreateTypeFromSignature(InstantiateSignatureInContextOf(signature, contextualSignature, context));
                    }
                }
            }
        }
        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:41587-41594 — <c>skippedGenericFunction</c>.</summary>
    internal void SkippedGenericFunction(Node node, CheckMode checkMode)
    {
        if ((checkMode & CheckMode.Inferential) != 0)
        {
            // We have skipped a generic function during inferential typing. Obtain the inference context and
            // indicate this has occurred such that we know a second pass of inference is be needed.
            var context = GetInferenceContext(node)!;
            context.Flags |= InferenceFlags.SkippedGenericFunction;
        }
    }
}
