namespace TypeScript.Compiler.Tests;

// ported from: src/compiler/program.ts:1514-1875 (createProgram end to end), 2688-2712, 2650-2666, 2827-2830 (the emit
// entry points) and src/compiler/emitter.ts:446-474, 481-497, 563-570 (the emit-path queries' bodies)
/// <summary>
/// The M6-1 acceptance proof: <c>createProgram</c> runs end to end over a file with an injected host and produces a
/// bound source file — plus the M7-B proof that the emit surface is real and the transformer chain is its named
/// blocker.
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

    private static (Program Program, ICompilerHost Host, FakeSystem System, FakeCompilerOptions Options) CreateProgramOverOneFile(string text, bool suppressOutputPathCheck = true)
    {
        // PORT-NOTE: `verifyCompilerOptions`'s emit-path check calls `Program.GetEmitHost` and
        // `Program.ForEachEmittedFile`, which M6 could not reach (`Program/Program.EmitBoundary.cs` was loud then).
        // Both are real now — `TheEmitSurfaceIsRealAndTheTransformerChainIsAssembled` runs the check unskipped —
        // and the default here keeps the flag for the file-graph tests, which are about the graph rather than the
        // output-path check the reference's own build mode also suppresses.
        var options = new FakeCompilerOptions { SuppressOutputPathCheck = suppressOutputPathCheck };
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
    public void TheEmitSurfaceIsRealAndTheTransformerChainIsAssembled()
    {
        var (program, _, _, options) = CreateProgramOverOneFile("const x = 1;\n");

        // The emit host is the object the emitter runs against: it answers the program's own queries, and a
        // createProgram program has no build info (TS's `program.getBuildInfo?.()` is undefined until a builder
        // assigns it).
        var host = program.GetEmitHost();
        // PORT-NOTE: `IEmitHost` inherits `GetCompilerOptions`/`GetCurrentDirectory`/`GetSourceFile` from both
        // `IScriptReferenceHost` and `ISourceFileMayBeEmittedHost` (types.ts declares them on each), so a call through
        // the derived interface is ambiguous in C#; the reference reads the one member, and the port picks a base
        // interface — the same qualification `Program.EmitHost`'s own members use.
        Assert.Same(options, ((IScriptReferenceHost)host).GetCompilerOptions());
        Assert.Same(program.GetSourceFile("/a.ts"), host.GetSourceFile("/a.ts"));
        Assert.False(host.IsEmitBlocked("/a.js"));
        Assert.Null(host.GetBuildInfo());
        Assert.EndsWith(Paths.DirectorySeparator, host.GetCommonSourceDirectory(), StringComparison.Ordinal);

        // `forEachEmittedFile` walks the source files that may be emitted (the default library is a `.d.ts` and is
        // not) and answers their output paths.
        var emitted = new List<IEmitFileNames>();
        Program.ForEachEmittedFile(host, (fileNames, _) => emitted.Add(fileNames));
        var names = Assert.Single(emitted);
        Assert.Equal("/a.js", names.JsFilePath);
        Assert.Null(names.DeclarationFilePath);

        // The two output-path queries answer from the options: a non-incremental program has no build-info path, an
        // incremental one names it beside its config file, and the declaration path is the out dir plus `.d.ts`.
        Assert.Null(Program.GetTsBuildInfoEmitOutputFilePath(options));
        Assert.Equal(
            "/tsconfig.tsbuildinfo",
            Program.GetTsBuildInfoEmitOutputFilePath(new FakeCompilerOptions { Incremental = true, ConfigFilePath = "/tsconfig.json" }));
        var configFile = new FakeParsedCommandLine
        {
            Options = new FakeCompilerOptions { ConfigFilePath = "/tsconfig.json", OutDir = "/out" },
            FileNames = ["/a.ts"],
        };
        Assert.Equal("/out/a.d.ts", Program.GetOutputDeclarationFileName("/a.ts", configFile, /*ignoreCase*/ false));

        // The declaration diagnostics are real down to the declaration transformer: a declaration file answers the
        // empty list, and a source file runs the transform (`const x = 1;` is declarable, so it answers no diagnostics).
        var sourceFile = program.GetSourceFile("/a.ts")!;
        var declarationFile = program.GetSourceFiles().First(file => file.IsDeclarationFile);
        Assert.Empty(program.GetDeclarationDiagnostics(declarationFile));
        Assert.Empty(program.GetDeclarationDiagnostics(sourceFile));

        // The transformer list is the reference's own composition (transformer.ts:127-197), for options that select
        // both the language-version tail and a per-file module transformer. Delegates compare by target and method
        // (`Delegate.Equals`), which is how the reference's factory identity is stated here.
        var chainOptions = new FakeCompilerOptions { Target = ScriptTarget.ES5, Module = ModuleKind.CommonJS };
        var transformers = Transformer.GetTransformers(chainOptions);
        var scriptTransformers = transformers.ScriptTransformers!;

        // transformer.ts:134-188 — the TypeScript transform first, the class-field transform before the ES2015 one
        // (transformer.ts:161-166, the ordering es2015.ts:2276-2278's unreachable default arm depends on), the
        // generator transform immediately after it, and the module transformer last.
        Assert.Equal(TypeScriptTransform.TransformTypeScript, scriptTransformers[0]);
        Assert.True(
            scriptTransformers.IndexOf(ClassFieldsTransform.TransformClassFields)
                < scriptTransformers.IndexOf(ES2015Transform.TransformES2015),
            "transformClassFields must precede transformES2015 (transformer.ts:161-166).");
        Assert.Equal(
            ES2015Transform.TransformES2015,
            scriptTransformers[scriptTransformers.IndexOf(GeneratorsTransform.TransformGenerators) - 1]);
        Assert.Equal(ImpliedNodeFormatDependent.GetModuleTransformer(ModuleKind.CommonJS), scriptTransformers[^1]);

        // transformer.ts:192-197 — the declaration half is the declaration transform, plus any custom
        // `afterDeclarations` factories.
        Assert.Equal(DeclarationsTransform.TransformDeclarations, Assert.Single(transformers.DeclarationTransformers!));

        // transformer.ts:128 — `emitOnly` answers the module-level empty array that `noTransformers` shares; the port
        // keeps it as the one list `NoTransformers` holds.
        Assert.Same(
            Transformer.NoTransformers.ScriptTransformers,
            Transformer.GetTransformers(chainOptions, emitOnly: true).ScriptTransformers);

        // `verifyCompilerOptions`'s emit-path check runs `GetEmitHost` and `forEachEmittedFile` for a program over
        // default options — the path M6 could not reach — so `createProgram` now completes without the flag that
        // skips it.
        var (uncheckedProgram, _, _, _) = CreateProgramOverOneFile("const x = 1;\n", suppressOutputPathCheck: false);
        Assert.Equal(["/a.ts"], uncheckedProgram.GetRootFileNames());
    }

    [Fact]
    public void EmitBuildInfoWritesTheVersionOnlyBuildInfoThroughTheHost()
    {
        // `--incremental` gives the program a build-info path, and `emitBuildInfo` is the one emit entry point that
        // needs no transformer list: it transforms nothing and writes `host.getBuildInfo() || { version }`.
        // PORT-NOTE: the reference fills `emittedFiles` only when `listEmittedFiles` is set (emitter.ts:767/790), so the
        // test turns it on to observe the written path — the reference's own condition, not a port deviation.
        var options = new FakeCompilerOptions { SuppressOutputPathCheck = true, Incremental = true, ConfigFilePath = "/tsconfig.json", ListEmittedFiles = true };
        var system = CreateSystemWithRootFile("const x = 1;\n");
        var host = Program.CreateCompilerHostWorker(options, /*setParentNodes*/ false, system);
        AddStubDefaultLib(system, host, options);
        var program = Program.CreateProgram(["/a.ts"], options, host);

        var result = program.EmitBuildInfo();

        Assert.False(result.EmitSkipped);
        Assert.Empty(result.Diagnostics!);
        Assert.Equal(["/tsconfig.tsbuildinfo"], result.EmittedFiles!.ToArray());
        // PORT-NOTE: the port's fake system records the paths it is asked to write (`FakeSystem.Written`) and keeps no
        // content, which is the contract `FilesTests` asserts against too; the build-info text itself is checked by
        // `getBuildInfoText`'s own tests.
        Assert.Equal(["/tsconfig.tsbuildinfo"], system.Written);
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

    /// <summary>The <c>ParsedCommandLine</c> object the emit-path queries take.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>ParsedCommandLine</c> is what <c>parseJsonConfigFileContent</c> returns, and that parser is
    /// M8's (the builder/config slice), so the port has no constructible one yet — the interface is
    /// <see cref="IParsedCommandLine"/> and every ported consumer takes it. This is the test double the emit-path
    /// assertions need, with the two slots those queries read.
    /// </remarks>
    private sealed class FakeParsedCommandLine : IParsedCommandLine
    {
        public ICompilerOptions? Options { get; set; }

        public ITypeAcquisition? TypeAcquisition { get; set; }

        public List<string>? FileNames { get; set; }

        public List<IProjectReference>? ProjectReferences { get; set; }

        public IWatchOptions? WatchOptions { get; set; }

        public object? Raw { get; set; }

        public List<IDiagnostic>? Errors { get; set; }

        public Dictionary<string, WatchDirectoryFlags>? WildcardDirectories { get; set; }

        public bool? CompileOnSave { get; set; }
    }
}
