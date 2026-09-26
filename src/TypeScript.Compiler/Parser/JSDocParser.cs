namespace TypeScript.Compiler;

// ported from: src/compiler/parser.ts:8790-9945 — namespace JSDocParser
/// <summary>
/// The JSDoc half of <see cref="Parser"/>: <c>/** … */</c> comments, the tags in them, their type expressions and
/// their signature types.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS models this as a namespace of module-level functions that close over the parser's module-level state
/// (<c>sourceText</c>, <c>scanner</c>, <c>parseDiagnostics</c>, <c>jsDocDiagnostics</c>, <c>currentToken</c>,
/// <c>identifierCount</c>, <c>hasDeprecatedTag</c>, the context flags). C# has no ambient state and no friend
/// namespaces, so the same functions are the static members of a class nested in <see cref="Parser"/> — which is what
/// lets them reach those private fields, and the private constructor the two isolated entry points need (a
/// namespace-level class could reach neither, and would force the whole machinery to be re-exposed as internal
/// members). The parse they belong to is their first parameter, which is what a TS closure would have captured;
/// <c>Parser.cs</c>/<c>Parser.Entry.cs</c> pass it at the three call sites.
/// <para>
/// PORT-NOTE: TS nests every tag and token helper inside <c>parseJSDocCommentWorker</c> and lets them share that
/// closure's locals (<c>tags</c>, <c>tagsPos</c>, <c>tagsEnd</c>, <c>linkEnd</c>, <c>commentsPos</c>, <c>comments</c>,
/// <c>parts</c>). The port keeps that shape as the private <see cref="CommentParser"/> class: the shared locals are its
/// fields and the nested functions are its methods. The two helpers that close over a *different* set of locals —
/// <c>doJSDocScan</c>'s <c>state</c>/<c>margin</c>/<c>indent</c> and <c>parseTagComments</c>'s own
/// <c>comments</c>/<c>parts</c>/<c>linkEnd</c> — keep them as method locals, exactly where TS shadows them.
/// </para>
/// <para>
/// PORT-NOTE: TS's <c>JSDocComment</c> is the union <c>JSDocText | JSDocLink | JSDocLinkCode | JSDocLinkPlain</c> and
/// its <c>Comment</c> slots are <c>string | NodeArray&lt;JSDocComment&gt;</c>; neither has a C# member, so a comment
/// list is a <c>NodeArray&lt;Node&gt;</c> and a "comment" is an <c>object?</c> that the use site narrows — the same
/// treatment <c>NodeFactory.CreateJSDocComment</c> already documents.
/// </para>
/// </remarks>
internal sealed partial class Parser
{
    internal static class JSDocParser
    {
        //
        // Entry points
        //

        /// <summary>ported from: src/compiler/parser.ts:8791-8807 — <c>parseJSDocTypeExpressionForTests</c>.</summary>
        /// <remarks>
        /// PORT-NOTE: TS calls <c>initializeState</c> and the module-level <c>scanner.setText</c>/<c>createSourceFile</c>/
        /// <c>attachFileToDiagnostics</c>/<c>clearState</c>; the port has one parse per <see cref="Parser"/> instance, so
        /// it builds one and calls the same members on it. TS's return type spells the diagnostics <c>Diagnostic[]</c>
        /// while <c>attachFileToDiagnostics</c> answers <c>DiagnosticWithLocation[]</c>; see
        /// <see cref="ToDiagnosticList"/> for the port's step between the two.
        /// </remarks>
        internal static (JSDocTypeExpression JsDocTypeExpression, List<Diagnostic> Diagnostics)? ParseJSDocTypeExpressionForTests(
            string content,
            int? start = null,
            int? length = null)
        {
            var parser = new Parser("file.js", content, ScriptTarget.Latest, /*syntaxCursor*/ null, ScriptKind.JS, JSDocParsingMode.ParseAll);
            parser._scanner.SetText(content, start, length);
            parser._currentToken = parser._scanner.Scan();
            var jsDocTypeExpression = ParseJSDocTypeExpression(parser);

            var sourceFile = parser.CreateSourceFile(
                "file.js",
                ScriptTarget.Latest,
                ScriptKind.JS,
                /*isDeclarationFile*/ false,
                [],
                (Token)parser._factory.CreateToken(SyntaxKind.EndOfFileToken),
                NodeFlags.None,
                FileMetadataNoop);
            var diagnostics = ToDiagnosticList(Utilities.AttachFileToDiagnostics(parser._parseDiagnostics, sourceFile));
            if (parser._jsDocDiagnostics is not null)
            {
                sourceFile.JsDocDiagnostics = Utilities.AttachFileToDiagnostics(parser._jsDocDiagnostics, sourceFile);
            }

            parser.ClearState();

            return jsDocTypeExpression is not null ? (jsDocTypeExpression, diagnostics) : null;
        }

        /// <summary>ported from: src/compiler/parser.ts:8809-8820 — <c>parseJSDocTypeExpression</c>.</summary>
        /// <remarks>
        /// PORT-NOTE: TS writes <c>(mayOmitBraces ? parseOptional : parseExpected)(SyntaxKind.OpenBraceToken)</c>; both
        /// answer a <c>bool</c>, so the port keeps the conditional as a call of each.
        /// </remarks>
        internal static JSDocTypeExpression ParseJSDocTypeExpression(Parser parser, bool mayOmitBraces = false)
        {
            var pos = parser.GetNodePos();
            var hasBrace = mayOmitBraces
                ? parser.ParseOptional(SyntaxKind.OpenBraceToken)
                : parser.ParseExpected(SyntaxKind.OpenBraceToken);
            var type = parser.DoInsideOfContext(NodeFlags.JSDoc, parser.ParseJSDocType);
            if (!mayOmitBraces || hasBrace)
            {
                parser.ParseExpectedJSDoc(SyntaxKind.CloseBraceToken);
            }

            var result = parser._factory.CreateJSDocTypeExpression(type);
            FixupParentReferences(result);
            return parser.FinishNode(result, pos);
        }

        /// <summary>ported from: src/compiler/parser.ts:8822-8839 — <c>parseJSDocNameReference</c>.</summary>
        /// <remarks>
        /// PORT-NOTE: TS answers the <c>EntityName | JSDocMemberName</c> union; the port carries it as the wide
        /// <see cref="Node"/> the factory's <c>createJSDocNameReference</c> slot takes.
        /// </remarks>
        internal static JSDocNameReference ParseJSDocNameReference(Parser parser)
        {
            var pos = parser.GetNodePos();
            var hasBrace = parser.ParseOptional(SyntaxKind.OpenBraceToken);
            var p2 = parser.GetNodePos();
            Node entityName = parser.ParseEntityName(/*allowReservedWords*/ false);
            while (parser.Token() == SyntaxKind.PrivateIdentifier)
            {
                parser.ReScanHashToken(); // rescan #id as # id
                parser.NextTokenJSDoc(); // then skip the #
                entityName = parser.FinishNode(parser._factory.CreateJSDocMemberName(entityName, parser.ParseIdentifier()), p2);
            }

            if (hasBrace)
            {
                parser.ParseExpectedJSDoc(SyntaxKind.CloseBraceToken);
            }

            var result = parser._factory.CreateJSDocNameReference(entityName);
            FixupParentReferences(result);
            return parser.FinishNode(result, pos);
        }

        /// <summary>ported from: src/compiler/parser.ts:8841-8850 — <c>parseIsolatedJSDocComment</c>.</summary>
        /// <remarks>
        /// PORT-NOTE: TS builds the file the diagnostics are attached to as the object literal
        /// <c>{ languageVariant, text }</c> cast to <c>SourceFile</c>; the port constructs a real
        /// <see cref="SourceFile"/> with the same two fields, which is all <c>attachFileToDiagnostics</c> reads
        /// (its <c>fileName</c> assertion passes because this parse's file name is the empty string).
        /// </remarks>
        internal static JsDocWithDiagnostics? ParseIsolatedJSDocComment(string content, int? start = null, int? length = null)
        {
            var parser = new Parser("", content, ScriptTarget.Latest, /*syntaxCursor*/ null, ScriptKind.JS, JSDocParsingMode.ParseAll);
            var jsDoc = parser.DoInsideOfContext(NodeFlags.JSDoc, () => ParseJSDocCommentWorker(parser, start ?? 0, length));

            var sourceFile = new SourceFile { LanguageVariant = LanguageVariant.Standard, Text = content };
            var diagnostics = ToDiagnosticList(Utilities.AttachFileToDiagnostics(parser._parseDiagnostics, sourceFile));
            parser.ClearState();

            return jsDoc is not null ? new JsDocWithDiagnostics(jsDoc, diagnostics) : null;
        }

        /// <summary>ported from: src/compiler/parser.ts:8852-8868 — <c>parseJSDocComment</c>.</summary>
        /// <remarks>
        /// This is the one entry point the parse itself calls (from <c>withJSDoc</c>): it saves the token and the error
        /// latch, parses the comment in the [JSDoc] context, hangs the comment on its parent, and — for a JavaScript
        /// file only — moves the diagnostics the comment produced into <c>jsDocDiagnostics</c>, then restores the saved
        /// state so the comment cannot affect the rest of the parse.
        /// </remarks>
        internal static JSDoc? ParseJSDocComment(Parser parser, Node node, int start, int length)
        {
            var saveToken = parser._currentToken;
            var saveParseDiagnosticsLength = parser._parseDiagnostics.Count;
            var saveParseErrorBeforeNextFinishedNode = parser._parseErrorBeforeNextFinishedNode;

            var comment = parser.DoInsideOfContext(NodeFlags.JSDoc, () => ParseJSDocCommentWorker(parser, start, length));
            Utilities.SetParent(comment, node);

            if ((parser._contextFlags & NodeFlags.JavaScriptFile) != 0)
            {
                parser._jsDocDiagnostics ??= [];
                Core.AddRange(parser._jsDocDiagnostics, parser._parseDiagnostics, saveParseDiagnosticsLength);
            }

            parser._currentToken = saveToken;
            parser._parseDiagnostics.RemoveRange(saveParseDiagnosticsLength, parser._parseDiagnostics.Count - saveParseDiagnosticsLength);
            parser._parseErrorBeforeNextFinishedNode = saveParseErrorBeforeNextFinishedNode;
            return comment;
        }

        /// <summary>ported from: src/compiler/parser.ts:8885-9091 — <c>parseJSDocCommentWorker</c>.</summary>
        /// <remarks>
        /// PORT-NOTE: TS's <c>length</c> has a default and is then rebound to <c>end - start</c>, and its three
        /// <c>Debug.assert</c>s guard the range; the port computes the same <c>end</c>/<c>length</c> where TS does and
        /// keeps the assertions.
        /// </remarks>
        private static JSDoc? ParseJSDocCommentWorker(Parser parser, int start, int? length)
            => new CommentParser(parser, start, length).Run();

        /// <summary>
        /// TS's <c>attachFileToDiagnostics</c> answer, viewed as the <c>Diagnostic[]</c> the isolated entry points declare.
        /// </summary>
        /// <remarks>
        /// PORT-NOTE: <c>Utilities.AttachFileToDiagnostics</c> answers <c>List&lt;IDiagnosticWithLocation&gt;</c> — the
        /// slot a parsed file wants — while TS's <c>parseIsolatedJSDocComment</c>/<c>parseJSDocTypeExpressionForTests</c>
        /// spell their answer <c>Diagnostic[]</c>, and <see cref="JsDocWithDiagnostics"/> keeps TS's <c>Diagnostic</c>
        /// list. The element type is the concrete <see cref="Diagnostic"/> at run time (that is what the one helper
        /// builds), so the copy is a widening in the other direction and cannot fail.
        /// </remarks>
        private static List<Diagnostic> ToDiagnosticList(List<IDiagnosticWithLocation> diagnostics)
        {
            var result = new List<Diagnostic>(diagnostics.Count);
            foreach (var diagnostic in diagnostics)
            {
                result.Add((Diagnostic)diagnostic);
            }

            return result;
        }

        //
        // The JSDoc tokenizer's two small enums
        //

        // ported from: src/compiler/parser.ts:8874-8879 — const enum JSDocState
        /// <summary>Where the JSDoc scanner is in a comment, for the tests that decide whether a tag may start.</summary>
        private enum JSDocState
        {
            BeginningOfLine,
            SawAsterisk,
            SavingComments,

            /// <summary>Only used when parsing tag comments.</summary>
            SavingBackticks,
        }

        // ported from: src/compiler/parser.ts:8881-8885 — const enum PropertyLikeParse
        /// <summary>
        /// What a <c>@param</c>-shaped tag may be: a property, a parameter, or a parameter of a <c>@callback</c>.
        /// </summary>
        /// <remarks>
        /// PORT-NOTE: TS declares the members as <c>1 &lt;&lt; n</c> and tests them with <c>&amp;</c> whenever a tag can be
        /// more than one of them (<c>tryParseChildTag</c> composes
        /// <c>PropertyLikeParse.Parameter | PropertyLikeParse.CallbackParameter</c>), so the port marks the enum
        /// <see cref="System.FlagsAttribute"/>.
        /// </remarks>
        [Flags]
        private enum PropertyLikeParse
        {
            Property = 1 << 0,
            Parameter = 1 << 1,
            CallbackParameter = 1 << 2,
        }

        //
        // The comment parse: TS's parseJSDocCommentWorker closure
        //

        /// <summary>
        /// One JSDoc comment's parse (TS: the locals and the nested functions of <c>parseJSDocCommentWorker</c>).
        /// </summary>
        /// <remarks>
        /// PORT-NOTE: the fields are the closure's variables, exactly as TS declares them: <c>tags</c>/<c>tagsPos</c>/
        /// <c>tagsEnd</c> accumulate the tag list, <c>commentsPos</c>/<c>comments</c>/<c>parts</c> accumulate the text
        /// around them, and <c>linkEnd</c> remembers where the last <c>{@link …}</c> ended. <c>doJSDocScan</c> and
        /// <c>parseTagComments</c> shadow three of them (<c>indent</c>/<c>margin</c>/<c>state</c> and the second set of
        /// <c>comments</c>/<c>parts</c>/<c>linkEnd</c>) — TS shadows them by declaring locals, and the port does too.
        /// </remarks>
        private sealed class CommentParser
        {
            private readonly Parser _parser;
            private readonly string _content;
            private readonly int _start;
            private readonly int _end;

            // TS: `let tags` / `let tagsPos` / `let tagsEnd` / `let linkEnd` / `let commentsPos` / `let comments = []`
            // / `const parts = []` of parseJSDocCommentWorker.
            private List<JSDocTag>? _tags;
            private int _tagsPos;
            private int _tagsEnd;
            private int? _linkEnd;
            private int? _commentsPos;
            private List<string> _comments = [];
            private readonly List<Node> _parts = [];

            public CommentParser(Parser parser, int start, int? length)
            {
                _parser = parser;
                _content = parser._sourceText;
                _end = length is null ? _content.Length : start + length.Value;
                _start = start;
            }

            /// <summary>ported from: src/compiler/parser.ts:8886-8901 — the head of <c>parseJSDocCommentWorker</c>.</summary>
            /// <remarks>
            /// The comment is scanned out of <c>/**</c> to <c>*/</c> by the scanner's range-based scan, which restores
            /// the scanner's state when the callback returns; the [JSDocComment] parsing context is set for the duration
            /// so that <c>isListElement</c> knows a tag list is being read.
            /// </remarks>
            public JSDoc? Run()
            {
                var length = _end - _start;

                Debug.Assert(_start >= 0);
                Debug.Assert(_start <= _end);
                Debug.Assert(_end <= _content.Length);

                // Check for /** (JSDoc opening part)
                if (!IsJSDocLikeText(_content, _start))
                {
                    return null;
                }

                var saveParsingContext = _parser._parsingContexts;
                _parser._parsingContexts |= 1 << (int)ParsingContext.JSDocComment;

                // + 3 for leading /**, - 5 in total for /** */
                var result = _parser._scanner.ScanRange(_start + 3, length - 5, DoJSDocScan);
                _parser._parsingContexts = saveParsingContext;
                return result;
            }

            /// <summary>ported from: src/compiler/parser.ts:8902-9045 — <c>doJSDocScan</c>.</summary>
            /// <remarks>
            /// PORT-NOTE: TS breaks its <c>loop</c> label on <c>EndOfFileToken</c> and falls through from
            /// <c>OpenBraceToken</c> to <c>default</c> when the <c>{@link</c> probe fails; C# has no loop labels, so the
            /// port uses <c>goto</c> to a label after the loop and <c>goto default</c> for the fall-through — the same
            /// control flow with the same effect.
            /// </remarks>
            private JSDoc? DoJSDocScan()
            {
                // Initially we can parse out a tag.  We also have seen a starting asterisk.
                // This is so that /** * @type */ doesn't parse.
                var state = JSDocState.SawAsterisk;
                int? margin = null;
                // + 4 for leading '/** '
                // + 1 because the last index of \n is always one index before the first character in the line and coincidentally, if there is no \n before start, it is -1, which is also one index before the first character
                var indent = _start - (LastNewlineBefore(_start) + 1) + 4;

                void PushComment(string text)
                {
                    margin ??= indent;
                    _comments.Add(text);
                    indent += text.Length;
                }

                _parser.NextTokenJSDoc();
                while (ParseOptionalJsdoc(SyntaxKind.WhitespaceTrivia)) { }
                if (ParseOptionalJsdoc(SyntaxKind.NewLineTrivia))
                {
                    state = JSDocState.BeginningOfLine;
                    indent = 0;
                }

                while (true)
                {
                    switch (_parser.Token())
                    {
                        case SyntaxKind.AtToken:
                            RemoveTrailingWhitespace(_comments);
                            _commentsPos ??= _parser.GetNodePos();
                            AddTag(ParseTag(indent));
                            // NOTE: According to usejsdoc.org, a tag goes to end of line, except the last tag.
                            // Real-world comments may break this rule, so "BeginningOfLine" will not be a real line beginning
                            // for malformed examples like `/** @param {string} x @returns {number} the length */`
                            state = JSDocState.BeginningOfLine;
                            margin = null;
                            break;
                        case SyntaxKind.NewLineTrivia:
                            _comments.Add(_parser._scanner.GetTokenText());
                            state = JSDocState.BeginningOfLine;
                            indent = 0;
                            break;
                        case SyntaxKind.AsteriskToken:
                            {
                                var asterisk = _parser._scanner.GetTokenText();
                                if (state == JSDocState.SawAsterisk)
                                {
                                    // If we've already seen an asterisk, then we can no longer parse a tag on this line
                                    state = JSDocState.SavingComments;
                                    PushComment(asterisk);
                                }
                                else
                                {
                                    Debug.Assert(state == JSDocState.BeginningOfLine);
                                    // Ignore the first asterisk on a line
                                    state = JSDocState.SawAsterisk;
                                    indent += asterisk.Length;
                                }

                                break;
                            }
                        case SyntaxKind.WhitespaceTrivia:
                            {
                                Debug.Assert(state != JSDocState.SavingComments, "whitespace shouldn't come from the scanner while saving top-level comment text");
                                // only collect whitespace if we're already saving comments or have just crossed the comment indent margin
                                var whitespace = _parser._scanner.GetTokenText();
                                if (margin is not null && indent + whitespace.Length > margin.Value)
                                {
                                    // PORT-NOTE: TS writes `whitespace.substring(margin - indent)`, and JS's
                                    // String.prototype.substring clamps a negative start to 0. The guard above only
                                    // bounds the value from above, so `margin - indent` can be negative (a comment whose
                                    // first line is indented less than the margin) — a C# range would throw. Found by the
                                    // P1a differential run on tests/cases/compiler/commentsFormatting.ts.
                                    _comments.Add(whitespace[Math.Max(0, margin.Value - indent)..]);
                                }

                                indent += whitespace.Length;
                                break;
                            }
                        case SyntaxKind.EndOfFileToken:
                            goto loopDone;
                        case SyntaxKind.JSDocCommentTextToken:
                            state = JSDocState.SavingComments;
                            PushComment(_parser._scanner.GetTokenValue() ?? "");
                            break;
                        case SyntaxKind.OpenBraceToken:
                            {
                                state = JSDocState.SavingComments;
                                var commentEnd = _parser._scanner.GetTokenFullStart();
                                var linkStart = _parser._scanner.GetTokenEnd() - 1;
                                var link = ParseJSDocLink(linkStart);
                                if (link is not null)
                                {
                                    if (_linkEnd is null)
                                    {
                                        RemoveLeadingNewlines(_comments);
                                    }

                                    _parts.Add(_parser.FinishNode(_parser._factory.CreateJSDocText(string.Concat(_comments)), _linkEnd ?? _start, commentEnd));
                                    _parts.Add(link);
                                    _comments = [];
                                    _linkEnd = _parser._scanner.GetTokenEnd();
                                    break;
                                }

                                // fallthrough if it's not a {@link sequence
                                goto default;
                            }
                        default:
                            // Anything else is doc comment text. We just save it. Because it
                            // wasn't a tag, we can no longer parse a tag on this line until we hit the next
                            // line break.
                            state = JSDocState.SavingComments;
                            PushComment(_parser._scanner.GetTokenText());
                            break;
                    }

                    if (state == JSDocState.SavingComments)
                    {
                        _parser.NextJSDocCommentTextToken(/*inBackticks*/ false);
                    }
                    else
                    {
                        _parser.NextTokenJSDoc();
                    }
                }

            loopDone:
                var trimmedComments = string.Concat(_comments).TrimEnd();
                if (_parts.Count != 0 && trimmedComments.Length != 0)
                {
                    _parts.Add(_parser.FinishNode(_parser._factory.CreateJSDocText(trimmedComments), _linkEnd ?? _start, _commentsPos));
                }

                if (_parts.Count != 0 && _tags is not null)
                {
                    Debug.Assert(_commentsPos is not null, "having parsed tags implies that the end of the comment span should be set");
                }

                var tagsArray = _tags is not null ? _parser.CreateNodeArray(_tags, _tagsPos, _tagsEnd) : null;
                return _parser.FinishNode(
                    _parser._factory.CreateJSDocComment(
                        _parts.Count != 0
                            ? _parser.CreateNodeArray(_parts, _start, _commentsPos)
                            : trimmedComments.Length != 0 ? trimmedComments : null,
                        tagsArray),
                    _start,
                    _end);
            }

            /// <summary>JS's <c>content.lastIndexOf("\n", start)</c>, whose index is clamped to the last character.</summary>
            /// <remarks>
            /// PORT-NOTE: JS clamps a <c>fromIndex</c> past the end of the string to <c>length - 1</c>, while .NET's
            /// <c>LastIndexOf(char, int)</c> throws for an index at or past the end, so the port clamps the same way.
            /// </remarks>
            private int LastNewlineBefore(int position)
            {
                var searchFrom = Math.Min(position, _content.Length - 1);
                return searchFrom >= 0 ? _content.LastIndexOf('\n', searchFrom) : -1;
            }

            /// <summary>ported from: src/compiler/parser.ts:9047-9051 — <c>removeLeadingNewlines</c>.</summary>
            private static void RemoveLeadingNewlines(List<string> comments)
            {
                while (comments.Count != 0 && (comments[0] == "\n" || comments[0] == "\r"))
                {
                    comments.RemoveAt(0);
                }
            }

            /// <summary>ported from: src/compiler/parser.ts:9053-9066 — <c>removeTrailingWhitespace</c>.</summary>
            private static void RemoveTrailingWhitespace(List<string> comments)
            {
                while (comments.Count != 0)
                {
                    var last = comments[^1];
                    var trimmed = last.TrimEnd();
                    if (trimmed.Length == 0)
                    {
                        comments.RemoveAt(comments.Count - 1);
                    }
                    else if (trimmed.Length < last.Length)
                    {
                        comments[^1] = trimmed;
                        break;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            /// <summary>ported from: src/compiler/parser.ts:9068-9080 — <c>isNextNonwhitespaceTokenEndOfFile</c>.</summary>
            private bool IsNextNonwhitespaceTokenEndOfFile()
            {
                // We must use infinite lookahead, as there could be any number of newlines :(
                while (true)
                {
                    _parser.NextTokenJSDoc();
                    if (_parser.Token() == SyntaxKind.EndOfFileToken)
                    {
                        return true;
                    }

                    if (_parser.Token() is not (SyntaxKind.WhitespaceTrivia or SyntaxKind.NewLineTrivia))
                    {
                        return false;
                    }
                }
            }

            /// <summary>ported from: src/compiler/parser.ts:9082-9091 — <c>skipWhitespace</c>.</summary>
            private void SkipWhitespace()
            {
                if (_parser.Token() is SyntaxKind.WhitespaceTrivia or SyntaxKind.NewLineTrivia)
                {
                    if (_parser.LookAheadBool(IsNextNonwhitespaceTokenEndOfFile))
                    {
                        return; // Don't skip whitespace prior to EoF (or end of comment) - that shouldn't be included in any node's range
                    }
                }

                while (_parser.Token() is SyntaxKind.WhitespaceTrivia or SyntaxKind.NewLineTrivia)
                {
                    _parser.NextTokenJSDoc();
                }
            }

            /// <summary>ported from: src/compiler/parser.ts:9093-9115 — <c>skipWhitespaceOrAsterisk</c>.</summary>
            private string SkipWhitespaceOrAsterisk()
            {
                if (_parser.Token() is SyntaxKind.WhitespaceTrivia or SyntaxKind.NewLineTrivia)
                {
                    if (_parser.LookAheadBool(IsNextNonwhitespaceTokenEndOfFile))
                    {
                        return ""; // Don't skip whitespace prior to EoF (or end of comment) - that shouldn't be included in any node's range
                    }
                }

                var precedingLineBreak = _parser._scanner.HasPrecedingLineBreak();
                var seenLineBreak = false;
                var indentText = "";
                while ((precedingLineBreak && _parser.Token() == SyntaxKind.AsteriskToken)
                    || _parser.Token() is SyntaxKind.WhitespaceTrivia or SyntaxKind.NewLineTrivia)
                {
                    indentText += _parser._scanner.GetTokenText();
                    if (_parser.Token() == SyntaxKind.NewLineTrivia)
                    {
                        precedingLineBreak = true;
                        seenLineBreak = true;
                        indentText = "";
                    }
                    else if (_parser.Token() == SyntaxKind.AsteriskToken)
                    {
                        precedingLineBreak = false;
                    }

                    _parser.NextTokenJSDoc();
                }

                return seenLineBreak ? indentText : "";
            }

            /// <summary>ported from: src/compiler/parser.ts:9117-9198 — <c>parseTag</c>.</summary>
            private JSDocTag? ParseTag(int margin)
            {
                Debug.Assert(_parser.Token() == SyntaxKind.AtToken);
                var start = _parser._scanner.GetTokenStart();
                _parser.NextTokenJSDoc();

                var tagName = ParseJSDocIdentifierName(/*message*/ null);
                var indentText = SkipWhitespaceOrAsterisk();

                JSDocTag? tag;
                switch (tagName.EscapedText)
                {
                    case "author":
                        tag = ParseAuthorTag(start, tagName, margin, indentText);
                        break;
                    case "implements":
                        tag = ParseImplementsTag(start, tagName, margin, indentText);
                        break;
                    case "augments":
                    case "extends":
                        tag = ParseAugmentsTag(start, tagName, margin, indentText);
                        break;
                    case "class":
                    case "constructor":
                        tag = ParseSimpleTag(start, _parser._factory.CreateJSDocClassTag, tagName, margin, indentText);
                        break;
                    case "public":
                        tag = ParseSimpleTag(start, _parser._factory.CreateJSDocPublicTag, tagName, margin, indentText);
                        break;
                    case "private":
                        tag = ParseSimpleTag(start, _parser._factory.CreateJSDocPrivateTag, tagName, margin, indentText);
                        break;
                    case "protected":
                        tag = ParseSimpleTag(start, _parser._factory.CreateJSDocProtectedTag, tagName, margin, indentText);
                        break;
                    case "readonly":
                        tag = ParseSimpleTag(start, _parser._factory.CreateJSDocReadonlyTag, tagName, margin, indentText);
                        break;
                    case "override":
                        tag = ParseSimpleTag(start, _parser._factory.CreateJSDocOverrideTag, tagName, margin, indentText);
                        break;
                    case "deprecated":
                        _parser._hasDeprecatedTag = true;
                        tag = ParseSimpleTag(start, _parser._factory.CreateJSDocDeprecatedTag, tagName, margin, indentText);
                        break;
                    case "this":
                        tag = ParseThisTag(start, tagName, margin, indentText);
                        break;
                    case "enum":
                        tag = ParseEnumTag(start, tagName, margin, indentText);
                        break;
                    case "arg":
                    case "argument":
                    case "param":
                        return ParseParameterOrPropertyTag(start, tagName, PropertyLikeParse.Parameter, margin);
                    case "return":
                    case "returns":
                        tag = ParseReturnTag(start, tagName, margin, indentText);
                        break;
                    case "template":
                        tag = ParseTemplateTag(start, tagName, margin, indentText);
                        break;
                    case "type":
                        tag = ParseTypeTag(start, tagName, margin, indentText);
                        break;
                    case "typedef":
                        tag = ParseTypedefTag(start, tagName, margin, indentText);
                        break;
                    case "callback":
                        tag = ParseCallbackTag(start, tagName, margin, indentText);
                        break;
                    case "overload":
                        tag = ParseOverloadTag(start, tagName, margin, indentText);
                        break;
                    case "satisfies":
                        tag = ParseSatisfiesTag(start, tagName, margin, indentText);
                        break;
                    case "see":
                        tag = ParseSeeTag(start, tagName, margin, indentText);
                        break;
                    case "exception":
                    case "throws":
                        tag = ParseThrowsTag(start, tagName, margin, indentText);
                        break;
                    case "import":
                        tag = ParseImportTag(start, tagName, margin, indentText);
                        break;
                    default:
                        tag = ParseUnknownTag(start, tagName, margin, indentText);
                        break;
                }

                return tag;
            }

            /// <summary>ported from: src/compiler/parser.ts:9200-9206 — <c>parseTrailingTagComments</c>.</summary>
            /// <remarks>
            /// PORT-NOTE: TS slices with <c>indentText.slice(margin)</c>, which answers the empty string when
            /// <c>margin</c> is past the end; C#'s range operator throws there, so the port clamps. TS's
            /// <c>if (!indentText)</c> is a truthiness test on a string, i.e. "is empty".
            /// </remarks>
            private object? ParseTrailingTagComments(int pos, int end, int margin, string indentText)
            {
                // some tags, like typedef and callback, have already parsed their comments earlier
                if (indentText.Length == 0)
                {
                    margin += end - pos;
                }

                return ParseTagComments(margin, indentText.Length > margin ? indentText[margin..] : "");
            }

            /// <summary>ported from: src/compiler/parser.ts:9208-9307 — <c>parseTagComments</c>.</summary>
            /// <remarks>
            /// PORT-NOTE: this is TS's second closure: it shadows the worker's <c>comments</c>/<c>parts</c>/<c>linkEnd</c>
            /// with locals of the same names (kept here as method locals), and answers <c>string | NodeArray&lt;JSDocComment&gt;
            /// | undefined</c> — an <c>object?</c> for the port. TS's <c>break loop</c> is a <c>goto</c> past the loop.
            /// </remarks>
            private object? ParseTagComments(int indent, string? initialMargin = null)
            {
                var commentsPos = _parser.GetNodePos();
                var comments = new List<string>();
                var parts = new List<Node>();
                int? linkEnd = null;
                var state = JSDocState.BeginningOfLine;
                int? margin = null;

                void PushComment(string text)
                {
                    margin ??= indent;
                    comments.Add(text);
                    indent += text.Length;
                }

                if (initialMargin is not null)
                {
                    // jump straight to saving comments if there is some initial indentation
                    if (initialMargin != "")
                    {
                        PushComment(initialMargin);
                    }

                    state = JSDocState.SawAsterisk;
                }

                var tok = _parser.Token();
                while (true)
                {
                    switch (tok)
                    {
                        case SyntaxKind.NewLineTrivia:
                            state = JSDocState.BeginningOfLine;
                            // don't use pushComment here because we want to keep the margin unchanged
                            comments.Add(_parser._scanner.GetTokenText());
                            indent = 0;
                            break;
                        case SyntaxKind.AtToken:
                            _parser._scanner.ResetTokenState(_parser._scanner.GetTokenEnd() - 1);
                            goto loopDone;
                        case SyntaxKind.EndOfFileToken:
                            // Done
                            goto loopDone;
                        case SyntaxKind.WhitespaceTrivia:
                            {
                                Debug.Assert(state is not (JSDocState.SavingComments or JSDocState.SavingBackticks), "whitespace shouldn't come from the scanner while saving comment text");
                                var whitespace = _parser._scanner.GetTokenText();
                                // if the whitespace crosses the margin, take only the whitespace that passes the margin
                                if (margin is not null && indent + whitespace.Length > margin.Value)
                                {
                                    comments.Add(whitespace[(margin.Value - indent)..]);
                                    state = JSDocState.SavingComments;
                                }

                                indent += whitespace.Length;
                                break;
                            }
                        case SyntaxKind.OpenBraceToken:
                            {
                                state = JSDocState.SavingComments;
                                var commentEnd = _parser._scanner.GetTokenFullStart();
                                var linkStart = _parser._scanner.GetTokenEnd() - 1;
                                var link = ParseJSDocLink(linkStart);
                                if (link is not null)
                                {
                                    parts.Add(_parser.FinishNode(_parser._factory.CreateJSDocText(string.Concat(comments)), linkEnd ?? commentsPos, commentEnd));
                                    parts.Add(link);
                                    comments = [];
                                    linkEnd = _parser._scanner.GetTokenEnd();
                                }
                                else
                                {
                                    PushComment(_parser._scanner.GetTokenText());
                                }

                                break;
                            }
                        case SyntaxKind.BacktickToken:
                            state = state == JSDocState.SavingBackticks ? JSDocState.SavingComments : JSDocState.SavingBackticks;
                            PushComment(_parser._scanner.GetTokenText());
                            break;
                        case SyntaxKind.JSDocCommentTextToken:
                            if (state != JSDocState.SavingBackticks)
                            {
                                state = JSDocState.SavingComments; // leading identifiers start recording as well
                            }

                            PushComment(_parser._scanner.GetTokenValue() ?? "");
                            break;
                        case SyntaxKind.AsteriskToken:
                            if (state == JSDocState.BeginningOfLine)
                            {
                                // leading asterisks start recording on the *next* (non-whitespace) token
                                state = JSDocState.SawAsterisk;
                                indent += 1;
                                break;
                            }

                            // record the * as a comment
                            goto default;
                        default:
                            if (state != JSDocState.SavingBackticks)
                            {
                                state = JSDocState.SavingComments; // leading identifiers start recording as well
                            }

                            PushComment(_parser._scanner.GetTokenText());
                            break;
                    }

                    tok = state is JSDocState.SavingComments or JSDocState.SavingBackticks
                        ? _parser.NextJSDocCommentTextToken(state == JSDocState.SavingBackticks)
                        : _parser.NextTokenJSDoc();
                }

            loopDone:
                RemoveLeadingNewlines(comments);
                var trimmedComments = string.Concat(comments).TrimEnd();
                if (parts.Count != 0)
                {
                    if (trimmedComments.Length != 0)
                    {
                        parts.Add(_parser.FinishNode(_parser._factory.CreateJSDocText(trimmedComments), linkEnd ?? commentsPos));
                    }

                    return _parser.CreateNodeArray(parts, commentsPos, _parser._scanner.GetTokenEnd());
                }

                if (trimmedComments.Length != 0)
                {
                    return trimmedComments;
                }

                return null;
            }

            /// <summary>ported from: src/compiler/parser.ts:9309-9326 — <c>parseJSDocLink</c>.</summary>
            /// <remarks>
            /// PORT-NOTE: TS picks one of three factory functions and calls it; the port picks the same three, as a
            /// <see cref="Node"/> because the three link classes have no closer common type.
            /// </remarks>
            private Node? ParseJSDocLink(int start)
            {
                var linkType = _parser.TryParse<string?>(ParseJSDocLinkPrefix);
                if (linkType is null)
                {
                    return null;
                }

                _parser.NextTokenJSDoc(); // start at token after link, then skip any whitespace
                SkipWhitespace();
                var name = ParseJSDocLinkName();
                var text = new List<string>();
                while (_parser.Token() != SyntaxKind.CloseBraceToken
                    && _parser.Token() != SyntaxKind.NewLineTrivia
                    && _parser.Token() != SyntaxKind.EndOfFileToken)
                {
                    text.Add(_parser._scanner.GetTokenText());
                    _parser.NextTokenJSDoc();
                }

                Node link = linkType == "link"
                    ? _parser._factory.CreateJSDocLink(name, string.Concat(text))
                    : linkType == "linkcode"
                        ? _parser._factory.CreateJSDocLinkCode(name, string.Concat(text))
                        : _parser._factory.CreateJSDocLinkPlain(name, string.Concat(text));
                return _parser.FinishNode(link, start, _parser._scanner.GetTokenEnd());
            }

            /// <summary>ported from: src/compiler/parser.ts:9328-9344 — <c>parseJSDocLinkName</c>.</summary>
            /// <remarks>
            /// PORT-NOTE: TS answers the <c>EntityName | JSDocMemberName</c> union, which the port holds as a
            /// <see cref="Node"/>; a <c>#name</c> after a dot is a missing identifier, as in TS.
            /// </remarks>
            private Node? ParseJSDocLinkName()
            {
                if (ScannerUtilities.TokenIsIdentifierOrKeyword(_parser.Token()))
                {
                    var pos = _parser.GetNodePos();

                    Node name = _parser.ParseIdentifierName();
                    while (_parser.ParseOptional(SyntaxKind.DotToken))
                    {
                        name = _parser.FinishNode(
                            _parser._factory.CreateQualifiedName(
                                name,
                                _parser.Token() == SyntaxKind.PrivateIdentifier
                                    ? _parser.CreateMissingNode<Identifier>(SyntaxKind.Identifier, /*reportAtCurrentPosition*/ false)
                                    : _parser.ParseIdentifierName()),
                            pos);
                    }

                    while (_parser.Token() == SyntaxKind.PrivateIdentifier)
                    {
                        _parser.ReScanHashToken();
                        _parser.NextTokenJSDoc();
                        name = _parser.FinishNode(_parser._factory.CreateJSDocMemberName(name, _parser.ParseIdentifier()), pos);
                    }

                    return name;
                }

                return null;
            }

            /// <summary>ported from: src/compiler/parser.ts:9346-9356 — <c>parseJSDocLinkPrefix</c>.</summary>
            private string? ParseJSDocLinkPrefix()
            {
                SkipWhitespaceOrAsterisk();
                if (_parser.Token() == SyntaxKind.OpenBraceToken
                    && _parser.NextTokenJSDoc() == SyntaxKind.AtToken
                    && ScannerUtilities.TokenIsIdentifierOrKeyword(_parser.NextTokenJSDoc()))
                {
                    var kind = _parser._scanner.GetTokenValue();
                    if (kind is not null && IsJSDocLinkTag(kind))
                    {
                        return kind;
                    }
                }

                return null;
            }

            /// <summary>ported from: src/compiler/parser.ts:9358-9360 — <c>isJSDocLinkTag</c>.</summary>
            private static bool IsJSDocLinkTag(string kind)
                => kind is "link" or "linkcode" or "linkplain";

            /// <summary>ported from: src/compiler/parser.ts:9362-9364 — <c>parseUnknownTag</c>.</summary>
            private JSDocUnknownTag ParseUnknownTag(int start, Identifier tagName, int indent, string indentText)
                => _parser.FinishNode(
                    _parser._factory.CreateJSDocUnknownTag(tagName, ParseTrailingTagComments(start, _parser.GetNodePos(), indent, indentText)),
                    start);

            /// <summary>ported from: src/compiler/parser.ts:9366-9378 — <c>addTag</c>.</summary>
            private void AddTag(JSDocTag? tag)
            {
                if (tag is null)
                {
                    return;
                }

                if (_tags is null)
                {
                    _tags = [tag];
                    _tagsPos = tag.Pos;
                }
                else
                {
                    _tags.Add(tag);
                }

                _tagsEnd = tag.End;
            }

            /// <summary>ported from: src/compiler/parser.ts:9380-9383 — <c>tryParseTypeExpression</c>.</summary>
            private JSDocTypeExpression? TryParseTypeExpression()
            {
                SkipWhitespaceOrAsterisk();
                return _parser.Token() == SyntaxKind.OpenBraceToken ? ParseJSDocTypeExpression(_parser) : null;
            }

            /// <summary>ported from: src/compiler/parser.ts:9385-9405 — <c>parseBracketNameInPropertyAndParamTag</c>.</summary>
            /// <remarks>
            /// PORT-NOTE: TS destructures the <c>{ name, isBracketed }</c> object literal it returns; the port answers a
            /// tuple with the same two values.
            /// </remarks>
            private (Node Name, bool IsBracketed) ParseBracketNameInPropertyAndParamTag()
            {
                // Looking for something like '[foo]', 'foo', '[foo.bar]' or 'foo.bar'
                var isBracketed = ParseOptionalJsdoc(SyntaxKind.OpenBracketToken);
                if (isBracketed)
                {
                    SkipWhitespace();
                }

                // a markdown-quoted name: `arg` is not legal jsdoc, but occurs in the wild
                var isBackquoted = ParseOptionalJsdoc(SyntaxKind.BacktickToken);
                var name = ParseJSDocEntityName();
                if (isBackquoted)
                {
                    _parser.ParseExpectedTokenJSDoc(SyntaxKind.BacktickToken);
                }

                if (isBracketed)
                {
                    SkipWhitespace();
                    // May have an optional default, e.g. '[foo = 42]'
                    if (_parser.ParseOptionalToken(SyntaxKind.EqualsToken) is not null)
                    {
                        _parser.ParseExpression();
                    }

                    _parser.ParseExpected(SyntaxKind.CloseBracketToken);
                }

                return (name, isBracketed);
            }

            /// <summary>ported from: src/compiler/parser.ts:9407-9419 — <c>isObjectOrObjectArrayTypeReference</c>.</summary>
            /// <remarks>
            /// PORT-NOTE: TS narrows the node with <c>isTypeReferenceNode</c>/<c>isIdentifierNode</c> and reads
            /// <c>typeName</c>/<c>typeArguments</c>; those predicates and members live on the <see cref="Node"/> class,
            /// while a type position carries the <see cref="ITypeNode"/> interface, so the port views the node as the
            /// class once (every node is one) and casts the two members it then reads.
            /// </remarks>
            private static bool IsObjectOrObjectArrayTypeReference(ITypeNode node)
            {
                var asNode = (Node)node;
                return asNode.Kind switch
                {
                    SyntaxKind.ObjectKeyword => true,
                    SyntaxKind.ArrayType => IsObjectOrObjectArrayTypeReference(((ArrayTypeNode)asNode).ElementType!),
                    _ => NodeTests.IsTypeReferenceNode(asNode)
                        && NodeTests.IsIdentifier(((TypeReferenceNode)asNode).TypeName!)
                        && ((Identifier)((TypeReferenceNode)asNode).TypeName!).EscapedText == "Object"
                        && ((TypeReferenceNode)asNode).TypeArguments is null,
                };
            }

            /// <summary>ported from: src/compiler/parser.ts:9421-9444 — <c>parseParameterOrPropertyTag</c>.</summary>
            /// <remarks>
            /// PORT-NOTE: TS tests <c>!lookAhead(parseJSDocLinkPrefix)</c> — the probe's answer is a string or
            /// <c>undefined</c>, and the port's <c>LookAhead</c> answers <c>null</c> for the latter, so the test is
            /// "is null" here.
            /// </remarks>
            private JSDocTag ParseParameterOrPropertyTag(int start, Identifier tagName, PropertyLikeParse target, int indent)
            {
                var typeExpression = TryParseTypeExpression();
                var isNameFirst = typeExpression is null;
                SkipWhitespaceOrAsterisk();

                var (name, isBracketed) = ParseBracketNameInPropertyAndParamTag();
                var indentText = SkipWhitespaceOrAsterisk();

                if (isNameFirst && _parser.LookAhead<string?>(ParseJSDocLinkPrefix) is null)
                {
                    typeExpression = TryParseTypeExpression();
                }

                var comment = ParseTrailingTagComments(start, _parser.GetNodePos(), indent, indentText);

                var nestedTypeLiteral = ParseNestedTypeLiteral(typeExpression, name, target, indent);
                if (nestedTypeLiteral is not null)
                {
                    typeExpression = nestedTypeLiteral;
                    isNameFirst = true;
                }

                var result = target == PropertyLikeParse.Property
                    ? (JSDocTag)_parser._factory.CreateJSDocPropertyTag(tagName, name, isBracketed, typeExpression, isNameFirst, comment)
                    : _parser._factory.CreateJSDocParameterTag(tagName, name, isBracketed, typeExpression, isNameFirst, comment);
                return _parser.FinishNode(result, start);
            }

            /// <summary>ported from: src/compiler/parser.ts:9446-9468 — <c>parseNestedTypeLiteral</c>.</summary>
            /// <remarks>
            /// PORT-NOTE: TS's loop assigns <c>tryParse(...)</c>'s answer to <c>child</c> and tests its truthiness; the
            /// port tests the same value for null, and appends only the two property-like tag kinds to the child list
            /// (TS narrows the union the same way).
            /// </remarks>
            private JSDocTypeExpression? ParseNestedTypeLiteral(JSDocTypeExpression? typeExpression, Node name, PropertyLikeParse target, int indent)
            {
                if (typeExpression is not null && IsObjectOrObjectArrayTypeReference(typeExpression.Type!))
                {
                    var pos = _parser.GetNodePos();
                    JSDocTag? child;
                    List<JSDocPropertyLikeTag>? children = null;
                    while ((child = _parser.TryParse<JSDocTag?>(() => ParseChildParameterOrPropertyTag(target, indent, name))) is not null)
                    {
                        if (child.Kind is SyntaxKind.JSDocParameterTag or SyntaxKind.JSDocPropertyTag)
                        {
                            children = Core.Append(children, (JSDocPropertyLikeTag)child) as List<JSDocPropertyLikeTag>;
                        }
                        else if (child.Kind == SyntaxKind.JSDocTemplateTag)
                        {
                            _parser.ParseErrorAtRange(((JSDocTemplateTag)child).TagName!, Diagnostics.A_JSDoc_template_tag_may_not_follow_a_typedef_callback_or_overload_tag);
                        }
                    }

                    if (children is not null)
                    {
                        var literal = _parser.FinishNode(_parser._factory.CreateJSDocTypeLiteral(children, typeExpression.Type!.Kind == SyntaxKind.ArrayType), pos);
                        return _parser.FinishNode(_parser._factory.CreateJSDocTypeExpression(literal), pos);
                    }
                }

                return null;
            }

            /// <summary>ported from: src/compiler/parser.ts:9470-9478 — <c>parseReturnTag</c>.</summary>
            /// <remarks>
            /// PORT-NOTE: TS passes <c>unescapeLeadingUnderscores(tagName.escapedText)</c> to the diagnostic; the port's
            /// interned text is nullable, so an absent one unescapes as the empty string.
            /// </remarks>
            private JSDocReturnTag ParseReturnTag(int start, Identifier tagName, int indent, string indentText)
            {
                if (Core.Some(_tags, NodeTests.IsJSDocReturnTag))
                {
                    _parser.ParseErrorAt(
                        tagName.Pos,
                        _parser._scanner.GetTokenStart(),
                        Diagnostics._0_tag_already_specified,
                        Utilities.UnescapeLeadingUnderscores(tagName.EscapedText ?? ""));
                }

                var typeExpression = TryParseTypeExpression();
                return _parser.FinishNode(
                    _parser._factory.CreateJSDocReturnTag(tagName, typeExpression, ParseTrailingTagComments(start, _parser.GetNodePos(), indent, indentText)),
                    start);
            }

            /// <summary>ported from: src/compiler/parser.ts:9480-9486 — <c>parseTypeTag</c>.</summary>
            private JSDocTypeTag ParseTypeTag(int start, Identifier tagName, int? indent, string? indentText)
            {
                if (Core.Some(_tags, NodeTests.IsJSDocTypeTag))
                {
                    _parser.ParseErrorAt(
                        tagName.Pos,
                        _parser._scanner.GetTokenStart(),
                        Diagnostics._0_tag_already_specified,
                        Utilities.UnescapeLeadingUnderscores(tagName.EscapedText ?? ""));
                }

                var typeExpression = ParseJSDocTypeExpression(_parser, /*mayOmitBraces*/ true);
                var comments = indent is not null && indentText is not null
                    ? ParseTrailingTagComments(start, _parser.GetNodePos(), indent.Value, indentText)
                    : null;
                return _parser.FinishNode(_parser._factory.CreateJSDocTypeTag(tagName, typeExpression, comments), start);
            }

            /// <summary>ported from: src/compiler/parser.ts:9488-9494 — <c>parseSeeTag</c>.</summary>
            /// <remarks>
            /// PORT-NOTE: TS probes with <c>lookAhead(() =&gt; …boolean)</c>; the port's boolean speculation helper is
            /// <c>LookAheadBool</c>, which takes exactly that shape.
            /// </remarks>
            private JSDocSeeTag ParseSeeTag(int start, Identifier tagName, int? indent, string? indentText)
            {
                var isMarkdownOrJSDocLink = _parser.Token() == SyntaxKind.OpenBracketToken
                    || _parser.LookAheadBool(() => _parser.NextTokenJSDoc() == SyntaxKind.AtToken
                        && ScannerUtilities.TokenIsIdentifierOrKeyword(_parser.NextTokenJSDoc())
                        && IsJSDocLinkTag(_parser._scanner.GetTokenValue() ?? ""));
                var nameExpression = isMarkdownOrJSDocLink ? null : ParseJSDocNameReference(_parser);
                var comments = indent is not null && indentText is not null
                    ? ParseTrailingTagComments(start, _parser.GetNodePos(), indent.Value, indentText)
                    : null;
                return _parser.FinishNode(_parser._factory.CreateJSDocSeeTag(tagName, nameExpression, comments), start);
            }

            /// <summary>ported from: src/compiler/parser.ts:9496-9500 — <c>parseThrowsTag</c>.</summary>
            private JSDocThrowsTag ParseThrowsTag(int start, Identifier tagName, int indent, string indentText)
            {
                var typeExpression = TryParseTypeExpression();
                var comment = ParseTrailingTagComments(start, _parser.GetNodePos(), indent, indentText);
                return _parser.FinishNode(_parser._factory.CreateJSDocThrowsTag(tagName, typeExpression, comment), start);
            }

            /// <summary>ported from: src/compiler/parser.ts:9502-9515 — <c>parseAuthorTag</c>.</summary>
            /// <remarks>
            /// PORT-NOTE: TS writes <c>typeof comments !== "string" ? createNodeArray(concatenate([textOnly], comments),
            /// commentStart) : textOnly.text + comments</c> — the first arm also covers <c>undefined</c> (which
            /// <c>concatenate</c> turns into the one-element array). The port keeps both arms and both casts.
            /// </remarks>
            private JSDocAuthorTag ParseAuthorTag(int start, Identifier tagName, int indent, string indentText)
            {
                var commentStart = _parser.GetNodePos();
                var textOnly = ParseAuthorNameAndEmail();
                var commentEnd = _parser._scanner.GetTokenFullStart();
                var comments = ParseTrailingTagComments(start, commentEnd, indent, indentText);
                if (comments is null)
                {
                    commentEnd = _parser._scanner.GetTokenFullStart();
                }

                object? allParts;
                if (comments is string commentsText)
                {
                    allParts = textOnly.Text + commentsText;
                }
                else
                {
                    var allComments = Core.Concatenate<Node>([_parser.FinishNode(textOnly, commentStart, commentEnd)], comments as IList<Node>);
                    allParts = _parser.CreateNodeArray(allComments!, commentStart);
                }

                return _parser.FinishNode(_parser._factory.CreateJSDocAuthorTag(tagName, allParts), start);
            }

            /// <summary>ported from: src/compiler/parser.ts:9517-9539 — <c>parseAuthorNameAndEmail</c>.</summary>
            private JSDocText ParseAuthorNameAndEmail()
            {
                var comments = new List<string>();
                var inEmail = false;
                var token = _parser._scanner.GetToken();
                while (token != SyntaxKind.EndOfFileToken && token != SyntaxKind.NewLineTrivia)
                {
                    if (token == SyntaxKind.LessThanToken)
                    {
                        inEmail = true;
                    }
                    else if (token == SyntaxKind.AtToken && !inEmail)
                    {
                        break;
                    }
                    else if (token == SyntaxKind.GreaterThanToken && inEmail)
                    {
                        comments.Add(_parser._scanner.GetTokenText());
                        _parser._scanner.ResetTokenState(_parser._scanner.GetTokenEnd());
                        break;
                    }

                    comments.Add(_parser._scanner.GetTokenText());
                    token = _parser.NextTokenJSDoc();
                }

                return _parser._factory.CreateJSDocText(string.Concat(comments));
            }

            /// <summary>ported from: src/compiler/parser.ts:9541-9544 — <c>parseImplementsTag</c>.</summary>
            private JSDocImplementsTag ParseImplementsTag(int start, Identifier tagName, int margin, string indentText)
            {
                var className = ParseExpressionWithTypeArgumentsForAugments();
                return _parser.FinishNode(
                    _parser._factory.CreateJSDocImplementsTag(tagName, className, ParseTrailingTagComments(start, _parser.GetNodePos(), margin, indentText)),
                    start);
            }

            /// <summary>ported from: src/compiler/parser.ts:9563-9566 — <c>parseAugmentsTag</c>.</summary>
            private JSDocAugmentsTag ParseAugmentsTag(int start, Identifier tagName, int margin, string indentText)
            {
                var className = ParseExpressionWithTypeArgumentsForAugments();
                return _parser.FinishNode(
                    _parser._factory.CreateJSDocAugmentsTag(tagName, className, ParseTrailingTagComments(start, _parser.GetNodePos(), margin, indentText)),
                    start);
            }

            /// <summary>ported from: src/compiler/parser.ts:9546-9551 — <c>parseSatisfiesTag</c>.</summary>
            /// <remarks>
            /// PORT-NOTE: TS guards the comment parse with <c>margin !== undefined &amp;&amp; indentText !== undefined</c>
            /// on two parameters its own signature declares as non-optional; the port's parameters are non-nullable
            /// values, so the guard can only ever succeed and is dropped.
            /// </remarks>
            private JSDocSatisfiesTag ParseSatisfiesTag(int start, Identifier tagName, int margin, string indentText)
            {
                var typeExpression = ParseJSDocTypeExpression(_parser, /*mayOmitBraces*/ false);
                var comments = ParseTrailingTagComments(start, _parser.GetNodePos(), margin, indentText);
                return _parser.FinishNode(_parser._factory.CreateJSDocSatisfiesTag(tagName, typeExpression, comments), start);
            }

            /// <summary>ported from: src/compiler/parser.ts:9553-9561 — <c>parseImportTag</c>.</summary>
            /// <remarks>PORT-NOTE: see <see cref="ParseSatisfiesTag"/> for the dropped always-true guard.</remarks>
            private JSDocImportTag ParseImportTag(int start, Identifier tagName, int margin, string indentText)
            {
                var afterImportTagPos = _parser._scanner.GetTokenFullStart();

                Identifier? identifier = null;
                if (_parser.IsIdentifier())
                {
                    identifier = _parser.ParseIdentifier();
                }

                var importClause = _parser.TryParseImportClause(identifier, afterImportTagPos, SyntaxKind.TypeKeyword, /*skipJsDocLeadingAsterisks*/ true);
                var moduleSpecifier = _parser.ParseModuleSpecifier();
                var attributes = _parser.TryParseImportAttributes();

                var comments = ParseTrailingTagComments(start, _parser.GetNodePos(), margin, indentText);
                return _parser.FinishNode(_parser._factory.CreateJSDocImportTag(tagName, importClause, moduleSpecifier, attributes, comments), start);
            }

            /// <summary>ported from: src/compiler/parser.ts:9568-9582 — <c>parseExpressionWithTypeArgumentsForAugments</c>.</summary>
            /// <remarks>
            /// PORT-NOTE: TS's return type intersects <c>ExpressionWithTypeArguments</c> with the shape of the two
            /// expressions it accepts; C# cannot express the intersection, and both nodes already satisfy the
            /// factory's <c>Expression?</c> slot, so the port answers the plain class.
            /// </remarks>
            private ExpressionWithTypeArguments ParseExpressionWithTypeArgumentsForAugments()
            {
                var usedBrace = _parser.ParseOptional(SyntaxKind.OpenBraceToken);
                var pos = _parser.GetNodePos();
                var expression = ParsePropertyAccessEntityNameExpression();
                _parser._scanner.SetSkipJsDocLeadingAsterisks(true);
                var typeArguments = _parser.TryParseTypeArguments();
                _parser._scanner.SetSkipJsDocLeadingAsterisks(false);
                var node = _parser._factory.CreateExpressionWithTypeArguments(expression, typeArguments);
                var res = _parser.FinishNode(node, pos);
                if (usedBrace)
                {
                    SkipWhitespace();
                    _parser.ParseExpected(SyntaxKind.CloseBraceToken);
                }

                return res;
            }

            /// <summary>ported from: src/compiler/parser.ts:9584-9592 — <c>parsePropertyAccessEntityNameExpression</c>.</summary>
            private Expression ParsePropertyAccessEntityNameExpression()
            {
                var pos = _parser.GetNodePos();
                Expression node = ParseJSDocIdentifierName();
                while (_parser.ParseOptional(SyntaxKind.DotToken))
                {
                    var name = ParseJSDocIdentifierName();
                    node = _parser.FinishNode(_parser._factory.CreatePropertyAccessExpression(node, name), pos);
                }

                return node;
            }

            /// <summary>ported from: src/compiler/parser.ts:9594-9596 — <c>parseSimpleTag</c>.</summary>
            /// <remarks>
            /// PORT-NOTE: TS passes a factory function plus its arguments; the port models the callback as a delegate
            /// (the concrete <c>CreateJSDoc*Tag(tagName, comment)</c> members convert to it, their return type being one
            /// of <see cref="JSDocTag"/>'s).
            /// </remarks>
            private JSDocTag ParseSimpleTag(int start, Func<Identifier?, object?, JSDocTag> createTag, Identifier tagName, int margin, string indentText)
                => _parser.FinishNode(createTag(tagName, ParseTrailingTagComments(start, _parser.GetNodePos(), margin, indentText)), start);

            /// <summary>ported from: src/compiler/parser.ts:9598-9602 — <c>parseThisTag</c>.</summary>
            private JSDocThisTag ParseThisTag(int start, Identifier tagName, int margin, string indentText)
            {
                var typeExpression = ParseJSDocTypeExpression(_parser, /*mayOmitBraces*/ true);
                SkipWhitespace();
                return _parser.FinishNode(
                    _parser._factory.CreateJSDocThisTag(tagName, typeExpression, ParseTrailingTagComments(start, _parser.GetNodePos(), margin, indentText)),
                    start);
            }

            /// <summary>ported from: src/compiler/parser.ts:9604-9608 — <c>parseEnumTag</c>.</summary>
            private JSDocEnumTag ParseEnumTag(int start, Identifier tagName, int margin, string indentText)
            {
                var typeExpression = ParseJSDocTypeExpression(_parser, /*mayOmitBraces*/ true);
                SkipWhitespace();
                return _parser.FinishNode(
                    _parser._factory.CreateJSDocEnumTag(tagName, typeExpression, ParseTrailingTagComments(start, _parser.GetNodePos(), margin, indentText)),
                    start);
            }

            /// <summary>ported from: src/compiler/parser.ts:9610-9656 — <c>parseTypedefTag</c>.</summary>
            /// <remarks>
            /// PORT-NOTE: TS's <c>typeExpression</c> is <c>JSDocTypeExpression | JSDocTypeLiteral | undefined</c> — the
            /// node's own slot is that union too — while <c>NodeFactory.CreateJSDocTypedefTag</c> in the port declares
            /// its parameter as <c>JSDocTypeExpression?</c> (TS's interface, which the parser is typed against, spells the
            /// union; the implementation there is narrower and the call still type-checks because the parser holds the
            /// interface). Passing the literal arm to the narrower port signature would not compile, so the factory is
            /// called with the union left out and the same value is assigned to the node's own slot — the node that comes
            /// back is the one the factory builds, with the same field set to the same value.
            /// </remarks>
            private JSDocTypedefTag ParseTypedefTag(int start, Identifier tagName, int indent, string indentText)
            {
                Node? typeExpression = TryParseTypeExpression();
                SkipWhitespaceOrAsterisk();

                var fullName = ParseJSDocTypeNameWithNamespace();
                SkipWhitespace();
                var comment = ParseTagComments(indent);

                int? end = null;
                if (typeExpression is null || IsObjectOrObjectArrayTypeReference(((JSDocTypeExpression)typeExpression).Type!))
                {
                    JSDocTag? child;
                    JSDocTypeTag? childTypeTag = null;
                    // PORT-NOTE: TS declares this as `JSDocPropertyTag[]` and hands it to
                    // createJSDocTypeLiteral's `readonly JSDocPropertyLikeTag[]` slot, which TS's array covariance allows;
                    // the port's lists are invariant, so it holds the wider element type and casts on append.
                    List<JSDocPropertyLikeTag>? jsDocPropertyTags = null;
                    var hasChildren = false;
                    while ((child = _parser.TryParse<JSDocTag?>(() => ParseChildPropertyTag(indent))) is not null)
                    {
                        if (child.Kind == SyntaxKind.JSDocTemplateTag)
                        {
                            break;
                        }

                        hasChildren = true;
                        if (child.Kind == SyntaxKind.JSDocTypeTag)
                        {
                            if (childTypeTag is not null)
                            {
                                var lastError = _parser.ParseErrorAtCurrentToken(Diagnostics.A_JSDoc_typedef_comment_may_not_contain_multiple_type_tags);
                                if (lastError is not null)
                                {
                                    Utilities.AddRelatedInfo(lastError, Utilities.CreateDetachedDiagnostic(_parser._fileName, _parser._sourceText, 0, 0, Diagnostics.The_tag_was_first_specified_here));
                                }

                                break;
                            }
                            else
                            {
                                childTypeTag = (JSDocTypeTag)child;
                            }
                        }
                        else
                        {
                            jsDocPropertyTags = Core.Append(jsDocPropertyTags, (JSDocPropertyLikeTag)child) as List<JSDocPropertyLikeTag>;
                        }
                    }

                    if (hasChildren)
                    {
                        var isArrayType = typeExpression is not null && ((JSDocTypeExpression)typeExpression).Type!.Kind == SyntaxKind.ArrayType;
                        var jsdocTypeLiteral = _parser._factory.CreateJSDocTypeLiteral(jsDocPropertyTags, isArrayType);
                        typeExpression = childTypeTag?.TypeExpression is { } childTypeExpression
                                && !IsObjectOrObjectArrayTypeReference(childTypeExpression.Type!)
                            ? childTypeExpression
                            : _parser.FinishNode<Node>(jsdocTypeLiteral, start);
                        end = typeExpression.End;
                    }
                }

                // Only include the characters between the name end and the next token if a comment was actually parsed out - otherwise it's just whitespace
                // PORT-NOTE: TS writes `end || comment !== undefined` — a truthiness test, so an `end` of 0 (which no
                // parsed node produces) would take the else arm as well; the port spells that out.
                end = (end ?? 0) != 0 || comment is not null
                    ? _parser.GetNodePos()
                    : (fullName ?? typeExpression ?? tagName).End;

                comment ??= ParseTrailingTagComments(start, end.Value, indent, indentText);

                var typedefTag = _parser._factory.CreateJSDocTypedefTag(tagName, /*typeExpression*/ null, fullName, comment);
                typedefTag.TypeExpression = typeExpression;
                return _parser.FinishNode(typedefTag, start, end);
            }

            /// <summary>ported from: src/compiler/parser.ts:9658-9681 — <c>parseJSDocTypeNameWithNamespace</c>.</summary>
            /// <remarks>
            /// PORT-NOTE: TS answers the <c>Identifier | JSDocNamespaceDeclaration</c> union, so the port answers the
            /// wide <see cref="Node"/>; the nested module declaration is the factory's, exactly as TS builds it (with
            /// <c>undefined</c>/<c>NodeFlags.NestedNamespace</c> as the flags argument).
            /// </remarks>
            private Node? ParseJSDocTypeNameWithNamespace(bool nested = false)
            {
                var start = _parser._scanner.GetTokenStart();
                if (!ScannerUtilities.TokenIsIdentifierOrKeyword(_parser.Token()))
                {
                    return null;
                }

                var typeNameOrNamespaceName = ParseJSDocIdentifierName();
                if (_parser.ParseOptional(SyntaxKind.DotToken))
                {
                    var body = ParseJSDocTypeNameWithNamespace(/*nested*/ true);
                    var jsDocNamespaceNode = _parser._factory.CreateModuleDeclaration(
                        /*modifiers*/ null,
                        typeNameOrNamespaceName,
                        body,
                        nested ? NodeFlags.NestedNamespace : null);
                    return _parser.FinishNode<Node>(jsDocNamespaceNode, start);
                }

                if (nested)
                {
                    typeNameOrNamespaceName.Flags |= NodeFlags.IdentifierIsInJSDocNamespace;
                }

                return typeNameOrNamespaceName;
            }

            /// <summary>
            /// Reinterprets a JSDoc tag as another JSDoc tag type, without a runtime check.
            /// </summary>
            /// <remarks>
            /// The single use is <c>ParseCallbackTagParameters</c> below, where the reference implementation's unchecked
            /// cast is observable in the AST. Everywhere else the port uses a checked cast or a kind check, so a wrong
            /// node type still fails loudly; that is why this is a named helper rather than a bare <c>Unsafe.As</c> at
            /// the call site.
            /// </remarks>
            private static T Reinterpret<T>(JSDocTag tag)
                where T : JSDocTag
                => System.Runtime.CompilerServices.Unsafe.As<JSDocTag, T>(ref tag);

            /// <summary>ported from: src/compiler/parser.ts:9691-9703 — <c>parseCallbackTagParameters</c>.</summary>
            private NodeArray<JSDocParameterTag> ParseCallbackTagParameters(int indent)
            {
                var pos = _parser.GetNodePos();
                JSDocTag? child;
                List<JSDocParameterTag>? parameters = null;
                while ((child = _parser.TryParse<JSDocTag?>(() => ParseChildParameterOrPropertyTag(PropertyLikeParse.CallbackParameter, indent))) is not null)
                {
                    if (child.Kind == SyntaxKind.JSDocTemplateTag)
                    {
                        _parser.ParseErrorAtRange(((JSDocTemplateTag)child).TagName!, Diagnostics.A_JSDoc_template_tag_may_not_follow_a_typedef_callback_or_overload_tag);
                        break;
                    }

                    // PORT-NOTE: TS casts the child to `JSDocParameterTag | JSDocTemplateTag` with an unchecked `as`,
                    // but `parseChildParameterOrPropertyTag` is *declared* to also answer a `JSDocThisTag` — so a
                    // `@callback` followed by `@this` really does put a this-tag into the signature's parameter array, and
                    // the reference compiler's AST holds it (tests/cases/conformance/jsdoc/callbackTag4.ts; its dump shows
                    // the this-tag among the parameters). No C# cast can reproduce that — `(T)`, `as` and `(T)(object)`
                    // all check the runtime type — so this is the one place the port punned the reference's own unchecked
                    // cast. Refusing the tag instead would change the AST for input the reference accepts.
                    parameters = Core.Append(parameters, Reinterpret<JSDocParameterTag>(child)) as List<JSDocParameterTag>;
                }

                return _parser.CreateNodeArray(parameters ?? [], pos);
            }

            /// <summary>ported from: src/compiler/parser.ts:9705-9716 — <c>parseJSDocSignature</c>.</summary>
            private JSDocSignature ParseJSDocSignature(int start, int indent)
            {
                var parameters = ParseCallbackTagParameters(indent);
                var returnTag = _parser.TryParse<JSDocReturnTag?>(() =>
                {
                    if (ParseOptionalJsdoc(SyntaxKind.AtToken))
                    {
                        var tag = ParseTag(indent);
                        if (tag is not null && tag.Kind == SyntaxKind.JSDocReturnTag)
                        {
                            return (JSDocReturnTag)tag;
                        }
                    }

                    return null;
                });
                return _parser.FinishNode(_parser._factory.CreateJSDocSignature(/*typeParameters*/ null, parameters, returnTag), start);
            }

            /// <summary>ported from: src/compiler/parser.ts:9718-9731 — <c>parseCallbackTag</c>.</summary>
            /// <remarks>
            /// PORT-NOTE: TS writes <c>if (!comment) comment = parseTrailingTagComments(…)</c> — a truthiness test that
            /// can never answer the empty string, so the null-coalescing assignment is the same choice.
            /// </remarks>
            private JSDocCallbackTag ParseCallbackTag(int start, Identifier tagName, int indent, string indentText)
            {
                var fullName = ParseJSDocTypeNameWithNamespace();
                SkipWhitespace();
                var comment = ParseTagComments(indent);
                var typeExpression = ParseJSDocSignature(start, indent);
                comment ??= ParseTrailingTagComments(start, _parser.GetNodePos(), indent, indentText);
                var end = comment is not null ? _parser.GetNodePos() : typeExpression.End;
                return _parser.FinishNode(_parser._factory.CreateJSDocCallbackTag(tagName, typeExpression, fullName, comment), start, end);
            }

            /// <summary>ported from: src/compiler/parser.ts:9733-9743 — <c>parseOverloadTag</c>.</summary>
            /// <remarks>PORT-NOTE: see <see cref="ParseCallbackTag"/> for the null-coalescing assignment.</remarks>
            private JSDocOverloadTag ParseOverloadTag(int start, Identifier tagName, int indent, string indentText)
            {
                SkipWhitespace();
                var comment = ParseTagComments(indent);
                var typeExpression = ParseJSDocSignature(start, indent);
                comment ??= ParseTrailingTagComments(start, _parser.GetNodePos(), indent, indentText);
                var end = comment is not null ? _parser.GetNodePos() : typeExpression.End;
                return _parser.FinishNode(_parser._factory.CreateJSDocOverloadTag(tagName, typeExpression, comment), start, end);
            }

            /// <summary>ported from: src/compiler/parser.ts:9745-9756 — <c>escapedTextsEqual</c>.</summary>
            /// <remarks>
            /// PORT-NOTE: TS narrows with <c>isIdentifierNode</c> and then reads <c>right</c>/<c>left</c>, which both the
            /// <c>QualifiedName</c> and the <c>JSDocMemberName</c> arm of the union carry; neither member exists on
            /// <see cref="Node"/>, so the port switches on the kind in <see cref="RightEscapedText"/> and
            /// <see cref="NameLeft"/>.
            /// </remarks>
            private static bool EscapedTextsEqual(Node a, Node b)
            {
                while (!NodeTests.IsIdentifier(a) || !NodeTests.IsIdentifier(b))
                {
                    if (!NodeTests.IsIdentifier(a) && !NodeTests.IsIdentifier(b) && RightEscapedText(a) == RightEscapedText(b))
                    {
                        a = NameLeft(a);
                        b = NameLeft(b);
                    }
                    else
                    {
                        return false;
                    }
                }

                return ((Identifier)a).EscapedText == ((Identifier)b).EscapedText;
            }

            /// <summary>TS: <c>node.right.escapedText</c> for the two node types <c>escapedTextsEqual</c> walks.</summary>
            private static string? RightEscapedText(Node node) => node switch
            {
                QualifiedName qualified => qualified.Right?.EscapedText,
                JSDocMemberName member => member.Right?.EscapedText,
                _ => null,
            };

            /// <summary>TS: <c>node.left</c> for the two node types <c>escapedTextsEqual</c> walks.</summary>
            /// <remarks>PORT-NOTE: the default arm is unreachable — the caller has already ruled out the identifier form.</remarks>
            private static Node NameLeft(Node node) => node switch
            {
                QualifiedName qualified => qualified.Left!,
                JSDocMemberName member => member.Left!,
                _ => node,
            };

            /// <summary>ported from: src/compiler/parser.ts:9758-9760 — <c>parseChildPropertyTag</c>.</summary>
            private JSDocTag? ParseChildPropertyTag(int indent)
                => ParseChildParameterOrPropertyTag(PropertyLikeParse.Property, indent);

            /// <summary>ported from: src/compiler/parser.ts:9762-9790 — <c>parseChildParameterOrPropertyTag</c>.</summary>
            /// <remarks>
            /// PORT-NOTE: TS answers the five-tag union or <c>false</c>; every arm is a <see cref="JSDocTag"/>, so the
            /// port answers <c>JSDocTag?</c> and returns null where TS returns <c>false</c>. TS reads
            /// <c>child.name.left</c> straight off the union (see <see cref="NameLeft"/>).
            /// </remarks>
            private JSDocTag? ParseChildParameterOrPropertyTag(PropertyLikeParse target, int indent, Node? name = null)
            {
                var canParseTag = true;
                var seenAsterisk = false;
                while (true)
                {
                    switch (_parser.NextTokenJSDoc())
                    {
                        case SyntaxKind.AtToken:
                            if (canParseTag)
                            {
                                var child = TryParseChildTag(target, indent);
                                if (child is not null
                                    && child.Kind is SyntaxKind.JSDocParameterTag or SyntaxKind.JSDocPropertyTag
                                    && name is not null)
                                {
                                    var childName = ((JSDocPropertyLikeTag)child).Name!;
                                    if (NodeTests.IsIdentifier(childName) || !EscapedTextsEqual(name, NameLeft(childName)))
                                    {
                                        return null;
                                    }
                                }

                                return child;
                            }

                            seenAsterisk = false;
                            break;
                        case SyntaxKind.NewLineTrivia:
                            canParseTag = true;
                            seenAsterisk = false;
                            break;
                        case SyntaxKind.AsteriskToken:
                            if (seenAsterisk)
                            {
                                canParseTag = false;
                            }

                            seenAsterisk = true;
                            break;
                        case SyntaxKind.Identifier:
                            canParseTag = false;
                            break;
                        case SyntaxKind.EndOfFileToken:
                            return null;
                    }
                }
            }

            /// <summary>ported from: src/compiler/parser.ts:9792-9824 — <c>tryParseChildTag</c>.</summary>
            /// <remarks>
            /// PORT-NOTE: TS answers the five-tag union or <c>false</c> (the port answers <c>JSDocTag?</c>), and its
            /// <c>"type"</c> arm is <c>target === PropertyLikeParse.Property &amp;&amp; parseTypeTag(…)</c> — a shortcut
            /// that answers <c>false</c> for any other target, which the port spells as a conditional.
            /// </remarks>
            private JSDocTag? TryParseChildTag(PropertyLikeParse target, int indent)
            {
                Debug.Assert(_parser.Token() == SyntaxKind.AtToken);
                var start = _parser._scanner.GetTokenFullStart();
                _parser.NextTokenJSDoc();

                var tagName = ParseJSDocIdentifierName(/*message*/ null);
                var indentText = SkipWhitespaceOrAsterisk();
                PropertyLikeParse t;
                switch (tagName.EscapedText)
                {
                    case "type":
                        return target == PropertyLikeParse.Property ? ParseTypeTag(start, tagName, indent, indentText) : null;
                    case "prop":
                    case "property":
                        t = PropertyLikeParse.Property;
                        break;
                    case "arg":
                    case "argument":
                    case "param":
                        t = PropertyLikeParse.Parameter | PropertyLikeParse.CallbackParameter;
                        break;
                    case "template":
                        return ParseTemplateTag(start, tagName, indent, indentText);
                    case "this":
                        return ParseThisTag(start, tagName, indent, indentText);
                    default:
                        return null;
                }

                if ((target & t) == 0)
                {
                    return null;
                }

                return ParseParameterOrPropertyTag(start, tagName, target, indent);
            }

            /// <summary>ported from: src/compiler/parser.ts:9826-9852 — <c>parseTemplateTagTypeParameter</c>.</summary>
            /// <remarks>
            /// PORT-NOTE: the factory's <c>modifiers</c> slot is <c>List&lt;ModifierToken&gt;?</c> while the parser builds
            /// <c>List&lt;Node&gt;</c>, so the parsed list is projected through the cast every modifier token satisfies
            /// (see the remarks on <c>ParseModifiers</c> in <c>Parser.Declarations.cs</c>).
            /// </remarks>
            private TypeParameterDeclaration? ParseTemplateTagTypeParameter()
            {
                var typeParameterPos = _parser.GetNodePos();
                var isBracketed = ParseOptionalJsdoc(SyntaxKind.OpenBracketToken);
                if (isBracketed)
                {
                    SkipWhitespace();
                }

                var modifiers = _parser.ParseModifiers(/*allowDecorators*/ false, /*permitConstAsModifier*/ true);
                var name = ParseJSDocIdentifierName(Diagnostics.Unexpected_token_A_type_parameter_name_was_expected_without_curly_braces);
                ITypeNode? defaultType = null;
                if (isBracketed)
                {
                    SkipWhitespace();
                    _parser.ParseExpected(SyntaxKind.EqualsToken);
                    defaultType = _parser.DoInsideOfContext(NodeFlags.JSDoc, _parser.ParseJSDocType);
                    _parser.ParseExpected(SyntaxKind.CloseBracketToken);
                }

                if (Utilities.NodeIsMissing(name))
                {
                    return null;
                }

                return _parser.FinishNode(
                    _parser._factory.CreateTypeParameterDeclaration(
                        modifiers is null ? null : Core.Map(modifiers, static (modifier, _) => (ModifierToken)modifier),
                        name,
                        /*constraint*/ null,
                        defaultType),
                    typeParameterPos);
            }

            /// <summary>ported from: src/compiler/parser.ts:9854-9866 — <c>parseTemplateTagTypeParameters</c>.</summary>
            private NodeArray<TypeParameterDeclaration> ParseTemplateTagTypeParameters()
            {
                var pos = _parser.GetNodePos();
                var typeParameters = new List<TypeParameterDeclaration>();
                do
                {
                    SkipWhitespace();
                    var node = ParseTemplateTagTypeParameter();
                    if (node is not null)
                    {
                        typeParameters.Add(node);
                    }

                    SkipWhitespaceOrAsterisk();
                }
                while (ParseOptionalJsdoc(SyntaxKind.CommaToken));
                return _parser.CreateNodeArray(typeParameters, pos);
            }

            /// <summary>ported from: src/compiler/parser.ts:9868-9883 — <c>parseTemplateTag</c>.</summary>
            private JSDocTemplateTag ParseTemplateTag(int start, Identifier tagName, int indent, string indentText)
            {
                // The template tag looks like one of the following:
                //   @template T,U,V
                //   @template {Constraint} T
                //
                // According to the [closure docs](https://github.com/google/closure-compiler/wiki/Generic-Types#multiple-bounded-template-types):
                //   > Multiple bounded generics cannot be declared on the same line. For the sake of clarity, if multiple templates share the same
                //   > type bound they must be declared on separate lines.
                //
                // TODO: Determine whether we should enforce this in the checker.
                // TODO: Consider moving the `constraint` to the first type parameter as we could then remove `getEffectiveConstraintOfTypeParameter`.
                // TODO: Consider only parsing a single type parameter if there is a constraint.
                var constraint = _parser.Token() == SyntaxKind.OpenBraceToken ? ParseJSDocTypeExpression(_parser) : null;
                var typeParameters = ParseTemplateTagTypeParameters();
                return _parser.FinishNode(
                    _parser._factory.CreateJSDocTemplateTag(tagName, constraint, typeParameters, ParseTrailingTagComments(start, _parser.GetNodePos(), indent, indentText)),
                    start);
            }

            /// <summary>ported from: src/compiler/parser.ts:9885-9891 — <c>parseOptionalJsdoc</c>.</summary>
            private bool ParseOptionalJsdoc(SyntaxKind t)
            {
                if (_parser.Token() == t)
                {
                    _parser.NextTokenJSDoc();
                    return true;
                }

                return false;
            }

            /// <summary>ported from: src/compiler/parser.ts:9893-9907 — <c>parseJSDocEntityName</c>.</summary>
            /// <remarks>
            /// PORT-NOTE: TS answers the <c>EntityName</c> union; its two arms meet at <see cref="Node"/> in the port's
            /// model. The postfix <c>[]</c> is parsed and dropped, as in TS: it is not saved for checking.
            /// </remarks>
            private Node ParseJSDocEntityName()
            {
                Node entity = ParseJSDocIdentifierName();
                if (_parser.ParseOptional(SyntaxKind.OpenBracketToken))
                {
                    _parser.ParseExpected(SyntaxKind.CloseBracketToken);
                    // Note that y[] is accepted as an entity name, but the postfix brackets are not saved for checking.
                    // Technically usejsdoc.org requires them for specifying a property of a type equivalent to Array<{ x: ...}>
                    // but it's not worth it to enforce that restriction.
                }

                while (_parser.ParseOptional(SyntaxKind.DotToken))
                {
                    var name = ParseJSDocIdentifierName();
                    if (_parser.ParseOptional(SyntaxKind.OpenBracketToken))
                    {
                        _parser.ParseExpected(SyntaxKind.CloseBracketToken);
                    }

                    entity = _parser.CreateQualifiedName(entity, name);
                }

                return entity;
            }

            /// <summary>ported from: src/compiler/parser.ts:9909-9923 — <c>parseJSDocIdentifierName</c>.</summary>
            /// <remarks>
            /// PORT-NOTE: TS counts and interns into the parse's own counters, which the port reaches through the parser
            /// it was given; TS's <c>!message</c> (report at the current position only when no message was supplied) is
            /// the null test here, and its <c>message || Diagnostics.Identifier_expected</c> is the null-coalescing one.
            /// </remarks>
            private Identifier ParseJSDocIdentifierName(DiagnosticMessage? message = null)
            {
                if (!ScannerUtilities.TokenIsIdentifierOrKeyword(_parser.Token()))
                {
                    return _parser.CreateMissingNode<Identifier>(
                        SyntaxKind.Identifier,
                        /*reportAtCurrentPosition*/ message is null,
                        message ?? Diagnostics.Identifier_expected);
                }

                _parser._identifierCount++;
                var start = _parser._scanner.GetTokenStart();
                var end = _parser._scanner.GetTokenEnd();
                var originalKeywordKind = _parser.Token();
                var text = _parser.InternIdentifier(_parser._scanner.GetTokenValue() ?? "");
                var result = _parser.FinishNode(_parser._factory.CreateIdentifier(text, originalKeywordKind), start, end);
                _parser.NextTokenJSDoc();
                return result;
            }
        }
    }
}
