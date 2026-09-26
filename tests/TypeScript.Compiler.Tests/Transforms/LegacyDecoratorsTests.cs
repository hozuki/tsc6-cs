namespace TypeScript.Compiler.Tests;

// ported from: src/compiler/transformers/legacyDecorators.ts:88-846
/// <summary>
/// Acceptance for the legacy (TypeScript-style) decorator transform (M7-B): the <c>__decorate</c>/<c>__param</c> calls it
/// appends for a decorated class and for its decorated members, the class alias it hoists for a decorated class that
/// refers to itself inside its own body, and the module exports it moves after the declaration — all measured against the
/// reference's own emit.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (the oracle): every expectation below was measured with
/// <c>ts.transpileModule(source, { compilerOptions })</c> from the reference's own <c>lib/typescript.js</c> (6.0.3) at
/// <c>target: ES2022</c>, <c>module: ESNext</c>, <c>experimentalDecorators: true</c>,
/// <c>useDefineForClassFields: true</c> and <c>alwaysStrict: false</c>. The exact command is recorded in this slice's
/// report; the fixture sources are the <c>&hellip;Source</c> constants below, verbatim.
/// </para>
/// <para>
/// PORT-NOTE (the chain): these fixtures drive <c>TypeScriptTransform.TransformTypeScript</c> and then
/// <c>LegacyDecorators.TransformLegacyDecorators</c>, the order (and the condition) the reference's
/// <c>getScriptTransformers</c> assembles them with (<c>transformer.ts:138-140</c>). The ts transform is not incidental
/// to these tests: the ambient <c>declare function</c> declarations the fixtures decorate with are elided there, and the
/// class the decorators are moved off is the one it leaves behind.
/// </para>
/// <para>
/// PORT-NOTE (the inert transformers of the real chain): at these options the reference's own chain is
/// <c>transformTypeScript</c>, <c>transformLegacyDecorators</c>, <c>transformESNext</c>, <c>transformClassFields</c> and
/// the ES-module transformer. <c>transformClassFields</c> leaves a class field alone at ES2022 with
/// <c>useDefineForClassFields</c> (its static-block and private-element rewrites are gated on
/// <c>languageVersion &lt; ScriptTarget.ES2022</c>), <c>transformESNext</c> rewrites none of the syntax below — the same
/// reading <c>ES2015TransformTests</c> relies on at ES5, where <c>transformESNext</c> is likewise in the chain — and the
/// module transformer is inert for ES-module output. The two-transformers-under-test chain therefore produces the
/// reference's text exactly, which is what each measurement below confirms.
/// </para>
/// <para>
/// PORT-NOTE (<c>alwaysStrict: false</c>): as in <c>TypeScriptTransformTests</c>, the prologue is disabled so each
/// expectation names only the decorator rewrite under test.
/// </para>
/// </remarks>
public class LegacyDecoratorsTests
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
    private static (Program Program, SourceFile Root, ICompilerOptions Options, IEmitResolver Resolver) CreateProgram(string text)
    {
        var options = new FakeCompilerOptions
        {
            SuppressOutputPathCheck = true,
            StrictNullChecks = true,
            AlwaysStrict = false,
            Target = ScriptTarget.ES2022,
            Module = ModuleKind.ESNext,
            ExperimentalDecorators = true,
            UseDefineForClassFields = true,
        };
        var system = new FakeSystem();
        system.Files["/a.ts"] = text;
        var host = Program.CreateCompilerHostWorker(options, /*setParentNodes*/ true, system);
        system.Files[host.GetDefaultLibFileName(options)] = string.Join("\n", StubLib) + "\n";
        var program = Program.CreateProgram(["/a.ts"], options, host);
        var root = program.GetSourceFiles().Single(file => (file.FileName ?? "").EndsWith("a.ts", StringComparison.Ordinal));
        var resolver = program.GetTypeChecker().GetEmitResolver(root, /*cancellationToken*/ null, /*forceDts*/ false);
        return (program, root, options, resolver);
    }

    /// <summary>Emits the test's text through <c>Emitter.EmitFiles</c> with the reference's own two-transformer
    /// prefix.</summary>
    private static string Emit(string text)
    {
        var (program, root, _, resolver) = CreateProgram(text);

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
                ScriptTransformers = [TypeScriptTransform.TransformTypeScript, LegacyDecorators.TransformLegacyDecorators],
                DeclarationTransformers = [],
            },
            /*emitOnly*/ null,
            /*onlyBuildInfo*/ false,
            /*forceDtsEmit*/ null,
            /*skipBuildInfo*/ null);

        return emitted.Replace("\r\n", "\n");
    }

    /// <summary>Asserts the emitted text against a raw literal, normalizing the literal's line endings.</summary>
    /// <remarks>PORT-NOTE: a multi-line raw literal does not carry the newline that would follow its last line, while
    /// <see cref="Emit"/> answers the writer's text including it; the helper states that one newline so every expectation can
    /// stay a plain copy of the reference's output.</remarks>
    private static void AssertEmitted(string expected, string actual)
        => Assert.Equal(expected.ReplaceLineEndings("\n") + "\n", actual);

    /// <summary>Asserts a fixture's emit, prefixed with the reference's own <c>__decorate</c> helper text.</summary>
    private static void AssertEmittedWithDecorateHelper(string expected, string actual)
        => AssertEmitted(DecorateHelper + "\n" + expected, actual);

    /// <summary>Asserts a fixture's emit, prefixed with both helper texts the legacy decorators transform requests.</summary>
    private static void AssertEmittedWithDecorateAndParamHelpers(string expected, string actual)
        => AssertEmitted(DecorateHelper + "\n" + ParamHelper + "\n" + expected, actual);

    // ---------------------------------------------------------------------------------------------
    // The emit helpers the fixtures below reuse (each one copied from the reference's own output)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts — the <c>__decorate</c> helper text.</summary>
    private const string DecorateHelper =
        """
var __decorate = (this && this.__decorate) || function (decorators, target, key, desc) {
    var c = arguments.length, r = c < 3 ? target : desc === null ? desc = Object.getOwnPropertyDescriptor(target, key) : desc, d;
    if (typeof Reflect === "object" && typeof Reflect.decorate === "function") r = Reflect.decorate(decorators, target, key, desc);
    else for (var i = decorators.length - 1; i >= 0; i--) if (d = decorators[i]) r = (c < 3 ? d(r) : c > 3 ? d(target, key, r) : d(target, key)) || r;
    return c > 3 && r && Object.defineProperty(target, key, r), r;
};
""";

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts — the <c>__param</c> helper text.</summary>
    private const string ParamHelper =
        """
var __param = (this && this.__param) || function (paramIndex, decorator) {
    return function (target, key) { decorator(target, key, paramIndex); }
};
""";

    // ---------------------------------------------------------------------------------------------
    // The fixtures (each one handed to the reference's own transpileModule at the options above)
    // ---------------------------------------------------------------------------------------------

    /// <summary>The source of <see cref="ClassMethodAccessorsPropertyAndParameterDecorators_EmitsDecorateAndParamCalls"/>.</summary>
    private const string ClassWithMethodAccessorsPropertyAndParameterDecoratorsSource =
        """
declare function dec(target: any, key?: any, desc?: any): any;
declare function pdec(target: any, key: any, index: number): any;

@dec
class C {
    @dec method(@pdec x: number) { return x; }
    @dec get accessor(): number { return 1; }
    @dec set accessor(v: number) { }
    @dec prop: number = 1;
}
""";

    /// <summary>The source of <see cref="DecoratedClassReferencingItself_EmitsAClassAliasAndReassignsItInAStaticBlock"/>.</summary>
    private const string DecoratedClassReferencingItselfInAMethodSource =
        """
declare function dec(target: any): any;
@dec
class C {
    static x() { return C.y; }
    static y = 1;
}
""";

    /// <summary>The source of <see cref="DecoratedExportedClass_MovesTheExportAfterTheDeclaration"/>.</summary>
    private const string DecoratedExportedClassSource =
        """
declare function dec(target: any): any;
@dec
export class C { }
""";

    /// <summary>The source of <see cref="DecoratedDefaultExportedClass_MovesTheExportAfterTheDeclaration"/>.</summary>
    private const string DecoratedDefaultExportedClassSource =
        """
declare function dec(target: any): any;
@dec
export default class C { }
""";

    /// <summary>The source of <see cref="DecoratedAnonymousDefaultExportedClass_ExportsTheGeneratedAlias"/>.</summary>
    private const string DecoratedAnonymousDefaultExportedClassSource =
        """
declare function dec(target: any): any;
@dec
export default class { }
""";

    /// <summary>The source of <see cref="DecoratedComputedPropertyNameWithAnIdentifier_UsesAGeneratedName"/>.</summary>
    private const string DecoratedIdentifierComputedPropertyNameSource =
        """
declare function dec(...args: any[]): any;
const key = "prop";
@dec
class C {
    @dec [key]() { }
}
""";

    /// <summary>The source of <see cref="DecoratedComputedPropertyNameWithAStringLiteral_IsInlined"/>.</summary>
    private const string DecoratedStringLiteralComputedPropertyNameSource =
        """
declare function dec(...args: any[]): any;
@dec
class C {
    @dec ["prop"]() { }
}
""";

    /// <summary>The source of <see cref="ClassWithDecoratedMembersOnly_KeepsTheClassDeclaration"/>.</summary>
    private const string DecoratedMembersWithoutAClassDecoratorSource =
        """
declare function dec(...args: any[]): any;
class C {
    @dec method() { }
}
""";

    /// <summary>The source of <see cref="DecoratedClassReferencingItselfInAStaticInitializer_SubstitutesTheAlias"/>.</summary>
    private const string DecoratedClassReferencingItselfInAStaticInitializerSource =
        """
declare function dec(target: any): any;
@dec
class C {
    static x = C.y;
    static y = 1;
}
""";

    /// <summary>The source of <see cref="DecoratorContainingAPrivateIdentifier_MovesTheDecorationIntoAStaticBlock"/>.</summary>
    private const string DecoratorWithAPrivateIdentifierInAnExpressionSource =
        """
declare function dec(...args: any[]): any;
class C {
    #x = 1;
    @dec(this.#x) method() { }
}
""";

    // ---------------------------------------------------------------------------------------------
    // The expectations (each one the reference's own output text)
    // ---------------------------------------------------------------------------------------------

    /// <remarks>PORT-NOTE: The whole surface at once: a class decorator, a decorated method with a decorated parameter, a
    /// decorated get/set accessor pair and a decorated property — the four <c>__decorate</c> shapes (method with a `__param`
    /// entry, accessor with `null`, property with `void 0`) and the class-level call.</remarks>
    [Fact]
    public void ClassMethodAccessorsPropertyAndParameterDecorators_EmitsDecorateAndParamCalls()
        => AssertEmittedWithDecorateAndParamHelpers(
            """
let C = class C {
    method(x) { return x; }
    get accessor() { return 1; }
    set accessor(v) { }
    prop = 1;
};
__decorate([
    dec,
    __param(0, pdec)
], C.prototype, "method", null);
__decorate([
    dec
], C.prototype, "accessor", null);
__decorate([
    dec
], C.prototype, "prop", void 0);
C = __decorate([
    dec
], C);
""",
            Emit(ClassWithMethodAccessorsPropertyAndParameterDecoratorsSource));

    /// <remarks>PORT-NOTE: The class-alias path: <c>C.y</c> inside a static method is checked for
    /// <c>ContainsConstructorReference</c>, so an alias is hoisted, substituted for the reference, and — because the class
    /// has a static property declaration at ES2022 — reassigned from <c>this</c> at the head of the body.</remarks>
    [Fact]
    public void DecoratedClassReferencingItself_EmitsAClassAliasAndReassignsItInAStaticBlock()
        => AssertEmittedWithDecorateHelper(
            """
var C_1;
let C = class C {
    static { C_1 = this; }
    static x() { return C_1.y; }
    static y = 1;
};
C = C_1 = __decorate([
    dec
], C);
""",
            Emit(DecoratedClassReferencingItselfInAMethodSource));

    /// <remarks>PORT-NOTE: An exported decorated class: the class becomes a <c>let</c> and <c>createExternalModuleExport</c>
    /// appends `export { C };` after the decoration call.</remarks>
    [Fact]
    public void DecoratedExportedClass_MovesTheExportAfterTheDeclaration()
        => AssertEmittedWithDecorateHelper(
            """
let C = class C {
};
C = __decorate([
    dec
], C);
export { C };
""",
            Emit(DecoratedExportedClassSource));

    /// <remarks>PORT-NOTE: A named default export: <c>createExportDefault(declName)</c> appends `export default C;` after the
    /// decoration call.</remarks>
    [Fact]
    public void DecoratedDefaultExportedClass_MovesTheExportAfterTheDeclaration()
        => AssertEmittedWithDecorateHelper(
            """
let C = class C {
};
C = __decorate([
    dec
], C);
export default C;
""",
            Emit(DecoratedDefaultExportedClassSource));

    /// <remarks>PORT-NOTE: An anonymous default export: the class expression keeps no name and the generated local name
    /// (<c>default_1</c>) is both the decorated binding and the exported one.</remarks>
    [Fact]
    public void DecoratedAnonymousDefaultExportedClass_ExportsTheGeneratedAlias()
        => AssertEmittedWithDecorateHelper(
            """
let default_1 = class {
};
default_1 = __decorate([
    dec
], default_1);
export default default_1;
""",
            Emit(DecoratedAnonymousDefaultExportedClassSource));

    /// <remarks>PORT-NOTE: A computed property name that is not simple-inlineable (an identifier is copyable but not
    /// inlineable, transformation utilities) — the member name becomes a generated name for the <c>ComputedPropertyName</c>
    /// node, which the emitter also writes into the class body as `[_a = key]`.</remarks>
    [Fact]
    public void DecoratedComputedPropertyNameWithAnIdentifier_UsesAGeneratedName()
        => AssertEmittedWithDecorateHelper(
            """
var _a;
const key = "prop";
let C = class C {
    [_a = key]() { }
};
__decorate([
    dec
], C.prototype, _a, null);
C = __decorate([
    dec
], C);
""",
            Emit(DecoratedIdentifierComputedPropertyNameSource));

    /// <remarks>PORT-NOTE: A computed property name that <em>is</em> simple-inlineable (a string literal): the reference
    /// passes the computed name's expression straight to <c>__decorate</c>, so the emit reads `["prop"]` and no temp is
    /// hoisted.</remarks>
    [Fact]
    public void DecoratedComputedPropertyNameWithAStringLiteral_IsInlined()
        => AssertEmittedWithDecorateHelper(
            """
let C = class C {
    ["prop"]() { }
};
__decorate([
    dec
], C.prototype, "prop", null);
C = __decorate([
    dec
], C);
""",
            Emit(DecoratedStringLiteralComputedPropertyNameSource));

    /// <remarks>PORT-NOTE: The without-class-decorators path: <c>childIsDecorated</c> is true but
    /// <c>classOrConstructorParameterIsDecorated</c> is false, so the class declaration is updated in place and only the
    /// member decoration statements follow it.</remarks>
    [Fact]
    public void ClassWithDecoratedMembersOnly_KeepsTheClassDeclaration()
        => AssertEmittedWithDecorateHelper(
            """
class C {
    method() { }
}
__decorate([
    dec
], C.prototype, "method", null);
""",
            Emit(DecoratedMembersWithoutAClassDecoratorSource));

    /// <remarks>PORT-NOTE: The substitution's other caller: the constructor reference sits in a static property initializer,
    /// so <c>trySubstituteClassAlias</c> rewrites it while the alias assignment block precedes the initializers.</remarks>
    [Fact]
    public void DecoratedClassReferencingItselfInAStaticInitializer_SubstitutesTheAlias()
        => AssertEmittedWithDecorateHelper(
            """
var C_1;
let C = class C {
    static { C_1 = this; }
    static x = C_1.y;
    static y = 1;
};
C = C_1 = __decorate([
    dec
], C);
""",
            Emit(DecoratedClassReferencingItselfInAStaticInitializerSource));

    /// <remarks>PORT-NOTE: The private-identifier path: a decorator expression containing `this.#x` makes the reference
    /// append a <c>static {}</c> block holding the decoration statements instead of emitting them after the class, and the
    /// class needs no class decorator for it.</remarks>
    [Fact]
    public void DecoratorContainingAPrivateIdentifier_MovesTheDecorationIntoAStaticBlock()
        => AssertEmittedWithDecorateHelper(
            """
class C {
    #x = 1;
    method() { }
    static {
        __decorate([
            dec(this.#x)
        ], C.prototype, "method", null);
    }
}
""",
            Emit(DecoratorWithAPrivateIdentifierInAnExpressionSource));
}
