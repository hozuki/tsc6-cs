using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// The tool's own entry-point class is named Program (tools/AstDiff and tools/BindDiff do the same), which is also the
// name of the port's class the driver creates a program with; the alias keeps the two apart.
using PortProgram = TypeScript.Compiler.Program;

namespace TypeScript.Compiler.Tools;

/// <summary>
/// TypeDiff — the checker half of docs/architecture.md §7's outer loop, and the M6 checker differential: check every
/// file of a corpus twice, once with the port's <c>Program</c> + <c>Checker</c> and once with the real compiler, dump
/// what each type checker wrote for that file in one canonical line format, hash the text, and report the files whose
/// hashes differ together with their first differing line pair.
/// </summary>
/// <remarks>
/// <para>
/// The canonical dump (identical text on both sides for equal checker state, LF only, no trailing spaces), one record
/// per line:
/// </para>
/// <code>
/// N&lt;depth&gt; &lt;kind&gt;(&lt;pos&gt;,&lt;end&gt;) sym=&lt;#s|-&gt; type=&lt;#t|-&gt; sig=&lt;#g|-&gt;
/// X0 config=&lt;[#d,…]|-&gt; syntactic=&lt;[#d,…]|-&gt; options=&lt;[#d,…]|-&gt; global=&lt;[#d,…]|-&gt; semantic=&lt;[#d,…]|-&gt;
/// S#&lt;id&gt; flags=&lt;n&gt; flagsName=&lt;names|-&gt; name=&lt;escaped&gt; decls=&lt;n&gt; members=&lt;[name,…]|-&gt; exports=&lt;[name,…]|-&gt;
/// T#&lt;id&gt; flags=&lt;n&gt; flagsName=&lt;names|-&gt; id=&lt;n&gt; obj=&lt;n|-&gt; sym=&lt;#s|-&gt; alias=&lt;#s|-&gt; types=&lt;[#t,…]|-&gt; props=&lt;[name,…]|-&gt; sigs=&lt;[#g,…]|-&gt;
/// G#&lt;id&gt; decl=&lt;kind(pos,end)|-&gt; minArgs=&lt;n&gt; params=&lt;[#s,…]|-&gt; ret=&lt;#t|-&gt; tp=&lt;[#t,…]|-&gt;
/// D#&lt;id&gt; code=&lt;n&gt; start=&lt;n|-&gt; len=&lt;n|-&gt; cat=&lt;n&gt;
/// </code>
/// <para>
/// An <c>N</c> line is one node of the file's tree in <c>forEachChild</c> pre-order (depth 0 is the source file); its
/// <c>sym</c>/<c>type</c> are what the checker answers for <c>getSymbolAtLocation</c>/<c>getTypeAtLocation</c>, and
/// <c>sig</c> is the resolved signature for the call-like kinds (see <see cref="IsCallLike"/>). The five <c>X0</c>
/// lists are the program's own diagnostics for that file, in the order <c>getPreEmitDiagnostics</c> concatenates them.
/// An <c>S</c> line is a symbol printed once per file: its <c>SymbolFlags</c> word, that word's members, its escaped
/// name, its declaration count and the keys of its <c>members</c>/<c>exports</c> tables in the compiler's own order —
/// never sorted, because insertion order is itself checker state (§15.15's <c>SymbolTable</c> finding). A <c>T</c>
/// line is a type: <c>TypeFlags</c>, the checker's own type id, <c>ObjectFlags</c>, its symbol and alias symbol, a
/// union's/intersection's members, the properties the checker resolves for it and its call/construct signatures. A
/// <c>G</c> line is one signature and a <c>D</c> line one diagnostic.
/// </para>
/// <para>
/// Every record is printed once per file and every field that names another record is a <c>#id</c> reference, so the
/// dump grows with the graph it describes rather than with the number of references to it — the memory lesson §15.13
/// records for <c>tools/BindDiff</c>. The ids are per-file and assigned in order of first reference, and no field
/// carries a number the reference allocates process-globally: a name that embeds <c>getSymbolId</c> is normalized to
/// <c>?</c> (<see cref="NormalizeName"/>), which is what makes a dump identical whether its file was checked alone or
/// after a thousand others.
/// </para>
/// <para>
/// A node the reference's own API cannot answer for — <c>getTypeAtLocation</c> dereferences a module source file's
/// missing parent, and a parse-recovery declaration can have no symbol — prints all three of its fields as <c>!</c>
/// rather than dropping the record or throwing: <c>!</c> is data, so both halves must still agree on where the
/// reference gave up, and the batch summary reports how many records carried it.
/// </para>
/// <para>
/// Files are read as raw bytes and decoded as UTF-8 on both sides; the reference side strips a leading BOM, because
/// TS's <c>sys.readFile</c> does before the parser sees the text, and a BOM left in place would shift every position
/// in the file by one and report a divergence that is not in the port.
/// </para>
/// <para>
/// The tool never runs <c>node</c> once per file: it hands a whole batch of files to <c>ts-type-dump.cjs</c> in one
/// process (default 250, the batch size §15.13 measured), which checks every file of the batch with the real compiler
/// and writes a full dump only for the files whose hash differs from the port's. Full dumps land in <c>&lt;out&gt;/port</c>
/// and <c>&lt;out&gt;/ts</c>; nothing carries a timestamp and file order is the sorted relative path, so two runs over
/// the same corpus produce byte-identical reports.
/// </para>
/// </remarks>
internal static partial class Program
{
    private const string Usage = """
        TypeDiff — type-checker differential harness (port vs lib/typescript.js).

        Usage: dotnet run --project tools/TypeDiff -- --cases <dir> [options]

          --cases <dir>      corpus root (e.g. <TypeScript>/tests/cases/conformance)     [required]
          --filter <glob>    matches the '/'-separated path relative to --cases;
                             '*' does not cross '/', '**' does (e.g. classes/**)
          --limit <n>        keep the first n files after filtering (sorted by path)
          --parallel <n>     port-half files checked at once (default: ProcessorCount; 1 = serial)
          --out <dir>        output directory (default: <repo>/artifacts/typediff)
          --js <file>        the reference lib/typescript.js (default: nearest ancestor of
                             --cases that has one, else the sibling TS checkout)
          --node <exe>       node executable (default: node)
          --node-script <f>  the batch TS dumper (default: <tool bin>/ts-type-dump.cjs)
          --node-batch <n>   files per node process (default: 250; each batch reports its own peak RSS)
          --strict           check with `strict` on both sides (the reference's default is off)

        Files are the `.ts`/`.tsx`/`.js`/`.jsx`/`.mjs`/`.cjs` ones under --cases; JSON files are skipped because the
        port's JSON conversion is a named M8 residual, not a checker concern.

        Exit code: 0 = every hash matched, 1 = divergences or crashes were found (see <out>/report.txt),
        2 = bad arguments, 3 = node could not run, 4 = the port cannot create a checker yet (M6-5).
        """;

    /// <summary>FNV-1a's 64-bit offset basis and prime; hashed over UTF-16 code units, so both sides hash exactly the
    /// same sequence without an encoding step in between (the same choice tools/AstDiff and tools/BindDiff record).</summary>
    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static int Main(string[] args)
    {
        // 数字（kind/pos/end/flags/id）必须与 node 侧输出逐字符相同，所以整个进程用不变文化。
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
            Console.Error.WriteLine($"typediff: no files matched under {settings.Cases}" + (settings.Filter is null ? "" : $" (filter '{settings.Filter}')"));
            return 2;
        }

        // The M6-5 boundary is checked before anything is written, so a tool run on a port without a checker says why
        // instead of leaving a half-empty output directory behind.
        if (CheckerNotLanded() is { } notLanded)
        {
            Console.Error.WriteLine(notLanded);
            return 4;
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

        Console.Error.WriteLine($"typediff: {files.Count} files under {settings.Cases}; checking them with the port (parallel={settings.Parallel})");
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
            // The checker recurses over both trees and its own type graph; 256 MiB of reserve (virtual on x64) keeps a
            // legitimately deep file from dying, the same reserve tools/AstDiff and tools/BindDiff take.
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
        // agreement — including the case where every file crashed without the crash index being readable back.
        var clean = same == files.Count && missing == 0 && divergences.Count == 0 && crashes.Count == 0 && tsErrors.Count == 0;
        var verdict = clean ? 0 : 1;
        Console.Error.WriteLine(
            $"typediff: VERDICT {(clean ? "clean" : "fail")} same={same}/{files.Count} diverged={divergences.Count}"
            + $" port-crash={crashes.Count} ts-error={tsErrors.Count} unpaired={missing} exit={verdict}");
        Console.Error.WriteLine($"typediff: port pass {portElapsed.TotalSeconds:F2}s, total {started.Elapsed.TotalSeconds:F2}s");
        return verdict;
    }

    // ------------------------------------------------------------------------------------------------------------
    // The M6-5 boundary.
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The M6-5 boundary: the port cannot check anything until the checker's assembly slice lands.
    /// </summary>
    /// <remarks>
    /// docs/architecture.md §15.15 ruling 4 fixes the checker's delivery form as <c>Checker : ITypeChecker</c> — the
    /// class implementing the generated 380-member interface, which only the assembly slice can finish — and
    /// <c>Checker.CreateTypeChecker(ITypeCheckerHost) : ITypeChecker</c> is the factory <c>Program.GetTypeChecker()</c>
    /// (program.ts:2684) calls. Until that slice lands there is no checker to create, so the port half has nothing to
    /// compare and says so here, naming the milestone, rather than producing an empty or partial dump that would read
    /// as agreement. Nothing is stubbed: the probe below <em>is</em> the model — a checker that cannot be created is
    /// exactly a <see cref="Checker"/> that does not yet implement <see cref="ITypeChecker"/> — so the moment M6-5
    /// lands it stops firing and <see cref="PortPass"/> runs <c>program.GetTypeChecker()</c> like any other caller.
    /// <c>Program.EmitNotLanded</c> is the same shape for M7.
    /// </remarks>
    private static string? CheckerNotLanded()
        => typeof(ITypeChecker).IsAssignableFrom(typeof(Checker)) ? null : CheckerNotLandedMessage;

    private const string CheckerNotLandedMessage = """
        typediff: the port cannot create a type checker yet — M6-5 (the checker assembly) has not landed.

          Checker.CreateTypeChecker(ITypeCheckerHost) : ITypeChecker   the factory Program.GetTypeChecker() calls
          Checker : ITypeChecker                                       the assembly of the generated interface

        Both are M6-5's (docs/architecture.md §15.15 ruling 4), so there is nothing to compare the reference dumps
        with. Nothing here is stubbed — no fake ITypeChecker, no placeholder dump — because a partial dump would read
        as agreement; the node half stays runnable on its own (tools/TypeDiff/README.md), and this tool's port pass
        starts working the moment the checker's assembly lands.
        """;

    /// <summary>The one call that needs M6-5; a failure there is reported as the boundary, not as a port crash.</summary>
    private static ITypeChecker CreateChecker(PortProgram program)
    {
        try
        {
            return program.GetTypeChecker();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new CheckerNotLandedException(CheckerNotLandedMessage, ex);
        }
    }

    private sealed class CheckerNotLandedException(string message, Exception inner) : Exception(message, inner);

    // ------------------------------------------------------------------------------------------------------------
    // The port pass.
    // ------------------------------------------------------------------------------------------------------------

    private static void PortPass(Settings settings, List<CorpusFile> files, TextWriter hashes, TextWriter crashes)
    {
        // PORT-NOTE (parallelism): one file is one program plus its own checker, so the pass fans out and each worker
        // writes only its own slots; the slots are flushed in file order afterwards, which is what keeps the index files
        // byte-identical to a serial run's (they are compared between runs). `--parallel 1` is the serial path.
        var hashLines = new string?[files.Count];
        var crashLines = new string?[files.Count];
        var checkerNotLanded = 0;
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
                var dump = DumpWithPort(settings, file, text);
                hashLines[i] = $"{file.Rel}\t{Hash(dump.Text):x16}\t{dump.Text.Length}";
            }
            catch (CheckerNotLandedException ex)
            {
                // The pre-flight probe (CheckerNotLanded) rules this out before the pass starts, so this arm is the
                // belt-and-braces M6-5 left behind: the reason is named once (the first file to hit it), and the pass
                // does not stop — one file's failure says nothing about the next one's, which is also what the parallel
                // pass requires. The marker keeps its index slot so the file is written in order.
                if (Interlocked.Exchange(ref checkerNotLanded, 1) == 0)
                {
                    Console.Error.WriteLine(ex.Message);
                    crashLines[i] = $"<checker>\t{FirstLine(ex.Message)}";
                }
                else
                {
                    crashLines[i] = $"{file.Rel}\tport-crash: {ex.GetType().Name}: {FirstLine(ex.Message)} @ {FrameNames(ex)}";
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
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
    /// .NET cannot copy that shape (the thread pool is the scheduler), so the port states the same intent with a bounded
    /// <see cref="Parallel.For(int, int, ParallelOptions, Action{int})"/> — one worker per core by default, and
    /// <c>--parallel 1</c> as the sequential path. See docs/architecture.md §18.
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

    /// <summary>Runs the port's program and checker over one file and dumps what the checker wrote.</summary>
    private static DumpResult DumpWithPort(Settings settings, CorpusFile file, string text)
    {
        var options = PortOptions(settings);
        var system = new TypeDiffSystem(file.Rel, text);
        // setParentNodes: the node half parses with it (ts-type-dump.cjs's createSourceFile call), and a checker query
        // that walks a parent is the reference's own behaviour, so both sides must have the parents set.
        var host = PortProgram.CreateCompilerHostWorker(options, setParentNodes: true, system);
        var program = PortProgram.CreateProgram([file.Rel], options, host);
        var checker = CreateChecker(program);
        var sourceFile = program.GetSourceFile(file.Rel) ?? program.GetSourceFiles().FirstOrDefault();
        if (sourceFile is null)
        {
            throw new InvalidOperationException($"the program has no source file for '{file.Rel}'");
        }

        return Dump(checker, program, sourceFile);
    }

    private static TypeDiffCompilerOptions PortOptions(Settings settings) => new()
    {
        Target = ScriptTarget.ESNext,
        AllowJs = true,
        Strict = settings.Strict ? true : null,
        // PORT-NOTE: the reference's `verifyCompilerOptions` asks for the emit host whenever
        // `!noEmit && !suppressOutputPathCheck` (program.ts:4392-4395: `const emitHost = getEmitHost();` then
        // `forEachEmittedFile(emitHost, …)`), and its own `getEmitHost` is real. The port's `getEmitHost` is M7's loud
        // blocker (`Program.EmitBoundary.cs`), so a program over default options cannot finish `createProgram` until
        // M7 — the same reachability the checker's own end-to-end tests take `suppressOutputPathCheck` for
        // (`CheckerCreateTypeCheckerTests.CreateProgramOver`). The flag is read in exactly two places in the reference
        // (that block and program.ts:4588, the project-reference build-info path) and neither emits a diagnostic for a
        // single-file corpus whose outputs cannot collide with its inputs, so both halves stay comparable; the node
        // half sets the same flag (`ts-type-dump.cjs`), which is what keeps the option sets identical.
        SuppressOutputPathCheck = true,
    };

    /// <summary>The canonical dump of one file: its diagnostic lists, then every node with the records it refers to.</summary>
    private static DumpResult Dump(ITypeChecker checker, PortProgram program, SourceFile file)
    {
        // The five diagnostic lists the program exposes for this file, in the order getPreEmitDiagnostics concatenates
        // them. Computing them first is what makes the checker check the whole file before the walk below queries nodes.
        var diagnostics = new FileDiagnostics(
            program.GetConfigFileParsingDiagnostics(),
            program.GetSyntacticDiagnostics(file, null),
            program.GetOptionsDiagnostics(null),
            program.GetGlobalDiagnostics(null),
            program.GetSemanticDiagnostics(file, null));

        var outLines = new List<string>();
        var dumper = new Dumper(checker, outLines);
        dumper.Node(file, 0, diagnostics);
        return new DumpResult(string.Join("\n", outLines) + "\n", dumper.GaveUp);
    }

    /// <summary>The call-like kinds whose resolved signature both halves dump: the reference's <c>isCallLikeExpression</c>
    /// minus its <c>Decorator</c> arm, whose checking slice is not comparable yet.</summary>
    private static bool IsCallLike(SyntaxKind kind) => kind
        is SyntaxKind.CallExpression
        or SyntaxKind.NewExpression
        or SyntaxKind.TaggedTemplateExpression
        or SyntaxKind.JsxOpeningElement
        or SyntaxKind.JsxSelfClosingElement;

    /// <summary>
    /// Prints each symbol/type/signature/diagnostic once per file and refers to it by <c>#id</c> afterwards.
    /// </summary>
    /// <remarks>
    /// The two obvious alternatives both explode. Numbering inside a single <c>Key</c> call re-prints a type graph once
    /// per reference to it (the same file has thousands of references), and embedding the referenced entity's
    /// description into its referrer's line nests a whole subgraph into every ancestor's string. Either way the dump
    /// grows far faster than the graph it prints — the reason tools/BindDiff once reached tens of gigabytes on the full
    /// corpus (§15.13). So: one id map per file per record kind, one line per record, and every field that names
    /// another record is an <c>#id</c> reference (or <c>-</c>).
    /// </remarks>
    private sealed class Dumper(ITypeChecker checker, List<string> outLines)
    {
        private readonly Dictionary<ISymbol, int> _symbolIds = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<IType, int> _typeIds = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<ISignature, int> _signatureIds = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<IDiagnostic, int> _diagnosticIds = new(ReferenceEqualityComparer.Instance);
        private int _nextSymbol = 1;
        private int _nextType = 1;
        private int _nextSignature = 1;
        private int _nextDiagnostic = 1;

        /// <summary>How many nodes the checker's own API gave up on; the run summary reports it.</summary>
        public int GaveUp { get; private set; }

        /// <summary>
        /// One node of the tree, with the three records the checker answers for it — or <c>!</c> in all three when the
        /// checker gave up (see the class remarks of <see cref="Program"/>).
        /// </summary>
        public void Node(Node node, int depth, FileDiagnostics? diagnostics)
        {
            string record;
            try
            {
                var symbol = SymbolKey(checker.GetSymbolAtLocation(node));
                var type = TypeKey(checker.GetTypeAtLocation(node));
                var signature = IsCallLike(node.Kind) ? SignatureKey(checker.GetResolvedSignature(node, null, null)) : "-";
                record = $"N{depth} {(int)node.Kind}({node.Pos},{node.End}) sym={symbol} type={type} sig={signature}";
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                GaveUp++;
                record = $"N{depth} {(int)node.Kind}({node.Pos},{node.End}) sym=! type=! sig=!";
            }

            outLines.Add(record);
            if (diagnostics is { } lists)
            {
                outLines.Add($"X0 config={DiagnosticList(lists.Config)}"
                    + $" syntactic={DiagnosticList(lists.Syntactic)}"
                    + $" options={DiagnosticList(lists.Options)}"
                    + $" global={DiagnosticList(lists.Global)}"
                    + $" semantic={DiagnosticList(lists.Semantic)}");
            }

            ForEachChildTable.ForEachChild(node, child => Node(child, depth + 1, null));
        }

        private string SymbolKey(ISymbol? symbol)
        {
            if (symbol is null)
            {
                return "-";
            }

            if (_symbolIds.TryGetValue(symbol, out var existing))
            {
                return "#" + existing;
            }

            var id = _nextSymbol++;
            _symbolIds[symbol] = id;

            // The keys are the escaped names and are printed in the table's own order (insertion order): §15.15's
            // finding is that this order is checker state, so sorting here would hide exactly the divergence this tool
            // exists to catch.
            var members = NameList(symbol.Members);
            var exports = NameList(symbol.Exports);
            outLines.Add($"S#{id} flags={FlagWord(symbol.Flags)} flagsName={FlagsName(symbol.Flags)}"
                + $" name={NameText(symbol.EscapedName ?? string.Empty)} decls={symbol.Declarations?.Count ?? 0}"
                + $" members={members} exports={exports}");
            return "#" + id;
        }

        private string TypeKey(IType? type)
        {
            if (type is null)
            {
                return "-";
            }

            if (_typeIds.TryGetValue(type, out var existing))
            {
                return "#" + existing;
            }

            var id = _nextType++;
            _typeIds[type] = id;

            // TS keeps an `objectFlags` slot on every type object and leaves it undefined where nothing set it; the
            // port's models declare it as a non-nullable enum, so "unset" reads as 0 there. Both sides normalize
            // unset/zero to '-', which is what keeps a type whose slot the reference never wrote from reporting a
            // false divergence.
            var objectFlags = ObjectFlagsOf(type);
            var symbol = SymbolKey(type.Symbol);
            var alias = SymbolKey(type.AliasSymbol);
            // The `types` slot is read for every model that declares it, exactly as the reference's own
            // `Array.isArray(type.types)` test does (tools/TypeDiff/ts-type-dump.cjs:194): `types.ts:6724`'s
            // union/intersection constituents *and* `types.ts:6960`'s template literal span types. Reading only the
            // former printed `types=-` for every template literal whose reference side printed its constituents.
            var members = TypeList(type switch
            {
                IUnionOrIntersectionType union => union.Types,
                ITemplateLiteralType template => template.Types,
                _ => null,
            });
            var properties = PropertyList(checker.GetPropertiesOfType(type));
            var signatures = SignatureList(checker.GetSignaturesOfType(type, SignatureKind.Call), checker.GetSignaturesOfType(type, SignatureKind.Construct));
            outLines.Add($"T#{id} flags={FlagWord(type.Flags)} flagsName={FlagsName(type.Flags)} id={type.Id}"
                + $" obj={objectFlags} sym={symbol} alias={alias} types={members} props={properties} sigs={signatures}");
            return "#" + id;
        }

        private string SignatureKey(ISignature? signature)
        {
            if (signature is null)
            {
                return "-";
            }

            if (_signatureIds.TryGetValue(signature, out var existing))
            {
                return "#" + existing;
            }

            var id = _nextSignature++;
            _signatureIds[signature] = id;

            var declaration = signature.Declaration is { } declared
                ? $"{(int)declared.Kind}({declared.Pos},{declared.End})"
                : "-";
            var parameters = SymbolList(signature.Parameters);
            var typeParameters = TypeList(signature.TypeParameters);
            var returns = TypeKey(checker.GetReturnTypeOfSignature(signature));
            outLines.Add($"G#{id} decl={declaration} minArgs={signature.MinArgumentCount}"
                + $" params={parameters} ret={returns} tp={typeParameters}");
            return "#" + id;
        }

        private string DiagnosticKey(IDiagnostic diagnostic)
        {
            if (_diagnosticIds.TryGetValue(diagnostic, out var existing))
            {
                return "#" + existing;
            }

            var id = _nextDiagnostic++;
            _diagnosticIds[diagnostic] = id;
            outLines.Add($"D#{id} code={diagnostic.Code}"
                + $" start={Number(diagnostic.Start)} len={Number(diagnostic.Length)} cat={(int)diagnostic.Category}");
            return "#" + id;
        }

        private string DiagnosticList(IEnumerable<IDiagnostic> diagnostics)
        {
            List<string> keys = [];
            foreach (var diagnostic in diagnostics)
            {
                keys.Add(DiagnosticKey(diagnostic));
            }

            return List(keys);
        }

        private string SymbolList(IEnumerable<ISymbol>? symbols) => MapList(symbols, SymbolKey);

        private string TypeList(IEnumerable<IType>? types) => MapList(types, TypeKey);

        private string SignatureList(params IEnumerable<ISignature>[] signatures)
            => MapList(signatures.SelectMany(signature => signature), SignatureKey);

        private string PropertyList(IEnumerable<ISymbol> properties)
            => List([.. properties.Select(property => NameText(property.EscapedName ?? string.Empty))]);

        private string NameList(SymbolTable? table)
            => table is null ? "-" : List([.. table.Keys.Select(NameText)]);

        private string MapList<T>(IEnumerable<T>? items, Func<T, string> key)
        {
            if (items is null)
            {
                return "-";
            }

            List<string> keys = [];
            foreach (var item in items)
            {
                keys.Add(key(item));
            }

            return List(keys);
        }

        private static string List(List<string> items) => items.Count == 0 ? "-" : "[" + string.Join(",", items) + "]";

        private static string Number(int? value) => value is { } number ? number.ToString(CultureInfo.InvariantCulture) : "-";
    }

    /// <summary>The five diagnostic lists the program answers for one file, in <c>getPreEmitDiagnostics</c>' order.</summary>
    private sealed record FileDiagnostics(
        List<IDiagnostic> Config,
        List<IDiagnosticWithLocation> Syntactic,
        List<IDiagnostic> Options,
        List<IDiagnostic> Global,
        List<IDiagnostic> Semantic);

    private sealed record DumpResult(string Text, int GaveUp);

    // ------------------------------------------------------------------------------------------------------------
    // The canonical dump's text: names, flag words and the hash.
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A flag word as the reference's runtime holds it: a 32-bit signed integer.
    /// </summary>
    /// <remarks>
    /// TS builds a flags word with <c>|</c>, and JavaScript's bitwise operators are 32-bit signed, so a member declared
    /// <c>1 &lt;&lt; 31</c> reads back negative there (TypeFlags has one). The port's generated enums are <c>uint</c>-
    /// backed, so printing the raw value would disagree with the reference on exactly those bits — the id and the
    /// members are the same either way, but the number is part of the contract, so both halves print the reinterpreted
    /// 32-bit value.
    /// </remarks>
    private static string FlagWord<TEnum>(TEnum value)
        where TEnum : struct, Enum
        => unchecked((int)Convert.ToUInt32(value)).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// A composite flag word rendered as the members whose bits it contains.
    /// </summary>
    /// <remarks>
    /// Single-bit members only, ascending bit order, and for a bit several members share (TS's <c>AliasExcludes =
    /// Alias</c>, <c>IncludesMissingType = TypeParameter</c>) the lexicographically smallest name — so the text does
    /// not depend on either side's declaration order, which is what makes the port's generated enum and the
    /// reference's runtime enum produce the same string. <c>|?</c> marks bits no single-bit member covers.
    /// </remarks>
    private static string FlagsName<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        var raw = Convert.ToUInt64(value);
        if (raw == 0)
        {
            return "-";
        }

        var byValue = new Dictionary<ulong, string>();
        ulong knownMask = 0;
        foreach (var name in Enum.GetNames<TEnum>())
        {
            var memberValue = Convert.ToUInt64(Enum.Parse<TEnum>(name));
            if (memberValue == 0 || (memberValue & (memberValue - 1)) != 0)
            {
                continue;
            }

            knownMask |= memberValue;
            if (!byValue.TryGetValue(memberValue, out var current) || string.CompareOrdinal(name, current) < 0)
            {
                byValue[memberValue] = name;
            }
        }

        var parts = byValue
            .Where(pair => (raw & pair.Key) != 0)
            .OrderBy(pair => pair.Key)
            .Select(pair => pair.Value)
            .ToList();
        if (parts.Count == 0)
        {
            return "-";
        }

        return string.Join("|", parts) + ((raw & ~knownMask) != 0 ? "|?" : "");
    }

    /// <summary>
    /// The <c>ObjectFlags</c> of a type, printed for the models that carry the slot and <c>-</c> when it is unset.
    /// </summary>
    /// <remarks>
    /// The reference keeps the slot on every type object; the port declares it on the object/union/intersection/
    /// intrinsic/substitution models only, which is the set of shapes the reference actually writes it for. Zero reads
    /// as "unset" on both sides (see <c>ts-type-dump.cjs</c>'s comment on the same field).
    /// </remarks>
    private static string ObjectFlagsOf(IType type) => type switch
    {
        IObjectType { ObjectFlags: not ObjectFlags.None } o => ((int)o.ObjectFlags).ToString(CultureInfo.InvariantCulture),
        IUnionOrIntersectionType { ObjectFlags: not ObjectFlags.None } u => ((int)u.ObjectFlags).ToString(CultureInfo.InvariantCulture),
        IIntrinsicType { ObjectFlags: not ObjectFlags.None } i => ((int)i.ObjectFlags).ToString(CultureInfo.InvariantCulture),
        ISubstitutionType { ObjectFlags: not ObjectFlags.None } s => ((int)s.ObjectFlags).ToString(CultureInfo.InvariantCulture),
        _ => "-",
    };

    /// <summary>
    /// The name with the process-global symbol ids the checker bakes into two of its synthetic names replaced by
    /// <c>?</c>: utilities.ts:5450's <c>__#&lt;getSymbolId(class)&gt;@&lt;description&gt;</c> for a private-name property and
    /// checker.ts:20345's <c>__@&lt;symbolName&gt;@&lt;getSymbolId(symbol)&gt;</c> for a synthetic type's escaped name.
    /// </summary>
    /// <remarks>
    /// The id is allocated from a counter shared by every checker in the process, so a name that carries it is not
    /// comparable across two processes — or even across two batch layouts. Normalizing it is what makes a file's dump
    /// identical whether it was checked alone or after a thousand others (measured: one 1311-file batch and six
    /// 250-file batches produce byte-identical hash files), and it is the same discipline that keeps the harness's own
    /// <c>#id</c>s per-file.
    /// </remarks>
    private static string NormalizeName(string text)
        => SyntheticTypeNameId().Replace(PrivateNameSymbolId().Replace(text, "__#?@"), "$1?");

    [GeneratedRegex(@"__#\d+@", RegexOptions.CultureInvariant)]
    private static partial Regex PrivateNameSymbolId();

    [GeneratedRegex(@"\A(__@[^@]*@)\d+\z", RegexOptions.CultureInvariant)]
    private static partial Regex SyntheticTypeNameId();

    /// <summary>
    /// One name, escaped so it cannot break the one-record-per-line shape: a space (the dump's field separator), any
    /// control character and <c>DEL</c> become <c>\uXXXX</c>, and the backslash and the whitespace escapes are
    /// backslash-escaped. UTF-16 code units, so the node half (which also walks code units) emits the identical
    /// sequence, and a surrogate pair passes through untouched.
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

    private static string NameText(string text) => EscapeText(NormalizeName(text));

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
            Console.Error.WriteLine($"typediff: node batch {batch + 1}/{batches} ({slice.Count} files)");

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
            if (settings.Strict) psi.ArgumentList.Add("--strict");

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
                error = $"typediff: cannot run '{settings.Node}' ({ex.GetType().Name}: {ex.Message}). "
                      + $"Pass --node <exe> if node is not on PATH, and --js <lib/typescript.js> for the reference compiler.";
                return false;
            }

            if (exitCode != 0)
            {
                error = $"typediff: {settings.Node} {Path.GetFileName(settings.NodeScript)} exited with {exitCode}\n{stderr}{stdout}";
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

    /// <summary>Re-checks the port's file for a diverging file, compares it with the node dump line by line, and writes
    /// both full dumps under <c>--out</c>.</summary>
    private static Divergence CompareFile(Settings settings, OutputPaths paths, CorpusFile file)
    {
        var tsText = ReadText(Path.Combine(paths.TsDumps, file.Rel.Replace('/', Path.DirectorySeparatorChar) + ".txt"));
        string portText;
        try
        {
            portText = DumpWithPort(settings, file, ReadText(Path.Combine(settings.Cases, file.Rel.Replace('/', Path.DirectorySeparatorChar)))).Text;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new Divergence(file.Rel, 0, $"port-recheck-crash: {ex.GetType().Name}: {FirstLine(ex.Message)}", null, null);
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
    /// Names the field the two dumps part company on. The dump is one record with named fields per line by
    /// construction, so the classification is a property of the two differing lines rather than a guess: a node line
    /// whose <c>kind(pos,end)</c> prefix differs is a tree divergence (not this harness's subject), a <c>!</c> on one
    /// side only is the checker giving up on one side, an <c>X0</c> pair is one of the five diagnostic lists, and an
    /// <c>S</c>/<c>T</c>/<c>G</c>/<c>D</c> pair is a divergence inside that kind of record's first differing field.
    /// </summary>
    private static string Classify(string? portLine, string? tsLine)
    {
        if (portLine is null && tsLine is null) return "hash differs but the dumps are identical (hash collision)";
        if (portLine is null || tsLine is null) return "line-count mismatch: one dump ends where the other continues";
        if (portLine.Contains("=!", StringComparison.Ordinal) || tsLine.Contains("=!", StringComparison.Ordinal))
        {
            return "the checker gave up on this node on exactly one side";
        }

        var portTag = Tag(portLine);
        var tsTag = Tag(tsLine);
        if (portTag != tsTag) return $"record kind mismatch ({TagName(portTag)} vs {TagName(tsTag)})";
        switch (portTag)
        {
            case "X":
                return "diagnostic list mismatch";
            case "S":
            case "T":
            case "G":
            case "D":
                return FieldOf(portLine, tsLine, 1) is { } field ? $"{TagName(portTag)} {field} mismatch" : $"{TagName(portTag)} line mismatch";
            default:
                var portPrefix = NodePrefix().Match(portLine);
                var tsPrefix = NodePrefix().Match(tsLine);
                if (!portPrefix.Success || !tsPrefix.Success || portPrefix.Value != tsPrefix.Value) return "node mismatch (kind/pos/end)";
                return FieldOf(portLine, tsLine, 2) is { } nodeField ? $"node {nodeField} mismatch" : "node line mismatch";
        }
    }

    [GeneratedRegex(@"^N\d+ (\d+)\((-?\d+),(-?\d+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex NodePrefix();

    private static string Tag(string line) => line.Length == 0 ? "" : line[0] is 'N' or 'X' or 'S' or 'T' or 'G' or 'D' ? line[..1] : "";

    private static string TagName(string tag) => tag switch
    {
        "N" => "node",
        "X" => "diagnostics",
        "S" => "symbol",
        "T" => "type",
        "G" => "signature",
        "D" => "diagnostic",
        _ => "unknown record",
    };

    /// <summary>The whitespace-separated field <paramref name="index"/>'s name when the two lines agree on every
    /// earlier character position, else <c>null</c>.</summary>
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
        sb.Append("# TypeDiff checker differential — corpus ").Append(settings.Cases).Append(" vs ").Append(settings.Js).Append('\n');
        if (settings.Filter is not null) sb.Append("# filter ").Append(settings.Filter).Append('\n');
        if (settings.Limit is not null) sb.Append("# limit ").Append(settings.Limit.Value).Append('\n');
        if (settings.Strict) sb.Append("# strict on\n");
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
        // (tools/BindDiff reached tens of gigabytes before its dumper was rewritten to print each record once).
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
            // A filter without a wildcard is a directory prefix, which is what a user typing `classes` means.
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
    /// check (JSON: the port's JSON conversion is a named M8 residual).</summary>
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
    /// that kept it would shift every position in a BOM-carrying file by one and report a divergence that is not in the
    /// port. The node half strips it in <c>ts-type-dump.cjs</c> for the same reason.
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
        var assembly = typeof(ITypeChecker).Assembly;
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
        var strict = false;
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
                        error = "typediff: --node-batch needs a positive integer\n" + Usage;
                        return false;
                    }

                    nodeBatch = batch;
                    break;

                case "--limit":
                    var raw = Next(args, ref i);
                    if (raw is null || !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
                    {
                        error = "typediff: --limit needs a positive integer\n" + Usage;
                        return false;
                    }

                    limit = parsed;
                    break;
                case "--parallel":
                    var parallelRaw = Next(args, ref i);
                    if (parallelRaw is null || !int.TryParse(parallelRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parallel) || parallel <= 0)
                    {
                        error = "typediff: --parallel needs a positive integer (1 = serial)\n" + Usage;
                        return false;
                    }

                    parallelFiles = parallel;
                    break;
                case "--out": outDir = Next(args, ref i); break;
                case "--js": js = Next(args, ref i); break;
                case "--node": node = Next(args, ref i); break;
                case "--node-script": nodeScript = Next(args, ref i); break;
                case "--strict": strict = true; break;
                case "-h" or "--help": error = Usage; return false;
                default:
                    error = $"typediff: unknown argument '{args[i]}'\n" + Usage;
                    return false;
            }
        }

        if (cases is null)
        {
            error = "typediff: --cases is required\n" + Usage;
            return false;
        }

        if (!Directory.Exists(cases))
        {
            error = $"typediff: --cases '{cases}' is not a directory";
            return false;
        }

        cases = Path.GetFullPath(cases);
        outDir = Path.GetFullPath(outDir ?? Path.Combine(FindRepoRoot(), "artifacts", "typediff"));
        nodeScript ??= Path.Combine(AppContext.BaseDirectory, "ts-type-dump.cjs");
        if (!File.Exists(nodeScript))
        {
            error = $"typediff: the node dumper '{nodeScript}' does not exist (pass --node-script)";
            return false;
        }

        js = js is null ? FindReferenceLib(cases) : Path.GetFullPath(js);
        if (js is null || !File.Exists(js))
        {
            error = $"typediff: cannot find lib/typescript.js (looked up from '{cases}'); pass --js <file>";
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
            Strict = strict,
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
            error = $"typediff: cannot write {path}: {ex.GetType().Name}: {ex.Message}";
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

        /// <summary>
        /// How many corpus files the port half checks at once (default: <see cref="Environment.ProcessorCount"/>).
        /// </summary>
        /// <remarks>
        /// Every file is an independent program with its own checker, so the pass fans out — which is only possible
        /// because the compiler became reentrant (docs/architecture.md §18). The index files are still written in file
        /// order, so a parallel run's artifacts are byte-identical to a serial one's; <c>--parallel 1</c> is that serial
        /// path, kept for A/B measurement. Memory is the cost: each worker holds one program and its checker state, so
        /// the peak grows with this number (the checker's link tables dominate).
        /// </remarks>
        public int Parallel { get; init; } = Environment.ProcessorCount;
        public required string Out { get; init; }
        public required string Js { get; init; }
        public required string Node { get; init; }
        public required string NodeScript { get; init; }
        public bool Strict { get; init; }
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
