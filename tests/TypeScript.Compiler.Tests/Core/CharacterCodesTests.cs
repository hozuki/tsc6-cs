namespace TypeScript.Compiler.Tests;

// 对应 src/compiler/types.ts:7841-7979
public class CharacterCodesTests
{
    [Theory]
    [InlineData(nameof(CharacterCodes.EOF), -1)]
    [InlineData(nameof(CharacterCodes.NullCharacter), 0x00)]
    [InlineData(nameof(CharacterCodes.MaxAsciiCharacter), 0x7F)]
    [InlineData(nameof(CharacterCodes.LineFeed), 0x0A)]
    [InlineData(nameof(CharacterCodes.CarriageReturn), 0x0D)]
    [InlineData(nameof(CharacterCodes.LineSeparator), 0x2028)]
    [InlineData(nameof(CharacterCodes.ParagraphSeparator), 0x2029)]
    [InlineData(nameof(CharacterCodes.Space), 0x20)]
    [InlineData(nameof(CharacterCodes.NonBreakingSpace), 0xA0)]
    [InlineData(nameof(CharacterCodes.IdeographicSpace), 0x3000)]
    [InlineData(nameof(CharacterCodes.ReplacementCharacter), 0xFFFD)]
    [InlineData(nameof(CharacterCodes.Underscore), 0x5F)]
    [InlineData(nameof(CharacterCodes.Dollar), 0x24)]
    [InlineData(nameof(CharacterCodes._0), 0x30)]
    [InlineData(nameof(CharacterCodes._9), 0x39)]
    [InlineData(nameof(CharacterCodes.a), 0x61)]
    [InlineData(nameof(CharacterCodes.z), 0x7A)]
    [InlineData(nameof(CharacterCodes.A), 0x41)]
    [InlineData(nameof(CharacterCodes.Z), 0x5A)]
    [InlineData(nameof(CharacterCodes.Ampersand), 0x26)]
    [InlineData(nameof(CharacterCodes.Backtick), 0x60)]
    [InlineData(nameof(CharacterCodes.Slash), 0x2F)]
    [InlineData(nameof(CharacterCodes.Backslash), 0x5C)]
    [InlineData(nameof(CharacterCodes.ByteOrderMark), 0xFEFF)]
    [InlineData(nameof(CharacterCodes.Tab), 0x09)]
    [InlineData(nameof(CharacterCodes.VerticalTab), 0x0B)]
    public void Member_HasExpectedCode(string memberName, int expected)
    {
        var actual = (int)typeof(CharacterCodes)
            .GetField(memberName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!
            .GetRawConstantValue()!;

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void AllMembers_HaveUniqueCodes_ExceptDocumentedAliases()
    {
        var byValue = typeof(CharacterCodes)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .GroupBy(f => (int)f.GetRawConstantValue()!)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key:X}: {string.Join(", ", g.Select(f => f.Name))}")
            .ToArray();

        // TS 的 CharacterCodes 中每个代码唯一；如有重复说明移植写错了。
        Assert.Empty(byValue);
    }
}
