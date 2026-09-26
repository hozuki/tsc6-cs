using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TypeScript.Compiler.Tools;

/// <summary>
/// BindDiff — the binding half of docs/architecture.md §7's outer loop, and the M5 flow-graph differential: bind every
/// file of a corpus twice, once with the port and once with the real compiler, dump the *control-flow graph* each side
/// built in one canonical line format, hash the text, and report the files whose hashes differ together with their
/// first differing line pair.
/// </summary>
/// <remarks>
/// <para>
/// The canonical dump (identical text on both sides for equal flow graphs, LF only, no trailing spaces):
/// </para>
/// <code>
/// N&lt;depth&gt; &lt;kind&gt;(&lt;pos&gt;,&lt;end&gt;) nflags=&lt;node-flags&gt; flow=&lt;flow&gt;
/// X&lt;depth&gt; endFlowNode=&lt;flow&gt; | possiblyExhaustive=&lt;0|1|-&gt;
///             | flowNodeWhenTrue=&lt;flow&gt; flowNodeWhenFalse=&lt;flow&gt;
///             | fallthroughFlowNode=&lt;flow&gt;
/// F#&lt;id&gt; flags=&lt;flow-flags&gt; node=&lt;payload&gt; ante=&lt;flow-or-[flow,…]|-&gt;
/// </code>
/// <para>
/// A node line carries the port's/<c>ts</c>'s own <c>flowNode</c> (the flow a statement is entered with) plus the whole
/// <c>NodeFlags</c> word, because the binder writes reachability into it (<c>NodeFlags.Unreachable</c>); an <c>X</c>
/// line carries the slots the binder writes that are not a <c>flowNode</c> — the file's <c>endFlowNode</c>, a switch's
/// <c>possiblyExhaustive</c>, a conditional expression's two branches, a case clause's <c>fallthroughFlowNode</c>. A
/// flow node is printed once per dump as an <c>F#</c> line: its <c>FlowFlags</c>, the payload it carries (a node's
/// <c>kind(pos,end)</c>, a switch clause's bounds, or a reduce label's target and antecedent count) and its
/// antecedents (a single flow node or an array, each referred to as <c>#id</c> after its first appearance). The ids come
/// from a deterministic depth-first walk of the antecedents, so the same graph prints the same text on both sides even
/// though it is cyclic (a loop's label is its own antecedent).
/// </para>
/// <para>
/// Files are read as raw bytes and decoded as UTF-8 on both sides; the reference side strips a leading BOM, because
/// TS's <c>sys.readFile</c> does before the parser sees the text, and a BOM left in place would shift every position
/// in the file by one and report a divergence that is not in the port.
/// </para>
/// <para>
/// The tool never runs <c>node</c> once per file: it hands the whole file list to <c>ts-flow-dump.cjs</c> in one
/// process, which binds every file with the real compiler and writes a full dump only for the files whose hash differs
/// from the port's. Full dumps land in <c>&lt;out&gt;/port</c> and <c>&lt;out&gt;/ts</c>; nothing carries a timestamp and
/// file order is the sorted relative path, so two runs over the same corpus produce byte-identical reports.
/// </para>
/// </remarks>
internal static class Program
{
    private const string Usage = """
        BindDiff — flow-graph differential harness (port vs lib/typescript.js).

        Usage: dotnet run --project tools/BindDiff -- --cases <dir> [options]

          --cases <dir>      corpus root (e.g. <TypeScript>/tests/cases/conformance)     [required]
          --filter <glob>    matches the '/'-separated path relative to --cases;
                             '*' does not cross '/', '**' does (e.g. controlFlow/**)
          --limit <n>        keep the first n files after filtering (sorted by path)
          --out <dir>        output directory (default: <repo>/artifacts/binddiff)
          --js <file>        the reference lib/typescript.js (default: nearest ancestor of
                             --cases that has one, else the sibling TS checkout)
          --node <exe>       node executable (default: node)
          --node-script <f>  the batch TS dumper (default: <tool bin>/ts-flow-dump.cjs)
          --no-fallthrough   bind with noFallthroughCasesInSwitch on both sides

        Files are the `.ts`/`.tsx`/`.js`/`.jsx`/`.mjs`/`.cjs` ones under --cases; JSON files are
        skipped because the port's JSON conversion is a named M8 residual, not a binder concern.

        Exit code: 0 = every hash matched, 1 = divergences or crashes were found (see
        <out>/report.txt), 2 = bad arguments, 3 = node could not run.
        """;

    /// <summary>FNV-1a's 64-bit offset basis and prime; hashed over UTF-16 code units, so both sides hash exactly the
    /// same sequence without an encoding step in between (the same choice tools/AstDiff records).</summary>
    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static int Main(string[] args)
    {
        // 数字（kind/pos/end/flags）必须与 node 侧输出逐字符相同，所以整个进程用不变文化。
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
            Console.Error.WriteLine($"binddiff: no files matched under {settings.Cases}" + (settings.Filter is null ? "" : $" (filter '{settings.Filter}')"));
            return 2;
        }

        Directory.CreateDirectory(settings.Out);
        var paths = new OutputPaths(settings.Out);
        foreach (var stale in new[] { paths.PortHashes, paths.PortCrashes, paths.TsHashes, paths.TsErrors, paths.TsSummary, paths.Report, paths.Divergences })
        {
            File.Delete(stale);
        }

        // The batched node pass writes one hash/error file per batch; a previous run may have left more of them than this
        // one produces, and the comparison must not read another run's leftovers.
        foreach (var stale in Directory.GetFiles(settings.Out, "ts-hashes.*.txt").Concat(Directory.GetFiles(settings.Out, "ts-errors.*.txt")))
        {
            File.Delete(stale);
        }

        Console.Error.WriteLine($"binddiff: {files.Count} files under {settings.Cases}; binding them with the port");
        var started = Stopwatch.StartNew();
        using (var hashWriter = new StreamWriter(File.Open(paths.PortHashes, FileMode.Create), Utf8NoBom) { AutoFlush = true, NewLine = "\n" })
        using (var crashWriter = new StreamWriter(File.Open(paths.PortCrashes, FileMode.Create), Utf8NoBom) { AutoFlush = true, NewLine = "\n" })
        {
            // A flow dump recurses over the AST and the binder recurses over the tree; 256 MiB of reserve (virtual on
            // x64) keeps a legitimately deep file from dying, the same reserve tools/AstDiff takes for the parser.
            var worker = new Thread(() => PortPass(settings, files, hashWriter, crashWriter), 256 * 1024 * 1024);
            worker.Start();
            worker.Join();
        }

        var portElapsed = started.Elapsed;
        var portHashes = ReadIndex(paths.PortHashes);
        var crashes = ReadIndex(paths.PortCrashes);
        if (!WriteManifest(paths.Manifest, settings.Cases, files, out error))
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        if (!RunNode(settings, paths, files, out error))
        {
            Console.Error.WriteLine(error);
            return 3;
        }

        var tsHashes = ReadIndex(paths.TsHashes);
        var tsErrors = ReadIndex(paths.TsErrors);
        var divergences = new List<Divergence>();
        var same = 0;
        var missing = 0;
        foreach (var file in files)
        {
            if (crashes.ContainsKey(file.Rel) || tsErrors.ContainsKey(file.Rel)) continue;
            if (!portHashes.TryGetValue(file.Rel, out var portHash) || !tsHashes.TryGetValue(file.Rel, out var tsHash))
            {
                missing++;
                continue;
            }

            if (portHash == tsHash)
            {
                same++;
                continue;
            }

            divergences.Add(CompareFile(settings, paths, file));
        }

        var report = BuildReport(settings, files.Count, same, divergences, crashes, tsErrors, missing);
        File.WriteAllText(paths.Report, report, Utf8NoBom);
        File.WriteAllText(paths.Divergences, BuildJsonLines(divergences), Utf8NoBom);
        Console.Out.Write(report);
        Console.Error.WriteLine($"binddiff: port pass {portElapsed.TotalSeconds:F2}s, total {started.Elapsed.TotalSeconds:F2}s");
        return divergences.Count == 0 && crashes.Count == 0 && tsErrors.Count == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------------------------------------------------
    // The port pass.
    // ------------------------------------------------------------------------------------------------------------

    private static void PortPass(Settings settings, List<CorpusFile> files, TextWriter hashes, TextWriter crashes)
    {
        var options = new BindDiffCompilerOptions
        {
            Target = ScriptTarget.ESNext,
            NoFallthroughCasesInSwitch = settings.NoFallthrough ? true : null,
        };
        foreach (var file in files)
        {
            string text;
            try
            {
                text = ReadText(Path.Combine(settings.Cases, file.Rel.Replace('/', Path.DirectorySeparatorChar)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                crashes.WriteLine($"{file.Rel}\tread-error: {ex.GetType().Name}: {FirstLine(ex.Message)}");
                continue;
            }

            try
            {
                var tree = Parser.CreateSourceFile(
                    file.Rel,
                    text,
                    ScriptTarget.ESNext,
                    setParentNodes: false,
                    ScriptKindOf(file.Kind));
                Binder.BindSourceFile(tree, options);
                var dump = Dump(tree);
                hashes.WriteLine($"{file.Rel}\t{Hash(dump):x16}\t{dump.Length}");
            }
            catch (Exception ex)
            {
                crashes.WriteLine($"{file.Rel}\tport-crash: {ex.GetType().Name}: {FirstLine(ex.Message)} @ {FrameNames(ex)}");
            }
        }
    }

    /// <summary>The canonical dump of one file: node lines and their extra flow slots, in <c>forEachChild</c> order.</summary>
    private static string Dump(SourceFile file)
    {
        var outLines = new List<string>();
        var flows = new FlowDumper(outLines);
        AppendNode(file, 0, outLines, flows);
        return string.Join("\n", outLines) + "\n";
    }

    private static void AppendNode(Node node, int depth, List<string> outLines, FlowDumper flows)
    {
        var flow = node is IFlowContainer container ? flows.Key(container.FlowNode) : "-";
        outLines.Add($"N{depth} {(int)node.Kind}({node.Pos},{node.End}) nflags={(int)node.Flags} flow={flow}");
        if (node is SourceFile sourceFile)
        {
            outLines.Add($"X{depth} endFlowNode={flows.Key(sourceFile.EndFlowNode)}");
        }

        if (node is SwitchStatement switchStatement)
        {
            outLines.Add($"X{depth} possiblyExhaustive={(switchStatement.PossiblyExhaustive is null ? "-" : switchStatement.PossiblyExhaustive.Value ? "1" : "0")}");
        }

        if (node is ConditionalExpression conditionalExpression)
        {
            outLines.Add($"X{depth} flowNodeWhenTrue={flows.Key(conditionalExpression.FlowNodeWhenTrue)}"
                + $" flowNodeWhenFalse={flows.Key(conditionalExpression.FlowNodeWhenFalse)}");
        }

        switch (node)
        {
            case CaseClause { FallthroughFlowNode: { } caseFallthrough }:
                outLines.Add($"X{depth} fallthroughFlowNode={flows.Key(caseFallthrough)}");
                break;
            case DefaultClause { FallthroughFlowNode: { } defaultFallthrough }:
                outLines.Add($"X{depth} fallthroughFlowNode={flows.Key(defaultFallthrough)}");
                break;
        }

        ForEachChildTable.ForEachChild(node, child => AppendNode(child, depth + 1, outLines, flows));
    }

    /// <summary>
    /// Prints each flow node once per file and refers to it by <c>#id</c> afterwards.
    /// </summary>
    /// <remarks>
    /// The two obvious alternatives both explode. Numbering inside a single <c>key</c> call re-prints one graph once per
    /// reference to it (the same file has thousands of references), and embedding the antecedents' descriptions into a
    /// node's line nests a whole subgraph into every ancestor's string. Either way the dump grows far faster than the
    /// flow graph does — the reason this tool once reached tens of gigabytes on the full corpus. So: one shared id map per
    /// file, one <c>F#</c> line per flow node, and every field that names another flow node is a <c>#id</c> reference (or
    /// <c>-</c>). The reference half (<c>ts-flow-dump.cjs</c>) emits exactly the same lines.
    /// </remarks>
    private sealed class FlowDumper(List<string> outLines)
    {
        private readonly Dictionary<FlowNode, int> _ids = [];
        private int _next = 1;

        /// <summary>The <c>#id</c> of <paramref name="flow"/>, printing it (and the nodes it reaches) on first use.</summary>
        public string Key(FlowNode? flow)
        {
            if (flow is null)
            {
                return "-";
            }

            if (_ids.TryGetValue(flow, out var existing))
            {
                return "#" + existing;
            }

            var id = _next++;
            _ids[flow] = id;

            var antecedent = flow.Antecedent switch
            {
                List<FlowNode> many => "[" + string.Join(",", many.Select(Key)) + "]",
                FlowNode single => Key(single),
                _ => "-",
            };
            outLines.Add($"F#{id} flags={(int)flow.Flags} ante={antecedent}");

            switch (flow.Node)
            {
                case Node payload:
                    outLines.Add($"F#{id} node={(int)payload.Kind}({payload.Pos},{payload.End})");
                    break;
                case FlowSwitchClauseData clauseData:
                    outLines.Add($"F#{id} clause={clauseData.SwitchStatement!.Pos},{clauseData.SwitchStatement.End},{clauseData.ClauseStart},{clauseData.ClauseEnd}");
                    break;
                case FlowReduceLabelData reduceData:
                    var targets = reduceData.Antecedents is { } reduceAntecedents
                        ? "[" + string.Join(",", reduceAntecedents.Select(Key)) + "]"
                        : "-";
                    outLines.Add($"F#{id} reduce target={Key(reduceData.Target as FlowNode)} outer={targets}");
                    break;
            }

            return "#" + id;
        }
    }

    private static ulong Hash(string text)
    {
        var hash = FnvOffset;
        foreach (var ch in text)
        {
            hash ^= ch;
            hash *= FnvPrime;
        }

        return hash;
    }

    // ------------------------------------------------------------------------------------------------------------
    // The node pass and the comparison.
    // ------------------------------------------------------------------------------------------------------------

    private static bool RunNode(Settings settings, OutputPaths paths, List<CorpusFile> files, out string error)
    {
        error = string.Empty;
        var batches = (files.Count + settings.NodeBatch - 1) / settings.NodeBatch;
        var hashFiles = new List<string>(batches);
        var errorFiles = new List<string>(batches);
        var summaries = new List<string>(batches);
        var manifestDir = Path.Combine(settings.Out, "manifests");
        Directory.CreateDirectory(manifestDir);

        for (var batch = 0; batch < batches; batch++)
        {
            var slice = files.GetRange(batch * settings.NodeBatch, Math.Min(settings.NodeBatch, files.Count - (batch * settings.NodeBatch)));
            var chunkManifest = Path.Combine(manifestDir, $"chunk{batch:D4}.json");
            if (!WriteManifest(chunkManifest, settings.Cases, slice, out error))
            {
                return false;
            }

            var chunkHashes = Path.Combine(settings.Out, $"ts-hashes.{batch:D4}.txt");
            var chunkErrors = Path.Combine(settings.Out, $"ts-errors.{batch:D4}.txt");
            Console.Error.WriteLine($"binddiff: node batch {batch + 1}/{batches} ({slice.Count} files)");

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
            psi.ArgumentList.Add(settings.Out);
            psi.ArgumentList.Add("--js");
            psi.ArgumentList.Add(settings.Js);
            // Several batches share one output directory, so each one is told where to read the port's hashes and where
            // to put its own outputs; the dumps of every batch land in the same directory.
            psi.ArgumentList.Add("--port-hashes");
            psi.ArgumentList.Add(paths.PortHashes);
            psi.ArgumentList.Add("--hashes");
            psi.ArgumentList.Add(chunkHashes);
            psi.ArgumentList.Add("--errors");
            psi.ArgumentList.Add(chunkErrors);
            psi.ArgumentList.Add("--dumps");
            psi.ArgumentList.Add(paths.TsDumps);
            if (settings.NoFallthrough) psi.ArgumentList.Add("--no-fallthrough");

            string stdout, stderr;
            int exitCode;
            try
            {
                using var process = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null");
                stdout = process.StandardOutput.ReadToEnd();
                stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                exitCode = process.ExitCode;
            }
            catch (Exception ex)
            {
                error = $"binddiff: cannot run '{settings.Node}' ({ex.GetType().Name}: {ex.Message}). "
                      + $"Pass --node <exe> if node is not on PATH, and --js <lib/typescript.js> for the reference compiler.";
                return false;
            }

            if (exitCode != 0)
            {
                error = $"binddiff: {settings.Node} {Path.GetFileName(settings.NodeScript)} exited with {exitCode}\n{stderr}{stdout}";
                return false;
            }

            hashFiles.Add(chunkHashes);
            errorFiles.Add(chunkErrors);
            summaries.Add(stdout.TrimEnd('\r', '\n'));
        }

        Concatenate(hashFiles, paths.TsHashes);
        Concatenate(errorFiles, paths.TsErrors);
        File.WriteAllText(
            paths.TsSummary,
            string.Join("\n", summaries)
                + $"\n# {batches} node batch(es) of at most {settings.NodeBatch} files; each batch reports its own peak RSS\n",
            Utf8NoBom);
        foreach (var chunk in hashFiles.Concat(errorFiles).Concat(Directory.GetFiles(manifestDir)))
        {
            File.Delete(chunk);
        }

        return true;
    }

    /// <summary>Concatenates the per-batch outputs into the single file the comparison reads, in batch order.</summary>
    private static void Concatenate(List<string> parts, string target)
    {
        using var writer = new StreamWriter(File.Open(target, FileMode.Create), Utf8NoBom) { NewLine = "\n" };
        foreach (var part in parts)
        {
            foreach (var line in File.ReadLines(part))
            {
                writer.Write(line);
                writer.Write('\n');
            }
        }
    }

    /// <summary>Re-dumps the port's tree for a diverging file, compares it line by line with the node dump, and writes
    /// both full dumps under <c>--out</c>.</summary>
    private static Divergence CompareFile(Settings settings, OutputPaths paths, CorpusFile file)
    {
        var tsText = ReadText(Path.Combine(paths.TsDumps, file.Rel.Replace('/', Path.DirectorySeparatorChar) + ".txt"));
        string portText;
        try
        {
            var text = ReadText(Path.Combine(settings.Cases, file.Rel.Replace('/', Path.DirectorySeparatorChar)));
            var tree = Parser.CreateSourceFile(file.Rel, text, ScriptTarget.ESNext, setParentNodes: false, ScriptKindOf(file.Kind));
            Binder.BindSourceFile(tree, new BindDiffCompilerOptions
            {
                Target = ScriptTarget.ESNext,
                NoFallthroughCasesInSwitch = settings.NoFallthrough ? true : null,
            });
            portText = Dump(tree);
        }
        catch (Exception ex)
        {
            return new Divergence(file.Rel, 0, $"port-reparse-crash: {ex.GetType().Name}: {FirstLine(ex.Message)}", null, null);
        }

        var target = Path.Combine(paths.PortDumps, file.Rel.Replace('/', Path.DirectorySeparatorChar) + ".txt");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, portText, Utf8NoBom);

        var portLines = portText.Split('\n');
        var tsLines = tsText.Split('\n');
        var index = 0;
        while (index < portLines.Length && index < tsLines.Length && portLines[index] == tsLines[index]) index++;
        var portLine = index < portLines.Length ? portLines[index] : null;
        var tsLine = index < tsLines.Length ? tsLines[index] : null;
        return new Divergence(file.Rel, index + 1, Classify(portLine, tsLine), portLine, tsLine);
    }

    /// <summary>
    /// Names the field the two dumps part company on. The dump is one field per line by construction, so the
    /// classification is a property of the two differing lines rather than a guess: a node line whose
    /// <c>kind(pos,end)</c> prefix differs is a tree divergence (not this harness's subject), one whose
    /// <c>nflags</c> differs is a node-flags divergence, one whose <c>flow=</c> differs is a flow-node divergence, an
    /// <c>F#</c> pair is a divergence inside a flow node's flags/payload/antecedents, and an <c>X</c> pair is one of the
    /// extra slots (<c>endFlowNode</c>, <c>possiblyExhaustive</c>, <c>flowNodeWhenTrue|False</c>,
    /// <c>fallthroughFlowNode</c>).
    /// </summary>
    private static string Classify(string? portLine, string? tsLine)
    {
        if (portLine is null && tsLine is null) return "hash differs but the dumps are identical (hash collision)";
        if (portLine is null || tsLine is null) return "line-count mismatch: one dump ends where the other continues";
        if (portLine.StartsWith("F#", StringComparison.Ordinal) && tsLine.StartsWith("F#", StringComparison.Ordinal))
        {
            return FieldOf(portLine, tsLine, 2) is { } field ? $"flow {field} mismatch" : "flow line mismatch";
        }

        if (portLine.StartsWith('X') && tsLine.StartsWith('X')) return "extra flow slot mismatch";

        var prefix = new Regex(@"^N\d+ (\d+)\((-?\d+),(-?\d+)\)", RegexOptions.CultureInvariant);
        var portPrefix = prefix.Match(portLine);
        var tsPrefix = prefix.Match(tsLine);
        if (!portPrefix.Success || !tsPrefix.Success || portPrefix.Value != tsPrefix.Value) return "node mismatch (kind/pos/end)";
        if (FieldOf(portLine, tsLine, 2) is { } nodeField) return $"node {nodeField} mismatch";
        return "line mismatch";
    }

    /// <summary>The whitespace-separated field <paramref name="index"/> when the two lines agree on every earlier one
    /// (so the first differing field is named), else <c>null</c>.</summary>
    private static string? FieldOf(string portLine, string tsLine, int index)
    {
        var portFields = portLine.Split(' ');
        var tsFields = tsLine.Split(' ');
        return index < portFields.Length && index < tsFields.Length ? portFields[index].Split('=')[0] : null;
    }

    // ------------------------------------------------------------------------------------------------------------
    // Output.
    // ------------------------------------------------------------------------------------------------------------

    private static string BuildReport(
        Settings settings,
        int fileCount,
        int same,
        List<Divergence> divergences,
        Dictionary<string, string> crashes,
        Dictionary<string, string> tsErrors,
        int missing)
    {
        var sb = new StringBuilder();
        sb.Append("# BindDiff flow-graph differential — corpus ").Append(settings.Cases).Append(" vs ").Append(settings.Js).Append('\n');
        if (settings.Filter is not null) sb.Append("# filter ").Append(settings.Filter).Append('\n');
        if (settings.Limit is not null) sb.Append("# limit ").Append(settings.Limit.Value).Append('\n');
        if (settings.NoFallthrough) sb.Append("# noFallthroughCasesInSwitch on\n");
        foreach (var divergence in divergences.OrderBy(d => d.File, StringComparer.Ordinal))
        {
            sb.Append("DIVERGE ").Append(divergence.File)
              .Append(" line=").Append(divergence.Line)
              .Append(" class=").Append(divergence.Classification)
              .Append(" | port: ").Append(divergence.PortLine ?? "<end of dump>")
              .Append(" | ts: ").Append(divergence.TsLine ?? "<end of dump>")
              .Append('\n');
        }

        foreach (var crash in crashes.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            sb.Append("CRASH ").Append(crash.Key).Append(' ').Append(crash.Value).Append('\n');
        }

        foreach (var tsError in tsErrors.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            sb.Append("TS-ERROR ").Append(tsError.Key).Append(' ').Append(tsError.Value).Append('\n');
        }

        if (missing > 0) sb.Append("# ").Append(missing).Append(" file(s) have no hash on one side\n");
        var tsSide = File.Exists(Path.Combine(settings.Out, "ts-summary.txt"))
            ? File.ReadAllText(Path.Combine(settings.Out, "ts-summary.txt")).Trim()
            : string.Empty;
        if (tsSide.Length > 0) sb.Append("TS-SIDE ").Append(tsSide).Append('\n');
        // The tool reports its own peak: it is the number that catches a dump that grows faster than the graph it prints
        // (this one reached tens of gigabytes before the flow dumper was rewritten to print each node once).
        sb.Append("# port peak working set ").Append(Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024)).Append(" MiB\n");
        sb.Append("SUMMARY files=").Append(fileCount)
          .Append(" same=").Append(same)
          .Append(" diverged=").Append(divergences.Count)
          .Append(" port-crash=").Append(crashes.Count)
          .Append(" ts-error=").Append(tsErrors.Count)
          .Append(" unpaired=").Append(missing)
          .Append('\n');
        return sb.ToString();
    }

    private static string BuildJsonLines(List<Divergence> divergences)
    {
        var sb = new StringBuilder();
        foreach (var divergence in divergences.OrderBy(d => d.File, StringComparer.Ordinal))
        {
            sb.Append(JsonSerializer.Serialize(new
            {
                file = divergence.File,
                line = divergence.Line,
                @class = divergence.Classification,
                port = divergence.PortLine,
                ts = divergence.TsLine,
            })).Append('\n');
        }

        return sb.ToString();
    }

    // ------------------------------------------------------------------------------------------------------------
    // Files, kinds, hashes.
    // ------------------------------------------------------------------------------------------------------------

    private static List<CorpusFile> EnumerateFiles(string root, string? filter, int? limit)
    {
        Regex? matcher = null;
        if (filter is not null)
        {
            // A filter without a wildcard is a directory prefix, which is what a user typing `controlFlow` means.
            var pattern = filter.IndexOfAny(['*', '?']) < 0 ? $"^{Regex.Escape(filter)}(/.*)?$" : GlobToRegex(filter);
            matcher = new Regex(pattern, RegexOptions.CultureInvariant);
        }

        var files = new List<CorpusFile>();
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var path in Directory.EnumerateFiles(root, "*", options))
        {
            var rel = Path.GetRelativePath(root, path).Replace('\\', '/');
            var kind = KindOf(rel);
            if (kind is null) continue;
            if (matcher is not null && !matcher.IsMatch(rel)) continue;
            files.Add(new CorpusFile(rel, kind));
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

    /// <summary>The script kind both sides derive from the file extension, or <c>null</c> for a file the harness does not
    /// bind (JSON: the port's JSON conversion is a named M8 residual, and the reference's JSON binding walks a different
    /// path — <c>convertToJson</c> — that has nothing to do with the flow graph).</summary>
    private static string? KindOf(string rel) => Path.GetExtension(rel).ToLowerInvariant() switch
    {
        ".ts" => "TS",
        ".tsx" => "TSX",
        ".js" or ".mjs" or ".cjs" => "JS",
        ".jsx" => "JSX",
        _ => null,
    };

    private static ScriptKind ScriptKindOf(string kind) => kind switch
    {
        "JS" => ScriptKind.JS,
        "JSX" => ScriptKind.JSX,
        "TS" => ScriptKind.TS,
        "TSX" => ScriptKind.TSX,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "unknown script kind"),
    };

    private static Dictionary<string, string> ReadIndex(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path)) return result;
        foreach (var line in File.ReadAllLines(path))
        {
            var tab = line.IndexOf('\t');
            if (tab <= 0) continue;
            result[line[..tab]] = line[(tab + 1)..];
        }

        return result;
    }

    /// <summary>
    /// The file's text as the binder is handed it: UTF-8, with a leading byte-order mark removed. The reference reads
    /// its files through <c>sys.readFile</c>, which strips the mark before the parser ever sees the text, so a harness
    /// that kept it would shift every position in a BOM-carrying file by one and report a divergence that is not in the
    /// port (588 of the conformance corpus's files carry one). The node half strips it in <c>ts-flow-dump.cjs</c> for
    /// the same reason.
    /// </summary>
    private static string ReadText(string path)
    {
        var text = Encoding.UTF8.GetString(File.ReadAllBytes(path));
        return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
    }

    private static string FirstLine(string? message)
    {
        if (string.IsNullOrEmpty(message)) return string.Empty;
        var index = message.IndexOfAny(['\r', '\n']);
        var line = index < 0 ? message : message[..index];
        return line.Length > 200 ? line[..200] : line;
    }

    /// <summary>The innermost frames that belong to the port itself; unfiltered, a stack trace spends its first frames
    /// inside the runtime, which says nothing about the bug.</summary>
    private static string FrameNames(Exception ex)
    {
        var assembly = typeof(Parser).Assembly;
        var frames = new StackTrace(ex, fNeedFileInfo: true).GetFrames() ?? [];
        var names = frames
            .Where(frame => frame.GetMethod()?.DeclaringType?.Assembly == assembly)
            .Take(4)
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

    // ------------------------------------------------------------------------------------------------------------
    // Command line and manifest.
    // ------------------------------------------------------------------------------------------------------------

    private static bool TryParseArguments(string[] args, out Settings? settings, out string error)
    {
        settings = null;
        error = string.Empty;
        string? cases = null, filter = null, outDir = null, js = null, node = null, nodeScript = null;
        int? limit = null;
        var nodeBatch = 250;
        var noFallthrough = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--cases": cases = Next(args, ref i); break;
                case "--filter": filter = Next(args, ref i); break;
                case "--node-batch":
                    var batchRaw = Next(args, ref i);
                    if (batchRaw is null || !int.TryParse(batchRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var batch) || batch <= 0)
                    {
                        error = "binddiff: --node-batch needs a positive integer\n" + Usage;
                        return false;
                    }

                    nodeBatch = batch;
                    break;

                case "--limit":
                    var raw = Next(args, ref i);
                    if (raw is null || !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
                    {
                        error = "binddiff: --limit needs a positive integer\n" + Usage;
                        return false;
                    }

                    limit = parsed;
                    break;
                case "--out": outDir = Next(args, ref i); break;
                case "--js": js = Next(args, ref i); break;
                case "--node": node = Next(args, ref i); break;
                case "--node-script": nodeScript = Next(args, ref i); break;
                case "--no-fallthrough": noFallthrough = true; break;
                case "-h" or "--help": error = Usage; return false;
                default:
                    error = $"binddiff: unknown argument '{args[i]}'\n" + Usage;
                    return false;
            }
        }

        if (cases is null)
        {
            error = "binddiff: --cases is required\n" + Usage;
            return false;
        }

        if (!Directory.Exists(cases))
        {
            error = $"binddiff: --cases '{cases}' is not a directory";
            return false;
        }

        cases = Path.GetFullPath(cases);
        outDir = Path.GetFullPath(outDir ?? Path.Combine(FindRepoRoot(), "artifacts", "binddiff"));
        nodeScript ??= Path.Combine(AppContext.BaseDirectory, "ts-flow-dump.cjs");
        if (!File.Exists(nodeScript))
        {
            error = $"binddiff: the node dumper '{nodeScript}' does not exist (pass --node-script)";
            return false;
        }

        js = js is null ? FindReferenceLib(cases) : Path.GetFullPath(js);
        if (js is null || !File.Exists(js))
        {
            error = $"binddiff: cannot find lib/typescript.js (looked up from '{cases}'); pass --js <file>";
            return false;
        }

        settings = new Settings
        {
            Cases = cases,
            Filter = filter,
            Limit = limit,
            NodeBatch = nodeBatch,
            Out = outDir,
            Js = js,
            Node = node ?? "node",
            NodeScript = nodeScript,
            NoFallthrough = noFallthrough,
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

    private static string FindRepoRoot()
    {
        for (DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "tsc-cs.slnx"))) return dir.FullName;
        }

        return Directory.GetCurrentDirectory();
    }

    private static bool WriteManifest(string path, string root, List<CorpusFile> files, out string error)
    {
        error = string.Empty;
        var entries = files.Select(file => new ManifestEntry(file.Rel, file.Kind)).ToList();
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new Manifest(root, entries)), Utf8NoBom);
            return true;
        }
        catch (Exception ex)
        {
            error = $"binddiff: cannot write {path}: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private sealed record CorpusFile(string Rel, string Kind);

    private sealed record Manifest(string root, List<ManifestEntry> files);

    private sealed record ManifestEntry(string path, string kind);

    private sealed record Divergence(string File, int Line, string Classification, string? PortLine, string? TsLine);

    private sealed class Settings
    {
        public required string Cases { get; init; }
        public string? Filter { get; init; }
        public int? Limit { get; init; }

        /// <summary>
        /// How many files one <c>node</c> process handles before it is restarted (default 250).
        /// </summary>
        /// <remarks>
        /// Each file costs a <c>ts.createProgram</c> plus a type checker, and node does not reclaim those quickly: with
        /// one process for the whole corpus the heap grows into the gigabytes. Restarting per batch bounds the peak at
        /// "the largest batch" (measured, and reported in the run summary) at the cost of one node startup per batch.
        /// </remarks>
        public int NodeBatch { get; init; } = 250;
        public required string Out { get; init; }
        public required string Js { get; init; }
        public required string Node { get; init; }
        public required string NodeScript { get; init; }
        public bool NoFallthrough { get; init; }
    }

    private sealed class OutputPaths(string outDir)
    {
        public string PortHashes { get; } = Path.Combine(outDir, "port-hashes.txt");
        public string PortCrashes { get; } = Path.Combine(outDir, "port-crashes.txt");
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
