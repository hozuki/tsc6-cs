namespace TypeScript.Compiler.Tests;

// ported from: src/compiler/transformers/ts.ts:235-2750
/// <summary>
/// Acceptance for the TypeScript erase transform (M7-B): the statements and expressions it elides, and the
/// enum/namespace/parameter-property rewrites it substitutes for them.
/// </summary>
/// <remarks>
/// <para>
/// The fixture builds a real <see cref="Program"/> over the test's own text plus the smallest stub library
/// <c>initializeTypeChecker</c> needs (the shape <c>TypeSerializerTests</c> records), so every question the transform
/// asks the emit resolver — import/export elision, const-enum values, namespace export containers — is answered by the
/// ported checker rather than a double.
/// </para>
/// <para>
/// Every assertion is about emitted JavaScript: <see cref="Emit"/> drives the landed emit path
/// (<c>Emitter.EmitFiles</c>) with this transform as its only script transformer — the chain
/// <c>getTransformers</c> assembles does not exist yet (Transformer.cs's named blocker), so this is the smallest thing
/// that stands in for it — and reads back what the emit host was handed.
/// </para>
/// <para>
/// PORT-NOTE (the fixture's <c>alwaysStrict: false</c>): <c>getAlwaysStrict({})</c> is <c>true</c>, so a script's
/// transformed tree always carries a <c>"use strict";</c> prologue (utilities.ts:9359, and the port's
/// <c>Utilities.GetAlwaysStrict</c> records the same reading). The fixture disables it so each expectation names only
/// the erasure under test; <c>ScriptFiles_GetTheUseStrictPrologue</c> covers the other setting on its own.
/// </para>
/// <para>
/// PORT-NOTE (the oracle): every expectation below was measured against the reference before it was pinned — with
/// <c>ts.transpileModule(source, { compilerOptions })</c> from the reference's own <c>lib/typescript.js</c> (6.0.3) at
/// the same options (<c>target</c>, <c>strictNullChecks: true</c>, <c>alwaysStrict</c>), or, for the two readings
/// <c>transpileModule</c> cannot model, with <c>node lib/tsc.js</c> over a real program: it implies
/// <c>isolatedModules</c> (so <c>preserveConstEnums</c> forces a const enum to be emitted and its members never
/// inlined), and it has no second file for the alias-resolution questions. The two places where this slice's chain is
/// deliberately smaller than the reference's are named at the tests that show them (the ES module transformer's
/// <c>export {}</c> and the class-fields transformer's elision of an uninitialized field).
/// </para>

/// </remarks>
public class TypeScriptTransformTests
{
    /// <summary>The smallest library that lets <c>initializeTypeChecker</c> resolve the globals it asks for.</summary>
    private static readonly string[] StubLib =
    [
        "interface Array<T> { }",
        "interface Object { }",
        "interface Function { }",
        "declare var undefined: undefined;",
    ];

    /// <summary>Builds the program the test's text is checked in and asks its checker for the emit resolver.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>verifyCompilerOptions</c>' emit-path check needs <c>getEmitHost</c> (an M7 blocker), so the options
    /// take <c>suppressOutputPathCheck</c> — the flag the reference's own build mode sets and the one
    /// <c>TypeSerializerTests</c> takes. The resolver is fetched with <c>forceDts: false</c>, which is what
    /// <c>Program.getEmitResolver</c> does for a normal emit.
    /// </remarks>
    private static (Program Program, SourceFile Root, ICompilerOptions Options, IEmitResolver Resolver) CreateProgram(
        string text,
        ScriptTarget target,
        bool? alwaysStrict,
        string? extraFile)
    {
        var options = new FakeCompilerOptions
        {
            SuppressOutputPathCheck = true,
            StrictNullChecks = true,
            AlwaysStrict = alwaysStrict,
            Target = target,
        };
        var system = new FakeSystem();
        system.Files["/a.ts"] = text;
        if (extraFile is not null) system.Files["/b.ts"] = extraFile;
        var host = Program.CreateCompilerHostWorker(options, /*setParentNodes*/ true, system);
        system.Files[host.GetDefaultLibFileName(options)] = string.Join("\n", StubLib) + "\n";
        var program = Program.CreateProgram(["/a.ts"], options, host);
        var root = program.GetSourceFiles().Single(file => (file.FileName ?? "").EndsWith("a.ts", StringComparison.Ordinal));
        var resolver = program.GetTypeChecker().GetEmitResolver(root, /*cancellationToken*/ null, /*forceDts*/ false);
        return (program, root, options, resolver);
    }

    /// <summary>Emits the test's text through <c>Emitter.EmitFiles</c> with this transform as the only script one.</summary>
    /// <remarks>
    /// PORT-NOTE: this is the whole emit path with this transform standing in for the chain <c>getTransformers</c> will
    /// assemble (Transformer.cs's named blocker), so the substitution hook — <c>onSubstituteNode</c> — runs over every
    /// node the printer reaches, including the ones this transform synthesized. That is what makes these tests a net
    /// for the resolver's parse-tree mapping, not just for the erasure itself.
    /// </remarks>
    private static string Emit(
        string text,
        ScriptTarget target = ScriptTarget.ESNext,
        bool? alwaysStrict = false,
        string? extraFile = null)
    {
        var (program, root, _, resolver) = CreateProgram(text, target, alwaysStrict, extraFile);

        var emitted = "";
        var emitHost = program.GetEmitHost((fileName, writeText, _, _, _, _, _) =>
        {
            if (fileName.EndsWith(".js", StringComparison.Ordinal)) emitted = writeText;
        });

        Emitter.EmitFiles(
            resolver,
            emitHost,
            root,
            new EmitTransformers
            {
                ScriptTransformers = [TypeScriptTransform.TransformTypeScript],
                DeclarationTransformers = [],
            },
            /*emitOnly*/ null,
            /*onlyBuildInfo*/ false,
            /*forceDtsEmit*/ null,
            /*skipBuildInfo*/ null);

        return emitted.Replace("\r\n", "\n");
    }

    // ---------------------------------------------------------------------------------------------
    // ts.ts:642-833 — visitTypeScript's elisions
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void TypeOnlyDeclarations_AreNotEmitted()
        => Assert.Equal("", Emit("interface I { x: number }\ntype T = string;\ndeclare var d: number;\n").Trim());

    [Fact]
    public void AmbientNamespace_IsNotEmitted()
        => Assert.Equal("", Emit("declare namespace N { const x: number; }\n").Trim());

    [Fact]
    public void TypeAnnotations_AreErasedFromEveryDeclarationKind()
        => Assert.Equal(
            "let x = 1;\nfunction f(a) { }\nclass C {\n    m(a) { }\n}\n",
            Emit("let x: number = 1;\nfunction f(a: string): void { }\nclass C { m(a: number): void { } }\n"));

    [Theory]
    [InlineData("let y = 1 as any;", "let y = 1;")]
    [InlineData("let y = <any>1;", "let y = 1;")]
    [InlineData("let y = 1 satisfies number;", "let y = 1;")]
    [InlineData("declare const x: number;\nlet y = x!;", "let y = x;")]
    public void Assertions_AreErased(string source, string expected)
        => Assert.Equal(expected, Emit(source).Trim());

    [Fact]
    public void ImplementsClauses_AndTypeOnlyModifiers_AreErased()
        => Assert.Equal(
            "class C {\n    x = 1;\n}\n",
            Emit("interface I { x: number }\nclass C implements I { private readonly x: number = 1; }\n"));

    [Fact]
    public void OverloadSignaturesAndAbstractMembers_AreElided()
        => Assert.Equal(
            "class C {\n    m() { }\n}\n",
            Emit("abstract class C { abstract m(): void;\n    m(): void { }\n}\n"));

    [Fact]
    public void IndexSignatures_AreElided()
        => Assert.Equal(
            "class C {\n    x = 1;\n}\n",
            Emit("class C { [key: string]: number; x = 1; }\n"));

    [Fact]
    public void JavaScriptBodies_GetUseStrictPrologues()
        => Assert.Equal(
            "\"use strict\";\nlet x = 1;\n",
            Emit("let x: number = 1;\n", alwaysStrict: null));

    [Fact]
    public void AnExternalModuleAtES2015_DoesNotGetTheUseStrictPrologue()
        => Assert.Equal(
            "import \"./b\";\n",
            Emit("import \"./b\";\n", target: ScriptTarget.ES2015, alwaysStrict: null, extraFile: "export const A = 1;\n"));

    // ---------------------------------------------------------------------------------------------
    // ts.ts:1382-1466 — parameter properties
    // ---------------------------------------------------------------------------------------------

    /// <remarks>PORT-NOTE: the <c>x;</c> member is this transform's own output — it prepends a field declaration for
    /// each parameter property (<c>ts.ts:1057-1079</c>), and the class-fields transformer (not in this slice's chain,
    /// and not run at all at <c>target: esnext</c>) is what a lower target elides. The reference's esnext program build
    /// shows the same member.</remarks>
    [Fact]
    public void ConstructorParameterProperties_BecomeAssignments()
        => Assert.Equal(
            "class C {\n    x;\n    constructor(x) {\n        this.x = x;\n    }\n}\n",
            Emit("class C { constructor(public x: number) { } }\n"));

    [Fact]
    public void ConstructorParameterProperties_FollowTheSuperCall()
        => Assert.Equal(
            "class D {\n}\nclass C extends D {\n    x;\n    y;\n    constructor(x, y) {\n        super();\n        this.x = x;\n        this.y = y;\n    }\n}\n",
            Emit("class D { }\nclass C extends D { constructor(public x: number, public y: number) { super(); } }\n"));

    [Fact]
    public void DestructuredParameterProperties_AreIgnored()
        => Assert.Equal(
            "class C {\n    constructor({ x }) {\n    }\n}\n",
            Emit("class C { constructor(public { x }: { x: number }) { } }\n"));

    // ---------------------------------------------------------------------------------------------
    // ts.ts:1802-1891 / ts.ts:2081-2245 — enums and namespaces
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Enum_EmitsTheIifeAndTheElementAssignments()
    {
        var emitted = Emit("enum E { A, B = 2 }\n");

        Assert.Contains("var E;", emitted);
        Assert.Contains("(function (E) {", emitted);
        Assert.Contains("E[E[\"A\"] = 0] = \"A\";", emitted);
        Assert.Contains("E[E[\"B\"] = 2] = \"B\";", emitted);
        Assert.Contains("})(E || (E = {}));", emitted);
    }

    /// <remarks>PORT-NOTE: measured with <c>node lib/tsc.js --target esnext --strictNullChecks --alwaysStrict false</c>
    /// rather than <c>transpileModule</c>, which implies <c>isolatedModules</c> and would therefore preserve the enum
    /// instead of eliding it. The substituted literal keeps the reference's synthetic trailing comment
    /// (<c>ts.ts:2729-2731</c>).</remarks>
    [Fact]
    public void AConstEnumIsElided_AndItsMembersAreInlined()
        => Assert.Equal("let x = 1 /* E.A */;", Emit("const enum E { A = 1 }\nlet x = E.A;\n").Trim());

    [Fact]
    public void ANonConstEnum_KeepsItsMembersUnsubstituted()
        => Assert.Contains("E[E[\"A\"] = 0] = \"A\";", Emit("enum E { A }\nlet x = E.A;\n"));

    [Fact]
    public void Namespace_EmitsTheIifeAndQualifiesItsExports()
    {
        var emitted = Emit("namespace N { export const x: number = 1; }\n");

        Assert.Contains("var N;", emitted);
        Assert.Contains("(function (N) {", emitted);
        Assert.Contains("N.x = 1;", emitted);
        Assert.Contains("})(N || (N = {}));", emitted);
    }

    [Fact]
    public void AMergedNamespace_EmitsTheVariableStatementOnlyOnce()
    {
        var emitted = Emit("namespace N { export const x = 1; }\nnamespace N { export const y = 2; }\n");

        Assert.Equal(1, emitted.Split("var N;").Length - 1);
        Assert.Contains("N.x = 1;", emitted);
        Assert.Contains("N.y = 2;", emitted);
    }

    [Fact]
    public void ADottedNamespace_NestsTheBlocks()
        => Assert.Equal(
            "var A;\n(function (A) {\n    var B;\n    (function (B) {\n        B.x = 1;\n    })(B = A.B || (A.B = {}));\n})(A || (A = {}));\n",
            Emit("namespace A.B { export const x = 1; }\n"));

    /// <remarks>PORT-NOTE: the class transformer the reference's chain would apply at a lower target is absent here,
    /// and at <c>target: esnext</c> the reference emits the same thing.</remarks>
    [Fact]
    public void ComputedPropertyNames_ArePreserved()
        => Assert.Equal(
            "class C {\n    [Symbol.iterator]() { }\n    get [1]() { return 1; }\n}\n",
            Emit("class C { [Symbol.iterator](): void { } get [1]() { return 1; } }\n"));

    /// <remarks>PORT-NOTE: the class is declared inside the namespace's IIFE and assigned to the namespace container's
    /// member (<c>ts.ts:1001-1008</c>), which is what makes an exported class visible to the namespace's consumers once
    /// the module transformers run.</remarks>
    [Fact]
    public void AnExportedClassInANamespace_EmitsTheMemberAssignment()
        => Assert.Equal(
            "var N;\n(function (N) {\n    class C {\n        m() { }\n    }\n    N.C = C;\n})(N || (N = {}));\n",
            Emit("namespace N { export class C { m(): void { } } }\n"));

    [Fact]
    public void AnExportedFunctionInANamespace_EmitsTheMemberAssignment()
        => Assert.Equal(
            "var N;\n(function (N) {\n    function f() { }\n    N.f = f;\n})(N || (N = {}));\n",
            Emit("namespace N { export function f(): void { } }\n"));

    [Fact]
    public void AnExportedImportEqualsInANamespace_BecomesAMemberAssignment()
        => Assert.Equal(
            "var N;\n(function (N) {\n    N.a = B.C;\n})(N || (N = {}));\n",
            Emit("declare namespace B { const C: number; }\nnamespace N { export import a = B.C; }\n"));

    [Fact]
    public void AMergedEnumAndNamespace_EmitsTheVariableStatementOnlyOnce()
    {
        var emitted = Emit("enum E { A }\nnamespace E { export const x = 1; }\n");

        Assert.Equal(1, emitted.Split("var E;").Length - 1);
        Assert.Contains("E[E[\"A\"] = 0] = \"A\";", emitted);
        Assert.Contains("E.x = 1;", emitted);
    }

    /// <remarks>PORT-NOTE: measured with <c>node lib/tsc.js</c> for the reason the single-member case records; the
    /// initializer of <c>B</c> is resolved by the checker (<c>A + 1</c>, a cross-member evaluation), which is the
    /// <c>getEnumMemberValue</c> path rather than the syntactic one.</remarks>
    [Fact]
    public void AConstEnumWithAnExpressionInitializer_InlinesTheEvaluatedValue()
        => Assert.Equal("let x = 2 /* E.B */;", Emit("const enum E { A = 1, B = A + 1 }\nlet x = E.B;\n").Trim());

    // ---------------------------------------------------------------------------------------------
    // ts.ts:2259-2489 — import and export elision
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ATypeOnlyImportAndAnUnreferencedValueImport_AreElidedWhileSideEffectImportsAreKept()
        => Assert.Equal(
            "import \"./b\";\n",
            Emit(
                "import type { A } from \"./b\";\nimport { B } from \"./b\";\nimport \"./b\";\n",
                extraFile: "export const A = 1;\nexport const B = 2;\n"));

    [Fact]
    public void AReferencedValueImport_IsKept()
        => Assert.Equal(
            "import { A } from \"./b\";\nconsole.log(A);\n",
            Emit("import { A } from \"./b\";\nconsole.log(A);\n", extraFile: "export const A = 1;\n"));

    /// <remarks>PORT-NOTE: the reference's whole chain answers <c>export {};</c> here — the declaration is elided by
    /// this transform and the ES module transformer then marks the (now export-less) module as one. This slice's chain
    /// stops after the first step, so the empty output is the same tree the reference reached before that
    /// transformer ran.</remarks>
    [Fact]
    public void ATypeOnlyExportDeclaration_IsElided()
        => Assert.Equal(
            "",
            Emit("export type { A } from \"./b\";\n", extraFile: "export const A = 1;\n").Trim());

    [Fact]
    public void AnExportStarDeclaration_IsKeptForItsSideEffects()
        => Assert.Equal(
            "export * from \"./b\";\n",
            Emit("export * from \"./b\";\n", extraFile: "export const A = 1;\n"));

    [Fact]
    public void ATypeOnlyImportEquals_IsElided()
        => Assert.Equal(
            "",
            Emit("import type A = B.C;\n", extraFile: "export namespace B { export const C = 1; }\n").Trim());

    [Fact]
    public void AnImportEquals_BecomesAVariableStatement()
        => Assert.Equal(
            "var A = B.C;\n",
            Emit("declare namespace B { const C: number; }\nimport A = B.C;\n"));
}
