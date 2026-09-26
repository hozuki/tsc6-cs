namespace TypeScript.Compiler;

// ported from: src/compiler/program.ts:1499-1515 (the createProgram overloads)
// ported from: src/compiler/program.ts:1516-1875 (the body of createProgram)
// ported from: src/compiler/program.ts:2124-2141 (compareDefaultLibFiles, getDefaultLibFilePriority)
// ported from: src/compiler/program.ts:2598-2700 (the emit-free program queries)
/// <summary>
/// <c>createProgram</c> itself, its constructor and the two worker factories it opens with.
/// </summary>
/// <remarks>
/// The state lives in <c>Program/Program.State.cs</c>, the interface implementation in
/// <c>Program/Program.IProgram.cs</c>, the file graph in <c>Program/Program.Graph.cs</c> and the emit surface in
/// <c>Program/Program.Emit.cs</c>.
/// <para>
/// PORT-NOTE: TS's <c>createProgram</c> falls back to <c>createCompilerHost(options)</c> when no host is given, which
/// needs the <c>sys</c> singleton (§13.5 drops it). There is therefore no host to fall back to:
/// <see cref="CreateProgram(ICreateProgramOptions)"/> throws <see cref="HostRequiredException"/> — a named,
/// test-pinned failure rather than a silently empty program — and the argument-list overload requires the host.
/// </para>
/// </remarks>
internal sealed partial class Program
{
    /// <summary>The failure <c>createProgram</c> reports when no host was injected (§13.5: there is no <c>sys</c>).</summary>
    internal sealed class HostRequiredException()
        : InvalidOperationException("createProgram requires a CompilerHost: the port has no `sys` singleton, so a host cannot be created implicitly (docs/architecture.md §13.5).")
    {
    }

    /// <summary>ported from: src/compiler/program.ts:1499-1513 — <c>createProgram</c> (the <c>CreateProgramOptions</c>
    /// overload).</summary>
    internal static Program CreateProgram(ICreateProgramOptions createProgramOptions) => new(createProgramOptions);

    /// <summary>ported from: src/compiler/program.ts:1514 — <c>createProgram</c> (the argument-list overload).</summary>
    internal static Program CreateProgram(
        List<string> rootNames,
        ICompilerOptions options,
        ICompilerHost host,
        Program? oldProgram = null,
        List<IDiagnostic>? configFileParsingDiagnostics = null)
        => new(CreateCreateProgramOptions(rootNames, options, host, oldProgram, configFileParsingDiagnostics));

    /// <summary>ported from: src/compiler/program.ts:1515-1875 — the body of <c>createProgram</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's guard for string-valued custom options
    /// (<c>for (const option of commandLineOptionOfCustomType) if (typeof options[option.name] === "string") throw …</c>)
    /// is not ported: the generated <see cref="ICompilerOptions"/> types every such slot as its enum, so a string cannot
    /// reach the object, and <c>commandLineOptionOfCustomType</c> is M8's tsconfig metadata besides.
    /// <para>
    /// PORT-NOTE: TS's <c>createProgram</c> is a function returning the <c>program</c> object; the port's constructor is
    /// the same body, because the object <em>is</em> this instance (<c>Program/Program.IProgram.cs</c>).
    /// </para>
    /// </remarks>
    private Program(ICreateProgramOptions createProgramOptions)
    {
        _rootNames = createProgramOptions.RootNames ?? [];
        _options = createProgramOptions.Options ?? throw new ArgumentNullException(nameof(createProgramOptions));
        _projectReferences = createProgramOptions.ProjectReferences;
        _configFileParsingDiagnostics = createProgramOptions.ConfigFileParsingDiagnostics;
        _typeScriptVersion = createProgramOptions.TypeScriptVersion;
        _oldProgram = (Program?)createProgramOptions.OldProgram;
        _host = createProgramOptions.Host ?? throw new HostRequiredException();
        _configParsingHost = ParseConfigHostFromCompilerHostLike(_host);

        _getCanonicalFileName = _host.GetCanonicalFileName;
        _getDefaultLibraryFileName = Core.Memoize(() => _host.GetDefaultLibFileName(_options));
        _defaultLibraryPath = Paths.GetDirectoryPath(_getDefaultLibraryFileName());
        _currentDirectory = _host.GetCurrentDirectory();
        _supportedExtensions = Utilities.GetSupportedExtensions(_options);
        _supportedExtensionsWithJsonIfResolveJsonModule = Utilities.GetSupportedExtensionsWithJsonIfResolveJsonModule(_options, _supportedExtensions);
        _maxNodeModuleJsDepth = _options.MaxNodeModuleJsDepth ?? 0;
        _filesByNameIgnoreCase = Utilities.HostUsesCaseSensitiveFileNames(_host) ? [] : null;

        _reportInvalidIgnoreDeprecations = Core.Memoize(() => CreateOptionValueDiagnostic("ignoreDeprecations", Diagnostics.Invalid_value_for_ignoreDeprecations));
        _programDiagnostics = ProgramDiagnostics.CreateProgramDiagnostics(GetCompilerOptionsObjectLiteralSyntax);

        _hasInvalidatedResolutions = _host.HasInvalidatedResolutionsFunc ?? (static _ => false);
        ResolveModuleNamesWorkerFactory();
        _hasInvalidatedLibResolutions = _host.HasInvalidatedLibResolutionsFunc ?? (static _ => false);
        ResolveLibraryFactory();

        Tracing.Current?.Push(Tracing.Phase.Program, "createProgram", TracingArgs.Of(
            ("configFilePath", _options.ConfigFilePath),
            ("rootDir", _options.RootDir)), separateBeginAndEnd: true);
        Performance.Mark("beforeProgram");

        _useSourceOfProjectReferenceRedirect = Js.IsTruthy(_host.UseSourceOfProjectReferenceRedirectFunc?.Invoke() ?? false) && !Js.IsTruthy(_options.DisableSourceOfProjectReferenceRedirect);
        var redirectHost = UpdateHostForUseSourceOfProjectReferenceRedirect(new HostForUseSourceOfProjectReferenceRedirect
        {
            CompilerHost = _host,
            GetSymlinkCache = GetSymlinkCache,
            UseSourceOfProjectReferenceRedirect = _useSourceOfProjectReferenceRedirect,
            ToPath = ToPath,
            GetResolvedProjectReferences = () => _resolvedProjectReferences,
            GetRedirectFromOutput = GetRedirectFromOutput,
            ForEachResolvedProjectReference = cb => ForEachResolvedProjectReference<object?>(cb),
        });
        _onProgramCreateComplete = redirectHost.OnProgramCreateComplete;
        _fileExists = redirectHost.FileExists;
        _directoryExists = redirectHost.DirectoryExists;
        _readFile = _host.ReadFile;

        Tracing.Current?.Push(Tracing.Phase.Program, "shouldProgramCreateNewSourceFiles", TracingArgs.Of(("hasOldProgram", _oldProgram is not null)));
        _shouldCreateNewSourceFile = ShouldProgramCreateNewSourceFiles(_oldProgram, _options);
        Tracing.Current?.Pop();

        // We set `structuralIsReused` to `undefined` because `tryReuseStructureFromOldProgram` calls
        // `tryReuseStructureFromOldProgram` which checks `structuralIsReused`, which would be a TDZ violation if it
        // was not set in advance to `undefined`.
        Tracing.Current?.Push(Tracing.Phase.Program, "tryReuseStructureFromOldProgram", null);
        // program.ts:1741-1743 — the reference declares the variable (`let structureIsReused`) and then assigns the
        // call's result to it, so the value is absent while the call runs (which program.ts:2244 and :3850 read through
        // `resolveNamesReusingOldState` / `pathForLibFileWorker`) and the call's result afterwards — that result being
        // also what the program object hands out (program.ts:1954, `structureIsReused,`). `_structureIsReused` *is* that
        // variable (see its own note), so the assignment belongs here too; without it, a program that reuses nothing
        // kept the absent state, which reads as "structure was reused" at the two comparison sites.
        _structureIsReused = TryReuseStructureFromOldProgram();
        Tracing.Current?.Pop();
        if (_structureIsReused != StructureIsReused.Completely)
        {
            CreateFileGraph();
        }

        ReleaseOldProgram();

        // unconditionally set oldProgram to undefined to prevent it from being captured in closure
        _oldProgram = null;
        _resolvedLibProcessing = null;
        _resolvedModulesProcessing = null;
        _resolvedTypeReferenceDirectiveNamesProcessing = null;

        _onProgramCreateComplete();

        if (!_skipVerifyCompilerOptions)
        {
            VerifyCompilerOptions();
        }

        Performance.Mark("afterProgram");
        Performance.Measure("Program", "beforeProgram", "afterProgram");
        Tracing.Current?.Pop();
    }

    /// <summary>ported from: src/compiler/program.ts:1593-1639 — <c>actualResolveModuleNamesWorker</c>'s three arms and
    /// the module-resolution cache they select.</summary>
    /// <remarks>
    /// PORT-NOTE: TS tests <c>if (host.resolveModuleNameLiterals)</c>; the generated host interface declares all three
    /// members, so the port tests them for null. The middle arm rebuilds the objects an older host may answer without a
    /// <c>extension</c> — TS's object spread — by constructing the port's
    /// <see cref="ResolvedModuleFull"/> from the interface's slots.
    /// <para>
    /// PORT-NOTE: TS's test is the unchecked read <c>(resolved as ResolvedModuleFull).extension !== undefined</c>, and
    /// its true arm hands the same object on. The port cannot read a slot the object's static type may not have, so the
    /// test is the pattern <c>resolvedModule is IResolvedModuleFull { Extension: not null }</c> — which is also what
    /// makes the true arm assignable to the wrapper's <c>IResolvedModuleFull?</c> slot — and a non-<c>ResolvedModuleFull</c>
    /// object falls to the rebuilding arm, as TS's own <c>extension === undefined</c> case does.
    /// </para>
    /// </remarks>
    private void ResolveModuleNamesWorkerFactory()
    {
        if (_host.ResolveModuleNameLiteralsFunc is not null)
        {
            _actualResolveModuleNamesWorker = (moduleNames, containingFile, redirectedReference, options, containingSourceFile, reusedNames)
                => _host.ResolveModuleNameLiteralsFunc!(moduleNames, containingFile, redirectedReference, options, containingSourceFile, reusedNames);
            _moduleResolutionCache = CastModuleResolutionCache(_host.GetModuleResolutionCacheFunc?.Invoke());
        }
        else if (_host.ResolveModuleNamesFunc is not null)
        {
            _actualResolveModuleNamesWorker = (moduleNames, containingFile, redirectedReference, options, containingSourceFile, reusedNames) =>
            {
                var resolved = _host.ResolveModuleNamesFunc!(
                    moduleNames.Select(GetModuleResolutionName).ToList(),
                    containingFile,
                    reusedNames?.Select(GetModuleResolutionName).ToList(),
                    redirectedReference,
                    options,
                    containingSourceFile);
                return resolved.Select(resolvedModule => resolvedModule is null
                    ? (IResolvedModuleWithFailedLookupLocations)EmptyResolution
                    : resolvedModule is IResolvedModuleFull { Extension: not null } resolvedModuleFull
                        ? new ResolvedModuleWithFailedLookupLocations { ResolvedModule = resolvedModuleFull }
                        // An older host may have omitted extension, in which case we should infer it from the file extension of resolvedFileName.
                        : new ResolvedModuleWithFailedLookupLocations
                        {
                            ResolvedModule = new ResolvedModuleFull
                            {
                                ResolvedFileName = resolvedModule.ResolvedFileName ?? "",
                                IsExternalLibraryImport = resolvedModule.IsExternalLibraryImport,
                                ResolvedUsingTsExtension = resolvedModule.ResolvedUsingTsExtension,
                                Extension = Utilities.ExtensionFromPath(resolvedModule.ResolvedFileName ?? ""),
                            },
                        }).ToList();
            };
            _moduleResolutionCache = CastModuleResolutionCache(_host.GetModuleResolutionCacheFunc?.Invoke());
        }
        else
        {
            _moduleResolutionCache = ModuleNameResolver.CreateModuleResolutionCache(_currentDirectory, _getCanonicalFileName, _options);
            _actualResolveModuleNamesWorker = (moduleNames, containingFile, redirectedReference, options, containingSourceFile, reusedNames)
                => LoadWithModeAwareCache(
                    moduleNames,
                    containingFile,
                    redirectedReference,
                    options,
                    containingSourceFile,
                    _host,
                    _moduleResolutionCache,
                    CreateModuleResolutionLoader);
        }

        // PORT-NOTE: TS's `host.resolveTypeReferenceDirectiveReferences` is one generic member (its entry type is
        // `string | FileReference`) and the port keeps one slot per instantiation, so the worker dispatches on the
        // runtime list type exactly as `CompilerHost.ResolveTypeReferenceDirectiveReferences` does.
        if (_host.ResolveTypeReferenceDirectiveReferencesFunc is not null || _host.ResolveTypeReferenceDirectiveReferencesStringsFunc is not null)
        {
            _actualResolveTypeReferenceDirectiveNamesWorker = (typeDirectiveNames, containingFile, redirectedReference, options, containingSourceFile, reusedNames)
                => typeDirectiveNames is List<string> stringNames
                    ? _host.ResolveTypeReferenceDirectiveReferencesStringsFunc!(stringNames, containingFile, redirectedReference, options, containingSourceFile, reusedNames as List<string>)
                    : _host.ResolveTypeReferenceDirectiveReferencesFunc!((List<IFileReference>)typeDirectiveNames, containingFile, redirectedReference, options, containingSourceFile, reusedNames as List<IFileReference>);
        }
        else if (_host.ResolveTypeReferenceDirectivesFunc is not null)
        {
            _actualResolveTypeReferenceDirectiveNamesWorker = (typeDirectiveNames, containingFile, redirectedReference, options, containingSourceFile, reusedNames)
                => _host.ResolveTypeReferenceDirectivesFunc!(
                        typeDirectiveNames is List<IFileReference> refs ? GetTypeReferenceResolutionNames(refs) : (List<string>)typeDirectiveNames,
                        containingFile,
                        redirectedReference,
                        options,
                        containingSourceFile?.ImpliedNodeFormat)
                    .Select(resolvedTypeReferenceDirective => (IResolvedTypeReferenceDirectiveWithFailedLookupLocations)new ResolvedTypeReferenceDirectiveWithFailedLookupLocations { ResolvedTypeReferenceDirective = resolvedTypeReferenceDirective })
                    .ToList();
        }
        else
        {
            var typeReferenceDirectiveResolutionCache = ModuleNameResolver.CreateTypeReferenceDirectiveResolutionCache(
                _currentDirectory,
                _getCanonicalFileName,
                /*options*/ null,
                _moduleResolutionCache?.GetPackageJsonInfoCache(),
                _moduleResolutionCache?.OptionsToRedirectsKey);
            _actualResolveTypeReferenceDirectiveNamesWorker = (typeDirectiveNames, containingFile, redirectedReference, options, containingSourceFile, reusedNames)
                // PORT-NOTE: the entry type is the delegate's erased `string | FileReference` union, so the two
                // instantiations are dispatched here — the same shape `CompilerHost.ResolveTypeReferenceDirectiveReferences`
                // uses — because `LoadWithModeAwareCache`'s `TEntry` cannot be inferred from an `object` slot. The four
                // type arguments are the reference's `loadWithModeAwareCache` instantiation, which the conditional arms
                // must name because a lambda's conditional body gives inference no target type to work from.
                => typeDirectiveNames is List<string> stringNames
                    ? LoadWithModeAwareCache<string, SourceFile?, TypeReferenceDirectiveResolutionCache, IResolvedTypeReferenceDirectiveWithFailedLookupLocations>(
                        stringNames,
                        containingFile,
                        redirectedReference,
                        options,
                        containingSourceFile,
                        _host,
                        typeReferenceDirectiveResolutionCache,
                        CreateTypeReferenceResolutionLoader<string>)
                    : LoadWithModeAwareCache<IFileReference, SourceFile?, TypeReferenceDirectiveResolutionCache, IResolvedTypeReferenceDirectiveWithFailedLookupLocations>(
                        (List<IFileReference>)typeDirectiveNames,
                        containingFile,
                        redirectedReference,
                        options,
                        containingSourceFile,
                        _host,
                        typeReferenceDirectiveResolutionCache,
                        CreateTypeReferenceResolutionLoader<IFileReference>);
        }
    }

    /// <summary>ported from: src/compiler/program.ts:1683-1695 — <c>actualResolveLibrary</c>.</summary>
    private void ResolveLibraryFactory()
    {
        if (_host.ResolveLibraryFunc is not null)
        {
            _actualResolveLibrary = (libraryName, resolveFrom, options, libFileName)
                => _host.ResolveLibraryFunc!(libraryName, resolveFrom, options, libFileName);
        }
        else
        {
            var libraryResolutionCache = ModuleNameResolver.CreateModuleResolutionCache(
                _currentDirectory,
                _getCanonicalFileName,
                _options,
                _moduleResolutionCache?.GetPackageJsonInfoCache());
            _actualResolveLibrary = (libraryName, resolveFrom, options, libFileName)
                => ModuleNameResolver.ResolveLibrary(libraryName, resolveFrom, options, _host, libraryResolutionCache);
        }
    }

    /// <summary>ported from: src/compiler/program.ts:1743-1810 — the project-reference and root-file walk.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>getCommonSourceDirectoryOfConfig</c> and <c>getOutputDeclarationFileName</c> live in
    /// <c>emitter.ts</c> (M7) in the reference; the first is declared by this slice (see
    /// <see cref="GetCommonSourceDirectoryOfConfig"/>) because the program needs it before M7 lands, the second is
    /// called on M7's own member.
    /// </remarks>
    private void CreateFileGraph()
    {
        _processingDefaultLibFiles = [];
        _processingOtherFiles = [];

        if (_projectReferences is not null)
        {
            _resolvedProjectReferences ??= _projectReferences.Select(ParseProjectReferenceConfigFile).ToList();
            if (_rootNames.Count != 0)
            {
                for (var index = 0; index < _resolvedProjectReferences.Count; index++)
                {
                    var parsedRef = _resolvedProjectReferences[index];
                    if (parsedRef?.CommandLine is null)
                    {
                        continue;
                    }

                    var outFile = parsedRef.CommandLine.Options?.OutFile;
                    if (_useSourceOfProjectReferenceRedirect)
                    {
                        if (outFile is not null || Utilities.GetEmitModuleKind(parsedRef.CommandLine.Options) == ModuleKind.None)
                        {
                            foreach (var fileName in parsedRef.CommandLine.FileNames ?? [])
                            {
                                ProcessProjectReferenceFile(fileName, new ProjectReferenceFile { Kind = FileIncludeKind.SourceFromProjectReference, Index = index });
                            }
                        }
                    }
                    else if (outFile is not null)
                    {
                        ProcessProjectReferenceFile(Utilities.ChangeExtension(outFile, Extension.Dts.Value), new ProjectReferenceFile { Kind = FileIncludeKind.OutputFromProjectReference, Index = index });
                    }
                    else if (Utilities.GetEmitModuleKind(parsedRef.CommandLine.Options) == ModuleKind.None)
                    {
                        var getCommonSourceDirectory = Core.Memoize(() => GetCommonSourceDirectoryOfConfig(parsedRef.CommandLine, !Utilities.HostUsesCaseSensitiveFileNames(_host)));
                        foreach (var fileName in parsedRef.CommandLine.FileNames ?? [])
                        {
                            if (!Parser.IsDeclarationFileName(fileName) && !Paths.FileExtensionIs(fileName, Extension.Json.Value))
                            {
                                ProcessProjectReferenceFile(
                                    GetOutputDeclarationFileName(fileName, parsedRef.CommandLine, !Utilities.HostUsesCaseSensitiveFileNames(_host), getCommonSourceDirectory),
                                    new ProjectReferenceFile { Kind = FileIncludeKind.OutputFromProjectReference, Index = index });
                            }
                        }
                    }
                }
            }
        }

        Tracing.Current?.Push(Tracing.Phase.Program, "processRootFiles", TracingArgs.Of(("count", _rootNames.Count)));
        for (var index = 0; index < _rootNames.Count; index++)
        {
            ProcessRootFile(_rootNames[index], /*isDefaultLib*/ false, new ReferencedFile { Kind = FileIncludeKind.RootFile, Index = index });
        }
        Tracing.Current?.Pop();

        // load type declarations specified via 'types' argument or implicitly from types/ and node_modules/@types folders
        _automaticTypeDirectiveNames ??= _rootNames.Count != 0 ? ModuleNameResolver.GetAutomaticTypeDirectiveNames(_options, _host) : [];
        _automaticTypeDirectiveResolutions = ModuleNameResolver.CreateModeAwareCache<IResolvedTypeReferenceDirectiveWithFailedLookupLocations>();
        if (_automaticTypeDirectiveNames.Count != 0)
        {
            Tracing.Current?.Push(Tracing.Phase.Program, "processTypeReferences", TracingArgs.Of(("count", _automaticTypeDirectiveNames.Count)));
            // This containingFilename needs to match with the one used in managed-side
            var containingDirectory = _options.ConfigFilePath is not null ? Paths.GetDirectoryPath(_options.ConfigFilePath) : _currentDirectory;
            var containingFilename = Paths.CombinePaths(containingDirectory, InferredTypesContainingFile);
            var resolutions = ResolveTypeReferenceDirectiveNamesReusingOldState(_automaticTypeDirectiveNames, containingFilename);
            for (var i = 0; i < _automaticTypeDirectiveNames.Count; i++)
            {
                // under node16/nodenext module resolution, load `types`/ata include names as cjs resolution results by passing an `undefined` mode
                _automaticTypeDirectiveResolutions.Set(_automaticTypeDirectiveNames[i], /*mode*/ null, resolutions[i]);
                ProcessTypeReferenceDirective(
                    _automaticTypeDirectiveNames[i],
                    /*mode*/ null,
                    resolutions[i],
                    new AutomaticTypeDirectiveFile
                    {
                        Kind = FileIncludeKind.AutomaticTypeDirectiveFile,
                        TypeReference = _automaticTypeDirectiveNames[i],
                        PackageId = resolutions[i]?.ResolvedTypeReferenceDirective?.PackageId,
                    });
            }
            Tracing.Current?.Pop();
        }

        if (_rootNames.Count != 0 && !Js.IsTruthy(_options.NoLib))
        {
            // If '--lib' is not specified, include default library file according to '--target'
            // otherwise, using options specified in '--lib' instead of '--target' default library file
            var defaultLibraryFileName = _getDefaultLibraryFileName();
            if (_options.Lib is null || _options.Lib.Count == 0)
            {
                if (defaultLibraryFileName is not null)
                {
                    _libFiles.Add(ToPath(defaultLibraryFileName));
                    ProcessRootFile(defaultLibraryFileName, /*isDefaultLib*/ true, new LibFile { Kind = FileIncludeKind.LibFile });
                }
            }
            else
            {
                for (var index = 0; index < _options.Lib.Count; index++)
                {
                    ProcessRootFile(PathForLibFile(_options.Lib[index]), /*isDefaultLib*/ true, new LibFile { Kind = FileIncludeKind.LibFile, Index = index });
                }
            }
        }

        _files = Core.ToSorted(_processingDefaultLibFiles, CompareDefaultLibFiles).Concat(_processingOtherFiles!).ToList();
        _processingDefaultLibFiles = null;
        _processingOtherFiles = null;
        _filesWithReferencesProcessed = null;
    }

    /// <summary>ported from: src/compiler/program.ts:1812-1875 — the release of the old program's files and command
    /// lines.</summary>
    private void ReleaseOldProgram()
    {
        var oldProgram = _oldProgram;
        // PORT-NOTE: TS tests the optional host members (`host.onReleaseOldSourceFile`, `!host.getParsedCommandLine`,
        // `host.onReleaseParsedCommandLine`); the generated host interface declares all three required, so the port
        // tests the slots the generated members delegate to (the `*Func` properties, `Program.Host.cs`).
        if (oldProgram is not null && _host.OnReleaseOldSourceFileFunc is not null)
        {
            foreach (var oldSourceFile in oldProgram.GetSourceFiles())
            {
                var newFile = GetSourceFileByPath(oldSourceFile.ResolvedPath ?? "");
                if (_shouldCreateNewSourceFile
                    || newFile is null
                    || newFile.ImpliedNodeFormat != oldSourceFile.ImpliedNodeFormat
                    // old file wasn't redirect but new file is
                    || oldSourceFile.ResolvedPath == oldSourceFile.Path && newFile.ResolvedPath != oldSourceFile.Path)
                {
                    _host.OnReleaseOldSourceFile(oldSourceFile, oldProgram.GetCompilerOptions(), GetSourceFileByPath(oldSourceFile.Path ?? "") is not null, newFile);
                }
            }

            if (_host.GetParsedCommandLineFunc is null)
            {
                oldProgram.ForEachResolvedProjectReference(resolvedProjectReference =>
                {
                    if (resolvedProjectReference?.SourceFile is not null && GetResolvedProjectReferenceByPath(resolvedProjectReference.SourceFile.Path ?? "") is null)
                    {
                        _host.OnReleaseOldSourceFile(resolvedProjectReference.SourceFile, oldProgram.GetCompilerOptions(), /*hasSourceFileByPath*/ false, /*newSourceFileByResolvedPath*/ null);
                    }

                    return false;
                });
            }
        }

        // Release commandlines that new program does not use
        if (oldProgram is not null && _host.OnReleaseParsedCommandLineFunc is not null)
        {
            Utilities.ForEachProjectReference(
                oldProgram.GetProjectReferences(),
                oldProgram.GetResolvedProjectReferences(),
                (oldResolvedRef, parent, index) =>
                {
                    // The reference asserts both sides non-null (`projectReferences![index] || oldProgram!.getProjectReferences()![index]`).
                    var oldReference = parent?.CommandLine?.ProjectReferences?[index] ?? oldProgram.GetProjectReferences()![index];
                    var oldRefPath = ResolveProjectReferencePath(oldReference);
                    if (_projectReferenceRedirects is null || !_projectReferenceRedirects.ContainsKey(ToPath(oldRefPath)))
                    {
                        _host.OnReleaseParsedCommandLine(oldRefPath, oldResolvedRef, oldProgram.GetCompilerOptions());
                    }
                });
        }
    }

    /// <summary>ported from: src/compiler/program.ts:2676-2678 — <c>isSourceFileFromExternalLibrary</c>.</summary>
    internal bool IsSourceFileFromExternalLibrary(SourceFile file)
        => _sourceFilesFoundSearchingNodeModules.GetValueOrDefault(file.Path ?? "");

    /// <summary>ported from: src/compiler/program.ts:2680-2682 — <c>isSourceFileDefaultLibrary</c>.</summary>
    /// <remarks>
    /// The reference is `libFiles.has(file.path)`: the set of library paths the three `libFiles.add(…)` call sites fill
    /// (program.ts:1816 — the program's own default-library pass, :2559 — the reuse path, :3831 — a resolved
    /// `libReplacement`), each with a `toPath` result. The port therefore tests the source file's own <c>Path</c> slot
    /// (the reference's <c>file.path</c>) against <see cref="_libFiles"/> and canonicalizes nothing here.
    /// <para>
    /// PORT-NOTE: the previous body instead asked <c>GetSourceFileFromReference(file, new FileReference(file.FileName))</c>
    /// and re-entered itself with the answer, which is unbounded recursion on any file that is not already in the set —
    /// and it also answered the wrong question (a file that *resolves* to a library file is not that library file). Both
    /// behaviours are gone; the reference has one expression here.
    /// </para>
    /// </remarks>
    internal bool IsSourceFileDefaultLibrary(SourceFile file)
        => _libFiles.Contains(file.Path ?? "");

    /// <summary>ported from: src/compiler/program.ts:1594-1600 — the <c>ResolvedModuleFull</c> realignment helper.</summary>
    /// <remarks>PORT-NOTE: the generated host slot <c>getModuleResolutionCache()</c> is <c>object?</c> (the generator
    /// has no type for <c>ModuleResolutionCache</c>); this is the one cast, named so the reason is visible.</remarks>
    private static ModuleResolutionCache? CastModuleResolutionCache(object? cache) => (ModuleResolutionCache?)cache;

    /// <summary>ported from: src/compiler/program.ts:1081-1102 — the <c>FileReference[]</c> half of the
    /// <c>resolveTypeReferenceDirectives</c> arm.</summary>
    private static List<string> GetTypeReferenceResolutionNames(List<IFileReference> refs)
        => refs.Select(@ref => @ref.FileName ?? "").ToList();
}
