using System.Diagnostics;
using System.Text;
using System.Text.Json;
// The port's class the driver creates a program with; the alias keeps it apart from this tool's own entry-point class
// (tools/EmitDiff, tools/TypeDiff and tools/BaselineDiff carry the same one).
using PortProgram = TypeScript.Compiler.Program;

namespace TypeScript.Compiler.Tools;

/// <summary>
/// The port's **project mode**: one <see cref="Program"/> over a list of files, so the port can be timed the way
/// <c>tsc</c> is timed (<c>tsc -p tsconfig.json</c>) instead of the per-file model the three differential tools use.
/// </summary>
/// <remarks>
/// <para>
/// Why this exists: the acceptance rings build a *fresh* program per corpus file (a per-case host, real libs re-parsed
/// each time), which measures the harness model rather than the compiler. tsgo and the JS reference compile the same
/// 5,908 conformance files as **one** project in ~1.5 s and ~1.1 s respectively; without a project mode the port had no
/// comparable number (docs/architecture.md §17.15).
/// </para>
/// <para>
/// Phases are timed separately (program construction = parse + bind + file graph, syntactic diagnostics, semantic
/// diagnostics, emit) so a slow phase is visible instead of a single wall-clock number. The diagnostic count printed at
/// the end is the sanity check that the run did the same work as <c>tsc</c> (~2,670 errors on this corpus).
/// </para>
/// </remarks>
internal static class ProjectBench
{
    private static int Main(string[] args)
    {
        string? filesPath = null;
        string? diagnosticsPath = null;
        var tsDir = "D:/Source/GitHub/TypeScript";
        var emit = false;
        var perfMeasures = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--files": filesPath = i + 1 < args.Length ? args[++i] : null; break;
                case "--ts": tsDir = i + 1 < args.Length ? args[++i] : tsDir; break;
                case "--emit": emit = true; break;
                case "--perf-measures": perfMeasures = true; break;
                case "--diagnostics": diagnosticsPath = i + 1 < args.Length ? args[++i] : null; break;
                case "-h" or "--help":
                    Console.WriteLine("usage: ProjectBench --files <tsconfig.json with a files[] list> [--ts <TypeScript dir>] [--emit] [--diagnostics <file>] [--perf-measures]");
                    Console.WriteLine("       TSC_CS_PARALLEL=<n> sets the phase fan-out, TSC_CS_CHECKERS=<n> the checker pool size");
                    return 0;
                default:
                    Console.Error.WriteLine($"projectbench: unknown argument '{args[i]}'");
                    return 2;
            }
        }
        if (filesPath is null || !File.Exists(filesPath))
        {
            Console.Error.WriteLine("projectbench: --files <tsconfig.json> is required (a tsconfig whose \"files\" lists the roots)");
            return 2;
        }

        var tsconfig = JsonDocument.Parse(File.ReadAllText(filesPath));
        var rootNames = tsconfig.RootElement.GetProperty("files").EnumerateArray().Select(e => e.GetString()!).ToList();

        var options = new BenchCompilerOptions
        {
            Target = ScriptTarget.ESNext,
            Module = ModuleKind.ESNext,
            AllowJs = true,
            CheckJs = false,
            NoEmit = !emit,
            SkipLibCheck = true,
            Types = [],
        };

        var system = new DiskSystem(tsDir);
        if (perfMeasures)
        {
            // The reference's CLI enables the performance harness for --extendedDiagnostics; the tool does the same.
            TypeScript.Compiler.Performance.Enable(system);
        }

        var host = Program.CreateCompilerHostWorker(options, /*setParentNodes*/ false, system);

        var total = Stopwatch.StartNew();

        // Allocated bytes per phase, alongside the wall clock. This is the perf line's *noise-free* metric: the machine's
        // wall clock drifts by ±20% between runs of the same binary (docs/architecture.md §18.4g), while the bytes a phase
        // allocates are deterministic — so an allocation-reducing change can be judged by this column even when the
        // timing cannot separate it. `precise: true` asks for the exact accounting counter rather than the cheap one.
        var allocations = new long[4];
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);

        var t0 = Stopwatch.StartNew();
        var program = Program.CreateProgram(rootNames, options, host);
        t0.Stop();
        allocations[0] = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

        var sourceFiles = program.GetSourceFiles().ToList();
        var libFiles = sourceFiles.Count(f => f.IsDeclarationFile);

        var t1 = Stopwatch.StartNew();
        var syntactic = program.GetSyntacticDiagnostics(/*sourceFile*/ null, /*cancellationToken*/ null);
        t1.Stop();
        allocations[1] = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore - allocations[0];

        var t2 = Stopwatch.StartNew();
        var semantic = program.GetSemanticDiagnostics(/*sourceFile*/ null, /*cancellationToken*/ null);
        t2.Stop();
        allocations[2] = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore - allocations[0] - allocations[1];

        double emitSeconds = 0;
        var emittedFiles = 0;
        if (emit)
        {
            var t3 = Stopwatch.StartNew();
            system.CountWrites = true;
            var result = program.Emit();
            system.CountWrites = false;
            emittedFiles = system.Writes;
            t3.Stop();
            emitSeconds = t3.Elapsed.TotalSeconds;
            allocations[3] = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore - allocations[0] - allocations[1] - allocations[2];
            total.Stop();
            Console.WriteLine($"emit           {emitSeconds,8:F3} s   {MiB(allocations[3]),8} MiB   files={emittedFiles} skipped={result.EmitSkipped}");
        }
        total.Stop();

        Console.WriteLine($"roots          {rootNames.Count,8}     source files={sourceFiles.Count} (declaration files={libFiles})");
        Console.WriteLine($"createProgram  {t0.Elapsed.TotalSeconds,8:F3} s   {MiB(allocations[0]),8} MiB   parse + bind + file graph");
        Console.WriteLine($"syntactic      {t1.Elapsed.TotalSeconds,8:F3} s   {MiB(allocations[1]),8} MiB   {syntactic.Count} diagnostics");
        Console.WriteLine($"semantic       {t2.Elapsed.TotalSeconds,8:F3} s   {MiB(allocations[2]),8} MiB   {semantic.Count} diagnostics");
        Console.WriteLine($"allocated      {MiB(GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore),26} MiB   total (all phases)");
        Console.WriteLine($"total          {total.Elapsed.TotalSeconds,8:F3} s   (checked {sourceFiles.Count - libFiles} corpus files)");
        if (perfMeasures)
        {
            Console.WriteLine("# measures (the port's own Performance marks; the reference's CLI enables them for --extendedDiagnostics)");
            TypeScript.Compiler.Performance.ForEachMeasure((name, duration) =>
                Console.WriteLine($"# perf {name,-16} {duration,10:F1} ms"));
        }

        // The tool reports its own peak — the same self-report tools/EmitDiff, tools/TypeDiff and tools/BindDiff carry,
        // because the parallel phases trade memory for time and that trade has to be visible in the run's own numbers.
        Console.WriteLine($"# port peak working set {Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024)} MiB (parallel={Parallelism.Degree})");
        if (diagnosticsPath is not null)
        {
            WriteDiagnostics(diagnosticsPath, syntactic, semantic);
            Console.WriteLine($"diagnostics   {syntactic.Count + semantic.Count,8}     written to {diagnosticsPath}");
        }

        return 0;
    }

    /// <summary>Bytes as a one-decimal MiB figure for the report — see the allocation accounting in <c>Main</c>.</summary>
    private static double MiB(long bytes) => Math.Round(bytes / (1024.0 * 1024.0), 1);

    /// <summary>
    /// Writes every diagnostic the run produced, one line each: category, code, file, start, length, message.
    /// </summary>
    /// <remarks>
    /// This is the checker pool's oracle (docs/architecture.md §18.4d). The differential tools all build a *one-file*
    /// program, so their corpora never reach a pool size above one; a project-mode run does, and nothing else compares
    /// its diagnostics. Dumping them makes "the pool must not change what a program reports" checkable: two runs that
    /// differ only in <c>TSC_CS_CHECKERS</c> have to produce the same file, byte for byte. The message text comes from
    /// the diagnostic's own flattened message, so a deferred argument (`Diagnostic.DeferArg`) is rendered exactly as a
    /// baseline would render it.
    /// </remarks>
    private static void WriteDiagnostics(string path, List<IDiagnosticWithLocation> syntactic, List<IDiagnostic> semantic)
    {
        var lines = new List<string>(syntactic.Count + semantic.Count);
        foreach (var diagnostic in syntactic.Concat(semantic))
        {
            // `flattenDiagnosticMessageText` is the compiler's own renderer (program.ts:814-844), the same one
            // tools/BaselineDiff uses to write `.errors.txt` — so what this file records is what a baseline would say,
            // chains and deferred arguments included. `Diagnostic.Message` does not exist on the class; the message is
            // the `MessageText` slot.
            var message = PortProgram.FlattenDiagnosticMessageText(diagnostic.MessageText, "\n");
            lines.Add(string.Join(
                '\t',
                diagnostic.Category,
                diagnostic.Code,
                diagnostic.File?.FileName ?? "<global>",
                diagnostic.Start,
                diagnostic.Length,
                message.Replace('\r', ' ').Replace('\n', ' ')));
        }

        lines.Sort(StringComparer.Ordinal);
        File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}

/// <summary>
/// The disk-backed <see cref="ISystem"/> a project-mode run needs: the three differential tools each supply a host for
/// their own virtual file set, and this one reads the real file system so the roots, their imports and the default
/// library all resolve the way <c>tsc</c> resolves them.
/// </summary>
/// <remarks>
/// PORT-NOTE: the port has no <c>sys</c> singleton and no concrete <see cref="ISystem"/> under <c>src/</c> (§13.5), so
/// every program-creating tool states its own. Only the members a program-construction run reads are implemented; the
/// optional members keep their interface defaults (no watching, no modified times).
/// </remarks>
internal sealed class DiskSystem(string tsDir) : ISystem
{
    /// <summary>The directory the default library is resolved from: <c>getDefaultLibLocation()</c> is the directory of
    /// the executing file, and the reference's is its <c>lib/</c>.</summary>
    private readonly string _libDir = Paths.NormalizePath(tsDir) + "/lib";

    /// <summary>Counts <see cref="WriteFile"/> calls when a run wants the emitted-file count.</summary>
    public bool CountWrites { get; set; }

    public int Writes { get; private set; }

    public IList<string> Args => [];

    public string NewLine => "\n";

    public bool UseCaseSensitiveFileNames => false;

    public void Write(string s) => Console.Out.Write(s);

    public string? ReadFile(string path, string? encoding = null)
    {
        var full = ToDiskPath(path);
        return File.Exists(full) ? File.ReadAllText(full) : null;
    }

    public void WriteFile(string path, string data, bool writeByteOrderMark = false)
    {
        if (CountWrites) Writes++;
    }

    public string ResolvePath(string path) => Paths.NormalizePath(ToDiskPath(path)).Replace('\\', '/');

    public bool FileExists(string path) => File.Exists(ToDiskPath(path));

    public bool DirectoryExists(string path) => Directory.Exists(ToDiskPath(path));

    public void CreateDirectory(string path)
    {
    }

    public string GetExecutingFilePath() => _libDir + "/tsc.js";

    public string GetCurrentDirectory() => Paths.NormalizePath(tsDir) + "/";

    public IList<string> GetDirectories(string path)
        => Directory.Exists(ToDiskPath(path))
            ? Directory.GetDirectories(ToDiskPath(path)).Select(d => Paths.NormalizePath(d)).ToList()
            : [];

    public IList<string> ReadDirectory(
        string path,
        IList<string>? extensions = null,
        IList<string>? exclude = null,
        IList<string>? include = null,
        int? depth = null)
        => Directory.Exists(ToDiskPath(path))
            ? Directory.GetFiles(ToDiskPath(path))
                .Where(f => extensions is null || extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Select(f => Paths.NormalizePath(f))
                .ToList()
            : [];

    public void Exit(int exitCode = 0)
    {
    }

    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    /// <summary>The interface's optional member has a default; a project-mode run reports the real modified time so an
    /// incremental host could use it (the benchmark itself does not).</summary>
    public DateTimeOffset? GetModifiedTime(string path)
        => File.Exists(ToDiskPath(path)) ? File.GetLastWriteTimeUtc(ToDiskPath(path)) : null;

    /// <summary>Maps the program's normalized (forward-slash) path onto a path the CLR can open.</summary>
    private static string ToDiskPath(string path) => path.Replace('/', Path.DirectorySeparatorChar);
}
