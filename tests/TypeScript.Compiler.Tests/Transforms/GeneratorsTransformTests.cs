#nullable enable

namespace TypeScript.Compiler.Tests;

// ported from: src/compiler/transformers/generators.ts:1-3284
/// <summary>
/// Acceptance for the generator downlevel transform (M7-B): the state machine <c>transformGenerators</c> compiles each
/// generator body into — its labels, its <c>[instruction, argument]</c> completions, the <c>state.trys</c> envelope for
/// protected regions, and the <c>__generator</c> entry point.
/// </summary>
/// <remarks>
/// <para>
/// The fixture builds a real <see cref="Program"/> over the test's own text plus the smallest stub library
/// <c>initializeTypeChecker</c> needs (the shape <c>TypeScriptTransformTests</c> records) and emits it through the
/// landed emit path (<c>Emitter.EmitFiles</c>) with this transform as its only script transformer. That is the smallest
/// thing that stands in for the reference's chain (<c>getTransformers</c>'s named blocker): every fixture below is
/// therefore written so that <c>transformGenerators</c> is the <em>only</em> transformer the reference would apply to
/// it — ES5 syntax plus generators — so the emitted text matches the reference's.
/// </para>
/// <para>
/// PORT-NOTE (what this slice cannot observe): the generator transform is the second half of the reference's
/// generator support. The first half is <c>transformES2015</c> (<c>es2015.ts</c>, a sibling slice), which runs before it
/// at <c>target &lt; ES2015</c> and is what lowers the other ES2015 syntax a generator body may contain (arrow
/// functions, destructuring, spread, <c>let</c>/<c>const</c>, <c>for…of</c>). A fixture that needed it would exercise
/// two transforms, so every fixture here stays inside the ES5+generators subset; the cases the pair would cover
/// jointly are named in this slice's report.
/// </para>
/// <para>
/// PORT-NOTE (the oracle): every expectation below was measured before it was pinned, with
/// <c>ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES5, module: ts.ModuleKind.CommonJS,
/// alwaysStrict: false } })</c> from the reference's own <c>lib/typescript.js</c> (6.0.3) — the same options the
/// fixture below passes. <c>transpileModule</c> runs the reference's whole ES5 chain, so what it answers for these
/// ES5+generators fixtures is exactly what <c>transformGenerators</c> produced.
/// </para>
/// <para>
/// PORT-NOTE (the helper text): the <c>__generator</c> helper's own text is <c>EmitHelpers.GeneratorHelper</c>
/// (landed with the emit-helper slice), so only <c>Simple_YieldEmitsTheHelperAndTheEntryPoint</c> pins it, in full,
/// together with this slice's call to it; the other expectations pin the state machine that follows it
/// (<see cref="Body"/> drops the helper's declaration).
/// </para>
/// </remarks>
public class GeneratorsTransformTests
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
    /// <remarks>PORT-NOTE: <c>verifyCompilerOptions</c>' emit-path check needs <c>getEmitHost</c> (an M7 blocker), so the
    /// options take <c>suppressOutputPathCheck</c> — the flag the reference's own build mode sets and the one
    /// <c>TypeScriptTransformTests</c> takes. The resolver is fetched with <c>forceDts: false</c>, which is what
    /// <c>Program.getEmitResolver</c> does for a normal emit.</remarks>
    private static (Program Program, SourceFile Root, ICompilerOptions Options, IEmitResolver Resolver) CreateProgram(string text)
    {
        var options = new FakeCompilerOptions
        {
            SuppressOutputPathCheck = true,
            StrictNullChecks = true,
            AlwaysStrict = false,
            Target = ScriptTarget.ES5,
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
    /// <remarks>PORT-NOTE: this is the whole emit path with the transform standing in for the chain
    /// <c>getTransformers</c> will assemble, so the substitution hook — <c>onSubstituteNode</c> — runs over every node
    /// the printer reaches, which is what <c>CatchVariable_IsRenamedAndTheReferenceIsSubstituted</c> observes.</remarks>
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
                ScriptTransformers = [GeneratorsTransform.TransformGenerators],
                DeclarationTransformers = [],
            },
            /*emitOnly*/ null,
            /*onlyBuildInfo*/ false,
            /*forceDtsEmit*/ null,
            /*skipBuildInfo*/ null);

        return emitted.Replace("\r\n", "\n");
    }

    /// <summary>The emitted text with the trailing newline the printer ends with removed.</summary>
    private static string Full(string emitted) => emitted.Replace("\r\n", "\n").TrimEnd('\n');

    /// <summary>The fixture's transformed code: everything after the <c>__generator</c> helper declaration.</summary>
    private static string Body(string emitted)
    {
        var lines = emitted.Replace("\r\n", "\n").Split('\n');
        var index = Array.IndexOf(lines, "};");
        Assert.True(index >= 0, "the __generator helper declaration was not emitted");
        return string.Join("\n", lines[(index + 1)..]).TrimEnd('\n');
    }



    /// <summary>The reference's own output for <c>function* f() { var x = yield 1; return x; }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void YieldInAVariableInitializer_AssignsTheSentValue()
        => Assert.Equal(
            """
            function f() { var x; return __generator(this, function (_a) {
                switch (_a.label) {
                    case 0: return [4 /*yield*/, 1];
                    case 1:
                        x = _a.sent();
                        return [2 /*return*/, x];
                }
            }); }
            """,
            Body(Emit("function* f() { var x = yield 1; return x; }\n")));


    /// <summary>The reference's own output for <c>function* f() { for (var i = 0; i < 3; i++) { yield i; } }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void ForLoop_EmitsTheConditionIncrementAndBreakLabels()
        => Assert.Equal(
            """
            function f() { var i; return __generator(this, function (_a) {
                switch (_a.label) {
                    case 0:
                        i = 0;
                        _a.label = 1;
                    case 1:
                        if (!(i < 3)) return [3 /*break*/, 4];
                        return [4 /*yield*/, i];
                    case 2:
                        _a.sent();
                        _a.label = 3;
                    case 3:
                        i++;
                        return [3 /*break*/, 1];
                    case 4: return [2 /*return*/];
                }
            }); }
            """,
            Body(Emit("function* f() { for (var i = 0; i < 3; i++) { yield i; } }\n")));


    /// <summary>The reference's own output for <c>function* f() { try { yield 1; } catch (e) { yield 2; } finally { yield 3; } }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void TryCatchFinally_EmitsTheTrysPushAndTheFinallyCompletion()
        => Assert.Equal(
            """
            function f() { var e_1; return __generator(this, function (_a) {
                switch (_a.label) {
                    case 0:
                        _a.trys.push([0, 2, 4, 6]);
                        return [4 /*yield*/, 1];
                    case 1:
                        _a.sent();
                        return [3 /*break*/, 6];
                    case 2:
                        e_1 = _a.sent();
                        return [4 /*yield*/, 2];
                    case 3:
                        _a.sent();
                        return [3 /*break*/, 6];
                    case 4: return [4 /*yield*/, 3];
                    case 5:
                        _a.sent();
                        return [7 /*endfinally*/];
                    case 6: return [2 /*return*/];
                }
            }); }
            """,
            Body(Emit("function* f() { try { yield 1; } catch (e) { yield 2; } finally { yield 3; } }\n")));


    /// <summary>The reference's own output for <c>function* f(x) { switch (x) { case 1: yield 1; break; case yield 2: yield 3; break; default: yield 4; } }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void SwitchWithAYieldInACaseExpression_SplitsTheClausesIntoRuns()
        => Assert.Equal(
            """
            function f(x) { var _a; return __generator(this, function (_b) {
                switch (_b.label) {
                    case 0:
                        _a = x;
                        switch (_a) {
                            case 1: return [3 /*break*/, 2];
                        }
                        return [4 /*yield*/, 2];
                    case 1:
                        switch (_a) {
                            case _b.sent(): return [3 /*break*/, 4];
                        }
                        return [3 /*break*/, 6];
                    case 2: return [4 /*yield*/, 1];
                    case 3:
                        _b.sent();
                        return [3 /*break*/, 8];
                    case 4: return [4 /*yield*/, 3];
                    case 5:
                        _b.sent();
                        return [3 /*break*/, 8];
                    case 6: return [4 /*yield*/, 4];
                    case 7:
                        _b.sent();
                        _b.label = 8;
                    case 8: return [2 /*return*/];
                }
            }); }
            """,
            Body(Emit("function* f(x) { switch (x) { case 1: yield 1; break; case yield 2: yield 3; break; default: yield 4; } }\n")));


    /// <summary>The reference's own output for <c>function* f() { lbl: { yield 1; break lbl; } }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void LabeledBlock_BreaksToTheEndLabel()
        => Assert.Equal(
            """
            function f() { return __generator(this, function (_a) {
                switch (_a.label) {
                    case 0: return [4 /*yield*/, 1];
                    case 1:
                        _a.sent();
                        return [3 /*break*/, 2];
                    case 2: return [2 /*return*/];
                }
            }); }
            """,
            Body(Emit("function* f() { lbl: { yield 1; break lbl; } }\n")));


    /// <summary>The reference's own output for <c>function* f() { yield* g(); }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void YieldStar_WrapsTheOperandInTheValuesHelper()
        => Assert.Equal(
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
            function f() { return __generator(this, function (_a) {
                switch (_a.label) {
                    case 0: return [5 /*yield**/, __values(g())];
                    case 1:
                        _a.sent();
                        return [2 /*return*/];
                }
            }); }
            """,
            Body(Emit("function* f() { yield* g(); }\n")));


    /// <summary>The reference's own output for <c>function* f() { var a = [1, yield, 2]; }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void ArrayLiteralWithYield_BuildsUpWithConcat()
        => Assert.Equal(
            """
            function f() { var a, _a; return __generator(this, function (_b) {
                switch (_b.label) {
                    case 0:
                        _a = [1];
                        return [4 /*yield*/];
                    case 1:
                        a = _a.concat([_b.sent(), 2]);
                        return [2 /*return*/];
                }
            }); }
            """,
            Body(Emit("function* f() { var a = [1, yield, 2]; }\n")));


    /// <summary>The reference's own output for <c>function* f() { a.b(1, yield, 2); }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void CallArgumentListWithYield_UsesTheApplyBinding()
        => Assert.Equal(
            """
            function f() { var _a, _b, _c; return __generator(this, function (_d) {
                switch (_d.label) {
                    case 0:
                        _b = (_a = a).b;
                        _c = [1];
                        return [4 /*yield*/];
                    case 1:
                        _b.apply(_a, _c.concat([_d.sent(), 2]));
                        return [2 /*return*/];
                }
            }); }
            """,
            Body(Emit("function* f() { a.b(1, yield, 2); }\n")));


    /// <summary>The reference's own output for <c>function* f() { new a.b(1, yield, 2); }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void NewArgumentListWithYield_UsesTheBindBinding()
        => Assert.Equal(
            """
            function f() { var _a, _b, _c; return __generator(this, function (_d) {
                switch (_d.label) {
                    case 0:
                        _b = (_a = a.b).bind;
                        _c = [void 0, 1];
                        return [4 /*yield*/];
                    case 1:
                        new (_b.apply(_a, _c.concat([_d.sent(), 2])))();
                        return [2 /*return*/];
                }
            }); }
            """,
            Body(Emit("function* f() { new a.b(1, yield, 2); }\n")));


    /// <summary>The reference's own output for <c>function* f() { with (o) { yield 1; } }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void WithStatement_ReopensTheWithBlockPerLabel()
        => Assert.Equal(
            """
            function f() { var _a; return __generator(this, function (_b) {
                switch (_b.label) {
                    case 0:
                        _a = o;
                        _b.label = 1;
                    case 1: with (_a) {
                        return [4 /*yield*/, 1];
                    }
                    case 2:
                        with (_a) {
                            _b.sent();
                        }
                        _b.label = 3;
                    case 3: return [2 /*return*/];
                }
            }); }
            """,
            Body(Emit("function* f() { with (o) { yield 1; } }\n")));


    /// <summary>The reference's own output for <c>function* f() { var x = a() && (yield); }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void LogicalAndWithYield_ShortCircuitsOnAFalseyLeft()
        => Assert.Equal(
            """
            function f() { var x, _a; return __generator(this, function (_b) {
                switch (_b.label) {
                    case 0:
                        _a = a();
                        if (!_a) return [3 /*break*/, 2];
                        return [4 /*yield*/];
                    case 1:
                        _a = (_b.sent());
                        _b.label = 2;
                    case 2:
                        x = _a;
                        return [2 /*return*/];
                }
            }); }
            """,
            Body(Emit("function* f() { var x = a() && (yield); }\n")));


    /// <summary>The reference's own output for <c>function* f() { var x = a() || (yield); }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void LogicalOrWithYield_ShortCircuitsOnATruthyLeft()
        => Assert.Equal(
            """
            function f() { var x, _a; return __generator(this, function (_b) {
                switch (_b.label) {
                    case 0:
                        _a = a();
                        if (_a) return [3 /*break*/, 2];
                        return [4 /*yield*/];
                    case 1:
                        _a = (_b.sent());
                        _b.label = 2;
                    case 2:
                        x = _a;
                        return [2 /*return*/];
                }
            }); }
            """,
            Body(Emit("function* f() { var x = a() || (yield); }\n")));


    /// <summary>The reference's own output for <c>function* f() { var x = a() ? (yield) : b(); }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void ConditionalWithYieldInABranch_EmitsTheWhenFalseLabel()
        => Assert.Equal(
            """
            function f() { var x, _a; return __generator(this, function (_b) {
                switch (_b.label) {
                    case 0:
                        if (!a()) return [3 /*break*/, 2];
                        return [4 /*yield*/];
                    case 1:
                        _a = (_b.sent());
                        return [3 /*break*/, 3];
                    case 2:
                        _a = b();
                        _b.label = 3;
                    case 3:
                        x = _a;
                        return [2 /*return*/];
                }
            }); }
            """,
            Body(Emit("function* f() { var x = a() ? (yield) : b(); }\n")));


    /// <summary>The reference's own output for <c>function* f() { var x = (a(), yield, b()); }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void CommaExpressionWithYield_EmitsTheLeftAsAStatement()
        => Assert.Equal(
            """
            function f() { var x; return __generator(this, function (_a) {
                switch (_a.label) {
                    case 0:
                        a();
                        return [4 /*yield*/];
                    case 1:
                        x = (_a.sent(), b());
                        return [2 /*return*/];
                }
            }); }
            """,
            Body(Emit("function* f() { var x = (a(), yield, b()); }\n")));


    /// <summary>The reference's own output for <c>function* f() { while (true) { yield 1; continue; } }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void WhileLoop_EmitsTheLoopAndConditionLabels()
        => Assert.Equal(
            """
            function f() { return __generator(this, function (_a) {
                switch (_a.label) {
                    case 0:
                        if (!true) return [3 /*break*/, 2];
                        return [4 /*yield*/, 1];
                    case 1:
                        _a.sent();
                        return [3 /*break*/, 0];
                    case 2: return [2 /*return*/];
                }
            }); }
            """,
            Body(Emit("function* f() { while (true) { yield 1; continue; } }\n")));


    /// <summary>The reference's own output for <c>function* f() { do { yield 1; } while (x); }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void DoLoop_EmitsTheBodyBeforeTheCondition()
        => Assert.Equal(
            """
            function f() { return __generator(this, function (_a) {
                switch (_a.label) {
                    case 0: return [4 /*yield*/, 1];
                    case 1:
                        _a.sent();
                        _a.label = 2;
                    case 2:
                        if (x) return [3 /*break*/, 0];
                        _a.label = 3;
                    case 3: return [2 /*return*/];
                }
            }); }
            """,
            Body(Emit("function* f() { do { yield 1; } while (x); }\n")));


    /// <summary>The reference's own output for <c>function* f(o) { for (var p in o) { yield p; } }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void ForInWithYield_LowersToTheKeysArrayEnvelope()
        => Assert.Equal(
            """
            function f(o) { var _a, _b, _c, _i, p; return __generator(this, function (_d) {
                switch (_d.label) {
                    case 0:
                        _a = o;
                        _b = [];
                        for (_c in _a)
                            _b.push(_c);
                        _i = 0;
                        _d.label = 1;
                    case 1:
                        if (!(_i < _b.length)) return [3 /*break*/, 4];
                        _c = _b[_i];
                        if (!(_c in _a)) return [3 /*break*/, 3];
                        p = _c;
                        return [4 /*yield*/, p];
                    case 2:
                        _d.sent();
                        _d.label = 3;
                    case 3:
                        _i++;
                        return [3 /*break*/, 1];
                    case 4: return [2 /*return*/];
                }
            }); }
            """,
            Body(Emit("function* f(o) { for (var p in o) { yield p; } }\n")));


    /// <summary>The reference's own output for <c>function* f() { a.b = yield; }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void RightAssociativeAssignmentWithYield_CachesTheTarget()
        => Assert.Equal(
            """
            function f() { var _a; return __generator(this, function (_b) {
                switch (_b.label) {
                    case 0:
                        _a = a;
                        return [4 /*yield*/];
                    case 1:
                        _a.b = _b.sent();
                        return [2 /*return*/];
                }
            }); }
            """,
            Body(Emit("function* f() { a.b = yield; }\n")));


    /// <summary>The reference's own output for <c>function* f() { a[b] = yield; }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void ElementAssignmentWithYield_CachesBothOperands()
        => Assert.Equal(
            """
            function f() { var _a, _b; return __generator(this, function (_c) {
                switch (_c.label) {
                    case 0:
                        _a = a;
                        _b = b;
                        return [4 /*yield*/];
                    case 1:
                        _a[_b] = _c.sent();
                        return [2 /*return*/];
                }
            }); }
            """,
            Body(Emit("function* f() { a[b] = yield; }\n")));


    /// <summary>The reference's own output for <c>function* f() { a += yield; }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void CompoundAssignmentWithYield_ExpandsToTheBaseOperator()
        => Assert.Equal(
            """
            function f() { var _a; return __generator(this, function (_b) {
                switch (_b.label) {
                    case 0:
                        _a = a;
                        return [4 /*yield*/];
                    case 1:
                        a = _a + _b.sent();
                        return [2 /*return*/];
                }
            }); }
            """,
            Body(Emit("function* f() { a += yield; }\n")));


    /// <summary>The reference's own output for <c>function* f() { var x = x[yield]; }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void ElementAccessWithYield_CachesTheReceiver()
        => Assert.Equal(
            """
            function f() { var x, _a; return __generator(this, function (_b) {
                switch (_b.label) {
                    case 0:
                        _a = x;
                        return [4 /*yield*/];
                    case 1:
                        x = _a[_b.sent()];
                        return [2 /*return*/];
                }
            }); }
            """,
            Body(Emit("function* f() { var x = x[yield]; }\n")));


    /// <summary>The reference's own output for <c>function* f() { throw yield; }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void ThrowWithYield_EmitsTheThrowCompletion()
        => Assert.Equal(
            """
            function f() { return __generator(this, function (_a) {
                switch (_a.label) {
                    case 0: return [4 /*yield*/];
                    case 1: throw _a.sent();
                }
            }); }
            """,
            Body(Emit("function* f() { throw yield; }\n")));


    /// <summary>The reference's own output for <c>function* f() { yield; }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void BareYield_EmitsTheYieldWithoutAnArgument()
        => Assert.Equal(
            """
            function f() { return __generator(this, function (_a) {
                switch (_a.label) {
                    case 0: return [4 /*yield*/];
                    case 1:
                        _a.sent();
                        return [2 /*return*/];
                }
            }); }
            """,
            Body(Emit("function* f() { yield; }\n")));


    /// <summary>The reference's own output for <c>function* f() { var x = 1 + (yield) + 2; }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void LeftAssociativeSumWithYield_CachesTheLeftOperand()
        => Assert.Equal(
            """
            function f() { var x, _a; return __generator(this, function (_b) {
                switch (_b.label) {
                    case 0:
                        _a = 1;
                        return [4 /*yield*/];
                    case 1:
                        x = _a + (_b.sent()) + 2;
                        return [2 /*return*/];
                }
            }); }
            """,
            Body(Emit("function* f() { var x = 1 + (yield) + 2; }\n")));


    /// <summary>The reference's own output for <c>function* f() { var g = function* () { yield 1; }; }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void NestedGeneratorFunctionExpression_IsDownleveledInPlace()
        => Assert.Equal(
            """
            function f() { var g; return __generator(this, function (_a) {
                g = function () { return __generator(this, function (_a) {
                    switch (_a.label) {
                        case 0: return [4 /*yield*/, 1];
                        case 1:
                            _a.sent();
                            return [2 /*return*/];
                    }
                }); };
                return [2 /*return*/];
            }); }
            """,
            Body(Emit("function* f() { var g = function* () { yield 1; }; }\n")));


    /// <summary>The reference's own output for <c>function* f() { function inner() { return 1; } yield inner(); }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void FunctionDeclarationInABody_IsHoistedOutOfTheStateMachine()
        => Assert.Equal(
            """
            function f() { function inner() { return 1; } return __generator(this, function (_a) {
                switch (_a.label) {
                    case 0: return [4 /*yield*/, inner()];
                    case 1:
                        _a.sent();
                        return [2 /*return*/];
                }
            }); }
            """,
            Body(Emit("function* f() { function inner() { return 1; } yield inner(); }\n")));


    /// <summary>The reference's own output for <c>function* f() { try { yield 1; } catch (e) { yield e; } }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void CatchVariable_IsRenamedAndTheReferenceIsSubstituted()
        => Assert.Equal(
            """
            function f() { var e_1; return __generator(this, function (_a) {
                switch (_a.label) {
                    case 0:
                        _a.trys.push([0, 2, , 4]);
                        return [4 /*yield*/, 1];
                    case 1:
                        _a.sent();
                        return [3 /*break*/, 4];
                    case 2:
                        e_1 = _a.sent();
                        return [4 /*yield*/, e_1];
                    case 3:
                        _a.sent();
                        return [3 /*break*/, 4];
                    case 4: return [2 /*return*/];
                }
            }); }
            """,
            Body(Emit("function* f() { try { yield 1; } catch (e) { yield e; } }\n")));


    /// <summary>The reference's own output for <c>function* f() { while (true) { break; } yield 1; }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void BreakWithoutYield_IsLeftToTheScriptLoopBlock()
        => Assert.Equal(
            """
            function f() { return __generator(this, function (_a) {
                switch (_a.label) {
                    case 0:
                        while (true) {
                            break;
                        }
                        return [4 /*yield*/, 1];
                    case 1:
                        _a.sent();
                        return [2 /*return*/];
                }
            }); }
            """,
            Body(Emit("function* f() { while (true) { break; } yield 1; }\n")));


    /// <summary>The reference's own output for <c>function* f(x) { switch (x) { default: yield 1; } yield 2; }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void SwitchWithoutYieldInItsCases_UsesTheScriptSwitchBlock()
        => Assert.Equal(
            """
            function f(x) { var _a; return __generator(this, function (_b) {
                switch (_b.label) {
                    case 0:
                        _a = x;
                        return [3 /*break*/, 1];
                    case 1: return [4 /*yield*/, 1];
                    case 2:
                        _b.sent();
                        _b.label = 3;
                    case 3: return [4 /*yield*/, 2];
                    case 4:
                        _b.sent();
                        return [2 /*return*/];
                }
            }); }
            """,
            Body(Emit("function* f(x) { switch (x) { default: yield 1; } yield 2; }\n")));


    /// <summary>The reference's own output for <c>function* f() { outer: while (true) { yield 1; break outer; } }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void NestedLabelIteration_BreaksToTheOuterEnd()
        => Assert.Equal(
            """
            function f() { return __generator(this, function (_a) {
                switch (_a.label) {
                    case 0:
                        if (!true) return [3 /*break*/, 2];
                        return [4 /*yield*/, 1];
                    case 1:
                        _a.sent();
                        return [3 /*break*/, 2];
                    case 2: return [2 /*return*/];
                }
            }); }
            """,
            Body(Emit("function* f() { outer: while (true) { yield 1; break outer; } }\n")));


    /// <summary>The reference's own output for <c>function* f() { }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void EmptyGeneratorBody_EmitsOnlyTheReturnCompletion()
        => Assert.Equal(
            """
            function f() { return __generator(this, function (_a) {
                return [2 /*return*/];
            }); }
            """,
            Body(Emit("function* f() { }\n")));


    /// <summary>The reference's own output for <c>var g = function* () { yield 1; };</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void GeneratorFunctionExpression_IsDownleveledInPlace()
        => Assert.Equal(
            """
            var g = function () { return __generator(this, function (_a) {
                switch (_a.label) {
                    case 0: return [4 /*yield*/, 1];
                    case 1:
                        _a.sent();
                        return [2 /*return*/];
                }
            }); };
            """,
            Body(Emit("var g = function* () { yield 1; };\n")));


    /// <summary>The reference's own output for <c>function* f() { return 1; }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void ReturnValue_EmitsTheReturnCompletionWithTheValue()
        => Assert.Equal(
            """
            function f() { return __generator(this, function (_a) {
                return [2 /*return*/, 1];
            }); }
            """,
            Body(Emit("function* f() { return 1; }\n")));


    /// <summary>The reference's own output for <c>function* f() { yield 1; yield 2; yield 3; }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void SuccessiveYields_EachResumesAtTheNextLabel()
        => Assert.Equal(
            """
            function f() { return __generator(this, function (_a) {
                switch (_a.label) {
                    case 0: return [4 /*yield*/, 1];
                    case 1:
                        _a.sent();
                        return [4 /*yield*/, 2];
                    case 2:
                        _a.sent();
                        return [4 /*yield*/, 3];
                    case 3:
                        _a.sent();
                        return [2 /*return*/];
                }
            }); }
            """,
            Body(Emit("function* f() { yield 1; yield 2; yield 3; }\n")));


    /// <summary>The reference's own output for <c>function* f() { var a = 1, b = yield, c = 2; }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void MultipleInitializedVariables_ShareOneCommaExpression()
        => Assert.Equal(
            """
            function f() { var a, b, c; return __generator(this, function (_a) {
                switch (_a.label) {
                    case 0:
                        a = 1;
                        return [4 /*yield*/];
                    case 1:
                        b = _a.sent(), c = 2;
                        return [2 /*return*/];
                }
            }); }
            """,
            Body(Emit("function* f() { var a = 1, b = yield, c = 2; }\n")));


    /// <summary>The reference's own output for <c>function* f(x) { if (x) { yield 1; } else { yield 2; } }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void IfWithYieldInABranch_EmitsTheElseAndEndLabels()
        => Assert.Equal(
            """
            function f(x) { return __generator(this, function (_a) {
                switch (_a.label) {
                    case 0:
                        if (!x) return [3 /*break*/, 2];
                        return [4 /*yield*/, 1];
                    case 1:
                        _a.sent();
                        return [3 /*break*/, 4];
                    case 2: return [4 /*yield*/, 2];
                    case 3:
                        _a.sent();
                        _a.label = 4;
                    case 4: return [2 /*return*/];
                }
            }); }
            """,
            Body(Emit("function* f(x) { if (x) { yield 1; } else { yield 2; } }\n")));


    /// <summary>The reference's own output for <c>function* f(x) { if (x) { yield 1; } yield 2; }</c>.</summary>
    /// <remarks>PORT-NOTE: measured with <c>ts.transpileModule</c> at <c>target: ES5, module: CommonJS,
    /// alwaysStrict: false</c> (the fixture's options); the helper declaration that precedes it is dropped.</remarks>
    [Fact]
    public void IfWithoutYield_IsEmittedAsAScriptBlock()
        => Assert.Equal(
            """
            function f(x) { return __generator(this, function (_a) {
                switch (_a.label) {
                    case 0:
                        if (!x) return [3 /*break*/, 2];
                        return [4 /*yield*/, 1];
                    case 1:
                        _a.sent();
                        _a.label = 2;
                    case 2: return [4 /*yield*/, 2];
                    case 3:
                        _a.sent();
                        return [2 /*return*/];
                }
            }); }
            """,
            Body(Emit("function* f(x) { if (x) { yield 1; } yield 2; }\n")));


    /// <summary>generators.ts:2591-2610 (<c>createGeneratorResume</c>), :2721-2775 (<c>build</c>), :2624-2631
    /// (<c>emitStatement</c>) — the generator function's own body: the hoisted locals, the
    /// <c>return __generator(this, function (_a) { … })</c> entry point, and the <c>switch (_a.label)</c> state
    /// machine.</summary>
    /// <remarks>PORT-NOTE: this is the one expectation that pins the <c>__generator</c> helper declaration as well,
    /// because calling it is what the transform's entry point does. Measured with <c>ts.transpileModule</c> at the
    /// fixture's options.</remarks>
    [Fact]
    public void Simple_YieldEmitsTheHelperAndTheEntryPoint()
        => Assert.Equal(
            """
            var __generator = (this && this.__generator) || function (thisArg, body) {
                var _ = { label: 0, sent: function() { if (t[0] & 1) throw t[1]; return t[1]; }, trys: [], ops: [] }, f, y, t, g = Object.create((typeof Iterator === "function" ? Iterator : Object).prototype);
                return g.next = verb(0), g["throw"] = verb(1), g["return"] = verb(2), typeof Symbol === "function" && (g[Symbol.iterator] = function() { return this; }), g;
                function verb(n) { return function (v) { return step([n, v]); }; }
                function step(op) {
                    if (f) throw new TypeError("Generator is already executing.");
                    while (g && (g = 0, op[0] && (_ = 0)), _) try {
                        if (f = 1, y && (t = op[0] & 2 ? y["return"] : op[0] ? y["throw"] || ((t = y["return"]) && t.call(y), 0) : y.next) && !(t = t.call(y, op[1])).done) return t;
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
            };
            function f() { return __generator(this, function (_a) {
                switch (_a.label) {
                    case 0: return [4 /*yield*/, 1];
                    case 1:
                        _a.sent();
                        return [2 /*return*/];
                }
            }); }
            """,
            Full(Emit("function* f() { yield 1; }\n")));


    /// <summary>generators.ts:1137-1195 (<c>visitObjectLiteralExpression</c>) — an object literal containing a
    /// <c>yield</c>.</summary>
    /// <remarks>PORT-NOTE (the named blocker): the reference's <c>reduceProperty</c> calls
    /// <c>createExpressionForObjectLiteralElementLike</c> (factory/utilities.ts:465-480), which is not ported —
    /// <c>FactoryUtilities</c>'s header records the omission, whose blocking dependency is
    /// <c>getAllAccessorDeclarations</c> (utilities.ts) — so the port's call site is a loud
    /// <see cref="NotSupportedException"/> rather than a silently wrong object literal. This is the acceptance for that
    /// failure; the emit the successful path owes is pinned beside it, measured from the reference.</remarks>
    [Fact]
    public void ObjectLiteralWithYield_FailsLoudlyUntilItsBlockerLands()
    {
        var error = Assert.Throws<NotSupportedException>(() => Emit("function* f() { var o = { a: 1, b: yield, c: 2 }; }\n"));

        Assert.Contains("createExpressionForObjectLiteralElementLike (factory/utilities.ts:465)", error.Message);
        Assert.Contains("getAllAccessorDeclarations", error.Message);
    }

    /// <summary>The emit the blocked path owes, measured from the reference.</summary>
    /// <remarks>PORT-NOTE: a constant rather than an assertion, because the port cannot reach this path until
    /// <c>createExpressionForObjectLiteralElementLike</c> lands; it is what that test must assert once it can.</remarks>
    private const string ReferenceOutputForAnObjectLiteralWithYield =
            """
            function f() { var o, _a; return __generator(this, function (_b) {
                switch (_b.label) {
                    case 0:
                        _a = { a: 1 };
                        return [4 /*yield*/];
                    case 1:
                        o = (_a.b = _b.sent(), _a.c = 2, _a);
                        return [2 /*return*/];
                }
            }); }
            """;
}
