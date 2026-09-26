#nullable enable

namespace TypeScript.Compiler.Tests;

// ported from: src/compiler/transformers/esnext.ts:71-790
/// <summary>
/// Acceptance for the ESNext downlevel transform (M7-B): the explicit resource management declarations
/// (<c>using</c>/<c>await using</c>), the environment block they become, the hoisting that keeps imports, exports,
/// classes and functions at the top level, and the named-evaluation hook the declarations' initializers go through.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (the oracle): every expectation below was measured with the reference's own
/// <c>transformNodes</c>/<c>createPrinter</c> driven by exactly the transformer list this fixture drives —
/// <c>transformTypeScript</c> then <c>transformESNext</c> — from
/// <c>D:/Source/GitHub/TypeScript/lib/typescript.js</c> (6.0.3) at <c>target: ES2020</c>, <c>module: ESNext</c>,
/// <c>alwaysStrict: false</c>, <c>noEmitHelpers: true</c>. <c>target: ES2020</c> is what makes the reference's chain
/// include <c>transformESNext</c> at all (<c>getScriptTransformers</c> pushes it only below <c>ESNext</c>).
/// </para>
/// <para>
/// PORT-NOTE (the chain, and the three cases where the full chain differs): <c>ts.transpileModule</c> at the same
/// options adds <c>transformES2021</c>, <c>transformESDecorators</c>, <c>transformClassFields</c> and the module
/// transformer. Every expectation below is byte-identical under that fuller chain except the three where a transform
/// outside this slice carries the result further, and each records the fuller text where it differs:
/// <c>exportDefaultClassHoistTest</c> and <c>classExpressionInitializer</c> (whose injected
/// <c>static { __setFunctionName(this, …); }</c> block is downlevelled by <c>transformClassFields</c> into
/// <c>(_a = class { }, __setFunctionName(_a, "…"), _a)</c>) and <c>exportEqualsHoist</c> (whose <c>export = _default;</c>
/// is re-spelled by the module transformer).
/// </para>
/// <para>
/// PORT-NOTE (<c>noEmitHelpers</c>): the expectations are the reference's own output for this chain with
/// <c>noEmitHelpers: true</c>, so the helper prelude is out of each of them;
/// <see cref="Helpers_AreRequestedWithTheEnvironmentArgument"/> pins that prelude, and
/// <see cref="AwaitUsing_RequestsTheDisposeResourcesHelperInTheFinallyBlock"/> pins the <c>await</c> form.
/// </para>
/// </remarks>
public class ESNextTransformTests
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
    private static (Program Program, SourceFile Root, ICompilerOptions Options, IEmitResolver Resolver) CreateProgram(
        string text,
        bool noEmitHelpers = true)
    {
        var options = new FakeCompilerOptions
        {
            SuppressOutputPathCheck = true,
            StrictNullChecks = true,
            AlwaysStrict = false,
            NoEmitHelpers = noEmitHelpers,
            Target = ScriptTarget.ES2020,
            Module = ModuleKind.ESNext,
            DownlevelIteration = false,
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

    /// <summary>Emits the test's text through <c>Emitter.EmitFiles</c> with the reference's transformer order.</summary>
    /// <remarks>PORT-NOTE (<c>noEmitHelpers</c>): the expectations below are the reference's own <c>transformNodes</c> output for this chain with <c>noEmitHelpers: true</c>, so the helper prelude the printer would otherwise print (the <c>__addDisposableResource</c>/<c>__disposeResources</c> text <see cref="AddDisposableResourceHelper"/>/<see cref="DisposeResourcesHelper"/> pin) stays out of each of them; <see cref="Helpers_AreRequestedWithTheEnvironmentArgument"/> covers the helper-carrying form.</remarks>
    private static string Emit(string text, bool noEmitHelpers = true)
    {
        var (program, root, _, resolver) = CreateProgram(text, noEmitHelpers);

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
                ScriptTransformers = [TypeScriptTransform.TransformTypeScript, ESNextTransform.TransformESNext],
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
    /// <see cref="Emit"/> answers the writer's text including it; the helper states that one newline so every
    /// expectation can stay a plain copy of the reference's output.</remarks>
    private static void AssertEmitted(string expected, string actual)
        => Assert.Equal(expected.ReplaceLineEndings("\n") + "\n", actual);

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts — the <c>__addDisposableResource</c> helper text.</summary>
    private const string AddDisposableResourceHelper =
        """
        var __addDisposableResource = (this && this.__addDisposableResource) || function (env, value, async) {
            if (value !== null && value !== void 0) {
                if (typeof value !== "object" && typeof value !== "function") throw new TypeError("Object expected.");
                var dispose, inner;
                if (async) {
                    if (!Symbol.asyncDispose) throw new TypeError("Symbol.asyncDispose is not defined.");
                    dispose = value[Symbol.asyncDispose];
                }
                if (dispose === void 0) {
                    if (!Symbol.dispose) throw new TypeError("Symbol.dispose is not defined.");
                    dispose = value[Symbol.dispose];
                    if (async) inner = dispose;
                }
                if (typeof dispose !== "function") throw new TypeError("Object not disposable.");
                if (inner) dispose = function() { try { inner.call(this); } catch (e) { return Promise.reject(e); } };
                env.stack.push({ value: value, dispose: dispose, async: async });
            }
            else if (async) {
                env.stack.push({ async: true });
            }
            return value;
        };
        """;

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts — the <c>__disposeResources</c> helper text.</summary>
    private const string DisposeResourcesHelper =
        """
        var __disposeResources = (this && this.__disposeResources) || (function (SuppressedError) {
            return function (env) {
                function fail(e) {
                    env.error = env.hasError ? new SuppressedError(e, env.error, "An error was suppressed during disposal.") : e;
                    env.hasError = true;
                }
                var r, s = 0;
                function next() {
                    while (r = env.stack.pop()) {
                        try {
                            if (!r.async && s === 1) return s = 0, env.stack.push(r), Promise.resolve().then(next);
                            if (r.dispose) {
                                var result = r.dispose.call(r.value);
                                if (r.async) return s |= 2, Promise.resolve(result).then(next, function(e) { fail(e); return next(); });
                            }
                            else s |= 1;
                        }
                        catch (e) {
                            fail(e);
                        }
                    }
                    if (s === 1) return env.hasError ? Promise.reject(env.error) : Promise.resolve();
                    if (env.hasError) throw env.error;
                }
                return next();
            };
        })(typeof SuppressedError === "function" ? SuppressedError : function (error, suppressed, message) {
            var e = new Error(message);
            return e.name = "SuppressedError", e.error = error, e.suppressed = suppressed, e;
        });
        """;

    /// <summary>The two helpers a <c>using</c> request, printed ahead of the transformed body.</summary>
    /// <remarks>PORT-NOTE: measured from the reference at <c>target: ES2020</c>, <c>module: ESNext</c>,
    /// <c>alwaysStrict: false</c> with helpers enabled — the same two definitions for the synchronous and the
    /// asynchronous fixture.</remarks>
    [Fact]
    public void Helpers_AreRequestedWithTheEnvironmentArgument()
        => AssertEmitted(
            AddDisposableResourceHelper + "\n" + DisposeResourcesHelper + "\n" +
"""
            var x;
            const env_1 = { stack: [], error: void 0, hasError: false };
            try {
                x = __addDisposableResource(env_1, f(), false);
            }
            catch (e_1) {
                env_1.error = e_1;
                env_1.hasError = true;
            }
            finally {
                __disposeResources(env_1);
            }
            """,
            Emit("using x = f();", /*noEmitHelpers*/ false));

    /// <summary><c>await using</c> requests the same disposal helper and awaits its promise.</summary>
    /// <remarks>PORT-NOTE: measured from the reference at the same options with helpers enabled.</remarks>
    [Fact]
    public void AwaitUsing_RequestsTheDisposeResourcesHelperInTheFinallyBlock()
        => AssertEmitted(
            AddDisposableResourceHelper + "\n" + DisposeResourcesHelper + "\n" +
"""
            async function f() {
                const env_1 = { stack: [], error: void 0, hasError: false };
                try {
                    const x = __addDisposableResource(env_1, g(), true);
                }
                catch (e_1) {
                    env_1.error = e_1;
                    env_1.hasError = true;
                }
                finally {
                    const result_1 = __disposeResources(env_1);
                    if (result_1)
                        await result_1;
                }
            }
            """,
            Emit("""
                async function f() {
                    await using x = g();
                }
                """, /*noEmitHelpers*/ false));

    /// <summary>`using x = f();` at the top level hoists the binding and assigns it inside the environment's `try`. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void TopLevelUsing_IsHoistedAndWrappedInAnEnvironmentBlock()
        => AssertEmitted(
            """
            var x;
            const env_1 = { stack: [], error: void 0, hasError: false };
            try {
                x = __addDisposableResource(env_1, f(), false);
            }
            catch (e_1) {
                env_1.error = e_1;
                env_1.hasError = true;
            }
            finally {
                __disposeResources(env_1);
            }
            """,
            Emit("""
                using x = f();
                """));

    /// <summary>Statements after a top-level `using` move into the `try` body. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void TopLevelUsing_KeepsTheFollowingStatementsInTheTryBody()
        => AssertEmitted(
            """
            var x;
            const env_1 = { stack: [], error: void 0, hasError: false };
            try {
                x = __addDisposableResource(env_1, f(), false);
                console.log(x);
            }
            catch (e_1) {
                env_1.error = e_1;
                env_1.hasError = true;
            }
            finally {
                __disposeResources(env_1);
            }
            """,
            Emit("""
                using x = f();
                console.log(x);
                """));

    /// <summary>A block's `using` stays a declaration in the block's own environment. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void BlockUsing_KeepsTheDeclarationLocalToTheEnvironment()
        => AssertEmitted(
            """
            {
                const env_1 = { stack: [], error: void 0, hasError: false };
                try {
                    const x = __addDisposableResource(env_1, f(), false);
                    console.log(x);
                }
                catch (e_1) {
                    env_1.error = e_1;
                    env_1.hasError = true;
                }
                finally {
                    __disposeResources(env_1);
                }
            }
            """,
            Emit("""
                {
                    using x = f();
                    console.log(x);
                }
                """));

    /// <summary>Two declarations of one `using` statement become one comma expression of two assignments. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void TwoDeclarationsInOneUsing_BecomeOneCommaExpression()
        => AssertEmitted(
            """
            var x, y;
            const env_1 = { stack: [], error: void 0, hasError: false };
            try {
                x = __addDisposableResource(env_1, f(), false), y = __addDisposableResource(env_1, g(), false);
            }
            catch (e_1) {
                env_1.error = e_1;
                env_1.hasError = true;
            }
            finally {
                __disposeResources(env_1);
            }
            """,
            Emit("""
                using x = f(), y = g();
                """));

    /// <summary>`for (using x = f(); ; )` becomes a block holding the `using` and the rewritten `for`. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void ForStatement_UsingInitializer_IsLiftedIntoAnEnclosingBlock()
        => AssertEmitted(
            """
            {
                const env_1 = { stack: [], error: void 0, hasError: false };
                try {
                    const x = __addDisposableResource(env_1, f(), false);
                    for (;;) {
                        console.log(x);
                    }
                }
                catch (e_1) {
                    env_1.error = e_1;
                    env_1.hasError = true;
                }
                finally {
                    __disposeResources(env_1);
                }
            }
            """,
            Emit("""
                for (using x = f(); ; ) {
                    console.log(x);
                }
                """));

    /// <summary>`for (using x of y)` iterates a generated `const x_1` and assigns `__addDisposableResource` to `x` inside the body. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void ForOfStatement_UsingInitializer_AssignsTheIterationBindingInsideTheBody()
        => AssertEmitted(
            """
            for (const x_1 of y) {
                const env_1 = { stack: [], error: void 0, hasError: false };
                try {
                    const x = __addDisposableResource(env_1, x_1, false);
                    console.log(x);
                }
                catch (e_1) {
                    env_1.error = e_1;
                    env_1.hasError = true;
                }
                finally {
                    __disposeResources(env_1);
                }
            }
            """,
            Emit("""
                for (using x of y) {
                    console.log(x);
                }
                """));

    /// <summary>A non-block body is wrapped in a multi-line block so the `using` statement has somewhere to go. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void ForOfStatement_UsingInitializer_WithANonBlockBody_WrapsItInABlock()
        => AssertEmitted(
            """
            for (const x_1 of y) {
                const env_1 = { stack: [], error: void 0, hasError: false };
                try {
                    const x = __addDisposableResource(env_1, x_1, false);
                    console.log(x);
                }
                catch (e_1) {
                    env_1.error = e_1;
                    env_1.hasError = true;
                }
                finally {
                    __disposeResources(env_1);
                }
            }
            """,
            Emit("""
                for (using x of y)
                    console.log(x);
                """));

    /// <summary>`await using` passes `true` to `__addDisposableResource` and awaits the result of `__disposeResources`. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void AwaitUsing_UsesTheAsyncResourceHelperAndAwaitsTheDisposal()
        => AssertEmitted(
            """
            async function f() {
                const env_1 = { stack: [], error: void 0, hasError: false };
                try {
                    const x = __addDisposableResource(env_1, g(), true);
                }
                catch (e_1) {
                    env_1.error = e_1;
                    env_1.hasError = true;
                }
                finally {
                    const result_1 = __disposeResources(env_1);
                    if (result_1)
                        await result_1;
                }
            }
            """,
            Emit("""
                async function f() {
                    await using x = g();
                }
                """));

    /// <summary>A synchronous `using` inside an async function keeps the synchronous disposal form. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void Using_InAnAsyncFunction_StaysSynchronous()
        => AssertEmitted(
            """
            async function f() {
                const env_1 = { stack: [], error: void 0, hasError: false };
                try {
                    const x = __addDisposableResource(env_1, g(), false);
                }
                catch (e_1) {
                    env_1.error = e_1;
                    env_1.hasError = true;
                }
                finally {
                    __disposeResources(env_1);
                }
            }
            """,
            Emit("""
                async function f() {
                    using x = g();
                }
                """));

    /// <summary>Everything before the first `using` is preserved where it was, and only what follows is hoisted. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void StatementsBeforeTheFirstUsing_StayInPlace()
        => AssertEmitted(
            """
            const a = 1;
            var x, b;
            const env_1 = { stack: [], error: void 0, hasError: false };
            try {
                x = __addDisposableResource(env_1, f(), false);
                b = 2;
                console.log(a, b);
            }
            catch (e_1) {
                env_1.error = e_1;
                env_1.hasError = true;
            }
            finally {
                __disposeResources(env_1);
            }
            """,
            Emit("""
                const a = 1;
                using x = f();
                const b = 2;
                console.log(a, b);
                """));

    /// <summary>An exported `const` after a `using` becomes a hoisted `export let` plus an assignment. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void ExportedLet_IsHoistedIntoAnExportLetStatement()
        => AssertEmitted(
            """
            var x;
            export let y;
            const env_1 = { stack: [], error: void 0, hasError: false };
            try {
                x = __addDisposableResource(env_1, f(), false);
                y = 1;
            }
            catch (e_1) {
                env_1.error = e_1;
                env_1.hasError = true;
            }
            finally {
                __disposeResources(env_1);
            }
            """,
            Emit("""
                using x = f();
                export const y = 1;
                """));

    /// <summary>An exported function declaration is hoisted out of the `try`. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void ExportedFunctionDeclaration_IsHoistedAboveTheEnvironment()
        => AssertEmitted(
            """
            export function f2() {
                return 1;
            }
            var x;
            const env_1 = { stack: [], error: void 0, hasError: false };
            try {
                x = __addDisposableResource(env_1, f(), false);
            }
            catch (e_1) {
                env_1.error = e_1;
                env_1.hasError = true;
            }
            finally {
                __disposeResources(env_1);
            }
            """,
            Emit("""
                using x = f();
                export function f2() {
                    return 1;
                }
                """));

    /// <summary>A function declaration is hoisted out of the `try`. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void FunctionDeclaration_IsHoistedAboveTheEnvironment()
        => AssertEmitted(
            """
            function g() {
            }
            var x;
            const env_1 = { stack: [], error: void 0, hasError: false };
            try {
                x = __addDisposableResource(env_1, f(), false);
            }
            catch (e_1) {
                env_1.error = e_1;
                env_1.hasError = true;
            }
            finally {
                __disposeResources(env_1);
            }
            """,
            Emit("""
                using x = f();
                function g() {
                }
                """));

    /// <summary>A `var` statement's bindings are hoisted and its initializers become one comma expression. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void VarStatement_HoistsItsBindingsAndBecomesOneCommaExpression()
        => AssertEmitted(
            """
            var x, v, w;
            const env_1 = { stack: [], error: void 0, hasError: false };
            try {
                x = __addDisposableResource(env_1, f(), false);
                v = 1, w = 2;
            }
            catch (e_1) {
                env_1.error = e_1;
                env_1.hasError = true;
            }
            finally {
                __disposeResources(env_1);
            }
            """,
            Emit("""
                using x = f();
                var v = 1, w = 2;
                """));

    /// <summary>A `let` statement is hoisted the same way (into the environment's `var` list). Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void LetStatement_IsHoistedLikeVar()
        => AssertEmitted(
            """
            var x, l;
            const env_1 = { stack: [], error: void 0, hasError: false };
            try {
                x = __addDisposableResource(env_1, f(), false);
                l = 1;
            }
            catch (e_1) {
                env_1.error = e_1;
                env_1.hasError = true;
            }
            finally {
                __disposeResources(env_1);
            }
            """,
            Emit("""
                using x = f();
                let l = 1;
                """));

    /// <summary>A class declaration becomes `C = class C { }` inside the `try`, with `C` hoisted. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void ClassDeclaration_BecomesAnAssignmentOfAClassExpression()
        => AssertEmitted(
            """
            var x, C;
            const env_1 = { stack: [], error: void 0, hasError: false };
            try {
                x = __addDisposableResource(env_1, f(), false);
                C = class C {
                };
            }
            catch (e_1) {
                env_1.error = e_1;
                env_1.hasError = true;
            }
            finally {
                __disposeResources(env_1);
            }
            """,
            Emit("""
                using x = f();
                class C {
                }
                """));

    /// <summary>An exported class declaration also emits `export { C };` for the hoisted binding. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void ExportedClassDeclaration_AddsAnExportClauseForTheHoistedBinding()
        => AssertEmitted(
            """
            export { C };
            var x, C;
            const env_1 = { stack: [], error: void 0, hasError: false };
            try {
                x = __addDisposableResource(env_1, f(), false);
                C = class C {
                    m() {
                    }
                };
            }
            catch (e_1) {
                env_1.error = e_1;
                env_1.hasError = true;
            }
            finally {
                __disposeResources(env_1);
            }
            """,
            Emit("""
                using x = f();
                export class C {
                    m() {
                    }
                }
                """));

    /// <summary>An anonymous default-exported class assigns `_default` and gets its name through the named-evaluation helper. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void DefaultExportedClass_AssignsTheGeneratedDefaultBindingAndNamesTheClass()
        => AssertEmitted(
            """
            export { _default as default };
            var x, _default;
            const env_1 = { stack: [], error: void 0, hasError: false };
            try {
                x = __addDisposableResource(env_1, f(), false);
                _default = class {
                    static { __setFunctionName(this, "default"); }
                };
            }
            catch (e_1) {
                env_1.error = e_1;
                env_1.hasError = true;
            }
            finally {
                __disposeResources(env_1);
            }
            """,
            Emit("""
                using x = f();
                export default class {
                }
                """));

    /// <summary>`export default 1;` becomes `_default = 1;` plus the `export { _default as default }` clause. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void DefaultExport_OfAnExpression_AssignsTheGeneratedDefaultBinding()
        => AssertEmitted(
            """
            export { _default as default };
            var x, _default;
            const env_1 = { stack: [], error: void 0, hasError: false };
            try {
                x = __addDisposableResource(env_1, f(), false);
                _default = 1;
            }
            catch (e_1) {
                env_1.error = e_1;
                env_1.hasError = true;
            }
            finally {
                __disposeResources(env_1);
            }
            """,
            Emit("""
                using x = f();
                export default 1;
                """));

    /// <summary>An identifier default export assigns the generated binding directly. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void DefaultExport_OfAnIdentifier_AssignsTheGeneratedDefaultBinding()
        => AssertEmitted(
            """
            export { _default as default };
            var x, _default;
            const env_1 = { stack: [], error: void 0, hasError: false };
            try {
                x = __addDisposableResource(env_1, f(), false);
                _default = g;
            }
            catch (e_1) {
                env_1.error = e_1;
                env_1.hasError = true;
            }
            finally {
                __disposeResources(env_1);
            }
            """,
            Emit("""
                using x = f();
                export default g;
                """));

    /// <summary>A default-exported function declaration is hoisted as a declaration. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void DefaultExportedFunctionDeclaration_IsHoisted()
        => AssertEmitted(
            """
            export default function () {
            }
            var x;
            const env_1 = { stack: [], error: void 0, hasError: false };
            try {
                x = __addDisposableResource(env_1, f(), false);
            }
            catch (e_1) {
                env_1.error = e_1;
                env_1.hasError = true;
            }
            finally {
                __disposeResources(env_1);
            }
            """,
            Emit("""
                using x = f();
                export default function () {
                }
                """));

    /// <summary>A binding pattern is a grammar error for `using`; the statement falls through to the hoist path, which turns the destructuring into a hoisted binding plus an assignment. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void UsingWithABindingPattern_IsNotAUsingAndGoesThroughTheHoistPath()
        => AssertEmitted(
            """
            var a, c;
            const env_1 = { stack: [], error: void 0, hasError: false };
            try {
                ({ a, b: c } = obj);
            }
            catch (e_1) {
                env_1.error = e_1;
                env_1.hasError = true;
            }
            finally {
                __disposeResources(env_1);
            }
            """,
            Emit("""
                using { a, b: c } = obj;
                """));

    /// <summary>Two top-level `using` statements share one environment and dispose in one `finally`. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void TwoUsingStatements_ShareTheFileEnvironment()
        => AssertEmitted(
            """
            var x, y;
            const env_1 = { stack: [], error: void 0, hasError: false };
            try {
                x = __addDisposableResource(env_1, f(), false);
                y = __addDisposableResource(env_1, g(), false);
            }
            catch (e_1) {
                env_1.error = e_1;
                env_1.hasError = true;
            }
            finally {
                __disposeResources(env_1);
            }
            """,
            Emit("""
                using x = f();
                using y = g();
                """));

    /// <summary>A nested block's `using` gets its own environment (numbered `env_2`) nested in the file's. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void NestedBlockUsing_GetsItsOwnEnvironment()
        => AssertEmitted(
            """
            var x;
            const env_1 = { stack: [], error: void 0, hasError: false };
            try {
                x = __addDisposableResource(env_1, f(), false);
                if (a) {
                    const env_2 = { stack: [], error: void 0, hasError: false };
                    try {
                        const y = __addDisposableResource(env_2, g(), false);
                    }
                    catch (e_1) {
                        env_2.error = e_1;
                        env_2.hasError = true;
                    }
                    finally {
                        __disposeResources(env_2);
                    }
                }
            }
            catch (e_2) {
                env_1.error = e_2;
                env_1.hasError = true;
            }
            finally {
                __disposeResources(env_1);
            }
            """,
            Emit("""
                using x = f();
                if (a) {
                    using y = g();
                }
                """));

    /// <summary>A `using` inside a loop body disposes at the end of each iteration. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void UsingInsideALoop_DisposesEachIteration()
        => AssertEmitted(
            """
            var x;
            const env_1 = { stack: [], error: void 0, hasError: false };
            try {
                x = __addDisposableResource(env_1, f(), false);
                for (const q of z) {
                    const env_2 = { stack: [], error: void 0, hasError: false };
                    try {
                        const w = __addDisposableResource(env_2, g(), false);
                    }
                    catch (e_1) {
                        env_2.error = e_1;
                        env_2.hasError = true;
                    }
                    finally {
                        __disposeResources(env_2);
                    }
                }
            }
            catch (e_2) {
                env_1.error = e_2;
                env_1.hasError = true;
            }
            finally {
                __disposeResources(env_1);
            }
            """,
            Emit("""
                using x = f();
                for (const q of z) {
                    using w = g();
                }
                """));

    /// <summary>A prologue directive is counted out of the transformed region and stays first. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void PrologueStatement_StaysAheadOfTheEnvironment()
        => AssertEmitted(
            """
            "use strict";
            var x;
            const env_1 = { stack: [], error: void 0, hasError: false };
            try {
                x = __addDisposableResource(env_1, f(), false);
            }
            catch (e_1) {
                env_1.error = e_1;
                env_1.hasError = true;
            }
            finally {
                __disposeResources(env_1);
            }
            """,
            Emit("""
                "use strict";
                using x = f();
                """));

    /// <summary>An `export * from` declaration is hoisted out of the `try`. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void ExportStar_IsHoistedAboveTheEnvironment()
        => AssertEmitted(
            """
            export * from "./m";
            var x;
            const env_1 = { stack: [], error: void 0, hasError: false };
            try {
                x = __addDisposableResource(env_1, f(), false);
            }
            catch (e_1) {
                env_1.error = e_1;
                env_1.hasError = true;
            }
            finally {
                __disposeResources(env_1);
            }
            """,
            Emit("""
                using x = f();
                export * from "./m";
                """));

    /// <summary>An `export { a }` declaration is hoisted out of the `try`. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void ExportClause_IsHoistedAboveTheEnvironment()
        => AssertEmitted(
            """
            export { a };
            var x;
            const env_1 = { stack: [], error: void 0, hasError: false };
            try {
                x = __addDisposableResource(env_1, f(), false);
            }
            catch (e_1) {
                env_1.error = e_1;
                env_1.hasError = true;
            }
            finally {
                __disposeResources(env_1);
            }
            """,
            Emit("""
                using x = f();
                export { a };
                """));

    /// <summary>`export = 1;` becomes a body assignment to `_default` plus the file-level `export = _default;`. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void ExportEquals_AssignsTheGeneratedBindingAndEmitsTheAssignmentLast()
        => AssertEmitted(
            """
            var x, _default;
            const env_1 = { stack: [], error: void 0, hasError: false };
            try {
                x = __addDisposableResource(env_1, f(), false);
                _default = 1;
            }
            catch (e_1) {
                env_1.error = e_1;
                env_1.hasError = true;
            }
            finally {
                __disposeResources(env_1);
            }
            export = _default;
            """,
            Emit("""
                using x = f();
                export = 1;
                """));

    /// <summary>An enum (already lowered by the ts transform) is hoisted as a binding plus its IIFE. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void EnumDeclaration_IsHoistedWithTheTsTransformOutput()
        => AssertEmitted(
            """
            var x, E;
            const env_1 = { stack: [], error: void 0, hasError: false };
            try {
                x = __addDisposableResource(env_1, f(), false);
                (function (E) {
                    E[E["A"] = 0] = "A";
                })(E || (E = {}));
            }
            catch (e_1) {
                env_1.error = e_1;
                env_1.hasError = true;
            }
            finally {
                __disposeResources(env_1);
            }
            """,
            Emit("""
                using x = f();
                enum E { A }
                """));

    /// <summary>A namespace is hoisted as a binding plus its IIFE. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void NamespaceDeclaration_IsHoistedWithTheTsTransformOutput()
        => AssertEmitted(
            """
            var x, N;
            const env_1 = { stack: [], error: void 0, hasError: false };
            try {
                x = __addDisposableResource(env_1, f(), false);
                (function (N) {
                    N.a = 1;
                })(N || (N = {}));
            }
            catch (e_1) {
                env_1.error = e_1;
                env_1.hasError = true;
            }
            finally {
                __disposeResources(env_1);
            }
            """,
            Emit("""
                using x = f();
                namespace N {
                    export const a = 1;
                }
                """));

    /// <summary>A `declare` statement has no emit and does not appear in the environment's `try`. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void DeclareStatement_DisappearsFromTheBody()
        => AssertEmitted(
            """
            var x;
            const env_1 = { stack: [], error: void 0, hasError: false };
            try {
                x = __addDisposableResource(env_1, f(), false);
            }
            catch (e_1) {
                env_1.error = e_1;
                env_1.hasError = true;
            }
            finally {
                __disposeResources(env_1);
            }
            """,
            Emit("""
                using x = f();
                declare const d: number;
                """));

    /// <summary>An arrow inside another expression is not the declaration's own named evaluation. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void ArrowInsideAnArgument_IsNotANamedEvaluation()
        => AssertEmitted(
            """
            var x;
            const env_1 = { stack: [], error: void 0, hasError: false };
            try {
                x = __addDisposableResource(env_1, f(() => 1), false);
            }
            catch (e_1) {
                env_1.error = e_1;
                env_1.hasError = true;
            }
            finally {
                __disposeResources(env_1);
            }
            """,
            Emit("""
                using x = f(() => 1);
                """));

    /// <summary>A function expression initializer is wrapped in `__setFunctionName(function () { }, "x")`. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void FunctionExpressionInitializer_GetsTheDeclarationsName()
        => AssertEmitted(
            """
            var x;
            const env_1 = { stack: [], error: void 0, hasError: false };
            try {
                x = __addDisposableResource(env_1, __setFunctionName(function () {
                }, "x"), false);
            }
            catch (e_1) {
                env_1.error = e_1;
                env_1.hasError = true;
            }
            finally {
                __disposeResources(env_1);
            }
            """,
            Emit("""
                using x = function () {
                };
                """));

    /// <summary>An arrow initializer is named the same way. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void ArrowInitializer_GetsTheDeclarationsName()
        => AssertEmitted(
            """
            var x;
            const env_1 = { stack: [], error: void 0, hasError: false };
            try {
                x = __addDisposableResource(env_1, __setFunctionName(() => 1, "x"), false);
            }
            catch (e_1) {
                env_1.error = e_1;
                env_1.hasError = true;
            }
            finally {
                __disposeResources(env_1);
            }
            """,
            Emit("""
                using x = () => 1;
                """));

    /// <summary>A class expression initializer gets the name through the named-evaluation helper. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void ClassExpressionInitializer_GetsTheDeclarationsName()
        => AssertEmitted(
            """
            var x;
            const env_1 = { stack: [], error: void 0, hasError: false };
            try {
                x = __addDisposableResource(env_1, class {
                    static { __setFunctionName(this, "x"); }
                }, false);
            }
            catch (e_1) {
                env_1.error = e_1;
                env_1.hasError = true;
            }
            finally {
                __disposeResources(env_1);
            }
            """,
            Emit("""
                using x = class {
                };
                """));

    /// <summary>The binding is hoisted before it is assigned, so `using x = x;` is an ordinary assignment. Measured through the reference's own `transformTypeScript` + `transformESNext` at `target: ES2020`, `module: ESNext`, `alwaysStrict: false`, `noEmitHelpers: true`.</summary>
    [Fact]
    public void SelfReference_IsJustAnAssignment()
        => AssertEmitted(
            """
            var x;
            const env_1 = { stack: [], error: void 0, hasError: false };
            try {
                x = __addDisposableResource(env_1, x, false);
            }
            catch (e_1) {
                env_1.error = e_1;
                env_1.hasError = true;
            }
            finally {
                __disposeResources(env_1);
            }
            """,
            Emit("""
                using x = x;
                """));

}
