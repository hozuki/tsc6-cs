#nullable enable

namespace TypeScript.Compiler.Tests;

// ported from: src/compiler/transformers/es2020.ts:42-267
/// <summary>
/// Acceptance for the ES2020 downlevel transform (M7-B): nullish coalescing and optional chaining, the temps it
/// hoists, the <c>this</c> captures it wraps in a synthetic reference, and the <c>delete</c> form it answers.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (the oracle): every expectation below was measured with the reference's own
/// <c>transformNodes</c>/<c>createPrinter</c> driven by exactly the transformer list this fixture drives —
/// <c>transformTypeScript</c> then <c>transformES2020</c> — from
/// <c>D:/Source/GitHub/TypeScript/lib/typescript.js</c> (6.0.3) at <c>target: ES2019</c>, <c>module: ESNext</c>,
/// <c>alwaysStrict: false</c>, <c>noEmitHelpers: true</c>. <c>target: ES2019</c> is what makes the reference's chain
/// include <c>transformES2020</c> at all (the ES2020 edition gates it off).
/// </para>
/// <para>
/// PORT-NOTE (the chain): <c>ts.transpileModule</c> at the same options adds <c>transformES2021</c>,
/// <c>transformESDecorators</c>, <c>transformClassFields</c> and the module transformer; every expectation below is
/// byte-identical under that fuller chain, since no fixture here holds a logical assignment, decorator, class field or
/// module syntax.
/// </para>
/// </remarks>
public class ES2020TransformTests
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
            Target = ScriptTarget.ES2019,
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
    /// <remarks>PORT-NOTE (<c>noEmitHelpers</c>): the expectations below are the reference's own <c>transformNodes</c> output for this chain with <c>noEmitHelpers: true</c>; <c>es2020.ts</c> requests no emit helper, so the flag changes nothing here and the text is what <c>ts.transpileModule</c> emits for the same options too.</remarks>
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
                ScriptTransformers = [TypeScriptTransform.TransformTypeScript, ES2020Transform.TransformES2020],
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

    /// <summary>`const r = a ?? b;` — measured through the reference's own `transformTypeScript` + `transformES2020` at `target: ES2019`.</summary>
    [Fact]
    public void NullishCoalescing_OnASimpleCopiableLeft_RepeatsTheLeft()
        => AssertEmitted(
            """
            const r = a !== null && a !== void 0 ? a : b;
            """,
            Emit("""
                const r = a ?? b;
                """));

    /// <summary>`f() ?? g();` — measured through the reference's own `transformTypeScript` + `transformES2020` at `target: ES2019`.</summary>
    [Fact]
    public void NullishCoalescing_OnANonSimpleLeft_HoistsItIntoATemp()
        => AssertEmitted(
            """
            var _a;
            (_a = f()) !== null && _a !== void 0 ? _a : g();
            """,
            Emit("""
                f() ?? g();
                """));

    /// <summary>`a.b ?? c;` — measured through the reference's own `transformTypeScript` + `transformES2020` at `target: ES2019`.</summary>
    [Fact]
    public void NullishCoalescing_OnAMemberAccess_HoistsTheMember()
        => AssertEmitted(
            """
            var _a;
            (_a = a.b) !== null && _a !== void 0 ? _a : c;
            """,
            Emit("""
                a.b ?? c;
                """));

    /// <summary>`a?.b;` — measured through the reference's own `transformTypeScript` + `transformES2020` at `target: ES2019`.</summary>
    [Fact]
    public void OptionalPropertyAccess_OnASimpleCopiableLeft_GuardsWithTwoStrictComparisons()
        => AssertEmitted(
            """
            a === null || a === void 0 ? void 0 : a.b;
            """,
            Emit("""
                a?.b;
                """));

    /// <summary>`a?.[b];` — measured through the reference's own `transformTypeScript` + `transformES2020` at `target: ES2019`.</summary>
    [Fact]
    public void OptionalElementAccess_GuardsTheElementAccess()
        => AssertEmitted(
            """
            a === null || a === void 0 ? void 0 : a[b];
            """,
            Emit("""
                a?.[b];
                """));

    /// <summary>`a?.();` — measured through the reference's own `transformTypeScript` + `transformES2020` at `target: ES2019`.</summary>
    [Fact]
    public void OptionalCall_GuardsTheCall()
        => AssertEmitted(
            """
            a === null || a === void 0 ? void 0 : a();
            """,
            Emit("""
                a?.();
                """));

    /// <summary>`a?.b();` — measured through the reference's own `transformTypeScript` + `transformES2020` at `target: ES2019`.</summary>
    [Fact]
    public void OptionalMethodCall_GuardsTheMethodCall()
        => AssertEmitted(
            """
            a === null || a === void 0 ? void 0 : a.b();
            """,
            Emit("""
                a?.b();
                """));

    /// <summary>`a.b?.c;` — measured through the reference's own `transformTypeScript` + `transformES2020` at `target: ES2019`.</summary>
    [Fact]
    public void OptionalAccess_AfterAPlainMember_HoistsTheMemberIntoATemp()
        => AssertEmitted(
            """
            var _a;
            (_a = a.b) === null || _a === void 0 ? void 0 : _a.c;
            """,
            Emit("""
                a.b?.c;
                """));

    /// <summary>`a?.b.c;` — measured through the reference's own `transformTypeScript` + `transformES2020` at `target: ES2019`.</summary>
    [Fact]
    public void PlainMemberAccess_AfterAnOptionalChain_ExtendsTheChainedConditional()
        => AssertEmitted(
            """
            a === null || a === void 0 ? void 0 : a.b.c;
            """,
            Emit("""
                a?.b.c;
                """));

    /// <summary>`delete a?.b;` — measured through the reference's own `transformTypeScript` + `transformES2020` at `target: ES2019`.</summary>
    [Fact]
    public void DeleteOfAnOptionalChain_AnswersTrueInTheShortCircuitArm()
        => AssertEmitted(
            """
            a === null || a === void 0 ? true : delete a.b;
            """,
            Emit("""
                delete a?.b;
                """));

    /// <summary>`a?.b?.c;` — measured through the reference's own `transformTypeScript` + `transformES2020` at `target: ES2019`.</summary>
    [Fact]
    public void TwoOptionalLinks_ChainTwoConditionalsThroughATemp()
        => AssertEmitted(
            """
            var _a;
            (_a = a === null || a === void 0 ? void 0 : a.b) === null || _a === void 0 ? void 0 : _a.c;
            """,
            Emit("""
                a?.b?.c;
                """));

    /// <summary>`(a?.b)();` — measured through the reference's own `transformTypeScript` + `transformES2020` at `target: ES2019`.</summary>
    [Fact]
    public void CallOfAParenthesizedOptionalChain_BecomesACallWithTheCapturedThisArg()
        => AssertEmitted(
            """
            (a === null || a === void 0 ? void 0 : a.b).call(a);
            """,
            Emit("""
                (a?.b)();
                """));

    /// <summary>`a?.b().c;` — measured through the reference's own `transformTypeScript` + `transformES2020` at `target: ES2019`.</summary>
    [Fact]
    public void MemberAccess_AfterAnOptionalCall_ExtendsTheChainedConditional()
        => AssertEmitted(
            """
            a === null || a === void 0 ? void 0 : a.b().c;
            """,
            Emit("""
                a?.b().c;
                """));

    /// <summary>`f?.(1, 2);` — measured through the reference's own `transformTypeScript` + `transformES2020` at `target: ES2019`.</summary>
    [Fact]
    public void OptionalCall_KeepsItsArgumentList()
        => AssertEmitted(
            """
            f === null || f === void 0 ? void 0 : f(1, 2);
            """,
            Emit("""
                f?.(1, 2);
                """));

    /// <summary>`a?.[b]?.();` — measured through the reference's own `transformTypeScript` + `transformES2020` at `target: ES2019`.</summary>
    [Fact]
    public void OptionalCall_AfterAnOptionalElement_CapturesTheElementAsThisArg()
        => AssertEmitted(
            """
            var _a;
            (_a = a === null || a === void 0 ? void 0 : a[b]) === null || _a === void 0 ? void 0 : _a.call(a);
            """,
            Emit("""
                a?.[b]?.();
                """));

    /// <summary>`a?.b ?? c;` — measured through the reference's own `transformTypeScript` + `transformES2020` at `target: ES2019`.</summary>
    [Fact]
    public void NullishCoalescing_OnAnOptionalChain_HoistsTheChainedConditional()
        => AssertEmitted(
            """
            var _a;
            (_a = a === null || a === void 0 ? void 0 : a.b) !== null && _a !== void 0 ? _a : c;
            """,
            Emit("""
                a?.b ?? c;
                """));

    /// <summary>`(a ?? b) ?? c;` — measured through the reference's own `transformTypeScript` + `transformES2020` at `target: ES2019`.</summary>
    [Fact]
    public void NestedNullishCoalescing_HoistsTheInnerConditional()
        => AssertEmitted(
            """
            var _a;
            (_a = (a !== null && a !== void 0 ? a : b)) !== null && _a !== void 0 ? _a : c;
            """,
            Emit("""
                (a ?? b) ?? c;
                """));

    /// <summary>`delete (a?.b).c;` — measured through the reference's own `transformTypeScript` + `transformES2020` at `target: ES2019`.</summary>
    [Fact]
    public void DeleteOfANonOptionalAccess_OverAnOptionalChain_OnlyRewritesTheChain()
        => AssertEmitted(
            """
            delete (a === null || a === void 0 ? void 0 : a.b).c;
            """,
            Emit("""
                delete (a?.b).c;
                """));

    /// <summary>`a[b?.c] ?? d;` — measured through the reference's own `transformTypeScript` + `transformES2020` at `target: ES2019`.</summary>
    [Fact]
    public void NullishCoalescing_WhoseOperandsContainAChain_VisitsTheElementArgument()
        => AssertEmitted(
            """
            var _a;
            (_a = a[b === null || b === void 0 ? void 0 : b.c]) !== null && _a !== void 0 ? _a : d;
            """,
            Emit("""
                a[b?.c] ?? d;
                """));

    /// <summary>`a?.b === void 0;` — measured through the reference's own `transformTypeScript` + `transformES2020` at `target: ES2019`.</summary>
    [Fact]
    public void OptionalChain_AsAComparisonOperand_IsParenthesizedByThePrinter()
        => AssertEmitted(
            """
            (a === null || a === void 0 ? void 0 : a.b) === void 0;
            """,
            Emit("""
                a?.b === void 0;
                """));

}
