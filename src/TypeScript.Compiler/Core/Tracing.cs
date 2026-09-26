using System.Globalization;
using System.Text;

namespace TypeScript.Compiler;

// ported from: src/compiler/tracing.ts:1-375
/// <summary>
/// Tracing events for the compiler: the devtools/ETW trace a <c>--generateTrace</c> run leaves behind
/// (<c>trace.json</c>, <c>types.json</c> and <c>legend.json</c>).
/// </summary>
/// <remarks>
/// The port keeps the reference's two-tier shape: <see cref="Tracing"/> is the module — its <see cref="Tracing.Current"/>
/// field is TS's <c>tracing</c> — and <see cref="TracingEnabled"/> is the <c>tracingEnabled</c> namespace object that
/// field is set to while a trace is in progress.
/// <para>
/// PORT-NOTE: TS writes the trace through a Node <c>fs</c> file descriptor that <c>startTracing</c> obtains with
/// <c>require("fs")</c>. It is the one compiler module that does not go through <c>sys</c>, because
/// <c>sys.writeFile</c> cannot stream. The port's host has no such member either, so the text is accumulated in a
/// <see cref="StringBuilder"/> and handed to <see cref="ISystem.WriteFile"/> when the trace stops. Two consequences,
/// neither of them observable in a run that completes: a trace of a crashed process is not written at all (TS leaves
/// the truncated prefix in place), and the trace is held in memory while it is produced (TS streams it to disk).
/// The host is passed to <see cref="Tracing.StartTracing"/> rather than obtained from a global, because the port has no
/// global <c>sys</c> (docs/architecture.md §13.5).
/// </para>
/// <para>
/// PORT-NOTE: TS hands plain JS objects to the engine's <c>JSON.stringify</c>. The port models those objects and their
/// values explicitly — <see cref="TracingArgs"/>, <see cref="TracingValue"/>, <see cref="TracingJson"/> — per §5 rules
/// 4/10, with <c>JSON.stringify</c>'s behaviour preserved: members are written in insertion order, a member whose value
/// is <c>undefined</c> is dropped, and an <c>undefined</c> array element is written as <c>null</c>.
/// </para>
/// </remarks>
internal static class Tracing
{
    // ported from: src/compiler/tracing.ts:38 — `type Mode = "project" | "build" | "server"`
    /// <summary>
    /// Which kind of compilation is being traced. TS's member values are strings; nothing serializes them — the
    /// members are only compared — so the port uses a plain enum (§4.6).
    /// </summary>
    internal enum Mode
    {
        Project,
        Build,
        Server,
    }

    // ported from: src/compiler/tracing.ts:132-140 — `export const enum Phase`
    /// <summary>
    /// The compiler phase an event belongs to: it is the <c>cat</c> field of the trace event that reports it.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>Phase</c> is a <em>string</em> enum whose member values are written into the trace
    /// (<c>cat: "${phase}"</c>). §4.1/§4.5 map a <c>const enum</c> to a plain C# enum with explicit values, so the
    /// strings live in <see cref="Tracing.PhaseName"/> instead of in the members.
    /// </remarks>
    internal enum Phase
    {
        Parse = 0,
        Program = 1,
        Bind = 2,
        Check = 3,
        CheckTypes = 4,
        Emit = 5,
        Session = 6,
    }

    // NOT ported from TS — it has no counterpart there, because the members carry these names as their values.
    /// <summary>The string TS's <see cref="Phase"/> member carries (tracing.ts:132-140).</summary>
    internal static string PhaseName(Phase phase) => phase switch
    {
        Phase.Parse => "parse",
        Phase.Program => "program",
        Phase.Bind => "bind",
        Phase.Check => "check",
        Phase.CheckTypes => "checkTypes",
        Phase.Emit => "emit",
        Phase.Session => "session",
        _ => Debug.Fail<string>($"Unknown tracing phase: {phase}"),
    };

    // ported from: src/compiler/tracing.ts:28-31
    /// <summary>
    /// The tracing state, or <c>null</c> while tracing is off — the field every call site reads as
    /// <c>Tracing.Current?.…</c>.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS declares <c>tracing</c> as <c>typeof tracingEnabled | undefined</c> and every use is a
    /// <c>tracing?.</c> guard, so the port keeps a nullable instance rather than a null object: <c>null</c> means
    /// "no trace in progress", which is also what <c>tracing?.recordType(...)</c> and <c>if (!tracing)</c> test.
    /// It is assigned only once the trace file has been opened (tracing.ts:95) and cleared by <c>stopTracing</c>.
    /// </remarks>
    internal static TracingEnabled? Current { get; set; }

    // ported from: src/compiler/tracing.ts:366-368 — the module-level alias of `tracingEnabled.startTracing`
    /// <summary>
    /// Starts tracing for the given project.
    /// </summary>
    /// <param name="mode">Which kind of compilation this is; decides the trace file naming.</param>
    /// <param name="traceDir">The directory the trace files are written to.</param>
    /// <param name="system">The host to create the directory and write the files through.</param>
    /// <param name="configFilePath">Recorded in the legend, for a later reader of the trace.</param>
    /// <remarks>
    /// PORT-NOTE: TS takes no host — it <c>require("fs")</c>s one. The port's host is an argument (§13.5), and that
    /// also removes TS's <c>tracing requires having fs</c> failure mode: there is no "host unavailable" case, only a
    /// host that cannot write, which surfaces from <see cref="ISystem.WriteFile"/>.
    /// </remarks>
    public static void StartTracing(Mode mode, string traceDir, ISystem system, string? configFilePath = null)
        => TracingEnabled.Instance.Start(mode, traceDir, system, configFilePath);

    // ported from: src/compiler/tracing.ts:369-370 — the module-level alias of `tracingEnabled.dumpLegend`
    /// <summary>
    /// Writes <c>legend.json</c>: which trace file belongs to which project. A no-op when tracing never started.
    /// </summary>
    public static void DumpLegend() => TracingEnabled.Instance.DumpLegend();
}

// ported from: src/compiler/tracing.ts:33-364 — `namespace tracingEnabled`
/// <summary>
/// The namespace object <see cref="Tracing.Current"/> points at while tracing is in progress.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS declares this as a namespace, whose members are functions over module-level state. §5 rule 6/12 map
/// that to a class; because the state has to outlive a start/stop cycle (the legend accumulates one record per
/// project and <c>traceCount</c> keeps counting across a build), there is exactly one instance and it is reachable
/// through <see cref="Tracing.Current"/> only when a trace is running.
/// <para>
/// PORT-NOTE: this subsystem is not thread-safe in TS and is not made so here; a trace belongs to one compilation on
/// one thread.
/// </para>
/// </remarks>
internal sealed class TracingEnabled
{
    /// <summary>
    /// The single instance <see cref="Tracing.Current"/> is set to (tracing.ts:95 — <c>tracing = tracingEnabled</c>).
    /// </summary>
    public static TracingEnabled Instance { get; } = new();

    private TracingEnabled()
    {
    }

    // ported from: src/compiler/tracing.ts:40 — `let fs`
    /// <summary>The host the trace is written through; set by <see cref="Start"/>.</summary>
    private ISystem? _system;

    // ported from: src/compiler/tracing.ts:42 — `let traceCount = 0`
    /// <summary>How many traces this process has started, used to number build-mode trace files.</summary>
    private int _traceCount;

    // ported from: src/compiler/tracing.ts:43 — `let traceFd = 0`
    /// <summary>
    /// The trace text produced so far, standing in for the open file descriptor TS writes through.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: see the class remarks — the host abstraction cannot stream, so the text is buffered until
    /// <see cref="StopTracing"/> flushes it.
    /// </remarks>
    private StringBuilder? _traceOutput;

    // ported from: src/compiler/tracing.ts:44 (the path `traceFd` was opened from) — the port keeps it because the
    // write is deferred.
    /// <summary>The file <see cref="_traceOutput"/> is flushed to.</summary>
    private string? _tracePath;

    // ported from: src/compiler/tracing.ts:45 — `let mode: Mode`
    /// <summary>Which kind of compilation is being traced; set by <see cref="Start"/>.</summary>
    private Tracing.Mode _mode;

    // ported from: src/compiler/tracing.ts:47 — `const typeCatalog: Type[] = []` (NB: id is index + 1)
    /// <summary>Every type the checker reported through <see cref="RecordType"/>, in the order it did.</summary>
    private readonly List<IType> _typeCatalog = [];

    // ported from: src/compiler/tracing.ts:49 — `let legendPath: string | undefined`
    /// <summary>Where <see cref="DumpLegend"/> writes; set by the first <see cref="Start"/> and kept after it.</summary>
    private string? _legendPath;

    // ported from: src/compiler/tracing.ts:50 — `const legend: TraceRecord[] = []`
    /// <summary>One record per trace started, written out by <see cref="DumpLegend"/>.</summary>
    private readonly List<TraceRecord> _legend = [];

    // ported from: src/compiler/tracing.ts:146
    /// <summary>The events <see cref="Push"/> opened and <see cref="Pop"/>/<see cref="PopAll"/> close.</summary>
    private readonly List<StackEvent> _eventStack = [];

    // ported from: src/compiler/tracing.ts:172-173 — `const sampleInterval = 1000 * 10`, "sample every 10ms"
    /// <summary>How far apart two events have to be for the shorter one to be dropped from the trace.</summary>
    private const double SampleInterval = 1000 * 10;

    // ported from: src/compiler/tracing.ts:57-105 — `startTracing`
    /// <summary>Starts tracing for the given project: opens the trace, creates the legend entry.</summary>
    /// <remarks>
    /// PORT-NOTE: TS asserts <c>!tracing</c> and <c>require("fs")</c>s its file system. The port asserts the same
    /// thing about <see cref="Tracing.Current"/> and takes the host as an argument, so the "fs is unavailable" throw
    /// has no counterpart (see <see cref="Tracing.StartTracing"/>). The trace directory is created through
    /// <see cref="Files.EnsureDirectoriesExist"/> — TS's <c>mkdirSync(dir, { recursive: true })</c>.
    /// <para>
    /// PORT-NOTE: TS skips the mkdir when the path merely *exists* (even as a file) and lets the later open fail.
    /// <see cref="ISystem.DirectoryExists"/> answers "is it a directory", so a file at that path makes the port try
    /// to create the directory and fail here instead of at the write. Same outcome, one step earlier.
    /// </para>
    /// </remarks>
    public void Start(Tracing.Mode tracingMode, string traceDir, ISystem system, string? configFilePath = null)
    {
        Debug.Assert(Tracing.Current is null, "Tracing already started");

        _system = system;
        _mode = tracingMode;
        _typeCatalog.Clear();

        _legendPath ??= Paths.CombinePaths(traceDir, "legend.json");

        // Note that writing will fail later on if it exists and is not a directory
        if (!system.DirectoryExists(traceDir))
        {
            Files.EnsureDirectoriesExist(traceDir, system.CreateDirectory, system.DirectoryExists);
        }

        var countPart = _mode == Tracing.Mode.Build ? $".{Environment.ProcessId}-{++_traceCount}"
            : _mode == Tracing.Mode.Server ? $".{Environment.ProcessId}"
            : "";
        var tracePath = Paths.CombinePaths(traceDir, $"trace{countPart}.json");
        var typesPath = Paths.CombinePaths(traceDir, $"types{countPart}.json");

        _legend.Add(new TraceRecord
        {
            ConfigFilePath = configFilePath,
            TracePath = tracePath,
            TypesPath = typesPath,
        });

        _tracePath = tracePath;
        _traceOutput = new StringBuilder();
        Tracing.Current = this; // only when traceFd is properly set

        // Start with a prefix that contains some metadata that the devtools profiler expects (also avoids a warning on import)
        var meta = new TracingArgs
        {
            ["cat"] = "__metadata",
            ["ph"] = "M",
            ["ts"] = 1000 * PerformanceCore.Timestamp(),
            ["pid"] = 1,
            ["tid"] = 1,
        };

        // TS spreads `meta` into each of the three header records; `With` is that spread, and the third record then
        // overwrites `cat`, which keeps the member's position — exactly as JS property assignment does.
        var processName = new TracingArgs { ["name"] = "process_name", ["args"] = new TracingArgs { ["name"] = "tsc" } }.With(meta);
        var threadName = new TracingArgs { ["name"] = "thread_name", ["args"] = new TracingArgs { ["name"] = "Main" } }.With(meta);
        var tracingStarted = new TracingArgs { ["name"] = "TracingStartedInBrowser" }.With(meta);
        tracingStarted["cat"] = "disabled-by-default-devtools.timeline";

        _traceOutput.Append("[\n");
        _traceOutput.Append(string.Join(",\n", new[] { processName, threadName, tracingStarted }.Select(TracingJson.Serialize)));
    }

    // ported from: src/compiler/tracing.ts:107-124 — `stopTracing`
    /// <summary>Stops tracing for the in-progress project and dumps the type catalog.</summary>
    /// <remarks>
    /// PORT-NOTE: TS closes the descriptor it was writing to; the port flushes the buffer through the host, which is
    /// the only point at which the trace file is written (see the class remarks).
    /// </remarks>
    public void StopTracing()
    {
        Debug.Assert(Tracing.Current is not null, "Tracing is not in progress");
        // Have a type catalog iff not in server mode
        Debug.Assert(_typeCatalog.Count > 0 == (_mode != Tracing.Mode.Server));

        _traceOutput!.Append("\n]\n");
        FlushTrace();
        Tracing.Current = null;

        if (_typeCatalog.Count > 0)
        {
            DumpTypes(_typeCatalog);
        }
        else
        {
            // We pre-computed this path for convenience, but clear it
            // now that the file won't be created.
            _legend[^1].TypesPath = null;
        }
    }

    // ported from: src/compiler/tracing.ts:126-130 — `recordType`
    /// <summary>
    /// Adds a type to the catalog <see cref="StopTracing"/> dumps. Dropped in server mode, where there is no
    /// convenient place to put the type information.
    /// </summary>
    public void RecordType(IType type)
    {
        if (_mode != Tracing.Mode.Server)
        {
            _typeCatalog.Add(type);
        }
    }

    // ported from: src/compiler/tracing.ts:142-144 — `instant`
    /// <summary>Records a point event.</summary>
    /// <remarks>TS passes the <c>"s":"g"</c> extra, which marks the event as "scope global".</remarks>
    public void Instant(Tracing.Phase phase, string name, TracingArgs? args = null)
        => WriteEvent("I", phase, name, args, extras: "\"s\":\"g\"");

    // ported from: src/compiler/tracing.ts:148-159 — `push`
    /// <summary>Starts a stack event: its duration is written when it is popped.</summary>
    /// <param name="separateBeginAndEnd">
    /// Used for special cases where we need the trace point even if the event never terminates (typically for
    /// reducing a scenario too big to trace to one that can be completed). In the future we might implement an exit
    /// handler to dump unfinished events, which would deprecate these operations.
    /// </param>
    public void Push(Tracing.Phase phase, string name, TracingArgs? args = null, bool separateBeginAndEnd = false)
    {
        if (separateBeginAndEnd)
        {
            WriteEvent("B", phase, name, args);
        }

        _eventStack.Add(new StackEvent(phase, name, args, 1000 * PerformanceCore.Timestamp(), separateBeginAndEnd));
    }

    // ported from: src/compiler/tracing.ts:160-164 — `pop`
    /// <summary>Closes the innermost stack event, writing its duration when it is long enough to be sampled.</summary>
    public void Pop(TracingArgs? results = null)
    {
        Debug.Assert(_eventStack.Count > 0);
        WriteStackEvent(_eventStack.Count - 1, 1000 * PerformanceCore.Timestamp(), results);
        _eventStack.RemoveAt(_eventStack.Count - 1);
    }

    // ported from: src/compiler/tracing.ts:165-171 — `popAll`
    /// <summary>Closes every open stack event at once, all of them ending at the same instant.</summary>
    public void PopAll()
    {
        var endTime = 1000 * PerformanceCore.Timestamp();
        for (var i = _eventStack.Count - 1; i >= 0; i--)
        {
            WriteStackEvent(i, endTime);
        }

        _eventStack.Clear();
    }

    // ported from: src/compiler/tracing.ts:174-184 — `writeStackEvent`
    /// <summary>Writes an open stack event out as an end event, or as a complete one when it was sampled.</summary>
    private void WriteStackEvent(int index, double endTime, TracingArgs? results = null)
    {
        var (phase, name, args, time, separateBeginAndEnd) = _eventStack[index];
        if (separateBeginAndEnd)
        {
            Debug.Assert(results is null, "`results` are not supported for events with `separateBeginAndEnd`");
            WriteEvent("E", phase, name, args, extras: null, endTime);
        }
        // test if [time,endTime) straddles a sampling point
        else if (SampleInterval - time % SampleInterval <= endTime - time)
        {
            // TS passes `{ ...args, results }`: the object is always present (an empty one when neither is), and a
            // `results` that is undefined is dropped from the JSON.
            var eventArgs = args?.Copy() ?? new TracingArgs();
            eventArgs["results"] = results;
            WriteEvent("X", phase, name, eventArgs, extras: $"\"dur\":{Js.NumberToString(endTime - time)}", time);
        }
    }

    // ported from: src/compiler/tracing.ts:186-197 — `writeEvent`
    /// <summary>Appends one trace event, in the format the devtools profiler reads.</summary>
    /// <remarks>
    /// PORT-NOTE: the event type, the name and the <c>extras</c> are interpolated into the line verbatim, as in TS;
    /// they are literals at every call site. <c>ts</c> and <c>dur</c> are JS numbers rendered by
    /// <see cref="Js.NumberToString"/>, which is the rendering those interpolation sites use in TS, whereas the JSON
    /// values below them go through <see cref="TracingJson"/> and <c>JSON.stringify</c>'s number rule.
    /// </remarks>
    private void WriteEvent(string eventType, Tracing.Phase phase, string name, TracingArgs? args, string? extras = null, double? time = null)
    {
        // In server mode, there's no easy way to dump type information, so we drop events that would require it.
        if (_mode == Tracing.Mode.Server && phase == Tracing.Phase.CheckTypes) return;

        Performance.Mark("beginTracing");
        var output = _traceOutput!;
        output.Append(",\n{\"pid\":1,\"tid\":1,\"ph\":\"").Append(eventType)
            .Append("\",\"cat\":\"").Append(Tracing.PhaseName(phase))
            .Append("\",\"ts\":").Append(Js.NumberToString(time ?? 1000 * PerformanceCore.Timestamp()))
            .Append(",\"name\":\"").Append(name).Append('"');
        if (extras is not null) output.Append(',').Append(extras);
        if (args is not null) output.Append(",\"args\":").Append(TracingJson.Serialize(args));
        output.Append('}');
        Performance.Mark("endTracing");
        Performance.Measure("Tracing", "beginTracing", "endTracing");
    }

    // ported from: src/compiler/tracing.ts:199-215 — `getLocation`
    /// <summary>The source location of a node, as a 1-based line/character pair, or <c>undefined</c> for a node that
    /// has no source file.</summary>
    /// <remarks>
    /// PORT-NOTE: TS takes <c>SourceFileLike</c> everywhere below <c>getSourceFileOfNode</c>; the port's
    /// <see cref="ISourceFileLike"/> is declared but implemented by nothing yet, so the line starts are read off the
    /// <see cref="SourceFile"/> directly — the same narrowing <c>Utilities.Binder.Diagnostics.cs</c> carries for the
    /// same reason, and both can call the interface member once <c>SourceFile</c> has that face.
    /// </remarks>
    private static TracingValue GetLocation(Node? node)
    {
        var file = Utilities.GetSourceFileOfNode(node);
        if (file is null)
        {
            return TracingValue.Undefined;
        }

        var lineStarts = file.LineMap ?? (file.LineMap = ScannerUtilities.ComputeLineStarts(file.Text ?? ""));
        return TracingValue.From(new TracingArgs
        {
            ["path"] = file.Path,
            ["start"] = IndexFromOne(ScannerUtilities.ComputeLineAndCharacterOfPosition(lineStarts, node!.Pos)),
            ["end"] = IndexFromOne(ScannerUtilities.ComputeLineAndCharacterOfPosition(lineStarts, node.End)),
        });

        static TracingValue IndexFromOne(LineAndCharacter lc) => TracingValue.From(new TracingArgs
        {
            ["line"] = lc.Line + 1,
            ["character"] = lc.Character + 1,
        });
    }

    // ported from: src/compiler/tracing.ts:217-349 — `dumpTypes`
    /// <summary>Writes the type catalog to <c>types.json</c>, one descriptor per line.</summary>
    /// <remarks>
    /// PORT-NOTE: TS opens the file before the loop and writes the descriptor as it goes, so a throw in the middle
    /// leaves a truncated file behind. The port builds the text and writes it once, so a throw leaves no file — the
    /// same "the host cannot stream" trade-off the class remarks describe.
    /// <para>
    /// PORT-NOTE: TS reads members that its interfaces declare non-optional (<c>type.checker</c>,
    /// <c>evolvingArrayType.elementType</c>) as if a null could not occur. The port's generated interfaces are all
    /// nullable, so those reads use <c>?.</c>: where TS would throw on a null, the port writes the descriptor it
    /// writes for an absent value. The one deliberate difference is <c>type.checker.getRecursionIdentity</c>, which
    /// TS calls unguarded and the port calls through <c>?.</c>.
    /// </para>
    /// </remarks>
    private void DumpTypes(IList<IType> types)
    {
        Performance.Mark("beginDumpTypes");

        var typesPath = _legend[^1].TypesPath!;
        var output = new StringBuilder();

        var recursionIdentityMap = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);

        // Cleverness: no line break here so that the type ID will match the line number
        output.Append('[');

        var numTypes = types.Count;
        for (var i = 0; i < numTypes; i++)
        {
            var type = types[i];
            var objectFlags = GetObjectFlags(type);
            var symbol = type.AliasSymbol ?? type.Symbol;

            // It's slow to compute the display text, so skip it unless it's really valuable (or cheap)
            string? display = null;
            if ((objectFlags & ObjectFlags.Anonymous) != 0 || (type.Flags & TypeFlags.Literal) != 0)
            {
                try
                {
                    display = type.Checker?.TypeToString(type, null, null);
                }
                catch
                {
                    display = null;
                }
            }

            // We can't print out an arbitrary object, so just assign each one a unique number.
            // Don't call it an "id" so people don't treat it as a type id.
            int? recursionToken = null;
            var recursionIdentity = type.Checker?.GetRecursionIdentity(type);
            if (Js.IsTruthy(recursionIdentity))
            {
                // TS: `recursionIdentityMap.get(identity)` — an absent identity reads as `undefined`, which the falsy
                // test below treats exactly like the token 0.
                recursionIdentityMap.TryGetValue(recursionIdentity!, out var token);
                // PORT-NOTE: TS's `if (!recursionToken)` is true for the token 0 as well as for `undefined`, so an
                // identity that was handed token 0 is renumbered on every later lookup. Faithful, see
                // docs/architecture.md §10.3.
                if (!Js.IsTruthy(token))
                {
                    token = recursionIdentityMap.Count;
                    recursionIdentityMap[recursionIdentity!] = token;
                }

                recursionToken = token;
            }

            var escapedName = symbol?.EscapedName;
            var descriptor = new TracingArgs
            {
                ["id"] = type.Id,
                ["intrinsicName"] = (type as IIntrinsicType)?.IntrinsicName,
                ["symbolName"] = escapedName is null || escapedName.Length == 0
                    ? TracingValue.From(escapedName)
                    : TracingValue.From(Utilities.UnescapeLeadingUnderscores(escapedName)),
                ["recursionId"] = recursionToken,
                ["isTuple"] = (objectFlags & ObjectFlags.Tuple) != 0 ? true : (bool?)null,
                ["unionTypes"] = (type.Flags & TypeFlags.Union) != 0 ? IdList(((IUnionType)type).Types) : TracingValue.Undefined,
                ["intersectionTypes"] = (type.Flags & TypeFlags.Intersection) != 0 ? IdList(((IIntersectionType)type).Types) : TracingValue.Undefined,
                ["aliasTypeArguments"] = IdList(type.AliasTypeArguments),
                // PORT-NOTE: TS reads `(type as IndexType).type?.id`; the port's `IIndexType.Type` is `IType?`, so
                // the id read here is the indexed type's own id, exactly as upstream (tracing.ts:324).
                ["keyofType"] = (type.Flags & TypeFlags.Index) != 0 ? ((IIndexType)type).Type?.Id : (int?)null,
            };

            // TS spreads one of these property bags in at this point, which is the position JS gives the members it
            // contributes; the assignments below keep that order.
            if ((type.Flags & TypeFlags.IndexedAccess) != 0)
            {
                var indexedAccessType = (IIndexedAccessType)type;
                descriptor["indexedAccessObjectType"] = indexedAccessType.ObjectType?.Id;
                descriptor["indexedAccessIndexType"] = indexedAccessType.IndexType?.Id;
            }

            if ((objectFlags & ObjectFlags.Reference) != 0)
            {
                var referenceType = (ITypeReference)type;
                descriptor["instantiatedType"] = referenceType.Target?.Id;
                descriptor["typeArguments"] = IdList(referenceType.ResolvedTypeArguments);
                descriptor["referenceLocation"] = GetLocation(referenceType.Node);
            }

            if ((type.Flags & TypeFlags.Conditional) != 0)
            {
                var conditionalType = (IConditionalType)type;
                descriptor["conditionalCheckType"] = conditionalType.CheckType?.Id;
                descriptor["conditionalExtendsType"] = conditionalType.ExtendsType?.Id;
                descriptor["conditionalTrueType"] = conditionalType.ResolvedTrueType?.Id ?? -1;
                descriptor["conditionalFalseType"] = conditionalType.ResolvedFalseType?.Id ?? -1;
            }

            if ((type.Flags & TypeFlags.Substitution) != 0)
            {
                var substitutionType = (ISubstitutionType)type;
                descriptor["substitutionBaseType"] = substitutionType.BaseType?.Id;
                descriptor["constraintType"] = substitutionType.Constraint?.Id;
            }

            if ((objectFlags & ObjectFlags.ReverseMapped) != 0)
            {
                var reverseMappedType = (IReverseMappedType)type;
                descriptor["reverseMappedSourceType"] = reverseMappedType.Source?.Id;
                descriptor["reverseMappedMappedType"] = reverseMappedType.MappedType?.Id;
                descriptor["reverseMappedConstraintType"] = reverseMappedType.ConstraintType?.Id;
            }

            if ((objectFlags & ObjectFlags.EvolvingArray) != 0)
            {
                var evolvingArrayType = (IEvolvingArrayType)type;
                descriptor["evolvingArrayElementType"] = evolvingArrayType.ElementType?.Id;
                descriptor["evolvingArrayFinalType"] = evolvingArrayType.FinalArrayType?.Id;
            }

            descriptor["destructuringPattern"] = GetLocation(type.Pattern);
            descriptor["firstDeclaration"] = GetLocation(
                symbol?.Declarations is { Count: > 0 } declarations ? declarations[0] as Node : null);
            descriptor["flags"] = TracingValue.Array(Debug.FormatTypeFlags(type.Flags).Split('|').Select(TracingValue.From));
            descriptor["display"] = display;

            output.Append(TracingJson.Serialize(descriptor));
            if (i < numTypes - 1)
            {
                output.Append(",\n");
            }
        }

        output.Append("]\n");

        FlushTypes(typesPath, output.ToString());

        Performance.Mark("endDumpTypes");
        Performance.Measure("Dump types", "beginDumpTypes", "endDumpTypes");

        // TS's `resolvedTypeArguments?.map(t => t.id)` and friends: absent means `undefined`, so the member is dropped.
        static TracingValue IdList(List<IType>? types) => types is null
            ? TracingValue.Undefined
            : TracingValue.Array(types.Select(t => TracingValue.From(t.Id)));
    }

    // ported from: src/compiler/tracing.ts:351-357 — `dumpLegend`
    /// <summary>Writes the legend. Does nothing when tracing never started (there is no path to write to).</summary>
    public void DumpLegend()
    {
        if (_legendPath is null)
        {
            return;
        }

        Debug.Assert(_system is not null);
        // TS: JSON.stringify(legend) — each record is a literal whose absent members are `undefined`, so they are
        // dropped; the port builds the same three members in the same order.
        var entries = _legend.Select(static record => TracingValue.From(new TracingArgs
        {
            ["configFilePath"] = record.ConfigFilePath,
            ["tracePath"] = record.TracePath,
            ["typesPath"] = record.TypesPath,
        }));
        _system.WriteFile(_legendPath, TracingJson.Serialize(TracingValue.Array(entries)));
    }

    // NOT ported from TS — it has no counterpart there.
    /// <summary>Writes the accumulated trace through the host, once, when the trace stops.</summary>
    private void FlushTrace()
    {
        Debug.Assert(_system is not null && _tracePath is not null && _traceOutput is not null);
        _system.WriteFile(_tracePath, _traceOutput.ToString());
        _traceOutput = null;
    }

    // NOT ported from TS — it has no counterpart there.
    /// <summary>Writes the type catalog through the host.</summary>
    private void FlushTypes(string typesPath, string text)
    {
        Debug.Assert(_system is not null);
        _system.WriteFile(typesPath, text);
    }

    // ported from: src/compiler/tracing.ts:231 — `const objectFlags = (type as any).objectFlags`
    /// <summary>
    /// The object flags of a type, or none when it has no such property.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS reads the property off the object and gets <c>undefined</c> for a type that does not carry it,
    /// which behaves like 0 in the bit tests below. The port asks the interfaces that declare it — types.ts:
    /// <c>IntrinsicType</c>/<c>NullableType</c>, <c>ObjectType</c>, <c>UnionOrIntersectionType</c>,
    /// <c>SubstitutionType</c>.
    /// </remarks>
    private static ObjectFlags GetObjectFlags(IType type) => type switch
    {
        ISubstitutionType substitutionType => substitutionType.ObjectFlags,
        IIntrinsicType intrinsicType => intrinsicType.ObjectFlags,
        IObjectType objectType => objectType.ObjectFlags,
        IUnionOrIntersectionType unionOrIntersectionType => unionOrIntersectionType.ObjectFlags,
        _ => 0,
    };

    // ported from: src/compiler/tracing.ts:146 — `const eventStack: { phase; name; args?; time; separateBeginAndEnd }[]`
    /// <summary>An event <see cref="Push"/> opened and has not closed yet.</summary>
    private sealed record StackEvent(
        Tracing.Phase Phase,
        string Name,
        TracingArgs? Args,
        double Time,
        bool SeparateBeginAndEnd);

    // ported from: src/compiler/tracing.ts:359-363 — `interface TraceRecord`
    /// <summary>One legend entry: which project a trace file belongs to.</summary>
    private sealed class TraceRecord
    {
        public string? ConfigFilePath { get; set; }

        public required string TracePath { get; set; }

        public string? TypesPath { get; set; }
    }
}

// ported from: src/compiler/tracing.ts:52-55 — `interface Args`
/// <summary>
/// The named values a trace event carries: a JS object, in insertion order.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS declares <c>Args</c> as an index signature over a value union and builds instances as object
/// literals (<c>{ path: file.path }</c>). The port needs a constructible type for that literal, so this is a
/// <see cref="TracingValue"/>-valued map that an object initializer or <see cref="Of"/> can produce. Assignment to an
/// existing name keeps its position and replaces the value, which is what JS property assignment does (the only
/// order-sensitive use is <c>StopTracing</c> clearing <c>typesPath</c> and the header's <c>cat</c> override).
/// </remarks>
internal sealed class TracingArgs
{
    private readonly List<(string Key, TracingValue Value)> _entries = [];

    /// <summary>Builds an argument bag from key/value pairs: <c>TracingArgs.Of(("path", file.Path))</c>.</summary>
    public static TracingArgs Of(params (string Key, TracingValue Value)[] entries)
    {
        var args = new TracingArgs();
        foreach (var (key, value) in entries)
        {
            args[key] = value;
        }

        return args;
    }

    /// <summary>The object-initializer face of <see cref="Of"/>: <c>new TracingArgs { ["path"] = … }</c>.</summary>
    public TracingValue this[string key]
    {
        set => Set(key, value);
    }

    /// <summary>TS's object spread, <c>{ ...this, ...other }</c>.</summary>
    public TracingArgs With(TracingArgs other)
    {
        var result = Copy();
        foreach (var (key, value) in other._entries)
        {
            result[key] = value;
        }

        return result;
    }

    /// <summary>A shallow copy, for the places TS spreads an argument bag into a new literal.</summary>
    public TracingArgs Copy()
    {
        var copy = new TracingArgs();
        copy._entries.AddRange(_entries);
        return copy;
    }

    /// <summary>The members, in insertion order — the order they are written in.</summary>
    internal IReadOnlyList<(string Key, TracingValue Value)> Entries => _entries;

    /// <summary>Sets a member: an existing name keeps its position, a new one is appended.</summary>
    private void Set(string key, TracingValue value)
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Key == key)
            {
                _entries[i] = (key, value);
                return;
            }
        }

        _entries.Add((key, value));
    }
}

// NOT ported from TS — the engine's value union behind `Args` has no nominal counterpart there.
/// <summary>
/// A value a trace event can carry: what JS's <c>undefined</c>, <c>null</c>, <c>boolean</c>, <c>number</c>,
/// <c>string</c>, object and array are in one type.
/// </summary>
/// <remarks>
/// PORT-NOTE: <c>Args</c> is typed in TS as a union of JSON-able JS values, and the distinction that matters is
/// <c>undefined</c> (dropped by <c>JSON.stringify</c>) versus <c>null</c> (written). C# has no such union, so this
/// structure carries the tag; the implicit conversions let a call site pass a <c>string?</c>, <c>double?</c>,
/// <c>bool?</c> or <see cref="TracingArgs"/> directly, with <c>null</c> meaning <c>undefined</c> (§5 rule 1).
/// </remarks>
internal readonly struct TracingValue
{
    private readonly double _number;
    private readonly object? _reference;
    private readonly Kind _kind;

    private TracingValue(Kind kind, double number = 0d, object? reference = null)
    {
        _kind = kind;
        _number = number;
        _reference = reference;
    }

    /// <summary>What the value is — the tag of the JS value union.</summary>
    internal enum Kind : byte
    {
        Undefined,
        Null,
        Boolean,
        Number,
        String,
        Object,
        Array,
    }

    /// <summary>JS's <c>undefined</c>: the value of an absent member.</summary>
    public static TracingValue Undefined => new(Kind.Undefined);

    /// <summary>
    /// JS's <c>null</c>, which unlike <c>undefined</c> is written out. The <c>Args</c> value union admits it
    /// (tracing.ts:54); no call site in the reference passes one, so nothing produces it today.
    /// </summary>
    public static TracingValue Null => new(Kind.Null);

    /// <summary>A string value; a null string is <c>undefined</c>.</summary>
    public static TracingValue From(string? value) => value is null ? Undefined : new TracingValue(Kind.String, reference: value);

    /// <summary>A number value; a null number is <c>undefined</c>.</summary>
    public static TracingValue From(double? value) => value is null ? Undefined : new TracingValue(Kind.Number, value.Value);

    /// <summary>A boolean value; a null boolean is <c>undefined</c>.</summary>
    public static TracingValue From(bool? value) => value is null ? Undefined : new TracingValue(Kind.Boolean, reference: value.Value);

    /// <summary>An object value; a null object is <c>undefined</c>.</summary>
    public static TracingValue From(TracingArgs? value) => value is null ? Undefined : new TracingValue(Kind.Object, reference: value);

    /// <summary>An array value; a null sequence is <c>undefined</c>.</summary>
    public static TracingValue Array(IEnumerable<TracingValue>? values)
        => values is null ? Undefined : new TracingValue(Kind.Array, reference: values.ToArray());

    /// <summary>An array value of the given elements.</summary>
    public static TracingValue Array(params TracingValue[] values) => new(Kind.Array, reference: values);

    public static implicit operator TracingValue(string? value) => From(value);

    public static implicit operator TracingValue(double? value) => From(value);

    public static implicit operator TracingValue(bool? value) => From(value);

    public static implicit operator TracingValue(TracingArgs? value) => From(value);

    /// <summary>What this value is.</summary>
    internal Kind ValueKind => _kind;

    /// <summary>The value of a <see cref="Kind.Number"/>.</summary>
    internal double Number => _number;

    /// <summary>The value of a <see cref="Kind.Boolean"/>, <see cref="Kind.String"/>, <see cref="Kind.Object"/> or
    /// <see cref="Kind.Array"/>.</summary>
    internal object? Reference => _reference;
}

// NOT ported from TS — it has no counterpart there.
/// <summary>
/// Writes the tracing subsystem's values as JSON, standing in for the engine's <c>JSON.stringify</c>.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS gets this writer from the JavaScript engine. The port has no port of <c>JSON.stringify</c> yet (the
/// one the test harness needs arrives with its own scope), so the writer lives here with the two behaviours the trace
/// files depend on: members are written in the order they were added, and a member whose value is <c>undefined</c> is
/// omitted while an <c>undefined</c> array element is written as <c>null</c>. Numbers are rendered by
/// <see cref="Js.NumberToString"/>, with <c>JSON.stringify</c>'s rule for a non-finite one (written as <c>null</c>).
/// What it does not do is escape non-ASCII (neither does <c>JSON.stringify</c>) or lone surrogates (which the engine's
/// well-formed mode would).
/// </remarks>
internal static class TracingJson
{
    /// <summary>Serializes an argument bag — TS's <c>JSON.stringify(args)</c>.</summary>
    public static string Serialize(TracingArgs args) => Serialize(TracingValue.From(args));

    /// <summary>Serializes a value — TS's <c>JSON.stringify(value)</c>.</summary>
    public static string Serialize(TracingValue value)
    {
        Debug.Assert(value.ValueKind != TracingValue.Kind.Undefined, "JSON.stringify(undefined) is not a string");
        var builder = new StringBuilder();
        Append(builder, value);
        return builder.ToString();
    }

    /// <summary>
    /// Writes one value. An <c>undefined</c> member is dropped by the object writer; an
    /// <c>undefined</c> array element is written as <c>null</c>, which is what <c>JSON.stringify</c> does.
    /// </summary>
    private static void Append(StringBuilder builder, TracingValue value, bool asArrayElement = false)
    {
        switch (value.ValueKind)
        {
            case TracingValue.Kind.Undefined:
                if (asArrayElement)
                {
                    builder.Append("null");
                }

                break;
            case TracingValue.Kind.Null:
                builder.Append("null");
                break;
            case TracingValue.Kind.Boolean:
                builder.Append((bool)value.Reference! ? "true" : "false");
                break;
            case TracingValue.Kind.Number:
                builder.Append(double.IsFinite(value.Number) ? Js.NumberToString(value.Number) : "null");
                break;
            case TracingValue.Kind.String:
                AppendString(builder, (string)value.Reference!);
                break;
            case TracingValue.Kind.Object:
                AppendObject(builder, (TracingArgs)value.Reference!);
                break;
            case TracingValue.Kind.Array:
                AppendArray(builder, (IReadOnlyList<TracingValue>)value.Reference!);
                break;
            default:
                Debug.AssertNever(value.ValueKind, "Unknown tracing value:");
                break;
        }
    }

    /// <summary>Writes an argument bag: <c>{"key":value,…}</c>, with an <c>undefined</c> member left out.</summary>
    private static void AppendObject(StringBuilder builder, TracingArgs args)
    {
        builder.Append('{');
        var isFirst = true;
        foreach (var (key, value) in args.Entries)
        {
            if (value.ValueKind == TracingValue.Kind.Undefined)
            {
                continue;
            }

            if (!isFirst)
            {
                builder.Append(',');
            }

            isFirst = false;
            AppendString(builder, key);
            builder.Append(':');
            Append(builder, value);
        }

        builder.Append('}');
    }

    /// <summary>Writes an array: <c>[value,…]</c>, with an <c>undefined</c> element written as <c>null</c>.</summary>
    private static void AppendArray(StringBuilder builder, IReadOnlyList<TracingValue> values)
    {
        builder.Append('[');
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            Append(builder, values[i], asArrayElement: true);
        }

        builder.Append(']');
    }

    /// <summary>Writes a quoted string with the escapes <c>JSON.stringify</c> produces.</summary>
    private static void AppendString(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                default:
                    if (ch < ' ')
                    {
                        builder.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(ch);
                    }

                    break;
            }
        }

        builder.Append('"');
    }
}