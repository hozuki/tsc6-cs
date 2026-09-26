namespace TypeScript.Compiler;

// ported from: src/compiler/program.ts:1876-1969 (the program object literal)
/// <summary>
/// The <c>Program</c> interface — TS's <c>program</c> object literal — as the class's interface implementation.
/// </summary>
/// <remarks>
/// TS's literal is a table of forwarders: each entry hands out a closure variable, a local function or a small adapter
/// over one. The port keeps the same table, but as the members of <see cref="IProgram"/> and of the host interfaces it
/// extends, because <c>Program</c> <em>is</em> the object the reference returns. The bodies live in the slices that own
/// them (<c>Program.cs</c>, <c>Program.Graph.cs</c>, <c>Program.Verify.cs</c>,
/// <c>Program.DiagnosticsAggregation.cs</c>, <c>Program.ProjectReferences.cs</c>, <c>Program.EmitBoundary.cs</c>), so
/// every member here is an <em>explicit</em> implementation forwarding to the slice's own member under TS's name.
/// <para>
/// PORT-NOTE: explicit rather than public, because the slices declare their members <c>internal</c>: a public member
/// with the same signature would be CS0111 against theirs, and <c>Program.State.cs</c>'s own <c>internal</c> members
/// are exactly the CS0737 the interface reported when a non-public member was the only candidate. The two members the
/// reference itself hands out as data (<see cref="StructureIsReused"/>, <see cref="WriteFile"/>) are public
/// properties, because the M6-1 owner and the M7 emit path read them as members.
/// </para>
/// <para>
/// PORT-NOTE (<c>object?</c> for <c>ModuleKind?</c>): TS's <c>ResolutionMode</c> is
/// <c>ModuleKind.ESNext | ModuleKind.CommonJS | undefined</c>, which the generator spells <c>object?</c> in the
/// interfaces; the port's own members spell it <see cref="ModuleKind"/> and cast at the generated boundary, as
/// <c>Program.State.cs</c> records. Each mode member below names its side of that cast.
/// </para>
/// <para>
/// PORT-NOTE (generated nullability): the generator records neither optional members nor total parameters, so a slot
/// TS's own types make total is <c>?</c> here. A read TS makes total carries <c>!</c> at the forward — the treatment
/// <c>Checker.IndexAccess.cs:52</c> records for the same generator gap — and each site below says so.
/// </para>
/// <para>
/// PORT-NOTE (<see cref="GeneratedContractException"/>): three members TS makes optional, declares on a host interface
/// and never has <c>createProgram</c> implement (<c>getModuleSpecifierCache</c> and
/// <c>getNearestAncestorDirectoryWithPackageJson</c>, <c>types.ts:9994,9997</c>, and <c>getBuildInfo</c>,
/// <c>types.ts:4934</c>). The generated interfaces require all three, so each throws a named, loud failure whose
/// message names the slot and the reason rather than returning a plausible value; the two unions
/// (<c>getFileProcessingDiagnostics</c>, <c>getFileIncludeReasons</c>) needed no such treatment — the generator maps
/// them to the port's <see cref="IFilePreprocessingDiagnostics"/>/<see cref="IFileIncludeReason"/> markers, and the
/// members below adapt at the boundary.
/// </para>
/// </remarks>
internal sealed partial class Program
{
    /// <summary>
    /// The failure a generated slot reports when it cannot express TS's missing optional member.
    /// </summary>
    /// <remarks>PORT-NOTE: the fix belongs to <c>tools/Gen</c> (record the optional member); when it lands, the member
    /// whose message named the slot gets the reference's body instead of the throw.</remarks>
    internal sealed class GeneratedContractException(string message) : NotSupportedException(message)
    {
    }

    // ---------------------------------------------------------------------------------------------
    // The object literal's entries (program.ts:1877-1956), in the reference's own order
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/program.ts:1877 — <c>getRootFileNames: () => rootNames</c>.</summary>
    List<string> IProgram.GetRootFileNames() => GetRootFileNames();

    /// <summary>ported from: src/compiler/program.ts:1878 — <c>getSourceFile</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is total (<c>getSourceFile(fileName: string)</c>), the generated slot is
    /// nullable; the forward dereferences it.</remarks>
    SourceFile? ITypeCheckerHost.GetSourceFile(string? fileName) => GetSourceFile(fileName!);

    /// <summary>ported from: src/compiler/program.ts:1878 — <c>getSourceFile</c>, the
    /// <see cref="IScriptReferenceHost"/> half of the same slot.</summary>
    /// <remarks>PORT-NOTE: nullable generated parameter, total in TS; see the <see cref="ITypeCheckerHost"/> half.</remarks>
    SourceFile? IScriptReferenceHost.GetSourceFile(string? fileName) => GetSourceFile(fileName!);

    /// <summary>ported from: src/compiler/program.ts:1879 — <c>getSourceFileByPath</c>.</summary>
    /// <remarks>PORT-NOTE: nullable generated parameter, total in TS (<c>getSourceFileByPath(path: Path)</c>).</remarks>
    SourceFile? IScriptReferenceHost.GetSourceFileByPath(string? path) => GetSourceFileByPath(path!);

    /// <summary>ported from: src/compiler/program.ts:1880 — <c>getSourceFiles: () => files</c>.</summary>
    List<SourceFile> ITypeCheckerHost.GetSourceFiles() => GetSourceFiles();

    /// <summary>ported from: src/compiler/program.ts:1881 — <c>getMissingFilePaths: () => missingFileNames</c>.</summary>
    Dictionary<string, string> IProgram.GetMissingFilePaths() => GetMissingFilePaths();

    /// <summary>ported from: src/compiler/program.ts:1882 — <c>getModuleResolutionCache: () => moduleResolutionCache</c>.</summary>
    /// <remarks>PORT-NOTE: the generated slot is <c>object?</c> (the generator has no type for
    /// <c>ModuleResolutionCache</c>); the field is the port's <see cref="IModuleResolutionCache"/> and is boxed here.</remarks>
    object? IModuleSpecifierResolutionHost.GetModuleResolutionCache() => _moduleResolutionCache;

    /// <summary>ported from: src/compiler/program.ts:1883 — <c>getFilesByNameMap: () => filesByName</c>.</summary>
    /// <remarks>PORT-NOTE: TS hands out its live <c>Map&lt;Path, SourceFile | false | undefined&gt;</c>; the port's map
    /// holds three-state <see cref="FileByNameEntry"/> wrappers, so the generated <c>Dictionary&lt;string, object?&gt;</c>
    /// is filled with a projection — the <see cref="SourceFile"/> for <c>File</c>, boxed <c>false</c> for
    /// <c>Missing</c> (TS's <c>false</c>) and <c>null</c> for <c>Unset</c> (TS's <c>undefined</c>). The dictionary is
    /// built fresh, so a caller that keeps it keeps a snapshot rather than the live map.</remarks>
    Dictionary<string, object?> IProgram.GetFilesByNameMap()
    {
        var result = new Dictionary<string, object?>(_filesByName.Count);
        foreach (var (path, entry) in _filesByName)
        {
            result[path] = entry.State switch
            {
                FileByNameState.File => entry.File,
                FileByNameState.Missing => false,
                _ => null,
            };
        }

        return result;
    }

    /// <summary>ported from: src/compiler/program.ts:1884 — <c>getCompilerOptions: () => options</c>.</summary>
    /// <remarks>PORT-NOTE: the generated <c>IProgram</c> inherits this slot from two bases
    /// (<see cref="ISourceFileMayBeEmittedHost"/> and <see cref="IScriptReferenceHost"/>), so an interface-typed read
    /// has to name the base it means — the reason <c>Program.GetCompilerOptionsOf</c> exists — while
    /// <see cref="Program.GetCompilerOptions"/>, the slot's one body, is what a <c>Program</c>-typed read reaches.</remarks>
    ICompilerOptions ISourceFileMayBeEmittedHost.GetCompilerOptions() => GetCompilerOptions();

    /// <summary>ported from: src/compiler/program.ts:1884 — <c>getCompilerOptions: () => options</c>, the
    /// <see cref="IScriptReferenceHost"/> half of the same slot.</summary>
    ICompilerOptions IScriptReferenceHost.GetCompilerOptions() => GetCompilerOptions();

    /// <summary>ported from: src/compiler/program.ts:1885 — <c>getSyntacticDiagnostics</c>.</summary>
    List<IDiagnosticWithLocation> IProgram.GetSyntacticDiagnostics(SourceFile? sourceFile, ICancellationToken? cancellationToken)
        => GetSyntacticDiagnostics(sourceFile, cancellationToken);

    /// <summary>ported from: src/compiler/program.ts:1886 — <c>getOptionsDiagnostics</c>.</summary>
    /// <remarks>PORT-NOTE: TS's own function takes no token (<c>program.ts:3267-3273</c>) while the interface the
    /// literal satisfies declares one; the port's member keeps the interface's parameter and the token is passed, which
    /// is what a call through the interface does in the reference too (JS ignores the extra argument).</remarks>
    List<IDiagnostic> IProgram.GetOptionsDiagnostics(ICancellationToken? cancellationToken) => GetOptionsDiagnostics(cancellationToken);

    /// <summary>ported from: src/compiler/program.ts:1887 — <c>getGlobalDiagnostics</c>.</summary>
    /// <remarks>PORT-NOTE: TS's own function takes no token (<c>program.ts:3283-3286</c>); the port's member keeps the
    /// interface's parameter and the token is passed, as <see cref="IProgram.GetOptionsDiagnostics"/> records.</remarks>
    List<IDiagnostic> IProgram.GetGlobalDiagnostics(ICancellationToken? cancellationToken) => GetGlobalDiagnostics(cancellationToken);

    /// <summary>ported from: src/compiler/program.ts:1888 — <c>getSemanticDiagnostics</c>.</summary>
    List<IDiagnostic> IProgram.GetSemanticDiagnostics(SourceFile? sourceFile, ICancellationToken? cancellationToken)
        => GetSemanticDiagnostics(sourceFile, cancellationToken);

    /// <summary>ported from: src/compiler/program.ts:1889 — <c>getCachedSemanticDiagnostics</c>.</summary>
    List<IDiagnostic>? IProgram.GetCachedSemanticDiagnostics(SourceFile? sourceFile)
        => GetCachedSemanticDiagnostics(sourceFile);

    /// <summary>ported from: src/compiler/program.ts:1890 — <c>getSuggestionDiagnostics</c>.</summary>
    List<IDiagnosticWithLocation> IProgram.GetSuggestionDiagnostics(SourceFile? sourceFile, ICancellationToken? cancellationToken)
        => GetSuggestionDiagnostics(sourceFile, cancellationToken);

    /// <summary>ported from: src/compiler/program.ts:1891 — <c>getDeclarationDiagnostics</c>.</summary>
    /// <remarks>PORT-NOTE: the M7 boundary; the forward reaches <c>Program.EmitBoundary.cs</c>, which throws
    /// <c>EmitNotLanded</c> until the emitter lands.</remarks>
    List<IDiagnosticWithLocation> IProgram.GetDeclarationDiagnostics(SourceFile? sourceFile, ICancellationToken? cancellationToken)
        => GetDeclarationDiagnostics(sourceFile, cancellationToken);

    /// <summary>ported from: src/compiler/program.ts:1892 — <c>getBindAndCheckDiagnostics</c>.</summary>
    List<IDiagnostic> IProgram.GetBindAndCheckDiagnostics(SourceFile? sourceFile, ICancellationToken? cancellationToken)
        => GetBindAndCheckDiagnostics(sourceFile, cancellationToken);

    /// <summary>ported from: src/compiler/program.ts:1893 — <c>getProgramDiagnostics</c>.</summary>
    List<IDiagnostic> IProgram.GetProgramDiagnostics(SourceFile? sourceFile, ICancellationToken? cancellationToken)
        => GetProgramDiagnostics(sourceFile, cancellationToken);

    /// <summary>ported from: src/compiler/program.ts:1894 — <c>getTypeChecker</c>.</summary>
    ITypeChecker IProgram.GetTypeChecker() => GetTypeChecker();

    /// <summary>ported from: src/compiler/program.ts:1895 — <c>getClassifiableNames</c>.</summary>
    HashSet<string> IProgram.GetClassifiableNames() => GetClassifiableNames();

    /// <summary>ported from: src/compiler/program.ts:1896 — <c>getCommonSourceDirectory</c>.</summary>
    string IModuleSpecifierResolutionHost.GetCommonSourceDirectory() => GetCommonSourceDirectory();

    /// <summary>ported from: src/compiler/program.ts:1897 — <c>emit</c>.</summary>
    /// <remarks>PORT-NOTE: the M7 boundary; the forward reaches <c>Program.EmitBoundary.cs</c>, which throws
    /// <c>EmitNotLanded</c> until the emitter lands.</remarks>
    IEmitResult IProgram.Emit(
        SourceFile? targetSourceFile,
        Action<string, string, bool, Action<string>, List<SourceFile>, IWriteFileCallbackData, object?>? writeFile,
        ICancellationToken? cancellationToken,
        bool? emitOnlyDtsFiles,
        ICustomTransformers? customTransformers)
        => Emit(targetSourceFile, writeFile, cancellationToken, emitOnlyDtsFiles, customTransformers);

    /// <summary>ported from: src/compiler/program.ts:1898 — <c>getCurrentDirectory: () => currentDirectory</c>.</summary>
    string IModuleSpecifierResolutionHost.GetCurrentDirectory() => _currentDirectory;

    /// <summary>ported from: src/compiler/program.ts:1898 — <c>getCurrentDirectory: () => currentDirectory</c>, the
    /// <see cref="ISourceFileMayBeEmittedHost"/> half of the same slot.</summary>
    string ISourceFileMayBeEmittedHost.GetCurrentDirectory() => _currentDirectory;

    /// <summary>ported from: src/compiler/program.ts:1898 — <c>getCurrentDirectory: () => currentDirectory</c>, the
    /// <see cref="IScriptReferenceHost"/> half of the same slot.</summary>
    string IScriptReferenceHost.GetCurrentDirectory() => _currentDirectory;

    /// <summary>ported from: src/compiler/program.ts:1899 — <c>getNodeCount: () => getTypeChecker().getNodeCount()</c>.</summary>
    int IProgram.GetNodeCount() => GetTypeChecker().GetNodeCount();

    /// <summary>ported from: src/compiler/program.ts:1900 —
    /// <c>getIdentifierCount: () => getTypeChecker().getIdentifierCount()</c>.</summary>
    int IProgram.GetIdentifierCount() => GetTypeChecker().GetIdentifierCount();

    /// <summary>ported from: src/compiler/program.ts:1901 — <c>getSymbolCount: () => getTypeChecker().getSymbolCount()</c>.</summary>
    int IProgram.GetSymbolCount() => GetTypeChecker().GetSymbolCount();

    /// <summary>ported from: src/compiler/program.ts:1902 — <c>getTypeCount: () => getTypeChecker().getTypeCount()</c>.</summary>
    int IProgram.GetTypeCount() => GetTypeChecker().GetTypeCount();

    /// <summary>ported from: src/compiler/program.ts:1903 —
    /// <c>getInstantiationCount: () => getTypeChecker().getInstantiationCount()</c>.</summary>
    int IProgram.GetInstantiationCount() => GetTypeChecker().GetInstantiationCount();

    /// <summary>ported from: src/compiler/program.ts:1904 —
    /// <c>getRelationCacheSizes: () => getTypeChecker().getRelationCacheSizes()</c>.</summary>
    /// <remarks>PORT-NOTE: TS's anonymous <c>{ assignable; identity; subtype; strictSubtype }</c> object has no C#
    /// counterpart in the generated model, so the slot stays <c>object?</c> on both sides (the generated
    /// <c>ITypeChecker.GetRelationCacheSizes()</c> declares it the same way).</remarks>
    object? IProgram.GetRelationCacheSizes() => GetTypeChecker().GetRelationCacheSizes();

    /// <summary>ported from: src/compiler/program.ts:1905 —
    /// <c>getFileProcessingDiagnostics: () => programDiagnostics.getFileProcessingDiagnostics()</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>FilePreprocessingDiagnostics[]</c> is a union whose arms are not nodes; the generator
    /// maps it to the port's marker interface <see cref="IFilePreprocessingDiagnostics"/>
    /// (<c>Program.ProgramDiagnostics.cs</c>), which is the type <c>ProgramDiagnostics.GetFileProcessingDiagnostics()</c>
    /// answers, so this is a direct forward and the list is the live one.</remarks>
    List<IFilePreprocessingDiagnostics>? IProgram.GetFileProcessingDiagnostics()
        => _programDiagnostics.GetFileProcessingDiagnostics();

    /// <summary>ported from: src/compiler/program.ts:1906 — <c>getAutomaticTypeDirectiveNames: () => automaticTypeDirectiveNames!</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>!</c> is a type assertion, so the value is <c>undefined</c> until the file graph sets
    /// it; the port's <c>!</c> is the same assertion over the nullable field.</remarks>
    List<string> IProgram.GetAutomaticTypeDirectiveNames() => _automaticTypeDirectiveNames!;

    /// <summary>ported from: src/compiler/program.ts:1907 —
    /// <c>getAutomaticTypeDirectiveResolutions: () => automaticTypeDirectiveResolutions</c>.</summary>
    ModeAwareCache<IResolvedTypeReferenceDirectiveWithFailedLookupLocations> IProgram.GetAutomaticTypeDirectiveResolutions()
        => _automaticTypeDirectiveResolutions;

    /// <summary>ported from: src/compiler/program.ts:1908 — <c>isSourceFileFromExternalLibrary</c>.</summary>
    /// <remarks>PORT-NOTE: nullable generated parameter, total in TS; the forward dereferences it.</remarks>
    bool ISourceFileMayBeEmittedHost.IsSourceFileFromExternalLibrary(SourceFile? file) => IsSourceFileFromExternalLibrary(file!);

    /// <summary>ported from: src/compiler/program.ts:1909 — <c>isSourceFileDefaultLibrary</c>.</summary>
    /// <remarks>PORT-NOTE: nullable generated parameter, total in TS; the forward dereferences it.</remarks>
    bool ITypeCheckerHost.IsSourceFileDefaultLibrary(SourceFile? file) => IsSourceFileDefaultLibrary(file!);

    /// <summary>ported from: src/compiler/program.ts:1910 — <c>getModeForUsageLocation</c>.</summary>
    /// <remarks>PORT-NOTE: the port's member answers <c>ModuleKind?</c> (TS's <c>ResolutionMode</c>) and the generated
    /// slot is <c>object?</c>, so the value is boxed; the nullable parameters are TS-total and are dereferenced.</remarks>
    object? ITypeCheckerHost.GetModeForUsageLocation(SourceFile? file, LiteralExpression? usage)
        => GetModeForUsageLocation(file!, usage!);

    /// <summary>ported from: src/compiler/program.ts:1911 — <c>getEmitSyntaxForUsageLocation</c>.</summary>
    /// <remarks>PORT-NOTE: <c>ModuleKind?</c> boxed into the generated <c>object?</c> slot, as above.</remarks>
    object? ITypeCheckerHost.GetEmitSyntaxForUsageLocation(SourceFile? file, LiteralExpression? usage)
        => GetEmitSyntaxForUsageLocation(file!, usage!);

    /// <summary>ported from: src/compiler/program.ts:1912 — <c>getModeForResolutionAtIndex</c>.</summary>
    /// <remarks>PORT-NOTE: <c>ModuleKind?</c> boxed into the generated <c>object?</c> slot, as above.</remarks>
    object? IModuleSpecifierResolutionHost.GetModeForResolutionAtIndex(SourceFile? file, int index)
        => GetModeForResolutionAtIndex(file!, index);

    /// <summary>ported from: src/compiler/program.ts:1913 — <c>getSourceFileFromReference</c>.</summary>
    /// <remarks>PORT-NOTE: both generated parameters are nullable where TS's are total
    /// (<c>getSourceFileFromReference(referencingFile: SourceFile, ref: FileReference)</c>); the forward dereferences
    /// both.</remarks>
    SourceFile? IProgram.GetSourceFileFromReference(SourceFile? referencingFile, IFileReference? @ref)
        => GetSourceFileFromReference(referencingFile!, @ref!);

    /// <summary>ported from: src/compiler/program.ts:1914 — <c>getLibFileFromReference</c>.</summary>
    /// <remarks>PORT-NOTE: nullable generated parameter, total in TS; the forward dereferences it.</remarks>
    SourceFile? IProgram.GetLibFileFromReference(IFileReference? @ref) => GetLibFileFromReference(@ref!);

    /// <summary>ported from: src/compiler/program.ts:1915 — <c>sourceFileToPackageName</c>.</summary>
    Dictionary<string, string>? IProgram.SourceFileToPackageName
    {
        get => _sourceFileToPackageName;
        set => _sourceFileToPackageName = value!;
    }

    /// <summary>ported from: src/compiler/program.ts:1916 — <c>redirectTargetsMap</c>.</summary>
    /// <remarks>PORT-NOTE: TS hands out its live <c>MultiMap&lt;Path, string&gt;</c>; the generated slot is a
    /// <c>Dictionary&lt;string, List&lt;string&gt;&gt;</c>, so this projects the multimap into one, sharing the
    /// per-key value lists (a mutation of a list is visible to the caller, an added key is not).</remarks>
    Dictionary<string, List<string>>? IModuleSpecifierResolutionHost.RedirectTargetsMap
        => _redirectTargetsMap.Keys.ToDictionary(key => key, key => _redirectTargetsMap.Get(key)!);

    /// <summary>ported from: src/compiler/program.ts:1917 — <c>usesUriStyleNodeCoreModules</c>.</summary>
    bool? IProgram.UsesUriStyleNodeCoreModules => _usesUriStyleNodeCoreModules;

    /// <summary>ported from: src/compiler/program.ts:1918 — <c>resolvedModules</c>.</summary>
    Dictionary<string, ModeAwareCache<IResolvedModuleWithFailedLookupLocations>>? IProgram.ResolvedModules
    {
        get => _resolvedModules;
        set => _resolvedModules = value;
    }

    /// <summary>ported from: src/compiler/program.ts:1919 — <c>resolvedTypeReferenceDirectiveNames</c>.</summary>
    Dictionary<string, ModeAwareCache<IResolvedTypeReferenceDirectiveWithFailedLookupLocations>>? IProgram.ResolvedTypeReferenceDirectiveNames
    {
        get => _resolvedTypeReferenceDirectiveNames;
        set => _resolvedTypeReferenceDirectiveNames = value;
    }

    /// <summary>ported from: src/compiler/program.ts:1920 — <c>resolvedLibReferences</c>.</summary>
    Dictionary<string, ILibResolution<object?>>? IProgram.ResolvedLibReferences
    {
        get => _resolvedLibReferences;
        set => _resolvedLibReferences = value;
    }

    /// <summary>ported from: src/compiler/program.ts:1921 — <c>getProgramDiagnosticsContainer: () => programDiagnostics</c>.</summary>
    /// <remarks>PORT-NOTE: TS's slot holds a function returning the diagnostics container
    /// (<c>getProgramDiagnosticsContainer: () => ProgramDiagnostics</c>, <c>types.ts:4910</c>), and its one reader
    /// (<c>program.ts:2580</c>) calls it. The generated slot is <c>Func&lt;object?&gt;?</c>, so the port builds the same
    /// delegate over <see cref="_programDiagnostics"/> and caches it — the reference creates one closure per program.
    /// The setter exists because TS's slot is assignable; nothing in the reference assigns it.</remarks>
    Func<object?>? IProgram.GetProgramDiagnosticsContainer
    {
        get => _programDiagnosticsContainer ??= () => _programDiagnostics;
        set => _programDiagnosticsContainer = value;
    }

    private Func<object?>? _programDiagnosticsContainer;

    /// <summary>ported from: src/compiler/program.ts:1922 — <c>getResolvedModule</c>.</summary>
    /// <remarks>PORT-NOTE: the generated mode is <c>object?</c> and the port's member takes <see cref="ModuleKind"/>;
    /// the box the interface hands over is the only value the port ever stores there, so the unbox is unconditional.
    /// The two generated parameters TS makes total are dereferenced.</remarks>
    IResolvedModuleWithFailedLookupLocations? ITypeCheckerHost.GetResolvedModule(SourceFile? f, string? moduleName, object? mode)
        => GetResolvedModule(f!, moduleName!, (ModuleKind?)mode);

    /// <summary>ported from: src/compiler/program.ts:1923 — <c>getResolvedModuleFromModuleSpecifier</c>.</summary>
    /// <remarks>PORT-NOTE: the generated module specifier is nullable where TS's is total; the forward dereferences it,
    /// and the internal member keeps TS's own <c>sourceFile?</c> default.</remarks>
    IResolvedModuleWithFailedLookupLocations? IProgram.GetResolvedModuleFromModuleSpecifier(LiteralExpression? moduleSpecifier, SourceFile? sourceFile)
        => GetResolvedModuleFromModuleSpecifier(moduleSpecifier!, sourceFile);

    /// <summary>ported from: src/compiler/program.ts:1924 — <c>getResolvedTypeReferenceDirective</c>.</summary>
    /// <remarks>PORT-NOTE: <c>object?</c> → <see cref="ModuleKind"/> unbox and TS-total parameters, exactly as
    /// <see cref="ITypeCheckerHost.GetResolvedModule"/> records.</remarks>
    IResolvedTypeReferenceDirectiveWithFailedLookupLocations? IProgram.GetResolvedTypeReferenceDirective(SourceFile? f, string? typeDirectiveName, object? mode)
        => GetResolvedTypeReferenceDirective(f!, typeDirectiveName!, (ModuleKind?)mode);

    /// <summary>ported from: src/compiler/program.ts:1925 — <c>getResolvedTypeReferenceDirectiveFromTypeReferenceDirective</c>.</summary>
    /// <remarks>PORT-NOTE: both generated parameters are nullable where TS's are total; the forward dereferences
    /// both.</remarks>
    IResolvedTypeReferenceDirectiveWithFailedLookupLocations? IProgram.GetResolvedTypeReferenceDirectiveFromTypeReferenceDirective(IFileReference? typedRef, SourceFile? sourceFile)
        => GetResolvedTypeReferenceDirectiveFromTypeReferenceDirective(typedRef!, sourceFile!);

    /// <summary>ported from: src/compiler/program.ts:1926 — <c>forEachResolvedModule</c>.</summary>
    /// <remarks>PORT-NOTE: the interface's callback takes the mode as <c>object?</c> and the port's member takes
    /// <see cref="ModuleKind"/>, so the adapter boxes the port's mode on the way out. The callback is nullable in the
    /// generated slot and total in TS's, so the dereference is TS's own.</remarks>
    void IProgram.ForEachResolvedModule(
        Action<IResolvedModuleWithFailedLookupLocations, string, object?, string>? callback,
        SourceFile? file)
        => ForEachResolvedModule((resolution, moduleName, mode, filePath) => callback!(resolution, moduleName, mode, filePath), file);

    /// <summary>ported from: src/compiler/program.ts:1927 — <c>forEachResolvedTypeReferenceDirective</c>.</summary>
    /// <remarks>PORT-NOTE: <c>ModuleKind?</c> → <c>object?</c> boxing and the nullable generated callback, exactly as
    /// <see cref="IProgram.ForEachResolvedModule"/> records.</remarks>
    void IProgram.ForEachResolvedTypeReferenceDirective(
        Action<IResolvedTypeReferenceDirectiveWithFailedLookupLocations, string, object?, string>? callback,
        SourceFile? file)
        => ForEachResolvedTypeReferenceDirective((resolution, moduleName, mode, filePath) => callback!(resolution, moduleName, mode, filePath), file);

    /// <summary>ported from: src/compiler/program.ts:1928 — <c>getCurrentPackagesMap: () => packageMap</c>.</summary>
    /// <remarks>PORT-NOTE: TS reads the memo slot itself (<c>packageMap</c> is <c>undefined</c> until
    /// <c>getPackagesMap</c> has run), and the port's <c>GetPackagesMap()</c> computes it — the slot is read directly
    /// so the two agree.</remarks>
    Dictionary<string, bool>? IProgram.GetCurrentPackagesMap() => _packageMap;

    /// <summary>ported from: src/compiler/program.ts:1929 — <c>typesPackageExists</c>.</summary>
    /// <remarks>PORT-NOTE: nullable generated parameter, total in TS; the forward dereferences it.</remarks>
    bool ITypeCheckerHost.TypesPackageExists(string? packageName) => TypesPackageExists(packageName!);

    /// <summary>ported from: src/compiler/program.ts:1930 — <c>packageBundlesTypes</c>.</summary>
    /// <remarks>PORT-NOTE: nullable generated parameter, total in TS; the forward dereferences it.</remarks>
    bool ITypeCheckerHost.PackageBundlesTypes(string? packageName) => PackageBundlesTypes(packageName!);

    /// <summary>ported from: src/compiler/program.ts:1931 — <c>isEmittedFile</c>.</summary>
    /// <remarks>PORT-NOTE: nullable generated parameter, total in TS (<c>isEmittedFile(file: string)</c>); the forward
    /// dereferences it.</remarks>
    bool IProgram.IsEmittedFile(string? file) => IsEmittedFile(file!);

    /// <summary>ported from: src/compiler/program.ts:1932 — <c>getConfigFileParsingDiagnostics</c>.</summary>
    List<IDiagnostic> IProgram.GetConfigFileParsingDiagnostics() => GetConfigFileParsingDiagnostics();

    /// <summary>ported from: src/compiler/program.ts:1933 — <c>getProjectReferences</c>.</summary>
    List<IProjectReference>? IProgram.GetProjectReferences() => GetProjectReferences();

    /// <summary>ported from: src/compiler/program.ts:1934 — <c>getResolvedProjectReferences</c>.</summary>
    /// <remarks>PORT-NOTE: TS's element type is <c>ResolvedProjectReference | undefined</c> — the reference's own reuse
    /// walk leaves holes in this array — and the generated slot drops the element's <c>| undefined</c>, so the member
    /// the port declares for the slot (<see cref="Program.GetResolvedProjectReferences"/>, which keeps TS's element
    /// type) is cast through <c>object</c> (the two spellings are the same runtime type, the treatment
    /// <c>NodeClasses.cs</c> gives a generated slot it has to widen). The holes stay reachable to a caller that indexes
    /// the list, exactly as in the reference.</remarks>
    List<IResolvedProjectReference>? IProgram.GetResolvedProjectReferences()
        => (List<IResolvedProjectReference>?)(object?)GetResolvedProjectReferences();

    /// <summary>ported from: src/compiler/program.ts:1935 — <c>getRedirectFromSourceFile</c>.</summary>
    /// <remarks>PORT-NOTE: nullable generated parameter, total in TS; the forward dereferences it.</remarks>
    IResolvedRefAndOutputDts? IModuleSpecifierResolutionHost.GetRedirectFromSourceFile(string? fileName)
        => GetRedirectFromSourceFile(fileName!);

    /// <summary>ported from: src/compiler/program.ts:1935 — <c>getRedirectFromSourceFile</c>, the
    /// <see cref="ISourceFileMayBeEmittedHost"/> half of the same slot.</summary>
    IResolvedRefAndOutputDts? ISourceFileMayBeEmittedHost.GetRedirectFromSourceFile(string? fileName)
        => GetRedirectFromSourceFile(fileName!);

    /// <summary>ported from: src/compiler/program.ts:1936 — <c>getResolvedProjectReferenceByPath</c>.</summary>
    /// <remarks>PORT-NOTE: nullable generated parameter, total in TS; the forward dereferences it.</remarks>
    IResolvedProjectReference? IProgram.GetResolvedProjectReferenceByPath(string? projectReferencePath)
        => GetResolvedProjectReferenceByPath(projectReferencePath!);

    /// <summary>ported from: src/compiler/program.ts:1937 — <c>forEachResolvedProjectReference</c>.</summary>
    /// <remarks>PORT-NOTE: the generated callback answers <c>T</c> where TS's (and the port's member) answers
    /// <c>T | undefined</c>, and the generated callback is nullable where TS's is total, so the forward is the null
    /// assertion TS's own dereference is. The <c>default</c> constraint is the C# spelling that keeps the generated
    /// <c>T?</c> return as TS's <c>T | undefined</c> instead of <c>Nullable&lt;T&gt;</c>.</remarks>
    T? IProgram.ForEachResolvedProjectReference<T>(Func<IResolvedProjectReference, T>? cb) where T : default
        => ForEachResolvedProjectReference(cb!);

    /// <summary>ported from: src/compiler/program.ts:1938 — <c>isSourceOfProjectReferenceRedirect</c>.</summary>
    /// <remarks>PORT-NOTE: nullable generated parameter, total in TS; the forward dereferences it.</remarks>
    bool IModuleSpecifierResolutionHost.IsSourceOfProjectReferenceRedirect(string? fileName)
        => IsSourceOfProjectReferenceRedirect(fileName!);

    /// <summary>ported from: src/compiler/program.ts:1938 — <c>isSourceOfProjectReferenceRedirect</c>, the
    /// <see cref="ISourceFileMayBeEmittedHost"/> half of the same slot.</summary>
    bool ISourceFileMayBeEmittedHost.IsSourceOfProjectReferenceRedirect(string? fileName)
        => IsSourceOfProjectReferenceRedirect(fileName!);

    /// <summary>ported from: src/compiler/program.ts:1939 — <c>getRedirectFromOutput</c>.</summary>
    /// <remarks>PORT-NOTE: nullable generated parameter, total in TS (<c>getRedirectFromOutput(path: Path)</c>); the
    /// forward dereferences it.</remarks>
    IResolvedRefAndSource? ITypeCheckerHost.GetRedirectFromOutput(string? filePath) => GetRedirectFromOutput(filePath!);

    /// <summary>ported from: src/compiler/program.ts:1940 — <c>getCompilerOptionsForFile</c>.</summary>
    /// <remarks>PORT-NOTE: nullable generated parameter, total in TS; the forward dereferences it.</remarks>
    ICompilerOptions IProgram.GetCompilerOptionsForFile(SourceFile? file) => GetCompilerOptionsForFile(file!);

    /// <summary>ported from: src/compiler/program.ts:1941 — <c>getDefaultResolutionModeForFile</c>.</summary>
    /// <remarks>PORT-NOTE: <c>ModuleKind?</c> boxed into the generated <c>object?</c> slot; nullable generated
    /// parameter, total in TS.</remarks>
    object? IModuleSpecifierResolutionHost.GetDefaultResolutionModeForFile(SourceFile? sourceFile)
        => GetDefaultResolutionModeForFile(sourceFile!);

    /// <summary>ported from: src/compiler/program.ts:1942 — <c>getEmitModuleFormatOfFile</c>.</summary>
    /// <remarks>PORT-NOTE: TS's answer is a <c>ModuleKind</c>, which the generated slot keeps; the nullable generated
    /// parameter is TS-total and is dereferenced.</remarks>
    ModuleKind ITypeCheckerHost.GetEmitModuleFormatOfFile(SourceFile? sourceFile)
        => GetEmitModuleFormatOfFile(sourceFile!);

    /// <summary>ported from: src/compiler/program.ts:1943 — <c>getImpliedNodeFormatForEmit</c>.</summary>
    /// <remarks>PORT-NOTE: <c>ModuleKind?</c> boxed into the generated <c>object?</c> slot; nullable generated
    /// parameter, total in TS.</remarks>
    object? ITypeCheckerHost.GetImpliedNodeFormatForEmit(SourceFile? sourceFile)
        => GetImpliedNodeFormatForEmit(sourceFile!);

    /// <summary>ported from: src/compiler/program.ts:1944 — <c>shouldTransformImportCall</c>.</summary>
    /// <remarks>PORT-NOTE: nullable generated parameter, total in TS; the forward dereferences it.</remarks>
    bool IProgram.ShouldTransformImportCall(SourceFile? sourceFile) => ShouldTransformImportCall(sourceFile!);

    /// <summary>ported from: src/compiler/program.ts:1945 — <c>emitBuildInfo</c>.</summary>
    /// <remarks>PORT-NOTE: the M7 boundary; the forward reaches <c>Program.EmitBoundary.cs</c>, which throws
    /// <c>EmitNotLanded</c> until the emitter lands.</remarks>
    IEmitResult IProgram.EmitBuildInfo(
        Action<string, string, bool, Action<string>, List<SourceFile>, IWriteFileCallbackData, object?>? writeFile,
        ICancellationToken? cancellationToken)
        => EmitBuildInfo(writeFile, cancellationToken);

    /// <summary>ported from: src/compiler/program.ts:1946 — <c>fileExists</c>.</summary>
    /// <remarks>PORT-NOTE: the port's <c>_fileExists</c> is the redirect-aware wrapper the literal hands out
    /// (<c>program.ts:4964-4971</c>); the generated parameter is nullable where TS's is total and is dereferenced.</remarks>
    bool IModuleSpecifierResolutionHost.FileExists(string? path) => _fileExists(path!);

    /// <summary>ported from: src/compiler/program.ts:1947 — <c>readFile</c>.</summary>
    /// <remarks>PORT-NOTE: nullable generated parameter, total in TS; the forward dereferences it.</remarks>
    string? IModuleSpecifierResolutionHost.ReadFile(string? path) => _readFile(path!);

    /// <summary>ported from: src/compiler/program.ts:1948 — <c>directoryExists</c>.</summary>
    /// <remarks>PORT-NOTE: nullable generated parameter, total in TS; the forward dereferences it.</remarks>
    bool IModuleSpecifierResolutionHost.DirectoryExists(string? path) => _directoryExists(path!);

    /// <summary>ported from: src/compiler/program.ts:1949 — <c>getSymlinkCache</c>.</summary>
    /// <remarks>PORT-NOTE: the generated slot is <c>object?</c> because <c>SymlinkCache</c> is declared outside the
    /// extracted sources; the slice's member is the wrapper the reference's local function builds.</remarks>
    object? IModuleSpecifierResolutionHost.GetSymlinkCache() => GetSymlinkCache();

    /// <summary>ported from: src/compiler/program.ts:1950 — <c>realpath: host.realpath?.bind(host)</c>.</summary>
    /// <remarks>PORT-NOTE: TS binds the host's <c>realpath</c> when it has one and answers <c>undefined</c> otherwise;
    /// the generated slot is a required method, so the port asks the host — the ruling
    /// <c>Utilities.DirectoryProbablyExists</c> records for a dropped optional host member.</remarks>
    string IModuleSpecifierResolutionHost.Realpath(string? path) => _host.Realpath(path);

    /// <summary>ported from: src/compiler/program.ts:1951 — <c>useCaseSensitiveFileNames: () => host.useCaseSensitiveFileNames()</c>.</summary>
    /// <remarks>PORT-NOTE: the generated <c>IModuleResolutionHost</c> types the host's slot <c>object?</c> (TS's is a
    /// boolean or a function, <c>utilities.ts:6483</c>), so the call goes through
    /// <c>Utilities.HostUsesCaseSensitiveFileNames</c>, which reads both arms.</remarks>
    bool IModuleSpecifierResolutionHost.UseCaseSensitiveFileNames()
        => Utilities.HostUsesCaseSensitiveFileNames(_host);

    /// <summary>ported from: src/compiler/program.ts:1951 — <c>useCaseSensitiveFileNames: () => host.useCaseSensitiveFileNames()</c>,
    /// the <see cref="ISourceFileMayBeEmittedHost"/> half of the same slot.</summary>
    bool ISourceFileMayBeEmittedHost.UseCaseSensitiveFileNames()
        => Utilities.HostUsesCaseSensitiveFileNames(_host);

    /// <summary>ported from: src/compiler/program.ts:1952 — <c>getCanonicalFileName</c>.</summary>
    /// <remarks>PORT-NOTE: the generated slot is <c>object?</c> because <c>GetCanonicalFileName</c> is declared outside
    /// the extracted sources; TS's slot holds the function itself (<c>types.ts:4936</c>), which the port stores in
    /// <c>Program.State.cs</c>'s <c>_getCanonicalFileName</c> field (the canonicalizer every other member calls). TS's
    /// property is settable and nothing in the reference assigns it; the setter stores the same delegate type.</remarks>
    object? ISourceFileMayBeEmittedHost.GetCanonicalFileName
    {
        get => _getCanonicalFileName;
        set => _getCanonicalFileName = (Core.GetCanonicalFileName)value!;
    }

    /// <summary>ported from: src/compiler/program.ts:1953 — <c>getFileIncludeReasons: () => programDiagnostics.getFileReasons()</c>.</summary>
    /// <remarks>PORT-NOTE: TS hands out its live <c>MultiMap&lt;Path, FileIncludeReason&gt;</c>; the generated slot is
    /// the container mapping the generator settled on (<c>Dictionary&lt;string, List&lt;IFileIncludeReason&gt;&gt;</c>,
    /// the port's <see cref="IFileIncludeReason"/> marker standing in for the union), so the multimap is projected into
    /// one here, sharing the per-key value lists (a mutation of a list is visible to the caller, an added key is not).</remarks>
    Dictionary<string, List<IFileIncludeReason>> IModuleSpecifierResolutionHost.GetFileIncludeReasons()
    {
        var reasons = _programDiagnostics.GetFileReasons();
        return reasons.Keys.ToDictionary(key => key, key => reasons.Get(key)!);
    }

    /// <summary>ported from: src/compiler/program.ts:1954 — <c>structureIsReused</c>.</summary>
    /// <remarks>PORT-NOTE: the port's slot is <c>StructureIsReused?</c> so TS's third state (<c>undefined</c>, before
    /// <c>tryReuseStructureFromOldProgram</c> has run) stays representable; the interface's slot is total and answers
    /// the absent arm as <see cref="StructureIsReused.Not"/>, the value the reference starts from. The property is
    /// public because the M6-1 owner and the builder tests read it as a member.</remarks>
    public StructureIsReused StructureIsReused => _structureIsReused ?? TypeScript.Compiler.StructureIsReused.Not;

    /// <summary>ported from: src/compiler/program.ts:1955 — <c>writeFile</c>.</summary>
    /// <remarks>PORT-NOTE: TS's slot is the local <c>writeFile</c> wrapper (<c>program.ts:2639-2648</c>) and the
    /// literal hands it out for the builder to replace; the port keeps the same settable storage — the M7 emit path in
    /// <c>Program.EmitBoundary.cs</c> reads it — so the slot is one public auto-property rather than a forwarder.</remarks>
    public Action<string, string, bool, Action<string>, List<SourceFile>, IWriteFileCallbackData, object?>? WriteFile { get; set; }

    /// <summary>ported from: src/compiler/program.ts:1956 —
    /// <c>getGlobalTypingsCacheLocation: maybeBind(host, host.getGlobalTypingsCacheLocation)</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>maybeBind</c> answers <c>undefined</c> when the host has no such member; the
    /// generated slot is a required method, so the port asks the host — the dropped-optionality ruling above. The
    /// generated <c>IModuleResolutionHost</c> declares it as a method returning <c>string?</c>, so an absent location is
    /// the host's <c>null</c>.</remarks>
    string? IModuleSpecifierResolutionHost.GetGlobalTypingsCacheLocation() => _host.GetGlobalTypingsCacheLocation();

    // ---------------------------------------------------------------------------------------------
    // The members TS declares optional on ModuleSpecifierResolutionHost (types.ts:9991-9999) and the
    // reference's `Program` therefore never implements: the generated interfaces require them
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/types.ts:9994 — <c>getModuleSpecifierCache?(): ModuleSpecifierCache</c>.</summary>
    /// <remarks>PORT-NOTE: TS's member is optional and the reference's <c>Program</c> does not declare it (the module
    /// specifier cache belongs to the language service — <c>moduleSpecifiers.ts:347,784</c> tests it with
    /// <c>?.()</c>), so the generated required slot has no value to answer and reports the contract gap instead of
    /// inventing an empty cache (<c>tools/Gen</c> cannot express the absence).</remarks>
    IModuleSpecifierCache IModuleSpecifierResolutionHost.GetModuleSpecifierCache()
        => throw new GeneratedContractException(
            "IModuleSpecifierResolutionHost.GetModuleSpecifierCache() is required but TS declares it optional "
            + "(getModuleSpecifierCache?(), types.ts:9994) and createProgram's Program has no module specifier cache "
            + "(the language service owns one, moduleSpecifiers.ts:347): the generated shape cannot express the absence (tools/Gen).");

    /// <summary>ported from: src/compiler/types.ts:9995 — <c>getPackageJsonInfoCache?(): PackageJsonInfoCache</c>.</summary>
    /// <remarks>PORT-NOTE: TS's member is optional and the reference's <c>Program</c> does not declare it; the only
    /// package.json cache a program owns is the module resolution cache's own
    /// (<c>moduleResolutionCache?.getPackageJsonInfoCache()</c>, <c>program.ts:1667,1689</c>), which is what the port
    /// answers. The generated slot is <c>object?</c> (the type is declared outside the extracted sources), so the
    /// interface's value is boxed there.</remarks>
    object? IModuleSpecifierResolutionHost.GetPackageJsonInfoCache() => _moduleResolutionCache?.GetPackageJsonInfoCache();

    /// <summary>ported from: src/compiler/types.ts:9997 —
    /// <c>getNearestAncestorDirectoryWithPackageJson?(fileName, rootDir?): string | undefined</c>.</summary>
    /// <remarks>PORT-NOTE: TS's member is optional and the reference's <c>Program</c> does not declare it — its callers
    /// fall back to a <c>fileExists</c> ancestor walk (<c>moduleSpecifiers.ts:698-707</c>), which is M9's member, not
    /// one a program can answer. The generated required slot cannot express the absence, so it reports the contract
    /// gap rather than answering a plausible "no package.json above this file" (<c>tools/Gen</c>).</remarks>
    string? IModuleSpecifierResolutionHost.GetNearestAncestorDirectoryWithPackageJson(string? fileName, string? rootDir)
        => throw new GeneratedContractException(
            "IModuleSpecifierResolutionHost.GetNearestAncestorDirectoryWithPackageJson() is required but TS declares it optional "
            + "(types.ts:9997) and createProgram's Program does not implement it; its callers use the fileExists ancestor walk "
            + "(moduleSpecifiers.ts:698). The generated shape cannot express the absence (tools/Gen).");

    /// <summary>ported from: src/compiler/types.ts:9999 — <c>trace?(s: string): void</c>.</summary>
    /// <remarks>PORT-NOTE: TS's member is optional and the literal has no <c>trace</c>; the port asks the compiler host's
    /// tracer, which is the function the reference binds where it needs one
    /// (<c>trace: maybeBind(host, host.trace)</c>, <c>program.ts:2634</c>, <c>5120</c>), per the dropped-optionality
    /// ruling.</remarks>
    void IModuleSpecifierResolutionHost.Trace(string? s) => _host.Trace(s);

    // ---------------------------------------------------------------------------------------------
    // The remaining Program member the literal does not carry (types.ts:4934)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/types.ts:4934 — <c>getBuildInfo?(): BuildInfo</c>.</summary>
    /// <remarks>PORT-NOTE: TS's member is optional and <c>createProgram</c> never assigns it: only a builder program
    /// does (<c>builder.ts:1686</c>, read back by the emit host at <c>program.ts:2628</c>) — <b>M8's builder owns that
    /// assignment</b>, so this slot belongs to the residual table with M8 as its owner. The generated slot is a required
    /// non-nullable <c>IBuildInfo</c>, which cannot express "this program has no build info", so the slot reports the
    /// contract gap instead of inventing an empty <c>BuildInfo</c>.</remarks>
    IBuildInfo IProgram.GetBuildInfo()
        => throw new GeneratedContractException(
            "IProgram.GetBuildInfo() is required but TS declares it optional (getBuildInfo?(), types.ts:4934), and only a builder "
            + "program assigns it (builder.ts:1686 — M8's builder owns that assignment): a createProgram Program has none, and the "
            + "generated shape cannot express the absence (tools/Gen).");
}
