namespace TypeScript.Compiler.Tests;

// ported from: src/compiler/transformers/typeSerializer.ts:132-633
/// <summary>
/// Acceptance for the decorator-metadata type serializer (M7-B): the kind table <c>serializeTypeNode</c> applies, the
/// declaration and parameter walkers the <c>design:type</c>/<c>design:paramtypes</c>/<c>design:returntype</c> entries are
/// built from, and the type-reference arm the emit resolver answers.
/// </summary>
/// <remarks>
/// <para>
/// These tests are possible before the emit pipeline lands because every member of the serializer is a pure function of
/// a parsed (and, for the type-reference arm, checked) tree plus the options and the emit resolver: no printer state is
/// read and no file is emitted. What they assert is what the reference's own callers read — the expression the serializer
/// answers (printed with the landed printer), the identifiers it handed to <c>hoistVariableDeclaration</c>, and the node
/// identity of the clones it builds.
/// </para>
/// <para>
/// The fixture builds a real <see cref="Program"/> over the test's own text plus the smallest stub library
/// <c>initializeTypeChecker</c> needs (the shape <c>CheckerCreateTypeCheckerTests</c> records), so the type-reference arm
/// runs against the ported <c>EmitResolver</c> rather than a double. What is NOT observable here is named in the batch
/// report: nothing consumes the serializer until <c>transformTypeScript</c>'s <c>getTypeMetadata</c> (a sibling slice)
/// and the emit-helper factory's <c>__metadata</c> call land, so no decorator metadata is emitted end to end yet.
/// </para>
/// <para>
/// PORT-NOTE: the two tables that would be <c>[Theory]</c> rows over <c>ScriptTarget</c>/<c>SyntaxKind</c> are local
/// arrays instead — those enums are internal and a public test class cannot carry them in a test method's signature
/// (CS0051), which is the constraint <c>TransformationUtilitiesTests</c> records for the same reason.
/// </para>
/// </remarks>
public class TypeSerializerTests
{
    /// <summary>The smallest library that lets <c>initializeTypeChecker</c> resolve the globals it asks for.</summary>
    private static readonly string[] StubLib =
    [
        "interface Array<T> { }",
        "interface Object { }",
        "interface Function { }",
        "declare var undefined: undefined;",
    ];

    /// <summary>The transformation context the serializer is built over: the factory, the compiler options, the hoist
    /// member the temporaries go through, and the checker's own emit resolver.</summary>
    private sealed class TestTransformationContext : ITransformationUtilitiesContext
    {
        private readonly IEmitResolver _resolver;
        private readonly ICompilerOptions _options;

        public TestTransformationContext(IEmitResolver resolver, ICompilerOptions options)
        {
            _resolver = resolver;
            _options = options;
        }

        /// <summary>The identifiers <c>hoistVariableDeclaration</c> was asked to record.</summary>
        public List<Identifier> Hoisted { get; } = [];

        public NodeFactory Factory => NodeFactory.Factory;

        public ICompilerOptions GetCompilerOptions() => _options;

        public void HoistVariableDeclaration(Identifier? node)
        {
            if (node is not null)
            {
                Hoisted.Add(node);
            }
        }

        public IEmitResolver GetEmitResolver() => _resolver;

        // The emit-helper factory belongs to the emit layer, and the serializer never asks for a helper.
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

    private sealed record Fixture(
        SourceFile Root,
        TypeSerializer.RuntimeTypeSerializer Serializer,
        TestTransformationContext Transform,
        TypeSerializer.RuntimeTypeSerializerContext Scope);

    /// <summary>Builds the program the test's text is checked in and the serializer over its checker's emit resolver.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>verifyCompilerOptions</c>' emit-path check needs <c>getEmitHost</c> (an M7 blocker), so the options
    /// take <c>suppressOutputPathCheck</c> — the flag the reference's own build mode sets and the one
    /// <c>CheckerCreateTypeCheckerTests</c> takes. The resolver is fetched with <c>forceDts: false</c>, which is what
    /// <c>Program.getEmitResolver</c> does for a normal emit and which checks the file first, so every question the
    /// serializer asks the checker is asked of a checked file. The scope the calls run under is the one the reference's
    /// callers build: the file as the lexical scope and, when the text declares one, its first class as the name scope
    /// (the "only when there is no other block scope between the current location and the class" case).
    /// </remarks>
    private static Fixture CreateFixture(string text, ScriptTarget target = ScriptTarget.ESNext, bool strictNullChecks = true)
    {
        var options = new FakeCompilerOptions
        {
            SuppressOutputPathCheck = true,
            StrictNullChecks = strictNullChecks,
            Target = target,
        };
        var system = new FakeSystem();
        system.Files["/a.ts"] = text;
        var host = Program.CreateCompilerHostWorker(options, /*setParentNodes*/ true, system);
        system.Files[host.GetDefaultLibFileName(options)] = string.Join("\n", StubLib) + "\n";
        var program = Program.CreateProgram(["/a.ts"], options, host);
        var root = program.GetSourceFiles().Single(file => (file.FileName ?? "").EndsWith("a.ts", StringComparison.Ordinal));
        var resolver = program.GetTypeChecker().GetEmitResolver(root, /*cancellationToken*/ null, /*forceDts*/ false);
        var transform = new TestTransformationContext(resolver, options);
        var scope = new TypeSerializer.RuntimeTypeSerializerContext
        {
            CurrentLexicalScope = root,
            CurrentNameScope = root.Statements?.OfType<ClassDeclaration>().FirstOrDefault() is { } declaration
                ? (IClassLikeDeclarationBase)declaration
                : null,
        };
        return new Fixture(root, TypeSerializer.CreateRuntimeTypeSerializer(transform), transform, scope);
    }

    private static string Print(Node node, SourceFile file)
        => PrinterFactory.CreatePrinterWithRemoveCommentsOmitTrailingSemicolon().PrintNode(EmitHint.Unspecified, node, file);

    /// <summary>The type annotation of the first <c>let</c> declaration of the fixture's own text.</summary>
    private static ITypeNode DeclaredType(SourceFile file)
        => file.Statements!.OfType<VariableStatement>().First().DeclarationList!.Declarations![0].Type!;

    private static string SerializeTypeNodeOf(Fixture fixture, ITypeNode node)
        => Print(fixture.Serializer.SerializeTypeNode(fixture.Scope, node), fixture.Root);

    private static string SerializeTypeOf(Fixture fixture, Node node, IClassLikeDeclarationBase container)
        => Print(fixture.Serializer.SerializeTypeOfNode(fixture.Scope, node, container), fixture.Root);

    private static string SerializeParametersOf(Fixture fixture, Node node, IClassLikeDeclarationBase container)
        => Print(fixture.Serializer.SerializeParameterTypesOfNode(fixture.Scope, node, container), fixture.Root);

    private static string SerializeReturnTypeOf(Fixture fixture, Node node)
        => Print(fixture.Serializer.SerializeReturnTypeOfNode(fixture.Scope, node), fixture.Root);

    /// <summary>Serializes a type written inline, through a fixture of its own.</summary>
    private static string SerializeType(string typeText, ScriptTarget target = ScriptTarget.ESNext, bool strictNullChecks = true)
    {
        var fixture = CreateFixture($"let x: {typeText};", target, strictNullChecks);
        return SerializeTypeNodeOf(fixture, DeclaredType(fixture.Root));
    }

    /// <summary>The class a <c>class C { … }</c> fixture declares, and its members.</summary>
    private static (Fixture Fixture, ClassDeclaration Class, NodeArray<IClassElement> Members) ClassFixture(string members)
    {
        var fixture = CreateFixture($"class C {{ {members} }}");
        var declaration = (ClassDeclaration)fixture.Root.Statements![0];
        fixture.Scope.CurrentNameScope = declaration;
        return (fixture, declaration, declaration.Members!);
    }

    // ---------------------------------------------------------------------------------------------
    // typeSerializer.ts:271-365 — serializeTypeNode's kind table
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("void", "void 0")]
    [InlineData("undefined", "void 0")]
    [InlineData("never", "void 0")]
    [InlineData("() => void", "Function")]
    [InlineData("new () => void", "Function")]
    [InlineData("number[]", "Array")]
    [InlineData("[number]", "Array")]
    [InlineData("boolean", "Boolean")]
    [InlineData("string", "String")]
    [InlineData("`a`", "String")]
    [InlineData("object", "Object")]
    [InlineData("number", "Number")]
    [InlineData("any", "Object")]
    [InlineData("unknown", "Object")]
    [InlineData("{ a: number }", "Object")]
    [InlineData("keyof number", "Object")]
    [InlineData("readonly number[]", "Array")]
    [InlineData("bigint", "BigInt")]
    [InlineData("symbol", "Symbol")]
    public void TypeNodes_SerializeToTheGlobalConstructorTheirKindSelects(string typeText, string expected)
        => Assert.Equal(expected, SerializeType(typeText));

    [Fact]
    public void AThisType_SerializesToObject()
        => Assert.Equal("Object", SerializeType("this"));

    /// <summary>A target below the constructor's language version asks for the global at runtime instead.</summary>
    /// <remarks><c>getGlobalConstructor</c> tests <c>languageVersion &lt; minLanguageVersion</c>, so <c>BigInt</c>
    /// (ES2020) falls back on every earlier target while <c>Symbol</c> (ES2015) still answers the plain identifier on
    /// ES2015.</remarks>
    [Fact]
    public void BelowTheConstructorsLanguageVersion_TheGlobalIsCheckedAtRuntime()
    {
        (ScriptTarget Target, string TypeText, string Expected)[] cases =
        [
            (ScriptTarget.ES2015, "bigint", "typeof BigInt === \"function\" ? BigInt : Object"),
            (ScriptTarget.ES2015, "symbol", "Symbol"),
            (ScriptTarget.ES2020, "bigint", "BigInt"),
        ];

        foreach (var (target, typeText, expected) in cases)
        {
            Assert.Equal(expected, SerializeType(typeText, target));
        }
    }

    // ---------------------------------------------------------------------------------------------
    // typeSerializer.ts:367-400 — serializeLiteralOfLiteralTypeNode
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("\"s\"", "String")]
    [InlineData("'s'", "String")]
    [InlineData("1", "Number")]
    [InlineData("-1", "Number")]
    [InlineData("1n", "BigInt")]
    [InlineData("-1n", "BigInt")]
    [InlineData("true", "Boolean")]
    [InlineData("false", "Boolean")]
    [InlineData("null", "void 0")]
    public void LiteralTypes_SerializeToTheirWrapperConstructor(string typeText, string expected)
        => Assert.Equal(expected, SerializeType(typeText));

    // ---------------------------------------------------------------------------------------------
    // typeSerializer.ts:402-484 — serializeUnionOrIntersectionConstituents / equateSerializedTypeNodes
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("number | string", "Object")]
    [InlineData("1 | 2", "Number")]
    [InlineData("string | \"a\"", "String")]
    [InlineData("\"a\" | 1", "Object")]
    [InlineData("boolean | true", "Boolean")]
    [InlineData("never | number", "Number")]
    [InlineData("never | never", "void 0")]
    [InlineData("unknown | number", "Object")]
    [InlineData("unknown & number", "Number")]
    [InlineData("number & never", "void 0")]
    [InlineData("any & number", "Object")]
    [InlineData("unknown extends number ? string : string", "String")]
    [InlineData("unknown extends number ? string : boolean", "Object")]
    public void UnionAndIntersectionConstituents_FoldToTheCommonConstructorOrObject(string typeText, string expected)
        => Assert.Equal(expected, SerializeType(typeText));

    [Theory]
    [InlineData("null | number", false, "Number")]
    [InlineData("null | number", true, "Object")]
    [InlineData("undefined | number", false, "Number")]
    [InlineData("undefined | number", true, "Object")]
    [InlineData("null | undefined", false, "void 0")]
    [InlineData("null | undefined", true, "void 0")]
    public void NullAndUndefined_AreElidedOnlyWithoutStrictNullChecks(
        string typeText,
        bool strictNullChecks,
        string expected)
        => Assert.Equal(expected, SerializeType(typeText, ScriptTarget.ESNext, strictNullChecks));

    // ---------------------------------------------------------------------------------------------
    // typeSerializer.ts:490-545 — serializeTypeReferenceNode (the emit resolver's arm)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void AClassReference_SerializesToACloneOfItsEntityName()
    {
        var fixture = CreateFixture("class C { }\nlet x: C;");
        var declaredType = DeclaredType(fixture.Root);
        var typeName = ((TypeReferenceNode)declaredType).TypeName!;

        var serialized = SerializeTypeNodeOf(fixture, declaredType);

        // The reference clones the name and re-parents it onto a parse-tree node so the checker treats it as one.
        Assert.Equal("C", serialized);
        Assert.NotSame(typeName, ((Expression)fixture.Serializer.SerializeTypeNode(fixture.Scope, declaredType)));
        Assert.Empty(fixture.Transform.Hoisted);
    }

    [Theory]
    [InlineData("interface I { }", "I", "Object")]
    [InlineData("type N = number;", "N", "Number")]
    public void AnInterfaceOrAliasReference_SerializesToTheKindItsDeclaredTypeIs(
        string declaration,
        string name,
        string expected)
    {
        var fixture = CreateFixture($"{declaration}\nlet x: {name};");

        Assert.Equal(expected, SerializeTypeNodeOf(fixture, DeclaredType(fixture.Root)));
    }

    [Fact]
    public void AnUnresolvableReference_SerializesToTheCheckedEntityNameFallbackAndHoistsItsTemporary()
    {
        var fixture = CreateFixture("let x: NoSuchName;");

        Assert.Equal(
            "typeof (_a = typeof NoSuchName !== \"undefined\" && NoSuchName) === \"function\" ? _a : Object",
            SerializeTypeNodeOf(fixture, DeclaredType(fixture.Root)));
        Assert.Single(fixture.Transform.Hoisted);
    }

    [Fact]
    public void AReferenceToAConditionalTypesBranch_IsObjectWithoutTheFallback()
    {
        var fixture = CreateFixture("let x: unknown extends infer T ? DoesNotExist : number;");
        var conditional = (ConditionalTypeNode)DeclaredType(fixture.Root);

        // `findAncestor` sees the conditional parent: the unresolved name is `any`-like there, so no temp is built.
        Assert.Equal("Object", SerializeTypeNodeOf(fixture, conditional.TrueType!));
        Assert.Empty(fixture.Transform.Hoisted);
    }

    [Fact]
    public void AQualifiedReference_SerializesToAPropertyAccessOfTwoClones()
    {
        var fixture = CreateFixture("namespace N { export class C { } }\nlet x: N.C;");
        var declaredType = (TypeReferenceNode)DeclaredType(fixture.Root);

        var serialized = fixture.Serializer.SerializeTypeNode(fixture.Scope, declaredType);

        Assert.Equal("N.C", Print(serialized, fixture.Root));
        Assert.NotSame(declaredType.TypeName, serialized);
    }

    // ---------------------------------------------------------------------------------------------
    // typeSerializer.ts:179-236 — serializeTypeOfNode / serializeParameterTypesOfNode
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void APropertyAndAParameter_SerializeToTheirAnnotation()
    {
        var (fixture, declaration, members) = ClassFixture("p: number; constructor(a: string) { }");
        var property = (Node)members[0];
        var parameter = (Node)((ConstructorDeclaration)members[1]).Parameters![0];

        Assert.Equal("Number", SerializeTypeOf(fixture, property, declaration));
        Assert.Equal("String", SerializeTypeOf(fixture, parameter, declaration));
    }

    [Fact]
    public void AMethodAndAClass_SerializeToTheFunctionConstructor()
    {
        var (fixture, declaration, members) = ClassFixture("m(a: number) { }");

        Assert.Equal("Function", SerializeTypeOf(fixture, (Node)members[0], declaration));
        Assert.Equal("Function", SerializeTypeOf(fixture, declaration, declaration));
    }

    [Fact]
    public void ANonDeclarationNode_SerializesToVoidZero()
    {
        var fixture = CreateFixture("class C { }\nlet x = 1;");
        var classDeclaration = (ClassDeclaration)fixture.Root.Statements![0];
        var variable = (Node)((VariableStatement)fixture.Root.Statements![1]).DeclarationList!.Declarations![0];

        Assert.Equal("void 0", SerializeTypeOf(fixture, variable, classDeclaration));
    }

    [Fact]
    public void AnAccessor_SerializesToTheSetterAnnotationAndElseToTheGetterReturnType()
    {
        var (fixture, declaration, members) = ClassFixture("get p(): number { return 1; } set p(v: string) { }");

        Assert.Equal("String", SerializeTypeOf(fixture, (Node)members[0], declaration));

        var (onlyGetter, onlyGetterClass, onlyGetterMembers) = ClassFixture("get p(): number { return 1; }");
        Assert.Equal("Number", SerializeTypeOf(onlyGetter, (Node)onlyGetterMembers[0], onlyGetterClass));
    }

    [Fact]
    public void ConstructorParameters_SerializeAsAnArrayOfTheirTypes()
    {
        var (fixture, declaration, _) = ClassFixture("constructor(a: number, b: string) { }");

        Assert.Equal("[Number, String]", SerializeParametersOf(fixture, declaration, declaration));
    }

    [Fact]
    public void TheThisParameter_IsSkippedAndARestParameter_ContributesItsElementType()
    {
        var (fixture, declaration, _) = ClassFixture("constructor(this: C, a: number, ...rest: string[]) { }");

        Assert.Equal("[Number, String]", SerializeParametersOf(fixture, declaration, declaration));
    }

    [Fact]
    public void AConstructorWithoutABody_ContributesNoParameterTypes()
    {
        var (fixture, declaration, _) = ClassFixture("constructor(a: number);");

        Assert.Equal("[]", SerializeParametersOf(fixture, declaration, declaration));
    }

    [Fact]
    public void AMethod_ContributesItsOwnParametersOnlyWhenItHasABody()
    {
        var (withBody, withBodyClass, withBodyMembers) = ClassFixture("m(a: number, b: string) { }");

        Assert.Equal("[Number, String]", SerializeParametersOf(withBody, (Node)withBodyMembers[0], withBodyClass));

        var (withoutBody, withoutBodyClass, withoutBodyMembers) = ClassFixture("m(a: number): void;");

        Assert.Equal("[]", SerializeParametersOf(withoutBody, (Node)withoutBodyMembers[0], withoutBodyClass));
    }

    [Fact]
    public void AGetterWithASetter_SerializesTheSettersParameters()
    {
        var (fixture, declaration, members) = ClassFixture("get p(): number { return 1; } set p(v: string) { }");

        Assert.Equal("[String]", SerializeParametersOf(fixture, (Node)members[0], declaration));

        var (onlyGetter, onlyGetterClass, onlyGetterMembers) = ClassFixture("get p(): number { return 1; }");
        Assert.Equal("[]", SerializeParametersOf(onlyGetter, (Node)onlyGetterMembers[0], onlyGetterClass));
    }

    // ---------------------------------------------------------------------------------------------
    // typeSerializer.ts:242-251 — serializeReturnTypeOfNode
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ADeclaredReturnType_SerializesToItsAnnotation()
    {
        var fixture = CreateFixture("function f(): number { return 1; }");

        Assert.Equal("Number", SerializeReturnTypeOf(fixture, (FunctionDeclaration)fixture.Root.Statements![0]));
    }

    [Fact]
    public void AnAsyncFunctionWithoutAnAnnotation_SerializesToPromise()
    {
        var fixture = CreateFixture("async function f() { }");

        Assert.Equal("Promise", SerializeReturnTypeOf(fixture, (FunctionDeclaration)fixture.Root.Statements![0]));
    }

    [Fact]
    public void AFunctionWithoutAReturnType_SerializesToVoidZero()
    {
        var fixture = CreateFixture("function f() { }");

        Assert.Equal("void 0", SerializeReturnTypeOf(fixture, (FunctionDeclaration)fixture.Root.Statements![0]));
    }

    // ---------------------------------------------------------------------------------------------
    // typeSerializer.ts:355-361 — the JSDoc wrappers, and 360-361 — the impossible kind
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void AJSDocWrapper_SerializesToItsInnerType()
    {
        var fixture = CreateFixture("let x: number;");
        var numberKeyword = DeclaredType(fixture.Root);

        (SyntaxKind Kind, ITypeNode Node)[] wrappers =
        [
            (SyntaxKind.JSDocNullableType, new JSDocNullableType { Type = numberKeyword }),
            (SyntaxKind.JSDocNonNullableType, new JSDocNonNullableType { Type = numberKeyword }),
            (SyntaxKind.JSDocOptionalType, new JSDocOptionalType { Type = numberKeyword }),
        ];

        foreach (var (kind, wrapper) in wrappers)
        {
            Assert.Equal(kind, wrapper.Kind);
            Assert.Equal("Number", SerializeTypeNodeOf(fixture, wrapper));
        }
    }

    [Fact]
    public void AnInferTypeNode_IsAnImpossibleKindAndFails()
    {
        var fixture = CreateFixture("let x: any extends infer T ? number : string;");
        var inferNode = ((ConditionalTypeNode)DeclaredType(fixture.Root)).ExtendsType!;

        var exception = Assert.Throws<InvalidOperationException>(
            () => fixture.Serializer.SerializeTypeNode(fixture.Scope, inferNode));

        Assert.Contains("Node InferType was unexpected.", exception.Message);
    }
}
