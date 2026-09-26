using System.Runtime.CompilerServices;

namespace TypeScript.Compiler;

// NOT ported from TS — this has no counterpart there.
/// <summary>
/// Flag arithmetic that TS writes with operators C# enums do not support.
/// </summary>
/// <remarks>
/// C# enums define <c>&amp;</c>, <c>|</c> and <c>^</c> but <em>not</em> unary <c>~</c>, so TS's
/// <c>flags &amp; ~mask</c> has no direct translation. The round trip through the underlying type is
/// <c>(T)((U)flags &amp; ~(U)mask)</c>; wrapping it here keeps call sites recognisable next to their TS
/// original instead of scattering casts.
/// <para>
/// Note the difference from the generated enum <em>initializers</em> (see <c>ExpressionEvaluator.Rewrite</c>):
/// a member initializer must be a constant of the <em>underlying</em> type, so there the expression is left
/// as <c>(uint)A &amp; ~(uint)B</c> with no cast back. Here the result has to be the enum type again.
/// </para>
/// <para>
/// The width switch is resolved at JIT time, so there is no boxing and no per-call branching cost.
/// </para>
/// </remarks>
internal static class Flags
{
    /// <summary>TS: <c>value &amp; ~mask</c> — clears the bits of <paramref name="mask"/>.</summary>
    public static T Clear<T>(T value, T mask)
        where T : struct, Enum
        => Transform(value, mask, static (v, m) => v & ~m);

    /// <summary>
    /// TS: <c>(value &amp; mask) !== 0</c> — true when <em>any</em> bit of <paramref name="mask"/> is set.
    /// This is the shape TS uses most often, and it is faster than <see cref="Enum.HasFlag"/> (which boxes).
    /// </summary>
    public static bool HasAny<T>(T value, T mask)
        where T : struct, Enum
    {
        var size = Unsafe.SizeOf<T>();
        if (size == 4) return (Unsafe.As<T, uint>(ref value) & Unsafe.As<T, uint>(ref mask)) != 0;
        if (size == 8) return (Unsafe.As<T, ulong>(ref value) & Unsafe.As<T, ulong>(ref mask)) != 0;
        throw new NotSupportedException($"Flags supports 4- and 8-byte enums; {typeof(T).Name} is {size} bytes.");
    }

    /// <summary>
    /// TS: <c>(value &amp; mask) === mask</c> — true when <em>every</em> bit of <paramref name="mask"/> is set
    /// (the same question <see cref="Enum.HasFlag"/> answers).
    /// </summary>
    public static bool HasAll<T>(T value, T mask)
        where T : struct, Enum
    {
        var size = Unsafe.SizeOf<T>();
        if (size == 4)
        {
            var m = Unsafe.As<T, uint>(ref mask);
            return (Unsafe.As<T, uint>(ref value) & m) == m;
        }

        if (size == 8)
        {
            var m = Unsafe.As<T, ulong>(ref mask);
            return (Unsafe.As<T, ulong>(ref value) & m) == m;
        }

        throw new NotSupportedException($"Flags supports 4- and 8-byte enums; {typeof(T).Name} is {size} bytes.");
    }

    /// <summary>TS: <c>value | mask</c>. Present for symmetry; C# already supports <c>|</c> on enums.</summary>
    public static T Set<T>(T value, T mask)
        where T : struct, Enum
        => Transform(value, mask, static (v, m) => v | m);

    private static T Transform<T>(T value, T mask, Func<uint, uint, uint> operation)
        where T : struct, Enum
    {
        var size = Unsafe.SizeOf<T>();
        if (size == 4)
        {
            var result = operation(Unsafe.As<T, uint>(ref value), Unsafe.As<T, uint>(ref mask));
            return Unsafe.As<uint, T>(ref result);
        }

        if (size == 8)
        {
            // Reuse the 32-bit operation per half; the masks in this codebase never exceed 32 bits, but
            // widening here keeps the helper honest for any 64-bit flag enum.
            var v = Unsafe.As<T, ulong>(ref value);
            var m = Unsafe.As<T, ulong>(ref mask);
            var low = (ulong)operation((uint)v, (uint)m);
            var high = (ulong)operation((uint)(v >> 32), (uint)(m >> 32));
            var wide = low | (high << 32);
            return Unsafe.As<ulong, T>(ref wide);
        }

        throw new NotSupportedException($"Flags supports 4- and 8-byte enums; {typeof(T).Name} is {size} bytes.");
    }
}
