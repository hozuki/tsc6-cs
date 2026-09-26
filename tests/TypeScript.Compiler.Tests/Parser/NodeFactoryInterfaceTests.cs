using System.Reflection;

namespace TypeScript.Compiler.Tests;

/// <summary>
/// The factory's contract with <c>INodeFactory</c> (docs/architecture.md §15.9).
/// </summary>
/// <remarks>
/// Until M4d this file pinned the list of generator-emitted stubs — the members the hand-written half had not reached
/// yet. Those are gone (the generated half now declares nothing that throws), so the marker moved to what is left of the
/// interface itself: the members <c>INodeFactory</c> declares that <c>NodeFactory</c> does not provide, which is a
/// shorter and more useful list than "which methods are stubs".
/// <para>
/// Attaching <c>INodeFactory</c> to <c>NodeFactory</c> is what would enforce this at compile time, and it is the step
/// that stays open: the six <c>getName</c>-family members need <c>getNonAssignedNameOfDeclaration</c> (and with it the
/// JS-file assignment analysis) from M5, and <c>CreateNodeArray</c>'s constraint is deliberately wider than the
/// interface's (<c>INode</c> against <c>Node</c>, §12.9), which C# does not accept as an implementation.
/// </para>
/// </remarks>
public class NodeFactoryInterfaceTests
{
    /// <summary>The interface members the factory does not provide, with the reason each one is missing.</summary>
    private static readonly string[] s_gaps =
    [
        // `baseNodeFactory.ts` is not ported at all (§15.2): C# has one `Node` type per assembly, so there is nothing
        // for this slot to hold.
        "BaseFactory",

        // `getName` needs `getNonAssignedNameOfDeclaration`, whose `exports.x =` / `module.exports.x =` analysis is
        // `getAssignmentDeclarationKind` — utilities.ts, arriving with M5.
        "GetDeclarationName",
        "GetExportName",
        "GetExternalModuleOrNamespaceExportName",
        "GetInternalName",
        "GetLocalName",
        "GetNamespaceMemberName",
    ];

    [Fact]
    public void TheFactory_ProvidesEverythingTheInterfaceDeclaresExceptTheNamedGaps()
    {
        var provided = typeof(NodeFactory)
            .GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Select(member => member.Name)
            .ToHashSet(StringComparer.Ordinal);

        var missing = typeof(INodeFactory)
            .GetMembers()
            .Where(member => member switch
            {
                PropertyInfo => true,
                MethodInfo { IsSpecialName: false } => true,
                _ => false,
            })
            .Select(member => member.Name)
            .Distinct()
            .Where(name => !provided.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(s_gaps, missing);
    }

    [Fact]
    public void TheGeneratedHalf_DeclaresNoStubs()
    {
        // The stub mechanism is what M4d retired: every member the generator could not emit is now hand-written and
        // registered in `NodeFactoryEmitter.s_implemented`, so the partial class carries no `NotImplementedException`.
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "src",
            "TypeScript.Compiler",
            "Parser",
            "NodeFactory.Generated.cs"));

        Assert.DoesNotContain("NotImplementedException", source, StringComparison.Ordinal);
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
