using System.Globalization;
using System.Text.Json;

// The port's own entry-point class is named Program (tools/AstDiff, tools/BindDiff, tools/TypeDiff and tools/EmitDiff
// do the same), which is also the name of the compiler's; the alias keeps the two apart.
using PortProgram = TypeScript.Compiler.Program;

namespace TypeScript.Compiler.Tools;

/// <summary>Where a run's corpus and reference live, for the two halves of the driver.</summary>
internal sealed record BaselineDiffContext(
    string WorkspaceRoot,
    string LibDirectory,
    string TestLibDirectory,
    string BaselinesDirectory,
    string CasesDirectory);

/// <summary>
/// One compilation of one case, with everything the baseline writers read off it:
/// <c>compiler.CompilationResult</c> (src/harness/compilerImpl.ts:44-180) — the program, its options, the
/// diagnostics <c>compileFiles</c> computed, and the outputs the host was handed.
/// </summary>
internal sealed class BaselineCompilation(
    TypeScript.Compiler.Program program,
    ICompilerOptions options,
    List<Diagnostic> diagnostics,
    List<BaselineDocument> outputs,
    BaselineDiffVirtualFileSystem vfs,
    string currentDirectory,
    List<TestFile> inputFiles,
    List<TestFile> otherFiles,
    ICompilerOptions? originalOptions,
    string? originalCurrentDirectory,
    BaselineDiffContext context,
    Dictionary<string, string>? harnessSettings)
{
    public TypeScript.Compiler.Program Program { get; } = program;

    public ICompilerOptions Options { get; } = options;

    public List<Diagnostic> Diagnostics { get; } = diagnostics;

    /// <summary>Every document the host accepted, in the order it accepted it, deduplicated by name — the harness's
    /// <c>_outputsMap</c> over <c>outputs</c>.</summary>
    public List<BaselineDocument> Outputs { get; } = outputs;

    public BaselineDiffVirtualFileSystem Vfs { get; } = vfs;

    public string CurrentDirectory { get; } = currentDirectory;

    public List<TestFile> InputFiles { get; } = inputFiles;

    public List<TestFile> OtherFiles { get; } = otherFiles;

    /// <summary>src/harness/compilerImpl.ts:60-75 — the outputs classified the way the harness classifies them: a
    /// <c>.json</c> output counts as a js output, a <c>.d.ts</c> one as a declaration, a <c>.map</c> one as a source
    /// map, and anything else as JavaScript.</summary>
    public Dictionary<string, BaselineDocument> Js { get; } = Classify(outputs, static file =>
        Paths.FileExtensionIs(file, ".js") || Paths.FileExtensionIs(file, ".jsx") || Paths.FileExtensionIs(file, ".mjs") || Paths.FileExtensionIs(file, ".cjs")
        || Paths.FileExtensionIs(file, ".json"));

    public Dictionary<string, BaselineDocument> Dts { get; } = Classify(outputs, static file => Paths.FileExtensionIs(file, ".d.ts") || Paths.FileExtensionIs(file, ".d.mts") || Paths.FileExtensionIs(file, ".d.cts"));

    public Dictionary<string, BaselineDocument> Maps { get; } = Classify(outputs, static file => Paths.FileExtensionIs(file, ".map"));

    public string CommonSourceDirectory
    {
        get
        {
            var common = ((IModuleSpecifierResolutionHost)Program).GetCommonSourceDirectory();
            return common.Length == 0 ? "" : Paths.CombinePaths(Vfs.Cwd, common);
        }
    }

    /// <summary>src/harness/compilerImpl.ts:160-180 — <c>getOutputPath</c>.</summary>
    public string GetOutputPath(string path, string extension)
    {
        if (Options.OutFile is { } outFile)
        {
            path = Paths.GetNormalizedAbsolutePath(outFile, Vfs.Cwd);
        }
        else
        {
            path = Paths.GetNormalizedAbsolutePath(path, Vfs.Cwd);
            var outDir = extension is ".d.ts" or ".d.mts" or ".d.cts" || extension.EndsWith(".ts") || extension.Contains(".d.")
                ? Options.DeclarationDir ?? Options.OutDir
                : Options.OutDir;
            if (outDir is not null)
            {
                var common = CommonSourceDirectory;
                if (common.Length != 0)
                {
                    path = Paths.GetRelativePathFromDirectory(common, path, Vfs.UseCaseSensitiveFileNames);
                    path = Paths.CombinePaths(Paths.GetNormalizedAbsolutePath(Options.OutDir!, Vfs.Cwd), path);
                }
            }
        }

        return Paths.ChangeAnyExtension(path, extension, "", ignoreCase: true);
    }

    /// <summary>src/harness/compilerImpl.ts:182-195 — <c>getNumberOfJsFiles</c>.</summary>
    public int GetNumberOfJsFiles(bool includeJson)
    {
        if (includeJson) return Js.Count;
        return Js.Count(document => !Paths.FileExtensionIs(document.Key, ".json"));
    }

    /// <summary>src/harness/harnessIO.ts:379 — <c>result.repeat</c>: compile the same inputs again with extra
    /// settings merged over the case's own (the noCheck comparison the `.js` baseline runs).</summary>
    public BaselineCompilation Repeat(Dictionary<string, string> extraSettings)
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        if (harnessSettings is not null)
        {
            foreach (var (key, value) in harnessSettings) settings[key] = value;
        }

        foreach (var (key, value) in extraSettings) settings[key] = value;
        return BaselineDiffHarnessModel.CompileFiles(InputFiles, OtherFiles, settings, originalOptions, originalCurrentDirectory, context);
    }

    private static Dictionary<string, BaselineDocument> Classify(List<BaselineDocument> outputs, Func<string, bool> predicate)
    {
        var result = new Dictionary<string, BaselineDocument>(StringComparer.Ordinal);
        foreach (var document in outputs)
        {
            if (predicate(document.File)) result[document.File] = document;
        }

        return result;
    }
}

/// <summary>
/// The harness's compilation entry points — <c>Compiler.compileFiles</c> (src/harness/harnessIO.ts:333-380),
/// <c>prepareDeclarationCompilationContext</c>/<c>compileDeclarationFiles</c> (harnessIO.ts:385-480) and
/// <c>compilerImpl.compileFiles</c> (src/harness/compilerImpl.ts:265-300).
/// </summary>
internal static partial class BaselineDiffHarnessModel
{
    /// <summary>src/harness/harnessIO.ts:333-380 — <c>compileFiles</c>: build the options the harness's defaults and
    /// the case's directives describe, mount the units in the virtual file system, and compile.</summary>
    public static BaselineCompilation CompileFiles(
        List<TestFile> inputFiles,
        List<TestFile> otherFiles,
        Dictionary<string, string>? harnessSettings,
        ICompilerOptions? compilerOptions,
        string? currentDirectory,
        BaselineDiffContext context)
    {
        var originalCurrentDirectory = currentDirectory;
        var options = compilerOptions is null
            ? new BaselineDiffCompilerOptions { NoResolve = false }
            : CloneOptions(compilerOptions);
        options.NewLine ??= NewLineKind.CarriageReturnLineFeed;
        options.NoErrorTruncation = true;
        options.SkipDefaultLibCheck ??= true;
        currentDirectory ??= BaselineDiffVirtualFileSystem.SrcFolder;

        if (harnessSettings is not null) SetCompilerOptionsFromHarnessSetting(harnessSettings, options);

        // When a tsconfig is present, root names are already absolute; the port reports a config unit instead of
        // reading one, so this branch is the reference's own for the cases that reach it.
        var programFileNames = inputFiles
            .Select(file => options.ConfigFile is not null ? Paths.GetNormalizedAbsolutePath(file.UnitName, currentDirectory) : file.UnitName)
            .Where(fileName => !Paths.FileExtensionIs(fileName, ".json"))
            .ToList();

        // src/harness/harnessIO.ts:352-353 — the harness reads the option it was just handed, so the setting wins when
        // the test declared one and the answer is "case-sensitive" otherwise. The slot is not part of
        // ICompilerOptions (it is one of the harness’s own options), so it is read from the settings here.
        var useCaseSensitive = !(harnessSettings is not null && harnessSettings.TryGetValue("useCaseSensitiveFileNames", out var caseSensitiveRaw) ? caseSensitiveRaw.ToLowerInvariant() == "true" : true);
        var ignoreCase = !useCaseSensitive;
        var vfs = new BaselineDiffVirtualFileSystem(context.LibDirectory, context.TestLibDirectory, ignoreCase, currentDirectory);
        foreach (var file in inputFiles.Concat(otherFiles)) vfs.AddDocument(file.UnitName, file.Content);

        ConvertToOptionsWithAbsolutePaths(options, path => Paths.GetNormalizedAbsolutePath(path, currentDirectory));

        var host = PortProgram.CreateCompilerHostWorker(options, setParentNodes: true, new BaselineDiffSystem(vfs));
        var outputs = new List<BaselineDocument>();
        var outputIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        host.WriteFile = (fileName, data, writeByteOrderMark, _, _, _, _) =>
        {
            // src/harness/fakesHosts.ts:325-338 — the fake host's writeFile: the BOM flag is applied to the text, the
            // document is stored on the file system, and the output is recorded once per name in insertion order.
            var content = writeByteOrderMark ? BaselineDiffHarness.AddUTF8ByteOrderMark(data) : data;
            vfs.WriteFile(fileName, content);
            var document = new BaselineDocument(fileName, content);
            if (outputIndex.TryGetValue(fileName, out var index)) outputs[index] = document;
            else
            {
                outputIndex[fileName] = outputs.Count;
                outputs.Add(document);
            }
        };

        var (program, diagnostics) = CompileFilesWithHost(host, programFileNames, options);
        return new BaselineCompilation(
            program,
            options,
            diagnostics,
            outputs,
            vfs,
            currentDirectory,
            inputFiles,
            otherFiles,
            compilerOptions,
            originalCurrentDirectory,
            context,
            harnessSettings);
    }

    /// <summary>src/harness/compilerImpl.ts:265-300 — <c>compileFiles</c>: the program's options per the harness's
    /// defaults, the pre-emit program's diagnostics compared with the post-emit ones, and the emit itself. The project
    /// branch is unreachable here (a config unit is reported rather than parsed).</summary>
    private static (TypeScript.Compiler.Program Program, List<Diagnostic> Diagnostics) CompileFilesWithHost(
        ICompilerHost host,
        List<string> rootFiles,
        ICompilerOptions compilerOptions)
    {
        // establish defaults (aligns with old harness)
        compilerOptions.NewLine ??= NewLineKind.CarriageReturnLineFeed;
        compilerOptions.SkipDefaultLibCheck ??= true;
        compilerOptions.NoErrorTruncation ??= true;

        var skipErrorComparison = rootFiles.Count >= 100 || (compilerOptions.SkipLibCheck == true && compilerOptions.Declaration == true);
        var preProgram = (TypeScript.Compiler.Program?)null;
        var preErrors = (List<Diagnostic>?)null;
        if (!skipErrorComparison)
        {
            var preOptions = CloneOptions(compilerOptions);
            preOptions.ConfigFile = compilerOptions.ConfigFile;
            preOptions.TraceResolution = false;
            preProgram = PortProgram.CreateProgram(rootFiles, preOptions, host);
            preErrors = PortProgram.GetPreEmitDiagnostics(preProgram);
        }

        var program = PortProgram.CreateProgram(rootFiles, compilerOptions, host);
        program.Emit();
        var postErrors = PortProgram.GetPreEmitDiagnostics(program);
        var errors = preErrors is not null && preErrors.Count != postErrors.Count
            ? BuildCountMismatchErrors(preErrors, postErrors)
            : postErrors;
        return (program, errors);
    }

    /// <summary>src/harness/compilerImpl.ts:180-200 — the diagnostic the harness synthesizes when the pre-emit and
    /// post-emit diagnostic counts disagree: the shorter list, then a heading diagnostic carrying the excess as its
    /// related information.</summary>
    private static List<Diagnostic> BuildCountMismatchErrors(List<Diagnostic> preErrors, List<Diagnostic> postErrors)
    {
        var longer = preErrors.Count > postErrors.Count ? preErrors : postErrors;
        var shorter = ReferenceEquals(longer, preErrors) ? postErrors : preErrors;
        var excess = longer
            .Where(candidate => !shorter.Any(other => Utilities.CompareDiagnostics(candidate, other) == Comparison.EqualTo))
            .ToList();
        var heading = new Diagnostic(
            $"Pre-emit ({preErrors.Count}) and post-emit ({postErrors.Count}) diagnostic counts do not match! This can indicate that a semantic _error_ was added by the emit resolver - such an error may not be reflected on the command line or in the editor, but may be captured in a baseline here!",
            DiagnosticCategory.Error,
            -1);
        var excessHeading = new Diagnostic("The excess diagnostics are:", DiagnosticCategory.Error, -1);
        var result = new List<Diagnostic>(shorter) { Utilities.AddRelatedInfo(heading, [excessHeading, .. excess]) };
        return result;
    }

    /// <summary>src/harness/harnessIO.ts:385-430 — <c>prepareDeclarationCompilationContext</c>: the emitted
    /// declaration files become the inputs of a second compilation, which is how the harness checks that the
    /// declarations it wrote compile.</summary>
    public static BaselineDeclarationContext? PrepareDeclarationCompilationContext(
        List<TestFile> toBeCompiled,
        List<TestFile> otherFiles,
        BaselineCompilation result,
        Dictionary<string, string> harnessSettings,
        ICompilerOptions options,
        string? currentDirectory,
        BaselineDiffContext context)
    {
        var declInputFiles = new List<TestFile>();
        var declOtherFiles = new List<TestFile>();
        var found = new Dictionary<string, TestFile>(StringComparer.Ordinal);

        // src/harness/harnessIO.ts:395-407 — the harness's own invariants about a declaration-only mode.
        if (options.Declaration == true && result.Diagnostics.Count == 0)
        {
            if (options.EmitDeclarationOnly == true)
            {
                if (result.Js.Count > 0 || (result.Dts.Count == 0 && options.NoEmit != true))
                {
                    throw new BaselineModelException("Only declaration files should be generated when emitDeclarationOnly:true");
                }
            }
            else if (result.Dts.Count != result.GetNumberOfJsFiles(includeJson: false))
            {
                throw new BaselineModelException("There were no errors and declFiles generated did not match number of js files generated");
            }
        }

        if (options.Declaration == true && result.Diagnostics.Count == 0 && result.Dts.Count > 0)
        {
            foreach (var file in toBeCompiled) AddDtsFile(file, declInputFiles);
            foreach (var file in otherFiles) AddDtsFile(file, declOtherFiles);
            return new BaselineDeclarationContext(declInputFiles, declOtherFiles, harnessSettings, options, currentDirectory ?? (harnessSettings.TryGetValue("currentDirectory", out var cd) ? cd : BaselineDiffVirtualFileSystem.SrcFolder), context);
        }

        return null;

        void AddDtsFile(TestFile file, List<TestFile> dtsFiles)
        {
            if (Paths.FileExtensionIs(file.UnitName, ".d.ts") || Paths.FileExtensionIs(file.UnitName, ".json"))
            {
                dtsFiles.Add(file);
            }
            else if (Paths.FileExtensionIs(file.UnitName, ".ts") || Paths.FileExtensionIs(file.UnitName, ".tsx")
                || (IsJavaScriptName(file.UnitName) && options.AllowJs == true))
            {
                var declaration = FindResultCodeFile(file.UnitName);
                if (declaration is not null && !found.ContainsKey(declaration.File) && !declInputFiles.Any(f => f.UnitName == declaration.File) && !declOtherFiles.Any(f => f.UnitName == declaration.File))
                {
                    found[declaration.File] = new TestFile(declaration.File, BaselineDiffHarness.RemoveByteOrderMark(declaration.Text));
                    dtsFiles.Add(found[declaration.File]);
                }
            }
        }

        BaselineDocument? FindResultCodeFile(string fileName)
        {
            var sourceFile = result.Program.GetSourceFile(fileName) ?? throw new BaselineModelException($"Program has no source file with name '{fileName}'");
            string sourceFileName;
            if (options.OutFile is not { } outFile)
            {
                if (options.OutDir is not null)
                {
                    var sourceFilePath = Paths.GetNormalizedAbsolutePath(sourceFile.FileName!, result.Vfs.Cwd);
                    sourceFilePath = sourceFilePath.Replace(((IModuleSpecifierResolutionHost)result.Program).GetCommonSourceDirectory(), "");
                    sourceFileName = Paths.CombinePaths(options.OutDir, sourceFilePath);
                }
                else
                {
                    sourceFileName = sourceFile.FileName!;
                }
            }
            else
            {
                sourceFileName = outFile;
            }

            var dtsFileName = Utilities.RemoveFileExtension(sourceFileName) + Utilities.GetDeclarationEmitExtensionForPath(sourceFileName);
            return result.Dts.GetValueOrDefault(dtsFileName);
        }
    }

    private static bool IsJavaScriptName(string name)
        => Paths.FileExtensionIs(name, ".js") || Paths.FileExtensionIs(name, ".jsx") || Paths.FileExtensionIs(name, ".mjs") || Paths.FileExtensionIs(name, ".cjs");

    /// <summary>src/harness/harnessIO.ts:472-480 — <c>compileDeclarationFiles</c>: <c>if (!context) return;</c>, so a
    /// case that produced no declarations to re-compile answers <c>null</c> rather than failing.</summary>
    public static BaselineCompilation? CompileDeclarationFiles(BaselineDeclarationContext? context) => context is null
        ? null
        : CompileFiles(context.DeclInputFiles, context.DeclOtherFiles, context.HarnessSettings, context.Options, context.CurrentDirectory, context.Context);

    /// <summary><c>JSON.stringify</c> of a string, for the one message the model spells with a quoted value.</summary>
    private static string JsonEscape(string value) => JsonSerializer.Serialize(value);
}

/// <summary>The second compilation's inputs — <c>Compiler.DeclarationCompilationContext</c>.</summary>
internal sealed record BaselineDeclarationContext(
    List<TestFile> DeclInputFiles,
    List<TestFile> DeclOtherFiles,
    Dictionary<string, string> HarnessSettings,
    ICompilerOptions Options,
    string CurrentDirectory,
    BaselineDiffContext Context);
