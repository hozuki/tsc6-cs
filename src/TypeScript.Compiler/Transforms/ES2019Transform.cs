#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/es2019.ts:1-51 — the whole ES2019 downlevel transform.
/// <summary>
/// ported from: src/compiler/transformers/es2019.ts:15-51 — <c>transformES2019</c>: the transformer that rewrites the
/// one ES2019 syntax a <see cref="SourceFile"/> may contain — the optional <c>catch</c> binding
/// (<c>catch { … }</c>) — into a <c>catch</c> clause with a generated binding the emitter prints.
/// </summary>
/// <remarks>
/// PORT-NOTE (the shape): as <c>ES2015Transform</c> records for <c>es2015.ts</c>, the reference exports one function
/// whose body is a table of closures over the factory's locals; the port states the factory as
/// <see cref="ES2019Transform.TransformES2019"/> and the closures as the members of <see cref="ES2019Transformer"/>.
/// </remarks>
internal static class ES2019Transform
{
    /// <summary>ported from: src/compiler/transformers/es2019.ts:15-51 — <c>transformES2019</c>.</summary>
    public static Func<object?, object?> TransformES2019(ITransformerContext context)
        => new ES2019Transformer(context).TransformSourceFileOrBundle;
}

/// <summary>ported from: src/compiler/transformers/es2019.ts:15-50 — the body of <c>transformES2019</c>.</summary>
internal sealed class ES2019Transformer
{
    private readonly ITransformerContext _context;
    private readonly NodeFactory _factory;

    /// <summary>ported from: src/compiler/transformers/es2019.ts:17 — <c>chainBundle(context, transformSourceFile)</c>.</summary>
    private readonly Func<Node, Node> _transformSourceFile;

    /// <summary>ported from: src/compiler/transformers/es2019.ts:16-17 — the factory's prologue.</summary>
    /// <remarks>PORT-NOTE: the reference destructures <c>factory</c> out of the context; the port keeps the context and
    /// reads the same slot through it (its body is <c>TransformationContext&lt;T&gt;</c>'s, Transformer.cs).</remarks>
    public ES2019Transformer(ITransformerContext context)
    {
        _context = context;
        _factory = context.Factory;
        _transformSourceFile = TransformationUtilities.ChainBundle(context, TransformSourceFile);
    }

    /// <summary>ported from: src/compiler/transformers/es2019.ts:17 — the returned
    /// <c>chainBundle(context, transformSourceFile)</c>.</summary>
    public object? TransformSourceFileOrBundle(object? node) => _transformSourceFile((Node)node!);

    /// <summary>ported from: src/compiler/transformers/es2019.ts:19-25 — <c>transformSourceFile</c>.</summary>
    private SourceFile TransformSourceFile(SourceFile node)
    {
        if (node.IsDeclarationFile)
        {
            return node;
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/es2019.ts:27-38 — <c>visitor</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>node.transformFlags &amp; TransformFlags.ContainsES2019</c> is truthiness of a
    /// number, so the port states the <c>!= 0</c> test.</remarks>
    private object? Visitor(Node node)
    {
        if ((node.TransformFlags & TransformFlags.ContainsES2019) == 0)
        {
            return node;
        }

        return node.Kind switch
        {
            SyntaxKind.CatchClause => VisitCatchClause((CatchClause)node),
            _ => Utilities.VisitEachChildWorker(node, Visitor, _context),
        };
    }

    /// <summary>ported from: src/compiler/transformers/es2019.ts:40-49 — <c>visitCatchClause</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>createTempVariable(undefined)</c> is the port's two-argument
    /// <c>CreateTempVariable</c> with the absent <c>reservedInNestedScopes</c> stated, and the reference's
    /// one-argument <c>createVariableDeclaration</c> is the port's four-argument form; <c>visitNode(node.block,
    /// visitor, isBlock)</c> is <see cref="Utilities.VisitNode"/> with the same test.</remarks>
    private CatchClause VisitCatchClause(CatchClause node)
    {
        if (node.VariableDeclaration is null)
        {
            return _factory.UpdateCatchClause(
                node,
                _factory.CreateVariableDeclaration(
                    _factory.CreateTempVariable(/*recordTempVariable*/ null, /*reservedInNestedScopes*/ null),
                    /*exclamationToken*/ null,
                    /*type*/ null,
                    /*initializer*/ null),
                Utilities.VisitNode(node.Block, Visitor, NodeTests.IsBlock)!);
        }
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }
}
