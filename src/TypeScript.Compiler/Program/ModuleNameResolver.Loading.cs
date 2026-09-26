namespace TypeScript.Compiler;

// ported from: src/compiler/moduleNameResolver.ts:1493-2278
//   1495-1553  tryLoadModuleUsingOptionalResolutionSettings / paths / rootDirs / baseUrl
//   1555-1676  the three optional-settings loaders
//   1678-1685  resolveJSModule
//   1712-1723  node16ModuleNameResolver
//   1725-1736  nodeNextModuleNameResolver
//   1738-1748  nodeNextModuleNameResolverWorker
//   1750-1764  tryResolveJSModuleWorker
//   1766-1776  bundlerModuleNameResolver
//   1778-1797  nodeModuleNameResolver
//   1799-1801  nodeNextJsonConfigResolver
//   1803-1964  nodeModuleNameResolverWorker
//   1966-2038  normalizePathForCJSResolution / realPath / nodeLoadModuleByRelativeName / nodeModulesPathPart /
//              pathContainsNodeModules / parseNodeModuleFromPath / moveToNextDirectorySeparatorIfAvailable
//   2064-2215  the file-level loaders (loadModuleFromFile … tryFileLookup)
//   2217-2221  loadNodeModuleFromDirectory
//   2228-2358  getEntrypointsFromPackageJsonInfo / loadEntrypointsFromExportMap
/// <summary>
/// The resolution-mode loaders: how each module resolution kind turns a candidate path into a file, and the file-level
/// loaders they share.
/// </summary>
internal static partial class ModuleNameResolver
{
    // ---------------------------------------------------------------------------------------------
    // Optional resolution settings (paths / rootDirs / baseUrl)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// ported from: src/compiler/moduleNameResolver.ts:1495-1503 — <c>tryLoadModuleUsingOptionalResolutionSettings</c>.
    /// </summary>
    /// <remarks>
    /// Any module resolution kind can be augmented with the optional settings 'baseUrl', 'paths' and 'rootDirs'. They
    /// mitigate the differences between the design-time structure of the project and its runtime counterpart, so the same
    /// import name can be resolved by the TypeScript compiler and by the runtime module loader. If these settings are
    /// set, the loading procedure tries them first and falls back to the standard routine when they fail.
    /// </remarks>
    private static Resolved? TryLoadModuleUsingOptionalResolutionSettings(
        Extensions extensions,
        string moduleName,
        string containingDirectory,
        ResolutionKindSpecificLoader loader,
        ModuleResolutionState state)
    {
        var resolved = TryLoadModuleUsingPathsIfEligible(extensions, moduleName, loader, state);
        if (resolved is not null) return resolved.Value;

        if (!Utilities.IsExternalModuleNameRelative(moduleName))
        {
            return TryLoadModuleUsingBaseUrl(extensions, moduleName, loader, state);
        }

        return TryLoadModuleUsingRootDirs(extensions, moduleName, containingDirectory, loader, state);
    }

    private static SearchResult<Resolved>? TryLoadModuleUsingPathsIfEligible(
        Extensions extensions,
        string moduleName,
        ResolutionKindSpecificLoader loader,
        ModuleResolutionState state)
    {
        var (baseUrl, paths) = (state.CompilerOptions.BaseUrl, state.CompilerOptions.Paths);
        if (Js.IsTruthy(paths) && !Paths.PathIsRelative(moduleName))
        {
            if (state.TraceEnabled)
            {
                if (Js.IsTruthy(baseUrl))
                {
                    Trace(state.Host, Diagnostics.baseUrl_option_is_set_to_0_using_this_value_to_resolve_non_relative_module_name_1, baseUrl, moduleName);
                }

                Trace(state.Host, Diagnostics.paths_option_is_specified_looking_for_a_pattern_to_match_module_name_0, moduleName);
            }

            // Always defined when 'paths' is defined
            var baseDirectory = Utilities.GetPathsBasePath(state.CompilerOptions, state.Host)!;
            var pathPatterns = Utilities.TryParsePatterns(paths!);
            return TryLoadModuleUsingPaths(extensions, moduleName, baseDirectory, paths!, pathPatterns, loader, onlyRecordFailures: false, state);
        }

        return null;
    }

    private static Resolved? TryLoadModuleUsingRootDirs(
        Extensions extensions,
        string moduleName,
        string containingDirectory,
        ResolutionKindSpecificLoader loader,
        ModuleResolutionState state)
    {
        if (state.CompilerOptions.RootDirs is not { } rootDirs)
        {
            return null;
        }

        if (state.TraceEnabled)
        {
            Trace(state.Host, Diagnostics.rootDirs_option_is_set_using_it_to_resolve_relative_module_name_0, moduleName);
        }

        var candidate = Paths.NormalizePath(Paths.CombinePaths(containingDirectory, moduleName));

        string? matchedRootDir = null;
        string? matchedNormalizedPrefix = null;
        foreach (var rootDir in rootDirs)
        {
            // rootDirs are expected to be absolute; in case of tsconfig.json this will happen automatically - the
            // compiler will expand relative names using the location of tsconfig.json as base location.
            var normalizedRoot = Paths.NormalizePath(rootDir);
            if (!Core.EndsWith(normalizedRoot, Paths.DirectorySeparator))
            {
                normalizedRoot += Paths.DirectorySeparator;
            }

            var isLongestMatchingPrefix = Core.StartsWith(candidate, normalizedRoot)
                && (matchedNormalizedPrefix is null || matchedNormalizedPrefix.Length < normalizedRoot.Length);

            if (state.TraceEnabled)
            {
                Trace(state.Host, Diagnostics.Checking_if_0_is_the_longest_matching_prefix_for_1_2, normalizedRoot, candidate, isLongestMatchingPrefix);
            }

            if (isLongestMatchingPrefix)
            {
                matchedNormalizedPrefix = normalizedRoot;
                matchedRootDir = rootDir;
            }
        }

        if (matchedNormalizedPrefix is not null)
        {
            if (state.TraceEnabled)
            {
                Trace(state.Host, Diagnostics.Longest_matching_prefix_for_0_is_1, candidate, matchedNormalizedPrefix);
            }

            var suffix = JsSlice(candidate, matchedNormalizedPrefix.Length);

            // first - try to load from an initial location
            if (state.TraceEnabled)
            {
                Trace(state.Host, Diagnostics.Loading_0_from_the_root_dir_1_candidate_location_2, suffix, matchedNormalizedPrefix, candidate);
            }

            var resolvedFileName = loader(extensions, candidate, !Utilities.DirectoryProbablyExists(containingDirectory, state.Host), state, null);
            if (resolvedFileName is not null)
            {
                return resolvedFileName;
            }

            if (state.TraceEnabled)
            {
                Trace(state.Host, Diagnostics.Trying_other_entries_in_rootDirs);
            }

            // then try to resolve using remaining entries in rootDirs
            foreach (var rootDir in rootDirs)
            {
                if (rootDir == matchedRootDir)
                {
                    // skip the initially matched entry
                    continue;
                }

                var otherCandidate = Paths.CombinePaths(Paths.NormalizePath(rootDir), suffix);
                if (state.TraceEnabled)
                {
                    Trace(state.Host, Diagnostics.Loading_0_from_the_root_dir_1_candidate_location_2, suffix, rootDir, otherCandidate);
                }

                var baseDirectory = Paths.GetDirectoryPath(otherCandidate);
                var resolvedFromOtherEntry = loader(extensions, otherCandidate, !Utilities.DirectoryProbablyExists(baseDirectory, state.Host), state, null);
                if (resolvedFromOtherEntry is not null)
                {
                    return resolvedFromOtherEntry;
                }
            }

            if (state.TraceEnabled)
            {
                Trace(state.Host, Diagnostics.Module_resolution_using_rootDirs_has_failed);
            }
        }

        return null;
    }

    private static Resolved? TryLoadModuleUsingBaseUrl(
        Extensions extensions,
        string moduleName,
        ResolutionKindSpecificLoader loader,
        ModuleResolutionState state)
    {
        var baseUrl = state.CompilerOptions.BaseUrl;
        if (!Js.IsTruthy(baseUrl))
        {
            return null;
        }

        if (state.TraceEnabled)
        {
            Trace(state.Host, Diagnostics.baseUrl_option_is_set_to_0_using_this_value_to_resolve_non_relative_module_name_1, baseUrl, moduleName);
        }

        var candidate = Paths.NormalizePath(Paths.CombinePaths(baseUrl, moduleName));
        if (state.TraceEnabled)
        {
            Trace(state.Host, Diagnostics.Resolving_module_name_0_relative_to_base_url_1_2, moduleName, baseUrl, candidate);
        }

        return loader(extensions, candidate, !Utilities.DirectoryProbablyExists(Paths.GetDirectoryPath(candidate), state.Host), state, null);
    }

    /// <summary>
    /// ported from: src/compiler/moduleNameResolver.ts:1678-1685 — <c>resolveJSModule</c>.
    /// </summary>
    /// <remarks>
    /// Exposes the resolution logic so the Node module resolution logic can be used from arbitrary locations. There is no
    /// way to do this with <c>require()</c>. Throws when the module cannot be resolved.
    /// <para>
    /// PORT-NOTE: TS throws <c>new Error(...)</c>; the port's stand-in for an untyped JS error is
    /// <see cref="InvalidOperationException"/> (the same choice <c>Core/Debug.cs</c> makes for <c>Debug.fail</c>).
    /// </para>
    /// </remarks>
    public static string ResolveJSModule(string moduleName, string initialDir, IModuleResolutionHost host)
    {
        var result = TryResolveJSModuleWorker(moduleName, initialDir, host);
        var resolvedModule = result.ResolvedModule;
        if (resolvedModule is null)
        {
            throw new InvalidOperationException(
                $"Could not resolve JS module '{moduleName}' starting at '{initialDir}'. Looked in: {string.Join(", ", result.FailedLookupLocations ?? [])}");
        }

        return resolvedModule.ResolvedFileName!;
    }

    // ---------------------------------------------------------------------------------------------
    // The per-mode resolvers
    // ---------------------------------------------------------------------------------------------

    private static ResolvedModuleWithFailedLookupLocations Node16ModuleNameResolver(
        string moduleName,
        string containingFile,
        ICompilerOptions compilerOptions,
        IModuleResolutionHost host,
        ModuleResolutionCache? cache = null,
        IResolvedProjectReference? redirectedReference = null,
        ModuleKind? resolutionMode = null)
        => NodeNextModuleNameResolverWorker(
            NodeResolutionFeatures.Node16Default,
            moduleName,
            containingFile,
            compilerOptions,
            host,
            cache,
            redirectedReference,
            resolutionMode);

    private static ResolvedModuleWithFailedLookupLocations NodeNextModuleNameResolver(
        string moduleName,
        string containingFile,
        ICompilerOptions compilerOptions,
        IModuleResolutionHost host,
        ModuleResolutionCache? cache = null,
        IResolvedProjectReference? redirectedReference = null,
        ModuleKind? resolutionMode = null)
        => NodeNextModuleNameResolverWorker(
            NodeResolutionFeatures.NodeNextDefault,
            moduleName,
            containingFile,
            compilerOptions,
            host,
            cache,
            redirectedReference,
            resolutionMode);

    private static ResolvedModuleWithFailedLookupLocations NodeNextModuleNameResolverWorker(
        NodeResolutionFeatures features,
        string moduleName,
        string containingFile,
        ICompilerOptions compilerOptions,
        IModuleResolutionHost host,
        ModuleResolutionCache? cache,
        IResolvedProjectReference? redirectedReference,
        ModuleKind? resolutionMode,
        IList<string>? conditions = null)
    {
        var containingDirectory = Paths.GetDirectoryPath(containingFile);

        // es module file or cjs-like input file: use a variant of the legacy cjs resolver that supports the selected
        // modern features
        var esmMode = resolutionMode == ModuleKind.ESNext ? NodeResolutionFeatures.EsmMode : NodeResolutionFeatures.None;
        var extensions = Js.IsTruthy(compilerOptions.NoDtsResolution)
            ? Extensions.ImplementationFiles
            : Extensions.TypeScript | Extensions.JavaScript | Extensions.Declaration;
        if (Utilities.GetResolveJsonModule(compilerOptions))
        {
            extensions |= Extensions.Json;
        }

        return NodeModuleNameResolverWorker(features | esmMode, moduleName, containingDirectory, compilerOptions, host, cache, extensions, isConfigLookup: false, redirectedReference, conditions);
    }

    private static ResolvedModuleWithFailedLookupLocations TryResolveJSModuleWorker(
        string moduleName,
        string initialDir,
        IModuleResolutionHost host)
        => NodeModuleNameResolverWorker(
            NodeResolutionFeatures.None,
            moduleName,
            initialDir,
            // PORT-NOTE: TS passes an object literal with two fields; the port's options model is the generated
            // interface, so the two-field literal is a concrete CompilerOptions with every other slot unset — which is
            // exactly what the literal means.
            new CompilerOptions { ModuleResolution = ModuleResolutionKind.Node10, AllowJs = true },
            host,
            cache: null,
            Extensions.JavaScript,
            isConfigLookup: false,
            redirectedReference: null,
            conditions: null);

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:1766-1776 — <c>bundlerModuleNameResolver</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares two overloads (the public one without <paramref name="conditions"/>); C# keeps one method
    /// whose last parameter is optional.
    /// </remarks>
    public static ResolvedModuleWithFailedLookupLocations BundlerModuleNameResolver(
        string moduleName,
        string containingFile,
        ICompilerOptions compilerOptions,
        IModuleResolutionHost host,
        ModuleResolutionCache? cache = null,
        IResolvedProjectReference? redirectedReference = null,
        IList<string>? conditions = null)
    {
        var containingDirectory = Paths.GetDirectoryPath(containingFile);
        var extensions = Js.IsTruthy(compilerOptions.NoDtsResolution)
            ? Extensions.ImplementationFiles
            : Extensions.TypeScript | Extensions.JavaScript | Extensions.Declaration;
        if (Utilities.GetResolveJsonModule(compilerOptions))
        {
            extensions |= Extensions.Json;
        }

        return NodeModuleNameResolverWorker(GetNodeResolutionFeatures(compilerOptions), moduleName, containingDirectory, compilerOptions, host, cache, extensions, isConfigLookup: false, redirectedReference, conditions);
    }

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:1778-1797 — <c>nodeModuleNameResolver</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares two overloads (the public one without <paramref name="conditions"/>/<paramref name="isConfigLookup"/>);
    /// C# keeps one method with optional trailing parameters.
    /// </remarks>
    public static ResolvedModuleWithFailedLookupLocations NodeModuleNameResolver(
        string moduleName,
        string containingFile,
        ICompilerOptions compilerOptions,
        IModuleResolutionHost host,
        ModuleResolutionCache? cache = null,
        IResolvedProjectReference? redirectedReference = null,
        IList<string>? conditions = null,
        bool isConfigLookup = false)
    {
        Extensions extensions;
        if (isConfigLookup)
        {
            extensions = Extensions.Json;
        }
        else if (Js.IsTruthy(compilerOptions.NoDtsResolution))
        {
            extensions = Extensions.ImplementationFiles;
            if (Utilities.GetResolveJsonModule(compilerOptions)) extensions |= Extensions.Json;
        }
        else
        {
            extensions = Utilities.GetResolveJsonModule(compilerOptions)
                ? Extensions.TypeScript | Extensions.JavaScript | Extensions.Declaration | Extensions.Json
                : Extensions.TypeScript | Extensions.JavaScript | Extensions.Declaration;
        }

        return NodeModuleNameResolverWorker(
            conditions is not null ? NodeResolutionFeatures.AllFeatures : NodeResolutionFeatures.None,
            moduleName,
            Paths.GetDirectoryPath(containingFile),
            compilerOptions,
            host,
            cache,
            extensions,
            isConfigLookup,
            redirectedReference,
            conditions);
    }

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:1799-1801 — <c>nodeNextJsonConfigResolver</c>.</summary>
    public static ResolvedModuleWithFailedLookupLocations NodeNextJsonConfigResolver(
        string moduleName,
        string containingFile,
        IModuleResolutionHost host)
        => NodeModuleNameResolverWorker(
            NodeResolutionFeatures.NodeNextDefault,
            moduleName,
            Paths.GetDirectoryPath(containingFile),
            // PORT-NOTE: TS's one-field object literal for this lookup, as a concrete options object.
            new CompilerOptions { ModuleResolution = ModuleResolutionKind.NodeNext },
            host,
            cache: null,
            Extensions.Json,
            isConfigLookup: true,
            redirectedReference: null,
            conditions: null);

    /// <summary>Value of the <c>tryResolve</c> search: the reference's anonymous <c>{ resolved, isExternalLibraryImport }</c>.</summary>
    private sealed class TryResolveValue
    {
        public required Resolved Resolved { get; init; }

        public required bool IsExternalLibraryImport { get; init; }
    }

    private static ResolvedModuleWithFailedLookupLocations NodeModuleNameResolverWorker(
        NodeResolutionFeatures features,
        string moduleName,
        string containingDirectory,
        ICompilerOptions compilerOptions,
        IModuleResolutionHost host,
        ModuleResolutionCache? cache,
        Extensions extensions,
        bool isConfigLookup,
        IResolvedProjectReference? redirectedReference,
        IList<string>? conditions)
    {
        var traceEnabled = IsTraceEnabled(compilerOptions, host);

        var failedLookupLocations = new List<string>();
        var affectingLocations = new List<string>();
        var moduleResolution = Utilities.GetEmitModuleResolutionKind(compilerOptions);
        conditions ??= GetConditions(
            compilerOptions,
            moduleResolution is ModuleResolutionKind.Bundler or ModuleResolutionKind.Node10
                ? null
                : (features & NodeResolutionFeatures.EsmMode) != 0 ? ModuleKind.ESNext : ModuleKind.CommonJS);

        var diagnostics = new List<IDiagnostic>();
        var state = new ModuleResolutionState
        {
            CompilerOptions = compilerOptions,
            Host = host,
            TraceEnabled = traceEnabled,
            FailedLookupLocations = failedLookupLocations,
            AffectingLocations = affectingLocations,
            PackageJsonInfoCache = cache,
            Features = features,
            Conditions = [.. conditions],
            RequestContainingDirectory = containingDirectory,
            ReportDiagnostic = diagnostics.Add,
            IsConfigLookup = isConfigLookup,
            CandidateIsFromPackageJsonField = false,
            ResolvedPackageDirectory = false,
        };
        if (traceEnabled && Utilities.ModuleResolutionSupportsPackageJsonExportsAndImports(moduleResolution))
        {
            Trace(
                host,
                Diagnostics.Resolving_in_0_mode_with_conditions_1,
                (features & NodeResolutionFeatures.EsmMode) != 0 ? "ESM" : "CJS",
                string.Join(", ", state.Conditions.Select(static c => $"'{c}'")));
        }

        SearchResult<TryResolveValue>? result;
        if (moduleResolution == ModuleResolutionKind.Node10)
        {
            var priorityExtensions = extensions & (Extensions.TypeScript | Extensions.Declaration);
            var secondaryExtensions = extensions & ~(Extensions.TypeScript | Extensions.Declaration);
            result = priorityExtensions != 0 && TryResolve(priorityExtensions, state) is { } priorityResult
                ? priorityResult
                : secondaryExtensions != 0 && TryResolve(secondaryExtensions, state) is { } secondaryResult
                    ? secondaryResult
                    : null;
        }
        else
        {
            result = TryResolve(extensions, state);
        }

        string? alternateResult = null;
        if (state.ResolvedPackageDirectory && !isConfigLookup && !Utilities.IsExternalModuleNameRelative(moduleName))
        {
            var wantedTypesButGotJs = result?.Value is not null
                && (extensions & (Extensions.TypeScript | Extensions.Declaration)) != 0
                && !ExtensionIsOk(Extensions.TypeScript | Extensions.Declaration, result.Value.Resolved.Extension);
            if (
                result?.Value?.IsExternalLibraryImport == true
                && wantedTypesButGotJs
                && (features & NodeResolutionFeatures.Exports) != 0
                && conditions.Contains("import")
            )
            {
                TraceIfEnabled(state, Diagnostics.Resolution_of_non_relative_name_failed_trying_with_modern_Node_resolution_features_disabled_to_see_if_npm_library_needs_configuration_update);
                var diagnosticState = new ModuleResolutionState(state)
                {
                    Features = state.Features & ~NodeResolutionFeatures.Exports,
                    ReportDiagnostic = static _ => { },
                };
                var diagnosticResult = TryResolve(extensions & (Extensions.TypeScript | Extensions.Declaration), diagnosticState);
                if (diagnosticResult?.Value?.IsExternalLibraryImport == true)
                {
                    alternateResult = diagnosticResult.Value.Resolved.Path;
                }
            }
            else if (
                (result?.Value is null || wantedTypesButGotJs)
                && moduleResolution == ModuleResolutionKind.Node10
            )
            {
                TraceIfEnabled(state, Diagnostics.Resolution_of_non_relative_name_failed_trying_with_moduleResolution_bundler_to_see_if_project_may_need_configuration_update);
                var diagnosticsCompilerOptions = new CompilerOptions(state.CompilerOptions)
                {
                    ModuleResolution = ModuleResolutionKind.Bundler,
                };
                var diagnosticState = new ModuleResolutionState(state)
                {
                    CompilerOptions = diagnosticsCompilerOptions,
                    Features = NodeResolutionFeatures.BundlerDefault,
                    Conditions = GetConditions(diagnosticsCompilerOptions),
                    ReportDiagnostic = static _ => { },
                };
                var diagnosticResult = TryResolve(extensions & (Extensions.TypeScript | Extensions.Declaration), diagnosticState);
                if (diagnosticResult?.Value?.IsExternalLibraryImport == true)
                {
                    alternateResult = diagnosticResult.Value.Resolved.Path;
                }
            }
        }

        return CreateResolvedModuleWithFailedLookupLocationsHandlingSymlink(
            moduleName,
            result?.Value?.Resolved,
            result?.Value?.IsExternalLibraryImport,
            failedLookupLocations,
            affectingLocations,
            diagnostics,
            state,
            cache,
            alternateResult);

        SearchResult<TryResolveValue>? TryResolve(Extensions localExtensions, ModuleResolutionState localState)
        {
            ResolutionKindSpecificLoader loader = (extensions2, candidate, onlyRecordFailures, loaderState, _) =>
                NodeLoadModuleByRelativeName(extensions2, candidate, onlyRecordFailures, loaderState, considerPackageJson: true);
            var resolved = TryLoadModuleUsingOptionalResolutionSettings(localExtensions, moduleName, containingDirectory, loader, localState);
            if (resolved is not null)
            {
                return ToSearchResult(new TryResolveValue
                {
                    Resolved = resolved,
                    IsExternalLibraryImport = PathContainsNodeModules(resolved.Path),
                });
            }

            if (!Utilities.IsExternalModuleNameRelative(moduleName))
            {
                if ((features & NodeResolutionFeatures.Imports) != 0 && Core.StartsWith(moduleName, "#"))
                {
                    var fromImports = LoadModuleFromImports(localExtensions, moduleName, containingDirectory, localState, cache, redirectedReference);
                    if (fromImports is not null)
                    {
                        return fromImports.Value is null
                            ? null
                            : new SearchResult<TryResolveValue>
                            {
                                Value = new TryResolveValue { Resolved = fromImports.Value, IsExternalLibraryImport = false },
                            };
                    }
                }

                if ((features & NodeResolutionFeatures.SelfName) != 0)
                {
                    var fromSelfName = LoadModuleFromSelfNameReference(localExtensions, moduleName, containingDirectory, localState, cache, redirectedReference);
                    if (fromSelfName is not null)
                    {
                        return fromSelfName.Value is null
                            ? null
                            : new SearchResult<TryResolveValue>
                            {
                                Value = new TryResolveValue { Resolved = fromSelfName.Value, IsExternalLibraryImport = false },
                            };
                    }
                }

                if (moduleName.Contains(':'))
                {
                    if (traceEnabled)
                    {
                        Trace(host, Diagnostics.Skipping_module_0_that_looks_like_an_absolute_URI_target_file_types_Colon_1, moduleName, FormatExtensions(localExtensions));
                    }

                    return null;
                }

                if (traceEnabled)
                {
                    Trace(host, Diagnostics.Loading_module_0_from_node_modules_folder_target_file_types_Colon_1, moduleName, FormatExtensions(localExtensions));
                }

                var resolvedFromNodeModules = LoadModuleFromNearestNodeModulesDirectory(localExtensions, moduleName, containingDirectory, localState, cache, redirectedReference);
                if ((localExtensions & Extensions.Declaration) != 0)
                {
                    resolvedFromNodeModules ??= ResolveFromTypeRoot(moduleName, localState);
                }

                // For node_modules lookups, get the real path so that multiple accesses to an `npm link`-ed module do not
                // create duplicate files.
                return resolvedFromNodeModules is null
                    ? null
                    : new SearchResult<TryResolveValue>
                    {
                        Value = resolvedFromNodeModules.Value is null
                            ? null
                            : new TryResolveValue { Resolved = resolvedFromNodeModules.Value, IsExternalLibraryImport = true },
                    };
            }

            var (relativeCandidate, parts) = NormalizePathForCJSResolution(containingDirectory, moduleName);
            var resolvedByRelativeName = NodeLoadModuleByRelativeName(localExtensions, relativeCandidate, onlyRecordFailures: false, localState, considerPackageJson: true);

            // Treat an explicit "node_modules" import as an external library import.
            return resolvedByRelativeName is null
                ? null
                : ToSearchResult(new TryResolveValue
                {
                    Resolved = resolvedByRelativeName,
                    IsExternalLibraryImport = Core.Contains(parts, "node_modules"),
                });
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Candidate paths
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// ported from: src/compiler/moduleNameResolver.ts:1966-1972 — <c>normalizePathForCJSResolution</c>.
    /// </summary>
    /// <remarks>
    /// Importing from "." inside a containing directory "/foo" makes <c>normalizePath</c> answer "/foo", which loses the
    /// information that <c>foo</c> is a directory we meant to look inside of. The Node CommonJS resolution algorithm does
    /// not call this out, but module paths ending in "." are normalized to "./" before the algorithm runs.
    /// </remarks>
    private static (string Path, List<string> Parts) NormalizePathForCJSResolution(string containingDirectory, string moduleName)
    {
        var combined = Paths.CombinePaths(containingDirectory, moduleName);
        var parts = Paths.GetPathComponents(combined);
        var lastPart = Core.LastOrUndefined(parts);
        var path = lastPart is "." or ".."
            ? Paths.EnsureTrailingDirectorySeparator(Paths.NormalizePath(combined))
            : Paths.NormalizePath(combined);
        return (path, parts);
    }

    private static string RealPath(string path, IModuleResolutionHost host, bool traceEnabled)
    {
        // PORT-NOTE: TS answers `path` unchanged when the host has no `realpath`; the generated host interface declares
        // it required, so the call is always made.
        var real = Paths.NormalizePath(host.Realpath(path));
        if (traceEnabled)
        {
            Trace(host, Diagnostics.Resolving_real_path_for_0_result_1, path, real);
        }

        return real;
    }

    private static Resolved? NodeLoadModuleByRelativeName(
        Extensions extensions,
        string candidate,
        bool onlyRecordFailures,
        ModuleResolutionState state,
        bool considerPackageJson)
    {
        if (state.TraceEnabled)
        {
            Trace(state.Host, Diagnostics.Loading_module_as_file_Slash_folder_candidate_module_location_0_target_file_types_Colon_1, candidate, FormatExtensions(extensions));
        }

        if (!Paths.HasTrailingDirectorySeparator(candidate))
        {
            if (!onlyRecordFailures)
            {
                var parentOfCandidate = Paths.GetDirectoryPath(candidate);
                if (!Utilities.DirectoryProbablyExists(parentOfCandidate, state.Host))
                {
                    if (state.TraceEnabled)
                    {
                        Trace(state.Host, Diagnostics.Directory_0_does_not_exist_skipping_all_lookups_in_it, parentOfCandidate);
                    }

                    onlyRecordFailures = true;
                }
            }

            var resolvedFromFile = LoadModuleFromFile(extensions, candidate, onlyRecordFailures, state);
            if (resolvedFromFile is not null)
            {
                var packageDirectory = considerPackageJson ? ParseNodeModuleFromPath(resolvedFromFile.Path) : null;
                var packageInfo = packageDirectory is not null
                    ? GetPackageJsonInfo(packageDirectory, onlyRecordFailures: false, state)
                    : null;
                return WithPackageId(packageInfo, resolvedFromFile, state);
            }
        }

        if (!onlyRecordFailures)
        {
            var candidateExists = Utilities.DirectoryProbablyExists(candidate, state.Host);
            if (!candidateExists)
            {
                if (state.TraceEnabled)
                {
                    Trace(state.Host, Diagnostics.Directory_0_does_not_exist_skipping_all_lookups_in_it, candidate);
                }

                onlyRecordFailures = true;
            }
        }

        // esm mode relative imports shouldn't do any directory lookups (either inside `package.json` files or implicit
        // `index.js`es). This is a notable departure from cjs norms, where `./foo/pkg` could have been redirected by
        // `./foo/pkg/package.json` to an arbitrary location!
        if ((state.Features & NodeResolutionFeatures.EsmMode) == 0)
        {
            return LoadNodeModuleFromDirectory(extensions, candidate, onlyRecordFailures, state, considerPackageJson);
        }

        return null;
    }

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:2026 — <c>nodeModulesPathPart</c>.</summary>
    public const string NodeModulesPathPart = "/node_modules/";

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:2028-2030 — <c>pathContainsNodeModules</c>.</summary>
    public static bool PathContainsNodeModules(string path) => path.Contains(NodeModulesPathPart);

    /// <summary>
    /// ported from: src/compiler/moduleNameResolver.ts:2040-2057 — <c>parseNodeModuleFromPath</c>.
    /// </summary>
    /// <remarks>
    /// Called on the successfully resolved path from <c>loadModuleFromFile</c> (not needed for
    /// <c>loadModuleFromNodeModules</c>, which looks up the <c>package.json</c> as part of resolution).
    /// <para>
    /// <paramref name="resolved"/> is the file; the package directory it belongs to is what is returned. For
    /// <c>blah/node_modules/foo/index.d.ts</c> that is "foo"; for <c>/node_modules/@types/foo/bar/index.d.ts</c> it is
    /// "@types/foo".
    /// </para>
    /// </remarks>
    public static string? ParseNodeModuleFromPath(string resolved, bool? isFolder = null)
    {
        var path = Paths.NormalizePath(resolved);
        var idx = path.LastIndexOf(NodeModulesPathPart, StringComparison.Ordinal);
        if (idx == -1)
        {
            return null;
        }

        var indexAfterNodeModules = idx + NodeModulesPathPart.Length;
        var indexAfterPackageName = MoveToNextDirectorySeparatorIfAvailable(path, indexAfterNodeModules, isFolder);
        // PORT-NOTE: TS's `path.charCodeAt(i) === CharacterCodes.at` maps an out-of-range read to NaN, which never
        // equals the code for '@'; the explicit bounds test is the same comparison.
        if (indexAfterNodeModules < path.Length && path[indexAfterNodeModules] == '@')
        {
            indexAfterPackageName = MoveToNextDirectorySeparatorIfAvailable(path, indexAfterPackageName, isFolder);
        }

        return path[..indexAfterPackageName];
    }

    private static int MoveToNextDirectorySeparatorIfAvailable(string path, int prevSeparatorIndex, bool? isFolder)
    {
        // PORT-NOTE: JS clamps `indexOf`'s start position to the string's length (a `prevSeparatorIndex` at the end of
        // the path is the "@types"-only case); `string.IndexOf` throws when the start index is past the end, so the
        // same clamp is written out here.
        var nextSeparatorIndex = path.IndexOf('/', Math.Min(prevSeparatorIndex + 1, path.Length));
        return nextSeparatorIndex == -1
            ? isFolder == true ? path.Length : prevSeparatorIndex
            : nextSeparatorIndex;
    }

    // ---------------------------------------------------------------------------------------------
    // File loaders
    // ---------------------------------------------------------------------------------------------

    private static Resolved? LoadModuleFromFileNoPackageId(
        Extensions extensions,
        string candidate,
        bool onlyRecordFailures,
        ModuleResolutionState state)
        => NoPackageId(LoadModuleFromFile(extensions, candidate, onlyRecordFailures, state));

    /// <summary>
    /// ported from: src/compiler/moduleNameResolver.ts:2072-2087 — <c>loadModuleFromFile</c>.
    /// </summary>
    /// <param name="onlyRecordFailures">
    /// When true the function does not try to load files but records all attempts as failures. That is needed when we know
    /// upfront that all load attempts will fail (because the containing folder does not exist) but still have to record
    /// every failed lookup location.
    /// </param>
    private static PathAndExtension? LoadModuleFromFile(
        Extensions extensions,
        string candidate,
        bool onlyRecordFailures,
        ModuleResolutionState state)
    {
        // ./foo.js -> ./foo.ts
        var resolvedByReplacingExtension = LoadModuleFromFileNoImplicitExtensions(extensions, candidate, onlyRecordFailures, state);
        if (resolvedByReplacingExtension is not null)
        {
            return resolvedByReplacingExtension;
        }

        // ./foo -> ./foo.ts
        if ((state.Features & NodeResolutionFeatures.EsmMode) == 0)
        {
            // First, try adding an extension. An import of "foo" could be matched by a file "foo.ts", or "foo.js" by
            // "foo.js.ts".
            var resolvedByAddingExtension = TryAddingExtensions(candidate, extensions, "", onlyRecordFailures, state);
            if (resolvedByAddingExtension is not null)
            {
                return resolvedByAddingExtension;
            }
        }

        return null;
    }

    private static PathAndExtension? LoadModuleFromFileNoImplicitExtensions(
        Extensions extensions,
        string candidate,
        bool onlyRecordFailures,
        ModuleResolutionState state)
    {
        var filename = Paths.GetBaseFileName(candidate);
        if (!filename.Contains('.'))
        {
            return null; // extensionless import, no lookups performed, since we don't support extensionless files
        }

        var extensionless = Utilities.RemoveFileExtension(candidate);
        if (extensionless == candidate)
        {
            // Once TS native extensions are handled, handle arbitrary extensions for declaration file mapping
            extensionless = JsSlice(candidate, 0, candidate.LastIndexOf('.'));
        }

        var extension = JsSlice(candidate, extensionless.Length);
        if (state.TraceEnabled)
        {
            Trace(state.Host, Diagnostics.File_name_0_has_a_1_extension_stripping_it, candidate, extension);
        }

        return TryAddingExtensions(extensionless, extensions, extension, onlyRecordFailures, state);
    }

    /// <summary>
    /// ported from: src/compiler/moduleNameResolver.ts:2112-2129 — <c>loadFileNameFromPackageJsonField</c>.
    /// </summary>
    /// <remarks>
    /// Only ever called with paths written in package.json files — never module specifiers written in source files — and
    /// so it always allows the candidate to end with a TS extension (but will also try substituting a JS extension for a
    /// TS extension).
    /// </remarks>
    private static PathAndExtension? LoadFileNameFromPackageJsonField(
        Extensions extensions,
        string candidate,
        string? packageJsonValue,
        bool onlyRecordFailures,
        ModuleResolutionState state)
    {
        if (
            (extensions & Extensions.TypeScript) != 0 && Paths.FileExtensionIsOneOf(candidate, Utilities.SupportedTSImplementationExtensions)
            || (extensions & Extensions.Declaration) != 0 && Paths.FileExtensionIsOneOf(candidate, Utilities.SupportedDeclarationExtensions)
        )
        {
            var result = TryFile(candidate, onlyRecordFailures, state);
            var ext = Utilities.TryExtractTSExtension(candidate)!;
            return result is not null
                ? new PathAndExtension
                {
                    Path = candidate,
                    Ext = ext,
                    ResolvedUsingTsExtension = Js.IsTruthy(packageJsonValue) ? !Core.EndsWith(packageJsonValue!, ext) : null,
                }
                : null;
        }

        if (state.IsConfigLookup && extensions == Extensions.Json && Paths.FileExtensionIs(candidate, Extension.Json.Value))
        {
            var result = TryFile(candidate, onlyRecordFailures, state);
            return result is not null
                ? new PathAndExtension { Path = candidate, Ext = Extension.Json.Value, ResolvedUsingTsExtension = null }
                : null;
        }

        return LoadModuleFromFileNoImplicitExtensions(extensions, candidate, onlyRecordFailures, state);
    }

    /// <summary>Try to return an existing file that adds one of the <paramref name="extensions"/> to <paramref name="candidate"/>.</summary>
    private static PathAndExtension? TryAddingExtensions(
        string candidate,
        Extensions extensions,
        string originalExtension,
        bool onlyRecordFailures,
        ModuleResolutionState state)
    {
        if (!onlyRecordFailures)
        {
            // check if the containing folder exists - if it doesn't then just record failures for all supported
            // extensions without disk probing
            var directory = Paths.GetDirectoryPath(candidate);
            if (Js.IsTruthy(directory))
            {
                onlyRecordFailures = !Utilities.DirectoryProbablyExists(directory, state.Host);
            }
        }

        // PORT-NOTE: TS switches on the `Extension` enum members, which in its model are the extension strings
        // themselves; the port's `Extension` is a typed-enum struct, so the arms are spelled as comparisons against its
        // values. The `||` chains are the reference's, including their short-circuiting: an arm whose extension bit is
        // not set does no lookup at all and records no failed-lookup location.
        var isMtsLike = originalExtension == Extension.Mjs.Value
            || originalExtension == Extension.Mts.Value
            || originalExtension == Extension.Dmts.Value;
        if (isMtsLike)
        {
            var resolvedUsingTsExtension = originalExtension == Extension.Mts.Value || originalExtension == Extension.Dmts.Value;
            if ((extensions & Extensions.TypeScript) != 0 && TryExtension(Extension.Mts.Value, resolvedUsingTsExtension) is { } mts) return mts;
            if ((extensions & Extensions.Declaration) != 0 && TryExtension(Extension.Dmts.Value, resolvedUsingTsExtension) is { } dmts) return dmts;
            if ((extensions & Extensions.JavaScript) != 0 && TryExtension(Extension.Mjs.Value) is { } mjs) return mjs;
            return null;
        }

        var isCtsLike = originalExtension == Extension.Cjs.Value
            || originalExtension == Extension.Cts.Value
            || originalExtension == Extension.Dcts.Value;
        if (isCtsLike)
        {
            var resolvedUsingTsExtension = originalExtension == Extension.Cts.Value || originalExtension == Extension.Dcts.Value;
            if ((extensions & Extensions.TypeScript) != 0 && TryExtension(Extension.Cts.Value, resolvedUsingTsExtension) is { } cts) return cts;
            if ((extensions & Extensions.Declaration) != 0 && TryExtension(Extension.Dcts.Value, resolvedUsingTsExtension) is { } dcts) return dcts;
            if ((extensions & Extensions.JavaScript) != 0 && TryExtension(Extension.Cjs.Value) is { } cjs) return cjs;
            return null;
        }

        if (originalExtension == Extension.Json.Value)
        {
            if ((extensions & Extensions.Declaration) != 0 && TryExtension(".d.json.ts") is { } djson) return djson;
            if ((extensions & Extensions.Json) != 0 && TryExtension(Extension.Json.Value) is { } json) return json;
            return null;
        }

        if (originalExtension == Extension.Tsx.Value || originalExtension == Extension.Jsx.Value)
        {
            // basically identical to the ts/js case below, but prefers matching tsx and jsx files exactly before
            // falling back to the ts or js file path
            // (historically, we disallow having both a a.ts and a.tsx file in the same compilation, since their outputs
            // clash)
            // TODO: We should probably error if `"./a.tsx"` resolved to `"./a.ts"`, right?
            var resolvedUsingTsExtension = originalExtension == Extension.Tsx.Value;
            if ((extensions & Extensions.TypeScript) != 0
                && (TryExtension(Extension.Tsx.Value, resolvedUsingTsExtension) ?? TryExtension(Extension.Ts.Value, resolvedUsingTsExtension)) is { } tsx) return tsx;
            if ((extensions & Extensions.Declaration) != 0 && TryExtension(Extension.Dts.Value, resolvedUsingTsExtension) is { } dts) return dts;
            if ((extensions & Extensions.JavaScript) != 0
                && (TryExtension(Extension.Jsx.Value) ?? TryExtension(Extension.Js.Value)) is { } jsx) return jsx;
            return null;
        }

        if (originalExtension == Extension.Ts.Value
            || originalExtension == Extension.Dts.Value
            || originalExtension == Extension.Js.Value
            || originalExtension == "")
        {
            var resolvedUsingTsExtension = originalExtension == Extension.Ts.Value || originalExtension == Extension.Dts.Value;
            if ((extensions & Extensions.TypeScript) != 0
                && (TryExtension(Extension.Ts.Value, resolvedUsingTsExtension) ?? TryExtension(Extension.Tsx.Value, resolvedUsingTsExtension)) is { } ts) return ts;
            if ((extensions & Extensions.Declaration) != 0 && TryExtension(Extension.Dts.Value, resolvedUsingTsExtension) is { } dts) return dts;
            if ((extensions & Extensions.JavaScript) != 0 && (TryExtension(Extension.Js.Value) ?? TryExtension(Extension.Jsx.Value)) is { } js) return js;
            if (state.IsConfigLookup && TryExtension(Extension.Json.Value) is { } configJson) return configJson;
            return null;
        }

        return (extensions & Extensions.Declaration) != 0
                && !Parser.IsDeclarationFileName(candidate + originalExtension)
                && TryExtension($".d{originalExtension}.ts") is { } dExtension
            ? dExtension
            : null;
        PathAndExtension? TryExtension(string ext, bool? resolvedUsingTsExtension = null)
        {
            var path = TryFile(candidate + ext, onlyRecordFailures, state);
            return path is null
                ? null
                : new PathAndExtension
                {
                    Path = path,
                    Ext = ext,
                    ResolvedUsingTsExtension = !state.CandidateIsFromPackageJsonField && resolvedUsingTsExtension == true,
                };
        }
    }

    /// <summary>Return the file if it exists.</summary>
    private static string? TryFile(string fileName, bool onlyRecordFailures, ModuleResolutionState state)
    {
        if (state.CompilerOptions.ModuleSuffixes is not { Count: > 0 } moduleSuffixes)
        {
            return TryFileLookup(fileName, onlyRecordFailures, state);
        }

        var ext = Utilities.TryGetExtensionFromPath(fileName) ?? "";
        var fileNameNoExtension = Js.IsTruthy(ext) ? Utilities.RemoveExtension(fileName, ext) : fileName;
        return Core.ForEach(moduleSuffixes, (suffix, _) => TryFileLookup(fileNameNoExtension + suffix + ext, onlyRecordFailures, state));
    }

    private static string? TryFileLookup(string fileName, bool onlyRecordFailures, ModuleResolutionState state)
    {
        if (!onlyRecordFailures)
        {
            if (state.Host.FileExists(fileName))
            {
                if (state.TraceEnabled)
                {
                    Trace(state.Host, Diagnostics.File_0_exists_use_it_as_a_name_resolution_result, fileName);
                }

                return fileName;
            }

            if (state.TraceEnabled)
            {
                Trace(state.Host, Diagnostics.File_0_does_not_exist, fileName);
            }
        }

        state.FailedLookupLocations?.Add(fileName);
        return null;
    }

    private static Resolved? LoadNodeModuleFromDirectory(
        Extensions extensions,
        string candidate,
        bool onlyRecordFailures,
        ModuleResolutionState state,
        bool considerPackageJson = true)
    {
        var packageInfo = considerPackageJson ? GetPackageJsonInfo(candidate, onlyRecordFailures, state) : null;
        return WithPackageId(packageInfo, LoadNodeModuleFromDirectoryWorker(extensions, candidate, onlyRecordFailures, state, packageInfo), state);
    }

    // ---------------------------------------------------------------------------------------------
    // Entrypoints
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:2228-2278 — <c>getEntrypointsFromPackageJsonInfo</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers <c>string[] | false</c> (the <c>false</c> arm is "no entrypoints"); the port answers
    /// <c>null</c> for that arm, which is how the whole port spells JS's <c>false</c> sentinel in a <c>| undefined</c>
    /// slot (see <c>PackageJsonInfoContents</c>).
    /// </remarks>
    public static List<string>? GetEntrypointsFromPackageJsonInfo(
        PackageJsonInfo packageJsonInfo,
        ICompilerOptions options,
        IGetPackageJsonEntrypointsHost host,
        ModuleResolutionCache? cache,
        bool resolveJs = false)
    {
        if (!resolveJs && packageJsonInfo.Contents.ResolvedEntrypointsComputed)
        {
            // Cached value excludes resolutions to JS files - those could be cached separately, but they're used rarely.
            return packageJsonInfo.Contents.ResolvedEntrypoints;
        }

        IList<string>? entrypoints = null;
        var extensions = Extensions.TypeScript | Extensions.Declaration | (resolveJs ? Extensions.JavaScript : 0);
        var features = GetNodeResolutionFeatures(options);
        var loadPackageJsonMainState = GetTemporaryModuleResolutionState(cache?.GetPackageJsonInfoCache(), host, options);
        loadPackageJsonMainState.Conditions = GetConditions(options);
        loadPackageJsonMainState.RequestContainingDirectory = packageJsonInfo.PackageDirectory;
        var mainResolution = LoadNodeModuleFromDirectoryWorker(
            extensions,
            packageJsonInfo.PackageDirectory,
            onlyRecordFailures: false,
            loadPackageJsonMainState,
            packageJsonInfo);
        entrypoints = Core.Append(entrypoints, mainResolution?.Path);

        if ((features & NodeResolutionFeatures.Exports) != 0 && Js.IsTruthy(packageJsonInfo.Contents.PackageJsonContent.Get("exports")))
        {
            var conditionSets = Core.Deduplicate(
                [GetConditions(options, ModuleKind.ESNext), GetConditions(options, ModuleKind.CommonJS)],
                static (a, b) => Core.ArrayIsEqualTo(a, b));
            foreach (var conditions in conditionSets)
            {
                var loadPackageJsonExportsState = new ModuleResolutionState(loadPackageJsonMainState)
                {
                    FailedLookupLocations = [],
                    Conditions = [.. conditions],
                    Host = host,
                };
                var exportResolutions = LoadEntrypointsFromExportMap(
                    packageJsonInfo,
                    packageJsonInfo.Contents.PackageJsonContent.Get("exports")!,
                    loadPackageJsonExportsState,
                    extensions);
                if (exportResolutions is not null)
                {
                    foreach (var resolution in exportResolutions)
                    {
                        entrypoints = Core.AppendIfUnique(entrypoints, resolution.Path);
                    }
                }
            }
        }

        packageJsonInfo.Contents.ResolvedEntrypointsComputed = true;
        if (entrypoints is null || entrypoints.Count == 0)
        {
            packageJsonInfo.Contents.ResolvedEntrypoints = null;
            return null;
        }

        packageJsonInfo.Contents.ResolvedEntrypoints = [.. entrypoints];
        return packageJsonInfo.Contents.ResolvedEntrypoints;
    }

    private static List<PathAndExtension>? LoadEntrypointsFromExportMap(
        PackageJsonInfo scope,
        object? exports,
        ModuleResolutionState state,
        Extensions extensions)
    {
        // PORT-NOTE: TS's `state: ModuleResolutionState & { host: GetPackageJsonEntrypointsHost }` is a state whose host
        // is the entrypoints host; the port takes that host from the state and names the shape once.
        var entrypointsHost = (IGetPackageJsonEntrypointsHost)state.Host;
        IList<PathAndExtension>? entrypoints = null;
        if (exports is List<object?> exportList)
        {
            foreach (var target in exportList)
            {
                LoadEntrypointsFromTargetExports(target);
            }
        }
        else if (exports is JsObject exportsObject && AllKeysStartWithDot(exportsObject))
        {
            foreach (var key in exportsObject.Keys)
            {
                LoadEntrypointsFromTargetExports(exportsObject.Get(key));
            }
        }
        else
        {
            LoadEntrypointsFromTargetExports(exports);
        }

        return entrypoints is null ? null : [.. entrypoints];

        bool? LoadEntrypointsFromTargetExports(object? target)
        {
            if (target is string targetString && Core.StartsWith(targetString, "./"))
            {
                // PORT-NOTE: TS also tests `state.host.readDirectory`; the generated host interface declares it
                // required, so only the wildcard test remains.
                if (targetString.Contains('*'))
                {
                    if (targetString.IndexOf('*') != targetString.LastIndexOf('*'))
                    {
                        return false;
                    }

                    var entries = entrypointsHost.ReadDirectory(
                        scope.PackageDirectory,
                        ExtensionsToExtensionsArray(extensions),
                        excludes: null,
                        [Paths.ChangeFullExtension(Utilities.ReplaceFirstStar(targetString, "**/*"), ".*")],
                        depth: null);
                    foreach (var entry in entries)
                    {
                        entrypoints = Core.AppendIfUnique(entrypoints, new PathAndExtension
                        {
                            Path = entry,
                            Ext = Paths.GetAnyExtensionFromPath(entry),
                            ResolvedUsingTsExtension = null,
                        });
                    }
                }
                else
                {
                    var partsAfterFirst = Paths.GetPathComponents(targetString).Skip(2).ToList();
                    if (partsAfterFirst.Contains("..") || partsAfterFirst.Contains(".") || partsAfterFirst.Contains("node_modules"))
                    {
                        return false;
                    }

                    var resolvedTarget = Paths.CombinePaths(scope.PackageDirectory, targetString);
                    var finalPath = Paths.GetNormalizedAbsolutePath(resolvedTarget, entrypointsHost.GetCurrentDirectory());
                    var result = LoadFileNameFromPackageJsonField(extensions, finalPath, targetString, onlyRecordFailures: false, state);
                    if (result is not null)
                    {
                        entrypoints = Core.AppendIfUnique(entrypoints, result, static (a, b) => a.Path == b.Path);
                        return true;
                    }
                }
            }
            else if (target is List<object?> targetList)
            {
                foreach (var t in targetList)
                {
                    var success = LoadEntrypointsFromTargetExports(t);
                    if (success == true)
                    {
                        return true;
                    }
                }
            }
            else if (target is JsObject targetObject)
            {
                // PORT-NOTE: TS's `forEach` stops on the first truthy callback result; `Core.Some` is that short-circuit.
                Core.Some(targetObject.Keys, key =>
                {
                    if (key == "default" || state.Conditions.Contains(key) || IsApplicableVersionedTypesKey(state.Conditions, key))
                    {
                        LoadEntrypointsFromTargetExports(targetObject.Get(key));
                        return true;
                    }

                    return false;
                });
            }

            return null;
        }
    }
}
