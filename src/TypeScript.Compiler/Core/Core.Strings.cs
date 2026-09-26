using System.Text.RegularExpressions;

namespace TypeScript.Compiler;

// ported from: src/compiler/core.ts:1835-1888, 2247-2485
/// <summary>
/// String and file-name helpers, plus the <c>prefix*suffix</c> pattern type. Equivalent to the
/// string-handling half of TS's core.ts.
/// </summary>
internal static partial class Core
{
    // ported from: src/compiler/core.ts:1828-1830
    /// <summary>Returns its argument.</summary>
    public static T Identity<T>(T x) => x;

    // ported from: src/compiler/core.ts:1835-1837 — JS's non-locale-aware `toLowerCase`.
    private static string ToLowerCase(string x) => x.ToLowerInvariant();

    // ported from: src/compiler/core.ts:1861
    // We convert file names to lower case as a key on case-insensitive file systems. While doing so we
    // need to handle special characters (e.g. \u0130, "I with dot above") so that a file name and its
    // lower-cased form can coexist. `a-z`, `0-9`, `\u0131`, `\u00DF`, `\`, `/`, `.`, `:` and space are
    // left alone to avoid string building in the common case.
    [GeneratedRegex(@"[^\u0130\u0131\u00DFa-z0-9\\/:\-_. ]+")]
    private static partial Regex FileNameLowerCaseRegExp();

    // ported from: src/compiler/core.ts:1875-1879
    /// <summary>
    /// Case-insensitive file systems disagree on how some characters are handled (e.g. Turkish upper
    /// case I with dot above, <c>\u0130</c>). This is the key-mangling function used on those systems.
    /// </summary>
    public static string ToFileNameLowerCase(string x)
        => FileNameLowerCaseRegExp().IsMatch(x)
            ? FileNameLowerCaseRegExp().Replace(x, static m => ToLowerCase(m.Value))
            : x;

    // ported from: src/compiler/core.ts:2375-2379
    /// <summary>TS: <c>GetCanonicalFileName = (fileName: string) =&gt; string</c>.</summary>
    public delegate string GetCanonicalFileName(string fileName);

    public static GetCanonicalFileName CreateGetCanonicalFileName(bool useCaseSensitiveFileNames)
        => useCaseSensitiveFileNames ? static fileName => fileName : ToFileNameLowerCase;

    /// <summary>The identity canonical-file-name function, used as the default in TS's <c>tryRemovePrefix</c>.</summary>
    public static readonly GetCanonicalFileName IdentityFileName = static fileName => fileName;

    // ported from: src/compiler/core.ts:2247-2264
    public static bool EndsWith(string str, string suffix, bool ignoreCase = false)
    {
        var expectedPos = str.Length - suffix.Length;
        return expectedPos >= 0
            && (ignoreCase
                ? EquateStringsCaseInsensitive(str[expectedPos..], suffix)
                : str.IndexOf(suffix, expectedPos, StringComparison.Ordinal) == expectedPos);
    }

    public static string RemoveSuffix(string str, string suffix)
        => EndsWith(str, suffix) ? str[..(str.Length - suffix.Length)] : str;

    public static string? TryRemoveSuffix(string str, string suffix)
        => EndsWith(str, suffix) ? str[..(str.Length - suffix.Length)] : null;

    // ported from: src/compiler/core.ts:2271-2318
    /// <summary>
    /// Takes a string like "jquery-min.4.2.3" and returns "jquery".
    /// </summary>
    public static string RemoveMinAndVersionNumbers(string fileName)
    {
        // We used to use the regex /[.-]((min)|(\d+(\.\d+)*))$/ and would just .replace it twice.
        // Unfortunately, that regex has O(n^2) performance because v8 doesn't match from the end of the
        // string. Instead, we now essentially scan the filename (backwards) ourselves.
        var end = fileName.Length;

        for (var pos = end - 1; pos > 0; pos--)
        {
            var ch = fileName[pos];
            if (ch >= CharacterCodes._0 && ch <= CharacterCodes._9)
            {
                // Match a \d+ segment
                do
                {
                    --pos;
                    ch = fileName[pos];
                }
                while (pos > 0 && ch >= CharacterCodes._0 && ch <= CharacterCodes._9);
            }
            else if (pos > 4 && (ch == CharacterCodes.n || ch == CharacterCodes.N))
            {
                // Looking for "min" or "MIN". Already matched the 'n'.
                --pos;
                ch = fileName[pos];
                if (ch != CharacterCodes.i && ch != CharacterCodes.I)
                {
                    break;
                }

                --pos;
                ch = fileName[pos];
                if (ch != CharacterCodes.m && ch != CharacterCodes.M)
                {
                    break;
                }

                --pos;
                ch = fileName[pos];
            }
            else
            {
                // This character is not part of either suffix pattern
                break;
            }

            if (ch != CharacterCodes.Minus && ch != CharacterCodes.Dot)
            {
                break;
            }

            end = pos;
        }

        // end might be fileName.length, in which case this should internally no-op
        return end == fileName.Length ? fileName : fileName[..end];
    }

    // ported from: src/compiler/core.ts:2430-2434
    // PORT-NOTE: TS tests `str.lastIndexOf(prefix, 0) === 0`. .NET's LastIndexOf(value, startIndex,
    // comparison) does *not* consider a match starting at startIndex when the value is longer than
    // startIndex + 1 (it returns -1 for `"./foo".LastIndexOf("./", 0, Ordinal)`), so the port asks the
    // question directly. The length guard reproduces JS's `str.slice(0, prefix.length)` behaviour when
    // the prefix is longer than the string.
    public static bool StartsWith(string str, string prefix, bool ignoreCase = false)
    {
        if (prefix.Length > str.Length)
        {
            return false;
        }

        return ignoreCase
            ? EquateStringsCaseInsensitive(str[..prefix.Length], prefix)
            : str.StartsWith(prefix, StringComparison.Ordinal);
    }

    public static string RemovePrefix(string str, string prefix)
        => StartsWith(str, prefix) ? str[prefix.Length..] : str;

    public static string? TryRemovePrefix(string str, string prefix, GetCanonicalFileName? getCanonicalFileName = null)
    {
        var canonical = getCanonicalFileName ?? IdentityFileName;
        return StartsWith(canonical(str), canonical(prefix)) ? str[prefix.Length..] : null;
    }

    // ported from: src/compiler/core.ts:2386-2451
    /// <summary>Represents a "prefix*suffix" pattern.</summary>
    internal readonly record struct Pattern(string Prefix, string Suffix);

    public static string PatternText(Pattern pattern) => $"{pattern.Prefix}*{pattern.Suffix}";

    /// <summary>
    /// Given that candidate matches pattern, returns the text matching the '*'.
    /// E.g.: <c>matchedText(tryParsePattern("foo*baz"), "foobarbaz") == "bar"</c>
    /// </summary>
    public static string MatchedText(Pattern pattern, string candidate)
    {
        Debug.Assert(IsPatternMatch(pattern, candidate));
        return candidate[pattern.Prefix.Length..(candidate.Length - pattern.Suffix.Length)];
    }

    /// <summary>Return the object corresponding to the best pattern to match <paramref name="candidate"/>.</summary>
    public static T? FindBestPatternMatch<T>(IReadOnlyList<T> values, Func<T, Pattern> getPattern, string candidate)
        where T : class
    {
        T? matchedValue = null;
        // use length of prefix as betterness criteria
        var longestMatchPrefixLength = -1;

        for (var i = 0; i < values.Count; i++)
        {
            var v = values[i];
            var pattern = getPattern(v);
            if (pattern.Prefix.Length > longestMatchPrefixLength && IsPatternMatch(pattern, candidate))
            {
                longestMatchPrefixLength = pattern.Prefix.Length;
                matchedValue = v;
            }
        }

        return matchedValue;
    }

    public static bool IsPatternMatch(Pattern pattern, string candidate)
        => candidate.Length >= pattern.Prefix.Length + pattern.Suffix.Length
            && StartsWith(candidate, pattern.Prefix)
            && EndsWith(candidate, pattern.Suffix);

    // ported from: src/compiler/core.ts:2454-2482
    public static Func<T, bool> And<T>(Func<T, bool> f, Func<T, bool> g) => arg => f(arg) && g(arg);

    public static Func<T, bool> Or<T>(Func<T, bool> f1, Func<T, bool> f2) => arg => f1(arg) || f2(arg);

    public static Func<T, bool> Or<T>(params Func<T, bool>[] fs)
        => arg =>
        {
            foreach (var f in fs)
            {
                if (f(arg))
                {
                    return true;
                }
            }

            return false;
        };

    public static Func<T, bool> Not<T>(Func<T, bool> fn) => arg => !fn(arg);

    // ported from: src/compiler/core.ts:2484-2486
    /// <summary>
    /// Asserts a value has the specified type in typespace only (does not perform a runtime assertion).
    /// </summary>
    // PORT-NOTE: TS's `assertType<T>(_: T)` is a compile-time-only device; C# has no counterpart and
    // expresses the same intent with an explicit cast or a `where T :` constraint.
}
