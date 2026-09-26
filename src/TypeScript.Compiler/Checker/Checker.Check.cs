namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:33262-33303 (checkGrammarRegularExpressionLiteral, checkRegularExpressionLiteral)
// ported from: src/compiler/checker.ts:33304-33328 (checkSpreadExpression … isSpreadIntoCallOrNew)
// ported from: src/compiler/checker.ts:33329-33526 (checkArrayLiteral … getImmediateAliasedSymbol)
// ported from: src/compiler/checker.ts:33527-33741 (checkObjectLiteral, isValidSpreadType)
// ported from: src/compiler/checker.ts:33742-34079 (checkJsxSelfClosingElement … getJsxNamespaceContainerForImplicitImport)
// ported from: src/compiler/checker.ts:34080-34341 (getJsxNamespaceAt … checkJsxPreconditions)
// ported from: src/compiler/checker.ts:34342-34437 (checkJsxOpeningLikeElementOrOpeningFragment … checkJsxExpression)
// ported from: src/compiler/checker.ts:34438-34739 (getDeclarationNodeFlagsFromSymbol … checkNonNullNonVoidType)
/// <summary>
/// The expression checks proper: regular expressions and spread, array and object literals, the JSX family, and the
/// null/undefined preconditions of expression positions.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS's <c>JsxCallLike</c> (<c>JsxOpeningElement | JsxSelfClosingElement | JsxOpeningFragment</c>) and its
/// other node unions have no port counterpart, so every member that takes one takes <see cref="Node"/> and casts to the
/// class the reference's narrowing selected. The three landed call sites of <c>GetEffectiveFirstArgumentForJsxSignature</c>
/// (<c>Checker/Checker.CallResolution.cs:419</c>, <c>:758</c> and <c>Checker/Checker.Inference.cs:1804</c>) pass a
/// <see cref="Node"/>, which fixes that choice for the JSX signature helpers.
/// </para>
/// <para>
/// PORT-NOTE: <c>__String</c> is erased to <see cref="string"/>, so the JSX attribute/name helpers deal in
/// <c>string</c>/<c>string?</c>. The two model link slots TS types <c>Symbol | false</c> are <c>object?</c>
/// (Model/NodeLinks.cs:82-93 with its PORT-NOTE), so <c>getJsxNamespaceContainerForImplicitImport</c> and
/// <c>getJsxNamespaceAt</c> unwrap the boxed <c>false</c> themselves.
/// </para>
/// <para>
/// PORT-NOTE: TS's <c>LanguageFeatureMinimumTarget</c> (types.ts:8488) has no port counterpart — the same note is
/// stated in <c>Checker.Check.Classes.cs</c> — so the two downlevel tests of this band compare against the target the
/// table holds: <c>SpreadElements</c> and <c>ObjectAssign</c> are both <c>ScriptTarget.ES2015</c>.
/// </para>
/// <para>
/// PORT-NOTE: <c>pragmas.has("jsx")</c> (:33781) is a lookup in the port's map (<c>SourceFile.Pragmas?.Get("jsx")</c>),
/// as <c>Utilities.GetJSXImplicitImportBase</c> already does at <c>Program/Utilities.Program.Nodes.cs:478</c>.
/// </para>
/// <para>
/// PORT-NOTE: an assignment the reference makes through a null-conditional call (<c>map?.set(k, v)</c>, :33587, :33870,
/// :33877) is an <c>if</c> in C#, which has no null-conditional assignment.
/// </para>
/// <para>
/// PORT-NOTE: <c>Debug.fail()</c> in return position is <c>Debug.Fail&lt;T&gt;()</c> (<c>Core/Debug.cs:120-125</c>), the
/// port's generic form, because <c>Debug.Fail()</c> is <c>void</c>.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ported from: src/compiler/checker.ts:54223-54235 — the module-scope `namespace JsxNames`
    // ported from: src/compiler/checker.ts:54236-54238 — the module-scope `namespace ReactNames`
    /// <summary>
    /// The names of the JSX namespace's members and of the classic runtime's fragment member.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS declares these two as module-scope <c>namespace</c>s of checker.ts, which carry no checker state;
    /// the port states them as nested static classes, the same treatment <c>_anon</c> and the other module-scope
    /// constants of checker.ts get (<c>Checker/Checker.cs:150-155</c>).
    /// </remarks>
    internal static class JsxNames
    {
        /// <summary>ported from: src/compiler/checker.ts:54224 — <c>JsxNames.JSX</c>.</summary>
        internal const string JSX = "JSX";

        /// <summary>ported from: src/compiler/checker.ts:54225 — <c>JsxNames.IntrinsicElements</c>.</summary>
        internal const string IntrinsicElements = "IntrinsicElements";

        /// <summary>ported from: src/compiler/checker.ts:54226 — <c>JsxNames.ElementClass</c>.</summary>
        internal const string ElementClass = "ElementClass";

        /// <summary>ported from: src/compiler/checker.ts:54227 — <c>JsxNames.ElementAttributesPropertyNameContainer</c>.</summary>
        internal const string ElementAttributesPropertyNameContainer = "ElementAttributesProperty";

        /// <summary>ported from: src/compiler/checker.ts:54228 — <c>JsxNames.ElementChildrenAttributeNameContainer</c>.</summary>
        internal const string ElementChildrenAttributeNameContainer = "ElementChildrenAttribute";

        /// <summary>ported from: src/compiler/checker.ts:54229 — <c>JsxNames.Element</c>.</summary>
        internal const string Element = "Element";

        /// <summary>ported from: src/compiler/checker.ts:54230 — <c>JsxNames.ElementType</c>.</summary>
        internal const string ElementType = "ElementType";

        /// <summary>ported from: src/compiler/checker.ts:54231 — <c>JsxNames.IntrinsicAttributes</c>.</summary>
        internal const string IntrinsicAttributes = "IntrinsicAttributes";

        /// <summary>ported from: src/compiler/checker.ts:54232 — <c>JsxNames.IntrinsicClassAttributes</c>.</summary>
        internal const string IntrinsicClassAttributes = "IntrinsicClassAttributes";

        /// <summary>ported from: src/compiler/checker.ts:54233 — <c>JsxNames.LibraryManagedAttributes</c>.</summary>
        internal const string LibraryManagedAttributes = "LibraryManagedAttributes";
    }

    /// <summary>ported from: src/compiler/checker.ts:54236-54238 — <c>ReactNames</c>.</summary>
    internal static class ReactNames
    {
        /// <summary>ported from: src/compiler/checker.ts:54237 — <c>ReactNames.Fragment</c>.</summary>
        internal const string Fragment = "Fragment";
    }

    // ---------------------------------------------------------------------------------------------
    // Regular expressions and spread (checker.ts:33262-33328)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:33262-33294 — <c>checkGrammarRegularExpressionLiteral</c>.</summary>
    internal bool CheckGrammarRegularExpressionLiteral(RegularExpressionLiteral node)
    {
        var sourceFile = Utilities.GetSourceFileOfNode(node)!;
        if (!HasParseDiagnostics(sourceFile) && node.IsUnterminated != true)
        {
            Diagnostic? lastError = null;
            _scanner ??= Scanner.Create(ScriptTarget.ESNext, /*skipTrivia*/ true);
            _scanner.SetScriptTarget(sourceFile.LanguageVersion);
            _scanner.SetLanguageVariant(sourceFile.LanguageVariant);
            _scanner.SetOnError((message, length, arg0) =>
            {
                // For providing spelling suggestions
                var start = _scanner!.GetTokenEnd();
                if (message.Category == DiagnosticCategory.Message && lastError is not null && start == lastError.Start && length == lastError.Length)
                {
                    var error = Utilities.CreateDetachedDiagnostic(sourceFile.FileName!, sourceFile.Text!, start, length, message, arg0);
                    Utilities.AddRelatedInfo(lastError, error);
                }
                else if (lastError is null || start != lastError.Start)
                {
                    lastError = Utilities.CreateFileDiagnostic(sourceFile, start, length, message, arg0);
                    _diagnostics.Add(lastError);
                }
            });
            _scanner.SetText(sourceFile.Text, node.Pos, node.End - node.Pos);
            try
            {
                _scanner.Scan();
                Debug.Assert(_scanner.ReScanSlashToken(/*reportErrors*/ true) == SyntaxKind.RegularExpressionLiteral, "Expected scanner to rescan RegularExpressionLiteral");
                return lastError is not null;
            }
            finally
            {
                _scanner.SetText("", null, null);
                _scanner.SetOnError(/*onError*/ null);
            }
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:33295-33303 — <c>checkRegularExpressionLiteral</c>.</summary>
    internal IType CheckRegularExpressionLiteral(RegularExpressionLiteral node)
    {
        var nodeLinks = GetNodeLinks(node);
        if ((nodeLinks.Flags & NodeCheckFlags.TypeChecked) == 0)
        {
            nodeLinks.Flags |= NodeCheckFlags.TypeChecked;
            AddLazyDiagnostic(() => CheckGrammarRegularExpressionLiteral(node));
        }
        return _globalRegExpType;
    }

    /// <summary>ported from: src/compiler/checker.ts:33304-33312 — <c>checkSpreadExpression</c>.</summary>
    internal IType CheckSpreadExpression(SpreadElement node, CheckMode? checkMode = null)
    {
        if (_languageVersion < ScriptTarget.ES2015)
        {
            CheckExternalEmitHelpers(node, Js.IsTruthy(_compilerOptions.DownlevelIteration) ? ExternalEmitHelpers.SpreadIncludes : ExternalEmitHelpers.SpreadArray);
        }

        var arrayOrIterableType = CheckExpression(node.Expression!, checkMode ?? CheckMode.Normal);
        return CheckIteratedTypeOrElementType(IterationUse.Spread, arrayOrIterableType, _undefinedType, node.Expression!);
    }

    /// <summary>ported from: src/compiler/checker.ts:33313-33316 — <c>checkSyntheticExpression</c>.</summary>
    internal IType CheckSyntheticExpression(SyntheticExpression node)
        => node.IsSpread ? GetIndexedAccessType(node.Type!, _numberType) : node.Type!;

    /// <summary>ported from: src/compiler/checker.ts:33317-33323 — <c>hasDefaultValue</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>BindingElement | ObjectLiteralElementLike | Expression</c>, which the
    /// port erases to <see cref="Node"/>; each arm casts to the class its <c>kind ===</c> test selected.
    /// </remarks>
    internal bool HasDefaultValue(Node node)
        => node.Kind == SyntaxKind.BindingElement && ((BindingElement)node).Initializer is not null ||
            node.Kind == SyntaxKind.PropertyAssignment && HasDefaultValue(((PropertyAssignment)node).Initializer!) ||
            node.Kind == SyntaxKind.ShorthandPropertyAssignment && ((ShorthandPropertyAssignment)node).ObjectAssignmentInitializer is not null ||
            node.Kind == SyntaxKind.BinaryExpression && ((BinaryExpression)node).OperatorToken!.Kind == SyntaxKind.EqualsToken;

    /// <summary>ported from: src/compiler/checker.ts:33324-33328 — <c>isSpreadIntoCallOrNew</c>.</summary>
    internal static bool IsSpreadIntoCallOrNew(ArrayLiteralExpression node)
    {
        var parent = Utilities.WalkUpParenthesizedExpressions(node.Parent!);
        return NodeTests.IsSpreadElement(parent) && Utilities.IsCallOrNewExpression(parent!.Parent!);
    }

    // ---------------------------------------------------------------------------------------------
    // Array literals (checker.ts:33329-33418)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:33329-33406 — <c>checkArrayLiteral</c>.</summary>
    internal IType CheckArrayLiteral(ArrayLiteralExpression node, CheckMode? checkMode = null, bool forceTuple = false)
    {
        var elements = node.Elements!;
        var elementCount = elements.Count;
        var elementTypes = new List<IType>();
        var elementFlags = new List<ElementFlags>();
        PushCachedContextualType(node);
        var inDestructuringPattern = Utilities.IsAssignmentTarget(node);
        var inConstContext = IsConstContext(node);
        var contextualType = GetApparentTypeOfContextualType(node, /*contextFlags*/ null);
        var inTupleContext = IsSpreadIntoCallOrNew(node) || contextualType is not null && SomeType(contextualType, t => IsTupleLikeType(t) || IsGenericMappedType(t) && ((IMappedType)t).NameType is null && GetHomomorphicTypeVariable((IMappedType)(((IMappedType)t).Target ?? t)) is not null);

        var hasOmittedExpression = false;
        for (var i = 0; i < elementCount; i++)
        {
            var e = elements[i];
            if (e.Kind == SyntaxKind.SpreadElement)
            {
                if (_languageVersion < ScriptTarget.ES2015)
                {
                    CheckExternalEmitHelpers(e, Js.IsTruthy(_compilerOptions.DownlevelIteration) ? ExternalEmitHelpers.SpreadIncludes : ExternalEmitHelpers.SpreadArray);
                }
                var spreadType = CheckExpression(((SpreadElement)e).Expression!, checkMode ?? CheckMode.Normal, forceTuple);
                if (IsArrayLikeType(spreadType))
                {
                    elementTypes.Add(spreadType);
                    elementFlags.Add(ElementFlags.Variadic);
                }
                else if (inDestructuringPattern)
                {
                    // Given the following situation:
                    //    var c: {};
                    //    [...c] = ["", 0];
                    //
                    // c is represented in the tree as a spread element in an array literal.
                    // But c really functions as a rest element, and its purpose is to provide
                    // a contextual type for the right hand side of the assignment. Therefore,
                    // instead of calling checkExpression on "...c", which will give an error
                    // if c is not iterable/array-like, we need to act as if we are trying to
                    // get the contextual element type from it. So we do something similar to
                    // getContextualTypeForElementExpression, which will crucially not error
                    // if there is no index type / iterated type.
                    var restElementType = GetIndexTypeOfType(spreadType, _numberType) ??
                        GetIteratedTypeOrElementType(IterationUse.Destructuring, spreadType, _undefinedType, /*errorNode*/ null, /*checkAssignability*/ false) ??
                        _unknownType;
                    elementTypes.Add(restElementType);
                    elementFlags.Add(ElementFlags.Rest);
                }
                else
                {
                    elementTypes.Add(CheckIteratedTypeOrElementType(IterationUse.Spread, spreadType, _undefinedType, ((SpreadElement)e).Expression!));
                    elementFlags.Add(ElementFlags.Rest);
                }
            }
            else if (_exactOptionalPropertyTypes == true && e.Kind == SyntaxKind.OmittedExpression)
            {
                hasOmittedExpression = true;
                elementTypes.Add(_undefinedOrMissingType);
                elementFlags.Add(ElementFlags.Optional);
            }
            else
            {
                var type = CheckExpressionForMutableLocation(e, checkMode ?? CheckMode.Normal, forceTuple);
                elementTypes.Add(AddOptionality(type, /*isProperty*/ true, hasOmittedExpression));
                elementFlags.Add(hasOmittedExpression ? ElementFlags.Optional : ElementFlags.Required);
                var mode = checkMode ?? CheckMode.Normal;
                if (inTupleContext && (mode & CheckMode.Inferential) != 0 && (mode & CheckMode.SkipContextSensitive) == 0 && IsContextSensitive(e))
                {
                    var inferenceContext = GetInferenceContext(node);
                    Debug.Assert(inferenceContext is not null); // In CheckMode.Inferential we should always have an inference context
                    AddIntraExpressionInferenceSite(inferenceContext, e, type);
                }
            }
        }
        PopContextualType();
        if (inDestructuringPattern)
        {
            return CreateTupleType(elementTypes, elementFlags);
        }
        if (forceTuple || inConstContext || inTupleContext)
        {
            return CreateArrayLiteralType(CreateTupleType(elementTypes, elementFlags, /*readonly*/ inConstContext && !(contextualType is not null && SomeType(contextualType, IsMutableArrayLikeType))));
        }
        return CreateArrayLiteralType(CreateArrayType(
            elementTypes.Count > 0 ?
                GetUnionType([.. Core.SameMap(elementTypes, (t, i) => (elementFlags[i] & ElementFlags.Variadic) != 0 ? GetIndexedAccessTypeOrUndefined(t, _numberType) ?? _anyType : t)!], UnionReduction.Subtype) :
                _strictNullChecks ? _implicitNeverType : _undefinedWideningType,
            inConstContext));
    }

    /// <summary>ported from: src/compiler/checker.ts:33407-33418 — <c>createArrayLiteralType</c>.</summary>
    internal IType CreateArrayLiteralType(IType type)
    {
        if ((GetObjectFlags(type) & ObjectFlags.Reference) == 0)
        {
            return type;
        }
        var literalType = ((ITypeReference)type).LiteralType;
        if (literalType is null)
        {
            literalType = ((ITypeReference)type).LiteralType = CloneTypeReference((ITypeReference)type);
            literalType.ObjectFlags |= ObjectFlags.ArrayLiteral | ObjectFlags.ContainsObjectOrArrayLiteral;
        }
        return literalType;
    }

    // ---------------------------------------------------------------------------------------------
    // Computed names and object literal index infos (checker.ts:33419-33526)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:33419-33432 — <c>isNumericName</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>DeclarationName</c> is a union alias with no port counterpart; the parameter is
    /// <see cref="Node"/> and each arm casts to the class its <c>kind ===</c> test selected.</remarks>
    internal bool IsNumericName(Node name)
    {
        switch (name.Kind)
        {
            case SyntaxKind.ComputedPropertyName:
                return IsNumericComputedName((ComputedPropertyName)name);
            case SyntaxKind.Identifier:
                return Utilities.IsNumericLiteralName(((Identifier)name).EscapedText ?? "");
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.StringLiteral:
                return Utilities.IsNumericLiteralName(((LiteralExpression)name).Text ?? "");
            default:
                return false;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:33433-33438 — <c>isNumericComputedName</c>.</summary>
    internal bool IsNumericComputedName(ComputedPropertyName name)
    {
        // It seems odd to consider an expression of type Any to result in a numeric name,
        // but this behavior is consistent with checkIndexedAccess
        return IsTypeAssignableToKind(CheckComputedPropertyName(name), TypeFlags.NumberLike);
    }

    /// <summary>ported from: src/compiler/checker.ts:33439-33477 — <c>checkComputedPropertyName</c>.</summary>
    internal IType CheckComputedPropertyName(ComputedPropertyName node)
    {
        var links = GetNodeLinks(node.Expression!);
        if (links.ResolvedType is null)
        {
            if (
                (NodeTests.IsTypeLiteralNode(node.Parent!.Parent!) || Utilities.IsClassLike(node.Parent!.Parent!) || NodeTests.IsInterfaceDeclaration(node.Parent!.Parent!))
                && NodeTests.IsBinaryExpression(node.Expression!) && ((BinaryExpression)node.Expression!).OperatorToken!.Kind == SyntaxKind.InKeyword
                && node.Parent!.Kind != SyntaxKind.GetAccessor && node.Parent!.Kind != SyntaxKind.SetAccessor
            )
            {
                return links.ResolvedType = _errorType;
            }
            links.ResolvedType = CheckExpression(node.Expression!, CheckMode.Normal);
            // The computed property name of a non-static class field within a loop must be stored in a block-scoped binding.
            // (It needs to be bound at class evaluation time.)
            if (NodeTests.IsPropertyDeclaration(node.Parent!) && !Utilities.HasStaticModifier(node.Parent!) && NodeTests.IsClassExpression(node.Parent!.Parent!))
            {
                var container = Utilities.GetEnclosingBlockScopeContainer(node.Parent!.Parent!);
                var enclosingIterationStatement = GetEnclosingIterationStatement(container);
                if (enclosingIterationStatement is not null)
                {
                    // The computed field name will use a block scoped binding which can be unique for each iteration of the loop.
                    GetNodeLinks(enclosingIterationStatement).Flags |= NodeCheckFlags.LoopWithCapturedBlockScopedBinding;
                    // The generated variable which stores the computed field name must be block-scoped.
                    GetNodeLinks(node).Flags |= NodeCheckFlags.BlockScopedBindingInLoop;
                    // The generated variable which stores the class must be block-scoped.
                    GetNodeLinks(node.Parent!.Parent!).Flags |= NodeCheckFlags.BlockScopedBindingInLoop;
                }
            }
            // This will allow types number, string, symbol or any. It will also allow enums, the unknown
            // type, and any union of these types (like string | number).
            if (
                (links.ResolvedType.Flags & TypeFlags.Nullable) != 0 ||
                (!IsTypeAssignableToKind(links.ResolvedType, TypeFlags.StringLike | TypeFlags.NumberLike | TypeFlags.ESSymbolLike) &&
                    !IsTypeAssignableTo(links.ResolvedType, _stringNumberSymbolType))
            )
            {
                Error(node, Diagnostics.A_computed_property_name_must_be_of_type_string_number_symbol_or_any);
            }
        }

        return links.ResolvedType;
    }

    /// <summary>ported from: src/compiler/checker.ts:33478-33482 — <c>isSymbolWithNumericName</c>.</summary>
    internal bool IsSymbolWithNumericName(ISymbol symbol)
    {
        var firstDecl = Core.FirstOrUndefined(symbol.Declarations);
        return Utilities.IsNumericLiteralName(symbol.EscapedName ?? "") || (firstDecl is not null && Utilities.IsNamedDeclaration((Node)firstDecl) && IsNumericName(Utilities.GetNamedDeclarationName((Node)firstDecl)!));
    }

    /// <summary>ported from: src/compiler/checker.ts:33483-33488 — <c>isSymbolWithSymbolName</c>.</summary>
    /// <remarks>PORT-NOTE: <c>isNamedDeclaration</c> answers for the node slot, and the port reads the name through
    /// <c>Utilities.GetNamedDeclarationName</c> (the same pair <c>Utilities.IsNamedDeclaration</c> documents), so the
    /// declaration's <c>Node</c> face is what both take.</remarks>
    internal bool IsSymbolWithSymbolName(ISymbol symbol)
    {
        var firstDecl = Core.FirstOrUndefined(symbol.Declarations);
        return Utilities.IsKnownSymbol(symbol) || (firstDecl is not null && Utilities.IsNamedDeclaration((Node)firstDecl) && NodeTests.IsComputedPropertyName(Utilities.GetNamedDeclarationName((Node)firstDecl)!) &&
            IsTypeAssignableToKind(CheckComputedPropertyName((ComputedPropertyName)Utilities.GetNamedDeclarationName((Node)firstDecl)!), TypeFlags.ESSymbol));
    }

    /// <summary>ported from: src/compiler/checker.ts:33489-33493 — <c>isSymbolWithComputedName</c>.</summary>
    internal bool IsSymbolWithComputedName(ISymbol symbol)
    {
        var firstDecl = Core.FirstOrUndefined(symbol.Declarations);
        return firstDecl is not null && Utilities.IsNamedDeclaration((Node)firstDecl) && NodeTests.IsComputedPropertyName(Utilities.GetNamedDeclarationName((Node)firstDecl)!);
    }

    /// <summary>ported from: src/compiler/checker.ts:33495-33514 — <c>getObjectLiteralIndexInfo</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS records <c>ElementWithComputedPropertyName</c> (typings.ts' <c>declaration</c> slot of an index
    /// info widened to <c>PropertyName</c>) — a type with no port counterpart — so the components list holds
    /// <see cref="Node"/>, which is what <c>CreateIndexInfo</c> already takes (<c>Checker/Checker.Types.cs:880</c>).
    /// </remarks>
    internal IIndexInfo GetObjectLiteralIndexInfo(bool isReadonly, int offset, List<ISymbol> properties, IType keyType)
    {
        var propTypes = new List<IType>();
        IList<Node>? components = null;
        for (var i = offset; i < properties.Count; i++)
        {
            var prop = properties[i];
            if (
                ReferenceEquals(keyType, _stringType) && !IsSymbolWithSymbolName(prop) ||
                ReferenceEquals(keyType, _numberType) && IsSymbolWithNumericName(prop) ||
                ReferenceEquals(keyType, _esSymbolType) && IsSymbolWithSymbolName(prop)
            )
            {
                propTypes.Add(GetTypeOfSymbol(properties[i]));
                if (IsSymbolWithComputedName(properties[i]))
                {
                    components = Core.Append(components, (Node)properties[i].Declarations![0]);
                }
            }
        }
        var unionType = propTypes.Count > 0 ? GetUnionType(propTypes, UnionReduction.Subtype) : _undefinedType;
        return CreateIndexInfo(keyType, unionType, isReadonly, /*declaration*/ null, components);
    }

    /// <summary>ported from: src/compiler/checker.ts:33515-33526 — <c>getImmediateAliasedSymbol</c>.</summary>
    internal ISymbol? GetImmediateAliasedSymbol(ISymbol symbol)
    {
        Debug.Assert((symbol.Flags & SymbolFlags.Alias) != 0, "Should only get Alias here.");
        var links = GetSymbolLinks(symbol);
        if (links.ImmediateTarget is null)
        {
            var node = GetDeclarationOfAliasSymbol(symbol);
            if (node is null) return Debug.Fail<ISymbol>();
            links.ImmediateTarget = GetTargetOfAliasDeclaration(node, /*dontRecursivelyResolve*/ true);
        }

        return links.ImmediateTarget;
    }

    // ---------------------------------------------------------------------------------------------
    // Object literals (checker.ts:33527-33741)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:33527-33735 — <c>checkObjectLiteral</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>createObjectLiteralType</c> is a nested function the reference declares after its uses; C# local
    /// functions are bound the same way, so the position is kept.
    /// PORT-NOTE: <c>memberDecl.name</c> (a <c>DeclarationName</c>) is the port's
    /// <c>Utilities.GetNamedDeclarationName</c>, the shared read for the duck-typed name slot
    /// (<c>Utilities/Utilities.cs:44-51</c>).
    /// </remarks>
    internal IType CheckObjectLiteral(ObjectLiteralExpression node, CheckMode? checkMode = null)
    {
        var inDestructuringPattern = Utilities.IsAssignmentTarget(node);
        // Grammar checking
        CheckGrammarObjectLiteralExpression(node, inDestructuringPattern);

        var allPropertiesTable = _strictNullChecks ? Utilities.CreateSymbolTable() : null;
        var propertiesTable = Utilities.CreateSymbolTable();
        var propertiesArray = new List<ISymbol>();
        IType spread = _emptyObjectType;

        PushCachedContextualType(node);
        var contextualType = GetApparentTypeOfContextualType(node, /*contextFlags*/ null);
        var contextualTypeHasPattern = contextualType?.Pattern is Node patternNode &&
            (patternNode.Kind == SyntaxKind.ObjectBindingPattern || patternNode.Kind == SyntaxKind.ObjectLiteralExpression);
        var inConstContext = IsConstContext(node);
        var checkFlags = inConstContext ? CheckFlags.Readonly : CheckFlags.None;
        var isInJavascript = Utilities.IsInJSFile(node) && !Utilities.IsInJsonFile(node);
        var enumTag = isInJavascript ? Utilities.GetJSDocEnumTag(node) : null;
        var isJSObjectLiteral = contextualType is null && isInJavascript && enumTag is null;
        var objectFlags = ObjectFlags.FreshLiteral;
        var patternWithComputedProperties = false;
        var hasComputedStringProperty = false;
        var hasComputedNumberProperty = false;
        var hasComputedSymbolProperty = false;

        // Spreads may cause an early bail; ensure computed names are always checked (this is cached)
        // As otherwise they may not be checked until exports for the type at this position are retrieved,
        // which may never occur.
        foreach (var elem in node.Properties!)
        {
            var elemName = Utilities.GetNamedDeclarationName(elem);
            if (elemName is not null && NodeTests.IsComputedPropertyName(elemName))
            {
                CheckComputedPropertyName((ComputedPropertyName)elemName);
            }
        }

        var offset = 0;
        foreach (var memberDecl in node.Properties!)
        {
            var member = GetSymbolOfDeclaration((IDeclaration)memberDecl);
            var memberName = Utilities.GetNamedDeclarationName(memberDecl);
            var computedNameType = memberName is not null && memberName.Kind == SyntaxKind.ComputedPropertyName ?
                CheckComputedPropertyName((ComputedPropertyName)memberName) : null;
            if (
                memberDecl.Kind == SyntaxKind.PropertyAssignment ||
                memberDecl.Kind == SyntaxKind.ShorthandPropertyAssignment ||
                Utilities.IsObjectLiteralMethod(memberDecl)
            )
            {
                var type = memberDecl.Kind == SyntaxKind.PropertyAssignment ? CheckPropertyAssignment((PropertyAssignment)memberDecl, checkMode ?? CheckMode.Normal) :
                    // avoid resolving the left side of the ShorthandPropertyAssignment outside of the destructuring
                    // for error recovery purposes. For example, if a user wrote `{ a = 100 }` instead of `{ a: 100 }`.
                    // we don't want to say "could not find 'a'".
                    memberDecl.Kind == SyntaxKind.ShorthandPropertyAssignment ? CheckExpressionForMutableLocation(!inDestructuringPattern && ((ShorthandPropertyAssignment)memberDecl).ObjectAssignmentInitializer is not null ? ((ShorthandPropertyAssignment)memberDecl).ObjectAssignmentInitializer! : (Expression)memberName!, checkMode ?? CheckMode.Normal) :
                    CheckObjectLiteralMethod((MethodDeclaration)memberDecl, checkMode ?? CheckMode.Normal);
                if (isInJavascript)
                {
                    var jsDocType = GetTypeForDeclarationFromJSDocComment(memberDecl);
                    if (jsDocType is not null)
                    {
                        CheckTypeAssignableTo(type, jsDocType, memberDecl);
                        type = jsDocType;
                    }
                    else if (enumTag?.TypeExpression is not null)
                    {
                        CheckTypeAssignableTo(type, GetTypeFromTypeNode(enumTag.TypeExpression), memberDecl);
                    }
                }
                objectFlags |= GetObjectFlags(type) & ObjectFlags.PropagatingFlags;
                var nameType = computedNameType is not null && Utilities.IsTypeUsableAsPropertyName(computedNameType) ? computedNameType : null;
                var prop = nameType is not null ?
                    CreateSymbol(SymbolFlags.Property | member.Flags, Utilities.GetPropertyNameFromType(nameType), checkFlags | CheckFlags.Late) :
                    CreateSymbol(SymbolFlags.Property | member.Flags, member.EscapedName!, checkFlags);
                if (nameType is not null)
                {
                    GetSymbolLinks(prop).NameType = nameType;
                }

                if (inDestructuringPattern && HasDefaultValue(memberDecl))
                {
                    // If object literal is an assignment pattern and if the assignment pattern specifies a default value
                    // for the property, make the property optional.
                    prop.Flags |= SymbolFlags.Optional;
                }
                else if (contextualTypeHasPattern && (GetObjectFlags(contextualType!) & ObjectFlags.ObjectLiteralPatternWithComputedProperties) == 0)
                {
                    // If object literal is contextually typed by the implied type of a binding pattern, and if the
                    // binding pattern specifies a default value for the property, make the property optional.
                    var impliedProp = GetPropertyOfType(contextualType, member.EscapedName!);
                    if (impliedProp is not null)
                    {
                        prop.Flags |= impliedProp.Flags & SymbolFlags.Optional;
                    }
                    else if (GetIndexInfoOfType(contextualType!, _stringType) is null)
                    {
                        // PORT-NOTE (A″): the message arguments below are deferred thunks (`Diagnostic.DeferArg` /
                        // `DiagnosticArgument` in Core/Diagnostic.cs), so the printer-gated `SymbolToString`/`TypeToString`
                        // run on the first read of the message text. That read is after `member = prop` below, while the
                        // reference stringifies `member` as it stands here (checker.ts:33609 vs :33621) — hence the pinned
                        // copy for the thunk to read.
                        var memberForDiagnostic = member;
                        Error(memberName, Diagnostics.Object_literal_may_only_specify_known_properties_and_0_does_not_exist_in_type_1, Diagnostic.DeferArg(() => SymbolToString(memberForDiagnostic)), Diagnostic.DeferArg(() => TypeToString(contextualType)));
                    }
                }

                prop.Declarations = member.Declarations;
                prop.Parent = member.Parent;
                if (member.ValueDeclaration is not null)
                {
                    prop.ValueDeclaration = member.ValueDeclaration;
                }

                GetSymbolLinks(prop).Type = type;
                GetSymbolLinks(prop).Target = member;
                member = prop;
                if (allPropertiesTable is not null)
                {
                    allPropertiesTable[prop.EscapedName!] = prop;
                }

                var mode = checkMode ?? CheckMode.Normal;
                if (
                    contextualType is not null && (mode & CheckMode.Inferential) != 0 && (mode & CheckMode.SkipContextSensitive) == 0 &&
                    (memberDecl.Kind is SyntaxKind.PropertyAssignment or SyntaxKind.MethodDeclaration) && IsContextSensitive(memberDecl)
                )
                {
                    var inferenceContext = GetInferenceContext(node);
                    Debug.Assert(inferenceContext is not null); // In CheckMode.Inferential we should always have an inference context
                    var inferenceNode = memberDecl.Kind == SyntaxKind.PropertyAssignment ? ((PropertyAssignment)memberDecl).Initializer! : memberDecl;
                    AddIntraExpressionInferenceSite(inferenceContext, inferenceNode, type);
                }
            }
            else if (memberDecl.Kind == SyntaxKind.SpreadAssignment)
            {
                if (_languageVersion < ScriptTarget.ES2015)
                {
                    CheckExternalEmitHelpers(memberDecl, ExternalEmitHelpers.Assign);
                }
                if (propertiesArray.Count > 0)
                {
                    spread = GetSpreadType(spread, CreateObjectLiteralType(), node.Symbol, objectFlags, inConstContext);
                    propertiesArray = [];
                    propertiesTable = Utilities.CreateSymbolTable();
                    hasComputedStringProperty = false;
                    hasComputedNumberProperty = false;
                    hasComputedSymbolProperty = false;
                }
                var type = GetReducedType(CheckExpression(((SpreadAssignment)memberDecl).Expression!, (checkMode ?? CheckMode.Normal) & CheckMode.Inferential));
                if (IsValidSpreadType(type))
                {
                    var mergedType = TryMergeUnionOfObjectTypeAndEmptyObject(type, inConstContext);
                    if (allPropertiesTable is not null)
                    {
                        CheckSpreadPropOverrides(mergedType, allPropertiesTable, memberDecl);
                    }
                    offset = propertiesArray.Count;
                    if (IsErrorType(spread))
                    {
                        continue;
                    }
                    spread = GetSpreadType(spread, mergedType, node.Symbol, objectFlags, inConstContext);
                }
                else
                {
                    Error(memberDecl, Diagnostics.Spread_types_may_only_be_created_from_object_types);
                    spread = _errorType;
                }
                continue;
            }
            else
            {
                // TypeScript 1.0 spec (April 2014)
                // A get accessor declaration is processed in the same manner as
                // an ordinary function declaration(section 6.1) with no parameters.
                // A set accessor declaration is processed in the same manner
                // as an ordinary function declaration with a single parameter and a Void return type.
                Debug.Assert(memberDecl.Kind == SyntaxKind.GetAccessor || memberDecl.Kind == SyntaxKind.SetAccessor);
                CheckNodeDeferred(memberDecl);
            }

            if (computedNameType is not null && (computedNameType.Flags & TypeFlags.StringOrNumberLiteralOrUnique) == 0)
            {
                if (IsTypeAssignableTo(computedNameType, _stringNumberSymbolType))
                {
                    if (IsTypeAssignableTo(computedNameType, _numberType))
                    {
                        hasComputedNumberProperty = true;
                    }
                    else if (IsTypeAssignableTo(computedNameType, _esSymbolType))
                    {
                        hasComputedSymbolProperty = true;
                    }
                    else
                    {
                        hasComputedStringProperty = true;
                    }
                    if (inDestructuringPattern)
                    {
                        patternWithComputedProperties = true;
                    }
                }
            }
            else
            {
                propertiesTable[member.EscapedName!] = member;
            }
            propertiesArray.Add(member);
        }
        PopContextualType();

        if (IsErrorType(spread))
        {
            return _errorType;
        }

        if (!ReferenceEquals(spread, _emptyObjectType))
        {
            if (propertiesArray.Count > 0)
            {
                spread = GetSpreadType(spread, CreateObjectLiteralType(), node.Symbol, objectFlags, inConstContext);
                propertiesArray = [];
                propertiesTable = Utilities.CreateSymbolTable();
                hasComputedStringProperty = false;
                hasComputedNumberProperty = false;
            }
            // remap the raw emptyObjectType fed in at the top into a fresh empty object literal type, unique to this use site
            return MapType(spread, t => ReferenceEquals(t, _emptyObjectType) ? CreateObjectLiteralType() : t)!;
        }

        return CreateObjectLiteralType();

        // ported from: src/compiler/checker.ts:33715-33734 — <c>createObjectLiteralType</c>.
        IType CreateObjectLiteralType()
        {
            var indexInfos = new List<IIndexInfo>();
            var isReadonly = IsConstContext(node);
            if (hasComputedStringProperty) indexInfos.Add(GetObjectLiteralIndexInfo(isReadonly, offset, propertiesArray, _stringType));
            if (hasComputedNumberProperty) indexInfos.Add(GetObjectLiteralIndexInfo(isReadonly, offset, propertiesArray, _numberType));
            if (hasComputedSymbolProperty) indexInfos.Add(GetObjectLiteralIndexInfo(isReadonly, offset, propertiesArray, _esSymbolType));
            var result = CreateAnonymousType(node.Symbol, propertiesTable, [], [], indexInfos);
            result.ObjectFlags |= objectFlags | ObjectFlags.ObjectLiteral | ObjectFlags.ContainsObjectOrArrayLiteral;
            if (isJSObjectLiteral)
            {
                result.ObjectFlags |= ObjectFlags.JSLiteral;
            }
            if (patternWithComputedProperties)
            {
                result.ObjectFlags |= ObjectFlags.ObjectLiteralPatternWithComputedProperties;
            }
            if (inDestructuringPattern)
            {
                result.Pattern = node;
            }
            return result;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:33736-33741 — <c>isValidSpreadType</c>.</summary>
    internal bool IsValidSpreadType(IType type)
    {
        var t = RemoveDefinitelyFalsyTypes(MapType(type, GetBaseConstraintOrType)!);
        return (t.Flags & (TypeFlags.Any | TypeFlags.NonPrimitive | TypeFlags.Object | TypeFlags.InstantiableNonPrimitive)) != 0 ||
            (t.Flags & TypeFlags.UnionOrIntersection) != 0 && Core.Every(((IUnionOrIntersectionType)t).Types, (u, _) => IsValidSpreadType(u));
    }

    // ---------------------------------------------------------------------------------------------
    // The JSX element and attribute checks (checker.ts:33742-34053)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:33742-33745 — <c>checkJsxSelfClosingElementDeferred</c>.</summary>
    internal void CheckJsxSelfClosingElementDeferred(JsxSelfClosingElement node)
        => CheckJsxOpeningLikeElementOrOpeningFragment(node);

    /// <summary>ported from: src/compiler/checker.ts:33746-33750 — <c>checkJsxSelfClosingElement</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's second parameter is the expression dispatch's unused <c>_checkMode</c>; it is
    /// kept because the dispatch passes it positionally (the <c>_</c> prefix is the port's marker for that).</remarks>
    internal IType CheckJsxSelfClosingElement(JsxSelfClosingElement node, CheckMode? _checkMode = null)
    {
        CheckNodeDeferred(node);
        return GetJsxElementTypeAt(node) ?? _anyType;
    }

    /// <summary>ported from: src/compiler/checker.ts:33751-33765 — <c>checkJsxElementDeferred</c>.</summary>
    internal void CheckJsxElementDeferred(JsxElement node)
    {
        // Check attributes
        CheckJsxOpeningLikeElementOrOpeningFragment(node.OpeningElement!);

        // Perform resolution on the closing tag so that rename/go to definition/etc work
        if (IsJsxIntrinsicTagName(node.ClosingElement!.TagName!))
        {
            GetIntrinsicTagSymbol(node.ClosingElement!);
        }
        else
        {
            CheckExpression((Expression)node.ClosingElement!.TagName!, CheckMode.Normal);
        }

        CheckJsxChildren(node);
    }

    /// <summary>ported from: src/compiler/checker.ts:33766-33771 — <c>checkJsxElement</c>.</summary>
    /// <remarks>PORT-NOTE: as <see cref="CheckJsxSelfClosingElement"/> — the unused <c>_checkMode</c> is kept.</remarks>
    internal IType CheckJsxElement(JsxElement node, CheckMode? _checkMode = null)
    {
        CheckNodeDeferred(node);

        return GetJsxElementTypeAt(node) ?? _anyType;
    }

    /// <summary>ported from: src/compiler/checker.ts:33772-33794 — <c>checkJsxFragment</c>.</summary>
    internal IType CheckJsxFragment(JsxFragment node)
    {
        CheckJsxOpeningLikeElementOrOpeningFragment(node.OpeningFragment!);

        // by default, jsx:'react' will use jsxFactory = React.createElement and jsxFragmentFactory = React.Fragment
        // if jsxFactory compiler option is provided, ensure jsxFragmentFactory compiler option or @jsxFrag pragma is provided too
        var nodeSourceFile = Utilities.GetSourceFileOfNode(node)!;
        if (
            Utilities.GetJSXTransformEnabled(_compilerOptions) && (!string.IsNullOrEmpty(_compilerOptions.JsxFactory) || nodeSourceFile.Pragmas?.Get("jsx") is not null)
            && string.IsNullOrEmpty(_compilerOptions.JsxFragmentFactory) && nodeSourceFile.Pragmas?.Get("jsxfrag") is null
        )
        {
            Error(
                node,
                !string.IsNullOrEmpty(_compilerOptions.JsxFactory)
                    ? Diagnostics.The_jsxFragmentFactory_compiler_option_must_be_provided_to_use_JSX_fragments_with_the_jsxFactory_compiler_option
                    : Diagnostics.An_jsxFrag_pragma_is_required_when_using_an_jsx_pragma_with_JSX_fragments);
        }

        CheckJsxChildren(node);
        var jsxElementType = GetJsxElementTypeAt(node);
        return IsErrorType(jsxElementType) ? _anyType : jsxElementType;
    }

    /// <summary>ported from: src/compiler/checker.ts:33795-33801 — <c>isHyphenatedJsxName</c>.</summary>
    internal static bool IsHyphenatedJsxName(string name) => name.Contains('-');

    /// <summary>ported from: src/compiler/checker.ts:33802-33805 — <c>isJsxIntrinsicTagName</c>.</summary>
    /// <remarks>PORT-NOTE: TS's return is the type predicate <c>tagName is Identifier | JsxNamespacedName</c>; C# has no
    /// narrowing predicate, so the return is <c>bool</c> and a caller that needs the narrowed node casts (the treatment
    /// <c>Utilities.IsNamedDeclaration</c> documents for the same pattern).</remarks>
    internal bool IsJsxIntrinsicTagName(Node tagName)
        => NodeTests.IsIdentifier(tagName) && Utilities.IsIntrinsicJsxName(((Identifier)tagName).EscapedText ?? "") || NodeTests.IsJsxNamespacedName(tagName);

    /// <summary>ported from: src/compiler/checker.ts:33806-33810 — <c>checkJsxAttribute</c>.</summary>
    internal IType CheckJsxAttribute(JsxAttribute node, CheckMode? checkMode = null)
        => node.Initializer is not null
            ? CheckExpressionForMutableLocation(node.Initializer, checkMode ?? CheckMode.Normal)
            : _trueType; // <Elem attr /> is sugar for <Elem attr={true} />

    /// <summary>ported from: src/compiler/checker.ts:33821-33953 — <c>createJsxAttributesTypeFromAttributesProperty</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>openingLikeElement.attributes</c> is read through the two classes that carry the slot, exactly as
    /// the landed caller does it for this function's argument (<c>Checker/Checker.CallResolution.cs:759</c>).
    /// PORT-NOTE: the children block's inner <c>const contextualType</c> shadows the attributes' one in TS; C# forbids
    /// block-scoped shadowing, so the inner one is named <c>jsxAttributesContextualType</c>.
    /// PORT-NOTE: <c>createJsxAttributesTypeHelper</c> is a nested function declared after its uses; C# local functions
    /// are bound the same way, so the position is kept.
    /// PORT-NOTE: the fabricated children property is the reference's <c>factory.createPropertySignature</c>
    /// (checker.ts:33927, factory/nodeFactory.ts:7407), i.e. <see cref="NodeFactory.Factory"/>.
    /// </remarks>
    internal IType CreateJsxAttributesTypeFromAttributesProperty(Node openingLikeElement, CheckMode? checkMode = null)
    {
        var allAttributesTable = _strictNullChecks ? Utilities.CreateSymbolTable() : null;
        var attributesTable = Utilities.CreateSymbolTable();
        IType spread = _emptyJsxObjectType;
        var hasSpreadAnyType = false;
        IType? typeToIntersect = null;
        var explicitlySpecifyChildrenAttribute = false;
        var objectFlags = ObjectFlags.JsxAttributes;
        var jsxChildrenPropertyName = GetJsxElementChildrenPropertyName(GetJsxNamespaceAt(openingLikeElement));

        var isJsxOpenFragment = NodeTests.IsJsxOpeningFragment(openingLikeElement);

        ISymbol? attributesSymbol = null;
        Node attributeParent = openingLikeElement;
        if (!isJsxOpenFragment)
        {
            var attributes = ((openingLikeElement as JsxOpeningElement)?.Attributes ?? (openingLikeElement as JsxSelfClosingElement)?.Attributes)!;
            attributesSymbol = attributes.Symbol;
            attributeParent = attributes;
            var contextualType = GetContextualType(attributes, ContextFlags.None);
            foreach (var attributeDecl in attributes.Properties!)
            {
                if (NodeTests.IsJsxAttribute(attributeDecl))
                {
                    var attribute = (JsxAttribute)attributeDecl;
                    var member = attribute.Symbol!;
                    var exprType = CheckJsxAttribute(attribute, checkMode ?? CheckMode.Normal);
                    objectFlags |= GetObjectFlags(exprType) & ObjectFlags.PropagatingFlags;

                    var attributeSymbol = CreateSymbol(SymbolFlags.Property | member.Flags, member.EscapedName!);
                    attributeSymbol.Declarations = member.Declarations;
                    attributeSymbol.Parent = member.Parent;
                    if (member.ValueDeclaration is not null)
                    {
                        attributeSymbol.ValueDeclaration = member.ValueDeclaration;
                    }
                    GetSymbolLinks(attributeSymbol).Type = exprType;
                    GetSymbolLinks(attributeSymbol).Target = member;
                    attributesTable[attributeSymbol.EscapedName!] = attributeSymbol;
                    if (allAttributesTable is not null)
                    {
                        allAttributesTable[attributeSymbol.EscapedName!] = attributeSymbol;
                    }
                    if (Utilities.GetEscapedTextOfJsxAttributeName(attribute.Name!) == jsxChildrenPropertyName)
                    {
                        explicitlySpecifyChildrenAttribute = true;
                    }
                    if (contextualType is not null)
                    {
                        var prop = GetPropertyOfType(contextualType, member.EscapedName!);
                        if (prop?.Declarations is not null && IsDeprecatedSymbol(prop) && NodeTests.IsIdentifier(attribute.Name!))
                        {
                            AddDeprecatedSuggestion(attribute.Name!, Core.Map(prop.Declarations, (d, _) => (Node)d)!, ((Identifier)attribute.Name!).EscapedText!);
                        }
                    }
                    var mode = checkMode ?? CheckMode.Normal;
                    if (contextualType is not null && (mode & CheckMode.Inferential) != 0 && (mode & CheckMode.SkipContextSensitive) == 0 && IsContextSensitive(attribute))
                    {
                        var inferenceContext = GetInferenceContext(attributes);
                        Debug.Assert(inferenceContext is not null); // In CheckMode.Inferential we should always have an inference context
                        var inferenceNode = ((JsxExpression)attribute.Initializer!).Expression!;
                        AddIntraExpressionInferenceSite(inferenceContext, inferenceNode, exprType);
                    }
                }
                else
                {
                    Debug.Assert(attributeDecl.Kind == SyntaxKind.JsxSpreadAttribute);
                    if (attributesTable.Count > 0)
                    {
                        spread = GetSpreadType(spread, CreateJsxAttributesTypeHelper(), attributes.Symbol, objectFlags, /*readonly*/ false);
                        attributesTable = Utilities.CreateSymbolTable();
                    }
                    var exprType = GetReducedType(CheckExpression(((JsxSpreadAttribute)attributeDecl).Expression!, (checkMode ?? CheckMode.Normal) & CheckMode.Inferential));
                    if (IsTypeAny(exprType))
                    {
                        hasSpreadAnyType = true;
                    }
                    if (IsValidSpreadType(exprType))
                    {
                        spread = GetSpreadType(spread, exprType, attributes.Symbol, objectFlags, /*readonly*/ false);
                        if (allAttributesTable is not null)
                        {
                            CheckSpreadPropOverrides(exprType, allAttributesTable, attributeDecl);
                        }
                    }
                    else
                    {
                        Error(((JsxSpreadAttribute)attributeDecl).Expression!, Diagnostics.Spread_types_may_only_be_created_from_object_types);
                        typeToIntersect = typeToIntersect is not null ? GetIntersectionType([typeToIntersect, exprType]) : exprType;
                    }
                }
            }

            if (!hasSpreadAnyType)
            {
                if (attributesTable.Count > 0)
                {
                    spread = GetSpreadType(spread, CreateJsxAttributesTypeHelper(), attributes.Symbol, objectFlags, /*readonly*/ false);
                }
            }
        }

        // Handle children attribute
        var parent = openingLikeElement.Parent;
        // We have to check that openingElement of the parent is the one we are visiting as this may not be true for selfClosingElement
        if (
            parent is not null &&
            (NodeTests.IsJsxElement(parent) && ((JsxElement)parent).OpeningElement == openingLikeElement || NodeTests.IsJsxFragment(parent) && ((JsxFragment)parent).OpeningFragment == openingLikeElement) &&
            Core.Length(Utilities.GetSemanticJsxChildren(parent is JsxElement elementParent ? elementParent.Children! : ((JsxFragment)parent).Children!)) > 0
        )
        {
            var childrenTypes = CheckJsxChildren(parent, checkMode);

            if (!hasSpreadAnyType && !string.IsNullOrEmpty(jsxChildrenPropertyName))
            {
                // Error if there is a attribute named "children" explicitly specified and children element.
                // This is because children element will overwrite the value from attributes.
                // Note: we will not warn "children" attribute overwritten if "children" attribute is specified in object spread.
                if (explicitlySpecifyChildrenAttribute)
                {
                    Error(attributeParent, Diagnostics._0_are_specified_twice_The_attribute_named_0_will_be_overwritten, Utilities.UnescapeLeadingUnderscores(jsxChildrenPropertyName));
                }

                var jsxAttributesContextualType = NodeTests.IsJsxOpeningElement(openingLikeElement) ? GetApparentTypeOfContextualType(((JsxOpeningElement)openingLikeElement).Attributes!, /*contextFlags*/ null) : null;
                var childrenContextualType = jsxAttributesContextualType is not null ? GetTypeOfPropertyOfContextualType(jsxAttributesContextualType, jsxChildrenPropertyName) : null;
                // If there are children in the body of JSX element, create dummy attribute "children" with the union of children types so that it will pass the attribute checking process
                var childrenPropSymbol = CreateSymbol(SymbolFlags.Property, jsxChildrenPropertyName);
                GetSymbolLinks(childrenPropSymbol).Type = childrenTypes.Count == 1 ? childrenTypes[0] :
                    childrenContextualType is not null && SomeType(childrenContextualType, IsTupleLikeType) ? CreateTupleType(childrenTypes) :
                    CreateArrayType(GetUnionType(childrenTypes));
                // Fake up a property declaration for the children
                var childrenPropDecl = NodeFactory.Factory.CreatePropertySignature(/*modifiers*/ null, Utilities.UnescapeLeadingUnderscores(jsxChildrenPropertyName), /*questionToken*/ null, /*type*/ null);
                childrenPropSymbol.ValueDeclaration = childrenPropDecl;
                Utilities.SetParent(childrenPropDecl, attributeParent);
                childrenPropDecl.Symbol = childrenPropSymbol;
                var childPropMap = Utilities.CreateSymbolTable();
                childPropMap[jsxChildrenPropertyName] = childrenPropSymbol;
                spread = GetSpreadType(spread, CreateAnonymousType(attributesSymbol, childPropMap, [], [], []), attributesSymbol, objectFlags | GetPropagatingFlagsOfTypes(childrenTypes), /*readonly*/ false);
            }
        }

        if (hasSpreadAnyType)
        {
            return _anyType;
        }
        if (typeToIntersect is not null && !ReferenceEquals(spread, _emptyJsxObjectType))
        {
            return GetIntersectionType([typeToIntersect, spread]);
        }
        return typeToIntersect ?? (ReferenceEquals(spread, _emptyJsxObjectType) ? CreateJsxAttributesTypeHelper() : spread);

        // ported from: src/compiler/checker.ts:33944-33947 — <c>createJsxAttributesTypeHelper</c>.
        IType CreateJsxAttributesTypeHelper()
        {
            objectFlags |= ObjectFlags.FreshLiteral;
            return CreateJsxAttributesType(objectFlags, attributesSymbol, attributesTable);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:33954-33959 — <c>createJsxAttributesType</c>.</summary>
    internal IType CreateJsxAttributesType(ObjectFlags objectFlags, ISymbol? attributesSymbol, SymbolTable attributesTable)
    {
        var result = CreateAnonymousType(attributesSymbol, attributesTable, [], [], []);
        result.ObjectFlags |= objectFlags | ObjectFlags.FreshLiteral | ObjectFlags.ObjectLiteral | ObjectFlags.ContainsObjectOrArrayLiteral;
        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:33960-33979 — <c>checkJsxChildren</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is the union <c>JsxElement | JsxFragment</c>, which the port erases to
    /// <see cref="Node"/>; both classes carry <c>children</c>, so the read is the one switch below.</remarks>
    internal List<IType> CheckJsxChildren(Node node, CheckMode? checkMode = null)
    {
        var childrenTypes = new List<IType>();
        var children = node is JsxElement jsxElement ? jsxElement.Children! : ((JsxFragment)node).Children!;
        foreach (var child in children)
        {
            // In React, JSX text that contains only whitespaces will be ignored so we don't want to type-check that
            // because then type of children property will have constituent of string type.
            if (child.Kind == SyntaxKind.JsxText)
            {
                if (!((JsxText)child).ContainsOnlyTriviaWhiteSpaces)
                {
                    childrenTypes.Add(_stringType);
                }
            }
            else if (child.Kind == SyntaxKind.JsxExpression && ((JsxExpression)child).Expression is null)
            {
                continue; // empty jsx expressions don't *really* count as present children
            }
            else
            {
                childrenTypes.Add(CheckExpressionForMutableLocation((Expression)child, checkMode ?? CheckMode.Normal));
            }
        }
        return childrenTypes;
    }

    /// <summary>ported from: src/compiler/checker.ts:33980-33996 — <c>checkSpreadPropOverrides</c>.</summary>
    /// <remarks>PORT-NOTE: TS's third parameter is <c>SpreadAssignment | JsxSpreadAttribute</c>; the port erases the
    /// union to <see cref="Node"/>, which is all the body needs (an error location).</remarks>
    internal void CheckSpreadPropOverrides(IType type, SymbolTable props, Node spread)
    {
        foreach (var right in GetPropertiesOfType(type))
        {
            if ((right.Flags & SymbolFlags.Optional) == 0 && (GetCheckFlags(right) & CheckFlags.Partial) == 0)
            {
                var left = props.GetValueOrDefault(right.EscapedName!);
                if (left is not null)
                {
                    var diagnostic = Error((Node)left.ValueDeclaration!, Diagnostics._0_is_specified_more_than_once_so_this_usage_will_be_overwritten, Utilities.UnescapeLeadingUnderscores(left.EscapedName!));
                    Utilities.AddRelatedInfo(diagnostic, Utilities.CreateDiagnosticForNode(spread, Diagnostics.This_spread_always_overwrites_this_property));
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:33997-34000 — <c>checkJsxAttributes</c>.</summary>
    internal IType CheckJsxAttributes(JsxAttributes node, CheckMode? checkMode = null)
        => CreateJsxAttributesTypeFromAttributesProperty(node.Parent!, checkMode);

    /// <summary>ported from: src/compiler/checker.ts:34001-34006 — <c>getJsxType</c>.</summary>
    internal IType GetJsxType(string name, Node? location)
    {
        var ns = GetJsxNamespaceAt(location);
        var exports = ns is not null ? GetExportsOfSymbol(ns) : null;
        var typeSymbol = exports is not null ? GetSymbol(exports, name, SymbolFlags.Type) : null;
        return typeSymbol is not null ? GetDeclaredTypeOfSymbol(typeSymbol) : _errorType;
    }

    /// <summary>ported from: src/compiler/checker.ts:34014-34053 — <c>getIntrinsicTagSymbol</c>.</summary>
    internal ISymbol GetIntrinsicTagSymbol(Node node)
    {
        var links = GetNodeLinks(node);
        if (links.ResolvedSymbol is null)
        {
            var intrinsicElementsType = GetJsxType(JsxNames.IntrinsicElements, node);
            if (!IsErrorType(intrinsicElementsType))
            {
                // Property case
                var tagName = node switch
                {
                    JsxOpeningElement openingElement => openingElement.TagName!,
                    JsxSelfClosingElement selfClosingElement => selfClosingElement.TagName!,
                    JsxClosingElement closingElement => closingElement.TagName!,
                    _ => Debug.Fail<Node>(),
                };
                if (!NodeTests.IsIdentifier(tagName) && !NodeTests.IsJsxNamespacedName(tagName)) return Debug.Fail<ISymbol>();
                var propName = NodeTests.IsJsxNamespacedName(tagName) ? Utilities.GetEscapedTextOfJsxNamespacedName((JsxNamespacedName)tagName) : ((Identifier)tagName).EscapedText ?? "";
                var intrinsicProp = GetPropertyOfType(intrinsicElementsType, propName);
                if (intrinsicProp is not null)
                {
                    links.JsxFlags |= JsxFlags.IntrinsicNamedElement;
                    links.ResolvedSymbol = intrinsicProp;
                    return intrinsicProp;
                }

                // Intrinsic string indexer case
                var indexSymbol = GetApplicableIndexSymbol(intrinsicElementsType, GetStringLiteralType(Utilities.UnescapeLeadingUnderscores(propName)));
                if (indexSymbol is not null)
                {
                    links.JsxFlags |= JsxFlags.IntrinsicIndexedElement;
                    links.ResolvedSymbol = indexSymbol;
                    return indexSymbol;
                }

                if (GetTypeOfPropertyOrIndexSignatureOfType(intrinsicElementsType, propName) is not null)
                {
                    links.JsxFlags |= JsxFlags.IntrinsicIndexedElement;
                    links.ResolvedSymbol = intrinsicElementsType.Symbol;
                    return intrinsicElementsType.Symbol!;
                }

                // Wasn't found
                Error(node, Diagnostics.Property_0_does_not_exist_on_type_1, Utilities.IntrinsicTagNameToString(tagName), "JSX." + JsxNames.IntrinsicElements);
                links.ResolvedSymbol = _unknownSymbol;
                return _unknownSymbol;
            }
            else
            {
                if (_noImplicitAny)
                {
                    Error(node, Diagnostics.JSX_element_implicitly_has_type_any_because_no_interface_JSX_0_exists, Utilities.UnescapeLeadingUnderscores(JsxNames.IntrinsicElements));
                }
                links.ResolvedSymbol = _unknownSymbol;
                return _unknownSymbol;
            }
        }
        return links.ResolvedSymbol;
    }

    /// <summary>ported from: src/compiler/checker.ts:34054-34079 — <c>getJsxNamespaceContainerForImplicitImport</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>links.jsxImplicitImportContainer</c> is the model's <c>object?</c> slot that stands for TS's
    /// <c>Symbol | false</c> (Model/NodeLinks.cs:82-93), so the two reads test the boxed <c>false</c> and the write
    /// spells TS's <c>result || false</c> as the two arms below.
    /// </remarks>
    internal ISymbol? GetJsxNamespaceContainerForImplicitImport(Node? location)
    {
        var file = location is not null ? Utilities.GetSourceFileOfNode(location) : null;
        var links = file is not null ? GetNodeLinks(file) : null;
        if (links?.JsxImplicitImportContainer is bool cachedAbsent && !cachedAbsent)
        {
            return null;
        }
        if (links?.JsxImplicitImportContainer is ISymbol cached)
        {
            return cached;
        }
        var runtimeImportSpecifier = Utilities.GetJsxRuntimeImport(Utilities.GetJsxImplicitImportBase(_compilerOptions, file), _compilerOptions);
        if (string.IsNullOrEmpty(runtimeImportSpecifier))
        {
            return null;
        }
        var isClassic = Utilities.GetEmitModuleResolutionKind(_compilerOptions) == ModuleResolutionKind.Classic;
        var errorMessage = isClassic
            ? Diagnostics.Cannot_find_module_0_Did_you_mean_to_set_the_moduleResolution_option_to_nodenext_or_to_add_aliases_to_the_paths_option
            : Diagnostics.This_JSX_tag_requires_the_module_path_0_to_exist_but_none_could_be_found_Make_sure_you_have_types_for_the_appropriate_package_installed;
        var specifier = GetJSXRuntimeImportSpecifier(file, runtimeImportSpecifier);
        var mod = ResolveExternalModule(specifier ?? location!, runtimeImportSpecifier, errorMessage, location);
        var result = mod is not null && !ReferenceEquals(mod, _unknownSymbol) ? GetMergedSymbol(ResolveSymbol(mod)) : null;
        if (links is not null)
        {
            if (result is not null)
            {
                links.JsxImplicitImportContainer = result;
            }
            else
            {
                links.JsxImplicitImportContainer = false;
            }
        }
        return result;
    }

    // ---------------------------------------------------------------------------------------------
    // The JSX namespace and signature helpers (checker.ts:34080-34341)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:34080-34120 — <c>getJsxNamespaceAt</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>links.jsxNamespace</c> is the model's <c>object?</c> slot standing for TS's <c>Symbol | false</c>
    /// (Model/NodeLinks.cs:82-89), so the two tests below are the "cache hit" and "resolved as absent" arms of
    /// <c>links.jsxNamespace</c>/<c>links.jsxNamespace !== false</c>.
    /// PORT-NOTE: the reference's final <c>return undefined!; // TODO: GH#18217</c> is <c>null</c> here, with the
    /// signature saying so.
    /// </remarks>
    internal ISymbol? GetJsxNamespaceAt(Node? location)
    {
        var links = location is not null ? GetNodeLinks(location) : null;
        if (links?.JsxNamespace is ISymbol cached)
        {
            return cached;
        }
        if (links is null || !(links.JsxNamespace is bool resolvedAbsent && !resolvedAbsent))
        {
            var resolvedNamespace = GetJsxNamespaceContainerForImplicitImport(location);

            if (resolvedNamespace is null || ReferenceEquals(resolvedNamespace, _unknownSymbol))
            {
                var namespaceName = GetJsxNamespace(location);
                resolvedNamespace = ResolveName(location, namespaceName, SymbolFlags.Namespace, /*nameNotFoundMessage*/ null, /*isUse*/ false, /*excludeGlobals*/ false);
            }

            if (resolvedNamespace is not null)
            {
                var candidate = ResolveSymbol(GetSymbol(GetExportsOfSymbol(ResolveSymbol(resolvedNamespace)!), JsxNames.JSX, SymbolFlags.Namespace));
                if (candidate is not null && !ReferenceEquals(candidate, _unknownSymbol))
                {
                    if (links is not null)
                    {
                        links.JsxNamespace = candidate;
                    }
                    return candidate;
                }
            }
            if (links is not null)
            {
                links.JsxNamespace = false;
            }
        }
        // JSX global fallback
        var s = ResolveSymbol(GetGlobalSymbol(JsxNames.JSX, SymbolFlags.Namespace, /*diagnostic*/ null));
        if (ReferenceEquals(s, _unknownSymbol))
        {
            return null; // TODO: GH#18217
        }
        return s;
    }

    /// <summary>ported from: src/compiler/checker.ts:34121-34145 — <c>getNameFromJsxElementAttributesContainer</c>.</summary>
    internal string? GetNameFromJsxElementAttributesContainer(string nameOfAttribPropContainer, ISymbol? jsxNamespace)
    {
        // JSX.ElementAttributesProperty | JSX.ElementChildrenAttribute [symbol]
        var jsxElementAttribPropInterfaceSym = jsxNamespace is not null ? GetSymbol(jsxNamespace.Exports!, nameOfAttribPropContainer, SymbolFlags.Type) : null;
        // JSX.ElementAttributesProperty | JSX.ElementChildrenAttribute [type]
        var jsxElementAttribPropInterfaceType = jsxElementAttribPropInterfaceSym is not null ? GetDeclaredTypeOfSymbol(jsxElementAttribPropInterfaceSym) : null;
        // The properties of JSX.ElementAttributesProperty | JSX.ElementChildrenAttribute
        var propertiesOfJsxElementAttribPropInterface = jsxElementAttribPropInterfaceType is not null ? GetPropertiesOfType(jsxElementAttribPropInterfaceType) : null;
        if (propertiesOfJsxElementAttribPropInterface is not null)
        {
            // Element Attributes has zero properties, so the element attributes type will be the class instance type
            if (propertiesOfJsxElementAttribPropInterface.Count == 0)
            {
                return "";
            }
            // Element Attributes has one property, so the element attributes type will be the type of the corresponding
            // property of the class instance type
            else if (propertiesOfJsxElementAttribPropInterface.Count == 1)
            {
                return propertiesOfJsxElementAttribPropInterface[0].EscapedName;
            }
            else if (propertiesOfJsxElementAttribPropInterface.Count > 1 && jsxElementAttribPropInterfaceSym!.Declarations is not null)
            {
                // More than one property on ElementAttributesProperty is an error
                Error((Node)jsxElementAttribPropInterfaceSym.Declarations[0], Diagnostics.The_global_type_JSX_0_may_not_have_more_than_one_property, Utilities.UnescapeLeadingUnderscores(nameOfAttribPropContainer));
            }
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:34146-34150 — <c>getJsxLibraryManagedAttributes</c>.</summary>
    internal ISymbol? GetJsxLibraryManagedAttributes(ISymbol? jsxNamespace)
    {
        // JSX.LibraryManagedAttributes [symbol]
        return jsxNamespace is not null ? GetSymbol(jsxNamespace.Exports!, JsxNames.LibraryManagedAttributes, SymbolFlags.Type) : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:34151-34155 — <c>getJsxElementTypeSymbol</c>.</summary>
    internal ISymbol? GetJsxElementTypeSymbol(ISymbol? jsxNamespace)
    {
        // JSX.ElementType [symbol]
        return jsxNamespace is not null ? GetSymbol(jsxNamespace.Exports!, JsxNames.ElementType, SymbolFlags.Type) : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:34161-34164 — <c>getJsxElementPropertiesName</c>.</summary>
    internal string? GetJsxElementPropertiesName(ISymbol? jsxNamespace)
        => GetNameFromJsxElementAttributesContainer(JsxNames.ElementAttributesPropertyNameContainer, jsxNamespace);

    /// <summary>ported from: src/compiler/checker.ts:34165-34172 — <c>getJsxElementChildrenPropertyName</c>.</summary>
    internal string? GetJsxElementChildrenPropertyName(ISymbol? jsxNamespace)
    {
        if (_compilerOptions.Jsx == JsxEmit.ReactJSX || _compilerOptions.Jsx == JsxEmit.ReactJSXDev)
        {
            // In these JsxEmit modes the children property is fixed to 'children'
            return "children";
        }
        return GetNameFromJsxElementAttributesContainer(JsxNames.ElementChildrenAttributeNameContainer, jsxNamespace);
    }

    /// <summary>ported from: src/compiler/checker.ts:34173-34201 — <c>getUninstantiatedJsxSignaturesOfType</c>.</summary>
    internal List<ISignature> GetUninstantiatedJsxSignaturesOfType(IType elementType, Node caller)
    {
        if ((elementType.Flags & TypeFlags.String) != 0)
        {
            return [_anySignature];
        }
        else if ((elementType.Flags & TypeFlags.StringLiteral) != 0)
        {
            var intrinsicType = GetIntrinsicAttributesTypeFromStringLiteralType((IStringLiteralType)elementType, caller);
            if (intrinsicType is null)
            {
                Error(caller, Diagnostics.Property_0_does_not_exist_on_type_1, ((IStringLiteralType)elementType).Value, "JSX." + JsxNames.IntrinsicElements);
                return [];
            }
            else
            {
                var fakeSignature = CreateSignatureForJSXIntrinsic(caller, intrinsicType);
                return [fakeSignature];
            }
        }
        var apparentElemType = GetApparentType(elementType);
        // Resolve the signatures, preferring constructor
        var signatures = GetSignaturesOfType(apparentElemType, SignatureKind.Construct);
        if (signatures.Count == 0)
        {
            // No construct signatures, try call signatures
            signatures = GetSignaturesOfType(apparentElemType, SignatureKind.Call);
        }
        if (signatures.Count == 0 && (apparentElemType.Flags & TypeFlags.Union) != 0)
        {
            // If each member has some combination of new/call signatures; make a union signature list for those
            signatures = GetUnionSignatures([.. Core.Map(((IUnionType)apparentElemType).Types, (t, _) => GetUninstantiatedJsxSignaturesOfType(t, caller))!]);
        }
        return signatures;
    }

    /// <summary>ported from: src/compiler/checker.ts:34202-34223 — <c>getIntrinsicAttributesTypeFromStringLiteralType</c>.</summary>
    internal IType? GetIntrinsicAttributesTypeFromStringLiteralType(IStringLiteralType type, Node location)
    {
        // If the elemType is a stringLiteral type, we can then provide a check to make sure that the string literal type is one of the Jsx intrinsic element type
        // For example:
        //      var CustomTag: "h1" = "h1";
        //      <CustomTag> Hello World </CustomTag>
        var intrinsicElementsType = GetJsxType(JsxNames.IntrinsicElements, location);
        if (!IsErrorType(intrinsicElementsType))
        {
            var stringLiteralTypeName = type.Value;
            var intrinsicProp = GetPropertyOfType(intrinsicElementsType, Utilities.EscapeLeadingUnderscores((string)stringLiteralTypeName!));
            if (intrinsicProp is not null)
            {
                return GetTypeOfSymbol(intrinsicProp);
            }
            var indexSignatureType = GetIndexTypeOfType(intrinsicElementsType, _stringType);
            if (indexSignatureType is not null)
            {
                return indexSignatureType;
            }
            return null;
        }
        // If we need to report an error, we already done so here. So just return any to prevent any more error downstream
        return _anyType;
    }

    /// <summary>ported from: src/compiler/checker.ts:34224-34258 — <c>checkJsxReturnAssignableToAppropriateBound</c>.</summary>
    /// <remarks>PORT-NOTE: TS's third parameter is <c>JsxOpeningLikeElement</c>; the port erases the union to
    /// <see cref="Node"/> and reads the tag name through <see cref="GetJsxTagName"/>.
    /// PORT-NOTE: <c>generateInitialErrorChain</c> is a nested function declared after its uses; C# local functions
    /// bind the same way.</remarks>
    internal void CheckJsxReturnAssignableToAppropriateBound(JsxReferenceKind refKind, IType elemInstanceType, Node openingLikeElement)
    {
        var tagName = GetJsxTagName(openingLikeElement);
        if (refKind == JsxReferenceKind.Function)
        {
            var sfcReturnConstraint = GetJsxStatelessElementTypeAt(openingLikeElement);
            if (sfcReturnConstraint is not null)
            {
                CheckTypeRelatedTo(elemInstanceType, sfcReturnConstraint, _assignableRelation, tagName, Diagnostics.Its_return_type_0_is_not_a_valid_JSX_element, GenerateInitialErrorChain);
            }
        }
        else if (refKind == JsxReferenceKind.Component)
        {
            var classConstraint = GetJsxElementClassTypeAt(openingLikeElement);
            if (classConstraint is not null)
            {
                // Issue an error if this return type isn't assignable to JSX.ElementClass, failing that
                CheckTypeRelatedTo(elemInstanceType, classConstraint, _assignableRelation, tagName, Diagnostics.Its_instance_type_0_is_not_a_valid_JSX_element, GenerateInitialErrorChain);
            }
        }
        else
        { // Mixed
            var sfcReturnConstraint = GetJsxStatelessElementTypeAt(openingLikeElement);
            var classConstraint = GetJsxElementClassTypeAt(openingLikeElement);
            if (sfcReturnConstraint is null || classConstraint is null)
            {
                return;
            }
            var combined = GetUnionType([sfcReturnConstraint, classConstraint]);
            CheckTypeRelatedTo(elemInstanceType, combined, _assignableRelation, tagName, Diagnostics.Its_element_type_0_is_not_a_valid_JSX_element, GenerateInitialErrorChain);
        }

        IDiagnosticMessageChain GenerateInitialErrorChain()
        {
            var componentName = Utilities.GetTextOfNode(tagName);
            return Utilities.ChainDiagnosticMessages(/*details*/ null, Diagnostics._0_cannot_be_used_as_a_JSX_component, componentName);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:34259-34277 — <c>getIntrinsicAttributesTypeFromJsxOpeningLikeElement</c>.</summary>
    internal IType GetIntrinsicAttributesTypeFromJsxOpeningLikeElement(Node node)
    {
        var tagName = GetJsxTagName(node);
        Debug.Assert(IsJsxIntrinsicTagName(tagName));
        var links = GetNodeLinks(node);
        if (links.ResolvedJsxElementAttributesType is null)
        {
            var symbol = GetIntrinsicTagSymbol(node);
            if ((links.JsxFlags & JsxFlags.IntrinsicNamedElement) != 0)
            {
                links.ResolvedJsxElementAttributesType = GetTypeOfSymbol(symbol);
                return links.ResolvedJsxElementAttributesType;
            }
            else if ((links.JsxFlags & JsxFlags.IntrinsicIndexedElement) != 0)
            {
                var propName = NodeTests.IsJsxNamespacedName(tagName) ? Utilities.GetEscapedTextOfJsxNamespacedName((JsxNamespacedName)tagName) : ((Identifier)tagName).EscapedText ?? "";
                links.ResolvedJsxElementAttributesType = GetApplicableIndexInfoForName(GetJsxType(JsxNames.IntrinsicElements, node), propName)?.Type ?? _errorType;
                return links.ResolvedJsxElementAttributesType;
            }
            else
            {
                links.ResolvedJsxElementAttributesType = _errorType;
                return links.ResolvedJsxElementAttributesType;
            }
        }
        return links.ResolvedJsxElementAttributesType;
    }

    /// <summary>ported from: src/compiler/checker.ts:34278-34283 — <c>getJsxElementClassTypeAt</c>.</summary>
    internal IType? GetJsxElementClassTypeAt(Node location)
    {
        var type = GetJsxType(JsxNames.ElementClass, location);
        if (IsErrorType(type)) return null;
        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:34284-34287 — <c>getJsxElementTypeAt</c>.</summary>
    internal IType GetJsxElementTypeAt(Node location)
        => GetJsxType(JsxNames.Element, location);

    /// <summary>ported from: src/compiler/checker.ts:34288-34294 — <c>getJsxStatelessElementTypeAt</c>.</summary>
    internal IType? GetJsxStatelessElementTypeAt(Node location)
    {
        var jsxElementType = GetJsxElementTypeAt(location);
        if (jsxElementType is not null)
        {
            return GetUnionType([jsxElementType, _nullType]);
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:34295-34304 — <c>getJsxElementTypeTypeAt</c>.</summary>
    internal IType? GetJsxElementTypeTypeAt(Node location)
    {
        var ns = GetJsxNamespaceAt(location);
        if (ns is null) return null;
        var sym = GetJsxElementTypeSymbol(ns);
        if (sym is null) return null;
        var type = InstantiateAliasOrInterfaceWithDefaults(sym, Utilities.IsInJSFile(location));
        if (type is null || IsErrorType(type)) return null;
        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:34305-34323 — <c>instantiateAliasOrInterfaceWithDefaults</c>.</summary>
    internal IType? InstantiateAliasOrInterfaceWithDefaults(ISymbol managedSym, bool inJs, params IType[] typeArguments)
    {
        var declaredManagedType = GetDeclaredTypeOfSymbol(managedSym); // fetches interface type, or initializes symbol links type parmaeters
        if ((managedSym.Flags & SymbolFlags.TypeAlias) != 0)
        {
            var parameters = GetSymbolLinks(managedSym).TypeParameters;
            if (Core.Length(parameters) >= typeArguments.Length)
            {
                var args = FillMissingTypeArguments([.. typeArguments], parameters, typeArguments.Length, inJs);
                return Core.Length(args) == 0 ? declaredManagedType : GetTypeAliasInstantiation(managedSym, args);
            }
        }
        if (Core.Length(((IGenericType)declaredManagedType).TypeParameters) >= typeArguments.Length)
        {
            var args = FillMissingTypeArguments([.. typeArguments], ((IGenericType)declaredManagedType).TypeParameters, typeArguments.Length, inJs);
            return CreateTypeReference((IGenericType)declaredManagedType, args);
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:34324-34328 — <c>getJsxIntrinsicTagNamesAt</c>.</summary>
    internal List<ISymbol> GetJsxIntrinsicTagNamesAt(Node location)
    {
        var intrinsics = GetJsxType(JsxNames.IntrinsicElements, location);
        return intrinsics is not null ? GetPropertiesOfType(intrinsics) : [];
    }

    /// <summary>ported from: src/compiler/checker.ts:34329-34341 — <c>checkJsxPreconditions</c>.</summary>
    internal void CheckJsxPreconditions(Node errorNode)
    {
        // Preconditions for using JSX
        if ((_compilerOptions.Jsx ?? JsxEmit.None) == JsxEmit.None)
        {
            Error(errorNode, Diagnostics.Cannot_use_JSX_unless_the_jsx_flag_is_provided);
        }

        if (GetJsxElementTypeAt(errorNode) is null)
        {
            if (_noImplicitAny)
            {
                Error(errorNode, Diagnostics.JSX_element_implicitly_has_type_any_because_the_global_type_JSX_Element_does_not_exist);
            }
        }
    }

    /// <summary>The <c>tagName</c> of a JSX opening-like element.</summary>
    /// <remarks>
    /// PORT-NOTE: not a member of the reference; TS reads <c>node.tagName</c> off the
    /// <c>JsxOpeningElement | JsxSelfClosingElement | JsxClosingElement</c> union, which C# cannot express, and three
    /// members of this band need that read.
    /// </remarks>
    private static Node GetJsxTagName(Node node) => node switch
    {
        JsxOpeningElement openingElement => openingElement.TagName!,
        JsxSelfClosingElement selfClosingElement => selfClosingElement.TagName!,
        JsxClosingElement closingElement => closingElement.TagName!,
        _ => Debug.Fail<Node>(),
    };

    // ---------------------------------------------------------------------------------------------
    // The JSX opening-like element check and the property checks it shares (checker.ts:34342-34437)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:34342-34387 — <c>checkJsxOpeningLikeElementOrOpeningFragment</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is the union <c>JsxOpeningLikeElement | JsxOpeningFragment</c>; the port
    /// erases it to <see cref="Node"/>, which is also what the landed <c>GetResolvedSignature</c> and
    /// <c>CheckDeprecatedSignature</c> take.</remarks>
    internal void CheckJsxOpeningLikeElementOrOpeningFragment(Node node)
    {
        var isNodeOpeningLikeElement = Utilities.IsJsxOpeningLikeElement(node);

        if (isNodeOpeningLikeElement)
        {
            CheckGrammarJsxElement(node);
        }

        CheckJsxPreconditions(node);

        MarkJsxAliasReferenced(node);

        var sig = GetResolvedSignature(node);
        CheckDeprecatedSignature(sig, node);

        if (isNodeOpeningLikeElement)
        {
            var elementTypeConstraint = GetJsxElementTypeTypeAt(node);
            if (elementTypeConstraint is not null)
            {
                var tagName = GetJsxTagName(node);
                var tagType = IsJsxIntrinsicTagName(tagName)
                    ? GetStringLiteralType(Utilities.IntrinsicTagNameToString(tagName))
                    : CheckExpression((Expression)tagName, CheckMode.Normal);
                CheckTypeRelatedTo(tagType, elementTypeConstraint, _assignableRelation, tagName, Diagnostics.Its_type_0_is_not_a_valid_JSX_element_type, () =>
                {
                    var componentName = Utilities.GetTextOfNode(tagName);
                    return Utilities.ChainDiagnosticMessages(/*details*/ null, Diagnostics._0_cannot_be_used_as_a_JSX_component, componentName);
                });
            }
            else
            {
                CheckJsxReturnAssignableToAppropriateBound(GetJsxReferenceKind(node), GetReturnTypeOfSignature(sig), node);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:34388-34415 — <c>isKnownProperty</c>.</summary>
    internal bool IsKnownProperty(IType targetType, string name, bool isComparingJsxAttributes)
    {
        if ((targetType.Flags & TypeFlags.Object) != 0)
        {
            // For backwards compatibility a symbol-named property is satisfied by a string index signature. This
            // is incorrect and inconsistent with element access expressions, where it is an error, so eventually
            // we should remove this exception.
            if (
                GetPropertyOfObjectType(targetType, name) is not null ||
                GetApplicableIndexInfoForName(targetType, name) is not null ||
                IsLateBoundName(name) && GetIndexInfoOfType(targetType, _stringType) is not null ||
                isComparingJsxAttributes && IsHyphenatedJsxName(name)
            )
            {
                // For JSXAttributes, if the attribute has a hyphenated name, consider that the attribute to be known.
                return true;
            }
        }
        if ((targetType.Flags & TypeFlags.Substitution) != 0)
        {
            return IsKnownProperty(((ISubstitutionType)targetType).BaseType!, name, isComparingJsxAttributes);
        }
        if ((targetType.Flags & TypeFlags.UnionOrIntersection) != 0 && IsExcessPropertyCheckTarget(targetType))
        {
            foreach (var t in ((IUnionOrIntersectionType)targetType).Types!)
            {
                if (IsKnownProperty(t, name, isComparingJsxAttributes))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:34416-34423 — <c>isExcessPropertyCheckTarget</c>.</summary>
    internal bool IsExcessPropertyCheckTarget(IType type)
        => (type.Flags & TypeFlags.Object) != 0 && (GetObjectFlags(type) & ObjectFlags.ObjectLiteralPatternWithComputedProperties) == 0 ||
            (type.Flags & TypeFlags.NonPrimitive) != 0 ||
            (type.Flags & TypeFlags.Substitution) != 0 && IsExcessPropertyCheckTarget(((ISubstitutionType)type).BaseType!) ||
            (type.Flags & TypeFlags.Union) != 0 && SomeType(type, IsExcessPropertyCheckTarget) ||
            (type.Flags & TypeFlags.Intersection) != 0 && Core.Every(((IUnionOrIntersectionType)type).Types, (t, _) => IsExcessPropertyCheckTarget(t));

    /// <summary>ported from: src/compiler/checker.ts:34424-34437 — <c>checkJsxExpression</c>.</summary>
    internal IType CheckJsxExpression(JsxExpression node, CheckMode? checkMode = null)
    {
        CheckGrammarJsxExpression(node);
        if (node.Expression is not null)
        {
            var type = CheckExpression(node.Expression, checkMode ?? CheckMode.Normal);
            if (node.DotDotDotToken is not null && !ReferenceEquals(type, _anyType) && !IsArrayType(type))
            {
                Error(node, Diagnostics.JSX_spread_child_must_be_an_array_type);
            }
            return type;
        }
        else
        {
            return _errorType;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Property accessibility and the null/undefined preconditions (checker.ts:34438-34739)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:34438-34445 — <c>getDeclarationNodeFlagsFromSymbol</c>.</summary>
    internal NodeFlags GetDeclarationNodeFlagsFromSymbol(ISymbol s)
        => s.ValueDeclaration is not null ? GetCombinedNodeFlagsCached((Node)s.ValueDeclaration) : NodeFlags.None;

    /// <summary>ported from: src/compiler/checker.ts:34446-34464 — <c>isPrototypeProperty</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's fall-through is <c>undefined</c> (<c>boolean | undefined</c>); every caller
    /// uses the answer in a boolean context, where <c>undefined</c> is false, so the port returns <c>bool</c> and states
    /// that arm as <c>false</c>.</remarks>
    internal bool IsPrototypeProperty(ISymbol symbol)
    {
        if ((symbol.Flags & SymbolFlags.Method) != 0 || (GetCheckFlags(symbol) & CheckFlags.SyntheticMethod) != 0)
        {
            return true;
        }
        if (Utilities.IsInJSFile((Node?)symbol.ValueDeclaration))
        {
            var parent = ((Node)symbol.ValueDeclaration!).Parent;
            return parent is not null && NodeTests.IsBinaryExpression(parent) &&
                Utilities.GetAssignmentDeclarationKind(parent) == AssignmentDeclarationKind.PrototypeProperty;
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:34465-34490 — <c>checkPropertyAccessibility</c>.</summary>
    /// <remarks>PORT-NOTE: TS's first parameter is a seven-member node union with no port counterpart; it is
    /// <see cref="Node"/> here and the <c>errorNode</c> read below is the reference's <c>node.name</c> arm.</remarks>
    internal bool CheckPropertyAccessibility(Node node, bool isSuper, bool writing, IType type, ISymbol prop, bool reportError = true)
    {
        var errorNode = !reportError ? null :
            node.Kind == SyntaxKind.QualifiedName ? ((QualifiedName)node).Right :
            node.Kind == SyntaxKind.ImportType ? node :
            node.Kind == SyntaxKind.BindingElement && ((BindingElement)node).PropertyName is not null ? ((BindingElement)node).PropertyName : Utilities.GetNamedDeclarationName(node);

        return CheckPropertyAccessibilityAtLocation(node, isSuper, writing, type, prop, errorNode);
    }

    /// <summary>ported from: src/compiler/checker.ts:34491-34605 — <c>checkPropertyAccessibilityAtLocation</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>prop.declarations?.some(isClassInstanceProperty)</c> is hoisted into
    /// <c>hasClassInstancePropertyDeclaration</c> because C# has no null-conditional call on a property read inside a
    /// larger expression.
    /// </remarks>
    internal bool CheckPropertyAccessibilityAtLocation(Node location, bool isSuper, bool writing, IType containingType, ISymbol prop, Node? errorNode)
    {
        var flags = Utilities.GetDeclarationModifierFlagsFromSymbol(prop, writing);
        var hasClassInstancePropertyDeclaration = prop.Declarations is not null && Core.Some(prop.Declarations, Utilities.IsClassInstanceProperty);

        if (isSuper)
        {
            // TS 1.0 spec (April 2014): 4.8.2
            // - In a constructor, instance member function, instance member accessor, or
            //   instance member variable initializer where this references a derived class instance,
            //   a super property access is permitted and must specify a public instance member function of the base class.
            // - In a static member function or static member accessor
            //   where this references the constructor function object of a derived class,
            //   a super property access is permitted and must specify a public static member function of the base class.
            if (_languageVersion < ScriptTarget.ES2015)
            {
                if (SymbolHasNonMethodDeclaration(prop))
                {
                    if (errorNode is not null)
                    {
                        Error(errorNode, Diagnostics.Only_public_and_protected_methods_of_the_base_class_are_accessible_via_the_super_keyword);
                    }
                    return false;
                }
            }
            if ((flags & ModifierFlags.Abstract) != 0)
            {
                // A method cannot be accessed in a super property access if the method is abstract.
                // This error could mask a private property access error. But, a member
                // cannot simultaneously be private and abstract, so this will trigger an
                // additional error elsewhere.
                if (errorNode is not null)
                {
                    Error(errorNode, Diagnostics.Abstract_method_0_in_class_1_cannot_be_accessed_via_super_expression, Diagnostic.DeferArg(() => SymbolToString(prop)), Diagnostic.DeferArg(() => TypeToString(GetDeclaringClass(prop)!)));
                }
                return false;
            }
            // A class field cannot be accessed via super.* from a derived class.
            // This is true for both [[Set]] (old) and [[Define]] (ES spec) semantics.
            if ((flags & ModifierFlags.Static) == 0 && hasClassInstancePropertyDeclaration)
            {
                if (errorNode is not null)
                {
                    Error(errorNode, Diagnostics.Class_field_0_defined_by_the_parent_class_is_not_accessible_in_the_child_class_via_super, Diagnostic.DeferArg(() => SymbolToString(prop)));
                }
                return false;
            }
        }

        // Referencing abstract properties within their own constructors is not allowed
        if (
            (flags & ModifierFlags.Abstract) != 0 && SymbolHasNonMethodDeclaration(prop) &&
            (Utilities.IsThisProperty(location) || Utilities.IsThisInitializedObjectBindingExpression(location) || NodeTests.IsObjectBindingPattern(location.Parent!) && Utilities.IsThisInitializedDeclaration(location.Parent!.Parent!))
        )
        {
            var parentSymbol = GetParentOfSymbol(prop);
            if (parentSymbol is not null && (parentSymbol.Flags & SymbolFlags.Class) != 0 && IsNodeUsedDuringClassInitialization(location))
            {
                if (errorNode is not null)
                {
                    Error(errorNode, Diagnostics.Abstract_property_0_in_class_1_cannot_be_accessed_in_the_constructor, Diagnostic.DeferArg(() => SymbolToString(prop)), Diagnostic.DeferArg(() => SymbolToString(parentSymbol)));
                }
                return false;
            }
        }

        // Public properties are otherwise accessible.
        if ((flags & ModifierFlags.NonPublicAccessibilityModifier) == 0)
        {
            return true;
        }

        // Property is known to be private or protected at this point

        // Private property is accessible if the property is within the declaring class
        if ((flags & ModifierFlags.Private) != 0)
        {
            var declaringClassDeclaration = (IClassLikeDeclarationBase)Utilities.GetClassLikeDeclarationOfSymbol(GetParentOfSymbol(prop)!)!;
            if (!IsNodeWithinClass(location, declaringClassDeclaration))
            {
                if (errorNode is not null)
                {
                    Error(errorNode, Diagnostics.Property_0_is_private_and_only_accessible_within_class_1, Diagnostic.DeferArg(() => SymbolToString(prop)), Diagnostic.DeferArg(() => TypeToString(GetDeclaringClass(prop)!)));
                }
                return false;
            }
            return true;
        }

        // Property is known to be protected at this point

        // All protected properties of a supertype are accessible in a super access
        if (isSuper)
        {
            return true;
        }

        // Find the first enclosing class that has the declaring classes of the protected constituents
        // of the property as base classes
        var enclosingClass = ForEachEnclosingClass(location, enclosingDeclaration =>
        {
            var enclosingClass = (InterfaceType)GetDeclaredTypeOfSymbol(GetSymbolOfDeclaration((IDeclaration)enclosingDeclaration));
            return IsClassDerivedFromDeclaringClasses(enclosingClass, prop, writing);
        });
        // A protected property is accessible if the property is within the declaring class or classes derived from it
        if (enclosingClass is null)
        {
            // allow PropertyAccessibility if context is in function with this parameter
            // static member access is disallowed
            enclosingClass = GetEnclosingClassFromThisParameter(location);
            enclosingClass = enclosingClass is not null ? IsClassDerivedFromDeclaringClasses(enclosingClass, prop, writing) : null;
            if ((flags & ModifierFlags.Static) != 0 || enclosingClass is null)
            {
                if (errorNode is not null)
                {
                    Error(errorNode, Diagnostics.Property_0_is_protected_and_only_accessible_within_class_1_and_its_subclasses, Diagnostic.DeferArg(() => SymbolToString(prop)), Diagnostic.DeferArg(() => TypeToString(GetDeclaringClass(prop) ?? containingType)));
                }
                return false;
            }
        }
        // No further restrictions for static properties
        if ((flags & ModifierFlags.Static) != 0)
        {
            return true;
        }
        if ((containingType.Flags & TypeFlags.TypeParameter) != 0)
        {
            // get the original type -- represented as the type constraint of the 'this' type
            containingType = ((ITypeParameter)containingType).IsThisType == true ? GetConstraintOfTypeParameter((ITypeParameter)containingType)! : GetBaseConstraintOfType((ITypeParameter)containingType)!; // TODO: GH#18217 Use a different variable that's allowed to be undefined
        }
        if (!HasBaseType(containingType, enclosingClass))
        {
            if (errorNode is not null)
            {
                Error(errorNode, Diagnostics.Property_0_is_protected_and_only_accessible_through_an_instance_of_class_1_This_is_an_instance_of_class_2, Diagnostic.DeferArg(() => SymbolToString(prop)), Diagnostic.DeferArg(() => TypeToString(enclosingClass)), Diagnostic.DeferArg(() => TypeToString(containingType)));
            }
            return false;
        }
        return true;
    }

    /// <summary>ported from: src/compiler/checker.ts:34606-34629 — <c>getEnclosingClassFromThisParameter</c>.</summary>
    internal InterfaceType? GetEnclosingClassFromThisParameter(Node node)
    {
        // 'this' type for a node comes from, in priority order...
        // 1. The type of a syntactic 'this' parameter in the enclosing function scope
        var thisParameter = GetThisParameterFromNodeContext(node);
        IType? thisType = thisParameter?.Type is not null ? GetTypeFromTypeNode(thisParameter.Type) : null;
        if (thisType is not null)
        {
            // 2. The constraint of a type parameter used for an explicit 'this' parameter
            if ((thisType.Flags & TypeFlags.TypeParameter) != 0)
            {
                thisType = GetConstraintOfTypeParameter((ITypeParameter)thisType);
            }
        }
        else
        {
            // 3. The 'this' parameter of a contextual type
            var thisContainer = Utilities.GetThisContainer(node, /*includeArrowFunctions*/ false, /*includeClassComputedPropertyName*/ false);
            if (Utilities.IsFunctionLike(thisContainer))
            {
                thisType = GetContextualThisParameterType(thisContainer!);
            }
        }
        if (thisType is not null && (GetObjectFlags(thisType) & (ObjectFlags.ClassOrInterface | ObjectFlags.Reference)) != 0)
        {
            return (InterfaceType)GetTargetType(thisType);
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:34630-34634 — <c>getThisParameterFromNodeContext</c>.</summary>
    internal ParameterDeclaration? GetThisParameterFromNodeContext(Node node)
    {
        var thisContainer = Utilities.GetThisContainer(node, /*includeArrowFunctions*/ false, /*includeClassComputedPropertyName*/ false);
        return thisContainer is not null && Utilities.IsFunctionLike(thisContainer) ? Utilities.GetThisParameter(thisContainer!) : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:34635-34638 — <c>symbolHasNonMethodDeclaration</c>.</summary>
    internal bool SymbolHasNonMethodDeclaration(ISymbol symbol)
        => ForEachProperty(symbol, prop => (prop.Flags & SymbolFlags.Method) == 0);

    /// <summary>ported from: src/compiler/checker.ts:34639-34642 — <c>checkNonNullExpression</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is <c>Expression | QualifiedName</c>; the port erases it to
    /// <see cref="Node"/>.</remarks>
    internal IType CheckNonNullExpression(Node node)
        => CheckNonNullType(CheckExpression(node, CheckMode.Normal), node);

    /// <summary>ported from: src/compiler/checker.ts:34643-34646 — <c>isNullableType</c>.</summary>
    internal bool IsNullableType(IType type)
        => HasTypeFacts(type, TypeFacts.IsUndefinedOrNull);

    /// <summary>ported from: src/compiler/checker.ts:34647-34650 — <c>getNonNullableTypeIfNeeded</c>.</summary>
    internal IType GetNonNullableTypeIfNeeded(IType type)
        => IsNullableType(type) ? GetNonNullableType(type) : type;

    /// <summary>ported from: src/compiler/checker.ts:34651-34681 — <c>reportObjectPossiblyNullOrUndefinedError</c>.</summary>
    internal void ReportObjectPossiblyNullOrUndefinedError(Node node, TypeFacts facts)
    {
        var nodeText = Utilities.IsEntityNameExpression(node) ? Utilities.EntityNameToString(node) : null;
        if (node.Kind == SyntaxKind.NullKeyword)
        {
            Error(node, Diagnostics.The_value_0_cannot_be_used_here, "null");
            return;
        }
        if (nodeText is not null && nodeText.Length < 100)
        {
            if (NodeTests.IsIdentifier(node) && nodeText == "undefined")
            {
                Error(node, Diagnostics.The_value_0_cannot_be_used_here, "undefined");
                return;
            }
            Error(
                node,
                (facts & TypeFacts.IsUndefined) != 0 ? (facts & TypeFacts.IsNull) != 0 ?
                    Diagnostics._0_is_possibly_null_or_undefined :
                    Diagnostics._0_is_possibly_undefined :
                    Diagnostics._0_is_possibly_null,
                nodeText);
        }
        else
        {
            Error(
                node,
                (facts & TypeFacts.IsUndefined) != 0 ? (facts & TypeFacts.IsNull) != 0 ?
                    Diagnostics.Object_is_possibly_null_or_undefined :
                    Diagnostics.Object_is_possibly_undefined :
                    Diagnostics.Object_is_possibly_null);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:34682-34691 — <c>reportCannotInvokePossiblyNullOrUndefinedError</c>.</summary>
    internal void ReportCannotInvokePossiblyNullOrUndefinedError(Node node, TypeFacts facts)
    {
        Error(
            node,
            (facts & TypeFacts.IsUndefined) != 0 ? (facts & TypeFacts.IsNull) != 0 ?
                Diagnostics.Cannot_invoke_an_object_which_is_possibly_null_or_undefined :
                Diagnostics.Cannot_invoke_an_object_which_is_possibly_undefined :
                Diagnostics.Cannot_invoke_an_object_which_is_possibly_null);
    }

    /// <summary>ported from: src/compiler/checker.ts:34692-34716 — <c>checkNonNullTypeWithReporter</c>.</summary>
    internal IType CheckNonNullTypeWithReporter(IType type, Node node, Action<Node, TypeFacts> reportError)
    {
        if (_strictNullChecks && (type.Flags & TypeFlags.Unknown) != 0)
        {
            if (Utilities.IsEntityNameExpression(node))
            {
                var nodeText = Utilities.EntityNameToString(node);
                if (nodeText.Length < 100)
                {
                    Error(node, Diagnostics._0_is_of_type_unknown, nodeText);
                    return _errorType;
                }
            }
            Error(node, Diagnostics.Object_is_of_type_unknown);
            return _errorType;
        }
        var facts = GetTypeFacts(type, TypeFacts.IsUndefinedOrNull);
        if ((facts & TypeFacts.IsUndefinedOrNull) != 0)
        {
            reportError(node, facts);
            var t = GetNonNullableType(type);
            return (t.Flags & (TypeFlags.Nullable | TypeFlags.Never)) != 0 ? _errorType : t;
        }
        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:34717-34720 — <c>checkNonNullType</c>.</summary>
    internal IType CheckNonNullType(IType type, Node node)
        => CheckNonNullTypeWithReporter(type, node, ReportObjectPossiblyNullOrUndefinedError);

    /// <summary>ported from: src/compiler/checker.ts:34721-34739 — <c>checkNonNullNonVoidType</c>.</summary>
    internal IType CheckNonNullNonVoidType(IType type, Node node)
    {
        var nonNullType = CheckNonNullType(type, node);
        if ((nonNullType.Flags & TypeFlags.Void) != 0)
        {
            if (Utilities.IsEntityNameExpression(node))
            {
                var nodeText = Utilities.EntityNameToString(node);
                if (NodeTests.IsIdentifier(node) && nodeText == "undefined")
                {
                    Error(node, Diagnostics.The_value_0_cannot_be_used_here, nodeText);
                    return nonNullType;
                }
                if (nodeText.Length < 100)
                {
                    Error(node, Diagnostics._0_is_possibly_undefined, nodeText);
                    return nonNullType;
                }
            }
            Error(node, Diagnostics.Object_is_possibly_undefined);
        }
        return nonNullType;
    }
}
