namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:37810-38055 (checkCallExpression … isCommonJsRequire)
// ported from: src/compiler/checker.ts:38057-38335 (checkTaggedTemplateExpression … checkImportMetaProperty)
// ported from: src/compiler/checker.ts:38337-38482 (getTypeOfParameter … isValidDeclarationForTupleLabel)
// ported from: src/compiler/checker.ts:38484-38638 (getNameableDeclarationAtPosition … getTypeOfFirstParameterOfSignatureWithFallback)
// ported from: src/compiler/checker.ts:38640-38753 (inferFromAnnotatedParametersAndReturn … assignBindingElementTypes)
// ported from: src/compiler/checker.ts:38755-38834 (the decorator-context type factories, createESDecoratorCallSignature)
// ported from: src/compiler/checker.ts:38842-39131 (getESDecoratorCallSignature … getDecoratorCallSignature)
// ported from: src/compiler/checker.ts:39133-39297 (createPromiseType … getReturnTypeFromBody)
// ported from: src/compiler/checker.ts:39299-39600 (createGeneratorType … checkAllCodePathsInNonVoidFunctionReturnOrThrow)
// ported from: src/compiler/checker.ts:54252-54254 (signatureHasRestParameter)
/// <summary>
/// The call, assertion, meta-property, JS-decorator and promise/generator check block, together with the
/// signature-parameter helpers it rests on: what a call resolves to, how a parameter's type is named and assigned,
/// and what type a decorator is called with.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS's <c>AssertionExpression</c>, <c>CallLikeExpression</c>, <c>ExpressionWithTypeArguments | TypeQueryNode</c>,
/// <c>MethodDeclaration | AccessorDeclaration | PropertyDeclaration</c> and <c>FunctionLikeDeclaration | MethodSignature</c>
/// are unions of concrete node types with no port counterpart; the generated model widened each to a single node class, the
/// port takes <see cref="Node"/> for those parameters and narrows inside on <c>Kind</c>, exactly as the widened model
/// forces. Where the reference names one concrete node type the port names that class (<see cref="CallExpression"/>,
/// <see cref="MetaProperty"/>, <see cref="Decorator"/>, <see cref="SwitchStatement"/>, <see cref="YieldExpression"/>, …).
/// </para>
/// <para>
/// PORT-NOTE: the reference narrows a node by its kind and then casts the *union* member
/// (<c>(node as Decorator | CallExpression | NewExpression).expression</c>, <c>getDeprecatedSuggestionNode</c>); C# casts
/// are nominal and reading <c>CallExpression.Expression</c> off a <c>Decorator</c> instance would read the wrong slot, so
/// each <c>case</c> casts to the class of its own kind.
/// </para>
/// <para>
/// PORT-NOTE: <c>getReturnTypeFromBody</c>'s parameter is <see cref="INode"/> rather than the reference's
/// <c>FunctionLikeDeclaration</c>: one landed caller passes <c>IFunctionLikeDeclarationBase</c>
/// (<c>Checker.Queries.cs:341</c>) and another passes <c>Node</c> (<c>Checker.SymbolSupport.cs:1482</c>), and <see cref="INode"/>
/// is the only type both convert to. The body narrows through <c>IFunctionLikeDeclarationBase</c> immediately.
/// </para>
/// <para>
/// PORT-NOTE: <c>links.isExhaustive</c> is a <c>boolean | 0 | undefined</c> tri-state in the reference, where <c>0</c> means
/// "resolution in process"; the port's slot is <c>object?</c>, so the tests are a boxed-<c>int</c> test and the final read is a
/// type test rather than the reference's truthiness conversion.
/// </para>
/// <para>
/// PORT-NOTE: <c>createDiagnosticForNodeArray</c> takes <c>NodeArray&lt;Node&gt;</c> in the port while the reference hands it the
/// call's <c>NodeArray&lt;TypeNode&gt;</c>; C# generics are invariant, so the argument is materialized as a
/// <c>NodeArray&lt;Node&gt;</c> that keeps the reference's range.
/// </para>
/// <para>
/// PORT-NOTE: <c>LanguageFeatureMinimumTarget</c> (types.ts:8488) has no port counterpart (see
/// <c>Checker.Check.Classes.cs</c>); the one comparison this block needs is <c>TaggedTemplates</c>, which the table fixes at
/// <c>ScriptTarget.ES2015</c>, and the site says so.
/// </para>
/// <para>
/// PORT-NOTE: <c>SourceFile.ImpliedNodeFormat</c> is <c>object?</c> in the port (the boxed <c>ResolutionMode</c>), so the
/// reference's <c>!==</c> against <c>ModuleKind.ESNext</c> is an <c>Equals</c> test.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // Calls and the deprecated-signature check (checker.ts:37810-37932)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:37810-37880 — <c>checkCallExpression</c>.</summary>
    internal IType CheckCallExpression(Node node, CheckMode? checkMode = null)
    {
        var typeArguments = node is NewExpression newExpression ? newExpression.TypeArguments : ((CallExpression)node).TypeArguments;
        CheckGrammarTypeArguments(node, typeArguments);

        var signature = GetResolvedSignature(node, /*candidatesOutArray*/ null, checkMode);
        if (ReferenceEquals(signature, _resolvingSignature))
        {
            // CheckMode.SkipGenericFunctions is enabled and this is a call to a generic function that
            // returns a function type. We defer checking and return silentNeverType.
            return _silentNeverType;
        }

        CheckDeprecatedSignature(signature, node);

        var expression = node is NewExpression newExpression2 ? newExpression2.Expression : ((CallExpression)node).Expression;
        if (expression!.Kind == SyntaxKind.SuperKeyword)
        {
            return _voidType;
        }

        if (node.Kind == SyntaxKind.NewExpression)
        {
            var declaration = signature.Declaration;

            if (
                declaration is not null &&
                declaration.Kind != SyntaxKind.Constructor &&
                declaration.Kind != SyntaxKind.ConstructSignature &&
                declaration.Kind != SyntaxKind.ConstructorType &&
                !(NodeTests.IsJSDocSignature(declaration) && Utilities.GetJSDocRoot(declaration)?.Parent?.Kind == SyntaxKind.Constructor) &&
                !Utilities.IsJSDocConstructSignature(declaration) &&
                !IsJSConstructor(declaration)
            )
            {
                // When resolved signature is a call signature (and not a construct signature) the result type is any
                if (_noImplicitAny)
                {
                    Error(node, Diagnostics.new_expression_whose_target_lacks_a_construct_signature_implicitly_has_an_any_type);
                }
                return _anyType;
            }
        }

        // In JavaScript files, calls to any identifier 'require' are treated as external module imports
        if (Utilities.IsInJSFile(node) && IsCommonJsRequire(node))
        {
            return ResolveExternalModuleTypeByLiteral((StringLiteral)((CallExpression)node).Arguments![0]);
        }

        var returnType = GetReturnTypeOfSignature(signature);
        // Treat any call to the global 'Symbol' function that is part of a const variable or readonly property
        // as a fresh unique symbol literal type.
        if ((returnType.Flags & TypeFlags.ESSymbolLike) != 0 && IsSymbolOrSymbolForCall(node))
        {
            return GetESSymbolLikeTypeForNode(Utilities.WalkUpParenthesizedExpressions(node.Parent!));
        }
        if (
            node.Kind == SyntaxKind.CallExpression && ((CallExpression)node).QuestionDotToken is null && node.Parent!.Kind == SyntaxKind.ExpressionStatement &&
            (returnType.Flags & TypeFlags.Void) != 0 && GetTypePredicateOfSignature(signature) is not null
        )
        {
            if (!Utilities.IsDottedName(expression!))
            {
                Error(expression!, Diagnostics.Assertions_require_the_call_target_to_be_an_identifier_or_qualified_name);
            }
            else if (GetEffectsSignature(node) is null)
            {
                var diagnostic = Error(expression!, Diagnostics.Assertions_require_every_name_in_the_call_target_to_be_declared_with_an_explicit_type_annotation);
                GetTypeOfDottedName(expression!, diagnostic);
            }
        }

        if (Utilities.IsInJSFile(node))
        {
            var jsSymbol = GetSymbolOfExpando(node, /*allowDeclaration*/ false);
            if (jsSymbol?.Exports is { Count: > 0 } exports)
            {
                var jsAssignmentType = CreateAnonymousType(jsSymbol, exports, [], [], []);
                jsAssignmentType.ObjectFlags |= ObjectFlags.JSLiteral;
                return GetIntersectionType([returnType, jsAssignmentType]);
            }
        }

        return returnType;
    }

    /// <summary>ported from: src/compiler/checker.ts:37882-37889 — <c>checkDeprecatedSignature</c>.</summary>
    internal void CheckDeprecatedSignature(ISignature signature, Node node)
    {
        if ((signature.Flags & SignatureFlags.IsSignatureCandidateForOverloadFailure) != 0) return;
        if (signature.Declaration is not null && (signature.Declaration.Flags & NodeFlags.Deprecated) != 0)
        {
            var suggestionNode = GetDeprecatedSuggestionNode(node);
            var name = Utilities.TryGetPropertyAccessOrIdentifierToString((Expression)Utilities.GetInvokedExpression(node));
            AddDeprecatedSuggestionWithSignature(suggestionNode, signature.Declaration, name, Diagnostic.DeferArg(() => SignatureToString(signature)));
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:37891-37913 — <c>getDeprecatedSuggestionNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference folds <c>CallExpression</c>, <c>Decorator</c> and <c>NewExpression</c> into one case that
    /// casts to the union and reads <c>.expression</c>; C# reads the slot from each node's own class.
    /// </remarks>
    internal Node GetDeprecatedSuggestionNode(Node node)
    {
        node = Utilities.SkipParentheses(node);
        switch (node.Kind)
        {
            case SyntaxKind.CallExpression:
                return GetDeprecatedSuggestionNode(((CallExpression)node).Expression!);
            case SyntaxKind.Decorator:
                return GetDeprecatedSuggestionNode(((Decorator)node).Expression!);
            case SyntaxKind.NewExpression:
                return GetDeprecatedSuggestionNode(((NewExpression)node).Expression!);
            case SyntaxKind.TaggedTemplateExpression:
                return GetDeprecatedSuggestionNode(((TaggedTemplateExpression)node).Tag!);
            case SyntaxKind.JsxOpeningElement:
                return GetDeprecatedSuggestionNode(((JsxOpeningElement)node).TagName!);
            case SyntaxKind.JsxSelfClosingElement:
                return GetDeprecatedSuggestionNode(((JsxSelfClosingElement)node).TagName!);
            case SyntaxKind.ElementAccessExpression:
                return ((ElementAccessExpression)node).ArgumentExpression!;
            case SyntaxKind.PropertyAccessExpression:
                return ((PropertyAccessExpression)node).Name!;
            case SyntaxKind.TypeReference:
                var typeReference = (TypeReferenceNode)node;
                return NodeTests.IsQualifiedName(typeReference.TypeName!) ? ((QualifiedName)typeReference.TypeName!).Right! : typeReference;
            default:
                return node;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:37915-37932 — <c>isSymbolOrSymbolForCall</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>resolveName</c>'s port takes the reference's <c>excludeGlobals</c> parameter as a required argument
    /// (<c>Checker.SymbolSupport.cs:97</c>), so the omitted argument is passed as <c>false</c>.
    /// </remarks>
    internal bool IsSymbolOrSymbolForCall(Node node)
    {
        if (!NodeTests.IsCallExpression(node)) return false;
        var call = (CallExpression)node;
        Expression left = call.Expression!;
        if (NodeTests.IsPropertyAccessExpression(left) && NodeTests.IsIdentifier(((PropertyAccessExpression)left).Name!) && ((Identifier)((PropertyAccessExpression)left).Name!).EscapedText == "for")
        {
            left = ((PropertyAccessExpression)left).Expression!;
        }
        if (!NodeTests.IsIdentifier(left) || ((Identifier)left).EscapedText != "Symbol")
        {
            return false;
        }

        // make sure `Symbol` is the global symbol
        var globalESSymbol = GetGlobalESSymbolConstructorSymbol(/*reportErrors*/ false);
        if (globalESSymbol is null)
        {
            return false;
        }

        return ReferenceEquals(globalESSymbol, ResolveName(left, "Symbol", SymbolFlags.Value, /*nameNotFoundMessage*/ null, /*isUse*/ false, /*excludeGlobals*/ false));
    }

    // ---------------------------------------------------------------------------------------------
    // Dynamic import and the synthetic-default wrappers (checker.ts:37934-38055)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:37934-37982 — <c>checkImportCallExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>ImportCall</c> is <c>CallExpression</c> (types.ts); the port's <c>IImportCall</c>
    /// interface declares no members, so the parameter is the class whose slots the body reads.
    /// </remarks>
    internal IType CheckImportCallExpression(CallExpression node)
    {
        // Check grammar of dynamic import
        CheckGrammarImportCallExpression(node);

        if (Core.Length(node.Arguments) == 0)
        {
            return CreatePromiseReturnType(node, _anyType);
        }

        var specifier = node.Arguments![0];
        var specifierType = CheckExpressionCached(specifier);
        var optionsType = node.Arguments.Count > 1 ? CheckExpressionCached(node.Arguments[1]) : null;
        // Even though multiple arguments is grammatically incorrect, type-check extra arguments for completion
        for (var i = 2; i < node.Arguments.Count; ++i)
        {
            CheckExpressionCached(node.Arguments[i]);
        }

        if ((specifierType.Flags & TypeFlags.Undefined) != 0 || (specifierType.Flags & TypeFlags.Null) != 0 || !IsTypeAssignableTo(specifierType, _stringType))
        {
            // PORT-NOTE (A″): each printer-gated stringifier passed as a diagnostic-message argument here is a memoised wrapper — see Core/Diagnostic.cs's `DiagnosticArgument`.
            Error(specifier, Diagnostics.Dynamic_import_s_specifier_must_be_of_type_string_but_here_has_type_0, Diagnostic.DeferArg(() => TypeToString(specifierType)));
        }

        if (optionsType is not null)
        {
            var importCallOptionsType = GetGlobalImportCallOptionsType(/*reportErrors*/ true);
            if (!ReferenceEquals(importCallOptionsType, _emptyObjectType))
            {
                CheckTypeAssignableTo(optionsType, GetNullableType(importCallOptionsType, TypeFlags.Undefined), node.Arguments[1]);
            }
            if (_compilerOptions.IgnoreDeprecations != "6.0" && NodeTests.IsObjectLiteralExpression(node.Arguments[1]))
            {
                foreach (var prop in ((ObjectLiteralExpression)node.Arguments[1]).Properties!)
                {
                    if (NodeTests.IsPropertyAssignment(prop) && NodeTests.IsIdentifier(((PropertyAssignment)prop).Name!) && ((Identifier)((PropertyAssignment)prop).Name!).EscapedText == "assert")
                    {
                        GrammarErrorOnNode(((PropertyAssignment)prop).Name!, Diagnostics.Import_assertions_have_been_replaced_by_import_attributes_Use_with_instead_of_assert);
                        break;
                    }
                }
            }
        }

        // resolveExternalModuleName will return undefined if the moduleReferenceExpression is not a string literal
        var moduleSymbol = ResolveExternalModuleName(node, specifier);
        if (moduleSymbol is not null)
        {
            var esModuleSymbol = ResolveESModuleSymbol(moduleSymbol, specifier, /*dontResolveAlias*/ true, /*suppressInteropError*/ false);
            if (esModuleSymbol is not null)
            {
                return CreatePromiseReturnType(
                    node,
                    GetTypeWithSyntheticDefaultOnly(GetTypeOfSymbol(esModuleSymbol), esModuleSymbol, moduleSymbol, specifier) ??
                        GetTypeWithSyntheticDefaultImportType(GetTypeOfSymbol(esModuleSymbol), esModuleSymbol, moduleSymbol, specifier));
            }
        }
        return CreatePromiseReturnType(node, _anyType);
    }

    /// <summary>ported from: src/compiler/checker.ts:37984-37992 — <c>createDefaultPropertyWrapperForModule</c>.</summary>
    internal ResolvedType CreateDefaultPropertyWrapperForModule(ISymbol symbol, ISymbol? originalSymbol, ISymbol? anonymousSymbol = null)
    {
        var memberTable = Utilities.CreateSymbolTable();
        var newSymbol = CreateSymbol(SymbolFlags.Alias, InternalSymbolName.Default.Value);
        newSymbol.Parent = originalSymbol;
        GetSymbolLinks(newSymbol).NameType = GetStringLiteralType("default");
        GetSymbolLinks(newSymbol).AliasTarget = ResolveSymbol(symbol);
        memberTable[InternalSymbolName.Default.Value] = newSymbol;
        return CreateAnonymousType(anonymousSymbol, memberTable, [], [], []);
    }

    /// <summary>ported from: src/compiler/checker.ts:37994-38005 — <c>getTypeWithSyntheticDefaultOnly</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference shadows its own <c>type</c> parameter with the wrapper it creates; C# has no shadowing, so
    /// the local carries the name of the slot it fills. The shape-cast to <c>SyntheticDefaultModuleType</c> is the
    /// reference's own unchecked assertion, and the port's slot lives on <c>ISyntheticDefaultModuleType</c>'s
    /// implementer (<c>Model/UnionOrIntersectionType.cs:38</c>).
    /// </remarks>
    internal IType? GetTypeWithSyntheticDefaultOnly(IType type, ISymbol symbol, ISymbol originalSymbol, Expression moduleSpecifier)
    {
        var hasDefaultOnly = IsOnlyImportableAsDefault(moduleSpecifier);
        // PORT-NOTE: the reference's `type &&` is a truthiness guard on a value TS types non-nullable too, and the
        // port's `IType` parameter cannot be null; the guard is dropped so that the nullable analysis sees the same.
        if (hasDefaultOnly && !IsErrorType(type))
        {
            var synthType = (ISyntheticDefaultModuleType)type;
            if (synthType.DefaultOnlyType is null)
            {
                var defaultOnlyType = CreateDefaultPropertyWrapperForModule(symbol, originalSymbol);
                synthType.DefaultOnlyType = defaultOnlyType;
            }
            return synthType.DefaultOnlyType;
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:38007-38026 — <c>getTypeWithSyntheticDefaultImportType</c>.</summary>
    internal IType GetTypeWithSyntheticDefaultImportType(IType type, ISymbol symbol, ISymbol originalSymbol, Expression moduleSpecifier)
    {
        if (_allowSyntheticDefaultImports && !IsErrorType(type))
        {
            var synthType = (ISyntheticDefaultModuleType)type;
            if (synthType.SyntheticType is null)
            {
                var file = Core.Find(originalSymbol.Declarations, (decl, _) => NodeTests.IsSourceFile((Node)decl));
                var hasSyntheticDefault = CanHaveSyntheticDefault((SourceFile?)file, originalSymbol, /*dontResolveAlias*/ false, moduleSpecifier);
                if (hasSyntheticDefault)
                {
                    var anonymousSymbol = CreateSymbol(SymbolFlags.TypeLiteral, InternalSymbolName.Type.Value);
                    var defaultContainingObject = CreateDefaultPropertyWrapperForModule(symbol, originalSymbol, anonymousSymbol);
                    GetSymbolLinks(anonymousSymbol).Type = defaultContainingObject;
                    synthType.SyntheticType = IsValidSpreadType(type) ? GetSpreadType(type, defaultContainingObject, anonymousSymbol, /*objectFlags*/ 0, /*readonly*/ false) : defaultContainingObject;
                }
                else
                {
                    synthType.SyntheticType = type;
                }
            }
            return synthType.SyntheticType!;
        }
        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:38028-38055 — <c>isCommonJsRequire</c>.</summary>
    internal bool IsCommonJsRequire(Node node)
    {
        if (!Utilities.IsRequireCall(node, /*requireStringLiteralLikeArgument*/ true))
        {
            return false;
        }

        // Make sure require is not a local function
        var call = (CallExpression)node;
        if (call.Expression is null || !NodeTests.IsIdentifier(call.Expression)) return Debug.Fail<bool>("isRequireCall guarantees an identifier callee");
        var requireIdentifier = (Identifier)call.Expression!;
        var resolvedRequire = ResolveName(requireIdentifier, requireIdentifier.EscapedText!, SymbolFlags.Value, /*nameNotFoundMessage*/ null, /*isUse*/ true, /*excludeGlobals*/ false)!;
        if (ReferenceEquals(resolvedRequire, _requireSymbol))
        {
            return true;
        }
        // project includes symbol named 'require' - make sure that it is ambient and local non-alias
        if ((resolvedRequire.Flags & SymbolFlags.Alias) != 0)
        {
            return false;
        }

        var targetDeclarationKind = (resolvedRequire.Flags & SymbolFlags.Function) != 0
            ? SyntaxKind.FunctionDeclaration
            : (resolvedRequire.Flags & SymbolFlags.Variable) != 0
            ? SyntaxKind.VariableDeclaration
            : SyntaxKind.Unknown;
        if (targetDeclarationKind != SyntaxKind.Unknown)
        {
            var decl = Utilities.GetDeclarationOfKind(resolvedRequire, targetDeclarationKind)!;
            // function/variable declaration should be ambient
            return decl is not null && (decl.Flags & NodeFlags.Ambient) != 0;
        }
        return false;
    }

    // ---------------------------------------------------------------------------------------------
    // Tagged templates, assertions and non-null chains (checker.ts:38057-38170)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:38057-38065 — <c>checkTaggedTemplateExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>languageVersion &lt; LanguageFeatureMinimumTarget.TaggedTemplates</c> is written against the target that
    /// entry holds (<c>ScriptTarget.ES2015</c>) because the table itself has no port counterpart.
    /// </remarks>
    internal IType CheckTaggedTemplateExpression(TaggedTemplateExpression node)
    {
        if (!CheckGrammarTaggedTemplateChain(node)) CheckGrammarTypeArguments(node, node.TypeArguments);
        if (_languageVersion < ScriptTarget.ES2015)
        {
            CheckExternalEmitHelpers(node, ExternalEmitHelpers.MakeTemplateObject);
        }
        var signature = GetResolvedSignature(node);
        CheckDeprecatedSignature(signature, node);
        return GetReturnTypeOfSignature(signature);
    }

    /// <summary>ported from: src/compiler/checker.ts:38067-38080 — <c>checkAssertion</c>.</summary>
    internal IType CheckAssertion(Node node, CheckMode? checkMode)
    {
        if (node.Kind == SyntaxKind.TypeAssertionExpression)
        {
            var file = Utilities.GetSourceFileOfNode(node);
            if (file is not null && Paths.FileExtensionIsOneOf(file.FileName!, [Extension.Cts.Value, Extension.Mts.Value]))
            {
                GrammarErrorOnNode(node, Diagnostics.This_syntax_is_reserved_in_files_with_the_mts_or_cts_extension_Use_an_as_expression_instead);
            }
            if (Js.IsTruthy(_compilerOptions.ErasableSyntaxOnly))
            {
                var start = ScannerUtilities.SkipTrivia(file!.Text!, node.Pos);
                var end = ((TypeAssertion)node).Expression!.Pos;
                _diagnostics.Add(Utilities.CreateFileDiagnostic(file, start, end - start, Diagnostics.This_syntax_is_not_allowed_when_erasableSyntaxOnly_is_enabled));
            }
        }
        return CheckAssertionWorker(node, checkMode);
    }

    /// <summary>ported from: src/compiler/checker.ts:38082-38108 — <c>isValidConstAssertionArgument</c>.</summary>
    internal bool IsValidConstAssertionArgument(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.StringLiteral:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
            case SyntaxKind.TrueKeyword:
            case SyntaxKind.FalseKeyword:
            case SyntaxKind.ArrayLiteralExpression:
            case SyntaxKind.ObjectLiteralExpression:
            case SyntaxKind.TemplateExpression:
                return true;
            case SyntaxKind.ParenthesizedExpression:
                return IsValidConstAssertionArgument(((ParenthesizedExpression)node).Expression!);
            case SyntaxKind.PrefixUnaryExpression:
                var op = ((PrefixUnaryExpression)node).Operator;
                var arg = ((PrefixUnaryExpression)node).Operand!;
                return op == SyntaxKind.MinusToken && (arg.Kind == SyntaxKind.NumericLiteral || arg.Kind == SyntaxKind.BigIntLiteral) ||
                    op == SyntaxKind.PlusToken && arg.Kind == SyntaxKind.NumericLiteral;
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ElementAccessExpression:
                var expr = Utilities.SkipParentheses(node is PropertyAccessExpression propertyAccess ? propertyAccess.Expression! : ((ElementAccessExpression)node).Expression!);
                var symbol = Utilities.IsEntityNameExpression(expr) ? ResolveEntityName(expr, SymbolFlags.Value, /*ignoreErrors*/ true) : null;
                return symbol is not null && (symbol.Flags & SymbolFlags.Enum) != 0;
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:38110-38124 — <c>checkAssertionWorker</c>.</summary>
    internal IType CheckAssertionWorker(Node node, CheckMode? checkMode)
    {
        var (type, expression) = GetAssertionTypeAndExpression(node);
        var exprType = CheckExpression(expression!, checkMode ?? CheckMode.Normal);
        if (Utilities.IsConstTypeReference((Node)type!))
        {
            if (!IsValidConstAssertionArgument(expression!))
            {
                Error(expression!, Diagnostics.A_const_assertion_can_only_be_applied_to_references_to_enum_members_or_string_number_boolean_array_or_object_literals);
            }
            return GetRegularTypeOfLiteralType(exprType);
        }
        var links = GetNodeLinks(node);
        links.AssertionExpressionType = exprType;
        CheckSourceElement((Node)type!);
        CheckNodeDeferred(node);
        return GetTypeFromTypeNode(type);
    }

    /// <summary>ported from: src/compiler/checker.ts:38126-38142 — <c>getAssertionTypeAndExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference returns the anonymous object <c>{ type, expression }</c>; the port returns the same pair as a
    /// tuple, and both members start null for the kinds the <c>switch</c> does not list, as the reference's
    /// <c>let</c> declarations do.
    /// </remarks>
    internal (ITypeNode? Type, Expression? Expression) GetAssertionTypeAndExpression(Node node)
    {
        ITypeNode? type = null;
        Expression? expression = null;
        switch (node.Kind)
        {
            case SyntaxKind.AsExpression:
                type = ((AsExpression)node).Type;
                expression = ((AsExpression)node).Expression;
                break;
            case SyntaxKind.TypeAssertionExpression:
                type = ((TypeAssertion)node).Type;
                expression = ((TypeAssertion)node).Expression;
                break;
            case SyntaxKind.ParenthesizedExpression:
                type = FactoryUtilities.GetJSDocTypeAssertionType(node);
                expression = ((ParenthesizedExpression)node).Expression;
                break;
        }

        return (type, expression);
    }

    /// <summary>ported from: src/compiler/checker.ts:38144-38159 — <c>checkAssertionDeferred</c>.</summary>
    internal void CheckAssertionDeferred(Node node)
    {
        var (type, _) = GetAssertionTypeAndExpression(node);
        var errNode = node.Kind == SyntaxKind.ParenthesizedExpression ? (Node)type! : node;
        var links = GetNodeLinks(node);
        Debug.AssertIsDefined(links.AssertionExpressionType);
        var exprType = GetRegularTypeOfObjectLiteral(GetBaseTypeOfLiteralType(links.AssertionExpressionType!));
        var targetType = GetTypeFromTypeNode(type);
        if (!IsErrorType(targetType))
        {
            AddLazyDiagnostic(() =>
            {
                var widenedType = GetWidenedType(exprType);
                if (!IsTypeComparableTo(targetType, widenedType))
                {
                    CheckTypeComparableTo(exprType, targetType, errNode, Diagnostics.Conversion_of_type_0_to_type_1_may_be_a_mistake_because_neither_type_sufficiently_overlaps_with_the_other_If_this_was_intentional_convert_the_expression_to_unknown_first);
                }
            });
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:38161-38165 — <c>checkNonNullChain</c>.</summary>
    internal IType CheckNonNullChain(NonNullExpression node)
    {
        var leftType = CheckExpression(node.Expression!);
        var nonOptionalType = GetOptionalExpressionType(leftType, node.Expression!);
        return PropagateOptionalTypeMarker(GetNonNullableType(nonOptionalType), node, !ReferenceEquals(nonOptionalType, leftType));
    }

    /// <summary>ported from: src/compiler/checker.ts:38167-38170 — <c>checkNonNullAssertion</c>.</summary>
    internal IType CheckNonNullAssertion(NonNullExpression node)
        => (node.Flags & NodeFlags.OptionalChain) != 0
            ? CheckNonNullChain(node)
            : GetNonNullableType(CheckExpression(node.Expression!));

    // ---------------------------------------------------------------------------------------------
    // Instantiation and satisfies expressions (checker.ts:38172-38275)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:38172-38185 — <c>checkExpressionWithTypeArguments</c>.</summary>
    internal IType CheckExpressionWithTypeArguments(Node node)
    {
        CheckGrammarExpressionWithTypeArguments(node);
        var typeArguments = node.Kind == SyntaxKind.ExpressionWithTypeArguments ? ((ExpressionWithTypeArguments)node).TypeArguments : ((TypeQueryNode)node).TypeArguments;
        Core.ForEach(typeArguments, (ITypeNode typeArgument, int _) =>
        {
            CheckSourceElement(typeArgument);
            return (object?)null;
        });
        if (node.Kind == SyntaxKind.ExpressionWithTypeArguments)
        {
            var parent = Utilities.WalkUpParenthesizedExpressions(node.Parent!);
            if (parent.Kind == SyntaxKind.BinaryExpression && ((BinaryExpression)parent).OperatorToken!.Kind == SyntaxKind.InstanceOfKeyword && Utilities.IsNodeDescendantOf(node, ((BinaryExpression)parent).Right!))
            {
                Error(node, Diagnostics.The_right_hand_side_of_an_instanceof_expression_must_not_be_an_instantiation_expression);
            }
        }
        var exprType = node.Kind == SyntaxKind.ExpressionWithTypeArguments ? CheckExpression(((ExpressionWithTypeArguments)node).Expression!)
            : Utilities.IsThisIdentifier(((TypeQueryNode)node).ExprName) ? CheckThisExpression(((TypeQueryNode)node).ExprName!)
            : CheckExpression(((TypeQueryNode)node).ExprName!);
        return GetInstantiationExpressionType(exprType, node);
    }

    /// <summary>ported from: src/compiler/checker.ts:38187-38259 — <c>getInstantiationExpressionType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>node.typeArguments</c> is read through <c>INodeWithTypeArguments</c>, the port's face for the slot the
    /// reference's <c>NodeWithTypeArguments</c> declares.
    /// </remarks>
    internal IType GetInstantiationExpressionType(IType exprType, Node node)
    {
        var typeArguments = ((INodeWithTypeArguments)node).TypeArguments;
        if (ReferenceEquals(exprType, _silentNeverType) || IsErrorType(exprType) || !Core.Some(typeArguments))
        {
            return exprType;
        }
        var links = GetNodeLinks(node);
        if (links.InstantiationExpressionTypes is null)
        {
            links.InstantiationExpressionTypes = [];
        }
        if (links.InstantiationExpressionTypes.ContainsKey(exprType.Id))
        {
            return links.InstantiationExpressionTypes[exprType.Id]!;
        }
        var hasSomeApplicableSignature = false;
        IType? nonApplicableType = null;
        var result = GetInstantiatedType(exprType);
        links.InstantiationExpressionTypes[exprType.Id] = result;
        var errorType = hasSomeApplicableSignature ? nonApplicableType : exprType;
        if (errorType is not null)
        {
            _diagnostics.Add(Utilities.CreateDiagnosticForNodeArray(Utilities.GetSourceFileOfNode(node)!, ToNodeArray(typeArguments!), Diagnostics.Type_0_has_no_signatures_for_which_the_type_argument_list_is_applicable, Diagnostic.DeferArg(() => TypeToString(errorType))));
        }
        return result;

        IType GetInstantiatedType(IType type)
        {
            var hasSignatures = false;
            var hasApplicableSignature = false;

            // PORT-NOTE: the reference declares `getInstantiatedTypePart` after `return result;`; a C# local function
            // must be declared before the statement that reads its captures, so it is declared first — the only change.
            IType GetInstantiatedTypePart(IType type)
            {
                if ((type.Flags & TypeFlags.Object) != 0)
                {
                    var resolved = ResolveStructuredTypeMembers((UnionOrIntersectionType)type);
                    var callSignatures = GetInstantiatedSignatures(resolved.CallSignatures!);
                    var constructSignatures = GetInstantiatedSignatures(resolved.ConstructSignatures!);
                    hasSignatures |= Core.Length(resolved.CallSignatures) != 0 || Core.Length(resolved.ConstructSignatures) != 0;
                    hasApplicableSignature |= Core.Length(callSignatures) != 0 || Core.Length(constructSignatures) != 0;
                    if (!ReferenceEquals(callSignatures, resolved.CallSignatures) || !ReferenceEquals(constructSignatures, resolved.ConstructSignatures))
                    {
                        // PORT-NOTE: the reference casts the anonymous type it creates to
                        // `ResolvedType & InstantiationExpressionType`; the port reaches the augmented face through the
                        // interface the object family's leaf class implements, which is where both slots live
                        // (Model/AnonymousType.cs:178).
                        var instantiated = (IInstantiationExpressionType)CreateAnonymousType(CreateSymbol(SymbolFlags.None, InternalSymbolName.InstantiationExpression.Value), resolved.Members!, callSignatures, constructSignatures, resolved.IndexInfos!);
                        instantiated.ObjectFlags |= ObjectFlags.InstantiationExpressionType;
                        instantiated.Node = (INodeWithTypeArguments)node;
                        return instantiated;
                    }
                }
                else if ((type.Flags & TypeFlags.InstantiableNonPrimitive) != 0)
                {
                    var constraint = GetBaseConstraintOfType(type);
                    if (constraint is not null)
                    {
                        var instantiated = GetInstantiatedTypePart(constraint);
                        if (!ReferenceEquals(instantiated, constraint))
                        {
                            return instantiated;
                        }
                    }
                }
                else if ((type.Flags & TypeFlags.Union) != 0)
                {
                    return MapType(type, GetInstantiatedType)!;
                }
                else if ((type.Flags & TypeFlags.Intersection) != 0)
                {
                    return GetIntersectionType((List<IType>)Core.SameMap(((IIntersectionType)type).Types!, (t, _) => GetInstantiatedTypePart(t))!);
                }
                return type;
            }

            var result = GetInstantiatedTypePart(type);
            hasSomeApplicableSignature |= hasApplicableSignature;
            if (hasSignatures && !hasApplicableSignature)
            {
                nonApplicableType ??= type;
            }
            return result;
        }

        List<ISignature> GetInstantiatedSignatures(IList<ISignature> signatures)
        {
            var applicableSignatures = Core.Filter(signatures, sig => sig.TypeParameters is not null && HasCorrectTypeArgumentArity(sig, typeArguments))!;
            return (List<ISignature>)Core.SameMap(applicableSignatures, (sig, _) =>
            {
                var typeArgumentTypes = CheckTypeArguments(sig, typeArguments!, /*reportErrors*/ true);
                return typeArgumentTypes is not null ? GetSignatureInstantiation(sig, typeArgumentTypes, Utilities.IsInJSFile(sig.Declaration)) : sig;
            })!;
        }

        // PORT-NOTE: `createDiagnosticForNodeArray` takes `NodeArray<Node>` and the reference hands it the call's
        // `NodeArray<TypeNode>`; C# generics are invariant, so the covariant view is materialized, keeping the range.
        NodeArray<Node> ToNodeArray(NodeArray<ITypeNode> nodes)
        {
            var result = new NodeArray<Node>
            {
                Pos = nodes.Pos,
                End = nodes.End,
                HasTrailingComma = nodes.HasTrailingComma,
                TransformFlags = nodes.TransformFlags,
            };
            foreach (var typeArgument in nodes)
            {
                result.Add((Node)typeArgument);
            }
            return result;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:38261-38264 — <c>checkSatisfiesExpression</c>.</summary>
    internal IType CheckSatisfiesExpression(SatisfiesExpression node)
    {
        CheckSourceElement(node.Type!);
        return CheckSatisfiesExpressionWorker(node.Expression!, node.Type!);
    }

    /// <summary>ported from: src/compiler/checker.ts:38266-38275 — <c>checkSatisfiesExpressionWorker</c>.</summary>
    internal IType CheckSatisfiesExpressionWorker(Expression expression, ITypeNode target, CheckMode? checkMode = null)
    {
        var exprType = CheckExpression(expression, checkMode ?? CheckMode.Normal);
        var targetType = GetTypeFromTypeNode(target);
        if (IsErrorType(targetType))
        {
            return targetType;
        }
        var errorNode = Utilities.FindAncestor(((Node)target).Parent, n => n.Kind == SyntaxKind.SatisfiesExpression || n.Kind == SyntaxKind.JSDocSatisfiesTag);
        CheckTypeAssignableToAndOptionallyElaborate(exprType, targetType, errorNode, expression, Diagnostics.Type_0_does_not_satisfy_the_expected_type_1);
        return exprType;
    }

    // ---------------------------------------------------------------------------------------------
    // Meta properties (checker.ts:38277-38335)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:38277-38293 — <c>checkMetaProperty</c>.</summary>
    internal IType CheckMetaProperty(MetaProperty node)
    {
        CheckGrammarMetaProperty(node);

        if (node.KeywordToken == SyntaxKind.NewKeyword)
        {
            return CheckNewTargetMetaProperty(node);
        }

        if (node.KeywordToken == SyntaxKind.ImportKeyword)
        {
            if (node.Name!.EscapedText == "defer")
            {
                Debug.Assert(!NodeTests.IsCallExpression(node.Parent!) || ((CallExpression)node.Parent!).Expression != node, "Trying to get the type of `import.defer` in `import.defer(...)`");
                return _errorType;
            }
            return CheckImportMetaProperty(node);
        }

        // PORT-NOTE: the reference's `Debug.assertNever` returns `never`; the port's is void, so the value-producing
        // `Debug.Fail<IType>` carries the message `Debug.AssertNever` would have built.
        return Debug.Fail<IType>($"Illegal value: SyntaxKind: {Debug.FormatSyntaxKind(node.KeywordToken)}");
    }

    /// <summary>ported from: src/compiler/checker.ts:38295-38305 — <c>checkMetaPropertyKeyword</c>.</summary>
    internal IType CheckMetaPropertyKeyword(MetaProperty node)
    {
        switch (node.KeywordToken)
        {
            case SyntaxKind.ImportKeyword:
                return GetGlobalImportMetaExpressionType();
            case SyntaxKind.NewKeyword:
                var type = CheckNewTargetMetaProperty(node);
                return IsErrorType(type) ? _errorType : CreateNewTargetExpressionType(type);
            default:
                return Debug.Fail<IType>($"Illegal value: SyntaxKind: {Debug.FormatSyntaxKind(node.KeywordToken)}");
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:38307-38321 — <c>checkNewTargetMetaProperty</c>.</summary>
    internal IType CheckNewTargetMetaProperty(MetaProperty node)
    {
        var container = Utilities.GetNewTargetContainer(node);
        if (container is null)
        {
            Error(node, Diagnostics.Meta_property_0_is_only_allowed_in_the_body_of_a_function_declaration_function_expression_or_constructor, "new.target");
            return _errorType;
        }
        else if (container.Kind == SyntaxKind.Constructor)
        {
            var symbol = GetSymbolOfDeclaration((IDeclaration)container.Parent!);
            return GetTypeOfSymbol(symbol);
        }
        else
        {
            var symbol = GetSymbolOfDeclaration((IDeclaration)container);
            return GetTypeOfSymbol(symbol);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:38323-38335 — <c>checkImportMetaProperty</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>SourceFile.ImpliedNodeFormat</c> is <c>object?</c> in the port, so the reference's
    /// <c>!== ModuleKind.ESNext</c> is an <c>Equals</c> test.
    /// </remarks>
    internal IType CheckImportMetaProperty(MetaProperty node)
    {
        if (ModuleKind.Node16 <= _moduleKind && _moduleKind <= ModuleKind.NodeNext)
        {
            if (!Equals(Utilities.GetSourceFileOfNode(node)!.ImpliedNodeFormat, ModuleKind.ESNext))
            {
                Error(node, Diagnostics.The_import_meta_meta_property_is_not_allowed_in_files_which_will_build_into_CommonJS_output);
            }
        }
        else if (_moduleKind < ModuleKind.ES2020 && _moduleKind != ModuleKind.System)
        {
            Error(node, Diagnostics.The_import_meta_meta_property_is_only_allowed_when_the_module_option_is_es2020_es2022_esnext_system_node16_node18_node20_or_nodenext);
        }
        var file = Utilities.GetSourceFileOfNode(node)!;
        Debug.Assert((file.Flags & NodeFlags.PossiblyContainsImportMeta) != 0, "Containing file is missing import meta node flag.");
        return node.Name!.EscapedText == "meta" ? GetGlobalImportMetaType() : _errorType;
    }

    // ---------------------------------------------------------------------------------------------
    // Parameter and tuple-element naming (checker.ts:38337-38498)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:38337-38344 — <c>getTypeOfParameter</c>.</summary>
    internal IType GetTypeOfParameter(ISymbol symbol)
    {
        var declaration = symbol.ValueDeclaration;
        return AddOptionality(
            GetTypeOfSymbol(symbol),
            /*isProperty*/ false,
            /*isOptional*/ declaration is not null && (Utilities.HasInitializer((Node)declaration) || Utilities.IsOptionalDeclaration(declaration)));
    }

    /// <summary>ported from: src/compiler/checker.ts:38352-38404 — <c>getTupleElementLabelFromBindingElement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>BindingElement | ParameterDeclaration</c> is a union of two classes that share only
    /// <c>INamedDeclaration.Name</c> and the <c>dotDotDotToken</c> slot the model does not lift to an interface, so the
    /// parameter is <see cref="Node"/> and the <c>dotDotDotToken</c> tests are per class, exactly as
    /// <c>assignBindingElementTypes</c> below does.
    /// </remarks>
    internal string GetTupleElementLabelFromBindingElement(Node node, int index, ElementFlags elementFlags)
    {
        var name = ((INamedDeclaration)node).Name;
        var dotDotDotToken = node is BindingElement bindingElement ? bindingElement.DotDotDotToken : ((ParameterDeclaration)node).DotDotDotToken;
        switch (name!.Kind)
        {
            case SyntaxKind.Identifier:
            {
                var nameText = ((Identifier)name).EscapedText!;
                if (dotDotDotToken is not null)
                {
                    // given
                    //   (...[x, y, ...z]: [number, number, ...number[]]) => ...
                    // this produces
                    //   (x: number, y: number, ...z: number[]) => ...
                    // which preserves rest elements of 'z'

                    // given
                    //   (...[x, y, ...z]: [number, number, ...[...number[], number]]) => ...
                    // this produces
                    //   (x: number, y: number, ...z: number[], z_1: number) => ...
                    // which preserves rest elements of z but gives distinct numbers to fixed elements of 'z'
                    return (elementFlags & ElementFlags.Variable) != 0 ? nameText : $"{nameText}_{index}";
                }
                else
                {
                    // given
                    //   (...[x]: [number]) => ...
                    // this produces
                    //   (x: number) => ...
                    // which preserves fixed elements of 'x'

                    // given
                    //   (...[x]: ...number[]) => ...
                    // this produces
                    //   (x_0: number) => ...
                    // which which numbers fixed elements of 'x' whose tuple element type is variable
                    return (elementFlags & ElementFlags.Fixed) != 0 ? nameText : $"{nameText}_n";
                }
            }
            case SyntaxKind.ArrayBindingPattern:
            {
                if (dotDotDotToken is not null)
                {
                    var elements = ((ArrayBindingPattern)name).Elements!;
                    var lastElement = Core.TryCast<BindingElement, Node>(Core.LastOrUndefined(elements), NodeTests.IsBindingElement);
                    var elementCount = elements.Count - (lastElement?.DotDotDotToken is not null ? 1 : 0);
                    if (index < elementCount)
                    {
                        var element = elements[index];
                        if (NodeTests.IsBindingElement(element))
                        {
                            return GetTupleElementLabelFromBindingElement(element, index, elementFlags);
                        }
                    }
                    else if (lastElement?.DotDotDotToken is not null)
                    {
                        return GetTupleElementLabelFromBindingElement(lastElement, index - elementCount, elementFlags);
                    }
                }
                break;
            }
        }
        return $"arg_{index}";
    }

    /// <summary>ported from: src/compiler/checker.ts:38406-38416 — <c>getTupleElementLabel</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's two overload signatures collapse into this one member with the implementation's own
    /// defaults (the first overload is the second called with <c>index = 0</c> and <c>Fixed</c>), matching the single
    /// landed call shape (<c>Checker.Members.cs:346</c>).
    /// </remarks>
    internal string GetTupleElementLabel(Node? d, int index = 0, ElementFlags elementFlags = ElementFlags.Fixed, ISymbol? restSymbol = null)
    {
        if (d is null)
        {
            var restParameter = Core.TryCast<ParameterDeclaration, Node>((Node?)restSymbol?.ValueDeclaration, NodeTests.IsParameter);
            return restParameter is not null ? GetTupleElementLabelFromBindingElement(restParameter, index, elementFlags)
                : $"{restSymbol?.EscapedName ?? "arg"}_{index}";
        }
        // PORT-NOTE: TS reads `d.name` off the union `NamedTupleMember | ParameterDeclaration`, whose two members both
        // declare it; the port's `NamedTupleMember` is not an `INamedDeclaration` (the same split §15.15 records for
        // the other unions), so the slot is read per class.
        var name = d switch
        {
            NamedTupleMember namedTupleMember => namedTupleMember.Name,
            ParameterDeclaration parameter => parameter.Name,
            _ => Debug.Fail<Node>($"getTupleElementLabel: unexpected declaration kind {d.Kind}"),
        };
        Debug.Assert(NodeTests.IsIdentifier(name!)); // Parameter declarations could be binding patterns, but we only allow identifier names
        return ((Identifier)name!).EscapedText!;
    }

    /// <summary>ported from: src/compiler/checker.ts:38418-38433 — <c>getParameterNameAtPosition</c>.</summary>
    internal string? GetParameterNameAtPosition(ISignature signature, int pos, IType? overrideRestType = null)
    {
        var paramCount = signature.Parameters!.Count - (SignatureHasRestParameter(signature) ? 1 : 0);
        if (pos < paramCount)
        {
            return signature.Parameters[pos].EscapedName;
        }
        var restParameter = pos < signature.Parameters.Count ? signature.Parameters[paramCount] : _unknownSymbol;
        var restType = overrideRestType ?? GetTypeOfSymbol(restParameter);
        if (IsTupleType(restType))
        {
            var tupleType = (ITupleType)((ITypeReference)restType).Target!;
            var index = pos - paramCount;
            var associatedName = tupleType.LabeledElementDeclarations is not null && index < tupleType.LabeledElementDeclarations.Count ? tupleType.LabeledElementDeclarations[index] : null;
            var elementFlags = tupleType.ElementFlags![index];
            return GetTupleElementLabel(associatedName, index, elementFlags, restParameter);
        }
        return restParameter.EscapedName;
    }

    /// <summary>ported from: src/compiler/checker.ts:38435-38475 — <c>getParameterIdentifierInfoAtPosition</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference returns the anonymous object
    /// <c>{ parameter: Identifier; parameterName: __String; isRestParameter: boolean }</c>; the port returns the same three
    /// slots as a tuple.
    /// </remarks>
    internal (Identifier Parameter, string? ParameterName, bool IsRestParameter)? GetParameterIdentifierInfoAtPosition(ISignature signature, int pos)
    {
        if (signature.Declaration?.Kind == SyntaxKind.JSDocFunctionType)
        {
            return null;
        }
        var paramCount = signature.Parameters!.Count - (SignatureHasRestParameter(signature) ? 1 : 0);
        if (pos < paramCount)
        {
            var param = signature.Parameters[pos];
            var paramIdent = GetParameterDeclarationIdentifier(param);
            return paramIdent is not null
                ? (paramIdent, param.EscapedName, false)
                : null;
        }

        var restParameter = signature.Parameters[paramCount];
        var restIdent = GetParameterDeclarationIdentifier(restParameter);
        if (restIdent is null)
        {
            return null;
        }

        var restType = GetTypeOfSymbol(restParameter);
        if (IsTupleType(restType))
        {
            var associatedNames = ((ITupleType)((ITypeReference)restType).Target!).LabeledElementDeclarations;
            var index = pos - paramCount;
            var associatedName = associatedNames is not null && index < associatedNames.Count ? associatedNames[index] : null;
            var isRestTupleElement = associatedName is NamedTupleMember { DotDotDotToken: not null };

            if (associatedName is not null)
            {
                Debug.Assert(NodeTests.IsIdentifier(((INamedDeclaration)associatedName).Name!));
                var name = (Identifier)((INamedDeclaration)associatedName).Name!;
                return (name, name.EscapedText, isRestTupleElement);
            }

            return null;
        }

        if (pos == paramCount)
        {
            return (restIdent, restParameter.EscapedName, true);
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:38477-38479 — <c>getParameterDeclarationIdentifier</c>.</summary>
    internal Identifier? GetParameterDeclarationIdentifier(ISymbol symbol)
        => symbol.ValueDeclaration is not null && NodeTests.IsParameter((Node)symbol.ValueDeclaration) && NodeTests.IsIdentifier(((INamedDeclaration)symbol.ValueDeclaration).Name!)
            ? (Identifier)((INamedDeclaration)symbol.ValueDeclaration).Name!
            : null;

    /// <summary>ported from: src/compiler/checker.ts:38480-38482 — <c>isValidDeclarationForTupleLabel</c>.</summary>
    internal bool IsValidDeclarationForTupleLabel(IDeclaration d)
        => d.Kind == SyntaxKind.NamedTupleMember || (NodeTests.IsParameter((Node)d) && ((INamedDeclaration)d).Name is not null && NodeTests.IsIdentifier(((INamedDeclaration)d).Name!));

    /// <summary>ported from: src/compiler/checker.ts:38484-38498 — <c>getNameableDeclarationAtPosition</c>.</summary>
    internal Node? GetNameableDeclarationAtPosition(ISignature signature, int pos)
    {
        var paramCount = signature.Parameters!.Count - (SignatureHasRestParameter(signature) ? 1 : 0);
        if (pos < paramCount)
        {
            var decl = signature.Parameters[pos].ValueDeclaration;
            return decl is not null && IsValidDeclarationForTupleLabel((IDeclaration)decl) ? (Node)decl : null;
        }
        var restParameter = signature.Parameters[paramCount];
        var restType = GetTypeOfSymbol(restParameter);
        if (IsTupleType(restType))
        {
            var associatedNames = ((ITupleType)((ITypeReference)restType).Target!).LabeledElementDeclarations;
            var index = pos - paramCount;
            return associatedNames is not null && index < associatedNames.Count ? associatedNames[index] : null;
        }
        return restParameter.ValueDeclaration is not null && IsValidDeclarationForTupleLabel((IDeclaration)restParameter.ValueDeclaration) ? (Node)restParameter.ValueDeclaration : null;
    }

    // ---------------------------------------------------------------------------------------------
    // The type of an argument position (checker.ts:38500-38657)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:38500-38502 — <c>getTypeAtPosition</c>.</summary>
    internal IType GetTypeAtPosition(ISignature signature, int pos)
        => TryGetTypeAtPosition(signature, pos) ?? _anyType;

    /// <summary>ported from: src/compiler/checker.ts:38504-38520 — <c>tryGetTypeAtPosition</c>.</summary>
    internal IType? TryGetTypeAtPosition(ISignature signature, int pos)
    {
        var paramCount = signature.Parameters!.Count - (SignatureHasRestParameter(signature) ? 1 : 0);
        if (pos < paramCount)
        {
            return GetTypeOfParameter(signature.Parameters[pos]);
        }
        if (SignatureHasRestParameter(signature))
        {
            // We want to return the value undefined for an out of bounds parameter position,
            // so we need to check bounds here before calling getIndexedAccessType (which
            // otherwise would return the type 'undefined').
            var restType = GetTypeOfSymbol(signature.Parameters[paramCount]);
            var index = pos - paramCount;
            if (!IsTupleType(restType) || (((ITupleType)((ITypeReference)restType).Target!).CombinedFlags & ElementFlags.Variable) != 0 || index < ((ITupleType)((ITypeReference)restType).Target!).FixedLength)
            {
                return GetIndexedAccessType(restType, GetNumberLiteralType(index));
            }
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:38522-38544 — <c>getRestTypeAtPosition</c>.</summary>
    internal IType GetRestTypeAtPosition(ISignature source, int pos, bool? @readonly = null)
    {
        var parameterCount = GetParameterCount(source);
        var minArgumentCount = GetMinArgumentCount(source);
        var restType = GetEffectiveRestType(source);
        if (restType is not null && pos >= parameterCount - 1)
        {
            return pos == parameterCount - 1 ? restType : CreateArrayType(GetIndexedAccessType(restType, _numberType));
        }
        var types = new List<IType>();
        var flags = new List<ElementFlags>();
        var names = new List<Node>();
        for (var i = pos; i < parameterCount; i++)
        {
            if (restType is null || i < parameterCount - 1)
            {
                types.Add(GetTypeAtPosition(source, i));
                flags.Add(i < minArgumentCount ? ElementFlags.Required : ElementFlags.Optional);
            }
            else
            {
                types.Add(restType);
                flags.Add(ElementFlags.Variadic);
            }
            names.Add(GetNameableDeclarationAtPosition(source, i)!);
        }
        return CreateTupleType(types, flags, @readonly ?? false, names);
    }

    /// <summary>ported from: src/compiler/checker.ts:38549-38553 — <c>getRestOrAnyTypeAtPosition</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's comment above this member is kept with <c>getEffectiveRestType</c>'s neighbours in the
    /// reference; it is repeated here so the "any[] in rest position" rule stays next to its implementation.
    /// </remarks>
    internal IType GetRestOrAnyTypeAtPosition(ISignature source, int pos)
    {
        var restType = GetRestTypeAtPosition(source, pos);
        var elementType = GetElementTypeOfArrayType(restType);
        return elementType is not null && IsTypeAny(elementType) ? _anyType : restType;
    }

    // Return the number of parameters in a signature. The rest parameter, if present, counts as one
    // parameter. For example, the parameter count of (x: number, y: number, ...z: string[]) is 3 and
    // the parameter count of (x: number, ...args: [number, ...string[], boolean])) is also 3. In the
    // latter example, the effective rest type is [...string[], boolean].
    /// <summary>ported from: src/compiler/checker.ts:38559-38568 — <c>getParameterCount</c>.</summary>
    internal int GetParameterCount(ISignature signature)
    {
        var length = signature.Parameters!.Count;
        if (SignatureHasRestParameter(signature))
        {
            var restType = GetTypeOfSymbol(signature.Parameters[length - 1]);
            if (IsTupleType(restType))
            {
                var tupleType = (ITupleType)((ITypeReference)restType).Target!;
                return length + tupleType.FixedLength - ((tupleType.CombinedFlags & ElementFlags.Variable) != 0 ? 0 : 1);
            }
        }
        return length;
    }

    /// <summary>ported from: src/compiler/checker.ts:38570-38604 — <c>getMinArgumentCount</c>.</summary>
    internal int GetMinArgumentCount(ISignature signature, MinArgumentCountFlags flags = MinArgumentCountFlags.None)
    {
        var strongArityForUntypedJS = (flags & MinArgumentCountFlags.StrongArityForUntypedJS) != 0;
        var voidIsNonOptional = (flags & MinArgumentCountFlags.VoidIsNonOptional) != 0;
        if (voidIsNonOptional || signature.ResolvedMinArgumentCount is null)
        {
            int? minArgumentCount = null;
            if (SignatureHasRestParameter(signature))
            {
                var restType = GetTypeOfSymbol(signature.Parameters![signature.Parameters.Count - 1]);
                if (IsTupleType(restType))
                {
                    var tupleType = (ITupleType)((ITypeReference)restType).Target!;
                    var firstOptionalIndex = Core.FindIndex(tupleType.ElementFlags!, (f, _) => (f & ElementFlags.Required) == 0);
                    var requiredCount = firstOptionalIndex < 0 ? tupleType.FixedLength : firstOptionalIndex;
                    if (requiredCount > 0)
                    {
                        minArgumentCount = signature.Parameters.Count - 1 + requiredCount;
                    }
                }
            }
            if (minArgumentCount is null)
            {
                if (!strongArityForUntypedJS && (signature.Flags & SignatureFlags.IsUntypedSignatureInJSFile) != 0)
                {
                    return 0;
                }
                minArgumentCount = signature.MinArgumentCount;
            }
            if (voidIsNonOptional)
            {
                return minArgumentCount.Value;
            }
            for (var i = minArgumentCount.Value - 1; i >= 0; i--)
            {
                var type = GetTypeAtPosition(signature, i);
                if ((FilterType(type, AcceptsVoid).Flags & TypeFlags.Never) != 0)
                {
                    break;
                }
                minArgumentCount = i;
            }
            signature.ResolvedMinArgumentCount = minArgumentCount;
        }
        return signature.ResolvedMinArgumentCount!.Value;
    }

    /// <summary>ported from: src/compiler/checker.ts:38606-38612 — <c>hasEffectiveRestParameter</c>.</summary>
    internal bool HasEffectiveRestParameter(ISignature signature)
    {
        if (SignatureHasRestParameter(signature))
        {
            var restType = GetTypeOfSymbol(signature.Parameters![signature.Parameters.Count - 1]);
            return !IsTupleType(restType) || (((ITupleType)((ITypeReference)restType).Target!).CombinedFlags & ElementFlags.Variable) != 0;
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:38614-38625 — <c>getEffectiveRestType</c>.</summary>
    internal IType? GetEffectiveRestType(ISignature signature)
    {
        if (SignatureHasRestParameter(signature))
        {
            var restType = GetTypeOfSymbol(signature.Parameters![signature.Parameters.Count - 1]);
            if (!IsTupleType(restType))
            {
                return IsTypeAny(restType) ? _anyArrayType : restType;
            }
            var tupleType = (ITupleType)((ITypeReference)restType).Target!;
            if ((tupleType.CombinedFlags & ElementFlags.Variable) != 0)
            {
                return SliceTupleType((ITupleTypeReference)restType, tupleType.FixedLength);
            }
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:38627-38630 — <c>getNonArrayRestType</c>.</summary>
    internal IType? GetNonArrayRestType(ISignature signature)
    {
        var restType = GetEffectiveRestType(signature);
        return restType is not null && !IsArrayType(restType) && !IsTypeAny(restType) ? restType : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:38632-38634 — <c>getTypeOfFirstParameterOfSignature</c>.</summary>
    internal IType GetTypeOfFirstParameterOfSignature(ISignature signature)
        => GetTypeOfFirstParameterOfSignatureWithFallback(signature, _neverType);

    /// <summary>ported from: src/compiler/checker.ts:38636-38638 — <c>getTypeOfFirstParameterOfSignatureWithFallback</c>.</summary>
    internal IType GetTypeOfFirstParameterOfSignatureWithFallback(ISignature signature, IType fallbackType)
        => Core.Length(signature.Parameters) > 0 ? GetTypeAtPosition(signature, 0) : fallbackType;

    /// <summary>ported from: src/compiler/checker.ts:38640-38657 — <c>inferFromAnnotatedParametersAndReturn</c>.</summary>
    internal void InferFromAnnotatedParametersAndReturn(ISignature signature, ISignature context, IInferenceContext inferenceContext)
    {
        var len = signature.Parameters!.Count - (SignatureHasRestParameter(signature) ? 1 : 0);
        for (var i = 0; i < len; i++)
        {
            var declaration = (ParameterDeclaration)signature.Parameters[i].ValueDeclaration!;
            var typeNode = Utilities.GetEffectiveTypeAnnotationNode(declaration);
            if (typeNode is not null)
            {
                var source = AddOptionality(GetTypeFromTypeNode(typeNode), /*isProperty*/ false, Utilities.IsOptionalDeclaration(declaration));
                var target = GetTypeAtPosition(context, i);
                InferTypes(inferenceContext.Inferences!, source, target);
            }
        }
        var returnTypeNode = signature.Declaration is not null ? Utilities.GetEffectiveReturnTypeNode(signature.Declaration) : null;
        if (returnTypeNode is not null)
        {
            var source = GetTypeFromTypeNode(returnTypeNode);
            var target = GetReturnTypeOfSignature(context);
            InferTypes(inferenceContext.Inferences!, source, target);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Assigning parameter types (checker.ts:38659-38753)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:38659-38706 — <c>assignContextualParameterTypes</c>.</summary>
    internal void AssignContextualParameterTypes(ISignature signature, ISignature context)
    {
        if (context.TypeParameters is not null)
        {
            if (signature.TypeParameters is null)
            {
                signature.TypeParameters = context.TypeParameters;
            }
            else
            {
                return; // This signature has already has a contextual inference performed and cached on it!
            }
        }
        if (context.ThisParameter is not null)
        {
            var parameter = signature.ThisParameter;
            if (parameter is null || parameter.ValueDeclaration is not null && ((ParameterDeclaration)parameter.ValueDeclaration).Type is null)
            {
                if (parameter is null)
                {
                    signature.ThisParameter = CreateSymbolWithType(context.ThisParameter, /*type*/ null);
                }
                AssignParameterType(signature.ThisParameter!, GetTypeOfSymbol(context.ThisParameter));
            }
        }
        var len = signature.Parameters!.Count - (SignatureHasRestParameter(signature) ? 1 : 0);
        for (var i = 0; i < len; i++)
        {
            var parameter = signature.Parameters[i];
            var declaration = (ParameterDeclaration)parameter.ValueDeclaration!;
            if (Utilities.GetEffectiveTypeAnnotationNode(declaration) is null)
            {
                var type = TryGetTypeAtPosition(context, i);
                if (type is not null && declaration.Initializer is not null)
                {
                    var initializerType = CheckDeclarationInitializer(declaration, CheckMode.Normal);
                    if (!IsTypeAssignableTo(initializerType, type) && IsTypeAssignableTo(type, initializerType = WidenTypeInferredFromInitializer(declaration, initializerType)))
                    {
                        type = initializerType;
                    }
                }
                AssignParameterType(parameter, type);
            }
        }
        if (SignatureHasRestParameter(signature))
        {
            // parameter might be a transient symbol generated by use of `arguments` in the function body.
            var parameter = Core.Last(signature.Parameters);
            if (
                parameter.ValueDeclaration is not null
                    ? Utilities.GetEffectiveTypeAnnotationNode((Node)parameter.ValueDeclaration) is null
                    // a declarationless parameter may still have a `.type` already set by its construction logic
                    // (which may pull a type from a jsdoc) - only allow fixing on `DeferredType` parameters with a fallback type
                    : (GetCheckFlags(parameter) & CheckFlags.DeferredType) != 0
            )
            {
                var contextualParameterType = GetRestTypeAtPosition(context, len);
                AssignParameterType(parameter, contextualParameterType);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:38708-38715 — <c>assignNonContextualParameterTypes</c>.</summary>
    internal void AssignNonContextualParameterTypes(ISignature signature)
    {
        if (signature.ThisParameter is not null)
        {
            AssignParameterType(signature.ThisParameter);
        }
        foreach (var parameter in signature.Parameters!)
        {
            AssignParameterType(parameter);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:38717-38737 — <c>assignParameterType</c>.</summary>
    internal void AssignParameterType(ISymbol parameter, IType? contextualType = null)
    {
        var links = GetSymbolLinks(parameter);
        if (links.Type is null)
        {
            var declaration = (ParameterDeclaration?)parameter.ValueDeclaration;
            links.Type = AddOptionality(
                contextualType ?? (declaration is not null ? GetWidenedTypeForVariableLikeDeclaration(declaration, /*reportErrors*/ true) : GetTypeOfSymbol(parameter)),
                /*isProperty*/ false,
                /*isOptional*/ declaration is not null && declaration.Initializer is null && Utilities.IsOptionalDeclaration(declaration));
            if (declaration is not null && declaration.Name!.Kind != SyntaxKind.Identifier)
            {
                // if inference didn't come up with anything but unknown, fall back to the binding pattern if present.
                if (ReferenceEquals(links.Type, _unknownType))
                {
                    links.Type = GetTypeFromBindingPattern(declaration.Name!);
                }
                AssignBindingElementTypes(declaration.Name!, links.Type);
            }
        }
        else if (contextualType is not null)
        {
            Debug.AssertEqual(links.Type, contextualType, "Parameter symbol already has a cached type which differs from newly assigned type");
        }
    }

    // When contextual typing assigns a type to a parameter that contains a binding pattern, we also need to push
    // the destructured type into the contained binding elements.
    /// <summary>ported from: src/compiler/checker.ts:38741-38753 — <c>assignBindingElementTypes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>BindingPattern</c> is the union <c>ObjectBindingPattern | ArrayBindingPattern</c>
    /// and the generated model has no single class for it, so the parameter is <see cref="Node"/>; the two classes'
    /// <c>Elements</c> slots are read through the covariant <see cref="IEnumerable{T}"/>, which is what keeps the
    /// reference's single loop over <c>pattern.elements</c>.
    /// </remarks>
    internal void AssignBindingElementTypes(Node pattern, IType parentType)
    {
        IEnumerable<Node>? elements = pattern is ArrayBindingPattern arrayPattern ? arrayPattern.Elements : ((ObjectBindingPattern)pattern).Elements;
        foreach (var element in elements!)
        {
            if (!NodeTests.IsOmittedExpression(element))
            {
                var bindingElement = (BindingElement)element;
                var type = GetBindingElementTypeFromParentType(bindingElement, parentType, /*noTupleBoundsCheck*/ false);
                if (bindingElement.Name!.Kind == SyntaxKind.Identifier)
                {
                    GetSymbolLinks(GetSymbolOfDeclaration((IDeclaration)element)).Type = type;
                }
                else
                {
                    AssignBindingElementTypes(bindingElement.Name!, type);
                }
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The decorator-context type factories (checker.ts:38755-38834)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:38755-38757 — <c>createClassDecoratorContextType</c>.</summary>
    internal IType CreateClassDecoratorContextType(IType classType)
        => TryCreateTypeReference(GetGlobalClassDecoratorContextType(/*reportErrors*/ true), [classType]);

    /// <summary>ported from: src/compiler/checker.ts:38759-38761 — <c>createClassMethodDecoratorContextType</c>.</summary>
    internal IType CreateClassMethodDecoratorContextType(IType thisType, IType valueType)
        => TryCreateTypeReference(GetGlobalClassMethodDecoratorContextType(/*reportErrors*/ true), [thisType, valueType]);

    /// <summary>ported from: src/compiler/checker.ts:38763-38765 — <c>createClassGetterDecoratorContextType</c>.</summary>
    internal IType CreateClassGetterDecoratorContextType(IType thisType, IType valueType)
        => TryCreateTypeReference(GetGlobalClassGetterDecoratorContextType(/*reportErrors*/ true), [thisType, valueType]);

    /// <summary>ported from: src/compiler/checker.ts:38767-38769 — <c>createClassSetterDecoratorContextType</c>.</summary>
    internal IType CreateClassSetterDecoratorContextType(IType thisType, IType valueType)
        => TryCreateTypeReference(GetGlobalClassSetterDecoratorContextType(/*reportErrors*/ true), [thisType, valueType]);

    /// <summary>ported from: src/compiler/checker.ts:38771-38773 — <c>createClassAccessorDecoratorContextType</c>.</summary>
    internal IType CreateClassAccessorDecoratorContextType(IType thisType, IType valueType)
        => TryCreateTypeReference(GetGlobalClassAccessorDecoratorContextType(/*reportErrors*/ true), [thisType, valueType]);

    /// <summary>ported from: src/compiler/checker.ts:38775-38777 — <c>createClassFieldDecoratorContextType</c>.</summary>
    internal IType CreateClassFieldDecoratorContextType(IType thisType, IType valueType)
        => TryCreateTypeReference(GetGlobalClassFieldDecoratorContextType(/*reportErrors*/ true), [thisType, valueType]);

    /// <summary>
    /// Gets a type like <c>{ name: "foo", private: false, static: true }</c> that is used to provided member-specific
    /// details that will be intersected with a decorator context type.
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:38783-38795 — <c>getClassMemberDecoratorContextOverrideType</c>.</remarks>
    internal IType GetClassMemberDecoratorContextOverrideType(IType nameType, bool isPrivate, bool isStatic)
    {
        var key = $"{(isPrivate ? "p" : "P")}{(isStatic ? "s" : "S")}{nameType.Id}";
        if (!_decoratorContextOverrideTypeCache.TryGetValue(key, out var overrideType))
        {
            var members = Utilities.CreateSymbolTable();
            members["name"] = CreateProperty("name", nameType);
            members["private"] = CreateProperty("private", isPrivate ? _trueType : _falseType);
            members["static"] = CreateProperty("static", isStatic ? _trueType : _falseType);
            overrideType = CreateAnonymousType(/*symbol*/ null, members, [], [], []);
            _decoratorContextOverrideTypeCache[key] = overrideType;
        }
        return overrideType;
    }

    /// <summary>ported from: src/compiler/checker.ts:38797-38809 — <c>createClassMemberDecoratorContextTypeForNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's chain ends in <c>Debug.failBadSyntaxKind(node)</c>, which TS declares <c>never</c> and
    /// can therefore use in expression position; the port's is <c>void</c>, so the value-producing
    /// <c>Debug.Fail&lt;IType&gt;</c> stands in for it, as <c>Checker.Check.Symbols.cs</c> does.
    /// </remarks>
    internal IType CreateClassMemberDecoratorContextTypeForNode(Node node, IType thisType, IType valueType)
    {
        var isStatic = Utilities.HasStaticModifier(node);
        var name = ((INamedDeclaration)node).Name!;
        var isPrivate = NodeTests.IsPrivateIdentifier(name);
        var nameType = isPrivate ? GetStringLiteralType(Utilities.IdText(name)) : GetLiteralTypeFromPropertyName(name);
        var contextType = NodeTests.IsMethodDeclaration(node) ? CreateClassMethodDecoratorContextType(thisType, valueType) :
            NodeTests.IsGetAccessorDeclaration(node) ? CreateClassGetterDecoratorContextType(thisType, valueType) :
            NodeTests.IsSetAccessorDeclaration(node) ? CreateClassSetterDecoratorContextType(thisType, valueType) :
            Utilities.IsAutoAccessorPropertyDeclaration(node) ? CreateClassAccessorDecoratorContextType(thisType, valueType) :
            NodeTests.IsPropertyDeclaration(node) ? CreateClassFieldDecoratorContextType(thisType, valueType) :
            Debug.Fail<IType>($"Illegal value: SyntaxKind: {Debug.FormatSyntaxKind(node.Kind)}");
        var overrideType = GetClassMemberDecoratorContextOverrideType(nameType, isPrivate, isStatic);
        return GetIntersectionType([contextType, overrideType]);
    }

    /// <summary>ported from: src/compiler/checker.ts:38811-38813 — <c>createClassAccessorDecoratorTargetType</c>.</summary>
    internal IType CreateClassAccessorDecoratorTargetType(IType thisType, IType valueType)
        => TryCreateTypeReference(GetGlobalClassAccessorDecoratorTargetType(/*reportErrors*/ true), [thisType, valueType]);

    /// <summary>ported from: src/compiler/checker.ts:38815-38817 — <c>createClassAccessorDecoratorResultType</c>.</summary>
    internal IType CreateClassAccessorDecoratorResultType(IType thisType, IType valueType)
        => TryCreateTypeReference(GetGlobalClassAccessorDecoratorResultType(/*reportErrors*/ true), [thisType, valueType]);

    /// <summary>ported from: src/compiler/checker.ts:38819-38823 — <c>createClassFieldDecoratorInitializerMutatorType</c>.</summary>
    internal IType CreateClassFieldDecoratorInitializerMutatorType(IType thisType, IType valueType)
    {
        var thisParam = CreateParameter("this", thisType);
        var valueParam = CreateParameter("value", valueType);
        return CreateFunctionType(/*typeParameters*/ null, thisParam, [valueParam], valueType, /*typePredicate*/ null, 1);
    }

    /// <summary>
    /// Creates a call signature for an ES Decorator. This method is used by the semantics of
    /// <c>getESDecoratorCallSignature</c>, which you should probably be using instead.
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:38829-38834 — <c>createESDecoratorCallSignature</c>.</remarks>
    internal ISignature CreateESDecoratorCallSignature(IType targetType, IType contextType, IType nonOptionalReturnType)
    {
        var targetParam = CreateParameter("target", targetType);
        var contextParam = CreateParameter("context", contextType);
        var returnType = GetUnionType([nonOptionalReturnType, _voidType]);
        return CreateCallSignature(/*typeParameters*/ null, /*thisParameter*/ null, [targetParam, contextParam], returnType);
    }

    // ---------------------------------------------------------------------------------------------
    // The decorator call signatures (checker.ts:38842-39131)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Gets a call signature that should be used when resolving <c>decorator</c> as a call.
    /// </summary>
    /// <remarks>
    /// <para>ported from: src/compiler/checker.ts:38842-39020 — <c>getESDecoratorCallSignature</c>.</para>
    /// <para>
    /// PORT-NOTE: the reference's 130-line explanation of decorator application order (checker.ts:38843-38920) is kept
    /// in the port as the summary above: it describes the specification the code below implements, and the code itself
    /// is ported line for line.
    /// </para>
    /// </remarks>
    internal ISignature? GetESDecoratorCallSignature(Decorator decorator)
    {
        var parent = decorator.Parent!;
        var links = GetNodeLinks(parent);
        if (links.DecoratorSignature is null)
        {
            links.DecoratorSignature = _anySignature;
            switch (parent.Kind)
            {
                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.ClassExpression:
                {
                    // Class decorators have a `context` of `ClassDecoratorContext<Class>`, where the `Class` type
                    // argument will be the "final type" of the class after all decorators are applied.
                    var targetType = GetTypeOfSymbol(GetSymbolOfDeclaration((IDeclaration)parent));
                    var contextType = CreateClassDecoratorContextType(targetType);
                    links.DecoratorSignature = CreateESDecoratorCallSignature(targetType, contextType, targetType);
                    break;
                }

                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                {
                    var node = parent;
                    if (!Utilities.IsClassLike(node.Parent!))
                    {
                        break;
                    }

                    // Method decorators have a `context` of `ClassMethodDecoratorContext<This, Value>`, getter
                    // decorators `<This, Value>` where `Value` is what the getter returns, setter decorators it where
                    // `Value` is what the setter takes, and auto-accessor decorators `<This, Value>`. In all three cases
                    // `This` is the "final type" of either the class or the instance.
                    var valueType = NodeTests.IsMethodDeclaration(node)
                        ? GetOrCreateTypeFromSignature(GetSignatureFromDeclaration(node))
                        : GetTypeOfNode(node);

                    var thisType = Utilities.HasStaticModifier(node)
                        ? GetTypeOfSymbol(GetSymbolOfDeclaration((IDeclaration)node.Parent!))
                        : GetDeclaredTypeOfClassOrInterface(GetSymbolOfDeclaration((IDeclaration)node.Parent!));

                    // We wrap the "input type", if necessary, to match the decoration target. For getters this is
                    // something like `() => inputType`, for setters it's `(value: inputType) => void` and for
                    // methods it is just the input type.
                    var targetType = NodeTests.IsGetAccessorDeclaration(node) ? CreateGetterFunctionType(valueType) :
                        NodeTests.IsSetAccessorDeclaration(node) ? CreateSetterFunctionType(valueType) :
                        valueType;

                    var contextType = CreateClassMemberDecoratorContextTypeForNode(node, thisType, valueType);

                    // We also wrap the "output type", as needed.
                    var returnType = NodeTests.IsGetAccessorDeclaration(node) ? CreateGetterFunctionType(valueType) :
                        NodeTests.IsSetAccessorDeclaration(node) ? CreateSetterFunctionType(valueType) :
                        valueType;

                    links.DecoratorSignature = CreateESDecoratorCallSignature(targetType, contextType, returnType);
                    break;
                }

                case SyntaxKind.PropertyDeclaration:
                {
                    var node = parent;
                    if (!Utilities.IsClassLike(node.Parent!))
                    {
                        break;
                    }

                    // Field decorators have a `context` of `ClassFieldDecoratorContext<This, Value>` and auto-accessor
                    // decorators `ClassAccessorDecoratorContext<This, Value>`; `This` is the "final type" of either the
                    // class or instance, `Value` the "final type" of the value stored in the field.
                    var valueType = GetTypeOfNode(node);
                    var thisType = Utilities.HasStaticModifier(node)
                        ? GetTypeOfSymbol(GetSymbolOfDeclaration((IDeclaration)node.Parent!))
                        : GetDeclaredTypeOfClassOrInterface(GetSymbolOfDeclaration((IDeclaration)node.Parent!));

                    // The `target` of an auto-accessor decorator is a `{ get, set }` object; the `target` of a regular
                    // field decorator is always `undefined` as it isn't installed until it is initialized.
                    var targetType = Utilities.HasAccessorModifier(node)
                        ? CreateClassAccessorDecoratorTargetType(thisType, valueType)
                        : _undefinedType;

                    var contextType = CreateClassMemberDecoratorContextTypeForNode(node, thisType, valueType);

                    // For auto-accessors we wrap the "output type" in a `ClassAccessorDecoratorResult<This, In, Out>`
                    // type, which allows mutation of the runtime-generated getter and setter as well as the injection of
                    // an initializer mutator; for regular fields we wrap it in an initializer mutator.
                    var returnType = Utilities.HasAccessorModifier(node)
                        ? CreateClassAccessorDecoratorResultType(thisType, valueType)
                        : CreateClassFieldDecoratorInitializerMutatorType(thisType, valueType);

                    links.DecoratorSignature = CreateESDecoratorCallSignature(targetType, contextType, returnType);
                    break;
                }
            }
        }
        return ReferenceEquals(links.DecoratorSignature, _anySignature) ? null : links.DecoratorSignature;
    }

    /// <summary>ported from: src/compiler/checker.ts:39022-39126 — <c>getLegacyDecoratorCallSignature</c>.</summary>
    internal ISignature? GetLegacyDecoratorCallSignature(Decorator decorator)
    {
        var parent = decorator.Parent!;
        var links = GetNodeLinks(parent);
        if (links.DecoratorSignature is null)
        {
            links.DecoratorSignature = _anySignature;
            switch (parent.Kind)
            {
                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.ClassExpression:
                {
                    // For a class decorator, the `target` is the type of the class (e.g. the "static" or "constructor"
                    // side of the class).
                    var targetType = GetTypeOfSymbol(GetSymbolOfDeclaration((IDeclaration)parent));
                    var targetParam = CreateParameter("target", targetType);
                    links.DecoratorSignature = CreateCallSignature(
                        /*typeParameters*/ null,
                        /*thisParameter*/ null,
                        [targetParam],
                        GetUnionType([targetType, _voidType]));
                    break;
                }
                case SyntaxKind.Parameter:
                {
                    var node = (ParameterDeclaration)parent;
                    if (
                        !NodeTests.IsConstructorDeclaration(node.Parent!)
                        && !(NodeTests.IsMethodDeclaration(node.Parent!) || NodeTests.IsSetAccessorDeclaration(node.Parent!) && Utilities.IsClassLike(node.Parent!.Parent!))
                    )
                    {
                        break;
                    }

                    if (ReferenceEquals(Utilities.GetThisParameter(node.Parent!), node))
                    {
                        break;
                    }

                    var index = Utilities.GetThisParameter(node.Parent!) is not null
                        ? ((IFunctionLikeDeclarationBase)node.Parent!).Parameters!.IndexOf(node) - 1
                        : ((IFunctionLikeDeclarationBase)node.Parent!).Parameters!.IndexOf(node);
                    Debug.Assert(index >= 0);

                    // A parameter declaration decorator will have three arguments (see `ParameterDecorator` in core.d.ts).
                    var targetType = NodeTests.IsConstructorDeclaration(node.Parent!)
                        ? GetTypeOfSymbol(GetSymbolOfDeclaration((IDeclaration)node.Parent!.Parent!))
                        : GetParentTypeOfClassElement(node.Parent!);

                    var keyType = NodeTests.IsConstructorDeclaration(node.Parent!)
                        ? _undefinedType
                        : GetClassElementPropertyKeyType(node.Parent!);

                    var indexType = GetNumberLiteralType(index);

                    var targetParam = CreateParameter("target", targetType);
                    var keyParam = CreateParameter("propertyKey", keyType);
                    var indexParam = CreateParameter("parameterIndex", indexType);
                    links.DecoratorSignature = CreateCallSignature(
                        /*typeParameters*/ null,
                        /*thisParameter*/ null,
                        [targetParam, keyParam, indexParam],
                        _voidType);
                    break;
                }
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                case SyntaxKind.PropertyDeclaration:
                {
                    var node = parent;
                    if (!Utilities.IsClassLike(node.Parent!))
                    {
                        break;
                    }

                    // A method or accessor declaration decorator will have either two or three arguments (see
                    // `PropertyDecorator` and `MethodDecorator` in core.d.ts).
                    var targetType = GetParentTypeOfClassElement(node);
                    var targetParam = CreateParameter("target", targetType);

                    var keyType = GetClassElementPropertyKeyType(node);
                    var keyParam = CreateParameter("propertyKey", keyType);

                    var returnType = NodeTests.IsPropertyDeclaration(node)
                        ? _voidType
                        : CreateTypedPropertyDescriptorType(GetTypeOfNode(node));

                    var hasPropDesc = !NodeTests.IsPropertyDeclaration(parent) || Utilities.HasAccessorModifier(parent);
                    if (hasPropDesc)
                    {
                        var descriptorType = CreateTypedPropertyDescriptorType(GetTypeOfNode(node));
                        var descriptorParam = CreateParameter("descriptor", descriptorType);
                        links.DecoratorSignature = CreateCallSignature(
                            /*typeParameters*/ null,
                            /*thisParameter*/ null,
                            [targetParam, keyParam, descriptorParam],
                            GetUnionType([returnType, _voidType]));
                    }
                    else
                    {
                        links.DecoratorSignature = CreateCallSignature(
                            /*typeParameters*/ null,
                            /*thisParameter*/ null,
                            [targetParam, keyParam],
                            GetUnionType([returnType, _voidType]));
                    }
                    break;
                }
            }
        }
        return ReferenceEquals(links.DecoratorSignature, _anySignature) ? null : links.DecoratorSignature;
    }

    /// <summary>ported from: src/compiler/checker.ts:39128-39131 — <c>getDecoratorCallSignature</c>.</summary>
    internal ISignature? GetDecoratorCallSignature(Decorator decorator)
        => _legacyDecorators
            ? GetLegacyDecoratorCallSignature(decorator)
            : GetESDecoratorCallSignature(decorator);

    // ---------------------------------------------------------------------------------------------
    // Promise and `new.target` types, and the return type inferred from a body (checker.ts:39133-39297)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:39133-39144 — <c>createPromiseType</c>.</summary>
    internal IType CreatePromiseType(IType promisedType)
    {
        // creates a `Promise<T>` type where `T` is the promisedType argument
        var globalPromiseType = GetGlobalPromiseType(/*reportErrors*/ true);
        if (!ReferenceEquals(globalPromiseType, _emptyGenericType))
        {
            // if the promised type is itself a promise, get the underlying type; otherwise, fallback to the promised type
            // Unwrap an `Awaited<T>` to `T` to improve inference.
            promisedType = GetAwaitedTypeNoAlias(UnwrapAwaitedType(promisedType)) ?? _unknownType;
            return CreateTypeReference(globalPromiseType, [promisedType]);
        }

        return _unknownType;
    }

    /// <summary>ported from: src/compiler/checker.ts:39146-39157 — <c>createPromiseLikeType</c>.</summary>
    internal IType CreatePromiseLikeType(IType promisedType)
    {
        // creates a `PromiseLike<T>` type where `T` is the promisedType argument
        var globalPromiseLikeType = GetGlobalPromiseLikeType(/*reportErrors*/ true);
        if (!ReferenceEquals(globalPromiseLikeType, _emptyGenericType))
        {
            // if the promised type is itself a promise, get the underlying type; otherwise, fallback to the promised type
            // Unwrap an `Awaited<T>` to `T` to improve inference.
            promisedType = GetAwaitedTypeNoAlias(UnwrapAwaitedType(promisedType)) ?? _unknownType;
            return CreateTypeReference(globalPromiseLikeType, [promisedType]);
        }

        return _unknownType;
    }

    /// <summary>ported from: src/compiler/checker.ts:39159-39180 — <c>createPromiseReturnType</c>.</summary>
    internal IType CreatePromiseReturnType(Node func, IType promisedType)
    {
        var promiseType = CreatePromiseType(promisedType);
        if (ReferenceEquals(promiseType, _unknownType))
        {
            Error(
                func,
                Utilities.IsImportCall(func)
                    ? Diagnostics.A_dynamic_import_call_returns_a_Promise_Make_sure_you_have_a_declaration_for_Promise_or_include_ES2015_in_your_lib_option
                    : Diagnostics.An_async_function_or_method_must_return_a_Promise_Make_sure_you_have_a_declaration_for_Promise_or_include_ES2015_in_your_lib_option);
            return _errorType;
        }
        else if (GetGlobalPromiseConstructorSymbol(/*reportErrors*/ true) is null)
        {
            Error(
                func,
                Utilities.IsImportCall(func)
                    ? Diagnostics.A_dynamic_import_call_in_ES5_requires_the_Promise_constructor_Make_sure_you_have_a_declaration_for_the_Promise_constructor_or_include_ES2015_in_your_lib_option
                    : Diagnostics.An_async_function_or_method_in_ES5_requires_the_Promise_constructor_Make_sure_you_have_a_declaration_for_the_Promise_constructor_or_include_ES2015_in_your_lib_option);
        }

        return promiseType;
    }

    /// <summary>ported from: src/compiler/checker.ts:39182-39193 — <c>createNewTargetExpressionType</c>.</summary>
    internal IType CreateNewTargetExpressionType(IType targetType)
    {
        // Create a synthetic type `NewTargetExpression { target: TargetType; }`
        var symbol = CreateSymbol(SymbolFlags.None, "NewTargetExpression");

        var targetPropertySymbol = CreateSymbol(SymbolFlags.Property, "target", CheckFlags.Readonly);
        targetPropertySymbol.Parent = symbol;
        GetSymbolLinks(targetPropertySymbol).Type = targetType;

        var members = Utilities.CreateSymbolTable([targetPropertySymbol]);
        symbol.Members = members;
        return CreateAnonymousType(symbol, members, [], [], []);
    }

    /// <summary>ported from: src/compiler/checker.ts:39195-39297 — <c>getReturnTypeFromBody</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference shadows <c>returnType</c> inside the <c>types.length === 0</c> branch
    /// (checker.ts:39237); C# forbids shadowing a local, so that local is named <c>noReturnType</c>.
    /// </remarks>
    internal IType GetReturnTypeFromBody(INode func, CheckMode? checkMode = null)
    {
        var declaration = (IFunctionLikeDeclarationBase)func;
        if (declaration.Body is null)
        {
            return _errorType;
        }

        var functionFlags = Utilities.GetFunctionFlags((Node)declaration);
        var isAsync = (functionFlags & FunctionFlags.Async) != 0;
        var isGenerator = (functionFlags & FunctionFlags.Generator) != 0;

        IType? returnType = null;
        IType? yieldType = null;
        IType? nextType = null;
        IType fallbackReturnType = _voidType;
        if (declaration.Body.Kind != SyntaxKind.Block) // Async or normal arrow function
        {
            returnType = CheckExpressionCached((Expression)declaration.Body, (checkMode ?? CheckMode.Normal) & ~CheckMode.SkipGenericFunctions);
            if (IsConstContext((Expression)declaration.Body))
            {
                returnType = GetRegularTypeOfLiteralType(returnType);
            }
            if (isAsync)
            {
                // From within an async function you can return either a non-promise value or a promise. Any
                // Promise/A+ compatible implementation will always assimilate any foreign promise, so the
                // return type of the body should be unwrapped to its awaited type, which we will wrap in
                // the native Promise<T> type later in this function.
                returnType = UnwrapAwaitedType(CheckAwaitedType(returnType, /*withAlias*/ false, (Node)declaration, Diagnostics.The_return_type_of_an_async_function_must_either_be_a_valid_promise_or_must_not_contain_a_callable_then_member));
            }
        }
        else if (isGenerator) // Generator or AsyncGenerator function
        {
            var returnTypes = CheckAndAggregateReturnExpressionTypes(declaration, checkMode);
            if (returnTypes is null)
            {
                fallbackReturnType = _neverType;
            }
            else if (returnTypes.Count > 0)
            {
                returnType = GetUnionType(returnTypes, UnionReduction.Subtype);
            }
            var (yieldTypes, nextTypes) = CheckAndAggregateYieldOperandTypes(declaration, checkMode);
            yieldType = Core.Some(yieldTypes) ? GetUnionType(yieldTypes, UnionReduction.Subtype) : null;
            nextType = Core.Some(nextTypes) ? GetIntersectionType(nextTypes) : null;
        }
        else // Async or normal function
        {
            var types = CheckAndAggregateReturnExpressionTypes(declaration, checkMode);
            if (types is null)
            {
                // For an async function, the return type will not be never, but rather a Promise for never.
                return (functionFlags & FunctionFlags.Async) != 0
                    ? CreatePromiseReturnType((Node)declaration, _neverType) // Async function
                    : _neverType; // Normal function
            }
            if (types.Count == 0)
            {
                // For an async function, the return type will not be void/undefined, but rather a Promise for void/undefined.
                var contextualReturnType = GetContextualReturnType((Node)declaration, /*contextFlags*/ null);
                var noReturnType = contextualReturnType is not null && SomeType(UnwrapReturnType(contextualReturnType, functionFlags) ?? _voidType, t => (t.Flags & TypeFlags.Undefined) != 0) ? _undefinedType : _voidType;
                return (functionFlags & FunctionFlags.Async) != 0
                    ? CreatePromiseReturnType((Node)declaration, noReturnType) // Async function
                    : noReturnType; // Normal function
            }

            // Return a union of the return expression types.
            returnType = GetUnionType(types, UnionReduction.Subtype);
        }

        if (returnType is not null || yieldType is not null || nextType is not null)
        {
            if (yieldType is not null) ReportErrorsFromWidening((IDeclaration)declaration, yieldType, WideningKind.GeneratorYield);
            if (returnType is not null) ReportErrorsFromWidening((IDeclaration)declaration, returnType, WideningKind.FunctionReturn);
            if (nextType is not null) ReportErrorsFromWidening((IDeclaration)declaration, nextType, WideningKind.GeneratorNext);
            if (
                returnType is not null && IsUnitType(returnType) ||
                yieldType is not null && IsUnitType(yieldType) ||
                nextType is not null && IsUnitType(nextType)
            )
            {
                var contextualSignature = GetContextualSignatureForFunctionLikeDeclaration((Node)declaration);
                var contextualType = contextualSignature is null ? null :
                    ReferenceEquals(contextualSignature, GetSignatureFromDeclaration((Node)declaration)) ? isGenerator ? null : returnType :
                    InstantiateContextualType(GetReturnTypeOfSignature(contextualSignature), (Node)declaration, /*contextFlags*/ null);
                if (isGenerator)
                {
                    yieldType = GetWidenedLiteralLikeTypeForContextualIterationTypeIfNeeded(yieldType, contextualType, IterationTypeKind.Yield, isAsync);
                    returnType = GetWidenedLiteralLikeTypeForContextualIterationTypeIfNeeded(returnType, contextualType, IterationTypeKind.Return, isAsync);
                    nextType = GetWidenedLiteralLikeTypeForContextualIterationTypeIfNeeded(nextType, contextualType, IterationTypeKind.Next, isAsync);
                }
                else
                {
                    returnType = GetWidenedLiteralLikeTypeForContextualReturnTypeIfNeeded(returnType, contextualType, isAsync);
                }
            }

            if (yieldType is not null) yieldType = GetWidenedType(yieldType);
            if (returnType is not null) returnType = GetWidenedType(returnType);
            if (nextType is not null) nextType = GetWidenedType(nextType);
        }

        if (isGenerator)
        {
            return CreateGeneratorType(
                yieldType ?? _neverType,
                returnType ?? fallbackReturnType,
                nextType ?? GetContextualIterationType(IterationTypeKind.Next, (Node)declaration) ?? _unknownType,
                isAsync);
        }
        else
        {
            // From within an async function you can return either a non-promise value or a promise. Any
            // Promise/A+ compatible implementation will always assimilate any foreign promise, so the
            // return type of the body is awaited type of the body, wrapped in a native Promise<T> type.
            return isAsync
                ? CreatePromiseType(returnType ?? fallbackReturnType)
                : returnType ?? fallbackReturnType;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Generators, exhaustive switches and the return/yield aggregation (checker.ts:39299-39468)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:39299-39317 — <c>createGeneratorType</c>.</summary>
    internal IType CreateGeneratorType(IType yieldType, IType returnType, IType nextType, bool isAsyncGenerator)
    {
        var resolver = isAsyncGenerator ? AsyncIterationTypesResolver : SyncIterationTypesResolver;
        var globalGeneratorType = resolver.GetGlobalGeneratorType(/*reportErrors*/ false);
        yieldType = resolver.ResolveIterationType(yieldType, /*errorNode*/ null) ?? _unknownType;
        returnType = resolver.ResolveIterationType(returnType, /*errorNode*/ null) ?? _unknownType;
        if (ReferenceEquals(globalGeneratorType, _emptyGenericType))
        {
            // Fall back to the global IterableIterator type.
            var globalIterableIteratorType = resolver.GetGlobalIterableIteratorType(/*reportErrors*/ false);
            if (!ReferenceEquals(globalIterableIteratorType, _emptyGenericType))
            {
                return CreateTypeFromGenericGlobalType(globalIterableIteratorType, [yieldType, returnType, nextType]);
            }

            // The global Generator type doesn't exist, so report an error
            resolver.GetGlobalIterableIteratorType(/*reportErrors*/ true);
            return _emptyObjectType;
        }

        return CreateTypeFromGenericGlobalType(globalGeneratorType, [yieldType, returnType, nextType]);
    }

    /// <summary>ported from: src/compiler/checker.ts:39319-39344 — <c>checkAndAggregateYieldOperandTypes</c>.</summary>
    internal (List<IType> YieldTypes, List<IType> NextTypes) CheckAndAggregateYieldOperandTypes(IFunctionLikeDeclarationBase func, CheckMode? checkMode)
    {
        var yieldTypes = new List<IType>();
        var nextTypes = new List<IType>();
        var isAsync = (Utilities.GetFunctionFlags((Node)func) & FunctionFlags.Async) != 0;
        Utilities.ForEachYieldExpression<object?>((Block)func.Body!, yieldExpression =>
        {
            var yieldExpressionType = yieldExpression.Expression is not null
                ? CheckExpression(yieldExpression.Expression, (checkMode ?? CheckMode.Normal) & ~CheckMode.SkipGenericFunctions)
                : _undefinedWideningType;
            if (yieldExpression.Expression is not null && IsConstContext(yieldExpression.Expression))
            {
                yieldExpressionType = GetRegularTypeOfLiteralType(yieldExpressionType);
            }
            Core.PushIfUnique(yieldTypes, GetYieldedTypeOfYieldExpression(yieldExpression, yieldExpressionType, _anyType, isAsync)!);
            IType? nextType;
            if (yieldExpression.AsteriskToken is not null)
            {
                var iterationTypes = GetIterationTypesOfIterable(
                    yieldExpressionType,
                    isAsync ? IterationUse.AsyncYieldStar : IterationUse.YieldStar,
                    yieldExpression.Expression);
                nextType = iterationTypes?.NextType;
            }
            else
            {
                nextType = GetContextualType(yieldExpression, /*contextFlags*/ null);
            }
            if (nextType is not null)
            {
                Core.PushIfUnique(nextTypes, nextType);
            }
            return null;
        });
        return (yieldTypes, nextTypes);
    }

    /// <summary>ported from: src/compiler/checker.ts:39346-39357 — <c>getYieldedTypeOfYieldExpression</c>.</summary>
    internal IType? GetYieldedTypeOfYieldExpression(YieldExpression node, IType expressionType, IType sentType, bool isAsync)
    {
        var errorNode = node.Expression ?? node;
        // A `yield*` expression effectively yields everything that its operand yields
        var yieldedType = node.AsteriskToken is not null
            ? CheckIteratedTypeOrElementType(isAsync ? IterationUse.AsyncYieldStar : IterationUse.YieldStar, expressionType, sentType, errorNode)
            : expressionType;
        return !isAsync ? yieldedType : GetAwaitedType(
            yieldedType,
            errorNode,
            node.AsteriskToken is not null
                ? Diagnostics.Type_of_iterated_elements_of_a_yield_Asterisk_operand_must_either_be_a_valid_promise_or_must_not_contain_a_callable_then_member
                : Diagnostics.Type_of_yield_operand_in_an_async_generator_must_either_be_a_valid_promise_or_must_not_contain_a_callable_then_member);
    }

    /// <summary>Return the combined not-equal type facts for all cases except those between the start and end indices.</summary>
    /// <remarks>ported from: src/compiler/checker.ts:39360-39367 — <c>getNotEqualFactsFromTypeofSwitch</c>.</remarks>
    internal TypeFacts GetNotEqualFactsFromTypeofSwitch(int start, int end, IList<string?> witnesses)
    {
        var facts = TypeFacts.None;
        for (var i = 0; i < witnesses.Count; i++)
        {
            var witness = i < start || i >= end ? witnesses[i] : null;
            facts |= witness is not null
                ? s_typeofNEFacts.TryGetValue(witness, out var value) && value != TypeFacts.None ? value : TypeFacts.TypeofNEHostObject
                : TypeFacts.None;
        }
        return facts;
    }

    /// <summary>ported from: src/compiler/checker.ts:39369-39382 — <c>isExhaustiveSwitchStatement</c>.</summary>
    internal bool IsExhaustiveSwitchStatement(SwitchStatement node)
    {
        var links = GetNodeLinks(node);
        if (links.IsExhaustive is null)
        {
            links.IsExhaustive = 0; // Indicate resolution is in process
            var exhaustive = ComputeExhaustiveSwitchStatement(node);
            if (links.IsExhaustive is int and 0)
            {
                links.IsExhaustive = exhaustive;
            }
        }
        else if (links.IsExhaustive is int and 0)
        {
            links.IsExhaustive = false; // Resolve circularity to false
        }
        return links.IsExhaustive is true;
    }

    /// <summary>ported from: src/compiler/checker.ts:39384-39409 — <c>computeExhaustiveSwitchStatement</c>.</summary>
    internal bool ComputeExhaustiveSwitchStatement(SwitchStatement node)
    {
        if (node.Expression!.Kind == SyntaxKind.TypeOfExpression)
        {
            var witnesses = GetSwitchClauseTypeOfWitnesses(node);
            if (witnesses is null)
            {
                return false;
            }
            var operandConstraint = GetBaseConstraintOrType(CheckExpressionCached(((TypeOfExpression)node.Expression!).Expression!));
            // Get the not-equal flags for all handled cases.
            var notEqualFacts = GetNotEqualFactsFromTypeofSwitch(0, 0, witnesses);
            if ((operandConstraint.Flags & TypeFlags.AnyOrUnknown) != 0)
            {
                // We special case the top types to be exhaustive when all cases are handled.
                return (TypeFacts.AllTypeofNE & notEqualFacts) == TypeFacts.AllTypeofNE;
            }
            // A missing not-equal flag indicates that the type wasn't handled by some case.
            return !SomeType(operandConstraint, t => GetTypeFacts(t, notEqualFacts) == notEqualFacts);
        }
        var type = GetBaseConstraintOrType(CheckExpressionCached(node.Expression!));
        if (!IsLiteralType(type))
        {
            return false;
        }
        var switchTypes = GetSwitchClauseTypes(node);
        if (switchTypes.Count == 0 || Core.Some(switchTypes, IsNeitherUnitTypeNorNever))
        {
            return false;
        }
        return EachTypeContainedIn(MapType(type, GetRegularTypeOfLiteralType)!, switchTypes);
    }

    /// <summary>ported from: src/compiler/checker.ts:39411-39413 — <c>functionHasImplicitReturn</c>.</summary>
    internal bool FunctionHasImplicitReturn(IFunctionLikeDeclarationBase func)
        => func.EndFlowNode is not null && IsReachableFlowNode(func.EndFlowNode);

    /// <summary>
    /// NOTE: Return value of <c>[]</c> means a different thing than <c>undefined</c>. <c>[]</c> means func returns
    /// <c>void</c>, <c>undefined</c> means it returns <c>never</c>.
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:39416-39468 — <c>checkAndAggregateReturnExpressionTypes</c>.</remarks>
    internal List<IType>? CheckAndAggregateReturnExpressionTypes(IFunctionLikeDeclarationBase func, CheckMode? checkMode)
    {
        var functionFlags = Utilities.GetFunctionFlags((Node)func);
        var aggregatedTypes = new List<IType>();
        var hasReturnWithNoExpression = FunctionHasImplicitReturn(func);
        var hasReturnOfTypeNever = false;
        Utilities.ForEachReturnStatement<object?>((IStatement)func.Body!, returnStatement =>
        {
            var expr = returnStatement.Expression;
            if (expr is not null)
            {
                expr = (Expression)Utilities.SkipParentheses(expr, /*excludeJSDocTypeAssertions*/ true);
                // Bare calls to this same function don't contribute to inference
                // and `return await` is also safe to unwrap here
                if ((functionFlags & FunctionFlags.Async) != 0 && expr.Kind == SyntaxKind.AwaitExpression)
                {
                    expr = (Expression)Utilities.SkipParentheses(((AwaitExpression)expr).Expression!, /*excludeJSDocTypeAssertions*/ true);
                }
                if (
                    expr.Kind == SyntaxKind.CallExpression
                    && ((CallExpression)expr).Expression!.Kind == SyntaxKind.Identifier
                    && ReferenceEquals(CheckExpressionCached(((CallExpression)expr).Expression!).Symbol, GetMergedSymbol(((IDeclaration)func).Symbol))
                    && (!Utilities.IsFunctionExpressionOrArrowFunction((Node)((IDeclaration)func).Symbol!.ValueDeclaration!) || IsConstantReference(((CallExpression)expr).Expression!))
                )
                {
                    hasReturnOfTypeNever = true;
                    return null;
                }

                var type = CheckExpressionCached(expr, (checkMode ?? CheckMode.Normal) & ~CheckMode.SkipGenericFunctions);
                if ((functionFlags & FunctionFlags.Async) != 0)
                {
                    // From within an async function you can return either a non-promise value or a promise. Any
                    // Promise/A+ compatible implementation will always assimilate any foreign promise, so the
                    // return type of the body should be unwrapped to its awaited type, which should be wrapped in
                    // the native Promise<T> type by the caller.
                    type = UnwrapAwaitedType(CheckAwaitedType(type, /*withAlias*/ false, (Node)func, Diagnostics.The_return_type_of_an_async_function_must_either_be_a_valid_promise_or_must_not_contain_a_callable_then_member));
                }
                if ((type.Flags & TypeFlags.Never) != 0)
                {
                    hasReturnOfTypeNever = true;
                }
                Core.PushIfUnique(aggregatedTypes, IsConstContext(expr) ? GetRegularTypeOfLiteralType(type) : type);
            }
            else
            {
                hasReturnWithNoExpression = true;
            }
            return null;
        });
        if (aggregatedTypes.Count == 0 && !hasReturnWithNoExpression && (hasReturnOfTypeNever || MayReturnNever(func)))
        {
            return null;
        }
        if (
            _strictNullChecks && aggregatedTypes.Count > 0 && hasReturnWithNoExpression
            && !(IsJSConstructor((Node)func) && Core.Some(aggregatedTypes, t => ReferenceEquals(t.Symbol, ((IDeclaration)func).Symbol)))
        )
        {
            // Javascript "callable constructors", containing eg `if (!(this instanceof A)) return new A()` should not add undefined
            Core.PushIfUnique(aggregatedTypes, _undefinedType);
        }
        return aggregatedTypes;
    }

    /// <summary>ported from: src/compiler/checker.ts:39469-39479 — <c>mayReturnNever</c>.</summary>
    internal bool MayReturnNever(IFunctionLikeDeclarationBase func)
    {
        switch (func.Kind)
        {
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ArrowFunction:
                return true;
            case SyntaxKind.MethodDeclaration:
                return ((Node)func).Parent!.Kind == SyntaxKind.ObjectLiteralExpression;
            default:
                return false;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The inferred type predicate and the return/throw path check (checker.ts:39481-39600)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:39481-39504 — <c>getTypePredicateFromBody</c>.</summary>
    internal ITypePredicateBase? GetTypePredicateFromBody(Node func)
    {
        switch (func.Kind)
        {
            case SyntaxKind.Constructor:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
                return null;
        }
        var declaration = (IFunctionLikeDeclarationBase)func;
        var functionFlags = Utilities.GetFunctionFlags(func);
        if (functionFlags != FunctionFlags.Normal)
        {
            return null;
        }

        // Only attempt to infer a type predicate if there's exactly one return.
        Expression? singleReturn = null;
        if (declaration.Body is not null && declaration.Body.Kind != SyntaxKind.Block)
        {
            singleReturn = (Expression)declaration.Body; // arrow function
        }
        else
        {
            var bailedEarly = Utilities.ForEachReturnStatement<object?>((IStatement)declaration.Body!, returnStatement =>
            {
                if (singleReturn is not null || returnStatement.Expression is null)
                {
                    return new object();
                }
                singleReturn = returnStatement.Expression;
                return null;
            });
            if (bailedEarly is not null || singleReturn is null || FunctionHasImplicitReturn(declaration))
            {
                return null;
            }
        }
        return CheckIfExpressionRefinesAnyParameter(declaration, singleReturn);
    }

    /// <summary>ported from: src/compiler/checker.ts:39506-39522 — <c>checkIfExpressionRefinesAnyParameter</c>.</summary>
    internal ITypePredicateBase? CheckIfExpressionRefinesAnyParameter(IFunctionLikeDeclarationBase func, Expression expr)
    {
        expr = (Expression)Utilities.SkipParentheses(expr, /*excludeJSDocTypeAssertions*/ true);
        var returnType = CheckExpressionCached(expr);
        if ((returnType.Flags & TypeFlags.Boolean) == 0)
        {
            return null;
        }

        return Core.ForEach<ParameterDeclaration, ITypePredicateBase>(func.Parameters, (param, i) =>
        {
            var initType = GetTypeOfSymbol(((IDeclaration)param).Symbol!);
            if ((initType.Flags & TypeFlags.Boolean) != 0 || !NodeTests.IsIdentifier(param.Name!) || IsSymbolAssigned(((IDeclaration)param).Symbol!) || Utilities.IsRestParameter(param))
            {
                // Refining "x: boolean" to "x is true" or "x is false" isn't useful.
                return null;
            }
            var trueType = CheckIfExpressionRefinesParameter(func, expr, param, initType);
            if (trueType is not null)
            {
                return CreateTypePredicate(TypePredicateKind.Identifier, Utilities.UnescapeLeadingUnderscores(((Identifier)param.Name!).EscapedText!), i, trueType);
            }
            return null;
        });
    }

    /// <summary>ported from: src/compiler/checker.ts:39524-39538 — <c>checkIfExpressionRefinesParameter</c>.</summary>
    internal IType? CheckIfExpressionRefinesParameter(IFunctionLikeDeclarationBase func, Expression expr, ParameterDeclaration param, IType initType)
    {
        FlowNode? antecedent;
        if (Utilities.CanHaveFlowNode(expr) && ((IFlowContainer)expr).FlowNode is not null)
        {
            antecedent = ((IFlowContainer)expr).FlowNode;
        }
        else if (expr.Parent is ReturnStatement returnStatement && returnStatement.FlowNode is not null)
        {
            antecedent = returnStatement.FlowNode;
        }
        else
        {
            antecedent = FlowNode.CreateFlowNode(FlowFlags.Start, /*node*/ null, /*antecedent*/ null);
        }
        var trueCondition = FlowNode.CreateFlowNode(FlowFlags.TrueCondition, expr, antecedent);

        var trueType = GetFlowTypeOfReference(param.Name!, initType, initType, (Node)func, trueCondition);
        if (ReferenceEquals(trueType, initType))
        {
            return null;
        }

        // "x is T" means that x is T if and only if it returns true. If it returns false then x is not T.
        // This means that if the function is called with an argument of type trueType, there can't be anything left in the `else` branch. It must reduce to `never`.
        var falseCondition = FlowNode.CreateFlowNode(FlowFlags.FalseCondition, expr, antecedent);
        var falseSubtype = GetReducedType(GetFlowTypeOfReference(param.Name!, initType, trueType, (Node)func, falseCondition));
        return (falseSubtype.Flags & TypeFlags.Never) != 0 ? trueType : null;
    }

    /// <summary>
    /// TypeScript Specification 1.0 (6.3) - July 2014
    ///   An explicitly typed function whose return type isn't the Void type,
    ///   the Any type, or a union type containing the Void or Any type as a constituent
    ///   must have at least one return statement somewhere in its body.
    ///   An exception to this rule is if the function implementation consists of a single 'throw' statement.
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:39549-39599 — <c>checkAllCodePathsInNonVoidFunctionReturnOrThrow</c>.</remarks>
    internal void CheckAllCodePathsInNonVoidFunctionReturnOrThrow(Node func, IType? returnType)
    {
        AddLazyDiagnostic(CheckAllCodePathsInNonVoidFunctionReturnOrThrowDiagnostics);
        return;

        void CheckAllCodePathsInNonVoidFunctionReturnOrThrowDiagnostics()
        {
            var functionFlags = Utilities.GetFunctionFlags(func);
            var type = returnType is not null ? UnwrapReturnType(returnType, functionFlags) : null;

            // Functions with an explicitly specified return type that includes `void` or is exactly `any` or `undefined` don't
            // need any return statements.
            if (type is not null && (MaybeTypeOfKind(type, TypeFlags.Void) || (type.Flags & (TypeFlags.Any | TypeFlags.Undefined)) != 0))
            {
                return;
            }

            // If all we have is a function signature, or an arrow function with an expression body, then there is nothing to check.
            // also if HasImplicitReturn flag is not set this means that all codepaths in function body end with return or throw
            if (func.Kind == SyntaxKind.MethodSignature || Utilities.NodeIsMissing(((IFunctionLikeDeclarationBase)func).Body) || ((IFunctionLikeDeclarationBase)func).Body!.Kind != SyntaxKind.Block || !FunctionHasImplicitReturn((IFunctionLikeDeclarationBase)func))
            {
                return;
            }

            var hasExplicitReturn = (func.Flags & NodeFlags.HasExplicitReturn) != 0;
            var errorNode = Utilities.GetEffectiveReturnTypeNode(func) as Node ?? func;

            if (type is not null && (type.Flags & TypeFlags.Never) != 0)
            {
                Error((Node)errorNode, Diagnostics.A_function_returning_never_cannot_have_a_reachable_end_point);
            }
            else if (type is not null && !hasExplicitReturn)
            {
                // minimal check: function has syntactic return type annotation and no explicit return statements in the body
                // this function does not conform to the specification.
                Error((Node)errorNode, Diagnostics.A_function_whose_declared_type_is_neither_undefined_void_nor_any_must_return_a_value);
            }
            else if (type is not null && _strictNullChecks && !IsTypeAssignableTo(_undefinedType, type))
            {
                Error((Node)errorNode, Diagnostics.Function_lacks_ending_return_statement_and_return_type_does_not_include_undefined);
            }
            else if (Js.IsTruthy(_compilerOptions.NoImplicitReturns))
            {
                if (type is null)
                {
                    // If return type annotation is omitted check if function has any explicit return statements.
                    // If it does not have any - its inferred return type is void - don't do any checks.
                    // Otherwise get inferred return type from function body and report error only if it is not void / anytype
                    if (!hasExplicitReturn)
                    {
                        return;
                    }
                    var inferredReturnType = GetReturnTypeOfSignature(GetSignatureFromDeclaration(func));
                    if (IsUnwrappedReturnTypeUndefinedVoidOrAny(func, inferredReturnType))
                    {
                        return;
                    }
                }
                Error((Node)errorNode, Diagnostics.Not_all_code_paths_return_a_value);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:54252-54254 — <c>signatureHasRestParameter</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference declares this at the end of <c>checker.ts</c>, outside <c>createTypeChecker</c> and
    /// therefore outside every band, and no landed file declares it; the split task assigned it to this slice, whose
    /// parameter helpers read it at nine sites.
    /// </remarks>
    internal static bool SignatureHasRestParameter(ISignature s) => (s.Flags & SignatureFlags.HasRestParameter) != 0;
}
