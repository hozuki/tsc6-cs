namespace TypeScript.Compiler;

// ported from: src/compiler/utilities.ts:3814-3816 (isSourceFileNotJson), 6540-6569
// (getOwnEmitOutputFilePath, getDeclarationEmitOutputFilePath, getDeclarationEmitOutputFilePathWorker,
// getDeclarationEmitExtensionForPath), 6634-6693 (getSourceFilesToEmit, sourceFileMayBeEmitted's forceDtsEmit leg,
// getSourceFilePathInNewDir) and 6707-6721 (writeFile).
//
// PORT-NOTE: these are `utilities.ts` members — `Utilities` is the port's class for that file — and they are the
// emit-path half M7's emitter was blocked on: `getOutputPathsFor` needs the two output-file-path members,
// `forEachEmittedFile` needs `getSourceFilesToEmit`, `getSourceMapDirectory` needs `getSourceFilePathInNewDir` and
// `emitFiles` writes through `writeFile`. They were split out of `Utilities.Emit.cs` (which carries the writer-side
// utilities) so that the emit-path family sits together; the class is the same partial.
internal static partial class Utilities
{
    /// <summary>The <c>getCompilerOptions</c> of an emit host.</summary>
    /// <remarks>
    /// PORT-NOTE: <see cref="IEmitHost"/> inherits <c>getCompilerOptions</c> from two bases
    /// (<see cref="IScriptReferenceHost"/> and <see cref="ISourceFileMayBeEmittedHost"/>), and
    /// <c>getCurrentDirectory</c>/<c>useCaseSensitiveFileNames</c> likewise, so an unqualified call on the interface
    /// is ambiguous (CS0121). One object satisfies every base, so the three helpers below name the base each member is
    /// read through and the emit path calls them — the same "the call site names the declaring interface" treatment
    /// <c>Program.GetCompilerOptionsOf</c> records for the same overlap on <c>Program</c>.
    /// </remarks>
    internal static ICompilerOptions EmitHostCompilerOptions(IEmitHost host) => ((IScriptReferenceHost)host).GetCompilerOptions();

    /// <inheritdoc cref="EmitHostCompilerOptions"/>
    internal static string EmitHostCurrentDirectory(IEmitHost host) => ((IScriptReferenceHost)host).GetCurrentDirectory();

    /// <inheritdoc cref="EmitHostCompilerOptions"/>
    internal static bool EmitHostUseCaseSensitiveFileNames(IEmitHost host) => ((ISourceFileMayBeEmittedHost)host).UseCaseSensitiveFileNames();

    /// <summary>ported from: src/compiler/utilities.ts:3814-3816 — <c>isSourceFileNotJson</c>.</summary>
    public static bool IsSourceFileNotJson(SourceFile file) => !IsJsonSourceFile(file);

    /// <summary>ported from: src/compiler/utilities.ts:6540-6552 — <c>getOwnEmitOutputFilePath</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>compilerOptions.outDir</c> is a <c>string?</c> (TS's optional slot), so the test is
    /// <c>is not null</c> — the spelling the emit-boundary slice fixed for <c>rootDir</c>/<c>configFilePath</c>.
    /// </remarks>
    public static string GetOwnEmitOutputFilePath(string fileName, IEmitHost host, string extension)
    {
        var compilerOptions = Utilities.EmitHostCompilerOptions(host);
        string emitOutputFilePathWithoutExtension;
        if (compilerOptions.OutDir is not null)
        {
            emitOutputFilePathWithoutExtension = RemoveFileExtension(GetSourceFilePathInNewDir(fileName, host, compilerOptions.OutDir));
        }
        else
        {
            emitOutputFilePathWithoutExtension = RemoveFileExtension(fileName);
        }

        return emitOutputFilePathWithoutExtension + extension;
    }

    /// <summary>ported from: src/compiler/utilities.ts:6554-6556 — <c>getDeclarationEmitOutputFilePath</c>.</summary>
    public static string GetDeclarationEmitOutputFilePath(string fileName, IEmitHost host)
        => GetDeclarationEmitOutputFilePathWorker(fileName, Utilities.EmitHostCompilerOptions(host), host);

    /// <summary>ported from: src/compiler/utilities.ts:6558-6567 — <c>getDeclarationEmitOutputFilePathWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's third parameter is a <c>Pick</c> of the emit host's three file-path members; the port has no
    /// structural <c>Pick</c>, so it is the whole <see cref="IEmitHost"/> — the same treatment the generated host
    /// interfaces get everywhere else. <c>getCanonicalFileName</c> is the generated <c>object?</c> slot (the delegate
    /// type is declared outside the extracted sources), so it is recovered with a cast.
    /// </remarks>
    public static string GetDeclarationEmitOutputFilePathWorker(string fileName, ICompilerOptions options, IEmitHost host)
    {
        var outputDir = options.DeclarationDir ?? options.OutDir; // Prefer declaration folder if specified

        var path = outputDir is not null
            ? GetSourceFilePathInNewDirWorker(
                fileName,
                outputDir,
                Utilities.EmitHostCurrentDirectory(host),
                host.GetCommonSourceDirectory(),
                (Core.GetCanonicalFileName)host.GetCanonicalFileName!)
            : fileName;
        var declarationExtension = GetDeclarationEmitExtensionForPath(path);
        return RemoveFileExtension(path) + declarationExtension;
    }

    /// <summary>ported from: src/compiler/utilities.ts:6569-6574 — <c>getDeclarationEmitExtensionForPath</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's return type is the union <c>Extension.Dts | Extension.Dmts | Extension.Dcts |
    /// ".d.json.ts"</c>; every member of that union is a string, and the port's <see cref="Extension"/> is a struct
    /// over its string value, so the member's face is <c>string</c> and each arm answers the value (the
    /// <see cref="Extension.Value"/> reads). The json arm is TS's literal, which is not en <c>Extension</c> member.
    /// </remarks>
    public static string GetDeclarationEmitExtensionForPath(string path)
        => Paths.FileExtensionIsOneOf(path, [Extension.Mjs.Value, Extension.Mts.Value]) ? Extension.Dmts.Value
            : Paths.FileExtensionIsOneOf(path, [Extension.Cjs.Value, Extension.Cts.Value]) ? Extension.Dcts.Value
            : Paths.FileExtensionIsOneOf(path, [Extension.Json.Value]) ? ".d.json.ts" // Drive-by redefinition of json declaration file output name so if it's ever enabled, it behaves well
            : Extension.Dts.Value;

    /// <summary>ported from: src/compiler/utilities.ts:6634-6659 — <c>getSourceFilesToEmit</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>filter</c> is <see cref="Core.Filter{T}"/>, which answers the same list type it was given; the
    /// <c>host.getSourceFiles()</c> answer is TS's live file list, and the target-source-file arm is TS's one-element
    /// array literal.
    /// </remarks>
    public static List<SourceFile> GetSourceFilesToEmit(IEmitHost host, SourceFile? targetSourceFile = null, bool? forceDtsEmit = null)
    {
        var options = Utilities.EmitHostCompilerOptions(host);
        if (options.OutFile is not null)
        {
            var moduleKind = GetEmitModuleKind(options);
            var moduleEmitEnabled = Js.IsTruthy(options.EmitDeclarationOnly) || moduleKind == ModuleKind.AMD || moduleKind == ModuleKind.System;
            // Can emit only sources that are not declaration file and are either non module code or module with --module or --target es6 specified
            return (List<SourceFile>)Core.Filter(
                host.GetSourceFiles(),
                sourceFile =>
                    (moduleEmitEnabled || !Parser.IsExternalModule(sourceFile))
                    && SourceFileMayBeEmitted(sourceFile, host, forceDtsEmit))!;
        }
        else
        {
            var sourceFiles = targetSourceFile is null ? host.GetSourceFiles() : new List<SourceFile> { targetSourceFile };
            return (List<SourceFile>)Core.Filter(
                sourceFiles,
                sourceFile => SourceFileMayBeEmitted(sourceFile, host, forceDtsEmit))!;
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:6691-6693 — <c>getSourceFilePathInNewDir</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the worker it hands the host's members to is <see cref="GetSourceFilePathInNewDirWorker"/>, which
    /// <c>Program/Utilities.Program.Nodes.cs</c> already declares for <c>sourceFileMayBeEmitted</c>.
    /// </remarks>
    public static string GetSourceFilePathInNewDir(string fileName, IEmitHost host, string newDirPath)
        => GetSourceFilePathInNewDirWorker(
            fileName,
            newDirPath,
            Utilities.EmitHostCurrentDirectory(host),
            host.GetCommonSourceDirectory(),
            (Core.GetCanonicalFileName)host.GetCanonicalFileName!);

    /// <summary>ported from: src/compiler/utilities.ts:6707-6721 — <c>writeFile</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's host parameter is the structural <c>{ writeFile: WriteFileCallback }</c> and it calls that
    /// member; the port's <see cref="IEmitHost.WriteFile"/> is the generated settable slot, so the call is through
    /// the property. <c>createCompilerDiagnostic</c> is <see cref="Utilities.CreateCompilerDiagnostic(DiagnosticMessage, object?[])"/>
    /// and the error callback's three arguments are TS's message arguments.
    /// <para>
    /// PORT-NOTE: <c>sourceFiles</c> is <c>readonly SourceFile[] | undefined</c> in TS and
    /// <c>List&lt;SourceFile&gt;?</c> in the generated callback; <c>data</c> is TS's <c>WriteFileCallbackData</c>, the
    /// port's <see cref="IWriteFileCallbackData"/>.
    /// </para>
    /// </remarks>
    public static void WriteFile(
        IEmitHost host,
        DiagnosticCollection diagnostics,
        string fileName,
        string text,
        bool writeByteOrderMark,
        List<SourceFile>? sourceFiles = null,
        IWriteFileCallbackData? data = null)
        => host.WriteFile!(
            fileName,
            text,
            writeByteOrderMark,
            hostErrorMessage => diagnostics.Add(CreateCompilerDiagnostic(Diagnostics.Could_not_write_file_0_Colon_1, fileName, hostErrorMessage)),
            sourceFiles!,
            data!,
            null);
}
