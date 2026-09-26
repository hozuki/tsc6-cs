namespace TypeScript.Compiler;

// ported from: src/compiler/parser.ts:2027-2042 (the context-flag wrappers this section is the first to need)
// ported from: src/compiler/parser.ts:7581-8789 (the DECLARATIONS section)
/// <summary>
/// The declaration half of <see cref="Parser"/>: binding elements and variable declarations; the modifiers, decorators
/// and members of a class body; function, class, interface, type-alias, enum, module, import and export declarations;
/// and the heritage clauses that hang off a class or interface.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS's <c>ModifierLike</c>, <c>PropertyName</c>, <c>BindingName</c>, <c>ModuleExportName</c>,
/// <c>ArrayBindingElement</c>, <c>ImportOrExportSpecifier</c>, <c>ClassLikeDeclaration</c> and
/// <c>PropertyDeclaration | MethodDeclaration</c> are unions with no C# member, so every member that answers one answers
/// the wide node type the model picked for it — <see cref="Node"/>, or the categorisation interface
/// <see cref="IClassElement"/>/<see cref="IStatement"/> when every arm implements it — and the concrete class the kind
/// selects is named where it is built. Callers narrow with a cast at the point the kind is known (§12.5).
/// <para>
/// PORT-NOTE: the two <c>parseModifiers</c> overloads (whose only job is to let TS spell the answer
/// <c>NodeArray&lt;Modifier&gt;</c> or <c>NodeArray&lt;ModifierLike&gt;</c>) collapse to one member, and the array the
/// parser builds is a <c>NodeArray&lt;Node&gt;</c> — the shape the factory's <c>modifiers</c> slot takes.
/// </para>
/// <para>
/// PORT-NOTE: modifier tokens are built with <c>NodeFactory.CreateModifier</c> where TS calls
/// <c>factory.createToken(kind as Modifier["kind"])</c>. The two are the same token in TS; in the port
/// <see cref="ModifierToken"/> is a class of its own, and the factory's function-like slots take
/// <c>List&lt;ModifierToken&gt;?</c> — <c>FactoryUtilities.AsModifierTokens</c> narrows a parsed array by casting each
/// element, so a plain <see cref="Token"/> would fail there. The kind, range and transform flags are identical.
/// </para>
/// <para>
/// PORT-NOTE: TS's <c>setDisallowInContext</c>/<c>setYieldContext</c>/<c>setAwaitContext</c> (parser.ts:2027-2042) are
/// one-line wrappers around <c>setContextFlag</c>; the port's frozen <c>Parser.cs</c> carries only <c>setContextFlag</c>
/// (with the <c>in*Context</c> readers), and this slice is the first whose members use all three wrappers, so they are
/// declared here.
/// </para>
/// </remarks>
internal sealed partial class Parser
{
    //
    // Context flags
    //

    /// <summary>ported from: src/compiler/parser.ts:2027-2029 — <c>setDisallowInContext</c>.</summary>
    /// <remarks>PORT-NOTE: see the class remarks — the wrapper is declared here because <c>Parser.cs</c> keeps only <see cref="SetContextFlag"/>.</remarks>
    private void SetDisallowInContext(bool val) => SetContextFlag(val, NodeFlags.DisallowInContext);

    /// <summary>ported from: src/compiler/parser.ts:2031-2033 — <c>setYieldContext</c>.</summary>
    /// <remarks>PORT-NOTE: see <see cref="SetDisallowInContext"/>.</remarks>
    private void SetYieldContext(bool val) => SetContextFlag(val, NodeFlags.YieldContext);

    /// <summary>ported from: src/compiler/parser.ts:2039-2041 — <c>setAwaitContext</c>.</summary>
    /// <remarks>PORT-NOTE: see <see cref="SetDisallowInContext"/>.</remarks>
    private void SetAwaitContext(bool val) => SetContextFlag(val, NodeFlags.AwaitContext);

    //
    // Binding elements and variable declarations
    //

    /// <summary>ported from: src/compiler/parser.ts:7583-7592 — <c>parseArrayBindingElement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: answers <see cref="Node"/> for the union <c>BindingElement | OmittedExpression</c>; the caller only
    /// ever hands it to the array-pattern factory, which takes <c>List&lt;Node&gt;?</c>.
    /// </remarks>
    private Node ParseArrayBindingElement()
    {
        var pos = GetNodePos();
        if (Token() == SyntaxKind.CommaToken)
        {
            return FinishNode(_factory.CreateOmittedExpression(), pos);
        }

        var dotDotDotToken = ParseOptionalToken(SyntaxKind.DotDotDotToken);
        var name = ParseIdentifierOrPattern();
        var initializer = ParseInitializer();
        return FinishNode(_factory.CreateBindingElement((PunctuationToken?)dotDotDotToken, /*propertyName*/ null, name, initializer), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:7594-7610 — <c>parseObjectBindingElement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS narrows <c>propertyName as Identifier</c> when the property name doubles as the binding name; that
    /// narrowing is only a type-level assertion (the node is the same object), so the port passes it through as the
    /// wide <see cref="Node"/> the factory's <c>name</c> slot takes.
    /// </remarks>
    private BindingElement ParseObjectBindingElement()
    {
        var pos = GetNodePos();
        var dotDotDotToken = ParseOptionalToken(SyntaxKind.DotDotDotToken);
        var tokenIsIdentifier = IsBindingIdentifier();
        Node? propertyName = ParsePropertyName();
        // BindingName (Identifier | BindingPattern) — no C# member covers both, so the wide node type does here.
        Node name;
        if (tokenIsIdentifier && Token() != SyntaxKind.ColonToken)
        {
            name = propertyName!;
            propertyName = null;
        }
        else
        {
            ParseExpected(SyntaxKind.ColonToken);
            name = ParseIdentifierOrPattern();
        }

        var initializer = ParseInitializer();
        return FinishNode(_factory.CreateBindingElement((PunctuationToken?)dotDotDotToken, propertyName, name, initializer), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:7612-7618 — <c>parseObjectBindingPattern</c>.</summary>
    /// <remarks>PORT-NOTE: the <c>!</c> is TS's "element parser always succeeds" <c>parseDelimitedList</c> overload (see <see cref="ParseDelimitedList{T}"/>).</remarks>
    private ObjectBindingPattern ParseObjectBindingPattern()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.OpenBraceToken);
        var elements = AllowInAnd(() => ParseDelimitedList(ParsingContext.ObjectBindingElements, ParseObjectBindingElement))!;
        ParseExpected(SyntaxKind.CloseBraceToken);
        return FinishNode(_factory.CreateObjectBindingPattern(elements), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:7620-7626 — <c>parseArrayBindingPattern</c>.</summary>
    /// <remarks>PORT-NOTE: see <see cref="ParseObjectBindingPattern"/> for the <c>!</c>.</remarks>
    private ArrayBindingPattern ParseArrayBindingPattern()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.OpenBracketToken);
        var elements = AllowInAnd(() => ParseDelimitedList(ParsingContext.ArrayBindingElements, ParseArrayBindingElement))!;
        ParseExpected(SyntaxKind.CloseBracketToken);
        return FinishNode(_factory.CreateArrayBindingPattern(elements), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:7628-7633 — <c>isBindingIdentifierOrPrivateIdentifierOrPattern</c>.</summary>
    private bool IsBindingIdentifierOrPrivateIdentifierOrPattern()
        => Token() == SyntaxKind.OpenBraceToken
            || Token() == SyntaxKind.OpenBracketToken
            || Token() == SyntaxKind.PrivateIdentifier
            || IsBindingIdentifier();

    /// <summary>ported from: src/compiler/parser.ts:7635-7643 — <c>parseIdentifierOrPattern</c>.</summary>
    /// <remarks>PORT-NOTE: answers <see cref="Node"/> for the union <c>Identifier | BindingPattern</c>.</remarks>
    private Node ParseIdentifierOrPattern(DiagnosticMessage? privateIdentifierDiagnosticMessage = null)
    {
        if (Token() == SyntaxKind.OpenBracketToken)
        {
            return ParseArrayBindingPattern();
        }

        if (Token() == SyntaxKind.OpenBraceToken)
        {
            return ParseObjectBindingPattern();
        }

        return ParseBindingIdentifier(privateIdentifierDiagnosticMessage);
    }

    /// <summary>ported from: src/compiler/parser.ts:7645-7647 — <c>parseVariableDeclarationAllowExclamation</c>.</summary>
    private VariableDeclaration ParseVariableDeclarationAllowExclamation() => ParseVariableDeclaration(/*allowExclamation*/ true);

    /// <summary>ported from: src/compiler/parser.ts:7649-7664 — <c>parseVariableDeclaration</c>.</summary>
    private VariableDeclaration ParseVariableDeclaration(bool allowExclamation = false)
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var name = ParseIdentifierOrPattern(Diagnostics.Private_identifiers_are_not_allowed_in_variable_declarations);
        PunctuationToken? exclamationToken = null;
        if (
            allowExclamation && name.Kind == SyntaxKind.Identifier &&
            Token() == SyntaxKind.ExclamationToken && !_scanner.HasPrecedingLineBreak()
        )
        {
            exclamationToken = ParseTokenNode<PunctuationToken>();
        }

        var type = ParseTypeAnnotation();
        var initializer = IsInOrOfKeyword(Token()) ? null : ParseInitializer();
        var node = _factory.CreateVariableDeclaration(name, exclamationToken, type, initializer);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:7666-7721 — <c>parseVariableDeclarationList</c>.</summary>
    /// <remarks>
    /// The <c>break</c> inside the <c>await</c> arm is TS's own control flow: it leaves the switch, so the
    /// <c>nextToken()</c> below it consumes exactly one token, while the <c>await using</c> arm consumes two.
    /// </remarks>
    private VariableDeclarationList ParseVariableDeclarationList(bool inForStatementInitializer)
    {
        var pos = GetNodePos();

        var flags = NodeFlags.None;
        switch (Token())
        {
            case SyntaxKind.VarKeyword:
                break;
            case SyntaxKind.LetKeyword:
                flags |= NodeFlags.Let;
                break;
            case SyntaxKind.ConstKeyword:
                flags |= NodeFlags.Const;
                break;
            case SyntaxKind.UsingKeyword:
                flags |= NodeFlags.Using;
                break;
            case SyntaxKind.AwaitKeyword:
                if (!IsAwaitUsingDeclaration())
                {
                    break;
                }

                flags |= NodeFlags.AwaitUsing;
                NextToken();
                break;
            default:
                Debug.Fail();
                break;
        }

        NextToken();

        // The user may have written the following:
        //
        //    for (let of X) { }
        //
        // In this case, we want to parse an empty declaration list, and then parse 'of'
        // as a keyword. The reason this is not automatic is that 'of' is a valid identifier.
        // So we need to look ahead to determine if 'of' should be treated as a keyword in
        // this context.
        // The checker will then give an error that there is an empty declaration list.
        NodeArray<VariableDeclaration> declarations;
        if (Token() == SyntaxKind.OfKeyword && LookAheadBool(CanFollowContextualOfKeyword))
        {
            declarations = CreateMissingList<VariableDeclaration>();
        }
        else
        {
            var savedDisallowIn = InDisallowInContext();
            SetDisallowInContext(inForStatementInitializer);

            declarations = ParseDelimitedList<VariableDeclaration>(
                ParsingContext.VariableDeclarations,
                // PORT-NOTE: TS passes the two function references directly; the two have different arities
                // (`parseVariableDeclaration` takes `allowExclamation`), so the port spells each arm as a lambda.
                inForStatementInitializer ? () => ParseVariableDeclaration() : () => ParseVariableDeclarationAllowExclamation())!;

            SetDisallowInContext(savedDisallowIn);
        }

        return FinishNode(_factory.CreateVariableDeclarationList(declarations, flags), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:7723-7725 — <c>canFollowContextualOfKeyword</c>.</summary>
    private bool CanFollowContextualOfKeyword() => NextTokenIsIdentifier() && NextToken() == SyntaxKind.CloseParenToken;

    /// <summary>ported from: src/compiler/parser.ts:7727-7732 — <c>parseVariableStatement</c>.</summary>
    private VariableStatement ParseVariableStatement(int pos, bool hasJSDoc, NodeArray<Node>? modifiers)
    {
        var declarationList = ParseVariableDeclarationList(/*inForStatementInitializer*/ false);
        ParseSemicolon();
        var node = _factory.CreateVariableStatement(modifiers, declarationList);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    //
    // Functions and their names
    //

    /// <summary>ported from: src/compiler/parser.ts:7734-7751 — <c>parseFunctionDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>modifiersToFlags</c> lives in <c>utilities.ts</c>, so the port calls
    /// <see cref="Utilities.ModifiersToFlags"/>; the flags are tested with explicit <c>!= 0</c> because C# has no
    /// truthiness for a flags enum.
    /// </remarks>
    private FunctionDeclaration ParseFunctionDeclaration(int pos, bool hasJSDoc, NodeArray<Node>? modifiers)
    {
        var savedAwaitContext = InAwaitContext();
        var modifierFlags = Utilities.ModifiersToFlags(modifiers);
        ParseExpected(SyntaxKind.FunctionKeyword);
        var asteriskToken = ParseOptionalToken(SyntaxKind.AsteriskToken);
        // We don't parse the name here in await context, instead we will report a grammar error in the checker.
        var name = (modifierFlags & ModifierFlags.Default) != 0 ? ParseOptionalBindingIdentifier() : ParseBindingIdentifier();
        var isGenerator = asteriskToken is not null ? ParserSignatureFlags.Yield : ParserSignatureFlags.None;
        var isAsync = (modifierFlags & ModifierFlags.Async) != 0 ? ParserSignatureFlags.Await : ParserSignatureFlags.None;
        var typeParameters = ParseTypeParameters();
        if ((modifierFlags & ModifierFlags.Export) != 0) SetAwaitContext(/*value*/ true);
        var parameters = ParseParameters(isGenerator | isAsync);
        var type = ParseReturnType(SyntaxKind.ColonToken, /*isType*/ false);
        var body = ParseFunctionBlockOrSemicolon(isGenerator | isAsync, Diagnostics.or_expected);
        SetAwaitContext(savedAwaitContext);
        var node = _factory.CreateFunctionDeclaration(modifiers, (PunctuationToken?)asteriskToken, name, typeParameters, parameters, type, body);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:7753-7763 — <c>parseConstructorName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>tryParse</c> answers the literal node (or <c>undefined</c>) and the caller only tests its
    /// truthiness; the port keeps that shape with a nullable <see cref="LiteralExpression"/> result, because
    /// <see cref="TryParse{T}"/> rolls the parse back on a null answer and a <c>bool</c> could not carry one.
    /// </remarks>
    private bool ParseConstructorName()
    {
        if (Token() == SyntaxKind.ConstructorKeyword)
        {
            return ParseExpected(SyntaxKind.ConstructorKeyword);
        }

        if (Token() == SyntaxKind.StringLiteral && LookAhead(NextToken) == SyntaxKind.OpenParenToken)
        {
            return TryParse<LiteralExpression?>(() =>
            {
                var literalNode = ParseLiteralNode();
                return literalNode.Text == "constructor" ? literalNode : null;
            }) is not null;
        }

        return false;
    }

    /// <summary>ported from: src/compiler/parser.ts:7765-7780 — <c>tryParseConstructorDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS attaches the type parameters and the return type to the freshly built node through
    /// <c>Mutable&lt;ConstructorDeclaration&gt;</c> so the grammar checker can report them; the port assigns the
    /// generated setters directly.
    /// </remarks>
    private ConstructorDeclaration? TryParseConstructorDeclaration(int pos, bool hasJSDoc, NodeArray<Node>? modifiers)
        => TryParse<ConstructorDeclaration?>(() =>
        {
            if (ParseConstructorName())
            {
                var typeParameters = ParseTypeParameters();
                var parameters = ParseParameters(ParserSignatureFlags.None);
                var type = ParseReturnType(SyntaxKind.ColonToken, /*isType*/ false);
                var body = ParseFunctionBlockOrSemicolon(ParserSignatureFlags.None, Diagnostics.or_expected);
                var node = _factory.CreateConstructorDeclaration(modifiers, parameters, body);

                // Attach invalid nodes if they exist so that we can report them in the grammar checker.
                node.TypeParameters = typeParameters;
                node.Type = type;
                return WithJSDoc(FinishNode(node, pos), hasJSDoc);
            }

            return null;
        });

    /// <summary>ported from: src/compiler/parser.ts:7782-7812 — <c>parseMethodDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>name</c> is TS's <c>PropertyName</c> union and <c>diagnosticMessage</c>'s absence is TS's
    /// <c>undefined</c>; both are the wide <see cref="Node"/> and <c>null</c> here. The <c>exclamationToken</c> is not a
    /// factory parameter in release-6.0 either, so it is assigned after construction — which is why it must be
    /// reachable as a setter.
    /// </remarks>
    private MethodDeclaration ParseMethodDeclaration(
        int pos,
        bool hasJSDoc,
        NodeArray<Node>? modifiers,
        PunctuationToken? asteriskToken,
        Node? name,
        PunctuationToken? questionToken,
        PunctuationToken? exclamationToken,
        DiagnosticMessage? diagnosticMessage = null)
    {
        var isGenerator = asteriskToken is not null ? ParserSignatureFlags.Yield : ParserSignatureFlags.None;
        var isAsync = Core.Some(modifiers, NodeTests.IsAsyncModifier) ? ParserSignatureFlags.Await : ParserSignatureFlags.None;
        var typeParameters = ParseTypeParameters();
        var parameters = ParseParameters(isGenerator | isAsync);
        var type = ParseReturnType(SyntaxKind.ColonToken, /*isType*/ false);
        var body = ParseFunctionBlockOrSemicolon(isGenerator | isAsync, diagnosticMessage);
        var node = _factory.CreateMethodDeclaration(
            modifiers,
            asteriskToken,
            name,
            questionToken,
            typeParameters,
            parameters,
            type,
            body);

        // An exclamation token on a method is invalid syntax and will be handled by the grammar checker
        node.ExclamationToken = exclamationToken;
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:7814-7833 — <c>parsePropertyDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS writes <c>questionToken || exclamationToken</c>; both are nodes, so the null-coalescing operator
    /// is the same choice. <c>name</c> is the <c>PropertyName</c> union, widened to <see cref="Node"/>.
    /// </remarks>
    private PropertyDeclaration ParsePropertyDeclaration(
        int pos,
        bool hasJSDoc,
        NodeArray<Node>? modifiers,
        Node? name,
        PunctuationToken? questionToken)
    {
        var exclamationToken = questionToken is null && !_scanner.HasPrecedingLineBreak()
            ? (PunctuationToken?)ParseOptionalToken(SyntaxKind.ExclamationToken)
            : null;
        var type = ParseTypeAnnotation();
        var initializer = DoOutsideOfContext(NodeFlags.YieldContext | NodeFlags.AwaitContext | NodeFlags.DisallowInContext, ParseInitializer);
        ParseSemicolonAfterPropertyName(name!, type, initializer);
        var node = _factory.CreatePropertyDeclaration(
            modifiers,
            name,
            questionToken ?? exclamationToken,
            type,
            initializer);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:7835-7847 — <c>parsePropertyOrMethodDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers <c>PropertyDeclaration | MethodDeclaration</c>; both implement <see cref="IClassElement"/>,
    /// which is what the only caller (<see cref="ParseClassElement"/>) needs, so that is the port's answer.
    /// </remarks>
    private IClassElement ParsePropertyOrMethodDeclaration(
        int pos,
        bool hasJSDoc,
        NodeArray<Node>? modifiers)
    {
        var asteriskToken = ParseOptionalToken(SyntaxKind.AsteriskToken);
        var name = ParsePropertyName();
        // Note: this is not legal as per the grammar.  But we allow it in the parser and
        // report an error in the grammar checker.
        var questionToken = ParseOptionalToken(SyntaxKind.QuestionToken);
        if (asteriskToken is not null || Token() == SyntaxKind.OpenParenToken || Token() == SyntaxKind.LessThanToken)
        {
            return ParseMethodDeclaration(pos, hasJSDoc, modifiers, (PunctuationToken?)asteriskToken, name, (PunctuationToken?)questionToken, /*exclamationToken*/ null, Diagnostics.or_expected);
        }

        return ParsePropertyDeclaration(pos, hasJSDoc, modifiers, name, (PunctuationToken?)questionToken);
    }

    /// <summary>ported from: src/compiler/parser.ts:7851-7864 — <c>parseAccessorDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers the <c>AccessorDeclaration</c> union and then pokes <c>typeParameters</c> (both arms) and
    /// <c>type</c> (setters only) onto it through <c>Mutable&lt;&gt;</c>; the port has one class per accessor kind and no
    /// union, so each arm builds its own node, records the extra nodes the grammar checker reports, and answers the
    /// wide <see cref="Node"/> (the callers cast to <see cref="IClassElement"/> or <see cref="ITypeElement"/>, which
    /// both accessor classes implement).
    /// </remarks>
    private Node ParseAccessorDeclaration(int pos, bool hasJSDoc, NodeArray<Node>? modifiers, SyntaxKind kind, ParserSignatureFlags flags)
    {
        var name = ParsePropertyName();
        var typeParameters = ParseTypeParameters();
        var parameters = ParseParameters(ParserSignatureFlags.None);
        var type = ParseReturnType(SyntaxKind.ColonToken, /*isType*/ false);
        var body = ParseFunctionBlockOrSemicolon(flags);
        if (kind == SyntaxKind.GetAccessor)
        {
            var getter = _factory.CreateGetAccessorDeclaration(modifiers, name, parameters, type, body);
            // Keep track of `typeParameters` (for both) and `type` (for setters) if they were parsed those indicate grammar errors
            getter.TypeParameters = typeParameters;
            return WithJSDoc(FinishNode(getter, pos), hasJSDoc);
        }

        var setter = _factory.CreateSetAccessorDeclaration(modifiers, name, parameters, body);
        setter.TypeParameters = typeParameters;
        setter.Type = type;
        return WithJSDoc(FinishNode(setter, pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:7866-7924 — <c>isClassMemberStart</c>.</summary>
    private bool IsClassMemberStart()
    {
        SyntaxKind? idToken = null;

        if (Token() == SyntaxKind.AtToken)
        {
            return true;
        }

        // Eat up all modifiers, but hold on to the last one in case it is actually an identifier.
        while (Utilities.IsModifierKind(Token()))
        {
            idToken = Token();
            // If the idToken is a class modifier (protected, private, public, and static), it is
            // certain that we are starting to parse class member. This allows better error recovery
            // Example:
            //      public foo() ...     // true
            //      public @dec blah ... // true; we will then report an error later
            //      export public ...    // true; we will then report an error later
            if (Utilities.IsClassMemberModifier(idToken.Value))
            {
                return true;
            }

            NextToken();
        }

        if (Token() == SyntaxKind.AsteriskToken)
        {
            return true;
        }

        // Try to get the first property-like token following all modifiers.
        // This can either be an identifier or the 'get' or 'set' keywords.
        if (IsLiteralPropertyName())
        {
            idToken = Token();
            NextToken();
        }

        // Index signatures and computed properties are class members; we can parse.
        if (Token() == SyntaxKind.OpenBracketToken)
        {
            return true;
        }

        // If we were able to get any potential identifier...
        if (idToken is not null)
        {
            // If we have a non-keyword identifier, or if we have an accessor, then it's safe to parse.
            if (!Utilities.IsKeyword(idToken.Value) || idToken == SyntaxKind.SetKeyword || idToken == SyntaxKind.GetKeyword)
            {
                return true;
            }

            // If it *is* a keyword, but not an accessor, check a little farther along
            // to see if it should actually be parsed as a class member.
            switch (Token())
            {
                case SyntaxKind.OpenParenToken: // Method declaration
                case SyntaxKind.LessThanToken: // Generic Method declaration
                case SyntaxKind.ExclamationToken: // Non-null assertion on property name
                case SyntaxKind.ColonToken: // Type Annotation for declaration
                case SyntaxKind.EqualsToken: // Initializer for declaration
                case SyntaxKind.QuestionToken: // Not valid, but permitted so that it gets caught later on.
                    return true;
                default:
                    // Covers
                    //  - Semicolons     (declaration termination)
                    //  - Closing braces (end-of-class, must be declaration)
                    //  - End-of-files   (not valid, but permitted so that it gets caught later on)
                    //  - Line-breaks    (enabling *automatic semicolon insertion*)
                    return CanParseSemicolon();
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/parser.ts:7935-7941 — <c>parseClassStaticBlockDeclaration</c>.</summary>
    private ClassStaticBlockDeclaration ParseClassStaticBlockDeclaration(int pos, bool hasJSDoc, NodeArray<Node>? modifiers)
    {
        ParseExpectedToken(SyntaxKind.StaticKeyword);
        var body = ParseClassStaticBlockBody();
        var node = WithJSDoc(FinishNode(_factory.CreateClassStaticBlockDeclaration(body), pos), hasJSDoc);
        node.Modifiers = modifiers;
        return node;
    }

    /// <summary>ported from: src/compiler/parser.ts:7943-7956 — <c>parseClassStaticBlockBody</c>.</summary>
    private Block ParseClassStaticBlockBody()
    {
        var savedYieldContext = InYieldContext();
        var savedAwaitContext = InAwaitContext();

        SetYieldContext(false);
        SetAwaitContext(true);

        var body = ParseBlock(/*ignoreMissingOpenBrace*/ false);

        SetYieldContext(savedYieldContext);
        SetAwaitContext(savedAwaitContext);

        return body;
    }

    //
    // Decorators and modifiers
    //

    /// <summary>ported from: src/compiler/parser.ts:7958-7969 — <c>parseDecoratorExpression</c>.</summary>
    private Expression ParseDecoratorExpression()
    {
        if (InAwaitContext() && Token() == SyntaxKind.AwaitKeyword)
        {
            // `@await` is is disallowed in an [Await] context, but can cause parsing to go off the rails
            // This simply parses the missing identifier and moves on.
            var pos = GetNodePos();
            var awaitExpression = ParseIdentifier(Diagnostics.Expression_expected);
            NextToken();
            var memberExpression = ParseMemberExpressionRest(pos, awaitExpression, /*allowOptionalChain*/ true);
            return ParseCallExpressionRest(pos, memberExpression);
        }

        return ParseLeftHandSideExpressionOrHigher();
    }

    /// <summary>ported from: src/compiler/parser.ts:7971-7978 — <c>tryParseDecorator</c>.</summary>
    private Decorator? TryParseDecorator()
    {
        var pos = GetNodePos();
        if (!ParseOptional(SyntaxKind.AtToken))
        {
            return null;
        }

        var expression = DoInDecoratorContext(ParseDecoratorExpression);
        return FinishNode(_factory.CreateDecorator(expression), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:7980-8011 — <c>tryParseModifier</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>tryParse(nextTokenIsOnSameLineAndCanFollowModifier)</c> is <see cref="TryParseBool"/>
    /// (<c>SpeculationHelper</c> rolls back on a falsy result, which a plain <c>bool</c> cannot express), and the token
    /// is built with <c>CreateModifier</c> — see the class remarks.
    /// </remarks>
    private ModifierToken? TryParseModifier(bool hasSeenStaticModifier, bool permitConstAsModifier = false, bool stopOnStartOfClassStaticBlock = false)
    {
        var pos = GetNodePos();
        var kind = Token();

        if (Token() == SyntaxKind.ConstKeyword && permitConstAsModifier)
        {
            // We need to ensure that any subsequent modifiers appear on the same line
            // so that when 'const' is a standalone declaration, we don't issue an error.
            if (!TryParseBool(NextTokenIsOnSameLineAndCanFollowModifier))
            {
                return null;
            }
        }
        else if (stopOnStartOfClassStaticBlock && Token() == SyntaxKind.StaticKeyword && LookAheadBool(NextTokenIsOpenBrace))
        {
            return null;
        }
        else if (hasSeenStaticModifier && Token() == SyntaxKind.StaticKeyword)
        {
            return null;
        }
        else
        {
            if (!ParseAnyContextualModifier())
            {
                return null;
            }
        }

        return FinishNode(_factory.CreateModifier(kind), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:8013-8055 — <c>parseModifiers</c> (both overloads).</summary>
    /// <remarks>
    /// PORT-NOTE: the answer is <c>NodeArray&lt;Node&gt;?</c> rather than TS's <c>NodeArray&lt;ModifierLike&gt;</c> (see
    /// the class remarks). The list is carried as the <c>List&lt;Node&gt;</c> the factory's slot takes, so each
    /// <c>Core.Append</c> result — which is typed as the <c>IList&lt;T&gt;</c> interface — is viewed back as that list;
    /// it is the very array that went in, because <c>append</c> returns the argument unchanged for a non-null value.
    /// </remarks>
    private NodeArray<Node>? ParseModifiers(bool allowDecorators, bool permitConstAsModifier = false, bool stopOnStartOfClassStaticBlock = false)
    {
        var pos = GetNodePos();
        List<Node>? list = null;
        Node? decorator;
        ModifierToken? modifier;
        var hasSeenStaticModifier = false;
        var hasLeadingModifier = false;
        var hasTrailingDecorator = false;

        // Decorators should be contiguous in a list of modifiers but can potentially appear in two places (i.e., `[...leadingDecorators, ...leadingModifiers, ...trailingDecorators, ...trailingModifiers]`).
        // The leading modifiers *should* only contain `export` and `default` when trailingDecorators are present, but we'll handle errors for any other leading modifiers in the checker.
        // It is illegal to have both leadingDecorators and trailingDecorators, but we will report that as a grammar check in the checker.

        // parse leading decorators
        if (allowDecorators && Token() == SyntaxKind.AtToken)
        {
            while ((decorator = TryParseDecorator()) is not null)
            {
                list = Core.Append(list, decorator) as List<Node>;
            }
        }

        // parse leading modifiers
        while ((modifier = TryParseModifier(hasSeenStaticModifier, permitConstAsModifier, stopOnStartOfClassStaticBlock)) is not null)
        {
            if (modifier.Kind == SyntaxKind.StaticKeyword) hasSeenStaticModifier = true;
            list = Core.Append(list, modifier) as List<Node>;
            hasLeadingModifier = true;
        }

        // parse trailing decorators, but only if we parsed any leading modifiers
        if (hasLeadingModifier && allowDecorators && Token() == SyntaxKind.AtToken)
        {
            while ((decorator = TryParseDecorator()) is not null)
            {
                list = Core.Append(list, decorator) as List<Node>;
                hasTrailingDecorator = true;
            }
        }

        // parse trailing modifiers, but only if we parsed any trailing decorators
        if (hasTrailingDecorator)
        {
            while ((modifier = TryParseModifier(hasSeenStaticModifier, permitConstAsModifier, stopOnStartOfClassStaticBlock)) is not null)
            {
                if (modifier.Kind == SyntaxKind.StaticKeyword) hasSeenStaticModifier = true;
                list = Core.Append(list, modifier) as List<Node>;
            }
        }

        return list is not null ? CreateNodeArray(list, pos) : null;
    }

    /// <summary>ported from: src/compiler/parser.ts:8057-8066 — <c>parseModifiersForArrowFunction</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers <c>NodeArray&lt;Modifier&gt;</c>; the port answers <c>NodeArray&lt;ModifierToken&gt;?</c>
    /// because the only callers hand it straight to the arrow/function-expression factories, whose slot is
    /// <c>List&lt;ModifierToken&gt;?</c> — and the token really is a <see cref="ModifierToken"/> (see the class remarks).
    /// </remarks>
    private NodeArray<ModifierToken>? ParseModifiersForArrowFunction()
    {
        NodeArray<ModifierToken>? modifiers = null;
        if (Token() == SyntaxKind.AsyncKeyword)
        {
            var pos = GetNodePos();
            NextToken();
            var modifier = FinishNode(_factory.CreateModifier(SyntaxKind.AsyncKeyword), pos);
            modifiers = CreateNodeArray<ModifierToken>([modifier], pos);
        }

        return modifiers;
    }

    //
    // Class members
    //

    /// <summary>ported from: src/compiler/parser.ts:8068-8130 — <c>parseClassElement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers the <c>ClassElement</c> union and reaches it through <c>Debug.fail</c>; the port answers
    /// <see cref="IClassElement"/> (every arm implements it) and <see cref="Debug.Fail"/> is declared
    /// <c>[DoesNotReturn]</c>, so it terminates the method the same way. The accessor arms cast the wide node
    /// <see cref="ParseAccessorDeclaration"/> answers.
    /// </remarks>
    private IClassElement ParseClassElement()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        if (Token() == SyntaxKind.SemicolonToken)
        {
            NextToken();
            return WithJSDoc(FinishNode(_factory.CreateSemicolonClassElement(), pos), hasJSDoc);
        }

        var modifiers = ParseModifiers(/*allowDecorators*/ true, /*permitConstAsModifier*/ true, /*stopOnStartOfClassStaticBlock*/ true);
        if (Token() == SyntaxKind.StaticKeyword && LookAheadBool(NextTokenIsOpenBrace))
        {
            return ParseClassStaticBlockDeclaration(pos, hasJSDoc, modifiers);
        }

        if (ParseContextualModifier(SyntaxKind.GetKeyword))
        {
            return (IClassElement)ParseAccessorDeclaration(pos, hasJSDoc, modifiers, SyntaxKind.GetAccessor, ParserSignatureFlags.None);
        }

        if (ParseContextualModifier(SyntaxKind.SetKeyword))
        {
            return (IClassElement)ParseAccessorDeclaration(pos, hasJSDoc, modifiers, SyntaxKind.SetAccessor, ParserSignatureFlags.None);
        }

        if (Token() == SyntaxKind.ConstructorKeyword || Token() == SyntaxKind.StringLiteral)
        {
            var constructorDeclaration = TryParseConstructorDeclaration(pos, hasJSDoc, modifiers);
            if (constructorDeclaration is not null)
            {
                return constructorDeclaration;
            }
        }

        if (IsIndexSignature())
        {
            return ParseIndexSignatureDeclaration(pos, hasJSDoc, modifiers);
        }

        // It is very important that we check this *after* checking indexers because
        // the [ token can start an index signature or a computed property name
        if (
            ScannerUtilities.TokenIsIdentifierOrKeyword(Token()) ||
            Token() == SyntaxKind.StringLiteral ||
            Token() == SyntaxKind.NumericLiteral ||
            Token() == SyntaxKind.BigIntLiteral ||
            Token() == SyntaxKind.AsteriskToken ||
            Token() == SyntaxKind.OpenBracketToken
        )
        {
            var isAmbient = Core.Some(modifiers, IsDeclareModifier);
            if (isAmbient)
            {
                foreach (var m in modifiers!)
                {
                    m.Flags |= NodeFlags.Ambient;
                }

                return DoInsideOfContext(NodeFlags.Ambient, () => ParsePropertyOrMethodDeclaration(pos, hasJSDoc, modifiers));
            }

            return ParsePropertyOrMethodDeclaration(pos, hasJSDoc, modifiers);
        }

        if (modifiers is not null)
        {
            // treat this as a property declaration with a missing name.
            var name = CreateMissingNode<Identifier>(SyntaxKind.Identifier, /*reportAtCurrentPosition*/ true, Diagnostics.Declaration_expected);
            return ParsePropertyDeclaration(pos, hasJSDoc, modifiers, name, /*questionToken*/ null);
        }

        // 'isClassMemberStart' should have hinted not to attempt parsing.
        Debug.Fail("Should not have attempted to parse class member declaration.");
        // Unreachable: Debug.Fail is [DoesNotReturn] — TS's `Debug.fail` never returns either.
        return null!;
    }

    /// <summary>ported from: src/compiler/parser.ts:8132-8144 — <c>parseDecoratedExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers <c>PrimaryExpression</c> and casts the class arm; the port answers
    /// <see cref="IPrimaryExpression"/> — which both <c>ClassExpression</c> and
    /// <c>MissingDeclaration</c> implement — and casts the helper's wide <see cref="Node"/> to it.
    /// </remarks>
    private IPrimaryExpression ParseDecoratedExpression()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var modifiers = ParseModifiers(/*allowDecorators*/ true);
        if (Token() == SyntaxKind.ClassKeyword)
        {
            return (IPrimaryExpression)ParseClassDeclarationOrExpression(pos, hasJSDoc, modifiers, SyntaxKind.ClassExpression);
        }

        var missing = CreateMissingNode<MissingDeclaration>(SyntaxKind.MissingDeclaration, /*reportAtCurrentPosition*/ true, Diagnostics.Expression_expected);
        Utilities.SetTextRangePos(missing, pos);
        missing.Modifiers = modifiers;
        return missing;
    }

    /// <summary>ported from: src/compiler/parser.ts:8146-8148 — <c>parseClassExpression</c>.</summary>
    private ClassExpression ParseClassExpression()
        => (ClassExpression)ParseClassDeclarationOrExpression(GetNodePos(), HasPrecedingJSDocComment(), /*modifiers*/ null, SyntaxKind.ClassExpression);

    /// <summary>ported from: src/compiler/parser.ts:8150-8152 — <c>parseClassDeclaration</c>.</summary>
    private ClassDeclaration ParseClassDeclaration(int pos, bool hasJSDoc, NodeArray<Node>? modifiers)
        => (ClassDeclaration)ParseClassDeclarationOrExpression(pos, hasJSDoc, modifiers, SyntaxKind.ClassDeclaration);

    /// <summary>ported from: src/compiler/parser.ts:8154-8179 — <c>parseClassDeclarationOrExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers the <c>ClassLikeDeclaration</c> union and picks the factory with a ternary on the kind; the
    /// port answers the wide <see cref="Node"/> and keeps the ternary, so each arm's factory only ever reacts to the
    /// kind's own class (<see cref="ClassDeclaration"/> or <see cref="ClassExpression"/>) — the three callers cast to
    /// the arm they asked for.
    /// </remarks>
    private Node ParseClassDeclarationOrExpression(int pos, bool hasJSDoc, NodeArray<Node>? modifiers, SyntaxKind kind)
    {
        var savedAwaitContext = InAwaitContext();
        ParseExpected(SyntaxKind.ClassKeyword);

        // We don't parse the name here in await context, instead we will report a grammar error in the checker.
        var name = ParseNameOfClassDeclarationOrExpression();
        var typeParameters = ParseTypeParameters();
        if (Core.Some(modifiers, NodeTests.IsExportModifier)) SetAwaitContext(/*value*/ true);
        var heritageClauses = ParseHeritageClauses();

        NodeArray<IClassElement> members;
        if (ParseExpected(SyntaxKind.OpenBraceToken))
        {
            // ClassTail[Yield,Await] : (Modified) See 14.5
            //      ClassHeritage[?Yield,?Await]opt { ClassBody[?Yield,?Await]opt }
            members = ParseClassMembers();
            ParseExpected(SyntaxKind.CloseBraceToken);
        }
        else
        {
            members = CreateMissingList<IClassElement>();
        }

        SetAwaitContext(savedAwaitContext);
        return kind == SyntaxKind.ClassDeclaration
            ? WithJSDoc(FinishNode(_factory.CreateClassDeclaration(modifiers, name, typeParameters, heritageClauses, members), pos), hasJSDoc)
            : WithJSDoc(FinishNode(_factory.CreateClassExpression(modifiers, name, typeParameters, heritageClauses, members), pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:8181-8190 — <c>parseNameOfClassDeclarationOrExpression</c>.</summary>
    private Identifier? ParseNameOfClassDeclarationOrExpression()
    {
        // implements is a future reserved word so
        // 'class implements' might mean either
        // - class expression with omitted name, 'implements' starts heritage clause
        // - class with name 'implements'
        // 'isImplementsClause' helps to disambiguate between these two cases
        return IsBindingIdentifier() && !IsImplementsClause()
            ? CreateIdentifier(IsBindingIdentifier())
            : null;
    }

    /// <summary>ported from: src/compiler/parser.ts:8192-8194 — <c>isImplementsClause</c>.</summary>
    private bool IsImplementsClause() => Token() == SyntaxKind.ImplementsKeyword && LookAheadBool(NextTokenIsIdentifierOrKeyword);

    //
    // Heritage clauses
    //

    /// <summary>ported from: src/compiler/parser.ts:8196-8205 — <c>parseHeritageClauses</c>.</summary>
    private NodeArray<HeritageClause>? ParseHeritageClauses()
    {
        // ClassTail[Yield,Await] : (Modified) See 14.5
        //      ClassHeritage[?Yield,?Await]opt { ClassBody[?Yield,?Await]opt }

        if (IsHeritageClause())
        {
            return ParseList(ParsingContext.HeritageClauses, ParseHeritageClause);
        }

        return null;
    }

    /// <summary>ported from: src/compiler/parser.ts:8207-8214 — <c>parseHeritageClause</c>.</summary>
    /// <remarks>PORT-NOTE: see <see cref="ParseObjectBindingPattern"/> for the <c>!</c>.</remarks>
    private HeritageClause ParseHeritageClause()
    {
        var pos = GetNodePos();
        var tok = Token();
        Debug.Assert(tok == SyntaxKind.ExtendsKeyword || tok == SyntaxKind.ImplementsKeyword); // isListElement() should ensure this.
        NextToken();
        var types = ParseDelimitedList(ParsingContext.HeritageClauseElement, ParseExpressionWithTypeArguments)!;
        return FinishNode(_factory.CreateHeritageClause(tok, types), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:8216-8224 — <c>parseExpressionWithTypeArguments</c>.</summary>
    private ExpressionWithTypeArguments ParseExpressionWithTypeArguments()
    {
        var pos = GetNodePos();
        var expression = ParseLeftHandSideExpressionOrHigher();
        if (expression.Kind == SyntaxKind.ExpressionWithTypeArguments)
        {
            return (ExpressionWithTypeArguments)expression;
        }

        var typeArguments = TryParseTypeArguments();
        return FinishNode(_factory.CreateExpressionWithTypeArguments(expression, typeArguments), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:8226-8229 — <c>tryParseTypeArguments</c>.</summary>
    private NodeArray<ITypeNode>? TryParseTypeArguments()
        => Token() == SyntaxKind.LessThanToken
            ? ParseBracketedList(ParsingContext.TypeArguments, ParseType, SyntaxKind.LessThanToken, SyntaxKind.GreaterThanToken)
            : null;

    /// <summary>ported from: src/compiler/parser.ts:8231-8233 — <c>isHeritageClause</c>.</summary>
    private bool IsHeritageClause() => Token() == SyntaxKind.ExtendsKeyword || Token() == SyntaxKind.ImplementsKeyword;

    /// <summary>ported from: src/compiler/parser.ts:8235-8237 — <c>parseClassMembers</c>.</summary>
    private NodeArray<IClassElement> ParseClassMembers() => ParseList(ParsingContext.ClassMembers, ParseClassElement);

    //
    // Other declarations
    //

    /// <summary>ported from: src/compiler/parser.ts:8239-8246 — <c>parseInterfaceDeclaration</c>.</summary>
    private InterfaceDeclaration ParseInterfaceDeclaration(int pos, bool hasJSDoc, NodeArray<Node>? modifiers)
    {
        ParseExpected(SyntaxKind.InterfaceKeyword);
        var name = ParseIdentifier();
        var typeParameters = ParseTypeParameters();
        var heritageClauses = ParseHeritageClauses();
        var members = ParseObjectTypeMembers();
        var node = _factory.CreateInterfaceDeclaration(modifiers, name, typeParameters, heritageClauses, members);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:8249-8265 — <c>parseTypeAliasDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS spells the initializer as
    /// <c>token() === IntrinsicKeyword &amp;&amp; tryParse(parseKeywordAndNoDot) || parseType()</c> — the <c>intrinsic</c>
    /// keyword wins only when the speculative keyword parse succeeds and is not followed by a dot, and the type is parsed
    /// again otherwise. The port keeps that order and the same <c>tryParse</c> (whose probe is re-run by
    /// <c>TryParse</c>, exactly as TS's does).
    /// </remarks>
    private TypeAliasDeclaration ParseTypeAliasDeclaration(int pos, bool hasJSDoc, NodeArray<Node>? modifiers)
    {
        ParseExpected(SyntaxKind.TypeKeyword);
        if (_scanner.HasPrecedingLineBreak())
        {
            ParseErrorAtCurrentToken(Diagnostics.Line_break_not_permitted_here);
        }

        var name = ParseIdentifier();
        var typeParameters = ParseTypeParameters();
        ParseExpected(SyntaxKind.EqualsToken);
        ITypeNode type;
        var intrinsicKeyword = Token() == SyntaxKind.IntrinsicKeyword ? TryParse<ITypeNode?>(ParseKeywordAndNoDot) : null;
        if (intrinsicKeyword is not null)
        {
            type = intrinsicKeyword;
        }
        else
        {
            type = ParseType();
        }

        ParseSemicolon();
        var node = _factory.CreateTypeAliasDeclaration(modifiers, name, typeParameters, type);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:8267-8273 — <c>parseEnumMember</c>.</summary>
    private EnumMember ParseEnumMember()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var name = ParsePropertyName();
        var initializer = AllowInAnd(ParseInitializer);
        return WithJSDoc(FinishNode(_factory.CreateEnumMember(name, initializer), pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:8275-8288 — <c>parseEnumDeclaration</c>.</summary>
    /// <remarks>PORT-NOTE: see <see cref="ParseObjectBindingPattern"/> for the <c>!</c>.</remarks>
    private EnumDeclaration ParseEnumDeclaration(int pos, bool hasJSDoc, NodeArray<Node>? modifiers)
    {
        ParseExpected(SyntaxKind.EnumKeyword);
        var name = ParseIdentifier();
        NodeArray<EnumMember> members;
        if (ParseExpected(SyntaxKind.OpenBraceToken))
        {
            members = DoOutsideOfYieldAndAwaitContext(() => ParseDelimitedList(ParsingContext.EnumMembers, ParseEnumMember))!;
            ParseExpected(SyntaxKind.CloseBraceToken);
        }
        else
        {
            members = CreateMissingList<EnumMember>();
        }

        var node = _factory.CreateEnumDeclaration(modifiers, name, members);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    //
    // Modules and namespaces
    //

    /// <summary>ported from: src/compiler/parser.ts:8290-8299 — <c>parseModuleBlock</c>.</summary>
    private ModuleBlock ParseModuleBlock()
    {
        var pos = GetNodePos();
        NodeArray<IStatement> statements;
        if (ParseExpected(SyntaxKind.OpenBraceToken))
        {
            statements = ParseList(ParsingContext.BlockStatements, ParseStatement);
            ParseExpected(SyntaxKind.CloseBraceToken);
        }
        else
        {
            statements = CreateMissingList<IStatement>();
        }

        return FinishNode(_factory.CreateModuleBlock(statements), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:8303-8313 — <c>parseModuleOrNamespaceDeclaration</c>.</summary>
    private ModuleDeclaration ParseModuleOrNamespaceDeclaration(int pos, bool hasJSDoc, NodeArray<Node>? modifiers, NodeFlags flags)
    {
        // If we are parsing a dotted namespace name, we want to
        // propagate the 'Namespace' flag across the names if set.
        var namespaceFlag = flags & NodeFlags.Namespace;
        var name = (flags & NodeFlags.NestedNamespace) != 0 ? ParseIdentifierName() : ParseIdentifier();
        var body = ParseOptional(SyntaxKind.DotToken)
            ? (Node)ParseModuleOrNamespaceDeclaration(GetNodePos(), /*hasJSDoc*/ false, /*modifiers*/ null, NodeFlags.NestedNamespace | namespaceFlag)
            : ParseModuleBlock();
        var node = _factory.CreateModuleDeclaration(modifiers, name, body, flags);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:8315-8336 — <c>parseAmbientExternalModuleDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS narrows the literal arm to <c>StringLiteral</c>; the port casts for the same reason and interning
    /// the text needs the concrete class. <c>name</c> is the <c>strIdentifier | StringLiteral</c> pair the factory's
    /// <c>MemberExpression</c> slot takes, so it is declared as that.
    /// </remarks>
    private ModuleDeclaration ParseAmbientExternalModuleDeclaration(int pos, bool hasJSDoc, NodeArray<Node>? modifiersIn)
    {
        var flags = NodeFlags.None;
        MemberExpression name;
        if (Token() == SyntaxKind.GlobalKeyword)
        {
            // parse 'global' as name of global scope augmentation
            name = ParseIdentifier();
            flags |= NodeFlags.GlobalAugmentation;
        }
        else
        {
            var literal = (StringLiteral)ParseLiteralNode();
            literal.Text = InternIdentifier(literal.Text ?? "");
            name = literal;
        }

        ModuleBlock? body = null;
        if (Token() == SyntaxKind.OpenBraceToken)
        {
            body = ParseModuleBlock();
        }
        else
        {
            ParseSemicolon();
        }

        var node = _factory.CreateModuleDeclaration(modifiersIn, name, body, flags);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:8338-8354 — <c>parseModuleDeclaration</c>.</summary>
    private ModuleDeclaration ParseModuleDeclaration(int pos, bool hasJSDoc, NodeArray<Node>? modifiersIn)
    {
        var flags = NodeFlags.None;
        if (Token() == SyntaxKind.GlobalKeyword)
        {
            // global augmentation
            return ParseAmbientExternalModuleDeclaration(pos, hasJSDoc, modifiersIn);
        }
        else if (ParseOptional(SyntaxKind.NamespaceKeyword))
        {
            flags |= NodeFlags.Namespace;
        }
        else
        {
            ParseExpected(SyntaxKind.ModuleKeyword);
            if (Token() == SyntaxKind.StringLiteral)
            {
                return ParseAmbientExternalModuleDeclaration(pos, hasJSDoc, modifiersIn);
            }
        }

        return ParseModuleOrNamespaceDeclaration(pos, hasJSDoc, modifiersIn, flags);
    }

    //
    // Imports
    //

    /// <summary>ported from: src/compiler/parser.ts:8356-8359 — <c>isExternalModuleReference</c>.</summary>
    private bool IsExternalModuleReference() => Token() == SyntaxKind.RequireKeyword && LookAheadBool(NextTokenIsOpenParen);

    /// <summary>ported from: src/compiler/parser.ts:8361-8363 — <c>nextTokenIsOpenParen</c>.</summary>
    private bool NextTokenIsOpenParen() => NextToken() == SyntaxKind.OpenParenToken;

    /// <summary>ported from: src/compiler/parser.ts:8365-8367 — <c>nextTokenIsOpenBrace</c>.</summary>
    private bool NextTokenIsOpenBrace() => NextToken() == SyntaxKind.OpenBraceToken;

    /// <summary>ported from: src/compiler/parser.ts:8369-8371 — <c>nextTokenIsSlash</c>.</summary>
    private bool NextTokenIsSlash() => NextToken() == SyntaxKind.SlashToken;

    /// <summary>ported from: src/compiler/parser.ts:8373-8382 — <c>parseNamespaceExportDeclaration</c>.</summary>
    /// <remarks>
    /// NamespaceExportDeclaration nodes cannot have decorators or modifiers, so TS attaches them through
    /// <c>Mutable&lt;&gt;</c> so the grammar checker can report them; the port assigns the generated setter.
    /// </remarks>
    private NamespaceExportDeclaration ParseNamespaceExportDeclaration(int pos, bool hasJSDoc, NodeArray<Node>? modifiers)
    {
        ParseExpected(SyntaxKind.AsKeyword);
        ParseExpected(SyntaxKind.NamespaceKeyword);
        var name = ParseIdentifier();
        ParseSemicolon();
        var node = _factory.CreateNamespaceExportDeclaration(name);
        // NamespaceExportDeclaration nodes cannot have decorators or modifiers, so we attach them here so we can report them in the grammar checker
        node.Modifiers = modifiers;
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:8384-8423 — <c>parseImportDeclarationOrImportEqualsDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers <c>ImportEqualsDeclaration | ImportDeclaration</c>; both are statements, so the port
    /// answers <see cref="IStatement"/>, and the import phase (TS's <c>ImportPhaseModifierSyntaxKind</c> — a subset of
    /// <c>SyntaxKind</c> with no C# counterpart) is a plain nullable <see cref="SyntaxKind"/>.
    /// </remarks>
    private IStatement ParseImportDeclarationOrImportEqualsDeclaration(int pos, bool hasJSDoc, NodeArray<Node>? modifiers)
    {
        ParseExpected(SyntaxKind.ImportKeyword);

        var afterImportPos = _scanner.GetTokenFullStart();

        // We don't parse the identifier here in await context, instead we will report a grammar error in the checker.
        Identifier? identifier = null;
        if (IsIdentifier())
        {
            identifier = ParseIdentifier();
        }

        SyntaxKind? phaseModifier = null;
        if (
            identifier?.EscapedText == "type" &&
            (Token() != SyntaxKind.FromKeyword || IsIdentifier() && LookAheadBool(NextTokenIsFromKeywordOrEqualsToken)) &&
            (IsIdentifier() || TokenAfterImportDefinitelyProducesImportDeclaration())
        )
        {
            phaseModifier = SyntaxKind.TypeKeyword;
            identifier = IsIdentifier() ? ParseIdentifier() : null;
        }
        else if (
            identifier?.EscapedText == "defer" &&
            (Token() == SyntaxKind.FromKeyword ? !LookAheadBool(NextTokenIsStringLiteral) : Token() != SyntaxKind.CommaToken && Token() != SyntaxKind.EqualsToken)
        )
        {
            phaseModifier = SyntaxKind.DeferKeyword;
            identifier = IsIdentifier() ? ParseIdentifier() : null;
        }

        if (identifier is not null && !TokenAfterImportedIdentifierDefinitelyProducesImportDeclaration() && phaseModifier != SyntaxKind.DeferKeyword)
        {
            return ParseImportEqualsDeclaration(pos, hasJSDoc, modifiers, identifier, phaseModifier == SyntaxKind.TypeKeyword);
        }

        var importClause = TryParseImportClause(identifier, afterImportPos, phaseModifier, /*skipJsDocLeadingAsterisks*/ false);
        var moduleSpecifier = ParseModuleSpecifier();
        var attributes = TryParseImportAttributes();

        ParseSemicolon();
        var node = _factory.CreateImportDeclaration(modifiers, importClause, moduleSpecifier, attributes);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:8425-8439 — <c>tryParseImportClause</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>skipJsDocLeadingAsterisks</c> defaults to <c>false</c>; the port spells it as a defaulted parameter.</remarks>
    private ImportClause? TryParseImportClause(Identifier? identifier, int pos, SyntaxKind? phaseModifier, bool skipJsDocLeadingAsterisks = false)
    {
        // ImportDeclaration:
        //  import ImportClause from ModuleSpecifier ;
        //  import ModuleSpecifier;
        ImportClause? importClause = null;
        if (
            identifier is not null || // import id
            Token() == SyntaxKind.AsteriskToken || // import *
            Token() == SyntaxKind.OpenBraceToken // import {
        )
        {
            importClause = ParseImportClause(identifier, pos, phaseModifier, skipJsDocLeadingAsterisks);
            ParseExpected(SyntaxKind.FromKeyword);
        }

        return importClause;
    }

    /// <summary>ported from: src/compiler/parser.ts:8441-8446 — <c>tryParseImportAttributes</c>.</summary>
    private ImportAttributes? TryParseImportAttributes()
    {
        var currentToken = Token();
        if (currentToken == SyntaxKind.WithKeyword || (currentToken == SyntaxKind.AssertKeyword && !_scanner.HasPrecedingLineBreak()))
        {
            return ParseImportAttributes(currentToken);
        }

        return null;
    }

    /// <summary>ported from: src/compiler/parser.ts:8448-8454 — <c>parseImportAttribute</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers <c>IdentifierName | StringLiteral</c> for the name; the port casts the literal arm to the
    /// <c>MemberExpression</c> the factory slot takes (an identifier name is one already).
    /// </remarks>
    private ImportAttribute ParseImportAttribute()
    {
        var pos = GetNodePos();
        var name = ScannerUtilities.TokenIsIdentifierOrKeyword(Token())
            ? (MemberExpression)ParseIdentifierName()
            : (MemberExpression)ParseLiteralLikeNode(SyntaxKind.StringLiteral);
        ParseExpected(SyntaxKind.ColonToken);
        var value = ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ true);
        return FinishNode(_factory.CreateImportAttribute(name, value), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:8456-8480 — <c>parseImportAttributes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types the keyword as <c>AssertKeyword | WithKeyword</c> and its <c>skipKeyword</c> as the literal
    /// type <c>true</c>; the port takes a <see cref="SyntaxKind"/> and a <c>bool</c> defaulting to <c>false</c> (TS's
    /// <c>undefined</c>). The empty array in the fallback arm is a <c>NodeArray&lt;ImportAttribute&gt;</c>, which is what
    /// the factory stores.
    /// </remarks>
    private ImportAttributes ParseImportAttributes(SyntaxKind token, bool skipKeyword = false)
    {
        var pos = GetNodePos();
        if (!skipKeyword)
        {
            ParseExpected(token);
        }

        var openBracePosition = _scanner.GetTokenStart();
        if (ParseExpected(SyntaxKind.OpenBraceToken))
        {
            var multiLine = _scanner.HasPrecedingLineBreak();
            var elements = ParseDelimitedList(ParsingContext.ImportAttributes, ParseImportAttribute, /*considerSemicolonAsDelimiter*/ true)!;
            if (!ParseExpected(SyntaxKind.CloseBraceToken))
            {
                var lastError = Core.LastOrUndefined(_parseDiagnostics);
                if (lastError is not null && lastError.Code == Diagnostics._0_expected.Code)
                {
                    Utilities.AddRelatedInfo(
                        lastError,
                        Utilities.CreateDetachedDiagnostic(_fileName, _sourceText, openBracePosition, 1, Diagnostics.The_parser_expected_to_find_a_1_to_match_the_0_token_here, "{", "}"));
                }
            }

            return FinishNode(_factory.CreateImportAttributes(elements, multiLine, token), pos);
        }
        else
        {
            var elements = CreateNodeArray<ImportAttribute>([], GetNodePos(), /*end*/ null, /*hasTrailingComma*/ false);
            return FinishNode(_factory.CreateImportAttributes(elements, /*multiLine*/ false, token), pos);
        }
    }

    /// <summary>ported from: src/compiler/parser.ts:8482-8484 — <c>tokenAfterImportDefinitelyProducesImportDeclaration</c>.</summary>
    private bool TokenAfterImportDefinitelyProducesImportDeclaration()
        => Token() == SyntaxKind.AsteriskToken || Token() == SyntaxKind.OpenBraceToken;

    /// <summary>ported from: src/compiler/parser.ts:8486-8490 — <c>tokenAfterImportedIdentifierDefinitelyProducesImportDeclaration</c>.</summary>
    private bool TokenAfterImportedIdentifierDefinitelyProducesImportDeclaration()
        // In `import id ___`, the current token decides whether to produce
        // an ImportDeclaration or ImportEqualsDeclaration.
        => Token() == SyntaxKind.CommaToken || Token() == SyntaxKind.FromKeyword;

    /// <summary>ported from: src/compiler/parser.ts:8492-8499 — <c>parseImportEqualsDeclaration</c>.</summary>
    private ImportEqualsDeclaration ParseImportEqualsDeclaration(int pos, bool hasJSDoc, NodeArray<Node>? modifiers, Identifier identifier, bool isTypeOnly)
    {
        ParseExpected(SyntaxKind.EqualsToken);
        var moduleReference = ParseModuleReference();
        ParseSemicolon();
        var node = _factory.CreateImportEqualsDeclaration(modifiers, isTypeOnly, identifier, moduleReference);
        var finished = WithJSDoc(FinishNode(node, pos), hasJSDoc);
        return finished;
    }

    /// <summary>ported from: src/compiler/parser.ts:8501-8527 — <c>parseImportClause</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the <c>namedBindings</c> slot is TS's <c>NamespaceImport | NamedImports</c>; both are nodes with no
    /// closer common type, so the wide <see cref="Node"/> holds it and the factory's <c>Node?</c> slot takes it.
    /// </remarks>
    private ImportClause ParseImportClause(Identifier? identifier, int pos, SyntaxKind? phaseModifier, bool skipJsDocLeadingAsterisks)
    {
        // ImportClause:
        //  ImportedDefaultBinding
        //  NameSpaceImport
        //  NamedImports
        //  ImportedDefaultBinding, NameSpaceImport
        //  ImportedDefaultBinding, NamedImports

        // If there was no default import or if there is comma token after default import
        // parse namespace or named imports
        Node? namedBindings = null;
        if (
            identifier is null ||
            ParseOptional(SyntaxKind.CommaToken)
        )
        {
            if (skipJsDocLeadingAsterisks) _scanner.SetSkipJsDocLeadingAsterisks(true);
            if (Token() == SyntaxKind.AsteriskToken)
            {
                namedBindings = ParseNamespaceImport();
            }
            else
            {
                namedBindings = ParseNamedImportsOrExports(SyntaxKind.NamedImports);
            }

            if (skipJsDocLeadingAsterisks) _scanner.SetSkipJsDocLeadingAsterisks(false);
        }

        return FinishNode(_factory.CreateImportClause(phaseModifier, identifier, namedBindings), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:8529-8533 — <c>parseModuleReference</c>.</summary>
    /// <remarks>PORT-NOTE: answers <see cref="Node"/> for the union <c>ExternalModuleReference | EntityName</c>.</remarks>
    private Node ParseModuleReference()
        => IsExternalModuleReference()
            ? ParseExternalModuleReference()
            : ParseEntityName(/*allowReservedWords*/ false);

    /// <summary>ported from: src/compiler/parser.ts:8535-8542 — <c>parseExternalModuleReference</c>.</summary>
    private ExternalModuleReference ParseExternalModuleReference()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.RequireKeyword);
        ParseExpected(SyntaxKind.OpenParenToken);
        var expression = ParseModuleSpecifier();
        ParseExpected(SyntaxKind.CloseParenToken);
        return FinishNode(_factory.CreateExternalModuleReference(expression), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:8544-8556 — <c>parseModuleSpecifier</c>.</summary>
    private Expression ParseModuleSpecifier()
    {
        if (Token() == SyntaxKind.StringLiteral)
        {
            var result = ParseLiteralNode();
            result.Text = InternIdentifier(result.Text ?? "");
            return result;
        }
        else
        {
            // We allow arbitrary expressions here, even though the grammar only allows string
            // literals.  We check to ensure that it is only a string literal later in the grammar
            // check pass.
            return ParseExpression();
        }
    }

    /// <summary>ported from: src/compiler/parser.ts:8558-8566 — <c>parseNamespaceImport</c>.</summary>
    private NamespaceImport ParseNamespaceImport()
    {
        // NameSpaceImport:
        //  * as ImportedBinding
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.AsteriskToken);
        ParseExpected(SyntaxKind.AsKeyword);
        var name = ParseIdentifier();
        return FinishNode(_factory.CreateNamespaceImport(name), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:8568-8570 — <c>canParseModuleExportName</c>.</summary>
    private bool CanParseModuleExportName() => ScannerUtilities.TokenIsIdentifierOrKeyword(Token()) || Token() == SyntaxKind.StringLiteral;

    /// <summary>ported from: src/compiler/parser.ts:8572-8574 — <c>parseModuleExportName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers the <c>ModuleExportName</c> union (<c>Identifier | StringLiteral</c>); the port answers the
    /// wide <see cref="Node"/> and its callers narrow where they know the arm.
    /// </remarks>
    private Node ParseModuleExportName(Func<Identifier> parseName)
        => Token() == SyntaxKind.StringLiteral ? ParseLiteralNode() : parseName();

    /// <summary>ported from: src/compiler/parser.ts:8576-8593 — <c>parseNamedImportsOrExports</c> (both overloads).</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares one overload per answer (<c>NamedImports</c> / <c>NamedExports</c>) so a caller can name
    /// the class it asked for; C# can only tell them apart by the kind argument, so the port keeps one member and
    /// answers the wide <see cref="Node"/> — the caller that already knows the kind narrows it, and the factory's
    /// <c>exportClause</c> slot takes the wide type anyway.
    /// </remarks>
    private Node ParseNamedImportsOrExports(SyntaxKind kind)
    {
        var pos = GetNodePos();

        // NamedImports:
        //  { }
        //  { ImportsList }
        //  { ImportsList, }

        // ImportsList:
        //  ImportSpecifier
        //  ImportsList, ImportSpecifier
        var node = kind == SyntaxKind.NamedImports
            ? (Node)_factory.CreateNamedImports(ParseBracketedList(ParsingContext.ImportOrExportSpecifiers, ParseImportSpecifier, SyntaxKind.OpenBraceToken, SyntaxKind.CloseBraceToken))
            : _factory.CreateNamedExports(ParseBracketedList(ParsingContext.ImportOrExportSpecifiers, ParseExportSpecifier, SyntaxKind.OpenBraceToken, SyntaxKind.CloseBraceToken));
        return FinishNode(node, pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:8595-8598 — <c>parseExportSpecifier</c>.</summary>
    private ExportSpecifier ParseExportSpecifier()
    {
        var hasJSDoc = HasPrecedingJSDocComment();
        return WithJSDoc((ExportSpecifier)ParseImportOrExportSpecifier(SyntaxKind.ExportSpecifier), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:8600-8602 — <c>parseImportSpecifier</c>.</summary>
    private ImportSpecifier ParseImportSpecifier()
        => (ImportSpecifier)ParseImportOrExportSpecifier(SyntaxKind.ImportSpecifier);

    /// <summary>ported from: src/compiler/parser.ts:8604-8695 — <c>parseImportOrExportSpecifier</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers the <c>ImportOrExportSpecifier</c> union; the port answers the wide <see cref="Node"/> and
    /// the two wrappers above cast to the class their kind builds. <c>propertyName</c>/<c>name</c> are TS's
    /// <c>ModuleExportName</c> union (identifier or string literal). TS closes <c>parseNameWithKeywordCheck</c> over the
    /// three <c>check*</c> locals and the port keeps that as a local function with the same captures.
    /// </remarks>
    private Node ParseImportOrExportSpecifier(SyntaxKind kind)
    {
        var pos = GetNodePos();
        // ImportSpecifier:
        //   BindingIdentifier
        //   ModuleExportName as BindingIdentifier
        // ExportSpecifier:
        //   ModuleExportName
        //   ModuleExportName as ModuleExportName
        var checkIdentifierIsKeyword = Utilities.IsKeyword(Token()) && !IsIdentifier();
        var checkIdentifierStart = _scanner.GetTokenStart();
        var checkIdentifierEnd = _scanner.GetTokenEnd();
        var isTypeOnly = false;
        Node? propertyName = null;
        var canParseAsKeyword = true;
        Node name = ParseModuleExportName(ParseNameWithKeywordCheck);
        if (name is Identifier nameIdentifier && nameIdentifier.EscapedText == "type")
        {
            // If the first token of an import specifier is 'type', there are a lot of possibilities,
            // especially if we see 'as' afterwards:
            //
            // import { type } from "mod";          - isTypeOnly: false,   name: type
            // import { type as } from "mod";       - isTypeOnly: true,    name: as
            // import { type as as } from "mod";    - isTypeOnly: false,   name: as,    propertyName: type
            // import { type as as as } from "mod"; - isTypeOnly: true,    name: as,    propertyName: as
            if (Token() == SyntaxKind.AsKeyword)
            {
                // { type as ...? }
                var firstAs = ParseIdentifierName();
                if (Token() == SyntaxKind.AsKeyword)
                {
                    // { type as as ...? }
                    var secondAs = ParseIdentifierName();
                    if (CanParseModuleExportName())
                    {
                        // { type as as something }
                        // { type as as "something" }
                        isTypeOnly = true;
                        propertyName = firstAs;
                        name = ParseModuleExportName(ParseNameWithKeywordCheck);
                        canParseAsKeyword = false;
                    }
                    else
                    {
                        // { type as as }
                        propertyName = name;
                        name = secondAs;
                        canParseAsKeyword = false;
                    }
                }
                else if (CanParseModuleExportName())
                {
                    // { type as something }
                    // { type as "something" }
                    propertyName = name;
                    canParseAsKeyword = false;
                    name = ParseModuleExportName(ParseNameWithKeywordCheck);
                }
                else
                {
                    // { type as }
                    isTypeOnly = true;
                    name = firstAs;
                }
            }
            else if (CanParseModuleExportName())
            {
                // { type something ...? }
                // { type "something" ...? }
                isTypeOnly = true;
                name = ParseModuleExportName(ParseNameWithKeywordCheck);
            }
        }

        if (canParseAsKeyword && Token() == SyntaxKind.AsKeyword)
        {
            propertyName = name;
            ParseExpected(SyntaxKind.AsKeyword);
            name = ParseModuleExportName(ParseNameWithKeywordCheck);
        }

        if (kind == SyntaxKind.ImportSpecifier)
        {
            if (name.Kind != SyntaxKind.Identifier)
            {
                // ImportSpecifier casts "name" to Identifier below, so make sure it's an identifier
                ParseErrorAt(ScannerUtilities.SkipTrivia(_sourceText, name.Pos), name.End, Diagnostics.Identifier_expected);
                name = Utilities.SetTextRangePosEnd(CreateMissingNode<Identifier>(SyntaxKind.Identifier, /*reportAtCurrentPosition*/ false), name.Pos, name.Pos);
            }
            else if (checkIdentifierIsKeyword)
            {
                ParseErrorAt(checkIdentifierStart, checkIdentifierEnd, Diagnostics.Identifier_expected);
            }
        }

        Node node = kind == SyntaxKind.ImportSpecifier
            ? _factory.CreateImportSpecifier(isTypeOnly, (MemberExpression?)propertyName, (Identifier)name)
            : _factory.CreateExportSpecifier(isTypeOnly, propertyName, name);
        return FinishNode(node, pos);

        Identifier ParseNameWithKeywordCheck()
        {
            checkIdentifierIsKeyword = Utilities.IsKeyword(Token()) && !IsIdentifier();
            checkIdentifierStart = _scanner.GetTokenStart();
            checkIdentifierEnd = _scanner.GetTokenEnd();
            return ParseIdentifierName();
        }
    }

    /// <summary>ported from: src/compiler/parser.ts:8697-8699 — <c>parseNamespaceExport</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the factory's slot is a <c>MemberExpression</c>, which both arms of the
    /// <c>ModuleExportName</c> union are (an identifier name and a string literal).
    /// </remarks>
    private NamespaceExport ParseNamespaceExport(int pos)
        => FinishNode(_factory.CreateNamespaceExport((MemberExpression)ParseModuleExportName(() => ParseIdentifierName())), pos);

    //
    // Exports
    //

    /// <summary>ported from: src/compiler/parser.ts:8701-8734 — <c>parseExportDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>exportClause</c> is TS's <c>NamedExportBindings</c> union, held here as the wide
    /// <see cref="Node"/> the factory's slot also takes.
    /// </remarks>
    private ExportDeclaration ParseExportDeclaration(int pos, bool hasJSDoc, NodeArray<Node>? modifiers)
    {
        var savedAwaitContext = InAwaitContext();
        SetAwaitContext(/*value*/ true);
        Node? exportClause = null;
        Expression? moduleSpecifier = null;
        ImportAttributes? attributes = null;
        var isTypeOnly = ParseOptional(SyntaxKind.TypeKeyword);
        var namespaceExportPos = GetNodePos();
        if (ParseOptional(SyntaxKind.AsteriskToken))
        {
            if (ParseOptional(SyntaxKind.AsKeyword))
            {
                exportClause = ParseNamespaceExport(namespaceExportPos);
            }

            ParseExpected(SyntaxKind.FromKeyword);
            moduleSpecifier = ParseModuleSpecifier();
        }
        else
        {
            exportClause = ParseNamedImportsOrExports(SyntaxKind.NamedExports);
            // It is not uncommon to accidentally omit the 'from' keyword. Additionally, in editing scenarios,
            // the 'from' keyword can be parsed as a named export when the export clause is unterminated (i.e. `export { from "moduleName";`)
            // If we don't have a 'from' keyword, see if we have a string literal such that ASI won't take effect.
            if (Token() == SyntaxKind.FromKeyword || (Token() == SyntaxKind.StringLiteral && !_scanner.HasPrecedingLineBreak()))
            {
                ParseExpected(SyntaxKind.FromKeyword);
                moduleSpecifier = ParseModuleSpecifier();
            }
        }

        var currentToken = Token();
        if (moduleSpecifier is not null && (currentToken == SyntaxKind.WithKeyword || currentToken == SyntaxKind.AssertKeyword) && !_scanner.HasPrecedingLineBreak())
        {
            attributes = ParseImportAttributes(currentToken);
        }

        ParseSemicolon();
        SetAwaitContext(savedAwaitContext);
        var node = _factory.CreateExportDeclaration(modifiers, isTypeOnly, exportClause, moduleSpecifier, attributes);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:8736-8748 — <c>parseExportAssignment</c>.</summary>
    private ExportAssignment ParseExportAssignment(int pos, bool hasJSDoc, NodeArray<Node>? modifiers)
    {
        var savedAwaitContext = InAwaitContext();
        SetAwaitContext(/*value*/ true);
        bool? isExportEquals = null;
        if (ParseOptional(SyntaxKind.EqualsToken))
        {
            isExportEquals = true;
        }
        else
        {
            ParseExpected(SyntaxKind.DefaultKeyword);
        }

        var expression = ParseAssignmentExpressionOrHigher(/*allowReturnTypeInArrowFunction*/ true);
        ParseSemicolon();
        SetAwaitContext(savedAwaitContext);
        var node = _factory.CreateExportAssignment(modifiers, isExportEquals, expression);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }
}
