namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:53761-53765 (getEffectivePropertyNameForPropertyNameNode),
//              :53767-53776 (getCombinedModifierFlagsCached), :53778-53786 (getCombinedNodeFlagsCached),
//              :53788-53793 (isVarConstLike), :53795-53803 (getJSXRuntimeImportSpecifier),
//              :53805-53810 (getImportHelpersImportSpecifier)
// ported from: src/compiler/checker.ts:53856-54073 (compareTypes), :54075-54081 (getSortOrderFlags),
//              :54083-54099 (compareTypeNames), :54101-54109 (getTypeNameSymbol), :54111-54134 (compareTupleTypes),
//              :54136-54147 (compareElementLabels), :54149-54158 (compareTypeLists), :54160-54199 (compareTypeMappers)
/// <summary>
/// The <c>stableTypeOrdering</c> comparators and the two cached combined-flag lookups of the checker body.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: the band's neighbours are landed elsewhere and are only referenced from here.
/// <c>findMatchingTypeReferenceOrTypeAliasReference</c>/<c>findBestTypeForObjectLiteral</c>/
/// <c>findBestTypeForInvokable</c>/<c>findMostOverlappyType</c>/<c>findMatchingDiscriminantType</c> (53670-53760) are
/// declared once in <c>Checker/Checker.Relations.Identical.cs</c>, beside their only caller <c>getBestMatchingType</c>,
/// and <c>sortSymbolsIfTSGoCompat</c>/<c>compareSymbols</c>/<c>compareNodes</c> (53812-53854) are declared in
/// <c>Checker/Checker.Types.cs</c>; <see cref="CompareTypes"/> is their sibling and the whole family is reached from
/// this file unqualified, once.
/// </para>
/// <para>
/// PORT-NOTE: TS's comparators return a JS sort <c>number</c> — any integer is a legal result of
/// <c>getSortOrderFlags(t1) - getSortOrderFlags(t2)</c>, not only <c>-1</c>/<c>0</c>/<c>1</c>. The port keeps the exact
/// ordering but returns <c>int</c>, the shape the already-landed <c>CompareSymbols</c>/<c>CompareNodes</c>
/// (Checker/Checker.Types.cs) have; a consumer that needs the port's <c>Comparison</c>-returning
/// <c>Core.Comparer&lt;T&gt;</c> adapts with <c>Core.CompareValues(x, 0)</c>, the shape <c>getNamedMembers</c> uses.
/// Every <c>Core.CompareComparableValues</c> result is cast to <c>int</c> for the same reason.
/// </para>
/// <para>
/// PORT-NOTE: the reference's arms switch on <c>t.flags</c> and cast to the family the flags announce
/// (<c>TypeReference</c>, <c>AnonymousType</c>, <c>UnionType</c>, <c>TupleType</c>, …). The port keeps the flag chain and
/// casts to the interface that declares the slots each arm reads (<see cref="ITypeReference"/>,
/// <see cref="IAnonymousType"/>, <see cref="IUnionType"/>, <see cref="IIntersectionType"/>, <see cref="ITupleType"/>,
/// <see cref="ILiteralType"/>, <see cref="IIntrinsicType"/>, <see cref="IIndexType"/>,
/// <see cref="IIndexedAccessType"/>, <see cref="IConditionalType"/>, <see cref="ISubstitutionType"/>,
/// <see cref="ITemplateLiteralType"/>, <see cref="IStringMappingType"/>), which is the same unchecked cast the
/// reference performed. Enum-valued slots (<c>ElementFlags</c>, <c>IndexFlags</c>, <c>ObjectFlags</c>,
/// <c>TypeMapKind</c>) are compared after an <c>int</c> cast, which is what the reference's JS subtraction does.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // Effective property names (checker.ts:53761-53765)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:53761-53765 — <c>getEffectivePropertyNameForPropertyNameNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's parameter is the union <c>PropertyName</c>; the port's generated node model has no
    /// such type, and <c>getPropertyNameForPropertyNameNode</c> already takes the <see cref="Node"/> class
    /// (<c>Utilities.GetPropertyNameForPropertyNameNode</c>), so the union is spelled <see cref="Node"/> here too and the
    /// computed-name arm casts to <see cref="ComputedPropertyName"/> before reading <c>expression</c>.
    /// </remarks>
    internal string? GetEffectivePropertyNameForPropertyNameNode(Node node)
    {
        var name = Utilities.GetPropertyNameForPropertyNameNode(node);
        return Js.IsTruthy(name)
            ? name
            : NodeTests.IsComputedPropertyName(node) ? TryGetNameFromType(GetTypeOfExpression(((ComputedPropertyName)node).Expression!)) : null;
    }

    // ---------------------------------------------------------------------------------------------
    // Cached combined flags (checker.ts:53767-53786)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:53767-53776 — <c>getCombinedModifierFlagsCached</c>.</summary>
    internal ModifierFlags GetCombinedModifierFlagsCached(IDeclaration node)
    {
        // we hold onto the last node and result to speed up repeated lookups against the same node.
        if (ReferenceEquals(_lastGetCombinedModifierFlagsNode, node))
        {
            return _lastGetCombinedModifierFlagsResult;
        }

        _lastGetCombinedModifierFlagsNode = node;
        _lastGetCombinedModifierFlagsResult = Utilities.GetCombinedModifierFlags((Node)node);
        return _lastGetCombinedModifierFlagsResult;
    }

    /// <summary>ported from: src/compiler/checker.ts:53778-53786 — <c>getCombinedNodeFlagsCached</c>.</summary>
    internal NodeFlags GetCombinedNodeFlagsCached(Node node)
    {
        // we hold onto the last node and result to speed up repeated lookups against the same node.
        if (ReferenceEquals(_lastGetCombinedNodeFlagsNode, node))
        {
            return _lastGetCombinedNodeFlagsResult;
        }

        _lastGetCombinedNodeFlagsNode = node;
        _lastGetCombinedNodeFlagsResult = Utilities.GetCombinedNodeFlags(node);
        return _lastGetCombinedNodeFlagsResult;
    }

    /// <summary>ported from: src/compiler/checker.ts:53788-53793 — <c>isVarConstLike</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's parameter is the union <c>VariableDeclaration | VariableDeclarationList</c>; the port
    /// spells it <see cref="Node"/>, the common base of the two generated classes, because the body reads nothing but
    /// the cached combined node flags.
    /// </remarks>
    internal bool IsVarConstLike(Node node)
    {
        var blockScopeKind = GetCombinedNodeFlagsCached(node) & NodeFlags.BlockScoped;
        return blockScopeKind == NodeFlags.Const ||
            blockScopeKind == NodeFlags.Using ||
            blockScopeKind == NodeFlags.AwaitUsing;
    }

    // ---------------------------------------------------------------------------------------------
    // Synthesized import specifiers (checker.ts:53795-53810)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:53795-53803 — <c>getJSXRuntimeImportSpecifier</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>StringLiteralLike | undefined</c> is <see cref="LiteralExpression"/>, the element
    /// type of the generated <c>SourceFile.Imports</c> slot, which is nullable although TS declares <c>imports</c>
    /// required; the bounds/null checks produce the reference's "no import at that index" answer (<c>undefined</c>,
    /// the port's <c>null</c>) instead of the throw a bare index would give.
    /// </remarks>
    internal LiteralExpression? GetJSXRuntimeImportSpecifier(SourceFile? file, string specifierText)
    {
        // Synthesized JSX import is either first or after tslib
        var jsxImportIndex = Js.IsTruthy(_compilerOptions.ImportHelpers) ? 1 : 0;
        var specifier = file is not null && file.Imports is not null && jsxImportIndex < file.Imports.Count ? file.Imports[jsxImportIndex] : null;
        if (specifier is not null)
        {
            Debug.Assert(Utilities.NodeIsSynthesized(specifier) && specifier.Text == specifierText, $"Expected sourceFile.imports[{jsxImportIndex}] to be the synthesized JSX runtime import");
        }

        return specifier;
    }

    /// <summary>ported from: src/compiler/checker.ts:53805-53810 — <c>getImportHelpersImportSpecifier</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's second assert leads with <c>specifier &amp;&amp;</c>, a truthiness test on a value its
    /// own declaration types as present; the port's <c>SourceFile.Imports</c> is a nullable generated slot, so the
    /// element access carries the <c>!</c> that states the same invariant and the truthiness test is dropped.
    /// </remarks>
    internal LiteralExpression GetImportHelpersImportSpecifier(SourceFile file)
    {
        Debug.Assert(Js.IsTruthy(_compilerOptions.ImportHelpers), "Expected importHelpers to be enabled");
        var specifier = file.Imports![0];
        Debug.Assert(Utilities.NodeIsSynthesized(specifier) && specifier.Text == "tslib", "Expected sourceFile.imports[0] to be the synthesized tslib import");
        return specifier;
    }

    // ---------------------------------------------------------------------------------------------
    // The stable type ordering comparators (checker.ts:53856-54199)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:53856-54073 — <c>compareTypes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the two nested helpers the reference hoists into the function body
    /// (<c>slicesCompareString</c>, and the numeric face of <c>compareComparableValues</c> that the port's
    /// <c>Core.CompareComparableValues</c> only lands for <c>string?</c> and <c>int?</c>) are local functions below the
    /// fall-through <c>return t1.id - t2.id</c>, where the reference declares them. A numeric literal's value is a JS
    /// double — <c>(double)((ILiteralType)t).Value!</c> is how the landed call sites read it — so the numeric arm
    /// compares doubles rather than the lossy <c>int?</c> face.
    /// </remarks>
    internal int CompareTypes(IType? t1, IType? t2)
    {
        if (ReferenceEquals(t1, t2)) return 0;
        if (t1 is null) return -1;
        if (t2 is null) return 1;

        // First sort in order of increasing type flags values.
        var c = (int)GetSortOrderFlags(t1) - (int)GetSortOrderFlags(t2);
        if (c != 0) return c;

        // Order named types by name and, in the case of aliased types, by alias type arguments.
        c = CompareTypeNames(t1, t2);
        if (c != 0) return c;

        // We have unnamed types or types with identical names. Now sort by data specific to the type.
        if ((t1.Flags & (TypeFlags.Any | TypeFlags.Unknown | TypeFlags.String | TypeFlags.Number | TypeFlags.Boolean | TypeFlags.BigInt | TypeFlags.ESSymbol | TypeFlags.Void | TypeFlags.Undefined | TypeFlags.Null | TypeFlags.Never | TypeFlags.NonPrimitive)) != 0)
        {
            // Only distinguished by type IDs, handled below.
        }
        else if ((t1.Flags & TypeFlags.Object) != 0)
        {
            // Order unnamed or identically named object types by symbol.
            var symbolComparison = CompareSymbols(t1.Symbol, t2.Symbol);
            if (symbolComparison != 0) return symbolComparison;

            // When object types have the same or no symbol, order by kind. We order type references before other kinds.
            if ((GetObjectFlags(t1) & ObjectFlags.Reference) != 0 && (GetObjectFlags(t2) & ObjectFlags.Reference) != 0)
            {
                var r1 = (ITypeReference)t1;
                var r2 = (ITypeReference)t2;
                if ((GetObjectFlags(r1.Target!) & ObjectFlags.Tuple) != 0 && (GetObjectFlags(r2.Target!) & ObjectFlags.Tuple) != 0)
                {
                    // Tuple types have no associated symbol, instead we order by tuple element information.
                    var tupleComparison = CompareTupleTypes((ITupleType)r1.Target!, (ITupleType)r2.Target!);
                    if (tupleComparison != 0)
                    {
                        return tupleComparison;
                    }
                }

                // Here we know we have references to instantiations of the same type because we have matching targets.
                if (r1.Node is null && r2.Node is null)
                {
                    // Non-deferred type references with the same target are sorted by their type argument lists.
                    var argumentComparison = CompareTypeLists(r1.ResolvedTypeArguments, r2.ResolvedTypeArguments);
                    if (argumentComparison != 0)
                    {
                        return argumentComparison;
                    }
                }
                else
                {
                    // Deferred type references with the same target are ordered by the source location of the reference.
                    var positionComparison = CompareNodes(r1.Node, r2.Node);
                    if (positionComparison != 0)
                    {
                        return positionComparison;
                    }

                    // Instantiations of the same deferred type reference are ordered by their associated type mappers
                    // (which reflect the mapping of in-scope type parameters to type arguments).
                    var deferredMapperComparison = CompareTypeMappers(r1.Mapper as TypeMapper, r2.Mapper as TypeMapper);
                    if (deferredMapperComparison != 0)
                    {
                        return deferredMapperComparison;
                    }
                }
            }
            else if ((GetObjectFlags(t1) & ObjectFlags.Reference) != 0)
            {
                return -1;
            }
            else if ((GetObjectFlags(t2) & ObjectFlags.Reference) != 0)
            {
                return 1;
            }
            else
            {
                // Order unnamed non-reference object types by kind associated type mappers. Reverse mapped types have
                // neither symbols nor mappers so they're ultimately ordered by unstable type IDs, but given their rarity
                // this should be fine.
                var kindComparison = (int)(GetObjectFlags(t1) & ObjectFlags.ObjectTypeKindMask) - (int)(GetObjectFlags(t2) & ObjectFlags.ObjectTypeKindMask);
                if (kindComparison != 0)
                {
                    return kindComparison;
                }

                var mapperComparison = CompareTypeMappers(((IAnonymousType)t1).Mapper as TypeMapper, ((IAnonymousType)t2).Mapper as TypeMapper);
                if (mapperComparison != 0)
                {
                    return mapperComparison;
                }
            }
        }
        else if ((t1.Flags & TypeFlags.Union) != 0)
        {
            // Unions are ordered by origin and then constituent type lists.
            var o1 = ((IUnionType)t1).Origin;
            var o2 = ((IUnionType)t2).Origin;
            if (o1 is null && o2 is null)
            {
                var constituentComparison = CompareTypeLists(((IUnionType)t1).Types, ((IUnionType)t2).Types);
                if (constituentComparison != 0)
                {
                    return constituentComparison;
                }
            }
            else if (o1 is null)
            {
                return 1;
            }
            else if (o2 is null)
            {
                return -1;
            }
            else
            {
                var originComparison = CompareTypes(o1, o2);
                if (originComparison != 0)
                {
                    return originComparison;
                }
            }
        }
        else if ((t1.Flags & TypeFlags.Intersection) != 0)
        {
            // Intersections are ordered by their constituent type lists.
            var intersectionComparison = CompareTypeLists(((IIntersectionType)t1).Types, ((IIntersectionType)t2).Types);
            if (intersectionComparison != 0)
            {
                return intersectionComparison;
            }
        }
        else if ((t1.Flags & (TypeFlags.Enum | TypeFlags.EnumLiteral | TypeFlags.UniqueESSymbol)) != 0)
        {
            // Enum members are ordered by their symbol (and thus their declaration order).
            var enumSymbolComparison = CompareSymbols(t1.Symbol, t2.Symbol);
            if (enumSymbolComparison != 0)
            {
                return enumSymbolComparison;
            }
        }
        else if ((t1.Flags & TypeFlags.StringLiteral) != 0)
        {
            // String literal types are ordered by their values.
            var stringComparison = (int)Core.CompareComparableValues((string)((ILiteralType)t1).Value!, (string)((ILiteralType)t2).Value!);
            if (stringComparison != 0)
            {
                return stringComparison;
            }
        }
        else if ((t1.Flags & TypeFlags.NumberLiteral) != 0)
        {
            // Numeric literal types are ordered by their values.
            var numberComparison = CompareNumericValues((double)((ILiteralType)t1).Value!, (double)((ILiteralType)t2).Value!);
            if (numberComparison != 0)
            {
                return numberComparison;
            }
        }
        else if ((t1.Flags & TypeFlags.BooleanLiteral) != 0)
        {
            var b1 = ((IIntrinsicType)t1).IntrinsicName == "true";
            var b2 = ((IIntrinsicType)t2).IntrinsicName == "true";
            if (b1 != b2)
            {
                if (b1)
                {
                    return 1;
                }

                return -1;
            }
        }
        else if ((t1.Flags & TypeFlags.TypeParameter) != 0)
        {
            var typeParameterComparison = CompareSymbols(t1.Symbol, t2.Symbol);
            if (typeParameterComparison != 0)
            {
                return typeParameterComparison;
            }
        }
        else if ((t1.Flags & TypeFlags.Index) != 0)
        {
            var indexComparison = CompareTypes(((IIndexType)t1).Type, ((IIndexType)t2).Type);
            if (indexComparison != 0)
            {
                return indexComparison;
            }

            c = (int)((IIndexType)t1).IndexFlags - (int)((IIndexType)t2).IndexFlags;
            if (c != 0)
            {
                return c;
            }
        }
        else if ((t1.Flags & TypeFlags.IndexedAccess) != 0)
        {
            var objectComparison = CompareTypes(((IIndexedAccessType)t1).ObjectType, ((IIndexedAccessType)t2).ObjectType);
            if (objectComparison != 0)
            {
                return objectComparison;
            }

            var innerIndexComparison = CompareTypes(((IIndexedAccessType)t1).IndexType, ((IIndexedAccessType)t2).IndexType);
            if (innerIndexComparison != 0)
            {
                return innerIndexComparison;
            }
        }
        else if ((t1.Flags & TypeFlags.Conditional) != 0)
        {
            var rootComparison = CompareNodes((Node?)((IConditionalType)t1).Root!.Node, (Node?)((IConditionalType)t2).Root!.Node);
            if (rootComparison != 0)
            {
                return rootComparison;
            }

            var conditionalMapperComparison = CompareTypeMappers(((IConditionalType)t1).Mapper as TypeMapper, ((IConditionalType)t2).Mapper as TypeMapper);
            if (conditionalMapperComparison != 0)
            {
                return conditionalMapperComparison;
            }
        }
        else if ((t1.Flags & TypeFlags.Substitution) != 0)
        {
            var baseTypeComparison = CompareTypes(((ISubstitutionType)t1).BaseType, ((ISubstitutionType)t2).BaseType);
            if (baseTypeComparison != 0)
            {
                return baseTypeComparison;
            }

            var constraintComparison = CompareTypes(((ISubstitutionType)t1).Constraint, ((ISubstitutionType)t2).Constraint);
            if (constraintComparison != 0)
            {
                return constraintComparison;
            }
        }
        else if ((t1.Flags & TypeFlags.TemplateLiteral) != 0)
        {
            var textsComparison = SlicesCompareString(((ITemplateLiteralType)t1).Texts!, ((ITemplateLiteralType)t2).Texts!);
            if (textsComparison != 0)
            {
                return textsComparison;
            }

            var templateTypesComparison = CompareTypeLists(((ITemplateLiteralType)t1).Types, ((ITemplateLiteralType)t2).Types);
            if (templateTypesComparison != 0)
            {
                return templateTypesComparison;
            }
        }
        else if ((t1.Flags & TypeFlags.StringMapping) != 0)
        {
            var stringMappingComparison = CompareTypes(((IStringMappingType)t1).Type, ((IStringMappingType)t2).Type);
            if (stringMappingComparison != 0)
            {
                return stringMappingComparison;
            }
        }

        // Fall back to type IDs. This results in type creation order for built-in types.
        return t1.Id - t2.Id;

        // PORT-NOTE: the reference's numeric `compareComparableValues` face (a JS double comparison) has no counterpart
        // in the port's core, so it is written here with the reference's own three-way shape: equal, then less-than,
        // then greater-than (which is also what a NaN pair answers).
        static int CompareNumericValues(double a, double b)
            => a == b ? 0 : a < b ? -1 : 1;

        static int SlicesCompareString(IList<string> s1, IList<string> s2)
        {
            for (var i = 0; i < s1.Count; i++)
            {
                if (i > s2.Count)
                {
                    return 1;
                }

                var v1 = s1[i];
                var v2 = i < s2.Count ? s2[i] : null;
                var comparison = (int)Core.CompareComparableValues(v1, v2);
                if (comparison != 0) return comparison;
            }

            if (s1.Count < s2.Count)
            {
                return -1;
            }

            return 0;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:54075-54081 — <c>getSortOrderFlags</c>.</summary>
    internal TypeFlags GetSortOrderFlags(IType t)
    {
        // Return TypeFlagsEnum for all enum-like unit types (they'll be sorted by their symbols)
        if ((t.Flags & (TypeFlags.EnumLiteral | TypeFlags.Enum)) != 0 && (t.Flags & TypeFlags.Union) == 0)
        {
            return TypeFlags.Enum;
        }

        return t.Flags;
    }

    /// <summary>ported from: src/compiler/checker.ts:54083-54099 — <c>compareTypeNames</c>.</summary>
    internal int CompareTypeNames(IType t1, IType t2)
    {
        var s1 = GetTypeNameSymbol(t1);
        var s2 = GetTypeNameSymbol(t2);
        if (ReferenceEquals(s1, s2))
        {
            if (t1.AliasTypeArguments is not null)
            {
                return CompareTypeLists(t1.AliasTypeArguments, t2.AliasTypeArguments);
            }

            return 0;
        }

        if (s1 is null)
        {
            return 1;
        }

        if (s2 is null)
        {
            return -1;
        }

        return (int)Core.CompareComparableValues(s1.EscapedName, s2.EscapedName);
    }

    /// <summary>ported from: src/compiler/checker.ts:54101-54109 — <c>getTypeNameSymbol</c>.</summary>
    internal ISymbol? GetTypeNameSymbol(IType t)
    {
        if (t.AliasSymbol is not null)
        {
            return t.AliasSymbol;
        }

        if ((t.Flags & (TypeFlags.TypeParameter | TypeFlags.StringMapping)) != 0 || (GetObjectFlags(t) & (ObjectFlags.ClassOrInterface | ObjectFlags.Reference)) != 0)
        {
            return t.Symbol;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:54111-54134 — <c>compareTupleTypes</c>.</summary>
    internal int CompareTupleTypes(ITupleType t1, ITupleType t2)
    {
        if (ReferenceEquals(t1, t2))
        {
            return 0;
        }

        if (t1.Readonly != t2.Readonly)
        {
            return t1.Readonly ? 1 : -1;
        }

        if (t1.ElementFlags!.Count != t2.ElementFlags!.Count)
        {
            return t1.ElementFlags.Count - t2.ElementFlags.Count;
        }

        for (var i = 0; i < t1.ElementFlags.Count; i++)
        {
            var elementFlagComparison = (int)t1.ElementFlags[i] - (int)t2.ElementFlags[i];
            if (elementFlagComparison != 0)
            {
                return elementFlagComparison;
            }
        }

        for (var i = 0; i < (t1.LabeledElementDeclarations?.Count ?? 0); i++)
        {
            var labelComparison = CompareElementLabels(t1.LabeledElementDeclarations![i], t2.LabeledElementDeclarations![i]);
            if (labelComparison != 0)
            {
                return labelComparison;
            }
        }

        return 0;
    }

    /// <summary>ported from: src/compiler/checker.ts:54136-54147 — <c>compareElementLabels</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's parameter is the union <c>NamedTupleMember | ParameterDeclaration</c> and its body
    /// reads <c>(n.name as Identifier).escapedText</c>; the two port classes declare <c>Name</c> with different types
    /// (<c>Identifier?</c> vs <c>Node?</c>), so the name is reached through a local switch over the two kinds, which is
    /// what the reference's <c>as Identifier</c> stated, and the label slot is a <see cref="Node"/> (the generated
    /// <c>TupleType.LabeledElementDeclarations</c> is <c>List&lt;Node&gt;</c>).
    /// </remarks>
    internal int CompareElementLabels(Node? n1, Node? n2)
    {
        if (ReferenceEquals(n1, n2))
        {
            return 0;
        }

        if (n1 is null)
        {
            return -1;
        }

        if (n2 is null)
        {
            return 1;
        }

        return (int)Core.CompareComparableValues(GetElementLabelName(n1), GetElementLabelName(n2));

        static string? GetElementLabelName(Node node) => node switch
        {
            NamedTupleMember namedTupleMember => namedTupleMember.Name?.EscapedText,
            ParameterDeclaration parameterDeclaration => (parameterDeclaration.Name as Identifier)?.EscapedText,
            _ => null,
        };
    }

    /// <summary>ported from: src/compiler/checker.ts:54149-54158 — <c>compareTypeLists</c>.</summary>
    internal int CompareTypeLists(IList<IType>? s1, IList<IType>? s2)
    {
        if (Core.Length(s1) != Core.Length(s2))
        {
            return Core.Length(s1) - Core.Length(s2);
        }

        for (var i = 0; i < Core.Length(s1); i++)
        {
            var comparison = CompareTypes(s1![i], s2 is not null && i < s2.Count ? s2[i] : null);
            if (comparison != 0) return comparison;
        }

        return 0;
    }

    /// <summary>ported from: src/compiler/checker.ts:54160-54199 — <c>compareTypeMappers</c>.</summary>
    internal int CompareTypeMappers(TypeMapper? m1, TypeMapper? m2)
    {
        if (ReferenceEquals(m1, m2))
        {
            return 0;
        }

        if (m1 is null)
        {
            return 1;
        }

        if (m2 is null)
        {
            return -1;
        }

        var kind1 = m1.Kind;
        var kind2 = m2.Kind;
        if (kind1 != kind2)
        {
            return (int)kind1 - (int)kind2;
        }

        switch (kind1)
        {
            case TypeMapKind.Simple:
                {
                    var comparison = CompareTypes(m1.Source, m2.Source);
                    if (comparison != 0)
                    {
                        return comparison;
                    }

                    return CompareTypes(m1.Target, m2.Target);
                }
            case TypeMapKind.Array:
                {
                    var comparison = CompareTypeLists(m1.Sources, m2.Sources);
                    if (comparison != 0)
                    {
                        return comparison;
                    }

                    return CompareTypeLists(m1.Targets, m2.Targets);
                }
            case TypeMapKind.Merged:
                {
                    var comparison = CompareTypeMappers(m1.Mapper1, m2.Mapper1);
                    if (comparison != 0)
                    {
                        return comparison;
                    }

                    return CompareTypeMappers(m1.Mapper2, m2.Mapper2);
                }
        }

        return 0;
    }
}
