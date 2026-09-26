namespace TypeScript.Compiler.Tests;

/// <summary>
/// <see cref="ForEachChildTable"/> 的行为：源序、JS truthiness 短路、数组回调拿到数组本身、
/// <see cref="ForEachChildTable.Skip"/> 与不依赖调用栈的深度遍历（parser.ts:1249-1331）。
/// </summary>
/// <remarks>
/// 这些断言对着 TS 的语义而不是对着生成物写：TS 的链是 <c>||</c>，<c>||</c> 判的是 truthy
/// （返回 <c>false</c>/<c>0</c> 要继续），而 <c>NodeArray&lt;T&gt;</c> 的协变让同一个数组回调服务所有元素类型。
/// </remarks>
public class ForEachChildTests
{
    private static NodeFactory Factory() => new();

    [Fact]
    public void QualifiedName_VisitsLeftThenRight()
    {
        var factory = Factory();
        var left = factory.CreateIdentifier("a");
        var right = factory.CreateIdentifier("b");
        var name = factory.CreateQualifiedName(left, right);

        var visits = Visits(name);

        Assert.Equal(new Node[] { left, right }, visits);
    }

    [Fact]
    public void CallExpression_VisitsExpressionThenTheArgumentsInOrder()
    {
        // No array callback: visitNodes falls back to walking the elements, in order.
        var factory = Factory();
        var callee = factory.CreateIdentifier("f");
        var first = factory.CreateIdentifier("a");
        var second = factory.CreateIdentifier("b");
        var call = factory.CreateCallExpression(callee, null, new List<Expression> { first, second });

        var visits = Visits(call);

        Assert.Equal(new Node[] { callee, first, second }, visits);
    }

    [Fact]
    public void ClassDeclaration_VisitsNameThenMembersInOrder()
    {
        var factory = Factory();
        var name = factory.CreateIdentifier("C");
        var property = factory.CreatePropertyDeclaration(null, factory.CreateIdentifier("p"), null, null, null);
        var declaration = factory.CreateClassDeclaration(null, name, null, null, new List<IClassElement> { property });

        var visits = Visits(declaration);

        // modifiers/typeParameters/heritageClauses are absent, so they contribute nothing.
        Assert.Equal(new Node[] { name, property }, visits);
    }

    [Fact]
    public void CallbackReturningFalse_KeepsTheChainGoing_ButTrueStopsIt()
    {
        var factory = Factory();
        var left = factory.CreateIdentifier("a");
        var right = factory.CreateIdentifier("b");
        var name = factory.CreateQualifiedName(left, right);

        var visits = new List<Node>();
        var stopped = ForEachChildTable.ForEachChild<bool>(name, node => { visits.Add(node); return node == right; });

        Assert.True(stopped);
        Assert.Equal(new Node[] { left, right }, visits);

        visits.Clear();
        var notStopped = ForEachChildTable.ForEachChild<bool>(name, node => { visits.Add(node); return false; });

        // `false` is falsy, so the chain must not stop: with a null test it would have.
        Assert.False(notStopped);
        Assert.Equal(new Node[] { left, right }, visits);
    }

    [Fact]
    public void CallbackReturningZero_IsFalsy_SoTheChainContinues()
    {
        var factory = Factory();
        var left = factory.CreateIdentifier("a");
        var right = factory.CreateIdentifier("b");
        var name = factory.CreateQualifiedName(left, right);

        var visits = new List<Node>();
        var zero = ForEachChildTable.ForEachChild<int>(name, node => { visits.Add(node); return 0; });
        Assert.Equal(0, zero);
        Assert.Equal(new Node[] { left, right }, visits);

        visits.Clear();
        var stopped = ForEachChildTable.ForEachChild<int>(name, node => { visits.Add(node); return node == left ? 0 : 1; });
        Assert.Equal(1, stopped);
        Assert.Equal(new Node[] { left, right }, visits);
    }

    [Fact]
    public void ArrayCallback_ReceivesTheArrayItself_AndItsTruthyResultStopsTheChain()
    {
        var factory = Factory();
        var callee = factory.CreateIdentifier("f");
        var call = factory.CreateCallExpression(callee, null, new List<Expression> { factory.CreateIdentifier("a"), factory.CreateIdentifier("b") });

        var nodeVisits = new List<Node>();
        INodeArray<INode>? received = null;
        var arrayCalls = 0;
        var result = ForEachChildTable.ForEachChild<Node>(
            call,
            node => { nodeVisits.Add(node); return null; },
            nodes => { arrayCalls++; received = nodes; return null; });

        Assert.Null(result);
        Assert.Equal(new Node[] { callee }, nodeVisits);
        Assert.Equal(1, arrayCalls);
        Assert.Same(call.Arguments, received);

        // A truthy array result stops the chain, like any other step of the `||`.
        nodeVisits.Clear();
        var stopped = ForEachChildTable.ForEachChild<bool>(
            call,
            node => { nodeVisits.Add(node); return false; },
            nodes => nodes.Count == 2);

        Assert.True(stopped);
        Assert.Equal(new Node[] { callee }, nodeVisits);
    }

    [Fact]
    public void JSDocComment_IsVisitedOnlyWhenItIsANodeArray()
    {
        var comment = new NodeArray<JSDocTag> { new JSDocUnknownTag() };
        var tag = new JSDocUnknownTag { Comment = comment };

        INodeArray<INode>? received = null;
        ForEachChildTable.ForEachChild<Node>(tag, _ => null, nodes => { received = nodes; return null; });

        Assert.Same(comment, received!);

        // TS's guard is `typeof node.comment === "string" ? undefined : visitNodes(…)`, so a string comment
        // contributes no children at all.
        var stringTag = new JSDocUnknownTag { Comment = "note" };
        Assert.Empty(Visits(stringTag));
    }

    [Fact]
    public void JSDocParameterTag_FollowsTheIsNameFirstOrder()
    {
        var factory = Factory();
        var tagName = factory.CreateIdentifier("param");
        var name = factory.CreateIdentifier("x");
        var type = factory.CreateJSDocTypeExpression(factory.CreateKeywordTypeNode(SyntaxKind.StringKeyword));

        var tag = new JSDocParameterTag { TagName = tagName, Name = name, TypeExpression = type, IsNameFirst = true };
        Assert.Equal(new Node[] { tagName, name, type }, Visits(tag));

        tag.IsNameFirst = false;
        Assert.Equal(new Node[] { tagName, type, name }, Visits(tag));
    }

    [Fact]
    public void JSDocSignature_WalksItsListsElementWise_NeverThroughTheArrayCallback()
    {
        var factory = Factory();
        var template = new JSDocTemplateTag();
        var first = new JSDocParameterTag();
        var second = new JSDocParameterTag();
        var type = new JSDocReturnTag();
        var signature = factory.CreateJSDocSignature(
            new List<JSDocTemplateTag> { template },
            new List<JSDocParameterTag> { first, second },
            type);

        Assert.Equal(new Node[] { template, first, second, type }, Visits(signature));

        // These two visitors use core.ts's `forEach`, not `visitNodes`: an array callback is never invoked.
        var arrayCalls = 0;
        var visits = new List<Node>();
        ForEachChildTable.ForEachChild<Node>(
            signature,
            node => { visits.Add(node); return null; },
            _ => { arrayCalls++; return null; });

        Assert.Equal(0, arrayCalls);
        Assert.Equal(new Node[] { template, first, second, type }, visits);
    }

    [Fact]
    public void Recursively_SkipStopsDescendingIntoThatNode_ButNotItsSiblings()
    {
        var factory = Factory();
        var a = factory.CreateIdentifier("a");
        var b = factory.CreateIdentifier("b");
        var c = factory.CreateIdentifier("c");
        var inner = factory.CreateQualifiedName(a, b);
        var root = factory.CreateQualifiedName(inner, c);

        var visits = new List<Node>();
        var result = ForEachChildTable.ForEachChildRecursively(
            root,
            (node, _) => { visits.Add(node); return node == inner ? ForEachChildTable.Skip : null; });

        Assert.Null(result);
        Assert.Equal(new Node[] { inner, c }, visits);
    }

    [Fact]
    public void Recursively_WalksDepthFirstPreOrder_WithEachChildsParent()
    {
        var factory = Factory();
        var a = factory.CreateIdentifier("a");
        var b = factory.CreateIdentifier("b");
        var c = factory.CreateIdentifier("c");
        var inner = factory.CreateQualifiedName(a, b);
        var root = factory.CreateQualifiedName(inner, c);

        var visits = new List<(Node Node, Node Parent)>();
        ForEachChildTable.ForEachChildRecursively(root, (node, parent) => { visits.Add((node, parent)); return null; });

        // The root itself is a work item's source, not a work item: the walk starts at its children.
        Assert.Equal(new Node[] { inner, a, b, c }, visits.Select(static visit => visit.Node));
        Assert.Equal(new Node[] { root, inner, inner, root }, visits.Select(static visit => visit.Parent));
    }

    [Fact]
    public void Recursively_HandlesATreeDeeperThanTheCallStack()
    {
        // The point of the explicit stack (parser.ts:1279-1280): a left-leaning tree of this depth overflows a
        // recursive walk. 20k nested QualifiedNames, each `Left = previous, Right = leaf`:
        //   - the root is a source of work items, never a work item, so 19,999 QualifiedNames are visited;
        //   - every name visits its right-hand identifier (20,000 of those) and the innermost one also visits the
        //     leaf on its left, so the shared identifier is visited 20,001 times.
        var factory = Factory();
        var leaf = factory.CreateIdentifier("leaf");
        Node node = leaf;
        for (var i = 0; i < 20_000; i++)
        {
            node = factory.CreateQualifiedName(node, leaf);
        }

        var count = 0;
        ForEachChildTable.ForEachChildRecursively(node, (_, _) => { count++; return null; });

        Assert.Equal(40_000, count);
    }

    [Fact]
    public void Dispatch_VisitsChildrenForRepresentativeKinds()
    {
        // A per-kind smoke: a table entry that went missing would otherwise only show up as a wrong tree walk.
        var factory = Factory();
        var name = factory.CreateIdentifier("x");
        var other = factory.CreateIdentifier("y");
        var property = factory.CreatePropertyAssignment(name, other);
        var parameter = new ParameterDeclaration { Name = name };
        var signature = factory.CreateJSDocSignature(null, new List<JSDocParameterTag> { new JSDocParameterTag() }, null);
        var jsdocTag = new JSDocUnknownTag();
        var jsdoc = new JSDoc { Tags = new NodeArray<JSDocTag> { jsdocTag } };

        var cases = new (string Kind, Node Node, Node Expected)[]
        {
            ("QualifiedName", factory.CreateQualifiedName(name, other), name),
            ("CallExpression", factory.CreateCallExpression(name, null, new List<Expression> { other }), other),
            ("ClassDeclaration", factory.CreateClassDeclaration(null, name, null, null, null), name),
            // The children of a signature/object literal/member list are the declarations inside them.
            ("CallSignature", factory.CreateCallSignature(null, new List<ParameterDeclaration> { parameter }, null), parameter),
            ("ObjectLiteralExpression", factory.CreateObjectLiteralExpression(new List<Node> { property }, null), property),
            ("JSDocSignature", signature, signature.Parameters![0]),
            ("JSDoc", jsdoc, jsdocTag),
            ("JSDocParameterTag", new JSDocParameterTag { TagName = name, IsNameFirst = true }, name),
        };

        foreach (var (kind, node, expected) in cases)
        {
            var visits = Visits(node);
            Assert.True(
                visits.Contains(expected),
                $"SyntaxKind.{kind} did not visit its expected child (visited {visits.Count} child/children)");
        }
    }

    private static List<Node> Visits(Node node, Func<INodeArray<INode>, Node?>? cbNodes = null)
    {
        var visits = new List<Node>();
        ForEachChildTable.ForEachChild<Node>(node, child => { visits.Add(child); return null; }, cbNodes);
        return visits;
    }
}
