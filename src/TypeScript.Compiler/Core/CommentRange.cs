namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:3870-3875 — `CommentKind` and `interface CommentRange`
/// <summary>
/// A comment's extent, as produced by <see cref="ScannerUtilities.GetLeadingCommentRanges"/> and
/// <see cref="ScannerUtilities.GetTrailingCommentRanges"/>.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS's <c>CommentKind</c> is the type alias
/// <c>SyntaxKind.SingleLineCommentTrivia | SyntaxKind.MultiLineCommentTrivia</c>, which has no C# counterpart, so
/// <see cref="Kind"/> is a <see cref="SyntaxKind"/> restricted to those two members by construction.
/// <para>
/// PORT-NOTE: TS gets <c>pos</c>/<c>end</c> from <c>CommentRange extends TextRange</c>; the port keeps them on the
/// type itself, the same treatment node positions get (see docs/architecture.md §12.1) — <c>ITextRange</c> carries
/// no members here.
/// </para>
/// </remarks>
internal sealed class CommentRange : ICommentRange
{
    public required SyntaxKind Kind { get; init; }

    public required int Pos { get; init; }

    public required int End { get; init; }

    public bool HasTrailingNewLine { get; set; }

    // The generated interface makes optional members nullable and the port's own type does not; bridge it the way
    // DiagnosticMessage bridges key/message.
    bool? ICommentRange.HasTrailingNewLine
    {
        get => HasTrailingNewLine;
        set => HasTrailingNewLine = value == true;
    }
}
