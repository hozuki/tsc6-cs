namespace TypeScript.Compiler;

// ported from: src/compiler/binder.ts:887-930 (declareModuleMember)
// ported from: src/compiler/binder.ts:932-951 (jsdocTreatAsExported)
// ported from: src/compiler/binder.ts:2256-2262 (addToContainerChain)
// ported from: src/compiler/binder.ts:2264-2322 (declareSymbolAndAddToSymbolTable)
// ported from: src/compiler/binder.ts:2324-2334 (declareClassMember, declareSourceFileMember)
// ported from: src/compiler/binder.ts:2428-2435 (bindAnonymousDeclaration)
// ported from: src/compiler/binder.ts:2437-2456 (bindBlockScopedDeclaration)
// ported from: src/compiler/binder.ts:3606-3646 (bindClassLikeDeclaration, bindEnumDeclaration)
/// <summary>
/// Where a declared symbol lands: the container-relative members of TS's binder — <c>declareModuleMember</c>,
/// <c>declareSymbolAndAddToSymbolTable</c> and the block-scoped/anonymous declarations that reach them.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS reads <c>container.symbol</c>/<c>container.locals</c> off its <c>container</c> variable, whose type
/// (<c>IsContainer | EntityNameExpression</c>) guarantees those slots. The port's slot is a plain <see cref="Node"/>
/// (§12.9), so each read goes through the capability face that carries the slot —
/// <see cref="IDeclaration.Symbol"/> or <see cref="ILocalsContainer.Locals"/>.
/// <para>
/// PORT-NOTE: TS's <c>Debug.assertNode(container, canHaveLocals)</c> is <see cref="Debug.AssertNode"/> with the port's
/// predicate passed as a method group.
/// </para>
/// </remarks>
internal partial class Binder
{
    /// <summary>ported from: src/compiler/binder.ts:887-930 — <c>declareModuleMember</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS asks the combined modifier flags — <c>getCombinedModifierFlags</c> (utilitiesPublic.ts:662-664)
    /// walks up through a variable declaration list and statement, which is what makes <c>export var x</c> inside a
    /// namespace land in the container's exports. The port's utilities slice states only the syntactic entry point
    /// (Utilities.Binder.cs:671-675), so this call names the combined member the reference names; the same member
    /// serves <c>jsdocTreatAsExported</c> and <c>bindVariableDeclarationOrBindingElement</c> below.
    /// <para>
    /// PORT-NOTE: <c>node.localSymbol = local</c> writes <see cref="IDeclaration.LocalSymbol"/>.
    /// </para>
    /// </remarks>
    internal ISymbol DeclareModuleMember(IDeclaration node, SymbolFlags symbolFlags, SymbolFlags symbolExcludes)
    {
        var hasExportModifier = (Utilities.GetCombinedModifierFlags((Node)node) & ModifierFlags.Export) != 0 || JsdocTreatAsExported((Node)node);
        if ((symbolFlags & SymbolFlags.Alias) != 0)
        {
            if (node.Kind == SyntaxKind.ExportSpecifier || (node.Kind == SyntaxKind.ImportEqualsDeclaration && hasExportModifier))
            {
                return DeclareSymbolInContainerExports(node, symbolFlags, symbolExcludes);
            }
            else
            {
                Debug.AssertNode(_container, Utilities.CanHaveLocals);
                return DeclareSymbol(((ILocalsContainer)_container!).Locals!, /*parent*/ null, node, symbolFlags, symbolExcludes);
            }
        }
        else
        {
            // Exported module members are given 2 symbols: A local symbol that is classified with an ExportValue flag,
            // and an associated export symbol with all the correct flags set on it. There are 2 main reasons:
            //
            //   1. We treat locals and exports of the same name as mutually exclusive within a container.
            //      That means the binder will issue a Duplicate Identifier error if you mix locals and exports
            //      with the same name in the same container.
            //      TODO: Make this a more specific error and decouple it from the exclusion logic.
            //   2. When we checkIdentifier in the checker, we set its resolved symbol to the local symbol,
            //      but return the export symbol (by calling getExportSymbolOfValueSymbolIfExported). That way
            //      when the emitter comes back to it, it knows not to qualify the name if it was found in a containing scope.

            // NOTE: Nested ambient modules always should go to to 'locals' table to prevent their automatic merge
            //       during global merging in the checker. Why? The only case when ambient module is permitted inside another module is module augmentation
            //       and this case is specially handled. Module augmentations should only be merged with original module definition
            //       and should never be merged directly with other augmentation, and the latter case would be possible if automatic merge is allowed.
            if (Utilities.IsJSDocTypeAlias((Node)node)) Debug.Assert(Utilities.IsInJSFile((Node)node)); // We shouldn't add symbols for JSDoc nodes if not in a JS file.
            var container = _container!;
            // PORT-NOTE: the reference's operand is the *declaration* (`binder.ts:911`: `!isAmbientModule(node)`), not
            // the container — which is exactly what the NOTE just above implements: it is a *nested ambient module*
            // that must go to `locals` so that it never merges during global merging. Testing the container instead
            // made every export written inside any ambient module (`declare module "x" { export var y }`) land in
            // `locals` and left the module symbol's `exports` table empty (`isAmbientModule`, utilitiesPublic.ts:1201).
            if (!Utilities.IsAmbientModule((Node)node)
                && (hasExportModifier || (container.Flags & NodeFlags.ExportContext) != 0))
            {
                if (!Utilities.CanHaveLocals(container)
                    || ((ILocalsContainer)container).Locals is null
                    || (Utilities.HasSyntacticModifier((Node)node, ModifierFlags.Default) && GetDeclarationName(node) is null))
                {
                    // No local symbol for an unnamed default!
                    return DeclareSymbolInContainerExports(node, symbolFlags, symbolExcludes);
                }

                var exportKind = (symbolFlags & SymbolFlags.Value) != 0 ? SymbolFlags.ExportValue : SymbolFlags.None;
                var local = DeclareSymbol(((ILocalsContainer)container).Locals!, /*parent*/ null, node, exportKind, symbolExcludes);
                local.ExportSymbol = DeclareSymbolInContainerExports(node, symbolFlags, symbolExcludes);
                node.LocalSymbol = local;
                return local;
            }
            else
            {
                Debug.AssertNode(container, Utilities.CanHaveLocals);
                return DeclareSymbol(((ILocalsContainer)container).Locals!, /*parent*/ null, node, symbolFlags, symbolExcludes);
            }
        }
    }

    /// <summary>
    /// The <c>declareSymbol(container.symbol.exports!, container.symbol, …)</c> call <c>declareModuleMember</c> makes
    /// four times (binder.ts:890, 922, 926, 929); stated once because the port's <c>container</c> is a plain node and
    /// the two reads it needs (<c>symbol</c>, <c>symbol.exports</c>) would otherwise be spelled out four times.
    /// </summary>
    private ISymbol DeclareSymbolInContainerExports(IDeclaration node, SymbolFlags symbolFlags, SymbolFlags symbolExcludes)
    {
        var symbol = ((IDeclaration)_container!).Symbol!;
        return DeclareSymbol(symbol.Exports!, symbol, node, symbolFlags, symbolExcludes);
    }

    /// <summary>ported from: src/compiler/binder.ts:932-951 — <c>jsdocTreatAsExported</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>node.fullName</c> test sits behind <c>!isJSDocEnumTag(node)</c>; <c>fullName</c> is declared
    /// on two of the three members of the <c>JSDocTypeAlias</c> union and not on the enum tag, so the port reads it
    /// through a pattern over those two classes.
    /// <para>
    /// PORT-NOTE: <c>isTopLevelNamespaceAssignment</c> (binder.ts:3519-3523) belongs to the JS-expando slice (M5-4) and
    /// is called here by the reference name. Its parameter is
    /// <c>BindableAccessExpression</c> = <c>PropertyAccessExpression | PropertyAccessChain | ElementAccessExpression</c>;
    /// <c>IsPropertyAccessEntityNameExpression</c> guarantees the <see cref="PropertyAccessExpression"/> half, so that
    /// is the cast used — the narrowest type the reference's own guard proves.
    /// </para>
    /// </remarks>
    internal bool JsdocTreatAsExported(Node node)
    {
        if (node.Parent is not null && NodeTests.IsModuleDeclaration(node))
        {
            node = node.Parent;
        }

        if (!Utilities.IsJSDocTypeAlias(node)) return false;
        // jsdoc typedef handling is a bit of a doozy, but to summarize, treat the typedef as exported if:
        // 1. It has an explicit name (since by default typedefs are always directly exported, either at the top level or in a container), or
        var fullName = node switch
        {
            JSDocTypedefTag typedefTag => typedefTag.FullName,
            JSDocCallbackTag callbackTag => callbackTag.FullName,
            _ => null,
        };
        if (!NodeTests.IsJSDocEnumTag(node) && fullName is not null) return true;
        // 2. The thing a nameless typedef pulls its name from is implicitly a direct export (either by assignment or actual export flag).
        var declName = (Node?)Utilities.GetNameOfDeclaration(node);
        if (declName is null) return false;
        if (Utilities.IsPropertyAccessEntityNameExpression(declName.Parent!) && IsTopLevelNamespaceAssignment((PropertyAccessExpression)declName.Parent!)) return true;
        if (Utilities.IsDeclaration(declName.Parent!) && (Utilities.GetCombinedModifierFlags(declName.Parent!) & ModifierFlags.Export) != 0) return true;
        // This could potentially be simplified by having `delayedBindJSDocTypedefTag` pass in an override for `hasExportModifier`, since it should
        // already have calculated and branched on most of this.
        return false;
    }

    /// <summary>ported from: src/compiler/binder.ts:2256-2262 — <c>addToContainerChain</c>.</summary>
    /// <remarks>
    /// All container nodes are kept on a linked list in declaration order. This list is used by the
    /// getLocalNameOfContainer function in the type checker to validate that the local name used for a container is
    /// unique.
    /// </remarks>
    private void AddToContainerChain(ILocalsContainer next)
    {
        if (_lastContainer is not null)
        {
            // PORT-NOTE: TS's `nextContainer` slot is a `HasLocals`-typed node; the port's slot is `Node?` and the
            // capability face is not a class, so the cast is the one TS's structural typing made implicit.
            _lastContainer.NextContainer = (Node)next;
        }

        _lastContainer = next;
    }

    /// <summary>ported from: src/compiler/binder.ts:2264-2322 — <c>declareSymbolAndAddToSymbolTable</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>container</c> is <c>IsContainer | EntityNameExpression</c>; the port reads it as a
    /// <see cref="Node"/> and takes <c>symbol</c>/<c>locals</c> through the capability faces. TS's function falls off
    /// the end (answering <c>undefined</c>) for a container kind no branch matches, which is why the port's answer is
    /// nullable and the switch has no <c>default</c> arm.
    /// </remarks>
    internal ISymbol? DeclareSymbolAndAddToSymbolTable(IDeclaration node, SymbolFlags symbolFlags, SymbolFlags symbolExcludes)
    {
        var container = _container!;
        switch (container.Kind)
        {
            // Modules, source files, and classes need specialized handling for how their
            // members are declared (for example, a member of a class will go into a specific
            // symbol table depending on if it is static or not). We defer to specialized
            // handlers to take care of declaring these child members.
            case SyntaxKind.ModuleDeclaration:
                return DeclareModuleMember(node, symbolFlags, symbolExcludes);

            case SyntaxKind.SourceFile:
                return DeclareSourceFileMember(node, symbolFlags, symbolExcludes);

            case SyntaxKind.ClassExpression:
            case SyntaxKind.ClassDeclaration:
                return DeclareClassMember(node, symbolFlags, symbolExcludes);

            case SyntaxKind.EnumDeclaration:
                return DeclareSymbolInContainerExports(node, symbolFlags, symbolExcludes);

            case SyntaxKind.TypeLiteral:
            case SyntaxKind.JSDocTypeLiteral:
            case SyntaxKind.ObjectLiteralExpression:
            case SyntaxKind.InterfaceDeclaration:
            case SyntaxKind.JsxAttributes:
            {
                // Interface/Object-types always have their children added to the 'members' of
                // their container. They are only accessible through an instance of their
                // container, and are never in scope otherwise (even inside the body of the
                // object / type / interface declaring them). An exception is type parameters,
                // which are in scope without qualification (similar to 'locals').
                var symbol = ((IDeclaration)container).Symbol!;
                return DeclareSymbol(symbol.Members!, symbol, node, symbolFlags, symbolExcludes);
            }

            case SyntaxKind.FunctionType:
            case SyntaxKind.ConstructorType:
            case SyntaxKind.CallSignature:
            case SyntaxKind.ConstructSignature:
            case SyntaxKind.JSDocSignature:
            case SyntaxKind.IndexSignature:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.MethodSignature:
            case SyntaxKind.Constructor:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.JSDocFunctionType:
            case SyntaxKind.ClassStaticBlockDeclaration:
            case SyntaxKind.TypeAliasDeclaration:
            case SyntaxKind.MappedType:
            {
                // All the children of these container types are never visible through another
                // symbol (i.e. through another symbol's 'exports' or 'members').  Instead,
                // they're only accessed 'lexically' (i.e. from code that exists underneath
                // their container in the tree). To accomplish this, we simply add their declared
                // symbol to the 'locals' of the container.  These symbols can then be found as
                // the type checker walks up the containers, checking them for matching names.
                var locals = ((ILocalsContainer)container).Locals;
                if (locals is not null) Debug.AssertNode(container, Utilities.CanHaveLocals);
                return DeclareSymbol(locals!, /*parent*/ null, node, symbolFlags, symbolExcludes);
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/binder.ts:2324-2328 — <c>declareClassMember</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS asks <c>isStatic(node)</c> — the *member* being declared, not the class it is being declared in —
    /// which is what sends a `static` member into the container symbol's <c>exports</c> table and an instance member
    /// into its <c>members</c> table.
    /// </remarks>
    private ISymbol DeclareClassMember(IDeclaration node, SymbolFlags symbolFlags, SymbolFlags symbolExcludes)
    {
        var symbol = ((IDeclaration)_container!).Symbol!;
        return Utilities.IsStatic((Node)node)
            ? DeclareSymbol(symbol.Exports!, symbol, node, symbolFlags, symbolExcludes)
            : DeclareSymbol(symbol.Members!, symbol, node, symbolFlags, symbolExcludes);
    }

    /// <summary>ported from: src/compiler/binder.ts:2330-2334 — <c>declareSourceFileMember</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>isExternalModule(file)</c> is the parser's one-liner, already ported as
    /// <see cref="Parser.IsExternalModule"/> (parser.ts:1386-1388); <c>isExternalOrCommonJsModule</c> — the other
    /// two-slot test — is <see cref="Utilities.IsExternalOrCommonJsModule"/> and is used where TS uses that one.
    /// </remarks>
    private ISymbol DeclareSourceFileMember(IDeclaration node, SymbolFlags symbolFlags, SymbolFlags symbolExcludes)
        => Parser.IsExternalModule(_file)
            ? DeclareModuleMember(node, symbolFlags, symbolExcludes)
            : DeclareSymbol(_file.Locals!, /*parent*/ null, node, symbolFlags, symbolExcludes);

    /// <summary>ported from: src/compiler/binder.ts:2428-2435 — <c>bindAnonymousDeclaration</c>.</summary>
    private ISymbol BindAnonymousDeclaration(IDeclaration node, SymbolFlags symbolFlags, string name)
    {
        var symbol = CreateSymbol(symbolFlags, name);
        if ((symbolFlags & (SymbolFlags.EnumMember | SymbolFlags.ClassMember)) != 0)
        {
            symbol.Parent = ((IDeclaration)_container!).Symbol;
        }

        AddDeclarationToSymbol(symbol, node, symbolFlags);
        return symbol;
    }

    /// <summary>ported from: src/compiler/binder.ts:2437-2456 — <c>bindBlockScopedDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>case SourceFile:</c> falls through to <c>default</c>; C# has no implicit fall-through, so the
    /// shared tail is reached with <c>goto default</c>, as in <c>getContainerFlags</c> (Binder/Binder.cs).
    /// <para>
    /// PORT-NOTE: TS tests <c>blockScopeContainer.locals</c> and assigns it in the same breath; the port takes the
    /// table into a local, because a null-state analysis cannot follow a property across the assignment.
    /// </para>
    /// </remarks>
    internal void BindBlockScopedDeclaration(IDeclaration node, SymbolFlags symbolFlags, SymbolFlags symbolExcludes)
    {
        var blockScopeContainer = _blockScopeContainer!;
        switch (blockScopeContainer.Kind)
        {
            case SyntaxKind.ModuleDeclaration:
                DeclareModuleMember(node, symbolFlags, symbolExcludes);
                break;
            case SyntaxKind.SourceFile:
                // PORT-NOTE: the reference passes `container as SourceFile` (binder.ts:2443) — an unchecked type
                // assertion on the *container* field, which is not necessarily blockScopeContainer (the switch is on
                // blockScopeContainer). The port's `(SourceFile)_container!` was a real cast and threw for the JSDoc
                // carriers that reach here (jsdoc/callbackOnConstructor.ts), so the predicate states the assertion.
                if (_container is SourceFile containerFile && Utilities.IsExternalOrCommonJsModule(containerFile))
                {
                    DeclareModuleMember(node, symbolFlags, symbolExcludes);
                    break;
                }

                // falls through
                goto default;
            default:
            {
                Debug.AssertNode(blockScopeContainer, Utilities.CanHaveLocals);
                var localsContainer = (ILocalsContainer)blockScopeContainer;
                var locals = localsContainer.Locals;
                if (locals is null)
                {
                    locals = Utilities.CreateSymbolTable();
                    localsContainer.Locals = locals;
                    AddToContainerChain(localsContainer);
                }

                DeclareSymbol(locals, /*parent*/ null, node, symbolFlags, symbolExcludes);
                break;
            }
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:3606-3640 — <c>bindClassLikeDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>ClassLikeDeclaration</c> union (<c>ClassDeclaration | ClassExpression</c>);
    /// the port takes <see cref="IClassLikeDeclarationBase"/>, the capability face both classes declare.
    /// <c>IClassLikeDeclarationBase.Name</c> is <see cref="INamedDeclaration.Name"/>'s <c>Node?</c>, so the identifier
    /// reads are cast to <see cref="Identifier"/>.
    /// <para>
    /// PORT-NOTE: <c>symbolName(prototypeSymbol)</c> (utilitiesPublic.ts:870-872) is not ported; this call names it,
    /// and its body is <c>unescapeLeadingUnderscores(symbol.escapedName)</c>.
    /// </para>
    /// </remarks>
    internal void BindClassLikeDeclaration(IClassLikeDeclarationBase node)
    {
        var name = (Identifier?)node.Name;
        if (((Node)node).Kind == SyntaxKind.ClassDeclaration)
        {
            BindBlockScopedDeclaration((IDeclaration)node, SymbolFlags.Class, SymbolFlags.ClassExcludes);
        }
        else
        {
            var bindingName = name is not null ? Debug.CheckDefined(name.EscapedText, "a class expression name must have escaped text") : InternalSymbolName.Class.Value;
            BindAnonymousDeclaration((IDeclaration)node, SymbolFlags.Class, bindingName);
            // Add name of class expression into the map for semantic classifier
            if (name is not null)
            {
                _classifiableNames.Add(bindingName);
            }
        }

        var symbol = ((IDeclaration)node).Symbol!;

        // TypeScript 1.0 spec (April 2014): 8.4
        // Every class automatically contains a static property member named 'prototype', the
        // type of which is an instantiation of the class type with type Any supplied as a type
        // argument for each type parameter. It is an error to explicitly declare a static
        // property member with the name 'prototype'.
        //
        // Note: we check for this here because this class may be merging into a module.  The
        // module might have an exported variable called 'prototype'.  We can't allow that as
        // that would clash with the built-in 'prototype' for the class.
        var prototypeSymbol = CreateSymbol(SymbolFlags.Property | SymbolFlags.Prototype, "prototype");
        var prototypeName = prototypeSymbol.EscapedName!;
        if (symbol.Exports!.TryGetValue(prototypeName, out var symbolExport))
        {
            if (name is not null)
            {
                Utilities.SetParent(name, (Node)node);
            }

            _file.BindDiagnostics!.Add(CreateDiagnosticForNode(
                (Node)symbolExport.Declarations![0],
                Diagnostics.Duplicate_identifier_0,
                Utilities.SymbolName(prototypeSymbol)));
        }

        symbol.Exports![prototypeName] = prototypeSymbol;
        prototypeSymbol.Parent = symbol;
    }

    /// <summary>ported from: src/compiler/binder.ts:3642-3646 — <c>bindEnumDeclaration</c>.</summary>
    private void BindEnumDeclaration(EnumDeclaration node)
    {
        if (Utilities.IsEnumConst(node))
        {
            BindBlockScopedDeclaration(node, SymbolFlags.ConstEnum, SymbolFlags.ConstEnumExcludes);
        }
        else
        {
            BindBlockScopedDeclaration(node, SymbolFlags.RegularEnum, SymbolFlags.RegularEnumExcludes);
        }
    }
}
