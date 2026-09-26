namespace TypeScript.Compiler.Tests;

// ported from: src/compiler/transformers/es2015.ts:488-5066
/// <summary>
/// Acceptance for the ES2015 downlevel transform (M7-B): the class, arrow, destructuring, template, spread,
/// <c>for&hellip;of</c>, computed-property and block-scope rewrites it performs, all measured against the reference's
/// own emit.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (the oracle): every expectation below was measured with
/// <c>ts.transpileModule(source, { compilerOptions, reportDiagnostics: false })</c> from the reference's own
/// <c>lib/typescript.js</c> (6.0.3) at the fixture's own options — <c>target: ES5</c>,
/// <c>module: ESNext</c>, <c>alwaysStrict: false</c> and <c>downlevelIteration</c> where the case names it. The
/// <c>target: ES5</c> setting is what makes the reference's chain include <c>transformES2015</c> at all (higher targets
/// gate it off), which is also why the transform's own module stores no target check.
/// </para>
/// <para>
/// PORT-NOTE (the chain): this fixture drives <c>TypeScriptTransform.TransformTypeScript</c> and then
/// <c>ES2015Transform.TransformES2015</c>, the order the reference's <c>getTransformers</c> assembles them. The ts
/// transform is not incidental to these tests: a class with a static property initializer is wrapped in the
/// <c>TypeScriptClassWrapper</c> IIFE by the ts transform and unwrapped by this transform's
/// <c>visitTypeScriptClassWrapper</c> (es2015.ts:4343-4520), so dropping it would test a state the reference never
/// reaches.
/// </para>
/// <para>
/// PORT-NOTE (<c>alwaysStrict: false</c>): as in <c>TypeScriptTransformTests</c>, the prologue is disabled so each
/// expectation names only the downlevel rewrite under test.
/// </para>
/// </remarks>
public class ES2015TransformTests
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
        ScriptTarget target,
        bool downlevelIteration)
    {
        var options = new FakeCompilerOptions
        {
            SuppressOutputPathCheck = true,
            StrictNullChecks = true,
            AlwaysStrict = false,
            Target = target,
            Module = ModuleKind.ESNext,
            DownlevelIteration = downlevelIteration,
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

    /// <summary>Emits the test's text through <c>Emitter.EmitFiles</c> with the reference's two-transformer order.</summary>
    private static string Emit(string text, bool downlevelIteration = false)
    {
        var (program, root, _, resolver) = CreateProgram(text, ScriptTarget.ES5, downlevelIteration);

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
                ScriptTransformers = [TypeScriptTransform.TransformTypeScript, ES2015Transform.TransformES2015],
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

    // ---------------------------------------------------------------------------------------------
    // The emit helpers the fixtures below reuse (each one copied from the reference's own output)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts — the <c>__extends</c> helper text.</summary>
    private const string ExtendsHelper =
        """
var __extends = (this && this.__extends) || (function () {
    var extendStatics = function (d, b) {
        extendStatics = Object.setPrototypeOf ||
            ({ __proto__: [] } instanceof Array && function (d, b) { d.__proto__ = b; }) ||
            function (d, b) { for (var p in b) if (Object.prototype.hasOwnProperty.call(b, p)) d[p] = b[p]; };
        return extendStatics(d, b);
    };
    return function (d, b) {
        if (typeof b !== "function" && b !== null)
            throw new TypeError("Class extends value " + String(b) + " is not a constructor or null");
        extendStatics(d, b);
        function __() { this.constructor = d; }
        d.prototype = b === null ? Object.create(b) : (__.prototype = b.prototype, new __());
    };
})();
""";

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts — the <c>__spreadArray</c> helper text.</summary>
    private const string SpreadArrayHelper =
        """
var __spreadArray = (this && this.__spreadArray) || function (to, from, pack) {
    if (pack || arguments.length === 2) for (var i = 0, l = from.length, ar; i < l; i++) {
        if (ar || !(i in from)) {
            if (!ar) ar = Array.prototype.slice.call(from, 0, i);
            ar[i] = from[i];
        }
    }
    return to.concat(ar || Array.prototype.slice.call(from));
};
""";

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts — the <c>__read</c> helper text.</summary>
    private const string ReadHelper =
        """
var __read = (this && this.__read) || function (o, n) {
    var m = typeof Symbol === "function" && o[Symbol.iterator];
    if (!m) return o;
    var i = m.call(o), r, ar = [], e;
    try {
        while ((n === void 0 || n-- > 0) && !(r = i.next()).done) ar.push(r.value);
    }
    catch (error) { e = { error: error }; }
    finally {
        try {
            if (r && !r.done && (m = i["return"])) m.call(i);
        }
        finally { if (e) throw e.error; }
    }
    return ar;
};
""";

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts — the <c>__values</c> helper text.</summary>
    private const string ValuesHelper =
        """
var __values = (this && this.__values) || function(o) {
    var s = typeof Symbol === "function" && Symbol.iterator, m = s && o[s], i = 0;
    if (m) return m.call(o);
    if (o && typeof o.length === "number") return {
        next: function () {
            if (o && i >= o.length) o = void 0;
            return { value: o && o[i++], done: !o };
        }
    };
    throw new TypeError(s ? "Object is not iterable." : "Symbol.iterator is not defined.");
};
""";

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts — the <c>__makeTemplateObject</c> helper text.</summary>
    private const string MakeTemplateObjectHelper =
        """
var __makeTemplateObject = (this && this.__makeTemplateObject) || function (cooked, raw) {
    if (Object.defineProperty) { Object.defineProperty(cooked, "raw", { value: raw }); } else { cooked.raw = raw; }
    return cooked;
};
""";

    // ---------------------------------------------------------------------------------------------
    // es2015.ts:952-1125 — class declarations and expressions
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ClassDeclaration_BecomesAnIife()
        => AssertEmitted(
            """
var C = /** @class */ (function () {
    function C() {
    }
    C.prototype.m = function () { return 1; };
    return C;
}());
""",
            Emit("class C { m() { return 1; } }"));

    [Fact]
    public void ClassExpression_BecomesAnIife()
        => AssertEmitted(
            """
var C = /** @class */ (function () {
    function C() {
    }
    C.prototype.m = function () { return 1; };
    return C;
}());
""",
            Emit("var C = class { m() { return 1; } };"));

    // ---------------------------------------------------------------------------------------------
    // REMOVED (M7-B) — the static-initializer fixture belongs to the transformClassFields batch (M7-B5)
    // ---------------------------------------------------------------------------------------------
    //
    // `Emit("class C { static x = 1; }")` (expecting `C.x = 1;` in the class iife) was deleted here rather than
    // re-pinned. In the reference `transformClassFields` runs unconditionally before `transformES2015`
    // (transformer.ts:155, ahead of the `languageVersion < ScriptTarget.ES2015` push at transformer.ts:182), so
    // es2015 never sees a class body that still holds a property declaration — which is why its `addClassMembers`
    // default arm is `Debug.failBadSyntaxKind(member, …)` (es2015.ts:2279-2281) and the port's `AddClassMembers` fails
    // the same way. The fixture must be measured through the real chain,
    // `[TypeScriptTransform.TransformTypeScript, TransformClassFields, ES2015Transform.TransformES2015]`, in M7-B5 — a
    // two-transformer fixture asserts an emit the reference cannot produce.

    /// <remarks>PORT-NOTE: <c>enumerable: false</c> in the descriptor is the reference's own reading of a class
    /// accessor — it emits <c>getAccessor || setAccessor ? createFalse() : createTrue()</c> (es2015.ts:2390-2393), so
    /// the descriptor is non-enumerable for a get/set pair and for a lone accessor alike (measured at the fixtures'
    /// options).</remarks>
    [Fact]
    public void ClassWithAccessorsAndAStaticMethod_UsesDefinePropertyAndAMemberAssignment()
        => AssertEmitted(
            """
var C = /** @class */ (function () {
    function C() {
    }
    Object.defineProperty(C.prototype, "x", {
        get: function () { return 1; },
        set: function (v) { },
        enumerable: false,
        configurable: true
    });
    C.s = function () { };
    return C;
}());
""",
            Emit("class C { get x() { return 1; } set x(v) { } static s() { } }"));

    /// <remarks>PORT-NOTE: a lone accessor takes the same <c>Object.defineProperty</c> path — the fixture that caught
    /// the clone-flag defect, since es2015 wraps the receiver in <c>setParent(setTextRange(factory.cloneNode(receiver),
    /// receiver), receiver.parent)</c> (es2015.ts:2354-2360) and a clone that lost <c>NodeFlags.Synthesized</c> was
    /// handed to the checker as if it were a parse-tree node.</remarks>
    [Fact]
    public void GetAccessorOnly_BecomesADefinePropertyCall()
        => AssertEmitted(
            """
var C = /** @class */ (function () {
    function C() {
    }
    Object.defineProperty(C.prototype, "x", {
        get: function () { return 1; },
        enumerable: false,
        configurable: true
    });
    return C;
}());
""",
            Emit("class C { get x() { return 1; } }"));

    [Fact]
    public void SetAccessorOnly_BecomesADefinePropertyCall()
        => AssertEmitted(
            """
var C = /** @class */ (function () {
    function C() {
    }
    Object.defineProperty(C.prototype, "x", {
        set: function (v) { },
        enumerable: false,
        configurable: true
    });
    return C;
}());
""",
            Emit("class C { set x(v) { } }"));

    [Fact]
    public void DerivedClassWithAConstructor_EmitsExtendsAndTheSuperCall()
        => AssertEmitted(
            ExtendsHelper + "\n" +
            """
var D = /** @class */ (function () {
    function D() {
    }
    return D;
}());
var C = /** @class */ (function (_super) {
    __extends(C, _super);
    function C() {
        return _super.call(this) || this;
    }
    C.prototype.m = function () { return 1; };
    return C;
}(D));
""",
            Emit("class D { }\nclass C extends D { constructor() { super(); } m() { return 1; } }"));

    [Fact]
    public void DerivedClassWithoutAConstructor_EmitsTheImplicitSuperCall()
        => AssertEmitted(
            ExtendsHelper + "\n" +
            """
var D = /** @class */ (function () {
    function D() {
    }
    return D;
}());
var C = /** @class */ (function (_super) {
    __extends(C, _super);
    function C() {
        return _super !== null && _super.apply(this, arguments) || this;
    }
    C.prototype.m = function () { return 1; };
    return C;
}(D));
""",
            Emit("class D { }\nclass C extends D { m() { return 1; } }"));

    [Fact]
    public void SuperMethodCall_BecomesAPrototypeCall()
        => AssertEmitted(
            ExtendsHelper + "\n" +
            """
var C = /** @class */ (function (_super) {
    __extends(C, _super);
    function C() {
        return _super !== null && _super.apply(this, arguments) || this;
    }
    C.prototype.m = function () { _super.prototype.m.call(this); };
    return C;
}(D));
""",
            Emit("class C extends D { m() { super.m(); } }"));

    [Fact]
    public void SuperPropertyAccess_BecomesAPrototypeAccess()
        => AssertEmitted(
            ExtendsHelper + "\n" +
            """
var C = /** @class */ (function (_super) {
    __extends(C, _super);
    function C() {
        return _super !== null && _super.apply(this, arguments) || this;
    }
    C.prototype.m = function () { return _super.prototype.x; };
    return C;
}(D));
""",
            Emit("class C extends D { m() { return super.x; } }"));

    [Fact]
    public void ArrowFunction_CapturesLexicalThis()
        => AssertEmitted(
            """
var C = /** @class */ (function () {
    function C() {
    }
    C.prototype.m = function () {
        var _this = this;
        var f = function () { return _this.x; };
        return f();
    };
    return C;
}());
""",
            Emit("class C { m() { var f = () => this.x; return f(); } }"));

    // ---------------------------------------------------------------------------------------------
    // es2015.ts:3310-3373, :4092-4193 — computed property names
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ComputedPropertyName_IntroducesATemporary()
        => AssertEmitted(
            """
var _a;
var o = (_a = { a: 1 }, _a[k] = 2, _a.b = 3, _a);
""",
            Emit("var o = { a: 1, [k]: 2, b: 3 };"));

    [Fact]
    public void ComputedAccessor_UsesDefinePropertyOnTheTemporary()
        => AssertEmitted(
            """
var _a;
var o = (_a = { a: 1 }, _a[k] = function () { return 1; }, _a[j] = 2, Object.defineProperty(_a, g, {
    get: function () { return 3; },
    enumerable: false,
    configurable: true
}), _a);
""",
            Emit("var o = { a: 1, [k]() { return 1; }, [j]: 2, get [g]() { return 3; } };"));

    // ---------------------------------------------------------------------------------------------
    // es2015.ts:4755-4821, :4788-4802 — template and tagged-template literals
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void TemplateLiteral_BecomesConcatCalls()
        => AssertEmitted(
            """
var s = "a".concat(b, "c").concat(d);
""",
            Emit("var s = `a${b}c${d}`;"));

    [Fact]
    public void TaggedTemplate_EmitsTheMakeTemplateObjectHelper()
        => AssertEmitted(
            MakeTemplateObjectHelper + "\n" +
            """
var s = tag(__makeTemplateObject(["a", "c"], ["a", "c"]), b);
""",
            Emit("var s = tag`a${b}c`;"));

    // ---------------------------------------------------------------------------------------------
    // es2015.ts:4308-4744 — spread elements
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void SpreadInAnArrayLiteral_UsesSpreadArray()
        => AssertEmitted(
            SpreadArrayHelper + "\n" +
            """
var a = __spreadArray(__spreadArray([1], b, true), [2], false);
""",
            Emit("var a = [1, ...b, 2];"));

    [Fact]
    public void SpreadOfAPackedArrayLiteral_IsNotPacked()
        => AssertEmitted(
            SpreadArrayHelper + "\n" +
            """
var a = __spreadArray(__spreadArray([], [1, 2], false), [3], false);
""",
            Emit("var a = [...[1, 2], 3];"));

    [Fact]
    public void NestedSpread_KeepsThePackingFlags()
        => AssertEmitted(
            SpreadArrayHelper + "\n" +
            """
var a = __spreadArray(__spreadArray([], b, true), c, true);
""",
            Emit("var a = [...b, ...c];"));

    [Fact]
    public void SpreadInAnArgumentList_BecomesApply()
        => AssertEmitted(
            SpreadArrayHelper + "\n" +
            """
f.apply(void 0, __spreadArray(__spreadArray([1], b, false), [2], false));
""",
            Emit("f(1, ...b, 2);"));

    [Fact]
    public void SpreadInANewExpression_BecomesBindApply()
        => AssertEmitted(
            SpreadArrayHelper + "\n" +
            """
var x = new (C.bind.apply(C, __spreadArray([void 0], b, false)))();
""",
            Emit("var x = new C(...b);"));

    [Fact]
    public void Spread_WithDownlevelIteration_ReadsTheIterable()
        => AssertEmitted(
            ReadHelper + "\n" +
            SpreadArrayHelper + "\n" +
            """
var a = __spreadArray(__spreadArray([1], __read(b), false), [2], false);
""",
            Emit("var a = [1, ...b, 2];", downlevelIteration: true));

    // ---------------------------------------------------------------------------------------------
    // es2015.ts:3011-3310 — for-of
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ForOf_OverAnArray_BecomesTheIndexLoop()
        => AssertEmitted(
            """
for (var _i = 0, arr_1 = arr; _i < arr_1.length; _i++) {
    var v = arr_1[_i];
    f(v);
}
""",
            Emit("for (var v of arr) { f(v); }"));

    [Fact]
    public void ForOf_WithDownlevelIteration_EmitsTheIteratorLoop()
        => AssertEmitted(
            ValuesHelper + "\n" +
            """
var e_1, _a;
try {
    for (var arr_1 = __values(arr), arr_1_1 = arr_1.next(); !arr_1_1.done; arr_1_1 = arr_1.next()) {
        var v = arr_1_1.value;
        f(v);
    }
}
catch (e_1_1) { e_1 = { error: e_1_1 }; }
finally {
    try {
        if (arr_1_1 && !arr_1_1.done && (_a = arr_1.return)) _a.call(arr_1);
    }
    finally { if (e_1) throw e_1.error; }
}
""",
            Emit("for (var v of arr) { f(v); }", downlevelIteration: true));

    // ---------------------------------------------------------------------------------------------
    // es2015.ts:3424-3911 — the converted-loop rewrite
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ForOf_WithACapturedBinding_MovesTheBodyIntoALoopFunction()
        => AssertEmitted(
            """
var _loop_1 = function (v) {
    f(function () { return v; });
};
for (var _i = 0, arr_1 = arr; _i < arr_1.length; _i++) {
    var v = arr_1[_i];
    _loop_1(v);
}
""",
            Emit("for (let v of arr) { f(() => v); }"));

    /// <remarks>PORT-NOTE: the converted loop is nested in the iterable's own loop, so the loop function is hoisted
    /// between the <c>__values</c> temporaries and the <c>try</c> — the reference's own placement.</remarks>
    [Fact]
    public void ForOf_WithDownlevelIterationAndACapturedBinding()
        => AssertEmitted(
            ValuesHelper + "\n" +
            """
var e_1, _a;
var _loop_1 = function (v) {
    f(function () { return v; });
};
try {
    for (var arr_1 = __values(arr), arr_1_1 = arr_1.next(); !arr_1_1.done; arr_1_1 = arr_1.next()) {
        var v = arr_1_1.value;
        _loop_1(v);
    }
}
catch (e_1_1) { e_1 = { error: e_1_1 }; }
finally {
    try {
        if (arr_1_1 && !arr_1_1.done && (_a = arr_1.return)) _a.call(arr_1);
    }
    finally { if (e_1) throw e_1.error; }
}
""",
            Emit("for (let v of arr) { f(() => v); }", downlevelIteration: true));

    [Fact]
    public void ForLet_WithACapturedBinding_MovesTheBodyIntoALoopFunction()
        => AssertEmitted(
            """
var _loop_1 = function (i) {
    f(function () { return i; });
};
for (var i = 0; i < 10; i++) {
    _loop_1(i);
}
""",
            Emit("for (let i = 0; i < 10; i++) { f(() => i); }"));

    [Fact]
    public void DoWhile_WithACapturedBinding_MovesTheBodyIntoALoopFunction()
        => AssertEmitted(
            """
var _loop_1 = function () {
    var x = f();
    g(function () { return x; });
};
do {
    _loop_1();
} while (a);
""",
            Emit("do { let x = f(); g(() => x); } while (a);"));

    /// <remarks>PORT-NOTE: <c>x</c> is renamed to <c>x_1</c> by the block-scoped-binding substitution
    /// (es2015.ts:4977-4986) because the checker recorded a colliding name; <c>y</c> is not captured and keeps its
    /// name. The block itself is not converted — only the binding that a closure captures is renamed.</remarks>
    [Fact]
    public void BlockScopedBinding_CapturedByAClosure_IsRenamed()
        => AssertEmitted(
            """
{
    var x_1 = 1;
    f(function () { return x_1; });
    var y = 2;
}
""",
            Emit("{ let x = 1; f(() => x); let y = 2; }"));

    // ---------------------------------------------------------------------------------------------
    // es2015.ts:1820-1844, :2177-2252, :4830-4853 — this, new.target and super
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void NewTarget_BecomesTheConstructCheck()
        => AssertEmitted(
            """
function f() {
    var _newTarget = this && this instanceof f ? this.constructor : void 0;
    return _newTarget;
}
""",
            Emit("function f() { return new.target; }"));

    // ---------------------------------------------------------------------------------------------
    // es2015.ts:1846-2149, :2935-2933 — parameters, defaults, rest and destructuring
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void DefaultParametersAndRest_AreMovedIntoTheBody()
        => AssertEmitted(
            """
function f(a, _a) {
    if (a === void 0) { a = 1; }
    var _b = _a === void 0 ? {} : _a, b = _b.b;
    var rest = [];
    for (var _i = 2; _i < arguments.length; _i++) {
        rest[_i - 2] = arguments[_i];
    }
    return a;
}
""",
            Emit("function f(a = 1, { b } = {}, ...rest) { return a; }"));

    [Fact]
    public void ArrowFunctionWithADefault_BecomesAFunctionExpression()
        => AssertEmitted(
            """
var f = function (a) {
    if (a === void 0) { a = 1; }
    return a;
};
""",
            Emit("var f = (a = 1) => a;"));

    [Fact]
    public void DestructuringBinding_BecomesIndividualDeclarations()
        => AssertEmitted(
            """
var a = obj.a, b = obj.b;
var c = arr[0], d = arr[1];
""",
            Emit("var { a, b } = obj;\nvar [c, d] = arr;"));

    [Fact]
    public void DestructuringAssignment_BecomesASequence()
        => AssertEmitted(
            """
var a, b;
(a = obj.a, b = obj.b);
a = arr[0], b = arr[1];
""",
            Emit("var a, b;\n({ a, b } = obj);\n[a, b] = arr;"));

    [Fact]
    public void CatchClauseWithABindingPattern_DestructuresAtTheTopOfTheBody()
        => AssertEmitted(
            """
try { }
catch (_a) {
    var message = _a.message;
    f(message);
}
""",
            Emit("try { } catch ({ message }) { f(message); }"));
}
