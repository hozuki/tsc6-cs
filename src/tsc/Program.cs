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
        string? batchManifest = null;

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
            if (equals >= 0 && ValueOptions.Contains(name[..equals]))
            {
                // Only a value-taking option may carry `--flag=value`. The reference answers
                // `Unknown compiler option '--strict=false'` for a boolean written that way, so the whole token stays
                // the option's name here too.
                value = name[(equals + 1)..];
                name = name[..equals];
            }
            else if (equals >= 0)
            {
                name = arg.TrimStart('-');
            }
            else if (ValueOptions.Contains(name))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                {
                    value = args[++i];
                }
                else
                {
                    // A value-taking option with nothing after it: the reference answers TS6044, and for an
                    // enum-valued option the accepted-value list (TS6046) as well — `tsc --target` prints both.
                    var canonical = CanonicalOptionName(name);
                    var expectsArgument = DiagnosticText(Diagnostics.Compiler_option_0_expects_an_argument, canonical);
                    if (EnumValuesByName.TryGetValue(canonical, out var enumValues))
                    {
                        expectsArgument = string.Join(
                            Environment.NewLine,
                            expectsArgument,
                            DiagnosticText(
                                Diagnostics.Argument_for_0_option_must_be_Colon_1,
                                "--" + canonical,
                                enumValues.ReportedList));
                    }

                    return UsageError(expectsArgument);
                }
            }
            else if (i + 1 < args.Length && args[i + 1] is "true" or "false")
            {
                // The reference accepts `--strict true|false` for its boolean options and consumes the token, which is
                // what keeps `true` out of the file list (see ShouldConsumeValue).
                value = args[++i];
            }

            if (name is "version" or "v")
            {
                Console.Out.Write(VersionBanner);
                return 0;
            }

            if (name is "help" or "h" or "?")
            {
                Console.Out.Write(VersionBanner);
                Console.Out.Write(Usage);
                return 0;
            }

            if (name is "project" or "p")
            {
                // `-p/--project` takes the path of a tsconfig.json. Reading one is not ported yet (M8's
                // commandLineParser/tsconfig reader), so the front end answers exactly what the reference answers
                // *before* it reads anything — TS5058 for a path that does not exist — and refuses loudly otherwise
                // rather than pretending to have read a config.
                if (value is null)
                {
                    return UsageError(DiagnosticText(Diagnostics.Compiler_option_0_expects_an_argument, name));
                }

                var projectPath = Path.GetFullPath(value);
                if (!File.Exists(projectPath) && !Directory.Exists(projectPath))
                {
                    return UsageError(DiagnosticText(Diagnostics.The_specified_path_does_not_exist_Colon_0, projectPath.Replace('\\', '/')));
                }

                return UsageError("error: reading tsconfig.json is not ported yet (M8's commandLineParser); this front end takes a file list only.");
            }

            if (name == "lib-dir")
            {
                if (value is null) { return UsageError("Option '--lib-dir' expects a directory."); }
                libDirectory = value;
                continue;
            }

            if (name == "batch")
            {
                if (value is null) { return UsageError("Option '--batch' expects a manifest path."); }
                batchManifest = value;
                continue;
            }

            if (!Apply(options, name, value, out var optionError))
            {
                // The reference's TS5023, with the token as the user wrote it (`--badflag`, `-z`, `--strict=false`).
                return UsageError(optionError ?? DiagnosticText(Diagnostics.Unknown_compiler_option_0, arg));
            }
        }

        if (batchManifest is not null)
        {
            return RunBatch(batchManifest, libDirectory ?? ResolveLibDirectory());
        }

        if (fileNames.Count == 0)
        {
            // tsgo prints the version banner and the help text and answers `DiagnosticsPresent_OutputsSkipped`
            // (`execute/tsc.go`'s no-input arm); so does this front end.
            Console.Out.Write(VersionBanner);
            Console.Out.Write(Usage);
            return 1;
        }

        var system = new DiskSystem(libDirectory ?? ResolveLibDirectory());
        var host = PortProgram.CreateCompilerHostWorker(options, /*setParentNodes*/ false, system);
        var program = PortProgram.CreateProgram(fileNames, options, host);

        var (diagnostics, emitSkipped) = CollectDiagnostics(program, options);
        var report = PortProgram.FormatDiagnostics(diagnostics, new FormatHost(system.GetCurrentDirectory(), system.NewLine));
        if (report.Length != 0)
        {
            Console.Out.Write(report);
        }

        if (!diagnostics.Any(d => d.Category == DiagnosticCategory.Error))
        {
            return (int)ExitStatus.Success;
        }

        // tsgo's ExitStatus (execute/tsc/compile.go:52-60): diagnostics present, with the outputs *skipped* when the
        // emit did not run and *generated* when it did. `--noEmit` therefore answers 1 and a plain `tsc bad.ts` 2 —
        // the reference's own distinction, and the one the differential ring reads.
        return (int)(options.NoEmit == true || emitSkipped
            ? ExitStatus.DiagnosticsPresent_OutputsSkipped
            : ExitStatus.DiagnosticsPresent_OutputsGenerated);
    }

    /// <summary>
    /// The batch form of this front end: one process, many cases (the shape a differential ring needs).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Why it exists (docs/architecture.md §18.7/§18.8): a one-file program costs 1224 ms in a fresh process, of which
    /// 203 ms is CLR start + JIT and ~385 ms is parse+bind of the 93 default-library files — paid *per case* by a ring
    /// that spawns a process per case (5,907 cases ≈ 2 h). This mode keeps one process (and therefore one JIT and one
    /// <see cref="LibSourceFileRegistry"/>, i.e. one parse+bind of the libraries) and runs a case per manifest line.
    /// </para>
    /// <para>
    /// The manifest is JSON Lines, one case per line:
    /// <c>{"id": "&lt;label&gt;", "cwd": "&lt;dir&gt;", "roots": ["a.ts", …], "flags": ["--target", "ES6", …]}</c>.
    /// The flags are this front end's own command-line flags, applied by the very parser the single-case form uses, so
    /// a case cannot mean something in batch mode that it would not mean on its own. Each case's report is written
    /// between <c>&gt;&gt;&gt;CASE &lt;id&gt;</c> and <c>&gt;&gt;&gt;END &lt;id&gt;</c>.
    /// </para>
    /// <para>
    /// PORT-NOTE: the working directory is switched per case (<c>Directory.SetCurrentDirectory</c>) because the
    /// reference's diagnostics are printed relative to it, and the disk host memoizes it at construction — so the host
    /// is rebuilt per case while the library registry is shared. A case whose flags or manifest line are malformed is
    /// reported as its own failure and the batch continues: one bad case must not cost the other 5,906.
    /// </para>
    /// </remarks>
    private static int RunBatch(string manifestPath, string libDirectory)
    {
        var registry = new LibSourceFileRegistry(libDirectory);
        var startDirectory = Directory.GetCurrentDirectory();
        var failures = 0;
        var cases = 0;

        foreach (var line in File.ReadLines(manifestPath))
        {
            if (line.Length == 0) continue;
            cases++;
            string id = $"case-{cases}";
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.TryGetProperty("id", out var idElement)) id = idElement.GetString() ?? id;
                var cwd = root.TryGetProperty("cwd", out var cwdElement) ? cwdElement.GetString() : null;
                if (!string.IsNullOrEmpty(cwd))
                {
                    // Resolved against the directory the process started in, not the previous case's: a batch that
                    // switched to case 1's directory first would otherwise resolve case 2's relative paths inside it.
                    Directory.SetCurrentDirectory(Path.IsPathRooted(cwd) ? cwd : Path.Combine(startDirectory, cwd));
                }

                var options = new TscCompilerOptions();
                if (root.TryGetProperty("flags", out var flagsElement))
                {
                    var flags = flagsElement.EnumerateArray().Select(e => e.GetString() ?? "").ToArray();
                    for (var i = 0; i < flags.Length; i++)
                    {
                        var name = flags[i].TrimStart('-');
                        string? value = null;
                        var equals = name.IndexOf('=');
                        if (equals >= 0) { value = name[(equals + 1)..]; name = name[..equals]; }
                        else if (i + 1 < flags.Length && ShouldConsumeValue(name, flags[i + 1])) value = flags[++i];
                        if (!Apply(options, name, value, out var error))
                        {
                            throw new InvalidOperationException(error ?? $"unknown option '--{name}'");
                        }
                    }
                }

                var roots = root.TryGetProperty("roots", out var rootsElement)
                    ? rootsElement.EnumerateArray().Select(e => e.GetString() ?? "").ToList()
                    : [];

                var system = new DiskSystem(libDirectory);
                var host = PortProgram.CreateCompilerHostWorker(options, /*setParentNodes*/ false, system, registry);
                var program = PortProgram.CreateProgram(roots, options, host);
                var report = PortProgram.FormatDiagnostics(CollectDiagnostics(program, options).Diagnostics, new FormatHost(system.GetCurrentDirectory(), system.NewLine));

                Console.Out.Write($">>>CASE {id}\n");
                Console.Out.Write(report);
                Console.Out.Write($">>>END {id}\n");
            }
            catch (Exception e)
            {
                failures++;
                Console.Out.Write($">>>CASE {id}\n>>>FAIL {id}: {e.GetType().Name}: {e.Message}\n>>>END {id}\n");
            }
        }

        Console.Out.Write($"# batch cases={cases} failures={failures} lib-cache hits={registry.Hits} misses={registry.Misses}\n");
        return failures == 0 ? 0 : 1;
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
    private static (List<Diagnostic> Diagnostics, bool EmitSkipped) CollectDiagnostics(PortProgram program, TscCompilerOptions options)
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

        var emitSkipped = true;
        if (options.ListFilesOnly != true)
        {
            var emit = program.Emit();
            emitSkipped = emit.EmitSkipped;
            if (emit.Diagnostics is not null) all.AddRange(emit.Diagnostics);
        }

        return ([.. Utilities.SortAndDeduplicateDiagnostics(all.Cast<Diagnostic>().ToList())], emitSkipped);
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

    /// <summary>An option the front end cannot honour: reported exactly as the reference reports it — the reference's
    /// own diagnostic, through the same formatter, on stdout, with
    /// <c>ExitStatusDiagnosticsPresent_OutputsSkipped</c> (what tsgo answers for `--badflag`).</summary>
    private static int UsageError(string diagnosticLine)
    {
        Console.Out.WriteLine(diagnosticLine);
        return (int)ExitStatus.DiagnosticsPresent_OutputsSkipped;
    }

    /// <summary>Formats one command-line diagnostic (no file, no location) the way the reference prints it:
    /// <c>error TS5023: Unknown compiler option '--x'.</c></summary>
    private static string DiagnosticText(DiagnosticMessage message, params object?[] args)
        => PortProgram.FormatDiagnostics([Utilities.CreateCompilerDiagnostic(message, args)], CliFormatHost).TrimEnd();

    /// <summary>The formatter a command-line diagnostic needs: no file, so only the newline is read.</summary>
    private static readonly FormatHost CliFormatHost = new(Directory.GetCurrentDirectory().Replace('\\', '/') + "/", Environment.NewLine);

    /// <summary>The banner tsgo prints for `--version` and ahead of the help text.</summary>
    private static readonly string VersionBanner = $"Version {VersionInfo.Version}{Environment.NewLine}";

    /// <summary>Whether <paramref name="next"/> is the value of <paramref name="name"/> rather than the next
    /// argument.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's command line accepts both <c>--strict</c> and <c>--strict true|false</c> for its
    /// boolean options (commandLineParser.ts's <c>parseCommandLineWorker</c>), and consuming the token is what keeps
    /// `true` out of the file list. Without it a differential run's own `--declaration true` would arrive as an extra
    /// input file named `true`, which the compiler then reports as a missing file — a harness bug that looks exactly
    /// like a compiler crash (`ProgramDiagnostics.CreateDiagnosticExplainingFile`). The batch form reads its flags
    /// through this same rule.
    /// </remarks>
    private static bool ShouldConsumeValue(string name, string next)
        => ValueOptions.Contains(name) || next is "true" or "false";
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
          --batch <file>    (port only: run every case in a JSON Lines manifest; see RunBatch)

        """;

    /// <summary>The options that take a separate value argument (<c>--target es5</c> as well as <c>--target=es5</c>).
    /// Every other option is a boolean flag.</summary>
    private static readonly HashSet<string> ValueOptions = new(StringComparer.Ordinal)
    {
        "target", "module", "moduleResolution", "jsx", "jsxFactory", "jsxFragmentFactory", "jsxImportSource",
        "lib", "types", "typeRoots", "moduleDetection", "newLine", "outDir", "outFile", "rootDir", "declarationDir",
        "baseUrl", "sourceRoot", "mapRoot", "lib-dir", "pretty", "batch", "project", "p",
    };

    /// <summary>Applies one command-line option. <paramref name="value"/> is <c>null</c> for the bare-flag form.</summary>
    private static bool Apply(TscCompilerOptions o, string name, string? value, out string? error)
    {
        error = null;
        switch (name)
        {
            // ---- module/target/jsx family ----
            case "target":
                // tsgo 7.0 removed ES5 as a target: `--target es5` answers TS5108 (measured), and 'es5' is not in the
                // value list its TS6046 prints. The port still *maps* the name here so the message is the reference's.
                if (value is not null && value.Equals("es5", StringComparison.OrdinalIgnoreCase))
                {
                    error = DiagnosticText(Diagnostics.Option_0_1_has_been_removed_Please_remove_it_from_your_configuration, "target", "ES5");
                    return false;
                }

                return SetEnum<ScriptTarget>(name, value, TargetValues, v => o.Target = v, out error);
            case "module": return SetEnum<ModuleKind>(name, value, ModuleValues, v => o.Module = v, out error);
            case "moduleResolution": return SetEnum<ModuleResolutionKind>(name, value, ModuleResolutionValues, v => o.ModuleResolution = v, out error);
            case "jsx": return SetEnum<JsxEmit>(name, value, JsxValues, v => o.Jsx = v, out error);
            case "moduleDetection": return SetEnum<ModuleDetectionKind>(name, value, ModuleDetectionValues, v => o.ModuleDetection = v, out error);
            case "newLine": return SetEnum<NewLineKind>(name, value, NewLineValues, v => o.NewLine = v, out error);

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
                error = null;
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

    /// <summary>The name the reference uses when it reports an option: measured, tsgo accepts `-p`, `--p`,
    /// `-project` and `--project` alike (it strips one or two leading dashes and looks the name up in a table that
    /// holds both the long names and the single-letter aliases), and its messages always use the long name — `tsc -p`
    /// answers "Compiler option 'project' expects an argument."</summary>
    private static string CanonicalOptionName(string name) => name switch
    {
        "p" => "project",
        "v" => "version",
        "h" => "help",
        _ => name,
    };

    /// <summary>Parses an enum-valued option against the reference's own value table
    /// (<c>tsgo/internal/tsoptions/enummaps.go</c>) and reports a bad value the way the reference does: TS6046 with
    /// the accepted names, in the table's order and quoted as the reference quotes them. A missing value answers both
    /// TS6044 and that list, which is what `tsc --target` prints.</summary>
    private static bool SetEnum<TEnum>(
        string name,
        string? value,
        EnumValues values,
        Action<TEnum> set,
        out string? error)
        where TEnum : struct, Enum
    {
        var valueList = DiagnosticText(
            Diagnostics.Argument_for_0_option_must_be_Colon_1,
            "--" + CanonicalOptionName(name),
            values.ReportedList);

        if (value is null)
        {
            error = string.Join(
                Environment.NewLine,
                DiagnosticText(Diagnostics.Compiler_option_0_expects_an_argument, CanonicalOptionName(name)),
                valueList);
            return false;
        }

        if (values.Map.TryGetValue(value.ToLowerInvariant(), out var enumName)
            && Enum.TryParse<TEnum>(enumName, ignoreCase: true, out var parsed))
        {
            set(parsed);
            error = null;
            return true;
        }

        error = valueList;
        return false;
    }

    /// <summary>One option's accepted values: the names in the reference's order (what TS6046 prints) and the C#
    /// enum member each maps to.</summary>
    private sealed record EnumValues(string[] Names, Dictionary<string, string> Map)
    {
        /// <summary>The names the reference *prints* when it rejects a value. A name the table still accepts but no
        /// longer lists (ES5 after tsgo removed it) is carried in <see cref="Map"/> and left out here.</summary>
        public string[] ReportedNames { get; init; } = Names;

        public string ReportedList => string.Join(", ", ReportedNames.Select(n => $"'{n}'"));
    }

    private static EnumValues Values(params (string Name, string EnumName)[] entries)
        => new([.. entries.Select(e => e.Name)], entries.ToDictionary(e => e.Name, e => e.EnumName, StringComparer.OrdinalIgnoreCase));

    /// <remarks>PORT-NOTE: the list is tsgo's own (measured: `tsc --target bogus` prints it), and it starts at
    /// <c>es6</c> — ES5 is gone in 7.0 (see the <c>target</c> arm of <see cref="Apply"/>). <c>es5</c> stays in the map
    /// so that the message for it is the reference's TS5108 rather than "unknown value".</remarks>
    private static readonly EnumValues TargetValues = Values(
        ("es5", "ES5"), ("es6", "ES2015"), ("es2015", "ES2015"), ("es2016", "ES2016"), ("es2017", "ES2017"),
        ("es2018", "ES2018"), ("es2019", "ES2019"), ("es2020", "ES2020"), ("es2021", "ES2021"), ("es2022", "ES2022"),
        ("es2023", "ES2023"), ("es2024", "ES2024"), ("es2025", "ES2025"), ("esnext", "ESNext")) with
    {
        ReportedNames = ["es6", "es2015", "es2016", "es2017", "es2018", "es2019", "es2020", "es2021", "es2022", "es2023", "es2024", "es2025", "esnext"],
    };

    private static readonly EnumValues ModuleValues = Values(
        ("commonjs", "CommonJS"), ("amd", "AMD"), ("system", "System"), ("umd", "UMD"), ("es6", "ES2015"),
        ("es2015", "ES2015"), ("es2020", "ES2020"), ("es2022", "ES2022"), ("esnext", "ESNext"), ("node16", "Node16"),
        ("node18", "Node18"), ("node20", "Node20"), ("nodenext", "NodeNext"), ("preserve", "Preserve"));

    private static readonly EnumValues ModuleResolutionValues = Values(
        ("node16", "Node16"), ("nodenext", "NodeNext"), ("bundler", "Bundler"), ("classic", "Classic"),
        ("node", "Node10"), ("node10", "Node10"));

    private static readonly EnumValues ModuleDetectionValues = Values(
        ("auto", "Auto"), ("legacy", "Legacy"), ("force", "Force"));

    private static readonly EnumValues JsxValues = Values(
        ("preserve", "Preserve"), ("react-native", "ReactNative"), ("react-jsx", "ReactJSX"),
        ("react-jsxdev", "ReactJSXDev"), ("react", "React"));

    /// <summary>PORT-NOTE: the reference's <c>newLine</c> is not an enum-valued option in the same sense (its values
    /// are <c>crlf</c>/<c>lf</c>), and the port's enum carries the two names the reference's value table does.</summary>
    private static readonly EnumValues NewLineValues = Values(("crlf", "CarriageReturnLineFeed"), ("lf", "LineFeed"));

    /// <summary>The options whose values come from a table, keyed by their canonical name — what the scanner needs to
    /// answer TS6046 alongside TS6044 when one of them is written without a value. Declared after the tables it reads:
    /// static initializers run in textual order, so a forward reference here would see nulls.</summary>
    private static readonly Dictionary<string, EnumValues> EnumValuesByName = new(StringComparer.Ordinal)
    {
        ["target"] = TargetValues,
        ["module"] = ModuleValues,
        ["moduleResolution"] = ModuleResolutionValues,
        ["jsx"] = JsxValues,
        ["moduleDetection"] = ModuleDetectionValues,
        ["newLine"] = NewLineValues,
    };

    /// <summary>The command line this front end accepts. Options it does not know are an error, never silently ignored:
    /// a differential run has to know exactly which options a case was compiled with.</summary>

    /// <summary>The <see cref="IFormatDiagnosticsHost"/> the plain report needs (program.ts:658-678): paths are
    /// printed relative to the current directory, case-insensitively on this platform.</summary>
    private sealed class FormatHost(string currentDirectory, string newLine) : IFormatDiagnosticsHost
    {
        public string GetCurrentDirectory() => currentDirectory;

        public string GetCanonicalFileName(string? fileName) => (fileName ?? "").ToLowerInvariant();

        public string GetNewLine() => newLine;
    }
}
