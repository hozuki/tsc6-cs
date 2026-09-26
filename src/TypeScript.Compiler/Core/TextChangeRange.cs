namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:10068-10071 — interface TextChangeRange
/// <summary>What a single edit did to a text: the span that was replaced and the length of its replacement.</summary>
/// <remarks>
/// PORT-NOTE: TS declares <c>TextChangeRange</c> as an interface and builds every value as an object literal
/// (<c>{ span, newLength }</c>); the port needs a constructible type — the same treatment <see cref="TextSpan"/>,
/// <see cref="LineAndCharacter"/>, <see cref="TextRange"/> and <see cref="CallBinding"/> got. The <c>span</c> member is
/// narrowed to the concrete <see cref="TextSpan"/> with an explicit interface implementation bridging the generated
/// <see cref="ITextChangeRange"/>, because <c>UpdateSourceFile</c> receives one through that interface and the writers
/// (<c>createTextChangeRange</c> and the Layer-8 builder) always produce <see cref="TextSpan"/> — the same pattern
/// <see cref="CommentDirective"/> uses for <c>range</c>.
/// </remarks>
internal sealed class TextChangeRange : ITextChangeRange
{
    // ported from: types.ts:10069 — `span: TextSpan`
    public required TextSpan Span { get; set; }

    // ported from: types.ts:10070 — `newLength: number`
    public required int NewLength { get; set; }

    ITextSpan? ITextChangeRange.Span
    {
        get => Span;
        set => Span = (TextSpan)value!;
    }
}

/// <summary>The constructors and readers for <see cref="TextChangeRange"/>, ported next to the type.</summary>
/// <remarks>
/// PORT-NOTE: as with <see cref="TextSpan"/>'s helpers, TS declares these in <c>utilitiesPublic.ts</c> and the port
/// keeps them in <c>Core</c> so the value type carries its own accessors instead of dragging in Layer 5. The sibling
/// slice that owns <c>Utilities/</c> may also land <c>Utilities.TextChangeRangeNewSpan</c>; these are the copies
/// <see cref="IncrementalParser"/> calls, and either name is the same one-liner.
/// </remarks>
internal static partial class Core
{
    // ported from: src/compiler/utilitiesPublic.ts:469-475 — createTextChangeRange
    /// <summary>Builds a change range, rejecting a negative replacement length the way TS does.</summary>
    /// <remarks>
    /// PORT-NOTE: TS takes <c>TextSpan</c> here, so the port keeps the concrete parameter type rather than widening it
    /// to <see cref="ITextSpan"/> — the range stores the span it was handed (identity preserved, no copy), and only
    /// <see cref="TextSpan"/> can be bridged onto <see cref="ITextChangeRange.Span"/> without a downcast.
    /// </remarks>
    public static TextChangeRange CreateTextChangeRange(TextSpan span, int newLength)
    {
        if (newLength < 0) throw new InvalidOperationException("newLength < 0");

        return new TextChangeRange { Span = span, NewLength = newLength };
    }

    // ported from: src/compiler/utilitiesPublic.ts:461-463 — textChangeRangeNewSpan
    /// <summary>The span the change replaced the old one with: the old start, the new length.</summary>
    public static TextSpan TextChangeRangeNewSpan(ITextChangeRange range) => CreateTextSpan(range.Span!.Start, range.NewLength);

    // ported from: src/compiler/utilitiesPublic.ts:465-467 — textChangeRangeIsUnchanged
    /// <summary>Whether the change replaced nothing with nothing, i.e. the text is identical.</summary>
    public static bool TextChangeRangeIsUnchanged(ITextChangeRange range) => TextSpanIsEmpty(range.Span!) && range.NewLength == 0;
}
