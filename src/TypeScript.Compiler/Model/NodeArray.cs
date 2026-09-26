namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:1583-1592 (MutableNodeArray / NodeArray)
/// <summary>
/// An array of nodes that also carries a text range.
/// </summary>
/// <remarks>
/// Hand-written: TS declares it as an interface extending <c>ReadonlyArray&lt;T&gt;</c>, which has no direct
/// C# counterpart, so the port derives from <see cref="List{T}"/> instead. The extra state
/// (<see cref="HasTrailingComma"/>, <see cref="TransformFlags"/>) is read by the emitter, which is why it
/// cannot simply be a plain list.
/// </remarks>
internal class NodeArray<T> : List<T>, IReadonlyTextRange
{
    public NodeArray()
    {
    }

    public NodeArray(IEnumerable<T> items)
        : base(items)
    {
    }

    public NodeArray(int capacity)
        : base(capacity)
    {
    }

    // ported from: types.ts:1590 — `readonly hasTrailingComma: boolean`
    public bool HasTrailingComma { get; set; }

    // ported from: types.ts:1591 — `transformFlags: TransformFlags`
    public TransformFlags TransformFlags { get; set; }

    // ported from: types.ts:33-36 (ReadonlyTextRange)
    public int Pos { get; set; }

    public int End { get; set; }
}

/// <summary>
/// ported from: src/compiler/types.ts:1583-1587 — TS's <c>MutableNodeArray&lt;T&gt;</c>.
/// </summary>
/// <remarks>
/// In TS the distinction is only about writability (<c>Array&lt;T&gt;</c> vs <c>ReadonlyArray&lt;T&gt;</c>);
/// <see cref="List{T}"/> is already mutable, so the two collapse into one type here.
/// </remarks>
internal sealed class MutableNodeArray<T> : NodeArray<T>
{
    public MutableNodeArray()
    {
    }

    public MutableNodeArray(IEnumerable<T> items)
        : base(items)
    {
    }
}
