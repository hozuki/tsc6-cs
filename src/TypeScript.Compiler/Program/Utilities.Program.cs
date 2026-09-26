namespace TypeScript.Compiler;

// ported from: src/compiler/utilities.ts:9041-9283 (createComputedCompilerOptions), 9291-9371 (the exported readers),
//              9374-9400 (emitModuleKindIsNonNodeESM/hasJsonModuleEmitEnabled/moduleResolutionSupportsPackageJsonExportsAndImports),
//              9960-10040 (the extension tables and getSupportedExtensions/getSupportedExtensionsWithJsonIfResolveJsonModule),
//              7827-7835 (getNewLineCharacter), 909-916 (packageIdToPackageName/packageIdToString), 12228-12233 (lib reference names)
// ported from: src/compiler/utilitiesPublic.ts:327-350 (getDefaultLibFileName)
/// <summary>
/// The <c>utilities.ts</c> members Program and module resolution read: the computed-option readers, the supported
/// extension tables and the lib-name tables.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS states each of these as an entry of the <c>createComputedCompilerOptions</c> table
/// (utilities.ts:9041-9283) and re-exports the ones callers name; the port has one method per exported name with that
/// entry's closure body, exactly as <see cref="Utilities.GetEmitScriptTarget"/> already does
/// (<c>Utilities/Utilities.Options.cs</c> records the reasoning — the memo table and the `dependencies` metadata
/// belong to M8's build/watch paths and are not ported).
/// <para>
/// PORT-NOTE: the option parameters are nullable for the same reason the ones in <c>Utilities.Options.cs</c> are: M8's
/// callers build the options object before filling it and a caller with no program has none, so each body asserts the
/// read rather than substituting a default (a default would turn a caller bug into a plausible value). Every slot read
/// here is a member of the generated <see cref="ICompilerOptions"/>, so a generator change that dropped one fails this
/// file's compilation.
/// </para>
/// <para>
/// PORT-NOTE: the extension tables are <c>readonly Extension[][]</c> in TS and are compared by <em>identity</em> in
/// <c>getSupportedExtensionsWithJsonIfResolveJsonModule</c> (<c>supportedExtensions === allSupportedExtensions</c>).
/// The port keeps the same static instances and compares them with <see cref="object.ReferenceEquals(object?, object?)"/>,
/// which is the same test; the members are <c>IList&lt;List&lt;string&gt;&gt;</c> because the generated host/option
/// interfaces that receive them (<c>readDirectory</c>, <c>resolveModuleNames</c>) spell their extension lists that way.
/// </para>
/// </remarks>
internal static partial class Utilities
{
    // ---------------------------------------------------------------------------------------------
    // The computed options Program, the checker and module resolution read (utilities.ts:9060-9356)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:9060-9079 — the <c>module</c> entry of
    /// <c>createComputedCompilerOptions</c>, exported as <c>getEmitModuleKind</c> at 9318.</summary>
    public static ModuleKind GetEmitModuleKind(ICompilerOptions? options)
    {
        var compilerOptions = options!;
        if (compilerOptions.Module is not null)
        {
            return compilerOptions.Module.Value;
        }

        var target = GetEmitScriptTarget(compilerOptions);
        if (target == ScriptTarget.ESNext)
        {
            return ModuleKind.ESNext;
        }

        if (target >= ScriptTarget.ES2022)
        {
            return ModuleKind.ES2022;
        }

        if (target >= ScriptTarget.ES2020)
        {
            return ModuleKind.ES2020;
        }

        return target >= ScriptTarget.ES2015 ? ModuleKind.ES2015 : ModuleKind.CommonJS;
    }

    /// <summary>ported from: src/compiler/utilities.ts:9080-9099 — the <c>moduleResolution</c> entry of
    /// <c>createComputedCompilerOptions</c>, exported as <c>getEmitModuleResolutionKind</c> at 9319.</summary>
    public static ModuleResolutionKind GetEmitModuleResolutionKind(ICompilerOptions? options)
    {
        var compilerOptions = options!;
        if (compilerOptions.ModuleResolution is not null)
        {
            return compilerOptions.ModuleResolution.Value;
        }

        var moduleKind = GetEmitModuleKind(compilerOptions);
        switch (moduleKind)
        {
            case ModuleKind.None:
            case ModuleKind.AMD:
            case ModuleKind.UMD:
            case ModuleKind.System:
                return ModuleResolutionKind.Classic;
            case ModuleKind.NodeNext:
                return ModuleResolutionKind.NodeNext;
        }

        if (ModuleKind.Node16 <= moduleKind && moduleKind < ModuleKind.NodeNext)
        {
            return ModuleResolutionKind.Node16;
        }

        return ModuleResolutionKind.Bundler;
    }

    /// <summary>ported from: src/compiler/utilities.ts:9100-9110 — the <c>moduleDetection</c> entry of
    /// <c>createComputedCompilerOptions</c>, exported as <c>getEmitModuleDetectionKind</c> at 9320.</summary>
    public static ModuleDetectionKind GetEmitModuleDetectionKind(ICompilerOptions? options)
    {
        var compilerOptions = options!;
        if (compilerOptions.ModuleDetection is not null)
        {
            return compilerOptions.ModuleDetection.Value;
        }

        var moduleKind = GetEmitModuleKind(compilerOptions);
        // PORT-NOTE: TS's `ModuleKind.CJS <= moduleKind && moduleKind <= ModuleKind.NodeNext` —
        // the two bounds are the CJS family, which in the reference is `Node16 <= moduleKind < NodeNext`.
        return ModuleKind.Node16 <= moduleKind && moduleKind <= ModuleKind.NodeNext
            ? ModuleDetectionKind.Force
            : ModuleDetectionKind.Auto;
    }

    /// <summary>ported from: src/compiler/utilities.ts:9111-9117 — the <c>isolatedModules</c> entry, exported as
    /// <c>getIsolatedModules</c> at 9321.</summary>
    public static bool GetIsolatedModules(ICompilerOptions? options)
    {
        var compilerOptions = options!;
        return Js.IsTruthy(compilerOptions.IsolatedModules) || Js.IsTruthy(compilerOptions.VerbatimModuleSyntax);
    }

    /// <summary>ported from: src/compiler/utilities.ts:9118-9125 — the <c>esModuleInterop</c> entry, exported as
    /// <c>getESModuleInterop</c> at 9322.</summary>
    public static bool GetESModuleInterop(ICompilerOptions? options)
        => options!.EsModuleInterop ?? true;

    /// <summary>ported from: src/compiler/utilities.ts:9126-9133 — the <c>allowSyntheticDefaultImports</c> entry,
    /// exported as <c>getAllowSyntheticDefaultImports</c> at 9323.</summary>
    public static bool GetAllowSyntheticDefaultImports(ICompilerOptions? options)
        => options!.AllowSyntheticDefaultImports ?? true;

    /// <summary>ported from: src/compiler/utilities.ts:9134-9150 — the <c>resolvePackageJsonExports</c> entry,
    /// exported as <c>getResolvePackageJsonExports</c> at 9324.</summary>
    public static bool GetResolvePackageJsonExports(ICompilerOptions? options)
    {
        var compilerOptions = options!;
        var moduleResolution = GetEmitModuleResolutionKind(compilerOptions);
        if (!ModuleResolutionSupportsPackageJsonExportsAndImports(moduleResolution))
        {
            return false;
        }

        if (compilerOptions.ResolvePackageJsonExports is not null)
        {
            return compilerOptions.ResolvePackageJsonExports.Value;
        }

        return moduleResolution is ModuleResolutionKind.Node16 or ModuleResolutionKind.NodeNext or ModuleResolutionKind.Bundler;
    }

    /// <summary>ported from: src/compiler/utilities.ts:9151-9167 — the <c>resolvePackageJsonImports</c> entry,
    /// exported as <c>getResolvePackageJsonImports</c> at 9325.</summary>
    public static bool GetResolvePackageJsonImports(ICompilerOptions? options)
    {
        var compilerOptions = options!;
        var moduleResolution = GetEmitModuleResolutionKind(compilerOptions);
        if (!ModuleResolutionSupportsPackageJsonExportsAndImports(moduleResolution))
        {
            return false;
        }

        if (compilerOptions.ResolvePackageJsonImports is not null)
        {
            return compilerOptions.ResolvePackageJsonImports.Value;
        }

        return moduleResolution is ModuleResolutionKind.Node16 or ModuleResolutionKind.NodeNext or ModuleResolutionKind.Bundler;
    }

    /// <summary>ported from: src/compiler/utilities.ts:9168-9183 — the <c>resolveJsonModule</c> entry, exported as
    /// <c>getResolveJsonModule</c> at 9326.</summary>
    public static bool GetResolveJsonModule(ICompilerOptions? options)
    {
        var compilerOptions = options!;
        if (compilerOptions.ResolveJsonModule is not null)
        {
            return compilerOptions.ResolveJsonModule.Value;
        }

        switch (GetEmitModuleKind(compilerOptions))
        {
            // TODO in 6.0: uncomment
            // case ModuleKind.Node16:
            // case ModuleKind.Node18:
            case ModuleKind.Node20:
            case ModuleKind.NodeNext:
                return true;
        }

        return GetEmitModuleResolutionKind(compilerOptions) == ModuleResolutionKind.Bundler;
    }

    /// <summary>ported from: src/compiler/utilities.ts:9184-9189 — the <c>declaration</c> entry, exported as
    /// <c>getEmitDeclarations</c> at 9327.</summary>
    public static bool GetEmitDeclarations(ICompilerOptions? options)
    {
        var compilerOptions = options!;
        return Js.IsTruthy(compilerOptions.Declaration) || Js.IsTruthy(compilerOptions.Composite);
    }

    /// <summary>ported from: src/compiler/utilities.ts:9190-9196 — the <c>preserveConstEnums</c> entry, exported as
    /// <c>shouldPreserveConstEnums</c> at 9328.</summary>
    public static bool ShouldPreserveConstEnums(ICompilerOptions? options)
    {
        var compilerOptions = options!;
        return Js.IsTruthy(compilerOptions.PreserveConstEnums) || GetIsolatedModules(compilerOptions);
    }

    /// <summary>ported from: src/compiler/utilities.ts:9197-9202 — the <c>incremental</c> entry, exported as
    /// <c>isIncrementalCompilation</c> at 9329.</summary>
    public static bool IsIncrementalCompilation(ICompilerOptions? options)
    {
        var compilerOptions = options!;
        return Js.IsTruthy(compilerOptions.Incremental) || Js.IsTruthy(compilerOptions.Composite);
    }

    /// <summary>ported from: src/compiler/utilities.ts:9203-9209 — the <c>declarationMap</c> entry, exported as
    /// <c>getAreDeclarationMapsEnabled</c> at 9330.</summary>
    public static bool GetAreDeclarationMapsEnabled(ICompilerOptions? options)
    {
        var compilerOptions = options!;
        return Js.IsTruthy(compilerOptions.DeclarationMap) && GetEmitDeclarations(compilerOptions);
    }

    /// <summary>ported from: src/compiler/utilities.ts:9210-9215 — the <c>allowJs</c> entry, exported as
    /// <c>getAllowJSCompilerOption</c> at 9331.</summary>
    public static bool GetAllowJsCompilerOption(ICompilerOptions? options)
    {
        var compilerOptions = options!;
        return compilerOptions.AllowJs ?? Js.IsTruthy(compilerOptions.CheckJs);
    }

    /// <summary>ported from: src/compiler/utilities.ts:9216-9224 — the <c>useDefineForClassFields</c> entry, exported
    /// as <c>getUseDefineForClassFields</c> at 9332.</summary>
    public static bool GetUseDefineForClassFields(ICompilerOptions? options)
    {
        var compilerOptions = options!;
        return compilerOptions.UseDefineForClassFields
            ?? GetEmitScriptTarget(compilerOptions) >= ScriptTarget.ES2022;
    }

    /// <summary>ported from: src/compiler/utilities.ts:9379-9381 — <c>getEmitStandardClassFields</c>.</summary>
    public static bool GetEmitStandardClassFields(ICompilerOptions? options)
    {
        var compilerOptions = options!;
        return compilerOptions.UseDefineForClassFields != false && GetEmitScriptTarget(compilerOptions) >= ScriptTarget.ES2022;
    }

    /// <summary>ported from: src/compiler/utilities.ts:9374-9377 — <c>emitModuleKindIsNonNodeESM</c>.</summary>
    public static bool EmitModuleKindIsNonNodeEsm(ModuleKind moduleKind)
        => moduleKind >= ModuleKind.ES2015 && moduleKind <= ModuleKind.ESNext;

    /// <summary>ported from: src/compiler/utilities.ts:9379-9388 — <c>hasJsonModuleEmitEnabled</c>.</summary>
    public static bool HasJsonModuleEmitEnabled(ICompilerOptions? options)
    {
        switch (GetEmitModuleKind(options))
        {
            case ModuleKind.None:
            case ModuleKind.System:
            case ModuleKind.UMD:
                return false;
        }

        return true;
    }

    /// <summary>ported from: src/compiler/utilities.ts:9390-9394 — <c>moduleResolutionSupportsPackageJsonExportsAndImports</c>.</summary>
    public static bool ModuleResolutionSupportsPackageJsonExportsAndImports(ModuleResolutionKind moduleResolution)
        => moduleResolution >= ModuleResolutionKind.Node16 && moduleResolution <= ModuleResolutionKind.NodeNext
            || moduleResolution == ModuleResolutionKind.Bundler;

    /// <summary>ported from: src/compiler/utilities.ts:9399-9404 — <c>moduleSupportsImportAttributes</c>.</summary>
    public static bool ModuleSupportsImportAttributes(ModuleKind moduleKind)
        => ModuleKind.Node18 <= moduleKind && moduleKind <= ModuleKind.NodeNext
            || moduleKind is ModuleKind.Preserve or ModuleKind.ESNext;

    // ---------------------------------------------------------------------------------------------
    // New lines, package ids and lib reference names (utilities.ts:7827-7835, 909-916, 12228-12233)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:7827-7835 — <c>getNewLineCharacter</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is <c>CompilerOptions | PrinterOptions</c> and reads only <c>newLine</c>;
    /// the port takes the generated <see cref="ICompilerOptions"/>, which is the only caller's type in this layer.</remarks>
    public static string GetNewLineCharacter(ICompilerOptions? options)
    {
        switch (options?.NewLine)
        {
            case NewLineKind.CarriageReturnLineFeed:
                return "\r\n";
            case NewLineKind.LineFeed:
            case null:
                return "\n";
            default:
                Debug.Fail("unknown newline kind");
                return "\n";
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:909-911 — <c>packageIdToPackageName</c>.</summary>
    public static string PackageIdToPackageName(IPackageId? id)
    {
        Debug.Assert(id is not null);
        return id.SubModuleName is not null ? $"{id.Name}/{id.SubModuleName}" : id.Name ?? "";
    }

    /// <summary>ported from: src/compiler/utilities.ts:914-916 — <c>packageIdToString</c>.</summary>
    public static string PackageIdToString(IPackageId? packageId)
    {
        Debug.Assert(packageId is not null);
        return $"{PackageIdToPackageName(packageId)}@{packageId.Version}{packageId.PeerDependencies ?? ""}";
    }

    /// <summary>ported from: src/compiler/utilities.ts:12222-12226 — <c>getLibNameFromLibReference</c>.</summary>
    public static string GetLibNameFromLibReference(IFileReference libReference)
        => Core.ToFileNameLowerCase(libReference.FileName ?? "");

    /// <summary>ported from: src/compiler/utilities.ts:12228-12233 — <c>getLibFileNameFromLibReference</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>libMap</c> is a <c>Map</c> keyed by the lower-cased lib name and its
    /// <c>get</c> answers <c>undefined</c> for a miss; the port's <c>TryGetValue</c> is the same lookup.</remarks>
    public static string? GetLibFileNameFromLibReference(IFileReference libReference)
    {
        var libName = GetLibNameFromLibReference(libReference);
        return LibMap.TryGetValue(libName, out var fileName) ? fileName : null;
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:327-350 — <c>getDefaultLibFileName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS builds this from a <c>targetToLibMap</c> that hits every target from ES2015 to ESNext; the port
    /// writes the same switch, and every member of the map answers the same file the table does
    /// (<c>lib.es2024.d.ts</c> for ES2024/ESNext through ES2025's shared entry, <c>lib.es6.d.ts</c> for ES2015).
    /// </remarks>
    public static string GetDefaultLibFileName(ICompilerOptions? options)
    {
        var target = GetEmitScriptTarget(options);
        switch (target)
        {
            case ScriptTarget.ESNext:
                return "lib.esnext.full.d.ts";
            case ScriptTarget.ES2025:
                return "lib.es2025.full.d.ts";
            case ScriptTarget.ES2024:
                return "lib.es2024.full.d.ts";
            case ScriptTarget.ES2023:
                return "lib.es2023.full.d.ts";
            case ScriptTarget.ES2022:
                return "lib.es2022.full.d.ts";
            case ScriptTarget.ES2021:
                return "lib.es2021.full.d.ts";
            case ScriptTarget.ES2020:
                return "lib.es2020.full.d.ts";
            case ScriptTarget.ES2019:
                return "lib.es2019.full.d.ts";
            case ScriptTarget.ES2018:
                return "lib.es2018.full.d.ts";
            case ScriptTarget.ES2017:
                return "lib.es2017.full.d.ts";
            case ScriptTarget.ES2016:
                return "lib.es2016.full.d.ts";
            default:
                return "lib.es6.d.ts";
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Supported extensions (utilities.ts:9960-10040)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:9970-9972 — <c>supportedTSExtensions</c> /
    /// <c>supportedTSExtensionsFlat</c>.</summary>
    public static readonly List<List<string>> SupportedTSExtensions =
    [
        [Extension.Ts.Value, Extension.Tsx.Value, Extension.Dts.Value],
        [Extension.Cts.Value, Extension.Dcts.Value],
        [Extension.Mts.Value, Extension.Dmts.Value],
    ];

    /// <summary>ported from: src/compiler/utilities.ts:9972 — <c>supportedTSExtensionsFlat</c>.</summary>
    public static readonly List<string> SupportedTSExtensionsFlat =
        [.. SupportedTSExtensions.SelectMany(static x => x)];

    /// <summary>ported from: src/compiler/utilities.ts:9973 — <c>supportedTSExtensionsWithJson</c>.</summary>
    public static readonly List<List<string>> SupportedTSExtensionsWithJson =
        [.. SupportedTSExtensions, [Extension.Json.Value]];

    /// <summary>ported from: src/compiler/utilities.ts:9975 — <c>supportedTSExtensionsForExtractExtension</c>.</summary>
    /// <remarks>PORT-NOTE: TS's comment is the contract — ".d.ts" must come first, or ".ts" is detected instead.</remarks>
    public static readonly List<string> SupportedTSExtensionsForExtractExtension =
    [
        Extension.Dts.Value, Extension.Dcts.Value, Extension.Dmts.Value,
        Extension.Cts.Value, Extension.Mts.Value, Extension.Ts.Value, Extension.Tsx.Value,
    ];

    /// <summary>ported from: src/compiler/utilities.ts:9976-9978 — <c>supportedJSExtensions</c> /
    /// <c>supportedJSExtensionsFlat</c>.</summary>
    public static readonly List<List<string>> SupportedJSExtensions =
    [
        [Extension.Js.Value, Extension.Jsx.Value],
        [Extension.Mjs.Value],
        [Extension.Cjs.Value],
    ];

    /// <summary>ported from: src/compiler/utilities.ts:9978 — <c>supportedJSExtensionsFlat</c>.</summary>
    public static readonly List<string> SupportedJSExtensionsFlat =
        [.. SupportedJSExtensions.SelectMany(static x => x)];

    /// <summary>ported from: src/compiler/utilities.ts:9979 — <c>allSupportedExtensions</c>.</summary>
    public static readonly List<List<string>> AllSupportedExtensions =
    [
        [Extension.Ts.Value, Extension.Tsx.Value, Extension.Dts.Value, Extension.Js.Value, Extension.Jsx.Value],
        [Extension.Cts.Value, Extension.Dcts.Value, Extension.Cjs.Value],
        [Extension.Mts.Value, Extension.Dmts.Value, Extension.Mjs.Value],
    ];

    /// <summary>ported from: src/compiler/utilities.ts:9980 — <c>allSupportedExtensionsWithJson</c>.</summary>
    public static readonly List<List<string>> AllSupportedExtensionsWithJson =
        [.. AllSupportedExtensions, [Extension.Json.Value]];

    /// <summary>ported from: src/compiler/utilities.ts:9984 — <c>supportedTSImplementationExtensions</c>.</summary>
    public static readonly List<string> SupportedTSImplementationExtensions =
        [Extension.Ts.Value, Extension.Cts.Value, Extension.Mts.Value, Extension.Tsx.Value];

    /// <summary>ported from: src/compiler/utilities.ts:9987 — <c>extensionsNotSupportingExtensionlessResolution</c>.</summary>
    public static readonly List<string> ExtensionsNotSupportingExtensionlessResolution =
    [
        Extension.Mts.Value, Extension.Dmts.Value, Extension.Mjs.Value,
        Extension.Cts.Value, Extension.Dcts.Value, Extension.Cjs.Value,
    ];

    /// <summary>ported from: src/compiler/utilities.ts:9993-10008 — <c>getSupportedExtensions</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS discriminates the two overloads by the presence of <c>extraFileExtensions</c>; C# gets one method
    /// with a nullable parameter, and the empty-list test is TS's <c>length === 0</c>.
    /// </remarks>
    public static List<List<string>> GetSupportedExtensions(ICompilerOptions? options, IList<IFileExtensionInfo>? extraFileExtensions = null)
    {
        var needJsExtensions = options is not null && GetAllowJsCompilerOption(options);

        if (extraFileExtensions is null || extraFileExtensions.Count == 0)
        {
            return needJsExtensions ? AllSupportedExtensions : SupportedTSExtensions;
        }

        var builtins = needJsExtensions ? AllSupportedExtensions : SupportedTSExtensions;
        var flatBuiltins = builtins.SelectMany(static x => x).ToList();
        List<List<string>> extensions = [.. builtins];
        foreach (var extraFileExtension in extraFileExtensions)
        {
            // TS's mapDefined drops the `undefined` arms.
            if (extraFileExtension.ScriptKind == ScriptKind.Deferred
                || needJsExtensions && IsJsLike(extraFileExtension.ScriptKind) && !flatBuiltins.Contains(extraFileExtension.Extension ?? ""))
            {
                extensions.Add([extraFileExtension.Extension ?? ""]);
            }
        }

        return extensions;

        static bool IsJsLike(ScriptKind? scriptKind)
            => scriptKind is ScriptKind.JS or ScriptKind.JSX;
    }

    /// <summary>ported from: src/compiler/utilities.ts:10015-10021 — <c>getSupportedExtensionsWithJsonIfResolveJsonModule</c>.</summary>
    /// <remarks>PORT-NOTE: TS's identity tests (<c>supportedExtensions === allSupportedExtensions</c>) become
    /// <see cref="object.ReferenceEquals(object?, object?)"/> over the same static instances.</remarks>
    public static List<List<string>> GetSupportedExtensionsWithJsonIfResolveJsonModule(ICompilerOptions? options, List<List<string>> supportedExtensions)
    {
        if (options is null || !GetResolveJsonModule(options))
        {
            return supportedExtensions;
        }

        if (ReferenceEquals(supportedExtensions, AllSupportedExtensions))
        {
            return AllSupportedExtensionsWithJson;
        }

        if (ReferenceEquals(supportedExtensions, SupportedTSExtensions))
        {
            return SupportedTSExtensionsWithJson;
        }

        return [.. supportedExtensions, [Extension.Json.Value]];
    }

    /// <summary>ported from: src/compiler/utilities.ts:10027-10029 — <c>hasJSFileExtension</c>.</summary>
    public static bool HasJsFileExtension(string fileName)
        => SupportedJSExtensionsFlat.Any(extension => Paths.FileExtensionIs(fileName, extension));

    /// <summary>ported from: src/compiler/utilities.ts:10032-10034 — <c>hasTSFileExtension</c>.</summary>
    /// <remarks>PORT-NOTE: not on the M6-1 member list; it is the sibling of <see cref="HasJsFileExtension"/> and the
    /// program's file collection reads both.</remarks>
    public static bool HasTsFileExtension(string fileName)
        => SupportedTSExtensionsFlat.Any(extension => Paths.FileExtensionIs(fileName, extension));

    /// <summary>ported from: src/compiler/utilities.ts:10193-10195 — <c>changeExtension</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference is declared in <c>utilities.ts</c> (Main's adjudication), not in <c>path.ts</c> — that file has
    /// <c>changeAnyExtension</c>/<c>changeFullExtension</c>, which are this member's implementation. TS's
    /// <c>changeExtension(path, newExtension)</c> passes no <c>extensionsToRemove</c> and <c>ignoreCase: true</c>, which
    /// is <c>path.ts</c>'s two-argument <c>changeAnyExtension</c> — the arm that skips the extension list entirely
    /// (with no list there is no case comparison left to make).
    /// </remarks>
    public static string ChangeExtension(string path, string newExtension)
        => Paths.ChangeAnyExtension(path, newExtension);

    /// <summary>ported from: src/compiler/utilities.ts:10276-10278 — <c>extensionIsTS</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's trailing test is <c>startsWith(ext, ".d.") &amp;&amp; endsWith(ext, ".ts")</c>, whose two helpers are
    /// the ordinal string tests; <c>.d.mts</c>/<c>.d.cts</c> are covered by the explicit members before it.
    /// </remarks>
    public static bool ExtensionIsTS(string ext)
        => ext == Extension.Ts.Value || ext == Extension.Tsx.Value || ext == Extension.Dts.Value
            || ext == Extension.Cts.Value || ext == Extension.Mts.Value || ext == Extension.Dmts.Value
            || ext == Extension.Dcts.Value
            || ext.StartsWith(".d.", StringComparison.Ordinal) && ext.EndsWith(".ts", StringComparison.Ordinal);

    /// <summary>ported from: src/compiler/utilities.ts:7626-7628 — <c>tryExtractTSExtension</c>.</summary>
    public static string? TryExtractTSExtension(string fileName)
    {
        foreach (var extension in SupportedTSExtensionsForExtractExtension)
        {
            if (Paths.FileExtensionIs(fileName, extension))
            {
                return extension;
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:6580-6584 — <c>getPossibleOriginalInputExtensionForExtension</c>.</summary>
    public static List<string> GetPossibleOriginalInputExtensionForExtension(string path)
        => Paths.FileExtensionIsOneOf(path, [Extension.Dmts.Value, Extension.Mjs.Value, Extension.Mts.Value])
            ? [Extension.Mts.Value, Extension.Mjs.Value]
            : Paths.FileExtensionIsOneOf(path, [Extension.Dcts.Value, Extension.Cjs.Value, Extension.Cts.Value])
                ? [Extension.Cts.Value, Extension.Cjs.Value]
                : Paths.FileExtensionIsOneOf(path, [".d.json.ts"])
                    ? [Extension.Json.Value]
                    : [Extension.Tsx.Value, Extension.Ts.Value, Extension.Jsx.Value, Extension.Js.Value];

    /// <summary>ported from: src/compiler/utilities.ts:8705-8712 — <c>formatMessage</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>some(args)</c> tests array truthiness through the shared helper; the port's
    /// <see cref="Core.Some"/> over the argument list is the same test (an empty argument list formats nothing).
    /// </remarks>
    public static string FormatMessage(DiagnosticMessage message, params object?[] args)
    {
        var text = GetLocaleSpecificMessage(message);
        if (Core.Some(args))
        {
            text = FormatStringFromArgs(text, args);
        }

        return text;
    }

    /// <summary>ported from: src/compiler/utilities.ts:10281-10283 — <c>resolutionExtensionIsTSOrJson</c>.</summary>
    public static bool ResolutionExtensionIsTSOrJson(string ext)
        => ExtensionIsTS(ext) || ext == Extension.Json.Value;

    /// <summary>ported from: src/compiler/utilities.ts:10291-10294 — <c>extensionFromPath</c>.</summary>
    public static string ExtensionFromPath(string path)
    {
        var ext = TryGetExtensionFromPath(path);
        return ext ?? Debug.Fail<string>($"File {path} has unknown extension.");
    }

    /// <summary>ported from: src/compiler/utilities.ts:10297-10299 — <c>isAnySupportedFileExtension</c>.</summary>
    public static bool IsAnySupportedFileExtension(string path) => TryGetExtensionFromPath(path) is not null;

    /// <summary>ported from: src/compiler/utilities.ts:10302-10304 — <c>tryGetExtensionFromPath</c>.</summary>
    public static string? TryGetExtensionFromPath(string path)
    {
        foreach (var ext in ExtensionsToRemove)
        {
            if (Paths.FileExtensionIs(path, ext))
            {
                return ext;
            }
        }

        return null;
    }

    // ---------------------------------------------------------------------------------------------
    // Whether a file is checked at all (utilities.ts:10397-10445)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:10397-10403 — <c>skipTypeChecking</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>host: HostWithIsSourceOfProjectReferenceRedirect</c> is an interface with two methods
    /// (<c>utilities.ts:10392-10395</c>); the port takes them as two delegates so a caller can hand in the program's own
    /// members without the program having to implement a second interface (its members are <c>internal</c> and an
    /// interface implementation would force them public).
    /// </remarks>
    public static bool SkipTypeChecking(
        SourceFile sourceFile,
        ICompilerOptions options,
        Func<SourceFile, bool> isSourceFileDefaultLibrary,
        Func<string, bool> isSourceOfProjectReferenceRedirect,
        bool ignoreNoCheck = false)
        => SkipTypeCheckingWorker(sourceFile, options, isSourceFileDefaultLibrary, isSourceOfProjectReferenceRedirect, ignoreNoCheck);

    /// <summary>ported from: src/compiler/utilities.ts:10406-10412 — <c>skipTypeCheckingIgnoringNoCheck</c>.</summary>
    public static bool SkipTypeCheckingIgnoringNoCheck(
        SourceFile sourceFile,
        ICompilerOptions options,
        Func<SourceFile, bool> isSourceFileDefaultLibrary,
        Func<string, bool> isSourceOfProjectReferenceRedirect)
        => SkipTypeCheckingWorker(sourceFile, options, isSourceFileDefaultLibrary, isSourceOfProjectReferenceRedirect, /*ignoreNoCheck*/ true);

    /// <summary>ported from: src/compiler/utilities.ts:10414-10426 — <c>skipTypeCheckingWorker</c>.</summary>
    private static bool SkipTypeCheckingWorker(
        SourceFile sourceFile,
        ICompilerOptions options,
        Func<SourceFile, bool> isSourceFileDefaultLibrary,
        Func<string, bool> isSourceOfProjectReferenceRedirect,
        bool ignoreNoCheck)
    {
        // If skipLibCheck is enabled, skip reporting errors if file is a declaration file.
        // If skipDefaultLibCheck is enabled, skip reporting errors if file is a lib.
        return (Js.IsTruthy(options.SkipLibCheck) && sourceFile.IsDeclarationFile
                || Js.IsTruthy(options.SkipDefaultLibCheck) && isSourceFileDefaultLibrary(sourceFile))
            || !ignoreNoCheck && Js.IsTruthy(options.NoCheck)
            || isSourceOfProjectReferenceRedirect(sourceFile.FileName ?? "")
            || !CanIncludeBindAndCheckDiagnostics(sourceFile, options);
    }

    /// <summary>ported from: src/compiler/utilities.ts:10430-10445 — <c>canIncludeBindAndCheckDiagnostics</c>.</summary>
    public static bool CanIncludeBindAndCheckDiagnostics(SourceFile sourceFile, ICompilerOptions options)
    {
        if (sourceFile.CheckJsDirective is not null && sourceFile.CheckJsDirective.Enabled == false)
        {
            return false;
        }

        if (sourceFile.ScriptKind is ScriptKind.TS or ScriptKind.TSX or ScriptKind.External)
        {
            return true;
        }

        var isJs = sourceFile.ScriptKind is ScriptKind.JS or ScriptKind.JSX;
        var isCheckJs = isJs && IsCheckJsEnabledForFile(sourceFile, options) == true;
        var isPlainJs = Utilities.IsPlainJsFile(sourceFile, options.CheckJs);

        // By default, only type-check .ts, .tsx, Deferred, plain JS, checked JS and External
        // - plain JS: .js files with no // ts-check and checkJs: undefined
        // - check JS: .js files with either // ts-check or checkJs: true
        // - external: files that are added by plugins
        return isPlainJs || isCheckJs || sourceFile.ScriptKind == ScriptKind.Deferred;
    }

    /// <summary>ported from: src/compiler/utilities.ts:10307-10309 — <c>isCheckJsEnabledForFile</c>.</summary>
    public static bool? IsCheckJsEnabledForFile(SourceFile sourceFile, ICompilerOptions compilerOptions)
        => sourceFile.CheckJsDirective is not null ? sourceFile.CheckJsDirective.Enabled : compilerOptions.CheckJs;
}
