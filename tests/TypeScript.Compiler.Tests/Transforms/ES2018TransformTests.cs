namespace TypeScript.Compiler.Tests;

// ported from: src/compiler/transformers/es2018.ts:154-1486
/// <summary>
/// Acceptance for the ES2018 downlevel transform (M7-B): object rest/spread, async generators, <c>for await&hellip;of</c>,
/// the object-rest parameters of a signature and the object-rest pattern of a <c>catch</c> clause.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (the oracle): every expectation below was measured with the reference's own compiler — <c>node
/// lib/tsc.js</c> from <c>D:/Source/GitHub/TypeScript</c> (6.0.3) over a one-file program — at the fixture's own options:
/// <c>--target ES2017 --module commonjs --alwaysStrict false --strictNullChecks true</c>. <c>ts.transpileModule</c>
/// cannot model these fixtures: it forces <c>isolatedModules = true</c> (<c>lib/typescript.js</c>'s
/// <c>transpileOptionValueCompilerOptions</c>), which makes <c>isEffectiveStrictModeSourceFile</c> answer
/// <c>true</c> (<c>utilities.ts:2166-2192</c>) and so flips the source-file-level <c>HasLexicalThis</c> flag the async
/// generator body reads at <c>es2018.ts:1181</c>. The fixtures are therefore measured with <c>tsc</c> at the fixture's
/// own options, which is the state the harness below builds.
/// </para>
/// <para>
/// PORT-NOTE (the chain): <c>transformES2018</c> is appended by <c>getScriptTransformers</c> when
/// <c>languageVersion &lt; ScriptTarget.ES2018</c>, so the target below is ES2017 — the one target at which this
/// transform is the only ES2016–ES2019 transform that acts on these fixtures (ES2019's <c>visitCatchClause</c> only
/// reads a clause with no variable declaration, and the object-spread fixtures request no ES2019 syntax).
/// <c>Object.assign</c> is the reference's own reading of an object spread at this target
/// (<c>createAssignHelper</c>, <c>factory/emitHelpers.ts:402-412</c> asks for <c>__assign</c> only below ES2015), while
/// object <em>rest</em> always asks for the <c>__rest</c> helper.
/// </para>
/// </remarks>
public class ES2018TransformTests
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
            Target = ScriptTarget.ES2017,
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
                ScriptTransformers = [TypeScriptTransform.TransformTypeScript, ES2018Transform.TransformES2018],
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

    // ---------------------------------------------------------------------------------------------
    // The emit helpers the fixtures below reuse (each one copied from the reference's own output)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts — the <c>__rest</c> helper text.</summary>
    private const string RestHelper =
        """
var __rest = (this && this.__rest) || function (s, e) {
    var t = {};
    for (var p in s) if (Object.prototype.hasOwnProperty.call(s, p) && e.indexOf(p) < 0)
        t[p] = s[p];
    if (s != null && typeof Object.getOwnPropertySymbols === "function")
        for (var i = 0, p = Object.getOwnPropertySymbols(s); i < p.length; i++) {
            if (e.indexOf(p[i]) < 0 && Object.prototype.propertyIsEnumerable.call(s, p[i]))
                t[p[i]] = s[p[i]];
        }
    return t;
};
""";

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts — the <c>__await</c> helper text.</summary>
    private const string AwaitHelper =
        """
var __await = (this && this.__await) || function (v) { return this instanceof __await ? (this.v = v, this) : new __await(v); }
""";

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts — the <c>__asyncValues</c> helper text.</summary>
    private const string AsyncValuesHelper =
        """
var __asyncValues = (this && this.__asyncValues) || function (o) {
    if (!Symbol.asyncIterator) throw new TypeError("Symbol.asyncIterator is not defined.");
    var m = o[Symbol.asyncIterator], i;
    return m ? m.call(o) : (o = typeof __values === "function" ? __values(o) : o[Symbol.iterator](), i = {}, verb("next"), verb("throw"), verb("return"), i[Symbol.asyncIterator] = function () { return this; }, i);
    function verb(n) { i[n] = o[n] && function (v) { return new Promise(function (resolve, reject) { v = o[n](v), settle(resolve, reject, v.done, v.value); }); }; }
    function settle(resolve, reject, d, v) { Promise.resolve(v).then(function(v) { resolve({ value: v, done: d }); }, reject); }
};
""";

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts — the <c>__asyncDelegator</c> helper text.</summary>
    private const string AsyncDelegatorHelper =
        """
var __asyncDelegator = (this && this.__asyncDelegator) || function (o) {
    var i, p;
    return i = {}, verb("next"), verb("throw", function (e) { throw e; }), verb("return"), i[Symbol.iterator] = function () { return this; }, i;
    function verb(n, f) { i[n] = o[n] ? function (v) { return (p = !p) ? { value: __await(o[n](v)), done: false } : f ? f(v) : v; } : f; }
};
""";

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts — the <c>__asyncGenerator</c> helper text.</summary>
    private const string AsyncGeneratorHelper =
        """
var __asyncGenerator = (this && this.__asyncGenerator) || function (thisArg, _arguments, generator) {
    if (!Symbol.asyncIterator) throw new TypeError("Symbol.asyncIterator is not defined.");
    var g = generator.apply(thisArg, _arguments || []), i, q = [];
    return i = Object.create((typeof AsyncIterator === "function" ? AsyncIterator : Object).prototype), verb("next"), verb("throw"), verb("return", awaitReturn), i[Symbol.asyncIterator] = function () { return this; }, i;
    function awaitReturn(f) { return function (v) { return Promise.resolve(v).then(f, reject); }; }
    function verb(n, f) { if (g[n]) { i[n] = function (v) { return new Promise(function (a, b) { q.push([n, v, a, b]) > 1 || resume(n, v); }); }; if (f) i[n] = f(i[n]); } }
    function resume(n, v) { try { step(g[n](v)); } catch (e) { settle(q[0][3], e); } }
    function step(r) { r.value instanceof __await ? Promise.resolve(r.value.v).then(fulfill, reject) : settle(q[0][2], r); }
    function fulfill(value) { resume("next", value); }
    function reject(value) { resume("throw", value); }
    function settle(f, v) { if (f(v), q.shift(), q.length) resume(q[0][0], q[0][1]); }
};
""";

    // ---------------------------------------------------------------------------------------------
    // es2018.ts:482-549 — object spread
    // ---------------------------------------------------------------------------------------------

    /// <remarks>PORT-NOTE: the non-spread properties are chunked into one object literal and <c>__assign</c>'s
    /// two-segment call is <c>Object.assign({ a: 1 }, b)</c> at this target.</remarks>
    [Fact]
    public void ObjectSpread_ChunksThePropertiesAndCallsAssign()
        => AssertEmitted(
            """
var o = Object.assign({ a: 1 }, b);
""",
            Emit("var o = { a: 1, ...b };\n"));

    /// <remarks>PORT-NOTE: a leading spread makes the first argument an empty object literal, and the remaining
    /// chunk is appended with a second <c>__assign</c> call so no element's evaluation order changes.</remarks>
    [Fact]
    public void ObjectSpreadWithALeadingSpread_StartsWithAnEmptyObjectLiteral()
        => AssertEmitted(
            """
var o = Object.assign(Object.assign({}, b), { a: 1 });
""",
            Emit("var o = { ...b, a: 1 };\n"));

    /// <remarks>PORT-NOTE: three segments produce the reference's nested calls — the argument list of <c>__assign</c>
    /// is never longer than two because either side of a spread may have side effects.</remarks>
    [Fact]
    public void ObjectSpreadBetweenTwoChunks_NestsTheAssignCalls()
        => AssertEmitted(
            """
var o = Object.assign(Object.assign({ a: 1 }, b), { c: 2 });
""",
            Emit("var o = { a: 1, ...b, c: 2 };\n"));

    // ---------------------------------------------------------------------------------------------
    // es2018.ts:643-708 — object rest in a binding
    // ---------------------------------------------------------------------------------------------

    /// <remarks>PORT-NOTE: <c>visitVariableDeclarationWorker</c> flattens the binding pattern through
    /// <c>flattenDestructuringBinding</c> at <c>FlattenLevel.ObjectRest</c>, which is what emits the <c>__rest</c>
    /// call and the generated <c>rest</c> declaration.</remarks>
    [Fact]
    public void ObjectRestInAVariableDeclaration_EmitsTheRestHelper()
        => AssertEmitted(
            RestHelper + "\n" +
            """
var { a } = obj, rest = __rest(obj, ["a"]);
""",
            Emit("var { a, ...rest } = obj;\n"));

    /// <remarks>PORT-NOTE: an object-rest parameter becomes a generated parameter binding and a
    /// <c>CustomPrologue</c> declaration at the head of the body (<c>appendObjectRestAssignmentsIfNeeded</c>,
    /// <c>es2018.ts:1281-1310</c>).</remarks>
    [Fact]
    public void ObjectRestParameter_EmitsAGeneratedParameterAndABodyPrologue()
        => AssertEmitted(
            RestHelper + "\n" +
            """
function f(_a) { var { a } = _a, b = __rest(_a, ["a"]); }
""",
            Emit("function f({ a, ...b }) { }\n"));

    /// <remarks>PORT-NOTE: a parameter <em>after</em> an object-rest parameter keeps its generated name and its
    /// initializer is converted into a body prologue statement — the reference's own reading of
    /// <c>parametersWithPrecedingObjectRestOrSpread</c> (<c>es2018.ts:974-985</c>) and of <c>appendObjectRestAssignmentsIfNeeded</c>'s
    /// initializer arm.</remarks>
    [Fact]
    public void ParameterAfterAnObjectRestParameter_KeepsItsGeneratedNameAndBodyPrologue()
        => AssertEmitted(
            RestHelper + "\n" +
            """
function f(a, _a, d) { var { b } = _a, c = __rest(_a, ["b"]);
    if (d === void 0) { d = 1; } }
""",
            Emit("function f(a, { b, ...c }, d = 1) { }\n"));

    /// <remarks>PORT-NOTE: the <c>catch</c> clause's binding pattern is moved into a generated catch variable and
    /// the flattened declarations are prepended to the block (<c>visitCatchClause</c>, <c>es2018.ts:643-666</c>).</remarks>
    [Fact]
    public void ObjectRestInACatchClauseBinding_MovesThePatternIntoTheBlock()
        => AssertEmitted(
            RestHelper + "\n" +
            """
try { }
catch (_a) {
    var { a } = _a, b = __rest(_a, ["a"]);
}
""",
            Emit("try { } catch ({ a, ...b }) { }\n"));

    // ---------------------------------------------------------------------------------------------
    // es2018.ts:395-469, 1164-1218 — async generators
    // ---------------------------------------------------------------------------------------------

    /// <remarks>PORT-NOTE: an async generator becomes a function whose body returns
    /// <c>__asyncGenerator(this, arguments, function* f_1() { … })</c> — the generated name of the inner generator
    /// comes from the function's own name through <c>getGeneratedNameForNode</c> — and every <c>await</c> inside it
    /// becomes <c>yield __await(…)</c>.</remarks>
    [Fact]
    public void AsyncGenerator_AwaitedExpression_BecomesAYieldedAwait()
        => AssertEmitted(
            AwaitHelper + "\n" +
            AsyncGeneratorHelper + "\n" +
            """
function f() {
    return __asyncGenerator(this, arguments, function* f_1() {
        yield __await(x);
    });
}
""",
            Emit("async function* f() {\n    await x;\n}\n"));

    /// <remarks>PORT-NOTE: the <c>yield</c> forms of an async generator — a plain yield becomes
    /// <c>yield yield __await(…)</c>, a delegated yield wraps the operand in <c>__asyncDelegator(__asyncValues(…))</c>,
    /// and a <c>return await</c> becomes <c>return yield __await(yield __await(…))</c> (the reference's
    /// <c>visitYieldExpression</c> and <c>visitReturnStatement</c>).</remarks>
    [Fact]
    public void AsyncGenerator_YieldYieldDelegateAndReturnAwait_UseTheAsyncHelpers()
        => AssertEmitted(
            AwaitHelper + "\n" +
            AsyncValuesHelper + "\n" +
            AsyncDelegatorHelper + "\n" +
            AsyncGeneratorHelper + "\n" +
            """
function f() {
    return __asyncGenerator(this, arguments, function* f_1() {
        yield yield __await(1);
        yield __await(yield* __asyncDelegator(__asyncValues(g())));
        return yield __await(yield __await(h()));
    });
}
""",
            Emit("async function* f() {\n    yield 1;\n    yield* g();\n    return await h();\n}\n"));

    // ---------------------------------------------------------------------------------------------
    // es2018.ts:729-939 — for await…of
    // ---------------------------------------------------------------------------------------------

    /// <remarks>PORT-NOTE: <c>for await</c> lowers into a <c>for</c> over <c>__asyncValues(y)</c>'s iterators inside
    /// a <c>try/finally</c> that returns the iterator, with the loop steps driven by <c>yield __await(y_1.next())</c>
    /// (<c>transformForAwaitOfStatement</c>, <c>es2018.ts:827-939</c>).</remarks>
    [Fact]
    public void ForAwaitOf_LowersIntoATryFinallyOverTheAsyncIterator()
        => AssertEmitted(
            AsyncValuesHelper + "\n" +
            AwaitHelper + "\n" +
            AsyncGeneratorHelper + "\n" +
            """
function f() {
    return __asyncGenerator(this, arguments, function* f_1() {
        var _a, e_1, _b, _c;
        try {
            for (var _d = true, y_1 = __asyncValues(y), y_1_1; y_1_1 = yield __await(y_1.next()), _a = y_1_1.done, !_a; _d = true) {
                _c = y_1_1.value;
                _d = false;
                const x = _c;
            }
        }
        catch (e_1_1) { e_1 = { error: e_1_1 }; }
        finally {
            try {
                if (!_d && !_a && (_b = y_1.return)) yield __await(_b.call(y_1));
            }
            finally { if (e_1) throw e_1.error; }
        }
    });
}
""",
            Emit("async function* f() {\n    for await (const x of y) {\n    }\n}\n"));

    /// <remarks>PORT-NOTE: an outer label survives the lowering — <c>visitLabeledStatement</c> unwraps it and hands
    /// it to <c>visitForOfStatement</c>, whose <c>restoreEnclosingLabel</c> puts it back around the generated
    /// <c>for</c> (<c>es2018.ts:471-480</c>).</remarks>
    [Fact]
    public void LabeledForAwaitOf_RestoresTheLabelOnTheGeneratedFor()
        => AssertEmitted(
            AsyncValuesHelper + "\n" +
            AwaitHelper + "\n" +
            AsyncGeneratorHelper + "\n" +
            """
function f() {
    return __asyncGenerator(this, arguments, function* f_1() {
        var _a, e_1, _b, _c;
        try {
            lbl: for (var _d = true, y_1 = __asyncValues(y), y_1_1; y_1_1 = yield __await(y_1.next()), _a = y_1_1.done, !_a; _d = true) {
                _c = y_1_1.value;
                _d = false;
                const x = _c;
                break lbl;
            }
        }
        catch (e_1_1) { e_1 = { error: e_1_1 }; }
        finally {
            try {
                if (!_d && !_a && (_b = y_1.return)) yield __await(_b.call(y_1));
            }
            finally { if (e_1) throw e_1.error; }
        }
    });
}
""",
            Emit("async function* f() {\n    lbl: for await (const x of y) {\n        break lbl;\n    }\n}\n"));
}
