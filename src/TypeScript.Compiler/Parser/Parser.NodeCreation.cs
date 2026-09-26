namespace TypeScript.Compiler;

// ported from: src/compiler/parser.ts:2364-2833 — node completion, the optional/expected token family, the identifier
// and property-name readers, and the small error helpers.
/// <summary>
/// The completion half of <see cref="Parser"/>: <c>finishNode</c>/<c>createNodeArray</c>, the missing-node factory, the
/// optional/expected token family, the identifier, property-name and modifier readers, and the diagnostics for a
/// statement that was not terminated.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS's <c>PropertyName</c> union (and the <c>Expression | PropertyName</c> TS lets
/// <c>parseErrorForMissingSemicolonAfter</c> take) has no C# member, so the members that answer one take the wide
/// <see cref="Node"/> and name the concrete class in a comment — the same treatment Parser.cs gives <c>EntityName</c>
/// (§12.5).
/// <para>
/// PORT-NOTE: TS declares the token readers as a generic-declaration + implementation overload pair
/// (<c>parseOptionalToken&lt;TKind extends SyntaxKind&gt;(t: TKind): Token&lt;TKind&gt;</c> next to
/// <c>parseOptionalToken(t: SyntaxKind): Node | undefined</c>), where the type parameter exists only so the
/// <em>caller</em> can name the token class. The port keeps one member per family, taking a <see cref="SyntaxKind"/>
/// and answering the wide <see cref="Node"/>, plus a generic <see cref="ParseTokenNode{T}"/> for the one member whose
/// TS shape is generic-only; a caller that wants the concrete class casts at the use site.
/// </para>
/// <para>
/// PORT-NOTE: TS's <c>createMissingNode</c> ties its type parameter to the kind (<c>kind: T["kind"]</c>) so that the
/// two always agree. C# has no dependent types, so the port takes a <see cref="SyntaxKind"/> and a separate
/// <c>T</c> — see <see cref="CreateMissingNode{T}"/> for how they are kept in step.
/// </para>
/// </remarks>
internal sealed partial class Parser
{
    //
    // Missing-semicolon diagnostics
    //

    /// <summary>ported from: src/compiler/parser.ts:2364-2433 — <c>parseErrorForMissingSemicolonAfter</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>Expression | PropertyName</c>; neither has a C# member that covers it,
    /// so this takes <see cref="Node"/> (§12.5).
    /// </remarks>
    private void ParseErrorForMissingSemicolonAfter(Node node)
    {
        // Tagged template literals are sometimes used in places where only simple strings are allowed, i.e.:
        //   module `M1` {
        //   ^^^^^^^^^^^ This block is parsed as a template literal like module`M1`.
        if (NodeTests.IsTaggedTemplateExpression(node))
        {
            var template = ((TaggedTemplateExpression)node).Template!;
            ParseErrorAt(
                ScannerUtilities.SkipTrivia(_sourceText, template.Pos),
                template.End,
                Diagnostics.Module_declaration_names_may_only_use_or_quoted_strings);
            return;
        }

        // Otherwise, if this isn't a well-known keyword-like identifier, give the generic fallback message.
        var expressionText = NodeTests.IsIdentifier(node) ? Utilities.IdText(node) : null;
        if (!Js.IsTruthy(expressionText) || !ScannerUtilities.IsIdentifierText(expressionText, _languageVersion))
        {
            ParseErrorAtCurrentToken(Diagnostics._0_expected, ScannerUtilities.TokenToString(SyntaxKind.SemicolonToken));
            return;
        }

        var pos = ScannerUtilities.SkipTrivia(_sourceText, node.Pos);

        // Some known keywords are likely signs of syntax being used improperly.
        switch (expressionText)
        {
            case "const":
            case "let":
            case "var":
                ParseErrorAt(pos, node.End, Diagnostics.Variable_declaration_not_allowed_at_this_location);
                return;

            case "declare":
                // If a declared node failed to parse, it would have emitted a diagnostic already.
                return;

            case "interface":
                ParseErrorForInvalidName(Diagnostics.Interface_name_cannot_be_0, Diagnostics.Interface_must_be_given_a_name, SyntaxKind.OpenBraceToken);
                return;

            case "is":
                ParseErrorAt(pos, _scanner.GetTokenStart(), Diagnostics.A_type_predicate_is_only_allowed_in_return_type_position_for_functions_and_methods);
                return;

            case "module":
            case "namespace":
                ParseErrorForInvalidName(Diagnostics.Namespace_name_cannot_be_0, Diagnostics.Namespace_must_be_given_a_name, SyntaxKind.OpenBraceToken);
                return;

            case "type":
                ParseErrorForInvalidName(Diagnostics.Type_alias_name_cannot_be_0, Diagnostics.Type_alias_must_be_given_a_name, SyntaxKind.EqualsToken);
                return;
        }

        // The user alternatively might have misspelled or forgotten to add a space after a common keyword.
        var suggestion = Core.GetSpellingSuggestion(expressionText, s_viableKeywordSuggestions, Core.Identity)
            ?? GetSpaceSuggestion(expressionText);
        if (Js.IsTruthy(suggestion))
        {
            ParseErrorAt(pos, node.End, Diagnostics.Unknown_keyword_or_identifier_Did_you_mean_0, suggestion);
            return;
        }

        // Unknown tokens are handled with their own errors in the scanner
        if (Token() == SyntaxKind.Unknown)
        {
            return;
        }

        // Otherwise, we know this some kind of unknown word, not just a missing expected semicolon.
        ParseErrorAt(pos, node.End, Diagnostics.Unexpected_keyword_or_identifier);
    }

    /// <summary>ported from: src/compiler/parser.ts:2435-2442 — <c>parseErrorForInvalidName</c>.</summary>
    /// <remarks>Reports a diagnostic error for the current token being an invalid name.</remarks>
    /// <param name="nameDiagnostic">Diagnostic to report for all other cases.</param>
    /// <param name="blankDiagnostic">Diagnostic to report for the case of the name being blank (matched
    /// <paramref name="tokenIfBlankName"/>).</param>
    /// <param name="tokenIfBlankName">Current token if the name was invalid for being blank (not provided /
    /// skipped).</param>
    private void ParseErrorForInvalidName(DiagnosticMessage nameDiagnostic, DiagnosticMessage blankDiagnostic, SyntaxKind tokenIfBlankName)
    {
        if (Token() == tokenIfBlankName)
        {
            ParseErrorAtCurrentToken(blankDiagnostic);
        }
        else
        {
            ParseErrorAtCurrentToken(nameDiagnostic, _scanner.GetTokenValue());
        }
    }

    /// <summary>ported from: src/compiler/parser.ts:2444-2452 — <c>getSpaceSuggestion</c>.</summary>
    /// <remarks>
    /// TS returns <c>undefined</c> for "no suggestion"; the port answers <c>null</c>. The member is <c>static</c>
    /// because the only state it reads is the static keyword list.
    /// </remarks>
    private static string? GetSpaceSuggestion(string expressionText)
    {
        foreach (var keyword in s_viableKeywordSuggestions)
        {
            if (expressionText.Length > keyword.Length + 2 && Core.StartsWith(expressionText, keyword))
            {
                return $"{keyword} {expressionText[keyword.Length..]}";
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/parser.ts:2454-2487 — <c>parseSemicolonAfterPropertyName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>name: PropertyName</c> and <c>type: TypeNode | undefined</c> become <see cref="Node"/> and
    /// <see cref="ITypeNode"/> (§12.5); the parameter is only ever tested for presence.
    /// </remarks>
    private void ParseSemicolonAfterPropertyName(Node name, ITypeNode? type, Expression? initializer)
    {
        if (Token() == SyntaxKind.AtToken && !_scanner.HasPrecedingLineBreak())
        {
            ParseErrorAtCurrentToken(Diagnostics.Decorators_must_precede_the_name_and_all_keywords_of_property_declarations);
            return;
        }

        if (Token() == SyntaxKind.OpenParenToken)
        {
            ParseErrorAtCurrentToken(Diagnostics.Cannot_start_a_function_call_in_a_type_annotation);
            NextToken();
            return;
        }

        if (type is not null && !CanParseSemicolon())
        {
            if (initializer is not null)
            {
                ParseErrorAtCurrentToken(Diagnostics._0_expected, ScannerUtilities.TokenToString(SyntaxKind.SemicolonToken));
            }
            else
            {
                ParseErrorAtCurrentToken(Diagnostics.Expected_for_property_initializer);
            }

            return;
        }

        if (TryParseSemicolon())
        {
            return;
        }

        if (initializer is not null)
        {
            ParseErrorAtCurrentToken(Diagnostics._0_expected, ScannerUtilities.TokenToString(SyntaxKind.SemicolonToken));
            return;
        }

        ParseErrorForMissingSemicolonAfter(name);
    }

    //
    // JSDoc tokens
    //

    /// <summary>ported from: src/compiler/parser.ts:2488-2496 — <c>parseExpectedJSDoc</c>.</summary>
    private bool ParseExpectedJSDoc(SyntaxKind kind)
    {
        if (Token() == kind)
        {
            NextTokenJSDoc();
            return true;
        }

        Debug.Assert(Utilities.IsKeywordOrPunctuation(kind));
        ParseErrorAtCurrentToken(Diagnostics._0_expected, ScannerUtilities.TokenToString(kind));
        return false;
    }

    /// <summary>ported from: src/compiler/parser.ts:2498-2512 — <c>parseExpectedMatchingBrackets</c>.</summary>
    private void ParseExpectedMatchingBrackets(SyntaxKind openKind, SyntaxKind closeKind, bool openParsed, int openPosition)
    {
        if (Token() == closeKind)
        {
            NextToken();
            return;
        }

        var lastError = ParseErrorAtCurrentToken(Diagnostics._0_expected, ScannerUtilities.TokenToString(closeKind));
        if (!openParsed)
        {
            return;
        }

        if (lastError is not null)
        {
            Utilities.AddRelatedInfo(
                lastError,
                Utilities.CreateDetachedDiagnostic(
                    _fileName,
                    _sourceText,
                    openPosition,
                    1,
                    Diagnostics.The_parser_expected_to_find_a_1_to_match_the_0_token_here,
                    ScannerUtilities.TokenToString(openKind),
                    ScannerUtilities.TokenToString(closeKind)));
        }
    }

    //
    // Optional and expected tokens
    //

    /// <summary>ported from: src/compiler/parser.ts:2515-2521 — <c>parseOptional</c>.</summary>
    private bool ParseOptional(SyntaxKind t)
    {
        if (Token() == t)
        {
            NextToken();
            return true;
        }

        return false;
    }

    /// <summary>ported from: src/compiler/parser.ts:2523-2529 — <c>parseOptionalToken</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS has this member both as a generic declaration (whose type parameter only lets the caller name the
    /// token class) and as the implementation overload that answers <c>Node | undefined</c>; the port keeps the
    /// implementation shape, and a caller wanting the concrete class casts — see <see cref="ParseTokenNode{T}"/>.
    /// </remarks>
    private Node? ParseOptionalToken(SyntaxKind t) => Token() == t ? ParseTokenNode<Node>() : null;

    /// <summary>ported from: src/compiler/parser.ts:2531-2537 — <c>parseOptionalTokenJSDoc</c>.</summary>
    private Node? ParseOptionalTokenJSDoc(SyntaxKind t) => Token() == t ? ParseTokenNodeJSDoc<Node>() : null;

    /// <summary>ported from: src/compiler/parser.ts:2539-2543 — <c>parseExpectedToken</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS writes <c>arg0 || tokenToString(t)</c>, whose left operand falls through when it is the empty
    /// string; <see cref="Js.IsTruthy(string?)"/> is what the port asks for that.
    /// </remarks>
    private Node ParseExpectedToken(SyntaxKind t, DiagnosticMessage? diagnosticMessage = null, string? arg0 = null)
        => ParseOptionalToken(t)
            ?? CreateMissingNode<Node>(
                t,
                /*reportAtCurrentPosition*/ false,
                diagnosticMessage ?? Diagnostics._0_expected,
                Js.IsTruthy(arg0) ? arg0 : ScannerUtilities.TokenToString(t));

    /// <summary>ported from: src/compiler/parser.ts:2545-2551 — <c>parseExpectedTokenJSDoc</c>.</summary>
    private Node ParseExpectedTokenJSDoc(SyntaxKind t)
    {
        var optional = ParseOptionalTokenJSDoc(t);
        if (optional is not null) return optional;
        Debug.Assert(Utilities.IsKeywordOrPunctuation(t));
        return CreateMissingNode<Node>(t, /*reportAtCurrentPosition*/ false, Diagnostics._0_expected, ScannerUtilities.TokenToString(t));
    }

    /// <summary>ported from: src/compiler/parser.ts:2553-2558 — <c>parseTokenNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the type parameter names the class the caller expects
    /// (<c>SyntaxKind.Identifier</c> → <see cref="Identifier"/>, any other kind → <see cref="Token"/>); TS's
    /// <c>as T</c> is unchecked, so a caller that names the wrong class only fails where it uses the result, while the
    /// port's cast fails at the cast — the same contract with an earlier error.
    /// </remarks>
    private T ParseTokenNode<T>()
        where T : Node
    {
        var pos = GetNodePos();
        var kind = Token();
        NextToken();
        return FinishNode((T)_factory.CreateToken(kind), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:2560-2565 — <c>parseTokenNodeJSDoc</c>.</summary>
    /// <remarks>PORT-NOTE: see <see cref="ParseTokenNode{T}"/> for the type parameter.</remarks>
    private T ParseTokenNodeJSDoc<T>()
        where T : Node
    {
        var pos = GetNodePos();
        var kind = Token();
        NextTokenJSDoc();
        return FinishNode((T)_factory.CreateToken(kind), pos);
    }

    //
    // Semicolons, node arrays and node completion
    //

    /// <summary>ported from: src/compiler/parser.ts:2567-2575 — <c>canParseSemicolon</c>.</summary>
    private bool CanParseSemicolon()
    {
        // If there's a real semicolon, then we can always parse it out.
        if (Token() == SyntaxKind.SemicolonToken)
        {
            return true;
        }

        // We can parse out an optional semicolon in ASI cases in the following cases.
        return Token() == SyntaxKind.CloseBraceToken || Token() == SyntaxKind.EndOfFileToken || _scanner.HasPrecedingLineBreak();
    }

    /// <summary>ported from: src/compiler/parser.ts:2577-2588 — <c>tryParseSemicolon</c>.</summary>
    private bool TryParseSemicolon()
    {
        if (!CanParseSemicolon())
        {
            return false;
        }

        if (Token() == SyntaxKind.SemicolonToken)
        {
            // consume the semicolon if it was explicitly provided.
            NextToken();
        }

        return true;
    }

    /// <summary>ported from: src/compiler/parser.ts:2590-2592 — <c>parseSemicolon</c>.</summary>
    private bool ParseSemicolon() => TryParseSemicolon() || ParseExpected(SyntaxKind.SemicolonToken);

    /// <summary>ported from: src/compiler/parser.ts:2594-2598 — <c>createNodeArray</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the work is divided between this member and <see cref="NodeFactory.CreateNodeArray{T}"/> exactly as
    /// TS divides it between <c>createNodeArray</c> and the factory's: the factory takes the elements and the
    /// trailing-comma flag (and reuses the input when it is already a <c>NodeArray</c> carrying the same flag), while
    /// this member stamps the text range afterwards — which is why the array the caller passed in keeps its identity,
    /// <c>hasTrailingComma</c> included. TS's <c>end</c> defaults to the scanner's token full start; so does the port's.
    /// </remarks>
    private NodeArray<T> CreateNodeArray<T>(IList<T> elements, int pos, int? end = null, bool? hasTrailingComma = null)
        where T : INode
    {
        var array = _factory.CreateNodeArray(elements, hasTrailingComma);
        Utilities.SetTextRangePosEnd(array, pos, end ?? _scanner.GetTokenFullStart());
        return array;
    }

    /// <summary>ported from: src/compiler/parser.ts:2600-2611 — <c>finishNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: this is all TS 6.0's body does — set the range, OR in the context flags, and consume the
    /// <c>parseErrorBeforeNextFinishedNode</c> latch. The incremental-error flags
    /// (<c>ThisNodeOrAnySubNodesHasError</c>/<c>HasAggregatedChildData</c>) and <c>transformFlags</c> are <em>not</em>
    /// set here; <c>utilities.ts</c> aggregates them after the parse (<c>Utilities.ContainsParseError</c>), and the
    /// parent link is set by <c>fixupParentReferences</c>/<c>setParentRecursive</c> — none of which belongs to this
    /// function in release-6.0.
    /// </remarks>
    private T FinishNode<T>(T node, int pos, int? end = null)
        where T : Node
    {
        Utilities.SetTextRangePosEnd(node, pos, end ?? _scanner.GetTokenFullStart());
        if (_contextFlags != NodeFlags.None)
        {
            node.Flags |= _contextFlags;
        }

        // Keep track on the node if we encountered an error while parsing it.  If we did, then
        // we cannot reuse the node incrementally.  Once we've marked this node, clear out the
        // flag so that we don't mark any subsequent nodes.
        if (_parseErrorBeforeNextFinishedNode)
        {
            _parseErrorBeforeNextFinishedNode = false;
            node.Flags |= NodeFlags.ThisNodeHasError;
        }

        return node;
    }

    /// <summary>ported from: src/compiler/parser.ts:2617-2635 — <c>createMissingNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS ties the type parameter to the kind (<c>kind: T["kind"]</c>) so the two cannot disagree; C# has no
    /// dependent types, so the port takes the kind and the class separately and checks them at the cast. The class the
    /// kind builds comes from one factory call per kind — <c>createIdentifier</c>, the template-literal factory,
    /// <c>createNumericLiteral</c>, <c>createStringLiteral</c>, <c>createMissingDeclaration</c> and otherwise the
    /// token factory — exactly as TS selects it.
    /// </remarks>
    private T CreateMissingNode<T>(SyntaxKind kind, bool reportAtCurrentPosition, DiagnosticMessage? diagnosticMessage = null, params object?[] args)
        where T : Node
    {
        if (reportAtCurrentPosition)
        {
            ParseErrorAtPosition(_scanner.GetTokenFullStart(), 0, diagnosticMessage!, args);
        }
        else if (diagnosticMessage is not null)
        {
            ParseErrorAtCurrentToken(diagnosticMessage, args);
        }

        var pos = GetNodePos();
        Node result = kind switch
        {
            SyntaxKind.Identifier => _factory.CreateIdentifier("", /*originalKeywordKind*/ null),
            _ when Utilities.IsTemplateLiteralKind(kind) => _factory.CreateTemplateLiteralLikeNode(kind, "", "", /*templateFlags*/ null),
            SyntaxKind.NumericLiteral => _factory.CreateNumericLiteral("", /*numericLiteralFlags*/ null),
            SyntaxKind.StringLiteral => _factory.CreateStringLiteral("", /*isSingleQuote*/ null),
            SyntaxKind.MissingDeclaration => _factory.CreateMissingDeclaration(),
            _ => _factory.CreateToken(kind),
        };
        return FinishNode((T)result, pos);
    }

    //
    // Identifiers and names
    //

    /// <summary>ported from: src/compiler/parser.ts:2637-2644 — <c>internIdentifier</c>.</summary>
    private string InternIdentifier(string text)
    {
        if (_identifiers.TryGetValue(text, out var identifier))
        {
            return identifier;
        }

        _identifiers[text] = identifier = text;
        return identifier;
    }

    /// <summary>ported from: src/compiler/parser.ts:2648-2682 — <c>createIdentifier</c>.</summary>
    /// <remarks>
    /// The identifier count, the interning of the text and the extended-unicode-escape flag all live here, as in TS.
    /// PORT-NOTE: TS's <c>scanner.getTokenValue()</c> is declared to answer a <c>string</c>; the port's scanner answers
    /// <c>string?</c>, so a null (which cannot come from the identifier, keyword or private-identifier tokens this
    /// reaches) interns as the empty string.
    /// </remarks>
    private Identifier CreateIdentifier(bool isIdentifier, DiagnosticMessage? diagnosticMessage = null, DiagnosticMessage? privateIdentifierDiagnosticMessage = null)
    {
        if (isIdentifier)
        {
            _identifierCount++;
            var pos = _scanner.HasPrecedingJSDocLeadingAsterisks() ? _scanner.GetTokenStart() : GetNodePos();
            // Store original token kind if it is not just an Identifier so we can report appropriate error later in type checker
            var originalKeywordKind = Token();
            var text = InternIdentifier(_scanner.GetTokenValue() ?? "");
            var hasExtendedUnicodeEscape = _scanner.HasExtendedUnicodeEscape();
            NextTokenWithoutCheck();
            return FinishNode(_factory.CreateIdentifier(text, originalKeywordKind, hasExtendedUnicodeEscape), pos);
        }

        if (Token() == SyntaxKind.PrivateIdentifier)
        {
            ParseErrorAtCurrentToken(privateIdentifierDiagnosticMessage ?? Diagnostics.Private_identifiers_are_not_allowed_outside_class_bodies);
            return CreateIdentifier(/*isIdentifier*/ true);
        }

        if (Token() == SyntaxKind.Unknown && _scanner.TryScan(() => _scanner.ReScanInvalidIdentifier() == SyntaxKind.Identifier))
        {
            // Scanner has already recorded an 'Invalid character' error, so no need to add another from the parser.
            return CreateIdentifier(/*isIdentifier*/ true);
        }

        _identifierCount++;
        // Only for end of file because the error gets reported incorrectly on embedded script tags.
        var reportAtCurrentPosition = Token() == SyntaxKind.EndOfFileToken;

        var isReservedWord = _scanner.IsReservedWord();
        var msgArg = _scanner.GetTokenText();

        var defaultMessage = isReservedWord
            ? Diagnostics.Identifier_expected_0_is_a_reserved_word_that_cannot_be_used_here
            : Diagnostics.Identifier_expected;

        return CreateMissingNode<Identifier>(SyntaxKind.Identifier, reportAtCurrentPosition, diagnosticMessage ?? defaultMessage, msgArg);
    }

    /// <summary>ported from: src/compiler/parser.ts:2684-2686 — <c>parseBindingIdentifier</c>.</summary>
    private Identifier ParseBindingIdentifier(DiagnosticMessage? privateIdentifierDiagnosticMessage = null)
        => CreateIdentifier(IsBindingIdentifier(), /*diagnosticMessage*/ null, privateIdentifierDiagnosticMessage);

    /// <summary>ported from: src/compiler/parser.ts:2688-2690 — <c>parseIdentifier</c>.</summary>
    private Identifier ParseIdentifier(DiagnosticMessage? diagnosticMessage = null, DiagnosticMessage? privateIdentifierDiagnosticMessage = null)
        => CreateIdentifier(IsIdentifier(), diagnosticMessage, privateIdentifierDiagnosticMessage);

    /// <summary>ported from: src/compiler/parser.ts:2692-2694 — <c>parseIdentifierName</c>.</summary>
    private Identifier ParseIdentifierName(DiagnosticMessage? diagnosticMessage = null)
        => CreateIdentifier(ScannerUtilities.TokenIsIdentifierOrKeyword(Token()), diagnosticMessage);

    /// <summary>ported from: src/compiler/parser.ts:2696-2701 — <c>parseIdentifierNameErrorOnUnicodeEscapeSequence</c>.</summary>
    private Identifier ParseIdentifierNameErrorOnUnicodeEscapeSequence()
    {
        if (_scanner.HasUnicodeEscape() || _scanner.HasExtendedUnicodeEscape())
        {
            ParseErrorAtCurrentToken(Diagnostics.Unicode_escape_sequence_cannot_appear_here);
        }

        return CreateIdentifier(ScannerUtilities.TokenIsIdentifierOrKeyword(Token()));
    }

    /// <summary>ported from: src/compiler/parser.ts:2703-2708 — <c>isLiteralPropertyName</c>.</summary>
    private bool IsLiteralPropertyName()
        => ScannerUtilities.TokenIsIdentifierOrKeyword(Token())
            || Token() == SyntaxKind.StringLiteral
            || Token() == SyntaxKind.NumericLiteral
            || Token() == SyntaxKind.BigIntLiteral;

    /// <summary>ported from: src/compiler/parser.ts:2710-2712 — <c>isImportAttributeName</c>.</summary>
    private bool IsImportAttributeName()
        => ScannerUtilities.TokenIsIdentifierOrKeyword(Token()) || Token() == SyntaxKind.StringLiteral;

    /// <summary>ported from: src/compiler/parser.ts:2714-2727 — <c>parsePropertyNameWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>PropertyName</c> union (<c>Identifier | PrivateIdentifier | ComputedPropertyName</c> plus the
    /// literal property names) has no C# member, so the answer stays <see cref="Node"/> and each branch's concrete
    /// class is left to the caller (§12.5).
    /// </remarks>
    private Node? ParsePropertyNameWorker(bool allowComputedPropertyNames)
    {
        if (Token() is SyntaxKind.StringLiteral or SyntaxKind.NumericLiteral or SyntaxKind.BigIntLiteral)
        {
            // TS writes `const node = parseLiteralNode() as StringLiteral | NumericLiteral | BigIntLiteral;
            // node.text = internIdentifier(node.text);`. The cast only exists so TS can reach `text`, which the port's
            // LiteralExpression carries on the common base, so it is dropped.
            // PORT-NOTE: the port's slot is `string?` where TS's is `string`, so a null text (which a literal token
            // cannot produce) would intern as the empty string rather than stay undefined.
            var node = ParseLiteralNode();
            node.Text = InternIdentifier(node.Text ?? "");
            return node;
        }

        if (allowComputedPropertyNames && Token() == SyntaxKind.OpenBracketToken)
        {
            return ParseComputedPropertyName();
        }

        if (Token() == SyntaxKind.PrivateIdentifier)
        {
            return ParsePrivateIdentifier();
        }

        return ParseIdentifierName();
    }

    /// <summary>ported from: src/compiler/parser.ts:2729-2731 — <c>parsePropertyName</c>.</summary>
    private Node? ParsePropertyName() => ParsePropertyNameWorker(/*allowComputedPropertyNames*/ true);

    /// <summary>ported from: src/compiler/parser.ts:2733-2745 — <c>parseComputedPropertyName</c>.</summary>
    private ComputedPropertyName ParseComputedPropertyName()
    {
        // PropertyName [Yield]:
        //      LiteralPropertyName
        //      ComputedPropertyName[?Yield]
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.OpenBracketToken);
        // We parse any expression (including a comma expression). But the grammar
        // says that only an assignment expression is allowed, so the grammar checker
        // will error if it sees a comma expression.
        var expression = AllowInAnd(ParseExpression);
        ParseExpected(SyntaxKind.CloseBracketToken);
        return FinishNode(_factory.CreateComputedPropertyName(expression), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:2747-2752 — <c>parsePrivateIdentifier</c>.</summary>
    private PrivateIdentifier ParsePrivateIdentifier()
    {
        var pos = GetNodePos();
        var node = _factory.CreatePrivateIdentifier(InternIdentifier(_scanner.GetTokenValue() ?? ""));
        NextToken();
        return FinishNode(node, pos);
    }

    //
    // Modifiers
    //

    /// <summary>TS's <c>tryParse</c> over a boolean callback (parser.ts:2304-2306).</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>speculationHelper</c> rolls the parser back when the callback returns a falsy value, so
    /// <c>tryParse</c> is used with <c>boolean</c> callbacks as well. The port's <c>TryParse&lt;T&gt;</c> is constrained
    /// to reference types (it models TS's "falsy" as <c>null</c>), which a <c>bool</c> cannot satisfy, so the boolean is
    /// turned into a boxed <c>true</c> or <c>null</c> first: <c>null</c> takes exactly the rollback path a
    /// <c>false</c> takes in TS, and "is not null" is the value TS returns.
    /// </remarks>
    private bool TryParseBool(Func<bool> callback)
        => TryParse<object?>(() => callback() ? s_boxedTrue : null) is not null;

    /// <summary>TS's <c>lookAhead</c> over a boolean callback (parser.ts:2295-2297).</summary>
    /// <remarks>PORT-NOTE: see <see cref="TryParseBool"/> — the boolean is boxed the same way.</remarks>
    private bool LookAheadBool(Func<bool> callback)
        => LookAhead<object?>(() => callback() ? s_boxedTrue : null) is not null;

    private static readonly object s_boxedTrue = true;

    /// <summary>ported from: src/compiler/parser.ts:2754-2756 — <c>parseContextualModifier</c>.</summary>
    private bool ParseContextualModifier(SyntaxKind t)
        => Token() == t && TryParseBool(NextTokenCanFollowModifier);

    /// <summary>ported from: src/compiler/parser.ts:2758-2764 — <c>nextTokenIsOnSameLineAndCanFollowModifier</c>.</summary>
    private bool NextTokenIsOnSameLineAndCanFollowModifier()
    {
        NextToken();
        if (_scanner.HasPrecedingLineBreak())
        {
            return false;
        }

        return CanFollowModifier();
    }

    /// <summary>ported from: src/compiler/parser.ts:2766-2792 — <c>nextTokenCanFollowModifier</c>.</summary>
    private bool NextTokenCanFollowModifier()
    {
        switch (Token())
        {
            case SyntaxKind.ConstKeyword:
                // 'const' is only a modifier if followed by 'enum'.
                return NextToken() == SyntaxKind.EnumKeyword;
            case SyntaxKind.ExportKeyword:
                NextToken();
                if (Token() == SyntaxKind.DefaultKeyword)
                {
                    return LookAheadBool(NextTokenCanFollowDefaultKeyword);
                }
                if (Token() == SyntaxKind.TypeKeyword)
                {
                    return LookAheadBool(NextTokenCanFollowExportModifier);
                }
                return CanFollowExportModifier();
            case SyntaxKind.DefaultKeyword:
                return NextTokenCanFollowDefaultKeyword();
            case SyntaxKind.StaticKeyword:
                NextToken();
                return CanFollowModifier();
            case SyntaxKind.GetKeyword:
            case SyntaxKind.SetKeyword:
                NextToken();
                return CanFollowGetOrSetKeyword();
            default:
                return NextTokenIsOnSameLineAndCanFollowModifier();
        }
    }

    /// <summary>ported from: src/compiler/parser.ts:2794-2800 — <c>canFollowExportModifier</c>.</summary>
    private bool CanFollowExportModifier()
        => Token() == SyntaxKind.AtToken
            || (Token() != SyntaxKind.AsteriskToken
                && Token() != SyntaxKind.AsKeyword
                && Token() != SyntaxKind.OpenBraceToken
                && CanFollowModifier());

    /// <summary>ported from: src/compiler/parser.ts:2802-2805 — <c>nextTokenCanFollowExportModifier</c>.</summary>
    private bool NextTokenCanFollowExportModifier()
    {
        NextToken();
        return CanFollowExportModifier();
    }

    /// <summary>ported from: src/compiler/parser.ts:2807-2809 — <c>parseAnyContextualModifier</c>.</summary>
    private bool ParseAnyContextualModifier()
        => Utilities.IsModifierKind(Token()) && TryParseBool(NextTokenCanFollowModifier);

    /// <summary>ported from: src/compiler/parser.ts:2811-2817 — <c>canFollowModifier</c>.</summary>
    private bool CanFollowModifier()
        => Token() == SyntaxKind.OpenBracketToken
            || Token() == SyntaxKind.OpenBraceToken
            || Token() == SyntaxKind.AsteriskToken
            || Token() == SyntaxKind.DotDotDotToken
            || IsLiteralPropertyName();

    /// <summary>ported from: src/compiler/parser.ts:2819-2822 — <c>canFollowGetOrSetKeyword</c>.</summary>
    private bool CanFollowGetOrSetKeyword()
        => Token() == SyntaxKind.OpenBracketToken || IsLiteralPropertyName();

    /// <summary>ported from: src/compiler/parser.ts:7153-7156 — <c>nextTokenIsClassKeywordOnSameLine</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares this helper next to the list predicates, outside this slice's range; it is private here
    /// because the only member that reads it is <see cref="NextTokenCanFollowDefaultKeyword"/> and every parser slice
    /// is part of the same partial class.
    /// </remarks>
    private bool NextTokenIsClassKeywordOnSameLine()
    {
        NextToken();
        return Token() == SyntaxKind.ClassKeyword && !_scanner.HasPrecedingLineBreak();
    }

    /// <summary>ported from: src/compiler/parser.ts:7158-7161 — <c>nextTokenIsFunctionKeywordOnSameLine</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: see <see cref="NextTokenIsClassKeywordOnSameLine"/>; the declarations slice reads it for
    /// `export default async function`.
    /// </remarks>
    private bool NextTokenIsFunctionKeywordOnSameLine()
    {
        NextToken();
        return Token() == SyntaxKind.FunctionKeyword && !_scanner.HasPrecedingLineBreak();
    }

    /// <summary>ported from: src/compiler/parser.ts:2824-2832 — <c>nextTokenCanFollowDefaultKeyword</c>.</summary>
    private bool NextTokenCanFollowDefaultKeyword()
    {
        NextToken();
        return Token() == SyntaxKind.ClassKeyword
            || Token() == SyntaxKind.FunctionKeyword
            || Token() == SyntaxKind.InterfaceKeyword
            || Token() == SyntaxKind.AtToken
            || (Token() == SyntaxKind.AbstractKeyword && LookAheadBool(NextTokenIsClassKeywordOnSameLine))
            || (Token() == SyntaxKind.AsyncKeyword && LookAheadBool(NextTokenIsFunctionKeywordOnSameLine));
    }
}
