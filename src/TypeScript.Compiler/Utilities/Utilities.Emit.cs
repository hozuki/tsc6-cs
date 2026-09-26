using System.Text.RegularExpressions;

namespace TypeScript.Compiler;

// ported from: src/compiler/utilities.ts:6236-6470 — the text-writer layer (`indentStrings` through
//              `getTrailingSemicolonDeferringWriter`), the first piece of Layer 7's writer stack.
//
// This file also holds the comment-emission helpers the printer's comment pipeline calls (the M7-0 A5 slice's
// dependencies, M6 integration ruling): utilities.ts:1182-1206 and 2781-2786 (the triple-slash comment tests and their
// regular expressions), 6754-6756 (`getLineOfLocalPositionFromLineMap`), 6938-7010
// (`emitNewLineBeforeLeadingComments*`, `emitComments`), 7012-7080 (`emitDetachedComments`), 7082-7173
// (`writeCommentRange`, `calculateIndent`), 10809-10875 (`getContainingNodeArray`), plus the three
// utilitiesPublic.ts predicates `getContainingNodeArray` reads (`isTypeElement`, `isJsxChild`, `isJSDocTag`). See the
// `ported from:` header on each member.
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
    /// <remarks>PORT-NOTE: access widened from <c>private</c> to <c>internal</c> so <c>Utilities.GetIndentString</c> can
    /// be the module-level face of the same cache (utilities.ts:6283/6293), which <c>writeCommentRange</c>
    /// (utilities.ts:7082) needs.</remarks>
    internal static readonly List<string> IndentStrings = ["", "    "];

    /// <summary>ported from: src/compiler/utilities.ts:6283-6291 — <c>getIndentString</c>.</summary>
    internal static string IndentString(int level)
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
    internal static int IndentSize() => IndentStrings[1].Length;
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

// ported from: src/compiler/utilities.ts:1182-1206 (the triple-slash comment tests), 2781-2786 (their regular
//              expressions), 6754-6756 (getLineOfLocalPositionFromLineMap), 6938-7010 (emitNewLineBeforeLeadingComments,
//              emitComments), 7012-7080 (emitDetachedComments), 7082-7173 (writeCommentRange, calculateIndent),
//              10809-10875 (getContainingNodeArray) — the comment-emission helpers the printer's comment pipeline calls,
//              plus the containing-array reader its list emission calls.
internal static partial class Utilities
{
    // ---------------------------------------------------------------------------------------------
    // The indentation cache's module-level face (utilities.ts:6281-6295)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:6283-6291 — <c>getIndentString</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's module-level <c>indentStrings</c> cache and its <c>getIndentString</c> are the
    /// writer layer's <c>EmitTextWriter.IndentStrings</c>/<c>IndentString</c> (Utilities/Utilities.Emit.cs:187/190);
    /// these two members are the module-level face of the same cache, because <c>writeCommentRange</c>
    /// (utilities.ts:7082) is a utilities.ts function that measures indentation.
    /// </remarks>
    public static string GetIndentString(int level) => EmitTextWriter.IndentString(level);

    /// <summary>ported from: src/compiler/utilities.ts:6293-6295 — <c>getIndentSize</c>.</summary>
    /// <remarks>PORT-NOTE: see <see cref="GetIndentString"/>.</remarks>
    public static int GetIndentSize() => EmitTextWriter.IndentSize();

    // ---------------------------------------------------------------------------------------------
    // The triple-slash comment tests (utilities.ts:1182-1206)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:1182-1201 — <c>isRecognizedTripleSlashComment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>text.charCodeAt(commentPos + 1) === CharacterCodes.slash</c> is
    /// <see cref="ScannerUtilities.CharCodeAt"/>, the port's helper for a read past the end (it answers
    /// <c>CharacterCodes.EOF</c>, which is what TS's <c>NaN</c> comparison effectively does), and
    /// <c>text.substring(commentPos, commentEnd)</c> is the range operator with the same half-open bounds, both
    /// bounds already tested by the guard.
    /// </remarks>
    public static bool IsRecognizedTripleSlashComment(string text, int commentPos, int commentEnd)
    {
        // Verify this is /// comment, but do the regexp match only when we first can find /// in the comment text
        // so that we don't end up computing comment string and doing match for all // comments
        if (
            ScannerUtilities.CharCodeAt(text, commentPos + 1) == CharacterCodes.Slash &&
            commentPos + 2 < commentEnd &&
            ScannerUtilities.CharCodeAt(text, commentPos + 2) == CharacterCodes.Slash
        )
        {
            var textSubStr = text[commentPos..commentEnd];
            return FullTripleSlashReferencePathRegEx().IsMatch(textSubStr)
                || FullTripleSlashAMDReferencePathRegEx().IsMatch(textSubStr)
                || FullTripleSlashAMDModuleRegEx().IsMatch(textSubStr)
                || FullTripleSlashReferenceTypeReferenceDirectiveRegEx().IsMatch(textSubStr)
                || FullTripleSlashLibReferenceRegEx().IsMatch(textSubStr)
                || DefaultLibReferenceRegEx().IsMatch(textSubStr);
        }

        return false;
    }

    // ported from: src/compiler/utilities.ts:2781-2786 — the six regular expressions the test above uses.
    /// <summary>ported from: src/compiler/utilities.ts:2781 — <c>fullTripleSlashReferencePathRegEx</c>.</summary>
    [GeneratedRegex("^///\\s*<reference\\s+path\\s*=\\s*(?:'[^']*'|\"[^\"]*\").*?/>")]
    private static partial Regex FullTripleSlashReferencePathRegEx();

    /// <summary>ported from: src/compiler/utilities.ts:2782 —
    /// <c>fullTripleSlashReferenceTypeReferenceDirectiveRegEx</c>.</summary>
    [GeneratedRegex("^///\\s*<reference\\s+types\\s*=\\s*(?:'[^']*'|\"[^\"]*\").*?/>")]
    private static partial Regex FullTripleSlashReferenceTypeReferenceDirectiveRegEx();

    /// <summary>ported from: src/compiler/utilities.ts:2783 — <c>fullTripleSlashLibReferenceRegEx</c>.</summary>
    [GeneratedRegex("^///\\s*<reference\\s+lib\\s*=\\s*(?:'[^']*'|\"[^\"]*\").*?/>")]
    private static partial Regex FullTripleSlashLibReferenceRegEx();

    /// <summary>ported from: src/compiler/utilities.ts:2784 — <c>fullTripleSlashAMDReferencePathRegEx</c>.</summary>
    [GeneratedRegex("^///\\s*<amd-dependency\\s+path\\s*=\\s*(?:'[^']*'|\"[^\"]*\").*?/>")]
    private static partial Regex FullTripleSlashAMDReferencePathRegEx();

    /// <summary>ported from: src/compiler/utilities.ts:2785 — <c>fullTripleSlashAMDModuleRegEx</c>.</summary>
    [GeneratedRegex("^///\\s*<amd-module\\s+(?:\\S.*?)??/>")]
    private static partial Regex FullTripleSlashAMDModuleRegEx();

    /// <summary>ported from: src/compiler/utilities.ts:2786 — <c>defaultLibReferenceRegEx</c>.</summary>
    [GeneratedRegex("^///\\s*<reference\\s+no-default-lib\\s*=\\s*(?:'[^']*'|\"[^\"]*\")\\s*/>")]
    private static partial Regex DefaultLibReferenceRegEx();

    /// <summary>ported from: src/compiler/utilities.ts:1203-1206 — <c>isPinnedComment</c>.</summary>
    public static bool IsPinnedComment(string text, int start)
        => ScannerUtilities.CharCodeAt(text, start + 1) == CharacterCodes.Asterisk
            && ScannerUtilities.CharCodeAt(text, start + 2) == CharacterCodes.Exclamation;

    // ---------------------------------------------------------------------------------------------
    // The three predicates getContainingNodeArray reads (utilitiesPublic.ts)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1782-1792 — <c>isTypeElement</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>const kind = node.kind;</c> then a chain of <c>kind === SyntaxKind.X</c> is the
    /// port's kind pattern; the answers and their order are the reference's.</remarks>
    public static bool IsTypeElement(Node node) => node.Kind is SyntaxKind.ConstructSignature
        or SyntaxKind.CallSignature
        or SyntaxKind.PropertySignature
        or SyntaxKind.MethodSignature
        or SyntaxKind.IndexSignature
        or SyntaxKind.GetAccessor
        or SyntaxKind.SetAccessor
        or SyntaxKind.NotEmittedTypeElement;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2468-2475 — <c>isJsxChild</c>.</summary>
    public static bool IsJsxChild(Node node) => node.Kind is SyntaxKind.JsxElement
        or SyntaxKind.JsxExpression
        or SyntaxKind.JsxSelfClosingElement
        or SyntaxKind.JsxText
        or SyntaxKind.JsxFragment;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2534-2536 — <c>isJSDocTag</c>.</summary>
    public static bool IsJSDocTag(Node node) => node.Kind >= SyntaxKind.FirstJSDocTagNode && node.Kind <= SyntaxKind.LastJSDocTagNode;

    // ---------------------------------------------------------------------------------------------
    // Position and comment emission (utilities.ts:6754-7010)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:6754-6756 — <c>getLineOfLocalPositionFromLineMap</c>.</summary>
    private static int GetLineOfLocalPositionFromLineMap(IList<int> lineMap, int pos)
        => ScannerUtilities.ComputeLineOfPosition(lineMap, pos);

    /// <summary>ported from: src/compiler/utilities.ts:6938-6940 — <c>emitNewLineBeforeLeadingComments</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>readonly CommentRange[] | undefined</c> is the port's
    /// <c>IList&lt;CommentRange&gt;?</c>, the type the scanner's comment-range readers answer.</remarks>
    private static void EmitNewLineBeforeLeadingComments(IList<int> lineMap, IEmitTextWriter writer, ITextRange node, IList<CommentRange>? leadingComments)
        => EmitNewLineBeforeLeadingCommentsOfPosition(lineMap, writer, node.Pos, leadingComments);

    /// <summary>ported from: src/compiler/utilities.ts:6942-6951 — <c>emitNewLineBeforeLeadingCommentsOfPosition</c>.
    /// </summary>
    private static void EmitNewLineBeforeLeadingCommentsOfPosition(IList<int> lineMap, IEmitTextWriter writer, int pos, IList<CommentRange>? leadingComments)
    {
        // If the leading comments start on different line than the start of node, write new line
        if (
            leadingComments is not null && leadingComments.Count != 0 && pos != leadingComments[0].Pos &&
            GetLineOfLocalPositionFromLineMap(lineMap, pos) != GetLineOfLocalPositionFromLineMap(lineMap, leadingComments[0].Pos)
        )
        {
            writer.WriteLine(null);
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:6953-6962 — <c>emitNewLineBeforeLeadingCommentOfPosition</c>.
    /// </summary>
    public static void EmitNewLineBeforeLeadingCommentOfPosition(IList<int> lineMap, IEmitTextWriter writer, int pos, int commentPos)
    {
        // If the leading comments start on different line than the start of node, write new line
        if (
            pos != commentPos &&
            GetLineOfLocalPositionFromLineMap(lineMap, pos) != GetLineOfLocalPositionFromLineMap(lineMap, commentPos)
        )
        {
            writer.WriteLine(null);
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:6963-7000 — <c>emitComments</c>.</summary>
    /// <remarks>PORT-NOTE: <c>comments.length &gt; 0</c> stays an explicit test (it is the reference's, and the port's
    /// <c>IList</c> has no implicit truthiness); <c>comment.hasTrailingNewLine</c> and the two separator flags are
    /// non-nullable <c>bool</c> in the port, so the reference's truthiness tests are the values themselves.</remarks>
    private static void EmitComments(
        string text,
        IList<int> lineMap,
        IEmitTextWriter writer,
        IList<CommentRange>? comments,
        bool leadingSeparator,
        bool trailingSeparator,
        string newLine,
        Action<string, IList<int>, IEmitTextWriter, int, int, string> writeComment)
    {
        if (comments is not null && comments.Count > 0)
        {
            if (leadingSeparator)
            {
                writer.WriteSpace(" ");
            }

            var emitInterveningSeparator = false;
            foreach (var comment in comments)
            {
                if (emitInterveningSeparator)
                {
                    writer.WriteSpace(" ");
                    emitInterveningSeparator = false;
                }

                writeComment(text, lineMap, writer, comment.Pos, comment.End, newLine);
                if (comment.HasTrailingNewLine)
                {
                    writer.WriteLine(null);
                }
                else
                {
                    emitInterveningSeparator = true;
                }
            }

            if (emitInterveningSeparator && trailingSeparator)
            {
                writer.WriteSpace(" ");
            }
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:7002-7080 — <c>emitDetachedComments</c>.</summary>
    /// <remarks>
    /// <para>Detached comment is a comment at the top of file or function body that is separated from the next statement
    /// by space.</para>
    /// <para>PORT-NOTE: TS returns <c>DetachedCommentInfo | undefined</c> and the port's
    /// <see cref="DetachedCommentInfo"/> (Emit/Printer.State.cs:43) is a value type, so "no detached comment" is
    /// <c>DetachedCommentInfo?</c>; <c>filter(getLeadingCommentRanges(...), isPinnedCommentLocal)</c> is
    /// <c>Core.Filter</c> over the scanner's list of <see cref="CommentRange"/>, and <c>last</c> is
    /// <c>Core.Last</c>.</para>
    /// </remarks>
    public static DetachedCommentInfo? EmitDetachedComments(
        string text,
        IList<int> lineMap,
        IEmitTextWriter writer,
        Action<string, IList<int>, IEmitTextWriter, int, int, string> writeComment,
        ITextRange node,
        string newLine,
        bool removeComments)
    {
        IList<CommentRange>? leadingComments;
        DetachedCommentInfo? currentDetachedCommentInfo = null;
        if (removeComments)
        {
            // removeComments is true, only reserve pinned comment at the top of file
            // For example:
            //      /*! Pinned Comment */
            //
            //      var x = 10;
            if (node.Pos == 0)
            {
                leadingComments = Core.Filter(ScannerUtilities.GetLeadingCommentRanges(text, node.Pos), comment => IsPinnedComment(text, comment.Pos));
            }
            else
            {
                leadingComments = null;
            }
        }
        else
        {
            // removeComments is false, just get detached as normal and bypass the process to filter comment
            leadingComments = ScannerUtilities.GetLeadingCommentRanges(text, node.Pos);
        }

        if (leadingComments is not null)
        {
            var detachedComments = new List<CommentRange>();
            CommentRange? lastComment = null;

            foreach (var comment in leadingComments)
            {
                if (lastComment is not null)
                {
                    var lastCommentLine = GetLineOfLocalPositionFromLineMap(lineMap, lastComment.End);
                    var commentLine = GetLineOfLocalPositionFromLineMap(lineMap, comment.Pos);

                    if (commentLine >= lastCommentLine + 2)
                    {
                        // There was a blank line between the last comment and this comment.  This
                        // comment is not part of the copyright comments.  Return what we have so
                        // far.
                        break;
                    }
                }

                detachedComments.Add(comment);
                lastComment = comment;
            }

            if (detachedComments.Count != 0)
            {
                // All comments look like they could have been part of the copyright header.  Make
                // sure there is at least one blank line between it and the node.  If not, it's not
                // a copyright header.
                var lastCommentLine = GetLineOfLocalPositionFromLineMap(lineMap, Core.Last(detachedComments).End);
                var nodeLine = GetLineOfLocalPositionFromLineMap(lineMap, ScannerUtilities.SkipTrivia(text, node.Pos));
                if (nodeLine >= lastCommentLine + 2)
                {
                    // Valid detachedComments
                    EmitNewLineBeforeLeadingComments(lineMap, writer, node, leadingComments);
                    EmitComments(text, lineMap, writer, detachedComments, /*leadingSeparator*/ false, /*trailingSeparator*/ true, newLine, writeComment);
                    currentDetachedCommentInfo = new DetachedCommentInfo(node.Pos, Core.Last(detachedComments).End);
                }
            }
        }

        return currentDetachedCommentInfo;
    }

    // ---------------------------------------------------------------------------------------------
    // Writing a comment's text (utilities.ts:7082-7173)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:7082-7145 — <c>writeCommentRange</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>firstCommentLineIndent: number | undefined</c> is <c>int?</c>;
    /// <c>writer.rawWrite</c>/<c>writer.writeComment</c>/<c>writer.writeLine</c> are
    /// <see cref="IEmitTextWriter.RawWrite"/>/<see cref="IEmitTextWriter.WriteComment"/>/
    /// <see cref="IEmitTextWriter.WriteLine"/> (the last one keeps TS's optional <c>force</c>, hence the <c>null</c>);
    /// and <c>while (numberOfSingleSpacesToEmit)</c> is the reference's truthiness test on a count, written out.
    /// </remarks>
    public static void WriteCommentRange(string text, IList<int> lineMap, IEmitTextWriter writer, int commentPos, int commentEnd, string newLine)
    {
        if (ScannerUtilities.CharCodeAt(text, commentPos + 1) == CharacterCodes.Asterisk)
        {
            var firstCommentLineAndCharacter = ScannerUtilities.ComputeLineAndCharacterOfPosition(lineMap, commentPos);
            var lineCount = lineMap.Count;
            int? firstCommentLineIndent = null;
            for (int pos = commentPos, currentLine = firstCommentLineAndCharacter.Line; pos < commentEnd; currentLine++)
            {
                var nextLineStart = (currentLine + 1) == lineCount
                    ? text.Length + 1
                    : lineMap[currentLine + 1];

                if (pos != commentPos)
                {
                    // If we are not emitting first line, we need to write the spaces to adjust the alignment
                    if (firstCommentLineIndent is null)
                    {
                        firstCommentLineIndent = CalculateIndent(text, lineMap[firstCommentLineAndCharacter.Line], commentPos);
                    }

                    // These are number of spaces writer is going to write at current indent
                    var currentWriterIndentSpacing = writer.GetIndent() * GetIndentSize();

                    // Number of spaces we want to be writing
                    // eg: Assume writer indent
                    // module m {
                    //         /* starts at character 9 this is line 1
                    //    * starts at character pos 4 line                        --1  = 8 - 8 + 3
                    //   More left indented comment */                            --2  = 8 - 8 + 2
                    //     class c { }
                    // }
                    // module m {
                    //     /* this is line 1 -- Assume current writer indent 8
                    //      * line                                                --3 = 8 - 4 + 5
                    //            More right indented comment */                  --4 = 8 - 4 + 11
                    //     class c { }
                    // }
                    var spacesToEmit = currentWriterIndentSpacing - firstCommentLineIndent.Value + CalculateIndent(text, pos, nextLineStart);
                    if (spacesToEmit > 0)
                    {
                        var numberOfSingleSpacesToEmit = spacesToEmit % GetIndentSize();
                        var indentSizeSpaceString = GetIndentString((spacesToEmit - numberOfSingleSpacesToEmit) / GetIndentSize());

                        // Write indent size string ( in eg 1: = "", 2: "" , 3: string with 8 spaces 4: string with 12 spaces
                        writer.RawWrite(indentSizeSpaceString);

                        // Emit the single spaces (in eg: 1: 3 spaces, 2: 2 spaces, 3: 1 space, 4: 3 spaces)
                        while (numberOfSingleSpacesToEmit != 0)
                        {
                            writer.RawWrite(" ");
                            numberOfSingleSpacesToEmit--;
                        }
                    }
                    else
                    {
                        // No spaces to emit write empty string
                        writer.RawWrite("");
                    }
                }

                // Write the comment line text
                WriteTrimmedCurrentLine(text, commentEnd, writer, newLine, pos, nextLineStart);

                pos = nextLineStart;
            }
        }
        else
        {
            // Single line comment of style //....
            writer.WriteComment(text[commentPos..commentEnd]);
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:7147-7161 — <c>writeTrimmedCurrentLine</c>.</summary>
    /// <remarks>PORT-NOTE: <c>text.substring(pos, end).trim()</c> is <c>text[pos..end].Trim()</c>; the range operator
    /// does not swap reversed bounds the way <c>substring</c> does, and there are none here — <c>end</c> is
    /// <c>Math.min(commentEnd, nextLineStart - 1)</c> with <c>pos &lt; nextLineStart</c>, both inside the text.</remarks>
    private static void WriteTrimmedCurrentLine(string text, int commentEnd, IEmitTextWriter writer, string newLine, int pos, int nextLineStart)
    {
        var end = Math.Min(commentEnd, nextLineStart - 1);
        var currentLineText = text[pos..end].Trim();
        if (currentLineText.Length != 0)
        {
            // trimmed forward and ending spaces text
            writer.WriteComment(currentLineText);
            if (end != commentEnd)
            {
                writer.WriteLine(null);
            }
        }
        else
        {
            // Empty string - make sure we write empty line
            writer.RawWrite(newLine);
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:7163-7173 — <c>calculateIndent</c>.</summary>
    /// <remarks>PORT-NOTE: <c>isWhiteSpaceSingleLine(text.charCodeAt(pos))</c> is
    /// <see cref="ScannerUtilities.IsWhiteSpaceSingleLine"/> over <see cref="ScannerUtilities.CharCodeAt"/>.</remarks>
    private static int CalculateIndent(string text, int pos, int end)
    {
        var currentLineIndent = 0;
        for (; pos < end && ScannerUtilities.IsWhiteSpaceSingleLine(ScannerUtilities.CharCodeAt(text, pos)); pos++)
        {
            if (ScannerUtilities.CharCodeAt(text, pos) == CharacterCodes.Tab)
            {
                // Tabs = TabSize = indent size and go to next tabStop
                currentLineIndent += GetIndentSize() - currentLineIndent % GetIndentSize();
            }
            else
            {
                // Single space
                currentLineIndent++;
            }
        }

        return currentLineIndent;
    }

    // ---------------------------------------------------------------------------------------------
    // The containing array of a node (utilities.ts:10809-10875)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:10809-10875 — <c>getContainingNodeArray</c>.</summary>
    /// <remarks>
    /// <para>PORT-NOTE: TS answers <c>NodeArray&lt;Node&gt;</c>, which its covariant <c>NodeArray</c> makes assignable
    /// from every element type. The port's <see cref="INodeArray{T}"/> is the same covariant view — the type the
    /// generated <c>ForEachChildTable</c> already takes for exactly this reason (Parser/ForEachChildTable.cs:31-40) —
    /// and every member read below is a <c>NodeArray&lt;T&gt;</c>, so nothing is copied or flattened.</para>
    /// <para>PORT-NOTE: TS's <c>parent</c> is the union its member declares, so <c>parent.members</c>,
    /// <c>parent.properties</c> and the other slots type-check on it; the port's <c>Node.Parent</c> is
    /// <see cref="Node"/>, so each union member is named — the treatment <c>Utilities.ModifiersOf</c>
    /// (Utilities/Utilities.Binder.cs:994) and <c>ForEachChildTable</c>'s visitors give the same problem.</para>
    /// </remarks>
    public static INodeArray<INode>? GetContainingNodeArray(Node node)
    {
        var parent = node.Parent;
        if (parent is null) return null;
        switch (node.Kind)
        {
            case SyntaxKind.TypeParameter:
                if (parent.Kind == SyntaxKind.InferType) return null;
                if (parent is IClassLikeDeclarationBase classLike) return classLike.TypeParameters;
                if (parent is ISignatureDeclarationBase signature)
                {
                    return signature.TypeParameters;
                }

                if (parent is InterfaceDeclaration interfaceDeclaration) return interfaceDeclaration.TypeParameters;
                if (parent is TypeAliasDeclaration typeAliasDeclaration) return typeAliasDeclaration.TypeParameters;
                return ((JSDocTemplateTag)parent).TypeParameters;

            case SyntaxKind.Parameter:
                return ((ISignatureDeclarationBase)parent).Parameters;

            case SyntaxKind.TemplateLiteralTypeSpan:
                return parent is TemplateLiteralTypeNode templateLiteralType ? templateLiteralType.TemplateSpans : null;

            case SyntaxKind.TemplateSpan:
                return parent is TemplateExpression templateExpression ? templateExpression.TemplateSpans : null;

            case SyntaxKind.Decorator:
                if (!CanHaveDecorators(parent)) return null;
                if (parent is ParameterDeclaration parameter) return parameter.Modifiers;
                if (parent is PropertyDeclaration property) return property.Modifiers;
                if (parent is MethodDeclaration method) return method.Modifiers;
                if (parent is GetAccessorDeclaration getAccessor) return getAccessor.Modifiers;
                if (parent is SetAccessorDeclaration setAccessor) return setAccessor.Modifiers;
                if (parent is ClassExpression classExpression) return classExpression.Modifiers;
                return ((ClassDeclaration)parent).Modifiers;

            case SyntaxKind.HeritageClause:
                if (parent is IClassLikeDeclarationBase heritageClassLike) return heritageClassLike.HeritageClauses;
                return ((InterfaceDeclaration)parent).HeritageClauses;
        }

        if (IsJSDocTag(node))
        {
            return NodeTests.IsJSDocTypeLiteral(parent) ? null : ((JSDoc)parent).Tags;
        }

        switch (parent.Kind)
        {
            case SyntaxKind.TypeLiteral:
            case SyntaxKind.InterfaceDeclaration:
                if (!IsTypeElement(node)) return null;
                return parent is TypeLiteralNode typeLiteral ? typeLiteral.Members : ((InterfaceDeclaration)parent).Members;

            case SyntaxKind.UnionType:
            case SyntaxKind.IntersectionType:
                return parent is UnionTypeNode unionType ? unionType.Types : ((IntersectionTypeNode)parent).Types;

            case SyntaxKind.TupleType:
                return ((TupleTypeNode)parent).Elements;

            case SyntaxKind.ArrayLiteralExpression:
                return ((ArrayLiteralExpression)parent).Elements;

            case SyntaxKind.CommaListExpression:
                return ((CommaListExpression)parent).Elements;

            case SyntaxKind.NamedImports:
                return ((NamedImports)parent).Elements;

            case SyntaxKind.NamedExports:
                return ((NamedExports)parent).Elements;

            case SyntaxKind.ObjectLiteralExpression:
            case SyntaxKind.JsxAttributes:
                return parent is ObjectLiteralExpression objectLiteral ? objectLiteral.Properties : ((JsxAttributes)parent).Properties;

            case SyntaxKind.CallExpression:
            case SyntaxKind.NewExpression:
                if (IsTypeNode(node)) return parent is CallExpression typedCall ? typedCall.TypeArguments : ((NewExpression)parent).TypeArguments;
                if (parent is CallExpression call)
                {
                    return call.Expression == node ? null : call.Arguments;
                }

                var newExpression = (NewExpression)parent;
                return newExpression.Expression == node ? null : newExpression.Arguments;

            case SyntaxKind.JsxElement:
            case SyntaxKind.JsxFragment:
                if (!IsJsxChild(node)) return null;
                return parent is JsxElement jsxElement ? jsxElement.Children : ((JsxFragment)parent).Children;

            case SyntaxKind.JsxOpeningElement:
            case SyntaxKind.JsxSelfClosingElement:
                if (!IsTypeNode(node)) return null;
                return parent is JsxOpeningElement jsxOpeningElement ? jsxOpeningElement.TypeArguments : ((JsxSelfClosingElement)parent).TypeArguments;

            case SyntaxKind.Block:
                return ((Block)parent).Statements;

            case SyntaxKind.CaseClause:
                return ((CaseClause)parent).Statements;

            case SyntaxKind.DefaultClause:
                return ((DefaultClause)parent).Statements;

            case SyntaxKind.ModuleBlock:
                return ((ModuleBlock)parent).Statements;

            case SyntaxKind.CaseBlock:
                return ((CaseBlock)parent).Clauses;

            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.ClassExpression:
                if (!IsClassElement(node)) return null;
                return parent is ClassDeclaration classDeclaration ? classDeclaration.Members : ((ClassExpression)parent).Members;

            case SyntaxKind.EnumDeclaration:
                return NodeTests.IsEnumMember(node) ? ((EnumDeclaration)parent).Members : null;

            case SyntaxKind.SourceFile:
                return ((SourceFile)parent).Statements;
        }

        return null;
    }
}
