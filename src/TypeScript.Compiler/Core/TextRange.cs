namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:3811-3814 — interface TextRange
/// <summary>A start and end offset into a text.</summary>
/// <remarks>
/// PORT-NOTE: TS builds these as object literals on top of the <c>TextRange</c> interface; the port needs a
/// constructible type (the same situation as <see cref="LineAndCharacter"/>). It is used where TS's <c>TextRange</c>
/// appears outside a node, such as a comment directive's range.
/// </remarks>
internal sealed class TextRange : ITextRange
{
    public required int Pos { get; init; }

    public required int End { get; init; }
}
