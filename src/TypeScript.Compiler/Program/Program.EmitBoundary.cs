namespace TypeScript.Compiler;

// ported from: src/compiler/program.ts:2598-2770 (getEmitHost, writeFile, emitBuildInfo, emit, emitWorker)
// ported from: src/compiler/program.ts:2827-2830, 3244-3266 (getDeclarationDiagnostics)
// ported from: src/compiler/emitter.ts:446-497, 563-570 (forEachEmittedFile, getTsBuildInfoEmitOutputFilePath, getOutputDeclarationFileName)
// ported from: src/compiler/emitter.ts:636-692 (getCommonSourceDirectory, getCommonSourceDirectoryOfConfig)
// ported from: src/compiler/emitter.ts:668-692 (getComputedCommonSourceDirectory)
/// <summary>
/// The M7 boundary — every member of <c>Program</c> that needs the transformer chain or the emitter — plus the two
/// common-source-directory helpers the program needs before M7.
/// </summary>
/// <remarks>
/// PORT-NOTE: the boundary members are the sanctioned loud, named failure of §15.15's first ruling (the precedent is
/// <c>FactoryUtilities.IsJSDocTypeAssertion</c>, §15.9): they throw <see cref="EmitNotLanded"/> with a message naming
/// the milestone, and <c>ProgramTests.EmitAndTheDeclarationDiagnosticsAreTheM7Boundary</c> pins each one — including the
/// three <c>emitter.ts</c> helpers below, which <c>verifyCompilerOptions</c>'s emit-path and project-reference checks
/// call, so their throw is reachable from <c>createProgram</c> itself. Nothing else in the port may call them, and when
/// M7 lands it replaces the throw with the reference's body — the strings are the contract, so the test turns into a
/// green one at that point rather than being deleted.
/// </remarks>
internal sealed partial class Program
{
    /// <summary>ported from: src/compiler/program.ts:2688-2712 — <c>emit</c>.</summary>
    internal IEmitResult Emit(
        SourceFile? targetSourceFile = null,
        Action<string, string, bool, Action<string>, List<SourceFile>, IWriteFileCallbackData, object?>? writeFile = null,
        ICancellationToken? cancellationToken = null,
        bool? emitOnlyDtsFiles = null,
        ICustomTransformers? customTransformers = null)
        => throw EmitNotLanded("emit (program.ts:2688)");

    /// <summary>ported from: src/compiler/program.ts:2650-2666 — <c>emitBuildInfo</c>.</summary>
    internal IEmitResult EmitBuildInfo(
        Action<string, string, bool, Action<string>, List<SourceFile>, IWriteFileCallbackData, object?>? writeFile = null,
        ICancellationToken? cancellationToken = null)
        => throw EmitNotLanded("emitBuildInfo (program.ts:2650)");

    /// <summary>ported from: src/compiler/program.ts:2827-2830 — <c>getDeclarationDiagnostics</c>.</summary>
    internal List<IDiagnosticWithLocation> GetDeclarationDiagnostics(SourceFile? sourceFile = null, ICancellationToken? cancellationToken = null)
        => throw EmitNotLanded("getDeclarationDiagnostics (program.ts:2827)");

    /// <summary>ported from: src/compiler/program.ts:3244-3254 — <c>getDeclarationDiagnosticsWorker</c>.</summary>
    private List<IDiagnosticWithLocation> GetDeclarationDiagnosticsWorker(SourceFile sourceFile, ICancellationToken? cancellationToken)
        => throw EmitNotLanded("getDeclarationDiagnosticsWorker (program.ts:3244)");

    /// <summary>ported from: src/compiler/program.ts:2598-2637 — <c>getEmitHost</c>.</summary>
    internal IEmitHost GetEmitHost(
        Action<string, string, bool, Action<string>, List<SourceFile>, IWriteFileCallbackData, object?>? writeFileCallback = null)
        => throw EmitNotLanded("getEmitHost (program.ts:2598)");

    /// <summary>ported from: src/compiler/program.ts:2714-2716 — <c>isEmitBlocked</c>.</summary>
    internal bool IsEmitBlocked(string emitFileName)
        => _hasEmitBlockingDiagnostics.GetValueOrDefault(emitFileName);

    /// <summary>The failure every member that needs M7 reports, naming the reference's member and line.</summary>
    internal static NotSupportedException EmitNotLanded(string what)
        => new($"{what} is not ported yet: the transformer chain and the emitter are M7's (docs/architecture.md §15.15).");

    // ---------------------------------------------------------------------------------------------
    // The emitter.ts helpers the program's own verify path calls (emitter.ts:446-570)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:446-474 — <c>forEachEmittedFile</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: M7's member — the body needs <c>getSourceFilesToEmit</c>, <c>getOutputPathsFor</c> and
    /// <c>factory.createBundle</c>, i.e. the transformer chain and the emitter — so it is the §15.15 ruling-1 loud
    /// blocker, called unqualified by <c>Program.Verify.cs</c>'s emit-path check under the arrangement that PORT-NOTE
    /// records (a static member of <c>Program</c>, because no class named <c>Emit</c> is addressable here). The throw is
    /// therefore reachable from <c>createProgram</c> whenever that check runs — it is a milestone boundary, not a
    /// hypothetical — and M7 replaces the throw with the reference's body.
    /// <para>
    /// PORT-NOTE: the reference's callback takes both <c>emitFileNames</c> and <c>sourceFileOrBundle</c> and returns
    /// <c>T</c>; the port's one caller ignores the result and the second parameter, so the declared face is the
    /// two-parameter <see cref="Action{T1,T2}"/> overload — the same "ignore the result" face
    /// <c>Utilities.ForEachProjectReference</c> records for TS's free <c>T</c>. The union
    /// <c>readonly SourceFile[] | SourceFile</c> is <c>object?</c>, the treatment
    /// <c>ResolveTypeReferenceDirectiveNamesWorker</c> records for the same generator gap.
    /// </para>
    /// </remarks>
    internal static void ForEachEmittedFile(
        IEmitHost host,
        Action<IEmitFileNames, Node?> action,
        object? sourceFilesOrTargetSourceFile = null,
        bool forceDtsEmit = false,
        bool? onlyBuildInfo = null,
        bool? includeBuildInfo = null)
        => throw EmitNotLanded("forEachEmittedFile (emitter.ts:446)");

    /// <summary>ported from: src/compiler/emitter.ts:481-497 — <c>getTsBuildInfoEmitOutputFilePath</c>.</summary>
    /// <remarks>PORT-NOTE: M7's member — the body asks <c>canEmitTsBuildInfo</c> and the emit-path family
    /// (<c>removeFileExtension</c>, <c>getRelativePathFromDirectory</c>, <c>combinePaths</c>) — so it is the §15.15
    /// ruling-1 loud blocker, called unqualified by <c>Program.Verify.cs</c>'s project-reference check for the reason
    /// <see cref="ForEachEmittedFile"/> records.</remarks>
    internal static string? GetTsBuildInfoEmitOutputFilePath(ICompilerOptions options)
        => throw EmitNotLanded("getTsBuildInfoEmitOutputFilePath (emitter.ts:481)");

    /// <summary>ported from: src/compiler/emitter.ts:563-570 — <c>getOutputDeclarationFileName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: M7's member — the body is <c>changeExtension(getOutputPathWithoutChangingExt(…), …)</c>, the same
    /// emit-path family — so it is the §15.15 ruling-1 loud blocker; the two project-reference call sites
    /// (<c>Program.cs</c>'s graph pass and <c>Program.ProjectReferences.cs</c>'s config parse) keep the reference's
    /// unqualified shape.
    /// <para>
    /// PORT-NOTE: the reference's fourth parameter defaults to
    /// <c>() =&gt; getCommonSourceDirectoryOfConfig(configFile, ignoreCase)</c>; both callers pass the memoized
    /// delegate, so the port's null default is the "not supplied" state and M7 supplies the reference's default in the
    /// body.
    /// </para>
    /// </remarks>
    internal static string GetOutputDeclarationFileName(
        string inputFileName,
        IParsedCommandLine configFile,
        bool ignoreCase,
        Func<string>? getCommonSourceDirectory = null)
        => throw EmitNotLanded("getOutputDeclarationFileName (emitter.ts:563)");

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
    /// M6-1 slice needs it before M7 lands, so the port declares it here under the reference's name; M7 must call this
    /// member rather than declare a second copy (the report names it as a cross-slice contract).
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
