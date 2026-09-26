namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:4483-4486 — interface CommentDirective
/// <summary>A <c>// @ts-expect-error</c> or <c>// @ts-ignore</c> directive found by the scanner.</summary>
/// <remarks>
/// PORT-NOTE: TS writes these as object literals; the port needs a constructible type. The generated interface
/// declares <c>range</c> as the empty <c>ITextRange</c>, so the concrete <see cref="TextRange"/> is bridged onto it.
/// </remarks>
internal sealed class CommentDirective : ICommentDirective
{
    public required TextRange Range { get; set; }

    public required CommentDirectiveType Type { get; set; }

    ITextRange? ICommentDirective.Range
    {
        get => Range;
        set => Range = (TextRange)value!;
    }
}
