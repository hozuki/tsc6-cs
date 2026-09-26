namespace TypeScript.Compiler.Tests;

// 对应 src/compiler/core.ts 的数组/迭代工具（121-1530）
public class CoreArraysTests
{
    /// <summary>
    /// TS: <c>compareValues(a: number | undefined, b: number | undefined)</c>. The port models
    /// numbers as <c>int?</c>, so adapters are needed where a <c>Comparer&lt;int&gt;</c> is expected.
    /// </summary>
    private static Comparison CompareInts(int a, int b) => Core.CompareValues(a, b);

    [Fact]
    public void Length_TreatsNullAsEmpty()
    {
        Assert.Equal(0, Core.Length<int>(null));
        Assert.Equal(3, Core.Length([1, 2, 3]));
    }

    [Fact]
    public void ForEach_StopsAtFirstTruthyResult()
    {
        // TS 语义：回调返回 truthy 即停止并返回该值。
        var seen = new List<int>();
        var result = Core.ForEach(new[] { 1, 2, 3, 4 }, (x, _) =>
        {
            seen.Add(x);
            return x > 2 ? $"found{x}" : null;
        });

        Assert.Equal("found3", result);
        Assert.Equal([1, 2, 3], seen);
    }

    [Fact]
    public void Filter_KeepsMatchingElementsInOrder()
    {
        Assert.Equal([1, 3], Core.Filter(new[] { 1, 2, 3, 4 }, static x => x % 2 == 1));
        Assert.Null(Core.Filter<int>(null, static _ => true));
    }

    [Fact]
    public void Filter_AllMatch_ReturnsEquivalentContents()
    {
        // TS 在此返回同一实例；C# 因返回类型不同而返回副本，内容必须一致。
        var source = new List<int> { 1, 2, 3 };
        var result = Core.Filter(source, static _ => true);

        Assert.Equal(source, result);
    }

    [Fact]
    public void MapDefined_DropsNullResults()
    {
        var result = Core.MapDefined(new[] { "a", "bb", "ccc" }, static (s, _) => s.Length == 2 ? null : s);
        Assert.Equal(["a", "ccc"], result);
    }

    [Fact]
    public void Contains_UsesSuppliedComparer()
    {
        Assert.True(Core.Contains(["Foo", "Bar"], "foo", Core.EquateStringsCaseInsensitive));
        Assert.False(Core.Contains(["Foo", "Bar"], "foo"));
        Assert.True(Core.Contains(["Foo", "Bar"], "Foo"));
    }

    [Fact]
    public void PushIfUnique_ReportsWhetherAdded()
    {
        var list = new List<string> { "a" };
        Assert.False(Core.PushIfUnique(list, "a"));
        Assert.True(Core.PushIfUnique(list, "b"));
        Assert.Equal(["a", "b"], list);
    }

    [Fact]
    public void Concatenate_ReusesOperandsWhenOneIsEmpty()
    {
        var a = new List<int> { 1 };
        var b = new List<int> { 2, 3 };

        Assert.Same(a, Core.Concatenate(a, null));
        Assert.Same(b, Core.Concatenate(null, b));
        Assert.Same(a, Core.Concatenate(a, []));
        Assert.Equal([1, 2, 3], Core.Concatenate(a, b));
    }

    [Fact]
    public void StableSort_KeepsRelativeOrderOfEqualElements()
    {
        // TS 依赖 Array.prototype.sort 的稳定性（ES2019 起）。C# 的 List.Sort 不稳定，
        // 所以移植版显式稳定化：这里验证等键元素保持原索引顺序。
        var items = new List<(string Key, int Index)>
        {
            ("b", 0), ("a", 1), ("b", 2), ("a", 3), ("b", 4),
        };

        var sorted = Core.ToSorted(items, static (x, y) => Core.CompareStringsCaseSensitive(x.Key, y.Key));

        Assert.Equal(["a", "a", "b", "b", "b"], sorted.Select(i => i.Key));
        Assert.Equal([1, 3, 0, 2, 4], sorted.Select(i => i.Index));
    }

    [Fact]
    public void SortAndDeduplicate_RemovesAdjacentDuplicates()
    {
        var result = Core.SortAndDeduplicate(["b", "a", "b", "c", "a"]);
        Assert.Equal(["a", "b", "c"], result);
    }

    [Fact]
    public void Deduplicate_WithComparer_RestoresOriginalOrder()
    {
        // TS 的 deduplicateRelational：稳定排序 -> 去重 -> 恢复原始顺序。
        var result = Core.Deduplicate(
            new List<string> { "b", "a", "B", "A" },
            Core.EquateStringsCaseInsensitive,
            Core.CompareStringsCaseInsensitive);

        Assert.Equal(["b", "a"], result);
    }

    [Fact]
    public void Deduplicate_WithoutComparer_KeepsFirstOccurrenceOrder()
    {
        var result = Core.Deduplicate(new List<string> { "b", "a", "b" }, Core.EquateStringsCaseSensitive);
        Assert.Equal(["b", "a"], result);
    }

    [Fact]
    public void BinarySearchKey_ReturnsComplementWhenMissing()
    {
        var array = new List<int> { 10, 20, 30, 40 };

        Assert.Equal(2, Core.BinarySearchKey(array, 30, static (v, _) => v, CompareInts));
        // 25 应插入到 30 的左侧（索引 2），未命中时返回 ~2
        Assert.Equal(~2, Core.BinarySearchKey(array, 25, static (v, _) => v, CompareInts));
        Assert.Equal(-1, Core.BinarySearchKey(new List<int>(), 1, static (v, _) => v, CompareInts));
    }

    [Fact]
    public void InsertSorted_ReplacesWhenEqualityComparerMatches()
    {
        var array = new List<string> { "a", "c" };
        Assert.True(Core.InsertSorted(array, "b", Core.CompareStringsCaseSensitive));
        Assert.Equal(["a", "b", "c"], array);

        // 插入位置的下一个元素可大小写不敏感地匹配：替换该位置
        var swap = new List<string> { "B", "d" };
        Assert.True(Core.InsertSorted(swap, "D", Core.CompareStringsCaseSensitive, Core.EquateStringsCaseInsensitive));
        Assert.Equal(["B", "D"], swap);

        // 插入位置的前一个元素即已相等：TS 视为重复，返回 false 且不修改数组
        var replacing = new List<string> { "A", "c" };
        Assert.False(Core.InsertSorted(replacing, "a", Core.CompareStringsCaseSensitive, Core.EquateStringsCaseInsensitive));
        Assert.Equal(["A", "c"], replacing);

        // 允许重复：插入
        var withDuplicates = new List<string> { "a" };
        Assert.True(Core.InsertSorted(withDuplicates, "a", Core.CompareStringsCaseSensitive, null, allowDuplicates: true));
        Assert.Equal(["a", "a"], withDuplicates);

        // 不允许重复且相等：不插入
        var unique = new List<string> { "a" };
        Assert.False(Core.InsertSorted(unique, "a", Core.CompareStringsCaseSensitive, Core.EquateStringsCaseSensitive));
        Assert.Equal(["a"], unique);
    }

    [Fact]
    public void RelativeComplement_ReturnsElementsOnlyInB()
    {
        var a = new List<int> { 1, 3, 5 };
        var b = new List<int> { 1, 2, 3, 4, 5, 6 };

        // PORT-NOTE: 这是 TS 实现的可观察行为，不是笔误。内层循环一旦耗尽 arrayA 就结束本轮
        // offsetB，因此“大于 arrayA 全部剩余元素”的 arrayB 元素不会被加入结果（6 丢失）。
        // 上游调用方总是传入“arrayA 为 arrayB 子集且覆盖其最大值”的数组，所以该分支不会被触发；
        // 移植版按“行为等价”原则保留该语义。
        Assert.Equal([2, 4], Core.RelativeComplement(a, b, CompareInts));
        Assert.Equal([2, 4, 6], Core.RelativeComplement(new List<int> { 1, 3, 5, 7 }, b, CompareInts));
        Assert.Null(Core.RelativeComplement<int>(null, null, CompareInts));
    }

    [Fact]
    public void SpanMap_GroupsContiguousRuns()
    {
        var input = new[] { "a1", "a2", "b1", "a3" };
        var result = Core.SpanMap(
            input,
            static (s, _) => s[0],
            static (chunk, key, start, end) => $"{key}:{chunk.Count}@{start}-{end}");

        // "a1","a2" 连续同键；"b1" 单独；"a3" 单独
        Assert.Equal(["a:2@0-2", "b:1@2-3", "a:1@3-4"], result);
    }

    [Fact]
    public void EnumerateInsertsAndDeletes_ReportsChanges()
    {
        var inserted = new List<int>();
        var deleted = new List<int>();

        var noChanges = Core.EnumerateInsertsAndDeletes(
            [1, 2, 3], [1, 2, 3],
            CompareInts, inserted.Add, deleted.Add);
        Assert.False(noChanges);
        Assert.Empty(inserted);
        Assert.Empty(deleted);

        var changed = Core.EnumerateInsertsAndDeletes(
            [1, 3], [1, 2],
            CompareInts, inserted.Add, deleted.Add);
        Assert.True(changed);
        Assert.Equal([3], inserted);
        Assert.Equal([2], deleted);
    }

    [Fact]
    public void CartesianProduct_MatchesExpectedOrder()
    {
        var arrays = new List<IList<int>>
        {
            new List<int> { 1, 2 },
            new List<int> { 7, 8 },
            new List<int> { 9 },
        };

        var result = Core.CartesianProduct(arrays);

        Assert.Equal(
            [
                new List<int> { 1, 7, 9 },
                new List<int> { 1, 8, 9 },
                new List<int> { 2, 7, 9 },
                new List<int> { 2, 8, 9 },
            ],
            result);
    }

    [Fact]
    public void TakeWhileAndSkipWhile_SplitAtFirstFailure()
    {
        var input = new[] { 1, 2, 3, 1 };

        Assert.Equal([1, 2], Core.TakeWhile(input, static x => x < 3));
        Assert.Equal([3, 1], Core.SkipWhile(input, static x => x < 3));
        Assert.Null(Core.TakeWhile<int>(null, static _ => true));
        Assert.Null(Core.SkipWhile<int>(null, static _ => true));
    }

    [Fact]
    public void ReplaceElement_DoesNotMutateSource()
    {
        var source = new List<string> { "a", "b" };
        var result = Core.ReplaceElement(source, 1, "c");

        Assert.Equal(["a", "b"], source);
        Assert.Equal(["a", "c"], result);
    }

    [Fact]
    public void OrderedAndUnorderedRemoveItem_RespectOrdering()
    {
        var ordered = new List<string> { "a", "b", "c" };
        Assert.True(Core.OrderedRemoveItem(ordered, "b"));
        Assert.Equal(["a", "c"], ordered);

        var unordered = new List<string> { "a", "b", "c" };
        Assert.True(Core.UnorderedRemoveItem(unordered, "b"));
        Assert.Equal(["a", "c"], unordered);
        Assert.False(Core.OrderedRemoveItem(ordered, "zzz"));
    }

    [Fact]
    public void ElementAt_SupportsNegativeOffsets()
    {
        var array = new[] { "a", "b", "c" };
        Assert.Equal("a", Core.ElementAt(array, 0));
        Assert.Equal("c", Core.ElementAt(array, -1));
        Assert.Null(Core.ElementAt(array, 5));
    }

    [Fact]
    public void GroupByAndArrayToMultiMap_PreserveElements()
    {
        var grouped = Core.GroupBy(new[] { "apple", "avocado", "banana" }, s => s[0]);
        Assert.Equal(["apple", "avocado"], grouped["a"]);
        Assert.Equal(["banana"], grouped["b"]);

        var multiMap = Core.ArrayToMultiMap(new[] { "apple", "avocado", "banana" }, s => s[0], s => s.Length);
        Assert.Equal([5, 7], multiMap.Get('a'));
        Assert.True(multiMap.ContainsKey('b'));
    }

    [Fact]
    public void FlatMap_DropsFalsyMappedValues()
    {
        // TS: flatMap(array, mapfn) keeps a mapped value only when it is truthy, so "" disappears
        // (the wildcard specs rely on this for empty specs).
        var mapped = Core.FlatMap<string, string>(["", "a", "b"], (s, _) => s);

        Assert.Equal(["a", "b"], mapped);
    }
}
