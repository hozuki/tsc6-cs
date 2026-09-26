using TypeScript.Compiler.Tools;

namespace TypeScript.Compiler.Tests;

/// <summary>
/// TS 把 enum 成员当类型用（<c>createToken(token: SyntaxKind.EndOfFileToken)</c>）时 C# 侧怎么落
/// （docs/architecture.md §16）：参数位置上换成"每个成员一个类型"，别的位置上仍是枚举本身。
/// </summary>
public class TypedEnumMappingTests
{
    [Fact]
    public void EnumMemberAsParameter_BecomesTypedEnumType()
    {
        // 这就是 TS 的静态路径：参数类型把返回类型定下来。
        var slot = Mapper().MapSlot("SyntaxKind.CommaToken", optional: false, parameter: true);

        Assert.Equal("TypedEnum.CommaToken", slot.Type);
    }

    [Fact]
    public void EnumMemberOutsideParameter_StaysSyntaxKind()
    {
        // 属性与返回类型位置上，枚举成员是一个值，不是类型选择器。
        Assert.Equal("SyntaxKind", Mapper().MapSlot("SyntaxKind.CommaToken", optional: false).Type);
    }

    [Fact]
    public void EnumMemberWithoutAType_StaysSyntaxKind()
    {
        // 别名（`FirstAssignment = EqualsToken`）与 `Count` 哨兵没有嵌套类型 —— 见 TypedEnumEmitter。
        var slot = Mapper().MapSlot("SyntaxKind.FirstAssignment", optional: false, parameter: true);

        Assert.Equal("SyntaxKind", slot.Type);
    }

    [Fact]
    public void GeneratedFactory_KeepsTheStaticAndDynamicPathsApart()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "TypeScript.Compiler", "Model", "NodeInterfaces.cs"));

        // 动态路径：一条宽签名，收编译期算不出的 kind。
        Assert.Contains("Node CreateToken(SyntaxKind token);", source);

        // 静态路径：每个成员一条默认实现，转发到宽签名。
        Assert.Contains(
            "SuperExpression CreateToken(TypedEnum.SuperKeyword? token) => (SuperExpression)CreateToken(token!.SyntaxKind);",
            source);
        Assert.Contains(
            "Token CreateToken(TypedEnum.Unknown? token) => (Token)CreateToken(token!.SyntaxKind);",
            source);
    }

    [Fact]
    public void GeneratedFactory_DropsEnumConstrainedTypeParameters()
    {
        // `createModifier<T extends ModifierSyntaxKind>(kind: T)` 在 C# 里没有对应写法：类型参数取不到具体枚举，
        // 而调用点传的就是枚举本身，所以参数直接就是它。
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "TypeScript.Compiler", "Model", "NodeInterfaces.cs"));

        Assert.Contains("ModifierToken CreateModifier(SyntaxKind kind);", source);
        Assert.DoesNotContain("CreateModifier<", source);
    }

    private static TsTypeMapper Mapper() => new(
        new TsTypeModel([new TsInterface { Name = "Node" }], []),
        ["SyntaxKind"],
        ["CommaToken"]);

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
