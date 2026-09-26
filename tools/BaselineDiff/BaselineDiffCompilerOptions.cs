namespace TypeScript.Compiler.Tools;

// ported from: src/compiler/types.ts:7403-7572 (interface CompilerOptions, the generated ICompilerOptions)
/// <summary>
/// The option object <c>tools/BaselineDiff</c> hands the program: one writable auto-property per member of the
/// generated <see cref="ICompilerOptions"/>, in the interface's own order. The harness's option model writes one slot
/// at a time from a `// @` directive (`Compiler.compileFiles`'s <c>setCompilerOptionsFromHarnessSetting</c>), so the
/// object has to be mutable and complete — an interface cannot be filled in by name, which is why this tool needs its
/// own concrete implementation.
/// </summary>
/// <remarks>
/// PORT-NOTE: the port has no concrete options class (Utilities/Utilities.Options.cs records that the generated
/// interface is the model, that the reference fills it in with object literals, and that the layer which builds a real
/// options object is M8's tsconfig reader — <c>parseJsonSourceFileConfigFileContent</c> is not ported at all). A tool
/// that creates a program therefore needs a concrete implementation, and this is the tool's own — it is not the
/// product's: nothing under <c>src/</c> refers to it, and when M8 lands a real options type this class is what
/// BaselineDiff switches to. tools/EmitDiff carries the same class for the same reason; the members are in the
/// interface's order so a generator change that adds or drops a slot fails this file's compilation.
/// </remarks>
internal sealed class BaselineDiffCompilerOptions : ICompilerOptions
{
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
}
