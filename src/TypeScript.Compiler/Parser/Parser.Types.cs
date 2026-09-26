namespace TypeScript.Compiler;

// ported from: src/compiler/parser.ts:3785-4964 — the `// TYPES` section: the type grammar (`parseType` down through
// the union/intersection/conditional productions), the type members an object type or interface is made of, the
// signature/parameter helpers TS keeps between them, and the JSDoc type helpers that share those productions.
//
// PORT-NOTE: `TypeNode` is a union with no C# counterpart, so every production that answers one answers `ITypeNode`
// (the classification interface, §12.9); `TypeElement` likewise becomes `ITypeElement`. The two things `ITypeNode`
// does not carry — `pos`/`end` and the ability to stand in for the `Node` class a predicate takes — are handled by
// an explicit cast at the few sites that need them.
internal sealed partial class Parser
{
    //
    // Type references and entity names
    //

    /// <summary>ported from: src/compiler/parser.ts:3787-3789 — <c>parseEntityNameOfTypeReference</c>.</summary>
    private Node ParseEntityNameOfTypeReference()
        => ParseEntityName(/*allowReservedWords*/ true, Diagnostics.Type_expected);

    /// <summary>ported from: src/compiler/parser.ts:3791-3795 — <c>parseTypeArgumentsOfTypeReference</c>.</summary>
    private NodeArray<ITypeNode>? ParseTypeArgumentsOfTypeReference()
    {
        if (!_scanner.HasPrecedingLineBreak() && ReScanLessThanToken() == SyntaxKind.LessThanToken)
        {
            return ParseBracketedList(ParsingContext.TypeArguments, ParseType, SyntaxKind.LessThanToken, SyntaxKind.GreaterThanToken);
        }

        return null;
    }

    /// <summary>ported from: src/compiler/parser.ts:3797-3806 — <c>parseTypeReference</c>.</summary>
    private TypeReferenceNode ParseTypeReference()
    {
        var pos = GetNodePos();
        return FinishNode(
            _factory.CreateTypeReferenceNode(
                ParseEntityNameOfTypeReference(),
                ParseTypeArgumentsOfTypeReference()),
            pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:3809-3823 — <c>typeHasArrowFunctionBlockingParseError</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS destructures the <c>FunctionType | ConstructorType</c> arm out of the union; the port narrows to
    /// <see cref="FunctionOrConstructorTypeNodeBase"/>, which is the class the two of them share and which is exactly
    /// where <c>parameters</c>/<c>type</c> are declared. <c>isMissingList</c> takes the covariant
    /// <see cref="INodeArray{T}"/> view, so the <c>NodeArray&lt;ParameterDeclaration&gt;</c> goes in as is.
    /// </remarks>
    private static bool TypeHasArrowFunctionBlockingParseError(ITypeNode node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.TypeReference:
                return Utilities.NodeIsMissing(((TypeReferenceNode)node).TypeName);
            case SyntaxKind.FunctionType:
            case SyntaxKind.ConstructorType:
            {
                var functionOrConstructorType = (FunctionOrConstructorTypeNodeBase)node;
                return IsMissingList(functionOrConstructorType.Parameters)
                    || TypeHasArrowFunctionBlockingParseError(functionOrConstructorType.Type!);
            }
            case SyntaxKind.ParenthesizedType:
                return TypeHasArrowFunctionBlockingParseError(((ParenthesizedTypeNode)node).Type!);
            default:
                return false;
        }
    }

    //
    // JSDoc types (`this`, `*`, `?`, `!`, `(...)`), which share this grammar's productions
    //

    /// <summary>ported from: src/compiler/parser.ts:3825-3828 — <c>parseThisTypePredicate</c>.</summary>
    private TypePredicateNode ParseThisTypePredicate(ThisTypeNode lhs)
    {
        NextToken();
        return FinishNode(_factory.CreateTypePredicateNode(/*assertsModifier*/ null, lhs, ParseType()), lhs.Pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:3830-3834 — <c>parseThisTypeNode</c>.</summary>
    private ThisTypeNode ParseThisTypeNode()
    {
        var pos = GetNodePos();
        NextToken();
        return FinishNode(_factory.CreateThisTypeNode(), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:3836-3840 — <c>parseJSDocAllType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers <c>JSDocAllType | JSDocOptionalType</c> (the <c>=</c> that turns one into the other is
    /// consumed by <c>parseJSDocType</c>, which is why the union appears here at all); both reach
    /// <see cref="ITypeNode"/>, which is what the port answers.
    /// </remarks>
    private ITypeNode ParseJSDocAllType()
    {
        var pos = GetNodePos();
        NextToken();
        return FinishNode(_factory.CreateJSDocAllType(), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:3842-3846 — <c>parseJSDocNonNullableType</c>.</summary>
    private ITypeNode ParseJSDocNonNullableType()
    {
        var pos = GetNodePos();
        NextToken();
        return FinishNode(_factory.CreateJSDocNonNullableType(ParseNonArrayType(), /*postfix*/ false), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:3848-3876 — <c>parseJSDocUnknownOrNullableType</c>.</summary>
    private ITypeNode ParseJSDocUnknownOrNullableType()
    {
        var pos = GetNodePos();
        // skip the ?
        NextToken();

        // Need to lookahead to decide if this is a nullable or unknown type.

        // Here are cases where we'll pick the unknown type:
        //
        //      Foo(?,
        //      { a: ? }
        //      Foo(?)
        //      Foo<?>
        //      Foo(?=
        //      (?|
        if (Token() == SyntaxKind.CommaToken
            || Token() == SyntaxKind.CloseBraceToken
            || Token() == SyntaxKind.CloseParenToken
            || Token() == SyntaxKind.GreaterThanToken
            || Token() == SyntaxKind.EqualsToken
            || Token() == SyntaxKind.BarToken)
        {
            return FinishNode(_factory.CreateJSDocUnknownType(), pos);
        }
        else
        {
            return FinishNode(_factory.CreateJSDocNullableType(ParseType(), /*postfix*/ false), pos);
        }
    }

    /// <summary>ported from: src/compiler/parser.ts:3878-3887 — <c>parseJSDocFunctionType</c>.</summary>
    private ITypeNode ParseJSDocFunctionType()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        if (TryParse(NextTokenIsOpenParen))
        {
            var parameters = ParseParameters(ParserSignatureFlags.Type | ParserSignatureFlags.JSDoc);
            var type = ParseReturnType(SyntaxKind.ColonToken, /*isType*/ false);
            return WithJSDoc(FinishNode(_factory.CreateJSDocFunctionType(parameters, type), pos), hasJSDoc);
        }

        return FinishNode(_factory.CreateTypeReferenceNode(ParseIdentifierName(), /*typeArguments*/ null), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:3889-3908 — <c>parseJSDocParameter</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS passes <c>name!</c>, an assertion over a name that is absent for every JSDoc parameter except
    /// <c>this</c>/<c>new</c>; the port's slot is <c>object?</c>, so the same absent value travels without the
    /// assertion (and with the same runtime result).
    /// </remarks>
    private ParameterDeclaration ParseJSDocParameter()
    {
        var pos = GetNodePos();
        Identifier? name = null;
        if (Token() == SyntaxKind.ThisKeyword || Token() == SyntaxKind.NewKeyword)
        {
            name = ParseIdentifierName();
            ParseExpected(SyntaxKind.ColonToken);
        }

        return FinishNode(
            _factory.CreateParameterDeclaration(
                /*modifiers*/ null,
                /*dotDotDotToken*/ null,
                // TODO(rbuckton): JSDoc parameters don't have names (except `this`/`new`), should we manufacture an empty identifier?
                name,
                /*questionToken*/ null,
                ParseJSDocType(),
                /*initializer*/ null),
            pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:3910-3944 — <c>parseJSDocType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>terminate:</c> label is a labeled <c>break</c> out of the <c>while</c> from inside the
    /// <c>switch</c>; C#'s <c>break</c> leaves only the <c>switch</c>, so the port carries the same decision in a flag
    /// (the loop body has no other exits to keep in step).
    /// </remarks>
    private ITypeNode ParseJSDocType()
    {
        _scanner.SetSkipJsDocLeadingAsterisks(true);
        var pos = GetNodePos();
        if (ParseOptional(SyntaxKind.ModuleKeyword))
        {
            // TODO(rbuckton): We never set the type for a JSDocNamepathType. What should we put here?
            var moduleTag = _factory.CreateJSDocNamepathType(/*type*/ null);
            var terminate = false;
            while (!terminate)
            {
                switch (Token())
                {
                    case SyntaxKind.CloseBraceToken:
                    case SyntaxKind.EndOfFileToken:
                    case SyntaxKind.CommaToken:
                    case SyntaxKind.WhitespaceTrivia:
                        terminate = true;
                        break;
                    default:
                        NextTokenJSDoc();
                        break;
                }
            }

            _scanner.SetSkipJsDocLeadingAsterisks(false);
            return FinishNode(moduleTag, pos);
        }

        var hasDotDotDot = ParseOptional(SyntaxKind.DotDotDotToken);
        var type = ParseTypeOrTypePredicate();
        _scanner.SetSkipJsDocLeadingAsterisks(false);
        if (hasDotDotDot)
        {
            type = FinishNode(_factory.CreateJSDocVariadicType(type), pos);
        }

        if (Token() == SyntaxKind.EqualsToken)
        {
            NextToken();
            return FinishNode(_factory.CreateJSDocOptionalType(type), pos);
        }

        return type;
    }

    //
    // Type queries, parameters and type parameters
    //

    /// <summary>ported from: src/compiler/parser.ts:3946-3953 — <c>parseTypeQuery</c>.</summary>
    private TypeQueryNode ParseTypeQuery()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.TypeOfKeyword);
        var entityName = ParseEntityName(/*allowReservedWords*/ true);
        // Make sure we perform ASI to prevent parsing the next line's type arguments as part of an instantiation expression.
        var typeArguments = !_scanner.HasPrecedingLineBreak() ? TryParseTypeArguments() : null;
        return FinishNode(_factory.CreateTypeQueryNode(entityName, typeArguments), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:3955-3985 — <c>parseTypeParameter</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>modifiers</c> slot is <c>NodeArray&lt;Modifier&gt;</c> while <c>parseModifiers</c> answers the
    /// <c>ModifierLike</c> array (the same array, covariant in TS, <c>List&lt;Node&gt;</c> here) — see
    /// <see cref="AsModifierTokens"/>. The <c>expression</c> slot is assigned after the node is built, exactly as TS
    /// does, because the factory's <c>defaultType</c> is a different slot.
    /// </remarks>
    private TypeParameterDeclaration ParseTypeParameter()
    {
        var pos = GetNodePos();
        var modifiers = ParseModifiers(/*allowDecorators*/ false, /*permitConstAsModifier*/ true);
        var name = ParseIdentifier();
        ITypeNode? constraint = null;
        Expression? expression = null;
        if (ParseOptional(SyntaxKind.ExtendsKeyword))
        {
            // It's not uncommon for people to write improper constraints to a generic.  If the
            // user writes a constraint that is an expression and not an actual type, then parse
            // it out as an expression (so we can recover well), but report that a type is needed
            // instead.
            if (IsStartOfType() || !IsStartOfExpression())
            {
                constraint = ParseType();
            }
            else
            {
                // It was not a type, and it looked like an expression.  Parse out an expression
                // here so we recover well.  Note: it is important that we call parseUnaryExpression
                // and not parseExpression here.  If the user has:
                //
                //      <T extends "">
                //
                // We do *not* want to consume the `>` as we're consuming the expression for "".
                expression = ParseUnaryExpressionOrHigher();
            }
        }

        var defaultType = ParseOptional(SyntaxKind.EqualsToken) ? ParseType() : null;
        var node = _factory.CreateTypeParameterDeclaration(AsModifierTokens(modifiers), name, constraint, defaultType);
        node.Expression = expression;
        return FinishNode(node, pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:3987-3991 — <c>parseTypeParameters</c>.</summary>
    private NodeArray<TypeParameterDeclaration>? ParseTypeParameters()
    {
        if (Token() == SyntaxKind.LessThanToken)
        {
            return ParseBracketedList(ParsingContext.TypeParameters, ParseTypeParameter, SyntaxKind.LessThanToken, SyntaxKind.GreaterThanToken);
        }

        return null;
    }

    /// <summary>ported from: src/compiler/parser.ts:3993-3999 — <c>isStartOfParameter</c>.</summary>
    private bool IsStartOfParameter(bool isJSDocParameter)
        => Token() == SyntaxKind.DotDotDotToken
            || IsBindingIdentifierOrPrivateIdentifierOrPattern()
            || Utilities.IsModifierKind(Token())
            || Token() == SyntaxKind.AtToken
            || IsStartOfType(/*inStartOfParameter*/ !isJSDocParameter);

    /// <summary>ported from: src/compiler/parser.ts:4001-4017 — <c>parseNameOfParameter</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers <c>Identifier | BindingPattern</c>; both reach the <see cref="Node"/> class in the port's
    /// model, which is what the callers (a parameter's <c>name</c> slot) take. <c>some(modifiers)</c> is a truthiness
    /// test over the array, which for a node array is "not empty" — <see cref="Core.Some{T}"/> with no predicate.
    /// </remarks>
    private Node ParseNameOfParameter(List<Node>? modifiers)
    {
        // FormalParameter [Yield,Await]:
        //      BindingElement[?Yield,?Await]
        var name = (Node)ParseIdentifierOrPattern(Diagnostics.Private_identifiers_cannot_be_used_as_parameters);
        if (Utilities.GetFullWidth(name) == 0 && !Core.Some(modifiers) && Utilities.IsModifierKind(Token()))
        {
            // in cases like
            // 'use strict'
            // function foo(static)
            // isParameter('static') === true, because of isModifier('static')
            // however 'static' is not a legal identifier in a strict mode.
            // so result of this function will be ParameterDeclaration (flags = 0, name = missing, type = undefined, initializer = undefined)
            // and current token will not change => parsing of the enclosing parameter list will last till the end of time (or OOM)
            // to avoid this we'll advance cursor to the next token.
            NextToken();
        }

        return name;
    }

    /// <summary>ported from: src/compiler/parser.ts:4019-4024 — <c>isParameterNameStart</c>.</summary>
    private bool IsParameterNameStart()
    {
        // Be permissive about await and yield by calling isBindingIdentifier instead of isIdentifier; disallowing
        // them during a speculative parse leads to many more follow-on errors than allowing the function to parse then later
        // complaining about the use of the keywords.
        return IsBindingIdentifier() || Token() == SyntaxKind.OpenBracketToken || Token() == SyntaxKind.OpenBraceToken;
    }

    /// <summary>ported from: src/compiler/parser.ts:4026-4028 — <c>parseParameter</c>.</summary>
    private ParameterDeclaration ParseParameter(bool inOuterAwaitContext)
        => ParseParameterWorker(inOuterAwaitContext)!;

    /// <summary>ported from: src/compiler/parser.ts:4030-4032 — <c>parseParameterForSpeculation</c>.</summary>
    private ParameterDeclaration? ParseParameterForSpeculation(bool inOuterAwaitContext)
        => ParseParameterWorker(inOuterAwaitContext, /*allowAmbiguity*/ false);

    /// <summary>ported from: src/compiler/parser.ts:4034-4091 — <c>parseParameterWorker</c> (both declarations).</summary>
    /// <remarks>
    /// PORT-NOTE: TS overloads this on <c>allowAmbiguity</c> so that the always-successful call shape (used by a
    /// bracketed parameter list) does not carry the <c>undefined</c>; C# cannot overload on a boolean value, so one
    /// member answers <c>ParameterDeclaration?</c> and <see cref="ParseParameter"/> supplies the <c>!</c> that TS's
    /// first overload encodes. <c>createIdentifier</c> is the parser's own (it emits the "identifier expected"
    /// diagnostic against the current token).
    /// <para>
    /// The early <c>undefined</c> return deliberately leaves <c>_topLevel</c> false: TS does not restore it on that
    /// path either (only the speculation machinery's own state is rolled back), and the value is restored by the next
    /// <em>successful</em> parameter parse.
    /// </para>
    /// </remarks>
    private ParameterDeclaration? ParseParameterWorker(bool inOuterAwaitContext, bool allowAmbiguity = true)
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();

        // FormalParameter [Yield,Await]:
        //      BindingElement[?Yield,?Await]

        // Decorators are parsed in the outer [Await] context, the rest of the parameter is parsed in the function's [Await] context.
        var modifiers = inOuterAwaitContext
            ? DoInAwaitContext(() => ParseModifiers(/*allowDecorators*/ true))
            : DoOutsideOfAwaitContext(() => ParseModifiers(/*allowDecorators*/ true));

        if (Token() == SyntaxKind.ThisKeyword)
        {
            var thisNode = _factory.CreateParameterDeclaration(
                modifiers,
                /*dotDotDotToken*/ null,
                CreateIdentifier(/*isIdentifier*/ true),
                /*questionToken*/ null,
                ParseTypeAnnotation(),
                /*initializer*/ null);

            var modifier = Core.FirstOrUndefined(modifiers);
            if (modifier is not null)
            {
                ParseErrorAtRange(modifier, Diagnostics.Neither_decorators_nor_modifiers_may_be_applied_to_this_parameters);
            }

            return WithJSDoc(FinishNode(thisNode, pos), hasJSDoc);
        }

        var savedTopLevel = _topLevel;
        _topLevel = false;

        var dotDotDotToken = ParseOptionalToken(SyntaxKind.DotDotDotToken);

        if (!allowAmbiguity && !IsParameterNameStart())
        {
            return null;
        }

        var node = WithJSDoc(
            FinishNode(
                _factory.CreateParameterDeclaration(
                    modifiers,
                    (PunctuationToken?)dotDotDotToken,
                    ParseNameOfParameter(modifiers),
                    (PunctuationToken?)ParseOptionalToken(SyntaxKind.QuestionToken),
                    ParseTypeAnnotation(),
                    ParseInitializer()),
                pos),
            hasJSDoc);
        _topLevel = savedTopLevel;
        return node;
    }

    /// <summary>ported from: src/compiler/parser.ts:4093-4099 — <c>parseReturnType</c> (both declarations).</summary>
    /// <remarks>
    /// PORT-NOTE: TS's first overload promises a non-optional <c>TypeNode</c> because a <c>=&gt;</c> return type always
    /// parses; C# cannot overload on the token, so the port answers <c>ITypeNode?</c> and the callers that need the
    /// promise (the function/constructor type productions) pass it on to a slot that is optional anyway.
    /// </remarks>
    private ITypeNode? ParseReturnType(SyntaxKind returnToken, bool isType)
    {
        if (ShouldParseReturnType(returnToken, isType))
        {
            return AllowConditionalTypesAnd(ParseTypeOrTypePredicate);
        }

        return null;
    }

    /// <summary>ported from: src/compiler/parser.ts:4101-4116 — <c>shouldParseReturnType</c>.</summary>
    private bool ShouldParseReturnType(SyntaxKind returnToken, bool isType)
    {
        if (returnToken == SyntaxKind.EqualsGreaterThanToken)
        {
            ParseExpected(returnToken);
            return true;
        }
        else if (ParseOptional(SyntaxKind.ColonToken))
        {
            return true;
        }
        else if (isType && Token() == SyntaxKind.EqualsGreaterThanToken)
        {
            // This is easy to get backward, especially in type contexts, so parse the type anyway
            ParseErrorAtCurrentToken(Diagnostics._0_expected, ScannerUtilities.TokenToString(SyntaxKind.ColonToken));
            NextToken();
            return true;
        }

        return false;
    }

    /// <summary>ported from: src/compiler/parser.ts:4118-4148 — <c>parseParametersWorker</c> (both declarations).</summary>
    /// <remarks>
    /// PORT-NOTE: TS's overload pair (narrowed on <c>allowAmbiguity</c>) collapses to one member answering
    /// <c>NodeArray&lt;ParameterDeclaration&gt;?</c>; <see cref="ParseParameters"/> appends the <c>!</c> of the
    /// first overload. <c>setYieldContext</c>/<c>setAwaitContext</c> are the shared <see cref="SetContextFlag"/>.
    /// </remarks>
    private NodeArray<ParameterDeclaration>? ParseParametersWorker(ParserSignatureFlags flags, bool allowAmbiguity)
    {
        // FormalParameters [Yield,Await]: (modified)
        //      [empty]
        //      FormalParameterList[?Yield,Await]
        //
        // FormalParameter[Yield,Await]: (modified)
        //      BindingElement[?Yield,Await]
        //
        // BindingElement [Yield,Await]: (modified)
        //      SingleNameBinding[?Yield,?Await]
        //      BindingPattern[?Yield,?Await]Initializer [In, ?Yield,?Await] opt
        //
        // SingleNameBinding [Yield,Await]:
        //      BindingIdentifier[?Yield,?Await]Initializer [In, ?Yield,?Await] opt
        var savedYieldContext = InYieldContext();
        var savedAwaitContext = InAwaitContext();

        SetContextFlag((flags & ParserSignatureFlags.Yield) != 0, NodeFlags.YieldContext);
        SetContextFlag((flags & ParserSignatureFlags.Await) != 0, NodeFlags.AwaitContext);

        var parameters = (flags & ParserSignatureFlags.JSDoc) != 0
            ? ParseDelimitedList(ParsingContext.JSDocParameters, ParseJSDocParameter)
            : ParseDelimitedList(ParsingContext.Parameters, () => allowAmbiguity ? ParseParameter(savedAwaitContext) : ParseParameterForSpeculation(savedAwaitContext));

        SetContextFlag(savedYieldContext, NodeFlags.YieldContext);
        SetContextFlag(savedAwaitContext, NodeFlags.AwaitContext);

        return parameters;
    }

    /// <summary>ported from: src/compiler/parser.ts:4150-4171 — <c>parseParameters</c>.</summary>
    private NodeArray<ParameterDeclaration> ParseParameters(ParserSignatureFlags flags)
    {
        // FormalParameters [Yield,Await]: (modified)
        //      [empty]
        //      FormalParameterList[?Yield,Await]
        //
        // FormalParameter[Yield,Await]: (modified)
        //      BindingElement[?Yield,Await]
        //
        // BindingElement [Yield,Await]: (modified)
        //      SingleNameBinding[?Yield,?Await]
        //      BindingPattern[?Yield,?Await]Initializer [In, ?Yield,?Await] opt
        //
        // SingleNameBinding [Yield,Await]:
        //      BindingIdentifier[?Yield,?Await]Initializer [In, ?Yield,?Await] opt
        if (!ParseExpected(SyntaxKind.OpenParenToken))
        {
            return CreateMissingList<ParameterDeclaration>();
        }

        var parameters = ParseParametersWorker(flags, /*allowAmbiguity*/ true)!;
        ParseExpected(SyntaxKind.CloseParenToken);
        return parameters;
    }

    //
    // Type members (signatures, index signatures, property/method signatures)
    //

    /// <summary>ported from: src/compiler/parser.ts:4173-4182 — <c>parseTypeMemberSemicolon</c>.</summary>
    private void ParseTypeMemberSemicolon()
    {
        // We allow type members to be separated by commas or (possibly ASI) semicolons.
        // First check if it was a comma.  If so, we're done with the member.
        if (ParseOptional(SyntaxKind.CommaToken))
        {
            return;
        }

        // Didn't have a comma.  We must have a (possible ASI) semicolon.
        ParseSemicolon();
    }

    /// <summary>ported from: src/compiler/parser.ts:4184-4199 — <c>parseSignatureMember</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's result is <c>CallSignatureDeclaration | ConstructSignatureDeclaration</c> and its
    /// <c>withJSDoc(finishNode(node, pos), …)</c> runs once on that union. The port's <c>withJSDoc</c> keeps the
    /// caller's class, which a union no longer has, so each arm builds, finishes and wraps its own node — the same two
    /// productions, each executed once (the productions above the branch are outside it and run once either way).
    /// </remarks>
    private ITypeElement ParseSignatureMember(SyntaxKind kind)
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        if (kind == SyntaxKind.ConstructSignature)
        {
            ParseExpected(SyntaxKind.NewKeyword);
        }

        var typeParameters = ParseTypeParameters();
        var parameters = ParseParameters(ParserSignatureFlags.Type);
        var type = ParseReturnType(SyntaxKind.ColonToken, /*isType*/ true);
        ParseTypeMemberSemicolon();
        if (kind == SyntaxKind.CallSignature)
        {
            return WithJSDoc(FinishNode(_factory.CreateCallSignature(typeParameters, parameters, type), pos), hasJSDoc);
        }

        return WithJSDoc(FinishNode(_factory.CreateConstructSignature(typeParameters, parameters, type), pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:4201-4203 — <c>isIndexSignature</c>.</summary>
    private bool IsIndexSignature()
        => Token() == SyntaxKind.OpenBracketToken && LookAhead(IsUnambiguouslyIndexSignature);

    /// <summary>ported from: src/compiler/parser.ts:4205-4258 — <c>isUnambiguouslyIndexSignature</c>.</summary>
    private bool IsUnambiguouslyIndexSignature()
    {
        // The only allowed sequence is:
        //
        //   [id:
        //
        // However, for error recovery, we also check the following cases:
        //
        //   [...
        //   [id,
        //   [id?,
        //   [id?:
        //   [id?]
        //   [public id
        //   [private id
        //   [protected id
        //   []
        //
        NextToken();
        if (Token() == SyntaxKind.DotDotDotToken || Token() == SyntaxKind.CloseBracketToken)
        {
            return true;
        }

        if (Utilities.IsModifierKind(Token()))
        {
            NextToken();
            if (IsIdentifier())
            {
                return true;
            }
        }
        else if (!IsIdentifier())
        {
            return false;
        }
        else
        {
            // Skip the identifier
            NextToken();
        }

        // A colon signifies a well formed indexer
        // A comma should be a badly formed indexer because comma expressions are not allowed
        // in computed properties.
        if (Token() == SyntaxKind.ColonToken || Token() == SyntaxKind.CommaToken)
        {
            return true;
        }

        // Question mark could be an indexer with an optional property,
        // or it could be a conditional expression in a computed property.
        if (Token() != SyntaxKind.QuestionToken)
        {
            return false;
        }

        // If any of the following tokens are after the question mark, it cannot
        // be a conditional expression, so treat it as an indexer.
        NextToken();
        return Token() == SyntaxKind.ColonToken || Token() == SyntaxKind.CommaToken || Token() == SyntaxKind.CloseBracketToken;
    }

    /// <summary>ported from: src/compiler/parser.ts:4260-4266 — <c>parseIndexSignatureDeclaration</c>.</summary>
    private IndexSignatureDeclaration ParseIndexSignatureDeclaration(int pos, bool hasJSDoc, List<Node>? modifiers)
    {
        var parameters = ParseBracketedList<ParameterDeclaration>(ParsingContext.Parameters, () => ParseParameter(/*inOuterAwaitContext*/ false), SyntaxKind.OpenBracketToken, SyntaxKind.CloseBracketToken);
        var type = ParseTypeAnnotation();
        ParseTypeMemberSemicolon();
        var node = _factory.CreateIndexSignature(modifiers, parameters, type);
        return WithJSDoc(FinishNode(node, pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:4268-4290 — <c>parsePropertyOrMethodSignature</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: as in <see cref="ParseSignatureMember"/>, TS finishes the <c>PropertySignature | MethodSignature</c>
    /// union once and calls <c>withJSDoc</c> on it, and the port cannot (the wrapper keeps the caller's class). The
    /// <c>parseTypeMemberSemicolon()</c> that TS runs once after the branch runs once in each arm here. TS's
    /// <c>modifiers</c> is <c>NodeArray&lt;Modifier&gt;</c> — see <see cref="AsModifierTokens"/> for the projection the
    /// invariant <c>List&lt;T&gt;</c> forces.
    /// </remarks>
    private ITypeElement ParsePropertyOrMethodSignature(int pos, bool hasJSDoc, List<Node>? modifiers)
    {
        var name = ParsePropertyName();
        var questionToken = (PunctuationToken?)ParseOptionalToken(SyntaxKind.QuestionToken);
        if (Token() == SyntaxKind.OpenParenToken || Token() == SyntaxKind.LessThanToken)
        {
            // Method signatures don't exist in expression contexts.  So they have neither
            // [Yield] nor [Await]
            var typeParameters = ParseTypeParameters();
            var parameters = ParseParameters(ParserSignatureFlags.Type);
            var type = ParseReturnType(SyntaxKind.ColonToken, /*isType*/ true);
            var method = _factory.CreateMethodSignature(AsModifierTokens(modifiers), name, questionToken, typeParameters, parameters, type);
            ParseTypeMemberSemicolon();
            return WithJSDoc(FinishNode(method, pos), hasJSDoc);
        }
        else
        {
            var type = ParseTypeAnnotation();
            var node = _factory.CreatePropertySignature(AsModifierTokens(modifiers), name, questionToken, type);
            // Although type literal properties cannot not have initializers, we attempt
            // to parse an initializer so we can report in the checker that an interface
            // property or type literal property cannot have an initializer.
            if (Token() == SyntaxKind.EqualsToken) node.Initializer = ParseInitializer();
            ParseTypeMemberSemicolon();
            return WithJSDoc(FinishNode(node, pos), hasJSDoc);
        }
    }

    /// <summary>ported from: src/compiler/parser.ts:4292-4328 — <c>isTypeMemberStart</c>.</summary>
    private bool IsTypeMemberStart()
    {
        // Return true if we have the start of a signature member
        if (Token() == SyntaxKind.OpenParenToken
            || Token() == SyntaxKind.LessThanToken
            || Token() == SyntaxKind.GetKeyword
            || Token() == SyntaxKind.SetKeyword)
        {
            return true;
        }

        var idToken = false;
        // Eat up all modifiers, but hold on to the last one in case it is actually an identifier
        while (Utilities.IsModifierKind(Token()))
        {
            idToken = true;
            NextToken();
        }

        // Index signatures and computed property names are type members
        if (Token() == SyntaxKind.OpenBracketToken)
        {
            return true;
        }

        // Try to get the first property-like token following all modifiers
        if (IsLiteralPropertyName())
        {
            idToken = true;
            NextToken();
        }

        // If we were able to get any potential identifier, check that it is
        // the start of a member declaration
        if (idToken)
        {
            return Token() == SyntaxKind.OpenParenToken
                || Token() == SyntaxKind.LessThanToken
                || Token() == SyntaxKind.QuestionToken
                || Token() == SyntaxKind.ColonToken
                || Token() == SyntaxKind.CommaToken
                || CanParseSemicolon();
        }

        return false;
    }

    /// <summary>ported from: src/compiler/parser.ts:4330-4352 — <c>parseTypeMember</c>.</summary>
    private ITypeElement ParseTypeMember()
    {
        if (Token() == SyntaxKind.OpenParenToken || Token() == SyntaxKind.LessThanToken)
        {
            return ParseSignatureMember(SyntaxKind.CallSignature);
        }

        if (Token() == SyntaxKind.NewKeyword && LookAhead(NextTokenIsOpenParenOrLessThan))
        {
            return ParseSignatureMember(SyntaxKind.ConstructSignature);
        }

        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var modifiers = ParseModifiers(/*allowDecorators*/ false);
        if (ParseContextualModifier(SyntaxKind.GetKeyword))
        {
            return (ITypeElement)ParseAccessorDeclaration(pos, hasJSDoc, modifiers, SyntaxKind.GetAccessor, ParserSignatureFlags.Type);
        }

        if (ParseContextualModifier(SyntaxKind.SetKeyword))
        {
            return (ITypeElement)ParseAccessorDeclaration(pos, hasJSDoc, modifiers, SyntaxKind.SetAccessor, ParserSignatureFlags.Type);
        }

        if (IsIndexSignature())
        {
            return ParseIndexSignatureDeclaration(pos, hasJSDoc, modifiers);
        }

        return ParsePropertyOrMethodSignature(pos, hasJSDoc, modifiers);
    }

    /// <summary>ported from: src/compiler/parser.ts:4354-4357 — <c>nextTokenIsOpenParenOrLessThan</c>.</summary>
    private bool NextTokenIsOpenParenOrLessThan()
    {
        NextToken();
        return Token() == SyntaxKind.OpenParenToken || Token() == SyntaxKind.LessThanToken;
    }

    /// <summary>ported from: src/compiler/parser.ts:4359-4361 — <c>nextTokenIsDot</c>.</summary>
    private bool NextTokenIsDot() => NextToken() == SyntaxKind.DotToken;

    /// <summary>ported from: src/compiler/parser.ts:4363-4371 — <c>nextTokenIsOpenParenOrLessThanOrDot</c>.</summary>
    private bool NextTokenIsOpenParenOrLessThanOrDot()
    {
        switch (NextToken())
        {
            case SyntaxKind.OpenParenToken:
            case SyntaxKind.LessThanToken:
            case SyntaxKind.DotToken:
                return true;
        }

        return false;
    }

    //
    // Type literals, mapped types and tuples
    //

    /// <summary>ported from: src/compiler/parser.ts:4373-4376 — <c>parseTypeLiteral</c>.</summary>
    private TypeLiteralNode ParseTypeLiteral()
    {
        var pos = GetNodePos();
        return FinishNode(_factory.CreateTypeLiteralNode(ParseObjectTypeMembers()), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:4378-4389 — <c>parseObjectTypeMembers</c>.</summary>
    private NodeArray<ITypeElement> ParseObjectTypeMembers()
    {
        NodeArray<ITypeElement> members;
        if (ParseExpected(SyntaxKind.OpenBraceToken))
        {
            members = ParseList(ParsingContext.TypeMembers, ParseTypeMember);
            ParseExpected(SyntaxKind.CloseBraceToken);
        }
        else
        {
            members = CreateMissingList<ITypeElement>();
        }

        return members;
    }

    /// <summary>ported from: src/compiler/parser.ts:4391-4400 — <c>isStartOfMappedType</c>.</summary>
    private bool IsStartOfMappedType()
    {
        NextToken();
        if (Token() == SyntaxKind.PlusToken || Token() == SyntaxKind.MinusToken)
        {
            return NextToken() == SyntaxKind.ReadonlyKeyword;
        }

        if (Token() == SyntaxKind.ReadonlyKeyword)
        {
            NextToken();
        }

        return Token() == SyntaxKind.OpenBracketToken && NextTokenIsIdentifier() && NextToken() == SyntaxKind.InKeyword;
    }

    /// <summary>ported from: src/compiler/parser.ts:4402-4408 — <c>parseMappedTypeParameter</c>.</summary>
    private TypeParameterDeclaration ParseMappedTypeParameter()
    {
        var pos = GetNodePos();
        var name = ParseIdentifierName();
        ParseExpected(SyntaxKind.InKeyword);
        var type = ParseType();
        return FinishNode(_factory.CreateTypeParameterDeclaration(/*modifiers*/ null, name, type, /*defaultType*/ null), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:4410-4436 — <c>parseMappedType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS builds the <c>readonly</c>/<c>+</c>/<c>-</c> and <c>?</c>/<c>+</c>/<c>-</c> tokens through
    /// <c>parseTokenNode&lt;ReadonlyKeyword | PlusToken | MinusToken&gt;()</c>; the port names the classes the model's
    /// slots are declared with — <see cref="Token"/> for the <c>readonly</c> slot (a plain keyword) and
    /// <see cref="PunctuationToken"/> for the question slot (all three kinds are punctuation).
    /// </remarks>
    private MappedTypeNode ParseMappedType()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.OpenBraceToken);
        Token? readonlyToken = null;
        if (Token() == SyntaxKind.ReadonlyKeyword || Token() == SyntaxKind.PlusToken || Token() == SyntaxKind.MinusToken)
        {
            readonlyToken = ParseTokenNode<Token>();
            if (readonlyToken.Kind != SyntaxKind.ReadonlyKeyword)
            {
                ParseExpected(SyntaxKind.ReadonlyKeyword);
            }
        }

        ParseExpected(SyntaxKind.OpenBracketToken);
        var typeParameter = ParseMappedTypeParameter();
        var nameType = ParseOptional(SyntaxKind.AsKeyword) ? ParseType() : null;
        ParseExpected(SyntaxKind.CloseBracketToken);
        PunctuationToken? questionToken = null;
        if (Token() == SyntaxKind.QuestionToken || Token() == SyntaxKind.PlusToken || Token() == SyntaxKind.MinusToken)
        {
            questionToken = ParseTokenNode<PunctuationToken>();
            if (questionToken.Kind != SyntaxKind.QuestionToken)
            {
                ParseExpected(SyntaxKind.QuestionToken);
            }
        }

        var type = ParseTypeAnnotation();
        ParseSemicolon();
        var members = ParseList(ParsingContext.TypeMembers, ParseTypeMember);
        ParseExpected(SyntaxKind.CloseBraceToken);
        return FinishNode(_factory.CreateMappedTypeNode(readonlyToken, typeParameter, nameType, questionToken, type, members), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:4438-4451 — <c>parseTupleElementType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>type.pos</c>/<c>type.flags</c> are read off the union's common base in TS; <c>ITypeNode</c> does
    /// not carry them, so the port casts to <see cref="Node"/> for the comparison and the assignment (the same slots on
    /// the same object).
    /// </remarks>
    private ITypeNode ParseTupleElementType()
    {
        var pos = GetNodePos();
        if (ParseOptional(SyntaxKind.DotDotDotToken))
        {
            return FinishNode(_factory.CreateRestTypeNode(ParseType()), pos);
        }

        var type = ParseType();
        if (NodeTests.IsJSDocNullableType((Node)type) && ((Node)type).Pos == ((Node)((JSDocNullableType)type).Type!).Pos)
        {
            var node = _factory.CreateOptionalTypeNode(((JSDocNullableType)type).Type);
            Utilities.SetTextRange(node, (Node)type);
            node.Flags = type.Flags;
            return node;
        }

        return type;
    }

    /// <summary>ported from: src/compiler/parser.ts:4453-4455 — <c>isNextTokenColonOrQuestionColon</c>.</summary>
    private bool IsNextTokenColonOrQuestionColon()
        => NextToken() == SyntaxKind.ColonToken
            || (Token() == SyntaxKind.QuestionToken && NextToken() == SyntaxKind.ColonToken);

    /// <summary>ported from: src/compiler/parser.ts:4457-4462 — <c>isTupleElementName</c>.</summary>
    private bool IsTupleElementName()
    {
        if (Token() == SyntaxKind.DotDotDotToken)
        {
            return ScannerUtilities.TokenIsIdentifierOrKeyword(NextToken()) && IsNextTokenColonOrQuestionColon();
        }

        return ScannerUtilities.TokenIsIdentifierOrKeyword(Token()) && IsNextTokenColonOrQuestionColon();
    }

    /// <summary>ported from: src/compiler/parser.ts:4464-4477 — <c>parseTupleElementNameOrTupleElementType</c>.</summary>
    private ITypeNode ParseTupleElementNameOrTupleElementType()
    {
        if (LookAhead(IsTupleElementName))
        {
            var pos = GetNodePos();
            var hasJSDoc = HasPrecedingJSDocComment();
            var dotDotDotToken = (PunctuationToken?)ParseOptionalToken(SyntaxKind.DotDotDotToken);
            var name = ParseIdentifierName();
            var questionToken = (PunctuationToken?)ParseOptionalToken(SyntaxKind.QuestionToken);
            ParseExpected(SyntaxKind.ColonToken);
            var type = ParseTupleElementType();
            var node = _factory.CreateNamedTupleMember(dotDotDotToken, name, questionToken, type);
            return WithJSDoc(FinishNode(node, pos), hasJSDoc);
        }

        return ParseTupleElementType();
    }

    /// <summary>ported from: src/compiler/parser.ts:4479-4487 — <c>parseTupleType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's factory slot is <c>readonly (TypeNode | NamedTupleMember)[]</c>, whose C# mapping is
    /// <c>List&lt;Node&gt;</c> — the union's common base (§12.5), not the <c>NodeArray&lt;ITypeNode&gt;</c> this list
    /// builds. TS relies on array covariance; the invariant <c>List&lt;T&gt;</c> needs the projection in
    /// <see cref="AsNodeList"/>.
    /// </remarks>
    private TupleTypeNode ParseTupleType()
    {
        var pos = GetNodePos();
        var elements = ParseBracketedList(ParsingContext.TupleElementTypes, ParseTupleElementNameOrTupleElementType, SyntaxKind.OpenBracketToken, SyntaxKind.CloseBracketToken);
        return FinishNode(_factory.CreateTupleTypeNode(AsNodeList(elements)), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:4489-4495 — <c>parseParenthesizedType</c>.</summary>
    private ITypeNode ParseParenthesizedType()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.OpenParenToken);
        var type = ParseType();
        ParseExpected(SyntaxKind.CloseParenToken);
        return FinishNode(_factory.CreateParenthesizedType(type), pos);
    }

    //
    // Function and constructor types
    //

    /// <summary>ported from: src/compiler/parser.ts:4497-4506 — <c>parseModifiersForConstructorType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS creates the token with <c>factoryCreateToken</c>, whose object is a plain token that its
    /// <c>NodeArray&lt;Modifier&gt;</c> accepts structurally. The port's <c>modifiers</c> slot is declared with the
    /// <see cref="ModifierToken"/> class, so the token is built through <c>createModifier</c> — the factory member that
    /// exists for exactly this (see its own PORT-NOTE) and that also applies the token's transform flags.
    /// </remarks>
    private List<ModifierToken>? ParseModifiersForConstructorType()
    {
        List<ModifierToken>? modifiers = null;
        if (Token() == SyntaxKind.AbstractKeyword)
        {
            var pos = GetNodePos();
            NextToken();
            var modifier = FinishNode(_factory.CreateModifier(SyntaxKind.AbstractKeyword), pos);
            modifiers = CreateNodeArray<ModifierToken>([modifier], pos);
        }

        return modifiers;
    }

    /// <summary>ported from: src/compiler/parser.ts:4508-4521 — <c>parseFunctionOrConstructorType</c>.</summary>
    /// <remarks>PORT-NOTE: see <see cref="ParseSignatureMember"/> for why each arm finishes and wraps its own node.</remarks>
    private ITypeNode ParseFunctionOrConstructorType()
    {
        var pos = GetNodePos();
        var hasJSDoc = HasPrecedingJSDocComment();
        var modifiers = ParseModifiersForConstructorType();
        var isConstructorType = ParseOptional(SyntaxKind.NewKeyword);
        Debug.Assert(modifiers is null || isConstructorType, "Per isStartOfFunctionOrConstructorType, a function type cannot have modifiers.");
        var typeParameters = ParseTypeParameters();
        var parameters = ParseParameters(ParserSignatureFlags.Type);
        var type = ParseReturnType(SyntaxKind.EqualsGreaterThanToken, /*isType*/ false);
        if (isConstructorType)
        {
            return WithJSDoc(FinishNode(_factory.CreateConstructorTypeNode(modifiers, typeParameters, parameters, type), pos), hasJSDoc);
        }

        return WithJSDoc(FinishNode(_factory.CreateFunctionTypeNode(typeParameters, parameters, type), pos), hasJSDoc);
    }

    /// <summary>ported from: src/compiler/parser.ts:4523-4526 — <c>parseKeywordAndNoDot</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>parseTokenNode&lt;TypeNode&gt;()</c> asks for the TypeNode *view* of the token it just
    /// scanned; the class that has both is <see cref="KeywordTypeToken"/> (the port's spelling of
    /// <c>KeywordTypeNode&lt;TKind&gt;</c>), and every kind this is called for is a keyword that is a type.
    /// </remarks>
    private ITypeNode? ParseKeywordAndNoDot()
    {
        var node = ParseTokenNode<KeywordTypeToken>();
        return Token() == SyntaxKind.DotToken ? null : node;
    }

    /// <summary>ported from: src/compiler/parser.ts:4528-4540 — <c>parseLiteralTypeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's local is <c>BooleanLiteral | NullLiteral | LiteralExpression | PrefixUnaryExpression</c>. The
    /// port widens it to <see cref="Expression"/> (the class the prefix-unary factory takes) and narrows once at the
    /// end to the <c>updateExpression</c> slot of <c>createLiteralTypeNode</c> — every class this can produce sits on
    /// that chain.
    /// </remarks>
    private LiteralTypeNode ParseLiteralTypeNode(bool negative = false)
    {
        var pos = GetNodePos();
        if (negative)
        {
            NextToken();
        }

        Expression expression = Token() is SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword or SyntaxKind.NullKeyword
            ? ParseTokenNode<Expression>()
            : (Expression)ParseLiteralLikeNode(Token());
        if (negative)
        {
            expression = FinishNode(_factory.CreatePrefixUnaryExpression(SyntaxKind.MinusToken, expression), pos);
        }

        return FinishNode(_factory.CreateLiteralTypeNode((UpdateExpression)expression), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:4542-4545 — <c>isStartOfTypeOfImportType</c>.</summary>
    private bool IsStartOfTypeOfImportType()
    {
        NextToken();
        return Token() == SyntaxKind.ImportKeyword;
    }

    /// <summary>ported from: src/compiler/parser.ts:4547-4582 — <c>parseImportType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS compares the two diagnostics by <c>code</c>; the port spells that <see cref="Diagnostic.Code"/>.
    /// The token for the attribute keyword is a runtime <see cref="SyntaxKind"/> value, so the two members of TS's
    /// union (<c>assert</c>/<c>with</c>) are decided at the call to <c>parseImportAttributes</c>, exactly as TS does.
    /// </remarks>
    private ImportTypeNode ParseImportType()
    {
        _sourceFlags |= NodeFlags.PossiblyContainsDynamicImport;
        var pos = GetNodePos();
        var isTypeOf = ParseOptional(SyntaxKind.TypeOfKeyword);
        ParseExpected(SyntaxKind.ImportKeyword);
        ParseExpected(SyntaxKind.OpenParenToken);
        var type = ParseType();
        ImportAttributes? attributes = null;
        if (ParseOptional(SyntaxKind.CommaToken))
        {
            var openBracePosition = _scanner.GetTokenStart();
            ParseExpected(SyntaxKind.OpenBraceToken);
            var currentToken = Token();
            if (currentToken is SyntaxKind.WithKeyword or SyntaxKind.AssertKeyword)
            {
                NextToken();
            }
            else
            {
                ParseErrorAtCurrentToken(Diagnostics._0_expected, ScannerUtilities.TokenToString(SyntaxKind.WithKeyword));
            }

            ParseExpected(SyntaxKind.ColonToken);
            attributes = ParseImportAttributes(currentToken, /*skipKeyword*/ true);
            ParseOptional(SyntaxKind.CommaToken);
            if (!ParseExpected(SyntaxKind.CloseBraceToken))
            {
                var lastError = Core.LastOrUndefined(_parseDiagnostics);
                if (lastError is not null && lastError.Code == Diagnostics._0_expected.Code)
                {
                    Utilities.AddRelatedInfo(
                        lastError,
                        Utilities.CreateDetachedDiagnostic(
                            _fileName,
                            _sourceText,
                            openBracePosition,
                            1,
                            Diagnostics.The_parser_expected_to_find_a_1_to_match_the_0_token_here,
                            "{",
                            "}"));
                }
            }
        }

        ParseExpected(SyntaxKind.CloseParenToken);
        var qualifier = ParseOptional(SyntaxKind.DotToken) ? ParseEntityNameOfTypeReference() : null;
        var typeArguments = ParseTypeArgumentsOfTypeReference();
        return FinishNode(_factory.CreateImportTypeNode(type, attributes, qualifier, typeArguments, isTypeOf), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:4584-4587 — <c>nextTokenIsNumericOrBigIntLiteral</c>.</summary>
    private bool NextTokenIsNumericOrBigIntLiteral()
    {
        NextToken();
        return Token() == SyntaxKind.NumericLiteral || Token() == SyntaxKind.BigIntLiteral;
    }

    /// <summary>ported from: src/compiler/parser.ts:4589-4657 — <c>parseNonArrayType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the two <c>// falls through</c> arms (<c>*=</c> → <c>*</c>, <c>??</c> → <c>?</c>) spell the
    /// fall-through as <c>goto case</c>, because C# does not let one case run into the next. The keyword-type arm
    /// (<c>void</c>) names <see cref="KeywordTypeToken"/>, which is the class the port's token factory builds for the
    /// kinds TS types as <c>KeywordTypeNode</c>.
    /// </remarks>
    private ITypeNode ParseNonArrayType()
    {
        switch (Token())
        {
            case SyntaxKind.AnyKeyword:
            case SyntaxKind.UnknownKeyword:
            case SyntaxKind.StringKeyword:
            case SyntaxKind.NumberKeyword:
            case SyntaxKind.BigIntKeyword:
            case SyntaxKind.SymbolKeyword:
            case SyntaxKind.BooleanKeyword:
            case SyntaxKind.UndefinedKeyword:
            case SyntaxKind.NeverKeyword:
            case SyntaxKind.ObjectKeyword:
                // If these are followed by a dot, then parse these out as a dotted type reference instead.
                return TryParse(ParseKeywordAndNoDot) ?? ParseTypeReference();
            case SyntaxKind.AsteriskEqualsToken:
                // If there is '*=', treat it as * followed by postfix =
                _scanner.ReScanAsteriskEqualsToken();
                goto case SyntaxKind.AsteriskToken;
            case SyntaxKind.AsteriskToken:
                return ParseJSDocAllType();
            case SyntaxKind.QuestionQuestionToken:
                // If there is '??', treat it as prefix-'?' in JSDoc type.
                _scanner.ReScanQuestionToken();
                goto case SyntaxKind.QuestionToken;
            case SyntaxKind.QuestionToken:
                return ParseJSDocUnknownOrNullableType();
            case SyntaxKind.FunctionKeyword:
                return ParseJSDocFunctionType();
            case SyntaxKind.ExclamationToken:
                return ParseJSDocNonNullableType();
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.StringLiteral:
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
            case SyntaxKind.TrueKeyword:
            case SyntaxKind.FalseKeyword:
            case SyntaxKind.NullKeyword:
                return ParseLiteralTypeNode();
            case SyntaxKind.MinusToken:
                return LookAhead(NextTokenIsNumericOrBigIntLiteral) ? ParseLiteralTypeNode(/*negative*/ true) : ParseTypeReference();
            case SyntaxKind.VoidKeyword:
                return ParseTokenNode<KeywordTypeToken>();
            case SyntaxKind.ThisKeyword:
            {
                var thisKeyword = ParseThisTypeNode();
                if (Token() == SyntaxKind.IsKeyword && !_scanner.HasPrecedingLineBreak())
                {
                    return ParseThisTypePredicate(thisKeyword);
                }

                return thisKeyword;
            }
            case SyntaxKind.TypeOfKeyword:
                return LookAhead(IsStartOfTypeOfImportType) ? ParseImportType() : ParseTypeQuery();
            case SyntaxKind.OpenBraceToken:
                return LookAhead(IsStartOfMappedType) ? ParseMappedType() : ParseTypeLiteral();
            case SyntaxKind.OpenBracketToken:
                return ParseTupleType();
            case SyntaxKind.OpenParenToken:
                return ParseParenthesizedType();
            case SyntaxKind.ImportKeyword:
                return ParseImportType();
            case SyntaxKind.AssertsKeyword:
                return LookAhead(NextTokenIsIdentifierOrKeywordOnSameLine) ? ParseAssertsTypePredicate() : ParseTypeReference();
            case SyntaxKind.TemplateHead:
                return ParseTemplateType();
            default:
                return ParseTypeReference();
        }
    }

    /// <summary>ported from: src/compiler/parser.ts:4659-4709 — <c>isStartOfType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>inStartOfParameter?</c> is optional and its <c>undefined</c> behaves like <c>false</c> in
    /// both places it is used, so the port defaults it to <c>false</c> — the callers that mean "yes" spell it out.
    /// </remarks>
    private bool IsStartOfType(bool inStartOfParameter = false)
    {
        switch (Token())
        {
            case SyntaxKind.AnyKeyword:
            case SyntaxKind.UnknownKeyword:
            case SyntaxKind.StringKeyword:
            case SyntaxKind.NumberKeyword:
            case SyntaxKind.BigIntKeyword:
            case SyntaxKind.BooleanKeyword:
            case SyntaxKind.ReadonlyKeyword:
            case SyntaxKind.SymbolKeyword:
            case SyntaxKind.UniqueKeyword:
            case SyntaxKind.VoidKeyword:
            case SyntaxKind.UndefinedKeyword:
            case SyntaxKind.NullKeyword:
            case SyntaxKind.ThisKeyword:
            case SyntaxKind.TypeOfKeyword:
            case SyntaxKind.NeverKeyword:
            case SyntaxKind.OpenBraceToken:
            case SyntaxKind.OpenBracketToken:
            case SyntaxKind.LessThanToken:
            case SyntaxKind.BarToken:
            case SyntaxKind.AmpersandToken:
            case SyntaxKind.NewKeyword:
            case SyntaxKind.StringLiteral:
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
            case SyntaxKind.TrueKeyword:
            case SyntaxKind.FalseKeyword:
            case SyntaxKind.ObjectKeyword:
            case SyntaxKind.AsteriskToken:
            case SyntaxKind.QuestionToken:
            case SyntaxKind.ExclamationToken:
            case SyntaxKind.DotDotDotToken:
            case SyntaxKind.InferKeyword:
            case SyntaxKind.ImportKeyword:
            case SyntaxKind.AssertsKeyword:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.TemplateHead:
                return true;
            case SyntaxKind.FunctionKeyword:
                return !inStartOfParameter;
            case SyntaxKind.MinusToken:
                return !inStartOfParameter && LookAhead(NextTokenIsNumericOrBigIntLiteral);
            case SyntaxKind.OpenParenToken:
                // Only consider '(' the start of a type if followed by ')', '...', an identifier, a modifier,
                // or something that starts a type. We don't want to consider things like '(1)' a type.
                return !inStartOfParameter && LookAhead(IsStartOfParenthesizedOrFunctionType);
            default:
                return IsIdentifier();
        }
    }

    /// <summary>ported from: src/compiler/parser.ts:4711-4714 — <c>isStartOfParenthesizedOrFunctionType</c>.</summary>
    private bool IsStartOfParenthesizedOrFunctionType()
    {
        NextToken();
        return Token() == SyntaxKind.CloseParenToken || IsStartOfParameter(/*isJSDocParameter*/ false) || IsStartOfType();
    }

    /// <summary>ported from: src/compiler/parser.ts:4716-4750 — <c>parsePostfixTypeOrHigher</c>.</summary>
    private ITypeNode ParsePostfixTypeOrHigher()
    {
        var pos = GetNodePos();
        var type = ParseNonArrayType();
        while (!_scanner.HasPrecedingLineBreak())
        {
            switch (Token())
            {
                case SyntaxKind.ExclamationToken:
                    NextToken();
                    type = FinishNode(_factory.CreateJSDocNonNullableType(type, /*postfix*/ true), pos);
                    break;
                case SyntaxKind.QuestionToken:
                    // If next token is start of a type we have a conditional type
                    if (LookAhead(NextTokenIsStartOfType))
                    {
                        return type;
                    }

                    NextToken();
                    type = FinishNode(_factory.CreateJSDocNullableType(type, /*postfix*/ true), pos);
                    break;
                case SyntaxKind.OpenBracketToken:
                    ParseExpected(SyntaxKind.OpenBracketToken);
                    if (IsStartOfType())
                    {
                        var indexType = ParseType();
                        ParseExpected(SyntaxKind.CloseBracketToken);
                        type = FinishNode(_factory.CreateIndexedAccessTypeNode(type, indexType), pos);
                    }
                    else
                    {
                        ParseExpected(SyntaxKind.CloseBracketToken);
                        type = FinishNode(_factory.CreateArrayTypeNode(type), pos);
                    }

                    break;
                default:
                    return type;
            }
        }

        return type;
    }

    /// <summary>ported from: src/compiler/parser.ts:4752-4756 — <c>parseTypeOperator</c>.</summary>
    private ITypeNode ParseTypeOperator(SyntaxKind @operator)
    {
        var pos = GetNodePos();
        ParseExpected(@operator);
        return FinishNode(_factory.CreateTypeOperatorNode(@operator, ParseTypeOperatorOrHigher()), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:4758-4765 — <c>tryParseConstraintOfInferType</c>.</summary>
    private ITypeNode? TryParseConstraintOfInferType()
    {
        if (ParseOptional(SyntaxKind.ExtendsKeyword))
        {
            var constraint = DisallowConditionalTypesAnd(ParseType);
            if (InDisallowConditionalTypesContext() || Token() != SyntaxKind.QuestionToken)
            {
                return constraint;
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/parser.ts:4767-4773 — <c>parseTypeParameterOfInferType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>createTypeParameterDeclaration</c> has an optional <c>defaultType</c>; the generated C#
    /// member takes all four arguments, so the two callers that omit it pass <c>null</c>.
    /// </remarks>
    private TypeParameterDeclaration ParseTypeParameterOfInferType()
    {
        var pos = GetNodePos();
        var name = ParseIdentifier();
        var constraint = TryParse(TryParseConstraintOfInferType);
        var node = _factory.CreateTypeParameterDeclaration(/*modifiers*/ null, name, constraint, /*defaultType*/ null);
        return FinishNode(node, pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:4775-4779 — <c>parseInferType</c>.</summary>
    private InferTypeNode ParseInferType()
    {
        var pos = GetNodePos();
        ParseExpected(SyntaxKind.InferKeyword);
        return FinishNode(_factory.CreateInferTypeNode(ParseTypeParameterOfInferType()), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:4781-4792 — <c>parseTypeOperatorOrHigher</c>.</summary>
    private ITypeNode ParseTypeOperatorOrHigher()
    {
        var @operator = Token();
        switch (@operator)
        {
            case SyntaxKind.KeyOfKeyword:
            case SyntaxKind.UniqueKeyword:
            case SyntaxKind.ReadonlyKeyword:
                return ParseTypeOperator(@operator);
            case SyntaxKind.InferKeyword:
                return ParseInferType();
        }

        return AllowConditionalTypesAnd(ParsePostfixTypeOrHigher);
    }

    /// <summary>ported from: src/compiler/parser.ts:4794-4817 — <c>parseFunctionOrConstructorTypeToError</c>.</summary>
    private ITypeNode? ParseFunctionOrConstructorTypeToError(bool isInUnionType)
    {
        // the function type and constructor type shorthand notation
        // are not allowed directly in unions and intersections, but we'll
        // try to parse them gracefully and issue a helpful message.
        if (IsStartOfFunctionTypeOrConstructorType())
        {
            var type = ParseFunctionOrConstructorType();
            DiagnosticMessage diagnostic;
            if (NodeTests.IsFunctionTypeNode((Node)type))
            {
                diagnostic = isInUnionType
                    ? Diagnostics.Function_type_notation_must_be_parenthesized_when_used_in_a_union_type
                    : Diagnostics.Function_type_notation_must_be_parenthesized_when_used_in_an_intersection_type;
            }
            else
            {
                diagnostic = isInUnionType
                    ? Diagnostics.Constructor_type_notation_must_be_parenthesized_when_used_in_a_union_type
                    : Diagnostics.Constructor_type_notation_must_be_parenthesized_when_used_in_an_intersection_type;
            }

            ParseErrorAtRange((Node)type, diagnostic);
            return type;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/parser.ts:4819-4837 — <c>parseUnionOrIntersectionType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>createTypeNode</c> is <c>(types: NodeArray&lt;TypeNode&gt;) =&gt; UnionOrIntersectionTypeNode</c>
    /// — a union with no C# counterpart. The two factory members it is passed are the only callers, so the port's
    /// delegate is spelled with their parameter type (<c>List&lt;ITypeNode&gt;?</c>, which a
    /// <c>NodeArray&lt;ITypeNode&gt;</c> is) and with <see cref="Node"/> as its result: that is where
    /// <c>UnionTypeNode</c> and <c>IntersectionTypeNode</c> meet, and it is what <c>finishNode</c> needs. The cast back
    /// to <see cref="ITypeNode"/> is the same promise TS's union makes — the only two classes this delegate can produce
    /// implement it.
    /// <para>
    /// The two <c>&amp;&amp;</c>/<c>||</c> pairs are TS's way of saying "the error-recovery parse if it produced
    /// anything, otherwise the constituent type"; the port writes the same choice as <c>??</c>.
    /// </para>
    /// </remarks>
    private ITypeNode ParseUnionOrIntersectionType(
        SyntaxKind @operator,
        Func<ITypeNode> parseConstituentType,
        Func<List<ITypeNode>, Node> createTypeNode)
    {
        var pos = GetNodePos();
        var isUnionType = @operator == SyntaxKind.BarToken;
        var hasLeadingOperator = ParseOptional(@operator);
        var type = (hasLeadingOperator ? ParseFunctionOrConstructorTypeToError(isUnionType) : null) ?? parseConstituentType();
        if (Token() == @operator || hasLeadingOperator)
        {
            var types = new List<ITypeNode> { type };
            while (ParseOptional(@operator))
            {
                types.Add(ParseFunctionOrConstructorTypeToError(isUnionType) ?? parseConstituentType());
            }

            type = (ITypeNode)FinishNode(createTypeNode(CreateNodeArray(types, pos)), pos);
        }

        return type;
    }

    /// <summary>ported from: src/compiler/parser.ts:4839-4841 — <c>parseIntersectionTypeOrHigher</c>.</summary>
    private ITypeNode ParseIntersectionTypeOrHigher()
        => ParseUnionOrIntersectionType(SyntaxKind.AmpersandToken, ParseTypeOperatorOrHigher, _factory.CreateIntersectionTypeNode);

    /// <summary>ported from: src/compiler/parser.ts:4843-4845 — <c>parseUnionTypeOrHigher</c>.</summary>
    private ITypeNode ParseUnionTypeOrHigher()
        => ParseUnionOrIntersectionType(SyntaxKind.BarToken, ParseIntersectionTypeOrHigher, _factory.CreateUnionTypeNode);

    /// <summary>ported from: src/compiler/parser.ts:4847-4850 — <c>nextTokenIsNewKeyword</c>.</summary>
    private bool NextTokenIsNewKeyword()
    {
        NextToken();
        return Token() == SyntaxKind.NewKeyword;
    }

    /// <summary>ported from: src/compiler/parser.ts:4852-4861 — <c>isStartOfFunctionTypeOrConstructorType</c>.</summary>
    private bool IsStartOfFunctionTypeOrConstructorType()
    {
        if (Token() == SyntaxKind.LessThanToken)
        {
            return true;
        }

        if (Token() == SyntaxKind.OpenParenToken && LookAhead(IsUnambiguouslyStartOfFunctionType))
        {
            return true;
        }

        return Token() == SyntaxKind.NewKeyword
            || Token() == SyntaxKind.AbstractKeyword && LookAhead(NextTokenIsNewKeyword);
    }

    /// <summary>ported from: src/compiler/parser.ts:4863-4879 — <c>skipParameterStart</c>.</summary>
    private bool SkipParameterStart()
    {
        if (Utilities.IsModifierKind(Token()))
        {
            // Skip modifiers
            ParseModifiers(/*allowDecorators*/ false);
        }

        if (IsIdentifier() || Token() == SyntaxKind.ThisKeyword)
        {
            NextToken();
            return true;
        }

        if (Token() == SyntaxKind.OpenBracketToken || Token() == SyntaxKind.OpenBraceToken)
        {
            // Return true if we can parse an array or object binding pattern with no errors
            var previousErrorCount = _parseDiagnostics.Count;
            ParseIdentifierOrPattern(/*privateIdentifierDiagnosticMessage*/ null);
            return previousErrorCount == _parseDiagnostics.Count;
        }

        return false;
    }

    /// <summary>ported from: src/compiler/parser.ts:4881-4910 — <c>isUnambiguouslyStartOfFunctionType</c>.</summary>
    private bool IsUnambiguouslyStartOfFunctionType()
    {
        NextToken();
        if (Token() == SyntaxKind.CloseParenToken || Token() == SyntaxKind.DotDotDotToken)
        {
            // ( )
            // ( ...
            return true;
        }

        if (SkipParameterStart())
        {
            // We successfully skipped modifiers (if any) and an identifier or binding pattern,
            // now see if we have something that indicates a parameter declaration
            if (Token() == SyntaxKind.ColonToken
                || Token() == SyntaxKind.CommaToken
                || Token() == SyntaxKind.QuestionToken
                || Token() == SyntaxKind.EqualsToken)
            {
                // ( xxx :
                // ( xxx ,
                // ( xxx ?
                // ( xxx =
                return true;
            }

            if (Token() == SyntaxKind.CloseParenToken)
            {
                NextToken();
                if (Token() == SyntaxKind.EqualsGreaterThanToken)
                {
                    // ( xxx ) =>
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/parser.ts:4912-4922 — <c>parseTypeOrTypePredicate</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>isIdentifier() &amp;&amp; tryParse(parseTypePredicatePrefix)</c> answers the identifier or a
    /// falsy value, and the truthiness test below is what tells the two apart; the port writes the same choice with a
    /// null test (the identifier parser never answers null).
    /// </remarks>
    private ITypeNode ParseTypeOrTypePredicate()
    {
        var pos = GetNodePos();
        var typePredicateVariable = IsIdentifier() ? TryParse(ParseTypePredicatePrefix) : null;
        var type = ParseType();
        if (typePredicateVariable is not null)
        {
            return FinishNode(_factory.CreateTypePredicateNode(/*assertsModifier*/ null, typePredicateVariable, type), pos);
        }

        return type;
    }

    /// <summary>ported from: src/compiler/parser.ts:4924-4930 — <c>parseTypePredicatePrefix</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers <c>Identifier | undefined</c>; both callers (this file's two predicate prefixes) test the
    /// result for truthiness, which null answers the same way.
    /// </remarks>
    private Identifier? ParseTypePredicatePrefix()
    {
        var id = ParseIdentifier();
        if (Token() == SyntaxKind.IsKeyword && !_scanner.HasPrecedingLineBreak())
        {
            NextToken();
            return id;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/parser.ts:4932-4938 — <c>parseAssertsTypePredicate</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>parseExpectedToken</c> gives the <c>asserts</c> keyword; see
    /// <see cref="ParseExpectedKeywordToken"/> for why the port needs its own call for a
    /// <see cref="KeywordToken"/>-typed slot.
    /// </remarks>
    private ITypeNode ParseAssertsTypePredicate()
    {
        var pos = GetNodePos();
        var assertsModifier = ParseExpectedKeywordToken(SyntaxKind.AssertsKeyword);
        Node parameterName = Token() == SyntaxKind.ThisKeyword ? ParseThisTypeNode() : ParseIdentifier();
        var type = ParseOptional(SyntaxKind.IsKeyword) ? ParseType() : null;
        return FinishNode(_factory.CreateTypePredicateNode(assertsModifier, parameterName, type), pos);
    }

    /// <summary>ported from: src/compiler/parser.ts:4940-4959 — <c>parseType</c>.</summary>
    private ITypeNode ParseType()
    {
        if ((_contextFlags & NodeFlags.TypeExcludesFlags) != 0)
        {
            return DoOutsideOfContext(NodeFlags.TypeExcludesFlags, ParseType);
        }

        if (IsStartOfFunctionTypeOrConstructorType())
        {
            return ParseFunctionOrConstructorType();
        }

        var pos = GetNodePos();
        var type = ParseUnionTypeOrHigher();
        if (!InDisallowConditionalTypesContext() && !_scanner.HasPrecedingLineBreak() && ParseOptional(SyntaxKind.ExtendsKeyword))
        {
            // The type following 'extends' is not permitted to be another conditional type
            var extendsType = DisallowConditionalTypesAnd(ParseType);
            ParseExpected(SyntaxKind.QuestionToken);
            var trueType = AllowConditionalTypesAnd(ParseType);
            ParseExpected(SyntaxKind.ColonToken);
            var falseType = AllowConditionalTypesAnd(ParseType);
            return FinishNode(_factory.CreateConditionalTypeNode(type, extendsType, trueType, falseType), pos);
        }

        return type;
    }

    /// <summary>ported from: src/compiler/parser.ts:4961-4963 — <c>parseTypeAnnotation</c>.</summary>
    private ITypeNode? ParseTypeAnnotation()
        => ParseOptional(SyntaxKind.ColonToken) ? ParseType() : null;

    //
    // Helpers the type grammar needs to meet the model's concrete types
    //

    /// <summary>
    /// The <c>List&lt;ModifierToken&gt;</c> view of a modifier array — the slots the factory declares that way
    /// (TS: the <c>NodeArray&lt;Modifier&gt;</c> result of <c>parseModifiers(/*allowDecorators*/ false)</c>).
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS hands one <c>NodeArray&lt;Modifier&gt;</c> both to members typed <c>NodeArray&lt;ModifierLike&gt;</c>
    /// and to members typed <c>NodeArray&lt;Modifier&gt;</c>, relying on array covariance; C#'s <c>List&lt;T&gt;</c> is
    /// invariant, so the three factory slots declared <c>List&lt;ModifierToken&gt;?</c> (type parameter, property
    /// signature, method signature) get a projected list. The elements are the same instances the parser built.
    /// </remarks>
    private static List<ModifierToken>? AsModifierTokens(List<Node>? modifiers)
        => modifiers is null ? null : Core.Map(modifiers, static (Node modifier, int _) => (ModifierToken)modifier);

    /// <summary>
    /// The <c>List&lt;Node&gt;</c> view of a type-node array (TS: the covariance that lets a
    /// <c>NodeArray&lt;TypeNode | NamedTupleMember&gt;</c> reach <c>createTupleTypeNode</c>).
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: <c>TupleTypeNode.elements</c> is <c>NodeArray&lt;Node&gt;</c> in the port's model, not
    /// <c>NodeArray&lt;ITypeNode&gt;</c> — the union's common C# class — so <c>parseTupleType</c> projects the list it
    /// gave the bracketed-list parser. The elements are passed through, not rebuilt.
    /// </remarks>
    private static List<Node> AsNodeList(NodeArray<ITypeNode> elements)
        => Core.Map(elements, static (ITypeNode element, int _) => (Node)element)!;

    /// <summary>
    /// The keyword token a <see cref="KeywordToken"/>-typed slot needs (TS: <c>parseExpectedToken</c>, parser.ts:2539).
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>parseExpectedToken(SyntaxKind.AssertsKeyword)</c> answers a token whose *static* type is
    /// <c>AssertsKeyword</c> while the object is the ordinary token the token factory made. The port's
    /// <c>TypePredicateNode.AssertsModifier</c> is declared with the class <see cref="KeywordToken"/>, and
    /// <c>NodeFactory.CreateToken</c> builds a plain <see cref="Token"/> for a plain keyword — so the token the factory
    /// hands over is used whenever it already has that class (which is what should happen once the token factory sorts
    /// keywords into <see cref="KeywordToken"/>), and rebuilt as one otherwise. The rebuild copies the range, the
    /// context flags and the transform flags the factory put on it, so the result is what this file would have got.
    /// </remarks>
    private KeywordToken ParseExpectedKeywordToken(SyntaxKind kind)
    {
        var token = ParseExpectedToken(kind);
        if (token is KeywordToken keywordToken)
        {
            return keywordToken;
        }

        return new KeywordToken(kind)
        {
            Pos = token.Pos,
            End = token.End,
            Flags = token.Flags,
            TransformFlags = token.TransformFlags,
        };
    }
}
