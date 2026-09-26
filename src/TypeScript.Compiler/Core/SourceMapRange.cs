namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:3836-3842 — interface SourceMapRange
/// <summary>
/// A text range to emit source-map information for, optionally pointing at a different source.
/// </summary>
/// <remarks>
/// PORT-NOTE: like <see cref="TextRange"/> and <see cref="CommentRange"/>, TS writes these as object literals over
/// <c>TextRange</c>; the port needs a constructible type. <c>source</c> is optional in TS and is a nullable property
/// here, so a range that maps within its own file simply leaves it unset.
/// </remarks>
internal sealed class SourceMapRange : ISourceMapRange
{
    public required int Pos { get; set; }

    public required int End { get; set; }

    public ISourceMapSource? Source { get; set; }
}
