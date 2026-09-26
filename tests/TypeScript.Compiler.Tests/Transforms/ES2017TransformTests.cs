namespace TypeScript.Compiler.Tests;

// ported from: src/compiler/transformers/es2017.ts:119-1154
/// <summary>
/// Acceptance for the ES2017 downlevel transform (M7-B): <c>async</c>/<c>await</c>, the colliding-name rewrites of an
/// async body, the lexical-<c>arguments</c> capture and the <c>super</c> substitutions inside async methods.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (the oracle): every expectation below was measured with the reference's own compiler — <c>node
/// lib/tsc.js</c> from <c>D:/Source/GitHub/TypeScript</c> (6.0.3) over a one-file program — at the fixture's own options:
/// <c>--target ES2016 --module commonjs --alwaysStrict false --strictNullChecks true</c>. <c>ts.transpileModule</c>
/// cannot model these fixtures: it forces <c>isolatedModules = true</c> (<c>lib/typescript.js</c>'s
/// <c>transpileOptionValueCompilerOptions</c>), which makes <c>isEffectiveStrictModeSourceFile</c> answer
/// <c>true</c> (<c>utilities.ts:2166-2192</c>) and so flips the source-file-level <c>HasLexicalThis</c> flag this
/// transform reads at <c>es2017.ts:175</c> — the arrow fixture below is exactly where that flag is observable. The
/// fixtures are therefore measured with <c>tsc</c> at the fixture's own options, which is the state the harness below
/// builds.
/// </para>
/// <para>
/// PORT-NOTE (the chain): <c>transformES2017</c> is appended by <c>getScriptTransformers</c> when
/// <c>languageVersion &lt; ScriptTarget.ES2017</c>, so the target below is ES2016 — the one target at which this
/// transform is the only ES2016–ES2019 transform that acts on these fixtures (ES2018's <c>visitAwaitExpression</c> and
/// its siblings return a node unchanged unless the enclosing function is both <c>async</c> and a generator,
/// <c>es2018.ts:395-406</c>). The generator function these fixtures leave standing is native at this target, which is
/// why no <c>__generator</c> declaration appears.
/// </para>
/// </remarks>
public class ES2017TransformTests
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
            Target = ScriptTarget.ES2016,
            Module = ModuleKind.CommonJS,
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

    /// <summary>Emits the test's text through <c>Emitter.EmitFiles</c> with this transform as the only script one.</summary>
    /// <remarks>PORT-NOTE: the fixture drives <c>TypeScriptTransform.TransformTypeScript</c> ahead of this transform —
    /// the reference chain's own order (<c>transformer.ts:138-140</c>), and what erases the ambient <c>declare class
    /// B</c> and the type annotations the two <c>super</c> fixtures carry — and this transform is the only one after
    /// it, which is what makes the substitution hook observable —
    /// <c>onSubstituteNode</c> runs over every node the printer reaches, which is how the <c>_super</c> fixtures below
    /// read the same <c>super.x</c> the reference's chain reads.</remarks>
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
                ScriptTransformers = [TypeScriptTransform.TransformTypeScript, ES2017Transform.TransformES2017],
                DeclarationTransformers = [],
            },
            /*emitOnly*/ null,
            /*onlyBuildInfo*/ false,
            /*forceDtsEmit*/ null,
            /*skipBuildInfo*/ null);

        return emitted.Replace("\r\n", "\n");
    }

    /// <summary>Asserts the emitted text against a raw literal, normalizing the literal's line endings.</summary>
    private static void AssertEmitted(string expected, string actual)
        => Assert.Equal(expected.ReplaceLineEndings("\n") + "\n", actual);

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:915-930 — the <c>__awaiter</c> helper text.</summary>
    private const string AwaiterHelper =
        """
var __awaiter = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
    function adopt(value) { return value instanceof P ? value : new P(function (resolve) { resolve(value); }); }
    return new (P || (P = Promise))(function (resolve, reject) {
        function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
        function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
        function step(result) { result.done ? resolve(result.value) : adopt(result.value).then(fulfilled, rejected); }
        step((generator = generator.apply(thisArg, _arguments || [])).next());
    });
};
""";

    // ---------------------------------------------------------------------------------------------
    // es2017.ts:411-571 — the async function/method/arrow bodies
    // ---------------------------------------------------------------------------------------------

    /// <remarks>PORT-NOTE: the <c>async</c> modifier is elided and the function becomes a call to <c>__awaiter</c>
    /// whose fourth argument is the generator function holding the original body; <c>await x</c> becomes
    /// <c>yield x</c>. <c>this</c> (not <c>void 0</c>) is the first argument because a function body sets the
    /// <c>HasLexicalThis</c> context flag (<c>es2017.ts:239-243</c> and the <c>DoWithContext</c> callers).</remarks>
    [Fact]
    public void AsyncFunction_BecomesAnAwaiterCall()
        => AssertEmitted(
            AwaiterHelper + "\n" +
            """
function f() {
    return __awaiter(this, void 0, void 0, function* () {
        yield x;
    });
}
""",
            Emit("async function f() {\n    await x;\n}\n"));

    /// <remarks>PORT-NOTE: an arrow function's async body is the awaiter call itself — the reference's
    /// <c>transformAsyncFunctionBody</c> overload for <c>ArrowFunction</c> answers a <c>ConciseBody</c>
    /// (<c>es2017.ts:555-571</c>) — and the captured <c>this</c> is the source file's, which the fixture's
    /// non-strict options state as <c>this</c>.</remarks>
    [Fact]
    public void AsyncArrowFunctionWithAConciseBody_BecomesAnAwaiterCall()
        => AssertEmitted(
            AwaiterHelper + "\n" +
            """
var f = () => __awaiter(this, void 0, void 0, function* () { return yield x; });
""",
            Emit("var f = async () => await x;\n"));

    // ---------------------------------------------------------------------------------------------
    // es2017.ts:316-392 — colliding names in an async body
    // ---------------------------------------------------------------------------------------------

    /// <remarks>PORT-NOTE: <c>var p</c> collides with the parameter <c>p</c> (<c>es2017.ts:586-591</c>), so the
    /// declaration list is hoisted into the generator body and its initializer becomes a plain assignment — the
    /// reference's <c>visitVariableStatementInAsyncBody</c> answers an <c>ExpressionStatement</c>.</remarks>
    [Fact]
    public void AsyncFunctionWithACollidingVariableName_ConvertsTheDeclarationToAnAssignment()
        => AssertEmitted(
            AwaiterHelper + "\n" +
            """
function f(p) {
    return __awaiter(this, void 0, void 0, function* () {
        var p;
        p = 1;
        yield p;
    });
}
""",
            Emit("async function f(p) {\n    var p = 1;\n    await p;\n}\n"));

    /// <remarks>PORT-NOTE: the <c>for&hellip;of</c> initializer is a colliding declaration list with a receiver, so
    /// its initializer list is replaced by an assignment target (<c>visitVariableDeclarationListWithCollidingNames</c>
    /// with <c>hasReceiver</c>, <c>es2017.ts:593-605</c>) and the hoisted <c>var a</c> is declared in the generator
    /// body.</remarks>
    [Fact]
    public void AsyncFunctionWithACollidingForOfInitializer_ConvertsTheInitializerToAnAssignment()
        => AssertEmitted(
            AwaiterHelper + "\n" +
            """
function f(a) {
    return __awaiter(this, void 0, void 0, function* () {
        var a;
        for (a of [1]) {
            yield a;
        }
    });
}
""",
            Emit("async function f(a) {\n    for (var a of [1]) {\n        await a;\n    }\n}\n"));

    // ---------------------------------------------------------------------------------------------
    // es2017.ts:693-700 — the lexical `arguments` capture
    // ---------------------------------------------------------------------------------------------

    /// <remarks>PORT-NOTE: the arrow inside the async body reads <c>arguments</c>, so the resolver's
    /// <c>CaptureArguments</c> flag is set: the outer function gets a <c>var arguments_1 = arguments;</c> prologue
    /// (<c>createCaptureArgumentsStatement</c>) and every reference to the async body's own <c>arguments</c> is
    /// substituted by that generated name (<c>argumentsVisitor</c>, <c>es2017.ts:211-231</c>).</remarks>
    [Fact]
    public void AsyncFunctionWhoseBodyReadsArguments_EmitsTheCaptureStatement()
        => AssertEmitted(
            AwaiterHelper + "\n" +
            """
function f() {
    var arguments_1 = arguments;
    return __awaiter(this, void 0, void 0, function* () {
        var g = () => arguments_1;
        yield 1;
    });
}
""",
            Emit("async function f() {\n    var g = () => arguments;\n    await 1;\n}\n"));

    // ---------------------------------------------------------------------------------------------
    // es2017.ts:926-1057 — `super` inside an async method
    // ---------------------------------------------------------------------------------------------

    /// <remarks>PORT-NOTE: the async method reads <c>super.x</c>, so <c>transformMethodBody</c>
    /// (<c>es2017.ts:649-691</c>) enables the substitution and prepends
    /// <c>createSuperAccessVariableStatement</c>'s <c>const _super = Object.create(…)</c> declaration to the method
    /// body; the access inside the generator is then substituted to <c>_super.x</c> by
    /// <c>onSubstituteNode</c>. The generated property accessor keeps the source order of the captured properties,
    /// which is why the getter list is a single entry here.</remarks>
    [Fact]
    public void AsyncMethodAccessingSuper_EmitsTheSuperAccessorStatement()
        => AssertEmitted(
            AwaiterHelper + "\n" +
            """
class C extends B {
    m() {
        const _super = Object.create(null, {
            x: { get: () => super.x }
        });
        return __awaiter(this, void 0, void 0, function* () { return _super.x; });
    }
}
""",
            Emit("declare class B { x: number; }\nclass C extends B {\n    async m() { return super.x; }\n}\n"));

    /// <remarks>PORT-NOTE: an element access on <c>super</c> cannot be captured by an accessor pair, so the method
    /// body gets the scoped <c>asyncSuperHelper</c> instead (<c>es2017.ts:676-687</c>) and every
    /// <c>super[k]</c> becomes <c>_superIndex(k)</c> (<c>createSuperElementAccessInAsyncMethod</c>,
    /// <c>es2017.ts:1027-1057</c>).</remarks>
    [Fact]
    public void AsyncMethodAccessingSuperElement_EmitsTheScopedSuperIndexHelper()
        => AssertEmitted(
            AwaiterHelper + "\n" +
            """
class C extends B {
    m(k) {
        const _superIndex = name => super[name];
        return __awaiter(this, void 0, void 0, function* () { return _superIndex(k); });
    }
}
""",
            Emit("declare class B { x: number; }\nclass C extends B {\n    async m(k: string) { return super[k]; }\n}\n"));
}
