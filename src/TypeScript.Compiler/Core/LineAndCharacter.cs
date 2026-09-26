namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:7643-7646 — interface LineAndCharacter
/// <summary>
/// A zero-based line and character pair within a source file.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS declares <c>LineAndCharacter</c> as an interface and builds instances as object literals; the
/// port needs a constructible type (see <see cref="DiagnosticMessage"/> for the same situation).
/// </remarks>
internal sealed class LineAndCharacter : ILineAndCharacter
{
    public int Line { get; set; }

    public int Character { get; set; }
}
