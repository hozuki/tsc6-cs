namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:11603-11609 (getTypeForBindingElementParent)
// ported from: src/compiler/checker.ts:11611-11664 (getRestType)
// ported from: src/compiler/checker.ts:11666-11673 (isGenericTypeWithUndefinedConstraint, getNonUndefinedType)
// ported from: src/compiler/checker.ts:11684-11819 (getFlowTypeOfDestructuring … getBindingElementTypeFromParentType)
// ported from: src/compiler/checker.ts:11829-11837 (isNullOrUndefined, isEmptyArrayLiteral)
// ported from: src/compiler/checker.ts:11844-11980 (getTypeForVariableLikeDeclaration)
// ported from: src/compiler/checker.ts:11982-12157 (isConstructorDeclaredProperty … getWidenedTypeForAssignmentDeclaration)
// ported from: src/compiler/checker.ts:12159-12347 (getJSContainerObjectType … getConstructorDefinedThisAssignmentTypes)
// ported from: src/compiler/checker.ts:12352-12508 (getTypeFromBindingElement … widenTypeForVariableLikeDeclaration)
// ported from: src/compiler/checker.ts:12697-12700 (getAnnotatedAccessorThisParameter, getThisTypeOfDeclaration)
// ported from: src/compiler/checker.ts:12883-12906 (reportCircularityError)
/// <summary>
/// The declaration-to-type block: what a binding element, a variable-like declaration, an assignment declaration or a
/// `this.xxx` assignment is worth, plus the two helpers the type-of-symbol block above it needs.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: several members of the reference's ranges above are already declared by sibling slices and are only
/// <em>called</em> here, never re-declared (a duplicate would be CS0111 and would break the whole partial class):
/// <c>getTypeOfPropertyOfType</c>/<c>getTypeOfPropertyOrIndexSignatureOfType</c>/<c>isTypeAny</c>/<c>isErrorType</c>/
/// <c>getTypeOfEnumMember</c> (Checker.Queries.cs), <c>addOptionality</c> (Checker.Support.cs), and
/// <c>getTypeForDeclarationFromJSDocComment</c>, <c>isParameterOfContextSensitiveSignature</c>,
/// <c>getTypeOfVariableOrParameterOrProperty</c> (and its worker), <c>getAnnotatedAccessorTypeNode</c>,
/// <c>getAnnotatedAccessorType</c>, <c>getTypeOfAccessors</c>, <c>getWriteTypeOfAccessors</c>,
/// <c>getBaseTypeVariableOfClass</c>, <c>getTypeOfFuncClassEnumModule</c> (and its worker) and <c>getTypeOfAlias</c>
/// (Checker.SymbolSupport.cs). The reference's order is preserved below by a pointer comment at each such site.
/// </para>
/// <para>
/// PORT-NOTE: TS's declaration unions (<c>ParameterDeclaration | PropertyDeclaration | …</c>,
/// <c>BindingElementGrandparent</c>, <c>PropertyName[]</c>, <c>Declaration | undefined</c>) are expressed with the
/// port's concrete node classes and <see cref="Node"/>; wherever the reference's union type performs a narrowing the
/// port spells the same narrowing with <c>NodeTests</c>/<c>Utilities</c> predicates and one cast. Those casts are the
/// ones TS gets for free and each is called out on its member.
/// </para>
/// <para>
/// PORT-NOTE: the reference's <c>factory</c> (checker.ts's import of the module-level <c>factory</c> object from
/// factory/nodeFactory.ts) is reached through <see cref="FactoryUtilities.ParseNodeFactory"/>, the one
/// <see cref="NodeFactory"/> instance the port gives the checker and the parser (parser.ts:441). The two instances
/// differ only in <c>NodeFactoryFlags.NoParenthesizerRules</c>, which no <c>create*</c> member used here reads.
/// </para>
/// <para>
/// PORT-NOTE: six <c>utilities.ts</c>/<c>utilitiesPublic.ts</c> members this file calls are not declared anywhere in
/// the port yet, so the scoped build reports <c>CS0117</c> for them and nothing else here: <c>copyEntries</c>
/// (utilities.ts:778 → <c>Utilities.CopyEntries</c>), <c>isPrototypePropertyAssignment</c> (:4294),
/// <c>getEffectiveModifierFlags</c> (:7276, three call sites), <c>isOptionalDeclaration</c> (:11088),
/// <c>getNameFromImportAttribute</c> (:11205) and <c>hasOnlyExpressionInitializer</c> (utilitiesPublic.ts:2578).
/// They are <c>Utilities</c> members by the reference's declaration site, so they are called qualified here and are
/// deliberately not stubbed or inlined.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // Rest/spread types and the non-undefined type (checker.ts:11603-11673)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:11603-11609 — <c>getTypeForBindingElementParent</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>symbol &amp;&amp; getSymbolLinks(symbol).type || …</c> relies on the grandparent of a binding
    /// pattern having no symbol at all; the port's <see cref="GetSymbolOfDeclaration"/> asserts that invariant, so the
    /// absent case is spelled <see cref="Utilities.CanHaveSymbol"/> first — the same answer without the assertion.
    /// PORT-NOTE: TS's <c>BindingElementGrandparent</c> (a binding pattern or a variable-like declaration) is the
    /// port's <see cref="Node"/>; <c>getTypeForVariableLikeDeclaration</c> narrows it exactly as the reference does.
    /// </remarks>
    private IType? GetTypeForBindingElementParent(Node node, CheckMode checkMode)
    {
        if (checkMode != CheckMode.Normal)
        {
            return GetTypeForVariableLikeDeclaration(node, /*includeOptionality*/ false, checkMode);
        }

        var symbol = Utilities.CanHaveSymbol(node) ? GetSymbolOfDeclaration((IDeclaration)node) : null;
        return (symbol is not null ? GetSymbolLinks(symbol).Type : null)
            ?? GetTypeForVariableLikeDeclaration(node, /*includeOptionality*/ false, checkMode);
    }

    /// <summary>ported from: src/compiler/checker.ts:11611-11664 — <c>getRestType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>properties: PropertyName[]</c> is a union of node types, so the port takes
    /// <c>IList&lt;Node&gt;</c>; the reference's <c>emptyArray</c> is the collection expression <c>[]</c> at each use.
    /// </remarks>
    private IType GetRestType(IType source, IList<Node> properties, ISymbol? symbol)
    {
        source = FilterType(source, t => (t.Flags & TypeFlags.Nullable) == 0);
        if ((source.Flags & TypeFlags.Never) != 0)
        {
            return _emptyObjectType;
        }

        if ((source.Flags & TypeFlags.Union) != 0)
        {
            return MapType(source, t => GetRestType(t, properties, symbol))!;
        }

        var omitKeyType = GetUnionType(Core.Map(properties, (p, _) => GetLiteralTypeFromPropertyName(p))!);

        var spreadableProperties = new List<ISymbol>();
        var unspreadableToRestKeys = new List<IType>();

        foreach (var prop in GetPropertiesOfType(source))
        {
            var literalTypeFromProperty = GetLiteralTypeFromProperty(prop, TypeFlags.StringOrNumberLiteralOrUnique);
            if (!IsTypeAssignableTo(literalTypeFromProperty, omitKeyType)
                && (Utilities.GetDeclarationModifierFlagsFromSymbol(prop) & (ModifierFlags.Private | ModifierFlags.Protected)) == 0
                && IsSpreadableProperty(prop))
            {
                spreadableProperties.Add(prop);
            }
            else
            {
                unspreadableToRestKeys.Add(literalTypeFromProperty);
            }
        }

        if (IsGenericObjectType(source) || IsGenericIndexType(omitKeyType))
        {
            if (unspreadableToRestKeys.Count != 0)
            {
                // If the type we're spreading from has properties that cannot
                // be spread into the rest type (e.g. getters, methods), ensure
                // they are explicitly omitted, as they would in the non-generic case.
                omitKeyType = GetUnionType([omitKeyType, .. unspreadableToRestKeys]);
            }

            if ((omitKeyType.Flags & TypeFlags.Never) != 0)
            {
                return source;
            }

            var omitTypeAlias = GetGlobalOmitSymbol();
            if (omitTypeAlias is null)
            {
                return _errorType;
            }

            return GetTypeAliasInstantiation(omitTypeAlias, [source, omitKeyType]);
        }

        var members = Utilities.CreateSymbolTable();
        foreach (var prop in spreadableProperties)
        {
            members[prop.EscapedName!] = GetSpreadSymbol(prop, /*readonly*/ false);
        }

        var result = CreateAnonymousType(symbol, members, [], [], GetIndexInfosOfType(source));
        result.ObjectFlags |= ObjectFlags.ObjectRestType;
        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:11666-11668 — <c>isGenericTypeWithUndefinedConstraint</c>.</summary>
    private bool IsGenericTypeWithUndefinedConstraint(IType type)
        => (type.Flags & TypeFlags.Instantiable) != 0
            && MaybeTypeOfKind(GetBaseConstraintOfType(type) ?? _unknownType, TypeFlags.Undefined);

    /// <summary>ported from: src/compiler/checker.ts:11670-11673 — <c>getNonUndefinedType</c>.</summary>
    private IType GetNonUndefinedType(IType type)
    {
        var typeOrConstraint = SomeType(type, t => IsGenericTypeWithUndefinedConstraint(t))
            ? MapType(type, t => (t.Flags & TypeFlags.Instantiable) != 0 ? GetBaseConstraintOrType(t) : t)!
            : type;
        return GetTypeWithFacts(typeOrConstraint, TypeFacts.NEUndefined);
    }

    // ---------------------------------------------------------------------------------------------
    // Destructuring (checker.ts:11684-11819)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:11684-11687 — <c>getFlowTypeOfDestructuring</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>BindingElement | PropertyAssignment | ShorthandPropertyAssignment | Expression</c> is the
    /// port's <see cref="Node"/> — every member of the union is a node and each helper below narrows with the same
    /// predicates as the reference.
    /// </remarks>
    private IType GetFlowTypeOfDestructuring(Node node, IType declaredType)
    {
        var reference = GetSyntheticElementAccess(node);
        return reference is not null ? GetFlowTypeOfReference(reference, declaredType) : declaredType;
    }

    /// <summary>ported from: src/compiler/checker.ts:11689-11706 — <c>getSyntheticElementAccess</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>setTextRangeWorker</c> is <see cref="Utilities.SetTextRange"/> (utilities.ts:71) and its
    /// <c>factory</c> is <see cref="FactoryUtilities.ParseNodeFactory"/> (see the file header). The <c>flowNode</c>
    /// read and write go through <see cref="IFlowContainer"/> because the reference's <c>canHaveFlowNode</c> guard is
    /// what proves the slot exists.
    /// </remarks>
    private ElementAccessExpression? GetSyntheticElementAccess(Node node)
    {
        var parentAccess = GetParentElementAccess(node);
        if (parentAccess is not null && Utilities.CanHaveFlowNode(parentAccess) && ((IFlowContainer)parentAccess).FlowNode is not null)
        {
            var propName = GetDestructuringPropertyName(node);
            if (propName is not null)
            {
                var literal = Utilities.SetTextRange(FactoryUtilities.ParseNodeFactory.CreateStringLiteral(propName, null), node)!;
                var lhsExpr = Utilities.IsLeftHandSideExpression(parentAccess)
                    ? (Expression)parentAccess
                    : FactoryUtilities.ParseNodeFactory.CreateParenthesizedExpression((Expression)parentAccess);
                var result = Utilities.SetTextRange(FactoryUtilities.ParseNodeFactory.CreateElementAccessExpression(lhsExpr, literal), node)!;
                Utilities.SetParent(literal, result);
                Utilities.SetParent(result, node);
                if (!ReferenceEquals(lhsExpr, parentAccess))
                {
                    Utilities.SetParent(lhsExpr, result);
                }

                result.FlowNode = ((IFlowContainer)parentAccess).FlowNode;
                return result;
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:11708-11721 — <c>getParentElementAccess</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types the result as <c>ElementAccessExpression | Expression | undefined</c>; the port answers
    /// <see cref="Node"/> and lets the one caller read the flow node through <see cref="IFlowContainer"/> after its
    /// own <c>canHaveFlowNode</c> guard. The <c>node.parent.parent</c> walk is asserted non-null (<c>!</c>) because the
    /// reference dereferences it too.
    /// </remarks>
    private Node? GetParentElementAccess(Node node)
    {
        var ancestor = node.Parent!.Parent!;
        switch (ancestor.Kind)
        {
            case SyntaxKind.BindingElement:
            case SyntaxKind.PropertyAssignment:
                return GetSyntheticElementAccess(ancestor);
            case SyntaxKind.ArrayLiteralExpression:
                return GetSyntheticElementAccess(node.Parent!);
            case SyntaxKind.VariableDeclaration:
                return ((VariableDeclaration)ancestor).Initializer;
            case SyntaxKind.BinaryExpression:
                return ((BinaryExpression)ancestor).Right;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:11723-11732 — <c>getDestructuringPropertyName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS casts the parent to <c>BindingPattern | ArrayLiteralExpression</c> and then reads
    /// <c>elements as NodeArray&lt;Node&gt;</c>; C# generics are invariant and the two patterns' element lists have
    /// different element types, so the port keeps the same <c>indexOf</c> with one arm per pattern (the cast to
    /// <c>BindingElement</c> is the one TS's cast to <c>NodeArray&lt;Node&gt;</c> buys).
    /// </remarks>
    private string? GetDestructuringPropertyName(Node node)
    {
        var parent = node.Parent!;
        if (node.Kind == SyntaxKind.BindingElement && parent.Kind == SyntaxKind.ObjectBindingPattern)
        {
            var element = (BindingElement)node;
            return GetLiteralPropertyNameText(element.PropertyName ?? element.Name!);
        }

        if (node.Kind == SyntaxKind.PropertyAssignment || node.Kind == SyntaxKind.ShorthandPropertyAssignment)
        {
            return GetLiteralPropertyNameText(((INamedDeclaration)node).Name!);
        }

        var index = parent.Kind == SyntaxKind.ObjectBindingPattern
            ? ((ObjectBindingPattern)parent).Elements!.IndexOf((BindingElement)node)
            : ((ArrayBindingPattern)parent).Elements!.IndexOf(node);
        return "" + index;
    }

    /// <summary>ported from: src/compiler/checker.ts:11734-11737 — <c>getLiteralPropertyNameText</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>"" + (type as StringLiteralType | NumberLiteralType).value</c> is the port's string arm read
    /// off <see cref="ILiteralType"/> and <see cref="Js.NumberToString"/> for the number arm (the same spelling
    /// <c>getTemplateStringForType</c> uses).
    /// </remarks>
    private string? GetLiteralPropertyNameText(Node name)
    {
        var type = GetLiteralTypeFromPropertyName(name);
        return (type.Flags & (TypeFlags.StringLiteral | TypeFlags.NumberLiteral)) != 0
            ? (type.Flags & TypeFlags.NumberLiteral) != 0
                ? Js.NumberToString((double)((ILiteralType)type).Value!)
                : (string)((ILiteralType)type).Value!
            : null;
    }

    /// <summary>Return the inferred type for a binding element.</summary>
    /// <remarks>ported from: src/compiler/checker.ts:11740-11744 — <c>getTypeForBindingElement</c>.</remarks>
    private IType? GetTypeForBindingElement(BindingElement declaration)
    {
        var checkMode = declaration.DotDotDotToken is not null ? CheckMode.RestBindingElement : CheckMode.Normal;
        var parentType = GetTypeForBindingElementParent(declaration.Parent!.Parent!, checkMode);
        return parentType is not null
            ? GetBindingElementTypeFromParentType(declaration, parentType, /*noTupleBoundsCheck*/ false)
            : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:11746-11819 — <c>getBindingElementTypeFromParentType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>pattern.parent.initializer</c> reads the initializer slot of the union
    /// <c>VariableDeclaration | ParameterDeclaration | BindingElement</c>; the port uses the checker's own
    /// <c>InitializerOfNode</c> (the same switch) and keeps the reference's short-circuit order. The
    /// <c>BindingPattern</c> union is narrowed per arm at its two uses, and <c>declaration.name</c> is that
    /// declaration's own name slot.
    /// </remarks>
    private IType GetBindingElementTypeFromParentType(BindingElement declaration, IType parentType, bool noTupleBoundsCheck)
    {
        // If an any type was inferred for parent, infer that for the binding element
        if (IsTypeAny(parentType))
        {
            return parentType;
        }

        var pattern = declaration.Parent!;
        var patternParentInitializer = InitializerOfNode(pattern.Parent!);
        // Relax null check on ambient destructuring parameters, since the parameters have no implementation and are just documentation
        if (_strictNullChecks && (declaration.Flags & NodeFlags.Ambient) != 0 && Utilities.IsPartOfParameterDeclaration(declaration))
        {
            parentType = GetNonNullableType(parentType);
        }
        // Filter `undefined` from the type we check against if the parent has an initializer and that initializer is not possibly `undefined`
        else if (_strictNullChecks && patternParentInitializer is not null
            && !HasTypeFacts(GetTypeOfInitializer(patternParentInitializer), TypeFacts.EQUndefined))
        {
            parentType = GetTypeWithFacts(parentType, TypeFacts.NEUndefined);
        }

        var accessFlags = AccessFlags.ExpressionPosition | (noTupleBoundsCheck || HasDefaultValue(declaration) ? AccessFlags.AllowMissing : 0);

        IType type;
        if (pattern.Kind == SyntaxKind.ObjectBindingPattern)
        {
            if (declaration.DotDotDotToken is not null)
            {
                parentType = GetReducedType(parentType);
                if ((parentType.Flags & TypeFlags.Unknown) != 0 || !IsValidSpreadType(parentType))
                {
                    Error(declaration, Diagnostics.Rest_types_may_only_be_created_from_object_types);
                    return _errorType;
                }

                var literalMembers = new List<Node>();
                foreach (var element in ((ObjectBindingPattern)pattern).Elements!)
                {
                    if (element.DotDotDotToken is null)
                    {
                        literalMembers.Add(element.PropertyName ?? element.Name!);
                    }
                }

                type = GetRestType(parentType, literalMembers, declaration.Symbol);
            }
            else
            {
                // Use explicitly specified property name ({ p: xxx } form), or otherwise the implied name ({ p } form)
                var name = declaration.PropertyName ?? declaration.Name!;
                var indexType = GetLiteralTypeFromPropertyName(name);
                var declaredType = GetIndexedAccessType(parentType, indexType, accessFlags, name);
                type = GetFlowTypeOfDestructuring(declaration, declaredType);
            }
        }
        else
        {
            // This elementType will be used if the specific property corresponding to this index is not
            // present (aka the tuple element property). This call also checks that the parentType is in
            // fact an iterable or array (depending on target language).
            var elementType = CheckIteratedTypeOrElementType(
                IterationUse.Destructuring | (declaration.DotDotDotToken is not null ? 0 : IterationUse.PossiblyOutOfBounds),
                parentType,
                _undefinedType,
                pattern);
            var index = ((ArrayBindingPattern)pattern).Elements!.IndexOf(declaration);
            if (declaration.DotDotDotToken is not null)
            {
                // If the parent is a tuple type, the rest element has a tuple type of the
                // remaining tuple element types. Otherwise, the rest element has an array type with same
                // element type as the parent type.
                var baseConstraint = MapType(parentType, t => (t.Flags & TypeFlags.InstantiableNonPrimitive) != 0 ? GetBaseConstraintOrType(t) : t)!;
                type = EveryType(baseConstraint, t => IsTupleType(t))
                    ? MapType(baseConstraint, t => SliceTupleType((ITupleTypeReference)t, index))!
                    : CreateArrayType(elementType);
            }
            else if (IsArrayLikeType(parentType))
            {
                var indexType = GetNumberLiteralType(index);
                var declaredType = GetIndexedAccessTypeOrUndefined(parentType, indexType, accessFlags, declaration.Name) ?? _errorType;
                type = GetFlowTypeOfDestructuring(declaration, declaredType);
            }
            else
            {
                type = elementType;
            }
        }

        if (declaration.Initializer is null)
        {
            return type;
        }

        if (Utilities.GetEffectiveTypeAnnotationNode(Utilities.WalkUpBindingElementsAndPatterns(declaration)) is not null)
        {
            // In strict null checking mode, if a default value of a non-undefined type is specified, remove
            // undefined from the final type.
            return _strictNullChecks && !HasTypeFacts(CheckDeclarationInitializer(declaration, CheckMode.Normal), TypeFacts.IsUndefined)
                ? GetNonUndefinedType(type)
                : type;
        }

        return WidenTypeInferredFromInitializer(
            declaration,
            GetUnionType([GetNonUndefinedType(type), CheckDeclarationInitializer(declaration, CheckMode.Normal)], UnionReduction.Subtype));
    }

    // ---------------------------------------------------------------------------------------------
    // The JSDoc/null/optionality helpers (checker.ts:11821-11841)
    // ---------------------------------------------------------------------------------------------

    // getTypeForDeclarationFromJSDocComment (checker.ts:11821-11827) is declared in Checker.SymbolSupport.cs:1130 and
    // is called here, never re-declared.

    /// <summary>ported from: src/compiler/checker.ts:11829-11832 — <c>isNullOrUndefined</c>.</summary>
    private bool IsNullOrUndefined(Expression node)
    {
        var expr = Utilities.SkipParentheses(node, /*excludeJSDocTypeAssertions*/ true);
        return expr.Kind == SyntaxKind.NullKeyword
            || expr.Kind == SyntaxKind.Identifier && ReferenceEquals(GetResolvedSymbol((Identifier)expr), _undefinedSymbol);
    }

    /// <summary>ported from: src/compiler/checker.ts:11834-11837 — <c>isEmptyArrayLiteral</c>.</summary>
    private static bool IsEmptyArrayLiteral(Expression node)
    {
        var expr = Utilities.SkipParentheses(node, /*excludeJSDocTypeAssertions*/ true);
        return expr.Kind == SyntaxKind.ArrayLiteralExpression && ((ArrayLiteralExpression)expr).Elements!.Count == 0;
    }

    // addOptionality (checker.ts:11839-11841) is declared in Checker.Support.cs:215 and is called here, never
    // re-declared.

    // ---------------------------------------------------------------------------------------------
    // The type of a variable-like declaration (checker.ts:11844-12490)
    // ---------------------------------------------------------------------------------------------

    /// <summary>Return the inferred type for a variable, parameter, or property declaration.</summary>
    /// <remarks>
    /// ported from: src/compiler/checker.ts:11844-11980 — <c>getTypeForVariableLikeDeclaration</c>.
    /// <para>
    /// PORT-NOTE: TS's parameter is the six-way declaration union (plus, at the
    /// <c>getTypeForBindingElementParent</c> call site, a binding pattern), so the port takes <see cref="Node"/> and
    /// narrows with the reference's own predicates; every cast in the body is the narrowing TS's union types give it
    /// for free. PORT-NOTE: the reference reads <c>declaration.name</c> twice off that union; the port hoists the same
    /// slot into <c>declarationName</c> in one place because <c>JSDocPropertyLikeTag</c> is the only face without
    /// <see cref="INamedDeclaration"/> and the two reads are adjacent and side-effect free.
    /// </para>
    /// </remarks>
    private IType? GetTypeForVariableLikeDeclaration(Node declaration, bool includeOptionality, CheckMode checkMode)
    {
        // A variable declared in a for..in statement is of type string, or of type keyof T when the
        // right hand expression is of a type parameter type.
        if (NodeTests.IsVariableDeclaration(declaration) && declaration.Parent!.Parent!.Kind == SyntaxKind.ForInStatement)
        {
            var indexType = GetIndexType(GetNonNullableTypeIfNeeded(
                CheckExpression(((ForInStatement)declaration.Parent!.Parent!).Expression!, /*checkMode*/ checkMode)));
            return (indexType.Flags & (TypeFlags.TypeParameter | TypeFlags.Index)) != 0 ? GetExtractStringType(indexType) : _stringType;
        }

        if (NodeTests.IsVariableDeclaration(declaration) && declaration.Parent!.Parent!.Kind == SyntaxKind.ForOfStatement)
        {
            // checkRightHandSideOfForOf will return undefined if the for-of expression type was
            // missing properties/signatures required to get its iteratedType (like
            // [Symbol.iterator] or next). This may be because we accessed properties from anyType,
            // or it may have led to an error inside getElementTypeOfIterable.
            var forOfStatement = (ForOfStatement)declaration.Parent!.Parent!;
            return CheckRightHandSideOfForOf(forOfStatement) ?? _anyType;
        }

        if (Utilities.IsBindingPattern(declaration.Parent!))
        {
            return GetTypeForBindingElement((BindingElement)declaration);
        }

        var declarationName = declaration switch
        {
            INamedDeclaration named => named.Name,
            JSDocPropertyLikeTag tag => tag.Name,
            _ => null,
        };

        var isProperty = (NodeTests.IsPropertyDeclaration(declaration) && !Utilities.HasAccessorModifier(declaration))
            || NodeTests.IsPropertySignature(declaration)
            || NodeTests.IsJSDocPropertyTag(declaration);
        var isOptional = includeOptionality && Utilities.IsOptionalDeclaration((IDeclaration)declaration);

        // Use type from type annotation if one is present
        var declaredType = TryGetTypeFromEffectiveTypeNode(declaration);
        if (Utilities.IsCatchClauseVariableDeclarationOrBindingElement((IDeclaration)declaration))
        {
            if (declaredType is not null)
            {
                // If the catch clause is explicitly annotated with any or unknown, accept it, otherwise error.
                return IsTypeAny(declaredType) || ReferenceEquals(declaredType, _unknownType) ? declaredType : _errorType;
            }

            // If the catch clause is not explicitly annotated, treat it as though it were explicitly
            // annotated with unknown or any, depending on useUnknownInCatchVariables.
            return _useUnknownInCatchVariables ? _unknownType : _anyType;
        }

        if (declaredType is not null)
        {
            return AddOptionality(declaredType, isProperty, isOptional);
        }

        if (
            (_noImplicitAny || Utilities.IsInJSFile(declaration)) &&
            NodeTests.IsVariableDeclaration(declaration) && declarationName is not null && !Utilities.IsBindingPattern(declarationName) &&
            (GetCombinedModifierFlagsCached((IDeclaration)declaration) & ModifierFlags.Export) == 0 && (declaration.Flags & NodeFlags.Ambient) == 0
        )
        {
            var initializer = ((VariableDeclaration)declaration).Initializer;
            // If --noImplicitAny is on or the declaration is in a Javascript file,
            // use control flow tracked 'any' type for non-ambient, non-exported var or let variables with no
            // initializer or a 'null' or 'undefined' initializer.
            if ((GetCombinedNodeFlagsCached(declaration) & NodeFlags.Constant) == 0 && (initializer is null || IsNullOrUndefined(initializer)))
            {
                return _autoType;
            }

            // Use control flow tracked 'any[]' type for non-ambient, non-exported variables with an empty array
            // literal initializer.
            if (initializer is not null && IsEmptyArrayLiteral(initializer))
            {
                return _autoArrayType;
            }
        }

        if (NodeTests.IsParameter(declaration))
        {
            var parameter = (ParameterDeclaration)declaration;
            if (parameter.Symbol is null)
            {
                // parameters of function types defined in JSDoc in TS files don't have symbols
                return null;
            }

            var func = parameter.Parent!;
            // For a parameter of a set accessor, use the type of the get accessor if one is present
            if (func.Kind == SyntaxKind.SetAccessor && HasBindableName((IDeclaration)func))
            {
                var getter = Utilities.GetDeclarationOfKind<Node>(GetSymbolOfDeclaration((IDeclaration)parameter.Parent!), SyntaxKind.GetAccessor);
                if (getter is not null)
                {
                    var getterSignature = GetSignatureFromDeclaration(getter);
                    var thisParameter = GetAccessorThisParameter(func);
                    if (thisParameter is not null && ReferenceEquals(parameter, thisParameter))
                    {
                        // Use the type from the *getter*
                        Debug.Assert(((ParameterDeclaration)thisParameter).Type is null);
                        return GetTypeOfSymbol(getterSignature.ThisParameter!);
                    }

                    return GetReturnTypeOfSignature(getterSignature);
                }
            }

            var parameterTypeOfTypeTag = GetParameterTypeOfTypeTag(func, parameter);
            if (parameterTypeOfTypeTag is not null) return parameterTypeOfTypeTag;
            // Use contextual parameter type if one is available
            var type = parameter.Symbol.EscapedName == InternalSymbolName.This.Value
                ? GetContextualThisParameterType(func)
                : GetContextuallyTypedParameterType(parameter);
            if (type is not null)
            {
                return AddOptionality(type, /*isProperty*/ false, isOptional);
            }
        }

        // Use the type of the initializer expression if one is present and the declaration is
        // not a parameter of a contextually typed function
        if (Utilities.HasOnlyExpressionInitializer(declaration) && InitializerOfNode(declaration) is not null)
        {
            if (Utilities.IsInJSFile(declaration) && !NodeTests.IsParameter(declaration))
            {
                var containerObjectType = GetJSContainerObjectType(
                    declaration,
                    GetSymbolOfDeclaration((IDeclaration)declaration),
                    Utilities.GetDeclaredExpandoInitializer(declaration));
                if (containerObjectType is not null)
                {
                    return containerObjectType;
                }
            }

            var initializerType = WidenTypeInferredFromInitializer(declaration, CheckDeclarationInitializer(declaration, checkMode));
            return AddOptionality(initializerType, isProperty, isOptional);
        }

        if (NodeTests.IsPropertyDeclaration(declaration) && (_noImplicitAny || Utilities.IsInJSFile(declaration)))
        {
            var property = (PropertyDeclaration)declaration;
            // We have a property declaration with no type annotation or initializer, in noImplicitAny mode or a .js file.
            // Use control flow analysis of this.xxx assignments in the constructor or static block to determine the type of the property.
            if (!Utilities.HasStaticModifier(declaration))
            {
                var constructor = Utilities.FindConstructorDeclaration(property.Parent!);
                var type = constructor is not null ? GetFlowTypeInConstructor(property.Symbol!, constructor)
                    : (Utilities.GetEffectiveModifierFlags(declaration) & ModifierFlags.Ambient) != 0 ? GetTypeOfPropertyInBaseClass(property.Symbol!)
                    : null;
                return type is not null ? AddOptionality(type, /*isProperty*/ true, isOptional) : null;
            }
            else
            {
                var staticBlocks = Core.Filter<IClassElement>(((ClassDeclaration)property.Parent!).Members, e => NodeTests.IsClassStaticBlockDeclaration((Node)e));
                var type = staticBlocks is not null && staticBlocks.Count != 0 ? GetFlowTypeInStaticBlocks(property.Symbol!, staticBlocks)
                    : (Utilities.GetEffectiveModifierFlags(declaration) & ModifierFlags.Ambient) != 0 ? GetTypeOfPropertyInBaseClass(property.Symbol!)
                    : null;
                return type is not null ? AddOptionality(type, /*isProperty*/ true, isOptional) : null;
            }
        }

        if (NodeTests.IsJsxAttribute(declaration))
        {
            // if JSX attribute doesn't have initializer, by default the attribute will have boolean value of true.
            // I.e <Elem attr /> is sugar for <Elem attr={true} />
            return _trueType;
        }

        // If the declaration specifies a binding pattern and is not a parameter of a contextually
        // typed function, use the type implied by the binding pattern
        if (declarationName is not null && Utilities.IsBindingPattern(declarationName))
        {
            return GetTypeFromBindingPattern(declarationName, /*includePatternInType*/ false, /*reportErrors*/ true);
        }

        // No type specified and nothing can be inferred
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:11982-11999 — <c>isConstructorDeclaredProperty</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>links.isConstructorDeclaredProperty === undefined</c> presence test is the port's
    /// <c>bool?</c> slot on <c>SymbolLinks</c> (Model/SymbolLinks.cs:170); the result is read as a <c>bool</c>.
    /// </remarks>
    internal bool IsConstructorDeclaredProperty(ISymbol symbol)
    {
        // A property is considered a constructor declared property when all declaration sites are this.xxx assignments,
        // when no declaration sites have JSDoc type annotations, and when at least one declaration site is in the body of
        // a class constructor.
        if (symbol.ValueDeclaration is not null && NodeTests.IsBinaryExpression((Node)symbol.ValueDeclaration))
        {
            var links = GetSymbolLinks(symbol);
            if (links.IsConstructorDeclaredProperty is null)
            {
                links.IsConstructorDeclaredProperty = false;
                links.IsConstructorDeclaredProperty = GetDeclaringConstructor(symbol) is not null && Core.Every(symbol.Declarations, (declaration, _) =>
                {
                    var declarationNode = (Node)declaration;
                    return NodeTests.IsBinaryExpression(declarationNode) &&
                        IsPossiblyAliasedThisProperty((BinaryExpression)declarationNode) &&
                        (declarationNode.Kind != SyntaxKind.ElementAccessExpression ||
                            Utilities.IsStringOrNumericLiteralLike(((ElementAccessExpression)declarationNode).ArgumentExpression!)) &&
                        GetAnnotatedTypeForAssignmentDeclaration(/*declaredType*/ null, declarationNode, symbol, declaration) is null;
                });
            }

            return links.IsConstructorDeclaredProperty.Value;
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:12001-12007 — <c>isAutoTypedProperty</c>.</summary>
    private bool IsAutoTypedProperty(ISymbol symbol)
    {
        // A property is auto-typed when its declaration has no type annotation or initializer and we're in
        // noImplicitAny mode or a .js file.
        var declaration = symbol.ValueDeclaration is not null ? (Node)symbol.ValueDeclaration : null;
        return declaration is not null && NodeTests.IsPropertyDeclaration(declaration) && Utilities.GetEffectiveTypeAnnotationNode(declaration) is null &&
            ((PropertyDeclaration)declaration).Initializer is null && (_noImplicitAny || Utilities.IsInJSFile(declaration));
    }

    /// <summary>ported from: src/compiler/checker.ts:12009-12019 — <c>getDeclaringConstructor</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>ConstructorDeclaration</c> result face is the port's <see cref="Node"/> (the container the
    /// <c>getThisContainer</c> walk returns); the caller only passes it to members that take a node.
    /// </remarks>
    internal Node? GetDeclaringConstructor(ISymbol symbol)
    {
        if (symbol.Declarations is null)
        {
            return null;
        }

        foreach (var declaration in symbol.Declarations)
        {
            var container = Utilities.GetThisContainer((Node)declaration, /*includeArrowFunctions*/ false, /*includeClassComputedPropertyName*/ false);
            if (container is not null && (container.Kind == SyntaxKind.Constructor || IsJSConstructor(container)))
            {
                return container;
            }
        }

        return null;
    }

    /// <summary>Create a synthetic property access flow node after the last statement of the file.</summary>
    /// <remarks>
    /// ported from: src/compiler/checker.ts:12022-12036 — <c>getFlowTypeFromCommonJSExport</c>.
    /// <para>
    /// PORT-NOTE: TS's <c>isAccessExpression(d) &amp;&amp; isModuleExportsAccessExpression(d.expression)</c> reads the
    /// access expression's <c>expression</c> slot, which the port spells with one switch over the two concrete
    /// classes; the <c>factory</c> is <see cref="FactoryUtilities.ParseNodeFactory"/> (see the file header).
    /// </para>
    /// </remarks>
    private IType GetFlowTypeFromCommonJSExport(ISymbol symbol)
    {
        var file = Utilities.GetSourceFileOfNode((Node)symbol.Declarations![0])!;
        var accessName = Utilities.UnescapeLeadingUnderscores(symbol.EscapedName!);
        var areAllModuleExports = Core.Every(symbol.Declarations, (d, _) =>
        {
            var declarationNode = (Node)d;
            return Utilities.IsInJSFile(declarationNode) && Utilities.IsAccessExpression(declarationNode) && Utilities.IsModuleExportsAccessExpression(
                declarationNode is PropertyAccessExpression access ? access.Expression! : ((ElementAccessExpression)declarationNode).Expression!);
        });
        var reference = areAllModuleExports
            ? FactoryUtilities.ParseNodeFactory.CreatePropertyAccessExpression(
                FactoryUtilities.ParseNodeFactory.CreatePropertyAccessExpression(
                    FactoryUtilities.ParseNodeFactory.CreateIdentifier("module"),
                    FactoryUtilities.ParseNodeFactory.CreateIdentifier("exports")),
                accessName)
            : FactoryUtilities.ParseNodeFactory.CreatePropertyAccessExpression(
                FactoryUtilities.ParseNodeFactory.CreateIdentifier("exports"),
                accessName);
        if (areAllModuleExports)
        {
            Utilities.SetParent(((PropertyAccessExpression)reference.Expression!).Expression, reference.Expression);
        }

        Utilities.SetParent(reference.Expression, reference);
        Utilities.SetParent(reference, file);
        reference.FlowNode = file.EndFlowNode;
        return GetFlowTypeOfReference(reference, _autoType, _undefinedType);
    }

    /// <summary>ported from: src/compiler/checker.ts:12038-12057 — <c>getFlowTypeInStaticBlocks</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares the second parameter <c>readonly ClassStaticBlockDeclaration[]</c>; the class's
    /// <c>members</c> slot is the union face <c>NodeArray&lt;IClassElement&gt;</c> in the port, so the parameter keeps
    /// that element type and each surviving element is cast to the class the filter selected
    /// (<c>isClassStaticBlockDeclaration</c>).
    /// The <c>factory</c> is <see cref="FactoryUtilities.ParseNodeFactory"/> (see the file header).
    /// </remarks>
    private IType? GetFlowTypeInStaticBlocks(ISymbol symbol, IList<IClassElement> staticBlocks)
    {
        object accessName = Core.StartsWith(symbol.EscapedName!, "__#")
            ? FactoryUtilities.ParseNodeFactory.CreatePrivateIdentifier(symbol.EscapedName!.Split("@")[1])
            : Utilities.UnescapeLeadingUnderscores(symbol.EscapedName!);
        foreach (var staticBlockNode in staticBlocks)
        {
            var staticBlock = (ClassStaticBlockDeclaration)staticBlockNode;
            var reference = FactoryUtilities.ParseNodeFactory.CreatePropertyAccessExpression(
                FactoryUtilities.ParseNodeFactory.CreateThis(),
                accessName);
            Utilities.SetParent(reference.Expression, reference);
            Utilities.SetParent(reference, staticBlock);
            reference.FlowNode = staticBlock.ReturnFlowNode;
            var flowType = GetFlowTypeOfProperty(reference, symbol);
            if (_noImplicitAny && (ReferenceEquals(flowType, _autoType) || ReferenceEquals(flowType, _autoArrayType)))
            {
                Error((Node)symbol.ValueDeclaration!, Diagnostics.Member_0_implicitly_has_an_1_type, SymbolToString(symbol), TypeToString(flowType));
            }

            // We don't infer a type if assignments are only null or undefined.
            if (EveryType(flowType, t => IsNullableType(t)))
            {
                continue;
            }

            return ConvertAutoToAny(flowType);
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:12059-12073 — <c>getFlowTypeInConstructor</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>ConstructorDeclaration</c> parameter is the port's <see cref="Node"/> (what
    /// <c>GetDeclaringConstructor</c> answers). The <c>factory</c> is
    /// <see cref="FactoryUtilities.ParseNodeFactory"/> (see the file header).
    /// </remarks>
    private IType? GetFlowTypeInConstructor(ISymbol symbol, Node constructor)
    {
        object accessName = Core.StartsWith(symbol.EscapedName!, "__#")
            ? FactoryUtilities.ParseNodeFactory.CreatePrivateIdentifier(symbol.EscapedName!.Split("@")[1])
            : Utilities.UnescapeLeadingUnderscores(symbol.EscapedName!);
        var reference = FactoryUtilities.ParseNodeFactory.CreatePropertyAccessExpression(
            FactoryUtilities.ParseNodeFactory.CreateThis(),
            accessName);
        Utilities.SetParent(reference.Expression, reference);
        Utilities.SetParent(reference, constructor);
        reference.FlowNode = ((ClassStaticBlockDeclaration)constructor).ReturnFlowNode;
        var flowType = GetFlowTypeOfProperty(reference, symbol);
        if (_noImplicitAny && (ReferenceEquals(flowType, _autoType) || ReferenceEquals(flowType, _autoArrayType)))
        {
            Error((Node)symbol.ValueDeclaration!, Diagnostics.Member_0_implicitly_has_an_1_type, SymbolToString(symbol), TypeToString(flowType));
        }

        // We don't infer a type if assignments are only null or undefined.
        return EveryType(flowType, t => IsNullableType(t)) ? null : ConvertAutoToAny(flowType);
    }

    /// <summary>ported from: src/compiler/checker.ts:12075-12081 — <c>getFlowTypeOfProperty</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>prop?.valueDeclaration &amp;&amp; (…) &amp;&amp; getTypeOfPropertyInBaseClass(prop) || undefinedType</c>
    /// yields the base-class type itself when every guard holds; the port names that intermediate value and keeps the
    /// same short-circuit order, so <c>getTypeOfPropertyInBaseClass</c> is still only called when the guards pass.
    /// </remarks>
    private IType GetFlowTypeOfProperty(Node reference, ISymbol? prop)
    {
        var baseType = prop?.ValueDeclaration is not null
            && (!IsAutoTypedProperty(prop) || (Utilities.GetEffectiveModifierFlags((Node)prop.ValueDeclaration) & ModifierFlags.Ambient) != 0)
            ? GetTypeOfPropertyInBaseClass(prop)
            : null;
        var initialType = baseType ?? _undefinedType;
        return GetFlowTypeOfReference(reference, _autoType, initialType);
    }

    /// <summary>ported from: src/compiler/checker.ts:12083-12157 — <c>getWidenedTypeForAssignmentDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>ResolutionDeclaration</c> walk builds <c>expression</c> as
    /// <c>BinaryExpression | CallExpression | Expression | undefined</c>; the port declares it <c>Expression?</c> and
    /// spells the three assignments with one cast each (the union's own narrowing). <c>copyEntries</c> is the
    /// <c>utilities.ts:778</c> member, so it is <c>Utilities.CopyEntries</c> (unlanded; see the slice report).
    /// </remarks>
    internal IType GetWidenedTypeForAssignmentDeclaration(ISymbol symbol, ISymbol? resolvedSymbol = null)
    {
        // function/class/{} initializers are themselves containers, so they won't merge in the same way as other initializers
        var container = Utilities.GetAssignedExpandoInitializer(symbol.ValueDeclaration is not null ? (Node)symbol.ValueDeclaration : null);
        if (container is not null)
        {
            var tag = Utilities.IsInJSFile(container) ? Utilities.GetJSDocTypeTag(container) : null;
            if (tag?.TypeExpression is not null)
            {
                return GetTypeFromTypeNode(tag.TypeExpression);
            }

            var containerObjectType = symbol.ValueDeclaration is not null
                ? GetJSContainerObjectType((Node)symbol.ValueDeclaration, symbol, container)
                : null;
            return containerObjectType ?? GetWidenedLiteralType(CheckExpressionCached(container));
        }

        IType? type = null;
        var definedInConstructor = false;
        var definedInMethod = false;
        // We use control flow analysis to determine the type of the property if the property qualifies as a constructor
        // declared property and the resulting control flow type isn't just undefined or null.
        if (IsConstructorDeclaredProperty(symbol))
        {
            type = GetFlowTypeInConstructor(symbol, GetDeclaringConstructor(symbol)!);
        }

        if (type is null)
        {
            List<IType>? types = null;
            if (symbol.Declarations is not null)
            {
                IType? jsdocType = null;
                foreach (var declaration in symbol.Declarations)
                {
                    var declarationNode = (Node)declaration;
                    var expression = NodeTests.IsBinaryExpression(declarationNode) || NodeTests.IsCallExpression(declarationNode) ? (Expression)declarationNode
                        : Utilities.IsAccessExpression(declarationNode) ? NodeTests.IsBinaryExpression(declarationNode.Parent!) ? (Expression)declarationNode.Parent!
                        : (Expression)declarationNode
                        : null;
                    if (expression is null)
                    {
                        continue; // Non-assignment declaration merged in (eg, an Identifier to mark the thing as a namespace) - skip over it and pull type info from elsewhere
                    }

                    var kind = Utilities.IsAccessExpression(expression)
                        ? Utilities.GetAssignmentDeclarationPropertyAccessKind(expression)
                        : Utilities.GetAssignmentDeclarationKind(expression);
                    if (kind == AssignmentDeclarationKind.ThisProperty || NodeTests.IsBinaryExpression(expression) && IsPossiblyAliasedThisProperty((BinaryExpression)expression, kind))
                    {
                        if (IsDeclarationInConstructor(expression))
                        {
                            definedInConstructor = true;
                        }
                        else
                        {
                            definedInMethod = true;
                        }
                    }

                    if (!NodeTests.IsCallExpression(expression))
                    {
                        jsdocType = GetAnnotatedTypeForAssignmentDeclaration(jsdocType, expression, symbol, declaration);
                    }

                    if (jsdocType is null)
                    {
                        (types ??= []).Add(NodeTests.IsBinaryExpression(expression) || NodeTests.IsCallExpression(expression)
                            ? GetInitializerTypeFromAssignmentDeclaration(symbol, resolvedSymbol, expression, kind)
                            : _neverType);
                    }
                }

                type = jsdocType;
            }

            if (type is null)
            {
                if (Core.Length(types) == 0)
                {
                    return _errorType; // No types from any declarations :(
                }

                var constructorTypes = definedInConstructor && symbol.Declarations is not null
                    ? GetConstructorDefinedThisAssignmentTypes(types!, symbol.Declarations)
                    : null;
                // use only the constructor types unless they were only assigned null | undefined (including widening variants)
                if (definedInMethod)
                {
                    var propType = GetTypeOfPropertyInBaseClass(symbol);
                    if (propType is not null)
                    {
                        (constructorTypes ??= []).Add(propType);
                        definedInConstructor = true;
                    }
                }

                var sourceTypes = Core.Some(constructorTypes, t => (t.Flags & ~TypeFlags.Nullable) != 0) ? constructorTypes : types; // TODO: GH#18217
                type = GetUnionType(sourceTypes!);
            }
        }

        var widened = GetWidenedType(AddOptionality(type, /*isProperty*/ false, definedInMethod && !definedInConstructor));
        if (symbol.ValueDeclaration is not null && Utilities.IsInJSFile((Node)symbol.ValueDeclaration)
            && ReferenceEquals(FilterType(widened, t => (t.Flags & ~TypeFlags.Nullable) != 0), _neverType))
        {
            ReportImplicitAny(symbol.ValueDeclaration!, _anyType);
            return _anyType;
        }

        return widened;
    }

    /// <summary>ported from: src/compiler/checker.ts:12159-12178 — <c>getJSContainerObjectType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>init: Expression | undefined</c> is the port's <c>Expression?</c> and the walk's
    /// <c>decl</c> stays a <see cref="Node"/> — the two <c>parent</c> steps are the ones the reference takes.
    /// </remarks>
    private IType? GetJSContainerObjectType(Node decl, ISymbol symbol, Expression? init)
    {
        if (!Utilities.IsInJSFile(decl) || init is null || !NodeTests.IsObjectLiteralExpression(init) || ((ObjectLiteralExpression)init).Properties!.Count != 0)
        {
            return null;
        }

        var exports = Utilities.CreateSymbolTable();
        while (NodeTests.IsBinaryExpression(decl) || NodeTests.IsPropertyAccessExpression(decl))
        {
            var s = GetSymbolOfNode(decl);
            if (s?.Exports is { Count: > 0 })
            {
                MergeSymbolTable(exports, s.Exports);
            }

            decl = NodeTests.IsBinaryExpression(decl) ? decl.Parent! : decl.Parent!.Parent!;
        }

        var containerSymbol = GetSymbolOfNode(decl);
        if (containerSymbol?.Exports is { Count: > 0 })
        {
            MergeSymbolTable(exports, containerSymbol.Exports);
        }

        var type = CreateAnonymousType(symbol, exports, [], [], []);
        type.ObjectFlags |= ObjectFlags.JSLiteral;
        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:12180-12205 — <c>getAnnotatedTypeForAssignmentDeclaration</c>.</summary>
    private IType? GetAnnotatedTypeForAssignmentDeclaration(IType? declaredType, Node expression, ISymbol symbol, IDeclaration declaration)
    {
        var typeNode = Utilities.GetEffectiveTypeAnnotationNode(expression.Parent!);
        if (typeNode is not null)
        {
            var type = GetWidenedType(GetTypeFromTypeNode(typeNode));
            if (declaredType is null)
            {
                return type;
            }
            else if (!IsErrorType(declaredType) && !IsErrorType(type) && !IsTypeIdenticalTo(declaredType, type))
            {
                ErrorNextVariableOrPropertyDeclarationMustHaveSameType(/*firstDeclaration*/ null, declaredType, declaration, type);
            }
        }

        if (symbol.Parent?.ValueDeclaration is not null)
        {
            var possiblyAnnotatedSymbol = GetFunctionExpressionParentSymbolOrSymbol(symbol.Parent);
            if (possiblyAnnotatedSymbol.ValueDeclaration is not null)
            {
                var annotationTypeNode = Utilities.GetEffectiveTypeAnnotationNode((Node)possiblyAnnotatedSymbol.ValueDeclaration);
                if (annotationTypeNode is not null)
                {
                    var annotationSymbol = GetPropertyOfType(GetTypeFromTypeNode(annotationTypeNode), symbol.EscapedName);
                    if (annotationSymbol is not null)
                    {
                        return GetNonMissingTypeOfSymbol(annotationSymbol);
                    }
                }
            }
        }

        return declaredType;
    }

    /// <summary>If we don't have an explicit JSDoc type, get the type from the initializer.</summary>
    /// <remarks>
    /// ported from: src/compiler/checker.ts:12208-12322 — <c>getInitializerTypeFromAssignmentDeclaration</c>.
    /// <para>
    /// PORT-NOTE: TS's <c>expression: BinaryExpression | CallExpression</c> is the port's <see cref="Node"/> and each
    /// arm narrows with the reference's predicate. The <c>expression.left.expression</c> reads of the
    /// <c>isDirectExport</c> line are one switch over the two access classes (that is the reference's
    /// <c>isPropertyAccessExpression(left) || isElementAccessExpression(left)</c> narrowing), and
    /// <c>tryCast(…, isNamedDeclaration)?.name || …</c> is one <see cref="INamedDeclaration"/> pattern.
    /// <c>createDiagnosticForNode</c> is utilities.ts:2416 and is called as
    /// <see cref="Utilities.CreateDiagnosticForNode"/>.
    /// </para>
    /// </remarks>
    private IType GetInitializerTypeFromAssignmentDeclaration(ISymbol symbol, ISymbol? resolvedSymbol, Node expression, AssignmentDeclarationKind kind)
    {
        if (NodeTests.IsCallExpression(expression))
        {
            if (resolvedSymbol is not null)
            {
                return GetTypeOfSymbol(resolvedSymbol); // This shouldn't happen except under some hopefully forbidden merges of export assignments and object define assignments
            }

            var objectLitType = CheckExpressionCached(((CallExpression)expression).Arguments![2]);
            var valueType = GetTypeOfPropertyOfType(objectLitType, "value");
            if (valueType is not null)
            {
                return valueType;
            }

            var getFunc = GetTypeOfPropertyOfType(objectLitType, "get");
            if (getFunc is not null)
            {
                var getSig = GetSingleCallSignature(getFunc);
                if (getSig is not null)
                {
                    return GetReturnTypeOfSignature(getSig);
                }
            }

            var setFunc = GetTypeOfPropertyOfType(objectLitType, "set");
            if (setFunc is not null)
            {
                var setSig = GetSingleCallSignature(setFunc);
                if (setSig is not null)
                {
                    return GetTypeOfFirstParameterOfSignature(setSig);
                }
            }

            return _anyType;
        }

        var binaryExpression = (BinaryExpression)expression;
        if (ContainsSameNamedThisProperty(binaryExpression.Left!, binaryExpression.Right!))
        {
            return _anyType;
        }

        var left = binaryExpression.Left!;
        var leftTarget = left is PropertyAccessExpression access ? access.Expression : left is ElementAccessExpression elementAccess ? elementAccess.Expression : null;
        var isDirectExport = kind == AssignmentDeclarationKind.ExportsProperty
            && (NodeTests.IsPropertyAccessExpression(left) || NodeTests.IsElementAccessExpression(left))
            && (Utilities.IsModuleExportsAccessExpression(leftTarget!) || (NodeTests.IsIdentifier(leftTarget!) && Utilities.IsExportsIdentifier((Identifier)leftTarget!)));
        var type = resolvedSymbol is not null ? GetTypeOfSymbol(resolvedSymbol)
            : isDirectExport ? GetRegularTypeOfLiteralType(CheckExpressionCached(binaryExpression.Right!))
            : GetWidenedLiteralType(CheckExpressionCached(binaryExpression.Right!));
        if (
            (type.Flags & TypeFlags.Object) != 0 &&
            kind == AssignmentDeclarationKind.ModuleExports &&
            symbol.EscapedName == InternalSymbolName.ExportEquals.Value
        )
        {
            var exportedType = ResolveStructuredTypeMembers((UnionOrIntersectionType)type);
            var members = Utilities.CreateSymbolTable();
            Utilities.CopyEntries(exportedType.Members!, members);
            var initialSize = members.Count;
            if (resolvedSymbol is not null && resolvedSymbol.Exports is null)
            {
                resolvedSymbol.Exports = Utilities.CreateSymbolTable();
            }

            (resolvedSymbol ?? symbol).Exports!.ForEach((s, name) =>
            {
                var exportedMember = members.GetValueOrDefault(name);
                if (exportedMember is not null && !ReferenceEquals(exportedMember, s) && (s.Flags & SymbolFlags.Alias) == 0)
                {
                    if ((s.Flags & SymbolFlags.Value) != 0 && (exportedMember.Flags & SymbolFlags.Value) != 0)
                    {
                        // If the member has an additional value-like declaration, union the types from the two declarations,
                        // but issue an error if they occurred in two different files. The purpose is to support a JS file with
                        // a pattern like:
                        //
                        // module.exports = { a: true };
                        // module.exports.a = 3;
                        //
                        // but we may have a JS file with `module.exports = { a: true }` along with a TypeScript module augmentation
                        // declaring an `export const a: number`. In that case, we issue a duplicate identifier error, because
                        // it's unclear what that's supposed to mean, so it's probably a mistake.
                        if (s.ValueDeclaration is not null && exportedMember.ValueDeclaration is not null && !ReferenceEquals(
                            Utilities.GetSourceFileOfNode((Node)s.ValueDeclaration),
                            Utilities.GetSourceFileOfNode((Node)exportedMember.ValueDeclaration)))
                        {
                            var unescapedName = Utilities.UnescapeLeadingUnderscores(s.EscapedName!);
                            var exportedMemberName = exportedMember.ValueDeclaration is INamedDeclaration { Name: not null } namedMember
                                ? namedMember.Name
                                : (Node)exportedMember.ValueDeclaration;
                            Utilities.AddRelatedInfo(
                                Error((Node)s.ValueDeclaration, Diagnostics.Duplicate_identifier_0, unescapedName),
                                Utilities.CreateDiagnosticForNode(exportedMemberName, Diagnostics._0_was_also_declared_here, unescapedName));
                            Utilities.AddRelatedInfo(
                                Error(exportedMemberName, Diagnostics.Duplicate_identifier_0, unescapedName),
                                Utilities.CreateDiagnosticForNode((Node)s.ValueDeclaration, Diagnostics._0_was_also_declared_here, unescapedName));
                        }

                        var union = CreateSymbol(s.Flags | exportedMember.Flags, name);
                        GetSymbolLinks(union).Type = GetUnionType([GetTypeOfSymbol(s), GetTypeOfSymbol(exportedMember)]);
                        union.ValueDeclaration = exportedMember.ValueDeclaration;
                        union.Declarations = Core.Concatenate(exportedMember.Declarations, s.Declarations) as List<IDeclaration>;
                        members[name] = union;
                    }
                    else
                    {
                        members[name] = MergeSymbol(s, exportedMember);
                    }
                }
                else
                {
                    members[name] = s;
                }
            });
            var result = CreateAnonymousType(
                initialSize != members.Count ? null : exportedType.Symbol, // Only set the type's symbol if it looks to be the same as the original type
                members,
                exportedType.CallSignatures!,
                exportedType.ConstructSignatures!,
                exportedType.IndexInfos!);
            if (initialSize == members.Count)
            {
                if (type.AliasSymbol is not null)
                {
                    result.AliasSymbol = type.AliasSymbol;
                    result.AliasTypeArguments = type.AliasTypeArguments;
                }

                if ((GetObjectFlags(type) & ObjectFlags.Reference) != 0)
                {
                    result.AliasSymbol = ((ITypeReference)type).Symbol;
                    var args = GetTypeArguments((ITypeReference)type);
                    result.AliasTypeArguments = args.Count != 0 ? args : null;
                }
            }

            result.ObjectFlags |= GetPropagatingFlagsOfTypes([type]) | GetObjectFlags(type) & (ObjectFlags.JSLiteral | ObjectFlags.ArrayLiteral | ObjectFlags.ObjectLiteral);
            if (result.Symbol is not null
                && (result.Symbol.Flags & SymbolFlags.Class) != 0
                && ReferenceEquals(type, GetDeclaredTypeOfClassOrInterface(result.Symbol)))
            {
                result.ObjectFlags |= ObjectFlags.IsClassInstanceClone; // Propagate the knowledge that this type is equivalent to the symbol's class instance type
            }

            return result;
        }

        if (IsEmptyArrayLiteralType(type))
        {
            ReportImplicitAny((IDeclaration)expression, _anyArrayType);
            return _anyArrayType;
        }

        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:12324-12328 — <c>containsSameNamedThisProperty</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>forEachChildRecursively</c> is typed <c>&lt;T&gt;(…): T | undefined</c>; the port's is the
    /// <c>object?</c> face (parser.ts:1272) and answers by <c>Js.IsTruthy</c>, so the boolean callback is read back
    /// through <see cref="Js.IsTruthy"/> exactly as the reference's truthiness test does.
    /// </remarks>
    private bool ContainsSameNamedThisProperty(Expression thisProperty, Expression expression)
        => NodeTests.IsPropertyAccessExpression(thisProperty)
            && ((PropertyAccessExpression)thisProperty).Expression!.Kind == SyntaxKind.ThisKeyword
            && Js.IsTruthy(ForEachChildTable.ForEachChildRecursively(expression, (n, _) => IsMatchingReference(thisProperty, n)));

    /// <summary>ported from: src/compiler/checker.ts:12330-12337 — <c>isDeclarationInConstructor</c>.</summary>
    private bool IsDeclarationInConstructor(Expression expression)
    {
        var thisContainer = Utilities.GetThisContainer(expression, /*includeArrowFunctions*/ false, /*includeClassComputedPropertyName*/ false)!;
        // Properties defined in a constructor (or base constructor, or javascript constructor function) don't get undefined added.
        // Function expressions that are assigned to the prototype count as methods.
        return thisContainer.Kind == SyntaxKind.Constructor
            || thisContainer.Kind == SyntaxKind.FunctionDeclaration
            || (thisContainer.Kind == SyntaxKind.FunctionExpression && !Utilities.IsPrototypePropertyAssignment(thisContainer.Parent!));
    }

    /// <summary>ported from: src/compiler/checker.ts:12339-12347 — <c>getConstructorDefinedThisAssignmentTypes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>types.filter((_, i) =&gt; …)</c> reads the index, which the port's element-wise
    /// <c>Core.Filter</c> does not hand out; the port keeps the same order and predicate with an index loop (the
    /// reference allocates a fresh array here too).
    /// </remarks>
    private List<IType> GetConstructorDefinedThisAssignmentTypes(List<IType> types, List<IDeclaration> declarations)
    {
        Debug.Assert(types.Count == declarations.Count);
        var result = new List<IType>();
        for (var i = 0; i < types.Count; i++)
        {
            var declaration = (Node)declarations[i];
            var expression = NodeTests.IsBinaryExpression(declaration) ? declaration
                : declaration.Parent is not null && NodeTests.IsBinaryExpression(declaration.Parent) ? declaration.Parent
                : null;
            if (expression is not null && IsDeclarationInConstructor((Expression)expression))
            {
                result.Add(types[i]);
            }
        }

        return result;
    }

    // Return the type implied by a binding pattern element. This is the type of the initializer of the element if
    // one is present. Otherwise, if the element is itself a binding pattern, it is the type implied by the binding
    // pattern. Otherwise, it is the type any.
    /// <summary>ported from: src/compiler/checker.ts:12352-12371 — <c>getTypeFromBindingElement</c>.</summary>
    private IType GetTypeFromBindingElement(BindingElement element, bool includePatternInType = false, bool reportErrors = false)
    {
        if (element.Initializer is not null)
        {
            // The type implied by a binding pattern is independent of context, so we check the initializer with no
            // contextual type or, if the element itself is a binding pattern, with the type implied by that binding
            // pattern.
            var contextualType = Utilities.IsBindingPattern(element.Name!)
                ? GetTypeFromBindingPattern(element.Name!, /*includePatternInType*/ true, /*reportErrors*/ false)
                : _unknownType;
            return AddOptionality(GetWidenedLiteralTypeForInitializer(element, CheckDeclarationInitializer(element, CheckMode.Normal, contextualType)));
        }

        if (Utilities.IsBindingPattern(element.Name!))
        {
            return GetTypeFromBindingPattern(element.Name!, includePatternInType, reportErrors);
        }

        if (reportErrors && !DeclarationBelongsToPrivateAmbientMember(element))
        {
            ReportImplicitAny(element, _anyType);
        }

        // When we're including the pattern in the type (an indication we're obtaining a contextual type), we
        // use a non-inferrable any type. Inference will never directly infer this type, but it is possible
        // to infer a type that contains it, e.g. for a binding pattern like [foo] or { foo }. In such cases,
        // widening of the binding pattern type substitutes a regular any for the non-inferrable any.
        return includePatternInType ? _nonInferrableAnyType : _anyType;
    }

    // Return the type implied by an object binding pattern
    /// <summary>ported from: src/compiler/checker.ts:12374-12404 — <c>getTypeFromObjectBindingPattern</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>forEach(pattern.elements, e =&gt; …)</c> uses bare <c>return</c> for "continue"; the port's
    /// <c>Core.ForEach</c> answers the first truthy result, so the callback answers <c>null</c> on both exits and the
    /// iteration always runs to the end — the reference's behaviour. The element type is spelled out because
    /// <c>NodeArray&lt;BindingElement&gt;</c> is not a <c>NodeArray&lt;Node&gt;</c> in C#.
    /// </remarks>
    private IType GetTypeFromObjectBindingPattern(ObjectBindingPattern pattern, bool includePatternInType, bool reportErrors)
    {
        var members = Utilities.CreateSymbolTable();
        IndexInfo? stringIndexInfo = null;
        var objectFlags = ObjectFlags.ObjectLiteral | ObjectFlags.ContainsObjectOrArrayLiteral;
        Core.ForEach<BindingElement, object>(pattern.Elements, (e, _) =>
        {
            var name = e.PropertyName ?? e.Name!;
            if (e.DotDotDotToken is not null)
            {
                stringIndexInfo = CreateIndexInfo(_stringType, _anyType, /*isReadonly*/ false);
                return null;
            }

            var exprType = GetLiteralTypeFromPropertyName(name);
            if (!Utilities.IsTypeUsableAsPropertyName(exprType))
            {
                // do not include computed properties in the implied type
                objectFlags |= ObjectFlags.ObjectLiteralPatternWithComputedProperties;
                return null;
            }

            var text = Utilities.GetPropertyNameFromType(exprType);
            var flags = SymbolFlags.Property | (e.Initializer is not null ? SymbolFlags.Optional : 0);
            var symbol = CreateSymbol(flags, text);
            GetSymbolLinks(symbol).Type = GetTypeFromBindingElement(e, includePatternInType, reportErrors);
            members[symbol.EscapedName!] = symbol;
            return null;
        });
        var result = CreateAnonymousType(/*symbol*/ null, members, [], [], stringIndexInfo is not null ? [stringIndexInfo] : []);
        result.ObjectFlags |= objectFlags;
        if (includePatternInType)
        {
            result.Pattern = pattern;
            result.ObjectFlags |= ObjectFlags.ContainsObjectOrArrayLiteral;
        }

        return result;
    }

    // Return the type implied by an array binding pattern
    /// <summary>ported from: src/compiler/checker.ts:12407-12424 — <c>getTypeFromArrayBindingPattern</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>lastElement &amp;&amp; lastElement.kind === SyntaxKind.BindingElement &amp;&amp;
    /// lastElement.dotDotDotToken ? lastElement : undefined</c> narrows to the <c>BindingElement</c> arm; the port
    /// spells that narrowing with the cast the union gave TS. <c>cloneTypeReference</c> answers the
    /// <c>TypeReference</c> face, whose <c>objectFlags</c> slot is read through <see cref="IObjectType"/> (the
    /// generated <c>ITypeReference</c> face does not carry it) — the same object either way.
    /// </remarks>
    private IType GetTypeFromArrayBindingPattern(ArrayBindingPattern pattern, bool includePatternInType, bool reportErrors)
    {
        var elements = pattern.Elements!;
        var lastElement = Core.LastOrUndefined(elements);
        var restElement = lastElement is not null && lastElement.Kind == SyntaxKind.BindingElement && ((BindingElement)lastElement).DotDotDotToken is not null
            ? (BindingElement)lastElement
            : null;
        if (elements.Count == 0 || (elements.Count == 1 && restElement is not null))
        {
            return _languageVersion >= ScriptTarget.ES2015 ? CreateIterableType(_anyType) : _anyArrayType;
        }

        var elementTypes = Core.Map(elements, (e, _) => NodeTests.IsOmittedExpression(e) ? _anyType : GetTypeFromBindingElement((BindingElement)e, includePatternInType, reportErrors))!;
        var minLength = Core.FindLastIndex(elements, (e, _) => !(ReferenceEquals(e, restElement) || NodeTests.IsOmittedExpression(e) || HasDefaultValue(e)), elements.Count - 1) + 1;
        var elementFlags = Core.Map(elements, (e, i) => ReferenceEquals(e, restElement) ? ElementFlags.Rest : i >= minLength ? ElementFlags.Optional : ElementFlags.Required)!;
        var result = (ITypeReference)CreateTupleType(elementTypes, elementFlags);
        if (includePatternInType)
        {
            var clone = CloneTypeReference(result);
            clone.Pattern = pattern;
            ((IObjectType)clone).ObjectFlags |= ObjectFlags.ContainsObjectOrArrayLiteral;
            result = clone;
        }

        return result;
    }

    // Return the type implied by a binding pattern. This is the type implied purely by the binding pattern itself
    // and without regard to its context (i.e. without regard any type annotation or initializer associated with the
    // declaration in which the binding pattern is contained). For example, the implied type of [x, y] is [any, any]
    // and the implied type of { x, y: z = 1 } is { x: any; y: number; }. The type implied by a binding pattern is
    // used as the contextual type of an initializer associated with the binding pattern. Also, for a destructuring
    // parameter with no type annotation or initializer, the type implied by the binding pattern becomes the type of
    // the parameter.
    /// <summary>ported from: src/compiler/checker.ts:12433-12440 — <c>getTypeFromBindingPattern</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>BindingPattern</c> union reaches the two builders through a <c>kind</c> test, which the
    /// port spells the same way with one cast per arm. <c>contextualBindingPatterns</c> (checker.ts:2215) is the
    /// checker-level stack; the port keeps the same push/pop order over the <c>_contextualBindingPatterns</c> slot.
    /// </remarks>
    private IType GetTypeFromBindingPattern(Node pattern, bool includePatternInType = false, bool reportErrors = false)
    {
        if (includePatternInType) _contextualBindingPatterns.Add(pattern);
        var result = pattern.Kind == SyntaxKind.ObjectBindingPattern
            ? GetTypeFromObjectBindingPattern((ObjectBindingPattern)pattern, includePatternInType, reportErrors)
            : GetTypeFromArrayBindingPattern((ArrayBindingPattern)pattern, includePatternInType, reportErrors);
        if (includePatternInType) _contextualBindingPatterns.RemoveAt(_contextualBindingPatterns.Count - 1);
        return result;
    }

    // Return the type associated with a variable, parameter, or property declaration. In the simple case this is the type
    // specified in a type annotation or inferred from an initializer. However, in the case of a destructuring declaration it
    // is a bit more involved. For example:
    //
    //   var [x, s = ""] = [1, "one"];
    //
    // Here, the array literal [1, "one"] is contextually typed by the type [any, string], which is the implied type of the
    // binding pattern [x, s = ""]. Because the contextual type is a tuple type, the resulting type of [1, "one"] is the
    // tuple type [number, string]. Thus, the type inferred for 'x' is number and the type inferred for 's' is string.
    /// <summary>ported from: src/compiler/checker.ts:12451-12453 — <c>getWidenedTypeForVariableLikeDeclaration</c>.</summary>
    internal IType GetWidenedTypeForVariableLikeDeclaration(Node declaration, bool? reportErrors = null)
        => WidenTypeForVariableLikeDeclaration(GetTypeForVariableLikeDeclaration(declaration, /*includeOptionality*/ true, CheckMode.Normal), declaration, reportErrors);

    /// <summary>ported from: src/compiler/checker.ts:12455-12472 — <c>getTypeFromImportAttributes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>forEach(node.elements, attr =&gt; …)</c> is a void callback; the port has no void
    /// <c>Core.ForEach</c>, so the same iteration over the same array is a <c>foreach</c>.
    /// </remarks>
    private IType GetTypeFromImportAttributes(ImportAttributes node)
    {
        var links = GetNodeLinks(node);
        if (links.ResolvedType is null)
        {
            var symbol = CreateSymbol(SymbolFlags.ObjectLiteral, InternalSymbolName.ImportAttributes.Value);
            var members = Utilities.CreateSymbolTable();
            foreach (var attr in node.Elements!)
            {
                var member = CreateSymbol(SymbolFlags.Property, Utilities.GetNameFromImportAttribute(attr));
                member.Parent = symbol;
                GetSymbolLinks(member).Type = CheckImportAttribute(attr);
                GetSymbolLinks(member).Target = member;
                members[member.EscapedName!] = member;
            }

            var type = CreateAnonymousType(symbol, members, [], [], []);
            type.ObjectFlags |= ObjectFlags.ObjectLiteral | ObjectFlags.NonInferrableType;
            links.ResolvedType = type;
        }

        return links.ResolvedType;
    }

    /// <summary>ported from: src/compiler/checker.ts:12474-12478 — <c>isGlobalSymbolConstructor</c>.</summary>
    private bool IsGlobalSymbolConstructor(Node node)
    {
        var symbol = GetSymbolOfNode(node);
        var globalSymbol = GetGlobalESSymbolConstructorTypeSymbol(/*reportErrors*/ false);
        return globalSymbol is not null && symbol is not null && ReferenceEquals(symbol, globalSymbol);
    }

    /// <summary>ported from: src/compiler/checker.ts:12480-12508 — <c>widenTypeForVariableLikeDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types this parameter <c>any</c> (six declaration kinds reach it); the port keeps the
    /// <see cref="Node"/> face its landed callers already pass. <c>type.symbol !== getSymbolOfDeclaration(declaration)</c>
    /// is spelled with the <see cref="Utilities.CanHaveSymbol"/> guard because the port's
    /// <c>getSymbolOfDeclaration</c> asserts the declaration invariant that a non-declaration node cannot meet. The
    /// two reporting helpers (<c>reportErrorsFromWidening</c>/<c>reportImplicitAny</c>, checker.ts:26208/:26103) take
    /// a <c>Declaration</c> in the reference, so the port's <c>IDeclaration</c>-faced members get the same <c>Node</c>
    /// parameter cast at their two call sites.
    /// </remarks>
    internal IType WidenTypeForVariableLikeDeclaration(IType? type, Node declaration, bool? reportErrors = null)
    {
        if (type is not null)
        {
            // TODO: If back compat with pre-3.0/4.0 libs isn't required, remove the following SymbolConstructor special case transforming `symbol` into `unique symbol`
            if ((type.Flags & TypeFlags.ESSymbol) != 0 && IsGlobalSymbolConstructor(declaration.Parent!))
            {
                type = GetESSymbolLikeTypeForNode(declaration);
            }

            if (reportErrors == true)
            {
                ReportErrorsFromWidening((IDeclaration)declaration, type);
            }

            // always widen a 'unique symbol' type if the type was created for a different declaration.
            if ((type.Flags & TypeFlags.UniqueESSymbol) != 0
                && (NodeTests.IsBindingElement(declaration) || TryGetTypeFromEffectiveTypeNode(declaration) is null)
                && !ReferenceEquals(type.Symbol, Utilities.CanHaveSymbol(declaration) ? GetSymbolOfDeclaration((IDeclaration)declaration) : null))
            {
                type = _esSymbolType;
            }

            return GetWidenedType(type);
        }

        // Rest parameters default to type any[], other parameters default to type any
        type = NodeTests.IsParameter(declaration) && ((ParameterDeclaration)declaration).DotDotDotToken is not null ? _anyArrayType : _anyType;

        // Report implicit any errors unless this is a private property within an ambient declaration
        if (reportErrors == true)
        {
            if (!DeclarationBelongsToPrivateAmbientMember(declaration))
            {
                ReportImplicitAny((IDeclaration)declaration, type);
            }
        }

        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:12510-12514 — <c>declarationBelongsToPrivateAmbientMember</c>.</summary>
    private bool DeclarationBelongsToPrivateAmbientMember(Node declaration)
    {
        var root = Utilities.GetRootDeclaration(declaration);
        var memberDeclaration = root.Kind == SyntaxKind.Parameter ? root.Parent! : root;
        return IsPrivateWithinAmbient(memberDeclaration);
    }

    /// <summary>ported from: src/compiler/checker.ts:12516-12521 — <c>tryGetTypeFromEffectiveTypeNode</c>.</summary>
    internal IType? TryGetTypeFromEffectiveTypeNode(Node node)
    {
        var typeNode = Utilities.GetEffectiveTypeAnnotationNode(node);
        if (typeNode is not null)
        {
            return GetTypeFromTypeNode(typeNode);
        }

        return null;
    }

    // getAnnotatedAccessorTypeNode (checker.ts:12674-12690) and getAnnotatedAccessorType (:12692-12695) are declared
    // in Checker.SymbolSupport.cs:1363/:1394 and are only called from here; the reference's order is kept below.

    /// <summary>ported from: src/compiler/checker.ts:12697-12700 — <c>getAnnotatedAccessorThisParameter</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>accessor: AccessorDeclaration</c> is the get/set accessor union and its <c>parameter</c> is a
    /// <c>ParameterDeclaration</c>; the port takes the <see cref="Node"/> face its landed caller
    /// (<c>Checker.TypeSupport.cs:1561</c>, a <c>GetDeclarationOfKind&lt;Node&gt;</c> result) already passes, and reads
    /// <c>symbol</c> off the parameter class the union narrows to.
    /// </remarks>
    internal ISymbol? GetAnnotatedAccessorThisParameter(Node accessor)
    {
        var parameter = GetAccessorThisParameter(accessor);
        return parameter is not null ? ((ParameterDeclaration)parameter).Symbol : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:12702-12704 — <c>getThisTypeOfDeclaration</c>.</summary>
    private IType? GetThisTypeOfDeclaration(Node declaration)
        => GetThisTypeOfSignature(GetSignatureFromDeclaration(declaration));

    /// <summary>ported from: src/compiler/checker.ts:12883-12906 — <c>reportCircularityError</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>(declaration as HasInitializer).initializer</c> narrows the union to the arm that carries
    /// <c>initializer</c>; the port reads that slot off the parameter class, which is the arm the guard leaves
    /// testable after <c>kind !== SyntaxKind.Parameter</c>.
    /// </remarks>
    internal IType ReportCircularityError(ISymbol symbol)
    {
        var declaration = symbol.ValueDeclaration is not null ? (Node)symbol.ValueDeclaration : null;
        // Check if variable has type annotation that circularly references the variable itself
        if (declaration is not null)
        {
            if (Utilities.GetEffectiveTypeAnnotationNode(declaration) is not null)
            {
                Error(declaration, Diagnostics._0_is_referenced_directly_or_indirectly_in_its_own_type_annotation, SymbolToString(symbol));
                return _errorType;
            }

            // Check if variable has initializer that circularly references the variable itself
            if (_noImplicitAny && (declaration.Kind != SyntaxKind.Parameter || ((ParameterDeclaration)declaration).Initializer is not null))
            {
                Error(declaration, Diagnostics._0_implicitly_has_type_any_because_it_does_not_have_a_type_annotation_and_is_referenced_directly_or_indirectly_in_its_own_initializer, SymbolToString(symbol));
            }
        }
        else if ((symbol.Flags & SymbolFlags.Alias) != 0)
        {
            var node = (Node?)GetDeclarationOfAliasSymbol(symbol);
            if (node is not null)
            {
                Error(node, Diagnostics.Circular_definition_of_import_alias_0, SymbolToString(symbol));
            }
        }

        // Circularities could also result from parameters in function expressions that end up
        // having themselves as contextual types following type argument inference. In those cases
        // we have already reported an implicit any error so we don't report anything here.
        return _anyType;
    }
}