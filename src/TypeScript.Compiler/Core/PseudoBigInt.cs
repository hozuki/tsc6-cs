using System.Globalization;

namespace TypeScript.Compiler;

// ported from: src/compiler/utilities.ts:10461-10526
/// <summary>
/// Turns the text of a BigInt literal into its decimal digit string.
/// </summary>
/// <remarks>
/// TS calls the result a "pseudo-BigInt": the compiler never needs the value itself, only a canonical decimal
/// spelling for the emitted output and for diagnostics, so it works on the digits instead of using a big-integer
/// type. The port keeps the same algorithm — the base-2/8/16 digits are packed into 16-bit segments which are then
/// divided by 10 repeatedly — because the observable result is the digit string.
/// </remarks>
internal static class PseudoBigInt
{
    // ported from: src/compiler/utilities.ts:10461-10526
    /// <summary>Converts a literal such as <c>0x1Fn</c> to <c>"31"</c>.</summary>
    public static string Parse(string stringValue)
    {
        int log2Base;
        // TS reads `charCodeAt(1)`, which is NaN — and therefore matches no case — for a one-character input.
        switch (stringValue.Length > 1 ? stringValue[1] : -1)
        {
            case CharacterCodes.b:
            case CharacterCodes.B: // 0b or 0B
                log2Base = 1;
                break;
            case CharacterCodes.o:
            case CharacterCodes.O: // 0o or 0O
                log2Base = 3;
                break;
            case CharacterCodes.x:
            case CharacterCodes.X: // 0x or 0X
                log2Base = 4;
                break;
            default:
            {
                // Already in decimal; omit the trailing "n".
                var nIndex = stringValue.Length - 1;

                // Skip leading 0s.
                var nonZeroStart = 0;
                while (nonZeroStart < nIndex && stringValue[nonZeroStart] == CharacterCodes._0) nonZeroStart++;

                var digits = stringValue[nonZeroStart..nIndex];
                return digits.Length != 0 ? digits : "0";
            }
        }

        // Omit the leading "0b"/"0o"/"0x" and the trailing "n".
        const int startIndex = 2;
        var endIndex = stringValue.Length - 1;
        var bitsNeeded = (endIndex - startIndex) * log2Base;

        // Stores the value as a LE array of 16-bit integers, using 16 rather than 32 bits so that the
        // combining steps below can use bitwise operators.
        var segments = new ushort[(bitsNeeded >>> 4) + ((bitsNeeded & 15) != 0 ? 1 : 0)];

        // Add the digits, one at a time.
        var bitOffset = 0;
        for (var i = endIndex - 1; i >= startIndex; i--, bitOffset += log2Base)
        {
            var segment = bitOffset >>> 4;
            var digitChar = stringValue[i];

            // Find the character range: 0-9 < A-F < a-f.
            var digit = digitChar <= CharacterCodes._9
                ? digitChar - CharacterCodes._0
                : 10 + digitChar - (digitChar <= CharacterCodes.F ? CharacterCodes.A : CharacterCodes.a);

            var shiftedDigit = digit << (bitOffset & 15);
            segments[segment] = (ushort)(segments[segment] | shiftedDigit);

            var residual = shiftedDigit >>> 16;
            // TS writes this into a Uint16Array, which silently drops an out-of-range index.
            if (residual != 0 && segment + 1 < segments.Length)
            {
                segments[segment + 1] = (ushort)(segments[segment + 1] | residual);
            }
        }

        // Repeatedly divide the segments by 10, collecting the remainders.
        var base10Value = "";
        var firstNonzeroSegment = segments.Length - 1;
        var segmentsRemaining = true;
        while (segmentsRemaining)
        {
            var mod10 = 0;
            segmentsRemaining = false;
            for (var segment = firstNonzeroSegment; segment >= 0; segment--)
            {
                var newSegment = (mod10 << 16) | segments[segment];
                var segmentValue = newSegment / 10;
                segments[segment] = (ushort)segmentValue;
                mod10 = newSegment - (segmentValue * 10);
                if (segmentValue != 0 && !segmentsRemaining)
                {
                    firstNonzeroSegment = segment;
                    segmentsRemaining = true;
                }
            }

            base10Value = mod10.ToString(CultureInfo.InvariantCulture) + base10Value;
        }

        return base10Value;
    }
}
