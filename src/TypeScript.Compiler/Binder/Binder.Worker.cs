namespace TypeScript.Compiler;

// ported from: src/compiler/binder.ts:2846-3095 (bindWorker)
/// <summary>
/// The per-kind dispatch at the centre of the walk — TS's <c>bindWorker</c>, stated as a kind table.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS writes <c>bindWorker</c> as one <c>switch (node.kind)</c> with 73 cases. The port keeps the
/// reference's per-kind arms but states the dispatch as a kind-to-handler table, the shape
/// <c>Parser/ForEachChildTable.cs</c> gives <c>parser.ts</c>'s <c>forEachChild</c>: the registry below is the one
/// place a kind is wired to its arm, and every arm is a named member. A missing kind and a changed arm are both a
/// one-line diff in the registry. A <c>switch</c> would compile to the same table but would put all 73 arms in a
/// single 250-line method body.
/// </para>
/// <para>
/// PORT-NOTE: the reference's <c>break</c> and its running off the end of the switch both mean "nothing further", so
/// an arm is a method that returns. The <c>TypePredicate</c> case, whose whole body is
/// <c>break; // Binding the children will handle everything</c>, is registered to an arm with an empty body rather
/// than to no arm, so the registry lists exactly the kinds the reference dispatches; an unregistered kind is the
/// reference's missing <c>default</c>.
/// </para>
/// <para>
/// PORT-NOTE: the arms are <c>static</c> and take the binder as their first argument. The table is built once
/// (<c>static readonly</c>) rather than once per file, so a handler cannot be an instance method and cannot capture
/// <c>this</c> — the same "context argument" shape <c>Core.ForEach</c> uses. An arm therefore reads state as
/// <c>binder._x</c>.
/// </para>
/// <para>
/// PORT-NOTE: the registry is filled by one segment method per owning slice — <see cref="AddCoreWorkerHandlers"/>
/// (this file: the kinds whose targets M5-1/M5-5 own), <c>AddStrictModeWorkerHandlers</c>
/// (Binder.Worker.Strict.cs, the kinds whose targets M5-3 owns), <c>AddModuleWorkerHandlers</c>
/// (Binder.Worker.Modules.cs, M5-4's targets) and <c>AddJsDocWorkerHandlers</c> (Binder.Worker.JsDoc.cs, the JSDoc
/// deferral set). The arm that calls a sibling slice's member therefore sits in that slice's own file, so a change to
/// such an arm never touches the dispatcher below.
/// </para>
/// </remarks>
internal partial class Binder
{
    /// <summary>
    /// One arm of TS's <c>bindWorker</c> switch. The binder is passed in because the table is static (see the
    /// remarks on this file).
    /// </summary>
    private delegate void WorkerHandler(Binder binder, Node node);

    /// <summary>ported from: src/compiler/binder.ts:2846-2847; the switch as a table — the kinds it has a case for.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>switch</c> is O(1) by the compiler's own jump table; the port indexes an array the same
    /// size as the kind enum, so the dispatch stays O(1) and allocates nothing per node. A <c>null</c> slot is the
    /// reference's absent <c>default</c>.
    /// </remarks>
    private static readonly WorkerHandler?[] WorkerHandlers = CreateWorkerHandlers();

    /// <summary>
    /// The number of distinct kinds the reference's <c>switch</c> has a <c>case</c> for — the invariant
    /// <see cref="CreateWorkerHandlers"/> asserts, which is what catches a kind registered twice (the second
    /// registration would silently win) or dropped by a later edit.
    /// </summary>
    private const int WorkerKindCount = 73;

    /// <summary>ported from: src/compiler/binder.ts:2846-3095 — <c>bindWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's switch body is the table lookup and an indirect call; the arms themselves are the members the
    /// registry maps to, each carrying its own part of the line range (2846-3095).
    /// </remarks>
    internal void BindWorker(Node node)
    {
        if (WorkerHandlers[(int)node.Kind] is { } handler)
        {
            handler(this, node);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:2847 — the switch head, as the table's construction.</summary>
    private static WorkerHandler?[] CreateWorkerHandlers()
    {
        var handlers = new WorkerHandler?[(int)SyntaxKind.Count];
        AddCoreWorkerHandlers(handlers);
        AddStrictModeWorkerHandlers(handlers);
        AddModuleWorkerHandlers(handlers);
        AddJsDocWorkerHandlers(handlers);

        var registered = 0;
        foreach (var handler in handlers)
        {
            if (handler is not null)
            {
                registered++;
            }
        }

        Debug.Assert(registered == WorkerKindCount, "every kind bindWorker dispatches must be registered exactly once");
        return handlers;
    }

    /// <summary>Wires one kind to its arm; assert that no kind is wired twice.</summary>
    private static void Register(WorkerHandler?[] handlers, SyntaxKind kind, WorkerHandler handler)
    {
        Debug.Assert(handlers[(int)kind] is null, "a kind is registered exactly once");
        handlers[(int)kind] = handler;
    }

    // ---------------------------------------------------------------------------------------------
    // The arms whose targets this slice or the M5-1 skeleton owns (binder.ts:2848-3053)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/binder.ts:2849-2871 — <c>bindWorker</c>'s <c>Identifier</c>/<c>ThisKeyword</c> arm.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>Identifier</c> case falls through to <c>ThisKeyword</c>, so the shared tail is one arm
    /// registered for both kinds and the JSDoc-namespace test is stated first, exactly where the reference states it.
    /// <para>
    /// PORT-NOTE: TS writes <c>(node as Identifier | ThisExpression).flowNode = currentFlow</c>; both classes
    /// implement <see cref="IFlowContainer"/> in the port, which is the shared face the union stands for.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS hands the node to <c>checkContextualIdentifier(node as Identifier)</c> even when it is a
    /// <c>ThisKeyword</c>. That cast is erased, so a `this` reaches the check with no <c>escapedText</c> and the
    /// keyword lookup misses; C#'s cast is checked, so the guard the cast implies is stated here instead.
    /// </para>
    /// </remarks>
    private static void BindIdentifierOrThisKeywordWorker(Binder binder, Node node)
    {
        // for typedef type names with namespaces, bind the new jsdoc type symbol here
        // because it requires all containing namespaces to be in effect, namely the
        // current "blockScopeContainer" needs to be set to its immediate namespace parent.
        if ((node.Flags & NodeFlags.IdentifierIsInJSDocNamespace) != 0)
        {
            var parentNode = node.Parent;
            while (parentNode is not null && !Utilities.IsJSDocTypeAlias(parentNode))
            {
                parentNode = parentNode.Parent;
            }

            binder.BindBlockScopedDeclaration((IDeclaration)parentNode!, SymbolFlags.TypeAlias, SymbolFlags.TypeAliasExcludes);
            return;
        }

        if (node.Kind == SyntaxKind.ThisKeyword)
        {
            binder._seenThisKeyword = true;
        }

        // TODO: Why use `isExpression` here? both Identifier and ThisKeyword are expressions.
        if (binder._currentFlow is not null
            && (Utilities.IsExpression(node) || binder._parent?.Kind == SyntaxKind.ShorthandPropertyAssignment))
        {
            ((IFlowContainer)node).FlowNode = binder._currentFlow;
        }

        if (node is Identifier identifier)
        {
            binder.CheckContextualIdentifier(identifier);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:2872-2876 — <c>bindWorker</c>'s <c>QualifiedName</c> arm.</summary>
    private static void BindQualifiedNameWorker(Binder binder, Node node)
    {
        if (binder._currentFlow is not null && Utilities.IsPartOfTypeQuery(node))
        {
            ((IFlowContainer)node).FlowNode = binder._currentFlow;
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:2877-2880 — <c>bindWorker</c>'s <c>MetaProperty</c>/<c>SuperKeyword</c> arm.</summary>
    private static void BindMetaPropertyOrSuperKeywordWorker(Binder binder, Node node)
        => ((IFlowContainer)node).FlowNode = binder._currentFlow;

    /// <summary>ported from: src/compiler/binder.ts:2949-2951 — <c>bindWorker</c>'s <c>ThisType</c> arm.</summary>
    private static void BindThisTypeWorker(Binder binder, Node node) => binder._seenThisKeyword = true;

    /// <summary>ported from: src/compiler/binder.ts:2952-2953 — <c>bindWorker</c>'s <c>TypePredicate</c> arm.</summary>
    /// <remarks>Binding the children will handle everything.</remarks>
    private static void BindTypePredicateWorker(Binder binder, Node node)
    {
    }

    /// <summary>ported from: src/compiler/binder.ts:2954-2955 — <c>bindWorker</c>'s <c>TypeParameter</c> arm.</summary>
    private static void BindTypeParameterWorker(Binder binder, Node node)
        => binder.BindTypeParameter((TypeParameterDeclaration)node);

    /// <summary>ported from: src/compiler/binder.ts:2956-2957 — <c>bindWorker</c>'s <c>Parameter</c> arm.</summary>
    private static void BindParameterWorker(Binder binder, Node node) => binder.BindParameter(node);

    /// <summary>ported from: src/compiler/binder.ts:2958-2959 — <c>bindWorker</c>'s <c>VariableDeclaration</c> arm.</summary>
    private static void BindVariableDeclarationWorker(Binder binder, Node node)
        => binder.BindVariableDeclarationOrBindingElement(node);

    /// <summary>ported from: src/compiler/binder.ts:2960-2962 — <c>bindWorker</c>'s <c>BindingElement</c> arm.</summary>
    private static void BindBindingElementWorker(Binder binder, Node node)
    {
        ((IFlowContainer)node).FlowNode = binder._currentFlow;
        binder.BindVariableDeclarationOrBindingElement(node);
    }

    /// <summary>ported from: src/compiler/binder.ts:2963-2965 — <c>bindWorker</c>'s <c>PropertyDeclaration</c>/<c>PropertySignature</c> arm.</summary>
    private static void BindPropertyDeclarationWorker(Binder binder, Node node) => binder.BindPropertyWorker(node);

    /// <summary>
    /// ported from: src/compiler/binder.ts:2966-2968 — <c>bindWorker</c>'s <c>PropertyAssignment</c>/<c>ShorthandPropertyAssignment</c>
    /// arm.
    /// </summary>
    private static void BindPropertyAssignmentWorker(Binder binder, Node node)
        => binder.BindPropertyOrMethodOrAccessor((IDeclaration)node, SymbolFlags.Property, SymbolFlags.PropertyExcludes);

    /// <summary>ported from: src/compiler/binder.ts:2969-2970 — <c>bindWorker</c>'s <c>EnumMember</c> arm.</summary>
    private static void BindEnumMemberWorker(Binder binder, Node node)
        => binder.BindPropertyOrMethodOrAccessor((IDeclaration)node, SymbolFlags.EnumMember, SymbolFlags.EnumMemberExcludes);

    /// <summary>
    /// ported from: src/compiler/binder.ts:2972-2975 — <c>bindWorker</c>'s <c>CallSignature</c>/<c>ConstructSignature</c>/
    /// <c>IndexSignature</c> arm.
    /// </summary>
    private static void BindSignatureDeclarationWorker(Binder binder, Node node)
        => binder.DeclareSymbolAndAddToSymbolTable((IDeclaration)node, SymbolFlags.Signature, SymbolFlags.None);

    /// <summary>ported from: src/compiler/binder.ts:2976-2982 — <c>bindWorker</c>'s <c>MethodDeclaration</c>/<c>MethodSignature</c> arm.</summary>
    /// <remarks>
    /// If this is an ObjectLiteralExpression method, then it sits in the same space as other properties in the object
    /// literal. So we use <c>SymbolFlags.PropertyExcludes</c> so that it will conflict with any other object literal
    /// members with the same name.
    /// <para>
    /// PORT-NOTE: TS reads <c>node.questionToken</c> off the <c>MethodDeclaration | MethodSignature</c> union; the
    /// port's <see cref="ISignatureDeclarationBase"/> does not declare it, so the pattern switch reads each class's own
    /// slot.
    /// </para>
    /// </remarks>
    private static void BindMethodDeclarationWorker(Binder binder, Node node)
    {
        var questionToken = node switch
        {
            MethodDeclaration method => method.QuestionToken,
            MethodSignature signature => signature.QuestionToken,
            _ => null,
        };
        var flags = SymbolFlags.Method | (questionToken is not null ? SymbolFlags.Optional : SymbolFlags.None);
        var excludes = Utilities.IsObjectLiteralMethod(node) ? SymbolFlags.PropertyExcludes : SymbolFlags.MethodExcludes;
        binder.BindPropertyOrMethodOrAccessor((IDeclaration)node, flags, excludes);
    }

    /// <summary>ported from: src/compiler/binder.ts:2983-2984 — <c>bindWorker</c>'s <c>FunctionDeclaration</c> arm.</summary>
    private static void BindFunctionDeclarationWorker(Binder binder, Node node)
        => binder.BindFunctionDeclaration((FunctionDeclaration)node);

    /// <summary>ported from: src/compiler/binder.ts:2985-2986 — <c>bindWorker</c>'s <c>Constructor</c> arm.</summary>
    private static void BindConstructorWorker(Binder binder, Node node)
        => binder.DeclareSymbolAndAddToSymbolTable((IDeclaration)node, SymbolFlags.Constructor, SymbolFlags.None);

    /// <summary>ported from: src/compiler/binder.ts:2987-2988 — <c>bindWorker</c>'s <c>GetAccessor</c> arm.</summary>
    private static void BindGetAccessorWorker(Binder binder, Node node)
        => binder.BindPropertyOrMethodOrAccessor((IDeclaration)node, SymbolFlags.GetAccessor, SymbolFlags.GetAccessorExcludes);

    /// <summary>ported from: src/compiler/binder.ts:2989-2990 — <c>bindWorker</c>'s <c>SetAccessor</c> arm.</summary>
    private static void BindSetAccessorWorker(Binder binder, Node node)
        => binder.BindPropertyOrMethodOrAccessor((IDeclaration)node, SymbolFlags.SetAccessor, SymbolFlags.SetAccessorExcludes);

    /// <summary>ported from: src/compiler/binder.ts:2996-2999 — <c>bindWorker</c>'s <c>TypeLiteral</c>/<c>JSDocTypeLiteral</c>/<c>MappedType</c> arm.</summary>
    private static void BindTypeLiteralWorker(Binder binder, Node node) => binder.BindAnonymousTypeWorker(node);

    /// <summary>ported from: src/compiler/binder.ts:3004-3006 — <c>bindWorker</c>'s <c>FunctionExpression</c>/<c>ArrowFunction</c> arm.</summary>
    private static void BindFunctionExpressionWorker(Binder binder, Node node) => binder.BindFunctionExpression(node);

    /// <summary>ported from: src/compiler/binder.ts:3028-3032 — <c>bindWorker</c>'s <c>ClassExpression</c>/<c>ClassDeclaration</c> arm.</summary>
    /// <remarks>All classes are automatically in strict mode in ES6.</remarks>
    private static void BindClassLikeDeclarationWorker(Binder binder, Node node)
    {
        binder._inStrictMode = true;
        binder.BindClassLikeDeclaration((IClassLikeDeclarationBase)node);
    }

    /// <summary>ported from: src/compiler/binder.ts:3033-3034 — <c>bindWorker</c>'s <c>InterfaceDeclaration</c> arm.</summary>
    private static void BindInterfaceDeclarationWorker(Binder binder, Node node)
        => binder.BindBlockScopedDeclaration((IDeclaration)node, SymbolFlags.Interface, SymbolFlags.InterfaceExcludes);

    /// <summary>ported from: src/compiler/binder.ts:3035-3036 — <c>bindWorker</c>'s <c>TypeAliasDeclaration</c> arm.</summary>
    private static void BindTypeAliasDeclarationWorker(Binder binder, Node node)
        => binder.BindBlockScopedDeclaration((IDeclaration)node, SymbolFlags.TypeAlias, SymbolFlags.TypeAliasExcludes);

    /// <summary>ported from: src/compiler/binder.ts:3037-3038 — <c>bindWorker</c>'s <c>EnumDeclaration</c> arm.</summary>
    private static void BindEnumDeclarationWorker(Binder binder, Node node)
        => binder.BindEnumDeclaration((EnumDeclaration)node);

    /// <summary>ported from: src/compiler/binder.ts:2847-3094 — the switch's kinds whose targets this file's slice owns.</summary>
    private static void AddCoreWorkerHandlers(WorkerHandler?[] handlers)
    {
        // Strict mode checks / the expression kinds whose binding is this slice's or M5-1's
        Register(handlers, SyntaxKind.Identifier, BindIdentifierOrThisKeywordWorker);
        Register(handlers, SyntaxKind.ThisKeyword, BindIdentifierOrThisKeywordWorker);
        Register(handlers, SyntaxKind.QualifiedName, BindQualifiedNameWorker);
        Register(handlers, SyntaxKind.MetaProperty, BindMetaPropertyOrSuperKeywordWorker);
        Register(handlers, SyntaxKind.SuperKeyword, BindMetaPropertyOrSuperKeywordWorker);
        Register(handlers, SyntaxKind.ThisType, BindThisTypeWorker);
        Register(handlers, SyntaxKind.TypePredicate, BindTypePredicateWorker);

        // Declarations
        Register(handlers, SyntaxKind.TypeParameter, BindTypeParameterWorker);
        Register(handlers, SyntaxKind.Parameter, BindParameterWorker);
        Register(handlers, SyntaxKind.VariableDeclaration, BindVariableDeclarationWorker);
        Register(handlers, SyntaxKind.BindingElement, BindBindingElementWorker);
        Register(handlers, SyntaxKind.PropertyDeclaration, BindPropertyDeclarationWorker);
        Register(handlers, SyntaxKind.PropertySignature, BindPropertyDeclarationWorker);
        Register(handlers, SyntaxKind.PropertyAssignment, BindPropertyAssignmentWorker);
        Register(handlers, SyntaxKind.ShorthandPropertyAssignment, BindPropertyAssignmentWorker);
        Register(handlers, SyntaxKind.EnumMember, BindEnumMemberWorker);
        Register(handlers, SyntaxKind.CallSignature, BindSignatureDeclarationWorker);
        Register(handlers, SyntaxKind.ConstructSignature, BindSignatureDeclarationWorker);
        Register(handlers, SyntaxKind.IndexSignature, BindSignatureDeclarationWorker);
        Register(handlers, SyntaxKind.MethodDeclaration, BindMethodDeclarationWorker);
        Register(handlers, SyntaxKind.MethodSignature, BindMethodDeclarationWorker);
        Register(handlers, SyntaxKind.FunctionDeclaration, BindFunctionDeclarationWorker);
        Register(handlers, SyntaxKind.Constructor, BindConstructorWorker);
        Register(handlers, SyntaxKind.GetAccessor, BindGetAccessorWorker);
        Register(handlers, SyntaxKind.SetAccessor, BindSetAccessorWorker);
        Register(handlers, SyntaxKind.TypeLiteral, BindTypeLiteralWorker);
        Register(handlers, SyntaxKind.JSDocTypeLiteral, BindTypeLiteralWorker);
        Register(handlers, SyntaxKind.MappedType, BindTypeLiteralWorker);
        Register(handlers, SyntaxKind.FunctionExpression, BindFunctionExpressionWorker);
        Register(handlers, SyntaxKind.ArrowFunction, BindFunctionExpressionWorker);
        Register(handlers, SyntaxKind.ClassExpression, BindClassLikeDeclarationWorker);
        Register(handlers, SyntaxKind.ClassDeclaration, BindClassLikeDeclarationWorker);
        Register(handlers, SyntaxKind.InterfaceDeclaration, BindInterfaceDeclarationWorker);
        Register(handlers, SyntaxKind.TypeAliasDeclaration, BindTypeAliasDeclarationWorker);
        Register(handlers, SyntaxKind.EnumDeclaration, BindEnumDeclarationWorker);
    }
}
