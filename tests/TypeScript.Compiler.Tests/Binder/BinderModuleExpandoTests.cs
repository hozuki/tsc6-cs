namespace TypeScript.Compiler.Tests;

/// <summary>
/// M5-4 的模块 / 导出 / JS 动态赋值集合：外部模块判定、CommonJS 的四种导出写法、<c>export</c> 家族、环境模块名字模式，
/// 以及 expando（<c>this.x</c> / 原型 / 静态属性 / <c>Object.defineProperty</c>）落进符号表的位置。
/// </summary>
/// <remarks>
/// 断言的全部是上游 binder 直接写在树和符号表上的东西 —— <c>file.symbol</c>/<c>file.locals</c>/<c>symbol.exports</c>/
/// <c>symbol.members</c>、<c>commonJsModuleIndicator</c>、<c>patternAmbientModules</c>、
/// <c>jsGlobalAugmentations</c>、<c>bindDiagnostics</c> —— 不经过 checker，所以不需要 M6。绝大多数用例经由
/// <c>bindSourceFile</c> 的入口走完整条 <c>bindWorker → bindChildren</c> 路径；也就是说它们同时钉住 M5-5 的分派分支和
/// 本切片的成员，两边任何一边退化成"没接上"都会让用例红。
/// </remarks>
public class BinderModuleExpandoTests
{
    private static SourceFile ParseAndBind(string fileName, string text, ScriptKind scriptKind = ScriptKind.TS)
    {
        var file = Parser.CreateSourceFile(fileName, text, ScriptTarget.ESNext, /*setParentNodes*/ true, scriptKind);
        Binder.BindSourceFile(file, new FakeCompilerOptions { Target = ScriptTarget.ESNext });
        return file;
    }

    // ---------------------------------------------------------------------------------------------
    // bindSourceFileIfExternalModule / bindSourceFileAsExternalModule / setCommonJsModuleIndicator
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void BindSourceFile_DeclaresTheExternalModuleSymbolFromTheFileName()
    {
        var file = ParseAndBind("test.ts", "import { q } from \"./m\";");

        // The parser recovered the external-module indicator from the import, so `bindSourceFileAsExternalModule` ran:
        // one anonymous `ValueModule` symbol named `"<fileName without extension>"` (binder.ts:3122-3124).
        var symbol = file.Symbol;
        Assert.NotNull(symbol);
        Assert.Equal(SymbolFlags.ValueModule, symbol.Flags);
        Assert.Equal("\"test\"", symbol.EscapedName);
        // `addDeclarationToSymbol` sets the value declaration for a `Value`-flagged symbol (binder.ts:652-654), and the
        // source file itself is the declaration.
        Assert.Same(file, symbol.ValueDeclaration);
        Assert.Same(file, symbol.Declarations![0]);
    }

    [Fact]
    public void BindSourceFile_ReportsACommonJsModuleFromARequireCall()
    {
        var file = ParseAndBind("test.js", "require(\"m\");", ScriptKind.JS);

        // `bindCallExpression` (binder.ts:3598-3603) is the only caller of `setCommonJsModuleIndicator` for a call, and
        // it both records the indicator and binds the file as an external module (binder.ts:3188-3199).
        var call = (CallExpression)((ExpressionStatement)file.Statements![0]).Expression!;
        Assert.Same(call, file.CommonJsModuleIndicator);
        Assert.Equal("\"test\"", file.Symbol!.EscapedName);
    }

    [Fact]
    public void BindSourceFile_AddsTheJsonModuleExportsEquivalent()
    {
        var file = Parser.CreateSourceFile("test.json", "{ \"a\": 1 }", ScriptTarget.JSON, /*setParentNodes*/ true);
        Binder.BindSourceFile(file, new FakeCompilerOptions { Target = ScriptTarget.JSON });

        // A JSON file is not an ES module, so it takes the `isJsonSourceFile` arm: the file is bound as an external
        // module and a `Property` symbol equivalent to `module.exports = {}` is declared in its exports under the
        // source file's own declaration name, `export=` (binder.ts:3113-3119).
        var symbol = file.Symbol!;
        Assert.Equal(SymbolFlags.ValueModule, symbol.Flags);
        var exportEquals = symbol.Exports!["export="];
        Assert.Equal(SymbolFlags.Property, exportEquals.Flags);
        Assert.Same(file, exportEquals.Declarations![0]);
        // The original module symbol is restored after `declareSymbol` overwrote `file.symbol` (binder.ts:3118-3119).
        Assert.Same(symbol, file.Symbol);
    }

    // ---------------------------------------------------------------------------------------------
    // bindModuleExportsAssignment / bindExportsPropertyAssignment / bindExportAssignedObjectMemberAlias
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void BindSourceFile_BindsModuleExportsShorthandPropertiesAsAliases()
    {
        var file = ParseAndBind("test.js", "module.exports = { a, b };", ScriptKind.JS);

        // `module.exports = { a, b }` is classified `ModuleExports` but every property is a shorthand, so the
        // shorthand arm runs instead of the `export=` declaration: one `Alias | Assignment` symbol per member
        // (binder.ts:3254-3257, 3263-3265).
        var exports = file.Symbol!.Exports!;
        Assert.Equal(SymbolFlags.Alias | SymbolFlags.Assignment, exports["a"].Flags);
        Assert.Equal(SymbolFlags.Alias | SymbolFlags.Assignment, exports["b"].Flags);
        Assert.False(exports.ContainsKey("export="));
    }

    [Fact]
    public void BindSourceFile_DeclaresModuleExportsAssignmentsUnderTheExportEqualsName()
    {
        var file = ParseAndBind("test.js", "module.exports = f();", ScriptKind.JS);

        // A non-shorthand right-hand side: `Property | ExportValue | ValueModule | Assignment` under the name
        // `getDeclarationName` gives the binary expression, `export=` (binder.ts:3247-3260).
        var binary = (BinaryExpression)((ExpressionStatement)file.Statements![0]).Expression!;
        var symbol = file.Symbol!.Exports!["export="];
        Assert.Equal(SymbolFlags.Property | SymbolFlags.ExportValue | SymbolFlags.ValueModule | SymbolFlags.Assignment, symbol.Flags);
        Assert.Same(binary, symbol.ValueDeclaration);
    }

    [Fact]
    public void BindSourceFile_DeclaresExportsPropertyAssignmentsOnTheModuleSymbol()
    {
        var file = ParseAndBind("test.js", "exports.foo = 1;", ScriptKind.JS);

        // `forEachIdentifierInEntityName` answers `file.symbol` for the `exports` identifier itself, so the
        // declaration lands in the module symbol's exports with the `ExportsProperty` flags (binder.ts:3217-3234).
        var left = (PropertyAccessExpression)((BinaryExpression)((ExpressionStatement)file.Statements![0]).Expression!).Left!;
        var symbol = file.Symbol!.Exports!["foo"];
        Assert.Equal(SymbolFlags.Property | SymbolFlags.ExportValue, symbol.Flags);
        Assert.Same(left, symbol.Declarations![0]);
    }

    [Fact]
    public void BindSourceFile_DeclaresObjectDefinePropertyExports()
    {
        var file = ParseAndBind("test.js", "Object.defineProperty(exports, \"bar\", { get: function () { return 1; } });", ScriptKind.JS);

        // `ObjectDefinePropertyExports` binds the call expression itself as the declaration, named from the second
        // argument — `getNonAssignedNameOfDeclaration`'s call-expression arm (binder.ts:3201-3215).
        var call = (CallExpression)((ExpressionStatement)file.Statements![0]).Expression!;
        var symbol = file.Symbol!.Exports!["bar"];
        Assert.Equal(SymbolFlags.Property | SymbolFlags.ExportValue, symbol.Flags);
        Assert.Same(call, symbol.Declarations![0]);
    }

    // ---------------------------------------------------------------------------------------------
    // bindThisPropertyAssignment / bindPrototypePropertyAssignment / bindSpecialPropertyAssignment
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void BindSourceFile_BindsThisPropertyAssignmentsToTheConstructorMembers()
    {
        var file = ParseAndBind("test.js", "function F() { this.x = 0; }", ScriptKind.JS);

        // `this.x = 0` inside a function: the property joins the function symbol's members as
        // `Property | Assignment` and the function is marked class-like (binder.ts:3267-3300, 3352).
        var constructor = file.Locals!["F"];
        var member = constructor.Members!["x"];
        Assert.Equal(SymbolFlags.Property | SymbolFlags.Assignment, member.Flags);
        Assert.Same(constructor, member.Parent);
        Assert.Equal(SymbolFlags.Function | SymbolFlags.Class, constructor.Flags);
    }

    [Fact]
    public void BindSourceFile_BindsPrototypeMemberAssignmentsToThePrototypeMembersTable()
    {
        var file = ParseAndBind("test.js", "function F() {}\nF.prototype.m = function () {};", ScriptKind.JS);

        // `F.prototype.m = function () {}` is a `PrototypeProperty` assignment: `bindPropertyAssignment` looks the
        // constructor up by name, marks it class-like and declares `m` in its *members* (the prototype side), with the
        // method flags `getAssignedExpandoInitializer` earns a function initializer (binder.ts:3384-3396, 3470-3517).
        var constructor = file.Locals!["F"];
        var member = constructor.Members!["m"];
        Assert.Equal(SymbolFlags.Method | SymbolFlags.Assignment, member.Flags);
        Assert.Same(constructor, member.Parent);
        Assert.Equal(SymbolFlags.Function | SymbolFlags.Class, constructor.Flags);
        // The class flag created an (empty) exports table, but the member went to the prototype side, `members`.
        Assert.Empty(constructor.Exports!);
    }

    [Fact]
    public void BindSourceFile_BindsStaticPropertyAssignmentsAndTheirIntermediateNamespaces()
    {
        var file = ParseAndBind("test.js", "function F() {}\nF.x = 1;", ScriptKind.JS);

        // `F.x = 1` is a plain `Property` assignment: the function symbol gains `Module | Assignment`, its `exports`
        // table comes into existence and `x` is declared there as `Property | Assignment` (binder.ts:3470-3517).
        var constructor = file.Locals!["F"];
        Assert.Equal(SymbolFlags.Function | SymbolFlags.Module | SymbolFlags.Assignment, constructor.Flags);
        var member = constructor.Exports!["x"];
        Assert.Equal(SymbolFlags.Property | SymbolFlags.Assignment, member.Flags);
        Assert.Same(constructor, member.Parent);
    }

    [Fact]
    public void BindSourceFile_MakesIntermediateNamespacesInTheGlobalAugmentationsTable()
    {
        var file = ParseAndBind("test.js", "A.B.x = 1;", ScriptKind.JS);

        // `A.B.x = 1` names nothing that exists, so `bindPotentiallyMissingNamespaces` walks the entity name and
        // creates `A` in `file.jsGlobalAugmentations` (the `parent === undefined` arm, binder.ts:3457-3461), `B` in
        // `A`'s exports, and finally `x` in `B`'s exports.
        var globalAugmentations = file.JsGlobalAugmentations;
        Assert.NotNull(globalAugmentations);
        Assert.Equal(SymbolFlags.Module | SymbolFlags.Assignment, globalAugmentations["A"].Flags);
        var b = globalAugmentations["A"].Exports!["B"];
        Assert.Equal(SymbolFlags.Module | SymbolFlags.Assignment, b.Flags);
        Assert.Equal(SymbolFlags.Property | SymbolFlags.Assignment, b.Exports!["x"].Flags);
    }

    // ---------------------------------------------------------------------------------------------
    // bindExportAssignment / bindExportDeclaration / bindImportClause
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void BindSourceFile_BindsExportDefaultWithAnIdentifierAsAnAlias()
    {
        var file = ParseAndBind("test.ts", "export default x;");

        // `export default <identifier>` exports all meanings of the identifier, so the symbol is an `Alias`;
        // `isExportEquals` is false, so no value declaration is forced (binder.ts:3126-3146).
        var symbol = file.Symbol!.Exports!["default"];
        Assert.Equal(SymbolFlags.Alias, symbol.Flags);
        Assert.Null(symbol.ValueDeclaration);
    }

    [Fact]
    public void BindSourceFile_BindsExportEqualsUnderTheExportEqualsNameAndForcesAValueDeclaration()
    {
        var file = ParseAndBind("test.ts", "export = q;");

        // The `isExportEquals` arm runs `setValueDeclaration` because the module already has other exports: an
        // `Alias` symbol does not carry the `Value` flag, so without it the symbol would have no value declaration
        // (binder.ts:3141-3144).
        var assignment = (ExportAssignment)file.Statements![0];
        var symbol = file.Symbol!.Exports!["export="];
        Assert.Equal(SymbolFlags.Alias, symbol.Flags);
        Assert.Same(assignment, symbol.ValueDeclaration);
    }

    [Fact]
    public void BindSourceFile_CollectsExportStarDeclarationsInTheExportStarSymbol()
    {
        var file = ParseAndBind("test.ts", "export * from \"./m\";");

        // Every `export *` without a clause merges into one symbol named `__export` (binder.ts:3165-3180).
        Assert.Equal(SymbolFlags.ExportStar, file.Symbol!.Exports![InternalSymbolName.ExportStar.Value].Flags);
    }

    [Fact]
    public void BindSourceFile_BindsADefaultImportedNameIntoTheFileLocals()
    {
        var file = ParseAndBind("test.ts", "import d from \"./m\";");

        // An import clause with a name is declared as an `Alias`; the file is an external module, so it travels through
        // `declareModuleMember`, which sends a non-`export` alias into the container's locals (binder.ts:3182-3186).
        Assert.Equal(SymbolFlags.Alias, file.Locals!["d"].Flags);
    }

    // ---------------------------------------------------------------------------------------------
    // bindModuleDeclaration (+ tryParsePattern)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void BindSourceFile_RecordsAnAmbientModulePattern()
    {
        var file = ParseAndBind("test.d.ts", "declare module \"foo*\" { }");

        // `tryParsePattern` turns the one-star name into a `Pattern`, and the module symbol is appended to
        // `file.patternAmbientModules` beside it (binder.ts:2352-2368).
        var module = (ModuleDeclaration)file.Statements![0];
        var patterns = file.PatternAmbientModules;
        Assert.NotNull(patterns);
        var entry = Assert.IsType<PatternAmbientModule>(Assert.Single(patterns));
        Assert.Equal("foo", entry.Pattern.Prefix);
        Assert.Equal("", entry.Pattern.Suffix);
        Assert.Equal("\"foo*\"", entry.Symbol.EscapedName);
        Assert.Equal(SymbolFlags.ValueModule, entry.Symbol.Flags);
        // The declaration itself is declared in the container (a script, so the file's locals) under the quoted name.
        Assert.Same(module, file.Locals!["\"foo*\""].Declarations![0]);
    }

    [Fact]
    public void BindSourceFile_DoesNotRecordAPatternForAnAmbientModuleWithoutAStar()
    {
        // `tryParsePattern` answers the input string when there is no `*`, and TS's `pattern && !isString(pattern)`
        // test then appends nothing — the port's `false`/null answer does the same (binder.ts:2366-2368).
        var file = ParseAndBind("test.d.ts", "declare module \"bar\" { }");
        Assert.Null(file.PatternAmbientModules);
        Assert.Equal(SymbolFlags.ValueModule, file.Locals!["\"bar\""].Flags);
    }

    // ---------------------------------------------------------------------------------------------
    // bindNamespaceExportDeclaration
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void BindSourceFile_DeclaresANamespaceExportInGlobalExports()
    {
        // `export {}` makes the declaration file an external module, which is the third condition the diagnostic chain
        // checks; the declaration then lands in `file.symbol.globalExports` as an alias (binder.ts:3148-3163).
        var file = ParseAndBind("test.d.ts", "export as namespace MyLib;\nexport {};");

        Assert.Empty(file.BindDiagnostics!);
        var symbol = file.Symbol!.GlobalExports!["MyLib"];
        Assert.Equal(SymbolFlags.Alias, symbol.Flags);
        Assert.Same(file.Statements![0], symbol.Declarations![0]);
    }

    [Fact]
    public void BindSourceFile_ReportsANamespaceExportOutsideAModule()
    {
        // A declaration file that is not a module: the second arm of the diagnostic chain fires and no symbol is
        // declared (binder.ts:3152-3156). A `NamespaceExportDeclaration` carries no modifiers (the parser strips the
        // `export` keyword before dispatching), so `Modifiers_cannot_appear_here` is not reported.
        var file = ParseAndBind("test.d.ts", "export as namespace MyLib;");

        var diagnostic = Assert.Single(file.BindDiagnostics!);
        Assert.Equal(1314, diagnostic.Code);
        Assert.Null(file.Symbol);
    }

    // ---------------------------------------------------------------------------------------------
    // bindFunctionOrConstructorType / bindObjectLiteralExpression / bindJsxAttributes / bindJsxAttribute
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void BindSourceFile_WrapsAFunctionTypeInASyntheticTypeLiteralSymbol()
    {
        var file = ParseAndBind("test.ts", "type T = () => void;");

        // `bindFunctionOrConstructorType` gives the signature symbol a type-literal parent and registers the signature
        // as its sole member, so the node's symbol is the type-literal one (binder.ts:2400-2414).
        var typeAlias = (TypeAliasDeclaration)file.Statements![0];
        var functionType = (FunctionTypeNode)typeAlias.Type!;
        var typeLiteral = functionType.Symbol!;
        Assert.Equal(SymbolFlags.TypeLiteral, typeLiteral.Flags);
        Assert.Equal(InternalSymbolName.Type.Value, typeLiteral.EscapedName);
        var call = Assert.Single(typeLiteral.Members!);
        Assert.Equal(InternalSymbolName.Call.Value, call.Key);
        Assert.Equal(SymbolFlags.Signature, call.Value.Flags);
        // The member is put in with a raw table write, not through `declareSymbol`, so it has no parent pointer —
        // exactly as in the reference (binder.ts:2410-2413).
        Assert.Null(call.Value.Parent);
    }

    [Fact]
    public void BindSourceFile_BindsJsxAttributesAndTheirAttributes()
    {
        var file = ParseAndBind("test.tsx", "const x = <div a=\"1\" />;", ScriptKind.TSX);

        // A JSX attribute list is an anonymous `ObjectLiteral` declaration named `__jsxAttributes`; each attribute is
        // declared in its members table (binder.ts:2420-2426), the attributes node being a container
        // (`getContainerFlags`).
        var statement = (VariableStatement)file.Statements![0];
        var jsx = (JsxSelfClosingElement)statement.DeclarationList!.Declarations![0].Initializer!;
        var attributes = jsx.Attributes!;
        Assert.Equal(SymbolFlags.ObjectLiteral, attributes.Symbol!.Flags);
        Assert.Equal(InternalSymbolName.JSXAttributes.Value, attributes.Symbol.EscapedName);
        Assert.Equal(SymbolFlags.Property, attributes.Symbol.Members!["a"].Flags);
    }
}
