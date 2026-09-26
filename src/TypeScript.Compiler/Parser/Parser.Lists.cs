namespace TypeScript.Compiler;

// ported from: src/compiler/parser.ts:2835-3784 — the list machinery (`isListElement` / `isListTerminator` /
// `parseList` / `parseDelimitedList`), the incremental-reuse predicates (`currentNode` / `consumeNode` /
// `isReusable*`) and the entity-name/template/literal helpers that close out the parser's shared infrastructure,
// up to the `// TYPES` banner at 3785.
//
// PORT-NOTE: `Js.IsTruthy` answers a plain `bool` (none of its overloads carry `[NotNullWhen(true)]`), so where TS's
// `if (node)` is what makes the value usable afterwards the port writes `Js.IsTruthy(node)` and marks the first use
// with `!` — the check is the one TS wrote, the `!` is only C#'s nullability plumbing.
internal sealed partial class Parser
{
    //
    // Lists: what can start an element, where the list ends, and how the two decide error recovery
    //

    /// <summary>ported from: src/compiler/parser.ts:2835 — <c>isListElement</c>.</summary>
    /// <remarks>
    /// Every arm of this switch is the other half of the error recovery: it says whether the current token can start
    /// the next element of the list being parsed, and <see cref="IsListTerminator"/> says where the list stops. Both
    /// are consulted by <see cref="ParseList"/> and <see cref="ParseDelimitedList"/> on each iteration, and by
    /// <see cref="AbortParsingListOrMoveToNextToken"/> to decide whether to give up.
    /// </remarks>
    private bool IsListElement(ParsingContext parsingContext, bool inErrorRecovery)
    {
        if (Js.IsTruthy(CurrentNode(parsingContext)))
        {
            return true;
        }

        switch (parsingContext)
        {
            case ParsingContext.SourceElements:
            case ParsingContext.BlockStatements:
            case ParsingContext.SwitchClauseStatements:
                // If we're in error recovery, then we don't want to treat ';' as an empty statement.
                // The problem is that ';' can show up in far too many contexts, and if we see one
                // and assume it's a statement, then we may bail out inappropriately from whatever
                // we're parsing.  For example, if we have a semicolon in the middle of a class, then
                // we really don't want to assume the class is over and we're on a statement in the
                // outer module.  We just want to consume and move on.
                return !(Token() == SyntaxKind.SemicolonToken && inErrorRecovery) && IsStartOfStatement();
            case ParsingContext.SwitchClauses:
                return Token() == SyntaxKind.CaseKeyword || Token() == SyntaxKind.DefaultKeyword;
            case ParsingContext.TypeMembers:
                return LookAhead(IsTypeMemberStart);
            case ParsingContext.ClassMembers:
                // We allow semicolons as class elements (as specified by ES6) as long as we're
                // not in error recovery.  If we're in error recovery, we don't want an errant
                // semicolon to be treated as a class member (since they're almost always used
                // for statements.
                return LookAhead(IsClassMemberStart) || (Token() == SyntaxKind.SemicolonToken && !inErrorRecovery);
            case ParsingContext.EnumMembers:
                // Include open bracket computed properties. This technically also lets in indexers,
                // which would be a candidate for improved error reporting.
                return Token() == SyntaxKind.OpenBracketToken || IsLiteralPropertyName();
            case ParsingContext.ObjectLiteralMembers:
                switch (Token())
                {
                    case SyntaxKind.OpenBracketToken:
                    case SyntaxKind.AsteriskToken:
                    case SyntaxKind.DotDotDotToken:
                    case SyntaxKind.DotToken: // Not an object literal member, but don't want to close the object (see `tests/cases/fourslash/completionsDotInObjectLiteral.ts`)
                        return true;
                    default:
                        return IsLiteralPropertyName();
                }
            case ParsingContext.RestProperties:
                return IsLiteralPropertyName();
            case ParsingContext.ObjectBindingElements:
                return Token() == SyntaxKind.OpenBracketToken || Token() == SyntaxKind.DotDotDotToken || IsLiteralPropertyName();
            case ParsingContext.ImportAttributes:
                return IsImportAttributeName();
            case ParsingContext.HeritageClauseElement:
                // If we see `{ ... }` then only consume it as an expression if it is followed by `,` or `{`
                // That way we won't consume the body of a class in its heritage clause.
                if (Token() == SyntaxKind.OpenBraceToken)
                {
                    return LookAhead(IsValidHeritageClauseObjectLiteral);
                }

                if (!inErrorRecovery)
                {
                    return IsStartOfLeftHandSideExpression() && !IsHeritageClauseExtendsOrImplementsKeyword();
                }
                else
                {
                    // If we're in error recovery we tighten up what we're willing to match.
                    // That way we don't treat something like "this" as a valid heritage clause
                    // element during recovery.
                    return IsIdentifier() && !IsHeritageClauseExtendsOrImplementsKeyword();
                }
            case ParsingContext.VariableDeclarations:
                return IsBindingIdentifierOrPrivateIdentifierOrPattern();
            case ParsingContext.ArrayBindingElements:
                return Token() == SyntaxKind.CommaToken || Token() == SyntaxKind.DotDotDotToken || IsBindingIdentifierOrPrivateIdentifierOrPattern();
            case ParsingContext.TypeParameters:
                return Token() == SyntaxKind.InKeyword || Token() == SyntaxKind.ConstKeyword || IsIdentifier();
            case ParsingContext.ArrayLiteralMembers:
                switch (Token())
                {
                    case SyntaxKind.CommaToken:
                    case SyntaxKind.DotToken: // Not an array literal member, but don't want to close the array (see `tests/cases/fourslash/completionsDotInArrayLiteralInObjectLiteral.ts`)
                        return true;
                }

                // falls through
                goto case ParsingContext.ArgumentExpressions;
            case ParsingContext.ArgumentExpressions:
                return Token() == SyntaxKind.DotDotDotToken || IsStartOfExpression();
            case ParsingContext.Parameters:
                return IsStartOfParameter(/*isJSDocParameter*/ false);
            case ParsingContext.JSDocParameters:
                return IsStartOfParameter(/*isJSDocParameter*/ true);
            case ParsingContext.TypeArguments:
            case ParsingContext.TupleElementTypes:
                return Token() == SyntaxKind.CommaToken || IsStartOfType();
            case ParsingContext.HeritageClauses:
                return IsHeritageClause();
            case ParsingContext.ImportOrExportSpecifiers:
                // bail out if the next token is [FromKeyword StringLiteral].
                // That means we're in something like `import { from "mod"`. Stop here can give better error message.
                if (Token() == SyntaxKind.FromKeyword && LookAhead(NextTokenIsStringLiteral))
                {
                    return false;
                }

                if (Token() == SyntaxKind.StringLiteral)
                {
                    return true; // For "arbitrary module namespace identifiers"
                }

                return ScannerUtilities.TokenIsIdentifierOrKeyword(Token());
            case ParsingContext.JsxAttributes:
                return ScannerUtilities.TokenIsIdentifierOrKeyword(Token()) || Token() == SyntaxKind.OpenBraceToken;
            case ParsingContext.JsxChildren:
                return true;
            case ParsingContext.JSDocComment:
                return true;
            case ParsingContext.Count:
                // Not a real context, only a marker.
                return Debug.Fail<bool>("ParsingContext.Count used as a context");
            default:
                Debug.AssertNever(parsingContext, "Non-exhaustive case in 'isListElement'.");
                return false;
        }
    }

    /// <summary>ported from: src/compiler/parser.ts:2945 — <c>isValidHeritageClauseObjectLiteral</c>.</summary>
    private bool IsValidHeritageClauseObjectLiteral()
    {
        Debug.Assert(Token() == SyntaxKind.OpenBraceToken);
        if (NextToken() == SyntaxKind.CloseBraceToken)
        {
            // if we see "extends {}" then only treat the {} as what we're extending (and not
            // the class body) if we have:
            //
            //      extends {} {
            //      extends {},
            //      extends {} extends
            //      extends {} implements
            var next = NextToken();
            return next == SyntaxKind.CommaToken || next == SyntaxKind.OpenBraceToken || next == SyntaxKind.ExtendsKeyword || next == SyntaxKind.ImplementsKeyword;
        }

        return true;
    }

    /// <summary>ported from: src/compiler/parser.ts:2963 — <c>nextTokenIsIdentifier</c>.</summary>
    private bool NextTokenIsIdentifier()
    {
        NextToken();
        return IsIdentifier();
    }

    /// <summary>ported from: src/compiler/parser.ts:2968 — <c>nextTokenIsIdentifierOrKeyword</c>.</summary>
    private bool NextTokenIsIdentifierOrKeyword()
    {
        NextToken();
        return ScannerUtilities.TokenIsIdentifierOrKeyword(Token());
    }

    /// <summary>ported from: src/compiler/parser.ts:2973 — <c>nextTokenIsIdentifierOrKeywordOrGreaterThan</c>.</summary>
    private bool NextTokenIsIdentifierOrKeywordOrGreaterThan()
    {
        NextToken();
        return ScannerUtilities.TokenIsIdentifierOrKeywordOrGreaterThan(Token());
    }

    /// <summary>ported from: src/compiler/parser.ts:2978 — <c>isHeritageClauseExtendsOrImplementsKeyword</c>.</summary>
    private bool IsHeritageClauseExtendsOrImplementsKeyword()
    {
        if (Token() == SyntaxKind.ImplementsKeyword || Token() == SyntaxKind.ExtendsKeyword)
        {
            return LookAhead(NextTokenIsStartOfExpression);
        }

        return false;
    }

    /// <summary>ported from: src/compiler/parser.ts:2989 — <c>nextTokenIsStartOfExpression</c>.</summary>
    private bool NextTokenIsStartOfExpression()
    {
        NextToken();
        return IsStartOfExpression();
    }

    /// <summary>ported from: src/compiler/parser.ts:2994 — <c>nextTokenIsStartOfType</c>.</summary>
    private bool NextTokenIsStartOfType()
    {
        NextToken();
        return IsStartOfType();
    }

    /// <summary>ported from: src/compiler/parser.ts:3000 — <c>isListTerminator</c>.</summary>
    /// <remarks>
    /// The end of the file ends every list; otherwise the context decides. The comments about "tokens other than …"
    /// mark the extra arms TS added purely so that recovery lands somewhere sensible instead of running to the end of
    /// the file.
    /// </remarks>
    private bool IsListTerminator(ParsingContext kind)
    {
        if (Token() == SyntaxKind.EndOfFileToken)
        {
            // Being at the end of the file ends all lists.
            return true;
        }

        switch (kind)
        {
            case ParsingContext.BlockStatements:
            case ParsingContext.SwitchClauses:
            case ParsingContext.TypeMembers:
            case ParsingContext.ClassMembers:
            case ParsingContext.EnumMembers:
            case ParsingContext.ObjectLiteralMembers:
            case ParsingContext.ObjectBindingElements:
            case ParsingContext.ImportOrExportSpecifiers:
            case ParsingContext.ImportAttributes:
                return Token() == SyntaxKind.CloseBraceToken;
            case ParsingContext.SwitchClauseStatements:
                return Token() == SyntaxKind.CloseBraceToken || Token() == SyntaxKind.CaseKeyword || Token() == SyntaxKind.DefaultKeyword;
            case ParsingContext.HeritageClauseElement:
                return Token() == SyntaxKind.OpenBraceToken || Token() == SyntaxKind.ExtendsKeyword || Token() == SyntaxKind.ImplementsKeyword;
            case ParsingContext.VariableDeclarations:
                return IsVariableDeclaratorListTerminator();
            case ParsingContext.TypeParameters:
                // Tokens other than '>' are here for better error recovery
                return Token() == SyntaxKind.GreaterThanToken || Token() == SyntaxKind.OpenParenToken || Token() == SyntaxKind.OpenBraceToken || Token() == SyntaxKind.ExtendsKeyword || Token() == SyntaxKind.ImplementsKeyword;
            case ParsingContext.ArgumentExpressions:
                // Tokens other than ')' are here for better error recovery
                return Token() == SyntaxKind.CloseParenToken || Token() == SyntaxKind.SemicolonToken;
            case ParsingContext.ArrayLiteralMembers:
            case ParsingContext.TupleElementTypes:
            case ParsingContext.ArrayBindingElements:
                return Token() == SyntaxKind.CloseBracketToken;
            case ParsingContext.JSDocParameters:
            case ParsingContext.Parameters:
            case ParsingContext.RestProperties:
                // Tokens other than ')' and ']' (the latter for index signatures) are here for better error recovery
                return Token() == SyntaxKind.CloseParenToken || Token() == SyntaxKind.CloseBracketToken /*|| token === SyntaxKind.OpenBraceToken*/;
            case ParsingContext.TypeArguments:
                // All other tokens should cause the type-argument to terminate except comma token
                return Token() != SyntaxKind.CommaToken;
            case ParsingContext.HeritageClauses:
                return Token() == SyntaxKind.OpenBraceToken || Token() == SyntaxKind.CloseBraceToken;
            case ParsingContext.JsxAttributes:
                return Token() == SyntaxKind.GreaterThanToken || Token() == SyntaxKind.SlashToken;
            case ParsingContext.JsxChildren:
                return Token() == SyntaxKind.LessThanToken && LookAhead(NextTokenIsSlash);
            default:
                return false;
        }
    }

    /// <summary>ported from: src/compiler/parser.ts:3052 — <c>isVariableDeclaratorListTerminator</c>.</summary>
    private bool IsVariableDeclaratorListTerminator()
    {
        // If we can consume a semicolon (either explicitly, or with ASI), then consider us done
        // with parsing the list of variable declarators.
        if (CanParseSemicolon())
        {
            return true;
        }

        // in the case where we're parsing the variable declarator of a 'for-in' statement, we
        // are done if we see an 'in' keyword in front of us. Same with for-of
        if (IsInOrOfKeyword(Token()))
        {
            return true;
        }

        // ERROR RECOVERY TWEAK:
        // For better error recovery, if we see an '=>' then we just stop immediately.  We've got an
        // arrow function here and it's going to be very unlikely that we'll resynchronize and get
        // another variable declaration.
        if (Token() == SyntaxKind.EqualsGreaterThanToken)
        {
            return true;
        }

        // Keep trying to parse out variable declarators.
        return false;
    }

    /// <summary>ported from: src/compiler/parser.ts:3078 — <c>isInSomeParsingContext</c>.</summary>
    /// <remarks>
    /// True if positioned at element or terminator of the current list or any enclosing list.
    /// </remarks>
    private bool IsInSomeParsingContext()
    {
        // We should be in at least one parsing context, be it SourceElements while parsing
        // a SourceFile, or JSDocComment when lazily parsing JSDoc.
        Debug.Assert(_parsingContexts != 0, "Missing parsing context");
        for (var kind = 0; kind < (int)ParsingContext.Count; kind++)
        {
            if ((_parsingContexts & (1 << kind)) != 0
                && (IsListElement((ParsingContext)kind, /*inErrorRecovery*/ true) || IsListTerminator((ParsingContext)kind)))
            {
                return true;
            }
        }

        return false;
    }

    //
    // parseList / parseDelimitedList / parseBracketedList
    //

    /// <summary>ported from: src/compiler/parser.ts:3094 — <c>parseList</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>parseListElement</c> answers <c>T | undefined</c> (the reuse half of it can answer
    /// <c>undefined</c>), so the port marks the one use of its result with <c>!</c> — an element parser typed
    /// <c>Func&lt;T&gt;</c> cannot answer <c>undefined</c>, which is exactly what TS's <c>T extends Node</c> overload
    /// of <c>parseListElement</c> asserts.
    /// </remarks>
    private NodeArray<T> ParseList<T>(ParsingContext kind, Func<T> parseElement)
        where T : class, INode
    {
        var saveParsingContext = _parsingContexts;
        _parsingContexts |= 1 << (int)kind;
        var list = new List<T>();
        var listPos = GetNodePos();

        while (!IsListTerminator(kind))
        {
            if (IsListElement(kind, /*inErrorRecovery*/ false))
            {
                list.Add(ParseListElement(kind, parseElement)!);

                continue;
            }

            if (AbortParsingListOrMoveToNextToken(kind))
            {
                break;
            }
        }

        _parsingContexts = saveParsingContext;
        return CreateNodeArray(list, listPos);
    }

    /// <summary>ported from: src/compiler/parser.ts:3116 — <c>parseListElement</c>.</summary>
    /// <remarks>
    /// The incremental half of every list: if the cursor can hand back the node that used to sit at this position,
    /// and it still fits this context, the parse skips straight past it.
    /// </remarks>
    private T? ParseListElement<T>(ParsingContext parsingContext, Func<T?> parseElement)
        where T : class, INode
    {
        var node = CurrentNode(parsingContext);
        if (Js.IsTruthy(node))
        {
            return ConsumeNode(node!) as T;
        }

        return parseElement();
    }

    /// <summary>ported from: src/compiler/parser.ts:3125 — <c>currentNode</c>.</summary>
    /// <remarks>
    /// The node the old tree had at this position, if incremental parsing may reuse it. Everything that could make
    /// the old shape wrong is checked here: the node may not be missing, may not intersect the edit, may not contain a
    /// parse error (we must produce the same errors again), must have been parsed under the same context flags (strict
    /// mode and friends change how tokens are interpreted), and must still be valid in the list context we are in.
    /// <para>
    /// PORT-NOTE: <c>nodeIsMissing(undefined)</c> is true in TS, so its first disjunct makes the
    /// <c>undefined</c> case return early; the port writes that check as the leading <c>node is null</c>, which lets
    /// the compiler see the value as non-null for the remaining three tests.
    /// </para>
    /// </remarks>
    private Node? CurrentNode(ParsingContext parsingContext, int? pos = null)
    {
        // If we don't have a cursor or the parsing context isn't reusable, there's nothing to reuse.
        //
        // If there is an outstanding parse error that we've encountered, but not attached to
        // some node, then we cannot get a node from the old source tree.  This is because we
        // want to mark the next node we encounter as being unusable.
        //
        // Note: This may be too conservative.  Perhaps we could reuse the node and set the bit
        // on it (or its leftmost child) as having the error.  For now though, being conservative
        // is nice and likely won't ever affect perf.
        if (_syntaxCursor is null || !IsReusableParsingContext(parsingContext) || _parseErrorBeforeNextFinishedNode)
        {
            return null;
        }

        var node = _syntaxCursor.CurrentNode(pos ?? _scanner.GetTokenFullStart());

        // Can't reuse a missing node.
        // Can't reuse a node that intersected the change range.
        // Can't reuse a node that contains a parse error.  This is necessary so that we
        // produce the same set of errors again.
        if (node is null
            || Utilities.NodeIsMissing(node)
            || IncrementalParser.IntersectsIncrementalChange(node)
            || Utilities.ContainsParseError(node))
        {
            return null;
        }

        // We can only reuse a node if it was parsed under the same strict mode that we're
        // currently in.  i.e. if we originally parsed a node in non-strict mode, but then
        // the user added 'using strict' at the top of the file, then we can't use that node
        // again as the presence of strict mode may cause us to parse the tokens in the file
        // differently.
        //
        // Note: we *can* reuse tokens when the strict mode changes.  That's because tokens
        // are unaffected by strict mode.  It's just the parser will decide what to do with it
        // differently depending on what mode it is in.
        //
        // This also applies to all our other context flags as well.
        var nodeContextFlags = node.Flags & NodeFlags.ContextFlags;
        if (nodeContextFlags != _contextFlags)
        {
            return null;
        }

        // Ok, we have a node that looks like it could be reused.  Now verify that it is valid
        // in the current list parsing context that we're currently at.
        if (!CanReuseNode(node, parsingContext))
        {
            return null;
        }

        if (Utilities.CanHaveJSDoc(node) && node is IJSDocContainer { JsDoc: JSDocArray { JsDocCache: not null } jsDoc })
        {
            // jsDocCache may include tags from parent nodes, which might have been modified.
            jsDoc.JsDocCache = null;
        }

        return node;
    }

    /// <summary>ported from: src/compiler/parser.ts:3179 — <c>consumeNode</c>.</summary>
    private Node ConsumeNode(Node node)
    {
        // Move the scanner so it is after the node we just consumed.
        _scanner.ResetTokenState(node.End);
        NextToken();
        return node;
    }

    /// <summary>ported from: src/compiler/parser.ts:3186 — <c>isReusableParsingContext</c>.</summary>
    private static bool IsReusableParsingContext(ParsingContext parsingContext)
    {
        switch (parsingContext)
        {
            case ParsingContext.ClassMembers:
            case ParsingContext.SwitchClauses:
            case ParsingContext.SourceElements:
            case ParsingContext.BlockStatements:
            case ParsingContext.SwitchClauseStatements:
            case ParsingContext.EnumMembers:
            case ParsingContext.TypeMembers:
            case ParsingContext.VariableDeclarations:
            case ParsingContext.JSDocParameters:
            case ParsingContext.Parameters:
                return true;
        }

        return false;
    }

    /// <summary>ported from: src/compiler/parser.ts:3203 — <c>canReuseNode</c>.</summary>
    /// <remarks>
    /// TS leaves the reasoning for the contexts that are *not* reuse-safe as a comment here; it is kept below because
    /// it is the argument for why each list is or is not in <see cref="IsReusableParsingContext"/>.
    /// </remarks>
    private bool CanReuseNode(Node node, ParsingContext parsingContext)
    {
        switch (parsingContext)
        {
            case ParsingContext.ClassMembers:
                return IsReusableClassMember(node);

            case ParsingContext.SwitchClauses:
                return IsReusableSwitchClause(node);

            case ParsingContext.SourceElements:
            case ParsingContext.BlockStatements:
            case ParsingContext.SwitchClauseStatements:
                return IsReusableStatement(node);

            case ParsingContext.EnumMembers:
                return IsReusableEnumMember(node);

            case ParsingContext.TypeMembers:
                return IsReusableTypeMember(node);

            case ParsingContext.VariableDeclarations:
                return IsReusableVariableDeclaration(node);

            case ParsingContext.JSDocParameters:
            case ParsingContext.Parameters:
                return IsReusableParameter(node);

                // Any other lists we do not care about reusing nodes in.  But feel free to add if
                // you can do so safely.  Danger areas involve nodes that may involve speculative
                // parsing.  If speculative parsing is involved with the node, then the range the
                // parser reached while looking ahead might be in the edited range (see the example
                // in canReuseVariableDeclaratorNode for a good case of this).

                // case ParsingContext.HeritageClauses:
                // This would probably be safe to reuse.  There is no speculative parsing with
                // heritage clauses.

                // case ParsingContext.TypeParameters:
                // This would probably be safe to reuse.  There is no speculative parsing with
                // type parameters.  Note that that's because type *parameters* only occur in
                // unambiguous *type* contexts.  While type *arguments* occur in very ambiguous
                // *expression* contexts.

                // case ParsingContext.TupleElementTypes:
                // This would probably be safe to reuse.  There is no speculative parsing with
                // tuple types.

                // Technically, type argument list types are probably safe to reuse.  While
                // speculative parsing is involved with them (since type argument lists are only
                // produced from speculative parsing a < as a type argument list), we only have
                // the types because speculative parsing succeeded.  Thus, the lookahead never
                // went past the end of the list and rewound.
                // case ParsingContext.TypeArguments:

                // Note: these are almost certainly not safe to ever reuse.  Expressions commonly
                // need a large amount of lookahead, and we should not reuse them as they may
                // have actually intersected the edit.
                // case ParsingContext.ArgumentExpressions:

                // This is not safe to reuse for the same reason as the 'AssignmentExpression'
                // cases.  i.e. a property assignment may end with an expression, and thus might
                // have lookahead far beyond it's old node.
                // case ParsingContext.ObjectLiteralMembers:

                // This is probably not safe to reuse.  There can be speculative parsing with
                // type names in a heritage clause.  There can be generic names in the type
                // name list, and there can be left hand side expressions (which can have type
                // arguments.)
                // case ParsingContext.HeritageClauseElement:

                // Perhaps safe to reuse, but it's unlikely we'd see more than a dozen attributes
                // on any given element. Same for children.
                // case ParsingContext.JsxAttributes:
                // case ParsingContext.JsxChildren:
        }

        return false;
    }

    /// <summary>ported from: src/compiler/parser.ts:3281 — <c>isReusableClassMember</c>.</summary>
    private static bool IsReusableClassMember(Node node)
    {
        if (Js.IsTruthy(node))
        {
            switch (node.Kind)
            {
                case SyntaxKind.Constructor:
                case SyntaxKind.IndexSignature:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                case SyntaxKind.PropertyDeclaration:
                case SyntaxKind.SemicolonClassElement:
                    return true;
                case SyntaxKind.MethodDeclaration:
                    // Method declarations are not necessarily reusable.  An object-literal
                    // may have a method calls "constructor(...)" and we must reparse that
                    // into an actual .ConstructorDeclaration.
                    var methodDeclaration = (MethodDeclaration)node;
                    var nameIsConstructor = methodDeclaration.Name?.Kind == SyntaxKind.Identifier
                        && ((Identifier)methodDeclaration.Name).EscapedText == "constructor";

                    return !nameIsConstructor;
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/parser.ts:3306 — <c>isReusableSwitchClause</c>.</summary>
    private static bool IsReusableSwitchClause(Node node)
    {
        if (Js.IsTruthy(node))
        {
            switch (node.Kind)
            {
                case SyntaxKind.CaseClause:
                case SyntaxKind.DefaultClause:
                    return true;
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/parser.ts:3318 — <c>isReusableStatement</c>.</summary>
    private static bool IsReusableStatement(Node node)
    {
        if (Js.IsTruthy(node))
        {
            switch (node.Kind)
            {
                case SyntaxKind.FunctionDeclaration:
                case SyntaxKind.VariableStatement:
                case SyntaxKind.Block:
                case SyntaxKind.IfStatement:
                case SyntaxKind.ExpressionStatement:
                case SyntaxKind.ThrowStatement:
                case SyntaxKind.ReturnStatement:
                case SyntaxKind.SwitchStatement:
                case SyntaxKind.BreakStatement:
                case SyntaxKind.ContinueStatement:
                case SyntaxKind.ForInStatement:
                case SyntaxKind.ForOfStatement:
                case SyntaxKind.ForStatement:
                case SyntaxKind.WhileStatement:
                case SyntaxKind.WithStatement:
                case SyntaxKind.EmptyStatement:
                case SyntaxKind.TryStatement:
                case SyntaxKind.LabeledStatement:
                case SyntaxKind.DoStatement:
                case SyntaxKind.DebuggerStatement:
                case SyntaxKind.ImportDeclaration:
                case SyntaxKind.ImportEqualsDeclaration:
                case SyntaxKind.ExportDeclaration:
                case SyntaxKind.ExportAssignment:
                case SyntaxKind.ModuleDeclaration:
                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.InterfaceDeclaration:
                case SyntaxKind.EnumDeclaration:
                case SyntaxKind.TypeAliasDeclaration:
                    return true;
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/parser.ts:3357 — <c>isReusableEnumMember</c>.</summary>
    private static bool IsReusableEnumMember(Node node) => node.Kind == SyntaxKind.EnumMember;

    /// <summary>ported from: src/compiler/parser.ts:3361 — <c>isReusableTypeMember</c>.</summary>
    private static bool IsReusableTypeMember(Node node)
    {
        if (Js.IsTruthy(node))
        {
            switch (node.Kind)
            {
                case SyntaxKind.ConstructSignature:
                case SyntaxKind.MethodSignature:
                case SyntaxKind.IndexSignature:
                case SyntaxKind.PropertySignature:
                case SyntaxKind.CallSignature:
                    return true;
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/parser.ts:3376 — <c>isReusableVariableDeclaration</c>.</summary>
    private static bool IsReusableVariableDeclaration(Node node)
    {
        if (node.Kind != SyntaxKind.VariableDeclaration)
        {
            return false;
        }

        // Very subtle incremental parsing bug.  Consider the following code:
        //
        //      let v = new List < A, B
        //
        // This is actually legal code.  It's a list of variable declarators "v = new List<A"
        // on one side and "B" on the other. If you then change that to:
        //
        //      let v = new List < A, B >()
        //
        // then we have a problem.  "v = new List<A" doesn't intersect the change range, so we
        // start reparsing at "B" and we completely fail to handle this properly.
        //
        // In order to prevent this, we do not allow a variable declarator to be reused if it
        // has an initializer.
        var variableDeclarator = (VariableDeclaration)node;
        return variableDeclarator.Initializer is null;
    }

    /// <summary>ported from: src/compiler/parser.ts:3399 — <c>isReusableParameter</c>.</summary>
    private static bool IsReusableParameter(Node node)
    {
        if (node.Kind != SyntaxKind.Parameter)
        {
            return false;
        }

        // See the comment in isReusableVariableDeclaration for why we do this.
        var parameter = (ParameterDeclaration)node;
        return parameter.Initializer is null;
    }

    //
    // Error recovery for lists
    //

    /// <summary>ported from: src/compiler/parser.ts:3410 — <c>abortParsingListOrMoveToNextToken</c>.</summary>
    /// <remarks>
    /// Returns true if we should abort parsing.
    /// </remarks>
    private bool AbortParsingListOrMoveToNextToken(ParsingContext kind)
    {
        ParsingContextErrors(kind);
        if (IsInSomeParsingContext())
        {
            return true;
        }

        NextToken();
        return false;
    }

    /// <summary>ported from: src/compiler/parser.ts:3420 — <c>parsingContextErrors</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's function has no return type, so each arm's value is the <c>Diagnostic</c> the last call
    /// produced — and every caller throws it away. The port declares it <c>void</c>.
    /// </remarks>
    private void ParsingContextErrors(ParsingContext context)
    {
        switch (context)
        {
            case ParsingContext.SourceElements:
                if (Token() == SyntaxKind.DefaultKeyword)
                {
                    ParseErrorAtCurrentToken(Diagnostics._0_expected, ScannerUtilities.TokenToString(SyntaxKind.ExportKeyword));
                }
                else
                {
                    ParseErrorAtCurrentToken(Diagnostics.Declaration_or_statement_expected);
                }

                break;
            case ParsingContext.BlockStatements:
                ParseErrorAtCurrentToken(Diagnostics.Declaration_or_statement_expected);
                break;
            case ParsingContext.SwitchClauses:
                ParseErrorAtCurrentToken(Diagnostics.case_or_default_expected);
                break;
            case ParsingContext.SwitchClauseStatements:
                ParseErrorAtCurrentToken(Diagnostics.Statement_expected);
                break;
            case ParsingContext.RestProperties: // fallthrough
            case ParsingContext.TypeMembers:
                ParseErrorAtCurrentToken(Diagnostics.Property_or_signature_expected);
                break;
            case ParsingContext.ClassMembers:
                ParseErrorAtCurrentToken(Diagnostics.Unexpected_token_A_constructor_method_accessor_or_property_was_expected);
                break;
            case ParsingContext.EnumMembers:
                ParseErrorAtCurrentToken(Diagnostics.Enum_member_expected);
                break;
            case ParsingContext.HeritageClauseElement:
                ParseErrorAtCurrentToken(Diagnostics.Expression_expected);
                break;
            case ParsingContext.VariableDeclarations:
                if (Utilities.IsKeyword(Token()))
                {
                    ParseErrorAtCurrentToken(Diagnostics._0_is_not_allowed_as_a_variable_declaration_name, ScannerUtilities.TokenToString(Token())!);
                }
                else
                {
                    ParseErrorAtCurrentToken(Diagnostics.Variable_declaration_expected);
                }

                break;
            case ParsingContext.ObjectBindingElements:
                ParseErrorAtCurrentToken(Diagnostics.Property_destructuring_pattern_expected);
                break;
            case ParsingContext.ArrayBindingElements:
                ParseErrorAtCurrentToken(Diagnostics.Array_element_destructuring_pattern_expected);
                break;
            case ParsingContext.ArgumentExpressions:
                ParseErrorAtCurrentToken(Diagnostics.Argument_expression_expected);
                break;
            case ParsingContext.ObjectLiteralMembers:
                ParseErrorAtCurrentToken(Diagnostics.Property_assignment_expected);
                break;
            case ParsingContext.ArrayLiteralMembers:
                ParseErrorAtCurrentToken(Diagnostics.Expression_or_comma_expected);
                break;
            case ParsingContext.JSDocParameters:
                ParseErrorAtCurrentToken(Diagnostics.Parameter_declaration_expected);
                break;
            case ParsingContext.Parameters:
                if (Utilities.IsKeyword(Token()))
                {
                    ParseErrorAtCurrentToken(Diagnostics._0_is_not_allowed_as_a_parameter_name, ScannerUtilities.TokenToString(Token())!);
                }
                else
                {
                    ParseErrorAtCurrentToken(Diagnostics.Parameter_declaration_expected);
                }

                break;
            case ParsingContext.TypeParameters:
                ParseErrorAtCurrentToken(Diagnostics.Type_parameter_declaration_expected);
                break;
            case ParsingContext.TypeArguments:
                ParseErrorAtCurrentToken(Diagnostics.Type_argument_expected);
                break;
            case ParsingContext.TupleElementTypes:
                ParseErrorAtCurrentToken(Diagnostics.Type_expected);
                break;
            case ParsingContext.HeritageClauses:
                ParseErrorAtCurrentToken(Diagnostics.Unexpected_token_expected);
                break;
            case ParsingContext.ImportOrExportSpecifiers:
                if (Token() == SyntaxKind.FromKeyword)
                {
                    ParseErrorAtCurrentToken(Diagnostics._0_expected, "}");
                }
                else
                {
                    ParseErrorAtCurrentToken(Diagnostics.Identifier_expected);
                }

                break;
            case ParsingContext.JsxAttributes:
                ParseErrorAtCurrentToken(Diagnostics.Identifier_expected);
                break;
            case ParsingContext.JsxChildren:
                ParseErrorAtCurrentToken(Diagnostics.Identifier_expected);
                break;
            case ParsingContext.ImportAttributes:
                ParseErrorAtCurrentToken(Diagnostics.Identifier_or_string_literal_expected);
                break;
            case ParsingContext.JSDocComment:
                ParseErrorAtCurrentToken(Diagnostics.Identifier_expected);
                break;
            case ParsingContext.Count:
                // Not a real context, only a marker.
                Debug.Fail("ParsingContext.Count used as a context");
                break;
            default:
                Debug.AssertNever(context);
                break;
        }
    }

    /// <summary>ported from: src/compiler/parser.ts:3490-3559 — <c>parseDelimitedList</c> (both overloads).</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares two overloads — one for an element type that is always a node (whose result is therefore
    /// never <c>undefined</c>) and one for <c>T | undefined</c> (whose result is <c>NodeArray&lt;NonNullable&lt;T&gt;&gt;
    /// | undefined</c>) — purely to keep the <c>undefined</c> out of the common caller's way. C# cannot overload on a
    /// type parameter's nullability (annotations are not part of the signature), so the port keeps the second, honest
    /// shape for both: <c>Func&lt;T?&gt;</c> in, <c>NodeArray&lt;T&gt;? | null</c> out. A call site whose element parser
    /// cannot fail appends <c>!</c>, which is exactly the promise TS's first overload encodes.
    /// </remarks>
    private NodeArray<T>? ParseDelimitedList<T>(ParsingContext kind, Func<T?> parseElement, bool considerSemicolonAsDelimiter = false)
        where T : class, INode
    {
        var saveParsingContext = _parsingContexts;
        _parsingContexts |= 1 << (int)kind;
        var list = new List<T>();
        var listPos = GetNodePos();

        var commaStart = -1; // Meaning the previous token was not a comma
        while (true)
        {
            if (IsListElement(kind, /*inErrorRecovery*/ false))
            {
                var startPos = _scanner.GetTokenFullStart();
                var result = ParseListElement(kind, parseElement);
                if (!Js.IsTruthy(result))
                {
                    _parsingContexts = saveParsingContext;
                    return null;
                }

                list.Add(result!);
                commaStart = _scanner.GetTokenStart();

                if (ParseOptional(SyntaxKind.CommaToken))
                {
                    // No need to check for a zero length node since we know we parsed a comma
                    continue;
                }

                commaStart = -1; // Back to the state where the last token was not a comma
                if (IsListTerminator(kind))
                {
                    break;
                }

                // We didn't get a comma, and the list wasn't terminated, explicitly parse
                // out a comma so we give a good error message.
                ParseExpected(SyntaxKind.CommaToken, GetExpectedCommaDiagnostic(kind));

                // If the token was a semicolon, and the caller allows that, then skip it and
                // continue.  This ensures we get back on track and don't result in tons of
                // parse errors.  For example, this can happen when people do things like use
                // a semicolon to delimit object literal members.   Note: we'll have already
                // reported an error when we called parseExpected above.
                if (considerSemicolonAsDelimiter && Token() == SyntaxKind.SemicolonToken && !_scanner.HasPrecedingLineBreak())
                {
                    NextToken();
                }

                if (startPos == _scanner.GetTokenFullStart())
                {
                    // What we're parsing isn't actually remotely recognizable as a element and we've consumed no tokens whatsoever
                    // Consume a token to advance the parser in some way and avoid an infinite loop
                    // This can happen when we're speculatively parsing parenthesized expressions which we think may be arrow functions,
                    // or when a modifier keyword which is disallowed as a parameter name (ie, `static` in strict mode) is supplied
                    NextToken();
                }

                continue;
            }

            if (IsListTerminator(kind))
            {
                break;
            }

            if (AbortParsingListOrMoveToNextToken(kind))
            {
                break;
            }
        }

        _parsingContexts = saveParsingContext;

        // Recording the trailing comma is deliberately done after the previous
        // loop, and not just if we see a list terminator. This is because the list
        // may have ended incorrectly, but it is still important to know if there
        // was a trailing comma.
        // Check if the last token was a comma.
        // Always preserve a trailing comma by marking it on the NodeArray
        return CreateNodeArray(list, listPos, /*end*/ null, commaStart >= 0);
    }

    /// <summary>ported from: src/compiler/parser.ts:3561 — <c>getExpectedCommaDiagnostic</c>.</summary>
    private static DiagnosticMessage? GetExpectedCommaDiagnostic(ParsingContext kind)
        => kind == ParsingContext.EnumMembers ? Diagnostics.An_enum_member_name_must_be_followed_by_a_or : null;

    //
    // Missing lists
    //

    /// <summary>port of the <c>isMissingList: true</c> slot TS's <c>MissingList&lt;T&gt;</c> adds to a node array (parser.ts:3566).</summary>
    /// <remarks>
    /// PORT-NOTE: the marker is the type rather than a property, because <see cref="NodeArray{T}"/> has no spare slot
    /// and TS's own check (<c>isMissingList</c>) is a type test in disguise. It is non-generic so that a
    /// <c>NodeArray&lt;ParameterDeclaration&gt;</c> can be tested through the covariant
    /// <see cref="INodeArray{T}"/> view.
    /// </remarks>
    private interface IMissingList;

    /// <summary>ported from: src/compiler/parser.ts:3565-3567 — <c>MissingList&lt;T&gt;</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares this as an interface extending <c>NodeArray&lt;T&gt;</c> with one extra member, which
    /// C# cannot express; the port derives a concrete class instead (the same treatment <c>JSDocArray</c> got in
    /// <c>Model/JSDocArray.cs</c>) and hangs the marker on <see cref="IMissingList"/>.
    /// </remarks>
    private sealed class MissingList<T> : NodeArray<T>, IMissingList
        where T : INode;

    /// <summary>ported from: src/compiler/parser.ts:3569 — <c>createMissingList</c>.</summary>
    private MissingList<T> CreateMissingList<T>()
        where T : class, INode
        => (MissingList<T>)CreateNodeArray(new MissingList<T>(), GetNodePos());

    /// <summary>ported from: src/compiler/parser.ts:3575 — <c>isMissingList</c>.</summary>
    private static bool IsMissingList(INodeArray<INode>? arr) => arr is IMissingList;

    /// <summary>ported from: src/compiler/parser.ts:3579 — <c>parseBracketedList</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>open</c>/<c>close</c> are <c>PunctuationSyntaxKind</c>, a subset of <c>SyntaxKind</c> with no
    /// C# counterpart, so the port takes <see cref="SyntaxKind"/>. The <c>!</c> is TS's first
    /// <c>parseDelimitedList</c> overload: a <c>T extends Node</c> element parser cannot fail, so the list cannot come
    /// back <c>undefined</c> (see <see cref="ParseDelimitedList{T}"/>).
    /// </remarks>
    private NodeArray<T> ParseBracketedList<T>(ParsingContext kind, Func<T> parseElement, SyntaxKind open, SyntaxKind close)
        where T : class, INode
    {
        if (ParseExpected(open))
        {
            var result = ParseDelimitedList(kind, parseElement);
            ParseExpected(close);
            return result!;
        }

        return CreateMissingList<T>();
    }

    //
    // Entity names
    //

    /// <summary>ported from: src/compiler/parser.ts:3589 — <c>parseEntityName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers <c>EntityName</c> (<c>Identifier | QualifiedName</c>); those two meet at the class
    /// <see cref="Node"/> in the port's model, so that is what it answers — the same widening
    /// <c>Parser.ParseIsolatedEntityName</c> documents.
    /// </remarks>
    private Node ParseEntityName(bool allowReservedWords, DiagnosticMessage? diagnosticMessage = null)
    {
        var pos = GetNodePos();
        Node entity = allowReservedWords ? ParseIdentifierName(diagnosticMessage) : ParseIdentifier(diagnosticMessage);
        while (ParseOptional(SyntaxKind.DotToken))
        {
            if (Token() == SyntaxKind.LessThanToken)
            {
                // The entity is part of a JSDoc-style generic. We will use the gap between `typeName` and
                // `typeArguments` to report it as a grammar error in the checker.
                break;
            }

            entity = FinishNode(
                _factory.CreateQualifiedName(
                    entity,
                    (Identifier)ParseRightSideOfDot(allowReservedWords, /*allowPrivateIdentifiers*/ false, /*allowUnicodeEscapeSequenceInIdentifierName*/ true)),
                pos);
        }

        return entity;
    }

    /// <summary>ported from: src/compiler/parser.ts:3609 — <c>createQualifiedName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>entity</c> is <c>EntityName</c>, which the port types as <see cref="Node"/> (see
    /// <see cref="ParseEntityName"/>).
    /// </remarks>
    private QualifiedName CreateQualifiedName(Node entity, Identifier name)
        => FinishNode(_factory.CreateQualifiedName(entity, name), entity.Pos);

    /// <summary>ported from: src/compiler/parser.ts:3613 — <c>parseRightSideOfDot</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers <c>Identifier | PrivateIdentifier</c>; both derive from <c>MemberExpression</c> in the
    /// port's model, so that is the type the port answers.
    /// <para>
    /// The three callers use it as an identifier name (the entity-name, heritage-clause and property-name paths), and
    /// the reserved-word cases below are why: a keyword is a valid identifier name, but one that starts another
    /// construct should be reported as a missing identifier rather than swallowed.
    /// </para>
    /// </remarks>
    private MemberExpression ParseRightSideOfDot(bool allowIdentifierNames, bool allowPrivateIdentifiers, bool allowUnicodeEscapeSequenceInIdentifierName)
    {
        // Technically a keyword is valid here as all identifiers and keywords are identifier names.
        // However, often we'll encounter this in error situations when the identifier or keyword
        // is actually starting another valid construct.
        //
        // So, we check for the following specific case:
        //
        //      name.
        //      identifierOrKeyword identifierNameOrKeyword
        //
        // Note: the newlines are important here.  For example, if that above code
        // were rewritten into:
        //
        //      name.identifierOrKeyword
        //      identifierNameOrKeyword
        //
        // Then we would consider it valid.  That's because ASI would take effect and
        // the code would be implicitly: "name.identifierOrKeyword; identifierNameOrKeyword".
        // In the first case though, ASI will not take effect because there is not a
        // line terminator after the identifier or keyword.
        if (_scanner.HasPrecedingLineBreak() && ScannerUtilities.TokenIsIdentifierOrKeyword(Token()))
        {
            var matchesPattern = LookAhead(NextTokenIsIdentifierOrKeywordOnSameLine);

            if (matchesPattern)
            {
                // Report that we need an identifier.  However, report it right after the dot,
                // and not on the next token.  This is because the next token might actually
                // be an identifier and the error would be quite confusing.
                return CreateMissingNode<Identifier>(SyntaxKind.Identifier, /*reportAtCurrentPosition*/ true, Diagnostics.Identifier_expected);
            }
        }

        if (Token() == SyntaxKind.PrivateIdentifier)
        {
            var node = ParsePrivateIdentifier();
            return allowPrivateIdentifiers ? node : CreateMissingNode<Identifier>(SyntaxKind.Identifier, /*reportAtCurrentPosition*/ true, Diagnostics.Identifier_expected);
        }

        if (allowIdentifierNames)
        {
            return allowUnicodeEscapeSequenceInIdentifierName ? ParseIdentifierName() : ParseIdentifierNameErrorOnUnicodeEscapeSequence();
        }

        return ParseIdentifier();
    }

    //
    // Templates and literals
    //

    /// <summary>ported from: src/compiler/parser.ts:3656 — <c>parseTemplateSpans</c>.</summary>
    private NodeArray<TemplateSpan> ParseTemplateSpans(bool isTaggedTemplate)
    {
        var pos = GetNodePos();
        var list = new List<TemplateSpan>();
        TemplateSpan node;
        do
        {
            node = ParseTemplateSpan(isTaggedTemplate);
            list.Add(node);
        }
        while (node.Literal?.Kind == SyntaxKind.TemplateMiddle);

        return CreateNodeArray(list, pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:3668 — <c>parseTemplateExpression</c>.</summary>
    private TemplateExpression ParseTemplateExpression(bool isTaggedTemplate)
    {
        var pos = GetNodePos();
        return FinishNode(
            _factory.CreateTemplateExpression(
                ParseTemplateHead(isTaggedTemplate),
                ParseTemplateSpans(isTaggedTemplate)),
            pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:3679 — <c>parseTemplateType</c>.</summary>
    private TemplateLiteralTypeNode ParseTemplateType()
    {
        var pos = GetNodePos();
        return FinishNode(
            _factory.CreateTemplateLiteralType(
                ParseTemplateHead(/*isTaggedTemplate*/ false),
                ParseTemplateTypeSpans()),
            pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:3690 — <c>parseTemplateTypeSpans</c>.</summary>
    private NodeArray<TemplateLiteralTypeSpan> ParseTemplateTypeSpans()
    {
        var pos = GetNodePos();
        var list = new List<TemplateLiteralTypeSpan>();
        TemplateLiteralTypeSpan node;
        do
        {
            node = ParseTemplateTypeSpan();
            list.Add(node);
        }
        while (node.Literal?.Kind == SyntaxKind.TemplateMiddle);

        return CreateNodeArray(list, pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:3702 — <c>parseTemplateTypeSpan</c>.</summary>
    private TemplateLiteralTypeSpan ParseTemplateTypeSpan()
    {
        var pos = GetNodePos();
        return FinishNode(
            _factory.CreateTemplateLiteralTypeSpan(
                ParseType(),
                ParseLiteralOfTemplateSpan(/*isTaggedTemplate*/ false)),
            pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:3713 — <c>parseLiteralOfTemplateSpan</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers <c>TemplateMiddle | TemplateTail</c>, which meet at the <see cref="Node"/> class in the
    /// port's model; every caller (a template span's <c>literal</c> slot) takes exactly that.
    /// </remarks>
    private Node ParseLiteralOfTemplateSpan(bool isTaggedTemplate)
    {
        if (Token() == SyntaxKind.CloseBraceToken)
        {
            ReScanTemplateToken(isTaggedTemplate);
            return ParseTemplateMiddleOrTemplateTail();
        }

        // TODO(rbuckton): Do we need to call `parseExpectedToken` or can we just call `createMissingNode` directly?
        return (TemplateTail)ParseExpectedToken(SyntaxKind.TemplateTail, Diagnostics._0_expected, ScannerUtilities.TokenToString(SyntaxKind.CloseBraceToken));
    }

    /// <summary>ported from: src/compiler/parser.ts:3724 — <c>parseTemplateSpan</c>.</summary>
    private TemplateSpan ParseTemplateSpan(bool isTaggedTemplate)
    {
        var pos = GetNodePos();
        return FinishNode(
            _factory.CreateTemplateSpan(
                AllowInAnd(ParseExpression),
                ParseLiteralOfTemplateSpan(isTaggedTemplate)),
            pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:3735 — <c>parseLiteralNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>as LiteralExpression</c> becomes a cast: the token is a literal kind at every call site, so
    /// the cast is the assertion TS wrote, not a silent narrowing.
    /// </remarks>
    private LiteralExpression ParseLiteralNode() => (LiteralExpression)ParseLiteralLikeNode(Token());

    /// <summary>ported from: src/compiler/parser.ts:3739 — <c>parseTemplateHead</c>.</summary>
    private TemplateHead ParseTemplateHead(bool isTaggedTemplate)
    {
        if (!isTaggedTemplate && (_scanner.GetTokenFlags() & TokenFlags.IsInvalid) != 0)
        {
            ReScanTemplateToken(/*isTaggedTemplate*/ false);
        }

        var fragment = ParseLiteralLikeNode(Token());
        Debug.Assert(fragment.Kind == SyntaxKind.TemplateHead, "Template head has wrong token kind");
        return (TemplateHead)fragment;
    }

    /// <summary>ported from: src/compiler/parser.ts:3748 — <c>parseTemplateMiddleOrTemplateTail</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers <c>TemplateMiddle | TemplateTail</c> (see <see cref="ParseLiteralOfTemplateSpan"/>), and
    /// the port answers <see cref="Node"/>; the fragment was built by <see cref="ParseLiteralLikeNode"/>, so it is one
    /// of the two literal-like template classes.
    /// </remarks>
    private Node ParseTemplateMiddleOrTemplateTail()
    {
        var fragment = ParseLiteralLikeNode(Token());
        Debug.Assert(fragment.Kind == SyntaxKind.TemplateMiddle || fragment.Kind == SyntaxKind.TemplateTail, "Template fragment has wrong token kind");
        return (Node)fragment;
    }

    /// <summary>ported from: src/compiler/parser.ts:3754 — <c>getTemplateLiteralRawText</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: JS's <c>substring(start, end)</c> swaps its arguments when <c>start &gt; end</c>; C#'s range slice
    /// throws instead. TS subtracts 0 for an unterminated token, 1 for the last fragment and 2 for the others, so the
    /// end offset is never below the leading backtick of a token the scanner produced.
    /// </remarks>
    private string GetTemplateLiteralRawText(SyntaxKind kind)
    {
        var isLast = kind == SyntaxKind.NoSubstitutionTemplateLiteral || kind == SyntaxKind.TemplateTail;
        var tokenText = _scanner.GetTokenText();
        var end = tokenText.Length - (_scanner.IsUnterminated() ? 0 : isLast ? 1 : 2);
        return tokenText[1..end];
    }

    /// <summary>ported from: src/compiler/parser.ts:3760 — <c>parseLiteralLikeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers <c>LiteralLikeNode</c>, a union that also names <c>LiteralTypeNode</c> — which implements
    /// no literal capability in the port's model — so the port answers the class both sides meet at,
    /// <see cref="Node"/>, and reads the two slots TS writes through <see cref="ILiteralLikeNode"/>. The callers
    /// (<see cref="ParseLiteralNode"/>, <see cref="ParseTemplateHead"/>, <see cref="ParseTemplateMiddleOrTemplateTail"/>)
    /// cast, as TS's do.
    /// </remarks>
    private Node ParseLiteralLikeNode(SyntaxKind kind)
    {
        var pos = GetNodePos();
        var node = Utilities.IsTemplateLiteralKind(kind)
            ? _factory.CreateTemplateLiteralLikeNode(
                kind,
                _scanner.GetTokenValue()!,
                GetTemplateLiteralRawText(kind),
                _scanner.GetTokenFlags() & TokenFlags.TemplateLiteralLikeFlags)
            :
            // Note that theoretically the following condition would hold true literals like 009,
            // which is not octal. But because of how the scanner separates the tokens, we would
            // never get a token like this. Instead, we would get 00 and 9 as two separate tokens.
            // We also do not need to check for negatives because any prefix operator would be part of a
            // parent unary expression.
            kind == SyntaxKind.NumericLiteral ? _factory.CreateNumericLiteral(_scanner.GetTokenValue(), _scanner.GetNumericLiteralFlags())
            : kind == SyntaxKind.StringLiteral ? _factory.CreateStringLiteral(_scanner.GetTokenValue(), /*isSingleQuote*/ null, _scanner.HasExtendedUnicodeEscape())
            : Utilities.IsLiteralKind(kind) ? _factory.CreateLiteralLikeNode(kind, _scanner.GetTokenValue()!)
            : Debug.Fail<Node>();

        var literalLike = (ILiteralLikeNode)node;
        if (_scanner.HasExtendedUnicodeEscape())
        {
            literalLike.HasExtendedUnicodeEscape = true;
        }

        if (_scanner.IsUnterminated())
        {
            literalLike.IsUnterminated = true;
        }

        NextToken();
        return FinishNode(node, pos);
    }
}
