namespace TypeScript.Compiler;

// ported from: src/compiler/parser.ts:6823-7580 — the STATEMENTS section: blocks and function bodies, the statement
// grammar, the statement-start predicates, and the declaration entry points the dispatcher falls into.
/// <summary>
/// The statement half of <see cref="Parser"/>: <c>parseBlock</c>/<c>parseFunctionBlock</c> and every statement form, the
/// predicates that decide whether a token starts a statement or a declaration (<c>isStartOfStatement</c>,
/// <c>isDeclaration</c>), the dispatcher (<c>parseStatement</c>) and the declaration entry points it uses
/// (<c>parseDeclaration</c>/<c>parseDeclarationWorker</c>).
/// </summary>
/// <remarks>
/// PORT-NOTE: TS types these members with unions and interfaces that have no single C# member. Where the port's model
/// does have one (<see cref="IStatement"/> for <c>Statement</c>, <c>BreakOrContinueStatement</c> and
/// <c>ExpressionStatement | LabeledStatement</c>) the members answer it; the two with none
/// (<c>CaseOrDefaultClause</c>, <c>IterationStatement</c> as a union at the return of
/// <c>parseForOrForInOrForOfStatement</c>) stay with the class the branches share, <see cref="Node"/> and
/// <see cref="IterationStatement"/> respectively (§12.5).
/// <para>
/// PORT-NOTE: TS's <c>withJSDoc&lt;T extends HasJSDoc&gt;</c> accepts any node that can carry JSDoc, so TS applies it to
/// a union-typed local once, at the end (<c>parseExpressionOrLabeledStatement</c>,
/// <c>parseBreakOrContinueStatement</c>). The port's <see cref="WithJSDoc{T}"/> is constrained to <see cref="Node"/> and
/// an interface cannot be its type argument, so those members finish each concrete arm and call <c>withJSDoc</c> there —
/// the same node with the same <c>hasJSDoc</c>, in the same order TS applies it. <see cref="IterationStatement"/> is an
/// abstract class here, so the <c>for</c> statement keeps TS's single trailing call.
/// </para>
/// <para>
/// PORT-NOTE: TS's <c>parseDeclarationWorker</c> can answer <c>undefined</c> (written <c>undefined!</c>) when modifiers
/// were consumed but no declaration followed and <c>modifiersIn</c> was absent; the port answers <c>null!</c> at the same
/// point.
/// </para>
/// <para>
/// PORT-NOTE: <c>nextTokenIsClassKeywordOnSameLine</c> and <c>nextTokenIsFunctionKeywordOnSameLine</c> sit in this
/// range (parser.ts:7153-7161) but were ported with the modifier helpers that call them, in
/// <c>Parser/Parser.NodeCreation.cs</c>; they are not repeated here.
/// </para>
/// </remarks>
internal sealed partial class Parser
{
    /// <summary>ported from: src/compiler/parser.ts:6824-6845 — <c>parseBlock</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>diagnosticMessage</c> defaults to <c>undefined</c>, which <c>parseExpected</c> reads as
    /// "report the generic <c>'{0}' expected</c>"; the port makes that default explicit as <c>null</c>.
    /// </remarks>
    private Block ParseBlock(bool ignoreMissingOpenBrace, DiagnosticMessage? diagnosticMessage = null)
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var openBracePosition = _scanner.GetTokenStart();
        var openBraceParsed = ParseExpected(SyntaxKind.OpenBraceToken, diagnosticMessage);
        if (openBraceParsed || ignoreMissingOpenBrace)
        {
            var multiLine = _scanner.HasPrecedingLineBreak();
            var statements = ParseList<IStatement>(ParsingContext.BlockStatements, ParseStatement);
            ParseExpectedMatchingBrackets(SyntaxKind.OpenBraceToken, SyntaxKind.CloseBraceToken, openBraceParsed, openBracePosition);
            var result = WithJSDoc(FinishNode(_factory.CreateBlock(statements, multiLine), pos), hasJSDoc);
            if (Token() == SyntaxKind.EqualsToken)
            {
                ParseErrorAtCurrentToken(Diagnostics.Declaration_or_statement_expected_This_follows_a_block_of_statements_so_if_you_intended_to_write_a_destructuring_assignment_you_might_need_to_wrap_the_whole_assignment_in_parentheses);
                NextToken();
            }

            return result;
        }

        var missingStatements = CreateMissingList<IStatement>();
        return WithJSDoc(FinishNode(_factory.CreateBlock(missingStatements, /*multiLine*/ null), pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:6847-6875 — <c>parseFunctionBlock</c>.</summary>
    /// <remarks>
    /// A body is not in [Yield]/[Await]/[Decorator] context just because the signature around it was, so those three
    /// are saved and restored around the block; <c>topLevel</c> is cleared for the same reason, so a
    /// <c>return</c> is not treated as a top-level await site.
    /// <para>
    /// PORT-NOTE: TS's <c>setYieldContext</c>/<c>setAwaitContext</c>/<c>setDecoratorContext</c> are one-line wrappers
    /// over <c>setContextFlag</c>. The first two are declared with the rest of the context-flag setters in
    /// <c>Parser/Parser.Declarations.cs</c> and are called by those names; <c>setDecoratorContext</c> has no declaring
    /// slice yet, so the decorator pair calls <see cref="SetContextFlag"/> directly — the same two lines TS runs.
    /// </para>
    /// </remarks>
    private Block ParseFunctionBlock(ParserSignatureFlags flags, DiagnosticMessage? diagnosticMessage = null)
    {
        var savedYieldContext = InYieldContext();
        SetYieldContext((flags & ParserSignatureFlags.Yield) != 0);

        var savedAwaitContext = InAwaitContext();
        SetAwaitContext((flags & ParserSignatureFlags.Await) != 0);

        var savedTopLevel = _topLevel;
        _topLevel = false;

        // We may be in a [Decorator] context when parsing a function expression or
        // arrow function. The body of the function is not in [Decorator] context.
        var saveDecoratorContext = InDecoratorContext();
        if (saveDecoratorContext)
        {
            SetContextFlag(/*val*/ false, NodeFlags.DecoratorContext);
        }

        var block = ParseBlock((flags & ParserSignatureFlags.IgnoreMissingOpenBrace) != 0, diagnosticMessage);

        if (saveDecoratorContext)
        {
            SetContextFlag(/*val*/ true, NodeFlags.DecoratorContext);
        }

        _topLevel = savedTopLevel;
        SetYieldContext(savedYieldContext);
        SetAwaitContext(savedAwaitContext);

        return block;
    }

    /// <summary>ported from: src/compiler/parser.ts:6877-6882 — <c>parseEmptyStatement</c>.</summary>
    private IStatement ParseEmptyStatement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.SemicolonToken);
        return WithJSDoc(FinishNode(_factory.CreateEmptyStatement(), pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:6884-6895 — <c>parseIfStatement</c>.</summary>
    private IfStatement ParseIfStatement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.IfKeyword);
        var openParenPosition = _scanner.GetTokenStart();
        var openParenParsed = ParseExpected(SyntaxKind.OpenParenToken);
        var expression = AllowInAnd(ParseExpression);
        ParseExpectedMatchingBrackets(SyntaxKind.OpenParenToken, SyntaxKind.CloseParenToken, openParenParsed, openParenPosition);
        var thenStatement = ParseStatement();
        var elseStatement = ParseOptional(SyntaxKind.ElseKeyword) ? ParseStatement() : null;
        return WithJSDoc(FinishNode(_factory.CreateIfStatement(expression, thenStatement, elseStatement), pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:6897-6914 — <c>parseDoStatement</c>.</summary>
    /// <remarks>
    /// From: https://mail.mozilla.org/pipermail/es-discuss/2011-August/016188.html
    /// 157 min --- All allen at wirfs-brock.com CONF --- "do{;}while(false)false" prohibited in
    /// spec but allowed in consensus reality. Approved -- this is the de-facto standard whereby
    ///  do;while(0)x will have a semicolon inserted before x.
    /// </remarks>
    private DoStatement ParseDoStatement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.DoKeyword);
        var statement = ParseStatement();
        ParseExpected(SyntaxKind.WhileKeyword);
        var openParenPosition = _scanner.GetTokenStart();
        var openParenParsed = ParseExpected(SyntaxKind.OpenParenToken);
        var expression = AllowInAnd(ParseExpression);
        ParseExpectedMatchingBrackets(SyntaxKind.OpenParenToken, SyntaxKind.CloseParenToken, openParenParsed, openParenPosition);

        ParseOptional(SyntaxKind.SemicolonToken);
        return WithJSDoc(FinishNode(_factory.CreateDoStatement(statement, expression), pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:6916-6926 — <c>parseWhileStatement</c>.</summary>
    private WhileStatement ParseWhileStatement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.WhileKeyword);
        var openParenPosition = _scanner.GetTokenStart();
        var openParenParsed = ParseExpected(SyntaxKind.OpenParenToken);
        var expression = AllowInAnd(ParseExpression);
        ParseExpectedMatchingBrackets(SyntaxKind.OpenParenToken, SyntaxKind.CloseParenToken, openParenParsed, openParenPosition);
        var statement = ParseStatement();
        return WithJSDoc(FinishNode(_factory.CreateWhileStatement(expression, statement), pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:6928-6975 — <c>parseForOrForInOrForOfStatement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers the union <c>ForStatement | ForInOrOfStatement</c> under the declared type
    /// <c>Statement</c>; the port's three classes share the abstract <see cref="IterationStatement"/>, which is what the
    /// local holds — the members are <c>IStatement</c> either way.
    /// <para>
    /// PORT-NOTE: TS passes a <c>Token&lt;SyntaxKind.AwaitKeyword&gt;</c> (its generic <c>parseOptionalToken</c>) to the
    /// <c>for await</c> factory; the port's <c>ParseOptionalToken</c> answers the wide <see cref="Node"/>, so the
    /// keyword token is recovered at the use site.
    /// </para>
    /// </remarks>
    private IStatement ParseForOrForInOrForOfStatement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.ForKeyword);
        var awaitToken = ParseOptionalToken(SyntaxKind.AwaitKeyword);
        ParseExpected(SyntaxKind.OpenParenToken);

        Node? initializer = null;
        if (Token() != SyntaxKind.SemicolonToken)
        {
            if (Token() == SyntaxKind.VarKeyword || Token() == SyntaxKind.LetKeyword || Token() == SyntaxKind.ConstKeyword
                || (Token() == SyntaxKind.UsingKeyword && LookAhead(NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLineDisallowOf))
                // this one is meant to allow of
                || (Token() == SyntaxKind.AwaitKeyword && LookAhead(() => NextTokenIsUsingKeywordThenBindingIdentifierOrStartOfObjectDestructuringOnSameLine(/*disallowOf*/ false))))
            {
                initializer = ParseVariableDeclarationList(/*inForStatementInitializer*/ true);
            }
            else
            {
                initializer = DisallowInAnd(ParseExpression);
            }
        }

        IterationStatement node;
        if (awaitToken is not null ? ParseExpected(SyntaxKind.OfKeyword) : ParseOptional(SyntaxKind.OfKeyword))
        {
            var expression = AllowInAnd(() => ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ true));
            ParseExpected(SyntaxKind.CloseParenToken);
            node = _factory.CreateForOfStatement(awaitToken as KeywordToken, initializer, expression, ParseStatement());
        }
        else if (ParseOptional(SyntaxKind.InKeyword))
        {
            var expression = AllowInAnd(ParseExpression);
            ParseExpected(SyntaxKind.CloseParenToken);
            node = _factory.CreateForInStatement(initializer, expression, ParseStatement());
        }
        else
        {
            ParseExpected(SyntaxKind.SemicolonToken);
            var condition = Token() != SyntaxKind.SemicolonToken && Token() != SyntaxKind.CloseParenToken
                ? AllowInAnd(ParseExpression)
                : null;
            ParseExpected(SyntaxKind.SemicolonToken);
            var incrementor = Token() != SyntaxKind.CloseParenToken
                ? AllowInAnd(ParseExpression)
                : null;
            ParseExpected(SyntaxKind.CloseParenToken);
            node = _factory.CreateForStatement(initializer, condition, incrementor, ParseStatement());
        }

        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:6977-6989 — <c>parseBreakOrContinueStatement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: see the file header on TS's single trailing <c>withJSDoc</c> over the <c>BreakOrContinueStatement</c>
    /// union; each arm finishes its own class here.
    /// </remarks>
    private IStatement ParseBreakOrContinueStatement(SyntaxKind kind)
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();

        ParseExpected(kind == SyntaxKind.BreakStatement ? SyntaxKind.BreakKeyword : SyntaxKind.ContinueKeyword);
        var label = CanParseSemicolon() ? null : ParseIdentifier();

        ParseSemicolon();
        if (kind == SyntaxKind.BreakStatement)
        {
            return WithJSDoc(FinishNode(_factory.CreateBreakStatement(label), pos), hasJSDoc);
        }

        return WithJSDoc(FinishNode(_factory.CreateContinueStatement(label), pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:6991-6998 — <c>parseReturnStatement</c>.</summary>
    private ReturnStatement ParseReturnStatement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.ReturnKeyword);
        var expression = CanParseSemicolon() ? null : AllowInAnd(ParseExpression);
        ParseSemicolon();
        return WithJSDoc(FinishNode(_factory.CreateReturnStatement(expression), pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:7000-7010 — <c>parseWithStatement</c>.</summary>
    /// <remarks>
    /// The body is parsed inside [WithStatement] context so that a nested function can report that it is not allowed to
    /// be there.
    /// </remarks>
    private WithStatement ParseWithStatement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.WithKeyword);
        var openParenPosition = _scanner.GetTokenStart();
        var openParenParsed = ParseExpected(SyntaxKind.OpenParenToken);
        var expression = AllowInAnd(ParseExpression);
        ParseExpectedMatchingBrackets(SyntaxKind.OpenParenToken, SyntaxKind.CloseParenToken, openParenParsed, openParenPosition);
        var statement = DoInsideOfContext(NodeFlags.InWithStatement, ParseStatement);
        return WithJSDoc(FinishNode(_factory.CreateWithStatement(expression, statement), pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:7012-7020 — <c>parseCaseClause</c>.</summary>
    private CaseClause ParseCaseClause()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.CaseKeyword);
        var expression = AllowInAnd(ParseExpression);
        ParseExpected(SyntaxKind.ColonToken);
        var statements = ParseList<IStatement>(ParsingContext.SwitchClauseStatements, ParseStatement);
        return WithJSDoc(FinishNode(_factory.CreateCaseClause(expression, statements), pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:7022-7028 — <c>parseDefaultClause</c>.</summary>
    private DefaultClause ParseDefaultClause()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.DefaultKeyword);
        ParseExpected(SyntaxKind.ColonToken);
        var statements = ParseList<IStatement>(ParsingContext.SwitchClauseStatements, ParseStatement);
        return FinishNode(_factory.CreateDefaultClause(statements), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:7030-7032 — <c>parseCaseOrDefaultClause</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers the <c>CaseOrDefaultClause</c> union; the port answers the class the two branches share,
    /// <see cref="Node"/> (they have no common C# base below it) (§12.5).
    /// </remarks>
    private Node ParseCaseOrDefaultClause()
        => Token() == SyntaxKind.CaseKeyword ? ParseCaseClause() : ParseDefaultClause();

    /// <summary>ported from: src/compiler/parser.ts:7034-7040 — <c>parseCaseBlock</c>.</summary>
    private CaseBlock ParseCaseBlock()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.OpenBraceToken);
        var clauses = ParseList<Node>(ParsingContext.SwitchClauses, ParseCaseOrDefaultClause);
        ParseExpected(SyntaxKind.CloseBraceToken);
        return FinishNode(_factory.CreateCaseBlock(clauses), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:7042-7051 — <c>parseSwitchStatement</c>.</summary>
    private SwitchStatement ParseSwitchStatement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.SwitchKeyword);
        ParseExpected(SyntaxKind.OpenParenToken);
        var expression = AllowInAnd(ParseExpression);
        ParseExpected(SyntaxKind.CloseParenToken);
        var caseBlock = ParseCaseBlock();
        return WithJSDoc(FinishNode(_factory.CreateSwitchStatement(expression, caseBlock), pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:7053-7075 — <c>parseThrowStatement</c>.</summary>
    /// <remarks>
    /// <c>ThrowStatement[Yield] : throw [no LineTerminator here]Expression[In, ?Yield];</c>
    /// <para>
    /// Because of automatic semicolon insertion, the parse reports an error if this <c>throw</c> could be terminated with
    /// a semicolon. <c>parseExpression</c> cannot be called directly as that might consume an expression on the
    /// following line, so a "missing" identifier is created instead, without reporting an error — the grammar walker
    /// reports the real one.
    /// </para>
    /// </remarks>
    private ThrowStatement ParseThrowStatement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.ThrowKeyword);

        var expression = _scanner.HasPrecedingLineBreak() ? null : AllowInAnd(ParseExpression);
        if (expression is null)
        {
            _identifierCount++;
            expression = FinishNode(_factory.CreateIdentifier("", /*originalKeywordKind*/ null), GetNodePos());
        }

        if (!TryParseSemicolon())
        {
            ParseErrorForMissingSemicolonAfter(expression);
        }

        return WithJSDoc(FinishNode(_factory.CreateThrowStatement(expression), pos), hasJSDoc);
    }

    // TODO: Review for error recovery
    /// <summary>ported from: src/compiler/parser.ts:7078-7095 — <c>parseTryStatement</c>.</summary>
    /// <remarks>
    /// If there is no catch clause, then there must be a finally clause; one is parsed out no matter what.
    /// </remarks>
    private TryStatement ParseTryStatement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();

        ParseExpected(SyntaxKind.TryKeyword);
        var tryBlock = ParseBlock(/*ignoreMissingOpenBrace*/ false);
        var catchClause = Token() == SyntaxKind.CatchKeyword ? ParseCatchClause() : null;

        Block? finallyBlock = null;
        if (catchClause is null || Token() == SyntaxKind.FinallyKeyword)
        {
            ParseExpected(SyntaxKind.FinallyKeyword, Diagnostics.catch_or_finally_expected);
            finallyBlock = ParseBlock(/*ignoreMissingOpenBrace*/ false);
        }

        return WithJSDoc(FinishNode(_factory.CreateTryStatement(tryBlock, catchClause, finallyBlock), pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:7097-7113 — <c>parseCatchClause</c>.</summary>
    /// <remarks>
    /// The binding is optional, and absent when there is no <c>(</c>; the shape of the node is kept either way to avoid
    /// degrading performance.
    /// </remarks>
    private CatchClause ParseCatchClause()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.CatchKeyword);

        VariableDeclaration? variableDeclaration;
        if (ParseOptional(SyntaxKind.OpenParenToken))
        {
            variableDeclaration = ParseVariableDeclaration();
            ParseExpected(SyntaxKind.CloseParenToken);
        }
        else
        {
            // Keep shape of node to avoid degrading performance.
            variableDeclaration = null;
        }

        var block = ParseBlock(/*ignoreMissingOpenBrace*/ false);
        return FinishNode(_factory.CreateCatchClause(variableDeclaration, block), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:7115-7121 — <c>parseDebuggerStatement</c>.</summary>
    private IStatement ParseDebuggerStatement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.DebuggerKeyword);
        ParseSemicolon();
        return WithJSDoc(FinishNode(_factory.CreateDebuggerStatement(), pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:7123-7146 — <c>parseExpressionOrLabeledStatement</c>.</summary>
    /// <remarks>
    /// Avoiding having to do the lookahead for a labeled statement by just trying to parse out an expression, seeing if
    /// it is an identifier and then seeing if it is followed by a colon.
    /// <para>
    /// PORT-NOTE: see the file header on the <c>ExpressionStatement | LabeledStatement</c> union and <c>withJSDoc</c>;
    /// the labeled arm finishes its class and returns, the expression arm falls through to the same trailing sequence TS
    /// has.
    /// </para>
    /// </remarks>
    private IStatement ParseExpressionOrLabeledStatement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var hasParen = Token() == SyntaxKind.OpenParenToken;
        var expression = AllowInAnd(ParseExpression);
        if (NodeTests.IsIdentifier(expression) && ParseOptional(SyntaxKind.ColonToken))
        {
            return WithJSDoc(FinishNode(_factory.CreateLabeledStatement(expression, ParseStatement()), pos), hasJSDoc);
        }

        if (!TryParseSemicolon())
        {
            ParseErrorForMissingSemicolonAfter(expression);
        }

        var node = _factory.CreateExpressionStatement(expression);
        if (hasParen)
        {
            // do not parse the same jsdoc twice
            hasJSDoc = false;
        }

        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:7148-7151 — <c>nextTokenIsIdentifierOrKeywordOnSameLine</c>.</summary>
    private bool NextTokenIsIdentifierOrKeywordOnSameLine()
    {
        NextToken();
        return ScannerUtilities.TokenIsIdentifierOrKeyword(Token()) && !_scanner.HasPrecedingLineBreak();
    }

    /// <summary>ported from: src/compiler/parser.ts:7163-7166 — <c>nextTokenIsIdentifierOrKeywordOrLiteralOnSameLine</c>.</summary>
    private bool NextTokenIsIdentifierOrKeywordOrLiteralOnSameLine()
    {
        NextToken();
        return (ScannerUtilities.TokenIsIdentifierOrKeyword(Token()) || Token() == SyntaxKind.NumericLiteral || Token() == SyntaxKind.BigIntLiteral || Token() == SyntaxKind.StringLiteral) && !_scanner.HasPrecedingLineBreak();
    }

    /// <summary>ported from: src/compiler/parser.ts:7168-7262 — <c>isDeclaration</c>.</summary>
    /// <remarks>
    /// <c>declare</c>, <c>module</c>, <c>namespace</c>, <c>interface</c>* and <c>type</c> are all legal JavaScript
    /// identifiers; however, an identifier cannot be followed by another identifier on the same line. This is what the
    /// loop counts on to parse out the respective declarations. For instance, it exploits this to say that
    /// <code>namespace n</code> can be none other than the beginning of a namespace declaration, but need to respect
    /// that JavaScript sees <code>namespace</code> / <code>n</code> as the identifier 'namespace' on one line followed
    /// by the identifier 'n' on another. One token of lookahead is needed to see if it is permissible to try parsing a
    /// declaration.
    /// <para>
    /// *Note*: 'interface' is actually a strict mode reserved word, so while
    /// <code>"use strict"</code> / <code>interface</code> / <code>I {}</code> could be legal, honouring that would add
    /// complexity for very little gain.
    /// </para>
    /// </remarks>
    private bool IsDeclaration()
    {
        while (true)
        {
            switch (Token())
            {
                case SyntaxKind.VarKeyword:
                case SyntaxKind.LetKeyword:
                case SyntaxKind.ConstKeyword:
                case SyntaxKind.FunctionKeyword:
                case SyntaxKind.ClassKeyword:
                case SyntaxKind.EnumKeyword:
                    return true;
                case SyntaxKind.UsingKeyword:
                    return IsUsingDeclaration();
                case SyntaxKind.AwaitKeyword:
                    return IsAwaitUsingDeclaration();

                case SyntaxKind.InterfaceKeyword:
                case SyntaxKind.TypeKeyword:
                case SyntaxKind.DeferKeyword:
                    return NextTokenIsIdentifierOnSameLine();
                case SyntaxKind.ModuleKeyword:
                case SyntaxKind.NamespaceKeyword:
                    return NextTokenIsIdentifierOrStringLiteralOnSameLine();
                case SyntaxKind.AbstractKeyword:
                case SyntaxKind.AccessorKeyword:
                case SyntaxKind.AsyncKeyword:
                case SyntaxKind.DeclareKeyword:
                case SyntaxKind.PrivateKeyword:
                case SyntaxKind.ProtectedKeyword:
                case SyntaxKind.PublicKeyword:
                case SyntaxKind.ReadonlyKeyword:
                    var previousToken = Token();
                    NextToken();
                    // ASI takes effect for this modifier.
                    if (_scanner.HasPrecedingLineBreak())
                    {
                        return false;
                    }

                    if (previousToken == SyntaxKind.DeclareKeyword && Token() == SyntaxKind.TypeKeyword)
                    {
                        // If we see 'declare type', then commit to parsing a type alias. parseTypeAliasDeclaration will
                        // report Line_break_not_permitted_here if needed.
                        return true;
                    }

                    continue;

                case SyntaxKind.GlobalKeyword:
                    NextToken();
                    return Token() == SyntaxKind.OpenBraceToken || Token() == SyntaxKind.Identifier || Token() == SyntaxKind.ExportKeyword;

                case SyntaxKind.ImportKeyword:
                    NextToken();
                    return Token() == SyntaxKind.DeferKeyword || Token() == SyntaxKind.StringLiteral || Token() == SyntaxKind.AsteriskToken
                        || Token() == SyntaxKind.OpenBraceToken || ScannerUtilities.TokenIsIdentifierOrKeyword(Token());
                case SyntaxKind.ExportKeyword:
                    var currentToken = NextToken();
                    if (currentToken == SyntaxKind.TypeKeyword)
                    {
                        currentToken = LookAhead(NextToken);
                    }

                    if (currentToken == SyntaxKind.EqualsToken || currentToken == SyntaxKind.AsteriskToken
                        || currentToken == SyntaxKind.OpenBraceToken || currentToken == SyntaxKind.DefaultKeyword
                        || currentToken == SyntaxKind.AsKeyword || currentToken == SyntaxKind.AtToken)
                    {
                        return true;
                    }

                    continue;

                case SyntaxKind.StaticKeyword:
                    NextToken();
                    continue;

                default:
                    return false;
            }
        }
    }

    /// <summary>ported from: src/compiler/parser.ts:7264-7266 — <c>isStartOfDeclaration</c>.</summary>
    private bool IsStartOfDeclaration() => LookAhead(IsDeclaration);

    /// <summary>ported from: src/compiler/parser.ts:7268-7329 — <c>isStartOfStatement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>nextTokenIsOpenParenOrLessThanOrDot</c> and <c>nextTokenIsIdentifierOnSameLine</c> live with
    /// the expression grammar; the port calls the same names from the slice that owns them.
    /// </remarks>
    private bool IsStartOfStatement()
    {
        switch (Token())
        {
            case SyntaxKind.AtToken:
            case SyntaxKind.SemicolonToken:
            case SyntaxKind.OpenBraceToken:
            case SyntaxKind.VarKeyword:
            case SyntaxKind.LetKeyword:
            case SyntaxKind.UsingKeyword:
            case SyntaxKind.FunctionKeyword:
            case SyntaxKind.ClassKeyword:
            case SyntaxKind.EnumKeyword:
            case SyntaxKind.IfKeyword:
            case SyntaxKind.DoKeyword:
            case SyntaxKind.WhileKeyword:
            case SyntaxKind.ForKeyword:
            case SyntaxKind.ContinueKeyword:
            case SyntaxKind.BreakKeyword:
            case SyntaxKind.ReturnKeyword:
            case SyntaxKind.WithKeyword:
            case SyntaxKind.SwitchKeyword:
            case SyntaxKind.ThrowKeyword:
            case SyntaxKind.TryKeyword:
            case SyntaxKind.DebuggerKeyword:
            // 'catch' and 'finally' do not actually indicate that the code is part of a statement,
            // however, we say they are here so that we may gracefully parse them and error later.
            // falls through
            case SyntaxKind.CatchKeyword:
            case SyntaxKind.FinallyKeyword:
                return true;

            case SyntaxKind.ImportKeyword:
                return IsStartOfDeclaration() || LookAhead(NextTokenIsOpenParenOrLessThanOrDot);

            case SyntaxKind.ConstKeyword:
            case SyntaxKind.ExportKeyword:
                return IsStartOfDeclaration();

            case SyntaxKind.AsyncKeyword:
            case SyntaxKind.DeclareKeyword:
            case SyntaxKind.InterfaceKeyword:
            case SyntaxKind.ModuleKeyword:
            case SyntaxKind.NamespaceKeyword:
            case SyntaxKind.TypeKeyword:
            case SyntaxKind.GlobalKeyword:
            case SyntaxKind.DeferKeyword:
                // When these don't start a declaration, they're an identifier in an expression statement
                return true;

            case SyntaxKind.AccessorKeyword:
            case SyntaxKind.PublicKeyword:
            case SyntaxKind.PrivateKeyword:
            case SyntaxKind.ProtectedKeyword:
            case SyntaxKind.StaticKeyword:
            case SyntaxKind.ReadonlyKeyword:
                // When these don't start a declaration, they may be the start of a class member if an identifier
                // immediately follows. Otherwise they're an identifier in an expression statement.
                return IsStartOfDeclaration() || !LookAhead(NextTokenIsIdentifierOrKeywordOnSameLine);

            default:
                return IsStartOfExpression();
        }
    }

    /// <summary>ported from: src/compiler/parser.ts:7331-7334 — <c>nextTokenIsBindingIdentifierOrStartOfDestructuring</c>.</summary>
    private bool NextTokenIsBindingIdentifierOrStartOfDestructuring()
    {
        NextToken();
        return IsBindingIdentifier() || Token() == SyntaxKind.OpenBraceToken || Token() == SyntaxKind.OpenBracketToken;
    }

    /// <summary>ported from: src/compiler/parser.ts:7336-7339 — <c>isLetDeclaration</c>.</summary>
    /// <remarks>In ES6 'let' always starts a lexical declaration if followed by an identifier or <c>{</c> or <c>[</c>.</remarks>
    private bool IsLetDeclaration()
    {
        return LookAhead(NextTokenIsBindingIdentifierOrStartOfDestructuring);
    }

    /// <summary>ported from: src/compiler/parser.ts:7342-7344 — <c>nextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLineDisallowOf</c>.</summary>
    private bool NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLineDisallowOf()
        => NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLine(/*disallowOf*/ true);

    /// <summary>ported from: src/compiler/parser.ts:7346-7349 — <c>nextTokenIsEqualsOrSemicolonOrColonToken</c>.</summary>
    private bool NextTokenIsEqualsOrSemicolonOrColonToken()
    {
        NextToken();
        return Token() == SyntaxKind.EqualsToken || Token() == SyntaxKind.SemicolonToken || Token() == SyntaxKind.ColonToken;
    }

    /// <summary>ported from: src/compiler/parser.ts:7351-7357 — <c>nextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLine</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>disallowOf</c> is optional, and its callers pass the function itself to <c>lookAhead</c>
    /// (<c>isUsingDeclaration</c>). A C# method with an optional parameter is not a <c>Func&lt;bool&gt;</c>, so those call
    /// sites spell the no-argument form out as a lambda; the default value here is the same <c>undefined</c>.
    /// </remarks>
    private bool NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLine(bool disallowOf = false)
    {
        NextToken();
        if (disallowOf && Token() == SyntaxKind.OfKeyword)
        {
            return LookAhead(NextTokenIsEqualsOrSemicolonOrColonToken);
        }

        return (IsBindingIdentifier() || Token() == SyntaxKind.OpenBraceToken) && !_scanner.HasPrecedingLineBreak();
    }

    /// <summary>ported from: src/compiler/parser.ts:7359-7363 — <c>isUsingDeclaration</c>.</summary>
    /// <remarks>
    /// 'using' always starts a lexical declaration if followed by an identifier. <c>ObjectBindingPattern</c> is also
    /// eagerly parsed so that a grammar error can be reported during check. <c>ArrayBindingPattern</c> is not parsed out
    /// since it potentially conflicts with element access (i.e., <c>using[x]</c>).
    /// </remarks>
    private bool IsUsingDeclaration()
    {
        return LookAhead(() => NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLine(/*disallowOf*/ false));
    }

    /// <summary>ported from: src/compiler/parser.ts:7366-7371 — <c>nextTokenIsUsingKeywordThenBindingIdentifierOrStartOfObjectDestructuringOnSameLine</c>.</summary>
    private bool NextTokenIsUsingKeywordThenBindingIdentifierOrStartOfObjectDestructuringOnSameLine(bool disallowOf = false)
    {
        if (NextToken() == SyntaxKind.UsingKeyword)
        {
            return NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLine(disallowOf);
        }

        return false;
    }

    /// <summary>ported from: src/compiler/parser.ts:7373-7377 — <c>isAwaitUsingDeclaration</c>.</summary>
    /// <remarks>
    /// 'await using' always starts a lexical declaration if followed by an identifier — the <c>await using</c> form of
    /// <see cref="IsUsingDeclaration"/>, with the same eager treatment of <c>ObjectBindingPattern</c> and the same
    /// reason for leaving <c>ArrayBindingPattern</c> alone (i.e., <c>await using[x]</c>).
    /// </remarks>
    private bool IsAwaitUsingDeclaration()
    {
        return LookAhead(() => NextTokenIsUsingKeywordThenBindingIdentifierOrStartOfObjectDestructuringOnSameLine(/*disallowOf*/ false));
    }

    /// <summary>ported from: src/compiler/parser.ts:7380-7461 — <c>parseStatement</c>.</summary>
    /// <remarks>
    /// The dispatcher: the token decides the statement form, and every token that can also begin a declaration first asks
    /// <see cref="IsStartOfDeclaration"/>; anything else is an expression statement or a label.
    /// <para>
    /// PORT-NOTE: TS declares the answer as <c>Statement</c>; the port answers <see cref="IStatement"/>, which is what
    /// the list machinery and <c>createSourceFile</c> take.
    /// </para>
    /// </remarks>
    private IStatement ParseStatement()
    {
        switch (Token())
        {
            case SyntaxKind.SemicolonToken:
                return ParseEmptyStatement();
            case SyntaxKind.OpenBraceToken:
                return ParseBlock(/*ignoreMissingOpenBrace*/ false);
            case SyntaxKind.VarKeyword:
                return ParseVariableStatement(GetNodePos(), HasPrecedingJSDocComment(), /*modifiers*/ null);
            case SyntaxKind.LetKeyword:
                if (IsLetDeclaration())
                {
                    return ParseVariableStatement(GetNodePos(), HasPrecedingJSDocComment(), /*modifiers*/ null);
                }

                break;
            case SyntaxKind.AwaitKeyword:
                if (IsAwaitUsingDeclaration())
                {
                    return ParseVariableStatement(GetNodePos(), HasPrecedingJSDocComment(), /*modifiers*/ null);
                }

                break;
            case SyntaxKind.UsingKeyword:
                if (IsUsingDeclaration())
                {
                    return ParseVariableStatement(GetNodePos(), HasPrecedingJSDocComment(), /*modifiers*/ null);
                }

                break;
            case SyntaxKind.FunctionKeyword:
                return ParseFunctionDeclaration(GetNodePos(), HasPrecedingJSDocComment(), /*modifiers*/ null);
            case SyntaxKind.ClassKeyword:
                return ParseClassDeclaration(GetNodePos(), HasPrecedingJSDocComment(), /*modifiers*/ null);
            case SyntaxKind.IfKeyword:
                return ParseIfStatement();
            case SyntaxKind.DoKeyword:
                return ParseDoStatement();
            case SyntaxKind.WhileKeyword:
                return ParseWhileStatement();
            case SyntaxKind.ForKeyword:
                return ParseForOrForInOrForOfStatement();
            case SyntaxKind.ContinueKeyword:
                return ParseBreakOrContinueStatement(SyntaxKind.ContinueStatement);
            case SyntaxKind.BreakKeyword:
                return ParseBreakOrContinueStatement(SyntaxKind.BreakStatement);
            case SyntaxKind.ReturnKeyword:
                return ParseReturnStatement();
            case SyntaxKind.WithKeyword:
                return ParseWithStatement();
            case SyntaxKind.SwitchKeyword:
                return ParseSwitchStatement();
            case SyntaxKind.ThrowKeyword:
                return ParseThrowStatement();
            case SyntaxKind.TryKeyword:
            // Include 'catch' and 'finally' for error recovery.
            // falls through
            case SyntaxKind.CatchKeyword:
            case SyntaxKind.FinallyKeyword:
                return ParseTryStatement();
            case SyntaxKind.DebuggerKeyword:
                return ParseDebuggerStatement();
            case SyntaxKind.AtToken:
                return ParseDeclaration();
            case SyntaxKind.AsyncKeyword:
            case SyntaxKind.InterfaceKeyword:
            case SyntaxKind.TypeKeyword:
            case SyntaxKind.ModuleKeyword:
            case SyntaxKind.NamespaceKeyword:
            case SyntaxKind.DeclareKeyword:
            case SyntaxKind.ConstKeyword:
            case SyntaxKind.EnumKeyword:
            case SyntaxKind.ExportKeyword:
            case SyntaxKind.ImportKeyword:
            case SyntaxKind.PrivateKeyword:
            case SyntaxKind.ProtectedKeyword:
            case SyntaxKind.PublicKeyword:
            case SyntaxKind.AbstractKeyword:
            case SyntaxKind.AccessorKeyword:
            case SyntaxKind.StaticKeyword:
            case SyntaxKind.ReadonlyKeyword:
            case SyntaxKind.GlobalKeyword:
                if (IsStartOfDeclaration())
                {
                    return ParseDeclaration();
                }

                break;
        }

        return ParseExpressionOrLabeledStatement();
    }

    /// <summary>ported from: src/compiler/parser.ts:7463-7465 — <c>isDeclareModifier</c>.</summary>
    private static bool IsDeclareModifier(Node modifier) => modifier.Kind == SyntaxKind.DeclareKeyword;

    /// <summary>ported from: src/compiler/parser.ts:7467-7489 — <c>parseDeclaration</c>.</summary>
    /// <remarks>
    /// <c>parseListElement</c> attempted to get the reused node at this position, but the ambient context flag was not yet
    /// set, so the node appeared not reusable in that context. With a <c>declare</c> modifier the modifiers are marked
    /// ambient, the reuse is retried under that context, and the worker runs inside it.
    /// </remarks>
    private IStatement ParseDeclaration()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var modifiers = ParseModifiers(/*allowDecorators*/ true);
        if (Core.Some(modifiers, IsDeclareModifier))
        {
            var node = TryReuseAmbientDeclaration(pos);
            if (node is not null)
            {
                return node;
            }

            foreach (var m in modifiers!)
            {
                m.Flags |= NodeFlags.Ambient;
            }

            return DoInsideOfContext(NodeFlags.Ambient, () => ParseDeclarationWorker(pos, hasJSDoc, modifiers));
        }

        return ParseDeclarationWorker(pos, hasJSDoc, modifiers);
    }

    /// <summary>ported from: src/compiler/parser.ts:7491-7500 — <c>tryReuseAmbientDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS passes the raw <c>parsingContext</c> mask to <c>currentNode</c>, whose type wants one
    /// <c>ParsingContext</c> — that is the "totally wrong" this function's own TODO calls out, because
    /// <c>parsingContext</c> is the OR of several <c>1 &lt;&lt; ParsingContext.XYZ</c> masks. The cast keeps the TS
    /// behaviour bit for bit instead of fixing it here.
    /// </remarks>
    private IStatement? TryReuseAmbientDeclaration(int pos)
    {
        return DoInsideOfContext<IStatement?>(NodeFlags.Ambient, () =>
        {
            var node = CurrentNode((ParsingContext)_parsingContexts, pos);
            if (node is not null)
            {
                return (IStatement)ConsumeNode(node);
            }

            return null;
        });
    }

    /// <summary>ported from: src/compiler/parser.ts:7502-7551 — <c>parseDeclarationWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the parameter is TS's <c>NodeArray&lt;ModifierLike&gt; | undefined</c>, which the port spells as
    /// <c>NodeArray&lt;Node&gt;?</c> — the shape <c>parseModifiers</c> and the declaration factories use (§12.5). TS's
    /// <c>export</c> arm consumes the keyword with <c>nextToken()</c> before dispatching on what follows; that is kept
    /// as a statement before the inner switch rather than folded into its cases.
    /// <para>
    /// TS's trailing <c>return undefined!</c> is <c>null!</c> in the port (see the file header).
    /// </para>
    /// </remarks>
    private IStatement ParseDeclarationWorker(int pos, bool hasJSDoc, NodeArray<Node>? modifiersIn)
    {
        switch (Token())
        {
            case SyntaxKind.VarKeyword:
            case SyntaxKind.LetKeyword:
            case SyntaxKind.ConstKeyword:
            case SyntaxKind.UsingKeyword:
                return ParseVariableStatement(pos, hasJSDoc, modifiersIn);
            case SyntaxKind.AwaitKeyword:
                if (!IsAwaitUsingDeclaration())
                {
                    break;
                }

                return ParseVariableStatement(pos, hasJSDoc, modifiersIn);
            case SyntaxKind.FunctionKeyword:
                return ParseFunctionDeclaration(pos, hasJSDoc, modifiersIn);
            case SyntaxKind.ClassKeyword:
                return ParseClassDeclaration(pos, hasJSDoc, modifiersIn);
            case SyntaxKind.InterfaceKeyword:
                return ParseInterfaceDeclaration(pos, hasJSDoc, modifiersIn);
            case SyntaxKind.TypeKeyword:
                return ParseTypeAliasDeclaration(pos, hasJSDoc, modifiersIn);
            case SyntaxKind.EnumKeyword:
                return ParseEnumDeclaration(pos, hasJSDoc, modifiersIn);
            case SyntaxKind.GlobalKeyword:
            case SyntaxKind.ModuleKeyword:
            case SyntaxKind.NamespaceKeyword:
                return ParseModuleDeclaration(pos, hasJSDoc, modifiersIn);
            case SyntaxKind.ImportKeyword:
                return ParseImportDeclarationOrImportEqualsDeclaration(pos, hasJSDoc, modifiersIn);
            case SyntaxKind.ExportKeyword:
                NextToken();
                switch (Token())
                {
                    case SyntaxKind.DefaultKeyword:
                    case SyntaxKind.EqualsToken:
                        return ParseExportAssignment(pos, hasJSDoc, modifiersIn);
                    case SyntaxKind.AsKeyword:
                        return ParseNamespaceExportDeclaration(pos, hasJSDoc, modifiersIn);
                    default:
                        return ParseExportDeclaration(pos, hasJSDoc, modifiersIn);
                }
        }

        if (modifiersIn is not null)
        {
            // We reached this point because we encountered decorators and/or modifiers and assumed a declaration
            // would follow. For recovery and error reporting purposes, return an incomplete declaration.
            var missing = CreateMissingNode<MissingDeclaration>(SyntaxKind.MissingDeclaration, /*reportAtCurrentPosition*/ true, Diagnostics.Declaration_expected);
            Utilities.SetTextRangePos(missing, pos);
            missing.Modifiers = modifiersIn;
            return missing;
        }

        return null!; // TODO: GH#18217
    }

    /// <summary>ported from: src/compiler/parser.ts:7553-7555 — <c>nextTokenIsStringLiteral</c>.</summary>
    private bool NextTokenIsStringLiteral() => NextToken() == SyntaxKind.StringLiteral;

    /// <summary>ported from: src/compiler/parser.ts:7557-7560 — <c>nextTokenIsFromKeywordOrEqualsToken</c>.</summary>
    private bool NextTokenIsFromKeywordOrEqualsToken()
    {
        NextToken();
        return Token() == SyntaxKind.FromKeyword || Token() == SyntaxKind.EqualsToken;
    }

    /// <summary>ported from: src/compiler/parser.ts:7562-7565 — <c>nextTokenIsIdentifierOrStringLiteralOnSameLine</c>.</summary>
    private bool NextTokenIsIdentifierOrStringLiteralOnSameLine()
    {
        NextToken();
        return !_scanner.HasPrecedingLineBreak() && (IsIdentifier() || Token() == SyntaxKind.StringLiteral);
    }

    /// <summary>ported from: src/compiler/parser.ts:7567-7579 — <c>parseFunctionBlockOrSemicolon</c>.</summary>
    /// <remarks>
    /// An overload body is a block; in a type context a <c>;</c> stands in for it, and outside one ASI may supply the
    /// terminator. Anything else is a block, which is where the diagnostic for a missing <c>{</c> is reported.
    /// </remarks>
    private Block? ParseFunctionBlockOrSemicolon(ParserSignatureFlags flags, DiagnosticMessage? diagnosticMessage = null)
    {
        if (Token() != SyntaxKind.OpenBraceToken)
        {
            if ((flags & ParserSignatureFlags.Type) != 0)
            {
                ParseTypeMemberSemicolon();
                return null;
            }

            if (CanParseSemicolon())
            {
                ParseSemicolon();
                return null;
            }
        }

        return ParseFunctionBlock(flags, diagnosticMessage);
    }
}
