namespace TypeScript.Compiler;

// ported from: src/compiler/utilities.ts:6236-6470 — the text-writer layer (`indentStrings` through
// `getTrailingSemicolonDeferringWriter`), the first piece of Layer 7's writer stack.
/// <summary>
/// The port of <c>createTextWriter</c> (utilities.ts:6298-6400) — the reference's object literal, stated as a class.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS returns an object literal typed <c>EmitTextWriter</c> whose members close over six mutable locals
/// (<c>output</c>, <c>indent</c>, <c>lineStart</c>, <c>lineCount</c>, <c>linePos</c>, <c>hasTrailingComment</c>); the
/// port's face is the generated <see cref="IEmitTextWriter"/> (which extends <see cref="ISymbolWriter"/>), so the
/// closure becomes this class and the locals become fields. The reference's <c>writeKeyword</c>/<c>writeOperator</c>/
/// <c>writeParameter</c>/<c>writeProperty</c>/<c>writePunctuation</c>/<c>writeSpace</c>/<c>writeStringLiteral</c>/
/// <c>writeTrailingSemicolon</c> aliases all point at <c>write</c>, and <c>writeSymbol</c> at
/// <c>(s, _) =&gt; write(s)</c>, so each of those members calls <see cref="Write"/>.
/// <para>
/// PORT-NOTE: TS leaves <c>nonEscapingWrite</c> undefined on this literal (it is an optional member, supplied only by
/// a snippet-escaping writer the emitter builds elsewhere). The generated port interface declares it, so — as
/// <see cref="SingleLineStringWriter"/> already does — it writes the text plainly, which is exactly what the
/// printer's own <c>nonEscapingWrite</c> helper falls back to when the writer lacks it (emitter.ts:4908-4914).
/// </para>
/// </remarks>
internal sealed class EmitTextWriter : IEmitTextWriter
{
    private readonly string _newLine;
    private string _output = "";
    private int _indent;
    private bool _lineStart = true;
    private int _lineCount;
    private int _linePos;
    private bool _hasTrailingComment;

    public EmitTextWriter(string newLine) => _newLine = newLine;

    /// <summary>ported from: src/compiler/utilities.ts:6311-6323 — <c>updateLineCountAndPosFor</c>.</summary>
    private void UpdateLineCountAndPosFor(string s)
    {
        var lineStartsOfS = ScannerUtilities.ComputeLineStarts(s);
        if (lineStartsOfS.Count > 1)
        {
            _lineCount = _lineCount + lineStartsOfS.Count - 1;
            _linePos = _output.Length - s.Length + Core.Last(lineStartsOfS);
            _lineStart = (_linePos - _output.Length) == 0;
        }
        else
        {
            _lineStart = false;
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:6325-6335 — <c>writeText</c>.</summary>
    private void WriteText(string? s)
    {
        if (!string.IsNullOrEmpty(s))
        {
            if (_lineStart)
            {
                s = IndentString(_indent) + s;
                _lineStart = false;
            }

            _output += s;
            UpdateLineCountAndPosFor(s);
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:6337-6340 — <c>write</c>.</summary>
    public void Write(string? s)
    {
        if (!string.IsNullOrEmpty(s)) _hasTrailingComment = false;
        WriteText(s);
    }

    /// <summary>ported from: src/compiler/utilities.ts:6342-6345 — <c>writeComment</c>.</summary>
    public void WriteComment(string? s)
    {
        if (!string.IsNullOrEmpty(s)) _hasTrailingComment = true;
        WriteText(s);
    }

    /// <summary>ported from: src/compiler/utilities.ts:6347-6355 — <c>reset</c>.</summary>
    public void Clear()
    {
        _output = "";
        _indent = 0;
        _lineStart = true;
        _lineCount = 0;
        _linePos = 0;
        _hasTrailingComment = false;
    }

    /// <summary>ported from: src/compiler/utilities.ts:6357-6363 — <c>rawWrite</c>.</summary>
    public void RawWrite(string? s)
    {
        if (s is not null)
        {
            _output += s;
            UpdateLineCountAndPosFor(s);
            _hasTrailingComment = false;
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:6365-6369 — <c>writeLiteral</c>.</summary>
    public void WriteLiteral(string? s)
    {
        if (!string.IsNullOrEmpty(s))
        {
            Write(s);
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:6371-6379 — <c>writeLine</c>.</summary>
    public void WriteLine(bool? force)
    {
        if (!_lineStart || force == true)
        {
            _output += _newLine;
            _lineCount++;
            _linePos = _output.Length;
            _lineStart = true;
            _hasTrailingComment = false;
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:6381-6383 — <c>increaseIndent</c>.</summary>
    public void IncreaseIndent() => _indent++;

    /// <summary>ported from: src/compiler/utilities.ts:6384-6386 — <c>decreaseIndent</c>.</summary>
    public void DecreaseIndent() => _indent--;

    /// <summary>ported from: src/compiler/utilities.ts:6387 — <c>getIndent</c>.</summary>
    public int GetIndent() => _indent;

    /// <summary>ported from: src/compiler/utilities.ts:6388 — <c>getTextPos</c>.</summary>
    public int GetTextPos() => _output.Length;

    /// <summary>ported from: src/compiler/utilities.ts:6389 — <c>getLine</c>.</summary>
    public int GetLine() => _lineCount;

    /// <summary>ported from: src/compiler/utilities.ts:6390 — <c>getColumn</c>.</summary>
    public int GetColumn() => _lineStart ? _indent * IndentSize() : _output.Length - _linePos;

    /// <summary>ported from: src/compiler/utilities.ts:6391 — <c>getText</c>.</summary>
    public string GetText() => _output;

    /// <summary>ported from: src/compiler/utilities.ts:6392 — <c>isAtStartOfLine</c>.</summary>
    public bool IsAtStartOfLine() => _lineStart;

    /// <summary>ported from: src/compiler/utilities.ts:6393 — <c>hasTrailingComment</c>.</summary>
    public bool HasTrailingComment() => _hasTrailingComment;

    /// <summary>ported from: src/compiler/utilities.ts:6394 — <c>hasTrailingWhitespace</c>.</summary>
    public bool HasTrailingWhitespace() => _output.Length != 0 && ScannerUtilities.IsWhiteSpaceLike(_output[^1]);

    // The writeXxx aliases of the reference's literal (utilities.ts:6396-6407); each is `write`, and writeSymbol is
    // `(s, _) => write(s)`.
    public void WriteKeyword(string? text) => Write(text);

    public void WriteOperator(string? text) => Write(text);

    public void WriteParameter(string? text) => Write(text);

    public void WriteProperty(string? text) => Write(text);

    public void WritePunctuation(string? text) => Write(text);

    public void WriteSpace(string? text) => Write(text);

    public void WriteStringLiteral(string? text) => Write(text);

    public void WriteSymbol(string? text, ISymbol? symbol) => Write(text);

    public void WriteTrailingSemicolon(string? text) => Write(text);

    public void NonEscapingWrite(string? text) => Write(text);

    // ---------------------------------------------------------------------------------------------
    // The indentation cache (utilities.ts:6281-6295)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:6281 — <c>indentStrings</c>.</summary>
    private static readonly List<string> IndentStrings = ["", "    "];

    /// <summary>ported from: src/compiler/utilities.ts:6283-6291 — <c>getIndentString</c>.</summary>
    private static string IndentString(int level)
    {
        // prepopulate cache
        var singleLevel = IndentStrings[1];
        for (var current = IndentStrings.Count; current <= level; current++)
        {
            IndentStrings.Add(IndentStrings[current - 1] + singleLevel);
        }

        return IndentStrings[level];
    }

    /// <summary>ported from: src/compiler/utilities.ts:6293-6295 — <c>getIndentSize</c>.</summary>
    private static int IndentSize() => IndentStrings[1].Length;
}

/// <summary>
/// The port of <c>getTrailingSemicolonDeferringWriter</c> (utilities.ts:6412-6470) — a writer that holds a pending
/// trailing semicolon back until anything else is written.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS builds this as an object literal spreading the wrapped writer (`{...writer, writeTrailingSemicolon()
/// {...}, ...}`); C# has no spread, so the class forwards every member to the wrapped writer explicitly — the
/// reference's own override list is exactly what the forwarding order encodes (each override commits the pending
/// semicolon first, and the members it does *not* override — <c>write</c>/<c>rawWrite</c>/<c>getText</c> and the rest
/// of the read-only family — pass straight through).
/// </remarks>
internal sealed class TrailingSemicolonDeferringWriter : IEmitTextWriter
{
    private readonly IEmitTextWriter _writer;
    private bool _pendingTrailingSemicolon;

    public TrailingSemicolonDeferringWriter(IEmitTextWriter writer) => _writer = writer;

    /// <summary>ported from: src/compiler/utilities.ts:6415-6420 — <c>commitPendingTrailingSemicolon</c>.</summary>
    private void CommitPendingTrailingSemicolon()
    {
        if (_pendingTrailingSemicolon)
        {
            _writer.WriteTrailingSemicolon(";");
            _pendingTrailingSemicolon = false;
        }
    }

    public void WriteTrailingSemicolon(string? text) => _pendingTrailingSemicolon = true;

    public void WriteLiteral(string? s)
    {
        CommitPendingTrailingSemicolon();
        _writer.WriteLiteral(s);
    }

    public void WriteStringLiteral(string? s)
    {
        CommitPendingTrailingSemicolon();
        _writer.WriteStringLiteral(s);
    }

    public void WriteSymbol(string? s, ISymbol? sym)
    {
        CommitPendingTrailingSemicolon();
        _writer.WriteSymbol(s, sym);
    }

    public void WritePunctuation(string? s)
    {
        CommitPendingTrailingSemicolon();
        _writer.WritePunctuation(s);
    }

    public void WriteKeyword(string? s)
    {
        CommitPendingTrailingSemicolon();
        _writer.WriteKeyword(s);
    }

    public void WriteOperator(string? s)
    {
        CommitPendingTrailingSemicolon();
        _writer.WriteOperator(s);
    }

    public void WriteParameter(string? s)
    {
        CommitPendingTrailingSemicolon();
        _writer.WriteParameter(s);
    }

    public void WriteSpace(string? s)
    {
        CommitPendingTrailingSemicolon();
        _writer.WriteSpace(s);
    }

    public void WriteProperty(string? s)
    {
        CommitPendingTrailingSemicolon();
        _writer.WriteProperty(s);
    }

    public void WriteComment(string? s)
    {
        CommitPendingTrailingSemicolon();
        _writer.WriteComment(s);
    }

    public void WriteLine(bool? force)
    {
        CommitPendingTrailingSemicolon();
        _writer.WriteLine(force);
    }

    public void IncreaseIndent()
    {
        CommitPendingTrailingSemicolon();
        _writer.IncreaseIndent();
    }

    public void DecreaseIndent()
    {
        CommitPendingTrailingSemicolon();
        _writer.DecreaseIndent();
    }

    // The members the reference's spread leaves to the wrapped writer.
    public void Write(string? s) => _writer.Write(s);

    public void RawWrite(string? s) => _writer.RawWrite(s);

    public void NonEscapingWrite(string? text) => _writer.NonEscapingWrite(text);

    public string GetText() => _writer.GetText();

    public int GetTextPos() => _writer.GetTextPos();

    public int GetLine() => _writer.GetLine();

    public int GetColumn() => _writer.GetColumn();

    public int GetIndent() => _writer.GetIndent();

    public bool IsAtStartOfLine() => _writer.IsAtStartOfLine();

    public bool HasTrailingComment() => _writer.HasTrailingComment();

    public bool HasTrailingWhitespace() => _writer.HasTrailingWhitespace();

    public void Clear() => _writer.Clear();
}

internal static partial class Utilities
{
    /// <summary>ported from: src/compiler/utilities.ts:6298-6408 — <c>createTextWriter</c>.</summary>
    public static IEmitTextWriter CreateTextWriter(string newLine) => new EmitTextWriter(newLine);

    /// <summary>ported from: src/compiler/utilities.ts:6412-6470 — <c>getTrailingSemicolonDeferringWriter</c>.</summary>
    public static IEmitTextWriter GetTrailingSemicolonDeferringWriter(IEmitTextWriter writer) => new TrailingSemicolonDeferringWriter(writer);
}
