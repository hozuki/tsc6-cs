namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:10063-10066 — interface TextSpan
/// <summary>A half-open range in a text: where it starts and how long it is.</summary>
/// <remarks>
/// PORT-NOTE: TS declares <c>TextSpan</c> as an interface and every value of it is an object literal
/// (<c>{ start, length }</c>); the port needs a constructible type — the same treatment <see cref="LineAndCharacter"/>,
/// <see cref="TextRange"/> and <see cref="CallBinding"/> got. It is a class rather than the <c>readonly record struct</c>
/// docs/architecture.md §5 suggests for pure data because the type lives in an interface slot:
/// <see cref="ITextChangeRange.Span"/> hands the span out as <see cref="ITextSpan"/>, and a struct would be boxed into
/// that slot, so a caller could no longer see the same object TS hands back and (were the span ever written through the
/// interface) the write would land on a copy.
/// </remarks>
internal sealed class TextSpan : ITextSpan
{
    // ported from: types.ts:10064 — `start: number`
    public required int Start { get; set; }

    // ported from: types.ts:10065 — `length: number`
    public required int Length { get; set; }
}

/// <summary>The arithmetic on <see cref="TextSpan"/>, ported next to the type it operates on.</summary>
/// <remarks>
/// PORT-NOTE: TS declares these in <c>utilitiesPublic.ts</c> (and <c>compareTextSpans</c> in <c>core.ts</c>), which the
/// port maps to <see cref="Utilities"/> and <see cref="Core"/>. They stay in <c>Core</c> because they are three lines of
/// arithmetic over a Layer-0 value type: putting them in <see cref="Utilities"/> would make the value type's own
/// accessors part of Layer 5. <c>utilitiesPublic.ts</c> arrives with M5 and can forward to these names then
/// (docs/architecture.md §10.4 lists <c>compareTextSpans</c> as an M2 leftover, which is this).
/// </remarks>
internal static partial class Core
{
    // ported from: src/compiler/utilitiesPublic.ts:348-350 — textSpanEnd
    /// <summary>The end of a span: <c>start + length</c>.</summary>
    public static int TextSpanEnd(ITextSpan span) => span.Start + span.Length;

    // ported from: src/compiler/utilitiesPublic.ts:352-354 — textSpanIsEmpty
    /// <summary>Whether a span covers no characters at all.</summary>
    public static bool TextSpanIsEmpty(ITextSpan span) => span.Length == 0;

    // ported from: src/compiler/utilitiesPublic.ts:446-455 — createTextSpan
    /// <summary>Builds a span, rejecting negative offsets the way TS does.</summary>
    public static TextSpan CreateTextSpan(int start, int length)
    {
        // PORT-NOTE: TS throws `new Error("start < 0")`; the port's counterpart for a JS `Error` thrown for a
        // programming mistake is InvalidOperationException (the same treatment the rest of Core gives it).
        if (start < 0) throw new InvalidOperationException("start < 0");
        if (length < 0) throw new InvalidOperationException("length < 0");

        return new TextSpan { Start = start, Length = length };
    }

    // ported from: src/compiler/utilitiesPublic.ts:457-459 — createTextSpanFromBounds
    /// <summary>Builds the span that runs from <paramref name="start"/> to <paramref name="end"/>.</summary>
    public static TextSpan CreateTextSpanFromBounds(int start, int end) => CreateTextSpan(start, end - start);

    // ported from: src/compiler/core.ts:1997-1999 — compareTextSpans
    /// <summary>Orders two spans by start, then by length.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's signature is <c>(a: Partial&lt;TextSpan&gt; | undefined, b: Partial&lt;TextSpan&gt; | undefined)</c>
    /// and it reads the members with <c>?.</c>, because a caller may hand over a span with one member absent. C# has no
    /// "partially specified" type, so the port takes two whole spans and drops the <c>?.</c> — the two are the same
    /// comparison for every caller that exists (TS uses this to sort source map spans and declaration spans, never a
    /// partial one). A caller that needs <c>undefined</c>-tolerance can add an overload that maps a null span to the
    /// missing members.
    /// <para>
    /// PORT-NOTE: TS writes <c>compareValues(a?.start, b?.start) || compareValues(a?.length, b?.length)</c>. JS's
    /// <c>||</c> falls through only on the *falsy* value, and <c>0</c> is the only falsy <c>Comparison</c> — a
    /// <c>LessThan</c> result of <c>-1</c> is truthy and is returned as is. The port spells that out as
    /// "not EqualTo" instead of copying the operator.
    /// </para>
    /// </remarks>
    public static Comparison CompareTextSpans(ITextSpan a, ITextSpan b)
    {
        var startComparison = CompareValues(a.Start, b.Start);
        return startComparison != Comparison.EqualTo ? startComparison : CompareValues(a.Length, b.Length);
    }
}
