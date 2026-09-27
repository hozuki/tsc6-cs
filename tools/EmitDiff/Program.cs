using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// The tool's own entry-point class is named Program (tools/AstDiff, tools/BindDiff and tools/TypeDiff do the same),
// which is also the name of the port's class the driver creates a program with; the alias keeps the two apart.
using PortProgram = TypeScript.Compiler.Program;

namespace TypeScript.Compiler.Tools;

/// <summary>
/// EmitDiff — the emission half of docs/architecture.md §7's outer loop, and the M7 `.js` differential (§17.5): compile
/// every file of a corpus twice, once with the port's <c>Program.Emit</c> and once with the real compiler, capture every
/// output each side's <c>writeFile</c> callback was handed (the emitted JavaScript, and — when the source-map flags are
/// on — the <c>.js.map</c> or the inlined map, in the order the emitter wrote them), hash it byte for byte, and report
/// the files whose hashes differ together with their first differing line pair.
/// </summary>
/// <remarks>
/// <para>
/// The canonical dump (identical text on both sides for equal emitted output, LF only, no trailing spaces): one line for
/// the run, then a begin framer, the emitted text verbatim and an end framer per emitted file, in the order the emitter
/// wrote them:
/// </para>
/// <code>
/// // EmitDiff skipped=&lt;0|1&gt; files=&lt;n&gt;
/// // EmitDiff begin &lt;escaped output file name&gt;
/// &lt;the emitted text, byte for byte&gt;
/// // EmitDiff end &lt;escaped output file name&gt; len=&lt;utf16 length&gt;
/// </code>
/// <para>
/// The <c>// EmitDiff</c> prefix is a JavaScript comment, so a dump is itself parseable JavaScript and the report's
/// <c>line=</c> numbers the file written under <c>&lt;out&gt;/port</c> and <c>&lt;out&gt;/ts</c>; the prefix cannot
/// collide with emitted text, because a line of output would have to start with exactly that comment. The length rides
/// on the <em>end</em> framer rather than the begin one: a divergence inside a file's text almost always changes its
/// length too, and a length in the begin line would be the first difference of every such pair — the report would point
/// at a number instead of at the emitted JavaScript it exists to point at. Everything in the dump — the skipped flag,
/// the output count, each output's name, length and order — is emit behaviour too, which is why it is compared rather
/// than normalized away.
/// </para>
/// <para>
/// Both halves are handed the same option object (<c>target: ESNext</c>, <c>module: ESNext</c>, <c>allowJs: true</c>,
/// <c>newLine: LineFeed</c>, <c>suppressOutputPathCheck: true</c>, plus the source-map family when a
/// <c>--source-map</c>-family flag asked for it — see <see cref="PortOptions"/>) and the same one-file host: a program
/// of exactly that one file, no default library and no module resolution. Emit is captured through the callback and
/// nothing else, on both sides (program.ts:2598-2637's <c>writeFile: writeFileCallback || writeFile</c>), so neither
/// half implements an emit host of its own — and every output the emitter hands that callback is what gets compared,
/// which is why a <c>--source-map</c> run compares the <c>.js.map</c> and the <c>//# sourceMappingURL=</c> comment
/// beside the <c>.js</c> without the dump's format changing at all: its <c>begin</c>/<c>end</c> pair already repeats
/// once per output.
/// </para>
/// <para>
/// The port's emit is live (M7 landed <c>Program.Emit</c>, the transformer chain and the emitter), so this tool no
/// longer reports a boundary: a port-side <c>NotSupportedException</c> is still *data* rather than a harness failure
/// — each file it happens on is recorded in <c>port-crashes.txt</c> as <c>port-crash: NotSupportedException: …</c>, it
/// counts against the verdict, and the file never reaches the comparison, because a boundary that has not been
/// implemented can never read as agreement. Nothing is stubbed on either side.
/// </para>
/// <para>
/// The tool never runs <c>node</c> once per file: it hands a whole batch of files to <c>ts-emit.cjs</c> in one process
/// (default 250), which emits every file of the batch with the real compiler and writes a full dump only for the files
/// whose hash differs from the port's. Full dumps land in <c>&lt;out&gt;/port</c> and <c>&lt;out&gt;/ts</c>; nothing
/// carries a timestamp and file order is the sorted relative path, so two runs over the same corpus produce the same
/// hashes, the same divergence lines and the same <c>SUMMARY</c> — only the two self-reported peak numbers (the port's
/// working set and each node batch's <c>maxRSS</c>) can differ, because they are measurements rather than results.
/// </para>
/// </remarks>
internal static partial class Program
{
    private const string Usage = """
        EmitDiff — emission differential harness (port vs lib/typescript.js).

        Usage: dotnet run --project tools/EmitDiff -- --cases <dir> [options]

          --cases <dir>      corpus root (e.g. <TypeScript>/tests/cases/conformance)     [required]
          --filter <glob>    matches the '/'-separated path relative to --cases;
                             '*' does not cross '/', '**' does (e.g. expressions/**)
          --limit <n>        keep the first n files after filtering (sorted by path)
          --parallel <n>     port-half files compiled at once (default: ProcessorCount; 1 = serial)
          --out <dir>        output directory (default: <repo>/artifacts/emitdiff)
          --js <file>        the reference lib/typescript.js (default: nearest ancestor of
                             --cases that has one, else the sibling TS checkout)
          --node <exe>       node executable (default: node)
          --node-script <f>  the batch TS emitter (default: <tool bin>/ts-emit.cjs)
          --node-batch <n>   files per node process (default: 250; each batch reports its own peak RSS)
          --source-map       emit `<name>.js.map` plus the `//# sourceMappingURL=` comment (both halves)
          --inline-source-map  inline the map into the `.js` as a `data:` URL instead of a `.js.map` (both halves)
          --inline-sources   include each source's text in the map as `sourcesContent` (both halves)
          --source-root <d>  `sourceRoot` written into the map (both halves; no effect without a map option)
          --map-root <d>     `mapRoot`: where the map is expected to be published (both halves)

        Both halves are handed the same options: target=ESNext, module=ESNext, allowJs=true, newLine=LF,
        suppressOutputPathCheck=true, plus whatever the source-map flags above add (the node half's `options` object and
        this tool's PortOptions must be changed together). Files are the `.ts`/`.tsx`/`.js`/`.jsx`/`.mjs`/`.cjs` ones
        under --cases; JSON files are skipped because the port's JSON conversion is a named M8 residual, not an emitter
        concern.

        Exit code: 0 = every hash matched, 1 = divergences or crashes were found (see <out>/report.txt),
        2 = bad arguments, 3 = node could not run.
        """;

    /// <summary>FNV-1a's 64-bit offset basis and prime; hashed over UTF-16 code units, so both sides hash exactly the
    /// same sequence without an encoding step in between (the same choice tools/AstDiff, tools/BindDiff and
    /// tools/TypeDiff record).</summary>
    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

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
            Console.Error.WriteLine($"emitdiff: no files matched under {settings.Cases}" + (settings.Filter is null ? "" : $" (filter '{settings.Filter}')"));
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

        Console.Error.WriteLine($"emitdiff: {files.Count} files under {settings.Cases}; emitting them with the port (parallel={settings.Parallel})");
        // PORT-NOTE (nesting): with the outer fan-out on, the compiler's own fan-out is pinned to one worker — a bind
        // fan-out inside each of N workers would put N × cores tasks on the pool for no gain (§18.5). With
        // `--parallel 1` the caller is running one file at a time and the compiler's degree is left alone, which is the
        // mode that exercises the compiler-side phases (and whose artifacts still compare byte for byte).
        if (settings.Parallel > 1)
        {
            Parallelism.Degree = 1;
        }
        var started = Stopwatch.StartNew();
        using (var hashWriter = new StreamWriter(File.Open(paths.PortHashes, FileMode.Create), Utf8NoBom) { AutoFlush = true, NewLine = "\n" })
        using (var crashWriter = new StreamWriter(File.Open(paths.PortCrashes, FileMode.Create), Utf8NoBom) { AutoFlush = true, NewLine = "\n" })
        {
            // The transformer chain rewrites the whole tree and the printer recurses over it; 256 MiB of reserve
            // (virtual on x64) keeps a legitimately deep file from dying, the same reserve the three sibling tools take.
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
        // Contract (the file header): 0 = every hash matched, 1 = divergences or crashes were found. `same` alone does
        // not make a run clean: a file with a hash on only one side (unpaired), a port crash, a reference error and a
        // divergence each mean something was *not* compared, and a run that compared nothing at all must never read as
        // agreement — including the case where every file crashed without the crash index being readable back. A port
        // half that cannot emit yet therefore exits 1 with every file in port-crashes.txt, never 0.
        var clean = same == files.Count && missing == 0 && divergences.Count == 0 && crashes.Count == 0 && tsErrors.Count == 0;
        var verdict = clean ? 0 : 1;
        Console.Error.WriteLine(
            $"emitdiff: VERDICT {(clean ? "clean" : "fail")} same={same}/{files.Count} diverged={divergences.Count}"
            + $" port-crash={crashes.Count} ts-error={tsErrors.Count} unpaired={missing} exit={verdict}");
        Console.Error.WriteLine($"emitdiff: port pass {portElapsed.TotalSeconds:F2}s, total {started.Elapsed.TotalSeconds:F2}s");
        return verdict;
    }

    // ------------------------------------------------------------------------------------------------------------
    // The port pass.
    // ------------------------------------------------------------------------------------------------------------

    private static void PortPass(Settings settings, List<CorpusFile> files, TextWriter hashes, TextWriter crashes)
    {
        // PORT-NOTE (parallelism): every file is an independent program (its own system, options, program and emit
        // run), so the pass fans out over the corpus and each worker writes only its own slots. The two slots are then
        // flushed in file order, which is what keeps the index files byte-identical to a serial run's — the verdict, the
        // report and every `artifacts/` file are compared between runs, so the order cannot depend on scheduling.
        var hashLines = new string?[files.Count];
        var crashLines = new string?[files.Count];
        RunFiles(settings.Parallel, files.Count, i =>
        {
            var file = files[i];
            string text;
            try
            {
                text = ReadText(Path.Combine(settings.Cases, file.Rel.Replace('/', Path.DirectorySeparatorChar)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                crashLines[i] = $"{file.Rel}\tread-error: {ex.GetType().Name}: {FirstLine(ex.Message)}";
                return;
            }

            try
            {
                var dump = EmitWithPort(settings, file, text);
                hashLines[i] = $"{file.Rel}\t{Hash(dump):x16}\t{dump.Length}";
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // M7's loud, named blockers (Program.EmitNotLanded) land here like any other failure, per file: the
                // message names the unported member, so `port-crash: NotSupportedException: …` is a report of where the
                // port stops rather than a harness fault. The pass does not stop — one file's crash says nothing about
                // the next one's, and a corpus-wide pass is what M7 is measured with.
                crashLines[i] = $"{file.Rel}\tport-crash: {ex.GetType().Name}: {FirstLine(ex.Message)} @ {FrameNames(ex)}";
            }
        });

        for (var i = 0; i < files.Count; i++)
        {
            if (hashLines[i] is not null) hashes.WriteLine(hashLines[i]);
            if (crashLines[i] is not null) crashes.WriteLine(crashLines[i]);
        }
    }

    /// <summary>
    /// Runs <paramref name="body"/> for every index in <c>[0, count)</c>, at most <paramref name="parallel"/> at a time.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE (parallelism): the Go port fans a work item out as one goroutine (<c>tsc/internal/core/workgroup.go:34-41</c>);
    /// .NET cannot copy that shape (the thread pool would be the scheduler), so the port states the same intent with a
    /// bounded <see cref="Parallel.For(int, int, ParallelOptions, Action{int})"/> — one worker per core by default, and
    /// <c>--parallel 1</c> as the sequential path the tools' numbers were measured with before §18. A body that throws
    /// propagates the aggregate exception, exactly as the serial loop would propagate the first one.
    /// </remarks>
    private static void RunFiles(int parallel, int count, Action<int> body)
    {
        if (parallel <= 1 || count <= 1)
        {
            for (var i = 0; i < count; i++) body(i);
            return;
        }

        Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = parallel }, body);
    }

    /// <summary>Runs the port's <c>Program.Emit</c> over one file and returns the canonical dump of what it wrote.</summary>
    private static string EmitWithPort(Settings settings, CorpusFile file, string text)
    {
        var options = PortOptions(settings);
        var system = new EmitDiffSystem(file.Rel, text);
        // setParentNodes: false is the reference's own createCompilerHost default — the tsc command line leaves the
        // flag unset and program.ts:448-499 passes it straight to createSourceFile — and the printer walks children
        // rather than parents. (tools/TypeDiff passes true because a checker query can walk a parent.)
        var host = PortProgram.CreateCompilerHostWorker(options, setParentNodes: false, system);
        var program = PortProgram.CreateProgram([file.Rel], options, host);
        var outputs = new List<EmittedFile>();
        // The callback is the whole comparison: what the emitter hands writeFile *is* the emitted JavaScript, and the
        // program's own writeFile is only reached when no callback is supplied, so this tool never implements an emit
        // host (program.ts:2598-2637's `writeFile: writeFileCallback || writeFile`; the same shape ts-emit.cjs uses).
        var result = program.Emit(writeFile: (fileName, emitted, _, _, _, _, _) => outputs.Add(new EmittedFile(fileName, emitted)));
        return Dump(result.EmitSkipped, outputs);
    }

    /// <summary>
    /// The option set both halves are handed, frozen in <c>tools/EmitDiff/ts-emit.cjs</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the five frozen slots are set unless a <c>--source-map</c>-family flag asked for more; each of the five
    /// source-map slots is the same member of the reference's <c>CompilerOptions</c> and the node half sets the same
    /// ones from the same settings.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>newLine: LineFeed</c> is stated rather than left to each side's default because the emitted text is
    /// compared byte for byte: the reference's <c>getNewLineCharacter</c> (utilities.ts:7827-7835) answers
    /// <c>lineFeed</c> for both <c>LineFeed</c> and an unset <c>newLine</c>, so <em>this</em> reference is LF on every
    /// OS — but the port's own system supplies a newline too (§13's OS system) and a future reference version could
    /// answer <c>sys.newLine</c>, which would make the comparison depend on the host OS. Naming the value keeps the two
    /// option sets identical and the dump OS-independent.
    /// </para>
    /// <para>
    /// PORT-NOTE: the reference's <c>verifyCompilerOptions</c> asks for the emit host whenever
    /// <c>!noEmit &amp;&amp; !suppressOutputPathCheck</c> (program.ts:4392-4395: <c>const emitHost = getEmitHost();</c>
    /// then <c>forEachEmittedFile(emitHost, …)</c>), and its own <c>getEmitHost</c> is real. The port's
    /// <c>getEmitHost</c> is M7's loud blocker (<c>Program.EmitBoundary.cs</c>), so a program over default options
    /// cannot finish <c>createProgram</c> until M7 — the same reachability tools/TypeDiff takes
    /// <c>suppressOutputPathCheck</c> for. The flag is read in exactly two places in the reference (that block and
    /// program.ts:4588, the project-reference build-info path) and neither emits a diagnostic for a single-file corpus
    /// whose outputs cannot collide with its inputs, so both halves stay comparable; the node half sets the same flag.
    /// Removing this line is what would turn every port-side run into a <c>port-crash: NotSupportedException:
    /// getEmitHost …</c> instead of the emit itself.
    /// </para>
    /// </remarks>
    private static EmitDiffCompilerOptions PortOptions(Settings settings) => new()
    {
        Target = ScriptTarget.ESNext,
        Module = ModuleKind.ESNext,
        AllowJs = true,
        NewLine = NewLineKind.LineFeed,
        SuppressOutputPathCheck = true,
        // The source-map family, from the same five settings the node half reads: `sourceMap` writes `<name>.js.map`
        // beside the `.js` and appends the `//# sourceMappingURL=` comment (`emitter.ts:506-537`'s getSourceMapFilePath,
        // `emitter.ts:997-1055`'s getSourceMappingURL), `inlineSourceMap` inlines the map as a `data:` URL instead,
        // `inlineSources` adds `sourcesContent`, and `sourceRoot`/`mapRoot` rewrite the map's `sources` and the comment.
        // An unset slot stays null — the reference's `undefined`, which is what the frozen object has — so a run without
        // these flags hands both halves exactly the object they were handed before the flags existed.
        SourceMap = settings.SourceMap ? true : null,
        InlineSourceMap = settings.InlineSourceMap ? true : null,
        InlineSources = settings.InlineSources ? true : null,
        SourceRoot = settings.SourceRoot,
        MapRoot = settings.MapRoot,
    };

    /// <summary>
    /// The canonical dump of one emit: the run's header, then each emitted file framed by a begin line, its text
    /// verbatim, and an end line carrying the text's length.
    /// </summary>
    /// <remarks>
    /// The length goes on the *end* line rather than the begin line on purpose: a divergence anywhere in a file's text
    /// almost always changes its length too, and a length in the header would be the first difference of every such
    /// pair — the report would point at a number instead of at the emitted JavaScript it exists to point at. On the end
    /// line it is still compared (so a difference that is only a trailing newline is visible as a length, not hidden by
    /// the framing) without ever masking the first differing text line. The emitted files are printed in the order the
    /// emitter wrote them, never sorted: the order is emit behaviour (the reference's <c>emitFiles</c> walks the
    /// program's source files) and the differential exists to compare it. A dump with <c>files=0</c> is a legitimate
    /// result — a <c>.d.ts</c> input with declaration emit off produces no output on either side — which is why nothing
    /// here treats "no output" as a failure; what must never happen is a *port* that failed to emit counting as
    /// agreement, and that is what <c>port-crashes.txt</c> is for.
    /// </remarks>
    private static string Dump(bool emitSkipped, List<EmittedFile> outputs)
    {
        var sb = new StringBuilder();
        sb.Append(Header($"skipped={(emitSkipped ? "1" : "0")} files={outputs.Count}"));
        foreach (var output in outputs)
        {
            sb.Append(Header($"begin {EscapeText(output.FileName)}"));
            sb.Append(output.Text);
            // A text that does not end in a newline would glue its end line onto its last line; `len=` is the text's
            // own length, so the framing newline never changes what the end line reports.
            if (output.Text.Length == 0 || output.Text[^1] != '\n') sb.Append('\n');
            sb.Append(Header($"end {EscapeText(output.FileName)} len={output.Text.Length}"));
        }

        return sb.ToString();
    }

    /// <summary>One of the dump's header comment lines; the prefix is JavaScript's own line comment, so a dump is
    /// parseable JavaScript and the report's line numbers match the file it wrote.</summary>
    private static string Header(string text) => $"// EmitDiff {text}\n";

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

    /// <summary>
    /// One output file name, escaped so it cannot break the one-header-per-line shape: a space (the dump's field
    /// separator), any control character and <c>DEL</c> become <c>\uXXXX</c>, and <c>\</c>, <c>\n</c>, <c>\r</c>,
    /// <c>\t</c> are backslash-escaped. UTF-16 code units on both sides (the C# half iterates chars, not code points),
    /// so a surrogate pair passes through untouched — the same rule tools/TypeDiff records for the names it prints.
    /// </summary>
    private static string EscapeText(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < ' ' || ch == ' ' || ch == '\u007F')
                    {
                        sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(ch);
                    }

                    break;
            }
        }

        return sb.ToString();
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
            Console.Error.WriteLine($"emitdiff: node batch {batch + 1}/{batches} ({slice.Count} files)");

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
            // The source-map slots, one-for-one with what PortOptions set: a flag that was not given is not passed, so
            // the node half's options object stays the frozen five (see ts-emit.cjs's `options`).
            if (settings.SourceMap)
            {
                psi.ArgumentList.Add("--source-map");
            }

            if (settings.InlineSourceMap)
            {
                psi.ArgumentList.Add("--inline-source-map");
            }

            if (settings.InlineSources)
            {
                psi.ArgumentList.Add("--inline-sources");
            }

            if (settings.SourceRoot is not null)
            {
                psi.ArgumentList.Add("--source-root");
                psi.ArgumentList.Add(settings.SourceRoot);
            }

            if (settings.MapRoot is not null)
            {
                psi.ArgumentList.Add("--map-root");
                psi.ArgumentList.Add(settings.MapRoot);
            }

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
                error = $"emitdiff: cannot run '{settings.Node}' ({ex.GetType().Name}: {ex.Message}). "
                      + $"Pass --node <exe> if node is not on PATH, and --js <lib/typescript.js> for the reference compiler.";
                return false;
            }

            if (exitCode != 0)
            {
                error = $"emitdiff: {settings.Node} {Path.GetFileName(settings.NodeScript)} exited with {exitCode}\n{stderr}{stdout}";
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

    /// <summary>Re-emits the port's file for a diverging file, compares it with the node dump line by line, and writes
    /// both full dumps under <c>--out</c>.</summary>
    private static Divergence CompareFile(Settings settings, OutputPaths paths, CorpusFile file)
    {
        var tsText = ReadText(Path.Combine(paths.TsDumps, file.Rel.Replace('/', Path.DirectorySeparatorChar) + ".js"));
        string portText;
        try
        {
            portText = EmitWithPort(settings, file, ReadText(Path.Combine(settings.Cases, file.Rel.Replace('/', Path.DirectorySeparatorChar))));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new Divergence(file.Rel, 0, 0, $"port-recheck-crash: {ex.GetType().Name}: {FirstLine(ex.Message)}", null, null);
        }

        var target = Path.Combine(paths.PortDumps, file.Rel.Replace('/', Path.DirectorySeparatorChar) + ".js");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, portText, Utf8NoBom);

        var portLines = portText.Split('\n');
        var tsLines = tsText.Split('\n');
        var index = 0;
        while (index < portLines.Length && index < tsLines.Length && portLines[index] == tsLines[index]) index++;
        var portLine = index < portLines.Length ? portLines[index] : null;
        var tsLine = index < tsLines.Length ? tsLines[index] : null;
        return new Divergence(file.Rel, index + 1, FirstDifference(portLine, tsLine), Classify(index, portLine, tsLine), portLine, tsLine);
    }

    /// <summary>
    /// Names the field the two dumps part company on. The dump is one header comment per emitted file followed by that
    /// file's text, so the classification is a property of the two differing lines rather than a guess: line 1 is always
    /// the run's own header (the skipped flag and the output count), a pair of file headers is an output file name or
    /// length, a header facing a text line is the two sides emitting different numbers of files, and two text lines are
    /// the emitted JavaScript itself.
    /// </summary>
    private static string Classify(int index, string? portLine, string? tsLine)
    {
        if (portLine is null && tsLine is null) return "hash differs but the dumps are identical (hash collision)";
        if (portLine is null || tsLine is null) return "line-count mismatch: one dump ends where the other continues";
        if (index == 0) return "emit summary mismatch (skipped or output count)";
        var portKind = LineKind(portLine);
        var tsKind = LineKind(tsLine);
        if (portKind != tsKind) return $"record kind mismatch ({portKind} vs {tsKind})";
        return portKind switch
        {
            "emit summary" => "emit summary mismatch (skipped or output count)",
            // Both lines frame an output: a difference in the name explains everything after it, and a difference in
            // length with the same name is a text whose *length* differs (a trailing newline) while the two lines look
            // nearly identical in the report — the case the `len=` field exists for, and the reason it is on the
            // closing line where it can never be the first difference of a divergence inside the text.
            "output begin" or "output end" => NameOf(portLine) == NameOf(tsLine)
                ? "output length mismatch (same output name, different lengths)"
                : "output file name mismatch",
            _ => WhitespaceOnlyDifference(portLine, tsLine)
                ? "emitted text differs in whitespace only"
                : "emitted text differs",
        };
    }

    /// <summary>Which of the dump's line kinds <paramref name="line"/> is: the run's own header, a line framing an
    /// emitted file, or emitted text.</summary>
    private static string LineKind(string line)
        => !line.StartsWith("// EmitDiff ", StringComparison.Ordinal) ? "emitted text"
         : line.StartsWith("// EmitDiff skipped=", StringComparison.Ordinal) ? "emit summary"
         : line.StartsWith("// EmitDiff begin ", StringComparison.Ordinal) ? "output begin"
         : "output end";

    /// <summary>An output framer line's escaped output file name, i.e. everything before its <c>len=</c> field.</summary>
    private static string NameOf(string line)
    {
        var name = line["// EmitDiff begin ".Length..];
        var lengthAt = name.IndexOf(" len=", StringComparison.Ordinal);
        return lengthAt < 0 ? name : name[..lengthAt];
    }

    /// <summary>Whether two text lines differ only in whitespace, which for emitted JavaScript is nearly always an
    /// indentation or line-terminator difference rather than a different program.</summary>
    private static bool WhitespaceOnlyDifference(string portLine, string tsLine)
        => portLine.Where(ch => !char.IsWhiteSpace(ch)).SequenceEqual(tsLine.Where(ch => !char.IsWhiteSpace(ch)));

    /// <summary>The 1-based column of the first differing character on the two lines, counting UTF-16 code units; past
    /// the end of the shorter line when one is a prefix of the other.</summary>
    private static int FirstDifference(string? portLine, string? tsLine)
    {
        if (portLine is null || tsLine is null) return 1;
        var shared = Math.Min(portLine.Length, tsLine.Length);
        for (var i = 0; i < shared; i++)
        {
            if (portLine[i] != tsLine[i]) return i + 1;
        }

        return shared + 1;
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
        sb.Append("# EmitDiff emission differential — corpus ").Append(settings.Cases).Append(" vs ").Append(settings.Js).Append('\n');
        if (settings.Filter is not null) sb.Append("# filter ").Append(settings.Filter).Append('\n');
        if (settings.Limit is not null) sb.Append("# limit ").Append(settings.Limit.Value).Append('\n');
        sb.Append("# options target=ESNext module=ESNext allowJs=true newLine=LF suppressOutputPathCheck=true");
        // The source-map slots are part of the compared option set, so a report says which ones it ran with; a run
        // without the flags prints nothing more, which keeps an old report's header byte-identical.
        if (settings.SourceMap) sb.Append(" sourceMap=true");
        if (settings.InlineSourceMap) sb.Append(" inlineSourceMap=true");
        if (settings.InlineSources) sb.Append(" inlineSources=true");
        if (settings.SourceRoot is not null) sb.Append(" sourceRoot=").Append(settings.SourceRoot);
        if (settings.MapRoot is not null) sb.Append(" mapRoot=").Append(settings.MapRoot);
        sb.Append('\n');
        foreach (var divergence in divergences.OrderBy(d => d.File, StringComparer.Ordinal))
        {
            sb.Append("DIVERGE ").Append(divergence.File)
              .Append(" line=").Append(divergence.Line)
              .Append(" col=").Append(divergence.Column)
              .Append(" class=").Append(divergence.Classification)
              .Append(" | port: ").Append(Excerpt(divergence.PortLine, divergence.Column))
              .Append(" | ts: ").Append(Excerpt(divergence.TsLine, divergence.Column))
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
        // The tool reports its own peak — the number that says whether a run stayed within a machine, the same
        // self-report tools/BindDiff and tools/TypeDiff carry.
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

    /// <summary>
    /// One of the two differing lines as the report prints it: verbatim when it is short, and otherwise a window around
    /// the first differing column with <c>...</c> marking what was cut, so the character the two dumps part company on is
    /// never the part that got cut. The whole line is always in <c>&lt;out&gt;/port</c> or <c>&lt;out&gt;/ts</c>.
    /// </summary>
    private static string Excerpt(string? line, int column)
    {
        if (line is null) return "<end of dump>";
        const int width = 200;
        if (line.Length <= width) return line;
        var start = Math.Max(0, column - 1 - (width / 3));
        var end = Math.Min(line.Length, start + width);
        return (start > 0 ? "..." : "")
            + line[start..end]
            + (end < line.Length ? "..." : "");
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
                column = divergence.Column,
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
            // A filter without a wildcard is a directory prefix, which is what a user typing `expressions` means.
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
    /// emit (JSON: the port's JSON conversion is a named M8 residual).</summary>
    private static string? KindOf(string rel) => Path.GetExtension(rel).ToLowerInvariant() switch
    {
        ".ts" => "TS",
        ".tsx" => "TSX",
        ".js" or ".mjs" or ".cjs" => "JS",
        ".jsx" => "JSX",
        _ => null,
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
    /// The file's text as the parser is handed it: UTF-8, with a leading byte-order mark removed. The reference reads
    /// its files through <c>sys.readFile</c>, which strips the mark before the parser ever sees the text, so a harness
    /// that kept it would shift every position in a BOM-carrying file by one and emit a divergence that is not in the
    /// port. The node half strips it in <c>ts-emit.cjs</c> for the same reason.
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
        var assembly = typeof(PortProgram).Assembly;
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
        var parallelFiles = Environment.ProcessorCount;
        // The source-map flags: off unless given, so a run without them hands both halves the frozen option set and its
        // verdicts stay comparable with every earlier run's.
        var sourceMap = false;
        var inlineSourceMap = false;
        var inlineSources = false;
        string? sourceRoot = null, mapRoot = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--cases": cases = Next(args, ref i); break;
                case "--filter": filter = Next(args, ref i); break;
                case "--source-map": sourceMap = true; break;
                case "--inline-source-map": inlineSourceMap = true; break;
                case "--inline-sources": inlineSources = true; break;
                case "--source-root": sourceRoot = Next(args, ref i); break;
                case "--map-root": mapRoot = Next(args, ref i); break;
                case "--node-batch":
                    var batchRaw = Next(args, ref i);
                    if (batchRaw is null || !int.TryParse(batchRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var batch) || batch <= 0)
                    {
                        error = "emitdiff: --node-batch needs a positive integer\n" + Usage;
                        return false;
                    }

                    nodeBatch = batch;
                    break;

                case "--limit":
                    var raw = Next(args, ref i);
                    if (raw is null || !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
                    {
                        error = "emitdiff: --limit needs a positive integer\n" + Usage;
                        return false;
                    }

                    limit = parsed;
                    break;
                case "--parallel":
                    var parallelRaw = Next(args, ref i);
                    if (parallelRaw is null || !int.TryParse(parallelRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parallel) || parallel <= 0)
                    {
                        error = "emitdiff: --parallel needs a positive integer (1 = serial)\n" + Usage;
                        return false;
                    }

                    parallelFiles = parallel;
                    break;
                case "--out": outDir = Next(args, ref i); break;
                case "--js": js = Next(args, ref i); break;
                case "--node": node = Next(args, ref i); break;
                case "--node-script": nodeScript = Next(args, ref i); break;
                case "-h" or "--help": error = Usage; return false;
                default:
                    error = $"emitdiff: unknown argument '{args[i]}'\n" + Usage;
                    return false;
            }
        }

        if (cases is null)
        {
            error = "emitdiff: --cases is required\n" + Usage;
            return false;
        }

        if (!Directory.Exists(cases))
        {
            error = $"emitdiff: --cases '{cases}' is not a directory";
            return false;
        }

        cases = Path.GetFullPath(cases);
        outDir = Path.GetFullPath(outDir ?? Path.Combine(FindRepoRoot(), "artifacts", "emitdiff"));
        nodeScript ??= Path.Combine(AppContext.BaseDirectory, "ts-emit.cjs");
        if (!File.Exists(nodeScript))
        {
            error = $"emitdiff: the node emitter '{nodeScript}' does not exist (pass --node-script)";
            return false;
        }

        js = js is null ? FindReferenceLib(cases) : Path.GetFullPath(js);
        if (js is null || !File.Exists(js))
        {
            error = $"emitdiff: cannot find lib/typescript.js (looked up from '{cases}'); pass --js <file>";
            return false;
        }

        settings = new Settings
        {
            Cases = cases,
            Filter = filter,
            Limit = limit,
            NodeBatch = nodeBatch,
            Parallel = parallelFiles,
            Out = outDir,
            Js = js,
            Node = node ?? "node",
            NodeScript = nodeScript,
            SourceMap = sourceMap,
            InlineSourceMap = inlineSourceMap,
            InlineSources = inlineSources,
            SourceRoot = sourceRoot,
            MapRoot = mapRoot,
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
            error = $"emitdiff: cannot write {path}: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    /// <summary>One file the emitter wrote: the name it passed and the text it handed the callback, which is emitted
    /// JavaScript and nothing else (the byte-order-mark flag and the write-file callback data stay with the host).</summary>
    private sealed record EmittedFile(string FileName, string Text);

    private sealed record CorpusFile(string Rel, string Kind);

    private sealed record Manifest(string root, List<ManifestEntry> files);

    private sealed record ManifestEntry(string path, string kind);

    private sealed record Divergence(string File, int Line, int Column, string Classification, string? PortLine, string? TsLine);

    private sealed class Settings
    {
        public required string Cases { get; init; }
        public string? Filter { get; init; }
        public int? Limit { get; init; }

        /// <summary>
        /// The source-map slots both option objects carry, off unless the matching flag was given. They are the five
        /// members of the reference's <c>CompilerOptions</c> its source-map path reads (<c>sourceMap</c>,
        /// <c>inlineSourceMap</c>, <c>inlineSources</c>, <c>sourceRoot</c>, <c>mapRoot</c>), and
        /// <c>ts-emit.cjs</c>'s <c>options</c> object is set from the same five.
        /// </summary>
        public bool SourceMap { get; init; }

        public bool InlineSourceMap { get; init; }

        public bool InlineSources { get; init; }

        public string? SourceRoot { get; init; }

        public string? MapRoot { get; init; }

        /// <summary>
        /// How many files one <c>node</c> process handles before it is restarted (default 250).
        /// </summary>
        /// <remarks>
        /// Each file costs a <c>ts.createProgram</c> plus an emit, and node does not reclaim those quickly: with one
        /// process for the whole corpus the heap grows into the gigabytes. Restarting per batch bounds the peak at "the
        /// largest batch" (measured, and reported in the run summary) at the cost of one node startup per batch.
        /// </remarks>
        public int NodeBatch { get; init; } = 250;

        /// <summary>
        /// How many corpus files the port half compiles at once (default: <see cref="Environment.ProcessorCount"/>).
        /// </summary>
        /// <remarks>
        /// Every file is an independent program, so the pass fans out — this is the tool's own parallelism, and it is
        /// only possible because the compiler became reentrant (docs/architecture.md §18: atomic ids, per-worker
        /// printers/scanners/writers, concurrent caches). The two index files are still written in file order, so a
        /// parallel run's artifacts are byte-identical to a serial one's; <c>--parallel 1</c> is that serial path, kept
        /// for A/B measurement. Memory is the cost: each worker holds one program, so the peak grows with this number.
        /// </remarks>
        public int Parallel { get; init; } = Environment.ProcessorCount;
        public required string Out { get; init; }
        public required string Js { get; init; }
        public required string Node { get; init; }
        public required string NodeScript { get; init; }
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
