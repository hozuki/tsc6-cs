namespace TypeScript.Compiler;

// ported from: src/compiler/binder.ts:2883-2900, 2901-2936, 2991-2995, 3002-3003, 3008-3025, 3039-3045, 3048-3060, 3061-3070
/// <summary>
/// The arms of <c>bindWorker</c> whose targets are the module, export and JS-expando members — the set the M5-4 slice
/// ports (binder.ts:2336-2426, 3108-3604).
/// </summary>
/// <remarks>
/// PORT-NOTE: the segment boundary is the owning slice, as in <c>Binder.Worker.Strict.cs</c>: every arm here calls an
/// M5-4 member (plus the shared helpers), so a change to one of them stays inside this file and never touches the
/// dispatcher. The line ranges the header lists are this segment's arms of the switch.
/// <para>
/// PORT-NOTE: the arms that reach a member whose TS parameter is one of the <c>Bindable*</c> aliases (types.ts:3055-3085)
/// pass the port's honest upper bound for it: <see cref="BinaryExpression"/> for the assignment shapes,
/// <see cref="CallExpression"/> for the object-define-property shapes, <see cref="MemberExpression"/> for the access
/// shapes, and <see cref="Expression"/> where the union mixes those families. The leaf class each kind proves is what
/// the arm holds, so the argument only ever widens.
/// </para>
/// </remarks>
internal partial class Binder
{
    /// <summary>
    /// ported from: src/compiler/binder.ts:2883-2900 — <c>bindWorker</c>'s <c>PropertyAccessExpression</c> /
    /// <c>ElementAccessExpression</c> arm.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS declares the module-export symbol on <c>expr.expression</c> when the access is
    /// <c>module.exports</c>; its guard narrows that member to an <c>Identifier</c> (utilities.ts:4104), which is why
    /// the declaration argument is the access expression's object and not the access.
    /// </remarks>
    private static void BindAccessExpressionWorker(Binder binder, Node node)
    {
        var expr = (MemberExpression)node;
        if (binder._currentFlow is not null && IsNarrowableReference(expr))
        {
            ((IFlowContainer)node).FlowNode = binder._currentFlow;
        }

        if (Utilities.IsSpecialPropertyDeclaration(expr))
        {
            binder.BindSpecialPropertyDeclaration(expr);
        }

        if (Utilities.IsInJSFile(expr)
            && binder._file.CommonJsModuleIndicator is not null
            && Utilities.IsModuleExportsAccessExpression(expr)
            && LookupSymbolForName(binder._blockScopeContainer!, "module") is null)
        {
            binder.DeclareSymbol(
                ((ILocalsContainer)binder._file).Locals!,
                /*parent*/ null,
                (IDeclaration)Utilities.GetAccessExpressionExpression(expr),
                SymbolFlags.FunctionScopedVariable | SymbolFlags.ModuleExports,
                SymbolFlags.FunctionScopedVariableExcludes);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:2901-2936 — <c>bindWorker</c>'s <c>BinaryExpression</c> arm.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads <c>left.expression</c> off the <c>AccessExpression</c> union for the
    /// <c>AssignmentDeclarationKind.Property</c> case; the port reads it through
    /// <see cref="Utilities.GetAccessExpressionExpression"/>, and the cast to <see cref="MemberExpression"/> is the
    /// union's own guarantee — <c>getAssignmentDeclarationKind</c> only answers <c>Property</c> for an assignment whose
    /// left is a property or element access (utilities.ts:4179-4196).
    /// <para>
    /// PORT-NOTE: TS's <c>break</c> inside the <c>Property</c> case leaves the inner switch and then runs
    /// <c>checkStrictModeBinaryExpression</c>; both paths through the inner switch reach it here too, because the
    /// inner <c>break</c>s are the C# <c>break</c>s of this arm's switch.
    /// </para>
    /// </remarks>
    private static void BindBinaryExpressionWorker(Binder binder, Node node)
    {
        var binary = (BinaryExpression)node;
        var specialKind = Utilities.GetAssignmentDeclarationKind(binary);
        switch (specialKind)
        {
            case AssignmentDeclarationKind.ExportsProperty:
                binder.BindExportsPropertyAssignment(binary);
                break;
            case AssignmentDeclarationKind.ModuleExports:
                binder.BindModuleExportsAssignment(binary);
                break;
            case AssignmentDeclarationKind.PrototypeProperty:
                binder.BindPrototypePropertyAssignment((MemberExpression)binary.Left!, binary);
                break;
            case AssignmentDeclarationKind.Prototype:
                binder.BindPrototypeAssignment(binary);
                break;
            case AssignmentDeclarationKind.ThisProperty:
                binder.BindThisPropertyAssignment(binary);
                break;
            case AssignmentDeclarationKind.Property:
            {
                var expression = Utilities.GetAccessExpressionExpression((MemberExpression)binary.Left!);
                if (Utilities.IsInJSFile(binary) && NodeTests.IsIdentifier(expression))
                {
                    var symbol = LookupSymbolForName(binder._blockScopeContainer!, ((Identifier)expression).EscapedText);
                    if (Utilities.IsThisInitializedDeclaration((Node?)symbol?.ValueDeclaration))
                    {
                        binder.BindThisPropertyAssignment(binary);
                        break;
                    }
                }

                binder.BindSpecialPropertyAssignment(binary);
                break;
            }

            case AssignmentDeclarationKind.None:
                // Nothing to do
                break;
            default:
                Debug.Fail("Unknown binary expression special property assignment kind");
                break;
        }

        binder.CheckStrictModeBinaryExpression(binary);
    }

    /// <summary>
    /// ported from: src/compiler/binder.ts:2991-2995 — <c>bindWorker</c>'s <c>FunctionType</c> /
    /// <c>JSDocFunctionType</c> / <c>JSDocSignature</c> / <c>ConstructorType</c> arm.
    /// </summary>
    private static void BindFunctionOrConstructorTypeWorker(Binder binder, Node node)
        => binder.BindFunctionOrConstructorType((IDeclaration)node);

    /// <summary>ported from: src/compiler/binder.ts:3002-3003 — <c>bindWorker</c>'s <c>ObjectLiteralExpression</c> arm.</summary>
    private static void BindObjectLiteralExpressionWorker(Binder binder, Node node)
        => binder.BindObjectLiteralExpression((ObjectLiteralExpression)node);

    /// <summary>ported from: src/compiler/binder.ts:3008-3025 — <c>bindWorker</c>'s <c>CallExpression</c> arm.</summary>
    private static void BindCallExpressionWorker(Binder binder, Node node)
    {
        var call = (CallExpression)node;
        var assignmentKind = Utilities.GetAssignmentDeclarationKind(call);
        switch (assignmentKind)
        {
            case AssignmentDeclarationKind.ObjectDefinePropertyValue:
                binder.BindObjectDefinePropertyAssignment(call);
                return;
            case AssignmentDeclarationKind.ObjectDefinePropertyExports:
                binder.BindObjectDefinePropertyExport(call);
                return;
            case AssignmentDeclarationKind.ObjectDefinePrototypeProperty:
                binder.BindObjectDefinePrototypeProperty(call);
                return;
            case AssignmentDeclarationKind.None:
                // Nothing to do
                break;
            default:
                Debug.Fail("Unknown call expression assignment declaration kind");
                return;
        }

        if (Utilities.IsInJSFile(call))
        {
            binder.BindCallExpression(call);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:3039-3040 — <c>bindWorker</c>'s <c>ModuleDeclaration</c> arm.</summary>
    private static void BindModuleDeclarationWorker(Binder binder, Node node)
        => binder.BindModuleDeclaration((ModuleDeclaration)node);

    /// <summary>ported from: src/compiler/binder.ts:3042-3043 — <c>bindWorker</c>'s <c>JsxAttributes</c> arm.</summary>
    private static void BindJsxAttributesWorker(Binder binder, Node node)
        => binder.BindJsxAttributes((JsxAttributes)node);

    /// <summary>ported from: src/compiler/binder.ts:3044-3045 — <c>bindWorker</c>'s <c>JsxAttribute</c> arm.</summary>
    private static void BindJsxAttributeWorker(Binder binder, Node node)
        => binder.BindJsxAttribute((JsxAttribute)node, SymbolFlags.Property, SymbolFlags.PropertyExcludes);

    /// <summary>
    /// ported from: src/compiler/binder.ts:3048-3052 — <c>bindWorker</c>'s <c>ImportEqualsDeclaration</c> /
    /// <c>NamespaceImport</c> / <c>ImportSpecifier</c> / <c>ExportSpecifier</c> arm.
    /// </summary>
    private static void BindAliasDeclarationWorker(Binder binder, Node node)
        => binder.DeclareSymbolAndAddToSymbolTable((IDeclaration)node, SymbolFlags.Alias, SymbolFlags.AliasExcludes);

    /// <summary>ported from: src/compiler/binder.ts:3053-3054 — <c>bindWorker</c>'s <c>NamespaceExportDeclaration</c> arm.</summary>
    private static void BindNamespaceExportDeclarationWorker(Binder binder, Node node)
        => binder.BindNamespaceExportDeclaration((NamespaceExportDeclaration)node);

    /// <summary>ported from: src/compiler/binder.ts:3055-3056 — <c>bindWorker</c>'s <c>ImportClause</c> arm.</summary>
    private static void BindImportClauseWorker(Binder binder, Node node) => binder.BindImportClause((ImportClause)node);

    /// <summary>ported from: src/compiler/binder.ts:3057-3058 — <c>bindWorker</c>'s <c>ExportDeclaration</c> arm.</summary>
    private static void BindExportDeclarationWorker(Binder binder, Node node)
        => binder.BindExportDeclaration((ExportDeclaration)node);

    /// <summary>ported from: src/compiler/binder.ts:3059-3060 — <c>bindWorker</c>'s <c>ExportAssignment</c> arm.</summary>
    private static void BindExportAssignmentWorker(Binder binder, Node node)
        => binder.BindExportAssignment((ExportAssignment)node);

    /// <summary>ported from: src/compiler/binder.ts:3061-3063 — <c>bindWorker</c>'s <c>SourceFile</c> arm.</summary>
    private static void BindSourceFileWorker(Binder binder, Node node)
    {
        binder.UpdateStrictModeStatementList(((SourceFile)node).Statements!);
        binder.BindSourceFileIfExternalModule();
    }

    /// <summary>ported from: src/compiler/binder.ts:3064-3070 — <c>bindWorker</c>'s <c>Block</c> / <c>ModuleBlock</c> arm.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>Block</c> case returns when the parent is neither function-like nor a class static block and
    /// otherwise falls through to <c>ModuleBlock</c>; the shared tail reads <c>statements</c> through a pattern
    /// switch because the port has no <c>Block | ModuleBlock</c> union.
    /// </remarks>
    private static void BindBlockOrModuleBlockWorker(Binder binder, Node node)
    {
        if (node.Kind == SyntaxKind.Block && !Utilities.IsFunctionLikeOrClassStaticBlockDeclaration(node.Parent))
        {
            return;
        }

        var statements = node switch
        {
            Block block => block.Statements,
            ModuleBlock moduleBlock => moduleBlock.Statements,
            _ => null,
        };
        binder.UpdateStrictModeStatementList(statements!);
    }

    /// <summary>ported from: src/compiler/binder.ts:2883-2900, 2901-2936, 2991-2995, 3002-3003, 3008-3025, 3039-3045, 3048-3070 — M5-4's kinds in the switch.</summary>
    private static void AddModuleWorkerHandlers(WorkerHandler?[] handlers)
    {
        // The expando family (the JS assignment shapes)
        Register(handlers, SyntaxKind.PropertyAccessExpression, BindAccessExpressionWorker);
        Register(handlers, SyntaxKind.ElementAccessExpression, BindAccessExpressionWorker);
        Register(handlers, SyntaxKind.BinaryExpression, BindBinaryExpressionWorker);
        Register(handlers, SyntaxKind.CallExpression, BindCallExpressionWorker);

        // Members of classes, interfaces, and modules
        Register(handlers, SyntaxKind.FunctionType, BindFunctionOrConstructorTypeWorker);
        Register(handlers, SyntaxKind.JSDocFunctionType, BindFunctionOrConstructorTypeWorker);
        Register(handlers, SyntaxKind.JSDocSignature, BindFunctionOrConstructorTypeWorker);
        Register(handlers, SyntaxKind.ConstructorType, BindFunctionOrConstructorTypeWorker);
        Register(handlers, SyntaxKind.ObjectLiteralExpression, BindObjectLiteralExpressionWorker);
        Register(handlers, SyntaxKind.ModuleDeclaration, BindModuleDeclarationWorker);

        // Jsx-attributes
        Register(handlers, SyntaxKind.JsxAttributes, BindJsxAttributesWorker);
        Register(handlers, SyntaxKind.JsxAttribute, BindJsxAttributeWorker);

        // Imports and exports
        Register(handlers, SyntaxKind.ImportEqualsDeclaration, BindAliasDeclarationWorker);
        Register(handlers, SyntaxKind.NamespaceImport, BindAliasDeclarationWorker);
        Register(handlers, SyntaxKind.ImportSpecifier, BindAliasDeclarationWorker);
        Register(handlers, SyntaxKind.ExportSpecifier, BindAliasDeclarationWorker);
        Register(handlers, SyntaxKind.NamespaceExportDeclaration, BindNamespaceExportDeclarationWorker);
        Register(handlers, SyntaxKind.ImportClause, BindImportClauseWorker);
        Register(handlers, SyntaxKind.ExportDeclaration, BindExportDeclarationWorker);
        Register(handlers, SyntaxKind.ExportAssignment, BindExportAssignmentWorker);
        Register(handlers, SyntaxKind.SourceFile, BindSourceFileWorker);
        Register(handlers, SyntaxKind.Block, BindBlockOrModuleBlockWorker);
        Register(handlers, SyntaxKind.ModuleBlock, BindBlockOrModuleBlockWorker);
    }
}
