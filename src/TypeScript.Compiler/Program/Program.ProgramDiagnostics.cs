// ported from: src/compiler/programDiagnostics.ts:1-427 (the whole module: createProgramDiagnostics, its closure
//   state, the ProgramDiagnostics object literal it returns and the four helpers nested in it)
// ported from: src/compiler/types.ts:4605-4689 (the file-include reasons and the file-preprocessing diagnostics)
// ported from: src/compiler/watch.ts:361-414, 415-425, 426-444, 445-552, 553-557 (the reason/redirect explainers
//   programDiagnostics.ts imports from watch.ts)
// ported from: src/compiler/utilities.ts:614-616, 2364-2382, 3094-3124, 9688-9692, 12246-12309 (the helpers those
//   two need)
// ported from: src/compiler/program.ts:1162-1169 (isReferencedFile)
namespace TypeScript.Compiler;

// ---------------------------------------------------------------------------------------------
// The file-include reasons (types.ts:4605-4658)
// ---------------------------------------------------------------------------------------------

/// <summary>
/// The union slot of TS's <c>FileIncludeReason</c> — one of the five reasons a file is in the program.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS declares <c>FileIncludeReason</c> as a union of five interfaces and narrows it with
/// <c>switch (reason.kind)</c>; §5 rule 3 maps that to a C# type test, so the port needs one type the five arms
/// share. It is a marker interface (the arms are unrelated TS shapes), and the two members below are the ones the
/// port's call sites read off the union: <c>Program/Program.Graph.cs</c> reads <c>kind</c> (for tracing) and
/// <c>file</c> (under an <c>isReferencedFile</c> test) directly, and the port cannot narrow a marker interface with
/// a <c>bool</c> test the way TS's type predicate does. The arms without a file answer <c>null</c>.
/// </remarks>
internal interface IFileIncludeReason
{
    /// <summary>ported from: src/compiler/types.ts:4606 etc. — <c>kind</c>.</summary>
    FileIncludeKind Kind { get; }

    /// <summary>ported from: src/compiler/types.ts:4641 — <c>ReferencedFile.file</c>, answered by every arm.</summary>
    string? File { get; }
}

/// <summary>ported from: src/compiler/types.ts:4605-4612 — <c>RootFile</c>.</summary>
internal sealed class RootFile : IFileIncludeReason, IRootFile
{
    /// <summary>ported from: src/compiler/types.ts:4610 — <c>kind</c>.</summary>
    public FileIncludeKind Kind { get; set; }

    /// <summary>ported from: src/compiler/types.ts:4611 — <c>index</c>.</summary>
    public int Index { get; set; }

    string? IFileIncludeReason.File => null;

    object? IRootFile.Kind
    {
        get => Kind;
        set => Kind = (FileIncludeKind)value!;
    }
}

/// <summary>ported from: src/compiler/types.ts:4614-4618 — <c>LibFile</c>.</summary>
internal sealed class LibFile : IFileIncludeReason, ILibFile
{
    /// <summary>ported from: src/compiler/types.ts:4616 — <c>kind</c>.</summary>
    public FileIncludeKind Kind { get; set; }

    /// <summary>ported from: src/compiler/types.ts:4617 — <c>index</c>.</summary>
    public int? Index { get; set; }

    string? IFileIncludeReason.File => null;

    object? ILibFile.Kind
    {
        get => Kind;
        set => Kind = (FileIncludeKind)value!;
    }
}

/// <summary>ported from: src/compiler/types.ts:4620-4629 — <c>ProjectReferenceFile</c>.</summary>
internal sealed class ProjectReferenceFile : IFileIncludeReason, IProjectReferenceFile
{
    /// <summary>ported from: src/compiler/types.ts:4627 — <c>kind</c>.</summary>
    public FileIncludeKind Kind { get; set; }

    /// <summary>ported from: src/compiler/types.ts:4628 — <c>index</c>.</summary>
    public int Index { get; set; }

    string? IFileIncludeReason.File => null;

    object? IProjectReferenceFile.Kind
    {
        get => Kind;
        set => Kind = (FileIncludeKind)value!;
    }
}

/// <summary>ported from: src/compiler/types.ts:4631-4643 — <c>ReferencedFile</c>.</summary>
internal sealed class ReferencedFile : IFileIncludeReason, IReferencedFile
{
    /// <summary>ported from: src/compiler/types.ts:4640 — <c>kind</c>.</summary>
    public FileIncludeKind Kind { get; set; }

    /// <summary>ported from: src/compiler/types.ts:4641 — <c>file</c>.</summary>
    public string? File { get; set; }

    /// <summary>ported from: src/compiler/types.ts:4642 — <c>index</c>.</summary>
    public int Index { get; set; }

    object? IReferencedFile.Kind
    {
        get => Kind;
        set => Kind = (FileIncludeKind)value!;
    }
}

/// <summary>ported from: src/compiler/types.ts:4645-4650 — <c>AutomaticTypeDirectiveFile</c>.</summary>
internal sealed class AutomaticTypeDirectiveFile : IFileIncludeReason, IAutomaticTypeDirectiveFile
{
    /// <summary>ported from: src/compiler/types.ts:4647 — <c>kind</c>.</summary>
    public FileIncludeKind Kind { get; set; }

    /// <summary>ported from: src/compiler/types.ts:4648 — <c>typeReference</c>.</summary>
    public string? TypeReference { get; set; }

    /// <summary>ported from: src/compiler/types.ts:4649 — <c>packageId</c>.</summary>
    public IPackageId? PackageId { get; set; }

    string? IFileIncludeReason.File => null;

    object? IAutomaticTypeDirectiveFile.Kind
    {
        get => Kind;
        set => Kind = (FileIncludeKind)value!;
    }
}

// ---------------------------------------------------------------------------------------------
// The file-preprocessing diagnostics (types.ts:4660-4689)
// ---------------------------------------------------------------------------------------------

/// <summary>
/// The union slot of TS's <c>FilePreprocessingDiagnostics</c> — what
/// <c>addFileProcessingDiagnostic</c> accumulates until <c>getCombinedDiagnostics</c> folds it in.
/// </summary>
/// <remarks>PORT-NOTE: as with <see cref="IFileIncludeReason"/>, the TS union becomes a marker interface and
/// <c>switch (diagnostic.kind)</c> becomes a type test.</remarks>
internal interface IFilePreprocessingDiagnostics
{
    /// <summary>ported from: src/compiler/types.ts:4669 etc. — <c>kind</c>.</summary>
    FilePreprocessingDiagnosticsKind Kind { get; }
}

/// <summary>ported from: src/compiler/types.ts:4667-4671 — <c>FilePreprocessingLibReferenceDiagnostic</c>.</summary>
internal sealed class FilePreprocessingLibReferenceDiagnostic : IFilePreprocessingDiagnostics, IFilePreprocessingLibReferenceDiagnostic
{
    /// <summary>ported from: src/compiler/types.ts:4669 — <c>kind</c>.</summary>
    public FilePreprocessingDiagnosticsKind Kind { get; set; }

    /// <remarks>PORT-NOTE: TS narrows this slot to <c>ReferencedFile &amp; { kind: LibReferenceDirective }</c>; the
    /// port stores the union arm, which is the class <see cref="ReferencedFile"/> — that is where every construction
    /// site (<c>Program/Program.Graph.cs:822</c>) gets its value from.</remarks>
    /// <summary>ported from: src/compiler/types.ts:4670 — <c>reason</c>.</summary>
    public IFileIncludeReason? Reason { get; set; }

    object? IFilePreprocessingLibReferenceDiagnostic.Kind
    {
        get => Kind;
        set => Kind = (FilePreprocessingDiagnosticsKind)value!;
    }

    IReferencedFile? IFilePreprocessingLibReferenceDiagnostic.Reason
    {
        get => Reason as IReferencedFile;
        set => Reason = (IFileIncludeReason?)value;
    }
}

/// <summary>ported from: src/compiler/types.ts:4673-4680 — <c>FilePreprocessingFileExplainingDiagnostic</c>.</summary>
/// <remarks>
/// PORT-NOTE: this class deliberately does <em>not</em> declare the generated
/// <see cref="IFilePreprocessingFileExplainingDiagnostic"/> face. That interface's <c>FileProcessingReason</c> slot
/// degraded to <c>Node?</c> (the generator has no type for the reason union), and a class cannot implement the slot
/// without either lying about the reason or failing at run time; no ported code reads this shape through the
/// interface. The slots below are the ones the reference uses: <c>file</c>, <c>fileProcessingReason</c>,
/// <c>diagnostic</c> and <c>args</c>.
/// </remarks>
internal sealed class FilePreprocessingFileExplainingDiagnostic : IFilePreprocessingDiagnostics
{
    /// <summary>ported from: src/compiler/types.ts:4675 — <c>kind</c>.</summary>
    public FilePreprocessingDiagnosticsKind Kind { get; set; }

    /// <summary>ported from: src/compiler/types.ts:4676 — <c>file</c>.</summary>
    public string? File { get; set; }

    /// <summary>ported from: src/compiler/types.ts:4677 — <c>fileProcessingReason</c>.</summary>
    public IFileIncludeReason? FileProcessingReason { get; set; }

    /// <summary>ported from: src/compiler/types.ts:4678 — <c>diagnostic</c>.</summary>
    public IDiagnosticMessage? Diagnostic { get; set; }

    /// <summary>ported from: src/compiler/types.ts:4679 — <c>args</c>.</summary>
    public List<object?>? Args { get; set; }
}

/// <summary>ported from: src/compiler/types.ts:4682-4686 — <c>ResolutionDiagnostics</c>.</summary>
internal sealed class ResolutionDiagnostics : IFilePreprocessingDiagnostics, IResolutionDiagnostics
{
    /// <summary>ported from: src/compiler/types.ts:4684 — <c>kind</c>.</summary>
    public FilePreprocessingDiagnosticsKind Kind { get; set; }

    /// <summary>ported from: src/compiler/types.ts:4685 — <c>diagnostics</c>.</summary>
    public List<IDiagnostic>? Diagnostics { get; set; }

    object? IResolutionDiagnostics.Kind
    {
        get => Kind;
        set => Kind = (FilePreprocessingDiagnosticsKind)value!;
    }
}

// ---------------------------------------------------------------------------------------------
// programDiagnostics.ts:58-69 (the two caches)
// ---------------------------------------------------------------------------------------------

/// <summary>ported from: src/compiler/programDiagnostics.ts:57-62 — <c>FileReasonToChainCache</c>.</summary>
internal sealed class FileReasonToChainCache
{
    /// <summary>ported from: src/compiler/programDiagnostics.ts:59 — <c>fileIncludeReasonDetails</c>.</summary>
    public IDiagnosticMessageChain? FileIncludeReasonDetails { get; set; }

    /// <summary>ported from: src/compiler/programDiagnostics.ts:60 — <c>redirectInfo</c>.</summary>
    public List<IDiagnosticMessageChain>? RedirectInfo { get; set; }

    /// <summary>ported from: src/compiler/programDiagnostics.ts:61 — <c>details</c>.</summary>
    public List<IDiagnosticMessageChain>? Details { get; set; }
}

/// <summary>ported from: src/compiler/programDiagnostics.ts:64-69 — <c>LazyConfigDiagnostic</c>.</summary>
internal sealed class LazyConfigDiagnostic
{
    /// <summary>ported from: src/compiler/programDiagnostics.ts:66 — <c>file</c>.</summary>
    public required SourceFile File { get; init; }

    /// <summary>ported from: src/compiler/programDiagnostics.ts:67 — <c>diagnostic</c>.</summary>
    public required DiagnosticMessage Diagnostic { get; init; }

    /// <summary>ported from: src/compiler/programDiagnostics.ts:68 — <c>args</c>.</summary>
    public required object?[] Args { get; init; }
}

/// <summary>
/// The diagnostics a <c>Program</c> accumulates outside its files: the option-syntax diagnostics, the file-include
/// reasons, the file-processing diagnostics and the common source directory. TS's <c>ProgramDiagnostics</c> object
/// literal, built by <c>createProgramDiagnostics</c>.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS's <c>createProgramDiagnostics</c> closes over six variables and returns an object literal of
/// thirteen members; §5 rule 6 makes that a class whose fields are the closure variables and whose members are the
/// literal's methods. One instance belongs to one program — <c>getCombinedDiagnostics</c> rewrites the position
/// information of what it folds in, which is why the reference refuses to share an instance.
/// <para>
/// PORT-NOTE: TS's local type is <c>Program</c> (the interface <c>createProgram</c> returns); the port passes the
/// class <see cref="Program"/>, which is that interface's implementation and the only thing with the file-reason
/// graph, the resolved project references and <c>getSourceFileByPath</c>.
/// </para>
/// </remarks>
internal sealed class ProgramDiagnostics
{
    /// <summary>ported from: src/compiler/programDiagnostics.ts:93 — <c>computedDiagnostics</c>.</summary>
    /// <remarks>Only valid for a single program: during <c>getCombinedDiagnostics</c> position information is applied
    /// to the diagnostics, so they cannot be shared between programs.</remarks>
    private DiagnosticCollection? _computedDiagnostics;

    /// <summary>ported from: src/compiler/programDiagnostics.ts:96 — <c>fileReasons</c>.</summary>
    /// <remarks>Valid between programs if <c>StructureIsReused.Completely</c>.</remarks>
    private Core.MultiMap<string, IFileIncludeReason> _fileReasons = Core.CreateMultiMap<string, IFileIncludeReason>();

    /// <summary>ported from: src/compiler/programDiagnostics.ts:97 — <c>fileProcessingDiagnostics</c>.</summary>
    private List<IFilePreprocessingDiagnostics>? _fileProcessingDiagnostics;

    /// <summary>ported from: src/compiler/programDiagnostics.ts:100 — <c>commonSourceDirectory</c>.</summary>
    /// <remarks>Valid between programs if <c>StructureIsReused.Completely</c> and the config file is identical.</remarks>
    private string? _commonSourceDirectory;

    /// <summary>ported from: src/compiler/programDiagnostics.ts:101 — <c>configDiagnostics</c>.</summary>
    private DiagnosticCollection? _configDiagnostics;

    /// <summary>ported from: src/compiler/programDiagnostics.ts:102 — <c>lazyConfigDiagnostics</c>.</summary>
    private List<LazyConfigDiagnostic>? _lazyConfigDiagnostics;

    /// <summary>ported from: src/compiler/programDiagnostics.ts:105 — <c>fileReasonsToChain</c>.</summary>
    /// <remarks>Local state, only used during <c>getDiagnostics</c>.</remarks>
    private Dictionary<string, FileReasonToChainCache>? _fileReasonsToChain;

    /// <summary>ported from: src/compiler/programDiagnostics.ts:106 — <c>reasonToRelatedInfo</c>.</summary>
    /// <remarks>
    /// <para>Local state, only used during <c>getDiagnostics</c>. Keys are compared by reference, as TS's <c>Map</c>
    /// compares them (no arm overrides <c>Equals</c>).</para>
    /// <para>PORT-NOTE: TS's value type is <c>DiagnosticWithLocation | false</c>, where <c>false</c> memoizes "looked
    /// up, there is none" and <c>undefined</c> means "absent" — a map cannot hold those two apart, so the port reads
    /// the absence from <c>TryGetValue</c> instead (see <see cref="GetFileIncludeReasonToRelatedInformation"/>).</para>
    /// </remarks>
    private Dictionary<IFileIncludeReason, Diagnostic?>? _reasonToRelatedInfo;

    /// <summary>ported from: src/compiler/programDiagnostics.ts:89 — the closure parameter
    /// <c>getCompilerOptionsObjectLiteralSyntax</c>.</summary>
    private readonly Func<ObjectLiteralExpression?>? _getCompilerOptionsObjectLiteralSyntax;

    /// <summary>ported from: src/compiler/programDiagnostics.ts:89 — <c>createProgramDiagnostics</c>'s closure.</summary>
    private ProgramDiagnostics(Func<ObjectLiteralExpression?>? getCompilerOptionsObjectLiteralSyntax)
        => _getCompilerOptionsObjectLiteralSyntax = getCompilerOptionsObjectLiteralSyntax;

    /// <summary>ported from: src/compiler/programDiagnostics.ts:88-89 — <c>createProgramDiagnostics</c>.</summary>
    internal static ProgramDiagnostics CreateProgramDiagnostics(Func<ObjectLiteralExpression?>? getCompilerOptionsObjectLiteralSyntax)
        => new(getCompilerOptionsObjectLiteralSyntax);

    /// <summary>ported from: src/compiler/programDiagnostics.ts:109-112 — <c>addConfigDiagnostic</c>.</summary>
    internal void AddConfigDiagnostic(Diagnostic diag)
    {
        Debug.Assert(_computedDiagnostics is null, "Cannot modify program diagnostic state after requesting combined diagnostics");
        (_configDiagnostics ??= Utilities.CreateDiagnosticCollection()).Add(diag);
    }

    /// <summary>ported from: src/compiler/programDiagnostics.ts:113-116 — <c>addLazyConfigDiagnostic</c>.</summary>
    internal void AddLazyConfigDiagnostic(SourceFile file, DiagnosticMessage message, params object?[] args)
    {
        Debug.Assert(_computedDiagnostics is null, "Cannot modify program diagnostic state after requesting combined diagnostics");
        (_lazyConfigDiagnostics ??= []).Add(new LazyConfigDiagnostic { File = file, Diagnostic = message, Args = args });
    }

    /// <summary>ported from: src/compiler/programDiagnostics.ts:117-120 — <c>addFileProcessingDiagnostic</c>.</summary>
    internal void AddFileProcessingDiagnostic(IFilePreprocessingDiagnostics diag)
    {
        Debug.Assert(_computedDiagnostics is null, "Cannot modify program diagnostic state after requesting combined diagnostics");
        (_fileProcessingDiagnostics ??= []).Add(diag);
    }

    /// <summary>ported from: src/compiler/program.ts:4577-4585 — <c>addFilePreprocessingFileExplainingDiagnostic</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference declares this in <c>program.ts</c>, not in <c>programDiagnostics.ts</c>; it is the
    /// builder of the <c>FilePreprocessingFileExplainingDiagnostic</c> arm, so the port keeps it next to the arm it
    /// builds (program.ts's own owner calls it under its TS name).
    /// </remarks>
    internal void AddFileExplainingDiagnostic(SourceFile? file, IFileIncludeReason reason, DiagnosticMessage message, params object?[] args)
        => AddFileProcessingDiagnostic(new FilePreprocessingFileExplainingDiagnostic
        {
            Kind = FilePreprocessingDiagnosticsKind.FilePreprocessingFileExplainingDiagnostic,
            File = file?.Path,
            FileProcessingReason = reason,
            Diagnostic = message,
            Args = [.. args],
        });

    /// <summary>ported from: src/compiler/programDiagnostics.ts:121-123 — <c>setCommonSourceDirectory</c>.</summary>
    internal void SetCommonSourceDirectory(string directory) => _commonSourceDirectory = directory;

    /// <summary>ported from: src/compiler/programDiagnostics.ts:125-133 — <c>reuseStateFromOldProgram</c>.</summary>
    internal void ReuseStateFromOldProgram(ProgramDiagnostics oldProgramDiagnostics, bool isConfigIdentical)
    {
        _fileReasons = oldProgramDiagnostics.GetFileReasons();
        _fileProcessingDiagnostics = oldProgramDiagnostics.GetFileProcessingDiagnostics();
        if (isConfigIdentical)
        {
            _commonSourceDirectory = oldProgramDiagnostics.GetCommonSourceDirectory();
            _configDiagnostics = oldProgramDiagnostics.GetConfigDiagnostics();
            _lazyConfigDiagnostics = oldProgramDiagnostics.GetLazyConfigDiagnostics();
        }
    }

    /// <summary>ported from: src/compiler/programDiagnostics.ts:135-137 — <c>getFileProcessingDiagnostics</c>.</summary>
    internal List<IFilePreprocessingDiagnostics>? GetFileProcessingDiagnostics() => _fileProcessingDiagnostics;

    /// <summary>ported from: src/compiler/programDiagnostics.ts:138-140 — <c>getFileReasons</c>.</summary>
    internal Core.MultiMap<string, IFileIncludeReason> GetFileReasons() => _fileReasons;

    /// <summary>ported from: src/compiler/programDiagnostics.ts:141-143 — <c>getCommonSourceDirectory</c>.</summary>
    internal string? GetCommonSourceDirectory() => _commonSourceDirectory;

    /// <summary>ported from: src/compiler/programDiagnostics.ts:144-146 — <c>getConfigDiagnostics</c>.</summary>
    internal DiagnosticCollection? GetConfigDiagnostics() => _configDiagnostics;

    /// <summary>ported from: src/compiler/programDiagnostics.ts:147-149 — <c>getLazyConfigDiagnostics</c>.</summary>
    internal List<LazyConfigDiagnostic>? GetLazyConfigDiagnostics() => _lazyConfigDiagnostics;

    /// <summary>ported from: src/compiler/programDiagnostics.ts:150-188 — <c>getCombinedDiagnostics</c>.</summary>
    /// <remarks>PORT-NOTE: TS switches on <c>diagnostic.kind</c> over the union; the port tests the union's classes
    /// (<see cref="IFilePreprocessingDiagnostics"/> is the union slot).</remarks>
    internal DiagnosticCollection GetCombinedDiagnostics(Program program)
    {
        if (_computedDiagnostics is not null)
        {
            return _computedDiagnostics;
        }

        var computedDiagnostics = Utilities.CreateDiagnosticCollection();
        _computedDiagnostics = computedDiagnostics;

        _configDiagnostics?.GetDiagnostics().ForEach(d => computedDiagnostics.Add(d));

        _fileProcessingDiagnostics?.ForEach(diagnostic =>
        {
            switch (diagnostic)
            {
                case FilePreprocessingFileExplainingDiagnostic explainingDiagnostic:
                    computedDiagnostics.Add(
                        CreateDiagnosticExplainingFile(
                            program,
                            explainingDiagnostic.File is not null ? program.GetSourceFileByPath(explainingDiagnostic.File) : null,
                            explainingDiagnostic.FileProcessingReason,
                            (DiagnosticMessage)explainingDiagnostic.Diagnostic!,
                            [.. explainingDiagnostic.Args ?? []]));
                    break;
                case FilePreprocessingLibReferenceDiagnostic libReferenceDiagnostic:
                    computedDiagnostics.Add(FilePreprocessingLibreferenceDiagnostic(program, libReferenceDiagnostic));
                    break;
                case ResolutionDiagnostics resolutionDiagnostics:
                    resolutionDiagnostics.Diagnostics!.ForEach(d => computedDiagnostics.Add((Diagnostic)d!));
                    break;
                default:
                    Debug.AssertNever(diagnostic);
                    break;
            }
        });

        _lazyConfigDiagnostics?.ForEach(lazyConfigDiagnostic => computedDiagnostics.Add(
            CreateDiagnosticExplainingFile(program, lazyConfigDiagnostic.File, /*fileProcessingReason*/ null, lazyConfigDiagnostic.Diagnostic, lazyConfigDiagnostic.Args)));

        _fileReasonsToChain = null;
        _reasonToRelatedInfo = null;
        return computedDiagnostics;
    }

    /// <summary>ported from: src/compiler/programDiagnostics.ts:190-204 — <c>filePreprocessingLibreferenceDiagnostic</c>.</summary>
    private static Diagnostic FilePreprocessingLibreferenceDiagnostic(Program program, FilePreprocessingLibReferenceDiagnostic diagnostic)
    {
        var reason = (IReferencedFile)diagnostic.Reason!;
        var location = (IReferenceFileLocation)Program.GetReferencedFileLocation(program, reason)!;
        var file = location.File!;
        var libReference = file.LibReferenceDirectives![reason.Index];
        var libName = Utilities.GetLibNameFromLibReference(libReference);
        var unqualifiedLibName = Core.RemoveSuffix(Core.RemovePrefix(libName, "lib."), ".d.ts");
        var suggestion = Core.GetSpellingSuggestion(unqualifiedLibName, Utilities.Libs, Core.Identity);
        // PORT-NOTE: TS's `pos`/`end` are `number | undefined` and it wraps them in Debug.checkDefined; the generated
        // IReferenceFileLocation types both as int, so there is nothing left to check.
        return Utilities.CreateFileDiagnostic(
            file,
            location.Pos,
            location.End - location.Pos,
            suggestion is not null ? Diagnostics.Cannot_find_lib_definition_for_0_Did_you_mean_1 : Diagnostics.Cannot_find_lib_definition_for_0,
            libName,
            suggestion!);
    }

    /// <summary>ported from: src/compiler/programDiagnostics.ts:206-320 — <c>createDiagnosticExplainingFile</c>.</summary>
    private Diagnostic CreateDiagnosticExplainingFile(
        Program program,
        SourceFile? file,
        IFileIncludeReason? fileProcessingReason,
        DiagnosticMessage diagnostic,
        object?[] args)
    {
        HashSet<IFileIncludeReason>? seenReasons = null;
        List<IDiagnosticMessageChain>? fileIncludeReasons = null;
        List<IDiagnosticRelatedInformation>? relatedInfo = null;
        IDiagnosticMessageChain? fileIncludeReasonDetails = null;
        List<IDiagnosticMessageChain>? redirectInfo = null;
        IDiagnosticMessageChain? chain = null;

        var reasons = file is not null ? _fileReasons.Get(file.Path ?? "") : null;
        IReferencedFile? locationReason = fileProcessingReason as IReferencedFile;
        var cachedChain = file is not null ? _fileReasonsToChain?.GetValueOrDefault(file.Path ?? "") : null;
        if (cachedChain is not null)
        {
            if (cachedChain.FileIncludeReasonDetails is not null)
            {
                seenReasons = reasons is not null ? [.. reasons] : [];
                reasons?.ForEach(PopulateRelatedInfo);
            }
            else
            {
                reasons?.ForEach(ProcessReason);
            }
            redirectInfo = cachedChain.RedirectInfo;
        }
        else
        {
            reasons?.ForEach(ProcessReason);
            redirectInfo = file is not null
                ? ExplainIfFileIsRedirectAndImpliedFormat(file, program.GetCompilerOptionsForFile(file))
                : null;
        }

        if (fileProcessingReason is not null) ProcessReason(fileProcessingReason);
        var processedExtraReason = seenReasons?.Count != reasons?.Count;

        // If we have location and there is only one reason file is in which is the location, dont add details for file include
        if (locationReason is not null && seenReasons?.Count == 1) seenReasons = null;

        if (seenReasons is not null && cachedChain is not null)
        {
            if (cachedChain.Details is not null && !processedExtraReason)
            {
                chain = Utilities.ChainDiagnosticMessages(cachedChain.Details, diagnostic, args);
            }
            else if (cachedChain.FileIncludeReasonDetails is not null)
            {
                if (!processedExtraReason)
                {
                    if (!CachedFileIncludeDetailsHasProcessedExtraReason())
                    {
                        fileIncludeReasonDetails = cachedChain.FileIncludeReasonDetails;
                    }
                    else
                    {
                        fileIncludeReasons = [.. cachedChain.FileIncludeReasonDetails.Next!.Take(reasons!.Count)];
                    }
                }
                else
                {
                    if (!CachedFileIncludeDetailsHasProcessedExtraReason())
                    {
                        fileIncludeReasons = [.. cachedChain.FileIncludeReasonDetails.Next!, fileIncludeReasons![0]];
                    }
                    else
                    {
                        var extraReasonDetails = cachedChain.FileIncludeReasonDetails.Next!.Take(reasons!.Count).ToList();
                        extraReasonDetails.Add(fileIncludeReasons![0]);
                        fileIncludeReasons = extraReasonDetails;
                    }
                }
            }
        }

        if (chain is null)
        {
            if (fileIncludeReasonDetails is null)
            {
                fileIncludeReasonDetails = seenReasons is not null
                    ? Utilities.ChainDiagnosticMessages(fileIncludeReasons, Diagnostics.The_file_is_in_the_program_because_Colon)
                    : null;
            }

            List<IDiagnosticMessageChain>? reasonDetailsWithRedirect = null;
            if (redirectInfo is not null && fileIncludeReasonDetails is not null)
            {
                // TS: [fileIncludeReasonDetails, ...redirectInfo]
                reasonDetailsWithRedirect = [fileIncludeReasonDetails, .. redirectInfo];
            }

            object? details = redirectInfo is not null
                ? reasonDetailsWithRedirect ?? (object?)redirectInfo
                : fileIncludeReasonDetails;
            chain = Utilities.ChainDiagnosticMessages(details, diagnostic, args);
        }

        // This is chain's next contains:
        //   - File is in program because:
        //      - Files reasons listed
        //      - extra reason if its not already processed - this happens in case sensitive file system where files differ in casing and we are giving reasons for two files so reason is not in file's reason
        //     fyi above whole secton is ommited if we have single reason and we are reporting at that reason's location
        //   - redirect and additional information about file
        // So cache result if we havent ommited file include reasons
        if (file is not null)
        {
            if (cachedChain is not null)
            {
                // Cache new fileIncludeDetails if we have update
                // Or if we had cached with more details than the reasons
                if (cachedChain.FileIncludeReasonDetails is null || (!processedExtraReason && fileIncludeReasonDetails is not null))
                {
                    cachedChain.FileIncludeReasonDetails = fileIncludeReasonDetails;
                }
            }
            else
            {
                _fileReasonsToChain ??= [];
                cachedChain = new FileReasonToChainCache { FileIncludeReasonDetails = fileIncludeReasonDetails, RedirectInfo = redirectInfo };
                _fileReasonsToChain[file.Path ?? ""] = cachedChain;
            }
            // If we didnt compute extra file include reason , cache the details to use directly
            if (cachedChain.Details is null && !processedExtraReason) cachedChain.Details = chain!.Next;
        }

        var location = locationReason is not null ? Program.GetReferencedFileLocation(program, locationReason) : null;
        // PORT-NOTE: TS's `isReferenceFileLocation` is a type predicate over the two location shapes; the port tests
        // the generated IReferenceFileLocation, which is the arm that carries pos/end.
        return location is IReferenceFileLocation referenceLocation
            ? Utilities.CreateFileDiagnosticFromMessageChain(referenceLocation.File!, referenceLocation.Pos, referenceLocation.End - referenceLocation.Pos, chain!, relatedInfo)
            : Utilities.CreateCompilerDiagnosticFromMessageChain(chain!, relatedInfo);

        void ProcessReason(IFileIncludeReason reason)
        {
            if (seenReasons?.Contains(reason) == true) return;
            (seenReasons ??= []).Add(reason);
            (fileIncludeReasons ??= []).Add(FileIncludeReasonToDiagnostics(program, reason));
            PopulateRelatedInfo(reason);
        }

        void PopulateRelatedInfo(IFileIncludeReason reason)
        {
            if (locationReason is null && Utilities.IsReferencedFile(reason))
            {
                // Report error at first reference file or file currently in processing and dont report in related information
                locationReason = (IReferencedFile)reason;
            }
            else if (!ReferenceEquals(locationReason, reason))
            {
                var info = GetFileIncludeReasonToRelatedInformation(program, reason);
                if (info is not null) (relatedInfo ??= []).Add(info);
            }
        }

        bool CachedFileIncludeDetailsHasProcessedExtraReason()
            => cachedChain!.FileIncludeReasonDetails!.Next?.Count != reasons?.Count;
    }

    /// <summary>ported from: src/compiler/programDiagnostics.ts:322-326 — <c>getFileIncludeReasonToRelatedInformation</c>.</summary>
    private Diagnostic? GetFileIncludeReasonToRelatedInformation(Program program, IFileIncludeReason reason)
    {
        _reasonToRelatedInfo ??= [];
        // PORT-NOTE: TS stores `false` for "looked up, there is none" so that the second lookup is still a cache hit;
        // a value of null in this dictionary carries that meaning, and the absent key is the reference's `undefined`.
        if (!_reasonToRelatedInfo.TryGetValue(reason, out var relatedInfo))
        {
            relatedInfo = FileIncludeReasonToRelatedInformation(program, reason);
            _reasonToRelatedInfo[reason] = relatedInfo;
        }

        return relatedInfo;
    }

    /// <summary>ported from: src/compiler/programDiagnostics.ts:328-426 — <c>fileIncludeReasonToRelatedInformation</c>.</summary>
    private Diagnostic? FileIncludeReasonToRelatedInformation(Program program, IFileIncludeReason reason)
    {
        if (Utilities.IsReferencedFile(reason))
        {
            var referenceLocation = Program.GetReferencedFileLocation(program, (IReferencedFile)reason);
            DiagnosticMessage? referencedMessage = null;
            // PORT-NOTE: TS switches on reason.kind inside the `isReferencedFile` guard, where the kind can only be
            // one of the four referenced-file kinds; the port reads the same slot on the union.
            switch (reason.Kind)
            {
                case FileIncludeKind.Import:
                    referencedMessage = Diagnostics.File_is_included_via_import_here;
                    break;
                case FileIncludeKind.ReferenceFile:
                    referencedMessage = Diagnostics.File_is_included_via_reference_here;
                    break;
                case FileIncludeKind.TypeReferenceDirective:
                    referencedMessage = Diagnostics.File_is_included_via_type_library_reference_here;
                    break;
                case FileIncludeKind.LibReferenceDirective:
                    referencedMessage = Diagnostics.File_is_included_via_library_reference_here;
                    break;
                default:
                    Debug.AssertNever(reason);
                    break;
            }

            return referenceLocation is IReferenceFileLocation locationWithRange
                ? Utilities.CreateFileDiagnostic(
                    locationWithRange.File!,
                    locationWithRange.Pos,
                    locationWithRange.End - locationWithRange.Pos,
                    referencedMessage!)
                : null;
        }

        var currentDirectory = program._currentDirectory;
        var rootNames = program._rootNames;
        var options = program._options;
        var configFile = options.ConfigFile;
        if (configFile is null) return null;
        Node? configFileNode = null;
        DiagnosticMessage? message = null;
        switch (reason)
        {
            case RootFile rootFile:
                if (configFile.ConfigFileSpecs is null) return null;
                var fileName = Paths.GetNormalizedAbsolutePath(rootNames[rootFile.Index], currentDirectory);
                var matchedByFiles = GetMatchedFileSpec(program, fileName);
                if (matchedByFiles is not null)
                {
                    configFileNode = Utilities.GetTsConfigPropArrayElementValue(configFile, "files", matchedByFiles);
                    message = Diagnostics.File_is_matched_by_files_list_specified_here;
                    break;
                }

                var matchedByInclude = GetMatchedIncludeSpec(program, fileName);
                // Could be additional files specified as roots
                if (!Js.IsTruthy(matchedByInclude) || matchedByInclude is not string matchedByIncludeSpec) return null;
                configFileNode = Utilities.GetTsConfigPropArrayElementValue(configFile, "include", matchedByIncludeSpec);
                message = Diagnostics.File_is_matched_by_include_pattern_specified_here;
                break;
            case ProjectReferenceFile projectReferenceFile:
                var resolvedProjectReferences = program._resolvedProjectReferences;
                var projectReferences = program._projectReferences;
                var referencedResolvedRef = Debug.CheckDefined(projectReferencesAtIndex(resolvedProjectReferences, projectReferenceFile.Index));
                var referenceInfo = Utilities.ForEachProjectReference(
                    projectReferences,
                    resolvedProjectReferences,
                    (resolvedRef, parent, index) => ReferenceEquals(resolvedRef, referencedResolvedRef)
                        // PORT-NOTE: TS's `parent?.sourceFile || options.configFile!` mixes the resolved reference's
                        // file with the config file; both are TsConfigSourceFile there, while the port's
                        // ITsConfigSourceFile is not a SourceFile, so the config file is bridged with a cast.
                        ? new ProjectReferenceInfo(Js.IsTruthy(parent?.SourceFile) ? parent!.SourceFile! : (SourceFile)configFile, index)
                        : null);
                if (referenceInfo is null) return null;
                var referencesSyntax = Utilities.ForEachTsConfigPropArray<ArrayLiteralExpression>(
                    (ITsConfigSourceFile)(object)referenceInfo.SourceFile,
                    "references",
                    property => property.Initializer is ArrayLiteralExpression initializer ? initializer : null);
                return referencesSyntax is not null && referencesSyntax.Elements!.Count > referenceInfo.Index
                    ? Utilities.CreateDiagnosticForNodeInSourceFile(
                        referenceInfo.SourceFile,
                        referencesSyntax.Elements[referenceInfo.Index],
                        projectReferenceFile.Kind == FileIncludeKind.OutputFromProjectReference
                            ? Diagnostics.File_is_output_from_referenced_project_specified_here
                            : Diagnostics.File_is_source_from_referenced_project_specified_here)
                    : null;
            case AutomaticTypeDirectiveFile automaticTypeDirectiveFile:
                configFileNode = Utilities.GetOptionsSyntaxByArrayElementValue(
                    _getCompilerOptionsObjectLiteralSyntax?.Invoke(),
                    "types",
                    Utilities.UsesWildcardTypes(options) ? "*" : automaticTypeDirectiveFile.TypeReference!);

                message = Diagnostics.File_is_entry_point_of_type_library_specified_here;
                break;
            case LibFile libFile:
                if (libFile.Index is not null)
                {
                    configFileNode = Utilities.GetOptionsSyntaxByArrayElementValue(_getCompilerOptionsObjectLiteralSyntax?.Invoke(), "lib", options.Lib![libFile.Index.Value]);
                    message = Diagnostics.File_is_library_specified_here;
                    break;
                }

                var target = ScriptTargets.GetNameOfScriptTarget(Utilities.GetEmitScriptTarget(options));
                configFileNode = target is not null ? Utilities.GetOptionsSyntaxByValue(_getCompilerOptionsObjectLiteralSyntax?.Invoke(), "target", target) : null;
                message = Diagnostics.File_is_default_library_for_target_specified_here;
                break;
            default:
                Debug.AssertNever(reason);
                break;
        }

        // PORT-NOTE: TS's options.configFile is a TsConfigSourceFile, i.e. a SourceFile; the port's generated
        // ITsConfigSourceFile is not, so the diagnostic site bridges it with a cast (see ProjectReferenceInfo above).
        return configFileNode is not null
            ? Utilities.CreateDiagnosticForNodeInSourceFile((SourceFile)configFile, configFileNode, message!)
            : null;

        static IResolvedProjectReference? projectReferencesAtIndex(List<IResolvedProjectReference?>? resolvedProjectReferences, int index)
            => resolvedProjectReferences is not null && index < resolvedProjectReferences.Count ? resolvedProjectReferences[index] : null;
    }

    /// <summary>ported from: src/compiler/programDiagnostics.ts:383-386 — the object literal the project-reference
    /// callback returns.</summary>
    private sealed record ProjectReferenceInfo(SourceFile SourceFile, int Index);

    // ---------------------------------------------------------------------------------------------
    // The explainers programDiagnostics.ts imports from watch.ts
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/watch.ts:415-425 — <c>getMatchedFileSpec</c>.</summary>
    /// <remarks>PORT-NOTE: TS reads <c>program.getCanonicalFileName</c>, a member of the Program interface; the port's
    /// Program keeps it in the <c>_getCanonicalFileName</c> field (assigned from the host in
    /// <c>Program/Program.cs</c>).</remarks>
    internal static string? GetMatchedFileSpec(Program program, string fileName)
    {
        var configFile = program._options.ConfigFile;
        if (configFile?.ConfigFileSpecs?.ValidatedFilesSpec is null) return null;

        var filePath = program._getCanonicalFileName(fileName);
        var basePath = Paths.GetDirectoryPath(Paths.GetNormalizedAbsolutePath(ConfigFileNameOf(configFile), program._currentDirectory));
        var index = Core.FindIndex(configFile.ConfigFileSpecs.ValidatedFilesSpec, (fileSpec, _) =>
            program._getCanonicalFileName(Paths.GetNormalizedAbsolutePath(fileSpec, basePath)) == filePath);
        return index != -1 ? configFile.ConfigFileSpecs.ValidatedFilesSpecBeforeSubstitution![index] : null;
    }

    /// <summary>ported from: src/compiler/watch.ts:426-444 — <c>getMatchedIncludeSpec</c>.</summary>
    /// <remarks>PORT-NOTE: TS's return type is <c>string | true | undefined</c>; the port returns <c>object?</c> whose
    /// arms are the matched spec (<see cref="string"/>) and <c>true</c> for the default include spec, exactly as §12.5
    /// keeps a union that cannot collapse. Callers test it with <c>Js.IsTruthy</c> and <c>is string</c>.</remarks>
    internal static object? GetMatchedIncludeSpec(Program program, string fileName)
    {
        var configFile = program._options.ConfigFile;
        if (configFile?.ConfigFileSpecs?.ValidatedIncludeSpecs is null) return null;

        // Return true if its default include spec
        if (configFile.ConfigFileSpecs.IsDefaultIncludeSpec) return true;

        var isJsonFile = Paths.FileExtensionIs(fileName, Extension.Json.Value);
        var basePath = Paths.GetDirectoryPath(Paths.GetNormalizedAbsolutePath(ConfigFileNameOf(configFile), program._currentDirectory));
        // PORT-NOTE: TS calls `program.useCaseSensitiveFileNames()`. IModuleSpecifierResolutionHost declares that name as
        // a method (which is what the reference means); IModuleResolutionHost declares the same name as an `object?`
        // slot, so the call is made through the interface that spells it as a method.
        var useCaseSensitiveFileNames = ((IModuleSpecifierResolutionHost)program).UseCaseSensitiveFileNames();
        var index = Core.FindIndex(configFile.ConfigFileSpecs.ValidatedIncludeSpecs, (includeSpec, _) =>
        {
            if (isJsonFile && !Core.EndsWith(includeSpec, Extension.Json.Value)) return false;
            var pattern = Utilities.GetPatternFromSpec(includeSpec, basePath, Wildcard.Usage.Files);
            return Js.IsTruthy(pattern) && Wildcard.GetRegexFromPattern($"(?:{pattern})$", useCaseSensitiveFileNames).IsMatch(fileName);
        });
        return index != -1 ? configFile.ConfigFileSpecs.ValidatedIncludeSpecsBeforeSubstitution![index] : null;
    }

    /// <summary>ported from: src/compiler/watch.ts:361-414 — <c>explainIfFileIsRedirectAndImpliedFormat</c>.</summary>
    internal static List<IDiagnosticMessageChain>? ExplainIfFileIsRedirectAndImpliedFormat(
        SourceFile file,
        ICompilerOptions options,
        Func<string, string>? fileNameConvertor = null)
    {
        List<IDiagnosticMessageChain>? result = null;
        if (file.Path != file.ResolvedPath)
        {
            (result ??= []).Add(Utilities.ChainDiagnosticMessages(
                /*details*/ null,
                Diagnostics.File_is_output_of_project_reference_source_0,
                ToFileName(file.OriginalFileName ?? "", fileNameConvertor)));
        }

        if (file.RedirectInfo is not null)
        {
            (result ??= []).Add(Utilities.ChainDiagnosticMessages(
                /*details*/ null,
                Diagnostics.File_redirects_to_file_0,
                ToFileName(file.RedirectInfo.RedirectTarget?.FileName ?? "", fileNameConvertor)));
        }

        if (Utilities.IsExternalOrCommonJsModule(file))
        {
            switch (Program.GetImpliedNodeFormatForEmitWorker(file, options))
            {
                case ModuleKind.ESNext:
                    if (file.PackageJsonScope is not null)
                    {
                        (result ??= []).Add(Utilities.ChainDiagnosticMessages(
                            /*details*/ null,
                            Diagnostics.File_is_ECMAScript_module_because_0_has_field_type_with_value_module,
                            ToFileName(Core.LastOrUndefined(file.PackageJsonLocations!) ?? "", fileNameConvertor)));
                    }
                    break;
                case ModuleKind.CommonJS:
                    if (file.PackageJsonScope is not null)
                    {
                        (result ??= []).Add(Utilities.ChainDiagnosticMessages(
                            /*details*/ null,
                            Js.IsTruthy(PackageJsonContentOf(file).Get("type"))
                                ? Diagnostics.File_is_CommonJS_module_because_0_has_field_type_whose_value_is_not_module
                                : Diagnostics.File_is_CommonJS_module_because_0_does_not_have_field_type,
                            ToFileName(Core.LastOrUndefined(file.PackageJsonLocations!) ?? "", fileNameConvertor)));
                    }
                    else if (file.PackageJsonLocations?.Count > 0)
                    {
                        (result ??= []).Add(Utilities.ChainDiagnosticMessages(
                            /*details*/ null,
                            Diagnostics.File_is_CommonJS_module_because_package_json_was_not_found));
                    }
                    break;
            }
        }

        return result;
    }

    /// <summary>ported from: src/compiler/watch.ts:445-552 — <c>fileIncludeReasonToDiagnostics</c>.</summary>
    internal static IDiagnosticMessageChain FileIncludeReasonToDiagnostics(Program program, IFileIncludeReason reason, Func<string, string>? fileNameConvertor = null)
    {
        var options = program._options;
        if (Utilities.IsReferencedFile(reason))
        {
            var referenceLocation = Program.GetReferencedFileLocation(program, (IReferencedFile)reason);
            // PORT-NOTE: TS reads `referenceLocation.file`/`.text`/`.packageId` off the two-shape union, where all
            // three slots exist on both arms; the port's union slot is a marker interface, so both shapes are bound
            // once here and read through the bound locals — the same narrowing Program.IsReferenceFileLocation does.
            var rangeLocation = referenceLocation as IReferenceFileLocation;
            var syntheticLocation = referenceLocation as ISyntheticReferenceFileLocation;
            var packageId = rangeLocation is not null ? rangeLocation.PackageId : syntheticLocation!.PackageId;
            var referenceText = rangeLocation is not null
                ? (rangeLocation.File!.Text ?? "").Substring(rangeLocation.Pos, rangeLocation.End - rangeLocation.Pos)
                : $"\"{syntheticLocation!.Text}\"";
            DiagnosticMessage? message = null;
            Debug.Assert(
                rangeLocation is not null || reason.Kind == FileIncludeKind.Import,
                "Only synthetic references are imports");
            switch (reason.Kind)
            {
                case FileIncludeKind.Import:
                    if (rangeLocation is not null)
                    {
                        message = packageId is not null
                            ? Diagnostics.Imported_via_0_from_file_1_with_packageId_2
                            : Diagnostics.Imported_via_0_from_file_1;
                    }
                    else if (syntheticLocation!.Text == Utilities.ExternalHelpersModuleNameText)
                    {
                        message = packageId is not null
                            ? Diagnostics.Imported_via_0_from_file_1_with_packageId_2_to_import_importHelpers_as_specified_in_compilerOptions
                            : Diagnostics.Imported_via_0_from_file_1_to_import_importHelpers_as_specified_in_compilerOptions;
                    }
                    else
                    {
                        message = packageId is not null
                            ? Diagnostics.Imported_via_0_from_file_1_with_packageId_2_to_import_jsx_and_jsxs_factory_functions
                            : Diagnostics.Imported_via_0_from_file_1_to_import_jsx_and_jsxs_factory_functions;
                    }
                    break;
                case FileIncludeKind.ReferenceFile:
                    Debug.Assert(packageId is null);
                    message = Diagnostics.Referenced_via_0_from_file_1;
                    break;
                case FileIncludeKind.TypeReferenceDirective:
                    message = packageId is not null
                        ? Diagnostics.Type_library_referenced_via_0_from_file_1_with_packageId_2
                        : Diagnostics.Type_library_referenced_via_0_from_file_1;
                    break;
                case FileIncludeKind.LibReferenceDirective:
                    Debug.Assert(packageId is null);
                    message = Diagnostics.Library_referenced_via_0_from_file_1;
                    break;
                default:
                    Debug.AssertNever(reason);
                    break;
            }

            return Utilities.ChainDiagnosticMessages(
                /*details*/ null,
                message!,
                referenceText,
                ToFileName(ReferenceLocationFile(referenceLocation), fileNameConvertor),
                packageId is not null ? Utilities.PackageIdToString(packageId) : null!);
        }

        switch (reason)
        {
            case RootFile rootFile:
                if (options.ConfigFile?.ConfigFileSpecs is null)
                {
                    return Utilities.ChainDiagnosticMessages(/*details*/ null, Diagnostics.Root_file_specified_for_compilation);
                }

                var fileName = Paths.GetNormalizedAbsolutePath(program._rootNames[rootFile.Index], program._currentDirectory);
                var matchedByFiles = GetMatchedFileSpec(program, fileName);
                if (matchedByFiles is not null)
                {
                    return Utilities.ChainDiagnosticMessages(/*details*/ null, Diagnostics.Part_of_files_list_in_tsconfig_json);
                }

                var matchedByInclude = GetMatchedIncludeSpec(program, fileName);
                return matchedByInclude is string matchedByIncludeSpec
                    ? Utilities.ChainDiagnosticMessages(
                        /*details*/ null,
                        Diagnostics.Matched_by_include_pattern_0_in_1,
                        matchedByIncludeSpec,
                        ToFileName(options.ConfigFile!, fileNameConvertor))
                    // Could be additional files specified as roots or matched by default include
                    : Utilities.ChainDiagnosticMessages(
                        /*details*/ null,
                        Js.IsTruthy(matchedByInclude)
                            ? Diagnostics.Matched_by_default_include_pattern_Asterisk_Asterisk_Slash_Asterisk
                            : Diagnostics.Root_file_specified_for_compilation);
            case ProjectReferenceFile projectReferenceFile:
                var isOutput = projectReferenceFile.Kind == FileIncludeKind.OutputFromProjectReference;
                var referencedResolvedRef = Debug.CheckDefined(resolvedProjectReferenceAt(program, projectReferenceFile.Index));
                return Utilities.ChainDiagnosticMessages(
                    /*details*/ null,
                    options.OutFile is not null
                        ? isOutput
                            ? Diagnostics.Output_from_referenced_project_0_included_because_1_specified
                            : Diagnostics.Source_from_referenced_project_0_included_because_1_specified
                        : isOutput
                            ? Diagnostics.Output_from_referenced_project_0_included_because_module_is_specified_as_none
                            : Diagnostics.Source_from_referenced_project_0_included_because_module_is_specified_as_none,
                    ToFileName(referencedResolvedRef.SourceFile!.FileName ?? "", fileNameConvertor),
                    options.OutFile is not null ? "--outFile" : "--out");
            case AutomaticTypeDirectiveFile automaticTypeDirectiveFile:
                (DiagnosticMessage Message, object?[] Args) typeLibraryMessageAndArgs = !Utilities.UsesWildcardTypes(options)
                    ? automaticTypeDirectiveFile.PackageId is not null
                        ? (Diagnostics.Entry_point_of_type_library_0_specified_in_compilerOptions_with_packageId_1,
                            new object?[] { automaticTypeDirectiveFile.TypeReference!, Utilities.PackageIdToString(automaticTypeDirectiveFile.PackageId) })
                        : (Diagnostics.Entry_point_of_type_library_0_specified_in_compilerOptions,
                            new object?[] { automaticTypeDirectiveFile.TypeReference! })
                    : automaticTypeDirectiveFile.PackageId is not null
                        ? (Diagnostics.Entry_point_for_implicit_type_library_0_with_packageId_1,
                            new object?[] { automaticTypeDirectiveFile.TypeReference!, Utilities.PackageIdToString(automaticTypeDirectiveFile.PackageId) })
                        : (Diagnostics.Entry_point_for_implicit_type_library_0,
                            new object?[] { automaticTypeDirectiveFile.TypeReference! });

                return Utilities.ChainDiagnosticMessages(/*details*/ null, typeLibraryMessageAndArgs.Message, typeLibraryMessageAndArgs.Args);
            case LibFile libFile:
                if (libFile.Index is not null)
                {
                    return Utilities.ChainDiagnosticMessages(/*details*/ null, Diagnostics.Library_0_specified_in_compilerOptions, options.Lib![libFile.Index.Value]);
                }

                var target = ScriptTargets.GetNameOfScriptTarget(Utilities.GetEmitScriptTarget(options));
                (DiagnosticMessage Message, object?[] Args) libraryMessageAndArgs = target is not null
                    ? (Diagnostics.Default_library_for_target_0, new object?[] { target })
                    : (Diagnostics.Default_library, []);
                return Utilities.ChainDiagnosticMessages(/*details*/ null, libraryMessageAndArgs.Message, libraryMessageAndArgs.Args);
            default:
                // PORT-NOTE: TS's exhaustive switch ends in `Debug.assertNever`; C# needs the arm to produce the
                // method's value (and needs a default arm even when the enum is covered), so it is the port's throwing
                // unreachable arm, as Utilities.Program.Nodes.cs does elsewhere.
                return Debug.Fail<IDiagnosticMessageChain>($"unexpected file include reason kind {(int)reason.Kind}");
        }

        static IResolvedProjectReference? resolvedProjectReferenceAt(Program program, int index)
        {
            var resolvedProjectReferences = program._resolvedProjectReferences;
            return resolvedProjectReferences is not null && index < resolvedProjectReferences.Count ? resolvedProjectReferences[index] : null;
        }
    }

    /// <summary>ported from: src/compiler/watch.ts:419 and 433 — <c>configFile.fileName</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>TsConfigSourceFile</c> is a <c>SourceFile</c>, so <c>configFile.fileName</c> is
    /// always there; the port's generated <see cref="ITsConfigSourceFile"/> carries only the two tsconfig slots, so the
    /// name is read through the <c>SourceFile</c> the tsconfig parse produces — the same bridge
    /// <see cref="GetTsConfigObjectLiteralExpression"/> makes for <c>statements</c>.</remarks>
    private static string ConfigFileNameOf(ITsConfigSourceFile configFile) => ((SourceFile)configFile).FileName ?? "";

    /// <summary>ported from: src/compiler/watch.ts:553-557 — <c>toFileName</c>.</summary>
    private static string ToFileName(object file, Func<string, string>? fileNameConvertor = null)
    {
        var fileName = file is string text ? text : ((SourceFile)file).FileName ?? "";
        return fileNameConvertor is not null ? fileNameConvertor(fileName) : fileName;
    }

    /// <summary>ported from: src/compiler/watch.ts:450 — the <c>referenceLocation.file</c> both location shapes
    /// carry.</summary>
    private static SourceFile ReferenceLocationFile(object referenceLocation)
        => referenceLocation is IReferenceFileLocation rangeLocation ? rangeLocation.File! : ((ISyntheticReferenceFileLocation)referenceLocation).File!;

    /// <summary>ported from: src/compiler/watch.ts:393 — <c>file.packageJsonScope.contents.packageJsonContent</c>.</summary>
    /// <remarks>PORT-NOTE: the generated model types the slot as <c>object?</c> ("PackageJsonInfo is declared outside
    /// the extracted sources"), so the module-resolution slice's <see cref="PackageJsonInfo"/> is what the port
    /// casts to — the same type <c>ModuleNameResolver</c> builds.</remarks>
    private static JsObject PackageJsonContentOf(SourceFile file)
        => ((PackageJsonInfo)file.PackageJsonScope!).Contents.PackageJsonContent;
}

// ---------------------------------------------------------------------------------------------
// The utilities.ts / watch.ts helpers programDiagnostics.ts depends on
// ---------------------------------------------------------------------------------------------

internal static partial class Utilities
{
    /// <summary>ported from: src/compiler/program.ts:1162-1169 — <c>isReferencedFile</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's version is a type predicate over the reason union; the port's version is the same test as a
    /// plain <c>bool</c>, because the arms are distinct classes (<c>reason is ReferencedFile</c> is the port's
    /// narrowing). It is declared here because both <c>programDiagnostics.ts</c> and <c>program.ts</c> call it and
    /// neither owns it.
    /// </remarks>
    public static bool IsReferencedFile(IFileIncludeReason? reason)
        => reason?.Kind switch
        {
            FileIncludeKind.Import
                or FileIncludeKind.ReferenceFile
                or FileIncludeKind.TypeReferenceDirective
                or FileIncludeKind.LibReferenceDirective => true,
            _ => false,
        };

    /// <summary>ported from: src/compiler/utilities.ts:614-616 — <c>externalHelpersModuleNameText</c>.</summary>
    /// <remarks>PORT-NOTE: declared here because <c>Program/Program.Graph.cs</c> (the only other user) and
    /// <c>programDiagnostics.ts</c>'s explainer both need it and no other ported file declares it.</remarks>
    public static readonly string ExternalHelpersModuleNameText = "tslib";

    /// <summary>ported from: src/compiler/utilities.ts:9688-9692 — <c>getPatternFromSpec</c>.</summary>
    /// <remarks>PORT-NOTE: TS keeps this next to the other wildcard helpers in <c>utilities.ts</c>; the port's
    /// wildcard family lives in <c>Core/Wildcard.cs</c>, so this is the <c>Utilities</c> face that calls into it —
    /// the same split <c>getMatchPatternOrExact</c>/<c>tryParsePatterns</c> already follow.</remarks>
    public static string? GetPatternFromSpec(string spec, string basePath, Wildcard.Usage usage)
    {
        var pattern = Js.IsTruthy(spec) ? Wildcard.GetSubPatternFromSpec(spec, basePath, usage, /*matcher*/ null) : null;
        return Js.IsTruthy(pattern)
            ? $"^(?:{pattern}){(usage == Wildcard.Usage.Exclude ? "(?:$|/)" : "$")}"
            : null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:2364-2382 — <c>tryGetTextOfPropertyName</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is the union <c>PropertyName | NoSubstitutionTemplateLiteral |
    /// JsxAttributeName</c>, which share only <c>Node</c>; the port takes the <c>Node</c> the switch reads
    /// (<c>name.kind</c>), which is what every call site has.</remarks>
    public static string? TryGetTextOfPropertyName(Node name)
    {
        switch (name.Kind)
        {
            case SyntaxKind.Identifier:
                return Js.IsTruthy(((Identifier)name).EmitNode?.AutoGenerate) ? null : ((Identifier)name).EscapedText;
            case SyntaxKind.PrivateIdentifier:
                return Js.IsTruthy(((PrivateIdentifier)name).EmitNode?.AutoGenerate) ? null : ((PrivateIdentifier)name).EscapedText;
            case SyntaxKind.StringLiteral:
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
                return EscapeLeadingUnderscores(((LiteralExpression)name).Text ?? "");
            case SyntaxKind.ComputedPropertyName:
                var expression = ((ComputedPropertyName)name).Expression;
                return expression is not null && IsStringOrNumericLiteralLike(expression)
                    ? EscapeLeadingUnderscores(((LiteralExpression)expression).Text ?? "")
                    : null;
            case SyntaxKind.JsxNamespacedName:
                return GetEscapedTextOfJsxNamespacedName((JsxNamespacedName)name);
            default:
                Debug.AssertNever(name);
                return null;
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:3095-3104 — <c>forEachPropertyAssignment</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>forEach</c> keeps iterating while the callback returns a falsy result; the port
    /// writes the loop out so the falsy test is <c>Js.IsTruthy</c> (which boxes a value-typed <typeparamref name="T"/>,
    /// as the reference's <c>if (result)</c> does).</remarks>
    public static T? ForEachPropertyAssignment<T>(ObjectLiteralExpression? objectLiteral, string key, Func<PropertyAssignment, T?> callback, string? key2 = null)
    {
        if (objectLiteral?.Properties is null) return default;
        foreach (var property in objectLiteral.Properties)
        {
            if (!NodeTests.IsPropertyAssignment(property)) continue;
            var propName = TryGetTextOfPropertyName(((PropertyAssignment)property).Name!);
            if (key != propName && (key2 is null || key2 != propName)) continue;
            var result = callback((PropertyAssignment)property);
            if (Js.IsTruthy(result)) return result;
        }

        return default;
    }

    /// <summary>ported from: src/compiler/utilities.ts:3106-3112 — <c>getTsConfigObjectLiteralExpression</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is <c>TsConfigSourceFile</c> (a <c>SourceFile</c>); the port's generated
    /// <see cref="ITsConfigSourceFile"/> is not a <c>SourceFile</c> and carries no <c>statements</c>, so the port
    /// tests for the <c>SourceFile</c> the tsconfig parse produces and answers <c>null</c> otherwise.</remarks>
    public static ObjectLiteralExpression? GetTsConfigObjectLiteralExpression(ITsConfigSourceFile? tsConfigSourceFile)
    {
        if (tsConfigSourceFile is SourceFile sourceFile && sourceFile.Statements is { Count: > 0 } statements)
        {
            return Core.TryCast<ObjectLiteralExpression, Node>(((ExpressionStatement)statements[0]).Expression, NodeTests.IsObjectLiteralExpression);
        }

        return null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:3114-3120 — <c>getTsConfigPropArrayElementValue</c>.</summary>
    public static StringLiteral? GetTsConfigPropArrayElementValue(ITsConfigSourceFile? tsConfigSourceFile, string propKey, string elementValue)
        => ForEachTsConfigPropArray(tsConfigSourceFile, propKey, property => FindStringLiteralElement(property, elementValue));

    /// <summary>ported from: src/compiler/utilities.ts:3122-3124 — <c>forEachTsConfigPropArray</c>.</summary>
    public static T? ForEachTsConfigPropArray<T>(ITsConfigSourceFile? tsConfigSourceFile, string propKey, Func<PropertyAssignment, T?> callback)
        => ForEachPropertyAssignment(GetTsConfigObjectLiteralExpression(tsConfigSourceFile), propKey, callback);

    /// <summary>ported from: src/compiler/utilities.ts:12291-12293 — <c>getOptionsSyntaxByArrayElementValue</c>.</summary>
    public static StringLiteral? GetOptionsSyntaxByArrayElementValue(ObjectLiteralExpression? optionsObject, string name, string value)
        => optionsObject is not null ? GetPropertyArrayElementValue(optionsObject, name, value) : null;

    /// <summary>ported from: src/compiler/utilities.ts:12295-12301 — <c>getPropertyArrayElementValue</c>.</summary>
    private static StringLiteral? GetPropertyArrayElementValue(ObjectLiteralExpression objectLiteral, string propKey, string elementValue)
        => ForEachPropertyAssignment(objectLiteral, propKey, property => FindStringLiteralElement(property, elementValue));

    /// <summary>ported from: src/compiler/utilities.ts:3096-3102 and 12293-12299 — the array-element lookup both
    /// <c>getTsConfigPropArrayElementValue</c> and <c>getPropertyArrayElementValue</c> spell out.</summary>
    /// <remarks>PORT-NOTE: the reference writes the same four lines twice (once per function); the port keeps one
    /// private copy, because the two call sites differ only in where the property comes from.</remarks>
    private static StringLiteral? FindStringLiteralElement(PropertyAssignment property, string elementValue)
    {
        if (property.Initializer is not ArrayLiteralExpression initializer) return null;
        var element = Core.Find<Expression>(initializer.Elements, (element, _) =>
            NodeTests.IsStringLiteral(element) && ((StringLiteral)element).Text == elementValue);
        return element as StringLiteral;
    }

    /// <summary>ported from: src/compiler/utilities.ts:12303 — <c>getOptionsSyntaxByValue</c>.</summary>
    public static StringLiteral? GetOptionsSyntaxByValue(ObjectLiteralExpression? optionsObject, string name, string value)
        => ForEachOptionsSyntaxByName(optionsObject, name, property => property.Initializer is StringLiteral initializer && initializer.Text == value ? initializer : null);

    /// <summary>ported from: src/compiler/utilities.ts:12308-12310 — <c>forEachOptionsSyntaxByName</c>.</summary>
    public static T? ForEachOptionsSyntaxByName<T>(ObjectLiteralExpression? optionsObject, string name, Func<PropertyAssignment, T?> callback)
        => ForEachPropertyAssignment(optionsObject, name, callback);

    /// <summary>ported from: src/compiler/utilities.ts:12248-12289 — <c>forEachProjectReference</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference has one signature because TS lets a caller ignore a returned value; C# needs two,
    /// because a statement-bodied lambda that returns nothing only converts to a delegate with a <c>void</c> return.
    /// The <see cref="Action{T1,T2,T3}"/> overload is the "ignore the result" face and forwards to the generic one.
    /// </remarks>
    public static void ForEachProjectReference(
        List<IProjectReference>? projectReferences,
        List<IResolvedProjectReference?>? resolvedProjectReferences,
        Action<IResolvedProjectReference?, IResolvedProjectReference?, int> cbResolvedRef,
        Action<List<IProjectReference>?, IResolvedProjectReference?>? cbRef = null)
        => ForEachProjectReference<object>(
            projectReferences,
            resolvedProjectReferences,
            (resolvedRef, parent, index) =>
            {
                cbResolvedRef(resolvedRef, parent, index);
                return null;
            },
            cbRef is null
                ? null
                : (references, parent) =>
                {
                    cbRef(references, parent);
                    return null;
                });

    /// <summary>ported from: src/compiler/utilities.ts:12248-12289 — <c>forEachProjectReference</c>.</summary>
    public static T? ForEachProjectReference<T>(
        List<IProjectReference>? projectReferences,
        List<IResolvedProjectReference?>? resolvedProjectReferences,
        Func<IResolvedProjectReference?, IResolvedProjectReference?, int, T?> cbResolvedRef,
        Func<List<IProjectReference>?, IResolvedProjectReference?, T?>? cbRef = null)
        where T : class
    {
        HashSet<string>? seenResolvedRefs = null;
        return Worker(projectReferences, resolvedProjectReferences, /*parent*/ null);

        T? Worker(List<IProjectReference>? projectReferences, List<IResolvedProjectReference?>? resolvedProjectReferences, IResolvedProjectReference? parent)
        {
            // Visit project references first
            if (cbRef is not null)
            {
                var referenceResult = cbRef(projectReferences, parent);
                if (Js.IsTruthy(referenceResult)) return referenceResult;
            }

            HashSet<IResolvedProjectReference>? skipChildren = null;
            var result = Core.ForEach<IResolvedProjectReference?, T>(resolvedProjectReferences, (resolvedRef, index) =>
            {
                if (resolvedRef is not null && seenResolvedRefs?.Contains(resolvedRef.SourceFile?.Path ?? "") == true)
                {
                    (skipChildren ??= []).Add(resolvedRef);
                    // ignore recursives
                    return null;
                }

                var resolvedRefResult = cbResolvedRef(resolvedRef, parent, index);
                if (Js.IsTruthy(resolvedRefResult) || resolvedRef is null) return resolvedRefResult;
                (seenResolvedRefs ??= []).Add(resolvedRef.SourceFile?.Path ?? "");
                return null;
            });
            if (Js.IsTruthy(result)) return result;

            return Core.ForEach<IResolvedProjectReference?, T>(resolvedProjectReferences, (resolvedRef, _) =>
                resolvedRef is not null && skipChildren?.Contains(resolvedRef) != true
                    // PORT-NOTE: TS's readonly arrays are covariant, so `references` (non-null elements) widens to the
                    // worker's `(ResolvedProjectReference | undefined)[]`; C#'s IList<T> is invariant, so the widening
                    // is spelled as a copy at this one call (the recursion is the release/verify path, not a hot one).
                    ? Worker(resolvedRef.CommandLine?.ProjectReferences, resolvedRef.References is null ? null : [.. resolvedRef.References], resolvedRef)
                    : null);
        }
    }
}
