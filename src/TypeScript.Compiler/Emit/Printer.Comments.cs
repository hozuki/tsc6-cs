namespace TypeScript.Compiler;

// ported from: src/compiler/emitter.ts:1266-1276, 5795-6168 — the comment pipeline of the printer.
//
// PORT-NOTE: the comment state the reference's closure declares (`containerPos`, `containerEnd`,
// `declarationListContainerEnd`, `currentLineMap`, `detachedCommentsInfo`, `hasWrittenComment`, `commentsDisabled`)
// is exactly the `Printer.State.cs` fields, so nothing below declares a second copy of any of them.
//
// PORT-NOTE: the reference's <c>enterComment</c>/<c>exitComment</c> locals (emitter.ts:1275) are the
// <c>_enterComment</c>/<c>_exitComment</c> fields PrinterCore declares in <c>Printer.cs</c> (the timer
// `performance.createTimerIf(extendedDiagnostics, "commentTime", "beforeComment", "afterComment")`), so the body below
// calls those two <see cref="Action"/> fields exactly where the reference calls the locals.
//
// PORT-NOTE: the reference calls `EmitTextWriter.writeLine()` with no argument in eight places below; the generated
// `IEmitTextWriter.WriteLine(bool?)` keeps TS's optional `force` parameter, so each of those calls is `WriteLine(null)`,
// which `EmitTextWriter.WriteLine` (utilities.ts:6366-6374) treats the same way TS's `force === undefined` does
// (`if (!lineStart || force)`).
//
// PORT-NOTE: `writer.writeSpace(" ")` is `WriteSpace(" ")`.
internal sealed partial class Printer
{
    // ---------------------------------------------------------------------------------------------
    // The phase and its two halves (emitter.ts:5795-5829)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:5795-5803 — <c>pipelineEmitWithComments</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>internal</c>, not <c>private</c>, because <c>getPipelinePhase</c> identifies the phase by method
    /// group identity (<c>pipelinePhase === pipelineEmitWithComments</c>, emitter.ts:1516) — the same treatment the
    /// other four phases get (PrinterCore).
    /// </remarks>
    internal void PipelineEmitWithComments(EmitHint hint, Node node)
    {
        var pipelinePhase = GetNextPipelinePhase(PipelinePhase.Comments, hint, node);
        var savedContainerPos = _containerPos;
        var savedContainerEnd = _containerEnd;
        var savedDeclarationListContainerEnd = _declarationListContainerEnd;
        EmitCommentsBeforeNode(node);
        pipelinePhase(hint, node);
        EmitCommentsAfterNode(node, savedContainerPos, savedContainerEnd, savedDeclarationListContainerEnd);
    }

    /// <summary>ported from: src/compiler/emitter.ts:5805-5814 — <c>emitCommentsBeforeNode</c>.</summary>
    private void EmitCommentsBeforeNode(Node node)
    {
        var emitFlags = Utilities.GetEmitFlags(node);
        var commentRange = EmitNodes.GetCommentRange(node);

        // Emit leading comments
        EmitLeadingCommentsOfNode(node, emitFlags, commentRange.Pos, commentRange.End);
        if ((emitFlags & EmitFlags.NoNestedComments) != 0)
        {
            _commentsDisabled = true;
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:5816-5829 — <c>emitCommentsAfterNode</c>.</summary>
    private void EmitCommentsAfterNode(Node node, int savedContainerPos, int savedContainerEnd, int savedDeclarationListContainerEnd)
    {
        var emitFlags = Utilities.GetEmitFlags(node);
        var commentRange = EmitNodes.GetCommentRange(node);

        // Emit trailing comments
        if ((emitFlags & EmitFlags.NoNestedComments) != 0)
        {
            _commentsDisabled = false;
        }

        EmitTrailingCommentsOfNode(node, emitFlags, commentRange.Pos, commentRange.End, savedContainerPos, savedContainerEnd, savedDeclarationListContainerEnd);
        var typeNode = EmitNodes.GetTypeNode(node);
        if (typeNode is not null)
        {
            // PORT-NOTE: TS reads `typeNode.pos`/`typeNode.end` off the `TextRange` every node participates in; the
            // generated `ITypeNode` is only `INode` (Model/NodeInterfaces.cs:3731), so the reads go through the `Node`
            // the slot holds.
            var typeNodeNode = (Node)typeNode;
            EmitTrailingCommentsOfNode(node, emitFlags, typeNodeNode.Pos, typeNodeNode.End, savedContainerPos, savedContainerEnd, savedDeclarationListContainerEnd);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Per-node leading / trailing comments (emitter.ts:5831-5908)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:5831-5866 — <c>emitLeadingCommentsOfNode</c>.</summary>
    private void EmitLeadingCommentsOfNode(Node node, EmitFlags emitFlags, int pos, int end)
    {
        _enterComment();
        _hasWrittenComment = false;

        // We have to explicitly check that the node is JsxText because if the compilerOptions.jsx is "preserve" we
        // will not do any transformation.
        // It is expensive to walk entire tree just to set one kind of node to have no comments.
        var skipLeadingComments = pos < 0 || (emitFlags & EmitFlags.NoLeadingComments) != 0 || node.Kind == SyntaxKind.JsxText;
        var skipTrailingComments = end < 0 || (emitFlags & EmitFlags.NoTrailingComments) != 0 || node.Kind == SyntaxKind.JsxText;

        // Save current container state on the stack.
        if ((pos > 0 || end > 0) && pos != end)
        {
            // Emit leading comments if the position is not synthesized and the node
            // has not opted out from emitting leading comments.
            if (!skipLeadingComments)
            {
                EmitLeadingComments(pos, /*isEmittedNode*/ node.Kind != SyntaxKind.NotEmittedStatement);
            }

            if (!skipLeadingComments || (pos >= 0 && (emitFlags & EmitFlags.NoLeadingComments) != 0))
            {
                // Advance the container position if comments get emitted or if they've been disabled explicitly
                // using NoLeadingComments.
                _containerPos = pos;
            }

            if (!skipTrailingComments || (end >= 0 && (emitFlags & EmitFlags.NoTrailingComments) != 0))
            {
                // As above.
                _containerEnd = end;

                // To avoid invalid comment emit in a down-level binding pattern, we
                // keep track of the last declaration list container's end
                if (node.Kind == SyntaxKind.VariableDeclarationList)
                {
                    _declarationListContainerEnd = end;
                }
            }
        }

        Core.ForEach(EmitNodes.GetSyntheticLeadingComments(node), (comment, _) =>
        {
            EmitLeadingSynthesizedComment(comment);
            return (object?)null;
        });
        _exitComment();
    }

    /// <summary>ported from: src/compiler/emitter.ts:5868-5885 — <c>emitTrailingCommentsOfNode</c>.</summary>
    private void EmitTrailingCommentsOfNode(Node node, EmitFlags emitFlags, int pos, int end, int savedContainerPos, int savedContainerEnd, int savedDeclarationListContainerEnd)
    {
        _enterComment();
        var skipTrailingComments = end < 0 || (emitFlags & EmitFlags.NoTrailingComments) != 0 || node.Kind == SyntaxKind.JsxText;
        Core.ForEach(EmitNodes.GetSyntheticTrailingComments(node), (comment, _) =>
        {
            EmitTrailingSynthesizedComment(comment);
            return (object?)null;
        });
        if ((pos > 0 || end > 0) && pos != end)
        {
            // Restore previous container state.
            _containerPos = savedContainerPos;
            _containerEnd = savedContainerEnd;
            _declarationListContainerEnd = savedDeclarationListContainerEnd;

            // Emit trailing comments if the position is not synthesized and the node
            // has not opted out from emitting leading comments and is an emitted node.
            if (!skipTrailingComments && node.Kind != SyntaxKind.NotEmittedStatement)
            {
                EmitTrailingComments(end);
            }
        }

        _exitComment();
    }

    // ---------------------------------------------------------------------------------------------
    // Synthesized comments (emitter.ts:5887-5920)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:5887-5898 — <c>emitLeadingSynthesizedComment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's `comment: SynthesizedComment` is the generated <see cref="ISynthesizedComment"/>
    /// here — the element type of <c>getSyntheticLeadingComments</c>/<c>getSyntheticTrailingComments</c>
    /// (emitter.ts:5864/5871). Its <c>hasLeadingNewline</c>/<c>hasTrailingNewLine</c> are nullable on that interface, so
    /// the truthiness tests go through <see cref="Js.IsTruthy"/>.
    /// </remarks>
    private void EmitLeadingSynthesizedComment(ISynthesizedComment comment)
    {
        if (Js.IsTruthy(comment.HasLeadingNewline) || comment.Kind == SyntaxKind.SingleLineCommentTrivia)
        {
            _writer.WriteLine(null);
        }

        WriteSynthesizedComment(comment);

        if (Js.IsTruthy(comment.HasTrailingNewLine) || comment.Kind == SyntaxKind.SingleLineCommentTrivia)
        {
            _writer.WriteLine(null);
        }
        else
        {
            _writer.WriteSpace(" ");
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:5900-5908 — <c>emitTrailingSynthesizedComment</c>.</summary>
    /// <remarks>PORT-NOTE: see <see cref="EmitLeadingSynthesizedComment"/> for the comment's type.</remarks>
    private void EmitTrailingSynthesizedComment(ISynthesizedComment comment)
    {
        if (!_writer.IsAtStartOfLine())
        {
            _writer.WriteSpace(" ");
        }

        WriteSynthesizedComment(comment);
        if (Js.IsTruthy(comment.HasTrailingNewLine))
        {
            _writer.WriteLine(null);
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:5910-5914 — <c>writeSynthesizedComment</c>.</summary>
    private void WriteSynthesizedComment(ISynthesizedComment comment)
    {
        var text = FormatSynthesizedComment(comment);
        var lineMap = comment.Kind == SyntaxKind.MultiLineCommentTrivia ? ScannerUtilities.ComputeLineStarts(text) : null;
        Utilities.WriteCommentRange(text, lineMap!, _writer, 0, text.Length, _newLine);
    }

    /// <summary>ported from: src/compiler/emitter.ts:5916-5920 — <c>formatSynthesizedComment</c>.</summary>
    private string FormatSynthesizedComment(ISynthesizedComment comment)
        => comment.Kind == SyntaxKind.MultiLineCommentTrivia
            ? $"/*{comment.Text}*/"
            : $"//{comment.Text}";

    // ---------------------------------------------------------------------------------------------
    // Detached comments (emitter.ts:5922-5974)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:5922-5950 — <c>emitBodyWithDetachedComments</c>.</summary>
    private void EmitBodyWithDetachedComments<T>(T node, ITextRange detachedRange, Action<T> emitCallback)
        where T : Node
    {
        _enterComment();
        var pos = detachedRange.Pos;
        var end = detachedRange.End;
        var emitFlags = Utilities.GetEmitFlags(node);
        var skipLeadingComments = pos < 0 || (emitFlags & EmitFlags.NoLeadingComments) != 0;
        var skipTrailingComments = _commentsDisabled || end < 0 || (emitFlags & EmitFlags.NoTrailingComments) != 0;
        if (!skipLeadingComments)
        {
            EmitDetachedCommentsAndUpdateCommentsInfo(detachedRange);
        }

        _exitComment();
        if ((emitFlags & EmitFlags.NoNestedComments) != 0 && !_commentsDisabled)
        {
            _commentsDisabled = true;
            emitCallback(node);
            _commentsDisabled = false;
        }
        else
        {
            emitCallback(node);
        }

        _enterComment();
        if (!skipTrailingComments)
        {
            EmitLeadingComments(detachedRange.End, /*isEmittedNode*/ true);
            if (_hasWrittenComment && !_writer.IsAtStartOfLine())
            {
                _writer.WriteLine(null);
            }
        }

        _exitComment();
    }

    /// <summary>ported from: src/compiler/emitter.ts:5952-5957 — <c>originalNodesHaveSameParent</c>.</summary>
    private bool OriginalNodesHaveSameParent(Node nodeA, Node nodeB)
    {
        nodeA = Utilities.GetOriginalNode(nodeA)!;
        // For performance, do not call `getOriginalNode` for `nodeB` if `nodeA` doesn't even
        // have a parent node.
        return nodeA.Parent is not null && nodeA.Parent == Utilities.GetOriginalNode(nodeB)!.Parent;
    }

    /// <summary>ported from: src/compiler/emitter.ts:5959-5974 — <c>siblingNodePositionsAreComparable</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>getContainingNodeArray</c> is the utilities.ts member (utilities.ts:10809-10875) and answers the
    /// covariant <see cref="INodeArray{T}"/> view of the array, so the reference's <c>indexOf</c> is
    /// <see cref="IndexOfNode"/>; the reference's <c>last</c> is <c>Core.Last</c> and its <c>getOriginalNode</c> is
    /// <c>Utilities.GetOriginalNode</c>.
    /// </remarks>
    private bool SiblingNodePositionsAreComparable(Node previousNode, Node nextNode)
    {
        if (nextNode.Pos < previousNode.End)
        {
            return false;
        }

        previousNode = Utilities.GetOriginalNode(previousNode)!;
        nextNode = Utilities.GetOriginalNode(nextNode)!;
        var parent = previousNode.Parent;
        if (parent is null || parent != nextNode.Parent)
        {
            return false;
        }

        var parentNodeArray = Utilities.GetContainingNodeArray(previousNode);
        var prevNodeIndex = parentNodeArray is not null ? IndexOfNode(parentNodeArray, previousNode) : (int?)null;
        return prevNodeIndex is not null && prevNodeIndex > -1 && IndexOfNode(parentNodeArray!, nextNode) == prevNodeIndex + 1;
    }

    /// <summary>The port of <c>Array.prototype.indexOf</c> over the covariant containing array.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference calls <c>parentNodeArray.indexOf(...)</c> on the <c>NodeArray&lt;Node&gt;</c>
    /// <c>getContainingNodeArray</c> answers (emitter.ts:5972/5973). The port's answer is the covariant
    /// <see cref="INodeArray{T}"/> view of that array (utilities.ts:10809's PORT-NOTE explains why), which is an
    /// <see cref="IReadOnlyList{T}"/> and therefore has no <c>IndexOf</c>; the linear scan below is what
    /// <c>List&lt;T&gt;.IndexOf</c> does, over the same elements and with the same reference equality.
    /// </remarks>
    private static int IndexOfNode(INodeArray<INode> array, Node node)
    {
        for (var i = 0; i < array.Count; i++)
        {
            if (array[i] == node)
            {
                return i;
            }
        }

        return -1;
    }

    // ---------------------------------------------------------------------------------------------
    // The leading-comment walk (emitter.ts:5976-6037)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:5976-5998 — <c>emitLeadingComments</c>.</summary>
    private void EmitLeadingComments(int pos, bool isEmittedNode)
    {
        _hasWrittenComment = false;

        if (isEmittedNode)
        {
            if (pos == 0 && _currentSourceFile?.IsDeclarationFile == true)
            {
                ForEachLeadingCommentToEmit(pos, EmitNonTripleSlashLeadingComment);
            }
            else
            {
                ForEachLeadingCommentToEmit(pos, EmitLeadingComment);
            }
        }
        else if (pos == 0)
        {
            // If the node will not be emitted in JS, remove all the comments(normal, pinned and ///) associated with
            // the node, unless it is a triple slash comment at the top of the file.
            // For Example:
            //      /// <reference-path ...>
            //      declare var x;
            //      /// <reference-path ...>
            //      interface F {}
            //  The first /// will NOT be removed while the second one will be removed even though both node will not
            //  be emitted
            ForEachLeadingCommentToEmit(pos, EmitTripleSlashLeadingComment);
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:6000-6004 — <c>emitTripleSlashLeadingComment</c>.</summary>
    private void EmitTripleSlashLeadingComment(int commentPos, int commentEnd, SyntaxKind kind, bool hasTrailingNewLine, int rangePos)
    {
        if (IsTripleSlashComment(commentPos, commentEnd))
        {
            EmitLeadingComment(commentPos, commentEnd, kind, hasTrailingNewLine, rangePos);
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:6006-6010 — <c>emitNonTripleSlashLeadingComment</c>.</summary>
    private void EmitNonTripleSlashLeadingComment(int commentPos, int commentEnd, SyntaxKind kind, bool hasTrailingNewLine, int rangePos)
    {
        if (!IsTripleSlashComment(commentPos, commentEnd))
        {
            EmitLeadingComment(commentPos, commentEnd, kind, hasTrailingNewLine, rangePos);
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:6012-6017 — <c>shouldWriteComment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>isJSDocLikeText</c> lives on the parser (parser.ts:462, <c>Parser.IsJSDocLikeText</c>) and
    /// <c>isPinnedComment</c> is the utilities.ts member (utilities.ts:1203-1206); both are called qualified, as the
    /// reference's imports are.
    /// </remarks>
    private bool ShouldWriteComment(string text, int pos)
    {
        if (Js.IsTruthy(_printerOptions.OnlyPrintJsDocStyle))
        {
            return Parser.IsJSDocLikeText(text, pos) || Utilities.IsPinnedComment(text, pos);
        }

        return true;
    }

    /// <summary>ported from: src/compiler/emitter.ts:6019-6037 — <c>emitLeadingComment</c>.</summary>
    private void EmitLeadingComment(int commentPos, int commentEnd, SyntaxKind kind, bool hasTrailingNewLine, int rangePos)
    {
        if (_currentSourceFile is null || !ShouldWriteComment(_currentSourceFile.Text!, commentPos)) return;
        if (!_hasWrittenComment)
        {
            Utilities.EmitNewLineBeforeLeadingCommentOfPosition(GetCurrentLineMap(), _writer, rangePos, commentPos);
            _hasWrittenComment = true;
        }

        // Leading comments are emitted at /*leading comment1 */space/*leading comment*/space
        EmitPos(commentPos);
        Utilities.WriteCommentRange(_currentSourceFile.Text!, GetCurrentLineMap(), _writer, commentPos, commentEnd, _newLine);
        EmitPos(commentEnd);

        if (hasTrailingNewLine)
        {
            _writer.WriteLine(null);
        }
        else if (kind == SyntaxKind.MultiLineCommentTrivia)
        {
            _writer.WriteSpace(" ");
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:6039-6045 — <c>emitLeadingCommentsOfPosition</c>.</summary>
    private void EmitLeadingCommentsOfPosition(int pos)
    {
        if (_commentsDisabled || pos == -1)
        {
            return;
        }

        EmitLeadingComments(pos, /*isEmittedNode*/ true);
    }

    // ---------------------------------------------------------------------------------------------
    // The trailing-comment walk (emitter.ts:6047-6103)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:6047-6049 — <c>emitTrailingComments</c>.</summary>
    private void EmitTrailingComments(int pos)
    {
        ForEachTrailingCommentToEmit(pos, EmitTrailingComment);
    }

    /// <summary>ported from: src/compiler/emitter.ts:6051-6065 — <c>emitTrailingComment</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's unused <c>_kind</c> parameter keeps its name.</remarks>
    private void EmitTrailingComment(int commentPos, int commentEnd, SyntaxKind _kind, bool hasTrailingNewLine)
    {
        if (_currentSourceFile is null || !ShouldWriteComment(_currentSourceFile.Text!, commentPos)) return;
        // trailing comments are emitted at space/*trailing comment1 */space/*trailing comment2*/
        if (!_writer.IsAtStartOfLine())
        {
            _writer.WriteSpace(" ");
        }

        EmitPos(commentPos);
        Utilities.WriteCommentRange(_currentSourceFile.Text!, GetCurrentLineMap(), _writer, commentPos, commentEnd, _newLine);
        EmitPos(commentEnd);

        if (hasTrailingNewLine)
        {
            _writer.WriteLine(null);
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:6067-6074 — <c>emitTrailingCommentsOfPosition</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference picks one of three callbacks with a nested conditional; TS's function types accept a
    /// three-parameter callback where four are wanted, C# delegates do not, so the <c>forceNoNewline</c> arm carries an
    /// explicit four-parameter delegate around the three-parameter method
    /// (<c>forceNoNewline ? emitTrailingCommentOfPositionNoNewline : …</c>, emitter.ts:6071).
    /// </remarks>
    private void EmitTrailingCommentsOfPosition(int pos, bool prefixSpace = false, bool forceNoNewline = false)
    {
        if (_commentsDisabled)
        {
            return;
        }

        _enterComment();
        ForEachTrailingCommentToEmit(
            pos,
            prefixSpace
                ? EmitTrailingComment
                : forceNoNewline
                    ? new Action<int, int, SyntaxKind, bool>((commentPos, commentEnd, kind, _) => EmitTrailingCommentOfPositionNoNewline(commentPos, commentEnd, kind))
                    : EmitTrailingCommentOfPosition);
        _exitComment();
    }

    /// <summary>ported from: src/compiler/emitter.ts:6076-6087 — <c>emitTrailingCommentOfPositionNoNewline</c>.</summary>
    private void EmitTrailingCommentOfPositionNoNewline(int commentPos, int commentEnd, SyntaxKind kind)
    {
        if (_currentSourceFile is null) return;
        // trailing comments of a position are emitted at /*trailing comment1 */space/*trailing comment*/space

        EmitPos(commentPos);
        Utilities.WriteCommentRange(_currentSourceFile.Text!, GetCurrentLineMap(), _writer, commentPos, commentEnd, _newLine);
        EmitPos(commentEnd);

        if (kind == SyntaxKind.SingleLineCommentTrivia)
        {
            // still write a newline for single-line comments, so closing tokens aren't written on the same line
            _writer.WriteLine(null);
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:6089-6103 — <c>emitTrailingCommentOfPosition</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's unused <c>_kind</c> parameter keeps its name.</remarks>
    private void EmitTrailingCommentOfPosition(int commentPos, int commentEnd, SyntaxKind _kind, bool hasTrailingNewLine)
    {
        if (_currentSourceFile is null) return;
        // trailing comments of a position are emitted at /*trailing comment1 */space/*trailing comment*/space

        EmitPos(commentPos);
        Utilities.WriteCommentRange(_currentSourceFile.Text!, GetCurrentLineMap(), _writer, commentPos, commentEnd, _newLine);
        EmitPos(commentEnd);

        if (hasTrailingNewLine)
        {
            _writer.WriteLine(null);
        }
        else
        {
            _writer.WriteSpace(" ");
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The comment-range walks (emitter.ts:6105-6168)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:6105-6115 — <c>forEachLeadingCommentToEmit</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's callback is the tuple
    /// <c>(commentPos, commentEnd, kind, hasTrailingNewLine, rangePos)</c> and the scanner's
    /// <c>forEachLeadingCommentRange</c> hands it the same <c>state</c> (scanner.ts:932-936) — that tuple is
    /// <see cref="Action{T1,T2,T3,T4,T5}"/> here, and the scanner's <c>T | undefined</c> result becomes the always-null
    /// <c>object?</c> the port's <c>Core.ForEach</c> convention uses for a callback that never stops the walk.
    /// </remarks>
    private void ForEachLeadingCommentToEmit(int pos, Action<int, int, SyntaxKind, bool, int> cb)
    {
        // Emit the leading comments only if the container's pos doesn't match because the container should take care
        // of emitting these comments
        if (_currentSourceFile is not null && (_containerPos == -1 || pos != _containerPos))
        {
            if (HasDetachedComments(pos))
            {
                ForEachLeadingCommentWithoutDetachedComments(cb);
            }
            else
            {
                ScannerUtilities.ForEachLeadingCommentRange<int, object?>(
                    _currentSourceFile.Text!,
                    pos,
                    (commentPos, commentEnd, kind, hasTrailingNewLine, rangePos, _) =>
                    {
                        cb(commentPos, commentEnd, kind, hasTrailingNewLine, rangePos);
                        return null;
                    },
                    pos);
            }
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:6117-6122 — <c>forEachTrailingCommentToEmit</c>.</summary>
    private void ForEachTrailingCommentToEmit(int end, Action<int, int, SyntaxKind, bool> cb)
    {
        // Emit the trailing comments only if the container's end doesn't match because the container should take care
        // of emitting these comments
        if (_currentSourceFile is not null && (_containerEnd == -1 || (end != _containerEnd && end != _declarationListContainerEnd)))
        {
            ScannerUtilities.ForEachTrailingCommentRange<object?>(
                _currentSourceFile.Text!,
                end,
                (commentPos, commentEnd, kind, hasTrailingNewLine) =>
                {
                    cb(commentPos, commentEnd, kind, hasTrailingNewLine);
                    return null;
                });
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:6124-6126 — <c>hasDetachedComments</c>.</summary>
    private bool HasDetachedComments(int pos)
        => _detachedCommentsInfo is not null && Core.Last(_detachedCommentsInfo).NodePos == pos;

    /// <summary>ported from: src/compiler/emitter.ts:6128-6140 — <c>forEachLeadingCommentWithoutDetachedComments</c>
    /// </summary>
    /// <remarks>PORT-NOTE: <c>Array.prototype.pop</c> is <c>RemoveAt(Count - 1)</c>.</remarks>
    private void ForEachLeadingCommentWithoutDetachedComments(Action<int, int, SyntaxKind, bool, int> cb)
    {
        if (_currentSourceFile is null) return;
        // get the leading comments from detachedPos
        var pos = Core.Last(_detachedCommentsInfo!).DetachedCommentEndPos;
        if (_detachedCommentsInfo!.Count - 1 != 0)
        {
            _detachedCommentsInfo!.RemoveAt(_detachedCommentsInfo!.Count - 1);
        }
        else
        {
            _detachedCommentsInfo = null;
        }

        ScannerUtilities.ForEachLeadingCommentRange<int, object?>(
            _currentSourceFile.Text!,
            pos,
            (commentPos, commentEnd, kind, hasTrailingNewLine, rangePos, _) =>
            {
                cb(commentPos, commentEnd, kind, hasTrailingNewLine, rangePos);
                return null;
            },
            pos);
    }

    /// <summary>ported from: src/compiler/emitter.ts:6142-6152 — <c>emitDetachedCommentsAndUpdateCommentsInfo</c>
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: <c>emitDetachedComments</c> is the utilities.ts member (utilities.ts:7012-7080); the port's
    /// <c>DetachedCommentInfo</c> (Printer.State.cs:43) is a value type modelling the reference's record, so "no
    /// detached comment" is <c>DetachedCommentInfo?</c> rather than <c>undefined</c>.
    /// </remarks>
    private void EmitDetachedCommentsAndUpdateCommentsInfo(ITextRange range)
    {
        var currentDetachedCommentInfo = _currentSourceFile is not null
            ? Utilities.EmitDetachedComments(_currentSourceFile.Text!, GetCurrentLineMap(), _writer, EmitComment, range, _newLine, _commentsDisabled)
            : null;
        if (currentDetachedCommentInfo is not null)
        {
            if (_detachedCommentsInfo is not null)
            {
                _detachedCommentsInfo.Add(currentDetachedCommentInfo.Value);
            }
            else
            {
                _detachedCommentsInfo = [currentDetachedCommentInfo.Value];
            }
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:6154-6160 — <c>emitComment</c>.</summary>
    private void EmitComment(string text, IList<int> lineMap, IEmitTextWriter writer, int commentPos, int commentEnd, string newLine)
    {
        if (_currentSourceFile is null || !ShouldWriteComment(_currentSourceFile.Text!, commentPos)) return;
        EmitPos(commentPos);
        Utilities.WriteCommentRange(text, lineMap, writer, commentPos, commentEnd, newLine);
        EmitPos(commentEnd);
    }

    /// <summary>ported from: src/compiler/emitter.ts:6162-6168 — <c>isTripleSlashComment</c>.</summary>
    /// <remarks>
    /// <para>Determine if the given comment is a triple-slash.</para>
    /// <para>PORT-NOTE: <c>isRecognizedTripleSlashComment</c> is the utilities.ts member (utilities.ts:1182-1201).</para>
    /// </remarks>
    private bool IsTripleSlashComment(int commentPos, int commentEnd)
        => _currentSourceFile is not null
            && Utilities.IsRecognizedTripleSlashComment(_currentSourceFile.Text!, commentPos, commentEnd);
}
