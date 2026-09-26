namespace TypeScript.Compiler.Tests;

// ported from: src/compiler/program.ts:1514-1875 (createProgram end to end), 2688-2712, 2650-2666, 2827-2830 (the M7 boundary)
/// <summary>
/// The M6-1 acceptance proof: <c>createProgram</c> runs end to end over a file with an injected host and produces a
/// bound source file, and the M7 boundary is loud.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS binds a program's files when the checker is created (<c>getTypeChecker</c>), and the port's checker
/// factory (<c>Checker.CreateTypeChecker</c>) is M6-2's, so these tests bind the root file through the binder's own
/// entry point (<c>Binder.BindSourceFile</c>) — the same code the checker's initialization will call — and assert the
/// symbols and locals it wrote. When M6-2 lands, <c>Program.GetTypeChecker()</c> is the way to reach the same state and
/// this comment is the record of why the test takes the binder directly.
/// </remarks>
public class ProgramTests
{
    private static FakeSystem CreateSystemWithRootFile(string text)
    {
        var system = new FakeSystem();
        system.Files["/a.ts"] = text;
        return system;
    }

    /// <summary>
    /// Registers the stub library under the name this host actually asks for.
    /// </summary>
    /// <remarks>
    /// The host's default library name is <c>combinePaths(getDefaultLibLocation(), getDefaultLibFileName(options))</c>
    /// (program.ts:475), and <c>getDefaultLibLocation()</c> is
    /// <c>getDirectoryPath(normalizePath(system.getExecutingFilePath()))</c> (program.ts:467) — with
    /// <see cref="FakeSystem"/>'s empty executing path that is <c>""</c>, so the name is the *relative* library name
    /// (<c>lib.es2025.full.d.ts</c>) even though the root file in these tests is absolute. Laying the stub down at
    /// <c>"/" + name</c> would leave the program's default-lib lookup unresolved, which is exactly what
    /// <c>getMissingFilePaths()</c> then reports: the host is asked, answers no, and the program records the miss.
    /// </remarks>
    private static void AddStubDefaultLib(FakeSystem system, ICompilerHost host, ICompilerOptions options)
        => system.Files[host.GetDefaultLibFileName(options)] = "declare var undefined: undefined;\n";

    private static (Program Program, ICompilerHost Host, FakeSystem System, FakeCompilerOptions Options) CreateProgramOverOneFile(string text)
    {
        // PORT-NOTE: `verifyCompilerOptions`'s emit-path check calls `Program.GetEmitHost` and
        // `Program.ForEachEmittedFile`, both M7's loud blockers (`Program/Program.EmitBoundary.cs`), so a program over
        // default options cannot finish `createProgram` until M7 lands. The tests take the option that skips exactly
        // that check — `suppressOutputPathCheck`, the flag the reference's own build mode sets — and nothing else about
        // the file graph or the queries below changes.
        var options = new FakeCompilerOptions { SuppressOutputPathCheck = true };
        var system = CreateSystemWithRootFile(text);
        var host = Program.CreateCompilerHostWorker(options, /*setParentNodes*/ false, system);
        // The default library file the host asks for; a program with a root file always pulls one in unless noLib is set.
        AddStubDefaultLib(system, host, options);
        var program = Program.CreateProgram(["/a.ts"], options, host);
        return (program, host, system, options);
    }

    [Fact]
    public void CreateProgramFindsParsesAndBindsTheRootFile()
    {
        const string text = "const x: number = 1;\nfunction f(p: string) { return p; }\n";
        var (program, _, _, options) = CreateProgramOverOneFile(text);

        var sourceFile = Assert.Single(program.GetSourceFiles(), file => (file.FileName ?? "").EndsWith("a.ts", StringComparison.Ordinal));
        Assert.Equal(text, sourceFile.Text);

        // The parse produced the two statements, and the program's syntactic diagnostics are empty for well-formed input.
        Assert.Equal(2, sourceFile.Statements!.Count);
        Assert.Empty(program.GetSyntacticDiagnostics(sourceFile, null));

        // Binding: the file's locals hold the two declared names and the declarations carry their symbols.
        Binder.BindSourceFile(sourceFile, options);
        Assert.NotNull(sourceFile.Locals);
        Assert.Contains("x", sourceFile.Locals!.Keys);
        Assert.Contains("f", sourceFile.Locals.Keys);

        var firstStatement = (VariableStatement)sourceFile.Statements[0];
        var declaration = firstStatement.DeclarationList!.Declarations![0];
        Assert.NotNull(declaration.Symbol);
        Assert.Equal("x", declaration.Symbol!.EscapedName);
        Assert.Same(declaration, declaration.Symbol.Declarations![0]);
    }

    [Fact]
    public void CreateProgramWithoutAHostThrows()
    {
        var options = new FakeCompilerOptions();
        var exception = Assert.Throws<Program.HostRequiredException>(
            () => Program.CreateProgram(new CreateProgramOptions { RootNames = ["/a.ts"], Options = options }));
        Assert.Contains("no `sys` singleton", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitAndTheDeclarationDiagnosticsAreTheM7Boundary()
    {
        var (program, _, _, _) = CreateProgramOverOneFile("const x = 1;\n");

        // Each boundary member names itself and the milestone in the failure, so a caller cannot mistake it for a
        // silently empty result. These assertions turn into the emit tests when M7 lands, not into deleted tests.
        foreach (var (what, action) in new (string, Action)[]
        {
            ("emit (program.ts:2688)", () => program.Emit()),
            ("emitBuildInfo (program.ts:2650)", () => program.EmitBuildInfo()),
            ("getDeclarationDiagnostics (program.ts:2827)", () => program.GetDeclarationDiagnostics()),
            ("getEmitHost (program.ts:2598)", () => program.GetEmitHost()),
            // The three `emitter.ts` members `program.ts`'s own verify path calls (emitter.ts:446/481/563); their
            // arguments are never read, because the throw is the whole body.
            ("forEachEmittedFile (emitter.ts:446)", () => Program.ForEachEmittedFile(null!, static (_, _) => { })),
            ("getTsBuildInfoEmitOutputFilePath (emitter.ts:481)", () => Program.GetTsBuildInfoEmitOutputFilePath(new FakeCompilerOptions())),
            ("getOutputDeclarationFileName (emitter.ts:563)", () => Program.GetOutputDeclarationFileName("a.ts", null!, false)),
        })
        {
            var exception = Assert.Throws<NotSupportedException>(action);
            Assert.StartsWith(what, exception.Message, StringComparison.Ordinal);
            Assert.Contains("M7", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ResolveConfigFileProjectNameIsTheM8Boundary()
    {
        // `resolveConfigFileProjectName` is `tsbuild.ts`'s (M8's builder); `Program.ResolveProjectReferencePath` is its
        // one program-side caller. The failure names the member, its reference line and the milestone, so M8 replaces
        // the throw with the reference's body instead of having to hunt for a silent stand-in.
        var exception = Assert.Throws<NotSupportedException>(() => { TsBuild.ResolveConfigFileProjectName("./tsconfig.json"); });
        Assert.StartsWith("resolveConfigFileProjectName (tsbuild.ts:177)", exception.Message, StringComparison.Ordinal);
        Assert.Contains("M8", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheProgramsOwnQueriesAnswerFromTheFileGraph()
    {
        var (program, _, _, _) = CreateProgramOverOneFile("export const x = 1;\n");

        Assert.Equal(["/a.ts"], program.GetRootFileNames());
        Assert.Contains("/a.ts", program.GetSourceFiles().Select(file => file.FileName));
        Assert.False(program.IsSourceFileFromExternalLibrary(program.GetSourceFiles()[0]));
        Assert.Empty(program.GetMissingFilePaths());
        Assert.Equal(StructureIsReused.Not, program._structureIsReused);
    }
}
