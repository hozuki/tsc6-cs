namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:9900-11167 — the declaration serializers nested in `symbolTableToDeclarationStatements`
/// <summary>
/// The node builder's declaration serializers: the half of <c>createNodeBuilder</c> that turns symbols into
/// <em>statements</em> — synthesizing <c>type</c>/<c>interface</c>/<c>class</c>/<c>namespace</c>/<c>enum</c>/
/// <c>import</c>/<c>export</c> declarations out of a symbol table, and the name/hash-private helpers they share.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (the frame, and why every member takes it explicitly): TS nests all of these inside
/// <c>symbolTableToDeclarationStatements</c> (checker.ts:9346, NBDecl's slice), so they close over that call's frame —
/// <c>context</c>, <c>results</c>, <c>addingDeclare</c>, <c>visitedSymbols</c>, <c>deferredPrivatesStack</c> and the
/// <c>enclosingDeclaration</c> snapshot. C# cannot share a closure across partial files, so each member here is an
/// <c>internal</c> member of <see cref="Checker"/> and takes exactly the frame state it reads as explicit parameters
/// (contract §7.5's last paragraph; §6's "no unused parameter" is why each parameter list is minimal). The order is
/// the reference's own parameters first, then <c>context</c>, <c>results</c>, <c>addingDeclare</c>,
/// <c>visitedSymbols</c>, <c>deferredPrivatesStack</c>, <c>enclosingDeclaration</c> — that is the order NBDecl's
/// <c>VisitSymbolTable</c>/<c>IncludePrivateSymbol</c>/<c>AddResult</c> use, so the thread is consistent across the
/// boundary.
/// </para>
/// <para>
/// PORT-NOTE (the enclosing declaration): TS's frame-local <c>enclosingDeclaration</c> (checker.ts:9356) is the value
/// <c>context.enclosingDeclaration</c> had on entry; the port reads <c>context.EnclosingDeclaration</c> instead
/// (checker.ts:9615's <c>serializeSymbol</c>, 10687's <c>getSourceFileOfNode(enclosingDeclaration)</c>, 11073's
/// <c>isSymbolAccessibleByFlags</c>). The two differ only while <c>SerializeAsNamespaceDeclaration</c> has swapped in
/// its fake-namespace sub-context (checker.ts:10218-10223), and there <c>Utilities.GetSourceFileOfNode</c> walks the
/// fake namespace's parent back to the same file, so the two spellings agree in every case that arises.
/// </para>
/// <para>
/// PORT-NOTE (<c>symbolName</c>): TS's frame-local <c>symbolName(symbol)</c> — <c>unescapeLeadingUnderscores</c> of the
/// escaped name — is <c>Utilities.SymbolName</c> (utilitiesPublic.ts:870) in the port, and the three call sites in this
/// range (10302, 10695, 10777, 10860) call it there.
/// </para>
/// <para>
/// PORT-NOTE (the two property-symbol serializers): TS's <c>serializePropertySymbolForClass</c>/
/// <c>serializePropertySymbolForInterfaceWorker</c> (checker.ts:9347-9348) are values of <c>makeSerializePropertySymbol</c>
/// created in NBDecl's frame, but every reader is in this file, so the port builds them here from
/// <see cref="MakeSerializePropertySymbol"/> — the delegate it returns is that value. TS's three overloads of
/// <c>makeSerializePropertySymbol</c> collapse into one method: the overloads differ only in the <c>useAccessors</c>
/// literal and in <c>createProperty</c>, and the reference has exactly two <c>createProperty</c> instantiations
/// (<c>createPropertyDeclaration</c> and <c>createPropertySignature</c>), which the <c>isClass</c> parameter selects.
/// </para>
/// <para>
/// PORT-NOTE (<c>context.out.truncated</c>): TS writes through the non-null <c>context.out</c>; the port's slot is
/// <c>IWriterContextOut?</c>, so the truncation paths use <c>!</c> like the rest of the port does for reference slots.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    /// <summary>
    /// The value <c>makeSerializePropertySymbol</c> returns in TS: one property-symbol serialization, which is a
    /// property declaration/signature, an accessor pair, or a list of method overloads.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS's return type is the union <c>T | AccessorDeclaration | (T | AccessorDeclaration)[]</c>; the port
    /// returns the list form always (an empty list for TS's <c>[]</c>), which is the shape both call sites of
    /// <c>serializePropertySymbolsForClassOrInterface</c> already reduce to with their <c>isArray</c> test.
    /// </remarks>
    internal delegate IList<Node> SerializePropertySymbolCallback(ISymbol p, bool isStatic, IType? baseType);

    /// <summary>
    /// The port's projection of TS's <c>readonly Modifier[]</c> onto the <c>List&lt;Node&gt;?</c> the generated factories take.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: <c>CreateModifiersFromModifierFlags</c> answers <c>List&lt;ModifierToken&gt;?</c> while the factories that
    /// place modifiers on a declaration take <c>List&lt;Node&gt;?</c>; generic invariance makes those two incompatible, so
    /// the port projects element-wise, exactly as the landed node-builder members do
    /// (<c>NodeBuilder.Signatures.cs:142</c>). It is declared here because this slice needs it at five call sites
    /// (the enum declaration, both constructor declarations and the two accessors) and no other slice declares it.
    /// </remarks>
    internal static List<Node>? ToNodeModifiers(List<ModifierToken>? modifiers)
        => modifiers is null ? null : Core.Map(modifiers, (modifier, _) => (Node)modifier);

    // ---------------------------------------------------------------------------------------------
    // serializeTypeAlias / serializeInterface / serializePropertySymbolsForClassOrInterface (checker.ts:9900-9989)
    // ---------------------------------------------------------------------------------------------

    /// PORT-NOTE: the trailing frame values (<c>visitedSymbols</c>/<c>deferredPrivatesStack</c>/<c>enclosingDeclaration</c>) are unread here — TS's closure reaches them only through the members this one calls, and NBDecl passes one uniform nine-argument shape at all of its call sites; see the file remarks.
    /// <summary>ported from: src/compiler/checker.ts:9900-9926 — <c>serializeTypeAlias</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>syntacticNodeBuilder.tryReuseExistingTypeNode</c> (checker.ts:9912) is the guarded
    /// <see cref="SyntacticNodeBuilder"/> property (the module it comes from, <c>expressionToTypeNode.ts</c>, is not
    /// ported — a loud, named blocker). PORT-NOTE: <c>jsdocAliasDecl.comment || jsdocAliasDecl.parent.comment</c> is
    /// the port's <c>JSDocTag.Comment ?? ((JSDoc)Parent).Comment</c> — the reference's <c>JSDocTypeAlias</c> union has
    /// no C# type, so the declaration is read through <see cref="JSDocTag"/>, which all three of its constituents share.
    /// </remarks>
    internal void SerializeTypeAlias(
        ISymbol symbol, string symbolName, ModifierFlags modifierFlags,
        NodeBuilderContext context,
        List<IStatement> results,
        bool addingDeclare,
        HashSet<int> visitedSymbols,
        List<Dictionary<int, ISymbol>> deferredPrivatesStack,
        Node? enclosingDeclaration)
    {
        var aliasType = GetDeclaredTypeOfTypeAlias(symbol);
        var typeParams = GetSymbolLinks(symbol).TypeParameters;
        var typeParamDecls = Core.Map(typeParams, (p, _) => TypeParameterToDeclaration(p, context));
        var jsdocAliasDecl = Core.Find(symbol.Declarations, (d, _) => Utilities.IsJSDocTypeAlias((Node)d));
        var comment = jsdocAliasDecl is not null
            ? ((JSDocTag)jsdocAliasDecl).Comment ?? ((JSDoc?)((Node)jsdocAliasDecl).Parent)?.Comment
            : null;
        var commentText = Utilities.GetTextOfJSDocComment(comment);
        var restoreFlags = SaveRestoreFlags(context);
        context.Flags |= NodeBuilderFlags.InTypeAlias;
        var oldEnclosingDecl = context.EnclosingDeclaration;
        context.EnclosingDeclaration = (Node?)jsdocAliasDecl;
        var jsdocTypeExpression = jsdocAliasDecl switch
        {
            JSDocTypedefTag typedefTag => typedefTag.TypeExpression,
            JSDocCallbackTag callbackTag => callbackTag.TypeExpression,
            JSDocEnumTag enumTag => enumTag.TypeExpression,
            _ => null,
        };
        var reusedTypeNode = jsdocTypeExpression is not null && NodeTests.IsJSDocTypeExpression(jsdocTypeExpression)
            ? SyntacticNodeBuilder.TryReuseExistingTypeNode!(context, ((JSDocTypeExpression)jsdocTypeExpression).Type!)
            : null;
        var typeNode = reusedTypeNode ?? TypeToTypeNodeHelper(aliasType, context);
        var internalSymbolName = GetInternalSymbolName(symbol, symbolName, context);
        context.ApproximateLength += 8 + (commentText?.Length ?? 0) + internalSymbolName.Length; // `/* comment */ type name = ...`
        List<ISynthesizedComment> comments = string.IsNullOrEmpty(commentText)
            ? []
            : [
                new SynthesizedComment
                {
                    Kind = SyntaxKind.MultiLineCommentTrivia,
                    Text = "*\n * " + commentText!.Replace("\n", "\n * ") + "\n ",
                    Pos = -1,
                    End = -1,
                    HasTrailingNewLine = true,
                },
            ];
        AddResult(
            context,
            results,
            addingDeclare,
            EmitNodes.SetSyntheticLeadingComments(
                _factory.CreateTypeAliasDeclaration(/*modifiers*/ null, internalSymbolName, typeParamDecls, typeNode),
                comments),
            modifierFlags);
        restoreFlags();
        context.EnclosingDeclaration = oldEnclosingDecl;
    }

    /// PORT-NOTE: the trailing frame values are unread here (see <see cref="SerializeTypeAlias"/>'s note).
    /// <summary>ported from: src/compiler/checker.ts:9927-9952 — <c>serializeInterface</c>.</summary>
    internal void SerializeInterface(
        ISymbol symbol, string symbolName, ModifierFlags modifierFlags,
        NodeBuilderContext context,
        List<IStatement> results,
        bool addingDeclare,
        HashSet<int> visitedSymbols,
        List<Dictionary<int, ISymbol>> deferredPrivatesStack,
        Node? enclosingDeclaration)
    {
        var internalSymbolName = GetInternalSymbolName(symbol, symbolName, context);
        context.ApproximateLength += 14 + internalSymbolName.Length; // `interface name { }`
        var interfaceType = GetDeclaredTypeOfClassOrInterface(symbol);
        var localParams = GetLocalTypeParametersOfClassOrInterfaceOrTypeAlias(symbol);
        var typeParamDecls = Core.Map(localParams, (p, _) => TypeParameterToDeclaration(p, context));
        var baseTypes = GetBaseTypes(interfaceType);
        var baseType = Core.Length(baseTypes) != 0 ? GetIntersectionType(baseTypes) : null;
        var members = SerializePropertySymbolsForClassOrInterface(GetPropertiesOfType(interfaceType), /*isClass*/ false, baseType, context);
        var callSignatures = SerializeSignatures(SignatureKind.Call, interfaceType, baseType, SyntaxKind.CallSignature, context);
        var constructSignatures = SerializeSignatures(SignatureKind.Construct, interfaceType, baseType, SyntaxKind.ConstructSignature, context);
        var indexSignatures = SerializeIndexSignatures(interfaceType, baseType, context);

        List<HeritageClause>? heritageClauses = null;
        if (Core.Length(baseTypes) != 0)
        {
            heritageClauses = [ _factory.CreateHeritageClause(SyntaxKind.ExtendsKeyword, Core.MapDefined(baseTypes, (b, _) => TrySerializeAsTypeReference(b, SymbolFlags.Value, context))) ];
        }
        var allMembers = new List<ITypeElement>(indexSignatures);
        foreach (var signature in constructSignatures)
        {
            allMembers.Add((ITypeElement)signature);
        }
        foreach (var signature in callSignatures)
        {
            allMembers.Add((ITypeElement)signature);
        }
        allMembers.AddRange(members);
        AddResult(
            context,
            results,
            addingDeclare,
            _factory.CreateInterfaceDeclaration(
                /*modifiers*/ null,
                internalSymbolName,
                typeParamDecls,
                heritageClauses,
                allMembers),
            modifierFlags);
    }

    /// <summary>ported from: src/compiler/checker.ts:9953-9954 — the <c>isClass: false</c> overload of <c>serializePropertySymbolsForClassOrInterface</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's single implementation takes <c>isClass</c> and, when it is false, calls
    /// <c>serializePropertySymbolForInterface</c> and never touches its <c>isStatic</c> argument; the port states that
    /// specialization as the overload the reference itself declares for this call shape.
    /// </remarks>
    internal List<ITypeElement> SerializePropertySymbolsForClassOrInterface(IList<ISymbol> props, bool isClass, IType? baseType, NodeBuilderContext context)
    {
        var elements = new List<ITypeElement>();
        var i = 0;
        foreach (var prop in props)
        {
            i++;
            if (CheckTruncationLengthIfExpanding(context) && i + 2 < props.Count - 1)
            {
                context.Out!.Truncated = true;
                elements.Add((ITypeElement)CreateTruncationProperty($"... {props.Count - i} more ... ", isClass, context));
                elements.AddRange(SerializePropertySymbolForInterface(Core.Last(props), baseType, context).Cast<ITypeElement>());
                break;
            }

            context.ApproximateLength += 1; // separator
            elements.AddRange(SerializePropertySymbolForInterface(prop, baseType, context).Cast<ITypeElement>());
        }
        return elements;
    }

    /// <summary>ported from: src/compiler/checker.ts:9955-9989 — the implementation of <c>serializePropertySymbolsForClassOrInterface</c>, specialized to the <c>isClass: true</c> call shape.</summary>
    /// <remarks>
    /// PORT-NOTE: see the <c>isClass: false</c> overload for how the reference's single implementation split. The
    /// elements this overload produces are class elements (property declarations and method overloads), which is what
    /// makes its result assignable to the class declaration's member list.
    /// </remarks>
    internal List<IClassElement> SerializePropertySymbolsForClassOrInterface(IList<ISymbol> props, bool isClass, IType? baseType, bool isStatic, NodeBuilderContext context)
    {
        var elements = new List<IClassElement>();
        var serializePropertySymbol = MakeSerializePropertySymbol(isClass, SyntaxKind.MethodDeclaration, /*useAccessors*/ true, context);
        var i = 0;
        foreach (var prop in props)
        {
            i++;
            if (CheckTruncationLengthIfExpanding(context) && i + 2 < props.Count - 1)
            {
                context.Out!.Truncated = true;
                elements.Add((IClassElement)CreateTruncationProperty($"... {props.Count - i} more ... ", isClass, context));
                foreach (var result in serializePropertySymbol(Core.Last(props), isStatic, baseType))
                {
                    elements.Add((IClassElement)result);
                }
                break;
            }

            context.ApproximateLength += 1; // separator
            foreach (var result in serializePropertySymbol(prop, isStatic, baseType))
            {
                elements.Add((IClassElement)result);
            }
        }
        return elements;
    }

    /// <summary>ported from: src/compiler/checker.ts:9990-10009 — <c>createTruncationProperty</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's return type is the union <c>TypeElement | ClassElement</c>, which has no C# type; the port
    /// returns <see cref="Node"/> and the caller casts to the element interface it needs (as the reference's own
    /// callers do through the array's element type).
    /// </remarks>
    internal Node CreateTruncationProperty(string dotDotDotText, bool isClass, NodeBuilderContext context)
    {
        if ((context.Flags & NodeBuilderFlags.NoTruncation) != 0)
        {
            return EmitNodes.AddSyntheticLeadingComment(_factory.CreateNotEmittedTypeElement(), SyntaxKind.MultiLineCommentTrivia, dotDotDotText);
        }
        return isClass
            ? _factory.CreatePropertyDeclaration(
                /*modifiers*/ null,
                dotDotDotText,
                /*questionOrExclamationToken*/ null,
                /*type*/ null,
                /*initializer*/ null)
            : _factory.CreatePropertySignature(
                /*modifiers*/ null,
                dotDotDotText,
                /*questionToken*/ null,
                /*type*/ null);
    }

    // ---------------------------------------------------------------------------------------------
    // getNamespaceMembersForSerialization / isTypeOnlyNamespace / serializeModule / serializeEnum (checker.ts:10010-10148)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:10010-10024 — <c>getNamespaceMembersForSerialization</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>isIdentifierText</c> (utilities.ts:11556, called at 10022 with <c>ScriptTarget.ESNext</c>) is
    /// <see cref="ScannerUtilities.IsIdentifierText"/> in the port, where it lives beside the scanner it uses.
    /// </remarks>
    internal IList<ISymbol> GetNamespaceMembersForSerialization(ISymbol symbol)
    {
        IList<ISymbol> exports = Core.ArrayFrom(GetExportsOfSymbol(symbol!).Values);
        var merged = GetMergedSymbol(symbol);
        if (!ReferenceEquals(merged, symbol))
        {
            var membersSet = new HashSet<ISymbol>(exports);
            foreach (var exported in GetExportsOfSymbol(merged!).Values)
            {
                if ((GetSymbolFlags(ResolveSymbol(exported)!) & SymbolFlags.Value) == 0)
                {
                    membersSet.Add(exported);
                }
            }
            exports = Core.ArrayFrom(membersSet);
        }
        return Core.Filter(exports, m => IsNamespaceMember(m) && ScannerUtilities.IsIdentifierText(m.EscapedName!, ScriptTarget.ESNext))!;
    }

    /// <summary>ported from: src/compiler/checker.ts:10025-10028 — <c>isTypeOnlyNamespace</c>.</summary>
    internal bool IsTypeOnlyNamespace(ISymbol symbol)
        => Core.Every(GetNamespaceMembersForSerialization(symbol), (m, _) => (GetSymbolFlags(ResolveSymbol(m)!) & SymbolFlags.Value) == 0);

    /// <summary>ported from: src/compiler/checker.ts:10029-10086 — <c>serializeModule</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>moduleSpecifiers.getModuleSpecifiers</c>'s neighbour <c>context.flags |= … | SymbolFormatFlags.UseOnlyExternalAliasing</c>
    /// (checker.ts:10045) ORs the *SymbolFormatFlags* bit value into the NodeBuilderFlags slot — the reference's own
    /// comment at types.ts:5590 records the hole between the two enums — so the port keeps the same numeric bit with an
    /// explicit cast rather than substituting <c>NodeBuilderFlags.UseOnlyExternalAliasing</c>. PORT-NOTE: TS's inner
    /// <c>localName</c> (checker.ts:10061) shadows the block's <c>localName</c>; C# forbids the shadowing, so the inner
    /// one is named <c>specifierLocalName</c>.
    /// </remarks>
    internal void SerializeModule(
        ISymbol symbol,
        string symbolName,
        ModifierFlags modifierFlags,
        NodeBuilderContext context,
        List<IStatement> results,
        bool addingDeclare,
        HashSet<int> visitedSymbols,
        List<Dictionary<int, ISymbol>> deferredPrivatesStack,
        Node? enclosingDeclaration)
    {
        var members = GetNamespaceMembersForSerialization(symbol);
        var expanding = IsExpanding(context);
        // Split NS members up by declaration - members whose parent symbol is the ns symbol vs those whose is not (but were added in later via merging)
        var locationMap = Core.ArrayToMultiMap(members, m => m.Parent is not null && ReferenceEquals(m.Parent, symbol) || expanding ? "real" : "merged", m => m);
        var realMembers = locationMap.Get("real") ?? (IList<ISymbol>)Core.EmptyArray<ISymbol>.Value;
        var mergedMembers = locationMap.Get("merged") ?? (IList<ISymbol>)Core.EmptyArray<ISymbol>.Value;
        // TODO: `suppressNewPrivateContext` is questionable -we need to simply be emitting privates in whatever scope they were declared in, rather
        // than whatever scope we traverse to them in. That's a bit of a complex rewrite, since we're not _actually_ tracking privates at all in advance,
        // so we don't even have placeholders to fill in.
        if (Core.Length(realMembers) != 0 || expanding)
        {
            Node localName;
            if (expanding)
            {
                // Use the same name as symbol display.
                var oldFlags = context.Flags;
                context.Flags |= NodeBuilderFlags.WriteTypeParametersInQualifiedName | (NodeBuilderFlags)SymbolFormatFlags.UseOnlyExternalAliasing;
                localName = SymbolToNode(symbol, context, /*meaning*/ SymbolFlags.All);
                context.Flags = oldFlags;
            }
            else
            {
                var localText = GetInternalSymbolName(symbol, symbolName, context);
                localName = _factory.CreateIdentifier(localText);
                context.ApproximateLength += localText.Length;
            }
            SerializeAsNamespaceDeclaration(realMembers, localName, modifierFlags, (symbol.Flags & (SymbolFlags.Function | SymbolFlags.Assignment)) != 0, context, results, addingDeclare, visitedSymbols, deferredPrivatesStack, enclosingDeclaration);
        }
        if (Core.Length(mergedMembers) != 0)
        {
            var containingFile = Utilities.GetSourceFileOfNode(context.EnclosingDeclaration);
            var localName = GetInternalSymbolName(symbol, symbolName, context);
            var nsBody = _factory.CreateModuleBlock([
                _factory.CreateExportDeclaration(
                    /*modifiers*/ null,
                    /*isTypeOnly*/ false,
                    _factory.CreateNamedExports(Core.MapDefined(
                        Core.Filter(mergedMembers, n => n.EscapedName != InternalSymbolName.ExportEquals.Value)!,
                        (s, _) =>
                        {
                            var name = Utilities.UnescapeLeadingUnderscores(s.EscapedName!);
                            var specifierLocalName = GetInternalSymbolName(s, name, context);
                            var aliasDecl = s.Declarations is not null ? GetDeclarationOfAliasSymbol(s) : null;
                            if (containingFile is not null && (aliasDecl is not null
                                    ? !ReferenceEquals(containingFile, Utilities.GetSourceFileOfNode((Node?)aliasDecl))
                                    : !Core.Some(s.Declarations, d => ReferenceEquals(Utilities.GetSourceFileOfNode((Node?)d), containingFile))))
                            {
                                context.Tracker?.ReportNonlocalAugmentation(containingFile, symbol, s);
                                return null;
                            }
                            var target = aliasDecl is not null ? GetTargetOfAliasDeclaration(aliasDecl, /*dontRecursivelyResolve*/ true) : null;
                            IncludePrivateSymbol(target ?? s, context, deferredPrivatesStack);
                            var targetName = target is not null ? GetInternalSymbolName(target, Utilities.UnescapeLeadingUnderscores(target.EscapedName!), context) : specifierLocalName;
                            return _factory.CreateExportSpecifier(/*isTypeOnly*/ false, name == targetName ? null : targetName, name);
                        })),
                    /*moduleSpecifier*/ null,
                    /*attributes*/ null),
            ]);
            AddResult(
                context,
                results,
                addingDeclare,
                _factory.CreateModuleDeclaration(
                    /*modifiers*/ null,
                    _factory.CreateIdentifier(localName),
                    nsBody,
                    NodeFlags.Namespace),
                ModifierFlags.None);
        }
    }

    /// PORT-NOTE: the trailing frame values are unread here (see <see cref="SerializeTypeAlias"/>'s note).
    /// <summary>ported from: src/compiler/checker.ts:10087-10148 — <c>serializeEnum</c>.</summary>
    internal void SerializeEnum(
        ISymbol symbol, string symbolName, ModifierFlags modifierFlags,
        NodeBuilderContext context,
        List<IStatement> results,
        bool addingDeclare,
        HashSet<int> visitedSymbols,
        List<Dictionary<int, ISymbol>> deferredPrivatesStack,
        Node? enclosingDeclaration)
    {
        var internalSymbolName = GetInternalSymbolName(symbol, symbolName, context);
        context.ApproximateLength += 9 + internalSymbolName.Length; // `enum internalName { }`
        var members = new List<EnumMember>();
        var memberProps = Core.Filter(GetPropertiesOfType(GetTypeOfSymbol(symbol)), p => (p.Flags & SymbolFlags.EnumMember) != 0)!;
        var i = 0;
        foreach (var p in memberProps)
        {
            i++;
            if (CheckTruncationLengthIfExpanding(context) && i + 2 < memberProps.Count - 1)
            {
                context.Out!.Truncated = true;
                members.Add(_factory.CreateEnumMember($" ... {memberProps.Count - i} more ... ", null));
                var last = Core.Last(memberProps);
                var lastDeclarations = last.Declarations;
                object? initializedValue = lastDeclarations is { Count: > 0 } && NodeTests.IsEnumMember((Node)lastDeclarations[0])
                    ? GetConstantValue((Node)lastDeclarations[0])
                    : null;
                Expression? initializer = initializedValue is null ? null
                    : initializedValue is string text ? (Expression)_factory.CreateStringLiteral(text)
                    : _factory.CreateNumericLiteral(initializedValue);
                var memberName = Utilities.UnescapeLeadingUnderscores(last.EscapedName!);
                members.Add(_factory.CreateEnumMember(memberName, initializer));
                break;
            }
            // TODO: Handle computed names
            // I hate that to get the initialized value we need to walk back to the declarations here; but there's no
            // other way to get the possible const value of an enum member that I'm aware of, as the value is cached
            // _on the declaration_, not on the declaration's symbol...
            var pDeclarations = p.Declarations;
            var memberDecl = pDeclarations is { Count: > 0 } && NodeTests.IsEnumMember((Node)pDeclarations[0])
                ? (EnumMember)pDeclarations[0]
                : null;
            Expression? initializerValue;
            int initializerLength;
            var memberInitializer = memberDecl?.Initializer;
            if (IsExpanding(context) && memberInitializer is not null)
            {
                initializerValue = Utilities.GetSynthesizedDeepClone(memberInitializer);
                initializerLength = memberInitializer.End - memberInitializer.Pos;
            }
            else
            {
                var constValue = memberDecl is not null ? GetConstantValue(memberDecl) : null;
                initializerValue = constValue is null ? null
                    : constValue is string constText ? (Expression)_factory.CreateStringLiteral(constText)
                    : _factory.CreateNumericLiteral(constValue);
                initializerLength = (initializerValue as StringLiteral)?.Text?.Length
                    ?? (initializerValue as NumericLiteral)?.Text?.Length
                    ?? 0;
            }
            var enumMemberName = Utilities.UnescapeLeadingUnderscores(p.EscapedName!);
            context.ApproximateLength += 4 + enumMemberName.Length + initializerLength; // `member = initializer,`
            members.Add(_factory.CreateEnumMember(enumMemberName, initializerValue));
        }
        AddResult(
            context,
            results,
            addingDeclare,
            _factory.CreateEnumDeclaration(
                ToNodeModifiers(_factory.CreateModifiersFromModifierFlags(IsConstEnumSymbol(symbol) ? ModifierFlags.Const : ModifierFlags.None)),
                internalSymbolName,
                members),
            modifierFlags);
    }

    // ---------------------------------------------------------------------------------------------
    // serializeAsFunctionNamespaceMerge / createTruncationStatement / getSignatureTextRangeLocation (checker.ts:10149-10184)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:10149-10164 — <c>serializeAsFunctionNamespaceMerge</c>.</summary>
    internal void SerializeAsFunctionNamespaceMerge(
        IType type,
        ISymbol symbol,
        string localName,
        ModifierFlags modifierFlags,
        NodeBuilderContext context,
        List<IStatement> results,
        bool addingDeclare,
        HashSet<int> visitedSymbols,
        List<Dictionary<int, ISymbol>> deferredPrivatesStack,
        Node? enclosingDeclaration)
    {
        var signatures = GetSignaturesOfType(type, SignatureKind.Call);
        foreach (var sig in signatures)
        {
            context.ApproximateLength += 1; // ;
            // Each overload becomes a separate function declaration, in order
            var decl = SignatureToSignatureDeclarationHelper(sig, SyntaxKind.FunctionDeclaration, context, new SignatureToSignatureDeclarationOptions { Name = _factory.CreateIdentifier(localName) });
            AddResult(context, results, addingDeclare, SetTextRange(context, decl, GetSignatureTextRangeLocation(sig)), modifierFlags);
        }
        // Module symbol emit will take care of module-y members, provided it has exports
        var symbolExports = symbol.Exports;
        if (!((symbol.Flags & (SymbolFlags.ValueModule | SymbolFlags.NamespaceModule)) != 0 && symbolExports is not null && symbolExports.Count != 0))
        {
            var props = Core.Filter(GetPropertiesOfType(type), IsNamespaceMember)!;
            context.ApproximateLength += localName.Length;
            SerializeAsNamespaceDeclaration(props, _factory.CreateIdentifier(localName), modifierFlags, /*suppressNewPrivateContext*/ true, context, results, addingDeclare, visitedSymbols, deferredPrivatesStack, enclosingDeclaration);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:10165-10171 — <c>createTruncationStatement</c>.</summary>
    internal Node CreateTruncationStatement(string dotDotDotText, NodeBuilderContext context)
    {
        if ((context.Flags & NodeBuilderFlags.NoTruncation) != 0)
        {
            return EmitNodes.AddSyntheticLeadingComment(_factory.CreateEmptyStatement(), SyntaxKind.MultiLineCommentTrivia, dotDotDotText);
        }
        return _factory.CreateExpressionStatement(_factory.CreateIdentifier(dotDotDotText));
    }

    /// <summary>ported from: src/compiler/checker.ts:10172-10184 — <c>getSignatureTextRangeLocation</c>.</summary>
    internal Node? GetSignatureTextRangeLocation(ISignature signature)
    {
        var declaration = signature.Declaration;
        var parent = declaration?.Parent;
        if (parent is not null)
        {
            if (NodeTests.IsBinaryExpression(parent) && Utilities.GetAssignmentDeclarationKind(parent) == AssignmentDeclarationKind.Property)
            {
                return parent;
            }
            // for expressions assigned to `var`s, use the `var` as the text range
            var grandParent = parent.Parent;
            if (NodeTests.IsVariableDeclaration(parent) && grandParent is not null)
            {
                return grandParent;
            }
        }
        return declaration;
    }

    // ---------------------------------------------------------------------------------------------
    // serializeAsNamespaceDeclaration / isNamespaceMember / sanitizeJSDocImplements (checker.ts:10185-10283)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:10185-10256 — <c>serializeAsNamespaceDeclaration</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the sub-context (checker.ts:10218) is a copy of the whole context — see
    /// <see cref="CreateNodeBuilderSubcontext"/>. <c>results</c> and <c>addingDeclare</c> are the caller's parameters,
    /// so the reference's swap of the frame variables (10215-10226) is a swap of locals here, and the recursion into
    /// <c>visitSymbolTable</c> gets the sub-context, the fresh results list and <c>addingDeclare = false</c>.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>parseNodeFactory</c> (checker.ts:10209, parser.ts:441) is
    /// <see cref="FactoryUtilities.ParseNodeFactory"/> — the port's parse tree factory (its class remarks record that
    /// this is the instance TS means here, not the synthetic <c>_factory</c>); the <c>factory.createModuleBlock([])</c>
    /// argument on that same reference line is the synthetic factory, i.e. <c>_factory</c>.
    /// </para>
    /// <para>
    /// PORT-NOTE: the port's <c>ModuleDeclaration.Name</c> is a <c>MemberExpression?</c>, while TS's
    /// <c>ModuleName</c> is <c>Identifier | QualifiedName</c>; the cast below is the port's only way to store the
    /// qualified-name case, which its factory signature cannot express.
    /// </para>
    /// </remarks>
    internal void SerializeAsNamespaceDeclaration(
        IList<ISymbol> props,
        Node localName,
        ModifierFlags modifierFlags,
        bool suppressNewPrivateContext,
        NodeBuilderContext context,
        List<IStatement> results,
        bool addingDeclare,
        HashSet<int> visitedSymbols,
        List<Dictionary<int, ISymbol>> deferredPrivatesStack,
        Node? enclosingDeclaration)
    {
        var nodeFlags = NodeTests.IsIdentifier(localName) ? NodeFlags.Namespace : NodeFlags.None;
        var expanding = IsExpanding(context);
        if (Core.Length(props) != 0)
        {
            context.ApproximateLength += 14; // "namespace { }"
            var localVsRemoteMap = Core.ArrayToMultiMap(
                props,
                p => Core.Length(p.Declarations) == 0
                    || Core.Some(p.Declarations, d => ReferenceEquals(Utilities.GetSourceFileOfNode((Node?)d), Utilities.GetSourceFileOfNode(context.EnclosingDeclaration)))
                    || expanding
                        ? "local"
                        : "remote",
                p => p);
            var localProps = localVsRemoteMap.Get("local") ?? (IList<ISymbol>)Core.EmptyArray<ISymbol>.Value;
            // handle remote props first - we need to make an `import` declaration that points at the module containing each remote
            // prop in the outermost scope (TODO: a namespace within a namespace would need to be appropriately handled by this)
            // Example:
            // import Foo_1 = require("./exporter");
            // export namespace ns {
            //     import Foo = Foo_1.Foo;
            //     export { Foo };
            //     export const c: number;
            // }
            // This is needed because in JS, statements like `const x = require("./f")` support both type and value lookup, even if they're
            // normally just value lookup (so it functions kinda like an alias even when it's not an alias)
            // _Usually_, we'll simply print the top-level as an alias instead of a `var` in such situations, however is is theoretically
            // possible to encounter a situation where a type has members from both the current file and other files - in those situations,
            // emit akin to the above would be needed.

            // Add a namespace
            // Create namespace as non-synthetic so it is usable as an enclosing declaration
            var fakespace = FactoryUtilities.ParseNodeFactory.CreateModuleDeclaration(
                /*modifiers*/ null,
                (MemberExpression)localName,
                _factory.CreateModuleBlock([]),
                nodeFlags);
            Utilities.SetParent(fakespace, enclosingDeclaration!);
            fakespace.Locals = Utilities.CreateSymbolTable(props);
            fakespace.Symbol = props[0].Parent!;

            var oldAddingDeclare = addingDeclare;
            addingDeclare = false;
            var subResults = new List<IStatement>();
            var subcontext = CreateNodeBuilderSubcontext(context, fakespace);
            var oldContext = context;
            context = subcontext;
            // TODO: implement handling for the localVsRemoteMap.get("remote") - should be difficult to trigger (see comment above), as only interesting cross-file js merges should make this possible
            VisitSymbolTable(Utilities.CreateSymbolTable(localProps), context, subResults, addingDeclare, visitedSymbols, deferredPrivatesStack, enclosingDeclaration, suppressNewPrivateContext, /*propertyAsAlias*/ true);
            context = oldContext;
            addingDeclare = oldAddingDeclare;
            var declarations = subResults;
            // replace namespace with synthetic version
            var defaultReplaced = Core.Map(declarations, (d, _) =>
                NodeTests.IsExportAssignment((Node)d) && ((ExportAssignment)d).IsExportEquals is not true && NodeTests.IsIdentifier(((ExportAssignment)d).Expression!)
                    ? (IStatement)_factory.CreateExportDeclaration(
                        /*modifiers*/ null,
                        /*isTypeOnly*/ false,
                        _factory.CreateNamedExports([
                            _factory.CreateExportSpecifier(
                                /*isTypeOnly*/ false,
                                ((ExportAssignment)d).Expression!,
                                _factory.CreateIdentifier(InternalSymbolName.Default.Value)),
                        ]),
                        /*moduleSpecifier*/ null,
                        /*attributes*/ null)
                    : d)!;
            var exportModifierStripped = Core.Every(defaultReplaced, (d, _) => Utilities.HasSyntacticModifier((Node)d, ModifierFlags.Export))
                ? Core.Map(defaultReplaced, (d, _) => RemoveExportModifier(d))!
                : defaultReplaced;
            fakespace = _factory.UpdateModuleDeclaration(
                fakespace,
                fakespace.Modifiers,
                fakespace.Name,
                _factory.CreateModuleBlock(exportModifierStripped));
            AddResult(context, results, addingDeclare, fakespace, modifierFlags); // namespaces can never be default exported
        }
        else if (expanding)
        {
            context.ApproximateLength += 14; // "namespace { }"
            AddResult(
                context,
                results,
                addingDeclare,
                _factory.CreateModuleDeclaration(
                    /*modifiers*/ null,
                    (MemberExpression)localName,
                    _factory.CreateModuleBlock([]),
                    nodeFlags),
                modifierFlags);
        }
    }

    /// <summary>
    /// A copy of <paramref name="context"/> whose enclosing declaration is <paramref name="enclosingDeclaration"/>.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: this is the port of the object spread at checker.ts:10218 (<c>{ ...context, enclosingDeclaration:
    /// fakespace }</c>), which has no C# counterpart, so the copy is spelled out slot by slot. It must be a copy and not
    /// a save/restore of the one instance: the reference discards everything the nested namespace serialization writes
    /// into its sub-context — the remapped/used name tables, <c>approximateLength</c>, the tracked symbol list and the
    /// flags — by dropping the copy. The tracker reference itself is shared, exactly as the spread shares it.
    /// </remarks>
    internal static NodeBuilderContext CreateNodeBuilderSubcontext(NodeBuilderContext context, Node? enclosingDeclaration)
    {
        var subcontext = new NodeBuilderContext
        {
            EnclosingFile = context.EnclosingFile,
            Flags = context.Flags,
            InternalFlags = context.InternalFlags,
            Tracker = context.Tracker,
            MaxExpansionDepth = context.MaxExpansionDepth,
            MaxTruncationLength = context.MaxTruncationLength,
            EncounteredError = context.EncounteredError,
            ReportedDiagnostic = context.ReportedDiagnostic,
            TrackedSymbols = context.TrackedSymbols,
            VisitedTypes = context.VisitedTypes,
            SymbolDepth = context.SymbolDepth,
            InferTypeParameters = context.InferTypeParameters,
            ApproximateLength = context.ApproximateLength,
            Truncating = context.Truncating,
            MustCreateTypeParameterSymbolList = context.MustCreateTypeParameterSymbolList,
            TypeParameterSymbolList = context.TypeParameterSymbolList,
            MustCreateTypeParametersNamesLookups = context.MustCreateTypeParametersNamesLookups,
            TypeParameterNames = context.TypeParameterNames,
            TypeParameterNamesByText = context.TypeParameterNamesByText,
            TypeParameterNamesByTextNextNameCount = context.TypeParameterNamesByTextNextNameCount,
            UsedSymbolNames = context.UsedSymbolNames,
            RemappedSymbolNames = context.RemappedSymbolNames,
            RemappedSymbolReferences = context.RemappedSymbolReferences,
            EnclosingSymbolTypes = context.EnclosingSymbolTypes,
            ReverseMappedStack = context.ReverseMappedStack,
            Bundled = context.Bundled,
            Mapper = context.Mapper,
            Depth = context.Depth,
            SuppressReportInferenceFallback = context.SuppressReportInferenceFallback,
            TypeStack = context.TypeStack,
            Out = context.Out,
            NoInferenceFallback = context.NoInferenceFallback,
            EnclosingDeclaration = enclosingDeclaration,
        };
        return subcontext;
    }

    /// <summary>ported from: src/compiler/checker.ts:10257-10261 — <c>isNamespaceMember</c>.</summary>
    internal bool IsNamespaceMember(ISymbol p)
    {
        if ((p.Flags & (SymbolFlags.Type | SymbolFlags.Namespace | SymbolFlags.Alias)) != 0)
        {
            return true;
        }
        if ((p.Flags & SymbolFlags.Prototype) != 0 || p.EscapedName == "prototype")
        {
            return false;
        }
        var valueDeclaration = p.ValueDeclaration;
        return valueDeclaration is not null && Utilities.IsStatic((Node)valueDeclaration) && Utilities.IsClassLike(((Node)valueDeclaration).Parent!);
    }

    /// <summary>ported from: src/compiler/checker.ts:10262-10283 — <c>sanitizeJSDocImplements</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's nested <c>cleanup</c> local function (10278-10281) is inlined at its three call sites — it only
    /// restores <c>context.enclosingDeclaration</c>, which is the line before each <c>return</c>/<c>continue</c> here.
    /// PORT-NOTE: <c>trackExistingEntityName</c> is agent NBNames' member; the port reads it as the pair the reference
    /// destructures (<c>{ introducesError, node }</c>), which is the only shape that can carry both.
    /// </remarks>
    internal IList<ExpressionWithTypeArguments>? SanitizeJSDocImplements(IList<ExpressionWithTypeArguments> clauses, NodeBuilderContext context)
    {
        var result = Core.MapDefined(clauses, (e, _) =>
        {
            var oldEnclosing = context.EnclosingDeclaration;
            context.EnclosingDeclaration = e;
            Expression? expr = e.Expression;
            if (expr is not null && Utilities.IsEntityNameExpression(expr))
            {
                if (NodeTests.IsIdentifier(expr) && Utilities.IdText(expr) == "")
                {
                    context.EnclosingDeclaration = oldEnclosing; // Empty heritage clause, should be an error, but prefer emitting no heritage clauses to reemitting the empty one
                    return null;
                }
                var (introducesError, trackedNode) = TrackExistingEntityName(expr, context);
                expr = trackedNode;
                if (introducesError)
                {
                    context.EnclosingDeclaration = oldEnclosing;
                    return null;
                }
            }
            var expressionWithTypeArguments = _factory.CreateExpressionWithTypeArguments(
                expr,
                Core.Map(e.TypeArguments, (a, _) =>
                {
                    ITypeNode? reusableTypeNode = SyntacticNodeBuilder.TryReuseExistingTypeNode!(context, a);
                    return reusableTypeNode ?? TypeToTypeNodeHelper(GetTypeFromTypeNode(context, a)!, context);
                }));
            context.EnclosingDeclaration = oldEnclosing;
            return expressionWithTypeArguments;
        });
        return result.Count == clauses.Count ? result : null;
    }

    // ---------------------------------------------------------------------------------------------
    // serializeAsClass / getSomeTargetNameFromDeclarations (checker.ts:10295-10394)
    // ---------------------------------------------------------------------------------------------

    /// PORT-NOTE: the trailing frame values are unread here (see <see cref="SerializeTypeAlias"/>'s note).
    /// <summary>ported from: src/compiler/checker.ts:10295-10372 — <c>serializeAsClass</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>allType</c>'s neighbour <c>anyType</c> (10308) is the checker's <c>_anyType</c> field.
    /// PORT-NOTE: <c>foo.forEach(localParams, p =&gt; …)</c> (10302) is a C# <c>foreach</c> — the port's
    /// <c>Core.ForEach</c> requires a reference-typed result, and the reference discards the callback's value.
    /// </remarks>
    internal void SerializeAsClass(
        ISymbol symbol, string localName, ModifierFlags modifierFlags,
        NodeBuilderContext context,
        List<IStatement> results,
        bool addingDeclare,
        HashSet<int> visitedSymbols,
        List<Dictionary<int, ISymbol>> deferredPrivatesStack,
        Node? enclosingDeclaration)
    {
        context.ApproximateLength += 9 + localName.Length; // `class localName { }`
        var originalDecl = Core.Find(symbol.Declarations, (d, _) => Utilities.IsClassLike((Node)d));
        var oldEnclosing = context.EnclosingDeclaration;
        context.EnclosingDeclaration = (Node?)originalDecl ?? oldEnclosing;
        var localParams = GetLocalTypeParametersOfClassOrInterfaceOrTypeAlias(symbol);
        var typeParamDecls = Core.Map(localParams, (p, _) => TypeParameterToDeclaration(p, context));
        if (localParams is not null)
        {
            foreach (var p in localParams)
            {
                context.ApproximateLength += Utilities.SymbolName(p.Symbol!).Length;
            }
        }
        var classType = (InterfaceType)GetTypeWithThisArgument(GetDeclaredTypeOfClassOrInterface(symbol));
        var baseTypes = GetBaseTypes(classType);
        var originalImplements = originalDecl is not null ? Utilities.GetEffectiveImplementsTypeNodes((IClassLikeDeclarationBase)originalDecl) : null;
        IList<ExpressionWithTypeArguments>? implementsExpressions = originalImplements is not null ? SanitizeJSDocImplements(originalImplements, context) : null;
        implementsExpressions ??= Core.MapDefined(GetImplementsTypes(classType), (t, _) => SerializeImplementedType(t, context));
        var staticType = GetTypeOfSymbol(symbol);
        var staticSymbol = staticType.Symbol;
        var staticValueDeclaration = staticSymbol?.ValueDeclaration;
        var isClass = staticValueDeclaration is not null && Utilities.IsClassLike((Node)staticValueDeclaration);
        var staticBaseType = isClass
            ? GetBaseConstructorTypeOfClass((InterfaceType)staticType)
            : _anyType;
        context.ApproximateLength += (Core.Length(baseTypes) != 0 ? 8 : 0) + (Core.Length(implementsExpressions) != 0 ? 11 : 0); // `extends ` and `implements `
        var heritageClauses = new List<HeritageClause>();
        if (Core.Length(baseTypes) != 0)
        {
            heritageClauses.Add(_factory.CreateHeritageClause(SyntaxKind.ExtendsKeyword, Core.Map(baseTypes, (b, _) => SerializeBaseType(b, staticBaseType, localName, context, results, addingDeclare))!));
        }
        if (Core.Length(implementsExpressions) != 0)
        {
            heritageClauses.Add(_factory.CreateHeritageClause(SyntaxKind.ImplementsKeyword, [ .. implementsExpressions ]));
        }
        var symbolProps = GetNonInheritedProperties(classType, baseTypes, GetPropertiesOfType(classType));
        var publicSymbolProps = Core.Filter(symbolProps, s => !IsHashPrivate(s))!;
        var hasPrivateIdentifier = Core.Some(symbolProps, IsHashPrivate);
        IList<IClassElement> privateProperties;
        // Boil down all private properties into a single one.
        if (hasPrivateIdentifier)
        {
            if (IsExpanding(context))
            {
                privateProperties = SerializePropertySymbolsForClassOrInterface(Core.Filter(symbolProps, IsHashPrivate)!, /*isClass*/ true, Core.ElementAt(baseTypes, 0), /*isStatic*/ false, context);
            }
            else
            {
                privateProperties = [
                    (IClassElement)_factory.CreatePropertyDeclaration(
                        /*modifiers*/ null,
                        _factory.CreatePrivateIdentifier("#private"),
                        /*questionOrExclamationToken*/ null,
                        /*type*/ null,
                        /*initializer*/ null),
                ];
            }
        }
        else
        {
            privateProperties = Core.EmptyArray<IClassElement>.Value;
        }
        if (hasPrivateIdentifier && !IsExpanding(context))
        {
            context.ApproximateLength += 9; // `#private;`
        }
        var publicProperties = SerializePropertySymbolsForClassOrInterface(publicSymbolProps, /*isClass*/ true, Core.ElementAt(baseTypes, 0), /*isStatic*/ false, context);
        // Consider static members empty if symbol also has function or module meaning - function namespacey emit will handle statics
        var staticMembers = SerializePropertySymbolsForClassOrInterface(
            Core.Filter(GetPropertiesOfType(staticType), p => (p.Flags & SymbolFlags.Prototype) == 0 && p.EscapedName != "prototype" && !IsNamespaceMember(p))!,
            /*isClass*/ true,
            staticBaseType,
            /*isStatic*/ true,
            context);
        // When we encounter an `X.prototype.y` assignment in a JS file, we bind `X` as a class regardless as to whether
        // the value is ever initialized with a class or function-like value. For cases where `X` could never be
        // created via `new`, we will inject a `private constructor()` declaration to indicate it is not createable.
        var isNonConstructableClassLikeInJsFile = !isClass
            && symbol.ValueDeclaration is not null
            && Utilities.IsInJSFile((Node)symbol.ValueDeclaration)
            && !Core.Some(GetSignaturesOfType(staticType, SignatureKind.Construct));
        if (isNonConstructableClassLikeInJsFile)
        {
            context.ApproximateLength += 21; // `private constructor()`
        }
        List<Node> constructors;
        if (isNonConstructableClassLikeInJsFile)
        {
            constructors = [
                _factory.CreateConstructorDeclaration(
                    ToNodeModifiers(_factory.CreateModifiersFromModifierFlags(ModifierFlags.Private)),
                    /*parameters*/ [],
                    /*body*/ null),
            ];
        }
        else
        {
            constructors = SerializeSignatures(SignatureKind.Construct, staticType, staticBaseType, SyntaxKind.Constructor, context);
        }
        var indexSignatures = SerializeIndexSignatures(classType, Core.ElementAt(baseTypes, 0), context);
        var textRange = symbol.Declarations is not null
            ? (Node?)Core.FirstOrUndefined(Core.Filter(symbol.Declarations, d => NodeTests.IsClassDeclaration((Node)d) || NodeTests.IsClassExpression((Node)d)))
            : null;
        context.EnclosingDeclaration = oldEnclosing;
        var members = new List<IClassElement>();
        members.AddRange(indexSignatures);
        members.AddRange(staticMembers);
        foreach (var constructor in constructors)
        {
            members.Add((IClassElement)constructor);
        }
        members.AddRange(publicProperties);
        members.AddRange(privateProperties);
        AddResult(
            context,
            results,
            addingDeclare,
            SetTextRange(
                context,
                _factory.CreateClassDeclaration(
                    /*modifiers*/ null,
                    localName,
                    typeParamDecls,
                    heritageClauses,
                    members),
                textRange),
            modifierFlags);
    }

    /// <summary>ported from: src/compiler/checker.ts:10373-10394 — <c>getSomeTargetNameFromDeclarations</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>isImportSpecifier(d) || isExportSpecifier(d)</c> narrows to the
    /// <c>ImportSpecifier | ExportSpecifier</c> union whose <c>propertyName ?? name</c> is a <c>ModuleExportName</c>;
    /// the port states that narrowing as a switch over the two classes and widens <c>ModuleExportNameTextUnescaped</c>'s
    /// argument to <see cref="Node"/>.
    /// </remarks>
    internal string? GetSomeTargetNameFromDeclarations(IList<IDeclaration>? declarations)
        => Core.FirstDefined(declarations, (d, _) =>
        {
            var node = (Node)d;
            if (NodeTests.IsImportSpecifier(node) || NodeTests.IsExportSpecifier(node))
            {
                return node switch
                {
                    ImportSpecifier importSpecifier => Utilities.ModuleExportNameTextUnescaped((Node?)importSpecifier.PropertyName ?? importSpecifier.Name),
                    ExportSpecifier exportSpecifier => Utilities.ModuleExportNameTextUnescaped((Node?)exportSpecifier.PropertyName ?? exportSpecifier.Name),
                    _ => null,
                };
            }
            if (NodeTests.IsBinaryExpression(node) || NodeTests.IsExportAssignment(node))
            {
                var expression = NodeTests.IsExportAssignment(node) ? ((ExportAssignment)node).Expression : ((BinaryExpression)node).Right;
                if (expression is not null && NodeTests.IsPropertyAccessExpression(expression))
                {
                    return Utilities.IdText(((PropertyAccessExpression)expression).Name!);
                }
            }
            if (IsAliasSymbolDeclaration(node))
            {
                // This is... heuristic, at best. But it's probably better than always printing the name of the shorthand ambient module.
                var name = Utilities.GetNameOfDeclaration(node);
                if (name is not null && NodeTests.IsIdentifier((Node)name))
                {
                    return Utilities.IdText((Node)name);
                }
            }
            return null;
        });

    // ---------------------------------------------------------------------------------------------
    // serializeAsAlias / serializeExportSpecifier / serializeMaybeAliasAssignment (checker.ts:10395-10760)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:10395-10631 — <c>serializeAsAlias</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: <c>allowSyntheticDefaultImports</c> (10407) is the checker's state slot of that name (checker.ts:1534),
    /// landed as <c>Checker._allowSyntheticDefaultImports</c> (Checker.cs:286).
    /// </para>
    /// <para>
    /// PORT-NOTE: the reference falls through from <c>VariableDeclaration</c> into <c>ImportEqualsDeclaration</c> when
    /// the initializer is not a property access ("else fall through and treat commonjs require just like import=");
    /// C# spells that as <c>goto case</c>. PORT-NOTE: <c>node.parent?.parent</c> reads (10413, 10445) go through
    /// <c>Node.Parent</c>, which every concrete declaration class has and the <c>IDeclaration</c> face does not, so the
    /// node is cast once at the top.
    /// </para>
    /// </remarks>
    internal void SerializeAsAlias(
        ISymbol symbol,
        string localName,
        ModifierFlags modifierFlags,
        NodeBuilderContext context,
        List<IStatement> results,
        bool addingDeclare,
        HashSet<int> visitedSymbols,
        List<Dictionary<int, ISymbol>> deferredPrivatesStack,
        Node? enclosingDeclaration)
    {
        // synthesize an alias, eg `export { symbolName as Name }`
        // need to mark the alias `symbol` points at
        // as something we need to serialize as a private declaration as well
        var aliasDeclaration = GetDeclarationOfAliasSymbol(symbol);
        if (aliasDeclaration is null)
        {
            Debug.Fail();
            return;
        }
        var node = (Node)aliasDeclaration;
        var target = GetMergedSymbol(GetTargetOfAliasDeclaration(aliasDeclaration, /*dontRecursivelyResolve*/ true));
        if (target is null)
        {
            return;
        }
        // If `target` refers to a shorthand module symbol, the name we're trying to pull out isn;t recoverable from the target symbol
        // In such a scenario, we must fall back to looking for an alias declaration on `symbol` and pulling the target name from that
        var verbatimTargetName = Utilities.IsShorthandAmbientModuleSymbol(target) && GetSomeTargetNameFromDeclarations(symbol.Declarations) is string someTargetName
            ? someTargetName
            : Utilities.UnescapeLeadingUnderscores(target.EscapedName!);
        if (verbatimTargetName == InternalSymbolName.ExportEquals.Value && _allowSyntheticDefaultImports)
        {
            // target refers to an `export=` symbol that was hoisted into a synthetic default - rename here to match
            verbatimTargetName = InternalSymbolName.Default.Value;
        }
        var targetName = GetInternalSymbolName(target, verbatimTargetName, context);
        IncludePrivateSymbol(target, context, deferredPrivatesStack); // the target may be within the same scope - attempt to serialize it first
        switch (node.Kind)
        {
            case SyntaxKind.BindingElement:
                {
                    var bindingElement = (BindingElement)node;
                    if (bindingElement.Parent?.Parent is { Kind: SyntaxKind.VariableDeclaration })
                    {
                        // const { SomeClass } = require('./lib');
                        var specifier = GetSpecifierForModuleSymbol(target.Parent ?? target, context); // './lib'
                        var propertyName = bindingElement.PropertyName;
                        var propertyNameText = propertyName is not null && NodeTests.IsIdentifier(propertyName) ? Utilities.IdText(propertyName) : null;
                        context.ApproximateLength += 24 + localName.Length + specifier.Length + (propertyNameText?.Length ?? 0); // `import { propertyName as name } from "specifier";`
                        AddResult(
                            context,
                            results,
                            addingDeclare,
                            _factory.CreateImportDeclaration(
                                /*modifiers*/ null,
                                _factory.CreateImportClause(
                                    /*phaseModifier*/ null,
                                    /*name*/ null,
                                    _factory.CreateNamedImports([
                                        _factory.CreateImportSpecifier(
                                            /*isTypeOnly*/ false,
                                            propertyNameText is not null ? _factory.CreateIdentifier(propertyNameText) : null,
                                            _factory.CreateIdentifier(localName)),
                                    ])),
                                _factory.CreateStringLiteral(specifier),
                                /*attributes*/ null),
                            ModifierFlags.None);
                        break;
                    }
                    // We don't know how to serialize this (nested?) binding element
                    Debug.FailBadSyntaxKind(bindingElement.Parent?.Parent ?? bindingElement, "Unhandled binding element grandparent kind in declaration serialization");
                    break;
                }
            case SyntaxKind.ShorthandPropertyAssignment:
                if (node.Parent?.Parent is { Kind: SyntaxKind.BinaryExpression })
                {
                    // module.exports = { SomeClass }
                    SerializeExportSpecifier(
                        Utilities.UnescapeLeadingUnderscores(symbol.EscapedName!),
                        targetName,
                        context,
                        results,
                        addingDeclare);
                }
                break;
            case SyntaxKind.VariableDeclaration:
                {
                    var variableDeclaration = (VariableDeclaration)node;
                    // commonjs require: const x = require('y')
                    var variableInitializer = variableDeclaration.Initializer;
                    if (variableInitializer is not null && NodeTests.IsPropertyAccessExpression(variableInitializer))
                    {
                        // const x = require('y').z
                        var initializer = (PropertyAccessExpression)variableInitializer; // require('y').z
                        var uniqueName = _factory.CreateUniqueName(localName, /*flags*/ null); // _x
                        var requireSpecifier = GetSpecifierForModuleSymbol(target.Parent ?? target, context); // 'y'
                        // import _x = require('y');
                        context.ApproximateLength += 22 + requireSpecifier.Length + Utilities.IdText(uniqueName).Length; // `import uniqueName = require("specifier");`
                        AddResult(
                            context,
                            results,
                            addingDeclare,
                            _factory.CreateImportEqualsDeclaration(
                                /*modifiers*/ null,
                                /*isTypeOnly*/ false,
                                uniqueName,
                                _factory.CreateExternalModuleReference(_factory.CreateStringLiteral(requireSpecifier))),
                            ModifierFlags.None);
                        // import x = _x.z
                        context.ApproximateLength += 12 + localName.Length + Utilities.IdText(uniqueName).Length + Utilities.IdText(initializer.Name!).Length; // `import localName = uniqueName.initializerName;`
                        AddResult(
                            context,
                            results,
                            addingDeclare,
                            _factory.CreateImportEqualsDeclaration(
                                /*modifiers*/ null,
                                /*isTypeOnly*/ false,
                                _factory.CreateIdentifier(localName),
                                _factory.CreateQualifiedName(uniqueName, initializer.Name)),
                            modifierFlags);
                        break;
                    }
                    // else fall through and treat commonjs require just like import=
                    goto case SyntaxKind.ImportEqualsDeclaration;
                }
            case SyntaxKind.ImportEqualsDeclaration:
                // This _specifically_ only exists to handle json declarations - where we make aliases, but since
                // we emit no declarations for the json document, must not refer to it in the declarations
                if (target.EscapedName == InternalSymbolName.ExportEquals.Value
                    && Core.Some(target.Declarations, d => NodeTests.IsSourceFile((Node)d) && Utilities.IsJsonSourceFile((SourceFile)d)))
                {
                    SerializeMaybeAliasAssignment(symbol, context, results, addingDeclare, visitedSymbols, deferredPrivatesStack, enclosingDeclaration);
                    break;
                }
                // Could be a local `import localName = ns.member` or
                // an external `import localName = require("whatever")`
                var isLocalImport = (target.Flags & SymbolFlags.ValueModule) == 0 && !NodeTests.IsVariableDeclaration(node);
                context.ApproximateLength += 11 + localName.Length + Utilities.UnescapeLeadingUnderscores(target.EscapedName!).Length; // `import localName = target;`
                AddResult(
                    context,
                    results,
                    addingDeclare,
                    _factory.CreateImportEqualsDeclaration(
                        /*modifiers*/ null,
                        /*isTypeOnly*/ false,
                        _factory.CreateIdentifier(localName),
                        isLocalImport
                            ? SymbolToName(target, context, SymbolFlags.All, /*expectsIdentifier*/ false)
                            : _factory.CreateExternalModuleReference(_factory.CreateStringLiteral(GetSpecifierForModuleSymbol(target, context)))),
                    isLocalImport ? modifierFlags : ModifierFlags.None);
                break;
            case SyntaxKind.NamespaceExportDeclaration:
                // export as namespace foo
                // TODO: Not part of a file's local or export symbol tables
                // Is bound into file.symbol.globalExports instead, which we don't currently traverse
                AddResult(context, results, addingDeclare, _factory.CreateNamespaceExportDeclaration(Utilities.IdText(((NamespaceExportDeclaration)node).Name!)), ModifierFlags.None);
                break;
            case SyntaxKind.ImportClause:
                {
                    var importClause = (ImportClause)node;
                    var generatedSpecifier = GetSpecifierForModuleSymbol(target.Parent ?? target, context); // generate specifier (even though we're reusing and existing one) for ambient module reference include side effects
                    var specifierExpression = context.Bundled
                        ? (Expression)_factory.CreateStringLiteral(generatedSpecifier)
                        : ((ImportDeclaration)importClause.Parent!).ModuleSpecifier;
                    var attributes = NodeTests.IsImportDeclaration(importClause.Parent!) ? ((ImportDeclaration)importClause.Parent!).Attributes : null;
                    var isTypeOnly = NodeTests.IsJSDocImportTag(importClause.Parent!);
                    context.ApproximateLength += 14 + localName.Length + 3 + (isTypeOnly ? 4 : 0); // `import localName from specifier;`, approximate specifier
                    AddResult(
                        context,
                        results,
                        addingDeclare,
                        _factory.CreateImportDeclaration(
                            /*modifiers*/ null,
                            _factory.CreateImportClause(
                                /* phaseModifier */ isTypeOnly ? (object?)SyntaxKind.TypeKeyword : null,
                                _factory.CreateIdentifier(localName),
                                /*namedBindings*/ null),
                            specifierExpression,
                            attributes),
                        ModifierFlags.None);
                    break;
                }
            case SyntaxKind.NamespaceImport:
                {
                    var namespaceImport = (NamespaceImport)node;
                    var generatedSpecifier = GetSpecifierForModuleSymbol(target.Parent ?? target, context); // generate specifier (even though we're reusing and existing one) for ambient module reference include side effects
                    var namespaceImportDeclaration = (ImportDeclaration)namespaceImport.Parent!.Parent!;
                    var specifierExpression = context.Bundled
                        ? (Expression)_factory.CreateStringLiteral(generatedSpecifier)
                        : namespaceImportDeclaration.ModuleSpecifier;
                    var isTypeOnly = NodeTests.IsJSDocImportTag(namespaceImport.Parent!.Parent!);
                    context.ApproximateLength += 19 + localName.Length + 3 + (isTypeOnly ? 4 : 0); // `import * as localName from specifier;`, approximate specifier
                    AddResult(
                        context,
                        results,
                        addingDeclare,
                        _factory.CreateImportDeclaration(
                            /*modifiers*/ null,
                            _factory.CreateImportClause(
                                /* phaseModifier */ isTypeOnly ? (object?)SyntaxKind.TypeKeyword : null,
                                /*name*/ null,
                                _factory.CreateNamespaceImport(_factory.CreateIdentifier(localName))),
                            specifierExpression,
                            ((ImportClause)node).Parent is ImportDeclaration outerImport ? outerImport.Attributes : null),
                        ModifierFlags.None);
                    break;
                }
            case SyntaxKind.NamespaceExport:
                context.ApproximateLength += 19 + localName.Length + 3; // `export * as localName from specifier;`, approximate specifier
                AddResult(
                    context,
                    results,
                    addingDeclare,
                    _factory.CreateExportDeclaration(
                        /*modifiers*/ null,
                        /*isTypeOnly*/ false,
                        _factory.CreateNamespaceExport(_factory.CreateIdentifier(localName)),
                        _factory.CreateStringLiteral(GetSpecifierForModuleSymbol(target, context)),
                        /*attributes*/ null),
                    ModifierFlags.None);
                break;
            case SyntaxKind.ImportSpecifier:
                {
                    var importSpecifier = (ImportSpecifier)node;
                    var generatedSpecifier = GetSpecifierForModuleSymbol(target.Parent ?? target, context); // generate specifier (even though we're reusing and existing one) for ambient module reference include side effects
                    var importSpecifierDeclaration = (ImportDeclaration)importSpecifier.Parent!.Parent!.Parent!;
                    var specifierExpression = context.Bundled
                        ? (Expression)_factory.CreateStringLiteral(generatedSpecifier)
                        : importSpecifierDeclaration.ModuleSpecifier;
                    var isTypeOnly = NodeTests.IsJSDocImportTag(importSpecifier.Parent!.Parent!.Parent!);
                    context.ApproximateLength += 19 + localName.Length + 3 + (isTypeOnly ? 4 : 0); // `import { localName } from specifier;`, approximate specifier
                    AddResult(
                        context,
                        results,
                        addingDeclare,
                        _factory.CreateImportDeclaration(
                            /*modifiers*/ null,
                            _factory.CreateImportClause(
                                /* phaseModifier */ isTypeOnly ? (object?)SyntaxKind.TypeKeyword : null,
                                /*name*/ null,
                                _factory.CreateNamedImports([
                                    _factory.CreateImportSpecifier(
                                        /*isTypeOnly*/ false,
                                        localName != verbatimTargetName ? _factory.CreateIdentifier(verbatimTargetName) : null,
                                        _factory.CreateIdentifier(localName)),
                                ])),
                            specifierExpression,
                            importSpecifierDeclaration.Attributes),
                        ModifierFlags.None);
                    break;
                }
            case SyntaxKind.ExportSpecifier:
                // does not use localName because the symbol name in this case refers to the name in the exports table,
                // which we must exactly preserve
                var exportSpecifier = (ExportSpecifier)node;
                var exportSpecifierDeclaration = (ExportDeclaration)exportSpecifier.Parent!.Parent!;
                var exportSpecifierModuleSpecifier = exportSpecifierDeclaration.ModuleSpecifier;
                if (exportSpecifierModuleSpecifier is not null)
                {
                    var propertyName = exportSpecifier.PropertyName;
                    if (propertyName is not null && Utilities.ModuleExportNameIsDefault(propertyName))
                    {
                        verbatimTargetName = InternalSymbolName.Default.Value;
                    }
                }
                // targetName is only used when the target is local, as otherwise the target is an alias that points at
                // another file
                SerializeExportSpecifier(
                    Utilities.UnescapeLeadingUnderscores(symbol.EscapedName!),
                    exportSpecifierModuleSpecifier is not null ? verbatimTargetName : targetName,
                    context,
                    results,
                    addingDeclare,
                    exportSpecifierModuleSpecifier is not null && Utilities.IsStringLiteralLike(exportSpecifierModuleSpecifier)
                        ? _factory.CreateStringLiteral(((LiteralExpression)exportSpecifierModuleSpecifier).Text)
                        : null);
                break;
            case SyntaxKind.ExportAssignment:
                SerializeMaybeAliasAssignment(symbol, context, results, addingDeclare, visitedSymbols, deferredPrivatesStack, enclosingDeclaration);
                break;
            case SyntaxKind.BinaryExpression:
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ElementAccessExpression:
                // Could be best encoded as though an export specifier or as though an export assignment
                // If name is default or export=, do an export assignment
                // Otherwise do an export specifier
                if (symbol.EscapedName == InternalSymbolName.Default.Value || symbol.EscapedName == InternalSymbolName.ExportEquals.Value)
                {
                    SerializeMaybeAliasAssignment(symbol, context, results, addingDeclare, visitedSymbols, deferredPrivatesStack, enclosingDeclaration);
                }
                else
                {
                    SerializeExportSpecifier(localName, targetName, context, results, addingDeclare);
                }
                break;
            default:
                Debug.FailBadSyntaxKind(node, "Unhandled alias declaration kind in symbol serializer!");
                break;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:10632-10647 — <c>serializeExportSpecifier</c>.</summary>
    internal void SerializeExportSpecifier(
        string localName,
        string targetName,
        NodeBuilderContext context,
        List<IStatement> results,
        bool addingDeclare,
        Expression? specifier = null)
    {
        context.ApproximateLength += 16 + localName.Length + (localName != targetName ? targetName.Length : 0); // `export { targetName as localName };`
        AddResult(
            context,
            results,
            addingDeclare,
            _factory.CreateExportDeclaration(
                /*modifiers*/ null,
                /*isTypeOnly*/ false,
                _factory.CreateNamedExports([
                    _factory.CreateExportSpecifier(/*isTypeOnly*/ false, localName != targetName ? targetName : null, localName),
                ]),
                specifier,
                /*attributes*/ null),
            ModifierFlags.None);
    }

    /// <summary>ported from: src/compiler/checker.ts:10648-10760 — <c>serializeMaybeAliasAssignment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS pushes the two export assignments straight into the frame's <c>results</c> (10691, 10737) instead
    /// of going through <c>addResult</c>; the port pushes into the <c>results</c> parameter the same way.
    /// PORT-NOTE: <c>getPropertyAssignmentAliasLikeExpression</c>'s argument union
    /// (<c>ShorthandPropertyAssignment | PropertyAssignment | PropertyAccessExpression</c>) has no C# type; the port
    /// passes the widened <see cref="Node"/> the reference's <c>as</c> produces.
    /// </remarks>
    internal bool SerializeMaybeAliasAssignment(
        ISymbol symbol,
        NodeBuilderContext context,
        List<IStatement> results,
        bool addingDeclare,
        HashSet<int> visitedSymbols,
        List<Dictionary<int, ISymbol>> deferredPrivatesStack,
        Node? enclosingDeclaration)
    {
        if ((symbol.Flags & SymbolFlags.Prototype) != 0)
        {
            return false;
        }
        var name = Utilities.UnescapeLeadingUnderscores(symbol.EscapedName!);
        var isExportEquals = name == InternalSymbolName.ExportEquals.Value;
        var isDefault = name == InternalSymbolName.Default.Value;
        var isExportAssignmentCompatibleSymbolName = isExportEquals || isDefault;
        // synthesize export = ref
        // ref should refer to either be a locally scoped symbol which we need to emit, or
        // a reference to another namespace/module which we may need to emit an `import` statement for
        var aliasDecl = symbol.Declarations is not null ? GetDeclarationOfAliasSymbol(symbol) : null;
        // serialize what the alias points to, preserve the declaration's initializer
        var target = aliasDecl is not null ? GetTargetOfAliasDeclaration(aliasDecl, /*dontRecursivelyResolve*/ true) : null;
        // If the target resolves and resolves to a thing defined in this file, emit as an alias, otherwise emit as a const
        if (target is not null
            && Core.Length(target.Declarations) != 0
            && Core.Some(target.Declarations, d => ReferenceEquals(Utilities.GetSourceFileOfNode((Node)d), Utilities.GetSourceFileOfNode(context.EnclosingDeclaration))))
        {
            // In case `target` refers to a namespace member, look at the declaration and serialize the leftmost symbol in it
            // eg, `namespace A { export class B {} }; exports = A.B;`
            // Technically, this is all that's required in the case where the assignment is an entity name expression
            Expression? expr = aliasDecl is not null
                ? NodeTests.IsExportAssignment((Node)aliasDecl) || NodeTests.IsBinaryExpression((Node)aliasDecl)
                    ? Utilities.GetExportAssignmentExpression((Node)aliasDecl)
                    : Utilities.GetPropertyAssignmentAliasLikeExpression((Node)aliasDecl)
                : null;
            var first = expr is not null && Utilities.IsEntityNameExpression(expr) ? GetFirstNonModuleExportsIdentifier(expr) : null;
            var referenced = first is not null ? ResolveEntityName(first, SymbolFlags.All, /*ignoreErrors*/ true, /*dontResolveAlias*/ true, context.EnclosingDeclaration) : null;
            if (referenced is not null || target is not null)
            {
                IncludePrivateSymbol(referenced ?? target, context, deferredPrivatesStack);
            }

            // We disable the context's symbol tracker for the duration of this name serialization
            // as, by virtue of being here, the name is required to print something, and we don't want to
            // issue a visibility error on it. Only anonymous classes that an alias points at _would_ issue
            // a visibility error here (as they're not visible within any scope), but we want to hoist them
            // into the containing scope anyway, so we want to skip the visibility checks.
            var prevDisableTrackSymbol = context.Tracker!.DisableTrackSymbol;
            context.Tracker.DisableTrackSymbol = true;
            if (isExportAssignmentCompatibleSymbolName)
            {
                context.ApproximateLength += 10; // `export = ;`
                results.Add(_factory.CreateExportAssignment(
                    /*modifiers*/ null,
                    isExportEquals,
                    SymbolToExpression(target!, context, SymbolFlags.All)));
            }
            else
            {
                if (ReferenceEquals(first, expr) && first is not null)
                {
                    // serialize as `export {target as name}`
                    SerializeExportSpecifier(name, Utilities.IdText(first), context, results, addingDeclare);
                }
                else if (expr is not null && NodeTests.IsClassExpression(expr))
                {
                    SerializeExportSpecifier(name, GetInternalSymbolName(target!, Utilities.SymbolName(target!), context), context, results, addingDeclare);
                }
                else
                {
                    // serialize as `import _Ref = t.arg.et; export { _Ref as name }`
                    var varName = GetUnusedName(name, symbol, context);
                    context.ApproximateLength += varName.Length + 10; // `import name = ;`
                    AddResult(
                        context,
                        results,
                        addingDeclare,
                        _factory.CreateImportEqualsDeclaration(
                            /*modifiers*/ null,
                            /*isTypeOnly*/ false,
                            _factory.CreateIdentifier(varName),
                            SymbolToName(target!, context, SymbolFlags.All, /*expectsIdentifier*/ false)),
                        ModifierFlags.None);
                    SerializeExportSpecifier(name, varName, context, results, addingDeclare);
                }
            }
            context.Tracker.DisableTrackSymbol = prevDisableTrackSymbol;
            return true;
        }
        else
        {
            // serialize as an anonymous property declaration
            var varName = GetUnusedName(name, symbol, context);
            // We have to use `getWidenedType` here since the object within a json file is unwidened within the file
            // (Unwidened types can only exist in expression contexts and should never be serialized)
            var typeToSerialize = GetWidenedType(GetTypeOfSymbol(GetMergedSymbol(symbol)));
            if (IsTypeRepresentableAsFunctionNamespaceMerge(typeToSerialize, symbol, context, enclosingDeclaration))
            {
                // If there are no index signatures and `typeToSerialize` is an object type, emit as a namespace instead of a const
                SerializeAsFunctionNamespaceMerge(typeToSerialize, symbol, varName, isExportAssignmentCompatibleSymbolName ? ModifierFlags.None : ModifierFlags.Export, context, results, addingDeclare, visitedSymbols, deferredPrivatesStack, enclosingDeclaration);
            }
            else
            {
                var flags = context.EnclosingDeclaration is { Kind: SyntaxKind.ModuleDeclaration } && ((symbol.Flags & SymbolFlags.Accessor) == 0 || (symbol.Flags & SymbolFlags.SetAccessor) != 0) ? NodeFlags.Let : NodeFlags.Const;
                context.ApproximateLength += varName.Length + 5; // `var name: ;`
                var statement = _factory.CreateVariableStatement(
                    /*modifiers*/ null,
                    _factory.CreateVariableDeclarationList(
                        [
                            _factory.CreateVariableDeclaration(
                                varName,
                                /*exclamationToken*/ null,
                                SerializeTypeForDeclaration(context, /*declaration*/ null, typeToSerialize, symbol),
                                /*initializer*/ null),
                        ],
                        flags));
                // Inlined JSON types exported with [module.]exports= will already emit an export=, so should use `declare`.
                // Otherwise, the type itself should be exported.
                AddResult(
                    context,
                    results,
                    addingDeclare,
                    statement,
                    target is not null && (target.Flags & SymbolFlags.Property) != 0 && target.EscapedName == InternalSymbolName.ExportEquals.Value ? ModifierFlags.Ambient
                        : name == varName ? ModifierFlags.Export
                        : ModifierFlags.None);
            }
            if (isExportAssignmentCompatibleSymbolName)
            {
                context.ApproximateLength += varName.Length + 10; // `export = name;`
                results.Add(_factory.CreateExportAssignment(
                    /*modifiers*/ null,
                    isExportEquals,
                    _factory.CreateIdentifier(varName)));
                return true;
            }
            else if (name != varName)
            {
                SerializeExportSpecifier(name, varName, context, results, addingDeclare);
                return true;
            }
            return false;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // isTypeRepresentableAsFunctionNamespaceMerge (checker.ts:10761-10786)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:10761-10786 — <c>isTypeRepresentableAsFunctionNamespaceMerge</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference reads its <c>enclosingDeclaration</c> frame local twice here (10766, 10772); the port
    /// reads <c>context.EnclosingDeclaration</c> (see the file remarks).
    /// </remarks>
    internal bool IsTypeRepresentableAsFunctionNamespaceMerge(IType typeToSerialize, ISymbol hostSymbol, NodeBuilderContext context, Node? enclosingDeclaration)
    {
        // Only object types which are not constructable, or indexable, whose members all come from the
        // context source file, and whose property names are all valid identifiers and not late-bound, _and_
        // whose input is not type annotated (if the input symbol has an annotation we can reuse, we should prefer it)
        var ctxSrc = Utilities.GetSourceFileOfNode(context.EnclosingDeclaration);
        var typeSymbol = typeToSerialize.Symbol;
        return (GetObjectFlags(typeToSerialize) & (ObjectFlags.Anonymous | ObjectFlags.Mapped)) != 0
            && !Core.Some(typeSymbol?.Declarations, d => Utilities.IsTypeNode((Node)d)) // If the type comes straight from a type node, we shouldn't try to break it up
            && Core.Length(GetIndexInfosOfType(typeToSerialize)) == 0
            && !IsClassInstanceSide(typeToSerialize) // While a class instance is potentially representable as a NS, prefer printing a reference to the instance type and serializing the class
            && (Core.Length(Core.Filter(GetPropertiesOfType(typeToSerialize), IsNamespaceMember)) != 0
                || Core.Length(GetSignaturesOfType(typeToSerialize, SignatureKind.Call)) != 0)
            && Core.Length(GetSignaturesOfType(typeToSerialize, SignatureKind.Construct)) == 0 // TODO: could probably serialize as function + ns + class, now that that's OK
            && GetDeclarationWithTypeAnnotation(hostSymbol, enclosingDeclaration) is null
            && !(typeSymbol is not null && Core.Some(typeSymbol.Declarations, d => !ReferenceEquals(Utilities.GetSourceFileOfNode((Node)d), ctxSrc)))
            && !Core.Some(GetPropertiesOfType(typeToSerialize), p => IsLateBoundName(p.EscapedName!))
            && !Core.Some(GetPropertiesOfType(typeToSerialize), p => Core.Some(p.Declarations, d => !ReferenceEquals(Utilities.GetSourceFileOfNode((Node)d), ctxSrc)))
            && Core.Every(GetPropertiesOfType(typeToSerialize), (p, _) =>
            {
                if (!ScannerUtilities.IsIdentifierText(Utilities.SymbolName(p), _languageVersion))
                {
                    return false;
                }
                if ((p.Flags & SymbolFlags.Accessor) == 0)
                {
                    return true;
                }
                return ReferenceEquals(GetNonMissingTypeOfSymbol(p), GetWriteTypeOfSymbol(p));
            });
    }

    // ---------------------------------------------------------------------------------------------
    // makeSerializePropertySymbol / modifiersLength / serializePropertySymbolForInterface (checker.ts:10787-10981)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:10787-10956 — <c>makeSerializePropertySymbol</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: TS declares three overloads differing in the <c>useAccessors</c> literal and the type parameter
    /// <c>T</c> from <c>createProperty</c>; the port has one method, because the reference has exactly two
    /// <c>createProperty</c> instantiations (10347's <c>factory.createPropertyDeclaration</c> and 10348's
    /// <c>factory.createPropertySignature</c>) and the <c>isClass</c> parameter selects between them — see
    /// <see cref="SerializePropertySymbolCallback"/>. The <c>createProperty</c> local function below is that
    /// selection.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>p.declarations &amp;&amp; forEach(p.declarations, …)</c> (10831) is the C# loop below — TS's
    /// <c>forEach</c> returns the first truthy callback result, and the port's <c>Core.ForEach</c> needs a
    /// reference-typed result. PORT-NOTE: <c>p.declarations?.find(isSetAccessor)</c>/<c>isGetAccessor</c> (10859, 10878)
    /// are <c>Utilities.IsSetAccessor</c>/<c>Utilities.IsGetAccessor</c> (utilitiesPublic.ts:2538/2542). PORT-NOTE:
    /// <c>factory.createToken(SyntaxKind.QuestionToken)</c> is <c>PunctuationToken</c> construction in the port, whose
    /// <c>CreateToken</c> returns the <c>Node</c> base.
    /// </para>
    /// </remarks>
    internal SerializePropertySymbolCallback MakeSerializePropertySymbol(bool isClass, SyntaxKind methodKind, bool useAccessors, NodeBuilderContext context)
    {
        return (p, isStatic, baseType) =>
        {
            var modifierFlags = Utilities.GetDeclarationModifierFlagsFromSymbol(p);
            var omitType = (modifierFlags & ModifierFlags.Private) != 0 && !IsExpanding(context);

            Node CreateProperty(List<ModifierToken>? modifiers, object? name, PunctuationToken? questionOrExclamationToken, ITypeNode? type, Expression? initializer)
                => isClass
                    ? _factory.CreatePropertyDeclaration(ToNodeModifiers(modifiers), name, questionOrExclamationToken, type, initializer)
                    : _factory.CreatePropertySignature(modifiers, name, questionOrExclamationToken, type);

            if (isStatic && (p.Flags & (SymbolFlags.Type | SymbolFlags.Namespace | SymbolFlags.Alias)) != 0)
            {
                // Only value-only-meaning symbols can be correctly encoded as class statics, type/namespace/alias meaning symbols
                // need to be merged namespace members
                return [];
            }
            if ((p.Flags & SymbolFlags.Prototype) != 0 || p.EscapedName == "constructor"
                || (baseType is not null && GetPropertyOfType(baseType, p.EscapedName) is not null
                    && IsReadonlySymbol(GetPropertyOfType(baseType, p.EscapedName)!) == IsReadonlySymbol(p)
                    && (p.Flags & SymbolFlags.Optional) == (GetPropertyOfType(baseType, p.EscapedName)!.Flags & SymbolFlags.Optional)
                    && IsTypeIdenticalTo(GetTypeOfSymbol(p), GetTypeOfPropertyOfType(baseType, p.EscapedName!)!)))
            {
                return [];
            }
            var flag = (modifierFlags & ~ModifierFlags.Async) | (isStatic ? ModifierFlags.Static : ModifierFlags.None);
            var name = GetPropertyNameNodeForSymbol(p, context);
            var firstPropertyLikeDecl = Core.Find(p.Declarations, (d, _) => NodeTests.IsPropertyDeclaration((Node)d)
                || Utilities.IsAccessor((Node)d)
                || NodeTests.IsVariableDeclaration((Node)d)
                || NodeTests.IsPropertySignature((Node)d)
                || NodeTests.IsBinaryExpression((Node)d)
                || NodeTests.IsPropertyAccessExpression((Node)d));
            if ((p.Flags & SymbolFlags.Accessor) != 0 && useAccessors)
            {
                var accessorResults = new List<Node>();
                if ((p.Flags & SymbolFlags.SetAccessor) != 0)
                {
                    Node? setter = null;
                    var declarations = p.Declarations;
                    if (declarations is not null)
                    {
                        foreach (var d in declarations)
                        {
                            if (((Node)d).Kind == SyntaxKind.SetAccessor)
                            {
                                setter = (Node)d;
                            }
                            else if (NodeTests.IsCallExpression((Node)d) && Utilities.IsBindableObjectDefinePropertyCall((CallExpression)d))
                            {
                                var properties = ((ObjectLiteralExpression)((CallExpression)d).Arguments![2]).Properties;
                                if (properties is not null)
                                {
                                    foreach (var propDecl in properties)
                                    {
                                        var id = Utilities.GetNameOfDeclaration(propDecl);
                                        if (id is not null && NodeTests.IsIdentifier((Node)id) && Utilities.IdText((Node)id) == "set")
                                        {
                                            setter = propDecl;
                                            break;
                                        }
                                    }
                                }
                            }
                            if (setter is not null)
                            {
                                break;
                            }
                        }
                    }

                    Debug.Assert(setter is not null);
                    var paramSymbol = Utilities.IsFunctionLikeDeclaration(setter!)
                        ? Core.ElementAt(GetSignatureFromDeclaration(setter!).Parameters, 0)
                        : null;
                    var setterDeclaration = Core.Find(p.Declarations, (d, _) => Utilities.IsSetAccessor((Node)d));
                    context.ApproximateLength += ModifiersLength(flag) + 7 + (paramSymbol is not null ? Utilities.SymbolName(paramSymbol).Length : 5) + (omitType ? 0 : 2); // `modifiers set name(param);`, approximate name
                    accessorResults.Add(SetTextRange(
                        context,
                        _factory.CreateSetAccessorDeclaration(
                            ToNodeModifiers(_factory.CreateModifiersFromModifierFlags(flag)),
                            name,
                            [
                                _factory.CreateParameterDeclaration(
                                    /*modifiers*/ null,
                                    /*dotDotDotToken*/ null,
                                    paramSymbol is not null ? ParameterToParameterDeclarationName(paramSymbol, GetEffectiveParameterDeclaration(paramSymbol), context) : "value",
                                    /*questionToken*/ null,
                                    omitType ? null : SerializeTypeForDeclaration(context, setterDeclaration, GetWriteTypeOfSymbol(p), p),
                                    /*initializer*/ null),
                            ],
                            /*body*/ null),
                        (Node?)(setterDeclaration ?? firstPropertyLikeDecl)));
                }
                if ((p.Flags & SymbolFlags.GetAccessor) != 0)
                {
                    var getterDeclaration = Core.Find(p.Declarations, (d, _) => Utilities.IsGetAccessor((Node)d));
                    context.ApproximateLength += ModifiersLength(flag) + 8 + (omitType ? 0 : 2); // `modifiers get name(): ;`, approximate name
                    accessorResults.Add(SetTextRange(
                        context,
                        _factory.CreateGetAccessorDeclaration(
                            ToNodeModifiers(_factory.CreateModifiersFromModifierFlags(flag)),
                            name,
                            [],
                            omitType ? null : SerializeTypeForDeclaration(context, getterDeclaration, GetTypeOfSymbol(p), p),
                            /*body*/ null),
                        (Node?)(getterDeclaration ?? firstPropertyLikeDecl)));
                }
                return accessorResults;
            }
            // This is an else/if as accessors and properties can't merge in TS, but might in JS
            // If this happens, we assume the accessor takes priority, as it imposes more constraints
            else if ((p.Flags & (SymbolFlags.Property | SymbolFlags.Variable | SymbolFlags.Accessor)) != 0)
            {
                var propertyModifierFlags = (IsReadonlySymbol(p) ? ModifierFlags.Readonly : ModifierFlags.None) | flag;
                context.ApproximateLength += 2 + (omitType ? 0 : 2) + ModifiersLength(propertyModifierFlags); // `modifiers name: ;`, approximate name
                return
                [
                    SetTextRange(
                        context,
                        CreateProperty(
                            _factory.CreateModifiersFromModifierFlags(propertyModifierFlags),
                            name,
                            (p.Flags & SymbolFlags.Optional) != 0 ? (PunctuationToken)_factory.CreateToken(SyntaxKind.QuestionToken) : null,
                            omitType ? null : SerializeTypeForDeclaration(context, Core.Find(p.Declarations, (d, _) => Utilities.IsSetAccessor((Node)d)), GetWriteTypeOfSymbol(p), p),
                            // TODO: https://github.com/microsoft/TypeScript/pull/32372#discussion_r328386357
                            // interface members can't have initializers, however class members _can_
                            /*initializer*/ null),
                        (Node?)(Core.Find(p.Declarations, (d, _) => NodeTests.IsPropertyDeclaration((Node)d) || NodeTests.IsVariableDeclaration((Node)d)) ?? firstPropertyLikeDecl)),
                ];
            }
            if ((p.Flags & (SymbolFlags.Method | SymbolFlags.Function)) != 0)
            {
                var type = GetTypeOfSymbol(p);
                var signatures = GetSignaturesOfType(type, SignatureKind.Call);
                if (omitType)
                {
                    var methodModifierFlags = (IsReadonlySymbol(p) ? ModifierFlags.Readonly : ModifierFlags.None) | flag;
                    context.ApproximateLength += 1 + ModifiersLength(methodModifierFlags); // `modifiers name;`, approximate name
                    Node? methodLocation = (Node?)Core.Find(p.Declarations, (d, _) => Utilities.IsFunctionLikeDeclaration((Node)d))
                        ?? Core.ElementAt(signatures, 0)?.Declaration
                        ?? (Node?)Core.ElementAt(p.Declarations, 0);
                    return
                    [
                        SetTextRange(
                            context,
                            CreateProperty(
                                _factory.CreateModifiersFromModifierFlags(methodModifierFlags),
                                name,
                                (p.Flags & SymbolFlags.Optional) != 0 ? (PunctuationToken)_factory.CreateToken(SyntaxKind.QuestionToken) : null,
                                /*type*/ null,
                                /*initializer*/ null),
                            methodLocation),
                    ];
                }

                var signatureResults = new List<Node>();
                foreach (var sig in signatures)
                {
                    context.ApproximateLength += 1; // ;
                    // Each overload becomes a separate method declaration, in order
                    var decl = SignatureToSignatureDeclarationHelper(
                        sig,
                        methodKind,
                        context,
                        new SignatureToSignatureDeclarationOptions
                        {
                            Name = name,
                            QuestionToken = (p.Flags & SymbolFlags.Optional) != 0 ? (PunctuationToken)_factory.CreateToken(SyntaxKind.QuestionToken) : null,
                            Modifiers = flag != ModifierFlags.None ? _factory.CreateModifiersFromModifierFlags(flag) : null,
                        });
                    var declaration = sig.Declaration;
                    var location = declaration is not null && Utilities.IsPrototypePropertyAssignment(declaration.Parent!) ? declaration.Parent : declaration;
                    signatureResults.Add(SetTextRange(context, decl, location));
                }
                return signatureResults;
            }
            // The `Constructor`'s symbol isn't in the class's properties lists, obviously, since it's a signature on the static
            return Debug.Fail<IList<Node>>($"Unhandled class member kind! {p.Flags}");
        };
    }

    /// <summary>ported from: src/compiler/checker.ts:10957-10977 — <c>modifiersLength</c>.</summary>
    internal static int ModifiersLength(ModifierFlags flags)
    {
        var result = 0;
        // Include the trailing space after the modifier keyword.
        if ((flags & ModifierFlags.Export) != 0) result += 7;
        if ((flags & ModifierFlags.Ambient) != 0) result += 8;
        if ((flags & ModifierFlags.Default) != 0) result += 8;
        if ((flags & ModifierFlags.Const) != 0) result += 6;
        if ((flags & ModifierFlags.Public) != 0) result += 7;
        if ((flags & ModifierFlags.Private) != 0) result += 8;
        if ((flags & ModifierFlags.Protected) != 0) result += 10;
        if ((flags & ModifierFlags.Abstract) != 0) result += 9;
        if ((flags & ModifierFlags.Static) != 0) result += 7;
        if ((flags & ModifierFlags.Override) != 0) result += 9;
        if ((flags & ModifierFlags.Readonly) != 0) result += 9;
        if ((flags & ModifierFlags.Accessor) != 0) result += 9;
        if ((flags & ModifierFlags.Async) != 0) result += 6;
        if ((flags & ModifierFlags.In) != 0) result += 3;
        if ((flags & ModifierFlags.Out) != 0) result += 4;
        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:10978-10981 — <c>serializePropertySymbolForInterface</c>.</summary>
    internal IList<Node> SerializePropertySymbolForInterface(ISymbol p, IType? baseType, NodeBuilderContext context)
        => MakeSerializePropertySymbol(/*isClass*/ false, SyntaxKind.MethodSignature, /*useAccessors*/ false, context)(p, /*isStatic*/ false, baseType);

    // ---------------------------------------------------------------------------------------------
    // serializeSignatures / serializeIndexSignatures / serializeBaseType / trySerializeAsTypeReference /
    // serializeImplementedType (checker.ts:10982-11095)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:10982-11035 — <c>serializeSignatures</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares the result as the union <c>CallSignatureDeclaration[] | ConstructSignatureDeclaration[]
    /// | ConstructorDeclaration[]</c> (the callers' <c>as</c> casts recover it); the port returns the shared
    /// <c>List&lt;Node&gt;</c> and the two callers cast to the element interface they need.
    /// </remarks>
    internal List<Node> SerializeSignatures(SignatureKind kind, IType input, IType? baseType, SyntaxKind outputKind, NodeBuilderContext context)
    {
        var signatures = GetSignaturesOfType(input, kind);
        if (kind == SignatureKind.Construct)
        {
            if (baseType is null && Core.Every(signatures, (s, _) => Core.Length(s.Parameters) == 0))
            {
                return []; // No base type, every constructor is empty - elide the extraneous `constructor()`
            }
            if (baseType is not null)
            {
                // If there is a base type, if every signature in the class is identical to a signature in the baseType, elide all the declarations
                var baseSigs = GetSignaturesOfType(baseType, SignatureKind.Construct);
                if (Core.Length(baseSigs) == 0 && Core.Every(signatures, (s, _) => Core.Length(s.Parameters) == 0))
                {
                    return []; // Base had no explicit signatures, if all our signatures are also implicit, return an empty list
                }
                if (baseSigs.Count == signatures.Count)
                {
                    var failed = false;
                    for (var i = 0; i < baseSigs.Count; i++)
                    {
                        if (CompareSignaturesIdentical(signatures[i], baseSigs[i], /*partialMatch*/ false, /*ignoreThisTypes*/ false, /*ignoreReturnTypes*/ true, CompareTypesIdentical) == Ternary.False)
                        {
                            failed = true;
                            break;
                        }
                    }
                    if (!failed)
                    {
                        return []; // Every signature was identical - elide constructor list as it is inherited
                    }
                }
            }
            var privateProtected = ModifierFlags.None;
            foreach (var s in signatures)
            {
                var signatureDeclaration = s.Declaration;
                if (signatureDeclaration is not null)
                {
                    privateProtected |= Utilities.GetSelectedEffectiveModifierFlags(signatureDeclaration, ModifierFlags.Private | ModifierFlags.Protected);
                }
            }
            if (privateProtected != ModifierFlags.None)
            {
                return
                [
                    SetTextRange(
                        context,
                        _factory.CreateConstructorDeclaration(
                            ToNodeModifiers(_factory.CreateModifiersFromModifierFlags(privateProtected)),
                            /*parameters*/ [],
                            /*body*/ null),
                        signatures[0].Declaration),
                ];
            }
        }

        var results = new List<Node>();
        foreach (var sig in signatures)
        {
            context.ApproximateLength += 1; // ;
            // Each overload becomes a separate constructor declaration, in order
            var decl = SignatureToSignatureDeclarationHelper(sig, outputKind, context);
            results.Add(SetTextRange(context, decl, sig.Declaration));
        }
        return results;
    }

    /// <summary>ported from: src/compiler/checker.ts:11036-11051 — <c>serializeIndexSignatures</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>getIndexInfosOfType</c> is the <c>List&lt;IIndexInfo&gt;</c> face in the port while
    /// <c>indexInfoToIndexSignatureDeclarationHelper</c> takes the model class, so the element is cast once.
    /// </remarks>
    internal List<IndexSignatureDeclaration> SerializeIndexSignatures(IType input, IType? baseType, NodeBuilderContext context)
    {
        var results = new List<IndexSignatureDeclaration>();
        foreach (var info in GetIndexInfosOfType(input))
        {
            if (baseType is not null)
            {
                var baseInfo = GetIndexInfoOfType(baseType, info.KeyType!);
                if (baseInfo is not null && IsTypeIdenticalTo(info.Type!, baseInfo.Type!))
                {
                    continue; // elide identical index signatures
                }
            }
            results.Add(IndexInfoToIndexSignatureDeclarationHelper((IndexInfo)info, context, /*typeNode*/ null));
        }
        return results;
    }

    /// <summary>ported from: src/compiler/checker.ts:11052-11067 — <c>serializeBaseType</c>.</summary>
    internal ExpressionWithTypeArguments SerializeBaseType(IType t, IType staticType, string rootName, NodeBuilderContext context, List<IStatement> results, bool addingDeclare)
    {
        var reference = TrySerializeAsTypeReference(t, SymbolFlags.Value, context);
        if (reference is not null)
        {
            return reference;
        }
        var tempName = GetUnusedName($"{rootName}_base", null, context);
        var statement = _factory.CreateVariableStatement(
            /*modifiers*/ null,
            _factory.CreateVariableDeclarationList(
                [
                    _factory.CreateVariableDeclaration(
                        tempName,
                        /*exclamationToken*/ null,
                        TypeToTypeNodeHelper(staticType, context),
                        /*initializer*/ null),
                ],
                NodeFlags.Const));
        AddResult(context, results, addingDeclare, statement, ModifierFlags.None);
        return _factory.CreateExpressionWithTypeArguments(_factory.CreateIdentifier(tempName), /*typeArguments*/ null);
    }

    /// <summary>ported from: src/compiler/checker.ts:11068-11085 — <c>trySerializeAsTypeReference</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>(t as TypeReference)</c> reads <c>target</c> off the <c>TypeReference</c> interface; the port's
    /// anchor is <see cref="ITypeReference"/> (its <c>GetTypeArguments</c> neighbour takes that face), so the test is a
    /// pattern match on it. PORT-NOTE: the enclosing declaration is <c>context.EnclosingDeclaration</c> (see the file
    /// remarks).
    /// </remarks>
    internal ExpressionWithTypeArguments? TrySerializeAsTypeReference(IType t, SymbolFlags flags, NodeBuilderContext context)
    {
        List<ITypeNode>? typeArgs = null;
        Expression? reference = null;

        // We don't use `isValueSymbolAccessible` below. since that considers alternative containers (like modules)
        // which we can't write out in a syntactically valid way as an expression
        var targetSymbol = (t as ITypeReference)?.Target?.Symbol;
        if (t is ITypeReference typeReference && targetSymbol is not null && IsSymbolAccessibleByFlags(targetSymbol, context.EnclosingDeclaration, flags))
        {
            typeArgs = Core.Map(GetTypeArguments(typeReference), (argumentType, _) => TypeToTypeNodeHelper(argumentType, context));
            reference = SymbolToExpression(targetSymbol, context, SymbolFlags.Type);
        }
        else
        {
            var symbol = t.Symbol;
            if (symbol is not null && IsSymbolAccessibleByFlags(symbol, context.EnclosingDeclaration, flags))
            {
                reference = SymbolToExpression(symbol, context, SymbolFlags.Type);
            }
        }
        if (reference is not null)
        {
            return _factory.CreateExpressionWithTypeArguments(reference, typeArgs);
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:11086-11095 — <c>serializeImplementedType</c>.</summary>
    internal ExpressionWithTypeArguments? SerializeImplementedType(IType t, NodeBuilderContext context)
    {
        var reference = TrySerializeAsTypeReference(t, SymbolFlags.Type, context);
        if (reference is not null)
        {
            return reference;
        }
        if (t.Symbol is not null)
        {
            return _factory.CreateExpressionWithTypeArguments(SymbolToExpression(t.Symbol, context, SymbolFlags.Type), /*typeArguments*/ null);
        }
        return null;
    }

    // ---------------------------------------------------------------------------------------------
    // getUnusedName / getNameCandidateWorker / getInternalSymbolName (checker.ts:11096-11148)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:11096-11118 — <c>getUnusedName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>id</c> is <c>SymbolId | undefined</c> where <c>getSymbolId</c> never answers 0, so the port
    /// uses <c>0</c> as the "no symbol" sentinel and keeps the reference's <c>if (id)</c> tests as <c>id != 0</c>.
    /// </remarks>
    internal string GetUnusedName(string input, ISymbol? symbol, NodeBuilderContext context)
    {
        var id = symbol is not null ? Utilities.GetSymbolId(symbol) : 0;
        if (id != 0)
        {
            if (context.RemappedSymbolNames!.ContainsKey(id))
            {
                return context.RemappedSymbolNames[id];
            }
        }
        if (symbol is not null)
        {
            input = GetNameCandidateWorker(symbol, input, context);
        }
        var i = 0;
        var original = input;
        while (context.UsedSymbolNames?.Contains(input) == true)
        {
            i++;
            input = $"{original}_{i}";
        }
        context.UsedSymbolNames?.Add(input);
        if (id != 0)
        {
            context.RemappedSymbolNames![id] = input;
        }
        return input;
    }

    /// <summary>ported from: src/compiler/checker.ts:11119-11136 — <c>getNameCandidateWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>localName.replace(/[^a-z0-9]/gi, "_")</c> is the character loop below — the reference's <c>i</c>
    /// flag makes the class <c>[A-Za-z0-9]</c>, and JS replaces per UTF-16 code unit, which is what iterating
    /// <c>char</c> does. It avoids a <c>System.Text.RegularExpressions</c> using for one substitution.
    /// </remarks>
    internal string GetNameCandidateWorker(ISymbol symbol, string localName, NodeBuilderContext context)
    {
        if (localName == InternalSymbolName.Default.Value || localName == InternalSymbolName.Class.Value || localName == InternalSymbolName.Function.Value)
        {
            var restoreFlags = SaveRestoreFlags(context);
            context.Flags |= NodeBuilderFlags.InInitialEntityName;
            var nameCandidate = GetNameOfSymbolAsWritten(symbol, context);
            restoreFlags();
            localName = nameCandidate.Length > 0 && Utilities.IsSingleOrDoubleQuote(nameCandidate[0])
                ? Utilities.StripQuotes(nameCandidate)
                : nameCandidate;
        }
        if (localName == InternalSymbolName.Default.Value)
        {
            localName = "_default";
        }
        else if (localName == InternalSymbolName.ExportEquals.Value)
        {
            localName = "_exports";
        }
        if (ScannerUtilities.IsIdentifierText(localName, _languageVersion) && !Utilities.IsStringANonContextualKeyword(localName))
        {
            return localName;
        }
        var escaped = new System.Text.StringBuilder(localName.Length + 1);
        escaped.Append('_');
        foreach (var ch in localName)
        {
            escaped.Append(ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' ? ch : '_');
        }
        return escaped.ToString();
    }

    /// <summary>ported from: src/compiler/checker.ts:11137-11148 — <c>getInternalSymbolName</c>.</summary>
    internal string GetInternalSymbolName(ISymbol symbol, string localName, NodeBuilderContext context)
    {
        var id = Utilities.GetSymbolId(symbol);
        if (context.RemappedSymbolNames!.ContainsKey(id))
        {
            return context.RemappedSymbolNames[id];
        }
        localName = GetNameCandidateWorker(symbol, localName, context);
        // The result of this is going to be used as the symbol's name - lock it in, so `getUnusedName` will also pick it up
        context.RemappedSymbolNames[id] = localName;
        return localName;
    }

    // ---------------------------------------------------------------------------------------------
    // isExpanding / isHashPrivate / getClonedHashPrivateName (checker.ts:11149-11166)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:11149-11152 — <c>isExpanding</c>.</summary>
    internal static bool IsExpanding(NodeBuilderContext context)
        => context.MaxExpansionDepth != -1;

    /// <summary>ported from: src/compiler/checker.ts:11153-11159 — <c>isHashPrivate</c>.</summary>
    internal static bool IsHashPrivate(ISymbol s)
    {
        // `valueDeclaration` could be undefined if inherited from
        // a union/intersection base type, but inherited properties
        // don't matter here.
        var valueDeclaration = s.ValueDeclaration;
        if (valueDeclaration is null)
        {
            return false;
        }
        var declaration = (Node)valueDeclaration;
        return Utilities.IsNamedDeclaration(declaration)
            && Utilities.GetNameOfDeclaration(declaration) is Node name
            && NodeTests.IsPrivateIdentifier(name);
    }

    /// <summary>ported from: src/compiler/checker.ts:11160-11166 — <c>getClonedHashPrivateName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads <c>s.valueDeclaration.name</c>, which C# exposes only on the concrete declaration classes;
    /// the port reads the same slot through <c>Utilities.GetNameOfDeclaration</c> and pattern-matches the
    /// <c>PrivateIdentifier</c> the reference's <c>isPrivateIdentifier</c> proves.
    /// </remarks>
    internal PrivateIdentifier? GetClonedHashPrivateName(ISymbol s)
    {
        var valueDeclaration = s.ValueDeclaration;
        if (valueDeclaration is not null
            && Utilities.IsNamedDeclaration((Node)valueDeclaration)
            && Utilities.GetNameOfDeclaration((Node)valueDeclaration) is PrivateIdentifier name)
        {
            return _factory.CloneNode(name);
        }
        return null;
    }
}
