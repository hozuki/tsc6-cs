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
    /// ICU's <c>u_strToUpper</c>/<c>u_strToLower</c>.
    /// </summary>
    /// <remarks>
    /// <c>UChar*</c> is UTF-16, so the source is marshalled as <c>LPWStr</c> and the destination as a
    /// <c>char[]</c> whose elements are explicitly two bytes — the surrounding <c>CharSet</c> must not leak into
    /// it, or the buffer comes back read as single bytes. The locale is a NUL-terminated C string and is always
    /// empty — ICU's root locale, which is what makes this locale-independent like JS's <c>toUpperCase</c>.
    /// </remarks>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int StrToCaseDelegate(
        [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2)] char[]? dest,
        int destCapacity,
        [MarshalAs(UnmanagedType.LPWStr)] string src,
        int srcLength,
        [MarshalAs(UnmanagedType.LPStr)] string locale,
        ref int errorCode);

    private static readonly StrToCaseDelegate? s_toUpper;
    private static readonly StrToCaseDelegate? s_toLower;

    static IcuCaseFolding()
    {
        if (!TryLoad("u_strToUpper", out var toUpper) || !TryLoad("u_strToLower", out var toLower))
        {
            Console.Error.WriteLine(
                "warning: ICU is unavailable, so case folding falls back to .NET's invariant mapping; results " +
                "will differ from V8 for characters such as 'ß' (see docs/architecture.md §10.2).");
            return;
        }

        s_toUpper = toUpper;
        s_toLower = toLower;
    }

    /// <summary>Whether the full case mapping is available; when false, the invariant fallback is in use.</summary>
    public static bool IsAvailable => s_toUpper is not null;

    public static string ToUpper(string value) => Convert(s_toUpper, value, static x => x.ToUpperInvariant());

    public static string ToLower(string value) => Convert(s_toLower, value, static x => x.ToLowerInvariant());

    private static string Convert(StrToCaseDelegate? convert, string value, Func<string, string> fallback)
    {
        if (convert is null) return fallback(value);
        if (value.Length == 0) return value;

        const string locale = "";
        var errorCode = ZeroError;

        // Measure first: ICU reports U_BUFFER_OVERFLOW_ERROR, which is expected on this pass.
        var length = convert(null, 0, value, value.Length, locale, ref errorCode);
        if (errorCode != BufferOverflowError && errorCode != ZeroError) return fallback(value);

        // ICU writes a terminator, so the buffer gets room for one.
        var buffer = new char[length + 1];
        errorCode = ZeroError;
        var written = convert(buffer, buffer.Length, value, value.Length, locale, ref errorCode);
        if (errorCode != ZeroError) return fallback(value);

        return new string(buffer, 0, written);
    }

    private static bool TryLoad(string symbol, out StrToCaseDelegate? convert)
    {
        foreach (var (library, symbolName) in CandidateLibraries(symbol))
        {
            if (!NativeLibrary.TryLoad(library, out var handle)) continue;
            if (NativeLibrary.TryGetExport(handle, symbolName, out var address))
            {
                convert = Marshal.GetDelegateForFunctionPointer<StrToCaseDelegate>(address);
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
