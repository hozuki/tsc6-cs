namespace TypeScript.Compiler.Tests;

/// <summary>
/// Acceptance for the generated <see cref="NodeTests"/> predicates (<c>factory/nodeTests.ts</c>).
/// </summary>
public class NodeTestsTests
{
    /// <summary>
    /// The source file has 227 exported predicate functions; the generator refuses any body it does not recognise,
    /// so a silent drop would show up here.
    /// </summary>
    [Fact]
    public void Generated_ContainsEveryPredicateFromTheTypeScriptSource()
    {
        var methods = typeof(NodeTests)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(m => m.Name.StartsWith("Is", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(227, methods.Count);
        Assert.All(methods, m =>
        {
            Assert.Equal(typeof(bool), m.ReturnType);
            var parameter = Assert.Single(m.GetParameters());
            Assert.Equal(typeof(Node), parameter.ParameterType);
        });
    }

    [Fact]
    public void Predicate_TestsTheKindNotTheClass()
    {
        var identifier = new Identifier();
        Assert.True(NodeTests.IsIdentifier(identifier));
        Assert.False(NodeTests.IsStringLiteral(identifier));

        var stringLiteral = new StringLiteral();
        Assert.True(NodeTests.IsStringLiteral(stringLiteral));
        Assert.False(NodeTests.IsIdentifier(stringLiteral));
    }

    /// <summary>
    /// All keywords share one C# class, so a predicate must compare kinds — <c>isExportModifier</c> tests
    /// <c>ExportKeyword</c> even though the port has no <c>ExportModifier</c> type.
    /// </summary>
    [Fact]
    public void KeywordPredicates_TellApartKindsThatShareAClass()
    {
        var exportKeyword = new KeywordToken(SyntaxKind.ExportKeyword);
        var defaultKeyword = new KeywordToken(SyntaxKind.DefaultKeyword);

        Assert.True(NodeTests.IsExportModifier(exportKeyword));
        Assert.False(NodeTests.IsDefaultModifier(exportKeyword));
        Assert.True(NodeTests.IsDefaultModifier(defaultKeyword));
        Assert.False(NodeTests.IsExportModifier(defaultKeyword));
    }

    /// <summary>
    /// <c>isModuleExportName</c> is the one predicate that accepts two kinds; the generator joins them with
    /// <c>||</c>.
    /// </summary>
    [Fact]
    public void IsModuleExportName_AcceptsBothIdentifierAndStringLiteral()
    {
        Assert.True(NodeTests.IsModuleExportName(new Identifier()));
        Assert.True(NodeTests.IsModuleExportName(new StringLiteral()));
        Assert.False(NodeTests.IsModuleExportName(new NumericLiteral()));
    }

    /// <summary>
    /// Not every <c>SyntaxKind</c> has a predicate (there is no <c>isSyntaxKind</c> for trivia), but every
    /// predicate must name a kind the enum actually has — this catches a typo in the generated table.
    /// </summary>
    [Fact]
    public void EveryPredicateKindExists()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "TypeScript.Compiler", "Parser", "NodeTests.cs"));
        var kinds = System.Text.RegularExpressions.Regex.Matches(source, @"SyntaxKind\.(\w+)")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        Assert.NotEmpty(kinds);
        Assert.All(kinds, kind =>
        {
            Assert.True(
                Enum.TryParse<SyntaxKind>(kind, out var parsed) && Enum.IsDefined(parsed),
                $"'{kind}' is not a SyntaxKind member");
        });
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "tsc-cs.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
