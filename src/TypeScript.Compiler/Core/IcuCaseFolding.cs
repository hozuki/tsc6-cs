using System.Runtime.InteropServices;

namespace TypeScript.Compiler;

// ported from: the places where TS calls `String.prototype.toUpperCase` / `toLowerCase`
/// <summary>
/// Case conversion for the ported call sites that TS writes as <c>toUpperCase()</c>/<c>toLowerCase()</c>.
/// </summary>
/// <remarks>
/// V8 implements those with the Unicode <em>full</em> case mapping, so <c>"ß".toUpperCase()</c> is <c>"SS"</c>.
/// .NET's <c>ToUpperInvariant</c> is a length-preserving <em>simple</em> mapping, so
/// <c>equateStringsCaseInsensitive("ss", "ß")</c> answers differently from TS. The full mapping lives in ICU, which
/// this class reaches through a platform probe because ICU is not a managed dependency.
/// <para>
/// The entry points are held as <c>delegate* unmanaged[Cdecl]</c> rather than as marshalled delegates: nothing
/// about the call is managed (the strings are pinned by hand, the locale is a NUL-terminated byte), so a delegate
/// wrapper would only add an indirection and a marshalling layer. Callers never see the pointer — the public
/// surface is the ordinary <see cref="ToUpper"/>/<see cref="ToLower"/> pair.
/// </para>
/// <para>
/// PORT-NOTE: Windows ships <c>icuuc.dll</c> with unrenamed symbols; Unix builds rename them with the ICU major
/// version (<c>u_strToUpper_74</c>) and carry that version in the file name. Both shapes are probed. When neither
/// is found the port falls back to .NET's invariant mapping and says so once on stderr — a silent fallback would
/// turn a behavioural difference into a mystery (docs/architecture.md §10.2).
/// </para>
/// </remarks>
internal static class IcuCaseFolding
{
    /// <summary>ICU's <c>U_ZERO_ERROR</c>.</summary>
    private const int ZeroError = 0;

    /// <summary>ICU's <c>U_BUFFER_OVERFLOW_ERROR</c> — what the measuring call reports.</summary>
    private const int BufferOverflowError = 15;

    /// <summary>The ICU releases the symbol suffix is probed for, newest first.</summary>
    private const int HighestProbedIcuVersion = 80;

    private const int LowestProbedIcuVersion = 60;

    /// <summary>
    /// <c>int32_t u_strToUpper(UChar* dest, int32_t destCapacity, const UChar* src, int32_t srcLength,
    /// const char* locale, UErrorCode* pErrorCode)</c> — and the same shape for <c>u_strToLower</c>.
    /// </summary>
    private static unsafe delegate* unmanaged[Cdecl]<char*, int, char*, int, byte*, int*, int> s_toUpper;

    private static unsafe delegate* unmanaged[Cdecl]<char*, int, char*, int, byte*, int*, int> s_toLower;

    static IcuCaseFolding()
    {
        unsafe
        {
            if (!TryLoad("u_strToUpper", out s_toUpper) || !TryLoad("u_strToLower", out s_toLower))
            {
                Console.Error.WriteLine(
                    "warning: ICU is unavailable, so case folding falls back to .NET's invariant mapping; results " +
                    "will differ from V8 for characters such as 'ß' (see docs/architecture.md §10.2).");
            }
        }
    }

    /// <summary>Whether the full case mapping is available; when false, the invariant fallback is in use.</summary>
    public static unsafe bool IsAvailable => s_toUpper != null;

    public static unsafe string ToUpper(string value) => Convert(s_toUpper, value, static x => x.ToUpperInvariant());

    public static unsafe string ToLower(string value) => Convert(s_toLower, value, static x => x.ToLowerInvariant());

    private static unsafe string Convert(
        delegate* unmanaged[Cdecl]<char*, int, char*, int, byte*, int*, int> convert,
        string value,
        Func<string, string> fallback)
    {
        if (convert is null) return fallback(value);
        if (value.Length == 0) return value;

        // ICU's root locale is the empty C string, which is what makes this locale-independent like JS's
        // toUpperCase.
        byte rootLocale = 0;

        fixed (char* src = value)
        {
            var errorCode = ZeroError;

            // Measure first: ICU reports U_BUFFER_OVERFLOW_ERROR, which is expected on this pass.
            var length = convert(null, 0, src, value.Length, &rootLocale, &errorCode);
            if (errorCode != BufferOverflowError && errorCode != ZeroError) return fallback(value);
            if (length == 0) return value;

            // ICU writes a terminator, so the buffer gets room for one.
            var buffer = new char[length + 1];
            fixed (char* dest = buffer)
            {
                errorCode = ZeroError;
                var written = convert(dest, buffer.Length, src, value.Length, &rootLocale, &errorCode);
                if (errorCode != ZeroError) return fallback(value);

                return new string(buffer, 0, written);
            }
        }
    }

    private static unsafe bool TryLoad(
        string symbol,
        out delegate* unmanaged[Cdecl]<char*, int, char*, int, byte*, int*, int> convert)
    {
        foreach (var (library, symbolName) in CandidateLibraries(symbol))
        {
            if (!NativeLibrary.TryLoad(library, out var handle)) continue;
            if (NativeLibrary.TryGetExport(handle, symbolName, out var address))
            {
                convert = (delegate* unmanaged[Cdecl]<char*, int, char*, int, byte*, int*, int>)address;
                return true;
            }
        }

        convert = null;
        return false;
    }

    /// <summary>
    /// The library/symbol name pairs to try, plain names first.
    /// </summary>
    private static IEnumerable<(string Library, string Symbol)> CandidateLibraries(string symbol)
    {
        yield return ("icuuc", symbol);
        yield return ("icu", symbol);
        yield return ("libicuuc", symbol);
        yield return ("libicuuc.so", symbol);
        yield return ("libicucore.dylib", symbol);

        // Unix builds rename the exported symbols with the ICU major version, and the same version is in the
        // file name — so a probe over recent releases covers the case without pinning one.
        for (var version = HighestProbedIcuVersion; version >= LowestProbedIcuVersion; version--)
        {
            yield return ($"libicuuc.so.{version}", $"{symbol}_{version}");
        }
    }
}
