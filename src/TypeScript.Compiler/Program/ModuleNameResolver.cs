namespace TypeScript.Compiler;

// ported from: src/compiler/moduleNameResolver.ts:1-1492
//   118-126     trace / isTraceEnabled
//   127-153     withPackageId / noPackageId / removeIgnoredPackageId
//   193-209     formatExtensions / extensionsToExtensionsArray
//   216-222     resolvedTypeScriptOnly
//   224-316     createResolvedModule* / the resolution-field helpers
//   458-536     typesVersions paths / effective type roots / type-root candidates
//   538-725     resolveTypeReferenceDirective
//   726-785     getNodeResolutionFeatures / getConditions
//   787-811     resolvePackageNameToPackageJson
//   813-850     getAutomaticTypeDirectiveNames
//   925-932     isPackageJsonInfo / isMissingPackageJsonInfo
//   944-964     compilerOptionValueToString / getKeyForCompilerOptions
//   1051-1066   createPackageJsonInfoCache
//   1068-1076   getOrCreateCache
//   1078-1390   the resolution-cache factories
//   1392-1492   getOptionsForLibraryResolution / resolveLibrary / resolveModuleNameFromCache / resolveModuleName
/// <summary>
/// Module resolution: the entry points (<see cref="ResolveModuleName"/>, <see cref="ResolveTypeReferenceDirective"/>,
/// <see cref="ResolveLibrary"/>, …), the per-resolution-mode loaders, and the resolution caches.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS has module-level functions; the port puts them on a static class (§10.1's <c>Core</c>/<c>Utilities</c>
/// treatment), split across the partial files listed below the same way the reference's sections are laid out.
/// <para>
/// PORT-NOTE: TS's <c>ResolutionMode</c> is <c>ModuleKind.ESNext | ModuleKind.CommonJS | undefined</c> and has no
/// generated C# counterpart, so every slot in this slice spells it <c>ModuleKind?</c>; the generated interfaces that
/// carry it at their own boundary spell it <c>object?</c>, and those two meet nowhere in this file.
/// </para>
/// <para>
/// PORT-NOTE: the reference reads several optional host members (<c>host.trace</c>, <c>host.realpath</c>,
/// <c>host.directoryExists</c>, <c>host.getDirectories</c>, <c>host.getCurrentDirectory</c>) to decide whether the host
/// supports them. The generated <see cref="IModuleResolutionHost"/> declares them all required (the generator does not
/// record optionality), so those guards are gone and the call is made; a host that cannot answer for a member must
/// answer the reference's default (see <c>Utilities.DirectoryProbablyExists</c>). Each site that dropped a guard says
/// so.
/// </para>
/// </remarks>
internal static partial class ModuleNameResolver
{
    // ---------------------------------------------------------------------------------------------
    // Tracing
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:118-120 — <c>trace</c>.</summary>
    public static void Trace(IModuleResolutionHost host, DiagnosticMessage message, params object?[] args)
        => host.Trace(Utilities.FormatMessage(message, args));

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:123-125 — <c>isTraceEnabled</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS also tests <c>host.trace !== undefined</c>; the generated host interface declares
    /// <c>trace</c> required, so only the option read remains.
    /// </remarks>
    public static bool IsTraceEnabled(ICompilerOptions compilerOptions, IModuleResolutionHost host)
        => Js.IsTruthy(compilerOptions.TraceResolution);

    private static void TraceIfEnabled(ModuleResolutionState state, DiagnosticMessage diagnostic, params string[] args)
    {
        if (state.TraceEnabled)
        {
            Trace(state.Host, diagnostic, args);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Package ids
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:127-141 — <c>withPackageId</c>.</summary>
    private static Resolved? WithPackageId(PackageJsonInfo? packageInfo, PathAndExtension? r, ModuleResolutionState? state)
    {
        IPackageId? packageId = null;
        if (r is not null && packageInfo is not null)
        {
            var packageJsonContent = packageInfo.Contents.PackageJsonContent;
            if (packageJsonContent.Get("name") is string name && packageJsonContent.Get("version") is string packageVersion)
            {
                packageId = new PackageId
                {
                    Name = name,
                    SubModuleName = JsSlice(
                        r.Path,
                        packageInfo.PackageDirectory.Length + Paths.DirectorySeparator.Length),
                    Version = packageVersion,
                    PeerDependencies = GetPeerDependenciesOfPackageJsonInfo(packageInfo, state!),
                };
            }
        }

        return r is null
            ? null
            : new Resolved
            {
                Path = r.Path,
                Extension = r.Ext,
                PackageId = packageId,
                ResolvedUsingTsExtension = r.ResolvedUsingTsExtension,
            };
    }

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:143-145 — <c>noPackageId</c>.</summary>
    private static Resolved? NoPackageId(PathAndExtension? r)
        // State will not be used so no need to pass
        => WithPackageId(packageInfo: null, r, state: null);

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:147-153 — <c>removeIgnoredPackageId</c>.</summary>
    private static PathAndExtension? RemoveIgnoredPackageId(Resolved? r)
    {
        if (r is not null)
        {
            Debug.Assert(r.PackageId is null);
            return new PathAndExtension
            {
                Path = r.Path,
                Ext = r.Extension,
                ResolvedUsingTsExtension = r.ResolvedUsingTsExtension,
            };
        }

        return null;
    }

    // ---------------------------------------------------------------------------------------------
    // Extensions
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:193-200 — <c>formatExtensions</c>.</summary>
    private static string FormatExtensions(Extensions extensions)
    {
        var result = new List<string>();
        if ((extensions & Extensions.TypeScript) != 0) result.Add("TypeScript");
        if ((extensions & Extensions.JavaScript) != 0) result.Add("JavaScript");
        if ((extensions & Extensions.Declaration) != 0) result.Add("Declaration");
        if ((extensions & Extensions.Json) != 0) result.Add("JSON");
        return string.Join(", ", result);
    }

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:202-209 — <c>extensionsToExtensionsArray</c>.</summary>
    private static List<string> ExtensionsToExtensionsArray(Extensions extensions)
    {
        var result = new List<string>();
        if ((extensions & Extensions.TypeScript) != 0) result.AddRange(Utilities.SupportedTSImplementationExtensions);
        if ((extensions & Extensions.JavaScript) != 0) result.AddRange(Utilities.SupportedJSExtensionsFlat);
        if ((extensions & Extensions.Declaration) != 0) result.AddRange(Utilities.SupportedDeclarationExtensions);
        if ((extensions & Extensions.Json) != 0) result.Add(Extension.Json.Value);
        return result;
    }

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:216-222 — <c>resolvedTypeScriptOnly</c>.</summary>
    private static PathAndPackageId? ResolvedTypeScriptOnly(Resolved? resolved)
    {
        if (resolved is null)
        {
            return null;
        }

        Debug.Assert(Utilities.ExtensionIsTS(resolved.Extension));
        return new PathAndPackageId { FileName = resolved.Path, PackageId = resolved.PackageId };
    }

    // ---------------------------------------------------------------------------------------------
    // Result construction
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:224-257 — <c>createResolvedModuleWithFailedLookupLocationsHandlingSymlink</c>.</summary>
    private static ResolvedModuleWithFailedLookupLocations CreateResolvedModuleWithFailedLookupLocationsHandlingSymlink(
        string moduleName,
        Resolved? resolved,
        bool? isExternalLibraryImport,
        List<string> failedLookupLocations,
        List<string> affectingLocations,
        List<IDiagnostic> diagnostics,
        ModuleResolutionState state,
        IResolutionCache? cache,
        string? alternateResult = null)
    {
        // If this is from node_modules for non relative name, always respect preserveSymlinks
        if (
            state.ResultFromCache is null
            && !Js.IsTruthy(state.CompilerOptions.PreserveSymlinks)
            && resolved is not null
            && isExternalLibraryImport == true
            && resolved.OriginalPath is null
            && !resolved.OriginalPathIsTrue
            && !Utilities.IsExternalModuleNameRelative(moduleName)
        )
        {
            var (resolvedFileName, originalPath) = GetOriginalAndResolvedFileName(
                resolved.Path,
                state.Host,
                state.TraceEnabled);
            if (originalPath is not null)
            {
                resolved = new Resolved
                {
                    Path = resolvedFileName,
                    Extension = resolved.Extension,
                    PackageId = resolved.PackageId,
                    OriginalPath = originalPath,
                    ResolvedUsingTsExtension = resolved.ResolvedUsingTsExtension,
                };
            }
        }

        return CreateResolvedModuleWithFailedLookupLocations(
            resolved,
            isExternalLibraryImport,
            failedLookupLocations,
            affectingLocations,
            diagnostics,
            state.ResultFromCache,
            cache,
            alternateResult);
    }

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:259-298 — <c>createResolvedModuleWithFailedLookupLocations</c>.</summary>
    private static ResolvedModuleWithFailedLookupLocations CreateResolvedModuleWithFailedLookupLocations(
        Resolved? resolved,
        bool? isExternalLibraryImport,
        List<string> failedLookupLocations,
        List<string> affectingLocations,
        List<IDiagnostic> diagnostics,
        ResolvedModuleWithFailedLookupLocations? resultFromCache,
        IResolutionCache? cache,
        string? alternateResult = null)
    {
        if (resultFromCache is not null)
        {
            if (cache?.IsReadonly != true)
            {
                resultFromCache.FailedLookupLocations = UpdateResolutionField(resultFromCache.FailedLookupLocations, failedLookupLocations);
                resultFromCache.AffectingLocations = UpdateResolutionField(resultFromCache.AffectingLocations, affectingLocations);
                resultFromCache.ResolutionDiagnostics = UpdateResolutionField(resultFromCache.ResolutionDiagnostics, diagnostics);
                return resultFromCache;
            }

            return new ResolvedModuleWithFailedLookupLocations
            {
                ResolvedModule = resultFromCache.ResolvedModule,
                FailedLookupLocations = InitializeResolutionFieldForReadonlyCache(resultFromCache.FailedLookupLocations, failedLookupLocations),
                AffectingLocations = InitializeResolutionFieldForReadonlyCache(resultFromCache.AffectingLocations, affectingLocations),
                ResolutionDiagnostics = InitializeResolutionFieldForReadonlyCache(resultFromCache.ResolutionDiagnostics, diagnostics),
                AlternateResult = resultFromCache.AlternateResult,
            };
        }

        return new ResolvedModuleWithFailedLookupLocations
        {
            ResolvedModule = resolved is null
                ? null
                : new ResolvedModuleFull
                {
                    ResolvedFileName = resolved.Path,
                    OriginalPath = resolved.OriginalPathIsTrue ? null : resolved.OriginalPath,
                    Extension = resolved.Extension,
                    IsExternalLibraryImport = isExternalLibraryImport,
                    PackageId = resolved.PackageId,
                    ResolvedUsingTsExtension = resolved.ResolvedUsingTsExtension == true,
                },
            FailedLookupLocations = InitializeResolutionField(failedLookupLocations),
            AffectingLocations = InitializeResolutionField(affectingLocations),
            ResolutionDiagnostics = InitializeResolutionField(diagnostics),
            AlternateResult = alternateResult,
        };
    }

    private static List<T>? InitializeResolutionField<T>(List<T> value)
        => value.Count != 0 ? value : null;

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:304-309 — <c>updateResolutionField</c>.</summary>
    public static List<T>? UpdateResolutionField<T>(List<T>? to, List<T>? value)
    {
        if (value is null || value.Count == 0)
        {
            return to;
        }

        if (to is null || to.Count == 0)
        {
            return value;
        }

        to.AddRange(value);
        return to;
    }

    private static List<T>? InitializeResolutionFieldForReadonlyCache<T>(List<T>? fromCache, List<T> value)
    {
        if (fromCache is null || fromCache.Count == 0)
        {
            return InitializeResolutionField(value);
        }

        if (value.Count == 0)
        {
            return [.. fromCache];
        }

        return [.. fromCache, .. value];
    }

    // ---------------------------------------------------------------------------------------------
    // typesVersions
    // ---------------------------------------------------------------------------------------------

    // PORT-NOTE (concurrency): the compiler's own version never changes, so parsing it once at type load is both
    // cheaper and thread-safe; the lazy `??=` it replaced was neither needed nor safe. See docs/architecture.md §18.
    private static readonly Version s_typeScriptVersion = new(VersionInfo.Version);

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:461-477 — <c>getPackageJsonTypesVersionsPaths</c>.</summary>
    public static VersionPaths? GetPackageJsonTypesVersionsPaths(Dictionary<string, Dictionary<string, List<string>>> typesVersions)
    {
        foreach (var key in Core.GetOwnKeys(typesVersions))
        {
            var keyRange = VersionRange.TryParse(key);
            if (keyRange is null)
            {
                continue;
            }

            // return the first entry whose range matches the current compiler version.
            if (keyRange.Test(s_typeScriptVersion))
            {
                return new VersionPaths { Version = key, Paths = typesVersions[key] };
            }
        }

        return null;
    }

    // ---------------------------------------------------------------------------------------------
    // Type roots
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:479-499 — <c>getEffectiveTypeRoots</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types the host as the one-member <c>GetEffectiveTypeRootsHost</c> (and its member optional); every
    /// call site passes a <c>ModuleResolutionHost</c>, and the generated interfaces have no structural compatibility, so
    /// the port takes <see cref="IModuleResolutionHost"/>.
    /// </remarks>
    public static List<string>? GetEffectiveTypeRoots(ICompilerOptions options, IModuleResolutionHost host)
    {
        if (Js.IsTruthy(options.TypeRoots))
        {
            return options.TypeRoots;
        }

        string? currentDirectory = null;
        if (Js.IsTruthy(options.ConfigFilePath))
        {
            currentDirectory = Paths.GetDirectoryPath(options.ConfigFilePath!);
        }
        else
        {
            currentDirectory = host.GetCurrentDirectory();
        }

        if (currentDirectory is not null)
        {
            return GetDefaultTypeRoots(currentDirectory);
        }

        return null;
    }

    /// <summary>
    /// ported from: src/compiler/moduleNameResolver.ts:505-508 — <c>getDefaultTypeRoots</c>.
    /// </summary>
    /// <remarks>
    /// Returns the path to every <c>node_modules/@types</c> directory from some ancestor directory. Returns
    /// <c>undefined</c> if there are none.
    /// </remarks>
    private static List<string>? GetDefaultTypeRoots(string currentDirectory)
    {
        List<string>? typeRoots = null;
        Paths.ForEachAncestorDirectory<object>(Paths.NormalizePath(currentDirectory), directory =>
        {
            var atTypes = Paths.CombinePaths(directory, NodeModulesAtTypes);
            (typeRoots ??= []).Add(atTypes);
            return null;
        });
        return typeRoots;
    }

    private const string NodeModulesAtTypes = "node_modules/@types";

    private static bool ArePathsEqual(string path1, string path2, IModuleResolutionHost host)
    {
        var useCaseSensitiveFileNames = Utilities.HostUsesCaseSensitiveFileNames(host);
        return Paths.ComparePaths(path1, path2, !useCaseSensitiveFileNames) == Comparison.EqualTo;
    }

    private static (string ResolvedFileName, string? OriginalPath) GetOriginalAndResolvedFileName(
        string fileName,
        IModuleResolutionHost host,
        bool traceEnabled)
    {
        var resolvedFileName = RealPath(fileName, host, traceEnabled);
        var pathsAreEqual = ArePathsEqual(fileName, resolvedFileName, host);
        return (
            // If the fileName and realpath are differing only in casing prefer fileName so that we can issue correct
            // errors for casing under forceConsistentCasingInFileNames
            pathsAreEqual ? fileName : resolvedFileName,
            pathsAreEqual ? null : fileName);
    }

    private static string GetCandidateFromTypeRoot(
        string typeRoot,
        string typeReferenceDirectiveName,
        ModuleResolutionState moduleResolutionState)
    {
        var nameForLookup = Core.EndsWith(typeRoot, "/node_modules/@types") || Core.EndsWith(typeRoot, "/node_modules/@types/")
            ? MangleScopedPackageNameWithTrace(typeReferenceDirectiveName, moduleResolutionState)
            : typeReferenceDirectiveName;
        return Paths.CombinePaths(typeRoot, nameForLookup);
    }

    // ---------------------------------------------------------------------------------------------
    // resolveTypeReferenceDirective
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:531-725 — <c>resolveTypeReferenceDirective</c>.</summary>
    /// <remarks>
    /// <paramref name="containingFile"/> can be <c>undefined</c> if the containing file is unknown. This is possible in
    /// case resolution is performed for directives specified via the 'types' parameter; in this case the initial path
    /// for secondary lookups is assumed to be the same as root directory of the project.
    /// </remarks>
    public static ResolvedTypeReferenceDirectiveWithFailedLookupLocations ResolveTypeReferenceDirective(
        string typeReferenceDirectiveName,
        string? containingFile,
        ICompilerOptions options,
        IModuleResolutionHost host,
        IResolvedProjectReference? redirectedReference = null,
        TypeReferenceDirectiveResolutionCache? cache = null,
        ModuleKind? resolutionMode = null)
    {
        Debug.Assert(
            typeReferenceDirectiveName is string,
            "Non-string value passed to `ts.resolveTypeReferenceDirective`, likely by a wrapping package working with an outdated `resolveTypeReferenceDirectives` signature. This is probably not a problem in TS itself.");
        var traceEnabled = IsTraceEnabled(options, host);
        if (redirectedReference is not null)
        {
            options = redirectedReference.CommandLine!.Options!;
        }

        var containingDirectory = containingFile is not null ? Paths.GetDirectoryPath(containingFile) : null;
        var result = containingDirectory is not null
            ? cache?.GetFromDirectoryCache(typeReferenceDirectiveName, resolutionMode, containingDirectory, redirectedReference)
            : null;
        if (result is null && containingDirectory is not null && !Utilities.IsExternalModuleNameRelative(typeReferenceDirectiveName))
        {
            result = cache?.GetFromNonRelativeNameCache(typeReferenceDirectiveName, resolutionMode, containingDirectory, redirectedReference);
        }

        if (result is not null)
        {
            if (traceEnabled)
            {
                Trace(host, Diagnostics.Resolving_type_reference_directive_0_containing_file_1, typeReferenceDirectiveName, containingFile);
                if (redirectedReference is not null)
                {
                    Trace(host, Diagnostics.Using_compiler_options_of_project_reference_redirect_0, redirectedReference.SourceFile!.FileName);
                }

                Trace(host, Diagnostics.Resolution_for_type_reference_directive_0_was_found_in_cache_from_location_1, typeReferenceDirectiveName, containingDirectory);
                TraceTypeReferenceDirectiveResult(result);
            }

            return result!;
        }

        var typeRoots = GetEffectiveTypeRoots(options, host);
        if (traceEnabled)
        {
            if (containingFile is null)
            {
                if (typeRoots is null)
                {
                    Trace(host, Diagnostics.Resolving_type_reference_directive_0_containing_file_not_set_root_directory_not_set, typeReferenceDirectiveName);
                }
                else
                {
                    Trace(host, Diagnostics.Resolving_type_reference_directive_0_containing_file_not_set_root_directory_1, typeReferenceDirectiveName, string.Join(",", typeRoots));
                }
            }
            else
            {
                if (typeRoots is null)
                {
                    Trace(host, Diagnostics.Resolving_type_reference_directive_0_containing_file_1_root_directory_not_set, typeReferenceDirectiveName, containingFile);
                }
                else
                {
                    Trace(host, Diagnostics.Resolving_type_reference_directive_0_containing_file_1_root_directory_2, typeReferenceDirectiveName, containingFile, string.Join(",", typeRoots));
                }
            }

            if (redirectedReference is not null)
            {
                Trace(host, Diagnostics.Using_compiler_options_of_project_reference_redirect_0, redirectedReference.SourceFile!.FileName);
            }
        }

        var failedLookupLocations = new List<string>();
        var affectingLocations = new List<string>();
        // Allow type reference directives to opt into `exports` resolution in any resolution mode
        // when a `resolution-mode` override is present.
        var features = GetNodeResolutionFeatures(options);
        if (resolutionMode is not null)
        {
            features |= NodeResolutionFeatures.AllFeatures;
        }

        var moduleResolution = Utilities.GetEmitModuleResolutionKind(options);
        if (resolutionMode == ModuleKind.ESNext && ModuleResolutionKind.Node16 <= moduleResolution && moduleResolution <= ModuleResolutionKind.NodeNext)
        {
            features |= NodeResolutionFeatures.EsmMode;
        }

        var conditions = (features & NodeResolutionFeatures.Exports) != 0
            ? GetConditions(options, resolutionMode)
            : [];
        var diagnostics = new List<IDiagnostic>();
        var moduleResolutionState = new ModuleResolutionState
        {
            CompilerOptions = options,
            Host = host,
            TraceEnabled = traceEnabled,
            FailedLookupLocations = failedLookupLocations,
            AffectingLocations = affectingLocations,
            PackageJsonInfoCache = cache,
            Features = features,
            Conditions = conditions,
            RequestContainingDirectory = containingDirectory,
            ReportDiagnostic = diagnostics.Add,
            IsConfigLookup = false,
            CandidateIsFromPackageJsonField = false,
            ResolvedPackageDirectory = false,
        };
        var resolved = PrimaryLookup();
        var primary = true;
        if (resolved is null)
        {
            resolved = SecondaryLookup();
            primary = false;
        }

        ResolvedTypeReferenceDirective? resolvedTypeReferenceDirective = null;
        if (resolved is not null)
        {
            var (fileName, packageId) = (resolved.FileName, resolved.PackageId);
            var resolvedFileName = fileName;
            string? originalPath = null;
            if (!Js.IsTruthy(options.PreserveSymlinks))
            {
                (resolvedFileName, originalPath) = GetOriginalAndResolvedFileName(fileName, host, traceEnabled);
            }

            resolvedTypeReferenceDirective = new ResolvedTypeReferenceDirective
            {
                Primary = primary,
                ResolvedFileName = resolvedFileName,
                OriginalPath = originalPath,
                PackageId = packageId,
                IsExternalLibraryImport = PathContainsNodeModules(fileName),
            };
        }

        result = new ResolvedTypeReferenceDirectiveWithFailedLookupLocations
        {
            ResolvedTypeReferenceDirective = resolvedTypeReferenceDirective,
            FailedLookupLocations = InitializeResolutionField(failedLookupLocations),
            AffectingLocations = InitializeResolutionField(affectingLocations),
            ResolutionDiagnostics = InitializeResolutionField(diagnostics),
        };
        if (containingDirectory is not null && cache is not null && !cache.IsReadonly)
        {
            cache.GetOrCreateCacheForDirectory(containingDirectory, redirectedReference).Set(typeReferenceDirectiveName, resolutionMode, result);
            if (!Utilities.IsExternalModuleNameRelative(typeReferenceDirectiveName))
            {
                cache.GetOrCreateCacheForNonRelativeName(typeReferenceDirectiveName, resolutionMode, redirectedReference).Set(containingDirectory, result);
            }
        }

        if (traceEnabled)
        {
            TraceTypeReferenceDirectiveResult(result);
        }

        return result;

        void TraceTypeReferenceDirectiveResult(ResolvedTypeReferenceDirectiveWithFailedLookupLocations toTrace)
        {
            if (toTrace.ResolvedTypeReferenceDirective?.ResolvedFileName is null)
            {
                Trace(host, Diagnostics.Type_reference_directive_0_was_not_resolved, typeReferenceDirectiveName);
            }
            else if (toTrace.ResolvedTypeReferenceDirective.PackageId is not null)
            {
                Trace(
                    host,
                    Diagnostics.Type_reference_directive_0_was_successfully_resolved_to_1_with_Package_ID_2_primary_Colon_3,
                    typeReferenceDirectiveName,
                    toTrace.ResolvedTypeReferenceDirective.ResolvedFileName,
                    Utilities.PackageIdToString(toTrace.ResolvedTypeReferenceDirective.PackageId),
                    toTrace.ResolvedTypeReferenceDirective.Primary);
            }
            else
            {
                Trace(
                    host,
                    Diagnostics.Type_reference_directive_0_was_successfully_resolved_to_1_primary_Colon_2,
                    typeReferenceDirectiveName,
                    toTrace.ResolvedTypeReferenceDirective.ResolvedFileName,
                    toTrace.ResolvedTypeReferenceDirective.Primary);
            }
        }

        PathAndPackageId? PrimaryLookup()
        {
            // Check primary library paths
            if (typeRoots is not null && typeRoots.Count != 0)
            {
                if (traceEnabled)
                {
                    Trace(host, Diagnostics.Resolving_with_primary_search_path_0, string.Join(", ", typeRoots));
                }

                return Core.FirstDefined<string, PathAndPackageId>(typeRoots, (typeRoot, _) =>
                {
                    var candidate = GetCandidateFromTypeRoot(typeRoot, typeReferenceDirectiveName, moduleResolutionState);
                    var directoryExists = Utilities.DirectoryProbablyExists(typeRoot, host);
                    if (!directoryExists && traceEnabled)
                    {
                        Trace(host, Diagnostics.Directory_0_does_not_exist_skipping_all_lookups_in_it, typeRoot);
                    }

                    if (Js.IsTruthy(options.TypeRoots))
                    {
                        // Custom typeRoots resolve as file or directory just like we do modules
                        var resolvedFromFile = LoadModuleFromFile(Extensions.Declaration, candidate, !directoryExists, moduleResolutionState);
                        if (resolvedFromFile is not null)
                        {
                            var packageDirectory = ParseNodeModuleFromPath(resolvedFromFile.Path);
                            var packageInfo = packageDirectory is not null
                                ? GetPackageJsonInfo(packageDirectory, onlyRecordFailures: false, moduleResolutionState)
                                : null;
                            return ResolvedTypeScriptOnly(WithPackageId(packageInfo, resolvedFromFile, moduleResolutionState));
                        }
                    }

                    return ResolvedTypeScriptOnly(
                        LoadNodeModuleFromDirectory(Extensions.Declaration, candidate, !directoryExists, moduleResolutionState));
                });
            }

            if (traceEnabled)
            {
                Trace(host, Diagnostics.Root_directory_cannot_be_determined_skipping_primary_search_paths);
            }

            return null;
        }

        PathAndPackageId? SecondaryLookup()
        {
            var initialLocationForSecondaryLookup = containingFile is not null ? Paths.GetDirectoryPath(containingFile) : null;
            if (initialLocationForSecondaryLookup is not null)
            {
                Resolved? secondaryResult = null;
                if (!Js.IsTruthy(options.TypeRoots) || !Core.EndsWith(containingFile!, Utilities.InferredTypesContainingFile))
                {
                    // check secondary locations
                    if (traceEnabled)
                    {
                        Trace(host, Diagnostics.Looking_up_in_node_modules_folder_initial_location_0, initialLocationForSecondaryLookup);
                    }

                    if (!Utilities.IsExternalModuleNameRelative(typeReferenceDirectiveName))
                    {
                        var searchResult = LoadModuleFromNearestNodeModulesDirectory(
                            Extensions.Declaration,
                            typeReferenceDirectiveName,
                            initialLocationForSecondaryLookup,
                            moduleResolutionState,
                            cache: null,
                            redirectedReference: null);
                        secondaryResult = searchResult?.Value;
                    }
                    else
                    {
                        var candidate = NormalizePathForCJSResolution(initialLocationForSecondaryLookup, typeReferenceDirectiveName).Path;
                        secondaryResult = NodeLoadModuleByRelativeName(
                            Extensions.Declaration,
                            candidate,
                            onlyRecordFailures: false,
                            moduleResolutionState,
                            considerPackageJson: true);
                    }
                }
                else if (traceEnabled)
                {
                    Trace(host, Diagnostics.Resolving_type_reference_directive_for_program_that_specifies_custom_typeRoots_skipping_lookup_in_node_modules_folder);
                }

                return ResolvedTypeScriptOnly(secondaryResult);
            }

            if (traceEnabled)
            {
                Trace(host, Diagnostics.Containing_file_is_not_specified_and_root_directory_cannot_be_determined_skipping_lookup_in_node_modules_folder);
            }

            return null;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Resolution-mode features and conditions
    // ---------------------------------------------------------------------------------------------

    private static NodeResolutionFeatures GetNodeResolutionFeatures(ICompilerOptions options)
    {
        var features = NodeResolutionFeatures.None;
        switch (Utilities.GetEmitModuleResolutionKind(options))
        {
            case ModuleResolutionKind.Node16:
                features = NodeResolutionFeatures.Node16Default;
                break;
            case ModuleResolutionKind.NodeNext:
                features = NodeResolutionFeatures.NodeNextDefault;
                break;
            case ModuleResolutionKind.Bundler:
                features = NodeResolutionFeatures.BundlerDefault;
                break;
        }

        if (Js.IsTruthy(options.ResolvePackageJsonExports))
        {
            features |= NodeResolutionFeatures.Exports;
        }
        else if (options.ResolvePackageJsonExports == false)
        {
            features &= ~NodeResolutionFeatures.Exports;
        }

        if (Js.IsTruthy(options.ResolvePackageJsonImports))
        {
            features |= NodeResolutionFeatures.Imports;
        }
        else if (options.ResolvePackageJsonImports == false)
        {
            features &= ~NodeResolutionFeatures.Imports;
        }

        return features;
    }

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:755-785 — <c>getConditions</c>.</summary>
    public static List<string> GetConditions(ICompilerOptions options, ModuleKind? resolutionMode = null)
    {
        var moduleResolution = Utilities.GetEmitModuleResolutionKind(options);
        if (resolutionMode is null)
        {
            if (moduleResolution == ModuleResolutionKind.Bundler)
            {
                // bundler always uses `import` unless explicitly overridden
                resolutionMode = ModuleKind.ESNext;
            }
            else if (moduleResolution == ModuleResolutionKind.Node10)
            {
                // node10 does not support package.json imports/exports without
                // an explicit resolution-mode override on a type-only import
                // (indicated by `esmMode` being set)
                return [];
            }
        }

        // conditions are only used by the node16/nodenext/bundler resolvers - there's no priority order in the list,
        // it's essentially a set (priority is determined by object insertion order in the object we look at).
        var conditions = resolutionMode == ModuleKind.ESNext
            ? new List<string> { "import" }
            : new List<string> { "require" };
        if (!Js.IsTruthy(options.NoDtsResolution))
        {
            conditions.Add("types");
        }

        if (moduleResolution != ModuleResolutionKind.Bundler)
        {
            conditions.Add("node");
        }

        return [.. Core.Concatenate(conditions, options.CustomConditions)!];
    }

    // ---------------------------------------------------------------------------------------------
    // Package.json lookups for the 'types' option
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// ported from: src/compiler/moduleNameResolver.ts:787-811 — <c>resolvePackageNameToPackageJson</c>.
    /// </summary>
    /// <remarks>Does not try <c>@types/${packageName}</c> - use a second pass if needed.</remarks>
    public static PackageJsonInfo? ResolvePackageNameToPackageJson(
        string packageName,
        string containingDirectory,
        ICompilerOptions options,
        IModuleResolutionHost host,
        ModuleResolutionCache? cache)
    {
        var moduleResolutionState = GetTemporaryModuleResolutionState(cache?.GetPackageJsonInfoCache(), host, options);

        return ForEachAncestorDirectoryStoppingAtGlobalCache(host, containingDirectory, ancestorDirectory =>
        {
            if (Paths.GetBaseFileName(ancestorDirectory) != "node_modules")
            {
                var nodeModulesFolder = Paths.CombinePaths(ancestorDirectory, "node_modules");
                var candidate = Paths.CombinePaths(nodeModulesFolder, packageName);
                return GetPackageJsonInfo(candidate, onlyRecordFailures: false, moduleResolutionState);
            }

            return null;
        });
    }

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:813-850 — <c>getAutomaticTypeDirectiveNames</c>.</summary>
    /// <remarks>
    /// Given a set of options, returns the set of type directive names that should be included for this program
    /// automatically. This list could either come from the config file, and/or from enumerating the types root + initial
    /// secondary types lookup location given "*" compat wildcard. More type directives might appear in the program later
    /// as a result of loading actual source files; this list is only the set of defaults that are implicitly included.
    /// </remarks>
    public static List<string> GetAutomaticTypeDirectiveNames(ICompilerOptions options, IModuleResolutionHost host)
    {
        if (!Utilities.UsesWildcardTypes(options))
        {
            return options.Types ?? [];
        }

        // Walk the primary type lookup locations
        var wildcardMatches = new List<string>();
        // PORT-NOTE: TS guards on `host.directoryExists && host.getDirectories`; both are required members of the
        // generated host interface, so the guard is gone (see the file's remarks).
        {
            var typeRoots = GetEffectiveTypeRoots(options, host);
            if (typeRoots is not null)
            {
                foreach (var root in typeRoots)
                {
                    if (host.DirectoryExists(root))
                    {
                        foreach (var typeDirectivePath in host.GetDirectories(root))
                        {
                            var normalized = Paths.NormalizePath(typeDirectivePath);
                            var packageJsonPath = Paths.CombinePaths(root, normalized, "package.json");
                            // `types-publisher` sometimes creates packages with `"typings": null` for packages that
                            // don't provide their own types. See `createNotNeededPackageJSON` in the types-publisher
                            // repo.
                            var packageJson = Utilities.ReadJson(packageJsonPath, host);
                            var isNotNeededPackage = host.FileExists(packageJsonPath)
                                // PORT-NOTE: TS tests `typings === null`, which is false for an absent member: the
                                // member must be present and JSON null.
                                && packageJson.Has("typings")
                                && packageJson.Get("typings") is null;
                            if (!isNotNeededPackage)
                            {
                                var baseFileName = Paths.GetBaseFileName(normalized);

                                // At this stage, skip results with leading dot.
                                if (baseFileName.Length == 0 || baseFileName[0] != '.')
                                {
                                    // Return just the type directive names
                                    wildcardMatches.Add(baseFileName);
                                }
                            }
                        }
                    }
                }
            }
        }

        // Order potentially matters in program construction, so substitute
        // in the wildcard in the position it was specified in the types array
        var flattened = Core.Flatten<string>(Core.Map<string, object?>(
            options.Types,
            (t, _) => t == "*" ? (object?)wildcardMatches : t)!);
        return [.. Core.Deduplicate(flattened, Core.EquateValues)];
    }

    // ---------------------------------------------------------------------------------------------
    // Package.json info predicates
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:925-927 — <c>isPackageJsonInfo</c>.</summary>
    public static bool IsPackageJsonInfo(IPackageJsonInfoCacheEntry? entry) => entry is PackageJsonInfo;

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:930-932 — <c>isMissingPackageJsonInfo</c>.</summary>
    public static bool IsMissingPackageJsonInfo(IPackageJsonInfoCacheEntry? entry) => entry is MissingPackageJsonInfo;

    // ---------------------------------------------------------------------------------------------
    // Compiler-option cache keys
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:944-963 — <c>compilerOptionValueToString</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: JS's <c>"" + value</c> arm covers <c>undefined</c>, numbers, strings, booleans and enums alike (an
    /// enum reads as its numeric value, and an unset option slot — the port's <c>null</c> — as <c>"undefined"</c>). The
    /// object arm iterates own enumerable keys; the option slots are <c>Dictionary</c>-backed, which preserves
    /// insertion order, the same assumption <c>Utilities.GetCompilerOptionValue</c> documents.
    /// </remarks>
    public static string CompilerOptionValueToString(object? value)
    {
        if (value is null || value is not (System.Collections.IDictionary or System.Collections.IEnumerable) || value is string)
        {
            return value switch
            {
                null => "undefined",
                string s => s,
                bool b => b ? "true" : "false",
                Enum e => Convert.ToInt64(e, System.Globalization.CultureInfo.InvariantCulture)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture),
                double d => Js.NumberToString(d),
                System.IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                _ => "",
            };
        }

        if (value is System.Collections.IDictionary dictionary)
        {
            var objectBuilder = new System.Text.StringBuilder("{");
            foreach (System.Collections.DictionaryEntry entry in dictionary)
            {
                objectBuilder.Append(entry.Key).Append(": ").Append(CompilerOptionValueToString(entry.Value));
            }

            return objectBuilder.Append('}').ToString();
        }

        var arrayBuilder = new System.Text.StringBuilder("[");
        var first = true;
        foreach (var element in (System.Collections.IEnumerable)value)
        {
            if (!first)
            {
                arrayBuilder.Append(',');
            }

            first = false;
            arrayBuilder.Append(CompilerOptionValueToString(element));
        }

        return arrayBuilder.Append(']').ToString();
    }

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:961-964 — <c>getKeyForCompilerOptions</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS takes <c>readonly CommandLineOption[]</c>; the metadata table is M8's, so the port takes the
    /// option names (see <c>Utilities.ModuleResolutionOptionDeclarations</c>).
    /// </remarks>
    public static string GetKeyForCompilerOptions(ICompilerOptions options, IList<string> affectingOptionDeclarations)
    {
        var parts = new List<string>(affectingOptionDeclarations.Count);
        foreach (var option in affectingOptionDeclarations)
        {
            parts.Add(CompilerOptionValueToString(Utilities.GetCompilerOptionValue(options, option)));
        }

        return string.Join("|", parts) + "|" + (options.PathsBasePath ?? "undefined");
    }

    // ---------------------------------------------------------------------------------------------
    // Cache factories
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:977-1050 — <c>createCacheWithRedirects</c>.</summary>
    internal static CacheWithRedirects<K, V> CreateCacheWithRedirects<K, V>(
        ICompilerOptions? ownOptions,
        Dictionary<ICompilerOptions, string> optionsToRedirectsKey)
        where K : notnull
        => new(ownOptions, optionsToRedirectsKey);

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:1051-1066 — <c>createPackageJsonInfoCache</c>.</summary>
    public static PackageJsonInfoCache CreatePackageJsonInfoCache(
        string currentDirectory,
        Core.GetCanonicalFileName getCanonicalFileName)
        => new(currentDirectory, getCanonicalFileName);

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:1068-1076 — <c>getOrCreateCache</c>.</summary>
    public static V GetOrCreateCache<K, V>(
        CacheWithRedirects<K, V> cacheWithRedirects,
        IResolvedProjectReference? redirectedReference,
        K key,
        Func<V> create)
        where K : notnull
    {
        var cache = cacheWithRedirects.GetOrCreateMapOfCacheRedirects(redirectedReference);
        if (!cache.TryGetValue(key, out var result))
        {
            result = create();
            cache[key] = result;
        }

        return result;
    }

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:1078-1111 — <c>createPerDirectoryResolutionCache</c>.</summary>
    public static PerDirectoryResolutionCache<T> CreatePerDirectoryResolutionCache<T>(
        string currentDirectory,
        Core.GetCanonicalFileName getCanonicalFileName,
        ICompilerOptions? options,
        Dictionary<ICompilerOptions, string> optionsToRedirectsKey)
        where T : class
        => new(currentDirectory, getCanonicalFileName, options, optionsToRedirectsKey);

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:1115-1117 — <c>createModeAwareCacheKey</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the body already lives at <c>ModeAwareCache&lt;T&gt;.CreateKey</c> (<c>Core/ModeAwareCache.cs</c>,
    /// ported from this same range by an earlier slice); this is the name the reference gives it and the name
    /// <c>NonRelativeNameResolutionCache</c> calls it by.
    /// </remarks>
    public static string CreateModeAwareCacheKey(string specifier, ModuleKind? mode)
        => ModeAwareCache<bool>.CreateKey(specifier, mode);

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:1119-1155 — <c>createModeAwareCache</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the cache body is <c>ModeAwareCache&lt;T&gt;</c> (<c>Core/ModeAwareCache.cs</c>, ported from the same
    /// range); the class constructor is the factory.
    /// </remarks>
    public static ModeAwareCache<T> CreateModeAwareCache<T>() => new();

    internal static string? GetOriginalOrResolvedModuleFileName(ResolvedModuleWithFailedLookupLocations result)
        => result.ResolvedModule is null
            ? null
            : Js.IsTruthy(result.ResolvedModule.OriginalPath)
                ? result.ResolvedModule.OriginalPath
                : result.ResolvedModule.ResolvedFileName;

    internal static string? GetOriginalOrResolvedTypeReferenceFileName(ResolvedTypeReferenceDirectiveWithFailedLookupLocations result)
        => result.ResolvedTypeReferenceDirective is null
            ? null
            : Js.IsTruthy(result.ResolvedTypeReferenceDirective.OriginalPath)
                ? result.ResolvedTypeReferenceDirective.OriginalPath
                : result.ResolvedTypeReferenceDirective.ResolvedFileName;

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:1166-1203 — <c>createNonRelativeNameResolutionCache</c>.</summary>
    public static NonRelativeNameResolutionCache<T> CreateNonRelativeNameResolutionCache<T>(
        string currentDirectory,
        Core.GetCanonicalFileName getCanonicalFileName,
        ICompilerOptions? options,
        Func<T, string?> getResolvedFileName,
        Dictionary<ICompilerOptions, string> optionsToRedirectsKey)
        where T : class
        => new(currentDirectory, getCanonicalFileName, options, getResolvedFileName, optionsToRedirectsKey);

    /// <summary>
    /// The reference's <c>createModuleOrTypeReferenceResolutionCache</c> (moduleNameResolver.ts:1276-1325) is the
    /// constructor of <see cref="ModuleOrTypeReferenceResolutionCache{T}"/> — the two caches below are its two
    /// instantiations, which is what its unchecked <c>as ModuleResolutionCache</c>/<c>as TypeReferenceDirectiveResolutionCache</c>
    /// cast meant. The maps keyed by compiler options are held by reference, as the reference's <c>Map</c> is.
    /// </summary>
    private static Dictionary<ICompilerOptions, string> NewOptionsToRedirectsKey()
        => new(ReferenceEqualityComparer.Instance);

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:1327-1358 — <c>createModuleResolutionCache</c>.</summary>
    public static ModuleResolutionCache CreateModuleResolutionCache(
        string currentDirectory,
        Core.GetCanonicalFileName getCanonicalFileName,
        ICompilerOptions? options = null,
        IPackageJsonInfoCache? packageJsonInfoCache = null,
        Dictionary<ICompilerOptions, string>? optionsToRedirectsKey = null)
        => new ModuleResolutionCache(
            currentDirectory,
            getCanonicalFileName,
            options,
            packageJsonInfoCache,
            optionsToRedirectsKey ?? NewOptionsToRedirectsKey());

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:1360-1390 — <c>createTypeReferenceDirectiveResolutionCache</c>.</summary>
    public static TypeReferenceDirectiveResolutionCache CreateTypeReferenceDirectiveResolutionCache(
        string currentDirectory,
        Core.GetCanonicalFileName getCanonicalFileName,
        ICompilerOptions? options = null,
        IPackageJsonInfoCache? packageJsonInfoCache = null,
        Dictionary<ICompilerOptions, string>? optionsToRedirectsKey = null)
        => new TypeReferenceDirectiveResolutionCache(
            currentDirectory,
            getCanonicalFileName,
            options,
            packageJsonInfoCache,
            optionsToRedirectsKey ?? NewOptionsToRedirectsKey());

    // ---------------------------------------------------------------------------------------------
    // Public entry points
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:1392-1395 — <c>getOptionsForLibraryResolution</c>.</summary>
    public static ICompilerOptions GetOptionsForLibraryResolution(ICompilerOptions options)
        => new CompilerOptions
        {
            ModuleResolution = ModuleResolutionKind.Node10,
            TraceResolution = options.TraceResolution,
        };

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:1397-1399 — <c>resolveLibrary</c>.</summary>
    public static ResolvedModuleWithFailedLookupLocations ResolveLibrary(
        string libraryName,
        string resolveFrom,
        ICompilerOptions compilerOptions,
        IModuleResolutionHost host,
        ModuleResolutionCache? cache = null)
        => ResolveModuleName(libraryName, resolveFrom, GetOptionsForLibraryResolution(compilerOptions), host, cache);

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:1401-1404 — <c>resolveModuleNameFromCache</c>.</summary>
    public static ResolvedModuleWithFailedLookupLocations? ResolveModuleNameFromCache(
        string moduleName,
        string containingFile,
        ModuleResolutionCache cache,
        ModuleKind? mode = null)
    {
        var containingDirectory = Paths.GetDirectoryPath(containingFile);
        return cache.GetFromDirectoryCache(moduleName, mode, containingDirectory, redirectedReference: null);
    }

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:1406-1492 — <c>resolveModuleName</c>.</summary>
    public static ResolvedModuleWithFailedLookupLocations ResolveModuleName(
        string moduleName,
        string containingFile,
        ICompilerOptions compilerOptions,
        IModuleResolutionHost host,
        ModuleResolutionCache? cache = null,
        IResolvedProjectReference? redirectedReference = null,
        ModuleKind? resolutionMode = null)
    {
        var traceEnabled = IsTraceEnabled(compilerOptions, host);
        if (redirectedReference is not null)
        {
            compilerOptions = redirectedReference.CommandLine!.Options!;
        }

        if (traceEnabled)
        {
            Trace(host, Diagnostics.Resolving_module_0_from_1, moduleName, containingFile);
            if (redirectedReference is not null)
            {
                Trace(host, Diagnostics.Using_compiler_options_of_project_reference_redirect_0, redirectedReference.SourceFile!.FileName);
            }
        }

        var containingDirectory = Paths.GetDirectoryPath(containingFile);
        var result = cache?.GetFromDirectoryCache(moduleName, resolutionMode, containingDirectory, redirectedReference);

        if (result is not null)
        {
            if (traceEnabled)
            {
                Trace(host, Diagnostics.Resolution_for_module_0_was_found_in_cache_from_location_1, moduleName, containingDirectory);
            }
        }
        else
        {
            var moduleResolution = compilerOptions.ModuleResolution;
            if (moduleResolution is null)
            {
                moduleResolution = Utilities.GetEmitModuleResolutionKind(compilerOptions);
                if (traceEnabled)
                {
                    Trace(host, Diagnostics.Module_resolution_kind_is_not_specified_using_0, ModuleResolutionKindName(moduleResolution.Value));
                }
            }
            else if (traceEnabled)
            {
                Trace(host, Diagnostics.Explicitly_specified_module_resolution_kind_Colon_0, ModuleResolutionKindName(moduleResolution.Value));
            }

            switch (moduleResolution.Value)
            {
                case ModuleResolutionKind.Node16:
                    result = Node16ModuleNameResolver(moduleName, containingFile, compilerOptions, host, cache, redirectedReference, resolutionMode);
                    break;
                case ModuleResolutionKind.NodeNext:
                    result = NodeNextModuleNameResolver(moduleName, containingFile, compilerOptions, host, cache, redirectedReference, resolutionMode);
                    break;
                case ModuleResolutionKind.Node10:
                    result = NodeModuleNameResolver(
                        moduleName,
                        containingFile,
                        compilerOptions,
                        host,
                        cache,
                        redirectedReference,
                        resolutionMode is not null ? GetConditions(compilerOptions, resolutionMode) : null);
                    break;
                case ModuleResolutionKind.Classic:
                    result = ClassicNameResolver(moduleName, containingFile, compilerOptions, host, cache, redirectedReference);
                    break;
                case ModuleResolutionKind.Bundler:
                    result = BundlerModuleNameResolver(
                        moduleName,
                        containingFile,
                        compilerOptions,
                        host,
                        cache,
                        redirectedReference,
                        resolutionMode is not null ? GetConditions(compilerOptions, resolutionMode) : null);
                    break;
                default:
                    return Debug.Fail<ResolvedModuleWithFailedLookupLocations>($"Unexpected moduleResolution: {moduleResolution}");
            }

            if (cache is not null && !cache.IsReadonly)
            {
                cache.GetOrCreateCacheForDirectory(containingDirectory, redirectedReference).Set(moduleName, resolutionMode, result);
                if (!Utilities.IsExternalModuleNameRelative(moduleName))
                {
                    // put result in per-module name cache
                    cache.GetOrCreateCacheForNonRelativeName(moduleName, resolutionMode, redirectedReference).Set(containingDirectory, result);
                }
            }
        }

        if (traceEnabled)
        {
            if (result.ResolvedModule is not null)
            {
                if (result.ResolvedModule.PackageId is not null)
                {
                    Trace(host, Diagnostics.Module_name_0_was_successfully_resolved_to_1_with_Package_ID_2, moduleName, result.ResolvedModule.ResolvedFileName, Utilities.PackageIdToString(result.ResolvedModule.PackageId));
                }
                else
                {
                    Trace(host, Diagnostics.Module_name_0_was_successfully_resolved_to_1, moduleName, result.ResolvedModule.ResolvedFileName);
                }
            }
            else
            {
                Trace(host, Diagnostics.Module_name_0_was_not_resolved, moduleName);
            }
        }

        return result;
    }

    private static string ModuleResolutionKindName(ModuleResolutionKind kind) => kind switch
    {
        ModuleResolutionKind.Classic => "Classic",
        // TS's reverse enum mapping resolves the duplicated value 2 to the later declaration.
        ModuleResolutionKind.Node10 => "Node10",
        ModuleResolutionKind.Node16 => "Node16",
        ModuleResolutionKind.NodeNext => "NodeNext",
        ModuleResolutionKind.Bundler => "Bundler",
        _ => Enum.GetName(kind) ?? "",
    };

    /// <summary>JS's <c>String.prototype.slice</c>, whose out-of-range arguments clamp instead of throwing.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>path.slice(n)</c> appears throughout the reference and always with a non-negative start; C#'s
    /// range operator throws when the start is past the end, so this keeps JS's "empty string" answer. Negative starts
    /// count from the end, matching <c>slice</c>.
    /// </remarks>
    private static string JsSlice(string s, int start, int? end = null)
    {
        var len = s.Length;
        var begin = start < 0 ? Math.Max(len + start, 0) : Math.Min(start, len);
        var e = end is null ? len : (end.Value < 0 ? Math.Max(len + end.Value, 0) : Math.Min(end.Value, len));
        return e <= begin ? "" : s[begin..e];
    }
}
