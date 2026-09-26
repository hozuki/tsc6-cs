namespace TypeScript.Compiler.Tests;

// ported from: src/compiler/transformers/es2019.ts:15-51
/// <summary>
/// Acceptance for the ES2019 downlevel transform (M7-B): the optional <c>catch</c> binding and its generated binding.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (the oracle): every expectation below was measured with the reference's own compiler — <c>node
/// lib/tsc.js</c> from <c>D:/Source/GitHub/TypeScript</c> (6.0.3) over a one-file program — at the fixture's own options:
/// <c>--target ES2018 --module commonjs --alwaysStrict false --strictNullChecks true</c>. <c>ts.transpileModule</c>
/// cannot model this fixture: it forces <c>isolatedModules = true</c> (<c>lib/typescript.js</c>'s
/// <c>transpileOptionValueCompilerOptions</c>), which changes <c>isEffectiveStrictModeSourceFile</c> and with it the
/// strictness-dependent parts of the emit, while this fixture's own options leave it unset — the same reason the
/// fixture is measured with <c>tsc</c> rather than through the transpile API.
/// </para>
/// <para>
/// PORT-NOTE (the chain): <c>transformES2019</c> is appended by <c>getScriptTransformers</c> when
/// <c>languageVersion &lt; ScriptTarget.ES2019</c>, so the target below is ES2018; the only other transformer the
/// reference's chain holds for this target that could touch the fixture is <c>transformES2018</c>, whose
/// <c>visitCatchClause</c> returns the clause unchanged when it has no variable declaration
/// (<c>es2018.ts:643-647</c>) — which is exactly the shape under test, so this transform is the only one that acts.
/// </para>
/// </remarks>
public class ES2019TransformTests
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
            Target = ScriptTarget.ES2018,
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
                ScriptTransformers = [TypeScriptTransform.TransformTypeScript, ES2019Transform.TransformES2019],
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

    [Fact]
    public void OptionalCatchBinding_GetsAGeneratedBinding()
        => AssertEmitted(
            """
try {
    f();
}
catch (_a) {
    g();
}
""",
            Emit("try { f(); } catch { g(); }\n"));

    /// <remarks>PORT-NOTE: the control fixture — a <c>catch</c> clause that already has a binding is emitted
    /// unchanged, which is the arm <c>visitCatchClause</c> takes when <c>node.variableDeclaration</c> is present.</remarks>
    [Fact]
    public void CatchClauseWithABinding_IsEmittedUnchanged()
        => AssertEmitted(
            """
try {
    f();
}
catch (e) {
    g(e);
}
""",
            Emit("try { f(); } catch (e) { g(e); }\n"));
}
