using TypeScript.Compiler.Tools;

namespace TypeScript.Compiler.Tests;

/// <summary>
/// 生成物是提交入库的（见 docs/architecture.md §11.1 D2），因此这里把「仓库里的文件与 TS 源一致」
/// 变成一条测试：TS 源更新后忘记重跑 tools/Gen 会立刻失败，而不是等到某个 baseline 对不上才发现。
/// </summary>
public class GeneratedTablesTests
{
    [Fact]
    public void GeneratedFiles_MatchTsSource()
    {
        var tsRoot = FindTsRoot();
        if (tsRoot is null)
        {
            // 只检出本仓库、没有 TS checkout 的环境下跳过（tools/ts-source.txt 指向的路径不存在）。
            return;
        }

        var repoRoot = FindRepoRoot();
        var model = TsModel.Extract(
            repoRoot,
            tsRoot,
            Path.Combine(repoRoot, "artifacts", "ts-model.test.json"),
            Generator.TypeModelSources);
        var files = Generator.Generate(model, tsRoot);

        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var path = Path.Combine(repoRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));

            Assert.True(File.Exists(path), $"{file.RelativePath} is missing — run tools/Gen");
            Assert.Equal(file.Content, File.ReadAllText(path));
        }
    }

    [Fact]
    public void TsSource_IsRecordedInConfig()
    {
        // 即使 checkout 不存在，配置文件本身也必须存在 —— 它是移植基准的唯一记录。
        var repoRoot = FindRepoRoot();
        var configPath = Path.Combine(repoRoot, "tools", "ts-source.txt");
        Assert.True(File.Exists(configPath), "tools/ts-source.txt must record the TypeScript baseline");
    }

    private static string? FindTsRoot()
    {
        var repoRoot = FindRepoRoot();
        var configPath = Path.Combine(repoRoot, "tools", "ts-source.txt");
        if (!File.Exists(configPath)) return null;

        foreach (var line in File.ReadAllLines(configPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            return Directory.Exists(trimmed) ? trimmed : null;
        }

        return null;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "tsc-cs.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("could not locate the repository root (tsc-cs.slnx)");
    }
}

/// <summary>生成表自身的语义断言（与 TS 源无关，因此在任何环境都会跑）。</summary>
public class SyntaxKindTests
{
    [Fact]
    public void Values_AreContiguousFromZero_AndAliasesIntroduceNoNewValues()
    {
        // TS 的 SyntaxKind 从 0 连续递增，`Count` 紧随最后一个真实种类之后。
        // 别名成员（FirstToken = Unknown 之类）共享目标值，因此不同值集合应恰好是 0..Count。
        var distinct = Enum.GetValues<SyntaxKind>().Cast<int>().Distinct().OrderBy(static v => v).ToArray();

        Assert.Equal(Enumerable.Range(0, (int)SyntaxKind.Count + 1), distinct);
    }

    [Fact]
    public void SentinelMembers_AreAliasesOfTheirTargets()
    {
        Assert.Equal(SyntaxKind.Unknown, SyntaxKind.FirstToken);
        Assert.Equal(SyntaxKind.EqualsToken, SyntaxKind.FirstAssignment);
        Assert.Equal(SyntaxKind.VariableStatement, SyntaxKind.FirstStatement);
        Assert.Equal(SyntaxKind.DebuggerStatement, SyntaxKind.LastStatement);
        Assert.Equal(SyntaxKind.BreakKeyword, SyntaxKind.FirstKeyword);
        Assert.Equal(SyntaxKind.DeferKeyword, SyntaxKind.LastKeyword);
        Assert.Equal(SyntaxKind.JSDoc, SyntaxKind.JSDocComment);
    }

    [Fact]
    public void SentinelRanges_AreWellOrdered()
    {
        Assert.True((int)SyntaxKind.FirstToken <= (int)SyntaxKind.LastToken);
        Assert.True((int)SyntaxKind.FirstNode < (int)SyntaxKind.FirstStatement);
        Assert.True((int)SyntaxKind.FirstLiteralToken <= (int)SyntaxKind.LastLiteralToken);
        Assert.True((int)SyntaxKind.FirstTemplateToken <= (int)SyntaxKind.LastTemplateToken);
        Assert.True((int)SyntaxKind.FirstPunctuation <= (int)SyntaxKind.LastPunctuation);
    }

    [Fact]
    public void KnownValues_MatchTsTable()
    {
        // 抽样固定住数值，任何解析顺序错误都会被这里挡住。
        Assert.Equal(0, (int)SyntaxKind.Unknown);
        Assert.Equal(1, (int)SyntaxKind.EndOfFileToken);
        Assert.Equal(9, (int)SyntaxKind.NumericLiteral);
        Assert.Equal(15, (int)SyntaxKind.NoSubstitutionTemplateLiteral);
        Assert.Equal(18, (int)SyntaxKind.TemplateTail);
        Assert.Equal(19, (int)SyntaxKind.OpenBraceToken);
    }
}

public class FlagsEnumTests
{
    [Fact]
    public void NodeFlags_CompositeMembersAreUnionsOfTheirParts()
    {
        Assert.Equal(NodeFlags.Const | NodeFlags.Using, NodeFlags.AwaitUsing);
        Assert.Equal(NodeFlags.Let | NodeFlags.Const | NodeFlags.Using, NodeFlags.BlockScoped);
        Assert.Equal(
            NodeFlags.PossiblyContainsDynamicImport | NodeFlags.PossiblyContainsImportMeta,
            NodeFlags.PermanentlySetIncrementalFlags);
    }

    [Fact]
    public void NodeFlags_ValuesFitTheirUnderlyingType()
    {
        // NodeFlags 用到 1 << 30，必须落在 uint 内且不是负数。
        Assert.True((uint)NodeFlags.Unreachable > 0);
        Assert.Equal(1u << 30, (uint)NodeFlags.Unreachable);
    }

    [Fact]
    public void ModifierFlags_ModifierIsAllWithoutDecorator()
    {
        const uint decoratorBit = 1u << 15;

        Assert.Equal(0u, (uint)ModifierFlags.Modifier & decoratorBit);
        Assert.Equal((uint)ModifierFlags.All & ~decoratorBit, (uint)ModifierFlags.Modifier);
        Assert.Equal(ModifierFlags.Export | ModifierFlags.Default, ModifierFlags.ExportDefault);
        Assert.Equal(
            ModifierFlags.Public | ModifierFlags.Private | ModifierFlags.Protected,
            ModifierFlags.AccessibilityModifier);
    }

    [Fact]
    public void TypeFlags_UsesBit31WithoutOverflowing()
    {
        // 1 << 31 是 int 溢出点；生成器把底层类型定为 uint 就为了这个。
        Assert.Equal(typeof(uint), Enum.GetUnderlyingType(typeof(TypeFlags)));
        Assert.Equal(1u << 31, (uint)TypeFlags.Reserved3);
    }

    [Fact]
    public void TransformFlags_UsesBit31WithoutOverflowing()
    {
        Assert.Equal(typeof(uint), Enum.GetUnderlyingType(typeof(TransformFlags)));
        Assert.Equal(1u << 31, (uint)TransformFlags.HasComputedFlags);
    }

    [Fact]
    public void SyntaxKind_IsNotAFlagsEnum()
    {
        Assert.Null(typeof(SyntaxKind).GetCustomAttributes(typeof(FlagsAttribute), inherit: false).FirstOrDefault());
        Assert.Equal(typeof(int), Enum.GetUnderlyingType(typeof(SyntaxKind)));
    }
}
