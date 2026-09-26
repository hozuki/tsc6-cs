namespace TypeScript.Compiler.Tests;

/// <summary>
/// scanner 的数据表（由 <c>tools/Gen</c> 从 <c>scanner.ts</c> 的字面量求值生成）。
/// </summary>
/// <remarks>
/// 这些表本身没有"行为"可测，所以这里固定的是它们的**形状与自洽性**：条数、排序、以及生成器必须正确处理的
/// 几处边界（计算属性名、spread、赋值跟随）。数值期望值取自 TS 6.0.3 <c>lib/typescript.js</c> 的运行时表。
/// </remarks>
public class ScannerTablesTests
{
    [Fact]
    public void TextToKeyword_HoldsEveryReservedWord()
    {
        var keywords = ScannerTables.TextToKeyword;

        Assert.Equal(84, keywords.Count);
        Assert.Equal(("abstract", SyntaxKind.AbstractKeyword), keywords[0]);
        Assert.Contains(("class", SyntaxKind.ClassKeyword), keywords);
        // TS writes this key as `["" + "constructor"]`, to keep it off the object's prototype.
        Assert.Contains(("constructor", SyntaxKind.ConstructorKeyword), keywords);
        Assert.Equal(("of", SyntaxKind.OfKeyword), keywords[^1]);
    }

    [Fact]
    public void TextToToken_IsTheKeywordsFollowedByThePunctuators()
    {
        var tokens = ScannerTables.TextToToken;

        // TS: `new Map(Object.entries({ ...textToKeywordObj, "{": …, … }))`.
        Assert.Equal(145, tokens.Count);
        Assert.Equal(ScannerTables.TextToKeyword, tokens.Take(ScannerTables.TextToKeyword.Count));

        Assert.Contains(("{", SyntaxKind.OpenBraceToken), tokens);
        Assert.Contains(("=>", SyntaxKind.EqualsGreaterThanToken), tokens);
        Assert.Contains(("**=", SyntaxKind.AsteriskAsteriskEqualsToken), tokens);
    }

    [Fact]
    public void CharCodeToRegExpFlag_SpellsOutEveryFlagCharacter()
    {
        Assert.Equal(8, ScannerTables.CharCodeToRegExpFlag.Count);
        Assert.Contains((CharacterCodes.d, RegularExpressionFlags.HasIndices), ScannerTables.CharCodeToRegExpFlag);
        Assert.Contains((CharacterCodes.y, RegularExpressionFlags.Sticky), ScannerTables.CharCodeToRegExpFlag);
    }

    [Fact]
    public void RegExpFlagToFirstAvailableTarget_ResolvesTheLanguageFeatureTable()
    {
        // TS stores the target indirectly (`LanguageFeatureMinimumTarget.RegularExpressionFlagsHasIndices`); the
        // generator resolves it, so what has to hold here is that the values are real targets.
        Assert.Equal(5, ScannerTables.RegExpFlagToFirstAvailableTarget.Count);

        var hasIndices = ScannerTables.RegExpFlagToFirstAvailableTarget.Single(e => e.Flag == RegularExpressionFlags.HasIndices);
        Assert.Equal(ScriptTarget.ES2022, hasIndices.Target);

        var unicode = ScannerTables.RegExpFlagToFirstAvailableTarget.Single(e => e.Flag == RegularExpressionFlags.Unicode);
        Assert.Equal(ScriptTarget.ES2015, unicode.Target);

        // Every flag in the table is one the scanner can actually set.
        Assert.All(
            ScannerTables.RegExpFlagToFirstAvailableTarget,
            static entry => Assert.Contains(entry.Flag, ScannerTables.CharCodeToRegExpFlag.Select(static e => e.Flag)));
    }

    [Theory]
    [InlineData(740)]
    [InlineData(856)]
    [InlineData(1320)]
    [InlineData(1538)]
    public void UnicodeRangeTables_AreSortedPairsOfBounds(int expectedCount)
    {
        var table = expectedCount switch
        {
            740 => ScannerTables.UnicodeES5IdentifierStart,
            856 => ScannerTables.UnicodeES5IdentifierPart,
            1320 => ScannerTables.UnicodeESNextIdentifierStart,
            _ => ScannerTables.UnicodeESNextIdentifierPart,
        };

        Assert.Equal(expectedCount, table.Count);
        Assert.Equal(0, table.Count % 2); // start/end pairs

        for (var i = 0; i < table.Count; i += 2)
        {
            Assert.True(table[i] <= table[i + 1], $"range {i / 2} is inverted");
            if (i > 0) Assert.True(table[i] > table[i - 1], $"range {i / 2} starts before the previous one ends");
        }
    }

    [Fact]
    public void UnicodePropertyTables_AreCarriedThrough()
    {
        Assert.Equal(("General_Category", "General_Category"), ScannerTables.NonBinaryUnicodeProperties[0]);
        Assert.Contains(("scx", "Script_Extensions"), ScannerTables.NonBinaryUnicodeProperties);

        Assert.Contains("ASCII", ScannerTables.BinaryUnicodeProperties);
        Assert.Contains("White_Space", ScannerTables.BinaryUnicodeProperties);
        Assert.Contains("RGI_Emoji", ScannerTables.BinaryUnicodePropertiesOfStrings);
    }

    [Fact]
    public void ValuesOfNonBinaryUnicodeProperties_FollowTheReassignmentTsDoesAfterTheLiteral()
    {
        var values = ScannerTables.ValuesOfNonBinaryUnicodeProperties;
        Assert.Equal(3, values.Count);

        var script = values.Single(static e => e.Property == "Script");
        var scriptExtensions = values.Single(static e => e.Property == "Script_Extensions");

        Assert.Contains("Latin", script.Values);
        // TS: `valuesOfNonBinaryUnicodeProperties.Script_Extensions = valuesOfNonBinaryUnicodeProperties.Script`.
        Assert.Equal(script.Values, scriptExtensions.Values);
    }
}
