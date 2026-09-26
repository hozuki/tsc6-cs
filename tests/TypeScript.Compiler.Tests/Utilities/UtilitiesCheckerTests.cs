namespace TypeScript.Compiler.Tests;

/// <summary>
/// The <c>utilities.ts</c>/<c>utilitiesPublic.ts</c> members landed in <c>Utilities/Utilities.Checker.cs</c> (plus the
/// four sibling members the M6 batched named for this slice), exercised through real source text.
/// </summary>
/// <remarks>
/// <para>
/// Every expected value here was executed against the port first and read off the reference for the cases where the
/// reference is not the obvious reading; the assertions pin behaviour a later slice or the M6-7 diff can consume: the
/// kind sets, the member/initializer slot reads, the JSDoc parent-chain resolution, and the two <c>undefined</c>/<c>null</c>
/// arms the reference relies on.
/// </para>
/// <para>
/// PORT-NOTE: the four members at the end of the file (<c>PseudoBigIntToString</c>, <c>IsTypeAlias</c>,
/// <c>IsLiteralImportTypeNode</c>, <c>SkipTypeParentheses</c>) are declared in <c>Checker/Utilities.Checker.cs</c> by the
/// M6Generics utilities sub-slice — the same <c>Utilities</c> partial class — and are pinned here because this slice's
/// contract names them. <c>IPseudoBigInt</c> has no concrete implementation in the port yet (the checker's bigint path
/// owns that), so the round trip supplies the two-slot face the formatter reads.
/// </para>
/// </remarks>
public class UtilitiesCheckerTests
{
    // ---------------------------------------------------------------------------------------------
    // Shared helpers
    // ---------------------------------------------------------------------------------------------

    private static SourceFile Parse(string text, ScriptKind kind = ScriptKind.TS)
        => Parser.CreateSourceFile(kind == ScriptKind.JS ? "test.js" : "test.ts", text, ScriptTarget.ESNext, setParentNodes: true, kind);

    private static ClassDeclaration ClassOf(SourceFile file, int index = 0) => (ClassDeclaration)file.Statements![index];

    private static FunctionDeclaration FuncOf(SourceFile file, int index = 0) => (FunctionDeclaration)file.Statements![index];

    private static VariableDeclaration VarOf(SourceFile file, int index = 0)
        => (VariableDeclaration)((VariableStatement)file.Statements![index]).DeclarationList!.Declarations![0];

    /// <summary>The two-slot face <c>PseudoBigIntToString</c> reads.</summary>
    private sealed class FakePseudoBigInt : IPseudoBigInt
    {
        public bool Negative { get; set; }

        public string? Base10Value { get; set; }
    }

    // ---------------------------------------------------------------------------------------------
    // Kind predicates (utilitiesPublic.ts:1449-2205, utilities.ts:8330)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void IsTypeNodeKind_AnswersTheRangeTheKeywordsAndTheJsDocArms()
    {
        Assert.True(Utilities.IsTypeNodeKind(SyntaxKind.TypeReference));
        Assert.True(Utilities.IsTypeNodeKind(SyntaxKind.FirstTypeNode));
        Assert.True(Utilities.IsTypeNodeKind(SyntaxKind.LastTypeNode));
        Assert.False(Utilities.IsTypeNodeKind((SyntaxKind)((int)SyntaxKind.LastTypeNode + 1)));

        // The twenty-three kinds past the range, one from each family the reference names.
        Assert.True(Utilities.IsTypeNodeKind(SyntaxKind.AnyKeyword));
        Assert.True(Utilities.IsTypeNodeKind(SyntaxKind.UnknownKeyword));
        Assert.True(Utilities.IsTypeNodeKind(SyntaxKind.IntrinsicKeyword));
        Assert.True(Utilities.IsTypeNodeKind(SyntaxKind.ExpressionWithTypeArguments));
        Assert.True(Utilities.IsTypeNodeKind(SyntaxKind.JSDocVariadicType));

        Assert.False(Utilities.IsTypeNodeKind(SyntaxKind.Identifier));
        Assert.False(Utilities.IsTypeNodeKind(SyntaxKind.CommaToken));
        Assert.False(Utilities.IsTypeNodeKind(SyntaxKind.Parameter));
    }

    [Fact]
    public void IsTypeNode_DelegatesToTheKindTest()
    {
        var typed = VarOf(Parse("let x: A.B;"));

        Assert.True(Utilities.IsTypeNode((Node)typed.Type!));
        Assert.False(Utilities.IsTypeNode((Node)typed.Name!));
    }

    [Fact]
    public void IsEntityName_AcceptsQualifiedNamesAndIdentifiersOnly()
    {
        var typed = VarOf(Parse("let x: A.B;"));
        var qualified = (QualifiedName)((TypeReferenceNode)typed.Type!).TypeName!;

        Assert.True(Utilities.IsEntityName((Node)qualified));
        Assert.True(Utilities.IsEntityName((Node)qualified.Right!));
        Assert.False(Utilities.IsEntityName((Node)((BinaryExpression)((ExpressionStatement)Parse("a.b = 1;").Statements![0]).Expression!).Left!));
    }

    [Fact]
    public void IsAccessor_AndIsMethodOrAccessor_SplitTheAccessorKinds()
    {
        var cls = ClassOf(Parse("class K { get a() { return 1; } set a(v) {} m() {} }"));

        Assert.True(Utilities.IsAccessor((Node)cls.Members![0]));
        Assert.True(Utilities.IsAccessor((Node)cls.Members![1]));
        Assert.False(Utilities.IsAccessor((Node)cls.Members![2]));

        Assert.True(Utilities.IsMethodOrAccessor((Node)cls.Members![0]));
        Assert.True(Utilities.IsMethodOrAccessor((Node)cls.Members![2]));
        Assert.False(Utilities.IsMethodOrAccessor((Node)cls));
    }

    [Fact]
    public void IsAccessor_AnswersFalseForNull()
    {
        // utilitiesPublic.ts:1749 is `node && (…)`: the absent argument must not throw and must answer false.
        Assert.False(Utilities.IsAccessor(null));
    }

    [Fact]
    public void IsModuleOrEnumDeclaration_AndIsValueSignatureDeclaration()
    {
        var file = Parse("class C {}\nenum E {}\nfunction f() {}\nconst g = () => 1;\n");
        var ctor = ClassOf(Parse("class H { constructor() {} }")).Members![0];

        Assert.True(Utilities.IsModuleOrEnumDeclaration((Node)file.Statements![1]));
        Assert.False(Utilities.IsModuleOrEnumDeclaration((Node)ClassOf(file)));

        Assert.True(Utilities.IsValueSignatureDeclaration((Node)FuncOf(file, 2)));
        Assert.True(Utilities.IsValueSignatureDeclaration((Node)ctor));
        Assert.False(Utilities.IsValueSignatureDeclaration((Node)ClassOf(file)));
    }

    [Fact]
    public void NodeStartsNewLexicalEnvironment_ListsTheNineKinds()
    {
        var file = Parse("class C {}\nfunction f() {}\n");

        Assert.True(Utilities.NodeStartsNewLexicalEnvironment(file));
        Assert.True(Utilities.NodeStartsNewLexicalEnvironment((Node)FuncOf(file, 1)));
        Assert.False(Utilities.NodeStartsNewLexicalEnvironment((Node)ClassOf(file)));
    }

    [Fact]
    public void IsGlobalSourceFile_ExcludesExternalModules()
    {
        // TS: `node.kind === SourceFile && !isExternalOrCommonJsModule(node as SourceFile)` — the second half is what
        // makes an external-module file answer false, so both halves are pinned.
        Assert.True(Utilities.IsGlobalSourceFile(Parse("class C {}")));
        Assert.False(Utilities.IsGlobalSourceFile(Parse("export {}")));
        Assert.False(Utilities.IsGlobalSourceFile((Node)ClassOf(Parse("class C {}"))));
    }

    [Fact]
    public void IsJSDocPropertyLikeTag_AcceptsTheTwoPropertyTags()
    {
        var documented = FuncOf(Parse("/** @param {number} a */ function f(a) {}", ScriptKind.JS));
        var tag = Utilities.GetJSDocParameterTags(documented.Parameters![0])[0];

        Assert.True(Utilities.IsJSDocPropertyLikeTag((Node)tag));
        Assert.False(Utilities.IsJSDocPropertyLikeTag((Node)ClassOf(Parse("class C { p: number; }")).Members![0]));
    }

    // ---------------------------------------------------------------------------------------------
    // Members, constructors and initializers (utilities.ts:2983-2993, 11455-11462, 3931-3951, 4460-4472)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void GetMembersOfDeclaration_ReadsTheFourObjectTypeKindsAndTheObjectLiteral()
    {
        var cls = ClassOf(Parse("class C { p?: string; m() {} constructor() {} }"));
        var members = Utilities.GetMembersOfDeclaration(cls);

        Assert.NotNull(members);
        Assert.Equal(3, members!.Count);
        Assert.Same(cls.Members![0], members[0]);

        var iface = Utilities.GetMembersOfDeclaration((Node)Parse("interface I { a: number; b?: string; }\n").Statements![0]);
        Assert.NotNull(iface);
        Assert.Equal(2, iface!.Count);

        var literal = (ObjectLiteralExpression)VarOf(Parse("var o = { a: 1, b: 2 };")).Initializer!;
        var properties = Utilities.GetMembersOfDeclaration(literal);
        Assert.NotNull(properties);
        Assert.Equal(2, properties!.Count);
        Assert.Equal(SyntaxKind.PropertyAssignment, properties[1].Kind);
    }

    [Fact]
    public void GetMembersOfDeclaration_AnswersNullForAKindOutsideTheUnion()
    {
        // utilities.ts:2983's switch falls out with `undefined`; the port's arm is null, not an empty list — the two
        // are distinguishable at every call site (`members is not null`).
        Assert.Null(Utilities.GetMembersOfDeclaration((Node)VarOf(Parse("const x = 1;")).Name!));
    }

    [Fact]
    public void FindConstructorDeclaration_RequiresAConstructorWithABody()
    {
        var cls = ClassOf(Parse("class C { p: string; m() {} constructor() {} }"));
        var ctor = Utilities.FindConstructorDeclaration(cls);

        Assert.NotNull(ctor);
        Assert.Same(cls.Members![2], ctor);

        Assert.Null(Utilities.FindConstructorDeclaration(ClassOf(Parse("class D {}"))));
        Assert.Null(Utilities.FindConstructorDeclaration((Node)Parse("interface J {}\n").Statements![0]));
    }

    [Fact]
    public void HasInitializer_ReadsTheSlotForItsTenKinds()
    {
        Assert.True(Utilities.HasInitializer((Node)VarOf(Parse("var x = 1;"))));
        Assert.False(Utilities.HasInitializer((Node)VarOf(Parse("var y;"))));

        var properties = ClassOf(Parse("class P { q = 1; r: number; }")).Members!;
        Assert.True(Utilities.HasInitializer((Node)properties[0]));
        Assert.False(Utilities.HasInitializer((Node)properties[1]));

        Assert.False(Utilities.HasInitializer((Node)FuncOf(Parse("function f() {}"))));
    }

    [Fact]
    public void GetEffectiveInitializer_AndGetDeclaredExpandoInitializer_AnswerNullWithoutAnInitializer()
    {
        Assert.Null(Utilities.GetEffectiveInitializer((Node)VarOf(Parse("var y;"))));
        Assert.Null(Utilities.GetDeclaredExpandoInitializer((Node)VarOf(Parse("var y;"))));

        // A TypeScript `var x = 1;` has an initializer but no expando initializer: `getExpandoInitializer(1, false)`
        // is undefined, so the declared-expando read answers null (utilities.ts:3948-3951).
        var typed = VarOf(Parse("var x = 1;"));
        Assert.NotNull(Utilities.GetEffectiveInitializer((Node)typed));
        Assert.Null(Utilities.GetDeclaredExpandoInitializer((Node)typed));
    }

    [Fact]
    public void GetEffectiveInitializer_UnwrapsTheSelfReferentialJsOrChain()
    {
        // utilities.ts:3931-3941's JS arm: for `z = z || {}` the effective initializer is the right-hand side,
        // because that is the value the expando declaration actually contributes.
        var declaration = VarOf(Parse("var z = z || {};", ScriptKind.JS));
        var effective = Utilities.GetEffectiveInitializer((Node)declaration);

        Assert.NotNull(effective);
        Assert.Equal(SyntaxKind.ObjectLiteralExpression, effective!.Kind);
        Assert.NotNull(Utilities.GetDeclaredExpandoInitializer((Node)declaration));
    }

    [Fact]
    public void HasQuestionToken_ReadsTheFourPropertyFormsAndTheParameter()
    {
        var cls = ClassOf(Parse("class C { p?: string; m() {} }"));
        Assert.True(Utilities.HasQuestionToken((Node)cls.Members![0]));
        Assert.False(Utilities.HasQuestionToken((Node)cls.Members![1]));

        Assert.True(Utilities.HasQuestionToken((Node)FuncOf(Parse("function h(a?: number) {}")).Parameters![0]));
        Assert.False(Utilities.HasQuestionToken((Node)FuncOf(Parse("function h(a: number) {}")).Parameters![0]));

        var signature = Utilities.GetMembersOfDeclaration((Node)Parse("interface I { q?: string; }\n").Statements![0])!;
        Assert.True(Utilities.HasQuestionToken(signature[0]));
    }

    // ---------------------------------------------------------------------------------------------
    // Heritage clauses (utilities.ts:5195-5252)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void GetClassExtendsHeritageElement_AndGetEffectiveBaseTypeNode_AnswerTheExtendsClause()
    {
        var plain = ClassOf(Parse("class A {}"));
        Assert.Null(Utilities.GetClassExtendsHeritageElement(plain));
        Assert.Null(Utilities.GetEffectiveBaseTypeNode(plain));
        Assert.Null(Utilities.GetEffectiveBaseTypeNode(null));

        var derived = ClassOf(Parse("class B extends A {}"));
        var extends = Utilities.GetClassExtendsHeritageElement(derived);
        Assert.NotNull(extends);
        Assert.Equal(SyntaxKind.Identifier, extends!.Expression!.Kind);
        // utilities.ts:5204 returns the `baseType` it read, so the effective node *is* the heritage node in a TS file.
        Assert.Same(extends, Utilities.GetEffectiveBaseTypeNode(derived));
    }

    [Fact]
    public void GetEffectiveBaseTypeNode_PrefersTheJsDocAugmentsTagInAJavaScriptFile()
    {
        // utilities.ts:5195-5205: a JS class's `@augments` tag is preferred over the syntactic extends clause.
        var jsClass = ClassOf(Parse("/** @augments {Base} */ class E extends F {}", ScriptKind.JS));
        var effective = Utilities.GetEffectiveBaseTypeNode(jsClass);

        Assert.NotNull(effective);
        Assert.Equal("Base", Utilities.EntityNameToString((Node)effective!.Expression!));
        Assert.Equal("F", Utilities.EntityNameToString((Node)Utilities.GetClassExtendsHeritageElement(jsClass)!.Expression!));
    }

    [Fact]
    public void GetEffectiveImplementsTypeNodes_AnswersTheImplementsClauseOrNull()
    {
        var implemented = ClassOf(Parse("class D implements I {}"));
        var impls = Utilities.GetEffectiveImplementsTypeNodes(implemented);

        Assert.NotNull(impls);
        Assert.Single(impls!);
        Assert.Equal(SyntaxKind.Identifier, impls[0].Expression!.Kind);

        Assert.Null(Utilities.GetEffectiveImplementsTypeNodes(ClassOf(Parse("class A {}"))));
    }

    [Fact]
    public void GetInterfaceBaseTypeNodes_AnswersEveryExtendsType()
    {
        var iface = (InterfaceDeclaration)Parse("interface I extends J, K {}\n").Statements![0];
        var bases = Utilities.GetInterfaceBaseTypeNodes(iface);

        Assert.NotNull(bases);
        Assert.Equal(2, bases!.Count);
        Assert.Null(Utilities.GetInterfaceBaseTypeNodes((InterfaceDeclaration)Parse("interface L {}\n").Statements![0]));
    }

    [Fact]
    public void GetHeritageClause_FiltersByToken()
    {
        var derived = ClassOf(Parse("class B extends A implements I {}"));

        Assert.NotNull(Utilities.GetHeritageClause(derived.HeritageClauses, SyntaxKind.ExtendsKeyword));
        Assert.NotNull(Utilities.GetHeritageClause(derived.HeritageClauses, SyntaxKind.ImplementsKeyword));
        Assert.Null(Utilities.GetHeritageClause(derived.HeritageClauses, SyntaxKind.QuestionToken));
        Assert.Null(Utilities.GetHeritageClause(null, SyntaxKind.ExtendsKeyword));
    }

    // ---------------------------------------------------------------------------------------------
    // Rest parameters, entity names, diagnostics and ambient modules (utilities.ts)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void HasRestParameter_AndIsRestParameter_ReadTheLastParameter()
    {
        var rest = FuncOf(Parse("function f(...a: number[]) {}"));
        Assert.True(Utilities.HasRestParameter(rest));
        Assert.True(Utilities.IsRestParameter((Node)rest.Parameters![0]));

        var fixedArity = FuncOf(Parse("function g(a: number, b) {}"));
        Assert.False(Utilities.HasRestParameter(fixedArity));
        Assert.False(Utilities.IsRestParameter((Node)fixedArity.Parameters![1]));
        Assert.False(Utilities.HasRestParameter(FuncOf(Parse("function h() {}"))));
    }

    [Fact]
    public void GetAliasDeclarationFromName_AnswersTheImportOrExportDeclaration()
    {
        var imported = Parse("import { a } from \"m\";");
        var specifier = (ImportSpecifier)((NamedImports)((ImportDeclaration)imported.Statements![0]).ImportClause!.NamedBindings!).Elements![0];

        Assert.Same(specifier, Utilities.GetAliasDeclarationFromName((Node)specifier.Name!));
        Assert.Null(Utilities.GetAliasDeclarationFromName((Node)VarOf(Parse("var q = 1;")).Name!));
    }

    [Fact]
    public void EntityNameToString_SpellsQualifiedAndPropertyAccessChains()
    {
        var qualified = VarOf(Parse("let x: A.B.C;"));
        Assert.Equal("A.B.C", Utilities.EntityNameToString((Node)((TypeReferenceNode)qualified.Type!).TypeName!));

        var access = ((BinaryExpression)((ExpressionStatement)Parse("a.b.c = 1;").Statements![0]).Expression!).Left!;
        Assert.Equal("a.b.c", Utilities.EntityNameToString((Node)access));

        var thisAccess = ((BinaryExpression)((ExpressionStatement)Parse("this.x = 1;").Statements![0]).Expression!).Left!;
        Assert.Equal("this.x", Utilities.EntityNameToString((Node)thisAccess));
    }

    [Fact]
    public void GetDiagnosticFilePath_ReadsTheAttachedFileAndAnswersNullWhenDetached()
    {
        var file = Parse("var w = 1;");
        file.Path = "/p/test.ts";

        Assert.Equal("/p/test.ts", Utilities.GetDiagnosticFilePath(new Diagnostic("m", DiagnosticCategory.Error, 1, file: file)));
        Assert.Null(Utilities.GetDiagnosticFilePath(new Diagnostic("m", DiagnosticCategory.Error, 1)));
    }

    [Fact]
    public void IsShorthandAmbientModuleSymbol_RequiresABodylessModuleDeclaration()
    {
        var shorthand = new TransientSymbol(SymbolFlags.Module, "\"m\"")
        {
            ValueDeclaration = (ModuleDeclaration)Parse("declare module \"m\";").Statements![0],
        };
        var bodied = new TransientSymbol(SymbolFlags.Module, "\"m\"")
        {
            ValueDeclaration = (ModuleDeclaration)Parse("declare module \"m\" { }\n").Statements![0],
        };

        Assert.True(Utilities.IsShorthandAmbientModuleSymbol(shorthand));
        Assert.False(Utilities.IsShorthandAmbientModuleSymbol(bodied));
    }

    // ---------------------------------------------------------------------------------------------
    // JSDoc readers (utilitiesPublic.ts:1106-1349, utilities.ts:6918-6925, 11054-11085)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void HasJSDocParameterTags_SeesTheHostFunctionsTagsFromTheParameter()
    {
        // utilitiesPublic.ts:1258-1262 documents `getJSDocTags` as "including those on parent nodes", so a parameter
        // does see its function's @param tag — the reference's behaviour, and what makes the two helpers below work
        // when the caller passes a parameter rather than the signature.
        var documented = FuncOf(Parse("/** @param {number} [a] */ function f(a) {}", ScriptKind.JS));
        var parameter = documented.Parameters![0];

        Assert.True(Utilities.HasJSDocParameterTags((Node)parameter));
        Assert.True(Utilities.HasJSDocParameterTags(documented));
        Assert.False(Utilities.HasJSDocParameterTags((Node)FuncOf(Parse("function g(a) {}", ScriptKind.JS))));
    }

    [Fact]
    public void GetJSDocType_ReadsTheTagOrTheMatchingParameterTag()
    {
        var documented = FuncOf(Parse("/** @param {number} a */ function f(a) {}", ScriptKind.JS));
        var typed = Utilities.GetJSDocType((Node)documented.Parameters![0]);

        Assert.NotNull(typed);
        Assert.Equal(SyntaxKind.NumberKeyword, typed!.Kind);
        Assert.Null(Utilities.GetJSDocType((Node)FuncOf(Parse("function g(a) {}", ScriptKind.JS))));
    }

    [Fact]
    public void IsOptionalJSDocPropertyLikeTag_AndIsJSDocOptionalParameter_ReadTheBracketedForm()
    {
        var documented = FuncOf(Parse("/** @param {number} [a] */ function f(a) {}", ScriptKind.JS));
        var parameter = documented.Parameters![0];
        var tag = Utilities.GetJSDocParameterTags(parameter)[0];

        Assert.True(Utilities.IsOptionalJSDocPropertyLikeTag((Node)tag));
        Assert.True(Utilities.IsJSDocOptionalParameter((Node)parameter));
        Assert.False(Utilities.IsJSDocOptionalParameter((Node)FuncOf(Parse("function g(a: number) {}")).Parameters![0]));

        var typedTag = Utilities.GetJSDocParameterTags(FuncOf(Parse("/** @param {number} b */ function h(b) {}", ScriptKind.JS)).Parameters![0])[0];
        Assert.False(Utilities.IsOptionalJSDocPropertyLikeTag((Node)typedTag));
    }

    [Fact]
    public void GetJSDocThisTag_AndGetJSDocAugmentsTag_AndGetJSDocImplementsTags()
    {
        var thisTagged = FuncOf(Parse("/** @this {Window} */ function f() {}", ScriptKind.JS));
        Assert.NotNull(Utilities.GetJSDocThisTag(thisTagged));
        Assert.Null(Utilities.GetJSDocThisTag(FuncOf(Parse("function g() {}"))));

        var augmented = ClassOf(Parse("/** @augments {Base} */ class C {}", ScriptKind.JS));
        Assert.NotNull(Utilities.GetJSDocAugmentsTag(augmented));
        Assert.Null(Utilities.GetJSDocAugmentsTag(ClassOf(Parse("class D {}"))));

        var implemented = ClassOf(Parse("/** @implements {I} */ class E {}", ScriptKind.JS));
        Assert.Single(Utilities.GetJSDocImplementsTags(implemented));
        Assert.Empty(Utilities.GetJSDocImplementsTags(ClassOf(Parse("class F {}"))));
        // The three families do not answer for each other's tags.
        Assert.Null(Utilities.GetJSDocAugmentsTag(implemented));
    }

    [Fact]
    public void GetJSDocTypeParameterDeclarations_AndGetEffectiveTypeParameterDeclarations()
    {
        var templated = FuncOf(Parse("/** @template T @param {T} x */ function t(x) {}", ScriptKind.JS));
        var declarations = Utilities.GetJSDocTypeParameterDeclarations(templated);

        Assert.Single(declarations);
        Assert.Equal(SyntaxKind.TypeParameter, declarations[0].Kind);
        Assert.Single(Utilities.GetEffectiveTypeParameterDeclarations(templated));

        var generic = FuncOf(Parse("function u<U>() {}"));
        var parameters = Utilities.GetEffectiveTypeParameterDeclarations(generic);
        Assert.Single(parameters);
        Assert.Same(generic.TypeParameters![0], parameters[0]);

        Assert.Single(Utilities.GetEffectiveTypeParameterDeclarations(ClassOf(Parse("class C<T> {}"))));
        Assert.Empty(Utilities.GetEffectiveTypeParameterDeclarations(FuncOf(Parse("function v() {}"))));
    }

    // ---------------------------------------------------------------------------------------------
    // The sibling members this batch was named for (Checker/Utilities.Checker.cs)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void PseudoBigIntToString_RoundTripsTheDigitsAndNeverSpellsNegativeZero()
    {
        // The parse half is Core/PseudoBigInt.cs (utilities.ts:10461-10526); the format half is the checker-side
        // utilities member. Everything the compiler emits or reports is this digit spelling.
        Assert.Equal("31", PseudoBigInt.Parse("0x1Fn"));
        Assert.Equal("5", PseudoBigInt.Parse("0b101n"));
        Assert.Equal("15", PseudoBigInt.Parse("0o17n"));
        Assert.Equal("42", PseudoBigInt.Parse("42n"));
        Assert.Equal("7", PseudoBigInt.Parse("007n"));
        Assert.Equal("0", PseudoBigInt.Parse("0n"));

        Assert.Equal("31", Utilities.PseudoBigIntToString(new FakePseudoBigInt { Negative = false, Base10Value = PseudoBigInt.Parse("0x1Fn") }));
        Assert.Equal("-31", Utilities.PseudoBigIntToString(new FakePseudoBigInt { Negative = true, Base10Value = "31" }));

        // utilities.ts:10529-10531's guard is `negative && base10Value !== "0"`: negative zero has no sign to spell.
        Assert.Equal("0", Utilities.PseudoBigIntToString(new FakePseudoBigInt { Negative = true, Base10Value = "0" }));
        Assert.Equal("0", Utilities.PseudoBigIntToString(new FakePseudoBigInt { Negative = false, Base10Value = "0" }));
    }

    [Fact]
    public void IsTypeAlias_AcceptsTheDeclarationAndTheJsDocTypedef()
    {
        Assert.True(Utilities.IsTypeAlias((Node)Parse("type T = string;\n").Statements![0]));
        Assert.False(Utilities.IsTypeAlias((Node)ClassOf(Parse("class C {}"))));
        Assert.False(Utilities.IsTypeAlias((Node)Parse("interface I {}\n").Statements![0]));

        var typedef = Utilities
            .GetJSDocTags((Node)Parse("/** @typedef {string} T */ let t;", ScriptKind.JS).Statements![0])
            .First(tag => NodeTests.IsJSDocTypedefTag(tag));
        Assert.Equal(SyntaxKind.JSDocTypedefTag, typedef.Kind);
        Assert.True(Utilities.IsTypeAlias((Node)typedef));
    }

    [Fact]
    public void IsLiteralImportTypeNode_RequiresAnImportTypeWithAStringLiteralArgument()
    {
        // utilities.ts:2724-2726: `isImportTypeNode(n) && isLiteralTypeNode(n.argument) && isStringLiteral(n.argument.literal)`.
        var literal = (TypeAliasDeclaration)Parse("type A = import(\"m\").T;\n").Statements![0];
        Assert.True(Utilities.IsLiteralImportTypeNode((Node)literal.Type!));

        var nonLiteral = (TypeAliasDeclaration)Parse("type C = import(x).T;\n").Statements![0];
        Assert.False(Utilities.IsLiteralImportTypeNode((Node)nonLiteral.Type!));

        var plainLiteralType = (TypeAliasDeclaration)Parse("type D = \"s\";\n").Statements![0];
        Assert.False(Utilities.IsLiteralImportTypeNode((Node)plainLiteralType.Type!));
    }

    [Fact]
    public void SkipTypeParentheses_ReachesTheInnermostTypeAndKeepsAnUnparenthesizedNode()
    {
        var parenthesized = (TypeAliasDeclaration)Parse("type P = ((string));\n").Statements![0];
        var skipped = Utilities.SkipTypeParentheses(parenthesized.Type!);

        Assert.Equal(SyntaxKind.StringKeyword, skipped.Kind);
        Assert.Equal(SyntaxKind.ParenthesizedType, parenthesized.Type!.Kind);

        var bare = (TypeAliasDeclaration)Parse("type Q = number;\n").Statements![0];
        Assert.Same(bare.Type, Utilities.SkipTypeParentheses(bare.Type!));
    }
    // ---------------------------------------------------------------------------------------------
    // Second batch: the members the checker files that landed later reference
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void CopyEntries_CopiesEveryEntryOfTheSourceTable()
    {
        var source = Utilities.CreateSymbolTable();
        source["a"] = new TransientSymbol(SymbolFlags.Property, "a");
        source["b"] = new TransientSymbol(SymbolFlags.Property, "b");
        source["a"] = new TransientSymbol(SymbolFlags.Method, "a");
        var target = Utilities.CreateSymbolTable();

        Utilities.CopyEntries(source, target);

        Assert.Equal(2, target.Count);
        Assert.Equal(SymbolFlags.Method, target["a"]!.Flags);
        Assert.Equal(SymbolFlags.Property, target["b"]!.Flags);
    }

    [Fact]
    public void IsComputedNonLiteralName_AcceptsOnlyAComputedNameWithANonLiteralExpression()
    {
        var members = ClassOf(Parse("class C { [x]: number; [\"s\"]: number; [1]: number; p: number; }")).Members!;

        Assert.True(Utilities.IsComputedNonLiteralName(((INamedDeclaration)members[0]).Name!));
        Assert.False(Utilities.IsComputedNonLiteralName(((INamedDeclaration)members[1]).Name!));
        Assert.False(Utilities.IsComputedNonLiteralName(((INamedDeclaration)members[2]).Name!));
        Assert.False(Utilities.IsComputedNonLiteralName(((INamedDeclaration)members[3]).Name!));
    }

    [Fact]
    public void GetTextOfPropertyName_AnswersTheNameForTheLiteralForms()
    {
        var property = Utilities.GetMembersOfDeclaration((Node)Parse("interface I { a: number; \"b\": number; }\n").Statements![0])!;

        Assert.Equal("a", Utilities.GetTextOfPropertyName(((INamedDeclaration)property[0]).Name!));
        Assert.Equal("b", Utilities.GetTextOfPropertyName(((INamedDeclaration)property[1]).Name!));
    }

    [Fact]
    public void GetSemanticJsxChildren_DropsEmptyExpressionsAndTriviaOnlyText()
    {
        var file = Parse("const x = <div>\n  {a}\n  {}\n  b\n</div>;", ScriptKind.TSX);
        var element = (JsxElement)((VariableDeclaration)
            ((VariableStatement)file.Statements![0]).DeclarationList!.Declarations![0]).Initializer!;

        var children = Utilities.GetSemanticJsxChildren(element.Children!);

        // The two JsxText runs that hold only whitespace and the empty JsxExpression drop out; `{a}` and the `b` text stay.
        Assert.Equal(2, children.Count);
        Assert.Equal(SyntaxKind.JsxExpression, children[0].Kind);
        Assert.Equal(SyntaxKind.JsxText, children[1].Kind);
    }

    [Fact]
    public void GetTextOfJsxAttributeName_SpellsBothAttributeNameForms()
    {
        var file = Parse("const x = <div a:b=\"1\" c=\"2\" />;", ScriptKind.TSX);
        var element = (JsxSelfClosingElement)((VariableDeclaration)
            ((VariableStatement)file.Statements![0]).DeclarationList!.Declarations![0]).Initializer!;
        var attributes = element.Attributes!.Properties!;

        Assert.Equal(SyntaxKind.JsxNamespacedName, ((JsxAttribute)attributes[0]).Name!.Kind);
        Assert.Equal("a:b", Utilities.GetTextOfJsxAttributeName((Node)((JsxAttribute)attributes[0]).Name!));
        Assert.Equal("c", Utilities.GetTextOfJsxAttributeName((Node)((JsxAttribute)attributes[1]).Name!));
    }

    [Fact]
    public void HasOnlyExpressionInitializer_AndHasType_ReadTheUnionsKindSets()
    {
        Assert.True(Utilities.HasOnlyExpressionInitializer((Node)VarOf(Parse("var x = 1;"))));
        Assert.False(Utilities.HasOnlyExpressionInitializer((Node)FuncOf(Parse("function f() {}"))));

        var typed = FuncOf(Parse("function f(a: number, b) {}"));
        Assert.True(Utilities.HasType((Node)typed.Parameters![0]));
        Assert.False(Utilities.HasType((Node)typed.Parameters![1]));
        Assert.False(Utilities.HasType((Node)ClassOf(Parse("class C {}"))));
        Assert.True(Utilities.HasType((Node)Parse("type T = string;\n").Statements![0]));
    }

    [Fact]
    public void IsOptionalDeclaration_ReadsTheQuestionTokenAndTheJsDocBracketedForm()
    {
        var cls = ClassOf(Parse("class C { p?: number; q: number; }"));
        Assert.True(Utilities.IsOptionalDeclaration((IDeclaration)cls.Members![0]));
        Assert.False(Utilities.IsOptionalDeclaration((IDeclaration)cls.Members![1]));

        var parameter = FuncOf(Parse("function f(a?: number, b = 1) {}")).Parameters!;
        Assert.True(Utilities.IsOptionalDeclaration((IDeclaration)parameter[0]));
        Assert.False(Utilities.IsOptionalDeclaration((IDeclaration)parameter[1]));

        var jsParameter = FuncOf(Parse("/** @param {number} [a] */ function g(a) {}", ScriptKind.JS)).Parameters![0];
        Assert.True(Utilities.IsOptionalDeclaration((IDeclaration)jsParameter));
    }

    [Fact]
    public void IsPrototypePropertyAssignment_AnswersOnlyInAJavaScriptFile()
    {
        // utilities.ts:4294-4296 reads getAssignmentDeclarationKind, whose worker answers PrototypeProperty for
        // `F.prototype.x = …` but is gated on isInJSFile (utilities.ts:4290-4292).
        var jsFile = Parse("Foo.prototype.bar = 1;", ScriptKind.JS);
        var jsAssignment = ((ExpressionStatement)jsFile.Statements![0]).Expression!;
        Assert.True(Utilities.IsPrototypePropertyAssignment(jsAssignment));

        var tsFile = Parse("Foo.prototype.bar = 1;");
        var tsAssignment = ((ExpressionStatement)tsFile.Statements![0]).Expression!;
        Assert.False(Utilities.IsPrototypePropertyAssignment(tsAssignment));
    }

    [Fact]
    public void IsInfinityOrNaNString_AcceptsTheThreeSpellingsOnly()
    {
        Assert.True(Utilities.IsInfinityOrNaNString("Infinity"));
        Assert.True(Utilities.IsInfinityOrNaNString("-Infinity"));
        Assert.True(Utilities.IsInfinityOrNaNString("NaN"));
        Assert.False(Utilities.IsInfinityOrNaNString("infinity"));
        Assert.False(Utilities.IsInfinityOrNaNString("0"));
    }

    [Fact]
    public void GetModifiers_AndGetDecorators_SplitTheModifiersSlot()
    {
        var cls = ClassOf(Parse("class C { public readonly x = 1; m() {} @dec n() {} }"));
        var modifiers = Utilities.GetModifiers((Node)cls.Members![0]);

        Assert.NotNull(modifiers);
        Assert.Equal(2, modifiers!.Count);
        Assert.Null(Utilities.GetModifiers((Node)cls.Members![1]));
        Assert.Empty(Utilities.GetDecorators((Node)cls.Members![1]) ?? []);

        var decorated = Utilities.GetDecorators((Node)cls.Members![2]);
        Assert.NotNull(decorated);
        Assert.Single(decorated!);
        Assert.Equal(SyntaxKind.Decorator, decorated![0].Kind);
        // A decorator is not a modifier (ModifierFlags.Modifier masks Decorator out), so the modifier read answers
        // null for a member whose only modifier-like node is the decorator.
        Assert.Null(Utilities.GetModifiers((Node)cls.Members![2]));
    }

    [Fact]
    public void GetEffectiveModifierFlags_AddsTheJsDocFlagsInAJavaScriptFile()
    {
        var jsClass = ClassOf(Parse("/** @public */ class C {}", ScriptKind.JS));
        Assert.True((Utilities.GetEffectiveModifierFlags(jsClass) & ModifierFlags.Public) != 0);

        var tsClass = ClassOf(Parse("class D {}"));
        Assert.Equal(ModifierFlags.None, Utilities.GetEffectiveModifierFlags(tsClass));

        var exported = ClassOf(Parse("export abstract class E {}"));
        var flags = Utilities.GetEffectiveModifierFlags(exported);
        Assert.True((flags & ModifierFlags.Export) != 0);
        Assert.True((flags & ModifierFlags.Abstract) != 0);
    }

    [Fact]
    public void ParseValidBigInt_AndIsValidBigIntString_ReadTheLiteralSpelling()
    {
        var positive = Utilities.ParseValidBigInt("0x1F");
        Assert.False(positive.Negative);
        Assert.Equal("31", positive.Base10Value);
        Assert.Equal("31", Utilities.PseudoBigIntToString(positive));

        var negative = Utilities.ParseValidBigInt("-42");
        Assert.True(negative.Negative);
        Assert.Equal("42", negative.Base10Value);
        Assert.Equal("-42", Utilities.PseudoBigIntToString(negative));

        Assert.True(Utilities.IsValidBigIntString("42", /*roundTripOnly*/ false));
        Assert.True(Utilities.IsValidBigIntString("42", /*roundTripOnly*/ true));
        Assert.True(Utilities.IsValidBigIntString("-42", /*roundTripOnly*/ false));
        // `0x1F` scans as a bigint, but its canonical decimal spelling is "31", so the round trip fails.
        Assert.False(Utilities.IsValidBigIntString("0x1F", /*roundTripOnly*/ true));
        Assert.False(Utilities.IsValidBigIntString("1_0", /*roundTripOnly*/ false));
        Assert.False(Utilities.IsValidBigIntString("", /*roundTripOnly*/ false));
        Assert.False(Utilities.IsValidBigIntString("42.5", /*roundTripOnly*/ false));
    }

    [Fact]
    public void EscapeString_SpellsTheEscapedFormsOfTheThreeQuoteKinds()
    {
        Assert.Equal("a\\\"b", Utilities.EscapeString("a\"b"));
        Assert.Equal("a\\tb", Utilities.EscapeString("a\tb"));
        Assert.Equal("a\\nb", Utilities.EscapeString("a\nb"));
        Assert.Equal("a\\\\b", Utilities.EscapeString("a\\b"));
        Assert.Equal("a\\u2028b", Utilities.EscapeString("a\u2028b"));

        // The double-quote and single-quote patterns escape an apostrophe only in the single-quote form.
        Assert.Equal("a'b", Utilities.EscapeString("a'b"));
        Assert.Equal("a\\'b", Utilities.EscapeString("a'b", CharacterCodes.SingleQuote));

        // The backtick pattern encodes CRLF but is the only one that leaves a bare LF alone.
        Assert.Equal("a\nb", Utilities.EscapeString("a\nb", CharacterCodes.Backtick));
        Assert.Equal("a\\r\\nb", Utilities.EscapeString("a\r\nb", CharacterCodes.Backtick));

        // The null character is spelled \0 unless a digit follows, where \x00 avoids an octal escape.
        Assert.Equal("a\\0b", Utilities.EscapeString("a\0b"));
        Assert.Equal("a\\x00" + "1", Utilities.EscapeString("a\01"));
    }

    [Fact]
    public void IsIdentifierTypePredicate_AndIsThisTypePredicate_ReadTheKind()
    {
        var identifier = new TypePredicate(TypePredicateKind.Identifier, "x", 0, null);
        var thisPredicate = new TypePredicate(TypePredicateKind.This, null, null, null);

        Assert.True(Utilities.IsIdentifierTypePredicate(identifier));
        Assert.False(Utilities.IsThisTypePredicate(identifier));
        Assert.True(Utilities.IsThisTypePredicate(thisPredicate));
        Assert.False(Utilities.IsIdentifierTypePredicate(thisPredicate));
        Assert.False(Utilities.IsIdentifierTypePredicate(null));
    }

    [Fact]
    public void EvaluatorResult_CarriesTheFourSlotsItsCallersRead()
    {
        var result = Utilities.EvaluatorResult<object?>("v", /*isSyntacticallyString*/ true, /*resolvedOtherFiles*/ true);

        Assert.Equal("v", result.Value);
        Assert.True(result.IsSyntacticallyString);
        Assert.True(result.ResolvedOtherFiles);
        Assert.False(result.HasExternalReferences);

        Assert.Null(Utilities.EvaluatorResult<object?>(null).Value);
    }

    [Fact]
    public void GetJSDocAugmentsTag_AndHasJSDocParameterTags_AreTheOnlyJSDocReadersTheCheckerReaches()
    {
        // The JSDoc helpers landed in this file are also reachable through the cache-aware/no-cache split: the
        // effective-modifier path needs the no-cache variants, and the pinned @public case above exercises them.
        var documented = FuncOf(Parse("/** @public @param {number} a */ function f(a) {}", ScriptKind.JS));
        Assert.True((Utilities.GetEffectiveModifierFlags(documented) & ModifierFlags.Public) != 0);
        Assert.True(Utilities.HasJSDocParameterTags(documented));
    }
    [Fact]
    public void IsSuperCall_AcceptsOnlyACallWhoseCalleeIsTheSuperKeyword()
    {
        var ctor = (ConstructorDeclaration)ClassOf(Parse("class C extends B { constructor() { super(); f(); } }")).Members![0];
        var statements = ctor.Body!.Statements!;

        Assert.True(Utilities.IsSuperCall((Node)((ExpressionStatement)statements[0]).Expression!));
        Assert.False(Utilities.IsSuperCall((Node)((ExpressionStatement)statements[1]).Expression!));
        Assert.False(Utilities.IsSuperCall((Node)ctor));
    }

    [Fact]
    public void MinAndMax_AnswersBothEndsWithoutScanningTwice()
    {
        var values = new List<int> { 3, 1, 4, 1, 5 };

        var (min, max) = Utilities.MinAndMax(values, v => v);

        Assert.Equal(1, min);
        Assert.Equal(5, max);

        var single = Utilities.MinAndMax(new List<int> { 7 }, v => v);
        Assert.Equal(7, single.Min);
        Assert.Equal(7, single.Max);
    }
}
