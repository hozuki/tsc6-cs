namespace TypeScript.Compiler.Tests;

/// <summary>
/// “没改动就返回入参本体”的那几个数组工具（core.ts 里同样的优化）。
/// </summary>
/// <remarks>
/// TS 的形参写的是 <c>readonly T[]</c>，运行时就是可变的 <c>Array</c>；移植版把两侧都放宽成
/// <see cref="IList{T}"/>——JS <c>Array</c> 在 C# 里的对应物——所以那一份实例可以真的交回去
/// （docs/architecture.md §10.2）。
/// </remarks>
public class ArraySharingTests
{
    [Fact]
    public void Filter_ReturnsTheInputWhenNothingIsRemoved()
    {
        IList<string> input = new List<string> { "a", "b" };

        Assert.Same(input, Core.Filter(input, static _ => true));
        Assert.NotSame(input, Core.Filter(input, static x => x == "a"));
        Assert.Null(Core.Filter<string>(null, static _ => true));
    }

    [Fact]
    public void Filter_AcceptsPlainArraysAsWell()
    {
        // 数组实现了 IList<T>，与 TS 里 filter 直接收数组一致。
        string[] input = ["a", "bb"];

        Assert.Equal(["bb"], Core.Filter(input, static x => x.Length == 2));
    }

    [Fact]
    public void SameMap_ReturnsTheInputWhenNothingChanges()
    {
        IList<string> input = new List<string> { "a", "b" };

        Assert.Same(input, Core.SameMap(input, static (x, _) => x));
    }

    [Fact]
    public void Compact_ReturnsTheInputWhenNothingIsFalsy()
    {
        IList<string> input = new List<string> { "a", "b" };

        Assert.Same(input, Core.Compact(input));
    }

    [Fact]
    public void Concatenate_ReturnsAnInputWhenTheOtherSideIsEmpty()
    {
        IList<string> first = new List<string> { "a" };
        IList<string> second = new List<string> { "b" };

        Assert.Same(first, Core.Concatenate(first, new List<string>()));
        Assert.Same(first, Core.Concatenate(first, null));
        Assert.Same(second, Core.Concatenate(null, second));

        var both = Core.Concatenate(first, second);
        Assert.NotSame(first, both);
        Assert.Equal(["a", "b"], both);
    }

    [Fact]
    public void AppendIfUnique_ReturnsTheInputItMutated()
    {
        IList<string> input = new List<string> { "a" };

        // TS pushes in place and returns the input either way.
        Assert.Same(input, Core.AppendIfUnique(input, "a"));
        Assert.Same(input, Core.AppendIfUnique(input, "b"));
        Assert.Equal(["a", "b"], input);

        // With no input there is nothing to share, so the result is fresh.
        var fresh = Core.AppendIfUnique<string>(null, "c");
        Assert.Equal(["c"], fresh);
    }
}
