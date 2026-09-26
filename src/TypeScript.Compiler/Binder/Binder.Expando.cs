namespace TypeScript.Compiler;

// ported from: src/compiler/binder.ts:3201-3215 (bindObjectDefinePropertyExport)
// ported from: src/compiler/binder.ts:3217-3235 (bindExportsPropertyAssignment)
// ported from: src/compiler/binder.ts:3237-3261 (bindModuleExportsAssignment)
// ported from: src/compiler/binder.ts:3263-3265 (bindExportAssignedObjectMemberAlias)
// ported from: src/compiler/binder.ts:3267-3337 (bindThisPropertyAssignment)
// ported from: src/compiler/binder.ts:3339-3342 (bindDynamicallyNamedThisPropertyAssignment)
// ported from: src/compiler/binder.ts:3344-3348 (addLateBoundAssignmentDeclarationToSymbol)
// ported from: src/compiler/binder.ts:3350-3362 (bindSpecialPropertyDeclaration)
// ported from: src/compiler/binder.ts:3365-3369 (bindPrototypeAssignment)
// ported from: src/compiler/binder.ts:3371-3378 (bindObjectDefinePrototypeProperty)
// ported from: src/compiler/binder.ts:3384-3396 (bindPrototypePropertyAssignment)
// ported from: src/compiler/binder.ts:3398-3403 (bindObjectDefinePropertyAssignment)
// ported from: src/compiler/binder.ts:3405-3432 (bindSpecialPropertyAssignment)
// ported from: src/compiler/binder.ts:3438-3442 (bindStaticPropertyAssignment)
// ported from: src/compiler/binder.ts:3444-3468 (bindPotentiallyMissingNamespaces)
// ported from: src/compiler/binder.ts:3470-3517 (bindPotentiallyNewExpandoMemberToNamespace)
// ported from: src/compiler/binder.ts:3519-3523 (isTopLevelNamespaceAssignment)
// ported from: src/compiler/binder.ts:3525-3530 (bindPropertyAssignment)
// ported from: src/compiler/binder.ts:3532-3561 (the isExpandoSymbol comment block and body)
// ported from: src/compiler/binder.ts:3563-3568 (getParentOfBinaryExpression)
// ported from: src/compiler/binder.ts:3570-3578 (lookupSymbolForPropertyAccess)
// ported from: src/compiler/binder.ts:3580-3596 (forEachIdentifierInEntityName)
// ported from: src/compiler/binder.ts:3598-3604 (bindCallExpression)
/// <summary>
/// The JS dynamic-assignment and expando half of the binder: the CommonJS export forms
/// (<c>exports.x =</c>, <c>module.exports =</c>, <c>Object.defineProperty(exports, …)</c>), the <c>this.x =</c> / prototype
/// assignments, and the symbol-table lookups and declarations they share.
/// </summary>
/// <remarks>
/// PORT-NOTE: this is the half of TS's JS-expando support that lives in <c>createBinder</c>; the pure predicates it
/// asks about a tree (<c>getAssignmentDeclarationKind</c> and its companions) landed with M5-0 in
/// Utilities/Utilities.JsExpando.cs and are not re-ported here.
/// <para>
/// PORT-NOTE: TS's parameter aliases over node unions (<c>BindableObjectDefinePropertyCall</c>,
/// <c>BindableStaticPropertyAssignmentExpression</c>, <c>BindableStaticAccessExpression</c>,
/// <c>BindableStaticNameExpression</c>, <c>LiteralLikeElementAccessExpression</c>, <c>EntityNameExpression</c>) have no
/// C# counterpart, so a parameter TS types with one is <see cref="Node"/>/<see cref="Expression"/> here and the body
/// narrows with the same predicates the reference used — <c>getAssignmentDeclarationKind</c> has already classified
/// every call site of the <c>Bind…Assignment</c> members, which is the invariant each <c>PORT-NOTE</c> below records.
/// </para>
/// <para>
/// PORT-NOTE: members whose body reads no binder state keep TS's <c>getParentOfBinaryExpression</c>-style purity as
/// <c>static</c>; the rest are instance members because they touch <c>file</c>/<c>container</c>/<c>blockScopeContainer</c>.
/// All of them are <c>internal</c> because the dispatch (M5-5) and the JSDoc deferral set (M5-3) call them, the same
/// reason the state slots are (Binder.State.cs).
/// </para>
/// </remarks>
internal partial class Binder
{
    /// <summary>ported from: src/compiler/binder.ts:3201-3215 — <c>bindObjectDefinePropertyExport</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>forEachIdentifierInEntityName(node.arguments[0], undefined, (id, symbol) =&gt; …)</c> shadows
    /// the <c>symbol</c> it is assigning; C# forbids that (CS0136), so the callback's parameter is named
    /// <c>found</c>. The callback's answer is the third parameter of <see cref="ForEachIdentifierInEntityName"/>'s
    /// action, unused here and therefore discarded.
    /// </remarks>
    internal void BindObjectDefinePropertyExport(CallExpression node)
    {
        if (!SetCommonJsModuleIndicator(node))
        {
            return;
        }

        var symbol = ForEachIdentifierInEntityName(node.Arguments![0], /*parent*/ null, (id, found, _) =>
        {
            if (found is not null)
            {
                AddDeclarationToSymbol(found, (IDeclaration)id, SymbolFlags.Module | SymbolFlags.Assignment);
            }

            return found;
        });
        if (symbol is not null)
        {
            var flags = SymbolFlags.Property | SymbolFlags.ExportValue;
            DeclareSymbol(symbol.Exports!, symbol, node, flags, SymbolFlags.None);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:3217-3235 — <c>bindExportsPropertyAssignment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads <c>node.left.expression</c>, a member of the <c>BindableStaticPropertyAssignmentExpression</c>
    /// alias, through <see cref="Utilities.GetAccessExpressionExpression"/> — the read every access expression needs in
    /// the port. <c>getAssignmentDeclarationKind</c> has already established both halves of the alias (an
    /// <c>ExportsProperty</c> classification requires a bindable static access expression on the left), which is what
    /// makes the <see cref="IDeclaration"/> cast at the declaration the alias's declaration face.
    /// </remarks>
    internal void BindExportsPropertyAssignment(BinaryExpression node)
    {
        // When we create a property via 'exports.foo = bar', the 'exports.foo' property access
        // expression is the declaration
        if (!SetCommonJsModuleIndicator(node))
        {
            return;
        }

        var left = Utilities.GetAccessExpressionExpression(node.Left!);
        var symbol = ForEachIdentifierInEntityName(left, /*parent*/ null, (id, found, _) =>
        {
            if (found is not null)
            {
                AddDeclarationToSymbol(found, (IDeclaration)id, SymbolFlags.Module | SymbolFlags.Assignment);
            }

            return found;
        });
        if (symbol is not null)
        {
            var isAlias = Utilities.IsAliasableExpression(node.Right!)
                && (Utilities.IsExportsIdentifier(left) || Utilities.IsModuleExportsAccessExpression(left));
            var flags = isAlias ? SymbolFlags.Alias : SymbolFlags.Property | SymbolFlags.ExportValue;
            Utilities.SetParent(node.Left, node);
            DeclareSymbol(symbol.Exports!, symbol, (IDeclaration)node.Left!, flags, SymbolFlags.None);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:3237-3261 — <c>bindModuleExportsAssignment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>container === file</c> is a reference comparison in TS and <see cref="object.ReferenceEquals"/>
    /// here. The <c>every(properties, isShorthandPropertyAssignment)</c> becomes
    /// <see cref="Core.Every{T}(IList{T}?, Func{T, int, bool})"/>, whose callback receives the index TS's predicate does
    /// not, and the <c>forEach(properties, bindExportAssignedObjectMemberAlias)</c> becomes a loop with the cast the
    /// <c>every</c> test has just proved.
    /// </remarks>
    internal void BindModuleExportsAssignment(BinaryExpression node)
    {
        // A common practice in node modules is to set 'export = module.exports = {}', this ensures that 'exports'
        // is still pointing to 'module.exports'.
        // We do not want to consider this as 'export=' since a module can have only one of these.
        // Similarly we do not want to treat 'module.exports = exports' as an 'export='.
        if (!SetCommonJsModuleIndicator(node))
        {
            return;
        }

        var assignedExpression = Utilities.GetRightMostAssignedExpression(node.Right!);
        if (Utilities.IsEmptyObjectLiteral(assignedExpression)
            || (ReferenceEquals(_container, _file) && IsExportsOrModuleExportsOrAlias(_file, assignedExpression)))
        {
            return;
        }

        if (assignedExpression is ObjectLiteralExpression objectLiteral
            && Core.Every<Node>(objectLiteral.Properties, (p, _) => NodeTests.IsShorthandPropertyAssignment(p)))
        {
            foreach (var property in objectLiteral.Properties!)
            {
                BindExportAssignedObjectMemberAlias((ShorthandPropertyAssignment)property);
            }

            return;
        }

        // 'module.exports = expr' assignment
        var flags = Utilities.ExportAssignmentIsAlias(node)
            ? SymbolFlags.Alias // An export= with an EntityNameExpression or a ClassExpression exports all meanings of that identifier or class
            : SymbolFlags.Property | SymbolFlags.ExportValue | SymbolFlags.ValueModule;
        var symbol = DeclareSymbol(_file.Symbol!.Exports!, _file.Symbol, node, flags | SymbolFlags.Assignment, SymbolFlags.None);
        Utilities.SetValueDeclaration(symbol, node);
    }

    /// <summary>ported from: src/compiler/binder.ts:3263-3265 — <c>bindExportAssignedObjectMemberAlias</c>.</summary>
    internal void BindExportAssignedObjectMemberAlias(ShorthandPropertyAssignment node)
        => DeclareSymbol(_file.Symbol!.Exports!, _file.Symbol, node, SymbolFlags.Alias | SymbolFlags.Assignment, SymbolFlags.None);

    /// <summary>ported from: src/compiler/binder.ts:3267-3337 — <c>bindThisPropertyAssignment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the
    /// <c>BindablePropertyAssignmentExpression | PropertyAccessExpression | LiteralLikeElementAccessExpression</c> union;
    /// the port takes <see cref="Expression"/>, the only member the body needs (everything else it reads is a kind
    /// test and a cast), and the declaration face is taken at the two <c>declareSymbol</c> calls — every member of the
    /// union is declared as a declaration upstream. The two
    /// <c>isBinaryExpression(node) &amp;&amp; isPropertyAccessExpression(node.left)</c>-shaped tests become property
    /// patterns, which is the same conjunction without the double test.
    /// <para>
    /// PORT-NOTE: <c>thisContainer.symbol</c> and <c>thisContainer.parent</c> are read through
    /// <see cref="IDeclaration"/>; every kind the switch labels is a declaration. <c>l.expression.expression</c> is two
    /// <see cref="Utilities.GetAccessExpressionExpression"/> reads, because the port has no union whose member would
    /// type-check that chain.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>thisContainer.parent</c> for an accessor/method/constructor is dereferenced without a test;
    /// the port keeps TS's <c>!</c> for the class it binds into rather than inventing a null answer the reference
    /// cannot produce.
    /// </para>
    /// </remarks>
    internal void BindThisPropertyAssignment(Expression node)
    {
        Debug.Assert(Utilities.IsInJSFile(node));
        // private identifiers *must* be declared (even in JS files)
        var hasPrivateIdentifier = (node is BinaryExpression { Left: PropertyAccessExpression leftPropertyAccess }
                && NodeTests.IsPrivateIdentifier(leftPropertyAccess.Name!))
            || (node is PropertyAccessExpression propertyAccess && NodeTests.IsPrivateIdentifier(propertyAccess.Name!));
        if (hasPrivateIdentifier)
        {
            return;
        }

        var thisContainer = Utilities.GetThisContainer(node, /*includeArrowFunctions*/ false, /*includeClassComputedPropertyName*/ false);
        switch (thisContainer.Kind)
        {
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.FunctionExpression:
            {
                ISymbol? constructorSymbol = ((IDeclaration)thisContainer).Symbol;
                // For `f.prototype.m = function() { this.x = 0; }`, `this.x = 0` should modify `f`'s members, not the function expression.
                if (thisContainer.Parent is BinaryExpression containerParent
                    && containerParent.OperatorToken!.Kind == SyntaxKind.EqualsToken)
                {
                    var l = containerParent.Left!;
                    if (Utilities.IsBindableStaticAccessExpression(l) && Utilities.IsPrototypeAccess(Utilities.GetAccessExpressionExpression(l)))
                    {
                        constructorSymbol = LookupSymbolForPropertyAccess(
                            Utilities.GetAccessExpressionExpression(Utilities.GetAccessExpressionExpression(l)),
                            _thisParentContainer);
                    }
                }

                if (constructorSymbol is not null && constructorSymbol.ValueDeclaration is not null)
                {
                    // Declare a 'member' if the container is an ES5 class or ES6 constructor
                    constructorSymbol.Members ??= Utilities.CreateSymbolTable();
                    // It's acceptable for multiple 'this' assignments of the same identifier to occur
                    if (Utilities.HasDynamicName(node))
                    {
                        BindDynamicallyNamedThisPropertyAssignment(node, constructorSymbol, constructorSymbol.Members);
                    }
                    else
                    {
                        DeclareSymbol(constructorSymbol.Members, constructorSymbol, (IDeclaration)node, SymbolFlags.Property | SymbolFlags.Assignment, SymbolFlags.PropertyExcludes & ~SymbolFlags.Property);
                    }

                    AddDeclarationToSymbol(constructorSymbol, constructorSymbol.ValueDeclaration, SymbolFlags.Class);
                }

                break;
            }

            case SyntaxKind.Constructor:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.ClassStaticBlockDeclaration:
            {
                // this.foo assignment in a JavaScript class
                // Bind this property to the containing class
                var containingClass = thisContainer.Parent!;
                var containingClassSymbol = ((IDeclaration)containingClass).Symbol!;
                var symbolTable = Utilities.IsStatic(thisContainer) ? containingClassSymbol.Exports! : containingClassSymbol.Members!;
                if (Utilities.HasDynamicName(node))
                {
                    BindDynamicallyNamedThisPropertyAssignment(node, containingClassSymbol, symbolTable);
                }
                else
                {
                    DeclareSymbol(symbolTable, containingClassSymbol, (IDeclaration)node, SymbolFlags.Property | SymbolFlags.Assignment, SymbolFlags.None, /*isReplaceableByMethod*/ true);
                }

                break;
            }

            case SyntaxKind.SourceFile:
                // this.property = assignment in a source file -- declare symbol in exports for a module, in locals for a script
                if (Utilities.HasDynamicName(node))
                {
                    break;
                }
                else if (((SourceFile)thisContainer).CommonJsModuleIndicator is not null)
                {
                    DeclareSymbol(((IDeclaration)thisContainer).Symbol!.Exports!, ((IDeclaration)thisContainer).Symbol, (IDeclaration)node, SymbolFlags.Property | SymbolFlags.ExportValue, SymbolFlags.None);
                }
                else
                {
                    DeclareSymbolAndAddToSymbolTable((IDeclaration)node, SymbolFlags.FunctionScopedVariable, SymbolFlags.FunctionScopedVariableExcludes);
                }

                break;

            // Namespaces are not allowed in javascript files, so do nothing here
            case SyntaxKind.ModuleDeclaration:
                break;

            default:
                Debug.FailBadSyntaxKind(thisContainer);
                break;
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:3339-3342 — <c>bindDynamicallyNamedThisPropertyAssignment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>BinaryExpression | DynamicNamedDeclaration</c> union; only the declaration
    /// face is used, so the port takes <see cref="Node"/> and casts at the declaration — every member of the union is a
    /// declaration upstream.
    /// </remarks>
    internal void BindDynamicallyNamedThisPropertyAssignment(Node node, ISymbol symbol, Dictionary<string, ISymbol> symbolTable)
    {
        DeclareSymbol(symbolTable, symbol, (IDeclaration)node, SymbolFlags.Property, SymbolFlags.None, /*isReplaceableByMethod*/ true, /*isComputedName*/ true);
        AddLateBoundAssignmentDeclarationToSymbol(node, symbol);
    }

    /// <summary>ported from: src/compiler/binder.ts:3344-3348 — <c>addLateBoundAssignmentDeclarationToSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>(x || (x = new Map())).set(k, v)</c> becomes the null-coalescing assignment plus an indexer
    /// write; the slot's <c>Dictionary&lt;int, IDeclaration&gt;</c> is keyed by the same <c>getNodeId</c>.
    /// </remarks>
    internal static void AddLateBoundAssignmentDeclarationToSymbol(Node node, ISymbol? symbol)
    {
        if (symbol is not null)
        {
            var members = symbol.AssignmentDeclarationMembers ??= new Dictionary<int, IDeclaration>();
            members[Utilities.GetNodeId(node)] = (IDeclaration)node;
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:3350-3362 — <c>bindSpecialPropertyDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>PropertyAccessExpression | LiteralLikeElementAccessExpression</c> union
    /// after <c>isSpecialPropertyDeclaration</c>'s guard has excluded a non-literal element access, so both
    /// <c>node.expression</c> reads go through <see cref="Utilities.GetAccessExpressionExpression"/>.
    /// <para>
    /// PORT-NOTE: TS dereferences <c>node.parent.parent</c> without a test; the port's pattern makes the missing-parent
    /// case answer false instead of throwing, which is where TS's walk cannot get to.
    /// </para>
    /// </remarks>
    internal void BindSpecialPropertyDeclaration(MemberExpression node)
    {
        if (Utilities.GetAccessExpressionExpression(node).Kind == SyntaxKind.ThisKeyword)
        {
            BindThisPropertyAssignment(node);
        }
        else if (Utilities.IsBindableStaticAccessExpression(node) && node.Parent is { Parent: { Kind: SyntaxKind.SourceFile } } parent)
        {
            if (Utilities.IsPrototypeAccess(Utilities.GetAccessExpressionExpression(node)))
            {
                BindPrototypePropertyAssignment(node, parent);
            }
            else
            {
                BindStaticPropertyAssignment(node);
            }
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:3365-3369 — <c>bindPrototypeAssignment</c>.</summary>
    /// <remarks>For <c>x.prototype = { p, ... }</c>, declare members <c>p</c>,… if <c>x</c> is function/class/{}.</remarks>
    internal void BindPrototypeAssignment(BinaryExpression node)
    {
        Utilities.SetParent(node.Left, node);
        Utilities.SetParent(node.Right, node);
        BindPropertyAssignment(Utilities.GetAccessExpressionExpression(node.Left!), node.Left!, /*isPrototypeProperty*/ false, /*containerIsClass*/ true);
    }

    /// <summary>ported from: src/compiler/binder.ts:3371-3378 — <c>bindObjectDefinePrototypeProperty</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>(node.arguments[0] as PropertyAccessExpression).expression</c> is an erased assertion, not a
    /// runtime check — the argument an <c>ObjectDefinePrototypeProperty</c> classification admits may equally be an
    /// element access (<c>getAssignmentDeclarationKindWorker</c> tests
    /// <c>isBindableStaticAccessExpression(entityName)</c>) — so the port reads the expression through
    /// <see cref="Utilities.GetAccessExpressionExpression"/> rather than casting to the property-access class, which
    /// would throw where TS reads the same slot.
    /// </remarks>
    internal void BindObjectDefinePrototypeProperty(CallExpression node)
    {
        var namespaceSymbol = LookupSymbolForPropertyAccess(Utilities.GetAccessExpressionExpression(node.Arguments![0]));
        if (namespaceSymbol?.ValueDeclaration is not null)
        {
            // Ensure the namespace symbol becomes class-like
            AddDeclarationToSymbol(namespaceSymbol, namespaceSymbol.ValueDeclaration, SymbolFlags.Class);
        }

        BindPotentiallyNewExpandoMemberToNamespace(node, namespaceSymbol, /*isPrototypeProperty*/ true);
    }

    /// <summary>ported from: src/compiler/binder.ts:3384-3396 — <c>bindPrototypePropertyAssignment</c>.</summary>
    /// <remarks>
    /// For <c>x.prototype.y = z</c>, declare a member <c>y</c> on <c>x</c> if <c>x</c> is a function or class, or not
    /// declared. Note that jsdoc preceding an ExpressionStatement like <c>x.prototype.y;</c> is also treated as a
    /// declaration.
    /// <para>
    /// PORT-NOTE: TS's parameter is <c>BindableStaticAccessExpression</c>; the port takes <see cref="MemberExpression"/>,
    /// the base class both arms share, and reads <c>lhs.expression</c> and <c>classPrototype.expression</c> with
    /// <see cref="Utilities.GetAccessExpressionExpression"/>. The caller's <c>isPrototypeAccess</c> guard is what makes
    /// the second read's input an access expression.
    /// </para>
    /// </remarks>
    internal void BindPrototypePropertyAssignment(MemberExpression lhs, Node parent)
    {
        // Look up the function in the local scope, since prototype assignments should
        // follow the function declaration
        var classPrototype = Utilities.GetAccessExpressionExpression(lhs);
        var constructorFunction = Utilities.GetAccessExpressionExpression(classPrototype);

        // Fix up parent pointers since we're going to use these nodes before we bind into them
        Utilities.SetParent(constructorFunction, classPrototype);
        Utilities.SetParent(classPrototype, lhs);
        Utilities.SetParent(lhs, parent);

        BindPropertyAssignment(constructorFunction, lhs, /*isPrototypeProperty*/ true, /*containerIsClass*/ true);
    }

    /// <summary>ported from: src/compiler/binder.ts:3398-3403 — <c>bindObjectDefinePropertyAssignment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS dereferences <c>node.parent.parent</c> without a test; the port's pattern answers false for a
    /// missing parent, the state the bound tree cannot be in.
    /// </remarks>
    internal void BindObjectDefinePropertyAssignment(CallExpression node)
    {
        var namespaceSymbol = LookupSymbolForPropertyAccess(node.Arguments![0]);
        var isToplevel = node.Parent?.Parent is { Kind: SyntaxKind.SourceFile };
        namespaceSymbol = BindPotentiallyMissingNamespaces(namespaceSymbol, node.Arguments[0], isToplevel, /*isPrototypeProperty*/ false, /*containerIsClass*/ false);
        BindPotentiallyNewExpandoMemberToNamespace(node, namespaceSymbol, /*isPrototypeProperty*/ false);
    }

    /// <summary>ported from: src/compiler/binder.ts:3405-3432 — <c>bindSpecialPropertyAssignment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>node.left.expression</c> is read once into <c>left</c> through
    /// <see cref="Utilities.GetAccessExpressionExpression"/> (the alias has no C# anchor);
    /// <c>getAssignmentDeclarationKind</c> has already required an access expression on the left.
    /// <para>
    /// PORT-NOTE: <c>lookupSymbolForName(...)?.flags! &amp; SymbolFlags.Alias</c> is a numbered truthiness test in TS;
    /// the port states it as a flag-set test over the optional flags. The trailing <c>cast(node.left,
    /// isBindableStaticNameExpression)</c> is erased in TS, so the port passes the same node to
    /// <see cref="BindStaticPropertyAssignment"/>'s <see cref="Node"/> parameter, where its own assertion and the
    /// declared <c>Expression</c> read apply.
    /// </para>
    /// </remarks>
    internal void BindSpecialPropertyAssignment(BinaryExpression node)
    {
        var left = Utilities.GetAccessExpressionExpression(node.Left!);
        // Class declarations in Typescript do not allow property declarations
        var parentSymbol = LookupSymbolForPropertyAccess(left, _blockScopeContainer) ?? LookupSymbolForPropertyAccess(left, _container);
        if (!Utilities.IsInJSFile(node) && !Utilities.IsFunctionSymbol(parentSymbol))
        {
            return;
        }

        var rootExpr = Utilities.GetLeftmostAccessExpression(left);
        if (NodeTests.IsIdentifier(rootExpr)
            && ((LookupSymbolForName(_container!, ((Identifier)rootExpr).EscapedText)?.Flags ?? SymbolFlags.None) & SymbolFlags.Alias) != 0)
        {
            return;
        }

        // Fix up parent pointers since we're going to use these nodes before we bind into them
        Utilities.SetParent(node.Left, node);
        Utilities.SetParent(node.Right, node);
        if (NodeTests.IsIdentifier(left) && ReferenceEquals(_container, _file) && IsExportsOrModuleExportsOrAlias(_file, left))
        {
            // This can be an alias for the 'exports' or 'module.exports' names, e.g.
            //    var util = module.exports;
            //    util.property = function ...
            BindExportsPropertyAssignment(node);
        }
        else if (Utilities.HasDynamicName(node))
        {
            BindAnonymousDeclaration(node, SymbolFlags.Property | SymbolFlags.Assignment, InternalSymbolName.Computed.Value);
            var sym = BindPotentiallyMissingNamespaces(parentSymbol, left, IsTopLevelNamespaceAssignment(node.Left!), /*isPrototypeProperty*/ false, /*containerIsClass*/ false);
            AddLateBoundAssignmentDeclarationToSymbol(node, sym);
        }
        else
        {
            BindStaticPropertyAssignment(node.Left!);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:3438-3442 — <c>bindStaticPropertyAssignment</c>.</summary>
    /// <remarks>
    /// For nodes like <c>x.y = z</c>, declare a member 'y' on 'x' if x is a function (or IIFE) or class or {}, or not
    /// declared. Also works for expression statements preceded by JSDoc, like <c>/** @type number */ x.y;</c>
    /// <para>
    /// PORT-NOTE: TS's parameter is <c>BindableStaticNameExpression</c> and its assertion excludes the identifier arm,
    /// leaving an access expression; the port takes <see cref="Node"/>, keeps the same
    /// <see cref="Debug.Assert(bool, string?, Func{string}?)"/> and reads the expression through
    /// <see cref="Utilities.GetAccessExpressionExpression"/>.
    /// </para>
    /// </remarks>
    internal void BindStaticPropertyAssignment(Node node)
    {
        Debug.Assert(!NodeTests.IsIdentifier(node));
        var expression = Utilities.GetAccessExpressionExpression(node);
        Utilities.SetParent(expression, node);
        BindPropertyAssignment(expression, node, /*isPrototypeProperty*/ false, /*containerIsClass*/ false);
    }

    /// <summary>ported from: src/compiler/binder.ts:3444-3468 — <c>bindPotentiallyMissingNamespaces</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>namespaceSymbol?.flags! &amp; SymbolFlags.Alias</c> is a flag-set test over the optional symbol.
    /// <c>parent ? parent.exports! : file.jsGlobalAugmentations || (file.jsGlobalAugmentations = createSymbolTable())</c>
    /// becomes a conditional plus the null-coalescing assignment; the callback's first parameter is TS's
    /// <c>Declaration</c> and is cast at the two members that need that face.
    /// </remarks>
    internal ISymbol? BindPotentiallyMissingNamespaces(ISymbol? namespaceSymbol, Node entityName, bool isToplevel, bool isPrototypeProperty, bool containerIsClass)
    {
        if (namespaceSymbol is not null && (namespaceSymbol.Flags & SymbolFlags.Alias) != 0)
        {
            return namespaceSymbol;
        }

        if (isToplevel && !isPrototypeProperty)
        {
            // make symbols or add declarations for intermediate containers
            var flags = SymbolFlags.Module | SymbolFlags.Assignment;
            var excludeFlags = SymbolFlags.ValueModuleExcludes & ~SymbolFlags.Assignment;
            namespaceSymbol = ForEachIdentifierInEntityName(entityName, namespaceSymbol, (id, found, parent) =>
            {
                if (found is not null)
                {
                    AddDeclarationToSymbol(found, (IDeclaration)id, flags);
                    return found;
                }

                var table = parent is not null
                    ? parent.Exports!
                    : _file.JsGlobalAugmentations ??= Utilities.CreateSymbolTable();
                return DeclareSymbol(table, parent, (IDeclaration)id, flags, excludeFlags);
            });
        }

        if (containerIsClass && namespaceSymbol is not null && namespaceSymbol.ValueDeclaration is not null)
        {
            AddDeclarationToSymbol(namespaceSymbol, namespaceSymbol.ValueDeclaration, SymbolFlags.Class);
        }

        return namespaceSymbol;
    }

    /// <summary>ported from: src/compiler/binder.ts:3470-3517 — <c>bindPotentiallyNewExpandoMemberToNamespace</c>.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>BindableStaticAccessExpression | CallExpression</c> union; only the
    /// <c>CallExpression</c> arm is read beyond the declaration face, so the port takes <see cref="Node"/>.
    /// <para>
    /// PORT-NOTE: <c>declaration.arguments[2].properties</c> is read off TS's tuple-typed alias
    /// <c>BindableObjectDefinePropertyCall</c>, i.e. without a check that the third argument is an object literal. The
    /// port matches the third argument on its kind: TS's unchecked read of a non-literal would leave
    /// <c>properties</c> undefined, both <c>some</c> calls would answer false, and the fallback below would then set
    /// <c>SymbolFlags.Property</c> — which is exactly what the unmatched branch here does.
    /// </para>
    /// <para>
    /// PORT-NOTE: the two <c>some(properties, p =&gt; { const id = getNameOfDeclaration(p); return !!id &amp;&amp;
    /// isIdentifier(id) &amp;&amp; idText(id) === "…"; })</c> callbacks become name patterns over
    /// <see cref="Utilities.GetNameOfDeclaration"/> and the same <c>idText</c> read.
    /// </para>
    /// </remarks>
    internal void BindPotentiallyNewExpandoMemberToNamespace(Node declaration, ISymbol? namespaceSymbol, bool isPrototypeProperty)
    {
        if (namespaceSymbol is null || !IsExpandoSymbol(namespaceSymbol))
        {
            return;
        }

        // Set up the members collection if it doesn't exist already
        var symbolTable = isPrototypeProperty
            ? namespaceSymbol.Members ??= Utilities.CreateSymbolTable()
            : namespaceSymbol.Exports ??= Utilities.CreateSymbolTable();

        var includes = SymbolFlags.None;
        var excludes = SymbolFlags.None;
        // Method-like
        var initializer = Utilities.GetAssignedExpandoInitializer(declaration);
        if (initializer is not null && Utilities.IsFunctionLikeDeclaration(initializer))
        {
            includes = SymbolFlags.Method;
            excludes = SymbolFlags.MethodExcludes;
        }
        // Maybe accessor-like
        else if (declaration is CallExpression callExpression
            && Utilities.IsBindableObjectDefinePropertyCall(callExpression)
            && callExpression.Arguments![2] is ObjectLiteralExpression descriptor)
        {
            if (Core.Some<Node>(descriptor.Properties, p => Utilities.GetNameOfDeclaration(p) is Identifier id && Utilities.IdText(id) == "set"))
            {
                // We mix in `SymbolFLags.Property` so in the checker `getTypeOfVariableParameterOrProperty` is used for this
                // symbol, instead of `getTypeOfAccessor` (which will assert as there is no real accessor declaration)
                includes |= SymbolFlags.SetAccessor | SymbolFlags.Property;
                excludes |= SymbolFlags.SetAccessorExcludes;
            }

            if (Core.Some<Node>(descriptor.Properties, p => Utilities.GetNameOfDeclaration(p) is Identifier id && Utilities.IdText(id) == "get"))
            {
                includes |= SymbolFlags.GetAccessor | SymbolFlags.Property;
                excludes |= SymbolFlags.GetAccessorExcludes;
            }
        }

        if (includes == SymbolFlags.None)
        {
            includes = SymbolFlags.Property;
            excludes = SymbolFlags.PropertyExcludes;
        }

        DeclareSymbol(symbolTable, namespaceSymbol, (IDeclaration)declaration, includes | SymbolFlags.Assignment, excludes & ~SymbolFlags.Assignment);
    }

    /// <summary>ported from: src/compiler/binder.ts:3519-3523 — <c>isTopLevelNamespaceAssignment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>BindableAccessExpression</c> union
    /// (<c>PropertyAccessExpression | PropertyAccessChain | ElementAccessExpression</c>); the body reads only
    /// <c>propertyAccess.parent</c>, and two of its three call sites pass a <c>BindableStaticAccessExpression</c> that
    /// may be an element access (<c>bindPropertyAssignment</c>, <c>bindSpecialPropertyAssignment</c>), so the port
    /// states the honest upper bound <see cref="Node"/> instead. The M5-1 call site
    /// (<c>jsdocTreatAsExported</c>) passes a <c>PropertyAccessExpression</c>, which is a <see cref="Node"/>.
    /// <para>
    /// PORT-NOTE: TS dereferences <c>propertyAccess.parent.parent</c> and the result of
    /// <see cref="GetParentOfBinaryExpression"/> without tests; the port's patterns answer false for the absent-parent
    /// states TS cannot reach from a bound tree.
    /// </para>
    /// </remarks>
    internal static bool IsTopLevelNamespaceAssignment(Node propertyAccess)
        => propertyAccess.Parent is BinaryExpression binary
            ? GetParentOfBinaryExpression(binary)?.Parent is { Kind: SyntaxKind.SourceFile }
            : propertyAccess.Parent?.Parent is { Kind: SyntaxKind.SourceFile };

    /// <summary>ported from: src/compiler/binder.ts:3525-3530 — <c>bindPropertyAssignment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: both parameters are union aliases with no C# anchor
    /// (<c>BindableStaticNameExpression</c> and <c>BindableStaticAccessExpression</c>); the port takes
    /// <see cref="Node"/> and reads <c>propertyAccess.expression</c> through
    /// <see cref="Utilities.GetAccessExpressionExpression"/>. <c>lookupSymbolForPropertyAccess(name,
    /// blockScopeContainer)</c> passes a possibly-null container, which the callee's default resolves to
    /// <c>container</c> — the same thing TS's default parameter does for an explicit <c>undefined</c>.
    /// </remarks>
    internal void BindPropertyAssignment(Node name, Node propertyAccess, bool isPrototypeProperty, bool containerIsClass)
    {
        var namespaceSymbol = LookupSymbolForPropertyAccess(name, _blockScopeContainer) ?? LookupSymbolForPropertyAccess(name, _container);
        var isToplevel = IsTopLevelNamespaceAssignment(propertyAccess);
        namespaceSymbol = BindPotentiallyMissingNamespaces(namespaceSymbol, Utilities.GetAccessExpressionExpression(propertyAccess), isToplevel, isPrototypeProperty, containerIsClass);
        BindPotentiallyNewExpandoMemberToNamespace(propertyAccess, namespaceSymbol, isPrototypeProperty);
    }

    /// <summary>ported from: src/compiler/binder.ts:3532-3561 — <c>isExpandoSymbol</c>.</summary>
    /// <remarks>
    /// Javascript expando values are:
    /// - Functions
    /// - classes
    /// - namespaces
    /// - variables initialized with function expressions
    /// -                       with class expressions
    /// -                       with empty object literals
    /// -                       with non-empty object literals if assigned to the prototype property
    /// <para>
    /// PORT-NOTE: TS's <c>init &amp;&amp; getRightMostAssignedExpression(init)</c> is a truthiness test of a node, so the
    /// port keeps it as a null test. The declared <c>init</c>/<c>node!</c> reads are a pattern switch over the three
    /// kinds TS narrows with <c>isVariableDeclaration</c>/<c>isBinaryExpression</c>.
    /// </para>
    /// </remarks>
    internal static bool IsExpandoSymbol(ISymbol symbol)
    {
        if ((symbol.Flags & (SymbolFlags.Function | SymbolFlags.Class | SymbolFlags.NamespaceModule)) != 0)
        {
            return true;
        }

        var node = (Node?)symbol.ValueDeclaration;
        if (node is not null && NodeTests.IsCallExpression(node))
        {
            return Utilities.GetAssignedExpandoInitializer(node) is not null;
        }

        var init = node switch
        {
            null => null,
            VariableDeclaration variableDeclaration => variableDeclaration.Initializer,
            BinaryExpression binaryExpression => binaryExpression.Right,
            PropertyAccessExpression { Parent: BinaryExpression parentBinary } => parentBinary.Right,
            _ => null,
        };
        init = init is not null ? Utilities.GetRightMostAssignedExpression(init) : null;
        if (init is not null)
        {
            var isPrototypeAssignment = Utilities.IsPrototypeAccess(
                node is VariableDeclaration declaration ? declaration.Name!
                : node is BinaryExpression binary ? binary.Left!
                : node!);
            return Utilities.GetExpandoInitializer(
                init is BinaryExpression initBinary && (initBinary.OperatorToken!.Kind == SyntaxKind.BarBarToken || initBinary.OperatorToken.Kind == SyntaxKind.QuestionQuestionToken)
                    ? initBinary.Right!
                    : init,
                isPrototypeAssignment) is not null;
        }

        return false;
    }

    /// <summary>ported from: src/compiler/binder.ts:3563-3568 — <c>getParentOfBinaryExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares the result <c>Node</c> because its callers immediately dereference it; a node in the
    /// first-bound position has no parent, so the honest C# answer is <c>Node?</c> and the caller's pattern test
    /// handles it.
    /// </remarks>
    internal static Node? GetParentOfBinaryExpression(Node expr)
    {
        while (expr.Parent is BinaryExpression parent)
        {
            expr = parent;
        }

        return expr.Parent;
    }

    /// <summary>ported from: src/compiler/binder.ts:3570-3578 — <c>lookupSymbolForPropertyAccess</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>lookupContainer</c> defaults to the closure's <c>container</c>; C# cannot use an instance
    /// slot as a default argument, so the parameter is nullable and the body applies the same default. That also keeps
    /// TS's behaviour for an explicit <c>undefined</c>: the recursive call omits the argument, so it looks in
    /// <c>container</c> again, and a caller that passes <c>thisParentContainer</c> while it is undefined gets the same
    /// answer (JS applies a default parameter for an explicit <c>undefined</c>).
    /// <para>
    /// PORT-NOTE: <c>node.expression</c> is the union read through
    /// <see cref="Utilities.GetAccessExpressionExpression"/>; <c>symbol.exports!.get(name)</c> is a dictionary
    /// <c>TryGetValue</c> and a missing name (or table) answers null, which is TS's <c>undefined</c>.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>lookupSymbolForName(container, …)</c> reads slots off that container, so an absent one is a
    /// crash upstream; the port states the walk's invariant with <see cref="Debug.Assert(bool, string?, Func{string}?)"/>
    /// rather than dereferencing a null the reference cannot reach.
    /// </para>
    /// </remarks>
    internal ISymbol? LookupSymbolForPropertyAccess(Node node, Node? lookupContainer = null)
    {
        var container = lookupContainer ?? _container;
        if (NodeTests.IsIdentifier(node))
        {
            Debug.Assert(container is not null);
            return LookupSymbolForName(container, ((Identifier)node).EscapedText);
        }
        else
        {
            var symbol = LookupSymbolForPropertyAccess(Utilities.GetAccessExpressionExpression(node));
            if (symbol is null || symbol.Exports is null)
            {
                return null;
            }

            var name = Utilities.GetElementOrPropertyAccessName(node);
            return name is not null && symbol.Exports.TryGetValue(name, out var exported) ? exported : null;
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:3580-3596 — <c>forEachIdentifierInEntityName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's callback type is
    /// <c>(e: Declaration, symbol: Symbol | undefined, parent: Symbol | undefined) =&gt; Symbol | undefined</c>; the
    /// port states it as <c>Func&lt;Node, ISymbol?, ISymbol?, ISymbol?&gt;</c> because <c>Declaration</c> is the
    /// capability face <see cref="IDeclaration"/> (the callbacks cast where they need it) and the union of node types
    /// otherwise has no anchor.
    /// <para>
    /// PORT-NOTE: <c>isExportsOrModuleExportsOrAlias(file, e)</c> takes an <see cref="Expression"/>; the entry's
    /// callers pass an access expression or an identifier, both of which the model classes expose as
    /// <see cref="Expression"/>, so the cast is the one TS's structural union made implicit.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>s &amp;&amp; s.exports &amp;&amp; s.exports.get(getElementOrPropertyAccessName(e))</c> is
    /// stated as three tests, and <c>getNameOrArgument(e)</c>'s non-optional TS result is asserted with
    /// <see cref="Debug.CheckDefined{T}(T?, string?)"/> because the port types those slots nullable (§12.6) — the
    /// reference's own comment marks the private-identifier test below as unreachable.
    /// </para>
    /// </remarks>
    internal ISymbol? ForEachIdentifierInEntityName(Node e, ISymbol? parent, Func<Node, ISymbol?, ISymbol?, ISymbol?> action)
    {
        if (IsExportsOrModuleExportsOrAlias(_file, (Expression)e))
        {
            return _file.Symbol;
        }
        else if (NodeTests.IsIdentifier(e))
        {
            return action(e, LookupSymbolForPropertyAccess(e), parent);
        }
        else
        {
            var s = ForEachIdentifierInEntityName(Utilities.GetAccessExpressionExpression(e), parent, action);
            var name = Debug.CheckDefined(Utilities.GetNameOrArgument(e), "an access expression must have a name or argument");
            // unreachable
            if (NodeTests.IsPrivateIdentifier(name))
            {
                Debug.Fail("unexpected PrivateIdentifier");
            }

            ISymbol? symbolForName = null;
            if (s is not null && s.Exports is not null && Utilities.GetElementOrPropertyAccessName(e) is { } nameText
                && s.Exports.TryGetValue(nameText, out var exported))
            {
                symbolForName = exported;
            }

            return action(name, symbolForName, s);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:3598-3604 — <c>bindCallExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>!file.commonJsModuleIndicator</c> is a truthiness test of a node, kept as a null test.
    /// </remarks>
    internal void BindCallExpression(CallExpression node)
    {
        // We're only inspecting call expressions to detect CommonJS modules, so we can skip
        // this check if we've already seen the module indicator
        if (_file.CommonJsModuleIndicator is null && Utilities.IsRequireCall(node, /*requireStringLiteralLikeArgument*/ false))
        {
            SetCommonJsModuleIndicator(node);
        }
    }
}
