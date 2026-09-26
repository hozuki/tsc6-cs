using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TypeScript.Compiler.Tools;

/// <summary>
/// AstDiff — the parse half of docs/architecture.md §7's P1 loop ("差分测试"): parse every file of the reference
/// compiler's own <c>tests/cases</c> corpus twice, once with the port and once with the real compiler, dump both
/// ASTs in one canonical line format, hash the text, and report the files whose hashes differ together with their
/// first differing line pair.
/// </summary>
/// <remarks>
/// <para>
/// The canonical dump (identical text on both sides for equal ASTs, LF only, no trailing spaces):
/// </para>
/// <code>
/// &lt;indent&gt;&lt;kind-number&gt;(&lt;pos&gt;,&lt;end&gt;)[ flags=&lt;flags-number&gt;][ `&lt;text&gt;`]
/// DIAG &lt;code&gt; &lt;start&gt; &lt;length&gt; &lt;messageText&gt;
/// </code>
/// <para>
/// <c>indent</c> is two spaces per depth; <c>flags=</c> appears only when the node's flags are non-zero; the
/// backtick-quoted text appears only for <c>Identifier</c>/<c>PrivateIdentifier</c> (<c>EscapedText</c>) and
/// <c>StringLiteral</c>/<c>NumericLiteral</c>/<c>NoSubstitutionTemplateLiteral</c> (<c>Text</c>). Children are
/// visited with the same <c>forEachChild</c> order on both sides, arrays element by element. Diagnostics follow the
/// node lines: the port's <c>SourceFile.ParseDiagnostics</c>, TS's <c>parseDiagnostics</c> (TS has no separate
/// <c>jsonDiagnostics</c> array — that field is a JSON config-file notion that this release's parser does not set).
/// </para>
/// <para>
/// Files are read as raw bytes and decoded as UTF-8 on both sides (BOM kept, invalid bytes replaced), because the
/// text handed to the parser must be identical for the comparison to mean anything.
/// </para>
/// <para>
/// The tool never runs <c>node</c> once per file: it hands the whole file list to
/// <c>ts-dump.cjs</c> in one process, which hashes every file with the real compiler and writes a full dump only
/// for the files whose hash differs from the port's. Full dumps land in <c>&lt;out&gt;/port</c> and
/// <c>&lt;out&gt;/ts</c>; nothing carries a timestamp, and file order is the sorted relative path, so two runs over
/// the same corpus produce byte-identical reports.
/// </para>
/// </remarks>
internal static class Program
{
    private const string Usage = """
        AstDiff — parse-differential harness (port vs lib/typescript.js).

        Usage: dotnet run --project tools/AstDiff -- --cases <dir> [options]

          --cases <dir>      corpus root (e.g. <TypeScript>/tests/cases)               [required]
          --filter <glob>    matches the '/'-separated path relative to --cases;
                             '*' does not cross '/', '**' does (e.g. conformance/parser/**)
          --limit <n>        keep the first n files after filtering (sorted by path)
          --out <dir>        output directory (default: <repo>/artifacts/astdiff)
          --js <file>        the reference lib/typescript.js (default: nearest ancestor of
                             --cases that has one, else the sibling TS checkout)
          --node <exe>       node executable (default: node)
          --node-script <f>  the batch TS dumper (default: <tool bin>/ts-dump.cjs)
          --resume           keep the hashes already in --out and skip those files, so a run
                             killed by an uncatchable failure (StackOverflowException) can
                             be continued with the same command

        Exit code: 0 = every hash matched, 1 = divergences or crashes were found (see
        <out>/report.txt), 2 = bad arguments, 3 = node could not run.
        """;

    /// <summary>FNV-1a's 64-bit offset basis and prime; the hash is taken over UTF-16 code units, not bytes, so
    /// both sides hash exactly the same sequence without an encoding step in between.</summary>
    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly Regex NodeLinePattern = new(
        @"^(?<indent> *)(?<kind>\d+)\((?<pos>-?\d+),(?<end>-?\d+)\)(?: flags=(?<flags>-?\d+))?(?: `(?<text>.*)`)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static int Main(string[] args)
    {
        // 数字（kind/pos/end/flags/code/start/length）必须与 node 侧输出逐字符相同，所以整个进程用不变文化。
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
            Console.Error.WriteLine($"astdiff: no files matched under {settings.Cases}" + (settings.Filter is null ? "" : $" (filter '{settings.Filter}')"));
            return 2;
        }

        Directory.CreateDirectory(settings.Out);
        var paths = new OutputPaths(settings.Out);
        if (!settings.Resume)
        {
            File.Delete(paths.PortHashes);
            File.Delete(paths.PortCrashes);
            File.Delete(paths.Progress);
            File.Delete(paths.TsHashes);
            File.Delete(paths.TsErrors);
            File.Delete(paths.Report);
            File.Delete(paths.Divergences);
        }

        var portHashes = ReadIndex(paths.PortHashes);
        var crashes = ReadIndex(paths.PortCrashes);
        if (settings.Resume && File.Exists(paths.Progress))
        {
            // The previous run died on this file before recording anything (only an uncatchable failure gets here),
            // so record it as such and never touch it again.
            var aborted = File.ReadAllText(paths.Progress).Trim();
            if (aborted.Length > 0 && !portHashes.ContainsKey(aborted) && !crashes.ContainsKey(aborted))
            {
                crashes[aborted] = "port-fatal: the process died mid-file (e.g. an uncatchable StackOverflowException)";
                File.AppendAllText(paths.PortCrashes, $"{aborted}\t{crashes[aborted]}\n", Utf8NoBom);
            }
        }

        var pending = new List<CorpusFile>(files.Count);
        foreach (var file in files)
        {
            if (settings.Resume && (portHashes.ContainsKey(file.Rel) || crashes.ContainsKey(file.Rel))) continue;
            pending.Add(file);
        }

        Console.Error.WriteLine($"astdiff: {files.Count} files under {settings.Cases}; parsing {pending.Count} with the port");
        var started = Stopwatch.StartNew();
        if (pending.Count > 0)
        {
            using var hashWriter = new StreamWriter(File.Open(paths.PortHashes, FileMode.Append), Utf8NoBom) { AutoFlush = true, NewLine = "\n" };
            using var crashWriter = new StreamWriter(File.Open(paths.PortCrashes, FileMode.Append), Utf8NoBom) { AutoFlush = true, NewLine = "\n" };
            using var progress = File.Open(paths.Progress, FileMode.Create, FileAccess.Write, FileShare.Read);
            // Recursive descent on adversarial input can be deeper than a default stack; 256 MiB of reserve (virtual,
            // on x64) keeps a legitimate deep tree from dying, and keeps the marker file honest if it dies anyway.
            var worker = new Thread(() => PortPass(settings.Cases, pending, hashWriter, crashWriter, progress), 256 * 1024 * 1024);
            worker.Start();
            worker.Join();
        }

        File.Delete(paths.Progress);
        portHashes = ReadIndex(paths.PortHashes);
        crashes = ReadIndex(paths.PortCrashes);
        var portElapsed = started.Elapsed;

        if (!WriteManifest(paths.Manifest, settings.Cases, files, out error))
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        if (!RunNode(settings, paths, out error))
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
            if (!portHashes.TryGetValue(file.Rel, out var portHash)) { missing++; continue; }
            if (!tsHashes.TryGetValue(file.Rel, out var tsHash)) { missing++; continue; }
            if (portHash == tsHash) { same++; continue; }
            divergences.Add(CompareFile(settings, paths, file.Rel, portHash, tsHash));
        }

        var tsSide = File.Exists(paths.TsSummary) ? SplitSummary(File.ReadAllText(paths.TsSummary)) : [];
        var report = BuildReport(settings, files.Count, same, divergences, crashes, tsErrors, missing, tsSide);
        File.WriteAllText(paths.Report, report, Utf8NoBom);
        File.WriteAllText(paths.Divergences, BuildJsonLines(divergences), Utf8NoBom);
        Console.Out.Write(report);
        Console.Error.WriteLine($"astdiff: port pass {portElapsed.TotalSeconds:F2}s, total {started.Elapsed.TotalSeconds:F2}s");
        return divergences.Count == 0 && crashes.Count == 0 && tsErrors.Count == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------------------------------------------------
    // The port pass.
    // ------------------------------------------------------------------------------------------------------------

    private static void PortPass(string root, List<CorpusFile> files, TextWriter hashes, TextWriter crashes, FileStream progress)
    {
        foreach (var file in files)
        {
            progress.SetLength(0);
            progress.Position = 0;
            progress.Write(Utf8NoBom.GetBytes(file.Rel));
            progress.Flush();

            string text;
            try
            {
                text = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root, file.Rel.Replace('/', Path.DirectorySeparatorChar))));
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
                var dump = Dump(tree);
                hashes.WriteLine($"{file.Rel}\t{Hash(dump):x16}\t{dump.Length}");
            }
            catch (Exception ex)
            {
                // Only catchable failures land here; an uncatchable one (StackOverflowException) kills the process and
                // leaves the progress marker behind for --resume.
                crashes.WriteLine($"{file.Rel}\tport-crash: {ex.GetType().Name}: {FirstLine(ex.Message)} @ {FrameNames(ex)}");
            }
        }
    }

    /// <summary>The canonical dump of one file: node lines in <c>forEachChild</c> order, then the diagnostics.</summary>
    private static string Dump(SourceFile file)
    {
        var sb = new StringBuilder(4096);
        AppendNode(sb, file, 0);
        foreach (var diagnostic in file.ParseDiagnostics ?? [])
        {
            sb.Append("DIAG ").Append(diagnostic.Code).Append(' ').Append(diagnostic.Start).Append(' ').Append(diagnostic.Length)
              .Append(' ').Append(diagnostic.MessageText).Append('\n');
        }

        return sb.Length > 0 ? sb.ToString(0, sb.Length - 1) : string.Empty;
    }

    private static void AppendNode(StringBuilder sb, Node node, int depth)
    {
        sb.Append(' ', depth * 2);
        sb.Append((int)node.Kind).Append('(').Append(node.Pos).Append(',').Append(node.End).Append(')');
        if (node.Flags != NodeFlags.None) sb.Append(" flags=").Append((uint)node.Flags);
        switch (node)
        {
            case Identifier identifier: sb.Append(" `").Append(identifier.EscapedText).Append('`'); break;
            case PrivateIdentifier privateIdentifier: sb.Append(" `").Append(privateIdentifier.EscapedText).Append('`'); break;
            case NoSubstitutionTemplateLiteral template: sb.Append(" `").Append(template.Text).Append('`'); break;
            case NumericLiteral numeric: sb.Append(" `").Append(numeric.Text).Append('`'); break;
            case StringLiteral literal: sb.Append(" `").Append(literal.Text).Append('`'); break;
            default: break;
        }

        sb.Append('\n');
        ForEachChildTable.ForEachChild<object?>(
            node,
            child => { AppendNode(sb, child, depth + 1); return null; },
            nodes => { foreach (var child in nodes) AppendNode(sb, (Node)child, depth + 1); return null; });
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

    private static bool RunNode(Settings settings, OutputPaths paths, out string error)
    {
        error = string.Empty;
        var psi = new ProcessStartInfo(settings.Node)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        psi.ArgumentList.Add(settings.NodeScript);
        psi.ArgumentList.Add("--manifest");
        psi.ArgumentList.Add(paths.Manifest);
        psi.ArgumentList.Add("--out");
        psi.ArgumentList.Add(settings.Out);
        psi.ArgumentList.Add("--js");
        psi.ArgumentList.Add(settings.Js);
        if (settings.Resume) psi.ArgumentList.Add("--resume");

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
            error = $"astdiff: cannot run '{settings.Node}' ({ex.GetType().Name}: {ex.Message}). "
                  + $"Pass --node <exe> if node is not on PATH, and --js <lib/typescript.js> for the reference compiler.";
            return false;
        }

        File.WriteAllText(paths.TsSummary, stdout.TrimEnd('\r', '\n') + "\n", Utf8NoBom);
        if (exitCode != 0)
        {
            error = $"astdiff: {settings.Node} {Path.GetFileName(settings.NodeScript)} exited with {exitCode}\n{stderr}{stdout}";
            return false;
        }

        if (stderr.Trim().Length > 0) Console.Error.WriteLine(stderr.TrimEnd('\r', '\n'));
        return true;
    }

    /// <summary>Re-dumps the port's tree for a diverging file, compares it line by line with the node dump, and
    /// writes both full dumps under <c>--out</c>.</summary>
    private static Divergence CompareFile(Settings settings, OutputPaths paths, string rel, string portHash, string tsHash)
    {
        var tsText = ReadText(Path.Combine(paths.TsDumps, rel + ".txt"));
        string portText;
        try
        {
            var text = ReadText(Path.Combine(settings.Cases, rel.Replace('/', Path.DirectorySeparatorChar)));
            portText = Dump(Parser.CreateSourceFile(rel, text, ScriptTarget.ESNext, setParentNodes: false, ScriptKindOf(KindOf(rel))));
        }
        catch (Exception ex)
        {
            return new Divergence(rel, 0, $"port-reparse-crash: {ex.GetType().Name}: {FirstLine(ex.Message)}", null, null, portHash, tsHash);
        }

        WriteDump(paths.PortDumps, rel, portText);
        var portLines = portText.Split('\n');
        var tsLines = tsText.Split('\n');
        var index = 0;
        while (index < portLines.Length && index < tsLines.Length && portLines[index] == tsLines[index]) index++;
        var portLine = index < portLines.Length ? portLines[index] : null;
        var tsLine = index < tsLines.Length ? tsLines[index] : null;

        string classification;
        if (portLine is null && tsLine is null)
        {
            classification = "hash differs but the dumps are identical (hash collision — the harness cannot report this file)";
        }
        else if (portLine is null || tsLine is null)
        {
            classification = "missing-or-extra node: one dump ends where the other continues";
        }
        else if (!IsDiagnostic(portLine) && !IsDiagnostic(tsLine))
        {
            classification = ShiftDirection(portLines, tsLines, index) switch
            {
                1 => "missing-or-extra node: the TS dump emits an entry the port dump omits",
                -1 => "missing-or-extra node: the port dump emits an entry the TS dump omits",
                _ => ClassifyNodeLines(portLines, tsLines, index, portLine, tsLine),
            };
        }
        else
        {
            classification = ClassifyDiagnosticShift(portLines, tsLines, portLine, tsLine);
        }

        return new Divergence(rel, index + 1, classification, portLine, tsLine, portHash, tsHash);
    }

    /// <summary>Tells a one-sided entry from a genuine per-node difference: when only one side emits an entry,
    /// skipping that one line makes the two dumps agree again for the rest of their common length. Returns 1 when the
    /// TS dump has the extra entry, -1 when the port dump does, 0 when neither skip explains the difference.</summary>
    private static int ShiftDirection(string[] portLines, string[] tsLines, int index)
    {
        var tsOnly = MatchAfterSkipping(portLines, tsLines, index, tsSkip: 1);
        var portOnly = MatchAfterSkipping(tsLines, portLines, index, tsSkip: 1);
        if (tsOnly == 0 && portOnly == 0) return 0;
        return tsOnly >= portOnly ? 1 : -1;

        static int MatchAfterSkipping(string[] left, string[] right, int index, int tsSkip)
        {
            var run = 0;
            while (index + run < left.Length && index + run + tsSkip < right.Length && left[index + run] == right[index + run + tsSkip]) run++;
            return run;
        }
    }

    /// <summary>Classifies the first differing node line pair; falls back to the enclosing entry when the differing
    /// physical line is a continuation of a literal whose text contains a real newline.</summary>
    private static string ClassifyNodeLines(string[] portLines, string[] tsLines, int index, string portLine, string tsLine)
    {
        var port = NodeLinePattern.Match(portLine);
        var ts = NodeLinePattern.Match(tsLine);
        var within = false;
        if (!port.Success || !ts.Success)
        {
            within = true;
            port = NodeLinePattern.Match(EnclosingEntry(portLines, index));
            ts = NodeLinePattern.Match(EnclosingEntry(tsLines, index));
        }

        if (!port.Success || !ts.Success) return "unparsable dump line";
        var suffix = within ? " (inside a multi-line node)" : string.Empty;
        int kind = int.Parse(port.Groups["kind"].Value, CultureInfo.InvariantCulture);
        int other = int.Parse(ts.Groups["kind"].Value, CultureInfo.InvariantCulture);
        if (kind != other) return "kind mismatch" + suffix;
        if (port.Groups["pos"].Value != ts.Groups["pos"].Value || port.Groups["end"].Value != ts.Groups["end"].Value)
            return "range mismatch" + suffix;
        var portFlags = port.Groups["flags"].Success ? port.Groups["flags"].Value : "0";
        var tsFlags = ts.Groups["flags"].Success ? ts.Groups["flags"].Value : "0";
        if (portFlags != tsFlags) return "flags mismatch" + suffix;
        if (port.Groups["text"].Value != ts.Groups["text"].Value) return "text mismatch" + suffix;
        return "identical entry (the diff is inside a multi-line node)" + suffix;
    }

    private static string ClassifyDiagnosticShift(string[] portLines, string[] tsLines, string? portLine, string? tsLine)
    {
        var nodesEqual = NonDiagnostics(portLines).SequenceEqual(NonDiagnostics(tsLines));
        if (portLine is null) return "diagnostic mismatch: the port dump ends before the TS dump's diagnostics";
        if (tsLine is null) return "diagnostic mismatch: the port dump has diagnostics the TS dump does not";
        if (portLine.StartsWith("DIAG ", StringComparison.Ordinal) && tsLine.StartsWith("DIAG ", StringComparison.Ordinal))
        {
            var port = portLine.Split(' ', 5);
            var ts = tsLine.Split(' ', 5);
            var where = port[1] != ts[1] ? "code"
                : port[2] != ts[2] ? "start"
                : port[3] != ts[3] ? "length"
                : "message";
            return $"diagnostic mismatch ({where})" + (nodesEqual ? "" : " and node lines differ");
        }

        return nodesEqual
            ? "diagnostic mismatch: the DIAG lines do not line up with the node lines"
            : "missing-or-extra node: the node lines part company at a DIAG line";
    }

    private static IEnumerable<string> NonDiagnostics(string[] lines) => lines.Where(line => !IsDiagnostic(line));

    private static bool IsDiagnostic(string line) => line.StartsWith("DIAG ", StringComparison.Ordinal);

    /// <summary>Walks back to the entry a continuation line belongs to (a line that starts a node or a DIAG).</summary>
    private static string EnclosingEntry(string[] lines, int index)
    {
        for (var i = index; i >= 0; i--)
        {
            if (IsDiagnostic(lines[i]) || NodeLinePattern.IsMatch(lines[i])) return lines[i];
        }

        return lines[index];
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
        int missing,
        string[] tsSide)
    {
        var sb = new StringBuilder();
        sb.Append("# AstDiff parse-differential — corpus ").Append(settings.Cases).Append(" vs ").Append(settings.Js).Append('\n');
        if (settings.Filter is not null) sb.Append("# filter ").Append(settings.Filter).Append('\n');
        if (settings.Limit is not null) sb.Append("# limit ").Append(settings.Limit.Value).Append('\n');
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
        if (tsSide.Length > 0) sb.Append("TS-SIDE ").Append(string.Join(' ', tsSide)).Append('\n');
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
                portHash = divergence.PortHash,
                tsHash = divergence.TsHash,
            })).Append('\n');
        }

        return sb.ToString();
    }

    private static string[] SplitSummary(string text)
    {
        var line = text.Trim();
        return line.Length == 0 ? [] : line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private static void WriteDump(string directory, string rel, string text)
    {
        var path = Path.Combine(directory, rel.Replace('/', Path.DirectorySeparatorChar) + ".txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, Utf8NoBom);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Files, kinds, hashes.
    // ------------------------------------------------------------------------------------------------------------

    private static List<CorpusFile> EnumerateFiles(string root, string? filter, int? limit)
    {
        Regex? matcher = null;
        if (filter is not null)
        {
            // A filter without a wildcard is a directory prefix, which is what a user typing `conformance/parser` means.
            var pattern = filter.IndexOfAny(['*', '?']) < 0 ? $"^{Regex.Escape(filter)}(/.*)?$" : GlobToRegex(filter);
            matcher = new Regex(pattern, RegexOptions.CultureInvariant);
        }

        var files = new List<CorpusFile>();
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var path in Directory.EnumerateFiles(root, "*", options))
        {
            var rel = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (matcher is not null && !matcher.IsMatch(rel)) continue;
            files.Add(new CorpusFile(rel, KindOf(rel)));
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

    /// <summary>The script kind both sides derive from the file extension (the port's <c>EnsureScriptKind</c> uses the
    /// same table; it is spelled out here because node needs the name, not the port's enum).</summary>
    private static string KindOf(string rel)
    {
        var ext = Path.GetExtension(rel).ToLowerInvariant();
        return ext switch
        {
            ".tsx" => "TSX",
            ".jsx" => "JSX",
            ".js" or ".mjs" or ".cjs" => "JS",
            ".json" => "JSON",
            _ => "TS",
        };
    }

    private static ScriptKind ScriptKindOf(string kind) => kind switch
    {
        "JS" => ScriptKind.JS,
        "JSX" => ScriptKind.JSX,
        "TS" => ScriptKind.TS,
        "TSX" => ScriptKind.TSX,
        "JSON" => ScriptKind.JSON,
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

    private static string ReadText(string path) => Encoding.UTF8.GetString(File.ReadAllBytes(path));

    private static string FirstLine(string? message)
    {
        if (string.IsNullOrEmpty(message)) return string.Empty;
        var index = message.IndexOfAny(['\r', '\n']);
        var line = index < 0 ? message : message[..index];
        return line.Length > 200 ? line[..200] : line;
    }

    /// <summary>The innermost frames that belong to the port itself. Unfiltered, a stack trace spends its first
    /// frames inside the runtime (System.Number.GrabInts, the cast helpers, …), which says nothing about the bug.</summary>
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
        var resume = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--cases": cases = Next(args, ref i); break;
                case "--filter": filter = Next(args, ref i); break;
                case "--limit":
                    var raw = Next(args, ref i);
                    if (raw is null || !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
                    {
                        error = "astdiff: --limit needs a positive integer\n" + Usage;
                        return false;
                    }

                    limit = parsed;
                    break;
                case "--out": outDir = Next(args, ref i); break;
                case "--js": js = Next(args, ref i); break;
                case "--node": node = Next(args, ref i); break;
                case "--node-script": nodeScript = Next(args, ref i); break;
                case "--resume": resume = true; break;
                case "-h" or "--help": error = Usage; return false;
                default:
                    error = $"astdiff: unknown argument '{args[i]}'\n" + Usage;
                    return false;
            }
        }

        if (cases is null)
        {
            error = "astdiff: --cases is required\n" + Usage;
            return false;
        }

        if (!Directory.Exists(cases))
        {
            error = $"astdiff: --cases '{cases}' is not a directory";
            return false;
        }

        cases = Path.GetFullPath(cases);
        outDir = Path.GetFullPath(outDir ?? Path.Combine(FindRepoRoot(), "artifacts", "astdiff"));
        nodeScript ??= Path.Combine(AppContext.BaseDirectory, "ts-dump.cjs");
        if (!File.Exists(nodeScript))
        {
            error = $"astdiff: the node dumper '{nodeScript}' does not exist (pass --node-script)";
            return false;
        }

        js = js is null ? FindReferenceLib(cases) : Path.GetFullPath(js);
        if (js is null || !File.Exists(js))
        {
            error = $"astdiff: cannot find lib/typescript.js (looked up from '{cases}'); pass --js <file>";
            return false;
        }

        settings = new Settings
        {
            Cases = cases,
            Filter = filter,
            Limit = limit,
            Out = outDir,
            Js = js,
            Node = node ?? "node",
            NodeScript = nodeScript,
            Resume = resume,
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
        var entries = new List<ManifestEntry>(files.Count);
        entries.AddRange(files.Select(file => new ManifestEntry(file.Rel, file.Kind)));
        try
        {
            var json = JsonSerializer.Serialize(new Manifest(root, entries));
            File.WriteAllText(path, json, Utf8NoBom);
            return true;
        }
        catch (Exception ex)
        {
            error = $"astdiff: cannot write {path}: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private sealed record CorpusFile(string Rel, string Kind);

    private sealed record Manifest(string root, List<ManifestEntry> files);

    private sealed record ManifestEntry(string path, string kind);

    private sealed record Divergence(string File, int Line, string Classification, string? PortLine, string? TsLine, string PortHash, string TsHash);

    private sealed class Settings
    {
        public required string Cases { get; init; }
        public string? Filter { get; init; }
        public int? Limit { get; init; }
        public required string Out { get; init; }
        public required string Js { get; init; }
        public required string Node { get; init; }
        public required string NodeScript { get; init; }
        public bool Resume { get; init; }
    }

    private sealed class OutputPaths(string outDir)
    {
        public string Out { get; } = outDir;
        public string PortHashes { get; } = Path.Combine(outDir, "port-hashes.txt");
        public string PortCrashes { get; } = Path.Combine(outDir, "port-crashes.txt");
        public string Progress { get; } = Path.Combine(outDir, "port-inprogress.txt");
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
