namespace TypeScript.Compiler;

// ported from: src/compiler/program.ts:1515-1755 (the state createProgram opens with)
// ported from: src/compiler/program.ts:1970-2175 (the resolution accessors, the packages map and the resolution workers)
// ported from: src/compiler/program.ts:2126-2172 (compareDefaultLibFiles/toPath/getCommonSourceDirectory/getClassifiableNames)
/// <summary>
/// The <c>Program</c> — TS's <c>createProgram</c> closure and the <c>program</c> object literal it returns, as one
/// instance class.
/// </summary>
/// <remarks>
/// TS's <c>createProgram</c> is a 3,300-line function whose locals are the program's mutable state and whose nested
/// functions are its members; the returned object literal is the <c>Program</c> interface. The port gives it the
/// treatment <c>parser.ts</c>, <c>binder.ts</c> and <c>checker.ts</c> already got (§15.10, §15.13, §15.15): the state
/// becomes instance fields on this class and the members become its members, split across partial files by the
/// reference's own sections:
/// <list type="bullet">
/// <item><c>Program/Program.State.cs</c> — this file: the state block, the constructor and the accessors that only
/// read it (resolved-module lookups, the packages map, the resolution workers, common source directory).</item>
/// <item><c>Program/Program.cs</c> — <c>CreateProgram</c> itself, the <c>Program</c> interface implementation and the
/// file graph (<c>processRootFile</c>/<c>findSourceFile</c>/<c>collectExternalModuleReferences</c>/…).</item>
/// <item><c>Program/Program.Resolutions.cs</c> — <c>resolveNamesReusingOldState</c> and the loaders.</item>
/// <item><c>Program/Program.ProjectReferences.cs</c> — <c>tryReuseStructureFromOldProgram</c>, the project-reference
/// redirect machinery and <c>parseProjectReferenceConfigFile</c>.</item>
/// <item><c>Program/Program.Verify.cs</c> — <c>verifyCompilerOptions</c>, the deprecation checks and the
/// option-syntax diagnostics.</item>
/// <item><c>Program/Program.JsSyntactic.cs</c> — <c>getJSSyntacticDiagnosticsForFile</c>, the comment-directive pass
/// and <c>markPrecedingCommentDirectiveLine</c>.</item>
/// <item><c>Program/Program.Diagnostics.cs</c> — <c>programDiagnostics.ts</c> (<see cref="ProgramDiagnostics"/>) and
/// the diagnostic aggregation the <c>Program</c> interface exposes.</item>
/// <item><c>Program/Program.Host.cs</c> — the module-level free functions of the file: <c>createCompilerHostWorker</c>,
/// the cache wrapper, the <c>formatDiagnostic*</c> family and the mode/loader helpers.</item>
/// <item><c>Program/Program.EmitBoundary.cs</c> — the M7 boundary: <c>emit</c>, <c>emitBuildInfo</c>,
/// <c>getEmitHost</c>, <c>getDeclarationDiagnostics</c>.</item>
/// </list>
/// <para>
/// PORT-NOTE: TS's closure variables are <c>internal</c> rather than <c>private</c>, for the reason
/// <c>Checker/Checker.cs</c> records: the slices above read and write them from sibling partial files, and a private
/// slot no member reads is a warning this project treats as an error.
/// </para>
/// <para>
/// PORT-NOTE: TS types <c>ResolutionMode</c> as <c>ModuleKind.ESNext | ModuleKind.CommonJS | undefined</c>; the
/// generated interfaces that carry it spell it <c>object?</c> (the generator has no union). Every new signature in the
/// port spells it <see cref="ModuleKind"/> and casts at the generated boundary — one cast per member instead of an
/// object-typed slot at every use.
/// </para>
/// </remarks>
internal sealed partial class Program : IProgram
{
    // ---------------------------------------------------------------------------------------------
    // The arguments createProgram was called with (program.ts:1516-1518, 1564-1570)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/program.ts:1517 — <c>rootNames</c>.</summary>
    internal readonly List<string> _rootNames;

    /// <summary>ported from: src/compiler/program.ts:1517 — <c>options</c>.</summary>
    internal readonly ICompilerOptions _options;

    /// <summary>ported from: src/compiler/program.ts:1518 — <c>oldProgram</c>.</summary>
    /// <remarks>PORT-NOTE: TS clears the binding (<c>oldProgram = undefined</c>) once the old program's source files
    /// have been released so the closure cannot keep it alive; the field is nulled at the same point.</remarks>
    internal Program? _oldProgram;

    /// <summary>ported from: src/compiler/program.ts:1517 — <c>projectReferences</c>.</summary>
    internal readonly List<IProjectReference>? _projectReferences;

    /// <summary>ported from: src/compiler/program.ts:1517 — <c>configFileParsingDiagnostics</c>.</summary>
    internal readonly List<IDiagnostic>? _configFileParsingDiagnostics;

    /// <summary>ported from: src/compiler/program.ts:1517 — <c>typeScriptVersion</c>.</summary>
    internal readonly string? _typeScriptVersion;

    /// <summary>ported from: src/compiler/program.ts:1577 — <c>host</c>.</summary>
    internal readonly ICompilerHost _host;

    /// <summary>ported from: src/compiler/program.ts:1578 — <c>configParsingHost</c>.</summary>
    internal readonly IParseConfigHost _configParsingHost;

    /// <summary>ported from: src/compiler/program.ts:1580 — <c>getDefaultLibraryFileName</c>.</summary>
    internal readonly Func<string> _getDefaultLibraryFileName;

    /// <summary>ported from: src/compiler/program.ts:1581 — <c>defaultLibraryPath</c>.</summary>
    internal readonly string _defaultLibraryPath;

    /// <summary>ported from: src/compiler/program.ts:1583 — <c>skipVerifyCompilerOptions</c>.</summary>
    internal bool _skipVerifyCompilerOptions;

    /// <summary>ported from: src/compiler/program.ts:1584 — <c>currentDirectory</c>.</summary>
    internal readonly string _currentDirectory;

    /// <summary>ported from: src/compiler/program.ts:1585 — <c>supportedExtensions</c>.</summary>
    internal readonly List<List<string>> _supportedExtensions;

    /// <summary>ported from: src/compiler/program.ts:1586 — <c>supportedExtensionsWithJsonIfResolveJsonModule</c>.</summary>
    internal readonly List<List<string>> _supportedExtensionsWithJsonIfResolveJsonModule;

    /// <summary>ported from: src/compiler/program.ts:1589 — <c>hasEmitBlockingDiagnostics</c>.</summary>
    internal readonly Dictionary<string, bool> _hasEmitBlockingDiagnostics = [];

    /// <summary>ported from: src/compiler/program.ts:1590 — <c>_compilerOptionsObjectLiteralSyntax</c>.</summary>
    /// <remarks>PORT-NOTE: TS's slot is <c>ObjectLiteralExpression | false | undefined</c>, where <c>false</c> is a
    /// memoized "there is none"; the port keeps the two states in a separate flag (see
    /// <see cref="_compilerOptionsObjectLiteralSyntaxComputed"/>).</remarks>
    internal ObjectLiteralExpression? _compilerOptionsObjectLiteralSyntax;

    /// <summary>ported from: src/compiler/program.ts:1590 — the <c>false</c> half of the memoized slot.</summary>
    internal bool _compilerOptionsObjectLiteralSyntaxComputed;

    /// <summary>ported from: src/compiler/program.ts:1591 — <c>_compilerOptionsPropertySyntax</c>.</summary>
    internal PropertyAssignment? _compilerOptionsPropertySyntax;

    /// <summary>ported from: src/compiler/program.ts:1591 — the <c>false</c> half of the memoized slot.</summary>
    internal bool _compilerOptionsPropertySyntaxComputed;

    /// <summary>ported from: src/compiler/program.ts:1593 — <c>moduleResolutionCache</c>.</summary>
    internal ModuleResolutionCache? _moduleResolutionCache;

    /// <summary>ported from: src/compiler/program.ts:1594-1601 — <c>actualResolveModuleNamesWorker</c>.</summary>
    internal ResolveModuleNamesWorker _actualResolveModuleNamesWorker = null!;

    /// <summary>ported from: src/compiler/program.ts:1602 — <c>hasInvalidatedResolutions</c>.</summary>
    internal readonly Func<string, bool> _hasInvalidatedResolutions;

    /// <summary>ported from: src/compiler/program.ts:1641-1642 — <c>actualResolveTypeReferenceDirectiveNamesWorker</c>.</summary>
    internal ResolveTypeReferenceDirectiveNamesWorker _actualResolveTypeReferenceDirectiveNamesWorker = null!;

    /// <summary>ported from: src/compiler/program.ts:1683 — <c>hasInvalidatedLibResolutions</c>.</summary>
    internal readonly Func<string, bool> _hasInvalidatedLibResolutions;

    /// <summary>ported from: src/compiler/program.ts:1684 — <c>actualResolveLibrary</c>.</summary>
    internal ResolveLibraryWorker _actualResolveLibrary = null!;

    /// <summary>ported from: src/compiler/program.ts:1696 — <c>packageIdToSourceFile</c>.</summary>
    internal readonly Dictionary<string, SourceFile> _packageIdToSourceFile = [];

    /// <summary>ported from: src/compiler/program.ts:1698 — <c>sourceFileToPackageName</c>.</summary>
    internal Dictionary<string, string> _sourceFileToPackageName = [];

    /// <summary>ported from: src/compiler/program.ts:1700 — <c>redirectTargetsMap</c>.</summary>
    internal Core.MultiMap<string, string> _redirectTargetsMap = Core.CreateMultiMap<string, string>();

    /// <summary>ported from: src/compiler/program.ts:1701 — <c>usesUriStyleNodeCoreModules</c>.</summary>
    internal bool? _usesUriStyleNodeCoreModules;

    /// <summary>ported from: src/compiler/program.ts:1709 — <c>filesByName</c>.</summary>
    /// <remarks>PORT-NOTE: TS's value is <c>SourceFile | false | undefined</c>: <c>undefined</c> = not looked at,
    /// <c>false</c> = looked at and missing, a file = found. The port keeps the three states as
    /// <see cref="FileByNameEntry"/> — a dictionary value cannot hold <c>undefined</c> and <c>false</c> apart.</remarks>
    internal readonly Dictionary<string, FileByNameEntry> _filesByName = [];

    /// <summary>ported from: src/compiler/program.ts:1710 — <c>libFiles</c>.</summary>
    internal readonly HashSet<string> _libFiles = [];

    /// <summary>ported from: src/compiler/program.ts:1711 — <c>missingFileNames</c>.</summary>
    internal Dictionary<string, string> _missingFileNames = [];

    /// <summary>ported from: src/compiler/program.ts:1714 — <c>filesByNameIgnoreCase</c>.</summary>
    internal readonly Dictionary<string, SourceFile>? _filesByNameIgnoreCase;

    /// <summary>ported from: src/compiler/program.ts:1717 — <c>resolvedProjectReferences</c>.</summary>
    internal List<IResolvedProjectReference?>? _resolvedProjectReferences;

    /// <summary>ported from: src/compiler/program.ts:1718 — <c>projectReferenceRedirects</c>.</summary>
    internal Dictionary<string, ProjectReferenceRedirect>? _projectReferenceRedirects;

    /// <summary>ported from: src/compiler/program.ts:1720 — <c>mapSourceFileToResolvedRef</c>.</summary>
    internal Dictionary<string, IResolvedRefAndOutputDts>? _mapSourceFileToResolvedRef;

    /// <summary>ported from: src/compiler/program.ts:1721 — <c>mapOutputFileToResolvedRef</c>.</summary>
    internal Dictionary<string, IResolvedRefAndSource>? _mapOutputFileToResolvedRef;

    /// <summary>ported from: src/compiler/program.ts:1723 — <c>useSourceOfProjectReferenceRedirect</c>.</summary>
    internal readonly bool _useSourceOfProjectReferenceRedirect;

    /// <summary>ported from: src/compiler/program.ts:1725 — <c>onProgramCreateComplete</c>.</summary>
    internal readonly Action _onProgramCreateComplete;

    /// <summary>ported from: src/compiler/program.ts:1725 — <c>fileExists</c> (the redirect-aware wrapper).</summary>
    internal readonly Func<string, bool> _fileExists;

    /// <summary>ported from: src/compiler/program.ts:1725 — <c>directoryExists</c> (the redirect-aware wrapper).</summary>
    internal readonly Func<string, bool> _directoryExists;

    /// <summary>ported from: src/compiler/program.ts:1734 — <c>readFile</c>.</summary>
    internal readonly Func<string, string?> _readFile;

    /// <summary>ported from: src/compiler/program.ts:1737 — <c>shouldCreateNewSourceFile</c>.</summary>
    internal readonly bool _shouldCreateNewSourceFile;

    // ---------------------------------------------------------------------------------------------
    // The state createProgram fills in as the file graph grows (program.ts:1531-1577, 1739-1742)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/program.ts:1531 — <c>reportInvalidIgnoreDeprecations</c>.</summary>
    internal readonly Action _reportInvalidIgnoreDeprecations;

    /// <summary>ported from: src/compiler/program.ts:1533 — <c>processingDefaultLibFiles</c>.</summary>
    internal List<SourceFile>? _processingDefaultLibFiles;

    /// <summary>ported from: src/compiler/program.ts:1534 — <c>processingOtherFiles</c>.</summary>
    internal List<SourceFile>? _processingOtherFiles;

    /// <summary>ported from: src/compiler/program.ts:1535 — <c>files</c>.</summary>
    internal List<SourceFile> _files = [];

    /// <summary>ported from: src/compiler/program.ts:1536 — <c>symlinks</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>SymlinkCache</c> comes from <c>symlinkCache.ts</c>, which no slice has ported yet, and
    /// the generated slots that carry it are <c>object?</c>. The slot is therefore <c>object?</c> here; the M6ProgVerify
    /// slice's <c>GetSymlinkCache()</c> is what fills and reads it.</remarks>
    internal object? _symlinks;

    /// <summary>ported from: src/compiler/program.ts:1537 — <c>typeChecker</c>.</summary>
    internal ITypeChecker? _typeChecker;

    /// <summary>ported from: src/compiler/program.ts:1538 — <c>classifiableNames</c>.</summary>
    internal HashSet<string>? _classifiableNames;

    /// <summary>ported from: src/compiler/program.ts:1539 — <c>filesWithReferencesProcessed</c>.</summary>
    internal HashSet<string>? _filesWithReferencesProcessed;

    /// <summary>ported from: src/compiler/program.ts:1540 — <c>cachedBindAndCheckDiagnosticsForFile</c>.</summary>
    internal Dictionary<string, List<IDiagnostic>>? _cachedBindAndCheckDiagnosticsForFile;

    /// <summary>ported from: src/compiler/program.ts:1541 — <c>cachedDeclarationDiagnosticsForFile</c>.</summary>
    internal Dictionary<string, List<IDiagnosticWithLocation>>? _cachedDeclarationDiagnosticsForFile;

    /// <summary>ported from: src/compiler/program.ts:1542 — <c>programDiagnostics</c>.</summary>
    internal readonly ProgramDiagnostics _programDiagnostics;

    /// <summary>ported from: src/compiler/program.ts:1544 — <c>automaticTypeDirectiveNames</c>.</summary>
    internal List<string>? _automaticTypeDirectiveNames;

    /// <summary>ported from: src/compiler/program.ts:1545 — <c>automaticTypeDirectiveResolutions</c>.</summary>
    internal ModeAwareCache<IResolvedTypeReferenceDirectiveWithFailedLookupLocations> _automaticTypeDirectiveResolutions = null!;

    /// <summary>ported from: src/compiler/program.ts:1547 — <c>resolvedLibReferences</c>.</summary>
    internal Dictionary<string, ILibResolution<object?>>? _resolvedLibReferences;

    /// <summary>ported from: src/compiler/program.ts:1548 — <c>resolvedLibProcessing</c>.</summary>
    internal Dictionary<string, ILibResolution<object?>>? _resolvedLibProcessing;

    /// <summary>ported from: src/compiler/program.ts:1550 — <c>resolvedModules</c>.</summary>
    internal Dictionary<string, ModeAwareCache<IResolvedModuleWithFailedLookupLocations>>? _resolvedModules;

    /// <summary>ported from: src/compiler/program.ts:1551 — <c>resolvedModulesProcessing</c>.</summary>
    internal Dictionary<string, List<IResolvedModuleWithFailedLookupLocations>>? _resolvedModulesProcessing;

    /// <summary>ported from: src/compiler/program.ts:1552 — <c>resolvedTypeReferenceDirectiveNames</c>.</summary>
    internal Dictionary<string, ModeAwareCache<IResolvedTypeReferenceDirectiveWithFailedLookupLocations>>? _resolvedTypeReferenceDirectiveNames;

    /// <summary>ported from: src/compiler/program.ts:1553 — <c>resolvedTypeReferenceDirectiveNamesProcessing</c>.</summary>
    internal Dictionary<string, List<IResolvedTypeReferenceDirectiveWithFailedLookupLocations>>? _resolvedTypeReferenceDirectiveNamesProcessing;

    /// <summary>ported from: src/compiler/program.ts:1555 — <c>packageMap</c>.</summary>
    internal Dictionary<string, bool>? _packageMap;

    /// <summary>ported from: src/compiler/program.ts:1565 — <c>maxNodeModuleJsDepth</c>.</summary>
    internal readonly int _maxNodeModuleJsDepth;

    /// <summary>ported from: src/compiler/program.ts:1566 — <c>currentNodeModulesDepth</c>.</summary>
    internal int _currentNodeModulesDepth;

    /// <summary>ported from: src/compiler/program.ts:1569 — <c>modulesWithElidedImports</c>.</summary>
    internal readonly Dictionary<string, bool> _modulesWithElidedImports = [];

    /// <summary>ported from: src/compiler/program.ts:1572 — <c>sourceFilesFoundSearchingNodeModules</c>.</summary>
    internal readonly Dictionary<string, bool> _sourceFilesFoundSearchingNodeModules = [];

    /// <summary>ported from: src/compiler/program.ts:1741 — <c>structureIsReused</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's slot is <c>StructureIsReused | undefined</c> and stays <c>undefined</c> on every early return of
    /// <c>tryReuseStructureFromOldProgram</c>; only the paths that reuse at least one file assign it. Two reference call
    /// sites read the <em>variable</em> rather than that function's result and therefore depend on the distinction
    /// (<c>program.ts:2244</c> and <c>:3850</c>), so the port keeps the third state instead of letting the return value
    /// overwrite it. <c>TryReuseStructureFromOldProgram</c> (M6ProgProjRefs) writes this field; the constructor only
    /// reads the returned value to decide whether to build the file graph.
    /// </remarks>
    internal StructureIsReused? _structureIsReused;

    // ---------------------------------------------------------------------------------------------
    // The program object literal's data slots (program.ts:1877-1935)
    // ---------------------------------------------------------------------------------------------
    //
    // The five entries of TS's `program` literal that hand out a closure variable verbatim. The reference's own
    // members of `createProgram` (releaseOldProgram, isProgramUptoDate, shouldProgramCreateNewSourceFiles) and the
    // tests read them off the program value, so the port declares them here and `Program/Program.IProgram.cs`'s
    // explicit implementations forward to them — the arrangement `Program.GetSourceFiles` (`Program.Graph.cs:199`)
    // and `ITypeCheckerHost.GetSourceFiles` already record. Declaring only the interface implementations (which is
    // what the port had) leaves them unreachable from a `Program`-typed receiver, which is exactly the seam these
    // members close; there is still one body per slot.

    /// <summary>ported from: src/compiler/program.ts:1877 — <c>getRootFileNames: () =&gt; rootNames</c>.</summary>
    internal List<string> GetRootFileNames() => _rootNames;

    /// <summary>ported from: src/compiler/program.ts:1881 — <c>getMissingFilePaths: () =&gt; missingFileNames</c>.</summary>
    internal Dictionary<string, string> GetMissingFilePaths() => _missingFileNames;

    /// <summary>ported from: src/compiler/program.ts:1884 — <c>getCompilerOptions: () =&gt; options</c>.</summary>
    internal ICompilerOptions GetCompilerOptions() => _options;

    /// <summary>ported from: src/compiler/program.ts:1933 — <c>getProjectReferences</c>.</summary>
    internal List<IProjectReference>? GetProjectReferences() => _projectReferences;

    /// <summary>ported from: src/compiler/program.ts:1934 — <c>getResolvedProjectReferences</c>.</summary>
    /// <remarks>PORT-NOTE: TS's element type is <c>ResolvedProjectReference | undefined</c> — the reference's own reuse
    /// walk leaves holes in this array — and the port's slot keeps that shape. The generated interface drops the
    /// element's <c>| undefined</c>, so <c>IProgram.GetResolvedProjectReferences</c> is an explicit implementation that
    /// casts (its own note in <c>Program/Program.IProgram.cs</c> records it), and a call site that needs the
    /// reference's element type reads this member.</remarks>
    internal List<IResolvedProjectReference?>? GetResolvedProjectReferences() => _resolvedProjectReferences;

    // ---------------------------------------------------------------------------------------------
    // The resolution accessors (program.ts:1970-2081)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/program.ts:1970-1972 — <c>getResolvedModule</c>.</summary>
    internal IResolvedModuleWithFailedLookupLocations? GetResolvedModule(SourceFile file, string moduleName, ModuleKind? mode)
        => _resolvedModules is not null && _resolvedModules.TryGetValue(file.Path ?? "", out var cache)
            ? cache.Get(moduleName, mode)
            : null;

    /// <summary>ported from: src/compiler/program.ts:1974-1978 — <c>getResolvedModuleFromModuleSpecifier</c>.</summary>
    internal IResolvedModuleWithFailedLookupLocations? GetResolvedModuleFromModuleSpecifier(LiteralExpression moduleSpecifier, SourceFile? sourceFile = null)
    {
        sourceFile ??= Utilities.GetSourceFileOfNode(moduleSpecifier);
        Debug.AssertIsDefined(sourceFile, "`moduleSpecifier` must have a `SourceFile` ancestor. Use `program.getResolvedModule` instead to provide the containing file and resolution mode.");
        return GetResolvedModule(sourceFile!, moduleSpecifier.Text ?? "", GetModeForUsageLocation(sourceFile, moduleSpecifier));
    }

    /// <summary>ported from: src/compiler/program.ts:1980-1982 — <c>getResolvedTypeReferenceDirective</c>.</summary>
    internal IResolvedTypeReferenceDirectiveWithFailedLookupLocations? GetResolvedTypeReferenceDirective(SourceFile file, string typeDirectiveName, ModuleKind? mode)
        => _resolvedTypeReferenceDirectiveNames is not null && _resolvedTypeReferenceDirectiveNames.TryGetValue(file.Path ?? "", out var cache)
            ? cache.Get(typeDirectiveName, mode)
            : null;

    /// <summary>ported from: src/compiler/program.ts:1984-1990 — <c>getResolvedTypeReferenceDirectiveFromTypeReferenceDirective</c>.</summary>
    internal IResolvedTypeReferenceDirectiveWithFailedLookupLocations? GetResolvedTypeReferenceDirectiveFromTypeReferenceDirective(IFileReference typeRef, SourceFile sourceFile)
        => GetResolvedTypeReferenceDirective(
            sourceFile,
            typeRef.FileName ?? "",
            GetModeForTypeReferenceDirectiveInFile(typeRef, sourceFile));

    /// <summary>ported from: src/compiler/program.ts:1992-1998 — <c>forEachResolvedModule</c>.</summary>
    internal void ForEachResolvedModule(Action<IResolvedModuleWithFailedLookupLocations, string, ModuleKind?, string> callback, SourceFile? file = null)
        => ForEachResolution(_resolvedModules, callback, file);

    /// <summary>ported from: src/compiler/program.ts:1999-2005 — <c>forEachResolvedTypeReferenceDirective</c>.</summary>
    internal void ForEachResolvedTypeReferenceDirective(Action<IResolvedTypeReferenceDirectiveWithFailedLookupLocations, string, ModuleKind?, string> callback, SourceFile? file = null)
        => ForEachResolution(_resolvedTypeReferenceDirectiveNames, callback, file);

    /// <summary>ported from: src/compiler/program.ts:2006-2013 — <c>forEachResolution</c>.</summary>
    private static void ForEachResolution<T>(
        Dictionary<string, ModeAwareCache<T>>? resolutionCache,
        Action<T, string, ModuleKind?, string> callback,
        SourceFile? file)
    {
        if (file is not null)
        {
            if (resolutionCache is not null && resolutionCache.TryGetValue(file.Path ?? "", out var cache))
            {
                cache.ForEach((resolution, name, mode) => callback(resolution, name, mode, file.Path ?? ""));
            }
        }
        else if (resolutionCache is not null)
        {
            foreach (var (filePath, resolutions) in resolutionCache)
            {
                resolutions.ForEach((resolution, name, mode) => callback(resolution, name, mode, filePath));
            }
        }
    }

    /// <summary>ported from: src/compiler/program.ts:2015-2026 — <c>getPackagesMap</c>.</summary>
    private Dictionary<string, bool> GetPackagesMap()
    {
        if (_packageMap is not null)
        {
            return _packageMap;
        }

        _packageMap = [];
        // A package name maps to true when we detect it has .d.ts files.
        // This is useful as an approximation of whether a package bundles its own types.
        // Note: we only look at files already found by module resolution,
        // so there may be files we did not consider.
        ForEachResolvedModule((resolution, _, _, _) =>
        {
            var resolvedModule = resolution.ResolvedModule;
            if (resolvedModule?.PackageId is not null)
            {
                _packageMap[resolvedModule.PackageId.Name ?? ""] =
                    resolvedModule.Extension == Extension.Dts.Value || _packageMap.GetValueOrDefault(resolvedModule.PackageId.Name ?? "");
            }
        });
        return _packageMap;
    }

    /// <summary>ported from: src/compiler/program.ts:2028-2030 — <c>typesPackageExists</c>.</summary>
    internal bool TypesPackageExists(string packageName)
        => GetPackagesMap().ContainsKey(ModuleNameResolver.GetTypesPackageName(packageName));

    /// <summary>ported from: src/compiler/program.ts:2031-2033 — <c>packageBundlesTypes</c>.</summary>
    internal bool PackageBundlesTypes(string packageName)
        => GetPackagesMap().GetValueOrDefault(packageName);

    /// <summary>ported from: src/compiler/program.ts:2035-2041 — <c>addResolutionDiagnostics</c>.</summary>
    internal void AddResolutionDiagnostics(IResolvedModuleWithFailedLookupLocations resolution)
        => AddResolutionDiagnosticsWorker(resolution.ResolutionDiagnostics);

    /// <summary>ported from: src/compiler/program.ts:2035-2041 — the type-reference half of
    /// <c>addResolutionDiagnostics</c>.</summary>
    internal void AddResolutionDiagnostics(IResolvedTypeReferenceDirectiveWithFailedLookupLocations resolution)
        => AddResolutionDiagnosticsWorker(resolution.ResolutionDiagnostics);

    private void AddResolutionDiagnosticsWorker(List<IDiagnostic>? resolutionDiagnostics)
    {
        if (resolutionDiagnostics is null || resolutionDiagnostics.Count == 0)
        {
            return;
        }

        _programDiagnostics.AddFileProcessingDiagnostic(new ResolutionDiagnostics
        {
            Kind = FilePreprocessingDiagnosticsKind.ResolutionDiagnostics,
            Diagnostics = resolutionDiagnostics,
        });
    }

    /// <summary>ported from: src/compiler/program.ts:2043-2057 — <c>addResolutionDiagnosticsFromResolutionOrCache</c>.</summary>
    private void AddResolutionDiagnosticsFromResolutionOrCache(SourceFile containingFile, string name, IResolvedModuleWithFailedLookupLocations resolution, ModuleKind? mode)
    {
        // diagnostics directly from the resolution
        // PORT-NOTE: TS tests `!host.resolveModuleNames`, the legacy host member; the generated host interface declares
        // it required, so the port tests the optional slot the same fact lives in (`ResolveModuleNamesFunc`, the one
        // `ResolveModuleNamesWorkerFactory` selects the legacy worker through).
        if (_host.ResolveModuleNameLiteralsFunc is not null || _host.ResolveModuleNamesFunc is null)
        {
            AddResolutionDiagnostics(resolution);
            return;
        }

        if (_moduleResolutionCache is null || Utilities.IsExternalModuleNameRelative(name))
        {
            return;
        }

        var containingFileName = Paths.GetNormalizedAbsolutePath(containingFile.OriginalFileName ?? "", _currentDirectory);
        var containingDir = Paths.GetDirectoryPath(containingFileName);
        var redirectedReference = GetRedirectReferenceForResolution(containingFile);
        // only nonrelative names hit the cache, and, at least as of right now, only nonrelative names can issue diagnostics
        // (Since diagnostics are only issued via import or export map lookup)
        // This may totally change if/when the issue of output paths not mapping to input files is fixed in a broader context
        // When it is, how we extract diagnostics from the module name resolver will have the be refined - the current cache
        // APIs wrapping the underlying resolver make it almost impossible to smuggle the diagnostics out in a generalized way
        var fromCache = _moduleResolutionCache.GetFromNonRelativeNameCache(name, mode, containingDir, redirectedReference);
        if (fromCache is not null)
        {
            AddResolutionDiagnostics(fromCache);
        }
    }

    /// <summary>ported from: src/compiler/program.ts:2059-2080 — <c>resolveModuleNamesWorker</c>.</summary>
    private List<IResolvedModuleWithFailedLookupLocations> ResolveModuleNamesWorker(
        List<LiteralExpression> moduleNames,
        SourceFile containingFile,
        List<LiteralExpression>? reusedNames)
    {
        var containingFileName = Paths.GetNormalizedAbsolutePath(containingFile.OriginalFileName ?? "", _currentDirectory);
        var redirectedReference = GetRedirectReferenceForResolution(containingFile);
        Tracing.Current?.Push(Tracing.Phase.Program, "resolveModuleNamesWorker", TracingArgs.Of(("containingFileName", containingFileName)));
        Performance.Mark("beforeResolveModule");
        var result = _actualResolveModuleNamesWorker(moduleNames, containingFileName, redirectedReference, _options, containingFile, reusedNames);
        Performance.Mark("afterResolveModule");
        Performance.Measure("ResolveModule", "beforeResolveModule", "afterResolveModule");
        Tracing.Current?.Pop();
        return result;
    }

    /// <summary>ported from: src/compiler/program.ts:2082-2104 — <c>resolveTypeReferenceDirectiveNamesWorker</c>.</summary>
    private List<IResolvedTypeReferenceDirectiveWithFailedLookupLocations> ResolveTypeReferenceDirectiveNamesWorker(
        List<IFileReference> typeDirectiveNames,
        string containingFile,
        List<IFileReference>? reusedNames)
        => ResolveTypeReferenceDirectiveNamesWorkerCore(typeDirectiveNames, containingFile, /*containingSourceFile*/ null, reusedNames);

    /// <summary>ported from: src/compiler/program.ts:2082-2104 — the <c>SourceFile</c> overload.</summary>
    private List<IResolvedTypeReferenceDirectiveWithFailedLookupLocations> ResolveTypeReferenceDirectiveNamesWorker(
        List<IFileReference> typeDirectiveNames,
        SourceFile containingFile,
        List<IFileReference>? reusedNames)
        => ResolveTypeReferenceDirectiveNamesWorkerCore(typeDirectiveNames, /*containingFile*/ null!, containingFile, reusedNames);

    /// <summary>ported from: src/compiler/program.ts:2082-2104 — the <c>string</c> overload.</summary>
    private List<IResolvedTypeReferenceDirectiveWithFailedLookupLocations> ResolveTypeReferenceDirectiveNamesWorker(
        List<string> typeDirectiveNames,
        string containingFile,
        List<string>? reusedNames)
    {
        var containingSourceFile = (SourceFile?)null;
        var redirectedReference = containingSourceFile is not null ? GetRedirectReferenceForResolution(containingSourceFile) : null;
        Tracing.Current?.Push(Tracing.Phase.Program, "resolveTypeReferenceDirectiveNamesWorker", TracingArgs.Of(("containingFileName", containingFile)));
        Performance.Mark("beforeResolveTypeReference");
        var result = _actualResolveTypeReferenceDirectiveNamesWorker(typeDirectiveNames, containingFile, redirectedReference, _options, containingSourceFile, reusedNames);
        Performance.Mark("afterResolveTypeReference");
        Performance.Measure("ResolveTypeReference", "beforeResolveTypeReference", "afterResolveTypeReference");
        Tracing.Current?.Pop();
        return result;
    }

    /// <summary>ported from: src/compiler/program.ts:2082-2104 — the shared body of the three overloads.</summary>
    private List<IResolvedTypeReferenceDirectiveWithFailedLookupLocations> ResolveTypeReferenceDirectiveNamesWorkerCore(
        object typeDirectiveNames,
        string? containingFile,
        SourceFile? containingSourceFile,
        object? reusedNames)
    {
        var containingFileName = containingSourceFile is not null
            ? Paths.GetNormalizedAbsolutePath(containingSourceFile.OriginalFileName ?? "", _currentDirectory)
            : containingFile!;
        var redirectedReference = containingSourceFile is not null ? GetRedirectReferenceForResolution(containingSourceFile) : null;
        Tracing.Current?.Push(Tracing.Phase.Program, "resolveTypeReferenceDirectiveNamesWorker", TracingArgs.Of(("containingFileName", containingFileName)));
        Performance.Mark("beforeResolveTypeReference");
        var result = _actualResolveTypeReferenceDirectiveNamesWorker(typeDirectiveNames, containingFileName, redirectedReference, _options, containingSourceFile, reusedNames);
        Performance.Mark("afterResolveTypeReference");
        Performance.Measure("ResolveTypeReference", "beforeResolveTypeReference", "afterResolveTypeReference");
        Tracing.Current?.Pop();
        return result;
    }

    /// <summary>ported from: src/compiler/program.ts:2106-2117 — <c>getRedirectReferenceForResolution</c>.</summary>
    internal IResolvedProjectReference? GetRedirectReferenceForResolution(SourceFile file)
    {
        var redirect = GetRedirectFromSourceFile(file.OriginalFileName ?? "");
        if (redirect is not null || !Parser.IsDeclarationFileName(file.OriginalFileName ?? ""))
        {
            return redirect?.ResolvedRef;
        }

        // The originalFileName could not be actual source file name if file found was d.ts from referecned project
        // So in this case try to look up if this is output from referenced project, if it is use the redirected project in that case
        var resultFromDts = GetRedirectFromOutput(file.Path ?? "")?.ResolvedRef;
        if (resultFromDts is not null)
        {
            return resultFromDts;
        }

        // If preserveSymlinks is true, module resolution wont jump the symlink
        // but the resolved real path may be the .d.ts from project reference
        // Note:: Currently we try the real path only if the
        // file is from node_modules to avoid having to run real path on all file paths
        if (!Utilities.HostUsesCaseSensitiveFileNames(_host) && false)
        {
            return null;
        }

        // PORT-NOTE: TS's guard is `!host.realpath || !options.preserveSymlinks || …`; the generated host interface
        // declares `realpath` required, so that clause compiles away (the treatment `Program.Graph.cs`'s
        // `findSourceFileWorker` guard records).
        if (!Js.IsTruthy(_options.PreserveSymlinks) || !(file.OriginalFileName ?? "").Contains(ModuleNameResolver.NodeModulesPathPart))
        {
            return null;
        }

        var realDeclarationPath = ToPath(_host.Realpath(file.OriginalFileName));
        return realDeclarationPath == file.Path ? null : GetRedirectFromOutput(realDeclarationPath)?.ResolvedRef;
    }

    /// <summary>ported from: src/compiler/program.ts:2124-2126 — <c>compareDefaultLibFiles</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's local function closes over nothing but <c>getDefaultLibFilePriority</c>, which reads the
    /// <c>defaultLibraryPath</c> <em>it</em> closes over — so both are instance members here (the reference's
    /// <c>compareDefaultLibFiles</c> is a local function of <c>createProgram</c>, i.e. the same receiver).
    /// <para>
    /// PORT-NOTE: the port's <see cref="Comparer{T}"/> is the three-way <see cref="Comparison"/> delegate
    /// (<c>corePublic.ts:39-40</c>), so the reference's subtraction
    /// (<c>getDefaultLibFilePriority(a) - getDefaultLibFilePriority(b)</c>) is spelled
    /// <see cref="Core.CompareValues(int?, int?)"/>.
    /// </para>
    /// </remarks>
    private Comparison CompareDefaultLibFiles(SourceFile a, SourceFile b)
        => Core.CompareValues(GetDefaultLibFilePriority(a), GetDefaultLibFilePriority(b));

    /// <summary>ported from: src/compiler/program.ts:2128-2137 — <c>getDefaultLibFilePriority</c>.</summary>
    private int GetDefaultLibFilePriority(SourceFile a)
    {
        if (Paths.ContainsPath(_defaultLibraryPath, a.FileName ?? ""))
        {
            var basename = Paths.GetBaseFileName(a.FileName ?? "");
            if (basename is "lib.d.ts" or "lib.es6.d.ts")
            {
                return 0;
            }

            var name = Core.RemoveSuffix(Core.RemovePrefix(basename, "lib."), ".d.ts");
            var index = Utilities.Libs.IndexOf(name);
            if (index != -1)
            {
                return index + 1;
            }
        }

        return Utilities.Libs.Count + 2;
    }

    /// <summary>ported from: src/compiler/program.ts:1584 — the host's <c>getCanonicalFileName</c>.</summary>
    /// <remarks>PORT-NOTE: TS calls <c>host.getCanonicalFileName</c> at every use site; the port caches it because
    /// <c>IProgram</c>'s generated <c>GetCanonicalFileName</c> slot is a settable <c>object?</c> property and the cached
    /// delegate is what it reads and writes.</remarks>
    internal Core.GetCanonicalFileName _getCanonicalFileName = null!;

    /// <summary>ported from: src/compiler/program.ts:2139-2141 — <c>toPath</c>.</summary>
    internal string ToPath(string fileName)
        => Paths.ToPath(fileName, _currentDirectory, _getCanonicalFileName);

}
