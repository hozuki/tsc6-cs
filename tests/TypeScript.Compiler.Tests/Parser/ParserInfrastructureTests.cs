namespace TypeScript.Compiler.Tests;

/// <summary>
/// Acceptance for the Layer 3 infrastructure: <see cref="Utilities"/>, <see cref="EmitNodes"/> and
/// <see cref="NodeChildren"/>.
/// </summary>
public class ParserInfrastructureTests
{
    // -----------------------------------------------------------------------------------------------
    // Utilities (parse-tree predicates)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public void IsParseTreeNode_DistinguishesParsedFromSynthesized()
    {
        var parsed = new Identifier();
        Assert.True(Utilities.IsParseTreeNode(parsed));

        var synthesized = new Identifier { Flags = NodeFlags.Synthesized };
        Assert.False(Utilities.IsParseTreeNode(synthesized));
    }

    /// <summary>
    /// TS returns the node itself in the early exit <em>without</em> applying the test, and applies it only to
    /// nodes found while walking <c>original</c> links.
    /// </summary>
    [Fact]
    public void GetParseTreeNode_AppliesTheTestOnlyToOriginalLinks()
    {
        var parsed = new Identifier();
        Assert.Same(parsed, Utilities.GetParseTreeNode(parsed, _ => false));

        var synthesized = new Identifier { Flags = NodeFlags.Synthesized };
        var original = new Identifier();
        synthesized.Original = original;
        Assert.Same(original, Utilities.GetParseTreeNode(synthesized));
        Assert.Same(original, Utilities.GetParseTreeNode(synthesized, _ => true));
        Assert.Null(Utilities.GetParseTreeNode(synthesized, _ => false));
    }

    [Fact]
    public void GetParseTreeNode_ReturnsNullWhenNoParseTreeAncestorExists()
    {
        var synthesized = new Identifier { Flags = NodeFlags.Synthesized };
        Assert.Null(Utilities.GetParseTreeNode(synthesized));
        Assert.Null(Utilities.GetParseTreeNode(null));
    }

    [Fact]
    public void GetParseTreeNode_StopsAtTheFirstParseTreeNode()
    {
        var oldest = new Identifier();
        var middle = new Identifier { Flags = NodeFlags.Synthesized, Original = oldest };
        var newest = new Identifier { Flags = NodeFlags.Synthesized, Original = middle };

        // `middle` is still synthesized, so the walk continues to `oldest`.
        Assert.Same(oldest, Utilities.GetParseTreeNode(newest));
    }

    [Fact]
    public void GetSourceFileOfNode_IsNullWithoutANodeChain()
    {
        Assert.Null(Utilities.GetSourceFileOfNode(new Identifier()));
        Assert.Null(Utilities.GetSourceFileOfNode(null));
    }

    // -----------------------------------------------------------------------------------------------
    // EmitNode
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public void GetOrCreateEmitNode_CreatesOnceThenReuses()
    {
        var identifier = new Identifier();
        Assert.Null(identifier.EmitNode);

        var created = EmitNodes.GetOrCreateEmitNode(identifier);
        Assert.Same(created, identifier.EmitNode);
        Assert.Same(created, EmitNodes.GetOrCreateEmitNode(identifier));
    }

    [Fact]
    public void EmitFlags_AreSetAndAccumulated()
    {
        var identifier = new Identifier();

        EmitNodes.SetEmitFlags(identifier, EmitFlags.NoComments);
        Assert.Equal(EmitFlags.NoComments, EmitNodes.GetOrCreateEmitNode(identifier).Flags);

        EmitNodes.AddEmitFlags(identifier, EmitFlags.NoNestedComments);
        Assert.Equal(
            EmitFlags.NoComments | EmitFlags.NoNestedComments,
            EmitNodes.GetOrCreateEmitNode(identifier).Flags);

        EmitNodes.RemoveAllComments(identifier);
        Assert.True(EmitNodes.GetOrCreateEmitNode(identifier).Flags.HasFlag(EmitFlags.NoComments));
    }

    /// <summary>
    /// A node that has been handed to an emitter is immutable; the check lives in <c>getOrCreateEmitNode</c>, so it
    /// only fires once an <c>EmitNode</c> already exists.
    /// </summary>
    [Fact]
    public void GetOrCreateEmitNode_RejectsMutationOfImmutableNodes()
    {
        var identifier = new Identifier();
        var emitNode = EmitNodes.GetOrCreateEmitNode(identifier);
        emitNode.InternalFlags |= InternalEmitFlags.Immutable;

        var previous = Debug.CurrentAssertionLevel;
        Debug.SetAssertionLevel(AssertionLevel.Normal);
        try
        {
            Assert.Throws<InvalidOperationException>(() => EmitNodes.GetOrCreateEmitNode(identifier));
        }
        finally
        {
            Debug.SetAssertionLevel(previous);
        }
    }

    [Fact]
    public void SyntheticComments_AccumulateWithAMinusOneExtent()
    {
        var identifier = new Identifier();
        EmitNodes.AddSyntheticLeadingComment(identifier, SyntaxKind.SingleLineCommentTrivia, "// one");
        EmitNodes.AddSyntheticLeadingComment(identifier, SyntaxKind.MultiLineCommentTrivia, "/* two */", hasTrailingNewLine: true);

        var comments = EmitNodes.GetSyntheticLeadingComments(identifier);
        Assert.NotNull(comments);
        Assert.Equal(2, comments!.Count);
        Assert.Equal(SyntaxKind.SingleLineCommentTrivia, comments[0].Kind);
        Assert.Equal(-1, comments[0].Pos);
        Assert.Equal(-1, comments[0].End);
        Assert.Equal("// one", comments[0].Text);
        Assert.False(comments[0].HasTrailingNewLine);
        Assert.Equal("/* two */", comments[1].Text);
        Assert.True(comments[1].HasTrailingNewLine);
    }

    [Fact]
    public void MoveSyntheticComments_LeavesTheOriginalBare()
    {
        var original = new Identifier();
        var target = new Identifier();
        EmitNodes.AddSyntheticLeadingComment(original, SyntaxKind.SingleLineCommentTrivia, "// moved");

        EmitNodes.MoveSyntheticComments(target, original);

        Assert.Null(EmitNodes.GetSyntheticLeadingComments(original));
        Assert.Equal("// moved", Assert.Single(EmitNodes.GetSyntheticLeadingComments(target)!).Text);
    }

    /// <summary>
    /// The custom source-map and comment ranges fall back to the node itself — the case that forced
    /// <c>Node</c> to implement <c>ITextRange</c>.
    /// </summary>
    [Fact]
    public void SourceMapAndCommentRanges_FallBackToTheNode()
    {
        var identifier = new Identifier { Pos = 3, End = 9 };

        Assert.Same(identifier, EmitNodes.GetSourceMapRange(identifier));
        Assert.Same(identifier, EmitNodes.GetCommentRange(identifier));

        var range = new TextRange { Pos = 1, End = 2 };
        EmitNodes.SetSourceMapRange(identifier, new SourceMapRange { Pos = 4, End = 5 });
        EmitNodes.SetCommentRange(identifier, range);

        Assert.Equal(4, EmitNodes.GetSourceMapRange(identifier).Pos);
        Assert.Same(range, EmitNodes.GetCommentRange(identifier));
    }

    [Fact]
    public void TokenSourceMapRanges_AreIndexedByToken()
    {
        var identifier = new Identifier();
        EmitNodes.SetTokenSourceMapRange(identifier, SyntaxKind.Identifier, new SourceMapRange { Pos = 7, End = 8 });

        // The list is padded with nulls up to the token's index, the way TS's sparse array has holes.
        Assert.Equal(8, EmitNodes.GetTokenSourceMapRange(identifier, SyntaxKind.Identifier)!.End);
        Assert.Null(EmitNodes.GetTokenSourceMapRange(identifier, SyntaxKind.EqualsToken));
    }

    [Fact]
    public void EmitHelpers_AreAddedUniquelyAndRemoved()
    {
        var identifier = new Identifier();
        var helper = new Identifier();

        EmitNodes.AddEmitHelper(identifier, helper);
        EmitNodes.AddEmitHelpers(identifier, [helper]);
        Assert.Same(helper, Assert.Single(EmitNodes.GetEmitHelpers(identifier)!));

        Assert.True(EmitNodes.RemoveEmitHelper(identifier, helper));
        Assert.False(EmitNodes.RemoveEmitHelper(identifier, helper));

        // Removal leaves an empty list, not null — the EmitNode itself stays.
        Assert.Empty(EmitNodes.GetEmitHelpers(identifier)!);
    }

    // -----------------------------------------------------------------------------------------------
    // NodeChildren
    // -----------------------------------------------------------------------------------------------

    /// <summary>
    /// The test is <c>kind &gt;= FirstNode</c>, which is not the same as "is a node": the literal kinds are declared
    /// below <c>FirstNode</c> (TS numbers <c>Identifier</c> 80 and puts <c>FirstNode</c> at 168). That is deliberate —
    /// identifiers and literals never have children, so they never reach the source-file table.
    /// </summary>
    [Fact]
    public void IsNodeKind_MatchesFirstNodeAndAbove()
    {
        Assert.True(NodeChildren.IsNodeKind(SyntaxKind.FirstNode));
        Assert.True(NodeChildren.IsNodeKind(SyntaxKind.SyntaxList));
        Assert.True(NodeChildren.IsNodeKind(SyntaxKind.Block));

        Assert.False(NodeChildren.IsNodeKind(SyntaxKind.Identifier));
        Assert.False(NodeChildren.IsNodeKind(SyntaxKind.ConstKeyword));
        Assert.False(NodeChildren.IsNodeKind(SyntaxKind.WhitespaceTrivia));
        Assert.False(NodeChildren.IsNodeKind(SyntaxKind.Unknown));
    }

    /// <summary>
    /// A syntax list holds its children on the node itself, so it never consults the source-file table (which is why
    /// the null source file here is harmless).
    /// </summary>
    [Fact]
    public void SyntaxList_HoldsItsChildrenDirectly()
    {
        var list = new SyntaxList { _children = [new Identifier()] };
        Assert.Same(list._children, NodeChildren.GetNodeChildren(list, null!));
    }

    [Fact]
    public void NodesBelowFirstNode_ReportNoChildren()
    {
        Assert.Empty(NodeChildren.GetNodeChildren(new KeywordToken(SyntaxKind.ConstKeyword), null!)!);
    }
}
