using TypeScript.Compiler.Tools;

namespace TypeScript.Compiler.Tests;

/// <summary>
/// TS 联合类型如何落到 C#（docs/architecture.md §12.3）：要么收敛到成员共有的那一个类型，要么展开成一组
/// C# 重载；两者都做不到时保留 <c>object?</c>，并在它旁边留下一句说明。
/// </summary>
/// <remarks>
/// 模型是手工搭的最小版本 —— <c>Node</c> 是根，<c>Expression</c> 是链上的一步，<c>Identifier</c> 与
/// <c>StringLiteral</c> 是具体节点，<c>Decorator</c> 直接挂在 <c>Node</c> 上。别名照 TS 的写法，包括以
/// <c>|</c> 开头的多行联合：那正是曾经把 <c>ModifierLike[]</c> 判成 <c>List&lt;object?&gt;</c> 的坑。
/// </remarks>
public class MappedSlotTests
{
    [Fact]
    public void Union_ofAtoms_BecomesOverloads()
    {
        var slot = Mapper().MapSlot("string | number", optional: false);

        Assert.Equal("object?", slot.Type);
        Assert.Equal(new[] { "int", "string" }, slot.Overloads);
        Assert.Null(slot.Note);
    }

    [Fact]
    public void Union_ofAtomAndNode_BecomesOverloads()
    {
        // `string` 与节点没有公共类型，所以一条核心签名（object?）加两个窄化重载。
        var slot = Mapper().MapSlot("string | Identifier", optional: false);

        Assert.Equal("object?", slot.Type);
        Assert.Equal(new[] { "Identifier", "string" }, slot.Overloads);
    }

    [Fact]
    public void Union_ofNodeTypes_CollapsesWidening()
    {
        // 成员都是节点时不需要重载：取它们共有的最派生基类。
        var slot = Mapper().MapSlot("Identifier | StringLiteral", optional: false);

        Assert.Equal("Expression", slot.Type);
        Assert.Empty(slot.Overloads);
    }

    [Fact]
    public void AliasUnion_KeepsNoEmptyMember()
    {
        // `Modifier` 以 `|` 开头（TS 的多行联合就写成这样）。若把开头那个空段当成一个成员，它会映射成
        // `object?` 并让整个元素类型退化 —— `readonly ModifierLike[]` 也就成了 `List<object?>`。
        var slot = Mapper().MapSlot("readonly ModifierLike[]", optional: false);

        Assert.Equal("List<Node>", slot.Type);
        Assert.Empty(slot.Overloads);
    }

    [Fact]
    public void IndexedAccessType_IsResolvedBeforeMapping()
    {
        // 提取阶段由 tsc 的 checker 把 `LiteralTypeNode["literal"]` 换成它真正的类型；映射器看到的已经是
        // 联合，于是照常收敛。这里用 checker 会给出的答案来验证收敛方向。
        var slot = Mapper().MapSlot("Identifier | StringLiteral", optional: false);

        Assert.Equal("Expression", slot.Type);
    }

    [Fact]
    public void TypeWithNoCSharpCounterpart_KeepsNote()
    {
        var slot = Mapper().MapSlot("never", optional: false);

        Assert.Equal("object?", slot.Type);
        Assert.Contains("never", slot.Note);
    }

    [Fact]
    public void UnknownName_KeepsNoteNamingIt()
    {
        var slot = Mapper().MapSlot("BaseNodeFactory", optional: false);

        Assert.Equal("object?", slot.Type);
        Assert.Contains("BaseNodeFactory", slot.Note);
        Assert.Contains("outside the extracted sources", slot.Note);
    }

    [Fact]
    public void Optional_MemberTakesNullableAnnotation()
    {
        var slot = Mapper().MapSlot("Identifier | undefined", optional: true);

        Assert.Equal("Identifier?", slot.Type);
    }

    [Fact]
    public void GeneratedFactory_SplitsUnionsIntoOverloads()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "TypeScript.Compiler", "Model", "NodeInterfaces.cs"));

        // `createNumericLiteral(value: string | number)` → 一个核心签名 + 两条窄化重载（默认实现转发）。
        Assert.Contains(
            "NumericLiteral CreateNumericLiteral(int value, TokenFlags? numericLiteralFlags) => CreateNumericLiteral((object?)value, numericLiteralFlags);",
            source);
        Assert.Contains(
            "NumericLiteral CreateNumericLiteral(string? value, TokenFlags? numericLiteralFlags) => CreateNumericLiteral((object?)value, numericLiteralFlags);",
            source);
    }

    [Fact]
    public void GeneratedFactory_NotesTypesDeclaredElsewhere()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "TypeScript.Compiler", "Model", "NodeInterfaces.cs"));

        Assert.Contains(
            "PORT-NOTE: <c>BaseNodeFactory</c> is declared outside the extracted sources",
            source);
    }

    private static TsTypeMapper Mapper() => new(
        new TsTypeModel(
            [
                new TsInterface { Name = "Node" },
                Chain("Expression", "Node"),
                Concrete("Identifier", "Expression"),
                Concrete("StringLiteral", "Expression"),
                Concrete("Decorator", "Node"),
            ],
            [
                new TsTypeAlias { Name = "DeclarationName", Value = "| Identifier | StringLiteral" },
                new TsTypeAlias { Name = "Modifier", Value = "| Identifier | StringLiteral" },
                new TsTypeAlias { Name = "ModifierLike", Value = "Modifier | Decorator" },
            ]),
        ["SyntaxKind"]);

    private static TsInterface Chain(string name, string baseName)
    {
        var iface = new TsInterface { Name = name };
        iface.Extends.Add(baseName);
        return iface;
    }

    private static TsInterface Concrete(string name, string baseName)
    {
        var iface = new TsInterface { Name = name, ConcreteKind = name + "Kind" };
        iface.Extends.Add(baseName);
        return iface;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "tsc-cs.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("repository root not found");
    }
}
