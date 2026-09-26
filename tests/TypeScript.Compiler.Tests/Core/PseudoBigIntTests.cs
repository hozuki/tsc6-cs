namespace TypeScript.Compiler.Tests;

/// <summary>
/// BigInt 字面量文本 → 十进制数字串（<c>utilities.ts</c> 的 <c>parsePseudoBigInt</c>）。
/// 期望值取自 TS 6.0.3 <c>lib/typescript.js</c> 里的同名导出。
/// </summary>
public class PseudoBigIntTests
{
    [Theory]
    [InlineData("0x1Fn", "31")]
    [InlineData("0b101n", "5")]
    [InlineData("0o17n", "15")]
    [InlineData("123n", "123")]
    [InlineData("0n", "0")]
    [InlineData("000n", "0")]
    [InlineData("0123n", "123")]
    [InlineData("0xFFn", "255")]
    [InlineData("0xFFFFFFFFFFFFFFFFn", "18446744073709551615")]
    [InlineData("0x123456789abcdefn", "81985529216486895")]
    [InlineData("0b1111000011110000111100001111n", "252645135")]
    public void Parse_WritesTheValueInDecimal(string literal, string expected)
        => Assert.Equal(expected, PseudoBigInt.Parse(literal));
}
