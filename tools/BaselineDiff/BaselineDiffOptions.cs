namespace TypeScript.Compiler.Tools;

// ported from: src/harness/harnessIO.ts:255-273 (harnessOptionDeclarations) and
// src/compiler/commandLineParser.ts:1184-2900 (optionDeclarations) — the option model Compiler.compileFiles builds a
// program from.
/// <summary>
/// The reference's own option declarations, as the driver needs them: one entry per `// @` directive name, carrying
/// the three things <c>optionValue</c> (harnessIO.ts:302-325) reads — the declared type, whether a string value names
/// a path, and the name→value table of a custom type.
/// </summary>
/// <remarks>
/// <para>
/// The table is derived from <c>lib/typescript.js</c> 6.0.3's own <c>optionDeclarations</c>, which is the same object
/// the harness concatenates its extras onto (<c>getCommandLineOption</c> indexes
/// <c>harnessOptionDeclarations.concat(ts.optionDeclarations)</c> by lower-cased name, later entries winning, so the
/// reference's declaration wins a collision exactly as it does here). Deriving it rather than transcribing it by hand
/// is the point: the option model is the reference's, and a hand-written table would be a second model to keep in
/// step with it. <c>tools/BaselineDiff/ts-baseline.cjs</c> reads the same table directly out of the loaded reference,
/// so the two halves agree by construction; the report's configuration check — the derived baseline stems against the
/// checked-in ones — is what catches the halves drifting apart.
/// </para>
/// <para>
/// PORT-NOTE (<c>HarnessOptionKind.Object</c>): <c>paths</c> and <c>plugins</c> are object-typed, and the harness's
/// <c>optionValue</c> falls through to <c>parseCustomTypeOption</c> for them, which reads <c>opt.type.get</c> — a call
/// on a non-Map that throws. A directive that reaches such an option is therefore reported as unmodelled by the driver
/// instead of being approximated, and <c>@paths</c>/<c>@plugins</c> never appear in the conformance corpus.
/// </para>
/// </remarks>
internal enum HarnessOptionKind
{
    Boolean,
    String,
    Number,
    List,
    ListOrElement,
    Map,
    Object,
}

/// <summary>Which part of a program an option takes part in (<c>CommandLineOption.affects*</c>), as
/// <c>CompilerTest.varyBy</c> (src/testRunner/compilerRunner.ts:130-150) filters on.</summary>
[Flags]
internal enum OptionAffects
{
    None = 0,
    ProgramStructure = 1 << 0,
    Emit = 1 << 1,
    ModuleResolution = 1 << 2,
    BindDiagnostics = 1 << 3,
    SemanticDiagnostics = 1 << 4,
    SourceFile = 1 << 5,
    DeclarationPath = 1 << 6,
    BuildInfo = 1 << 7,
}

/// <summary>One list-typed option's element declaration (<c>CommandLineOptionOfListType.element</c>).</summary>
internal sealed record HarnessOptionElement(
    HarnessOptionKind Kind,
    bool IsFilePath,
    IReadOnlyDictionary<string, object>? Map);

/// <summary>One <c>CommandLineOption</c>: its canonical name, its declared type, whether it names a path, its
/// custom-type table and (for a list) its element declaration.</summary>
internal sealed record HarnessOption(
    string Name,
    HarnessOptionKind Kind,
    bool IsCommandLineOnly,
    bool IsFilePath,
    OptionAffects Affects,
    IReadOnlyDictionary<string, object>? Map,
    HarnessOptionElement? Element);

/// <summary>The reference's option declarations, indexed by lower-cased name as <c>getCommandLineOption</c> does.</summary>
internal static class BaselineDiffOptions
{
    internal static readonly IReadOnlyDictionary<string, HarnessOption> Declarations = Build();

    private static Dictionary<string, HarnessOption> Build()
    {
        var options = new Dictionary<string, HarnessOption>(StringComparer.OrdinalIgnoreCase);
        void Add(HarnessOption option) => options[option.Name] = option;

        // src/harness/harnessIO.ts:257-273 — the harness's own options, which are not part of ts.optionDeclarations.
        Add(new HarnessOption("allowNonTsExtensions", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("useCaseSensitiveFileNames", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("baselineFile", HarnessOptionKind.String, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("fileName", HarnessOptionKind.String, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("noErrorTruncation", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("suppressOutputPathCheck", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("noImplicitReferences", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("currentDirectory", HarnessOptionKind.String, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("symlink", HarnessOptionKind.String, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("link", HarnessOptionKind.String, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("noTypesAndSymbols", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("fullEmitPaths", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("noCheck", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("reportDiagnostics", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("captureSuggestions", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));

        // src/compiler/commandLineParser.ts — the reference's own declarations, in their declared order.
        Add(new HarnessOption("help", HarnessOptionKind.Boolean, IsCommandLineOnly: true, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("watch", HarnessOptionKind.Boolean, IsCommandLineOnly: true, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("preserveWatchOutput", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("listFiles", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("explainFiles", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("listEmittedFiles", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("pretty", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("traceResolution", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("diagnostics", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("extendedDiagnostics", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("generateCpuProfile", HarnessOptionKind.String, IsCommandLineOnly: false, IsFilePath: true, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("generateTrace", HarnessOptionKind.String, IsCommandLineOnly: false, IsFilePath: true, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("incremental", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("declaration", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("declarationMap", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("emitDeclarationOnly", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("sourceMap", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("inlineSourceMap", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("noCheck", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("noEmit", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("assumeChangesOnlyAffectDirectDependencies", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.Emit | OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("locale", HarnessOptionKind.String, IsCommandLineOnly: true, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("all", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("version", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("init", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("project", HarnessOptionKind.String, IsCommandLineOnly: false, IsFilePath: true, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("showConfig", HarnessOptionKind.Boolean, IsCommandLineOnly: true, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("listFilesOnly", HarnessOptionKind.Boolean, IsCommandLineOnly: true, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("ignoreConfig", HarnessOptionKind.Boolean, IsCommandLineOnly: true, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("target", HarnessOptionKind.Map, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.Emit | OptionAffects.ModuleResolution | OptionAffects.SourceFile | OptionAffects.BuildInfo, Map: new Dictionary<string, object>(StringComparer.Ordinal) { ["es3"] = 0, ["es5"] = 1, ["es6"] = 2, ["es2015"] = 2, ["es2016"] = 3, ["es2017"] = 4, ["es2018"] = 5, ["es2019"] = 6, ["es2020"] = 7, ["es2021"] = 8, ["es2022"] = 9, ["es2023"] = 10, ["es2024"] = 11, ["es2025"] = 12, ["esnext"] = 99 }, Element: null));
        Add(new HarnessOption("module", HarnessOptionKind.Map, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.Emit | OptionAffects.ModuleResolution | OptionAffects.SourceFile | OptionAffects.BuildInfo, Map: new Dictionary<string, object>(StringComparer.Ordinal) { ["none"] = 0, ["commonjs"] = 1, ["amd"] = 2, ["system"] = 4, ["umd"] = 3, ["es6"] = 5, ["es2015"] = 5, ["es2020"] = 6, ["es2022"] = 7, ["esnext"] = 99, ["node16"] = 100, ["node18"] = 101, ["node20"] = 102, ["nodenext"] = 199, ["preserve"] = 200 }, Element: null));
        Add(new HarnessOption("lib", HarnessOptionKind.List, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.ProgramStructure, Map: null, Element: new HarnessOptionElement(HarnessOptionKind.Map, IsFilePath: false, Map: new Dictionary<string, object>(StringComparer.Ordinal) { ["es5"] = "lib.es5.d.ts", ["es6"] = "lib.es2015.d.ts", ["es2015"] = "lib.es2015.d.ts", ["es7"] = "lib.es2016.d.ts", ["es2016"] = "lib.es2016.d.ts", ["es2017"] = "lib.es2017.d.ts", ["es2018"] = "lib.es2018.d.ts", ["es2019"] = "lib.es2019.d.ts", ["es2020"] = "lib.es2020.d.ts", ["es2021"] = "lib.es2021.d.ts", ["es2022"] = "lib.es2022.d.ts", ["es2023"] = "lib.es2023.d.ts", ["es2024"] = "lib.es2024.d.ts", ["es2025"] = "lib.es2025.d.ts", ["esnext"] = "lib.esnext.d.ts", ["dom"] = "lib.dom.d.ts", ["dom.iterable"] = "lib.dom.iterable.d.ts", ["dom.asynciterable"] = "lib.dom.asynciterable.d.ts", ["webworker"] = "lib.webworker.d.ts", ["webworker.importscripts"] = "lib.webworker.importscripts.d.ts", ["webworker.iterable"] = "lib.webworker.iterable.d.ts", ["webworker.asynciterable"] = "lib.webworker.asynciterable.d.ts", ["scripthost"] = "lib.scripthost.d.ts", ["es2015.core"] = "lib.es2015.core.d.ts", ["es2015.collection"] = "lib.es2015.collection.d.ts", ["es2015.generator"] = "lib.es2015.generator.d.ts", ["es2015.iterable"] = "lib.es2015.iterable.d.ts", ["es2015.promise"] = "lib.es2015.promise.d.ts", ["es2015.proxy"] = "lib.es2015.proxy.d.ts", ["es2015.reflect"] = "lib.es2015.reflect.d.ts", ["es2015.symbol"] = "lib.es2015.symbol.d.ts", ["es2015.symbol.wellknown"] = "lib.es2015.symbol.wellknown.d.ts", ["es2016.array.include"] = "lib.es2016.array.include.d.ts", ["es2016.intl"] = "lib.es2016.intl.d.ts", ["es2017.arraybuffer"] = "lib.es2017.arraybuffer.d.ts", ["es2017.date"] = "lib.es2017.date.d.ts", ["es2017.object"] = "lib.es2017.object.d.ts", ["es2017.sharedmemory"] = "lib.es2017.sharedmemory.d.ts", ["es2017.string"] = "lib.es2017.string.d.ts", ["es2017.intl"] = "lib.es2017.intl.d.ts", ["es2017.typedarrays"] = "lib.es2017.typedarrays.d.ts", ["es2018.asyncgenerator"] = "lib.es2018.asyncgenerator.d.ts", ["es2018.asynciterable"] = "lib.es2018.asynciterable.d.ts", ["es2018.intl"] = "lib.es2018.intl.d.ts", ["es2018.promise"] = "lib.es2018.promise.d.ts", ["es2018.regexp"] = "lib.es2018.regexp.d.ts", ["es2019.array"] = "lib.es2019.array.d.ts", ["es2019.object"] = "lib.es2019.object.d.ts", ["es2019.string"] = "lib.es2019.string.d.ts", ["es2019.symbol"] = "lib.es2019.symbol.d.ts", ["es2019.intl"] = "lib.es2019.intl.d.ts", ["es2020.bigint"] = "lib.es2020.bigint.d.ts", ["es2020.date"] = "lib.es2020.date.d.ts", ["es2020.promise"] = "lib.es2020.promise.d.ts", ["es2020.sharedmemory"] = "lib.es2020.sharedmemory.d.ts", ["es2020.string"] = "lib.es2020.string.d.ts", ["es2020.symbol.wellknown"] = "lib.es2020.symbol.wellknown.d.ts", ["es2020.intl"] = "lib.es2020.intl.d.ts", ["es2020.number"] = "lib.es2020.number.d.ts", ["es2021.promise"] = "lib.es2021.promise.d.ts", ["es2021.string"] = "lib.es2021.string.d.ts", ["es2021.weakref"] = "lib.es2021.weakref.d.ts", ["es2021.intl"] = "lib.es2021.intl.d.ts", ["es2022.array"] = "lib.es2022.array.d.ts", ["es2022.error"] = "lib.es2022.error.d.ts", ["es2022.intl"] = "lib.es2022.intl.d.ts", ["es2022.object"] = "lib.es2022.object.d.ts", ["es2022.string"] = "lib.es2022.string.d.ts", ["es2022.regexp"] = "lib.es2022.regexp.d.ts", ["es2023.array"] = "lib.es2023.array.d.ts", ["es2023.collection"] = "lib.es2023.collection.d.ts", ["es2023.intl"] = "lib.es2023.intl.d.ts", ["es2024.arraybuffer"] = "lib.es2024.arraybuffer.d.ts", ["es2024.collection"] = "lib.es2024.collection.d.ts", ["es2024.object"] = "lib.es2024.object.d.ts", ["es2024.promise"] = "lib.es2024.promise.d.ts", ["es2024.regexp"] = "lib.es2024.regexp.d.ts", ["es2024.sharedmemory"] = "lib.es2024.sharedmemory.d.ts", ["es2024.string"] = "lib.es2024.string.d.ts", ["es2025.collection"] = "lib.es2025.collection.d.ts", ["es2025.float16"] = "lib.es2025.float16.d.ts", ["es2025.intl"] = "lib.es2025.intl.d.ts", ["es2025.iterator"] = "lib.es2025.iterator.d.ts", ["es2025.promise"] = "lib.es2025.promise.d.ts", ["es2025.regexp"] = "lib.es2025.regexp.d.ts", ["esnext.asynciterable"] = "lib.es2018.asynciterable.d.ts", ["esnext.symbol"] = "lib.es2019.symbol.d.ts", ["esnext.bigint"] = "lib.es2020.bigint.d.ts", ["esnext.weakref"] = "lib.es2021.weakref.d.ts", ["esnext.object"] = "lib.es2024.object.d.ts", ["esnext.regexp"] = "lib.es2024.regexp.d.ts", ["esnext.string"] = "lib.es2024.string.d.ts", ["esnext.float16"] = "lib.es2025.float16.d.ts", ["esnext.iterator"] = "lib.es2025.iterator.d.ts", ["esnext.promise"] = "lib.es2025.promise.d.ts", ["esnext.array"] = "lib.esnext.array.d.ts", ["esnext.collection"] = "lib.esnext.collection.d.ts", ["esnext.date"] = "lib.esnext.date.d.ts", ["esnext.decorators"] = "lib.esnext.decorators.d.ts", ["esnext.disposable"] = "lib.esnext.disposable.d.ts", ["esnext.error"] = "lib.esnext.error.d.ts", ["esnext.intl"] = "lib.esnext.intl.d.ts", ["esnext.sharedmemory"] = "lib.esnext.sharedmemory.d.ts", ["esnext.temporal"] = "lib.esnext.temporal.d.ts", ["esnext.typedarrays"] = "lib.esnext.typedarrays.d.ts", ["decorators"] = "lib.decorators.d.ts", ["decorators.legacy"] = "lib.decorators.legacy.d.ts" })));
        Add(new HarnessOption("allowJs", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("checkJs", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.ModuleResolution | OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("jsx", HarnessOptionKind.Map, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.Emit | OptionAffects.ModuleResolution | OptionAffects.SemanticDiagnostics | OptionAffects.SourceFile | OptionAffects.BuildInfo, Map: new Dictionary<string, object>(StringComparer.Ordinal) { ["preserve"] = 1, ["react-native"] = 3, ["react-jsx"] = 4, ["react-jsxdev"] = 5, ["react"] = 2 }, Element: null));
        Add(new HarnessOption("outFile", HarnessOptionKind.String, IsCommandLineOnly: false, IsFilePath: true, Affects: OptionAffects.Emit | OptionAffects.DeclarationPath | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("outDir", HarnessOptionKind.String, IsCommandLineOnly: false, IsFilePath: true, Affects: OptionAffects.Emit | OptionAffects.DeclarationPath | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("rootDir", HarnessOptionKind.String, IsCommandLineOnly: false, IsFilePath: true, Affects: OptionAffects.Emit | OptionAffects.DeclarationPath | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("composite", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("tsBuildInfoFile", HarnessOptionKind.String, IsCommandLineOnly: false, IsFilePath: true, Affects: OptionAffects.Emit | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("removeComments", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.Emit | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("importHelpers", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.Emit | OptionAffects.SourceFile | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("importsNotUsedAsValues", HarnessOptionKind.Map, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.Emit | OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: new Dictionary<string, object>(StringComparer.Ordinal) { ["remove"] = 0, ["preserve"] = 1, ["error"] = 2 }, Element: null));
        Add(new HarnessOption("downlevelIteration", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.Emit | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("isolatedModules", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("verbatimModuleSyntax", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.Emit | OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("isolatedDeclarations", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("erasableSyntaxOnly", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("libReplacement", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.ProgramStructure, Map: null, Element: null));
        Add(new HarnessOption("strict", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("noImplicitAny", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("strictNullChecks", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("strictFunctionTypes", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("strictBindCallApply", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("strictPropertyInitialization", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("strictBuiltinIteratorReturn", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("stableTypeOrdering", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("noImplicitThis", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("useUnknownInCatchVariables", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("alwaysStrict", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.Emit | OptionAffects.SourceFile | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("noUnusedLocals", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("noUnusedParameters", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("exactOptionalPropertyTypes", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("noImplicitReturns", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("noFallthroughCasesInSwitch", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.BindDiagnostics | OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("noUncheckedIndexedAccess", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("noImplicitOverride", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("noPropertyAccessFromIndexSignature", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("moduleResolution", HarnessOptionKind.Map, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.ModuleResolution | OptionAffects.SourceFile, Map: new Dictionary<string, object>(StringComparer.Ordinal) { ["node10"] = 2, ["node"] = 2, ["classic"] = 1, ["node16"] = 3, ["nodenext"] = 99, ["bundler"] = 100 }, Element: null));
        Add(new HarnessOption("baseUrl", HarnessOptionKind.String, IsCommandLineOnly: false, IsFilePath: true, Affects: OptionAffects.ModuleResolution, Map: null, Element: null));
        Add(new HarnessOption("paths", HarnessOptionKind.Object, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.ModuleResolution, Map: null, Element: null));
        Add(new HarnessOption("rootDirs", HarnessOptionKind.List, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.ModuleResolution, Map: null, Element: new HarnessOptionElement(HarnessOptionKind.String, IsFilePath: true, Map: null)));
        Add(new HarnessOption("typeRoots", HarnessOptionKind.List, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.ModuleResolution, Map: null, Element: new HarnessOptionElement(HarnessOptionKind.String, IsFilePath: true, Map: null)));
        Add(new HarnessOption("types", HarnessOptionKind.List, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.ProgramStructure, Map: null, Element: new HarnessOptionElement(HarnessOptionKind.String, IsFilePath: false, Map: null)));
        Add(new HarnessOption("allowSyntheticDefaultImports", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("esModuleInterop", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.Emit | OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("preserveSymlinks", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("allowUmdGlobalAccess", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("moduleSuffixes", HarnessOptionKind.List, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.ModuleResolution, Map: null, Element: new HarnessOptionElement(HarnessOptionKind.String, IsFilePath: false, Map: null)));
        Add(new HarnessOption("allowImportingTsExtensions", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("rewriteRelativeImportExtensions", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("resolvePackageJsonExports", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.ModuleResolution, Map: null, Element: null));
        Add(new HarnessOption("resolvePackageJsonImports", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.ModuleResolution, Map: null, Element: null));
        Add(new HarnessOption("customConditions", HarnessOptionKind.List, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.ModuleResolution, Map: null, Element: new HarnessOptionElement(HarnessOptionKind.String, IsFilePath: false, Map: null)));
        Add(new HarnessOption("noUncheckedSideEffectImports", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("sourceRoot", HarnessOptionKind.String, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.Emit | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("mapRoot", HarnessOptionKind.String, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.Emit | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("inlineSources", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.Emit | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("experimentalDecorators", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.Emit | OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("emitDecoratorMetadata", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.Emit | OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("jsxFactory", HarnessOptionKind.String, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("jsxFragmentFactory", HarnessOptionKind.String, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("jsxImportSource", HarnessOptionKind.String, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.Emit | OptionAffects.ModuleResolution | OptionAffects.SemanticDiagnostics | OptionAffects.SourceFile | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("resolveJsonModule", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.ModuleResolution, Map: null, Element: null));
        Add(new HarnessOption("allowArbitraryExtensions", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.ProgramStructure, Map: null, Element: null));
        Add(new HarnessOption("out", HarnessOptionKind.String, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.Emit | OptionAffects.DeclarationPath | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("reactNamespace", HarnessOptionKind.String, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.Emit | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("skipDefaultLibCheck", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("charset", HarnessOptionKind.String, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("emitBOM", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.Emit | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("newLine", HarnessOptionKind.Map, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.Emit | OptionAffects.BuildInfo, Map: new Dictionary<string, object>(StringComparer.Ordinal) { ["crlf"] = 0, ["lf"] = 1 }, Element: null));
        Add(new HarnessOption("noErrorTruncation", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("noLib", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.ProgramStructure, Map: null, Element: null));
        Add(new HarnessOption("noResolve", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.ModuleResolution, Map: null, Element: null));
        Add(new HarnessOption("stripInternal", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.Emit | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("disableSizeLimit", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.ProgramStructure, Map: null, Element: null));
        Add(new HarnessOption("disableSourceOfProjectReferenceRedirect", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("disableSolutionSearching", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("disableReferencedProjectLoad", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("noImplicitUseStrict", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("noEmitHelpers", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.Emit | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("noEmitOnError", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.Emit | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("preserveConstEnums", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.Emit | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("declarationDir", HarnessOptionKind.String, IsCommandLineOnly: false, IsFilePath: true, Affects: OptionAffects.Emit | OptionAffects.DeclarationPath | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("skipLibCheck", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("allowUnusedLabels", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.BindDiagnostics | OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("allowUnreachableCode", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.BindDiagnostics | OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("suppressExcessPropertyErrors", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("suppressImplicitAnyIndexErrors", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("forceConsistentCasingInFileNames", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.ModuleResolution, Map: null, Element: null));
        Add(new HarnessOption("maxNodeModuleJsDepth", HarnessOptionKind.Number, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.ModuleResolution, Map: null, Element: null));
        Add(new HarnessOption("noStrictGenericChecks", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("useDefineForClassFields", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.Emit | OptionAffects.SemanticDiagnostics | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("preserveValueImports", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.Emit | OptionAffects.BuildInfo, Map: null, Element: null));
        Add(new HarnessOption("keyofStringsOnly", HarnessOptionKind.Boolean, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));
        Add(new HarnessOption("plugins", HarnessOptionKind.List, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: new HarnessOptionElement(HarnessOptionKind.Object, IsFilePath: false, Map: null)));
        Add(new HarnessOption("moduleDetection", HarnessOptionKind.Map, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.ModuleResolution | OptionAffects.SourceFile, Map: new Dictionary<string, object>(StringComparer.Ordinal) { ["auto"] = 2, ["legacy"] = 1, ["force"] = 3 }, Element: null));
        Add(new HarnessOption("ignoreDeprecations", HarnessOptionKind.String, IsCommandLineOnly: false, IsFilePath: false, Affects: OptionAffects.None, Map: null, Element: null));

        return options;
    }
}
