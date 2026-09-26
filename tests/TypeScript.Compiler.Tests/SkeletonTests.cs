namespace TypeScript.Compiler.Tests;

/// <summary>
/// 解决方案骨架的冒烟测试：确保测试项目能引用编译器程序集。
/// </summary>
public class SkeletonTests
{
    [Fact]
    public void CompilerAssembly_IsReferenceable()
    {
        Assert.NotNull(typeof(AssemblyMarker).Assembly);
    }
}
