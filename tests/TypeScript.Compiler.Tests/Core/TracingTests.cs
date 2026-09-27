using System.Text.Json;

namespace TypeScript.Compiler.Tests;

/// <summary>
/// <c>tracing.ts</c>：追踪在显式开启前不存在（<see cref="Tracing.Current"/> 为 null），开启后写出的 trace /
/// types / legend 三个文件的形状与 TS 一致。
/// </summary>
/// <remarks>
/// 断言只针对结构：时间戳来自 <see cref="System.Diagnostics.Stopwatch"/>，绝对值与用例无关，故一条都不比。
/// 追踪状态是进程级静态的，每个用例都在 finally 里收尾（同 <see cref="PerformanceTests"/>）。
/// <para>
/// 所有用例共用同一个 <see cref="TraceDir"/>：<c>startTracing</c> 只在第一次记录 legend 路径（tracing.ts:73-75），
/// 换了目录就会写去第一个目录，TS 亦如此。
/// </para>
/// </remarks>
[Collection(ProcessWideStateCollection.Name)]
public class TracingTests
{
    private const string TraceDir = "trace";

    [Fact]
    public void TracingIsAbsentUntilStarted_AndClearedByStop()
    {
        var system = new RecordingSystem();
        try
        {
            Assert.Null(Tracing.Current);

            // 未开启时，宿主不会收到任何写入。
            Assert.Empty(system.Files);

            Tracing.StartTracing(Tracing.Mode.Server, TraceDir, system);
            Assert.NotNull(Tracing.Current);
            Assert.Contains(TraceDir, system.CreatedDirectories);

            var current = Tracing.Current!;
            current.Push(Tracing.Phase.Bind, "bindSourceFile", TracingArgs.Of(("path", "src/a.ts")), separateBeginAndEnd: true);
            current.Pop();
            current.StopTracing();

            Assert.Null(Tracing.Current);

            var text = WrittenFile(system, ModeFileName("trace", Tracing.Mode.Server));
            Assert.StartsWith("[\n", text);
            Assert.EndsWith("]\n", text);

            using var trace = JsonDocument.Parse(text);
            var events = trace.RootElement.EnumerateArray().ToArray();

            // 三个元数据记录（tracing.ts:97-104），第三个的 cat 被覆盖却仍排在其原位。
            Assert.Equal(5, events.Length);
            Assert.Equal("M", events[0].GetProperty("ph").GetString());
            Assert.Equal("__metadata", events[0].GetProperty("cat").GetString());
            Assert.Equal("process_name", events[0].GetProperty("name").GetString());
            Assert.Equal("tsc", events[0].GetProperty("args").GetProperty("name").GetString());
            Assert.Equal("thread_name", events[1].GetProperty("name").GetString());
            Assert.Equal("TracingStartedInBrowser", events[2].GetProperty("name").GetString());
            Assert.Equal("disabled-by-default-devtools.timeline", events[2].GetProperty("cat").GetString());
            foreach (var metadata in events[..3])
            {
                Assert.Equal(1, metadata.GetProperty("pid").GetInt32());
                Assert.Equal(1, metadata.GetProperty("tid").GetInt32());
                Assert.Equal(JsonValueKind.Number, metadata.GetProperty("ts").ValueKind);
            }

            // separateBeginAndEnd：push 写 B、pop 写 E，两者都带同一份 args。
            Assert.Equal("B", events[3].GetProperty("ph").GetString());
            Assert.Equal("bind", events[3].GetProperty("cat").GetString());
            Assert.Equal("bindSourceFile", events[3].GetProperty("name").GetString());
            Assert.Equal("src/a.ts", events[3].GetProperty("args").GetProperty("path").GetString());
            Assert.Equal("E", events[4].GetProperty("ph").GetString());
            Assert.Equal("bindSourceFile", events[4].GetProperty("name").GetString());
            Assert.Equal("src/a.ts", events[4].GetProperty("args").GetProperty("path").GetString());
            Assert.False(events[4].TryGetProperty("dur", out _));
        }
        finally
        {
            StopIfTracing();
        }
    }

    [Fact]
    public void PushPopThatOutlivesTheSampleInterval_WritesACompleteEvent()
    {
        var system = new RecordingSystem();
        try
        {
            Tracing.StartTracing(Tracing.Mode.Server, TraceDir, system);
            var current = Tracing.Current!;

            current.Push(Tracing.Phase.Check, "checkExpression", TracingArgs.Of(("kind", (double)SyntaxKind.Identifier), ("pos", 3)));
            // 采样间隔是 10ms（tracing.ts:172-182）：跨过一个采样点才会留下完整事件。
            Thread.Sleep(20);
            current.Pop(TracingArgs.Of(("variances", TracingValue.Array(1, 2))));
            current.StopTracing();

            using var trace = JsonDocument.Parse(WrittenFile(system, ModeFileName("trace", Tracing.Mode.Server)));
            var events = trace.RootElement.EnumerateArray().ToArray();

            Assert.Equal(4, events.Length);
            var completed = events[3];
            Assert.Equal("X", completed.GetProperty("ph").GetString());
            Assert.Equal("check", completed.GetProperty("cat").GetString());
            Assert.Equal("checkExpression", completed.GetProperty("name").GetString());
            Assert.Equal(3, completed.GetProperty("args").GetProperty("pos").GetInt32());
            Assert.True(completed.GetProperty("dur").GetDouble() > 0);
            // pop 的 results 被并入 args，排在原有成员之后。
            Assert.Equal(1, completed.GetProperty("args").GetProperty("results").GetProperty("variances")[0].GetInt32());
            Assert.Equal(2, completed.GetProperty("args").GetProperty("results").GetProperty("variances")[1].GetInt32());
        }
        finally
        {
            StopIfTracing();
        }
    }

    [Fact]
    public void InstantWritesAPointEvent_AndServerModeDropsCheckTypes()
    {
        var system = new RecordingSystem();
        try
        {
            Tracing.StartTracing(Tracing.Mode.Server, TraceDir, system);
            var current = Tracing.Current!;

            // server 模式没有地方放类型信息，checkTypes 事件被丢弃（tracing.ts:187-188）。
            current.Instant(Tracing.Phase.CheckTypes, "checkTypeRelatedTo_DepthLimit", TracingArgs.Of(("sourceId", 1), ("targetId", 2)));
            current.Instant(Tracing.Phase.Parse, "createSourceFile", TracingArgs.Of(("path", "src/a.ts")));
            current.StopTracing();

            using var trace = JsonDocument.Parse(WrittenFile(system, ModeFileName("trace", Tracing.Mode.Server)));
            var events = trace.RootElement.EnumerateArray().ToArray();

            Assert.Equal(4, events.Length);
            Assert.Equal("I", events[3].GetProperty("ph").GetString());
            Assert.Equal("parse", events[3].GetProperty("cat").GetString());
            Assert.Equal("createSourceFile", events[3].GetProperty("name").GetString());
            Assert.Equal("g", events[3].GetProperty("s").GetString());
            Assert.Equal("src/a.ts", events[3].GetProperty("args").GetProperty("path").GetString());
        }
        finally
        {
            StopIfTracing();
        }
    }

    [Fact]
    public void RecordTypeDumpsTheCatalogInProjectMode_AndIsDroppedInServerMode()
    {
        var system = new RecordingSystem();
        try
        {
            var type = new FakeType(TypeFlags.String, id: 7, new FakeSymbol("___proto__"));

            Tracing.StartTracing(Tracing.Mode.Server, TraceDir, system);
            Tracing.Current!.RecordType(type);
            Tracing.Current.StopTracing();
            Assert.False(system.Files.ContainsKey(TracePath(ModeFileName("types", Tracing.Mode.Server))));

            Tracing.StartTracing(Tracing.Mode.Project, TraceDir, system, configFilePath: "tsconfig.json");
            Tracing.Current!.RecordType(type);
            Tracing.Current.StopTracing();

            var typesText = WrittenFile(system, ModeFileName("types", Tracing.Mode.Project));
            using var types = JsonDocument.Parse(typesText);
            Assert.Equal(1, types.RootElement.GetArrayLength());
            // 成员按插入序，值为 undefined 的成员不写出；symbolName 是 unescapeLeadingUnderscores 之后的名字
            // （`___proto__` 逃逸出来的就是 `__proto__`）。
            Assert.Equal("""{"id":7,"symbolName":"__proto__","flags":["String"]}""", types.RootElement[0].GetRawText());

            Tracing.DumpLegend();
            using var legend = JsonDocument.Parse(WrittenFile(system, "legend.json"));
            var count = legend.RootElement.GetArrayLength();
            var server = legend.RootElement[count - 2];
            var project = legend.RootElement[count - 1];
            Assert.Equal(TracePath(ModeFileName("trace", Tracing.Mode.Server)), server.GetProperty("tracePath").GetString());
            Assert.False(server.TryGetProperty("typesPath", out _));
            Assert.False(server.TryGetProperty("configFilePath", out _));
            Assert.Equal("tsconfig.json", project.GetProperty("configFilePath").GetString());
            Assert.Equal(TracePath(ModeFileName("trace", Tracing.Mode.Project)), project.GetProperty("tracePath").GetString());
            Assert.Equal(TracePath(ModeFileName("types", Tracing.Mode.Project)), project.GetProperty("typesPath").GetString());
        }
        finally
        {
            StopIfTracing();
        }
    }

    [Fact]
    public void BuildModeNumbersTheTraceFilesAfterTheProcessId()
    {
        var system = new RecordingSystem();
        try
        {
            Tracing.StartTracing(Tracing.Mode.Build, TraceDir, system);
            Tracing.Current!.RecordType(new FakeType(TypeFlags.Number, id: 1));
            Tracing.Current.StopTracing();

            // build 模式的文件名是 `trace.${pid}-${n}.json`（tracing.ts:82-86），n 从 1 开始。
            Assert.Contains(TracePath($"trace.{Environment.ProcessId}-1.json"), system.Files.Keys);
            Assert.Contains(TracePath($"types.{Environment.ProcessId}-1.json"), system.Files.Keys);
            Assert.DoesNotContain(TracePath("trace.json"), system.Files.Keys);
        }
        finally
        {
            StopIfTracing();
        }
    }

    [Fact]
    public void DumpTypes_WritesReferencePropertiesAndSourceLocations()
    {
        var system = new RecordingSystem();
        try
        {
            var file = new SourceFile { Text = "const a = 1;\nconst b = 2;\n", Path = "src/a.ts", Pos = 0, End = 26 };
            var name = new Identifier { EscapedText = "a", Pos = 6, End = 7, Parent = file, Id = 42 };

            var reference = new FakeReferenceType(id: 3, ObjectFlags.Reference, name)
            {
                ResolvedTypeArguments = [new FakeType(TypeFlags.Number, id: 9)],
            };

            Tracing.StartTracing(Tracing.Mode.Project, TraceDir, system);
            Tracing.Current!.RecordType(reference);
            // `IndexType.type` 是 `IType?`，所以这里读到的是被索引类型自己的 id（tracing.ts:324）。
            Tracing.Current.RecordType(new FakeIndexType(id: 5, new FakeType(TypeFlags.String, id: 42)));
            Tracing.Current.StopTracing();

            using var types = JsonDocument.Parse(WrittenFile(system, ModeFileName("types", Tracing.Mode.Project)));
            var first = types.RootElement[0];
            Assert.Equal(3, first.GetProperty("id").GetInt32());
            Assert.False(first.TryGetProperty("instantiatedType", out _));
            Assert.Equal(9, first.GetProperty("typeArguments")[0].GetInt32());
            Assert.Equal("src/a.ts", first.GetProperty("referenceLocation").GetProperty("path").GetString());
            // getLocation 把行号列号都从 0 起改写为 1 起（tracing.ts:209-214）。
            Assert.Equal(1, first.GetProperty("referenceLocation").GetProperty("start").GetProperty("line").GetInt32());
            Assert.Equal(7, first.GetProperty("referenceLocation").GetProperty("start").GetProperty("character").GetInt32());
            Assert.Equal(8, first.GetProperty("referenceLocation").GetProperty("end").GetProperty("character").GetInt32());

            var second = types.RootElement[1];
            Assert.Equal(42, second.GetProperty("keyofType").GetInt32());
            // formatEnum 按成员值升序逐个比对原始 flags，且不因已经清掉某位而跳过同值的别名（debug.ts:466-479），
            // Index 与 IncludesNonWideningType 是同一位上的两个成员，所以两个名字都会出现。
            Assert.Equal(["Index", "IncludesNonWideningType"], second.GetProperty("flags").EnumerateArray().Select(flag => flag.GetString()!).ToArray());
        }
        finally
        {
            StopIfTracing();
        }
    }

    /// <summary>TS 的 <c>countPart</c>（tracing.ts:82-84）：只有 project 模式不带后缀。</summary>
    private static string ModeFileName(string stem, Tracing.Mode mode, int count = 1) => mode switch
    {
        Tracing.Mode.Build => $"{stem}.{Environment.ProcessId}-{count}.json",
        Tracing.Mode.Server => $"{stem}.{Environment.ProcessId}.json",
        _ => $"{stem}.json",
    };

    private static string TracePath(string fileName) => Paths.CombinePaths(TraceDir, fileName);

    private static string WrittenFile(RecordingSystem system, string fileName)
    {
        Assert.True(system.Files.TryGetValue(TracePath(fileName), out var text), $"nothing was written to {TracePath(fileName)}");
        return text!;
    }

    private static void StopIfTracing()
    {
        if (Tracing.Current is not null)
        {
            Tracing.Current.StopTracing();
        }
    }

    /// <summary>只记录写入内容与建过的目录的宿主。</summary>
    private sealed class RecordingSystem : ISystem
    {
        private readonly HashSet<string> _directories = new(StringComparer.Ordinal);

        public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

        public List<string> CreatedDirectories { get; } = [];

        public IList<string> Args { get; } = [];

        public string NewLine => "\n";

        public bool UseCaseSensitiveFileNames => true;

        public void Write(string s)
        {
        }

        public string? ReadFile(string path, string? encoding = null) => Files.GetValueOrDefault(path);

        public void WriteFile(string path, string data, bool writeByteOrderMark = false) => Files[path] = data;

        public string ResolvePath(string path) => path;

        public bool FileExists(string path) => Files.ContainsKey(path);

        public bool DirectoryExists(string path) => _directories.Contains(path);

        public void CreateDirectory(string path)
        {
            CreatedDirectories.Add(path);
            _directories.Add(path);
        }

        public string GetExecutingFilePath() => string.Empty;

        public string GetCurrentDirectory() => string.Empty;

        public IList<string> GetDirectories(string path) => [];

        public IList<string> ReadDirectory(
            string path,
            IList<string>? extensions = null,
            IList<string>? exclude = null,
            IList<string>? include = null,
            int? depth = null) => [];

        public void Exit(int exitCode = 0)
        {
        }

        public string? GetEnvironmentVariable(string name) => null;

        public DateTimeOffset? GetModifiedTime(string path) => null;
    }

    /// <summary>最小的 <see cref="IType"/>：只有 <c>dumpTypes</c> 读到的槽位。</summary>
    private sealed class FakeType : IType
    {
        public FakeType(TypeFlags flags, int id, ISymbol? symbol = null)
        {
            Flags = flags;
            Id = id;
            Symbol = symbol;
        }

        public TypeFlags Flags { get; set; }

        public int Id { get; set; }

        public ITypeChecker? Checker { get; set; }

        public ISymbol? Symbol { get; set; }

        public Node? Pattern { get; set; }

        public ISymbol? AliasSymbol { get; set; }

        public List<IType>? AliasTypeArguments { get; set; }

        public IType? PermissiveInstantiation { get; set; }

        public IType? RestrictiveInstantiation { get; set; }

        public IType? ImmediateBaseConstraint { get; set; }

        public IType? Widened { get; set; }
    }

    /// <summary>最小的 <see cref="ISymbol"/>：只有 <c>dumpTypes</c> 读到的槽位。</summary>
    private sealed class FakeSymbol : ISymbol
    {
        public FakeSymbol(string? escapedName)
        {
            EscapedName = escapedName;
        }

        public SymbolFlags Flags { get; set; }

        public string? EscapedName { get; set; }

        public List<IDeclaration>? Declarations { get; set; }

        public IDeclaration? ValueDeclaration { get; set; }

        public SymbolTable? Members { get; set; }

        public SymbolTable? Exports { get; set; }

        public SymbolTable? GlobalExports { get; set; }

        public int Id { get; set; }

        public int MergeId { get; set; }

        public ISymbol? Parent { get; set; }

        public ISymbol? ExportSymbol { get; set; }

        public bool? ConstEnumOnlyModule { get; set; }

        public SymbolFlags? IsReferenced { get; set; }

        public int? LastAssignmentPos { get; set; }

        public bool? IsReplaceableByMethod { get; set; }

        public Dictionary<int, IDeclaration>? AssignmentDeclarationMembers { get; set; }
    }

    /// <summary>最小的 <see cref="ITypeReference"/>：<c>dumpTypes</c> 的 reference 分支读到的槽位。</summary>
    private sealed class FakeReferenceType : IType, IObjectType, ITypeReference
    {
        public FakeReferenceType(int id, ObjectFlags objectFlags, Node? node)
        {
            Id = id;
            Flags = TypeFlags.Object;
            ObjectFlags = objectFlags;
            Node = node;
        }

        public TypeFlags Flags { get; set; }

        public int Id { get; set; }

        public ITypeChecker? Checker { get; set; }

        public ISymbol? Symbol { get; set; }

        public Node? Pattern { get; set; }

        public ISymbol? AliasSymbol { get; set; }

        public List<IType>? AliasTypeArguments { get; set; }

        public IType? PermissiveInstantiation { get; set; }

        public IType? RestrictiveInstantiation { get; set; }

        public IType? ImmediateBaseConstraint { get; set; }

        public IType? Widened { get; set; }

        public ObjectFlags ObjectFlags { get; set; }

        public SymbolTable? Members { get; set; }

        public List<ISymbol>? Properties { get; set; }

        public List<ISignature>? CallSignatures { get; set; }

        public List<ISignature>? ConstructSignatures { get; set; }

        public List<IIndexInfo>? IndexInfos { get; set; }

        public IObjectType? ObjectTypeWithoutAbstractConstructSignatures { get; set; }

        public IGenericType? Target { get; set; }

        public Node? Node { get; set; }

        public object? Mapper { get; set; }

        public List<IType>? ResolvedTypeArguments { get; set; }

        public ITypeReference? LiteralType { get; set; }

        public IType? CachedEquivalentBaseType { get; set; }
    }

    /// <summary>最小的 <see cref="IIndexType"/>：<c>dumpTypes</c> 的 keyof 分支读到的槽位。</summary>
    private sealed class FakeIndexType : IType, IIndexType
    {
        public FakeIndexType(int id, IType type)
        {
            Id = id;
            Type = type;
        }

        public TypeFlags Flags { get; set; } = TypeFlags.Index;

        public int Id { get; set; }

        public ITypeChecker? Checker { get; set; }

        public ISymbol? Symbol { get; set; }

        public Node? Pattern { get; set; }

        public ISymbol? AliasSymbol { get; set; }

        public List<IType>? AliasTypeArguments { get; set; }

        public IType? PermissiveInstantiation { get; set; }

        public IType? RestrictiveInstantiation { get; set; }

        public IType? ImmediateBaseConstraint { get; set; }

        public IType? Widened { get; set; }

        public IType? Type { get; set; }

        public IndexFlags IndexFlags { get; set; }

        public IType? ResolvedBaseConstraint { get; set; }

        public IIndexType? ResolvedIndexType { get; set; }

        public IIndexType? ResolvedStringIndexType { get; set; }
    }
}
