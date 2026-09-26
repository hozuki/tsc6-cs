namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:8068-8082
/// <summary>
/// Well-known file extensions. Equivalent to TS's <c>Extension</c> const enum.
/// </summary>
/// <remarks>
/// PORT-NOTE: this is the first piece of the model layer to land, because <c>path.ts</c> (Layer 0)
/// depends on it through <c>getDeclarationFileExtension</c> — which TS itself declares in <c>parser.ts</c>
/// and reaches across layers for. Declaring it here keeps the port's Layer 0 self-contained instead of
/// introducing a stub. The remaining <c>types.ts</c> content (SyntaxKind, Node, …) lands in milestone M2.
/// <para>
/// TS's is a <c>const enum</c> whose members are *string* values; C# enums cannot carry strings, so the
/// port uses constants and keeps the <c>Extension</c> name for call-site parity.
/// </para>
/// </remarks>
internal static class Extension
{
    public const string Ts = ".ts";
    public const string Tsx = ".tsx";
    public const string Dts = ".d.ts";
    public const string Js = ".js";
    public const string Jsx = ".jsx";
    public const string Json = ".json";
    public const string TsBuildInfo = ".tsbuildinfo";
    public const string Mjs = ".mjs";
    public const string Mts = ".mts";
    public const string Dmts = ".d.mts";
    public const string Cjs = ".cjs";
    public const string Cts = ".cts";
    public const string Dcts = ".d.cts";
}
