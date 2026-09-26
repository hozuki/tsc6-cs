using System.Runtime.CompilerServices;

namespace TypeScript.Compiler;

// ported from: src/compiler/parser.ts:9927-10552
/// <summary>
/// Incremental re-parsing: given the tree of a previous parse and the text that replaced one span of it, produce a new
/// tree that reuses every node the edit cannot have affected.
/// </summary>
/// <remarks>
/// PORT-NOTE: the feature mutates the old tree in place — nodes are moved to their new positions, marked as
/// intersecting the change, and then handed to the parser for reuse — so a file that has been through
/// <see cref="UpdateSourceFile"/> must never be used again. TS guards that with a <c>WeakSet</c> (<c>markAsIncrementallyParsed</c>,
/// parser.ts:9929); the port keeps the same guard, only the set's type differs.
/// <para>
/// PORT-NOTE: TS's <c>markAsIncrementallyParsed</c>, <c>intersectsIncrementalChange</c> and
/// <c>markAsIntersectingIncrementalChange</c> are module-level functions in <c>parser.ts</c>, not members of the
/// <c>IncrementalParser</c> namespace. They exist only for this feature, and the parser calls two of them
/// (parser.ts:1964, 3145), so the port hangs all three off this class rather than inventing a second home.
/// </para>
/// <para>
/// PORT-NOTE: TS models the <c>Node | NodeArray&lt;Node&gt;</c> union by passing an <c>isArray</c> flag that the
/// implementation switches on, and by reading <c>.parent</c> through <c>any</c>. The port cannot union a class with an
/// unrelated class, so every member of that union becomes two overloads: <see cref="Node"/> and
/// <see cref="INodeArray{T}"/>, with the shared arithmetic behind one core. The array element view is
/// <c>INodeArray&lt;INode&gt;</c> — the type <c>ForEachChild</c> hands out — because an interface-chain node (for
/// example <c>IStatement</c>) is not the class <see cref="Node"/>; element casts go through <see cref="AsNode"/>.
/// </para>
/// <para>
/// PORT-NOTE: <c>forEachChild</c>'s callbacks keep walking on any <em>falsy</em> result — the generated table chains
/// them with <c>||</c>, so TS writes both <c>return;</c> (undefined) and <c>return false;</c> for "keep going" and
/// <c>return true;</c> for "stop". The port's <c>ForEachChildTable.ForEachChild</c> tests the result with
/// <c>Js.IsTruthy</c>, so <c>false</c> would keep working; every callback below nevertheless returns the null value of
/// its <c>T?</c> result instead, because that is the one result that means "no result" under both that test and TS's
/// own table. <c>true</c> is the stop value, unchanged.
/// </para>
/// </remarks>
internal static class IncrementalParser
{
    /// <summary>The "no position cached yet" sentinel of the syntax cursor.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares <c>const enum InvalidPosition { Value = -1 }</c> only so the use sites can read
    /// <c>InvalidPosition.Value</c>. A one-member enum is not a better spelling of <c>-1</c> in C#, so the port keeps the
    /// name as a private constant. It doubles as "no array cached" for <c>currentArrayIndex</c>.
    /// <para>
    /// PORT-NOTE: the brief mentioned an <c>isValidPosition</c> helper next to this sentinel; release-6.0's
    /// <c>parser.ts</c> has no such function (the cursor compares against <c>InvalidPosition</c> directly), so none is
    /// ported.
    /// </para>
    /// </remarks>
    private const int InvalidPosition = -1;

    /// <summary>The value stored in the two weak sets; TS's <c>WeakSet</c> carries no value at all.</summary>
    private static readonly object s_marker = new();

    // ported from: src/compiler/parser.ts:9927 — incrementallyParsedFiles
    /// <remarks>
    /// PORT-NOTE: TS uses <c>WeakSet&lt;SourceFile&gt;</c> — membership only, and the key is held weakly.
    /// <see cref="ConditionalWeakTable{TKey,TValue}"/> with a dummy value is the CLR counterpart (the same choice
    /// <see cref="NodeChildren"/> makes for its node table).
    /// </remarks>
    private static readonly ConditionalWeakTable<SourceFile, object> s_incrementallyParsedFiles = new();

    // ported from: src/compiler/parser.ts:9936 — intersectingChangeSet
    /// <remarks>
    /// PORT-NOTE: TS keys one <c>WeakSet</c> by the union <c>Node | NodeArray&lt;Node&gt;</c>. C# cannot key one table by
    /// two unrelated types, so each arm of the union gets a table of its own; both are reference-identity sets, and the
    /// public API keeps TS's single name as a pair of overloads.
    /// </remarks>
    private static readonly ConditionalWeakTable<Node, object> s_intersectingNodes = new();
    private static readonly ConditionalWeakTable<INodeArray<INode>, object> s_intersectingArrays = new();

    // ported from: src/compiler/parser.ts:9929-9934 — markAsIncrementallyParsed
    /// <summary>Records that this file has been updated in place, and fails if it already has been.</summary>
    private static void MarkAsIncrementallyParsed(SourceFile sourceFile)
    {
        if (s_incrementallyParsedFiles.TryGetValue(sourceFile, out _))
        {
            Debug.Fail("Source file has already been incrementally parsed");
        }

        // TS's WeakSet.add is idempotent; the Debug.Fail above has already thrown for the duplicate case.
        s_incrementallyParsedFiles.AddOrUpdate(sourceFile, s_marker);
    }

    // ported from: src/compiler/parser.ts:9938-9940 — intersectsIncrementalChange
    /// <summary>Whether the edit range touched this node, which makes it unusable for reuse.</summary>
    public static bool IntersectsIncrementalChange(Node node) => s_intersectingNodes.TryGetValue(node, out _);

    /// <summary>TS: <c>intersectsIncrementalChange</c> over the <c>NodeArray</c> arm of the union.</summary>
    public static bool IntersectsIncrementalChange(INodeArray<INode> array) => s_intersectingArrays.TryGetValue(array, out _);

    // ported from: src/compiler/parser.ts:9942-9944 — markAsIntersectingIncrementalChange
    /// <summary>Marks a node as intersecting the edit range.</summary>
    public static void MarkAsIntersectingIncrementalChange(Node node) => s_intersectingNodes.AddOrUpdate(node, s_marker);

    /// <summary>TS: <c>markAsIntersectingIncrementalChange</c> over the <c>NodeArray</c> arm of the union.</summary>
    public static void MarkAsIntersectingIncrementalChange(INodeArray<INode> array) => s_intersectingArrays.AddOrUpdate(array, s_marker);

    // ported from: src/compiler/parser.ts:9948-10033 — updateSourceFile
    /// <summary>
    /// Re-parses <paramref name="sourceFile"/> against <paramref name="newText"/>, reusing the nodes that
    /// <paramref name="textChangeRange"/> cannot have affected.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: <paramref name="sourceFile"/> is mutated in place (see the class remarks) and must not be used again
    /// once this returns. The returned file is a new <see cref="SourceFile"/>.
    /// <para>
    /// PORT-NOTE: TS tests <c>sourceFile.statements.length === 0</c> to pick the full-reparse fallback. A file the port
    /// has not parsed yet can carry a null <c>statements</c> (the generated field is nullable and only the parser fills
    /// it), and "no statements" is the same answer, so both count as empty here.
    /// </para>
    /// </remarks>
    public static SourceFile UpdateSourceFile(SourceFile sourceFile, string newText, ITextChangeRange textChangeRange, bool aggressiveChecks)
    {
        aggressiveChecks = aggressiveChecks || Debug.ShouldAssert(AssertionLevel.Aggressive);

        CheckChangeRange(sourceFile, newText, textChangeRange, aggressiveChecks);
        if (Core.TextChangeRangeIsUnchanged(textChangeRange))
        {
            // If the text didn't change, then we can just return our current source file as-is.
            return sourceFile;
        }

        if (sourceFile.Statements is null || sourceFile.Statements.Count == 0)
        {
            // If we don't have any statements in the current source file, then there's no real way to incrementally
            // parse. So just do a full parse instead.
            return ParseSourceFile(sourceFile, newText, syntaxCursor: null);
        }

        // Make sure we're not trying to incrementally update a source file more than once. Once we do an update the
        // original source file is considered unusable from that point onwards.
        //
        // This is because we do incremental parsing in-place: we take nodes from the old tree and give them new
        // positions and parents. From that point on, trusting the old tree at all is not possible as far too much of it
        // may violate invariants.
        MarkAsIncrementallyParsed(sourceFile);
        Parser.FixupParentReferences(sourceFile);
        var oldText = sourceFile.Text!;
        var syntaxCursor = CreateSyntaxCursor(sourceFile);

        // Make the actual change larger so that we know to reparse anything whose lookahead might have intersected the
        // change.
        var changeRange = ExtendToAffectedRange(sourceFile, textChangeRange);
        CheckChangeRange(sourceFile, newText, changeRange, aggressiveChecks);

        // Ensure that extending the affected range only moved the start of the change range earlier in the file.
        Debug.Assert(changeRange.Span.Start <= textChangeRange.Span!.Start);
        Debug.Assert(Core.TextSpanEnd(changeRange.Span) == Core.TextSpanEnd(textChangeRange.Span!));
        Debug.Assert(Core.TextSpanEnd(Core.TextChangeRangeNewSpan(changeRange)) == Core.TextSpanEnd(Core.TextChangeRangeNewSpan(textChangeRange)));

        // This is the amount the nodes after the edit range need to be adjusted by. It can be positive (if the edit
        // added characters), negative (if the edit deleted characters) or zero (if this was a pure overwrite with
        // nothing added or removed).
        var delta = Core.TextChangeRangeNewSpan(changeRange).Length - changeRange.Span.Length;

        // If we added or removed characters during the edit, then we need to go and adjust all the nodes after the edit.
        // Those nodes may move forward (if we inserted chars) or backward (if we deleted chars).
        //
        // Doing this helps us out in two ways. First, it means that any nodes/tokens we want to reuse are already at
        // the appropriate position in the new text. That way when we reuse them, we don't have to figure out if they
        // need to be adjusted. Second, it makes it very easy to determine if we can reuse a node: if the node's
        // position is at where we are in the text, then we can reuse it. Otherwise we can't. If the node's position is
        // ahead of us, then we'll need to rescan tokens. If the node's position is behind us, then we'll need to skip it
        // or crumble it as appropriate.
        //
        // We will also adjust the positions of nodes that intersect the change range as well. By doing this, we ensure
        // that all the positions in the old tree are consistent, not just the positions of nodes entirely before/after
        // the change range. By being consistent, we can then easily map from positions to nodes in the old tree easily.
        //
        // Also, mark any syntax elements that intersect the changed span. We know, up front, that we cannot reuse these
        // elements.
        UpdateTokenPositionsAndMarkElements(
            sourceFile,
            changeRange.Span.Start,
            Core.TextSpanEnd(changeRange.Span),
            Core.TextSpanEnd(Core.TextChangeRangeNewSpan(changeRange)),
            delta,
            oldText,
            newText,
            aggressiveChecks);

        // Now that we've set up our internal incremental state just proceed and parse the source file in the normal
        // fashion. When possible the parser will retrieve and reuse nodes from the old tree.
        //
        // Note: passing in 'true' for setNodeParents is very important. When incrementally parsing, we will be reusing
        // nodes from the old tree, and placing it into new parents. If we don't set the parents now, we'll end up with
        // an observably inconsistent tree. Setting the parents on the new tree should be very fast. We will immediately
        // bail out of walking any subtrees when we can see that their parents are already correct.
        var result = ParseSourceFile(sourceFile, newText, syntaxCursor);
        result.CommentDirectives = GetNewCommentDirectives(
            sourceFile.CommentDirectives,
            result.CommentDirectives,
            changeRange.Span.Start,
            Core.TextSpanEnd(changeRange.Span),
            delta,
            oldText,
            newText,
            aggressiveChecks);
        result.ImpliedNodeFormat = sourceFile.ImpliedNodeFormat;
        NodeChildren.TransferSourceFileChildren(sourceFile, result);
        return result;
    }

    /// <summary>TS's two <c>Parser.parseSourceFile</c> calls in <c>updateSourceFile</c> — same arguments, one place.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads <c>sourceFile.fileName</c>, <c>scriptKind</c>, <c>setExternalModuleIndicator</c> and
    /// <c>jsDocParsingMode</c> at both call sites. The port's <c>jsDocParsingMode</c> is non-optional at the parser's
    /// entry (TS gives it the default <c>JSDocParsingMode.ParseAll</c>), so an unset mode on the old file maps to
    /// <c>ParseAll</c> here — which is exactly what TS's default parameter does when it is handed <c>undefined</c>.
    /// </remarks>
    private static SourceFile ParseSourceFile(SourceFile sourceFile, string newText, ISyntaxCursor? syntaxCursor)
        => Parser.ParseSourceFile(
            sourceFile.FileName!,
            newText,
            sourceFile.LanguageVersion,
            syntaxCursor,
            setParentNodes: true,
            sourceFile.ScriptKind,
            sourceFile.SetExternalModuleIndicator,
            sourceFile.JsDocParsingMode ?? JSDocParsingMode.ParseAll);

    // ported from: src/compiler/parser.ts:10035-10084 — getNewCommentDirectives
    /// <summary>
    /// Carries the old file's <c>@ts-ignore</c>/<c>@ts-expect-error</c> directives over to the new file: the ones before
    /// the change are kept as they are, the ones after it are shifted by <paramref name="delta"/>, and the ones inside
    /// the change are dropped in favour of the ones the fresh parse found.
    /// </summary>
    private static List<ICommentDirective>? GetNewCommentDirectives(
        List<ICommentDirective>? oldDirectives,
        List<ICommentDirective>? newDirectives,
        int changeStart,
        int changeRangeOldEnd,
        int delta,
        string oldText,
        string newText,
        bool aggressiveChecks)
    {
        if (oldDirectives is null) return newDirectives;

        List<ICommentDirective>? commentDirectives = null;
        var addedNewlyScannedDirectives = false;
        foreach (var directive in oldDirectives)
        {
            var range = directive.Range!;
            var type = directive.Type;

            // Range before the change
            if (range.End < changeStart)
            {
                commentDirectives = Core.Append(commentDirectives, directive) as List<ICommentDirective>;
            }
            else if (range.Pos > changeRangeOldEnd)
            {
                AddNewlyScannedDirectives();

                // Node is entirely past the change range. We need to move both its pos and end, forward or backward
                // appropriately.
                var updatedDirective = new CommentDirective
                {
                    Range = new TextRange { Pos = range.Pos + delta, End = range.End + delta },
                    Type = type,
                };
                commentDirectives = Core.Append(commentDirectives, updatedDirective) as List<ICommentDirective>;
                if (aggressiveChecks)
                {
                    Debug.Assert(oldText.Substring(range.Pos, range.End - range.Pos) == newText.Substring(updatedDirective.Range.Pos, updatedDirective.Range.End - updatedDirective.Range.Pos));
                }
            }

            // Ignore ranges that fall in change range
        }

        AddNewlyScannedDirectives();
        return commentDirectives;

        void AddNewlyScannedDirectives()
        {
            if (addedNewlyScannedDirectives) return;
            addedNewlyScannedDirectives = true;
            if (commentDirectives is null)
            {
                commentDirectives = newDirectives;
            }
            else if (newDirectives is not null)
            {
                commentDirectives.AddRange(newDirectives);
            }
        }
    }

    // ported from: src/compiler/parser.ts:10086-10138 — moveElementEntirelyPastChangeRange
    /// <summary>
    /// Moves a node that sits entirely after the change range, and everything under it, by <paramref name="delta"/>.
    /// </summary>
    private static void MoveElementEntirelyPastChangeRange(Node element, SourceFile origSourceFile, int delta, string oldText, string newText, bool aggressiveChecks)
        => MoveElementEntirelyPastChangeRange(element, null, origSourceFile, delta, oldText, newText, aggressiveChecks);

    /// <summary>TS: <c>moveElementEntirelyPastChangeRange</c> over the <c>NodeArray</c> arm of the union.</summary>
    private static void MoveElementEntirelyPastChangeRange(INodeArray<INode> element, SourceFile origSourceFile, int delta, string oldText, string newText, bool aggressiveChecks)
        => MoveElementEntirelyPastChangeRange(null, element, origSourceFile, delta, oldText, newText, aggressiveChecks);

    /// <summary>The shared body of the two <c>MoveElementEntirelyPastChangeRange</c> overloads.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares two signatures over the <c>Node | NodeArray&lt;Node&gt;</c> union plus the implementation
    /// that dispatches on an <c>isArray</c> flag, then casts the two callbacks to <c>void</c>-returning ones to pass
    /// them to <c>forEachChild</c>. The two <c>Node?</c>/<c>INodeArray</c> parameters and the null test are that
    /// dispatch; keeping one body is what the original shape is for, because both arms recurse into the same two local
    /// functions. The flag/cast noise around the callbacks is gone: <c>ForEachChild</c> takes them as they are.
    /// </remarks>
    private static void MoveElementEntirelyPastChangeRange(Node? node, INodeArray<INode>? array, SourceFile origSourceFile, int delta, string oldText, string newText, bool aggressiveChecks)
    {
        if (array is not null)
        {
            VisitArray(array);
        }
        else
        {
            VisitNode(node!);
        }

        bool? VisitNode(Node node)
        {
            var text = "";
            if (aggressiveChecks && ShouldCheckNode(node))
            {
                text = oldText.Substring(node.Pos, node.End - node.Pos);
            }

            // Ditch any existing LS children we may have created. This way we can avoid moving them forward.
            NodeChildren.UnsetNodeChildren(node, origSourceFile);

            SetTextRangePosEnd(node, node.Pos + delta, node.End + delta);

            if (aggressiveChecks && ShouldCheckNode(node))
            {
                Debug.Assert(text == newText.Substring(node.Pos, node.End - node.Pos));
            }

            ForEachChildTable.ForEachChild(node, VisitNode, VisitArray);
            if (GetJSDocNodes(node) is { } jsDocNodes)
            {
                foreach (var jsDocComment in jsDocNodes)
                {
                    VisitNode(jsDocComment);
                }
            }

            CheckNodePositions(node, aggressiveChecks);
            return null;
        }

        bool? VisitArray(INodeArray<INode> array)
        {
            SetTextRangePosEnd(array, array.Pos + delta, array.End + delta);

            foreach (var element in array)
            {
                VisitNode(AsNode(element));
            }

            return null;
        }
    }

    // ported from: src/compiler/parser.ts:10140-10150 — shouldCheckNode
    /// <summary>Whether the node's text is cheap and stable enough to assert on after a move.</summary>
    private static bool ShouldCheckNode(Node node)
        => NodeTests.IsStringLiteral(node) || NodeTests.IsNumericLiteral(node) || NodeTests.IsIdentifier(node);

    // ported from: src/compiler/parser.ts:10153-10221 — adjustIntersectingElement
    /// <summary>
    /// Re-positions an element that overlaps the change range so the tree stays consistent: children stay inside their
    /// parents and siblings stay ordered.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS takes <c>Node | NodeArray&lt;Node&gt;</c> and reads <c>(element as any).parent</c>. The port splits
    /// the union into two overloads; <see cref="AdjustIntersectingElementCore"/> is the shared body (the asserts and the
    /// two <c>Math</c> calls), and only the node arm has a parent to check.
    /// </remarks>
    private static void AdjustIntersectingElement(Node element, int changeStart, int changeRangeOldEnd, int changeRangeNewEnd, int delta)
    {
        var (pos, end) = AdjustIntersectingElementCore(element, changeStart, changeRangeOldEnd, changeRangeNewEnd, delta);

        if (element.Parent is not null)
        {
            Debug.AssertGreaterThanOrEqual(pos, element.Parent.Pos);
            Debug.AssertLessThanOrEqual(end, element.Parent.End);
        }

        SetTextRangePosEnd(element, pos, end);
    }

    /// <summary>TS: <c>adjustIntersectingElement</c> over the <c>NodeArray</c> arm of the union.</summary>
    private static void AdjustIntersectingElement(INodeArray<INode> element, int changeStart, int changeRangeOldEnd, int changeRangeNewEnd, int delta)
    {
        var (pos, end) = AdjustIntersectingElementCore(element, changeStart, changeRangeOldEnd, changeRangeNewEnd, delta);
        SetTextRangePosEnd(element, pos, end);
    }

    /// <summary>The shared body of the two <c>AdjustIntersectingElement</c> overloads.</summary>
    /// <remarks>
    /// PORT-NOTE: the parameter is <see cref="IReadonlyTextRange"/> because that is the read view both arms of the
    /// union meet in (TS's <c>Node | NodeArray&lt;Node&gt;</c> reads <c>pos</c>/<c>end</c> the same way); the writable
    /// <see cref="ITextRange"/> that the two overloads write through is not a view the array interface declares.
    /// </remarks>
    private static (int Pos, int End) AdjustIntersectingElementCore(IReadonlyTextRange element, int changeStart, int changeRangeOldEnd, int changeRangeNewEnd, int delta)
    {
        Debug.Assert(element.End >= changeStart, "Adjusting an element that was entirely before the change range");
        Debug.Assert(element.Pos <= changeRangeOldEnd, "Adjusting an element that was entirely after the change range");
        Debug.Assert(element.Pos <= element.End);

        // We have an element that intersects the change range in some way. It may have its start, or its end (or both)
        // in the changed range. We want to adjust any part that intersects such that the final tree is in a consistent
        // state. i.e. all children have spans within the span of their parent, and all siblings are ordered properly.
        //
        // We may need to update both the 'pos' and the 'end' of the element.

        // If the 'pos' is before the start of the change, then we don't need to touch it. If it isn't, then the 'pos'
        // must be inside the change. How we update it will depend on if delta is positive or negative. If delta is
        // positive then we have something like:
        //
        //  -------------------AAA-----------------
        //  -------------------BBBCCCCCCC-----------------
        //
        // In this case, we consider any node that started in the change range to still be starting at the same position.
        //
        // however, if the delta is negative, then we instead have something like this:
        //
        //  -------------------XXXYYYYYYY-----------------
        //  -------------------ZZZ-----------------
        //
        // In this case, any element that started in the 'X' range will keep its position. However any element that
        // started after that will have their pos adjusted to be at the end of the new range. i.e. any node that started
        // in the 'Y' range will be adjusted to have their start at the end of the 'Z' range.
        //
        // The element will keep its position if possible. Or move backward to the new-end if it's in the 'Y' range.
        var pos = Math.Min(element.Pos, changeRangeNewEnd);

        // If the 'end' is after the change range, then we always adjust it by the delta amount. However, if the end is
        // in the change range, then how we adjust it will depend on if delta is positive or negative. If delta is
        // positive then we have something like:
        //
        //  -------------------AAA-----------------
        //  -------------------BBBCCCCCCC-----------------
        //
        // In this case, we consider any node that ended inside the change range to keep its end position.
        //
        // however, if the delta is negative, then we instead have something like this:
        //
        //  -------------------XXXYYYYYYY-----------------
        //  -------------------ZZZ-----------------
        //
        // In this case, any element that ended in the 'X' range will keep its position. However any element that ended
        // after that will have their pos adjusted to be at the end of the new range. i.e. any element that ended in the
        // 'Y' range will be adjusted to have their end at the end of the 'Z' range.
        var end = element.End >= changeRangeOldEnd
            // Element ends after the change range. Always adjust the end pos.
            ? element.End + delta
            // Element ends in the change range. The element will keep its position if possible. Or move backward to the
            // new-end if it's in the 'Y' range.
            : Math.Min(element.End, changeRangeNewEnd);

        Debug.Assert(pos <= end);
        return (pos, end);
    }

    // ported from: src/compiler/parser.ts:10223-10300 — updateTokenPositionsAndMarkElements
    /// <summary>
    /// Walks the old tree once: everything after the change range is moved by <paramref name="delta"/>, everything the
    /// change range intersects has its span clamped and is marked as unusable, and everything before it is left alone.
    /// </summary>
    private static void UpdateTokenPositionsAndMarkElements(
        SourceFile sourceFile,
        int changeStart,
        int changeRangeOldEnd,
        int changeRangeNewEnd,
        int delta,
        string oldText,
        string newText,
        bool aggressiveChecks)
    {
        VisitNode(sourceFile);

        bool? VisitNode(Node child)
        {
            Debug.Assert(child.Pos <= child.End);
            if (child.Pos > changeRangeOldEnd)
            {
                // Node is entirely past the change range. We need to move both its pos and end, forward or backward
                // appropriately.
                MoveElementEntirelyPastChangeRange(child, sourceFile, delta, oldText, newText, aggressiveChecks);
                return null;
            }

            // Check if the element intersects the change range. If it does, then it is not reusable. Also, we'll need
            // to recurse to see what constituent portions we may be able to use.
            var fullEnd = child.End;
            if (fullEnd >= changeStart)
            {
                MarkAsIntersectingIncrementalChange(child);
                NodeChildren.UnsetNodeChildren(child, sourceFile);

                // Adjust the pos or end (or both) of the intersecting element accordingly.
                AdjustIntersectingElement(child, changeStart, changeRangeOldEnd, changeRangeNewEnd, delta);
                ForEachChildTable.ForEachChild(child, VisitNode, VisitArray);
                if (GetJSDocNodes(child) is { } jsDocNodes)
                {
                    foreach (var jsDocComment in jsDocNodes)
                    {
                        VisitNode(jsDocComment);
                    }
                }

                CheckNodePositions(child, aggressiveChecks);
                return null;
            }

            // Otherwise, the node is entirely before the change range. No need to do anything with it.
            Debug.Assert(fullEnd < changeStart);
            return null;
        }

        bool? VisitArray(INodeArray<INode> array)
        {
            Debug.Assert(array.Pos <= array.End);
            if (array.Pos > changeRangeOldEnd)
            {
                // Array is entirely after the change range. We need to move it, and move any of its children.
                MoveElementEntirelyPastChangeRange(array, sourceFile, delta, oldText, newText, aggressiveChecks);
                return null;
            }

            // Check if the element intersects the change range. If it does, then it is not reusable. Also, we'll need
            // to recurse to see what constituent portions we may be able to use.
            var fullEnd = array.End;
            if (fullEnd >= changeStart)
            {
                MarkAsIntersectingIncrementalChange(array);

                // Adjust the pos or end (or both) of the intersecting array accordingly.
                AdjustIntersectingElement(array, changeStart, changeRangeOldEnd, changeRangeNewEnd, delta);
                foreach (var node in array)
                {
                    VisitNode(AsNode(node));
                }

                return null;
            }

            // Otherwise, the array is entirely before the change range. No need to do anything with it.
            Debug.Assert(fullEnd < changeStart);
            return null;
        }
    }

    // ported from: src/compiler/parser.ts:10200-10221 — checkNodePositions
    /// <summary>The aggressive-mode invariant that a node's children are ordered inside it.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>visitNode</c> returns nothing, and it is passed both to <c>forEachChild</c> and called
    /// directly for the JSDoc children. The port keeps one local function and gives it the result type the walker
    /// wants; the JSDoc calls simply discard it.
    /// </remarks>
    private static void CheckNodePositions(Node node, bool aggressiveChecks)
    {
        if (aggressiveChecks)
        {
            var pos = node.Pos;
            ForEachChildTable.ForEachChild(node, VisitNode);
            if (GetJSDocNodes(node) is { } jsDocNodes)
            {
                foreach (var jsDocComment in jsDocNodes)
                {
                    VisitNode(jsDocComment);
                }
            }

            Debug.Assert(pos <= node.End);

            bool? VisitNode(Node child)
            {
                Debug.Assert(child.Pos >= pos);
                pos = child.End;
                return null;
            }
        }
    }

    // ported from: src/compiler/parser.ts:10304-10337 — extendToAffectedRange
    /// <summary>
    /// Widens the change range to the left so the reparsed region starts at least one token before the edit.
    /// </summary>
    /// <remarks>
    /// Consider the following code:
    /// <code>void foo() { /; }</code>
    /// If the text changes with an insertion of <c>/</c> just before the semicolon then we end up with
    /// <code>void foo() { //; }</code>.
    /// If we were to just use the changeRange as is, then we would not rescan the <c>{</c> token (as it does not
    /// intersect the actual original change range). Because an edit may change the token touching it, we actually need
    /// to look back *at least* one token so that the prior token sees that change.
    /// </remarks>
    private static TextChangeRange ExtendToAffectedRange(SourceFile sourceFile, ITextChangeRange changeRange)
    {
        const int maxLookahead = 1;

        var start = changeRange.Span!.Start;

        // the first iteration aligns us with the change start. subsequent iterations move us to the left by maxLookahead
        // tokens. We only need to do this as long as we're not at the start of the tree.
        for (var i = 0; start > 0 && i <= maxLookahead; i++)
        {
            var nearestNode = FindNearestNodeStartingBeforeOrAtPosition(sourceFile, start);
            Debug.Assert(nearestNode.Pos <= start);
            var position = nearestNode.Pos;

            start = Math.Max(0, position - 1);
        }

        var finalSpan = Core.CreateTextSpanFromBounds(start, Core.TextSpanEnd(changeRange.Span!));
        var finalLength = changeRange.NewLength + (changeRange.Span!.Start - start);

        return Core.CreateTextChangeRange(finalSpan, finalLength);
    }

    // ported from: src/compiler/parser.ts:10339-10406 — findNearestNodeStartingBeforeOrAtPosition
    /// <summary>The deepest node that starts at or before a position, used to widen the change range to a token boundary.</summary>
    private static Node FindNearestNodeStartingBeforeOrAtPosition(SourceFile sourceFile, int position)
    {
        var bestResult = (Node)sourceFile;
        Node? lastNodeEntirelyBeforePosition = null;

        ForEachChildTable.ForEachChild(sourceFile, Visit);

        if (lastNodeEntirelyBeforePosition is not null)
        {
            var lastChildOfLastEntireNodeBeforePosition = GetLastDescendant(lastNodeEntirelyBeforePosition);
            if (lastChildOfLastEntireNodeBeforePosition.Pos > bestResult.Pos)
            {
                bestResult = lastChildOfLastEntireNodeBeforePosition;
            }
        }

        return bestResult;

        bool? Visit(Node child)
        {
            if (NodeIsMissing(child))
            {
                // Missing nodes are effectively invisible to us. We never even consider them when trying to find the
                // nearest node before us.
                return null;
            }

            // If the child intersects this position, then this node is currently the nearest node that starts before
            // the position.
            if (child.Pos <= position)
            {
                if (child.Pos >= bestResult.Pos)
                {
                    // This node starts before the position, and is closer to the position than the previous best node
                    // we found. It is now the new best node.
                    bestResult = child;
                }

                // Now, the node may overlap the position, or it may end entirely before the position. If it overlaps
                // with the position, then either it, or one of its children must be the nearest node before the
                // position. So we can just recurse into this child to see if we can find something better.
                if (position < child.End)
                {
                    // The nearest node is either this child, or one of the children inside of it. We've already marked
                    // this child as the best so far. Recurse in case one of the children is better.
                    ForEachChildTable.ForEachChild(child, Visit);

                    // Once we look at the children of this node, then there's no need to continue any further.
                    return true;
                }
                else
                {
                    Debug.Assert(child.End <= position);
                    // The child ends entirely before this position. Say you have the following (where $ is the
                    // position)
                    //
                    //      <complex expr 1> ? <complex expr 2> $ : <...> <...>
                    //
                    // We would want to find the nearest preceding node in "complex expr 2". To support that, we keep
                    // track of this node, and once we're done searching for a best node, we recurse down this node to
                    // see if we can find a good result in it.
                    //
                    // This approach allows us to quickly skip over nodes that are entirely before the position, while
                    // still allowing us to find any nodes in the last one that might be what we want.
                    lastNodeEntirelyBeforePosition = child;
                }
            }
            else
            {
                Debug.Assert(child.Pos > position);
                // We're now at a node that is entirely past the position we're searching for. This node (and all
                // following nodes) could never contribute to the result, so just skip them by returning 'true' here.
                return true;
            }

            return null;
        }

        static Node GetLastDescendant(Node node)
        {
            while (true)
            {
                var lastChild = GetLastChild(node);
                if (lastChild is not null)
                {
                    node = lastChild;
                }
                else
                {
                    return node;
                }
            }
        }
    }

    // ported from: src/compiler/utilities.ts:8295-8309 — getLastChild
    /// <summary>The last present child of a node, or null when it has none.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>utilities.ts</c> owns this function and arrives with M5; incremental parsing is its only caller
    /// today, so the port keeps it here as a private helper and M5 can move it out (or forward to it) then.
    /// </remarks>
    private static Node? GetLastChild(Node node)
    {
        Node? lastChild = null;
        ForEachChildTable.ForEachChild(node, VisitNode, VisitArray);
        return lastChild;

        bool? VisitNode(Node child)
        {
            if (NodeIsPresent(child)) lastChild = child;
            return null;
        }

        bool? VisitArray(INodeArray<INode> children)
        {
            // As an optimization, jump straight to the end of the list.
            for (var i = children.Count - 1; i >= 0; i--)
            {
                var child = AsNode(children[i]);
                if (NodeIsPresent(child))
                {
                    lastChild = child;
                    break;
                }
            }

            return null;
        }
    }

    // ported from: src/compiler/utilities.ts:1079-1086 — nodeIsMissing
    /// <summary>
    /// Whether a node was inserted by error recovery rather than parsed from text. A node with no text of its own
    /// (its span is empty) is missing, except for the end-of-file token, which legitimately occupies no text.
    /// </summary>
    private static bool NodeIsMissing(Node? node)
        => node is null || (node.Pos == node.End && node.Pos >= 0 && node.Kind != SyntaxKind.EndOfFileToken);

    // ported from: src/compiler/utilities.ts:1088-1090 — nodeIsPresent
    /// <summary>The complement of <see cref="NodeIsMissing"/>.</summary>
    private static bool NodeIsPresent(Node? node) => !NodeIsMissing(node);

    // ported from: src/compiler/utilities.ts:10664-10666 — setTextRangePosEnd
    /// <summary>Bypasses immutability and sets both ends of a range.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>ReadonlyTextRange</c> and the body writes through it anyway. The port's writable
    /// view is <see cref="ITextRange"/>, which both <see cref="Node"/> and <c>NodeArray&lt;T&gt;</c> implement, but the
    /// <see cref="INodeArray{T}"/> view <c>ForEachChild</c> hands out declares only the read side, so the array overload
    /// casts to the writable view. That cast is total here: every array in a parsed tree is a
    /// <c>NodeArray&lt;T&gt;</c>, which implements <see cref="ITextRange"/> regardless of its element type.
    /// </remarks>
    private static void SetTextRangePosEnd(ITextRange range, int pos, int end)
    {
        range.Pos = pos;
        range.End = end;
    }

    /// <summary>TS: <c>setTextRangePosEnd</c> over the <c>NodeArray</c> arm of the union.</summary>
    private static void SetTextRangePosEnd(INodeArray<INode> range, int pos, int end) => SetTextRangePosEnd((ITextRange)range, pos, end);

    /// <summary>The JSDoc nodes that directly precede a node, or null when it has none.</summary>
    /// <remarks>
    /// PORT-NOTE: TS asks <c>hasJSDocNodes(node)</c> — <c>canHaveJSDoc</c> plus "the array is non-empty" — and then
    /// iterates <c>node.jsDoc!</c>. The port answers both questions with one nullable view: the
    /// <see cref="IJSDocContainer"/> test is <c>canHaveJSDoc</c>, and an empty array iterates zero times, so the length
    /// test drops out. The view is a list rather than <see cref="IJSDocArray"/> because the generated
    /// <c>IJSDocArray</c> mirrors only the <c>jsDocCache</c> member of TS's <c>JSDocArray</c> — the
    /// <c>extends Array&lt;JSDoc&gt;</c> half has no C# interface to declare — so the slot is read through the list
    /// view of the concrete <c>JSDocArray : NodeArray&lt;JSDoc&gt;</c>.
    /// </remarks>
    private static IReadOnlyList<JSDoc>? GetJSDocNodes(Node node)
        => node is IJSDocContainer { JsDoc: IReadOnlyList<JSDoc> jsDoc } ? jsDoc : null;

    /// <summary>An element of a <c>ForEachChild</c> array as the node class the walk needs.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>NodeArray&lt;Node&gt;</c> yields <c>Node</c>; the port's callback hands over
    /// <c>INodeArray&lt;INode&gt;</c> because a node interface (say <c>IStatement</c>) does not derive from the
    /// <see cref="Node"/> class. Every element of a parsed tree is nevertheless a <see cref="Node"/> — the interfaces
    /// exist for capability tests — so the cast is total, and it is spelled here once instead of at every walk site.
    /// </remarks>
    private static Node AsNode(INode node) => (Node)node;

    // ported from: src/compiler/parser.ts:10408-10433 — checkChangeRange
    /// <summary>The aggressive-mode invariant that <paramref name="textChangeRange"/> really describes the edit.</summary>
    private static void CheckChangeRange(SourceFile sourceFile, string newText, ITextChangeRange textChangeRange, bool aggressiveChecks)
    {
        var oldText = sourceFile.Text!;
        if (textChangeRange is not null)
        {
            Debug.Assert(oldText.Length - textChangeRange.Span!.Length + textChangeRange.NewLength == newText.Length);

            if (aggressiveChecks || Debug.ShouldAssert(AssertionLevel.VeryAggressive))
            {
                var oldTextPrefix = oldText.Substring(0, textChangeRange.Span!.Start);
                var newTextPrefix = newText.Substring(0, textChangeRange.Span!.Start);
                Debug.Assert(oldTextPrefix == newTextPrefix);

                var oldTextSuffix = oldText.Substring(Core.TextSpanEnd(textChangeRange.Span!));
                var newTextSuffix = newText.Substring(Core.TextSpanEnd(Core.TextChangeRangeNewSpan(textChangeRange)));
                Debug.Assert(oldTextSuffix == newTextSuffix);
            }
        }
    }

    // ported from: src/compiler/parser.ts:10440-10444 — interface SyntaxCursor
    /// <summary>
    /// The view the parser has of the previous tree while it re-parses: "give me the node that used to start here".
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS declares <c>SyntaxCursor</c> inside the <c>IncrementalParser</c> namespace; the port nests it in
    /// this class (the same shape <c>TypedEnum.EndOfFileToken</c> and <c>SysWatch.System</c> got).
    /// </remarks>
    public interface ISyntaxCursor
    {
        // ported from: src/compiler/parser.ts:10443 — `currentNode(position: number): Node`
        /// <summary>
        /// The node that used to start at <paramref name="position"/>, or null when nothing reusable starts there.
        /// The parser asks for the same position twice in a row (once to test a list element, once to consume it), and
        /// the second ask must not repeat the search.
        /// </summary>
        Node? CurrentNode(int position);
    }

    // ported from: src/compiler/parser.ts:10448-10540 — createSyntaxCursor
    /// <summary>Creates the stateful cursor over the previous tree's statements.</summary>
    /// <remarks>
    /// PORT-NOTE: TS returns an object literal capturing two caches; the port's cursor is
    /// <see cref="SyntaxCursor"/>, a class holding the same two caches.
    /// </remarks>
    public static ISyntaxCursor CreateSyntaxCursor(SourceFile sourceFile) => new SyntaxCursor(sourceFile);

    /// <summary>
    /// Finds nodes in the source file at a certain position in an efficient manner, taking advantage of the calling
    /// pattern the parser is known to make.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS's cached state is <c>NodeArray&lt;Node&gt; | undefined</c> for <c>currentArray</c> plus
    /// <c>Node | undefined</c> for <c>current</c>. The port types the array as <c>IReadOnlyList&lt;INode&gt;</c>: the
    /// two things it is ever asked for are <c>length</c> and an element, an interface of a node chain does not derive
    /// from the <see cref="Node"/> class, and the top-level list is <c>NodeArray&lt;IStatement&gt;</c> — so
    /// <c>IReadOnlyList&lt;INode&gt;</c> is the one view both the statements array and the arrays
    /// <c>ForEachChild</c> hands out convert to. Elements come back out through <see cref="AsNode"/>.
    /// <para>
    /// PORT-NOTE: TS's <c>InvalidPosition.Value</c> sentinel is <see cref="InvalidPosition"/> here, and the two caches
    /// are fields instead of closure variables; nothing else about the cursor changed.
    /// </para>
    /// </remarks>
    private sealed class SyntaxCursor : ISyntaxCursor
    {
        private readonly SourceFile _sourceFile;

        /// <summary>The list <see cref="_current"/> was taken from; null while no list has been found for the position.</summary>
        private IReadOnlyList<INode>? _currentArray;

        private int _currentArrayIndex;

        private Node? _current;

        /// <summary>The position of the previous query, so the very common "same position twice" costs nothing.</summary>
        private int _lastQueriedPosition = InvalidPosition;

        public SyntaxCursor(SourceFile sourceFile)
        {
            _sourceFile = sourceFile;

            var statements = sourceFile.Statements!;
            _currentArray = statements;
            _currentArrayIndex = 0;

            Debug.Assert(_currentArrayIndex < statements.Count);
            _current = AsNode(statements[_currentArrayIndex]);
        }

        // ported from: src/compiler/parser.ts:10455-10493 — currentNode
        public Node? CurrentNode(int position)
        {
            // Only compute the current node if the position is different than the last time we were asked. The parser
            // commonly asks for the node at the same position twice: once to know if it can read an appropriate list
            // element at a certain point, and then to actually read and consume the node.
            if (position != _lastQueriedPosition)
            {
                // Much of the time the parser will need the very next node in the array that we just returned a node
                // from. So just simply check for that case and move forward in the array instead of searching for the
                // node again.
                if (_current is not null && _current.End == position && _currentArrayIndex < (_currentArray!.Count - 1))
                {
                    _currentArrayIndex++;
                    _current = AsNode(_currentArray[_currentArrayIndex]);
                }

                // If we don't have a node, or the node we have isn't in the right position, then try to find a viable
                // node at the position requested.
                if (_current is null || _current.Pos != position)
                {
                    FindHighestListElementThatStartsAtPosition(position);
                }
            }

            // Cache this query so that we don't do any extra work if the parser calls back into us. Note: this is very
            // common as the parser will make pairs of calls like 'isListElement -> parseListElement'. If we were unable
            // to find a node when called with 'isListElement', we don't want to redo the work when parseListElement is
            // called immediately after.
            _lastQueriedPosition = position;

            // Either we don't have a node, or we have a node at the position being asked for.
            Debug.Assert(_current is null || _current.Pos == position);
            return _current;
        }

        /// <summary>
        /// Finds the highest element in the tree we can find that starts at the provided position. The element must be a
        /// direct child of some node list in the tree, so that after we return it, we can easily return its next sibling
        /// in the list.
        /// </summary>
        private void FindHighestListElementThatStartsAtPosition(int position)
        {
            // Clear out any cached state about the last node we found.
            _currentArray = null;
            _currentArrayIndex = InvalidPosition;
            _current = null;

            // Recurse into the source file to find the highest node at this position.
            ForEachChildTable.ForEachChild(_sourceFile, VisitNode, VisitArray);

            bool? VisitNode(Node node)
            {
                if (position >= node.Pos && position < node.End)
                {
                    // Position was within this node. Keep searching deeper to find the node.
                    ForEachChildTable.ForEachChild(node, VisitNode, VisitArray);

                    // don't proceed any further in the search.
                    return true;
                }

                // position wasn't in this node, have to keep searching.
                return null;
            }

            bool? VisitArray(INodeArray<INode> array)
            {
                if (position >= array.Pos && position < array.End)
                {
                    // position was in this array. Search through this array to see if we find a viable element.
                    for (var i = 0; i < array.Count; i++)
                    {
                        var child = AsNode(array[i]);
                        if (child is not null)
                        {
                            if (child.Pos == position)
                            {
                                // Found the right node. We're done.
                                _currentArray = array;
                                _currentArrayIndex = i;
                                _current = child;
                                return true;
                            }
                            else
                            {
                                if (child.Pos < position && position < child.End)
                                {
                                    // Position is somewhere within this child. Search in it and stop searching in this
                                    // array.
                                    ForEachChildTable.ForEachChild(child, VisitNode, VisitArray);
                                    return true;
                                }
                            }
                        }
                    }
                }

                // position wasn't in this array, have to keep searching.
                return null;
            }
        }
    }
}
