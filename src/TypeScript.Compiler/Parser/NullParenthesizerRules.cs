namespace TypeScript.Compiler;

// ported from: src/compiler/factory/parenthesizerRules.ts — `nullParenthesizerRules`
/// <summary>
/// The parenthesizer that adds nothing: every method hands back what it was given.
/// </summary>
/// <remarks>
/// TS has the same object, used when a factory is built with
/// <see cref="NodeFactoryFlags.NoParenthesizerRules"/>. The port uses it for <em>every</em> factory until M4d ports
/// <c>parenthesizerRules.ts</c> — which is a temporary state rather than a design: a factory built with these rules
/// runs, but emits without the parentheses TS's rules insert, and that difference would surface in the emitted
/// baselines of M7.
/// </remarks>
internal sealed class NullParenthesizerRules : IParenthesizerRules
{
    public static NullParenthesizerRules Instance { get; } = new();

    private NullParenthesizerRules()
    {
    }

    private static Expression Identity(Expression? expression) => expression!;

    /// <summary>TS: <c>() =&gt; identity</c> — the per-operator functions are the same answer as the direct calls.</summary>
    public Func<Expression, Expression> GetParenthesizeLeftSideOfBinaryForOperator(SyntaxKind binaryOperator) => Identity;

    /// <summary>TS: <c>() =&gt; identity</c>.</summary>
    public Func<Expression, Expression> GetParenthesizeRightSideOfBinaryForOperator(SyntaxKind binaryOperator) => Identity;

    public Expression ParenthesizeLeftSideOfBinary(SyntaxKind binaryOperator, Expression? leftSide) => leftSide!;

    public Expression ParenthesizeRightSideOfBinary(SyntaxKind binaryOperator, Expression? leftSide, Expression? rightSide) => rightSide!;

    public Expression ParenthesizeExpressionOfComputedPropertyName(Expression? expression) => expression!;

    public Expression ParenthesizeConditionOfConditionalExpression(Expression? condition) => condition!;

    public Expression ParenthesizeBranchOfConditionalExpression(Expression? branch) => branch!;

    public Expression ParenthesizeExpressionOfExportDefault(Expression? expression) => expression!;

    public LeftHandSideExpression ParenthesizeExpressionOfNew(Expression? expression) => (LeftHandSideExpression)expression!;

    public LeftHandSideExpression ParenthesizeLeftSideOfAccess(Expression? expression, bool? optionalChain) => (LeftHandSideExpression)expression!;

    public LeftHandSideExpression ParenthesizeOperandOfPostfixUnary(Expression? operand) => (LeftHandSideExpression)operand!;

    public UnaryExpression ParenthesizeOperandOfPrefixUnary(Expression? operand) => (UnaryExpression)operand!;

    // The list-returning members cannot simply answer with their argument: TS's structural types let a
    // `readonly Expression[]` stand in for a `NodeArray<Expression>`, C# does not.
    public NodeArray<Expression> ParenthesizeExpressionsOfCommaDelimitedList(List<Expression>? elements)
        => new(elements ?? []);

    public Expression ParenthesizeExpressionForDisallowedComma(Expression? expression) => expression!;

    public Expression ParenthesizeExpressionOfExpressionStatement(Expression? expression) => expression!;

    public Expression ParenthesizeConciseBodyOfArrowFunction(Expression? body) => body!;

    public Node ParenthesizeConciseBodyOfArrowFunction(Node? body) => body!;

    public ITypeNode ParenthesizeCheckTypeOfConditionalType(ITypeNode? type) => type!;

    public ITypeNode ParenthesizeExtendsTypeOfConditionalType(ITypeNode? type) => type!;

    public ITypeNode ParenthesizeOperandOfTypeOperator(ITypeNode? type) => type!;

    public ITypeNode ParenthesizeOperandOfReadonlyTypeOperator(ITypeNode? type) => type!;

    public ITypeNode ParenthesizeNonArrayTypeOfPostfixType(ITypeNode? type) => type!;

    public NodeArray<ITypeNode> ParenthesizeElementTypesOfTupleType(List<Node>? types)
        => new(types?.Cast<ITypeNode>() ?? []);

    public Node ParenthesizeElementTypeOfTupleType(Node? type) => type!;

    public ITypeNode ParenthesizeTypeOfOptionalType(ITypeNode? type) => type!;

    public ITypeNode ParenthesizeConstituentTypeOfUnionType(ITypeNode? type) => type!;

    public NodeArray<ITypeNode> ParenthesizeConstituentTypesOfUnionType(List<ITypeNode>? constituents)
        => new(constituents ?? []);

    public ITypeNode ParenthesizeConstituentTypeOfIntersectionType(ITypeNode? type) => type!;

    public NodeArray<ITypeNode> ParenthesizeConstituentTypesOfIntersectionType(List<ITypeNode>? constituents)
        => new(constituents ?? []);

    public ITypeNode ParenthesizeLeadingTypeArgument(ITypeNode? typeNode) => typeNode!;

    public NodeArray<ITypeNode> ParenthesizeTypeArguments(List<ITypeNode>? typeParameters)
        => new(typeParameters ?? []);
}
