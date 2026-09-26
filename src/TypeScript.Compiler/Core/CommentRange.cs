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
    public required SyntaxKind Kind { get; set; }

    public required int Pos { get; set; }

    public required int End { get; set; }

    public bool HasTrailingNewLine { get; set; }

    // The generated interface makes optional members nullable and the port's own type does not; bridge it the way
    // DiagnosticMessage bridges key/message.
    bool? ICommentRange.HasTrailingNewLine
    {
        get => HasTrailingNewLine;
        set => HasTrailingNewLine = value == true;
    }
}

// ported from: src/compiler/types.ts:3877-3881 — interface SynthesizedComment
/// <summary>
/// A comment attached to a node by transforms (<c>addSyntheticLeadingComment</c> and friends) rather than found in
/// the source text.
/// </summary>
/// <remarks>
/// PORT-NOTE: same treatment as <see cref="CommentRange"/> — TS builds these as object literals over
/// <c>CommentRange</c>, so the port supplies a constructible type that carries its own <c>kind</c>/<c>pos</c>/<c>end</c>.
/// <c>SynthesizedComment.hasLeadingNewline</c> is misspelled that way in TS (no capital N in "newline") and the
/// generated interface keeps the spelling; the port's property uses the same name so the two line up.
/// </remarks>
internal sealed class SynthesizedComment : ISynthesizedComment
{
    public required SyntaxKind Kind { get; set; }

    public required int Pos { get; set; }

    public required int End { get; set; }

    public required string Text { get; set; }

    public bool HasTrailingNewLine { get; set; }

    public bool HasLeadingNewline { get; set; }

    bool? ICommentRange.HasTrailingNewLine
    {
        get => HasTrailingNewLine;
        set => HasTrailingNewLine = value == true;
    }

    string? ISynthesizedComment.Text
    {
        get => Text;
        set => Text = value ?? "";
    }

    bool? ISynthesizedComment.HasLeadingNewline
    {
        get => HasLeadingNewline;
        set => HasLeadingNewline = value == true;
    }
}
