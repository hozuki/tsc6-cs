namespace TypeScript.Compiler;

// ported from: src/compiler/core.ts:17-1260
/// <summary>
/// Array, iteration and collection utilities. Equivalent to the array/iteration half of TS's
/// <c>core.ts</c> module.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS distinguishes <c>T[]</c> from <c>readonly T[]</c> in the type system only — at run time both are
/// a mutable <c>Array</c>. The port therefore maps both sides to <see cref="IList{T}"/>, with freshly-built
/// values being <see cref="List{T}"/>. That is what lets the functions TS documents as "returns the same array
/// instance" (<c>filter</c>, <c>sameMap</c>, <c>compact</c>, <c>concatenate</c>, …) hand the input back
/// (see docs/architecture.md §10.2).
/// <para>
/// PORT-NOTE: <c>undefined</c> becomes <c>null</c>; nullable reference types make this explicit.
/// Functions whose TS signature is <c>readonly T[] | undefined</c> take an <see cref="IList{T}"/>?
/// and return <c>null</c> for the <c>undefined</c> case.
/// </para>
/// <para>
/// PORT-NOTE: TS's <c>forEach</c> stops on a *truthy* callback result, which for reference types is the
/// same as "not null". The port constrains the result type to <c>class</c> so the two coincide; value-typed
/// truthiness would need a separate overload.
/// </para>
/// </remarks>
internal static partial class Core
{
    // ported from: src/compiler/core.ts:22-24
    public static int Length<T>(IList<T>? array) => array?.Count ?? 0;

    // ported from: src/compiler/core.ts:33-43
    public static TU? ForEach<T, TU>(IList<T>? array, Func<T, int, TU?> callback)
        where TU : class
    {
        if (array is not null)
        {
            for (var i = 0; i < array.Count; i++)
            {
                var result = callback(array[i], i);
                if (result is not null)
                {
                    return result;
                }
            }
        }

        return null;
    }

    // ported from: src/compiler/core.ts:50-60
    public static TU? ForEachRight<T, TU>(IList<T>? array, Func<T, int, TU?> callback)
        where TU : class
    {
        if (array is not null)
        {
            for (var i = array.Count - 1; i >= 0; i--)
            {
                var result = callback(array[i], i);
                if (result is not null)
                {
                    return result;
                }
            }
        }

        return null;
    }

    // ported from: src/compiler/core.ts:67-79
    public static TU? FirstDefined<T, TU>(IList<T>? array, Func<T, int, TU?> callback)
        where TU : class
    {
        if (array is null)
        {
            return null;
        }

        for (var i = 0; i < array.Count; i++)
        {
            var result = callback(array[i], i);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    // ported from: src/compiler/core.ts:82-90
    public static TU? FirstDefinedIterator<T, TU>(IEnumerable<T> iter, Func<T, TU?> callback)
        where TU : class
    {
        foreach (var value in iter)
        {
            var result = callback(value);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    // ported from: src/compiler/core.ts:93-103
    public static TU ReduceLeftIterator<T, TU>(IEnumerable<T>? iterator, Func<TU, T, int, TU> f, TU initial)
    {
        var result = initial;
        if (iterator is not null)
        {
            var pos = 0;
            foreach (var value in iterator)
            {
                result = f(result, value, pos);
                pos++;
            }
        }

        return result;
    }

    // ported from: src/compiler/core.ts:106-113
    public static List<TV> ZipWith<T, TU, TV>(IList<T> arrayA, IList<TU> arrayB, Func<T, TU, int, TV> callback)
    {
        Debug.AssertEqual(arrayA.Count, arrayB.Count);
        var result = new List<TV>(arrayA.Count);
        for (var i = 0; i < arrayA.Count; i++)
        {
            result.Add(callback(arrayA[i], arrayB[i], i));
        }

        return result;
    }

    // ported from: src/compiler/core.ts:121-131
    public static IList<T> Intersperse<T>(IList<T> input, T element)
    {
        if (input.Count <= 1)
        {
            return new List<T>(input);
        }

        var result = new List<T>();
        for (int i = 0, n = input.Count; i < n; i++)
        {
            if (i != 0) result.Add(element);
            result.Add(input[i]);
        }

        return result;
    }

    // ported from: src/compiler/core.ts:145-155
    public static bool Every<T>(IList<T>? array, Func<T, int, bool> callback)
    {
        if (array is not null)
        {
            for (var i = 0; i < array.Count; i++)
            {
                if (!callback(array[i], i))
                {
                    return false;
                }
            }
        }

        return true;
    }

    // ported from: src/compiler/core.ts:166-175
    public static T? Find<T>(IList<T>? array, Func<T, int, bool> predicate, int startIndex = 0)
        where T : class
    {
        if (array is null) return null;
        for (var i = startIndex; i < array.Count; i++)
        {
            var value = array[i];
            if (predicate(value, i))
            {
                return value;
            }
        }

        return null;
    }

    // ported from: src/compiler/core.ts:182-191
    public static T? FindLast<T>(IList<T>? array, Func<T, int, bool> predicate, int? startIndex = null)
        where T : class
    {
        if (array is null) return null;
        for (var i = startIndex ?? array.Count - 1; i >= 0; i--)
        {
            var value = array[i];
            if (predicate(value, i))
            {
                return value;
            }
        }

        return null;
    }

    // ported from: src/compiler/core.ts:198-206
    public static int FindIndex<T>(IList<T>? array, Func<T, int, bool> predicate, int startIndex = 0)
    {
        if (array is null) return -1;
        for (var i = startIndex; i < array.Count; i++)
        {
            if (predicate(array[i], i))
            {
                return i;
            }
        }

        return -1;
    }

    // ported from: src/compiler/core.ts:209-217
    public static int FindLastIndex<T>(IList<T>? array, Func<T, int, bool> predicate, int? startIndex = null)
    {
        if (array is null) return -1;
        for (var i = startIndex ?? array.Count - 1; i >= 0; i--)
        {
            if (predicate(array[i], i))
            {
                return i;
            }
        }

        return -1;
    }

    // ported from: src/compiler/core.ts:220-229
    public static bool Contains<T>(IList<T>? array, T value, EqualityComparer<T>? equalityComparer = null)
    {
        var comparer = equalityComparer ?? EquateValues;
        if (array is not null)
        {
            for (var i = 0; i < array.Count; i++)
            {
                if (comparer(array[i], value))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // ported from: src/compiler/core.ts:232-239
    public static int IndexOfAnyCharCode(string text, IList<int> charCodes, int start = 0)
    {
        for (var i = start; i < text.Length; i++)
        {
            if (Contains(charCodes, text[i]))
            {
                return i;
            }
        }

        return -1;
    }

    // ported from: src/compiler/core.ts:242-253
    public static int CountWhere<T>(IList<T>? array, Func<T, int, bool> predicate)
    {
        var count = 0;
        if (array is not null)
        {
            for (var i = 0; i < array.Count; i++)
            {
                if (predicate(array[i], i))
                {
                    count++;
                }
            }
        }

        return count;
    }

    // ported from: src/compiler/core.ts:277-296
    public static IList<T>? Filter<T>(IList<T>? array, Func<T, bool> f)
    {
        if (array is null)
        {
            return null;
        }

        // TS builds the whole result and then returns the *input* when nothing was filtered out. Both sides are
        // IList<T> here — the counterpart of JS's mutable array — so that instance can be handed back as-is
        // (docs/architecture.md §10.2).
        var result = new List<T>(array.Count);
        for (var i = 0; i < array.Count; i++)
        {
            var item = array[i];
            if (f(item))
            {
                result.Add(item);
            }
        }

        return result.Count != array.Count ? result : array;
    }

    // ported from: src/compiler/core.ts:299-308
    public static void FilterMutate<T>(IList<T> array, Func<T, int, IList<T>, bool> f)
    {
        var outIndex = 0;
        for (var i = 0; i < array.Count; i++)
        {
            if (f(array[i], i, array))
            {
                array[outIndex] = array[i];
                outIndex++;
            }
        }

        // PORT-NOTE: IList<T> has no RemoveRange, so the tail is trimmed one item at a time — same effect, and
        // the parameter stays an interface (docs/architecture.md §10.2).
        while (array.Count > outIndex) array.RemoveAt(array.Count - 1);
    }

    // ported from: src/compiler/core.ts:311-313
    public static void Clear<T>(IList<T> array) => array.Clear();

    // ported from: src/compiler/core.ts:320-329
    public static List<TU>? Map<T, TU>(IList<T>? array, Func<T, int, TU> f)
    {
        if (array is null)
        {
            return null;
        }

        var result = new List<TU>(array.Count);
        for (var i = 0; i < array.Count; i++)
        {
            result.Add(f(array[i], i));
        }

        return result;
    }

    // ported from: src/compiler/core.ts:332-336
    public static IEnumerable<TU> MapIterator<T, TU>(IEnumerable<T> iter, Func<T, TU> mapFn)
    {
        foreach (var x in iter)
        {
            yield return mapFn(x);
        }
    }

    // ported from: src/compiler/core.ts:350-366
    public static IList<T>? SameMap<T>(IList<T>? array, Func<T, int, T> f)
    {
        if (array is not null)
        {
            for (var i = 0; i < array.Count; i++)
            {
                var item = array[i];
                var mapped = f(item, i);
                if (!ReferenceEquals(item, mapped))
                {
                    var result = new List<T>(array.Count);
                    for (var j = 0; j < i; j++)
                    {
                        result.Add(array[j]);
                    }

                    result.Add(mapped);
                    for (i++; i < array.Count; i++)
                    {
                        result.Add(f(array[i], i));
                    }

                    return result;
                }
            }
        }

        return array;
    }

    // ported from: src/compiler/core.ts:375-389
    // PORT-NOTE: TS's `flatten` tests `if (v)` (JS truthiness); Js.IsTruthy keeps that semantic.
    public static IList<T> Flatten<T>(IList<object?> array)
    {
        var result = new List<T>();
        for (var i = 0; i < array.Count; i++)
        {
            var v = array[i];
            if (!Js.IsTruthy(v))
            {
                continue;
            }

            if (v is IEnumerable<T> many)
            {
                result.AddRange(many);
            }
            else
            {
                result.Add((T)v!);
            }
        }

        return result;
    }

    // ported from: src/compiler/core.ts:399-415
    /// <remarks>
    /// PORT-NOTE: the test is JS <em>truthiness</em>, not "is not undefined": TS drops a mapped value that is
    /// falsy, so an empty string produced by the mapper never reaches the result (wildcard specs rely on this).
    /// </remarks>
    public static IList<TU> FlatMap<T, TU>(IList<T>? array, Func<T, int, object?> mapfn)
    {
        var result = new List<TU>();
        if (array is not null)
        {
            for (var i = 0; i < array.Count; i++)
            {
                var v = mapfn(array[i], i);
                if (!Js.IsTruthy(v)) continue;
                if (v is IEnumerable<TU> many)
                {
                    result.AddRange(many);
                }
                else
                {
                    result.Add((TU)v!);
                }
            }
        }

        return result;
    }

    // ported from: src/compiler/core.ts:418-434
    /// <inheritdoc cref="FlatMap{T,TU}(IList{T}?,Func{T,int,object}?)"/>
    public static List<TU> FlatMapToMutable<T, TU>(IList<T>? array, Func<T, int, object?> mapfn)
    {
        var result = new List<TU>();
        if (array is not null)
        {
            for (var i = 0; i < array.Count; i++)
            {
                var v = mapfn(array[i], i);
                if (!Js.IsTruthy(v)) continue;
                if (v is IEnumerable<TU> many)
                {
                    result.AddRange(many);
                }
                else
                {
                    result.Add((TU)v!);
                }
            }
        }

        return result;
    }

    // ported from: src/compiler/core.ts:437-443
    public static IEnumerable<U> FlatMapIterator<T, U>(IEnumerable<T> iter, Func<T, IEnumerable<U>?> mapfn)
    {
        foreach (var x in iter)
        {
            var iter2 = mapfn(x);
            if (iter2 is null) continue;
            foreach (var y in iter2)
            {
                yield return y;
            }
        }
    }

    // ported from: src/compiler/core.ts:458-478
    /// <remarks>
    /// PORT-NOTE: TS's <c>array !== undefined</c> guard is the whole body's precondition, and it hands the caller back
    /// either <c>undefined</c> or the same array — so the parameter and the result are both nullable here.
    /// </remarks>
    public static IList<T>? SameFlatMap<T>(IList<T>? array, Func<T, int, object> mapfn)
    {
        if (array is null) return null;

        IList<T>? result = null;
        for (var i = 0; i < array.Count; i++)
        {
            var item = array[i];
            var mapped = mapfn(item, i);
            if (result is not null || !ReferenceEquals(item, mapped) || mapped is IEnumerable<T>)
            {
                result ??= Slice(array, 0, i);
                if (mapped is IEnumerable<T> many)
                {
                    foreach (var value in many) result.Add(value);
                }
                else
                {
                    result.Add((T)mapped);
                }
            }
        }

        return result ?? array;
    }

    // ported from: src/compiler/core.ts:481-491
    public static List<TU>? MapAllOrFail<T, TU>(IList<T> array, Func<T, int, TU?> mapFn)
        where TU : class
    {
        var result = new List<TU>(array.Count);
        for (var i = 0; i < array.Count; i++)
        {
            var mapped = mapFn(array[i], i);
            if (mapped is null)
            {
                return null;
            }

            result.Add(mapped);
        }

        return result;
    }

    // ported from: src/compiler/core.ts:494-505
    public static List<TU> MapDefined<T, TU>(IList<T>? array, Func<T, int, TU?> mapFn)
        where TU : class
    {
        var result = new List<TU>();
        if (array is not null)
        {
            for (var i = 0; i < array.Count; i++)
            {
                var mapped = mapFn(array[i], i);
                if (mapped is not null)
                {
                    result.Add(mapped);
                }
            }
        }

        return result;
    }

    // ported from: src/compiler/core.ts:509-516
    public static IEnumerable<TU> MapDefinedIterator<T, TU>(IEnumerable<T> iter, Func<T, TU?> mapFn)
        where TU : class
    {
        foreach (var x in iter)
        {
            var value = mapFn(x);
            if (value is not null)
            {
                yield return value;
            }
        }
    }

    // ported from: src/compiler/core.ts:519-526
    public static TV GetOrUpdate<TK, TV>(Dictionary<TK, TV> map, TK key, Func<TV> callback)
        where TK : notnull
    {
        if (map.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var value = callback();
        map.Add(key, value);
        return value;
    }

    // ported from: src/compiler/core.ts:529-535
    // PORT-NOTE: BCL HashSet<T>.Add already reports whether the item was newly added.
    public static bool TryAddToSet<T>(HashSet<T> set, T value) => set.Add(value);

    // ported from: src/compiler/core.ts:538-540
    public static IEnumerable<T> SingleIterator<T>(T value)
    {
        yield return value;
    }

    // ported from: src/compiler/core.ts:555-593
    public static List<TU>? SpanMap<T, TK, TU>(IList<T>? array, Func<T, int, TK> keyfn, Func<IList<T>, TK, int, int, TU?> mapfn)
    {
        List<TU>? result = null;
        if (array is not null)
        {
            result = new List<TU>();
            var len = array.Count;
            TK? previousKey = default;
            TK? key = default;
            var start = 0;
            var pos = 0;
            while (start < len)
            {
                while (pos < len)
                {
                    var value = array[pos];
                    key = keyfn(value, pos);
                    if (pos == 0)
                    {
                        previousKey = key;
                    }
                    else if (!EquateValues(key, previousKey))
                    {
                        break;
                    }

                    pos++;
                }

                if (start < pos)
                {
                    var v = mapfn(Slice(array, start, pos), previousKey!, start, pos);
                    if (v is not null)
                    {
                        result.Add(v);
                    }

                    start = pos;
                }

                previousKey = key;
                pos++;
            }
        }

        return result;
    }

    // ported from: src/compiler/core.ts:600-611
    public static Dictionary<TK2, TV2>? MapEntries<TK1, TV1, TK2, TV2>(
        IReadOnlyDictionary<TK1, TV1>? map,
        Func<TK1, TV1, (TK2 Key, TV2 Value)> f)
        where TK1 : notnull
        where TK2 : notnull
    {
        if (map is null)
        {
            return null;
        }

        var result = new Dictionary<TK2, TV2>();
        foreach (var (key, value) in map)
        {
            var (newKey, newValue) = f(key, value);
            result[newKey] = newValue;
        }

        return result;
    }

    // ported from: src/compiler/core.ts:619-632
    /// <remarks>
    /// PORT-NOTE: TS's <c>some</c> predicate is <c>(value: T, index: number) =&gt; boolean</c>. The port's original
    /// overload narrowed that to <c>Func&lt;T, bool&gt;</c>, which cannot express the reference's own index-reading
    /// callers; this overload restores that shape. The two coexist by lambda arity, and this body is the reference's
    /// own loop rather than a delegation through the one-predicate form, because the index it passes must be the
    /// element's position — an <c>IndexOf</c>-based delegation would report the first occurrence for a duplicated
    /// element.
    /// </remarks>
    public static bool Some<T>(IList<T>? array, Func<T, int, bool> predicate)
    {
        if (array is not null)
        {
            for (var i = 0; i < array.Count; i++)
            {
                if (predicate(array[i], i))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // ported from: src/compiler/core.ts:618-632
    public static bool Some<T>(IList<T>? array, Func<T, bool>? predicate = null)
    {
        if (array is not null)
        {
            if (predicate is not null)
            {
                for (var i = 0; i < array.Count; i++)
                {
                    if (predicate(array[i]))
                    {
                        return true;
                    }
                }
            }
            else
            {
                return array.Count > 0;
            }
        }

        return false;
    }

    // ported from: src/compiler/core.ts:639-653
    public static void GetRangesWhere<T>(IList<T> arr, Func<T, bool> pred, Action<int, int> cb)
    {
        int? start = null;
        for (var i = 0; i < arr.Count; i++)
        {
            if (pred(arr[i]))
            {
                start ??= i;
            }
            else
            {
                if (start is not null)
                {
                    cb(start.Value, i);
                    start = null;
                }
            }
        }

        if (start is not null) cb(start.Value, arr.Count);
    }

    // ported from: src/compiler/core.ts:672-676
    public static IList<T>? Concatenate<T>(IList<T>? array1, IList<T>? array2)
    {
        if (array2 is null || array2.Count == 0) return array1;
        if (array1 is null || array1.Count == 0) return array2;
        var result = new List<T>(array1.Count + array2.Count);
        result.AddRange(array1);
        result.AddRange(array2);
        return result;
    }

    // ported from: src/compiler/core.ts:678-685
    public static List<int> IndicesOf<T>(IList<T> array)
    {
        var result = new List<int>(array.Count);
        for (var i = 0; i < array.Count; i++)
        {
            result.Add(i);
        }

        return result;
    }

    // ported from: src/compiler/core.ts:725-730
    public static IList<T> Deduplicate<T>(IList<T> array, EqualityComparer<T> equalityComparer, Comparer<T>? comparer = null)
    {
        if (array.Count == 0)
        {
            return new List<T>();
        }

        if (array.Count == 1)
        {
            return new List<T>(array);
        }

        return comparer is not null
            ? DeduplicateRelational(array, equalityComparer, comparer)
            : DeduplicateEquality(array, equalityComparer);
    }

    // ported from: src/compiler/core.ts:687-707
    private static IList<T> DeduplicateRelational<T>(IList<T> array, EqualityComparer<T> equalityComparer, Comparer<T> comparer)
    {
        // Perform a stable sort of the array. This ensures the first entry in a list of
        // duplicates remains the first entry in the result.
        var indices = IndicesOf(array);
        StableSortIndices(array, indices, comparer);

        var last = array[indices[0]];
        var deduplicated = new List<int> { indices[0] };
        for (var i = 1; i < indices.Count; i++)
        {
            var index = indices[i];
            var item = array[index];
            if (!equalityComparer(last, item))
            {
                deduplicated.Add(index);
                last = item;
            }
        }

        // restore original order
        deduplicated.Sort();
        var result = new List<T>(deduplicated.Count);
        foreach (var i in deduplicated)
        {
            result.Add(array[i]);
        }

        return result;
    }

    // ported from: src/compiler/core.ts:709-715
    private static IList<T> DeduplicateEquality<T>(IList<T> array, EqualityComparer<T> equalityComparer)
    {
        var result = new List<T>();
        for (var i = 0; i < array.Count; i++)
        {
            PushIfUnique(result, array[i], equalityComparer);
        }

        return result;
    }

    // ported from: src/compiler/core.ts:735-760
    internal static IList<T> DeduplicateSorted<T>(IList<T> array, EqualityComparer<T> comparer)
    {
        if (array.Count == 0) return array;

        var last = array[0];
        var deduplicated = new List<T> { last };
        for (var i = 1; i < array.Count; i++)
        {
            var next = array[i];
            if (comparer(next, last))
            {
                continue;
            }

            last = next;
            deduplicated.Add(last);
        }

        return deduplicated;
    }

    // ported from: src/compiler/core.ts:763-765
    // PORT-NOTE: TS's SortedArray<T> brand type has no runtime representation in C#; a sorted array
    // is simply a List<T> that the caller keeps sorted.
    public static IList<T> CreateSortedArray<T>() => new List<T>();

    // ported from: src/compiler/core.ts:768-802
    public static bool InsertSorted<T>(
        List<T> array,
        T insert,
        Comparer<T> compare,
        EqualityComparer<T>? equalityComparer = null,
        bool allowDuplicates = false)
    {
        if (array.Count == 0)
        {
            array.Add(insert);
            return true;
        }

        var insertIndex = BinarySearch(array, insert, static v => v, compare);
        if (insertIndex < 0)
        {
            if (equalityComparer is not null && !allowDuplicates)
            {
                var idx = ~insertIndex;
                if (idx > 0 && equalityComparer(insert, array[idx - 1]))
                {
                    return false;
                }

                if (idx < array.Count && equalityComparer(insert, array[idx]))
                {
                    array[idx] = insert;
                    return true;
                }
            }

            array.Insert(~insertIndex, insert);
            return true;
        }

        if (allowDuplicates)
        {
            array.Insert(insertIndex, insert);
            return true;
        }

        return false;
    }

    // ported from: src/compiler/core.ts:805-811
    // PORT-NOTE: the TS string overload defaults its comparer to compareStringsCaseSensitive; in C# that
    // becomes an explicit overload so the generic version can keep comparer mandatory.
    public static IList<string> SortAndDeduplicate(IList<string> array)
        => SortAndDeduplicate(array, Core.CompareStringsCaseSensitive, Core.EquateStringsCaseSensitive);

    public static IList<T> SortAndDeduplicate<T>(IList<T> array, Comparer<T> comparer, EqualityComparer<T>? equalityComparer = null)
    {
        var equality = equalityComparer ?? ((a, b) => comparer(a, b) == Comparison.EqualTo);
        return DeduplicateSorted(ToSorted(array, comparer), equality);
    }

    // ported from: src/compiler/core.ts:814-830
    public static bool ArrayIsEqualTo<T>(
        IList<T>? array1,
        IList<T>? array2,
        Func<T, T, int, bool>? equalityComparer = null)
    {
        var comparer = equalityComparer ?? ((a, b, _) => EquateValues(a, b));
        if (array1 is null || array2 is null)
        {
            return ReferenceEquals(array1, array2);
        }

        if (array1.Count != array2.Count)
        {
            return false;
        }

        for (var i = 0; i < array1.Count; i++)
        {
            if (!comparer(array1[i], array2[i], i))
            {
                return false;
            }
        }

        return true;
    }

    // ported from: src/compiler/core.ts:846-862
    public static IList<T>? Compact<T>(IList<T>? array)
        where T : class
    {
        IList<T>? result = null;
        if (array is not null)
        {
            for (var i = 0; i < array.Count; i++)
            {
                var v = array[i];
                // Either the result has been initialized (and is looking to collect truthy values
                // separately), or we've hit our first falsy value and need to copy over the current
                // stretch of truthy values.
                if (result is not null || v is null)
                {
                    result ??= Slice(array, 0, i);
                    if (v is not null)
                    {
                        result.Add(v);
                    }
                }
            }
        }

        return result ?? array;
    }

    // ported from: src/compiler/core.ts:871-909
    public static IList<T>? RelativeComplement<T>(IList<T>? arrayA, IList<T>? arrayB, Comparer<T> comparer)
    {
        if (arrayB is null || arrayA is null || arrayB.Count == 0 || arrayA.Count == 0) return arrayB;
        var result = new List<T>();
        var offsetA = 0;
        for (var offsetB = 0; offsetB < arrayB.Count; offsetB++)
        {
            if (offsetB > 0)
            {
                // Ensure `arrayB` is properly sorted.
                Debug.AssertGreaterThanOrEqual((int)comparer(arrayB[offsetB], arrayB[offsetB - 1]), (int)Comparison.EqualTo);
            }

            for (var startA = offsetA; offsetA < arrayA.Count; offsetA++)
            {
                if (offsetA > startA)
                {
                    // Ensure `arrayA` is properly sorted. We only need to perform this check if
                    // `offsetA` has changed since we entered the loop.
                    Debug.AssertGreaterThanOrEqual((int)comparer(arrayA[offsetA], arrayA[offsetA - 1]), (int)Comparison.EqualTo);
                }

                // LessThan: B does not exist in arrayA - add B and move to the next element of arrayB.
                // EqualTo:  B exists in arrayA - move to the next element of arrayB without adding.
                // GreaterThan: keep looking for B further along arrayA.
                var c = comparer(arrayB[offsetB], arrayA[offsetA]);
                if (c != Comparison.GreaterThan)
                {
                    if (c == Comparison.LessThan)
                    {
                        result.Add(arrayB[offsetB]);
                    }

                    break; // continue loopB
                }
            }
        }

        return result;
    }

    // ported from: src/compiler/core.ts:926-931
    public static IList<T>? Append<T>(IList<T>? to, T? value)
        where T : class
    {
        if (value is null) return to;
        if (to is null) return [value];
        to.Add(value);
        return to;
    }

    // ported from: src/compiler/core.ts:965-967
    private static int ToOffset(int length, int offset) => offset < 0 ? length + offset : offset;

    // ported from: src/compiler/core.ts:985-996
    public static IList<T>? AddRange<T>(IList<T>? to, IList<T>? from, int? start = null, int? end = null)
    {
        if (from is null || from.Count == 0) return to;
        if (to is null) return Slice(from, start ?? 0, end ?? from.Count);
        var s = start is null ? 0 : ToOffset(from.Count, start.Value);
        var e = end is null ? from.Count : ToOffset(from.Count, end.Value);
        for (var i = s; i < e && i < from.Count; i++)
        {
            to.Add(from[i]);
        }

        return to;
    }

    // ported from: src/compiler/core.ts:1003-1011
    public static bool PushIfUnique<T>(IList<T> array, T toAdd, EqualityComparer<T>? equalityComparer = null)
    {
        if (Contains(array, toAdd, equalityComparer))
        {
            return false;
        }

        array.Add(toAdd);
        return true;
    }

    // ported from: src/compiler/core.ts:1018-1026
    public static IList<T> AppendIfUnique<T>(IList<T>? array, T toAdd, EqualityComparer<T>? equalityComparer = null)
    {
        if (array is not null)
        {
            PushIfUnique(array, toAdd, equalityComparer);
            return array;
        }

        return [toAdd];
    }

    // ported from: src/compiler/core.ts:1028-1031
    private static void StableSortIndices<T>(IList<T> array, List<int> indices, Comparer<T> comparer)
    {
        // sort indices by value then position
        StableSort(indices, (x, y) =>
        {
            var c = comparer(array[x], array[y]);
            return c != Comparison.EqualTo ? c : CompareValues(x, y);
        });
    }

    // ported from: src/compiler/core.ts:1038-1040
    public static IList<T> ToSorted<T>(IList<T> array, Comparer<T>? comparer = null)
    {
        var result = new List<T>(array);
        if (comparer is not null)
        {
            StableSort(result, comparer);
        }
        else
        {
            // PORT-NOTE: TS falls back to Array.prototype.sort's default (string) comparison here.
            StableSort(result, static (a, b) => CompareComparableValues(a?.ToString(), b?.ToString()));
        }

        return result;
    }

    // ported from: src/compiler/core.ts:1043-1047
    public static IEnumerable<T> ArrayReverseIterator<T>(IList<T> array)
    {
        for (var i = array.Count - 1; i >= 0; i--)
        {
            yield return array[i];
        }
    }

    // ported from: src/compiler/core.ts:1050-1058
    public static bool RangeEquals<T>(IList<T> array1, IList<T> array2, int pos, int end)
    {
        while (pos < end)
        {
            if (!EquateValues(array1[pos], array2[pos]))
            {
                return false;
            }

            pos++;
        }

        return true;
    }

    // ported from: src/compiler/core.ts:1066-1076
    public static T? ElementAt<T>(IList<T>? array, int offset)
        where T : class
    {
        if (array is not null)
        {
            offset = ToOffset(array.Count, offset);
            if (offset >= 0 && offset < array.Count)
            {
                return array[offset];
            }
        }

        return null;
    }

    // ported from: src/compiler/core.ts:1083-1095
    public static T? FirstOrUndefined<T>(IList<T>? array)
        where T : class
        => array is null || array.Count == 0 ? null : array[0];

    public static T? FirstOrUndefinedIterator<T>(IEnumerable<T>? iter)
        where T : class
    {
        if (iter is not null)
        {
            foreach (var value in iter)
            {
                return value;
            }
        }

        return null;
    }

    // ported from: src/compiler/core.ts:1098-1109
    public static T First<T>(IList<T> array)
    {
        Debug.Assert(array.Count != 0);
        return array[0];
    }

    public static T FirstIterator<T>(IEnumerable<T> iter)
    {
        foreach (var value in iter)
        {
            return value;
        }

        return Debug.Fail<T>("iterator is empty");
    }

    // ported from: src/compiler/core.ts:1116-1124
    public static T? LastOrUndefined<T>(IList<T>? array)
        where T : class
        => array is null || array.Count == 0 ? null : array[array.Count - 1];

    public static T Last<T>(IList<T> array)
    {
        Debug.Assert(array.Count != 0);
        return array[array.Count - 1];
    }

    // ported from: src/compiler/core.ts:1131-1144
    public static T? SingleOrUndefined<T>(IList<T>? array)
        where T : class
        => array is not null && array.Count == 1 ? array[0] : null;

    public static T Single<T>(IList<T> array)
        where T : class
        => Debug.CheckDefined(SingleOrUndefined(array));

    // ported from: src/compiler/core.ts:1167-1171
    public static IList<T> ReplaceElement<T>(IList<T> array, int index, T value)
    {
        var result = new List<T>(array);
        result[index] = value;
        return result;
    }

    // ported from: src/compiler/core.ts:1186-1188
    public static int BinarySearch<T, TU>(IList<T> array, T value, Func<T, TU> keySelector, Comparer<TU> keyComparer, int offset = 0)
        => BinarySearchKey(array, keySelector(value), (v, _) => keySelector(v), keyComparer, offset);

    // ported from: src/compiler/core.ts:1202-1225
    public static int BinarySearchKey<T, TU>(IList<T> array, TU key, Func<T, int, TU> keySelector, Comparer<TU> keyComparer, int offset = 0)
    {
        if (!Some(array))
        {
            return -1;
        }

        var low = offset;
        var high = array.Count - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            var midKey = keySelector(array[middle], middle);
            switch ((int)keyComparer(midKey, key))
            {
                case < 0:
                    low = middle + 1;
                    break;
                case 0:
                    return middle;
                default:
                    high = middle - 1;
                    break;
            }
        }

        return ~low;
    }

    // ported from: src/compiler/core.ts:1228-1254
    // PORT-NOTE: TS distinguishes "no initial value" via `arguments.length <= 2`; C# expresses that with
    // two overloads instead of a runtime arity check.
    public static TU ReduceLeft<T, TU>(IList<T>? array, Func<TU, T, int, TU> f, TU initial, int? start = null, int? count = null)
    {
        if (array is { Count: > 0 })
        {
            var size = array.Count;
            var pos = start is null || start < 0 ? 0 : start.Value;
            var end = count is null || pos + count > size - 1 ? size - 1 : pos + count.Value;
            var result = initial;
            while (pos <= end)
            {
                result = f(result, array[pos], pos);
                pos++;
            }

            return result;
        }

        return initial;
    }

    public static T? ReduceLeft<T>(IList<T>? array, Func<T, T, int, T> f, int? start = null, int? count = null)
        where T : class
    {
        if (array is { Count: > 0 })
        {
            var size = array.Count;
            var pos = start is null || start < 0 ? 0 : start.Value;
            var end = count is null || pos + count > size - 1 ? size - 1 : pos + count.Value;
            var result = array[pos];
            pos++;
            while (pos <= end)
            {
                result = f(result, array[pos], pos);
                pos++;
            }

            return result;
        }

        return null;
    }

    // ported from: src/compiler/core.ts:1324-1330
    public static IList<T> ArrayOf<T>(int count, Func<int, T> f)
    {
        var result = new List<T>(count);
        for (var i = 0; i < count; i++)
        {
            result.Add(f(i));
        }

        return result;
    }

    // ported from: src/compiler/core.ts:1341-1347
    public static List<TU> ArrayFrom<T, TU>(IEnumerable<T> iterator, Func<T, TU> map)
    {
        var result = new List<TU>();
        foreach (var value in iterator)
        {
            result.Add(map(value));
        }

        return result;
    }

    public static IList<T> ArrayFrom<T>(IEnumerable<T> iterator)
    {
        var result = new List<T>();
        foreach (var value in iterator)
        {
            result.Add(value);
        }

        return result;
    }

    // ported from: src/compiler/core.ts:1409-1417
    public static Dictionary<TK, TV> ArrayToMap<T, TK, TV>(IList<T> array, Func<T, TK?> makeKey, Func<T, TV> makeValue)
        where TK : class
    {
        var result = new Dictionary<TK, TV>();
        for (var i = 0; i < array.Count; i++)
        {
            var value = array[i];
            var key = makeKey(value);
            if (key is not null) result[key] = makeValue(value);
        }

        return result;
    }

    // ported from: src/compiler/core.ts:1438-1445
    public static MultiMap<TK, TU> ArrayToMultiMap<T, TK, TU>(IList<T> values, Func<T, TK> makeKey, Func<T, TU> makeValue)
        where TK : notnull
    {
        var result = CreateMultiMap<TK, TU>();
        for (var i = 0; i < values.Count; i++)
        {
            var value = values[i];
            result.Add(makeKey(value), makeValue(value));
        }

        return result;
    }

    // ported from: src/compiler/core.ts:1456-1458
    public static List<R> Group<T, TK, R>(IList<T> values, Func<T, TK> getGroupId, Func<IList<T>, R> resultSelector)
        where TK : notnull
    {
        var multiMap = ArrayToMultiMap(values, getGroupId, static v => v);
        var result = new List<R>(multiMap.Count);
        foreach (var group in multiMap.Values)
        {
            result.Add(resultSelector(group));
        }

        return result;
    }

    // ported from: src/compiler/core.ts:1464-1475
    public static Dictionary<string, IList<T>> GroupBy<T, TK>(IList<T>? values, Func<T, TK> keySelector)
        where TK : notnull
    {
        var result = new Dictionary<string, IList<T>>();
        if (values is not null)
        {
            for (var i = 0; i < values.Count; i++)
            {
                var value = values[i];
                var key = keySelector(value).ToString() ?? "";
                var array = GetOrUpdate(result, key, static () => new List<T>());
                array.Add(value);
            }
        }

        return result;
    }

    // ported from: src/compiler/core.ts:2325-2333
    // PORT-NOTE: TS uses `===` (reference equality for objects); EqualityComparer<T>.Default is the C#
    // equivalent for value types and matches it for types that do not override Equals.
    public static bool OrderedRemoveItem<T>(IList<T> array, T item)
    {
        for (var i = 0; i < array.Count; i++)
        {
            if (EquateValues(array[i], item))
            {
                array.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    // ported from: src/compiler/core.ts:2340-2348
    public static void OrderedRemoveItemAt<T>(IList<T> array, int index)
    {
        for (var i = index; i < array.Count - 1; i++)
        {
            array[i] = array[i + 1];
        }

        array.RemoveAt(array.Count - 1);
    }

    // ported from: src/compiler/core.ts:2359-2372
    public static bool UnorderedRemoveItem<T>(IList<T> array, T item)
    {
        for (var i = 0; i < array.Count; i++)
        {
            if (EquateValues(array[i], item))
            {
                if (i == array.Count - 1)
                {
                    array.RemoveAt(i);
                }
                else
                {
                    array[i] = array[array.Count - 1];
                    array.RemoveAt(array.Count - 1);
                }

                return true;
            }
        }

        return false;
    }

    // ported from: src/compiler/core.ts:2487-2489
    public static IList<T>? SingleElementArray<T>(T? t)
        where T : class
        => t is null ? null : [t];

    // ported from: src/compiler/core.ts:2492-2528
    public static bool EnumerateInsertsAndDeletes<T, TU>(
        IList<T> newItems,
        IList<TU> oldItems,
        Func<T, TU, Comparison> comparer,
        Action<T> inserted,
        Action<TU> deleted,
        Action<TU, T>? unchanged = null)
    {
        var newIndex = 0;
        var oldIndex = 0;
        var newLen = newItems.Count;
        var oldLen = oldItems.Count;
        var hasChanges = false;
        while (newIndex < newLen && oldIndex < oldLen)
        {
            var newItem = newItems[newIndex];
            var oldItem = oldItems[oldIndex];
            var compareResult = comparer(newItem, oldItem);
            if (compareResult == Comparison.LessThan)
            {
                inserted(newItem);
                newIndex++;
                hasChanges = true;
            }
            else if (compareResult == Comparison.GreaterThan)
            {
                deleted(oldItem);
                oldIndex++;
                hasChanges = true;
            }
            else
            {
                unchanged?.Invoke(oldItem, newItem);
                newIndex++;
                oldIndex++;
            }
        }

        while (newIndex < newLen)
        {
            inserted(newItems[newIndex++]);
            hasChanges = true;
        }

        while (oldIndex < oldLen)
        {
            deleted(oldItems[oldIndex++]);
            hasChanges = true;
        }

        return hasChanges;
    }

    // ported from: src/compiler/core.ts:2531-2554
    public static List<IList<T>> CartesianProduct<T>(IList<IList<T>> arrays)
    {
        var result = new List<IList<T>>();
        CartesianProductWorker(arrays, result, null, 0);
        return result;
    }

    private static void CartesianProductWorker<T>(IList<IList<T>> arrays, List<IList<T>> result, IList<T>? outer, int index)
    {
        foreach (var element in arrays[index])
        {
            List<T> inner;
            if (outer is not null)
            {
                inner = new List<T>(outer) { element };
            }
            else
            {
                inner = [element];
            }

            if (index == arrays.Count - 1)
            {
                result.Add(inner);
            }
            else
            {
                CartesianProductWorker(arrays, result, inner, index + 1);
            }
        }
    }

    // ported from: src/compiler/core.ts:2560-2585
    public static IList<T>? TakeWhile<T>(IList<T>? array, Func<T, bool> predicate)
    {
        if (array is not null)
        {
            var len = array.Count;
            var index = 0;
            while (index < len && predicate(array[index]))
            {
                index++;
            }

            return Slice(array, 0, index);
        }

        return null;
    }

    public static IList<T>? SkipWhile<T>(IList<T>? array, Func<T, bool> predicate)
    {
        if (array is not null)
        {
            var len = array.Count;
            var index = 0;
            while (index < len && predicate(array[index]))
            {
                index++;
            }

            return Slice(array, index, len);
        }

        return null;
    }

    // ---- shared helpers ----

    /// <summary>Shared helper: TS expresses this as <c>array.slice(start, end)</c>.</summary>
    internal static IList<T> Slice<T>(IList<T> array, int start, int end)
    {
        var result = new List<T>(Math.Max(0, end - start));
        for (var i = start; i < end; i++)
        {
            result.Add(array[i]);
        }

        return result;
    }

    /// <summary>ported from: src/compiler/core.ts:17 — <c>emptyArray</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>emptyArray</c> is one shared <c>never[]</c>, whose emptiness is a compile-time fact —
    /// nothing can write through a <c>never[]</c>. C# has no such element type, so the port keeps one shared instance
    /// per element type <em>in a nested class</em> and callers read <c>Core.EmptyArray&lt;T&gt;.Value</c>. The instance
    /// is shared rather than freshly allocated because two call sites in the checker test its identity
    /// (<c>type.resolvedBaseTypes === emptyArray</c>, checker.ts:13410); nothing in the port writes through it, which
    /// is the invariant the reference gets from the element type.
    /// </remarks>
    internal static class EmptyArray<T>
    {
        public static readonly List<T> Value = [];
    }

    /// <summary>
    /// A stable sort. TS relies on <c>Array.prototype.sort</c> (stable since ES2019); the BCL's
    /// <c>List.Sort</c>/<c>Array.Sort</c> are introspective and *not* stable, so ordering is made
    /// stable explicitly by breaking ties on the original index.
    /// </summary>
    internal static void StableSort<T>(IList<T> list, Comparer<T> comparer)
    {
        var count = list.Count;
        var items = new T[count];
        var indices = new int[count];
        for (var i = 0; i < count; i++)
        {
            items[i] = list[i];
            indices[i] = i;
        }

        Array.Sort(indices, (x, y) =>
        {
            var c = (int)comparer(items[x], items[y]);
            return c != 0 ? c : x - y;
        });

        for (var i = 0; i < count; i++)
        {
            list[i] = items[indices[i]];
        }
    }
}