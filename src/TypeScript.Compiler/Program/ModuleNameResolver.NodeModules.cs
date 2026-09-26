namespace TypeScript.Compiler;

// ported from: src/compiler/moduleNameResolver.ts:2997-3428
//   2997-2999  loadModuleFromNearestNodeModulesDirectory
//   3001-3004  loadModuleFromNearestNodeModulesDirectoryTypesScope
//   3006-3049  loadModuleFromNearestNodeModulesDirectoryWorker
//   3051-3062  forEachAncestorDirectoryStoppingAtGlobalCache
//   3064-3089  loadModuleFromImmediateNodeModulesDirectory
//   3091-3167  loadModuleFromSpecificNodeModulesDirectory
//   3169-3197  tryLoadModuleUsingPaths
//   3199-3240  mangleScopedPackageName and friends
//   3242-3259  tryFindNonRelativeModuleNameInCache
//   3261-3330  classicNameResolver
//   3332-3352  resolveFromTypeRoot
//   3354-3362  shouldAllowImportingTsExtension
//   3364-3406  loadModuleFromGlobalCache
/// <summary>
/// The node_modules walk, the <c>paths</c> loader, the name-mangling helpers and the classic (non-node) resolver.
/// </summary>
internal static partial class ModuleNameResolver
{
    // ---------------------------------------------------------------------------------------------
    // The node_modules walk
    // ---------------------------------------------------------------------------------------------

    private static SearchResult<Resolved>? LoadModuleFromNearestNodeModulesDirectory(
        Extensions extensions,
        string moduleName,
        string directory,
        ModuleResolutionState state,
        ModuleResolutionCache? cache,
        IResolvedProjectReference? redirectedReference)
        => LoadModuleFromNearestNodeModulesDirectoryWorker(extensions, moduleName, directory, state, typesScopeOnly: false, cache, redirectedReference);

    private static SearchResult<Resolved>? LoadModuleFromNearestNodeModulesDirectoryTypesScope(
        string moduleName,
        string directory,
        ModuleResolutionState state)
        // Extensions parameter here doesn't actually matter, because typesOnly ensures we're just doing @types lookup,
        // which is always DtsOnly.
        => LoadModuleFromNearestNodeModulesDirectoryWorker(Extensions.Declaration, moduleName, directory, state, typesScopeOnly: true, cache: null, redirectedReference: null);

    private static SearchResult<Resolved>? LoadModuleFromNearestNodeModulesDirectoryWorker(
        Extensions extensions,
        string moduleName,
        string directory,
        ModuleResolutionState state,
        bool typesScopeOnly,
        ModuleResolutionCache? cache,
        IResolvedProjectReference? redirectedReference)
    {
        ModuleKind? mode;
        if (state.Features == NodeResolutionFeatures.None)
        {
            mode = null;
        }
        else if ((state.Features & NodeResolutionFeatures.EsmMode) != 0 || state.Conditions.Contains("import"))
        {
            mode = ModuleKind.ESNext;
        }
        else
        {
            mode = ModuleKind.CommonJS;
        }
        // Do (up to) two passes through node_modules:
        //   1. For each ancestor node_modules directory, try to find:
        //      i.  TS/DTS files in the implementation package
        //      ii. DTS files in the @types package
        //   2. For each ancestor node_modules directory, try to find:
        //      i.  JS files in the implementation package
        var priorityExtensions = extensions & (Extensions.TypeScript | Extensions.Declaration);
        var secondaryExtensions = extensions & ~(Extensions.TypeScript | Extensions.Declaration);
        // (1)
        if (priorityExtensions != 0)
        {
            TraceIfEnabled(state, Diagnostics.Searching_all_ancestor_node_modules_directories_for_preferred_extensions_Colon_0, FormatExtensions(priorityExtensions));
            var priorityResult = Lookup(priorityExtensions);
            if (priorityResult is not null) return priorityResult;
        }

        // (2)
        if (secondaryExtensions != 0 && !typesScopeOnly)
        {
            TraceIfEnabled(state, Diagnostics.Searching_all_ancestor_node_modules_directories_for_fallback_extensions_Colon_0, FormatExtensions(secondaryExtensions));
            return Lookup(secondaryExtensions);
        }

        return null;

        SearchResult<Resolved>? Lookup(Extensions lookupExtensions)
            => ForEachAncestorDirectoryStoppingAtGlobalCache<SearchResult<Resolved>>(
                state.Host,
                Paths.NormalizeSlashes(directory),
                ancestorDirectory =>
                {
                    // Dont go past global cache location
                    if (Paths.GetBaseFileName(ancestorDirectory) != "node_modules")
                    {
                        var resolutionFromCache = TryFindNonRelativeModuleNameInCache(cache, moduleName, mode, ancestorDirectory, redirectedReference, state);
                        if (resolutionFromCache is not null)
                        {
                            return resolutionFromCache;
                        }

                        return ToSearchResult(LoadModuleFromImmediateNodeModulesDirectory(lookupExtensions, moduleName, ancestorDirectory, state, typesScopeOnly, cache, redirectedReference));
                    }

                    return null;
                });
    }

    /// <summary>Set by the callback of <see cref="ForEachAncestorDirectoryStoppingAtGlobalCache{T}"/> to stop the walk.</summary>
    private static readonly object StopAncestorWalk = new();

    /// <summary>
    /// ported from: src/compiler/moduleNameResolver.ts:3051-3062 — <c>forEachAncestorDirectoryStoppingAtGlobalCache</c>.
    /// </summary>
    /// <remarks>
    /// Calls <paramref name="callback"/> on <c>directory</c> and every ancestor directory it has, returning the first
    /// defined result. Stops at the global cache location.
    /// <para>
    /// PORT-NOTE: TS's callback answers <c>false</c> to stop the walk and the outer <c>|| undefined</c> turns that
    /// <c>false</c> into <c>undefined</c>; the port's <c>Paths.ForEachAncestorDirectory</c> stops on any non-null result,
    /// so the "stop" answer is a private sentinel that is mapped back to <c>null</c> here.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's host parameter is <c>ModuleResolutionHost | ModuleSpecifierResolutionHost</c> and only
    /// <c>getGlobalTypingsCacheLocation</c> is read from it; every call site in this file passes a
    /// <c>ModuleResolutionHost</c>, so that is the declared type (C# has no structural typing — M6-late's
    /// <c>moduleSpecifiers.ts</c> caller can add the overload when it lands).
    /// </para>
    /// </remarks>
    public static T? ForEachAncestorDirectoryStoppingAtGlobalCache<T>(
        IModuleResolutionHost host,
        string directory,
        Func<string, T?> callback)
        where T : class
    {
        var globalCache = host.GetGlobalTypingsCacheLocation();
        var result = Paths.ForEachAncestorDirectory<object>(directory, ancestorDirectory =>
        {
            var callbackResult = callback(ancestorDirectory);
            if (callbackResult is not null)
            {
                return callbackResult;
            }

            if (ancestorDirectory == globalCache)
            {
                return StopAncestorWalk;
            }

            return null;
        });
        return result is T typed ? typed : null;
    }

    private static Resolved? LoadModuleFromImmediateNodeModulesDirectory(
        Extensions extensions,
        string moduleName,
        string directory,
        ModuleResolutionState state,
        bool typesScopeOnly,
        ModuleResolutionCache? cache,
        IResolvedProjectReference? redirectedReference)
    {
        var nodeModulesFolder = Paths.CombinePaths(directory, "node_modules");
        var nodeModulesFolderExists = Utilities.DirectoryProbablyExists(nodeModulesFolder, state.Host);
        if (!nodeModulesFolderExists && state.TraceEnabled)
        {
            Trace(state.Host, Diagnostics.Directory_0_does_not_exist_skipping_all_lookups_in_it, nodeModulesFolder);
        }

        if (!typesScopeOnly)
        {
            var packageResult = LoadModuleFromSpecificNodeModulesDirectory(extensions, moduleName, nodeModulesFolder, nodeModulesFolderExists, state, cache, redirectedReference);
            if (packageResult is not null)
            {
                return packageResult;
            }
        }

        if ((extensions & Extensions.Declaration) != 0)
        {
            var nodeModulesAtTypes = Paths.CombinePaths(nodeModulesFolder, "@types");
            var nodeModulesAtTypesExists = nodeModulesFolderExists;
            if (nodeModulesFolderExists && !Utilities.DirectoryProbablyExists(nodeModulesAtTypes, state.Host))
            {
                if (state.TraceEnabled)
                {
                    Trace(state.Host, Diagnostics.Directory_0_does_not_exist_skipping_all_lookups_in_it, nodeModulesAtTypes);
                }

                nodeModulesAtTypesExists = false;
            }

            return LoadModuleFromSpecificNodeModulesDirectory(
                Extensions.Declaration,
                MangleScopedPackageNameWithTrace(moduleName, state),
                nodeModulesAtTypes,
                nodeModulesAtTypesExists,
                state,
                cache,
                redirectedReference);
        }

        return null;
    }

    private static Resolved? LoadModuleFromSpecificNodeModulesDirectory(
        Extensions extensions,
        string moduleName,
        string nodeModulesDirectory,
        bool nodeModulesDirectoryExists,
        ModuleResolutionState state,
        ModuleResolutionCache? cache,
        IResolvedProjectReference? redirectedReference)
    {
        var candidate = Paths.NormalizePath(Paths.CombinePaths(nodeModulesDirectory, moduleName));
        var (packageName, rest) = ParsePackageName(moduleName);
        var packageDirectory = Paths.CombinePaths(nodeModulesDirectory, packageName);

        PackageJsonInfo? rootPackageInfo = null;
        // First look for a nested package.json, as in `node_modules/foo/bar/package.json`.
        var packageInfo = GetPackageJsonInfo(candidate, !nodeModulesDirectoryExists, state);
        // But only if we're not respecting export maps (if we are, we might redirect around this location)
        var useNestedPackageJson = false;
        if (rest != "" && packageInfo is not null)
        {
            if ((state.Features & NodeResolutionFeatures.Exports) == 0)
            {
                useNestedPackageJson = true;
            }
            else
            {
                rootPackageInfo = GetPackageJsonInfo(packageDirectory, !nodeModulesDirectoryExists, state);
                useNestedPackageJson = !HasExportsField(rootPackageInfo?.Contents.PackageJsonContent ?? new JsObject());
            }
        }

        if (useNestedPackageJson)
        {
            var fromFile = LoadModuleFromFile(extensions, candidate, !nodeModulesDirectoryExists, state);
            if (fromFile is not null)
            {
                return NoPackageId(fromFile);
            }

            var fromDirectory = LoadNodeModuleFromDirectoryWorker(
                extensions,
                candidate,
                !nodeModulesDirectoryExists,
                state,
                packageInfo);
            return WithPackageId(packageInfo, fromDirectory, state);
        }

        ResolutionKindSpecificLoader loader = (loaderExtensions, loaderCandidate, loaderOnlyRecordFailures, loaderState, _) =>
        {
            PathAndExtension? pathAndExtension = null;
            if (Js.IsTruthy(rest) || (loaderState.Features & NodeResolutionFeatures.EsmMode) == 0)
            {
                pathAndExtension = LoadModuleFromFile(loaderExtensions, loaderCandidate, loaderOnlyRecordFailures, loaderState);
            }

            pathAndExtension ??= LoadNodeModuleFromDirectoryWorker(
                loaderExtensions,
                loaderCandidate,
                loaderOnlyRecordFailures,
                loaderState,
                packageInfo);
            if (
                pathAndExtension is null
                && !Js.IsTruthy(rest)
                && packageInfo is not null
                && (!packageInfo.Contents.PackageJsonContent.Has("exports") || packageInfo.Contents.PackageJsonContent.Get("exports") is null)
                && (loaderState.Features & NodeResolutionFeatures.EsmMode) != 0
            )
            {
                // EsmMode disables index lookup in `loadNodeModuleFromDirectoryWorker` generally, however non-relative
                // package resolutions still assume a default `index.js` entrypoint if no `main` or `exports` are present
                pathAndExtension = LoadModuleFromFile(loaderExtensions, Paths.CombinePaths(loaderCandidate, "index.js"), loaderOnlyRecordFailures, loaderState);
            }

            return WithPackageId(packageInfo, pathAndExtension, state);
        };

        if (rest != "")
        {
            // Previous `packageInfo` may have been from a nested package.json; ensure we have the one from the package
            // root now.
            packageInfo = rootPackageInfo ?? GetPackageJsonInfo(packageDirectory, !nodeModulesDirectoryExists, state);
        }

        if (packageInfo is not null)
        {
            state.ResolvedPackageDirectory = true;
        }

        // package exports are higher priority than file/directory/typesVersions lookups and (and, if there's exports
        // present, blocks them)
        if (packageInfo is not null && Js.IsTruthy(packageInfo.Contents.PackageJsonContent.Get("exports")) && (state.Features & NodeResolutionFeatures.Exports) != 0)
        {
            return LoadModuleFromExports(packageInfo, extensions, Paths.CombinePaths(".", rest), state, cache, redirectedReference)?.Value;
        }

        var versionPaths = rest != "" && packageInfo is not null ? GetVersionPathsOfPackageJsonInfo(packageInfo, state) : null;
        if (versionPaths is not null)
        {
            if (state.TraceEnabled)
            {
                Trace(
                    state.Host,
                    Diagnostics.package_json_has_a_typesVersions_entry_0_that_matches_compiler_version_1_looking_for_a_pattern_to_match_module_name_2,
                    versionPaths.Version,
                    VersionInfo.Version,
                    rest);
            }

            var packageDirectoryExists = nodeModulesDirectoryExists && Utilities.DirectoryProbablyExists(packageDirectory, state.Host);
            var pathPatterns = Utilities.TryParsePatterns(versionPaths.Paths);
            var fromPaths = TryLoadModuleUsingPaths(extensions, rest, packageDirectory, versionPaths.Paths, pathPatterns, loader, !packageDirectoryExists, state);
            if (fromPaths is not null)
            {
                return fromPaths.Value;
            }
        }

        return loader(extensions, candidate, !nodeModulesDirectoryExists, state, null);
    }

    private static bool HasExportsField(JsObject packageJsonContent) => packageJsonContent.Has("exports");

    // ---------------------------------------------------------------------------------------------
    // The paths loader
    // ---------------------------------------------------------------------------------------------

    private static SearchResult<Resolved>? TryLoadModuleUsingPaths(
        Extensions extensions,
        string moduleName,
        string baseDirectory,
        Dictionary<string, List<string>> paths,
        Utilities.ParsedPatterns pathPatterns,
        ResolutionKindSpecificLoader loader,
        bool onlyRecordFailures,
        ModuleResolutionState state)
    {
        var matchedPattern = Utilities.MatchPatternOrExact(pathPatterns, moduleName);
        if (matchedPattern is not null)
        {
            var matchedStar = matchedPattern is string ? null : Core.MatchedText((Core.Pattern)matchedPattern, moduleName);
            var matchedPatternText = matchedPattern is string matchedPatternString
                ? matchedPatternString
                : Core.PatternText((Core.Pattern)matchedPattern);
            if (state.TraceEnabled)
            {
                Trace(state.Host, Diagnostics.Module_name_0_matched_pattern_1, moduleName, matchedPatternText);
            }

            var resolved = Core.ForEach<string, Resolved>(paths.GetValueOrDefault(matchedPatternText), (subst, _) =>
            {
                var path = matchedStar is not null ? Utilities.ReplaceFirstStar(subst, matchedStar) : subst;
                // When baseUrl is not specified, the command line parser resolves relative paths to the config file
                // location.
                var candidate = Paths.NormalizePath(Paths.CombinePaths(baseDirectory, path));
                if (state.TraceEnabled)
                {
                    Trace(state.Host, Diagnostics.Trying_substitution_0_candidate_module_location_Colon_1, subst, path);
                }

                // A path mapping may have an extension, in contrast to an import, which should omit it.
                var extension = Utilities.TryGetExtensionFromPath(subst);
                if (extension is not null)
                {
                    var resolvedByFile = TryFile(candidate, onlyRecordFailures, state);
                    if (resolvedByFile is not null)
                    {
                        return NoPackageId(new PathAndExtension
                        {
                            Path = resolvedByFile,
                            Ext = extension,
                            ResolvedUsingTsExtension = null,
                        });
                    }
                }

                return loader(extensions, candidate, onlyRecordFailures || !Utilities.DirectoryProbablyExists(Paths.GetDirectoryPath(candidate), state.Host), state, null);
            });
            return new SearchResult<Resolved> { Value = resolved };
        }

        return null;
    }

    // ---------------------------------------------------------------------------------------------
    // Scoped package names
    // ---------------------------------------------------------------------------------------------

    /// <summary>Double underscores are used in DefinitelyTyped to delimit scoped packages.</summary>
    private const string MangledScopedPackageSeparator = "__";

    /// <summary>For a scoped package, we must look in <c>@types/foo__bar</c> instead of <c>@types/@foo/bar</c>.</summary>
    private static string MangleScopedPackageNameWithTrace(string packageName, ModuleResolutionState state)
    {
        var mangled = MangleScopedPackageName(packageName);
        if (state.TraceEnabled && mangled != packageName)
        {
            Trace(state.Host, Diagnostics.Scoped_package_detected_looking_in_0, mangled);
        }

        return mangled;
    }

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:3211-3214 — <c>getTypesPackageName</c>.</summary>
    public static string GetTypesPackageName(string packageName)
        => $"@types/{MangleScopedPackageName(packageName)}";

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:3216-3225 — <c>mangleScopedPackageName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS writes <c>packageName.replace(directorySeparator, "__")</c> — <c>String.prototype.replace</c> with a
    /// string pattern, i.e. the <em>first</em> separator only; <see cref="string.Replace(string,string)"/> would take
    /// every one, so the first index is taken explicitly.
    /// </remarks>
    public static string MangleScopedPackageName(string packageName)
    {
        if (Core.StartsWith(packageName, "@"))
        {
            var separatorIndex = packageName.IndexOf(Paths.DirectorySeparator, StringComparison.Ordinal);
            if (separatorIndex != -1)
            {
                // Take off the "@" (TS slices the replaced name, which is the same thing)
                return packageName[1..separatorIndex] + MangledScopedPackageSeparator + packageName[(separatorIndex + Paths.DirectorySeparator.Length)..];
            }
        }

        return packageName;
    }

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:3227-3234 — <c>getPackageNameFromTypesPackageName</c>.</summary>
    public static string GetPackageNameFromTypesPackageName(string mangledName)
    {
        var withoutAtTypePrefix = Core.RemovePrefix(mangledName, "@types/");
        if (withoutAtTypePrefix != mangledName)
        {
            return UnmangleScopedPackageName(withoutAtTypePrefix);
        }

        return mangledName;
    }

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:3236-3240 — <c>unmangleScopedPackageName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: as in <c>mangleScopedPackageName</c>, TS's <c>replace</c> with a string pattern replaces the first
    /// occurrence only, so the first index is taken explicitly.
    /// </remarks>
    public static string UnmangleScopedPackageName(string typesPackageName)
    {
        var separatorIndex = typesPackageName.IndexOf(MangledScopedPackageSeparator, StringComparison.Ordinal);
        return separatorIndex != -1
            ? "@" + typesPackageName[..separatorIndex] + Paths.DirectorySeparator + typesPackageName[(separatorIndex + MangledScopedPackageSeparator.Length)..]
            : typesPackageName;
    }

    // ---------------------------------------------------------------------------------------------
    // The cache probe and the classic resolver
    // ---------------------------------------------------------------------------------------------

    private static SearchResult<Resolved>? TryFindNonRelativeModuleNameInCache(
        INonRelativeNameResolutionCache<ResolvedModuleWithFailedLookupLocations>? cache,
        string moduleName,
        ModuleKind? mode,
        string containingDirectory,
        IResolvedProjectReference? redirectedReference,
        ModuleResolutionState state)
    {
        var result = cache?.GetFromNonRelativeNameCache(moduleName, mode, containingDirectory, redirectedReference);
        if (result is not null)
        {
            if (state.TraceEnabled)
            {
                Trace(state.Host, Diagnostics.Resolution_for_module_0_was_found_in_cache_from_location_1, moduleName, containingDirectory);
            }

            state.ResultFromCache = result;
            return new SearchResult<Resolved>
            {
                Value = result.ResolvedModule is null
                    ? null
                    : new Resolved
                    {
                        Path = result.ResolvedModule.ResolvedFileName!,
                        OriginalPath = Js.IsTruthy(result.ResolvedModule.OriginalPath) ? result.ResolvedModule.OriginalPath : null,
                        OriginalPathIsTrue = !Js.IsTruthy(result.ResolvedModule.OriginalPath),
                        Extension = result.ResolvedModule.Extension!,
                        PackageId = result.ResolvedModule.PackageId,
                        ResolvedUsingTsExtension = result.ResolvedModule.ResolvedUsingTsExtension,
                    },
            };
        }

        return null;
    }

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:3261-3330 — <c>classicNameResolver</c>.</summary>
    public static ResolvedModuleWithFailedLookupLocations ClassicNameResolver(
        string moduleName,
        string containingFile,
        ICompilerOptions compilerOptions,
        IModuleResolutionHost host,
        INonRelativeModuleNameResolutionCache<ResolvedModuleWithFailedLookupLocations>? cache = null,
        IResolvedProjectReference? redirectedReference = null)
    {
        var traceEnabled = IsTraceEnabled(compilerOptions, host);
        var failedLookupLocations = new List<string>();
        var affectingLocations = new List<string>();
        var containingDirectory = Paths.GetDirectoryPath(containingFile);
        var diagnostics = new List<IDiagnostic>();
        var state = new ModuleResolutionState
        {
            CompilerOptions = compilerOptions,
            Host = host,
            TraceEnabled = traceEnabled,
            FailedLookupLocations = failedLookupLocations,
            AffectingLocations = affectingLocations,
            PackageJsonInfoCache = cache,
            Features = NodeResolutionFeatures.None,
            Conditions = [],
            RequestContainingDirectory = containingDirectory,
            ReportDiagnostic = diagnostics.Add,
            IsConfigLookup = false,
            CandidateIsFromPackageJsonField = false,
            ResolvedPackageDirectory = false,
        };

        var resolved = TryResolve(Extensions.TypeScript | Extensions.Declaration)
            ?? TryResolve(Extensions.JavaScript | (Js.IsTruthy(compilerOptions.ResolveJsonModule) ? Extensions.Json : 0));
        // No originalPath because classic resolution doesn't resolve realPath
        return CreateResolvedModuleWithFailedLookupLocationsHandlingSymlink(
            moduleName,
            resolved?.Value,
            resolved?.Value is not null && PathContainsNodeModules(resolved.Value.Path),
            failedLookupLocations,
            affectingLocations,
            diagnostics,
            state,
            cache);

        SearchResult<Resolved>? TryResolve(Extensions extensions)
        {
            var resolvedUsingSettings = TryLoadModuleUsingOptionalResolutionSettings(
                extensions,
                moduleName,
                containingDirectory,
                (settingsExtensions, candidate, onlyRecordFailures, settingsState, _) => LoadModuleFromFileNoPackageId(settingsExtensions, candidate, onlyRecordFailures, settingsState),
                state);
            if (resolvedUsingSettings is not null)
            {
                return new SearchResult<Resolved> { Value = resolvedUsingSettings };
            }

            if (!Utilities.IsExternalModuleNameRelative(moduleName))
            {
                // Climb up parent directories looking for a module.
                var resolvedByAncestor = ForEachAncestorDirectoryStoppingAtGlobalCache<SearchResult<Resolved>>(
                    state.Host,
                    containingDirectory,
                    directory =>
                    {
                        var resolutionFromCache = TryFindNonRelativeModuleNameInCache(cache, moduleName, mode: null, directory, redirectedReference, state);
                        if (resolutionFromCache is not null)
                        {
                            return resolutionFromCache;
                        }

                        var searchName = Paths.NormalizePath(Paths.CombinePaths(directory, moduleName));
                        return ToSearchResult(LoadModuleFromFileNoPackageId(extensions, searchName, onlyRecordFailures: false, state));
                    });
                if (resolvedByAncestor is not null) return resolvedByAncestor;
                if ((extensions & (Extensions.TypeScript | Extensions.Declaration)) != 0)
                {
                    // If we didn't find the file normally, look it up in @types.
                    var resolvedFromTypes = LoadModuleFromNearestNodeModulesDirectoryTypesScope(moduleName, containingDirectory, state);
                    if ((extensions & Extensions.Declaration) != 0)
                    {
                        resolvedFromTypes ??= ResolveFromTypeRoot(moduleName, state);
                    }

                    return resolvedFromTypes;
                }
            }
            else
            {
                var candidate = Paths.NormalizePath(Paths.CombinePaths(containingDirectory, moduleName));
                return ToSearchResult(LoadModuleFromFileNoPackageId(extensions, candidate, onlyRecordFailures: false, state));
            }

            return null;
        }
    }

    private static SearchResult<Resolved>? ResolveFromTypeRoot(string moduleName, ModuleResolutionState state)
    {
        if (state.CompilerOptions.TypeRoots is not { } typeRoots) return null;
        foreach (var typeRoot in typeRoots)
        {
            var candidate = GetCandidateFromTypeRoot(typeRoot, moduleName, state);
            var directoryExists = Utilities.DirectoryProbablyExists(typeRoot, state.Host);
            if (!directoryExists && state.TraceEnabled)
            {
                Trace(state.Host, Diagnostics.Directory_0_does_not_exist_skipping_all_lookups_in_it, typeRoot);
            }

            var resolvedFromFile = LoadModuleFromFile(Extensions.Declaration, candidate, !directoryExists, state);
            if (resolvedFromFile is not null)
            {
                var packageDirectory = ParseNodeModuleFromPath(resolvedFromFile.Path);
                var packageInfo = packageDirectory is not null
                    ? GetPackageJsonInfo(packageDirectory, onlyRecordFailures: false, state)
                    : null;
                return ToSearchResult(WithPackageId(packageInfo, resolvedFromFile, state));
            }

            var resolved = LoadNodeModuleFromDirectory(Extensions.Declaration, candidate, !directoryExists, state);
            if (resolved is not null) return ToSearchResult(resolved);
        }

        return null;
    }

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:3354-3362 — <c>shouldAllowImportingTsExtension</c>.</summary>
    /// <remarks>
    /// Program errors validate that <c>noEmit</c> or <c>emitDeclarationOnly</c> is also set, so this function doesn't
    /// check them to avoid propagating errors.
    /// </remarks>
    public static bool ShouldAllowImportingTsExtension(ICompilerOptions compilerOptions, string? fromFileName = null)
        => Utilities.GetAllowImportingTsExtensions(compilerOptions)
            || (fromFileName is not null && Parser.IsDeclarationFileName(fromFileName));

    /// <summary>
    /// ported from: src/compiler/moduleNameResolver.ts:3364-3406 — <c>loadModuleFromGlobalCache</c>.
    /// </summary>
    /// <remarks>
    /// A host may load a module from a global cache of typings. This is the minimum code needed to expose that
    /// functionality; the rest is in the host.
    /// </remarks>
    public static ResolvedModuleWithFailedLookupLocations LoadModuleFromGlobalCache(
        string moduleName,
        string? projectName,
        ICompilerOptions compilerOptions,
        IModuleResolutionHost host,
        string globalCache,
        IPackageJsonInfoCache packageJsonInfoCache)
    {
        var traceEnabled = IsTraceEnabled(compilerOptions, host);
        if (traceEnabled)
        {
            Trace(host, Diagnostics.Auto_discovery_for_typings_is_enabled_in_project_0_Running_extra_resolution_pass_for_module_1_using_cache_location_2, projectName, moduleName, globalCache);
        }

        var failedLookupLocations = new List<string>();
        var affectingLocations = new List<string>();
        var diagnostics = new List<IDiagnostic>();
        var state = new ModuleResolutionState
        {
            CompilerOptions = compilerOptions,
            Host = host,
            TraceEnabled = traceEnabled,
            FailedLookupLocations = failedLookupLocations,
            AffectingLocations = affectingLocations,
            PackageJsonInfoCache = packageJsonInfoCache,
            Features = NodeResolutionFeatures.None,
            Conditions = [],
            RequestContainingDirectory = null,
            ReportDiagnostic = diagnostics.Add,
            IsConfigLookup = false,
            CandidateIsFromPackageJsonField = false,
            ResolvedPackageDirectory = false,
        };
        var resolved = LoadModuleFromImmediateNodeModulesDirectory(
            Extensions.Declaration,
            moduleName,
            globalCache,
            state,
            typesScopeOnly: false,
            cache: null,
            redirectedReference: null);
        return CreateResolvedModuleWithFailedLookupLocations(
            resolved,
            isExternalLibraryImport: true,
            failedLookupLocations,
            affectingLocations,
            diagnostics,
            state.ResultFromCache,
            cache: null);
    }
}
