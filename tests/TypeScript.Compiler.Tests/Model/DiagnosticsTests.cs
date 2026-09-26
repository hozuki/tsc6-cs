using System.Reflection;

namespace TypeScript.Compiler.Tests;

/// <summary>
/// 诊断表（由 <c>diagnosticMessages.json</c> 生成）。
/// </summary>
/// <remarks>
/// 成员名是 TS 自己对消息文本做的名字改写（<c>scripts/processDiagnosticMessages.mjs</c> 的
/// <c>convertPropertyName</c>），而移植代码里写的正是 <c>Diagnostics.Unexpected_character</c> 这类名字 ——
/// 改写规则一旦走偏，scanner/parser/checker 的调用点会全线编译不过。所以这里把规则的每个分支都固定住。
/// </remarks>
public class DiagnosticsTests
{
    private static IReadOnlyList<(string Name, DiagnosticMessage Message)> All()
        => typeof(Diagnostics)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(static field => field.FieldType == typeof(DiagnosticMessage))
            .Select(static field => (field.Name, (DiagnosticMessage)field.GetValue(null)!))
            .ToList();

    [Fact]
    public void MangledNames_FollowTheTsRule()
    {
        // 普通句子：空格变下划线，结尾的句点只留一个下划线然后被去掉。
        Assert.Equal("Unterminated_string_literal", nameof(Diagnostics.Unterminated_string_literal));
        Assert.Equal(1002, Diagnostics.Unterminated_string_literal.Code);
        Assert.Equal("Unterminated string literal.", Diagnostics.Unterminated_string_literal.Message);

        // 前导下划线在"后面跟数字"时保留：`'{0}' expected.` → `_0_expected`。
        Assert.Equal("_0_expected", nameof(Diagnostics._0_expected));
        Assert.Equal("'{0}' expected.", Diagnostics._0_expected.Message);
        Assert.Equal(DiagnosticCategory.Error, Diagnostics._0_expected.Category);

        // `*`、`/`、`:` 有专门拼写（分别是 _Asterisk、_Slash、_Colon）。
        Assert.Equal("Asterisk_Slash_expected", nameof(Diagnostics.Asterisk_Slash_expected));
        Assert.Equal("'*/' expected.", Diagnostics.Asterisk_Slash_expected.Message);

        var all = All();
        Assert.Contains(all, static e => e.Name.Contains("_Colon", StringComparison.Ordinal));
        Assert.DoesNotContain(all, static e => e.Name.Contains("__", StringComparison.Ordinal));
        Assert.DoesNotContain(all, static e => e.Name.EndsWith('_'));
    }

    [Fact]
    public void EveryEntry_HasAConsistentKeyAndAUniqueCode()
    {
        var all = All();
        Assert.True(all.Count > 2000, $"expected the full table, found {all.Count} entries");

        var codes = new HashSet<int>();
        foreach (var (name, message) in all)
        {
            // TS: key = name.slice(0, 100) + "_" + code.
            var prefix = name.Length <= 100 ? name : name[..100];
            Assert.Equal($"{prefix}_{message.Code}", message.Key);
            Assert.False(string.IsNullOrEmpty(message.Message));
            Assert.True(codes.Add(message.Code), $"diagnostic code {message.Code} appears twice");
        }
    }

    [Fact]
    public void LongNames_AreTruncatedInTheLocalizationKey()
    {
        var all = All();
        var truncated = all.Where(static e => e.Name.Length > 100).ToList();

        Assert.NotEmpty(truncated);
        Assert.All(truncated, static e => Assert.Equal(100, e.Message.Key.Length - e.Message.Code.ToString().Length - 1));
    }

    [Fact]
    public void OptionalMarkers_AreCarriedThrough()
    {
        Assert.True(Diagnostics._0_is_declared_but_its_value_is_never_read.ReportsUnnecessary is true);
        Assert.Null(Diagnostics.Unterminated_string_literal.ReportsUnnecessary);

        Assert.True(Diagnostics._0_is_deprecated.ReportsDeprecated is true);
        Assert.True(Diagnostics.Call_signature_return_types_0_and_1_are_incompatible.ElidedInCompatabilityPyramid is true);
    }

    [Fact]
    public void NamesReferencedByTheTsScanner_AllExist()
    {
        // scanner.ts 里的 Diagnostics.* 引用（68 个）必须都能在这里找到 —— 命名规则走偏时这条最先失败。
        Assert.Equal(1005, Diagnostics._0_expected.Code);
        Assert.Equal(1010, Diagnostics.Asterisk_Slash_expected.Code);
        Assert.Equal(1127, Diagnostics.Invalid_character.Code);
        Assert.Equal(1161, Diagnostics.Unterminated_regular_expression_literal.Code);
        Assert.Equal(1369, Diagnostics.Did_you_mean_0.Code);
        Assert.Equal(1490, Diagnostics.File_appears_to_be_binary.Code);
        Assert.Equal(18026, Diagnostics.can_only_be_used_at_the_start_of_a_file.Code);
    }
}
