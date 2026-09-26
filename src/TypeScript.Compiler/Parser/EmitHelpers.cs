namespace TypeScript.Compiler;

// ported from: src/compiler/factory/emitHelpers.ts:1-1491 (PrivateIdentifierKind, already generated from :42-46)
/// <summary>
/// The emit-helper factory and the helper descriptors it hands out: the <c>__awaiter</c>-style runtime preludes a
/// down-leveling transformer pulls into a file, plus the call expressions that reference them.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS builds the factory as the object literal returned from <c>createEmitHelperFactory(context)</c> — a set
/// of closures over the transformation context — so the port is a class holding the context, the same treatment
/// <see cref="ParenthesizerRules"/> got (docs/architecture.md §5 rule 6, §15.9). The context is the three-member slice
/// <see cref="IEmitHelperFactoryContext"/> (the emitter-side context itself is M7's), and the factory implements the
/// generated <see cref="IEmitHelperFactory"/>, which is what <c>ITransformationContext.getEmitHelperFactory()</c>
/// answers.
/// <para>
/// PORT-NOTE: the descriptors at the bottom of the file are the reference's module-level <c>const</c>s. C# has no
/// module scope, and <c>factory/emitHelpers.ts</c> becomes one file/one static class here, so they are fields of
/// <see cref="EmitHelpers"/> — as in the reference, two of them
/// (<see cref="EmitHelpers.AsyncSuperHelper"/>, <see cref="EmitHelpers.AdvancedAsyncSuperHelper"/>) are also used by
/// transformers outside this file. Their text is reproduced with the leading newline and indentation the reference's
/// template literals carry: that whitespace is part of the value the emitter writes, not formatting.
/// </para>
/// </remarks>
internal static class EmitHelpers
{
    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:148-151 — <c>createEmitHelperFactory</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's function is the factory (it ends with <c>return { … }</c> over the whole file), so the
    /// port's entry point is just the constructor call; the two <c>memoize</c>d constants TS builds before the return
    /// are built by that constructor, once per factory, the same way.
    /// </remarks>
    public static EmitHelperFactory CreateEmitHelperFactory(IEmitHelperFactoryContext context) => new(context);

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:703-710 — <c>compareEmitHelpers</c>.</summary>
    /// <remarks>
    /// This is the sort order the emitter uses for the helpers on one node: a helper with no priority is emitted before
    /// every helper that has one, and otherwise the priorities compare numerically.
    /// </remarks>
    public static Comparison CompareEmitHelpers(EmitHelper x, EmitHelper y)
    {
        if (ReferenceEquals(x, y)) return Comparison.EqualTo;
        if (x.Priority == y.Priority) return Comparison.EqualTo;
        if (x.Priority is null) return Comparison.GreaterThan;
        if (y.Priority is null) return Comparison.LessThan;
        return Core.CompareValues(x.Priority, y.Priority);
    }

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:1485-1491 — <c>isCallToHelper</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>helperName</c> is a <c>__String</c> and compares against <c>escapedText</c>, so callers pass
    /// the escaped spelling (the three-underscore form the leading-underscore escape produces); the port's
    /// <c>escapedText</c> is a plain <c>string</c> (§5 rule 1) and the comparison is the same one.
    /// </remarks>
    public static bool IsCallToHelper(Expression firstSegment, string helperName)
    {
        if (!NodeTests.IsCallExpression(firstSegment)) return false;

        var call = (CallExpression)firstSegment;
        if (call.Expression is not Identifier identifier) return false;

        return (Utilities.GetEmitFlags(identifier) & EmitFlags.HelperName) != 0
            && identifier.EscapedText == helperName;
    }

    // ported from: src/compiler/factory/emitHelpers.ts:712-727 — helperString
    /// <summary>
    /// Builds the text of a helper that has to refer to another helper by its file-level unique name.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>helperString</c> is a template-literal tag — it receives the literal's chunks as a
    /// <c>TemplateStringsArray</c> and the interpolated expressions as separate arguments, and returns
    /// <c>(uniqueName) =&gt; string</c>. C# has no tag functions, so the same thing is spelled out: the literal's chunks
    /// (one more than the interpolations) and the names to interpolate between them. The loop, the concatenation order
    /// and the trailing chunk are the reference's.
    /// </remarks>
    private static Func<EmitHelperUniqueNameCallback, string> HelperString(string[] chunks, params string[] args)
        => uniqueName =>
        {
            var result = "";
            for (var i = 0; i < args.Length; i++)
            {
                result += chunks[i];
                result += uniqueName(args[i]);
            }

            result += chunks[chunks.Length - 1];
            return result;
        };

    // ported from: src/compiler/factory/emitHelpers.ts:1467-1472 — asyncSuperHelper/advancedAsyncSuperHelper
    /// <remarks>
    /// PORT-NOTE: both texts are built with <c>helperString</c> (:716-727) and interpolate <c>_superIndex</c> — the
    /// only interpolation either template has — so each call spells its literal's two chunks out around it. The text is
    /// the delegate-typed form of <see cref="EmitHelper.Text"/>.
    /// </remarks>
    public static readonly EmitHelper AsyncSuperHelper = new(
        name: "typescript:async-super",
        scoped: true,
        text: HelperString(["\n            const ", " = name => super[name];"], "_superIndex"));

    /// <inheritdoc cref="AsyncSuperHelper"/>
    public static readonly EmitHelper AdvancedAsyncSuperHelper = new(
        name: "typescript:advanced-async-super",
        scoped: true,
        text: HelperString(
        [
            "\n            const ",
            @" = (function (geti, seti) {
                const cache = Object.create(null);
                return name => cache[name] || (cache[name] = { get value() { return geti(name); }, set value(v) { seti(name, v); } });
            })(name => super[name], (name, value) => super[name] = value);",
        ],
        "_superIndex"));

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:730-742 — <c>decorateHelper</c>.</summary>
    public static readonly EmitHelper DecorateHelper = new(
        "typescript:decorate",
        scoped: false,
        priority: 2,
        importName: "__decorate",
        text: @"
            var __decorate = (this && this.__decorate) || function (decorators, target, key, desc) {
                var c = arguments.length, r = c < 3 ? target : desc === null ? desc = Object.getOwnPropertyDescriptor(target, key) : desc, d;
                if (typeof Reflect === ""object"" && typeof Reflect.decorate === ""function"") r = Reflect.decorate(decorators, target, key, desc);
                else for (var i = decorators.length - 1; i >= 0; i--) if (d = decorators[i]) r = (c < 3 ? d(r) : c > 3 ? d(target, key, r) : d(target, key)) || r;
                return c > 3 && r && Object.defineProperty(target, key, r), r;
            };");

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:744-753 — <c>metadataHelper</c>.</summary>
    public static readonly EmitHelper MetadataHelper = new(
        "typescript:metadata",
        scoped: false,
        priority: 3,
        importName: "__metadata",
        text: @"
            var __metadata = (this && this.__metadata) || function (k, v) {
                if (typeof Reflect === ""object"" && typeof Reflect.metadata === ""function"") return Reflect.metadata(k, v);
            };");

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:755-764 — <c>paramHelper</c>.</summary>
    public static readonly EmitHelper ParamHelper = new(
        "typescript:param",
        scoped: false,
        priority: 4,
        importName: "__param",
        text: @"
            var __param = (this && this.__param) || function (paramIndex, decorator) {
                return function (target, key) { decorator(target, key, paramIndex); }
            };");

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:767-800 — <c>esDecorateHelper</c>.</summary>
    public static readonly EmitHelper ESDecorateHelper = new(
        "typescript:esDecorate",
        scoped: false,
        priority: 2,
        importName: "__esDecorate",
        text: @"
        var __esDecorate = (this && this.__esDecorate) || function (ctor, descriptorIn, decorators, contextIn, initializers, extraInitializers) {
            function accept(f) { if (f !== void 0 && typeof f !== ""function"") throw new TypeError(""Function expected""); return f; }
            var kind = contextIn.kind, key = kind === ""getter"" ? ""get"" : kind === ""setter"" ? ""set"" : ""value"";
            var target = !descriptorIn && ctor ? contextIn[""static""] ? ctor : ctor.prototype : null;
            var descriptor = descriptorIn || (target ? Object.getOwnPropertyDescriptor(target, contextIn.name) : {});
            var _, done = false;
            for (var i = decorators.length - 1; i >= 0; i--) {
                var context = {};
                for (var p in contextIn) context[p] = p === ""access"" ? {} : contextIn[p];
                for (var p in contextIn.access) context.access[p] = contextIn.access[p];
                context.addInitializer = function (f) { if (done) throw new TypeError(""Cannot add initializers after decoration has completed""); extraInitializers.push(accept(f || null)); };
                var result = (0, decorators[i])(kind === ""accessor"" ? { get: descriptor.get, set: descriptor.set } : descriptor[key], context);
                if (kind === ""accessor"") {
                    if (result === void 0) continue;
                    if (result === null || typeof result !== ""object"") throw new TypeError(""Object expected"");
                    if (_ = accept(result.get)) descriptor.get = _;
                    if (_ = accept(result.set)) descriptor.set = _;
                    if (_ = accept(result.init)) initializers.unshift(_);
                }
                else if (_ = accept(result)) {
                    if (kind === ""field"") initializers.unshift(_);
                    else descriptor[key] = _;
                }
            }
            if (target) Object.defineProperty(target, contextIn.name, descriptor);
            done = true;
        };");

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:802-815 — <c>runInitializersHelper</c>.</summary>
    public static readonly EmitHelper RunInitializersHelper = new(
        "typescript:runInitializers",
        scoped: false,
        priority: 2,
        importName: "__runInitializers",
        text: @"
        var __runInitializers = (this && this.__runInitializers) || function (thisArg, initializers, value) {
            var useValue = arguments.length > 2;
            for (var i = 0; i < initializers.length; i++) {
                value = useValue ? initializers[i].call(thisArg, value) : initializers[i].call(thisArg);
            }
            return useValue ? value : void 0;
        };");

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:819-836 — <c>assignHelper</c>.</summary>
    public static readonly EmitHelper AssignHelper = new(
        "typescript:assign",
        scoped: false,
        priority: 1,
        importName: "__assign",
        text: @"
            var __assign = (this && this.__assign) || function () {
                __assign = Object.assign || function(t) {
                    for (var s, i = 1, n = arguments.length; i < n; i++) {
                        s = arguments[i];
                        for (var p in s) if (Object.prototype.hasOwnProperty.call(s, p))
                            t[p] = s[p];
                    }
                    return t;
                };
                return __assign.apply(this, arguments);
            };");

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:838-844 — <c>awaitHelper</c>.</summary>
    public static readonly EmitHelper AwaitHelper = new(
        "typescript:await",
        scoped: false,
        importName: "__await",
        text: @"
            var __await = (this && this.__await) || function (v) { return this instanceof __await ? (this.v = v, this) : new __await(v); }");

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:846-864 — <c>asyncGeneratorHelper</c>.</summary>
    public static readonly EmitHelper AsyncGeneratorHelper = new(
        "typescript:asyncGenerator",
        scoped: false,
        dependencies: [AwaitHelper],
        importName: "__asyncGenerator",
        text: @"
        var __asyncGenerator = (this && this.__asyncGenerator) || function (thisArg, _arguments, generator) {
            if (!Symbol.asyncIterator) throw new TypeError(""Symbol.asyncIterator is not defined."");
            var g = generator.apply(thisArg, _arguments || []), i, q = [];
            return i = Object.create((typeof AsyncIterator === ""function"" ? AsyncIterator : Object).prototype), verb(""next""), verb(""throw""), verb(""return"", awaitReturn), i[Symbol.asyncIterator] = function () { return this; }, i;
            function awaitReturn(f) { return function (v) { return Promise.resolve(v).then(f, reject); }; }
            function verb(n, f) { if (g[n]) { i[n] = function (v) { return new Promise(function (a, b) { q.push([n, v, a, b]) > 1 || resume(n, v); }); }; if (f) i[n] = f(i[n]); } }
            function resume(n, v) { try { step(g[n](v)); } catch (e) { settle(q[0][3], e); } }
            function step(r) { r.value instanceof __await ? Promise.resolve(r.value.v).then(fulfill, reject) : settle(q[0][2], r); }
            function fulfill(value) { resume(""next"", value); }
            function reject(value) { resume(""throw"", value); }
            function settle(f, v) { if (f(v), q.shift(), q.length) resume(q[0][0], q[0][1]); }
        };");

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:866-877 — <c>asyncDelegator</c>.</summary>
    public static readonly EmitHelper AsyncDelegator = new(
        "typescript:asyncDelegator",
        scoped: false,
        dependencies: [AwaitHelper],
        importName: "__asyncDelegator",
        text: @"
            var __asyncDelegator = (this && this.__asyncDelegator) || function (o) {
                var i, p;
                return i = {}, verb(""next""), verb(""throw"", function (e) { throw e; }), verb(""return""), i[Symbol.iterator] = function () { return this; }, i;
                function verb(n, f) { i[n] = o[n] ? function (v) { return (p = !p) ? { value: __await(o[n](v)), done: false } : f ? f(v) : v; } : f; }
            };");

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:879-891 — <c>asyncValues</c>.</summary>
    public static readonly EmitHelper AsyncValues = new(
        "typescript:asyncValues",
        scoped: false,
        importName: "__asyncValues",
        text: @"
            var __asyncValues = (this && this.__asyncValues) || function (o) {
                if (!Symbol.asyncIterator) throw new TypeError(""Symbol.asyncIterator is not defined."");
                var m = o[Symbol.asyncIterator], i;
                return m ? m.call(o) : (o = typeof __values === ""function"" ? __values(o) : o[Symbol.iterator](), i = {}, verb(""next""), verb(""throw""), verb(""return""), i[Symbol.asyncIterator] = function () { return this; }, i);
                function verb(n) { i[n] = o[n] && function (v) { return new Promise(function (resolve, reject) { v = o[n](v), settle(resolve, reject, v.done, v.value); }); }; }
                function settle(resolve, reject, d, v) { Promise.resolve(v).then(function(v) { resolve({ value: v, done: d }); }, reject); }
            };");

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:895-911 — <c>restHelper</c>.</summary>
    public static readonly EmitHelper RestHelper = new(
        "typescript:rest",
        scoped: false,
        importName: "__rest",
        text: @"
            var __rest = (this && this.__rest) || function (s, e) {
                var t = {};
                for (var p in s) if (Object.prototype.hasOwnProperty.call(s, p) && e.indexOf(p) < 0)
                    t[p] = s[p];
                if (s != null && typeof Object.getOwnPropertySymbols === ""function"")
                    for (var i = 0, p = Object.getOwnPropertySymbols(s); i < p.length; i++) {
                        if (e.indexOf(p[i]) < 0 && Object.prototype.propertyIsEnumerable.call(s, p[i]))
                            t[p[i]] = s[p[i]];
                    }
                return t;
            };");

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:915-930 — <c>awaiterHelper</c>.</summary>
    public static readonly EmitHelper AwaiterHelper = new(
        "typescript:awaiter",
        scoped: false,
        priority: 5,
        importName: "__awaiter",
        text: @"
            var __awaiter = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
                function adopt(value) { return value instanceof P ? value : new P(function (resolve) { resolve(value); }); }
                return new (P || (P = Promise))(function (resolve, reject) {
                    function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
                    function rejected(value) { try { step(generator[""throw""](value)); } catch (e) { reject(e); } }
                    function step(result) { result.done ? resolve(result.value) : adopt(result.value).then(fulfilled, rejected); }
                    step((generator = generator.apply(thisArg, _arguments || [])).next());
                });
            };");

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:934-956 — <c>extendsHelper</c>.</summary>
    public static readonly EmitHelper ExtendsHelper = new(
        "typescript:extends",
        scoped: false,
        priority: 0,
        importName: "__extends",
        text: @"
            var __extends = (this && this.__extends) || (function () {
                var extendStatics = function (d, b) {
                    extendStatics = Object.setPrototypeOf ||
                        ({ __proto__: [] } instanceof Array && function (d, b) { d.__proto__ = b; }) ||
                        function (d, b) { for (var p in b) if (Object.prototype.hasOwnProperty.call(b, p)) d[p] = b[p]; };
                    return extendStatics(d, b);
                };

                return function (d, b) {
                    if (typeof b !== ""function"" && b !== null)
                        throw new TypeError(""Class extends value "" + String(b) + "" is not a constructor or null"");
                    extendStatics(d, b);
                    function __() { this.constructor = d; }
                    d.prototype = b === null ? Object.create(b) : (__.prototype = b.prototype, new __());
                };
            })();");

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:958-968 — <c>templateObjectHelper</c>.</summary>
    public static readonly EmitHelper TemplateObjectHelper = new(
        "typescript:makeTemplateObject",
        scoped: false,
        priority: 0,
        importName: "__makeTemplateObject",
        text: @"
            var __makeTemplateObject = (this && this.__makeTemplateObject) || function (cooked, raw) {
                if (Object.defineProperty) { Object.defineProperty(cooked, ""raw"", { value: raw }); } else { cooked.raw = raw; }
                return cooked;
            };");

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:970-991 — <c>readHelper</c>.</summary>
    public static readonly EmitHelper ReadHelper = new(
        "typescript:read",
        scoped: false,
        importName: "__read",
        text: @"
            var __read = (this && this.__read) || function (o, n) {
                var m = typeof Symbol === ""function"" && o[Symbol.iterator];
                if (!m) return o;
                var i = m.call(o), r, ar = [], e;
                try {
                    while ((n === void 0 || n-- > 0) && !(r = i.next()).done) ar.push(r.value);
                }
                catch (error) { e = { error: error }; }
                finally {
                    try {
                        if (r && !r.done && (m = i[""return""])) m.call(i);
                    }
                    finally { if (e) throw e.error; }
                }
                return ar;
            };");

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:993-1007 — <c>spreadArrayHelper</c>.</summary>
    public static readonly EmitHelper SpreadArrayHelper = new(
        "typescript:spreadArray",
        scoped: false,
        importName: "__spreadArray",
        text: @"
            var __spreadArray = (this && this.__spreadArray) || function (to, from, pack) {
                if (pack || arguments.length === 2) for (var i = 0, l = from.length, ar; i < l; i++) {
                    if (ar || !(i in from)) {
                        if (!ar) ar = Array.prototype.slice.call(from, 0, i);
                        ar[i] = from[i];
                    }
                }
                return to.concat(ar || Array.prototype.slice.call(from));
            };");

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:1009-1017 — <c>propKeyHelper</c>.</summary>
    public static readonly EmitHelper PropKeyHelper = new(
        "typescript:propKey",
        scoped: false,
        importName: "__propKey",
        text: @"
        var __propKey = (this && this.__propKey) || function (x) {
            return typeof x === ""symbol"" ? x : """".concat(x);
        };");

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:1020-1029 — <c>setFunctionNameHelper</c>.</summary>
    public static readonly EmitHelper SetFunctionNameHelper = new(
        "typescript:setFunctionName",
        scoped: false,
        importName: "__setFunctionName",
        text: @"
        var __setFunctionName = (this && this.__setFunctionName) || function (f, name, prefix) {
            if (typeof name === ""symbol"") name = name.description ? ""["".concat(name.description, ""]"") : """";
            return Object.defineProperty(f, ""name"", { configurable: true, value: prefix ? """".concat(prefix, "" "", name) : name });
        };");

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:1033-1049 — <c>valuesHelper</c>.</summary>
    public static readonly EmitHelper ValuesHelper = new(
        "typescript:values",
        scoped: false,
        importName: "__values",
        text: @"
            var __values = (this && this.__values) || function(o) {
                var s = typeof Symbol === ""function"" && Symbol.iterator, m = s && o[s], i = 0;
                if (m) return m.call(o);
                if (o && typeof o.length === ""number"") return {
                    next: function () {
                        if (o && i >= o.length) o = void 0;
                        return { value: o && o[i++], done: !o };
                    }
                };
                throw new TypeError(s ? ""Object is not iterable."" : ""Symbol.iterator is not defined."");
            };");

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:1114-1147 — <c>generatorHelper</c>.</summary>
    public static readonly EmitHelper GeneratorHelper = new(
        "typescript:generator",
        scoped: false,
        priority: 6,
        importName: "__generator",
        text: @"
            var __generator = (this && this.__generator) || function (thisArg, body) {
                var _ = { label: 0, sent: function() { if (t[0] & 1) throw t[1]; return t[1]; }, trys: [], ops: [] }, f, y, t, g = Object.create((typeof Iterator === ""function"" ? Iterator : Object).prototype);
                return g.next = verb(0), g[""throw""] = verb(1), g[""return""] = verb(2), typeof Symbol === ""function"" && (g[Symbol.iterator] = function() { return this; }), g;
                function verb(n) { return function (v) { return step([n, v]); }; }
                function step(op) {
                    if (f) throw new TypeError(""Generator is already executing."");
                    while (g && (g = 0, op[0] && (_ = 0)), _) try {
                        if (f = 1, y && (t = op[0] & 2 ? y[""return""] : op[0] ? y[""throw""] || ((t = y[""return""]) && t.call(y), 0) : y.next) && !(t = t.call(y, op[1])).done) return t;
                        if (y = 0, t) op = [op[0] & 2, t.value];
                        switch (op[0]) {
                            case 0: case 1: t = op; break;
                            case 4: _.label++; return { value: op[1], done: false };
                            case 5: _.label++; y = op[1]; op = [0]; continue;
                            case 7: op = _.ops.pop(); _.trys.pop(); continue;
                            default:
                                if (!(t = _.trys, t = t.length > 0 && t[t.length - 1]) && (op[0] === 6 || op[0] === 2)) { _ = 0; continue; }
                                if (op[0] === 3 && (!t || (op[1] > t[0] && op[1] < t[3]))) { _.label = op[1]; break; }
                                if (op[0] === 6 && _.label < t[1]) { _.label = t[1]; t = op; break; }
                                if (t && _.label < t[2]) { _.label = t[2]; _.ops.push(op); break; }
                                if (t[2]) _.ops.pop();
                                _.trys.pop(); continue;
                        }
                        op = body.call(thisArg, _);
                    } catch (e) { op = [6, e]; y = 0; } finally { f = t = 0; }
                    if (op[0] & 5) throw op[1]; return { value: op[0] ? op[1] : void 0, done: true };
                }
            };");

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:1151-1168 — <c>createBindingHelper</c>.</summary>
    public static readonly EmitHelper CreateBindingHelper = new(
        "typescript:commonjscreatebinding",
        scoped: false,
        priority: 1,
        importName: "__createBinding",
        text: @"
            var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
                if (k2 === undefined) k2 = k;
                var desc = Object.getOwnPropertyDescriptor(m, k);
                if (!desc || (""get"" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
                  desc = { enumerable: true, get: function() { return m[k]; } };
                }
                Object.defineProperty(o, k2, desc);
            }) : (function(o, m, k, k2) {
                if (k2 === undefined) k2 = k;
                o[k2] = m[k];
            }));");

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:1170-1181 — <c>setModuleDefaultHelper</c>.</summary>
    public static readonly EmitHelper SetModuleDefaultHelper = new(
        "typescript:commonjscreatevalue",
        scoped: false,
        priority: 1,
        importName: "__setModuleDefault",
        text: @"
            var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
                Object.defineProperty(o, ""default"", { enumerable: true, value: v });
            }) : function(o, v) {
                o[""default""] = v;
            });");

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:1184-1208 — <c>importStarHelper</c>.</summary>
    public static readonly EmitHelper ImportStarHelper = new(
        "typescript:commonjsimportstar",
        scoped: false,
        priority: 2,
        dependencies: [CreateBindingHelper, SetModuleDefaultHelper],
        importName: "__importStar",
        text: @"
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
                    if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== ""default"") __createBinding(result, mod, k[i]);
                    __setModuleDefault(result, mod);
                    return result;
                };
            })();");

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:1211-1219 — <c>importDefaultHelper</c>.</summary>
    public static readonly EmitHelper ImportDefaultHelper = new(
        "typescript:commonjsimportdefault",
        scoped: false,
        importName: "__importDefault",
        text: @"
            var __importDefault = (this && this.__importDefault) || function (mod) {
                return (mod && mod.__esModule) ? mod : { ""default"": mod };
            };");

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:1221-1231 — <c>exportStarHelper</c>.</summary>
    public static readonly EmitHelper ExportStarHelper = new(
        "typescript:export-star",
        scoped: false,
        priority: 2,
        dependencies: [CreateBindingHelper],
        importName: "__exportStar",
        text: @"
            var __exportStar = (this && this.__exportStar) || function(m, exports) {
                for (var p in m) if (p !== ""default"" && !Object.prototype.hasOwnProperty.call(exports, p)) __createBinding(exports, m, p);
            };");

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:1281-1291 — <c>classPrivateFieldGetHelper</c>.</summary>
    public static readonly EmitHelper ClassPrivateFieldGetHelper = new(
        "typescript:classPrivateFieldGet",
        scoped: false,
        importName: "__classPrivateFieldGet",
        text: @"
            var __classPrivateFieldGet = (this && this.__classPrivateFieldGet) || function (receiver, state, kind, f) {
                if (kind === ""a"" && !f) throw new TypeError(""Private accessor was defined without a getter"");
                if (typeof state === ""function"" ? receiver !== state || !f : !state.has(receiver)) throw new TypeError(""Cannot read private member from an object whose class did not declare it"");
                return kind === ""m"" ? f : kind === ""a"" ? f.call(receiver) : f ? f.value : state.get(receiver);
            };");

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:1344-1355 — <c>classPrivateFieldSetHelper</c>.</summary>
    public static readonly EmitHelper ClassPrivateFieldSetHelper = new(
        "typescript:classPrivateFieldSet",
        scoped: false,
        importName: "__classPrivateFieldSet",
        text: @"
            var __classPrivateFieldSet = (this && this.__classPrivateFieldSet) || function (receiver, state, value, kind, f) {
                if (kind === ""m"") throw new TypeError(""Private method is not writable"");
                if (kind === ""a"" && !f) throw new TypeError(""Private accessor was defined without a setter"");
                if (typeof state === ""function"" ? receiver !== state || !f : !state.has(receiver)) throw new TypeError(""Cannot write private member to an object whose class did not declare it"");
                return (kind === ""a"" ? f.call(receiver, value) : f ? f.value = value : state.set(receiver, value)), value;
            };");

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:1369-1378 — <c>classPrivateFieldInHelper</c>.</summary>
    public static readonly EmitHelper ClassPrivateFieldInHelper = new(
        "typescript:classPrivateFieldIn",
        scoped: false,
        importName: "__classPrivateFieldIn",
        text: @"
            var __classPrivateFieldIn = (this && this.__classPrivateFieldIn) || function(state, receiver) {
                if (receiver === null || (typeof receiver !== ""object"" && typeof receiver !== ""function"")) throw new TypeError(""Cannot use 'in' operator on non-object"");
                return typeof state === ""function"" ? receiver === state : state.has(receiver);
            };");

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:1380-1407 — <c>addDisposableResourceHelper</c>.</summary>
    public static readonly EmitHelper AddDisposableResourceHelper = new(
        "typescript:addDisposableResource",
        scoped: false,
        importName: "__addDisposableResource",
        text: @"
        var __addDisposableResource = (this && this.__addDisposableResource) || function (env, value, async) {
            if (value !== null && value !== void 0) {
                if (typeof value !== ""object"" && typeof value !== ""function"") throw new TypeError(""Object expected."");
                var dispose, inner;
                if (async) {
                    if (!Symbol.asyncDispose) throw new TypeError(""Symbol.asyncDispose is not defined."");
                    dispose = value[Symbol.asyncDispose];
                }
                if (dispose === void 0) {
                    if (!Symbol.dispose) throw new TypeError(""Symbol.dispose is not defined."");
                    dispose = value[Symbol.dispose];
                    if (async) inner = dispose;
                }
                if (typeof dispose !== ""function"") throw new TypeError(""Object not disposable."");
                if (inner) dispose = function() { try { inner.call(this); } catch (e) { return Promise.reject(e); } };
                env.stack.push({ value: value, dispose: dispose, async: async });
            }
            else if (async) {
                env.stack.push({ async: true });
            }
            return value;
        };");

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:1414-1449 — <c>disposeResourcesHelper</c>.</summary>
    public static readonly EmitHelper DisposeResourcesHelper = new(
        "typescript:disposeResources",
        scoped: false,
        importName: "__disposeResources",
        text: @"
        var __disposeResources = (this && this.__disposeResources) || (function (SuppressedError) {
            return function (env) {
                function fail(e) {
                    env.error = env.hasError ? new SuppressedError(e, env.error, ""An error was suppressed during disposal."") : e;
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
        })(typeof SuppressedError === ""function"" ? SuppressedError : function (error, suppressed, message) {
            var e = new Error(message);
            return e.name = ""SuppressedError"", e.error = error, e.suppressed = suppressed, e;
        });");

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:1451-1464 — <c>rewriteRelativeImportExtensionsHelper</c>.</summary>
    public static readonly EmitHelper RewriteRelativeImportExtensionsHelper = new(
        "typescript:rewriteRelativeImportExtensions",
        scoped: false,
        importName: "__rewriteRelativeImportExtension",
        text: @"
        var __rewriteRelativeImportExtension = (this && this.__rewriteRelativeImportExtension) || function (path, preserveJsx) {
            if (typeof path === ""string"" && /^\.\.?\//.test(path)) {
                return path.replace(/\.(tsx)$|((?:\.d)?)((?:\.[^./]+?)?)\.([cm]?)ts$/i, function (m, tsx, d, ext, cm) {
                    return tsx ? preserveJsx ? "".jsx"" : "".js"" : d && (!ext || !cm) ? m : (d + ext + ""."" + cm.toLowerCase() + ""js"");
                });
            }
            return path;
        };");
}

// ported from: src/compiler/factory/emitHelpers.ts:53-98 — the ESDecorate context types
/// <summary>
/// The decorator context object a native ECMAScript decorator is called with: either the class variant or the class
/// element variant.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS declares <c>ESDecorateContext</c> as the union <c>ESDecorateClassContext |
/// ESDecorateClassElementContext</c>, discriminated at runtime by <c>kind === "class"</c>. C# has no union of
/// interfaces, so the port has an abstract base with one subclass per variant and the discriminant <em>is</em> the
/// class — the same mapping the model's other unions get (§5 rule 3). The generated
/// <see cref="IEmitHelperFactory"/> slot for it is <c>Node?</c> (the union anchors at the model's common base, §12.9),
/// so the variants derive from <see cref="Node"/> exactly as <see cref="EmitHelper"/> does; nothing ever puts one in a
/// tree and only the body of <c>createESDecorateContextObject</c> has to tell them apart.
/// </remarks>
internal abstract class ESDecorateContext : Node
{
    /// <summary>A context object is not a syntax form; it is handed to a decorator factory call.</summary>
    public override SyntaxKind Kind => SyntaxKind.Unknown;
}

/// <summary>ported from: src/compiler/factory/emitHelpers.ts:53-66 — <c>ESDecorateClassContext</c>.</summary>
/// <remarks>
/// PORT-NOTE: TS's <c>kind: "class"</c> member is the union's discriminant; it is written by the transformer and never
/// read by this file (the class context object here always spells <c>"class"</c> literally), and the port's
/// discriminant is the type, so it is not carried as a slot.
/// </remarks>
internal sealed class ESDecorateClassContext : ESDecorateContext
{
    public ESDecorateClassContext(Expression? name, Expression? metadata)
    {
        Name = name;
        Metadata = metadata;
    }

    public Expression? Name { get; set; }

    public Expression? Metadata { get; set; }
}

/// <summary>ported from: src/compiler/factory/emitHelpers.ts:72-83 — <c>ESDecorateClassElementContext</c>.</summary>
internal sealed class ESDecorateClassElementContext : ESDecorateContext
{
    public ESDecorateClassElementContext(
        string kind,
        ESDecorateName name,
        bool @static,
        bool @private,
        ESDecorateClassElementAccess? access,
        Expression? metadata)
    {
        Kind = kind;
        Name = name;
        Static = @static;
        Private = @private;
        Access = access;
        Metadata = metadata;
    }

    /// <summary>The kind of the decorated element: <c>method</c>, <c>getter</c>, <c>setter</c>, <c>accessor</c> or
    /// <c>field</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's slot is the string <c>kind</c>, and this DTO derives from <see cref="Node"/> (see the note on
    /// <see cref="ESDecorateContext"/>), whose own <c>Kind</c> is a <see cref="SyntaxKind"/>. The two cannot merge, so
    /// this member hides the base one: a caller holding the context reads the element kind here, and the syntax-kind
    /// member (always <see cref="SyntaxKind.Unknown"/>, like every other non-syntax object in the factory) is only
    /// reachable through a <see cref="Node"/>-typed reference, which nothing takes.
    /// </remarks>
    public new string Kind { get; set; }

    public ESDecorateName Name { get; set; }

    public bool Static { get; set; }

    public bool Private { get; set; }

    public ESDecorateClassElementAccess? Access { get; set; }

    public Expression? Metadata { get; set; }
}

/// <summary>ported from: src/compiler/factory/emitHelpers.ts:85-88 — <c>ESDecorateClassElementAccess</c>.</summary>
internal sealed class ESDecorateClassElementAccess
{
    public ESDecorateClassElementAccess(bool? get, bool? set)
    {
        Get = get;
        Set = set;
    }

    public bool? Get { get; set; }

    public bool? Set { get; set; }
}

/// <summary>ported from: src/compiler/factory/emitHelpers.ts:91-93 — <c>ESDecorateName</c>.</summary>
/// <remarks>
/// PORT-NOTE: TS's <c>ESDecorateName</c> is a union of two inline object types, <c>{ computed: true; name: Expression }</c>
/// and <c>{ computed: false; name: Identifier | PrivateIdentifier }</c>; the union collapses into one constructible class
/// with the flag as a slot, which is what the two <c>name</c> members would be discriminated by anyway. TS's two
/// variants are told apart by <c>computed</c> alone in every use here, so the collapsed shape reads exactly like the
/// reference's <c>elementName.computed ? … : …</c>.
/// </remarks>
internal sealed class ESDecorateName
{
    public ESDecorateName(bool computed, Node? name)
    {
        Computed = computed;
        Name = name;
    }

    /// <summary><c>true</c> for a computed name (<c>[expr]</c>), which the context object must quote as a string.</summary>
    public bool Computed { get; set; }

    public Node? Name { get; set; }
}

// ported from: src/compiler/factory/emitHelpers.ts:148-701 — the factory closes over the context and returns the
// object literal that implements EmitHelperFactory (:101-146, generated as Model/NodeInterfaces.cs's IEmitHelperFactory)
/// <summary>ported from: src/compiler/factory/emitHelpers.ts:148-701 — <c>createEmitHelperFactory</c>.</summary>
/// <remarks>
/// PORT-NOTE: TS's factory closes over <c>context</c> and <c>factory</c>; here they are the
/// <see cref="_context"/>/<see cref="_factory"/> fields, so each member's body reads like the reference's.
/// <para>
/// PORT-NOTE: <c>createParamHelper</c> is the one member whose implementation takes a parameter its interface does not
/// declare (<c>location?: TextRange</c>, :240) and no caller in the reference passes it — the two call sites
/// (<c>legacyDecorators.ts:722</c>) set the range afterwards with <c>setTextRange</c>. The port's member is the
/// interface's two parameters, and the <c>setTextRange</c> the reference wraps around the call is dropped with it,
/// because for every reachable call it returns the call unchanged.
/// </para>
/// </remarks>
internal sealed class EmitHelperFactory : IEmitHelperFactory
{
    private readonly IEmitHelperFactoryContext _context;

    private readonly NodeFactory _factory;

    /// <summary>A <c>true</c> literal the emitter must not substitute for a generated name (<c>InternalEmitFlags.Immutable</c>).</summary>
    private readonly Func<TrueLiteral> _immutableTrue;

    /// <inheritdoc cref="_immutableTrue"/>
    private readonly Func<FalseLiteral> _immutableFalse;

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:149-151 — the <c>factory</c> and the two memoized literals.</summary>
    public EmitHelperFactory(IEmitHelperFactoryContext context)
    {
        _context = context;
        _factory = context.Factory;
        _immutableTrue = Core.Memoize(() => EmitNodes.SetInternalEmitFlags(_factory.CreateTrue(), InternalEmitFlags.Immutable));
        _immutableFalse = Core.Memoize(() => EmitNodes.SetInternalEmitFlags(_factory.CreateFalse(), InternalEmitFlags.Immutable));
    }

    // ---------------------------------------------------------------------------------------------
    // TypeScript Helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:202-206 — <c>getUnscopedHelperName</c>.</summary>
    public Identifier GetUnscopedHelperName(string? name)
        => EmitNodes.SetEmitFlags(_factory.CreateIdentifier(name), EmitFlags.HelperName | EmitFlags.AdviseOnEmitNode);

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:208-226 — <c>createDecorateHelper</c>.</summary>
    public Expression CreateDecorateHelper(List<Expression>? decoratorExpressions, Expression? target, Expression? memberName, Expression? descriptor)
    {
        _context.RequestEmitHelper(EmitHelpers.DecorateHelper);

        var argumentsArray = new List<Expression>
        {
            _factory.CreateArrayLiteralExpression(decoratorExpressions, /*multiLine*/ true),
            target!,
        };
        if (memberName is not null)
        {
            argumentsArray.Add(memberName);
            if (descriptor is not null)
            {
                argumentsArray.Add(descriptor);
            }
        }

        return _factory.CreateCallExpression(
            GetUnscopedHelperName("__decorate"),
            /*typeArguments*/ null,
            argumentsArray);
    }

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:228-238 — <c>createMetadataHelper</c>.</summary>
    public Expression CreateMetadataHelper(string? metadataKey, Expression? metadataValue)
    {
        _context.RequestEmitHelper(EmitHelpers.MetadataHelper);
        return _factory.CreateCallExpression(
            GetUnscopedHelperName("__metadata"),
            /*typeArguments*/ null,
            [
                _factory.CreateStringLiteral(metadataKey),
                metadataValue!,
            ]);
    }

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:240-255 — <c>createParamHelper</c>.</summary>
    public Expression CreateParamHelper(Expression? expression, int parameterOffset)
    {
        _context.RequestEmitHelper(EmitHelpers.ParamHelper);
        return _factory.CreateCallExpression(
            GetUnscopedHelperName("__param"),
            /*typeArguments*/ null,
            [
                _factory.CreateNumericLiteral(parameterOffset),
                expression!,
            ]);
    }

    // ---------------------------------------------------------------------------------------------
    // ES Decorators Helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:257-265 — <c>createESDecorateClassContextObject</c>.</summary>
    private ObjectLiteralExpression CreateESDecorateClassContextObject(ESDecorateClassContext contextIn)
    {
        var properties = new List<Node>
        {
            _factory.CreatePropertyAssignment(_factory.CreateIdentifier("kind"), _factory.CreateStringLiteral("class")),
            _factory.CreatePropertyAssignment(_factory.CreateIdentifier("name"), contextIn.Name!),
            _factory.CreatePropertyAssignment(_factory.CreateIdentifier("metadata"), contextIn.Metadata!),
        };

        return _factory.CreateObjectLiteralExpression(properties, /*multiLine*/ null);
    }

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:267-287 — <c>createESDecorateClassElementAccessGetMethod</c>.</summary>
    private PropertyAssignment CreateESDecorateClassElementAccessGetMethod(ESDecorateName elementName)
    {
        var accessor = elementName.Computed
            ? (Expression)_factory.CreateElementAccessExpression(_factory.CreateIdentifier("obj"), elementName.Name)
            : _factory.CreatePropertyAccessExpression(_factory.CreateIdentifier("obj"), elementName.Name);

        return _factory.CreatePropertyAssignment(
            "get",
            _factory.CreateArrowFunction(
                /*modifiers*/ null,
                /*typeParameters*/ null,
                [_factory.CreateParameterDeclaration(
                    /*modifiers*/ null,
                    /*dotDotDotToken*/ null,
                    _factory.CreateIdentifier("obj"),
                    /*questionToken*/ null,
                    /*type*/ null,
                    /*initializer*/ null)],
                /*type*/ null,
                /*equalsGreaterThanToken*/ null,
                accessor));
    }

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:289-323 — <c>createESDecorateClassElementAccessSetMethod</c>.</summary>
    private PropertyAssignment CreateESDecorateClassElementAccessSetMethod(ESDecorateName elementName)
    {
        var accessor = elementName.Computed
            ? (Expression)_factory.CreateElementAccessExpression(_factory.CreateIdentifier("obj"), elementName.Name)
            : _factory.CreatePropertyAccessExpression(_factory.CreateIdentifier("obj"), elementName.Name);

        return _factory.CreatePropertyAssignment(
            "set",
            _factory.CreateArrowFunction(
                /*modifiers*/ null,
                /*typeParameters*/ null,
                [
                    _factory.CreateParameterDeclaration(
                        /*modifiers*/ null,
                        /*dotDotDotToken*/ null,
                        _factory.CreateIdentifier("obj"),
                        /*questionToken*/ null,
                        /*type*/ null,
                        /*initializer*/ null),
                    _factory.CreateParameterDeclaration(
                        /*modifiers*/ null,
                        /*dotDotDotToken*/ null,
                        _factory.CreateIdentifier("value"),
                        /*questionToken*/ null,
                        /*type*/ null,
                        /*initializer*/ null),
                ],
                /*type*/ null,
                /*equalsGreaterThanToken*/ null,
                _factory.CreateBlock(
                    [
                        _factory.CreateExpressionStatement(
                            _factory.CreateAssignment(
                                accessor,
                                _factory.CreateIdentifier("value"))),
                    ],
                    /*multiLine*/ null)));
    }

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:325-349 — <c>createESDecorateClassElementAccessHasMethod</c>.</summary>
    private PropertyAssignment CreateESDecorateClassElementAccessHasMethod(ESDecorateName elementName)
    {
        var propertyName = elementName.Computed
            ? elementName.Name!
            : elementName.Name is Identifier identifier ? _factory.CreateStringLiteralFromNode(identifier, /*isSingleQuote*/ null) : elementName.Name!;

        return _factory.CreatePropertyAssignment(
            "has",
            _factory.CreateArrowFunction(
                /*modifiers*/ null,
                /*typeParameters*/ null,
                [_factory.CreateParameterDeclaration(
                    /*modifiers*/ null,
                    /*dotDotDotToken*/ null,
                    _factory.CreateIdentifier("obj"),
                    /*questionToken*/ null,
                    /*type*/ null,
                    /*initializer*/ null)],
                /*type*/ null,
                /*equalsGreaterThanToken*/ null,
                _factory.CreateBinaryExpression(
                    (Expression)propertyName,
                    SyntaxKind.InKeyword,
                    _factory.CreateIdentifier("obj"))));
    }

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:351-357 — <c>createESDecorateClassElementAccessObject</c>.</summary>
    private ObjectLiteralExpression CreateESDecorateClassElementAccessObject(ESDecorateName name, ESDecorateClassElementAccess access)
    {
        var properties = new List<Node>();
        properties.Add(CreateESDecorateClassElementAccessHasMethod(name));
        if (Js.IsTruthy(access.Get)) properties.Add(CreateESDecorateClassElementAccessGetMethod(name));
        if (Js.IsTruthy(access.Set)) properties.Add(CreateESDecorateClassElementAccessSetMethod(name));
        return _factory.CreateObjectLiteralExpression(properties, /*multiLine*/ null);
    }

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:359-369 — <c>createESDecorateClassElementContextObject</c>.</summary>
    private ObjectLiteralExpression CreateESDecorateClassElementContextObject(ESDecorateClassElementContext contextIn)
    {
        var name = contextIn.Name;
        var nameExpression = name.Computed
            ? (Expression)name.Name!
            : _factory.CreateStringLiteralFromNode(name.Name, /*isSingleQuote*/ null);

        var properties = new List<Node>
        {
            _factory.CreatePropertyAssignment(_factory.CreateIdentifier("kind"), _factory.CreateStringLiteral(contextIn.Kind)),
            _factory.CreatePropertyAssignment(_factory.CreateIdentifier("name"), nameExpression),
            _factory.CreatePropertyAssignment(_factory.CreateIdentifier("static"), contextIn.Static ? _factory.CreateTrue() : (Expression)_factory.CreateFalse()),
            _factory.CreatePropertyAssignment(_factory.CreateIdentifier("private"), contextIn.Private ? _factory.CreateTrue() : (Expression)_factory.CreateFalse()),
            _factory.CreatePropertyAssignment(_factory.CreateIdentifier("access"), CreateESDecorateClassElementAccessObject(name, contextIn.Access!)),
            _factory.CreatePropertyAssignment(_factory.CreateIdentifier("metadata"), contextIn.Metadata!),
        };

        return _factory.CreateObjectLiteralExpression(properties, /*multiLine*/ null);
    }

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:371-374 — <c>createESDecorateContextObject</c>.</summary>
    private ObjectLiteralExpression CreateESDecorateContextObject(ESDecorateContext contextIn)
        => contextIn is ESDecorateClassContext classContext
            ? CreateESDecorateClassContextObject(classContext)
            : CreateESDecorateClassElementContextObject((ESDecorateClassElementContext)contextIn);

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:376-390 — <c>createESDecorateHelper</c>.</summary>
    public Expression CreateESDecorateHelper(Expression? ctor, Expression? descriptorIn, Expression? decorators, Node? contextIn, Expression? initializers, Expression? extraInitializers)
    {
        _context.RequestEmitHelper(EmitHelpers.ESDecorateHelper);
        return _factory.CreateCallExpression(
            GetUnscopedHelperName("__esDecorate"),
            /*typeArguments*/ null,
            [
                ctor ?? _factory.CreateNull(),
                descriptorIn ?? _factory.CreateNull(),
                decorators!,
                CreateESDecorateContextObject((ESDecorateContext)contextIn!),
                initializers!,
                extraInitializers!,
            ]);
    }

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:392-400 — <c>createRunInitializersHelper</c>.</summary>
    public Expression CreateRunInitializersHelper(Expression? thisArg, Expression? initializers, Expression? value)
    {
        _context.RequestEmitHelper(EmitHelpers.RunInitializersHelper);
        List<Expression> arguments = Js.IsTruthy(value)
            ? [thisArg!, initializers!, value!]
            : [thisArg!, initializers!];

        return _factory.CreateCallExpression(
            GetUnscopedHelperName("__runInitializers"),
            /*typeArguments*/ null,
            arguments);
    }

    // ---------------------------------------------------------------------------------------------
    // ES2018 Helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:402-412 — <c>createAssignHelper</c>.</summary>
    public Expression CreateAssignHelper(List<Expression>? attributesSegments)
    {
        if (Utilities.GetEmitScriptTarget(_context.GetCompilerOptions()) >= ScriptTarget.ES2015)
        {
            return _factory.CreateCallExpression(
                _factory.CreatePropertyAccessExpression(_factory.CreateIdentifier("Object"), "assign"),
                /*typeArguments*/ null,
                attributesSegments);
        }

        _context.RequestEmitHelper(EmitHelpers.AssignHelper);
        return _factory.CreateCallExpression(
            GetUnscopedHelperName("__assign"),
            /*typeArguments*/ null,
            attributesSegments);
    }

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:414-417 — <c>createAwaitHelper</c>.</summary>
    public Expression CreateAwaitHelper(Expression? expression)
    {
        _context.RequestEmitHelper(EmitHelpers.AwaitHelper);
        return _factory.CreateCallExpression(GetUnscopedHelperName("__await"), /*typeArguments*/ null, [expression!]);
    }

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:419-435 — <c>createAsyncGeneratorHelper</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS writes the emit-node slot inline here — <c>(generatorFunc.emitNode || (generatorFunc.emitNode =
    /// {} as EmitNode)).flags |= …</c> — rather than calling <c>getOrCreateEmitNode</c>, because the annotation tracking
    /// that helper does is for parse-tree nodes. The generator function is always built by this very member (or by a
    /// transformer through the factory), so it is synthesized and the port's
    /// <see cref="EmitNodes.GetOrCreateEmitNode"/> does exactly the same thing: create an empty <c>EmitNode</c>.
    /// </remarks>
    public Expression CreateAsyncGeneratorHelper(FunctionExpression? generatorFunc, bool hasLexicalThis)
    {
        _context.RequestEmitHelper(EmitHelpers.AwaitHelper);
        _context.RequestEmitHelper(EmitHelpers.AsyncGeneratorHelper);

        // Mark this node as originally an async function
        EmitNodes.GetOrCreateEmitNode(generatorFunc!).Flags |= EmitFlags.AsyncFunctionBody | EmitFlags.ReuseTempVariableScope;

        return _factory.CreateCallExpression(
            GetUnscopedHelperName("__asyncGenerator"),
            /*typeArguments*/ null,
            [
                hasLexicalThis ? _factory.CreateThis() : (Expression)_factory.CreateVoidZero(),
                _factory.CreateIdentifier("arguments"),
                generatorFunc!,
            ]);
    }

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:437-445 — <c>createAsyncDelegatorHelper</c>.</summary>
    public Expression CreateAsyncDelegatorHelper(Expression? expression)
    {
        _context.RequestEmitHelper(EmitHelpers.AwaitHelper);
        _context.RequestEmitHelper(EmitHelpers.AsyncDelegator);
        return _factory.CreateCallExpression(
            GetUnscopedHelperName("__asyncDelegator"),
            /*typeArguments*/ null,
            [expression!]);
    }

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:447-459 — <c>createAsyncValuesHelper</c>.</summary>
    public Expression CreateAsyncValuesHelper(Expression? expression)
    {
        _context.RequestEmitHelper(EmitHelpers.AsyncValues);
        return _factory.CreateCallExpression(
            GetUnscopedHelperName("__asyncValues"),
            /*typeArguments*/ null,
            [expression!]);
    }

    // ---------------------------------------------------------------------------------------------
    // ES2018 Destructuring Helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:461-500 — <c>createRestHelper</c>.</summary>
    /// <remarks>
    /// Given <c>value: o</c>, <c>propName: p</c>, pattern <c>{ a, b, ...p }</c> from the original statement
    /// <c>{ a, b, ...p } = o</c>, create <c>p = __rest(o, ["a", "b"]);</c>
    /// <para>
    /// PORT-NOTE: the generated slot for <c>elements</c> is <c>List&lt;object?&gt;?</c> (TS's
    /// <c>readonly BindingOrAssignmentElement[]</c> is a union, §12.5); every element a caller can pass is a node, so the
    /// loop casts back to <see cref="Node"/> for <c>getPropertyNameOfBindingOrAssignmentElement</c> — a non-node would
    /// be a caller bug and throws rather than being skipped.
    /// </para>
    /// </remarks>
    public Expression CreateRestHelper(Expression? value, List<object?>? elements, List<Expression>? computedTempVariables, ITextRange? location)
    {
        _context.RequestEmitHelper(EmitHelpers.RestHelper);
        var propertyNames = new List<Expression>();
        var computedTempVariableOffset = 0;
        for (var i = 0; i < elements!.Count - 1; i++)
        {
            var propertyName = FactoryUtilities.GetPropertyNameOfBindingOrAssignmentElement((Node)elements[i]!);
            if (propertyName is not null)
            {
                if (NodeTests.IsComputedPropertyName(propertyName))
                {
                    Debug.AssertIsDefined(computedTempVariables, "Encountered computed property name but 'computedTempVariables' argument was not provided.");
                    var temp = computedTempVariables[computedTempVariableOffset];
                    computedTempVariableOffset++;
                    // typeof _tmp === "symbol" ? _tmp : _tmp + ""
                    propertyNames.Add(
                        _factory.CreateConditionalExpression(
                            _factory.CreateTypeCheck(temp, "symbol"),
                            /*questionToken*/ null,
                            temp,
                            /*colonToken*/ null,
                            _factory.CreateAdd(temp, _factory.CreateStringLiteral(""))));
                }
                else
                {
                    propertyNames.Add(_factory.CreateStringLiteralFromNode(propertyName, /*isSingleQuote*/ null));
                }
            }
        }

        return _factory.CreateCallExpression(
            GetUnscopedHelperName("__rest"),
            /*typeArguments*/ null,
            [
                value!,
                Utilities.SetTextRange(
                    _factory.CreateArrayLiteralExpression(propertyNames, /*multiLine*/ null),
                    location),
            ]);
    }

    // ---------------------------------------------------------------------------------------------
    // ES2017 Helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:503-531 — <c>createAwaiterHelper</c>.</summary>
    /// <remarks>PORT-NOTE: the generator function's emit-node slot is filled in like the one in
    /// <see cref="CreateAsyncGeneratorHelper"/> (TS writes the same inline <c>emitNode ||= …</c> there).</remarks>
    public Expression CreateAwaiterHelper(bool hasLexicalThis, Expression? argumentsExpression, Node? promiseConstructor, List<ParameterDeclaration>? parameters, Block? body)
    {
        _context.RequestEmitHelper(EmitHelpers.AwaiterHelper);

        var generatorFunc = _factory.CreateFunctionExpression(
            /*modifiers*/ null,
            new PunctuationToken(SyntaxKind.AsteriskToken),
            /*name*/ null,
            /*typeParameters*/ null,
            parameters ?? [],
            /*type*/ null,
            body);

        // Mark this node as originally an async function
        EmitNodes.GetOrCreateEmitNode(generatorFunc).Flags |= EmitFlags.AsyncFunctionBody | EmitFlags.ReuseTempVariableScope;

        return _factory.CreateCallExpression(
            GetUnscopedHelperName("__awaiter"),
            /*typeArguments*/ null,
            [
                hasLexicalThis ? _factory.CreateThis() : (Expression)_factory.CreateVoidZero(),
                argumentsExpression ?? (Expression)_factory.CreateVoidZero(),
                promiseConstructor is not null ? FactoryUtilities.CreateExpressionFromEntityName(_factory, promiseConstructor) : _factory.CreateVoidZero(),
                generatorFunc,
            ]);
    }

    // ---------------------------------------------------------------------------------------------
    // ES2015 Helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:533-540 — <c>createExtendsHelper</c>.</summary>
    public Expression CreateExtendsHelper(Identifier? name)
    {
        _context.RequestEmitHelper(EmitHelpers.ExtendsHelper);
        return _factory.CreateCallExpression(
            GetUnscopedHelperName("__extends"),
            /*typeArguments*/ null,
            [
                name!,
                _factory.CreateUniqueName("_super", GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel),
            ]);
    }

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:542-549 — <c>createTemplateObjectHelper</c>.</summary>
    public Expression CreateTemplateObjectHelper(ArrayLiteralExpression? cooked, ArrayLiteralExpression? raw)
    {
        _context.RequestEmitHelper(EmitHelpers.TemplateObjectHelper);
        return _factory.CreateCallExpression(
            GetUnscopedHelperName("__makeTemplateObject"),
            /*typeArguments*/ null,
            [cooked!, raw!]);
    }

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:551-558 — <c>createSpreadArrayHelper</c>.</summary>
    public Expression CreateSpreadArrayHelper(Expression? to, Expression? from, bool packFrom)
    {
        _context.RequestEmitHelper(EmitHelpers.SpreadArrayHelper);
        return _factory.CreateCallExpression(
            GetUnscopedHelperName("__spreadArray"),
            /*typeArguments*/ null,
            [to!, from!, packFrom ? (Expression)_immutableTrue() : _immutableFalse()]);
    }

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:560-567 — <c>createPropKeyHelper</c>.</summary>
    public Expression CreatePropKeyHelper(Expression? expr)
    {
        _context.RequestEmitHelper(EmitHelpers.PropKeyHelper);
        return _factory.CreateCallExpression(
            GetUnscopedHelperName("__propKey"),
            /*typeArguments*/ null,
            [expr!]);
    }

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:569-578 — <c>createSetFunctionNameHelper</c>.</summary>
    public Expression CreateSetFunctionNameHelper(Expression? f, Expression? name, string? prefix)
    {
        _context.RequestEmitHelper(EmitHelpers.SetFunctionNameHelper);
        List<Expression> arguments = Js.IsTruthy(prefix)
            ? [f!, name!, _factory.CreateStringLiteral(prefix)]
            : [f!, name!];

        return _factory.CreateCallExpression(
            GetUnscopedHelperName("__setFunctionName"),
            /*typeArguments*/ null,
            arguments);
    }

    // ---------------------------------------------------------------------------------------------
    // ES2015 Destructuring Helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:580-587 — <c>createValuesHelper</c>.</summary>
    public Expression CreateValuesHelper(Expression? expression)
    {
        _context.RequestEmitHelper(EmitHelpers.ValuesHelper);
        return _factory.CreateCallExpression(
            GetUnscopedHelperName("__values"),
            /*typeArguments*/ null,
            [expression!]);
    }

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:589-600 — <c>createReadHelper</c>.</summary>
    public Expression CreateReadHelper(Expression? iteratorRecord, int? count)
    {
        _context.RequestEmitHelper(EmitHelpers.ReadHelper);
        List<Expression> arguments = count is not null
            ? [iteratorRecord!, _factory.CreateNumericLiteral(count.Value)]
            : [iteratorRecord!];

        return _factory.CreateCallExpression(
            GetUnscopedHelperName("__read"),
            /*typeArguments*/ null,
            arguments);
    }

    // ---------------------------------------------------------------------------------------------
    // ES2015 Generator Helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:602-611 — <c>createGeneratorHelper</c>.</summary>
    public Expression CreateGeneratorHelper(FunctionExpression? body)
    {
        _context.RequestEmitHelper(EmitHelpers.GeneratorHelper);
        return _factory.CreateCallExpression(
            GetUnscopedHelperName("__generator"),
            /*typeArguments*/ null,
            [_factory.CreateThis(), body!]);
    }

    // ---------------------------------------------------------------------------------------------
    // ES Module Helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:613-620 — <c>createImportStarHelper</c>.</summary>
    public Expression CreateImportStarHelper(Expression? expression)
    {
        _context.RequestEmitHelper(EmitHelpers.ImportStarHelper);
        return _factory.CreateCallExpression(
            GetUnscopedHelperName("__importStar"),
            /*typeArguments*/ null,
            [expression!]);
    }

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:622-625 — <c>createImportStarCallbackHelper</c>.</summary>
    public Expression CreateImportStarCallbackHelper()
    {
        _context.RequestEmitHelper(EmitHelpers.ImportStarHelper);
        return GetUnscopedHelperName("__importStar");
    }

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:627-634 — <c>createImportDefaultHelper</c>.</summary>
    public Expression CreateImportDefaultHelper(Expression? expression)
    {
        _context.RequestEmitHelper(EmitHelpers.ImportDefaultHelper);
        return _factory.CreateCallExpression(
            GetUnscopedHelperName("__importDefault"),
            /*typeArguments*/ null,
            [expression!]);
    }

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:636-646 — <c>createExportStarHelper</c>.</summary>
    public Expression CreateExportStarHelper(Expression? moduleExpression, Expression? exportsExpression)
    {
        _context.RequestEmitHelper(EmitHelpers.ExportStarHelper);
        _context.RequestEmitHelper(EmitHelpers.CreateBindingHelper);
        return _factory.CreateCallExpression(
            GetUnscopedHelperName("__exportStar"),
            /*typeArguments*/ null,
            [moduleExpression!, exportsExpression ?? _factory.CreateIdentifier("exports")]);
    }

    // ---------------------------------------------------------------------------------------------
    // Class Fields Helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:648-658 — <c>createClassPrivateFieldGetHelper</c>.</summary>
    public Expression CreateClassPrivateFieldGetHelper(Expression? receiver, Identifier? state, PrivateIdentifierKind kind, Identifier? f)
    {
        _context.RequestEmitHelper(EmitHelpers.ClassPrivateFieldGetHelper);
        List<Expression> arguments = f is null
            ? [receiver!, state!, _factory.CreateStringLiteral(kind.Value)]
            : [receiver!, state!, _factory.CreateStringLiteral(kind.Value), f];

        return _factory.CreateCallExpression(GetUnscopedHelperName("__classPrivateFieldGet"), /*typeArguments*/ null, arguments);
    }

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:660-670 — <c>createClassPrivateFieldSetHelper</c>.</summary>
    public Expression CreateClassPrivateFieldSetHelper(Expression? receiver, Identifier? state, Expression? value, PrivateIdentifierKind kind, Identifier? f)
    {
        _context.RequestEmitHelper(EmitHelpers.ClassPrivateFieldSetHelper);
        List<Expression> arguments = f is null
            ? [receiver!, state!, value!, _factory.CreateStringLiteral(kind.Value)]
            : [receiver!, state!, value!, _factory.CreateStringLiteral(kind.Value), f];

        return _factory.CreateCallExpression(GetUnscopedHelperName("__classPrivateFieldSet"), /*typeArguments*/ null, arguments);
    }

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:672-675 — <c>createClassPrivateFieldInHelper</c>.</summary>
    public Expression CreateClassPrivateFieldInHelper(Identifier? state, Expression? receiver)
    {
        _context.RequestEmitHelper(EmitHelpers.ClassPrivateFieldInHelper);
        return _factory.CreateCallExpression(GetUnscopedHelperName("__classPrivateFieldIn"), /*typeArguments*/ null, [state!, receiver!]);
    }

    // ---------------------------------------------------------------------------------------------
    // 'using' helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:677-684 — <c>createAddDisposableResourceHelper</c>.</summary>
    public Expression CreateAddDisposableResourceHelper(Expression? envBinding, Expression? value, bool @async)
    {
        _context.RequestEmitHelper(EmitHelpers.AddDisposableResourceHelper);
        return _factory.CreateCallExpression(
            GetUnscopedHelperName("__addDisposableResource"),
            /*typeArguments*/ null,
            [envBinding!, value!, @async ? _factory.CreateTrue() : (Expression)_factory.CreateFalse()]);
    }

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:686-689 — <c>createDisposeResourcesHelper</c>.</summary>
    public Expression CreateDisposeResourcesHelper(Expression? envBinding)
    {
        _context.RequestEmitHelper(EmitHelpers.DisposeResourcesHelper);
        return _factory.CreateCallExpression(GetUnscopedHelperName("__disposeResources"), /*typeArguments*/ null, [envBinding!]);
    }

    // ---------------------------------------------------------------------------------------------
    // --rewriteRelativeImportExtensions helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/factory/emitHelpers.ts:691-700 — <c>createRewriteRelativeImportExtensionsHelper</c>.</summary>
    /// <remarks>
    /// The helper takes a second <c>preserveJsx</c> argument exactly when the compiler is asked to preserve JSX; the
    /// option is read straight off the options object (<c>undefined</c> is not <c>JsxEmit.Preserve</c>, so an unset
    /// <c>jsx</c> means "not preserving").
    /// </remarks>
    public Expression CreateRewriteRelativeImportExtensionsHelper(Expression? expression)
    {
        _context.RequestEmitHelper(EmitHelpers.RewriteRelativeImportExtensionsHelper);
        return _factory.CreateCallExpression(
            GetUnscopedHelperName("__rewriteRelativeImportExtension"),
            /*typeArguments*/ null,
            _context.GetCompilerOptions().Jsx == JsxEmit.Preserve
                ? [expression!, (Expression)_factory.CreateTrue()]
                : [expression!]);
    }
}
