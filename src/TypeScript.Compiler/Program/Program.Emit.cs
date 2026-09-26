namespace TypeScript.Compiler;

// ported from: src/compiler/program.ts:2598-2637 (getEmitHost), 2639-2648 (writeFile), 2650-2666 (emitBuildInfo),
// 2668-2712 (emit), 2714-2770 (emitWorker), 2827-2830 + 3244-3266 (the declaration-diagnostics family);
// src/compiler/emitter.ts:446-474, 481-497, 563-570 (the three emitter members the program's own verify path calls),
// 636-692 (the common source directory) and src/compiler/utilities.ts:10261-10280
// (getComputedCommonSourceDirectory) + src/compiler/program.ts:342-386 (computeCommonSourceDirectoryOfFilenames).
/// <summary>
/// The program's emit surface: the emit host it hands the emitter, the two emit entry points, the declaration
/// diagnostics and the emit-path queries <c>verifyCompilerOptions</c> and the project-reference graph ask for.
/// </summary>
/// <remarks>
/// PORT-NOTE: this file is <c>Program.EmitBoundary.cs</c> after its blockers were retired. What it used to hold has
/// three homes now: the emit host and the two emit entry points are this file's (their reference home is
/// <c>program.ts</c>), the emitter's own members are <c>Emit/Emitter.cs</c> (their reference home is
/// <c>emitter.ts</c>) and the facades below are the three static members the program's own call sites reach
/// unqualified — the arrangement M6 recorded, because a class named <c>Emit</c>/<c>Emitter</c> is not addressable from
/// inside <c>Program</c> under the reference's unqualified call shape. The common-source-directory family is M6-1's
/// cross-slice contract and is unchanged (the emitter calls it rather than declaring a second copy).
/// <para>
/// PORT-NOTE (<c>getBuildInfo</c>): TS's optional member is assigned only by a builder program (<c>builder.ts:1686</c>,
/// M8), and <c>program.ts:2628</c> reads it as <c>program.getBuildInfo?.()</c>; <see cref="Program.BuildInfo"/> is that
/// slot, and a <c>createProgram</c> program leaves it null — TS's <c>undefined</c>, which is what the emitter's
/// <c>host.getBuildInfo() || { version }</c> reads.
/// </para>
/// </remarks>
internal sealed partial class Program
{
    // ---------------------------------------------------------------------------------------------
    // The emit host (program.ts:2598-2637) and the write-file wrapper (program.ts:2639-2648)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/program.ts:2598-2637 — <c>getEmitHost</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS returns an object literal over the closure's locals; the port states it as
    /// <see cref="EmitHost"/>, the treatment §5 rule 6 gives every literal, and builds a new one per call exactly as
    /// the reference does (the literal is not cached on either side).
    /// <para>
    /// PORT-NOTE (<c>writeFile</c>): the reference's member is <c>writeFileCallback || writeFile</c>, where the second
    /// operand is the literal's settable slot — assigned by <c>createProgram</c> to the local wrapper below and
    /// replaced by a builder (<c>builder.ts:1583</c>, M8). The port keeps the slot on <see cref="Program.WriteFile"/>
    /// (<c>Program.IProgram.cs</c> records that) and falls back to the wrapper, which is what a createProgram program
    /// has: <see cref="CreateWriteFile"/>.
    /// </para>
    /// </remarks>
    internal IEmitHost GetEmitHost(Action<string, string, bool, Action<string>, List<SourceFile>, IWriteFileCallbackData, object?>? writeFileCallback = null)
        => new EmitHost(this, writeFileCallback);

    /// <summary>ported from: src/compiler/program.ts:2639-2648 — <c>writeFile</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's wrapper forwards its six arguments to the compiler host's
    /// <c>writeFile</c>; the port's delegate has a seventh (<c>emitData</c>, a generated-shape artifact every ported
    /// implementation ignores), which is forwarded unchanged rather than dropped.</remarks>
    private static Action<string, string, bool, Action<string>, List<SourceFile>, IWriteFileCallbackData, object?> CreateWriteFile(Program program)
        => (fileName, text, writeByteOrderMark, onError, sourceFiles, data, emitData)
            => program._host.WriteFile!(fileName, text, writeByteOrderMark, onError, sourceFiles, data, emitData);

    /// <summary>ported from: src/compiler/core.ts:1794 — <c>noop</c>, in the write-file delegate's shape.</summary>
    /// <remarks>PORT-NOTE: the reference hands <c>noop</c> to <c>getEmitHost</c> when it wants the emit host to write
    /// nothing (<c>getDeclarationDiagnosticsForFileNoCache</c>); C#'s delegate has a fixed parameter list, so the
    /// "does nothing" body is stated once here.</remarks>
    private static readonly Action<string, string, bool, Action<string>, List<SourceFile>, IWriteFileCallbackData, object?> s_noopWriteFile
        = (_, _, _, _, _, _, _) => { };

    /// <summary>ported from: src/compiler/program.ts:2598-2637 — the <c>EmitHost</c> literal.</summary>
    /// <remarks>
    /// PORT-NOTE: the literal's members that TS leaves absent (<c>directoryExists</c>, <c>realpath</c>,
    /// <c>getModuleSpecifierCache</c>, <c>getPackageJsonInfoCache</c>, <c>getNearestAncestorDirectoryWithPackageJson</c>,
    /// <c>trace</c>) are required by the generated <see cref="IEmitHost"/> and are forwarded to the program, which is
    /// the only source for them — the ruling <c>BasicNodeBuilderModuleSpecifierResolutionHost</c> records for the same
    /// interface. The three members TS declares optional there and the program does not implement are the program's own
    /// contract failures (<c>Program.IProgram.cs</c>), so forwarding them reports the same gap it does.
    /// </remarks>
    internal sealed class EmitHost : IEmitHost
    {
        private readonly Program _program;

        /// <summary>ported from: src/compiler/program.ts:2599 — <c>writeFileCallback</c>.</summary>
        public EmitHost(Program program, Action<string, string, bool, Action<string>, List<SourceFile>, IWriteFileCallbackData, object?>? writeFileCallback)
        {
            _program = program;

            /// <summary>ported from: src/compiler/program.ts:2607 — <c>writeFile: writeFileCallback || writeFile</c>.</summary>
            WriteFile = writeFileCallback ?? program.WriteFile ?? CreateWriteFile(program);

            /// <summary>ported from: src/compiler/program.ts:2629 — <c>getSourceFileFromReference</c>.</summary>
            GetSourceFileFromReference = (file, @ref) => program.GetSourceFileFromReference(file, @ref)!;
        }

        /// <summary>ported from: src/compiler/program.ts:2600 — <c>getCanonicalFileName</c>.</summary>
        public object? GetCanonicalFileName
        {
            get => ((ISourceFileMayBeEmittedHost)_program).GetCanonicalFileName;
            set => ((ISourceFileMayBeEmittedHost)_program).GetCanonicalFileName = value;
        }

        /// <summary>ported from: src/compiler/program.ts:2601 — <c>getCommonSourceDirectory</c>.</summary>
        public string GetCommonSourceDirectory() => ((IModuleSpecifierResolutionHost)_program).GetCommonSourceDirectory();

        /// <summary>ported from: src/compiler/program.ts:2602 — <c>getCompilerOptions</c>.</summary>
        public ICompilerOptions GetCompilerOptions() => _program.GetCompilerOptions();

        /// <summary>ported from: src/compiler/program.ts:2603 — <c>getCurrentDirectory: () =&gt; currentDirectory</c>.</summary>
        public string GetCurrentDirectory() => _program._currentDirectory;

        /// <summary>ported from: src/compiler/program.ts:2604 — <c>getSourceFile</c>.</summary>
        public SourceFile? GetSourceFile(string? fileName) => _program.GetSourceFile(fileName!);

        /// <summary>ported from: src/compiler/program.ts:2605 — <c>getSourceFileByPath</c>.</summary>
        public SourceFile? GetSourceFileByPath(string? path) => _program.GetSourceFileByPath(path!);

        /// <summary>ported from: src/compiler/program.ts:2606 — <c>getSourceFiles: () =&gt; files</c>.</summary>
        public List<SourceFile> GetSourceFiles() => _program.GetSourceFiles();

        /// <summary>ported from: src/compiler/program.ts:2608 — <c>isSourceFileFromExternalLibrary</c>.</summary>
        public bool IsSourceFileFromExternalLibrary(SourceFile? file) => _program.IsSourceFileFromExternalLibrary(file!);

        /// <summary>ported from: src/compiler/program.ts:2609 — <c>getRedirectFromSourceFile</c>.</summary>
        public IResolvedRefAndOutputDts? GetRedirectFromSourceFile(string? fileName)
            => ((IModuleSpecifierResolutionHost)_program).GetRedirectFromSourceFile(fileName);

        /// <summary>ported from: src/compiler/program.ts:2610 — <c>isSourceOfProjectReferenceRedirect</c>.</summary>
        public bool IsSourceOfProjectReferenceRedirect(string? fileName)
            => ((IModuleSpecifierResolutionHost)_program).IsSourceOfProjectReferenceRedirect(fileName);

        /// <summary>ported from: src/compiler/program.ts:2611 — <c>getSymlinkCache</c>.</summary>
        public object? GetSymlinkCache() => _program.GetSymlinkCache();

        /// <summary>ported from: src/compiler/program.ts:2612 — <c>writeFile</c>.</summary>
        public Action<string, string, bool, Action<string>, List<SourceFile>, IWriteFileCallbackData, object?>? WriteFile { get; set; }

        /// <summary>ported from: src/compiler/program.ts:2613 — <c>isEmitBlocked</c>.</summary>
        public bool IsEmitBlocked(string? emitFileName) => _program.IsEmitBlocked(emitFileName!);

        /// <summary>ported from: src/compiler/program.ts:2614 — <c>shouldTransformImportCall</c>.</summary>
        public bool ShouldTransformImportCall(SourceFile? sourceFile) => _program.ShouldTransformImportCall(sourceFile!);

        /// <summary>ported from: src/compiler/program.ts:2615 — <c>getEmitModuleFormatOfFile</c>.</summary>
        public ModuleKind GetEmitModuleFormatOfFile(SourceFile? sourceFile) => _program.GetEmitModuleFormatOfFile(sourceFile!);

        /// <summary>ported from: src/compiler/program.ts:2616 — <c>getDefaultResolutionModeForFile</c>.</summary>
        public object? GetDefaultResolutionModeForFile(SourceFile? sourceFile)
            => ((IModuleSpecifierResolutionHost)_program).GetDefaultResolutionModeForFile(sourceFile);

        /// <summary>ported from: src/compiler/program.ts:2617 — <c>getModeForResolutionAtIndex</c>.</summary>
        public object? GetModeForResolutionAtIndex(SourceFile? file, int index)
            => ((IModuleSpecifierResolutionHost)_program).GetModeForResolutionAtIndex(file, index);

        /// <summary>ported from: src/compiler/program.ts:2618 — <c>readFile: f =&gt; host.readFile(f)</c>.</summary>
        public string? ReadFile(string? path) => _program._readFile(path!);

        /// <summary>ported from: src/compiler/program.ts:2619-2624 — <c>fileExists</c>.</summary>
        /// <remarks>PORT-NOTE: the reference reads its two local caches (<c>getSourceFileByPath</c> and
        /// <c>missingFileNames.has</c>) before falling back to the host — the program's own caches, which the port's
        /// state file holds under the same names.</remarks>
        public bool FileExists(string? f)
        {
            // Use local caches
            var path = _program.ToPath(f!);
            if (_program.GetSourceFileByPath(path) is not null) return true;
            if (_program._missingFileNames.ContainsKey(path)) return false;
            // Before falling back to the host
            return _program._host.FileExists(f!);
        }

        /// <summary>ported from: src/compiler/program.ts:2625 — <c>realpath: maybeBind(host, host.realpath)</c>.</summary>
        /// <remarks>PORT-NOTE: TS binds the host's <c>realpath</c> when it has one and answers <c>undefined</c> when it
        /// does not; the generated slot is a required method, so the port asks the host — the dropped-optionality ruling
        /// <c>Program.IProgram.cs</c> records for the same member.</remarks>
        public string Realpath(string? path) => _program._host.Realpath(path!);

        /// <summary>ported from: src/compiler/program.ts:2626 — <c>useCaseSensitiveFileNames:
        /// () =&gt; host.useCaseSensitiveFileNames()</c>.</summary>
        public bool UseCaseSensitiveFileNames() => Utilities.HostUsesCaseSensitiveFileNames(_program._host);

        /// <summary>ported from: src/compiler/program.ts:2627 — <c>getBuildInfo: () =&gt; program.getBuildInfo?.()</c>.</summary>
        /// <remarks>PORT-NOTE: TS's optional member is <see cref="Program.BuildInfo"/>, which only a builder assigns
        /// (M8), so a createProgram program answers C#'s null for TS's <c>undefined</c>.</remarks>
        public IBuildInfo? GetBuildInfo() => _program.BuildInfo;

        /// <summary>ported from: src/compiler/program.ts:2629 — <c>getSourceFileFromReference</c>.</summary>
        public Func<SourceFile, IFileReference, SourceFile>? GetSourceFileFromReference { get; set; }

        /// <summary>ported from: src/compiler/program.ts:2630 — <c>redirectTargetsMap</c>.</summary>
        public Dictionary<string, List<string>>? RedirectTargetsMap => ((IModuleSpecifierResolutionHost)_program).RedirectTargetsMap;

        /// <summary>ported from: src/compiler/program.ts:2631 — <c>getFileIncludeReasons</c>.</summary>
        public Dictionary<string, List<IFileIncludeReason>> GetFileIncludeReasons()
            => ((IModuleSpecifierResolutionHost)_program).GetFileIncludeReasons();

        /// <summary>ported from: src/compiler/program.ts:2632 — <c>createHash: maybeBind(host, host.createHash)</c>.</summary>
        /// <remarks>PORT-NOTE: see <see cref="Realpath"/> — the generated slot is required, so the port asks the
        /// compiler host, which always implements it.</remarks>
        public string CreateHash(string? data) => _program._host.CreateHash(data!);

        /// <summary>ported from: src/compiler/program.ts:2633 — <c>getModuleResolutionCache</c>.</summary>
        public object? GetModuleResolutionCache() => ((IModuleSpecifierResolutionHost)_program).GetModuleResolutionCache();

        /// <summary>ported from: src/compiler/program.ts:2634 — <c>trace: maybeBind(host, host.trace)</c>.</summary>
        public void Trace(string? s) => ((IModuleSpecifierResolutionHost)_program).Trace(s);

        /// <summary>ported from: src/compiler/program.ts:2635 — <c>getGlobalTypingsCacheLocation</c>.</summary>
        public string? GetGlobalTypingsCacheLocation() => ((IModuleSpecifierResolutionHost)_program).GetGlobalTypingsCacheLocation();

        /// <summary>PORT-NOTE: TS's literal leaves this member absent; the generated <see cref="IModuleSpecifierResolutionHost"/>
        /// requires it, so it forwards — the same treatment the six dropped members above get.</summary>
        public bool DirectoryExists(string? path) => _program._directoryExists(path!);

        /// <summary>PORT-NOTE: see <see cref="DirectoryExists"/>.</summary>
        public IModuleSpecifierCache GetModuleSpecifierCache() => ((IModuleSpecifierResolutionHost)_program).GetModuleSpecifierCache();

        /// <summary>PORT-NOTE: see <see cref="DirectoryExists"/>.</summary>
        public object? GetPackageJsonInfoCache() => ((IModuleSpecifierResolutionHost)_program).GetPackageJsonInfoCache();

        /// <summary>PORT-NOTE: see <see cref="DirectoryExists"/>.</summary>
        public string? GetNearestAncestorDirectoryWithPackageJson(string? fileName, string? rootDir)
            => ((IModuleSpecifierResolutionHost)_program).GetNearestAncestorDirectoryWithPackageJson(fileName, rootDir);
    }

    // ---------------------------------------------------------------------------------------------
    // The two emit entry points (program.ts:2650-2770)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/program.ts:2650-2666 — <c>emitBuildInfo</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>tracing?.push(…, {}, true)</c> is <see cref="TracingArgs.Of"/> with no entries, and
    /// the resolver it hands <c>emitFiles</c> is <see cref="Emitter.NotImplementedResolver"/> — the build-info write
    /// transforms nothing, which is why the reference can state it that way.</remarks>
    internal IEmitResult EmitBuildInfo(
        Action<string, string, bool, Action<string>, List<SourceFile>, IWriteFileCallbackData, object?>? writeFileCallback = null,
        ICancellationToken? cancellationToken = null)
    {
        Tracing.Current?.Push(Tracing.Phase.Emit, "emitBuildInfo", TracingArgs.Of(), /*separateBeginAndEnd*/ true);
        Performance.Mark("beforeEmit");
        var emitResult = Emitter.EmitFiles(
            Emitter.NotImplementedResolver,
            GetEmitHost(writeFileCallback),
            /*targetSourceFile*/ null,
            Transformer.NoTransformers,
            /*emitOnly*/ null,
            /*onlyBuildInfo*/ true);

        Performance.Mark("afterEmit");
        Performance.Measure("Emit", "beforeEmit", "afterEmit");
        Tracing.Current?.Pop();
        return emitResult;
    }

    /// <summary>ported from: src/compiler/program.ts:2688-2712 — <c>emit</c>.</summary>
    /// <remarks>
    /// PORT-NOTE (<c>emitOnly</c>): TS's slot is <c>boolean | EmitOnly</c> and the reference's own four tests
    /// distinguish the enum members (<c>EmitFiles</c> records them); the generated <c>IProgram.Emit</c> flattens it to
    /// <c>bool?</c>, so this member states the union and the interface's forward passes what it has.
    /// <para>
    /// PORT-NOTE (<c>forceDtsEmit</c>/<c>skipBuildInfo</c>): the reference's <c>emit</c> has two more parameters than
    /// the generated interface's slot (the builder passes them, <c>builder.ts:1691</c>); the port keeps the reference's
    /// full list here and the interface's forward names only the five it declares.
    /// </para>
    /// </remarks>
    internal IEmitResult Emit(
        SourceFile? targetSourceFile = null,
        Action<string, string, bool, Action<string>, List<SourceFile>, IWriteFileCallbackData, object?>? writeFile = null,
        ICancellationToken? cancellationToken = null,
        object? emitOnly = null,
        ICustomTransformers? customTransformers = null,
        bool? forceDtsEmit = null,
        bool? skipBuildInfo = null)
    {
        Tracing.Current?.Push(Tracing.Phase.Emit, "emit", TracingArgs.Of(("path", targetSourceFile?.Path)), /*separateBeginAndEnd*/ true);
        var result = RunWithCancellationToken(() =>
            EmitWorker(
                this,
                targetSourceFile,
                writeFile,
                cancellationToken,
                emitOnly,
                customTransformers,
                forceDtsEmit,
                skipBuildInfo));
        Tracing.Current?.Pop();
        return result;
    }

    /// <summary>ported from: src/compiler/program.ts:2714-2770 — <c>emitWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>typeChecker.runWithCancellationToken(cancellationToken, () =&gt; emitFiles(…))</c> passes a
    /// no-argument callback; the generated slot's delegate takes the checker, so the port's lambda ignores it. The
    /// transformers come from <see cref="Transformer.GetTransformers"/>, which is the named M7-B blocker until the
    /// transform batches land.
    /// </remarks>
    private IEmitResult EmitWorker(
        Program program,
        SourceFile? sourceFile,
        Action<string, string, bool, Action<string>, List<SourceFile>, IWriteFileCallbackData, object?>? writeFileCallback,
        ICancellationToken? cancellationToken,
        object? emitOnly,
        ICustomTransformers? customTransformers,
        bool? forceDtsEmit,
        bool? skipBuildInfo)
    {
        if (!Js.IsTruthy(forceDtsEmit))
        {
            var result = HandleNoEmitOptions(program, sourceFile, writeFileCallback, cancellationToken);
            if (result is not null) return result;
        }

        // Create the emit resolver outside of the "emitTime" tracking code below.  That way
        // any cost associated with it (like type checking) are appropriate associated with
        // the type-checking counter.
        //
        // If the -out option is specified, we should not pass the source file to getEmitResolver.
        // This is because in the -out scenario all files need to be emitted, and therefore all
        // files need to be type checked. And the way to specify that all files need to be type
        // checked is to not pass the file to getEmitResolver.
        var typeChecker = GetTypeChecker();
        var options = program.GetCompilerOptions();
        var emitResolver = typeChecker.GetEmitResolver(
            options.OutFile is not null ? null : sourceFile,
            cancellationToken,
            Emitter.EmitResolverSkipsTypeChecking(emitOnly, forceDtsEmit));

        Performance.Mark("beforeEmit");

        var emitResult = typeChecker.RunWithCancellationToken(
            cancellationToken,
            _ => Emitter.EmitFiles(
                emitResolver,
                GetEmitHost(writeFileCallback),
                sourceFile,
                Transformer.GetTransformers(options, customTransformers, emitOnly),
                emitOnly,
                /*onlyBuildInfo*/ false,
                forceDtsEmit,
                skipBuildInfo));

        Performance.Mark("afterEmit");
        Performance.Measure("Emit", "beforeEmit", "afterEmit");
        return emitResult;
    }

    /// <summary>ported from: src/compiler/program.ts:2714-2716 — <c>isEmitBlocked</c>.</summary>
    /// <remarks>PORT-NOTE: the reference is <c>hasEmitBlockingDiagnostics.has(toPath(emitFileName))</c>; the port's map
    /// is the same dictionary, and its keys are written as <c>ToPath(emitFileName)</c> by <c>BlockEmittingOfFile</c>
    /// (<c>Program.Verify.cs:1033</c>) and by the project-reference check (<c>:790</c>), so the reader converts the
    /// same way. (The retired M6 blocker's body read the map with the unconverted name; that could never answer true,
    /// which the unblocked callers would have shown.)</remarks>
    internal bool IsEmitBlocked(string emitFileName) => _hasEmitBlockingDiagnostics.ContainsKey(ToPath(emitFileName));

    /// <summary>ported from: src/compiler/types.ts:4934 — the optional <c>getBuildInfo</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's member is a function only a builder program assigns (<c>builder.ts:1686</c>, M8) and the emit
    /// host reads it as <c>program.getBuildInfo?.()</c> (<c>program.ts:2628</c>); the port states the value it answers,
    /// with C#'s null for TS's absent member. M8's builder assigns this slot; <c>IProgram.GetBuildInfo</c> (the
    /// generated required face, which cannot express the absence) answers this value or reports the contract gap.
    /// </remarks>
    internal IBuildInfo? BuildInfo { get; set; }

    // ---------------------------------------------------------------------------------------------
    // The declaration diagnostics (program.ts:2827-2830, 3244-3266)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/program.ts:2827-2829 — <c>getDeclarationDiagnostics</c>.</summary>
    /// <remarks>PORT-NOTE: <c>getDiagnosticsHelper</c> is the port's, in <c>Program.DiagnosticsAggregation.cs</c>; TS's
    /// answer is <c>readonly DiagnosticWithLocation[]</c> and the generated slot is the list type, so the sorted list is
    /// widened once (the same bridge the sibling diagnostic members use).</remarks>
    internal List<IDiagnosticWithLocation> GetDeclarationDiagnostics(SourceFile? sourceFile = null, ICancellationToken? cancellationToken = null)
        => [.. GetDiagnosticsHelper(sourceFile, (file, token) => GetDeclarationDiagnosticsForFile(file, token), cancellationToken)];

    /// <summary>ported from: src/compiler/program.ts:3263-3265 — <c>getDeclarationDiagnosticsForFile</c>.</summary>
    private List<IDiagnosticWithLocation> GetDeclarationDiagnosticsForFile(SourceFile sourceFile, ICancellationToken? cancellationToken)
        => sourceFile.IsDeclarationFile ? [] : GetDeclarationDiagnosticsWorker(sourceFile, cancellationToken);

    /// <summary>ported from: src/compiler/program.ts:3244-3253 — <c>getDeclarationDiagnosticsWorker</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>cached?.get(path)</c> plus <c>if (!result)</c> treats a cached empty array as present
    /// (a JS array is truthy) and only <c>undefined</c> as absent, which is what <c>TryGetValue</c> tests — the reading
    /// the sibling <c>GetBindAndCheckDiagnosticsForFile</c> records. The reference assigns the fresh result inside the
    /// <c>set</c> call; the port computes it first and stores it, the same order (nothing reads the cache in between).
    /// </remarks>
    private List<IDiagnosticWithLocation> GetDeclarationDiagnosticsWorker(SourceFile sourceFile, ICancellationToken? cancellationToken)
    {
        var path = sourceFile.Path ?? "";
        if (_cachedDeclarationDiagnosticsForFile is not null
            && _cachedDeclarationDiagnosticsForFile.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var result = GetDeclarationDiagnosticsForFileNoCache(sourceFile, cancellationToken);
        _cachedDeclarationDiagnosticsForFile ??= [];
        _cachedDeclarationDiagnosticsForFile[path] = result;
        return result;
    }

    /// <summary>ported from: src/compiler/program.ts:3255-3261 — <c>getDeclarationDiagnosticsForFileNoCache</c>.</summary>
    /// <remarks>PORT-NOTE: <c>runWithCancellationToken</c> is this class's (program.ts:2843); <c>getEmitHost(noop)</c> is
    /// <see cref="GetEmitHost"/> with <see cref="s_noopWriteFile"/>, i.e. "don't actually write any files since we're
    /// just getting diagnostics"; the transform-side call is the named blocker below.</remarks>
    private List<IDiagnosticWithLocation> GetDeclarationDiagnosticsForFileNoCache(SourceFile sourceFile, ICancellationToken? cancellationToken)
        => RunWithCancellationToken(() =>
        {
            var resolver = GetTypeChecker().GetEmitResolver(sourceFile, cancellationToken, /*forceDts*/ null);
            // Don't actually write any files since we're just getting diagnostics.
            return GetDeclarationDiagnosticsFromTransform(GetEmitHost(s_noopWriteFile), resolver, sourceFile) ?? [];
        });

    /// <summary>ported from: src/compiler/transformers/declarations.ts:221-238 — <c>getDeclarationDiagnostics</c>.</summary>
    /// <remarks>PORT-NOTE: the member is the declaration transformer's own entry point
    /// (<see cref="DeclarationsTransform.GetDeclarationDiagnostics"/>, landed with the transform), which is why this
    /// facade carries no body of its own: the reference's <c>program.getDeclarationDiagnosticsForFileNoCache</c> calls
    /// that function directly and the port's `Program`/`DeclarationsTransform` split needs the name reachable from both
    /// (the same arrangement <see cref="ForEachEmittedFile"/> records). Its <c>undefined</c> arm stays <c>null</c>, and
    /// the call site above is the reference's own <c>|| emptyArray</c>.</remarks>
    private static List<IDiagnosticWithLocation>? GetDeclarationDiagnosticsFromTransform(IEmitHost host, IEmitResolver resolver, SourceFile sourceFile)
        => DeclarationsTransform.GetDeclarationDiagnostics(host, resolver, sourceFile);

    // ---------------------------------------------------------------------------------------------
    // The emitter members the program's own call sites reach unqualified (emitter.ts:446-570)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:446-474 — <c>forEachEmittedFile</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the body is <see cref="Emitter.ForEachEmittedFile{T}"/>, which is the member's home; this static
    /// facade exists because <c>Program.Verify.cs</c>'s emit-path check calls the reference's name unqualified and a
    /// class named <c>Emit</c>/<c>Emitter</c> is not addressable from inside <c>Program</c> in that position (the
    /// arrangement the previous slice recorded). The reference's callback returns <c>T</c> and its <c>void</c>
    /// instantiation is what this face states: the caller's action is run and the facade answers nothing.
    /// </remarks>
    internal static void ForEachEmittedFile(
        IEmitHost host,
        Action<IEmitFileNames, Node?> action,
        object? sourceFilesOrTargetSourceFile = null,
        bool forceDtsEmit = false,
        bool? onlyBuildInfo = null,
        bool? includeBuildInfo = null)
        => Emitter.ForEachEmittedFile<object>(
            host,
            (fileNames, sourceFileOrBundle) =>
            {
                action(fileNames, sourceFileOrBundle);
                return null;
            },
            sourceFilesOrTargetSourceFile,
            forceDtsEmit,
            onlyBuildInfo,
            includeBuildInfo);

    /// <summary>ported from: src/compiler/emitter.ts:481-497 — <c>getTsBuildInfoEmitOutputFilePath</c>.</summary>
    /// <remarks>PORT-NOTE: the body is <see cref="Emitter.GetTsBuildInfoEmitOutputFilePath"/>; the façade exists for the
    /// reason <see cref="ForEachEmittedFile"/> records.</remarks>
    internal static string? GetTsBuildInfoEmitOutputFilePath(ICompilerOptions options)
        => Emitter.GetTsBuildInfoEmitOutputFilePath(options);

    /// <summary>ported from: src/compiler/emitter.ts:563-570 — <c>getOutputDeclarationFileName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the body is <see cref="Emitter.GetOutputDeclarationFileName"/>; the façade exists for the reason
    /// <see cref="ForEachEmittedFile"/> records.
    /// <para>
    /// PORT-NOTE: the reference's fourth parameter defaults to
    /// <c>() =&gt; getCommonSourceDirectoryOfConfig(configFile, ignoreCase)</c>; the port's null default is the "not
    /// supplied" state and the emitter's member supplies the reference's default.
    /// </para>
    /// </remarks>
    internal static string GetOutputDeclarationFileName(
        string inputFileName,
        IParsedCommandLine configFile,
        bool ignoreCase,
        Func<string>? getCommonSourceDirectory = null)
        => Emitter.GetOutputDeclarationFileName(inputFileName, configFile, ignoreCase, getCommonSourceDirectory);

    // ---------------------------------------------------------------------------------------------
    // The common source directory (emitter.ts:636-692, utilities.ts:10261-10280)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/program.ts:1138 — <c>inferredTypesContainingFile</c>.</summary>
    /// <remarks>PORT-NOTE: Main's ruling (the reference's declaration site, program.ts, decides the class) moved this from a
    /// <c>Utilities</c> partial to <c>Program</c>; M6ProgHost's `Utilities.InferredTypesContainingFile` is superseded and
    /// must be deleted (nothing else calls it).</remarks>
    internal const string InferredTypesContainingFile = "__inferred type names__.ts";

    /// <summary>ported from: src/compiler/program.ts:342-386 — <c>computeCommonSourceDirectoryOfFilenames</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: a <c>program.ts</c> member, so it belongs on <c>Program</c> (Main's declaration-site rule), which is why
    /// its call sites in <see cref="GetCommonSourceDirectoryFromOptions"/> and
    /// <see cref="GetComputedCommonSourceDirectory"/> are unqualified.
    /// <para>
    /// PORT-NOTE: TS's <c>commonPathComponents.length = i</c> truncation is <see cref="List{T}.RemoveRange"/> here, and its
    /// <c>forEach</c> early exit is the loop below returning <c>true</c>.
    /// </para>
    /// </remarks>
    internal static string ComputeCommonSourceDirectoryOfFilenames(
        List<string> fileNames,
        string currentDirectory,
        Core.GetCanonicalFileName getCanonicalFileName)
    {
        List<string>? commonPathComponents = null;
        var failed = false;
        foreach (var sourceFile in fileNames)
        {
            // Each file contributes into common source file path
            var sourcePathComponents = Paths.GetNormalizedPathComponents(sourceFile, currentDirectory);
            sourcePathComponents.RemoveAt(sourcePathComponents.Count - 1); // The base file name is not part of the common directory path

            if (commonPathComponents is null)
            {
                // first file
                commonPathComponents = sourcePathComponents;
                continue;
            }

            var n = Math.Min(commonPathComponents.Count, sourcePathComponents.Count);
            for (var i = 0; i < n; i++)
            {
                if (getCanonicalFileName(commonPathComponents[i]) != getCanonicalFileName(sourcePathComponents[i]))
                {
                    if (i == 0)
                    {
                        // Failed to find any common path component
                        failed = true;
                        break;
                    }

                    // New common path found that is 0 -> i-1
                    commonPathComponents.RemoveRange(i, commonPathComponents.Count - i);
                    break;
                }
            }

            if (failed)
            {
                break;
            }

            // If the sourcePathComponents was shorter than the commonPathComponents, truncate to the sourcePathComponents
            if (sourcePathComponents.Count < commonPathComponents.Count)
            {
                commonPathComponents.RemoveRange(sourcePathComponents.Count, commonPathComponents.Count - sourcePathComponents.Count);
            }
        }

        // A common path can not be found when paths span multiple drives on windows, for example
        if (failed)
        {
            return "";
        }

        if (commonPathComponents is null)
        {
            // Can happen when all input files are .d.ts files
            return currentDirectory;
        }

        return Paths.GetPathFromPathComponents(commonPathComponents);
    }

    /// <summary>ported from: src/compiler/emitter.ts:636-671 — <c>getCommonSourceDirectory</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: this member lives in <c>emitter.ts</c> (M7's file) in the reference, but the value it computes is
    /// program state: <c>program.getCommonSourceDirectory()</c> memoizes it on
    /// <see cref="ProgramDiagnostics"/>, <c>--rootDir</c> verification reads it and declaration emit needs it. The
    /// M6-1 slice needs it before M7 lands, so the port declares it here under the reference's name; M7 calls this
    /// member rather than declaring a second copy (the emitter's <c>getOutputDeclarationFileName</c> and
    /// <c>getCommonSourceDirectoryOfConfig</c> forwarders do).
    /// </remarks>
    internal static string GetCommonSourceDirectoryFromOptions(
        ICompilerOptions options,
        Func<List<string>> emittedFiles,
        string currentDirectory,
        Core.GetCanonicalFileName getCanonicalFileName,
        Action<string>? checkSourceFilesBelongToPath = null)
    {
        string commonSourceDirectory;
        if (options.RootDir is not null)
        {
            // If a rootDir is specified use it as the commonSourceDirectory
            commonSourceDirectory = Paths.GetNormalizedAbsolutePath(options.RootDir, currentDirectory);
            checkSourceFilesBelongToPath?.Invoke(options.RootDir);
        }
        else if (options.ConfigFilePath is not null)
        {
            // Project compilations never infer their root from the input source paths
            commonSourceDirectory = Paths.GetDirectoryPath(Paths.NormalizeSlashes(options.ConfigFilePath));
            checkSourceFilesBelongToPath?.Invoke(commonSourceDirectory);
        }
        else
        {
            commonSourceDirectory = ComputeCommonSourceDirectoryOfFilenames(emittedFiles(), currentDirectory, getCanonicalFileName);
        }

        return EndWithDirectorySeparator(commonSourceDirectory);
    }

    /// <summary>ported from: src/compiler/utilities.ts:10261-10280 — <c>getComputedCommonSourceDirectory</c>.</summary>
    internal static string GetComputedCommonSourceDirectory(
        List<string> emittedFiles,
        string currentDirectory,
        Core.GetCanonicalFileName getCanonicalFileName)
        => EndWithDirectorySeparator(ComputeCommonSourceDirectoryOfFilenames(emittedFiles, currentDirectory, getCanonicalFileName));

    /// <summary>ported from: src/compiler/emitter.ts:685-692 — <c>getCommonSourceDirectoryOfConfig</c>.</summary>
    /// <remarks>PORT-NOTE: see <see cref="GetCommonSourceDirectoryFromOptions"/> — emitter.ts member, declared here for
    /// the same reason.</remarks>
    internal static string GetCommonSourceDirectoryOfConfig(IParsedCommandLine configFile, bool ignoreCase)
        => GetCommonSourceDirectoryFromOptions(
            configFile.Options!,
            () => (configFile.FileNames ?? [])
                .Where(file => !(Js.IsTruthy(configFile.Options?.NoEmitForJsFiles) && Paths.FileExtensionIsOneOf(file, Utilities.SupportedJSExtensionsFlat)) && !Parser.IsDeclarationFileName(file))
                .ToList(),
            Paths.GetDirectoryPath(Paths.NormalizeSlashes(Debug.CheckDefined(configFile.Options?.ConfigFilePath, "a config file must have a path"))),
            Core.CreateGetCanonicalFileName(!ignoreCase));

    /// <summary>ported from: src/compiler/emitter.ts:663-670 — the trailing-separator step both helpers end with.</summary>
    private static string EndWithDirectorySeparator(string commonSourceDirectory)
        => commonSourceDirectory.Length != 0 && commonSourceDirectory[^1] != Paths.DirectorySeparator[0]
            // Make sure directory path ends with directory separator so this string can directly
            // used to replace with "" to get the relative path of the source file and the relative path doesn't
            // start with / making it rooted path
            ? commonSourceDirectory + Paths.DirectorySeparator
            : commonSourceDirectory;
}
