using System.Text.Json;

namespace TypeScript.Compiler;

// ported from: src/compiler/emitter.ts:1-1190 — the non-printer half of the emitter: 431-444 (isBuildInfoFile —
// already landed as `Utilities.IsBuildInfoFile`), 446-474 (forEachEmittedFile), 481-504 (getTsBuildInfoEmitOutputFilePath,
// canEmitTsBuildInfo), 506-535 (getOutputPathsForBundle, getOutputPathsFor, getSourceMapFilePath), 540-546
// (getOutputExtension), 548-561 (getOutputPathWithoutChangingExt), 563-591 (getOutputDeclarationFileName(+Worker),
// getOutputJSFileName(+Worker)), 597-634 (createAddOutput, getSingleOutputFileNames, getOwnOutputFileNames),
// 695-744 (getAllProjectOutputs, getOutputFileNames, getFirstProjectOutput), 746-750
// (emitResolverSkipsTypeChecking), 752-1145 (emitFiles), 1137-1145 (getBuildInfoText, getBuildInfo).
//
// PORT-NOTE (the module): TS exports these as module-level functions; C# has no module scope, so they are the members
// of the class named after the module (the treatment `TransformationUtilities` and `Printer` record). The printer
// itself is `Emit/Printer*.cs` and is called, never re-ported.
internal static partial class Emitter
{
    /// <summary>ported from: src/compiler/emitter.ts:446-474 — <c>forEachEmittedFile</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's callback takes both <c>emitFileNames</c> and <c>sourceFileOrBundle</c> and returns
    /// <c>T</c>, which short-circuits the iteration when truthy. The port states that <c>T</c> with the
    /// <c>where T : class</c> constraint the truthiness read needs (<see cref="Js.IsTruthy{T}"/>); <c>emitFiles</c> and
    /// the program's verify path both pass an action that answers nothing, so the early-exit arm is the reference's own
    /// <c>T = void</c> instantiation of it.
    /// <para>
    /// PORT-NOTE: the reference's <c>isArray(sourceFilesOrTargetSourceFile)</c> is the port's test for the list arm of
    /// the union <c>readonly SourceFile[] | SourceFile</c>, which is <c>object?</c> here — the treatment
    /// <c>Program.ForEachEmittedFile</c> already records for the same generator gap.
    /// </para>
    /// </remarks>
    internal static T? ForEachEmittedFile<T>(
        IEmitHost host,
        Func<IEmitFileNames, Node?, T?> action,
        object? sourceFilesOrTargetSourceFile = null,
        bool forceDtsEmit = false,
        bool? onlyBuildInfo = null,
        bool? includeBuildInfo = null)
        where T : class
    {
        var sourceFiles = sourceFilesOrTargetSourceFile is List<SourceFile> sourceFileList
            ? sourceFileList
            : Utilities.GetSourceFilesToEmit(host, sourceFilesOrTargetSourceFile as SourceFile, forceDtsEmit);
        var options = Utilities.EmitHostCompilerOptions(host);
        if (!Js.IsTruthy(onlyBuildInfo))
        {
            if (options.OutFile is not null)
            {
                if (sourceFiles.Count != 0)
                {
                    var bundle = NodeFactory.Factory.CreateBundle(sourceFiles);
                    var result = action(GetOutputPathsFor(bundle, host, forceDtsEmit), bundle);
                    if (Js.IsTruthy(result))
                    {
                        return result;
                    }
                }
            }
            else
            {
                foreach (var sourceFile in sourceFiles)
                {
                    var result = action(GetOutputPathsFor(sourceFile, host, forceDtsEmit), sourceFile);
                    if (Js.IsTruthy(result))
                    {
                        return result;
                    }
                }
            }
        }

        if (Js.IsTruthy(includeBuildInfo))
        {
            var buildInfoPath = GetTsBuildInfoEmitOutputFilePath(options);
            if (Js.IsTruthy(buildInfoPath)) return action(new EmitFileNames { BuildInfoPath = buildInfoPath }, /*sourceFileOrBundle*/ null);
        }

        return null;
    }

    /// <summary>ported from: src/compiler/emitter.ts:481-497 — <c>getTsBuildInfoEmitOutputFilePath</c>.</summary>
    /// <remarks>PORT-NOTE: <c>configFile</c>/<c>outPath</c> are the option string slots, tested with <c>is not null</c>
    /// (the spelling the emit-boundary slice fixed); <see cref="Extension.TsBuildInfo"/> is read through
    /// <see cref="Extension.Value"/>.</remarks>
    internal static string? GetTsBuildInfoEmitOutputFilePath(ICompilerOptions options)
    {
        var configFile = options.ConfigFilePath;
        if (!CanEmitTsBuildInfo(options)) return null;
        if (options.TsBuildInfoFile is not null) return options.TsBuildInfoFile;
        var outPath = options.OutFile;
        string buildInfoExtensionLess;
        if (outPath is not null)
        {
            buildInfoExtensionLess = Utilities.RemoveFileExtension(outPath);
        }
        else
        {
            if (configFile is null) return null;
            var configFileExtensionLess = Utilities.RemoveFileExtension(configFile);
            buildInfoExtensionLess = options.OutDir is not null
                ? options.RootDir is not null
                    ? Paths.ResolvePath(options.OutDir, Paths.GetRelativePathFromDirectory(options.RootDir, configFileExtensionLess, /*ignoreCase*/ true))
                    : Paths.CombinePaths(options.OutDir, Paths.GetBaseFileName(configFileExtensionLess))
                : configFileExtensionLess;
        }

        return buildInfoExtensionLess + Extension.TsBuildInfo.Value;
    }

    /// <summary>ported from: src/compiler/emitter.ts:502-504 — <c>canEmitTsBuildInfo</c>.</summary>
    private static bool CanEmitTsBuildInfo(ICompilerOptions options)
        => Utilities.IsIncrementalCompilation(options) || Js.IsTruthy(options.TscBuild);

    /// <summary>ported from: src/compiler/emitter.ts:506-514 — <c>getOutputPathsForBundle</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>outPath</c> is <c>options.outFile!</c>; the port asserts the same
    /// expectation, because this member is only reached through the <c>outFile</c> arm of its callers.</remarks>
    private static EmitFileNames GetOutputPathsForBundle(ICompilerOptions options, bool forceDtsPaths)
    {
        var outPath = options.OutFile!;
        var jsFilePath = Js.IsTruthy(options.EmitDeclarationOnly) ? null : outPath;
        var sourceMapFilePath = Js.IsTruthy(jsFilePath) ? GetSourceMapFilePath(jsFilePath, options) : null;
        var declarationFilePath = forceDtsPaths || Utilities.GetEmitDeclarations(options)
            ? Utilities.RemoveFileExtension(outPath) + Extension.Dts.Value
            : null;
        var declarationMapPath = Js.IsTruthy(declarationFilePath) && Utilities.GetAreDeclarationMapsEnabled(options)
            ? declarationFilePath + ".map"
            : null;
        return new EmitFileNames
        {
            JsFilePath = jsFilePath,
            SourceMapFilePath = sourceMapFilePath,
            DeclarationFilePath = declarationFilePath,
            DeclarationMapPath = declarationMapPath,
        };
    }

    /// <summary>ported from: src/compiler/emitter.ts:516-533 — <c>getOutputPathsFor</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is the union <c>SourceFile | Bundle</c>, which an <c>isBundle</c> test splits;
    /// the port takes the common <see cref="Node"/> base and splits on the kind (the union's usual treatment). The
    /// reference's <c>comparePaths(...)</c> is <see cref="Paths.ComparePaths(string, string, string, bool)"/>.</remarks>
    internal static EmitFileNames GetOutputPathsFor(Node sourceFileOrBundle, IEmitHost host, bool forceDtsPaths)
    {
        var options = Utilities.EmitHostCompilerOptions(host);
        if (sourceFileOrBundle.Kind == SyntaxKind.Bundle)
        {
            return GetOutputPathsForBundle(options, forceDtsPaths);
        }

        var sourceFile = (SourceFile)sourceFileOrBundle;
        var ownOutputFilePath = Utilities.GetOwnEmitOutputFilePath(sourceFile.FileName ?? "", host, GetOutputExtension(sourceFile.FileName ?? "", options));
        var isJsonFile = Utilities.IsJsonSourceFile(sourceFile);
        // If json file emits to the same location skip writing it, if emitDeclarationOnly skip writing it
        var isJsonEmittedToSameLocation = isJsonFile &&
            Paths.ComparePaths(sourceFile.FileName ?? "", ownOutputFilePath, Utilities.EmitHostCurrentDirectory(host), !Utilities.EmitHostUseCaseSensitiveFileNames(host)) == Comparison.EqualTo;
        var jsFilePath = Js.IsTruthy(options.EmitDeclarationOnly) || isJsonEmittedToSameLocation ? null : ownOutputFilePath;
        var sourceMapFilePath = !Js.IsTruthy(jsFilePath) || Utilities.IsJsonSourceFile(sourceFile) ? null : GetSourceMapFilePath(jsFilePath!, options);
        var declarationFilePath = forceDtsPaths || (Utilities.GetEmitDeclarations(options) && !isJsonFile)
            ? Utilities.GetDeclarationEmitOutputFilePath(sourceFile.FileName ?? "", host)
            : null;
        var declarationMapPath = Js.IsTruthy(declarationFilePath) && Utilities.GetAreDeclarationMapsEnabled(options)
            ? declarationFilePath + ".map"
            : null;
        return new EmitFileNames
        {
            JsFilePath = jsFilePath,
            SourceMapFilePath = sourceMapFilePath,
            DeclarationFilePath = declarationFilePath,
            DeclarationMapPath = declarationMapPath,
        };
    }

    /// <summary>ported from: src/compiler/emitter.ts:535-537 — <c>getSourceMapFilePath</c>.</summary>
    private static string? GetSourceMapFilePath(string jsFilePath, ICompilerOptions options)
        => Js.IsTruthy(options.SourceMap) && !Js.IsTruthy(options.InlineSourceMap) ? jsFilePath + ".map" : null;

    /// <summary>ported from: src/compiler/emitter.ts:540-546 — <c>getOutputExtension</c>.</summary>
    /// <remarks>
    /// PORT-NOTE (home): this member's reference home is <c>emitter.ts</c>, and its first ported reader was
    /// <c>TransformationUtilities.RewriteModuleSpecifier</c>, which stated a copy of it under the "the reader declares
    /// it" rule; this slice owns the home, so that copy is gone and the reader calls this one.
    /// <para>
    /// PORT-NOTE: TS's return type is the union <c>Extension</c> (a set of string-literal members), and its parameter is
    /// <c>Pick&lt;CompilerOptions, "jsx"&gt;</c>; the port states <c>string</c> and the whole
    /// <see cref="ICompilerOptions"/>, reading each member's <see cref="Extension.Value"/>, because the one reader is
    /// the <c>extension</c> argument of <c>getOwnEmitOutputFilePath</c>.
    /// </para>
    /// </remarks>
    internal static string GetOutputExtension(string fileName, ICompilerOptions options)
        => Paths.FileExtensionIs(fileName, Extension.Json.Value) ? Extension.Json.Value
            : options.Jsx == JsxEmit.Preserve && Paths.FileExtensionIsOneOf(fileName, [Extension.Jsx.Value, Extension.Tsx.Value]) ? Extension.Jsx.Value
            : Paths.FileExtensionIsOneOf(fileName, [Extension.Mts.Value, Extension.Mjs.Value]) ? Extension.Mjs.Value
            : Paths.FileExtensionIsOneOf(fileName, [Extension.Cts.Value, Extension.Cjs.Value]) ? Extension.Cjs.Value
            : Extension.Js.Value;

    /// <summary>ported from: src/compiler/emitter.ts:548-561 — <c>getOutputPathWithoutChangingExt</c>.</summary>
    /// <remarks>PORT-NOTE: <c>outputDir</c> is the option slot, tested with <c>is not null</c>; the reference's
    /// <c>getRelativePathFromDirectory</c> is <see cref="Paths.GetRelativePathFromDirectory(string, string, bool)"/>.
    /// </remarks>
    private static string GetOutputPathWithoutChangingExt(
        string inputFileName,
        bool ignoreCase,
        string? outputDir,
        Func<string> getCommonSourceDirectory)
        => outputDir is not null
            ? Paths.ResolvePath(
                outputDir,
                Paths.GetRelativePathFromDirectory(getCommonSourceDirectory(), inputFileName, ignoreCase))
            : inputFileName;

    /// <summary>ported from: src/compiler/emitter.ts:563-570 — <c>getOutputDeclarationFileName</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's fourth parameter defaults to
    /// <c>() =&gt; getCommonSourceDirectoryOfConfig(configFile, ignoreCase)</c>; the port's null default is the "not
    /// supplied" state (the shape <c>Program.GetOutputDeclarationFileName</c> already declared), and the reference's
    /// default is supplied here.</remarks>
    internal static string GetOutputDeclarationFileName(
        string inputFileName,
        IParsedCommandLine configFile,
        bool ignoreCase,
        Func<string>? getCommonSourceDirectory = null)
        => GetOutputDeclarationFileNameWorker(
            inputFileName,
            configFile.Options!,
            ignoreCase,
            getCommonSourceDirectory ?? (() => Program.GetCommonSourceDirectoryOfConfig(configFile, ignoreCase)));

    /// <summary>ported from: src/compiler/emitter.ts:573-578 — <c>getOutputDeclarationFileNameWorker</c>.</summary>
    internal static string GetOutputDeclarationFileNameWorker(string inputFileName, ICompilerOptions options, bool ignoreCase, Func<string> getCommonSourceDirectory)
        => Utilities.ChangeExtension(
            GetOutputPathWithoutChangingExt(inputFileName, ignoreCase, options.DeclarationDir ?? options.OutDir, getCommonSourceDirectory),
            Utilities.GetDeclarationEmitExtensionForPath(inputFileName));

    /// <summary>ported from: src/compiler/emitter.ts:580-588 — <c>getOutputJSFileName</c>.</summary>
    /// <remarks>PORT-NOTE: <c>configFile.options.emitDeclarationOnly</c> answers the number <c>1</c> for
    /// <c>undefined</c>, which is why the reference's bare <c>return undefined</c> is the port's null; the comparison's
    /// third argument is the reference's <c>Debug.checkDefined(configFile.options.configFilePath)</c>.</remarks>
    private static string? GetOutputJSFileName(string inputFileName, IParsedCommandLine configFile, bool ignoreCase, Func<string>? getCommonSourceDirectory = null)
    {
        if (Js.IsTruthy(configFile.Options!.EmitDeclarationOnly)) return null;
        var isJsonFile = Paths.FileExtensionIs(inputFileName, Extension.Json.Value);
        var outputFileName = GetOutputJSFileNameWorker(
            inputFileName,
            configFile.Options,
            ignoreCase,
            getCommonSourceDirectory ?? (() => Program.GetCommonSourceDirectoryOfConfig(configFile, ignoreCase)));
        return !isJsonFile || Paths.ComparePaths(inputFileName, outputFileName, Debug.CheckDefined(configFile.Options.ConfigFilePath), ignoreCase) != Comparison.EqualTo
            ? outputFileName
            : null;
    }

    /// <summary>ported from: src/compiler/emitter.ts:590-595 — <c>getOutputJSFileNameWorker</c>.</summary>
    internal static string GetOutputJSFileNameWorker(string inputFileName, ICompilerOptions options, bool ignoreCase, Func<string> getCommonSourceDirectory)
        => Utilities.ChangeExtension(
            GetOutputPathWithoutChangingExt(inputFileName, ignoreCase, options.OutDir, getCommonSourceDirectory),
            GetOutputExtension(inputFileName, options));

    /// <summary>ported from: src/compiler/emitter.ts:597-608 — <c>createAddOutput</c>.</summary>
    /// <remarks>PORT-NOTE: the reference returns the two-closure literal <c>{ addOutput, getOutputs }</c>; the port
    /// states it as the class below, which is the same pair over one shared list
    /// (<see cref="Core.EmptyArray{T}"/>'s "hand the input back" rule is why an untouched collector answers the empty
    /// list).</remarks>
    private sealed class AddOutput
    {
        private List<string>? _outputs;

        /// <summary>ported from: src/compiler/emitter.ts:600-604 — <c>addOutput</c>.</summary>
        public void Add(string? path)
        {
            if (Js.IsTruthy(path))
            {
                (_outputs ??= []).Add(path!);
            }
        }

        /// <summary>ported from: src/compiler/emitter.ts:605-607 — <c>getOutputs</c>.</summary>
        public List<string> GetOutputs() => _outputs ?? [];
    }

    /// <summary>ported from: src/compiler/emitter.ts:610-616 — <c>getSingleOutputFileNames</c>.</summary>
    private static void GetSingleOutputFileNames(IParsedCommandLine configFile, AddOutput addOutput)
    {
        var fileNames = GetOutputPathsForBundle(configFile.Options!, /*forceDtsPaths*/ false);
        addOutput.Add(fileNames.JsFilePath);
        addOutput.Add(fileNames.SourceMapFilePath);
        addOutput.Add(fileNames.DeclarationFilePath);
        addOutput.Add(fileNames.DeclarationMapPath);
    }

    /// <summary>ported from: src/compiler/emitter.ts:618-634 — <c>getOwnOutputFileNames</c>.</summary>
    /// <remarks>PORT-NOTE: <c>js &amp;&amp; configFile.options.sourceMap</c> is the string-truthiness test the reference's
    /// <c>&amp;&amp;</c> makes, and the two <c>`${…}.map`</c> template literals are the concatenations below.</remarks>
    private static void GetOwnOutputFileNames(
        IParsedCommandLine configFile,
        string inputFileName,
        bool ignoreCase,
        AddOutput addOutput,
        Func<string>? getCommonSourceDirectory = null)
    {
        if (Parser.IsDeclarationFileName(inputFileName)) return;
        var js = GetOutputJSFileName(inputFileName, configFile, ignoreCase, getCommonSourceDirectory);
        addOutput.Add(js);
        if (Paths.FileExtensionIs(inputFileName, Extension.Json.Value)) return;
        if (Js.IsTruthy(js) && Js.IsTruthy(configFile.Options!.SourceMap))
        {
            addOutput.Add($"{js}.map");
        }

        if (Utilities.GetEmitDeclarations(configFile.Options))
        {
            var dts = GetOutputDeclarationFileName(inputFileName, configFile, ignoreCase, getCommonSourceDirectory);
            addOutput.Add(dts);
            if (Js.IsTruthy(configFile.Options!.DeclarationMap))
            {
                addOutput.Add($"{dts}.map");
            }
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:695-705 — <c>getAllProjectOutputs</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>memoize(() =&gt; getCommonSourceDirectoryOfConfig(configFile, ignoreCase))</c> is
    /// <see cref="Core.Memoize{T}"/>, the port's <c>memoize</c>.</remarks>
    internal static List<string> GetAllProjectOutputs(IParsedCommandLine configFile, bool ignoreCase)
    {
        var addOutput = new AddOutput();
        if (configFile.Options!.OutFile is not null)
        {
            GetSingleOutputFileNames(configFile, addOutput);
        }
        else
        {
            var getCommonSourceDirectory = Core.Memoize(() => Program.GetCommonSourceDirectoryOfConfig(configFile, ignoreCase));
            foreach (var inputFileName in configFile.FileNames ?? [])
            {
                GetOwnOutputFileNames(configFile, inputFileName, ignoreCase, addOutput, getCommonSourceDirectory);
            }
        }

        addOutput.Add(GetTsBuildInfoEmitOutputFilePath(configFile.Options));
        return addOutput.GetOutputs();
    }

    /// <summary>ported from: src/compiler/emitter.ts:710-722 — <c>getOutputFileNames</c>.</summary>
    internal static List<string> GetOutputFileNames(IParsedCommandLine commandLine, string inputFileName, bool ignoreCase)
    {
        inputFileName = Paths.NormalizePath(inputFileName);
        Debug.Assert(Core.Contains(commandLine.FileNames, inputFileName), "Expected fileName to be present in command line");
        var addOutput = new AddOutput();
        if (commandLine.Options!.OutFile is not null)
        {
            GetSingleOutputFileNames(commandLine, addOutput);
        }
        else
        {
            GetOwnOutputFileNames(commandLine, inputFileName, ignoreCase, addOutput);
        }

        return addOutput.GetOutputs();
    }

    /// <summary>ported from: src/compiler/emitter.ts:724-744 — <c>getFirstProjectOutput</c>.</summary>
    internal static string GetFirstProjectOutput(IParsedCommandLine configFile, bool ignoreCase)
    {
        if (configFile.Options!.OutFile is not null)
        {
            var fileNames = GetOutputPathsForBundle(configFile.Options, /*forceDtsPaths*/ false);
            return Debug.CheckDefined(
                fileNames.JsFilePath ?? fileNames.DeclarationFilePath,
                $"project {configFile.Options.ConfigFilePath} expected to have at least one output");
        }

        var getCommonSourceDirectory = Core.Memoize(() => Program.GetCommonSourceDirectoryOfConfig(configFile, ignoreCase));
        foreach (var inputFileName in configFile.FileNames ?? [])
        {
            if (Parser.IsDeclarationFileName(inputFileName)) continue;
            var jsFilePath = GetOutputJSFileName(inputFileName, configFile, ignoreCase, getCommonSourceDirectory);
            if (Js.IsTruthy(jsFilePath)) return jsFilePath!;
            if (Paths.FileExtensionIs(inputFileName, Extension.Json.Value)) continue;
            if (Utilities.GetEmitDeclarations(configFile.Options))
            {
                return GetOutputDeclarationFileName(inputFileName, configFile, ignoreCase, getCommonSourceDirectory);
            }
        }

        var buildInfoPath = GetTsBuildInfoEmitOutputFilePath(configFile.Options);
        if (Js.IsTruthy(buildInfoPath)) return buildInfoPath!;
        return Debug.Fail<string>($"project {configFile.Options.ConfigFilePath} expected to have at least one output");
    }

    /// <summary>ported from: src/compiler/emitter.ts:746-750 — <c>emitResolverSkipsTypeChecking</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameters are the unions <c>boolean | EmitOnly</c>; the port takes them as
    /// <c>object?</c> (the erasure <c>EmitFiles</c> uses for the same slot) and reads them with JS truthiness.</remarks>
    internal static bool EmitResolverSkipsTypeChecking(object? emitOnly, bool? forceDtsEmit)
        => Js.IsTruthy(forceDtsEmit) && Js.IsTruthy(emitOnly);

    /// <summary>ported from: src/compiler/emitter.ts:752-1145 — <c>emitFiles</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the reference declares this function with a destructured transformer list and a set of <c>var</c>s it
    /// calls out as deliberate ("It avoids TDZ checks in the runtime which can be costly"), then closes a family of
    /// local functions over them; the port is the same body, whose local functions are C# local functions over the same
    /// locals (the header's note on `TransformationContext` is the other shape the reference's closures take).
    /// </para>
    /// <para>
    /// PORT-NOTE (<c>emitOnly</c>): TS's slot is <c>boolean | EmitOnly</c>, whose members are what the four emits'
    /// tests distinguish (<c>EmitOnly.Js = 0</c> is falsy, <c>EmitOnly.Dts = 1</c> and
    /// <c>EmitOnly.BuilderSignature = 2</c> are truthy). The generated <c>IProgram.Emit</c> flattens the union to
    /// <c>bool?</c>, which cannot express the two <c>EmitOnly</c> arms, so this member takes the union as <c>object?</c>
    /// and each test is stated in the reference's own terms.
    /// </para>
    /// <para>
    /// PORT-NOTE (<c>transformers</c>): the generated <c>IEmitTransformers</c>' element type is
    /// <c>Func&lt;ITransformationContext, …&gt;</c>, which no port value can be built for (see
    /// <c>Transforms/Transformer.cs</c>'s header), so the parameter is the port's own
    /// <see cref="EmitTransformers"/>.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>createSourceMapGenerator</c> is the one dependency of this member that has not landed
    /// (<c>sourcemap.ts</c>, the M7-B source-map slice); the call site keeps the reference's call and the named blocker
    /// reports it.
    /// </para>
    /// </remarks>
    internal static IEmitResult EmitFiles(
        IEmitResolver resolver,
        IEmitHost host,
        SourceFile? targetSourceFile,
        EmitTransformers transformers,
        object? emitOnly,
        bool onlyBuildInfo,
        bool? forceDtsEmit = null,
        bool? skipBuildInfo = null)
    {
        var scriptTransformers = transformers.ScriptTransformers!;
        var declarationTransformers = transformers.DeclarationTransformers!;

        var compilerOptions = Utilities.EmitHostCompilerOptions(host);
        List<ISourceMapEmitResult>? sourceMapDataList = Js.IsTruthy(compilerOptions.SourceMap) || Js.IsTruthy(compilerOptions.InlineSourceMap) || Utilities.GetAreDeclarationMapsEnabled(compilerOptions) ? [] : null;
        List<string>? emittedFilesList = Js.IsTruthy(compilerOptions.ListEmittedFiles) ? [] : null;
        var emitterDiagnostics = Utilities.CreateDiagnosticCollection();
        var newLine = Utilities.GetNewLineCharacter(compilerOptions);
        var writer = (EmitTextWriter)Utilities.CreateTextWriter(newLine);
        var printTimer = Performance.CreateTimer("printTime", "beforePrint", "afterPrint");
        var emitSkipped = false;

        // Emit each output file
        printTimer.Enter();
        ForEachEmittedFile<object>(
            host,
            (fileNames, sourceFileOrBundle) =>
            {
                EmitSourceFileOrBundle(fileNames, sourceFileOrBundle);
                return null;
            },
            Utilities.GetSourceFilesToEmit(host, targetSourceFile, forceDtsEmit),
            Js.IsTruthy(forceDtsEmit),
            onlyBuildInfo,
            targetSourceFile is null && !Js.IsTruthy(skipBuildInfo));
        printTimer.Exit();

        return new EmitResult
        {
            EmitSkipped = emitSkipped,
            Diagnostics = [.. emitterDiagnostics.GetDiagnostics()],
            EmittedFiles = emittedFilesList,
            SourceMaps = sourceMapDataList,
        };

        void EmitSourceFileOrBundle(IEmitFileNames fileNames, Node? sourceFileOrBundle)
        {
            Tracing.Current?.Push(Tracing.Phase.Emit, "emitJsFileOrBundle", TracingArgs.Of(("jsFilePath", fileNames.JsFilePath)));
            EmitJsFileOrBundle(sourceFileOrBundle, fileNames.JsFilePath, fileNames.SourceMapFilePath);
            Tracing.Current?.Pop();

            Tracing.Current?.Push(Tracing.Phase.Emit, "emitDeclarationFileOrBundle", TracingArgs.Of(("declarationFilePath", fileNames.DeclarationFilePath)));
            EmitDeclarationFileOrBundle(sourceFileOrBundle, fileNames.DeclarationFilePath, fileNames.DeclarationMapPath);
            Tracing.Current?.Pop();

            Tracing.Current?.Push(Tracing.Phase.Emit, "emitBuildInfo", TracingArgs.Of(("buildInfoPath", fileNames.BuildInfoPath)));
            EmitBuildInfo(fileNames.BuildInfoPath);
            Tracing.Current?.Pop();
        }

        void EmitBuildInfo(string? buildInfoPath)
        {
            // Write build information if applicable
            if (buildInfoPath is null || targetSourceFile is not null) return;
            if (host.IsEmitBlocked(buildInfoPath))
            {
                emitSkipped = true;
                return;
            }

            var buildInfo = host.GetBuildInfo() ?? new BuildInfo { Version = VersionInfo.Version };
            // Pass buildinfo as additional data to avoid having to reparse
            Utilities.WriteFile(
                host,
                emitterDiagnostics,
                buildInfoPath,
                GetBuildInfoText(buildInfo),
                /*writeByteOrderMark*/ false,
                /*sourceFiles*/ null,
                new WriteFileCallbackData { BuildInfo = buildInfo });
            emittedFilesList?.Add(buildInfoPath);
        }

        void EmitJsFileOrBundle(Node? sourceFileOrBundle, string? jsFilePath, string? sourceMapFilePath)
        {
            if (sourceFileOrBundle is null || Js.IsTruthy(emitOnly) || jsFilePath is null)
            {
                return;
            }

            // Make sure not to write js file and source map file if any of them cannot be written
            if (host.IsEmitBlocked(jsFilePath) || Js.IsTruthy(compilerOptions.NoEmit))
            {
                emitSkipped = true;
                return;
            }

            // PORT-NOTE: TS's `(isSourceFile(sourceFileOrBundle) ? [sourceFileOrBundle] : filter(sourceFileOrBundle.sourceFiles, isSourceFileNotJson)).forEach(…)`
            // is the port's two-arm list walk; `Core.Filter`'s answer is the `IList` face, so the JS arm's one-element
            // list and it are narrowed to the list type the walk reads.
            var filesToMark = NodeTests.IsSourceFile(sourceFileOrBundle)
                ? new List<SourceFile> { (SourceFile)sourceFileOrBundle }
                : (List<SourceFile>)Core.Filter(((Bundle)sourceFileOrBundle).SourceFiles, Utilities.IsSourceFileNotJson)!;
            foreach (var sourceFile in filesToMark)
            {
                if (
                    Js.IsTruthy(compilerOptions.NoCheck)
                    || !Utilities.CanIncludeBindAndCheckDiagnostics(sourceFile, compilerOptions)
                ) MarkLinkedReferences(sourceFile);
            }

            // Transform the source files
            var transform = Transformer.TransformNodes(
                resolver,
                host,
                NodeFactory.Factory,
                compilerOptions,
                new List<Node> { sourceFileOrBundle },
                scriptTransformers,
                /*allowDtsFiles*/ false);

            var printerOptions = new PrinterOptions
            {
                RemoveComments = compilerOptions.RemoveComments,
                NewLine = compilerOptions.NewLine,
                NoEmitHelpers = compilerOptions.NoEmitHelpers,
                Module = Utilities.GetEmitModuleKind(compilerOptions),
                ModuleResolution = Utilities.GetEmitModuleResolutionKind(compilerOptions),
                Target = Utilities.GetEmitScriptTarget(compilerOptions),
                SourceMap = compilerOptions.SourceMap,
                InlineSourceMap = compilerOptions.InlineSourceMap,
                InlineSources = compilerOptions.InlineSources,
                ExtendedDiagnostics = compilerOptions.ExtendedDiagnostics,
            };

            // Create a printer to print the nodes
            var printer = new Printer(printerOptions, new EmitPrintHandlers(resolver, transform));

            Debug.Assert(transform.Transformed!.Count == 1, "Should only see one output from the transform");
            PrintSourceFileOrBundle(jsFilePath, sourceMapFilePath, transform, printer, SourceMapOptions.Of(compilerOptions));

            // Clean up emit nodes on parse tree
            transform.Dispose();

            if (emittedFilesList is not null)
            {
                emittedFilesList.Add(jsFilePath);
                if (sourceMapFilePath is not null)
                {
                    emittedFilesList.Add(sourceMapFilePath);
                }
            }
        }

        void EmitDeclarationFileOrBundle(Node? sourceFileOrBundle, string? declarationFilePath, string? declarationMapPath)
        {
            if (sourceFileOrBundle is null || IsEmitOnlyJs(emitOnly)) return;
            if (declarationFilePath is null)
            {
                if (Js.IsTruthy(emitOnly) || Js.IsTruthy(compilerOptions.EmitDeclarationOnly)) emitSkipped = true;
                return;
            }

            var sourceFiles = NodeTests.IsSourceFile(sourceFileOrBundle)
                ? new List<SourceFile> { (SourceFile)sourceFileOrBundle }
                : ((Bundle)sourceFileOrBundle).SourceFiles!;
            var filesForEmit = Js.IsTruthy(forceDtsEmit)
                ? sourceFiles
                : (List<SourceFile>)Core.Filter(sourceFiles, Utilities.IsSourceFileNotJson)!;
            // Setup and perform the transformation to retrieve declarations from the input files
            var inputListOrBundle = compilerOptions.OutFile is not null
                ? new List<Node> { NodeFactory.Factory.CreateBundle(filesForEmit) }
                : [.. filesForEmit];
            // Checker wont collect the linked aliases since thats only done when declaration is enabled and checking is performed.
            // Do that here when emitting only dts files
            foreach (var sourceFile in filesForEmit)
            {
                if (
                    (Js.IsTruthy(emitOnly) && !Utilities.GetEmitDeclarations(compilerOptions))
                    || Js.IsTruthy(compilerOptions.NoCheck)
                    || EmitResolverSkipsTypeChecking(emitOnly, forceDtsEmit)
                    || !Utilities.CanIncludeBindAndCheckDiagnostics(sourceFile, compilerOptions)
                )
                {
                    CollectLinkedAliases(sourceFile);
                }
            }

            var declarationTransform = Transformer.TransformNodes(
                resolver,
                host,
                NodeFactory.Factory,
                compilerOptions,
                inputListOrBundle,
                declarationTransformers,
                /*allowDtsFiles*/ false);
            if (declarationTransform.Diagnostics is { Count: > 0 })
            {
                foreach (var diagnostic in declarationTransform.Diagnostics)
                {
                    emitterDiagnostics.Add((Diagnostic)diagnostic);
                }
            }

            var declBlocked = (declarationTransform.Diagnostics is { Count: > 0 })
                || host.IsEmitBlocked(declarationFilePath)
                || Js.IsTruthy(compilerOptions.NoEmit);
            emitSkipped = emitSkipped || declBlocked;
            if (!declBlocked || Js.IsTruthy(forceDtsEmit))
            {
                Debug.Assert(declarationTransform.Transformed!.Count == 1, "Should only see one output from the decl transform");
                var printerOptions = new PrinterOptions
                {
                    RemoveComments = compilerOptions.RemoveComments,
                    NewLine = compilerOptions.NewLine,
                    NoEmitHelpers = true,
                    Module = compilerOptions.Module,
                    ModuleResolution = compilerOptions.ModuleResolution,
                    Target = compilerOptions.Target,
                    SourceMap = !IsEmitOnlyBuilderSignature(emitOnly) && Js.IsTruthy(compilerOptions.DeclarationMap),
                    InlineSourceMap = compilerOptions.InlineSourceMap,
                    ExtendedDiagnostics = compilerOptions.ExtendedDiagnostics,
                    OnlyPrintJsDocStyle = true,
                    OmitBraceSourceMapPositions = true,
                };

                var declarationPrinter = new Printer(printerOptions, new EmitPrintHandlers(resolver, declarationTransform));
                var dtsWritten = PrintSourceFileOrBundle(
                    declarationFilePath,
                    declarationMapPath,
                    declarationTransform,
                    declarationPrinter,
                    new SourceMapOptions
                    {
                        SourceMap = printerOptions.SourceMap,
                        SourceRoot = compilerOptions.SourceRoot,
                        MapRoot = compilerOptions.MapRoot,
                        ExtendedDiagnostics = compilerOptions.ExtendedDiagnostics,
                        // Explicitly do not passthru either `inline` option
                    });
                if (emittedFilesList is not null)
                {
                    if (dtsWritten) emittedFilesList.Add(declarationFilePath);
                    if (declarationMapPath is not null)
                    {
                        emittedFilesList.Add(declarationMapPath);
                    }
                }
            }

            declarationTransform.Dispose();
        }

        void CollectLinkedAliases(Node node)
        {
            if (NodeTests.IsExportAssignment(node))
            {
                if (((ExportAssignment)node).Expression!.Kind == SyntaxKind.Identifier)
                {
                    // PORT-NOTE: TS's node.expression is an Identifier (a `MemberExpression` member of the union); the
                    // port's model has the concrete class for the union, so the narrowing is the cast.
                    resolver.CollectLinkedAliases((MemberExpression)((ExportAssignment)node).Expression!, /*setVisibility*/ true);
                }

                return;
            }

            if (NodeTests.IsExportSpecifier(node))
            {
                var exportSpecifier = (ExportSpecifier)node;
                resolver.CollectLinkedAliases((MemberExpression)(exportSpecifier.PropertyName ?? exportSpecifier.Name)!, /*setVisibility*/ true);
                return;
            }

            // PORT-NOTE: TS's `forEachChild(node, collectLinkedAliases)` discards the walk's result, and the port's
            // `ForEachChild<T>` needs the element type of the callback's answer, so the adapter answers null.
            ForEachChildTable.ForEachChild<object>(node, child =>
            {
                CollectLinkedAliases(child);
                return null;
            });
        }

        void MarkLinkedReferences(SourceFile file)
        {
            if (Utilities.IsSourceFileJS(file)) return; // JS files don't use reference calculations as they don't do import ellision, no need to calculate it
            // PORT-NOTE: TS's callback takes the node and its parent and returns the `"skip"` sentinel; the port's
            // `ForEachChildRecursively` states the same two parameters and its own sentinel,
            // <see cref="ForEachChildTable.Skip"/> (a string cannot be told from a legitimate result).
            ForEachChildTable.ForEachChildRecursively(file, (n, _) =>
            {
                if (NodeTests.IsImportEqualsDeclaration(n) && (Utilities.GetSyntacticModifierFlags(n) & ModifierFlags.Export) == 0) return ForEachChildTable.Skip; // These are deferred and marked in a chain when referenced
                if (NodeTests.IsImportDeclaration(n)) return ForEachChildTable.Skip; // likewise, these are ultimately what get marked by calls on other nodes - we want to skip them
                resolver.MarkLinkedReferences(n);
                return null;
            });
        }

        bool PrintSourceFileOrBundle(
            string jsFilePath,
            string? sourceMapFilePath,
            ITransformationResult<Node> transform,
            Printer printer,
            SourceMapOptions mapOptions)
        {
            var sourceFileOrBundle = transform.Transformed![0];
            var bundle = sourceFileOrBundle.Kind == SyntaxKind.Bundle ? (Bundle)sourceFileOrBundle : null;
            var sourceFile = sourceFileOrBundle.Kind == SyntaxKind.SourceFile ? (SourceFile)sourceFileOrBundle : null;
            var sourceFiles = bundle is not null ? bundle.SourceFiles! : new List<SourceFile> { sourceFile! };

            ISourceMapGenerator? sourceMapGenerator = null;
            if (ShouldEmitSourceMaps(mapOptions, sourceFileOrBundle))
            {
                sourceMapGenerator = CreateSourceMapGenerator(
                    host,
                    Paths.GetBaseFileName(Paths.NormalizeSlashes(jsFilePath)),
                    GetSourceRoot(mapOptions),
                    GetSourceMapDirectory(mapOptions, jsFilePath, sourceFile),
                    mapOptions);
            }

            if (bundle is not null)
            {
                printer.WriteBundle(bundle, writer, sourceMapGenerator);
            }
            else
            {
                printer.WriteFile(sourceFile!, writer, sourceMapGenerator);
            }

            int? sourceMapUrlPos = null;
            if (sourceMapGenerator is not null)
            {
                if (sourceMapDataList is not null)
                {
                    sourceMapDataList.Add(new SourceMapEmitResult
                    {
                        InputSourceFileNames = sourceMapGenerator.GetSources(),
                        SourceMap = sourceMapGenerator.ToJSON(),
                    });
                }

                var sourceMappingURL = GetSourceMappingURL(mapOptions, sourceMapGenerator, jsFilePath, sourceMapFilePath, sourceFile);

                if (Js.IsTruthy(sourceMappingURL))
                {
                    if (!writer.IsAtStartOfLine()) writer.RawWrite(newLine);
                    sourceMapUrlPos = writer.GetTextPos();
                    writer.WriteComment($"//# {"sourceMappingURL"}={sourceMappingURL}"); // Tools can sometimes see this line as a source mapping url comment
                }

                // Write the source map
                if (sourceMapFilePath is not null)
                {
                    var sourceMap = sourceMapGenerator.ToString();
                    Utilities.WriteFile(host, emitterDiagnostics, sourceMapFilePath, sourceMap, /*writeByteOrderMark*/ false, sourceFiles);
                }
            }
            else
            {
                writer.WriteLine(null);
            }

            // Write the output file
            var text = writer.GetText();
            var data = new WriteFileCallbackData { SourceMapUrlPos = sourceMapUrlPos, Diagnostics = transform.Diagnostics };
            Utilities.WriteFile(host, emitterDiagnostics, jsFilePath, text, Js.IsTruthy(compilerOptions.EmitBOM), sourceFiles, data);

            // Reset state
            writer.Clear();
            return !Js.IsTruthy(data.SkippedDtsWrite);
        }

        bool ShouldEmitSourceMaps(SourceMapOptions mapOptions, Node sourceFileOrBundle)
            => (Js.IsTruthy(mapOptions.SourceMap) || Js.IsTruthy(mapOptions.InlineSourceMap))
                && (sourceFileOrBundle.Kind != SyntaxKind.SourceFile || !Paths.FileExtensionIs(((SourceFile)sourceFileOrBundle).FileName ?? "", Extension.Json.Value));

        string GetSourceRoot(SourceMapOptions mapOptions)
        {
            // Normalize source root and make sure it has trailing "/" so that it can be used to combine paths with the
            // relative paths of the sources list in the sourcemap
            var sourceRoot = Paths.NormalizeSlashes(mapOptions.SourceRoot ?? "");
            return Js.IsTruthy(sourceRoot) ? Paths.EnsureTrailingDirectorySeparator(sourceRoot) : sourceRoot;
        }

        string GetSourceMapDirectory(SourceMapOptions mapOptions, string filePath, SourceFile? sourceFile)
        {
            if (mapOptions.SourceRoot is not null) return host.GetCommonSourceDirectory();
            if (mapOptions.MapRoot is not null)
            {
                var sourceMapDir = Paths.NormalizeSlashes(mapOptions.MapRoot);
                if (sourceFile is not null)
                {
                    // For modules or multiple emit files the mapRoot will have directory structure like the sources
                    // So if src\a.ts and src\lib\b.ts are compiled together user would be moving the maps into mapRoot\a.js.map and mapRoot\lib\b.js.map
                    sourceMapDir = Paths.GetDirectoryPath(Utilities.GetSourceFilePathInNewDir(sourceFile.FileName ?? "", host, sourceMapDir));
                }

                if (Paths.GetRootLength(sourceMapDir) == 0)
                {
                    // The relative paths are relative to the common directory
                    sourceMapDir = Paths.CombinePaths(host.GetCommonSourceDirectory(), sourceMapDir);
                }

                return sourceMapDir;
            }

            return Paths.GetDirectoryPath(Paths.NormalizePath(filePath));
        }

        string GetSourceMappingURL(SourceMapOptions mapOptions, ISourceMapGenerator sourceMapGenerator, string filePath, string? sourceMapFilePath, SourceFile? sourceFile)
        {
            if (Js.IsTruthy(mapOptions.InlineSourceMap))
            {
                // Encode the sourceMap into the sourceMap url
                var sourceMapText = sourceMapGenerator.ToString();
                var base64SourceMapText = Base64Encode(sourceMapText);
                return $"data:application/json;base64,{base64SourceMapText}";
            }

            var sourceMapFile = Paths.GetBaseFileName(Paths.NormalizeSlashes(Debug.CheckDefined(sourceMapFilePath)));
            if (mapOptions.MapRoot is not null)
            {
                var sourceMapDir = Paths.NormalizeSlashes(mapOptions.MapRoot);
                if (sourceFile is not null)
                {
                    // For modules or multiple emit files the mapRoot will have directory structure like the sources
                    // So if src\a.ts and src\lib\b.ts are compiled together user would be moving the maps into mapRoot\a.js.map and mapRoot\lib\b.js.map
                    sourceMapDir = Paths.GetDirectoryPath(Utilities.GetSourceFilePathInNewDir(sourceFile.FileName ?? "", host, sourceMapDir));
                }

                if (Paths.GetRootLength(sourceMapDir) == 0)
                {
                    // The relative paths are relative to the common directory
                    sourceMapDir = Paths.CombinePaths(host.GetCommonSourceDirectory(), sourceMapDir);
                    return EncodeUri(
                        Paths.GetRelativePathToDirectoryOrUrl(
                            Paths.GetDirectoryPath(Paths.NormalizePath(filePath)), // get the relative sourceMapDir path based on jsFilePath
                            Paths.CombinePaths(sourceMapDir, sourceMapFile), // this is where user expects to see sourceMap
                            Utilities.EmitHostCurrentDirectory(host),
                            (Core.GetCanonicalFileName)host.GetCanonicalFileName!,
                            /*isAbsolutePathAnUrl*/ true));
                }

                return EncodeUri(Paths.CombinePaths(sourceMapDir, sourceMapFile));
            }

            return EncodeUri(sourceMapFile);
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:1060-1067 — the <c>SourceMapOptions</c> shape.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares this as a local interface the two <c>printSourceFileOrBundle</c> call sites satisfy
    /// structurally (the JS site passes <c>compilerOptions</c> itself); C# has no structural typing, so this class is
    /// the shape and <see cref="Of"/> is what builds it from the compiler options.
    /// </remarks>
    private sealed class SourceMapOptions
    {
        public bool? SourceMap { get; set; }

        public bool? InlineSourceMap { get; set; }

        public bool? InlineSources { get; set; }

        public string? SourceRoot { get; set; }

        public string? MapRoot { get; set; }

        public bool? ExtendedDiagnostics { get; set; }

        /// <summary>The JS emit's <c>mapOptions</c>: the reference's <c>compilerOptions</c> read structurally.</summary>
        public static SourceMapOptions Of(ICompilerOptions compilerOptions) => new()
        {
            SourceMap = compilerOptions.SourceMap,
            InlineSourceMap = compilerOptions.InlineSourceMap,
            InlineSources = compilerOptions.InlineSources,
            SourceRoot = compilerOptions.SourceRoot,
            MapRoot = compilerOptions.MapRoot,
            ExtendedDiagnostics = compilerOptions.ExtendedDiagnostics,
        };
    }

    /// <summary>ported from: src/compiler/emitter.ts:1186-1195 — the printer handlers both emit paths build.</summary>
    /// <remarks>
    /// PORT-NOTE: TS passes the two object literals <c>{ hasGlobalName: resolver.hasGlobalName, onEmitNode:
    /// transform.emitNodeWithNotification, isEmitNotificationEnabled: transform.isEmitNotificationEnabled,
    /// substituteNode: transform.substituteNode }</c> (emitter.ts:860-868, 942-950) as <c>createPrinter</c>'s
    /// <c>handlers</c>. The generated <see cref="IPrintHandlers"/> requires all fourteen members, so this class states
    /// the four the reference supplies and leaves the ten it leaves absent (<c>PrintHandlers.Default</c> is only the
    /// no-handler set, recognised by the printer by identity).
    /// </remarks>
    private sealed class EmitPrintHandlers(IEmitResolver resolver, ITransformationResult<Node> transform) : IPrintHandlers
    {
        // resolver hooks
        public bool HasGlobalName(string? name) => resolver.HasGlobalName(name);

        // transform hooks
        public void OnEmitNode(EmitHint hint, Node? node, Action<EmitHint, Node>? emitCallback)
            => transform.EmitNodeWithNotification(hint, node, emitCallback);

        public bool IsEmitNotificationEnabled(Node? node) => transform.IsEmitNotificationEnabled(node);

        public Node SubstituteNode(EmitHint hint, Node? node) => transform.SubstituteNode(hint, node);

        public Action<EmitHint, Node, Action<EmitHint, Node>>? OnEmitSourceMapOfNode { get; set; }

        public Func<Node, SyntaxKind, Action<string>, int, Func<SyntaxKind, Action<string>, int, int>, int>? OnEmitSourceMapOfToken { get; set; }

        public Action<int>? OnEmitSourceMapOfPosition { get; set; }

        public Action<SourceFile>? OnSetSourceFile { get; set; }

        public Action<Node>? OnBeforeEmitNode { get; set; }

        public Action<Node>? OnAfterEmitNode { get; set; }

        public Action<NodeArray<object?>>? OnBeforeEmitNodeArray { get; set; }

        public Action<NodeArray<object?>>? OnAfterEmitNodeArray { get; set; }

        public Action<Node>? OnBeforeEmitToken { get; set; }

        public Action<Node>? OnAfterEmitToken { get; set; }
    }

    /// <summary>ported from: src/compiler/emitter.ts:1137-1139 — <c>getBuildInfoText</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS is <c>JSON.stringify(buildInfo)</c>. The port has no <c>JSON.stringify</c> (the same gap
    /// <c>TracingJson</c> records), so the serialization is <c>System.Text.Json</c> with the camel-case naming policy —
    /// which is what makes the written text the reference's: the generated model names each member in PascalCase where
    /// TS's object literal uses the camelCase spelling (<c>BuildInfo.version</c>, types.ts:4930-4934), so the policy is
    /// the general form of the reference's member names, not a special case for <c>Version</c>. Nothing else is set, so
    /// a ported <c>BuildInfo</c> member serializes the way TS's does.
    /// </remarks>
    internal static string GetBuildInfoText(IBuildInfo buildInfo) => JsonSerializer.Serialize(buildInfo, s_buildInfoJsonOptions);

    /// <summary>The serializer options <see cref="GetBuildInfoText"/> uses; see its note.</summary>
    private static readonly JsonSerializerOptions s_buildInfoJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>ported from: src/compiler/emitter.ts:1142-1144 — <c>getBuildInfo</c>.</summary>
    /// <remarks>PORT-NOTE: <c>readJsonOrUndefined</c> is <see cref="Utilities.ReadJsonOrUndefined(string, string?)"/>
    /// and the reference's cast is the port's <c>as</c>.</remarks>
    internal static BuildInfo? GetBuildInfo(string buildInfoFile, string buildInfoText)
        => Utilities.ReadJsonOrUndefined(buildInfoFile, buildInfoText) as BuildInfo;

    /// <summary>ported from: src/compiler/emitter.ts:752 — <c>notImplementedResolver</c>.</summary>
    /// <remarks>PORT-NOTE: <c>emitBuildInfo</c> (program.ts:2650) is its one caller and it transforms nothing, so every
    /// slot keeps the reference's <c>notImplemented</c> (see <c>Emitter.Model.cs</c> for the class).</remarks>
    internal static readonly IEmitResolver NotImplementedResolver = new NotImplementedEmitResolver();

    /// <summary>ported from: src/compiler/emitter.ts:752 — the silent failure <c>createSourceMapGenerator</c> is.</summary>
    /// <remarks>
    /// PORT-NOTE (the named blocker): <c>createSourceMapGenerator</c> is <c>sourcemap.ts</c>'s
    /// (<c>src/compiler/sourcemap.ts:36-58</c>), the M7-B source-map slice, so this slice keeps the reference's call
    /// site (<c>emitter.ts:997-1005</c>) and reports the missing generator by name rather than printing without one.
    /// <c>Printer</c> already reports the printer-side half of the same gap (<c>Printer.SourceMaps.cs</c>'s
    /// <c>SourceMapGeneratorNotSupported</c>); this member is the entry the emitter itself needs.
    /// </remarks>
    private static ISourceMapGenerator CreateSourceMapGenerator(
        IEmitHost host,
        string file,
        string sourceRoot,
        string sourcesDirectoryPath,
        SourceMapOptions generatorOptions)
        => throw new NotSupportedException(
            "createSourceMapGenerator (src/compiler/sourcemap.ts:36) is not ported yet: SourceMapGenerator is the M7-B "
            + "source-map slice (docs/architecture.md §17.4 item 8).");

    /// <summary>ported from: src/compiler/core.ts — <c>base64encode(sys, input)</c>'s pure arm.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>base64encode</c> (utilitiesPublic.ts:1744-1757) asks the <c>sys</c> singleton first and falls
    /// back to the engine's base64 encoder. §13.5 drops that singleton and the emit host is not an
    /// <see cref="ISystem"/>, so the fallback — the standard alphabet with padding over the UTF-8 bytes — is the arm
    /// the port states.
    /// </remarks>
    private static string Base64Encode(string input) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(input));

    /// <summary>ported from: ECMAScript's <c>encodeURI</c> (es5.d.ts:60), which the emitter's source-map URL uses.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>encodeURI</c> is a JS builtin with no BCL counterpart; <c>Uri.EscapeDataString</c> escapes the
    /// reserved set <c>;,/?:@&amp;=+$#</c> that <c>encodeURI</c> leaves alone, so each of those escape sequences is
    /// restored. The rest of the unreserved set (<c>A-Za-z0-9-_.!~*'()</c>) is the same in both. A lone surrogate is an
    /// error in both (TS's <c>encodeURI</c> throws <c>URIError</c>; <see cref="Uri.EscapeDataString"/> throws
    /// <see cref="UriFormatException"/>).
    /// </remarks>
    private static string EncodeUri(string uri)
    {
        var escaped = Uri.EscapeDataString(uri);
        return escaped
            .Replace("%3B", ";", StringComparison.Ordinal)
            .Replace("%2C", ",", StringComparison.Ordinal)
            .Replace("%2F", "/", StringComparison.Ordinal)
            .Replace("%3F", "?", StringComparison.Ordinal)
            .Replace("%3A", ":", StringComparison.Ordinal)
            .Replace("%40", "@", StringComparison.Ordinal)
            .Replace("%26", "&", StringComparison.Ordinal)
            .Replace("%3D", "=", StringComparison.Ordinal)
            .Replace("%2B", "+", StringComparison.Ordinal)
            .Replace("%24", "$", StringComparison.Ordinal)
            .Replace("%23", "#", StringComparison.Ordinal);
    }

    /// <summary>ported from: src/compiler/types.ts:4700 — the <c>EmitOnly.Js</c> comparison the two emit paths make.</summary>
    /// <remarks>PORT-NOTE: TS's <c>emitOnly === EmitOnly.Js</c> is a strict comparison against the enum member (the
    /// number <c>0</c>, which <c>false</c> is not equal to under <c>===</c>), so the port compares the boxed member.</remarks>
    private static bool IsEmitOnlyJs(object? emitOnly) => emitOnly is EmitOnly value && value == EmitOnly.Js;

    /// <summary>ported from: src/compiler/types.ts:4702 — the <c>EmitOnly.BuilderSignature</c> comparison the
    /// declaration printer's <c>sourceMap</c> option makes.</summary>
    private static bool IsEmitOnlyBuilderSignature(object? emitOnly) => emitOnly is EmitOnly value && value == EmitOnly.BuilderSignature;
}
