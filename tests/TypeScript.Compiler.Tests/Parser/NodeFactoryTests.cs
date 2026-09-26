namespace TypeScript.Compiler.Tests;

/// <summary>
/// <see cref="NodeFactory.CreateNodeArray{T}"/> 的语义（factory/nodeFactory.ts:1169）：它既是构造入口，也是
/// <c>update*</c> 家族判断"有没有变化"的那一半，所以复用与标志聚合这两件事必须成立。
/// </summary>
public class NodeFactoryTests
{
    [Fact]
    public void CreateNodeArray_OfNothing_IsAnEmptyArrayWithNoRange()
    {
        var array = Factory().CreateNodeArray<Node>(null, null);

        Assert.Empty(array);
        Assert.Equal(-1, array.Pos);
        Assert.Equal(-1, array.End);
        Assert.False(array.HasTrailingComma);
    }

    [Fact]
    public void CreateNodeArray_KeepsTheTrailingCommaItWasAskedFor()
    {
        var array = Factory().CreateNodeArray(new List<Node> { Literal() }, hasTrailingComma: true);

        Assert.Single(array);
        Assert.True(array.HasTrailingComma);
    }

    [Fact]
    public void CreateNodeArray_ReturnsTheSameInstanceWhenNothingDiffers()
    {
        // This is what makes `update*` cheap: passing an existing NodeArray straight back must not allocate.
        var factory = Factory();
        var first = factory.CreateNodeArray(new List<Node> { Literal() }, null);

        Assert.Same(first, factory.CreateNodeArray(first, null));
        Assert.Same(first, factory.CreateNodeArray(first, false));
    }

    [Fact]
    public void CreateNodeArray_RebuildsWhenOnlyTheTrailingCommaDiffers()
    {
        var factory = Factory();
        var first = factory.CreateNodeArray(new List<Node> { Literal() }, hasTrailingComma: false);

        var rebuilt = factory.CreateNodeArray(first, hasTrailingComma: true);

        Assert.NotSame(first, rebuilt);
        Assert.True(rebuilt.HasTrailingComma);
        Assert.Same(first[0], rebuilt[0]);
        Assert.Equal(first.TransformFlags, rebuilt.TransformFlags);
        Assert.Equal(first.Pos, rebuilt.Pos);
    }

    [Fact]
    public void CreateNodeArray_AggregatesTheChildrensTransformFlags()
    {
        var array = Factory().CreateNodeArray(
            new List<Node> { new StringLiteral { Text = "x", TransformFlags = TransformFlags.ContainsES2015 } },
            null);

        Assert.Equal(TransformFlags.ContainsES2015, array.TransformFlags & TransformFlags.ContainsES2015);
    }

    [Fact]
    public void CreateNodeArray_DropsFlagsAChildStopsPropagating()
    {
        // A nested function body does not make the enclosing array contain a `yield` — `FunctionExcludes` is what
        // expresses that, and it is applied while aggregating.
        var array = Factory().CreateNodeArray(
            new List<Node> { new FunctionExpression { TransformFlags = TransformFlags.ContainsYield } },
            null);

        Assert.Equal(TransformFlags.None, array.TransformFlags & TransformFlags.ContainsYield);
    }

    [Fact]
    public void IsPropertyName_AnswersByKind()
    {
        Assert.True(Utilities.IsPropertyName(Literal()));
        Assert.True(Utilities.IsPropertyName(new Identifier()));
        Assert.True(Utilities.IsPropertyName(new ComputedPropertyName()));
        Assert.False(Utilities.IsPropertyName(new FunctionExpression()));
    }

    [Fact]
    public void SetTextRange_CopiesTheLocationWhenThereIsOne()
    {
        var node = new StringLiteral { Text = "x", Pos = 5, End = 9 };

        Utilities.SetTextRange(node, new TextRange { Pos = 1, End = 2 });
        Assert.Equal(1, node.Pos);
        Assert.Equal(2, node.End);

        Utilities.SetTextRange(node, null);
        Assert.Equal(1, node.Pos);
    }

    private static NodeFactory Factory() => new();

    private static StringLiteral Literal() => new() { Text = "x" };
}
