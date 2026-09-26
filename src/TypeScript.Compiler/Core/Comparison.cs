namespace TypeScript.Compiler;

// ported from: src/compiler/corePublic.ts:36-47
/// <summary>
/// Result of a three-way comparison. Equivalent to TS's <c>Comparison</c> const enum.
/// </summary>
internal enum Comparison
{
    LessThan = -1,
    EqualTo = 0,
    GreaterThan = 1,
}

/// <summary>
/// ported from: src/compiler/corePublic.ts:37-38 — TS: <c>EqualityComparer&lt;T&gt; = (a: T, b: T) =&gt; boolean</c>.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS keeps equality and hashing separate (see <c>core.createSet</c> / <c>core.createMap</c>,
/// which take an <c>EqualityComparer</c> or a <c>getHashCode</c> function). The BCL instead models both
/// through <see cref="IEqualityComparer{T}"/>; <see cref="EqualityComparerAdapter{T}"/> bridges the two
/// wherever a BCL collection is used.
/// </remarks>
internal delegate bool EqualityComparer<T>(T a, T b);

/// <summary>
/// ported from: src/compiler/corePublic.ts:39-40 — TS: <c>Comparer&lt;T&gt; = (a: T, b: T) =&gt; Comparison</c>.
/// </summary>
internal delegate Comparison Comparer<T>(T a, T b);

/// <summary>
/// ported from: src/compiler/corePublic.ts:25-34 — TS: <c>ReadonlyCollection&lt;K&gt;</c>,
/// the common read surface of ES6 Map/Set (used for <c>getKeys()</c>-style results).
/// </summary>
internal interface IReadonlyCollection<K> : IEnumerable<K>
{
    int Count { get; }

    bool Contains(K key);
}

/// <summary>
/// Bridges a TS-style <see cref="EqualityComparer{T}"/> (plus an optional hash function) into the
/// <see cref="IEqualityComparer{T}"/> shape required by BCL collections.
/// </summary>
internal sealed class EqualityComparerAdapter<T> : IEqualityComparer<T>
{
    private readonly Func<T, int> _getHashCode;
    private readonly EqualityComparer<T> _equals;

    public EqualityComparerAdapter(EqualityComparer<T> equals, Func<T, int>? getHashCode = null)
    {
        _equals = equals;
        _getHashCode = getHashCode ?? (_ => 0);
    }

    public bool Equals(T? x, T? y)
    {
        if (x is null) return y is null;
        if (y is null) return false;
        return _equals(x, y);
    }

    public int GetHashCode(T obj) => _getHashCode(obj);
}
