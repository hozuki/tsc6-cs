using System.Text.RegularExpressions;

namespace TypeScript.Compiler.Tests;

/// <summary>
/// The stubs are the port's progress marker (docs/architecture.md §15.8): tools/Gen emits one per factory member the
/// hand-written half does not provide yet, and `NodeFactoryEmitter.s_implemented` is the switch that removes it.
/// </summary>
/// <remarks>
/// This pins the list, which catches the two ways the marker can drift: implementing a member without adding its name to
/// `s_implemented` would leave a stub next to a real method (a CS0111, but only if the signatures collide), and a
/// generator change that starts stubbing something new would otherwise go unnoticed. The expected list is the set of
/// members waiting on M4d/M5/M7/M4e — update it as those land.
/// </remarks>
public class NodeFactoryStubTests
{
    private static readonly string[] s_expected =
    [
        "createBaseGeneratedIdentifier",
        "createBaseGeneratedPrivateIdentifier",
        "createBinaryExpression",
        "createCallBinding",
        "createIsNotTypeCheck",
        "createLoopVariable",
        "createTempVariable",
        "createTypeCheck",
        "createUniqueName",
        "createUniquePrivateName",
        "updateBinaryExpression",
    ];

    [Fact]
    public void TheGeneratedHalf_StubsExactlyTheMembersThatWaitOnLaterBatches()
    {
        var path = Path.Combine(
            RepoRoot(),
            "src",
            "TypeScript.Compiler",
            "Parser",
            "NodeFactory.Generated.cs");

        var stubbed = Regex.Matches(File.ReadAllText(path), @"the port has not reached `(\w+)` yet")
            .Select(match => match.Groups[1].Value)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(s_expected, stubbed);
    }

    /// <summary>
    /// The stubs are the only members that throw; everything else the ported factory exposes has a body.
    /// </summary>
    [Fact]
    public void TheGeneratedHalf_ThrowsNowhereElse()
    {
        var path = Path.Combine(
            RepoRoot(),
            "src",
            "TypeScript.Compiler",
            "Parser",
            "NodeFactory.Generated.cs");

        var source = File.ReadAllText(path);

        Assert.Equal(
            s_expected.Length,
            Regex.Matches(source, @"throw new NotImplementedException").Count);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "tsc-cs.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("could not find the repository root");
    }
}
