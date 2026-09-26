namespace TypeScript.Compiler.Tests;

/// <summary>
/// Acceptance for the parenthesizer rules (docs/architecture.md §15.9) — the port of
/// <c>factory/parenthesizerRules.ts</c>.
/// </summary>
/// <remarks>
/// Each test pins a decision the rule makes from the operator tables rather than from its input's spelling: which
/// operand needs parentheses, and which does not. That distinction is invisible in the port's own code (it is the
/// precedence comparison that answers it) but shows up immediately in emitted output, so it is checked here.
/// </remarks>
public class ParenthesizerRulesTests
{
    private readonly NodeFactory _factory = new();

    private IParenthesizerRules Rules => _factory.Parenthesizer;

    private Identifier Id(string text) => _factory.CreateIdentifier(text, null, null);

    private BinaryExpression Binary(Expression left, SyntaxKind @operator, Expression right)
        => _factory.CreateBinaryExpression(left, @operator, right);

    // ---------------------------------------------------------------------------------------------
    // Computed property names, conditional expressions, arrow bodies
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ComputedPropertyName_WrapsACommaSequenceOnly()
    {
        var sequence = Binary(Id("a"), SyntaxKind.CommaToken, Id("b"));
        var identifier = Id("a");

        var wrapped = Assert.IsType<ParenthesizedExpression>(Rules.ParenthesizeExpressionOfComputedPropertyName(sequence));

        Assert.Same(sequence, wrapped.Expression);
        Assert.Same(identifier, Rules.ParenthesizeExpressionOfComputedPropertyName(identifier));
    }

    [Fact]
    public void ConditionOfConditional_WrapsThePrecedenceBelowIt()
    {
        // `a = b` binds looser than `? :`, so it has to be parenthesized; `a || b` binds tighter and does not.
        var assignment = (Expression)_factory.CreateAssignment(Id("a"), Id("b"));
        var logicalOr = Binary(Id("a"), SyntaxKind.BarBarToken, Id("b"));

        Assert.IsType<ParenthesizedExpression>(Rules.ParenthesizeConditionOfConditionalExpression(assignment));
        Assert.Same(logicalOr, Rules.ParenthesizeConditionOfConditionalExpression(logicalOr));
    }

    [Fact]
    public void BranchOfConditional_WrapsACommaSequence()
    {
        var sequence = Binary(Id("a"), SyntaxKind.CommaToken, Id("b"));

        Assert.IsType<ParenthesizedExpression>(Rules.ParenthesizeBranchOfConditionalExpression(sequence));
    }

    [Fact]
    public void ConciseBodyOfArrowFunction_WrapsAnObjectLiteralAndACommaSequence()
    {
        var objectLiteral = _factory.CreateObjectLiteralExpression([], null);
        var sequence = Binary(Id("a"), SyntaxKind.CommaToken, Id("b"));

        Assert.IsType<ParenthesizedExpression>(Rules.ParenthesizeConciseBodyOfArrowFunction((Expression)objectLiteral));
        Assert.IsType<ParenthesizedExpression>(Rules.ParenthesizeConciseBodyOfArrowFunction(sequence));

        var identifier = Id("a");
        Assert.Same(identifier, Rules.ParenthesizeConciseBodyOfArrowFunction(identifier));
    }

    [Fact]
    public void ConciseBodyOfArrowFunction_LeavesABlockAlone()
    {
        // The `ConciseBody` union reaches the port as two overloads; a block is not an `Expression`, so it lands on the
        // `Node` one and comes back unchanged — which is what TS's `isBlock(body)` test means.
        var block = _factory.CreateBlock([], null);

        Assert.Same(block, Rules.ParenthesizeConciseBodyOfArrowFunction((Node)block));
    }

    // ---------------------------------------------------------------------------------------------
    // Access and `new`
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void LeftSideOfAccess_KeepsANewWithArgumentsAndWrapsANewWithout()
    {
        // `new C.x` parses as `new (C.x)`; `(new C).x` needs the parentheses the rule adds.
        var withArguments = _factory.CreateNewExpression(Id("C"), null, []);
        var withoutArguments = _factory.CreateNewExpression(Id("C"), null, null);

        Assert.Same(withArguments, Rules.ParenthesizeLeftSideOfAccess(withArguments, false));

        var wrapped = Assert.IsType<ParenthesizedExpression>(Rules.ParenthesizeLeftSideOfAccess(withoutArguments, false));
        Assert.Same(withoutArguments, wrapped.Expression);
    }

    [Fact]
    public void LeftSideOfAccess_WrapsAnOptionalChainUnlessItIsTheChainBeingBuilt()
    {
        var chain = _factory.CreatePropertyAccessChain(Id("a"), null, Id("b"));

        // `a?.b.c` — the chain continues without parentheses.
        Assert.Same(chain, Rules.ParenthesizeLeftSideOfAccess(chain, true));

        // `(a?.b).c` — the chain ends, so it needs them.
        Assert.IsType<ParenthesizedExpression>(Rules.ParenthesizeLeftSideOfAccess(chain, false));
    }

    [Fact]
    public void ExpressionOfNew_WrapsACallButNotANewWithArguments()
    {
        var call = _factory.CreateCallExpression(Id("f"), null, []);
        var newWithArguments = _factory.CreateNewExpression(Id("C"), null, []);

        Assert.IsType<ParenthesizedExpression>(Rules.ParenthesizeExpressionOfNew(call));
        Assert.Same(newWithArguments, Rules.ParenthesizeExpressionOfNew(newWithArguments));
    }

    [Fact]
    public void OperandOfPrefixUnary_KeepsAUnaryExpressionAndWrapsABinary()
    {
        var prefix = _factory.CreatePrefixUnaryExpression(SyntaxKind.ExclamationToken, Id("a"));
        var binary = Binary(Id("a"), SyntaxKind.PlusToken, Id("b"));

        Assert.Same(prefix, Rules.ParenthesizeOperandOfPrefixUnary(prefix));
        Assert.IsType<ParenthesizedExpression>(Rules.ParenthesizeOperandOfPrefixUnary(binary));
    }

    // ---------------------------------------------------------------------------------------------
    // Binary operands
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void LeftSideOfBinary_LeavesALeftAssociativeOperandAlone()
    {
        // (a*b)/x -> a*b/x: same precedence, left associative, so the left operand needs nothing.
        var product = Binary(Id("a"), SyntaxKind.AsteriskToken, Id("b"));

        Assert.Same(product, Rules.ParenthesizeLeftSideOfBinary(SyntaxKind.SlashToken, product));
    }

    [Fact]
    public void LeftSideOfBinary_WrapsForARightAssociativeOperator()
    {
        // (a/b)**x -> (a/b)**x: `**` is right associative, so the left operand has to keep them.
        var quotient = Binary(Id("a"), SyntaxKind.SlashToken, Id("b"));

        Assert.IsType<ParenthesizedExpression>(Rules.ParenthesizeLeftSideOfBinary(SyntaxKind.AsteriskAsteriskToken, quotient));
    }

    [Fact]
    public void RightSideOfBinary_LeavesAHigherPrecedenceOperandAlone()
    {
        var product = Binary(Id("a"), SyntaxKind.AsteriskToken, Id("b"));

        Assert.Same(product, Rules.ParenthesizeRightSideOfBinary(SyntaxKind.PlusToken, Id("x"), product));
    }

    [Fact]
    public void RightSideOfBinary_WrapsAnEqualPrecedenceLeftAssociativeOperand()
    {
        // x*(a/d) -> x*(a/d): same precedence and `*` is left associative, so the right operand needs them.
        var quotient = Binary(Id("a"), SyntaxKind.SlashToken, Id("d"));

        Assert.IsType<ParenthesizedExpression>(Rules.ParenthesizeRightSideOfBinary(SyntaxKind.AsteriskToken, Id("x"), quotient));
    }

    [Fact]
    public void RightSideOfBinary_LeavesTheSameAssociativeOperatorAlone()
    {
        // x*(a*b) -> x*a*b.
        var product = Binary(Id("a"), SyntaxKind.AsteriskToken, Id("b"));

        Assert.Same(product, Rules.ParenthesizeRightSideOfBinary(SyntaxKind.AsteriskToken, Id("x"), product));
    }

    [Fact]
    public void RightSideOfBinary_LeavesHomogeneousLiteralPlusAlone()
    {
        // "a"+("b"+"c") -> "a"+"b"+"c": both sides are string literals, so `+` cannot re-associate into concatenation.
        var right = Binary(_factory.CreateStringLiteral("b", null, null), SyntaxKind.PlusToken, _factory.CreateStringLiteral("c", null, null));
        var left = _factory.CreateStringLiteral("a", null, null);

        Assert.Same(right, Rules.ParenthesizeRightSideOfBinary(SyntaxKind.PlusToken, left, right));
    }

    [Fact]
    public void RightSideOfBinary_WrapsWhenCoalesceMeetsLogicalOperator()
    {
        // `a ?? (b || c)` — mixing `??` with `||` is a syntax error without the parentheses.
        var logicalOr = Binary(Id("b"), SyntaxKind.BarBarToken, Id("c"));

        Assert.IsType<ParenthesizedExpression>(
            Rules.ParenthesizeRightSideOfBinary(SyntaxKind.QuestionQuestionToken, Id("a"), logicalOr));
    }

    [Fact]
    public void RightSideOfBinary_WrapsAHomogeneousPlusOfMixedLiteralKinds()
    {
        // "a"+(1+"c"): the right operand's literal kind is not homogeneous with the left's, so `+` may re-associate
        // into concatenation and the parentheses stay.
        var right = Binary(_factory.CreateNumericLiteral(1, null), SyntaxKind.PlusToken, _factory.CreateStringLiteral("c", null, null));
        var left = _factory.CreateStringLiteral("a", null, null);

        Assert.IsType<ParenthesizedExpression>(Rules.ParenthesizeRightSideOfBinary(SyntaxKind.PlusToken, left, right));
    }

    // ---------------------------------------------------------------------------------------------
    // Type nodes
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void CheckTypeOfConditionalType_WrapsFunctionAndConstructorTypes()
    {
        var functionType = _factory.CreateFunctionTypeNode(null, null, _factory.CreateTypeReferenceNode("T", null));
        var reference = _factory.CreateTypeReferenceNode("T", null);

        Assert.IsType<ParenthesizedTypeNode>((Node)Rules.ParenthesizeCheckTypeOfConditionalType(functionType));
        Assert.Same(reference, Rules.ParenthesizeCheckTypeOfConditionalType(reference));
    }

    [Fact]
    public void ExtendsTypeOfConditionalType_WrapsOnlyAConditionalType()
    {
        var nested = _factory.CreateConditionalTypeNode(
            _factory.CreateTypeReferenceNode("T", null),
            _factory.CreateTypeReferenceNode("U", null),
            _factory.CreateTypeReferenceNode("X", null),
            _factory.CreateTypeReferenceNode("Y", null));
        var functionType = _factory.CreateFunctionTypeNode(null, null, _factory.CreateTypeReferenceNode("T", null));

        Assert.IsType<ParenthesizedTypeNode>((Node)Rules.ParenthesizeExtendsTypeOfConditionalType(nested));
        Assert.Same(functionType, Rules.ParenthesizeExtendsTypeOfConditionalType(functionType));
    }

    [Fact]
    public void NonArrayTypeOfPostfixType_WrapsAnInferType()
    {
        var inferType = _factory.CreateInferTypeNode(_factory.CreateTypeParameterDeclaration(null, Id("T"), null, null));

        Assert.IsType<ParenthesizedTypeNode>((Node)Rules.ParenthesizeNonArrayTypeOfPostfixType(inferType));
    }

    [Fact]
    public void ElementTypeOfTupleType_WrapsAJSDocPostfixQuestion()
    {
        // `@type {T?}` is a postfix-optional *type*, and as a tuple element it is ambiguous with the element's own `?`.
        var postfix = _factory.CreateJSDocNullableType(_factory.CreateTypeReferenceNode("T", null), true);
        var prefix = _factory.CreateJSDocNullableType(_factory.CreateTypeReferenceNode("T", null), false);

        Assert.IsType<ParenthesizedTypeNode>((Node)Rules.ParenthesizeElementTypeOfTupleType(postfix));
        Assert.Same(prefix, Rules.ParenthesizeElementTypeOfTupleType(prefix));
    }

    [Fact]
    public void LeadingTypeArgument_WrapsAFunctionTypeWithTypeParameters()
    {
        var genericFunction = _factory.CreateFunctionTypeNode(
            [_factory.CreateTypeParameterDeclaration(null, Id("T"), null, null)],
            null,
            _factory.CreateTypeReferenceNode("T", null));

        Assert.IsType<ParenthesizedTypeNode>((Node)Rules.ParenthesizeLeadingTypeArgument(genericFunction));
    }

    // ---------------------------------------------------------------------------------------------
    // The per-operator rule cache and the expression-statement rule
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void GetParenthesizeLeftSideOfBinaryForOperator_MemoizesPerOperator()
    {
        var plus = Rules.GetParenthesizeLeftSideOfBinaryForOperator(SyntaxKind.PlusToken);
        var plusAgain = Rules.GetParenthesizeLeftSideOfBinaryForOperator(SyntaxKind.PlusToken);
        var minus = Rules.GetParenthesizeLeftSideOfBinaryForOperator(SyntaxKind.MinusToken);

        Assert.Same(plus, plusAgain);
        Assert.NotSame(plus, minus);
        Assert.IsType<ParenthesizedExpression>(plus(Binary(Id("a"), SyntaxKind.BarBarToken, Id("b"))));
    }

    [Fact]
    public void ExpressionOfExpressionStatement_ParenthesizesAFunctionExpressionCallee()
    {
        // `(function () {})()` — the callee's kind is what forces the parentheses, and the rule rewrites in place.
        var callee = _factory.CreateFunctionExpression(null, null, null, null, [], null, _factory.CreateBlock([], null));
        var call = _factory.CreateCallExpression(callee, null, []);

        var updated = Assert.IsType<CallExpression>(Rules.ParenthesizeExpressionOfExpressionStatement(call));

        Assert.NotSame(call, updated);
        Assert.IsType<ParenthesizedExpression>(updated.Expression);
        Assert.Same(callee, ((ParenthesizedExpression)updated.Expression!).Expression);
    }

    [Fact]
    public void ExpressionOfExpressionStatement_ParenthesizesAnObjectLiteral()
    {

        var updated = Assert.IsType<ParenthesizedExpression>(
            Rules.ParenthesizeExpressionOfExpressionStatement(_factory.CreateObjectLiteralExpression([], null)));

        Assert.IsType<ObjectLiteralExpression>(updated.Expression);
    }
}
