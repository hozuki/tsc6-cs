using System.Reflection;

namespace TypeScript.Compiler.Tests;

/// <summary>
/// The node model's capability-versus-classification rule (docs/architecture.md §12.8) has one failure mode that no
/// compile error can catch: a type that TS inherits in the first position for its <em>capability</em> silently takes the
/// class slot, and the kinds that reach it miss their chain.
/// </summary>
/// <remarks>
/// <c>ClassExpression extends ClassLikeDeclarationBase, PrimaryExpression</c> is the case that made this test exist: the
/// capability came first, so the class derived from <c>ClassLikeDeclarationBase</c> instead of walking
/// <c>PrimaryExpression → MemberExpression → … → Expression</c>, and a class expression could not be built at all —
/// while <c>Utilities.IsExpression</c> (a kind check) kept answering <c>true</c> for it. The two sides of that
/// disagreement are checked here: every node the port's own predicates call an expression has to be an
/// <see cref="Expression"/> subclass.
/// </remarks>
public class NodeChainTests
{
    [Fact]
    public void EveryKindTheExpressionPredicatesAccept_IsAnExpressionClass()
    {
        var broken = new List<string>();

        var factory = new NodeFactory();

        foreach (var type in ConcreteNodeTypes())
        {
            var node = (Node)Activator.CreateInstance(type)!;
            Complete(node, factory);
            if (Utilities.IsExpression(node) && node is not Expression)
            {
                broken.Add($"{type.Name} ({node.Kind})");
            }
        }

        Assert.Equal("", string.Join(", ", broken));
    }

    [Fact]
    public void EveryKindTheStatementPredicateAccepts_IsAStatement()
    {
        // `isStatement` is a kind test in the port (`Utilities.IsStatementOrBlock` covers the same kinds plus Block);
        // the model side is the `IStatement` interface on the class.
        var broken = new List<string>();

        foreach (var type in ConcreteNodeTypes())
        {
            var node = (Node)Activator.CreateInstance(type)!;
            if (Utilities.IsStatementOrBlock(node) && node is not IStatement && node.Kind != SyntaxKind.Block)
            {
                broken.Add($"{type.Name} ({node.Kind})");
            }
        }

        Assert.Equal([], broken);
    }

    [Fact]
    public void EveryExpressionSubclass_IsAlsoAnExpressionKind()
    {
        // The converse direction: a class that derives from Expression but whose kind the predicate does not list would
        // be skipped by the parenthesizer's `isLeftHandSideExpression` checks.
        var broken = new List<string>();
        var exempted = new List<SyntaxKind>();

        var factory = new NodeFactory();

        foreach (var type in ConcreteNodeTypes())
        {
            var node = (Node)Activator.CreateInstance(type)!;
            Complete(node, factory);
            if (node is not Expression || Utilities.IsExpression(node)) continue;

            if (ExpressionUnionButNotExpressionKind.Contains(node.Kind))
            {
                exempted.Add(node.Kind);
                continue;
            }

            broken.Add($"{type.Name} ({node.Kind})");
        }

        Assert.Equal("", string.Join(", ", broken));
        Assert.Equal(ExpressionUnionButNotExpressionKind.Order().ToArray(), exempted.Distinct().Order().ToArray());
    }

    /// <summary>
    /// The kinds the reference implementation's own two halves disagree about: its type model puts them in the
    /// <c>Expression</c> union — which is where the node classes' bases come from — while its hand-written
    /// <c>isExpression</c> kind list (<c>utilitiesPublic.ts:2086</c> and the <c>is*ExpressionKind</c> chain under it)
    /// leaves them out.
    /// </summary>
    /// <remarks>
    /// Verified per entry against the reference compiler itself: <c>ts.isExpression({ kind })</c> answers <c>false</c>
    /// for all seven while answering <c>true</c> for <c>JsxElement</c> and <c>JsxSelfClosingElement</c>, the two JSX
    /// forms the predicate does accept. JSX's structural nodes are children of those two, and the two
    /// <c>Synthetic*</c> nodes are built by the transformer layer, never by the parser. Re-run that probe before
    /// changing this list: the guard asserts the list matches exactly what the model produces today, so a stale or
    /// missing entry fails.
    /// </remarks>
    private static readonly SyntaxKind[] ExpressionUnionButNotExpressionKind =
    [
        SyntaxKind.JsxAttributes,
        SyntaxKind.JsxClosingFragment,
        SyntaxKind.JsxExpression,
        SyntaxKind.JsxOpeningElement,
        SyntaxKind.JsxOpeningFragment,
        SyntaxKind.SyntheticExpression,
        SyntaxKind.SyntheticReferenceExpression,
    ];

    /// <summary>
    /// Fills in the one child the predicates in these tests walk through: <c>IsExpression</c> calls
    /// <c>SkipPartiallyEmittedExpressions</c>, which follows <c>PartiallyEmittedExpression.Expression</c> until it finds
    /// a node of another kind. An <c>Activator</c>-created node has that child null, so the walk would dereference null
    /// — the reference implementation crashes the same way on the same half-built node.
    /// </summary>
    private static void Complete(Node node, NodeFactory factory)
    {
        if (node is PartiallyEmittedExpression partial)
        {
            partial.Expression = factory.CreateIdentifier("");
        }
    }

    /// <summary>Every concrete node class the model declares, one instance each.</summary>
    private static IEnumerable<Type> ConcreteNodeTypes()
        => typeof(Node).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsClass: true } && typeof(Node).IsAssignableFrom(type))
            .Where(type => type.GetConstructor(Type.EmptyTypes) is not null);

    [Fact]
    public void EveryTokenKindTheFactoryBuilds_AgreesWithTheExpressionPredicates()
    {
        // The class-side and the kind-side can also disagree in the other direction: a class exists and is correct, but
        // the factory never builds it — `ImportExpression` was exactly that (an `import` token came back as a bare
        // `Token`, which the predicates call an expression and the class chain does not). This walks every kind
        // `createToken` accepts, which is the token range minus the three families TS rejects (identifiers, literals and
        // template pieces).
        var factory = new NodeFactory();
        var broken = new List<string>();
        var exempted = new List<SyntaxKind>();

        foreach (var kind in Enum.GetValues<SyntaxKind>())
        {
            if (kind < SyntaxKind.FirstToken || kind > SyntaxKind.LastToken) continue;
            if (kind == SyntaxKind.Identifier) continue;
            if (kind >= SyntaxKind.FirstLiteralToken && kind <= SyntaxKind.LastLiteralToken) continue;
            if (kind >= SyntaxKind.FirstTemplateToken && kind <= SyntaxKind.LastTemplateToken) continue;

            var node = factory.CreateToken(kind);
            if (node.Kind != kind)
            {
                broken.Add($"{kind}: the factory built {node.Kind}");
            }
            else if (Utilities.IsExpression(node) && node is not Expression)
            {
                if (kind == SyntaxKind.PrivateIdentifier)
                {
                    // The one kind where the reference implementation has the same disagreement:
                    // `ts.factory.createToken(SyntaxKind.PrivateIdentifier)` returns a bare token (`escapedText` is
                    // undefined on it, while `createPrivateIdentifier("#x").escapedText` is "#x") because `createToken`
                    // builds a plain `Token` for every kind and only maps the overloads — and `ts.isExpression` is a kind
                    // test, so it answers true for it. The parser never routes that kind through `createToken`.
                    exempted.Add(kind);
                }
                else
                {
                    broken.Add($"{kind}: the predicates call it an expression, the factory built {node.GetType().Name}");
                }
            }
        }

        Assert.Equal("", string.Join(", ", broken));
        Assert.Equal([SyntaxKind.PrivateIdentifier], exempted);
    }
}
