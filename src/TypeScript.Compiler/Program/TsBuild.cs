namespace TypeScript.Compiler;

// ported from: src/compiler/tsbuild.ts:170-182
/// <summary>
/// The one <c>tsbuild.ts</c> helper the program side calls — <c>resolveConfigFileProjectName</c>, which
/// <c>program.ts</c>'s <c>resolveProjectReferencePath</c> delegates to.
/// </summary>
/// <remarks>
/// PORT-NOTE: <c>tsbuild.ts</c> is the incremental builder's module (docs/architecture.md §15.15: M8), and this is the
/// only member of it the program side reaches (<c>Program.ResolveProjectReferencePath</c>, its caller at
/// <c>Program/Program.ProjectReferences.cs</c>). Until M8 lands, that member throws
/// <see cref="BuildNotLanded"/> — the builder-side counterpart of <see cref="Program.EmitNotLanded"/>, the same loud,
/// named failure (the reference's member name and line are in the message), never a re-implementation of the
/// five-line body under a second name and never a silent fallback.
/// <para>
/// PORT-NOTE: the class is <c>partial</c> and lives here so that M8's <c>tsbuild.ts</c> slice <em>extends</em> it —
/// the ownership rules of §15.15 ("one name, one owner, one home") require grepping for the name before declaring it,
/// and a second, non-partial <c>TsBuild</c> would be CS0101. The module's status model (<c>UpToDateStatus</c> and its
/// arms, <c>tsbuild.ts</c>'s remainder) arrives with that slice.
/// </para>
/// </remarks>
internal static partial class TsBuild
{
    /// <summary>ported from: src/compiler/tsbuild.ts:177-182 — <c>resolveConfigFileProjectName</c>.</summary>
    internal static string ResolveConfigFileProjectName(string project)
        => throw BuildNotLanded("resolveConfigFileProjectName (tsbuild.ts:177)");

    /// <summary>The failure every member that needs M8 reports, naming the reference's member and line.</summary>
    internal static NotSupportedException BuildNotLanded(string what)
        => new($"{what} is not ported yet: the incremental builder is M8's (docs/architecture.md §15.15).");
}
