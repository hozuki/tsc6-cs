namespace TypeScript.Compiler.Tests;

// ported from: src/compiler/transformers/utilities.ts:107-886, destructuring.ts:99-626, taggedTemplate.ts:29-132
/// <summary>
/// Acceptance for the shared transformer utilities (M7-B's first batch): the predicates, the two identifier-name maps,
/// the private/lexical environments, the class-member and decorator queries, the module-specifier rewrite, and the two
/// destructuring flatteners.
/// </summary>
/// <remarks>
/// These tests are possible before the emitter lands because every member here is a pure function of the parsed tree
/// plus a <c>TransformationContext</c>; the two entries that need a context run against
/// <see cref="TestTransformationUtilitiesContext"/>, and the answers the reference computes as arrays/objects are
/// asserted through the landed printer (<c>PrinterFactory</c>) or by identity, which is what the reference's own callers
/// read. What is NOT observable here is named in the batch report: <c>processTaggedTemplateExpression</c>,
/// <c>collectExternalModuleInfo</c>'s external-helpers arm, and <c>chainBundle</c> (they need the emit-helper factory,
/// the emit resolver or the emitter's file paths).
/// </remarks>
public class TransformationUtilitiesTests
{
    private static SourceFile Parse(string text)
        => Parser.CreateSourceFile("test.ts", text, ScriptTarget.ESNext, /*setParentNodes*/ true, ScriptKind.TS);

    private static Expression InitializerOf(string expression)
        => ((VariableStatement)Parse($"var _ = {expression};").Statements![0]).DeclarationList!.Declarations![0].Initializer!;

    private static string Print(Node node, SourceFile file)
        => PrinterFactory.CreatePrinterWithRemoveCommentsOmitTrailingSemicolon().PrintNode(EmitHint.Unspecified, node, file);

    // ---------------------------------------------------------------------------------------------
    // transformers/utilities.ts:471-534 — the expression and operator predicates
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("\"s\"", true, true)]
    [InlineData("'s'", true, true)]
    [InlineData("1", true, true)]
    [InlineData("1n", false, false)]
    [InlineData("true", true, true)]
    [InlineData("null", true, true)]
    [InlineData("x", true, false)]
    [InlineData("`t`", true, true)]
    [InlineData("1 + 2", false, false)]
    [InlineData("f()", false, false)]
    public void SimpleExpressionPredicates_ClassifyLiteralsKeywordsAndIdentifiers(string source, bool copiable, bool inlineable)
    {
        var expression = InitializerOf(source);

        Assert.Equal(copiable, TransformationUtilities.IsSimpleCopiableExpression(expression));
        Assert.Equal(inlineable, TransformationUtilities.IsSimpleInlineableExpression(expression));
    }

    [Fact]
    public void CompoundAssignments_AreInTheReferenceRangeAndMapToTheirUnderlyingOperator()
    {
        // PORT-NOTE: a local table rather than a [Theory]: the test class is public and `SyntaxKind` is internal, so an
        // internal enum cannot appear in a test method's signature (CS0051).
        (SyntaxKind Compound, SyntaxKind Underlying)[] pairs =
        [
            (SyntaxKind.PlusEqualsToken, SyntaxKind.PlusToken),
            (SyntaxKind.MinusEqualsToken, SyntaxKind.MinusToken),
            (SyntaxKind.AsteriskEqualsToken, SyntaxKind.AsteriskToken),
            (SyntaxKind.AsteriskAsteriskEqualsToken, SyntaxKind.AsteriskAsteriskToken),
            (SyntaxKind.SlashEqualsToken, SyntaxKind.SlashToken),
            (SyntaxKind.PercentEqualsToken, SyntaxKind.PercentToken),
            (SyntaxKind.LessThanLessThanEqualsToken, SyntaxKind.LessThanLessThanToken),
            (SyntaxKind.GreaterThanGreaterThanEqualsToken, SyntaxKind.GreaterThanGreaterThanToken),
            (SyntaxKind.GreaterThanGreaterThanGreaterThanEqualsToken, SyntaxKind.GreaterThanGreaterThanGreaterThanToken),
            (SyntaxKind.AmpersandEqualsToken, SyntaxKind.AmpersandToken),
            (SyntaxKind.BarEqualsToken, SyntaxKind.BarToken),
            (SyntaxKind.CaretEqualsToken, SyntaxKind.CaretToken),
            (SyntaxKind.BarBarEqualsToken, SyntaxKind.BarBarToken),
            (SyntaxKind.AmpersandAmpersandEqualsToken, SyntaxKind.AmpersandAmpersandToken),
            (SyntaxKind.QuestionQuestionEqualsToken, SyntaxKind.QuestionQuestionToken),
        ];

        foreach (var (compound, underlying) in pairs)
        {
            Assert.True(TransformationUtilities.IsCompoundAssignment(compound));
            Assert.Equal(underlying, TransformationUtilities.GetNonAssignmentOperatorForCompoundAssignment(compound));
        }

        foreach (var kind in new[] { SyntaxKind.EqualsToken, SyntaxKind.PlusToken, SyntaxKind.BarBarToken, SyntaxKind.LessThanToken })
        {
            Assert.False(TransformationUtilities.IsCompoundAssignment(kind));
        }
    }

    // ---------------------------------------------------------------------------------------------
    // transformers/utilities.ts:536-578 — the super() search
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ASuperCall_IsFoundThroughTryStatementsButNotThroughAnExpression()
    {
        var file = Parse("class C extends B { constructor() { if (x) {} try { super(1); } catch { } } }");
        var body = ((ConstructorDeclaration)((ClassDeclaration)file.Statements![0]).Members![0]).Body!;

        Assert.Equal([1, 0], TransformationUtilities.FindSuperStatementIndexPath(body.Statements!, 0));
        Assert.Null(TransformationUtilities.GetSuperCallFromStatement((Node)body.Statements![0]));
        Assert.Null(TransformationUtilities.GetSuperCallFromStatement((Node)body.Statements![1]));

        var tryBlock = ((TryStatement)body.Statements![1]).TryBlock!;
        var call = TransformationUtilities.GetSuperCallFromStatement((Node)tryBlock.Statements![0]);
        Assert.NotNull(call);
        Assert.Equal(SyntaxKind.SuperKeyword, call!.Expression!.Kind);
    }

    [Fact]
    public void NoSuperCall_AnswersAnEmptyPath()
    {
        var file = Parse("class C { m() { try { f(); } catch { } } }");
        var body = ((MethodDeclaration)((ClassDeclaration)file.Statements![0]).Members![0]).Body!;

        Assert.Empty(TransformationUtilities.FindSuperStatementIndexPath(body.Statements!, 0));
    }

    // ---------------------------------------------------------------------------------------------
    // transformers/utilities.ts:581-649 — the class-member queries
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ClassMemberQueries_SplitInstanceStaticAndPrivateMembers()
    {
        var file = Parse("class C { a = 1; b!: number; static s = 2; static { } #p = 3; #m() { } static #g() { } get x() { return 1; } }");
        var declaration = (Node)file.Statements![0];
        var members = ((ClassDeclaration)file.Statements![0]).Members!;

        Assert.Equal(
            ["a", "#p"],
            TransformationUtilities.GetProperties(declaration, /*requireInitializer*/ true, /*isStatic*/ false).Select(property => Utilities.IdText(property.Name!)));
        Assert.Equal(
            ["a", "b", "#p"],
            TransformationUtilities.GetProperties(declaration, /*requireInitializer*/ false, /*isStatic*/ false).Select(property => Utilities.IdText(property.Name!)));
        Assert.Equal(
            ["s"],
            TransformationUtilities.GetProperties(declaration, /*requireInitializer*/ true, /*isStatic*/ true).Select(property => Utilities.IdText(property.Name!)));

        var statics = TransformationUtilities.GetStaticPropertiesAndClassStaticBlock(declaration);
        Assert.Equal(2, statics.Count);
        Assert.Equal(SyntaxKind.PropertyDeclaration, statics[0].Kind);
        Assert.Equal(SyntaxKind.ClassStaticBlockDeclaration, statics[1].Kind);

        var byName = members.ToDictionary(m => m.Name is null ? m.Kind.ToString() : Utilities.IdText(m.Name!));
        Assert.True(TransformationUtilities.IsInitializedProperty(byName["a"]));
        Assert.False(TransformationUtilities.IsInitializedProperty(byName["b"]));

        Assert.True(TransformationUtilities.IsNonStaticMethodOrAccessorWithPrivateName(byName["#m"]));
        Assert.False(TransformationUtilities.IsNonStaticMethodOrAccessorWithPrivateName(byName["#p"]));
        Assert.False(TransformationUtilities.IsNonStaticMethodOrAccessorWithPrivateName(byName["#g"]));
        Assert.False(TransformationUtilities.IsNonStaticMethodOrAccessorWithPrivateName(byName["a"]));
        Assert.False(TransformationUtilities.IsNonStaticMethodOrAccessorWithPrivateName(byName["x"]));
    }

    // ---------------------------------------------------------------------------------------------
    // transformers/utilities.ts:652-786 — the decorator queries
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void DecoratorsOfAClass_ComeWithTheConstructorParametersDecorators()
    {
        var file = Parse("@dec class C { constructor(@p0 x: number, y: number) { } }");
        var declaration = (ClassDeclaration)file.Statements![0];

        var all = TransformationUtilities.GetAllDecoratorsOfClass(declaration, /*useLegacyDecorators*/ true);

        Assert.NotNull(all);
        Assert.Single(all!.Decorators!);
        Assert.Equal(2, all.Parameters!.Count);
        Assert.Single(all.Parameters![0]!);
        Assert.Null(all.Parameters[1]);

        // The legacy flag decides whether the parameter decorators are collected at all.
        Assert.Null(TransformationUtilities.GetAllDecoratorsOfClass(declaration, /*useLegacyDecorators*/ false)!.Parameters);
    }

    [Fact]
    public void AnUndecoratedClass_HasNoDecoratorsObject()
    {
        var file = Parse("class C { }");

        Assert.Null(TransformationUtilities.GetAllDecoratorsOfClass((Node)file.Statements![0], /*useLegacyDecorators*/ true));
    }

    [Fact]
    public void DecoratorsOfAMember_AreReportedForTheFirstApiShapeOnly()
    {
        var file = Parse("@dec class C { @m method(@q z: number) { } @g get p() { return 1; } @s set p(v: number) { } }");
        var declaration = (ClassDeclaration)file.Statements![0];
        var members = declaration.Members!;
        var method = members[0];
        var getAccessor = members[1];
        var setAccessor = members[2];

        var methodDecorators = TransformationUtilities.GetAllDecoratorsOfClassElement(method, declaration, /*useLegacyDecorators*/ true);
        Assert.Single(methodDecorators!.Decorators!);
        Assert.Single(methodDecorators.Parameters![0]!);

        var getter = TransformationUtilities.GetAllDecoratorsOfClassElement(getAccessor, declaration, /*useLegacyDecorators*/ true);
        Assert.Single(getter!.Decorators!);
        Assert.Null(getter.Parameters);

        // The accessor pair is reported once, from the first accessor that carries decorators.
        Assert.Single(getter.SetDecorators!);
        Assert.Null(TransformationUtilities.GetAllDecoratorsOfClassElement(setAccessor, declaration, /*useLegacyDecorators*/ true));

        // Without the legacy flag an accessor is treated like any other method.
        var nonLegacy = TransformationUtilities.GetAllDecoratorsOfClassElement(getAccessor, declaration, /*useLegacyDecorators*/ false);
        Assert.Single(nonLegacy!.Decorators!);
        Assert.Null(nonLegacy.SetDecorators);
    }

    // ---------------------------------------------------------------------------------------------
    // transformers/utilities.ts:389-469 — the identifier-name maps
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void IdentifierNameMap_KeysByIdentifierTextAndByPrivateName()
    {
        var map = new TransformationUtilities.IdentifierNameMap<string>();
        var identifier = NodeFactory.Factory.CreateIdentifier("x", /*originalKeywordKind*/ null, /*hasExtendedUnicodeEscape*/ null);

        Assert.Equal(0, map.Count);
        Assert.False(map.Has(identifier));
        Assert.Null(map.Get(identifier));

        Assert.Same(map, map.Set(identifier, "one"));
        Assert.Equal(1, map.Count);
        Assert.Equal("one", map.Get(NodeFactory.Factory.CreateIdentifier("x", null, null)));
        Assert.Equal(["one"], map.Values());

        var privateIdentifier = (PrivateIdentifier)((MethodDeclaration)((ClassDeclaration)Parse("class C { #p() { } }").Statements![0]).Members![0]).Name!;
        Assert.False(map.Has(privateIdentifier));
        map.Set(privateIdentifier, "two");
        Assert.Equal("two", map.Get(privateIdentifier));
        Assert.Equal(2, map.Count);

        Assert.True(map.Delete(identifier));
        Assert.False(map.Delete(identifier));
        Assert.Equal(["two"], map.Values());

        map.Clear();
        Assert.Equal(0, map.Count);
    }

    [Fact]
    public void IdentifierNameMap_GivesAGeneratedIdentifierTheNameOfTheNodeItStandsFor()
    {
        var member = ((ClassDeclaration)Parse("class C { #p() { } }").Statements![0]).Members![0];
        var generated = NodeFactory.Factory.GetGeneratedPrivateNameForNode((Node)member, /*prefix*/ null, /*suffix*/ null);

        var map = new TransformationUtilities.IdentifierNameMap<string>();
        map.Set(generated, "generated");

        Assert.Equal("generated", map.Get(generated));
        Assert.False(map.Has((PrivateIdentifier)member.Name!));
    }

    [Fact]
    public void IdentifierNameMultiMap_AccumulatesAndRemovesPerKey()
    {
        var multiMap = new TransformationUtilities.IdentifierNameMultiMap<string>();
        var identifier = NodeFactory.Factory.CreateIdentifier("x", null, null);

        Assert.Equal(["a"], multiMap.Add(identifier, "a"));
        Assert.Equal(["a", "b"], multiMap.Add(identifier, "b"));

        multiMap.Remove(identifier, "a");
        Assert.Equal(["b"], multiMap.Get(identifier));

        multiMap.Remove(identifier, "b");
        Assert.False(multiMap.Has(identifier));
    }

    // ---------------------------------------------------------------------------------------------
    // transformers/utilities.ts:788-873 — the private environments and the parameter-list test
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void PrivateEnvironments_ResolveNamedAndGeneratedPrivateIdentifiers()
    {
        var member = ((ClassDeclaration)Parse("class C { #p() { } }").Statements![0]).Members![0];
        var name = (PrivateIdentifier)member.Name!;
        PrivateIdentifier generatedName = NodeFactory.Factory.GetGeneratedPrivateNameForNode((Node)member, /*prefix*/ null, /*suffix*/ null);
        var environment = TransformationUtilities.NewPrivateEnvironment<string, string>("data");

        Assert.Equal("data", environment.Data);
        Assert.Null(TransformationUtilities.GetPrivateIdentifier(environment, name));

        TransformationUtilities.SetPrivateIdentifier(environment, name, "named");
        Assert.Equal("named", TransformationUtilities.GetPrivateIdentifier(environment, name));

        TransformationUtilities.SetPrivateIdentifier(environment, generatedName, "generated");
        Assert.Equal("generated", TransformationUtilities.GetPrivateIdentifier(environment, generatedName));

        var lexical = new TestLexicalEnvironment { Data = "inner", PrivateEnv = environment, Previous = null };
        Assert.Equal("named", TransformationUtilities.AccessPrivateIdentifier(lexical, name));
        Assert.Null(TransformationUtilities.AccessPrivateIdentifier<string, string, string>(null, name));

        // An entry only reachable through an outer environment is found by walking up.
        var outer = TransformationUtilities.NewPrivateEnvironment<string, string>("outer");
        TransformationUtilities.SetPrivateIdentifier(outer, name, "outer");
        var inner = new TestLexicalEnvironment
        {
            Data = "inner",
            PrivateEnv = null,
            Previous = new TestLexicalEnvironment { Data = "outer", PrivateEnv = outer, Previous = null },
        };
        Assert.Equal("outer", TransformationUtilities.AccessPrivateIdentifier(inner, name));
    }

    [Theory]
    [InlineData("function f(a, b) { }", true)]
    [InlineData("function f(a = 1) { }", false)]
    [InlineData("function f({ a }) { }", false)]
    [InlineData("function f(a: number, b: string) { }", true)]
    public void IsSimpleParameterList_RejectsInitializersAndPatterns(string source, bool expected)
    {
        var parameters = ((FunctionDeclaration)Parse(source).Statements![0]).Parameters!;

        Assert.Equal(expected, TransformationUtilities.IsSimpleParameterList(parameters));
    }

    // ---------------------------------------------------------------------------------------------
    // transformers/utilities.ts:875-886 — rewriteModuleSpecifier
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void RewriteModuleSpecifier_RewritesTheExtensionOnlyWhenTheOptionIsOn()
    {
        var specifier = (Expression)((ImportDeclaration)Parse("import x from \"./a.ts\";").Statements![0]).ModuleSpecifier!;
        var on = new FakeCompilerOptions { RewriteRelativeImportExtensions = true };
        var off = new FakeCompilerOptions();

        var rewritten = TransformationUtilities.RewriteModuleSpecifier(specifier, on);

        Assert.NotNull(rewritten);
        Assert.NotSame(specifier, rewritten);
        Assert.Equal("./a.js", ((StringLiteral)rewritten!).Text);
        Assert.Same(specifier, rewritten!.Original);

        // The reference answers the input node itself when nothing is rewritten.
        Assert.Same(specifier, TransformationUtilities.RewriteModuleSpecifier(specifier, off));
    }

    // ---------------------------------------------------------------------------------------------
    // transformers/utilities.ts:148-177 — the import/export helper predicates
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("import * as ns from \"m\";", true, false)]
    [InlineData("import { a } from \"m\";", false, false)]
    [InlineData("import d from \"m\";", false, true)]
    [InlineData("import d, { a } from \"m\";", true, false)]
    [InlineData("import { default as a, b } from \"m\";", true, false)]
    [InlineData("import { default as a } from \"m\";", false, true)]
    [InlineData("import d, { default as e } from \"m\";", false, true)]
    [InlineData("import \"m\";", false, false)]
    public void ImportHelperPredicates_ReadTheImportShape(string source, bool needsImportStar, bool needsImportDefault)
    {
        var declaration = (ImportDeclaration)Parse(source).Statements![0];

        Assert.Equal(needsImportStar, TransformationUtilities.GetImportNeedsImportStarHelper(declaration));
        Assert.Equal(needsImportDefault, TransformationUtilities.GetImportNeedsImportDefaultHelper(declaration));
    }

    [Theory]
    [InlineData("export * as ns from \"m\";", true)]
    [InlineData("export * from \"m\";", false)]
    [InlineData("export { a } from \"m\";", false)]
    public void ExportHelperPredicate_ReadsTheNamespaceClause(string source, bool expected)
    {
        var declaration = (ExportDeclaration)Parse(source).Statements![0];

        Assert.Equal(expected, TransformationUtilities.GetExportNeedsImportStarHelper(declaration));
    }

    // ---------------------------------------------------------------------------------------------
    // destructuring.ts:99-626 — the two flatteners
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void FlatteningAnObjectBinding_BuildsOneDeclarationPerElement()
    {
        var file = Parse("let { a: b, c } = x;");
        var declaration = ((VariableStatement)file.Statements![0]).DeclarationList!.Declarations![0];

        var declarations = Destructuring.FlattenDestructuringBinding(
            declaration,
            node => node,
            new TestTransformationUtilitiesContext(),
            FlattenLevel.All,
            /*rval*/ null,
            /*hoistTempVariables*/ false,
            /*skipInitializer*/ false);

        Assert.Equal(2, declarations.Count);
        Assert.Equal(["b = x.a", "c = x.c"], declarations.Select(d => Print(d, file)));
    }

    [Fact]
    public void FlatteningAnArrayBinding_UsesAnElementAccessAndASliceCall()
    {
        var file = Parse("let [a, ...rest] = x;");
        var declaration = ((VariableStatement)file.Statements![0]).DeclarationList!.Declarations![0];

        var declarations = Destructuring.FlattenDestructuringBinding(
            declaration,
            node => node,
            new TestTransformationUtilitiesContext(),
            FlattenLevel.All,
            /*rval*/ null,
            /*hoistTempVariables*/ false,
            /*skipInitializer*/ false);

        Assert.Equal(2, declarations.Count);
        Assert.Equal(["a = x[0]", "rest = x.slice(1)"], declarations.Select(d => Print(d, file)));
    }

    [Fact]
    public void FlatteningAnEmptyPattern_KeepsTheTemporaryThatEvaluatesTheValue()
    {
        var file = Parse("let { } = x;");
        var declaration = ((VariableStatement)file.Statements![0]).DeclarationList!.Declarations![0];

        var declarations = Destructuring.FlattenDestructuringBinding(
            declaration,
            node => node,
            new TestTransformationUtilitiesContext(),
            FlattenLevel.All,
            /*rval*/ null,
            /*hoistTempVariables*/ false,
            /*skipInitializer*/ false);

        // The reference creates the temporary deliberately: a `var` keyword may already have been emitted.
        Assert.Single(declarations);
        Assert.Equal("_a = x", Print(declarations[0], file));
    }

    [Fact]
    public void HoistedTemporaries_AreRecordedOnTheContextAndNotInTheDeclarations()
    {
        var file = Parse("let { } = x;");
        var declaration = ((VariableStatement)file.Statements![0]).DeclarationList!.Declarations![0];
        var context = new TestTransformationUtilitiesContext();

        var declarations = Destructuring.FlattenDestructuringBinding(
            declaration,
            node => node,
            context,
            FlattenLevel.All,
            /*rval*/ null,
            /*hoistTempVariables*/ true,
            /*skipInitializer*/ false);

        Assert.Single(declarations);
        Assert.Single(context.Hoisted);
        Assert.Equal("_a = x", Print(declarations[0].Initializer!, file));
    }

    [Fact]
    public void FlatteningADestructuringAssignment_AnswersAnAssignmentExpression()
    {
        var file = Parse("({ a } = x);");
        var assignment = (BinaryExpression)((ParenthesizedExpression)((ExpressionStatement)file.Statements![0]).Expression!).Expression!;

        var expression = Destructuring.FlattenDestructuringAssignment(
            assignment,
            node => node,
            new TestTransformationUtilitiesContext(),
            FlattenLevel.All);

        Assert.Equal("a = x.a", Print(expression, file));
    }

    [Fact]
    public void FlatteningADestructuringAssignmentThatNeedsItsValue_AppendsTheCachedValue()
    {
        var file = Parse("({ a } = x);");
        var assignment = (BinaryExpression)((ParenthesizedExpression)((ExpressionStatement)file.Statements![0]).Expression!).Expression!;

        var expression = Destructuring.FlattenDestructuringAssignment(
            assignment,
            node => node,
            new TestTransformationUtilitiesContext(),
            FlattenLevel.All,
            /*needsValue*/ true);

        // `ensureIdentifier` caches the right-hand side under a temporary, and the value is appended as the result.
        Assert.Equal("a = x.a, x", Print(expression, file));
    }

    // ---------------------------------------------------------------------------------------------
    // The context face the two flatteners read (Transformer.cs's concrete context implements it)
    // ---------------------------------------------------------------------------------------------

    private sealed class TestLexicalEnvironment : TransformationUtilities.LexicalEnvironment<string, string, string>
    {
        public string Data { get; set; } = "";

        public TransformationUtilities.PrivateEnvironment<string, string>? PrivateEnv { get; set; }

        public TransformationUtilities.LexicalEnvironment<string, string, string>? Previous { get; set; }
    }

    private sealed class TestTransformationUtilitiesContext : ITransformationUtilitiesContext
    {
        /// <summary>The identifiers <c>hoistVariableDeclaration</c> was asked to record.</summary>
        public List<Identifier> Hoisted { get; } = [];

        public NodeFactory Factory => NodeFactory.Factory;

        public ICompilerOptions GetCompilerOptions() => new FakeCompilerOptions();

        public void HoistVariableDeclaration(Identifier? node)
        {
            if (node is not null)
            {
                Hoisted.Add(node);
            }
        }

        // The two members the flattener only reaches for a rest/iterator helper; the emit-helper factory belongs to the
        // emit layer, and nothing in these tests asks for a helper.
        public IEmitResolver GetEmitResolver() => throw new NotSupportedException();

        public IEmitHelperFactory GetEmitHelperFactory() => throw new NotSupportedException();

        public void StartLexicalEnvironment()
        {
        }

        public void SetLexicalEnvironmentFlags(LexicalEnvironmentFlags flags, bool value)
        {
        }

        public LexicalEnvironmentFlags GetLexicalEnvironmentFlags() => LexicalEnvironmentFlags.None;

        public void SuspendLexicalEnvironment()
        {
        }

        public void ResumeLexicalEnvironment()
        {
        }

        public List<IStatement>? EndLexicalEnvironment() => null;

        public void StartBlockScope()
        {
        }

        public List<IStatement>? EndBlockScope() => null;

        public void AddInitializationStatement(IStatement? node)
        {
        }
    }
}
