namespace TypeScript.Compiler.Tests;

// ported from: src/compiler/transformers/module/{module.ts,system.ts,esnextAnd2015.ts,impliedNodeFormatDependent.ts}
/// <summary>
/// Acceptance for the module transforms (M7-B): the CommonJS/AMD/UMD rewrites of <c>module.ts</c>, the ES-module
/// rewrites of <c>esnextAnd2015.ts</c> (driven through <c>impliedNodeFormatDependent.ts</c>, the transformer the
/// reference's <c>getModuleTransformer</c> selects for CommonJS/ESNext/ES2020/&hellip;), and the <c>System.register</c>
/// wrapper of <c>system.ts</c>.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (the oracle): every expectation below was measured with
/// <c>ts.transpileModule(source, { compilerOptions, reportDiagnostics: false })</c> from the reference's own
/// <c>lib/typescript.js</c> (6.0.3) at each fixture's own options &mdash; <c>target: ES2015</c>, <c>module</c> as the
/// case's name says, <c>alwaysStrict: false</c>, and <c>esModuleInterop: true</c> where the case names it. The
/// measurement is exact (every case ends in <c>AssertEmitted</c>), including the emit-helper texts the
/// <c>__importStar</c>/<c>__importDefault</c>/<c>__exportStar</c>/<c>__createBinding</c> cases pull in.
/// </para>
/// <para>
/// PORT-NOTE (the chain): each fixture drives <c>TypeScriptTransform.TransformTypeScript</c> and then the module
/// transformer <c>getModuleTransformer</c> answers for that <c>module</c> kind (transformer.ts:78-116):
/// <c>transformSystemModule</c> for System, <c>transformModule</c> for AMD/UMD (its <c>default</c> arm) and
/// <c>transformImpliedNodeFormatDependentModule</c> for CommonJS/ESNext &mdash; which itself selects
/// <c>transformModule</c>'s or <c>transformECMAScriptModule</c>'s hooks per file from
/// <c>getEmitModuleFormatOfFile</c>. The expectations were measured through exactly that selection, so the two
/// dispatcher files are under test here too (<c>transpileModule</c> makes the same choice).
/// </para>
/// <para>
/// PORT-NOTE (why only these two transformers): with <c>target: ES2015</c> the reference's chain applies
/// <c>transformTypeScript</c>, the class-field/es2015-and-below transforms (all gated off at this target except
/// <c>transformClassFields</c>, which has nothing to do in these fixtures) and then the module transformer, so a
/// two-transformer fixture is the reference's own chain for this input.
/// </para>
/// </remarks>
public class ModuleTransformTests
{
    /// <summary>The smallest library that lets <c>initializeTypeChecker</c> resolve the globals it asks for.</summary>
    private static readonly string[] StubLib =
    [
        "interface Array<T> { }",
        "interface Object { }",
        "interface Function { }",
        "declare var undefined: undefined;",
        "declare var console: any;",
    ];

    /// <summary>Builds the program the test's text is checked in and asks its checker for the emit resolver.</summary>
    private static (Program Program, SourceFile Root, IEmitResolver Resolver) CreateProgram(string text, ModuleKind module, bool? esModuleInterop)
    {
        var options = new FakeCompilerOptions
        {
            SuppressOutputPathCheck = true,
            StrictNullChecks = true,
            AlwaysStrict = false,
            Target = ScriptTarget.ES2015,
            Module = module,
            EsModuleInterop = esModuleInterop,   // null = the reference's default (true), which is what transpileModule measured: the option is unset there
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

    /// <summary>Emits the test's text through <c>Emitter.EmitFiles</c> with the reference's own transformer selection.</summary>
    private static string Emit(string text, ModuleKind module, bool? esModuleInterop = null)
    {
        var (program, root, resolver) = CreateProgram(text, module, esModuleInterop);

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
                ScriptTransformers = [TypeScriptTransform.TransformTypeScript, SelectModuleTransformer(module)],
                DeclarationTransformers = [],
            },
            /*emitOnly*/ null,
            /*onlyBuildInfo*/ false,
            /*forceDtsEmit*/ null,
            /*skipBuildInfo*/ null);

        return emitted.Replace("\r\n", "\n");
    }

    /// <summary>ported from: src/compiler/transformer.ts:78-116 &mdash; <c>getModuleTransformer</c>'s selection.</summary>
    /// <remarks>PORT-NOTE: the fixtures go through <see cref="ImpliedNodeFormatDependent.GetModuleTransformer"/> itself
    /// (the port's copy of the reference's selector), so the three-way choice between the CommonJS/AMD/UMD transform,
    /// the per-file implied-format chooser and the <c>System.register</c> wrapper is under test alongside the
    /// transforms it picks.</remarks>
    private static Func<ITransformerContext, Func<object?, object?>> SelectModuleTransformer(ModuleKind module)
        => ImpliedNodeFormatDependent.GetModuleTransformer(module);

    /// <summary>Asserts the emitted text against a raw literal, normalizing the literal's line endings.</summary>
    /// <remarks>PORT-NOTE: as in <c>ES2015TransformTests</c> &mdash; a multi-line raw literal does not carry the newline
    /// that would follow its last line, while <see cref="Emit"/> answers the writer's text including it.</remarks>
    private static void AssertEmitted(string expected, string actual)
        => Assert.Equal(expected.ReplaceLineEndings("\n") + "\n", actual);

    // fixture: export const x = 1;
    /// <summary>Measured from <c>ts.transpileModule</c> with <c>{"module":"CommonJS","target":"ES2015","alwaysStrict":false,"esModuleInterop":false}</c>.</summary>
    [Fact]
    public void CjsExportConst()
        => AssertEmitted(
            """
"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.x = void 0;
exports.x = 1;
""",
            Emit("export const x = 1;\n", ModuleKind.CommonJS));

    // fixture: import d, { a, b as c } from "./m";\nexport const y = d + a + c;
    /// <summary>Measured from <c>ts.transpileModule</c> with <c>{"module":"CommonJS","target":"ES2015","alwaysStrict":false,"esModuleInterop":false}</c>.</summary>
    [Fact]
    public void CjsImportMixed()
        => AssertEmitted(
            """
"use strict";
var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
Object.defineProperty(exports, "__esModule", { value: true });
exports.y = void 0;
const m_1 = __importStar(require("./m"));
exports.y = m_1.default + m_1.a + m_1.b;
""",
            Emit("import d, { a, b as c } from \"./m\";\nexport const y = d + a + c;\n", ModuleKind.CommonJS));

    // fixture: export default function f() { return 1; }
    /// <summary>Measured from <c>ts.transpileModule</c> with <c>{"module":"CommonJS","target":"ES2015","alwaysStrict":false,"esModuleInterop":false}</c>.</summary>
    [Fact]
    public void CjsExportDefaultFunction()
        => AssertEmitted(
            """
"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.default = f;
function f() { return 1; }
""",
            Emit("export default function f() { return 1; }\n", ModuleKind.CommonJS));

    // fixture: import * as ns from "./m";\nexport { ns };
    /// <summary>Measured from <c>ts.transpileModule</c> with <c>{"module":"CommonJS","target":"ES2015","alwaysStrict":false,"esModuleInterop":false}</c>.</summary>
    [Fact]
    public void CjsNamespaceReexport()
        => AssertEmitted(
            """
"use strict";
var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
Object.defineProperty(exports, "__esModule", { value: true });
exports.ns = void 0;
const ns = __importStar(require("./m"));
exports.ns = ns;
""",
            Emit("import * as ns from \"./m\";\nexport { ns };\n", ModuleKind.CommonJS));

    // fixture: export { a, b } from "./m";
    /// <summary>Measured from <c>ts.transpileModule</c> with <c>{"module":"CommonJS","target":"ES2015","alwaysStrict":false,"esModuleInterop":false}</c>.</summary>
    [Fact]
    public void CjsNamedReexport()
        => AssertEmitted(
            """
"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.b = exports.a = void 0;
var m_1 = require("./m");
Object.defineProperty(exports, "a", { enumerable: true, get: function () { return m_1.a; } });
Object.defineProperty(exports, "b", { enumerable: true, get: function () { return m_1.b; } });
""",
            Emit("export { a, b } from \"./m\";\n", ModuleKind.CommonJS));

    // fixture: export * from "./m";
    /// <summary>Measured from <c>ts.transpileModule</c> with <c>{"module":"CommonJS","target":"ES2015","alwaysStrict":false,"esModuleInterop":false}</c>.</summary>
    [Fact]
    public void CjsExportStar()
        => AssertEmitted(
            """
"use strict";
var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __exportStar = (this && this.__exportStar) || function(m, exports) {
    for (var p in m) if (p !== "default" && !Object.prototype.hasOwnProperty.call(exports, p)) __createBinding(exports, m, p);
};
Object.defineProperty(exports, "__esModule", { value: true });
__exportStar(require("./m"), exports);
""",
            Emit("export * from \"./m\";\n", ModuleKind.CommonJS));

    // fixture: import x = require("./m");\nexport { x };
    /// <summary>Measured from <c>ts.transpileModule</c> with <c>{"module":"CommonJS","target":"ES2015","alwaysStrict":false,"esModuleInterop":false}</c>.</summary>
    [Fact]
    public void CjsImportEquals()
        => AssertEmitted(
            """
"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.x = void 0;
const x = require("./m");
exports.x = x;
""",
            Emit("import x = require(\"./m\");\nexport { x };\n", ModuleKind.CommonJS));

    // fixture: const q = 1;\nexport = q;
    /// <summary>Measured from <c>ts.transpileModule</c> with <c>{"module":"CommonJS","target":"ES2015","alwaysStrict":false,"esModuleInterop":false}</c>.</summary>
    [Fact]
    public void CjsExportEquals()
        => AssertEmitted(
            """
"use strict";
const q = 1;
module.exports = q;
""",
            Emit("const q = 1;\nexport = q;\n", ModuleKind.CommonJS));

    // fixture: import { a } from "./m";\nconsole.log(a);
    /// <summary>Measured from <c>ts.transpileModule</c> with <c>{"module":"CommonJS","target":"ES2015","alwaysStrict":false,"esModuleInterop":false}</c>.</summary>
    [Fact]
    public void CjsImportSubstitution()
        => AssertEmitted(
            """
"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
const m_1 = require("./m");
console.log(m_1.a);
""",
            Emit("import { a } from \"./m\";\nconsole.log(a);\n", ModuleKind.CommonJS));

    // fixture: import d from "./m";\nconsole.log(d);
    /// <summary>Measured from <c>ts.transpileModule</c> with <c>{"module":"CommonJS","target":"ES2015","alwaysStrict":false,"esModuleInterop":true}</c>.</summary>
    [Fact]
    public void CjsEsModuleInteropDefault()
        => AssertEmitted(
            """
"use strict";
var __importDefault = (this && this.__importDefault) || function (mod) {
    return (mod && mod.__esModule) ? mod : { "default": mod };
};
Object.defineProperty(exports, "__esModule", { value: true });
const m_1 = __importDefault(require("./m"));
console.log(m_1.default);
""",
            Emit("import d from \"./m\";\nconsole.log(d);\n", ModuleKind.CommonJS, esModuleInterop: true));

    // fixture: export let n = 0;\nn++;\nn += 2;
    /// <summary>Measured from <c>ts.transpileModule</c> with <c>{"module":"CommonJS","target":"ES2015","alwaysStrict":false,"esModuleInterop":false}</c>.</summary>
    [Fact]
    public void CjsExportUpdate()
        => AssertEmitted(
            """
"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.n = void 0;
exports.n = 0;
exports.n++;
exports.n += 2;
""",
            Emit("export let n = 0;\nn++;\nn += 2;\n", ModuleKind.CommonJS));

    // fixture: import("./m").then(m => console.log(m));
    /// <summary>Measured from <c>ts.transpileModule</c> with <c>{"module":"CommonJS","target":"ES2015","alwaysStrict":false,"esModuleInterop":false}</c>.</summary>
    [Fact]
    public void CjsDynamicImport()
        => AssertEmitted(
            """
var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
Promise.resolve().then(() => __importStar(require("./m"))).then(m => console.log(m));
""",
            Emit("import(\"./m\").then(m => console.log(m));\n", ModuleKind.CommonJS));

    // fixture: export const { p, q: [r] } = { p: 1, q: [2] };
    /// <summary>Measured from <c>ts.transpileModule</c> with <c>{"module":"CommonJS","target":"ES2015","alwaysStrict":false,"esModuleInterop":false}</c>.</summary>
    [Fact]
    public void CjsExportDestructure()
        => AssertEmitted(
            """
"use strict";
var _a;
Object.defineProperty(exports, "__esModule", { value: true });
exports.r = exports.p = void 0;
_a = { p: 1, q: [2] }, exports.p = _a.p, exports.r = _a.q[0];
""",
            Emit("export const { p, q: [r] } = { p: 1, q: [2] };\n", ModuleKind.CommonJS));

    // fixture: import { a } from "./m";\nexport const o = { a };
    /// <summary>Measured from <c>ts.transpileModule</c> with <c>{"module":"CommonJS","target":"ES2015","alwaysStrict":false,"esModuleInterop":false}</c>.</summary>
    [Fact]
    public void CjsShorthand()
        => AssertEmitted(
            """
"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.o = void 0;
const m_1 = require("./m");
exports.o = { a: m_1.a };
""",
            Emit("import { a } from \"./m\";\nexport const o = { a };\n", ModuleKind.CommonJS));

    // fixture: export const x = 1;
    /// <summary>Measured from <c>ts.transpileModule</c> with <c>{"module":"AMD","target":"ES2015","alwaysStrict":false,"esModuleInterop":false}</c>.</summary>
    [Fact]
    public void AmdExportConst()
        => AssertEmitted(
            """
define(["require", "exports"], function (require, exports) {
    "use strict";
    Object.defineProperty(exports, "__esModule", { value: true });
    exports.x = void 0;
    exports.x = 1;
});
""",
            Emit("export const x = 1;\n", ModuleKind.AMD));

    // fixture: import d from "./m";\nexport const y = d;
    /// <summary>Measured from <c>ts.transpileModule</c> with <c>{"module":"AMD","target":"ES2015","alwaysStrict":false,"esModuleInterop":false}</c>.</summary>
    [Fact]
    public void AmdImportDefault()
        => AssertEmitted(
            """
var __importDefault = (this && this.__importDefault) || function (mod) {
    return (mod && mod.__esModule) ? mod : { "default": mod };
};
define(["require", "exports", "./m"], function (require, exports, m_1) {
    "use strict";
    Object.defineProperty(exports, "__esModule", { value: true });
    exports.y = void 0;
    m_1 = __importDefault(m_1);
    exports.y = m_1.default;
});
""",
            Emit("import d from \"./m\";\nexport const y = d;\n", ModuleKind.AMD));

    // fixture: export const x = 1;
    /// <summary>Measured from <c>ts.transpileModule</c> with <c>{"module":"UMD","target":"ES2015","alwaysStrict":false,"esModuleInterop":false}</c>.</summary>
    [Fact]
    public void UmdExportConst()
        => AssertEmitted(
            """
(function (factory) {
    if (typeof module === "object" && typeof module.exports === "object") {
        var v = factory(require, exports);
        if (v !== undefined) module.exports = v;
    }
    else if (typeof define === "function" && define.amd) {
        define(["require", "exports"], factory);
    }
})(function (require, exports) {
    "use strict";
    Object.defineProperty(exports, "__esModule", { value: true });
    exports.x = void 0;
    exports.x = 1;
});
""",
            Emit("export const x = 1;\n", ModuleKind.UMD));

    // fixture: export { a } from "./m";
    /// <summary>Measured from <c>ts.transpileModule</c> with <c>{"module":"UMD","target":"ES2015","alwaysStrict":false,"esModuleInterop":false}</c>.</summary>
    [Fact]
    public void UmdNamedReexport()
        => AssertEmitted(
            """
(function (factory) {
    if (typeof module === "object" && typeof module.exports === "object") {
        var v = factory(require, exports);
        if (v !== undefined) module.exports = v;
    }
    else if (typeof define === "function" && define.amd) {
        define(["require", "exports", "./m"], factory);
    }
})(function (require, exports) {
    "use strict";
    Object.defineProperty(exports, "__esModule", { value: true });
    exports.a = void 0;
    var m_1 = require("./m");
    Object.defineProperty(exports, "a", { enumerable: true, get: function () { return m_1.a; } });
});
""",
            Emit("export { a } from \"./m\";\n", ModuleKind.UMD));

    // fixture: export const x = 1;
    /// <summary>Measured from <c>ts.transpileModule</c> with <c>{"module":"System","target":"ES2015","alwaysStrict":false,"esModuleInterop":false}</c>.</summary>
    [Fact]
    public void SystemExportConst()
        => AssertEmitted(
            """
System.register([], function (exports_1, context_1) {
    "use strict";
    var x;
    var __moduleName = context_1 && context_1.id;
    return {
        setters: [],
        execute: function () {
            exports_1("x", x = 1);
        }
    };
});
""",
            Emit("export const x = 1;\n", ModuleKind.System));

    // fixture: import { a } from "./m";\nexport function f() { return a; }
    /// <summary>Measured from <c>ts.transpileModule</c> with <c>{"module":"System","target":"ES2015","alwaysStrict":false,"esModuleInterop":false}</c>.</summary>
    [Fact]
    public void SystemImportExport()
        => AssertEmitted(
            """
System.register(["./m"], function (exports_1, context_1) {
    "use strict";
    var m_1;
    var __moduleName = context_1 && context_1.id;
    function f() { return m_1.a; }
    exports_1("f", f);
    return {
        setters: [
            function (m_1_1) {
                m_1 = m_1_1;
            }
        ],
        execute: function () {
        }
    };
});
""",
            Emit("import { a } from \"./m\";\nexport function f() { return a; }\n", ModuleKind.System));

    // fixture: export const x = 1;
    /// <summary>Measured from <c>ts.transpileModule</c> with <c>{"module":"ESNext","target":"ES2015","alwaysStrict":false,"esModuleInterop":false}</c>.</summary>
    [Fact]
    public void EsNextExportConst()
        => AssertEmitted(
            """
export const x = 1;
""",
            Emit("export const x = 1;\n", ModuleKind.ESNext));

    // fixture: export * as ns from "./m";
    /// <summary>Measured from <c>ts.transpileModule</c> with <c>{"module":"ESNext","target":"ES2015","alwaysStrict":false,"esModuleInterop":false}</c>.</summary>
    [Fact]
    public void EsNextNamespaceReexport()
        => AssertEmitted(
            """
export * as ns from "./m";
""",
            Emit("export * as ns from \"./m\";\n", ModuleKind.ESNext));

    // fixture: export * as default from "./m";
    /// <summary>Measured from <c>ts.transpileModule</c> with <c>{"module":"ESNext","target":"ES2015","alwaysStrict":false,"esModuleInterop":false}</c>.</summary>
    [Fact]
    public void EsNextDefaultReexport()
        => AssertEmitted(
            """
export * as default from "./m";
""",
            Emit("export * as default from \"./m\";\n", ModuleKind.ESNext));

}
