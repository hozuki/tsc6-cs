using TypeScript.Compiler;
using PortProgram = TypeScript.Compiler.Program;

namespace TypeScript.Tsc;

// ported from: src/tsc/tsc.ts (the command-line front end) + src/compiler/executeCommandLine.ts / watch.ts
/// <summary>
/// The port's <c>tsc</c>: parses a command line, builds a <see cref="PortProgram"/> over the real file system, and
/// reports the diagnostics the way the reference's command line does.
/// </summary>
/// <remarks>
/// PORT-NOTE (scope): this is the *first* piece of M8 (docs/architecture.md §15.15), not the whole front end. What is
/// here: the file-list form of the command line (`tsc a.ts b.ts --strict …`), the option model it needs, the
/// real-file-system host, the diagnostic assembly of <c>emitFilesAndReportErrors</c> (watch.ts:564-613) and the
/// plain-text diagnostic report (<c>formatDiagnostics</c>, program.ts:658-666).
/// <para>
/// What is deliberately *not* here, each with the reason it is not a line that was skipped by accident:
/// <list type="bullet">
/// <item><c>tsconfig.json</c> reading (<c>parseJsonSourceFileConfigFileContent</c>) and <c>-p</c>/<c>--build</c>:
/// the option *declaration* table (commandLineParser.ts:1184-2900) is itself a milestone — <c>tools/BaselineDiff</c>
/// carries a generated copy of it for the harness model, but nothing under <c>src/</c> has it. Until it lands, options
/// come from the command line only, which is exactly what a per-case differential run needs.</item>
/// <item>watch mode, <c>--build</c>, project references: <c>tsbuild.ts</c> is the rest of M8 and
/// <c>TsBuild.ResolveConfigFileProjectName</c> still throws <c>BuildNotLanded</c>.</item>
/// <item>pretty output (<c>formatDiagnosticsWithColorAndContext</c>) and error summaries: the formatter is ported and
/// callable (see <c>FormatDiagnosticsWithColorAndContext</c>), the CLI flags that select it are not.</item>
/// </list>
/// </para>
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.Write(Usage);
            return 2;
        }

        var options = new TscCompilerOptions();
        var fileNames = new List<string>();
        string? libDirectory = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Length == 0) continue;
            if (arg == "--") { for (i++; i < args.Length; i++) fileNames.Add(args[i]); break; }
            if (arg[0] != '-') { fileNames.Add(arg); continue; }
            if (arg is "-h" or "--help" or "-?") { Console.Out.Write(Usage); return 0; }

            var name = arg.TrimStart('-');
            string? value = null;
            var equals = name.IndexOf('=');
            if (equals >= 0)
            {
                value = name[(equals + 1)..];
                name = name[..equals];
            }
            else if (ValueOptions.Contains(name) && i + 1 < args.Length)
            {
                value = args[++i];
            }

            if (name == "lib-dir")
            {
                if (value is null) { return UsageError("Option '--lib-dir' expects a directory."); }
                libDirectory = value;
                continue;
            }

            if (!Apply(options, name, value, out var optionError))
            {
                return UsageError(optionError ?? $"Unknown compiler option '--{name}'.");
            }
        }

        if (fileNames.Count == 0)
        {
            return UsageError("No input files.");
        }

        var system = new DiskSystem(libDirectory ?? ResolveLibDirectory());
        var host = PortProgram.CreateCompilerHostWorker(options, /*setParentNodes*/ false, system);
        var program = PortProgram.CreateProgram(fileNames, options, host);

        var diagnostics = CollectDiagnostics(program, options);
        var report = PortProgram.FormatDiagnostics(diagnostics, new FormatHost(system));
        if (report.Length != 0)
        {
            Console.Out.Write(report);
        }

        return diagnostics.Any(d => d.Category == DiagnosticCategory.Error) ? 1 : 0;
    }

    /// <summary>
    /// The diagnostics a command-line run reports, in the order and with the gating the reference's
    /// <c>emitFilesAndReportErrors</c> uses (watch.ts:577-612): config-file parsing first, then syntactic errors; only
    /// when there are none of those, the option, global and semantic diagnostics — and the emit's own diagnostics last.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: <c>getDeclarationDiagnostics</c> is asked for under the same three conditions the reference states
    /// (<c>noEmit &amp;&amp; getEmitDeclarations(options)</c> and no diagnostics so far). The port's emitter, not this
    /// front end, is what decides whether declarations are produced during <c>emit</c>.
    /// </remarks>
    private static List<Diagnostic> CollectDiagnostics(PortProgram program, TscCompilerOptions options)
    {
        var all = new List<IDiagnostic>();
        all.AddRange(program.GetConfigFileParsingDiagnostics());
        var configFileParsingCount = all.Count;
        all.AddRange(program.GetSyntacticDiagnostics(/*sourceFile*/ null, /*cancellationToken*/ null));

        if (all.Count == configFileParsingCount)
        {
            all.AddRange(program.GetOptionsDiagnostics(null));

            if (options.ListFilesOnly != true)
            {
                all.AddRange(program.GetGlobalDiagnostics(null));
                if (all.Count == configFileParsingCount)
                {
                    all.AddRange(program.GetSemanticDiagnostics(null, null));
                }

                if (options.NoEmit == true
                    && Utilities.GetEmitDeclarations(options)
                    && all.Count == configFileParsingCount)
                {
                    all.AddRange(program.GetDeclarationDiagnostics(null, null));
                }
            }
        }

        if (options.ListFilesOnly != true)
        {
            var emit = program.Emit();
            if (emit.Diagnostics is not null) all.AddRange(emit.Diagnostics);
        }

        return [.. Utilities.SortAndDeduplicateDiagnostics(all.Cast<Diagnostic>().ToList())];
    }

    /// <summary>
    /// Where the default libraries are. <c>getExecutingFilePath()</c>'s directory is the reference's answer and there
    /// is no <c>lib/</c> next to the compiler assembly, so the front end looks, in order: next to the executable, then
    /// <c>TSC_CS_LIB_DIR</c>. Nothing found is not an error here — the compiler reports the missing default library
    /// itself (<c>lib.es5.d.ts not found</c>), which is the same diagnostic the reference gives.
    /// </summary>
    private static string ResolveLibDirectory()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "lib");
        if (Directory.Exists(beside)) return beside;

        var fromEnvironment = Environment.GetEnvironmentVariable("TSC_CS_LIB_DIR");
        if (!string.IsNullOrEmpty(fromEnvironment)) return fromEnvironment;

        return beside;
    }

    private static int UsageError(string message)
    {
        Console.Error.WriteLine($"error TS5023: {message}");
        Console.Error.Write(Usage);
        return 2;
    }

    /// <summary>The command line this front end accepts. Options it does not know are an error, never silently ignored:
    /// a differential run has to know exactly which options a case was compiled with.</summary>
    private const string Usage = """

        tsc (C# port): a command-line front end over the ported compiler.

        Usage: tsc [options] <file...>

        Compiler options (the subset this front end implements — `tsc --help --all` is the reference's full list):
          --target <es3|es5|es2015..es2025|esnext>      --module <none|commonjs|amd|umd|system|es2015|es2020|es2022|esnext|node16|node18|node20|nodenext|preserve>
          --moduleResolution <classic|node10|node16|nodenext|bundler>
          --jsx <preserve|react|react-native|react-jsx|react-jsxdev>
          --lib <name,...>  --types <name,...>  --typeRoots <dir,...>
          --strict and the strict family (--noImplicitAny, --strictNullChecks, --strictFunctionTypes,
            --strictBindCallApply, --strictPropertyInitialization, --useUnknownInCatchVariables, --alwaysStrict,
            --exactOptionalPropertyTypes, --noUncheckedIndexedAccess, --noImplicitOverride, --noPropertyAccessFromIndexSignature),
          --noUnusedLocals --noUnusedParameters --noImplicitReturns --noFallthroughCasesInSwitch
          --allowJs --checkJs --noEmit --noEmitOnError --skipLibCheck --esModuleInterop --allowSyntheticDefaultImports
          --allowUmdGlobalAccess --resolveJsonModule --downlevelIteration --experimentalDecorators --emitDecoratorMetadata
          --useDefineForClassFields --isolatedModules --verbatimModuleSyntax --preserveConstEnums --removeComments
          --declaration --declarationMap --sourceMap --inlineSourceMap --inlineSources --noResolve --noLib
          --outDir <dir> --outFile <file> --rootDir <dir> --declarationDir <dir> --baseUrl <dir> --sourceRoot <dir> --mapRoot <dir>
          --moduleDetection <legacy|auto|force> --newLine <crlf|lf> --jsxFactory <name> --jsxFragmentFactory <name> --jsxImportSource <name>
          --listFiles --pretty <true|false> (accepted; the report is always plain text)
          --lib-dir <dir>   (port only: where the default libraries live; default: `lib/` next to the executable or TSC_CS_LIB_DIR)

        """;

    /// <summary>The options that take a separate value argument (<c>--target es5</c> as well as <c>--target=es5</c>).
    /// Every other option is a boolean flag.</summary>
    private static readonly HashSet<string> ValueOptions = new(StringComparer.Ordinal)
    {
        "target", "module", "moduleResolution", "jsx", "jsxFactory", "jsxFragmentFactory", "jsxImportSource",
        "lib", "types", "typeRoots", "moduleDetection", "newLine", "outDir", "outFile", "rootDir", "declarationDir",
        "baseUrl", "sourceRoot", "mapRoot", "lib-dir", "pretty",
    };

    /// <summary>Applies one command-line option. <paramref name="value"/> is <c>null</c> for the bare-flag form.</summary>
    private static bool Apply(TscCompilerOptions o, string name, string? value, out string? error)
    {
        error = null;
        switch (name)
        {
            // ---- module/target/jsx family ----
            case "target": return SetEnum<ScriptTarget>(name, value, TargetAliases, v => o.Target = v, out error);
            case "module": return SetEnum<ModuleKind>(name, value, ModuleAliases, v => o.Module = v, out error);
            case "moduleResolution": return SetEnum<ModuleResolutionKind>(name, value, ModuleResolutionAliases, v => o.ModuleResolution = v, out error);
            case "jsx": return SetEnum<JsxEmit>(name, value, JsxAliases, v => o.Jsx = v, out error);
            case "moduleDetection": return SetEnum<ModuleDetectionKind>(name, value, NoAliases, v => o.ModuleDetection = v, out error);
            case "newLine": return SetEnum<NewLineKind>(name, value, NewLineAliases, v => o.NewLine = v, out error);

            // ---- lists and paths ----
            case "lib": o.Lib = Split(value); return true;
            case "types": o.Types = Split(value); return true;
            case "typeRoots": o.TypeRoots = Split(value); return true;
            case "outDir": o.OutDir = value; return true;
            case "outFile": o.OutFile = value; return true;
            case "rootDir": o.RootDir = value; return true;
            case "declarationDir": o.DeclarationDir = value; return true;
            case "baseUrl": o.BaseUrl = value; return true;
            case "sourceRoot": o.SourceRoot = value; return true;
            case "mapRoot": o.MapRoot = value; return true;
            case "jsxFactory": o.JsxFactory = value; return true;
            case "jsxFragmentFactory": o.JsxFragmentFactory = value; return true;
            case "jsxImportSource": o.JsxImportSource = value; return true;

            // ---- strict family ----
            case "strict": return SetBool(name, value, v => o.Strict = v, out error);
            case "noImplicitAny": return SetBool(name, value, v => o.NoImplicitAny = v, out error);
            case "noImplicitThis": return SetBool(name, value, v => o.NoImplicitThis = v, out error);
            case "strictNullChecks": return SetBool(name, value, v => o.StrictNullChecks = v, out error);
            case "strictFunctionTypes": return SetBool(name, value, v => o.StrictFunctionTypes = v, out error);
            case "strictBindCallApply": return SetBool(name, value, v => o.StrictBindCallApply = v, out error);
            case "strictPropertyInitialization": return SetBool(name, value, v => o.StrictPropertyInitialization = v, out error);
            case "strictBuiltinIteratorReturn": return SetBool(name, value, v => o.StrictBuiltinIteratorReturn = v, out error);
            case "useUnknownInCatchVariables": return SetBool(name, value, v => o.UseUnknownInCatchVariables = v, out error);
            case "alwaysStrict": return SetBool(name, value, v => o.AlwaysStrict = v, out error);
            case "exactOptionalPropertyTypes": return SetBool(name, value, v => o.ExactOptionalPropertyTypes = v, out error);
            case "noUncheckedIndexedAccess": return SetBool(name, value, v => o.NoUncheckedIndexedAccess = v, out error);
            case "noImplicitOverride": return SetBool(name, value, v => o.NoImplicitOverride = v, out error);
            case "noPropertyAccessFromIndexSignature": return SetBool(name, value, v => o.NoPropertyAccessFromIndexSignature = v, out error);

            // ---- strictness/lint family ----
            case "noUnusedLocals": return SetBool(name, value, v => o.NoUnusedLocals = v, out error);
            case "noUnusedParameters": return SetBool(name, value, v => o.NoUnusedParameters = v, out error);
            case "noImplicitReturns": return SetBool(name, value, v => o.NoImplicitReturns = v, out error);
            case "noFallthroughCasesInSwitch": return SetBool(name, value, v => o.NoFallthroughCasesInSwitch = v, out error);
            case "allowUnreachableCode": return SetBool(name, value, v => o.AllowUnreachableCode = v, out error);
            case "allowUnusedLabels": return SetBool(name, value, v => o.AllowUnusedLabels = v, out error);
            case "noImplicitUseStrict": return SetBool(name, value, v => o.NoImplicitUseStrict = v, out error);

            // ---- inputs and checking ----
            case "allowJs": return SetBool(name, value, v => o.AllowJs = v, out error);
            case "checkJs": return SetBool(name, value, v => o.CheckJs = v, out error);
            case "maxNodeModuleJsDepth": return SetInt(name, value, v => o.MaxNodeModuleJsDepth = v, out error);
            case "skipLibCheck": return SetBool(name, value, v => o.SkipLibCheck = v, out error);
            case "skipDefaultLibCheck": return SetBool(name, value, v => o.SkipDefaultLibCheck = v, out error);
            case "noCheck": return SetBool(name, value, v => o.NoCheck = v, out error);
            case "noResolve": return SetBool(name, value, v => o.NoResolve = v, out error);
            case "noLib": return SetBool(name, value, v => o.NoLib = v, out error);
            case "noErrorTruncation": return SetBool(name, value, v => o.NoErrorTruncation = v, out error);
            case "resolveJsonModule": return SetBool(name, value, v => o.ResolveJsonModule = v, out error);
            case "resolvePackageJsonExports": return SetBool(name, value, v => o.ResolvePackageJsonExports = v, out error);
            case "resolvePackageJsonImports": return SetBool(name, value, v => o.ResolvePackageJsonImports = v, out error);
            case "allowImportingTsExtensions": return SetBool(name, value, v => o.AllowImportingTsExtensions = v, out error);
            case "allowArbitraryExtensions": return SetBool(name, value, v => o.AllowArbitraryExtensions = v, out error);
            case "rewriteRelativeImportExtensions": return SetBool(name, value, v => o.RewriteRelativeImportExtensions = v, out error);
            case "noUncheckedSideEffectImports": return SetBool(name, value, v => o.NoUncheckedSideEffectImports = v, out error);
            case "verbatimModuleSyntax": return SetBool(name, value, v => o.VerbatimModuleSyntax = v, out error);
            case "esModuleInterop": return SetBool(name, value, v => o.EsModuleInterop = v, out error);
            case "allowSyntheticDefaultImports": return SetBool(name, value, v => o.AllowSyntheticDefaultImports = v, out error);
            case "allowUmdGlobalAccess": return SetBool(name, value, v => o.AllowUmdGlobalAccess = v, out error);
            case "isolatedModules": return SetBool(name, value, v => o.IsolatedModules = v, out error);
            case "isolatedDeclarations": return SetBool(name, value, v => o.IsolatedDeclarations = v, out error);
            case "erasableSyntaxOnly": return SetBool(name, value, v => o.ErasableSyntaxOnly = v, out error);
            case "downlevelIteration": return SetBool(name, value, v => o.DownlevelIteration = v, out error);
            case "experimentalDecorators": return SetBool(name, value, v => o.ExperimentalDecorators = v, out error);
            case "emitDecoratorMetadata": return SetBool(name, value, v => o.EmitDecoratorMetadata = v, out error);

            // ---- output ----
            case "noEmit": return SetBool(name, value, v => o.NoEmit = v, out error);
            case "noEmitOnError": return SetBool(name, value, v => o.NoEmitOnError = v, out error);
            case "declaration": return SetBool(name, value, v => o.Declaration = v, out error);
            case "declarationMap": return SetBool(name, value, v => o.DeclarationMap = v, out error);
            case "emitDeclarationOnly": return SetBool(name, value, v => o.EmitDeclarationOnly = v, out error);
            case "sourceMap": return SetBool(name, value, v => o.SourceMap = v, out error);
            case "inlineSourceMap": return SetBool(name, value, v => o.InlineSourceMap = v, out error);
            case "inlineSources": return SetBool(name, value, v => o.InlineSources = v, out error);
            case "removeComments": return SetBool(name, value, v => o.RemoveComments = v, out error);
            case "preserveConstEnums": return SetBool(name, value, v => o.PreserveConstEnums = v, out error);
            case "importHelpers": return SetBool(name, value, v => o.ImportHelpers = v, out error);

            // ---- front-end-only flags ----
            case "listFiles": return SetBool(name, value, v => o.ListFilesOnly = v, out error);
            case "pretty": SetBool(name, value, _ => { }, out error); return error is null;

            default:
                error = $"Unknown compiler option '--{name}'.";
                return false;
        }
    }

    private static List<string>? Split(string? value)
        => value is null ? null : [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static bool SetBool(string name, string? value, Action<bool> set, out string? error)
    {
        error = null;
        if (value is null) { set(true); return true; }
        if (value is "true") { set(true); return true; }
        if (value is "false") { set(false); return true; }
        error = $"Option '--{name}' expects 'true' or 'false'.";
        return false;
    }

    private static bool SetInt(string name, string? value, Action<int> set, out string? error)
    {
        error = null;
        if (value is not null && int.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var number))
        {
            set(number);
            return true;
        }

        error = $"Option '--{name}' expects a number.";
        return false;
    }

    /// <summary>Parses an enum-valued option, honouring the reference's value aliases (commandLineParser.ts's
    /// <c>valueMap</c>s: <c>es6</c>=<c>ES2015</c>, <c>node</c>=<c>Node10</c>/<c>CommonJS</c>, <c>react-jsx</c>=… ).</summary>
    private static bool SetEnum<TEnum>(
        string name,
        string? value,
        IReadOnlyDictionary<string, string> aliases,
        Action<TEnum> set,
        out string? error)
        where TEnum : struct, Enum
    {
        error = null;
        if (value is null)
        {
            error = $"Option '--{name}' expects a value.";
            return false;
        }

        var text = aliases.TryGetValue(value, out var aliased) ? aliased : value;
        if (Enum.TryParse<TEnum>(text, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            set(parsed);
            return true;
        }

        error = $"Argument for '--{name}' option must be one of: {string.Join(", ", Enum.GetNames<TEnum>())}. Got '{value}'.";
        return false;
    }

    private static readonly Dictionary<string, string> NoAliases = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> TargetAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["es6"] = "ES2015",
        ["es7"] = "ES2016",
        ["latest"] = "ESNext",
    };

    private static readonly Dictionary<string, string> ModuleAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["node"] = "CommonJS",
        ["es6"] = "ES2015",
        ["es2015"] = "ES2015",
    };

    private static readonly Dictionary<string, string> ModuleResolutionAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["node"] = "Node10",
        ["nodejs"] = "Node10",
    };

    private static readonly Dictionary<string, string> JsxAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["react-jsx"] = "ReactJSX",
        ["react-jsxdev"] = "ReactJSXDev",
        ["react-native"] = "ReactNative",
    };

    private static readonly Dictionary<string, string> NewLineAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["crlf"] = "CarriageReturnLineFeed",
        ["lf"] = "LineFeed",
    };

    /// <summary>The <see cref="IFormatDiagnosticsHost"/> the plain report needs (program.ts:658-678): paths are
    /// printed relative to the current directory, case-insensitively on this platform.</summary>
    private sealed class FormatHost(DiskSystem system) : IFormatDiagnosticsHost
    {
        public string GetCurrentDirectory() => system.GetCurrentDirectory();

        public string GetCanonicalFileName(string? fileName) => (fileName ?? "").ToLowerInvariant();

        public string GetNewLine() => system.NewLine;
    }
}
