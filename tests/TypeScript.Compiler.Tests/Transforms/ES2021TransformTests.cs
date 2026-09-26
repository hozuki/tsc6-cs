#nullable enable

namespace TypeScript.Compiler.Tests;

// ported from: src/compiler/transformers/es2021.ts:25-106
/// <summary>
/// Acceptance for the ES2021 downlevel transform (M7-B): the logical assignment operators it rewrites
/// (<c>&amp;&amp;=</c>, <c>||=</c>, <c>??=</c>) and the operands it hoists into temps, all measured against the
/// reference's own emit.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (the oracle): every expectation below was measured with the reference's own
/// <c>transformNodes</c>/<c>createPrinter</c> driven by exactly the transformer list this fixture drives —
/// <c>transformTypeScript</c> then <c>transformES2021</c> — from
/// <c>D:/Source/GitHub/TypeScript/lib/typescript.js</c> (6.0.3) at <c>target: ES2020</c>, <c>module: ESNext</c>,
/// <c>alwaysStrict: false</c>, <c>noEmitHelpers: true</c>. <c>target: ES2020</c> is what makes the reference's chain
/// include <c>transformES2021</c> at all (the ES2021 edition gates it off), which is also why the transform's own
/// module stores no target check.
/// </para>
/// <para>
/// PORT-NOTE (the chain): <c>ts.transpileModule</c> at the same options runs the same two transformers plus
/// <c>transformESDecorators</c>, <c>transformClassFields</c> and the module transformer; every expectation below is
/// byte-identical under that fuller chain, since none of those transformers touches a fixture without decorators,
/// class fields or module syntax.
/// </para>
/// </remarks>
public class ES2021TransformTests
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
    /// <remarks>PORT-NOTE (<c>noEmitHelpers</c>): the expectations below are the reference's own <c>transformNodes</c> output for this chain with <c>noEmitHelpers: true</c>; <c>es2021.ts</c> requests no emit helper, so the flag changes nothing here and the text is what <c>ts.transpileModule</c> emits for the same options too.</remarks>
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
                ScriptTransformers = [TypeScriptTransform.TransformTypeScript, ES2021Transform.TransformES2021],
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

    /// <summary>`a ||= b;` becomes `a || (a = b);`.</summary>
    [Fact]
    public void OrEquals_BecomesALogicalOrOfTheOriginalValueAndAnAssignment()
        => AssertEmitted(
            """
            a || (a = b);
            """,
            Emit("""
                a ||= b;
                """));

    /// <summary>`a.b ||= c;` becomes `a.b || (a.b = c);`.</summary>
    [Fact]
    public void MemberOrEquals_BecomesALogicalOrOfTheMemberAndAnAssignmentToIt()
        => AssertEmitted(
            """
            a.b || (a.b = c);
            """,
            Emit("""
                a.b ||= c;
                """));

    /// <summary>`a[b] ??= c;` becomes `a[b] ?? (a[b] = c);`.</summary>
    [Fact]
    public void ElementNullishEquals_BecomesANullishOrOfTheElementAndAnAssignmentToIt()
        => AssertEmitted(
            """
            a[b] ?? (a[b] = c);
            """,
            Emit("""
                a[b] ??= c;
                """));

    /// <summary>`a &&= b.c();` becomes `a && (a = b.c());`.</summary>
    [Fact]
    public void AndEquals_BecomesALogicalAndOfTheOriginalValueAndAnAssignment()
        => AssertEmitted(
            """
            a && (a = b.c());
            """,
            Emit("""
                a &&= b.c();
                """));

    /// <summary>`a ||= (b, c);` becomes `a || (a = (b, c));`.</summary>
    [Fact]
    public void OrEqualsSequence_KeepsTheRightHandSequenceParenthesized()
        => AssertEmitted(
            """
            a || (a = (b, c));
            """,
            Emit("""
                a ||= (b, c);
                """));

    /// <summary>`a.b ||= c, d;` becomes `a.b || (a.b = c), d;` — the comma expression's own operand.</summary>
    [Fact]
    public void OrEqualsComma_LeavesTheEnclosingCommaExpressionAlone()
        => AssertEmitted(
            """
            a.b || (a.b = c), d;
            """,
            Emit("""
                a.b ||= c, d;
                """));

    /// <summary>`obj.get().x ||= f();` hoists the receiver into a temp: the object of the read and of the assignment is the same `_a`.</summary>
    [Fact]
    public void NonSimpleReceiver_IsHoistedIntoATempAndReadTwice()
        => AssertEmitted(
            """
            var _a;
            (_a = obj.get()).x || (_a.x = f());
            """,
            Emit("""
                obj.get().x ||= f();
                """));

    /// <summary>`a.b.c ||= d;` copies `a.b` into a temp, so `a.b` is evaluated once.</summary>
    [Fact]
    public void DeepMember_ReadsTheInnermostObjectTwiceThroughATemp()
        => AssertEmitted(
            """
            var _a;
            (_a = a.b).c || (_a.c = d);
            """,
            Emit("""
                a.b.c ||= d;
                """));

    /// <summary>`a[getKey()] ??= v;` assigns the argument inside the access: `a[_a = getKey()] ?? (a[_a] = v)`.</summary>
    [Fact]
    public void NonSimpleElementArgument_IsAssignedInsideTheElementAccess()
        => AssertEmitted(
            """
            var _a;
            a[_a = getKey()] ?? (a[_a] = v);
            """,
            Emit("""
                a[getKey()] ??= v;
                """));

    /// <summary>`a[0] += 1;` is not a logical assignment; the visitor's own transform-flag gate leaves it alone.</summary>
    [Fact]
    public void OtherCompoundAssignments_AreUnchanged()
        => AssertEmitted(
            """
            a[0] += 1;
            """,
            Emit("""
                a[0] += 1;
                """));

    /// <summary>`const r = a ?? b;` carries no `ContainsES2021` flag, so the visitor returns it as it stands.</summary>
    [Fact]
    public void OtherNullishExpressions_AreUnchanged()
        => AssertEmitted(
            """
            const r = a ?? b;
            """,
            Emit("""
                const r = a ?? b;
                """));

}
