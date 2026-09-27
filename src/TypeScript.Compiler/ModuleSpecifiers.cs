using System.Text.RegularExpressions;

namespace TypeScript.Compiler;

// ported from: src/compiler/moduleSpecifiers.ts:1-1470
//    133-161    stringToRegex (memoized)
//    164-181    RelativePreference / ModuleSpecifierPreferences
//    182-253    getModuleSpecifierPreferences (with its nested getPreferredEnding)
//    255-299    updateModuleSpecifier / getModuleSpecifier / getNodeModulesPackageName
//    301-316    getModuleSpecifierWorker
//    318-355    tryGetModuleSpecifiersFromCache / tryGetModuleSpecifiersFromCacheWorker
//    357-450    getModuleSpecifiers / ModuleSpecifierResult / getModuleSpecifiersWithCacheInfo /
//               getLocalModuleSpecifierBetweenFileNames
//    453-560    computeModuleSpecifiers / isExcludedByRegex
//    562-579    Info / getInfo
//    581-697    getLocalModuleSpecifier / packageJsonPathsAreEqual / countPathComponents /
//               comparePathsByRedirectAndNumberOfDirectorySeparators / getNearestAncestorDirectoryWithPackageJson
//    713-772    forEachFileNameOfModule
//    774-877    getAllModulePaths / runtimeDependencyFields / getAllRuntimeDependencies / getAllModulePathsWorker
//    879-924    tryGetModuleNameFromAmbientModule
//    926-1012   tryGetModuleNameFromPaths
//    1014-1167  MatchingMode / tryGetModuleNameFromExportsOrImports / tryGetModuleNameFromExports /
//               tryGetModuleNameFromPackageJsonImports
//    1169-1184  tryGetModuleNameFromRootDirs
//    1186-1343  tryGetModuleNameAsNodeModule (with its nested tryDirectoryWithPackageJson)
//    1345-1414  tryGetAnyFileFromPath / getPathsRelativeToRootDirs / processEnding
//    1416-1470  tryGetRealFileNameForNonJsDeclarationFileName / getJSExtensionForFile /
//               tryGetJSExtensionForFile / getRelativePathIfInSameVolume / isPathRelativeToParent /
//               getDefaultResolutionModeForFile / prefersTsExtension
//
// The support members the reference imports from `utilities.ts`, `moduleNameResolver.ts` and `program.ts` and that no
// ported file declares yet are at the bottom under "The support the reference imports" — each names its own home
// there.
/// <summary>
/// Module specifier synthesis: the specifiers declaration emit and the language service generate for a module symbol
/// or a target file name (<see cref="GetModuleSpecifiers"/>, <see cref="GetModuleSpecifier"/>, …), the preferences
/// that steer them, and the path/`node_modules`/`paths`/`package.json`-mapping machinery behind them.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS has module-level functions; the port puts them on a static class (§10.1's <c>Core</c>/<c>Utilities</c>
/// treatment), and the shapes the reference declares at module level (<c>RelativePreference</c>,
/// <c>ModuleSpecifierPreferences</c>, <c>Info</c>, <c>MatchingMode</c>, …) are nested in it, so their names stay scoped
/// to the module they belong to.
/// </para>
/// <para>
/// PORT-NOTE (<c>ResolutionMode</c>): TS's <c>ResolutionMode = ModuleKind.ESNext | ModuleKind.CommonJS | undefined</c>
/// has no generated counterpart, so every slot this file owns spells it <see cref="ModuleKind"/>?; the generated
/// interfaces that carry it at their own boundary spell it <c>object?</c>, and the two meet through the casts at the
/// reads (<c>ModuleSpecifierOptions.OverrideMode</c>, <c>SourceFile.ImpliedNodeFormat</c>).
/// </para>
/// <para>
/// PORT-NOTE (<c>FutureSourceFile</c>): TS types <c>importingSourceFile</c> as
/// <c>SourceFile | FutureSourceFile</c> — the file renamer's not-yet-parsed file — and tests it with
/// <c>isFullSourceFile</c>, which checks <c>kind === SyntaxKind.SourceFile</c>. The port has no <c>FutureSourceFile</c>
/// type and no caller that passes one, so every such parameter is a <see cref="SourceFile"/> and the two
/// <c>isFullSourceFile</c> sites (<c>:465</c>, <c>:1464</c>) take the <c>SourceFile</c> arm, which is the arm a real
/// source file takes in the reference.
/// </para>
/// <para>
/// PORT-NOTE (<c>UserPreferences</c>): the reference's public members take types.ts's <c>UserPreferences</c> (an
/// interface of optional language-service preferences) and read exactly three members of it —
/// <c>importModuleSpecifierPreference</c>, <c>importModuleSpecifierEnding</c> and <c>autoImportSpecifierExcludeRegexes</c>
/// (<c>:184-232</c>, <c>:392</c>) — while the generated <see cref="IModuleSpecifierCache"/> also keys on it. The port's
/// generated <see cref="IUserPreferences"/> is get-only, so <see cref="UserPreferences"/> is its implementation (the
/// object literals the reference's callers pass).
/// </para>
/// <para>
/// PORT-NOTE (optional host members): the reference reads <c>host.readFile</c>, <c>host.getModuleSpecifierCache</c>,
/// <c>host.getModuleResolutionCache</c>, <c>host.getNearestAncestorDirectoryWithPackageJson</c> and
/// <c>host.getSymlinkCache</c> to decide whether the host supports them. The generated
/// <see cref="IModuleSpecifierResolutionHost"/> declares them all required, so the calls are made; each site that
/// dropped a guard says so. Two of them — <see cref="Program"/> reports the two members it cannot express as
/// <see cref="Program.GeneratedContractException"/> — are read through the helpers below, which map that exception
/// back to the reference's <c>?.</c>/absent-member answer.
/// </para>
/// </remarks>
internal static class ModuleSpecifiers
{
    // ---------------------------------------------------------------------------------------------
    // The shapes (moduleSpecifiers.ts:164-181, 379-384, 562-567, 1014-1018 and the types.ts ones)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:164-169 — <c>RelativePreference</c>.</summary>
    internal enum RelativePreference
    {
        Relative,

        NonRelative,

        Shortest,

        ExternalNonRelative,
    }

    /// <summary>ported from: src/compiler/utilities.ts:10044-10049 — <c>ModuleSpecifierEnding</c>.</summary>
    /// <remarks>PORT-NOTE (home): <c>utilities.ts</c>'s enum, which <c>moduleSpecifiers.ts</c> imports; it is read only
    /// here, so it is declared with its readers.</remarks>
    internal enum ModuleSpecifierEnding
    {
        Minimal,

        Index,

        JsExtension,

        TsExtension,
    }

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:172-180 — <c>ModuleSpecifierPreferences</c>.</summary>
    internal sealed class ModuleSpecifierPreferences
    {
        /// <summary><c>relativePreference</c>.</summary>
        public required RelativePreference RelativePreference { get; init; }

        /// <summary><c>getAllowedEndingsInPreferredOrder</c>.</summary>
        /// <remarks>PORT-NOTE: TS closes this over <c>oldImportSpecifier</c>, the importing file and the host;
        /// <see cref="GetModuleSpecifierPreferences"/> builds it the same way (the delegate is the method's local
        /// function).</remarks>
        public required Func<ModuleKind?, List<ModuleSpecifierEnding>> GetAllowedEndingsInPreferredOrder { get; init; }

        /// <summary><c>excludeRegexes</c>.</summary>
        public List<string>? ExcludeRegexes { get; init; }
    }

    /// <summary>ported from: src/compiler/types.ts:9960-10010 — the <c>UserPreferences</c> object literal the
    /// reference's callers write (<c>{ importModuleSpecifierPreference: … }</c>, <c>{}</c>).</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>UserPreferences</c> is an interface of optional members that callers satisfy with object
    /// literals; the port's generated <see cref="IUserPreferences"/> is that interface with every member get-only, so
    /// this class is its one implementation (the compiler-side literal the checker's <c>getSpecifierForModuleSymbol</c>
    /// and this file's <c>{}</c> writes build). The members the language service owns answer <c>null</c>, which is the
    /// absence an object literal that never sets them also has.
    /// </remarks>
    internal sealed class UserPreferences : IUserPreferences
    {
        public bool? DisableSuggestions { get; init; }

        public string? QuotePreference { get; init; }

        public bool? IncludeCompletionsForModuleExports { get; init; }

        public bool? IncludeCompletionsForImportStatements { get; init; }

        public bool? IncludeCompletionsWithSnippetText { get; init; }

        public bool? IncludeAutomaticOptionalChainCompletions { get; init; }

        public bool? IncludeCompletionsWithInsertText { get; init; }

        public bool? IncludeCompletionsWithClassMemberSnippets { get; init; }

        public bool? IncludeCompletionsWithObjectLiteralMethodSnippets { get; init; }

        public bool? UseLabelDetailsInCompletionEntries { get; init; }

        public bool? AllowIncompleteCompletions { get; init; }

        public string? ImportModuleSpecifierPreference { get; init; }

        public string? ImportModuleSpecifierEnding { get; init; }

        public bool? AllowTextChangesInNewFiles { get; init; }

        public bool? ProvidePrefixAndSuffixTextForRename { get; init; }

        public string? IncludePackageJsonAutoImports { get; init; }

        public bool? ProvideRefactorNotApplicableReason { get; init; }

        public string? JsxAttributeCompletionStyle { get; init; }

        public string? IncludeInlayParameterNameHints { get; init; }

        public bool? IncludeInlayParameterNameHintsWhenArgumentMatchesName { get; init; }

        public bool? IncludeInlayFunctionParameterTypeHints { get; init; }

        public bool? IncludeInlayVariableTypeHints { get; init; }

        public bool? IncludeInlayVariableTypeHintsWhenTypeMatchesName { get; init; }

        public bool? IncludeInlayPropertyDeclarationTypeHints { get; init; }

        public bool? IncludeInlayFunctionLikeReturnTypeHints { get; init; }

        public bool? IncludeInlayEnumMemberValueHints { get; init; }

        public bool? InteractiveInlayHints { get; init; }

        public bool? AllowRenameOfImportPath { get; init; }

        public List<string>? AutoImportFileExcludePatterns { get; init; }

        public List<string>? AutoImportSpecifierExcludeRegexes { get; init; }

        public bool? PreferTypeOnlyAutoImports { get; init; }

        public object? OrganizeImportsIgnoreCase { get; init; }

        public string? OrganizeImportsCollation { get; init; }

        public string? OrganizeImportsLocale { get; init; }

        public bool? OrganizeImportsNumericCollation { get; init; }

        public bool? OrganizeImportsAccentCollation { get; init; }

        public object? OrganizeImportsCaseFirst { get; init; }

        public string? OrganizeImportsTypeOrder { get; init; }

        public bool? ExcludeLibrarySymbolsInNavTo { get; init; }

        public bool? LazyConfiguredProjectsFromExternalProject { get; init; }

        public bool? DisplayPartsForJSDoc { get; init; }

        public bool? GenerateReturnInDocTemplate { get; init; }

        public bool? DisableLineTextInReferences { get; init; }

        public int? MaximumHoverLength { get; init; }
    }

    /// <summary>ported from: src/compiler/types.ts:10020-10022 — <c>ModuleSpecifierOptions</c>.</summary>
    internal sealed class ModuleSpecifierOptions : IModuleSpecifierOptions
    {
        /// <summary><c>overrideImportMode</c>.</summary>
        public ModuleKind? OverrideMode { get; set; }

        /// <remarks>PORT-NOTE: the generated slot is <c>object?</c> (the generator has no type for
        /// <c>ResolutionMode</c>), the same boxing <c>SourceFile.ImpliedNodeFormat</c> uses.</remarks>
        object? IModuleSpecifierOptions.OverrideImportMode
        {
            get => OverrideMode;
            set => OverrideMode = value as ModuleKind?;
        }
    }

    /// <summary>ported from: src/compiler/types.ts:10005-10009 — <c>ModulePath</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>ModulePath</c> object literal; the port's <see cref="IModulePath"/> is the generated
    /// interface and this is its one implementation (the only producer is <see cref="GetAllModulePathsWorker"/>).</remarks>
    internal sealed class ModulePath : IModulePath
    {
        public string? Path { get; set; }

        public bool IsInNodeModules { get; set; }

        public bool IsRedirect { get; set; }
    }

    /// <summary>ported from: src/compiler/types.ts:9900-9904 — <c>FileExtensionInfo</c>.</summary>
    /// <remarks>PORT-NOTE: the port's <see cref="IFileExtensionInfo"/> is the generated interface; the one producer is
    /// <see cref="TryGetAnyFileFromPath"/>.</remarks>
    internal sealed class FileExtensionInfo : IFileExtensionInfo
    {
        public string? Extension { get; set; }

        public bool IsMixedContent { get; set; }

        public ScriptKind? ScriptKind { get; set; }
    }

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:562-567 — <c>Info</c>.</summary>
    /// <remarks>PORT-NOTE (name): TS's <c>Info</c> is file-local; the port qualifies it because a bare <c>Info</c> in
    /// the namespace would collide with every host's own notion of one.</remarks>
    internal sealed class ModuleSpecifierInfo
    {
        public required Core.GetCanonicalFileName GetCanonicalFileName { get; init; }

        public required string ImportingSourceFileName { get; init; }

        public required string SourceDirectory { get; init; }

        public required string CanonicalSourceDirectory { get; init; }
    }

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:379-383 — <c>ModuleSpecifierResult</c>.</summary>
    internal sealed class ModuleSpecifierResult
    {
        /// <summary>TS's <c>ResolvedModuleSpecifierInfo["kind"]</c>: <c>"ambient" | "node_modules" | "paths" | "redirect" | "relative" | undefined</c>.</summary>
        public string? Kind { get; init; }

        public required List<string> ModuleSpecifiers { get; init; }

        public bool ComputedWithoutCache { get; init; }
    }

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:1014-1018 — <c>MatchingMode</c>.</summary>
    private enum MatchingMode
    {
        Exact,

        Directory,

        Pattern,
    }

    /// <summary>ported from: src/compiler/utilities.ts:10956-10961 — <c>NodeModulePathParts</c>.</summary>
    internal sealed class NodeModulePathParts
    {
        public required int TopLevelNodeModulesIndex { get; init; }

        public required int TopLevelPackageNameIndex { get; init; }

        public required int PackageRootIndex { get; init; }

        public required int FileNameIndex { get; init; }
    }

    // ---------------------------------------------------------------------------------------------
    // The preferences (moduleSpecifiers.ts:133-253)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:133-161 — <c>stringToRegex</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>memoizeOne</c> caches by argument identity and answers <c>undefined</c> for a pattern
    /// <c>RegExp</c> rejects; both are <see cref="Core.MemoizeOne{A,T}"/> plus the <c>catch</c> arm here.
    /// <para>
    /// PORT-NOTE (regex dialect): the flags TS keeps are <c>i</c> and <c>u</c>; <see cref="RegexOptions.IgnoreCase"/>
    /// is <c>i</c>, and <c>u</c> has no .NET counterpart because .NET's regexes are Unicode-aware by default. The
    /// dialect is otherwise .NET's, the treatment the port's other transcribed patterns get.
    /// </para>
    /// </remarks>
    private static readonly Func<string, Regex?> s_stringToRegex = Core.MemoizeOne<string, Regex?>(pattern =>
    {
        try
        {
            var slash = pattern.IndexOf('/');
            if (slash != 0)
            {
                // No leading slash, treat as a pattern
                return new Regex(pattern);
            }

            var lastSlash = pattern.LastIndexOf('/');
            if (slash == lastSlash)
            {
                // Only one slash, treat as a pattern
                return new Regex(pattern);
            }

            while ((slash = pattern.IndexOf('/', slash + 1)) != lastSlash)
            {
                if (pattern[slash - 1] != '\\')
                {
                    // Unescaped middle slash, treat as a pattern
                    return new Regex(pattern);
                }
            }

            // Only case-insensitive and unicode flags make sense
            var options = RegexOptions.None;
            foreach (var flag in pattern[(lastSlash + 1)..])
            {
                if (flag == 'i') options |= RegexOptions.IgnoreCase;
            }

            return new Regex(pattern[1..lastSlash], options);
        }
        catch
        {
            return null;
        }
    });

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:182-253 — <c>getModuleSpecifierPreferences</c> (with its nested <c>getPreferredEnding</c>).</summary>
    /// <remarks>
    /// PORT-NOTE: <c>getAllowedEndingsInPreferredOrder</c> closes over the importing file, the host, the options and
    /// the previous specifier, so the local <c>getPreferredEnding</c> stays a local function (contract §5) and the
    /// returned preferences carry it as the delegate.
    /// </remarks>
    public static ModuleSpecifierPreferences GetModuleSpecifierPreferences(
        IUserPreferences preferences,
        IModuleSpecifierResolutionHost host,
        ICompilerOptions compilerOptions,
        SourceFile importingSourceFile,
        string? oldImportSpecifier = null)
    {
        var filePreferredEnding = GetPreferredEnding();
        return new ModuleSpecifierPreferences
        {
            ExcludeRegexes = preferences.AutoImportSpecifierExcludeRegexes,
            RelativePreference = oldImportSpecifier is not null
                ? Utilities.IsExternalModuleNameRelative(oldImportSpecifier) ? RelativePreference.Relative : RelativePreference.NonRelative
                : preferences.ImportModuleSpecifierPreference switch
                {
                    "relative" => RelativePreference.Relative,
                    "non-relative" => RelativePreference.NonRelative,
                    "project-relative" => RelativePreference.ExternalNonRelative,
                    _ => RelativePreference.Shortest,
                },
            GetAllowedEndingsInPreferredOrder = syntaxImpliedNodeFormat =>
            {
                var impliedNodeFormat = GetDefaultResolutionModeForFile(importingSourceFile, host, compilerOptions);
                var preferredEnding = syntaxImpliedNodeFormat != impliedNodeFormat ? GetPreferredEnding(syntaxImpliedNodeFormat) : filePreferredEnding;
                var moduleResolution = Utilities.GetEmitModuleResolutionKind(compilerOptions);
                if ((syntaxImpliedNodeFormat ?? impliedNodeFormat) == ModuleKind.ESNext
                    && ModuleResolutionKind.Node16 <= moduleResolution && moduleResolution <= ModuleResolutionKind.NodeNext)
                {
                    if (ModuleNameResolver.ShouldAllowImportingTsExtension(compilerOptions, importingSourceFile.FileName))
                    {
                        return [ModuleSpecifierEnding.TsExtension, ModuleSpecifierEnding.JsExtension];
                    }

                    return [ModuleSpecifierEnding.JsExtension];
                }

                if (Utilities.GetEmitModuleResolutionKind(compilerOptions) == ModuleResolutionKind.Classic)
                {
                    return preferredEnding == ModuleSpecifierEnding.JsExtension
                        ? [ModuleSpecifierEnding.JsExtension, ModuleSpecifierEnding.Index]
                        : [ModuleSpecifierEnding.Index, ModuleSpecifierEnding.JsExtension];
                }

                var allowImportingTsExtension = ModuleNameResolver.ShouldAllowImportingTsExtension(compilerOptions, importingSourceFile.FileName);
                switch (preferredEnding)
                {
                    case ModuleSpecifierEnding.JsExtension:
                        return allowImportingTsExtension
                            ? [ModuleSpecifierEnding.JsExtension, ModuleSpecifierEnding.TsExtension, ModuleSpecifierEnding.Minimal, ModuleSpecifierEnding.Index]
                            : [ModuleSpecifierEnding.JsExtension, ModuleSpecifierEnding.Minimal, ModuleSpecifierEnding.Index];
                    case ModuleSpecifierEnding.TsExtension:
                        return [ModuleSpecifierEnding.TsExtension, ModuleSpecifierEnding.Minimal, ModuleSpecifierEnding.JsExtension, ModuleSpecifierEnding.Index];
                    case ModuleSpecifierEnding.Index:
                        return allowImportingTsExtension
                            ? [ModuleSpecifierEnding.Index, ModuleSpecifierEnding.Minimal, ModuleSpecifierEnding.TsExtension, ModuleSpecifierEnding.JsExtension]
                            : [ModuleSpecifierEnding.Index, ModuleSpecifierEnding.Minimal, ModuleSpecifierEnding.JsExtension];
                    case ModuleSpecifierEnding.Minimal:
                        return allowImportingTsExtension
                            ? [ModuleSpecifierEnding.Minimal, ModuleSpecifierEnding.Index, ModuleSpecifierEnding.TsExtension, ModuleSpecifierEnding.JsExtension]
                            : [ModuleSpecifierEnding.Minimal, ModuleSpecifierEnding.Index, ModuleSpecifierEnding.JsExtension];
                    default:
                        Debug.AssertNever(preferredEnding);
                        return [];
                }
            },
        };

        ModuleSpecifierEnding GetPreferredEnding(ModuleKind? resolutionMode = null)
        {
            if (oldImportSpecifier is not null)
            {
                if (ModuleTransformSupport.HasJSFileExtension(oldImportSpecifier)) return ModuleSpecifierEnding.JsExtension;
                if (Core.EndsWith(oldImportSpecifier, "/index")) return ModuleSpecifierEnding.Index;
            }

            return GetModuleSpecifierEndingPreference(
                preferences.ImportModuleSpecifierEnding,
                resolutionMode ?? GetDefaultResolutionModeForFile(importingSourceFile, host, compilerOptions),
                compilerOptions,
                importingSourceFile);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The entry points (moduleSpecifiers.ts:255-450)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:255-266 — <c>updateModuleSpecifier</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS passes an empty <c>UserPreferences</c> (<c>{}</c>) and an empty <c>ModuleSpecifierOptions</c>
    /// (<c>{}</c>) here and to <see cref="GetModuleSpecifier"/>; the port passes empty instances of the two shapes.
    /// </remarks>
    public static string? UpdateModuleSpecifier(
        ICompilerOptions compilerOptions,
        SourceFile importingSourceFile,
        string importingSourceFileName,
        string toFileName,
        IModuleSpecifierResolutionHost host,
        string oldImportSpecifier,
        ModuleSpecifierOptions? options = null)
    {
        var res = GetModuleSpecifierWorker(
            compilerOptions,
            importingSourceFile,
            importingSourceFileName,
            toFileName,
            host,
            GetModuleSpecifierPreferences(
                new UserPreferences(),
                host,
                compilerOptions,
                importingSourceFile,
                oldImportSpecifier),
            new UserPreferences(),
            options);
        if (res == oldImportSpecifier) return null;
        return res;
    }

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:276-286 — <c>getModuleSpecifier</c>.</summary>
    public static string GetModuleSpecifier(
        ICompilerOptions compilerOptions,
        SourceFile importingSourceFile,
        string importingSourceFileName,
        string toFileName,
        IModuleSpecifierResolutionHost host,
        ModuleSpecifierOptions? options = null)
        => GetModuleSpecifierWorker(
            compilerOptions,
            importingSourceFile,
            importingSourceFileName,
            toFileName,
            host,
            GetModuleSpecifierPreferences(new UserPreferences(), host, compilerOptions, importingSourceFile),
            new UserPreferences(),
            options);

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:288-299 — <c>getNodeModulesPackageName</c>.</summary>
    public static string? GetNodeModulesPackageName(
        ICompilerOptions compilerOptions,
        SourceFile importingSourceFile,
        string nodeModulesFileName,
        IModuleSpecifierResolutionHost host,
        IUserPreferences preferences,
        ModuleSpecifierOptions? options = null)
    {
        var info = GetInfo(importingSourceFile.FileName!, host);
        var modulePaths = GetAllModulePaths(info, nodeModulesFileName, host, preferences, compilerOptions, options);
        return Core.FirstDefined<IModulePath, string>(
            modulePaths,
            (modulePath, _) => TryGetModuleNameAsNodeModule(
                modulePath,
                info,
                importingSourceFile,
                host,
                compilerOptions,
                preferences,
                /*packageNameOnly*/ true,
                options?.OverrideMode));
    }

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:301-316 — <c>getModuleSpecifierWorker</c>.</summary>
    private static string GetModuleSpecifierWorker(
        ICompilerOptions compilerOptions,
        SourceFile importingSourceFile,
        string importingSourceFileName,
        string toFileName,
        IModuleSpecifierResolutionHost host,
        ModuleSpecifierPreferences preferences,
        IUserPreferences userPreferences,
        ModuleSpecifierOptions? options)
    {
        var info = GetInfo(importingSourceFileName, host);
        var modulePaths = GetAllModulePaths(info, toFileName, host, userPreferences, compilerOptions, options);
        return Core.FirstDefined<IModulePath, string>(
                modulePaths,
                (modulePath, _) => TryGetModuleNameAsNodeModule(
                    modulePath,
                    info,
                    importingSourceFile,
                    host,
                    compilerOptions,
                    userPreferences,
                    /*packageNameOnly*/ null,
                    options?.OverrideMode))
            ?? GetLocalModuleSpecifier(
                toFileName,
                info,
                compilerOptions,
                host,
                options?.OverrideMode ?? GetDefaultResolutionModeForFile(importingSourceFile, host, compilerOptions),
                preferences)!;
    }

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:318-333 — <c>tryGetModuleSpecifiersFromCache</c>.</summary>
    public static ModuleSpecifierResult? TryGetModuleSpecifiersFromCache(
        ISymbol moduleSymbol,
        SourceFile importingSourceFile,
        IModuleSpecifierResolutionHost host,
        IUserPreferences userPreferences,
        ModuleSpecifierOptions? options = null)
    {
        var result = TryGetModuleSpecifiersFromCacheWorker(
            moduleSymbol,
            importingSourceFile,
            host,
            userPreferences,
            options);
        return result.Specifiers is not null
            ? new ModuleSpecifierResult
            {
                Kind = result.Kind,
                ModuleSpecifiers = result.Specifiers,
                ComputedWithoutCache = false,
            }
            : null;
    }

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:335-355 — <c>tryGetModuleSpecifiersFromCacheWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers a five-element tuple; the port returns the same five slots as a value tuple (the
    /// reference's <c>emptyArray as []</c> for a symbol with no module source file is the all-null tuple).
    /// </remarks>
    private static (
        string? Kind,
        List<string>? Specifiers,
        SourceFile? ModuleFile,
        List<IModulePath>? ModulePaths,
        IModuleSpecifierCache? Cache) TryGetModuleSpecifiersFromCacheWorker(
            ISymbol moduleSymbol,
            SourceFile importingSourceFile,
            IModuleSpecifierResolutionHost host,
            IUserPreferences userPreferences,
            ModuleSpecifierOptions? options)
    {
        var moduleSourceFile = Utilities.GetSourceFileOfModule(moduleSymbol);
        if (moduleSourceFile is null)
        {
            return (null, null, null, null, null);
        }

        var cache = GetModuleSpecifierCache(host);
        var cached = (IResolvedModuleSpecifierInfo?)cache?.Get(importingSourceFile.Path, moduleSourceFile.Path, userPreferences, options);
        return (cached?.Kind, cached?.ModuleSpecifiers, moduleSourceFile, cached?.ModulePaths, cache);
    }

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:357-377 — <c>getModuleSpecifiers</c>.</summary>
    public static List<string> GetModuleSpecifiers(
        ISymbol moduleSymbol,
        ITypeChecker checker,
        ICompilerOptions compilerOptions,
        SourceFile importingSourceFile,
        IModuleSpecifierResolutionHost host,
        IUserPreferences userPreferences,
        ModuleSpecifierOptions? options = null)
        => GetModuleSpecifiersWithCacheInfo(
            moduleSymbol,
            checker,
            compilerOptions,
            importingSourceFile,
            host,
            userPreferences,
            options,
            /*forAutoImport*/ false).ModuleSpecifiers;

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:386-431 — <c>getModuleSpecifiersWithCacheInfo</c>.</summary>
    public static ModuleSpecifierResult GetModuleSpecifiersWithCacheInfo(
        ISymbol moduleSymbol,
        ITypeChecker checker,
        ICompilerOptions compilerOptions,
        SourceFile importingSourceFile,
        IModuleSpecifierResolutionHost host,
        IUserPreferences userPreferences,
        ModuleSpecifierOptions? options = null,
        bool forAutoImport = false)
    {
        var computedWithoutCache = false;
        var ambient = TryGetModuleNameFromAmbientModule(moduleSymbol, checker);
        if (ambient is not null)
        {
            return new ModuleSpecifierResult
            {
                Kind = "ambient",
                ModuleSpecifiers = !(forAutoImport && IsExcludedByRegex(ambient, userPreferences.AutoImportSpecifierExcludeRegexes))
                    ? [ambient]
                    : [],
                ComputedWithoutCache = computedWithoutCache,
            };
        }

        var (kind, specifiers, moduleSourceFile, modulePaths, cache) = TryGetModuleSpecifiersFromCacheWorker(
            moduleSymbol,
            importingSourceFile,
            host,
            userPreferences,
            options);
        if (specifiers is not null)
        {
            return new ModuleSpecifierResult
            {
                Kind = kind,
                ModuleSpecifiers = specifiers,
                ComputedWithoutCache = computedWithoutCache,
            };
        }

        if (moduleSourceFile is null)
        {
            return new ModuleSpecifierResult
            {
                Kind = null,
                ModuleSpecifiers = [],
                ComputedWithoutCache = computedWithoutCache,
            };
        }

        computedWithoutCache = true;
        modulePaths ??= GetAllModulePathsWorker(
            GetInfo(importingSourceFile.FileName!, host),
            moduleSourceFile.OriginalFileName!,
            host,
            compilerOptions,
            options);
        var result = ComputeModuleSpecifiers(
            modulePaths,
            compilerOptions,
            importingSourceFile,
            host,
            userPreferences,
            options,
            forAutoImport);
        cache?.Set(importingSourceFile.Path, moduleSourceFile.Path, userPreferences, options, result.Kind, modulePaths, result.ModuleSpecifiers);
        return result;
    }

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:433-451 — <c>getLocalModuleSpecifierBetweenFileNames</c>.</summary>
    public static string GetLocalModuleSpecifierBetweenFileNames(
        SourceFile importingFile,
        string targetFileName,
        ICompilerOptions compilerOptions,
        IModuleSpecifierResolutionHost host,
        IUserPreferences preferences,
        ModuleSpecifierOptions? options = null)
    {
        var info = GetInfo(importingFile.FileName!, host);
        var importMode = options?.OverrideMode ?? (ModuleKind?)importingFile.ImpliedNodeFormat;
        return GetLocalModuleSpecifier(
            targetFileName,
            info,
            compilerOptions,
            host,
            importMode,
            GetModuleSpecifierPreferences(preferences, host, compilerOptions, importingFile))!;
    }

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:453-556 — <c>computeModuleSpecifiers</c>.</summary>
    private static ModuleSpecifierResult ComputeModuleSpecifiers(
        List<IModulePath> modulePaths,
        ICompilerOptions compilerOptions,
        SourceFile importingSourceFile,
        IModuleSpecifierResolutionHost host,
        IUserPreferences userPreferences,
        ModuleSpecifierOptions? options,
        bool forAutoImport)
    {
        var info = GetInfo(importingSourceFile.FileName!, host);
        var preferences = GetModuleSpecifierPreferences(userPreferences, host, compilerOptions, importingSourceFile);
        // PORT-NOTE: the reference's `isFullSourceFile(importingSourceFile) && …` — see the class remarks; the port's
        // parameter is always a SourceFile, so the guard is the `true` arm and only `forEach` remains.
        var existingSpecifier = Core.FirstDefined<IModulePath, string>(
            modulePaths,
            (modulePath, _) => Core.FirstDefined<IFileIncludeReason, string>(
                host.GetFileIncludeReasons().GetValueOrDefault(
                    Paths.ToPath(modulePath.Path!, host.GetCurrentDirectory(), info.GetCanonicalFileName)),
                (reason, _) =>
                {
                    if (reason.Kind != FileIncludeKind.Import || reason.File != importingSourceFile.Path) return null;
                    // If the candidate import mode doesn't match the mode we're generating for, don't consider it
                    // TODO: maybe useful to keep around as an alternative option for certain contexts where the mode is overridable
                    var index = ((IReferencedFile)reason).Index;
                    var existingMode = (ModuleKind?)host.GetModeForResolutionAtIndex(importingSourceFile, index);
                    var targetMode = options?.OverrideMode ?? (ModuleKind?)host.GetDefaultResolutionModeForFile(importingSourceFile);
                    if (existingMode != targetMode && existingMode is not null && targetMode is not null)
                    {
                        return null;
                    }

                    var specifier = Program.GetModuleNameStringLiteralAt(importingSourceFile, index).Text!;
                    // If the preference is for non relative and the module specifier is relative, ignore it
                    return preferences.RelativePreference != RelativePreference.NonRelative || !Paths.PathIsRelative(specifier)
                        ? specifier
                        : null;
                }));
        if (existingSpecifier is not null)
        {
            return new ModuleSpecifierResult
            {
                Kind = null,
                ModuleSpecifiers = [existingSpecifier],
                ComputedWithoutCache = true,
            };
        }

        var importedFileIsInNodeModules = Core.Some(modulePaths, (p, _) => p.IsInNodeModules);

        // Module specifier priority:
        //   1. "Bare package specifiers" (e.g. "@foo/bar") resulting from a path through node_modules to a package.json's "types" entry
        //   2. Specifiers generated using "paths" from tsconfig
        //   3. Non-relative specfiers resulting from a path through node_modules (e.g. "@foo/bar/path/to/file")
        //   4. Relative paths
        List<string>? nodeModulesSpecifiers = null;
        List<string>? pathsSpecifiers = null;
        List<string>? redirectPathsSpecifiers = null;
        List<string>? relativeSpecifiers = null;
        foreach (var modulePath in modulePaths)
        {
            var specifier = modulePath.IsInNodeModules
                ? TryGetModuleNameAsNodeModule(
                    modulePath,
                    info,
                    importingSourceFile,
                    host,
                    compilerOptions,
                    userPreferences,
                    /*packageNameOnly*/ null,
                    options?.OverrideMode)
                : null;
            if (specifier is not null && !(forAutoImport && IsExcludedByRegex(specifier, preferences.ExcludeRegexes)))
            {
                nodeModulesSpecifiers = (List<string>)Core.Append(nodeModulesSpecifiers, specifier)!;
                if (modulePath.IsRedirect)
                {
                    // If we got a specifier for a redirect, it was a bare package specifier (e.g. "@foo/bar",
                    // not "@foo/bar/path/to/file"). No other specifier will be this good, so stop looking.
                    return new ModuleSpecifierResult
                    {
                        Kind = "node_modules",
                        ModuleSpecifiers = nodeModulesSpecifiers,
                        ComputedWithoutCache = true,
                    };
                }
            }

            var local = GetLocalModuleSpecifier(
                modulePath.Path!,
                info,
                compilerOptions,
                host,
                options?.OverrideMode ?? (ModuleKind?)importingSourceFile.ImpliedNodeFormat,
                preferences,
                /*pathsOnly*/ modulePath.IsRedirect || specifier is not null);
            if (local is null || forAutoImport && IsExcludedByRegex(local, preferences.ExcludeRegexes))
            {
                continue;
            }

            if (modulePath.IsRedirect)
            {
                redirectPathsSpecifiers = (List<string>)Core.Append(redirectPathsSpecifiers, local)!;
            }
            else if (Paths.PathIsBareSpecifier(local))
            {
                if (ModuleNameResolver.PathContainsNodeModules(local))
                {
                    // We could be in this branch due to inappropriate use of `baseUrl`, not intentional `paths`
                    // usage. It's impossible to reason about where to prioritize baseUrl-generated module
                    // specifiers, but if they contain `/node_modules/`, they're going to trigger a portability
                    // error, so *at least* don't prioritize those.
                    relativeSpecifiers = (List<string>)Core.Append(relativeSpecifiers, local)!;
                }
                else
                {
                    pathsSpecifiers = (List<string>)Core.Append(pathsSpecifiers, local)!;
                }
            }
            else if (forAutoImport || !importedFileIsInNodeModules || modulePath.IsInNodeModules)
            {
                // Why this extra conditional, not just an `else`? If some path to the file contained
                // 'node_modules', but we can't create a non-relative specifier (e.g. "@foo/bar/path/to/file"),
                // that means we had to go through a *sibling's* node_modules, not one we can access directly.
                // If some path to the file was in node_modules but another was not, this likely indicates that
                // we have a monorepo structure with symlinks. In this case, the non-node_modules path is
                // probably the realpath, e.g. "../bar/path/to/file", but a relative path to another package
                // in a monorepo is probably not portable. So, the module specifier we actually go with will be
                // the relative path through node_modules, so that the declaration emitter can produce a
                // portability error. (See declarationEmitReexportedSymlinkReference3)
                relativeSpecifiers = (List<string>)Core.Append(relativeSpecifiers, local)!;
            }
        }

        return pathsSpecifiers is { Count: > 0 } ? PathsResult("paths", pathsSpecifiers)
            : redirectPathsSpecifiers is { Count: > 0 } ? PathsResult("redirect", redirectPathsSpecifiers)
            : nodeModulesSpecifiers is { Count: > 0 } ? PathsResult("node_modules", nodeModulesSpecifiers)
            : PathsResult("relative", relativeSpecifiers ?? []);

        static ModuleSpecifierResult PathsResult(string kind, List<string> specifiers)
            => new() { Kind = kind, ModuleSpecifiers = specifiers, ComputedWithoutCache = true };
    }

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:558-560 — <c>isExcludedByRegex</c>.</summary>
    private static bool IsExcludedByRegex(string moduleSpecifier, List<string>? excludeRegexes)
        => Core.Some(excludeRegexes, (pattern, _) => s_stringToRegex(pattern)?.IsMatch(moduleSpecifier) == true);

    // ---------------------------------------------------------------------------------------------
    // Info / the local specifier (moduleSpecifiers.ts:562-711)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:569-579 — <c>getInfo</c>.</summary>
    private static ModuleSpecifierInfo GetInfo(string importingSourceFileName, IModuleSpecifierResolutionHost host)
    {
        importingSourceFileName = Paths.GetNormalizedAbsolutePath(importingSourceFileName, host.GetCurrentDirectory());
        var getCanonicalFileName = Core.CreateGetCanonicalFileName(host.UseCaseSensitiveFileNames());
        var sourceDirectory = Paths.GetDirectoryPath(importingSourceFileName);
        return new ModuleSpecifierInfo
        {
            GetCanonicalFileName = getCanonicalFileName,
            ImportingSourceFileName = importingSourceFileName,
            SourceDirectory = sourceDirectory,
            CanonicalSourceDirectory = getCanonicalFileName(sourceDirectory),
        };
    }

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:583-677 — <c>getLocalModuleSpecifier</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares three overloads that differ only in whether <c>pathsOnly</c> is passed and whether that
    /// makes the answer total; the port declares one member whose answer is always <c>string?</c>, and the two call
    /// sites that pass no <c>pathsOnly</c> dereference it (the reference's total overloads).
    /// </remarks>
    private static string? GetLocalModuleSpecifier(
        string moduleFileName,
        ModuleSpecifierInfo info,
        ICompilerOptions compilerOptions,
        IModuleSpecifierResolutionHost host,
        ModuleKind? importMode,
        ModuleSpecifierPreferences preferences,
        bool? pathsOnly = null)
    {
        var baseUrl = compilerOptions.BaseUrl;
        var paths = compilerOptions.Paths;
        var rootDirs = compilerOptions.RootDirs;
        if (pathsOnly == true && paths is null)
        {
            return null;
        }

        var sourceDirectory = info.SourceDirectory;
        var canonicalSourceDirectory = info.CanonicalSourceDirectory;
        var getCanonicalFileName = info.GetCanonicalFileName;
        var allowedEndings = preferences.GetAllowedEndingsInPreferredOrder(importMode);
        var relativePath = rootDirs is not null && TryGetModuleNameFromRootDirs(rootDirs, moduleFileName, sourceDirectory, getCanonicalFileName, allowedEndings, compilerOptions)
            is { } fromRootDirs
            ? fromRootDirs
            : ProcessEnding(
                Paths.EnsurePathIsNonModuleName(Paths.GetRelativePathFromDirectory(sourceDirectory, moduleFileName, getCanonicalFileName)),
                allowedEndings,
                compilerOptions);
        if (baseUrl is null && paths is null && !Utilities.GetResolvePackageJsonImports(compilerOptions)
            || preferences.RelativePreference == RelativePreference.Relative)
        {
            return pathsOnly == true ? null : relativePath;
        }

        var pathsBasePath = Utilities.GetPathsBasePath(compilerOptions, host);
        var baseDirectory = Paths.GetNormalizedAbsolutePath(
            Js.IsTruthy(pathsBasePath) ? pathsBasePath : baseUrl!,
            host.GetCurrentDirectory());
        var relativeToBaseUrl = GetRelativePathIfInSameVolume(moduleFileName, baseDirectory, getCanonicalFileName);
        if (relativeToBaseUrl is null)
        {
            return pathsOnly == true ? null : relativePath;
        }

        var fromPackageJsonImports = pathsOnly == true
            ? null
            : TryGetModuleNameFromPackageJsonImports(
                moduleFileName,
                sourceDirectory,
                compilerOptions,
                host,
                importMode,
                PrefersTsExtension(allowedEndings));

        var fromPaths = pathsOnly == true || fromPackageJsonImports is null
            ? paths is not null
                ? TryGetModuleNameFromPaths(relativeToBaseUrl, paths, allowedEndings, baseDirectory, getCanonicalFileName, host, compilerOptions)
                : null
            : null;
        if (pathsOnly == true)
        {
            return fromPaths;
        }

        var maybeNonRelative = fromPackageJsonImports
            ?? (fromPaths is null && baseUrl is not null ? ProcessEnding(relativeToBaseUrl, allowedEndings, compilerOptions) : fromPaths);
        if (maybeNonRelative is null)
        {
            return relativePath;
        }

        var relativeIsExcluded = IsExcludedByRegex(relativePath, preferences.ExcludeRegexes);
        var nonRelativeIsExcluded = IsExcludedByRegex(maybeNonRelative, preferences.ExcludeRegexes);
        if (!relativeIsExcluded && nonRelativeIsExcluded)
        {
            return relativePath;
        }

        if (relativeIsExcluded && !nonRelativeIsExcluded)
        {
            return maybeNonRelative;
        }

        if (preferences.RelativePreference == RelativePreference.NonRelative && !Paths.PathIsRelative(maybeNonRelative))
        {
            return maybeNonRelative;
        }

        if (preferences.RelativePreference == RelativePreference.ExternalNonRelative && !Paths.PathIsRelative(maybeNonRelative))
        {
            var projectDirectory = compilerOptions.ConfigFilePath is not null
                ? Paths.ToPath(Paths.GetDirectoryPath(compilerOptions.ConfigFilePath), host.GetCurrentDirectory(), info.GetCanonicalFileName)
                : info.GetCanonicalFileName(host.GetCurrentDirectory());
            var modulePath = Paths.ToPath(moduleFileName, projectDirectory, getCanonicalFileName);
            var sourceIsInternal = Core.StartsWith(canonicalSourceDirectory, projectDirectory);
            var targetIsInternal = Core.StartsWith(modulePath, projectDirectory);
            if (sourceIsInternal && !targetIsInternal || !sourceIsInternal && targetIsInternal)
            {
                // 1. The import path crosses the boundary of the tsconfig.json-containing directory.
                //
                //      src/
                //        tsconfig.json
                //        index.ts -------
                //      lib/              | (path crosses tsconfig.json)
                //        imported.ts <---
                //
                return maybeNonRelative;
            }

            var nearestTargetPackageJson = GetNearestAncestorDirectoryWithPackageJson(host, Paths.GetDirectoryPath(modulePath));
            var nearestSourcePackageJson = GetNearestAncestorDirectoryWithPackageJson(host, sourceDirectory);
            var ignoreCase = !host.UseCaseSensitiveFileNames();
            if (!PackageJsonPathsAreEqual(nearestTargetPackageJson, nearestSourcePackageJson, ignoreCase))
            {
                // 2. The importing and imported files are part of different packages.
                //
                //      packages/a/
                //        package.json
                //        index.ts --------
                //      packages/b/        | (path crosses package.json)
                //        package.json     |
                //        component.ts <---
                //
                return maybeNonRelative;
            }

            return relativePath;
        }

        // Prefer a relative import over a baseUrl import if it has fewer components.
        return IsPathRelativeToParent(maybeNonRelative) || CountPathComponents(relativePath) < CountPathComponents(maybeNonRelative)
            ? relativePath
            : maybeNonRelative;
    }

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:679-684 — <c>packageJsonPathsAreEqual</c>.</summary>
    private static bool PackageJsonPathsAreEqual(string? a, string? b, bool ignoreCase)
    {
        if (a == b) return true;
        if (a is null || b is null) return false;
        return Paths.ComparePaths(a, b, ignoreCase) == Comparison.EqualTo;
    }

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:686-692 — <c>countPathComponents</c>.</summary>
    public static int CountPathComponents(string path)
    {
        var count = 0;
        for (var i = Core.StartsWith(path, "./") ? 2 : 0; i < path.Length; i++)
        {
            if (path[i] == CharacterCodes.Slash) count++;
        }

        return count;
    }

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:694-696 — <c>comparePathsByRedirectAndNumberOfDirectorySeparators</c>.</summary>
    private static Comparison ComparePathsByRedirectAndNumberOfDirectorySeparators(IModulePath a, IModulePath b)
    {
        var byRedirect = Core.CompareBooleans(b.IsRedirect, a.IsRedirect);
        return byRedirect != Comparison.EqualTo ? byRedirect : CompareNumberOfDirectorySeparators(a.Path!, b.Path!);
    }

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:698-711 — <c>getNearestAncestorDirectoryWithPackageJson</c>.</summary>
    private static string? GetNearestAncestorDirectoryWithPackageJson(IModuleSpecifierResolutionHost host, string fileName)
    {
        if (TryGetHostNearestAncestorDirectoryWithPackageJson(host, fileName, out var fromHost))
        {
            return fromHost;
        }

        return ModuleNameResolver.ForEachAncestorDirectoryStoppingAtGlobalCache(
            host,
            fileName,
            directory => host.FileExists(Paths.CombinePaths(directory, "package.json")) ? directory : null);
    }

    // ---------------------------------------------------------------------------------------------
    // Symlinks and module paths (moduleSpecifiers.ts:713-877)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:713-771 — <c>forEachFileNameOfModule</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: TS's callback answers <c>false</c> to stop the ancestor walk with no result; the port's
    /// <see cref="ModuleNameResolver.ForEachAncestorDirectoryStoppingAtGlobalCache{T}"/> stops on a non-null answer,
    /// so the "stop" arm continues instead. The two are the same walk here: the stopping test is
    /// <c>startsWithDirectory(importingFileName, realPathDirectory)</c>, which holds for every ancestor of a directory
    /// that satisfies it (each ancestor is a prefix of the directory), so the comments' claim — "each ancestor
    /// directory will also hit this condition" — makes the remaining ancestors answer the same way.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>T</c> is unconstrained; the port constrains it to a reference type because the walk needs a
    /// null "no answer" (which is also what the reference's <c>undefined</c> is).
    /// </para>
    /// </remarks>
    public static T? ForEachFileNameOfModule<T>(
        string importingFileName,
        string importedFileName,
        IModuleSpecifierResolutionHost host,
        bool preferSymlinks,
        Func<string, bool, T?> cb)
        where T : class
    {
        var getCanonicalFileName = Utilities.HostGetCanonicalFileName(host);
        var cwd = host.GetCurrentDirectory();
        var referenceRedirect = host.IsSourceOfProjectReferenceRedirect(importedFileName)
            ? host.GetRedirectFromSourceFile(importedFileName)?.OutputDts
            : null;
        var importedPath = Paths.ToPath(importedFileName, cwd, getCanonicalFileName);
        var redirects = host.RedirectTargetsMap?.GetValueOrDefault(importedPath);
        var importedFileNames = new List<string>();
        if (referenceRedirect is not null) importedFileNames.Add(referenceRedirect);
        importedFileNames.Add(importedFileName);
        if (redirects is not null) importedFileNames.AddRange(redirects);
        var targets = Core.Map(importedFileNames, (f, _) => Paths.GetNormalizedAbsolutePath(f, cwd))!;
        var shouldFilterIgnoredPaths = !Core.Every(targets, (t, _) => Utilities.ContainsIgnoredPath(t));

        if (!preferSymlinks)
        {
            // Symlinks inside ignored paths are already filtered out of the symlink cache,
            // so we only need to remove them from the realpath filenames.
            var result = Core.FirstDefined<string, T>(
                targets,
                (p, _) => !(shouldFilterIgnoredPaths && Utilities.ContainsIgnoredPath(p)) ? cb(p, referenceRedirect == p) : null);
            if (result is not null) return result;
        }

        var symlinkedDirectories = ((SymlinkCache?)host.GetSymlinkCache())?.GetSymlinkedDirectoriesByRealpath();
        var fullImportedFileName = Paths.GetNormalizedAbsolutePath(importedFileName, cwd);
        if (symlinkedDirectories is not null)
        {
            var result = ModuleNameResolver.ForEachAncestorDirectoryStoppingAtGlobalCache(
                host,
                Paths.GetDirectoryPath(fullImportedFileName),
                realPathDirectory =>
                {
                    var symlinkDirectories = symlinkedDirectories.Get(
                        Paths.EnsureTrailingDirectorySeparator(Paths.ToPath(realPathDirectory, cwd, getCanonicalFileName)));
                    if (symlinkDirectories is null) return null; // Continue to ancestor directory

                    // Don't want to a package to globally import from itself (importNameCodeFix_symlink_own_package.ts)
                    if (Paths.StartsWithDirectory(importingFileName, realPathDirectory, getCanonicalFileName))
                    {
                        return null; // (reference: `false` — stop the search; see the PORT-NOTE on this member)
                    }

                    return Core.FirstDefined<string, T>(
                        targets,
                        (target, _) =>
                        {
                            if (!Paths.StartsWithDirectory(target, realPathDirectory, getCanonicalFileName))
                            {
                                return null;
                            }

                            var relative = Paths.GetRelativePathFromDirectory(realPathDirectory, target, getCanonicalFileName);
                            foreach (var symlinkDirectory in symlinkDirectories)
                            {
                                var option = Paths.ResolvePath(symlinkDirectory, relative);
                                var optionResult = cb(option, target == referenceRedirect);
                                shouldFilterIgnoredPaths = true; // We found a non-ignored path in symlinks, so we can reject ignored-path realpaths
                                if (optionResult is not null) return optionResult;
                            }

                            return null;
                        });
                });
            if (result is not null) return result;
        }

        return preferSymlinks
            ? Core.FirstDefined<string, T>(
                targets,
                (p, _) => shouldFilterIgnoredPaths && Utilities.ContainsIgnoredPath(p) ? null : cb(p, p == referenceRedirect))
            : null;
    }

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:774-794 — <c>getAllModulePaths</c>.</summary>
    /// <remarks>Looks for existing imports that use symlinks to this module. Symlinks will be returned first so they
    /// are preferred over the real path.</remarks>
    private static List<IModulePath> GetAllModulePaths(
        ModuleSpecifierInfo info,
        string importedFileName,
        IModuleSpecifierResolutionHost host,
        IUserPreferences preferences,
        ICompilerOptions compilerOptions,
        ModuleSpecifierOptions? options)
    {
        var importingFilePath = Paths.ToPath(info.ImportingSourceFileName, host.GetCurrentDirectory(), Utilities.HostGetCanonicalFileName(host));
        var importedFilePath = Paths.ToPath(importedFileName, host.GetCurrentDirectory(), Utilities.HostGetCanonicalFileName(host));
        var cache = GetModuleSpecifierCache(host);
        if (cache is not null)
        {
            var cached = (IResolvedModuleSpecifierInfo?)cache.Get(importingFilePath, importedFilePath, preferences, options);
            if (cached?.ModulePaths is not null) return cached.ModulePaths;
        }

        var modulePaths = GetAllModulePathsWorker(info, importedFileName, host, compilerOptions, options);
        cache?.SetModulePaths(importingFilePath, importedFilePath, preferences, options, modulePaths);
        return modulePaths;
    }

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:796 — <c>runtimeDependencyFields</c>.</summary>
    private static readonly List<string> RuntimeDependencyFields = ["dependencies", "peerDependencies", "optionalDependencies"];

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:798-807 — <c>getAllRuntimeDependencies</c>.</summary>
    private static IList<string>? GetAllRuntimeDependencies(JsObject packageJson)
    {
        IList<string>? result = null;
        foreach (var field in RuntimeDependencyFields)
        {
            if (packageJson.Get(field) is JsObject deps)
            {
                result = Core.Concatenate(result, deps.Keys);
            }
        }

        return result;
    }

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:809-877 — <c>getAllModulePathsWorker</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE (the resolution-cache warm-up): the reference guards the block on
    /// <c>host.getModuleResolutionCache?.()</c> and asserts the host is also a <c>ModuleResolutionHost</c>
    /// (<c>Debug.type&lt;ModuleResolutionHost&gt;(host)</c>, <c>:812</c>). The port's two host roles are separate
    /// nominal interfaces, so the same object is only asked for the resolution members when it really carries them —
    /// the runtime test below. The declaration emitter's host does not: TS's
    /// <c>createBasicNodeBuilderModuleSpecifierResolutionHost</c> literal has no <c>getModuleResolutionCache</c>
    /// either (checker.ts:54260-54278), so that host skips this block in both. The guard the reference puts on
    /// <c>host.readFile</c> is dropped (the generated host declares it).
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>allFileNames.forEach</c> deletes from the map it walks, which JS allows; the port walks a
    /// snapshot of the entries, which visits the same pairs in the same order.
    /// </para>
    /// </remarks>
    private static List<IModulePath> GetAllModulePathsWorker(
        ModuleSpecifierInfo info,
        string importedFileName,
        IModuleSpecifierResolutionHost host,
        ICompilerOptions compilerOptions,
        ModuleSpecifierOptions? options)
    {
        var cache = (ModuleResolutionCache?)host.GetModuleResolutionCache();
        var links = (SymlinkCache?)host.GetSymlinkCache();
        if (cache is not null
            && links is not null
            && host is IModuleResolutionHost resolutionHost
            && !ModuleNameResolver.PathContainsNodeModules(info.ImportingSourceFileName))
        {
            // Cache resolutions for all `dependencies` of the `package.json` context of the input file.
            // This should populate all the relevant symlinks in the symlink cache, and most, if not all, of these
            // resolutions should get (re)used.
            var state = ModuleNameResolver.GetTemporaryModuleResolutionState(cache.GetPackageJsonInfoCache(), resolutionHost, compilerOptions);
            var packageJson = ModuleNameResolver.GetPackageScopeForPath(Paths.GetDirectoryPath(info.ImportingSourceFileName), state);
            if (packageJson is not null)
            {
                var toResolve = GetAllRuntimeDependencies(packageJson.Contents.PackageJsonContent);
                foreach (var depName in toResolve ?? new List<string>())
                {
                    var resolved = ModuleNameResolver.ResolveModuleName(
                        depName,
                        Paths.CombinePaths(packageJson.PackageDirectory, "package.json"),
                        compilerOptions,
                        resolutionHost,
                        cache,
                        /*redirectedReference*/ null,
                        options?.OverrideMode);
                    links.SetSymlinksFromResolution(resolved.ResolvedModule);
                }
            }
        }

        var allFileNames = new Dictionary<string, (string Path, bool IsRedirect, bool IsInNodeModules)>(StringComparer.Ordinal);
        var importedFileFromNodeModules = false;
        ForEachFileNameOfModule<object>(
            info.ImportingSourceFileName,
            importedFileName,
            host,
            /*preferSymlinks*/ true,
            (path, isRedirect) =>
            {
                var isInNodeModules = ModuleNameResolver.PathContainsNodeModules(path);
                allFileNames[path] = (info.GetCanonicalFileName(path), isRedirect, isInNodeModules);
                importedFileFromNodeModules = importedFileFromNodeModules || isInNodeModules;
                // don't return value, so we collect everything
                return null;
            });

        // Sort by paths closest to importing file Name directory
        var sortedPaths = new List<IModulePath>();
        var directory = info.CanonicalSourceDirectory;
        while (allFileNames.Count != 0)
        {
            var directoryStart = Paths.EnsureTrailingDirectorySeparator(directory);
            List<IModulePath>? pathsInDirectory = null;
            foreach (var (fileName, value) in allFileNames.ToList())
            {
                if (Core.StartsWith(value.Path, directoryStart))
                {
                    (pathsInDirectory ??= []).Add(new ModulePath
                    {
                        Path = fileName,
                        IsRedirect = value.IsRedirect,
                        IsInNodeModules = value.IsInNodeModules,
                    });
                    allFileNames.Remove(fileName);
                }
            }

            if (pathsInDirectory is not null)
            {
                if (pathsInDirectory.Count > 1)
                {
                    Core.StableSort(pathsInDirectory, (a, b) => ComparePathsByRedirectAndNumberOfDirectorySeparators(a, b));
                }

                sortedPaths.AddRange(pathsInDirectory);
            }

            var newDirectory = Paths.GetDirectoryPath(directory);
            if (newDirectory == directory) break;
            directory = newDirectory;
        }

        if (allFileNames.Count != 0)
        {
            var remainingPaths = Core.ArrayFrom(allFileNames, kv => (IModulePath)new ModulePath
            {
                Path = kv.Key,
                IsRedirect = kv.Value.IsRedirect,
                IsInNodeModules = kv.Value.IsInNodeModules,
            });
            if (remainingPaths.Count > 1)
            {
                Core.StableSort(remainingPaths, (a, b) => ComparePathsByRedirectAndNumberOfDirectorySeparators(a, b));
            }

            sortedPaths.AddRange(remainingPaths);
        }

        return sortedPaths;
    }

    // ---------------------------------------------------------------------------------------------
    // The ambient-module / paths / exports specifiers (moduleSpecifiers.ts:879-1167)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:879-924 — <c>tryGetModuleNameFromAmbientModule</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>getTopNamespace</c> is declared inside the <c>mapDefined</c> callback; the port
    /// declares it as a member — the same function, which closes over nothing.
    /// </remarks>
    private static string? TryGetModuleNameFromAmbientModule(ISymbol moduleSymbol, ITypeChecker checker)
    {
        var decl = Core.Find(
            moduleSymbol.Declarations,
            (d, _) => IsNonGlobalAmbientModule((Node)d)
                && (!Utilities.IsExternalModuleAugmentation((Node)d)
                    || !Utilities.IsExternalModuleNameRelative(Utilities.GetTextOfIdentifierOrLiteral(((ModuleDeclaration)d).Name!))));
        if (decl is not null)
        {
            return ((StringLiteral)((ModuleDeclaration)decl).Name!).Text;
        }

        // the module could be a namespace, which is export through "export=" from an ambient module.
        //
        //     declare module "m" {
        //         namespace ns {
        //             class c {}
        //         }
        //         export = ns;
        //     }
        //
        // `import {c} from "m";` is valid, in which case, `moduleSymbol` is "ns", but the module name should be "m".
        var ambientModuleDeclareCandidates = Core.MapDefined<IDeclaration, Node>(moduleSymbol.Declarations, (d, _) =>
        {
            if (!NodeTests.IsModuleDeclaration((Node)d)) return null;
            var topNamespace = GetTopNamespace((ModuleDeclaration)d);
            var ambient = topNamespace.Parent?.Parent;
            if (ambient is null
                || !NodeTests.IsModuleBlock(topNamespace.Parent!)
                || !Utilities.IsAmbientModule(ambient)
                || !NodeTests.IsSourceFile(ambient.Parent!))
            {
                return null;
            }

            var exportAssignment = (((ModuleDeclaration)ambient).Symbol?.Exports?.GetValueOrDefault(InternalSymbolName.ExportEquals.Value)?.ValueDeclaration as ExportAssignment)?.Expression;
            if (exportAssignment is null) return null;
            var exportSymbol = checker.GetSymbolAtLocation(exportAssignment);
            if (exportSymbol is null) return null;
            var originalExportSymbol = (exportSymbol.Flags & SymbolFlags.Alias) != 0 ? checker.GetAliasedSymbol(exportSymbol) : exportSymbol;
            return ReferenceEquals(originalExportSymbol, d.Symbol) ? ambient : null;
        });
        if (ambientModuleDeclareCandidates.Count > 0)
        {
            return ((StringLiteral)((ModuleDeclaration)ambientModuleDeclareCandidates[0]).Name!).Text;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:915-921 — <c>getTopNamespace</c>.</summary>
    private static ModuleDeclaration GetTopNamespace(ModuleDeclaration namespaceDeclaration)
    {
        while ((namespaceDeclaration.Flags & NodeFlags.NestedNamespace) != 0)
        {
            namespaceDeclaration = (ModuleDeclaration)namespaceDeclaration.Parent!;
        }

        return namespaceDeclaration;
    }

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:926-1012 — <c>tryGetModuleNameFromPaths</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>validateEnding</c> is a local function declared after the loop; the port declares
    /// it as a member, taking what it read from the enclosing scope.
    /// <para>
    /// PORT-NOTE: the candidate list is TS's <c>{ ending, value }[]</c> as a named tuple list; the extensionless-extras
    /// candidate is <c>ending: undefined</c>.
    /// </para>
    /// </remarks>
    private static string? TryGetModuleNameFromPaths(
        string relativeToBaseUrl,
        Dictionary<string, List<string>> paths,
        List<ModuleSpecifierEnding> allowedEndings,
        string baseDirectory,
        Core.GetCanonicalFileName getCanonicalFileName,
        IModuleSpecifierResolutionHost host,
        ICompilerOptions compilerOptions)
    {
        foreach (var key in Core.GetOwnKeys(paths))
        {
            foreach (var patternText in paths[key])
            {
                var normalized = Paths.NormalizePath(patternText);
                var pattern = GetRelativePathIfInSameVolume(normalized, baseDirectory, getCanonicalFileName) ?? normalized;
                var indexOfStar = pattern.IndexOf('*');
                // In module resolution, if `pattern` itself has an extension, a file with that extension is looked up
                // directly, meaning a '.ts' or '.d.ts' extension is allowed to resolve. This is distinct from the case
                // where a '*' substitution causes a module specifier to have an extension, i.e. the extension comes
                // from the module specifier in a JS/TS file and matches the '*'. For example:
                //
                // Module Specifier      | Path Mapping (key: [pattern]) | Interpolation       | Resolution Action
                // ---------------------->------------------------------->--------------------->--------…
                // import "@app/foo"    -> "@app/*": ["./src/app/*.ts"] -> "./src/app/foo.ts" -> tryFile("./src/app/foo.ts") || [continue resolution algorithm]
                // import "@app/foo.ts" -> "@app/*": ["./src/app/*"]    -> "./src/app/foo.ts" -> [continue resolution algorithm]
                //
                // (https://github.com/microsoft/TypeScript/blob/ad4ded80e1d58f0bf36ac16bea71bc10d9f09895/src/compiler/moduleNameResolver.ts#L2509-L2516)
                //
                // The interpolation produced by both scenarios is identical, but only in the former, where the extension
                // is encoded in the path mapping rather than in the module specifier, will we prioritize a file lookup
                // on the interpolation result. (In fact, currently, the latter scenario will necessarily fail since no
                // resolution mode recognizes '.ts' as a valid extension for a module specifier.)
                //
                // Here, this means we need to be careful about whether we generate a match from the target filename
                // (typically with a .ts extension) or the possible relative module specifiers representing that file:
                //
                // Filename            | Relative Module Specifier Candidates         | Path Mapping                 | Filename Result    | Module Specifier Results
                // --------------------<----------------------------------------------<------------------------------<-------------------||----------------------------
                // dist/haha.d.ts      <- dist/haha, dist/haha.js                     <- "@app/*": ["./dist/*.d.ts"] <- @app/haha        || (none)
                // dist/haha.d.ts      <- dist/haha, dist/haha.js                     <- "@app/*": ["./dist/*"]      <- (none)           || @app/haha, @app/haha.js
                // dist/foo/index.d.ts <- dist/foo, dist/foo/index, dist/foo/index.js <- "@app/*": ["./dist/*.d.ts"] <- @app/foo/index   || (none)
                // dist/foo/index.d.ts <- dist/foo, dist/foo/index, dist/foo/index.js <- "@app/*": ["./dist/*"]      <- (none)           || @app/foo, @app/foo/index, @app/foo/index.js
                // dist/wow.js.js      <- dist/wow.js, dist/wow.js.js                 <- "@app/*": ["./dist/*.js"]   <- @app/wow.js      || @app/wow, @app/wow.js
                //
                // The "Filename Result" can be generated only if `pattern` has an extension. Care must be taken that the
                // list of relative module specifiers to run the interpolation (a) is actually valid for the module
                // resolution mode, (b) takes into account the existence of other files (e.g. 'dist/wow.js' cannot refer
                // to 'dist/wow.js.js' if 'dist/wow.js' exists) and (c) that they are ordered by preference. The last row
                // shows that the filename result and module specifier results are not mutually exclusive. Note that the
                // filename result is a higher priority in module resolution, but as long criteria (b) above is met, I
                // don't think its result needs to be the highest priority result in module specifier generation. I have
                // included it last, as it's difficult to tell exactly where it should be sorted among the others for a
                // particular value of `importModuleSpecifierEnding`.
                var candidates = Core.Map(allowedEndings, (ending, _) => (Ending: (ModuleSpecifierEnding?)ending, Value: ProcessEnding(relativeToBaseUrl, [ending], compilerOptions)))!;
                if (Utilities.TryGetExtensionFromPath(pattern) is not null)
                {
                    candidates.Add((Ending: null, Value: relativeToBaseUrl));
                }

                if (indexOfStar != -1)
                {
                    var prefix = pattern[..indexOfStar];
                    var suffix = pattern[(indexOfStar + 1)..];
                    foreach (var (ending, value) in candidates)
                    {
                        if (value.Length >= prefix.Length + suffix.Length
                            && Core.StartsWith(value, prefix)
                            && Core.EndsWith(value, suffix)
                            && ValidateEnding(ending, value, relativeToBaseUrl, compilerOptions, host))
                        {
                            var matchedStar = value[prefix.Length..(value.Length - suffix.Length)];
                            if (!Paths.PathIsRelative(matchedStar))
                            {
                                return Utilities.ReplaceFirstStar(key, matchedStar);
                            }
                        }
                    }
                }
                else if (
                    Core.Some(candidates, (c, _) => c.Ending != ModuleSpecifierEnding.Minimal && pattern == c.Value)
                    || Core.Some(
                        candidates,
                        (c, _) => c.Ending == ModuleSpecifierEnding.Minimal
                            && pattern == c.Value
                            && ValidateEnding(c.Ending, c.Value, relativeToBaseUrl, compilerOptions, host)))
                {
                    return key;
                }
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:999-1011 — <c>validateEnding</c>.</summary>
    /// <remarks>
    /// Optimization: <c>removeExtensionAndIndexPostFix</c> can query the file system (a good bit) if `ending` is
    /// `Minimal`, the basename is 'index', and a `host` is provided. To avoid that until it's unavoidable, the
    /// reference ran <c>processEnding</c> with no `host` when it built the candidates. Only here, after it has checked
    /// that the minimal ending is indeed a match (via the length and prefix/suffix checks / `some` calls), does it
    /// check that the host-validated result is consistent with the answer it got before. If it's not, it falls back to
    /// the `ModuleSpecifierEnding.Index` result, which should already be in the list of candidates if `Minimal` was.
    /// (The assumption is that every module resolution mode that supports dropping extensions also supports dropping
    /// `/index`.)
    /// </remarks>
    private static bool ValidateEnding(
        ModuleSpecifierEnding? ending,
        string value,
        string relativeToBaseUrl,
        ICompilerOptions compilerOptions,
        IModuleSpecifierResolutionHost host)
        => ending != ModuleSpecifierEnding.Minimal
            || value == ProcessEnding(relativeToBaseUrl, [ending!.Value], compilerOptions, host);

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:1020-1120 — <c>tryGetModuleNameFromExportsOrImports</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: TS's <c>exports: unknown</c> is a JSON value — a string, an array, or a conditional-mapping object;
    /// the port tests the shapes its JSON model produces (<see cref="string"/>, <see cref="List{T}"/> of
    /// <see cref="object"/>, <see cref="JsObject"/>), which is the discrimination the reference's <c>typeof</c> and
    /// <c>Array.isArray</c> tests make.
    /// </para>
    /// <para>
    /// PORT-NOTE: the reference's one-member <c>{ moduleFileToTry }</c> answer is that string.
    /// </para>
    /// </remarks>
    private static string? TryGetModuleNameFromExportsOrImports(
        ICompilerOptions options,
        IModuleSpecifierResolutionHost host,
        string targetFilePath,
        string packageDirectory,
        string packageName,
        object? exports,
        List<string> conditions,
        MatchingMode mode,
        bool isImports,
        bool preferTsExtension)
    {
        if (exports is string exportsString)
        {
            var ignoreCase = !Utilities.HostUsesCaseSensitiveFileNames(host);
            var outputFile = isImports ? Emitter.GetOutputJSFileNameWorker(targetFilePath, options, ignoreCase, () => host.GetCommonSourceDirectory()) : null;
            var declarationFile = isImports ? Emitter.GetOutputDeclarationFileNameWorker(targetFilePath, options, ignoreCase, () => host.GetCommonSourceDirectory()) : null;

            var pathOrPattern = Paths.GetNormalizedAbsolutePath(Paths.CombinePaths(packageDirectory, exportsString), /*currentDirectory*/ null);
            var extensionSwappedTarget = Utilities.HasTSFileExtension(targetFilePath)
                ? Utilities.RemoveFileExtension(targetFilePath) + TryGetJSExtensionForFile(targetFilePath, options)!
                : null;
            var canTryTsExtension = preferTsExtension && HasImplementationTSFileExtension(targetFilePath);

            switch (mode)
            {
                case MatchingMode.Exact:
                    if (extensionSwappedTarget is not null && Paths.ComparePaths(extensionSwappedTarget, pathOrPattern, ignoreCase) == Comparison.EqualTo
                        || Paths.ComparePaths(targetFilePath, pathOrPattern, ignoreCase) == Comparison.EqualTo
                        || outputFile is not null && Paths.ComparePaths(outputFile, pathOrPattern, ignoreCase) == Comparison.EqualTo
                        || declarationFile is not null && Paths.ComparePaths(declarationFile, pathOrPattern, ignoreCase) == Comparison.EqualTo)
                    {
                        return packageName;
                    }
                    break;
                case MatchingMode.Directory:
                    if (canTryTsExtension && Paths.ContainsPath(pathOrPattern, targetFilePath, ignoreCase))
                    {
                        var fragment = Paths.GetRelativePathFromDirectory(pathOrPattern, targetFilePath, /*ignoreCase*/ false);
                        return Paths.GetNormalizedAbsolutePath(Paths.CombinePaths(Paths.CombinePaths(packageName, exportsString), fragment), /*currentDirectory*/ null);
                    }
                    if (extensionSwappedTarget is not null && Paths.ContainsPath(pathOrPattern, extensionSwappedTarget, ignoreCase))
                    {
                        var fragment = Paths.GetRelativePathFromDirectory(pathOrPattern, extensionSwappedTarget, /*ignoreCase*/ false);
                        return Paths.GetNormalizedAbsolutePath(Paths.CombinePaths(Paths.CombinePaths(packageName, exportsString), fragment), /*currentDirectory*/ null);
                    }
                    if (!canTryTsExtension && Paths.ContainsPath(pathOrPattern, targetFilePath, ignoreCase))
                    {
                        var fragment = Paths.GetRelativePathFromDirectory(pathOrPattern, targetFilePath, /*ignoreCase*/ false);
                        return Paths.GetNormalizedAbsolutePath(Paths.CombinePaths(Paths.CombinePaths(packageName, exportsString), fragment), /*currentDirectory*/ null);
                    }
                    if (outputFile is not null && Paths.ContainsPath(pathOrPattern, outputFile, ignoreCase))
                    {
                        var fragment = Paths.GetRelativePathFromDirectory(pathOrPattern, outputFile, /*ignoreCase*/ false);
                        return Paths.CombinePaths(packageName, fragment);
                    }
                    if (declarationFile is not null && Paths.ContainsPath(pathOrPattern, declarationFile, ignoreCase))
                    {
                        var fragment = Paths.ChangeFullExtension(
                            Paths.GetRelativePathFromDirectory(pathOrPattern, declarationFile, /*ignoreCase*/ false),
                            GetJSExtensionForFile(declarationFile, options));
                        return Paths.CombinePaths(packageName, fragment);
                    }
                    break;
                case MatchingMode.Pattern:
                    var starPos = pathOrPattern.IndexOf('*');
                    var leadingSlice = pathOrPattern[..starPos];
                    var trailingSlice = pathOrPattern[(starPos + 1)..];
                    if (canTryTsExtension && Core.StartsWith(targetFilePath, leadingSlice, ignoreCase) && Core.EndsWith(targetFilePath, trailingSlice, ignoreCase))
                    {
                        var starReplacement = targetFilePath[leadingSlice.Length..(targetFilePath.Length - trailingSlice.Length)];
                        return Utilities.ReplaceFirstStar(packageName, starReplacement);
                    }
                    if (extensionSwappedTarget is not null && Core.StartsWith(extensionSwappedTarget, leadingSlice, ignoreCase) && Core.EndsWith(extensionSwappedTarget, trailingSlice, ignoreCase))
                    {
                        var starReplacement = extensionSwappedTarget[leadingSlice.Length..(extensionSwappedTarget.Length - trailingSlice.Length)];
                        return Utilities.ReplaceFirstStar(packageName, starReplacement);
                    }
                    if (!canTryTsExtension && Core.StartsWith(targetFilePath, leadingSlice, ignoreCase) && Core.EndsWith(targetFilePath, trailingSlice, ignoreCase))
                    {
                        var starReplacement = targetFilePath[leadingSlice.Length..(targetFilePath.Length - trailingSlice.Length)];
                        return Utilities.ReplaceFirstStar(packageName, starReplacement);
                    }
                    if (outputFile is not null && Core.StartsWith(outputFile, leadingSlice, ignoreCase) && Core.EndsWith(outputFile, trailingSlice, ignoreCase))
                    {
                        var starReplacement = outputFile[leadingSlice.Length..(outputFile.Length - trailingSlice.Length)];
                        return Utilities.ReplaceFirstStar(packageName, starReplacement);
                    }
                    if (declarationFile is not null && Core.StartsWith(declarationFile, leadingSlice, ignoreCase) && Core.EndsWith(declarationFile, trailingSlice, ignoreCase))
                    {
                        var starReplacement = declarationFile[leadingSlice.Length..(declarationFile.Length - trailingSlice.Length)];
                        var substituted = Utilities.ReplaceFirstStar(packageName, starReplacement);
                        var jsExtension = TryGetJSExtensionForFile(declarationFile, options);
                        return jsExtension is not null ? Paths.ChangeFullExtension(substituted, jsExtension) : null;
                    }
                    break;
            }
        }
        else if (exports is List<object?> exportsArray)
        {
            return Core.FirstDefined<object?, string>(
                exportsArray,
                (e, _) => TryGetModuleNameFromExportsOrImports(options, host, targetFilePath, packageDirectory, packageName, e, conditions, mode, isImports, preferTsExtension));
        }
        else if (exports is JsObject exportsObject)
        {
            // conditional mapping
            foreach (var key in exportsObject.Keys)
            {
                if (key == "default" || conditions.IndexOf(key) >= 0 || ModuleNameResolver.IsApplicableVersionedTypesKey(conditions, key))
                {
                    var result = TryGetModuleNameFromExportsOrImports(options, host, targetFilePath, packageDirectory, packageName, exportsObject.Get(key), conditions, mode, isImports, preferTsExtension);
                    if (result is not null)
                    {
                        return result;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:1122-1138 — <c>tryGetModuleNameFromExports</c>.</summary>
    private static string? TryGetModuleNameFromExports(
        ICompilerOptions options,
        IModuleSpecifierResolutionHost host,
        string targetFilePath,
        string packageDirectory,
        string packageName,
        object? exports,
        List<string> conditions)
    {
        if (exports is JsObject exportsObject && ModuleNameResolver.AllKeysStartWithDot(exportsObject))
        {
            // sub-mappings
            // 3 cases:
            // * directory mappings (legacyish, key ends with / (technically allows index/extension resolution under cjs mode))
            // * pattern mappings (contains a *)
            // * exact mappings (no *, does not end with /)
            return Core.FirstDefined(exportsObject.Keys, (k, _) =>
            {
                var subPackageName = Paths.GetNormalizedAbsolutePath(Paths.CombinePaths(packageName, k), /*currentDirectory*/ null);
                var mode = Core.EndsWith(k, "/") ? MatchingMode.Directory
                    : k.Contains('*') ? MatchingMode.Pattern
                    : MatchingMode.Exact;
                return TryGetModuleNameFromExportsOrImports(options, host, targetFilePath, packageDirectory, subPackageName, exportsObject.Get(k), conditions, mode, /*isImports*/ false, /*preferTsExtension*/ false);
            });
        }

        return TryGetModuleNameFromExportsOrImports(options, host, targetFilePath, packageDirectory, packageName, exports, conditions, MatchingMode.Exact, /*isImports*/ false, /*preferTsExtension*/ false);
    }

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:1140-1167 — <c>tryGetModuleNameFromPackageJsonImports</c>.</summary>
    /// <remarks>PORT-NOTE: the reference returns <c>…?.moduleFileToTry</c> and also tests <c>host.readFile</c>; the port
    /// answers the string and drops that guard (the generated host declares the member — see the class remarks).</remarks>
    private static string? TryGetModuleNameFromPackageJsonImports(
        string moduleFileName,
        string sourceDirectory,
        ICompilerOptions options,
        IModuleSpecifierResolutionHost host,
        ModuleKind? importMode,
        bool preferTsExtension)
    {
        if (!Utilities.GetResolvePackageJsonImports(options))
        {
            return null;
        }

        var ancestorDirectoryWithPackageJson = GetNearestAncestorDirectoryWithPackageJson(host, sourceDirectory);
        if (ancestorDirectoryWithPackageJson is null)
        {
            return null;
        }

        var packageJsonPath = Paths.CombinePaths(ancestorDirectoryWithPackageJson, "package.json");
        var cachedPackageJson = ((IPackageJsonInfoCache?)host.GetPackageJsonInfoCache())?.GetPackageJsonInfo(packageJsonPath);
        if (ModuleNameResolver.IsMissingPackageJsonInfo(cachedPackageJson) || !host.FileExists(packageJsonPath))
        {
            return null;
        }

        var packageJsonContent = (cachedPackageJson as PackageJsonInfo)?.Contents.PackageJsonContent
            ?? (host.ReadFile(packageJsonPath) is { } packageJsonText && Utilities.TryParseJson(packageJsonText, out var parsed)
                ? parsed as JsObject
                : null);
        if (packageJsonContent?.Get("imports") is not JsObject imports)
        {
            return null;
        }

        var conditions = ModuleNameResolver.GetConditions(options, importMode);
        return Core.FirstDefined(imports.Keys, (k, _) =>
        {
            if (!Core.StartsWith(k, "#") || k == "#" || Core.StartsWith(k, "#/")) return null;
            var mode = Core.EndsWith(k, "/") ? MatchingMode.Directory
                : k.Contains('*') ? MatchingMode.Pattern
                : MatchingMode.Exact;
            return TryGetModuleNameFromExportsOrImports(options, host, moduleFileName, ancestorDirectoryWithPackageJson, k, imports.Get(k), conditions, mode, /*isImports*/ true, preferTsExtension);
        });
    }

    // ---------------------------------------------------------------------------------------------
    // The node_modules specifier (moduleSpecifiers.ts:1169-1343)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:1169-1184 — <c>tryGetModuleNameFromRootDirs</c>.</summary>
    /// <remarks>PORT-NOTE: the reference tests <c>normalizedTargetPaths === undefined</c> after
    /// <c>getPathsRelativeToRootDirs</c>, which <c>mapDefined</c> never answers (the same dead guard
    /// <c>moduleSpecifiers.ts:1360</c> has); the port's member answers a list, so only the empty result remains.</remarks>
    private static string? TryGetModuleNameFromRootDirs(
        List<string> rootDirs,
        string moduleFileName,
        string sourceDirectory,
        Core.GetCanonicalFileName getCanonicalFileName,
        List<ModuleSpecifierEnding> allowedEndings,
        ICompilerOptions compilerOptions)
    {
        var normalizedTargetPaths = GetPathsRelativeToRootDirs(moduleFileName, rootDirs, getCanonicalFileName);
        var normalizedSourcePaths = GetPathsRelativeToRootDirs(sourceDirectory, rootDirs, getCanonicalFileName);
        var relativePaths = Core.FlatMap<string, string>(
            normalizedSourcePaths,
            (sourcePath, _) => Core.Map(
                normalizedTargetPaths,
                (targetPath, _) => Paths.EnsurePathIsNonModuleName(Paths.GetRelativePathFromDirectory(sourcePath, targetPath, getCanonicalFileName))))!;
        var shortest = Core.Min<string>(relativePaths, (a, b) => CompareNumberOfDirectorySeparators(a, b));
        if (shortest is null)
        {
            return null;
        }

        return ProcessEnding(shortest, allowedEndings, compilerOptions);
    }

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:1224-1226 — the object literal <c>tryDirectoryWithPackageJson</c> answers.</summary>
    private sealed class DirectoryWithPackageJsonResult
    {
        /// <summary><c>moduleFileToTry</c>.</summary>
        public required string ModuleFileToTry { get; init; }

        /// <summary><c>packageRootPath</c>.</summary>
        public string? PackageRootPath { get; init; }

        /// <summary><c>blockedByExports</c>.</summary>
        public bool BlockedByExports { get; init; }

        /// <summary><c>verbatimFromExports</c>.</summary>
        public bool VerbatimFromExports { get; init; }
    }

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:1186-1343 — <c>tryGetModuleNameAsNodeModule</c> (with its nested <c>tryDirectoryWithPackageJson</c>).</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the reference destructures the <c>ModulePath</c> and the <c>Info</c> it is given (its <c>ModulePath</c>
    /// slot is <c>path</c>); the port reads the same two members off <see cref="IModulePath"/> and
    /// <see cref="ModuleSpecifierInfo"/>.
    /// </para>
    /// <para>
    /// PORT-NOTE: the guard the reference puts on <c>host.fileExists</c>/<c>host.readFile</c> is dropped (the generated
    /// host declares both — see the class remarks), and <c>host.getGlobalTypingsCacheLocation &amp;&amp; …</c> is the
    /// required member's read.
    /// </para>
    /// <para>
    /// PORT-NOTE: the nested worker is a member rather than a local function: it reads nine names off the frame it is
    /// declared in, and stating them is both shorter than the capture and free of the definite-assignment rules a
    /// local function's captured locals bring.
    /// </para>
    /// </remarks>
    private static string? TryGetModuleNameAsNodeModule(
        IModulePath modulePath,
        ModuleSpecifierInfo info,
        SourceFile importingSourceFile,
        IModuleSpecifierResolutionHost host,
        ICompilerOptions options,
        IUserPreferences userPreferences,
        bool? packageNameOnly,
        ModuleKind? overrideMode)
    {
        var path = modulePath.Path!;
        var isRedirect = modulePath.IsRedirect;
        var getCanonicalFileName = info.GetCanonicalFileName;
        var canonicalSourceDirectory = info.CanonicalSourceDirectory;
        var parts = GetNodeModulePathParts(path);
        if (parts is null)
        {
            return null;
        }

        // Simplify the full file path to something that can be resolved by Node.

        var preferences = GetModuleSpecifierPreferences(userPreferences, host, options, importingSourceFile);
        var allowedEndings = preferences.GetAllowedEndingsInPreferredOrder(null);
        var moduleSpecifier = path;
        var isPackageRootPath = false;
        if (packageNameOnly != true)
        {
            var packageRootIndex = parts.PackageRootIndex;
            string? moduleFileName = null;
            while (true)
            {
                // If the module could be imported by a directory name, use that directory's name
                var directoryResult = TryDirectoryWithPackageJson(packageRootIndex, path, parts, importingSourceFile, host, options, overrideMode, allowedEndings, getCanonicalFileName);
                if (Utilities.GetEmitModuleResolutionKind(options) != ModuleResolutionKind.Classic)
                {
                    if (directoryResult.BlockedByExports)
                    {
                        return null; // File is under this package.json, but is not publicly exported - there's no way to name it via `node_modules` resolution
                    }

                    if (directoryResult.VerbatimFromExports)
                    {
                        return directoryResult.ModuleFileToTry;
                    }
                }

                if (directoryResult.PackageRootPath is not null)
                {
                    moduleSpecifier = directoryResult.PackageRootPath;
                    isPackageRootPath = true;
                    break;
                }

                moduleFileName ??= directoryResult.ModuleFileToTry;

                // try with next level of directory
                packageRootIndex = path.IndexOf('/', packageRootIndex + 1);
                if (packageRootIndex == -1)
                {
                    moduleSpecifier = ProcessEnding(moduleFileName, allowedEndings, options, host);
                    break;
                }
            }
        }

        if (isRedirect && !isPackageRootPath)
        {
            return null;
        }

        var globalTypingsCacheLocation = host.GetGlobalTypingsCacheLocation();
        // Get a path that's relative to node_modules or the importing file's path
        // if node_modules folder is in this folder or any of its parent folders, no need to keep it.
        var pathToTopLevelNodeModules = getCanonicalFileName(moduleSpecifier[..parts.TopLevelNodeModulesIndex]);
        if (!(Core.StartsWith(canonicalSourceDirectory, pathToTopLevelNodeModules)
            || Js.IsTruthy(globalTypingsCacheLocation) && Core.StartsWith(getCanonicalFileName(globalTypingsCacheLocation!), pathToTopLevelNodeModules)))
        {
            return null;
        }

        // If the module was found in @types, get the actual Node package name
        var nodeModulesDirectoryName = moduleSpecifier[(parts.TopLevelPackageNameIndex + 1)..];
        var packageName = ModuleNameResolver.GetPackageNameFromTypesPackageName(nodeModulesDirectoryName);
        // For classic resolution, only allow importing from node_modules/@types, not other node_modules
        return Utilities.GetEmitModuleResolutionKind(options) == ModuleResolutionKind.Classic && packageName == nodeModulesDirectoryName
            ? null
            : packageName;
    }

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:1224-1342 — <c>tryDirectoryWithPackageJson</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the package.json this reads is the port's <see cref="JsObject"/> model, so
    /// <c>packageJsonContent.typesVersions</c> goes through <see cref="GetTypesVersionsPaths"/> — the reader-shaped
    /// conversion <c>moduleNameResolver.ts</c>'s own <c>readPackageJsonTypesVersionsField</c> does
    /// (<c>Program/ModuleNameResolver.PackageJson.cs:120-150</c>).
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>cachedPackageJson &#x26;&#x26; host.fileExists(…)</c> — the reference knows the cache entry is a
    /// <c>PackageJsonInfo</c> under that test; the port reads it through <c>as PackageJsonInfo</c>, which is the same
    /// discrimination its cached entry type carries.
    /// </para>
    /// </remarks>
    private static DirectoryWithPackageJsonResult TryDirectoryWithPackageJson(
        int packageRootIndex,
        string path,
        NodeModulePathParts parts,
        SourceFile importingSourceFile,
        IModuleSpecifierResolutionHost host,
        ICompilerOptions options,
        ModuleKind? overrideMode,
        List<ModuleSpecifierEnding> allowedEndings,
        Core.GetCanonicalFileName getCanonicalFileName)
    {
        var packageRootPath = path[..packageRootIndex];
        var packageJsonPath = Paths.CombinePaths(packageRootPath, "package.json");
        var moduleFileToTry = path;
        var maybeBlockedByTypesVersions = false;
        var cachedPackageJson = ((IPackageJsonInfoCache?)host.GetPackageJsonInfoCache())?.GetPackageJsonInfo(packageJsonPath) as PackageJsonInfo;
        if (cachedPackageJson is not null || host.FileExists(packageJsonPath))
        {
            var packageJsonContent = cachedPackageJson?.Contents.PackageJsonContent
                ?? (host.ReadFile(packageJsonPath) is { } packageJsonText && Utilities.TryParseJson(packageJsonText, out var parsed)
                    ? parsed as JsObject
                    : null);
            var importMode = overrideMode ?? GetDefaultResolutionModeForFile(importingSourceFile, host, options);
            if (Utilities.GetResolvePackageJsonExports(options))
            {
                // The package name that we found in node_modules could be different from the package
                // name in the package.json content via url/filepath dependency specifiers. We need to
                // use the actual directory name, so don't look at `packageJsonContent.name` here.
                var nodeModulesDirectoryName = packageRootPath[(parts.TopLevelPackageNameIndex + 1)..];
                var packageName = ModuleNameResolver.GetPackageNameFromTypesPackageName(nodeModulesDirectoryName);
                var conditions = ModuleNameResolver.GetConditions(options, importMode);
                var fromExports = packageJsonContent?.Get("exports") is { } exports
                    ? TryGetModuleNameFromExports(
                        options,
                        host,
                        path,
                        packageRootPath,
                        packageName,
                        exports,
                        conditions)
                    : null;
                if (fromExports is not null)
                {
                    return new DirectoryWithPackageJsonResult { ModuleFileToTry = fromExports, VerbatimFromExports = true };
                }

                if (packageJsonContent?.Has("exports") == true)
                {
                    return new DirectoryWithPackageJsonResult { ModuleFileToTry = path, BlockedByExports = true };
                }
            }

            var versionPaths = packageJsonContent?.Get("typesVersions") is not null
                ? GetTypesVersionsPaths(packageJsonContent)
                : null;
            if (versionPaths is not null)
            {
                var subModuleName = path[(packageRootPath.Length + 1)..];
                var fromPaths = TryGetModuleNameFromPaths(
                    subModuleName,
                    versionPaths.Paths,
                    allowedEndings,
                    packageRootPath,
                    getCanonicalFileName,
                    host,
                    options);
                if (fromPaths is null)
                {
                    maybeBlockedByTypesVersions = true;
                }
                else
                {
                    moduleFileToTry = Paths.CombinePaths(packageRootPath, fromPaths);
                }
            }

            // If the file is the main module, it can be imported by the package name
            var mainFileRelative = JsOr(
                packageJsonContent?.Get("typings"),
                JsOr(packageJsonContent?.Get("types"), JsOr(packageJsonContent?.Get("main"), "index.js")));
            if (Core.IsString(mainFileRelative)
                && !(maybeBlockedByTypesVersions && Utilities.MatchPatternOrExact(Utilities.TryParsePatterns(versionPaths!.Paths), (string)mainFileRelative!) is not null))
            {
                // The 'main' file is also subject to mapping through typesVersions, and we couldn't come up with a path
                // explicitly through typesVersions, so if it matches a key in typesVersions now, it's not reachable.
                // (The only way this can happen is if some file in a package that's not resolvable from outside the
                // package got pulled into the program anyway, e.g. transitively through a file that *is* reachable. It
                // happens very easily in fourslash tests though, since every test file listed gets included. See
                // importNameCodeFix_typesVersions.ts for an example.)
                var mainExportFile = Paths.ToPath((string)mainFileRelative!, packageRootPath, getCanonicalFileName);
                var canonicalModuleFileToTry = getCanonicalFileName(moduleFileToTry);
                if (Utilities.RemoveFileExtension(mainExportFile) == Utilities.RemoveFileExtension(canonicalModuleFileToTry))
                {
                    // ^ An arbitrary removal of file extension for this comparison is almost certainly wrong
                    return new DirectoryWithPackageJsonResult { PackageRootPath = packageRootPath, ModuleFileToTry = moduleFileToTry };
                }
                else if (
                    packageJsonContent?.Get("type") as string != "module"
                    && !Paths.FileExtensionIsOneOf(canonicalModuleFileToTry, Utilities.ExtensionsNotSupportingExtensionlessResolution)
                    && Core.StartsWith(canonicalModuleFileToTry, mainExportFile)
                    && Paths.GetDirectoryPath(canonicalModuleFileToTry) == Paths.RemoveTrailingDirectorySeparator(mainExportFile)
                    && Utilities.RemoveFileExtension(Paths.GetBaseFileName(canonicalModuleFileToTry)) == "index")
                {
                    // if mainExportFile is a directory, which contains moduleFileToTry, we just try index file
                    // example mainExportFile: `pkg/lib` and moduleFileToTry: `pkg/lib/index`, we can use packageRootPath
                    // but this behavior is deprecated for packages with "type": "module", so we only do this for packages without "type": "module"
                    // and make sure that the extension on index.{???} is something that supports omitting the extension
                    return new DirectoryWithPackageJsonResult { PackageRootPath = packageRootPath, ModuleFileToTry = moduleFileToTry };
                }
            }
        }
        else
        {
            // No package.json exists; an index.js will still resolve as the package name
            var fileName = getCanonicalFileName(moduleFileToTry[(parts.PackageRootIndex + 1)..]);
            if (fileName is "index.d.ts" or "index.js" or "index.ts" or "index.tsx")
            {
                return new DirectoryWithPackageJsonResult { ModuleFileToTry = moduleFileToTry, PackageRootPath = packageRootPath };
            }
        }

        return new DirectoryWithPackageJsonResult { ModuleFileToTry = moduleFileToTry };
    }

    // ---------------------------------------------------------------------------------------------
    // Paths and endings (moduleSpecifiers.ts:1345-1470)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:1345-1355 — <c>tryGetAnyFileFromPath</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's two <c>ExtraFileExtensionInfo</c> literals are
    /// <see cref="FileExtensionInfo"/> instances; with <c>allowJs</c> set, <c>getSupportedExtensions</c> drops both
    /// (the first has no <c>scriptKind</c>, the second's is JSON — neither is JS-like, and neither is
    /// <c>ScriptKind.Deferred</c>, <c>utilities.ts:10004</c>), so the answer is the all-extensions table either way.
    /// The port still passes them, so the call is the reference's.
    /// </remarks>
    private static string? TryGetAnyFileFromPath(IModuleSpecifierResolutionHost host, string path)
    {
        // We check all js, `node` and `json` extensions in addition to TS, since node module resolution would also
        // choose those over the directory
        var extensions = Core.Flatten<string>(
            Utilities.GetSupportedExtensions(
                new CompilerOptions { AllowJs = true },
                [
                    new FileExtensionInfo { Extension = "node", IsMixedContent = false },
                    new FileExtensionInfo { Extension = "json", IsMixedContent = false, ScriptKind = ScriptKind.JSON },
                ]).Cast<object?>().ToList());
        foreach (var e in extensions)
        {
            var fullPath = path + e;
            if (host.FileExists(fullPath))
            {
                return fullPath;
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:1357-1362 — <c>getPathsRelativeToRootDirs</c>.</summary>
    private static List<string> GetPathsRelativeToRootDirs(string path, List<string> rootDirs, Core.GetCanonicalFileName getCanonicalFileName)
        => Core.MapDefined<string, string>(
            rootDirs,
            (rootDir, _) =>
            {
                var relativePath = GetRelativePathIfInSameVolume(path, rootDir, getCanonicalFileName);
                return relativePath is not null && IsPathRelativeToParent(relativePath) ? null : relativePath;
            });

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:1364-1414 — <c>processEnding</c>.</summary>
    private static string ProcessEnding(
        string fileName,
        List<ModuleSpecifierEnding> allowedEndings,
        ICompilerOptions options,
        IModuleSpecifierResolutionHost? host = null)
    {
        if (Paths.FileExtensionIsOneOf(fileName, [Extension.Json.Value, Extension.Mjs.Value, Extension.Cjs.Value]))
        {
            return fileName;
        }

        var noExtension = Utilities.RemoveFileExtension(fileName);
        if (fileName == noExtension)
        {
            return fileName;
        }

        var jsPriority = allowedEndings.IndexOf(ModuleSpecifierEnding.JsExtension);
        var tsPriority = allowedEndings.IndexOf(ModuleSpecifierEnding.TsExtension);
        if (Paths.FileExtensionIsOneOf(fileName, [Extension.Mts.Value, Extension.Cts.Value]) && tsPriority != -1 && tsPriority < jsPriority)
        {
            return fileName;
        }
        else if (Paths.FileExtensionIsOneOf(fileName, [Extension.Dmts.Value, Extension.Mts.Value, Extension.Dcts.Value, Extension.Cts.Value]))
        {
            return noExtension + GetJSExtensionForFile(fileName, options);
        }
        else if (!Paths.FileExtensionIsOneOf(fileName, [Extension.Dts.Value]) && Paths.FileExtensionIsOneOf(fileName, [Extension.Ts.Value]) && fileName.Contains(".d."))
        {
            // `foo.d.json.ts` and the like - remap back to `foo.json`
            return TryGetRealFileNameForNonJsDeclarationFileName(fileName)!;
        }

        switch (allowedEndings[0])
        {
            case ModuleSpecifierEnding.Minimal:
                var withoutIndex = Core.RemoveSuffix(noExtension, "/index");
                if (host is not null && withoutIndex != noExtension && TryGetAnyFileFromPath(host, withoutIndex) is not null)
                {
                    // Can't remove index if there's a file by the same name as the directory.
                    // Probably more callers should pass `host` so we can determine this?
                    return noExtension;
                }

                return withoutIndex;
            case ModuleSpecifierEnding.Index:
                return noExtension;
            case ModuleSpecifierEnding.JsExtension:
                return noExtension + GetJSExtensionForFile(fileName, options);
            case ModuleSpecifierEnding.TsExtension:
                // For now, we don't know if this import is going to be type-only, which means we don't
                // know if a .d.ts extension is valid, so use no extension or a .js extension
                if (Parser.IsDeclarationFileName(fileName))
                {
                    var extensionlessPriority = allowedEndings.FindIndex(e => e is ModuleSpecifierEnding.Minimal or ModuleSpecifierEnding.Index);
                    return extensionlessPriority != -1 && extensionlessPriority < jsPriority
                        ? noExtension
                        : noExtension + GetJSExtensionForFile(fileName, options);
                }

                return fileName;
            default:
                Debug.AssertNever(allowedEndings[0]);
                return fileName;
        }
    }

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:1416-1422 — <c>tryGetRealFileNameForNonJsDeclarationFileName</c>.</summary>
    public static string? TryGetRealFileNameForNonJsDeclarationFileName(string fileName)
    {
        var baseName = Paths.GetBaseFileName(fileName);
        if (!Core.EndsWith(fileName, Extension.Ts.Value)
            || !baseName.Contains(".d.")
            || Paths.FileExtensionIsOneOf(baseName, [Extension.Dts.Value]))
        {
            return null;
        }

        var noExtension = Utilities.RemoveExtension(fileName, Extension.Ts.Value);
        var ext = noExtension[noExtension.LastIndexOf('.')..];
        return noExtension[..noExtension.IndexOf(".d.")] + ext;
    }

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:1424-1427 — <c>getJSExtensionForFile</c>.</summary>
    private static string GetJSExtensionForFile(string fileName, ICompilerOptions options)
        => TryGetJSExtensionForFile(fileName, options)
            ?? Debug.Fail<string>($"Extension {Utilities.ExtensionFromPath(fileName)} is unsupported:: FileName:: {fileName}");

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:1429-1452 — <c>tryGetJSExtensionForFile</c>.</summary>
    /// <remarks>PORT-NOTE: the port's <see cref="Extension"/> is a struct over its string value, and
    /// <c>tryGetExtensionFromPath</c> answers that value (<c>Utilities.TryGetExtensionFromPath</c>), so the reference's
    /// <c>switch</c> over <c>Extension</c> members is stated as the same test over the values it compares.</remarks>
    public static string? TryGetJSExtensionForFile(string fileName, ICompilerOptions options)
    {
        var ext = Utilities.TryGetExtensionFromPath(fileName);
        if (ext is null) return null;
        if (ext == Extension.Ts.Value || ext == Extension.Dts.Value) return Extension.Js.Value;
        if (ext == Extension.Tsx.Value) return Js.IsTruthy(options.Jsx == JsxEmit.Preserve) ? Extension.Jsx.Value : Extension.Js.Value;
        if (ext == Extension.Js.Value || ext == Extension.Jsx.Value || ext == Extension.Json.Value) return ext;
        if (ext == Extension.Dmts.Value || ext == Extension.Mts.Value || ext == Extension.Mjs.Value) return Extension.Mjs.Value;
        if (ext == Extension.Dcts.Value || ext == Extension.Cts.Value || ext == Extension.Cjs.Value) return Extension.Cjs.Value;
        return null;
    }

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:1454-1457 — <c>getRelativePathIfInSameVolume</c>.</summary>
    private static string? GetRelativePathIfInSameVolume(string path, string directoryPath, Core.GetCanonicalFileName getCanonicalFileName)
    {
        var relativePath = Paths.GetRelativePathToDirectoryOrUrl(directoryPath, path, directoryPath, getCanonicalFileName, /*isAbsolutePathAnUrl*/ false);
        return Paths.IsRootedDiskPath(relativePath) ? null : relativePath;
    }

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:1459-1461 — <c>isPathRelativeToParent</c>.</summary>
    private static bool IsPathRelativeToParent(string path) => Core.StartsWith(path, "..");

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:1463-1465 — <c>getDefaultResolutionModeForFile</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>isFullSourceFile(file) ? host.getDefaultResolutionModeForFile(file) :
    /// getDefaultResolutionModeForFileWorker(file, compilerOptions)</c> — see the class remarks: the port's parameter is
    /// always a <c>SourceFile</c>, so the host member is the arm (and the options are the file's own).</remarks>
    private static ModuleKind? GetDefaultResolutionModeForFile(SourceFile file, IModuleSpecifierResolutionHost host, ICompilerOptions compilerOptions)
        => (ModuleKind?)host.GetDefaultResolutionModeForFile(file);

    /// <summary>ported from: src/compiler/moduleSpecifiers.ts:1467-1470 — <c>prefersTsExtension</c>.</summary>
    private static bool PrefersTsExtension(List<ModuleSpecifierEnding> allowedEndings)
    {
        var tsPriority = allowedEndings.IndexOf(ModuleSpecifierEnding.TsExtension);
        return tsPriority > -1 && tsPriority < allowedEndings.IndexOf(ModuleSpecifierEnding.JsExtension);
    }

    // ---------------------------------------------------------------------------------------------
    // The support the reference imports (utilities.ts / program.ts / moduleNameResolver.ts members no
    // ported file declares yet — each names its home)
    // ---------------------------------------------------------------------------------------------

    /// <summary>TS's <c>host.getModuleSpecifierCache?.()</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the generated <see cref="IModuleSpecifierResolutionHost"/> declares the member required, and the
    /// compiler's host reports the one it cannot express as <see cref="Program.GeneratedContractException"/>
    /// (<c>Program/Program.IProgram.cs:560</c>) — <c>createProgram</c> has no module specifier cache, the language
    /// service owns one. TS's <c>?.</c> reads that same absence, so the gap exception is what this helper maps back to
    /// "no cache".
    /// </remarks>
    private static IModuleSpecifierCache? GetModuleSpecifierCache(IModuleSpecifierResolutionHost host)
    {
        try
        {
            return host.GetModuleSpecifierCache();
        }
        catch (Program.GeneratedContractException)
        {
            return null;
        }
    }

    /// <summary>TS's <c>host.getNearestAncestorDirectoryWithPackageJson</c> presence test.</summary>
    /// <remarks>
    /// PORT-NOTE: as <see cref="GetModuleSpecifierCache"/> — the member is required by the generated interface and
    /// <see cref="Program"/> reports the gap. The test is the <em>presence</em> of the member, not its answer: the
    /// reference falls back to the <c>fileExists</c> ancestor walk only when the host does not carry it
    /// (<c>moduleSpecifiers.ts:698-707</c>), which is why this answers a bool.
    /// </remarks>
    private static bool TryGetHostNearestAncestorDirectoryWithPackageJson(IModuleSpecifierResolutionHost host, string fileName, out string? directory)
    {
        try
        {
            directory = host.GetNearestAncestorDirectoryWithPackageJson(fileName, null);
            return true;
        }
        catch (Program.GeneratedContractException)
        {
            directory = null;
            return false;
        }
    }

    /// <summary>JS's <c>a || b</c>.</summary>
    /// <remarks>PORT-NOTE: the reference chains <c>||</c> over JSON members
    /// (<c>packageJsonContent?.typings || packageJsonContent?.types || …</c>), where an empty string is falsy;
    /// <c>??</c> would keep it.</remarks>
    private static object? JsOr(object? a, object? b) => Js.IsTruthy(a) ? a : b;

    /// <summary>TS's <c>packageJsonContent.typesVersions</c> handed to <c>getPackageJsonTypesVersionsPaths</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference passes the raw JSON value straight to <c>getPackageJsonTypesVersionsPaths</c>, which
    /// walks it as a <c>MapLike</c>; the port's JSON model is <see cref="JsObject"/>, so this converts it to the same
    /// table <c>moduleNameResolver.ts</c>'s own reader builds
    /// (<c>readPackageJsonTypesVersionsField</c>, <c>Program/ModuleNameResolver.PackageJson.cs:120-150</c>) — without
    /// that member's trace and semver validation, which the reference's module-specifier path has neither.
    /// </remarks>
    private static VersionPaths? GetTypesVersionsPaths(JsObject packageJsonContent)
    {
        if (packageJsonContent.Get("typesVersions") is not JsObject typesVersions)
        {
            return null;
        }

        var table = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);
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

            table[key] = paths;
        }

        return ModuleNameResolver.GetPackageJsonTypesVersionsPaths(table);
    }

    /// <summary>ported from: src/compiler/utilities.ts:10061-10122 — <c>getModuleSpecifierEndingPreference</c> (with its nested <c>inferPreference</c>).</summary>
    /// <remarks>PORT-NOTE: <c>utilities.ts</c>'s member, which <c>moduleSpecifiers.ts</c> imports and is its only reader
    /// in the compiler, so it is declared here with its reader. Its <c>sourceFile</c> argument is the reference's
    /// <c>isFullSourceFile(importingSourceFile) ? importingSourceFile : undefined</c>, always the file (see the class
    /// remarks).</remarks>
    private static ModuleSpecifierEnding GetModuleSpecifierEndingPreference(
        string? preference,
        ModuleKind? resolutionMode,
        ICompilerOptions compilerOptions,
        SourceFile? sourceFile)
    {
        var moduleResolution = Utilities.GetEmitModuleResolutionKind(compilerOptions);
        var moduleResolutionIsNodeNext = ModuleResolutionKind.Node16 <= moduleResolution && moduleResolution <= ModuleResolutionKind.NodeNext;
        if (preference == "js" || resolutionMode == ModuleKind.ESNext && moduleResolutionIsNodeNext)
        {
            // Extensions are explicitly requested or required. Now choose between .js and .ts.
            if (!ModuleNameResolver.ShouldAllowImportingTsExtension(compilerOptions))
            {
                return ModuleSpecifierEnding.JsExtension;
            }

            // `allowImportingTsExtensions` is a strong signal, so use .ts unless the file
            // already uses .js extensions and no .ts extensions.
            return InferPreference() != ModuleSpecifierEnding.JsExtension
                ? ModuleSpecifierEnding.TsExtension
                : ModuleSpecifierEnding.JsExtension;
        }

        if (preference == "minimal") return ModuleSpecifierEnding.Minimal;
        if (preference == "index") return ModuleSpecifierEnding.Index;

        // No preference was specified.
        // Look at imports and/or requires to guess whether .js, .ts, or extensionless imports are preferred.
        // N.B. that `Index` detection is not supported since it would require file system probing to do
        // accurately, and more importantly, literally nobody wants `Index` and its existence is a mystery.
        if (!ModuleNameResolver.ShouldAllowImportingTsExtension(compilerOptions))
        {
            // If .ts imports are not valid, we only need to see one .js import to go with that.
            return sourceFile is not null && UsesExtensionsOnImports(sourceFile)
                ? ModuleSpecifierEnding.JsExtension
                : ModuleSpecifierEnding.Minimal;
        }

        return InferPreference();

        ModuleSpecifierEnding InferPreference()
        {
            var usesJsExtensions = false;
            IList<LiteralExpression> specifiers = sourceFile?.Imports is { Count: > 0 } imports
                ? imports
                : sourceFile is not null && Utilities.IsSourceFileJS(sourceFile)
                    ? Core.Map(GetRequiresAtTopOfFile(sourceFile), (r, _) => (LiteralExpression)r.Arguments![0]!)!
                    : [];
            foreach (var specifier in specifiers)
            {
                if (Paths.PathIsRelative(specifier.Text!))
                {
                    if (moduleResolutionIsNodeNext
                        && resolutionMode == ModuleKind.CommonJS
                        && Program.GetModeForUsageLocationWorker(sourceFile, specifier, compilerOptions) == ModuleKind.ESNext)
                    {
                        // We're trying to decide a preference for a CommonJS module specifier, but looking at an ESM import.
                        continue;
                    }

                    if (Paths.FileExtensionIsOneOf(specifier.Text!, Utilities.ExtensionsNotSupportingExtensionlessResolution))
                    {
                        // These extensions are not optional, so do not indicate a preference.
                        continue;
                    }

                    if (Utilities.HasTSFileExtension(specifier.Text!)) return ModuleSpecifierEnding.TsExtension;
                    if (ModuleTransformSupport.HasJSFileExtension(specifier.Text!)) usesJsExtensions = true;
                }
            }

            return usesJsExtensions ? ModuleSpecifierEnding.JsExtension : ModuleSpecifierEnding.Minimal;
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:10051-10059 — <c>usesExtensionsOnImports</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>firstDefined(imports, …)</c> stops at the first import that is relative (and not
    /// one of the extensions that are mandatory), answering whether it carries an extension; the port spells that as
    /// the same first-match loop because <c>Core.FirstDefined</c> requires a reference-type answer. The reference's
    /// <c>hasExtension</c> parameter defaults to <c>or(hasJSFileExtension, hasTSFileExtension)</c> and no caller passes
    /// anything else, so that default is stated inline: <c>hasJSFileExtension</c> is <c>module.ts</c>'s transcribed copy
    /// of the utilities.ts member (<c>Transforms/Module/ModuleTransformSupport.cs:223</c>) and
    /// <c>hasTSFileExtension</c> is the utilities.ts member (<c>Checker/Utilities.SymbolBand.cs:482</c>).
    /// </remarks>
    private static bool UsesExtensionsOnImports(SourceFile sourceFile)
    {
        foreach (var import in sourceFile.Imports ?? new List<LiteralExpression>())
        {
            if (Paths.PathIsRelative(import.Text!)
                && !Paths.FileExtensionIsOneOf(import.Text!, Utilities.ExtensionsNotSupportingExtensionlessResolution))
            {
                return ModuleTransformSupport.HasJSFileExtension(import.Text!) || Utilities.HasTSFileExtension(import.Text!);
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/utilities.ts:10124-10142 — <c>getRequiresAtTopOfFile</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>declarationList.declarations.map(d =&gt; d.initializer)</c> keeps the
    /// <c>undefined</c> arms, which its one reader would crash on; <c>isRequireVariableStatement</c> has already
    /// established that every declaration is initialized to a require call, so the port adds that call.</remarks>
    private static List<CallExpression> GetRequiresAtTopOfFile(SourceFile sourceFile)
    {
        var nonRequireStatementCount = 0;
        List<CallExpression>? requires = null;
        foreach (var statement in sourceFile.Statements!)
        {
            if (nonRequireStatementCount > 3)
            {
                break;
            }

            if (IsRequireVariableStatement((Node)statement))
            {
                requires ??= [];
                foreach (var declaration in ((VariableStatement)statement).DeclarationList!.Declarations!)
                {
                    requires.Add((CallExpression)declaration.Initializer!);
                }
            }
            else if (NodeTests.IsExpressionStatement((Node)statement)
                && Utilities.IsRequireCall(((ExpressionStatement)statement).Expression!, /*requireStringLiteralLikeArgument*/ true))
            {
                (requires ??= []).Add((CallExpression)((ExpressionStatement)statement).Expression!);
            }
            else
            {
                nonRequireStatementCount++;
            }
        }

        return requires ?? [];
    }

    /// <summary>ported from: src/compiler/utilities.ts:3905-3909 — <c>isRequireVariableStatement</c>.</summary>
    private static bool IsRequireVariableStatement(Node node)
        => NodeTests.IsVariableStatement(node)
            && ((VariableStatement)node).DeclarationList?.Declarations is { Count: > 0 } declarations
            && Core.Every(declarations, (decl, _) => IsVariableDeclarationInitializedToRequire(decl));

    /// <summary>ported from: src/compiler/utilities.ts:3866-3868 (with <c>isVariableDeclarationInitializedWithRequireHelper</c>, <c>:3899-3904</c>) — <c>isVariableDeclarationInitializedToRequire</c>.</summary>
    private static bool IsVariableDeclarationInitializedToRequire(Node node)
        => NodeTests.IsVariableDeclaration(node)
            && ((VariableDeclaration)node).Initializer is { } initializer
            && Utilities.IsRequireCall(initializer, /*requireStringLiteralLikeArgument*/ true);

    /// <summary>ported from: src/compiler/utilities.ts:10037-10040 — <c>hasImplementationTSFileExtension</c>.</summary>
    private static bool HasImplementationTSFileExtension(string fileName)
        => Core.Some(Utilities.SupportedTSImplementationExtensions, (extension, _) => Paths.FileExtensionIs(fileName, extension))
            && !Parser.IsDeclarationFileName(fileName);

    /// <summary>ported from: src/compiler/utilities.ts:10163-10168 — <c>compareNumberOfDirectorySeparators</c>.</summary>
    private static Comparison CompareNumberOfDirectorySeparators(string path1, string path2)
        => Core.CompareValues(NumberOfDirectorySeparators(path1), NumberOfDirectorySeparators(path2));

    /// <inheritdoc cref="CompareNumberOfDirectorySeparators"/>
    private static int NumberOfDirectorySeparators(string str)
    {
        var count = 0;
        foreach (var ch in str)
        {
            if (ch == '/') count++;
        }

        return count;
    }

    /// <summary>ported from: src/compiler/utilities.ts:10956-10961 — <c>NodeModulePathParts</c>, and the <c>States</c> enum of <c>getNodeModulePathParts</c> (<c>:10975-10980</c>).</summary>
    private enum NodeModulePathState
    {
        BeforeNodeModules,

        NodeModules,

        Scope,

        PackageContent,
    }

    /// <summary>ported from: src/compiler/utilities.ts:10966-11009 — <c>getNodeModulePathParts</c>.</summary>
    /// <remarks>PORT-NOTE: the reference reads the character after the part start with <c>charAt</c>, which answers
    /// <c>""</c> past the end; the guard below is that same answer. <c>nodeModulesPathPart</c> is
    /// <c>ModuleNameResolver.NodeModulesPathPart</c>.</remarks>
    public static NodeModulePathParts? GetNodeModulePathParts(string fullPath)
    {
        // If fullPath can't be valid module file within node_modules, returns undefined.
        // Example of expected pattern: /base/path/node_modules/[@scope/otherpackage/@otherscope/node_modules/]package/[subdirectory/]file.js
        // Returns indices:                       ^            ^                                                      ^             ^
        var topLevelNodeModulesIndex = 0;
        var topLevelPackageNameIndex = 0;
        var packageRootIndex = 0;
        var fileNameIndex = 0;

        var partStart = 0;
        var partEnd = 0;
        var state = NodeModulePathState.BeforeNodeModules;

        while (partEnd >= 0)
        {
            partStart = partEnd;
            partEnd = fullPath.IndexOf('/', partStart + 1);
            switch (state)
            {
                case NodeModulePathState.BeforeNodeModules:
                    if (fullPath.IndexOf(ModuleNameResolver.NodeModulesPathPart, partStart, StringComparison.Ordinal) == partStart)
                    {
                        topLevelNodeModulesIndex = partStart;
                        topLevelPackageNameIndex = partEnd;
                        state = NodeModulePathState.NodeModules;
                    }
                    break;
                case NodeModulePathState.NodeModules:
                case NodeModulePathState.Scope:
                    if (state == NodeModulePathState.NodeModules
                        && partStart + 1 < fullPath.Length
                        && fullPath[partStart + 1] == '@')
                    {
                        state = NodeModulePathState.Scope;
                    }
                    else
                    {
                        packageRootIndex = partEnd;
                        state = NodeModulePathState.PackageContent;
                    }
                    break;
                case NodeModulePathState.PackageContent:
                    if (fullPath.IndexOf(ModuleNameResolver.NodeModulesPathPart, partStart, StringComparison.Ordinal) == partStart)
                    {
                        state = NodeModulePathState.NodeModules;
                    }
                    else
                    {
                        state = NodeModulePathState.PackageContent;
                    }
                    break;
            }
        }

        fileNameIndex = partStart;

        return state > NodeModulePathState.NodeModules
            ? new NodeModulePathParts
            {
                TopLevelNodeModulesIndex = topLevelNodeModulesIndex,
                TopLevelPackageNameIndex = topLevelPackageNameIndex,
                PackageRootIndex = packageRootIndex,
                FileNameIndex = fileNameIndex,
            }
            : null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:2088-2090 — <c>isNonGlobalAmbientModule</c>.</summary>
    private static bool IsNonGlobalAmbientModule(Node node)
        => NodeTests.IsModuleDeclaration(node) && NodeTests.IsStringLiteral(((ModuleDeclaration)node).Name!);
}
