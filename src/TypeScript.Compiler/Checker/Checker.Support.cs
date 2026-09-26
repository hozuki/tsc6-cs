using System.Globalization;

namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:2348-2351 (the type-resolution stack's state)
// ported from: src/compiler/checker.ts:11480-11546 (pushTypeResolution … popTypeResolution)
// ported from: src/compiler/checker.ts:11839-11841 (addOptionality)
// ported from: src/compiler/checker.ts:14912-15003 (getTypeParameterFromMappedType … isPartialMappedType)
// ported from: src/compiler/checker.ts:15148-15566 (getConstraintOfType … getReducedApparentType)
// ported from: src/compiler/checker.ts:15811-15880 (getReducedType … isReducibleIntersection)
// ported from: src/compiler/checker.ts:15981-16067 (findIndexInfo … getTypeParametersFromDeclaration)
// ported from: src/compiler/checker.ts:16121-16176 (createTypePredicate … fillMissingTypeArguments)
// ported from: src/compiler/checker.ts:16418-16471 (getThisTypeOfSignature … getUnionOrIntersectionType)
// ported from: src/compiler/checker.ts:16760-16762 (getConstraintDeclaration)
// ported from: src/compiler/checker.ts:16872-16952 (getTypeListId … createDeferredTypeReference)
// ported from: src/compiler/checker.ts:26385-26411 (couldContainTypeVariables, isNonGenericTopLevelType)
// ported from: src/compiler/checker.ts:37696-37715 (isJSConstructor)
// ported from: src/compiler/utilities.ts:625-636 (getDeclarationOfKind)
// ported from: src/compiler/utilities.ts:2788-2882 (isPartOfTypeNode, isPartOfTypeExpressionWithTypeArguments)
// ported from: src/compiler/utilities.ts:5014-5016 (walkUpParenthesizedExpressions)
// ported from: src/compiler/utilities.ts:5063-5069 (isNodeDescendantOf)
// ported from: src/compiler/utilities.ts:7444-7497 (the two tryGetClass* helpers, isExpressionWithTypeArgumentsInClassExtendsClause)
// ported from: src/compiler/utilities.ts:7505-7522 (getFirstIdentifier)
// ported from: src/compiler/utilities.ts:10920-10936 (isNumericLiteralName)
// ported from: src/compiler/utilities.ts:8045-8065 (getDeclarationModifierFlagsFromSymbol)
// ported from: src/compiler/utilities.ts:8275-8277 (getClassLikeDeclarationOfSymbol)
// ported from: src/compiler/utilities.ts:8280-8282 (getObjectFlags)
// ported from: src/compiler/utilities.ts:4780-4782 (getJSDocOverloadTags)
// ported from: src/compiler/utilitiesPublic.ts:1121-1123 (getJSDocClassTag)
/// <summary>
/// The constraint/apparent-type support block: the type-resolution stack that every cyclic type query pushes onto, the
/// mapped-type accessors, the constraint machinery (<c>getConstraintOfType</c>, the base-constraint resolver, the
/// type-parameter defaults), the reduced-type family, the index-info lookups, and the type-predicate/id/key helpers
/// around them.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS declares the type-resolution stack as four <c>var</c>s in the state block of <c>createTypeChecker</c>
/// (checker.ts:2348-2351), i.e. ~9,000 lines above their first user, because the checker's state is hoisted to the top
/// of the closure. The port states the four slots here, next to the only members that touch them, the way
/// <c>Checker.rs</c> states the slots of its own sections; they are <c>readonly</c> (nothing reassigns a buffer —
/// <c>pop</c>/<c>push</c> mutate it) except <c>_resolutionStart</c>, which <c>recursiveTypeRelatedTo</c> and
/// <c>inferTypeArguments</c> save and restore (checker.ts:25027, :37668).
/// </para>
/// <para>
/// PORT-NOTE: <c>resolutionTargets</c> is typed <c>TypeSystemEntity = Type | Symbol | Signature</c> (types.ts:5673),
/// a union the port has no name for and whose arms share no interface, so the buffers hold <c>object</c> and the one
/// member that reads an element — <see cref="ResolutionTargetHasProperty"/> — casts to the face its
/// <see cref="TypeSystemPropertyName"/> selects, exactly as the reference does.
/// </para>
/// <para>
/// PORT-NOTE: the two <c>ObjectFlags</c>-shaped <c>readonly Type[]</c>-taking helpers at the end
/// (<see cref="GetObjectFlags"/>, <see cref="IsPartOfTypeNode"/> and the four small utilities beside them) are
/// <c>utilities.ts</c> functions with no declarer in the port yet that the checker's landed slices call by bare name;
/// they are declared here as members of <see cref="Checker"/> and the <c>utilities.ts</c> slice replaces them with
/// <c>Utilities.*</c> copies at cutover. Each carries its own PORT-NOTE naming the upstream file and range.
/// </para>
/// <para>
/// PORT-NOTE: <see cref="GetResolvedBaseConstraint"/> is TS's one non-trivial case of nested <em>mutually recursive</em>
/// closures: <c>getImmediateBaseConstraint</c>, <c>getBaseConstraint</c> and <c>computeBaseConstraint</c> close over
/// the <c>stack</c> local and call each other. The port states them as C# local functions of the same member, which
/// capture the same local and may be mutually recursive, so no extra state and no field appears.
/// </para>
/// <para>
/// PORT-NOTE: the four <c>objectFlags</c>-carrying faces (<c>SubstitutionType</c>, <c>IntrinsicType</c>,
/// <c>ObjectType</c>, <c>UnionOrIntersectionType</c>) and the two <c>resolvedBaseConstraint</c>-carrying faces
/// (<c>InstantiableType</c>, <c>UnionOrIntersectionType</c>) each hold one slot that TS reads off the
/// <c>ObjectFlagsType</c> / <c>InstantiableType | UnionOrIntersectionType</c> unions. C# has no common view of those
/// faces, so the port switches over the interfaces that declare the slot — the same treatment
/// <c>Checker.Instantiation.cs</c>'s <c>OrObjectFlags</c> uses for the writer side.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // The type-resolution stack (checker.ts:2348-2351, 11480-11546)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:2348 — <c>resolutionTargets</c>.</summary>
    /// <remarks>PORT-NOTE: TS's element type is the <c>TypeSystemEntity</c> union; see the class remarks.</remarks>
    internal readonly List<object> _resolutionTargets = [];

    /// <summary>ported from: src/compiler/checker.ts:2349 — <c>resolutionResults</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>boolean[]</c> holds a <c>true</c> per pushed entry (<c>pushTypeResolution</c> pushes
    /// <c>/*items*/ true</c>) and flips the ones from the cycle's start to <c>false</c>; the buffer is popped by
    /// <c>popTypeResolution</c>.
    /// </remarks>
    internal readonly List<bool> _resolutionResults = [];

    /// <summary>ported from: src/compiler/checker.ts:2350 — <c>resolutionPropertyNames</c>.</summary>
    internal readonly List<TypeSystemPropertyName> _resolutionPropertyNames = [];

    /// <summary>ported from: src/compiler/checker.ts:2351 — <c>resolutionStart</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference saves and restores this around an inner query
    /// (<c>const saveResolutionStart = resolutionStart; … resolutionStart = resolutionTargets.length; …</c>,
    /// checker.ts:25027-25072 and :37668-37680) so a nested cycle search does not look at the outer entries. It stays
    /// settable here for those two callers.
    /// </remarks>
    internal int _resolutionStart;

    /// <summary>ported from: src/compiler/checker.ts:11487-11501 — <c>pushTypeResolution</c>.</summary>
    /// <remarks>
    /// <para>
    /// Push an entry on the type resolution stack. If an entry with the given target and the given property name is
    /// already on the stack, and no entries in between already have a type, then a circularity has occurred. In this
    /// case, the result values of the existing entry and all entries pushed after it are changed to false, and the
    /// value false is returned. Otherwise, the new entry is just pushed onto the stack, and true is returned. In order
    /// to see if the same query has already been done before, the target object and the propertyName both must match
    /// the one passed in.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>target</c> is TS's <c>TypeSystemEntity</c> union (<c>Type | Symbol | Signature</c>); the port
    /// takes <c>object</c> (see the class remarks). The callers pass a <c>Signature</c> (Checker.Queries.cs's
    /// <c>getReturnTypeOfSignature</c>), a <c>Symbol</c> (its <c>tryGetDeclaredTypeOfSymbol</c>) and a
    /// <c>TypeReference</c> (Checker.TypeReferences.cs's <c>getTypeArguments</c>).
    /// </para>
    /// </remarks>
    internal bool PushTypeResolution(object target, TypeSystemPropertyName propertyName)
    {
        var resolutionCycleStartIndex = FindResolutionCycleStartIndex(target, propertyName);
        if (resolutionCycleStartIndex >= 0)
        {
            // A cycle was found
            var length = _resolutionTargets.Count;
            for (var i = resolutionCycleStartIndex; i < length; i++)
            {
                _resolutionResults[i] = false;
            }

            return false;
        }

        _resolutionTargets.Add(target);
        _resolutionResults.Add(true);
        _resolutionPropertyNames.Add(propertyName);
        return true;
    }

    /// <summary>ported from: src/compiler/checker.ts:11503-11513 — <c>findResolutionCycleStartIndex</c>.</summary>
    internal int FindResolutionCycleStartIndex(object target, TypeSystemPropertyName propertyName)
    {
        for (var i = _resolutionTargets.Count - 1; i >= _resolutionStart; i--)
        {
            if (ResolutionTargetHasProperty(_resolutionTargets[i], _resolutionPropertyNames[i]))
            {
                return -1;
            }

            if (ReferenceEquals(_resolutionTargets[i], target) && _resolutionPropertyNames[i] == propertyName)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>ported from: src/compiler/checker.ts:11515-11538 — <c>resolutionTargetHasProperty</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS casts the target to the face each property name selects (<c>target as Symbol</c>,
    /// <c>target as InterfaceType</c>, <c>target as Signature</c>, <c>target as Type</c>,
    /// <c>target as TypeReference</c>, <c>target as ParameterDeclaration</c>) and tests the slot for truthiness. The
    /// port casts the same way and writes the truthiness tests out (<c>is not null</c> for the reference slots,
    /// <c>!!</c> over a <c>bool | undefined</c> slot becoming <c>is not null</c>).
    /// </remarks>
    internal bool ResolutionTargetHasProperty(object target, TypeSystemPropertyName propertyName)
    {
        switch (propertyName)
        {
            case TypeSystemPropertyName.Type:
                return GetSymbolLinks((ISymbol)target).Type is not null;
            case TypeSystemPropertyName.DeclaredType:
                return GetSymbolLinks((ISymbol)target).DeclaredType is not null;
            case TypeSystemPropertyName.ResolvedBaseConstructorType:
                return ((IInterfaceType)target).ResolvedBaseConstructorType is not null;
            case TypeSystemPropertyName.ResolvedReturnType:
                return ((ISignature)target).ResolvedReturnType is not null;
            case TypeSystemPropertyName.ImmediateBaseConstraint:
                return ((IType)target).ImmediateBaseConstraint is not null;
            case TypeSystemPropertyName.ResolvedTypeArguments:
                return ((ITypeReference)target).ResolvedTypeArguments is not null;
            case TypeSystemPropertyName.ResolvedBaseTypes:
                return ((IInterfaceType)target).BaseTypesResolved is not null;
            case TypeSystemPropertyName.WriteType:
                return GetSymbolLinks((ISymbol)target).WriteType is not null;
            case TypeSystemPropertyName.ParameterInitializerContainsUndefined:
                return GetNodeLinks((ParameterDeclaration)target).ParameterInitializerContainsUndefined is not null;
        }

        Debug.AssertNever(propertyName);
        throw new InvalidOperationException("unreachable");
    }

    /// <summary>ported from: src/compiler/checker.ts:11543-11547 — <c>popTypeResolution</c>.</summary>
    /// <remarks>
    /// Pop an entry from the type resolution stack and return its associated result value. The result value will be
    /// true if no circularities were detected, or false if a circularity was found.
    /// </remarks>
    internal bool PopTypeResolution()
    {
        _resolutionTargets.RemoveAt(_resolutionTargets.Count - 1);
        _resolutionPropertyNames.RemoveAt(_resolutionPropertyNames.Count - 1);
        var last = _resolutionResults.Count - 1;
        var result = _resolutionResults[last];
        _resolutionResults.RemoveAt(last);
        return result;
    }

    // ---------------------------------------------------------------------------------------------
    // The optionality of a declaration (checker.ts:11839-11841)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:11839-11841 — <c>addOptionality</c>.</summary>
    internal IType AddOptionality(IType type, bool isProperty = false, bool isOptional = true)
        => _strictNullChecks && isOptional ? GetOptionalType(type, isProperty) : type;

    // ---------------------------------------------------------------------------------------------
    // The mapped-type accessors (checker.ts:14912-15003)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:14912-14915 — <c>getTypeParameterFromMappedType</c>.</summary>
    internal ITypeParameter GetTypeParameterFromMappedType(IMappedType type)
        => type.TypeParameter ??= GetDeclaredTypeOfTypeParameter(GetSymbolOfDeclaration(type.Declaration!.TypeParameter!));

    /// <summary>ported from: src/compiler/checker.ts:14917-14920 — <c>getConstraintTypeFromMappedType</c>.</summary>
    internal IType GetConstraintTypeFromMappedType(IMappedType type)
        => type.ConstraintType ??= GetConstraintOfTypeParameter(GetTypeParameterFromMappedType(type)) ?? _errorType;

    /// <summary>ported from: src/compiler/checker.ts:14922-14926 — <c>getNameTypeFromMappedType</c>.</summary>
    internal IType? GetNameTypeFromMappedType(IMappedType type)
        => type.Declaration!.NameType is not null
            ? type.NameType ??= InstantiateType(GetTypeFromTypeNode(type.Declaration.NameType), (TypeMapper?)type.Mapper)
            : null;

    /// <summary>ported from: src/compiler/checker.ts:14928-14933 — <c>getTemplateTypeFromMappedType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>type.declaration.type ? instantiateType(…) : errorType</c> assigns the result straight to
    /// the non-optional <c>templateType</c>; the port keeps the reference's <c>||</c>/assignment pair as <c>??=</c> and
    /// asserts the non-null <c>InstantiateType</c> result.
    /// </remarks>
    internal IType GetTemplateTypeFromMappedType(IMappedType type)
        => type.TemplateType ??= type.Declaration!.Type is not null
            ? InstantiateType(AddOptionality(GetTypeFromTypeNode(type.Declaration.Type), /*isProperty*/ true, (GetMappedTypeModifiers(type) & MappedTypeModifiers.IncludeOptional) != 0), (TypeMapper?)type.Mapper)!
            : _errorType;

    /// <summary>ported from: src/compiler/checker.ts:14935-14937 — <c>getConstraintDeclarationForMappedType</c>.</summary>
    internal ITypeNode? GetConstraintDeclarationForMappedType(IMappedType type)
        => Utilities.GetEffectiveConstraintOfTypeParameter(type.Declaration!.TypeParameter!);

    /// <summary>ported from: src/compiler/checker.ts:14939-14943 — <c>isMappedTypeWithKeyofConstraintDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>! // TODO: GH#18217</c> on the constraint declaration means "a mapped type always
    /// has one"; the port states that as <see cref="Debug.CheckDefined"/> instead of a null-forgiving operator, so a
    /// violation is caught rather than propagated.
    /// </remarks>
    internal bool IsMappedTypeWithKeyofConstraintDeclaration(IMappedType type)
    {
        var constraintDeclaration = Debug.CheckDefined(GetConstraintDeclarationForMappedType(type), "a mapped type always has a constraint declaration");
        return constraintDeclaration.Kind == SyntaxKind.TypeOperator && ((TypeOperatorNode)constraintDeclaration).Operator == SyntaxKind.KeyOfKeyword;
    }

    /// <summary>ported from: src/compiler/checker.ts:14945-14964 — <c>getModifiersTypeFromMappedType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's two <c>type.modifiersType = instantiateType(…)</c> assignments write a non-optional slot from
    /// an optional-returning call; the port asserts them. The <c>constraint &amp;&amp; … ? … : constraint</c> and
    /// <c>extendedConstraint &amp;&amp; … ? … : unknownType</c> pairs are JS truthiness over a possibly-undefined value,
    /// hence <c>is not null</c> here.
    /// </remarks>
    internal IType GetModifiersTypeFromMappedType(IMappedType type)
    {
        if (type.ModifiersType is null)
        {
            if (IsMappedTypeWithKeyofConstraintDeclaration(type))
            {
                // If the constraint declaration is a 'keyof T' node, the modifiers type is T. We check
                // AST nodes here because, when T is a non-generic type, the logic below eagerly resolves
                // 'keyof T' to a literal union type and we can't recover T from that type.
                type.ModifiersType = InstantiateType(GetTypeFromTypeNode(((TypeOperatorNode)GetConstraintDeclarationForMappedType(type)!).Type), (TypeMapper?)type.Mapper)!;
            }
            else
            {
                // Otherwise, get the declared constraint type, and if the constraint type is a type parameter,
                // get the constraint of that type parameter. If the resulting type is an indexed type 'keyof T',
                // the modifiers type is T. Otherwise, the modifiers type is unknown.
                var declaredType = (IMappedType)GetTypeFromMappedTypeNode(type.Declaration!);
                var constraint = GetConstraintTypeFromMappedType(declaredType);
                var extendedConstraint = constraint is not null && (constraint.Flags & TypeFlags.TypeParameter) != 0 ? GetConstraintOfTypeParameter((ITypeParameter)constraint) : constraint;
                type.ModifiersType = extendedConstraint is not null && (extendedConstraint.Flags & TypeFlags.Index) != 0
                    ? InstantiateType(((IIndexType)extendedConstraint).Type, (TypeMapper?)type.Mapper)!
                    : _unknownType;
            }
        }

        return type.ModifiersType;
    }

    /// <summary>ported from: src/compiler/checker.ts:14966-14970 — <c>getMappedTypeModifiers</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's two <c>: 0</c> arms give <c>MappedTypeModifiers</c> a zero value; the port spells it
    /// <c>(MappedTypeModifiers)0</c> because the enum declares no <c>None</c> member (CheckersEnums.cs, deviating from
    /// the reference's own list only in spelling).
    /// </remarks>
    internal static MappedTypeModifiers GetMappedTypeModifiers(IMappedType type)
    {
        var declaration = type.Declaration!;
        return (declaration.ReadonlyToken is not null
                ? declaration.ReadonlyToken.Kind == SyntaxKind.MinusToken ? MappedTypeModifiers.ExcludeReadonly : MappedTypeModifiers.IncludeReadonly
                : (MappedTypeModifiers)0)
            | (declaration.QuestionToken is not null
                ? declaration.QuestionToken.Kind == SyntaxKind.MinusToken ? MappedTypeModifiers.ExcludeOptional : MappedTypeModifiers.IncludeOptional
                : (MappedTypeModifiers)0);
    }

    /// <summary>ported from: src/compiler/checker.ts:14974-14977 — <c>getMappedTypeOptionality</c>.</summary>
    // Return -1, 0, or 1, where -1 means optionality is stripped (i.e. -?), 0 means optionality is unchanged, and 1 means
    // optionality is added (i.e. +?).
    internal static int GetMappedTypeOptionality(IMappedType type)
    {
        var modifiers = GetMappedTypeModifiers(type);
        return (modifiers & MappedTypeModifiers.ExcludeOptional) != 0 ? -1 : (modifiers & MappedTypeModifiers.IncludeOptional) != 0 ? 1 : 0;
    }

    /// <summary>ported from: src/compiler/checker.ts:14982-14990 — <c>getCombinedMappedTypeOptionality</c>.</summary>
    // Return -1, 0, or 1, for stripped, unchanged, or added optionality respectively. When a homomorphic mapped type doesn't
    // modify optionality, recursively consult the optionality of the type being mapped over to see if it strips or adds optionality.
    // For intersections, return -1 or 1 when all constituents strip or add optionality, otherwise return 0.
    internal int GetCombinedMappedTypeOptionality(IType type)
    {
        if ((GetObjectFlags(type) & ObjectFlags.Mapped) != 0)
        {
            var optionality = GetMappedTypeOptionality((IMappedType)type);
            return optionality != 0 ? optionality : GetCombinedMappedTypeOptionality(GetModifiersTypeFromMappedType((IMappedType)type));
        }

        if ((type.Flags & TypeFlags.Intersection) != 0)
        {
            var types = ((IIntersectionType)type).Types!;
            var optionality = GetCombinedMappedTypeOptionality(types[0]);
            return Core.Every(types, (t, i) => i == 0 || GetCombinedMappedTypeOptionality(t) == optionality) ? optionality : 0;
        }

        return 0;
    }

    /// <summary>ported from: src/compiler/checker.ts:14993-14995 — <c>isPartialMappedType</c>.</summary>
    internal bool IsPartialMappedType(IType type)
        => (GetObjectFlags(type) & ObjectFlags.Mapped) != 0 && (GetMappedTypeModifiers((IMappedType)type) & MappedTypeModifiers.IncludeOptional) != 0;

    // ---------------------------------------------------------------------------------------------
    // The constraint machinery (checker.ts:15148-15566)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:15148-15153 — <c>getConstraintOfType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>InstantiableType | UnionOrIntersectionType</c> union; the port takes
    /// <c>IType</c>, the honest upper bound of both, and narrows on the flags exactly as the reference does.
    /// </remarks>
    internal IType? GetConstraintOfType(IType type)
        => (type.Flags & TypeFlags.TypeParameter) != 0 ? GetConstraintOfTypeParameter((ITypeParameter)type)
            : (type.Flags & TypeFlags.IndexedAccess) != 0 ? GetConstraintOfIndexedAccess((IIndexedAccessType)type)
            : (type.Flags & TypeFlags.Conditional) != 0 ? GetConstraintOfConditionalType((IConditionalType)type)
            : GetBaseConstraintOfType(type);

    /// <summary>ported from: src/compiler/checker.ts:15155-15157 — <c>getConstraintOfTypeParameter</c>.</summary>
    internal IType? GetConstraintOfTypeParameter(ITypeParameter typeParameter)
        => HasNonCircularBaseConstraint(typeParameter) ? GetConstraintFromTypeParameter(typeParameter) : null;

    /// <summary>ported from: src/compiler/checker.ts:16840-16865 — <c>getConstraintFromTypeParameter</c>.</summary>
    /// <remarks>
    /// <para>
    /// This is a worker function. Use <c>getConstraintOfTypeParameter</c> which guards against circular constraints.
    /// </para>
    /// <para>
    /// PORT-NOTE: the target arm calls <c>getConstraintOfTypeParameter</c> and not this worker, which is the reference's
    /// own recursion guard. The declaration's <c>parent.parent</c> read reaches the node class through a cast to
    /// <see cref="Node"/>, because the <c>ITypeNode</c> face carries no <c>parent</c> slot (§4.2); the
    /// <c>any</c> → <c>stringNumberSymbolType</c>/<c>unknownType</c> substitution is the reference's, typo included, and
    /// <c>isErrorType</c> is the ported Checker.Queries.cs member.
    /// </para>
    /// </remarks>
    internal IType? GetConstraintFromTypeParameter(ITypeParameter typeParameter)
    {
        if (typeParameter.Constraint is null)
        {
            if (typeParameter.Target is not null)
            {
                var targetConstraint = GetConstraintOfTypeParameter(typeParameter.Target);
                typeParameter.Constraint = targetConstraint is not null
                    ? InstantiateType(targetConstraint, (TypeMapper?)typeParameter.Mapper)
                    : _noConstraintType;
            }
            else
            {
                var constraintDeclaration = GetConstraintDeclaration(typeParameter);
                if (constraintDeclaration is null)
                {
                    typeParameter.Constraint = GetInferredTypeParameterConstraint(typeParameter) ?? _noConstraintType;
                }
                else
                {
                    var type = GetTypeFromTypeNode(constraintDeclaration);
                    if ((type.Flags & TypeFlags.Any) != 0 && !IsErrorType(type)) // Allow errorType to propegate to keep downstream errors suppressed
                    {
                        // use stringNumberSymbolType as the base constraint for mapped type key constraints (unknown isn;t assignable to that, but `any` was),
                        // use unknown otherwise
                        type = ((Node)constraintDeclaration).Parent!.Parent!.Kind == SyntaxKind.MappedType ? _stringNumberSymbolType : _unknownType;
                    }

                    typeParameter.Constraint = type;
                }
            }
        }

        return ReferenceEquals(typeParameter.Constraint, _noConstraintType) ? null : typeParameter.Constraint;
    }

    /// <summary>ported from: src/compiler/checker.ts:15159-15162 — <c>isConstMappedType</c>.</summary>
    internal bool IsConstMappedType(IMappedType type, int depth)
    {
        var typeVariable = GetHomomorphicTypeVariable(type);
        return typeVariable is not null && IsConstTypeVariable(typeVariable, depth);
    }

    /// <summary>ported from: src/compiler/checker.ts:15164-15174 — <c>isConstTypeVariable</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>getElementTypes(type)…findIndex(…, type.target.elementFlags[i] &amp; Variadic &amp;&amp; …)</c>
    /// reads <c>target</c> off the tuple-type face; the port narrows to <see cref="ITupleType"/> for the
    /// <c>elementFlags</c> list and to <see cref="ITupleTypeReference"/> for <c>getElementTypes</c>. The
    /// <c>some(symbol?.declarations, d =&gt; hasSyntacticModifier(d, Const))</c> arm passes TS's <c>Declaration</c>
    /// union element, which the port's declaration interfaces reach through a cast to <see cref="Node"/> — the same
    /// cast <c>Utilities.HasSyntacticModifier</c>'s other callers make.
    /// </remarks>
    internal bool IsConstTypeVariable(IType? type, int depth = 0)
        => depth < 5 && type is not null && (
            (type.Flags & TypeFlags.TypeParameter) != 0 && Core.Some(type.Symbol?.Declarations, d => Utilities.HasSyntacticModifier((Node)d, ModifierFlags.Const))
            || (type.Flags & TypeFlags.UnionOrIntersection) != 0 && Core.Some(((IUnionOrIntersectionType)type).Types, t => IsConstTypeVariable(t, depth))
            || (type.Flags & TypeFlags.IndexedAccess) != 0 && IsConstTypeVariable(((IIndexedAccessType)type).ObjectType, depth + 1)
            || (type.Flags & TypeFlags.Conditional) != 0 && IsConstTypeVariable(GetConstraintOfConditionalType((IConditionalType)type), depth + 1)
            || (type.Flags & TypeFlags.Substitution) != 0 && IsConstTypeVariable(((ISubstitutionType)type).BaseType, depth)
            || (GetObjectFlags(type) & ObjectFlags.Mapped) != 0 && IsConstMappedType((IMappedType)type, depth)
            || IsGenericTupleType(type) && Core.FindIndex(GetElementTypes((ITupleTypeReference)type)!, (t, i) => (((ITupleType)((ITypeReference)type).Target!).ElementFlags![i] & ElementFlags.Variadic) != 0 && IsConstTypeVariable(t, depth)) >= 0);

    /// <summary>ported from: src/compiler/checker.ts:15176-15178 — <c>getConstraintOfIndexedAccess</c>.</summary>
    internal IType? GetConstraintOfIndexedAccess(IIndexedAccessType type)
        => HasNonCircularBaseConstraint(type) ? GetConstraintFromIndexedAccess(type) : null;

    /// <summary>ported from: src/compiler/checker.ts:15180-15183 — <c>getSimplifiedTypeOrConstraint</c>.</summary>
    internal IType? GetSimplifiedTypeOrConstraint(IType type)
    {
        var simplified = GetSimplifiedType(type, /*writing*/ false);
        return !ReferenceEquals(simplified, type) ? simplified : GetConstraintOfType(type);
    }

    /// <summary>ported from: src/compiler/checker.ts:15185-15203 — <c>getConstraintFromIndexedAccess</c>.</summary>
    internal IType? GetConstraintFromIndexedAccess(IIndexedAccessType type)
    {
        if (IsMappedTypeGenericIndexedAccess(type))
        {
            // For indexed access types of the form { [P in K]: E }[X], where K is non-generic and X is generic,
            // we substitute an instantiation of E where P is replaced with X.
            return SubstituteIndexedMappedType((IMappedType)type.ObjectType!, type.IndexType!);
        }

        var indexConstraint = GetSimplifiedTypeOrConstraint(type.IndexType!);
        if (indexConstraint is not null && !ReferenceEquals(indexConstraint, type.IndexType))
        {
            var indexedAccess = GetIndexedAccessTypeOrUndefined(type.ObjectType!, indexConstraint, type.AccessFlags);
            if (indexedAccess is not null)
            {
                return indexedAccess;
            }
        }

        var objectConstraint = GetSimplifiedTypeOrConstraint(type.ObjectType!);
        if (objectConstraint is not null && !ReferenceEquals(objectConstraint, type.ObjectType))
        {
            return GetIndexedAccessTypeOrUndefined(objectConstraint, type.IndexType!, type.AccessFlags);
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:15205-15216 — <c>getDefaultConstraintOfConditionalType</c>.</summary>
    internal IType GetDefaultConstraintOfConditionalType(IConditionalType type)
    {
        if (type.ResolvedDefaultConstraint is null)
        {
            // An `any` branch of a conditional type would normally be viral - specifically, without special handling here,
            // a conditional type with a single branch of type `any` would be assignable to anything, since it's constraint would simplify to
            // just `any`. This result is _usually_ unwanted - so instead here we elide an `any` branch from the constraint type,
            // in effect treating `any` like `never` rather than `unknown` in this location.
            var trueConstraint = GetInferredTrueTypeFromConditionalType(type);
            var falseConstraint = GetFalseTypeFromConditionalType(type);
            type.ResolvedDefaultConstraint = IsTypeAny(trueConstraint) ? falseConstraint : IsTypeAny(falseConstraint) ? trueConstraint : GetUnionType([trueConstraint, falseConstraint]);
        }

        return type.ResolvedDefaultConstraint!;
    }

    /// <summary>ported from: src/compiler/checker.ts:15218-15247 — <c>getConstraintOfDistributiveConditionalType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>resolvedConstraintOfDistributive</c> is <c>Type | false</c> (types.ts:6951) and
    /// the reader is <c>if (… !== undefined) return … || undefined</c>. The port keeps the boxed sentinel in the
    /// <c>object?</c> slot: <c>is not null</c> distinguishes "computed as false" from "not computed", and the
    /// <c>as IType</c> read turns the boxed <c>false</c> into <c>null</c>, which is TS's <c>|| undefined</c>
    /// (Model/ConditionalType.cs's remarks).
    /// </remarks>
    internal IType? GetConstraintOfDistributiveConditionalType(IConditionalType type)
    {
        if (type.ResolvedConstraintOfDistributive is not null)
        {
            return type.ResolvedConstraintOfDistributive as IType;
        }

        // Check if we have a conditional type of the form 'T extends U ? X : Y', where T is a constrained
        // type parameter. If so, create an instantiation of the conditional type where T is replaced
        // with its constraint. We do this because if the constraint is a union type it will be distributed
        // over the conditional type and possibly reduced. For example, 'T extends undefined ? never : T'
        // removes 'undefined' from T.
        // We skip returning a distributive constraint for a restrictive instantiation of a conditional type
        // as the constraint for all type params (check type included) have been replace with `unknown`, which
        // is going to produce even more false positive/negative results than the distribute constraint already does.
        // Please note: the distributive constraint is a kludge for emulating what a negated type could to do filter
        // a union - once negated types exist and are applied to the conditional false branch, this "constraint"
        // likely doesn't need to exist.
        if (type.Root!.IsDistributive && !ReferenceEquals(type.RestrictiveInstantiation, type))
        {
            var simplified = GetSimplifiedType(type.CheckType!, /*writing*/ false);
            var constraint = ReferenceEquals(simplified, type.CheckType) ? GetConstraintOfType(simplified) : simplified;
            if (constraint is not null && !ReferenceEquals(constraint, type.CheckType))
            {
                var instantiated = GetConditionalTypeInstantiation(type, PrependTypeMapping(type.Root!.CheckType!, constraint, (TypeMapper?)type.Mapper), /*forConstraint*/ true, /*aliasSymbol*/ null, /*aliasTypeArguments*/ null);
                if ((instantiated.Flags & TypeFlags.Never) == 0)
                {
                    type.ResolvedConstraintOfDistributive = instantiated;
                    return instantiated;
                }
            }
        }

        type.ResolvedConstraintOfDistributive = false;
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:15249-15251 — <c>getConstraintFromConditionalType</c>.</summary>
    internal IType GetConstraintFromConditionalType(IConditionalType type)
        => GetConstraintOfDistributiveConditionalType(type) ?? GetDefaultConstraintOfConditionalType(type);

    /// <summary>ported from: src/compiler/checker.ts:15253-15255 — <c>getConstraintOfConditionalType</c>.</summary>
    internal IType? GetConstraintOfConditionalType(IConditionalType type)
        => HasNonCircularBaseConstraint(type) ? GetConstraintFromConditionalType(type) : null;

    /// <summary>ported from: src/compiler/checker.ts:15257-15295 — <c>getEffectiveConstraintOfIntersection</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>constraints = append(constraints, …)</c> grows a <c>Type[]</c>; the port keeps the
    /// reference's shape with <see cref="Core.Append"/> and the <c>as List&lt;IType&gt;</c> the landed callers of the
    /// same helper use (Checker.TypeReferences.cs:719), because <c>getIntersectionType</c> takes a
    /// <c>List&lt;IType&gt;</c>.
    /// </remarks>
    internal IType? GetEffectiveConstraintOfIntersection(List<IType> types, bool targetIsUnion)
    {
        List<IType>? constraints = null;
        var hasDisjointDomainType = false;
        foreach (var t in types)
        {
            if ((t.Flags & TypeFlags.Instantiable) != 0)
            {
                // We keep following constraints as long as we have an instantiable type that is known
                // not to be circular or infinite (hence we stop on index access types).
                var constraint = GetConstraintOfType(t);
                while (constraint is not null && (constraint.Flags & (TypeFlags.TypeParameter | TypeFlags.Index | TypeFlags.Conditional)) != 0)
                {
                    constraint = GetConstraintOfType(constraint);
                }

                if (constraint is not null)
                {
                    constraints = Core.Append(constraints, constraint) as List<IType>;
                    if (targetIsUnion)
                    {
                        constraints = Core.Append(constraints, t) as List<IType>;
                    }
                }
            }
            else if ((t.Flags & TypeFlags.DisjointDomains) != 0 || IsEmptyAnonymousObjectType(t))
            {
                hasDisjointDomainType = true;
            }
        }

        // If the target is a union type or if we are intersecting with types belonging to one of the
        // disjoint domains, we may end up producing a constraint that hasn't been examined before.
        if (constraints is not null && (targetIsUnion || hasDisjointDomainType))
        {
            if (hasDisjointDomainType)
            {
                // We add any types belong to one of the disjoint domains because they might cause the final
                // intersection operation to reduce the union constraints.
                foreach (var t in types)
                {
                    if ((t.Flags & TypeFlags.DisjointDomains) != 0 || IsEmptyAnonymousObjectType(t))
                    {
                        constraints = Core.Append(constraints, t) as List<IType>;
                    }
                }
            }

            // The source types were normalized; ensure the result is normalized too.
            return GetNormalizedType(GetIntersectionType(constraints!, IntersectionFlags.NoConstraintReduction), /*writing*/ false);
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:15297-15307 — <c>getBaseConstraintOfType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS maps an <c>Index</c> type to the checker's <c>stringNumberSymbolType</c> field, which is a
    /// sibling's slot (Checker.Types.cs's <c>_stringNumberSymbolType</c>), and compares the resolved constraint
    /// against the two singleton types by identity — hence <see cref="ReferenceEquals"/>.
    /// </remarks>
    internal IType? GetBaseConstraintOfType(IType type)
    {
        if ((type.Flags & (TypeFlags.InstantiableNonPrimitive | TypeFlags.UnionOrIntersection | TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) != 0 || IsGenericTupleType(type))
        {
            var constraint = GetResolvedBaseConstraint(type);
            return !ReferenceEquals(constraint, _noConstraintType) && !ReferenceEquals(constraint, _circularConstraintType) ? constraint : null;
        }

        return (type.Flags & TypeFlags.Index) != 0 ? _stringNumberSymbolType : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:15309-15311 — <c>getBaseConstraintOrType</c>.</summary>
    /// <remarks>
    /// This is similar to <c>getBaseConstraintOfType</c> except it returns the input type if there's no base
    /// constraint, instead of <c>undefined</c>. It also doesn't map indexes to <c>string</c>, as where this is used
    /// this would be unneeded (and likely undesirable).
    /// </remarks>
    internal IType GetBaseConstraintOrType(IType type) => GetBaseConstraintOfType(type) ?? type;

    /// <summary>ported from: src/compiler/checker.ts:15313-15315 — <c>hasNonCircularBaseConstraint</c>.</summary>
    internal bool HasNonCircularBaseConstraint(IType type) => !ReferenceEquals(GetResolvedBaseConstraint(type), _circularConstraintType);

    /// <summary>ported from: src/compiler/checker.ts:15317-15446 — <c>getResolvedBaseConstraint</c>.</summary>
    /// <remarks>
    /// <para>
    /// Return the resolved base constraint of a type variable. The <c>noConstraintType</c> singleton is returned if the
    /// type variable has no constraint, and the <c>circularConstraintType</c> singleton is returned if the constraint
    /// circularly references the type variable.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's parameter is the <c>InstantiableType | UnionOrIntersectionType</c> union and its
    /// <c>resolvedBaseConstraint</c> slot is declared by both faces; C# has no common view of them, so the port takes
    /// <c>IType</c> and reads/writes the slot through the switch below (the same treatment the class remarks describe
    /// for <c>objectFlags</c>).
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>stack</c> is TS's <c>object[]</c> of recursion identities; <c>push</c>/<c>pop</c> are
    /// <see cref="List{T}.Add"/>/<see cref="List{T}.RemoveAt"/> and <c>contains(stack, identity)</c> is
    /// <see cref="Core.Contains{T}"/>, which compares with the default (reference) equality — the identity objects come
    /// from <c>getRecursionIdentity</c> and are compared by <c>===</c> upstream.
    /// </para>
    /// <para>
    /// PORT-NOTE: the reference takes the diagnostic's related-information helper from <c>utilities.ts</c>
    /// (<c>addRelatedInfo</c>/<c>createDiagnosticForNode</c>), which the port has as
    /// <see cref="Utilities.AddRelatedInfo"/> / <see cref="Utilities.CreateDiagnosticForNode"/>; <c>currentNode</c> is
    /// read once into a local so the null check covers both uses. <c>typeToString(t)</c> is M6-6's printer-gated member
    /// and is called as <c>TypeToString</c> here (docs/architecture.md §15.15 ruling 1).
    /// </para>
    /// </remarks>
    internal IType GetResolvedBaseConstraint(IType type)
    {
        var resolved = ReadResolvedBaseConstraint(type);
        if (resolved is not null)
        {
            return resolved;
        }

        var stack = new List<object>();
        var result = GetImmediateBaseConstraint(type);
        WriteResolvedBaseConstraint(type, result);
        return result;

        IType GetImmediateBaseConstraint(IType t)
        {
            if (t.ImmediateBaseConstraint is null)
            {
                if (!PushTypeResolution(t, TypeSystemPropertyName.ImmediateBaseConstraint))
                {
                    return _circularConstraintType;
                }

                IType? internalResult = null;
                // We always explore at least 10 levels of nested constraints. Thereafter, we continue to explore
                // up to 50 levels of nested constraints provided there are no "deeply nested" types on the stack
                // (i.e. no types for which five instantiations have been recorded on the stack). If we reach 50
                // levels of nesting, we are presumably exploring a repeating pattern with a long cycle that hasn't
                // yet triggered the deeply nested limiter. We have no test cases that actually get to 50 levels of
                // nesting, so it is effectively just a safety stop.
                var identity = GetRecursionIdentity(t)!;
                if (stack.Count < 10 || (stack.Count < 50 && !Core.Contains(stack, identity)))
                {
                    stack.Add(identity);
                    internalResult = ComputeBaseConstraint(GetSimplifiedType(t, /*writing*/ false));
                    stack.RemoveAt(stack.Count - 1);
                }

                if (!PopTypeResolution())
                {
                    if ((t.Flags & TypeFlags.TypeParameter) != 0)
                    {
                        var errorNode = GetConstraintDeclaration((ITypeParameter)t);
                        if (errorNode is not null)
                        {
                            var diagnostic = Error((Node)errorNode, Diagnostics.Type_parameter_0_has_a_circular_constraint, TypeToString(t));
                            var currentNode = _currentNode;
                            if (currentNode is not null && !Utilities.IsNodeDescendantOf((Node)errorNode, currentNode) && !Utilities.IsNodeDescendantOf(currentNode, (Node)errorNode))
                            {
                                Utilities.AddRelatedInfo(diagnostic, Utilities.CreateDiagnosticForNode(currentNode, Diagnostics.Circularity_originates_in_type_at_this_location));
                            }
                        }
                    }

                    internalResult = _circularConstraintType;
                }

                t.ImmediateBaseConstraint = internalResult ?? _noConstraintType;
            }

            return t.ImmediateBaseConstraint!;
        }

        IType? GetBaseConstraint(IType t)
        {
            var c = GetImmediateBaseConstraint(t);
            return !ReferenceEquals(c, _noConstraintType) && !ReferenceEquals(c, _circularConstraintType) ? c : null;
        }

        IType? ComputeBaseConstraint(IType t)
        {
            if ((t.Flags & TypeFlags.TypeParameter) != 0)
            {
                var constraint = GetConstraintFromTypeParameter((ITypeParameter)t);
                return ((ITypeParameter)t).IsThisType == true || constraint is null
                    ? constraint
                    : GetBaseConstraint(constraint);
            }

            if ((t.Flags & TypeFlags.UnionOrIntersection) != 0)
            {
                var types = ((IUnionOrIntersectionType)t).Types!;
                var baseTypes = new List<IType>();
                var different = false;
                foreach (var type in types)
                {
                    var baseType = GetBaseConstraint(type);
                    if (baseType is not null)
                    {
                        if (!ReferenceEquals(baseType, type))
                        {
                            different = true;
                        }

                        baseTypes.Add(baseType);
                    }
                    else
                    {
                        different = true;
                    }
                }

                if (!different)
                {
                    return t;
                }

                return (t.Flags & TypeFlags.Union) != 0 && baseTypes.Count == types.Count ? GetUnionType(baseTypes)
                    : (t.Flags & TypeFlags.Intersection) != 0 && baseTypes.Count > 0 ? GetIntersectionType(baseTypes)
                    : null;
            }

            if ((t.Flags & TypeFlags.Index) != 0)
            {
                if (IsGenericMappedType(((IIndexType)t).Type!))
                {
                    var mappedType = (IMappedType)((IIndexType)t).Type!;
                    if (GetNameTypeFromMappedType(mappedType) is not null && !IsMappedTypeWithKeyofConstraintDeclaration(mappedType))
                    {
                        return GetBaseConstraint(GetIndexTypeForMappedType(mappedType, IndexFlags.None));
                    }
                }

                return _stringNumberSymbolType;
            }

            if ((t.Flags & TypeFlags.TemplateLiteral) != 0)
            {
                var types = ((ITemplateLiteralType)t).Types!;
                var constraints = Core.MapDefined(types, (IType type, int _) => GetBaseConstraint(type));
                return constraints.Count == types.Count ? GetTemplateLiteralType(((ITemplateLiteralType)t).Texts!, constraints) : _stringType;
            }

            if ((t.Flags & TypeFlags.StringMapping) != 0)
            {
                var constraint = GetBaseConstraint(((IStringMappingType)t).Type!);
                return constraint is not null && !ReferenceEquals(constraint, ((IStringMappingType)t).Type)
                    ? GetStringMappingType(((IStringMappingType)t).Symbol!, constraint)
                    : _stringType;
            }

            if ((t.Flags & TypeFlags.IndexedAccess) != 0)
            {
                if (IsMappedTypeGenericIndexedAccess(t))
                {
                    // For indexed access types of the form { [P in K]: E }[X], where K is non-generic and X is generic,
                    // we substitute an instantiation of E where P is replaced with X.
                    return GetBaseConstraint(SubstituteIndexedMappedType((IMappedType)((IIndexedAccessType)t).ObjectType!, ((IIndexedAccessType)t).IndexType!));
                }

                var baseObjectType = GetBaseConstraint(((IIndexedAccessType)t).ObjectType!);
                var baseIndexType = GetBaseConstraint(((IIndexedAccessType)t).IndexType!);
                var baseIndexedAccess = baseObjectType is not null && baseIndexType is not null
                    ? GetIndexedAccessTypeOrUndefined(baseObjectType, baseIndexType, ((IIndexedAccessType)t).AccessFlags)
                    : null;
                return baseIndexedAccess is not null ? GetBaseConstraint(baseIndexedAccess) : null;
            }

            if ((t.Flags & TypeFlags.Conditional) != 0)
            {
                var constraint = GetConstraintFromConditionalType((IConditionalType)t);
                return constraint is not null ? GetBaseConstraint(constraint) : null;
            }

            if ((t.Flags & TypeFlags.Substitution) != 0)
            {
                return GetBaseConstraint(GetSubstitutionIntersection((ISubstitutionType)t));
            }

            if (IsGenericTupleType(t))
            {
                // We substitute constraints for variadic elements only when the constraints are array types or
                // non-variadic tuple types as we want to avoid further (possibly unbounded) recursion.
                var target = (ITupleType)((ITypeReference)t).Target!;
                var newElements = Core.Map(GetElementTypes((ITupleTypeReference)t)!, (v, i) =>
                {
                    var constraint = ((v.Flags & TypeFlags.TypeParameter) != 0 && (target.ElementFlags![i] & ElementFlags.Variadic) != 0 ? GetBaseConstraint(v) : null) ?? v;
                    return !ReferenceEquals(constraint, v) && EveryType(constraint, c => IsArrayOrTupleType(c) && !IsGenericTupleType(c)) ? constraint : v;
                });
                return CreateTupleType(newElements!, target.ElementFlags, target.Readonly, target.LabeledElementDeclarations);
            }

            return t;
        }
    }

    /// <summary>The <c>resolvedBaseConstraint</c> reader of <see cref="GetResolvedBaseConstraint"/> (checker.ts:15322).</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads <c>type.resolvedBaseConstraint</c> off the <c>InstantiableType | UnionOrIntersectionType</c>
    /// union; the two faces that declare the slot are listed here so one <c>IType</c>-taking member can read it.
    /// </remarks>
    private static IType? ReadResolvedBaseConstraint(IType type) => type switch
    {
        IInstantiableType instantiableType => instantiableType.ResolvedBaseConstraint,
        IUnionOrIntersectionType unionOrIntersectionType => unionOrIntersectionType.ResolvedBaseConstraint,
        _ => null,
    };

    /// <summary>The <c>resolvedBaseConstraint</c> writer of <see cref="GetResolvedBaseConstraint"/> (checker.ts:15322).</summary>
    /// <remarks>PORT-NOTE: as <see cref="ReadResolvedBaseConstraint"/>; both callers pass a type of one of the two faces.</remarks>
    private static void WriteResolvedBaseConstraint(IType type, IType value)
    {
        switch (type)
        {
            case IInstantiableType instantiableType:
                instantiableType.ResolvedBaseConstraint = value;
                break;
            case IUnionOrIntersectionType unionOrIntersectionType:
                unionOrIntersectionType.ResolvedBaseConstraint = value;
                break;
            default:
                Debug.Fail("a type with a base constraint must carry a resolvedBaseConstraint slot");
                break;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:15448-15454 — <c>getApparentTypeOfIntersectionType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the cache key is the reference's template string
    /// (<c>`I${getTypeId(type)},${getTypeId(thisArgument)}`</c>); the port builds the same string with the invariant
    /// culture, as the surrounding slice does for <c>getTypeId</c> keys.
    /// </remarks>
    internal IType GetApparentTypeOfIntersectionType(IIntersectionType type, IType thisArgument)
    {
        if (ReferenceEquals(type, thisArgument))
        {
            return type.ResolvedApparentType ??= GetTypeWithThisArgument(type, thisArgument, /*needApparentType*/ true);
        }

        var key = $"I{GetTypeId(type).ToString(CultureInfo.InvariantCulture)},{GetTypeId(thisArgument).ToString(CultureInfo.InvariantCulture)}";
        return GetCachedType(key) ?? SetCachedType(key, GetTypeWithThisArgument(type, thisArgument, /*needApparentType*/ true));
    }

    /// <summary>ported from: src/compiler/checker.ts:15456-15485 — <c>getResolvedTypeParameterDefault</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>forEach(typeParameter.symbol.declarations, decl =&gt; isTypeParameterDeclaration(decl) &amp;&amp;
    /// decl.default)</c> answers the first truthy callback result, i.e. the first type parameter declaration that has a
    /// default; the port's <see cref="Core.ForEach{T,TU}(IList{T}?, Func{T,int,TU})"/> has the same contract.
    /// </remarks>
    internal IType? GetResolvedTypeParameterDefault(ITypeParameter typeParameter)
    {
        if (typeParameter.Default is null)
        {
            if (typeParameter.Target is not null)
            {
                var targetDefault = GetResolvedTypeParameterDefault(typeParameter.Target);
                typeParameter.Default = targetDefault is not null ? InstantiateType(targetDefault, (TypeMapper?)typeParameter.Mapper) : _noConstraintType;
            }
            else
            {
                // To block recursion, set the initial value to the resolvingDefaultType.
                typeParameter.Default = _resolvingDefaultType;
                var defaultDeclaration = typeParameter.Symbol is not null
                    ? Core.ForEach(typeParameter.Symbol.Declarations, (IDeclaration decl, int _) => NodeTests.IsTypeParameterDeclaration((Node)decl) ? ((TypeParameterDeclaration)decl).Default : null)
                    : null;
                var defaultType = defaultDeclaration is not null ? GetTypeFromTypeNode(defaultDeclaration) : _noConstraintType;
                if (ReferenceEquals(typeParameter.Default, _resolvingDefaultType))
                {
                    // If we have not been called recursively, set the correct default type.
                    typeParameter.Default = defaultType;
                }
            }
        }
        else if (ReferenceEquals(typeParameter.Default, _resolvingDefaultType))
        {
            // If we are called recursively for this type parameter, mark the default as circular.
            typeParameter.Default = _circularConstraintType;
        }

        return typeParameter.Default;
    }

    /// <summary>ported from: src/compiler/checker.ts:15487-15490 — <c>getDefaultFromTypeParameter</c>.</summary>
    /// <remarks>
    /// Gets the default type for a type parameter. If the type parameter is the result of an instantiation, this gets
    /// the instantiated default type of its target. If the type parameter has no default type or the default is
    /// circular, <c>undefined</c> is returned.
    /// </remarks>
    internal IType? GetDefaultFromTypeParameter(ITypeParameter typeParameter)
    {
        var defaultType = GetResolvedTypeParameterDefault(typeParameter);
        return defaultType is not null && !ReferenceEquals(defaultType, _noConstraintType) && !ReferenceEquals(defaultType, _circularConstraintType) ? defaultType : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:15492-15494 — <c>hasNonCircularTypeParameterDefault</c>.</summary>
    internal bool HasNonCircularTypeParameterDefault(ITypeParameter typeParameter)
        => !ReferenceEquals(GetResolvedTypeParameterDefault(typeParameter), _circularConstraintType);

    /// <summary>ported from: src/compiler/checker.ts:15496-15501 — <c>hasTypeParameterDefault</c>.</summary>
    /// <remarks>
    /// Indicates whether the declaration of a typeParameter has a default type. PORT-NOTE: TS's
    /// <c>forEach(declarations, decl =&gt; isTypeParameterDeclaration(decl) &amp;&amp; decl.default)</c> wrapped in <c>!!</c>
    /// is <see cref="Core.Some{T}(IList{T}?, Func{T,bool})"/> over the same predicate.
    /// </remarks>
    internal static bool HasTypeParameterDefault(ITypeParameter typeParameter)
        => typeParameter.Symbol is not null
            && Core.Some(typeParameter.Symbol.Declarations, (IDeclaration decl, int _) => NodeTests.IsTypeParameterDeclaration((Node)decl) && ((TypeParameterDeclaration)decl).Default is not null);

    /// <summary>ported from: src/compiler/checker.ts:15503-15505 — <c>getApparentTypeOfMappedType</c>.</summary>
    internal IType GetApparentTypeOfMappedType(IMappedType type)
        => type.ResolvedApparentType ??= GetResolvedApparentTypeOfMappedType(type);

    /// <summary>ported from: src/compiler/checker.ts:15507-15524 — <c>getResolvedApparentTypeOfMappedType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>(type.target ?? type) as MappedType</c> reads the <c>AnonymousType.target</c> slot and casts
    /// it back to the mapped face; the port narrows through <see cref="IAnonymousType"/> first because that is the face
    /// declaring <c>target</c>.
    /// </remarks>
    internal IType GetResolvedApparentTypeOfMappedType(IMappedType type)
    {
        var target = (IMappedType)(type.Target ?? type);
        var typeVariable = GetHomomorphicTypeVariable(target);
        if (typeVariable is not null && target.Declaration!.NameType is null)
        {
            // We have a homomorphic mapped type or an instantiation of a homomorphic mapped type, i.e. a type
            // of the form { [P in keyof T]: X }. Obtain the modifiers type (the T of the keyof T), and if it is
            // another generic mapped type, recursively obtain its apparent type. Otherwise, obtain its base
            // constraint. Then, if every constituent of the base constraint is an array or tuple type, apply
            // this mapped type to the base constraint. It is safe to recurse when the modifiers type is a
            // mapped type because we protect again circular constraints in getTypeFromMappedTypeNode.
            var modifiersType = GetModifiersTypeFromMappedType(type);
            var baseConstraint = IsGenericMappedType(modifiersType) ? GetApparentTypeOfMappedType((IMappedType)modifiersType) : GetBaseConstraintOfType(modifiersType);
            if (baseConstraint is not null && EveryType(baseConstraint, t => IsArrayOrTupleType(t) || IsArrayOrTupleOrIntersection(t)))
            {
                return InstantiateType(target, PrependTypeMapping(typeVariable, baseConstraint, (TypeMapper?)type.Mapper))!;
            }
        }

        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:15526-15528 — <c>isArrayOrTupleOrIntersection</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares no statics here — <c>isArrayOrTupleOrIntersection</c> is a <c>createTypeChecker</c>
    /// member and reads <c>isArrayOrTupleType</c>, which is an instance member of this class
    /// (<c>Checker.Relations.Identical.cs</c>); the port's one-word <c>static</c> made that call CS0120.
    /// </remarks>
    internal bool IsArrayOrTupleOrIntersection(IType type)
        => (type.Flags & TypeFlags.Intersection) != 0 && Core.Every(((IIntersectionType)type).Types, (IType t, int _) => IsArrayOrTupleType(t));

    /// <summary>ported from: src/compiler/checker.ts:15530-15535 — <c>isMappedTypeGenericIndexedAccess</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS assigns <c>objectType</c> inside the <c>&amp;&amp;</c> chain
    /// (<c>getObjectFlags(objectType = (type as IndexedAccessType).objectType) &amp; Mapped &amp;&amp; …</c>); the port
    /// hoists the guard above the assignment, which is the same order of evaluation and the same short-circuit.
    /// </remarks>
    internal bool IsMappedTypeGenericIndexedAccess(IType type)
    {
        if ((type.Flags & TypeFlags.IndexedAccess) == 0)
        {
            return false;
        }

        var objectType = ((IIndexedAccessType)type).ObjectType!;
        return (GetObjectFlags(objectType) & ObjectFlags.Mapped) != 0
            && !IsGenericMappedType(objectType)
            && IsGenericIndexType(((IIndexedAccessType)type).IndexType!)
            && (GetMappedTypeModifiers((IMappedType)objectType) & MappedTypeModifiers.ExcludeOptional) == 0
            && ((IMappedType)objectType).Declaration!.NameType is null;
    }

    /// <summary>ported from: src/compiler/checker.ts:15559-15564 — <c>getReducedApparentType</c>.</summary>
    /// <remarks>
    /// Since <c>getApparentType</c> may return a non-reduced union or intersection type, we need to perform
    /// type reduction both before and after obtaining the apparent type. For example, given a type parameter
    /// 'T extends A | B', the type 'T &amp; X' becomes 'A &amp; X | B &amp; X' after obtaining the apparent type, and
    /// that type may need further reduction to remove empty intersections.
    /// </remarks>
    internal IType GetReducedApparentType(IType type) => GetReducedType(GetApparentType(GetReducedType(type)));

    // ---------------------------------------------------------------------------------------------
    // The reduced-type family (checker.ts:15811-15869)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:15811-15823 — <c>getReducedType</c>.</summary>
    /// <remarks>
    /// <para>
    /// Return the reduced form of the given type. For a union type, it is a union of the normalized constituent types.
    /// For an intersection of types containing one or more mututally exclusive discriminant properties, it is 'never'.
    /// For all other types, it is simply the type itself. Discriminant properties are considered mutually exclusive
    /// when no constituent property has type 'never', but the intersection of the constituent property types is
    /// 'never'.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>some(getPropertiesOfUnionOrIntersectionType(type as IntersectionType), isNeverReducedProperty)</c>
    /// is <see cref="Core.Some{T}(IList{T}?, Func{T,bool})"/>; the helper takes the union/intersection class rather
    /// than the face, so the call narrows to <see cref="IntersectionType"/> first, which any intersection object is.
    /// </para>
    /// </remarks>
    internal IType GetReducedType(IType type)
    {
        if ((type.Flags & TypeFlags.Union) != 0 && (((IUnionType)type).ObjectFlags & ObjectFlags.ContainsIntersections) != 0)
        {
            var unionType = (IUnionType)type;
            return unionType.ResolvedReducedType ??= GetReducedUnionType(unionType);
        }

        if ((type.Flags & TypeFlags.Intersection) != 0)
        {
            var intersectionType = (IIntersectionType)type;
            if ((intersectionType.ObjectFlags & ObjectFlags.IsNeverIntersectionComputed) == 0)
            {
                intersectionType.ObjectFlags |= ObjectFlags.IsNeverIntersectionComputed
                    | (Core.Some(GetPropertiesOfUnionOrIntersectionType((IntersectionType)type), IsNeverReducedProperty) ? ObjectFlags.IsNeverIntersection : ObjectFlags.None);
            }

            return (intersectionType.ObjectFlags & ObjectFlags.IsNeverIntersection) != 0 ? _neverType : type;
        }

        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:15825-15835 — <c>getReducedUnionType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>sameMap(unionType.types, getReducedType)</c> hands the input array back when no constituent
    /// changed, and the port's <see cref="Core.SameMap{T}"/> keeps that contract; the returned <c>IList</c> is a
    /// <c>List</c> in both arms (the input list or a fresh one), which is what <c>getUnionType</c> takes.
    /// </remarks>
    internal IType GetReducedUnionType(IUnionType unionType)
    {
        var reducedTypes = Core.SameMap(unionType.Types, (t, _) => GetReducedType(t)) as List<IType>;
        if (ReferenceEquals(reducedTypes, unionType.Types))
        {
            return unionType;
        }

        var reduced = GetUnionType(reducedTypes!);
        if ((reduced.Flags & TypeFlags.Union) != 0)
        {
            ((IUnionType)reduced).ResolvedReducedType = reduced;
        }

        return reduced;
    }

    /// <summary>ported from: src/compiler/checker.ts:15837-15839 — <c>isNeverReducedProperty</c>.</summary>
    internal bool IsNeverReducedProperty(ISymbol prop) => IsDiscriminantWithNeverType(prop) || IsConflictingPrivateProperty(prop);

    /// <summary>ported from: src/compiler/checker.ts:15841-15847 — <c>isDiscriminantWithNeverType</c>.</summary>
    /// <remarks>
    /// Return true for a synthetic non-optional property with non-uniform types, where at least one is a literal type
    /// and none is never, that reduces to never.
    /// </remarks>
    internal bool IsDiscriminantWithNeverType(ISymbol prop)
        => (prop.Flags & SymbolFlags.Optional) == 0
            && (GetCheckFlags(prop) & (CheckFlags.Discriminant | CheckFlags.HasNeverType)) == CheckFlags.Discriminant
            && (GetTypeOfSymbol(prop).Flags & TypeFlags.Never) != 0;

    /// <summary>ported from: src/compiler/checker.ts:15849-15851 — <c>isConflictingPrivateProperty</c>.</summary>
    /// <remarks>Return true for a synthetic property with multiple declarations, at least one of which is private.</remarks>
    internal bool IsConflictingPrivateProperty(ISymbol prop)
        => prop.ValueDeclaration is null && (GetCheckFlags(prop) & CheckFlags.ContainsPrivate) != 0;

    /// <summary>ported from: src/compiler/checker.ts:15853-15864 — <c>isGenericReducibleType</c>.</summary>
    /// <remarks>
    /// A union type which is reducible upon instantiation (meaning some members are removed under certain
    /// instantiations) must be kept generic, as that instantiation information needs to flow through the type system.
    /// By replacing all type parameters in the union with a special never type that is treated as a literal in
    /// <c>getReducedType</c>, we can cause the <c>getReducedType</c> logic to reduce the resulting type if possible
    /// (since only intersections with conflicting literal-typed properties are reducible).
    /// </remarks>
    internal bool IsGenericReducibleType(IType type)
        => ((type.Flags & TypeFlags.Union) != 0
                && (((IUnionType)type).ObjectFlags & ObjectFlags.ContainsIntersections) != 0
                && Core.Some(((IUnionType)type).Types, IsGenericReducibleType))
            || ((type.Flags & TypeFlags.Intersection) != 0 && IsReducibleIntersection((IIntersectionType)type));

    /// <summary>ported from: src/compiler/checker.ts:15866-15869 — <c>isReducibleIntersection</c>.</summary>
    internal bool IsReducibleIntersection(IIntersectionType type)
    {
        var uniqueFilled = type.UniqueLiteralFilledInstantiation ??= InstantiateType(type, _uniqueLiteralMapper)!;
        return !ReferenceEquals(GetReducedType(uniqueFilled), uniqueFilled);
    }

    // ---------------------------------------------------------------------------------------------
    // The index-info lookups (checker.ts:15981-16067)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:15981-15983 — <c>findIndexInfo</c>.</summary>
    internal static IIndexInfo? FindIndexInfo(IList<IIndexInfo> indexInfos, IType keyType)
        => Core.Find(indexInfos, (IIndexInfo info, int _) => ReferenceEquals(info.KeyType, keyType));

    /// <summary>ported from: src/compiler/checker.ts:15985-16010 — <c>findApplicableIndexInfo</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>(applicableInfos || (applicableInfos = [applicableInfo])).push(info)</c> is the port's
    /// <c>??=</c> over the same list; the tail of the reference's conditional chain
    /// (<c>applicableInfo ? applicableInfo : stringIndexInfo &amp;&amp; isApplicableIndexType(…) ? stringIndexInfo : undefined</c>)
    /// is written with <c>??</c> and an explicit null.
    /// </remarks>
    internal IIndexInfo? FindApplicableIndexInfo(IList<IIndexInfo> indexInfos, IType keyType)
    {
        // Index signatures for type 'string' are considered only when no other index signatures apply.
        IIndexInfo? stringIndexInfo = null;
        IIndexInfo? applicableInfo = null;
        List<IIndexInfo>? applicableInfos = null;
        foreach (var info in indexInfos)
        {
            if (ReferenceEquals(info.KeyType, _stringType))
            {
                stringIndexInfo = info;
            }
            else if (IsApplicableIndexType(keyType, info.KeyType!))
            {
                if (applicableInfo is null)
                {
                    applicableInfo = info;
                }
                else
                {
                    applicableInfos ??= [applicableInfo];
                    applicableInfos.Add(info);
                }
            }
        }

        // When more than one index signature is applicable we create a synthetic IndexInfo. Instead of computing
        // the intersected key type, we just use unknownType for the key type as nothing actually depends on the
        // keyType property of the returned IndexInfo.
        return applicableInfos is not null
            ? CreateIndexInfo(
                _unknownType,
                GetIntersectionType(Core.Map(applicableInfos, (IIndexInfo info, int _) => info.Type!)!),
                Core.ReduceLeft(applicableInfos, (isReadonly, info, _) => isReadonly && info.IsReadonly, /*initial*/ true))
            : applicableInfo
                ?? (stringIndexInfo is not null && IsApplicableIndexType(keyType, _stringType) ? stringIndexInfo : null);
    }

    /// <summary>ported from: src/compiler/checker.ts:16012-16019 — <c>isApplicableIndexType</c>.</summary>
    /// <remarks>
    /// A 'string' index signature applies to types assignable to 'string' or 'number', and a 'number' index signature
    /// applies to types assignable to 'number', <c>${number}</c> and numeric string literal types.
    /// </remarks>
    internal bool IsApplicableIndexType(IType source, IType target)
        => IsTypeAssignableTo(source, target)
            || (ReferenceEquals(target, _stringType) && IsTypeAssignableTo(source, _numberType))
            || (ReferenceEquals(target, _numberType)
                && (ReferenceEquals(source, _numericStringType)
                    || ((source.Flags & TypeFlags.StringLiteral) != 0 && Utilities.IsNumericLiteralName((string)((IStringLiteralType)source).Value!))));

    /// <summary>ported from: src/compiler/checker.ts:16034-16037 — <c>getIndexInfoOfType</c>.</summary>
    /// <remarks>
    /// Return the indexing info of the given kind in the given type. Creates synthetic union index types when necessary
    /// and maps primitive types and type parameters are to their apparent types.
    /// </remarks>
    internal IIndexInfo? GetIndexInfoOfType(IType type, IType keyType) => FindIndexInfo(GetIndexInfosOfType(type), keyType);

    /// <summary>ported from: src/compiler/checker.ts:16040-16042 — <c>getIndexTypeOfType</c>.</summary>
    /// <remarks>
    /// Return the index type of the given kind in the given type. Creates synthetic union index types when necessary
    /// and maps primitive types and type parameters are to their apparent types.
    /// </remarks>
    internal IType? GetIndexTypeOfType(IType type, IType keyType) => GetIndexInfoOfType(type, keyType)?.Type;

    /// <summary>ported from: src/compiler/checker.ts:16044-16046 — <c>getApplicableIndexInfos</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>.filter(…)</c> builds an <c>IndexInfo[]</c>; <see cref="Core.Filter{T}"/> answers an
    /// <c>IList</c> (the input when nothing is filtered out), which is the port's array idiom.
    /// </remarks>
    internal IList<IIndexInfo> GetApplicableIndexInfos(IType type, IType keyType)
        => Core.Filter(GetIndexInfosOfType(type), info => IsApplicableIndexType(keyType, info.KeyType!))!;

    /// <summary>ported from: src/compiler/checker.ts:16048-16050 — <c>getApplicableIndexInfo</c>.</summary>
    internal IIndexInfo? GetApplicableIndexInfo(IType type, IType keyType) => FindApplicableIndexInfo(GetIndexInfosOfType(type), keyType);

    /// <summary>ported from: src/compiler/checker.ts:16052-16054 — <c>getApplicableIndexInfoForName</c>.</summary>
    internal IIndexInfo? GetApplicableIndexInfoForName(IType type, string name)
        => GetApplicableIndexInfo(type, IsLateBoundName(name) ? _esSymbolType : GetStringLiteralType(Utilities.UnescapeLeadingUnderscores(name)));

    /// <summary>ported from: src/compiler/checker.ts:16058-16067 — <c>getTypeParametersFromDeclaration</c>.</summary>
    /// <remarks>
    /// <para>
    /// Return list of type parameters with duplicates removed (duplicate identifier errors are generated in the actual
    /// type checking functions).
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's parameter is the structural <c>DeclarationWithTypeParameters</c>, which C# cannot express, so
    /// the port reads the <c>typeParameters</c> slot through the small switch in
    /// <see cref="GetTypeParameterDeclarationsOfDeclaration"/> — the eighteen classes the generator declares it on —
    /// and answers an empty list for any other node, which is TS's optional slot.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS iterates <c>getEffectiveTypeParameterDeclarations</c> (utilitiesPublic.ts:1318), whose JSDoc arms
    /// (<c>JSDocSignature</c>, <c>JSDocOverloadTag</c>, <c>JSDocTypeAlias</c>, <c>@template</c> tags and the
    /// <c>canHaveIllegalTypeParameters</c> path) are not ported; for every non-JSDoc declaration the two read the same
    /// plain slot, which is the case this slice's callers use. The <c>appendIfUnique</c> and the
    /// <c>isFunctionDeclaration</c> fallback to the signature's own type parameters are the reference's.
    /// </para>
    /// </remarks>
    internal List<ITypeParameter>? GetTypeParametersFromDeclaration(Node declaration)
    {
        List<ITypeParameter>? result = null;
        foreach (var node in GetTypeParameterDeclarationsOfDeclaration(declaration))
        {
            result = (List<ITypeParameter>)Core.AppendIfUnique(result, GetDeclaredTypeOfTypeParameter(GetSymbolOfDeclaration(node)))!;
        }

        return Core.Length(result) > 0
            ? result
            : NodeTests.IsFunctionDeclaration(declaration) ? GetSignatureOfTypeTag(declaration)?.TypeParameters : null;
    }

    /// <summary>The <c>typeParameters</c> read of <see cref="GetTypeParametersFromDeclaration"/> (utilitiesPublic.ts:1318).</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>DeclarationWithTypeParameters</c> is a union of the eighteen declarations that can carry type
    /// parameters; C# selects the face with this switch, which is that union written out. Every arm reads the same
    /// <c>NodeArray&lt;TypeParameterDeclaration&gt;</c> slot the generator emits on those classes.
    /// </remarks>
    private static List<TypeParameterDeclaration> GetTypeParameterDeclarationsOfDeclaration(Node declaration)
    {
        NodeArray<TypeParameterDeclaration>? typeParameters = declaration switch
        {
            ArrowFunction node => node.TypeParameters,
            CallSignatureDeclaration node => node.TypeParameters,
            ClassDeclaration node => node.TypeParameters,
            ClassExpression node => node.TypeParameters,
            ConstructSignatureDeclaration node => node.TypeParameters,
            ConstructorDeclaration node => node.TypeParameters,
            FunctionDeclaration node => node.TypeParameters,
            FunctionExpression node => node.TypeParameters,
            FunctionOrConstructorTypeNodeBase node => node.TypeParameters,
            GetAccessorDeclaration node => node.TypeParameters,
            IndexSignatureDeclaration node => node.TypeParameters,
            InterfaceDeclaration node => node.TypeParameters,
            JSDocFunctionType node => node.TypeParameters,
            JSDocTemplateTag node => node.TypeParameters,
            MethodDeclaration node => node.TypeParameters,
            MethodSignature node => node.TypeParameters,
            SetAccessorDeclaration node => node.TypeParameters,
            TypeAliasDeclaration node => node.TypeParameters,
            _ => null,
        };

        return typeParameters is null ? [] : typeParameters;
    }

    // ---------------------------------------------------------------------------------------------
    // The type-predicate and type-argument helpers (checker.ts:16121-16176)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:16121-16123 — <c>createTypePredicate</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS builds the object literal <c>{ kind, parameterName, parameterIndex, type }</c> and casts it to the
    /// four-variant <c>TypePredicate</c> union; the port's <see cref="TypePredicate"/> constructor takes those four
    /// arguments in that order and the class implements all four faces (Model/TypePredicate.cs).
    /// </remarks>
    internal static TypePredicate CreateTypePredicate(TypePredicateKind kind, string? parameterName, int? parameterIndex, IType? type)
        => new(kind, parameterName, parameterIndex, type);

    /// <summary>ported from: src/compiler/checker.ts:16129-16140 — <c>getMinTypeArgumentCount</c>.</summary>
    /// <remarks>Gets the minimum number of type arguments needed to satisfy all non-optional type parameters.</remarks>
    internal static int GetMinTypeArgumentCount(List<ITypeParameter>? typeParameters)
    {
        var minTypeArgumentCount = 0;
        if (typeParameters is not null)
        {
            for (var i = 0; i < typeParameters.Count; i++)
            {
                if (!HasTypeParameterDefault(typeParameters[i]))
                {
                    minTypeArgumentCount = i + 1;
                }
            }
        }

        return minTypeArgumentCount;
    }

    /// <summary>ported from: src/compiler/checker.ts:16149-16176 — <c>fillMissingTypeArguments</c>.</summary>
    /// <remarks>
    /// <para>
    /// Fill in default types for unsupplied type arguments. If <c>typeArguments</c> is undefined when a default type is
    /// supplied, a new array will be created and returned.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS declares two overloads (the non-optional and the <c>| undefined</c> argument), which C# cannot
    /// state — nullable annotations do not participate in overload resolution. The port declares the single
    /// <c>List&lt;IType&gt;?</c>-taking member and each caller adds <c>!</c> where its argument is known non-null.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>result[i] = …</c> walks a <c>slice()</c> of the supplied arguments; the port models that with
    /// <c>Add</c> for the error-type pre-fill (the list is exactly <c>numTypeArguments</c> long at that point), the
    /// indexed writes of the second loop, and the reference's <c>result.length = typeParameters.length</c> truncation —
    /// reachable only when <c>isJavaScriptImplicitAny</c> admits more arguments than parameters.
    /// </para>
    /// <para>
    /// PORT-NOTE: the reference's <c>unknownType</c>/<c>emptyObjectType</c> comparisons are the checker's
    /// <c>_unknownType</c>/<c>_emptyObjectType</c> singletons.
    /// </para>
    /// </remarks>
    internal List<IType>? FillMissingTypeArguments(List<IType>? typeArguments, List<ITypeParameter>? typeParameters, int minTypeArgumentCount, bool isJavaScriptImplicitAny)
    {
        var numTypeParameters = Core.Length(typeParameters);
        if (numTypeParameters == 0)
        {
            return [];
        }

        var numTypeArguments = Core.Length(typeArguments);
        if (isJavaScriptImplicitAny || (numTypeArguments >= minTypeArgumentCount && numTypeArguments <= numTypeParameters))
        {
            var result = typeArguments is not null ? new List<IType>(typeArguments) : [];
            // Map invalid forward references in default types to the error type
            for (var i = numTypeArguments; i < numTypeParameters; i++)
            {
                result.Add(_errorType);
            }

            var baseDefaultType = GetDefaultTypeArgumentType(isJavaScriptImplicitAny);
            for (var i = numTypeArguments; i < numTypeParameters; i++)
            {
                var defaultType = GetDefaultFromTypeParameter(typeParameters![i]);
                if (isJavaScriptImplicitAny && defaultType is not null && (IsTypeIdenticalTo(defaultType, _unknownType) || IsTypeIdenticalTo(defaultType, _emptyObjectType)))
                {
                    defaultType = _anyType;
                }

                result[i] = defaultType is not null ? InstantiateType(defaultType, CreateTypeMapper(typeParameters!, result))! : baseDefaultType;
            }

            if (result.Count > numTypeParameters)
            {
                result.RemoveRange(numTypeParameters, result.Count - numTypeParameters);
            }

            return result;
        }

        return typeArguments is not null ? new List<IType>(typeArguments) : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:16418-16422 — <c>getThisTypeOfSignature</c>.</summary>
    internal IType? GetThisTypeOfSignature(ISignature signature)
        => signature.ThisParameter is not null ? GetTypeOfSymbol(signature.ThisParameter) : null;

    /// <summary>ported from: src/compiler/checker.ts:16424-16459 — <c>getTypePredicateOfSignature</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the reference's <c>signature.mapper!</c> is TS's <c>TypeMapper</c> union in an <c>object?</c> slot, so
    /// the port casts to <see cref="TypeMapper"/> (the concrete class the factories build).
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>noTypePredicate</c> is the checker's sentinel slot <c>_noTypePredicate</c> — an ordinary
    /// <c>Identifier</c> predicate — and the reader at the end turns it back into <c>undefined</c>
    /// (Model/Signature.cs's remarks). <c>getEffectiveReturnTypeNode</c> is the ported
    /// <see cref="Utilities.GetEffectiveReturnTypeNode"/>; the declaration's node face reaches it through a cast to
    /// <see cref="Node"/>, the reference's <c>SignatureDeclaration | JSDocSignature</c> being a node union.
    /// </para>
    /// </remarks>
    internal ITypePredicateBase? GetTypePredicateOfSignature(ISignature signature)
    {
        if (signature.ResolvedTypePredicate is null)
        {
            if (signature.Target is not null)
            {
                var targetTypePredicate = GetTypePredicateOfSignature(signature.Target);
                signature.ResolvedTypePredicate = targetTypePredicate is not null
                    ? InstantiateTypePredicate(targetTypePredicate, (TypeMapper)signature.Mapper!)
                    : _noTypePredicate;
            }
            else if (signature.CompositeSignatures is not null)
            {
                signature.ResolvedTypePredicate = GetUnionOrIntersectionTypePredicate(signature.CompositeSignatures, signature.CompositeKind) ?? _noTypePredicate;
            }
            else
            {
                var type = signature.Declaration is not null ? Utilities.GetEffectiveReturnTypeNode((Node)signature.Declaration) : null;
                ITypePredicateBase? jsdocPredicate = null;
                if (type is null)
                {
                    var jsdocSignature = GetSignatureOfTypeTag(signature.Declaration!);
                    if (jsdocSignature is not null && !ReferenceEquals(signature, jsdocSignature))
                    {
                        jsdocPredicate = GetTypePredicateOfSignature(jsdocSignature);
                    }
                }

                if (type is not null || jsdocPredicate is not null)
                {
                    signature.ResolvedTypePredicate = type is not null && NodeTests.IsTypePredicateNode((Node)type)
                        ? CreateTypePredicateFromTypePredicateNode((TypePredicateNode)type, signature)
                        : jsdocPredicate ?? _noTypePredicate;
                }
                else if (signature.Declaration is not null
                    && Utilities.IsFunctionLikeDeclaration((Node)signature.Declaration)
                    && (signature.ResolvedReturnType is null || (signature.ResolvedReturnType.Flags & TypeFlags.Boolean) != 0)
                    && GetParameterCount(signature) > 0)
                {
                    // avoid infinite loop
                    signature.ResolvedTypePredicate = _noTypePredicate;
                    signature.ResolvedTypePredicate = GetTypePredicateFromBody((Node)signature.Declaration) ?? _noTypePredicate;
                }
                else
                {
                    signature.ResolvedTypePredicate = _noTypePredicate;
                }
            }

            Debug.Assert(signature.ResolvedTypePredicate is not null);
        }

        return ReferenceEquals(signature.ResolvedTypePredicate, _noTypePredicate) ? null : signature.ResolvedTypePredicate;
    }

    /// <summary>ported from: src/compiler/checker.ts:16469-16471 — <c>getUnionOrIntersectionType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>kind: TypeFlags | undefined</c> and its <c>unionReduction?</c> is optional, so
    /// the callers that omit it get <c>getUnionType</c>'s own default (<c>UnionReduction.Literal</c>); the port keeps
    /// both defaults on the C# signature. The intersection arm passes no flags — <c>getIntersectionType(types)</c> is
    /// <c>IntersectionFlags.None</c>.
    /// </remarks>
    internal IType GetUnionOrIntersectionType(List<IType> types, TypeFlags? kind, UnionReduction unionReduction = UnionReduction.Literal)
        => kind != TypeFlags.Intersection ? GetUnionType(types, unionReduction) : GetIntersectionType(types);

    /// <summary>ported from: src/compiler/checker.ts:16760-16762 — <c>getConstraintDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>mapDefined(filter(…, isTypeParameterDeclaration), getEffectiveConstraintOfTypeParameter)[0]</c>
    /// is the port's <see cref="Core.MapDefined{T,TU}"/> over <see cref="Core.Filter{T}"/> with
    /// <see cref="Core.FirstOrUndefined{T}"/> for the index, and <c>getEffectiveConstraintOfTypeParameter</c> is the
    /// ported <c>utilitiesPublic.ts:1351</c> helper.
    /// </remarks>
    internal ITypeNode? GetConstraintDeclaration(ITypeParameter type)
        => Core.FirstOrUndefined(Core.MapDefined(
            Core.Filter(type.Symbol?.Declarations, decl => NodeTests.IsTypeParameterDeclaration((Node)decl)),
            (decl, _) => Utilities.GetEffectiveConstraintOfTypeParameter((TypeParameterDeclaration)decl)));

    // ---------------------------------------------------------------------------------------------
    // The id and key helpers (checker.ts:16872-16952)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:16872-16894 — <c>getTypeListId</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS concatenates a number into a string (<c>result += startId</c>), which the engine renders with its
    /// decimal <c>ToString</c>; the port spells that out with the invariant culture so the key is culture-independent
    /// (the same treatment the surrounding slices give their type-id keys). The run-length form
    /// (<c>startId ":" count</c>) is the reference's.
    /// </remarks>
    internal static string GetTypeListId(List<IType>? types)
    {
        var result = "";
        if (types is not null)
        {
            var length = types.Count;
            var i = 0;
            while (i < length)
            {
                var startId = types[i].Id;
                var count = 1;
                while ((i + count) < length && types[i + count].Id == startId + count)
                {
                    count++;
                }

                if (result.Length > 0)
                {
                    result += ",";
                }

                result += startId.ToString(CultureInfo.InvariantCulture);
                if (count > 1)
                {
                    result += ":" + count.ToString(CultureInfo.InvariantCulture);
                }

                i += count;
            }
        }

        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:16896-16898 — <c>getAliasId</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS tests the arguments for truthiness (<c>aliasTypeArguments ? … : ""</c>); an empty array is truthy
    /// in JS, so the port tests <c>is not null</c> and a zero-length list still contributes its <c>":"</c>.
    /// </remarks>
    internal static string GetAliasId(ISymbol? aliasSymbol, List<IType>? aliasTypeArguments)
        => aliasSymbol is not null
            ? "@" + Utilities.GetSymbolId(aliasSymbol).ToString(CultureInfo.InvariantCulture)
                + (aliasTypeArguments is not null ? ":" + GetTypeListId(aliasTypeArguments) : "")
            : "";

    /// <summary>ported from: src/compiler/checker.ts:16901-16910 — <c>getPropagatingFlagsOfTypes</c>.</summary>
    /// <remarks>
    /// This function is used to propagate certain flags when creating new object type references and union types.
    /// It is only necessary to do so if a constituent type might be the undefined type, the null type, the type
    /// of an object literal or a non-inferrable type. This is because there are operations in the type checker
    /// that care about the presence of such types at arbitrary depth in a containing type.
    /// </remarks>
    internal static ObjectFlags GetPropagatingFlagsOfTypes(List<IType> types, TypeFlags? excludeKinds = null)
    {
        var result = ObjectFlags.None;
        foreach (var type in types)
        {
            if (excludeKinds is null || (type.Flags & excludeKinds.Value) == 0)
            {
                result |= GetObjectFlags(type);
            }
        }

        return result & ObjectFlags.PropagatingFlags;
    }

    /// <summary>ported from: src/compiler/checker.ts:16914-16920 — <c>tryCreateTypeReference</c>.</summary>
    internal IType TryCreateTypeReference(IGenericType target, List<IType>? typeArguments)
        => Core.Some(typeArguments) && ReferenceEquals(target, _emptyGenericType) ? _unknownType : CreateTypeReference(target, typeArguments);

    /// <summary>ported from: src/compiler/checker.ts:16922-16933 — <c>createTypeReference</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: <c>target.instantiations</c> is the <c>GenericType</c> face of the dual-view
    /// <c>InstantiationsSlot</c> (Model/ObjectType.cs), so the read/write goes through
    /// <see cref="IGenericType.Instantiations"/>'s <c>Map&lt;string, TypeReference&gt;</c> view — a
    /// <c>Dictionary&lt;string, ITypeReference&gt;</c>, created with the ordinal comparer the class/interface and tuple
    /// targets use (Checker.Queries.cs:675, Checker.TypeReferences.cs:1740).
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>Map.get</c>/<c>set</c> pair is the port's <c>TryGetValue</c>/indexer; the reference relies on
    /// the target's <c>instantiations</c> having been created by the class/interface/tuple constructors, and the port's
    /// <c>??=</c> also covers the <c>null</c> a freshly built <c>GenericType</c> starts with.
    /// </para>
    /// </remarks>
    internal ITypeReference CreateTypeReference(IGenericType target, List<IType>? typeArguments)
    {
        var id = GetTypeListId(typeArguments);
        var instantiations = target.Instantiations ??= new Dictionary<string, ITypeReference>(StringComparer.Ordinal);
        if (!instantiations.TryGetValue(id, out var type))
        {
            type = (ITypeReference)CreateObjectType(ObjectFlags.Reference, target.Symbol);
            instantiations[id] = type;
            type.ObjectFlags |= typeArguments is not null ? GetPropagatingFlagsOfTypes(typeArguments) : ObjectFlags.None;
            type.Target = target;
            type.ResolvedTypeArguments = typeArguments;
        }

        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:16935-16941 — <c>cloneTypeReference</c>.</summary>
    internal ITypeReference CloneTypeReference(ITypeReference source)
    {
        var type = (ITypeReference)CreateTypeWithSymbol(source.Flags, source.Symbol!);
        type.ObjectFlags = source.ObjectFlags;
        type.Target = source.Target;
        type.ResolvedTypeArguments = source.ResolvedTypeArguments;
        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:16943-16952 — <c>createDeferredTypeReference</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>node</c> parameter is the <c>TypeReferenceNode | ArrayTypeNode | TupleTypeNode</c> union; the
    /// port takes <see cref="Node"/>, the union's common base. The <c>mapper?</c> slot is the port's <c>object?</c> its
    /// callers cast (Model/TypeReference.cs's remarks).
    /// </remarks>
    internal IDeferredTypeReference CreateDeferredTypeReference(IGenericType target, Node node, TypeMapper? mapper = null, ISymbol? aliasSymbol = null, List<IType>? aliasTypeArguments = null)
    {
        if (aliasSymbol is null)
        {
            aliasSymbol = GetAliasSymbolForTypeNode(node);
            var localAliasTypeArguments = GetTypeArgumentsForAliasSymbol(aliasSymbol);
            aliasTypeArguments = mapper is not null ? InstantiateTypes(localAliasTypeArguments, mapper) : localAliasTypeArguments;
        }

        var type = (IDeferredTypeReference)CreateObjectType(ObjectFlags.Reference, target.Symbol);
        type.Target = target;
        type.Node = node;
        type.Mapper = mapper;
        type.AliasSymbol = aliasSymbol;
        type.AliasTypeArguments = aliasTypeArguments;
        return type;
    }

    // ---------------------------------------------------------------------------------------------
    // Whether a type can reference a type parameter (checker.ts:26385-26409)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:26385-26401 — <c>couldContainTypeVariables</c>.</summary>
    /// <remarks>
    /// <para>
    /// Return true if the given type could possibly reference a type parameter for which we perform type inference
    /// (i.e. a type parameter of a generic function). We cache results for union and intersection types for performance
    /// reasons.
    /// </para>
    /// <para>
    /// PORT-NOTE: the reference writes the flag pair through <c>(type as ObjectFlagsType).objectFlags |= …</c>, and the
    /// port calls the same writer <c>Checker.Instantiation.cs</c>'s <c>getObjectTypeInstantiation</c> uses for exactly
    /// that pair (<c>OrObjectFlags</c>, a private static member of this class, hence reachable from this partial file).
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>getTypeArguments(type as TypeReference)</c> takes the reference face; the <c>node</c> test reads
    /// <c>TypeReference.node</c> off that same face. The <c>symbol &amp;&amp; symbol.flags &amp; (…)</c> arm is JS
    /// truthiness over an optional symbol, written out as <c>is not null</c> plus a flag test.
    /// </para>
    /// </remarks>
    internal bool CouldContainTypeVariables(IType type)
    {
        var objectFlags = GetObjectFlags(type);
        if ((objectFlags & ObjectFlags.CouldContainTypeVariablesComputed) != 0)
        {
            return (objectFlags & ObjectFlags.CouldContainTypeVariables) != 0;
        }

        var result = (type.Flags & TypeFlags.Instantiable) != 0
            || ((type.Flags & TypeFlags.Object) != 0 && !IsNonGenericTopLevelType(type) && (
                    ((objectFlags & ObjectFlags.Reference) != 0
                        && (((ITypeReference)type).Node is not null || Core.Some(GetTypeArguments((ITypeReference)type), CouldContainTypeVariables)))
                    || ((objectFlags & ObjectFlags.Anonymous) != 0
                        && type.Symbol is not null
                        && (type.Symbol.Flags & (SymbolFlags.Function | SymbolFlags.Method | SymbolFlags.Class | SymbolFlags.TypeLiteral | SymbolFlags.ObjectLiteral)) != 0
                        && type.Symbol.Declarations is not null)
                    || (objectFlags & (ObjectFlags.Mapped | ObjectFlags.ReverseMapped | ObjectFlags.ObjectRestType | ObjectFlags.InstantiationExpressionType)) != 0))
            || ((type.Flags & TypeFlags.UnionOrIntersection) != 0
                && (type.Flags & TypeFlags.EnumLiteral) == 0
                && !IsNonGenericTopLevelType(type)
                && Core.Some(((IUnionOrIntersectionType)type).Types, CouldContainTypeVariables));
        if ((type.Flags & TypeFlags.ObjectFlagsType) != 0)
        {
            OrObjectFlags(type, ObjectFlags.CouldContainTypeVariablesComputed | (result ? ObjectFlags.CouldContainTypeVariables : ObjectFlags.None));
        }

        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:26403-26409 — <c>isNonGenericTopLevelType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>findAncestor(declaration.parent, n =&gt; … : "quit")</c> is the port's quit-capable
    /// <see cref="Utilities.FindAncestor"/> overload (a <c>null</c> answer stops the walk); the declaration arrives as
    /// the <c>Declaration</c> face of <c>getDeclarationOfKind</c>, which the port's <c>Utilities</c> member answers as a
    /// <see cref="TypeAliasDeclaration"/> for the type argument used here.
    /// </remarks>
    internal bool IsNonGenericTopLevelType(IType type)
    {
        if (type.AliasSymbol is not null && type.AliasTypeArguments is null)
        {
            var declaration = Utilities.GetDeclarationOfKind<TypeAliasDeclaration>(type.AliasSymbol, SyntaxKind.TypeAliasDeclaration);
            return declaration is not null
                && Utilities.FindAncestor(
                    declaration.Parent,
                    n => n.Kind == SyntaxKind.SourceFile ? true : n.Kind == SyntaxKind.ModuleDeclaration ? false : (bool?)null) is not null;
        }

        return false;
    }

    // ---------------------------------------------------------------------------------------------
    // Is this a JavaScript constructor? (checker.ts:37696-37715)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:37696-37715 — <c>isJSConstructor</c>.</summary>
    /// <remarks>
    /// <para>
    /// Indicates whether a declaration can be treated as a constructor in a JavaScript file.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS narrows <c>node</c> through the <c>||</c> of its two declaration tests and then reads
    /// <c>node.initializer</c>; C# has no equivalent narrowing of a class-typed value, so the port selects the slot
    /// with the pattern switch in <see cref="InitializerOfNode"/> — the same two classes the reference's guard admits.
    /// The <c>@class</c>/<c>@constructor</c> tag test is the ported <see cref="Utilities.GetJSDocClassTag"/> and the
    /// parenthesized-expression walk is the ported <see cref="Utilities.WalkUpParenthesizedExpressions"/>; the
    /// symbol-with-members test is <c>!!symbol?.members?.size</c>, i.e. a non-null table with at least one entry.
    /// </para>
    /// </remarks>
    internal bool IsJSConstructor(Node? node)
    {
        if (node is null || !Utilities.IsInJSFile(node))
        {
            return false;
        }

        var func = NodeTests.IsFunctionDeclaration(node) || NodeTests.IsFunctionExpression(node) ? node
            : (NodeTests.IsVariableDeclaration(node) || NodeTests.IsPropertyAssignment(node)) && InitializerOfNode(node) is { } initializer && NodeTests.IsFunctionExpression(initializer) ? initializer
            : null;
        if (func is not null)
        {
            // If the node has a @class or @constructor tag, treat it like a constructor.
            if (Utilities.GetJSDocClassTag(node) is not null)
            {
                return true;
            }

            // If the node is a property of an object literal.
            if (NodeTests.IsPropertyAssignment(Utilities.WalkUpParenthesizedExpressions(func.Parent!)))
            {
                return false;
            }

            // If the symbol of the node has members, treat it like a constructor.
            // PORT-NOTE: `getSymbolOfDeclaration` is ported as the IDeclaration-taking member (`getSymbolOfNode` is the
            // Node-taking one), and TS's `func` is the FunctionDeclaration | FunctionExpression union, so the node face
            // reaches it through the cast the reference's narrowing supplies for free.
            var symbol = GetSymbolOfDeclaration((IDeclaration)func);
            return symbol.Members is { Count: > 0 };
        }

        return false;
    }

    /// <summary>The <c>node.initializer</c> read of <see cref="IsJSConstructor"/> (checker.ts:37700-37702).</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads <c>node.initializer</c> off the union its guard narrowed to
    /// <c>VariableDeclaration | PropertyAssignment</c>; C# names those two faces explicitly, which is that union.
    /// </remarks>
    private static Expression? InitializerOfNode(Node node) => node switch
    {
        VariableDeclaration variableDeclaration => variableDeclaration.Initializer,
        PropertyAssignment propertyAssignment => propertyAssignment.Initializer,
        _ => null,
    };

    // ---------------------------------------------------------------------------------------------
    // The object flags of a type (utilities.ts:8280-8282)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:8280-8282 — <c>getObjectFlags</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: this member belongs to the <c>utilities.ts</c> slice upstream, which this milestone has not ported.
    /// It is declared here as a member of <see cref="Checker"/> because four landed sibling slices call it by bare name
    /// (<c>Checker.Instantiation.cs</c>'s <c>getObjectTypeInstantiation</c>, its <c>instantiateMappedType</c>,
    /// <c>Checker.Queries.cs</c>'s <c>getApparentType</c>, <c>Checker.IndexAccess.cs</c>'s
    /// <c>getGenericObjectFlags</c>), and the <c>utilities.ts</c> slice replaces it with a <c>Utilities.*</c> copy at
    /// cutover.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS reads <c>(type as ObjectFlagsType).objectFlags</c>, which answers the slot for every type and
    /// <c>undefined</c> (0 in the bit tests) for the rest. C# has no common view of the four faces, so the port
    /// switches over the interfaces that declare the slot — the precedent is <c>Core/Tracing.cs:628-638</c>, and
    /// <c>Checker.Instantiation.cs</c>'s <c>OrObjectFlags</c> is the writer of the same switch.
    /// </para>
    /// </remarks>
    internal static ObjectFlags GetObjectFlags(IType type) => (type.Flags & TypeFlags.ObjectFlagsType) != 0
        ? type switch
        {
            ISubstitutionType substitutionType => substitutionType.ObjectFlags,
            IIntrinsicType intrinsicType => intrinsicType.ObjectFlags,
            IObjectType objectType => objectType.ObjectFlags,
            IUnionOrIntersectionType unionOrIntersectionType => unionOrIntersectionType.ObjectFlags,
            _ => ObjectFlags.None,
        }
        : ObjectFlags.None;

    // ---------------------------------------------------------------------------------------------
    // The checker.ts helpers outside the assigned bands
    //
    // The reference declarations these port: isInstantiatedModule (checker.ts:1479-1483, file scope
    // above createTypeChecker), isDefinitelyReferenceToGlobalSymbolObject (:2414-2426),
    // isTypeInvalidDueToUnionDiscriminant (:15119-15127), getAllPossiblePropertiesOfTypes
    // (:15129-15146), tryFindAmbientModule (:16078-16085), the return-type-of-signature trio
    // (:16543-16560), getInferredTypeParameterConstraint (:16764-16839), getParentSymbolOfTypeParameter
    // (:16866-16870), the context-sensitivity family (:21180-21267), getExternalModuleFileFromDeclaration
    // (:51546-51553), filterPrimitivesIfContainsNonPrimitive (:53730-53738) and the four
    // duplicate-declaration helpers at the reference file's tail (:54202-54258).
    //
    // PORT-NOTE: TS declares five of them (isInstantiatedModule, isNotAccessor, isNotOverload,
    // isDeclarationNameOrImportPropertyName, signatureHasLiteralTypes) at checker.ts file scope and the
    // rest as locals of createTypeChecker; the port states every one as a member of this class, which is
    // where their landed call sites — all inside Checker partials — call them by bare name. §15.15's
    // Utilities rule does not reach them: none of them is declared in utilities.ts.
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:1479-1483 — <c>isInstantiatedModule</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>getModuleInstanceState</c> is the binder's (binder.ts:330), ported as
    /// <see cref="Binder.GetModuleInstanceState"/>; its other caller, Checker.Check.Classes.cs, wrote
    /// <c>Utilities.IsInstantiatedModule</c> while Checker.Check.Modules.cs called it by bare name — the two
    /// agreed on <c>Checker</c> and the qualified call site dropped its qualifier.
    /// </remarks>
    internal bool IsInstantiatedModule(ModuleDeclaration node, bool preserveConstEnums)
    {
        var moduleState = Binder.GetModuleInstanceState(node);
        return moduleState == ModuleInstanceState.Instantiated
            || (preserveConstEnums && moduleState == ModuleInstanceState.ConstEnumOnly);
    }

    /// <summary>ported from: src/compiler/checker.ts:2414-2426 — <c>isDefinitelyReferenceToGlobalSymbolObject</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the reference reads the slots off the union its three guards narrowed; C# keeps the declared face and
    /// states each narrowed read as a cast, the same shape <c>Checker.Relations.Identical.cs</c>'s node walks use. The
    /// parameter is <c>Node?</c> because that is what both interfaces this forwards to declare
    /// (<c>IEmitResolver</c>, <c>ISyntacticTypeNodeBuilderResolver</c>; Model/NodeInterfaces.cs:842, :3315) and what
    /// both forwarding call sites pass; the reference's non-optional parameter is why the extra absent-node arm answers
    /// <c>false</c> rather than asserting.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>getGlobalSymbol(…, undefined) || unknownSymbol</c> is <c>GetGlobalSymbol(…) ?? _unknownSymbol</c> —
    /// the reference's <c>||</c> tests truthiness of an object that is never falsy when present; <c>getResolvedSymbol</c>
    /// answers the unknown symbol rather than <c>undefined</c> for an unresolved name.
    /// </para>
    /// </remarks>
    internal bool IsDefinitelyReferenceToGlobalSymbolObject(Node? node)
    {
        if (node is null || !NodeTests.IsPropertyAccessExpression(node))
        {
            return false;
        }

        var access = (PropertyAccessExpression)node;
        if (!NodeTests.IsIdentifier(access.Name!))
        {
            return false;
        }

        if (!NodeTests.IsPropertyAccessExpression(access.Expression!) && !NodeTests.IsIdentifier(access.Expression!))
        {
            return false;
        }

        if (NodeTests.IsIdentifier(access.Expression!))
        {
            // Exactly `Symbol.something` and `Symbol` either does not resolve or definitely resolves to the global Symbol
            var expression = (Identifier)access.Expression!;
            return Utilities.IdText(expression) == "Symbol"
                && ReferenceEquals(
                    GetResolvedSymbol(expression),
                    GetGlobalSymbol("Symbol", SymbolFlags.Value | SymbolFlags.ExportValue, /*diagnostic*/ null) ?? _unknownSymbol);
        }

        var outerAccess = (PropertyAccessExpression)access.Expression!;
        if (!NodeTests.IsIdentifier(outerAccess.Expression!))
        {
            return false;
        }

        // Exactly `globalThis.Symbol.something` and `globalThis` resolves to the global `globalThis`
        return Utilities.IdText(outerAccess.Name!) == "Symbol"
            && Utilities.IdText(outerAccess.Expression!) == "globalThis"
            && ReferenceEquals(GetResolvedSymbol((Identifier)outerAccess.Expression!), _globalThisSymbol);
    }

    /// <summary>ported from: src/compiler/checker.ts:15119-15127 — <c>isTypeInvalidDueToUnionDiscriminant</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the reference's <c>obj: ObjectLiteralExpression | JsxAttributes</c> is what the generated
    /// <c>ITypeChecker</c> slot widens to <see cref="MemberExpression"/>, and the two properties lists (whose
    /// element union TS states as <c>ObjectLiteralElementLike | JsxAttributeLike</c>) are read through the same
    /// switch, since <see cref="MemberExpression"/> itself carries no <c>properties</c> slot.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>property.name</c> off that element union is
    /// <see cref="Utilities.GetNameOfDeclaration"/>'s shape — an optional slot every member carries — so the port
    /// reads it through <see cref="PropertyNameOfElement"/> (the port's <see cref="JsxAttribute"/> implements no
    /// named-declaration interface, so an <c>INamedDeclaration</c> pattern alone would drop JSX attributes).
    /// </para>
    /// </remarks>
    internal bool IsTypeInvalidDueToUnionDiscriminant(IType? contextualType, MemberExpression? obj)
    {
        var list = obj switch
        {
            ObjectLiteralExpression objectLiteral => (IList<Node>?)objectLiteral.Properties,
            JsxAttributes jsxAttributes => jsxAttributes.Properties,
            _ => null,
        };
        return Core.Some(list, property =>
        {
            var propertyName = PropertyNameOfElement(property);
            var nameType = propertyName is not null
                ? NodeTests.IsJsxNamespacedName(propertyName)
                    ? GetStringLiteralType(Utilities.GetTextOfJsxAttributeName(propertyName))
                    : GetLiteralTypeFromPropertyName(propertyName)
                : null;
            var name = nameType is not null && Utilities.IsTypeUsableAsPropertyName(nameType)
                ? Utilities.GetPropertyNameFromType(nameType)
                : null;
            var expected = name is null ? null : GetTypeOfPropertyOfType(contextualType, name);
            return expected is not null && IsLiteralType(expected) && !IsTypeAssignableTo(GetTypeOfNode(property), expected);
        });
    }

    /// <summary>The <c>property.name</c> read of <see cref="IsTypeInvalidDueToUnionDiscriminant"/> (checker.ts:15121).</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads the optional <c>name</c> of the <c>ObjectLiteralElementLike | JsxAttributeLike</c> element
    /// union; the port's <see cref="JsxAttribute"/> has the slot but implements no named-declaration interface, so the
    /// switch names it first and then takes the interface the object-literal elements do implement.
    /// </remarks>
    private static Node? PropertyNameOfElement(Node property) => property switch
    {
        JsxAttribute jsxAttribute => jsxAttribute.Name,
        INamedDeclaration namedDeclaration => namedDeclaration.Name,
        _ => null,
    };

    /// <summary>ported from: src/compiler/checker.ts:15129-15146 — <c>getAllPossiblePropertiesOfTypes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the generated <c>ITypeChecker</c> slot passes a nullable list, so the parameter is
    /// <c>List&lt;IType&gt;?</c> and the one read asserts it the way the wrapper's sibling slots do; TS's
    /// <c>createSymbolTable</c> is the ported <see cref="Utilities.CreateSymbolTable"/>, <c>Map.has</c>/<c>Map.set</c>
    /// are <see cref="SymbolTable.ContainsKey"/>/its indexer, and <c>arrayFrom(props.values())</c> is the
    /// collection expression over <see cref="SymbolTable.Values"/>.
    /// </remarks>
    internal List<ISymbol> GetAllPossiblePropertiesOfTypes(List<IType>? types)
    {
        var unionType = GetUnionType(types!);
        if ((unionType.Flags & TypeFlags.Union) == 0)
        {
            return GetAugmentedPropertiesOfType(unionType);
        }

        var props = Utilities.CreateSymbolTable();
        foreach (var memberType in types!)
        {
            foreach (var member in GetAugmentedPropertiesOfType(memberType))
            {
                var escapedName = member.EscapedName!;
                if (!props.ContainsKey(escapedName))
                {
                    var prop = CreateUnionOrIntersectionProperty((UnionOrIntersectionType)unionType, escapedName);
                    // May be undefined if the property is private
                    if (prop is not null)
                    {
                        props[escapedName] = prop;
                    }
                }
            }
        }

        return [.. props.Values];
    }

    /// <summary>ported from: src/compiler/checker.ts:16078-16085 — <c>tryFindAmbientModule</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>globals</c> is the checker's global <see cref="SymbolTable"/> slot
    /// (<c>_globals</c>), and <c>'"' + moduleName + '"' as __String</c> is that same string, since the port's
    /// <c>__String</c> is <c>string</c>.
    /// </remarks>
    internal ISymbol? TryFindAmbientModule(string moduleName, bool withAugmentations)
    {
        if (Utilities.IsExternalModuleNameRelative(moduleName))
        {
            return null;
        }

        var symbol = GetSymbol(_globals, "\"" + moduleName + "\"", SymbolFlags.ValueModule);
        // merged symbol is module declaration symbol combined with all augmentations
        return symbol is not null && withAugmentations ? GetMergedSymbol(symbol) : symbol;
    }

    /// <summary>ported from: src/compiler/checker.ts:16543-16546 — <c>isResolvingReturnTypeOfSignature</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>signature.compositeSignatures &amp;&amp; some(…)</c> is <see cref="Core.Some"/>, which is
    /// already false for the absent list the <c>&amp;&amp;</c> guards.
    /// </remarks>
    internal bool IsResolvingReturnTypeOfSignature(ISignature signature)
        => Core.Some(signature.CompositeSignatures, IsResolvingReturnTypeOfSignature)
            || signature.ResolvedReturnType is null && FindResolutionCycleStartIndex(signature, TypeSystemPropertyName.ResolvedReturnType) >= 0;

    /// <summary>ported from: src/compiler/checker.ts:16548-16550 — <c>getRestTypeOfSignature</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>tryGetRestTypeOfSignature(signature) || anyType</c> tests truthiness of a type, which is
    /// never falsy when present, so the port's <c>??</c> is the same answer.
    /// </remarks>
    internal IType GetRestTypeOfSignature(ISignature signature) => TryGetRestTypeOfSignature(signature) ?? _anyType;

    /// <summary>ported from: src/compiler/checker.ts:16552-16560 — <c>tryGetRestTypeOfSignature</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>isTupleType(sigRestType) ? getRestTypeOfTupleType(sigRestType) : sigRestType</c> narrows to the
    /// tuple face in TS; the port states the cast <see cref="GetRestTypeOfTupleType"/>'s parameter asks for.
    /// </remarks>
    internal IType? TryGetRestTypeOfSignature(ISignature signature)
    {
        if (SignatureHasRestParameter(signature))
        {
            var parameters = signature.Parameters!;
            var sigRestType = GetTypeOfSymbol(parameters[parameters.Count - 1]);
            var restType = IsTupleType(sigRestType) ? GetRestTypeOfTupleType((ITypeReference)sigRestType) : sigRestType;
            return restType is not null ? GetIndexTypeOfType(restType, _numberType) : null;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:16764-16839 — <c>getInferredTypeParameterConstraint</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: TS's <c>typeParameter.symbol?.declarations</c> guard becomes the null-conditional test on the
    /// symbol's list; <c>declaration</c> is the <c>Declaration</c> face, so each <c>declaration.parent</c> read is the
    /// port's cast to <see cref="Node"/>.
    /// </para>
    /// <para>
    /// PORT-NOTE: the destructuring <c>const [childTypeParameter = declaration.parent, grandParent] = walkUp…</c>
    /// becomes two statements: the walk (the ported
    /// <see cref="Utilities.WalkUpParenthesizedTypesAndGetParentAndChild"/>) and the reference's default for the
    /// undefined first element.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>stringNumberSymbolType</c>, <c>unknownType</c> and <c>stringType</c> are the port's
    /// <c>_stringNumberSymbolType</c>, <c>_unknownType</c> and <c>_stringType</c>; <c>append(inferences, x)</c> is
    /// <see cref="Core.Append{T}"/>, whose absent-input answer the reference states as a fresh one-element array.
    /// </para>
    /// </remarks>
    internal IType? GetInferredTypeParameterConstraint(ITypeParameter typeParameter, bool omitTypeReferences = false)
    {
        List<IType>? inferences = null;
        if (typeParameter.Symbol?.Declarations is { } declarations)
        {
            foreach (var declaration in declarations)
            {
                var declarationNode = (Node)declaration;
                if (declarationNode.Parent!.Kind != SyntaxKind.InferType)
                {
                    continue;
                }

                // When an 'infer T' declaration is immediately contained in a type reference node
                // (such as 'Foo<infer T>'), T's constraint is inferred from the constraint of the
                // corresponding type parameter in 'Foo'. When multiple 'infer T' declarations are
                // present, we form an intersection of the inferred constraint types.
                var (childTypeParameter, grandParent) =
                    Utilities.WalkUpParenthesizedTypesAndGetParentAndChild(declarationNode.Parent!.Parent!);
                var childTypeParameterNode = (Node?)childTypeParameter ?? declarationNode.Parent!;
                if (grandParent.Kind == SyntaxKind.TypeReference && !omitTypeReferences)
                {
                    var typeReference = (TypeReferenceNode)grandParent;
                    var typeParameters = GetTypeParametersForTypeReferenceOrImport(typeReference);
                    if (typeParameters is not null)
                    {
                        var index = typeReference.TypeArguments!.IndexOf((ITypeNode)childTypeParameterNode);
                        if (index < typeParameters.Count)
                        {
                            var declaredConstraint = GetConstraintOfTypeParameter(typeParameters[index]);
                            if (declaredConstraint is not null)
                            {
                                // Type parameter constraints can reference other type parameters so
                                // constraints need to be instantiated. If instantiation produces the
                                // type parameter itself, we discard that inference. For example, in
                                //   type Foo<T extends string, U extends T> = [T, U];
                                //   type Bar<T> = T extends Foo<infer X, infer X> ? Foo<X, X> : T;
                                // the instantiated constraint for U is X, so we discard that inference.
                                var mapper = MakeDeferredTypeMapper(
                                    typeParameters,
                                    Core.Map(
                                        typeParameters,
                                        (ITypeParameter _, int i) => (Func<IType>)(() => GetEffectiveTypeArgumentAtIndex(typeReference, typeParameters, i)))!);
                                var constraint = InstantiateType(declaredConstraint, mapper)!;
                                if (!ReferenceEquals(constraint, typeParameter))
                                {
                                    inferences = (List<IType>?)Core.Append(inferences, constraint);
                                }
                            }
                        }
                    }
                }
                // When an 'infer T' declaration is immediately contained in a rest parameter declaration, a rest type
                // or a named rest tuple element, we infer an 'unknown[]' constraint.
                else if (
                    grandParent.Kind == SyntaxKind.Parameter && ((ParameterDeclaration)grandParent).DotDotDotToken is not null
                    || grandParent.Kind == SyntaxKind.RestType
                    || grandParent.Kind == SyntaxKind.NamedTupleMember && ((NamedTupleMember)grandParent).DotDotDotToken is not null)
                {
                    inferences = (List<IType>?)Core.Append(inferences, CreateArrayType(_unknownType));
                }
                // When an 'infer T' declaration is immediately contained in a string template type, we infer a 'string'
                // constraint.
                else if (grandParent.Kind == SyntaxKind.TemplateLiteralTypeSpan)
                {
                    inferences = (List<IType>?)Core.Append(inferences, _stringType);
                }
                // When an 'infer T' declaration is in the constraint position of a mapped type, we infer a 'keyof any'
                // constraint.
                else if (grandParent.Kind == SyntaxKind.TypeParameter && grandParent.Parent!.Kind == SyntaxKind.MappedType)
                {
                    inferences = (List<IType>?)Core.Append(inferences, _stringNumberSymbolType);
                }
                // When an 'infer T' declaration is the template of a mapped type, and that mapped type is the extends
                // clause of a conditional whose check type is also a mapped type, give it a constraint equal to the template
                // of the check type's mapped type
                else if (
                    grandParent.Kind == SyntaxKind.MappedType && ((MappedTypeNode)grandParent).Type is { } mappedTypeTemplate
                    && ReferenceEquals(Utilities.SkipParentheses((Node)mappedTypeTemplate), declarationNode.Parent)
                    && grandParent.Parent is ConditionalTypeNode enclosingConditional
                    && ReferenceEquals(enclosingConditional.ExtendsType, grandParent)
                    && enclosingConditional.CheckType is MappedTypeNode { Type: not null } checkMappedType)
                {
                    var nodeType = GetTypeFromTypeNode(checkMappedType.Type!);
                    inferences = (List<IType>?)Core.Append(
                        inferences,
                        InstantiateType(
                            nodeType,
                            MakeUnaryTypeMapper(
                                GetDeclaredTypeOfTypeParameter(GetSymbolOfDeclaration(checkMappedType.TypeParameter!)!),
                                checkMappedType.TypeParameter!.Constraint is not null
                                    ? GetTypeFromTypeNode(checkMappedType.TypeParameter.Constraint)
                                    : _stringNumberSymbolType)));
                }
            }
        }

        return inferences is not null ? GetIntersectionType(inferences) : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:16866-16870 — <c>getParentSymbolOfTypeParameter</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>getDeclarationOfKind&lt;TypeParameterDeclaration&gt;</c> is the ported generic
    /// <see cref="Utilities.GetDeclarationOfKind{T}"/>; the host of a <c>@template</c> tag is the
    /// <see cref="Utilities.GetEffectiveContainerForJSDocTemplateTag"/> answer, which the port returns as the node
    /// face and <see cref="GetSymbolOfNode"/> takes.
    /// </remarks>
    internal ISymbol? GetParentSymbolOfTypeParameter(ITypeParameter typeParameter)
    {
        var tp = Utilities.GetDeclarationOfKind<TypeParameterDeclaration>(typeParameter.Symbol!, SyntaxKind.TypeParameter)!;
        Node? host = NodeTests.IsJSDocTemplateTag(tp.Parent!)
            ? (Node?)Utilities.GetEffectiveContainerForJSDocTemplateTag((JSDocTemplateTag)tp.Parent!)
            : tp.Parent;
        return host is not null ? GetSymbolOfNode(host) : null;
    }

    /// <summary>Returns true if the given expression contains (at any level of nesting) a function or arrow expression
    /// that is subject to contextual typing.</summary>
    /// <remarks>
    /// <para>ported from: src/compiler/checker.ts:21180-21218 — <c>isContextSensitive</c>.</para>
    /// <para>
    /// PORT-NOTE: the reference's parameter is the <c>Expression | MethodDeclaration | ObjectLiteralElementLike |
    /// JsxAttributeLike | JsxChild</c> union, which the port's <see cref="Node"/> is the upper bound of; the
    /// deliberately dead <c>methodDeclaration</c> case of the reference's assert is kept against the same
    /// <see cref="Utilities.IsObjectLiteralMethod"/> test.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>node.parent.parent.children</c> is read through <see cref="JsxChildrenOfOpeningElement"/>, since
    /// only the <c>JsxElement</c>/<c>JsxFragment</c> classes the guard admits carry the slot; the two literal-bag
    /// cases (<c>JsxAttribute</c>, <c>JsxExpression | YieldExpression</c>) select their class explicitly, because C#
    /// cannot reach an optional slot through the widened union.
    /// </para>
    /// </remarks>
    internal bool IsContextSensitive(Node? node)
    {
        if (node is null)
        {
            return false;
        }

        Debug.Assert(node.Kind != SyntaxKind.MethodDeclaration || Utilities.IsObjectLiteralMethod(node));
        switch (node.Kind)
        {
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.FunctionDeclaration: // Function declarations can have context when annotated with a jsdoc @type
                return IsContextSensitiveFunctionLikeDeclaration(node);
            case SyntaxKind.ObjectLiteralExpression:
                return Core.Some(((ObjectLiteralExpression)node).Properties, IsContextSensitive);
            case SyntaxKind.ArrayLiteralExpression:
                return Core.Some(((ArrayLiteralExpression)node).Elements, IsContextSensitive);
            case SyntaxKind.ConditionalExpression:
                return IsContextSensitive(((ConditionalExpression)node).WhenTrue)
                    || IsContextSensitive(((ConditionalExpression)node).WhenFalse);
            case SyntaxKind.BinaryExpression:
                var binary = (BinaryExpression)node;
                return (binary.OperatorToken!.Kind == SyntaxKind.BarBarToken
                        || binary.OperatorToken.Kind == SyntaxKind.QuestionQuestionToken)
                    && (IsContextSensitive(binary.Left) || IsContextSensitive(binary.Right));
            case SyntaxKind.PropertyAssignment:
                return IsContextSensitive(((PropertyAssignment)node).Initializer);
            case SyntaxKind.ParenthesizedExpression:
                return IsContextSensitive(((ParenthesizedExpression)node).Expression);
            case SyntaxKind.JsxAttributes:
                return Core.Some(((JsxAttributes)node).Properties, IsContextSensitive)
                    || NodeTests.IsJsxOpeningElement(node.Parent!)
                        && JsxChildrenOfOpeningElement(node.Parent!.Parent!) is { } jsxChildren
                        && Core.Some(jsxChildren, IsContextSensitive);
            case SyntaxKind.JsxAttribute:
                // If there is no initializer, JSX attribute has a boolean value of true which is not context sensitive.
                return ((JsxAttribute)node).Initializer is { } attributeInitializer && IsContextSensitive(attributeInitializer);
            case SyntaxKind.JsxExpression:
            case SyntaxKind.YieldExpression:
                // It is possible to that node.expression is undefined (e.g <div x={} />)
                var bagExpression = node switch
                {
                    JsxExpression jsxExpression => jsxExpression.Expression,
                    YieldExpression yieldExpression => yieldExpression.Expression,
                    _ => null,
                };
                return bagExpression is not null && IsContextSensitive(bagExpression);
        }

        return false;
    }

    /// <summary>The <c>node.parent.parent.children</c> read of <see cref="IsContextSensitive"/> (checker.ts:21201).</summary>
    /// <remarks>
    /// PORT-NOTE: TS reaches <c>children</c> off the <c>JsxElement</c> its <c>isJsxOpeningElement</c> guard narrowed the
    /// parent to; the port's <see cref="Node"/> carries no such slot, so the two classes that declare it are named.
    /// </remarks>
    private static NodeArray<Node>? JsxChildrenOfOpeningElement(Node node) => node switch
    {
        JsxElement jsxElement => jsxElement.Children,
        JsxFragment jsxFragment => jsxFragment.Children,
        _ => null,
    };

    /// <summary>ported from: src/compiler/checker.ts:21220-21222 — <c>isContextSensitiveFunctionLikeDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>FunctionLikeDeclaration</c> is the port's widened <see cref="Node"/>;
    /// <c>hasContextSensitiveParameters</c> is the <c>utilities.ts:10880</c> helper that the port states as
    /// <see cref="Utilities.HasContextSensitiveParameters"/>. The two <c>has…</c> helpers beside it are file-local
    /// upstream, so they stay <c>private</c> here.
    /// </remarks>
    private bool IsContextSensitiveFunctionLikeDeclaration(Node node)
        => Utilities.HasContextSensitiveParameters(node)
            || HasContextSensitiveReturnExpression(node)
            || HasContextSensitiveYieldExpression(node);

    /// <summary>ported from: src/compiler/checker.ts:21224-21232 — <c>hasContextSensitiveReturnExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference reads <c>typeParameters</c>/<c>returnType</c>/<c>body</c> off the
    /// <c>FunctionLikeDeclaration</c> union; the port takes the <see cref="IFunctionLikeDeclarationBase"/> face the
    /// widened <see cref="Node"/> reaches through a cast, and its <c>body</c> is the <c>Block | Expression</c> union
    /// TS's <c>ConciseBody</c> is — so the non-block arm is a <see cref="Node"/> and the block arm the ported
    /// <see cref="Utilities.ForEachReturnStatement{T}"/>, whose <c>T | undefined</c> answer the reference's <c>!!</c>
    /// collapses.
    /// </remarks>
    private bool HasContextSensitiveReturnExpression(Node node)
    {
        var declaration = (IFunctionLikeDeclarationBase)node;
        if (declaration.TypeParameters is not null
            || Utilities.GetEffectiveReturnTypeNode(node) is not null
            || declaration.Body is null)
        {
            return false;
        }

        if (declaration.Body.Kind != SyntaxKind.Block)
        {
            return IsContextSensitive(declaration.Body);
        }

        return Utilities.ForEachReturnStatement<bool>(
            (Block)declaration.Body,
            statement => statement.Expression is not null && IsContextSensitive(statement.Expression)) == true;
    }

    /// <summary>ported from: src/compiler/checker.ts:21234-21243 — <c>hasContextSensitiveYieldExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>node.body &amp;&amp; forEachYieldExpression(node.body as Block, …)</c> is the port's
    /// <c>is Block</c> test — a generator's body is always a block, and the cast the reference writes is the same
    /// assumption; <c>getFunctionFlags</c> is the ported <see cref="Utilities.GetFunctionFlags"/>.
    /// </remarks>
    private bool HasContextSensitiveYieldExpression(Node node)
    {
        if ((Utilities.GetFunctionFlags(node) & FunctionFlags.Generator) == 0)
        {
            return false;
        }

        return ((IFunctionLikeDeclarationBase)node).Body is Block body
            && Utilities.ForEachYieldExpression<bool>(body, IsContextSensitive) == true;
    }

    /// <summary>ported from: src/compiler/checker.ts:21245-21248 — <c>isContextSensitiveFunctionOrObjectLiteralMethod</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's member is a type guard (<c>func is FunctionExpression | ArrowFunction | MethodDeclaration</c>)
    /// whose narrowed type has no C# name, so the port answers the guard's boolean; <c>isFunctionExpressionOrArrowFunction</c>
    /// and <c>isObjectLiteralMethod</c> are the ported <see cref="Utilities.IsFunctionExpressionOrArrowFunction"/> and
    /// <see cref="Utilities.IsObjectLiteralMethod"/>.
    /// </remarks>
    internal bool IsContextSensitiveFunctionOrObjectLiteralMethod(Node node)
        => (Utilities.IsFunctionExpressionOrArrowFunction(node) || Utilities.IsObjectLiteralMethod(node))
            && IsContextSensitiveFunctionLikeDeclaration(node);

    /// <summary>ported from: src/compiler/checker.ts:21250-21267 — <c>getTypeWithoutSignatures</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>resolved.constructSignatures.length || resolved.callSignatures.length</c> reads two
    /// <c>readonly Signature[]</c> slots that the port states as nullable lists, so each length is the absent-tolerant
    /// count; <c>createObjectType(ObjectFlags.Anonymous, type.symbol)</c> is the ported
    /// <see cref="CreateObjectType"/>, whose <see cref="ObjectType"/> carries all five assigned slots, and TS's
    /// <c>emptyArray</c> is the port's empty collection expression.
    /// </remarks>
    internal IType GetTypeWithoutSignatures(IType type)
    {
        if ((type.Flags & TypeFlags.Object) != 0)
        {
            var resolved = ResolveStructuredTypeMembers((UnionOrIntersectionType)type);
            if ((resolved.ConstructSignatures?.Count ?? 0) != 0 || (resolved.CallSignatures?.Count ?? 0) != 0)
            {
                var result = CreateObjectType(ObjectFlags.Anonymous, type.Symbol);
                result.Members = resolved.Members;
                result.Properties = resolved.Properties;
                result.CallSignatures = [];
                result.ConstructSignatures = [];
                result.IndexInfos = [];
                return result;
            }
        }
        else if ((type.Flags & TypeFlags.Intersection) != 0)
        {
            return GetIntersectionType(
                Core.Map(((IIntersectionType)type).Types, (IType t, int _) => GetTypeWithoutSignatures(t))!);
        }

        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:51546-51553 — <c>getExternalModuleFileFromDeclaration</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the reference's parameter is the <c>AnyImportOrReExport | ModuleDeclaration | ImportTypeNode |
    /// ImportCall</c> union, which the port's <see cref="Node"/> is the upper bound of (the <c>IEmitResolver</c> and
    /// <c>ISyntacticTypeNodeBuilderResolver</c> slots both declare <see cref="Node"/>); <c>tryCast(declaration.name,
    /// isStringLiteral)</c> is <see cref="Core.TryCast{TOut, TIn}"/>, and <c>getExternalModuleName</c> the ported
    /// <see cref="Utilities.GetExternalModuleName"/>, whose <see cref="Expression"/> face is the specifier type both
    /// the cast and the reference's own <c>Expression</c> parameter want.
    /// </para>
    /// <para>
    /// PORT-NOTE: the resolved symbol's declaration is fetched through the ported
    /// <see cref="Utilities.GetDeclarationOfKind"/> and cast to the <see cref="SourceFile"/> the reference's
    /// <c>getDeclarationOfKind(moduleSymbol, SyntaxKind.SourceFile)</c> answers.
    /// </para>
    /// </remarks>
    internal SourceFile? GetExternalModuleFileFromDeclaration(Node declaration)
    {
        Expression? specifier = declaration.Kind == SyntaxKind.ModuleDeclaration
            ? Core.TryCast<StringLiteral, Node>(((ModuleDeclaration)declaration).Name, NodeTests.IsStringLiteral)
            : Utilities.GetExternalModuleName(declaration);
        var moduleSymbol = ResolveExternalModuleNameWorker(specifier!, specifier!, /*moduleNotFoundError*/ null); // TODO: GH#18217
        if (moduleSymbol is null)
        {
            return null;
        }

        return (SourceFile?)Utilities.GetDeclarationOfKind(moduleSymbol, SyntaxKind.SourceFile);
    }

    /// <summary>ported from: src/compiler/checker.ts:53730-53738 — <c>filterPrimitivesIfContainsNonPrimitive</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's parameter is the <c>UnionType</c> class; the port's one caller passes
    /// <see cref="IUnionType"/>, whose <see cref="IType"/> base is what <see cref="MaybeTypeOfKind"/> and
    /// <see cref="FilterType"/> take, so the member's parameter is that interface.
    /// </remarks>
    internal IType FilterPrimitivesIfContainsNonPrimitive(IUnionType type)
    {
        if (MaybeTypeOfKind(type, TypeFlags.NonPrimitive))
        {
            var result = FilterType(type, t => (t.Flags & TypeFlags.Primitive) == 0);
            if ((result.Flags & TypeFlags.Never) == 0)
            {
                return result;
            }
        }

        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:54202-54205 — <c>isNotAccessor</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>isAccessor</c> is the <c>utilities.ts</c> predicate, ported as
    /// <see cref="Utilities.IsAccessor"/>; the reference's <c>Declaration</c> parameter is the port's
    /// <see cref="IDeclaration"/> face, which the ported predicate takes as a <see cref="Node"/>.
    /// </remarks>
    internal bool IsNotAccessor(IDeclaration declaration) => !Utilities.IsAccessor((Node)declaration);

    /// <summary>ported from: src/compiler/checker.ts:54207-54210 — <c>isNotOverload</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>(declaration as FunctionDeclaration).body</c> is a compile-time narrowing with no runtime
    /// cast (a <c>MethodDeclaration</c> answers its own body through it), so the port selects the body with the
    /// pattern switch over exactly the two kinds the left operand admits — a real cast to
    /// <see cref="FunctionDeclaration"/> would throw for the method declaration.
    /// </remarks>
    internal bool IsNotOverload(IDeclaration declaration)
    {
        var node = (Node)declaration;
        return node.Kind != SyntaxKind.FunctionDeclaration && node.Kind != SyntaxKind.MethodDeclaration
            || FunctionLikeBodyOf(node) is not null;
    }

    /// <summary>The <c>body</c> read of <see cref="IsNotOverload"/> (checker.ts:54208-54209).</summary>
    private static Block? FunctionLikeBodyOf(Node node) => node switch
    {
        FunctionDeclaration functionDeclaration => functionDeclaration.Body,
        MethodDeclaration methodDeclaration => methodDeclaration.Body,
        _ => null,
    };

    /// <summary>ported from: src/compiler/checker.ts:1410 — <c>isNotOverloadAndNotAccessor</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS composes the two predicates with core.ts's <c>and(isNotOverload, isNotAccessor)</c>
    /// (core.ts:2454-2456), which the port does not carry as a helper — the two calls are stated in sequence, which is
    /// that combinator's body.
    /// </remarks>
    internal bool IsNotOverloadAndNotAccessor(IDeclaration declaration)
        => IsNotOverload(declaration) && IsNotAccessor(declaration);

    /// <summary>ported from: src/compiler/checker.ts:54213-54221 — <c>isDeclarationNameOrImportPropertyName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the default arm is <c>utilities.ts:5073</c>'s <c>isDeclarationName</c>, the ported
    /// <see cref="Utilities.IsDeclarationName"/>; the two specifier arms test the name with
    /// <see cref="NodeTests.IsIdentifier"/> / the string-literal kind, as the reference does.
    /// </remarks>
    internal bool IsDeclarationNameOrImportPropertyName(Node name)
    {
        switch (name.Parent!.Kind)
        {
            case SyntaxKind.ImportSpecifier:
            case SyntaxKind.ExportSpecifier:
                return NodeTests.IsIdentifier(name) || name.Kind == SyntaxKind.StringLiteral;
            default:
                return Utilities.IsDeclarationName(name);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:54256-54258 — <c>signatureHasLiteralTypes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>static</c> as its landed sibling <c>SignatureHasRestParameter</c> is
    /// (Checker.Check.Calls.cs), which lets the reference's own <c>some(candidates, signatureHasLiteralTypes)</c>
    /// method-group call site stay a method group.
    /// </remarks>
    internal static bool SignatureHasLiteralTypes(ISignature s) => (s.Flags & SignatureFlags.HasLiteralTypes) != 0;
}

// ported from: src/compiler/utilities.ts:625-636 (getDeclarationOfKind)
// ported from: src/compiler/utilitiesPublic.ts:1351-1355 (getEffectiveConstraintOfTypeParameter)
// ported from: src/compiler/utilities.ts:2788-2882 (isPartOfTypeNode), 2884-2888 (isPartOfTypeExpressionWithTypeArguments)
// ported from: src/compiler/utilities.ts:5014-5016 (walkUpParenthesizedExpressions), 5001-5006 (the walkUp helper it wraps)
// ported from: src/compiler/utilities.ts:5025-5036 (walkUpParenthesizedTypesAndGetParentAndChild)
// ported from: src/compiler/utilities.ts:5063-5069 (isNodeDescendantOf)
// ported from: src/compiler/utilities.ts:7444-7497 (tryGetClassExtending/Implementing…, isExpressionWithTypeArgumentsInClassExtendsClause)
// ported from: src/compiler/utilities.ts:7505-7522 (getFirstIdentifier)
// ported from: src/compiler/utilities.ts:8045-8065 (getDeclarationModifierFlagsFromSymbol)
// ported from: src/compiler/utilities.ts:8275-8277 (getClassLikeDeclarationOfSymbol)
// ported from: src/compiler/utilities.ts:10920-10936 (isNumericLiteralName)
// ported from: src/compiler/utilities.ts:4780-4782 (getJSDocOverloadTags)
// ported from: src/compiler/utilitiesPublic.ts:1121-1123 (getJSDocClassTag)
//
// PORT-NOTE: this second class in the file — a `Utilities` partial beside the checker slice that needs it — is the
// layout Main settled on 2026-09-26 ("a `Utilities` partial may live beside its slice's file; the rule is one class,
// not one directory"), the same shape as Program/Utilities.Program.cs and Utilities/Utilities.Binder*.cs. Every name
// here has exactly one declaration in the solution, and Checker/Utilities.Checker.cs (the utilities slice) is the file
// that later extends this class.
/// <summary>
/// The <c>utilities.ts</c> helpers the checker's landed slices call that no slice has declared yet.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: every member of this class belongs to the <c>utilities.ts</c> / <c>utilitiesPublic.ts</c> slice upstream
/// (Layer 5, unassigned in this milestone). They are declared here — a second class in
/// <c>Checker/Checker.Support.cs</c>, next to the checker block that needs them — because the landed files already call
/// them under the <c>Utilities.</c> name (<c>Utilities.IsPartOfTypeNode</c>,
/// <c>Utilities.GetDeclarationModifierFlagsFromSymbol</c>, <c>Utilities.IsNumericLiteralName</c>, …), and
/// <see cref="Utilities"/> is already a <c>partial</c> class spanning <c>Utilities/*.cs</c>. The <c>utilities.ts</c>
/// slice replaces each with the copy it ports from the same range; nothing here should be re-declared elsewhere.
/// </para>
/// <para>
/// PORT-NOTE: <c>getEffectiveReturnTypeNode</c> (utilities.ts:6911) and <c>getCombinedModifierFlags</c>
/// (utilities.ts:1754) are already declared on this class (<c>Utilities/Utilities.cs:1136</c>,
/// <c>Utilities/Utilities.Binder.Modifiers.cs:33</c>), so the checker's call sites use those and no copy is added here.
/// </para>
/// <para>
/// PORT-NOTE: the two helpers of <see cref="IsPartOfTypeNode"/> that the port adds are file-local functions upstream
/// (utilities.ts:2884, :7444-7497). <c>isPartOfTypeExpressionWithTypeArguments</c> stays <c>private</c> — only
/// <c>IsPartOfTypeNode</c> calls it. <c>isExpressionWithTypeArgumentsInClassExtendsClause</c> is <c>internal</c>
/// because a landed slice calls it across classes as
/// <c>Utilities.IsExpressionWithTypeArgumentsInClassExtendsClause(…)</c> (Checker/Checker.Check.Source.cs, the
/// reference's checker.ts:49983). Each name is unique in the solution, so a second declaration anywhere is a CS0111.
/// </para>
/// </remarks>
internal static partial class Utilities
{
    /// <summary>ported from: src/compiler/utilities.ts:625-636 — <c>getDeclarationOfKind</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>T extends Declaration</c> is inferred from the <c>T["kind"]</c> parameter; C# has no such
    /// inference, so the generic overload takes the type argument explicitly (<c>GetDeclarationOfKind&lt;Node&gt;(…)</c>,
    /// which is what the landed call sites write) and the non-generic overload answers the <c>Declaration</c> face for
    /// the callers that do not name one.
    /// </remarks>
    public static IDeclaration? GetDeclarationOfKind(ISymbol symbol, SyntaxKind kind) => GetDeclarationOfKind<IDeclaration>(symbol, kind);

    /// <summary>ported from: src/compiler/utilities.ts:625-636 — <c>getDeclarationOfKind</c>.</summary>
    public static T? GetDeclarationOfKind<T>(ISymbol symbol, SyntaxKind kind)
        where T : class
    {
        var declarations = symbol.Declarations;
        if (declarations is not null)
        {
            foreach (var declaration in declarations)
            {
                if (declaration.Kind == kind)
                {
                    return declaration as T;
                }
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1351-1355 — <c>getEffectiveConstraintOfTypeParameter</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's second arm compares the node with <c>node.parent.typeParameters[0]</c>, which the port spells as
    /// the first element of the <c>JSDocTemplateTag</c>'s list (the generated slot is nullable, the reference's is not).
    /// The JSDoc arm is why the parameter is a declaration rather than a type parameter: a <c>@template</c> tag carries
    /// its constraint on the tag, not on the declaration. This member is read by
    /// <c>getConstraintDeclarationForMappedType</c>/<c>getConstraintDeclaration</c> (Checker.Support.cs) and belongs to
    /// the <c>utilitiesPublic.ts</c> slice upstream (see the class remarks).
    /// </remarks>
    public static ITypeNode? GetEffectiveConstraintOfTypeParameter(TypeParameterDeclaration node)
    {
        if (node.Constraint is not null)
        {
            return node.Constraint;
        }

        if (NodeTests.IsJSDocTemplateTag(node.Parent!)
            && ((JSDocTemplateTag)node.Parent!).TypeParameters is { Count: > 0 } typeParameters
            && ReferenceEquals(node, typeParameters[0]))
        {
            return ((JSDocTemplateTag)node.Parent!).Constraint;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:4780-4782 — <c>getJSDocOverloadTags</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>getAllJSDocTags(node, isJSDocOverloadTag)</c> is <c>getJSDocTags(node).filter(predicate)</c>;
    /// the port filters the ported <see cref="GetJSDocTags"/> list and maps the matches onto the overload face, which is
    /// the element type the callers read <c>typeExpression</c> off (Checker.Queries.cs:293).
    /// </remarks>
    public static List<JSDocOverloadTag> GetJSDocOverloadTags(Node node)
    {
        var tags = Core.Filter(GetJSDocTags(node), tag => NodeTests.IsJSDocOverloadTag(tag));
        return tags is null ? [] : Core.Map(tags, (JSDocTag tag, int _) => (JSDocOverloadTag)tag)!;
    }

    /// <summary>ported from: src/compiler/utilities.ts:8045-8065 — <c>getDeclarationModifierFlagsFromSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>find(s.declarations, isSetAccessorDeclaration)</c> is <see cref="Core.Find{T}"/> and the reference's
    /// three-way <c>||</c> is written out as two guarded lookups plus the <c>??=</c> over <c>valueDeclaration</c>. The
    /// <c>getCheckFlags(s)</c> test inlines that function's one-line read (<c>Transient</c> ⟹ the symbol's own links)
    /// because the port declares <c>GetCheckFlags</c> on <see cref="Checker"/> (Checker.Symbols.cs:452), which a static
    /// member of this class cannot call.
    /// </remarks>
    public static ModifierFlags GetDeclarationModifierFlagsFromSymbol(ISymbol symbol, bool isWrite = false)
    {
        if (symbol.ValueDeclaration is not null)
        {
            IDeclaration? declaration = isWrite && symbol.Declarations is not null
                ? Core.Find(symbol.Declarations, (IDeclaration d, int _) => NodeTests.IsSetAccessorDeclaration((Node)d))
                : null;
            if (declaration is null && (symbol.Flags & SymbolFlags.GetAccessor) != 0)
            {
                declaration = Core.Find(symbol.Declarations, (IDeclaration d, int _) => NodeTests.IsGetAccessorDeclaration((Node)d));
            }

            declaration ??= symbol.ValueDeclaration;
            var flags = GetCombinedModifierFlags((Node)declaration);
            return symbol.Parent is not null && (symbol.Parent.Flags & SymbolFlags.Class) != 0 ? flags : flags & ~ModifierFlags.AccessibilityModifier;
        }

        var checkFlags = (symbol.Flags & SymbolFlags.Transient) != 0 ? ((ITransientSymbol)symbol).Links!.CheckFlags : CheckFlags.None;
        if ((checkFlags & CheckFlags.Synthetic) != 0)
        {
            // NOTE: potentially unchecked cast to TransientSymbol
            var accessModifier = (checkFlags & CheckFlags.ContainsPrivate) != 0 ? ModifierFlags.Private
                : (checkFlags & CheckFlags.ContainsPublic) != 0 ? ModifierFlags.Public
                : ModifierFlags.Protected;
            var staticModifier = (checkFlags & CheckFlags.ContainsStatic) != 0 ? ModifierFlags.Static : ModifierFlags.None;
            return accessModifier | staticModifier;
        }

        if ((symbol.Flags & SymbolFlags.Prototype) != 0)
        {
            return ModifierFlags.Public | ModifierFlags.Static;
        }

        return ModifierFlags.None;
    }

    /// <summary>ported from: src/compiler/utilities.ts:8275-8277 — <c>getClassLikeDeclarationOfSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>declarations?.find(isClassLike)</c> returns the declaration face; the port answers
    /// <see cref="Node"/>, which is what the landed caller passes on to
    /// <see cref="HasSyntacticModifier"/> (Checker.Members.cs:382).
    /// </remarks>
    public static Node? GetClassLikeDeclarationOfSymbol(ISymbol symbol)
    {
        var declaration = Core.Find(symbol.Declarations, (IDeclaration d, int _) => IsClassLike((Node)d));
        return declaration is null ? null : (Node)declaration;
    }

    /// <summary>ported from: src/compiler/utilities.ts:10920-10936 — <c>isNumericLiteralName</c>.</summary>
    /// <remarks>
    /// <para>
    /// Here, we test whether <c>ToString(ToNumber(name))</c> is exactly equal to <c>name</c>. The <c>+</c> prefix
    /// operator is equivalent here to applying the abstract ToNumber operation. Applying the <c>toString()</c> method
    /// on a number gives us the abstract ToString operation on a number. Note that this accepts the values
    /// 'Infinity', '-Infinity', and 'NaN', and that this is intentional.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>+name</c> and <c>.toString()</c> are the engine's ToNumber and
    /// <c>Number.prototype.toString()</c>; the port calls <see cref="Js.ToNumber"/> and
    /// <see cref="Js.NumberToString"/>, which implement the same two operations (Core/Js.cs). The reference's parameter
    /// is <c>string | __String</c>, both of which are the port's <c>string</c>.
    /// </para>
    /// </remarks>
    public static bool IsNumericLiteralName(string name) => Js.NumberToString(Js.ToNumber(name)) == name;

    /// <summary>ported from: src/compiler/utilities.ts:7505-7522 — <c>getFirstIdentifier</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>EntityNameOrEntityNameExpression</c> union
    /// (<c>Identifier | QualifiedName | PropertyAccessExpression</c>); C# takes the <see cref="Node"/> class and walks
    /// the same two slots (<c>QualifiedName.left</c>, <c>PropertyAccessExpression.expression</c>), exactly as the
    /// reference does. The reference's exhaustive switch answers <c>Identifier</c> for those three kinds; the port's
    /// trailing <c>AssertNever</c> is the same "cannot happen" the reference's type system proves.
    /// </remarks>
    public static Identifier GetFirstIdentifier(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.Identifier:
                return (Identifier)node;
            case SyntaxKind.QualifiedName:
                do
                {
                    node = ((QualifiedName)node).Left!;
                }
                while (node.Kind != SyntaxKind.Identifier);
                return (Identifier)node;
            case SyntaxKind.PropertyAccessExpression:
                do
                {
                    node = ((PropertyAccessExpression)node).Expression!;
                }
                while (node.Kind != SyntaxKind.Identifier);
                return (Identifier)node;
        }

        Debug.AssertNever(node.Kind);
        throw new InvalidOperationException("unreachable");
    }

    /// <summary>ported from: src/compiler/utilities.ts:5063-5069 — <c>isNodeDescendantOf</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is a non-optional <c>Node</c> while its <c>while (node)</c> test tolerates the
    /// <c>undefined</c> a walked-off root supplies; the port states the walk honestly as <c>Node?</c>, which every
    /// caller's non-null argument converts to. Every call site inside a <c>Checker</c> partial qualifies it as
    /// <c>Utilities.IsNodeDescendantOf(…)</c>.
    /// </remarks>
    public static bool IsNodeDescendantOf(Node? node, Node? ancestor)
    {
        while (node is not null)
        {
            if (ReferenceEquals(node, ancestor))
            {
                return true;
            }

            node = node.Parent;
        }

        return false;
    }

    /// <summary>ported from: src/compiler/utilities.ts:5014-5016 — <c>walkUpParenthesizedExpressions</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference is <c>walkUp(node, ParenthesizedExpression)</c> (utilities.ts:5001-5006), which climbs
    /// while the <em>current</em> node is a parenthesized expression, not while its parent is one, so a value wrapped
    /// in parens is unwrapped and the node above it is returned. The port folds that one-line helper in and keeps the
    /// same order; the innermost <c>Node</c> is TS's declared return type, whose <c>undefined</c> arm only a
    /// parenthesized root could reach.
    /// </remarks>
    public static Node WalkUpParenthesizedExpressions(Node node)
    {
        Node? current = node;
        while (current is not null && current.Kind == SyntaxKind.ParenthesizedExpression)
        {
            current = current.Parent;
        }

        return current!;
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1121-1123 — <c>getJSDocClassTag</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>getFirstJSDocTag(node, isJSDocClassTag)</c> is <c>find(getJSDocTagsWorker(node), predicate)</c>;
    /// the port searches the ported <see cref="GetJSDocTags"/> list with the same kind predicate. Only
    /// <c>isJSConstructor</c> (Checker.Support.cs) reads it.
    /// </remarks>
    public static JSDocClassTag? GetJSDocClassTag(Node node)
        => Core.Find(GetJSDocTags(node), (JSDocTag tag, int _) => NodeTests.IsJSDocClassTag(tag)) as JSDocClassTag;

    /// <summary>ported from: src/compiler/utilities.ts:2788-2882 — <c>isPartOfTypeNode</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the reference's identifier arm falls through to the qualified-name arm; the port's <c>goto case</c> is
    /// that fall-through, with the reference's <c>Debug.assert</c> kept.
    /// </para>
    /// <para>
    /// PORT-NOTE: the three structural unions the arms read have no C# name, so each is selected by the kind already
    /// matched: <c>HasType</c> is the pattern switch over the four declarations, <c>FunctionLikeDeclaration</c> and
    /// <c>SignatureDeclaration</c> are both <see cref="ISignatureDeclarationBase"/>, and
    /// <c>CallExpression | TaggedTemplateExpression</c> is the type-argument switch. The reference's
    /// <c>contains(…, node)</c> is identity containment over the type-argument list (<see cref="Core.Some{T}"/> with
    /// <see cref="ReferenceEquals"/>), not a value search.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>isPartOfTypeExpressionWithTypeArguments</c> and the class-extends test it needs are file-local
    /// helpers upstream (utilities.ts:2884, :7495); they are the two <c>private</c> members at the end of this class.
    /// </para>
    /// </remarks>
    public static bool IsPartOfTypeNode(Node node)
    {
        if (SyntaxKind.FirstTypeNode <= node.Kind && node.Kind <= SyntaxKind.LastTypeNode)
        {
            return true;
        }

        switch (node.Kind)
        {
            case SyntaxKind.AnyKeyword:
            case SyntaxKind.UnknownKeyword:
            case SyntaxKind.NumberKeyword:
            case SyntaxKind.BigIntKeyword:
            case SyntaxKind.StringKeyword:
            case SyntaxKind.BooleanKeyword:
            case SyntaxKind.SymbolKeyword:
            case SyntaxKind.ObjectKeyword:
            case SyntaxKind.UndefinedKeyword:
            case SyntaxKind.NullKeyword:
            case SyntaxKind.NeverKeyword:
                return true;
            case SyntaxKind.VoidKeyword:
                return node.Parent!.Kind != SyntaxKind.VoidExpression;
            case SyntaxKind.ExpressionWithTypeArguments:
                return IsPartOfTypeExpressionWithTypeArguments(node);
            case SyntaxKind.TypeParameter:
                return node.Parent!.Kind == SyntaxKind.MappedType || node.Parent.Kind == SyntaxKind.InferType;

            // Identifiers and qualified names may be type nodes, depending on their context. Climb
            // above them to find the lowest container
            case SyntaxKind.Identifier:
                // If the identifier is the RHS of a qualified name, then it's a type iff its parent is.
                if (node.Parent!.Kind == SyntaxKind.QualifiedName && ReferenceEquals(((QualifiedName)node.Parent).Right, node))
                {
                    node = node.Parent!;
                }
                else if (node.Parent.Kind == SyntaxKind.PropertyAccessExpression && ReferenceEquals(((PropertyAccessExpression)node.Parent).Name, node))
                {
                    node = node.Parent!;
                }

                // At this point, node is either a qualified name or an identifier
                Debug.Assert(
                    node.Kind == SyntaxKind.Identifier || node.Kind == SyntaxKind.QualifiedName || node.Kind == SyntaxKind.PropertyAccessExpression,
                    "'node' was expected to be a qualified name, identifier or property access in 'isPartOfTypeNode'.");
                goto case SyntaxKind.QualifiedName;

            case SyntaxKind.QualifiedName:
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ThisKeyword:
                {
                    var parent = node.Parent!;
                    if (parent.Kind == SyntaxKind.TypeQuery)
                    {
                        return false;
                    }

                    if (parent.Kind == SyntaxKind.ImportType)
                    {
                        return !((ImportTypeNode)parent).IsTypeOf;
                    }

                    // Do not recursively call isPartOfTypeNode on the parent. In the example:
                    //
                    //     let a: A.B.C;
                    //
                    // Calling isPartOfTypeNode would consider the qualified name A.B a type node.
                    // Only C and A.B.C are type nodes.
                    if (SyntaxKind.FirstTypeNode <= parent.Kind && parent.Kind <= SyntaxKind.LastTypeNode)
                    {
                        return true;
                    }

                    switch (parent.Kind)
                    {
                        case SyntaxKind.ExpressionWithTypeArguments:
                            return IsPartOfTypeExpressionWithTypeArguments(parent);
                        case SyntaxKind.TypeParameter:
                            return ReferenceEquals(node, ((TypeParameterDeclaration)parent).Constraint);
                        case SyntaxKind.JSDocTemplateTag:
                            return ReferenceEquals(node, ((JSDocTemplateTag)parent).Constraint);
                        case SyntaxKind.PropertyDeclaration:
                        case SyntaxKind.PropertySignature:
                        case SyntaxKind.Parameter:
                        case SyntaxKind.VariableDeclaration:
                            // TS's `node === (parent as HasType).type`; the union has no C# name, so the port
                            // selects the face by the kind already matched.
                            return parent switch
                            {
                                PropertyDeclaration propertyDeclaration => ReferenceEquals(node, propertyDeclaration.Type),
                                PropertySignature propertySignature => ReferenceEquals(node, propertySignature.Type),
                                ParameterDeclaration parameterDeclaration => ReferenceEquals(node, parameterDeclaration.Type),
                                VariableDeclaration variableDeclaration => ReferenceEquals(node, variableDeclaration.Type),
                                _ => false,
                            };
                        case SyntaxKind.FunctionDeclaration:
                        case SyntaxKind.FunctionExpression:
                        case SyntaxKind.ArrowFunction:
                        case SyntaxKind.Constructor:
                        case SyntaxKind.MethodDeclaration:
                        case SyntaxKind.MethodSignature:
                        case SyntaxKind.GetAccessor:
                        case SyntaxKind.SetAccessor:
                            return ReferenceEquals(node, ((ISignatureDeclarationBase)parent).Type);
                        case SyntaxKind.CallSignature:
                        case SyntaxKind.ConstructSignature:
                        case SyntaxKind.IndexSignature:
                            return ReferenceEquals(node, ((ISignatureDeclarationBase)parent).Type);
                        case SyntaxKind.TypeAssertionExpression:
                            return ReferenceEquals(node, ((TypeAssertion)parent).Type);
                        case SyntaxKind.CallExpression:
                        case SyntaxKind.NewExpression:
                        case SyntaxKind.TaggedTemplateExpression:
                            var typeArguments = parent switch
                            {
                                CallExpression callExpression => callExpression.TypeArguments,
                                NewExpression newExpression => newExpression.TypeArguments,
                                TaggedTemplateExpression taggedTemplateExpression => taggedTemplateExpression.TypeArguments,
                                _ => null,
                            };
                            return typeArguments is not null && Core.Some(typeArguments, (ITypeNode t, int _) => ReferenceEquals(t, node));
                    }

                    break;
                }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/utilities.ts:2884-2888 — <c>isPartOfTypeExpressionWithTypeArguments</c>.</summary>
    private static bool IsPartOfTypeExpressionWithTypeArguments(Node node)
        => NodeTests.IsJSDocImplementsTag(node.Parent!)
            || NodeTests.IsJSDocAugmentsTag(node.Parent!)
            || (NodeTests.IsHeritageClause(node.Parent!) && !IsExpressionWithTypeArgumentsInClassExtendsClause(node));

    /// <summary>ported from: src/compiler/utilities.ts:7444-7497 — <c>isExpressionWithTypeArgumentsInClassExtendsClause</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS splits this over <c>tryGetClassExtendingExpressionWithTypeArguments</c> (utilities.ts:7444) and
    /// <c>tryGetClassImplementingOrExtendingExpressionWithTypeArguments</c> (utilities.ts:7455), whose intermediate
    /// record <c>{ class, isImplements }</c> has no other user in this slice; the port folds the three into one
    /// predicate with the same answer — the heritage clause is a class-extends clause exactly when it is not an
    /// <c>implements</c> clause, and the <c>@augments</c> arm of the reference's record answers
    /// <c>isImplements: false</c> for a class-like host.
    /// <para>
    /// PORT-NOTE: <c>internal</c> rather than <c>private</c> because a landed slice calls it across classes as
    /// <c>Utilities.IsExpressionWithTypeArgumentsInClassExtendsClause(name.Parent)</c>
    /// (Checker/Checker.Check.Source.cs, the reference's <c>getSymbolOfNameOrPropertyAccessExpression</c> arm at
    /// checker.ts:49983). The class is <c>internal</c>, so this is the same effective accessibility as
    /// <c>public</c>; the name stays unique in the solution.
    /// </para>
    /// </remarks>
    internal static bool IsExpressionWithTypeArgumentsInClassExtendsClause(Node node)
    {
        if (!NodeTests.IsExpressionWithTypeArguments(node))
        {
            return false;
        }

        var parent = node.Parent!;
        if (NodeTests.IsHeritageClause(parent) && IsClassLike(parent.Parent!))
        {
            return ((HeritageClause)parent).Token != SyntaxKind.ImplementsKeyword;
        }

        return NodeTests.IsJSDocAugmentsTag(parent) && GetEffectiveJSDocHost(parent) is { } host && IsClassLike(host);
    }

    /// <summary>ported from: src/compiler/utilities.ts:5025-5036 — <c>walkUpParenthesizedTypesAndGetParentAndChild</c>.</summary>
    /// <remarks>
    /// <para>
    /// Walks up parenthesized types. It returns both the outermost parenthesized type and its parent. If given node is
    /// not a parenthesized type, <c>null</c> is returned as the former.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's return type is the tuple <c>[ParenthesizedTypeNode | undefined, Node]</c>; the port answers the
    /// value tuple <c>(ParenthesizedTypeNode?, Node)</c>, which its one caller — the checker's
    /// <c>GetInferredTypeParameterConstraint</c> — destructures the same way (and applies the same
    /// <c>?? declaration.parent</c> default for the absent first element).
    /// </para>
    /// <para>
    /// PORT-NOTE: the reference's loop tests <c>node &amp;&amp; node.kind === SyntaxKind.ParenthesizedType</c> against a
    /// <c>Node | undefined</c> walk; the port states the walk as <c>Node?</c> so the same test is honest, and asserts
    /// the non-null answer the reference's return type declares.
    /// </para>
    /// </remarks>
    public static (ParenthesizedTypeNode? Child, Node Parent) WalkUpParenthesizedTypesAndGetParentAndChild(Node node)
    {
        ParenthesizedTypeNode? child = null;
        Node? current = node;
        while (current is not null && current.Kind == SyntaxKind.ParenthesizedType)
        {
            child = (ParenthesizedTypeNode)current;
            current = current.Parent;
        }

        return (child, current!);
    }
}