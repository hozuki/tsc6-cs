namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:46751-46828 (checkIndexConstraints … checkIndexConstraintForIndexSignature)
// ported from: src/compiler/checker.ts:46830-46908 (checkTypeNameIsReserved … checkUnmatchedJSDocParameters)
// ported from: src/compiler/checker.ts:46910-47054 (checkTypeParameters … getFirstTransformableStaticClassElement)
// ported from: src/compiler/checker.ts:47056-47090 (checkClassExpressionExternalHelpers … checkClassExpressionDeferred)
// ported from: src/compiler/checker.ts:47091-47225 (checkClassDeclaration, checkClassLikeDeclaration)
// ported from: src/compiler/checker.ts:47227-47757 (the override-modifier and inherited-member block)
// ported from: src/compiler/checker.ts:47759-47815 (the property-initialization block)
// ported from: src/compiler/checker.ts:47817-47883 (checkInterfaceDeclaration, checkTypeAliasDeclaration)
// ported from: src/compiler/checker.ts:47885-48111 (the enum block)
// ported from: src/compiler/checker.ts:48113-48141 (getFirstNonAmbientClassOrFunctionDeclaration, inSameLexicalScope)
// ported from: src/compiler/checker.ts:48143-48312 (checkModuleDeclaration, checkModuleAugmentationElement)
/// <summary>
/// The declaration checks of the checker: what a class, interface, type alias, enum or module declaration has to
/// satisfy, and the two consistency checks the class/interface heritage path needs.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: the reference is one closure and its members read the state TS keeps in <c>var</c>s; the port reads the
/// same slots (<c>_languageVersion</c>, <c>_legacyDecorators</c>, <c>_strictNullChecks</c>,
/// <c>_strictPropertyInitialization</c>, <c>_compilerOptions</c>, <c>_diagnostics</c>) and the four that the M6
/// utilities slice still owes: <c>_moduleKind</c> (checker.ts:1530), <c>_useDefineForClassFields</c> (:1532),
/// <c>_emitStandardClassFields</c> (:1533) and <c>_evaluate</c> (:1553). Each is named in the slice report.
/// </para>
/// <para>
/// PORT-NOTE: TS's <c>LanguageFeatureMinimumTarget</c> (types.ts:8488) is a <c>Record&lt;LanugageFeatures, ScriptTarget&gt;</c>
/// with no port counterpart, so the two comparisons that need it are written against the target the table holds —
/// <c>Classes</c> is <c>ScriptTarget.ES2015</c>, <c>PrivateNamesAndClassStaticBlocks</c> is <c>ScriptTarget.ES2022</c>
/// and <c>ClassAndClassElementDecorators</c> is <c>ScriptTarget.ESNext</c> — the same shape
/// <c>Binder.StrictMode.cs</c> uses for <c>Classes</c>. Each site says which entry it is standing in for.
/// </para>
/// <para>
/// PORT-NOTE: <c>ModuleKind[moduleKind]</c> (:46858) is an enum reverse mapping; the port spells it
/// <see cref="Enum.GetName{TEnum}(TEnum)"/>, as <c>Program.Verify.cs</c> and <c>Utilities.DiagnosticCategoryName</c>
/// already do.
/// </para>
/// <para>
/// PORT-NOTE: <c>evaluatorResult</c> is generic over its value in the reference and TS's <c>EvaluatorResult</c> is that
/// type instantiated with the <c>string | number | undefined</c> union. The port's <see cref="IEvaluatorResult{T}"/> is
/// invariant, so every call in this file names its type argument <c>&lt;object?&gt;</c> — the union's upper bound and
/// the type the generated <c>IEvaluatorResult&lt;object?&gt;</c> slots already use — rather than letting a <c>null</c>
/// argument (for which <c>T</c> cannot be inferred) or a <c>double?</c> decide it.
/// </para>
/// <para>
/// PORT-NOTE: TS's <c>MemberOverrideStatus</c> is a bit-flag enum whose members are combined with <c>|</c> nowhere in
/// this band; the port keeps the generated flags enum and its plain members.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // The index-constraint checks (checker.ts:46751-46828)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:46751-46781 — <c>checkIndexConstraints</c>.</summary>
    internal void CheckIndexConstraints(IType type, ISymbol symbol, bool? isStaticIndex = null)
    {
        var indexInfos = GetIndexInfosOfType(type);
        if (indexInfos.Count == 0)
        {
            return;
        }

        foreach (var prop in GetPropertiesOfObjectType(type))
        {
            if (!(isStaticIndex == true && (prop.Flags & SymbolFlags.Prototype) != 0))
            {
                CheckIndexConstraintForProperty(type, prop, GetLiteralTypeFromProperty(prop, TypeFlags.StringOrNumberLiteralOrUnique, /*includeNonPublic*/ true), GetNonMissingTypeOfSymbol(prop));
            }
        }

        var typeDeclaration = symbol.ValueDeclaration;
        if (typeDeclaration is not null && Utilities.IsClassLike((Node)typeDeclaration))
        {
            foreach (var member in ((IClassLikeDeclarationBase)typeDeclaration).Members!)
            {
                // Only process instance properties against instance index signatures and static properties against static index signatures
                if (
                    (
                        (isStaticIndex != true && !Utilities.IsStatic((Node)member)) ||
                        (isStaticIndex == true && Utilities.IsStatic((Node)member))
                    ) && !HasBindableName((IDeclaration)member))
                {
                    var memberSymbol = GetSymbolOfDeclaration((IDeclaration)member);
                    // PORT-NOTE: TS's `member as DynamicNamedDeclaration` (types.ts:1767) is a view of the member whose
                    // one requirement is `name: ComputedPropertyName` (`hasDynamicName` is what established it), and
                    // the port has no such face on its class elements. The name is therefore read through
                    // `INamedDeclaration` and narrowed by the cast, as `isLateBindableName`'s guard is: a class
                    // element's name is a `PropertyName`, and `ComputedPropertyName` is its only dynamic member (the
                    // `ElementAccessExpression` arm of `isDynamicName` belongs to the expando binary expression, which
                    // is not a class element).
                    CheckIndexConstraintForProperty(type, memberSymbol, GetTypeOfExpression(((ComputedPropertyName)((INamedDeclaration)member).Name!).Expression!), GetNonMissingTypeOfSymbol(memberSymbol));
                }
            }
        }

        if (indexInfos.Count > 1)
        {
            foreach (var info in indexInfos)
            {
                CheckIndexConstraintForIndexSignature(type, info);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:46783-46809 — <c>checkIndexConstraintForProperty</c>.</summary>
    internal void CheckIndexConstraintForProperty(IType type, ISymbol prop, IType propNameType, IType propType)
    {
        var declaration = prop.ValueDeclaration;
        var name = Utilities.GetNameOfDeclaration((Node?)declaration) as Node;
        if (name is not null && NodeTests.IsPrivateIdentifier(name))
        {
            return;
        }
        var indexInfos = GetApplicableIndexInfos(type, propNameType);
        var interfaceDeclaration = (GetObjectFlags(type) & ObjectFlags.Interface) != 0 ? Utilities.GetDeclarationOfKind<InterfaceDeclaration>(type.Symbol!, SyntaxKind.InterfaceDeclaration) : null;
        var propDeclaration = declaration is not null && declaration.Kind == SyntaxKind.BinaryExpression ||
                name is not null && name.Kind == SyntaxKind.ComputedPropertyName
            ? (Node)declaration!
            : null;
        var localPropDeclaration = ReferenceEquals(GetParentOfSymbol(prop), type.Symbol) ? (Node?)declaration : null;
        foreach (var info in indexInfos)
        {
            var localIndexDeclaration = info.Declaration is not null && ReferenceEquals(GetParentOfSymbol(GetSymbolOfDeclaration(info.Declaration)), type.Symbol) ? info.Declaration : null;
            // We check only when (a) the property is declared in the containing type, or (b) the applicable index signature is declared
            // in the containing type, or (c) the containing type is an interface and no base interface contains both the property and
            // the index signature (i.e. property and index signature are declared in separate inherited interfaces).
            var errorNode = localPropDeclaration ?? (Node?)localIndexDeclaration ??
                (interfaceDeclaration is not null && !Core.Some(GetBaseTypes((InterfaceType)type), baseType => GetPropertyOfObjectType(baseType, prop.EscapedName!) is not null && GetIndexTypeOfType(baseType, info.KeyType!) is not null) ? interfaceDeclaration : null);
            if (errorNode is not null && !IsTypeAssignableTo(propType, info.Type))
            {
                var diagnostic = CreateError(errorNode, Diagnostics.Property_0_of_type_1_is_not_assignable_to_2_index_type_3, SymbolToString(prop), TypeToString(propType), TypeToString(info.KeyType!), TypeToString(info.Type!));
                if (propDeclaration is not null && !ReferenceEquals(errorNode, propDeclaration))
                {
                    Utilities.AddRelatedInfo(diagnostic, Utilities.CreateDiagnosticForNode(propDeclaration, Diagnostics._0_is_declared_here, SymbolToString(prop)));
                }
                _diagnostics.Add(diagnostic);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:46811-46828 — <c>checkIndexConstraintForIndexSignature</c>.</summary>
    internal void CheckIndexConstraintForIndexSignature(IType type, IIndexInfo checkInfo)
    {
        var declaration = checkInfo.Declaration;
        var indexInfos = GetApplicableIndexInfos(type, checkInfo.KeyType!);
        var interfaceDeclaration = (GetObjectFlags(type) & ObjectFlags.Interface) != 0 ? Utilities.GetDeclarationOfKind<InterfaceDeclaration>(type.Symbol!, SyntaxKind.InterfaceDeclaration) : null;
        var localCheckDeclaration = declaration is not null && ReferenceEquals(GetParentOfSymbol(GetSymbolOfDeclaration(declaration)), type.Symbol) ? declaration : null;
        foreach (var info in indexInfos)
        {
            if (ReferenceEquals(info, checkInfo)) continue;
            var localIndexDeclaration = info.Declaration is not null && ReferenceEquals(GetParentOfSymbol(GetSymbolOfDeclaration(info.Declaration)), type.Symbol) ? info.Declaration : null;
            // We check only when (a) the check index signature is declared in the containing type, or (b) the applicable index
            // signature is declared in the containing type, or (c) the containing type is an interface and no base interface contains
            // both index signatures (i.e. the index signatures are declared in separate inherited interfaces).
            var errorNode = localCheckDeclaration ?? (Node?)localIndexDeclaration ??
                (interfaceDeclaration is not null && !Core.Some(GetBaseTypes((InterfaceType)type), baseType => GetIndexInfoOfType(baseType, checkInfo.KeyType!) is not null && GetIndexTypeOfType(baseType, info.KeyType!) is not null) ? interfaceDeclaration : null);
            if (errorNode is not null && !IsTypeAssignableTo(checkInfo.Type, info.Type))
            {
                Error(errorNode, Diagnostics._0_index_type_1_is_not_assignable_to_2_index_type_3, TypeToString(checkInfo.KeyType!), TypeToString(checkInfo.Type!), TypeToString(info.KeyType!), TypeToString(info.Type!));
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The reserved names and the JSDoc parameter check (checker.ts:46830-46908)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:46830-46850 — <c>checkTypeNameIsReserved</c>.</summary>
    internal void CheckTypeNameIsReserved(Identifier name, DiagnosticMessage message)
    {
        // TS 1.0 spec (April 2014): 3.6.1
        // The predefined type keywords are reserved and cannot be used as names of user defined types.
        switch (name.EscapedText)
        {
            case "any":
            case "unknown":
            case "never":
            case "number":
            case "bigint":
            case "boolean":
            case "string":
            case "symbol":
            case "void":
            case "object":
            case "undefined":
                Error(name, message, name.EscapedText);
                break;
        }
    }

    /// <summary>
    /// The name cannot be used as 'Object' of user defined types with special target.
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:46852-46859 — <c>checkClassNameCollisionWithObject</c>.</remarks>
    internal void CheckClassNameCollisionWithObject(Identifier name)
    {
        if (
            name.EscapedText == "Object"
            && _host.GetEmitModuleFormatOfFile(Utilities.GetSourceFileOfNode(name)) < ModuleKind.ES2015
        )
        {
            Error(name, Diagnostics.Class_name_cannot_be_Object_when_targeting_ES5_and_above_with_module_0, Enum.GetName(_moduleKind)); // https://github.com/Microsoft/TypeScript/issues/17494
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:46861-46908 — <c>checkUnmatchedJSDocParameters</c>.</summary>
    internal void CheckUnmatchedJSDocParameters(ISignatureDeclarationBase node)
    {
        var jsdocParameters = Core.Map(Core.Filter(Utilities.GetJSDocTags((Node)node), NodeTests.IsJSDocParameterTag), (tag, _) => (JSDocParameterTag)tag)!;
        if (Core.Length(jsdocParameters) == 0) return;

        var isJs = Utilities.IsInJSFile((Node)node);
        var parameters = new HashSet<string>();
        var excludedParameters = new HashSet<int>();
        Core.ForEach(node.Parameters, (ParameterDeclaration parameter, int index) =>
        {
            if (parameter.Name is Identifier parameterName)
            {
                parameters.Add(parameterName.EscapedText!);
            }
            if (Utilities.IsBindingPattern(parameter.Name))
            {
                excludedParameters.Add(index);
            }
            return (object?)null;
        });

        var containsArguments = ContainsArgumentsReference((Node)node);
        if (containsArguments)
        {
            var lastJSDocParamIndex = jsdocParameters.Count - 1;
            var lastJSDocParam = jsdocParameters[lastJSDocParamIndex];
            if (
                isJs && lastJSDocParam is not null && lastJSDocParam.Name is Identifier lastJSDocParamName && lastJSDocParam.TypeExpression is not null &&
                lastJSDocParam.TypeExpression.Type is not null && !parameters.Contains(lastJSDocParamName.EscapedText!) && !excludedParameters.Contains(lastJSDocParamIndex) && !IsArrayType(GetTypeFromTypeNode(lastJSDocParam.TypeExpression.Type))
            )
            {
                Error(lastJSDocParamName, Diagnostics.JSDoc_param_tag_has_name_0_but_there_is_no_parameter_with_that_name_It_would_match_arguments_if_it_had_an_array_type, Utilities.IdText(lastJSDocParamName));
            }
        }
        else
        {
            Core.ForEach(jsdocParameters, (JSDocParameterTag tag, int index) =>
            {
                if (excludedParameters.Contains(index) || tag.Name is Identifier tagName && parameters.Contains(tagName.EscapedText!))
                {
                    return (object?)null;
                }
                if (NodeTests.IsQualifiedName(tag.Name!))
                {
                    if (isJs)
                    {
                        Error(tag.Name, Diagnostics.Qualified_name_0_is_not_allowed_without_a_leading_param_object_1, Utilities.EntityNameToString(tag.Name!), Utilities.EntityNameToString(((QualifiedName)tag.Name!).Left!));
                    }
                }
                else
                {
                    if (!tag.IsNameFirst)
                    {
                        ErrorOrSuggestion(isJs, tag.Name!, Diagnostics.JSDoc_param_tag_has_name_0_but_there_is_no_parameter_with_that_name, Utilities.IdText(tag.Name!));
                    }
                }
                return (object?)null;
            });
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The type-parameter checks (checker.ts:46910-47054)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Check each type parameter and check that type parameters have no duplicate type parameter declarations
    /// </summary>
    /// <remarks>
    /// ported from: src/compiler/checker.ts:46910-46937 — <c>checkTypeParameters</c>.
    /// <para>
    /// PORT-NOTE: TS declares <c>createCheckTypeParameterDiagnostic</c> after the loop that calls it; a C# local
    /// function is visible throughout its block, so the port keeps the reference's own order of the two.
    /// </para>
    /// </remarks>
    internal void CheckTypeParameters(IList<TypeParameterDeclaration>? typeParameterDeclarations)
    {
        var seenDefault = false;
        if (typeParameterDeclarations is not null)
        {
            for (var i = 0; i < typeParameterDeclarations.Count; i++)
            {
                var node = typeParameterDeclarations[i];
                CheckTypeParameter(node);

                AddLazyDiagnostic(CreateCheckTypeParameterDiagnostic(node, i));
            }
        }

        Action CreateCheckTypeParameterDiagnostic(TypeParameterDeclaration node, int i)
        {
            return () =>
            {
                if (node.Default is not null)
                {
                    seenDefault = true;
                    CheckTypeParametersNotReferenced(node.Default, typeParameterDeclarations!, i);
                }
                else if (seenDefault)
                {
                    Error(node, Diagnostics.Required_type_parameters_may_not_follow_optional_type_parameters);
                }
                for (var j = 0; j < i; j++)
                {
                    if (ReferenceEquals(typeParameterDeclarations![j].Symbol, node.Symbol))
                    {
                        Error(node.Name!, Diagnostics.Duplicate_identifier_0, Utilities.DeclarationNameToString(node.Name));
                    }
                }
            };
        }
    }

    /// <summary>Check that type parameter defaults only reference previously declared type parameters</summary>
    /// <remarks>ported from: src/compiler/checker.ts:46940-46956 — <c>checkTypeParametersNotReferenced</c>.</remarks>
    internal void CheckTypeParametersNotReferenced(ITypeNode root, IList<TypeParameterDeclaration> typeParameters, int index)
    {
        Visit((Node)root);
        void Visit(Node node)
        {
            if (node.Kind == SyntaxKind.TypeReference)
            {
                var type = GetTypeFromTypeReference(node);
                if ((type.Flags & TypeFlags.TypeParameter) != 0)
                {
                    for (var i = index; i < typeParameters.Count; i++)
                    {
                        if (ReferenceEquals(type.Symbol, GetSymbolOfDeclaration(typeParameters[i])))
                        {
                            Error(node, Diagnostics.Type_parameter_defaults_can_only_reference_previously_declared_type_parameters);
                        }
                    }
                }
            }
            ForEachChildTable.ForEachChild(node, Visit);
        }
    }

    /// <summary>Check that type parameter lists are identical across multiple declarations</summary>
    /// <remarks>ported from: src/compiler/checker.ts:46958-46980 — <c>checkTypeParameterListsIdentical</c>.</remarks>
    internal void CheckTypeParameterListsIdentical(ISymbol symbol)
    {
        if (symbol.Declarations is not null && symbol.Declarations.Count == 1)
        {
            return;
        }

        var links = GetSymbolLinks(symbol);
        if (links.TypeParametersChecked != true)
        {
            links.TypeParametersChecked = true;
            var declarations = GetClassOrInterfaceDeclarationsOfSymbol(symbol);
            if (declarations is null || declarations.Count <= 1)
            {
                return;
            }

            var type = (InterfaceType)GetDeclaredTypeOfSymbol(symbol);
            if (!AreTypeParametersIdentical(declarations, type.LocalTypeParameters!, Utilities.GetEffectiveTypeParameterDeclarations))
            {
                // Report an error on every conflicting declaration.
                var name = SymbolToString(symbol);
                foreach (var declaration in declarations)
                {
                    Error(((INamedDeclaration)declaration).Name, Diagnostics.All_declarations_of_0_must_have_identical_type_parameters, name);
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:46982-47026 — <c>areTypeParametersIdentical</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>T extends DeclarationWithTypeParameters | TypeParameterDeclaration</c> has no port
    /// counterpart (there is no <c>DeclarationWithTypeParameters</c> interface), and the body reads nothing off
    /// <typeparamref name="T"/> — the only use of a declaration is the caller-supplied accessor — so the port's
    /// constraint is <c>class</c> and both the reference's callers fit it: this band passes the class/interface
    /// declarations, and <c>checkInferType</c> (checker.ts:43010) passes type-parameter declarations.
    /// </remarks>
    internal bool AreTypeParametersIdentical<T>(IList<T> declarations, List<ITypeParameter> targetParameters, Func<T, List<TypeParameterDeclaration>> getTypeParameterDeclarations)
        where T : class
    {
        var maxTypeArgumentCount = Core.Length(targetParameters);
        var minTypeArgumentCount = GetMinTypeArgumentCount(targetParameters);

        foreach (var declaration in declarations)
        {
            // If this declaration has too few or too many type parameters, we report an error
            var sourceParameters = getTypeParameterDeclarations(declaration);
            var numTypeParameters = sourceParameters.Count;
            if (numTypeParameters < minTypeArgumentCount || numTypeParameters > maxTypeArgumentCount)
            {
                return false;
            }

            for (var i = 0; i < numTypeParameters; i++)
            {
                var source = sourceParameters[i];
                var target = targetParameters[i];

                // If the type parameter node does not have the same as the resolved type
                // parameter at this position, we report an error.
                if (source.Name!.EscapedText != target.Symbol!.EscapedName)
                {
                    return false;
                }

                // If the type parameter node does not have an identical constraint as the resolved
                // type parameter at this position, we report an error.
                var constraint = Utilities.GetEffectiveConstraintOfTypeParameter(source);
                var sourceConstraint = constraint is not null ? GetTypeFromTypeNode(constraint) : null;
                var targetConstraint = GetConstraintOfTypeParameter(target);
                // relax check if later interface augmentation has no constraint, it's more broad and is OK to merge with
                // a more constrained interface (this could be generalized to a full hierarchy check, but that's maybe overkill)
                if (sourceConstraint is not null && targetConstraint is not null && !IsTypeIdenticalTo(sourceConstraint, targetConstraint))
                {
                    return false;
                }

                // If the type parameter node has a default and it is not identical to the default
                // for the type parameter at this position, we report an error.
                var sourceDefault = source.Default is not null ? GetTypeFromTypeNode(source.Default) : null;
                var targetDefault = GetDefaultFromTypeParameter(target);
                if (sourceDefault is not null && targetDefault is not null && !IsTypeIdenticalTo(sourceDefault, targetDefault))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>ported from: src/compiler/checker.ts:47028-47054 — <c>getFirstTransformableStaticClassElement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>LanguageFeatureMinimumTarget.ClassAndClassElementDecorators</c> is <c>ScriptTarget.ESNext</c> and
    /// <c>LanguageFeatureMinimumTarget.PrivateNamesAndClassStaticBlocks</c> is <c>ScriptTarget.ES2022</c>
    /// (types.ts:8517/8522); the table itself is not ported (see the class remarks).
    /// </remarks>
    internal Node? GetFirstTransformableStaticClassElement(IClassLikeDeclarationBase node)
    {
        var willTransformStaticElementsOfDecoratedClass = !_legacyDecorators && _languageVersion < ScriptTarget.ESNext &&
            Utilities.ClassOrConstructorParameterIsDecorated(/*useLegacyDecorators*/ false, (Node)node);
        var willTransformPrivateElementsOrClassStaticBlocks = _languageVersion < ScriptTarget.ES2022 ||
            _languageVersion < ScriptTarget.ESNext;
        var willTransformInitializers = !_emitStandardClassFields;
        if (willTransformStaticElementsOfDecoratedClass || willTransformPrivateElementsOrClassStaticBlocks)
        {
            foreach (var member in node.Members!)
            {
                if (willTransformStaticElementsOfDecoratedClass && Utilities.ClassElementOrClassElementParameterIsDecorated(/*useLegacyDecorators*/ false, (Node)member, (Node)node))
                {
                    return (Node?)Core.FirstOrUndefined(Utilities.GetDecorators((Node)node)) ?? (Node)node;
                }
                else if (willTransformPrivateElementsOrClassStaticBlocks)
                {
                    if (NodeTests.IsClassStaticBlockDeclaration((Node)member))
                    {
                        return (Node)member;
                    }
                    else if (Utilities.IsStatic((Node)member))
                    {
                        if (
                            Utilities.IsPrivateIdentifierClassElementDeclaration((Node)member) ||
                            willTransformInitializers && Utilities.IsInitializedProperty((Node)member)
                        )
                        {
                            return (Node)member;
                        }
                    }
                }
            }
        }

        return null;
    }

    // ---------------------------------------------------------------------------------------------
    // The class-expression and class-declaration checks (checker.ts:47056-47225)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:47056-47076 — <c>checkClassExpressionExternalHelpers</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>LanguageFeatureMinimumTarget.ClassAndClassElementDecorators</c> is <c>ScriptTarget.ESNext</c>
    /// (types.ts:8522); the table itself is not ported (see the class remarks).
    /// </remarks>
    internal void CheckClassExpressionExternalHelpers(ClassExpression node)
    {
        if (node.Name is not null) return;

        var parent = FactoryUtilities.WalkUpOuterExpressions(node);
        if (parent is null || !Utilities.IsNamedEvaluationSource(parent)) return;

        var willTransformESDecorators = !_legacyDecorators && _languageVersion < ScriptTarget.ESNext;
        Node? location;
        if (willTransformESDecorators && Utilities.ClassOrConstructorParameterIsDecorated(/*useLegacyDecorators*/ false, node))
        {
            location = (Node?)Core.FirstOrUndefined(Utilities.GetDecorators(node)) ?? node;
        }
        else
        {
            location = GetFirstTransformableStaticClassElement(node);
        }

        if (location is not null)
        {
            CheckExternalEmitHelpers(location, ExternalEmitHelpers.SetFunctionName);
            if ((NodeTests.IsPropertyAssignment(parent!) || NodeTests.IsPropertyDeclaration(parent!) || NodeTests.IsBindingElement(parent!)) && NodeTests.IsComputedPropertyName(((INamedDeclaration)parent!).Name!))
            {
                CheckExternalEmitHelpers(location, ExternalEmitHelpers.PropKey);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:47079-47084 — <c>checkClassExpression</c>.</summary>
    internal IType CheckClassExpression(ClassExpression node)
    {
        CheckClassLikeDeclaration(node);
        CheckNodeDeferred(node);
        CheckClassExpressionExternalHelpers(node);
        return GetTypeOfSymbol(GetSymbolOfDeclaration(node));
    }

    /// <summary>ported from: src/compiler/checker.ts:47086-47089 — <c>checkClassExpressionDeferred</c>.</summary>
    internal void CheckClassExpressionDeferred(ClassExpression node)
    {
        Core.ForEach<IClassElement, object>(node.Members, (member, _) =>
        {
            CheckSourceElement((Node)member);
            return null;
        });
        RegisterForUnusedIdentifiersCheck(node);
    }

    /// <summary>ported from: src/compiler/checker.ts:47091-47103 — <c>checkClassDeclaration</c>.</summary>
    internal void CheckClassDeclaration(ClassDeclaration node)
    {
        var firstDecorator = Core.Find(node.Modifiers, (Node modifier, int _) => NodeTests.IsDecorator(modifier));
        if (_legacyDecorators && firstDecorator is not null && Core.Some(node.Members, p => Utilities.HasStaticModifier((Node)p!) && Utilities.IsPrivateIdentifierClassElementDeclaration((Node)p)))
        {
            GrammarErrorOnNode(firstDecorator, Diagnostics.Class_decorators_can_t_be_used_with_static_private_identifier_Consider_removing_the_experimental_decorator);
        }
        if (node.Name is null && !Utilities.HasSyntacticModifier(node, ModifierFlags.Default))
        {
            GrammarErrorOnFirstToken(node, Diagnostics.A_class_declaration_without_the_default_modifier_must_have_a_name);
        }
        CheckClassLikeDeclaration(node);
        Core.ForEach<IClassElement, object>(node.Members, (member, _) =>
        {
            CheckSourceElement((Node)member);
            return null;
        });

        RegisterForUnusedIdentifiersCheck(node);
    }

    /// <summary>ported from: src/compiler/checker.ts:47105-47225 — <c>checkClassLikeDeclaration</c>.</summary>
    internal void CheckClassLikeDeclaration(IClassLikeDeclarationBase node)
    {
        CheckGrammarClassLikeDeclaration((Node)node);
        CheckDecorators((Node)node);
        CheckCollisionsForDeclarationName((Node)node, ((INamedDeclaration)node).Name as Identifier);
        CheckTypeParameters(Utilities.GetEffectiveTypeParameterDeclarations((Node)node));
        CheckExportsOnMergedDeclarations((IDeclaration)node);
        var symbol = GetSymbolOfDeclaration((IDeclaration)node);
        var type = (InterfaceType)GetDeclaredTypeOfSymbol(symbol);
        var typeWithThis = GetTypeWithThisArgument(type);
        // PORT-NOTE: TS's `getTypeOfSymbol(symbol) as ObjectType` (types.ts:6600) views the static side through the
        // `ObjectType` face — which an intersection type satisfies structurally, `objectFlags` included, since TS 6.0
        // declares that slot on `UnionOrIntersectionType` (types.ts:6726) — but the port models `ObjectType` as a
        // concrete class that is a *sibling* of `IntersectionType` under `UnionOrIntersectionType` (a mixin class
        // expression's static side is exactly such an intersection). `IObjectType` is the face both implement, and the
        // value the reference observes is unchanged.
        var staticType = (IObjectType)GetTypeOfSymbol(symbol);
        CheckTypeParameterListsIdentical(symbol);
        CheckFunctionOrConstructorSymbol(symbol);
        CheckClassForDuplicateDeclarations((Node)node);

        // Only check for reserved static identifiers on non-ambient context.
        var nodeInAmbientContext = (((Node)node).Flags & NodeFlags.Ambient) != 0;
        if (!nodeInAmbientContext)
        {
            CheckClassForStaticPropertyNameConflicts((Node)node);
        }

        var baseTypeNode = Utilities.GetEffectiveBaseTypeNode((Node)node);
        if (baseTypeNode is not null)
        {
            Core.ForEach<ITypeNode, object>(baseTypeNode.TypeArguments, (typeArgument, _) =>
            {
                CheckSourceElement((Node)typeArgument);
                return null;
            });
            // PORT-NOTE: LanguageFeatureMinimumTarget.Classes is ScriptTarget.ES2015 (types.ts:8489).
            if (_languageVersion < ScriptTarget.ES2015)
            {
                CheckExternalEmitHelpers(baseTypeNode.Parent!, ExternalEmitHelpers.Extends);
            }
            // check both @extends and extends if both are specified.
            var extendsNode = Utilities.GetClassExtendsHeritageElement((Node)node);
            if (extendsNode is not null && !ReferenceEquals(extendsNode, baseTypeNode))
            {
                CheckExpression(extendsNode.Expression!);
            }

            var baseTypes = GetBaseTypes(type);
            if (baseTypes.Count != 0)
            {
                AddLazyDiagnostic(() =>
                {
                    var baseType = baseTypes[0];
                    var baseConstructorType = GetBaseConstructorTypeOfClass(type);
                    var staticBaseType = GetApparentType(baseConstructorType);
                    CheckBaseTypeAccessibility(staticBaseType, baseTypeNode);
                    CheckSourceElement(baseTypeNode.Expression);
                    if (Core.Some(baseTypeNode.TypeArguments))
                    {
                        Core.ForEach<ITypeNode, object>(baseTypeNode.TypeArguments, (typeArgument, _) =>
                        {
                            CheckSourceElement((Node)typeArgument);
                            return null;
                        });
                        foreach (var constructor in GetConstructorsForTypeArguments(staticBaseType, baseTypeNode.TypeArguments, baseTypeNode))
                        {
                            if (!CheckTypeArgumentConstraints(baseTypeNode, constructor.TypeParameters!))
                            {
                                break;
                            }
                        }
                    }
                    var baseWithThis = GetTypeWithThisArgument(baseType, type.ThisType!);
                    if (!CheckTypeAssignableTo(typeWithThis, baseWithThis, /*errorNode*/ null))
                    {
                        IssueMemberSpecificError(node, typeWithThis, baseWithThis, Diagnostics.Class_0_incorrectly_extends_base_class_1);
                    }
                    else
                    {
                        // Report static side error only when instance type is assignable
                        CheckTypeAssignableTo(staticType, GetTypeWithoutSignatures(staticBaseType), ((INamedDeclaration)node).Name ?? (Node)node, Diagnostics.Class_static_side_0_incorrectly_extends_base_class_static_side_1);
                    }
                    if ((baseConstructorType.Flags & TypeFlags.TypeVariable) != 0)
                    {
                        if (!IsMixinConstructorType(staticType))
                        {
                            Error(((INamedDeclaration)node).Name ?? (Node)node, Diagnostics.A_mixin_class_must_have_a_constructor_with_a_single_rest_parameter_of_type_any);
                        }
                        else
                        {
                            var constructSignatures = GetSignaturesOfType(baseConstructorType, SignatureKind.Construct);
                            if (Core.Some(constructSignatures, signature => (signature.Flags & SignatureFlags.Abstract) != 0) && !Utilities.HasSyntacticModifier((Node)node, ModifierFlags.Abstract))
                            {
                                Error(((INamedDeclaration)node).Name ?? (Node)node, Diagnostics.A_mixin_class_that_extends_from_a_type_variable_containing_an_abstract_construct_signature_must_also_be_declared_abstract);
                            }
                        }
                    }

                    if (!(staticBaseType.Symbol is not null && (staticBaseType.Symbol.Flags & SymbolFlags.Class) != 0) && (baseConstructorType.Flags & TypeFlags.TypeVariable) == 0)
                    {
                        // When the static base type is a "class-like" constructor function (but not actually a class), we verify
                        // that all instantiated base constructor signatures return the same type.
                        var constructors = GetInstantiatedConstructorsForTypeArguments(staticBaseType, baseTypeNode.TypeArguments, baseTypeNode);
                        if (Core.Some(constructors, sig => !IsJSConstructor(sig.Declaration!) && !IsTypeIdenticalTo(GetReturnTypeOfSignature(sig), baseType)))
                        {
                            Error(baseTypeNode.Expression, Diagnostics.Base_constructors_must_all_have_the_same_return_type);
                        }
                    }
                    CheckKindsOfPropertyMemberOverrides(type, baseType);
                });
            }
        }

        CheckMembersForOverrideModifier(node, type, typeWithThis, staticType);

        var implementedTypeNodes = Utilities.GetEffectiveImplementsTypeNodes(node);
        if (implementedTypeNodes is not null)
        {
            foreach (var typeRefNode in implementedTypeNodes)
            {
                if (!Utilities.IsEntityNameExpression((Node)typeRefNode.Expression!) || Utilities.IsOptionalChain((Node)typeRefNode.Expression!))
                {
                    Error((Node?)typeRefNode.Expression, Diagnostics.A_class_can_only_implement_an_identifier_Slashqualified_name_with_optional_type_arguments);
                }
                CheckTypeReferenceNode(typeRefNode);
                AddLazyDiagnostic(CreateImplementsDiagnostics(typeRefNode));
            }
        }

        AddLazyDiagnostic(() =>
        {
            CheckIndexConstraints(type, symbol);
            CheckIndexConstraints(staticType, symbol, /*isStaticIndex*/ true);
            CheckTypeForDuplicateIndexSignatures((Node)node);
            CheckPropertyInitialization(node);
        });

        Action CreateImplementsDiagnostics(ExpressionWithTypeArguments typeRefNode)
        {
            return () =>
            {
                var t = GetReducedType(GetTypeFromTypeNode(typeRefNode));
                if (!IsErrorType(t))
                {
                    if (IsValidBaseType(t))
                    {
                        var genericDiag = t.Symbol is not null && (t.Symbol.Flags & SymbolFlags.Class) != 0 ?
                            Diagnostics.Class_0_incorrectly_implements_class_1_Did_you_mean_to_extend_1_and_inherit_its_members_as_a_subclass :
                            Diagnostics.Class_0_incorrectly_implements_interface_1;
                        var baseWithThis = GetTypeWithThisArgument(t, type.ThisType!);
                        if (!CheckTypeAssignableTo(typeWithThis, baseWithThis, /*errorNode*/ null))
                        {
                            IssueMemberSpecificError(node, typeWithThis, baseWithThis, genericDiag);
                        }
                    }
                    else
                    {
                        Error((Node?)typeRefNode, Diagnostics.A_class_can_only_implement_an_object_type_or_intersection_of_object_types_with_statically_known_members);
                    }
                }
            };
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The `override` modifier checks (checker.ts:47227-47495)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:47227-47269 — <c>checkMembersForOverrideModifier</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>staticType: ObjectType</c> is <c>IObjectType</c> here — the static side of a mixin class
    /// expression is an intersection type, which the port models outside the <c>ObjectType</c> class (see
    /// <see cref="CheckClassLikeDeclaration"/>).
    /// </remarks>
    internal void CheckMembersForOverrideModifier(IClassLikeDeclarationBase node, InterfaceType type, IType typeWithThis, IObjectType staticType)
    {
        var baseTypeNode = Utilities.GetEffectiveBaseTypeNode((Node)node);
        var baseTypes = baseTypeNode is not null ? GetBaseTypes(type) : null;
        var baseWithThis = baseTypes is { Count: > 0 } ? GetTypeWithThisArgument(Core.First(baseTypes), type.ThisType!) : null;
        var baseStaticType = GetBaseConstructorTypeOfClass(type);

        foreach (var member in node.Members!)
        {
            if (Utilities.HasAmbientModifier((Node)member))
            {
                continue;
            }

            if (NodeTests.IsConstructorDeclaration((Node)member))
            {
                Core.ForEach(((ConstructorDeclaration)member).Parameters, (ParameterDeclaration param, int _) =>
                {
                    if (Utilities.IsParameterPropertyDeclaration(param, (Node)member))
                    {
                        CheckExistingMemberForOverrideModifier(
                            node,
                            staticType,
                            baseStaticType,
                            baseWithThis,
                            type,
                            typeWithThis,
                            param,
                            /*memberIsParameterProperty*/ true);
                    }
                    return (object?)null;
                });
            }
            CheckExistingMemberForOverrideModifier(
                node,
                staticType,
                baseStaticType,
                baseWithThis,
                type,
                typeWithThis,
                (Node)member,
                /*memberIsParameterProperty*/ false);
        }
    }

    /// <summary>
    /// <c>member</c> is an existing member node to be checked; it cannot be a synthetic node.
    /// </summary>
    /// <remarks>
    /// ported from: src/compiler/checker.ts:47271-47303 — <c>checkExistingMemberForOverrideModifier</c>.
    /// <para>
    /// PORT-NOTE: TS's <c>staticType: ObjectType</c> is <c>IObjectType</c>, as in
    /// <see cref="CheckMembersForOverrideModifier"/>.
    /// </para>
    /// </remarks>
    internal MemberOverrideStatus CheckExistingMemberForOverrideModifier(
        IClassLikeDeclarationBase node,
        IObjectType staticType,
        IType baseStaticType,
        IType? baseWithThis,
        InterfaceType type,
        IType typeWithThis,
        Node member,
        bool memberIsParameterProperty,
        bool reportErrors = true)
    {
        var memberName = ((INamedDeclaration)member).Name;
        var declaredProp = (memberName is not null ? GetSymbolAtLocation(memberName) : null) ?? GetSymbolAtLocation(member);
        if (declaredProp is null)
        {
            return MemberOverrideStatus.Ok;
        }

        return CheckMemberForOverrideModifier(
            node,
            staticType,
            baseStaticType,
            baseWithThis,
            type,
            typeWithThis,
            Utilities.HasOverrideModifier(member),
            Utilities.HasAbstractModifier(member),
            Utilities.IsStatic(member),
            memberIsParameterProperty,
            declaredProp,
            reportErrors ? member : null);
    }

    /// <summary>
    /// Checks a class member declaration for either a missing or an invalid <c>override</c> modifier.
    /// Note: this function can be used for speculative checking, i.e. checking a member that does not yet exist in the
    /// program. An example of that would be to call this function in a completions scenario, when offering a method
    /// declaration as completion.
    /// </summary>
    /// <remarks>
    /// ported from: src/compiler/checker.ts:47313-47409 — <c>checkMemberForOverrideModifier</c>.
    /// <para>
    /// PORT-NOTE: TS's <c>staticType: ObjectType</c> is <c>IObjectType</c>, as in
    /// <see cref="CheckMembersForOverrideModifier"/>.
    /// </para>
    /// </remarks>
    internal MemberOverrideStatus CheckMemberForOverrideModifier(
        IClassLikeDeclarationBase node,
        IObjectType staticType,
        IType baseStaticType,
        IType? baseWithThis,
        InterfaceType type,
        IType typeWithThis,
        bool memberHasOverrideModifier,
        bool memberHasAbstractModifier,
        bool memberIsStatic,
        bool memberIsParameterProperty,
        ISymbol member,
        Node? errorNode = null)
    {
        var isJs = Utilities.IsInJSFile((Node)node);
        var nodeInAmbientContext = (((Node)node).Flags & NodeFlags.Ambient) != 0;
        if (memberHasOverrideModifier && member.ValueDeclaration is not null && Utilities.IsClassElement((Node)member.ValueDeclaration) && ((INamedDeclaration)member.ValueDeclaration).Name is not null && IsNonBindableDynamicName((Node)((INamedDeclaration)member.ValueDeclaration).Name!))
        {
            Error(
                errorNode,
                isJs ?
                    Diagnostics.This_member_cannot_have_a_JSDoc_comment_with_an_override_tag_because_its_name_is_dynamic :
                    Diagnostics.This_member_cannot_have_an_override_modifier_because_its_name_is_dynamic);
            return MemberOverrideStatus.HasInvalidOverride;
        }
        if (baseWithThis is not null && (memberHasOverrideModifier || Js.IsTruthy(_compilerOptions.NoImplicitOverride)))
        {
            var thisType = memberIsStatic ? staticType : typeWithThis;
            var baseType = memberIsStatic ? baseStaticType : baseWithThis;
            var prop = GetPropertyOfType(thisType, member.EscapedName!);
            var baseProp = GetPropertyOfType(baseType, member.EscapedName!);

            var baseClassName = TypeToString(baseWithThis);
            if (prop is not null && baseProp is null && memberHasOverrideModifier)
            {
                if (errorNode is not null)
                {
                    // Again, using symbol name: note that's different from `symbol.escapedName`
                    var suggestion = GetSuggestedSymbolForNonexistentClassMember(Utilities.SymbolName(member), baseType);
                    if (suggestion is not null)
                    {
                        Error(
                            errorNode,
                            isJs ?
                                Diagnostics.This_member_cannot_have_a_JSDoc_comment_with_an_override_tag_because_it_is_not_declared_in_the_base_class_0_Did_you_mean_1 :
                                Diagnostics.This_member_cannot_have_an_override_modifier_because_it_is_not_declared_in_the_base_class_0_Did_you_mean_1,
                            baseClassName,
                            SymbolToString(suggestion));
                    }
                    else
                    {
                        Error(
                            errorNode,
                            isJs ?
                                Diagnostics.This_member_cannot_have_a_JSDoc_comment_with_an_override_tag_because_it_is_not_declared_in_the_base_class_0 :
                                Diagnostics.This_member_cannot_have_an_override_modifier_because_it_is_not_declared_in_the_base_class_0,
                            baseClassName);
                    }
                }
                return MemberOverrideStatus.HasInvalidOverride;
            }
            else if (prop is not null && baseProp?.Declarations is not null && Js.IsTruthy(_compilerOptions.NoImplicitOverride) && !nodeInAmbientContext)
            {
                var baseHasAbstract = Core.Some(baseProp.Declarations, declaration => Utilities.HasAbstractModifier((Node)declaration));
                if (memberHasOverrideModifier)
                {
                    return MemberOverrideStatus.Ok;
                }

                if (!baseHasAbstract)
                {
                    if (errorNode is not null)
                    {
                        var diag = memberIsParameterProperty ?
                            isJs ?
                                Diagnostics.This_parameter_property_must_have_a_JSDoc_comment_with_an_override_tag_because_it_overrides_a_member_in_the_base_class_0 :
                                Diagnostics.This_parameter_property_must_have_an_override_modifier_because_it_overrides_a_member_in_base_class_0 :
                            isJs ?
                                Diagnostics.This_member_must_have_a_JSDoc_comment_with_an_override_tag_because_it_overrides_a_member_in_the_base_class_0 :
                                Diagnostics.This_member_must_have_an_override_modifier_because_it_overrides_a_member_in_the_base_class_0;
                        Error(errorNode, diag, baseClassName);
                    }
                    return MemberOverrideStatus.NeedsOverride;
                }
                else if (memberHasAbstractModifier && baseHasAbstract)
                {
                    if (errorNode is not null)
                    {
                        Error(errorNode, Diagnostics.This_member_must_have_an_override_modifier_because_it_overrides_an_abstract_method_that_is_declared_in_the_base_class_0, baseClassName);
                    }
                    return MemberOverrideStatus.NeedsOverride;
                }
            }
        }
        else if (memberHasOverrideModifier)
        {
            if (errorNode is not null)
            {
                var className = TypeToString(type);
                Error(
                    errorNode,
                    isJs ?
                        Diagnostics.This_member_cannot_have_a_JSDoc_comment_with_an_override_tag_because_its_containing_class_0_does_not_extend_another_class :
                        Diagnostics.This_member_cannot_have_an_override_modifier_because_its_containing_class_0_does_not_extend_another_class,
                    className);
            }
            return MemberOverrideStatus.HasInvalidOverride;
        }

        return MemberOverrideStatus.Ok;
    }

    /// <summary>ported from: src/compiler/checker.ts:47411-47441 — <c>issueMemberSpecificError</c>.</summary>
    internal void IssueMemberSpecificError(IClassLikeDeclarationBase node, IType typeWithThis, IType baseWithThis, DiagnosticMessage broadDiag)
    {
        // iterate over all implemented properties and issue errors on each one which isn't compatible, rather than the class as a whole, if possible
        var issuedMemberError = false;
        foreach (var member in node.Members!)
        {
            if (Utilities.IsStatic((Node)member))
            {
                continue;
            }
            var memberName = ((INamedDeclaration)member).Name;
            var declaredProp = (memberName is not null ? GetSymbolAtLocation(memberName) : null) ?? GetSymbolAtLocation((Node)member);
            if (declaredProp is not null)
            {
                var prop = GetPropertyOfType(typeWithThis, declaredProp.EscapedName!);
                var baseProp = GetPropertyOfType(baseWithThis, declaredProp.EscapedName!);
                if (prop is not null && baseProp is not null)
                {
                    Func<IDiagnosticMessageChain?> rootChain = () =>
                        Utilities.ChainDiagnosticMessages(
                            /*details*/ null,
                            Diagnostics.Property_0_in_type_1_is_not_assignable_to_the_same_property_in_base_type_2,
                            SymbolToString(declaredProp),
                            TypeToString(typeWithThis),
                            TypeToString(baseWithThis));
                    if (!CheckTypeAssignableTo(GetTypeOfSymbol(prop), GetTypeOfSymbol(baseProp), memberName ?? (Node)member, /*headMessage*/ null, rootChain))
                    {
                        issuedMemberError = true;
                    }
                }
            }
        }
        if (!issuedMemberError)
        {
            // check again with diagnostics to generate a less-specific error
            CheckTypeAssignableTo(typeWithThis, baseWithThis, ((INamedDeclaration)node).Name ?? (Node)node, broadDiag);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:47443-47454 — <c>checkBaseTypeAccessibility</c>.</summary>
    internal void CheckBaseTypeAccessibility(IType type, ExpressionWithTypeArguments node)
    {
        var signatures = GetSignaturesOfType(type, SignatureKind.Construct);
        if (signatures.Count != 0)
        {
            var declaration = signatures[0].Declaration;
            if (declaration is not null && Utilities.HasEffectiveModifier((Node)declaration, ModifierFlags.Private))
            {
                var typeClassDeclaration = Utilities.GetClassLikeDeclarationOfSymbol(type.Symbol!)!;
                if (!IsNodeWithinClass(node, (IClassLikeDeclarationBase)typeClassDeclaration))
                {
                    Error(node, Diagnostics.Cannot_extend_a_class_0_Class_constructor_is_marked_as_private, GetFullyQualifiedName(type.Symbol!));
                }
            }
        }
    }

    /// <summary>
    /// Checks a member declaration node to see if has a missing or invalid <c>override</c> modifier.
    /// Note: <c>member</c> can be a synthetic node without a parent.
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:47463-47495 — <c>getMemberOverrideModifierStatus</c>.</remarks>
    internal MemberOverrideStatus GetMemberOverrideModifierStatus(IClassLikeDeclarationBase node, IClassElement member, ISymbol memberSymbol)
    {
        var memberName = ((INamedDeclaration)member).Name;
        if (memberName is null)
        {
            return MemberOverrideStatus.Ok;
        }

        var classSymbol = GetSymbolOfDeclaration((IDeclaration)node);
        var type = (InterfaceType)GetDeclaredTypeOfSymbol(classSymbol);
        var typeWithThis = GetTypeWithThisArgument(type);
        // PORT-NOTE: TS's `getTypeOfSymbol(classSymbol) as ObjectType` is read through `IObjectType` for the same
        // reason as in `CheckClassLikeDeclaration`.
        var staticType = (IObjectType)GetTypeOfSymbol(classSymbol);

        var baseTypeNode = Utilities.GetEffectiveBaseTypeNode((Node)node);
        var baseTypes = baseTypeNode is not null ? GetBaseTypes(type) : null;
        var baseWithThis = baseTypes is { Count: > 0 } ? GetTypeWithThisArgument(Core.First(baseTypes), type.ThisType!) : null;
        var baseStaticType = GetBaseConstructorTypeOfClass(type);

        var memberHasOverrideModifier = ((Node)member).Parent is not null
            ? Utilities.HasOverrideModifier((Node)member)
            : Utilities.HasSyntacticModifier((Node)member, ModifierFlags.Override);

        return CheckMemberForOverrideModifier(
            node,
            staticType,
            baseStaticType,
            baseWithThis,
            type,
            typeWithThis,
            memberHasOverrideModifier,
            Utilities.HasAbstractModifier((Node)member),
            Utilities.IsStatic((Node)member),
            /*memberIsParameterProperty*/ false,
            memberSymbol);
    }

    /// <summary>ported from: src/compiler/checker.ts:47497-47502 — <c>getTargetSymbol</c>.</summary>
    internal ISymbol GetTargetSymbol(ISymbol symbol)
    {
        // if symbol is instantiated its flags are not copied from the 'target'
        // so we'll need to get back original 'target' symbol to work with correct set of flags
        // NOTE: cast to TransientSymbol should be safe because only TransientSymbols have CheckFlags.Instantiated
        return (GetCheckFlags(symbol) & CheckFlags.Instantiated) != 0 ? ((ITransientSymbol)symbol).Links!.Target! : symbol;
    }

    /// <summary>ported from: src/compiler/checker.ts:47504-47506 — <c>getClassOrInterfaceDeclarationsOfSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's filter is a type guard answering <c>(ClassDeclaration | InterfaceDeclaration)[]</c>; the port
    /// answers the same two kinds as their common <c>Node</c> face, which is what the two readers (this file and
    /// <c>areTypeParametersIdentical</c>) need, and <c>null</c> for the absent declaration list.
    /// </remarks>
    internal List<Node>? GetClassOrInterfaceDeclarationsOfSymbol(ISymbol symbol)
        => Core.Map(Core.Filter(symbol.Declarations, declaration => declaration.Kind == SyntaxKind.ClassDeclaration || declaration.Kind == SyntaxKind.InterfaceDeclaration), (declaration, _) => (Node)declaration);

    /// <summary>ported from: src/compiler/checker.ts:47508-47688 — <c>checkKindsOfPropertyMemberOverrides</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: TS's in-function <c>interface MemberInfo</c> becomes the named tuple the map below holds — the port
    /// has no place for a method-local type and a nested <c>Checker</c> type of that name would be a new declaration
    /// shared by every slice; the three fields keep the reference's names and order, which is what the readers use.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>Map</c> accepts <c>undefined</c> as a key, and <c>derivedClassDecl</c> can be
    /// <c>undefined</c> for a class expression without a declaration; the port's <c>Dictionary</c> cannot, so the key
    /// site keeps the reference's own <c>!</c> on <c>getClassLikeDeclarationOfSymbol</c> — the case that assertion
    /// covers (a class expression whose symbol has no class-like declaration) is the one the map would key by
    /// <c>undefined</c>, and it is the case the reference itself cannot report on (it hands that key to
    /// <c>isClassExpression</c>).
    /// </para>
    /// </remarks>
    internal void CheckKindsOfPropertyMemberOverrides(InterfaceType type, IType baseType)
    {
        // TypeScript 1.0 spec (April 2014): 8.2.3
        // A derived class inherits all members from its base class it doesn't override.
        // Inheritance means that a derived class implicitly contains all non - overridden members of the base class.
        // Both public and private property members are inherited, but only public property members can be overridden.
        // A property member in a derived class is said to override a property member in a base class
        // when the derived class property member has the same name and kind(instance or static)
        // as the base class property member.
        // The type of an overriding property member must be assignable(section 3.8.4)
        // to the type of the overridden property member, or otherwise a compile - time error occurs.
        // Base class instance member functions can be overridden by derived class instance member functions,
        // but not by other kinds of members.
        // Base class instance member variables and accessors can be overridden by
        // derived class instance member variables and accessors, but not by other kinds of members.

        // NOTE: assignability is checked in checkClassDeclaration
        var baseProperties = GetPropertiesOfType(baseType);

        var notImplementedInfo = new Dictionary<Node, (string BaseTypeName, string TypeName, IList<string>? MissedProperties)>();

        foreach (var baseProperty in baseProperties)
        {
            var baseSymbolTarget = GetTargetSymbol(baseProperty);

            if ((baseSymbolTarget.Flags & SymbolFlags.Prototype) != 0)
            {
                continue;
            }
            var baseSymbol = GetPropertyOfObjectType(type, baseSymbolTarget.EscapedName!);
            if (baseSymbol is null)
            {
                continue;
            }
            var derived = GetTargetSymbol(baseSymbol);
            var baseDeclarationFlags = Utilities.GetDeclarationModifierFlagsFromSymbol(baseSymbolTarget);

            Debug.Assert(derived is not null, "derived should point to something, even if it is the base class' declaration.");

            // In order to resolve whether the inherited method was overridden in the base class or not,
            // we compare the Symbols obtained. Since getTargetSymbol returns the symbol on the *uninstantiated*
            // type declaration, derived and base resolve to the same symbol even in the case of generic classes.
            if (ReferenceEquals(derived, baseSymbolTarget))
            {
                // derived class inherits base without override/redeclaration
                var derivedClassDecl = Utilities.GetClassLikeDeclarationOfSymbol(type.Symbol!);

                // It is an error to inherit an abstract member without implementing it or being declared abstract.
                // If there is no declaration for the derived class (as in the case of class expressions),
                // then the class cannot be declared abstract.
                if ((baseDeclarationFlags & ModifierFlags.Abstract) != 0 && (derivedClassDecl is null || !Utilities.HasSyntacticModifier(derivedClassDecl, ModifierFlags.Abstract)))
                {
                    // Searches other base types for a declaration that would satisfy the inherited abstract member.
                    // (The class may have more than one base type via declaration merging with an interface with the
                    // same name.)
                    var satisfiedElsewhere = false;
                    foreach (var otherBaseType in GetBaseTypes(type))
                    {
                        if (ReferenceEquals(otherBaseType, baseType)) continue;
                        var otherBaseSymbol = GetPropertyOfObjectType(otherBaseType, baseSymbolTarget.EscapedName!);
                        var derivedElsewhere = otherBaseSymbol is not null ? GetTargetSymbol(otherBaseSymbol) : null;
                        if (derivedElsewhere is not null && !ReferenceEquals(derivedElsewhere, baseSymbolTarget))
                        {
                            satisfiedElsewhere = true;
                            break;
                        }
                    }
                    if (!satisfiedElsewhere)
                    {
                        var baseTypeName = TypeToString(baseType);
                        var typeName = TypeToString(type);
                        var basePropertyName = SymbolToString(baseProperty);
                        var missedProperties = Core.Append(
                            notImplementedInfo.TryGetValue(derivedClassDecl!, out var existingInfo) ? existingInfo.MissedProperties : null,
                            basePropertyName);
                        notImplementedInfo[derivedClassDecl!] = (baseTypeName, typeName, missedProperties);
                    }
                }
            }
            else
            {
                // derived overrides base.
                var derivedDeclarationFlags = Utilities.GetDeclarationModifierFlagsFromSymbol(derived);
                if ((baseDeclarationFlags & ModifierFlags.Private) != 0 || (derivedDeclarationFlags & ModifierFlags.Private) != 0)
                {
                    // either base or derived property is private - not override, skip it
                    continue;
                }

                DiagnosticMessage errorMessage;
                var basePropertyFlags = baseSymbolTarget.Flags & SymbolFlags.PropertyOrAccessor;
                var derivedPropertyFlags = derived.Flags & SymbolFlags.PropertyOrAccessor;
                if (basePropertyFlags != 0 && derivedPropertyFlags != 0)
                {
                    // property/accessor is overridden with property/accessor
                    if (
                        ((GetCheckFlags(baseSymbolTarget) & CheckFlags.Synthetic) != 0
                            ? Core.Some(baseSymbolTarget.Declarations, d => IsPropertyAbstractOrInterface(d, baseDeclarationFlags))
                            : Core.Every(baseSymbolTarget.Declarations, (d, _) => IsPropertyAbstractOrInterface(d, baseDeclarationFlags)))
                        || (GetCheckFlags(baseSymbolTarget) & CheckFlags.Mapped) != 0
                        || derived.ValueDeclaration is not null && NodeTests.IsBinaryExpression((Node)derived.ValueDeclaration)
                    )
                    {
                        // when the base property is abstract or from an interface, base/derived flags don't need to match
                        // for intersection properties, this must be true of *any* of the declarations, for others it must be true of *all*
                        // same when the derived property is from an assignment
                        continue;
                    }

                    var overriddenInstanceProperty = basePropertyFlags != SymbolFlags.Property && derivedPropertyFlags == SymbolFlags.Property;
                    var overriddenInstanceAccessor = basePropertyFlags == SymbolFlags.Property && derivedPropertyFlags != SymbolFlags.Property;
                    if (overriddenInstanceProperty || overriddenInstanceAccessor)
                    {
                        var overriddenMessage = overriddenInstanceProperty ?
                            Diagnostics._0_is_defined_as_an_accessor_in_class_1_but_is_overridden_here_in_2_as_an_instance_property :
                            Diagnostics._0_is_defined_as_a_property_in_class_1_but_is_overridden_here_in_2_as_an_accessor;
                        Error(Utilities.GetNameOfDeclaration((Node?)derived.ValueDeclaration) as Node ?? (Node)derived.ValueDeclaration!, overriddenMessage, SymbolToString(baseSymbolTarget), TypeToString(baseType), TypeToString(type));
                    }
                    else if (_useDefineForClassFields)
                    {
                        var uninitialized = Core.Find(derived.Declarations, (IDeclaration d, int _) => d.Kind == SyntaxKind.PropertyDeclaration && ((PropertyDeclaration)d).Initializer is null);
                        if (
                            uninitialized is not null
                            && (derived.Flags & SymbolFlags.Transient) == 0
                            && (baseDeclarationFlags & ModifierFlags.Abstract) == 0
                            && (derivedDeclarationFlags & ModifierFlags.Abstract) == 0
                            && !Core.Some(derived.Declarations, d => (d.Flags & NodeFlags.Ambient) != 0)
                        )
                        {
                            var constructor = Utilities.FindConstructorDeclaration(Utilities.GetClassLikeDeclarationOfSymbol(type.Symbol!)!);
                            var propName = ((PropertyDeclaration)uninitialized).Name!;
                            if (
                                ((PropertyDeclaration)uninitialized).ExclamationToken is not null
                                || constructor is null
                                || !NodeTests.IsIdentifier(propName)
                                || !_strictNullChecks
                                || !IsPropertyInitializedInConstructor(propName, type, constructor)
                            )
                            {
                                var overwriteMessage = Diagnostics.Property_0_will_overwrite_the_base_property_in_1_If_this_is_intentional_add_an_initializer_Otherwise_add_a_declare_modifier_or_remove_the_redundant_declaration;
                                Error(Utilities.GetNameOfDeclaration((Node?)derived.ValueDeclaration) as Node ?? (Node)derived.ValueDeclaration!, overwriteMessage, SymbolToString(baseSymbolTarget), TypeToString(baseType));
                            }
                        }
                    }

                    // correct case
                    continue;
                }
                else if (IsPrototypeProperty(baseSymbolTarget))
                {
                    if (IsPrototypeProperty(derived) || (derived.Flags & SymbolFlags.Property) != 0)
                    {
                        // method is overridden with method or property -- correct case
                        continue;
                    }
                    else
                    {
                        Debug.Assert((derived.Flags & SymbolFlags.Accessor) != 0);
                        errorMessage = Diagnostics.Class_0_defines_instance_member_function_1_but_extended_class_2_defines_it_as_instance_member_accessor;
                    }
                }
                else if ((baseSymbolTarget.Flags & SymbolFlags.Accessor) != 0)
                {
                    errorMessage = Diagnostics.Class_0_defines_instance_member_accessor_1_but_extended_class_2_defines_it_as_instance_member_function;
                }
                else
                {
                    errorMessage = Diagnostics.Class_0_defines_instance_member_property_1_but_extended_class_2_defines_it_as_instance_member_function;
                }

                Error(Utilities.GetNameOfDeclaration((Node?)derived.ValueDeclaration) as Node ?? (Node)derived.ValueDeclaration!, errorMessage, TypeToString(baseType), SymbolToString(baseSymbolTarget), TypeToString(type));
            }
        }

        foreach (var (errorNode, memberInfo) in notImplementedInfo)
        {
            if (Core.Length(memberInfo.MissedProperties) == 1)
            {
                if (NodeTests.IsClassExpression(errorNode))
                {
                    Error(errorNode, Diagnostics.Non_abstract_class_expression_does_not_implement_inherited_abstract_member_0_from_class_1, Core.First(memberInfo.MissedProperties!), memberInfo.BaseTypeName);
                }
                else
                {
                    Error(errorNode, Diagnostics.Non_abstract_class_0_does_not_implement_inherited_abstract_member_1_from_class_2, memberInfo.TypeName, Core.First(memberInfo.MissedProperties!), memberInfo.BaseTypeName);
                }
            }
            else if (Core.Length(memberInfo.MissedProperties) > 5)
            {
                var missedProperties = string.Join(", ", Core.Map(Core.Slice(memberInfo.MissedProperties!, 0, 4), (prop, _) => $"'{prop}'")!);
                var remainingMissedProperties = Core.Length(memberInfo.MissedProperties) - 4;
                if (NodeTests.IsClassExpression(errorNode))
                {
                    Error(errorNode, Diagnostics.Non_abstract_class_expression_is_missing_implementations_for_the_following_members_of_0_Colon_1_and_2_more, memberInfo.BaseTypeName, missedProperties, remainingMissedProperties);
                }
                else
                {
                    Error(errorNode, Diagnostics.Non_abstract_class_0_is_missing_implementations_for_the_following_members_of_1_Colon_2_and_3_more, memberInfo.TypeName, memberInfo.BaseTypeName, missedProperties, remainingMissedProperties);
                }
            }
            else
            {
                var missedProperties = string.Join(", ", Core.Map(memberInfo.MissedProperties, (prop, _) => $"'{prop}'")!);
                if (NodeTests.IsClassExpression(errorNode))
                {
                    Error(errorNode, Diagnostics.Non_abstract_class_expression_is_missing_implementations_for_the_following_members_of_0_Colon_1, memberInfo.BaseTypeName, missedProperties);
                }
                else
                {
                    Error(errorNode, Diagnostics.Non_abstract_class_0_is_missing_implementations_for_the_following_members_of_1_Colon_2, memberInfo.TypeName, memberInfo.BaseTypeName, missedProperties);
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:47690-47693 — <c>isPropertyAbstractOrInterface</c>.</summary>
    internal bool IsPropertyAbstractOrInterface(IDeclaration declaration, ModifierFlags baseDeclarationFlags)
        => (baseDeclarationFlags & ModifierFlags.Abstract) != 0 && (!NodeTests.IsPropertyDeclaration((Node)declaration) || ((PropertyDeclaration)declaration).Initializer is null)
            || NodeTests.IsInterfaceDeclaration(((Node)declaration).Parent!);

    /// <summary>ported from: src/compiler/checker.ts:47695-47715 — <c>getNonInheritedProperties</c>.</summary>
    internal List<ISymbol> GetNonInheritedProperties(InterfaceType type, List<IType> baseTypes, List<ISymbol> properties)
    {
        if (Core.Length(baseTypes) == 0)
        {
            return properties;
        }
        var seen = new Dictionary<string, ISymbol>();
        Core.ForEach(properties, (ISymbol p, int _) =>
        {
            seen[p.EscapedName!] = p;
            return (object?)null;
        });

        foreach (var baseType in baseTypes)
        {
            var baseProperties = GetPropertiesOfType(GetTypeWithThisArgument(baseType, type.ThisType!));
            foreach (var prop in baseProperties)
            {
                var existing = seen.GetValueOrDefault(prop.EscapedName!);
                if (existing is not null && ReferenceEquals(prop.Parent, existing.Parent))
                {
                    seen.Remove(prop.EscapedName!);
                }
            }
        }

        return [.. Core.ArrayFrom(seen.Values)];
    }

    /// <summary>ported from: src/compiler/checker.ts:47717-47757 — <c>checkInheritedPropertiesAreIdentical</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's in-function <c>interface InheritanceInfoMap</c> is the named tuple the map holds, for the same
    /// reason as the <c>MemberInfo</c> note above; the reference's <c>Map.get</c> is <c>TryGetValue</c> because the
    /// port's indexer throws on a miss.
    /// </remarks>
    internal bool CheckInheritedPropertiesAreIdentical(InterfaceType type, Node typeNode)
    {
        var baseTypes = GetBaseTypes(type);
        if (baseTypes.Count < 2)
        {
            return true;
        }

        var seen = new Dictionary<string, (ISymbol Prop, IType ContainingType)>();
        Core.ForEach(ResolveDeclaredMembers(type).DeclaredProperties, (ISymbol p, int _) =>
        {
            seen[p.EscapedName!] = (p, type);
            return (object?)null;
        });
        var ok = true;

        foreach (var baseType in baseTypes)
        {
            var properties = GetPropertiesOfType(GetTypeWithThisArgument(baseType, type.ThisType!));
            foreach (var prop in properties)
            {
                if (!seen.TryGetValue(prop.EscapedName!, out var existing))
                {
                    seen[prop.EscapedName!] = (prop, baseType);
                }
                else
                {
                    var isInheritedProperty = !ReferenceEquals(existing.ContainingType, type);
                    if (isInheritedProperty && !IsPropertyIdenticalTo(existing.Prop, prop))
                    {
                        ok = false;

                        var typeName1 = TypeToString(existing.ContainingType);
                        var typeName2 = TypeToString(baseType);

                        var errorInfo = Utilities.ChainDiagnosticMessages(/*details*/ null, Diagnostics.Named_property_0_of_types_1_and_2_are_not_identical, SymbolToString(prop), typeName1, typeName2);
                        errorInfo = Utilities.ChainDiagnosticMessages(errorInfo, Diagnostics.Interface_0_cannot_simultaneously_extend_types_1_and_2, TypeToString(type), typeName1, typeName2);
                        _diagnostics.Add(Utilities.CreateDiagnosticForNodeFromMessageChain(Utilities.GetSourceFileOfNode(typeNode)!, typeNode, errorInfo));
                    }
                }
            }
        }

        return ok;
    }

    // ---------------------------------------------------------------------------------------------
    // The property-initialization checks (checker.ts:47759-47815)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:47759-47780 — <c>checkPropertyInitialization</c>.</summary>
    internal void CheckPropertyInitialization(IClassLikeDeclarationBase node)
    {
        if (!_strictNullChecks || !_strictPropertyInitialization || (((Node)node).Flags & NodeFlags.Ambient) != 0)
        {
            return;
        }
        var constructor = Utilities.FindConstructorDeclaration((Node)node);
        foreach (var member in node.Members!)
        {
            if ((Utilities.GetEffectiveModifierFlags((Node)member) & ModifierFlags.Ambient) != 0)
            {
                continue;
            }
            if (!Utilities.IsStatic((Node)member) && IsPropertyWithoutInitializer((Node)member))
            {
                var propName = ((PropertyDeclaration)member).Name!;
                if (NodeTests.IsIdentifier(propName) || NodeTests.IsPrivateIdentifier(propName) || NodeTests.IsComputedPropertyName(propName))
                {
                    var type = GetTypeOfSymbol(GetSymbolOfDeclaration((IDeclaration)member));
                    if ((type.Flags & TypeFlags.AnyOrUnknown) == 0 && !ContainsUndefinedType(type))
                    {
                        if (constructor is null || !IsPropertyInitializedInConstructor(propName, type, constructor))
                        {
                            Error(((PropertyDeclaration)member).Name, Diagnostics.Property_0_has_no_initializer_and_is_not_definitely_assigned_in_the_constructor, Utilities.DeclarationNameToString(propName));
                        }
                    }
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:47782-47787 — <c>isPropertyWithoutInitializer</c>.</summary>
    internal bool IsPropertyWithoutInitializer(Node node)
        => node.Kind == SyntaxKind.PropertyDeclaration &&
            !Utilities.HasAbstractModifier(node) &&
            ((PropertyDeclaration)node).ExclamationToken is null &&
            ((PropertyDeclaration)node).Initializer is null;

    /// <summary>ported from: src/compiler/checker.ts:47789-47804 — <c>isPropertyInitializedInStaticBlocks</c>.</summary>
    internal bool IsPropertyInitializedInStaticBlocks(Identifier propName, IType propType, IList<ClassStaticBlockDeclaration> staticBlocks, int startPos, int endPos)
    {
        foreach (var staticBlock in staticBlocks)
        {
            // static block must be within the provided range as they are evaluated in document order (unlike constructors)
            if (staticBlock.Pos >= startPos && staticBlock.Pos <= endPos)
            {
                var reference = _factory.CreatePropertyAccessExpression(_factory.CreateThis(), propName);
                Utilities.SetParent(reference.Expression, reference);
                Utilities.SetParent(reference, staticBlock);
                reference.FlowNode = staticBlock.ReturnFlowNode;
                var flowType = GetFlowTypeOfReference(reference, propType, GetOptionalType(propType));
                if (!ContainsUndefinedType(flowType))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:47806-47815 — <c>isPropertyInitializedInConstructor</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>reference</c> is the union <c>PropertyAccessExpression | ElementAccessExpression</c>, both of
    /// which carry <c>flowNode</c>; in the port only the concrete classes declare it (the two capability interfaces
    /// they meet in are <c>IFlowContainer</c>/<c>INamedDeclaration</c>), so the slot is written through
    /// <see cref="IFlowContainer"/> and the reference is held as the shared <c>Node</c> face. The reference's
    /// <c>setParent(reference.expression, reference)</c> is the one <c>createThis()</c> both arms build, which is why
    /// it is hoisted into a local.
    /// </remarks>
    internal bool IsPropertyInitializedInConstructor(Node propName, IType propType, ConstructorDeclaration constructor)
    {
        var thisExpression = _factory.CreateThis();
        var reference = NodeTests.IsComputedPropertyName(propName)
            ? (Node)_factory.CreateElementAccessExpression(thisExpression, ((ComputedPropertyName)propName).Expression!)
            : _factory.CreatePropertyAccessExpression(thisExpression, propName);
        Utilities.SetParent(thisExpression, reference);
        Utilities.SetParent(reference, constructor);
        ((IFlowContainer)reference).FlowNode = constructor.ReturnFlowNode;
        var flowType = GetFlowTypeOfReference(reference, propType, GetOptionalType(propType));
        return !ContainsUndefinedType(flowType);
    }

    // ---------------------------------------------------------------------------------------------
    // The interface and type-alias declarations (checker.ts:47817-47883)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:47817-47860 — <c>checkInterfaceDeclaration</c>.</summary>
    internal void CheckInterfaceDeclaration(InterfaceDeclaration node)
    {
        // Grammar checking
        if (!CheckGrammarModifiers(node)) CheckGrammarInterfaceDeclaration(node);
        if (!AllowBlockDeclarations(node.Parent!))
        {
            GrammarErrorOnNode(node, Diagnostics._0_declarations_can_only_be_declared_inside_a_block, "interface");
        }

        CheckTypeParameters(node.TypeParameters);
        AddLazyDiagnostic(() =>
        {
            CheckTypeNameIsReserved(node.Name!, Diagnostics.Interface_name_cannot_be_0);

            CheckExportsOnMergedDeclarations(node);
            var symbol = GetSymbolOfDeclaration(node);
            CheckTypeParameterListsIdentical(symbol);

            // Only check this symbol once
            var firstInterfaceDecl = Utilities.GetDeclarationOfKind<InterfaceDeclaration>(symbol, SyntaxKind.InterfaceDeclaration);
            if (ReferenceEquals(node, firstInterfaceDecl))
            {
                var type = (InterfaceType)GetDeclaredTypeOfSymbol(symbol);
                var typeWithThis = GetTypeWithThisArgument(type);
                // run subsequent checks only if first set succeeded
                if (CheckInheritedPropertiesAreIdentical(type, node.Name!))
                {
                    foreach (var baseType in GetBaseTypes(type))
                    {
                        CheckTypeAssignableTo(typeWithThis, GetTypeWithThisArgument(baseType, type.ThisType!), node.Name!, Diagnostics.Interface_0_incorrectly_extends_interface_1);
                    }
                    CheckIndexConstraints(type, symbol);
                }
            }
            CheckObjectTypeForDuplicateDeclarations(node);
        });
        Core.ForEach(Utilities.GetInterfaceBaseTypeNodes(node), (ExpressionWithTypeArguments heritageElement, int _) =>
        {
            if (!Utilities.IsEntityNameExpression((Node)heritageElement.Expression!) || Utilities.IsOptionalChain((Node)heritageElement.Expression!))
            {
                Error((Node?)heritageElement.Expression, Diagnostics.An_interface_can_only_extend_an_identifier_Slashqualified_name_with_optional_type_arguments);
            }
            CheckTypeReferenceNode(heritageElement);
            return (object?)null;
        });

        Core.ForEach<ITypeElement, object>(node.Members, (member, _) =>
        {
            CheckSourceElement((Node)member);
            return null;
        });

        AddLazyDiagnostic(() =>
        {
            CheckTypeForDuplicateIndexSignatures(node);
            RegisterForUnusedIdentifiersCheck(node);
        });
    }

    /// <summary>ported from: src/compiler/checker.ts:47862-47883 — <c>checkTypeAliasDeclaration</c>.</summary>
    internal void CheckTypeAliasDeclaration(TypeAliasDeclaration node)
    {
        // Grammar checking
        CheckGrammarModifiers(node);
        CheckTypeNameIsReserved(node.Name!, Diagnostics.Type_alias_name_cannot_be_0);
        if (!AllowBlockDeclarations(node.Parent!))
        {
            GrammarErrorOnNode(node, Diagnostics._0_declarations_can_only_be_declared_inside_a_block, "type");
        }
        CheckExportsOnMergedDeclarations(node);
        CheckTypeParameters(node.TypeParameters);
        if (node.Type!.Kind == SyntaxKind.IntrinsicKeyword)
        {
            var typeParameterCount = Core.Length(node.TypeParameters);
            var valid = typeParameterCount == 0 ? node.Name!.EscapedText == "BuiltinIteratorReturn" :
                typeParameterCount == 1 && _intrinsicTypeKinds.ContainsKey(node.Name!.EscapedText!);
            if (!valid)
            {
                Error((Node)node.Type, Diagnostics.The_intrinsic_keyword_can_only_be_used_to_declare_compiler_provided_intrinsic_types);
            }
        }
        else
        {
            CheckSourceElement(node.Type);
            RegisterForUnusedIdentifiersCheck(node);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The enum declarations (checker.ts:47885-48111)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:47885-47898 — <c>computeEnumMemberValues</c>.</summary>
    internal void ComputeEnumMemberValues(EnumDeclaration node)
    {
        var nodeLinks = GetNodeLinks(node);
        if ((nodeLinks.Flags & NodeCheckFlags.EnumValuesComputed) == 0)
        {
            nodeLinks.Flags |= NodeCheckFlags.EnumValuesComputed;
            double? autoValue = 0;
            EnumMember? previous = null;
            foreach (var member in node.Members!)
            {
                var result = ComputeEnumMemberValue(member, autoValue, previous);
                GetNodeLinks(member).EnumMemberValue = result;
                autoValue = result.Value is double number ? number + 1 : null;
                previous = member;
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:47900-47939 — <c>computeEnumMemberValue</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>typeof result.value === "number"</c> is <see cref="Core.IsNumber"/>'s field, but the slot is
    /// read as a <c>double</c> immediately afterwards, which is the shape the reference's <c>autoValue</c> arithmetic
    /// requires.
    /// </remarks>
    internal IEvaluatorResult<object?> ComputeEnumMemberValue(EnumMember member, double? autoValue, EnumMember? previous)
    {
        if (Utilities.IsComputedNonLiteralName(member.Name!))
        {
            Error(member.Name, Diagnostics.Computed_property_names_are_not_allowed_in_enums);
        }
        else if (NodeTests.IsBigIntLiteral(member.Name!))
        {
            Error(member.Name, Diagnostics.An_enum_member_cannot_have_a_numeric_name);
        }
        else
        {
            var text = Utilities.GetTextOfPropertyName(member.Name!);
            if (Utilities.IsNumericLiteralName(text) && !Utilities.IsInfinityOrNaNString(text))
            {
                Error(member.Name, Diagnostics.An_enum_member_cannot_have_a_numeric_name);
            }
        }
        if (member.Initializer is not null)
        {
            return ComputeConstantEnumMemberValue(member);
        }
        // In ambient non-const numeric enum declarations, enum members without initializers are
        // considered computed members (as opposed to having auto-incremented values).
        if ((((Node)member.Parent!).Flags & NodeFlags.Ambient) != 0 && !Utilities.IsEnumConst((EnumDeclaration)member.Parent!))
        {
            return Utilities.EvaluatorResult<object?>(/*value*/ null);
        }
        // If the member declaration specifies no value, the member is considered a constant enum member.
        // If the member is the first member in the enum declaration, it is assigned the value zero.
        // Otherwise, it is assigned the value of the immediately preceding member plus one, and an error
        // occurs if the immediately preceding member is not a constant enum member.
        if (autoValue is null)
        {
            Error(member.Name, Diagnostics.Enum_member_must_have_initializer);
            return Utilities.EvaluatorResult<object?>(/*value*/ null);
        }
        if (Utilities.GetIsolatedModules(_compilerOptions) && previous?.Initializer is not null)
        {
            var prevValue = GetEnumMemberValue(previous);
            if (!(prevValue.Value is double && !prevValue.ResolvedOtherFiles))
            {
                Error(
                    member.Name,
                    Diagnostics.Enum_member_following_a_non_literal_numeric_member_must_have_an_initializer_when_isolatedModules_is_enabled);
            }
        }
        return Utilities.EvaluatorResult<object?>(autoValue);
    }

    /// <summary>ported from: src/compiler/checker.ts:47941-47972 — <c>computeConstantEnumMemberValue</c>.</summary>
    internal IEvaluatorResult<object?> ComputeConstantEnumMemberValue(EnumMember member)
    {
        var isConstEnum = Utilities.IsEnumConst((EnumDeclaration)member.Parent!);
        var initializer = member.Initializer!;
        var result = _evaluate(initializer, member)!;
        if (result.Value is not null)
        {
            if (isConstEnum && result.Value is double numberValue && !double.IsFinite(numberValue))
            {
                Error(
                    initializer,
                    double.IsNaN(numberValue) ?
                        Diagnostics.const_enum_member_initializer_was_evaluated_to_disallowed_value_NaN :
                        Diagnostics.const_enum_member_initializer_was_evaluated_to_a_non_finite_value);
            }
            else if (Utilities.GetIsolatedModules(_compilerOptions) && result.Value is string && !result.IsSyntacticallyString)
            {
                Error(
                    initializer,
                    Diagnostics._0_has_a_string_type_but_must_have_syntactically_recognizable_string_syntax_when_isolatedModules_is_enabled,
                    $"{Utilities.IdText(((EnumDeclaration)member.Parent!).Name!)}.{Utilities.GetTextOfPropertyName(member.Name!)}");
            }
        }
        else if (isConstEnum)
        {
            Error(initializer, Diagnostics.const_enum_member_initializers_must_be_constant_expressions);
        }
        else if ((((Node)member.Parent!).Flags & NodeFlags.Ambient) != 0)
        {
            Error(initializer, Diagnostics.In_ambient_enum_declarations_member_initializer_must_be_constant_expression);
        }
        else
        {
            CheckTypeAssignableTo(CheckExpression(initializer), _numberType, initializer, Diagnostics.Type_0_is_not_assignable_to_type_1_as_required_for_computed_enum_member_values);
        }
        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:47974-48007 — <c>evaluateEntityNameExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>+identifier.escapedText</c> is JS's unary plus over a string, which is the port's
    /// <see cref="Js.ToNumber"/>; the reference's own comment on the <c>Infinity</c>/<c>NaN</c> arm is kept.
    /// </remarks>
    internal IEvaluatorResult<object?> EvaluateEntityNameExpression(MemberExpression expr, IDeclaration? location = null)
    {
        var symbol = ResolveEntityName(expr, SymbolFlags.Value, /*ignoreErrors*/ true);
        if (symbol is null) return Utilities.EvaluatorResult<object?>(/*value*/ null);

        if (expr.Kind == SyntaxKind.Identifier)
        {
            var identifier = (Identifier)expr;
            if (Utilities.IsInfinityOrNaNString(identifier.EscapedText!) && ReferenceEquals(symbol, GetGlobalSymbol(identifier.EscapedText!, SymbolFlags.Value, /*diagnostic*/ null)))
            {
                // Technically we resolved a global lib file here, but the decision to treat this as numeric
                // is more predicated on the fact that the single-file resolution *didn't* resolve to a
                // different meaning of `Infinity` or `NaN`. Transpilers handle this no problem.
                return Utilities.EvaluatorResult<object?>(Js.ToNumber(identifier.EscapedText!), /*isSyntacticallyString*/ false);
            }
        }

        if ((symbol.Flags & SymbolFlags.EnumMember) != 0)
        {
            return location is not null ? EvaluateEnumMember(expr, symbol, location) : GetEnumMemberValue((EnumMember)symbol.ValueDeclaration!);
        }
        if (IsConstantVariable(symbol))
        {
            var declaration = symbol.ValueDeclaration;
            if (declaration is not null && NodeTests.IsVariableDeclaration((Node)declaration) && ((VariableDeclaration)declaration).Type is null && ((VariableDeclaration)declaration).Initializer is not null && (location is null || (!ReferenceEquals(declaration, location) && IsBlockScopedNameDeclaredBeforeUse(declaration, (Node)location))))
            {
                var result = _evaluate(((VariableDeclaration)declaration!).Initializer!, declaration)!;
                if (location is not null && !ReferenceEquals(Utilities.GetSourceFileOfNode((Node)location), Utilities.GetSourceFileOfNode((Node)declaration)))
                {
                    return Utilities.EvaluatorResult<object?>(
                        result.Value,
                        /*isSyntacticallyString*/ false,
                        /*resolvedOtherFiles*/ true,
                        /*hasExternalReferences*/ true);
                }
                return Utilities.EvaluatorResult<object?>(result.Value, result.IsSyntacticallyString, result.ResolvedOtherFiles, /*hasExternalReferences*/ true);
            }
        }
        return Utilities.EvaluatorResult<object?>(/*value*/ null);
    }

    /// <summary>ported from: src/compiler/checker.ts:48009-48023 — <c>evaluateElementAccessExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads <c>expr.argumentExpression.text</c> off the string-literal-like the guard proved; the port
    /// reads the <c>StringLiteral</c>'s <c>Text</c> slot, whose other literal-kind is the no-substitution template the
    /// same guard admits.
    /// </remarks>
    internal IEvaluatorResult<object?> EvaluateElementAccessExpression(ElementAccessExpression expr, IDeclaration? location = null)
    {
        var root = expr.Expression!;
        if (Utilities.IsEntityNameExpression(root) && Utilities.IsStringLiteralLike(expr.ArgumentExpression!))
        {
            var rootSymbol = ResolveEntityName(root, SymbolFlags.Value, /*ignoreErrors*/ true);
            if (rootSymbol is not null && (rootSymbol.Flags & SymbolFlags.Enum) != 0)
            {
                var name = Utilities.EscapeLeadingUnderscores(((StringLiteral)expr.ArgumentExpression!).Text!);
                if (rootSymbol.Exports!.TryGetValue(name, out var member))
                {
                    Debug.Assert(ReferenceEquals(Utilities.GetSourceFileOfNode((Node)member.ValueDeclaration!), Utilities.GetSourceFileOfNode((Node)rootSymbol.ValueDeclaration!)));
                    return location is not null ? EvaluateEnumMember(expr, member, location) : GetEnumMemberValue((EnumMember)member.ValueDeclaration!);
                }
            }
        }
        return Utilities.EvaluatorResult<object?>(/*value*/ null);
    }

    /// <summary>ported from: src/compiler/checker.ts:48025-48040 — <c>evaluateEnumMember</c>.</summary>
    internal IEvaluatorResult<object?> EvaluateEnumMember(Expression expr, ISymbol symbol, IDeclaration location)
    {
        var declaration = symbol.ValueDeclaration;
        if (declaration is null || ReferenceEquals(declaration, location))
        {
            Error(expr, Diagnostics.Property_0_is_used_before_being_assigned, SymbolToString(symbol));
            return Utilities.EvaluatorResult<object?>(/*value*/ null);
        }
        if (!IsBlockScopedNameDeclaredBeforeUse(declaration, (Node)location))
        {
            Error(expr, Diagnostics.A_member_initializer_in_a_enum_declaration_cannot_reference_members_declared_after_it_including_members_defined_in_other_enums);
            return Utilities.EvaluatorResult<object?>(/*value*/ 0d);
        }
        var value = GetEnumMemberValue((EnumMember)declaration);
        if (!ReferenceEquals(((Node)location).Parent, ((Node)declaration).Parent))
        {
            return Utilities.EvaluatorResult<object?>(value.Value, value.IsSyntacticallyString, value.ResolvedOtherFiles, /*hasExternalReferences*/ true);
        }
        return value;
    }

    /// <summary>ported from: src/compiler/checker.ts:48042-48044 — <c>checkEnumDeclaration</c>.</summary>
    internal void CheckEnumDeclaration(EnumDeclaration node)
    {
        AddLazyDiagnostic(() => CheckEnumDeclarationWorker(node));
    }

    /// <summary>ported from: src/compiler/checker.ts:48046-48102 — <c>checkEnumDeclarationWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>forEach</c> callback answers <c>false</c> on the two arms that keep iterating; the
    /// port's <see cref="Core.ForEach{T,TU}"/> stops on a non-null answer, so those arms answer <c>null</c> — the same
    /// "continue" TS's falsy <c>false</c> means.
    /// </remarks>
    internal void CheckEnumDeclarationWorker(EnumDeclaration node)
    {
        // Grammar checking
        CheckGrammarModifiers(node);

        CheckCollisionsForDeclarationName(node, node.Name);
        CheckExportsOnMergedDeclarations(node);
        Core.ForEach<EnumMember, object>(node.Members, (member, _) =>
        {
            CheckSourceElement(member);
            return null;
        });

        if (Js.IsTruthy(_compilerOptions.ErasableSyntaxOnly) && (node.Flags & NodeFlags.Ambient) == 0)
        {
            Error(node, Diagnostics.This_syntax_is_not_allowed_when_erasableSyntaxOnly_is_enabled);
        }

        ComputeEnumMemberValues(node);

        // Spec 2014 - Section 9.3:
        // It isn't possible for one enum declaration to continue the automatic numbering sequence of another,
        // and when an enum type has multiple declarations, only one declaration is permitted to omit a value
        // for the first member.
        //
        // Only perform this check once per symbol
        var enumSymbol = GetSymbolOfDeclaration(node);
        var firstDeclaration = Utilities.GetDeclarationOfKind(enumSymbol, node.Kind);
        if (ReferenceEquals(node, firstDeclaration))
        {
            if (enumSymbol.Declarations is not null && enumSymbol.Declarations.Count > 1)
            {
                var enumIsConst = Utilities.IsEnumConst(node);
                // check that const is placed\omitted on all enum declarations
                Core.ForEach(enumSymbol.Declarations, (IDeclaration declaration, int _) =>
                {
                    if (NodeTests.IsEnumDeclaration((Node)declaration) && Utilities.IsEnumConst((EnumDeclaration)declaration) != enumIsConst)
                    {
                        Error((Node?)Utilities.GetNameOfDeclaration((Node)declaration), Diagnostics.Enum_declarations_must_all_be_const_or_non_const);
                    }
                    return (object?)null;
                });
            }

            var seenEnumMissingInitialInitializer = false;
            Core.ForEach(enumSymbol.Declarations, (IDeclaration declaration, int _) =>
            {
                // return true if we hit a violation of the rule, false otherwise
                if (declaration.Kind != SyntaxKind.EnumDeclaration)
                {
                    return (object?)null;
                }

                var enumDeclaration = (EnumDeclaration)declaration;
                if (Core.Length(enumDeclaration.Members) == 0)
                {
                    return (object?)null;
                }

                var firstEnumMember = enumDeclaration.Members![0];
                if (firstEnumMember.Initializer is null)
                {
                    if (seenEnumMissingInitialInitializer)
                    {
                        Error(firstEnumMember.Name, Diagnostics.In_an_enum_with_multiple_declarations_only_one_declaration_can_omit_an_initializer_for_its_first_enum_element);
                    }
                    else
                    {
                        seenEnumMissingInitialInitializer = true;
                    }
                }
                return (object?)null;
            });
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:48104-48111 — <c>checkEnumMember</c>.</summary>
    internal void CheckEnumMember(EnumMember node)
    {
        if (NodeTests.IsPrivateIdentifier(node.Name!))
        {
            Error(node, Diagnostics.An_enum_member_cannot_be_named_with_a_private_identifier);
        }
        if (node.Initializer is not null)
        {
            CheckExpression(node.Initializer);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The lexical-scope helpers (checker.ts:48113-48141)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:48113-48127 — <c>getFirstNonAmbientClassOrFunctionDeclaration</c>.</summary>
    internal IDeclaration? GetFirstNonAmbientClassOrFunctionDeclaration(ISymbol symbol)
    {
        var declarations = symbol.Declarations;
        if (declarations is not null)
        {
            foreach (var declaration in declarations)
            {
                if (
                    (declaration.Kind == SyntaxKind.ClassDeclaration ||
                        (declaration.Kind == SyntaxKind.FunctionDeclaration && Utilities.NodeIsPresent(((FunctionDeclaration)declaration).Body))) &&
                    (declaration.Flags & NodeFlags.Ambient) == 0
                )
                {
                    return declaration;
                }
            }
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:48129-48141 — <c>inSameLexicalScope</c>.</summary>
    internal bool InSameLexicalScope(Node node1, Node node2)
    {
        var container1 = Utilities.GetEnclosingBlockScopeContainer(node1);
        var container2 = Utilities.GetEnclosingBlockScopeContainer(node2);
        if (Utilities.IsGlobalSourceFile(container1))
        {
            return Utilities.IsGlobalSourceFile(container2);
        }
        else if (Utilities.IsGlobalSourceFile(container2))
        {
            return false;
        }
        else
        {
            return ReferenceEquals(container1, container2);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The module declarations (checker.ts:48143-48312)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:48143-48269 — <c>checkModuleDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares <c>checkModuleDeclarationDiagnostics</c> after the <c>addLazyDiagnostic</c> call that
    /// takes it; a C# local function is visible throughout its block, so the port keeps the reference's order, and the
    /// optional chain of its three callbacks is the local function itself.
    /// </remarks>
    internal void CheckModuleDeclaration(ModuleDeclaration node)
    {
        if (node.Body is not null)
        {
            CheckSourceElement(node.Body);
            if (!Utilities.IsGlobalScopeAugmentation(node))
            {
                RegisterForUnusedIdentifiersCheck(node);
            }
        }

        AddLazyDiagnostic(CheckModuleDeclarationDiagnostics);

        void CheckModuleDeclarationDiagnostics()
        {
            // Grammar checking
            var isGlobalAugmentation = Utilities.IsGlobalScopeAugmentation(node);
            var inAmbientContext = (node.Flags & NodeFlags.Ambient) != 0;
            if (isGlobalAugmentation && !inAmbientContext)
            {
                Error(node.Name, Diagnostics.Augmentations_for_the_global_scope_should_have_declare_modifier_unless_they_appear_in_already_ambient_context);
            }

            var isAmbientExternalModule = Utilities.IsAmbientModule(node);
            var contextErrorMessage = isAmbientExternalModule
                ? Diagnostics.An_ambient_module_declaration_is_only_allowed_at_the_top_level_in_a_file
                : Diagnostics.A_namespace_declaration_is_only_allowed_at_the_top_level_of_a_namespace_or_module;
            if (CheckGrammarModuleElementContext(node, contextErrorMessage))
            {
                // If we hit a module declaration in an illegal context, just bail out to avoid cascading errors.
                return;
            }

            if (!CheckGrammarModifiers(node))
            {
                if (!inAmbientContext && node.Name!.Kind == SyntaxKind.StringLiteral)
                {
                    GrammarErrorOnNode(node.Name, Diagnostics.Only_ambient_modules_can_use_quoted_names);
                }
            }

            if (NodeTests.IsIdentifier(node.Name!))
            {
                CheckCollisionsForDeclarationName(node, node.Name as Identifier);
                if ((node.Flags & (NodeFlags.Namespace | NodeFlags.GlobalAugmentation)) == 0)
                {
                    Error(node.Name, Diagnostics.A_namespace_declaration_should_not_be_declared_using_the_module_keyword_Please_use_the_namespace_keyword_instead);
                }
            }

            CheckExportsOnMergedDeclarations(node);
            var symbol = GetSymbolOfDeclaration(node);

            // The following checks only apply on a non-ambient instantiated module declaration.
            if (
                (symbol.Flags & SymbolFlags.ValueModule) != 0
                && !inAmbientContext
                && IsInstantiatedModule(node, Utilities.ShouldPreserveConstEnums(_compilerOptions))
            )
            {
                if (Js.IsTruthy(_compilerOptions.ErasableSyntaxOnly))
                {
                    Error(node.Name, Diagnostics.This_syntax_is_not_allowed_when_erasableSyntaxOnly_is_enabled);
                }

                if (Utilities.GetIsolatedModules(_compilerOptions) && Utilities.GetSourceFileOfNode(node)!.ExternalModuleIndicator is null)
                {
                    // This could be loosened a little if needed. The only problem we are trying to avoid is unqualified
                    // references to namespace members declared in other files. But use of namespaces is discouraged anyway,
                    // so for now we will just not allow them in scripts, which is the only place they can merge cross-file.
                    Error(node.Name, Diagnostics.Namespaces_are_not_allowed_in_global_script_files_when_0_is_enabled_If_this_file_is_not_intended_to_be_a_global_script_set_moduleDetection_to_force_or_add_an_empty_export_statement, _isolatedModulesLikeFlagName);
                }
                if (symbol.Declarations?.Count > 1)
                {
                    var firstNonAmbientClassOrFunc = GetFirstNonAmbientClassOrFunctionDeclaration(symbol);
                    if (firstNonAmbientClassOrFunc is not null)
                    {
                        if (!ReferenceEquals(Utilities.GetSourceFileOfNode(node), Utilities.GetSourceFileOfNode((Node)firstNonAmbientClassOrFunc)))
                        {
                            Error(node.Name, Diagnostics.A_namespace_declaration_cannot_be_in_a_different_file_from_a_class_or_function_with_which_it_is_merged);
                        }
                        else if (node.Pos < ((Node)firstNonAmbientClassOrFunc).Pos)
                        {
                            Error(node.Name, Diagnostics.A_namespace_declaration_cannot_be_located_prior_to_a_class_or_function_with_which_it_is_merged);
                        }
                    }

                    // if the module merges with a class declaration in the same lexical scope,
                    // we need to track this to ensure the correct emit.
                    var mergedClass = Utilities.GetDeclarationOfKind(symbol, SyntaxKind.ClassDeclaration);
                    if (
                        mergedClass is not null &&
                        InSameLexicalScope(node, (Node)mergedClass)
                    )
                    {
                        GetNodeLinks(node).Flags |= NodeCheckFlags.LexicalModuleMergesWithClass;
                    }
                }
                if (
                    Js.IsTruthy(_compilerOptions.VerbatimModuleSyntax) &&
                    node.Parent!.Kind == SyntaxKind.SourceFile &&
                    _host.GetEmitModuleFormatOfFile((SourceFile)node.Parent) == ModuleKind.CommonJS
                )
                {
                    var exportModifier = Core.Find(node.Modifiers, (Node modifier, int _) => modifier.Kind == SyntaxKind.ExportKeyword);
                    if (exportModifier is not null)
                    {
                        Error(exportModifier, Diagnostics.A_top_level_export_modifier_cannot_be_used_on_value_declarations_in_a_CommonJS_module_when_verbatimModuleSyntax_is_enabled);
                    }
                }
            }

            if (isAmbientExternalModule)
            {
                if (Utilities.IsExternalModuleAugmentation(node))
                {
                    // body of the augmentation should be checked for consistency only if augmentation was applied to its target (either global scope or module)
                    // otherwise we'll be swamped in cascading errors.
                    // We can detect if augmentation was applied using following rules:
                    // - augmentation for a global scope is always applied
                    // - augmentation for some external module is applied if symbol for augmentation is merged (it was combined with target module).
                    var checkBody = isGlobalAugmentation || (GetSymbolOfDeclaration(node).Flags & SymbolFlags.Transient) != 0;
                    if (checkBody && node.Body is not null)
                    {
                        foreach (var statement in ((ModuleBlock)node.Body).Statements!)
                        {
                            CheckModuleAugmentationElement((Node)statement, isGlobalAugmentation);
                        }
                    }
                }
                else if (Utilities.IsGlobalSourceFile(node.Parent!))
                {
                    if (isGlobalAugmentation)
                    {
                        Error(node.Name, Diagnostics.Augmentations_for_the_global_scope_can_only_be_directly_nested_in_external_modules_or_ambient_module_declarations);
                    }
                    else if (Utilities.IsExternalModuleNameRelative(Utilities.GetTextOfIdentifierOrLiteral(node.Name!)))
                    {
                        Error(node.Name, Diagnostics.Ambient_module_declaration_cannot_specify_relative_module_name);
                    }
                }
                else
                {
                    if (isGlobalAugmentation)
                    {
                        Error(node.Name, Diagnostics.Augmentations_for_the_global_scope_can_only_be_directly_nested_in_external_modules_or_ambient_module_declarations);
                    }
                    else
                    {
                        // Node is not an augmentation and is not located on the script level.
                        // This means that this is declaration of ambient module that is located in other module or namespace which is prohibited.
                        Error(node.Name, Diagnostics.Ambient_modules_cannot_be_nested_in_other_modules_or_namespaces);
                    }
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:48271-48312 — <c>checkModuleAugmentationElement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's two <c>falls through</c> comments are C# <c>goto case</c>s <em>into</em> the case labels below
    /// (<c>ImportDeclaration</c>, and the class/enum/function/interface/module/type-alias arm), and the reference's
    /// <c>break</c> out of the binding-pattern arm is the <c>break</c> inside its block. Its
    /// <c>(node as VariableDeclaration | BindingElement).name</c> reads <c>INamedDeclaration</c>, which is the
    /// capability interface both nodes carry in the port, and the binding pattern's elements come off the two concrete
    /// classes because the port has no <c>BindingPattern</c> class (<c>ObjectBindingPattern</c>/<c>ArrayBindingPattern</c>
    /// only).
    /// </remarks>
    internal void CheckModuleAugmentationElement(Node node, bool isGlobalAugmentation)
    {
        switch (node.Kind)
        {
            case SyntaxKind.VariableStatement:
            {
                // error each individual name in variable statement instead of marking the entire variable statement
                foreach (var decl in ((VariableStatement)node).DeclarationList!.Declarations!)
                {
                    CheckModuleAugmentationElement(decl, isGlobalAugmentation);
                }
                break;
            }
            case SyntaxKind.ExportAssignment:
            case SyntaxKind.ExportDeclaration:
                GrammarErrorOnFirstToken(node, Diagnostics.Exports_and_export_assignments_are_not_permitted_in_module_augmentations);
                break;
            case SyntaxKind.ImportEqualsDeclaration:
                // import a = e.x; in module augmentation is ok, but not import a = require('fs)
                if (Utilities.IsInternalModuleImportEqualsDeclaration(node)) break;
                goto case SyntaxKind.ImportDeclaration;
            case SyntaxKind.ImportDeclaration:
                GrammarErrorOnFirstToken(node, Diagnostics.Imports_are_not_permitted_in_module_augmentations_Consider_moving_them_to_the_enclosing_external_module);
                break;
            case SyntaxKind.BindingElement:
            case SyntaxKind.VariableDeclaration:
            {
                var name = ((INamedDeclaration)node).Name!;
                if (Utilities.IsBindingPattern(name))
                {
                    // PORT-NOTE: the port has no `BindingPattern` class (only the two concrete kinds), and their
                    // `elements` lists do not share a mutable list face; `IEnumerable<T>` covariance is the one type
                    // both convert to.
                    IEnumerable<Node> elements = name.Kind == SyntaxKind.ObjectBindingPattern
                        ? ((ObjectBindingPattern)name).Elements!
                        : ((ArrayBindingPattern)name).Elements!;
                    foreach (var el in elements)
                    {
                        // mark individual names in binding pattern
                        CheckModuleAugmentationElement(el, isGlobalAugmentation);
                    }
                    break;
                }
                goto case SyntaxKind.ClassDeclaration;
            }
            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.EnumDeclaration:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.InterfaceDeclaration:
            case SyntaxKind.ModuleDeclaration:
            case SyntaxKind.TypeAliasDeclaration:
                if (isGlobalAugmentation)
                {
                    return;
                }
                break;
        }
    }
}
