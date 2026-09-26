using System.Globalization;

namespace TypeScript.Compiler;

// ported from: src/compiler/core.ts:1937-2196
/// <summary>
/// Equality, ordering and spelling-suggestion helpers. Equivalent to the comparison half of TS's core.ts.
/// </summary>
/// <remarks>
/// PORT-NOTE: JS compares strings with <c>&lt;</c>/<c>&gt;</c>, which orders by UTF-16 code unit. C# has no
/// comparison operators on <see cref="string"/>, so every ordinal string comparison goes through
/// <see cref="string.CompareOrdinal(string, string)"/>, which has the same code-unit semantics.
/// <para>
/// PORT-NOTE: JS's <c>toUpperCase</c>/<c>toLowerCase</c> are locale-independent, and V8 applies the Unicode
/// <em>full</em> case mapping. The port therefore goes through <see cref="IcuCaseFolding"/>, which asks ICU for
/// that mapping and only falls back to .NET's invariant one when ICU cannot be reached — the fallback is where
/// ß-style characters start to differ from the reference.
/// </para>
/// </remarks>
internal static partial class Core
{
    // ported from: src/compiler/core.ts:1937-1939
    // PORT-NOTE: this namespace declares TS's `EqualityComparer<T>` delegate, which shadows the BCL
    // abstract class of the same name, so the BCL one is spelled out in full here.
    public static bool EquateValues<T>(T a, T b)
        => System.Collections.Generic.EqualityComparer<T>.Default.Equals(a, b);

    // ported from: src/compiler/core.ts:1951-1956
    /// <summary>
    /// Compare the equality of two strings using a case-insensitive ordinal comparison.
    /// </summary>
    /// <remarks>
    /// TS maps both strings to upper case as some unicode characters do not properly round-trip to
    /// lowercase (such as <c>ẞ</c>, German sharp capital s).
    /// </remarks>
    public static bool EquateStringsCaseInsensitive(string? a, string? b)
        => a == b
            || (a is not null
                && b is not null
                && IcuCaseFolding.ToUpper(a) == IcuCaseFolding.ToUpper(b));

    // ported from: src/compiler/core.ts:1966-1968
    public static bool EquateStringsCaseSensitive(string? a, string? b) => a == b;

    // ported from: src/compiler/core.ts:1974-1980
    public static Comparison CompareComparableValues(string? a, string? b)
    {
        if (a == b) return Comparison.EqualTo;
        if (a is null) return Comparison.LessThan;
        if (b is null) return Comparison.GreaterThan;
        return CompareStringsOrdinal(a, b);
    }

    public static Comparison CompareComparableValues(int? a, int? b)
    {
        if (a == b) return Comparison.EqualTo;
        if (a is null) return Comparison.LessThan;
        if (b is null) return Comparison.GreaterThan;
        return a < b ? Comparison.LessThan : Comparison.GreaterThan;
    }

    // ported from: src/compiler/core.ts:1988-1990
    public static Comparison CompareValues(int? a, int? b) => CompareComparableValues(a, b);

    // ported from: src/compiler/core.ts:2002-2007
    public static double MaxBy<T>(IList<T> arr, double init, Func<T, double> mapper)
    {
        for (var i = 0; i < arr.Count; i++)
        {
            init = Math.Max(init, mapper(arr[i]));
        }

        return init;
    }

    // ported from: src/compiler/core.ts:2010-2016
    public static T? Min<T>(IList<T> items, Comparer<T> compare)
        where T : class
        => ReduceLeft(items, (x, y, _) => compare(x, y) == Comparison.LessThan ? x : y);

    // ported from: src/compiler/core.ts:2032-2039
    /// <summary>
    /// Compare two strings using a case-insensitive ordinal comparison.
    /// </summary>
    public static Comparison CompareStringsCaseInsensitive(string? a, string? b)
    {
        if (a == b) return Comparison.EqualTo;
        if (a is null) return Comparison.LessThan;
        if (b is null) return Comparison.GreaterThan;
        return CompareStringsOrdinal(IcuCaseFolding.ToUpper(a), IcuCaseFolding.ToUpper(b));
    }

    // ported from: src/compiler/core.ts:2053-2060
    /// <summary>
    /// Like <see cref="CompareStringsCaseInsensitive"/>, but lower-cases instead of upper-casing so that
    /// the ordering agrees with eslint's <c>sort-imports</c> rule.
    /// </summary>
    public static Comparison CompareStringsCaseInsensitiveEslintCompatible(string? a, string? b)
    {
        if (a == b) return Comparison.EqualTo;
        if (a is null) return Comparison.LessThan;
        if (b is null) return Comparison.GreaterThan;
        return CompareStringsOrdinal(IcuCaseFolding.ToLower(a), IcuCaseFolding.ToLower(b));
    }

    // ported from: src/compiler/core.ts:2074-2076
    public static Comparison CompareStringsCaseSensitive(string? a, string? b) => CompareComparableValues(a, b);

    // ported from: src/compiler/core.ts:2079-2081
    public static Comparer<string?> GetStringComparer(bool ignoreCase = false)
        => ignoreCase ? CompareStringsCaseInsensitive : CompareStringsCaseSensitive;

    // ported from: src/compiler/core.ts:2086-2136
    // PORT-NOTE: TS builds the UI comparer from `Intl.Collator(locale, { usage: "sort", sensitivity:
    // "variant", numeric: true })`. C# does not expose numeric collation through StringComparer, so the
    // port uses a culture-aware StringComparer with CompareOptions.StringSort. Ordering for UI display
    // may differ from the JS implementation for strings containing digit runs.
    private static Comparer<string?>? _uiComparerCaseSensitive;
    private static string? _uiLocale;

    public static string? GetUILocale() => _uiLocale;

    public static void SetUILocale(string? value)
    {
        if (_uiLocale != value)
        {
            _uiLocale = value;
            _uiComparerCaseSensitive = null;
        }
    }

    /// <summary>
    /// Compare two strings using the case-sensitive sort behavior of the UI locale.
    /// </summary>
    public static Comparison CompareStringsCaseSensitiveUI(string? a, string? b)
    {
        _uiComparerCaseSensitive ??= CreateUIStringComparer(_uiLocale);
        return _uiComparerCaseSensitive(a, b);
    }

    private static Comparer<string?> CreateUIStringComparer(string? locale)
    {
        var culture = locale is null ? CultureInfo.CurrentCulture : CultureInfo.GetCultureInfo(locale);
        var stringComparer = StringComparer.Create(culture, CompareOptions.StringSort);
        return (a, b) => CompareWithCallback(a, b, stringComparer.Compare);
    }

    private static Comparison CompareWithCallback(string? a, string? b, Func<string, string, int> comparer)
    {
        if (a == b) return Comparison.EqualTo;
        if (a is null) return Comparison.LessThan;
        if (b is null) return Comparison.GreaterThan;
        var value = comparer(a, b);
        return value < 0 ? Comparison.LessThan : value > 0 ? Comparison.GreaterThan : Comparison.EqualTo;
    }

    // ported from: src/compiler/core.ts:2139-2144
    public static Comparison CompareProperties<T, TK>(T? a, T? b, Func<T, TK> key, Comparer<TK> comparer)
        where T : class
    {
        if (ReferenceEquals(a, b)) return Comparison.EqualTo;
        if (a is null) return Comparison.LessThan;
        if (b is null) return Comparison.GreaterThan;
        return comparer(key(a), key(b));
    }

    // ported from: src/compiler/core.ts:2151-2153
    /// <summary>True is greater than false.</summary>
    public static Comparison CompareBooleans(bool a, bool b) => CompareValues(a ? 1 : 0, b ? 1 : 0);

    // ported from: src/compiler/core.ts:2169-2196
    /// <summary>
    /// Given a name and a list of names that are *not* equal to the name, return a spelling suggestion if
    /// there is one that is close enough. Names less than length 3 only check for case-insensitive equality.
    /// <para>
    /// Find the candidate with the smallest Levenshtein distance, except for candidates: with no name;
    /// whose length differs from the target name by more than 0.34 of the length of the name; whose
    /// Levenshtein distance is more than 0.4 of the length of the name (0.4 allows 1 substitution or
    /// transposition for every 5 characters, and 1 insertion/deletion at 3 characters).
    /// </para>
    /// </summary>
    public static T? GetSpellingSuggestion<T>(string name, IEnumerable<T> candidates, Func<T, string?> getName)
        where T : class
    {
        var maximumLengthDifference = Math.Max(2, (int)Math.Floor(name.Length * 0.34));
        var bestDistance = (int)Math.Floor(name.Length * 0.4) + 1; // If the best result is worse than this, don't bother.
        T? bestCandidate = null;
        foreach (var candidate in candidates)
        {
            var candidateName = getName(candidate);
            if (candidateName is not null && Math.Abs(candidateName.Length - name.Length) <= maximumLengthDifference)
            {
                if (candidateName == name)
                {
                    continue;
                }

                // Only consider candidates less than 3 characters long when they differ by case.
                // Otherwise, don't bother, since a user would usually notice differences of a 2-character name.
                if (candidateName.Length < 3 && IcuCaseFolding.ToLower(candidateName) != IcuCaseFolding.ToLower(name))
                {
                    continue;
                }

                var distance = LevenshteinWithMax(name, candidateName, bestDistance - 0.1);
                if (distance is null)
                {
                    continue;
                }

                Debug.Assert(distance.Value < bestDistance); // Else `levenshteinWithMax` should return undefined
                bestDistance = distance.Value;
                bestCandidate = candidate;
            }
        }

        return bestCandidate;
    }

    // ported from: src/compiler/core.ts:2198-2244
    private static int? LevenshteinWithMax(string s1, string s2, double max)
    {
        var previous = new double[s2.Length + 1];
        var current = new double[s2.Length + 1];
        /** Represents any value > max. We don't care about the particular value. */
        var big = max + 0.01;

        for (var i = 0; i <= s2.Length; i++)
        {
            previous[i] = i;
        }

        for (var i = 1; i <= s1.Length; i++)
        {
            var c1 = s1[i - 1];
            var minJ = (int)Math.Ceiling(i > max ? i - max : 1);
            var maxJ = (int)Math.Floor(s2.Length > max + i ? max + i : s2.Length);
            current[0] = i;
            /** Smallest value of the matrix in the ith column. */
            var colMin = (double)i;
            for (var j = 1; j < minJ; j++)
            {
                current[j] = big;
            }

            for (var j = minJ; j <= maxJ; j++)
            {
                // case difference should be significantly cheaper than other differences
                var substitutionDistance = IcuCaseFolding.ToLower(s1[i - 1].ToString()) == IcuCaseFolding.ToLower(s2[j - 1].ToString())
                    ? previous[j - 1] + 0.1
                    : previous[j - 1] + 2;
                var dist = c1 == s2[j - 1]
                    ? previous[j - 1]
                    : Math.Min(Math.Min(/*delete*/ previous[j] + 1, /*insert*/ current[j - 1] + 1), /*substitute*/ substitutionDistance);
                current[j] = dist;
                colMin = Math.Min(colMin, dist);
            }

            for (var j = maxJ + 1; j <= s2.Length; j++)
            {
                current[j] = big;
            }

            if (colMin > max)
            {
                // Give up -- everything in this column is > max and it can't get better in future columns.
                return null;
            }

            (previous, current) = (current, previous);
        }

        var res = previous[s2.Length];
        return res > max ? null : (int)res;
    }

    /// <summary>
    /// JS's <c>a &lt; b ? LessThan : GreaterThan</c> on strings, kept in one place so the ordinal-comparison
    /// choice cannot drift between call sites.
    /// </summary>
    private static Comparison CompareStringsOrdinal(string a, string b)
    {
        var c = string.CompareOrdinal(a, b);
        return c < 0 ? Comparison.LessThan : c > 0 ? Comparison.GreaterThan : Comparison.EqualTo;
    }
}
