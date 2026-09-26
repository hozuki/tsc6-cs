namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:51685-51725 (checkExternalEmitHelpers)
// ported from: src/compiler/checker.ts:51727-51784 (getHelperNames)
// ported from: src/compiler/checker.ts:51786-51794 (resolveHelpersModule)
// ported from: src/compiler/checker.ts:51796-52217 (checkGrammarModifiers)
// ported from: src/compiler/checker.ts:52219-52224 (reportObviousModifierErrors)
// ported from: src/compiler/checker.ts:52226-52229 (findFirstModifierExcept)
// ported from: src/compiler/checker.ts:52231-52281 (findFirstIllegalModifier)
// ported from: src/compiler/checker.ts:52283-52286 (reportObviousDecoratorErrors)
// ported from: src/compiler/checker.ts:52288-52290 (findFirstIllegalDecorator)
// ported from: src/compiler/checker.ts:52292-52302 (checkGrammarAsyncModifier)
// ported from: src/compiler/checker.ts:52304-52309 (checkGrammarForDisallowedTrailingComma)
// ported from: src/compiler/checker.ts:52311-52318 (checkGrammarTypeParameterList)
// ported from: src/compiler/checker.ts:52320-52352 (checkGrammarParameterList)
// ported from: src/compiler/checker.ts:52354-52356 (getNonSimpleParameters)
// ported from: src/compiler/checker.ts:52358-52380 (checkGrammarForUseStrictSimpleParameterList)
// ported from: src/compiler/checker.ts:52382-52390 (checkGrammarFunctionLikeDeclaration)
// ported from: src/compiler/checker.ts:52392-52396 (checkGrammarClassLikeDeclaration)
// ported from: src/compiler/checker.ts:52398-52413 (checkGrammarArrowFunction)
// ported from: src/compiler/checker.ts:52415-52452 (checkGrammarIndexSignatureParameters)
// ported from: src/compiler/checker.ts:52454-52457 (checkGrammarIndexSignature)
// ported from: src/compiler/checker.ts:52459-52467 (checkGrammarForAtLeastOneTypeArgument)
// ported from: src/compiler/checker.ts:52469-52472 (checkGrammarTypeArguments)
// ported from: src/compiler/checker.ts:52474-52479 (checkGrammarTaggedTemplateChain)
// ported from: src/compiler/checker.ts:52481-52491 (checkGrammarHeritageClause)
// ported from: src/compiler/checker.ts:52493-52498 (checkGrammarExpressionWithTypeArguments)
// ported from: src/compiler/checker.ts:52500-52534 (checkGrammarClassDeclarationHeritageClauses)
// ported from: src/compiler/checker.ts:52536-52558 (checkGrammarInterfaceDeclaration)
// ported from: src/compiler/checker.ts:52560-52571 (checkGrammarComputedPropertyName)
// ported from: src/compiler/checker.ts:52573-52587 (checkGrammarForGenerator)
// ported from: src/compiler/checker.ts:52589-52591 (checkGrammarForInvalidQuestionMark)
// ported from: src/compiler/checker.ts:52593-52595 (checkGrammarForInvalidExclamationToken)
// ported from: src/compiler/checker.ts:52597-52710 (checkGrammarObjectLiteralExpression)
// ported from: src/compiler/checker.ts:52712-52735 (checkGrammarJsxElement)
// ported from: src/compiler/checker.ts:52737-52744 (checkGrammarJsxName)
// ported from: src/compiler/checker.ts:52746-52750 (checkGrammarJsxExpression)
// ported from: src/compiler/checker.ts:52752-52858 (checkGrammarForInOrForOfStatement)
// ported from: src/compiler/checker.ts:52860-52908 (checkGrammarAccessor)
// ported from: src/compiler/checker.ts:52910-52912 (doesAccessorHaveCorrectParameterCount)
// ported from: src/compiler/checker.ts:52914-52918 (getAccessorThisParameter)
// ported from: src/compiler/checker.ts:52920-52970 (checkGrammarTypeOperatorNode)
// ported from: src/compiler/checker.ts:52972-52977 (checkGrammarForInvalidDynamicName)
// ported from: src/compiler/checker.ts:52979-53027 (checkGrammarMethod)
// ported from: src/compiler/checker.ts:53029-53081 (checkGrammarBreakOrContinueStatement)
// ported from: src/compiler/checker.ts:53083-53100 (checkGrammarBindingElement)
// ported from: src/compiler/checker.ts:53102-53106 (isStringOrNumberLiteralExpression)
// ported from: src/compiler/checker.ts:53108-53112 (isBigIntLiteralExpression)
// ported from: src/compiler/checker.ts:53114-53121 (isSimpleLiteralEnumReference)
// ported from: src/compiler/checker.ts:53123-53142 (checkAmbientInitializer)
// ported from: src/compiler/checker.ts:53144-53199 (checkGrammarVariableDeclaration)
// ported from: src/compiler/checker.ts:53201-53216 (checkESModuleMarker)
// ported from: src/compiler/checker.ts:53218-53233 (checkGrammarNameInLetOrConstDeclarations)
// ported from: src/compiler/checker.ts:53235-53279 (checkGrammarVariableDeclarationList)
// ported from: src/compiler/checker.ts:53281-53296 (allowBlockDeclarations)
// ported from: src/compiler/checker.ts:53298-53310 (checkGrammarForDisallowedBlockScopedVariableStatement)
// ported from: src/compiler/checker.ts:53312-53337 (checkGrammarMetaProperty)
// ported from: src/compiler/checker.ts:53339-53341 (hasParseDiagnostics)
// ported from: src/compiler/checker.ts:53343-53351 (grammarErrorOnFirstToken)
// ported from: src/compiler/checker.ts:53353-53360 (grammarErrorAtPos)
// ported from: src/compiler/checker.ts:53362-53369 (grammarErrorOnNodeSkippedOn)
// ported from: src/compiler/checker.ts:53371-53378 (grammarErrorOnNode)
// ported from: src/compiler/checker.ts:53380-53387 (checkGrammarConstructorTypeParameters)
// ported from: src/compiler/checker.ts:53389-53394 (checkGrammarConstructorTypeAnnotation)
// ported from: src/compiler/checker.ts:53396-53454 (checkGrammarProperty)
// ported from: src/compiler/checker.ts:53456-53483 (checkGrammarTopLevelElementForRequiredDeclareModifier)
// ported from: src/compiler/checker.ts:53485-53494 (checkGrammarTopLevelElementsForRequiredDeclareModifier)
// ported from: src/compiler/checker.ts:53496-53498 (checkGrammarSourceFile)
// ported from: src/compiler/checker.ts:53500-53527 (checkGrammarStatementInAmbientContext)
// ported from: src/compiler/checker.ts:53529-53553 (checkGrammarNumericLiteral)
// ported from: src/compiler/checker.ts:53555-53567 (checkGrammarBigIntLiteral)
// ported from: src/compiler/checker.ts:53569-53577 (grammarErrorAfterFirstToken)
// ported from: src/compiler/checker.ts:53579-53590 (getAmbientModules)
// ported from: src/compiler/checker.ts:53592-53613 (checkGrammarImportClause)
// ported from: src/compiler/checker.ts:53615-53626 (checkGrammarNamedImportsOrExports)
// ported from: src/compiler/checker.ts:53628-53668 (checkGrammarImportCallExpression)

/// <summary>
/// The grammar block: the syntactic diagnostics the checker reports before it checks a declaration — modifier and
/// decorator legality, parameter and type-argument list shape, ambient-context statements, `use strict`, the
/// `__importHelpers` module requirement and the four positions a computed name may take.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: the reference's union-typed parameters have no C# counterpart: <c>HasModifiers</c>/<c>HasDecorators</c>/
/// <c>HasIllegalModifiers</c>/<c>HasIllegalDecorators</c>, <c>FunctionLikeDeclaration | MethodSignature</c>,
/// <c>AccessorDeclaration</c>, <c>DeclarationName</c>, <c>JsxOpeningLikeElement</c>, <c>JsxTagNameExpression</c>,
/// <c>ForInOrOfStatement</c>, <c>DeclarationWithName</c>, <c>NamedImportsOrExports</c>, <c>BreakOrContinueStatement</c>
/// and <c>ImportCall</c> are all intersections or unions the generated model does not declare as a single type. Where
/// the reference narrows by kind the port takes <see cref="Node"/> and switches on <c>node.Kind</c>, casting to the
/// class that kind selects — the shape <c>Program.DiagnosticsAggregation.cs</c> uses for its own unions; where the
/// union adds no kind the port takes the concrete class the caller passes.
/// </para>
/// <para>
/// PORT-NOTE: the <c>modifiers</c> slot of those four unions is read through <c>Utilities.ModifiersOf</c>, whose home is
/// <c>Utilities</c> because the reference declares <c>modifiersOf</c> in <c>utilities.ts</c>; per M6Check's ruling this
/// block calls it qualified and declares no copy of its own (a checker copy would also fail
/// <c>CrossSliceOwnershipTests</c>). This file's <see cref="FindModifier"/> is the other half — core.ts's <c>find</c>
/// over that array — and stays local, because <c>Core.Find</c> takes an <c>IList&lt;T&gt;</c> while the shared accessor
/// answers the <c>IEnumerable&lt;Node&gt;</c> both element types can share. Where a member needs that array's length or
/// an element, it narrows the shared result locally with <c>as IReadOnlyList&lt;Node&gt;</c> (the runtime value is always
/// a <c>NodeArray</c>, a <c>List&lt;T&gt;</c>) rather than widening the shared signature.
/// </para>
/// <para>
/// PORT-NOTE: every <c>grammarError*</c> helper keeps the reference's arity and optionality, answers <c>bool</c> and
/// appends to the same <see cref="_diagnostics"/> collection the reference's <c>diagnostics.add</c> targets. TS's
/// <c>keyof CompilerOptions</c> first parameter of <c>grammarErrorOnNodeSkippedOn</c> is the option's name, which is
/// what <see cref="ErrorSkippedOn"/> takes.
/// </para>
/// <para>
/// PORT-NOTE: <c>getHelperNames</c>'s arrays and its <c>Debug.fail</c> default port as <c>List&lt;string&gt;</c> and
/// <c>Debug.Fail&lt;List&lt;string&gt;&gt;</c>; <c>checkGrammarNamedImportsOrExports</c>'s <c>forEach</c> callback
/// over the two specifier classes is extracted into <see cref="CheckGrammarNamedSpecifier"/>, because the port's
/// <c>Core.ForEach</c> constrains its result to a reference type and the only element type the two arms share is
/// <see cref="Node"/>.
/// </para>
/// <para>
/// PORT-NOTE: where TS's inferred return type is <c>boolean | undefined</c> because the body falls off its end (which
/// most of the <c>checkGrammar*</c> members do), and every caller consumes the result in a boolean position, the port
/// returns <c>bool</c> with <c>false</c> for the implicit <c>undefined</c>. The two members whose result the reference
/// compares against <c>undefined</c> (<c>reportObviousDecoratorErrors</c>/<c>reportObviousModifierErrors</c>) keep the
/// three-valued <c>bool?</c>, and <see cref="CheckGrammarModifiers"/> spells their <c>||</c> out as two tests so the
/// falsy-but-defined case still evaluates the second.
/// </para>
/// <para>
/// PORT-NOTE: the reference's <c>seen.get/set</c> maps whose keys are property names port to
/// <c>Dictionary&lt;string, DeclarationMeaning&gt;</c>/<c>HashSet&lt;string&gt;</c> — the same identity the erased
/// <c>__String</c> has.
/// </para>
/// <para>
/// PORT-NOTE: names this block references but does not declare, each with its owner. The utilities the M6 utilities
/// slice ports: <c>isEffectiveExternalModule</c> (utilities.ts:2157), <c>nodeCanBeDecorated</c> (:3471-3479),
/// <c>isIntrinsicJsxName</c> (:6277), <c>getThisParameter</c> (:6782), <c>hasEffectiveModifiers</c> (:7180),
/// <c>hasAbstractModifier</c> (:7216), <c>hasDecorators</c> (:7236), <c>getJSXTransformEnabled</c> (:9406),
/// <c>getEscapedTextOfJsxAttributeName</c> (:11127), <c>isIterationStatement</c> (utilitiesPublic.ts:2116-2118),
/// <c>isCaseOrDefaultClause</c> (:2504), <c>isDeclarationReadonly</c> (utilities.ts:2653) and
/// <c>isVarConstLike</c> (utilities.ts:2685, shadowed in the reference by checker.ts:53788).
/// </para>
/// <para>
/// PORT-NOTE: names owned by sibling bands: <c>getParameterCount</c> (checker.ts:38559), <c>checkAwaitGrammar</c>
/// (:39891), <c>checkExpressionCached</c> (:41335), <c>resolveExternalModule</c> (:4730-4765),
/// <c>getAllAccessorDeclarationsForDeclaration</c> (:51190), <c>visibilityToString</c> (:11214), and the
/// 53670-53860 stretch's <c>getEffectivePropertyNameForPropertyNameNode</c> (:53761), <c>isVarConstLike</c> (:53788)
/// and <c>getImportHelpersImportSpecifier</c> (:53805). All are called unqualified.
/// </para>
/// <para>
/// PORT-NOTE: one state slot of this block is not declared anywhere yet and is referenced unqualified rather than
/// declared here, so that nothing can read a silently wrong one: <c>_moduleKind</c> (checker.ts:1530, whose factory
/// <c>Utilities.GetEmitModuleKind</c> has landed). It is the slot <c>Checker.Check.Calls.cs</c>,
/// <c>Checker.Check.Classes.cs</c> and <c>Checker.Check.Modules.cs</c> also read and no slice declares.
/// <c>_globals</c>, <c>_languageVersion</c>, <c>_legacyDecorators</c>, <c>_unknownSymbol</c>,
/// <c>_ambientModulesCache</c> (<c>Checker.Initialize.cs</c>), the two link tables and
/// <c>GetCombinedNodeFlagsCached</c> are declared already and are read here.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // The __importHelpers block (checker.ts:51685-51794)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:51685-51725 — <c>checkExternalEmitHelpers</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>helpers &amp; ~links.requestedExternalEmitHelpers</c> and the <c>&lt;&lt;= 1</c> loop step
    /// are spelled with the enum's backing type, because C# has no shift operator over an enum.
    /// </remarks>
    internal void CheckExternalEmitHelpers(Node location, ExternalEmitHelpers helpers)
    {
        if (Js.IsTruthy(_compilerOptions.ImportHelpers))
        {
            var sourceFile = Utilities.GetSourceFileOfNode(location)!;
            if (Utilities.IsEffectiveExternalModule(sourceFile, _compilerOptions) && (location.Flags & NodeFlags.Ambient) == 0)
            {
                var helpersModule = ResolveHelpersModule(sourceFile, location);
                if (!ReferenceEquals(helpersModule, _unknownSymbol))
                {
                    var links = GetSymbolLinks(helpersModule);
                    links.RequestedExternalEmitHelpers ??= (ExternalEmitHelpers)0;
                    if ((links.RequestedExternalEmitHelpers.Value & helpers) != helpers)
                    {
                        var uncheckedHelpers = helpers & ~links.RequestedExternalEmitHelpers.Value;
                        for (var helper = ExternalEmitHelpers.FirstEmitHelper; helper <= ExternalEmitHelpers.LastEmitHelper; helper = (ExternalEmitHelpers)((int)helper << 1))
                        {
                            if ((uncheckedHelpers & helper) != 0)
                            {
                                foreach (var name in GetHelperNames(helper))
                                {
                                    var symbol = ResolveSymbol(GetSymbol(GetExportsOfModuleTable(helpersModule), Utilities.EscapeLeadingUnderscores(name), SymbolFlags.Value));
                                    if (symbol is null)
                                    {
                                        Error(location, Diagnostics.This_syntax_requires_an_imported_helper_named_1_which_does_not_exist_in_0_Consider_upgrading_your_version_of_0, Utilities.ExternalHelpersModuleNameText, name);
                                    }
                                    else if ((helper & ExternalEmitHelpers.ClassPrivateFieldGet) != 0)
                                    {
                                        if (!Core.Some(GetSignaturesOfSymbol(symbol), signature => GetParameterCount(signature) > 3))
                                        {
                                            Error(location, Diagnostics.This_syntax_requires_an_imported_helper_named_1_with_2_parameters_which_is_not_compatible_with_the_one_in_0_Consider_upgrading_your_version_of_0, Utilities.ExternalHelpersModuleNameText, name, 4);
                                        }
                                    }
                                    else if ((helper & ExternalEmitHelpers.ClassPrivateFieldSet) != 0)
                                    {
                                        if (!Core.Some(GetSignaturesOfSymbol(symbol), signature => GetParameterCount(signature) > 4))
                                        {
                                            Error(location, Diagnostics.This_syntax_requires_an_imported_helper_named_1_with_2_parameters_which_is_not_compatible_with_the_one_in_0_Consider_upgrading_your_version_of_0, Utilities.ExternalHelpersModuleNameText, name, 5);
                                        }
                                    }
                                    else if ((helper & ExternalEmitHelpers.SpreadArray) != 0)
                                    {
                                        if (!Core.Some(GetSignaturesOfSymbol(symbol), signature => GetParameterCount(signature) > 2))
                                        {
                                            Error(location, Diagnostics.This_syntax_requires_an_imported_helper_named_1_with_2_parameters_which_is_not_compatible_with_the_one_in_0_Consider_upgrading_your_version_of_0, Utilities.ExternalHelpersModuleNameText, name, 3);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    links.RequestedExternalEmitHelpers |= helpers;
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:51727-51784 — <c>getHelperNames</c>.</summary>
    internal List<string> GetHelperNames(ExternalEmitHelpers helper)
    {
        switch (helper)
        {
            case ExternalEmitHelpers.Extends:
                return ["__extends"];
            case ExternalEmitHelpers.Assign:
                return ["__assign"];
            case ExternalEmitHelpers.Rest:
                return ["__rest"];
            case ExternalEmitHelpers.Decorate:
                return _legacyDecorators ? ["__decorate"] : ["__esDecorate", "__runInitializers"];
            case ExternalEmitHelpers.Metadata:
                return ["__metadata"];
            case ExternalEmitHelpers.Param:
                return ["__param"];
            case ExternalEmitHelpers.Awaiter:
                return ["__awaiter"];
            case ExternalEmitHelpers.Generator:
                return ["__generator"];
            case ExternalEmitHelpers.Values:
                return ["__values"];
            case ExternalEmitHelpers.Read:
                return ["__read"];
            case ExternalEmitHelpers.SpreadArray:
                return ["__spreadArray"];
            case ExternalEmitHelpers.Await:
                return ["__await"];
            case ExternalEmitHelpers.AsyncGenerator:
                return ["__asyncGenerator"];
            case ExternalEmitHelpers.AsyncDelegator:
                return ["__asyncDelegator"];
            case ExternalEmitHelpers.AsyncValues:
                return ["__asyncValues"];
            case ExternalEmitHelpers.ExportStar:
                return ["__exportStar"];
            case ExternalEmitHelpers.ImportStar:
                return ["__importStar"];
            case ExternalEmitHelpers.ImportDefault:
                return ["__importDefault"];
            case ExternalEmitHelpers.MakeTemplateObject:
                return ["__makeTemplateObject"];
            case ExternalEmitHelpers.ClassPrivateFieldGet:
                return ["__classPrivateFieldGet"];
            case ExternalEmitHelpers.ClassPrivateFieldSet:
                return ["__classPrivateFieldSet"];
            case ExternalEmitHelpers.ClassPrivateFieldIn:
                return ["__classPrivateFieldIn"];
            case ExternalEmitHelpers.SetFunctionName:
                return ["__setFunctionName"];
            case ExternalEmitHelpers.PropKey:
                return ["__propKey"];
            case ExternalEmitHelpers.AddDisposableResourceAndDisposeResources:
                return ["__addDisposableResource", "__disposeResources"];
            case ExternalEmitHelpers.RewriteRelativeImportExtension:
                return ["__rewriteRelativeImportExtension"];
            default:
                return Debug.Fail<List<string>>("Unrecognized helper");
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:51786-51794 — <c>resolveHelpersModule</c>.</summary>
    internal ISymbol ResolveHelpersModule(SourceFile file, Node errorNode)
    {
        var links = GetNodeLinks(file);
        if (links.ExternalHelpersModule is null)
        {
            links.ExternalHelpersModule = ResolveExternalModule(GetImportHelpersImportSpecifier(file), Utilities.ExternalHelpersModuleNameText, Diagnostics.This_syntax_requires_an_imported_helper_but_module_0_cannot_be_found, errorNode) ?? _unknownSymbol;
        }
        return links.ExternalHelpersModule;
    }

    // ---------------------------------------------------------------------------------------------
    // The modifier slot (checker.ts's HasModifiers/HasDecorators/HasIllegalModifiers unions)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:51796-52217 — <c>checkGrammarModifiers</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union
    /// <c>HasModifiers | HasDecorators | HasIllegalModifiers | HasIllegalDecorators</c>, whose every member declares
    /// <c>modifiers</c>; the port takes <see cref="Node"/> and reads that slot through <see cref="Utilities.ModifiersOf"/>. The
    /// arms that read a kind-specific slot (<c>name</c>, <c>body</c>, <c>declarationList</c>) cast the node to the class
    /// <c>node.Kind</c> selects, which is what the reference's narrowing does.
    /// </remarks>
    internal bool CheckGrammarModifiers(Node node)
    {
        var decoratorResult = ReportObviousDecoratorErrors(node);
        if (decoratorResult == true)
        {
            return true;
        }

        var modifierResult = ReportObviousModifierErrors(node);
        if (modifierResult is not null)
        {
            return modifierResult.Value;
        }

        if (NodeTests.IsParameter(node) && Utilities.ParameterIsThisKeyword((ParameterDeclaration)node))
        {
            return GrammarErrorOnFirstToken(node, Diagnostics.Neither_decorators_nor_modifiers_may_be_applied_to_this_parameters);
        }

        var blockScopeKind = NodeTests.IsVariableStatement(node) ? ((VariableStatement)node).DeclarationList!.Flags & NodeFlags.BlockScoped : NodeFlags.None;
        Node? lastStatic = null, lastDeclare = null, lastAsync = null, lastOverride = null, firstDecorator = null;
        var flags = ModifierFlags.None;
        var sawExportBeforeDecorators = false;
        // We parse decorators and modifiers in four contiguous chunks:
        // [...leadingDecorators, ...leadingModifiers, ...trailingDecorators, ...trailingModifiers]. It is an error to
        // have both leading and trailing decorators.
        var hasLeadingDecorators = false;
        foreach (var modifier in Utilities.ModifiersOf(node)!)
        {
            if (NodeTests.IsDecorator(modifier))
            {
                if (!Utilities.NodeCanBeDecorated(_legacyDecorators, node, node.Parent!, node.Parent!.Parent!))
                {
                    if (node.Kind == SyntaxKind.MethodDeclaration && !Utilities.NodeIsPresent(((MethodDeclaration)node).Body))
                    {
                        return GrammarErrorOnFirstToken(node, Diagnostics.A_decorator_can_only_decorate_a_method_implementation_not_an_overload);
                    }
                    else
                    {
                        return GrammarErrorOnFirstToken(node, Diagnostics.Decorators_are_not_valid_here);
                    }
                }
                else if (_legacyDecorators && (node.Kind == SyntaxKind.GetAccessor || node.Kind == SyntaxKind.SetAccessor))
                {
                    var accessors = GetAllAccessorDeclarationsForDeclaration(node);
                    if (Utilities.HasDecorators(accessors.FirstAccessor!) && ReferenceEquals(node, accessors.SecondAccessor))
                    {
                        return GrammarErrorOnFirstToken(node, Diagnostics.Decorators_cannot_be_applied_to_multiple_get_Slashset_accessors_of_the_same_name);
                    }
                }

                // if we've seen any modifiers aside from `export`, `default`, or another decorator, then this is an invalid position
                if ((flags & ~(ModifierFlags.ExportDefault | ModifierFlags.Decorator)) != ModifierFlags.None)
                {
                    return GrammarErrorOnNode(modifier, Diagnostics.Decorators_are_not_valid_here);
                }

                // if we've already seen leading decorators and leading modifiers, then trailing decorators are an invalid position
                if (hasLeadingDecorators && (flags & ModifierFlags.Modifier) != ModifierFlags.None)
                {
                    Debug.AssertIsDefined(firstDecorator);
                    var sourceFile = Utilities.GetSourceFileOfNode(modifier)!;
                    if (!HasParseDiagnostics(sourceFile))
                    {
                        Utilities.AddRelatedInfo(
                            Error(modifier, Diagnostics.Decorators_may_not_appear_after_export_or_export_default_if_they_also_appear_before_export),
                            Utilities.CreateDiagnosticForNode(firstDecorator!, Diagnostics.Decorator_used_before_export_here));
                        return true;
                    }
                    return false;
                }

                flags |= ModifierFlags.Decorator;

                // if we have not yet seen a modifier, then these are leading decorators
                if ((flags & ModifierFlags.Modifier) == ModifierFlags.None)
                {
                    hasLeadingDecorators = true;
                }
                else if ((flags & ModifierFlags.Export) != ModifierFlags.None)
                {
                    sawExportBeforeDecorators = true;
                }

                firstDecorator ??= modifier;
            }
            else
            {
                if (modifier.Kind != SyntaxKind.ReadonlyKeyword)
                {
                    if (node.Kind == SyntaxKind.PropertySignature || node.Kind == SyntaxKind.MethodSignature)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_a_type_member, ScannerUtilities.TokenToString(modifier.Kind));
                    }
                    if (node.Kind == SyntaxKind.IndexSignature && (modifier.Kind != SyntaxKind.StaticKeyword || !Utilities.IsClassLike(node.Parent!)))
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_an_index_signature, ScannerUtilities.TokenToString(modifier.Kind));
                    }
                }
                if (modifier.Kind != SyntaxKind.InKeyword && modifier.Kind != SyntaxKind.OutKeyword && modifier.Kind != SyntaxKind.ConstKeyword)
                {
                    if (node.Kind == SyntaxKind.TypeParameter)
                    {
                        return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_a_type_parameter, ScannerUtilities.TokenToString(modifier.Kind));
                    }
                }
                switch (modifier.Kind)
                {
                    case SyntaxKind.ConstKeyword:
                    {
                        if (node.Kind != SyntaxKind.EnumDeclaration && node.Kind != SyntaxKind.TypeParameter)
                        {
                            return GrammarErrorOnNode(node, Diagnostics.A_class_member_cannot_have_the_0_keyword, ScannerUtilities.TokenToString(SyntaxKind.ConstKeyword));
                        }
                        var jsdocHost = NodeTests.IsJSDocTemplateTag(node.Parent!) ? Utilities.GetEffectiveJSDocHost(node.Parent!) : null;
                        var parent = jsdocHost ?? node.Parent!;
                        if (
                            node.Kind == SyntaxKind.TypeParameter && !(Utilities.IsFunctionLikeDeclaration(parent) || Utilities.IsClassLike(parent) || NodeTests.IsFunctionTypeNode(parent) ||
                                NodeTests.IsConstructorTypeNode(parent) || NodeTests.IsCallSignatureDeclaration(parent) || NodeTests.IsConstructSignatureDeclaration(parent) || NodeTests.IsMethodSignature(parent))
                        )
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_can_only_appear_on_a_type_parameter_of_a_function_method_or_class, ScannerUtilities.TokenToString(modifier.Kind));
                        }
                        break;
                    }
                    case SyntaxKind.OverrideKeyword:
                        // If node.kind === SyntaxKind.Parameter, checkParameter reports an error if it's not a parameter property.
                        if ((flags & ModifierFlags.Override) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_already_seen, "override");
                        }
                        else if ((flags & ModifierFlags.Ambient) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_be_used_with_1_modifier, "override", "declare");
                        }
                        else if ((flags & ModifierFlags.Readonly) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, "override", "readonly");
                        }
                        else if ((flags & ModifierFlags.Accessor) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, "override", "accessor");
                        }
                        else if ((flags & ModifierFlags.Async) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, "override", "async");
                        }
                        flags |= ModifierFlags.Override;
                        lastOverride = modifier;
                        break;

                    case SyntaxKind.PublicKeyword:
                    case SyntaxKind.ProtectedKeyword:
                    case SyntaxKind.PrivateKeyword:
                    {
                        var text = VisibilityToString(Utilities.ModifierToFlag(modifier.Kind));

                        if ((flags & ModifierFlags.AccessibilityModifier) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics.Accessibility_modifier_already_seen);
                        }
                        else if ((flags & ModifierFlags.Override) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, text, "override");
                        }
                        else if ((flags & ModifierFlags.Static) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, text, "static");
                        }
                        else if ((flags & ModifierFlags.Accessor) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, text, "accessor");
                        }
                        else if ((flags & ModifierFlags.Readonly) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, text, "readonly");
                        }
                        else if ((flags & ModifierFlags.Async) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, text, "async");
                        }
                        else if (node.Parent!.Kind == SyntaxKind.ModuleBlock || node.Parent!.Kind == SyntaxKind.SourceFile)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_a_module_or_namespace_element, text);
                        }
                        else if ((flags & ModifierFlags.Abstract) != ModifierFlags.None)
                        {
                            if (modifier.Kind == SyntaxKind.PrivateKeyword)
                            {
                                return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_be_used_with_1_modifier, text, "abstract");
                            }
                            else
                            {
                                return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, text, "abstract");
                            }
                        }
                        else if (Utilities.IsPrivateIdentifierClassElementDeclaration(node))
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics.An_accessibility_modifier_cannot_be_used_with_a_private_identifier);
                        }
                        flags |= Utilities.ModifierToFlag(modifier.Kind);
                        break;
                    }
                    case SyntaxKind.StaticKeyword:
                        if ((flags & ModifierFlags.Static) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_already_seen, "static");
                        }
                        else if ((flags & ModifierFlags.Readonly) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, "static", "readonly");
                        }
                        else if ((flags & ModifierFlags.Async) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, "static", "async");
                        }
                        else if ((flags & ModifierFlags.Accessor) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, "static", "accessor");
                        }
                        else if (node.Parent!.Kind == SyntaxKind.ModuleBlock || node.Parent!.Kind == SyntaxKind.SourceFile)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_a_module_or_namespace_element, "static");
                        }
                        else if (node.Kind == SyntaxKind.Parameter)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_a_parameter, "static");
                        }
                        else if ((flags & ModifierFlags.Abstract) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_be_used_with_1_modifier, "static", "abstract");
                        }
                        else if ((flags & ModifierFlags.Override) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, "static", "override");
                        }
                        flags |= ModifierFlags.Static;
                        lastStatic = modifier;
                        break;

                    case SyntaxKind.AccessorKeyword:
                        if ((flags & ModifierFlags.Accessor) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_already_seen, "accessor");
                        }
                        else if ((flags & ModifierFlags.Readonly) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_be_used_with_1_modifier, "accessor", "readonly");
                        }
                        else if ((flags & ModifierFlags.Ambient) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_be_used_with_1_modifier, "accessor", "declare");
                        }
                        else if (node.Kind != SyntaxKind.PropertyDeclaration)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics.accessor_modifier_can_only_appear_on_a_property_declaration);
                        }

                        flags |= ModifierFlags.Accessor;
                        break;

                    case SyntaxKind.ReadonlyKeyword:
                        if ((flags & ModifierFlags.Readonly) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_already_seen, "readonly");
                        }
                        else if (node.Kind != SyntaxKind.PropertyDeclaration && node.Kind != SyntaxKind.PropertySignature && node.Kind != SyntaxKind.IndexSignature && node.Kind != SyntaxKind.Parameter)
                        {
                            // If node.kind === SyntaxKind.Parameter, checkParameter reports an error if it's not a parameter property.
                            return GrammarErrorOnNode(modifier, Diagnostics.readonly_modifier_can_only_appear_on_a_property_declaration_or_index_signature);
                        }
                        else if ((flags & ModifierFlags.Accessor) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_be_used_with_1_modifier, "readonly", "accessor");
                        }
                        flags |= ModifierFlags.Readonly;
                        break;

                    case SyntaxKind.ExportKeyword:
                        if (
                            Js.IsTruthy(_compilerOptions.VerbatimModuleSyntax) &&
                            (node.Flags & NodeFlags.Ambient) == 0 &&
                            node.Kind != SyntaxKind.TypeAliasDeclaration &&
                            node.Kind != SyntaxKind.InterfaceDeclaration &&
                            // ModuleDeclaration needs to be checked that it is uninstantiated later
                            node.Kind != SyntaxKind.ModuleDeclaration &&
                            node.Parent!.Kind == SyntaxKind.SourceFile &&
                            _host.GetEmitModuleFormatOfFile(Utilities.GetSourceFileOfNode(node)) == ModuleKind.CommonJS
                        ) {
                            return GrammarErrorOnNode(modifier, Diagnostics.A_top_level_export_modifier_cannot_be_used_on_value_declarations_in_a_CommonJS_module_when_verbatimModuleSyntax_is_enabled);
                        }
                        if ((flags & ModifierFlags.Export) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_already_seen, "export");
                        }
                        else if ((flags & ModifierFlags.Ambient) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, "export", "declare");
                        }
                        else if ((flags & ModifierFlags.Abstract) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, "export", "abstract");
                        }
                        else if ((flags & ModifierFlags.Async) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, "export", "async");
                        }
                        else if (Utilities.IsClassLike(node.Parent!))
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_class_elements_of_this_kind, "export");
                        }
                        else if (node.Kind == SyntaxKind.Parameter)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_a_parameter, "export");
                        }
                        else if (blockScopeKind == NodeFlags.Using)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_a_using_declaration, "export");
                        }
                        else if (blockScopeKind == NodeFlags.AwaitUsing)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_an_await_using_declaration, "export");
                        }
                        flags |= ModifierFlags.Export;
                        break;
                    case SyntaxKind.DefaultKeyword:
                    {
                        var container = node.Parent!.Kind == SyntaxKind.SourceFile ? node.Parent! : node.Parent!.Parent!;
                        if (container.Kind == SyntaxKind.ModuleDeclaration && !Utilities.IsAmbientModule(container))
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics.A_default_export_can_only_be_used_in_an_ECMAScript_style_module);
                        }
                        else if (blockScopeKind == NodeFlags.Using)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_a_using_declaration, "default");
                        }
                        else if (blockScopeKind == NodeFlags.AwaitUsing)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_an_await_using_declaration, "default");
                        }
                        else if ((flags & ModifierFlags.Export) == ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, "export", "default");
                        }
                        else if (sawExportBeforeDecorators)
                        {
                            return GrammarErrorOnNode(firstDecorator!, Diagnostics.Decorators_are_not_valid_here);
                        }

                        flags |= ModifierFlags.Default;
                        break;
                    }
                    case SyntaxKind.DeclareKeyword:
                        if ((flags & ModifierFlags.Ambient) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_already_seen, "declare");
                        }
                        else if ((flags & ModifierFlags.Async) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_be_used_in_an_ambient_context, "async");
                        }
                        else if ((flags & ModifierFlags.Override) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_be_used_in_an_ambient_context, "override");
                        }
                        else if (Utilities.IsClassLike(node.Parent!) && !NodeTests.IsPropertyDeclaration(node))
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_class_elements_of_this_kind, "declare");
                        }
                        else if (node.Kind == SyntaxKind.Parameter)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_a_parameter, "declare");
                        }
                        else if (blockScopeKind == NodeFlags.Using)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_a_using_declaration, "declare");
                        }
                        else if (blockScopeKind == NodeFlags.AwaitUsing)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_an_await_using_declaration, "declare");
                        }
                        else if ((node.Parent!.Flags & NodeFlags.Ambient) != 0 && node.Parent!.Kind == SyntaxKind.ModuleBlock)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics.A_declare_modifier_cannot_be_used_in_an_already_ambient_context);
                        }
                        else if (Utilities.IsPrivateIdentifierClassElementDeclaration(node))
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_be_used_with_a_private_identifier, "declare");
                        }
                        else if ((flags & ModifierFlags.Accessor) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_be_used_with_1_modifier, "declare", "accessor");
                        }
                        flags |= ModifierFlags.Ambient;
                        lastDeclare = modifier;
                        break;

                    case SyntaxKind.AbstractKeyword:
                        if ((flags & ModifierFlags.Abstract) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_already_seen, "abstract");
                        }
                        if (
                            node.Kind != SyntaxKind.ClassDeclaration &&
                            node.Kind != SyntaxKind.ConstructorType
                        ) {
                            if (
                                node.Kind != SyntaxKind.MethodDeclaration &&
                                node.Kind != SyntaxKind.PropertyDeclaration &&
                                node.Kind != SyntaxKind.GetAccessor &&
                                node.Kind != SyntaxKind.SetAccessor
                            ) {
                                return GrammarErrorOnNode(modifier, Diagnostics.abstract_modifier_can_only_appear_on_a_class_method_or_property_declaration);
                            }
                            if (!(node.Parent!.Kind == SyntaxKind.ClassDeclaration && Utilities.HasSyntacticModifier(node.Parent!, ModifierFlags.Abstract)))
                            {
                                var message = node.Kind == SyntaxKind.PropertyDeclaration
                                    ? Diagnostics.Abstract_properties_can_only_appear_within_an_abstract_class
                                    : Diagnostics.Abstract_methods_can_only_appear_within_an_abstract_class;
                                return GrammarErrorOnNode(modifier, message);
                            }
                            if ((flags & ModifierFlags.Static) != ModifierFlags.None)
                            {
                                return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_be_used_with_1_modifier, "static", "abstract");
                            }
                            if ((flags & ModifierFlags.Private) != ModifierFlags.None)
                            {
                                return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_be_used_with_1_modifier, "private", "abstract");
                            }
                            if ((flags & ModifierFlags.Async) != ModifierFlags.None && lastAsync is not null)
                            {
                                return GrammarErrorOnNode(lastAsync, Diagnostics._0_modifier_cannot_be_used_with_1_modifier, "async", "abstract");
                            }
                            if ((flags & ModifierFlags.Override) != ModifierFlags.None)
                            {
                                return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, "abstract", "override");
                            }
                            if ((flags & ModifierFlags.Accessor) != ModifierFlags.None)
                            {
                                return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, "abstract", "accessor");
                            }
                        }
                        if (Utilities.IsNamedDeclaration(node) && ((INamedDeclaration)node).Name!.Kind == SyntaxKind.PrivateIdentifier)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_be_used_with_a_private_identifier, "abstract");
                        }

                        flags |= ModifierFlags.Abstract;
                        break;

                    case SyntaxKind.AsyncKeyword:
                        if ((flags & ModifierFlags.Async) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_already_seen, "async");
                        }
                        else if ((flags & ModifierFlags.Ambient) != ModifierFlags.None || (node.Parent!.Flags & NodeFlags.Ambient) != 0)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_be_used_in_an_ambient_context, "async");
                        }
                        else if (node.Kind == SyntaxKind.Parameter)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_appear_on_a_parameter, "async");
                        }
                        if ((flags & ModifierFlags.Abstract) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_cannot_be_used_with_1_modifier, "async", "abstract");
                        }
                        flags |= ModifierFlags.Async;
                        lastAsync = modifier;
                        break;

                    case SyntaxKind.InKeyword:
                    case SyntaxKind.OutKeyword:
                    {
                        var inOutFlag = modifier.Kind == SyntaxKind.InKeyword ? ModifierFlags.In : ModifierFlags.Out;
                        var inOutText = modifier.Kind == SyntaxKind.InKeyword ? "in" : "out";
                        Node? jsdocTypedefHost = null;
                        if (NodeTests.IsJSDocTemplateTag(node.Parent!))
                        {
                            var jsdocRoot = Utilities.GetJSDocRoot(node.Parent!);
                            jsdocTypedefHost = Utilities.GetEffectiveJSDocHost(node.Parent!)
                                ?? Core.Find(jsdocRoot is JSDoc root ? root.Tags : null, (JSDocTag tag, int _) => NodeTests.IsJSDocTypedefTag(tag));
                        }
                        var parent = jsdocTypedefHost ?? node.Parent!;
                        if (node.Kind != SyntaxKind.TypeParameter || !(NodeTests.IsInterfaceDeclaration(parent) || Utilities.IsClassLike(parent) || NodeTests.IsTypeAliasDeclaration(parent) || NodeTests.IsJSDocTypedefTag(parent)))
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_can_only_appear_on_a_type_parameter_of_a_class_interface_or_type_alias, inOutText);
                        }
                        if ((flags & inOutFlag) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_already_seen, inOutText);
                        }
                        if ((inOutFlag & ModifierFlags.In) != ModifierFlags.None && (flags & ModifierFlags.Out) != ModifierFlags.None)
                        {
                            return GrammarErrorOnNode(modifier, Diagnostics._0_modifier_must_precede_1_modifier, "in", "out");
                        }
                        flags |= inOutFlag;
                        break;
                    }
                }
            }
        }

        if (node.Kind == SyntaxKind.Constructor)
        {
            if ((flags & ModifierFlags.Static) != ModifierFlags.None)
            {
                return GrammarErrorOnNode(lastStatic!, Diagnostics._0_modifier_cannot_appear_on_a_constructor_declaration, "static");
            }
            if ((flags & ModifierFlags.Override) != ModifierFlags.None)
            {
                return GrammarErrorOnNode(lastOverride!, Diagnostics._0_modifier_cannot_appear_on_a_constructor_declaration, "override"); // TODO: GH#18217
            }
            if ((flags & ModifierFlags.Async) != ModifierFlags.None)
            {
                return GrammarErrorOnNode(lastAsync!, Diagnostics._0_modifier_cannot_appear_on_a_constructor_declaration, "async");
            }
            return false;
        }
        else if ((node.Kind == SyntaxKind.ImportDeclaration || node.Kind == SyntaxKind.ImportEqualsDeclaration) && (flags & ModifierFlags.Ambient) != ModifierFlags.None)
        {
            return GrammarErrorOnNode(lastDeclare!, Diagnostics.A_0_modifier_cannot_be_used_with_an_import_declaration, "declare");
        }
        else if (node.Kind == SyntaxKind.Parameter && (flags & ModifierFlags.ParameterPropertyModifier) != ModifierFlags.None && Utilities.IsBindingPattern(((ParameterDeclaration)node).Name!))
        {
            return GrammarErrorOnNode(node, Diagnostics.A_parameter_property_may_not_be_declared_using_a_binding_pattern);
        }
        else if (node.Kind == SyntaxKind.Parameter && (flags & ModifierFlags.ParameterPropertyModifier) != ModifierFlags.None && ((ParameterDeclaration)node).DotDotDotToken is not null)
        {
            return GrammarErrorOnNode(node, Diagnostics.A_parameter_property_cannot_be_declared_using_a_rest_parameter);
        }
        if ((flags & ModifierFlags.Async) != ModifierFlags.None)
        {
            return CheckGrammarAsyncModifier(node, lastAsync!);
        }
        return false;
    }

    /// <summary>
    /// true | false: Early return this value from checkGrammarModifiers.
    /// undefined: Need to do full checking on the modifiers.
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:52219-52224 — <c>reportObviousModifierErrors</c>.</remarks>
    internal bool? ReportObviousModifierErrors(Node node)
    {
        if (Utilities.ModifiersOf(node) is null) return false;

        var modifier = FindFirstIllegalModifier(node);
        return modifier is not null ? GrammarErrorOnFirstToken(modifier, Diagnostics.Modifiers_cannot_appear_here) : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:52226-52229 — <c>findFirstModifierExcept</c>.</summary>
    internal static Node? FindFirstModifierExcept(Node node, SyntaxKind allowedModifier)
    {
        var modifier = FindModifier(Utilities.ModifiersOf(node), m => Utilities.IsModifierKind(m.Kind));
        return modifier is not null && modifier.Kind != allowedModifier ? modifier : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:52231-52281 — <c>findFirstIllegalModifier</c>.</summary>
    internal static Node? FindFirstIllegalModifier(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.Constructor:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.PropertySignature:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.MethodSignature:
            case SyntaxKind.IndexSignature:
            case SyntaxKind.ModuleDeclaration:
            case SyntaxKind.ImportDeclaration:
            case SyntaxKind.ImportEqualsDeclaration:
            case SyntaxKind.ExportDeclaration:
            case SyntaxKind.ExportAssignment:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.Parameter:
            case SyntaxKind.TypeParameter:
                return null;
            case SyntaxKind.ClassStaticBlockDeclaration:
            case SyntaxKind.PropertyAssignment:
            case SyntaxKind.ShorthandPropertyAssignment:
            case SyntaxKind.NamespaceExportDeclaration:
            case SyntaxKind.MissingDeclaration:
                return FindModifier(Utilities.ModifiersOf(node), m => Utilities.IsModifierKind(m.Kind));
            default:
                if (node.Parent!.Kind == SyntaxKind.ModuleBlock || node.Parent!.Kind == SyntaxKind.SourceFile)
                {
                    return null;
                }
                switch (node.Kind)
                {
                    case SyntaxKind.FunctionDeclaration:
                        return FindFirstModifierExcept(node, SyntaxKind.AsyncKeyword);
                    case SyntaxKind.ClassDeclaration:
                    case SyntaxKind.ConstructorType:
                        return FindFirstModifierExcept(node, SyntaxKind.AbstractKeyword);
                    case SyntaxKind.ClassExpression:
                    case SyntaxKind.InterfaceDeclaration:
                    case SyntaxKind.TypeAliasDeclaration:
                        return FindModifier(Utilities.ModifiersOf(node), m => Utilities.IsModifierKind(m.Kind));
                    case SyntaxKind.VariableStatement:
                        return (((VariableStatement)node).DeclarationList!.Flags & NodeFlags.Using) != 0
                            ? FindFirstModifierExcept(node, SyntaxKind.AwaitKeyword)
                            : FindModifier(Utilities.ModifiersOf(node), m => Utilities.IsModifierKind(m.Kind));
                    case SyntaxKind.EnumDeclaration:
                        return FindFirstModifierExcept(node, SyntaxKind.ConstKeyword);
                    default:
                        Debug.AssertNever(node);
                        return null;
                }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:52283-52286 — <c>reportObviousDecoratorErrors</c>.</summary>
    internal bool? ReportObviousDecoratorErrors(Node node)
    {
        var decorator = FindFirstIllegalDecorator(node);
        return decorator is not null ? GrammarErrorOnFirstToken(decorator, Diagnostics.Decorators_are_not_valid_here) : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:52288-52290 — <c>findFirstIllegalDecorator</c>.</summary>
    internal static Node? FindFirstIllegalDecorator(Node node)
        => FactoryUtilities.CanHaveIllegalDecorators(node) ? FindModifier(Utilities.ModifiersOf(node), NodeTests.IsDecorator) : null;

    /// <summary>ported from: src/compiler/core.ts:166-175 — <c>find</c>, over the modifier arrays this block reads.</summary>
    /// <remarks>PORT-NOTE: <c>Core.Find</c> takes an <c>IList&lt;T&gt;</c> and the shared <c>Utilities.ModifiersOf</c>
    /// answers the <c>IEnumerable&lt;Node&gt;</c> both modifier element types can share, so the search is spelled here —
    /// the same narrowing <c>Program/Program.DiagnosticsAggregation.cs</c> declares for its own reads. The reference's
    /// <c>startIndex</c> is not part of the signature because no caller in this block advances it.</remarks>
    internal static Node? FindModifier(IEnumerable<Node>? modifiers, Func<Node, bool> predicate)
    {
        if (modifiers is null)
        {
            return null;
        }

        foreach (var value in modifiers)
        {
            if (predicate(value))
            {
                return value;
            }
        }

        return null;
    }

    // ---------------------------------------------------------------------------------------------
    // The list-shape checks (checker.ts:52292-52380)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:52292-52302 — <c>checkGrammarAsyncModifier</c>.</summary>
    internal bool CheckGrammarAsyncModifier(Node node, Node asyncModifier)
    {
        switch (node.Kind)
        {
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ArrowFunction:
                return false;
        }

        return GrammarErrorOnNode(asyncModifier, Diagnostics._0_modifier_cannot_be_used_here, "async");
    }

    /// <summary>ported from: src/compiler/checker.ts:52304-52309 — <c>checkGrammarForDisallowedTrailingComma</c>.</summary>
    /// <remarks>PORT-NOTE: TS's default argument is the diagnostic <c>Trailing_comma_not_allowed</c>, which is not a
    /// C# constant; the port defaults to null and substitutes the same diagnostic, so the arity is unchanged. The
    /// parameter is the covariant <see cref="INodeArray{T}"/> view of the caller's own array type, because those types
    /// are interface-typed in places (<c>NodeArray&lt;ITypeNode&gt;</c>) where the covariant-to-<c>Node</c> view does
    /// not exist, and only the array's text range, <c>hasTrailingComma</c> and first element are read. That first
    /// element is the diagnostic's anchor and is reached through <c>object</c>, because C# has no conversion from an
    /// unconstrained-in-<c>Node</c> type parameter to <c>Node</c>; every caller's array holds nodes.</remarks>
    internal bool CheckGrammarForDisallowedTrailingComma<T>(INodeArray<T>? list, DiagnosticMessage? diag = null)
        where T : INode
    {
        if (list is not null && list.HasTrailingComma)
        {
            return GrammarErrorAtPos((Node)(object)list[0], list.End - 1, 1, diag ?? Diagnostics.Trailing_comma_not_allowed);
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:52311-52318 — <c>checkGrammarTypeParameterList</c>.</summary>
    internal bool CheckGrammarTypeParameterList(NodeArray<TypeParameterDeclaration>? typeParameters, SourceFile file)
    {
        if (typeParameters is not null && typeParameters.Count == 0)
        {
            var start = typeParameters.Pos - 1;
            var end = ScannerUtilities.SkipTrivia(file.Text ?? "", typeParameters.End) + 1;
            return GrammarErrorAtPos(file, start, end - start, Diagnostics.Type_parameter_list_cannot_be_empty);
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:52320-52352 — <c>checkGrammarParameterList</c>.</summary>
    internal bool CheckGrammarParameterList(NodeArray<ParameterDeclaration> parameters)
    {
        var seenOptionalParameter = false;
        var parameterCount = parameters.Count;

        for (var i = 0; i < parameterCount; i++)
        {
            var parameter = parameters[i];
            if (parameter.DotDotDotToken is not null)
            {
                if (i != parameterCount - 1)
                {
                    return GrammarErrorOnNode(parameter.DotDotDotToken, Diagnostics.A_rest_parameter_must_be_last_in_a_parameter_list);
                }
                if ((parameter.Flags & NodeFlags.Ambient) == 0) // Allow `...foo,` in ambient declarations; see GH#23070
                {
                    CheckGrammarForDisallowedTrailingComma(parameters, Diagnostics.A_rest_parameter_or_binding_pattern_may_not_have_a_trailing_comma);
                }

                if (parameter.QuestionToken is not null)
                {
                    return GrammarErrorOnNode(parameter.QuestionToken, Diagnostics.A_rest_parameter_cannot_be_optional);
                }

                if (parameter.Initializer is not null)
                {
                    return GrammarErrorOnNode(parameter.Name!, Diagnostics.A_rest_parameter_cannot_have_an_initializer);
                }
            }
            else if (HasEffectiveQuestionToken(parameter))
            {
                seenOptionalParameter = true;
                if (parameter.QuestionToken is not null && parameter.Initializer is not null)
                {
                    return GrammarErrorOnNode(parameter.Name!, Diagnostics.Parameter_cannot_have_question_mark_and_initializer);
                }
            }
            else if (seenOptionalParameter && parameter.Initializer is null)
            {
                return GrammarErrorOnNode(parameter.Name!, Diagnostics.A_required_parameter_cannot_follow_an_optional_parameter);
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:52354-52356 — <c>getNonSimpleParameters</c>.</summary>
    internal static IList<ParameterDeclaration> GetNonSimpleParameters(IList<ParameterDeclaration> parameters)
        => Core.Filter(parameters, parameter => parameter.Initializer is not null || Utilities.IsBindingPattern(parameter.Name!) || Utilities.IsRestParameter(parameter))!;

    /// <summary>ported from: src/compiler/checker.ts:52358-52380 — <c>checkGrammarForUseStrictSimpleParameterList</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS spreads the <c>nonSimpleParameters.map(...)</c> array into <c>addRelatedInfo</c>'s rest parameter;
    /// the port fills the <c>IDiagnosticRelatedInformation[]</c> the same call wants, because a <c>List&lt;Diagnostic&gt;</c>
    /// is not that array. The two <c>Node</c>-typed arguments are the doc's <c>IStatement</c> narrowed to the class the
    /// constructors of those diagnostics take.
    /// </remarks>
    internal bool CheckGrammarForUseStrictSimpleParameterList(Node node)
    {
        if (_languageVersion >= ScriptTarget.ES2016)
        {
            var body = ((IFunctionLikeDeclarationBase)node).Body;
            var useStrictDirective = body is not null && NodeTests.IsBlock(body) ? FactoryUtilities.FindUseStrictPrologue(((Block)body).Statements!) : null;
            if (useStrictDirective is not null)
            {
                var nonSimpleParameters = GetNonSimpleParameters(((ISignatureDeclarationBase)node).Parameters!);
                if (Core.Length(nonSimpleParameters) > 0)
                {
                    Core.ForEach(nonSimpleParameters, (ParameterDeclaration parameter, int _) =>
                    {
                        Utilities.AddRelatedInfo(
                            Error(parameter, Diagnostics.This_parameter_is_not_allowed_with_use_strict_directive),
                            Utilities.CreateDiagnosticForNode((Node)useStrictDirective, Diagnostics.use_strict_directive_used_here));
                        return (object?)null;
                    });

                    var diagnostics = new IDiagnosticRelatedInformation[nonSimpleParameters.Count];
                    for (var i = 0; i < nonSimpleParameters.Count; i++)
                    {
                        diagnostics[i] = i == 0
                            ? Utilities.CreateDiagnosticForNode(nonSimpleParameters[i], Diagnostics.Non_simple_parameter_declared_here)
                            : Utilities.CreateDiagnosticForNode(nonSimpleParameters[i], Diagnostics.and_here);
                    }
                    Utilities.AddRelatedInfo(Error((Node)useStrictDirective, Diagnostics.use_strict_directive_cannot_be_used_with_non_simple_parameter_list), diagnostics);
                    return true;
                }
            }
        }
        return false;
    }

    // ---------------------------------------------------------------------------------------------
    // The declaration-shaped checks (checker.ts:52382-52534)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:52382-52390 — <c>checkGrammarFunctionLikeDeclaration</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is <c>FunctionLikeDeclaration | MethodSignature</c>; the port takes
    /// <see cref="Node"/> and reads <c>typeParameters</c>/<c>parameters</c> through
    /// <see cref="ISignatureDeclarationBase"/>, which every one of those kinds implements.</remarks>
    internal bool CheckGrammarFunctionLikeDeclaration(Node node)
    {
        // Prevent cascading error by short-circuit
        var file = Utilities.GetSourceFileOfNode(node)!;
        return CheckGrammarModifiers(node)
            || CheckGrammarTypeParameterList(((ISignatureDeclarationBase)node).TypeParameters, file)
            || CheckGrammarParameterList(((ISignatureDeclarationBase)node).Parameters!)
            || CheckGrammarArrowFunction(node, file)
            || (Utilities.IsFunctionLikeDeclaration(node) && CheckGrammarForUseStrictSimpleParameterList(node));
    }

    /// <summary>ported from: src/compiler/checker.ts:52392-52396 — <c>checkGrammarClassLikeDeclaration</c>.</summary>
    internal bool CheckGrammarClassLikeDeclaration(Node node)
    {
        var file = Utilities.GetSourceFileOfNode(node)!;
        return CheckGrammarClassDeclarationHeritageClauses(node)
            || CheckGrammarTypeParameterList(((IClassLikeDeclarationBase)node).TypeParameters, file);
    }

    /// <summary>ported from: src/compiler/checker.ts:52398-52413 — <c>checkGrammarArrowFunction</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>getLineAndCharacterOfPosition</c> (scanner.ts:547) is the memoized
    /// <c>file.lineMap ??= getLineStarts(file)</c> plus <c>computeLineAndCharacterOfPosition</c>. The port's
    /// <c>ScannerUtilities.GetLineAndCharacterOfPosition</c> takes an <c>ISourceFileLike</c>, which <c>SourceFile</c>
    /// does not implement, so the two steps are inlined here with the same memo — the shape
    /// <c>Program/Program.DiagnosticsAggregation.cs</c> and <c>Core/Tracing.cs</c> already use for that call.
    /// </remarks>
    internal bool CheckGrammarArrowFunction(Node node, SourceFile file)
    {
        if (!NodeTests.IsArrowFunction(node))
        {
            return false;
        }

        var arrow = (ArrowFunction)node;
        if (arrow.TypeParameters is not null && !(Core.Length(arrow.TypeParameters) > 1 || arrow.TypeParameters.HasTrailingComma || arrow.TypeParameters[0].Constraint is not null))
        {
            if (Paths.FileExtensionIsOneOf(file.FileName!, [Extension.Mts.Value, Extension.Cts.Value]))
            {
                GrammarErrorOnNode(arrow.TypeParameters[0], Diagnostics.This_syntax_is_reserved_in_files_with_the_mts_or_cts_extension_Add_a_trailing_comma_or_explicit_constraint);
            }
        }

        var equalsGreaterThanToken = arrow.EqualsGreaterThanToken!;
        var lineStarts = file.LineMap ?? (file.LineMap = ScannerUtilities.ComputeLineStarts(file.Text ?? ""));
        var startLine = ScannerUtilities.ComputeLineAndCharacterOfPosition(lineStarts, equalsGreaterThanToken.Pos).Line;
        var endLine = ScannerUtilities.ComputeLineAndCharacterOfPosition(lineStarts, equalsGreaterThanToken.End).Line;
        return startLine != endLine && GrammarErrorOnNode(equalsGreaterThanToken, Diagnostics.Line_terminator_not_permitted_before_arrow);
    }


    // ---------------------------------------------------------------------------------------------
    // The list/tuple/type-argument checks (checker.ts:52415-52498)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:52415-52452 — <c>checkGrammarIndexSignatureParameters</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is <c>SignatureDeclaration</c>, which the model has no type for, so the port
    /// takes <see cref="Node"/> and reads <c>parameters</c>/<c>type</c> through
    /// <see cref="ISignatureDeclarationBase"/>.</remarks>
    internal bool CheckGrammarIndexSignatureParameters(Node node)
    {
        var parameters = ((ISignatureDeclarationBase)node).Parameters!;
        var parameter = parameters.Count > 0 ? parameters[0] : null;
        if (parameters.Count != 1)
        {
            if (parameter is not null)
            {
                return GrammarErrorOnNode(parameter.Name!, Diagnostics.An_index_signature_must_have_exactly_one_parameter);
            }
            else
            {
                return GrammarErrorOnNode(node, Diagnostics.An_index_signature_must_have_exactly_one_parameter);
            }
        }
        CheckGrammarForDisallowedTrailingComma(parameters, Diagnostics.An_index_signature_cannot_have_a_trailing_comma);
        if (parameter!.DotDotDotToken is not null)
        {
            return GrammarErrorOnNode(parameter.DotDotDotToken, Diagnostics.An_index_signature_cannot_have_a_rest_parameter);
        }
        if (Utilities.HasEffectiveModifiers(parameter))
        {
            return GrammarErrorOnNode(parameter.Name!, Diagnostics.An_index_signature_parameter_cannot_have_an_accessibility_modifier);
        }
        if (parameter.QuestionToken is not null)
        {
            return GrammarErrorOnNode(parameter.QuestionToken, Diagnostics.An_index_signature_parameter_cannot_have_a_question_mark);
        }
        if (parameter.Initializer is not null)
        {
            return GrammarErrorOnNode(parameter.Name!, Diagnostics.An_index_signature_parameter_cannot_have_an_initializer);
        }
        if (parameter.Type is null)
        {
            return GrammarErrorOnNode(parameter.Name!, Diagnostics.An_index_signature_parameter_must_have_a_type_annotation);
        }
        var type = GetTypeFromTypeNode(parameter.Type);
        if (SomeType(type, t => (t.Flags & TypeFlags.StringOrNumberLiteralOrUnique) != 0) || IsGenericType(type))
        {
            return GrammarErrorOnNode(parameter.Name!, Diagnostics.An_index_signature_parameter_type_cannot_be_a_literal_type_or_generic_type_Consider_using_a_mapped_object_type_instead);
        }
        if (!EveryType(type, t => IsValidIndexKeyType(t)))
        {
            return GrammarErrorOnNode(parameter.Name!, Diagnostics.An_index_signature_parameter_type_must_be_string_number_symbol_or_a_template_literal_type);
        }
        if (((ISignatureDeclarationBase)node).Type is null)
        {
            return GrammarErrorOnNode(node, Diagnostics.An_index_signature_must_have_a_type_annotation);
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:52454-52457 — <c>checkGrammarIndexSignature</c>.</summary>
    internal bool CheckGrammarIndexSignature(Node node)
        // Prevent cascading error by short-circuit
        => CheckGrammarModifiers(node) || CheckGrammarIndexSignatureParameters(node);

    /// <summary>ported from: src/compiler/checker.ts:52459-52467 — <c>checkGrammarForAtLeastOneTypeArgument</c>.</summary>
    internal bool CheckGrammarForAtLeastOneTypeArgument(Node node, NodeArray<ITypeNode>? typeArguments)
    {
        if (typeArguments is not null && typeArguments.Count == 0)
        {
            var sourceFile = Utilities.GetSourceFileOfNode(node)!;
            var start = typeArguments.Pos - 1;
            var end = ScannerUtilities.SkipTrivia(sourceFile.Text ?? "", typeArguments.End) + 1;
            return GrammarErrorAtPos(sourceFile, start, end - start, Diagnostics.Type_argument_list_cannot_be_empty);
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:52469-52472 — <c>checkGrammarTypeArguments</c>.</summary>
    internal bool CheckGrammarTypeArguments(Node node, NodeArray<ITypeNode>? typeArguments)
        => CheckGrammarForDisallowedTrailingComma(typeArguments)
            || CheckGrammarForAtLeastOneTypeArgument(node, typeArguments);

    /// <summary>ported from: src/compiler/checker.ts:52474-52479 — <c>checkGrammarTaggedTemplateChain</c>.</summary>
    internal bool CheckGrammarTaggedTemplateChain(TaggedTemplateExpression node)
    {
        if (node.QuestionDotToken is not null || (node.Flags & NodeFlags.OptionalChain) != 0)
        {
            return GrammarErrorOnNode(node.Template!, Diagnostics.Tagged_template_expressions_are_not_permitted_in_an_optional_chain);
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:52481-52491 — <c>checkGrammarHeritageClause</c>.</summary>
    internal bool CheckGrammarHeritageClause(HeritageClause node)
    {
        var types = node.Types;
        if (CheckGrammarForDisallowedTrailingComma(types))
        {
            return true;
        }
        if (types is not null && types.Count == 0)
        {
            var listType = ScannerUtilities.TokenToString(node.Token);
            return GrammarErrorAtPos(node, types.Pos, 0, Diagnostics._0_list_cannot_be_empty, listType);
        }
        return Core.Some(types, CheckGrammarExpressionWithTypeArguments);
    }

    /// <summary>ported from: src/compiler/checker.ts:52493-52498 — <c>checkGrammarExpressionWithTypeArguments</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is <c>ExpressionWithTypeArguments | TypeQueryNode</c>; both classes implement
    /// <see cref="INodeWithTypeArguments"/>, which is where the port reads <c>typeArguments</c> from.</remarks>
    internal bool CheckGrammarExpressionWithTypeArguments(Node node)
    {
        if (NodeTests.IsExpressionWithTypeArguments(node) && NodeTests.IsImportKeyword(((ExpressionWithTypeArguments)node).Expression!) && ((INodeWithTypeArguments)node).TypeArguments is not null)
        {
            return GrammarErrorOnNode(node, Diagnostics.This_use_of_import_is_invalid_import_calls_can_be_written_but_they_must_have_parentheses_and_cannot_have_type_arguments);
        }
        return CheckGrammarTypeArguments(node, ((INodeWithTypeArguments)node).TypeArguments);
    }

    // ---------------------------------------------------------------------------------------------
    // The heritage/computed-name/generator checks (checker.ts:52500-52595)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:52500-52534 — <c>checkGrammarClassDeclarationHeritageClauses</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is <c>ClassLikeDeclaration</c>; the port takes <see cref="Node"/> and reads
    /// <c>heritageClauses</c> through <see cref="IClassLikeDeclarationBase"/>, which both class kinds implement.</remarks>
    internal bool CheckGrammarClassDeclarationHeritageClauses(Node node)
    {
        var seenExtendsClause = false;
        var seenImplementsClause = false;

        if (!CheckGrammarModifiers(node) && ((IClassLikeDeclarationBase)node).HeritageClauses is { } heritageClauses)
        {
            foreach (var heritageClause in heritageClauses)
            {
                if (heritageClause.Token == SyntaxKind.ExtendsKeyword)
                {
                    if (seenExtendsClause)
                    {
                        return GrammarErrorOnFirstToken(heritageClause, Diagnostics.extends_clause_already_seen);
                    }

                    if (seenImplementsClause)
                    {
                        return GrammarErrorOnFirstToken(heritageClause, Diagnostics.extends_clause_must_precede_implements_clause);
                    }

                    if (heritageClause.Types!.Count > 1)
                    {
                        return GrammarErrorOnFirstToken(heritageClause.Types[1], Diagnostics.Classes_can_only_extend_a_single_class);
                    }

                    seenExtendsClause = true;
                }
                else
                {
                    Debug.Assert(heritageClause.Token == SyntaxKind.ImplementsKeyword);
                    if (seenImplementsClause)
                    {
                        return GrammarErrorOnFirstToken(heritageClause, Diagnostics.implements_clause_already_seen);
                    }

                    seenImplementsClause = true;
                }

                // Grammar checking heritageClause inside class declaration
                CheckGrammarHeritageClause(heritageClause);
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:52536-52558 — <c>checkGrammarInterfaceDeclaration</c>.</summary>
    internal bool CheckGrammarInterfaceDeclaration(InterfaceDeclaration node)
    {
        var seenExtendsClause = false;

        if (node.HeritageClauses is { } heritageClauses)
        {
            foreach (var heritageClause in heritageClauses)
            {
                if (heritageClause.Token == SyntaxKind.ExtendsKeyword)
                {
                    if (seenExtendsClause)
                    {
                        return GrammarErrorOnFirstToken(heritageClause, Diagnostics.extends_clause_already_seen);
                    }

                    seenExtendsClause = true;
                }
                else
                {
                    Debug.Assert(heritageClause.Token == SyntaxKind.ImplementsKeyword);
                    return GrammarErrorOnFirstToken(heritageClause, Diagnostics.Interface_declaration_cannot_have_implements_clause);
                }

                // Grammar checking heritageClause inside class declaration
                CheckGrammarHeritageClause(heritageClause);
            }
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:52560-52571 — <c>checkGrammarComputedPropertyName</c>.</summary>
    internal bool CheckGrammarComputedPropertyName(Node node)
    {
        // If node is not a computedPropertyName, just skip the grammar checking
        if (node.Kind != SyntaxKind.ComputedPropertyName)
        {
            return false;
        }

        var computedPropertyName = (ComputedPropertyName)node;
        if (computedPropertyName.Expression!.Kind == SyntaxKind.BinaryExpression && ((BinaryExpression)computedPropertyName.Expression).OperatorToken!.Kind == SyntaxKind.CommaToken)
        {
            return GrammarErrorOnNode(computedPropertyName.Expression, Diagnostics.A_comma_expression_is_not_allowed_in_a_computed_property_name);
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:52573-52587 — <c>checkGrammarForGenerator</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is <c>FunctionLikeDeclaration</c>; the port takes <see cref="Node"/> and reads
    /// <c>asteriskToken</c>/<c>body</c> through <see cref="IFunctionLikeDeclarationBase"/>.</remarks>
    internal bool CheckGrammarForGenerator(Node node)
    {
        var asteriskToken = ((IFunctionLikeDeclarationBase)node).AsteriskToken;
        if (asteriskToken is not null)
        {
            Debug.Assert(
                node.Kind == SyntaxKind.FunctionDeclaration ||
                    node.Kind == SyntaxKind.FunctionExpression ||
                    node.Kind == SyntaxKind.MethodDeclaration);
            if ((node.Flags & NodeFlags.Ambient) != 0)
            {
                return GrammarErrorOnNode(asteriskToken, Diagnostics.Generators_are_not_allowed_in_an_ambient_context);
            }
            if (((IFunctionLikeDeclarationBase)node).Body is null)
            {
                return GrammarErrorOnNode(asteriskToken, Diagnostics.An_overload_signature_cannot_be_declared_as_a_generator);
            }
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:52589-52591 — <c>checkGrammarForInvalidQuestionMark</c>.</summary>
    internal bool CheckGrammarForInvalidQuestionMark(PunctuationToken? questionToken, DiagnosticMessage message)
        => questionToken is not null && GrammarErrorOnNode(questionToken, message);

    /// <summary>ported from: src/compiler/checker.ts:52593-52595 — <c>checkGrammarForInvalidExclamationToken</c>.</summary>
    internal bool CheckGrammarForInvalidExclamationToken(PunctuationToken? exclamationToken, DiagnosticMessage message)
        => exclamationToken is not null && GrammarErrorOnNode(exclamationToken, message);

    /// <summary>ported from: src/compiler/checker.ts:52597-52710 — <c>checkGrammarObjectLiteralExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>prop</c> is <c>ObjectLiteralElementLike</c>, whose <c>name</c>, <c>exclamationToken</c> and
    /// <c>questionToken</c> slots the model declares per class; the port reads <c>name</c> through
    /// <see cref="INamedDeclaration"/> (every non-spread member implements it) and the two tokens off the class the
    /// shared case label selects, because TS's fallthrough between <c>ShorthandPropertyAssignment</c> and
    /// <c>PropertyAssignment</c> is not expressible in C# without duplicating the arm.
    /// </remarks>
    internal bool CheckGrammarObjectLiteralExpression(ObjectLiteralExpression node, bool inDestructuring)
    {
        var seen = new Dictionary<string, DeclarationMeaning>();

        foreach (var prop in node.Properties!)
        {
            if (prop.Kind == SyntaxKind.SpreadAssignment)
            {
                if (inDestructuring)
                {
                    // a rest property cannot be destructured any further
                    var expression = Utilities.SkipParentheses(((SpreadAssignment)prop).Expression!);
                    if (NodeTests.IsArrayLiteralExpression(expression) || NodeTests.IsObjectLiteralExpression(expression))
                    {
                        return GrammarErrorOnNode(((SpreadAssignment)prop).Expression!, Diagnostics.A_rest_element_cannot_contain_a_binding_pattern);
                    }
                }
                continue;
            }
            var name = Utilities.IsNamedDeclaration(prop) ? ((INamedDeclaration)prop).Name : null;
            if (name is not null && name.Kind == SyntaxKind.ComputedPropertyName)
            {
                // If the name is not a ComputedPropertyName, the grammar checking will skip it
                CheckGrammarComputedPropertyName(name);
            }

            if (prop.Kind == SyntaxKind.ShorthandPropertyAssignment && !inDestructuring && ((ShorthandPropertyAssignment)prop).ObjectAssignmentInitializer is not null)
            {
                // having objectAssignmentInitializer is only valid in ObjectAssignmentPattern
                // outside of destructuring it is a syntax error
                GrammarErrorOnNode(((ShorthandPropertyAssignment)prop).EqualsToken!, Diagnostics.Did_you_mean_to_use_a_Colon_An_can_only_follow_a_property_name_when_the_containing_object_literal_is_part_of_a_destructuring_pattern);
            }

            if (name is not null && name.Kind == SyntaxKind.PrivateIdentifier)
            {
                GrammarErrorOnNode(name, Diagnostics.Private_identifiers_are_not_allowed_outside_class_bodies);
            }

            // Modifiers are never allowed on properties except for 'async' on a method declaration
            if (Utilities.CanHaveModifiers(prop) && Utilities.ModifiersOf(prop) is not null)
            {
                foreach (var mod in Utilities.ModifiersOf(prop)!)
                {
                    if (Utilities.IsModifierKind(mod.Kind) && (mod.Kind != SyntaxKind.AsyncKeyword || prop.Kind != SyntaxKind.MethodDeclaration))
                    {
                        GrammarErrorOnNode(mod, Diagnostics._0_modifier_cannot_be_used_here, Utilities.GetTextOfNode(mod));
                    }
                }
            }
            else if (FactoryUtilities.CanHaveIllegalModifiers(prop) && Utilities.ModifiersOf(prop) is not null)
            {
                foreach (var mod in Utilities.ModifiersOf(prop)!)
                {
                    if (Utilities.IsModifierKind(mod.Kind))
                    {
                        GrammarErrorOnNode(mod, Diagnostics._0_modifier_cannot_be_used_here, Utilities.GetTextOfNode(mod));
                    }
                }
            }

            // ECMA-262 11.1.5 Object Initializer
            // If previous is not undefined then throw a SyntaxError exception if any of the following conditions are true
            // a.This production is contained in strict code and IsDataDescriptor(previous) is true and
            // IsDataDescriptor(propId.descriptor) is true.
            //    b.IsDataDescriptor(previous) is true and IsAccessorDescriptor(propId.descriptor) is true.
            //    c.IsAccessorDescriptor(previous) is true and IsDataDescriptor(propId.descriptor) is true.
            //    d.IsAccessorDescriptor(previous) is true and IsAccessorDescriptor(propId.descriptor) is true
            // and either both previous and propId.descriptor have[[Get]] fields or both previous and propId.descriptor have[[Set]] fields
            DeclarationMeaning currentKind;
            switch (prop.Kind)
            {
                case SyntaxKind.ShorthandPropertyAssignment:
                case SyntaxKind.PropertyAssignment:
                {
                    // Grammar checking for computedPropertyName and shorthandPropertyAssignment
                    var exclamationToken = prop is ShorthandPropertyAssignment shorthand ? shorthand.ExclamationToken : ((PropertyAssignment)prop).ExclamationToken;
                    var questionToken = prop is ShorthandPropertyAssignment shorthandElement ? shorthandElement.QuestionToken : ((PropertyAssignment)prop).QuestionToken;
                    CheckGrammarForInvalidExclamationToken(exclamationToken, Diagnostics.A_definite_assignment_assertion_is_not_permitted_in_this_context);
                    CheckGrammarForInvalidQuestionMark(questionToken, Diagnostics.An_object_member_cannot_be_declared_optional);
                    if (name is not null && name.Kind == SyntaxKind.NumericLiteral)
                    {
                        CheckGrammarNumericLiteral((NumericLiteral)name);
                    }
                    if (name is not null && name.Kind == SyntaxKind.BigIntLiteral)
                    {
                        AddErrorOrSuggestion(/*isError*/ true, Utilities.CreateDiagnosticForNode(name, Diagnostics.A_bigint_literal_cannot_be_used_as_a_property_name));
                    }
                    currentKind = DeclarationMeaning.PropertyAssignment;
                    break;
                }
                case SyntaxKind.MethodDeclaration:
                    currentKind = DeclarationMeaning.Method;
                    break;
                case SyntaxKind.GetAccessor:
                    currentKind = DeclarationMeaning.GetAccessor;
                    break;
                case SyntaxKind.SetAccessor:
                    currentKind = DeclarationMeaning.SetAccessor;
                    break;
                default:
                    Debug.AssertNever(prop, "Unexpected syntax kind:" + prop.Kind);
                    return false;
            }

            if (!inDestructuring)
            {
                var effectiveName = GetEffectivePropertyNameForPropertyNameNode(name!);
                if (effectiveName is null)
                {
                    continue;
                }

                if (!seen.TryGetValue(effectiveName, out var existingKind))
                {
                    seen[effectiveName] = currentKind;
                }
                else
                {
                    if ((currentKind & DeclarationMeaning.Method) != 0 && (existingKind & DeclarationMeaning.Method) != 0)
                    {
                        GrammarErrorOnNode(name, Diagnostics.Duplicate_identifier_0, Utilities.GetTextOfNode(name!));
                    }
                    else if ((currentKind & DeclarationMeaning.PropertyAssignment) != 0 && (existingKind & DeclarationMeaning.PropertyAssignment) != 0)
                    {
                        GrammarErrorOnNode(name, Diagnostics.An_object_literal_cannot_have_multiple_properties_with_the_same_name, Utilities.GetTextOfNode(name!));
                    }
                    else if ((currentKind & DeclarationMeaning.GetOrSetAccessor) != 0 && (existingKind & DeclarationMeaning.GetOrSetAccessor) != 0)
                    {
                        if (existingKind != DeclarationMeaning.GetOrSetAccessor && currentKind != existingKind)
                        {
                            seen[effectiveName] = currentKind | existingKind;
                        }
                        else
                        {
                            return GrammarErrorOnNode(name, Diagnostics.An_object_literal_cannot_have_multiple_get_Slashset_accessors_with_the_same_name);
                        }
                    }
                    else
                    {
                        return GrammarErrorOnNode(name, Diagnostics.An_object_literal_cannot_have_property_and_accessor_with_the_same_name);
                    }
                }
            }
        }

        return false;
    }

    // ---------------------------------------------------------------------------------------------
    // The JSX checks (checker.ts:52712-52750)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:52712-52735 — <c>checkGrammarJsxElement</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is <c>JsxOpeningLikeElement</c>, the two classes
    /// <see cref="JsxOpeningElement"/>/<see cref="JsxSelfClosingElement"/>; the model declares no interface for the
    /// three slots it reads, so the kind is named once and the slots are read off the class.</remarks>
    internal bool CheckGrammarJsxElement(Node node)
    {
        Node? tagName;
        NodeArray<ITypeNode>? typeArguments;
        JsxAttributes? attributes;
        switch (node)
        {
            case JsxOpeningElement opening:
                tagName = opening.TagName;
                typeArguments = opening.TypeArguments;
                attributes = opening.Attributes;
                break;
            default:
                var selfClosing = (JsxSelfClosingElement)node;
                tagName = selfClosing.TagName;
                typeArguments = selfClosing.TypeArguments;
                attributes = selfClosing.Attributes;
                break;
        }

        CheckGrammarJsxName(tagName!);
        CheckGrammarTypeArguments(node, typeArguments);
        var seen = new HashSet<string>();

        foreach (var attr in attributes!.Properties!)
        {
            if (attr.Kind == SyntaxKind.JsxSpreadAttribute)
            {
                continue;
            }

            var name = ((JsxAttribute)attr).Name!;
            var initializer = ((JsxAttribute)attr).Initializer;
            var escapedText = Utilities.GetEscapedTextOfJsxAttributeName(name);
            if (!seen.Contains(escapedText))
            {
                seen.Add(escapedText);
            }
            else
            {
                return GrammarErrorOnNode(name, Diagnostics.JSX_elements_cannot_have_multiple_attributes_with_the_same_name);
            }

            if (initializer is not null && initializer.Kind == SyntaxKind.JsxExpression && ((JsxExpression)initializer).Expression is null)
            {
                return GrammarErrorOnNode(initializer, Diagnostics.JSX_attributes_must_only_be_assigned_a_non_empty_expression);
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:52737-52744 — <c>checkGrammarJsxName</c>.</summary>
    internal bool CheckGrammarJsxName(Node node)
    {
        if (NodeTests.IsPropertyAccessExpression(node) && NodeTests.IsJsxNamespacedName(((PropertyAccessExpression)node).Expression!))
        {
            return GrammarErrorOnNode(((PropertyAccessExpression)node).Expression!, Diagnostics.JSX_property_access_expressions_cannot_include_JSX_namespace_names);
        }
        if (NodeTests.IsJsxNamespacedName(node) && Utilities.GetJSXTransformEnabled(_compilerOptions) && !Utilities.IsIntrinsicJsxName(((JsxNamespacedName)node).Namespace!.EscapedText!))
        {
            return GrammarErrorOnNode(node, Diagnostics.React_components_cannot_include_JSX_namespace_names);
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:52746-52750 — <c>checkGrammarJsxExpression</c>.</summary>
    internal bool CheckGrammarJsxExpression(JsxExpression node)
    {
        if (node.Expression is not null && FactoryUtilities.IsCommaSequence(node.Expression))
        {
            return GrammarErrorOnNode(node.Expression, Diagnostics.JSX_expressions_may_not_use_the_comma_operator_Did_you_mean_to_write_an_array);
        }
        return false;
    }


    // ---------------------------------------------------------------------------------------------
    // The statement/accessor/type-operator checks (checker.ts:52752-53027)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:52752-52858 — <c>checkGrammarForInOrForOfStatement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>ForInOrOfStatement</c>; the port takes <see cref="Node"/> and reads
    /// <c>initializer</c> off the class the kind selects. The two <c>// fallthrough</c> comments of the module-kind
    /// switch are the C# <c>goto case</c>/<c>goto default</c>, which is how this port spells TS's case fallthrough.
    /// </remarks>
    internal bool CheckGrammarForInOrForOfStatement(Node forInOrOfStatement)
    {
        if (CheckGrammarStatementInAmbientContext(forInOrOfStatement))
        {
            return true;
        }

        var forOf = forInOrOfStatement as ForOfStatement;
        if (forInOrOfStatement.Kind == SyntaxKind.ForOfStatement && forOf!.AwaitModifier is not null)
        {
            if ((forInOrOfStatement.Flags & NodeFlags.AwaitContext) == 0)
            {
                var sourceFile = Utilities.GetSourceFileOfNode(forInOrOfStatement)!;
                if (Utilities.IsInTopLevelContext(forInOrOfStatement))
                {
                    if (!HasParseDiagnostics(sourceFile))
                    {
                        if (!Utilities.IsEffectiveExternalModule(sourceFile, _compilerOptions))
                        {
                            _diagnostics.Add(Utilities.CreateDiagnosticForNode(forOf.AwaitModifier, Diagnostics.for_await_loops_are_only_allowed_at_the_top_level_of_a_file_when_that_file_is_a_module_but_this_file_has_no_imports_or_exports_Consider_adding_an_empty_export_to_make_this_file_a_module));
                        }
                        switch (_moduleKind)
                        {
                            case ModuleKind.Node16:
                            case ModuleKind.Node18:
                            case ModuleKind.Node20:
                            case ModuleKind.NodeNext:
                                if ((sourceFile.ImpliedNodeFormat as ModuleKind?) == ModuleKind.CommonJS)
                                {
                                    _diagnostics.Add(
                                        Utilities.CreateDiagnosticForNode(forOf.AwaitModifier, Diagnostics.The_current_file_is_a_CommonJS_module_and_cannot_use_await_at_the_top_level));
                                    break;
                                }
                                // fallthrough
                                goto case ModuleKind.ES2022;
                            case ModuleKind.ES2022:
                            case ModuleKind.ESNext:
                            case ModuleKind.Preserve:
                            case ModuleKind.System:
                                if (_languageVersion >= ScriptTarget.ES2017)
                                {
                                    break;
                                }
                                // fallthrough
                                goto default;
                            default:
                                _diagnostics.Add(
                                    Utilities.CreateDiagnosticForNode(forOf.AwaitModifier, Diagnostics.Top_level_for_await_loops_are_only_allowed_when_the_module_option_is_set_to_es2022_esnext_system_node16_node18_node20_nodenext_or_preserve_and_the_target_option_is_set_to_es2017_or_higher));
                                break;
                        }
                    }
                }
                else
                {
                    // use of 'for-await-of' in non-async function
                    if (!HasParseDiagnostics(sourceFile))
                    {
                        var diagnostic = Utilities.CreateDiagnosticForNode(forOf.AwaitModifier, Diagnostics.for_await_loops_are_only_allowed_within_async_functions_and_at_the_top_levels_of_modules);
                        var func = Utilities.GetContainingFunction(forInOrOfStatement);
                        if (func is not null && func.Kind != SyntaxKind.Constructor)
                        {
                            Debug.Assert((Utilities.GetFunctionFlags(func) & FunctionFlags.Async) == 0, "Enclosing function should never be an async function.");
                            var relatedInfo = Utilities.CreateDiagnosticForNode(func, Diagnostics.Did_you_mean_to_mark_this_function_as_async);
                            Utilities.AddRelatedInfo(diagnostic, relatedInfo);
                        }
                        _diagnostics.Add(diagnostic);
                        return true;
                    }
                }
            }
        }

        var initializer = forInOrOfStatement.Kind == SyntaxKind.ForOfStatement ? forOf!.Initializer : ((ForInStatement)forInOrOfStatement).Initializer;
        if (
            NodeTests.IsForOfStatement(forInOrOfStatement) && (forInOrOfStatement.Flags & NodeFlags.AwaitContext) == 0 &&
            initializer is not null && NodeTests.IsIdentifier(initializer) && ((Identifier)initializer).EscapedText == "async"
        )
        {
            GrammarErrorOnNode(initializer, Diagnostics.The_left_hand_side_of_a_for_of_statement_may_not_be_async);
            return false;
        }

        if (initializer is not null && initializer.Kind == SyntaxKind.VariableDeclarationList)
        {
            var variableList = (VariableDeclarationList)initializer;
            if (!CheckGrammarVariableDeclarationList(variableList))
            {
                var declarations = variableList.Declarations!;

                // declarations.length can be zero if there is an error in variable declaration in for-of or for-in
                // See http://www.ecma-international.org/ecma-262/6.0/#sec-for-in-and-for-of-statements for details
                // For example:
                //      var let = 10;
                //      for (let of [1,2,3]) {} // this is invalid ES6 syntax
                //      for (let in [1,2,3]) {} // this is invalid ES6 syntax
                // We will then want to skip on grammar checking on variableList declaration
                if (declarations.Count == 0)
                {
                    return false;
                }

                if (declarations.Count > 1)
                {
                    var diagnostic = forInOrOfStatement.Kind == SyntaxKind.ForInStatement
                        ? Diagnostics.Only_a_single_variable_declaration_is_allowed_in_a_for_in_statement
                        : Diagnostics.Only_a_single_variable_declaration_is_allowed_in_a_for_of_statement;
                    return GrammarErrorOnFirstToken(declarations[1], diagnostic);
                }
                var firstDeclaration = declarations[0];

                if (firstDeclaration.Initializer is not null)
                {
                    var diagnostic = forInOrOfStatement.Kind == SyntaxKind.ForInStatement
                        ? Diagnostics.The_variable_declaration_of_a_for_in_statement_cannot_have_an_initializer
                        : Diagnostics.The_variable_declaration_of_a_for_of_statement_cannot_have_an_initializer;
                    return GrammarErrorOnNode(firstDeclaration.Name!, diagnostic);
                }
                if (firstDeclaration.Type is not null)
                {
                    var diagnostic = forInOrOfStatement.Kind == SyntaxKind.ForInStatement
                        ? Diagnostics.The_left_hand_side_of_a_for_in_statement_cannot_use_a_type_annotation
                        : Diagnostics.The_left_hand_side_of_a_for_of_statement_cannot_use_a_type_annotation;
                    return GrammarErrorOnNode(firstDeclaration, diagnostic);
                }
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:52860-52908 — <c>checkGrammarAccessor</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is <c>AccessorDeclaration</c> (the get/set accessor union); the port takes
    /// <see cref="Node"/> and reads <c>name</c>/<c>body</c>/<c>typeParameters</c>/<c>type</c> through
    /// <see cref="INamedDeclaration"/>, <see cref="IFunctionLikeDeclarationBase"/> and
    /// <see cref="ISignatureDeclarationBase"/>.</remarks>
    internal bool CheckGrammarAccessor(Node accessor)
    {
        if ((accessor.Flags & NodeFlags.Ambient) == 0 && (accessor.Parent!.Kind != SyntaxKind.TypeLiteral) && (accessor.Parent!.Kind != SyntaxKind.InterfaceDeclaration))
        {
            if (_languageVersion < ScriptTarget.ES2015 && NodeTests.IsPrivateIdentifier(((INamedDeclaration)accessor).Name!))
            {
                return GrammarErrorOnNode(((INamedDeclaration)accessor).Name!, Diagnostics.Private_identifiers_are_only_available_when_targeting_ECMAScript_2015_and_higher);
            }
            if (((IFunctionLikeDeclarationBase)accessor).Body is null && !Utilities.HasSyntacticModifier(accessor, ModifierFlags.Abstract))
            {
                return GrammarErrorAtPos(accessor, accessor.End - 1, 1, Diagnostics._0_expected, "{");
            }
        }
        if (((IFunctionLikeDeclarationBase)accessor).Body is { } body)
        {
            if (Utilities.HasSyntacticModifier(accessor, ModifierFlags.Abstract))
            {
                return GrammarErrorOnNode(accessor, Diagnostics.An_abstract_accessor_cannot_have_an_implementation);
            }
            if (accessor.Parent!.Kind == SyntaxKind.TypeLiteral || accessor.Parent!.Kind == SyntaxKind.InterfaceDeclaration)
            {
                return GrammarErrorOnNode(body, Diagnostics.An_implementation_cannot_be_declared_in_ambient_contexts);
            }
        }
        if (((ISignatureDeclarationBase)accessor).TypeParameters is not null)
        {
            return GrammarErrorOnNode(((INamedDeclaration)accessor).Name!, Diagnostics.An_accessor_cannot_have_type_parameters);
        }
        if (!DoesAccessorHaveCorrectParameterCount(accessor))
        {
            return GrammarErrorOnNode(
                ((INamedDeclaration)accessor).Name!,
                accessor.Kind == SyntaxKind.GetAccessor
                    ? Diagnostics.A_get_accessor_cannot_have_parameters
                    : Diagnostics.A_set_accessor_must_have_exactly_one_parameter);
        }
        if (accessor.Kind == SyntaxKind.SetAccessor)
        {
            if (((ISignatureDeclarationBase)accessor).Type is not null)
            {
                return GrammarErrorOnNode(((INamedDeclaration)accessor).Name!, Diagnostics.A_set_accessor_cannot_have_a_return_type_annotation);
            }
            var parameter = Debug.CheckDefined(Utilities.GetSetAccessorValueParameter((SetAccessorDeclaration)accessor), "Return value does not match parameter count assertion.");
            if (parameter.DotDotDotToken is not null)
            {
                return GrammarErrorOnNode(parameter.DotDotDotToken, Diagnostics.A_set_accessor_cannot_have_rest_parameter);
            }
            if (parameter.QuestionToken is not null)
            {
                return GrammarErrorOnNode(parameter.QuestionToken, Diagnostics.A_set_accessor_cannot_have_an_optional_parameter);
            }
            if (parameter.Initializer is not null)
            {
                return GrammarErrorOnNode(((INamedDeclaration)accessor).Name!, Diagnostics.A_set_accessor_parameter_cannot_have_an_initializer);
            }
        }
        return false;
    }

    /// <summary>
    /// Does the accessor have the right number of parameters?
    /// A get accessor has no parameters or a single `this` parameter.
    /// A set accessor has one parameter or a `this` parameter and one more parameter.
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:52910-52912 — <c>doesAccessorHaveCorrectParameterCount</c>.</remarks>
    internal bool DoesAccessorHaveCorrectParameterCount(Node accessor)
        => GetAccessorThisParameter(accessor) is not null
            || ((ISignatureDeclarationBase)accessor).Parameters!.Count == (accessor.Kind == SyntaxKind.GetAccessor ? 0 : 1);

    /// <summary>ported from: src/compiler/checker.ts:52914-52918 — <c>getAccessorThisParameter</c>.</summary>
    internal ParameterDeclaration? GetAccessorThisParameter(Node accessor)
    {
        if (((ISignatureDeclarationBase)accessor).Parameters!.Count == (accessor.Kind == SyntaxKind.GetAccessor ? 1 : 2))
        {
            return Utilities.GetThisParameter(accessor);
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:52920-52970 — <c>checkGrammarTypeOperatorNode</c>.</summary>
    internal bool CheckGrammarTypeOperatorNode(TypeOperatorNode node)
    {
        if (node.Operator == SyntaxKind.UniqueKeyword)
        {
            if (node.Type!.Kind != SyntaxKind.SymbolKeyword)
            {
                return GrammarErrorOnNode((Node)node.Type, Diagnostics._0_expected, ScannerUtilities.TokenToString(SyntaxKind.SymbolKeyword));
            }
            var parent = Utilities.WalkUpParenthesizedTypes(node.Parent!);
            if (Utilities.IsInJSFile(parent) && NodeTests.IsJSDocTypeExpression(parent))
            {
                var host = Utilities.GetJSDocHost(parent);
                if (host is not null)
                {
                    parent = Utilities.GetSingleVariableOfVariableStatement(host) ?? host;
                }
            }
            switch (parent.Kind)
            {
                case SyntaxKind.VariableDeclaration:
                {
                    var decl = (VariableDeclaration)parent;
                    if (decl.Name!.Kind != SyntaxKind.Identifier)
                    {
                        return GrammarErrorOnNode(node, Diagnostics.unique_symbol_types_may_not_be_used_on_a_variable_declaration_with_a_binding_name);
                    }
                    if (!Utilities.IsVariableDeclarationInVariableStatement(decl))
                    {
                        return GrammarErrorOnNode(node, Diagnostics.unique_symbol_types_are_only_allowed_on_variables_in_a_variable_statement);
                    }

                    if ((decl.Parent!.Flags & NodeFlags.Const) == 0)
                    {
                        return GrammarErrorOnNode(decl.Name, Diagnostics.A_variable_whose_type_is_a_unique_symbol_type_must_be_const);
                    }
                    break;
                }

                case SyntaxKind.PropertyDeclaration:
                    if (
                        !Utilities.IsStatic(parent) ||
                        !Utilities.HasEffectiveReadonlyModifier(parent)
                    ) {
                        return GrammarErrorOnNode(((PropertyDeclaration)parent).Name!, Diagnostics.A_property_of_a_class_whose_type_is_a_unique_symbol_type_must_be_both_static_and_readonly);
                    }
                    break;

                case SyntaxKind.PropertySignature:
                    if (!Utilities.HasSyntacticModifier(parent, ModifierFlags.Readonly))
                    {
                        return GrammarErrorOnNode(((PropertySignature)parent).Name!, Diagnostics.A_property_of_an_interface_or_type_literal_whose_type_is_a_unique_symbol_type_must_be_readonly);
                    }
                    break;

                default:
                    return GrammarErrorOnNode(node, Diagnostics.unique_symbol_types_are_not_allowed_here);
            }
        }
        else if (node.Operator == SyntaxKind.ReadonlyKeyword)
        {
            if (node.Type!.Kind != SyntaxKind.ArrayType && node.Type.Kind != SyntaxKind.TupleType)
            {
                return GrammarErrorOnFirstToken(node, Diagnostics.readonly_type_modifier_is_only_permitted_on_array_and_tuple_literal_types, ScannerUtilities.TokenToString(SyntaxKind.SymbolKeyword));
            }
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:52972-52977 — <c>checkGrammarForInvalidDynamicName</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is <c>DeclarationName</c>, which the model has no type for; the port takes
    /// <see cref="Node"/> and reads <c>argumentExpression</c> off the element-access class the guard selects.</remarks>
    internal bool CheckGrammarForInvalidDynamicName(Node node, DiagnosticMessage message)
    {
        // Even non-bindable names are allowed as late-bound implied index signatures so long as the name is a simple `a.b.c` type name expression
        if (IsNonBindableDynamicName(node) && !Utilities.IsEntityNameExpression(NodeTests.IsElementAccessExpression(node) ? Utilities.SkipParentheses(((ElementAccessExpression)node).ArgumentExpression!) : ((ComputedPropertyName)node).Expression!))
        {
            return GrammarErrorOnNode(node, message);
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:52979-53027 — <c>checkGrammarMethod</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is <c>MethodDeclaration | MethodSignature</c>; the port takes
    /// <see cref="Node"/> and reads <c>name</c> through <see cref="INamedDeclaration"/>, casting to
    /// <see cref="MethodDeclaration"/> only inside the arm its kind guards.</remarks>
    internal bool CheckGrammarMethod(Node node)
    {
        if (CheckGrammarFunctionLikeDeclaration(node))
        {
            return true;
        }

        if (node.Kind == SyntaxKind.MethodDeclaration)
        {
            var method = (MethodDeclaration)node;
            if (node.Parent!.Kind == SyntaxKind.ObjectLiteralExpression)
            {
                // We only disallow modifier on a method declaration if it is a property of object-literal-expression
                // PORT-NOTE: the reference reads `node.modifiers.length` and `first(node.modifiers)`; the shared
                // accessor answers IEnumerable, so the runtime NodeArray is reached through the local `as` below.
                var modifiers = Utilities.ModifiersOf(node) as IReadOnlyList<Node>;
                if (modifiers is not null && !(modifiers.Count == 1 && modifiers[0].Kind == SyntaxKind.AsyncKeyword))
                {
                    return GrammarErrorOnFirstToken(node, Diagnostics.Modifiers_cannot_appear_here);
                }
                else if (CheckGrammarForInvalidQuestionMark(method.QuestionToken, Diagnostics.An_object_member_cannot_be_declared_optional))
                {
                    return true;
                }
                else if (CheckGrammarForInvalidExclamationToken(method.ExclamationToken, Diagnostics.A_definite_assignment_assertion_is_not_permitted_in_this_context))
                {
                    return true;
                }
                else if (method.Body is null)
                {
                    return GrammarErrorAtPos(node, node.End - 1, 1, Diagnostics._0_expected, "{");
                }
            }
            if (CheckGrammarForGenerator(node))
            {
                return true;
            }
        }

        if (Utilities.IsClassLike(node.Parent!))
        {
            if (_languageVersion < ScriptTarget.ES2015 && NodeTests.IsPrivateIdentifier(((INamedDeclaration)node).Name!))
            {
                return GrammarErrorOnNode(((INamedDeclaration)node).Name!, Diagnostics.Private_identifiers_are_only_available_when_targeting_ECMAScript_2015_and_higher);
            }
            // Technically, computed properties in ambient contexts is disallowed
            // for property declarations and accessors too, not just methods.
            // However, property declarations disallow computed names in general,
            // and accessors are not allowed in ambient contexts in general,
            // so this error only really matters for methods.
            if ((node.Flags & NodeFlags.Ambient) != 0)
            {
                return CheckGrammarForInvalidDynamicName(((INamedDeclaration)node).Name!, Diagnostics.A_computed_property_name_in_an_ambient_context_must_refer_to_an_expression_whose_type_is_a_literal_type_or_a_unique_symbol_type);
            }
            else if (node.Kind == SyntaxKind.MethodDeclaration && ((IFunctionLikeDeclarationBase)node).Body is null)
            {
                return CheckGrammarForInvalidDynamicName(((INamedDeclaration)node).Name!, Diagnostics.A_computed_property_name_in_a_method_overload_must_refer_to_an_expression_whose_type_is_a_literal_type_or_a_unique_symbol_type);
            }
        }
        else if (node.Parent!.Kind == SyntaxKind.InterfaceDeclaration)
        {
            return CheckGrammarForInvalidDynamicName(((INamedDeclaration)node).Name!, Diagnostics.A_computed_property_name_in_an_interface_must_refer_to_an_expression_whose_type_is_a_literal_type_or_a_unique_symbol_type);
        }
        else if (node.Parent!.Kind == SyntaxKind.TypeLiteral)
        {
            return CheckGrammarForInvalidDynamicName(((INamedDeclaration)node).Name!, Diagnostics.A_computed_property_name_in_a_type_literal_must_refer_to_an_expression_whose_type_is_a_literal_type_or_a_unique_symbol_type);
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:53029-53081 — <c>checkGrammarBreakOrContinueStatement</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is <c>BreakOrContinueStatement</c>; the port takes <see cref="Node"/> and
    /// reads <c>label</c> off the class the kind selects.</remarks>
    internal bool CheckGrammarBreakOrContinueStatement(Node node)
    {
        var label = node.Kind == SyntaxKind.BreakStatement ? ((BreakStatement)node).Label : ((ContinueStatement)node).Label;
        Node? current = node;
        while (current is not null)
        {
            if (Utilities.IsFunctionLikeOrClassStaticBlockDeclaration(current))
            {
                return GrammarErrorOnNode(node, Diagnostics.Jump_target_cannot_cross_function_boundary);
            }

            switch (current.Kind)
            {
                case SyntaxKind.LabeledStatement:
                    var labeledStatement = (LabeledStatement)current;
                    if (label is not null && labeledStatement.Label!.EscapedText == label.EscapedText)
                    {
                        // found matching label - verify that label usage is correct
                        // continue can only target labels that are on iteration statements
                        var isMisplacedContinueLabel = node.Kind == SyntaxKind.ContinueStatement
                            && !Utilities.IsIterationStatement((Node)labeledStatement.Statement!, /*lookInLabeledStatements*/ true);

                        if (isMisplacedContinueLabel)
                        {
                            return GrammarErrorOnNode(node, Diagnostics.A_continue_statement_can_only_jump_to_a_label_of_an_enclosing_iteration_statement);
                        }

                        return false;
                    }
                    break;
                case SyntaxKind.SwitchStatement:
                    if (node.Kind == SyntaxKind.BreakStatement && label is null)
                    {
                        // unlabeled break within switch statement - ok
                        return false;
                    }
                    break;
                default:
                    if (Utilities.IsIterationStatement(current, /*lookInLabeledStatements*/ false) && label is null)
                    {
                        // unlabeled break or continue within iteration statement - ok
                        return false;
                    }
                    break;
            }

            current = current.Parent!;
        }

        if (label is not null)
        {
            var message = node.Kind == SyntaxKind.BreakStatement
                ? Diagnostics.A_break_statement_can_only_jump_to_a_label_of_an_enclosing_statement
                : Diagnostics.A_continue_statement_can_only_jump_to_a_label_of_an_enclosing_iteration_statement;

            return GrammarErrorOnNode(node, message);
        }
        else
        {
            var message = node.Kind == SyntaxKind.BreakStatement
                ? Diagnostics.A_break_statement_can_only_be_used_within_an_enclosing_iteration_or_switch_statement
                : Diagnostics.A_continue_statement_can_only_be_used_within_an_enclosing_iteration_statement;
            return GrammarErrorOnNode(node, message);
        }
    }


    // ---------------------------------------------------------------------------------------------
    // The element/variable-declaration checks (checker.ts:53083-53233)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:53083-53100 — <c>checkGrammarBindingElement</c>.</summary>
    /// <remarks>PORT-NOTE: TS reads <c>node.parent.elements</c> off the array/object binding pattern union; the port
    /// narrows to the covariant <see cref="INodeArray{T}"/> view of the node whose kind the parent has, because the two
    /// classes' element types differ (<c>Node</c> and <c>BindingElement</c>).</remarks>
    internal bool CheckGrammarBindingElement(BindingElement node)
    {
        if (node.DotDotDotToken is not null)
        {
            var elements = node.Parent switch
            {
                ArrayBindingPattern arrayBinding => (INodeArray<Node>?)arrayBinding.Elements,
                ObjectBindingPattern objectBinding => objectBinding.Elements,
                _ => null,
            };
            if (!ReferenceEquals(node, elements![elements.Count - 1]))
            {
                return GrammarErrorOnNode(node, Diagnostics.A_rest_element_must_be_last_in_a_destructuring_pattern);
            }
            CheckGrammarForDisallowedTrailingComma(elements, Diagnostics.A_rest_parameter_or_binding_pattern_may_not_have_a_trailing_comma);

            if (node.PropertyName is not null)
            {
                return GrammarErrorOnNode(node.Name!, Diagnostics.A_rest_element_cannot_have_a_property_name);
            }
        }

        if (node.DotDotDotToken is not null && node.Initializer is not null)
        {
            // Error on equals token which immediately precedes the initializer
            return GrammarErrorAtPos(node, node.Initializer.Pos - 1, 1, Diagnostics.A_rest_element_cannot_have_an_initializer);
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:53102-53106 — <c>isStringOrNumberLiteralExpression</c>.</summary>
    internal static bool IsStringOrNumberLiteralExpression(Expression expr)
        => Utilities.IsStringOrNumericLiteralLike(expr)
            || (expr.Kind == SyntaxKind.PrefixUnaryExpression && ((PrefixUnaryExpression)expr).Operator == SyntaxKind.MinusToken
                && ((PrefixUnaryExpression)expr).Operand!.Kind == SyntaxKind.NumericLiteral);

    /// <summary>ported from: src/compiler/checker.ts:53108-53112 — <c>isBigIntLiteralExpression</c>.</summary>
    internal static bool IsBigIntLiteralExpression(Expression expr)
        => expr.Kind == SyntaxKind.BigIntLiteral
            || (expr.Kind == SyntaxKind.PrefixUnaryExpression && ((PrefixUnaryExpression)expr).Operator == SyntaxKind.MinusToken
                && ((PrefixUnaryExpression)expr).Operand!.Kind == SyntaxKind.BigIntLiteral);

    /// <summary>ported from: src/compiler/checker.ts:53114-53121 — <c>isSimpleLiteralEnumReference</c>.</summary>
    /// <remarks>PORT-NOTE: TS narrows <c>expr</c> to <c>PropertyAccessExpression | ElementAccessExpression</c> and reads
    /// their shared <c>expression</c>; the port reads it off the class the kind selects, because the generated
    /// <c>MemberExpression</c> base declares no such slot.</remarks>
    internal bool IsSimpleLiteralEnumReference(Expression expr)
    {
        var expression = expr switch
        {
            PropertyAccessExpression propertyAccess => propertyAccess.Expression,
            ElementAccessExpression elementAccess => elementAccess.Expression,
            _ => null,
        };
        if (
            (NodeTests.IsPropertyAccessExpression(expr) || (NodeTests.IsElementAccessExpression(expr) && IsStringOrNumberLiteralExpression(((ElementAccessExpression)expr).ArgumentExpression!))) &&
            expression is not null && Utilities.IsEntityNameExpression(expression)
        ) {
            return (CheckExpressionCached(expr).Flags & TypeFlags.EnumLike) != 0;
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:53123-53142 — <c>checkAmbientInitializer</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is <c>VariableDeclaration | PropertyDeclaration | PropertySignature</c>, and
    /// the model declares <c>initializer</c>/<c>type</c> on those three classes without a shared interface, so the port
    /// takes <see cref="Node"/> and reads both slots off the class the node is. <c>isDeclarationReadonly</c> takes TS's
    /// <c>Declaration</c>, which the port spells <see cref="IDeclaration"/> (the same mapping the landed
    /// <c>GetCombinedModifierFlagsCached</c> uses).</remarks>
    internal bool CheckAmbientInitializer(Node node)
    {
        var (initializer, type) = node switch
        {
            VariableDeclaration declaration => (declaration.Initializer, declaration.Type),
            PropertyDeclaration declaration => (declaration.Initializer, declaration.Type),
            PropertySignature declaration => (declaration.Initializer, declaration.Type),
            _ => ((Expression?)null, (ITypeNode?)null),
        };
        if (initializer is not null)
        {
            var isInvalidInitializer = !(
                IsStringOrNumberLiteralExpression(initializer) ||
                IsSimpleLiteralEnumReference(initializer) ||
                initializer.Kind == SyntaxKind.TrueKeyword || initializer.Kind == SyntaxKind.FalseKeyword ||
                IsBigIntLiteralExpression(initializer)
            );
            var isConstOrReadonly = Utilities.IsDeclarationReadonly((IDeclaration)node) || NodeTests.IsVariableDeclaration(node) && IsVarConstLike(node);
            if (isConstOrReadonly && type is null)
            {
                if (isInvalidInitializer)
                {
                    return GrammarErrorOnNode(initializer, Diagnostics.A_const_initializer_in_an_ambient_context_must_be_a_string_or_numeric_literal_or_literal_enum_reference);
                }
            }
            else
            {
                return GrammarErrorOnNode(initializer, Diagnostics.Initializers_are_not_allowed_in_ambient_contexts);
            }
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:53144-53199 — <c>checkGrammarVariableDeclaration</c>.</summary>
    internal bool CheckGrammarVariableDeclaration(VariableDeclaration node)
    {
        var nodeFlags = GetCombinedNodeFlagsCached(node);
        var blockScopeKind = nodeFlags & NodeFlags.BlockScoped;
        if (Utilities.IsBindingPattern(node.Name!))
        {
            switch (blockScopeKind)
            {
                case NodeFlags.AwaitUsing:
                    return GrammarErrorOnNode(node, Diagnostics._0_declarations_may_not_have_binding_patterns, "await using");
                case NodeFlags.Using:
                    return GrammarErrorOnNode(node, Diagnostics._0_declarations_may_not_have_binding_patterns, "using");
            }
        }

        if (node.Parent!.Parent!.Kind != SyntaxKind.ForInStatement && node.Parent!.Parent!.Kind != SyntaxKind.ForOfStatement)
        {
            if ((nodeFlags & NodeFlags.Ambient) != 0)
            {
                CheckAmbientInitializer(node);
            }
            else if (node.Initializer is null)
            {
                if (Utilities.IsBindingPattern(node.Name!) && !Utilities.IsBindingPattern(node.Parent!))
                {
                    return GrammarErrorOnNode(node, Diagnostics.A_destructuring_declaration_must_have_an_initializer);
                }
                switch (blockScopeKind)
                {
                    case NodeFlags.AwaitUsing:
                        return GrammarErrorOnNode(node, Diagnostics._0_declarations_must_be_initialized, "await using");
                    case NodeFlags.Using:
                        return GrammarErrorOnNode(node, Diagnostics._0_declarations_must_be_initialized, "using");
                    case NodeFlags.Const:
                        return GrammarErrorOnNode(node, Diagnostics._0_declarations_must_be_initialized, "const");
                }
            }
        }

        if (node.ExclamationToken is not null && (node.Parent!.Parent!.Kind != SyntaxKind.VariableStatement || node.Type is null || node.Initializer is not null || (nodeFlags & NodeFlags.Ambient) != 0))
        {
            var message = node.Initializer is not null
                ? Diagnostics.Declarations_with_initializers_cannot_also_have_definite_assignment_assertions
                : node.Type is null
                    ? Diagnostics.Declarations_with_definite_assignment_assertions_must_also_have_type_annotations
                    : Diagnostics.A_definite_assignment_assertion_is_not_permitted_in_this_context;
            return GrammarErrorOnNode(node.ExclamationToken, message);
        }

        if (
            _host.GetEmitModuleFormatOfFile(Utilities.GetSourceFileOfNode(node)) < ModuleKind.System &&
            (node.Parent!.Parent!.Flags & NodeFlags.Ambient) == 0 && Utilities.HasSyntacticModifier(node.Parent!.Parent!, ModifierFlags.Export)
        ) {
            CheckESModuleMarker(node.Name!);
        }

        // 1. LexicalDeclaration : LetOrConst BindingList ;
        // It is a Syntax Error if the BoundNames of BindingList contains "let".
        // 2. ForDeclaration: ForDeclaration : LetOrConst ForBinding
        // It is a Syntax Error if the BoundNames of ForDeclaration contains "let".

        // It is a SyntaxError if a VariableDeclaration or VariableDeclarationNoIn occurs within strict code
        // and its Identifier is eval or arguments
        return blockScopeKind != NodeFlags.None && CheckGrammarNameInLetOrConstDeclarations(node.Name!);
    }

    /// <summary>ported from: src/compiler/checker.ts:53201-53216 — <c>checkESModuleMarker</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is <c>Identifier | BindingPattern</c>; the port takes <see cref="Node"/> and
    /// narrows <c>elements</c>/<c>name</c> the same way <see cref="CheckGrammarBindingElement"/> does.</remarks>
    internal bool CheckESModuleMarker(Node name)
    {
        if (name.Kind == SyntaxKind.Identifier)
        {
            if (Utilities.IdText(name) == "__esModule")
            {
                return GrammarErrorOnNodeSkippedOn("noEmit", name, Diagnostics.Identifier_expected_esModule_is_reserved_as_an_exported_marker_when_transforming_ECMAScript_modules);
            }
        }
        else
        {
            var elements = name switch
            {
                ArrayBindingPattern arrayBinding => (IReadOnlyList<Node>?)arrayBinding.Elements,
                ObjectBindingPattern objectBinding => objectBinding.Elements,
                _ => null,
            };
            foreach (var element in elements!)
            {
                if (!NodeTests.IsOmittedExpression(element))
                {
                    return CheckESModuleMarker(((BindingElement)element).Name!);
                }
            }
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:53218-53233 — <c>checkGrammarNameInLetOrConstDeclarations</c>.</summary>
    internal bool CheckGrammarNameInLetOrConstDeclarations(Node name)
    {
        if (name.Kind == SyntaxKind.Identifier)
        {
            if (((Identifier)name).EscapedText == "let")
            {
                return GrammarErrorOnNode(name, Diagnostics.let_is_not_allowed_to_be_used_as_a_name_in_let_or_const_declarations);
            }
        }
        else
        {
            var elements = name switch
            {
                ArrayBindingPattern arrayBinding => (IReadOnlyList<Node>?)arrayBinding.Elements,
                ObjectBindingPattern objectBinding => objectBinding.Elements,
                _ => null,
            };
            foreach (var element in elements!)
            {
                if (!NodeTests.IsOmittedExpression(element))
                {
                    CheckGrammarNameInLetOrConstDeclarations(((BindingElement)element).Name!);
                }
            }
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:53235-53279 — <c>checkGrammarVariableDeclarationList</c>.</summary>
    internal bool CheckGrammarVariableDeclarationList(VariableDeclarationList declarationList)
    {
        var declarations = declarationList.Declarations!;
        if (CheckGrammarForDisallowedTrailingComma(declarationList.Declarations))
        {
            return true;
        }

        if (declarations.Count == 0)
        {
            return GrammarErrorAtPos(declarationList, declarations.Pos, declarations.End - declarations.Pos, Diagnostics.Variable_declaration_list_cannot_be_empty);
        }

        var blockScopeFlags = declarationList.Flags & NodeFlags.BlockScoped;
        if (blockScopeFlags == NodeFlags.Using || blockScopeFlags == NodeFlags.AwaitUsing)
        {
            if (NodeTests.IsForInStatement(declarationList.Parent!))
            {
                return GrammarErrorOnNode(
                    declarationList,
                    blockScopeFlags == NodeFlags.Using
                        ? Diagnostics.The_left_hand_side_of_a_for_in_statement_cannot_be_a_using_declaration
                        : Diagnostics.The_left_hand_side_of_a_for_in_statement_cannot_be_an_await_using_declaration);
            }
            else if (NodeTests.IsVariableStatement(declarationList.Parent!) && Utilities.IsCaseOrDefaultClause(declarationList.Parent!.Parent!))
            {
                return GrammarErrorOnNode(
                    declarationList,
                    blockScopeFlags == NodeFlags.Using
                        ? Diagnostics.using_declarations_are_not_allowed_in_case_or_default_clauses_unless_contained_within_a_block
                        : Diagnostics.await_using_declarations_are_not_allowed_in_case_or_default_clauses_unless_contained_within_a_block);
            }

            if ((declarationList.Flags & NodeFlags.Ambient) != 0)
            {
                return GrammarErrorOnNode(
                    declarationList,
                    blockScopeFlags == NodeFlags.Using
                        ? Diagnostics.using_declarations_are_not_allowed_in_ambient_contexts
                        : Diagnostics.await_using_declarations_are_not_allowed_in_ambient_contexts);
            }

            if (blockScopeFlags == NodeFlags.AwaitUsing)
            {
                return CheckAwaitGrammar(declarationList);
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:53281-53296 — <c>allowBlockDeclarations</c>.</summary>
    internal static bool AllowBlockDeclarations(Node parent)
    {
        switch (parent.Kind)
        {
            case SyntaxKind.IfStatement:
            case SyntaxKind.DoStatement:
            case SyntaxKind.WhileStatement:
            case SyntaxKind.WithStatement:
            case SyntaxKind.ForStatement:
            case SyntaxKind.ForInStatement:
            case SyntaxKind.ForOfStatement:
                return false;
            case SyntaxKind.LabeledStatement:
                return AllowBlockDeclarations(parent.Parent!);
        }

        return true;
    }

    /// <summary>ported from: src/compiler/checker.ts:53298-53310 — <c>checkGrammarForDisallowedBlockScopedVariableStatement</c>.</summary>
    internal void CheckGrammarForDisallowedBlockScopedVariableStatement(VariableStatement node)
    {
        if (!AllowBlockDeclarations(node.Parent!))
        {
            var blockScopeKind = GetCombinedNodeFlagsCached(node.DeclarationList!) & NodeFlags.BlockScoped;
            if (blockScopeKind != NodeFlags.None)
            {
                var keyword = blockScopeKind == NodeFlags.Let ? "let"
                    : blockScopeKind == NodeFlags.Const ? "const"
                    : blockScopeKind == NodeFlags.Using ? "using"
                    : blockScopeKind == NodeFlags.AwaitUsing ? "await using"
                    : Debug.Fail<string>("Unknown BlockScope flag");
                Error(node, Diagnostics._0_declarations_can_only_be_declared_inside_a_block, keyword);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:53312-53337 — <c>checkGrammarMetaProperty</c>.</summary>
    internal bool CheckGrammarMetaProperty(MetaProperty node)
    {
        var escapedText = node.Name!.EscapedText;
        switch (node.KeywordToken)
        {
            case SyntaxKind.NewKeyword:
                if (escapedText != "target")
                {
                    return GrammarErrorOnNode(node.Name!, Diagnostics._0_is_not_a_valid_meta_property_for_keyword_1_Did_you_mean_2, Utilities.UnescapeLeadingUnderscores(node.Name!.EscapedText!), ScannerUtilities.TokenToString(node.KeywordToken), "target");
                }
                break;
            case SyntaxKind.ImportKeyword:
                if (escapedText != "meta")
                {
                    var isCallee = NodeTests.IsCallExpression(node.Parent!) && ReferenceEquals(((CallExpression)node.Parent!).Expression, node);
                    if (escapedText == "defer")
                    {
                        if (!isCallee)
                        {
                            return GrammarErrorAtPos(node, node.End, 0, Diagnostics._0_expected, "(");
                        }
                    }
                    else
                    {
                        if (isCallee)
                        {
                            return GrammarErrorOnNode(node.Name!, Diagnostics._0_is_not_a_valid_meta_property_for_keyword_import_Did_you_mean_meta_or_defer, Utilities.UnescapeLeadingUnderscores(node.Name!.EscapedText!));
                        }
                        return GrammarErrorOnNode(node.Name!, Diagnostics._0_is_not_a_valid_meta_property_for_keyword_1_Did_you_mean_2, Utilities.UnescapeLeadingUnderscores(node.Name!.EscapedText!), ScannerUtilities.TokenToString(node.KeywordToken), "meta");
                    }
                }
                break;
        }
        return false;
    }


    // ---------------------------------------------------------------------------------------------
    // The diagnostic helpers (checker.ts:53339-53378)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:53339-53341 — <c>hasParseDiagnostics</c>.</summary>
    internal static bool HasParseDiagnostics(SourceFile sourceFile) => Core.Length(sourceFile.ParseDiagnostics) > 0;

    /// <summary>ported from: src/compiler/checker.ts:53343-53351 — <c>grammarErrorOnFirstToken</c>.</summary>
    internal bool GrammarErrorOnFirstToken(Node node, DiagnosticMessage message, params object?[] args)
    {
        var sourceFile = Utilities.GetSourceFileOfNode(node)!;
        if (!HasParseDiagnostics(sourceFile))
        {
            var span = Utilities.GetSpanOfTokenAtPosition(sourceFile, node.Pos);
            _diagnostics.Add(Utilities.CreateFileDiagnostic(sourceFile, span.Start, span.Length, message, args));
            return true;
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:53353-53360 — <c>grammarErrorAtPos</c>.</summary>
    internal bool GrammarErrorAtPos(Node nodeForSourceFile, int start, int length, DiagnosticMessage message, params object?[] args)
    {
        var sourceFile = Utilities.GetSourceFileOfNode(nodeForSourceFile)!;
        if (!HasParseDiagnostics(sourceFile))
        {
            _diagnostics.Add(Utilities.CreateFileDiagnostic(sourceFile, start, length, message, args));
            return true;
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:53362-53369 — <c>grammarErrorOnNodeSkippedOn</c>.</summary>
    internal bool GrammarErrorOnNodeSkippedOn(string key, Node node, DiagnosticMessage message, params object?[] args)
    {
        var sourceFile = Utilities.GetSourceFileOfNode(node)!;
        if (!HasParseDiagnostics(sourceFile))
        {
            ErrorSkippedOn(key, node, message, args);
            return true;
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:53371-53378 — <c>grammarErrorOnNode</c>.</summary>
    internal bool GrammarErrorOnNode(Node? node, DiagnosticMessage message, params object?[] args)
    {
        var sourceFile = Utilities.GetSourceFileOfNode(node)!;
        if (!HasParseDiagnostics(sourceFile))
        {
            Error(node, message, args);
            return true;
        }
        return false;
    }

    // ---------------------------------------------------------------------------------------------
    // The constructor/property/top-level checks (checker.ts:53380-53498)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:53380-53387 — <c>checkGrammarConstructorTypeParameters</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>range</c> is <c>NodeArray&lt;TypeParameterDeclaration&gt; | TypeParameterDeclaration</c>;
    /// the port states it as the <see cref="IReadonlyTextRange"/> both implement, which is all the body reads.</remarks>
    internal bool CheckGrammarConstructorTypeParameters(ConstructorDeclaration node)
    {
        var jsdocTypeParameters = Utilities.IsInJSFile(node) ? Utilities.GetJSDocTypeParameterDeclarations(node) : null;
        IReadonlyTextRange? range = (IReadonlyTextRange?)node.TypeParameters ?? (jsdocTypeParameters is not null ? Core.FirstOrUndefined(jsdocTypeParameters) : null);
        if (range is not null)
        {
            var pos = range.Pos == range.End ? range.Pos : ScannerUtilities.SkipTrivia(Utilities.GetSourceFileOfNode(node)!.Text ?? "", range.Pos);
            return GrammarErrorAtPos(node, pos, range.End - pos, Diagnostics.Type_parameters_cannot_appear_on_a_constructor_declaration);
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:53389-53394 — <c>checkGrammarConstructorTypeAnnotation</c>.</summary>
    internal bool CheckGrammarConstructorTypeAnnotation(ConstructorDeclaration node)
    {
        var type = node.Type ?? Utilities.GetEffectiveReturnTypeNode(node);
        if (type is not null)
        {
            return GrammarErrorOnNode((Node)type, Diagnostics.Type_annotation_cannot_appear_on_a_constructor_declaration);
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:53396-53454 — <c>checkGrammarProperty</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is <c>PropertyDeclaration | PropertySignature</c>; the port takes
    /// <see cref="Node"/>, reads <c>name</c>/<c>questionToken</c> off the class the kind selects, and asserts the
    /// <c>PropertySignature</c> arm with <c>Debug.AssertNode</c> exactly as the reference does.
    /// PORT-NOTE: TS's <c>node.parent.members[0]</c> (checker.ts:53398) reads whatever parent its guard admitted, and
    /// the two parameter types admit three faces — <c>ClassLikeDeclaration</c> for <c>PropertyDeclaration</c>
    /// (types.ts:1934) and <c>TypeLiteralNode | InterfaceDeclaration</c> for <c>PropertySignature</c> (types.ts:1921) —
    /// so no one of them can carry the read, though all three declare <c>members</c>. The parser also hangs trailing
    /// members off the <c>MappedTypeNode</c> it builds for a mapped type (parser.ts:4433, types.ts:2353), whose own
    /// members <c>checkGrammarMappedType</c> reports (checker.ts:42979-42983). The port's faces name two different list
    /// types (<c>IClassElement</c> against <c>ITypeElement</c>), so the read is a pattern over the five concrete
    /// declarations instead of a cast to one of them; the reported node and diagnostic are unchanged.</remarks>
    internal bool CheckGrammarProperty(Node node)
    {
        var name = ((INamedDeclaration)node).Name!;
        if (name.Kind == SyntaxKind.ComputedPropertyName && ((ComputedPropertyName)name).Expression!.Kind == SyntaxKind.BinaryExpression && ((BinaryExpression)((ComputedPropertyName)name).Expression!).OperatorToken!.Kind == SyntaxKind.InKeyword)
        {
            var firstMember = node.Parent switch
            {
                ClassDeclaration declaration => (Node)declaration.Members![0],
                ClassExpression declaration => (Node)declaration.Members![0],
                InterfaceDeclaration declaration => (Node)declaration.Members![0],
                TypeLiteralNode declaration => (Node)declaration.Members![0],
                MappedTypeNode declaration => (Node)declaration.Members![0],
                _ => Debug.Fail<Node>($"CheckGrammarProperty: an 'in' name's parent is {node.Parent!.Kind}"),
            };
            return GrammarErrorOnNode(firstMember, Diagnostics.A_mapped_type_may_not_declare_properties_or_methods);
        }
        if (Utilities.IsClassLike(node.Parent!))
        {
            if (NodeTests.IsStringLiteral(name) && ((StringLiteral)name).Text == "constructor")
            {
                return GrammarErrorOnNode(name, Diagnostics.Classes_may_not_have_a_field_named_constructor);
            }
            if (CheckGrammarForInvalidDynamicName(name, Diagnostics.A_computed_property_name_in_a_class_property_declaration_must_have_a_simple_literal_type_or_a_unique_symbol_type))
            {
                return true;
            }
            if (_languageVersion < ScriptTarget.ES2015 && NodeTests.IsPrivateIdentifier(name))
            {
                return GrammarErrorOnNode(name, Diagnostics.Private_identifiers_are_only_available_when_targeting_ECMAScript_2015_and_higher);
            }
            if (_languageVersion < ScriptTarget.ES2015 && Utilities.IsAutoAccessorPropertyDeclaration(node) && (node.Flags & NodeFlags.Ambient) == 0)
            {
                return GrammarErrorOnNode(name, Diagnostics.Properties_with_the_accessor_modifier_are_only_available_when_targeting_ECMAScript_2015_and_higher);
            }
            var questionToken = node switch
            {
                PropertyDeclaration declaration => declaration.QuestionToken,
                PropertySignature signature => signature.QuestionToken,
                _ => null,
            };
            if (Utilities.IsAutoAccessorPropertyDeclaration(node) && CheckGrammarForInvalidQuestionMark(questionToken, Diagnostics.An_accessor_property_cannot_be_declared_optional))
            {
                return true;
            }
        }
        else if (node.Parent!.Kind == SyntaxKind.InterfaceDeclaration)
        {
            if (CheckGrammarForInvalidDynamicName(name, Diagnostics.A_computed_property_name_in_an_interface_must_refer_to_an_expression_whose_type_is_a_literal_type_or_a_unique_symbol_type))
            {
                return true;
            }

            // Interfaces cannot contain property declarations
            Debug.AssertNode(node, NodeTests.IsPropertySignature);
            if (((PropertySignature)node).Initializer is not null)
            {
                return GrammarErrorOnNode(((PropertySignature)node).Initializer, Diagnostics.An_interface_property_cannot_have_an_initializer);
            }
        }
        else if (NodeTests.IsTypeLiteralNode(node.Parent!))
        {
            if (CheckGrammarForInvalidDynamicName(name, Diagnostics.A_computed_property_name_in_a_type_literal_must_refer_to_an_expression_whose_type_is_a_literal_type_or_a_unique_symbol_type))
            {
                return true;
            }
            // Type literals cannot contain property declarations
            Debug.AssertNode(node, NodeTests.IsPropertySignature);
            if (((PropertySignature)node).Initializer is not null)
            {
                return GrammarErrorOnNode(((PropertySignature)node).Initializer, Diagnostics.A_type_literal_property_cannot_have_an_initializer);
            }
        }

        if ((node.Flags & NodeFlags.Ambient) != 0)
        {
            CheckAmbientInitializer(node);
        }

        if (NodeTests.IsPropertyDeclaration(node))
        {
            var declaration = (PropertyDeclaration)node;
            if (declaration.ExclamationToken is not null && (!Utilities.IsClassLike(node.Parent!) || declaration.Type is null || declaration.Initializer is not null ||
                (node.Flags & NodeFlags.Ambient) != 0 || Utilities.IsStatic(node) || Utilities.HasAbstractModifier(node)))
            {
                var message = declaration.Initializer is not null
                    ? Diagnostics.Declarations_with_initializers_cannot_also_have_definite_assignment_assertions
                    : declaration.Type is null
                        ? Diagnostics.Declarations_with_definite_assignment_assertions_must_also_have_type_annotations
                        : Diagnostics.A_definite_assignment_assertion_is_not_permitted_in_this_context;
                return GrammarErrorOnNode(declaration.ExclamationToken, message);
            }
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:53456-53483 — <c>checkGrammarTopLevelElementForRequiredDeclareModifier</c>.</summary>
    internal bool CheckGrammarTopLevelElementForRequiredDeclareModifier(Node node)
    {
        // A declare modifier is required for any top level .d.ts declaration except export=, export default, export as namespace
        // interfaces and imports categories:
        //
        //  DeclarationElement:
        //     ExportAssignment
        //     export_opt   InterfaceDeclaration
        //     export_opt   TypeAliasDeclaration
        //     export_opt   ImportDeclaration
        //     export_opt   ExternalImportDeclaration
        //     export_opt   AmbientDeclaration
        //
        // TODO: The spec needs to be amended to reflect this grammar.
        if (
            node.Kind == SyntaxKind.InterfaceDeclaration ||
            node.Kind == SyntaxKind.TypeAliasDeclaration ||
            node.Kind == SyntaxKind.ImportDeclaration ||
            node.Kind == SyntaxKind.ImportEqualsDeclaration ||
            node.Kind == SyntaxKind.ExportDeclaration ||
            node.Kind == SyntaxKind.ExportAssignment ||
            node.Kind == SyntaxKind.NamespaceExportDeclaration ||
            Utilities.HasSyntacticModifier(node, ModifierFlags.Ambient | ModifierFlags.Export | ModifierFlags.Default)
        )
        {
            return false;
        }

        return GrammarErrorOnFirstToken(node, Diagnostics.Top_level_declarations_in_d_ts_files_must_start_with_either_a_declare_or_export_modifier);
    }

    /// <summary>ported from: src/compiler/checker.ts:53485-53494 — <c>checkGrammarTopLevelElementsForRequiredDeclareModifier</c>.</summary>
    internal bool CheckGrammarTopLevelElementsForRequiredDeclareModifier(SourceFile file)
    {
        foreach (var decl in file.Statements!)
        {
            if (Utilities.IsDeclaration((Node)decl) || decl.Kind == SyntaxKind.VariableStatement)
            {
                if (CheckGrammarTopLevelElementForRequiredDeclareModifier((Node)decl))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:53496-53498 — <c>checkGrammarSourceFile</c>.</summary>
    internal bool CheckGrammarSourceFile(SourceFile node)
        => (node.Flags & NodeFlags.Ambient) != 0 && CheckGrammarTopLevelElementsForRequiredDeclareModifier(node);


    // ---------------------------------------------------------------------------------------------
    // The ambient-statement/literal/module checks (checker.ts:53500-53668)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:53500-53527 — <c>checkGrammarStatementInAmbientContext</c>.</summary>
    internal bool CheckGrammarStatementInAmbientContext(Node node)
    {
        if ((node.Flags & NodeFlags.Ambient) != 0)
        {
            // Find containing block which is either Block, ModuleBlock, SourceFile
            var links = GetNodeLinks(node);
            if (links.HasReportedStatementInAmbientContext != true && (Utilities.IsFunctionLike(node.Parent) || Utilities.IsAccessor(node.Parent)))
            {
                var reported = GrammarErrorOnFirstToken(node, Diagnostics.An_implementation_cannot_be_declared_in_ambient_contexts);
                links.HasReportedStatementInAmbientContext = reported;
                return reported;
            }

            // We are either parented by another statement, or some sort of block.
            // If we're in a block, we only want to really report an error once
            // to prevent noisiness.  So use a bit on the block to indicate if
            // this has already been reported, and don't report if it has.
            //
            if (node.Parent!.Kind == SyntaxKind.Block || node.Parent!.Kind == SyntaxKind.ModuleBlock || node.Parent!.Kind == SyntaxKind.SourceFile)
            {
                var parentLinks = GetNodeLinks(node.Parent!);
                // Check if the containing block ever report this error
                if (parentLinks.HasReportedStatementInAmbientContext != true)
                {
                    var reported = GrammarErrorOnFirstToken(node, Diagnostics.Statements_are_not_allowed_in_ambient_contexts);
                    parentLinks.HasReportedStatementInAmbientContext = reported;
                    return reported;
                }
            }
            else
            {
                // We must be parented by a statement.  If so, there's no need
                // to report the error as our parent will have already done it.
                // Debug.assert(isStatement(node.parent));
            }
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:53529-53553 — <c>checkGrammarNumericLiteral</c>.</summary>
    internal void CheckGrammarNumericLiteral(NumericLiteral node)
    {
        // Realism (size) checking
        // We should test against `getTextOfNode(node)` rather than `node.text`, because `node.text` for large numeric literals can contain "."
        // e.g. `node.text` for numeric literal `1100000000000000000000` is `1.1e21`.
        var isFractional = Utilities.GetTextOfNode(node).Contains('.');
        var isScientific = (node.NumericLiteralFlags & TokenFlags.Scientific) != 0;

        // Scientific notation (e.g. 2e54 and 1e00000000010) can't be converted to bigint
        // Fractional numbers (e.g. 9000000000000000.001) are inherently imprecise anyway
        if (isFractional || isScientific)
        {
            return;
        }

        // Here `node` is guaranteed to be a numeric literal representing an integer.
        // We need to judge whether the integer `node` represents is <= 2 ** 53 - 1, which can be accomplished by comparing to `value` defined below because:
        // 1) when `node` represents an integer <= 2 ** 53 - 1, `node.text` is its exact string representation and thus `value` precisely represents the integer.
        // 2) otherwise, although `node.text` may be imprecise string representation, its mathematical value and consequently `value` cannot be less than 2 ** 53,
        //    thus the result of the predicate won't be affected.
        var value = Js.ToNumber(node.Text!);
        if (value <= Math.Pow(2, 53) - 1)
        {
            return;
        }

        AddErrorOrSuggestion(/*isError*/ false, Utilities.CreateDiagnosticForNode(node, Diagnostics.Numeric_literals_with_absolute_values_equal_to_2_53_or_greater_are_too_large_to_be_represented_accurately_as_integers));
    }

    /// <summary>ported from: src/compiler/checker.ts:53555-53567 — <c>checkGrammarBigIntLiteral</c>.</summary>
    internal bool CheckGrammarBigIntLiteral(BigIntLiteral node)
    {
        var literalType = NodeTests.IsLiteralTypeNode(node.Parent!) ||
            NodeTests.IsPrefixUnaryExpression(node.Parent!) && NodeTests.IsLiteralTypeNode(node.Parent!.Parent!);
        if (!literalType)
        {
            // Don't error on BigInt literals in ambient contexts
            if ((node.Flags & NodeFlags.Ambient) == 0 && _languageVersion < ScriptTarget.ES2020)
            {
                if (GrammarErrorOnNode(node, Diagnostics.BigInt_literals_are_not_available_when_targeting_lower_than_ES2020))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:53569-53577 — <c>grammarErrorAfterFirstToken</c>.</summary>
    internal bool GrammarErrorAfterFirstToken(Node node, DiagnosticMessage message, params object?[] args)
    {
        var sourceFile = Utilities.GetSourceFileOfNode(node)!;
        if (!HasParseDiagnostics(sourceFile))
        {
            var span = Utilities.GetSpanOfTokenAtPosition(sourceFile, node.Pos);
            _diagnostics.Add(Utilities.CreateFileDiagnostic(sourceFile, Core.TextSpanEnd(span), /*length*/ 0, message, args));
            return true;
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:53579-53590 — <c>getAmbientModules</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>ambientModuleSymbolRegex.test(sym)</c> is the port's
    /// <c>AmbientModuleSymbolRegex().IsMatch(sym)</c> — the pattern is a <c>[GeneratedRegex]</c> on this class
    /// (<c>Checker.cs:179</c>) rather than a runtime-parsed literal (§9 rule 7).</remarks>
    internal List<ISymbol> GetAmbientModules()
    {
        if (_ambientModulesCache is null)
        {
            _ambientModulesCache = [];
            _globals.ForEach((ISymbol global, string sym) =>
            {
                // No need to `unescapeLeadingUnderscores`, an escaped symbol is never an ambient module.
                if (AmbientModuleSymbolRegex().IsMatch(sym))
                {
                    _ambientModulesCache.Add(global);
                }
            });
        }
        return _ambientModulesCache;
    }

    /// <summary>ported from: src/compiler/checker.ts:53592-53613 — <c>checkGrammarImportClause</c>.</summary>
    internal bool CheckGrammarImportClause(ImportClause node)
    {
        if (node.PhaseModifier == SyntaxKind.TypeKeyword)
        {
            if (node.Name is not null && node.NamedBindings is not null)
            {
                return GrammarErrorOnNode(node, Diagnostics.A_type_only_import_can_specify_a_default_import_or_named_bindings_but_not_both);
            }
            if (node.NamedBindings?.Kind == SyntaxKind.NamedImports)
            {
                return CheckGrammarNamedImportsOrExports(node.NamedBindings);
            }
        }
        else if (node.PhaseModifier == SyntaxKind.DeferKeyword)
        {
            if (node.Name is not null)
            {
                return GrammarErrorOnNode(node, Diagnostics.Default_imports_are_not_allowed_in_a_deferred_import);
            }
            if (node.NamedBindings?.Kind == SyntaxKind.NamedImports)
            {
                return GrammarErrorOnNode(node, Diagnostics.Named_imports_are_not_allowed_in_a_deferred_import);
            }
            if (_moduleKind != ModuleKind.ESNext && _moduleKind != ModuleKind.Preserve)
            {
                return GrammarErrorOnNode(node, Diagnostics.Deferred_imports_are_only_supported_when_the_module_flag_is_set_to_esnext_or_preserve);
            }
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:53615-53626 — <c>checkGrammarNamedImportsOrExports</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is <c>NamedImportsOrExports</c> and its <c>forEach</c> runs one callback over
    /// the union of the two specifier classes; the port takes <see cref="Node"/> and routes each class to
    /// <see cref="CheckGrammarNamedSpecifier"/>, the body of that callback.</remarks>
    internal bool CheckGrammarNamedImportsOrExports(Node namedBindings)
        => namedBindings switch
        {
            NamedImports imports => Core.Some(imports.Elements, specifier => CheckGrammarNamedSpecifier(specifier.IsTypeOnly, specifier.Kind, specifier)),
            NamedExports exports => Core.Some(exports.Elements, specifier => CheckGrammarNamedSpecifier(specifier.IsTypeOnly, specifier.Kind, specifier)),
            _ => false,
        };

    /// <summary>ported from: src/compiler/checker.ts:53615-53626 — the <c>forEach</c> callback of <c>checkGrammarNamedImportsOrExports</c>.</summary>
    /// <remarks>PORT-NOTE: extracted from the two arms above because the port's <c>Core.ForEach</c> constrains its result
    /// to a reference type and the only element type the two arms share is <see cref="Node"/>.</remarks>
    private bool CheckGrammarNamedSpecifier(bool isTypeOnly, SyntaxKind kind, Node specifier)
    {
        if (isTypeOnly)
        {
            return GrammarErrorOnFirstToken(
                specifier,
                kind == SyntaxKind.ImportSpecifier
                    ? Diagnostics.The_type_modifier_cannot_be_used_on_a_named_import_when_import_type_is_used_on_its_import_statement
                    : Diagnostics.The_type_modifier_cannot_be_used_on_a_named_export_when_export_type_is_used_on_its_export_statement);
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:53628-53668 — <c>checkGrammarImportCallExpression</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>ImportCall</c> is the intersection <c>CallExpression</c> with a keyword/metaproperty
    /// callee; the model has only the <c>SyntaxKind.ImportCall</c> kind, so the port takes the
    /// <see cref="CallExpression"/> the reference's slots live on.</remarks>
    internal bool CheckGrammarImportCallExpression(CallExpression node)
    {
        if (Js.IsTruthy(_compilerOptions.VerbatimModuleSyntax) && _moduleKind == ModuleKind.CommonJS)
        {
            return GrammarErrorOnNode(node, GetVerbatimModuleSyntaxErrorMessage(node));
        }

        if (node.Expression!.Kind == SyntaxKind.MetaProperty)
        {
            if (_moduleKind != ModuleKind.ESNext && _moduleKind != ModuleKind.Preserve)
            {
                return GrammarErrorOnNode(node, Diagnostics.Deferred_imports_are_only_supported_when_the_module_flag_is_set_to_esnext_or_preserve);
            }
        }
        else if (_moduleKind == ModuleKind.ES2015)
        {
            return GrammarErrorOnNode(node, Diagnostics.Dynamic_imports_are_only_supported_when_the_module_flag_is_set_to_es2020_es2022_esnext_commonjs_amd_system_umd_node16_node18_node20_or_nodenext);
        }

        if (node.TypeArguments is not null)
        {
            return GrammarErrorOnNode(node, Diagnostics.This_use_of_import_is_invalid_import_calls_can_be_written_but_they_must_have_parentheses_and_cannot_have_type_arguments);
        }

        var nodeArguments = node.Arguments!;
        if (!(ModuleKind.Node16 <= _moduleKind && _moduleKind <= ModuleKind.NodeNext) && _moduleKind != ModuleKind.ESNext && _moduleKind != ModuleKind.Preserve)
        {
            // We are allowed trailing comma after proposal-import-assertions.
            CheckGrammarForDisallowedTrailingComma(nodeArguments);

            if (nodeArguments.Count > 1)
            {
                var importAttributesArgument = nodeArguments[1];
                return GrammarErrorOnNode(importAttributesArgument, Diagnostics.Dynamic_imports_only_support_a_second_argument_when_the_module_option_is_set_to_esnext_node16_node18_node20_nodenext_or_preserve);
            }
        }

        if (nodeArguments.Count == 0 || nodeArguments.Count > 2)
        {
            return GrammarErrorOnNode(node, Diagnostics.Dynamic_imports_can_only_accept_a_module_specifier_and_an_optional_set_of_attributes_as_arguments);
        }

        // see: parseArgumentOrArrayLiteralElement...we use this function which parse arguments of callExpression to parse specifier for dynamic import.
        // parseArgumentOrArrayLiteralElement allows spread element to be in an argument list which is not allowed as specifier in dynamic import.
        var spreadElement = Core.Find(nodeArguments, (Expression argument, int _) => NodeTests.IsSpreadElement(argument));
        if (spreadElement is not null)
        {
            return GrammarErrorOnNode(spreadElement, Diagnostics.Argument_of_dynamic_import_cannot_be_spread_element);
        }
        return false;
    }
}
