namespace TypeScript.Compiler;

// ported from: src/compiler/moduleNameResolver.ts:2280-2993
//   2280-2358  loadEntrypointsFromExportMap
//   2360-2377  getTemporaryModuleResolutionState
//   2399-2405  getPackageScopeForPath
//   2407-2443  the package.json info accessors and peer dependencies
//   2445-2487  getPackageJsonInfo
//   2489-2553  loadNodeModuleFromDirectoryWorker
//   2555-2562  extensionIsOk
//   2564-2579  parsePackageName / allKeysStartWithDot / noKeyStartsWithDot
//   2581-2692  loadModuleFromSelfNameReference / loadModuleFromExports / loadModuleFromImports
//   2694-2750  comparePatternKeys / loadModuleFromExportsOrImports / hasOneAsterisk
//   2751-2987  getLoadModuleFromTargetExportOrImport
//   2989-2995  isApplicableVersionedTypesKey
// ported from: src/compiler/moduleNameResolver.ts:339-456 (the package.json field readers, from the top of the file)
/// <summary>
/// package.json: the cache of parsed package info, the field readers, and the <c>exports</c>/<c>imports</c> lookup
/// machinery.
/// </summary>
internal static partial class ModuleNameResolver
{
    // ---------------------------------------------------------------------------------------------
    // package.json readers
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:361-377 — <c>readPackageJsonField</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's two generic overloads select the field's declared type at compile time; the port has one method
    /// with the <c>typeOfTag</c> runtime tag plus the two casts below, which is the same read.
    /// <para>
    /// PORT-NOTE: JS's <c>typeof</c> reports an array as "object", so an array also passes the "object" arm and is then
    /// walked with <c>for…in</c>. The port's object arm is <see cref="JsObject"/> only: an array in
    /// <c>typesVersions</c>/<c>peerDependencies</c> is malformed package.json, and its numeric keys are not a shape this
    /// slice resolves against.
    /// </para>
    /// </remarks>
    private static object? ReadPackageJsonField(JsObject jsonContent, string fieldName, string typeOfTag, ModuleResolutionState state)
    {
        if (!jsonContent.Has(fieldName))
        {
            if (state.TraceEnabled)
            {
                Trace(state.Host, Diagnostics.package_json_does_not_have_a_0_field, fieldName);
            }

            return null;
        }

        var value = jsonContent.Get(fieldName);
        var matches = typeOfTag == "string" ? value is string : value is JsObject;
        if (!matches)
        {
            if (state.TraceEnabled)
            {
                Trace(
                    state.Host,
                    Diagnostics.Expected_type_of_0_field_in_package_json_to_be_1_got_2,
                    fieldName,
                    typeOfTag,
                    value is null ? "null" : Utilities.JsTypeOf(value));
            }

            return null;
        }

        return value;
    }

    private static string? ReadPackageJsonStringField(JsObject jsonContent, string fieldName, ModuleResolutionState state)
        => ReadPackageJsonField(jsonContent, fieldName, "string", state) as string;

    private static JsObject? ReadPackageJsonObjectField(JsObject jsonContent, string fieldName, ModuleResolutionState state)
        => ReadPackageJsonField(jsonContent, fieldName, "object", state) as JsObject;

    private static string? ReadPackageJsonPathField(JsObject jsonContent, string fieldName, string baseDirectory, ModuleResolutionState state)
    {
        var fileName = ReadPackageJsonStringField(jsonContent, fieldName, state);
        if (fileName is null)
        {
            return null;
        }

        if (fileName.Length == 0)
        {
            if (state.TraceEnabled)
            {
                Trace(state.Host, Diagnostics.package_json_had_a_falsy_0_field, fieldName);
            }

            return null;
        }

        var path = Paths.NormalizePath(Paths.CombinePaths(baseDirectory, fileName));
        if (state.TraceEnabled)
        {
            Trace(state.Host, Diagnostics.package_json_has_0_field_1_that_references_2, fieldName, fileName, path);
        }

        return path;
    }

    private static string? ReadPackageJsonTypesFields(JsObject jsonContent, string baseDirectory, ModuleResolutionState state)
        => ReadPackageJsonPathField(jsonContent, "typings", baseDirectory, state)
            ?? ReadPackageJsonPathField(jsonContent, "types", baseDirectory, state);

    private static string? ReadPackageJsonTSConfigField(JsObject jsonContent, string baseDirectory, ModuleResolutionState state)
        => ReadPackageJsonPathField(jsonContent, "tsconfig", baseDirectory, state);

    private static string? ReadPackageJsonMainField(JsObject jsonContent, string baseDirectory, ModuleResolutionState state)
        => ReadPackageJsonPathField(jsonContent, "main", baseDirectory, state);

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:410-420 — <c>readPackageJsonTypesVersionsField</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS hands back the raw JSON object and indexes it as <c>MapLike&lt;MapLike&lt;string[]&gt;&gt;</c>. The
    /// port materializes that shape once, here, so <c>GetPackageJsonTypesVersionsPaths</c> (which M8's command line
    /// parser also calls) keeps TS's signature; a member whose value is not an array of strings contributes the empty
    /// substitution list, which is what a missing <c>forEach</c> over it does on the reference side.
    /// </remarks>
    private static Dictionary<string, Dictionary<string, List<string>>>? ReadPackageJsonTypesVersionsField(
        JsObject jsonContent,
        ModuleResolutionState state)
    {
        var typesVersions = ReadPackageJsonObjectField(jsonContent, "typesVersions", state);
        if (typesVersions is null) return null;

        if (state.TraceEnabled)
        {
            Trace(state.Host, Diagnostics.package_json_has_a_typesVersions_field_with_version_specific_path_mappings);
        }

        var result = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);
        foreach (var key in typesVersions.Keys)
        {
            var paths = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            if (typesVersions.Get(key) is JsObject pathTable)
            {
                foreach (var pattern in pathTable.Keys)
                {
                    paths[pattern] = pathTable.Get(pattern) is List<object?> substitutions
                        ? [.. substitutions.OfType<string>()]
                        : [];
                }
            }

            result[key] = paths;
        }

        return result;
    }

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:427-456 — <c>readPackageJsonTypesVersionPaths</c>.</summary>
    private static VersionPaths? ReadPackageJsonTypesVersionPaths(JsObject jsonContent, ModuleResolutionState state)
    {
        var typesVersions = ReadPackageJsonTypesVersionsField(jsonContent, state);
        if (typesVersions is null) return null;

        if (state.TraceEnabled)
        {
            foreach (var key in typesVersions.Keys)
            {
                if (VersionRange.TryParse(key) is null)
                {
                    Trace(state.Host, Diagnostics.package_json_has_a_typesVersions_entry_0_that_is_not_a_valid_semver_range, key);
                }
            }
        }

        var result = GetPackageJsonTypesVersionsPaths(typesVersions);
        if (result is null)
        {
            if (state.TraceEnabled)
            {
                Trace(state.Host, Diagnostics.package_json_does_not_have_a_typesVersions_entry_that_matches_version_0, VersionInfo.VersionMajorMinor);
            }

            return null;
        }

        return result;
    }

    // ---------------------------------------------------------------------------------------------
    // package.json info
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:2360-2377 — <c>getTemporaryModuleResolutionState</c>.</summary>
    public static ModuleResolutionState GetTemporaryModuleResolutionState(
        IPackageJsonInfoCache? packageJsonInfoCache,
        IModuleResolutionHost host,
        ICompilerOptions options)
        => new()
        {
            Host = host,
            CompilerOptions = options,
            TraceEnabled = IsTraceEnabled(options, host),
            FailedLookupLocations = null,
            AffectingLocations = null,
            PackageJsonInfoCache = packageJsonInfoCache,
            Features = NodeResolutionFeatures.None,
            Conditions = [],
            RequestContainingDirectory = null,
            ReportDiagnostic = static _ => { },
            IsConfigLookup = false,
            CandidateIsFromPackageJsonField = false,
            ResolvedPackageDirectory = false,
        };

    /// <summary>
    /// ported from: src/compiler/moduleNameResolver.ts:2399-2405 — <c>getPackageScopeForPath</c>.
    /// </summary>
    /// <remarks>Locates the package.json scope for a given path.</remarks>
    public static PackageJsonInfo? GetPackageScopeForPath(string directory, ModuleResolutionState state)
        => ForEachAncestorDirectoryStoppingAtGlobalCache(
            state.Host,
            directory,
            dir => GetPackageJsonInfo(dir, onlyRecordFailures: false, state));

    private static VersionPaths? GetVersionPathsOfPackageJsonInfo(PackageJsonInfo packageJsonInfo, ModuleResolutionState state)
    {
        if (!packageJsonInfo.Contents.VersionPathsComputed)
        {
            packageJsonInfo.Contents.VersionPaths = ReadPackageJsonTypesVersionPaths(packageJsonInfo.Contents.PackageJsonContent, state);
            packageJsonInfo.Contents.VersionPathsComputed = true;
        }

        return packageJsonInfo.Contents.VersionPaths;
    }

    private static string? GetPeerDependenciesOfPackageJsonInfo(PackageJsonInfo packageJsonInfo, ModuleResolutionState state)
    {
        if (!packageJsonInfo.Contents.PeerDependenciesComputed)
        {
            packageJsonInfo.Contents.PeerDependencies = ReadPackageJsonPeerDependencies(packageJsonInfo, state);
            packageJsonInfo.Contents.PeerDependenciesComputed = true;
        }

        return packageJsonInfo.Contents.PeerDependencies;
    }

    private static string? ReadPackageJsonPeerDependencies(PackageJsonInfo packageJsonInfo, ModuleResolutionState state)
    {
        var peerDependencies = ReadPackageJsonObjectField(packageJsonInfo.Contents.PackageJsonContent, "peerDependencies", state);
        if (peerDependencies is null) return null;
        if (state.TraceEnabled) Trace(state.Host, Diagnostics.package_json_has_a_peerDependencies_field);
        var packageDirectory = RealPath(packageJsonInfo.PackageDirectory, state.Host, state.TraceEnabled);
        const string nodeModulesName = "node_modules";
        var nodeModules = JsSlice(
            packageDirectory,
            0,
            packageDirectory.LastIndexOf(nodeModulesName, StringComparison.Ordinal) + nodeModulesName.Length) + Paths.DirectorySeparator;
        var result = "";
        foreach (var key in peerDependencies.Keys)
        {
            var peerPackageJson = GetPackageJsonInfo(nodeModules + key, onlyRecordFailures: false, state);
            if (peerPackageJson is not null)
            {
                if (peerPackageJson.Contents.PackageJsonContent.Get("version") is string version)
                {
                    result += $"+{key}@{version}";
                    if (state.TraceEnabled) Trace(state.Host, Diagnostics.Found_peerDependency_0_with_1_version, key, version);
                }
            }
            else if (state.TraceEnabled)
            {
                // Read the dependency version
                Trace(state.Host, Diagnostics.Failed_to_find_peerDependency_0, key);
            }
        }

        return result.Length == 0 ? null : result;
    }

    private static PackageJsonInfo? GetPackageJsonInfo(string packageDirectory, bool onlyRecordFailures, ModuleResolutionState state)
    {
        var host = state.Host;
        var traceEnabled = state.TraceEnabled;
        var packageJsonPath = Paths.CombinePaths(packageDirectory, "package.json");
        if (onlyRecordFailures)
        {
            state.FailedLookupLocations?.Add(packageJsonPath);
            return null;
        }

        var existing = state.PackageJsonInfoCache?.GetPackageJsonInfo(packageJsonPath);
        if (existing is not null)
        {
            if (existing is PackageJsonInfo existingInfo)
            {
                if (traceEnabled) Trace(host, Diagnostics.File_0_exists_according_to_earlier_cached_lookups, packageJsonPath);
                state.AffectingLocations?.Add(packageJsonPath);
                return existingInfo.PackageDirectory == packageDirectory
                    ? existingInfo
                    : new PackageJsonInfo { PackageDirectory = packageDirectory, Contents = existingInfo.Contents };
            }

            if (existing is MissingPackageJsonInfo missing && missing.DirectoryExists && traceEnabled)
            {
                Trace(host, Diagnostics.File_0_does_not_exist_according_to_earlier_cached_lookups, packageJsonPath);
            }

            state.FailedLookupLocations?.Add(packageJsonPath);
            return null;
        }

        var directoryExists = Utilities.DirectoryProbablyExists(packageDirectory, host);
        if (directoryExists && host.FileExists(packageJsonPath))
        {
            var packageJsonContent = Utilities.ReadJson(packageJsonPath, host);
            if (traceEnabled)
            {
                Trace(host, Diagnostics.Found_package_json_at_0, packageJsonPath);
            }

            var result = new PackageJsonInfo
            {
                PackageDirectory = packageDirectory,
                Contents = new PackageJsonInfoContents { PackageJsonContent = packageJsonContent },
            };
            if (state.PackageJsonInfoCache is { IsReadonly: false })
            {
                state.PackageJsonInfoCache.SetPackageJsonInfo(packageJsonPath, result);
            }

            state.AffectingLocations?.Add(packageJsonPath);
            return result;
        }

        if (directoryExists && traceEnabled)
        {
            Trace(host, Diagnostics.File_0_does_not_exist, packageJsonPath);
        }

        if (state.PackageJsonInfoCache is { IsReadonly: false })
        {
            state.PackageJsonInfoCache.SetPackageJsonInfo(packageJsonPath, new MissingPackageJsonInfo
            {
                PackageDirectory = packageDirectory,
                DirectoryExists = directoryExists,
            });
        }

        // record package json as one of failed lookup locations - in the future if this file will appear it will
        // invalidate resolution results
        state.FailedLookupLocations?.Add(packageJsonPath);
        return null;
    }

    private static PathAndExtension? LoadNodeModuleFromDirectoryWorker(
        Extensions extensions,
        string candidate,
        bool onlyRecordFailures,
        ModuleResolutionState state,
        PackageJsonInfo? packageJson)
    {
        var versionPaths = packageJson is not null ? GetVersionPathsOfPackageJsonInfo(packageJson, state) : null;
        string? packageFile = null;
        if (packageJson is not null && ArePathsEqual(packageJson.PackageDirectory, candidate, state.Host))
        {
            if (state.IsConfigLookup)
            {
                packageFile = ReadPackageJsonTSConfigField(packageJson.Contents.PackageJsonContent, packageJson.PackageDirectory, state);
            }
            else
            {
                packageFile = (extensions & Extensions.Declaration) != 0
                        && ReadPackageJsonTypesFields(packageJson.Contents.PackageJsonContent, packageJson.PackageDirectory, state) is { } typesField
                    ? typesField
                    : (extensions & (Extensions.ImplementationFiles | Extensions.Declaration)) != 0
                        && ReadPackageJsonMainField(packageJson.Contents.PackageJsonContent, packageJson.PackageDirectory, state) is { } mainField
                        ? mainField
                        : null;
            }
        }

        ResolutionKindSpecificLoader loader = (loaderExtensions, loaderCandidate, loaderOnlyRecordFailures, loaderState, _) =>
        {
            var fromFile = LoadFileNameFromPackageJsonField(loaderExtensions, loaderCandidate, packageJsonValue: null, loaderOnlyRecordFailures, loaderState);
            if (fromFile is not null)
            {
                return NoPackageId(fromFile);
            }

            // Even if extensions is DtsOnly, we can still look up a .ts file as a result of package.json "types"
            var expandedExtensions = loaderExtensions == Extensions.Declaration
                ? Extensions.TypeScript | Extensions.Declaration
                : loaderExtensions;
            // Don't do package.json lookup recursively, because Node.js' package lookup doesn't.

            // Disable `EsmMode` for the resolution of the package path for cjs-mode packages (so the `main` field can omit
            // extensions) (technically it only emits a deprecation warning in esm packages right now, but that's probably
            // enough to mean we don't need to support it)
            var features = loaderState.Features;
            var candidateIsFromPackageJsonField = loaderState.CandidateIsFromPackageJsonField;
            loaderState.CandidateIsFromPackageJsonField = true;
            if (packageJson?.Contents.PackageJsonContent.Get("type") as string != "module")
            {
                loaderState.Features &= ~NodeResolutionFeatures.EsmMode;
            }

            var result = NodeLoadModuleByRelativeName(expandedExtensions, loaderCandidate, loaderOnlyRecordFailures, loaderState, considerPackageJson: false);
            loaderState.Features = features;
            loaderState.CandidateIsFromPackageJsonField = candidateIsFromPackageJsonField;
            return result;
        };

        bool? onlyRecordFailuresForPackageFile = packageFile is not null
            ? !Utilities.DirectoryProbablyExists(Paths.GetDirectoryPath(packageFile), state.Host)
            : null;
        var onlyRecordFailuresForIndex = onlyRecordFailures || !Utilities.DirectoryProbablyExists(candidate, state.Host);
        var indexPath = Paths.CombinePaths(candidate, state.IsConfigLookup ? "tsconfig" : "index");

        if (versionPaths is not null && (packageFile is null || Paths.ContainsPath(candidate, packageFile)))
        {
            var moduleName = Paths.GetRelativePathFromDirectory(candidate, packageFile ?? indexPath, ignoreCase: false);
            if (state.TraceEnabled)
            {
                Trace(
                    state.Host,
                    Diagnostics.package_json_has_a_typesVersions_entry_0_that_matches_compiler_version_1_looking_for_a_pattern_to_match_module_name_2,
                    versionPaths.Version,
                    VersionInfo.Version,
                    moduleName);
            }

            var pathPatterns = Utilities.TryParsePatterns(versionPaths.Paths);
            var result = TryLoadModuleUsingPaths(extensions, moduleName, candidate, versionPaths.Paths, pathPatterns, loader, onlyRecordFailuresForPackageFile == true || onlyRecordFailuresForIndex, state);
            if (result is not null)
            {
                return RemoveIgnoredPackageId(result.Value);
            }
        }

        // It won't have a `packageId` set, because we disabled `considerPackageJson`.
        var packageFileResult = packageFile is not null
            ? RemoveIgnoredPackageId(loader(extensions, packageFile, onlyRecordFailuresForPackageFile!.Value, state, null))
            : null;
        if (packageFileResult is not null) return packageFileResult;

        // esm mode resolutions don't do package `index` lookups
        if ((state.Features & NodeResolutionFeatures.EsmMode) == 0)
        {
            return LoadModuleFromFile(extensions, indexPath, onlyRecordFailuresForIndex, state);
        }

        return null;
    }

    /// <summary>True if <paramref name="extension"/> is one of the supported <paramref name="extensions"/>.</summary>
    private static bool ExtensionIsOk(Extensions extensions, string extension)
        => (extensions & Extensions.JavaScript) != 0 && extension is ".js" or ".jsx" or ".mjs" or ".cjs"
            || (extensions & Extensions.TypeScript) != 0 && extension is ".ts" or ".tsx" or ".mts" or ".cts"
            || (extensions & Extensions.Declaration) != 0 && extension is ".d.ts" or ".d.mts" or ".d.cts"
            || (extensions & Extensions.Json) != 0 && extension == ".json";

    // ---------------------------------------------------------------------------------------------
    // Package names
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:2564-2571 — <c>parsePackageName</c>.</summary>
    public static (string PackageName, string Remainder) ParsePackageName(string moduleName)
    {
        var idx = moduleName.IndexOf('/');
        if (moduleName.Length != 0 && moduleName[0] == '@')
        {
            idx = moduleName.IndexOf('/', idx + 1);
        }

        return idx == -1
            ? (moduleName, "")
            : (JsSlice(moduleName, 0, idx), JsSlice(moduleName, idx + 1));
    }

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:2573-2575 — <c>allKeysStartWithDot</c>.</summary>
    public static bool AllKeysStartWithDot(JsObject obj)
        => Core.Every(obj.Keys, static (k, _) => Core.StartsWith(k, "."));

    private static bool NoKeyStartsWithDot(JsObject obj)
        => !Core.Some(obj.Keys, static k => Core.StartsWith(k, "."));

    // ---------------------------------------------------------------------------------------------
    // exports / imports
    // ---------------------------------------------------------------------------------------------

    private static SearchResult<Resolved>? LoadModuleFromSelfNameReference(
        Extensions extensions,
        string moduleName,
        string directory,
        ModuleResolutionState state,
        ModuleResolutionCache? cache,
        IResolvedProjectReference? redirectedReference)
    {
        var directoryPath = Paths.GetNormalizedAbsolutePath(directory, state.Host.GetCurrentDirectory());
        var scope = GetPackageScopeForPath(directoryPath, state);
        if (scope is null || !Js.IsTruthy(scope.Contents.PackageJsonContent.Get("exports")))
        {
            return null;
        }

        if (scope.Contents.PackageJsonContent.Get("name") is not string packageName)
        {
            return null;
        }

        // unrooted paths should have `""` as their 0th entry
        var parts = Paths.GetPathComponents(moduleName);
        var nameParts = Paths.GetPathComponents(packageName);
        if (!Core.Every(nameParts, (p, i) => i < parts.Count && parts[i] == p))
        {
            return null;
        }

        var trailingParts = JsSliceList(parts, nameParts.Count);
        var subpath = trailingParts.Count == 0
            ? "."
            : $".{Paths.DirectorySeparator}{string.Join(Paths.DirectorySeparator, trailingParts)}";
        // Maybe TODO: splitting extensions into two priorities should be unnecessary, except
        // https://github.com/microsoft/TypeScript/issues/50762 makes the behavior different.
        // As long as that bug exists, we need to do two passes here in self-name loading
        // in order to be consistent with (non-self) library-name loading in
        // `loadModuleFromNearestNodeModulesDirectoryWorker`, which uses two passes in order
        // to prioritize `@types` packages higher up the directory tree over untyped
        // implementation packages. See the selfNameModuleAugmentation.ts test for why this matters.
        //
        // However, there's an exception. If the user has `allowJs` and `declaration`, we need to ensure that self-name
        // imports of their own package can resolve back to their input JS files via `tryLoadInputFileForPath` at a higher
        // priority than their output declaration files, so we need to do a single pass with all extensions for that case.
        if (Utilities.GetAllowJsCompilerOption(state.CompilerOptions) && !PathContainsNodeModules(directory))
        {
            return LoadModuleFromExports(scope, extensions, subpath, state, cache, redirectedReference);
        }

        var priorityExtensions = extensions & (Extensions.TypeScript | Extensions.Declaration);
        var secondaryExtensions = extensions & ~(Extensions.TypeScript | Extensions.Declaration);
        return LoadModuleFromExports(scope, priorityExtensions, subpath, state, cache, redirectedReference)
            ?? LoadModuleFromExports(scope, secondaryExtensions, subpath, state, cache, redirectedReference);
    }

    private static SearchResult<Resolved>? LoadModuleFromExports(
        PackageJsonInfo scope,
        Extensions extensions,
        string subpath,
        ModuleResolutionState state,
        ModuleResolutionCache? cache,
        IResolvedProjectReference? redirectedReference)
    {
        var exports = scope.Contents.PackageJsonContent.Get("exports");
        if (!Js.IsTruthy(exports))
        {
            return null;
        }

        if (subpath == ".")
        {
            object? mainExport = null;
            if (exports is string || exports is List<object?> || (exports is JsObject exportsAsTable && NoKeyStartsWithDot(exportsAsTable)))
            {
                mainExport = exports;
            }
            else if (exports is JsObject exportsWithDot && exportsWithDot.Has("."))
            {
                mainExport = exportsWithDot.Get(".");
            }

            if (Js.IsTruthy(mainExport))
            {
                var loadModuleFromTargetExportOrImport = GetLoadModuleFromTargetExportOrImport(extensions, state, cache, redirectedReference, subpath, scope, isImports: false);
                return loadModuleFromTargetExportOrImport(mainExport, "", false, ".");
            }
        }
        else if (exports is JsObject exportsObject)
        {
            if (!AllKeysStartWithDot(exportsObject))
            {
                if (state.TraceEnabled)
                {
                    Trace(state.Host, Diagnostics.Export_specifier_0_does_not_exist_in_package_json_scope_at_path_1, subpath, scope.PackageDirectory);
                }

                return ToSearchResult<Resolved>(null);
            }

            var result = LoadModuleFromExportsOrImports(extensions, state, cache, redirectedReference, subpath, exportsObject, scope, isImports: false);
            if (result is not null)
            {
                return result;
            }
        }

        if (state.TraceEnabled)
        {
            Trace(state.Host, Diagnostics.Export_specifier_0_does_not_exist_in_package_json_scope_at_path_1, subpath, scope.PackageDirectory);
        }

        return ToSearchResult<Resolved>(null);
    }

    private static SearchResult<Resolved>? LoadModuleFromImports(
        Extensions extensions,
        string moduleName,
        string directory,
        ModuleResolutionState state,
        ModuleResolutionCache? cache,
        IResolvedProjectReference? redirectedReference)
    {
        if (moduleName == "#" || (Core.StartsWith(moduleName, "#/") && (state.Features & NodeResolutionFeatures.ImportsPatternRoot) == 0))
        {
            if (state.TraceEnabled)
            {
                Trace(state.Host, Diagnostics.Invalid_import_specifier_0_has_no_possible_resolutions, moduleName);
            }

            return ToSearchResult<Resolved>(null);
        }

        var directoryPath = Paths.GetNormalizedAbsolutePath(directory, state.Host.GetCurrentDirectory());
        var scope = GetPackageScopeForPath(directoryPath, state);
        if (scope is null)
        {
            if (state.TraceEnabled)
            {
                Trace(state.Host, Diagnostics.Directory_0_has_no_containing_package_json_scope_Imports_will_not_resolve, directoryPath);
            }

            return ToSearchResult<Resolved>(null);
        }

        var imports = scope.Contents.PackageJsonContent.Get("imports");
        if (imports is not JsObject importsTable)
        {
            if (state.TraceEnabled)
            {
                Trace(state.Host, Diagnostics.package_json_scope_0_has_no_imports_defined, scope.PackageDirectory);
            }

            return ToSearchResult<Resolved>(null);
        }

        var result = LoadModuleFromExportsOrImports(extensions, state, cache, redirectedReference, moduleName, importsTable, scope, isImports: true);
        if (result is not null)
        {
            return result;
        }

        if (state.TraceEnabled)
        {
            Trace(state.Host, Diagnostics.Import_specifier_0_does_not_exist_in_package_json_scope_at_path_1, moduleName, scope.PackageDirectory);
        }

        return ToSearchResult<Resolved>(null);
    }

    /// <summary>
    /// ported from: src/compiler/moduleNameResolver.ts:2694-2706 — <c>comparePatternKeys</c>.
    /// </summary>
    /// <remarks>
    /// From the Node resolver: "longest" has some nuance as to what "longest" means in the presence of pattern trailers.
    /// </remarks>
    public static Comparison ComparePatternKeys(string a, string b)
    {
        var aPatternIndex = a.IndexOf('*');
        var bPatternIndex = b.IndexOf('*');
        var baseLenA = aPatternIndex == -1 ? a.Length : aPatternIndex + 1;
        var baseLenB = bPatternIndex == -1 ? b.Length : bPatternIndex + 1;
        if (baseLenA > baseLenB) return Comparison.LessThan;
        if (baseLenB > baseLenA) return Comparison.GreaterThan;
        if (aPatternIndex == -1) return Comparison.GreaterThan;
        if (bPatternIndex == -1) return Comparison.LessThan;
        if (a.Length > b.Length) return Comparison.LessThan;
        if (b.Length > a.Length) return Comparison.GreaterThan;
        return Comparison.EqualTo;
    }

    private static SearchResult<Resolved>? LoadModuleFromExportsOrImports(
        Extensions extensions,
        ModuleResolutionState state,
        ModuleResolutionCache? cache,
        IResolvedProjectReference? redirectedReference,
        string moduleName,
        JsObject lookupTable,
        PackageJsonInfo scope,
        bool isImports)
    {
        var loadModuleFromTargetExportOrImport = GetLoadModuleFromTargetExportOrImport(extensions, state, cache, redirectedReference, moduleName, scope, isImports);

        if (!Core.EndsWith(moduleName, Paths.DirectorySeparator) && !moduleName.Contains('*') && lookupTable.Has(moduleName))
        {
            var target = lookupTable.Get(moduleName);
            return loadModuleFromTargetExportOrImport(target, "", false, moduleName);
        }

        var expandingKeys = Core.ToSorted(
            Core.Filter(lookupTable.Keys, static k => HasOneAsterisk(k) || Core.EndsWith(k, Paths.DirectorySeparator))!,
            ComparePatternKeys);
        foreach (var potentialTarget in expandingKeys)
        {
            if ((state.Features & NodeResolutionFeatures.ExportsPatternTrailers) != 0 && MatchesPatternWithTrailer(potentialTarget, moduleName))
            {
                var target = lookupTable.Get(potentialTarget);
                var starPos = potentialTarget.IndexOf('*');
                var subpath = JsSlice(
                    moduleName,
                    JsSlice(potentialTarget, 0, starPos).Length,
                    moduleName.Length - (potentialTarget.Length - 1 - starPos));
                return loadModuleFromTargetExportOrImport(target, subpath, true, potentialTarget);
            }

            if (Core.EndsWith(potentialTarget, "*") && Core.StartsWith(moduleName, JsSlice(potentialTarget, 0, potentialTarget.Length - 1)))
            {
                var target = lookupTable.Get(potentialTarget);
                var subpath = JsSlice(moduleName, potentialTarget.Length - 1);
                return loadModuleFromTargetExportOrImport(target, subpath, true, potentialTarget);
            }

            if (Core.StartsWith(moduleName, potentialTarget))
            {
                var target = lookupTable.Get(potentialTarget);
                var subpath = JsSlice(moduleName, potentialTarget.Length);
                return loadModuleFromTargetExportOrImport(target, subpath, false, potentialTarget);
            }
        }

        return null;

        static bool MatchesPatternWithTrailer(string target, string name)
        {
            if (Core.EndsWith(target, "*")) return false; // handled by next case in loop
            var starPos = target.IndexOf('*');
            if (starPos == -1) return false; // handled by last case in loop
            return Core.StartsWith(name, target[..starPos]) && Core.EndsWith(name, target[(starPos + 1)..]);
        }
    }

    private static bool HasOneAsterisk(string patternKey)
    {
        var firstStar = patternKey.IndexOf('*');
        return firstStar != -1 && firstStar == patternKey.LastIndexOf('*');
    }

    /// <summary>
    /// Gets the self-recursive function specialized to retrieving the targeted import/export element for the given
    /// resolution configuration.
    /// </summary>
    private static Func<object?, string, bool, string, SearchResult<Resolved>?> GetLoadModuleFromTargetExportOrImport(
        Extensions extensions,
        ModuleResolutionState state,
        ModuleResolutionCache? cache,
        IResolvedProjectReference? redirectedReference,
        string moduleName,
        PackageJsonInfo scope,
        bool isImports)
    {
        return LoadModuleFromTargetExportOrImport;

        SearchResult<Resolved>? LoadModuleFromTargetExportOrImport(object? target, string subpath, bool pattern, string key)
        {
            if (target is string targetString)
            {
                if (!pattern && subpath.Length > 0 && !Core.EndsWith(targetString, "/"))
                {
                    if (state.TraceEnabled)
                    {
                        Trace(state.Host, Diagnostics.package_json_scope_0_has_invalid_type_for_target_of_specifier_1, scope.PackageDirectory, moduleName);
                    }

                    return ToSearchResult<Resolved>(null);
                }

                if (!Core.StartsWith(targetString, "./"))
                {
                    if (isImports && !Core.StartsWith(targetString, "../") && !Core.StartsWith(targetString, "/") && !Paths.IsRootedDiskPath(targetString))
                    {
                        var combinedLookup = pattern ? targetString.Replace("*", subpath) : targetString + subpath;
                        TraceIfEnabled(state, Diagnostics.Using_0_subpath_1_with_target_2, "imports", key, combinedLookup);
                        TraceIfEnabled(state, Diagnostics.Resolving_module_0_from_1, combinedLookup, scope.PackageDirectory + "/");
                        var result = NodeModuleNameResolverWorker(
                            state.Features,
                            combinedLookup,
                            scope.PackageDirectory + "/",
                            state.CompilerOptions,
                            state.Host,
                            cache,
                            extensions,
                            isConfigLookup: false,
                            redirectedReference,
                            state.Conditions);
                        // Note: we cannot safely reassign `state.failedLookupLocations` during a request;
                        // `nodeModuleNameResolverWorker` relies on the `state` property remaining reference-equal to the
                        // one it initializes.
                        state.FailedLookupLocations?.AddRange(result.FailedLookupLocations ?? []);
                        state.AffectingLocations?.AddRange(result.AffectingLocations ?? []);
                        return ToSearchResult<Resolved>(
                            result.ResolvedModule is not null
                                ? new Resolved
                                {
                                    Path = result.ResolvedModule.ResolvedFileName!,
                                    Extension = result.ResolvedModule.Extension!,
                                    PackageId = result.ResolvedModule.PackageId,
                                    OriginalPath = result.ResolvedModule.OriginalPath,
                                    ResolvedUsingTsExtension = result.ResolvedModule.ResolvedUsingTsExtension,
                                }
                                : null);
                    }

                    if (state.TraceEnabled)
                    {
                        Trace(state.Host, Diagnostics.package_json_scope_0_has_invalid_type_for_target_of_specifier_1, scope.PackageDirectory, moduleName);
                    }

                    return ToSearchResult<Resolved>(null);
                }

                var parts = Paths.PathIsRelative(targetString)
                    ? JsSliceList(Paths.GetPathComponents(targetString), 1)
                    : Paths.GetPathComponents(targetString);
                var partsAfterFirst = JsSliceList(parts, 1);
                if (partsAfterFirst.Contains("..") || partsAfterFirst.Contains(".") || partsAfterFirst.Contains("node_modules"))
                {
                    if (state.TraceEnabled)
                    {
                        Trace(state.Host, Diagnostics.package_json_scope_0_has_invalid_type_for_target_of_specifier_1, scope.PackageDirectory, moduleName);
                    }

                    return ToSearchResult<Resolved>(null);
                }

                var resolvedTarget = Paths.CombinePaths(scope.PackageDirectory, targetString);
                // TODO: Assert that `resolvedTarget` is actually within the package directory? That's what the spec
                // says.... but I'm not sure we need to be in the business of validating everyone's import and export map
                // correctness.
                var subpathParts = Paths.GetPathComponents(subpath);
                if (subpathParts.Contains("..") || subpathParts.Contains(".") || subpathParts.Contains("node_modules"))
                {
                    if (state.TraceEnabled)
                    {
                        Trace(state.Host, Diagnostics.package_json_scope_0_has_invalid_type_for_target_of_specifier_1, scope.PackageDirectory, moduleName);
                    }

                    return ToSearchResult<Resolved>(null);
                }

                if (state.TraceEnabled)
                {
                    Trace(
                        state.Host,
                        Diagnostics.Using_0_subpath_1_with_target_2,
                        isImports ? "imports" : "exports",
                        key,
                        pattern ? targetString.Replace("*", subpath) : targetString + subpath);
                }

                var finalPath = ToAbsolutePath(pattern ? resolvedTarget.Replace("*", subpath) : resolvedTarget + subpath)!;
                var inputLink = TryLoadInputFileForPath(finalPath, subpath, Paths.CombinePaths(scope.PackageDirectory, "package.json"), isImports);
                if (inputLink is not null) return inputLink;
                return ToSearchResult(WithPackageId(scope, LoadFileNameFromPackageJsonField(extensions, finalPath, targetString, onlyRecordFailures: false, state), state));
            }

            if (target is JsObject targetObject)
            {
                TraceIfEnabled(state, Diagnostics.Entering_conditional_exports);
                foreach (var condition in targetObject.Keys)
                {
                    if (condition == "default" || state.Conditions.Contains(condition) || IsApplicableVersionedTypesKey(state.Conditions, condition))
                    {
                        TraceIfEnabled(state, Diagnostics.Matched_0_condition_1, isImports ? "imports" : "exports", condition);
                        var subTarget = targetObject.Get(condition);
                        var result = LoadModuleFromTargetExportOrImport(subTarget, subpath, pattern, key);
                        if (result is not null)
                        {
                            if (result.Value is not null)
                            {
                                TraceIfEnabled(state, Diagnostics.Resolved_under_condition_0, condition);
                            }

                            TraceIfEnabled(state, Diagnostics.Exiting_conditional_exports);
                            return result;
                        }

                        TraceIfEnabled(state, Diagnostics.Failed_to_resolve_under_condition_0, condition);
                    }
                    else
                    {
                        TraceIfEnabled(state, Diagnostics.Saw_non_matching_condition_0, condition);
                    }
                }

                TraceIfEnabled(state, Diagnostics.Exiting_conditional_exports);
                return null;
            }

            if (target is List<object?> targetList)
            {
                if (targetList.Count == 0)
                {
                    if (state.TraceEnabled)
                    {
                        Trace(state.Host, Diagnostics.package_json_scope_0_has_invalid_type_for_target_of_specifier_1, scope.PackageDirectory, moduleName);
                    }

                    return ToSearchResult<Resolved>(null);
                }

                foreach (var elem in targetList)
                {
                    var result = LoadModuleFromTargetExportOrImport(elem, subpath, pattern, key);
                    if (result is not null)
                    {
                        return result;
                    }
                }
            }
            else if (target is null)
            {
                if (state.TraceEnabled)
                {
                    Trace(state.Host, Diagnostics.package_json_scope_0_explicitly_maps_specifier_1_to_null, scope.PackageDirectory, moduleName);
                }

                return new SearchResult<Resolved>();
            }

            if (state.TraceEnabled)
            {
                Trace(state.Host, Diagnostics.package_json_scope_0_has_invalid_type_for_target_of_specifier_1, scope.PackageDirectory, moduleName);
            }

            return ToSearchResult<Resolved>(null);

            string? ToAbsolutePath(string? path)
                => path is null ? null : Paths.GetNormalizedAbsolutePath(path, state.Host.GetCurrentDirectory());

            string[] GetJsAndDtsExtensions()
                => [Extension.Mjs.Value, Extension.Cjs.Value, Extension.Js.Value, Extension.Json.Value, Extension.Dmts.Value, Extension.Dcts.Value, Extension.Dts.Value];

            SearchResult<Resolved>? TryLoadInputFileForPath(string finalPath, string entry, string packagePath, bool forImports)
            {
                // Replace any references to outputs for files in the program with the input files to support package
                // self-names used with outDir.
                // PROBLEM: We don't know how to calculate the output paths yet, because the "common source directory" we
                // use as the base of the file structure we reproduce into the output directory is based on the set of input
                // files, which we're still in the process of traversing and resolving! _Given that_, we have to guess what
                // the base of the output directory is (obviously the user wrote the export map, so has some idea what it
                // is!). We are going to probe _so many_ possible paths. We limit where we'll do this to try to reduce the
                // possibilities of false positive lookups.
                if (
                    !state.IsConfigLookup
                    && (Js.IsTruthy(state.CompilerOptions.DeclarationDir) || Js.IsTruthy(state.CompilerOptions.OutDir))
                    && !finalPath.Contains("/node_modules/")
                    && (state.CompilerOptions.ConfigFile is not null
                        ? Paths.ContainsPath(
                            scope.PackageDirectory,
                            ToAbsolutePath(GetConfigFileForResolution(state.CompilerOptions)!)!,
                            !UseCaseSensitiveFileNames(state))
                        : true)
                )
                {
                    // So that all means we'll only try these guesses for files outside `node_modules` in a directory where
                    // the `package.json` and `tsconfig.json` are siblings. Even with all that, we still don't know if the
                    // root of the output file structure will be (relative to the package file) `.`, `./src` or any other
                    // deeper directory structure. (If project references are used, it's definitely `.` by fiat, so that
                    // should be pretty common.)
                    var getCanonicalFileName = Core.CreateGetCanonicalFileName(UseCaseSensitiveFileNames(state));
                    var commonSourceDirGuesses = new List<string>();
                    // A `rootDir` compiler option strongly indicates the root location.
                    // A `composite` project is using project references and has its common src dir set to `.`, so it
                    // shouldn't need to check any other locations.
                    if (Js.IsTruthy(state.CompilerOptions.RootDir) || Js.IsTruthy(state.CompilerOptions.ConfigFilePath))
                    {
                        var commonDir = ToAbsolutePath(GetCommonSourceDirectoryForResolution(
                            state.CompilerOptions,
                            [],
                            state.Host.GetCurrentDirectory(),
                            getCanonicalFileName))!;
                        commonSourceDirGuesses.Add(commonDir);
                    }
                    else if (state.RequestContainingDirectory is not null)
                    {
                        // However without either of those set we're in the dark. Let's say you have
                        //
                        // ./tools/index.ts
                        // ./src/index.ts
                        // ./dist/index.js
                        // ./package.json <-- references ./dist/index.js
                        // ./tsconfig.json <-- loads ./src/index.ts
                        //
                        // How do we know `./src` is the common src dir, and not `./tools`, given only the `./dist` out
                        // dir and `./dist/index.js` filename? Answer: We... don't. We know we're looking for an
                        // `index.ts` input file, but we have _no clue_ which subfolder it's supposed to be loaded from
                        // without more context. But we do have more context! Just a tiny bit more! We're resolving an
                        // import _for some other input file_! And that input file, too, must be inside the common source
                        // directory! So we propagate that tidbit of info all the way to here via
                        // state.requestContainingDirectory
                        var requestingFile = ToAbsolutePath(Paths.CombinePaths(state.RequestContainingDirectory, "index.ts"))!;
                        // And we can try every folder above the common folder for the request folder and the
                        // config/package base directory. This technically can be wrong - we may load ./src/index.ts when
                        // ./src/sub/index.ts was right because we don't know if only `./src/sub` files were loaded by the
                        // program; but this has the best chance to be right of just about anything else we have. And,
                        // given that we're about to load `./src/index.ts` because we choose it as likely correct, there
                        // will then be a file outside of `./src/sub` in the program (the file we resolved to), making us
                        // de-facto right. So this fallback lookup logic may influence what files are pulled in by
                        // self-names, which in turn influences the output path shape, but it's all internally consistent so
                        // the paths should be stable so long as we prefer the "most general" (meaning: top-most-level
                        // directory) possible results first.
                        var commonDir = ToAbsolutePath(GetCommonSourceDirectoryForResolution(
                            state.CompilerOptions,
                            [requestingFile, ToAbsolutePath(packagePath)!],
                            state.Host.GetCurrentDirectory(),
                            getCanonicalFileName))!;
                        commonSourceDirGuesses.Add(commonDir);

                        var fragment = Paths.EnsureTrailingDirectorySeparator(commonDir);
                        while (Js.IsTruthy(fragment) && fragment.Length > 1)
                        {
                            var parts = Paths.GetPathComponents(fragment);
                            parts.RemoveAt(parts.Count - 1); // remove a directory
                            var parentCommonDir = Paths.GetPathFromPathComponents(parts);
                            commonSourceDirGuesses.Insert(0, parentCommonDir);
                            fragment = Paths.EnsureTrailingDirectorySeparator(parentCommonDir);
                        }
                    }

                    if (commonSourceDirGuesses.Count > 1)
                    {
                        state.ReportDiagnostic(Utilities.CreateCompilerDiagnostic(
                            forImports
                                ? Diagnostics.The_project_root_is_ambiguous_but_is_required_to_resolve_import_map_entry_0_in_file_1_Supply_the_rootDir_compiler_option_to_disambiguate
                                : Diagnostics.The_project_root_is_ambiguous_but_is_required_to_resolve_export_map_entry_0_in_file_1_Supply_the_rootDir_compiler_option_to_disambiguate,
                            // replace empty string with `.` - the reverse of the operation done when entries are built -
                            // so main entrypoint errors don't look weird
                            entry.Length == 0 ? "." : entry,
                            packagePath));
                    }

                    foreach (var commonSourceDirGuess in commonSourceDirGuesses)
                    {
                        var candidateDirectories = GetOutputDirectoriesForBaseDirectory(commonSourceDirGuess);
                        foreach (var candidateDir in candidateDirectories)
                        {
                            if (Paths.ContainsPath(candidateDir, finalPath, !UseCaseSensitiveFileNames(state)))
                            {
                                // The matched export is looking up something in either the out declaration or js dir, now
                                // map the written path back into the source dir and source extension
                                var pathFragment = JsSlice(finalPath, candidateDir.Length + 1); // +1 to also remove directory separator
                                var possibleInputBase = Paths.CombinePaths(commonSourceDirGuess, pathFragment);
                                foreach (var ext in GetJsAndDtsExtensions())
                                {
                                    if (Paths.FileExtensionIs(possibleInputBase, ext))
                                    {
                                        var inputExts = Utilities.GetPossibleOriginalInputExtensionForExtension(possibleInputBase);
                                        foreach (var possibleExt in inputExts)
                                        {
                                            if (!ExtensionIsOk(extensions, possibleExt)) continue;
                                            var possibleInputWithInputExtension = Paths.ChangeAnyExtension(possibleInputBase, possibleExt, ext, !UseCaseSensitiveFileNames(state));
                                            if (state.Host.FileExists(possibleInputWithInputExtension))
                                            {
                                                return ToSearchResult(WithPackageId(
                                                    scope,
                                                    LoadFileNameFromPackageJsonField(extensions, possibleInputWithInputExtension, packageJsonValue: null, onlyRecordFailures: false, state),
                                                    state));
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                return null;

                List<string> GetOutputDirectoriesForBaseDirectory(string commonSourceDirGuess)
                {
                    // Config file output paths are processed to be relative to the host's current directory, while
                    // otherwise the paths are resolved relative to the common source dir the compiler puts together
                    var currentDir = state.CompilerOptions.ConfigFile is not null
                        ? state.Host.GetCurrentDirectory()
                        : commonSourceDirGuess;
                    var candidateDirectories = new List<string>();
                    if (Js.IsTruthy(state.CompilerOptions.DeclarationDir))
                    {
                        candidateDirectories.Add(ToAbsolutePath(CombineDirectoryPath(currentDir, state.CompilerOptions.DeclarationDir!))!);
                    }

                    if (Js.IsTruthy(state.CompilerOptions.OutDir) && state.CompilerOptions.OutDir != state.CompilerOptions.DeclarationDir)
                    {
                        candidateDirectories.Add(ToAbsolutePath(CombineDirectoryPath(currentDir, state.CompilerOptions.OutDir!))!);
                    }

                    return candidateDirectories;

                    static string CombineDirectoryPath(string root, string dir)
                        => Paths.EnsureTrailingDirectorySeparator(Paths.CombinePaths(root, dir));
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:2989-2995 — <c>isApplicableVersionedTypesKey</c>.</summary>
    public static bool IsApplicableVersionedTypesKey(IList<string> conditions, string key)
    {
        if (!conditions.Contains("types")) return false; // only apply versioned types conditions if the types condition is applied
        if (!Core.StartsWith(key, "types@")) return false;
        var range = VersionRange.TryParse(JsSlice(key, "types@".Length));
        if (range is null) return false;
        return range.Test(VersionInfo.Version);
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers the loaders above share
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The value of <c>getCommonSourceDirectory</c> (<c>emitter.ts:636-660</c>) for the one caller in
    /// <c>moduleNameResolver.ts</c>.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: <c>getCommonSourceDirectory</c> is M7's, so the port cannot call it; that caller reaches only two of its
    /// three arms (the first is guarded on <c>rootDir || configFilePath</c>, the second has neither and falls through to
    /// the computed arm), and both live here with the reference's trailing-separator behavior. When M7 lands the emitter
    /// version this helper should go away in its favor.
    /// </remarks>
    private static string GetCommonSourceDirectoryForResolution(
        ICompilerOptions options,
        IList<string> emittedFiles,
        string currentDirectory,
        Core.GetCanonicalFileName getCanonicalFileName)
    {
        string commonSourceDirectory;
        if (Js.IsTruthy(options.RootDir))
        {
            // If a rootDir is specified use it as the commonSourceDirectory
            commonSourceDirectory = Paths.GetNormalizedAbsolutePath(options.RootDir!, currentDirectory);
        }
        else if (Js.IsTruthy(options.ConfigFilePath))
        {
            // Project compilations never infer their root from the input source paths
            commonSourceDirectory = Paths.GetDirectoryPath(Paths.NormalizeSlashes(options.ConfigFilePath!));
        }
        else
        {
            commonSourceDirectory = Utilities.ComputeCommonSourceDirectoryOfFilenames(emittedFiles, currentDirectory, getCanonicalFileName);
        }

        if (Js.IsTruthy(commonSourceDirectory) && !Core.EndsWith(commonSourceDirectory, Paths.DirectorySeparator))
        {
            // Make sure the directory path ends with a directory separator so this string can be used directly to replace
            // with "" to get the relative path of the source file and the relative path doesn't start with / making it a
            // rooted path
            commonSourceDirectory += Paths.DirectorySeparator;
        }

        return commonSourceDirectory;
    }

    /// <summary>
    /// The file name TS reads as <c>state.compilerOptions.configFile.fileName</c>.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: the generated <see cref="ITsConfigSourceFile"/> is an <c>INode</c>-derived interface without a
    /// <c>fileName</c> slot (in TS the object is a <c>JsonSourceFile</c>, so it has one). The port therefore reads the
    /// file name off the config object when it is a <see cref="SourceFile"/> — which is what M8's parser will build —
    /// and falls back to the <c>configFilePath</c> option, the same file name the parser attaches, otherwise.
    /// </remarks>
    private static string? GetConfigFileForResolution(ICompilerOptions options)
        => options.ConfigFile is SourceFile configSourceFile ? configSourceFile.FileName : options.ConfigFilePath;

    /// <summary>ported from: src/compiler/moduleNameResolver.ts:3424-3428 — <c>useCaseSensitiveFileNames</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>!host.useCaseSensitiveFileNames</c> arm answers <c>true</c> for an absent member (unlike
    /// <c>Utilities.HostUsesCaseSensitiveFileNames</c>, which answers <c>false</c>); the generated host interface types
    /// the slot <c>object?</c> so both a boolean and a function are accepted here, exactly as the reference's three-arm
    /// test does.
    /// </remarks>
    private static bool UseCaseSensitiveFileNames(ModuleResolutionState state)
        => state.Host.UseCaseSensitiveFileNames switch
        {
            bool b => b,
            Func<bool> f => f(),
            _ => true,
        };

    /// <summary>JS's <c>Array.prototype.slice</c> for the string lists the reference slices.</summary>
    private static List<string> JsSliceList(IList<string> list, int start, int? end = null)
    {
        var count = list.Count;
        var begin = start < 0 ? Math.Max(count + start, 0) : Math.Min(start, count);
        var e = end is null ? count : (end.Value < 0 ? Math.Max(count + end.Value, 0) : Math.Min(end.Value, count));
        var result = new List<string>();
        for (var i = begin; i < e; i++)
        {
            result.Add(list[i]);
        }

        return result;
    }

    /// <summary>
    /// ported from: src/compiler/moduleNameResolver.ts:3414-3416 — <c>toSearchResult</c>.
    /// </summary>
    /// <remarks>Wraps a value in a <see cref="SearchResult{T}"/>: <c>null</c> when the value is absent, the instance otherwise.</remarks>
    private static SearchResult<T>? ToSearchResult<T>(T? value)
        where T : class
        => value is null ? null : new SearchResult<T> { Value = value };
}
