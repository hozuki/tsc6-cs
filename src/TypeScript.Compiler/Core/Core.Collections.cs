namespace TypeScript.Compiler;

// ported from: src/compiler/core.ts:1527-1743, 1750-1775, 1266-1321
/// <summary>
/// Container types (multi-map, queue, custom-equality set) and small value tests. Equivalent to the
/// container half of TS's core.ts.
/// </summary>
internal static partial class Core
{
    // ported from: src/compiler/core.ts:1527-1566
    /// <summary>
    /// A map from a key to an array of values, with helper methods to append and remove single values.
    /// Equivalent to TS's <c>MultiMap&lt;K, V&gt;</c> (a <c>Map&lt;K, V[]&gt;</c> with <c>add</c>/<c>remove</c>).
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS attaches <c>add</c>/<c>remove</c> onto a <c>Map</c> instance. C# can't add members to
    /// <see cref="Dictionary{TKey,TValue}"/>, and inheriting it would collide with its existing
    /// <c>Add(TKey, TValue)</c> (which differs only by return type from TS's <c>add</c>), so the port uses
    /// composition instead.
    /// </remarks>
    internal sealed class MultiMap<TK, TV>
        where TK : notnull
    {
        private readonly Dictionary<TK, List<TV>> _map = new();

        public int Count => _map.Count;

        public IEnumerable<TK> Keys => _map.Keys;

        public IEnumerable<List<TV>> Values => _map.Values;

        public bool ContainsKey(TK key) => _map.ContainsKey(key);

        public List<TV>? Get(TK key) => _map.TryGetValue(key, out var values) ? values : null;

        /// <summary>
        /// Adds the value to an array of values associated with the key, and returns the array.
        /// Creates the array if it does not already exist.
        /// </summary>
        public List<TV> Add(TK key, TV value)
        {
            if (_map.TryGetValue(key, out var values))
            {
                values.Add(value);
                return values;
            }

            var created = new List<TV> { value };
            _map[key] = created;
            return created;
        }

        /// <summary>
        /// Removes a value from an array of values associated with the key, without preserving order.
        /// Does nothing if the key is not in the map, or the value is not in the key's array.
        /// </summary>
        public void Remove(TK key, TV value)
        {
            if (_map.TryGetValue(key, out var values))
            {
                UnorderedRemoveItem(values, value);
                if (values.Count == 0)
                {
                    _map.Remove(key);
                }
            }
        }
    }

    // ported from: src/compiler/core.ts:1542-1547
    public static MultiMap<TK, TV> CreateMultiMap<TK, TV>()
        where TK : notnull
        => new();

    // ported from: src/compiler/core.ts:1569-1608
    /// <summary>
    /// A FIFO queue. Equivalent to TS's <c>Queue&lt;T&gt;</c> (<c>types.ts:10594</c>).
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS hand-rolls the storage (a backing array plus a head index, compacted once the head
    /// passes 100 entries) purely as a performance tuning. <see cref="Queue{T}"/> is the same FIFO with the
    /// same amortised behaviour, so the port delegates to it rather than reproducing the bookkeeping.
    /// </remarks>
    internal sealed class TsQueue<T>
    {
        private readonly Queue<T> _queue;

        public TsQueue(IList<T>? items = null)
        {
            _queue = items is null ? new Queue<T>() : new Queue<T>(items);
        }

        public bool IsEmpty => _queue.Count == 0;

        public int Count => _queue.Count;

        public void Enqueue(T item) => _queue.Enqueue(item);

        public void EnqueueRange(IEnumerable<T> items)
        {
            foreach (var item in items)
            {
                _queue.Enqueue(item);
            }
        }

        public T Dequeue()
            => _queue.Count > 0 ? _queue.Dequeue() : throw new InvalidOperationException("Queue is empty");
    }

    // ported from: src/compiler/core.ts:1569-1571
    public static TsQueue<T> CreateQueue<T>(IList<T>? items = null) => new(items);

    // ported from: src/compiler/core.ts:1622-1743
    /// <summary>
    /// Creates a set with custom equality and hash code functionality. This is useful when you want
    /// something looser than object identity - e.g. "has the same span".
    /// <para>
    /// If <c>equals(a, b)</c>, it must be the case that <c>getHashCode(a) == getHashCode(b)</c>. The
    /// converse is not required.
    /// </para>
    /// </summary>
    // PORT-NOTE: TS hand-rolls the set (a Map from hash to element-or-element-array, with lazy bucket
    // allocation because TElement is assumed not to be an array type). HashSet<T> with an
    // IEqualityComparer that folds the hash function in has the same semantics, so the port delegates to it.
    public static HashSet<TElement> CreateSet<TElement, THash>(Func<TElement, THash> getHashCode, EqualityComparer<TElement> equals)
        where TElement : notnull
        => new(new EqualityComparerAdapter<TElement>(equals, e => getHashCode(e)?.GetHashCode() ?? 0));

    // ported from: src/compiler/core.ts:1266-1321
    public static bool HasProperty<T>(IReadOnlyDictionary<string, T> map, string key) => map.ContainsKey(key);

    public static T? GetProperty<T>(IReadOnlyDictionary<string, T> map, string key)
        => map.TryGetValue(key, out var value) ? value : default;

    /// <summary>Gets the owned, enumerable property keys of a map-like.</summary>
    // PORT-NOTE: C# Dictionary does not guarantee enumeration order, while TS object key order is insertion
    // order. Callers that need a stable order must sort or use an order-preserving structure (see
    // docs/architecture.md §5 #17).
    public static List<string> GetOwnKeys<T>(IReadOnlyDictionary<string, T> map) => [.. map.Keys];

    public static List<T> GetOwnValues<T>(IReadOnlyDictionary<string, T> map) => [.. map.Values];

    /// <summary>Performs a shallow equality comparison of the contents of two map-likes.</summary>
    public static bool EqualOwnProperties<T>(
        IReadOnlyDictionary<string, T>? left,
        IReadOnlyDictionary<string, T>? right,
        EqualityComparer<T>? equalityComparer = null)
    {
        var comparer = equalityComparer ?? EquateValues;
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        if (left.Count != right.Count) return false;
        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var rightValue)) return false;
            if (!comparer(value, rightValue)) return false;
        }

        return true;
    }

    // ported from: src/compiler/core.ts:1750-1762
    /// <summary>Tests whether a value is an array.</summary>
    public static bool IsArray(object? value) => value is Array;

    // ported from: src/compiler/core.ts:1760-1762
    // PORT-NOTE: TS relies on the runtime shape of `T | T[]`; C# cannot distinguish those from a type
    // parameter, so the element type is supplied by the caller via the generic argument.
    public static IList<T> ToArray<T>(object? value)
        => value is IList<T> list ? list : [(T)value!];

    // ported from: src/compiler/core.ts:1769-1775
    public static bool IsString(object? text) => text is string;

    public static bool IsNumber(object? x) => x is double or int;

    // ported from: src/compiler/core.ts:1778-1787
    public static TOut? TryCast<TOut, TIn>(TIn? value, Func<TIn, bool> test)
        where TIn : class
        where TOut : class, TIn
        => value is not null && test(value) ? (TOut)value : null;

    public static TOut Cast<TOut, TIn>(TIn? value, Func<TIn, bool> test)
        where TIn : class
        where TOut : class, TIn
    {
        if (value is not null && test(value)) return (TOut)value;

        return Debug.Fail<TOut>($"Invalid cast. The supplied value {value} did not pass the test '{Debug.GetFunctionName(test)}'.");
    }

    // ported from: src/compiler/core.ts:1794-1830
    /// <summary>Does nothing.</summary>
    public static void Noop(object? _ = null)
    {
    }

    /// <summary>Do nothing and return false.</summary>
    public static bool ReturnFalse() => false;

    /// <summary>Do nothing and return true.</summary>
    public static bool ReturnTrue() => true;

    /// <summary>Do nothing and return null.</summary>
    public static object? ReturnUndefined() => null;

    // ported from: src/compiler/core.ts:1886-1888
    /// <summary>Throws an error because a function is not implemented.</summary>
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    public static void NotImplemented() => throw new NotImplementedException("Not implemented");

    // ported from: src/compiler/core.ts:1891-1918
    public static Func<T> Memoize<T>(Func<T> callback)
    {
        var evaluated = false;
        T value = default!;
        return () =>
        {
            if (!evaluated)
            {
                value = callback();
                evaluated = true;
            }

            return value;
        };
    }

    /// <summary>
    /// A version of <see cref="Memoize"/> that supports a single primitive argument.
    /// </summary>
    public static Func<A, T> MemoizeOne<A, T>(Func<A, T> callback)
        where A : notnull
    {
        var map = new Dictionary<string, T>();
        return arg =>
        {
            var key = $"{typeof(A).Name}:{arg}";
            if (!map.TryGetValue(key, out var value))
            {
                value = callback(arg);
                map[key] = value;
            }

            return value;
        };
    }

    // ported from: src/compiler/core.ts:2588-2595
    /// <summary>
    /// Whether the host looks like Node.js (used to gate Node-only performance APIs).
    /// </summary>
    // PORT-NOTE: TS probes `process.nextTick`/`require`. The C# port runs on the CLR, where the answer is
    // always "no"; performance instrumentation uses Stopwatch/Activity instead (see Core/Performance.cs).
    public static bool IsNodeLikeSystem() => false;

    // ---- not yet ported ----
    // TODO(M5): `hasProperty`/`getProperty`/`getOwnKeys`/`getOwnValues` above cover the map-like cases the
    // compiler actually uses. The remaining object utilities (`getAllKeys`, `assign`, `clone`, `extend`,
    // `copyProperties`, `maybeBind`, `flatten`'s object form) operate on JS prototype semantics
    // (Object.getPrototypeOf, for-in over prototypes, Function.prototype.bind). Their C# shape depends on
    // how `CompilerOptions` and friends are modelled in the Program layer, so they land with that layer
    // rather than being faked with reflection here.
}
