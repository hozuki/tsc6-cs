using System.Diagnostics.CodeAnalysis;

namespace TypeScript.Compiler;

// ported from: src/compiler/moduleNameResolver.ts (the declarations module resolution is built out of)
//   155-183   Resolved / PathAndExtension
//   184-191   const enum Extensions
//   211-214   PathAndPackageId
//   318-337   ModuleResolutionState
//   422-425   VersionPaths
//   1493      ResolutionKindSpecificLoader
//   852-909   the resolution-cache interfaces
//   916-942   MissingPackageJsonInfo / PackageJsonInfoCacheEntry / PackageJsonInfoCache
//   966-1050  CacheWithRedirects / RedirectsCacheKey / createCacheWithRedirects
//   1051-1066 createPackageJsonInfoCache
//   1078-1111 createPerDirectoryResolutionCache
//   1166-1270 createNonRelativeNameResolutionCache (+ createPerModuleNameCache)
//   1271-1326 createModuleOrTypeReferenceResolutionCache
//   1687-1710 NodeResolutionFeatures
//   2223-2226 GetPackageJsonEntrypointsHost
//   2379-2397 PackageJsonInfo / PackageJsonInfoContents
//   3408-3412 SearchResult
/// <summary>
/// The constructible model of <c>moduleNameResolver.ts</c>: the shapes its free functions pass around, the caches
/// <c>createModuleResolutionCache</c>/<c>createTypeReferenceDirectiveResolutionCache</c> build, and the result
/// objects they return.
/// </summary>
/// <remarks>
/// PORT-NOTE: every TS interface here is backed by an object literal in the reference (the resolvers hand 4–8 field
/// literals around), so §5's rule 6 applies: the literal becomes a class, and the factory that made it a method on
/// <see cref="ModuleNameResolver"/>. The generated <c>IResolvedModule*</c>/<c>IResolvedTypeReferenceDirective*</c>
/// interfaces (<c>Model/NodeInterfaces.cs</c>) are implemented by the classes below because TS's literals satisfy them
/// structurally.
/// <para>
/// PORT-NOTE: TS's <c>ResolutionMode</c> has no generated C# counterpart, so it is <c>ModuleKind?</c> everywhere in
/// this slice (the generated interfaces that carry it spell the slot <c>object?</c> at their own boundary).
/// </para>
/// <para>
/// PORT-NOTE: three TS slots are typed <c>X | false | undefined</c> (the caches' "not found" sentinel). C# has no
/// three-state reference slot, so each becomes the value plus a "computed" flag; <c>false</c> is
/// "computed &amp;&amp; value is null".
/// </para>
/// </remarks>
internal enum Extensions
{
    /// <summary>'.ts', '.tsx', '.mts', '.cts'</summary>
    TypeScript = 1 << 0,

    /// <summary>'.js', '.jsx', '.mjs', '.cjs'</summary>
    JavaScript = 1 << 1,

    /// <summary>'.d.ts', etc.</summary>
    Declaration = 1 << 2,

    /// <summary>'.json'</summary>
    Json = 1 << 3,

    ImplementationFiles = TypeScript | JavaScript,
}

/// <summary>ported from: src/compiler/moduleNameResolver.ts:155-171 — <c>Resolved</c>.</summary>
/// <remarks>
/// PORT-NOTE: TS's <c>originalPath?: string | true</c> says two different things with one slot: a string is the
/// preserved-casing path the symlink target resolved to, and <c>true</c> means "the originalPath calculation is
/// already done and needs to be skipped" (only ever produced by the non-relative cache). The port keeps the string in
/// <see cref="OriginalPath"/> and the <c>true</c> in <see cref="OriginalPathIsTrue"/>, so every read is explicit.
/// </remarks>
internal sealed class Resolved
{
    public required string Path { get; set; }

    public required string Extension { get; set; }

    public IPackageId? PackageId { get; set; }

    public string? OriginalPath { get; set; }

    /// <summary>TS's <c>originalPath === true</c> arm.</summary>
    public bool OriginalPathIsTrue { get; set; }

    public bool? ResolvedUsingTsExtension { get; set; }
}

/// <summary>ported from: src/compiler/moduleNameResolver.ts:173-181 — <c>PathAndExtension</c>.</summary>
internal sealed class PathAndExtension
{
    public required string Path { get; set; }

    /// <summary>TS uses a different name than <c>extension</c> so that <c>Resolved</c> is not assignable to this.</summary>
    public required string Ext { get; set; }

    public bool? ResolvedUsingTsExtension { get; set; }
}

/// <summary>ported from: src/compiler/moduleNameResolver.ts:211-214 — <c>PathAndPackageId</c>.</summary>
/// <remarks>
/// PORT-NOTE: kept a class rather than a struct so that <c>Core.FirstDefined</c> (whose result is constrained to a
/// reference type, mirroring JS's "undefined means keep looking") can return it from the primary type-root search.
/// </remarks>
internal sealed class PathAndPackageId
{
    public required string FileName { get; init; }

    public IPackageId? PackageId { get; init; }
}

/// <summary>
/// ported from: src/compiler/moduleNameResolver.ts:3408-3412 — <c>SearchResult&lt;T&gt;</c>.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS's <c>{ value: T | undefined } | undefined</c> distinguishes "not found, keep searching"
/// (<c>undefined</c>) from "searched, result is nothing, stop" (<c>{ value: undefined }</c>). The port keeps the same
/// two states: a <c>null</c> <see cref="SearchResult{T}"/> is the first, an instance whose <see cref="Value"/> is
/// <c>null</c> is the second.
/// </remarks>
internal sealed class SearchResult<T>
{
    public T? Value { get; init; }
}

/// <summary>ported from: src/compiler/moduleNameResolver.ts:1493 — <c>ResolutionKindSpecificLoader</c>.</summary>
/// <remarks>
/// PORT-NOTE: TS declares the fifth parameter optional; C# cannot give a delegate parameter a default value
/// (CS1065), so the port spells it out at every call site. No call site in the reference passes it — the resolver's
/// loaders all close over what they need.
/// </remarks>
internal delegate Resolved? ResolutionKindSpecificLoader(
    Extensions extensions,
    string candidate,
    bool onlyRecordFailures,
    ModuleResolutionState state,
    string? packageJsonValue);

/// <summary>ported from: src/compiler/moduleNameResolver.ts:422-425 — <c>VersionPaths</c>.</summary>
internal sealed class VersionPaths
{
    public required string Version { get; init; }

    public required Dictionary<string, List<string>> Paths { get; init; }
}

/// <summary>ported from: src/compiler/moduleNameResolver.ts:1687-1710 — <c>NodeResolutionFeatures</c>.</summary>
[Flags]
internal enum NodeResolutionFeatures
{
    None = 0,

    /// <summary>Resolving <c>#local</c> names in your own package.json.</summary>
    Imports = 1 << 1,

    /// <summary>Resolving <c>your-own-name</c> from your own package.json.</summary>
    SelfName = 1 << 2,

    /// <summary>
    /// Respecting the <c>.exports</c> member of packages' package.json files and its (conditional) mappings of
    /// export names.
    /// </summary>
    Exports = 1 << 3,

    /// <summary>
    /// Allowing <c>*</c> in the LHS of an export to be followed by more content, eg <c>"./whatever/*.js"</c>.
    /// </summary>
    ExportsPatternTrailers = 1 << 4,

    /// <summary>Allowing <c>#/</c> root imports in package.json imports field.</summary>
    ImportsPatternRoot = 1 << 6,

    EsmMode = 1 << 5,

    AllFeatures = Imports | SelfName | Exports | ExportsPatternTrailers | ImportsPatternRoot,

    Node16Default = Imports | SelfName | Exports | ExportsPatternTrailers,

    NodeNextDefault = AllFeatures,

    BundlerDefault = Imports | SelfName | Exports | ExportsPatternTrailers | ImportsPatternRoot,
}

/// <summary>ported from: src/compiler/moduleNameResolver.ts:318-337 — <c>ModuleResolutionState</c>.</summary>
/// <remarks>
/// PORT-NOTE: the reference hands this object to nested lookups and lets them write back through it
/// (<c>state.resultFromCache</c>, <c>state.resolvedPackageDirectory</c>, <c>state.features</c>,
/// <c>state.candidateIsFromPackageJsonField</c> are all assigned mid-resolution), so most slots are mutable. The
/// spread copies the resolvers make (<c>{ ...state, features: … }</c>) are reproduced by the copy constructor, which
/// shares the list and cache references exactly as the JS spread does.
/// </remarks>
internal sealed class ModuleResolutionState
{
    public ModuleResolutionState()
    {
    }

    [SetsRequiredMembers]
    public ModuleResolutionState(ModuleResolutionState other)
    {
        Host = other.Host;
        CompilerOptions = other.CompilerOptions;
        TraceEnabled = other.TraceEnabled;
        FailedLookupLocations = other.FailedLookupLocations;
        AffectingLocations = other.AffectingLocations;
        ResultFromCache = other.ResultFromCache;
        PackageJsonInfoCache = other.PackageJsonInfoCache;
        Features = other.Features;
        Conditions = other.Conditions;
        RequestContainingDirectory = other.RequestContainingDirectory;
        ReportDiagnostic = other.ReportDiagnostic;
        IsConfigLookup = other.IsConfigLookup;
        CandidateIsFromPackageJsonField = other.CandidateIsFromPackageJsonField;
        ResolvedPackageDirectory = other.ResolvedPackageDirectory;
    }

    public required IModuleResolutionHost Host { get; init; }

    public required ICompilerOptions CompilerOptions { get; init; }

    public required bool TraceEnabled { get; init; }

    public List<string>? FailedLookupLocations { get; set; }

    public List<string>? AffectingLocations { get; set; }

    public ResolvedModuleWithFailedLookupLocations? ResultFromCache { get; set; }

    public IPackageJsonInfoCache? PackageJsonInfoCache { get; init; }

    public NodeResolutionFeatures Features { get; set; }

    /// <summary>PORT-NOTE: TS types this <c>readonly string[]</c> and still reassigns it in <c>getEntrypointsFromPackageJsonInfo</c>, so the port keeps it mutable.</summary>
    public List<string> Conditions { get; set; } = [];

    public string? RequestContainingDirectory { get; set; }

    /// <summary>PORT-NOTE: TS replaces this while building the "try again without exports" probe states, so it is settable rather than init-only.</summary>
    public Action<IDiagnostic> ReportDiagnostic { get; set; } = static _ => { };

    public bool IsConfigLookup { get; set; }

    public bool CandidateIsFromPackageJsonField { get; set; }

    public bool ResolvedPackageDirectory { get; set; }
}

// ---------------------------------------------------------------------------------------------
// Result objects
// ---------------------------------------------------------------------------------------------

/// <summary>ported from: src/compiler/types.ts:9986-10010 — <c>ResolvedModuleFull</c>, as the class <c>createResolvedModuleWithFailedLookupLocations</c> builds.</summary>
internal sealed class ResolvedModuleFull : IResolvedModuleFull
{
    public required string? ResolvedFileName { get; set; }

    public string? OriginalPath { get; set; }

    public required string? Extension { get; set; }

    public bool? IsExternalLibraryImport { get; set; }

    public IPackageId? PackageId { get; set; }

    public bool? ResolvedUsingTsExtension { get; set; }
}

/// <summary>ported from: src/compiler/types.ts:10039-10048 — <c>ResolvedModuleWithFailedLookupLocations</c>.</summary>
internal sealed class ResolvedModuleWithFailedLookupLocations : IResolvedModuleWithFailedLookupLocations
{
    public IResolvedModuleFull? ResolvedModule { get; set; }

    public List<string>? FailedLookupLocations { get; set; }

    public List<string>? AffectingLocations { get; set; }

    public List<IDiagnostic>? ResolutionDiagnostics { get; set; }

    public string? AlternateResult { get; set; }
}

/// <summary>ported from: src/compiler/types.ts:10141-10158 — <c>ResolvedTypeReferenceDirective</c>.</summary>
internal sealed class ResolvedTypeReferenceDirective : IResolvedTypeReferenceDirective
{
    public required bool Primary { get; set; }

    public required string? ResolvedFileName { get; set; }

    public string? OriginalPath { get; set; }

    public IPackageId? PackageId { get; set; }

    public bool? IsExternalLibraryImport { get; set; }
}

/// <summary>ported from: src/compiler/types.ts:10166-10172 — <c>ResolvedTypeReferenceDirectiveWithFailedLookupLocations</c>.</summary>
internal sealed class ResolvedTypeReferenceDirectiveWithFailedLookupLocations : IResolvedTypeReferenceDirectiveWithFailedLookupLocations
{
    public IResolvedTypeReferenceDirective? ResolvedTypeReferenceDirective { get; set; }

    public List<string>? FailedLookupLocations { get; set; }

    public List<string>? AffectingLocations { get; set; }

    public List<IDiagnostic>? ResolutionDiagnostics { get; set; }
}

// ---------------------------------------------------------------------------------------------
// package.json info
// ---------------------------------------------------------------------------------------------

/// <summary>ported from: src/compiler/moduleNameResolver.ts:916-920 — <c>MissingPackageJsonInfo</c>.</summary>
internal sealed class MissingPackageJsonInfo : IPackageJsonInfoCacheEntry
{
    public required string PackageDirectory { get; init; }

    public required bool DirectoryExists { get; init; }
}

/// <summary>
/// ported from: src/compiler/moduleNameResolver.ts:922 — <c>PackageJsonInfoCacheEntry</c>.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS's <c>PackageJsonInfo | MissingPackageJsonInfo</c> is discriminated by <c>entry.contents</c>; C# uses
/// a sealed marker interface, which makes <c>isPackageJsonInfo</c>/<c>isMissingPackageJsonInfo</c> total (the two
/// classes are the only implementations).
/// </remarks>
internal interface IPackageJsonInfoCacheEntry;

/// <summary>ported from: src/compiler/moduleNameResolver.ts:2379-2382 — <c>PackageJsonInfo</c>.</summary>
internal sealed class PackageJsonInfo : IPackageJsonInfoCacheEntry
{
    public required string PackageDirectory { get; init; }

    public required PackageJsonInfoContents Contents { get; init; }
}

/// <summary>ported from: src/compiler/moduleNameResolver.ts:2384-2397 — <c>PackageJsonInfoContents</c>.</summary>
internal sealed class PackageJsonInfoContents
{
    /// <summary>
    /// PORT-NOTE: TS types this <c>PackageJsonPathFields</c>, an interface the JSON reader's result is cast to. The
    /// port's JSON model (<c>JsObject</c>, <c>Program/Utilities.ModuleResolution.cs</c>) is what the readers walk, so
    /// the three declared <c>PackageJson</c>/<c>PackageJsonPathFields</c> shapes collapse into it.
    /// </summary>
    public required JsObject PackageJsonContent { get; init; }

    /// <summary><c>false</c> means "versionPaths are not present"; <c>null</c> means "not yet resolved".</summary>
    public VersionPaths? VersionPaths { get; set; }

    public bool VersionPathsComputed { get; set; }

    /// <summary><c>false</c> means "resolved to nothing"; <c>null</c> means "not yet resolved".</summary>
    public List<string>? ResolvedEntrypoints { get; set; }

    public bool ResolvedEntrypointsComputed { get; set; }

    /// <summary><c>false</c> means "peerDependencies are not present"; <c>null</c> means "not yet resolved".</summary>
    public string? PeerDependencies { get; set; }

    public bool PeerDependenciesComputed { get; set; }
}

// ---------------------------------------------------------------------------------------------
// Caches
// ---------------------------------------------------------------------------------------------

/// <summary>
/// The <c>isReadonly</c> slot every resolution cache carries.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS's <c>createResolvedModuleWithFailedLookupLocations</c> types its parameter as
/// <c>ModuleResolutionCache | NonRelativeModuleNameResolutionCache | undefined</c> and reads only <c>cache?.isReadonly</c>,
/// relying on both classes carrying the member structurally. C# needs the shared member named, so the two implement it
/// through this interface at the use site.
/// </remarks>
internal interface IResolutionCache
{
    bool IsReadonly { get; set; }
}

/// <summary>ported from: src/compiler/moduleNameResolver.ts:934-942 — <c>PackageJsonInfoCache</c>.</summary>
internal interface IPackageJsonInfoCache
{
    IPackageJsonInfoCacheEntry? GetPackageJsonInfo(string packageJsonPath);

    void SetPackageJsonInfo(string packageJsonPath, IPackageJsonInfoCacheEntry info);

    Dictionary<string, IPackageJsonInfoCacheEntry>? GetInternalMap();

    void Clear();

    /// <summary>PORT-NOTE: TS's <c>isReadonly?</c> is set from outside after the cache is built (program.ts), so the port keeps a settable flag.</summary>
    bool IsReadonly { get; set; }
}

/// <summary>ported from: src/compiler/moduleNameResolver.ts:1051-1066 — <c>createPackageJsonInfoCache</c>.</summary>
/// <remarks>
/// PORT-NOTE: TS returns an object literal closing over the <c>Map</c>; §5's rule 6 turns it into a class holding the
/// map, built by <see cref="ModuleNameResolver.CreatePackageJsonInfoCache"/>.
/// </remarks>
internal sealed class PackageJsonInfoCache : IPackageJsonInfoCache
{
    private readonly string _currentDirectory;
    private readonly Core.GetCanonicalFileName _getCanonicalFileName;
    private Dictionary<string, IPackageJsonInfoCacheEntry>? _cache;

    public PackageJsonInfoCache(string currentDirectory, Core.GetCanonicalFileName getCanonicalFileName)
    {
        _currentDirectory = currentDirectory;
        _getCanonicalFileName = getCanonicalFileName;
    }

    public bool IsReadonly { get; set; }

    public IPackageJsonInfoCacheEntry? GetPackageJsonInfo(string packageJsonPath)
        => _cache?.GetValueOrDefault(Paths.ToPath(packageJsonPath, _currentDirectory, _getCanonicalFileName));

    public void SetPackageJsonInfo(string packageJsonPath, IPackageJsonInfoCacheEntry info)
    {
        _cache ??= new Dictionary<string, IPackageJsonInfoCacheEntry>(StringComparer.Ordinal);
        _cache[Paths.ToPath(packageJsonPath, _currentDirectory, _getCanonicalFileName)] = info;
    }

    public void Clear() => _cache = null;

    public Dictionary<string, IPackageJsonInfoCacheEntry>? GetInternalMap() => _cache;
}

/// <summary>ported from: src/compiler/moduleNameResolver.ts:966-975 — <c>CacheWithRedirects</c> / <c>RedirectsCacheKey</c>.</summary>
/// <remarks>
/// PORT-NOTE: <c>RedirectsCacheKey</c> is <c>string &amp; { __compilerOptionsKey }</c>, a compile-time-only brand, so it
/// is <see cref="string"/> here (the same treatment <c>Paths</c> gives <c>Path</c>). The maps are keyed by compiler-options
/// <em>identity</em>, which is what <see cref="ReferenceEqualityComparer"/> provides.
/// </remarks>
internal sealed class CacheWithRedirects<K, V>
    where K : notnull
{
    private readonly Dictionary<ICompilerOptions, Dictionary<K, V>> _redirectsMap = new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<string, Dictionary<K, V>> _redirectsKeyToMap = new(StringComparer.Ordinal);

    private readonly Dictionary<ICompilerOptions, string> _optionsToRedirectsKey;

    private ICompilerOptions? _ownOptions;

    private Dictionary<K, V> _ownMap = [];

    public CacheWithRedirects(
        ICompilerOptions? ownOptions,
        Dictionary<ICompilerOptions, string> optionsToRedirectsKey)
    {
        _optionsToRedirectsKey = optionsToRedirectsKey;
        _ownOptions = ownOptions;
        if (ownOptions is not null)
        {
            _redirectsMap[ownOptions] = _ownMap;
        }
    }

    public Dictionary<K, V>? GetMapOfCacheRedirects(IResolvedProjectReference? redirectedReference)
        => redirectedReference is not null
            ? GetOrCreateMap(redirectedReference.CommandLine!.Options!, create: false)
            : _ownMap;

    public Dictionary<K, V> GetOrCreateMapOfCacheRedirects(IResolvedProjectReference? redirectedReference)
        => redirectedReference is not null
            ? GetOrCreateMap(redirectedReference.CommandLine!.Options!, create: true)!
            : _ownMap;

    public void Update(ICompilerOptions newOptions)
    {
        if (!ReferenceEquals(_ownOptions, newOptions))
        {
            if (_ownOptions is not null)
            {
                // set new map for new options as ownMap
                _ownMap = GetOrCreateMap(newOptions, create: true)!;
            }
            else
            {
                // Use existing map if oldOptions = undefined
                _redirectsMap[newOptions] = _ownMap;
            }

            _ownOptions = newOptions;
        }
    }

    public void Clear()
    {
        var ownKey = _ownOptions is not null ? _optionsToRedirectsKey.GetValueOrDefault(_ownOptions) : null;
        _ownMap.Clear();
        _redirectsMap.Clear();
        _optionsToRedirectsKey.Clear();
        _redirectsKeyToMap.Clear();
        if (_ownOptions is not null)
        {
            if (ownKey is not null)
            {
                _optionsToRedirectsKey[_ownOptions] = ownKey;
            }

            _redirectsMap[_ownOptions] = _ownMap;
        }
    }

    public Dictionary<K, V> GetOwnMap() => _ownMap;

    private Dictionary<K, V>? GetOrCreateMap(ICompilerOptions redirectOptions, bool create)
    {
        var result = _redirectsMap.GetValueOrDefault(redirectOptions);
        if (result is not null)
        {
            return result;
        }

        var key = GetRedirectsCacheKey(redirectOptions);
        result = _redirectsKeyToMap.GetValueOrDefault(key);
        if (result is null)
        {
            if (_ownOptions is not null)
            {
                var ownKey = GetRedirectsCacheKey(_ownOptions);
                if (ownKey == key)
                {
                    result = _ownMap;
                }
                else if (!_redirectsKeyToMap.ContainsKey(ownKey))
                {
                    _redirectsKeyToMap[ownKey] = _ownMap;
                }
            }

            if (create)
            {
                result ??= [];
            }

            if (result is not null)
            {
                _redirectsKeyToMap[key] = result;
            }
        }

        if (result is not null)
        {
            _redirectsMap[redirectOptions] = result;
        }

        return result;
    }

    private string GetRedirectsCacheKey(ICompilerOptions options)
    {
        var result = _optionsToRedirectsKey.GetValueOrDefault(options);
        if (result is null)
        {
            result = ModuleNameResolver.GetKeyForCompilerOptions(options, Utilities.ModuleResolutionOptionDeclarations);
            _optionsToRedirectsKey[options] = result;
        }

        return result;
    }
}

/// <summary>ported from: src/compiler/moduleNameResolver.ts:869-881 — <c>PerDirectoryResolutionCache&lt;T&gt;</c>.</summary>
internal interface IPerDirectoryResolutionCache<T>
    where T : class
{
    T? GetFromDirectoryCache(string name, ModuleKind? mode, string directoryName, IResolvedProjectReference? redirectedReference);

    ModeAwareCache<T> GetOrCreateCacheForDirectory(string directoryName, IResolvedProjectReference? redirectedReference = null);

    void Clear();

    void Update(ICompilerOptions options);

    CacheWithRedirects<string, ModeAwareCache<T>> DirectoryToModuleNameMap { get; }

    bool IsReadonly { get; set; }
}

/// <summary>ported from: src/compiler/moduleNameResolver.ts:1078-1111 — <c>createPerDirectoryResolutionCache</c>.</summary>
internal sealed class PerDirectoryResolutionCache<T> : IPerDirectoryResolutionCache<T>
    where T : class
{
    public PerDirectoryResolutionCache(
        string currentDirectory,
        Core.GetCanonicalFileName getCanonicalFileName,
        ICompilerOptions? options,
        Dictionary<ICompilerOptions, string> optionsToRedirectsKey)
    {
        CurrentDirectory = currentDirectory;
        GetCanonicalFileName = getCanonicalFileName;
        DirectoryToModuleNameMap = ModuleNameResolver.CreateCacheWithRedirects<string, ModeAwareCache<T>>(options, optionsToRedirectsKey);
    }

    internal string CurrentDirectory { get; }

    internal Core.GetCanonicalFileName GetCanonicalFileName { get; }

    public CacheWithRedirects<string, ModeAwareCache<T>> DirectoryToModuleNameMap { get; }

    public bool IsReadonly { get; set; }

    public void Clear() => DirectoryToModuleNameMap.Clear();

    public void Update(ICompilerOptions options) => DirectoryToModuleNameMap.Update(options);

    public ModeAwareCache<T> GetOrCreateCacheForDirectory(string directoryName, IResolvedProjectReference? redirectedReference = null)
    {
        var path = Paths.ToPath(directoryName, CurrentDirectory, GetCanonicalFileName);
        return ModuleNameResolver.GetOrCreateCache(
            DirectoryToModuleNameMap,
            redirectedReference,
            path,
            static () => new ModeAwareCache<T>());
    }

    public T? GetFromDirectoryCache(string name, ModuleKind? mode, string directoryName, IResolvedProjectReference? redirectedReference)
    {
        var path = Paths.ToPath(directoryName, CurrentDirectory, GetCanonicalFileName);
        return DirectoryToModuleNameMap.GetMapOfCacheRedirects(redirectedReference)?.GetValueOrDefault(path)?.Get(name, mode);
    }
}

/// <summary>ported from: src/compiler/moduleNameResolver.ts:894-899 — <c>PerNonRelativeNameCache&lt;T&gt;</c>.</summary>
internal interface IPerNonRelativeNameCache<T>
    where T : class
{
    T? Get(string directory);

    void Set(string directory, T result);

    bool IsReadonly { get; set; }
}

/// <summary>ported from: src/compiler/moduleNameResolver.ts:882-893 — <c>NonRelativeNameResolutionCache&lt;T&gt;</c>.</summary>
internal interface INonRelativeNameResolutionCache<T> : IResolutionCache
    where T : class
{
    T? GetFromNonRelativeNameCache(string nonRelativeName, ModuleKind? mode, string directoryName, IResolvedProjectReference? redirectedReference);

    IPerNonRelativeNameCache<T> GetOrCreateCacheForNonRelativeName(string nonRelativeName, ModuleKind? mode, IResolvedProjectReference? redirectedReference = null);

    void Clear();

    void Update(ICompilerOptions options);
}

/// <summary>
/// ported from: src/compiler/moduleNameResolver.ts:910-914 — <c>NonRelativeModuleNameResolutionCache</c>.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS's <c>NonRelativeModuleNameResolutionCache</c> is the specialization of
/// <c>NonRelativeNameResolutionCache</c> that is *also* a <c>PackageJsonInfoCache</c> (which is why
/// <c>classicNameResolver</c> can hand its cache on as the state's package.json cache). C# needs that union named, so it
/// is this interface.
/// </remarks>
internal interface INonRelativeModuleNameResolutionCache<T> : INonRelativeNameResolutionCache<T>, IPackageJsonInfoCache
    where T : class;

/// <summary>ported from: src/compiler/moduleNameResolver.ts:1205-1268 — <c>createPerModuleNameCache</c>.</summary>
/// <remarks>
/// PORT-NOTE: the reference builds this with a closure over the directory map; the class keeps the same state (the map
/// plus what <c>set</c> needs to compute the shared prefix) and the same bodies.
/// </remarks>
internal sealed class PerNonRelativeNameCache<T> : IPerNonRelativeNameCache<T>
    where T : class
{
    private readonly Dictionary<string, T> _directoryPathMap = new(StringComparer.Ordinal);
    private readonly string _currentDirectory;
    private readonly Core.GetCanonicalFileName _getCanonicalFileName;
    private readonly Func<T, string?> _getResolvedFileName;

    public PerNonRelativeNameCache(
        string currentDirectory,
        Core.GetCanonicalFileName getCanonicalFileName,
        Func<T, string?> getResolvedFileName)
    {
        _currentDirectory = currentDirectory;
        _getCanonicalFileName = getCanonicalFileName;
        _getResolvedFileName = getResolvedFileName;
    }

    public bool IsReadonly { get; set; }

    public T? Get(string directory)
        => _directoryPathMap.GetValueOrDefault(Paths.ToPath(directory, _currentDirectory, _getCanonicalFileName));

    /// <summary>
    /// At first this adds the entry directory → module resolution result to the table. Then it computes the set of parent
    /// folders for <paramref name="directory"/> that should have the same module resolution result and adds an entry for
    /// every one of them. Say the first directory name is <c>/a/b/c/d/e</c> and the result is <c>/a/b/bar.ts</c>: the
    /// parents that share the result are <c>/a/b/c/d</c>, <c>/a/b/c</c> and <c>/a/b</c>, so a request from any file in
    /// those is an immediate cache hit.
    /// </summary>
    public void Set(string directory, T result)
    {
        var path = Paths.ToPath(directory, _currentDirectory, _getCanonicalFileName);
        // if the entry is already in the cache, do nothing
        if (_directoryPathMap.ContainsKey(path))
        {
            return;
        }

        _directoryPathMap[path] = result;

        var resolvedFileName = _getResolvedFileName(result);
        // Find the common prefix between the directory and the resolved file name: it is the shortest path that has the
        // same resolution. For failed lookups, cache the result for every directory up to the root.
        var commonPrefix = resolvedFileName is not null ? GetCommonPrefix(path, resolvedFileName) : null;
        var current = path;
        while (current != commonPrefix)
        {
            var parent = Paths.GetDirectoryPath(current);
            if (parent == current || _directoryPathMap.ContainsKey(parent))
            {
                break;
            }

            _directoryPathMap[parent] = result;
            current = parent;
        }
    }

    private string? GetCommonPrefix(string directory, string resolution)
    {
        var resolutionDirectory = Paths.ToPath(
            Paths.GetDirectoryPath(resolution),
            _currentDirectory,
            _getCanonicalFileName);

        // find the first position where the directory and the resolution differ
        var i = 0;
        var limit = Math.Min(directory.Length, resolutionDirectory.Length);
        while (i < limit && directory[i] == resolutionDirectory[i])
        {
            i++;
        }

        if (i == directory.Length && (resolutionDirectory.Length == i || resolutionDirectory[i] == Paths.DirectorySeparator[0]))
        {
            return directory;
        }

        var rootLength = Paths.GetRootLength(directory);
        if (i < rootLength)
        {
            return null;
        }

        var sep = directory.LastIndexOf(Paths.DirectorySeparator[0], Math.Max(0, i - 1));
        if (sep == -1)
        {
            return null;
        }

        return directory[..Math.Max(sep, rootLength)];
    }
}

/// <summary>ported from: src/compiler/moduleNameResolver.ts:1166-1203 — <c>createNonRelativeNameResolutionCache</c>.</summary>
internal sealed class NonRelativeNameResolutionCache<T> : INonRelativeNameResolutionCache<T>, IResolutionCache
    where T : class
{
    private readonly string _currentDirectory;
    private readonly Core.GetCanonicalFileName _getCanonicalFileName;
    private readonly Func<T, string?> _getResolvedFileName;
    private readonly CacheWithRedirects<string, IPerNonRelativeNameCache<T>> _moduleNameToDirectoryMap;

    public NonRelativeNameResolutionCache(
        string currentDirectory,
        Core.GetCanonicalFileName getCanonicalFileName,
        ICompilerOptions? options,
        Func<T, string?> getResolvedFileName,
        Dictionary<ICompilerOptions, string> optionsToRedirectsKey)
    {
        _currentDirectory = currentDirectory;
        _getCanonicalFileName = getCanonicalFileName;
        _getResolvedFileName = getResolvedFileName;
        _moduleNameToDirectoryMap = ModuleNameResolver.CreateCacheWithRedirects<string, IPerNonRelativeNameCache<T>>(options, optionsToRedirectsKey);
    }

    public bool IsReadonly { get; set; }

    public void Clear() => _moduleNameToDirectoryMap.Clear();

    public void Update(ICompilerOptions options) => _moduleNameToDirectoryMap.Update(options);

    public T? GetFromNonRelativeNameCache(
        string nonRelativeName,
        ModuleKind? mode,
        string directoryName,
        IResolvedProjectReference? redirectedReference)
    {
        Debug.Assert(!Utilities.IsExternalModuleNameRelative(nonRelativeName));
        return _moduleNameToDirectoryMap
            .GetMapOfCacheRedirects(redirectedReference)
            ?.GetValueOrDefault(ModuleNameResolver.CreateModeAwareCacheKey(nonRelativeName, mode))
            ?.Get(directoryName);
    }

    public IPerNonRelativeNameCache<T> GetOrCreateCacheForNonRelativeName(
        string nonRelativeName,
        ModuleKind? mode,
        IResolvedProjectReference? redirectedReference = null)
    {
        Debug.Assert(!Utilities.IsExternalModuleNameRelative(nonRelativeName));
        return ModuleNameResolver.GetOrCreateCache(
            _moduleNameToDirectoryMap,
            redirectedReference,
            ModuleNameResolver.CreateModeAwareCacheKey(nonRelativeName, mode),
            () => new PerNonRelativeNameCache<T>(_currentDirectory, _getCanonicalFileName, _getResolvedFileName));
    }
}

/// <summary>ported from: src/compiler/moduleNameResolver.ts:900-909 — <c>ModuleResolutionCache</c>.</summary>
internal sealed class ModuleResolutionCache
    : ModuleOrTypeReferenceResolutionCache<ResolvedModuleWithFailedLookupLocations>
{
    public ModuleResolutionCache(
        string currentDirectory,
        Core.GetCanonicalFileName getCanonicalFileName,
        ICompilerOptions? options,
        IPackageJsonInfoCache? packageJsonInfoCache,
        Dictionary<ICompilerOptions, string> optionsToRedirectsKey)
        : base(currentDirectory, getCanonicalFileName, options, packageJsonInfoCache, ModuleNameResolver.GetOriginalOrResolvedModuleFileName, optionsToRedirectsKey)
    {
    }

    /// <summary>PORT-NOTE: the deprecated alias (<c>getOrCreateCacheForModuleName</c>) is ported because the interface declares it.</summary>
    public IPerNonRelativeNameCache<ResolvedModuleWithFailedLookupLocations> GetOrCreateCacheForModuleName(
        string nonRelativeModuleName,
        ModuleKind? mode,
        IResolvedProjectReference? redirectedReference = null)
        => GetOrCreateCacheForNonRelativeName(nonRelativeModuleName, mode, redirectedReference);
}

/// <summary>ported from: src/compiler/moduleNameResolver.ts:852-855 — <c>TypeReferenceDirectiveResolutionCache</c>.</summary>
internal sealed class TypeReferenceDirectiveResolutionCache
    : ModuleOrTypeReferenceResolutionCache<ResolvedTypeReferenceDirectiveWithFailedLookupLocations>
{
    public TypeReferenceDirectiveResolutionCache(
        string currentDirectory,
        Core.GetCanonicalFileName getCanonicalFileName,
        ICompilerOptions? options,
        IPackageJsonInfoCache? packageJsonInfoCache,
        Dictionary<ICompilerOptions, string> optionsToRedirectsKey)
        : base(currentDirectory, getCanonicalFileName, options, packageJsonInfoCache, ModuleNameResolver.GetOriginalOrResolvedTypeReferenceFileName, optionsToRedirectsKey)
    {
    }
}

/// <summary>
/// ported from: src/compiler/moduleNameResolver.ts:1271-1326 — <c>createModuleOrTypeReferenceResolutionCache</c>, whose
/// constructor is the port's counterpart of that factory; <see cref="ModuleResolutionCache"/> and
/// <see cref="TypeReferenceDirectiveResolutionCache"/> are its two instantiations.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS composes this by spreading three object literals into one; the port keeps the three as fields and
/// forwards, which is the same shape at the call sites. The one place the spread is observable is <c>isReadonly</c>:
/// spreading copied the *value* (undefined) from the package.json cache, so the combined cache's flag is its own and the
/// inner package.json cache keeps a separate one. That is reproduced deliberately — <c>getPackageJsonInfoCache()</c>
/// hands out the inner cache, and the reference would let that inner cache keep writing after the combined one was
/// marked readonly.
/// </remarks>
internal class ModuleOrTypeReferenceResolutionCache<T>
    : IPackageJsonInfoCache, IResolutionCache, IPerDirectoryResolutionCache<T>, INonRelativeModuleNameResolutionCache<T>
    where T : class
{
    private readonly PerDirectoryResolutionCache<T> _perDirectoryResolutionCache;
    private readonly NonRelativeNameResolutionCache<T> _nonRelativeNameResolutionCache;
    private readonly IPackageJsonInfoCache _packageJsonInfoCache;

    public ModuleOrTypeReferenceResolutionCache(
        string currentDirectory,
        Core.GetCanonicalFileName getCanonicalFileName,
        ICompilerOptions? options,
        IPackageJsonInfoCache? packageJsonInfoCache,
        Func<T, string?> getResolvedFileName,
        Dictionary<ICompilerOptions, string>? optionsToRedirectsKey)
    {
        optionsToRedirectsKey ??= new Dictionary<ICompilerOptions, string>(ReferenceEqualityComparer.Instance);
        OptionsToRedirectsKey = optionsToRedirectsKey;
        _perDirectoryResolutionCache = new PerDirectoryResolutionCache<T>(currentDirectory, getCanonicalFileName, options, optionsToRedirectsKey);
        _nonRelativeNameResolutionCache = new NonRelativeNameResolutionCache<T>(
            currentDirectory,
            getCanonicalFileName,
            options,
            getResolvedFileName,
            optionsToRedirectsKey);
        _packageJsonInfoCache = packageJsonInfoCache ?? ModuleNameResolver.CreatePackageJsonInfoCache(currentDirectory, getCanonicalFileName);
    }

    public Dictionary<ICompilerOptions, string> OptionsToRedirectsKey { get; }

    public bool IsReadonly { get; set; }

    public CacheWithRedirects<string, ModeAwareCache<T>> DirectoryToModuleNameMap => _perDirectoryResolutionCache.DirectoryToModuleNameMap;

    public IPackageJsonInfoCacheEntry? GetPackageJsonInfo(string packageJsonPath)
        => _packageJsonInfoCache.GetPackageJsonInfo(packageJsonPath);

    public void SetPackageJsonInfo(string packageJsonPath, IPackageJsonInfoCacheEntry info)
        => _packageJsonInfoCache.SetPackageJsonInfo(packageJsonPath, info);

    public Dictionary<string, IPackageJsonInfoCacheEntry>? GetInternalMap() => _packageJsonInfoCache.GetInternalMap();

    public T? GetFromDirectoryCache(string name, ModuleKind? mode, string directoryName, IResolvedProjectReference? redirectedReference)
        => _perDirectoryResolutionCache.GetFromDirectoryCache(name, mode, directoryName, redirectedReference);

    public ModeAwareCache<T> GetOrCreateCacheForDirectory(string directoryName, IResolvedProjectReference? redirectedReference = null)
        => _perDirectoryResolutionCache.GetOrCreateCacheForDirectory(directoryName, redirectedReference);

    public T? GetFromNonRelativeNameCache(string nonRelativeName, ModuleKind? mode, string directoryName, IResolvedProjectReference? redirectedReference)
        => _nonRelativeNameResolutionCache.GetFromNonRelativeNameCache(nonRelativeName, mode, directoryName, redirectedReference);

    public IPerNonRelativeNameCache<T> GetOrCreateCacheForNonRelativeName(
        string nonRelativeName,
        ModuleKind? mode,
        IResolvedProjectReference? redirectedReference = null)
        => _nonRelativeNameResolutionCache.GetOrCreateCacheForNonRelativeName(nonRelativeName, mode, redirectedReference);

    public void Update(ICompilerOptions options)
    {
        _perDirectoryResolutionCache.Update(options);
        _nonRelativeNameResolutionCache.Update(options);
    }

    public void Clear()
    {
        ClearAllExceptPackageJsonInfoCache();
        _packageJsonInfoCache.Clear();
    }

    public void ClearAllExceptPackageJsonInfoCache()
    {
        _perDirectoryResolutionCache.Clear();
        _nonRelativeNameResolutionCache.Clear();
    }

    public IPackageJsonInfoCache GetPackageJsonInfoCache() => _packageJsonInfoCache;
}

/// <summary>ported from: src/compiler/moduleNameResolver.ts:2223-2226 — <c>GetPackageJsonEntrypointsHost</c>.</summary>
internal interface IGetPackageJsonEntrypointsHost : IModuleResolutionHost
{
    List<string> ReadDirectory(string? rootDir, List<string>? extensions, List<string>? excludes, List<string>? includes, int? depth);
}

// ---------------------------------------------------------------------------------------------
// Constructible models the resolvers create by hand
// ---------------------------------------------------------------------------------------------

/// <summary>ported from: src/compiler/types.ts:8051-8066 — <c>PackageId</c>.</summary>
/// <remarks>
/// PORT-NOTE: TS declares <c>PackageId</c> as an interface and fills it in with an object literal in
/// <c>withPackageId</c>; §5 rule 6 makes it a concrete class. Every consumer declares its slot as the generated
/// <see cref="IPackageId"/>, so this class exists to be constructed and then read through that interface.
/// </remarks>
internal sealed class PackageId : IPackageId
{
    /// <summary>Name of the package. Should not include <c>@types</c>. If accessing a non-index file, this should include its name e.g. "foo/bar".</summary>
    public string? Name { get; set; }

    /// <summary>Name of a submodule within this package. May be "".</summary>
    public string? SubModuleName { get; set; }

    /// <summary>Version of the package, e.g. "1.2.3".</summary>
    public string? Version { get; set; }

    /// <summary>Sorted list of peer dependencies, or undefined if there are no peer dependencies.</summary>
    public string? PeerDependencies { get; set; }
}

// ported from: src/compiler/types.ts:7403-7572 (interface CompilerOptions, as the generated ICompilerOptions declares it)
/// <summary>
/// A settable <see cref="ICompilerOptions"/>: the concrete options object the reference builds with object literals.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS's <c>CompilerOptions</c> is an object literal everywhere it is *created* — there is no constructor in
/// the compiler — so the port, whose options model is the generated interface, needs one constructible type for the
/// three places that create an options object of their own (<c>getOptionsForLibraryResolution</c>,
/// <c>tryResolveJSModuleWorker</c>, <c>nodeNextJsonConfigResolver</c>) and for the "resolve again as if bundler" probe
/// (which is TS's <c>{ ...options, moduleResolution: Bundler }</c>, i.e. the copy constructor below): §5 rule 6. A
/// fully-populated options object keeps being the caller's business (M8's tsconfig reader), exactly as
/// <c>tests/TypeScript.Compiler.Tests/FakeCompilerOptions.cs</c> already records for the tests.
/// <para>
/// PORT-NOTE: one plain auto-property per slot, in the interface's own order — the same construction the test fake uses,
/// and for the same reason: a generator change that adds or drops a slot fails this file's compilation instead of silently
/// leaving a slot unread. Every slot defaults to unset (<c>null</c>), which is TS's <c>undefined</c>.
/// </para>
/// </remarks>
internal sealed class CompilerOptions : ICompilerOptions
{
    /// <summary>A fully unset options object — what TS's two-field object literals start from.</summary>
    public CompilerOptions()
    {
    }

    public bool? All { get; set; }
    public bool? AllowImportingTsExtensions { get; set; }
    public bool? AllowJs { get; set; }
    public bool? AllowNonTsExtensions { get; set; }
    public bool? AllowArbitraryExtensions { get; set; }
    public bool? AllowSyntheticDefaultImports { get; set; }
    public bool? AllowUmdGlobalAccess { get; set; }
    public bool? AllowUnreachableCode { get; set; }
    public bool? AllowUnusedLabels { get; set; }
    public bool? AlwaysStrict { get; set; }
    public string? BaseUrl { get; set; }
    public bool? Build { get; set; }
    public string? Charset { get; set; }
    public bool? CheckJs { get; set; }
    public string? ConfigFilePath { get; set; }
    public ITsConfigSourceFile? ConfigFile { get; set; }
    public List<string>? CustomConditions { get; set; }
    public bool? Declaration { get; set; }
    public bool? DeclarationMap { get; set; }
    public bool? EmitDeclarationOnly { get; set; }
    public string? DeclarationDir { get; set; }
    public bool? Diagnostics { get; set; }
    public bool? ExtendedDiagnostics { get; set; }
    public bool? DisableSizeLimit { get; set; }
    public bool? DisableSourceOfProjectReferenceRedirect { get; set; }
    public bool? DisableSolutionSearching { get; set; }
    public bool? DisableReferencedProjectLoad { get; set; }
    public bool? DownlevelIteration { get; set; }
    public bool? EmitBOM { get; set; }
    public bool? EmitDecoratorMetadata { get; set; }
    public bool? ExactOptionalPropertyTypes { get; set; }
    public bool? ExperimentalDecorators { get; set; }
    public bool? ForceConsistentCasingInFileNames { get; set; }
    public string? GenerateCpuProfile { get; set; }
    public string? GenerateTrace { get; set; }
    public bool? Help { get; set; }
    public string? IgnoreDeprecations { get; set; }
    public bool? ImportHelpers { get; set; }
    public ImportsNotUsedAsValues? ImportsNotUsedAsValues { get; set; }
    public bool? Init { get; set; }
    public bool? InlineSourceMap { get; set; }
    public bool? InlineSources { get; set; }
    public bool? IsolatedModules { get; set; }
    public bool? IsolatedDeclarations { get; set; }
    public JsxEmit? Jsx { get; set; }
    public bool? KeyofStringsOnly { get; set; }
    public List<string>? Lib { get; set; }
    public bool? LibReplacement { get; set; }
    public bool? ListEmittedFiles { get; set; }
    public bool? ListFiles { get; set; }
    public bool? ExplainFiles { get; set; }
    public bool? ListFilesOnly { get; set; }
    public string? Locale { get; set; }
    public string? MapRoot { get; set; }
    public int? MaxNodeModuleJsDepth { get; set; }
    public ModuleKind? Module { get; set; }
    public ModuleResolutionKind? ModuleResolution { get; set; }
    public List<string>? ModuleSuffixes { get; set; }
    public ModuleDetectionKind? ModuleDetection { get; set; }
    public NewLineKind? NewLine { get; set; }
    public bool? NoEmit { get; set; }
    public bool? NoCheck { get; set; }
    public bool? NoEmitForJsFiles { get; set; }
    public bool? NoEmitHelpers { get; set; }
    public bool? NoEmitOnError { get; set; }
    public bool? NoErrorTruncation { get; set; }
    public bool? NoFallthroughCasesInSwitch { get; set; }
    public bool? NoImplicitAny { get; set; }
    public bool? NoImplicitReturns { get; set; }
    public bool? NoImplicitThis { get; set; }
    public bool? NoStrictGenericChecks { get; set; }
    public bool? NoUnusedLocals { get; set; }
    public bool? NoUnusedParameters { get; set; }
    public bool? NoImplicitUseStrict { get; set; }
    public bool? NoPropertyAccessFromIndexSignature { get; set; }
    public bool? AssumeChangesOnlyAffectDirectDependencies { get; set; }
    public bool? NoLib { get; set; }
    public bool? NoResolve { get; set; }
    public bool? NoDtsResolution { get; set; }
    public bool? NoUncheckedIndexedAccess { get; set; }
    public string? Out { get; set; }
    public string? OutDir { get; set; }
    public string? OutFile { get; set; }
    public Dictionary<string, List<string>>? Paths { get; set; }
    public string? PathsBasePath { get; set; }
    public List<IPluginImport>? Plugins { get; set; }
    public bool? PreserveConstEnums { get; set; }
    public bool? NoImplicitOverride { get; set; }
    public bool? PreserveSymlinks { get; set; }
    public bool? PreserveValueImports { get; set; }
    public bool? PreserveWatchOutput { get; set; }
    public string? Project { get; set; }
    public bool? Pretty { get; set; }
    public string? ReactNamespace { get; set; }
    public string? JsxFactory { get; set; }
    public string? JsxFragmentFactory { get; set; }
    public string? JsxImportSource { get; set; }
    public bool? Composite { get; set; }
    public bool? Incremental { get; set; }
    public string? TsBuildInfoFile { get; set; }
    public bool? RemoveComments { get; set; }
    public bool? ResolvePackageJsonExports { get; set; }
    public bool? ResolvePackageJsonImports { get; set; }
    public bool? RewriteRelativeImportExtensions { get; set; }
    public string? RootDir { get; set; }
    public List<string>? RootDirs { get; set; }
    public bool? SkipLibCheck { get; set; }
    public bool? SkipDefaultLibCheck { get; set; }
    public bool? SourceMap { get; set; }
    public string? SourceRoot { get; set; }
    public bool? Strict { get; set; }
    public bool? StrictFunctionTypes { get; set; }
    public bool? StrictBindCallApply { get; set; }
    public bool? StrictNullChecks { get; set; }
    public bool? StrictPropertyInitialization { get; set; }
    public bool? StrictBuiltinIteratorReturn { get; set; }
    public bool? StableTypeOrdering { get; set; }
    public bool? StripInternal { get; set; }
    public bool? SuppressExcessPropertyErrors { get; set; }
    public bool? SuppressImplicitAnyIndexErrors { get; set; }
    public bool? SuppressOutputPathCheck { get; set; }
    public ScriptTarget? Target { get; set; }
    public bool? TraceResolution { get; set; }
    public bool? UseUnknownInCatchVariables { get; set; }
    public bool? NoUncheckedSideEffectImports { get; set; }
    public bool? ResolveJsonModule { get; set; }
    public List<string>? Types { get; set; }
    public List<string>? TypeRoots { get; set; }
    public bool? VerbatimModuleSyntax { get; set; }
    public bool? ErasableSyntaxOnly { get; set; }
    public bool? Version { get; set; }
    public bool? Watch { get; set; }
    public bool? EsModuleInterop { get; set; }
    public bool? ShowConfig { get; set; }
    public bool? IgnoreConfig { get; set; }
    public bool? UseDefineForClassFields { get; set; }
    public bool? TscBuild { get; set; }

    /// <summary>
    /// Copies every slot of another options object — what TS's <c>{ ...options, moduleResolution: … }</c> means.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS spreads the options object (sharing no state — every slot is a value or an array reference), which
    /// C# cannot do over an interface; copying slot by slot through the interface is the same operation, and it is what
    /// <c>nodeModuleNameResolverWorker</c> needs for its "resolve with moduleResolution: bundler" probe.
    /// </remarks>
    public CompilerOptions(ICompilerOptions other)
    {
        All = other.All;
        AllowImportingTsExtensions = other.AllowImportingTsExtensions;
        AllowJs = other.AllowJs;
        AllowNonTsExtensions = other.AllowNonTsExtensions;
        AllowArbitraryExtensions = other.AllowArbitraryExtensions;
        AllowSyntheticDefaultImports = other.AllowSyntheticDefaultImports;
        AllowUmdGlobalAccess = other.AllowUmdGlobalAccess;
        AllowUnreachableCode = other.AllowUnreachableCode;
        AllowUnusedLabels = other.AllowUnusedLabels;
        AlwaysStrict = other.AlwaysStrict;
        BaseUrl = other.BaseUrl;
        Build = other.Build;
        Charset = other.Charset;
        CheckJs = other.CheckJs;
        ConfigFilePath = other.ConfigFilePath;
        ConfigFile = other.ConfigFile;
        CustomConditions = other.CustomConditions;
        Declaration = other.Declaration;
        DeclarationMap = other.DeclarationMap;
        EmitDeclarationOnly = other.EmitDeclarationOnly;
        DeclarationDir = other.DeclarationDir;
        Diagnostics = other.Diagnostics;
        ExtendedDiagnostics = other.ExtendedDiagnostics;
        DisableSizeLimit = other.DisableSizeLimit;
        DisableSourceOfProjectReferenceRedirect = other.DisableSourceOfProjectReferenceRedirect;
        DisableSolutionSearching = other.DisableSolutionSearching;
        DisableReferencedProjectLoad = other.DisableReferencedProjectLoad;
        DownlevelIteration = other.DownlevelIteration;
        EmitBOM = other.EmitBOM;
        EmitDecoratorMetadata = other.EmitDecoratorMetadata;
        ExactOptionalPropertyTypes = other.ExactOptionalPropertyTypes;
        ExperimentalDecorators = other.ExperimentalDecorators;
        ForceConsistentCasingInFileNames = other.ForceConsistentCasingInFileNames;
        GenerateCpuProfile = other.GenerateCpuProfile;
        GenerateTrace = other.GenerateTrace;
        Help = other.Help;
        IgnoreDeprecations = other.IgnoreDeprecations;
        ImportHelpers = other.ImportHelpers;
        ImportsNotUsedAsValues = other.ImportsNotUsedAsValues;
        Init = other.Init;
        InlineSourceMap = other.InlineSourceMap;
        InlineSources = other.InlineSources;
        IsolatedModules = other.IsolatedModules;
        IsolatedDeclarations = other.IsolatedDeclarations;
        Jsx = other.Jsx;
        KeyofStringsOnly = other.KeyofStringsOnly;
        Lib = other.Lib;
        LibReplacement = other.LibReplacement;
        ListEmittedFiles = other.ListEmittedFiles;
        ListFiles = other.ListFiles;
        ExplainFiles = other.ExplainFiles;
        ListFilesOnly = other.ListFilesOnly;
        Locale = other.Locale;
        MapRoot = other.MapRoot;
        MaxNodeModuleJsDepth = other.MaxNodeModuleJsDepth;
        Module = other.Module;
        ModuleResolution = other.ModuleResolution;
        ModuleSuffixes = other.ModuleSuffixes;
        ModuleDetection = other.ModuleDetection;
        NewLine = other.NewLine;
        NoEmit = other.NoEmit;
        NoCheck = other.NoCheck;
        NoEmitForJsFiles = other.NoEmitForJsFiles;
        NoEmitHelpers = other.NoEmitHelpers;
        NoEmitOnError = other.NoEmitOnError;
        NoErrorTruncation = other.NoErrorTruncation;
        NoFallthroughCasesInSwitch = other.NoFallthroughCasesInSwitch;
        NoImplicitAny = other.NoImplicitAny;
        NoImplicitReturns = other.NoImplicitReturns;
        NoImplicitThis = other.NoImplicitThis;
        NoStrictGenericChecks = other.NoStrictGenericChecks;
        NoUnusedLocals = other.NoUnusedLocals;
        NoUnusedParameters = other.NoUnusedParameters;
        NoImplicitUseStrict = other.NoImplicitUseStrict;
        NoPropertyAccessFromIndexSignature = other.NoPropertyAccessFromIndexSignature;
        AssumeChangesOnlyAffectDirectDependencies = other.AssumeChangesOnlyAffectDirectDependencies;
        NoLib = other.NoLib;
        NoResolve = other.NoResolve;
        NoDtsResolution = other.NoDtsResolution;
        NoUncheckedIndexedAccess = other.NoUncheckedIndexedAccess;
        Out = other.Out;
        OutDir = other.OutDir;
        OutFile = other.OutFile;
        Paths = other.Paths;
        PathsBasePath = other.PathsBasePath;
        Plugins = other.Plugins;
        PreserveConstEnums = other.PreserveConstEnums;
        NoImplicitOverride = other.NoImplicitOverride;
        PreserveSymlinks = other.PreserveSymlinks;
        PreserveValueImports = other.PreserveValueImports;
        PreserveWatchOutput = other.PreserveWatchOutput;
        Project = other.Project;
        Pretty = other.Pretty;
        ReactNamespace = other.ReactNamespace;
        JsxFactory = other.JsxFactory;
        JsxFragmentFactory = other.JsxFragmentFactory;
        JsxImportSource = other.JsxImportSource;
        Composite = other.Composite;
        Incremental = other.Incremental;
        TsBuildInfoFile = other.TsBuildInfoFile;
        RemoveComments = other.RemoveComments;
        ResolvePackageJsonExports = other.ResolvePackageJsonExports;
        ResolvePackageJsonImports = other.ResolvePackageJsonImports;
        RewriteRelativeImportExtensions = other.RewriteRelativeImportExtensions;
        RootDir = other.RootDir;
        RootDirs = other.RootDirs;
        SkipLibCheck = other.SkipLibCheck;
        SkipDefaultLibCheck = other.SkipDefaultLibCheck;
        SourceMap = other.SourceMap;
        SourceRoot = other.SourceRoot;
        Strict = other.Strict;
        StrictFunctionTypes = other.StrictFunctionTypes;
        StrictBindCallApply = other.StrictBindCallApply;
        StrictNullChecks = other.StrictNullChecks;
        StrictPropertyInitialization = other.StrictPropertyInitialization;
        StrictBuiltinIteratorReturn = other.StrictBuiltinIteratorReturn;
        StableTypeOrdering = other.StableTypeOrdering;
        StripInternal = other.StripInternal;
        SuppressExcessPropertyErrors = other.SuppressExcessPropertyErrors;
        SuppressImplicitAnyIndexErrors = other.SuppressImplicitAnyIndexErrors;
        SuppressOutputPathCheck = other.SuppressOutputPathCheck;
        Target = other.Target;
        TraceResolution = other.TraceResolution;
        UseUnknownInCatchVariables = other.UseUnknownInCatchVariables;
        NoUncheckedSideEffectImports = other.NoUncheckedSideEffectImports;
        ResolveJsonModule = other.ResolveJsonModule;
        Types = other.Types;
        TypeRoots = other.TypeRoots;
        VerbatimModuleSyntax = other.VerbatimModuleSyntax;
        ErasableSyntaxOnly = other.ErasableSyntaxOnly;
        Version = other.Version;
        Watch = other.Watch;
        EsModuleInterop = other.EsModuleInterop;
        ShowConfig = other.ShowConfig;
        IgnoreConfig = other.IgnoreConfig;
        UseDefineForClassFields = other.UseDefineForClassFields;
        TscBuild = other.TscBuild;
    }
}
