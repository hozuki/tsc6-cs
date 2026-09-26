namespace TypeScript.Compiler;

/// <summary>
/// The mutable store behind <see cref="Node.EmitNode"/>.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS declares <c>EmitNode</c> as an interface and everywhere builds it as an object literal
/// (<c>node.emitNode = {} as EmitNode</c>). The port needs a constructible type, so the generated interface keeps the
/// <c>I</c> prefix and this class is the thing that gets allocated — the same split as
/// <see cref="ITextRange"/>/<see cref="TextRange"/>.
/// </remarks>
internal sealed class EmitNode : IEmitNode
{
    public EmitFlags Flags { get; set; }

    public InternalEmitFlags InternalFlags { get; set; }

    public List<Node>? AnnotatedNodes { get; set; }

    public List<ISynthesizedComment>? LeadingComments { get; set; }

    public List<ISynthesizedComment>? TrailingComments { get; set; }

    public ITextRange? CommentRange { get; set; }

    public ISourceMapRange? SourceMapRange { get; set; }

    public List<ISourceMapRange>? TokenSourceMapRanges { get; set; }

    public object? ConstantValue { get; set; }

    public Identifier? ExternalHelpersModuleName { get; set; }

    public bool? ExternalHelpers { get; set; }

    public List<Node>? Helpers { get; set; }

    public bool? StartsOnNewLine { get; set; }

    public Node? SnippetElement { get; set; }

    public ITypeNode? TypeNode { get; set; }

    public Identifier? ClassThis { get; set; }

    public Expression? AssignedName { get; set; }

    public NodeArray<Node>? IdentifierTypeArguments { get; set; }

    public IAutoGenerateInfo? AutoGenerate { get; set; }

    public ImportSpecifier? GeneratedImportReference { get; set; }
}

// ported from: src/compiler/factory/emitNode.ts
/// <summary>
/// Transient per-node state used by transforms and emit — the emit flags, the synthetic comments, custom source-map
/// and comment ranges, and the helper list.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS's <c>helpers</c> is <c>EmitHelper[]</c>, a union of a helper descriptor and a node. The generated
/// <see cref="IEmitNode.Helpers"/> is <c>List&lt;Node&gt;</c> (union members with no common C# base collapse to
/// <c>Node</c>), so these entry points take <c>Node</c> too.
/// </remarks>
internal static class EmitNodes
{
    /// <summary>
    /// Associates a node with the current transformation, initializing its transient properties.
    /// </summary>
    public static IEmitNode GetOrCreateEmitNode(Node node)
    {
        if (node.EmitNode is null)
        {
            if (Utilities.IsParseTreeNode(node))
            {
                // To avoid holding onto transformation artifacts, track any parse tree node being annotated so the
                // annotations can be disposed once all transforms have run.
                if (node.Kind == SyntaxKind.SourceFile)
                {
                    return node.EmitNode = new EmitNode { AnnotatedNodes = [node] };
                }

                var sourceFile = Utilities.GetSourceFileOfNode(
                    Utilities.GetParseTreeNode(Utilities.GetSourceFileOfNode(node)));
                if (sourceFile is not null)
                {
                    GetOrCreateEmitNode(sourceFile).AnnotatedNodes!.Add(node);
                }
            }

            node.EmitNode = new EmitNode();
        }
        else
        {
            Debug.Assert(
                (node.EmitNode.InternalFlags & InternalEmitFlags.Immutable) == 0,
                "Invalid attempt to mutate an immutable node.");
        }

        return node.EmitNode;
    }

    /// <summary>
    /// Clears any emit-node entries from parse-tree nodes.
    /// </summary>
    /// <remarks>
    /// Parse-tree nodes outlive transform nodes, so the annotations put on them by <see cref="GetOrCreateEmitNode"/>
    /// are reclaimed here — partly to avoid holding whole subtrees alive for position information, and partly to
    /// reset the nodes for incremental parsing.
    /// </remarks>
    public static void DisposeEmitNodes(SourceFile? sourceFile)
    {
        var annotatedNodes = Utilities.GetSourceFileOfNode(Utilities.GetParseTreeNode(sourceFile))?.EmitNode?.AnnotatedNodes;
        if (annotatedNodes is not null)
        {
            foreach (var node in annotatedNodes)
            {
                node.EmitNode = null;
            }
        }
    }

    /// <summary>Sets <see cref="EmitFlags.NoComments"/> and drops any leading and trailing synthetic comments.</summary>
    public static T RemoveAllComments<T>(T node)
        where T : Node
    {
        var emitNode = GetOrCreateEmitNode(node);
        emitNode.Flags |= EmitFlags.NoComments;
        emitNode.LeadingComments = null;
        emitNode.TrailingComments = null;
        return node;
    }

    /// <summary>Sets the flags that control emit behavior of a node.</summary>
    public static T SetEmitFlags<T>(T node, EmitFlags emitFlags)
        where T : Node
    {
        GetOrCreateEmitNode(node).Flags = emitFlags;
        return node;
    }

    /// <summary>Adds to the flags that control emit behavior of a node.</summary>
    public static T AddEmitFlags<T>(T node, EmitFlags emitFlags)
        where T : Node
    {
        var emitNode = GetOrCreateEmitNode(node);
        emitNode.Flags |= emitFlags;
        return node;
    }

    public static T SetInternalEmitFlags<T>(T node, InternalEmitFlags emitFlags)
        where T : Node
    {
        GetOrCreateEmitNode(node).InternalFlags = emitFlags;
        return node;
    }

    public static T AddInternalEmitFlags<T>(T node, InternalEmitFlags emitFlags)
        where T : Node
    {
        var emitNode = GetOrCreateEmitNode(node);
        emitNode.InternalFlags |= emitFlags;
        return node;
    }

    /// <summary>
    /// The custom text range to use when emitting source maps, falling back to the node itself.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS returns <c>SourceMapRange</c> here while returning <c>node</c> in the fallback — legal only
    /// because TS's types are structural. The port returns <see cref="ITextRange"/>, the common shape; a caller that
    /// needs the optional <c>source</c> member tests with <c>as ISourceMapRange</c>.
    /// </remarks>
    public static ITextRange GetSourceMapRange(Node node) => node.EmitNode?.SourceMapRange ?? (ITextRange)node;

    public static T SetSourceMapRange<T>(T node, ISourceMapRange? range)
        where T : Node
    {
        GetOrCreateEmitNode(node).SourceMapRange = range;
        return node;
    }

    /// <summary>The text range to use for source maps for a token of a node.</summary>
    public static ISourceMapRange? GetTokenSourceMapRange(Node node, SyntaxKind token)
    {
        var ranges = node.EmitNode?.TokenSourceMapRanges;
        return ranges is not null && (int)token < ranges.Count ? ranges[(int)token] : null;
    }

    /// <summary>Sets the text range to use for source maps for a token of a node.</summary>
    /// <remarks>
    /// PORT-NOTE: TS indexes a sparse array by <c>token</c>, which leaves holes. The port pads a
    /// <c>List&lt;ISourceMapRange&gt;</c> with <c>null!</c> up to the index — the same "hole punching" §13.4 uses for
    /// the polling queue.
    /// </remarks>
    public static T SetTokenSourceMapRange<T>(T node, SyntaxKind token, ISourceMapRange? range)
        where T : Node
    {
        var emitNode = GetOrCreateEmitNode(node);
        var ranges = emitNode.TokenSourceMapRanges ??= [];
        while (ranges.Count <= (int)token)
        {
            ranges.Add(null!);
        }

        ranges[(int)token] = range!;
        return node;
    }

    public static bool? GetStartsOnNewLine(Node node) => node.EmitNode?.StartsOnNewLine;

    public static T SetStartsOnNewLine<T>(T node, bool newLine)
        where T : Node
    {
        GetOrCreateEmitNode(node).StartsOnNewLine = newLine;
        return node;
    }

    /// <summary>The custom text range to use when emitting comments, falling back to the node itself.</summary>
    public static ITextRange GetCommentRange(Node node) => node.EmitNode?.CommentRange ?? node;

    public static T SetCommentRange<T>(T node, ITextRange range)
        where T : Node
    {
        GetOrCreateEmitNode(node).CommentRange = range;
        return node;
    }

    public static List<ISynthesizedComment>? GetSyntheticLeadingComments(Node node) => node.EmitNode?.LeadingComments;

    public static T SetSyntheticLeadingComments<T>(T node, List<ISynthesizedComment>? comments)
        where T : Node
    {
        GetOrCreateEmitNode(node).LeadingComments = comments;
        return node;
    }

    public static T AddSyntheticLeadingComment<T>(T node, SyntaxKind kind, string text, bool? hasTrailingNewLine = null)
        where T : Node => SetSyntheticLeadingComments(node, Core.Append(
            GetSyntheticLeadingComments(node),
            new SynthesizedComment
            {
                Kind = kind,
                Pos = -1,
                End = -1,
                HasTrailingNewLine = hasTrailingNewLine == true,
                Text = text,
            }) as List<ISynthesizedComment>);

    public static List<ISynthesizedComment>? GetSyntheticTrailingComments(Node node) => node.EmitNode?.TrailingComments;

    public static T SetSyntheticTrailingComments<T>(T node, List<ISynthesizedComment>? comments)
        where T : Node
    {
        GetOrCreateEmitNode(node).TrailingComments = comments;
        return node;
    }

    public static T AddSyntheticTrailingComment<T>(T node, SyntaxKind kind, string text, bool? hasTrailingNewLine = null)
        where T : Node => SetSyntheticTrailingComments(node, Core.Append(
            GetSyntheticTrailingComments(node),
            new SynthesizedComment
            {
                Kind = kind,
                Pos = -1,
                End = -1,
                HasTrailingNewLine = hasTrailingNewLine == true,
                Text = text,
            }) as List<ISynthesizedComment>);

    public static T MoveSyntheticComments<T>(T node, Node original)
        where T : Node
    {
        SetSyntheticLeadingComments(node, GetSyntheticLeadingComments(original));
        SetSyntheticTrailingComments(node, GetSyntheticTrailingComments(original));
        var emit = GetOrCreateEmitNode(original);
        emit.LeadingComments = null;
        emit.TrailingComments = null;
        return node;
    }

    /// <summary>The constant value to emit for an expression representing an enum.</summary>
    public static object? GetConstantValue(Node node) => node.EmitNode?.ConstantValue;

    /// <summary>Sets the constant value to emit for an expression.</summary>
    public static Node SetConstantValue(Node node, object value)
    {
        GetOrCreateEmitNode(node).ConstantValue = value;
        return node;
    }

    public static T AddEmitHelper<T>(T node, Node helper)
        where T : Node
    {
        var emitNode = GetOrCreateEmitNode(node);
            emitNode.Helpers = Core.Append(emitNode.Helpers, helper) as List<Node>;
        return node;
    }

    public static T AddEmitHelpers<T>(T node, IList<Node>? helpers)
        where T : Node
    {
        if (Core.Some(helpers))
        {
            var emitNode = GetOrCreateEmitNode(node);
            foreach (var helper in helpers!)
            {
                emitNode.Helpers = Core.AppendIfUnique(emitNode.Helpers, helper) as List<Node>;
            }
        }

        return node;
    }

    public static bool RemoveEmitHelper(Node node, Node helper)
    {
        var helpers = node.EmitNode?.Helpers;
        return helpers is not null && Core.OrderedRemoveItem(helpers, helper);
    }

    public static List<Node>? GetEmitHelpers(Node node) => node.EmitNode?.Helpers;

    /// <summary>Moves matching emit helpers from a source node to a target node.</summary>
    public static void MoveEmitHelpers(Node source, Node target, Func<Node, bool> predicate)
    {
        var sourceEmitHelpers = source.EmitNode?.Helpers;
        if (!Core.Some(sourceEmitHelpers))
        {
            return;
        }

        var targetEmitNode = GetOrCreateEmitNode(target);
        var helpersRemoved = 0;
        for (var i = 0; i < sourceEmitHelpers!.Count; i++)
        {
            var helper = sourceEmitHelpers[i];
            if (predicate(helper))
            {
                helpersRemoved++;
                targetEmitNode.Helpers = Core.AppendIfUnique(targetEmitNode.Helpers, helper) as List<Node>;
            }
            else if (helpersRemoved > 0)
            {
                sourceEmitHelpers[i - helpersRemoved] = helper;
            }
        }

        if (helpersRemoved > 0)
        {
            // `sourceEmitHelpers.length -= helpersRemoved` — IList<T> has no RemoveRange, so trim one by one.
            for (var i = 0; i < helpersRemoved; i++)
            {
                sourceEmitHelpers.RemoveAt(sourceEmitHelpers.Count - 1);
            }
        }
    }

    public static Node? GetSnippetElement(Node node) => node.EmitNode?.SnippetElement;

    public static T SetSnippetElement<T>(T node, Node snippet)
        where T : Node
    {
        GetOrCreateEmitNode(node).SnippetElement = snippet;
        return node;
    }

    public static T IgnoreSourceNewlines<T>(T node)
        where T : Node
    {
        GetOrCreateEmitNode(node).InternalFlags |= InternalEmitFlags.IgnoreSourceNewlines;
        return node;
    }

    public static T SetTypeNode<T>(T node, ITypeNode type)
        where T : Node
    {
        GetOrCreateEmitNode(node).TypeNode = type;
        return node;
    }

    public static ITypeNode? GetTypeNode(Node node) => node.EmitNode?.TypeNode;

    // PORT-NOTE: TS writes `T extends Identifier`; the port's Identifier is sealed (every node class is), so the
    // constraint is Node and the caller's static type carries the rest.
    public static T SetIdentifierTypeArguments<T>(T node, NodeArray<Node>? typeArguments)
        where T : Node
    {
        GetOrCreateEmitNode(node).IdentifierTypeArguments = typeArguments;
        return node;
    }

    public static NodeArray<Node>? GetIdentifierTypeArguments(Node node) => node.EmitNode?.IdentifierTypeArguments;
    public static T SetIdentifierAutoGenerate<T>(T node, IAutoGenerateInfo? autoGenerate)
        where T : Node
    {
        GetOrCreateEmitNode(node).AutoGenerate = autoGenerate;
        return node;
    }

    public static IAutoGenerateInfo? GetIdentifierAutoGenerate(Node node) => node.EmitNode?.AutoGenerate;

    public static T SetIdentifierGeneratedImportReference<T>(T node, ImportSpecifier? value)
        where T : Node
    {
        GetOrCreateEmitNode(node).GeneratedImportReference = value;
        return node;
    }

    public static ImportSpecifier? GetIdentifierGeneratedImportReference(Node node) =>
        node.EmitNode?.GeneratedImportReference;
}
