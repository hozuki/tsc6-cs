#nullable enable

namespace TypeScript.Compiler.Tests;

// ported from: src/compiler/transformers/esDecorators.ts:295-2533
/// <summary>
/// Acceptance for the standard ("ES") decorators transform (M7-B): the <c>__esDecorate</c>/<c>__runInitializers</c>
/// rewriting of class, method, accessor, field and private-member decorators, the class-<c>this</c> static block, and
/// the named-evaluation helper block an anonymous decorated class gets.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (the oracle): every expectation below was measured with the reference compiler itself —
/// <c>ts.transpileModule(source, { compilerOptions: { target: ES2022, module: ESNext, useDefineForClassFields: true,
/// experimentalDecorators: false, alwaysStrict: false } })</c> from the reference's own
/// <c>lib/typescript.js</c> (6.0.3). At those options the reference's own <c>getScriptTransformers</c> chain is
/// exactly <c>[transformTypeScript, transformESDecorators]</c>: <c>transformClassFields</c> is inert at ES2022 with
/// <c>useDefineForClassFields</c> true (<c>shouldTransformAnything</c> is false), <c>transformESNext</c> is off at
/// <c>ES2022</c> only when the target is at least <c>ESNext</c> — the transform still runs because the target is below
/// <c>ESNext</c> — and no ES2016+ transform is selected above ES2022.
/// </para>
/// <para>
/// PORT-NOTE (the chain): this fixture drives <c>TypeScriptTransform.TransformTypeScript</c> and then
/// <c>ESDecorators.TransformESDecorators</c> through <c>Emitter.EmitFiles</c>, the order the reference assembles them,
/// and compares the whole emitted file — helper preambles included, since the printer emits them.
/// </para>
/// </remarks>
public class ESDecoratorsTests
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

    /// <summary>Emits the test's text through <c>Emitter.EmitFiles</c> with the reference's two-transformer order.</summary>
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
                ScriptTransformers = [TypeScriptTransform.TransformTypeScript, ESDecorators.TransformESDecorators],
                DeclarationTransformers = [],
            },
            /*emitOnly*/ null,
            /*onlyBuildInfo*/ false,
            /*forceDtsEmit*/ null,
            /*skipBuildInfo*/ null);

        return emitted.Replace("\r\n", "\n");
    }

    /// <summary>Asserts the emitted text against a raw literal, normalizing the literal's line endings.</summary>
    /// <remarks>PORT-NOTE: as <c>ES2015TransformTests.AssertEmitted</c> — a multi-line raw literal does not carry the
    /// newline that follows its last line, while <see cref="Emit"/> answers the writer's text including it.</remarks>
    private static void AssertEmitted(string expected, string actual)
        => Assert.Equal(expected.ReplaceLineEndings("\n") + "\n", actual);

    // ---------------------------------------------------------------------------------------------
    // The three emit helpers these fixtures preamble (each copied from the reference's own output)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts — the <c>__esDecorate</c> helper text.</summary>
    private const string EsDecorateHelper =
"""
        var __esDecorate = (this && this.__esDecorate) || function (ctor, descriptorIn, decorators, contextIn, initializers, extraInitializers) {
            function accept(f) { if (f !== void 0 && typeof f !== "function") throw new TypeError("Function expected"); return f; }
            var kind = contextIn.kind, key = kind === "getter" ? "get" : kind === "setter" ? "set" : "value";
            var target = !descriptorIn && ctor ? contextIn["static"] ? ctor : ctor.prototype : null;
            var descriptor = descriptorIn || (target ? Object.getOwnPropertyDescriptor(target, contextIn.name) : {});
            var _, done = false;
            for (var i = decorators.length - 1; i >= 0; i--) {
                var context = {};
                for (var p in contextIn) context[p] = p === "access" ? {} : contextIn[p];
                for (var p in contextIn.access) context.access[p] = contextIn.access[p];
                context.addInitializer = function (f) { if (done) throw new TypeError("Cannot add initializers after decoration has completed"); extraInitializers.push(accept(f || null)); };
                var result = (0, decorators[i])(kind === "accessor" ? { get: descriptor.get, set: descriptor.set } : descriptor[key], context);
                if (kind === "accessor") {
                    if (result === void 0) continue;
                    if (result === null || typeof result !== "object") throw new TypeError("Object expected");
                    if (_ = accept(result.get)) descriptor.get = _;
                    if (_ = accept(result.set)) descriptor.set = _;
                    if (_ = accept(result.init)) initializers.unshift(_);
                }
                else if (_ = accept(result)) {
                    if (kind === "field") initializers.unshift(_);
                    else descriptor[key] = _;
                }
            }
            if (target) Object.defineProperty(target, contextIn.name, descriptor);
            done = true;
        };
        """;

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts — the <c>__runInitializers</c> helper text.</summary>
    private const string RunInitializersHelper =
"""
        var __runInitializers = (this && this.__runInitializers) || function (thisArg, initializers, value) {
            var useValue = arguments.length > 2;
            for (var i = 0; i < initializers.length; i++) {
                value = useValue ? initializers[i].call(thisArg, value) : initializers[i].call(thisArg);
            }
            return useValue ? value : void 0;
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
    // ClassAndMethodDecorators_RewriteToTheEsDecorateHelpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>The reference's own emit for the fixture below (measured with <c>ts.transpileModule</c>).</summary>
    [Fact]
    public void ClassAndMethodDecorators_RewriteToTheEsDecorateHelpers()
    {
        var source =
            """
            declare function dec(...a: any[]): any;
            @dec
            class C {
                @dec m() { return 1; }
            }
            """;

        AssertEmitted(
            RunInitializersHelper + "\n" +
            EsDecorateHelper + "\n" +
            """
            let C = (() => {
                let _classDecorators = [dec];
                let _classDescriptor;
                let _classExtraInitializers = [];
                let _classThis;
                let _instanceExtraInitializers = [];
                let _m_decorators;
                var C = class {
                    static { _classThis = this; }
                    static {
                        const _metadata = typeof Symbol === "function" && Symbol.metadata ? Object.create(null) : void 0;
                        _m_decorators = [dec];
                        __esDecorate(this, null, _m_decorators, { kind: "method", name: "m", static: false, private: false, access: { has: obj => "m" in obj, get: obj => obj.m }, metadata: _metadata }, null, _instanceExtraInitializers);
                        __esDecorate(null, _classDescriptor = { value: _classThis }, _classDecorators, { kind: "class", name: _classThis.name, metadata: _metadata }, null, _classExtraInitializers);
                        C = _classThis = _classDescriptor.value;
                        if (_metadata) Object.defineProperty(_classThis, Symbol.metadata, { enumerable: true, configurable: true, writable: true, value: _metadata });
                        __runInitializers(_classThis, _classExtraInitializers);
                    }
                    m() { return 1; }
                    constructor() {
                        __runInitializers(this, _instanceExtraInitializers);
                    }
                };
                return C = _classThis;
            })();
            """,
            Emit(source));
    }

    // ---------------------------------------------------------------------------------------------
    // FieldDecorators_UseInitializersAndExtraInitializers
    // ---------------------------------------------------------------------------------------------

    /// <summary>The reference's own emit for the fixture below (measured with <c>ts.transpileModule</c>).</summary>
    [Fact]
    public void FieldDecorators_UseInitializersAndExtraInitializers()
    {
        var source =
            """
            declare function dec(...a: any[]): any;
            @dec
            class C {
                @dec x = 1;
                @dec static y = 2;
            }
            """;

        AssertEmitted(
            EsDecorateHelper + "\n" +
            RunInitializersHelper + "\n" +
            """
            let C = (() => {
                let _classDecorators = [dec];
                let _classDescriptor;
                let _classExtraInitializers = [];
                let _classThis;
                let _static_y_decorators;
                let _static_y_initializers = [];
                let _static_y_extraInitializers = [];
                let _x_decorators;
                let _x_initializers = [];
                let _x_extraInitializers = [];
                var C = class {
                    static { _classThis = this; }
                    static {
                        const _metadata = typeof Symbol === "function" && Symbol.metadata ? Object.create(null) : void 0;
                        _x_decorators = [dec];
                        _static_y_decorators = [dec];
                        __esDecorate(null, null, _static_y_decorators, { kind: "field", name: "y", static: true, private: false, access: { has: obj => "y" in obj, get: obj => obj.y, set: (obj, value) => { obj.y = value; } }, metadata: _metadata }, _static_y_initializers, _static_y_extraInitializers);
                        __esDecorate(null, null, _x_decorators, { kind: "field", name: "x", static: false, private: false, access: { has: obj => "x" in obj, get: obj => obj.x, set: (obj, value) => { obj.x = value; } }, metadata: _metadata }, _x_initializers, _x_extraInitializers);
                        __esDecorate(null, _classDescriptor = { value: _classThis }, _classDecorators, { kind: "class", name: _classThis.name, metadata: _metadata }, null, _classExtraInitializers);
                        C = _classThis = _classDescriptor.value;
                        if (_metadata) Object.defineProperty(_classThis, Symbol.metadata, { enumerable: true, configurable: true, writable: true, value: _metadata });
                    }
                    x = __runInitializers(this, _x_initializers, 1);
                    static y = __runInitializers(_classThis, _static_y_initializers, 2);
                    constructor() {
                        __runInitializers(this, _x_extraInitializers);
                    }
                    static {
                        __runInitializers(_classThis, _static_y_extraInitializers);
                        __runInitializers(_classThis, _classExtraInitializers);
                    }
                };
                return C = _classThis;
            })();
            """,
            Emit(source));
    }

    // ---------------------------------------------------------------------------------------------
    // PrivateMethodDecorator_KeepsTheDescriptorAndSetFunctionName
    // ---------------------------------------------------------------------------------------------

    /// <summary>The reference's own emit for the fixture below (measured with <c>ts.transpileModule</c>).</summary>
    [Fact]
    public void PrivateMethodDecorator_KeepsTheDescriptorAndSetFunctionName()
    {
        var source =
            """
            declare function dec(...a: any[]): any;
            @dec
            class C {
                @dec #m() { return 1; }
            }
            """;

        AssertEmitted(
            RunInitializersHelper + "\n" +
            EsDecorateHelper + "\n" +
            SetFunctionNameHelper + "\n" +
            """
            let C = (() => {
                let _classDecorators = [dec];
                let _classDescriptor;
                let _classExtraInitializers = [];
                let _classThis;
                let _instanceExtraInitializers = [];
                let _private_m_decorators;
                let _private_m_descriptor;
                var C = class {
                    static { _classThis = this; }
                    static {
                        const _metadata = typeof Symbol === "function" && Symbol.metadata ? Object.create(null) : void 0;
                        _private_m_decorators = [dec];
                        __esDecorate(this, _private_m_descriptor = { value: __setFunctionName(function () { return 1; }, "#m") }, _private_m_decorators, { kind: "method", name: "#m", static: false, private: true, access: { has: obj => #m in obj, get: obj => obj.#m }, metadata: _metadata }, null, _instanceExtraInitializers);
                        __esDecorate(null, _classDescriptor = { value: _classThis }, _classDecorators, { kind: "class", name: _classThis.name, metadata: _metadata }, null, _classExtraInitializers);
                        C = _classThis = _classDescriptor.value;
                        if (_metadata) Object.defineProperty(_classThis, Symbol.metadata, { enumerable: true, configurable: true, writable: true, value: _metadata });
                        __runInitializers(_classThis, _classExtraInitializers);
                    }
                    get #m() { return _private_m_descriptor.value; }
                    constructor() {
                        __runInitializers(this, _instanceExtraInitializers);
                    }
                };
                return C = _classThis;
            })();
            """,
            Emit(source));
    }

    // ---------------------------------------------------------------------------------------------
    // AnonymousDefaultExport_GetsTheNamedEvaluationHelperBlock
    // ---------------------------------------------------------------------------------------------

    /// <summary>The reference's own emit for the fixture below (measured with <c>ts.transpileModule</c>).</summary>
    [Fact]
    public void AnonymousDefaultExport_GetsTheNamedEvaluationHelperBlock()
    {
        var source =
            """
            declare function dec(...a: any[]): any;
            @dec
            export default class { }
            """;

        AssertEmitted(
            EsDecorateHelper + "\n" +
            RunInitializersHelper + "\n" +
            SetFunctionNameHelper + "\n" +
            """
            export default (() => {
                let _classDecorators = [dec];
                let _classDescriptor;
                let _classExtraInitializers = [];
                let _classThis;
                var default_1 = class {
                    static { _classThis = this; }
                    static { __setFunctionName(_classThis, "default"); }
                    static {
                        const _metadata = typeof Symbol === "function" && Symbol.metadata ? Object.create(null) : void 0;
                        __esDecorate(null, _classDescriptor = { value: _classThis }, _classDecorators, { kind: "class", name: _classThis.name, metadata: _metadata }, null, _classExtraInitializers);
                        default_1 = _classThis = _classDescriptor.value;
                        if (_metadata) Object.defineProperty(_classThis, Symbol.metadata, { enumerable: true, configurable: true, writable: true, value: _metadata });
                        __runInitializers(_classThis, _classExtraInitializers);
                    }
                };
                return default_1 = _classThis;
            })();
            """,
            Emit(source));
    }

    // ---------------------------------------------------------------------------------------------
    // SelfReferencingClass_InitializesClassThisInsideTheStaticBlock
    // ---------------------------------------------------------------------------------------------

    /// <summary>The reference's own emit for the fixture below (measured with <c>ts.transpileModule</c>).</summary>
    [Fact]
    public void SelfReferencingClass_InitializesClassThisInsideTheStaticBlock()
    {
        var source =
            """
            declare function dec(...a: any[]): any;
            @dec
            class C {
                static x = C;
            }
            """;

        AssertEmitted(
            EsDecorateHelper + "\n" +
            RunInitializersHelper + "\n" +
            """
            let C = (() => {
                let _classDecorators = [dec];
                let _classDescriptor;
                let _classExtraInitializers = [];
                let _classThis;
                var C = class {
                    static { _classThis = this; }
                    static {
                        const _metadata = typeof Symbol === "function" && Symbol.metadata ? Object.create(null) : void 0;
                        __esDecorate(null, _classDescriptor = { value: _classThis }, _classDecorators, { kind: "class", name: _classThis.name, metadata: _metadata }, null, _classExtraInitializers);
                        C = _classThis = _classDescriptor.value;
                        if (_metadata) Object.defineProperty(_classThis, Symbol.metadata, { enumerable: true, configurable: true, writable: true, value: _metadata });
                    }
                    static x = C;
                    static {
                        __runInitializers(_classThis, _classExtraInitializers);
                    }
                };
                return C = _classThis;
            })();
            """,
            Emit(source));
    }

    // ---------------------------------------------------------------------------------------------
    // AccessorDecorators_UseGetterAndSetterContexts
    // ---------------------------------------------------------------------------------------------

    /// <summary>The reference's own emit for the fixture below (measured with <c>ts.transpileModule</c>).</summary>
    [Fact]
    public void AccessorDecorators_UseGetterAndSetterContexts()
    {
        var source =
            """
            declare function dec(...a: any[]): any;
            class C {
                @dec get x() { return 1; }
                @dec set x(v) { }
            }
            """;

        AssertEmitted(
            RunInitializersHelper + "\n" +
            EsDecorateHelper + "\n" +
            """
            let C = (() => {
                let _instanceExtraInitializers = [];
                let _get_x_decorators;
                let _set_x_decorators;
                return class C {
                    static {
                        const _metadata = typeof Symbol === "function" && Symbol.metadata ? Object.create(null) : void 0;
                        _get_x_decorators = [dec];
                        _set_x_decorators = [dec];
                        __esDecorate(this, null, _get_x_decorators, { kind: "getter", name: "x", static: false, private: false, access: { has: obj => "x" in obj, get: obj => obj.x }, metadata: _metadata }, null, _instanceExtraInitializers);
                        __esDecorate(this, null, _set_x_decorators, { kind: "setter", name: "x", static: false, private: false, access: { has: obj => "x" in obj, set: (obj, value) => { obj.x = value; } }, metadata: _metadata }, null, _instanceExtraInitializers);
                        if (_metadata) Object.defineProperty(this, Symbol.metadata, { enumerable: true, configurable: true, writable: true, value: _metadata });
                    }
                    get x() { return 1; }
                    set x(v) { }
                    constructor() {
                        __runInitializers(this, _instanceExtraInitializers);
                    }
                };
            })();
            """,
            Emit(source));
    }

    // ---------------------------------------------------------------------------------------------
    // ComputedMethodName_IsQuotedInTheContext
    // ---------------------------------------------------------------------------------------------

    /// <summary>The reference's own emit for the fixture below (measured with <c>ts.transpileModule</c>).</summary>
    [Fact]
    public void ComputedMethodName_IsQuotedInTheContext()
    {
        var source =
            """
            declare function dec(...a: any[]): any;
            class C {
                @dec ["m"]() { return 1; }
            }
            """;

        AssertEmitted(
            RunInitializersHelper + "\n" +
            EsDecorateHelper + "\n" +
            """
            let C = (() => {
                let _instanceExtraInitializers = [];
                let _member_decorators;
                return class C {
                    static {
                        const _metadata = typeof Symbol === "function" && Symbol.metadata ? Object.create(null) : void 0;
                        _member_decorators = [dec];
                        __esDecorate(this, null, _member_decorators, { kind: "method", name: "m", static: false, private: false, access: { has: obj => "m" in obj, get: obj => obj["m"] }, metadata: _metadata }, null, _instanceExtraInitializers);
                        if (_metadata) Object.defineProperty(this, Symbol.metadata, { enumerable: true, configurable: true, writable: true, value: _metadata });
                    }
                    ["m"]() { return 1; }
                    constructor() {
                        __runInitializers(this, _instanceExtraInitializers);
                    }
                };
            })();
            """,
            Emit(source));
    }

}
