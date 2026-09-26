namespace TypeScript.Compiler.Tests;

/// <summary>
/// <see cref="Flags"/> 为 C# enum 缺失的一元 <c>~</c> 提供 TS 的 <c>flags &amp; ~mask</c> 语义。
/// </summary>
public class FlagsHelperTests
{
    [Fact]
    public void Clear_RemovesTheMaskBitsAndKeepsTheRest()
    {
        var value = ModifierFlags.Export | ModifierFlags.Decorator | ModifierFlags.Async;
        var cleared = Flags.Clear(value, ModifierFlags.Decorator);

        Assert.Equal(ModifierFlags.Export | ModifierFlags.Async, cleared);
        Assert.False(Flags.HasAny(cleared, ModifierFlags.Decorator));
        Assert.True(Flags.HasAny(cleared, ModifierFlags.Export));
    }

    [Fact]
    public void Clear_AgreesWithTheGeneratedCompositeMember()
    {
        // 生成的 `ModifierFlags.Modifier` 是 TS 的 `All & ~Decorator`（在成员初始化式里写成
        // `(uint)All & ~(uint)Decorator`）；helper 必须给出同一个值。
        Assert.Equal(ModifierFlags.Modifier, Flags.Clear(ModifierFlags.All, ModifierFlags.Decorator));
    }

    [Fact]
    public void Clear_AgreesWithEveryGeneratedExcludesMember()
    {
        // SymbolFlags 里有一整套 `…Excludes = X & ~(Y | Z)` 形式的成员，逐个与 helper 对拍。
        Assert.Equal(
            SymbolFlags.FunctionScopedVariableExcludes,
            Flags.Clear(SymbolFlags.Value, SymbolFlags.FunctionScopedVariable));
        Assert.Equal(
            SymbolFlags.FunctionExcludes,
            Flags.Clear(SymbolFlags.Value, SymbolFlags.Function | SymbolFlags.ValueModule | SymbolFlags.Class));
        Assert.Equal(
            SymbolFlags.ClassExcludes,
            Flags.Clear(SymbolFlags.Value | SymbolFlags.Type, SymbolFlags.ValueModule | SymbolFlags.Interface | SymbolFlags.Function));
        Assert.Equal(
            SymbolFlags.RegularEnumExcludes,
            Flags.Clear(SymbolFlags.Value | SymbolFlags.Type, SymbolFlags.RegularEnum | SymbolFlags.ValueModule));
    }

    [Fact]
    public void Has_TestsIndividualBits()
    {
        var value = ModifierFlags.Export | ModifierFlags.Async;

        Assert.True(Flags.HasAny(value, ModifierFlags.Export));
        Assert.True(Flags.HasAny(value, ModifierFlags.Async));
        Assert.False(Flags.HasAny(value, ModifierFlags.Decorator));
        // 「任一位命中」——这正是 TS 里 (flags & X) !== 0 的语义
        Assert.True(Flags.HasAny(value, ModifierFlags.Export | ModifierFlags.Decorator));
        // 而 HasAll 要求全部命中
        Assert.False(Flags.HasAll(value, ModifierFlags.Export | ModifierFlags.Decorator));
    }

    [Fact]
    public void Set_AddsBits()
    {
        Assert.Equal(ModifierFlags.Export | ModifierFlags.Async, Flags.Set(ModifierFlags.Export, ModifierFlags.Async));
    }

    [Fact]
    public void WorksForSixtyFourBitEnums()
    {
        var value = WideFlags.High | WideFlags.Low;
        var cleared = Flags.Clear(value, WideFlags.High);

        Assert.Equal(WideFlags.Low, cleared);
        Assert.True(Flags.HasAny(value, WideFlags.High));
        Assert.False(Flags.HasAny(cleared, WideFlags.High));
    }

    [Fact]
    public void SymbolFlags_AllIsEveryBitSet()
    {
        // TS 写作 `All = -1`；生成器把它渲染为 unchecked((uint)-1)。
        Assert.Equal(0xFFFFFFFFu, (uint)SymbolFlags.All);
    }

    [Flags]
    private enum WideFlags : ulong
    {
        None = 0,
        Low = 1ul << 3,
        High = 1ul << 40,
    }
}
