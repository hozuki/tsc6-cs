namespace TypeScript.Compiler;

// ported from: src/compiler/binder.ts:2114-2124 (bindJSDocTypeAlias)
// ported from: src/compiler/binder.ts:2126-2132 (bindJSDocClassTag)
// ported from: src/compiler/binder.ts:2134-2142 (bindJSDocImportTag)
// ported from: src/compiler/binder.ts:2458-2524 (delayedBindJSDocTypedefTag)
// ported from: src/compiler/binder.ts:2526-2556 (bindJSDocImports)
/// <summary>
/// The JSDoc tag binders and the two deferral passes that run once the file's walk is over: a <c>@typedef</c>/
/// <c>@callback</c>/<c>@enum</c> name and a <c>@import</c>'s clause have to be bound against a container the walk has
/// not necessarily set up when the tag is reached, so the tags are queued (<c>_delayedTypeAliases</c>,
/// <c>_jsDocImports</c>) and bound here with the container state TS's two passes restore around each.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS types the delayed sets as the unions <c>JSDocTypedefTag | JSDocCallbackTag | JSDocEnumTag</c> and
/// <c>JSDocImportTag</c>; the port's first slot is the union's common base <see cref="JSDocTag"/> (Binder.State.cs:98)
/// and the two union-only slots — <c>fullName</c> and <c>typeExpression</c> — are read with a pattern over the members
/// that declare them, exactly as <c>jsdocTreatAsExported</c> (M5-1) reads <c>fullName</c>. The declaration faces the
/// two members need (<c>declareModuleMember</c>/<c>bindBlockScopedDeclaration</c> take <see cref="IDeclaration"/>) come
/// from the same cast: every member of that union implements it.
/// <para>
/// PORT-NOTE: TS's <c>getEnclosingBlockScopeContainer(host) || file</c> is, in the delayed-typedef pass, a dead
/// fallback — the port's helper answers non-nullable (it asserts the walk found a container,
/// utilities.ts:2329-2334) — so it is not repeated there; in the <c>@import</c> pass the enclosing container is only
/// asked for when the host exists, so the fallback stays.
/// </para>
/// <para>
/// PORT-NOTE: <c>bindPotentiallyMissingNamespaces</c> (binder.ts:3444-3468) and <c>isTopLevelNamespaceAssignment</c>
/// (binder.ts:3519-3523) are the module/expando slice's members (M5-4, <c>Binder.Expando.cs</c>) and are called here
/// by those names; their `Node` entity-name parameter matches every call site's argument shape.
/// </para>
/// <para>
/// PORT-NOTE: the comment slots the two tag binders walk are <c>string | NodeArray&lt;JSDocComment&gt;</c> upstream
/// and <c>object?</c> here (JSDocParser.cs:26-32) — a comment list is a <c>NodeArray&lt;Node&gt;</c> — so TS's
/// <c>typeof node.comment !== "string"</c> test is kept and the non-string value is narrowed with <c>as</c> rather
/// than a cast, because the absent comment (<c>undefined</c> upstream) also passes the test and reaches
/// <c>bindEach</c>, which tolerates it in both implementations.
/// </para>
/// </remarks>
internal partial class Binder
{
    /// <summary>ported from: src/compiler/binder.ts:2114-2124 — <c>bindJSDocTypeAlias</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>JSDocTypedefTag | JSDocCallbackTag | JSDocEnumTag</c> union, which the
    /// contract states as <see cref="JSDocTag"/> for its <c>bindChildren</c> arm; <c>fullName</c> is declared by the
    /// first two members only (the enum tag has none), so it is read with a pattern whose <c>_</c> arm is the enum
    /// tag's absent slot. The <c>node.kind !== JSDocEnumTag</c> guard is kept alongside it, exactly as upstream.
    /// </remarks>
    internal void BindJSDocTypeAlias(JSDocTag node)
    {
        Bind(node.TagName);
        var fullName = node switch
        {
            JSDocTypedefTag typedefTag => typedefTag.FullName,
            JSDocCallbackTag callbackTag => callbackTag.FullName,
            _ => null,
        };
        if (node.Kind != SyntaxKind.JSDocEnumTag && fullName is not null)
        {
            // don't bind the type name yet; that's delayed until delayedBindJSDocTypedefTag
            Utilities.SetParent(fullName, node);
            Utilities.SetParentRecursive(fullName, /*incremental*/ false);
        }
        if (node.Comment is not string)
        {
            BindEach(node.Comment as NodeArray<Node>);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:2126-2132 — <c>bindJSDocClassTag</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS passes <c>host.symbol</c> to <c>addDeclarationToSymbol</c> without a test because the signature
    /// requires a symbol; the guard above proves the host is a bound declaration, so the port asserts the slot the
    /// same way — <c>getHostSignatureFromJSDoc</c> answers the signature face, which reaches
    /// <see cref="IDeclaration.Symbol"/> through <see cref="INamedDeclaration"/>.
    /// </remarks>
    internal void BindJSDocClassTag(JSDocClassTag node)
    {
        BindEachChild(node);
        var host = Utilities.GetHostSignatureFromJSDoc(node);
        if (host is not null && host.Kind != SyntaxKind.MethodDeclaration)
        {
            AddDeclarationToSymbol(host.Symbol!, host, SymbolFlags.Class);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:2134-2142 — <c>bindJSDocImportTag</c>.</summary>
    internal void BindJSDocImportTag(JSDocImportTag node)
    {
        // don't bind the importClause yet; that's delayed until bindJSDocImports
        Bind(node.TagName);
        Bind(node.ModuleSpecifier);
        Bind(node.Attributes);

        if (node.Comment is not string)
        {
            BindEach(node.Comment as NodeArray<Node>);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:2458-2524 — <c>delayedBindJSDocTypedefTag</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>host = typeAlias.parent.parent</c> walks up through the tag's owning <c>JSDoc</c>; both
    /// parents are set by the walk before deferral, so the port asserts them. <c>createFlowNode(FlowFlags.Start, …)</c>
    /// is <see cref="FlowNode.CreateFlowNode"/> (Model/FlowNode.cs). The assignment-kind switch's <c>None</c> arm is
    /// TS's <c>return Debug.fail(...)</c>, which is <see cref="Debug.Fail"/> — non-returning, so the loop ends there
    /// rather than continuing with a stale container.
    /// </remarks>
    internal void DelayedBindJSDocTypedefTag()
    {
        if (_delayedTypeAliases is null)
        {
            return;
        }

        var saveContainer = _container;
        var saveLastContainer = _lastContainer;
        var saveBlockScopeContainer = _blockScopeContainer;
        var saveParent = _parent;
        var saveCurrentFlow = _currentFlow;
        foreach (var typeAlias in _delayedTypeAliases)
        {
            var host = typeAlias.Parent!.Parent!;
            _container = Utilities.GetEnclosingContainer(host) ?? _file;
            _blockScopeContainer = Utilities.GetEnclosingBlockScopeContainer(host);
            _currentFlow = FlowNode.CreateFlowNode(FlowFlags.Start, /*node*/ null, /*antecedent*/ null);
            _parent = typeAlias;
            Bind(typeAlias switch
            {
                JSDocTypedefTag typedefTag => typedefTag.TypeExpression,
                JSDocCallbackTag callbackTag => callbackTag.TypeExpression,
                JSDocEnumTag enumTag => enumTag.TypeExpression,
                _ => null,
            });
            var fullName = typeAlias switch
            {
                JSDocTypedefTag typedefTag => typedefTag.FullName,
                JSDocCallbackTag callbackTag => callbackTag.FullName,
                _ => null,
            };
            var declName = (Node?)Utilities.GetNameOfDeclaration(typeAlias);
            if ((NodeTests.IsJSDocEnumTag(typeAlias) || fullName is null) && declName is not null && Utilities.IsPropertyAccessEntityNameExpression(declName.Parent!))
            {
                // typedef anchored to an A.B.C assignment - we need to bind into B's namespace under name C
                var isTopLevel = IsTopLevelNamespaceAssignment((PropertyAccessExpression)declName.Parent!);
                if (isTopLevel)
                {
                    BindPotentiallyMissingNamespaces(
                        _file.Symbol,
                        declName.Parent!,
                        isTopLevel,
                        Utilities.FindAncestor(declName, d => NodeTests.IsPropertyAccessExpression(d) && Utilities.IdText(((PropertyAccessExpression)d).Name!) == "prototype") is not null,
                        /*containerIsClass*/ false);
                    var oldContainer = _container;
                    switch (Utilities.GetAssignmentDeclarationPropertyAccessKind(declName.Parent!))
                    {
                        case AssignmentDeclarationKind.ExportsProperty:
                        case AssignmentDeclarationKind.ModuleExports:
                            if (!Utilities.IsExternalOrCommonJsModule(_file))
                            {
                                _container = null;
                            }
                            else
                            {
                                _container = _file;
                            }
                            break;
                        case AssignmentDeclarationKind.ThisProperty:
                            _container = ((PropertyAccessExpression)declName.Parent!).Expression;
                            break;
                        case AssignmentDeclarationKind.PrototypeProperty:
                            _container = ((PropertyAccessExpression)((PropertyAccessExpression)declName.Parent!).Expression!).Name;
                            break;
                        case AssignmentDeclarationKind.Property:
                        {
                            var expression = ((PropertyAccessExpression)declName.Parent!).Expression!;
                            _container = IsExportsOrModuleExportsOrAlias(_file, expression)
                                ? _file
                                : expression is PropertyAccessExpression propertyAccess ? propertyAccess.Name
                                : expression;
                            break;
                        }
                        case AssignmentDeclarationKind.None:
                            Debug.Fail("Shouldn't have detected typedef or enum on non-assignment declaration");
                            break;
                    }
                    if (_container is not null)
                    {
                        DeclareModuleMember((IDeclaration)typeAlias, SymbolFlags.TypeAlias, SymbolFlags.TypeAliasExcludes);
                    }
                    _container = oldContainer;
                }
            }
            else if (NodeTests.IsJSDocEnumTag(typeAlias) || fullName is null || fullName.Kind == SyntaxKind.Identifier)
            {
                _parent = typeAlias.Parent;
                BindBlockScopedDeclaration((IDeclaration)typeAlias, SymbolFlags.TypeAlias, SymbolFlags.TypeAliasExcludes);
            }
            else
            {
                Bind(fullName);
            }
        }

        _container = saveContainer;
        _lastContainer = saveLastContainer;
        _blockScopeContainer = saveBlockScopeContainer;
        _parent = saveParent;
        _currentFlow = saveCurrentFlow;
    }

    /// <summary>ported from: src/compiler/binder.ts:2526-2556 — <c>bindJSDocImports</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS asks for the two enclosing containers only when the host exists (the ternary is on
    /// <c>host</c>), and the port keeps that shape; <c>|| file</c> stays because the absent host is a real
    /// <c>null</c> arm here.
    /// </remarks>
    internal void BindJSDocImports()
    {
        if (_jsDocImports is null)
        {
            return;
        }

        var saveContainer = _container;
        var saveLastContainer = _lastContainer;
        var saveBlockScopeContainer = _blockScopeContainer;
        var saveParent = _parent;
        var saveCurrentFlow = _currentFlow;

        foreach (var jsDocImportTag in _jsDocImports)
        {
            var host = Utilities.GetJSDocHost(jsDocImportTag);
            var enclosingContainer = host is not null ? Utilities.GetEnclosingContainer(host) : null;
            var enclosingBlockScopeContainer = host is not null ? Utilities.GetEnclosingBlockScopeContainer(host) : null;
            _container = enclosingContainer ?? _file;
            _blockScopeContainer = enclosingBlockScopeContainer ?? _file;
            _currentFlow = FlowNode.CreateFlowNode(FlowFlags.Start, /*node*/ null, /*antecedent*/ null);
            _parent = jsDocImportTag;
            Bind(jsDocImportTag.ImportClause);
        }

        _container = saveContainer;
        _lastContainer = saveLastContainer;
        _blockScopeContainer = saveBlockScopeContainer;
        _parent = saveParent;
        _currentFlow = saveCurrentFlow;
    }
}
