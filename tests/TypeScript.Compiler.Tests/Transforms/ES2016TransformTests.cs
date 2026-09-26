namespace TypeScript.Compiler.Tests;

// ported from: src/compiler/transformers/es2016.ts:21-122
/// <summary>
/// Acceptance for the ES2016 downlevel transform (M7-B): the exponentiation operator and its assignment form.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (the oracle): every expectation below was measured with the reference's own compiler — <c>node
/// lib/tsc.js</c> from <c>D:/Source/GitHub/TypeScript</c> (6.0.3) over a one-file program — at the fixture's own options:
/// <c>--target ES2015 --module commonjs --alwaysStrict false --strictNullChecks true</c>. <c>ts.transpileModule</c>
/// cannot model these fixtures: it forces <c>isolatedModules = true</c> (<c>lib/typescript.js</c>'s
/// <c>transpileOptionValueCompilerOptions</c>), while the fixture's own options leave it unset — the same reason the
/// fixtures are measured with <c>tsc</c> rather than through the transpile API.
/// </para>
/// <para>
/// PORT-NOTE (the chain): <c>transformES2016</c> is appended by <c>getScriptTransformers</c> when
/// <c>languageVersion &lt; ScriptTarget.ES2016</c>, so the target below is ES2015 — the one target at which this
/// transform is the only ES2016–ES2019 transform that acts (ES2017/ES2018/ES2019 are also in the chain but none of them
/// reads a <c>**</c>/<c>**=</c> node). The fixture still drives <c>TypeScriptTransform.TransformTypeScript</c> ahead of
/// it, which is the reference chain's own order (<c>transformer.ts:138-140</c>) and is a no-op for the fixtures here.
/// </para>
/// </remarks>
public class ES2016TransformTests
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
            Target = ScriptTarget.ES2015,
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
                ScriptTransformers = [TypeScriptTransform.TransformTypeScript, ES2016Transform.TransformES2016],
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

    /// <remarks>PORT-NOTE: the nested case pins the reference's right associativity — <c>2 ** 3 ** 4</c> parses as
    /// <c>2 ** (3 ** 4)</c>, so the inner call is the right operand of the outer one.</remarks>
    [Fact]
    public void ExponentiationOperator_BecomesAGlobalPowCall()
        => AssertEmitted(
            """
var x = Math.pow(2, 3);
var y = Math.pow(2, Math.pow(3, 4));
""",
            Emit("var x = 2 ** 3;\nvar y = 2 ** 3 ** 4;\n"));

    /// <remarks>PORT-NOTE: the property-access target is evaluated once into a hoisted temporary, so <c>a</c> is read
    /// once and the <c>var _a;</c> declaration is hoisted to the top of the file.</remarks>
    [Fact]
    public void ExponentiationAssignmentOnAPropertyAccess_ReusesTheReceiver()
        => AssertEmitted(
            """
var _a;
var a = {}, b = 1;
(_a = a).x = Math.pow(_a.x, b);
""",
            Emit("var a = {}, b = 1;\na.x **= b;\n"));

    /// <remarks>PORT-NOTE: the element-access target evaluates <em>both</em> the receiver and the argument expression
    /// into hoisted temporaries, and the argument expression is assigned in the index position so the index is computed
    /// once.</remarks>
    [Fact]
    public void ExponentiationAssignmentOnAnElementAccess_HoistsBothTemporaries()
        => AssertEmitted(
            """
var _a, _b;
var a = [], i = function () { return 0; }, b = 1;
(_a = a)[_b = i()] = Math.pow(_a[_b], b);
""",
            Emit("var a = [], i = function () { return 0; }, b = 1;\na[i()] **= b;\n"));
}
