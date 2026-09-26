namespace TypeScript.Compiler.Tests;

/// <summary>
/// M5-5: the dispatch of <c>bindWorker</c> (binder.ts:2846-3095) — the one place where a node's kind decides which
/// member gets to bind it.
/// </summary>
/// <remarks>
/// The port states TS's <c>switch (node.kind)</c> as a kind-to-handler table (<see cref="Binder"/>'s
/// <c>WorkerHandlers</c>), so the two things worth pinning are the table's contents and the arms whose effect is
/// visible on the bound tree without a checker: <c>ThisType</c> raising <c>_seenThisKeyword</c>, <c>QualifiedName</c>
/// attaching the current flow node, the strict-mode arms reaching their checks, the three statement-list arms, and the
/// JSDoc deferral arms that queue a <c>@typedef</c> for the end of the file.
/// </remarks>
public class BinderWorkerTests
{
    private static SourceFile ParseAndBind(string text, ScriptKind kind = ScriptKind.TS)
    {
        var file = Parser.CreateSourceFile(
            kind == ScriptKind.JS ? "test.js" : "test.ts",
            text,
            ScriptTarget.ESNext,
            /*setParentNodes*/ false,
            kind);
        Binder.BindSourceFile(file, new FakeCompilerOptions { Target = ScriptTarget.ESNext });
        return file;
    }

    /// <summary>
    /// The kinds the reference's <c>switch</c> has a <c>case</c> for, in the order the reference lists them
    /// (binder.ts:2849-3093). <c>TypePredicate</c> is here because the reference has a case for it that deliberately
    /// does nothing.
    /// </summary>
    private static readonly SyntaxKind[] DispatchedKinds =
    [
        SyntaxKind.Identifier, SyntaxKind.ThisKeyword, SyntaxKind.QualifiedName, SyntaxKind.MetaProperty,
        SyntaxKind.SuperKeyword, SyntaxKind.PrivateIdentifier, SyntaxKind.PropertyAccessExpression,
        SyntaxKind.ElementAccessExpression, SyntaxKind.BinaryExpression, SyntaxKind.CatchClause,
        SyntaxKind.DeleteExpression, SyntaxKind.PostfixUnaryExpression, SyntaxKind.PrefixUnaryExpression,
        SyntaxKind.WithStatement, SyntaxKind.LabeledStatement, SyntaxKind.ThisType, SyntaxKind.TypePredicate,
        SyntaxKind.TypeParameter, SyntaxKind.Parameter, SyntaxKind.VariableDeclaration, SyntaxKind.BindingElement,
        SyntaxKind.PropertyDeclaration, SyntaxKind.PropertySignature, SyntaxKind.PropertyAssignment,
        SyntaxKind.ShorthandPropertyAssignment, SyntaxKind.EnumMember, SyntaxKind.CallSignature,
        SyntaxKind.ConstructSignature, SyntaxKind.IndexSignature, SyntaxKind.MethodDeclaration,
        SyntaxKind.MethodSignature, SyntaxKind.FunctionDeclaration, SyntaxKind.Constructor, SyntaxKind.GetAccessor,
        SyntaxKind.SetAccessor, SyntaxKind.FunctionType, SyntaxKind.JSDocFunctionType, SyntaxKind.JSDocSignature,
        SyntaxKind.ConstructorType, SyntaxKind.TypeLiteral, SyntaxKind.JSDocTypeLiteral, SyntaxKind.MappedType,
        SyntaxKind.JSDocClassTag, SyntaxKind.ObjectLiteralExpression, SyntaxKind.FunctionExpression,
        SyntaxKind.ArrowFunction, SyntaxKind.CallExpression, SyntaxKind.ClassExpression, SyntaxKind.ClassDeclaration,
        SyntaxKind.InterfaceDeclaration, SyntaxKind.TypeAliasDeclaration, SyntaxKind.EnumDeclaration,
        SyntaxKind.ModuleDeclaration, SyntaxKind.JsxAttributes, SyntaxKind.JsxAttribute,
        SyntaxKind.ImportEqualsDeclaration, SyntaxKind.NamespaceImport, SyntaxKind.ImportSpecifier,
        SyntaxKind.ExportSpecifier, SyntaxKind.NamespaceExportDeclaration, SyntaxKind.ImportClause,
        SyntaxKind.ExportDeclaration, SyntaxKind.ExportAssignment, SyntaxKind.SourceFile, SyntaxKind.Block,
        SyntaxKind.ModuleBlock, SyntaxKind.JSDocParameterTag, SyntaxKind.JSDocPropertyTag,
        SyntaxKind.JSDocTypedefTag, SyntaxKind.JSDocCallbackTag, SyntaxKind.JSDocEnumTag, SyntaxKind.JSDocOverloadTag,
        SyntaxKind.JSDocImportTag,
    ];

    /// <summary>
    /// The registry is the dispatch, so a kind the reference handles that has no arm is a silently unbound
    /// declaration. Reading the table runs its initialiser, whose <c>Debug.Assert</c> states the same invariant from
    /// the inside (no kind registered twice, none missing).
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: this is the one test in the file that reads the binder's shape rather than the tree it writes: the
    /// table is private, so the field is reached by reflection. A rename of the field makes the lookup return
    /// <c>null</c> and fails loudly.
    /// </remarks>
    [Fact]
    public void BindWorker_HasAnArmForEveryKindTheReferenceSwitchHandles()
    {
        var field = typeof(Binder).GetField(
            "WorkerHandlers",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);
        var handlers = field!.GetValue(null) as Array;
        Assert.NotNull(handlers);

        var registered = 0;
        foreach (var handler in handlers)
        {
            if (handler is not null)
            {
                registered++;
            }
        }

        Assert.Equal(DispatchedKinds.Length, registered);
        foreach (var kind in DispatchedKinds)
        {
            Assert.NotNull(handlers.GetValue((int)kind));
        }
    }

    /// <summary>
    /// <c>case SyntaxKind.ThisType: seenThisKeyword = true;</c> (binder.ts:2949-2951). An <c>interface</c> has no body
    /// to walk, so a <c>this</c> type in it is the only thing that can raise the flag — the negative control proves
    /// the first assertion is about this arm and not about some other path.
    /// </summary>
    [Fact]
    public void BindWorker_DispatchesThisTypeIntoSeenThisKeyword()
    {
        var withThisType = ParseAndBind("interface I { x: this; }");
        Assert.NotEqual(NodeFlags.None, ((InterfaceDeclaration)withThisType.Statements![0]!).Flags & NodeFlags.ContainsThis);

        var withoutThisType = ParseAndBind("interface J { x: number; }");
        Assert.Equal(NodeFlags.None, ((InterfaceDeclaration)withoutThisType.Statements![0]!).Flags & NodeFlags.ContainsThis);
    }

    /// <summary>
    /// <c>case SyntaxKind.QualifiedName</c> attaches the current flow node when the name is part of a type query
    /// (binder.ts:2872-2876, <c>isPartOfTypeQuery</c>). The negative control is a qualified name in a plain type
    /// reference, which the guard excludes.
    /// </summary>
    [Fact]
    public void BindWorker_DispatchesQualifiedNameIntoTheCurrentFlow()
    {
        var typeQuery = ParseAndBind("declare namespace A { var b: number; } type Q = typeof A.b;");
        var queryAlias = (TypeAliasDeclaration)typeQuery.Statements![1]!;
        var queryName = (QualifiedName)((TypeQueryNode)queryAlias.Type!).ExprName!;
        Assert.NotNull(queryName.FlowNode);

        var typeReference = ParseAndBind("declare namespace A { namespace B { var c: number; } } type T = A.B;");
        var referenceAlias = (TypeAliasDeclaration)typeReference.Statements![1]!;
        var referenceName = (QualifiedName)((TypeReferenceNode)referenceAlias.Type!).TypeName!;
        Assert.Null(referenceName.FlowNode);
    }

    /// <summary>
    /// The strict-mode arms hand their node to the checks M5-3 ports (binder.ts:2937-2948). A <c>with</c> statement in
    /// a module is the shortest observable one, and a file with an <c>export</c> is strict (binder.ts:621-628).
    /// </summary>
    [Fact]
    public void BindWorker_DispatchesTheStrictModeArmsIntoTheirChecks()
    {
        var file = ParseAndBind("export {};\nwith (x) {}\n");

        var diagnostic = Assert.Single(file.BindDiagnostics!);
        Assert.Equal(Diagnostics.with_statements_are_not_allowed_in_strict_mode.Code, diagnostic.Code);
        Assert.Equal(DiagnosticCategory.Error, diagnostic.Category);
    }

    /// <summary>
    /// The <c>SourceFile</c>, <c>Block</c> and <c>ModuleBlock</c> arms all reach
    /// <c>updateStrictModeStatementList</c> (binder.ts:3061-3070), so a <c>"use strict"</c> prologue in each of the
    /// three is observable through the same <c>with</c> diagnostic as above.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: the three cases are separate on purpose — the reference reaches the shared tail by falling through
    /// <c>Block</c> into <c>ModuleBlock</c>, and a port that reads <c>statements</c> off the wrong class binds a
    /// namespace body by throwing. The last case is that regression.
    /// </remarks>
    [Fact]
    public void BindWorker_UpdatesStrictModeStatementListForSourceFileBlockAndModuleBlock()
    {
        foreach (var text in new[]
        {
            "\"use strict\";\nwith (x) {}",
            "function f() { \"use strict\";\nwith (x) {} }",
            "namespace N { \"use strict\";\nwith (x) {} }",
        })
        {
            var file = ParseAndBind(text);
            var diagnostic = Assert.Single(file.BindDiagnostics!);
            Assert.Equal(Diagnostics.with_statements_are_not_allowed_in_strict_mode.Code, diagnostic.Code);
        }
    }

    /// <summary>
    /// The <c>JSDocTypedefTag</c>/<c>JSDocCallbackTag</c>/<c>JSDocEnumTag</c> arm queues the tag for
    /// <c>delayedBindJSDocTypedefTag</c>, which runs once the file's scope is complete (binder.ts:3086-3089,
    /// 584-585): the tag must end up as a type alias in the file's locals.
    /// </summary>
    [Fact]
    public void BindWorker_DefersJSDocTypeAliasTags()
    {
        var file = ParseAndBind("/** @typedef {number} MyType */", ScriptKind.JS);

        var symbol = Assert.Contains("MyType", file.Locals!);
        Assert.Equal(SymbolFlags.TypeAlias, symbol.Flags);
    }
}
