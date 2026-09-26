using System.Collections;

namespace TypeScript.Compiler;

// ported from: src/compiler/sourcemap.ts:436-652 — the mapping shape, the base64 VLQ codec and `decodeMappings`.

/// <summary>ported from: src/compiler/sourcemap.ts:443-451 — <c>Mapping</c>.</summary>
/// <remarks>
/// PORT-NOTE: TS declares <c>Mapping</c> as an interface and builds mappings as object literals; the port needs a
/// constructible reference type (which <see cref="SourceMap.SameMapping"/>'s identity arm also relies on). The four
/// optional members are <c>int?</c>, the port's spelling of TS's <c>number | undefined</c>.
/// <para>
/// PORT-NOTE: TS also declares <c>SourceMapping</c> (sourcemap.ts:453-458) as the narrowing of <c>Mapping</c> a type
/// guard produces. C# has no such narrowing, so the port keeps the guard as
/// <see cref="SourceMap.IsSourceMapping"/> and reads the three members it proves through the <c>!</c> of the values the
/// guard established — never a fallback.
/// </para>
/// </remarks>
internal sealed class Mapping
{
    /// <summary>ported from: src/compiler/sourcemap.ts:445 — <c>generatedLine</c>.</summary>
    public int GeneratedLine { get; set; }

    /// <summary>ported from: src/compiler/sourcemap.ts:446 — <c>generatedCharacter</c>.</summary>
    public int GeneratedCharacter { get; set; }

    /// <summary>ported from: src/compiler/sourcemap.ts:447 — <c>sourceIndex</c>.</summary>
    public int? SourceIndex { get; set; }

    /// <summary>ported from: src/compiler/sourcemap.ts:448 — <c>sourceLine</c>.</summary>
    public int? SourceLine { get; set; }

    /// <summary>ported from: src/compiler/sourcemap.ts:449 — <c>sourceCharacter</c>.</summary>
    public int? SourceCharacter { get; set; }

    /// <summary>ported from: src/compiler/sourcemap.ts:450 — <c>nameIndex</c>.</summary>
    public int? NameIndex { get; set; }
}

/// <summary>ported from: src/compiler/sourcemap.ts:436-441, 461-616 — <c>MappingsDecoder</c> and
/// <c>decodeMappings</c>.</summary>
/// <remarks>
/// PORT-NOTE: TS's decoder is a hand-written iterator object that is its own <c>[Symbol.iterator]</c>; the port states it
/// as the BCL pair of that shape — <see cref="IEnumerable{T}"/> over <see cref="IEnumerator{T}"/> — so a <c>foreach</c>
/// advances one mapping at a time exactly as the reference's <c>for…of</c> does, and <see cref="Pos"/>,
/// <see cref="Error"/> and <see cref="State"/> are read the same way afterwards.
/// </remarks>
internal sealed class MappingsDecoder : IEnumerable<Mapping>, IEnumerator<Mapping>
{
    private readonly string _mappings;
    private bool _done;
    private int _pos;
    private int _generatedLine;
    private int _generatedCharacter;
    private int _sourceIndex;
    private int _sourceLine;
    private int _sourceCharacter;
    private int _nameIndex;
    private string? _error;
    private Mapping? _current;

    /// <summary>ported from: src/compiler/sourcemap.ts:461-479 — <c>decodeMappings</c>'s state.</summary>
    internal MappingsDecoder(string mappings) => _mappings = mappings;

    /// <summary>ported from: src/compiler/sourcemap.ts:438 — <c>pos</c>.</summary>
    public int Pos => _pos;

    /// <summary>ported from: src/compiler/sourcemap.ts:439 — <c>error</c>.</summary>
    public string? Error => _error;

    /// <summary>ported from: src/compiler/sourcemap.ts:440 — <c>state</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>state</c> is <c>Required&lt;Mapping&gt;</c> because it captures with both flags set;
    /// the port states the same capture, whose members are therefore all present.</remarks>
    public Mapping State => CaptureMapping(hasSource: true, hasName: true);

    /// <summary>ported from: src/compiler/sourcemap.ts:492-536 — <c>next</c>.</summary>
    public bool MoveNext()
    {
        while (!_done && _pos < _mappings.Length)
        {
            var ch = (int)_mappings[_pos];
            if (ch == CharacterCodes.Semicolon)
            {
                // new line
                _generatedLine++;
                _generatedCharacter = 0;
                _pos++;
                continue;
            }

            if (ch == CharacterCodes.Comma)
            {
                // Next entry is on same line - no action needed
                _pos++;
                continue;
            }

            var hasSource = false;
            var hasName = false;

            _generatedCharacter += Base64VLQFormatDecode();
            if (HasReportedError()) return StopIterating();
            if (_generatedCharacter < 0) return SetErrorAndStopIterating("Invalid generatedCharacter found");

            if (!IsSourceMappingSegmentEnd())
            {
                hasSource = true;

                _sourceIndex += Base64VLQFormatDecode();
                if (HasReportedError()) return StopIterating();
                if (_sourceIndex < 0) return SetErrorAndStopIterating("Invalid sourceIndex found");
                if (IsSourceMappingSegmentEnd()) return SetErrorAndStopIterating("Unsupported Format: No entries after sourceIndex");

                _sourceLine += Base64VLQFormatDecode();
                if (HasReportedError()) return StopIterating();
                if (_sourceLine < 0) return SetErrorAndStopIterating("Invalid sourceLine found");
                if (IsSourceMappingSegmentEnd()) return SetErrorAndStopIterating("Unsupported Format: No entries after sourceLine");

                _sourceCharacter += Base64VLQFormatDecode();
                if (HasReportedError()) return StopIterating();
                if (_sourceCharacter < 0) return SetErrorAndStopIterating("Invalid sourceCharacter found");

                if (!IsSourceMappingSegmentEnd())
                {
                    hasName = true;
                    _nameIndex += Base64VLQFormatDecode();
                    if (HasReportedError()) return StopIterating();
                    if (_nameIndex < 0) return SetErrorAndStopIterating("Invalid nameIndex found");

                    if (!IsSourceMappingSegmentEnd()) return SetErrorAndStopIterating("Unsupported Error Format: Entries after nameIndex");
                }
            }

            _current = CaptureMapping(hasSource, hasName);
            return true;
        }

        StopIterating();
        return false;
    }

    /// <summary>ported from: src/compiler/sourcemap.ts:544-556 — <c>captureMapping</c>.</summary>
    private Mapping CaptureMapping(bool hasSource, bool hasName) => new()
    {
        GeneratedLine = _generatedLine,
        GeneratedCharacter = _generatedCharacter,
        SourceIndex = hasSource ? _sourceIndex : null,
        SourceLine = hasSource ? _sourceLine : null,
        SourceCharacter = hasSource ? _sourceCharacter : null,
        NameIndex = hasName ? _nameIndex : null,
    };

    /// <summary>ported from: src/compiler/sourcemap.ts:557-560 — <c>stopIterating</c>.</summary>
    private bool StopIterating()
    {
        _done = true;
        _current = null;
        return false;
    }

    /// <summary>ported from: src/compiler/sourcemap.ts:562-566 — <c>setError</c>.</summary>
    private void SetError(string message) => _error ??= message;

    /// <summary>ported from: src/compiler/sourcemap.ts:568-571 — <c>setErrorAndStopIterating</c>.</summary>
    private bool SetErrorAndStopIterating(string message)
    {
        SetError(message);
        return StopIterating();
    }

    /// <summary>ported from: src/compiler/sourcemap.ts:573-575 — <c>hasReportedError</c>.</summary>
    private bool HasReportedError() => _error is not null;

    /// <summary>ported from: src/compiler/sourcemap.ts:577-581 — <c>isSourceMappingSegmentEnd</c>.</summary>
    private bool IsSourceMappingSegmentEnd()
        => _pos == _mappings.Length
            || _mappings[_pos] == CharacterCodes.Comma
            || _mappings[_pos] == CharacterCodes.Semicolon;

    /// <summary>ported from: src/compiler/sourcemap.ts:583-616 — <c>base64VLQFormatDecode</c>.</summary>
    private int Base64VLQFormatDecode()
    {
        var moreDigits = true;
        var shiftCount = 0;
        var value = 0;

        for (; moreDigits; _pos++)
        {
            if (_pos >= _mappings.Length)
            {
                // PORT-NOTE: TS is `return setError("Error in decoding base64VLQFormatDecode, past the mapping string"), -1;`.
                SetError("Error in decoding base64VLQFormatDecode, past the mapping string");
                return -1;
            }

            // 6 digit number
            var currentByte = SourceMap.Base64FormatDecode(_mappings[_pos]);
            if (currentByte == -1)
            {
                // PORT-NOTE: TS is `return setError("Invalid character in VLQ"), -1;`.
                SetError("Invalid character in VLQ");
                return -1;
            }

            // If msb is set, we still have more bits to continue
            moreDigits = (currentByte & 32) != 0;

            // least significant 5 bits are the next msbs in the final value.
            value = value | ((currentByte & 31) << shiftCount);
            shiftCount += 5;
        }

        // Least significant bit if 1 represents negative and rest of the msb is actual absolute value
        if ((value & 1) == 0)
        {
            // + number
            value = value >> 1;
        }
        else
        {
            // - number
            value = value >> 1;
            value = -value;
        }

        return value;
    }

    /// <summary>ported from: src/compiler/sourcemap.ts:492-494 — <c>next</c>'s value.</summary>
    public Mapping Current => _current!;

    object IEnumerator.Current => Current;

    /// <summary>ported from: src/compiler/sourcemap.ts:538-540 — <c>[Symbol.iterator]() { return this; }</c>.</summary>
    public IEnumerator<Mapping> GetEnumerator() => this;

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>PORT-NOTE: TS's iterator has no reset; the BCL member it corresponds to is stated as the failure it is.</summary>
    void IEnumerator.Reset() => throw new NotSupportedException("TypeScript's MappingsDecoder (src/compiler/sourcemap.ts:461) is a single-pass iterator.");

    /// <summary>PORT-NOTE: TS's iterator holds no resource.</summary>
    public void Dispose()
    {
    }
}

internal static partial class SourceMap
{
    /// <summary>ported from: src/compiler/sourcemap.ts:461 — <c>decodeMappings</c>.</summary>
    /// <remarks>PORT-NOTE: TS builds the iterator object literal inline; the port states it as the decoder type, so this
    /// member is the reference's entry with the decoder's own state passed through.</remarks>
    internal static MappingsDecoder DecodeMappings(string mappings) => new(mappings);

    /// <summary>ported from: src/compiler/sourcemap.ts:618-627 — <c>sameMapping</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>&lt;T extends Mapping&gt;</c> is dropped: both operands are the same <c>Mapping</c> in
    /// every call, and the reference's first arm is the identity test <see cref="ReferenceEquals"/> states.</remarks>
    internal static bool SameMapping(Mapping left, Mapping right)
        => ReferenceEquals(left, right)
            || left.GeneratedLine == right.GeneratedLine
                && left.GeneratedCharacter == right.GeneratedCharacter
                && left.SourceIndex == right.SourceIndex
                && left.SourceLine == right.SourceLine
                && left.SourceCharacter == right.SourceCharacter
                && left.NameIndex == right.NameIndex;

    /// <summary>ported from: src/compiler/sourcemap.ts:629-634 — <c>isSourceMapping</c>.</summary>
    /// <inheritdoc cref="Mapping"/>
    internal static bool IsSourceMapping(Mapping mapping)
        => mapping.SourceIndex is not null
            && mapping.SourceLine is not null
            && mapping.SourceCharacter is not null;

    /// <summary>ported from: src/compiler/sourcemap.ts:636-643 — <c>base64FormatEncode</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>fail</c> is <c>never</c> and can sit in the last arm of the ternary; the port's
    /// <see cref="Debug.Fail{T}"/> is <see cref="DoesNotReturnAttribute"/> and does the same.</remarks>
    internal static char Base64FormatEncode(int value)
        => value is >= 0 and < 26 ? (char)(CharacterCodes.A + value)
            : value is >= 26 and < 52 ? (char)(CharacterCodes.a + value - 26)
            : value is >= 52 and < 62 ? (char)(CharacterCodes._0 + value - 52)
            : value == 62 ? (char)CharacterCodes.Plus
            : value == 63 ? (char)CharacterCodes.Slash
            : Debug.Fail<char>($"{value}: not a base64 value");

    /// <summary>ported from: src/compiler/sourcemap.ts:645-652 — <c>base64FormatDecode</c>.</summary>
    internal static int Base64FormatDecode(int ch)
        => ch >= CharacterCodes.A && ch <= CharacterCodes.Z ? ch - CharacterCodes.A
            : ch >= CharacterCodes.a && ch <= CharacterCodes.z ? ch - CharacterCodes.a + 26
            : ch >= CharacterCodes._0 && ch <= CharacterCodes._9 ? ch - CharacterCodes._0 + 52
            : ch == CharacterCodes.Plus ? 62
            : ch == CharacterCodes.Slash ? 63
            : -1;
}
