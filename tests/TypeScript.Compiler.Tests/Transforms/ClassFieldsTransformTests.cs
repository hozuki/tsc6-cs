#nullable enable

namespace TypeScript.Compiler.Tests;

// ported from: src/compiler/transformers/classFields.ts:230-3360 and src/compiler/transformers/namedEvaluation.ts:1-489
/// <summary>
/// Acceptance for the class-field transform (M7-B): the property-declaration rewrites (Set and Define semantics), the
/// <c>this</c>/<c>super</c> handling a static initializer needs once it leaves the class body, the private-element and
/// auto-accessor lowering, and the named-evaluation helper block a class expression with transformable statics needs.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (the oracle): every expectation below was measured with
/// <c>ts.transpileModule(source, { compilerOptions, reportDiagnostics: true })</c> from the reference's own
/// <c>lib/typescript.js</c> (6.0.3) at the fixture's own options — <c>module: ESNext</c>, <c>alwaysStrict: false</c>,
/// <c>strictNullChecks: true</c> and the <c>target</c>/<c>useDefineForClassFields</c> each case names. The
/// <c>lib/typescript.js</c> emit emits the deprecation warnings for <c>alwaysStrict</c>/<c>target: ES5</c>, which the
/// harnes&rsquo; own options text does not carry; they are diagnostics, not emit.
/// </para>
/// <para>
/// PORT-NOTE (the chain): the reference runs <c>transformClassFields</c> unconditionally and immediately before
/// <c>transformES2015</c> (transformer.ts:161-166), so a class-field fixture is only faithful through
/// <c>[TransformTypeScript, TransformClassFields]</c> at a target whose language version is already ES2015, and through
/// <c>[&hellip;, TransformES2015]</c> at ES5. `tests/Transforms/ES2015TransformTests.cs` records the fixture that moved
/// here: at ES5 a static property initializer is lifted into a member assignment by <em>this</em> transform, and es2015
/// never sees a property declaration at all (<c>addClassMembers</c>'s default arm is a debug failure,
/// es2015.ts:2279-2281).
/// </para>
/// <para>
/// PORT-NOTE (<c>alwaysStrict: false</c>): as in the sibling fixtures, the prologue is disabled so each expectation
/// names only the downlevel rewrite under test.
/// </para>
/// </remarks>
public class ClassFieldsTransformTests
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
    private static (Program Program, SourceFile Root, IEmitResolver Resolver) CreateProgram(
        string text,
        ScriptTarget target,
        bool? useDefineForClassFields)
    {
        var options = new FakeCompilerOptions
        {
            SuppressOutputPathCheck = true,
            StrictNullChecks = true,
            AlwaysStrict = false,
            Target = target,
            Module = ModuleKind.ESNext,
            UseDefineForClassFields = useDefineForClassFields,
        };
        var system = new FakeSystem();
        system.Files["/a.ts"] = text;
        var host = Program.CreateCompilerHostWorker(options, /*setParentNodes*/ true, system);
        system.Files[host.GetDefaultLibFileName(options)] = string.Join("\n", StubLib) + "\n";
        var program = Program.CreateProgram(["/a.ts"], options, host);
        var root = program.GetSourceFiles().Single(file => (file.FileName ?? "").EndsWith("a.ts", StringComparison.Ordinal));
        var resolver = program.GetTypeChecker().GetEmitResolver(root, /*cancellationToken*/ null, /*forceDts*/ false);
        return (program, root, resolver);
    }

    /// <summary>Emits the test's text through <c>Emitter.EmitFiles</c> with the reference's transformer order.</summary>
    /// <remarks>PORT-NOTE: <c>transformES2015</c> is pushed only when the reference's own gate (<c>languageVersion &lt;
    /// ScriptTarget.ES2015</c>, transformer.ts:182) would push it.</remarks>
    private static string Emit(string text, ScriptTarget target, bool? useDefineForClassFields = null)
    {
        var (program, root, resolver) = CreateProgram(text, target, useDefineForClassFields);

        List<Func<ITransformerContext, Func<object?, object?>>> transformers =
        [
            TypeScriptTransform.TransformTypeScript,
            ClassFieldsTransform.TransformClassFields,
        ];
        if (target < ScriptTarget.ES2015)
        {
            transformers.Add(ES2015Transform.TransformES2015);
        }

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
                ScriptTransformers = transformers,
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

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts — the <c>__classPrivateFieldGet</c> helper text.</summary>
    private const string ClassPrivateFieldGetHelper =
        """
var __classPrivateFieldGet = (this && this.__classPrivateFieldGet) || function (receiver, state, kind, f) {
    if (kind === "a" && !f) throw new TypeError("Private accessor was defined without a getter");
    if (typeof state === "function" ? receiver !== state || !f : !state.has(receiver)) throw new TypeError("Cannot read private member from an object whose class did not declare it");
    return kind === "m" ? f : kind === "a" ? f.call(receiver) : f ? f.value : state.get(receiver);
};
""";

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts — the <c>__classPrivateFieldIn</c> helper text.</summary>
    private const string ClassPrivateFieldInHelper =
        """
var __classPrivateFieldIn = (this && this.__classPrivateFieldIn) || function(state, receiver) {
    if (receiver === null || (typeof receiver !== "object" && typeof receiver !== "function")) throw new TypeError("Cannot use 'in' operator on non-object");
    return typeof state === "function" ? receiver === state : state.has(receiver);
};
""";

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts — the <c>__classPrivateFieldSet</c> helper text.</summary>
    private const string ClassPrivateFieldSetHelper =
        """
var __classPrivateFieldSet = (this && this.__classPrivateFieldSet) || function (receiver, state, value, kind, f) {
    if (kind === "m") throw new TypeError("Private method is not writable");
    if (kind === "a" && !f) throw new TypeError("Private accessor was defined without a setter");
    if (typeof state === "function" ? receiver !== state || !f : !state.has(receiver)) throw new TypeError("Cannot write private member to an object whose class did not declare it");
    return (kind === "a" ? f.call(receiver, value) : f ? f.value = value : state.set(receiver, value)), value;
};
""";

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts — the <c>__setFunctionName</c> helper text.</summary>
    private const string SetFunctionNameHelper =
        """
var __setFunctionName = (this && this.__setFunctionName) || function (f, name, prefix) {
    if (typeof name === "symbol") name = name.description ? "[".concat(name.description, "]") : "";
    return Object.defineProperty(f, "name", { configurable: true, value: prefix ? "".concat(prefix, " ", name) : name });
};
""";

    // ---------------------------------------------------------------------------------------------
    // classFields.ts — the property declarations the ES2015 transform can no longer see
    // ---------------------------------------------------------------------------------------------

    /// <remarks>PORT-NOTE: the fixture <c>ES2015TransformTests.ClassWithAccessorsAndAStaticMethod&hellip;</c>'s
    /// neighbours record as REMOVED (M7-B) — the reference lifts the static initializer into <c>C.x = 1</c> inside the
    /// class IIFE through the class-field transform, which runs ahead of es2015, so the two-transformer chain cannot
    /// produce it.</remarks>
    [Fact]
    public void StaticPropertyInitializer_BecomesAStaticAssignment()
        => AssertEmitted(
            """
var C = /** @class */ (function () {
    function C() {
    }
    C.x = 1;
    return C;
}());
""",
            Emit("class C { static x = 1; }", ScriptTarget.ES5));

    [Fact]
    public void InstancePropertyInitializer_BecomesAConstructorAssignment()
        => AssertEmitted(
            """
var C = /** @class */ (function () {
    function C() {
        this.x = 1;
    }
    return C;
}());
""",
            Emit("class C { x = 1; }", ScriptTarget.ES5));

    [Fact]
    public void InstancePropertyInitializer_DefineSemanticsUsesADescriptor()
        => AssertEmitted(
            """
class C {
    constructor() {
        Object.defineProperty(this, "x", {
            enumerable: true,
            configurable: true,
            writable: true,
            value: 1
        });
    }
}
""",
            Emit("class C { x = 1; }", ScriptTarget.ES2015, useDefineForClassFields: true));

    [Fact]
    public void InstancePropertyInitializer_SetSemanticsUsesAnAssignment()
        => AssertEmitted(
            """
class C {
    constructor() {
        this.x = 1;
    }
}
""",
            Emit("class C { x = 1; }", ScriptTarget.ES2015, useDefineForClassFields: false));

    [Fact]
    public void InstancePropertyInitializer_IsLeftAloneAtESNext()
        => AssertEmitted(
            """
class C {
    x = 1;
}
""",
            Emit("class C { x = 1; }", ScriptTarget.ESNext, useDefineForClassFields: true));

    /// <remarks>PORT-NOTE: the static field's computed name is neither inlineable nor already cached, so
    /// <c>getPropertyNameExpressionIfNeeded</c> hoists one name for the member and the pending expression that assigns
    /// it is written as a statement after the class (<c>_a = y; C[_a] = 1;</c>).</remarks>
    [Fact]
    public void ComputedStaticPropertyName_IsCachedInAHoistedVariable()
        => AssertEmitted(
            """
var _a;
class C {
    [x]() { }
}
_a = y;
C[_a] = 1;
""",
            Emit("class C { [x]() { } static [y] = 1; }", ScriptTarget.ES2015, useDefineForClassFields: false));

    [Fact]
    public void DerivedClass_MovesInitializersAfterTheSuperCall()
        => AssertEmitted(
            """
class D {
}
class C extends D {
    constructor() {
        super();
        this.x = 1;
        this.y = 2;
    }
}
""",
            Emit("class D {}\nclass C extends D { x = 1; constructor() { super(); this.y = 2; } }", ScriptTarget.ES2015, useDefineForClassFields: false));

    /// <remarks>PORT-NOTE: a <c>static {}</c> block under a target below ES2022 leaves the class body as an
    /// immediately-invoked arrow function whose <c>this</c> is the captured class reference.</remarks>
    [Fact]
    public void StaticBlock_BecomesAnImmediatelyInvokedArrowFunction()
        => AssertEmitted(
            """
var _a;
class C {
}
_a = C;
(() => {
    _a.x = 1;
})();
""",
            Emit("class C { static { this.x = 1; } }", ScriptTarget.ES2015, useDefineForClassFields: false));

    // ---------------------------------------------------------------------------------------------
    // classFields.ts — `this`/`super` substitution in static initializers
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void StaticThisInitializer_IsSubstitutedWithTheClassReference()
        => AssertEmitted(
            """
var _a;
class C {
}
_a = C;
C.x = _a;
""",
            Emit("class C { static x = this; }", ScriptTarget.ES2015, useDefineForClassFields: false));

    [Fact]
    public void StaticSuperCall_IsSubstitutedWithAReflectGet()
        => AssertEmitted(
            """
var _a, _b;
class D {
    static f() { return 1; }
}
class C extends (_b = D) {
}
_a = C;
C.x = Reflect.get(_b, "f", _a).call(_a);
""",
            Emit("class D { static f() { return 1; } }\nclass C extends D { static x = super.f(); }", ScriptTarget.ES2015, useDefineForClassFields: false));

    /// <remarks>PORT-NOTE: an assignment to a <c>super</c> property inside a static initializer becomes the three-argument
    /// <c>Reflect.set</c> — the receiver is the class reference the static block's <c>this</c> was substituted with.</remarks>
    [Fact]
    public void StaticSuperAssignment_UsesReflectSet()
        => AssertEmitted(
            """
var _a, _b;
class D {
}
D.f = 1;
class C extends (_b = D) {
}
_a = C;
(() => {
    Reflect.set(_b, "f", 2, _a);
})();
""",
            Emit("class D { static f = 1; }\nclass C extends D { static { super.f = 2; } }", ScriptTarget.ES2015, useDefineForClassFields: false));

    /// <remarks>PORT-NOTE: at ES5 <c>shouldTransformSuperInStaticInitializers</c> is false
    /// (<c>languageVersion &gt;= ScriptTarget.ES2015</c> is required, classFields.ts:391-392), so the reference does not
    /// rewrite <c>super.f</c> itself: the static initializer is lifted into the class IIFE, where the es2015 transform's
    /// own <c>super</c> rewriting reaches it, and the <c>this</c> substitution still answers the class reference.</remarks>
    [Fact]
    public void DerivedClassAtES5_LiftsTheStaticInitializerIntoTheClassIife()
        => AssertEmitted(
            ExtendsHelper + "\n" +
            """
var D = /** @class */ (function () {
    function D() {
    }
    D.f = function () { return 1; };
    return D;
}());
var C = /** @class */ (function (_super) {
    __extends(C, _super);
    function C() {
        return _super !== null && _super.apply(this, arguments) || this;
    }
    var _a;
    _a = C;
    C.x = _super.f.call(_a);
    return C;
}(D));
""",
            Emit("class D { static f() { return 1; } }\nclass C extends D { static x = super.f(); }", ScriptTarget.ES5));

    // ---------------------------------------------------------------------------------------------
    // classFields.ts — private elements and auto-accessors
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void PrivateField_BecomesAWeakMapEntry()
        => AssertEmitted(
            ClassPrivateFieldGetHelper + "\n" +
            """
var _C_x;
class C {
    constructor() {
        _C_x.set(this, 1);
    }
    m() { return __classPrivateFieldGet(this, _C_x, "f"); }
}
_C_x = new WeakMap();
""",
            Emit("class C { #x = 1; m() { return this.#x; } }", ScriptTarget.ES2015, useDefineForClassFields: false));

    [Fact]
    public void PrivateMethod_BecomesAWeakSetMember()
        => AssertEmitted(
            ClassPrivateFieldGetHelper + "\n" +
            """
var _C_instances, _C_m;
class C {
    constructor() {
        _C_instances.add(this);
    }
    n() { return __classPrivateFieldGet(this, _C_instances, "m", _C_m).call(this); }
}
_C_instances = new WeakSet(), _C_m = function _C_m() { return 1; };
""",
            Emit("class C { #m() { return 1; } n() { return this.#m(); } }", ScriptTarget.ES2015, useDefineForClassFields: false));

    /// <remarks>PORT-NOTE: the implicit backing field is named from the property name
    /// (<c>#x_accessor_storage</c>), which is what <c>transformAutoAccessor</c>'s storage-name step and the second
    /// <c>transformFieldInitializer</c> pass both key on.</remarks>
    [Fact]
    public void AutoAccessor_BecomesABackingFieldWithGetAndSetRedirectors()
        => AssertEmitted(
            ClassPrivateFieldGetHelper + "\n" + ClassPrivateFieldSetHelper + "\n" +
            """
var _C_x_accessor_storage;
class C {
    constructor() {
        _C_x_accessor_storage.set(this, 1);
    }
    get x() { return __classPrivateFieldGet(this, _C_x_accessor_storage, "f"); }
    set x(value) { __classPrivateFieldSet(this, _C_x_accessor_storage, value, "f"); }
}
_C_x_accessor_storage = new WeakMap();
""",
            Emit("class C { accessor x = 1; }", ScriptTarget.ES2015, useDefineForClassFields: false));

    /// <remarks>PORT-NOTE: <c>#x in o</c> is the one private-name expression that does not read or write the field, so it
    /// answers the brand-check helper rather than a get/set pair.</remarks>
    [Fact]
    public void PrivateIdentifierInExpression_UsesTheBrandCheckHelper()
        => AssertEmitted(
            ClassPrivateFieldInHelper + "\n" +
            """
var _C_x;
class C {
    constructor() {
        _C_x.set(this, void 0);
    }
    m(o) { return __classPrivateFieldIn(_C_x, o); }
}
_C_x = new WeakMap();
""",
            Emit("class C { #x; m(o: any) { return #x in o; } }", ScriptTarget.ES2015, useDefineForClassFields: false));

    /// <remarks>PORT-NOTE: the compound read-modify-write path runs the receiver through
    /// <c>createCopiableReceiverExpr</c> and <c>createPrivateIdentifierAssignment</c>'s
    /// <c>isCompoundAssignment</c> arm; the postfix form's extra temp is what the trailing comma expression carries.</remarks>
    [Fact]
    public void PrivateCompoundAssignment_UsesTheAccessAndSetHelpers()
        => AssertEmitted(
            ClassPrivateFieldGetHelper + "\n" + ClassPrivateFieldSetHelper + "\n" +
            """
var _C_x;
class C {
    constructor() {
        _C_x.set(this, 1);
    }
    m() { var _a, _b; __classPrivateFieldSet(this, _C_x, __classPrivateFieldGet(this, _C_x, "f") + 1, "f"); return __classPrivateFieldSet(this, _C_x, (_b = __classPrivateFieldGet(this, _C_x, "f"), _a = _b++, _b), "f"), _a; }
}
_C_x = new WeakMap();
""",
            Emit("class C { #x = 1; m() { this.#x += 1; return this.#x++; } }", ScriptTarget.ES2015, useDefineForClassFields: false));

    /// <remarks>PORT-NOTE: a private name used as a *destructuring target* is rewritten through
    /// <c>wrapPrivateIdentifierForDestructuringTarget</c> (classFields.ts:2998) and its
    /// <c>createAssignmentTargetWrapper</c> (factory/nodeFactory.ts:6725-6745), whose setter body is a block built with
    /// **no** <c>multiLine</c> argument. `shouldEmitBlockFunctionBodyOnSingleLine` (emitter.ts:3449) bails out on a
    /// truthy <c>multiLine</c>, so passing <c>true</c> here printed the wrapper's <c>set value(_b) { … }</c> across
    /// three lines instead of inline — a whitespace-only `.js` difference the unit ring did not cover and
    /// <c>tools/BaselineDiff</c> caught on seven conformance fixtures.</remarks>
    [Fact]
    public void PrivateNameDestructuringTarget_EmitsTheSetterWrapperInline()
        => AssertEmitted(
            ClassPrivateFieldSetHelper + "\n" +
            """
var _C_p;
class C {
    constructor() {
        _C_p.set(this, 1);
    }
    m() { var _a; (_a = this, { x: ({ set value(_b) { __classPrivateFieldSet(_a, _C_p, _b, "f"); } }).value } = { x: 1 }); }
}
_C_p = new WeakMap();
""",
            Emit("class C { #p = 1; m() { ({ x: this.#p } = { x: 1 }); } }\n", ScriptTarget.ES2015, useDefineForClassFields: false));

    // ---------------------------------------------------------------------------------------------
    // namedEvaluation.ts — the assigned-name helper block a class expression needs
    // ---------------------------------------------------------------------------------------------

    /// <remarks>PORT-NOTE: <c>ClassHasExplicitlyAssignedName</c> is false for this class (it has no
    /// <c>static {}</c> block and no <c>emitNode.assignedName</c>), the class expression is anonymous and its single
    /// member is an initialized property, so <c>isAnonymousClassNeedingAssignedName</c> answers true and
    /// <c>finishTransformNamedEvaluation</c> injects the <c>__setFunctionName</c> helper block — the whole chain
    /// <c>visitVariableDeclaration</c> &rarr; <c>isNamedEvaluation</c> &rarr; <c>transformNamedEvaluation</c>.</remarks>
    [Fact]
    public void AnonymousClassExpressionWithAStaticField_TakesTheAssignedName()
        => AssertEmitted(
            SetFunctionNameHelper + "\n" +
            """
var _a;
let C = (_a = class {
    },
    __setFunctionName(_a, "C"),
    _a.x = 1,
    _a);
""",
            Emit("let C = class { static x = 1; };", ScriptTarget.ES2015, useDefineForClassFields: false));

    /// <remarks>PORT-NOTE: a class expression with no transformable statics needs no helper block, so
    /// <c>isAnonymousClassNeedingAssignedName</c> answers false and the expression is left exactly as written.</remarks>
    [Fact]
    public void AnonymousClassExpressionWithoutStatics_IsLeftAlone()
        => AssertEmitted(
            """
let C = class {
    m() { return 1; }
};
""",
            Emit("let C = class { m() { return 1; } };", ScriptTarget.ES2015, useDefineForClassFields: false));
}
