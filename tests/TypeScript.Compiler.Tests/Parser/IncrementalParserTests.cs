namespace TypeScript.Compiler.Tests;

/// <summary>
/// Acceptance for <see cref="IncrementalParser"/> (the port of <c>parser.ts:9929-10552</c>) and for the two value types
/// it needs (<see cref="TextSpan"/>, <see cref="TextChangeRange"/>).
/// </summary>
/// <remarks>
/// The expectations of the four <c>UpdateSourceFile</c> tests were taken from the TypeScript 6.0 build itself
/// (<c>lib/typescript.js</c>) running the same edit on the same source: statement spans, reused-instance identities and
/// the carried comment directives are exactly what TS produces (a node's <c>Pos</c> includes the trivia that precedes
/// its first token, which is why the spans below do not always start where the statement's own text does — that is what
/// <see cref="NodeStart"/> compensates for).
/// </remarks>
public class IncrementalParserTests
{
    // -----------------------------------------------------------------------------------------------
    // The syntax cursor (no parser needed — the tree is built by hand)
    // -----------------------------------------------------------------------------------------------

    /// <summary>
    /// The parser asks the cursor for a position, and the cursor answers with the *list element* that starts there.
    /// </summary>
    [Fact]
    public void CreateSyntaxCursor_FindsEachStatementByItsStartPosition()
    {
        var file = CreateHandBuiltFile("abcdef", starts: [0, 2, 4], ends: [2, 4, 6]);
        var cursor = IncrementalParser.CreateSyntaxCursor(file);

        Assert.Same(file.Statements![0], cursor.CurrentNode(0));
        Assert.Same(file.Statements[1], cursor.CurrentNode(2));
        Assert.Same(file.Statements[2], cursor.CurrentNode(4));
    }

    /// <summary>
    /// A cursor walks one statement list: every query answers the element that starts at the position, a repeated query
    /// answers the same node (the parser asks for the same position twice in a row — once to test the element, once to
    /// consume it), and a position nothing starts at answers null.
    /// </summary>
    [Fact]
    public void CreateSyntaxCursor_AnswersSequentialAndRepeatedQueries()
    {
        var file = CreateHandBuiltFile("abcdefghi", starts: [0, 3, 6], ends: [3, 6, 9]);
        var cursor = IncrementalParser.CreateSyntaxCursor(file);

        Assert.Same(file.Statements![0], cursor.CurrentNode(0));

        // 3 is where statement 1 ends and statement 2 starts: the cursor moves to the next element of the list it is
        // already in, which is what the parser hits constantly (the next token starts where the last node ended).
        Assert.Same(file.Statements[1], cursor.CurrentNode(3));
        Assert.Same(file.Statements[2], cursor.CurrentNode(6));

        // The same position twice is answered from the cached current node.
        Assert.Same(file.Statements[2], cursor.CurrentNode(6));

        // Past the end of the list there is no element to reuse...
        Assert.Null(cursor.CurrentNode(9));

        // ...and a miss must not poison the next query.
        Assert.Same(file.Statements[1], cursor.CurrentNode(3));
    }

    /// <summary>
    /// A position that no list element starts at answers null, even when it is inside a statement: the parser may only
    /// reuse nodes it can splice into a list.
    /// </summary>
    [Fact]
    public void CreateSyntaxCursor_ReturnsNullWhenNoListElementStartsAtThePosition()
    {
        var file = CreateHandBuiltFile("abcdef", starts: [0, 2, 4], ends: [2, 4, 6]);
        var cursor = IncrementalParser.CreateSyntaxCursor(file);

        Assert.Null(cursor.CurrentNode(3));
        Assert.Null(cursor.CurrentNode(17));
    }

    /// <summary>The search recurses into a child's own lists, not just the source file's statements.</summary>
    [Fact]
    public void CreateSyntaxCursor_SearchesInsideANestedList()
    {
        var inner = new ExpressionStatement { Expression = new Identifier { EscapedText = "b" } };
        inner.Pos = 3;
        inner.End = 5;

        var innerStatements = new NodeArray<IStatement> { inner };
        innerStatements.Pos = 2;
        innerStatements.End = 8;

        var block = new Block { Statements = innerStatements };
        block.Pos = 0;
        block.End = 10;

        var statements = new NodeArray<IStatement> { block };
        statements.Pos = 0;
        statements.End = 10;

        var file = new SourceFile { FileName = "hand.ts", Text = "abcdefghij", Statements = statements };
        var cursor = IncrementalParser.CreateSyntaxCursor(file);

        Assert.Same(inner, cursor.CurrentNode(3));

        // Nothing *starts* at 6: neither in the inner list nor in the outer one.
        Assert.Null(cursor.CurrentNode(6));
    }

    // -----------------------------------------------------------------------------------------------
    // updateSourceFile
    // -----------------------------------------------------------------------------------------------

    /// <summary>
    /// The first thing <c>updateSourceFile</c> does is answer "nothing changed" with the very same file — no walk, no
    /// parse, no mutation.
    /// </summary>
    [Fact]
    public void UpdateSourceFile_ReturnsTheSameInstanceWhenTheRangeIsUnchanged()
    {
        var text = "const a = 1;\n";
        var file = CreateHandBuiltFile(text, starts: [0], ends: [12]);

        var result = IncrementalParser.UpdateSourceFile(file, text, UnchangedRange(), aggressiveChecks: false);

        Assert.Same(file, result);
    }

    /// <summary>Nothing changed: an empty span in the middle is still an unchanged range.</summary>
    [Fact]
    public void UpdateSourceFile_TreatsAnEmptySpanWithNoNewTextAsUnchanged()
    {
        var text = "const a = 1;\n";
        var file = CreateHandBuiltFile(text, starts: [0], ends: [12]);

        var result = IncrementalParser.UpdateSourceFile(file, text, UnchangedRange(start: 5), aggressiveChecks: true);

        Assert.Same(file, result);
    }

    /// <summary>
    /// Appending a statement to the end of the file: the two statements before the edit come out of the new tree as the
    /// very same objects, and the two after it are where the new text puts them.
    /// </summary>
    [Fact]
    public void UpdateSourceFile_ReusesTheStatementsBeforeAnInsertionAtTheEnd()
    {
        const string text = "const a = 1;\nconst b = 2;\nconst c = 3;\n";
        var file = Parse(text);
        var oldStatements = file.Statements!;
        Assert.Equal(3, oldStatements.Count);

        const string appended = "const d = 4;\n";
        var result = IncrementalParser.UpdateSourceFile(
            file,
            text + appended,
            Range(start: text.Length, length: 0, newLength: appended.Length),
            aggressiveChecks: true);

        Assert.NotSame(file, result);
        Assert.Equal(4, result.Statements!.Count);
        Assert.Equal(text + appended, result.Text);

        // The edit is at the end of the file, so the first two statements were never in the affected range.
        Assert.Same(oldStatements[0], result.Statements[0]);
        Assert.Same(oldStatements[1], result.Statements[1]);

        Assert.Equal("const a = 1;", NodeText(result, StatementAt(result, 0)));
        Assert.Equal("const b = 2;", NodeText(result, StatementAt(result, 1)));
        Assert.Equal("const c = 3;", NodeText(result, StatementAt(result, 2)));
        Assert.Equal("const d = 4;", NodeText(result, StatementAt(result, 3)));

        // The reparsed statement starts where the old one did and the appended one starts at the old end of file.
        Assert.Equal(26, NodeStart(result, StatementAt(result, 2)));
        Assert.Equal(text.Length, NodeStart(result, StatementAt(result, 3)));
    }

    /// <summary>
    /// Deleting a character inside the second statement: the third statement is past the change range, so it is moved by
    /// the (negative) delta and handed back as the same object.
    /// </summary>
    [Fact]
    public void UpdateSourceFile_ReusesAndShiftsTheStatementsAfterADeletion()
    {
        const string text = "const a = 1;\nconst b = 2;\nconst c = 3;\n";
        var file = Parse(text);
        var oldStatements = file.Statements!;
        var thirdStatement = StatementAt(file, 2);
        var oldThirdStatementPos = thirdStatement.Pos;
        var oldThirdStatementEnd = thirdStatement.End;
        var oldThirdStatementStart = NodeStart(file, thirdStatement);

        // Drop the "2" of "const b = 2;".
        var deletedAt = text.IndexOf("const b = 2;", StringComparison.Ordinal) + "const b = ".Length;
        Assert.Equal(23, deletedAt);
        Assert.Equal(25, oldThirdStatementPos);
        Assert.Equal(38, oldThirdStatementEnd);
        Assert.Equal(26, oldThirdStatementStart);

        var newText = text.Remove(deletedAt, 1);

        var result = IncrementalParser.UpdateSourceFile(
            file,
            newText,
            Range(start: deletedAt, length: 1, newLength: 0),
            aggressiveChecks: true);

        Assert.Equal(3, result.Statements!.Count);
        Assert.Equal(newText, result.Text);

        // The first statement is untouched (the second one is reparsed: the look-back widened the change range into it),
        // and the statement after the edit is the same node at its new position.
        Assert.Same(oldStatements[0], result.Statements[0]);
        Assert.Same(thirdStatement, result.Statements[2]);

        Assert.Equal("const a = 1;", NodeText(result, StatementAt(result, 0)));
        Assert.Equal("const b = ;", NodeText(result, StatementAt(result, 1)));
        Assert.Equal("const c = 3;", NodeText(result, StatementAt(result, 2)));

        // One character was removed, so the reused statement moved back by exactly one.
        Assert.Equal(oldThirdStatementPos - 1, thirdStatement.Pos);
        Assert.Equal(oldThirdStatementEnd - 1, thirdStatement.End);
        Assert.Equal(oldThirdStatementStart - 1, NodeStart(result, thirdStatement));
        Assert.Equal(text.Length - 1, result.Text!.Length);
    }

    /// <summary>
    /// Inserting before the third statement: the statement after the insertion point is reparsed at its new position,
    /// the statements before it are reused.
    /// </summary>
    [Fact]
    public void UpdateSourceFile_ReusesTheStatementsBeforeAnInsertionAndRepositionsTheOneAfterIt()
    {
        const string text = "const a = 1;\nconst b = 2;\nconst c = 3;\n";
        var file = Parse(text);
        var oldStatements = file.Statements!;

        var insertAt = text.IndexOf("const c = 3;", StringComparison.Ordinal);
        Assert.Equal(26, insertAt);

        const string inserted = "  ";
        var newText = text.Insert(insertAt, inserted);

        var result = IncrementalParser.UpdateSourceFile(
            file,
            newText,
            Range(start: insertAt, length: 0, newLength: inserted.Length),
            aggressiveChecks: true);

        Assert.Equal(3, result.Statements!.Count);
        Assert.Equal(newText, result.Text);

        // Only the first statement is untouched: the look-back pulls the change range into the second one.
        Assert.Same(oldStatements[0], result.Statements[0]);
        Assert.NotSame(oldStatements[1], result.Statements[1]);
        Assert.NotSame(oldStatements[2], result.Statements[2]);

        Assert.Equal("const a = 1;", NodeText(result, StatementAt(result, 0)));
        Assert.Equal("const b = 2;", NodeText(result, StatementAt(result, 1)));
        Assert.Equal("const c = 3;", NodeText(result, StatementAt(result, 2)));

        // The reparsed statement's own text sits two characters further along; its Pos is still the trivia before it.
        Assert.Equal(insertAt + inserted.Length, NodeStart(result, StatementAt(result, 2)));
        Assert.Equal(25, StatementAt(result, 2).Pos);
        Assert.Equal(40, StatementAt(result, 2).End);
    }

    /// <summary>
    /// <c>updateSourceFile</c> rewrites the old tree in place before the new parse runs: nodes after the change move by
    /// the delta, nodes the change range intersects keep a consistent span, and every moved token still holds exactly
    /// its own text — that last invariant is what <c>shouldCheckNode</c> asserts in aggressive mode, so this test fails
    /// loudly on an off-by-one.
    /// </summary>
    [Fact]
    public void UpdateSourceFile_MovesAndAdjustsTheOldTreeInPlace()
    {
        const string text = "aa;\nbb;\ncc;\n";
        var file = Parse(text);

        var first = ((ExpressionStatement)file.Statements![0]).Expression!;
        var second = ((ExpressionStatement)file.Statements[1]).Expression!;
        var third = ((ExpressionStatement)file.Statements[2]).Expression!;
        Assert.Equal(0, first.Pos);
        Assert.Equal(2, first.End);
        Assert.Equal(3, second.Pos);
        Assert.Equal(6, second.End);
        Assert.Equal(7, third.Pos);
        Assert.Equal(10, third.End);

        // Drop the "c" of "cc;".
        const int deletedAt = 9;
        var newText = text.Remove(deletedAt, 1);

        var result = IncrementalParser.UpdateSourceFile(
            file,
            newText,
            Range(start: deletedAt, length: 1, newLength: 0),
            aggressiveChecks: true);

        // The identifier that was moved to make room is the same object at its new span, holding the text that is left
        // for it; the identifiers before the change kept their spans.
        Assert.Equal(7, third.Pos);
        Assert.Equal(9, third.End);
        Assert.Equal("c", NodeText(result, third));
        Assert.Equal(3, second.Pos);
        Assert.Equal(6, second.End);
        Assert.Equal("bb", NodeText(result, second));
        Assert.Equal(0, first.Pos);
        Assert.Equal(2, first.End);
        Assert.Equal("aa", NodeText(result, first));
    }

    /// <summary>
    /// The directives before the change are carried over as the same objects, the ones inside it are dropped in favour
    /// of whatever the new parse found, and the ones after it are shifted by the delta.
    /// </summary>
    [Fact]
    public void UpdateSourceFile_CarriesCommentDirectivesAcrossTheChange()
    {
        const string text = "aa;\nbb;\ncc;\n";
        var file = Parse(text);

        var kept = new CommentDirective { Range = new TextRange { Pos = 0, End = 1 }, Type = CommentDirectiveType.Ignore };
        var dropped = new CommentDirective { Range = new TextRange { Pos = 4, End = 6 }, Type = CommentDirectiveType.Ignore };
        var after = new CommentDirective { Range = new TextRange { Pos = 11, End = 12 }, Type = CommentDirectiveType.ExpectError };
        file.CommentDirectives = [kept, dropped, after];

        var result = IncrementalParser.UpdateSourceFile(
            file,
            text.Remove(9, 1),
            Range(start: 9, length: 1, newLength: 0),
            aggressiveChecks: true);

        // TS keeps [0, 1) as it is, drops [4, 6) — the look-back widened the change range over it — and moves
        // [11, 12) back by the delta of -1.
        Assert.NotNull(result.CommentDirectives);
        Assert.Equal(2, result.CommentDirectives!.Count);
        Assert.Same(kept, result.CommentDirectives[0]);

        var shifted = Assert.IsType<CommentDirective>(result.CommentDirectives[1]);
        Assert.Equal(10, shifted.Range.Pos);
        Assert.Equal(11, shifted.Range.End);
        Assert.Equal(CommentDirectiveType.ExpectError, shifted.Type);
        Assert.DoesNotContain(result.CommentDirectives, directive => ReferenceEquals(directive, dropped));
    }

    // -----------------------------------------------------------------------------------------------
    // helpers
    // -----------------------------------------------------------------------------------------------

    /// <summary>The n-th statement as a node: the generated array is <c>NodeArray&lt;IStatement&gt;</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>IStatement</c> is a capability interface and does not declare <c>Pos</c>/<c>End</c> (only
    /// <see cref="Node"/> does), so reading a statement's span means going back to the class — which is total, because
    /// every element of a parsed file's statement list is one.
    /// </remarks>
    private static Node StatementAt(SourceFile file, int index) => (Node)file.Statements![index];

    private static SourceFile Parse(string text)
        => Parser.ParseSourceFile(
            fileName: "file.ts",
            sourceText: text,
            languageVersion: ScriptTarget.ES2015,
            syntaxCursor: null,
            setParentNodes: true,
            scriptKind: ScriptKind.TS,
            setExternalModuleIndicator: null,
            jsDocParsingMode: JSDocParsingMode.ParseAll);

    private static TextChangeRange UnchangedRange(int start = 0)
        => new() { Span = new TextSpan { Start = start, Length = 0 }, NewLength = 0 };

    private static TextChangeRange Range(int start, int length, int newLength)
        => new() { Span = new TextSpan { Start = start, Length = length }, NewLength = newLength };

    /// <summary>The node's own text, without the trivia its <c>Pos</c> reaches back over.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers this with <c>node.getStart(sourceFile)</c>; that scanner-backed helper is not ported yet,
    /// so the test skips the leading whitespace itself. Only the whitespace the test sources contain needs handling.
    /// </remarks>
    private static string NodeText(SourceFile file, Node node)
    {
        var start = NodeStart(file, node);
        return file.Text!.Substring(start, node.End - start);
    }

    /// <summary>Where the node's first token starts (TS's <c>getStart</c>).</summary>
    private static int NodeStart(SourceFile file, Node node)
    {
        var text = file.Text!;
        var start = node.Pos;
        while (start < node.End && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        return start;
    }

    /// <summary>
    /// A source file whose statements are plain expression statements laid out over <paramref name="text"/>. Every
    /// statement carries its own position because that is the only thing the cursor looks at.
    /// </summary>
    private static SourceFile CreateHandBuiltFile(string text, int[] starts, int[] ends)
    {
        var statements = new NodeArray<IStatement>();
        for (var i = 0; i < starts.Length; i++)
        {
            var statement = new ExpressionStatement { Expression = new Identifier { EscapedText = "x" } };
            statement.Pos = starts[i];
            statement.End = ends[i];
            statements.Add(statement);
        }

        statements.Pos = 0;
        statements.End = text.Length;

        return new SourceFile { FileName = "hand.ts", Text = text, Statements = statements };
    }
}
