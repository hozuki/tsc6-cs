namespace TypeScript.Compiler.Tests;

/// <summary>
/// M5-0 的 binder 叶导出：<c>getModuleInstanceState</c> 家族、<c>getContainerFlags</c>、
/// <c>isExportsOrModuleExportsOrAlias</c>。
/// </summary>
/// <remarks>
/// 这三个是 binder 里唯一不依赖 <c>createBinder</c> 的成员，而 <c>bindWorker</c> 一落地就要用
/// <c>getContainerFlags</c> 决定每个 kind 是不是容器。测的是它们对真实 AST 的判定，不是它们被谁调用。
/// </remarks>
public class BinderPrereqTests
{
    private static SourceFile Parse(string text)
        => Parser.CreateSourceFile("test.ts", text, ScriptTarget.ESNext, /*setParentNodes*/ true, ScriptKind.TS);

    private static ModuleDeclaration ParseAmbientModule(string body)
        => (ModuleDeclaration)Parse($"declare module \"m\" {{ {body} }}").Statements![0];

    // ---------------------------------------------------------------------------------------------
    // getModuleInstanceState
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("interface I {}", (int)ModuleInstanceState.NonInstantiated)]
    [InlineData("type T = number;", (int)ModuleInstanceState.NonInstantiated)]
    [InlineData("const enum E {}", (int)ModuleInstanceState.ConstEnumOnly)]
    [InlineData("enum E {}", (int)ModuleInstanceState.Instantiated)]
    [InlineData("var x: number;", (int)ModuleInstanceState.Instantiated)]
    // A non-exported import makes the module uninstantiated; the `export` modifier flips the same statement back.
    [InlineData("import x = require(\"y\");", (int)ModuleInstanceState.NonInstantiated)]
    [InlineData("export import x = require(\"y\");", (int)ModuleInstanceState.Instantiated)]
    [InlineData("import y = x.z;", (int)ModuleInstanceState.NonInstantiated)]
    public void GetModuleInstanceState_ClassifiesAnAmbientModuleBody(string body, int expected)
    {
        Assert.Equal((ModuleInstanceState)expected, Binder.GetModuleInstanceState(ParseAmbientModule(body)));
    }

    [Fact]
    public void GetModuleInstanceState_FollowsAnExportAliasToItsTarget()
    {
        // `export { E }` is not instantiated on its own; the answer is the strongest state of the statements its
        // exported name resolves to inside the module block (binder.ts:422-463).
        Assert.Equal(
            ModuleInstanceState.ConstEnumOnly,
            Binder.GetModuleInstanceState(ParseAmbientModule("const enum E {}\nexport { E };")));

        Assert.Equal(
            ModuleInstanceState.NonInstantiated,
            Binder.GetModuleInstanceState(ParseAmbientModule("interface I {}\nexport { I };")));
    }

    [Fact]
    public void GetModuleInstanceState_UsesTheCallersVisitedMap()
    {
        var module = ParseAmbientModule("interface I {}");
        var visited = new Dictionary<int, ModuleInstanceState?>();

        Assert.Equal(ModuleInstanceState.NonInstantiated, Binder.GetModuleInstanceState(module, visited));

        // The module block and its interface declaration were both recorded (binder.ts:339-348).
        Assert.NotEmpty(visited);
    }

    // ---------------------------------------------------------------------------------------------
    // getContainerFlags
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void GetContainerFlags_AnswersForTheKindsBindContainerBranchesOn()
    {
        var file = Parse("function f() { { } }");

        Assert.Equal(
            ContainerFlags.IsContainer | ContainerFlags.IsControlFlowContainer | ContainerFlags.HasLocals,
            Binder.GetContainerFlags(file));

        var function = Assert.IsType<FunctionDeclaration>(Assert.Single(file.Statements!));

        // A block that is a function's body keeps its locals in the function container (binder.ts:3893-3901).
        Assert.Equal(ContainerFlags.None, Binder.GetContainerFlags(function.Body!));

        var nested = (Block)((IStatement)function.Body!.Statements![0]);
        Assert.Equal(ContainerFlags.IsBlockScopedContainer | ContainerFlags.HasLocals, Binder.GetContainerFlags(nested));
    }

    [Fact]
    public void GetContainerFlags_MakesAPropertyDeclarationAControlFlowContainer_OnlyWhenItHasAnInitializer()
    {
        var file = Parse("class C { x = 1; y: number; }");
        var @class = Assert.IsType<ClassDeclaration>(Assert.Single(file.Statements!));
        var members = @class.Members!;

        Assert.Equal(ContainerFlags.IsControlFlowContainer, Binder.GetContainerFlags((Node)members[0]));
        Assert.Equal(ContainerFlags.None, Binder.GetContainerFlags((Node)members[1]));
    }

    // ---------------------------------------------------------------------------------------------
    // isExportsOrModuleExportsOrAlias
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void IsExportsOrModuleExportsOrAlias_RecognisesModuleExports_AndRejectsAPlainIdentifier()
    {
        var file = Parse("module.exports = 1;");
        var statement = Assert.IsType<ExpressionStatement>(Assert.Single(file.Statements!));
        var assignment = Assert.IsType<BinaryExpression>(statement.Expression);

        Assert.True(Binder.IsExportsOrModuleExportsOrAlias(file, assignment.Left!));

        var plain = Parse("x;");
        var plainStatement = Assert.IsType<ExpressionStatement>(Assert.Single(plain.Statements!));

        Assert.False(Binder.IsExportsOrModuleExportsOrAlias(plain, plainStatement.Expression!));
    }

    [Fact]
    public void IsExportsOrModuleExportsOrAlias_RecognisesTheExportsIdentifier()
    {
        var file = Parse("exports;");
        var statement = Assert.IsType<ExpressionStatement>(Assert.Single(file.Statements!));

        Assert.True(Binder.IsExportsOrModuleExportsOrAlias(file, statement.Expression!));
    }
}
