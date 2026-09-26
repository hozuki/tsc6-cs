namespace TypeScript.Compiler;

// ported from: src/compiler/corePublic.ts:1-6
/// <summary>
/// Compiler version constants.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS declares these as module-level constants <c>versionMajorMinor</c> and <c>version</c>.
/// The TS comment warns that <c>configurePrerelease.ts</c> parses <c>version</c> with a regexp when
/// changing the text here; the C# port tracks the same upstream release (6.0.3) so no equivalent
/// tooling is needed yet.
/// </remarks>
internal static class VersionInfo
{
    public const string VersionMajorMinor = "6.0";

    /// <summary>The version of the TypeScript compiler release.</summary>
    public const string Version = "6.0.3";
}
