namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:13438-13460 (isThislessInterface)
// ported from: src/compiler/checker.ts:13641-13717 (isThislessType … isThisless)
// ported from: src/compiler/checker.ts:13778-14053 (isLateBindableName … getLateBoundSymbol)
// ported from: src/compiler/checker.ts:14595-15020 (resolveAnonymousTypeMembers … getMappedTypeNameTypeKind)
// ported from: src/compiler/checker.ts:15567-15795 (createUnionOrIntersectionProperty … getCommonDeclarationsOfSymbols)
// ported from: src/compiler/checker.ts:15967-16089 (isArrayOrTupleSymbol, isReadonlyArraySymbol, symbolsToArray, hasEffectiveQuestionToken)
// ported from: src/compiler/checker.ts:16177-16364 (getSignatureFromDeclaration … containsArgumentsReference)
// ported from: src/compiler/checker.ts:16406-16590 (resolveExternalModuleTypeByLiteral, createTypePredicateFromTypePredicateNode, createSignatureInstantiation)
// ported from: src/compiler/checker.ts:16675-16759 (getIndexSymbol … isValidIndexKeyType)
/// <summary>
/// The type-side support the checker's other slices call: the "this"-less analysis the declared-type slice reads
/// (<c>isThislessInterface</c>/<c>isThisless</c>), the late-binding machinery that turns a computed member name into a
/// real member symbol (<c>getResolvedMembersOrExportsOfSymbol</c> and everything it drives), the three member
/// resolvers the structured-type dispatch dispatches to (<c>resolveAnonymousTypeMembers</c>,
/// <c>resolveReverseMappedTypeMembers</c>, <c>resolveMappedTypeMembers</c>), the union/intersection property synthesis,
/// and the signature/index-symbol queries built on top of them.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS's two families of module-scope helpers that only these members reach are declared here too, because
/// no other slice owns their definition site and leaving them out would leave the members above calling nothing: the
/// "this"-less walk (<c>isThislessType</c>, <c>isThislessTypeParameter</c>,
/// <c>isThislessVariableLikeDeclaration</c>, <c>isThislessFunctionLikeDeclaration</c>,
/// checker.ts:13641-13690) and the late-binding walk (<c>isLateBindableName</c>/<c>isLateBindableIndexSignature</c>/
/// <c>isLateBindableAST</c>/<c>isTypeUsableAsIndexSignature</c>/<c>isLateBoundName</c>/<c>hasLateBindableName</c>/
/// <c>hasLateBindableIndexSignature</c>/<c>addDeclarationToLateBoundSymbol</c>/<c>lateBindMember</c>/
/// <c>lateBindIndexSignature</c>, checker.ts:13778-13947). Each is named with the reference's own line range on its
/// <c>summary</c>; every other name these members call is declared by a sibling slice and is listed in the slice
/// report.
/// </para>
/// <para>
/// PORT-NOTE: <c>getSignaturesOfSymbol</c> (checker.ts:16366-16408) sits in this file's band but is already declared
/// by <c>Checker/Checker.Queries.cs</c> (M6TypesQuery); it is not declared a second time here.
/// </para>
/// <para>
/// PORT-NOTE: TS's <c>SymbolTable</c> is one <c>Map</c> of symbols; the port's <see cref="SymbolTable"/> is the same
/// identity-keyed table, so a lookup that may miss is written <c>GetValueOrDefault</c>/<c>TryGetValue</c> and a write
/// is the indexer (<c>Map.get</c>/<c>Map.set</c>), as the landed slices do.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // The "this"-less analysis (checker.ts:13438-13717)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:13438-13460 — <c>isThislessInterface</c>.</summary>
    /// <remarks>
    /// Returns true if the interface given by the symbol is free of "this" references. Specifically, the result is true
    /// if the interface itself contains no references to "this" in its body, if all base types are interfaces, and if
    /// none of the base interfaces have a "this" type.
    /// </remarks>
    internal bool IsThislessInterface(ISymbol symbol)
    {
        if (symbol.Declarations is null)
        {
            return true;
        }

        foreach (var declaration in symbol.Declarations)
        {
            if (declaration.Kind == SyntaxKind.InterfaceDeclaration)
            {
                if ((declaration.Flags & NodeFlags.ContainsThis) != 0)
                {
                    return false;
                }

                var baseTypeNodes = Utilities.GetInterfaceBaseTypeNodes((InterfaceDeclaration)declaration);
                if (baseTypeNodes is not null)
                {
                    foreach (var node in baseTypeNodes)
                    {
                        if (Utilities.IsEntityNameExpression(node.Expression!))
                        {
                            var baseSymbol = ResolveEntityName(node.Expression!, SymbolFlags.Type, /*ignoreErrors*/ true);
                            if (baseSymbol is null
                                || (baseSymbol.Flags & SymbolFlags.Interface) == 0
                                || GetDeclaredTypeOfClassOrInterface(baseSymbol).ThisType is not null)
                            {
                                return false;
                            }
                        }
                    }
                }
            }
        }

        return true;
    }

    /// <summary>ported from: src/compiler/checker.ts:13641-13663 — <c>isThislessType</c>.</summary>
    /// <remarks>
    /// A type is free of this references if it's the any, string, number, boolean, symbol, or void keyword, a string
    /// literal type, an array with an element type that is free of this references, or a type reference that is free of
    /// this references.
    /// </remarks>
    internal bool IsThislessType(ITypeNode node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.AnyKeyword:
            case SyntaxKind.UnknownKeyword:
            case SyntaxKind.StringKeyword:
            case SyntaxKind.NumberKeyword:
            case SyntaxKind.BigIntKeyword:
            case SyntaxKind.BooleanKeyword:
            case SyntaxKind.SymbolKeyword:
            case SyntaxKind.ObjectKeyword:
            case SyntaxKind.VoidKeyword:
            case SyntaxKind.UndefinedKeyword:
            case SyntaxKind.NeverKeyword:
            case SyntaxKind.LiteralType:
                return true;
            case SyntaxKind.ArrayType:
                return IsThislessType(((ArrayTypeNode)node).ElementType!);
            case SyntaxKind.TypeReference:
                var typeArguments = ((TypeReferenceNode)node).TypeArguments;
                return typeArguments is null || Core.Every(typeArguments, (t, _) => IsThislessType(t));
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:13665-13668 — <c>isThislessTypeParameter</c>.</summary>
    /// <remarks>A type parameter is thisless if its constraint is thisless, or if it has no constraint.</remarks>
    internal bool IsThislessTypeParameter(TypeParameterDeclaration node)
    {
        var constraint = Utilities.GetEffectiveConstraintOfTypeParameter(node);
        return constraint is null || IsThislessType(constraint);
    }

    /// <summary>ported from: src/compiler/checker.ts:13674-13677 — <c>isThislessVariableLikeDeclaration</c>.</summary>
    /// <remarks>
    /// A variable-like declaration is free of this references if it has a type annotation that is thisless, or if it
    /// has no type annotation and no initializer (and is thus of type any).
    /// </remarks>
    internal bool IsThislessVariableLikeDeclaration(Node node)
    {
        var typeNode = Utilities.GetEffectiveTypeAnnotationNode(node);
        return typeNode is not null ? IsThislessType(typeNode) : !Utilities.HasInitializer(node);
    }

    /// <summary>ported from: src/compiler/checker.ts:13684-13690 — <c>isThislessFunctionLikeDeclaration</c>.</summary>
    /// <remarks>
    /// A function-like declaration is considered free of `this` references if it has a return type annotation that is
    /// free of this references and if each parameter is thisless and if each type parameter (if present) is thisless.
    /// </remarks>
    internal bool IsThislessFunctionLikeDeclaration(Node node)
    {
        var returnType = Utilities.GetEffectiveReturnTypeNode(node);
        var typeParameters = Utilities.GetEffectiveTypeParameterDeclarations(node);
        return (node.Kind == SyntaxKind.Constructor || (returnType is not null && IsThislessType(returnType)))
            && Core.Every(((ISignatureDeclarationBase)node).Parameters, (p, _) => IsThislessVariableLikeDeclaration(p))
            && Core.Every(typeParameters, (t, _) => IsThislessTypeParameter(t));
    }

    /// <summary>ported from: src/compiler/checker.ts:13699-13717 — <c>isThisless</c>.</summary>
    /// <remarks>
    /// Returns true if the class or interface member given by the symbol is free of "this" references. The function may
    /// return false for symbols that are actually free of "this" references because it is not feasible to perform a
    /// complete analysis in all cases. In particular, property members with types inferred from their initializers and
    /// function members with inferred return types are conservatively assumed not to be free of "this" references.
    /// </remarks>
    internal bool IsThisless(ISymbol symbol)
    {
        if (symbol.Declarations is not null && Core.Length(symbol.Declarations) == 1)
        {
            var declaration = symbol.Declarations[0];
            switch (declaration.Kind)
            {
                case SyntaxKind.PropertyDeclaration:
                case SyntaxKind.PropertySignature:
                    return IsThislessVariableLikeDeclaration((Node)declaration);
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.MethodSignature:
                case SyntaxKind.Constructor:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                    return IsThislessFunctionLikeDeclaration((Node)declaration);
            }
        }

        return false;
    }

    // ---------------------------------------------------------------------------------------------
    // Late binding (checker.ts:13778-14053)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:13778-13781 — <c>isLateBindableName</c>.</summary>
    /// <remarks>
    /// Indicates whether a declaration name is definitely late-bindable. A declaration name is only late-bindable if:
    /// it is a <c>ComputedPropertyName</c>; its expression is an <c>Identifier</c> or either a
    /// <c>PropertyAccessExpression</c> or an <c>ElementAccessExpression</c> consisting only of these same three types
    /// of nodes; and the type of its expression is a string or numeric literal type, or is a <c>unique symbol</c> type.
    /// </remarks>
    internal bool IsLateBindableName(Node node)
        => IsLateBindableAST(node)
            && Utilities.IsTypeUsableAsPropertyName(NodeTests.IsComputedPropertyName(node) ? CheckComputedPropertyName(node) : CheckExpressionCached(((ElementAccessExpression)node).ArgumentExpression!));

    /// <summary>ported from: src/compiler/checker.ts:13783-13786 — <c>isLateBindableIndexSignature</c>.</summary>
    internal bool IsLateBindableIndexSignature(Node node)
        => IsLateBindableAST(node)
            && IsTypeUsableAsIndexSignature(NodeTests.IsComputedPropertyName(node) ? CheckComputedPropertyName(node) : CheckExpressionCached(((ElementAccessExpression)node).ArgumentExpression!));

    /// <summary>ported from: src/compiler/checker.ts:13788-13794 — <c>isLateBindableAST</c>.</summary>
    internal bool IsLateBindableAST(Node node)
    {
        if (!NodeTests.IsComputedPropertyName(node) && !NodeTests.IsElementAccessExpression(node))
        {
            return false;
        }

        var expr = NodeTests.IsComputedPropertyName(node)
            ? ((ComputedPropertyName)node).Expression!
            : ((ElementAccessExpression)node).ArgumentExpression!;
        return Utilities.IsEntityNameExpression(expr);
    }

    /// <summary>ported from: src/compiler/checker.ts:13796-13798 — <c>isTypeUsableAsIndexSignature</c>.</summary>
    internal bool IsTypeUsableAsIndexSignature(IType type) => IsTypeAssignableTo(type, _stringNumberSymbolType);

    /// <summary>ported from: src/compiler/checker.ts:13800-13807 — <c>isLateBoundName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads <c>charCodeAt(2)</c>, which answers <c>NaN</c> past the end of the string and therefore
    /// compares unequal to <c>CharacterCodes.at</c> — i.e. the name <c>"__"</c> is not late-bound.
    /// <c>ScannerUtilities.CharCodeAt</c> answers <c>CharacterCodes.EOF</c> there, which is unequal to it too, so the
    /// two agree (the same note <c>isReservedMemberName</c> carries).
    /// </remarks>
    internal bool IsLateBoundName(string name)
        => ScannerUtilities.CharCodeAt(name, 0) == '_'
            && ScannerUtilities.CharCodeAt(name, 1) == '_'
            && ScannerUtilities.CharCodeAt(name, 2) == '@';

    /// <summary>ported from: src/compiler/checker.ts:13809-13812 — <c>hasLateBindableName</c>.</summary>
    /// <remarks>Indicates whether a declaration has a late-bindable dynamic name.</remarks>
    internal bool HasLateBindableName(IDeclaration node)
    {
        var name = Utilities.GetNameOfDeclaration((Node)node);
        return name is Node nameNode && IsLateBindableName(nameNode);
    }

    /// <summary>ported from: src/compiler/checker.ts:13814-13820 — <c>hasLateBindableIndexSignature</c>.</summary>
    internal bool HasLateBindableIndexSignature(IDeclaration node)
    {
        var name = Utilities.GetNameOfDeclaration((Node)node);
        return name is Node nameNode && IsLateBindableIndexSignature(nameNode);
    }

    /// <summary>ported from: src/compiler/checker.ts:13822-13824 — <c>hasBindableName</c>.</summary>
    /// <remarks>Indicates whether a declaration has an early-bound name or a dynamic name that can be late-bound.</remarks>
    internal bool HasBindableName(IDeclaration node)
        => !Utilities.HasDynamicName((Node)node) || HasLateBindableName(node);

    /// <summary>ported from: src/compiler/checker.ts:13838-13856 — <c>addDeclarationToLateBoundSymbol</c>.</summary>
    /// <remarks>
    /// Adds a declaration to a late-bound dynamic member. This performs the same function for late-bound members that
    /// `addDeclarationToSymbol` in binder.ts performs for early-bound members.
    /// </remarks>
    internal void AddDeclarationToLateBoundSymbol(ISymbol symbol, IDeclaration member, SymbolFlags symbolFlags)
    {
        Debug.Assert((GetCheckFlags(symbol) & CheckFlags.Late) != 0, "Expected a late-bound symbol.");
        symbol.Flags |= symbolFlags;
        GetSymbolLinks(member.Symbol!).LateSymbol = symbol;
        if (symbol.Declarations is null)
        {
            symbol.Declarations = [member];
        }
        else if (member.Symbol!.IsReplaceableByMethod != true)
        {
            symbol.Declarations.Add(member);
        }

        if ((symbolFlags & SymbolFlags.Value) != 0)
        {
            Utilities.SetValueDeclaration(symbol, member);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:13881-13922 — <c>lateBindMember</c>.</summary>
    /// <remarks>
    /// Performs late-binding of a dynamic member. This performs the same function for late-bound members that
    /// `declareSymbol` in binder.ts performs for early-bound members.
    /// <para>
    /// If a symbol is a dynamic name from a computed property, we perform an additional "late" binding phase to attempt
    /// to resolve the name for the symbol from the type of the computed property's expression. If the type of the
    /// expression is a string-literal, numeric-literal, or unique symbol type, we can use that type as the name of the
    /// symbol.
    /// </para>
    /// <para>
    /// For example, given <c>const x = Symbol(); interface I { [x]: number; }</c>: the binder gives the property
    /// <c>[x]: number</c> a special symbol with the name "__computed". In the late-binding phase we can type-check the
    /// expression <c>x</c> and see that it has a unique symbol type which we can then use as the name of the member.
    /// This allows users to define custom symbols that can be used in the members of an object type.
    /// </para>
    /// </remarks>
    internal ISymbol? LateBindMember(ISymbol parent, SymbolTable? earlySymbols, SymbolTable lateSymbols, IDeclaration decl)
    {
        Debug.Assert(decl.Symbol is not null, "The member is expected to have a symbol.");
        var links = GetNodeLinks((Node)decl);
        if (links.ResolvedSymbol is null)
        {
            // In the event we attempt to resolve the late-bound name of this member recursively, fall back to the
            // early-bound name of this member.
            links.ResolvedSymbol = decl.Symbol;
            var declName = NodeTests.IsBinaryExpression((Node)decl) ? ((BinaryExpression)decl).Left! : ((INamedDeclaration)decl).Name!;
            var type = NodeTests.IsElementAccessExpression(declName)
                ? CheckExpressionCached(((ElementAccessExpression)declName).ArgumentExpression!)
                : CheckComputedPropertyName(declName);
            if (Utilities.IsTypeUsableAsPropertyName(type))
            {
                var memberName = Utilities.GetPropertyNameFromType(type);
                var symbolFlags = decl.Symbol!.Flags;

                // Get or add a late-bound symbol for the member. This allows us to merge late-bound accessor declarations.
                var lateSymbol = lateSymbols.GetValueOrDefault(memberName) as ITransientSymbol;
                if (lateSymbol is null)
                {
                    lateSymbol = CreateSymbol(SymbolFlags.None, memberName, CheckFlags.Late);
                    lateSymbols[memberName] = lateSymbol;
                }

                // Report an error if there's a symbol declaration with the same name and conflicting flags.
                var earlySymbol = earlySymbols?.GetValueOrDefault(memberName);
                // Duplicate property declarations of classes are checked in checkClassForDuplicateDeclarations.
                if ((parent.Flags & SymbolFlags.Class) == 0 && (lateSymbol.Flags & GetExcludedSymbolFlags(symbolFlags)) != 0)
                {
                    // If we have an existing early-bound member, combine its declarations so that we can report an error at each declaration.
                    var declarations = earlySymbol is not null
                        ? Core.Concatenate(earlySymbol.Declarations, lateSymbol.Declarations)
                        : lateSymbol.Declarations;
                    var unescapedMemberName = Utilities.UnescapeLeadingUnderscores(memberName);
                    // PORT-NOTE: TS's `!(type.flags & TypeFlags.UniqueESSymbol) && unescapeLeadingUnderscores(memberName)
                    // || declarationNameToString(declName)` is a JS value-producing `&&`/`||` chain; the port states the
                    // same two cases as a conditional over the truthiness of the unescaped name.
                    var name = (type.Flags & TypeFlags.UniqueESSymbol) == 0 && Js.IsTruthy(unescapedMemberName)
                        ? unescapedMemberName
                        : DeclarationNameToString(declName);
                    foreach (var declaration in declarations ?? [])
                    {
                        var declarationName = Utilities.GetNameOfDeclaration((Node)declaration) as Node ?? (Node)declaration;
                        Error(declarationName, Diagnostics.Property_0_was_also_declared_here, name);
                    }

                    Error(declName, Diagnostics.Duplicate_property_0, name);
                    lateSymbol = CreateSymbol(SymbolFlags.None, memberName, CheckFlags.Late);
                }

                GetSymbolLinks(lateSymbol).NameType = type;
                AddDeclarationToLateBoundSymbol(lateSymbol, decl, symbolFlags);
                if (lateSymbol.Parent is not null)
                {
                    Debug.Assert(ReferenceEquals(lateSymbol.Parent, parent), "Existing symbol parent should match new one");
                }
                else
                {
                    lateSymbol.Parent = parent;
                }

                return links.ResolvedSymbol = lateSymbol;
            }
        }

        return links.ResolvedSymbol;
    }

    /// <summary>ported from: src/compiler/checker.ts:13924-13947 — <c>lateBindIndexSignature</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: unlike <c>addDeclarationToLateBoundSymbol</c> we do not set up a <c>.lateSymbol</c> on <c>decl</c>'s
    /// links, since that would point at an index symbol and not a single property symbol, like most consumers would
    /// expect (the reference says so in its own comment).
    /// </remarks>
    internal void LateBindIndexSignature(ISymbol parent, SymbolTable? earlySymbols, SymbolTable lateSymbols, IDeclaration decl)
    {
        // First, late bind the index symbol itself, if needed
        var indexSymbol = lateSymbols.GetValueOrDefault(InternalSymbolName.Index.Value) as ITransientSymbol;
        if (indexSymbol is null)
        {
            var early = earlySymbols?.GetValueOrDefault(InternalSymbolName.Index.Value);
            if (early is null)
            {
                indexSymbol = CreateSymbol(SymbolFlags.None, InternalSymbolName.Index.Value, CheckFlags.Late);
            }
            else
            {
                indexSymbol = CloneSymbol(early);
                GetSymbolLinks(indexSymbol).CheckFlags |= CheckFlags.Late;
            }

            lateSymbols[InternalSymbolName.Index.Value] = indexSymbol;
        }

        // Then just add the computed name as a late bound declaration
        if (indexSymbol.Declarations is null)
        {
            indexSymbol.Declarations = [decl];
        }
        else if (decl.Symbol!.IsReplaceableByMethod != true)
        {
            indexSymbol.Declarations.Add(decl);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:13949-14023 — <c>getResolvedMembersOrExportsOfSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS selects the cache slot with <c>links[resolutionKind]</c> because the enum's members are the
    /// strings <c>"resolvedExports"</c>/<c>"resolvedMembers"</c>; the port switches on
    /// <see cref="MembersOrExportsResolutionKind"/> and reads <see cref="ISymbolLinks.ResolvedExports"/>/
    /// <see cref="ISymbolLinks.ResolvedMembers"/> (the same two slots, in the same order).
    /// </remarks>
    internal SymbolTable GetResolvedMembersOrExportsOfSymbol(ISymbol symbol, MembersOrExportsResolutionKind resolutionKind)
    {
        var links = GetSymbolLinks(symbol);
        var resolved = resolutionKind == MembersOrExportsResolutionKind.ResolvedExports ? links.ResolvedExports : links.ResolvedMembers;
        if (resolved is null)
        {
            var isStatic = resolutionKind == MembersOrExportsResolutionKind.ResolvedExports;
            var earlySymbols = !isStatic
                ? symbol.Members
                : (symbol.Flags & SymbolFlags.Module) != 0 ? GetExportsOfModuleWorker(symbol).Exports : symbol.Exports;

            // In the event we recursively resolve the members/exports of the symbol, we set the initial value of
            // resolvedMembers/resolvedExports to the early-bound members/exports of the symbol.
            resolved = earlySymbols ?? _emptySymbols;

            // fill in any as-yet-unresolved late-bound members.
            var lateSymbols = Utilities.CreateSymbolTable();
            foreach (var decl in symbol.Declarations ?? [])
            {
                var members = Utilities.GetMembersOfDeclaration((Node)decl);
                if (members is not null)
                {
                    foreach (var member in members)
                    {
                        if (isStatic == Utilities.HasStaticModifier(member))
                        {
                            if (HasLateBindableName((IDeclaration)member))
                            {
                                LateBindMember(symbol, earlySymbols, lateSymbols, (IDeclaration)member);
                            }
                            else if (HasLateBindableIndexSignature((IDeclaration)member))
                            {
                                LateBindIndexSignature(symbol, earlySymbols, lateSymbols, (IDeclaration)member);
                            }
                        }
                    }
                }
            }

            var assignments = GetFunctionExpressionParentSymbolOrSymbol(symbol).AssignmentDeclarationMembers;
            if (assignments is not null)
            {
                var decls = Core.ArrayFrom(assignments.Values);
                foreach (var member in decls)
                {
                    var assignmentKind = Utilities.GetAssignmentDeclarationKind((Node)member);
                    var isInstanceMember = assignmentKind == AssignmentDeclarationKind.PrototypeProperty
                        || NodeTests.IsBinaryExpression((Node)member) && IsPossiblyAliasedThisProperty((BinaryExpression)member, assignmentKind)
                        || assignmentKind == AssignmentDeclarationKind.ObjectDefinePrototypeProperty
                        || assignmentKind == AssignmentDeclarationKind.Prototype; // A straight `Prototype` assignment probably can never have a computed name
                    if (isStatic == !isInstanceMember && HasLateBindableName(member))
                    {
                        LateBindMember(symbol, earlySymbols, lateSymbols, member);
                    }
                }
            }

            var combined = CombineSymbolTables(earlySymbols, lateSymbols);
            if ((symbol.Flags & SymbolFlags.Transient) != 0 && links.CjsExportMerged is not null && symbol.Declarations is not null)
            {
                foreach (var decl in symbol.Declarations)
                {
                    var original = resolutionKind == MembersOrExportsResolutionKind.ResolvedExports
                        ? GetSymbolLinks(decl.Symbol!).ResolvedExports
                        : GetSymbolLinks(decl.Symbol!).ResolvedMembers;
                    if (combined is null)
                    {
                        combined = original;
                        continue;
                    }

                    if (original is null)
                    {
                        continue;
                    }

                    original.ForEach((s, name) =>
                    {
                        var existing = combined!.GetValueOrDefault(name);
                        if (existing is null)
                        {
                            combined[name] = s;
                        }
                        else if (ReferenceEquals(existing, s))
                        {
                            return;
                        }
                        else
                        {
                            combined[name] = MergeSymbol(existing, s);
                        }
                    });
                }
            }

            resolved = combined ?? _emptySymbols;
            if (resolutionKind == MembersOrExportsResolutionKind.ResolvedExports)
            {
                links.ResolvedExports = resolved;
            }
            else
            {
                links.ResolvedMembers = resolved;
            }
        }

        return resolved;
    }

    /// <summary>ported from: src/compiler/checker.ts:14025-14029 — <c>getMembersOfSymbol</c>.</summary>
    /// <remarks>
    /// Gets a SymbolTable containing both the early- and late-bound members of a symbol. For a description of
    /// late-binding, see <see cref="LateBindMember"/>.
    /// </remarks>
    internal SymbolTable GetMembersOfSymbol(ISymbol symbol)
        => (symbol.Flags & SymbolFlags.LateBindingContainer) != 0
            ? GetResolvedMembersOrExportsOfSymbol(symbol, MembersOrExportsResolutionKind.ResolvedMembers)
            : symbol.Members ?? _emptySymbols;

    /// <summary>ported from: src/compiler/checker.ts:14037-14053 — <c>getLateBoundSymbol</c>.</summary>
    /// <remarks>
    /// If a symbol is the dynamic name of the member of an object type, get the late-bound symbol of the member. For a
    /// description of late-binding, see <see cref="LateBindMember"/>.
    /// </remarks>
    internal ISymbol GetLateBoundSymbol(ISymbol symbol)
    {
        if ((symbol.Flags & SymbolFlags.ClassMember) != 0 && symbol.EscapedName == InternalSymbolName.Computed.Value)
        {
            var links = GetSymbolLinks(symbol);
            if (links.LateSymbol is null && Core.Some(symbol.Declarations, (d, _) => HasLateBindableName(d)))
            {
                // force late binding of members/exports. This will set the late-bound symbol
                var parent = GetMergedSymbol(symbol.Parent)!;
                if (Core.Some(symbol.Declarations, (d, _) => Utilities.HasStaticModifier((Node)d)))
                {
                    GetExportsOfSymbol(parent);
                }
                else
                {
                    GetMembersOfSymbol(parent);
                }
            }

            return links.LateSymbol ?? (links.LateSymbol = symbol);
        }

        return symbol;
    }

    // ---------------------------------------------------------------------------------------------
    // The members of an anonymous, reverse-mapped or mapped type (checker.ts:14595-15020)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:14595-14698 — <c>resolveAnonymousTypeMembers</c>.</summary>
    /// <remarks>Converts an AnonymousType to a ResolvedType.</remarks>
    internal void ResolveAnonymousTypeMembers(AnonymousType type)
    {
        if (type.Target is not null)
        {
            SetStructuredTypeMembers(type, _emptySymbols, [], [], []);
            var targetMembers = CreateInstantiatedSymbolTable(GetPropertiesOfObjectType(type.Target), (TypeMapper)type.Mapper!, /*mappingThisOnly*/ false);
            var targetCallSignatures = InstantiateSignatures(GetSignaturesOfType(type.Target, SignatureKind.Call), (TypeMapper)type.Mapper!)!;
            var targetConstructSignatures = InstantiateSignatures(GetSignaturesOfType(type.Target, SignatureKind.Construct), (TypeMapper)type.Mapper!)!;
            var targetIndexInfos = InstantiateIndexInfos(GetIndexInfosOfType(type.Target), (TypeMapper)type.Mapper!)!;
            SetStructuredTypeMembers(type, targetMembers, targetCallSignatures, targetConstructSignatures, targetIndexInfos);
            return;
        }

        var symbol = GetMergedSymbol(type.Symbol)!;
        if ((symbol.Flags & SymbolFlags.TypeLiteral) != 0)
        {
            SetStructuredTypeMembers(type, _emptySymbols, [], [], []);
            var members = GetMembersOfSymbol(symbol);
            var callSignatures = GetSignaturesOfSymbol(members.GetValueOrDefault(InternalSymbolName.Call.Value));
            var constructSignatures = GetSignaturesOfSymbol(members.GetValueOrDefault(InternalSymbolName.New.Value));
            var indexInfos = GetIndexInfosOfSymbol(symbol);
            SetStructuredTypeMembers(type, members, callSignatures, constructSignatures, indexInfos);
            return;
        }

        // Combinations of function, class, enum and module
        var combinedMembers = GetExportsOfSymbol(symbol);
        IList<IIndexInfo>? combinedIndexInfos = null;
        if (ReferenceEquals(symbol, _globalThisSymbol))
        {
            var varsOnly = Utilities.CreateSymbolTable();
            foreach (var p in combinedMembers.Values)
            {
                if ((p.Flags & SymbolFlags.BlockScoped) == 0 && !((p.Flags & SymbolFlags.ValueModule) != 0
                    && p.Declarations is not null
                    && Core.Length(p.Declarations) != 0
                    && Core.Every(p.Declarations, (d, _) => Utilities.IsAmbientModule((Node)d))))
                {
                    varsOnly[p.EscapedName ?? ""] = p;
                }
            }

            combinedMembers = varsOnly;
        }

        IIndexInfo? baseConstructorIndexInfo = null;
        SetStructuredTypeMembers(type, combinedMembers, [], [], []);
        if ((symbol.Flags & SymbolFlags.Class) != 0)
        {
            var classType = GetDeclaredTypeOfClassOrInterface(symbol);
            var baseConstructorType = GetBaseConstructorTypeOfClass(classType);
            if ((baseConstructorType.Flags & (TypeFlags.Object | TypeFlags.Intersection | TypeFlags.TypeVariable)) != 0)
            {
                combinedMembers = Utilities.CreateSymbolTable(GetNamedOrIndexSignatureMembers(combinedMembers, symbol));
                AddInheritedMembers(combinedMembers, GetPropertiesOfType(baseConstructorType));
            }
            else if (ReferenceEquals(baseConstructorType, _anyType))
            {
                baseConstructorIndexInfo = _anyBaseTypeIndexInfo;
            }
        }

        var indexSymbol = GetIndexSymbolFromSymbolTable(combinedMembers);
        if (indexSymbol is not null)
        {
            combinedIndexInfos = GetIndexInfosOfIndexSymbol(indexSymbol, [.. combinedMembers.Values]);
        }
        else
        {
            if (baseConstructorIndexInfo is not null)
            {
                combinedIndexInfos = AppendIndexInfo(combinedIndexInfos, baseConstructorIndexInfo, /*union*/ false);
            }

            if ((symbol.Flags & SymbolFlags.Enum) != 0
                && ((GetDeclaredTypeOfSymbol(symbol).Flags & TypeFlags.Enum) != 0
                    || Core.Some(type.Properties, prop => (GetTypeOfSymbol(prop).Flags & TypeFlags.NumberLike) != 0)))
            {
                combinedIndexInfos = AppendIndexInfo(combinedIndexInfos, _enumNumberIndexInfo, /*union*/ false);
            }
        }

        SetStructuredTypeMembers(type, combinedMembers, [], [], combinedIndexInfos ?? []);
        // We resolve the members before computing the signatures because a signature may use typeof with a qualified
        // name expression that circularly references the type we are in the process of resolving (see issue #6072).
        // The temporarily empty signature list will never be observed because a qualified name can't reference
        // signatures.
        if ((symbol.Flags & (SymbolFlags.Function | SymbolFlags.Method)) != 0)
        {
            type.CallSignatures = GetSignaturesOfSymbol(symbol);
        }

        // And likewise for construct signatures for classes
        if ((symbol.Flags & SymbolFlags.Class) != 0)
        {
            var classType = GetDeclaredTypeOfClassOrInterface(symbol);
            List<ISignature> constructSignatures = symbol.Members is not null
                ? GetSignaturesOfSymbol(symbol.Members.GetValueOrDefault(InternalSymbolName.Constructor.Value))
                : [];
            if ((symbol.Flags & SymbolFlags.Function) != 0)
            {
                constructSignatures = [.. constructSignatures];
                Core.AddRange(constructSignatures, Core.MapDefined<ISignature, ISignature>(type.CallSignatures, (sig, _) =>
                    IsJSConstructor(sig.Declaration)
                        ? CreateSignature(
                            sig.Declaration,
                            sig.TypeParameters,
                            sig.ThisParameter,
                            sig.Parameters,
                            classType,
                            /*resolvedTypePredicate*/ null,
                            sig.MinArgumentCount,
                            sig.Flags & SignatureFlags.PropagatingFlags)
                        : null));
            }

            if (Core.Length(constructSignatures) == 0)
            {
                constructSignatures = [.. GetDefaultConstructSignatures(classType)];
            }

            type.ConstructSignatures = constructSignatures;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:14739-14745 — <c>replaceIndexedAccess</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>ReplaceableIndexedAccessType</c> is a type alias over <see cref="IIndexedAccessType"/> that
    /// narrows the two members to <c>TypeParameter</c>; the port takes the same face and casts the two members at the
    /// two points the reference's narrowing guarantees it (the caller checks both flags first).
    /// </remarks>
    internal IType ReplaceIndexedAccess(IType instantiable, IIndexedAccessType type, IType replacement)
    {
        // map type.indexType to 0
        // map type.objectType to `[TReplacement]`
        // thus making the indexed access `[TReplacement][0]` or `TReplacement`
        return InstantiateType(
            instantiable,
            CreateTypeMapper([(ITypeParameter)type.IndexType!, (ITypeParameter)type.ObjectType!], [GetNumberLiteralType(0), CreateTupleType([replacement])]))!;
    }

    /// <summary>ported from: src/compiler/checker.ts:14747-14758 — <c>getLimitedConstraint</c>.</summary>
    /// <remarks>
    /// If the original mapped type had an intersection constraint we extract its components, and we make an attempt to
    /// do so even if the intersection has been reduced to a union. This entire process allows us to possibly retrieve
    /// the filtering type literals, e.g. <c>{ [K in keyof U &amp; ("a" | "b") ] }</c> -&gt; <c>"a" | "b"</c>.
    /// </remarks>
    internal IType? GetLimitedConstraint(ReverseMappedType type)
    {
        var constraint = GetConstraintTypeFromMappedType(type.MappedType!);
        if (!((constraint.Flags & TypeFlags.Union) != 0 || (constraint.Flags & TypeFlags.Intersection) != 0))
        {
            return null;
        }

        var origin = (constraint.Flags & TypeFlags.Union) != 0 ? ((IUnionType)constraint).Origin : constraint;
        if (origin is null || (origin.Flags & TypeFlags.Intersection) == 0)
        {
            return null;
        }

        var limitedConstraint = GetIntersectionType([.. Core.Filter(((IIntersectionType)origin).Types, t => !ReferenceEquals(t, type.ConstraintType))!]);
        return !ReferenceEquals(limitedConstraint, _neverType) ? limitedConstraint : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:14712-14787 — <c>resolveReverseMappedTypeMembers</c>.</summary>
    internal void ResolveReverseMappedTypeMembers(ReverseMappedType type)
    {
        var indexInfo = GetIndexInfoOfType(type.Source!, _stringType);
        var modifiers = GetMappedTypeModifiers(type.MappedType!);
        var readonlyMask = (modifiers & MappedTypeModifiers.IncludeReadonly) == 0;
        var optionalMask = (modifiers & MappedTypeModifiers.IncludeOptional) != 0 ? SymbolFlags.None : SymbolFlags.Optional;
        IList<IIndexInfo> indexInfos = indexInfo is not null
            ? [CreateIndexInfo(_stringType, InferReverseMappedType(indexInfo.Type!, type.MappedType!, type.ConstraintType!) ?? _unknownType, readonlyMask && indexInfo.IsReadonly)]
            : [];
        var members = Utilities.CreateSymbolTable();
        var limitedConstraint = GetLimitedConstraint(type);
        foreach (var prop in GetPropertiesOfType(type.Source!))
        {
            // In case of a reverse mapped type with an intersection constraint, if we were able to extract the
            // filtering type literals we skip those properties that are not assignable to them, because the extra
            // properties wouldn't get through the application of the mapped type anyway.
            if (limitedConstraint is not null)
            {
                var propertyNameType = GetLiteralTypeFromProperty(prop, TypeFlags.StringOrNumberLiteralOrUnique);
                if (!IsTypeAssignableTo(propertyNameType, limitedConstraint))
                {
                    continue;
                }
            }

            var checkFlags = CheckFlags.ReverseMapped | (readonlyMask && IsReadonlySymbol(prop) ? CheckFlags.Readonly : CheckFlags.None);
            var inferredProp = (IReverseMappedSymbol)CreateSymbol(SymbolFlags.Property | (prop.Flags & optionalMask), prop.EscapedName!, checkFlags);
            inferredProp.Declarations = prop.Declarations;
            var inferredLinks = GetSymbolLinks(inferredProp);
            inferredLinks.NameType = GetSymbolLinks(prop).NameType;
            inferredLinks.PropertyType = GetTypeOfSymbol(prop);
            var constraintType = type.ConstraintType!.Type!;
            if ((constraintType.Flags & TypeFlags.IndexedAccess) != 0
                && (((IIndexedAccessType)constraintType).ObjectType!.Flags & TypeFlags.TypeParameter) != 0
                && (((IIndexedAccessType)constraintType).IndexType!.Flags & TypeFlags.TypeParameter) != 0)
            {
                // A reverse mapping of `{[K in keyof T[K_1]]: T[K_1]}` is the same as that of `{[K in keyof T]: T}`,
                // since all we care about is inferring to the "type parameter" (or indexed access) shared by the
                // constraint and template. So, to reduce the number of type identities produced, we simplify such
                // indexed access occurrences.
                var newTypeParam = ((IIndexedAccessType)constraintType).ObjectType!;
                var newMappedType = ReplaceIndexedAccess(type.MappedType!, (IIndexedAccessType)constraintType, newTypeParam);
                inferredLinks.MappedType = (IMappedType)newMappedType;
                inferredLinks.ConstraintType = (IIndexType)GetIndexType(newTypeParam);
            }
            else
            {
                inferredLinks.MappedType = type.MappedType;
                inferredLinks.ConstraintType = type.ConstraintType;
            }

            members[prop.EscapedName!] = inferredProp;
        }

        SetStructuredTypeMembers(type, members, [], [], indexInfos);
    }

    /// <summary>ported from: src/compiler/checker.ts:14760-14786 — <c>getLowerBoundOfKeyType</c>.</summary>
    /// <remarks>
    /// Return the lower bound of the key type in a mapped type. Intuitively, the lower bound includes those keys that
    /// are known to always be present, for example because of constraints on type parameters (e.g. 'keyof T' for a
    /// constrained T).
    /// </remarks>
    internal IType GetLowerBoundOfKeyType(IType type)
    {
        if ((type.Flags & TypeFlags.Index) != 0)
        {
            var t = GetApparentType(((IIndexType)type).Type);
            return IsGenericTupleType(t) ? GetKnownKeysOfTupleType((ITupleTypeReference)t) : GetIndexType(t);
        }

        if ((type.Flags & TypeFlags.Conditional) != 0)
        {
            var conditional = (IConditionalType)type;
            if (conditional.Root!.IsDistributive)
            {
                var checkType = conditional.CheckType!;
                var constraint = GetLowerBoundOfKeyType(checkType);
                if (!ReferenceEquals(constraint, checkType))
                {
                    return GetConditionalTypeInstantiation(
                        conditional,
                        PrependTypeMapping(conditional.Root!.CheckType!, constraint, (TypeMapper?)conditional.Mapper),
                        /*forConstraint*/ false,
                        /*aliasSymbol*/ null,
                        /*aliasTypeArguments*/ null);
                }
            }

            return type;
        }

        if ((type.Flags & TypeFlags.Union) != 0)
        {
            return MapType(type, t => GetLowerBoundOfKeyType(t), /*noReductions*/ true);
        }

        if ((type.Flags & TypeFlags.Intersection) != 0)
        {
            // Similarly to getTypeFromIntersectionTypeNode, we preserve the special string & {}, number & {},
            // and bigint & {} intersections that are used to prevent subtype reduction in union types.
            var types = ((IIntersectionType)type).Types!;
            if (types.Count == 2
                && (types[0].Flags & (TypeFlags.String | TypeFlags.Number | TypeFlags.BigInt)) != 0
                && ReferenceEquals(types[1], _emptyTypeLiteralType))
            {
                return type;
            }

            return GetIntersectionType([.. Core.SameMap(types, (t, _) => GetLowerBoundOfKeyType(t))!]);
        }

        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:14790-14796 — <c>getIsLateCheckFlag</c>.</summary>
    internal CheckFlags GetIsLateCheckFlag(ISymbol s) => GetCheckFlags(s) & CheckFlags.Late;

    /// <summary>ported from: src/compiler/checker.ts:14794-14802 — <c>forEachMappedTypePropertyKeyTypeAndIndexSignatureKeyType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>cb</c> is <c>(keyType: Type) =&gt; void</c>, which is <see cref="Action{T}"/> over
    /// <see cref="IType"/>; the two callers pass a local function, exactly as the reference passes its nested one.
    /// </remarks>
    internal void ForEachMappedTypePropertyKeyTypeAndIndexSignatureKeyType(IType type, TypeFlags include, bool stringsOnly, Action<IType> cb)
    {
        foreach (var prop in GetPropertiesOfType(type))
        {
            cb(GetLiteralTypeFromProperty(prop, include));
        }

        if ((type.Flags & TypeFlags.Any) != 0)
        {
            cb(_stringType);
        }
        else
        {
            foreach (var info in GetIndexInfosOfType(type))
            {
                if (!stringsOnly || (info.KeyType!.Flags & (TypeFlags.String | TypeFlags.TemplateLiteral)) != 0)
                {
                    cb(info.KeyType!);
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:14811-14885 — <c>resolveMappedTypeMembers</c>.</summary>
    /// <remarks>Resolve the members of a mapped type <c>{ [P in K]: T }</c>.</remarks>
    internal void ResolveMappedTypeMembers(MappedType type)
    {
        var members = Utilities.CreateSymbolTable();
        List<IIndexInfo>? indexInfos = null;
        // Resolve upfront such that recursive references see an empty object type.
        SetStructuredTypeMembers(type, _emptySymbols, [], [], []);
        // In { [P in K]: T }, we refer to P as the type parameter type, K as the constraint type,
        // and T as the template type.
        var typeParameter = GetTypeParameterFromMappedType(type);
        var constraintType = GetConstraintTypeFromMappedType(type);
        var mappedType = type.Target as IMappedType ?? type;
        var nameType = GetNameTypeFromMappedType(mappedType);
        var shouldLinkPropDeclarations = GetMappedTypeNameTypeKind(mappedType) != MappedTypeNameTypeKind.Remapping;
        var templateType = GetTemplateTypeFromMappedType(mappedType);
        var modifiersType = GetApparentType(GetModifiersTypeFromMappedType(type)); // The 'T' in 'keyof T'
        var templateModifiers = GetMappedTypeModifiers(type);
        const TypeFlags include = TypeFlags.StringOrNumberLiteralOrUnique;
        if (IsMappedTypeWithKeyofConstraintDeclaration(type))
        {
            // We have a { [P in keyof T]: X }
            ForEachMappedTypePropertyKeyTypeAndIndexSignatureKeyType(modifiersType, include, /*stringsOnly*/ false, AddMemberForKeyType);
        }
        else
        {
            ForEachType(GetLowerBoundOfKeyType(constraintType), AddMemberForKeyType);
        }

        SetStructuredTypeMembers(type, members, [], [], indexInfos ?? []);

        void AddMemberForKeyType(IType keyType)
        {
            var propNameType = nameType is not null
                ? InstantiateType(nameType, AppendTypeMapping((TypeMapper?)type.Mapper, typeParameter, keyType))!
                : keyType;
            ForEachType(propNameType, t => AddMemberForKeyTypeWorker(keyType, t));
        }

        void AddMemberForKeyTypeWorker(IType keyType, IType propNameType)
        {
            // If the current iteration type constituent is a string literal type, create a property.
            // Otherwise, for type string create a string index signature.
            if (Utilities.IsTypeUsableAsPropertyName(propNameType))
            {
                var propName = Utilities.GetPropertyNameFromType(propNameType);
                // String enum members from separate enums with identical values are distinct types with the same
                // property name. Make the resulting property symbol's name type be the union of those enum member
                // types.
                var existingProp = members.GetValueOrDefault(propName) as IMappedSymbol;
                if (existingProp is not null)
                {
                    var existingLinks = GetSymbolLinks(existingProp);
                    existingLinks.NameType = GetUnionType([existingLinks.NameType!, propNameType]);
                    existingLinks.KeyType = GetUnionType([existingLinks.KeyType!, keyType]);
                }
                else
                {
                    var modifiersProp = Utilities.IsTypeUsableAsPropertyName(keyType)
                        ? GetPropertyOfType(modifiersType, Utilities.GetPropertyNameFromType(keyType))
                        : null;
                    var isOptional = (templateModifiers & MappedTypeModifiers.IncludeOptional) != 0
                        || ((templateModifiers & MappedTypeModifiers.ExcludeOptional) == 0
                            && modifiersProp is not null
                            && (modifiersProp.Flags & SymbolFlags.Optional) != 0);
                    var isReadonly = (templateModifiers & MappedTypeModifiers.IncludeReadonly) != 0
                        || ((templateModifiers & MappedTypeModifiers.ExcludeReadonly) == 0
                            && modifiersProp is not null
                            && IsReadonlySymbol(modifiersProp));
                    var stripOptional = _strictNullChecks && !isOptional && modifiersProp is not null && (modifiersProp.Flags & SymbolFlags.Optional) != 0;
                    var lateFlag = modifiersProp is not null ? GetIsLateCheckFlag(modifiersProp) : CheckFlags.None;
                    var prop = (IMappedSymbol)CreateSymbol(
                        SymbolFlags.Property | (isOptional ? SymbolFlags.Optional : SymbolFlags.None),
                        propName,
                        lateFlag
                            | CheckFlags.Mapped
                            | (isReadonly ? CheckFlags.Readonly : CheckFlags.None)
                            | (stripOptional ? CheckFlags.StripOptional : CheckFlags.None));
                    var propLinks = GetSymbolLinks(prop);
                    propLinks.MappedType = type;
                    propLinks.NameType = propNameType;
                    propLinks.KeyType = keyType;
                    if (modifiersProp is not null)
                    {
                        propLinks.SyntheticOrigin = modifiersProp;
                        prop.Declarations = shouldLinkPropDeclarations ? modifiersProp.Declarations : null;
                    }

                    members[propName] = prop;
                }
            }
            else if (IsValidIndexKeyType(propNameType) || (propNameType.Flags & (TypeFlags.Any | TypeFlags.Enum)) != 0)
            {
                var indexKeyType = (propNameType.Flags & (TypeFlags.Any | TypeFlags.String)) != 0 ? _stringType
                    : (propNameType.Flags & (TypeFlags.Number | TypeFlags.Enum)) != 0 ? _numberType
                    : propNameType;
                var propType = InstantiateType(templateType, AppendTypeMapping((TypeMapper?)type.Mapper, typeParameter, keyType))!;
                var modifiersIndexInfo = GetApplicableIndexInfo(modifiersType, propNameType);
                var isReadonly = (templateModifiers & MappedTypeModifiers.IncludeReadonly) != 0
                    || ((templateModifiers & MappedTypeModifiers.ExcludeReadonly) == 0 && modifiersIndexInfo?.IsReadonly == true);
                var indexInfo = CreateIndexInfo(indexKeyType, propType, isReadonly);
                indexInfos = AppendIndexInfo(indexInfos, indexInfo, /*union*/ true);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:14887-14912 — <c>getTypeOfMappedSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>symbol.links</c> is the <c>MappedSymbolLinks</c> face of the one links object
    /// (<c>Model/SymbolLinks.cs</c>); the port reads it through <see cref="GetSymbolLinks"/> for the same reason every
    /// other mapped-symbol access does.
    /// </remarks>
    internal IType GetTypeOfMappedSymbol(IMappedSymbol symbol)
    {
        var links = GetSymbolLinks(symbol);
        if (links.Type is null)
        {
            var mappedType = links.MappedType!;
            if (!PushTypeResolution(symbol, TypeSystemPropertyName.Type))
            {
                mappedType.ContainsError = true;
                return _errorType;
            }

            var templateType = GetTemplateTypeFromMappedType(mappedType.Target as IMappedType ?? mappedType);
            var mapper = AppendTypeMapping((TypeMapper?)mappedType.Mapper, GetTypeParameterFromMappedType(mappedType), links.KeyType!);
            var propType = InstantiateType(templateType, mapper)!;
            // When creating an optional property in strictNullChecks mode, if 'undefined' isn't assignable to the
            // type, we include 'undefined' in the type. Similarly, when creating a non-optional property in
            // strictNullChecks mode, if the underlying property is optional we remove 'undefined' from the type.
            var type = _strictNullChecks && (symbol.Flags & SymbolFlags.Optional) != 0 && !MaybeTypeOfKind(propType, TypeFlags.Undefined | TypeFlags.Void)
                ? GetOptionalType(propType, /*isProperty*/ true)
                : (links.CheckFlags & CheckFlags.StripOptional) != 0 ? RemoveMissingOrUndefinedType(propType)
                : propType;
            if (!PopTypeResolution())
            {
                Error(_currentNode, Diagnostics.Type_of_property_0_circularly_references_itself_in_mapped_type_1, SymbolToString(symbol), TypeToString(mappedType));
                type = _errorType;
            }

            links.Type ??= type;
        }

        return links.Type!;
    }

    /// <summary>ported from: src/compiler/checker.ts:14997-15012 — <c>isGenericMappedType</c>.</summary>
    internal bool IsGenericMappedType(IType type)
    {
        if ((GetObjectFlags(type) & ObjectFlags.Mapped) != 0)
        {
            var mappedType = (IMappedType)type;
            var constraint = GetConstraintTypeFromMappedType(mappedType);
            if (IsGenericIndexType(constraint))
            {
                return true;
            }

            // A mapped type is generic if the 'as' clause references generic types other than the iteration type.
            // To determine this, we substitute the constraint type (that we now know isn't generic) for the iteration
            // type and check whether the resulting type is generic.
            var nameType = GetNameTypeFromMappedType(mappedType);
            if (nameType is not null
                && IsGenericIndexType(InstantiateType(nameType, MakeUnaryTypeMapper(GetTypeParameterFromMappedType(mappedType), constraint))!))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:15014-15020 — <c>getMappedTypeNameTypeKind</c>.</summary>
    internal MappedTypeNameTypeKind GetMappedTypeNameTypeKind(IMappedType type)
    {
        var nameType = GetNameTypeFromMappedType(type);
        if (nameType is null)
        {
            return MappedTypeNameTypeKind.None;
        }

        return IsTypeAssignableTo(nameType, GetTypeParameterFromMappedType(type))
            ? MappedTypeNameTypeKind.Filtering
            : MappedTypeNameTypeKind.Remapping;
    }

    // ---------------------------------------------------------------------------------------------
    // The properties of a union or intersection type (checker.ts:15567-15795)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:15567-15754 — <c>createUnionOrIntersectionProperty</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>Map&lt;SymbolId, Symbol&gt;</c> is keyed by the symbol id, so the port uses
    /// <c>Dictionary&lt;int, ISymbol&gt;</c> with <see cref="Utilities.GetSymbolId"/> (the id it is keyed by) and the
    /// insertion order of <c>Map</c> for the <c>props</c> list is preserved because <c>Dictionary</c> enumeration
    /// order is insertion order only for an untouched dictionary — the reference's own iteration order over the id
    /// map is what both produce here, and the second loop only aggregates over the set.
    /// </remarks>
    internal ISymbol? CreateUnionOrIntersectionProperty(UnionOrIntersectionType containingType, string name, bool? skipObjectFunctionPropertyAugment = null)
    {
        var propFlags = SymbolFlags.None;
        ISymbol? singleProp = null;
        Dictionary<int, ISymbol>? propSet = null;
        List<IType>? indexTypes = null;
        var isUnion = (containingType.Flags & TypeFlags.Union) != 0;
        // Flags we want to propagate to the result if they exist in all source symbols
        SymbolFlags? optionalFlag = null;
        var syntheticFlag = CheckFlags.SyntheticMethod;
        var checkFlags = isUnion ? CheckFlags.None : CheckFlags.Readonly;
        var mergedInstantiations = false;
        foreach (var current in containingType.Types!)
        {
            var type = GetApparentType(current);
            if (!(IsErrorType(type) || (type.Flags & TypeFlags.Never) != 0))
            {
                var prop = GetPropertyOfType(type, name, skipObjectFunctionPropertyAugment);
                var modifiers = prop is not null ? Utilities.GetDeclarationModifierFlagsFromSymbol(prop) : ModifierFlags.None;
                if (prop is not null)
                {
                    if ((prop.Flags & SymbolFlags.ClassMember) != 0)
                    {
                        optionalFlag ??= isUnion ? SymbolFlags.None : SymbolFlags.Optional;
                        if (isUnion)
                        {
                            optionalFlag |= prop.Flags & SymbolFlags.Optional;
                        }
                        else
                        {
                            optionalFlag &= prop.Flags;
                        }
                    }

                    if (singleProp is null)
                    {
                        singleProp = prop;
                        propFlags = (prop.Flags & SymbolFlags.Accessor) != 0 ? prop.Flags & SymbolFlags.Accessor : SymbolFlags.Property;
                    }
                    else if (!ReferenceEquals(prop, singleProp))
                    {
                        var isInstantiation = ReferenceEquals(GetTargetSymbol(prop) ?? prop, GetTargetSymbol(singleProp) ?? singleProp);
                        // If the symbols are instances of one another with identical types - consider the symbols
                        // equivalent and just use the first one, which thus allows us to avoid eliding private
                        // members when intersecting a (this-)instantiations of a class with its raw base or another
                        // instance.
                        if (isInstantiation && CompareProperties(singleProp, prop, (IType a, IType b) => ReferenceEquals(a, b) ? Ternary.True : Ternary.False) == Ternary.True)
                        {
                            // If we merged instantiations of a generic type, we replicate the symbol parent resetting
                            // behavior we used to do when we recorded multiple distinct symbols so that we still get,
                            // eg, `Array<T>.length` printed back and not `Array<string>.length` when we're looking at
                            // a `.length` access on a `string[] | number[]`.
                            mergedInstantiations = singleProp.Parent is not null && Core.Length(GetLocalTypeParametersOfClassOrInterfaceOrTypeAlias(singleProp.Parent)) != 0;
                        }
                        else
                        {
                            if (propSet is null)
                            {
                                propSet = [];
                                propSet[Utilities.GetSymbolId(singleProp)] = singleProp;
                            }

                            var id = Utilities.GetSymbolId(prop);
                            if (!propSet.ContainsKey(id))
                            {
                                propSet[id] = prop;
                            }
                        }

                        // classes created by mixins are represented as intersections and overriding a property in a
                        // derived class redefines it completely at runtime so a get accessor can't be merged with a
                        // set accessor in a base class, for that reason the accessor flags are only used when they are
                        // the same in all constituents.
                        if ((propFlags & SymbolFlags.Accessor) != 0 && (prop.Flags & SymbolFlags.Accessor) != (propFlags & SymbolFlags.Accessor))
                        {
                            propFlags = (propFlags & ~SymbolFlags.Accessor) | SymbolFlags.Property;
                        }
                    }

                    if (isUnion && IsReadonlySymbol(prop))
                    {
                        checkFlags |= CheckFlags.Readonly;
                    }
                    else if (!isUnion && !IsReadonlySymbol(prop))
                    {
                        checkFlags &= ~CheckFlags.Readonly;
                    }

                    checkFlags |= ((modifiers & ModifierFlags.NonPublicAccessibilityModifier) == 0 ? CheckFlags.ContainsPublic : CheckFlags.None)
                        | ((modifiers & ModifierFlags.Protected) != 0 ? CheckFlags.ContainsProtected : CheckFlags.None)
                        | ((modifiers & ModifierFlags.Private) != 0 ? CheckFlags.ContainsPrivate : CheckFlags.None)
                        | ((modifiers & ModifierFlags.Static) != 0 ? CheckFlags.ContainsStatic : CheckFlags.None);
                    if (!IsPrototypeProperty(prop))
                    {
                        syntheticFlag = CheckFlags.SyntheticProperty;
                    }
                }
                else if (isUnion)
                {
                    var indexInfo = !IsLateBoundName(name) ? GetApplicableIndexInfoForName(type, name) : null;
                    if (indexInfo is not null)
                    {
                        propFlags = (propFlags & ~SymbolFlags.Accessor) | SymbolFlags.Property;
                        checkFlags |= CheckFlags.WritePartial | (indexInfo.IsReadonly ? CheckFlags.Readonly : CheckFlags.None);
                        indexTypes = (List<IType>?)Core.Append(indexTypes, IsTupleType(type) ? GetRestTypeOfTupleType(type) ?? _undefinedType : indexInfo.Type!);
                    }
                    else if (IsObjectLiteralType(type) && (GetObjectFlags(type) & ObjectFlags.ContainsSpread) == 0)
                    {
                        checkFlags |= CheckFlags.WritePartial;
                        indexTypes = (List<IType>?)Core.Append(indexTypes, _undefinedType);
                    }
                    else
                    {
                        checkFlags |= CheckFlags.ReadPartial;
                    }
                }
            }
        }

        if (singleProp is null
            || isUnion
                && (propSet is not null || (checkFlags & CheckFlags.Partial) != 0)
                && (checkFlags & (CheckFlags.ContainsPrivate | CheckFlags.ContainsProtected)) != 0
                && !(propSet is not null && GetCommonDeclarationsOfSymbols(propSet.Values) is not null))
        {
            // No property was found, or, in a union, a property has a private or protected declaration in one
            // constituent, but is missing or has a different declaration in another constituent.
            return null;
        }

        if (propSet is null && (checkFlags & CheckFlags.ReadPartial) == 0 && indexTypes is null)
        {
            if (mergedInstantiations)
            {
                // No symbol from a union/intersection should have a `.parent` set (since unions/intersections don't act
                // as symbol parents) unless that parent is "reconstituted" from the "first value declaration" on the
                // symbol (which is likely different than its instantiated parent!). They also have a
                // `.containingType` set, which affects some services endpoints behavior, like `getRootSymbol`.
                var links = (singleProp as ITransientSymbol)?.Links;
                var clone = CreateSymbolWithType(singleProp, links?.Type);
                clone.Parent = ((Node?)singleProp.ValueDeclaration)?.Symbol?.Parent;
                var cloneLinks = GetSymbolLinks(clone);
                cloneLinks.ContainingType = containingType;
                cloneLinks.Mapper = links?.Mapper;
                cloneLinks.WriteType = GetWriteTypeOfSymbol(singleProp);
                return clone;
            }
            else
            {
                return singleProp;
            }
        }

        IList<ISymbol> props = propSet is not null ? Core.ArrayFrom(propSet.Values) : [singleProp!];
        List<IDeclaration>? declarations = null;
        IType? firstType = null;
        IType? nameType = null;
        var propTypes = new List<IType>();
        List<IType>? writeTypes = null;
        IDeclaration? firstValueDeclaration = null;
        var hasNonUniformValueDeclaration = false;
        foreach (var prop in props)
        {
            if (firstValueDeclaration is null)
            {
                firstValueDeclaration = prop.ValueDeclaration;
            }
            else if (prop.ValueDeclaration is not null && !ReferenceEquals(prop.ValueDeclaration, firstValueDeclaration))
            {
                hasNonUniformValueDeclaration = true;
            }

            declarations = (List<IDeclaration>?)Core.AddRange(declarations, prop.Declarations);
            var type = GetTypeOfSymbol(prop);
            if (firstType is null)
            {
                firstType = type;
                nameType = GetSymbolLinks(prop).NameType;
            }

            var writeType = GetWriteTypeOfSymbol(prop);
            if (writeTypes is not null || !ReferenceEquals(writeType, type))
            {
                writeTypes = (List<IType>?)Core.Append(writeTypes ?? [.. propTypes], writeType);
            }

            if (!ReferenceEquals(type, firstType))
            {
                checkFlags |= CheckFlags.HasNonUniformType;
            }

            if (IsLiteralType(type) || IsPatternLiteralType(type))
            {
                checkFlags |= CheckFlags.HasLiteralType;
            }

            if ((type.Flags & TypeFlags.Never) != 0 && !ReferenceEquals(type, _uniqueLiteralType))
            {
                checkFlags |= CheckFlags.HasNeverType;
            }

            propTypes.Add(type);
        }

        Core.AddRange(propTypes, indexTypes);
        var result = CreateSymbol(propFlags | (optionalFlag ?? SymbolFlags.None), name, syntheticFlag | checkFlags);
        var resultLinks = GetSymbolLinks(result);
        resultLinks.ContainingType = containingType;
        if (!hasNonUniformValueDeclaration && firstValueDeclaration is not null)
        {
            result.ValueDeclaration = firstValueDeclaration;

            // Inherit information about parent type.
            if (firstValueDeclaration.Symbol?.Parent is { } firstValueParent)
            {
                result.Parent = firstValueParent;
            }
        }

        result.Declarations = declarations;
        resultLinks.NameType = nameType;
        if (propTypes.Count > 2)
        {
            // When `propTypes` has the potential to explode in size when normalized, defer normalization until
            // absolutely needed.
            resultLinks.CheckFlags |= CheckFlags.DeferredType;
            resultLinks.DeferralParent = containingType;
            resultLinks.DeferralConstituents = propTypes;
            resultLinks.DeferralWriteConstituents = writeTypes;
        }
        else
        {
            resultLinks.Type = isUnion ? GetUnionType(propTypes) : GetIntersectionType(propTypes);
            if (writeTypes is not null)
            {
                resultLinks.WriteType = isUnion ? GetUnionType(writeTypes) : GetIntersectionType(writeTypes);
            }
        }

        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:15756-15775 — <c>getUnionOrIntersectionProperty</c>.</summary>
    /// <remarks>
    /// Return the symbol for a given property in a union or intersection type, or undefined if the property does not
    /// exist in any constituent type. Note that the returned property may only be present in some constituents, in
    /// which case the isPartial flag is set when the containing type is union type. We need these partial properties
    /// when identifying discriminant properties, but otherwise they are filtered out and do not appear to be present
    /// in the union type.
    /// </remarks>
    internal ISymbol? GetUnionOrIntersectionProperty(UnionOrIntersectionType type, string name, bool? skipObjectFunctionPropertyAugment = null)
    {
        var property = skipObjectFunctionPropertyAugment == true
            ? type.PropertyCacheWithoutObjectFunctionPropertyAugment?.GetValueOrDefault(name)
            : type.PropertyCache?.GetValueOrDefault(name);
        if (property is null)
        {
            property = CreateUnionOrIntersectionProperty(type, name, skipObjectFunctionPropertyAugment);
            if (property is not null)
            {
                var properties = skipObjectFunctionPropertyAugment == true
                    ? type.PropertyCacheWithoutObjectFunctionPropertyAugment ??= Utilities.CreateSymbolTable()
                    : type.PropertyCache ??= Utilities.CreateSymbolTable();
                properties[name] = property;
                // Propagate an entry from the non-augmented cache to the augmented cache unless the property is partial.
                if (skipObjectFunctionPropertyAugment == true
                    && (GetCheckFlags(property) & CheckFlags.Partial) == 0
                    && type.PropertyCache?.GetValueOrDefault(name) is null)
                {
                    var augmentedProperties = type.PropertyCache ??= Utilities.CreateSymbolTable();
                    augmentedProperties[name] = property;
                }
            }
        }

        return property;
    }

    /// <summary>ported from: src/compiler/checker.ts:15777-15795 — <c>getCommonDeclarationsOfSymbols</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>Set&lt;Node&gt;</c> is iterated while entries are deleted, which JS allows; the port snapshots
    /// the set into a list first because <see cref="HashSet{T}"/> forbids modifying during enumeration. The three-set
    /// identity is unchanged (a <c>Node</c> does not override <c>Equals</c>/<c>GetHashCode</c>).
    /// </remarks>
    internal HashSet<Node>? GetCommonDeclarationsOfSymbols(IEnumerable<ISymbol> symbols)
    {
        HashSet<Node>? commonDeclarations = null;
        foreach (var symbol in symbols)
        {
            if (symbol.Declarations is null)
            {
                return null;
            }

            if (commonDeclarations is null)
            {
                commonDeclarations = [];
                foreach (var declaration in symbol.Declarations)
                {
                    commonDeclarations.Add((Node)declaration);
                }

                continue;
            }

            foreach (var declaration in new List<Node>(commonDeclarations))
            {
                if (!symbol.Declarations.Contains((IDeclaration)declaration))
                {
                    commonDeclarations.Remove(declaration);
                }
            }

            if (commonDeclarations.Count == 0)
            {
                return null;
            }
        }

        return commonDeclarations;
    }

    // ---------------------------------------------------------------------------------------------
    // The array symbols and symbol tables (checker.ts:15967-16089)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:15967-15972 — <c>isArrayOrTupleSymbol</c>.</summary>
    internal bool IsArrayOrTupleSymbol(ISymbol? symbol)
    {
        if (symbol is null || _globalArrayType.Symbol is null || _globalReadonlyArrayType.Symbol is null)
        {
            return false;
        }

        return GetSymbolIfSameReference(symbol, _globalArrayType.Symbol) is not null
            || GetSymbolIfSameReference(symbol, _globalReadonlyArrayType.Symbol) is not null;
    }

    /// <summary>ported from: src/compiler/checker.ts:15974-15979 — <c>isReadonlyArraySymbol</c>.</summary>
    internal bool IsReadonlyArraySymbol(ISymbol? symbol)
    {
        if (symbol is null || _globalReadonlyArrayType.Symbol is null)
        {
            return false;
        }

        return GetSymbolIfSameReference(symbol, _globalReadonlyArrayType.Symbol) is not null;
    }

    /// <summary>ported from: src/compiler/checker.ts:16068-16076 — <c>symbolsToArray</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>symbols.forEach((symbol, id) =&gt; …)</c> passes the map key under the name <c>id</c> — it is
    /// the escaped name, which is what <see cref="IsReservedMemberName"/> filters on; the port's
    /// <see cref="SymbolTable.ForEach"/> hands the same two arguments over.
    /// </remarks>
    internal List<ISymbol> SymbolsToArray(SymbolTable symbols)
    {
        var result = new List<ISymbol>();
        symbols.ForEach((symbol, id) =>
        {
            if (!IsReservedMemberName(id))
            {
                result.Add(symbol);
            }
        });
        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:16087-16089 — <c>hasEffectiveQuestionToken</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the declaration union <c>ParameterDeclaration | JSDocParameterTag | JSDocPropertyTag</c>,
    /// which the port states as <see cref="Node"/>; each of the three helpers it calls takes that node.
    /// </remarks>
    internal bool HasEffectiveQuestionToken(Node node)
        => Utilities.HasQuestionToken(node)
            || Utilities.IsOptionalJSDocPropertyLikeTag(node)
            || NodeTests.IsParameter(node) && Utilities.IsJSDocOptionalParameter(node);

    // ---------------------------------------------------------------------------------------------
    // The signatures of a declaration (checker.ts:16177-16364)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:16177-16276 — <c>getSignatureFromDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>declaration</c> is <c>SignatureDeclaration | JSDocSignature</c>, whose <c>parameters</c>
    /// member is a different array type per arm; the port takes <see cref="Node"/> and reads the list through the
    /// <c>ParameterAt</c> local function, which selects the JSDoc arm with the same test the reference's own JSDoc
    /// helpers use. The <c>parameters</c> slot of a <c>JSDocSignature</c> holds <c>JSDocParameterTag</c> nodes at run
    /// time, which is the union the reference's loop body already handles.
    /// <para>
    /// PORT-NOTE: three assignments return <c>void</c> in C# where TS's are expressions used as values (the
    /// <c>parameters.push</c>/<c>parameterCount</c> pair is read back from <c>parameters.Count</c>), so the
    /// <c>minArgumentCount</c> update is the same number the reference computes.
    /// </para>
    /// </remarks>
    internal Signature GetSignatureFromDeclaration(Node declaration)
    {
        var links = GetNodeLinks(declaration);
        if (links.ResolvedSignature is null)
        {
            var parameters = new List<ISymbol>();
            var flags = SignatureFlags.None;
            var minArgumentCount = 0;
            ISymbol? thisParameter = null;
            var declarationIsJSDocSignature = NodeTests.IsJSDocSignature(declaration);
            JSDocThisTag? thisTag = Utilities.IsInJSFile(declaration) ? Utilities.GetJSDocThisTag(declaration) : null;
            var hasThisParameter = false;
            var iife = Utilities.GetImmediatelyInvokedFunctionExpression(declaration);
            var isJSConstructSignature = Utilities.IsJSDocConstructSignature(declaration);
            var parameterCount = declarationIsJSDocSignature
                ? ((JSDocSignature)declaration).Parameters?.Count ?? 0
                : ((ISignatureDeclarationBase)declaration).Parameters?.Count ?? 0;
            var isUntypedSignatureInJSFile = iife is null
                && Utilities.IsInJSFile(declaration)
                && Utilities.IsValueSignatureDeclaration(declaration)
                && !Utilities.HasJSDocParameterTags(declaration)
                && !AnyParameterHasJSDocType()
                && Utilities.GetJSDocType(declaration) is null
                && GetContextualSignatureForFunctionLikeDeclaration(declaration) is null;
            if (isUntypedSignatureInJSFile)
            {
                flags |= SignatureFlags.IsUntypedSignatureInJSFile;
            }

            // If this is a JSDoc construct signature, then skip the first parameter in the parameter list. The first
            // parameter represents the return type of the construct signature.
            for (var i = isJSConstructSignature ? 1 : 0; i < parameterCount; i++)
            {
                var param = ParameterAt(i);
                if (Utilities.IsInJSFile(param) && Utilities.IsJSDocThisTag(param))
                {
                    thisTag = (JSDocThisTag)param;
                    continue;
                }

                var paramSymbol = ((IDeclaration)param).Symbol;
                var type = NodeTests.IsJSDocParameterTag(param)
                    ? ((JSDocParameterTag)param).TypeExpression?.Type
                    : ((ParameterDeclaration)param).Type;
                // Include parameter symbol instead of property symbol in the signature
                if (paramSymbol is not null && (paramSymbol.Flags & SymbolFlags.Property) != 0 && !Utilities.IsBindingPattern(NameOf(param)))
                {
                    var resolvedSymbol = ResolveName(param, paramSymbol.EscapedName!, SymbolFlags.Value, /*nameNotFoundMessage*/ null, /*isUse*/ false, /*excludeGlobals*/ false);
                    paramSymbol = resolvedSymbol!;
                }

                if (i == 0 && paramSymbol!.EscapedName == InternalSymbolName.This.Value)
                {
                    hasThisParameter = true;
                    thisParameter = ((IDeclaration)param).Symbol;
                }
                else
                {
                    parameters.Add(paramSymbol!);
                }

                if (type is not null && type.Kind == SyntaxKind.LiteralType)
                {
                    flags |= SignatureFlags.HasLiteralTypes;
                }

                // Record a new minimum argument count if this is not an optional parameter
                var isOptionalParameter = HasEffectiveQuestionToken(param)
                    || NodeTests.IsParameter(param) && ((ParameterDeclaration)param).Initializer is not null
                    || Utilities.IsRestParameter(param)
                    || iife is not null && parameters.Count > Core.Length(iife.Arguments) && type is null;
                if (!isOptionalParameter)
                {
                    minArgumentCount = parameters.Count;
                }
            }

            // If only one accessor includes a this-type annotation, the other behaves as if it had the same type annotation
            if ((declaration.Kind == SyntaxKind.GetAccessor || declaration.Kind == SyntaxKind.SetAccessor)
                && HasBindableName((IDeclaration)declaration)
                && (!hasThisParameter || thisParameter is null))
            {
                var otherKind = declaration.Kind == SyntaxKind.GetAccessor ? SyntaxKind.SetAccessor : SyntaxKind.GetAccessor;
                var other = Utilities.GetDeclarationOfKind<Node>(GetSymbolOfDeclaration((IDeclaration)declaration), otherKind);
                if (other is not null)
                {
                    thisParameter = GetAnnotatedAccessorThisParameter(other);
                }
            }

            if (thisTag is not null && thisTag.TypeExpression is not null)
            {
                thisParameter = CreateSymbolWithType(
                    CreateSymbol(SymbolFlags.FunctionScopedVariable, InternalSymbolName.This.Value),
                    GetTypeFromTypeNode(thisTag.TypeExpression));
            }

            var hostDeclaration = declarationIsJSDocSignature ? Utilities.GetEffectiveJSDocHost(declaration) : declaration;
            var classType = hostDeclaration is not null && NodeTests.IsConstructorDeclaration(hostDeclaration)
                ? GetDeclaredTypeOfClassOrInterface(GetMergedSymbol(((ClassDeclaration)hostDeclaration.Parent!).Symbol!)!)
                : null;
            var typeParameters = classType is not null ? classType.LocalTypeParameters : GetTypeParametersFromDeclaration(declaration);
            if (Utilities.HasRestParameter(declaration) || Utilities.IsInJSFile(declaration) && MaybeAddJsSyntheticRestParameter(declaration, parameters))
            {
                flags |= SignatureFlags.HasRestParameter;
            }

            if (NodeTests.IsConstructorTypeNode(declaration) && Utilities.HasSyntacticModifier(declaration, ModifierFlags.Abstract)
                || NodeTests.IsConstructorDeclaration(declaration) && Utilities.HasSyntacticModifier(declaration.Parent!, ModifierFlags.Abstract))
            {
                flags |= SignatureFlags.Abstract;
            }

            links.ResolvedSignature = CreateSignature(
                declaration,
                typeParameters,
                thisParameter,
                parameters,
                /*resolvedReturnType*/ null,
                /*resolvedTypePredicate*/ null,
                minArgumentCount,
                flags);
        }

        return (Signature)links.ResolvedSignature!;

        Node ParameterAt(int index) => declarationIsJSDocSignature
            ? ((JSDocSignature)declaration).Parameters![index]
            : ((ISignatureDeclarationBase)declaration).Parameters![index];

        Node? NameOf(Node param) => NodeTests.IsParameter(param) ? ((ParameterDeclaration)param).Name : ((JSDocPropertyLikeTag)param).Name;

        bool AnyParameterHasJSDocType()
        {
            for (var i = 0; i < parameterCount; i++)
            {
                if (Utilities.GetJSDocType(ParameterAt(i)) is not null)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:16279-16307 — <c>maybeAddJsSyntheticRestParameter</c>.</summary>
    /// <remarks>
    /// A JS function gets a synthetic rest parameter if it references `arguments` AND: (1) it has no parameters but at
    /// least one `@param` with a type that starts with `...`, OR (2) it has at least one parameter, and the last
    /// parameter has a matching `@param` with a type that starts with `...`.
    /// <para>
    /// PORT-NOTE: TS's <c>getJSDocTags(declaration).filter(isJSDocParameterTag)</c> narrows <c>JSDocTag[]</c> to
    /// <c>JSDocParameterTag[]</c> with its type guard; C# has no such narrowing, so the port builds the same list with
    /// an explicit test.
    /// </para>
    /// </remarks>
    internal bool MaybeAddJsSyntheticRestParameter(Node declaration, List<ISymbol> parameters)
    {
        if (NodeTests.IsJSDocSignature(declaration) || !ContainsArgumentsReference(declaration))
        {
            return false;
        }

        var declarationParameters = ((ISignatureDeclarationBase)declaration).Parameters!;
        var lastParam = Core.LastOrUndefined(declarationParameters);
        List<JSDocParameterTag> lastParamTags;
        if (lastParam is not null)
        {
            lastParamTags = Utilities.GetJSDocParameterTags(lastParam);
        }
        else
        {
            lastParamTags = [];
            foreach (var tag in Utilities.GetJSDocTags(declaration))
            {
                if (NodeTests.IsJSDocParameterTag(tag))
                {
                    lastParamTags.Add((JSDocParameterTag)tag);
                }
            }
        }

        var lastParamVariadicType = Core.FirstDefined(lastParamTags, (JSDocParameterTag p, int _) =>
            p.TypeExpression?.Type is { } variadicTypeNode && NodeTests.IsJSDocVariadicType((Node)variadicTypeNode)
                ? (JSDocVariadicType)variadicTypeNode
                : null);

        var syntheticArgsSymbol = CreateSymbol(SymbolFlags.Variable, "args", CheckFlags.RestParameter);
        var syntheticArgsLinks = GetSymbolLinks(syntheticArgsSymbol);
        if (lastParamVariadicType is not null)
        {
            // Parameter has effective annotation, lock in type
            syntheticArgsLinks.Type = CreateArrayType(GetTypeFromTypeNode(lastParamVariadicType.Type!));
        }
        else
        {
            // Parameter has no annotation. By using a `DeferredType` symbol, we allow the type of this rest arg to be
            // overridden by contextual type assignment so long as its type hasn't been cached by `getTypeOfSymbol` yet.
            syntheticArgsLinks.CheckFlags |= CheckFlags.DeferredType;
            syntheticArgsLinks.DeferralParent = _neverType;
            syntheticArgsLinks.DeferralConstituents = [_anyArrayType];
            syntheticArgsLinks.DeferralWriteConstituents = [_anyArrayType];
        }

        if (lastParamVariadicType is not null)
        {
            // Replace the last parameter with a rest parameter.
            parameters.RemoveAt(parameters.Count - 1);
        }

        parameters.Add(syntheticArgsSymbol);
        return true;
    }

    /// <summary>ported from: src/compiler/checker.ts:16309-16314 — <c>getSignatureOfTypeTag</c>.</summary>
    /// <remarks>PORT-NOTE: the signature is expected to be attached to a function declaration or expression.</remarks>
    internal ISignature? GetSignatureOfTypeTag(Node node)
    {
        if (!(Utilities.IsInJSFile(node) && Utilities.IsFunctionLikeDeclaration(node)))
        {
            return null;
        }

        var typeTag = Utilities.GetJSDocTypeTag(node);
        return typeTag?.TypeExpression is not null
            ? GetSingleCallSignature(GetTypeFromTypeNode(typeTag.TypeExpression))
            : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:16316-16321 — <c>getParameterTypeOfTypeTag</c>.</summary>
    internal IType? GetParameterTypeOfTypeTag(Node func, Node parameter)
    {
        var signature = GetSignatureOfTypeTag(func);
        if (signature is null)
        {
            return null;
        }

        var pos = ((ISignatureDeclarationBase)func).Parameters!.IndexOf((ParameterDeclaration)parameter);
        return ((ParameterDeclaration)parameter).DotDotDotToken is not null
            ? GetRestTypeAtPosition(signature, pos)
            : GetTypeAtPosition(signature, pos);
    }

    /// <summary>ported from: src/compiler/checker.ts:16323-16326 — <c>getReturnTypeOfTypeTag</c>.</summary>
    internal IType? GetReturnTypeOfTypeTag(Node node)
    {
        var signature = GetSignatureOfTypeTag(node);
        return signature is not null ? GetReturnTypeOfSignature(signature) : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:16328-16364 — <c>containsArgumentsReference</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference guards its nested <c>traverse</c> with <c>if (!node) return false</c>; the port's
    /// parameter is a non-null <see cref="Node"/>, so the arm is not reachable and is not stated. The child walk is the
    /// generated <see cref="ForEachChildTable"/> visitor, whose <c>bool?</c> answer is the reference's truthy-or-undefined
    /// early exit.
    /// </remarks>
    internal bool ContainsArgumentsReference(Node declaration)
    {
        var links = GetNodeLinks(declaration);
        if (links.ContainsArgumentsReference is null)
        {
            if ((links.Flags & NodeCheckFlags.CaptureArguments) != 0)
            {
                links.ContainsArgumentsReference = true;
            }
            else
            {
                links.ContainsArgumentsReference = Traverse(((IFunctionLikeDeclarationBase)declaration).Body!);
            }
        }

        return links.ContainsArgumentsReference == true;

        bool Traverse(Node node)
        {
            switch (node.Kind)
            {
                case SyntaxKind.Identifier:
                    return ((Identifier)node).EscapedText == _argumentsSymbol!.EscapedName
                        && ReferenceEquals(GetReferencedValueSymbol((Identifier)node), _argumentsSymbol);

                case SyntaxKind.PropertyDeclaration:
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                    return ((INamedDeclaration)node).Name is { } computedName
                        && computedName.Kind == SyntaxKind.ComputedPropertyName
                        && Traverse(computedName);

                case SyntaxKind.PropertyAccessExpression:
                case SyntaxKind.ElementAccessExpression:
                    return Traverse(node.Kind == SyntaxKind.PropertyAccessExpression
                        ? ((PropertyAccessExpression)node).Expression!
                        : ((ElementAccessExpression)node).Expression!);

                case SyntaxKind.PropertyAssignment:
                    return Traverse(((PropertyAssignment)node).Initializer!);

                default:
                    return !Utilities.NodeStartsNewLexicalEnvironment(node)
                        && !IsPartOfTypeNode(node)
                        && ForEachChildTable.ForEachChild<bool>(node, child => Traverse(child)) == true;
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The module type of a literal, type predicates and signature instantiation (checker.ts:16406-16590)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:16406-16416 — <c>resolveExternalModuleTypeByLiteral</c>.</summary>
    internal IType ResolveExternalModuleTypeByLiteral(StringLiteral name)
    {
        var moduleSym = ResolveExternalModuleName(name, name);
        if (moduleSym is not null)
        {
            var resolvedModuleSymbol = ResolveExternalModuleSymbol(moduleSym, /*dontResolveAlias*/ false);
            if (resolvedModuleSymbol is not null)
            {
                return GetTypeOfSymbol(resolvedModuleSymbol);
            }
        }

        return _anyType;
    }

    /// <summary>ported from: src/compiler/checker.ts:16461-16467 — <c>createTypePredicateFromTypePredicateNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>parameterName</c> is <c>Identifier | ThisTypeNode</c>; the port's slot is <see cref="Node"/>
    /// and the two arms cast to the face the <c>ThisType</c> test selected.
    /// </remarks>
    internal ITypePredicateBase CreateTypePredicateFromTypePredicateNode(TypePredicateNode node, ISignature signature)
    {
        var parameterName = node.ParameterName!;
        var type = node.Type is not null ? GetTypeFromTypeNode(node.Type) : null;
        return NodeTests.IsThisTypeNode(parameterName)
            ? CreateTypePredicate(
                node.AssertsModifier is not null ? TypePredicateKind.AssertsThis : TypePredicateKind.This,
                /*parameterName*/ null,
                /*parameterIndex*/ null,
                type)
            : CreateTypePredicate(
                node.AssertsModifier is not null ? TypePredicateKind.AssertsIdentifier : TypePredicateKind.Identifier,
                ((Identifier)parameterName).EscapedText,
                Core.FindIndex(signature.Parameters, (ISymbol p, int _) => p.EscapedName == ((Identifier)parameterName).EscapedText),
                type);
    }

    /// <summary>ported from: src/compiler/checker.ts:16588-16590 — <c>createSignatureInstantiation</c>.</summary>
    internal ISignature CreateSignatureInstantiation(ISignature signature, List<IType>? typeArguments)
        => InstantiateSignature(signature, CreateSignatureTypeMapper(signature, typeArguments), /*eraseTypeParameters*/ true);

    // ---------------------------------------------------------------------------------------------
    // Index symbols and their index infos (checker.ts:16675-16759)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:16675-16677 — <c>getIndexSymbol</c>.</summary>
    internal ISymbol? GetIndexSymbol(ISymbol symbol)
        => symbol.Members is not null ? GetIndexSymbolFromSymbolTable(GetMembersOfSymbol(symbol)) : null;

    /// <summary>ported from: src/compiler/checker.ts:16679-16681 — <c>getIndexSymbolFromSymbolTable</c>.</summary>
    internal ISymbol? GetIndexSymbolFromSymbolTable(SymbolTable symbolTable)
        => symbolTable.GetValueOrDefault(InternalSymbolName.Index.Value);

    /// <summary>ported from: src/compiler/checker.ts:16687-16690 — <c>getIndexInfosOfSymbol</c>.</summary>
    internal List<IIndexInfo> GetIndexInfosOfSymbol(ISymbol symbol)
    {
        var indexSymbol = GetIndexSymbol(symbol);
        return indexSymbol is not null
            ? GetIndexInfosOfIndexSymbol(indexSymbol, Core.ArrayFrom(GetMembersOfSymbol(symbol).Values))
            : [];
    }

    /// <summary>ported from: src/compiler/checker.ts:16693-16753 — <c>getIndexInfosOfIndexSymbol</c>.</summary>
    /// <remarks>
    /// Note the intentional similarities to index signature building in <c>checkObjectLiteral</c> for parity.
    /// <para>
    /// PORT-NOTE: TS's second parameter defaults to
    /// <c>indexSymbol.parent ? arrayFrom(getMembersOfSymbol(indexSymbol.parent).values()) : undefined</c>; a C# default
    /// argument cannot call a member, so the port defaults to <c>null</c> and computes the same expression in the body
    /// — an explicit <c>undefined</c> and an omitted argument are the same value in the reference too, so the two are
    /// indistinguishable.
    /// </para>
    /// <para>
    /// PORT-NOTE: the aggregated <c>allPropertySymbols</c> is materialized as a <see cref="List{T}"/> because the
    /// <c>getObjectLiteralIndexInfo</c> delegate it is handed to (checker.ts:33495) takes the reference's
    /// <c>Symbol[]</c> face; the reference's own <c>concatenate</c> builds a fresh array in every arm that reaches it
    /// with both operands non-empty, so the copy is the same allocation.
    /// </para>
    /// </remarks>
    internal List<IIndexInfo> GetIndexInfosOfIndexSymbol(ISymbol indexSymbol, IList<ISymbol>? siblingSymbols = null)
    {
        siblingSymbols ??= indexSymbol.Parent is not null ? Core.ArrayFrom(GetMembersOfSymbol(indexSymbol.Parent).Values) : null;
        if (indexSymbol.Declarations is not null)
        {
            var indexInfos = new List<IIndexInfo>();
            var hasComputedNumberProperty = false;
            var readonlyComputedNumberProperty = true;
            var hasComputedSymbolProperty = false;
            var readonlyComputedSymbolProperty = true;
            var hasComputedStringProperty = false;
            var readonlyComputedStringProperty = true;
            var computedPropertySymbols = new List<ISymbol>();
            foreach (var declaration in indexSymbol.Declarations)
            {
                if (NodeTests.IsIndexSignatureDeclaration((Node)declaration))
                {
                    var declarationParameters = ((ISignatureDeclarationBase)declaration).Parameters!;
                    if (declarationParameters.Count == 1)
                    {
                        var parameter = declarationParameters[0];
                        if (parameter.Type is not null)
                        {
                            ForEachType(GetTypeFromTypeNode(parameter.Type), keyType =>
                            {
                                if (IsValidIndexKeyType(keyType) && FindIndexInfo(indexInfos, keyType) is null)
                                {
                                    indexInfos.Add(CreateIndexInfo(
                                        keyType,
                                        ((ISignatureDeclarationBase)declaration).Type is not null
                                            ? GetTypeFromTypeNode(((ISignatureDeclarationBase)declaration).Type)
                                            : _anyType,
                                        Utilities.HasEffectiveModifier((Node)declaration, ModifierFlags.Readonly),
                                        (IndexSignatureDeclaration)declaration));
                                }
                            });
                        }
                    }
                }
                else if (HasLateBindableIndexSignature(declaration))
                {
                    var declName = NodeTests.IsBinaryExpression((Node)declaration)
                        ? ((BinaryExpression)declaration).Left!
                        : ((INamedDeclaration)declaration).Name!;
                    var keyType = NodeTests.IsElementAccessExpression(declName)
                        ? CheckExpressionCached(((ElementAccessExpression)declName).ArgumentExpression!)
                        : CheckComputedPropertyName(declName);
                    if (FindIndexInfo(indexInfos, keyType) is not null)
                    {
                        continue; // Explicit index for key type takes priority
                    }

                    if (IsTypeAssignableTo(keyType, _stringNumberSymbolType))
                    {
                        if (IsTypeAssignableTo(keyType, _numberType))
                        {
                            hasComputedNumberProperty = true;
                            if (!Utilities.HasEffectiveReadonlyModifier((Node)declaration))
                            {
                                readonlyComputedNumberProperty = false;
                            }
                        }
                        else if (IsTypeAssignableTo(keyType, _esSymbolType))
                        {
                            hasComputedSymbolProperty = true;
                            if (!Utilities.HasEffectiveReadonlyModifier((Node)declaration))
                            {
                                readonlyComputedSymbolProperty = false;
                            }
                        }
                        else
                        {
                            hasComputedStringProperty = true;
                            if (!Utilities.HasEffectiveReadonlyModifier((Node)declaration))
                            {
                                readonlyComputedStringProperty = false;
                            }
                        }

                        computedPropertySymbols.Add(declaration.Symbol!);
                    }
                }
            }

            var allPropertySymbols = new List<ISymbol>(Core.Concatenate(computedPropertySymbols, Core.Filter(siblingSymbols, s => !ReferenceEquals(s, indexSymbol)))!);
            // aggregate similar index infos implied to be the same key to the same combined index info
            if (hasComputedStringProperty && FindIndexInfo(indexInfos, _stringType) is null) indexInfos.Add(GetObjectLiteralIndexInfo(readonlyComputedStringProperty, 0, allPropertySymbols, _stringType));
            if (hasComputedNumberProperty && FindIndexInfo(indexInfos, _numberType) is null) indexInfos.Add(GetObjectLiteralIndexInfo(readonlyComputedNumberProperty, 0, allPropertySymbols, _numberType));
            if (hasComputedSymbolProperty && FindIndexInfo(indexInfos, _esSymbolType) is null) indexInfos.Add(GetObjectLiteralIndexInfo(readonlyComputedSymbolProperty, 0, allPropertySymbols, _esSymbolType));
            return indexInfos;
        }

        return [];
    }

    /// <summary>ported from: src/compiler/checker.ts:16755-16759 — <c>isValidIndexKeyType</c>.</summary>
    internal bool IsValidIndexKeyType(IType type)
        => (type.Flags & (TypeFlags.String | TypeFlags.Number | TypeFlags.ESSymbol)) != 0
            || IsPatternLiteralType(type)
            || (type.Flags & TypeFlags.Intersection) != 0 && !IsGenericType(type) && Core.Some(((IIntersectionType)type).Types, IsValidIndexKeyType);
}
