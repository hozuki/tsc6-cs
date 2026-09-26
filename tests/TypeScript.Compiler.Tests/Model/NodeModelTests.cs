namespace TypeScript.Compiler.Tests;

/// <summary>
/// M2b 的验收：生成的节点模型必须复刻 <c>types.ts</c> 的形状 —— 继承链、能力接口，以及 TS 在子接口上
/// 收窄成员类型时 C# 如何桥接。
/// </summary>
public class NodeModelTests
{
    [Fact]
    public void Node_IsTheAbstractRoot()
    {
        Assert.True(typeof(Node).IsAbstract);
        Assert.True(typeof(Identifier).IsSubclassOf(typeof(Node)));
        Assert.Equal(typeof(int), System.Enum.GetUnderlyingType(typeof(SyntaxKind)));
    }

    [Fact]
    public void ConcreteNodes_PinTheirOwnKind()
    {
        // Kind 是抽象属性，由每个具体节点用表达式体重写。
        Assert.Equal(SyntaxKind.Identifier, new Identifier().Kind);
        Assert.Equal(SyntaxKind.CallExpression, new CallExpression().Kind);
        Assert.Equal(SyntaxKind.FunctionDeclaration, new FunctionDeclaration().Kind);
        Assert.Equal(SyntaxKind.Block, new Block().Kind);
    }

    [Fact]
    public void CategorisationChain_IsAClassChain()
    {
        // TS: CallExpression → LeftHandSideExpression → UpdateExpression → UnaryExpression → Expression → Node
        Assert.True(typeof(LeftHandSideExpression).IsSubclassOf(typeof(UpdateExpression)));
        Assert.True(typeof(UpdateExpression).IsSubclassOf(typeof(UnaryExpression)));
        Assert.True(typeof(UnaryExpression).IsSubclassOf(typeof(Expression)));
        Assert.True(typeof(Expression).IsSubclassOf(typeof(Node)));
    }

    [Fact]
    public void CategorisationSteps_AreAbstract()
    {
        // 没有钉死 kind 的类只是分类台阶，不能直接实例化。
        Assert.True(typeof(Expression).IsAbstract);
        Assert.True(typeof(LeftHandSideExpression).IsAbstract);
        Assert.False(typeof(Identifier).IsAbstract);
    }

    [Fact]
    public void Mixins_AreInterfacesImplementedByTheNodes()
    {
        // D1：只有出现在 extends 首位的接口才是链上的一环（类），其余是 mixin（接口）。
        Assert.True(typeof(IDeclaration).IsInterface);
        Assert.True(typeof(ILocalsContainer).IsInterface);

        Assert.True(typeof(IDeclaration).IsAssignableFrom(typeof(Identifier)));
        Assert.True(typeof(IDeclaration).IsAssignableFrom(typeof(CallExpression)));
    }

    [Fact]
    public void TokenFamily_IsOneClassPerShapeNotPerKind()
    {
        // 187 个 token 种类共享少数几个类，靠 Kind 区分（TS 用类型别名声明它们）。
        var comma = new PunctuationToken(SyntaxKind.CommaToken);
        var arrow = new KeywordToken(SyntaxKind.EqualsGreaterThanToken);

        Assert.Equal(SyntaxKind.CommaToken, comma.Kind);
        Assert.Equal(SyntaxKind.EqualsGreaterThanToken, arrow.Kind);
        Assert.True(comma is Token);
        Assert.True(arrow is Token);
        Assert.True(new ModifierToken(SyntaxKind.PublicKeyword) is KeywordToken);
    }

    [Fact]
    public void NodeCarriesTheSharedSlots()
    {
        var node = new Identifier
        {
            Flags = NodeFlags.Let,
            Pos = 3,
            End = 9,
        };

        Assert.Equal(NodeFlags.Let, node.Flags);
        Assert.Equal(3, node.Pos);
        Assert.Equal(9, node.End);
        Assert.Null(node.Parent);
        Assert.Null(node.Id);
    }

    [Fact]
    public void ReadonlyMembers_AreGetOnly()
    {
        // TS 的 `readonly` 成员在生成物里是只读属性 —— 这让"哪些字段由 parser/factory 写入"在类型层面可见。
        Assert.Null(typeof(Identifier).GetProperty(nameof(Identifier.EscapedText))!.SetMethod);
        Assert.NotNull(typeof(Identifier).GetProperty(nameof(Identifier.Flags))!.SetMethod);
    }

    [Fact]
    public void NarrowedMember_IsBridgedByAnExplicitInterfaceImplementation()
    {
        // TS 的 FunctionDeclaration.body 收窄为 Block，而 FunctionLikeDeclarationBase.body 是 ConciseBody。
        // C# 要求实现成员与接口精确匹配，且协变返回只适用于 override，因此生成器为接口侧补了显式实现。
        var bodyProperty = typeof(FunctionDeclaration).GetProperty(nameof(FunctionDeclaration.Body))!;
        Assert.Equal(typeof(Block), bodyProperty.PropertyType);

        var bridged = typeof(FunctionDeclaration)
            .GetProperties(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Where(p => p.Name.Contains("Body", StringComparison.Ordinal))
            .ToList();

        // 显式实现的名字形如 "TypeScript.Compiler.IFunctionLikeDeclarationBase.Body"。
        Assert.Contains(bridged, p => p.Name.StartsWith("TypeScript.Compiler.I", StringComparison.Ordinal));
    }

    [Fact]
    public void NodeArray_CarriesATextRangeAndItsFlags()
    {
        var array = new NodeArray<Node> { HasTrailingComma = true, Pos = 1, End = 12 };
        array.Add(new Identifier());

        Assert.Single(array);
        Assert.True(array.HasTrailingComma);
        Assert.Equal(1, array.Pos);
        Assert.Equal(12, array.End);
    }
}
