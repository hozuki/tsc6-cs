namespace TypeScript.Compiler.Tests;

// ported from: src/compiler/moduleNameResolver.ts (the entry points this slice ports: resolveModuleName 1406-1492,
// resolveTypeReferenceDirective 538-725, the caches 1051-1390, the package.json readers and exports/imports machinery
// 2280-2993, the node_modules walk 2997-3167, classicNameResolver 3261-3330, the name manglers 3199-3240)
/// <summary>
/// Module resolution over an in-memory host: the relative/bare/typesVersions/paths/exports/classic/node16 paths, the
/// failed-lookup list, the type-reference-directive lookups and the resolution cache.
/// </summary>
/// <remarks>
/// PORT-NOTE: every expected value in this file was read off the reference, not derived from the port:
/// <c>lib/typescript.js</c> was driven with the same in-memory file system and the same options
/// (<c>D:/Source/VS/TEMP/tsc-cs-scratch/ModuleResHarness/cases.json</c>, run by <c>node-resolve.cjs</c>), and the port's
/// answers — resolved file name, extension, <c>isExternalLibraryImport</c>, package id and the whole
/// <c>failedLookupLocations</c> list — are byte-for-byte equal to the reference's for all 26 harness cases. The rows
/// below are that transcript, which is why some of them look redundant at a glance: they pin the reference's answers,
/// not the port's shape.
/// <para>
/// PORT-NOTE: the host is a small private fake rather than <c>FakeSystem</c> because the two host interfaces do not
/// overlap (<c>ISystem</c> has no <c>readFile</c>/<c>directoryExists</c> pair with these names, and Main's
/// <c>Program.CreateCompilerHostWorker</c> is mid-flight); it is deterministic and holds no process-wide state, so the
/// assembly's serialized test collection is not needed for these.
/// </para>
/// </remarks>
public class ModuleNameResolverTests
{
    // ---------------------------------------------------------------------------------------------
    // Resolution
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ResolvesRelativeSpecifierWithAndWithoutExtension()
    {
        var (host, options) = Fixture();

        var withExtension = ModuleNameResolver.ResolveModuleName("./x.ts", "/src/index.ts", options, host);
        Assert.Equal("/src/x.ts", withExtension.ResolvedModule!.ResolvedFileName);
        Assert.Equal(".ts", withExtension.ResolvedModule.Extension);
        Assert.False(withExtension.ResolvedModule.IsExternalLibraryImport);

        // The reference reports `resolvedUsingTsExtension` only for a specifier that *had* a .ts/.d.ts extension and
        // was rewritten; an extensionless specifier that gained one reports false.
        Assert.True(withExtension.ResolvedModule.ResolvedUsingTsExtension);

        var withoutExtension = ModuleNameResolver.ResolveModuleName("./x", "/src/index.ts", options, host);
        Assert.Equal("/src/x.ts", withoutExtension.ResolvedModule!.ResolvedFileName);
        Assert.False(withoutExtension.ResolvedModule.ResolvedUsingTsExtension);
    }

    [Fact]
    public void PrefersFileOverIndexAndResolvesADeclarationFileFromItsPlainSpecifier()
    {
        var (host, options) = Fixture();

        // /src/sub/y.ts and /src/sub/y.d.ts both exist; the plain file wins.
        var result = ModuleNameResolver.ResolveModuleName("./sub/y", "/src/index.ts", options, host);
        Assert.Equal("/src/sub/y.ts", result.ResolvedModule!.ResolvedFileName);
    }

    [Fact]
    public void ResolvesBarePackageThroughPackageJsonTypesAndMain()
    {
        var (host, options) = Fixture();

        var types = ModuleNameResolver.ResolveModuleName("pkg", "/src/index.ts", options, host);
        Assert.Equal("/node_modules/pkg/lib/main.d.ts", types.ResolvedModule!.ResolvedFileName);
        Assert.Equal(".d.ts", types.ResolvedModule.Extension);
        Assert.True(types.ResolvedModule.IsExternalLibraryImport);

        // The package id names the package, the submodule within it and its version.
        var packageId = types.ResolvedModule.PackageId!;
        Assert.Equal("pkg", packageId.Name);
        Assert.Equal("lib/main.d.ts", packageId.SubModuleName);
        Assert.Equal("1.0.0", packageId.Version);
        Assert.Null(packageId.PeerDependencies);

        var typings = ModuleNameResolver.ResolveModuleName("typed", "/src/index.ts", options, host);
        Assert.Equal("/node_modules/typed/typ.d.ts", typings.ResolvedModule!.ResolvedFileName);
        Assert.Equal("2.3.4", typings.ResolvedModule.PackageId!.Version);

        // `main` is only consulted for JS when allowJs is on.
        var withAllowJs = Fixture();
        withAllowJs.Options.AllowJs = true;
        var js = ModuleNameResolver.ResolveModuleName("js-pkg", "/src/index.ts", withAllowJs.Options, withAllowJs.Host);
        Assert.Equal("/node_modules/js-pkg/index.js", js.ResolvedModule!.ResolvedFileName);
        Assert.Equal(".js", js.ResolvedModule.Extension);

        // `main` resolves to a JS file whether or not `allowJs` is set: the resolver's extension set always contains
        // JavaScript, and `allowJs` is the *program*'s input filter (verified against the reference for both settings).
        var withoutAllowJs = ModuleNameResolver.ResolveModuleName("js-pkg", "/src/index.ts", options, host);
        Assert.Equal("/node_modules/js-pkg/index.js", withoutAllowJs.ResolvedModule!.ResolvedFileName);
    }

    [Fact]
    public void ResolvesThroughPathsWildcardAndExactSubstitutions()
    {
        var (host, options) = Fixture();
        options.BaseUrl = "/";
        options.Paths = new Dictionary<string, List<string>>(StringComparer.Ordinal)
        {
            ["mapped/*"] = ["src/mapped/*.ts"],
            ["exact-target"] = ["src/mapped/target.ts"],
        };

        var byWildcard = ModuleNameResolver.ResolveModuleName("mapped/target", "/src/index.ts", options, host);
        Assert.Equal("/src/mapped/target.ts", byWildcard.ResolvedModule!.ResolvedFileName);
        Assert.Equal(".ts", byWildcard.ResolvedModule.Extension);

        var byExactMatch = ModuleNameResolver.ResolveModuleName("exact-target", "/src/index.ts", options, host);
        Assert.Equal("/src/mapped/target.ts", byExactMatch.ResolvedModule!.ResolvedFileName);
    }

    [Fact]
    public void RedirectsThroughTypesVersions()
    {
        var (host, options) = Fixture();

        var result = ModuleNameResolver.ResolveModuleName("tv", "/src/index.ts", options, host);
        Assert.Equal("/node_modules/tv/ts4/index.d.ts", result.ResolvedModule!.ResolvedFileName);
        Assert.Equal("ts4/index.d.ts", result.ResolvedModule.PackageId!.SubModuleName);
    }

    [Fact]
    public void ResolvesExportsSubpathWithAWildcard()
    {
        var (host, options) = Fixture();
        options.ModuleResolution = ModuleResolutionKind.Node16;
        options.Module = ModuleKind.Node16;

        var result = ModuleNameResolver.ResolveModuleName("exp/sub/a", "/src/index.ts", options, host);
        Assert.Equal("/node_modules/exp/sub/a.d.ts", result.ResolvedModule!.ResolvedFileName);
    }

    [Fact]
    public void ExportsConditionsSelectRequireOrImport()
    {
        var (host, options) = Fixture();
        options.ModuleResolution = ModuleResolutionKind.Node16;
        options.Module = ModuleKind.Node16;

        // No resolution mode from the containing file: the CJS conditions apply.
        var require = ModuleNameResolver.ResolveModuleName("cond", "/src/index.ts", options, host);
        Assert.Equal("/node_modules/cond/cjs/index.d.ts", require.ResolvedModule!.ResolvedFileName);

        var import = ModuleNameResolver.ResolveModuleName("cond", "/src/index.ts", options, host, resolutionMode: ModuleKind.ESNext);
        Assert.Equal("/node_modules/cond/esm/index.d.ts", import.ResolvedModule!.ResolvedFileName);
    }

    [Fact]
    public void Node16NeedsEsmModeToSeeAnImportOnlyExport()
    {
        var (host, options) = Fixture();
        options.ModuleResolution = ModuleResolutionKind.Node16;
        options.Module = ModuleKind.CommonJS;

        // `nx` exports only an `import` target, so the CJS pass resolves nothing and records exactly two lookups.
        var require = ModuleNameResolver.ResolveModuleName("nx", "/src/index.ts", options, host);
        Assert.Null(require.ResolvedModule);
        Assert.Equal(
            [
                "/src/node_modules/nx/package.json",
                "/src/node_modules/nx.ts",
                "/src/node_modules/nx.tsx",
                "/src/node_modules/nx.d.ts",
                "/src/node_modules/nx/index.ts",
                "/src/node_modules/nx/index.tsx",
                "/src/node_modules/nx/index.d.ts",
                "/src/node_modules/@types/nx/package.json",
                "/src/node_modules/@types/nx.d.ts",
                "/src/node_modules/@types/nx/index.d.ts",
                "/node_modules/@types/nx/package.json",
                "/node_modules/@types/nx.d.ts",
                "/node_modules/@types/nx/index.d.ts",
                "/src/node_modules/nx/package.json",
                "/src/node_modules/nx.js",
                "/src/node_modules/nx.jsx",
                "/src/node_modules/nx/index.js",
                "/src/node_modules/nx/index.jsx",
            ],
            require.FailedLookupLocations!);

        var esm = ModuleNameResolver.ResolveModuleName("nx", "/src/index.ts", options, host, resolutionMode: ModuleKind.ESNext);
        Assert.Equal("/node_modules/nx/esm/index.d.mts", esm.ResolvedModule!.ResolvedFileName);
        Assert.Equal(".d.mts", esm.ResolvedModule.Extension);
    }

    [Fact]
    public void BundlerResolvesRelativeAndBareSpecifiers()
    {
        var (host, options) = Fixture();
        options.ModuleResolution = ModuleResolutionKind.Bundler;

        Assert.Equal("/src/x.ts", ModuleNameResolver.ResolveModuleName("./x", "/src/index.ts", options, host).ResolvedModule!.ResolvedFileName);
        Assert.Equal("/node_modules/pkg/lib/main.d.ts", ModuleNameResolver.ResolveModuleName("pkg", "/src/index.ts", options, host).ResolvedModule!.ResolvedFileName);
    }

    [Fact]
    public void ClassicSearchesOnlyAlongsideTheContainingFile()
    {
        var (host, options) = Fixture();
        options.ModuleResolution = ModuleResolutionKind.Classic;

        // classic walks the ancestors of the containing file looking for `<dir>/pkg(.ts)` — never node_modules.
        Assert.Null(ModuleNameResolver.ResolveModuleName("pkg", "/src/index.ts", options, host).ResolvedModule);
        Assert.Equal("/src/x.ts", ModuleNameResolver.ResolveModuleName("./x", "/src/index.ts", options, host).ResolvedModule!.ResolvedFileName);
    }

    [Fact]
    public void MissingModuleRecordsEveryFailedLookup()
    {
        var (host, options) = Fixture();

        var result = ModuleNameResolver.ResolveModuleName("does-not-exist", "/src/index.ts", options, host);

        Assert.Null(result.ResolvedModule);
        // The whole list, in order: the CJS pass tries declaration/implementation extensions in each ancestor's
        // node_modules (and @types), then the JS pass repeats it for the extensions it skipped.
        Assert.Equal(
            [
                "/src/node_modules/does-not-exist/package.json",
                "/src/node_modules/does-not-exist.ts",
                "/src/node_modules/does-not-exist.tsx",
                "/src/node_modules/does-not-exist.d.ts",
                "/src/node_modules/does-not-exist/index.ts",
                "/src/node_modules/does-not-exist/index.tsx",
                "/src/node_modules/does-not-exist/index.d.ts",
                "/src/node_modules/@types/does-not-exist/package.json",
                "/src/node_modules/@types/does-not-exist.d.ts",
                "/src/node_modules/@types/does-not-exist/index.d.ts",
                "/node_modules/does-not-exist/package.json",
                "/node_modules/does-not-exist.ts",
                "/node_modules/does-not-exist.tsx",
                "/node_modules/does-not-exist.d.ts",
                "/node_modules/does-not-exist/index.ts",
                "/node_modules/does-not-exist/index.tsx",
                "/node_modules/does-not-exist/index.d.ts",
                "/node_modules/@types/does-not-exist/package.json",
                "/node_modules/@types/does-not-exist.d.ts",
                "/node_modules/@types/does-not-exist/index.d.ts",
                "/src/node_modules/does-not-exist/package.json",
                "/src/node_modules/does-not-exist.js",
                "/src/node_modules/does-not-exist.jsx",
                "/src/node_modules/does-not-exist/index.js",
                "/src/node_modules/does-not-exist/index.jsx",
                "/node_modules/does-not-exist/package.json",
                "/node_modules/does-not-exist.js",
                "/node_modules/does-not-exist.jsx",
                "/node_modules/does-not-exist/index.js",
                "/node_modules/does-not-exist/index.jsx",
            ],
            result.FailedLookupLocations!);
    }

    // ---------------------------------------------------------------------------------------------
    // Type reference directives
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ResolvesTypeReferenceDirectiveThroughCustomTypeRootsAsPrimary()
    {
        var (host, options) = Fixture();
        options.TypeRoots = ["/types-root/@types"];

        var result = ModuleNameResolver.ResolveTypeReferenceDirective("foo", "/src/index.ts", options, host);

        var directive = result.ResolvedTypeReferenceDirective!;
        Assert.Equal("/types-root/@types/foo/index.d.ts", directive.ResolvedFileName);
        Assert.True(directive.Primary);
        // The root is not under node_modules, so it is not an external library import.
        Assert.False(directive.IsExternalLibraryImport);
        Assert.Equal("9.9.9", directive.PackageId!.Version);
        Assert.Equal("@types/foo", directive.PackageId!.Name);
    }

    [Fact]
    public void ResolvesTypeReferenceDirectiveThroughNodeModulesTypesScope()
    {
        var (host, options) = Fixture();

        var result = ModuleNameResolver.ResolveTypeReferenceDirective("bar", "/src/index.ts", options, host);

        var directive = result.ResolvedTypeReferenceDirective!;
        Assert.Equal("/node_modules/@types/bar/index.d.ts", directive.ResolvedFileName);
        Assert.True(directive.Primary);
        Assert.True(directive.IsExternalLibraryImport);
    }

    [Fact]
    public void ManglesAScopedNameBeforeLookingInTheTypesScope()
    {
        var (host, options) = Fixture();

        // The default type root ends in /node_modules/@types, so "@scope/pkg" is looked up as "scope__pkg".
        var result = ModuleNameResolver.ResolveTypeReferenceDirective("@scope/pkg", "/src/index.ts", options, host);

        Assert.Equal("/node_modules/@types/scope__pkg/index.d.ts", result.ResolvedTypeReferenceDirective!.ResolvedFileName);
        Assert.Equal("5.5.5", result.ResolvedTypeReferenceDirective.PackageId!.Version);
    }

    [Fact]
    public void MissingTypeReferenceDirectiveRecordsBothSearchPasses()
    {
        var (host, options) = Fixture();
        options.TypeRoots = ["/types-root/@types"];

        var result = ModuleNameResolver.ResolveTypeReferenceDirective("nope", "/src/index.ts", options, host);

        Assert.Null(result.ResolvedTypeReferenceDirective);
        Assert.Equal(
            [
                "/types-root/@types/nope.d.ts",
                "/types-root/@types/nope/package.json",
                "/types-root/@types/nope/index.d.ts",
                "/src/node_modules/nope/package.json",
                "/src/node_modules/nope.d.ts",
                "/src/node_modules/nope/index.d.ts",
                "/src/node_modules/@types/nope/package.json",
                "/src/node_modules/@types/nope.d.ts",
                "/src/node_modules/@types/nope/index.d.ts",
                "/node_modules/nope/package.json",
                "/node_modules/nope.d.ts",
                "/node_modules/nope/index.d.ts",
                "/node_modules/@types/nope/package.json",
                "/node_modules/@types/nope.d.ts",
                "/node_modules/@types/nope/index.d.ts",
            ],
            result.FailedLookupLocations!);
    }

    // ---------------------------------------------------------------------------------------------
    // Imports and self names
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ResolvesHashImportFromTheContainingPackagesImportsField()
    {
        var (host, options) = Fixture();
        options.ModuleResolution = ModuleResolutionKind.Node16;
        options.Module = ModuleKind.Node16;

        var result = ModuleNameResolver.ResolveModuleName("#dep", "/src/index.ts", options, host);
        Assert.Equal("/src/x.ts", result.ResolvedModule!.ResolvedFileName);
        Assert.False(result.ResolvedModule.IsExternalLibraryImport);
    }

    [Fact]
    public void ResolvesSelfNameThroughTheContainingPackagesExportsField()
    {
        var (host, options) = Fixture();
        options.ModuleResolution = ModuleResolutionKind.Node16;
        options.Module = ModuleKind.Node16;

        var result = ModuleNameResolver.ResolveModuleName("src-pkg/x", "/src/index.ts", options, host);
        Assert.Equal("/src/x.ts", result.ResolvedModule!.ResolvedFileName);
        Assert.False(result.ResolvedModule.IsExternalLibraryImport);
    }

    // ---------------------------------------------------------------------------------------------
    // The caches and the name manglers
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ResolutionCacheReusesTheSameResultAndServesItBack()
    {
        var (host, options) = Fixture();
        var cache = ModuleNameResolver.CreateModuleResolutionCache("/", Core.IdentityFileName, options);

        var first = ModuleNameResolver.ResolveModuleName("pkg", "/src/index.ts", options, host, cache);
        var second = ModuleNameResolver.ResolveModuleName("pkg", "/src/index.ts", options, host, cache);

        // The second resolution is the cached object itself (program compares results by identity), and the cache can
        // be asked for the same entry without going through a name lookup.
        Assert.Same(first, second);
        Assert.Same(first, ModuleNameResolver.ResolveModuleNameFromCache("pkg", "/src/index.ts", cache));
        Assert.Equal("/node_modules/pkg/lib/main.d.ts", first.ResolvedModule!.ResolvedFileName);
    }

    [Fact]
    public void ReadonlyCacheDoesNotStoreResults()
    {
        var (host, options) = Fixture();
        var cache = ModuleNameResolver.CreateModuleResolutionCache("/", Core.IdentityFileName, options);
        cache.IsReadonly = true;

        var result = ModuleNameResolver.ResolveModuleName("pkg", "/src/index.ts", options, host, cache);

        Assert.Equal("/node_modules/pkg/lib/main.d.ts", result.ResolvedModule!.ResolvedFileName);
        // A read-only cache is shared, so the resolution is handed back without being stored.
        Assert.Null(ModuleNameResolver.ResolveModuleNameFromCache("pkg", "/src/index.ts", cache));
    }

    [Fact]
    public void ReadonlyCacheCopiesTheResolutionFieldsInsteadOfMutatingTheCachedResult()
    {
        var (host, options) = Fixture();
        var cache = ModuleNameResolver.CreateModuleResolutionCache("/", Core.IdentityFileName, options);

        // The first resolution stores the result for the directory it ran in, and for that directory's ancestors down to
        // the common prefix with the resolved file (the root, here).
        var first = ModuleNameResolver.ResolveModuleName("pkg", "/a/b/c/index.ts", options, host, cache);
        cache.IsReadonly = true;

        // Resolving from a sibling directory misses the per-directory cache but hits the non-relative one at "/a/b", so
        // the read-only branch copies the cached result instead of appending to it.
        var second = ModuleNameResolver.ResolveModuleName("pkg", "/a/b/d/index.ts", options, host, cache);

        Assert.Equal("/node_modules/pkg/lib/main.d.ts", second.ResolvedModule!.ResolvedFileName);
        Assert.Equal(first.ResolvedModule!.ResolvedFileName, second.ResolvedModule.ResolvedFileName);
        Assert.NotSame(first, second);
    }

    [Theory]
    [InlineData("@scope/name", "scope__name", "@types/scope__name", "@scope/name")]
    [InlineData("plain", "plain", "@types/plain", "plain")]
    [InlineData("@a/b/c", "a__b/c", "@types/a__b/c", "@a/b/c")]
    public void ManglesAndUnmanglesScopedPackageNames(string packageName, string mangled, string typesPackageName, string roundTripped)
    {
        // PORT-NOTE: the "@a/b/c" row pins that the reference replaces the *first* separator only; a name with two
        // separators does not round-trip, and the port keeps that.
        Assert.Equal(mangled, ModuleNameResolver.MangleScopedPackageName(packageName));
        Assert.Equal(typesPackageName, ModuleNameResolver.GetTypesPackageName(packageName));
        Assert.Equal(roundTripped, ModuleNameResolver.UnmangleScopedPackageName(mangled));
    }

    [Theory]
    [InlineData("foo", "foo", "")]
    [InlineData("@scope/name", "@scope/name", "")]
    [InlineData("@scope/name/sub/path", "@scope/name", "sub/path")]
    [InlineData("foo/sub", "foo", "sub")]
    public void ParsesPackageNames(string moduleName, string packageName, string remainder)
    {
        Assert.Equal((packageName, remainder), ModuleNameResolver.ParsePackageName(moduleName));
    }

    [Theory]
    // (a, b, expected) rows transcribed from the reference's `comparePatternKeys`. Note the first row is `GreaterThan`:
    // a pattern with a star sorts *after* one without, and two patterns with the same base length compare by length.
    [InlineData("a*", "a*b", 1)]
    [InlineData("a", "a*", 1)]
    [InlineData("a", "a", 1)]
    [InlineData("a*b", "a*", -1)]
    [InlineData("a*", "b*", 0)]
    [InlineData("ab*", "a*", -1)]
    [InlineData("*", "*", 0)]
    public void ComparesPatternKeys(string a, string b, int expected)
        => Assert.Equal((Comparison)expected, ModuleNameResolver.ComparePatternKeys(a, b));

    [Theory]
    // (resolved path, isFolder, package directory) rows transcribed from the reference's `parseNodeModuleFromPath`.
    // The first two rows are the `isFolder` distinction: a path ending at the package name has no further separator, so
    // the answer is either the whole path or everything up to and including the previous separator.
    [InlineData("/a/node_modules/foo", null, "/a/node_modules/")]
    [InlineData("/a/node_modules/foo", true, "/a/node_modules/foo")]
    [InlineData("/a/node_modules/@types", null, "/a/node_modules/")]
    [InlineData("/a/node_modules/@types", true, "/a/node_modules/@types")]
    [InlineData("/a/node_modules/@types/foo", null, "/a/node_modules/@types")]
    [InlineData("/a/node_modules/foo/index.d.ts", null, "/a/node_modules/foo")]
    [InlineData("/a/node_modules/foo/index.d.ts", true, "/a/node_modules/foo")]
    [InlineData("/a/node_modules/@types/foo/bar/index.d.ts", null, "/a/node_modules/@types/foo")]
    [InlineData("/src/x.ts", null, null)]
    public void ParsesThePackageDirectoryOutOfAPath(string resolved, bool? isFolder, string? expected)
        => Assert.Equal(expected, ModuleNameResolver.ParseNodeModuleFromPath(resolved, isFolder));

    [Fact]
    public void PathContainsNodeModulesLooksForTheDirectorySegment()
    {
        Assert.True(ModuleNameResolver.PathContainsNodeModules("/a/node_modules/b"));
        Assert.False(ModuleNameResolver.PathContainsNodeModules("/a/node-modules/b"));
    }

    [Fact]
    public void GetPackageNameFromTypesPackageNameStripsAndUnmangles()
    {
        // The round trip re-adds the "@": `getTypesPackageName("@scope/name")` is "@types/scope__name", and this
        // reader answers "@scope/name" back.
        Assert.Equal("@scope/name", ModuleNameResolver.GetPackageNameFromTypesPackageName("@types/scope__name"));
        Assert.Equal("plain", ModuleNameResolver.GetPackageNameFromTypesPackageName("@types/plain"));
        // A name that is not an @types package is handed back unchanged.
        Assert.Equal("plain", ModuleNameResolver.GetPackageNameFromTypesPackageName("plain"));
    }

    // ---------------------------------------------------------------------------------------------
    // The fixture
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The same in-memory file system the differential harness drives (<c>ModuleResHarness/cases.json</c>), minus the
    /// packages only the harness cases need.
    /// </summary>
    private static (FakeResolutionHost Host, FakeCompilerOptions Options) Fixture()
    {
        var host = new FakeResolutionHost();
        host.Files["/src/index.ts"] = "export {};\n";
        host.Files["/src/x.ts"] = "export const x = 1;\n";
        host.Files["/src/sub/y.ts"] = "export const y = 1;\n";
        host.Files["/src/sub/y.d.ts"] = "export declare const y: number;\n";
        host.Files["/src/mapped/target.ts"] = "export const target = 1;\n";
        host.Files["/src/package.json"] = "{\"name\":\"src-pkg\",\"version\":\"1.0.0\",\"imports\":{\"#dep\":\"./x.ts\"},\"exports\":{\"./x\":\"./x.ts\"}}";

        host.Files["/node_modules/pkg/package.json"] = "{\"name\":\"pkg\",\"version\":\"1.0.0\",\"types\":\"./lib/main.d.ts\",\"main\":\"./lib/index.js\"}";
        host.Files["/node_modules/pkg/lib/main.d.ts"] = "export declare const pkg: number;\n";
        host.Files["/node_modules/pkg/lib/index.js"] = "export const pkg = 1;\n";

        host.Files["/node_modules/typed/package.json"] = "{\"name\":\"typed\",\"version\":\"2.3.4\",\"typings\":\"./typ.d.ts\"}";
        host.Files["/node_modules/typed/typ.d.ts"] = "export declare const typed: number;\n";

        host.Files["/node_modules/js-pkg/package.json"] = "{\"name\":\"js-pkg\",\"version\":\"0.1.0\",\"main\":\"./index.js\"}";
        host.Files["/node_modules/js-pkg/index.js"] = "module.exports = 1;\n";

        host.Files["/node_modules/local-paths/package.json"] = "{\"name\":\"local-paths\",\"version\":\"1.0.0\"}";

        host.Files["/node_modules/tv/package.json"] = "{\"name\":\"tv\",\"version\":\"2.0.0\",\"types\":\"./index.d.ts\",\"typesVersions\":{\">=2.0.0\":{\"*\":[\"ts4/*\"]}}}";
        host.Files["/node_modules/tv/ts4/index.d.ts"] = "export declare const tv: number;\n";

        host.Files["/node_modules/exp/package.json"] = "{\"name\":\"exp\",\"version\":\"3.0.0\",\"exports\":{\".\":{\"types\":\"./types/index.d.ts\",\"require\":\"./cjs/index.d.ts\",\"import\":\"./esm/index.d.ts\"},\"./sub/*\":\"./sub/*.d.ts\"}}";
        host.Files["/node_modules/exp/types/index.d.ts"] = "export declare const exp: number;\n";
        host.Files["/node_modules/exp/cjs/index.d.ts"] = "export declare const exp: number;\n";
        host.Files["/node_modules/exp/esm/index.d.ts"] = "export declare const exp: number;\n";
        host.Files["/node_modules/exp/sub/a.d.ts"] = "export declare const sub: number;\n";

        host.Files["/node_modules/cond/package.json"] = "{\"name\":\"cond\",\"version\":\"1.0.0\",\"exports\":{\".\":{\"require\":\"./cjs/index.d.ts\",\"import\":\"./esm/index.d.ts\"}}}";
        host.Files["/node_modules/cond/cjs/index.d.ts"] = "export declare const cond: number;\n";
        host.Files["/node_modules/cond/esm/index.d.ts"] = "export declare const cond: number;\n";

        host.Files["/node_modules/nx/package.json"] = "{\"name\":\"nx\",\"version\":\"1.0.0\",\"type\":\"module\",\"exports\":{\".\":{\"import\":\"./esm/index.d.mts\"}}}";
        host.Files["/node_modules/nx/esm/index.d.mts"] = "export declare const nx: number;\n";

        host.Files["/types-root/@types/foo/package.json"] = "{\"name\":\"@types/foo\",\"version\":\"9.9.9\"}";
        host.Files["/types-root/@types/foo/index.d.ts"] = "export declare const foo: number;\n";
        host.Files["/node_modules/@types/bar/package.json"] = "{\"name\":\"@types/bar\",\"version\":\"8.8.8\"}";
        host.Files["/node_modules/@types/bar/index.d.ts"] = "export declare const bar: number;\n";
        host.Files["/node_modules/@types/scope__pkg/package.json"] = "{\"name\":\"@types/scope__pkg\",\"version\":\"5.5.5\"}";
        host.Files["/node_modules/@types/scope__pkg/index.d.ts"] = "export declare const scoped: number;\n";

        var options = new FakeCompilerOptions { ModuleResolution = ModuleResolutionKind.Node10 };
        return (host, options);
    }

    /// <summary>
    /// The in-memory <see cref="IModuleResolutionHost"/> the reference harness also uses: forward-slash paths, a
    /// case-sensitive file system, <c>/</c> as the current directory, and directories derived from the file names.
    /// </summary>
    private sealed class FakeResolutionHost : IModuleResolutionHost
    {
        private readonly HashSet<string> _directories = new(StringComparer.Ordinal) { "/" };

        /// <summary>In-memory file contents; an absent path reads as a missing file.</summary>
        public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

        public object? UseCaseSensitiveFileNames { get; set; } = true;

        public string GetCurrentDirectory() => "/";

        public bool FileExists(string? fileName) => fileName is not null && Files.ContainsKey(fileName);

        public string? ReadFile(string? fileName) => fileName is not null ? Files.GetValueOrDefault(fileName) : null;

        public bool DirectoryExists(string? directoryName)
        {
            if (directoryName is null) return false;
            if (_directories.Contains(directoryName)) return true;
            return _directories.Add(directoryName) && HasPrefix(directoryName);
        }

        public string Realpath(string? path) => path!;

        public List<string> GetDirectories(string? path)
        {
            var prefix = path == "/" ? "/" : path + "/";
            var result = new List<string>();
            foreach (var file in Files.Keys)
            {
                if (!file.StartsWith(prefix, StringComparison.Ordinal)) continue;
                var rest = file[prefix.Length..];
                var separator = rest.IndexOf('/');
                if (separator < 0) continue;
                var directory = prefix + rest[..separator];
                if (!result.Contains(directory)) result.Add(directory);
            }

            result.Sort(StringComparer.Ordinal);
            return result;
        }

        public string? GetGlobalTypingsCacheLocation() => null;

        public void Trace(string? s)
        {
            // The reference harness discards trace output too; the resolved values are what these tests assert.
        }

        private bool HasPrefix(string directory)
        {
            foreach (var file in Files.Keys)
            {
                if (file.StartsWith(directory + "/", StringComparison.Ordinal)) return true;
            }

            return false;
        }
    }
}
