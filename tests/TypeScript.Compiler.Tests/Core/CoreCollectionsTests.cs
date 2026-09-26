namespace TypeScript.Compiler.Tests;

// 对应 src/compiler/core.ts:1527-1743 的容器类型
public class CoreCollectionsTests
{
    [Fact]
    public void MultiMap_AddReturnsBackingList()
    {
        var map = Core.CreateMultiMap<string, int>();

        var values = map.Add("a", 1);
        Assert.Same(values, map.Add("a", 2));

        Assert.Equal([1, 2], map.Get("a"));
        Assert.Null(map.Get("b"));
        Assert.Equal(1, map.Count);
    }

    [Fact]
    public void MultiMap_RemoveDropsEmptyBuckets()
    {
        var map = Core.CreateMultiMap<string, int>();
        map.Add("a", 1);
        map.Add("a", 2);

        map.Remove("a", 1);
        Assert.Equal([2], map.Get("a"));

        map.Remove("a", 2);
        Assert.Null(map.Get("a"));
        Assert.Equal(0, map.Count);

        // 移除不存在的键/值不应抛错
        map.Remove("missing", 1);
        Assert.Equal(0, map.Count);
    }

    [Fact]
    public void TsQueue_IsFifo()
    {
        var queue = Core.CreateQueue(new[] { 1, 2 });

        Assert.False(queue.IsEmpty);
        queue.Enqueue(3);
        queue.EnqueueRange([4, 5]);

        Assert.Equal(1, queue.Dequeue());
        Assert.Equal(2, queue.Dequeue());
        Assert.Equal([3, 4, 5], [queue.Dequeue(), queue.Dequeue(), queue.Dequeue()]);
        Assert.True(queue.IsEmpty);
    }

    [Fact]
    public void TsQueue_DequeueOnEmptyThrows()
    {
        var queue = Core.CreateQueue<int>();
        Assert.Throws<InvalidOperationException>(() => queue.Dequeue());
    }

    [Fact]
    public void CreateSet_UsesCustomEqualityAndHash()
    {
        // 忽略大小写、并按大写形式散列：与 TS createSet(getHashCode, equals) 的契约一致
        var set = Core.CreateSet<string, int>(
            static e => e.ToUpperInvariant().GetHashCode(),
            Core.EquateStringsCaseInsensitive);

        Assert.True(set.Add("Foo"));
        Assert.False(set.Add("FOO"));
        Assert.Contains("foo", set);
        Assert.DoesNotContain("bar", set);
        Assert.Single(set);
    }

    [Fact]
    public void EqualOwnProperties_ComparesContents()
    {
        var left = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        var right = new Dictionary<string, int> { ["b"] = 2, ["a"] = 1 };

        Assert.True(Core.EqualOwnProperties(left, right));
        Assert.False(Core.EqualOwnProperties(left, new Dictionary<string, int> { ["a"] = 1 }));
        Assert.False(Core.EqualOwnProperties(left, new Dictionary<string, int> { ["a"] = 1, ["b"] = 3 }));
        Assert.False(Core.EqualOwnProperties(left, null));
    }

    [Fact]
    public void MapLikeHelpers_ReadFromDictionary()
    {
        var map = new Dictionary<string, string> { ["a"] = "1" };

        Assert.True(Core.HasProperty(map, "a"));
        Assert.False(Core.HasProperty(map, "b"));
        Assert.Equal("1", Core.GetProperty(map, "a"));
        Assert.Null(Core.GetProperty(map, "b"));
        Assert.Equal(["a"], Core.GetOwnKeys(map));
        Assert.Equal(["1"], Core.GetOwnValues(map));
    }

    [Fact]
    public void Memoize_EvaluatesOnce()
    {
        var calls = 0;
        var memoized = Core.Memoize(() => ++calls);

        Assert.Equal(1, memoized());
        Assert.Equal(1, memoized());
        Assert.Equal(1, calls);
    }

    [Fact]
    public void MemoizeOne_CachesPerArgument()
    {
        var calls = 0;
        var memoized = Core.MemoizeOne<int, int>(x =>
        {
            calls++;
            return x * 2;
        });

        Assert.Equal(2, memoized(1));
        Assert.Equal(2, memoized(1));
        Assert.Equal(4, memoized(2));
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(false, false)]
    [InlineData(0, false)]
    [InlineData(0.0, false)]
    [InlineData(double.NaN, false)]
    [InlineData("", false)]
    [InlineData(true, true)]
    [InlineData(1, true)]
    [InlineData("0", true)]
    public void JsIsTruthy_MatchesJavaScript(object? value, bool expected)
    {
        Assert.Equal(expected, Js.IsTruthy(value));
    }

    [Fact]
    public void JsIsTruthy_EmptyCollectionIsStillTruthy()
    {
        // JS 中空数组/空对象都是 truthy；这一点与 C# 直觉不同，必须固定住。
        Assert.True(Js.IsTruthy(new List<int>()));
        Assert.True(Js.IsTruthy(new Dictionary<string, int>()));
    }

    [Fact]
    public void TypeTests_RecognizeArraysAndPrimitives()
    {
        Assert.True(Core.IsArray(new[] { 1 }));
        Assert.False(Core.IsArray("abc"));
        Assert.True(Core.IsString("abc"));
        Assert.False(Core.IsString(1));
        Assert.True(Core.IsNumber(1));
        Assert.True(Core.IsNumber(1.5));
        Assert.False(Core.IsNumber("1"));
    }

    [Fact]
    public void TryCastAndCast_UseSuppliedTest()
    {
        var value = Core.TryCast<string, string>("abc", static s => s.Length == 3);
        Assert.Equal("abc", value);
        Assert.Null(Core.TryCast<string, string>("abc", static s => s.Length == 4));

        Assert.Equal("abc", Core.Cast<string, string>("abc", static s => s.Length == 3));
        Assert.Throws<InvalidOperationException>(() => Core.Cast<string, string>("abc", static s => s.Length == 4));
    }

    [Fact]
    public void DebugAssertions_ThrowWithTsStyleMessages()
    {
        Debug.SetAssertionLevel(AssertionLevel.Normal);
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Debug.Assert(false, "boom"));
            Assert.Contains("False expression: boom", ex.Message);

            var equal = Assert.Throws<InvalidOperationException>(() => Debug.AssertEqual(1, 2));
            Assert.Contains("Expected 1 === 2", equal.Message);

            Assert.Throws<InvalidOperationException>(() => Debug.CheckDefined<string>(null));
        }
        finally
        {
            Debug.SetAssertionLevel(AssertionLevel.None);
        }
    }

    [Fact]
    public void DebugLogging_RespectsLevelAndHost()
    {
        var host = new RecordingLoggingHost();
        var originalLevel = Debug.CurrentLogLevel;
        Debug.LoggingHost = host;
        try
        {
            // TS 语义：shouldLog(level) === currentLogLevel <= level。
            // LogLevel 顺序为 Off(0) < Error(1) < Warning(2) < Info(3) < Verbose(4)，
            // 因此 currentLogLevel = Info 时只放行 Info 与 Verbose。
            Debug.CurrentLogLevel = LogLevel.Info;
            Debug.LogError("error");     // Error(1) < Info(3) -> 不记录
            Debug.LogWarning("warning"); // Warning(2) < Info(3) -> 不记录
            Debug.Log("info");
            Debug.LogTrace("trace");

            Assert.Equal(["info", "trace"], host.Messages);
        }
        finally
        {
            Debug.LoggingHost = null;
            Debug.CurrentLogLevel = originalLevel;
        }
    }

    private sealed class RecordingLoggingHost : ILoggingHost
    {
        public List<string> Messages { get; } = [];

        public void Log(LogLevel level, string s) => Messages.Add(s);
    }
}
