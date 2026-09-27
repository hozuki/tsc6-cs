using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// The tool's own entry-point class is named Program (tools/AstDiff, tools/BindDiff, tools/TypeDiff and tools/EmitDiff
// do the same), which is also the name of the port's class the driver creates a program with; the alias keeps the two
// apart.
using PortProgram = TypeScript.Compiler.Program;

namespace TypeScript.Compiler.Tools;

/// <summary>
/// BaselineDiff — the baseline differential, M7-C's outer ring: replay a corpus a second time, this time with the
/// <em>official test harness's</em> option model (the per-file `// @` directives, the harness's own defaults, and the
/// configurations <c>CompilerTest.varyBy</c> derives from them), render each case's baseline texts exactly as
/// <c>tests/baselines/reference</c> holds them, and compare the port's bytes against the checked-in baseline.
/// </summary>
/// <remarks>
/// <para>
/// The three rings that came before it share one fixed option object (<c>tools/TypeDiff</c>, <c>tools/EmitDiff</c>) or
/// no program at all; none of them exercises a `// @` directive, so none of them can tell whether the port agrees with
/// the reference under the options the corpus's own tests choose. This ring is that check, and it is the only one whose
/// third party is not the reference compiler but a file: the checked-in baseline.
/// </para>
/// <para>
/// The comparison is therefore three-way, with two verdicts that must be kept apart:
/// </para>
/// <list type="bullet">
/// <item><description><b>port vs baseline</b> — the ring's verdict. A divergence here is a port defect, or a
/// divergence inside a baseline that was written by something other than this reference version.</description></item>
/// <item><description><b>reference vs baseline</b> — the harness model's own check. The reference half runs the same
/// harness model over the same corpus with <c>lib/typescript.js</c>; if it cannot reproduce the checked-in bytes, the
/// harness model is wrong and every port verdict from that case is worth nothing. The report counts these separately
/// and never folds them into the port's.</description></item>
/// </list>
/// <para>
/// Both halves write their verdict per (baseline stem, artifact) in one line format, and the driver derives the
/// baseline side itself by reading <c>tests/baselines/reference</c>:
/// <c>&lt;stem&gt;\t&lt;kind&gt;\t&lt;state&gt;\t&lt;hash&gt;\t&lt;length&gt;</c>, where <c>state</c> is <c>text</c> (a
/// baseline text was produced), <c>null</c> (the generator answered <c>null</c>, which the baseline machinery compares
/// as the literal <c>&lt;no content&gt;</c> — its hash is on the line), <c>skipped</c> (the harness does not check this
/// baseline for this case at all), <c>error</c> or <c>unmodelled</c>. Only <c>text</c> and <c>null</c> can compare as
/// agreement, and each artifact is compared against the baseline file itself or against <c>&lt;no content&gt;</c> when
/// the corpus has none.
/// </para>
/// <para>
/// Nothing carries a timestamp and both halves walk the corpus in sorted order, so two runs over the same corpus
/// produce the same hashes, the same divergence lines and the same <c>SUMMARY</c>; only the self-reported peak numbers
/// (the port's working set and each node batch's <c>maxRSS</c>) can differ, because they are measurements.
/// </para>
/// </remarks>
internal static partial class Program
{
    private const string Usage = """
        BaselineDiff — baseline differential harness (port vs the checked-in conformance baselines).

        Usage: dotnet run --project tools/BaselineDiff -- --cases <dir> --baselines <dir> [options]

          --cases <dir>      corpus root (e.g. <TypeScript>/tests/cases/conformance)          [required]
          --baselines <dir>  the checked-in baselines (e.g. <TypeScript>/tests/baselines/reference) [required]
          --filter <glob>    matches the '/'-separated path relative to --cases;
                             '*' does not cross '/', '**' does (e.g. classes/**)
          --limit <n>        keep the first n files after filtering (sorted by path)
          --out <dir>        output directory (default: <repo>/artifacts/baselinediff)
          --js <file>        the reference lib/typescript.js (default: nearest ancestor of
                             --cases that has one, else the sibling TS checkout)
          --lib <dir>        the default-library directory the reference half mounts at /.ts
                             (default: <workspace root>/lib)
          --test-lib <dir>   the extra test libraries mounted at /.lib (default: <workspace root>/tests/lib)
          --workspace <dir>  the checkout the `.js`/`.types`/`.symbols` headers name their file against
                             (default: the nearest ancestor of --cases that holds tests/baselines)
          --node <exe>       node executable (default: node)
          --node-script <f>  the batch reference half (default: <tool bin>/ts-baseline.cjs)
          --node-batch <n>   files per node process (default 250; each batch reports its own peak RSS)
          --node-parallel <n>  node batches to run at once (default 1; the outputs are concatenated in batch order)
          --artifacts <list> which baseline kinds to compare, comma-separated
                             (default: js,errors,types,symbols)

        The corpus files are the `.ts`/`.tsx` ones under --cases, which is what the conformance runner enumerates
        (src/testRunner/compilerRunner.ts:54-57). Each file becomes one case per configuration the harness derives from
        its own directives, and each case is compared against up to four baselines named after the file's base name
        (`foo.ts`, `foo(target=es5).ts`, …).

        Exit code: 0 = every artifact matched, 1 = divergences or failures were found (see <out>/report.txt),
        2 = bad arguments, 3 = node could not run.
        """;

    /// <summary>FNV-1a's 64-bit offset basis and prime; hashed over UTF-16 code units, so both sides hash exactly the
    /// same sequence without an encoding step in between (the same choice tools/AstDiff, tools/BindDiff, tools/TypeDiff
    /// and tools/EmitDiff record).</summary>
    private const ulong FnvOffset = 14695981039346656037UL;

    private const ulong FnvPrime = 1099511628211UL;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Every artifact kind this tool can produce, in report order.</summary>
    private static readonly string[] AllKinds = ["js", "errors", "types", "symbols"];

    /// <summary>The per-artifact columns of the triage table.</summary>
    private static readonly string[] Columns = ["same", "diverged", "port-crash", "ts-error", "unpaired", "unmodelled", "skipped", "extra-baseline"];

    private static int Main(string[] args)
    {
        // 数字（长度/位置/诊断码）必须与 node 侧输出逐字符相同，所以整个进程用不变文化。
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

        if (!TryParseArguments(args, out var settings, out var error) || settings is null)
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        var files = EnumerateFiles(settings.Cases, settings.Filter, settings.Limit);
        if (files.Count == 0)
        {
            Console.Error.WriteLine($"baselinediff: no files matched under {settings.Cases}" + (settings.Filter is null ? "" : $" (filter '{settings.Filter}')"));
            return 2;
        }

        Directory.CreateDirectory(settings.Out);
        var paths = new OutputPaths(settings.Out);
        foreach (var stale in new[] { paths.PortHashes, paths.PortErrors, paths.TsHashes, paths.TsErrors, paths.TsSummary, paths.Report, paths.Divergences, paths.Manifest })
        {
            File.Delete(stale);
        }

        // The batched node pass writes one hash/error file per batch; a previous run may have left more of them than
        // this one produces, and the comparison must not read another run's leftovers.
        foreach (var stale in Directory.GetFiles(settings.Out, "ts-hashes.*.txt").Concat(Directory.GetFiles(settings.Out, "ts-errors.*.txt")))
        {
            File.Delete(stale);
        }

        var context = new BaselineDiffContext(settings.Workspace, settings.Lib, settings.TestLib, settings.Baselines, settings.Cases);
        Console.Error.WriteLine($"baselinediff: {files.Count} files under {settings.Cases}; compiling them with the port");
        var started = Stopwatch.StartNew();
        var portCases = new List<PortCase>(files.Count);
        using (var hashWriter = new StreamWriter(File.Open(paths.PortHashes, FileMode.Create), Utf8NoBom) { AutoFlush = true, NewLine = "\n" })
        using (var errorWriter = new StreamWriter(File.Open(paths.PortErrors, FileMode.Create), Utf8NoBom) { AutoFlush = true, NewLine = "\n" })
        {
            // The checker, the transformer chain and the printer all recurse; 256 MiB of reserve (virtual on x64)
            // keeps a legitimately deep file from dying, the same reserve the sibling tools take.
            var worker = new Thread(() => PortPass(settings, context, files, portCases, hashWriter, errorWriter), 256 * 1024 * 1024);
            worker.Start();
            worker.Join();
        }

        var portElapsed = started.Elapsed;
        if (!WriteManifest(paths.Manifest, settings, files, out error))
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        if (!RunNode(settings, paths, files, out error))
        {
            Console.Error.WriteLine(error);
            return 3;
        }

        var portHashes = ReadIndex(paths.PortHashes);
        var tsHashes = ReadIndex(paths.TsHashes);
        var expected = ExpectedBaselines(settings, portHashes, tsHashes);
        var comparison = Compare(settings, expected, portHashes, tsHashes);
        DetailPortDivergences(settings, context, paths, portCases, comparison);
        DetailReferenceDivergences(settings, paths, comparison);
        // A shard's baselines are mostly other files' cases, so the check only runs on a whole-corpus pass; the report
        // says so when it was skipped.
        if (settings.Filter is null && settings.Limit is null) CountExtraBaselines(settings, comparison, expected, portHashes, tsHashes);

        var report = BuildReport(settings, files.Count, comparison, ReadLines(paths.PortErrors), ReadLines(paths.TsErrors), portElapsed);
        File.WriteAllText(paths.Report, report, Utf8NoBom);
        File.WriteAllText(paths.Divergences, BuildJsonLines(comparison), Utf8NoBom);
        Console.Out.Write(report);

        // Contract (the file header): 0 = every artifact matched. A skipped baseline is not a match and an extra
        // baseline is a case the model never derived, so neither can make a run clean either; only `same` can.
        var clean = comparison.PortDiverged.Count == 0
            && comparison.PortCrashes == 0
            && comparison.PortUnmodelled == 0
            && comparison.PortUnpaired == 0
            && comparison.ExtraBaselines == 0
            && comparison.ReferenceDiverged.Count == 0
            && comparison.ReferenceCrashes == 0;
        var verdict = clean ? 0 : 1;
        Console.Error.WriteLine(
            $"baselinediff: VERDICT {(clean ? "clean" : "fail")} cases={comparison.CaseCount} same={comparison.PortSame}"
            + $" port-diverge={comparison.PortDiverged.Count} port-crash={comparison.PortCrashes} port-unmodelled={comparison.PortUnmodelled}"
            + $" ref-diverge={comparison.ReferenceDiverged.Count} ref-crash={comparison.ReferenceCrashes}"
            + $" unpaired={comparison.PortUnpaired} extra-baseline={comparison.ExtraBaselines} exit={verdict}");
        Console.Error.WriteLine($"baselinediff: port pass {portElapsed.TotalSeconds:F2}s, total {started.Elapsed.TotalSeconds:F2}s");
        return verdict;
    }

    // ------------------------------------------------------------------------------------------------------------
    // The port pass.
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>One case's port-side verdict, kept in memory so a diverging artifact can be re-derived for its first
    /// differing line without re-deriving every other case.</summary>
    private sealed record PortCase(string Stem, string File, BaselineCase Case);

    private static void PortPass(
        Settings settings,
        BaselineDiffContext context,
        List<CorpusFile> files,
        List<PortCase> cases,
        TextWriter hashes,
        TextWriter errors)
    {
        foreach (var file in files)
        {
            string text;
            try
            {
                text = ReadText(Path.Combine(settings.Cases, file.Rel.Replace('/', Path.DirectorySeparatorChar)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.WriteLine($"{Paths.GetBaseFileName(file.Rel)}\tread-error: {ex.GetType().Name}: {FirstLine(ex.Message)}");
                continue;
            }

            List<BaselineCase> derived;
            try
            {
                derived = BaselineDiffHarnessModel.DeriveCases(RelativeCasePath(context, file.Rel), text);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The file itself could not be modelled (a tsconfig unit, a symlink, an unknown directive): the stem it
                // would have had is reported once, and the reference half reports the same thing independently.
                var stem = BaselineDiffHarnessModel.ConfiguredName(Paths.GetBaseFileName(file.Rel), null);
                errors.WriteLine($"{stem}\t{StateOf(ex)}: {ex.GetType().Name}: {FirstLine(ex.Message)} @ {FrameNames(ex)}");
                foreach (var kind in settings.Artifacts) hashes.WriteLine($"{stem}\t{kind}\t{StateOf(ex)}\t\t");
                continue;
            }

            foreach (var derivedCase in derived)
            {
                var stem = BaselineDiffHarnessModel.ConfiguredName(Paths.GetBaseFileName(file.Rel), derivedCase.Configuration);
                cases.Add(new PortCase(stem, file.Rel, derivedCase));
                Dictionary<string, BaselineArtifact> produced;
                try
                {
                    produced = RunCase(settings, context, file.Rel, derivedCase);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    errors.WriteLine($"{stem}\t{StateOf(ex)}: {ex.GetType().Name}: {FirstLine(ex.Message)} @ {FrameNames(ex)}");
                    foreach (var kind in settings.Artifacts) hashes.WriteLine($"{stem}\t{kind}\t{StateOf(ex)}\t\t");
                    continue;
                }

                foreach (var kind in settings.Artifacts)
                {
                    var artifact = produced.GetValueOrDefault(kind) ?? BaselineArtifact.Skipped;
                    hashes.WriteLine($"{stem}\t{kind}\t{artifact.State}\t{artifact.Hash}\t{artifact.Length}");
                }
            }
        }
    }

    /// <summary>`unmodelled` is the harness model's own gap and `error` is anything else — a port defect, an invariant
    /// the harness enforces, or a tool bug. The two are counted apart because only the second is a port result.</summary>
    private static string StateOf(Exception ex) => ex is BaselineModelException ? "unmodelled" : "error";

    /// <summary>One artifact's produced text, or the state that stands in for it.</summary>
    private sealed record BaselineArtifact(string State, string Hash, int Length, string? Text)
    {
        public static readonly BaselineArtifact Skipped = new("skipped", "", 0, null);

        public static BaselineArtifact None() => new("null", Hash16(NoContent), NoContent.Length, null);

        public static BaselineArtifact From(string text) => new("text", Hash16(text), text.Length, text);
    }

    private static string NoContent => BaselineDiffBaselines.NoContent;

    /// <summary>Runs one case with the port and produces the baseline texts the harness would check in. A missing entry
    /// is "the harness does not check this baseline for this case", and <c>null</c> is the harness's "the generator
    /// answered null".</summary>
    private static Dictionary<string, BaselineArtifact> RunCase(Settings settings, BaselineDiffContext context, string rel, BaselineCase derivedCase)
    {
        var units = BaselineDiffHarnessModel.AssembleUnits(derivedCase.Content, derivedCase.Settings);
        var result = BaselineDiffHarnessModel.CompileFiles(
            units.ToBeCompiled,
            units.OtherFiles,
            derivedCase.Settings,
            null,
            settings.CurrentDirectoryOf(derivedCase.Settings),
            context);
        var options = result.Options;
        var header = RelativeCasePath(context, rel);
        var produced = new Dictionary<string, BaselineArtifact>(StringComparer.Ordinal);

        if (settings.Artifacts.Contains("js") && units.HasNonDtsFiles)
        {
            var js = BaselineDiffJsBaseline.DoJsEmitBaseline(header, options, result, units.TsConfigFiles, units.ToBeCompiled, units.OtherFiles, derivedCase.Settings, context);
            produced["js"] = js is null ? BaselineArtifact.None() : BaselineArtifact.From(js);
        }

        if (settings.Artifacts.Contains("errors"))
        {
            var errors = BaselineDiffBaselines.DoErrorBaseline(
                [.. units.TsConfigFiles, .. units.ToBeCompiled, .. units.OtherFiles],
                result.Diagnostics,
                options.Pretty == true);
            produced["errors"] = errors is null ? BaselineArtifact.None() : BaselineArtifact.From(errors);
        }

        var noTypesAndSymbols = derivedCase.Settings.TryGetValue("noTypesAndSymbols", out var raw) && raw.ToLowerInvariant() == "true";
        if (!noTypesAndSymbols)
        {
            var allFiles = units.ToBeCompiled.Concat(units.OtherFiles)
                .Where(file => result.Program.GetSourceFile(file.UnitName) is not null)
                .Select(file => (file.UnitName, file.Content))
                .ToList();
            var hasErrors = result.Diagnostics.Count != 0;
            if (settings.Artifacts.Contains("types"))
            {
                var types = BaselineTypeWriter.Generate(result.Program, header, allFiles, hasErrors, isSymbolBaseline: false, underlines: settings.TypeUnderlines);
                produced["types"] = types is null ? BaselineArtifact.None() : BaselineArtifact.From(types);
            }

            if (settings.Artifacts.Contains("symbols"))
            {
                var symbols = BaselineTypeWriter.Generate(result.Program, header, allFiles, hasErrors, isSymbolBaseline: true, underlines: settings.TypeUnderlines);
                produced["symbols"] = symbols is null ? BaselineArtifact.None() : BaselineArtifact.From(symbols);
            }
        }

        return produced;
    }

    /// <summary>The path a baseline header prints: the case file's path relative to the workspace root
    /// (<c>tests/cases/conformance/…</c>), which is what <c>CompilerBaselineRunner</c> enumerates and hands to
    /// <c>doJsEmitBaseline</c>/<c>doTypeAndSymbolBaseline</c> as their <c>header</c>.</summary>
    private static string RelativeCasePath(BaselineDiffContext context, string rel)
        => Path.GetRelativePath(context.WorkspaceRoot, Path.Combine(context.CasesDirectory, rel.Replace('/', Path.DirectorySeparatorChar))).Replace('\\', '/');

    // ------------------------------------------------------------------------------------------------------------
    // The baseline side and the comparison.
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>One (stem, artifact) the two halves named, with the checked-in baseline's own hash and what each half
    /// made of it.</summary>
    private sealed record Row(
        string Stem,
        string Kind,
        string BaselineName,
        bool BaselineExists,
        string ExpectedHash,
        int ExpectedLength,
        string? PortState,
        string PortHash,
        string? ReferenceState,
        string ReferenceHash);

    private sealed class Comparison
    {
        public List<Row> Rows { get; } = [];

        public List<(Row Row, string Class, int Line, int Column, string? ProducedLine, string? BaselineLine)> PortDiverged { get; } = [];

        public List<(Row Row, string Class, int Line, int Column, string? ReferenceLine, string? BaselineLine)> ReferenceDiverged { get; } = [];

        public int PortSame { get; set; }

        public int PortCrashes { get; set; }

        public int PortUnmodelled { get; set; }

        public int PortUnpaired { get; set; }

        public int ReferenceCrashes { get; set; }

        public int ExtraBaselines { get; set; }

        public int CaseCount { get; set; }

        public Dictionary<string, int[]> ByKind { get; } = [];

        public void Count(string kind, string column)
        {
            if (!ByKind.TryGetValue(kind, out var counts)) ByKind[kind] = counts = new int[Columns.Length];
            counts[Array.IndexOf(Columns, column)]++;
        }
    }

    private static Comparison Compare(
        Settings settings,
        Dictionary<(string Stem, string Kind), Row> expected,
        Dictionary<string, string> portHashes,
        Dictionary<string, string> tsHashes)
    {
        var comparison = new Comparison();
        comparison.CaseCount = portHashes.Keys.Select(key => SplitKey(key).Item1).Distinct(StringComparer.Ordinal).Count();
        foreach (var key in expected.Keys.OrderBy(key => key.Stem, StringComparer.Ordinal).ThenBy(key => key.Kind, StringComparer.Ordinal))
        {
            var row = expected[key];
            var port = portHashes.GetValueOrDefault(HashKey(key.Stem, key.Kind));
            var reference = tsHashes.GetValueOrDefault(HashKey(key.Stem, key.Kind));
            var withPort = row with
            {
                PortState = port is null ? null : port.Split('\t')[0],
                PortHash = port is null ? "" : port.Split('\t')[1],
                ReferenceState = reference is null ? null : reference.Split('\t')[0],
                ReferenceHash = reference is null ? "" : reference.Split('\t')[1],
            };
            comparison.Rows.Add(withPort);

            // The reference half is the harness model's own check: a reference-vs-baseline mismatch means the model is
            // wrong, so it is reported on its own and never counted as the port's verdict.
            switch (withPort.ReferenceState)
            {
                case "text" or "null":
                    if (withPort.ReferenceHash != withPort.ExpectedHash)
                    {
                        comparison.ReferenceDiverged.Add((withPort, "reference differs from the checked-in baseline", 0, 0, null, null));
                    }

                    break;
                case "error" or "unmodelled":
                    comparison.ReferenceCrashes++;
                    break;
            }

            switch (withPort.PortState)
            {
                case "text" or "null":
                    if (withPort.PortHash == withPort.ExpectedHash)
                    {
                        comparison.PortSame++;
                        comparison.Count(key.Kind, "same");
                    }
                    else
                    {
                        comparison.PortDiverged.Add((withPort, "bytes differ", 0, 0, null, null));
                        comparison.Count(key.Kind, "diverged");
                    }

                    break;
                case "skipped":
                    // The harness does not check this baseline for this case; a baseline that exists anyway is a case
                    // this tool never derived, which no port agreement can make up for.
                    if (withPort.BaselineExists)
                    {
                        comparison.ExtraBaselines++;
                        comparison.Count(key.Kind, "extra-baseline");
                    }
                    else
                    {
                        comparison.Count(key.Kind, "skipped");
                    }

                    break;
                case "error":
                    comparison.PortCrashes++;
                    comparison.Count(key.Kind, "port-crash");
                    break;
                case "unmodelled":
                    comparison.PortUnmodelled++;
                    comparison.Count(key.Kind, "unmodelled");
                    break;
                default:
                    comparison.PortUnpaired++;
                    comparison.Count(key.Kind, "unpaired");
                    break;
            }
        }

        return comparison;
    }

    /// <summary>Every baseline file name under <c>--baselines</c> whose kind this tool compares.</summary>
    private static void CountExtraBaselines(
        Settings settings,
        Comparison comparison,
        Dictionary<(string Stem, string Kind), Row> expected,
        Dictionary<string, string> portHashes,
        Dictionary<string, string> tsHashes)
    {
        foreach (var baselineName in EnumerateBaselineNames(settings))
        {
            var (stem, kind) = SplitBaselineName(baselineName);
            if (kind is null || !settings.Artifacts.Contains(kind)) continue;
            if (expected.ContainsKey((stem, kind))) continue;
            if (portHashes.ContainsKey(HashKey(stem, kind)) || tsHashes.ContainsKey(HashKey(stem, kind))) continue;
            comparison.ExtraBaselines++;
            comparison.Count(kind, "extra-baseline");
        }
    }

    private static Dictionary<(string Stem, string Kind), Row> ExpectedBaselines(
        Settings settings,
        Dictionary<string, string> portHashes,
        Dictionary<string, string> tsHashes)
    {
        var rows = new Dictionary<(string, string), Row>();
        var names = new HashSet<(string, string)>();
        foreach (var key in portHashes.Keys.Concat(tsHashes.Keys)) names.Add(SplitKey(key));

        foreach (var (stem, kind) in names)
        {
            if (!settings.Artifacts.Contains(kind)) continue;
            var baselineName = ArtifactName(stem, kind);
            var path = Path.Combine(settings.Baselines, baselineName);
            var exists = File.Exists(path);
            var text = exists ? ReadText(path) : NoContent;
            rows[(stem, kind)] = new Row(stem, kind, baselineName, exists, Hash16(text), text.Length, null, "", null, "");
        }

        return rows;
    }

    /// <summary>
    /// The first differing line pair of each port divergence, and the two full dumps: the port's artifact is
    /// re-derived for that one case (the pass that produced the hash kept no text) and written under
    /// <c>&lt;out&gt;/port</c>, and the checked-in baseline it was compared against is read from
    /// <c>tests/baselines/reference</c> — the third party is on disk, so no reference dump is needed to point at it.
    /// </summary>
    private static void DetailPortDivergences(
        Settings settings,
        BaselineDiffContext context,
        OutputPaths paths,
        List<PortCase> cases,
        Comparison comparison)
    {
        var byStem = cases.GroupBy(@case => @case.Stem, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        for (var i = 0; i < comparison.PortDiverged.Count; i++)
        {
            var (row, _, _, _, _, _) = comparison.PortDiverged[i];
            var @case = byStem.GetValueOrDefault(row.Stem);
            var portText = (string?)null;
            var failure = (string?)null;
            if (@case is not null)
            {
                try
                {
                    portText = RunCase(settings, context, @case.File, @case.Case).GetValueOrDefault(row.Kind)?.Text;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    failure = $"port-recheck-{StateOf(ex)}: {ex.GetType().Name}: {FirstLine(ex.Message)}";
                }
            }

            var baselineText = row.BaselineExists ? ReadText(Path.Combine(settings.Baselines, row.BaselineName)) : NoContent;
            if (portText is not null)
            {
                var target = Path.Combine(paths.PortDumps, row.BaselineName);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, portText, Utf8NoBom);
            }

            if (failure is null && portText is null)
            {
                comparison.PortDiverged[i] = (row, "the port produced no text on the re-run", 0, 0, null, null);
                continue;
            }

            var (line, column, classification, producedLine, baselineLine) = Classify(portText ?? "", baselineText, row.Kind);
            comparison.PortDiverged[i] = (row, failure ?? classification, failure is null ? line : 0, failure is null ? column : 0, producedLine, baselineLine);
        }
    }

    /// <summary>The reference half already wrote a dump for each of its own divergences, so its first differing line
    /// pair is read from <c>&lt;out&gt;/ts</c> and the checked-in baseline.</summary>
    private static void DetailReferenceDivergences(Settings settings, OutputPaths paths, Comparison comparison)
    {
        for (var i = 0; i < comparison.ReferenceDiverged.Count; i++)
        {
            var (row, _, _, _, _, _) = comparison.ReferenceDiverged[i];
            var referencePath = Path.Combine(paths.TsDumps, row.BaselineName);
            var referenceText = File.Exists(referencePath) ? ReadText(referencePath) : null;
            var baselineText = row.BaselineExists ? ReadText(Path.Combine(settings.Baselines, row.BaselineName)) : NoContent;
            if (referenceText is null)
            {
                comparison.ReferenceDiverged[i] = (row, "reference differs from the checked-in baseline (no dump written)", 0, 0, null, null);
                continue;
            }

            var (line, column, classification, referenceLine, baselineLine) = Classify(referenceText, baselineText, row.Kind);
            comparison.ReferenceDiverged[i] = (row, classification, line, column, referenceLine, baselineLine);
        }
    }

    /// <summary>The first differing line pair of two baseline texts, classified by what the two lines are.</summary>
    private static (int Line, int Column, string Classification, string? ProducedLine, string? BaselineLine) Classify(string produced, string baseline, string kind)
    {
        var producedLines = produced.Split('\n');
        var baselineLines = baseline.Split('\n');
        var index = 0;
        while (index < producedLines.Length && index < baselineLines.Length && producedLines[index] == baselineLines[index]) index++;
        var producedLine = index < producedLines.Length ? producedLines[index] : null;
        var baselineLine = index < baselineLines.Length ? baselineLines[index] : null;
        if (producedLine is null && baselineLine is null) return (index + 1, 1, "hash differs but the texts are identical (hash collision)", null, null);
        if (producedLine is null || baselineLine is null) return (index + 1, 1, "line-count mismatch: one text ends where the other continues", producedLine, baselineLine);
        var column = 1;
        var shared = Math.Min(producedLine.Length, baselineLine.Length);
        while (column <= shared && producedLine[column - 1] == baselineLine[column - 1]) column++;
        var classification = kind == "errors" ? "diagnostics text differs"
            : (producedLine.StartsWith("//// [", StringComparison.Ordinal) && baselineLine.StartsWith("//// [", StringComparison.Ordinal)
                ? "baseline header mismatch"
                : producedLine.StartsWith("=== ", StringComparison.Ordinal) && baselineLine.StartsWith("=== ", StringComparison.Ordinal)
                    ? "source-file section mismatch"
                    : WhitespaceOnly(producedLine, baselineLine) ? "text differs in whitespace only" : "text differs");
        return (index + 1, column, classification, producedLine, baselineLine);
    }

    private static bool WhitespaceOnly(string a, string b)
        => a.Where(ch => !char.IsWhiteSpace(ch)).SequenceEqual(b.Where(ch => !char.IsWhiteSpace(ch)));

    // ------------------------------------------------------------------------------------------------------------
    // The node pass.
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>Writes the manifest the reference half reads and hands a whole batch of files to one node process per
    /// batch, which is how both this tool and its siblings avoid one interpreter start per file.</summary>
    private static bool RunNode(Settings settings, OutputPaths paths, List<CorpusFile> files, out string error)
    {
        error = string.Empty;
        var batches = (files.Count + settings.NodeBatch - 1) / settings.NodeBatch;
        var hashFiles = new List<(int Batch, string Path)>(batches);
        var errorFiles = new List<(int Batch, string Path)>(batches);
        var summaries = new List<(int Batch, string Text)>(batches);
        var manifestDir = Path.Combine(settings.Out, "node");

        var failures = new List<string>();
        var pending = new List<Task<(int Batch, bool Ok, string Output, string HashFile, string ErrorFile, string Manifest)>>();
        using var throttle = new SemaphoreSlim(Math.Max(1, settings.NodeParallel));

        for (var batch = 0; batch < batches; batch++)
        {
            var slice = files.GetRange(batch * settings.NodeBatch, Math.Min(settings.NodeBatch, files.Count - (batch * settings.NodeBatch)));
            var chunkManifest = Path.Combine(manifestDir, $"chunk{batch:D4}.json");
            if (!WriteManifest(chunkManifest, settings, slice, out error)) return false;
            var chunkHashes = Path.Combine(settings.Out, $"ts-hashes.{batch:D4}.txt");
            var chunkErrors = Path.Combine(settings.Out, $"ts-errors.{batch:D4}.txt");
            var batchOut = Path.Combine(manifestDir, $"out{batch:D4}");
            Directory.CreateDirectory(batchOut);
            Console.Error.WriteLine($"baselinediff: node batch {batch + 1}/{batches} ({slice.Count} files)");

            var psi = new ProcessStartInfo(settings.Node)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = AppContext.BaseDirectory,
            };
            psi.ArgumentList.Add(settings.NodeScript);
            psi.ArgumentList.Add("--manifest");
            psi.ArgumentList.Add(chunkManifest);
            psi.ArgumentList.Add("--out");
            psi.ArgumentList.Add(batchOut);
            psi.ArgumentList.Add("--js");
            psi.ArgumentList.Add(settings.Js);
            psi.ArgumentList.Add("--baselines");
            psi.ArgumentList.Add(settings.Baselines);
            psi.ArgumentList.Add("--hashes");
            psi.ArgumentList.Add(chunkHashes);
            psi.ArgumentList.Add("--cases");
            psi.ArgumentList.Add(Path.Combine(batchOut, "ts-cases.jsonl"));
            psi.ArgumentList.Add("--errors");
            psi.ArgumentList.Add(chunkErrors);
            psi.ArgumentList.Add("--summary");
            psi.ArgumentList.Add(Path.Combine(batchOut, "ts-summary.txt"));
            psi.ArgumentList.Add("--dumps");
            psi.ArgumentList.Add(paths.TsDumps);
            if (settings.Lib is not null)
            {
                psi.ArgumentList.Add("--lib");
                psi.ArgumentList.Add(settings.Lib);
            }

            if (settings.TestLib is not null)
            {
                psi.ArgumentList.Add("--test-lib");
                psi.ArgumentList.Add(settings.TestLib);
            }

            pending.Add(RunBatchAsync(psi, batch, chunkHashes, chunkErrors, chunkManifest));
            if (pending.Count >= Math.Max(1, settings.NodeParallel))
            {
                foreach (var finished in Drain(pending, settings.NodeParallel))
                {
                    if (!finished.Ok)
                    {
                        failures.Add(finished.Output);
                        continue;
                    }

                    hashFiles.Add((finished.Batch, finished.HashFile));
                    errorFiles.Add((finished.Batch, finished.ErrorFile));
                    summaries.Add((finished.Batch, finished.Output));
                }
            }
        }

        foreach (var finished in Drain(pending, int.MaxValue))
        {
            if (!finished.Ok)
            {
                failures.Add(finished.Output);
                continue;
            }

            hashFiles.Add((finished.Batch, finished.HashFile));
            errorFiles.Add((finished.Batch, finished.ErrorFile));
            summaries.Add((finished.Batch, finished.Output));
        }

        if (failures.Count > 0)
        {
            error = string.Join("\n", failures);
            return false;
        }

        // The batches ran concurrently but their outputs are concatenated in batch order, so the merged files (and
        // therefore the report) do not depend on which batch finished first.
        Concatenate(hashFiles.OrderBy(entry => entry.Batch).Select(entry => entry.Path), paths.TsHashes);
        Concatenate(errorFiles.OrderBy(entry => entry.Batch).Select(entry => entry.Path), paths.TsErrors);
        File.WriteAllText(
            paths.TsSummary,
            string.Join("\n", summaries.OrderBy(entry => entry.Batch).Select(entry => entry.Text))
                + $"\n# {batches} node batch(es) of at most {settings.NodeBatch} files, {settings.NodeParallel} at a time; each batch reports its own peak RSS\n",
            Utf8NoBom);
        foreach (var chunk in hashFiles.Concat(errorFiles).Select(entry => entry.Path).Concat(Directory.GetFiles(manifestDir)))
        {
            File.Delete(chunk);
        }

        Directory.Delete(manifestDir, recursive: true);
        return true;
    }

    private static async Task<(int Batch, bool Ok, string Output, string HashFile, string ErrorFile, string Manifest)> RunBatchAsync(
        ProcessStartInfo psi,
        int batch,
        string hashFile,
        string errorFile,
        string manifest)
    {
        try
        {
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            return process.ExitCode == 0
                ? (batch, true, stdout.TrimEnd('\r', '\n'), hashFile, errorFile, manifest)
                : (batch, false, $"baselinediff: {psi.FileName} exited with {process.ExitCode}\n{stderr}{stdout}", hashFile, errorFile, manifest);
        }
        catch (Exception ex)
        {
            return (batch, false, $"baselinediff: cannot run '{psi.FileName}' ({ex.GetType().Name}: {ex.Message}). Pass --node <exe> if node is not on PATH, and --js <lib/typescript.js> for the reference compiler.", hashFile, errorFile, manifest);
        }
    }

    private static List<(int Batch, bool Ok, string Output, string HashFile, string ErrorFile, string Manifest)> Drain(
        List<Task<(int Batch, bool Ok, string Output, string HashFile, string ErrorFile, string Manifest)>> pending,
        int count)
    {
        var take = count == int.MaxValue ? pending.Count : Math.Min(count, pending.Count);
        var finished = new List<(int, bool, string, string, string, string)>(take);
        for (var i = 0; i < take; i++)
        {
            finished.Add(pending[i].GetAwaiter().GetResult());
        }

        pending.RemoveRange(0, take);
        return finished;
    }

    /// <summary>Concatenates the per-batch outputs into the single file the comparison reads, in batch order.</summary>
    private static void Concatenate(IEnumerable<string> parts, string target)
    {
        using var writer = new StreamWriter(File.Open(target, FileMode.Create), Utf8NoBom) { NewLine = "\n" };
        foreach (var part in parts)
        {
            if (!File.Exists(part)) continue;
            foreach (var line in File.ReadLines(part))
            {
                writer.Write(line);
                writer.Write('\n');
            }
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // Output.
    // ------------------------------------------------------------------------------------------------------------

    private static string BuildReport(
        Settings settings,
        int fileCount,
        Comparison comparison,
        List<string> portErrors,
        List<string> tsErrors,
        TimeSpan portElapsed)
    {
        var sb = new StringBuilder();
        sb.Append("# BaselineDiff — corpus ").Append(settings.Cases).Append('\n');
        sb.Append("# baselines ").Append(settings.Baselines).Append(" vs ").Append(settings.Js).Append('\n');
        sb.Append("# workspace ").Append(settings.Workspace).Append(" lib ").Append(settings.Lib).Append(" test-lib ").Append(settings.TestLib).Append('\n');
        sb.Append("# artifacts ").Append(string.Join(",", settings.Artifacts))
          .Append("; one case per configuration the harness derives from the file's own `// @` directives\n");
        if (settings.Filter is not null) sb.Append("# filter ").Append(settings.Filter).Append('\n');
        if (settings.Limit is not null) sb.Append("# limit ").Append(settings.Limit.Value).Append('\n');

        foreach (var (row, classification, line, column, producedLine, baselineLine) in comparison.PortDiverged.OrderBy(entry => entry.Row.Stem, StringComparer.Ordinal).ThenBy(entry => entry.Row.Kind, StringComparer.Ordinal))
        {
            sb.Append("DIVERGE ").Append(row.Stem).Append(' ').Append(row.Kind)
              .Append(" line=").Append(line)
              .Append(" col=").Append(column)
              .Append(" class=").Append(classification)
              .Append(" | port: ").Append(Excerpt(producedLine, column))
              .Append(" | baseline: ").Append(Excerpt(baselineLine, column))
              .Append('\n');
        }

        foreach (var (row, classification, line, column, referenceLine, baselineLine) in comparison.ReferenceDiverged.OrderBy(entry => entry.Row.Stem, StringComparer.Ordinal).ThenBy(entry => entry.Row.Kind, StringComparer.Ordinal))
        {
            sb.Append("REF-DIVERGE ").Append(row.Stem).Append(' ').Append(row.Kind)
              .Append(" line=").Append(line)
              .Append(" col=").Append(column)
              .Append(" class=").Append(classification)
              .Append(" | reference: ").Append(Excerpt(referenceLine, column))
              .Append(" | baseline: ").Append(Excerpt(baselineLine, column))
              .Append('\n');
        }

        foreach (var line in portErrors.OrderBy(line => line, StringComparer.Ordinal)) sb.Append("PORT ").Append(line).Append('\n');
        foreach (var line in tsErrors.OrderBy(line => line, StringComparer.Ordinal)) sb.Append("TS ").Append(line).Append('\n');

        if (settings.Filter is not null || settings.Limit is not null)
        {
            sb.Append("# extra-baseline is not checked on a shard: this run covers ").Append(fileCount)
              .Append(" of the corpus's files, so most baselines belong to cases it never derived\n");
        }

        sb.Append("# per-artifact verdicts (a case's failure counts once per artifact kind it was asked for)\n");
        foreach (var kind in AllKinds)
        {
            if (!settings.Artifacts.Contains(kind)) continue;
            var counts = comparison.ByKind.GetValueOrDefault(kind) ?? new int[Columns.Length];
            sb.Append("ARTIFACT ").Append(kind);
            for (var i = 0; i < Columns.Length; i++) sb.Append(' ').Append(Columns[i]).Append('=').Append(counts[i]);
            sb.Append('\n');
        }

        sb.Append("BASELINE-SIDE ");
        foreach (var kind in AllKinds)
        {
            if (!settings.Artifacts.Contains(kind)) continue;
            var total = CountBaselines(settings, kind);
            sb.Append(kind).Append('=').Append(total).Append(' ');
        }

        sb.Append('\n');
        var tsSide = File.Exists(Path.Combine(settings.Out, "ts-summary.txt"))
            ? File.ReadAllText(Path.Combine(settings.Out, "ts-summary.txt")).Trim()
            : string.Empty;
        if (tsSide.Length > 0) sb.Append("TS-SIDE ").Append(tsSide.Replace("\n", "\nTS-SIDE ")).Append('\n');
        sb.Append("# port peak working set ").Append(Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024)).Append(" MiB\n");
        sb.Append("# port pass ").Append(portElapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture)).Append("s\n");
        sb.Append("SUMMARY files=").Append(fileCount)
          .Append(" cases=").Append(comparison.CaseCount)
          .Append(" same=").Append(comparison.PortSame)
          .Append(" diverged=").Append(comparison.PortDiverged.Count)
          .Append(" port-crash=").Append(comparison.PortCrashes)
          .Append(" unmodelled=").Append(comparison.PortUnmodelled)
          .Append(" unpaired=").Append(comparison.PortUnpaired)
          .Append(" extra-baseline=").Append(comparison.ExtraBaselines)
          .Append(" ref-diverged=").Append(comparison.ReferenceDiverged.Count)
          .Append(" ref-crash=").Append(comparison.ReferenceCrashes)
          .Append('\n');
        return sb.ToString();
    }

    private static int CountBaselines(Settings settings, string kind)
    {
        if (!Directory.Exists(settings.Baselines)) return 0;
        return EnumerateBaselineNames(settings).Count(name => SplitBaselineName(name).Kind == kind);
    }

    /// <summary>One of the two differing lines as the report prints it: verbatim when it is short, and otherwise a
    /// window around the first differing column with <c>...</c> marking what was cut, so the character the two sides
    /// part company on is never the part that got cut. The whole texts are under <c>&lt;out&gt;/port</c>,
    /// <c>&lt;out&gt;/ts</c> and the baseline directory.</summary>
    private static string Excerpt(string? line, int column)
    {
        if (line is null) return "<end of text>";
        const int width = 200;
        if (line.Length <= width) return line;
        var start = Math.Max(0, column - 1 - (width / 3));
        var end = Math.Min(line.Length, start + width);
        return (start > 0 ? "..." : "") + line[start..end] + (end < line.Length ? "..." : "");
    }

    private static string BuildJsonLines(Comparison comparison)
    {
        var sb = new StringBuilder();
        foreach (var (row, classification, line, column, producedLine, baselineLine) in comparison.PortDiverged.OrderBy(entry => entry.Row.Stem, StringComparer.Ordinal))
        {
            sb.Append(JsonSerializer.Serialize(new
            {
                side = "port",
                stem = row.Stem,
                artifact = row.Kind,
                baseline = row.BaselineName,
                line,
                column,
                @class = classification,
                produced = producedLine,
                expected = baselineLine,
            })).Append('\n');
        }

        foreach (var (row, classification, line, column, referenceLine, baselineLine) in comparison.ReferenceDiverged.OrderBy(entry => entry.Row.Stem, StringComparer.Ordinal))
        {
            sb.Append(JsonSerializer.Serialize(new
            {
                side = "reference",
                stem = row.Stem,
                artifact = row.Kind,
                baseline = row.BaselineName,
                line,
                column,
                @class = classification,
                produced = referenceLine,
                expected = baselineLine,
            })).Append('\n');
        }

        return sb.ToString();
    }

    // ------------------------------------------------------------------------------------------------------------
    // Command line, manifest, files.
    // ------------------------------------------------------------------------------------------------------------

    private static bool TryParseArguments(string[] args, out Settings? settings, out string error)
    {
        settings = null;
        error = string.Empty;
        string? cases = null, baselines = null, filter = null, outDir = null, js = null, node = null, nodeScript = null, lib = null, testLib = null, workspace = null;
        string? artifacts = null;
        int? limit = null;
        var nodeBatch = 250;
        var nodeParallel = 1;
        // TS's underline annotations are what the port writes by default; `--types-underlines false` matches the
        // baselines whose generator does not fill them yet (see Settings.TypeUnderlines).
        var typeUnderlines = true;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--cases": cases = Next(args, ref i); break;
                case "--baselines": baselines = Next(args, ref i); break;
                case "--filter": filter = Next(args, ref i); break;
                case "--out": outDir = Next(args, ref i); break;
                case "--js": js = Next(args, ref i); break;
                case "--lib": lib = Next(args, ref i); break;
                case "--test-lib": testLib = Next(args, ref i); break;
                case "--workspace": workspace = Next(args, ref i); break;
                case "--node": node = Next(args, ref i); break;
                case "--node-script": nodeScript = Next(args, ref i); break;
                case "--artifacts": artifacts = Next(args, ref i); break;
                case "--node-batch":
                    var batchRaw = Next(args, ref i);
                    if (batchRaw is null || !int.TryParse(batchRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var batch) || batch <= 0)
                    {
                        error = "baselinediff: --node-batch needs a positive integer\n" + Usage;
                        return false;
                    }

                    nodeBatch = batch;
                    break;
                case "--node-parallel":
                    var parallelRaw = Next(args, ref i);
                    if (parallelRaw is null || !int.TryParse(parallelRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parallel) || parallel <= 0)
                    {
                        error = "baselinediff: --node-parallel needs a positive integer\n" + Usage;
                        return false;
                    }

                    nodeParallel = parallel;
                    break;
                case "--types-underlines":
                    var underlinesRaw = Next(args, ref i);
                    if (underlinesRaw is not ("true" or "false"))
                    {
                        error = "baselinediff: --types-underlines needs 'true' or 'false'\n" + Usage;
                        return false;
                    }

                    typeUnderlines = underlinesRaw == "true";
                    break;
                case "--limit":
                    var raw = Next(args, ref i);
                    if (raw is null || !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
                    {
                        error = "baselinediff: --limit needs a positive integer\n" + Usage;
                        return false;
                    }

                    limit = parsed;
                    break;
                case "-h" or "--help": error = Usage; return false;
                default:
                    error = $"baselinediff: unknown argument '{args[i]}'\n" + Usage;
                    return false;
            }
        }

        if (cases is null)
        {
            error = "baselinediff: --cases is required\n" + Usage;
            return false;
        }

        if (!Directory.Exists(cases))
        {
            error = $"baselinediff: --cases '{cases}' is not a directory";
            return false;
        }

        cases = Path.GetFullPath(cases);
        if (baselines is null)
        {
            error = "baselinediff: --baselines is required\n" + Usage;
            return false;
        }

        baselines = Path.GetFullPath(baselines);
        if (!Directory.Exists(baselines))
        {
            error = $"baselinediff: --baselines '{baselines}' is not a directory";
            return false;
        }

        outDir = Path.GetFullPath(outDir ?? Path.Combine(FindRepoRoot(), "artifacts", "baselinediff"));
        nodeScript ??= Path.Combine(AppContext.BaseDirectory, "ts-baseline.cjs");
        if (!File.Exists(nodeScript))
        {
            error = $"baselinediff: the reference half '{nodeScript}' does not exist (pass --node-script)";
            return false;
        }

        js = js is null ? FindReferenceLib(cases) : Path.GetFullPath(js);
        if (js is null || !File.Exists(js))
        {
            error = $"baselinediff: cannot find lib/typescript.js (looked up from '{cases}'); pass --js <file>";
            return false;
        }

        workspace = workspace is null ? FindWorkspaceRoot(cases, baselines) : Path.GetFullPath(workspace);
        var kinds = (artifacts ?? string.Join(",", AllKinds)).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var kind in kinds)
        {
            if (AllKinds.Contains(kind, StringComparer.Ordinal)) continue;
            error = $"baselinediff: unknown artifact kind '{kind}' (expected one of {string.Join(",", AllKinds)})\n" + Usage;
            return false;
        }

        settings = new Settings
        {
            Cases = cases,
            Baselines = baselines,
            Filter = filter,
            Limit = limit,
            NodeBatch = nodeBatch,
            NodeParallel = nodeParallel,
            Out = outDir,
            Js = js,
            Lib = lib is null ? Path.Combine(workspace, "lib") : Path.GetFullPath(lib),
            TestLib = testLib is null ? Path.Combine(workspace, "tests", "lib") : Path.GetFullPath(testLib),
            Workspace = workspace,
            Node = node ?? "node",
            NodeScript = nodeScript,
            Artifacts = kinds,
            TypeUnderlines = typeUnderlines,
        };
        return true;
    }

    private static string? Next(string[] args, ref int i)
    {
        if (i + 1 >= args.Length) return null;
        i++;
        return args[i];
    }

    private static string? FindReferenceLib(string casesDir)
    {
        for (DirectoryInfo? dir = new DirectoryInfo(casesDir); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "lib", "typescript.js");
            if (File.Exists(candidate)) return candidate;
        }

        const string fallback = "D:/Source/GitHub/TypeScript/lib/typescript.js";
        return File.Exists(fallback) ? fallback : null;
    }

    /// <summary>The checkout whose paths the baseline headers are relative to: the nearest ancestor of the corpus (and
    /// of the baseline directory) that holds a <c>tests/baselines</c> directory.</summary>
    private static string FindWorkspaceRoot(string casesDir, string baselinesDir)
    {
        foreach (var start in new[] { casesDir, baselinesDir })
        {
            for (DirectoryInfo? dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "tests", "baselines"))) return dir.FullName;
            }
        }

        return new DirectoryInfo(baselinesDir).Parent?.Parent?.FullName ?? baselinesDir;
    }

    private static string FindRepoRoot()
    {
        for (DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "tsc-cs.slnx"))) return dir.FullName;
        }

        return Directory.GetCurrentDirectory();
    }

    private static bool WriteManifest(string path, Settings settings, List<CorpusFile> files, out string error)
    {
        error = string.Empty;
        var entries = files.Select(file => new ManifestEntry(file.Rel, file.Kind)).ToList();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new Manifest(settings.Cases, settings.Workspace, entries)), Utf8NoBom);
            return true;
        }
        catch (Exception ex)
        {
            error = $"baselinediff: cannot write {path}: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private static List<CorpusFile> EnumerateFiles(string root, string? filter, int? limit)
    {
        Regex? matcher = null;
        if (filter is not null)
        {
            // A filter without a wildcard is a directory prefix, which is what a user typing `expressions` means.
            var pattern = filter.IndexOfAny(['*', '?']) < 0 ? $"^{Regex.Escape(filter)}(/.*)?$" : GlobToRegex(filter);
            matcher = new Regex(pattern, RegexOptions.CultureInvariant);
        }

        var files = new List<CorpusFile>();
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var path in Directory.EnumerateFiles(root, "*", options))
        {
            var rel = Path.GetRelativePath(root, path).Replace('\\', '/');
            // The conformance runner enumerates `/\.tsx?$/` (src/testRunner/compilerRunner.ts:54-57); the corpus holds
            // no `.js`/`.json` cases, so those are not this ring's corpus.
            if (!rel.EndsWith(".ts", StringComparison.Ordinal) && !rel.EndsWith(".tsx", StringComparison.Ordinal)) continue;
            if (matcher is not null && !matcher.IsMatch(rel)) continue;
            files.Add(new CorpusFile(rel, rel.EndsWith(".tsx", StringComparison.Ordinal) ? "TSX" : "TS"));
        }

        files.Sort(static (a, b) => string.CompareOrdinal(a.Rel, b.Rel));
        if (limit is int n && files.Count > n) files.RemoveRange(n, files.Count - n);
        return files;
    }

    private static string GlobToRegex(string glob)
    {
        var sb = new StringBuilder("^");
        for (var i = 0; i < glob.Length; i++)
        {
            switch (glob[i])
            {
                case '*':
                    if (i + 1 < glob.Length && glob[i + 1] == '*')
                    {
                        i++;
                        sb.Append(".*");
                    }
                    else
                    {
                        sb.Append("[^/]*");
                    }

                    break;
                case '?': sb.Append("[^/]"); break;
                default: sb.Append(Regex.Escape(glob[i].ToString())); break;
            }
        }

        return sb.Append('$').ToString();
    }

    /// <summary>Every baseline file name under <c>--baselines</c> whose suffix is one of the four kinds.</summary>
    private static IEnumerable<string> EnumerateBaselineNames(Settings settings)
    {
        if (!Directory.Exists(settings.Baselines)) yield break;
        foreach (var path in Directory.EnumerateFiles(settings.Baselines, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(path);
            if (SplitBaselineName(name).Kind is not null) yield return name;
        }
    }

    /// <summary>A baseline file name to its (stem, artifact kind), or a null kind when it is not one of the four. The
    /// extensions are the harness's own <c>baselinePath.replace</c> rules
    /// (src/harness/harnessIO.ts:659-663, 864-866, 1113-1121; src/testRunner/compilerRunner.ts:255-340).</summary>
    private static (string Stem, string? Kind) SplitBaselineName(string name)
    {
        if (name.EndsWith(".errors.txt", StringComparison.Ordinal)) return (name[..^".errors.txt".Length] + ".ts", "errors");
        if (name.EndsWith(".types", StringComparison.Ordinal)) return (name[..^".types".Length] + ".ts", "types");
        if (name.EndsWith(".symbols", StringComparison.Ordinal)) return (name[..^".symbols".Length] + ".ts", "symbols");
        if (name.EndsWith(".js", StringComparison.Ordinal)) return (name[..^".js".Length] + ".ts", "js");
        return (name, null);
    }

    /// <summary>The baseline file name an artifact is checked in as: the node half's <c>artifactName</c>, which is the
    /// reference's own three <c>replace</c> rules (the `.js` one replaces the first `.ts`/`.tsx`, the other two anchor
    /// at the end).</summary>
    private static string ArtifactName(string stem, string kind) => kind switch
    {
        "js" => Regex.Replace(stem, @"\.tsx?", ".js"),
        "errors" => Regex.Replace(stem, @"\.tsx?$", ".errors.txt"),
        "types" => StripTsExtension(stem) + ".types",
        "symbols" => StripTsExtension(stem) + ".symbols",
        _ => throw new ArgumentException($"unknown artifact kind '{kind}'", nameof(kind)),
    };

    private static string StripTsExtension(string stem)
        => stem.EndsWith(".tsx", StringComparison.Ordinal) || stem.EndsWith(".ts", StringComparison.Ordinal)
            ? Regex.Replace(stem, @"\.tsx?", "")
            : stem;

    private static string HashKey(string stem, string kind) => stem + "\t" + kind;

    private static (string Stem, string Kind) SplitKey(string key)
    {
        var tab = key.IndexOf('\t');
        return (key[..tab], key[(tab + 1)..]);
    }

    /// <summary>The per-(stem, kind) lines both halves write, as <c>key -&gt; "state\thash\tlength"</c>.</summary>
    private static Dictionary<string, string> ReadIndex(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path)) return result;
        foreach (var line in File.ReadLines(path))
        {
            var fields = line.Split('\t');
            if (fields.Length < 5) continue;
            result[HashKey(fields[0], fields[1])] = string.Join('\t', fields[2], fields[3], fields[4]);
        }

        return result;
    }

    private static List<string> ReadLines(string path) => File.Exists(path) ? [.. File.ReadLines(path).Where(line => line.Length > 0)] : [];

    private static string Hash16(string text)
    {
        var hash = FnvOffset;
        foreach (var ch in text)
        {
            hash ^= ch;
            hash *= FnvPrime;
        }

        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The file's text as the parser is handed it: UTF-8, with a leading byte-order mark removed. The harness reads its
    /// files — and the checked-in baselines it compares against — through <c>sys.readFile</c>, which strips the mark
    /// before either reaches the comparison, so a BOM-carrying source file does not shift every position by one and a
    /// baseline that carries none is not read as one that does.
    /// </summary>
    private static string ReadText(string path)
    {
        var text = Encoding.UTF8.GetString(File.ReadAllBytes(path));
        return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
    }

    /// <summary>
    /// The innermost frames that belong to the port itself, appended to every case-level failure record: unfiltered, a
    /// stack trace spends its first frames inside the runtime, which says nothing about the bug, and a case that throws
    /// without a frame to look at can only be re-derived by hand (tools/EmitDiff prints the same shape).
    /// </summary>
    private static string FrameNames(Exception ex)
    {
        var assembly = typeof(PortProgram).Assembly;
        var frames = new StackTrace(ex, fNeedFileInfo: true).GetFrames() ?? [];
        var names = frames
            .Where(frame => frame.GetMethod()?.DeclaringType?.Assembly == assembly)
            .Take(6)
            .Select(frame =>
            {
                var method = frame.GetMethod();
                var file = frame.GetFileName();
                var where = file is null ? "" : $" ({Path.GetFileName(file)}:{frame.GetFileLineNumber()})";
                return $"{method?.DeclaringType?.Name}.{method?.Name}{where}";
            })
            .ToArray();
        return names.Length == 0 ? "<no port frames>" : string.Join(" < ", names);
    }

    private static string FirstLine(string? message)
    {
        if (string.IsNullOrEmpty(message)) return string.Empty;
        var index = message.IndexOfAny(['\r', '\n']);
        var line = index < 0 ? message : message[..index];
        return line.Length > 300 ? line[..300] : line;
    }

    private sealed record CorpusFile(string Rel, string Kind);

    private sealed record Manifest(string root, string workspaceRoot, List<ManifestEntry> files);

    private sealed record ManifestEntry(string path, string kind);

    private sealed class Settings
    {
        public required string Cases { get; init; }

        public required string Baselines { get; init; }

        public string? Filter { get; init; }

        public int? Limit { get; init; }

        /// <summary>How many files one <c>node</c> process handles before it is restarted (default 250).</summary>
        /// <remarks>
        /// Each file costs a <c>ts.createProgram</c> plus an emit plus the declaration re-compilation, and node does
        /// not reclaim those quickly: with one process for the whole corpus the heap grows into the gigabytes.
        /// Restarting per batch bounds the peak at "the largest batch" (measured, and reported in the run summary) at
        /// the cost of one node startup per batch. The batches are independent — each writes its own files — so they
        /// may run at once (<see cref="NodeParallel"/>) and are concatenated in batch order either way.
        /// </remarks>
        public int NodeBatch { get; init; } = 250;

        public int NodeParallel { get; init; } = 1;

        public required string Out { get; init; }

        public required string Js { get; init; }

        public required string Lib { get; init; }

        public required string TestLib { get; init; }

        public required string Workspace { get; init; }

        public required string Node { get; init; }

        public required string NodeScript { get; init; }

        public required string[] Artifacts { get; init; }

        /// <summary>Whether the <c>.types</c> renderer writes TS's underline annotations. Their absence in tsgo's
        /// <c>testdata/baselines</c> is an unimplemented feature there, not a format change: its harness reads
        /// <c>result.underline</c> and prints the line when it is non-empty, while the producer never fills it
        /// (<c>internal/testutil/tsbaseline/type_symbol_baseline.go</c> lines 403/413 — "!!! TODO: port underline
        /// printer"). Comparing against those baselines therefore needs the same suppression, which is what
        /// <c>--types-underlines false</c> is for; the port's own rendering keeps TS's behaviour by default.</summary>
        public bool TypeUnderlines { get; init; } = true;

        /// <summary>src/harness/harnessIO.ts:393 — <c>currentDirectory || harnessSettings.currentDirectory</c>: the
        /// harness's <c>currentDirectory</c> argument is the case's own setting, so the value comes from the settings
        /// and the default is <c>null</c> (the virtual source folder).</summary>
        public string? CurrentDirectoryOf(Dictionary<string, string> settings)
            => settings.TryGetValue("currentDirectory", out var currentDirectory) ? currentDirectory : null;
    }

    private sealed class OutputPaths(string outDir)
    {
        public string PortHashes { get; } = Path.Combine(outDir, "port-hashes.txt");

        public string PortErrors { get; } = Path.Combine(outDir, "port-errors.txt");

        public string TsHashes { get; } = Path.Combine(outDir, "ts-hashes.txt");

        public string TsErrors { get; } = Path.Combine(outDir, "ts-errors.txt");

        public string TsSummary { get; } = Path.Combine(outDir, "ts-summary.txt");

        public string Manifest { get; } = Path.Combine(outDir, "manifest.json");

        public string Report { get; } = Path.Combine(outDir, "report.txt");

        public string Divergences { get; } = Path.Combine(outDir, "divergences.jsonl");

        public string PortDumps { get; } = Path.Combine(outDir, "port");

        public string TsDumps { get; } = Path.Combine(outDir, "ts");
    }
}
