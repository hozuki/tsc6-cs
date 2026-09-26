namespace TypeScript.Compiler.Tests;

/// <summary>
/// A node the factory builds is synthesized — its text range is (-1, -1) — until the parser or a transform gives
/// it one.
/// </summary>
/// <remarks>
/// TS gets this from the base factory's constructor call (<c>new Node(kind, /*pos*/ -1, /*end*/ -1)</c> in
/// <c>factory/baseNodeFactory.ts</c>), and it is read back through <c>nodeIsSynthesized</c> by the parenthesizer
/// (via <c>isIgnorableParen</c>), by the emitter's source-map ranges, and by <c>cloneNode</c>, which resets the
/// range because the fresh node it starts from owns one.
/// </remarks>
public class NodeFactoryRangeTests
{
    [Fact]
    public void FactoryNode_StartsSynthesized()
    {
        var identifier = new NodeFactory().CreateIdentifier("x");

        Assert.Equal(-1, identifier.Pos);
        Assert.Equal(-1, identifier.End);
        Assert.True(Utilities.NodeIsSynthesized(identifier));
    }

    [Fact]
    public void DirectlyConstructedNode_StartsSynthesized()
    {
        // The range belongs to `Node` itself rather than to each factory method: TS's node constructor is what
        // carries it, and the port's factory builds through `new T()`.
        Assert.True(Utilities.NodeIsSynthesized(new Identifier()));
    }

    [Fact]
    public void SetTextRange_MakesASynthesizedNodeLookParsed()
    {
        var node = new NodeFactory().CreateIdentifier("x");

        Utilities.SetTextRange(node, new TextRange { Pos = 0, End = 1 });

        Assert.False(Utilities.NodeIsSynthesized(node));
    }
}
