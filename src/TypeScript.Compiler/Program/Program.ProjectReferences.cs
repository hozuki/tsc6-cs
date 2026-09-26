namespace TypeScript.Compiler;

// ported from: src/compiler/program.ts:2174-2317 (resolveModuleNamesReusingOldState, resolveTypeReferenceDirectiveNamesReusingOldState,
//              ResolveNamesReusingOldStateInput, resolveNamesReusingOldState)
// ported from: src/compiler/program.ts:2319-2344 (canReuseProjectReferences)
// ported from: src/compiler/program.ts:2345-2597 (tryReuseStructureFromOldProgram)
// ported from: src/compiler/program.ts:3734-3759 (getRedirectFromSourceFile, forEachResolvedProjectReference, getRedirectFromOutput,
//              isSourceOfProjectReferenceRedirect, getResolvedProjectReferenceByPath)
// ported from: src/compiler/program.ts:3999-4074 (parseProjectReferenceConfigFile)
// ported from: src/compiler/program.ts:4881-5056 (HostForUseSourceOfProjectReferenceRedirect,
//              updateHostForUseSourceOfProjectReferenceRedirect)
// ported from: src/compiler/program.ts:5128-5130 (resolveProjectReferencePath)
// ported from: src/compiler/utilities.ts:710-729 (changesAffectModuleResolution … optionsHaveChanges)
// ported from: src/compiler/utilities.ts:742-771 (forEachEntry, forEachKey)
// ported from: src/compiler/utilities.ts:803-831 (projectReferenceIsEqualTo, moduleResolutionIsEqualTo, the two getResolved*FromResolution)
// ported from: src/compiler/utilities.ts:898-900 (packageIdIsEqual), 919-947 (typeDirectiveIsEqualTo, hasChangesInResolutions)
// ported from: src/compiler/utilities.ts:9452-9601 (SymlinkedDirectory, SymlinkCache, createSymlinkCache, guessDirectorySymlink,
//              isNodeModulesOrScopedPackageDirectory), 10803-10806 (containsIgnoredPath)
// ported from: src/compiler/utilities.ts:12236-12300 (forEachResolvedProjectReference, forEachProjectReference)
// ported from: src/compiler/commandLineParser.ts:1713 (optionsAffectingProgramStructure, as the filtered name list)
/// <summary>
/// The project-reference half of <c>program.ts</c>: the old program's structure reuse
/// (<c>tryReuseStructureFromOldProgram</c> and the resolution-reuse worker), the project-reference redirect lookups,
/// <c>parseProjectReferenceConfigFile</c>, the source-of-project-reference host wrapper and the module-level helpers
/// the reference keeps in <c>utilities.ts</c> for these paths.
/// </summary>
/// <remarks>
/// PORT-NOTE: the two <c>program.ts</c> object literals that this area needs become classes here, because a
/// <c>Program</c> method initializes both with an object initializer: <see cref="ResolvedProjectReference"/> (the
/// <c>{ commandLine, sourceFile }</c> literal <c>parseProjectReferenceConfigFile</c> builds and
/// <c>ProjectReferenceRedirect</c> wraps) and <see cref="ResolvedRefAndOutputDts"/>/<see cref="ResolvedRefAndSource"/>
/// (the two redirect-map values). The generated interfaces exist but nothing constructs them.
/// <para>
/// PORT-NOTE: <c>resolveNamesReusingOldState</c> is the one member whose TS shape cannot be transliterated: TS types
/// its input with four type parameters and a structural <c>ResolutionWithResolvedFileName</c> result. The port keeps
/// the input object (<see cref="ResolveNamesReusingOldStateInput{TEntry,TFile,TResolution}"/>) but carries the
/// resolved-name view as a tuple (see the input's own note), and records the "is this the module-names worker" test
/// (<c>resolutionWorker === resolveModuleNamesWorker</c>, program.ts:2246) as an explicit flag instead of a delegate
/// identity test, since the port's worker is a lambda.
/// </para>
/// <para>
/// PORT-NOTE: TS's optional host members stop being optional in the generated interfaces, so the guards the
/// reference writes around <c>host.getSourceFileByPath</c> (program.ts:2395), <c>host.realpath</c>,
/// <c>compilerHost.directoryExists</c>, <c>compilerHost.getDirectories</c> and <c>directoryExists</c>
/// (program.ts:4897-4927) are gone — the port calls the member and a host that cannot answer must answer the
/// reference's default (the same rule <c>Program/ModuleNameResolver.cs</c> records).
/// </para>
/// <para>
/// PORT-NOTE: <c>updateHostForUseSourceOfProjectReferenceRedirect</c> is TS's one place that *mutates the host*
/// (<c>host.compilerHost.fileExists = …</c>, <c>… directoryExists</c>, <c>… getDirectories</c>, <c>… realpath</c>,
/// program.ts:4897-4927). A C# interface's method cannot be reassigned, and a decorator implementing
/// <see cref="ICompilerHost"/> would have to forward ~40 members for behavior only reachable in
/// source-of-project-reference-redirect mode; the port therefore returns the wrappers (which
/// <c>Program.FileExists</c>/<c>Program.DirectoryExists</c> expose) and leaves the host object untouched. The
/// deviation is loud: in that mode the host's own <c>fileExists</c>/<c>directoryExists</c>/<c>getDirectories</c>/
/// <c>realpath</c> do not see the redirect-aware wrappers, so module resolution through the raw host cannot observe a
/// referenced project's not-yet-built <c>.d.ts</c>. Recorded in the M6 report.
/// </para>
/// <para>
/// PORT-NOTE: the reference <em>does</em> still need the mixed state's fields (<c>_filesByName</c>,
/// <c>_missingFileNames</c>, <c>_packageMap</c>, …) of the *old* program; reading them off
/// <c>_oldProgram</c> is the same object the interface's accessors return, and the port says so at each use rather
/// than going through the generated <c>object?</c>-shaped accessors.
/// </para>
/// </remarks>
internal sealed partial class Program
{

    // ---------------------------------------------------------------------------------------------
    // Reusing the old program's module/type-reference resolutions (program.ts:2174-2317)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/program.ts:2174-2189 — <c>resolveModuleNamesReusingOldState</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS passes <c>moduleResolutionNameAndModeGetter</c> (program.ts:1043, the host slice's singleton);
    /// the port passes it too, and it is also the input's <c>NameAndModeGetter</c>.
    /// </remarks>
    internal List<IResolvedModuleWithFailedLookupLocations> ResolveModuleNamesReusingOldState(List<LiteralExpression> moduleNames, SourceFile containingFile)
        => ResolveNamesReusingOldState(new ResolveNamesReusingOldStateInput<LiteralExpression, SourceFile, IResolvedModuleWithFailedLookupLocations>
        {
            Entries = moduleNames,
            ContainingFile = containingFile,
            ContainingSourceFile = containingFile,
            RedirectedReference = GetRedirectReferenceForResolution(containingFile),
            // PORT-NOTE: `moduleResolutionNameAndModeGetter` (program.ts:1043-1052): getName is
            // `getModuleResolutionName` (program.ts:1035-1037, the literal's text), getMode is the 3-arg
            // `getModeForUsageLocation` (program.ts:932-934).
            GetName = static entry => entry.Text ?? "",
            GetMode = static (entry, file, compilerOptions) => GetModeForUsageLocation(file, entry, compilerOptions),
            // PORT-NOTE: TS passes `resolveModuleNamesWorker` itself, whose containing-file parameter is the input's
            // `containingFile`; the port's delegate captures the same typed value (the worker's second argument is the
            // input's, so re-passing it would only need the `object` → `SourceFile` cast back).
            ResolutionWorker = (entries, _, reusedNames) => ResolveModuleNamesWorker(entries, containingFile, reusedNames),
            GetResolutionFromOldProgram = (name, mode) => _oldProgram?.GetResolvedModule(containingFile, name, mode),
            GetResolved = static resolution =>
                Utilities.GetResolvedModuleFromResolution(resolution) is { } resolved
                    ? (resolved.ResolvedFileName, resolved.PackageId)
                    : null,
            CanReuseResolutionsInFile = () =>
                ReferenceEquals(containingFile, _oldProgram?.GetSourceFile(containingFile.FileName ?? ""))
                && !_hasInvalidatedResolutions(containingFile.Path ?? ""),
            ResolveToOwnAmbientModule = true,
            ResolutionWorkerIsModuleNamesWorker = true,
        });

    /// <summary>ported from: src/compiler/program.ts:2191-2192 — the <c>FileReference[]</c> overload of
    /// <c>resolveTypeReferenceDirectiveNamesReusingOldState</c>.</summary>
    internal List<IResolvedTypeReferenceDirectiveWithFailedLookupLocations> ResolveTypeReferenceDirectiveNamesReusingOldState(List<IFileReference> typeDirectiveNames, SourceFile containingFile)
        => ResolveTypeReferenceDirectiveNamesReusingOldState(
            typeDirectiveNames,
            containingFile,
            containingFile,
            (entries, reusedNames) => ResolveTypeReferenceDirectiveNamesWorker(entries, containingFile, reusedNames));

    /// <summary>ported from: src/compiler/program.ts:2192 — the <c>string[]</c> overload of
    /// <c>resolveTypeReferenceDirectiveNamesReusingOldState</c>.</summary>
    internal List<IResolvedTypeReferenceDirectiveWithFailedLookupLocations> ResolveTypeReferenceDirectiveNamesReusingOldState(List<string> typeDirectiveNames, string containingFile)
        => ResolveTypeReferenceDirectiveNamesReusingOldState(
            typeDirectiveNames,
            /*containingSourceFile*/ null,
            containingFile,
            (entries, reusedNames) => ResolveTypeReferenceDirectiveNamesWorker(entries, containingFile, reusedNames));

    /// <summary>ported from: src/compiler/program.ts:2193-2213 — the implementation of
    /// <c>resolveTypeReferenceDirectiveNamesReusingOldState</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference spells the two overloads as one generic function whose containing file is
    /// <c>string | SourceFile</c> and derives <c>containingSourceFile</c> with <c>isString</c>; the port takes the
    /// SourceFile (or its absence) as a parameter, which is the same value, and keeps the containing file itself as
    /// the input's <c>object</c> slot for the trace text.
    /// </remarks>
    private List<IResolvedTypeReferenceDirectiveWithFailedLookupLocations> ResolveTypeReferenceDirectiveNamesReusingOldState<TEntry>(
        List<TEntry> typeDirectiveNames,
        SourceFile? containingSourceFile,
        object containingFile,
        Func<List<TEntry>, List<TEntry>?, List<IResolvedTypeReferenceDirectiveWithFailedLookupLocations>> resolutionWorker)
        => ResolveNamesReusingOldState(new ResolveNamesReusingOldStateInput<TEntry, SourceFile?, IResolvedTypeReferenceDirectiveWithFailedLookupLocations>
        {
            Entries = typeDirectiveNames,
            ContainingFile = containingFile,
            ContainingSourceFile = containingSourceFile,
            RedirectedReference = containingSourceFile is not null ? GetRedirectReferenceForResolution(containingSourceFile) : null,
            // PORT-NOTE: `typeReferenceResolutionNameAndModeGetter` (program.ts:1075-1078): getName is
            // `getTypeReferenceResolutionName` (program.ts:1071-1073, the host slice's), getMode is
            // `getModeForFileReference(entry, file && getDefaultResolutionModeForFileWorker(file, compilerOptions))`
            // (program.ts:857-859, 4878-4880), which the port spells out for the absent-file case.
            GetName = static entry => GetTypeReferenceResolutionName(entry),
            GetMode = static (entry, file, compilerOptions) =>
                GetModeForFileReference(entry, file is not null ? GetDefaultResolutionModeForFileWorker(file, compilerOptions) : null),
            // PORT-NOTE: TS's `resolutionWorker: resolveTypeReferenceDirectiveNamesWorker` is one generic function over
            // `T extends FileReference | string`; the port's worker has one overload per entry type, so the two
            // overloads above pass the matching one in (the delegate closes over the same typed containing file the
            // input carries).
            ResolutionWorker = (entries, _, reusedNames) => resolutionWorker(entries, reusedNames),
            GetResolutionFromOldProgram = containingSourceFile is not null
                ? (name, mode) => _oldProgram?.GetResolvedTypeReferenceDirective(containingSourceFile, name, mode)
                : (name, mode) => _oldProgram?._automaticTypeDirectiveResolutions?.Get(name, mode),
            GetResolved = static resolution =>
                Utilities.GetResolvedTypeReferenceDirectiveFromResolution(resolution) is { } resolved
                    ? (resolved.ResolvedFileName, resolved.PackageId)
                    : null,
            CanReuseResolutionsInFile = containingSourceFile is not null
                ? () =>
                    ReferenceEquals(containingSourceFile, _oldProgram?.GetSourceFile(containingSourceFile.FileName ?? ""))
                    && !_hasInvalidatedResolutions(containingSourceFile.Path ?? "")
                : () => !_hasInvalidatedResolutions(ToPath((string)containingFile)),
        });

    /// <summary>ported from: src/compiler/program.ts:2214-2230 — <c>ResolveNamesReusingOldStateInput</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's four type parameters are <c>Entry, SourceFileOrString, SourceFileOrUndefined, Resolution</c>;
    /// the port keeps <c>Entry</c>, the getter's file type and <c>Resolution</c>, and types the containing file as
    /// <c>object</c> (the two shapes the reference unites) because the port's input carries a delegate for the worker
    /// rather than the worker's signature.
    /// <para>
    /// PORT-NOTE: <c>getResolved</c> returns TS's structural <c>ResolutionWithResolvedFileName</c>
    /// (utilities.ts:1013-1016: <c>resolvedFileName</c> + <c>packageId</c>) or undefined. The port's
    /// <c>ResolvedModuleFull</c>/<c>ResolvedTypeReferenceDirective</c> do not implement the generated
    /// <c>IResolutionWithResolvedFileName</c>, so the input carries the two fields the body reads as a nullable tuple:
    /// a null tuple is the reference's undefined, and every other state of the reference's object is preserved (the
    /// body reads <c>resolvedFileName</c> and <c>packageId</c> and nothing else).
    /// </para>
    /// </remarks>
    private sealed class ResolveNamesReusingOldStateInput<TEntry, TFile, TResolution>
        where TFile : class?
        where TResolution : class
    {
        /// <summary>ported from: src/compiler/program.ts:2215 — <c>entries</c>.</summary>
        internal required List<TEntry> Entries { get; init; }

        /// <summary>ported from: src/compiler/program.ts:2216 — <c>containingFile</c>.</summary>
        internal required object ContainingFile { get; init; }

        /// <summary>ported from: src/compiler/program.ts:2217 — <c>containingSourceFile</c>.</summary>
        internal TFile? ContainingSourceFile { get; init; }

        /// <summary>ported from: src/compiler/program.ts:2218 — <c>redirectedReference</c>.</summary>
        internal IResolvedProjectReference? RedirectedReference { get; init; }

        /// <summary>ported from: src/compiler/program.ts:2219 — <c>nameAndModeGetter</c>'s <c>getName</c>.</summary>
        /// <remarks>PORT-NOTE: TS passes <c>moduleResolutionNameAndModeGetter</c>/<c>typeReferenceResolutionNameAndModeGetter</c>
        /// (program.ts:1043-1081, the host slice's <c>IResolutionNameAndModeGetter</c> objects). The port passes the two
        /// members those objects carry — the same functions — as delegates, so this file depends on no declaration of
        /// that interface (<c>Program/Program.Host.cs</c>).</remarks>
        internal required Func<TEntry, string> GetName { get; init; }

        /// <summary>ported from: src/compiler/program.ts:2219 — <c>nameAndModeGetter</c>'s <c>getMode</c>.</summary>
        internal required Func<TEntry, TFile?, ICompilerOptions?, ModuleKind?> GetMode { get; init; }

        /// <summary>ported from: src/compiler/program.ts:2220-2224 — <c>resolutionWorker</c>.</summary>
        internal required Func<List<TEntry>, object, List<TEntry>?, List<TResolution>> ResolutionWorker { get; init; }

        /// <summary>ported from: src/compiler/program.ts:2225 — <c>getResolutionFromOldProgram</c>.</summary>
        internal required Func<string, ModuleKind?, TResolution?> GetResolutionFromOldProgram { get; init; }

        /// <summary>ported from: src/compiler/program.ts:2226 — <c>getResolved</c>.</summary>
        internal required Func<TResolution, (string? ResolvedFileName, IPackageId? PackageId)?> GetResolved { get; init; }

        /// <summary>ported from: src/compiler/program.ts:2227 — <c>canReuseResolutionsInFile</c>.</summary>
        internal required Func<bool> CanReuseResolutionsInFile { get; init; }

        /// <summary>ported from: src/compiler/program.ts:2228 — <c>resolveToOwnAmbientModule</c>.</summary>
        internal bool ResolveToOwnAmbientModule { get; init; }

        /// <summary>ported from: src/compiler/program.ts:2246 — the <c>resolutionWorker === resolveModuleNamesWorker</c>
        /// test, which selects the reusing-resolution trace's wording.</summary>
        /// <remarks>PORT-NOTE: TS distinguishes the two callers by function identity; the port's workers are lambdas, so
        /// the discrimination is an explicit input field (set by <c>resolveModuleNamesReusingOldState</c> only).</remarks>
        internal bool ResolutionWorkerIsModuleNamesWorker { get; init; }
    }

    /// <summary>ported from: src/compiler/program.ts:2231-2317 — <c>resolveNamesReusingOldState</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS builds <c>result</c> with <c>new Array(entries.length)</c> and leaves the slots it has not reached
    /// yet as holes. Every index is written exactly once — reused from the old program, the ambient-module
    /// <c>emptyResolution</c>, or the resolution worker below — so the port pre-sizes a list whose placeholder slots
    /// are null and asserts the invariant instead of leaking a null: the returned list is TS's fully-populated array.
    /// The one shared <c>emptyArray</c> (core.ts:17) is kept as <see cref="EmptyArray{TResolution}"/>, since C# needs
    /// it per element type.
    /// </remarks>
    private List<TResolution> ResolveNamesReusingOldState<TEntry, TFile, TResolution>(ResolveNamesReusingOldStateInput<TEntry, TFile, TResolution> input)
        where TFile : class?
        where TResolution : class
    {
        if (input.Entries.Count == 0)
        {
            return EmptyArray<TResolution>.Value;
        }

        if (_structureIsReused == StructureIsReused.Not
            && (!input.ResolveToOwnAmbientModule
                || !(input.ContainingSourceFile is SourceFile ambientFile && ambientFile.AmbientModuleNames?.Count > 0)))
        {
            // If the old program state does not permit reusing resolutions and `file` does not contain locally defined ambient modules,
            // the best we can do is fallback to the default logic.
            return input.ResolutionWorker(input.Entries, input.ContainingFile, /*reusedNames*/ null);
        }

        /** An ordered list of module names for which we cannot recover the resolution. */
        List<TEntry>? unknownEntries = null;
        List<int>? unknownEntryIndices = null;
        List<TResolution>? result = null;
        List<TEntry>? reusedNames = null;
        var reuseResolutions = input.CanReuseResolutionsInFile();
        for (var i = 0; i < input.Entries.Count; i++)
        {
            var entry = input.Entries[i];
            if (reuseResolutions)
            {
                var name = input.GetName(entry);
                var mode = input.GetMode(entry, input.ContainingSourceFile, input.RedirectedReference?.CommandLine?.Options ?? _options);
                var oldResolution = input.GetResolutionFromOldProgram(name, mode);
                var oldResolved = oldResolution is null ? null : input.GetResolved(oldResolution);
                if (oldResolved is not null)
                {
                    if (ModuleNameResolver.IsTraceEnabled(_options, _host))
                    {
                        ModuleNameResolver.Trace(
                            _host,
                            input.ResolutionWorkerIsModuleNamesWorker
                                ? oldResolved.Value.PackageId is not null
                                    ? Diagnostics.Reusing_resolution_of_module_0_from_1_of_old_program_it_was_successfully_resolved_to_2_with_Package_ID_3
                                    : Diagnostics.Reusing_resolution_of_module_0_from_1_of_old_program_it_was_successfully_resolved_to_2
                                : oldResolved.Value.PackageId is not null
                                    ? Diagnostics.Reusing_resolution_of_type_reference_directive_0_from_1_of_old_program_it_was_successfully_resolved_to_2_with_Package_ID_3
                                    : Diagnostics.Reusing_resolution_of_type_reference_directive_0_from_1_of_old_program_it_was_successfully_resolved_to_2,
                            name,
                            input.ContainingSourceFile is SourceFile containingFile
                                ? Paths.GetNormalizedAbsolutePath(containingFile.OriginalFileName ?? "", _currentDirectory)
                                : input.ContainingFile,
                            oldResolved.Value.ResolvedFileName,
                            oldResolved.Value.PackageId is not null ? Utilities.PackageIdToString(oldResolved.Value.PackageId) : null);
                    }

                    InitializeResult(ref result, input.Entries.Count);
                    result![i] = oldResolution!;
                    (reusedNames ??= []).Add(entry);
                    continue;
                }
            }

            if (input.ResolveToOwnAmbientModule)
            {
                var name = input.GetName(entry);
                // We know moduleName resolves to an ambient module provided that moduleName:
                // - is in the list of ambient modules locally declared in the current source file.
                if (input.ContainingSourceFile is SourceFile sourceFile && Core.Contains(sourceFile.AmbientModuleNames, name))
                {
                    if (ModuleNameResolver.IsTraceEnabled(_options, _host))
                    {
                        ModuleNameResolver.Trace(
                            _host,
                            Diagnostics.Module_0_was_resolved_as_locally_declared_ambient_module_in_file_1,
                            name,
                            Paths.GetNormalizedAbsolutePath(sourceFile.OriginalFileName ?? "", _currentDirectory));
                    }

                    InitializeResult(ref result, input.Entries.Count);
                    result![i] = (TResolution)(object)EmptyResolution;
                    continue;
                }
            }

            // Resolution failed in the old program, or resolved to an ambient module for which we can't reuse the result.
            (unknownEntries ??= []).Add(entry);
            (unknownEntryIndices ??= []).Add(i);
        }

        if (unknownEntries is null)
        {
            Debug.Assert(result is not null, "every entry was resolved or reused");
            return result;
        }

        var resolutions = input.ResolutionWorker(unknownEntries, input.ContainingFile, reusedNames);
        if (result is null)
        {
            return resolutions;
        }

        for (var index = 0; index < resolutions.Count; index++)
        {
            result[unknownEntryIndices![index]] = resolutions[index];
        }

        return result;
    }

    /// <summary>ported from: src/compiler/core.ts:17 — <c>emptyArray</c>.</summary>
    /// <remarks>PORT-NOTE: TS's one <c>never[]</c> is untyped at runtime; C# needs a list per element type, so the
    /// shared instance lives on a private generic holder (the same trick the checker's slices use for shared empty
    /// tables).</remarks>
    private static class EmptyArray<TResolution>
        where TResolution : class
    {
        internal static readonly List<TResolution> Value = [];
    }

    /// <summary>ported from: src/compiler/program.ts:2232-2316 — the <c>result ??= new Array(entries.length)</c>
    /// allocator.</summary>
    private static void InitializeResult<TResolution>(ref List<TResolution>? result, int count)
        where TResolution : class
    {
        if (result is not null)
        {
            return;
        }

        result = new List<TResolution>(count);
        for (var i = 0; i < count; i++)
        {
            result.Add(null!);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Structure reuse and the project references (program.ts:2319-2597)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/program.ts:2388-2391 — <c>SeenPackageName</c>.</summary>
    /// <remarks>PORT-NOTE: TS declares this const enum inside <c>tryReuseStructureFromOldProgram</c>; C# has no local
    /// enums, so it lives beside the method that uses it.</remarks>
    private enum SeenPackageName
    {
        Exists,
        Modified,
    }

    /// <summary>ported from: src/compiler/program.ts:2319-2344 — <c>canReuseProjectReferences</c>.</summary>
    /// <remarks>PORT-NOTE: TS's callbacks return a boolean and the walk's result is read for truthiness; the port's
    /// <c>Utilities.ForEachProjectReference&lt;T&gt;</c> constrains <c>T</c> to a reference type
    /// (<c>Program/Program.ProgramDiagnostics.cs:1154</c>), so the two callbacks return boxed <c>true</c>/null and the
    /// result is read with <c>Js.IsTruthy</c>, which is the same test <c>!forEachProjectReference(…)</c> makes.</remarks>
    private bool CanReuseProjectReferences()
    {
        object? changed = Utilities.ForEachProjectReference<object>(
            _oldProgram!._projectReferences,
            _oldProgram!._resolvedProjectReferences,
            (oldResolvedRef, parent, index) =>
            {
                var newRef = (parent is not null ? parent.CommandLine!.ProjectReferences : _projectReferences)![index];
                var newResolvedRef = ParseProjectReferenceConfigFile(newRef);
                if (oldResolvedRef is not null)
                {
                    // Resolved project reference has gone missing or changed
                    return !(newResolvedRef is not null
                        && ReferenceEquals(newResolvedRef.SourceFile, oldResolvedRef.SourceFile)
                        && Core.ArrayIsEqualTo(oldResolvedRef.CommandLine!.FileNames, newResolvedRef.CommandLine!.FileNames))
                        ? (object)true
                        : null;
                }

                // A previously-unresolved reference may be resolved now
                return newResolvedRef is not null ? true : null;
            },
            (oldProjectReferences, parent) =>
            {
                // If array of references is changed, we cant resue old program
                var newReferences = parent is not null
                    ? GetResolvedProjectReferenceByPath(parent.SourceFile!.Path ?? "")!.CommandLine!.ProjectReferences
                    : _projectReferences;
                return !Core.ArrayIsEqualTo(oldProjectReferences, newReferences, (a, b, _) => Utilities.ProjectReferenceIsEqualTo(a, b))
                    ? (object)true
                    : null;
            });
        return !Js.IsTruthy(changed);
    }

    /// <summary>ported from: src/compiler/program.ts:2345-2597 — <c>tryReuseStructureFromOldProgram</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference assigns its closure's <c>structureIsReused</c> only from program.ts:2374 on, and every
    /// earlier <c>return StructureIsReused.Not</c> leaves that variable <c>undefined</c> — which is what
    /// <c>resolveNamesReusingOldState</c> (program.ts:2244) and <c>pathForLibFile</c> (program.ts:3850) read. The port's
    /// field is therefore <c>StructureIsReused?</c> and this method writes it exactly where the reference writes its
    /// variable, leaving it null on the early returns.
    /// <para>
    /// PORT-NOTE: the old program's state is read off <c>_oldProgram</c>'s own fields
    /// (<c>_files</c>, <c>_missingFileNames</c>, <c>_filesByName</c>, <c>_packageMap</c>, …), which the <c>Program</c>
    /// interface's accessors return verbatim; going through them would only re-spell the same object in a generated
    /// <c>object?</c> slot (see <c>getFilesByNameMap</c> in the generated <see cref="IProgram"/>).
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>oldSourceFile.impliedNodeFormat !== newSourceFile.impliedNodeFormat</c> is a
    /// <c>ResolutionMode</c> (<c>ModuleKind | undefined</c>) comparison; the generated slot is <c>object?</c>, so the
    /// port compares boxed values with <see cref="object.Equals(object?, object?)"/>, which is TS's <c>!==</c> for the
    /// two <c>ModuleKind</c> cases.
    /// </para>
    /// </remarks>
    internal StructureIsReused TryReuseStructureFromOldProgram()
    {
        if (_oldProgram is null)
        {
            return StructureIsReused.Not;
        }

        // check properties that can affect structure of the program or module resolution strategy
        // if any of these properties has changed - structure cannot be reused
        var oldOptions = _oldProgram._options;
        if (Utilities.ChangesAffectModuleResolution(oldOptions, _options))
        {
            return StructureIsReused.Not;
        }

        // there is an old program, check if we can reuse its structure
        var oldRootNames = _oldProgram._rootNames;
        if (!Core.ArrayIsEqualTo(oldRootNames, _rootNames))
        {
            return StructureIsReused.Not;
        }

        // Check if any referenced project tsconfig files are different
        if (!CanReuseProjectReferences())
        {
            return StructureIsReused.Not;
        }
        if (_projectReferences is not null)
        {
            _resolvedProjectReferences = _projectReferences.Select(ParseProjectReferenceConfigFile).ToList();
        }

        // check if program source files has changed in the way that can affect structure of the program
        var newSourceFiles = new List<SourceFile>();
        var modifiedSourceFiles = new List<SourceFile>();
        _structureIsReused = StructureIsReused.Completely;

        // If the missing file paths are now present, it can change the progam structure,
        // and hence cant reuse the structure.
        // This is same as how we dont reuse the structure if one of the file from old program is now missing
        if (Utilities.ForEachEntry(_oldProgram._missingFileNames, (missingFileName, _) => _host.FileExists(missingFileName)) is true)
        {
            return StructureIsReused.Not;
        }

        var oldSourceFiles = _oldProgram._files;
        var seenPackageNames = new Dictionary<string, SeenPackageName>();

        foreach (var oldSourceFile in oldSourceFiles)
        {
            var sourceFileOptions = GetCreateSourceFileOptions(oldSourceFile.FileName ?? "", _moduleResolutionCache);
            // PORT-NOTE: TS guards `host.getSourceFileByPath ? … : host.getSourceFile(…)`; the generated interface
            // declares both required, so the port calls the by-path one (the reference's own preference).
            var newSourceFile = _host.GetSourceFileByPath(
                oldSourceFile.FileName,
                oldSourceFile.ResolvedPath,
                sourceFileOptions,
                /*onError*/ null,
                _shouldCreateNewSourceFile);

            if (newSourceFile is null)
            {
                return StructureIsReused.Not;
            }
            newSourceFile.PackageJsonLocations = sourceFileOptions.PackageJsonLocations is { Count: > 0 } ? sourceFileOptions.PackageJsonLocations : null;
            newSourceFile.PackageJsonScope = sourceFileOptions.PackageJsonScope;

            Debug.Assert(newSourceFile.RedirectInfo is null, "Host should not return a redirect source file from `getSourceFile`");

            bool fileChanged;
            if (oldSourceFile.RedirectInfo is not null)
            {
                // We got `newSourceFile` by path, so it is actually for the unredirected file.
                // This lets us know if the unredirected file has changed. If it has we should break the redirect.
                if (!ReferenceEquals(newSourceFile, oldSourceFile.RedirectInfo.Unredirected))
                {
                    // Underlying file has changed. Might not redirect anymore. Must rebuild program.
                    return StructureIsReused.Not;
                }
                fileChanged = false;
                newSourceFile = oldSourceFile; // Use the redirect.
            }
            else if (_oldProgram._redirectTargetsMap.ContainsKey(oldSourceFile.Path ?? ""))
            {
                // If a redirected-to source file changes, the redirect may be broken.
                if (!ReferenceEquals(newSourceFile, oldSourceFile))
                {
                    return StructureIsReused.Not;
                }
                fileChanged = false;
            }
            else
            {
                fileChanged = !ReferenceEquals(newSourceFile, oldSourceFile);
            }

            // Since the project references havent changed, its right to set originalFileName and resolvedPath here
            newSourceFile.Path = oldSourceFile.Path;
            newSourceFile.OriginalFileName = oldSourceFile.OriginalFileName;
            newSourceFile.ResolvedPath = oldSourceFile.ResolvedPath;
            newSourceFile.FileName = oldSourceFile.FileName;

            var packageName = _oldProgram._sourceFileToPackageName.GetValueOrDefault(oldSourceFile.Path ?? "");
            if (packageName is not null)
            {
                // If there are 2 different source files for the same package name and at least one of them changes,
                // they might become redirects. So we must rebuild the program.
                var prevKind = seenPackageNames.TryGetValue(packageName, out var prev) ? prev : (SeenPackageName?)null;
                var newKind = fileChanged ? SeenPackageName.Modified : SeenPackageName.Exists;
                if ((prevKind is not null && newKind == SeenPackageName.Modified) || prevKind == SeenPackageName.Modified)
                {
                    return StructureIsReused.Not;
                }
                seenPackageNames[packageName] = newKind;
            }

            if (fileChanged)
            {
                if (!Equals(oldSourceFile.ImpliedNodeFormat, newSourceFile.ImpliedNodeFormat))
                {
                    _structureIsReused = StructureIsReused.SafeModules;
                }
                // The `newSourceFile` object was created for the new program.
                else if (!Core.ArrayIsEqualTo(oldSourceFile.LibReferenceDirectives, newSourceFile.LibReferenceDirectives, (a, b, _) => FileReferenceIsEqualTo(a, b)))
                {
                    // 'lib' references has changed. Matches behavior in changesAffectModuleResolution
                    _structureIsReused = StructureIsReused.SafeModules;
                }
                // check tripleslash references
                else if (!Core.ArrayIsEqualTo(oldSourceFile.ReferencedFiles, newSourceFile.ReferencedFiles, (a, b, _) => FileReferenceIsEqualTo(a, b)))
                {
                    // tripleslash references has changed
                    _structureIsReused = StructureIsReused.SafeModules;
                }
                else
                {
                    // check imports and module augmentations
                    CollectExternalModuleReferences(newSourceFile);
                    if (!Core.ArrayIsEqualTo(oldSourceFile.Imports, newSourceFile.Imports, (a, b, _) => ModuleNameIsEqualTo(a, b)))
                    {
                        // imports has changed
                        _structureIsReused = StructureIsReused.SafeModules;
                    }
                    else if (!Core.ArrayIsEqualTo(oldSourceFile.ModuleAugmentations, newSourceFile.ModuleAugmentations, (a, b, _) => ModuleNameIsEqualTo(a, b)))
                    {
                        // moduleAugmentations has changed
                        _structureIsReused = StructureIsReused.SafeModules;
                    }
                    else if ((oldSourceFile.Flags & NodeFlags.PermanentlySetIncrementalFlags) != (newSourceFile.Flags & NodeFlags.PermanentlySetIncrementalFlags))
                    {
                        // dynamicImport has changed
                        _structureIsReused = StructureIsReused.SafeModules;
                    }
                    else if (!Core.ArrayIsEqualTo(oldSourceFile.TypeReferenceDirectives, newSourceFile.TypeReferenceDirectives, (a, b, _) => FileReferenceIsEqualTo(a, b)))
                    {
                        // 'types' references has changed
                        _structureIsReused = StructureIsReused.SafeModules;
                    }
                }

                // tentatively approve the file
                modifiedSourceFiles.Add(newSourceFile);
            }
            else if (_hasInvalidatedResolutions(oldSourceFile.Path ?? ""))
            {
                // 'module/types' references could have changed
                _structureIsReused = StructureIsReused.SafeModules;

                // add file to the modified list so that we will resolve it later
                modifiedSourceFiles.Add(newSourceFile);
            }

            // if file has passed all checks it should be safe to reuse it
            newSourceFiles.Add(newSourceFile);
        }

        if (_structureIsReused != StructureIsReused.Completely)
        {
            return _structureIsReused!.Value;
        }

        // try to verify results of module resolution
        foreach (var newSourceFile in modifiedSourceFiles)
        {
            var moduleNames = GetModuleNames(newSourceFile);
            var resolutions = ResolveModuleNamesReusingOldState(moduleNames, newSourceFile);
            (_resolvedModulesProcessing ??= [])[newSourceFile.Path ?? ""] = resolutions;
            var optionsForFile = GetCompilerOptionsForFile(newSourceFile);
            // ensure that module resolution results are still correct
            var resolutionsChanged = Utilities.HasChangesInResolutions(
                moduleNames,
                resolutions,
                name => _oldProgram!.GetResolvedModule(newSourceFile, name.Text ?? "", GetModeForUsageLocationWorker(newSourceFile, name, optionsForFile)),
                static (oldResolution, newResolution) => Utilities.ModuleResolutionIsEqualTo(oldResolution, newResolution));
            if (resolutionsChanged) _structureIsReused = StructureIsReused.SafeModules;
            var typesReferenceDirectives = newSourceFile.TypeReferenceDirectives ?? [];
            var typeReferenceResolutions = ResolveTypeReferenceDirectiveNamesReusingOldState(typesReferenceDirectives, newSourceFile);
            (_resolvedTypeReferenceDirectiveNamesProcessing ??= [])[newSourceFile.Path ?? ""] = typeReferenceResolutions;
            // ensure that types resolutions are still correct
            var typeReferenceResolutionsChanged = Utilities.HasChangesInResolutions(
                typesReferenceDirectives,
                typeReferenceResolutions,
                name => _oldProgram!.GetResolvedTypeReferenceDirective(
                    newSourceFile,
                    GetTypeReferenceResolutionName(name),
                    GetModeForTypeReferenceDirectiveInFile(name, newSourceFile)),
                static (oldResolution, newResolution) => Utilities.TypeDirectiveIsEqualTo(oldResolution, newResolution));
            if (typeReferenceResolutionsChanged) _structureIsReused = StructureIsReused.SafeModules;
        }

        if (_structureIsReused != StructureIsReused.Completely)
        {
            return _structureIsReused!.Value;
        }

        if (Utilities.ChangesAffectingProgramStructure(oldOptions, _options))
        {
            return StructureIsReused.SafeModules;
        }

        if (_oldProgram._resolvedLibReferences is not null
            && Utilities.ForEachEntry(_oldProgram._resolvedLibReferences, (resolution, libFileName) => PathForLibFileWorker(libFileName).Actual != resolution.Actual) is true)
        {
            return StructureIsReused.SafeModules;
        }

        if (_host.HasChangedAutomaticTypeDirectiveNames is not null)
        {
            if (_host.HasChangedAutomaticTypeDirectiveNames()) return StructureIsReused.SafeModules;
        }
        else
        {
            _automaticTypeDirectiveNames = ModuleNameResolver.GetAutomaticTypeDirectiveNames(_options, _host);
            if (!Core.ArrayIsEqualTo(_oldProgram._automaticTypeDirectiveNames, _automaticTypeDirectiveNames)) return StructureIsReused.SafeModules;
        }
        _missingFileNames = _oldProgram._missingFileNames;

        // update fileName -> file mapping
        Debug.Assert(newSourceFiles.Count == _oldProgram._files.Count);
        foreach (var newSourceFile in newSourceFiles)
        {
            _filesByName[newSourceFile.Path ?? ""] = FileByNameEntry.Of(newSourceFile);
            if (_oldProgram.IsSourceFileDefaultLibrary(newSourceFile))
            {
                _libFiles.Add(newSourceFile.Path ?? "");
            }
        }
        var oldFilesByNameMap = _oldProgram._filesByName;
        foreach (var (path, oldFile) in oldFilesByNameMap)
        {
            var oldFileEntry = oldFile ?? FileByNameEntry.Unset;
            if (oldFileEntry.State != FileByNameState.File)
            {
                _filesByName[path] = oldFileEntry;
                continue;
            }
            if (oldFileEntry.File!.Path == path)
            {
                // Set the file as found during node modules search if it was found that way in old progra.m
                if (_oldProgram.IsSourceFileFromExternalLibrary(oldFileEntry.File))
                {
                    _sourceFilesFoundSearchingNodeModules[oldFileEntry.File.Path ?? ""] = true;
                }
                continue;
            }
            _filesByName[path] = _filesByName.GetValueOrDefault(oldFileEntry.File.Path ?? "") ?? FileByNameEntry.Unset;
        }

        var isConfigIdentical = (_oldProgram._options.ConfigFile is not null && ReferenceEquals(_oldProgram._options.ConfigFile, _options.ConfigFile))
            || (_oldProgram._options.ConfigFile is null && _options.ConfigFile is null
                && !AnyCompilerOptionChanged(_oldProgram._options, _options));
        _programDiagnostics.ReuseStateFromOldProgram(_oldProgram._programDiagnostics, isConfigIdentical);
        _skipVerifyCompilerOptions = isConfigIdentical;

        _files = newSourceFiles;
        _automaticTypeDirectiveNames = _oldProgram._automaticTypeDirectiveNames;
        _automaticTypeDirectiveResolutions = _oldProgram._automaticTypeDirectiveResolutions;

        _sourceFileToPackageName = _oldProgram._sourceFileToPackageName;
        _redirectTargetsMap = _oldProgram._redirectTargetsMap;
        _usesUriStyleNodeCoreModules = _oldProgram._usesUriStyleNodeCoreModules;
        _resolvedModules = _oldProgram._resolvedModules;
        _resolvedTypeReferenceDirectiveNames = _oldProgram._resolvedTypeReferenceDirectiveNames;
        _resolvedLibReferences = _oldProgram._resolvedLibReferences;
        _packageMap = _oldProgram._packageMap;

        return StructureIsReused.Completely;
    }

    // ---------------------------------------------------------------------------------------------
    // The project-reference redirect lookups (program.ts:3734-3759)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/program.ts:3734-3736 — <c>getRedirectFromSourceFile</c>.</summary>
    internal IResolvedRefAndOutputDts? GetRedirectFromSourceFile(string fileName)
        => _mapSourceFileToResolvedRef?.GetValueOrDefault(ToPath(fileName));

    /// <summary>ported from: src/compiler/program.ts:3738-3743 — <c>forEachResolvedProjectReference</c>.</summary>
    internal T? ForEachResolvedProjectReference<T>(Func<IResolvedProjectReference, T?> callback)
        => Utilities.ForEachResolvedProjectReference(_resolvedProjectReferences, callback);

    /// <summary>ported from: src/compiler/program.ts:3744-3746 — <c>getRedirectFromOutput</c>.</summary>
    internal IResolvedRefAndSource? GetRedirectFromOutput(string filePath)
        => _mapOutputFileToResolvedRef?.GetValueOrDefault(filePath);

    /// <summary>ported from: src/compiler/program.ts:3748-3750 — <c>isSourceOfProjectReferenceRedirect</c>.</summary>
    internal bool IsSourceOfProjectReferenceRedirect(string fileName)
        => _useSourceOfProjectReferenceRedirect && GetRedirectFromSourceFile(fileName) is not null;

    /// <summary>ported from: src/compiler/program.ts:3752-3759 — <c>getResolvedProjectReferenceByPath</c>.</summary>
    internal IResolvedProjectReference? GetResolvedProjectReferenceByPath(string projectReferencePath)
        => _projectReferenceRedirects?.GetValueOrDefault(projectReferencePath)?.ResolvedRef;

    // ---------------------------------------------------------------------------------------------
    // Reading a referenced project's config (program.ts:3999-4074)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:725-727 — the full <c>optionDeclarations</c> read of
    /// <c>optionsHaveChanges</c> (<c>isConfigIdentical</c>'s second clause).</summary>
    /// <remarks>PORT-NOTE: TS passes the whole <c>optionDeclarations</c> table (commandLineParser.ts, M8's). The port's
    /// equivalent of "did any compiler option change" is <c>Utilities.CompareDataObjects</c>
    /// (<c>Program/Program.Host.cs:2439</c>, the walk the host slice already uses for the reference's other
    /// <c>optionsHaveChanges</c> call site at program.ts:1475), with the reference's own
    /// <c>oldOptions !== newOptions</c> guard in front of it — that is <c>optionsHaveChanges</c>'s first test.</remarks>
    private static bool AnyCompilerOptionChanged(ICompilerOptions oldOptions, ICompilerOptions newOptions)
        => !ReferenceEquals(oldOptions, newOptions) && !Utilities.CompareDataObjects(oldOptions, newOptions);

    /// <summary>ported from: src/compiler/program.ts:3999-4074 — <c>parseProjectReferenceConfigFile</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's first arm (program.ts:4011-4023) is host.getParsedCommandLine's, which needs no
    /// tsconfig reader. The second arm (program.ts:4024-4032) parses the referenced config with
    /// <c>parseJsonSourceFileConfigFileContent</c> (<c>commandLineParser.ts:3539</c>, M8's tsconfig reader) — the port
    /// has no reader and no way to express "this host has no getParsedCommandLine" (the generated
    /// <see cref="ICompilerHost"/> declares the member required), so the port takes the first arm and treats a null
    /// result as the reference's own "resolved to nothing" case below. The second arm is an M8 dependency at
    /// program.ts:4024-4032; see the M6 report.
    /// <para>
    /// PORT-NOTE: <c>getOutputDeclarationFileName</c> lives in <c>emitter.ts</c> (M7) and stays an unresolved name in
    /// the port until that slice lands (the same treatment <c>Checker.CreateTypeChecker</c> gets);
    /// <c>getCommonSourceDirectoryOfConfig</c> is the M6-1 slice's <c>Program/Program.EmitBoundary.cs</c>. Both are
    /// called unqualified, as the reference calls them (a class named <c>Emit</c> is not addressable inside
    /// <c>Program</c>, whose <c>IProgram.Emit</c> member hides the name).
    /// </para>
    /// </remarks>
    private IResolvedProjectReference? ParseProjectReferenceConfigFile(IProjectReference @ref)
    {
        _projectReferenceRedirects ??= [];

        // The actual filename (i.e. add "/tsconfig.json" if necessary)
        var refPath = ResolveProjectReferencePath(@ref);
        var sourceFilePath = ToPath(refPath);
        if (_projectReferenceRedirects.TryGetValue(sourceFilePath, out var fromCache))
        {
            return fromCache.ResolvedRef;
        }

        IParsedCommandLine? commandLine;
        SourceFile? sourceFile;
        commandLine = _host.GetParsedCommandLine(refPath);
        if (commandLine is null)
        {
            AddFileToFilesByName(/*file*/ null, sourceFilePath, refPath, /*redirectedPath*/ null);
            _projectReferenceRedirects[sourceFilePath] = ProjectReferenceRedirect.Missing;
            return null;
        }
        // PORT-NOTE: TS's `commandLine.options` is non-optional; the generated slot is nullable, and the reference
        // asserts it here (it reads options.configFile two lines down), so the port asserts it once.
        var parsedOptions = commandLine.Options!;
        // PORT-NOTE: TS's options.configFile is a TsConfigSourceFile, i.e. a SourceFile; the port's generated
        // ITsConfigSourceFile is an INode-derived interface that SourceFile does not implement, so the two are bridged
        // with the cast the diagnostics slice already records (Program/Program.ProgramDiagnostics.cs:712-713).
        sourceFile = (SourceFile)(object)Debug.CheckDefined(parsedOptions.ConfigFile);
        Debug.Assert(sourceFile.Path is null || sourceFile.Path == sourceFilePath);
        AddFileToFilesByName(sourceFile, sourceFilePath, refPath, /*redirectedPath*/ null);
        sourceFile.FileName = refPath;
        sourceFile.Path = sourceFilePath;
        sourceFile.ResolvedPath = sourceFilePath;
        sourceFile.OriginalFileName = refPath;

        var resolvedRef = new ResolvedProjectReference { CommandLine = commandLine, SourceFile = sourceFile };
        _projectReferenceRedirects[sourceFilePath] = ProjectReferenceRedirect.Of(resolvedRef);
        if (!ReferenceEquals(_options.ConfigFile, sourceFile))
        {
            _mapSourceFileToResolvedRef ??= [];
            _mapOutputFileToResolvedRef ??= [];
            string? outDts = null;
            if (parsedOptions.OutFile is not null)
            {
                outDts = Utilities.ChangeExtension(parsedOptions.OutFile, Extension.Dts.Value);
                _mapOutputFileToResolvedRef[ToPath(outDts)] = new ResolvedRefAndSource { ResolvedRef = resolvedRef };
            }
            var getCommonSourceDirectory = Core.Memoize(() => GetCommonSourceDirectoryOfConfig(resolvedRef.CommandLine!, !Utilities.HostUsesCaseSensitiveFileNames(_host)));
            foreach (var fileName in commandLine.FileNames ?? [])
            {
                var path = ToPath(fileName);
                string? outputDts = null;
                if (!Parser.IsDeclarationFileName(fileName) && !Paths.FileExtensionIs(fileName, Extension.Json.Value))
                {
                    if (parsedOptions.OutFile is null)
                    {
                        outputDts = GetOutputDeclarationFileName(fileName, resolvedRef.CommandLine!, !Utilities.HostUsesCaseSensitiveFileNames(_host), getCommonSourceDirectory);
                        _mapOutputFileToResolvedRef[ToPath(outputDts)] = new ResolvedRefAndSource { ResolvedRef = resolvedRef, Source = fileName };
                    }
                    else
                    {
                        outputDts = outDts;
                    }
                }
                _mapSourceFileToResolvedRef[path] = new ResolvedRefAndOutputDts { ResolvedRef = resolvedRef, OutputDts = outputDts };
            }
        }

        if (commandLine.ProjectReferences is not null)
        {
            // PORT-NOTE: TS's `references` is `(ResolvedProjectReference | undefined)[]`; the generated
            // `IResolvedProjectReference.References` spells its elements non-nullable, so the port stores the null
            // arm as null! — the same value TS's callers then test with `forEachProjectReference`'s undefined check.
            resolvedRef.References = [.. commandLine.ProjectReferences.Select(@ref => ParseProjectReferenceConfigFile(@ref)!)];
        }

        return resolvedRef;
    }

    // ---------------------------------------------------------------------------------------------
    // The source-of-project-reference-redirect host wrapper (program.ts:4881-5056)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/program.ts:5128-5130 — <c>resolveProjectReferencePath</c>.</summary>
    /// <remarks>PORT-NOTE: the reference delegates to <c>resolveConfigFileProjectName</c> (tsbuild.ts:177-183), which
    /// is the tsbuild slice's (M8); the port calls it there and lists it as a cross-slice reference.</remarks>
    internal static string ResolveProjectReferencePath(IProjectReference @ref)
        => TsBuild.ResolveConfigFileProjectName(@ref.Path ?? "");

    /// <summary>ported from: src/compiler/program.ts:4891-5056 — <c>updateHostForUseSourceOfProjectReferenceRedirect</c>.</summary>
    /// <remarks>
    /// See the class note for the host mutation the port cannot perform. Two further reference details are spelled out
    /// here: the early return for <c>!useSourceOfProjectReferenceRedirect</c> (program.ts:4898) returns the hoisted
    /// <c>fileExists</c> function (not the host's member) and <em>no</em> <c>directoryExists</c>; and
    /// <c>originalRealpath</c>'s guard compiles away because the generated host interface declares <c>realpath</c>
    /// required.
    /// </remarks>
    internal static HostForUseSourceOfProjectReferenceRedirectResult UpdateHostForUseSourceOfProjectReferenceRedirect(HostForUseSourceOfProjectReferenceRedirect host)
    {
        var compilerHost = host.CompilerHost;
        HashSet<string>? setOfDeclarationDirectories = null;

        if (!host.UseSourceOfProjectReferenceRedirect)
        {
            return new HostForUseSourceOfProjectReferenceRedirectResult
            {
                // TS returns `noop` here; the port's OnProgramCreateComplete is empty for the same reason (no patching).
                OnProgramCreateComplete = OnProgramCreateComplete,
                FileExists = FileExists,
                // PORT-NOTE: TS's arm omits directoryExists entirely, and its consumers read a missing one as "assume
                // the directory exists"; the port's IProgram member is required and read like the host's, so the port
                // supplies the host's own member for this arm.
                DirectoryExists = compilerHost.DirectoryExists,
            };
        }

        return new HostForUseSourceOfProjectReferenceRedirectResult
        {
            OnProgramCreateComplete = OnProgramCreateComplete,
            FileExists = FileExists,
            DirectoryExists = DirectoryExists,
        };

        void OnProgramCreateComplete()
        {
            // PORT-NOTE: TS restores the host's fileExists/directoryExists/getDirectories here (and keeps realpath
            // patched); the port never patched them, so there is nothing to restore (see the class note).
        }

        // This implementation of fileExists checks if the file being requested is
        // .d.ts file for the referenced Project.
        // If it is it returns true irrespective of whether that file exists on host
        bool FileExists(string file)
        {
            if (compilerHost.FileExists(file)) return true;
            if (host.GetResolvedProjectReferences() is null) return false;
            if (!Parser.IsDeclarationFileName(file)) return false;

            // Project references go to source file instead of .d.ts file
            return FileOrDirectoryExistsUsingSource(file, /*isFile*/ true);
        }

        // This implementation of directoryExists checks if the directory being requested is
        // directory of .d.ts file for the referenced Project.
        // If it is it returns true irrespective of whether that directory exists on host
        bool DirectoryExists(string path)
        {
            if (compilerHost.DirectoryExists(path))
            {
                HandleDirectoryCouldBeSymlink(path);
                return true;
            }

            if (host.GetResolvedProjectReferences() is null) return false;

            if (setOfDeclarationDirectories is null)
            {
                setOfDeclarationDirectories = [];
                host.ForEachResolvedProjectReference(@ref =>
                {
                    var outFile = @ref.CommandLine?.Options?.OutFile;
                    if (outFile is not null)
                    {
                        setOfDeclarationDirectories!.Add(Paths.GetDirectoryPath(host.ToPath(outFile)));
                    }
                    else
                    {
                        // Set declaration's in different locations only, if they are next to source the directory present doesnt change
                        var declarationDir = @ref.CommandLine?.Options?.DeclarationDir ?? @ref.CommandLine?.Options?.OutDir;
                        if (declarationDir is not null)
                        {
                            setOfDeclarationDirectories!.Add(host.ToPath(declarationDir));
                        }
                    }

                    return null;
                });
            }

            return FileOrDirectoryExistsUsingSource(path, /*isFile*/ false);
        }

        bool? FileExistsIfProjectReferenceDts(string file)
        {
            var source = host.GetRedirectFromOutput(host.ToPath(file));
            return source is not null
                ? source.Source is not null ? compilerHost.FileExists(source.Source) : true
                : null;
        }

        bool? DirectoryExistsIfProjectReferenceDeclDir(string dir)
        {
            var dirPath = host.ToPath(dir);
            var dirPathWithTrailingDirectorySeparator = dirPath + Paths.DirectorySeparator;
            return Utilities.ForEachKey<string, bool>(
                setOfDeclarationDirectories!,
                declDirPath =>
                    dirPath == declDirPath ||
                    // Any parent directory of declaration dir
                    Core.StartsWith(declDirPath, dirPathWithTrailingDirectorySeparator) ||
                    // Any directory inside declaration dir
                    Core.StartsWith(dirPath, declDirPath + "/"));
        }

        void HandleDirectoryCouldBeSymlink(string directory)
        {
            if (host.GetResolvedProjectReferences() is null || Utilities.ContainsIgnoredPath(directory)) return;

            // Because we already watch node_modules, handle symlinks in there
            if (!directory.Contains(ModuleNameResolver.NodeModulesPathPart)) return;
            var symlinkCache = (SymlinkCache)host.GetSymlinkCache()!;
            var directoryPath = Paths.EnsureTrailingDirectorySeparator(host.ToPath(directory));
            if (symlinkCache.GetSymlinkedDirectories()?.ContainsKey(directoryPath) is true) return;

            var real = Paths.NormalizePath(compilerHost.Realpath(directory));
            // PORT-NOTE: TS assigns `realPath` inside the condition below; the port hoists the assignment so no
            // use-before-assignment is possible. `toPath` is pure, and the value is unused on the first arm.
            var realPath = Paths.EnsureTrailingDirectorySeparator(host.ToPath(real));
            if (real == directory || realPath == directoryPath)
            {
                // not symlinked
                symlinkCache.SetSymlinkedDirectory(directoryPath, /*real*/ null);
                return;
            }

            symlinkCache.SetSymlinkedDirectory(directory, new SymlinkedDirectory
            {
                Real = Paths.EnsureTrailingDirectorySeparator(real),
                RealPath = realPath,
            });
        }

        bool FileOrDirectoryExistsUsingSource(string fileOrDirectory, bool isFile)
        {
            // PORT-NOTE: TS's conditional picks between the two functions; a C# conditional between two method groups
            // has no natural type, so the delegate type is spelled out.
            Func<string, bool?> fileOrDirectoryExistsUsingSource = isFile
                ? FileExistsIfProjectReferenceDts
                : DirectoryExistsIfProjectReferenceDeclDir;
            // Check current directory or file
            var result = fileOrDirectoryExistsUsingSource(fileOrDirectory);
            if (result is not null) return result.Value;

            var symlinkCache = (SymlinkCache)host.GetSymlinkCache()!;
            var symlinkedDirectories = symlinkCache.GetSymlinkedDirectories();
            if (symlinkedDirectories is null) return false;
            var fileOrDirectoryPath = host.ToPath(fileOrDirectory);
            if (!fileOrDirectoryPath.Contains(ModuleNameResolver.NodeModulesPathPart)) return false;
            if (isFile && symlinkCache.GetSymlinkedFiles()?.ContainsKey(fileOrDirectoryPath) is true) return true;

            // If it contains node_modules check if its one of the symlinked path we know of
            // PORT-NOTE: TS runs this walk with `firstDefinedIterator` (core.ts:82-90), whose result is `U | undefined`;
            // the port's `Core.FirstDefinedIterator` constrains `U` to a reference type and this walk's result is a
            // boolean, so the loop is spelled out. The first entry that matches is the reference's first
            // non-undefined result, which is what the early return is.
            foreach (var (directoryPath, symlinkedDirectory) in symlinkedDirectories)
            {
                if (symlinkedDirectory is null || !Core.StartsWith(fileOrDirectoryPath, directoryPath)) continue;
                var sourceResult = FileOrDirectoryExistsUsingSource(ReplacePathPrefix(fileOrDirectoryPath, directoryPath, symlinkedDirectory.RealPath), isFile);
                if (isFile && sourceResult)
                {
                    // Store the real path for the file'
                    var absolutePath = Paths.GetNormalizedAbsolutePath(fileOrDirectory, compilerHost.GetCurrentDirectory());
                    symlinkCache.SetSymlinkedFile(
                        fileOrDirectoryPath,
                        symlinkedDirectory.Real + ReplacePathPrefix(absolutePath, directoryPath, ""));
                }

                return sourceResult;
            }

            return false;
        }

        // PORT-NOTE: TS builds `new RegExp(directoryPath, "i")` to strip the symlinked directory's prefix
        // case-insensitively; the port does the literal ordinal-ignore-case replacement, which never treats a path
        // character as a pattern (and is the same replacement for the path alphabet).
        static string ReplacePathPrefix(string text, string directoryPath, string replacement)
            => directoryPath.Length == 0 ? text : text.Replace(directoryPath, replacement, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>ported from: src/compiler/program.ts:4881-4890 — <c>HostForUseSourceOfProjectReferenceRedirect</c>.</summary>
/// <remarks>PORT-NOTE: the getter slots are the tests the reference makes on the host's optional members; each is a
/// delegate here because the implementation supplies one (<c>getSymlinkCache</c> is the verify slice's
/// <c>object?</c>-returning member, per the generated <c>IProgram</c>).</remarks>
internal sealed class HostForUseSourceOfProjectReferenceRedirect
{
    /// <summary>ported from: src/compiler/program.ts:4882 — <c>compilerHost</c>.</summary>
    internal required ICompilerHost CompilerHost { get; init; }

    /// <summary>ported from: src/compiler/program.ts:4883 — <c>getSymlinkCache</c>.</summary>
    internal required Func<object?> GetSymlinkCache { get; init; }

    /// <summary>ported from: src/compiler/program.ts:4884 — <c>useSourceOfProjectReferenceRedirect</c>.</summary>
    internal required bool UseSourceOfProjectReferenceRedirect { get; init; }

    /// <summary>ported from: src/compiler/program.ts:4885 — <c>toPath</c>.</summary>
    internal required Func<string, string> ToPath { get; init; }

    /// <summary>ported from: src/compiler/program.ts:4886 — <c>getResolvedProjectReferences</c>.</summary>
    internal required Func<List<IResolvedProjectReference?>?> GetResolvedProjectReferences { get; init; }

    /// <summary>ported from: src/compiler/program.ts:4887 — <c>getRedirectFromOutput</c>.</summary>
    internal required Func<string, IResolvedRefAndSource?> GetRedirectFromOutput { get; init; }

    /// <summary>ported from: src/compiler/program.ts:4888 — <c>forEachResolvedProjectReference</c>.</summary>
    internal required Func<Func<IResolvedProjectReference, object?>, object?> ForEachResolvedProjectReference { get; init; }
}

/// <summary>ported from: src/compiler/program.ts:4891-5056 — the object <c>updateHostForUseSourceOfProjectReferenceRedirect</c>
/// returns.</summary>
/// <remarks>PORT-NOTE: TS's return type is inferred (and its <c>directoryExists</c> is absent on the early-return arm);
/// the port names the three members so <c>createProgram</c> can destructure them into its state.</remarks>
internal sealed class HostForUseSourceOfProjectReferenceRedirectResult
{
    /// <summary>ported from: src/compiler/program.ts:4898, 4946-4951 — <c>onProgramCreateComplete</c>.</summary>
    internal required Action OnProgramCreateComplete { get; init; }

    /// <summary>ported from: src/compiler/program.ts:4898, 4945-4955 — <c>fileExists</c>.</summary>
    internal required Func<string, bool> FileExists { get; init; }

    /// <summary>ported from: src/compiler/program.ts:4902-4935 — <c>directoryExists</c>.</summary>
    internal required Func<string, bool> DirectoryExists { get; init; }
}

/// <summary>ported from: src/compiler/program.ts:4034 — the <c>{ commandLine, sourceFile }</c> literal
/// <c>parseProjectReferenceConfigFile</c> builds.</summary>
internal sealed class ResolvedProjectReference : IResolvedProjectReference
{
    /// <summary>ported from: src/compiler/types.ts:9992 — <c>commandLine</c>.</summary>
    public required IParsedCommandLine? CommandLine { get; set; }

    /// <summary>ported from: src/compiler/types.ts:9993 — <c>sourceFile</c>.</summary>
    public required SourceFile? SourceFile { get; set; }

    /// <summary>ported from: src/compiler/types.ts:9994 — <c>references</c>.</summary>
    public List<IResolvedProjectReference>? References { get; set; }
}

/// <summary>ported from: src/compiler/program.ts:4045 — the <c>{ resolvedRef, outputDts }</c> value of
/// <c>mapSourceFileToResolvedRef</c>.</summary>
internal sealed class ResolvedRefAndOutputDts : IResolvedRefAndOutputDts
{
    /// <summary>ported from: src/compiler/types.ts:10060 — <c>resolvedRef</c>.</summary>
    public required IResolvedProjectReference? ResolvedRef { get; set; }

    /// <summary>ported from: src/compiler/types.ts:10061 — <c>outputDts</c>.</summary>
    public string? OutputDts { get; set; }
}

/// <summary>ported from: src/compiler/program.ts:4048, 4059 — the <c>{ resolvedRef }</c>/<c>{ resolvedRef, source }</c>
/// values of <c>mapOutputFileToResolvedRef</c>.</summary>
internal sealed class ResolvedRefAndSource : IResolvedRefAndSource
{
    /// <summary>ported from: src/compiler/types.ts:10066 — <c>resolvedRef</c>.</summary>
    public required IResolvedProjectReference? ResolvedRef { get; set; }

    /// <summary>ported from: src/compiler/types.ts:10067 — <c>source</c>.</summary>
    public string? Source { get; set; }
}

/// <summary>
/// The <c>utilities.ts</c> members the project-reference paths of <c>program.ts</c> call.
/// </summary>
/// <remarks>
/// PORT-NOTE: the option tables are name lists here, not <c>CommandLineOption</c> metadata: the metadata table is
/// M8's (<c>commandLineParser.ts</c>) and <c>Program/Utilities.ModuleResolution.cs:209-248</c> already records why the
/// module-resolution subset was transcribed as names. <c>optionsAffectingProgramStructure</c> is the same treatment —
/// its six names are the options the reference's own table marks <c>affectsProgramStructure</c>
/// (<c>commandLineParser.ts:712, 898, 1173, 1372, 1451, 1481</c>).
/// </remarks>
internal static partial class Utilities
{
    // ---------------------------------------------------------------------------------------------
    // Options and program shape (utilities.ts:710-729)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:710-713 — <c>changesAffectModuleResolution</c>.</summary>
    public static bool ChangesAffectModuleResolution(ICompilerOptions oldOptions, ICompilerOptions newOptions)
        => oldOptions.ConfigFilePath != newOptions.ConfigFilePath
        || OptionsHaveModuleResolutionChanges(oldOptions, newOptions);

    /// <summary>ported from: src/compiler/utilities.ts:715-717 — <c>optionsHaveModuleResolutionChanges</c>.</summary>
    private static bool OptionsHaveModuleResolutionChanges(ICompilerOptions oldOptions, ICompilerOptions newOptions)
        => OptionsHaveChanges(oldOptions, newOptions, ModuleResolutionOptionDeclarations, GetCompilerOptionValue);

    /// <summary>ported from: src/compiler/utilities.ts:720-723 — <c>changesAffectingProgramStructure</c>.</summary>
    public static bool ChangesAffectingProgramStructure(ICompilerOptions oldOptions, ICompilerOptions newOptions)
        => OptionsHaveChanges(oldOptions, newOptions, OptionsAffectingProgramStructure, GetOptionsAffectingProgramStructureValue);

    /// <summary>ported from: src/compiler/utilities.ts:725-727 — <c>optionsHaveChanges</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's body calls <c>getCompilerOptionValue(options, option)</c> (utilities.ts:9399), whose ported
    /// version (<c>Program/Utilities.ModuleResolution.cs:224</c>) answers for the module-resolution names only,
    /// because the metadata table that tells it which slot to read is M8's. The reader is therefore a parameter here:
    /// each caller passes the one that matches its own name list, and both disappear when M8's
    /// <c>optionDeclarations</c> lands.
    /// </remarks>
    public static bool OptionsHaveChanges(
        ICompilerOptions oldOptions,
        ICompilerOptions newOptions,
        IList<string> optionDeclarations,
        Func<ICompilerOptions, string, object?> getCompilerOptionValue)
        => !ReferenceEquals(oldOptions, newOptions)
        && optionDeclarations.Any(option => !IsJsonEqual(getCompilerOptionValue(oldOptions, option), getCompilerOptionValue(newOptions, option)));

    /// <summary>ported from: src/compiler/commandLineParser.ts:1713 — <c>optionsAffectingProgramStructure</c>.</summary>
    /// <remarks>PORT-NOTE: the reference filters the full <c>optionDeclarations</c> table (M8) down to the entries
    /// marked <c>affectsProgramStructure</c>; only the filtered names are transcribed here, the treatment
    /// <c>Program/Utilities.ModuleResolution.cs:209-248</c> already gives the module-resolution subset. The six names
    /// are the entries the reference's table marks: <c>lib</c> (commandLineParser.ts:712), <c>libReplacement</c>
    /// (:898), <c>types</c> (:1173), <c>allowArbitraryExtensions</c> (:1372), <c>noLib</c> (:1451) and
    /// <c>disableSizeLimit</c> (:1481).</remarks>
    public static readonly List<string> OptionsAffectingProgramStructure =
    [
        "lib",
        "libReplacement",
        "types",
        "allowArbitraryExtensions",
        "noLib",
        "disableSizeLimit",
    ];

    /// <summary>ported from: src/compiler/utilities.ts:9399-9403 — the <c>getCompilerOptionValue</c> reads
    /// <c>optionsAffectingProgramStructure</c> needs.</summary>
    /// <remarks>PORT-NOTE: the port's <c>Utilities.GetCompilerOptionValue</c>
    /// (<c>Program/Utilities.ModuleResolution.cs:224</c>) answers for the module-resolution names only; the six names
    /// this slice's table contains are read here. Both readers collapse into one when M8's table lands.</remarks>
    private static object? GetOptionsAffectingProgramStructureValue(ICompilerOptions options, string optionName) => optionName switch
    {
        "lib" => options.Lib,
        "libReplacement" => options.LibReplacement,
        "types" => options.Types,
        "allowArbitraryExtensions" => options.AllowArbitraryExtensions,
        "noLib" => options.NoLib,
        "disableSizeLimit" => options.DisableSizeLimit,
        _ => null,
    };

    // ---------------------------------------------------------------------------------------------
    // The comparison and walk helpers (utilities.ts:742-771, 803-947, 10450, 10804, 12236-12300)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:746-757 — <c>forEachEntry</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>ReadonlyMap&lt;K, V&gt;</c> is any map-like; the port takes an
    /// <see cref="IReadOnlyDictionary{TKey,TValue}"/>, which is what every caller here has.</remarks>
    public static TU? ForEachEntry<TK, TV, TU>(IReadOnlyDictionary<TK, TV> map, Func<TV, TK, TU?> callback)
        where TK : notnull
    {
        foreach (var (key, value) in map)
        {
            var result = callback(value, key);
            if (Js.IsTruthy(result))
            {
                return result;
            }
        }

        return default;
    }

    /// <summary>ported from: src/compiler/utilities.ts:762-771 — <c>forEachKey</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>ReadonlyCollection&lt;K&gt;</c> is a set's or a map's keys; the port takes the
    /// sequence (a <c>HashSet</c> or a dictionary's <c>Keys</c> both convert).</remarks>
    public static TU? ForEachKey<TK, TU>(IEnumerable<TK> map, Func<TK, TU?> callback)
    {
        foreach (var key in map)
        {
            var result = callback(key);
            if (Js.IsTruthy(result))
            {
                return result;
            }
        }

        return default;
    }

    /// <summary>ported from: src/compiler/utilities.ts:803-806 — <c>projectReferenceIsEqualTo</c>.</summary>
    public static bool ProjectReferenceIsEqualTo(IProjectReference oldRef, IProjectReference newRef)
        => oldRef.Path == newRef.Path
        && !Js.IsTruthy(oldRef.Prepend) == !Js.IsTruthy(newRef.Prepend)
        && !Js.IsTruthy(oldRef.Circular) == !Js.IsTruthy(newRef.Circular);

    /// <summary>ported from: src/compiler/utilities.ts:810-822 — <c>moduleResolutionIsEqualTo</c>.</summary>
    public static bool ModuleResolutionIsEqualTo(
        IResolvedModuleWithFailedLookupLocations oldResolution,
        IResolvedModuleWithFailedLookupLocations newResolution)
    {
        var oldModule = oldResolution.ResolvedModule;
        var newModule = newResolution.ResolvedModule;
        return ReferenceEquals(oldResolution, newResolution)
            || ReferenceEquals(oldModule, newModule)
            || (oldModule is not null
                && newModule is not null
                && oldModule.IsExternalLibraryImport == newModule.IsExternalLibraryImport
                && oldModule.Extension == newModule.Extension
                && oldModule.ResolvedFileName == newModule.ResolvedFileName
                && oldModule.OriginalPath == newModule.OriginalPath
                && PackageIdIsEqual(oldModule.PackageId, newModule.PackageId)
                && oldResolution.AlternateResult == newResolution.AlternateResult);
    }

    /// <summary>ported from: src/compiler/utilities.ts:904-906 — <c>packageIdIsEqual</c>.</summary>
    private static bool PackageIdIsEqual(IPackageId? a, IPackageId? b)
        => ReferenceEquals(a, b)
        || (a is not null
            && b is not null
            && a.Name == b.Name
            && a.SubModuleName == b.SubModuleName
            && a.Version == b.Version
            && a.PeerDependencies == b.PeerDependencies);

    /// <summary>ported from: src/compiler/utilities.ts:824-826 — <c>getResolvedModuleFromResolution</c>.</summary>
    public static IResolvedModuleFull? GetResolvedModuleFromResolution(IResolvedModuleWithFailedLookupLocations resolution)
        => resolution.ResolvedModule;

    /// <summary>ported from: src/compiler/utilities.ts:829-831 —
    /// <c>getResolvedTypeReferenceDirectiveFromResolution</c>.</summary>
    public static IResolvedTypeReferenceDirective? GetResolvedTypeReferenceDirectiveFromResolution(
        IResolvedTypeReferenceDirectiveWithFailedLookupLocations resolution)
        => resolution.ResolvedTypeReferenceDirective;

    /// <summary>ported from: src/compiler/utilities.ts:919-928 — <c>typeDirectiveIsEqualTo</c>.</summary>
    public static bool TypeDirectiveIsEqualTo(
        IResolvedTypeReferenceDirectiveWithFailedLookupLocations oldResolution,
        IResolvedTypeReferenceDirectiveWithFailedLookupLocations newResolution)
    {
        var oldDirective = oldResolution.ResolvedTypeReferenceDirective;
        var newDirective = newResolution.ResolvedTypeReferenceDirective;
        return ReferenceEquals(oldResolution, newResolution)
            || ReferenceEquals(oldDirective, newDirective)
            || (oldDirective is not null
                && newDirective is not null
                && oldDirective.ResolvedFileName == newDirective.ResolvedFileName
                && oldDirective.Primary == newDirective.Primary
                && oldDirective.OriginalPath == newDirective.OriginalPath);
    }

    /// <summary>ported from: src/compiler/utilities.ts:930-947 — <c>hasChangesInResolutions</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>changed</c> is <c>boolean | V</c> (the else arm is the resolution itself) and is
    /// read as a truthiness test; the port's is the boolean that test yields.</remarks>
    public static bool HasChangesInResolutions<TName, TResolution>(
        IList<TName> names,
        IList<TResolution> newResolutions,
        Func<TName, TResolution?> getOldResolution,
        Func<TResolution, TResolution, bool> comparer)
        where TResolution : class
    {
        Debug.Assert(names.Count == newResolutions.Count);

        for (var i = 0; i < names.Count; i++)
        {
            var newResolution = newResolutions[i];
            var entry = names[i];
            var oldResolution = getOldResolution(entry);
            var changed = oldResolution is not null
                ? newResolution is null || !comparer(oldResolution, newResolution)
                : newResolution is not null;
            if (changed)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/utilities.ts:10804-10806 — <c>containsIgnoredPath</c>.</summary>
    /// <remarks>PORT-NOTE: <c>ignoredPaths</c> is <c>sys.ts:562</c>'s list, which the port keeps as
    /// <see cref="SysCore.IgnoredPaths"/> (§13.5 drops the <c>sys</c> singleton, not its data).</remarks>
    public static bool ContainsIgnoredPath(string path)
        => Core.Some(SysCore.IgnoredPaths, searchPath => path.Contains(searchPath));

    /// <summary>ported from: src/compiler/utilities.ts:12236-12246 — <c>forEachResolvedProjectReference</c>.</summary>
    /// <remarks>PORT-NOTE: TS forwards to <c>forEachProjectReference</c> with a callback whose result is
    /// <c>T | undefined</c>. The port's sibling declaration of that walk constrains its result to a reference type
    /// (<c>Program/Program.ProgramDiagnostics.cs:1154</c>), so the callback's result is boxed to <c>object?</c> here and
    /// unboxed on the way out; the walk itself stays the reference's single implementation, as it is in TS.</remarks>
    public static T? ForEachResolvedProjectReference<T>(
        List<IResolvedProjectReference?>? resolvedProjectReferences,
        Func<IResolvedProjectReference, T> callback)
    {
        var result = ForEachProjectReference<object>(
            /*projectReferences*/ null,
            resolvedProjectReferences,
            (resolvedRef, _, _) => resolvedRef is not null ? callback(resolvedRef) : null);
        return result is null ? default : (T)result;
    }

    // ---------------------------------------------------------------------------------------------
    // The symlink cache (utilities.ts:9452-9601)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:9500-9571 — <c>createSymlinkCache</c>.</summary>
    /// <remarks>PORT-NOTE: TS returns an object literal closing over four variables; the port's closure is
    /// <see cref="SymlinkCache"/>'s constructor, so this is the factory that names it (<c>Program/Program.Verify.cs</c>
    /// is the caller, and <c>Program/Program.Verify.cs:1099</c> calls it exactly here).</remarks>
    public static SymlinkCache CreateSymlinkCache(string cwd, Core.GetCanonicalFileName getCanonicalFileName)
        => new(cwd, getCanonicalFileName);

    /// <summary>ported from: src/compiler/utilities.ts:9573-9588 — <c>guessDirectorySymlink</c>.</summary>
    /// <remarks>PORT-NOTE: TS returns the pair or <c>emptyArray</c> and the caller tests both elements; the port returns
    /// the nullable pair, whose two elements are non-empty whenever it is present.</remarks>
    public static (string Resolved, string Original)? GuessDirectorySymlink(
        string a,
        string b,
        string cwd,
        Core.GetCanonicalFileName getCanonicalFileName)
    {
        var aParts = Paths.GetPathComponents(Paths.GetNormalizedAbsolutePath(a, cwd));
        var bParts = Paths.GetPathComponents(Paths.GetNormalizedAbsolutePath(b, cwd));
        var isDirectory = false;
        while (aParts.Count >= 2
            && bParts.Count >= 2
            && !IsNodeModulesOrScopedPackageDirectory(aParts[^2], getCanonicalFileName)
            && !IsNodeModulesOrScopedPackageDirectory(bParts[^2], getCanonicalFileName)
            && getCanonicalFileName(aParts[^1]) == getCanonicalFileName(bParts[^1]))
        {
            aParts.RemoveAt(aParts.Count - 1);
            bParts.RemoveAt(bParts.Count - 1);
            isDirectory = true;
        }

        return isDirectory
            ? (Paths.GetPathFromPathComponents(aParts), Paths.GetPathFromPathComponents(bParts))
            : null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:9592-9594 — <c>isNodeModulesOrScopedPackageDirectory</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>s !== undefined</c> is the port's <c>s is not null</c> (the element can be absent
    /// from the component list, which C# spells <c>string?</c>).</remarks>
    private static bool IsNodeModulesOrScopedPackageDirectory(string? s, Core.GetCanonicalFileName getCanonicalFileName)
        => s is not null && (getCanonicalFileName(s) == "node_modules" || Core.StartsWith(s, "@"));
}

/// <summary>ported from: src/compiler/utilities.ts:9453-9463 — <c>SymlinkedDirectory</c>.</summary>
internal sealed class SymlinkedDirectory
{
    /// <summary>ported from: src/compiler/utilities.ts:9458 — <c>real</c>.</summary>
    public string Real { get; set; } = "";

    /// <summary>ported from: src/compiler/utilities.ts:9463 — <c>realPath</c>.</summary>
    public string RealPath { get; set; } = "";
}

/// <summary>ported from: src/compiler/utilities.ts:9465-9498 — <c>SymlinkCache</c>.</summary>
/// <remarks>
/// PORT-NOTE: the reference's <c>createSymlinkCache</c> object literal is this class, with its four closure variables
/// as fields (<c>Program/Program.Verify.cs</c> records why the type is not in the generated model).
/// <para>
/// PORT-NOTE: TS's <c>SymlinkedDirectory | false</c> dictionary value cannot exist in C#, so a null value is the
/// reference's <c>false</c> and an absent key is its <c>undefined</c> — the same reading
/// <c>Program/Program.State.cs</c> records for <c>FileByNameEntry</c>.
/// </para>
/// </remarks>
internal sealed class SymlinkCache
{
    private readonly string _cwd;
    private readonly Core.GetCanonicalFileName _getCanonicalFileName;
    private Dictionary<string, SymlinkedDirectory>? _symlinkedDirectories;
    private Core.MultiMap<string, string>? _symlinkedDirectoriesByRealpath;
    private Dictionary<string, string>? _symlinkedFiles;
    private bool _hasProcessedResolutions;

    internal SymlinkCache(string cwd, Core.GetCanonicalFileName getCanonicalFileName)
    {
        _cwd = cwd;
        _getCanonicalFileName = getCanonicalFileName;
    }

    /// <summary>ported from: src/compiler/utilities.ts:9467 — <c>getSymlinkedDirectories</c>.</summary>
    public Dictionary<string, SymlinkedDirectory>? GetSymlinkedDirectories() => _symlinkedDirectories;

    /// <summary>ported from: src/compiler/utilities.ts:9469 — <c>getSymlinkedDirectoriesByRealpath</c>.</summary>
    public Core.MultiMap<string, string>? GetSymlinkedDirectoriesByRealpath() => _symlinkedDirectoriesByRealpath;

    /// <summary>ported from: src/compiler/utilities.ts:9471 — <c>getSymlinkedFiles</c>.</summary>
    public Dictionary<string, string>? GetSymlinkedFiles() => _symlinkedFiles;

    /// <summary>ported from: src/compiler/utilities.ts:9473 — <c>setSymlinkedFile</c>.</summary>
    public void SetSymlinkedFile(string symlinkPath, string real)
        => (_symlinkedFiles ??= [])[symlinkPath] = real;

    /// <summary>ported from: src/compiler/utilities.ts:9474-9485 — <c>setSymlinkedDirectory</c>.</summary>
    public void SetSymlinkedDirectory(string symlink, SymlinkedDirectory? real)
    {
        // Large, interconnected dependency graphs in pnpm will have a huge number of symlinks
        // where both the realpath and the symlink path are inside node_modules/.pnpm. Since
        // this path is never a candidate for a module specifier, we can ignore it entirely.
        var symlinkPath = Paths.ToPath(symlink, _cwd, _getCanonicalFileName);
        if (!Utilities.ContainsIgnoredPath(symlinkPath))
        {
            symlinkPath = Paths.EnsureTrailingDirectorySeparator(symlinkPath);
            if (real is not null && _symlinkedDirectories?.ContainsKey(symlinkPath) is not true)
            {
                (_symlinkedDirectoriesByRealpath ??= Core.CreateMultiMap<string, string>()).Add(real.RealPath, symlink);
            }

            (_symlinkedDirectories ??= [])[symlinkPath] = real!;
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:9486-9492 — <c>setSymlinksFromResolutions</c>.</summary>
    /// <remarks>PORT-NOTE: TS's two parameters are callbacks that receive the per-resolution callback (whose own
    /// parameters are resolution, module name, mode and file path); the port spells that
    /// <see cref="Action{T}"/> over the ported callback shape, which is what
    /// <c>Program/Program.Verify.cs:1102</c> passes and what the four-argument lambdas below feed.</remarks>
    public void SetSymlinksFromResolutions(
        Action<Action<IResolvedModuleWithFailedLookupLocations, string, ModuleKind?, string>> forEachResolvedModule,
        Action<Action<IResolvedTypeReferenceDirectiveWithFailedLookupLocations, string, ModuleKind?, string>> forEachResolvedTypeReferenceDirective,
        ModeAwareCache<IResolvedTypeReferenceDirectiveWithFailedLookupLocations> typeReferenceDirectives)
    {
        Debug.Assert(!_hasProcessedResolutions);
        _hasProcessedResolutions = true;
        forEachResolvedModule((resolution, _, _, _) => ProcessResolution(resolution.ResolvedModule));
        forEachResolvedTypeReferenceDirective((resolution, _, _, _) => ProcessResolution(resolution.ResolvedTypeReferenceDirective));
        typeReferenceDirectives.ForEach((resolution, _, _) => ProcessResolution(resolution.ResolvedTypeReferenceDirective));
    }

    /// <summary>ported from: src/compiler/utilities.ts:9493 — <c>hasProcessedResolutions</c>.</summary>
    public bool HasProcessedResolutions() => _hasProcessedResolutions;

    /// <summary>ported from: src/compiler/utilities.ts:9494-9496 — <c>setSymlinksFromResolution</c>.</summary>
    public void SetSymlinksFromResolution(IResolvedModuleFull? resolution) => ProcessResolution(resolution);

    /// <summary>ported from: src/compiler/utilities.ts:9556-9558 — <c>hasAnySymlinks</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>forEachEntry(…, value => !!value)</c> is the port's
    /// <c>Utilities.ForEachEntry</c> with a boolean result; the explicit type arguments keep the result's type a
    /// <c>bool?</c> rather than <c>bool</c>.</remarks>
    public bool HasAnySymlinks()
        => _symlinkedFiles is { Count: > 0 }
        || (_symlinkedDirectories is not null
            && Utilities.ForEachEntry<string, SymlinkedDirectory, bool>(_symlinkedDirectories, (value, _) => value is not null) is true);

    /// <summary>ported from: src/compiler/utilities.ts:9560-9570 — <c>processResolution</c>.</summary>
    /// <remarks>PORT-NOTE: TS's single function takes the <c>ResolvedModuleFull | ResolvedTypeReferenceDirective |
    /// undefined</c> union; the port has one overload per member of that union over a shared body.</remarks>
    private void ProcessResolution(IResolvedModuleFull? resolution)
        => ProcessResolution(resolution?.OriginalPath, resolution?.ResolvedFileName);

    /// <summary>ported from: src/compiler/utilities.ts:9560-9570 — <c>processResolution</c> (the type-reference
    /// member of the union).</summary>
    private void ProcessResolution(IResolvedTypeReferenceDirective? resolution)
        => ProcessResolution(resolution?.OriginalPath, resolution?.ResolvedFileName);

    private void ProcessResolution(string? originalPath, string? resolvedFileName)
    {
        if (originalPath is null || resolvedFileName is null)
        {
            return;
        }

        SetSymlinkedFile(Paths.ToPath(originalPath, _cwd, _getCanonicalFileName), resolvedFileName);
        var guessed = Utilities.GuessDirectorySymlink(resolvedFileName, originalPath, _cwd, _getCanonicalFileName);
        if (guessed is not null)
        {
            SetSymlinkedDirectory(
                guessed.Value.Original,
                new SymlinkedDirectory
                {
                    Real = Paths.EnsureTrailingDirectorySeparator(guessed.Value.Resolved),
                    RealPath = Paths.EnsureTrailingDirectorySeparator(Paths.ToPath(guessed.Value.Resolved, _cwd, _getCanonicalFileName)),
                });
        }
    }
}
