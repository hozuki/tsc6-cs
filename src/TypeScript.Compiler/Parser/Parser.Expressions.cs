namespace TypeScript.Compiler;

// ported from: src/compiler/parser.ts:4965-6822 — the EXPRESSIONS section: the start-of-expression predicates,
// precedence climbing, arrow-function ambiguity handling, unary and update expressions, member/call chains, JSX,
// type assertions, tagged templates, array/object literals and function expressions.
/// <summary>
/// The expression grammar of <see cref="Parser"/>.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS's <c>PrimaryExpression</c>, <c>JsxChild</c>, <c>JsxAttributeValue</c>, <c>JsxTagNameExpression</c>,
/// <c>JsxAttributeName</c> and <c>ObjectLiteralElementLike</c> are unions whose members share no C# base narrower than
/// the class each of them really is. Each member therefore answers the widest C# type its members share
/// (<see cref="MemberExpression"/> for <c>PrimaryExpression</c>, <see cref="Node"/> for the JSX and object-literal
/// unions) and narrows at the use site, as §12.5 does for <c>EntityName</c>/<c>PropertyName</c>.
/// <para>
/// PORT-NOTE: the two places TS leans on a cast its type system makes harmless are spelled differently here, because
/// C#'s class hierarchy cannot express either lie:
/// <list type="bullet">
/// <item><description>
/// the JSX error-recovery tail of <see cref="ParseJsxElementOrSelfClosingElementOrFragment"/> returns a
/// <c>BinaryExpression</c> through TS's <c>as Node as JsxElement</c>. The port widens that member and
/// <see cref="ParseUpdateExpression"/> to <see cref="Expression"/>, which is the only type both a JSX node and that
/// comma sequence satisfy — no cast that could throw.
/// </description></item>
/// <item><description>
/// <see cref="ParsePropertyAccessExpressionRest"/>, <see cref="ParseElementAccessExpressionRest"/> and
/// <see cref="ParseMemberExpressionRest"/> pass the expression back out through the factory, which the port's
/// <see cref="NodeFactory"/> accepts as <see cref="Expression"/>; TS spells the same flow
/// <c>LeftHandSideExpression</c>.
/// </description></item>
/// </list>
/// </para>
/// </remarks>
internal sealed partial class Parser
{
    /// <summary>ported from: src/compiler/parser.ts:1446 — <c>disallowInAndDecoratorContext</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares this next to the state variables; the port keeps it in this slice because the only two
    /// readers (parser.ts:5588 and 6686) are here.
    /// </remarks>
    private const NodeFlags DisallowInAndDecoratorContext = NodeFlags.DisallowInContext | NodeFlags.DecoratorContext;

    //
    // Start-of-expression predicates
    //

    /// <summary>ported from: src/compiler/parser.ts:4966-4993 — <c>isStartOfLeftHandSideExpression</c>.</summary>
    private bool IsStartOfLeftHandSideExpression() => Token() switch
    {
        SyntaxKind.ThisKeyword
            or SyntaxKind.SuperKeyword
            or SyntaxKind.NullKeyword
            or SyntaxKind.TrueKeyword
            or SyntaxKind.FalseKeyword
            or SyntaxKind.NumericLiteral
            or SyntaxKind.BigIntLiteral
            or SyntaxKind.StringLiteral
            or SyntaxKind.NoSubstitutionTemplateLiteral
            or SyntaxKind.TemplateHead
            or SyntaxKind.OpenParenToken
            or SyntaxKind.OpenBracketToken
            or SyntaxKind.OpenBraceToken
            or SyntaxKind.FunctionKeyword
            or SyntaxKind.ClassKeyword
            or SyntaxKind.NewKeyword
            or SyntaxKind.SlashToken
            or SyntaxKind.SlashEqualsToken
            or SyntaxKind.Identifier => true,
        SyntaxKind.ImportKeyword => LookAheadBool(NextTokenIsOpenParenOrLessThanOrDot),
        _ => IsIdentifier(),
    };

    /// <summary>ported from: src/compiler/parser.ts:4995-5030 — <c>isStartOfExpression</c>.</summary>
    private bool IsStartOfExpression()
    {
        if (IsStartOfLeftHandSideExpression())
        {
            return true;
        }

        switch (Token())
        {
            case SyntaxKind.PlusToken:
            case SyntaxKind.MinusToken:
            case SyntaxKind.TildeToken:
            case SyntaxKind.ExclamationToken:
            case SyntaxKind.DeleteKeyword:
            case SyntaxKind.TypeOfKeyword:
            case SyntaxKind.VoidKeyword:
            case SyntaxKind.PlusPlusToken:
            case SyntaxKind.MinusMinusToken:
            case SyntaxKind.LessThanToken:
            case SyntaxKind.AwaitKeyword:
            case SyntaxKind.YieldKeyword:
            case SyntaxKind.PrivateIdentifier:
            case SyntaxKind.AtToken:
                // Yield/await always starts an expression.  Either it is an identifier (in which case
                // it is definitely an expression).  Or it's a keyword (either because we're in
                // a generator or async function, or in strict mode (or both)) and it started a yield or await expression.
                return true;
            default:
                // Error tolerance.  If we see the start of some binary operator, we consider
                // that the start of an expression.  That way we'll parse out a missing identifier,
                // give a good message about an identifier being missing, and then consume the
                // rest of the binary expression.
                if (IsBinaryOperator())
                {
                    return true;
                }

                return IsIdentifier();
        }
    }

    /// <summary>ported from: src/compiler/parser.ts:5032 — <c>isStartOfExpressionStatement</c>.</summary>
    private bool IsStartOfExpressionStatement()
        // As per the grammar, none of '{' or 'function' or 'class' can start an expression statement.
        => Token() != SyntaxKind.OpenBraceToken
            && Token() != SyntaxKind.FunctionKeyword
            && Token() != SyntaxKind.ClassKeyword
            && Token() != SyntaxKind.AtToken
            && IsStartOfExpression();

    //
    // Expressions
    //

    /// <summary>ported from: src/compiler/parser.ts:5041-5063 — <c>parseExpression</c>.</summary>
    private Expression ParseExpression()
    {
        // Expression[in]:
        //      AssignmentExpression[in]
        //      Expression[in] , AssignmentExpression[in]

        // clear the decorator context when parsing Expression, as it should be unambiguous when parsing a decorator
        var saveDecoratorContext = InDecoratorContext();
        if (saveDecoratorContext)
        {
            SetContextFlag(/*val*/ false, NodeFlags.DecoratorContext);
        }

        var pos = GetNodePos();
        var expr = ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ true);
        Node? operatorToken;
        while ((operatorToken = ParseOptionalToken(SyntaxKind.CommaToken)) is not null)
        {
            expr = MakeBinaryExpression(expr, operatorToken, ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ true), pos);
        }

        if (saveDecoratorContext)
        {
            SetContextFlag(/*val*/ true, NodeFlags.DecoratorContext);
        }

        return expr;
    }

    /// <summary>ported from: src/compiler/parser.ts:5065-5067 — <c>parseInitializer</c>.</summary>
    private Expression? ParseInitializer()
        => ParseOptional(SyntaxKind.EqualsToken) ? ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ true) : null;

    /// <summary>ported from: src/compiler/parser.ts:5069-5134 — <c>parseAssignmentExpressionOrHigher</c>.</summary>
    private Expression ParseAssignmentExpressionOrHigher(bool allowReturnTypeInArrowFunction)
    {
        //  AssignmentExpression[in,yield]:
        //      1) ConditionalExpression[?in,?yield]
        //      2) LeftHandSideExpression = AssignmentExpression[?in,?yield]
        //      3) LeftHandSideExpression AssignmentOperator AssignmentExpression[?in,?yield]
        //      4) ArrowFunctionExpression[?in,?yield]
        //      5) AsyncArrowFunctionExpression[in,yield,await]
        //      6) [+Yield] YieldExpression[?In]
        //
        // Note: for ease of implementation we treat productions '2' and '3' as the same thing.
        // (i.e. they're both BinaryExpressions with an assignment operator in it).

        // First, do the simple check if we have a YieldExpression (production '6').
        if (IsYieldExpression())
        {
            return ParseYieldExpression();
        }

        // Then, check if we have an arrow function (production '4' and '5') that starts with a parenthesized
        // parameter list or is an async arrow function.
        // AsyncArrowFunctionExpression:
        //      1) async[no LineTerminator here]AsyncArrowBindingIdentifier[?Yield][no LineTerminator here]=>AsyncConciseBody[?In]
        //      2) CoverCallExpressionAndAsyncArrowHead[?Yield, ?Await][no LineTerminator here]=>AsyncConciseBody[?In]
        // Production (1) of AsyncArrowFunctionExpression is parsed in "tryParseAsyncSimpleArrowFunctionExpression".
        // And production (2) is parsed in "tryParseParenthesizedArrowFunctionExpression".
        //
        // If we do successfully parse arrow-function, we must *not* recurse for productions 1, 2 or 3. An ArrowFunction is
        // not a LeftHandSideExpression, nor does it start a ConditionalExpression.  So we are done
        // with AssignmentExpression if we see one.
        var arrowExpression = TryParseParenthesizedArrowFunctionExpression(allowReturnTypeInArrowFunction)
            ?? TryParseAsyncSimpleArrowFunctionExpression(allowReturnTypeInArrowFunction);
        if (arrowExpression is not null)
        {
            return arrowExpression;
        }

        // Now try to see if we're in production '1', '2' or '3'.  A conditional expression can
        // start with a LogicalOrExpression, while the assignment productions can only start with
        // LeftHandSideExpressions.
        //
        // So, first, we try to just parse out a BinaryExpression.  If we get something that is a
        // LeftHandSide or higher, then we can try to parse out the assignment expression part.
        // Otherwise, we try to parse out the conditional expression bit.  We want to allow any
        // binary expression here, so we pass in the 'lowest' precedence here so that it matches
        // and consumes anything.
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var expr = ParseBinaryExpressionOrHigher(OperatorPrecedence.Lowest);

        // To avoid a look-ahead, we did not handle the case of an arrow function with a single un-parenthesized
        // parameter ('x => ...') above. We handle it here by checking if the parsed expression was a single
        // identifier and the current token is an arrow.
        if (expr.Kind == SyntaxKind.Identifier && Token() == SyntaxKind.EqualsGreaterThanToken)
        {
            return ParseSimpleArrowFunctionExpression(pos, (Identifier)expr, allowReturnTypeInArrowFunction, hasJSDoc, /*asyncModifier*/ null);
        }

        // Now see if we might be in cases '2' or '3'.
        // If the expression was a LHS expression, and we have an assignment operator, then
        // we're in '2' or '3'. Consume the assignment and return.
        //
        // Note: we call reScanGreaterToken so that we get an appropriately merged token
        // for cases like `> > =` becoming `>>=`
        if (Utilities.IsLeftHandSideExpression(expr) && Utilities.IsAssignmentOperator(ReScanGreaterToken()))
        {
            return MakeBinaryExpression(expr, ParseTokenNode<Token>(), ParseAssignmentExpressionOrHigher(allowReturnTypeInArrowFunction), pos);
        }

        // It wasn't an assignment or a lambda.  This is a conditional expression:
        return ParseConditionalExpressionRest(expr, pos, allowReturnTypeInArrowFunction);
    }

    /// <summary>ported from: src/compiler/parser.ts:5136-5162 — <c>isYieldExpression</c>.</summary>
    private bool IsYieldExpression()
    {
        if (Token() == SyntaxKind.YieldKeyword)
        {
            // If we have a 'yield' keyword, and this is a context where yield expressions are
            // allowed, then definitely parse out a yield expression.
            if (InYieldContext())
            {
                return true;
            }

            // We're in a context where 'yield expr' is not allowed.  However, if we can
            // definitely tell that the user was trying to parse a 'yield expr' and not
            // just a normal expr that start with a 'yield' identifier, then parse out
            // a 'yield expr'.  We can then report an error later that they are only
            // allowed in generator expressions.
            //
            // for example, if we see 'yield(foo)', then we'll have to treat that as an
            // invocation expression of something called 'yield'.  However, if we have
            // 'yield foo' then that is not legal as a normal expression, so we can
            // definitely recognize this as a yield expression.
            //
            // for now we just check if the next token is an identifier.  More heuristics
            // can be added here later as necessary.  We just need to make sure that we
            // don't accidentally consume something legal.
            return LookAheadBool(NextTokenIsIdentifierOrKeywordOrLiteralOnSameLine);
        }

        return false;
    }

    /// <summary>ported from: src/compiler/parser.ts:5164-5167 — <c>nextTokenIsIdentifierOnSameLine</c>.</summary>
    private bool NextTokenIsIdentifierOnSameLine()
    {
        NextToken();
        return !_scanner.HasPrecedingLineBreak() && IsIdentifier();
    }

    /// <summary>ported from: src/compiler/parser.ts:5169-5195 — <c>parseYieldExpression</c>.</summary>
    private YieldExpression ParseYieldExpression()
    {
        var pos = GetNodePos();

        // YieldExpression[In] :
        //      yield
        //      yield [no LineTerminator here] [Lexical goal InputElementRegExp]AssignmentExpression[?In, Yield]
        //      yield [no LineTerminator here] * [Lexical goal InputElementRegExp]AssignmentExpression[?In, Yield]
        NextToken();

        if (!_scanner.HasPrecedingLineBreak() && (Token() == SyntaxKind.AsteriskToken || IsStartOfExpression()))
        {
            return FinishNode(
                _factory.CreateYieldExpression(
                    (PunctuationToken?)ParseOptionalToken(SyntaxKind.AsteriskToken),
                    ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ true)),
                pos);
        }
        else
        {
            // if the next token is not on the same line as yield.  or we don't have an '*' or
            // the start of an expression, then this is just a simple "yield" expression.
            return FinishNode(_factory.CreateYieldExpression(/*asteriskToken*/ null, /*expression*/ null), pos);
        }
    }

    /// <summary>ported from: src/compiler/parser.ts:5197-5214 — <c>parseSimpleArrowFunctionExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS writes <c>!!asyncModifier</c>, which is "the modifier list is not <c>undefined</c>" — an empty
    /// array would be truthy there, so <c>is not null</c> is what the port asks for.
    /// </remarks>
    private ArrowFunction ParseSimpleArrowFunctionExpression(
        int pos,
        Identifier identifier,
        bool allowReturnTypeInArrowFunction,
        bool hasJSDoc,
        NodeArray<ModifierToken>? asyncModifier = null)
    {
        Debug.Assert(Token() == SyntaxKind.EqualsGreaterThanToken, "parseSimpleArrowFunctionExpression should only have been called if we had a =>");
        var parameter = _factory.CreateParameterDeclaration(
            /*modifiers*/ null,
            /*dotDotDotToken*/ null,
            identifier,
            /*questionToken*/ null,
            /*type*/ null,
            /*initializer*/ null);
        FinishNode(parameter, identifier.Pos);

        var parameters = CreateNodeArray<ParameterDeclaration>([parameter], parameter.Pos, parameter.End);
        var equalsGreaterThanToken = (PunctuationToken)ParseExpectedToken(SyntaxKind.EqualsGreaterThanToken);
        var body = ParseArrowFunctionExpressionBody(/*isAsync*/ asyncModifier is not null, allowReturnTypeInArrowFunction);
        var node = _factory.CreateArrowFunction(asyncModifier, /*typeParameters*/ null, parameters, /*type*/ null, equalsGreaterThanToken, body);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:5216 — <c>tryParseParenthesizedArrowFunctionExpression</c>.</summary>
    private Expression? TryParseParenthesizedArrowFunctionExpression(bool allowReturnTypeInArrowFunction)
    {
        var triState = IsParenthesizedArrowFunctionExpression();
        if (triState == Tristate.False)
        {
            // It's definitely not a parenthesized arrow function expression.
            return null;
        }

        // If we definitely have an arrow function, then we can just parse one, not requiring a
        // following => or { token. Otherwise, we *might* have an arrow function.  Try to parse
        // it out, but don't allow any ambiguity, and return 'undefined' if this could be an
        // expression instead.
        return triState == Tristate.True
            ? ParseParenthesizedArrowFunctionExpression(/*allowAmbiguity*/ true, /*allowReturnTypeInArrowFunction*/ true)
            : TryParse(() => ParsePossibleParenthesizedArrowFunctionExpression(allowReturnTypeInArrowFunction));
    }

    /// <summary>ported from: src/compiler/parser.ts:5236-5249 — <c>isParenthesizedArrowFunctionExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the three answers are TS's <c>Tristate</c>:
    /// <code>
    /// True  -&gt; We definitely expect a parenthesized arrow function here.
    /// False -&gt; There *cannot* be a parenthesized arrow function here.
    /// Unknown -&gt; There *might* be a parenthesized arrow function here. Speculatively look ahead to be sure, and roll
    ///            back if not.
    /// </code>
    /// </remarks>
    private Tristate IsParenthesizedArrowFunctionExpression()
    {
        if (Token() == SyntaxKind.OpenParenToken || Token() == SyntaxKind.LessThanToken || Token() == SyntaxKind.AsyncKeyword)
        {
            return LookAhead(IsParenthesizedArrowFunctionExpressionWorker);
        }

        if (Token() == SyntaxKind.EqualsGreaterThanToken)
        {
            // ERROR RECOVERY TWEAK:
            // If we see a standalone => try to parse it as an arrow function expression as that's
            // likely what the user intended to write.
            return Tristate.True;
        }

        // Definitely not a parenthesized arrow function.
        return Tristate.False;
    }

    /// <summary>ported from: src/compiler/parser.ts:5251-5379 — <c>isParenthesizedArrowFunctionExpressionWorker</c>.</summary>
    private Tristate IsParenthesizedArrowFunctionExpressionWorker()
    {
        if (Token() == SyntaxKind.AsyncKeyword)
        {
            NextToken();
            if (_scanner.HasPrecedingLineBreak())
            {
                return Tristate.False;
            }

            if (Token() != SyntaxKind.OpenParenToken && Token() != SyntaxKind.LessThanToken)
            {
                return Tristate.False;
            }
        }

        var first = Token();
        var second = NextToken();

        if (first == SyntaxKind.OpenParenToken)
        {
            if (second == SyntaxKind.CloseParenToken)
            {
                // Simple cases: "() =>", "(): ", and "() {".
                // This is an arrow function with no parameters.
                // The last one is not actually an arrow function,
                // but this is probably what the user intended.
                var third = NextToken();
                switch (third)
                {
                    case SyntaxKind.EqualsGreaterThanToken:
                    case SyntaxKind.ColonToken:
                    case SyntaxKind.OpenBraceToken:
                        return Tristate.True;
                    default:
                        return Tristate.False;
                }
            }

            // If encounter "([" or "({", this could be the start of a binding pattern.
            // Examples:
            //      ([ x ]) => { }
            //      ({ x }) => { }
            //      ([ x ])
            //      ({ x })
            if (second == SyntaxKind.OpenBracketToken || second == SyntaxKind.OpenBraceToken)
            {
                return Tristate.Unknown;
            }

            // Simple case: "(..."
            // This is an arrow function with a rest parameter.
            if (second == SyntaxKind.DotDotDotToken)
            {
                return Tristate.True;
            }

            // Check for "(xxx yyy", where xxx is a modifier and yyy is an identifier. This
            // isn't actually allowed, but we want to treat it as a lambda so we can provide
            // a good error message.
            if (Utilities.IsModifierKind(second) && second != SyntaxKind.AsyncKeyword && LookAheadBool(NextTokenIsIdentifier))
            {
                if (NextToken() == SyntaxKind.AsKeyword)
                {
                    // https://github.com/microsoft/TypeScript/issues/44466
                    return Tristate.False;
                }

                return Tristate.True;
            }

            // If we had "(" followed by something that's not an identifier,
            // then this definitely doesn't look like a lambda.  "this" is not
            // valid, but we want to parse it and then give a semantic error.
            if (!IsIdentifier() && second != SyntaxKind.ThisKeyword)
            {
                return Tristate.False;
            }

            switch (NextToken())
            {
                case SyntaxKind.ColonToken:
                    // If we have something like "(a:", then we must have a
                    // type-annotated parameter in an arrow function expression.
                    return Tristate.True;
                case SyntaxKind.QuestionToken:
                    NextToken();
                    // If we have "(a?:" or "(a?," or "(a?=" or "(a?)" then it is definitely a lambda.
                    if (Token() == SyntaxKind.ColonToken || Token() == SyntaxKind.CommaToken || Token() == SyntaxKind.EqualsToken || Token() == SyntaxKind.CloseParenToken)
                    {
                        return Tristate.True;
                    }

                    // Otherwise it is definitely not a lambda.
                    return Tristate.False;
                case SyntaxKind.CommaToken:
                case SyntaxKind.EqualsToken:
                case SyntaxKind.CloseParenToken:
                    // If we have "(a," or "(a=" or "(a)" this *could* be an arrow function
                    return Tristate.Unknown;
            }

            // It is definitely not an arrow function
            return Tristate.False;
        }
        else
        {
            Debug.Assert(first == SyntaxKind.LessThanToken);

            // If we have "<" not followed by an identifier,
            // then this definitely is not an arrow function.
            if (!IsIdentifier() && Token() != SyntaxKind.ConstKeyword)
            {
                return Tristate.False;
            }

            // JSX overrides
            if (_languageVariant == LanguageVariant.JSX)
            {
                var isArrowFunctionInJsx = LookAheadBool(() =>
                {
                    ParseOptional(SyntaxKind.ConstKeyword);
                    var third = NextToken();
                    if (third == SyntaxKind.ExtendsKeyword)
                    {
                        var fourth = NextToken();
                        switch (fourth)
                        {
                            case SyntaxKind.EqualsToken:
                            case SyntaxKind.GreaterThanToken:
                            case SyntaxKind.SlashToken:
                                return false;
                            default:
                                return true;
                        }
                    }
                    else if (third == SyntaxKind.CommaToken || third == SyntaxKind.EqualsToken)
                    {
                        return true;
                    }

                    return false;
                });

                if (isArrowFunctionInJsx)
                {
                    return Tristate.True;
                }

                return Tristate.False;
            }

            // This *could* be a parenthesized arrow function.
            return Tristate.Unknown;
        }
    }

    /// <summary>ported from: src/compiler/parser.ts:5381-5393 — <c>parsePossibleParenthesizedArrowFunctionExpression</c>.</summary>
    private ArrowFunction? ParsePossibleParenthesizedArrowFunctionExpression(bool allowReturnTypeInArrowFunction)
    {
        var tokenPos = _scanner.GetTokenStart();
        if (_notParenthesizedArrow?.Contains(tokenPos) == true)
        {
            return null;
        }

        var result = ParseParenthesizedArrowFunctionExpression(/*allowAmbiguity*/ false, allowReturnTypeInArrowFunction);
        if (result is null)
        {
            (_notParenthesizedArrow ??= []).Add(tokenPos);
        }

        return result;
    }

    /// <summary>ported from: src/compiler/parser.ts:5395-5407 — <c>tryParseAsyncSimpleArrowFunctionExpression</c>.</summary>
    private ArrowFunction? TryParseAsyncSimpleArrowFunctionExpression(bool allowReturnTypeInArrowFunction)
    {
        // We do a check here so that we won't be doing unnecessarily call to "lookAhead"
        if (Token() == SyntaxKind.AsyncKeyword)
        {
            if (LookAhead(IsUnParenthesizedAsyncArrowFunctionWorker) == Tristate.True)
            {
                var pos = GetNodePos();
                var hasJSDoc = HasPrecedingJSDocComment();
                var asyncModifier = ParseModifiersForArrowFunction();
                var expr = ParseBinaryExpressionOrHigher(OperatorPrecedence.Lowest);
                return ParseSimpleArrowFunctionExpression(pos, (Identifier)expr, allowReturnTypeInArrowFunction, hasJSDoc, asyncModifier);
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/parser.ts:5409-5428 — <c>isUnParenthesizedAsyncArrowFunctionWorker</c>.</summary>
    private Tristate IsUnParenthesizedAsyncArrowFunctionWorker()
    {
        // AsyncArrowFunctionExpression:
        //      1) async[no LineTerminator here]AsyncArrowBindingIdentifier[?Yield][no LineTerminator here]=>AsyncConciseBody[?In]
        //      2) CoverCallExpressionAndAsyncArrowHead[?Yield, ?Await][no LineTerminator here]=>AsyncConciseBody[?In]
        if (Token() == SyntaxKind.AsyncKeyword)
        {
            NextToken();
            // If the "async" is followed by "=>" token then it is not a beginning of an async arrow-function
            // but instead a simple arrow-function which will be parsed inside "parseAssignmentExpressionOrHigher"
            if (_scanner.HasPrecedingLineBreak() || Token() == SyntaxKind.EqualsGreaterThanToken)
            {
                return Tristate.False;
            }

            // Check for un-parenthesized AsyncArrowFunction
            var expr = ParseBinaryExpressionOrHigher(OperatorPrecedence.Lowest);
            if (!_scanner.HasPrecedingLineBreak() && expr.Kind == SyntaxKind.Identifier && Token() == SyntaxKind.EqualsGreaterThanToken)
            {
                return Tristate.True;
            }
        }

        return Tristate.False;
    }

    /// <summary>ported from: src/compiler/parser.ts:5430-5531 — <c>parseParenthesizedArrowFunctionExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares <c>parseParametersWorker</c> as a pair of overloads whose <c>allowAmbiguity</c> literal
    /// picks a non-optional result; with a runtime <c>bool</c> the port has one member answering
    /// <c>NodeArray&lt;ParameterDeclaration&gt;?</c>, so the call that TS proves non-null is asserted here.
    /// <para>
    /// PORT-NOTE: <c>isJSDocFunctionType</c> is written as the port's predicate over the node class the union's common
    /// base reaches; TS narrows <c>TypeNode</c> with it directly.
    /// </para>
    /// </remarks>
    private ArrowFunction? ParseParenthesizedArrowFunctionExpression(bool allowAmbiguity, bool allowReturnTypeInArrowFunction)
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var modifiers = ParseModifiersForArrowFunction();
        var isAsync = Core.Some(modifiers, NodeTests.IsAsyncModifier) ? ParserSignatureFlags.Await : ParserSignatureFlags.None;
        // Arrow functions are never generators.
        //
        // If we're speculatively parsing a signature for a parenthesized arrow function, then
        // we have to have a complete parameter list.  Otherwise we might see something like
        // a => (b => c)
        // And think that "(b =>" was actually a parenthesized arrow function with a missing
        // close paren.
        var typeParameters = ParseTypeParameters();

        NodeArray<ParameterDeclaration> parameters;
        if (!ParseExpected(SyntaxKind.OpenParenToken))
        {
            if (!allowAmbiguity)
            {
                return null;
            }

            parameters = CreateMissingList<ParameterDeclaration>();
        }
        else
        {
            if (!allowAmbiguity)
            {
                var maybeParameters = ParseParametersWorker(isAsync, /*allowAmbiguity*/ false);
                if (maybeParameters is null)
                {
                    return null;
                }

                parameters = maybeParameters;
            }
            else
            {
                parameters = ParseParametersWorker(isAsync, /*allowAmbiguity*/ true)!;
            }

            if (!ParseExpected(SyntaxKind.CloseParenToken) && !allowAmbiguity)
            {
                return null;
            }
        }

        var hasReturnColon = Token() == SyntaxKind.ColonToken;
        var type = ParseReturnType(SyntaxKind.ColonToken, /*isType*/ false);
        if (type is not null && !allowAmbiguity && TypeHasArrowFunctionBlockingParseError(type))
        {
            return null;
        }

        // Parsing a signature isn't enough.
        // Parenthesized arrow signatures often look like other valid expressions.
        // For instance:
        //  - "(x = 10)" is an assignment expression parsed as a signature with a default parameter value.
        //  - "(x,y)" is a comma expression parsed as a signature with two parameters.
        //  - "a ? (b): c" will have "(b):" parsed as a signature with a return type annotation.
        //  - "a ? (b): function() {}" will too, since function() is a valid JSDoc function type.
        //  - "a ? (b): (function() {})" as well, but inside of a parenthesized type with an arbitrary amount of nesting.
        //
        // So we need just a bit of lookahead to ensure that it can only be a signature.
        var unwrappedType = type;
        // PORT-NOTE: TS writes `while (unwrappedType?.kind === SyntaxKind.ParenthesizedType)`; the port spells the
        // same test as an explicit null check so that the narrowing below is provable without a null-forgiving cast.
        while (unwrappedType is not null && unwrappedType.Kind == SyntaxKind.ParenthesizedType)
        {
            unwrappedType = ((ParenthesizedTypeNode)unwrappedType).Type; // Skip parens if need be
        }

        var hasJSDocFunctionType = unwrappedType is not null && NodeTests.IsJSDocFunctionType((Node)unwrappedType);
        if (!allowAmbiguity && Token() != SyntaxKind.EqualsGreaterThanToken && (hasJSDocFunctionType || Token() != SyntaxKind.OpenBraceToken))
        {
            // Returning undefined here will cause our caller to rewind to where we started from.
            return null;
        }

        // If we have an arrow, then try to parse the body. Even if not, try to parse if we
        // have an opening brace, just in case we're in an error state.
        var lastToken = Token();
        var equalsGreaterThanToken = (PunctuationToken)ParseExpectedToken(SyntaxKind.EqualsGreaterThanToken);
        var body = lastToken == SyntaxKind.EqualsGreaterThanToken || lastToken == SyntaxKind.OpenBraceToken
            ? ParseArrowFunctionExpressionBody(Core.Some(modifiers, NodeTests.IsAsyncModifier), allowReturnTypeInArrowFunction)
            : ParseIdentifier();

        // Given:
        //     x ? y => ({ y }) : z => ({ z })
        // We try to parse the body of the first arrow function by looking at:
        //     ({ y }) : z => ({ z })
        // This is a valid arrow function with "z" as the return type.
        //
        // But, if we're in the true side of a conditional expression, this colon
        // terminates the expression, so we cannot allow a return type if we aren't
        // certain whether or not the preceding text was parsed as a parameter list.
        //
        // For example,
        //     a() ? (b: number, c?: string): void => d() : e
        // is determined by isParenthesizedArrowFunctionExpression to unambiguously
        // be an arrow expression, so we allow a return type.
        if (!allowReturnTypeInArrowFunction && hasReturnColon)
        {
            // However, if the arrow function we were able to parse is followed by another colon
            // as in:
            //     a ? (x): string => x : null
            // Then allow the arrow function, and treat the second colon as terminating
            // the conditional expression. It's okay to do this because this code would
            // be a syntax error in JavaScript (as the second colon shouldn't be there).
            if (Token() != SyntaxKind.ColonToken)
            {
                return null;
            }
        }

        var node = _factory.CreateArrowFunction(modifiers, typeParameters, parameters, type, equalsGreaterThanToken, body);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:5533-5560 — <c>parseArrowFunctionExpressionBody</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers <c>Block | Expression</c>; the two share no C# base narrower than <see cref="Node"/>,
    /// which is what the arrow-function factory takes and what this answers.
    /// </remarks>
    private Node ParseArrowFunctionExpressionBody(bool isAsync, bool allowReturnTypeInArrowFunction)
    {
        if (Token() == SyntaxKind.OpenBraceToken)
        {
            return ParseFunctionBlock(isAsync ? ParserSignatureFlags.Await : ParserSignatureFlags.None);
        }

        if (Token() != SyntaxKind.SemicolonToken
            && Token() != SyntaxKind.FunctionKeyword
            && Token() != SyntaxKind.ClassKeyword
            && IsStartOfStatement()
            && !IsStartOfExpressionStatement())
        {
            // Check if we got a plain statement (i.e. no expression-statements, no function/class expressions/declarations)
            //
            // Here we try to recover from a potential error situation in the case where the
            // user meant to supply a block. For example, if the user wrote:
            //
            //  a =>
            //      let v = 0;
            //  }
            //
            // they may be missing an open brace.  Check to see if that's the case so we can
            // try to recover better.  If we don't do this, then the next close curly we see may end
            // up preemptively closing the containing construct.
            //
            // Note: even when 'IgnoreMissingOpenBrace' is passed, parseBody will still error.
            return ParseFunctionBlock(ParserSignatureFlags.IgnoreMissingOpenBrace | (isAsync ? ParserSignatureFlags.Await : ParserSignatureFlags.None));
        }

        var savedYieldContext = InYieldContext();
        SetContextFlag(/*val*/ false, NodeFlags.YieldContext);
        var savedTopLevel = _topLevel;
        _topLevel = false;
        var node = isAsync
            ? DoInAwaitContext(() => ParseAssignmentExpressionOrHigher(allowReturnTypeInArrowFunction))
            : DoOutsideOfAwaitContext(() => ParseAssignmentExpressionOrHigher(allowReturnTypeInArrowFunction));
        _topLevel = savedTopLevel;
        SetContextFlag(savedYieldContext, NodeFlags.YieldContext);
        return node;
    }

    /// <summary>ported from: src/compiler/parser.ts:5574-5596 — <c>parseConditionalExpressionRest</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS assigns <c>colonToken</c> inside the factory call's argument list. The port hoists that
    /// assignment onto its own line, which evaluates at the same point (the arguments of a call are evaluated left to
    /// right, so the colon is read after the when-true expression and before the when-false one).
    /// </remarks>
    private Expression ParseConditionalExpressionRest(Expression leftOperand, int pos, bool allowReturnTypeInArrowFunction)
    {
        // Note: we are passed in an expression which was produced from parseBinaryExpressionOrHigher.
        var questionToken = (PunctuationToken?)ParseOptionalToken(SyntaxKind.QuestionToken);
        if (questionToken is null)
        {
            return leftOperand;
        }

        // Note: we explicitly 'allowIn' in the whenTrue part of the condition expression, and
        // we do not that for the 'whenFalse' part.
        var whenTrue = DoOutsideOfContext(DisallowInAndDecoratorContext, () => ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ false));
        var colonToken = (PunctuationToken)ParseExpectedToken(SyntaxKind.ColonToken);
        var whenFalse = Utilities.NodeIsPresent(colonToken)
            ? ParseAssignmentExpressionOrHigher(allowReturnTypeInArrowFunction)
            : CreateMissingNode<Identifier>(SyntaxKind.Identifier, /*reportAtCurrentPosition*/ false, Diagnostics._0_expected, ScannerUtilities.TokenToString(SyntaxKind.ColonToken));
        return FinishNode(_factory.CreateConditionalExpression(leftOperand, questionToken, whenTrue, colonToken, whenFalse), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:5598-5602 — <c>parseBinaryExpressionOrHigher</c>.</summary>
    private Expression ParseBinaryExpressionOrHigher(OperatorPrecedence precedence)
    {
        var pos = GetNodePos();
        var leftOperand = ParseUnaryExpressionOrHigher();
        return ParseBinaryExpressionRest(precedence, leftOperand, pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:5604-5606 — <c>isInOrOfKeyword</c>.</summary>
    private static bool IsInOrOfKeyword(SyntaxKind t)
        => t == SyntaxKind.InKeyword || t == SyntaxKind.OfKeyword;

    /// <summary>ported from: src/compiler/parser.ts:5608-5671 — <c>parseBinaryExpressionRest</c>.</summary>
    private Expression ParseBinaryExpressionRest(OperatorPrecedence precedence, Expression leftOperand, int pos)
    {
        while (true)
        {
            // We either have a binary operator here, or we're finished.  We call
            // reScanGreaterToken so that we merge token sequences like > and = into >=

            ReScanGreaterToken();
            var newPrecedence = Utilities.GetBinaryOperatorPrecedence(Token());

            // Check the precedence to see if we should "take" this operator
            // - For left associative operator (all operator but **), consume the operator,
            //   recursively call the function below, and parse binaryExpression as a rightOperand
            //   of the caller if the new precedence of the operator is greater then or equal to the current precedence.
            //   For example:
            //      a - b - c;
            //            ^token; leftOperand = b. Return b to the caller as a rightOperand
            //      a * b - c
            //            ^token; leftOperand = b. Return b to the caller as a rightOperand
            //      a - b * c;
            //            ^token; leftOperand = b. Return b * c to the caller as a rightOperand
            // - For right associative operator (**), consume the operator, recursively call the function
            //   and parse binaryExpression as a rightOperand of the caller if the new precedence of
            //   the operator is strictly grater than the current precedence
            //   For example:
            //      a ** b ** c;
            //             ^^token; leftOperand = b. Return b ** c to the caller as a rightOperand
            //      a - b ** c;
            //            ^^token; leftOperand = b. Return b ** c to the caller as a rightOperand
            //      a ** b - c
            //             ^token; leftOperand = b. Return b to the caller as a rightOperand
            var consumeCurrentOperator = Token() == SyntaxKind.AsteriskAsteriskToken
                ? newPrecedence >= precedence
                : newPrecedence > precedence;

            if (!consumeCurrentOperator)
            {
                break;
            }

            if (Token() == SyntaxKind.InKeyword && InDisallowInContext())
            {
                break;
            }

            if (Token() == SyntaxKind.AsKeyword || Token() == SyntaxKind.SatisfiesKeyword)
            {
                // Make sure we *do* perform ASI for constructs like this:
                //    var x = foo
                //    as (Bar)
                // This should be parsed as an initialized variable, followed
                // by a function call to 'as' with the argument 'Bar'
                if (_scanner.HasPrecedingLineBreak())
                {
                    break;
                }
                else
                {
                    var keywordKind = Token();
                    NextToken();
                    leftOperand = keywordKind == SyntaxKind.SatisfiesKeyword
                        ? MakeSatisfiesExpression(leftOperand, ParseType())
                        : MakeAsExpression(leftOperand, ParseType());
                }
            }
            else
            {
                leftOperand = MakeBinaryExpression(leftOperand, ParseTokenNode<Token>(), ParseBinaryExpressionOrHigher(newPrecedence), pos);
            }
        }

        return leftOperand;
    }

    /// <summary>ported from: src/compiler/parser.ts:5673-5679 — <c>isBinaryOperator</c>.</summary>
    private bool IsBinaryOperator()
    {
        if (InDisallowInContext() && Token() == SyntaxKind.InKeyword)
        {
            return false;
        }

        return Utilities.GetBinaryOperatorPrecedence(Token()) > 0;
    }

    /// <summary>ported from: src/compiler/parser.ts:5681-5683 — <c>makeSatisfiesExpression</c>.</summary>
    private SatisfiesExpression MakeSatisfiesExpression(Expression left, ITypeNode right)
        => FinishNode(_factory.CreateSatisfiesExpression(left, right), left.Pos);

    /// <summary>ported from: src/compiler/parser.ts:5685-5687 — <c>makeBinaryExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>operatorToken: BinaryOperatorToken</c> has no C# member; the node is passed as the wide
    /// <see cref="Node"/>, which is also what the factory's <c>operator</c> parameter takes.
    /// </remarks>
    private BinaryExpression MakeBinaryExpression(Expression left, Node operatorToken, Expression right, int pos)
        => FinishNode(_factory.CreateBinaryExpression(left, operatorToken, right), pos);

    /// <summary>ported from: src/compiler/parser.ts:5689-5691 — <c>makeAsExpression</c>.</summary>
    private AsExpression MakeAsExpression(Expression left, ITypeNode right)
        => FinishNode(_factory.CreateAsExpression(left, right), left.Pos);

    //
    // Unary and update expressions
    //

    /// <summary>ported from: src/compiler/parser.ts:5693-5696 — <c>parsePrefixUnaryExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>token() as PrefixUnaryOperator</c> is a cast between <c>SyntaxKind</c> subsets; the port's
    /// factory takes the kind as-is.
    /// </remarks>
    private PrefixUnaryExpression ParsePrefixUnaryExpression()
    {
        var pos = GetNodePos();
        return FinishNode(_factory.CreatePrefixUnaryExpression(Token(), NextTokenAnd(ParseSimpleUnaryExpression)), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:5698-5701 — <c>parseDeleteExpression</c>.</summary>
    private DeleteExpression ParseDeleteExpression()
    {
        var pos = GetNodePos();
        return FinishNode(_factory.CreateDeleteExpression(NextTokenAnd(ParseSimpleUnaryExpression)), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:5703-5706 — <c>parseTypeOfExpression</c>.</summary>
    private TypeOfExpression ParseTypeOfExpression()
    {
        var pos = GetNodePos();
        return FinishNode(_factory.CreateTypeOfExpression(NextTokenAnd(ParseSimpleUnaryExpression)), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:5708-5711 — <c>parseVoidExpression</c>.</summary>
    private VoidExpression ParseVoidExpression()
    {
        var pos = GetNodePos();
        return FinishNode(_factory.CreateVoidExpression(NextTokenAnd(ParseSimpleUnaryExpression)), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:5713-5724 — <c>isAwaitExpression</c>.</summary>
    private bool IsAwaitExpression()
    {
        if (Token() == SyntaxKind.AwaitKeyword)
        {
            if (InAwaitContext())
            {
                return true;
            }

            // here we are using similar heuristics as 'isYieldExpression'
            return LookAheadBool(NextTokenIsIdentifierOrKeywordOrLiteralOnSameLine);
        }

        return false;
    }

    /// <summary>ported from: src/compiler/parser.ts:5726-5729 — <c>parseAwaitExpression</c>.</summary>
    private AwaitExpression ParseAwaitExpression()
    {
        var pos = GetNodePos();
        return FinishNode(_factory.CreateAwaitExpression(NextTokenAnd(ParseSimpleUnaryExpression)), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:5731-5780 — <c>parseUnaryExpressionOrHigher</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers <c>UnaryExpression | BinaryExpression</c>. Both derive from <see cref="Expression"/>, which
    /// is what this answers — the union's common base (§12.5). The one caller reads the kind, never the static type.
    /// <para>
    /// TS: Parse ES7 exponential expression and await expression.
    /// <code>
    /// ES7 ExponentiationExpression:
    ///      1) UnaryExpression[?Yield]
    ///      2) UpdateExpression[?Yield] ** ExponentiationExpression[?Yield]
    /// </code>
    /// </para>
    /// </remarks>
    private Expression ParseUnaryExpressionOrHigher()
    {
        // ES7 UpdateExpression:
        //      1) LeftHandSideExpression[?Yield]
        //      2) LeftHandSideExpression[?Yield][no LineTerminator here]++
        //      3) LeftHandSideExpression[?Yield][no LineTerminator here]--
        //      4) ++UnaryExpression[?Yield]
        //      5) --UnaryExpression[?Yield]
        if (IsUpdateExpression())
        {
            var pos = GetNodePos();
            var updateExpression = ParseUpdateExpression();
            return Token() == SyntaxKind.AsteriskAsteriskToken
                ? ParseBinaryExpressionRest(Utilities.GetBinaryOperatorPrecedence(Token()), updateExpression, pos)
                : updateExpression;
        }

        // ES7 UnaryExpression:
        //      1) UpdateExpression[?yield]
        //      2) delete UpdateExpression[?yield]
        //      3) void UpdateExpression[?yield]
        //      4) typeof UpdateExpression[?yield]
        //      5) + UpdateExpression[?yield]
        //      6) - UpdateExpression[?yield]
        //      7) ~ UpdateExpression[?yield]
        //      8) ! UpdateExpression[?yield]
        var unaryOperator = Token();
        var simpleUnaryExpression = ParseSimpleUnaryExpression();
        if (Token() == SyntaxKind.AsteriskAsteriskToken)
        {
            var pos = ScannerUtilities.SkipTrivia(_sourceText, simpleUnaryExpression.Pos);
            var end = simpleUnaryExpression.End;
            if (simpleUnaryExpression.Kind == SyntaxKind.TypeAssertionExpression)
            {
                ParseErrorAt(pos, end, Diagnostics.A_type_assertion_expression_is_not_allowed_in_the_left_hand_side_of_an_exponentiation_expression_Consider_enclosing_the_expression_in_parentheses);
            }
            else
            {
                Debug.Assert(Utilities.IsKeywordOrPunctuation(unaryOperator));
                ParseErrorAt(pos, end, Diagnostics.An_unary_expression_with_the_0_operator_is_not_allowed_in_the_left_hand_side_of_an_exponentiation_expression_Consider_enclosing_the_expression_in_parentheses, ScannerUtilities.TokenToString(unaryOperator));
            }
        }

        return simpleUnaryExpression;
    }

    /// <summary>ported from: src/compiler/parser.ts:5782-5827 — <c>parseSimpleUnaryExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: see <see cref="ParseUpdateExpression"/> — this answers <see cref="Expression"/> rather than TS's
    /// <c>UnaryExpression</c> because the JSX update-expression path can hand back a comma sequence (TS's own
    /// <c>as Node as JsxElement</c>).
    /// <para>
    /// TS: Parse ES7 simple-unary expression or higher:
    /// <code>
    /// ES7 UnaryExpression:
    ///      1) UpdateExpression[?yield]
    ///      2) delete UnaryExpression[?yield]
    ///      3) void UnaryExpression[?yield]
    ///      4) typeof UnaryExpression[?yield]
    ///      5) + UnaryExpression[?yield]
    ///      6) - UnaryExpression[?yield]
    ///      7) ~ UnaryExpression[?yield]
    ///      8) ! UnaryExpression[?yield]
    ///      9) [+Await] await UnaryExpression[?yield]
    /// </code>
    /// </para>
    /// </remarks>
    private Expression ParseSimpleUnaryExpression()
    {
        switch (Token())
        {
            case SyntaxKind.PlusToken:
            case SyntaxKind.MinusToken:
            case SyntaxKind.TildeToken:
            case SyntaxKind.ExclamationToken:
                return ParsePrefixUnaryExpression();
            case SyntaxKind.DeleteKeyword:
                return ParseDeleteExpression();
            case SyntaxKind.TypeOfKeyword:
                return ParseTypeOfExpression();
            case SyntaxKind.VoidKeyword:
                return ParseVoidExpression();
            case SyntaxKind.LessThanToken:
                // Just like in parseUpdateExpression, we need to avoid parsing type assertions when
                // in JSX and we see an expression like "+ <foo> bar".
                if (_languageVariant == LanguageVariant.JSX)
                {
                    return ParseJsxElementOrSelfClosingElementOrFragment(/*inExpressionContext*/ true, /*topInvalidNodePosition*/ null, /*openingTag*/ null, /*mustBeUnary*/ true);
                }

                // This is modified UnaryExpression grammar in TypeScript
                //  UnaryExpression (modified):
                //      < type > UnaryExpression
                return ParseTypeAssertion();
            case SyntaxKind.AwaitKeyword:
                if (IsAwaitExpression())
                {
                    return ParseAwaitExpression();
                }

                // falls through
                goto default;
            default:
                return ParseUpdateExpression();
        }
    }

    /// <summary>ported from: src/compiler/parser.ts:5829-5862 — <c>isUpdateExpression</c>.</summary>
    /// <remarks>
    /// TS: Check if the current token can possibly be an ES7 increment expression.
    /// <code>
    /// ES7 UpdateExpression:
    ///      LeftHandSideExpression[?Yield]
    ///      LeftHandSideExpression[?Yield][no LineTerminator here]++
    ///      LeftHandSideExpression[?Yield][no LineTerminator here]--
    ///      ++LeftHandSideExpression[?Yield]
    ///      --LeftHandSideExpression[?Yield]
    /// </code>
    /// </remarks>
    private bool IsUpdateExpression()
    {
        // This function is called inside parseUnaryExpression to decide
        // whether to call parseSimpleUnaryExpression or call parseUpdateExpression directly
        switch (Token())
        {
            case SyntaxKind.PlusToken:
            case SyntaxKind.MinusToken:
            case SyntaxKind.TildeToken:
            case SyntaxKind.ExclamationToken:
            case SyntaxKind.DeleteKeyword:
            case SyntaxKind.TypeOfKeyword:
            case SyntaxKind.VoidKeyword:
            case SyntaxKind.AwaitKeyword:
                return false;
            case SyntaxKind.LessThanToken:
                // If we are not in JSX context, we are parsing TypeAssertion which is an UnaryExpression
                if (_languageVariant != LanguageVariant.JSX)
                {
                    return false;
                }

                // We are in JSX context and the token is part of JSXElement.
                // falls through
                goto default;
            default:
                return true;
        }
    }

    /// <summary>ported from: src/compiler/parser.ts:5864-5895 — <c>parseUpdateExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers <c>UpdateExpression</c>. The JSX branch returns
    /// <see cref="ParseJsxElementOrSelfClosingElementOrFragment"/>, which answers <see cref="Expression"/> because of the
    /// error-recovery tail that TS launders through <c>as Node as JsxElement</c> — a <c>BinaryExpression</c>, which is
    /// not an <c>UpdateExpression</c>. Widening the answer to <see cref="Expression"/> is behaviourally identical and
    /// avoids a cast that would throw on exactly the malformed input that path exists for.
    /// <para>
    /// TS: Parse ES7 UpdateExpression. UpdateExpression is used instead of ES6's PostFixExpression.
    /// <code>
    /// ES7 UpdateExpression[yield]:
    ///      1) LeftHandSideExpression[?yield]
    ///      2) LeftHandSideExpression[?yield] [[no LineTerminator here]]++
    ///      3) LeftHandSideExpression[?yield] [[no LineTerminator here]]--
    ///      4) ++LeftHandSideExpression[?yield]
    ///      5) --LeftHandSideExpression[?yield]
    /// In TypeScript (2), (3) are parsed as PostfixUnaryExpression. (4), (5) are parsed as PrefixUnaryExpression
    /// </code>
    /// </para>
    /// </remarks>
    private Expression ParseUpdateExpression()
    {
        if (Token() == SyntaxKind.PlusPlusToken || Token() == SyntaxKind.MinusMinusToken)
        {
            var pos = GetNodePos();
            return FinishNode(_factory.CreatePrefixUnaryExpression(Token(), NextTokenAnd(ParseLeftHandSideExpressionOrHigher)), pos);
        }
        else if (_languageVariant == LanguageVariant.JSX && Token() == SyntaxKind.LessThanToken && LookAheadBool(NextTokenIsIdentifierOrKeywordOrGreaterThan))
        {
            // JSXElement is part of primaryExpression
            return ParseJsxElementOrSelfClosingElementOrFragment(/*inExpressionContext*/ true);
        }

        var expression = ParseLeftHandSideExpressionOrHigher();

        Debug.Assert(Utilities.IsLeftHandSideExpression(expression));
        if ((Token() == SyntaxKind.PlusPlusToken || Token() == SyntaxKind.MinusMinusToken) && !_scanner.HasPrecedingLineBreak())
        {
            var @operator = Token();
            NextToken();
            return FinishNode(_factory.CreatePostfixUnaryExpression(expression, @operator), expression.Pos);
        }

        return expression;
    }

    //
    // Left-hand-side expressions
    //

    /// <summary>ported from: src/compiler/parser.ts:5897-5968 — <c>parseLeftHandSideExpressionOrHigher</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares the local and the answer <c>MemberExpression</c>; the port's chain members
    /// (<see cref="ParseMemberExpressionRest"/>, <see cref="ParseCallExpressionRest"/>) take and answer
    /// <see cref="Expression"/>, because the call rest feeds call expressions back through them — wider than TS's
    /// declared <c>MemberExpression</c> and the type the declaration slice also calls this member with.
    /// </remarks>
    private Expression ParseLeftHandSideExpressionOrHigher()
    {
        // Original Ecma:
        // LeftHandSideExpression: See 11.2
        //      NewExpression
        //      CallExpression
        //
        // Our simplification:
        //
        // LeftHandSideExpression: See 11.2
        //      MemberExpression
        //      CallExpression
        //
        // See comment in parseMemberExpressionOrHigher on how we replaced NewExpression with
        // MemberExpression to make our lives easier.
        //
        // to best understand the below code, it's important to see how CallExpression expands
        // out into its own productions:
        //
        // CallExpression:
        //      MemberExpression Arguments
        //      CallExpression Arguments
        //      CallExpression[Expression]
        //      CallExpression.IdentifierName
        //      import (AssignmentExpression)
        //      super Arguments
        //      super.IdentifierName
        //
        // Because of the recursion in these calls, we need to bottom out first. There are three
        // bottom out states we can run into: 1) We see 'super' which must start either of
        // the last two CallExpression productions. 2) We see 'import' which must start import call.
        // 3)we have a MemberExpression which either completes the LeftHandSideExpression,
        // or starts the beginning of the first four CallExpression productions.
        var pos = GetNodePos();
        // PORT-NOTE: TS's local is `MemberExpression`; the port's local is `Expression` for the same reason the answer
        // is (see the remarks above) — `ParseCallExpressionRest` hands call expressions back out.
        Expression expression;
        if (Token() == SyntaxKind.ImportKeyword)
        {
            if (LookAheadBool(NextTokenIsOpenParenOrLessThan))
            {
                // We don't want to eagerly consume all import keyword as import call expression so we look ahead to find "("
                // For example:
                //      var foo3 = require("subfolder
                //      import * as foo1 from "module-from-node
                // We want this import to be a statement rather than import call expression
                _sourceFlags |= NodeFlags.PossiblyContainsDynamicImport;
                expression = ParseTokenNode<MemberExpression>();
            }
            else if (LookAheadBool(NextTokenIsDot))
            {
                // This is an 'import.*' metaproperty (i.e. 'import.meta')
                NextToken(); // advance past the 'import'
                NextToken(); // advance past the dot
                expression = FinishNode(_factory.CreateMetaProperty(SyntaxKind.ImportKeyword, ParseIdentifierName()), pos);

                if (((MetaProperty)expression).Name!.EscapedText == "defer")
                {
                    if (Token() == SyntaxKind.OpenParenToken || Token() == SyntaxKind.LessThanToken)
                    {
                        _sourceFlags |= NodeFlags.PossiblyContainsDynamicImport;
                    }
                }
                else
                {
                    _sourceFlags |= NodeFlags.PossiblyContainsImportMeta;
                }
            }
            else
            {
                expression = ParseMemberExpressionOrHigher();
            }
        }
        else
        {
            expression = Token() == SyntaxKind.SuperKeyword ? ParseSuperExpression() : ParseMemberExpressionOrHigher();
        }

        // Now, we *may* be complete.  However, we might have consumed the start of a
        // CallExpression or OptionalExpression.  As such, we need to consume the rest
        // of it here to be complete.
        return ParseCallExpressionRest(pos, expression);
    }

    /// <summary>ported from: src/compiler/parser.ts:5970-6021 — <c>parseMemberExpressionOrHigher</c>.</summary>
    /// <remarks>
    /// TS: Note: to make our lives simpler, we decompose the NewExpression productions and place ObjectCreationExpression
    /// and FunctionExpression into PrimaryExpression. like so:
    /// <code>
    ///   PrimaryExpression : See 11.1
    ///      this
    ///      Identifier
    ///      Literal
    ///      ArrayLiteral
    ///      ObjectLiteral
    ///      (Expression)
    ///      FunctionExpression
    ///      new MemberExpression Arguments?
    ///
    ///   MemberExpression : See 11.2
    ///      PrimaryExpression
    ///      MemberExpression[Expression]
    ///      MemberExpression.IdentifierName
    ///
    ///   CallExpression : See 11.2
    ///      MemberExpression
    ///      CallExpression Arguments
    ///      CallExpression[Expression]
    ///      CallExpression.IdentifierName
    /// </code>
    /// Technically this is ambiguous.  i.e. CallExpression defines "CallExpression Arguments". If you see: "new Foo()" then
    /// that could be treated as a single ObjectCreationExpression, or it could be treated as the invocation of "new Foo".
    /// We disambiguate that in code (to match the original grammar) by making sure that if we see an
    /// ObjectCreationExpression we always consume arguments if they are there. So we treat "new Foo()" as an object
    /// creation only, and not at all as an invocation.  Another way to think about this is that for every "new" that we
    /// see, we will consume an argument list if it is there as part of the *associated* object creation node.  Any
    /// additional argument lists we see, will become invocation expressions.
    /// <para>
    /// Because there are no other places in the grammar now that refer to FunctionExpression or ObjectCreationExpression,
    /// it is safe to push down into the PrimaryExpression production. Because CallExpression and MemberExpression are
    /// left recursive, we need to bottom out of the recursion immediately.  So we parse out a primary expression to start
    /// with.
    /// </para>
    /// </remarks>
    private LeftHandSideExpression ParseMemberExpressionOrHigher()
    {
        var pos = GetNodePos();
        var expression = ParsePrimaryExpression();
        // PORT-NOTE: TS declares `MemberExpression` here and writes
        // `return parseMemberExpressionRest(pos, parsePrimaryExpression(), true)` below. That declared answer cannot
        // hold: the `!` branch of that member wraps its input in a `NonNullExpression`, which TS's own type graph makes a
        // sibling of `MemberExpression` (both extend `LeftHandSideExpression`), so `const x = y!;` answers a node that
        // TS's `MemberExpression` type does not describe. An earlier revision of this member carried TS's cast anyway
        // and crashed with `InvalidCastException: NonNullExpression → MemberExpression` on the first real source file
        // the differential run parsed. The answer here is the honest upper bound of the branches this member can reach:
        // the primary expression itself, a property or element access, a tagged template, an
        // `ExpressionWithTypeArguments`, or a `NonNullExpression`.
        return (LeftHandSideExpression)ParseMemberExpressionRest(pos, expression, /*allowOptionalChain*/ true);
    }

    /// <summary>ported from: src/compiler/parser.ts:6023-6046 — <c>parseSuperExpression</c>.</summary>
    private MemberExpression ParseSuperExpression()
    {
        var pos = GetNodePos();
        var expression = ParseTokenNode<MemberExpression>();
        if (Token() == SyntaxKind.LessThanToken)
        {
            var startPos = GetNodePos();
            var typeArguments = TryParse(ParseTypeArgumentsInExpression);
            if (typeArguments is not null)
            {
                ParseErrorAt(startPos, GetNodePos(), Diagnostics.super_may_not_use_type_arguments);
                if (!IsTemplateStartOfTaggedTemplate())
                {
                    expression = _factory.CreateExpressionWithTypeArguments(expression, typeArguments);
                }
            }
        }

        if (Token() == SyntaxKind.OpenParenToken || Token() == SyntaxKind.DotToken || Token() == SyntaxKind.OpenBracketToken)
        {
            return expression;
        }

        // If we have seen "super" it must be followed by '(' or '.'.
        // If it wasn't then just try to parse out a '.' and report an error.
        ParseExpectedToken(SyntaxKind.DotToken, Diagnostics.super_must_be_followed_by_an_argument_list_or_member_access);
        // private names will never work with `super` (`super.#foo`), but that's a semantic error, not syntactic
        return FinishNode(_factory.CreatePropertyAccessExpression(expression, ParseRightSideOfDot(/*allowIdentifierNames*/ true, /*allowPrivateIdentifiers*/ true, /*allowUnicodeEscapeSequenceInIdentifierName*/ true)), pos);
    }

    //
    // JSX
    //

    /// <summary>ported from: src/compiler/parser.ts:6048-6124 — <c>parseJsxElementOrSelfClosingElementOrFragment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers <c>JsxElement | JsxSelfClosingElement | JsxFragment</c> and, on the error-recovery path at
    /// the end, returns a <c>BinaryExpression</c> through <c>as Node as JsxElement</c>. The three JSX classes are
    /// <see cref="MemberExpression"/>s and a comma <c>BinaryExpression</c> is not, so the port widens the answer to the
    /// union's common base <see cref="Expression"/>; every cast below is between JSX classes and the same union.
    /// <para>
    /// PORT-NOTE: TS calls the parser-level <c>scanJsxText()</c> (and <c>scanner.scanJsxToken()</c>) at the places that
    /// must scan *forward* from the current position, and <c>scanner.reScanJsxToken()</c> at the one place that must
    /// rewind to <c>fullStartPos</c> (the child loop). The port's <c>Parser.ScanJsxText</c> (Parser.cs:516) is the
    /// scanning twin of <c>reScanJsxToken</c>, so this slice calls the scanner's two members directly and names which is
    /// which at each site.
    /// </para>
    /// </remarks>
    private Expression ParseJsxElementOrSelfClosingElementOrFragment(
        bool inExpressionContext,
        int? topInvalidNodePosition = null,
        Expression? openingTag = null,
        bool mustBeUnary = false)
    {
        var pos = GetNodePos();
        var opening = ParseJsxOpeningOrSelfClosingElementOrOpeningFragment(inExpressionContext);
        Expression result;
        if (opening.Kind == SyntaxKind.JsxOpeningElement)
        {
            var children = ParseJsxChildren(opening);
            JsxClosingElement closingElement;

            var lastChild = Core.LastOrUndefined(children);
            // PORT-NOTE: TS writes `lastChild?.kind === SyntaxKind.JsxElement`; the explicit null check is the same
            // condition and makes the narrowing provable for the casts below.
            if (lastChild is not null && lastChild.Kind == SyntaxKind.JsxElement
                && !TagNamesAreEquivalent(((JsxElement)lastChild).OpeningElement!.TagName!, ((JsxElement)lastChild).ClosingElement!.TagName!)
                && TagNamesAreEquivalent(((JsxOpeningElement)opening).TagName!, ((JsxElement)lastChild).ClosingElement!.TagName!))
            {
                // when an unclosed JsxOpeningElement incorrectly parses its parent's JsxClosingElement,
                // restructure (<div>(...<span>...</div>)) --> (<div>(...<span>...</>)</div>)
                // (no need to error; the parent will error)
                var end = ((JsxElement)lastChild).Children!.End;
                var newLast = FinishNode(
                    _factory.CreateJsxElement(
                        ((JsxElement)lastChild).OpeningElement,
                        ((JsxElement)lastChild).Children!,
                        FinishNode(_factory.CreateJsxClosingElement(FinishNode(_factory.CreateIdentifier(""), end, end)), end, end)),
                    ((JsxElement)lastChild).OpeningElement!.Pos,
                    end);

                children = CreateNodeArray([.. children.Take(children.Count - 1), newLast], children.Pos, end);
                closingElement = ((JsxElement)lastChild).ClosingElement!;
            }
            else
            {
                closingElement = ParseJsxClosingElement((JsxOpeningElement)opening, inExpressionContext);
                if (!TagNamesAreEquivalent(((JsxOpeningElement)opening).TagName!, closingElement.TagName!))
                {
                    if (openingTag is not null && NodeTests.IsJsxOpeningElement(openingTag) && TagNamesAreEquivalent(closingElement.TagName!, ((JsxOpeningElement)openingTag).TagName!))
                    {
                        // opening incorrectly matched with its parent's closing -- put error on opening
                        ParseErrorAtRange(((JsxOpeningElement)opening).TagName!, Diagnostics.JSX_element_0_has_no_corresponding_closing_tag, Utilities.GetTextOfNodeFromSourceText(_sourceText, ((JsxOpeningElement)opening).TagName!));
                    }
                    else
                    {
                        // other opening/closing mismatches -- put error on closing
                        ParseErrorAtRange(closingElement.TagName!, Diagnostics.Expected_corresponding_JSX_closing_tag_for_0, Utilities.GetTextOfNodeFromSourceText(_sourceText, ((JsxOpeningElement)opening).TagName!));
                    }
                }
            }

            result = FinishNode(_factory.CreateJsxElement((JsxOpeningElement)opening, children, closingElement), pos);
        }
        else if (opening.Kind == SyntaxKind.JsxOpeningFragment)
        {
            result = FinishNode(_factory.CreateJsxFragment((JsxOpeningFragment)opening, ParseJsxChildren(opening), ParseJsxClosingFragment(inExpressionContext)), pos);
        }
        else
        {
            Debug.Assert(opening.Kind == SyntaxKind.JsxSelfClosingElement);
            // Nothing else to do for self-closing elements
            result = opening;
        }

        // If the user writes the invalid code '<div></div><div></div>' in an expression context (i.e. not wrapped in
        // an enclosing tag), we'll naively try to parse   ^ this as a 'less than' operator and the remainder of the tag
        // as garbage, which will cause the formatter to badly mangle the JSX. Perform a speculative parse of a JSX
        // element if we see a < token so that we can wrap it in a synthetic binary expression so the formatter
        // does less damage and we can report a better error.
        // Since JSX elements are invalid < operands anyway, this lookahead parse will only occur in error scenarios
        // of one sort or another.
        // If we are in a unary context, we can't do this recovery; the binary expression we return here is not
        // a valid UnaryExpression and will cause problems later.
        if (!mustBeUnary && inExpressionContext && Token() == SyntaxKind.LessThanToken)
        {
            var topBadPos = topInvalidNodePosition ?? result.Pos;
            var invalidElement = TryParse(() => ParseJsxElementOrSelfClosingElementOrFragment(/*inExpressionContext*/ true, topBadPos));
            if (invalidElement is not null)
            {
                var operatorToken = CreateMissingNode<Node>(SyntaxKind.CommaToken, /*reportAtCurrentPosition*/ false);
                Utilities.SetTextRangePosWidth(operatorToken, invalidElement.Pos, 0);
                ParseErrorAt(ScannerUtilities.SkipTrivia(_sourceText, topBadPos), invalidElement.End, Diagnostics.JSX_expressions_must_have_one_parent_element);
                return FinishNode(_factory.CreateBinaryExpression(result, operatorToken, invalidElement), pos);
            }
        }

        return result;
    }

    /// <summary>ported from: src/compiler/parser.ts:6126-6131 — <c>parseJsxText</c>.</summary>
    private JsxText ParseJsxText()
    {
        var pos = GetNodePos();
        var node = _factory.CreateJsxText(_scanner.GetTokenValue(), Token() == SyntaxKind.JsxTextAllWhiteSpaces);
        _currentToken = _scanner.ScanJsxToken();
        return FinishNode(node, pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:6133-6162 — <c>parseJsxChild</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>JsxTokenSyntaxKind</c> (the JSX subset of <see cref="SyntaxKind"/>), which the
    /// port has no member for; the caller has just assigned that kind to <c>currentToken</c>, so the wide kind is what
    /// crosses the boundary. TS's <c>openingTag</c> is <c>JsxOpeningElement | JsxOpeningFragment</c>, whose common C#
    /// base is <see cref="Expression"/>.
    /// </remarks>
    private Node? ParseJsxChild(Expression openingTag, SyntaxKind jsxToken)
    {
        switch (jsxToken)
        {
            case SyntaxKind.EndOfFileToken:
                // If we hit EOF, issue the error at the tag that lacks the closing element
                // rather than at the end of the file (which is useless)
                if (NodeTests.IsJsxOpeningFragment(openingTag))
                {
                    ParseErrorAtRange(openingTag, Diagnostics.JSX_fragment_has_no_corresponding_closing_tag);
                }
                else
                {
                    // We want the error span to cover only 'Foo.Bar' in < Foo.Bar >
                    // or to cover only 'Foo' in < Foo >
                    var tag = ((JsxOpeningElement)openingTag).TagName!;
                    var start = Math.Min(ScannerUtilities.SkipTrivia(_sourceText, tag.Pos), tag.End);
                    ParseErrorAt(start, tag.End, Diagnostics.JSX_element_0_has_no_corresponding_closing_tag, Utilities.GetTextOfNodeFromSourceText(_sourceText, tag));
                }

                return null;
            case SyntaxKind.LessThanSlashToken:
            case SyntaxKind.ConflictMarkerTrivia:
                return null;
            case SyntaxKind.JsxText:
            case SyntaxKind.JsxTextAllWhiteSpaces:
                return ParseJsxText();
            case SyntaxKind.OpenBraceToken:
                return ParseJsxExpression(/*inExpressionContext*/ false);
            case SyntaxKind.LessThanToken:
                return ParseJsxElementOrSelfClosingElementOrFragment(/*inExpressionContext*/ false, /*topInvalidNodePosition*/ null, openingTag);
            default:
                Debug.AssertNever(jsxToken);
                return null;
        }
    }

    /// <summary>ported from: src/compiler/parser.ts:6164-6187 — <c>parseJsxChildren</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: this is the one JSX site that calls <c>scanner.reScanJsxToken()</c> — the rewind to
    /// <c>fullStartPos</c> — because the child loop re-reads the currently-current JSX token.
    /// </remarks>
    private NodeArray<Node> ParseJsxChildren(Expression openingTag)
    {
        var list = new List<Node>();
        var listPos = GetNodePos();
        var saveParsingContext = _parsingContexts;
        _parsingContexts |= 1 << (int)ParsingContext.JsxChildren;

        while (true)
        {
            var child = ParseJsxChild(openingTag, _currentToken = _scanner.ReScanJsxToken());
            if (child is null) break;
            list.Add(child);
            if (NodeTests.IsJsxOpeningElement(openingTag)
                && child.Kind == SyntaxKind.JsxElement
                && !TagNamesAreEquivalent(((JsxElement)child).OpeningElement!.TagName!, ((JsxElement)child).ClosingElement!.TagName!)
                && TagNamesAreEquivalent(((JsxOpeningElement)openingTag).TagName!, ((JsxElement)child).ClosingElement!.TagName!))
            {
                // stop after parsing a mismatched child like <div>...(<span></div>) in order to reattach the </div> higher
                break;
            }
        }

        _parsingContexts = saveParsingContext;
        return CreateNodeArray(list, listPos);
    }

    /// <summary>ported from: src/compiler/parser.ts:6189-6192 — <c>parseJsxAttributes</c>.</summary>
    private JsxAttributes ParseJsxAttributes()
    {
        var pos = GetNodePos();
        return FinishNode(_factory.CreateJsxAttributes(ParseList(ParsingContext.JsxAttributes, ParseJsxAttribute)), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:6194-6232 — <c>parseJsxOpeningOrSelfClosingElementOrOpeningFragment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: answers the union <c>JsxOpeningElement | JsxSelfClosingElement | JsxOpeningFragment</c> as its common
    /// base <see cref="Expression"/> (§12.5).
    /// </remarks>
    private Expression ParseJsxOpeningOrSelfClosingElementOrOpeningFragment(bool inExpressionContext)
    {
        var pos = GetNodePos();

        ParseExpected(SyntaxKind.LessThanToken);

        if (Token() == SyntaxKind.GreaterThanToken)
        {
            // See below for explanation of scanJsxText
            _currentToken = _scanner.ScanJsxToken();
            return FinishNode(_factory.CreateJsxOpeningFragment(), pos);
        }

        var tagName = ParseJsxElementName();
        var typeArguments = (_contextFlags & NodeFlags.JavaScriptFile) == 0 ? TryParseTypeArguments() : null;
        var attributes = ParseJsxAttributes();

        Expression node;

        if (Token() == SyntaxKind.GreaterThanToken)
        {
            // Closing tag, so scan the immediately-following text with the JSX scanning instead
            // of regular scanning to avoid treating illegal characters (e.g. '#') as immediate
            // scanning errors
            _currentToken = _scanner.ScanJsxToken();
            node = _factory.CreateJsxOpeningElement(tagName, typeArguments, attributes);
        }
        else
        {
            ParseExpected(SyntaxKind.SlashToken);
            if (ParseExpected(SyntaxKind.GreaterThanToken, /*diagnosticMessage*/ null, /*shouldAdvance*/ false))
            {
                // manually advance the scanner in order to look for jsx text inside jsx
                if (inExpressionContext)
                {
                    NextToken();
                }
                else
                {
                    _currentToken = _scanner.ScanJsxToken();
                }
            }

            node = _factory.CreateJsxSelfClosingElement(tagName, typeArguments, attributes);
        }

        return FinishNode(node, pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:6234-6250 — <c>parseJsxElementName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's answer is <c>JsxTagNameExpression</c>; the union is held as <see cref="Node"/> because a
    /// <c>JsxNamespacedName</c> is not an <see cref="Expression"/>. Every other member is, so the one
    /// <c>factoryCreatePropertyAccessExpression</c> call casts — and the namespaced name has already returned by then.
    /// </remarks>
    private Node ParseJsxElementName()
    {
        var pos = GetNodePos();
        // JsxElement can have name in the form of
        //      propertyAccessExpression
        //      primaryExpression in the form of an identifier and "this" keyword
        // We can't just simply use parseLeftHandSideExpressionOrHigher because then we will start consider class,function etc as a keyword
        // We only want to consider "this" as a primaryExpression
        var initialExpression = ParseJsxTagName();
        if (NodeTests.IsJsxNamespacedName(initialExpression))
        {
            return initialExpression; // `a:b.c` is invalid syntax, don't even look for the `.` if we parse `a:b`, and let `parseAttribute` report "unexpected :" instead.
        }

        var expression = initialExpression;
        while (ParseOptional(SyntaxKind.DotToken))
        {
            expression = FinishNode(_factory.CreatePropertyAccessExpression((Expression)expression, ParseRightSideOfDot(/*allowIdentifierNames*/ true, /*allowPrivateIdentifiers*/ false, /*allowUnicodeEscapeSequenceInIdentifierName*/ false)), pos);
        }

        return expression;
    }

    /// <summary>ported from: src/compiler/parser.ts:6252-6263 — <c>parseJsxTagName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers <c>Identifier | JsxNamespacedName | ThisExpression</c>; the port keeps the union's common
    /// base <see cref="Node"/> because <c>JsxNamespacedName</c> is not a member of the <c>Expression</c> chain.
    /// </remarks>
    private Node ParseJsxTagName()
    {
        var pos = GetNodePos();
        ScanJsxIdentifier();

        var isThis = Token() == SyntaxKind.ThisKeyword;
        var tagName = ParseIdentifierNameErrorOnUnicodeEscapeSequence();
        if (ParseOptional(SyntaxKind.ColonToken))
        {
            ScanJsxIdentifier();
            return FinishNode(_factory.CreateJsxNamespacedName(tagName, ParseIdentifierNameErrorOnUnicodeEscapeSequence()), pos);
        }

        return isThis ? FinishNode(_factory.CreateToken(SyntaxKind.ThisKeyword), pos) : tagName;
    }

    /// <summary>ported from: src/compiler/parser.ts:6265-6292 — <c>parseJsxExpression</c>.</summary>
    private JsxExpression? ParseJsxExpression(bool inExpressionContext)
    {
        var pos = GetNodePos();
        if (!ParseExpected(SyntaxKind.OpenBraceToken))
        {
            return null;
        }

        PunctuationToken? dotDotDotToken = null;
        Expression? expression = null;
        if (Token() != SyntaxKind.CloseBraceToken)
        {
            if (!inExpressionContext)
            {
                dotDotDotToken = (PunctuationToken?)ParseOptionalToken(SyntaxKind.DotDotDotToken);
            }

            // Only an AssignmentExpression is valid here per the JSX spec,
            // but we can unambiguously parse a comma sequence and provide
            // a better error message in grammar checking.
            expression = ParseExpression();
        }

        if (inExpressionContext)
        {
            ParseExpected(SyntaxKind.CloseBraceToken);
        }
        else
        {
            if (ParseExpected(SyntaxKind.CloseBraceToken, /*diagnosticMessage*/ null, /*shouldAdvance*/ false))
            {
                _currentToken = _scanner.ScanJsxToken();
            }
        }

        return FinishNode(_factory.CreateJsxExpression(dotDotDotToken, expression), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:6294-6301 — <c>parseJsxAttribute</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers <c>JsxAttribute | JsxSpreadAttribute</c>, which share no C# base narrower than
    /// <see cref="Node"/> — which is also what a JSX attribute list holds.
    /// </remarks>
    private Node ParseJsxAttribute()
    {
        if (Token() == SyntaxKind.OpenBraceToken)
        {
            return ParseJsxSpreadAttribute();
        }

        var pos = GetNodePos();
        return FinishNode(_factory.CreateJsxAttribute(ParseJsxAttributeName(), ParseJsxAttributeValue()), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:6303-6317 — <c>parseJsxAttributeValue</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers <c>StringLiteral | JsxExpression | JsxElement | JsxSelfClosingElement | JsxFragment</c>;
    /// every one of them is an <see cref="Expression"/>, which is also the type of the slot it fills.
    /// </remarks>
    private Expression? ParseJsxAttributeValue()
    {
        if (Token() == SyntaxKind.EqualsToken)
        {
            if (ScanJsxAttributeValue() == SyntaxKind.StringLiteral)
            {
                return ParseLiteralNode();
            }

            if (Token() == SyntaxKind.OpenBraceToken)
            {
                return ParseJsxExpression(/*inExpressionContext*/ true);
            }

            if (Token() == SyntaxKind.LessThanToken)
            {
                return ParseJsxElementOrSelfClosingElementOrFragment(/*inExpressionContext*/ true);
            }

            ParseErrorAtCurrentToken(Diagnostics.or_JSX_element_expected);
        }

        return null;
    }

    /// <summary>ported from: src/compiler/parser.ts:6319-6329 — <c>parseJsxAttributeName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers <c>Identifier | JsxNamespacedName</c>; the port keeps <see cref="Node"/> because a
    /// <c>JsxNamespacedName</c> is not an <see cref="Expression"/>.
    /// </remarks>
    private Node ParseJsxAttributeName()
    {
        var pos = GetNodePos();
        ScanJsxIdentifier();

        var attrName = ParseIdentifierNameErrorOnUnicodeEscapeSequence();
        if (ParseOptional(SyntaxKind.ColonToken))
        {
            ScanJsxIdentifier();
            return FinishNode(_factory.CreateJsxNamespacedName(attrName, ParseIdentifierNameErrorOnUnicodeEscapeSequence()), pos);
        }

        return attrName;
    }

    /// <summary>ported from: src/compiler/parser.ts:6331-6338 — <c>parseJsxSpreadAttribute</c>.</summary>
    private JsxSpreadAttribute ParseJsxSpreadAttribute()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.OpenBraceToken);
        ParseExpected(SyntaxKind.DotDotDotToken);
        var expression = ParseExpression();
        ParseExpected(SyntaxKind.CloseBraceToken);
        return FinishNode(_factory.CreateJsxSpreadAttribute(expression), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:6340-6354 — <c>parseJsxClosingElement</c>.</summary>
    private JsxClosingElement ParseJsxClosingElement(JsxOpeningElement open, bool inExpressionContext)
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.LessThanSlashToken);
        var tagName = ParseJsxElementName();
        if (ParseExpected(SyntaxKind.GreaterThanToken, /*diagnosticMessage*/ null, /*shouldAdvance*/ false))
        {
            // manually advance the scanner in order to look for jsx text inside jsx
            if (inExpressionContext || !TagNamesAreEquivalent(open.TagName!, tagName))
            {
                NextToken();
            }
            else
            {
                _currentToken = _scanner.ScanJsxToken();
            }
        }

        return FinishNode(_factory.CreateJsxClosingElement(tagName), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:6356-6369 — <c>parseJsxClosingFragment</c>.</summary>
    private JsxClosingFragment ParseJsxClosingFragment(bool inExpressionContext)
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.LessThanSlashToken);
        if (ParseExpected(SyntaxKind.GreaterThanToken, Diagnostics.Expected_corresponding_closing_tag_for_JSX_fragment, /*shouldAdvance*/ false))
        {
            // manually advance the scanner in order to look for jsx text inside jsx
            if (inExpressionContext)
            {
                NextToken();
            }
            else
            {
                _currentToken = _scanner.ScanJsxToken();
            }
        }

        return FinishNode(_factory.CreateJsxJsxClosingFragment(), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:6371-6379 — <c>parseTypeAssertion</c>.</summary>
    private TypeAssertion ParseTypeAssertion()
    {
        Debug.Assert(_languageVariant != LanguageVariant.JSX, "Type assertions should never be parsed in JSX; they should be parsed as comparisons or JSX elements/fragments.");
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.LessThanToken);
        var type = ParseType();
        ParseExpected(SyntaxKind.GreaterThanToken);
        var expression = ParseSimpleUnaryExpression();
        return FinishNode(_factory.CreateTypeAssertion(type, expression), pos);
    }

    //
    // Member, element and call expressions
    //

    /// <summary>ported from: src/compiler/parser.ts:6381-6386 — <c>nextTokenIsIdentifierOrKeywordOrOpenBracketOrTemplate</c>.</summary>
    private bool NextTokenIsIdentifierOrKeywordOrOpenBracketOrTemplate()
    {
        NextToken();
        return ScannerUtilities.TokenIsIdentifierOrKeyword(Token())
            || Token() == SyntaxKind.OpenBracketToken
            || IsTemplateStartOfTaggedTemplate();
    }

    /// <summary>ported from: src/compiler/parser.ts:6388-6391 — <c>isStartOfOptionalPropertyOrElementAccessChain</c>.</summary>
    private bool IsStartOfOptionalPropertyOrElementAccessChain()
        => Token() == SyntaxKind.QuestionDotToken
            && LookAheadBool(NextTokenIsIdentifierOrKeywordOrOpenBracketOrTemplate);

    /// <summary>ported from: src/compiler/parser.ts:6393-6413 — <c>tryReparseOptionalChain</c>.</summary>
    private bool TryReparseOptionalChain(Expression node)
    {
        if ((node.Flags & NodeFlags.OptionalChain) != 0)
        {
            return true;
        }

        // check for an optional chain in a non-null expression
        if (NodeTests.IsNonNullExpression(node))
        {
            var expr = ((NonNullExpression)node).Expression!;
            while (NodeTests.IsNonNullExpression(expr) && (expr.Flags & NodeFlags.OptionalChain) == 0)
            {
                expr = ((NonNullExpression)expr).Expression!;
            }

            if ((expr.Flags & NodeFlags.OptionalChain) != 0)
            {
                // this is part of an optional chain. Walk down from `node` to `expression` and set the flag.
                while (NodeTests.IsNonNullExpression(node))
                {
                    node.Flags |= NodeFlags.OptionalChain;
                    node = ((NonNullExpression)node).Expression!;
                }

                return true;
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/parser.ts:6415-6430 — <c>parsePropertyAccessExpressionRest</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS shadows its <c>pos</c> parameter with the <c>const pos</c> that names the instantiation
    /// expression's type-argument start; C# forbids that shadowing, so the inner one is named
    /// <c>typeArgumentsPos</c>. (The rest of the member needs no <c>pos</c> of its own.)
    /// </remarks>
    private PropertyAccessExpression ParsePropertyAccessExpressionRest(int pos, Expression expression, PunctuationToken? questionDotToken)
    {
        var name = ParseRightSideOfDot(/*allowIdentifierNames*/ true, /*allowPrivateIdentifiers*/ true, /*allowUnicodeEscapeSequenceInIdentifierName*/ true);
        var isOptionalChain = questionDotToken is not null || TryReparseOptionalChain(expression);
        var propertyAccess = isOptionalChain
            ? _factory.CreatePropertyAccessChain(expression, questionDotToken, name)
            : _factory.CreatePropertyAccessExpression(expression, name);
        if (isOptionalChain && NodeTests.IsPrivateIdentifier(propertyAccess.Name!))
        {
            ParseErrorAtRange(propertyAccess.Name!, Diagnostics.An_optional_chain_cannot_contain_private_identifiers);
        }

        if (NodeTests.IsExpressionWithTypeArguments(expression) && ((ExpressionWithTypeArguments)expression).TypeArguments is not null)
        {
            var typeArgumentsPos = ((ExpressionWithTypeArguments)expression).TypeArguments!.Pos - 1;
            var end = ScannerUtilities.SkipTrivia(_sourceText, ((ExpressionWithTypeArguments)expression).TypeArguments!.End) + 1;
            ParseErrorAt(typeArgumentsPos, end, Diagnostics.An_instantiation_expression_cannot_be_followed_by_a_property_access);
        }

        return FinishNode(propertyAccess, pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:6432-6451 — <c>parseElementAccessExpressionRest</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS writes <c>node.text = internIdentifier(node.text)</c> on a literal whose <c>text</c> is a
    /// <c>string</c>; the port's slot is <c>string?</c>, so a null (which the three kinds the predicate accepts cannot
    /// produce) interns as the empty string — the same treatment Parser.NodeCreation gives <c>parsePropertyNameWorker</c>.
    /// </remarks>
    private ElementAccessExpression ParseElementAccessExpressionRest(int pos, Expression expression, PunctuationToken? questionDotToken)
    {
        Expression argumentExpression;
        if (Token() == SyntaxKind.CloseBracketToken)
        {
            argumentExpression = CreateMissingNode<Identifier>(SyntaxKind.Identifier, /*reportAtCurrentPosition*/ true, Diagnostics.An_element_access_expression_should_take_an_argument);
        }
        else
        {
            var argument = AllowInAnd(ParseExpression);
            if (Utilities.IsStringOrNumericLiteralLike(argument))
            {
                ((LiteralExpression)argument).Text = InternIdentifier(((LiteralExpression)argument).Text ?? "");
            }

            argumentExpression = argument;
        }

        ParseExpected(SyntaxKind.CloseBracketToken);

        var indexedAccess = questionDotToken is not null || TryReparseOptionalChain(expression)
            ? _factory.CreateElementAccessChain(expression, questionDotToken, argumentExpression)
            : _factory.CreateElementAccessExpression(expression, argumentExpression);
        return FinishNode(indexedAccess, pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:6453-6499 — <c>parseMemberExpressionRest</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares the parameter and the answer <c>MemberExpression</c> and writes
    /// <c>return expression as MemberExpression</c> on the way out. That cast is a lie at the one call site that matters:
    /// <c>parseCallExpressionRest</c> feeds call expressions (which are <c>LeftHandSideExpression</c>s, not member
    /// expressions) back in, and TS stores the result straight back into its <c>LeftHandSideExpression</c> local. The
    /// port takes and answers <see cref="Expression"/> — the type that is true at every return — and
    /// <see cref="ParseMemberExpressionOrHigher"/> narrows once, where the input is a primary expression and every branch
    /// of this member either returns that input or wraps it in a member expression.
    /// </remarks>
    private Expression ParseMemberExpressionRest(int pos, Expression expression, bool allowOptionalChain)
    {
        while (true)
        {
            PunctuationToken? questionDotToken = null;
            bool isPropertyAccess;
            if (allowOptionalChain && IsStartOfOptionalPropertyOrElementAccessChain())
            {
                questionDotToken = (PunctuationToken)ParseExpectedToken(SyntaxKind.QuestionDotToken);
                isPropertyAccess = ScannerUtilities.TokenIsIdentifierOrKeyword(Token());
            }
            else
            {
                isPropertyAccess = ParseOptional(SyntaxKind.DotToken);
            }

            if (isPropertyAccess)
            {
                expression = ParsePropertyAccessExpressionRest(pos, expression, questionDotToken);
                continue;
            }

            // when in the [Decorator] context, we do not parse ElementAccess as it could be part of a ComputedPropertyName
            if ((questionDotToken is not null || !InDecoratorContext()) && ParseOptional(SyntaxKind.OpenBracketToken))
            {
                expression = ParseElementAccessExpressionRest(pos, expression, questionDotToken);
                continue;
            }

            if (IsTemplateStartOfTaggedTemplate())
            {
                // Absorb type arguments into TemplateExpression when preceding expression is ExpressionWithTypeArguments
                expression = questionDotToken is null && expression.Kind == SyntaxKind.ExpressionWithTypeArguments
                    ? ParseTaggedTemplateRest(pos, ((ExpressionWithTypeArguments)expression).Expression!, questionDotToken, ((ExpressionWithTypeArguments)expression).TypeArguments)
                    : ParseTaggedTemplateRest(pos, expression, questionDotToken, /*typeArguments*/ null);
                continue;
            }

            if (questionDotToken is null)
            {
                if (Token() == SyntaxKind.ExclamationToken && !_scanner.HasPrecedingLineBreak())
                {
                    NextToken();
                    expression = FinishNode(_factory.CreateNonNullExpression(expression), pos);
                    continue;
                }

                var typeArguments = TryParse(ParseTypeArgumentsInExpression);
                if (typeArguments is not null)
                {
                    expression = FinishNode(_factory.CreateExpressionWithTypeArguments(expression, typeArguments), pos);
                    continue;
                }
            }

            return expression;
        }
    }

    /// <summary>ported from: src/compiler/parser.ts:6501-6503 — <c>isTemplateStartOfTaggedTemplate</c>.</summary>
    private bool IsTemplateStartOfTaggedTemplate()
        => Token() == SyntaxKind.NoSubstitutionTemplateLiteral || Token() == SyntaxKind.TemplateHead;

    /// <summary>ported from: src/compiler/parser.ts:6505-6518 — <c>parseTaggedTemplateRest</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS writes the "re-scan then read the literal" pair as the comma expression
    /// <c>(reScanTemplateToken(true), parseLiteralNode() as NoSubstitutionTemplateLiteral)</c> inside the factory call.
    /// C# has no comma operator, so the branch is spelled as an if/else and the template is computed before the factory
    /// call; neither of that call's other arguments has a side effect and the token is not read in between, so the order
    /// is the same.
    /// </remarks>
    private TaggedTemplateExpression ParseTaggedTemplateRest(int pos, Expression tag, PunctuationToken? questionDotToken, NodeArray<ITypeNode>? typeArguments)
    {
        MemberExpression template;
        if (Token() == SyntaxKind.NoSubstitutionTemplateLiteral)
        {
            ReScanTemplateToken(/*isTaggedTemplate*/ true);
            template = ParseLiteralNode();
        }
        else
        {
            template = ParseTemplateExpression(/*isTaggedTemplate*/ true);
        }

        var tagExpression = _factory.CreateTaggedTemplateExpression(tag, typeArguments, template);
        if (questionDotToken is not null || (tag.Flags & NodeFlags.OptionalChain) != 0)
        {
            tagExpression.Flags |= NodeFlags.OptionalChain;
        }

        tagExpression.QuestionDotToken = questionDotToken;
        return FinishNode(tagExpression, pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:6520-6553 — <c>parseCallExpressionRest</c>.</summary>
    private Expression ParseCallExpressionRest(int pos, Expression expression)
    {
        while (true)
        {
            expression = ParseMemberExpressionRest(pos, expression, /*allowOptionalChain*/ true);
            NodeArray<ITypeNode>? typeArguments = null;
            var questionDotToken = (PunctuationToken?)ParseOptionalToken(SyntaxKind.QuestionDotToken);
            if (questionDotToken is not null)
            {
                typeArguments = TryParse(ParseTypeArgumentsInExpression);
                if (IsTemplateStartOfTaggedTemplate())
                {
                    expression = ParseTaggedTemplateRest(pos, expression, questionDotToken, typeArguments);
                    continue;
                }
            }

            if (typeArguments is not null || Token() == SyntaxKind.OpenParenToken)
            {
                // Absorb type arguments into CallExpression when preceding expression is ExpressionWithTypeArguments
                if (questionDotToken is null && expression.Kind == SyntaxKind.ExpressionWithTypeArguments)
                {
                    typeArguments = ((ExpressionWithTypeArguments)expression).TypeArguments;
                    expression = ((ExpressionWithTypeArguments)expression).Expression!;
                }

                var argumentList = ParseArgumentList();
                var callExpr = questionDotToken is not null || TryReparseOptionalChain(expression)
                    ? _factory.CreateCallChain(expression, questionDotToken, typeArguments, argumentList)
                    : _factory.CreateCallExpression(expression, typeArguments, argumentList);
                expression = FinishNode(callExpr, pos);
                continue;
            }

            if (questionDotToken is not null)
            {
                // We parsed `?.` but then failed to parse anything, so report a missing identifier here.
                var name = CreateMissingNode<Identifier>(SyntaxKind.Identifier, /*reportAtCurrentPosition*/ false, Diagnostics.Identifier_expected);
                expression = FinishNode(_factory.CreatePropertyAccessChain(expression, questionDotToken, name), pos);
            }

            break;
        }

        return expression;
    }

    /// <summary>ported from: src/compiler/parser.ts:6555-6560 — <c>parseArgumentList</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the port's <see cref="ParseDelimitedList{T}"/> answers <c>NodeArray&lt;T&gt;?</c> for both of TS's
    /// overloads, so a call site whose element parser cannot fail appends <c>!</c> (Parser.Lists's convention).
    /// </remarks>
    private NodeArray<Expression> ParseArgumentList()
    {
        ParseExpected(SyntaxKind.OpenParenToken);
        var result = ParseDelimitedList(ParsingContext.ArgumentExpressions, ParseArgumentExpression);
        ParseExpected(SyntaxKind.CloseParenToken);
        return result!;
    }

    /// <summary>ported from: src/compiler/parser.ts:6562-6585 — <c>parseTypeArgumentsInExpression</c>.</summary>
    private NodeArray<ITypeNode>? ParseTypeArgumentsInExpression()
    {
        if ((_contextFlags & NodeFlags.JavaScriptFile) != 0)
        {
            // TypeArguments must not be parsed in JavaScript files to avoid ambiguity with binary operators.
            return null;
        }

        if (ReScanLessThanToken() != SyntaxKind.LessThanToken)
        {
            return null;
        }

        NextToken();

        var typeArguments = ParseDelimitedList(ParsingContext.TypeArguments, ParseType);
        if (ReScanGreaterToken() != SyntaxKind.GreaterThanToken)
        {
            // If it doesn't have the closing `>` then it's definitely not an type argument list.
            return null;
        }

        NextToken();

        // We successfully parsed a type argument list. The next token determines whether we want to
        // treat it as such. If the type argument list is followed by `(` or a template literal, as in
        // `f<number>(42)`, we favor the type argument interpretation even though JavaScript would view
        // it as a relational expression.
        return typeArguments is not null && CanFollowTypeArgumentsInExpression() ? typeArguments : null;
    }

    /// <summary>ported from: src/compiler/parser.ts:6587-6606 — <c>canFollowTypeArgumentsInExpression</c>.</summary>
    private bool CanFollowTypeArgumentsInExpression()
    {
        switch (Token())
        {
            // These tokens can follow a type argument list in a call expression.
            case SyntaxKind.OpenParenToken: // foo<x>(
            case SyntaxKind.NoSubstitutionTemplateLiteral: // foo<T> `...`
            case SyntaxKind.TemplateHead: // foo<T> `...${100}...`
                return true;
            // A type argument list followed by `<` never makes sense, and a type argument list followed
            // by `>` is ambiguous with a (re-scanned) `>>` operator, so we disqualify both. Also, in
            // this context, `+` and `-` are unary operators, not binary operators.
            case SyntaxKind.LessThanToken:
            case SyntaxKind.GreaterThanToken:
            case SyntaxKind.PlusToken:
            case SyntaxKind.MinusToken:
                return false;
        }

        // We favor the type argument list interpretation when it is immediately followed by
        // a line break, a binary operator, or something that can't start an expression.
        return _scanner.HasPrecedingLineBreak() || IsBinaryOperator() || !IsStartOfExpression();
    }

    //
    // Primary expressions
    //

    /// <summary>ported from: src/compiler/parser.ts:6608-6661 — <c>parsePrimaryExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers <c>PrimaryExpression</c>, a brand on top of <c>MemberExpression</c>. Every class the port
    /// can return here is a <see cref="MemberExpression"/> (<c>PrivateIdentifier</c> and the literals reach it through
    /// their base, and <c>ClassExpression</c> through the interface walk §12.8 describes), so that is what this answers —
    /// the same shape TS's callers rely on, without the brand.
    /// <para>
    /// PORT-NOTE: TS's <c>parseTokenNode&lt;PrimaryExpression&gt;()</c> for the five keyword primary expressions maps to
    /// <see cref="ParseTokenNode{T}"/> over <see cref="MemberExpression"/>: the factory builds the
    /// <c>ThisExpression</c>/<c>SuperExpression</c>/<c>NullLiteral</c>/<c>TrueLiteral</c>/<c>FalseLiteral</c> class for
    /// those kinds, which is exactly the class TS only gets to name on the interface.
    /// </para>
    /// </remarks>
    private MemberExpression ParsePrimaryExpression()
    {
        switch (Token())
        {
            case SyntaxKind.NoSubstitutionTemplateLiteral:
                if ((_scanner.GetTokenFlags() & TokenFlags.IsInvalid) != 0)
                {
                    ReScanTemplateToken(/*isTaggedTemplate*/ false);
                }

                // falls through
                goto case SyntaxKind.NumericLiteral;
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
            case SyntaxKind.StringLiteral:
                return ParseLiteralNode();
            case SyntaxKind.ThisKeyword:
            case SyntaxKind.SuperKeyword:
            case SyntaxKind.NullKeyword:
            case SyntaxKind.TrueKeyword:
            case SyntaxKind.FalseKeyword:
                return ParseTokenNode<MemberExpression>();
            case SyntaxKind.OpenParenToken:
                return ParseParenthesizedExpression();
            case SyntaxKind.OpenBracketToken:
                return ParseArrayLiteralExpression();
            case SyntaxKind.OpenBraceToken:
                return ParseObjectLiteralExpression();
            case SyntaxKind.AsyncKeyword:
                // Async arrow functions are parsed earlier in parseAssignmentExpressionOrHigher.
                // If we encounter `async [no LineTerminator here] function` then this is an async
                // function; otherwise, its an identifier.
                if (!LookAheadBool(NextTokenIsFunctionKeywordOnSameLine))
                {
                    break;
                }

                return ParseFunctionExpression();
            case SyntaxKind.AtToken:
                // PORT-NOTE: TS answers `PrimaryExpression` here (parser.ts:8132); both of that member's results — a
                // `ClassExpression` and a `MissingDeclaration` — are `MemberExpression`s in this model, so the narrowing
                // to this member's answer is done at the call site and cannot fail.
                return (MemberExpression)ParseDecoratedExpression();
            case SyntaxKind.ClassKeyword:
                return ParseClassExpression();
            case SyntaxKind.FunctionKeyword:
                return ParseFunctionExpression();
            case SyntaxKind.NewKeyword:
                return ParseNewExpressionOrNewDotTarget();
            case SyntaxKind.SlashToken:
            case SyntaxKind.SlashEqualsToken:
                if (ReScanSlashToken() == SyntaxKind.RegularExpressionLiteral)
                {
                    return ParseLiteralNode();
                }

                break;
            case SyntaxKind.TemplateHead:
                return ParseTemplateExpression(/*isTaggedTemplate*/ false);
            case SyntaxKind.PrivateIdentifier:
                return ParsePrivateIdentifier();
        }

        return ParseIdentifier(Diagnostics.Expression_expected);
    }

    /// <summary>ported from: src/compiler/parser.ts:6663-6670 — <c>parseParenthesizedExpression</c>.</summary>
    private ParenthesizedExpression ParseParenthesizedExpression()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        ParseExpected(SyntaxKind.OpenParenToken);
        var expression = AllowInAnd(ParseExpression);
        ParseExpected(SyntaxKind.CloseParenToken);
        return WithJSDoc(FinishNode(_factory.CreateParenthesizedExpression(expression), pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:6672-6677 — <c>parseSpreadElement</c>.</summary>
    private Expression ParseSpreadElement()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.DotDotDotToken);
        var expression = ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ true);
        return FinishNode(_factory.CreateSpreadElement(expression), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:6679-6683 — <c>parseArgumentOrArrayLiteralElement</c>.</summary>
    private Expression ParseArgumentOrArrayLiteralElement()
        => Token() == SyntaxKind.DotDotDotToken ? ParseSpreadElement()
            : Token() == SyntaxKind.CommaToken ? FinishNode(_factory.CreateOmittedExpression(), GetNodePos())
            : ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ true);

    /// <summary>ported from: src/compiler/parser.ts:6685-6687 — <c>parseArgumentExpression</c>.</summary>
    private Expression ParseArgumentExpression()
        => DoOutsideOfContext(DisallowInAndDecoratorContext, ParseArgumentOrArrayLiteralElement);

    /// <summary>ported from: src/compiler/parser.ts:6689-6697 — <c>parseArrayLiteralExpression</c>.</summary>
    private ArrayLiteralExpression ParseArrayLiteralExpression()
    {
        var pos = GetNodePos();
        var openBracketPosition = _scanner.GetTokenStart();
        var openBracketParsed = ParseExpected(SyntaxKind.OpenBracketToken);
        var multiLine = _scanner.HasPrecedingLineBreak();
        var elements = ParseDelimitedList(ParsingContext.ArrayLiteralMembers, ParseArgumentOrArrayLiteralElement);
        ParseExpectedMatchingBrackets(SyntaxKind.OpenBracketToken, SyntaxKind.CloseBracketToken, openBracketParsed, openBracketPosition);
        return FinishNode(_factory.CreateArrayLiteralExpression(elements, multiLine), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:6699-6753 — <c>parseObjectLiteralElement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers the union <c>ObjectLiteralElementLike</c>, whose members share no C# base narrower than
    /// <see cref="Node"/> (§12.5).
    /// <para>
    /// PORT-NOTE: TS builds either a short-hand or a normal property assignment into one <c>let node</c>, writes
    /// <c>node.modifiers/questionToken/exclamationToken</c> on the union, and applies
    /// <c>withJSDoc(finishNode(node, pos))</c> once at the end. C# has no union type here: <c>ShorthandPropertyAssignment</c>
    /// and <c>PropertyAssignment</c> share only <see cref="Node"/>, which neither declares those three slots on nor
    /// satisfies <see cref="WithJSDoc{T}"/>'s <c>IJSDocContainer</c> constraint, so each branch finishes its own node and
    /// applies the JSDoc at the same point in the sequence (after the slots, after <c>finishNode</c>).
    /// </para>
    /// </remarks>
    private Node ParseObjectLiteralElement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();

        if (ParseOptionalToken(SyntaxKind.DotDotDotToken) is not null)
        {
            var expression = ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ true);
            return WithJSDoc(FinishNode(_factory.CreateSpreadAssignment(expression), pos), hasJSDoc);
        }

        var modifiers = ParseModifiers(/*allowDecorators*/ true);
        if (ParseContextualModifier(SyntaxKind.GetKeyword))
        {
            return ParseAccessorDeclaration(pos, hasJSDoc, modifiers, SyntaxKind.GetAccessor, ParserSignatureFlags.None);
        }

        if (ParseContextualModifier(SyntaxKind.SetKeyword))
        {
            return ParseAccessorDeclaration(pos, hasJSDoc, modifiers, SyntaxKind.SetAccessor, ParserSignatureFlags.None);
        }

        var asteriskToken = (PunctuationToken?)ParseOptionalToken(SyntaxKind.AsteriskToken);
        var tokenIsIdentifier = IsIdentifier();
        var name = ParsePropertyName();

        // Disallowing of optional property assignments and definite assignment assertion happens in the grammar checker.
        var questionToken = (PunctuationToken?)ParseOptionalToken(SyntaxKind.QuestionToken);
        var exclamationToken = (PunctuationToken?)ParseOptionalToken(SyntaxKind.ExclamationToken);

        if (asteriskToken is not null || Token() == SyntaxKind.OpenParenToken || Token() == SyntaxKind.LessThanToken)
        {
            return ParseMethodDeclaration(pos, hasJSDoc, modifiers, asteriskToken, name, questionToken, exclamationToken);
        }

        // check if it is short-hand property assignment or normal property assignment
        // NOTE: if token is EqualsToken it is interpreted as CoverInitializedName production
        // CoverInitializedName[Yield] :
        //     IdentifierReference[?Yield] Initializer[In, ?Yield]
        // this is necessary because ObjectLiteral productions are also used to cover grammar for ObjectAssignmentPattern
        var isShorthandPropertyAssignment = tokenIsIdentifier && Token() != SyntaxKind.ColonToken;
        if (isShorthandPropertyAssignment)
        {
            var equalsToken = (PunctuationToken?)ParseOptionalToken(SyntaxKind.EqualsToken);
            var objectAssignmentInitializer = equalsToken is not null ? AllowInAnd(() => ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ true)) : null;
            var node = _factory.CreateShorthandPropertyAssignment(name, objectAssignmentInitializer);
            // Save equals token for error reporting.
            // TODO(rbuckton): Consider manufacturing this when we need to report an error as it is otherwise not useful.
            node.EqualsToken = equalsToken;
            // Decorators, Modifiers, questionToken, and exclamationToken are not supported by property assignments and are reported in the grammar checker
            node.Modifiers = modifiers;
            node.QuestionToken = questionToken;
            node.ExclamationToken = exclamationToken;
            return WithJSDoc(FinishNode(node, pos), hasJSDoc);
        }
        else
        {
            ParseExpected(SyntaxKind.ColonToken);
            var initializer = AllowInAnd(() => ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ true));
            var node = _factory.CreatePropertyAssignment(name, initializer);
            // Decorators, Modifiers, questionToken, and exclamationToken are not supported by property assignments and are reported in the grammar checker
            node.Modifiers = modifiers;
            node.QuestionToken = questionToken;
            node.ExclamationToken = exclamationToken;
            return WithJSDoc(FinishNode(node, pos), hasJSDoc);
        }
    }

    /// <summary>ported from: src/compiler/parser.ts:6755-6763 — <c>parseObjectLiteralExpression</c>.</summary>
    private ObjectLiteralExpression ParseObjectLiteralExpression()
    {
        var pos = GetNodePos();
        var openBracePosition = _scanner.GetTokenStart();
        var openBraceParsed = ParseExpected(SyntaxKind.OpenBraceToken);
        var multiLine = _scanner.HasPrecedingLineBreak();
        var properties = ParseDelimitedList(ParsingContext.ObjectLiteralMembers, ParseObjectLiteralElement, /*considerSemicolonAsDelimiter*/ true);
        ParseExpectedMatchingBrackets(SyntaxKind.OpenBraceToken, SyntaxKind.CloseBraceToken, openBraceParsed, openBracePosition);
        return FinishNode(_factory.CreateObjectLiteralExpression(properties, multiLine), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:6765-6795 — <c>parseFunctionExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>parseModifiers(/*allowDecorators*/ false)</c> is typed <c>NodeArray&lt;Modifier&gt;</c> by its
    /// overload pair, which is what <c>createFunctionExpression</c> wants; the port's <c>ParseModifiers</c> answers the
    /// wider <c>NodeArray&lt;Node&gt;</c> (that is the array TS hands to <c>createFunctionDeclaration</c>), so the slot
    /// goes through <see cref="AsModifierTokens"/> — the same projection Parser.Types uses for the three factory slots
    /// declared <c>List&lt;ModifierToken&gt;?</c>.
    /// </remarks>
    private FunctionExpression ParseFunctionExpression()
    {
        // GeneratorExpression:
        //      function* BindingIdentifier [Yield][opt](FormalParameters[Yield]){ GeneratorBody }
        //
        // FunctionExpression:
        //      function BindingIdentifier[opt](FormalParameters){ FunctionBody }
        var savedDecoratorContext = InDecoratorContext();
        SetContextFlag(/*val*/ false, NodeFlags.DecoratorContext);

        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var modifiers = ParseModifiers(/*allowDecorators*/ false);
        ParseExpected(SyntaxKind.FunctionKeyword);
        var asteriskToken = (PunctuationToken?)ParseOptionalToken(SyntaxKind.AsteriskToken);
        var isGenerator = asteriskToken is not null ? ParserSignatureFlags.Yield : ParserSignatureFlags.None;
        var isAsync = Core.Some(modifiers, NodeTests.IsAsyncModifier) ? ParserSignatureFlags.Await : ParserSignatureFlags.None;
        var name = isGenerator != ParserSignatureFlags.None && isAsync != ParserSignatureFlags.None ? DoInYieldAndAwaitContext(ParseOptionalBindingIdentifier)
            : isGenerator != ParserSignatureFlags.None ? DoInYieldContext(ParseOptionalBindingIdentifier)
            : isAsync != ParserSignatureFlags.None ? DoInAwaitContext(ParseOptionalBindingIdentifier)
            : ParseOptionalBindingIdentifier();

        var typeParameters = ParseTypeParameters();
        var parameters = ParseParameters(isGenerator | isAsync);
        var type = ParseReturnType(SyntaxKind.ColonToken, /*isType*/ false);
        var body = ParseFunctionBlock(isGenerator | isAsync);

        SetContextFlag(savedDecoratorContext, NodeFlags.DecoratorContext);

        var node = _factory.CreateFunctionExpression(AsModifierTokens(modifiers), asteriskToken, name, typeParameters, parameters, type, body);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:6797-6799 — <c>parseOptionalBindingIdentifier</c>.</summary>
    private Identifier? ParseOptionalBindingIdentifier()
        => IsBindingIdentifier() ? ParseBindingIdentifier() : null;

    /// <summary>ported from: src/compiler/parser.ts:6801-6821 — <c>parseNewExpressionOrNewDotTarget</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers <c>NewExpression | MetaProperty</c>, both of which are <see cref="MemberExpression"/>s — the
    /// union's common C# base.
    /// </remarks>
    private MemberExpression ParseNewExpressionOrNewDotTarget()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.NewKeyword);
        if (ParseOptional(SyntaxKind.DotToken))
        {
            var name = ParseIdentifierName();
            return FinishNode(_factory.CreateMetaProperty(SyntaxKind.NewKeyword, name), pos);
        }

        var expressionPos = GetNodePos();
        var expression = ParseMemberExpressionRest(expressionPos, ParsePrimaryExpression(), /*allowOptionalChain*/ false);
        NodeArray<ITypeNode>? typeArguments = null;
        // Absorb type arguments into NewExpression when preceding expression is ExpressionWithTypeArguments
        if (expression.Kind == SyntaxKind.ExpressionWithTypeArguments)
        {
            typeArguments = ((ExpressionWithTypeArguments)expression).TypeArguments;
            expression = ((ExpressionWithTypeArguments)expression).Expression!;
        }

        if (Token() == SyntaxKind.QuestionDotToken)
        {
            ParseErrorAtCurrentToken(Diagnostics.Invalid_optional_chain_from_new_expression_Did_you_mean_to_call_0, Utilities.GetTextOfNodeFromSourceText(_sourceText, expression));
        }

        var argumentList = Token() == SyntaxKind.OpenParenToken ? ParseArgumentList() : null;
        return FinishNode(_factory.CreateNewExpression(expression, typeArguments, argumentList), pos);
    }
}
