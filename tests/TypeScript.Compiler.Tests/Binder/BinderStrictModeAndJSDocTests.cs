namespace TypeScript.Compiler.Tests;

/// <summary>
/// M5-3 的端到端证据：严格模式与保留字检查（<c>checkContextualIdentifier</c>/<c>checkStrictMode*</c> 全家族 +
/// <c>errorOnFirstToken</c>）与 JSDoc 延迟绑定（<c>bindJSDocTypeAlias</c>/<c>bindJSDocClassTag</c>/
/// <c>bindJSDocImportTag</c>，以及两个延迟段 <c>delayedBindJSDocTypedefTag</c>/<c>bindJSDocImports</c>）。
/// </summary>
/// <remarks>
/// 断言的全部是 binder 直接写在树上的东西 —— <c>file.bindDiagnostics</c> 的 code 与 <c>file.locals</c> 的符号 ——
/// 不经过 checker，所以不需要 M6。每一条期望值都先用真编译器（<c>lib/typescript.js</c> 的 <c>createProgram</c> +
/// <c>getTypeChecker()</c>）对同一份源文本验过；三条 JSDoc 用例之所以挂 <c>.js</c> 文件名，是因为 <c>bindJSDoc</c>
/// 只在 JS 文件里绑定注释（binder.ts:2810-2814），<c>.ts</c> 的那条是这条前提的反例。
/// <para>
/// 这些检查几乎都挂在 <c>inStrictMode</c> 上，而 <c>inStrictMode</c> 在默认选项下就是 <c>true</c> ——
/// <c>getAlwaysStrict</c> 的判据是 <c>alwaysStrict !== false</c>（utilities.ts:9271-9276），未设置即视为开启 ——
/// 所以多数用例不需要 <c>"use strict"</c> 前导；带前导的那几条只是把它写明白。
/// </para>
/// </remarks>
public class BinderStrictModeAndJSDocTests
{
    private static SourceFile ParseAndBind(string fileName, string text, ScriptTarget target = ScriptTarget.ESNext)
    {
        var file = Parser.CreateSourceFile(
            fileName,
            text,
            target,
            /*setParentNodes*/ false,
            fileName.EndsWith(".js", StringComparison.Ordinal) ? ScriptKind.JS : ScriptKind.TS);
        Binder.BindSourceFile(file, new FakeCompilerOptions { Target = target });
        return file;
    }

    private static SourceFile Parse(string text)
        => Parser.CreateSourceFile("test.ts", text, ScriptTarget.ESNext, /*setParentNodes*/ true, ScriptKind.TS);

    private static int[] BindDiagnosticCodes(SourceFile file)
        => [.. file.BindDiagnostics!.Select(diagnostic => diagnostic.Code)];

    // ---------------------------------------------------------------------------------------------
    // checkContextualIdentifier / checkPrivateIdentifier
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void CheckContextualIdentifier_ReportsAFutureReservedWordUsedInStrictMode()
    {
        // `public` is one of the future reserved words the contextual check reports when the file is strict
        // (binder.ts:2571-2577), and a non-declaration file is strict by default (binder.ts:620-628).
        var file = ParseAndBind("test.ts", "\"use strict\";\nvar public = 1;\n");

        Assert.Equal(new[] { 1212 }, BindDiagnosticCodes(file));
    }

    [Fact]
    public void CheckContextualIdentifier_PrefersTheModuleMessageForAModuleFile()
    {
        // An external module's message says the strictness comes from the module (binder.ts:2597-2599 → 1214).
        var file = ParseAndBind("test.ts", "export {};\nvar public = 1;\n");

        Assert.Equal(new[] { 1214 }, BindDiagnosticCodes(file));
    }

    [Fact]
    public void CheckContextualIdentifier_ReportsAwaitAtTheTopLevelOfAModule()
    {
        // 1262 for `await` as a top-level module name (binder.ts:2581-2583); outside a module the same declaration is
        // not reported, because the module half of that guard fails.
        Assert.Equal(new[] { 1262 }, BindDiagnosticCodes(ParseAndBind("test.ts", "export {};\nvar await = 1;\n")));
        Assert.Empty(BindDiagnosticCodes(ParseAndBind("test.ts", "var await = 1;\n")));
    }

    [Fact]
    public void CheckPrivateIdentifier_ReportsTheReservedPrivateName()
    {
        // `#constructor` is the one reserved private name (binder.ts:2609-2616 → 18012).
        var file = ParseAndBind("test.ts", "class C { #constructor() {} }\n");

        Assert.Equal(new[] { 18012 }, BindDiagnosticCodes(file));
    }

    // ---------------------------------------------------------------------------------------------
    // checkStrictModeEvalOrArguments and the arms that reach it
    // ---------------------------------------------------------------------------------------------

    [Theory]
    // binder.ts:2626-2632 — the catch binding; the same check runs a second time from
    // `bindVariableDeclarationOrBindingElement` (binder.ts:3649-3653) for the catch variable's own declaration, which
    // is why the reference reports this one twice, at the same span.
    [InlineData("\"use strict\";\ntry {} catch (eval) {}\n", new[] { 1100, 1100 })]
    // binder.ts:2618-2624 — `eval` as the left-hand side of an assignment.
    [InlineData("\"use strict\";\neval = 1;\n", new[] { 1100 })]
    // binder.ts:2711-2719 — a postfix update.
    [InlineData("\"use strict\";\neval++;\n", new[] { 1100 })]
    // binder.ts:2721-2728 — a prefix `++`/`--`; any other prefix operator reports nothing.
    [InlineData("\"use strict\";\n++eval;\n", new[] { 1100 })]
    [InlineData("\"use strict\";\n!eval;\n", new int[0])]
    // binder.ts:2648-2658 reached from `bindParameter` (binder.ts:3686-3692) — a parameter named `eval`.
    [InlineData("\"use strict\";\nfunction f(eval) {}\n", new[] { 1100 })]
    // binder.ts:2674-2679 reached from `bindFunctionExpression` (binder.ts:3730) — a function expression's own name.
    [InlineData("\"use strict\";\nvar f = function eval() {};\n", new[] { 1100 })]
    public void CheckStrictModeEvalOrArguments_ReportsTheInvalidUse(string text, int[] expected)
    {
        var file = ParseAndBind("test.ts", text);

        Assert.Equal(expected, BindDiagnosticCodes(file));
    }

    [Fact]
    public void CheckStrictModeEvalOrArguments_NamesTheClassWhenTheContextIsAClass()
    {
        // The specialized message is chosen by `getContainingClass` (binder.ts:2664-2666 → 1210). The node's class
        // ancestry is what picks it, not the file's strictness, so the check fires on a method parameter with no
        // module and no prologue.
        var file = ParseAndBind("test.ts", "class C { m(eval) {} }\n");

        Assert.Equal(new[] { 1210 }, BindDiagnosticCodes(file));
    }

    // ---------------------------------------------------------------------------------------------
    // The remaining checkStrictMode arms each carry their own diagnostic
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void CheckStrictModeDeleteExpression_ReportsADeleteOfAnIdentifier()
    {
        // binder.ts:2634-2642 → 1102, anchored at the operand's own span.
        var file = ParseAndBind("test.ts", "\"use strict\";\ndelete x;\n");

        Assert.Equal(new[] { 1102 }, BindDiagnosticCodes(file));
    }

    [Fact]
    public void CheckStrictModeWithStatement_ReportsAWithStatementInStrictMode()
    {
        // binder.ts:2730-2735 → 1101 through `errorOnFirstToken`, which anchors at the statement's first token
        // (binder.ts:2746-2749).
        var file = ParseAndBind("test.ts", "\"use strict\";\nwith (x) {}\n");

        Assert.Equal(new[] { 1101 }, BindDiagnosticCodes(file));
    }

    [Fact]
    public void CheckStrictModeLabeledStatement_ReportsALabeledDeclarationOnlyFromES2015On()
    {
        // binder.ts:2737-2744 → 1344, gated on `getEmitScriptTarget(options) >= ScriptTarget.ES2015`; at the ES5
        // target the same statement is not reported.
        Assert.Equal(new[] { 1344 }, BindDiagnosticCodes(ParseAndBind("test.ts", "\"use strict\";\nl: var x = 1;\n")));
        Assert.Empty(BindDiagnosticCodes(ParseAndBind("test.ts", "\"use strict\";\nl: var x = 1;\n", ScriptTarget.ES5)));
    }

    [Theory]
    // binder.ts:2695-2709 → 1250 for a function declaration in a block, 1251 when the block sits in a class — the
    // message is chosen by `getStrictModeBlockScopeFunctionDeclarationMessage` (binder.ts:2681-2693).
    [InlineData("\"use strict\";\n{ function f() {} }\n", 1250)]
    [InlineData("class C { m() { { function f() {} } } }\n", 1251)]
    public void CheckStrictModeFunctionDeclaration_ReportsABlockScopedFunctionBelowES2015(string text, int expected)
    {
        var file = ParseAndBind("test.ts", text, ScriptTarget.ES5);

        Assert.Equal(new[] { expected }, BindDiagnosticCodes(file));
    }

    [Fact]
    public void CheckStrictModeFunctionDeclaration_DoesNotReportFromES2015On()
    {
        // `languageVersion < ScriptTarget.ES2015` is the whole guard (binder.ts:2696): the same block is legal from
        // ES2015 on, and this check has no `inStrictMode` test of its own.
        Assert.Empty(BindDiagnosticCodes(ParseAndBind("test.ts", "\"use strict\";\n{ function f() {} }\n")));
    }

    // ---------------------------------------------------------------------------------------------
    // The JSDoc tag lookups (Utilities/Utilities.JSDoc.cs)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void GetJSDocTags_WalksUpToTheStatementThatCarriesTheComment()
    {
        // The comment sits on the variable *statement*, so the lookup from the declaration has to take the
        // `getNextJSDocCommentLocation` step (utilities.ts:4710-4743 → `getSingleVariableOfVariableStatement`), which
        // is what makes the tag reachable from the declaration at all (utilities.ts:4658-4682).
        var file = Parse("/** @type {number} */\nvar x;\n");
        var variable = ((VariableStatement)file.Statements![0]).DeclarationList!.Declarations![0];

        var tags = Utilities.GetJSDocTags(variable);

        Assert.Single(tags);
        Assert.Equal(SyntaxKind.JSDocTypeTag, tags[0].Kind);
    }

    [Fact]
    public void GetJSDocTypeTag_ReturnsTheTagAndItsType()
    {
        // The one caller in the binder (Binder.DeclarationBinders.cs:79) only asks whether this is null, but the
        // answer is the tag whose `typeExpression.type` is present (utilitiesPublic.ts:1204-1211).
        var file = Parse("/** @type {number} */\nvar x;\n");
        var variable = ((VariableStatement)file.Statements![0]).DeclarationList!.Declarations![0];

        var tag = Utilities.GetJSDocTypeTag(variable);

        Assert.NotNull(tag);
        Assert.Equal(SyntaxKind.NumberKeyword, ((Node)tag!.TypeExpression!.Type!).Kind);
    }

    [Fact]
    public void GetJSDocParameterTags_MatchesAnIdentifierParameterByName()
    {
        var file = Parse("/**\n * @param p the p\n * @param other not this one\n */\nfunction f(p: number) {}\n");
        var function = (FunctionDeclaration)file.Statements![0];

        var tags = Utilities.GetJSDocParameterTags(function.Parameters![0]);

        var tag = Assert.Single(tags);
        Assert.Equal("p", Utilities.IdText(tag.Name!));
    }

    [Fact]
    public void GetJSDocParameterTags_MatchesABindingPatternParameterByPosition()
    {
        // A parameter whose name is a binding pattern has no `escapedText` to match on, so the tags are taken by
        // position in the signature's parameter list (utilitiesPublic.ts:1042-1049).
        var file = Parse("/**\n * @param a first\n * @param b second\n */\nfunction g([a, b]: number[]) {}\n");
        var function = (FunctionDeclaration)file.Statements![0];

        var tags = Utilities.GetJSDocParameterTags(function.Parameters![0]);

        var tag = Assert.Single(tags);
        Assert.Equal("a", Utilities.IdText(tag.Name!));
    }

    [Fact]
    public void GetJSDocTypeParameterTags_MatchesATemplateTagByName()
    {
        var file = Parse("/**\n * @template T\n */\nfunction h<T>() {}\n");
        var function = (FunctionDeclaration)file.Statements![0];

        var tags = Utilities.GetJSDocTypeParameterTags(function.TypeParameters![0]);

        Assert.Equal(SyntaxKind.JSDocTemplateTag, Assert.Single(tags).Kind);
    }

    // ---------------------------------------------------------------------------------------------
    // The JSDoc deferral set
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void DelayedBindJSDocTypedefTag_DeclaresTheTypedefNameInTheEnclosingContainer()
    {
        // `@typedef {number} T` on a function declaration: the tag's own name is not declared when the tag is visited
        // (`bindJSDocTypeAlias`, binder.ts:2114-2124), only by the deferred pass, which binds the type expression and
        // then declares the name — here into the source file's locals, as a type alias, beside the function
        // (binder.ts:2511-2513's `bindBlockScopedDeclaration` against the enclosing block-scope container).
        var file = ParseAndBind("test.js", "/** @typedef {number} T */\nfunction f() {}\n");

        Assert.Equal(SymbolFlags.TypeAlias, file.Locals!["T"].Flags);
        Assert.IsType<JSDocTypedefTag>(file.Locals["T"].Declarations![0]);
        Assert.Equal(SymbolFlags.Function, file.Locals["f"].Flags);
    }

    [Fact]
    public void DelayedBindJSDocTypedefTag_IsNotReachedInATypeScriptFile()
    {
        // Control: the same source in a `.ts` file declares nothing from JSDoc — `bindJSDoc` only binds comments in
        // JS files (binder.ts:2810-2814), so the deferral list stays empty and `T` never reaches the locals.
        var file = ParseAndBind("test.ts", "/** @typedef {number} T */\nfunction f() {}\n");

        Assert.False(file.Locals!.ContainsKey("T"));
    }

    [Fact]
    public void BindJSDocClassTag_AddsTheClassFlagToTheAnnotatedFunction()
    {
        // `@class` on a function declaration makes the function a class: `addDeclarationToSymbol(host.symbol, host,
        // SymbolFlags.Class)` (binder.ts:2126-2132), so the symbol ends up carrying both `Function` and `Class`.
        var file = ParseAndBind("test.js", "/** @class */\nfunction f() {}\n");

        Assert.Equal(SymbolFlags.Function | SymbolFlags.Class, file.Locals!["f"].Flags);
    }

    [Fact]
    public void BindJSDocImports_BindsTheDeferredNamedImport()
    {
        // `bindJSDocImportTag` binds only the tag name, the module specifier and the attributes — deliberately not the
        // clause (binder.ts:2134-2142) — so `bindJSDocImports` walks the clause once the file is bound, against the
        // host's enclosing container (binder.ts:2526-2556). The named binding becomes an alias in the file's locals.
        var file = ParseAndBind("test.js", "/** @import { x } from \"./m\" */\nconst y = 1;\n");

        Assert.Equal(SymbolFlags.Alias, file.Locals!["x"].Flags);
        Assert.Equal(SymbolFlags.BlockScopedVariable, file.Locals["y"].Flags);
    }

    [Fact]
    public void BindJSDocImports_BindsADefaultImportNameToo()
    {
        // The default half of the clause is the same pass's work: it reaches `bindImportClause`, which declares
        // `node.name` (binder.ts:2545).
        var file = ParseAndBind("test.js", "/** @import z from \"./m\" */\nconst y = 1;\n");

        Assert.Equal(SymbolFlags.Alias, file.Locals!["z"].Flags);
    }
}
