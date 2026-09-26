using System.Runtime.CompilerServices;

namespace TypeScript.Compiler;

// ported from: src/compiler/factory/parenthesizerRules.ts:54-672
/// <summary>
/// The factory's parentheses rules: given an operand of a rebuilt node, decide whether it has to be wrapped in
/// parentheses so the rebuilt tree still parses as the expression (or type) the caller meant.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS builds this as the closure returned from <c>createParenthesizerRules(factory)</c>; the port is a class
/// holding the factory, the same treatment <see cref="NodeFactory"/> itself got (§5 rule 6). The <c>factory</c>
/// parameter TS closes over is the <see cref="_factory"/> field.
/// <para>
/// PORT-NOTE: the two per-operator caches hold <see cref="Func{T,TResult}"/> instead of TS's one-argument closures.
/// C# has no free-standing closure to memoize here, and the delegate <em>is</em> that closure, so what TS gets from
/// <c>binaryLeftOperandParenthesizerCache</c> — one function object per operator kind, built on first use and reused
/// for the life of the factory — is preserved exactly.
/// </para>
/// <para>
/// PORT-NOTE: TS's two experimental helpers (<c>parenthesizeMemberOfElementType</c>, <c>parenthesizeElementTypeOfArrayType</c>)
/// are commented out in the source as well, so nothing is ported for them.
/// </para>
/// </remarks>
internal sealed class ParenthesizerRules : IParenthesizerRules
{
    private readonly NodeFactory _factory;

    /// <remarks>
    /// PORT-NOTE: TS creates this map on first use (<c>binaryLeftOperandParenthesizerCache ||= new Map()</c>) because a
    /// factory that never parenthesizes a binary operand should not pay for it; a lazy field keeps that.
    /// </remarks>
    private Dictionary<SyntaxKind, Func<Expression, Expression>>? _binaryLeftOperandParenthesizerCache;

    /// <inheritdoc cref="_binaryLeftOperandParenthesizerCache"/>
    private Dictionary<SyntaxKind, Func<Expression, Expression>>? _binaryRightOperandParenthesizerCache;

    /// <remarks>
    /// PORT-NOTE: TS stashes <c>getLiteralKindOfBinaryPlusOperand</c>'s answer on the node itself
    /// (<c>(node as BinaryPlusExpression).cachedLiteralKind</c>), which a C# node cannot carry: its slots are fixed and
    /// <see cref="Node.EmitNode"/> is emit state, not a cache. A <see cref="ConditionalWeakTable{TKey,TValue}"/> keyed
    /// by the expression keeps the two properties that matter — the memo is collected together with the node it
    /// describes (a plain dictionary would pin every <c>+</c> node for as long as the factory lives), and the
    /// <see cref="StrongBox{T}"/> separates "not computed yet" from "computed as <see cref="SyntaxKind.Unknown"/>",
    /// which is what TS's <c>!== undefined</c> test does.
    /// </remarks>
    private readonly ConditionalWeakTable<BinaryExpression, StrongBox<SyntaxKind>> _cachedLiteralKind = new();

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:54 — <c>createParenthesizerRules</c>.</summary>
    public ParenthesizerRules(NodeFactory factory) => _factory = factory;

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:95 — <c>getParenthesizeLeftSideOfBinaryForOperator</c>.</summary>
    public Func<Expression, Expression> GetParenthesizeLeftSideOfBinaryForOperator(SyntaxKind binaryOperator)
    {
        _binaryLeftOperandParenthesizerCache ??= [];

        if (!_binaryLeftOperandParenthesizerCache.TryGetValue(binaryOperator, out var parenthesizerRule))
        {
            parenthesizerRule = node => ParenthesizeLeftSideOfBinary(binaryOperator, node);
            _binaryLeftOperandParenthesizerCache[binaryOperator] = parenthesizerRule;
        }

        return parenthesizerRule;
    }

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:105 — <c>getParenthesizeRightSideOfBinaryForOperator</c>.</summary>
    public Func<Expression, Expression> GetParenthesizeRightSideOfBinaryForOperator(SyntaxKind binaryOperator)
    {
        _binaryRightOperandParenthesizerCache ??= [];

        if (!_binaryRightOperandParenthesizerCache.TryGetValue(binaryOperator, out var parenthesizerRule))
        {
            parenthesizerRule = node => ParenthesizeRightSideOfBinary(binaryOperator, null, node);
            _binaryRightOperandParenthesizerCache[binaryOperator] = parenthesizerRule;
        }

        return parenthesizerRule;
    }

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:115 — <c>mixingBinaryOperatorsRequiresParentheses</c>.</summary>
    /// <remarks>
    /// <c>&amp;&amp;</c> and <c>||</c> may not be mixed with <c>??</c> without parentheses, in either order.
    /// </remarks>
    private static bool MixingBinaryOperatorsRequiresParentheses(SyntaxKind a, SyntaxKind b)
    {
        if (a == SyntaxKind.QuestionQuestionToken)
        {
            return b == SyntaxKind.AmpersandAmpersandToken || b == SyntaxKind.BarBarToken;
        }

        if (b == SyntaxKind.QuestionQuestionToken)
        {
            return a == SyntaxKind.AmpersandAmpersandToken || a == SyntaxKind.BarBarToken;
        }

        return false;
    }

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:133 — <c>binaryOperandNeedsParentheses</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's function falls off the end of its <c>switch</c> and answers <c>undefined</c>, which its callers
    /// read as "no parentheses"; the port's <see cref="Comparison"/> has exactly the three members the switch tests, so
    /// the unreachable arm answers the same thing explicitly.
    /// </remarks>
    private bool BinaryOperandNeedsParentheses(
        SyntaxKind binaryOperator,
        Expression operand,
        bool isLeftSideOfBinary,
        Expression? leftOperand)
    {
        var emittedOperand = Utilities.SkipPartiallyEmittedExpressions(operand);
        if (NodeTests.IsBinaryExpression(emittedOperand)
            && MixingBinaryOperatorsRequiresParentheses(binaryOperator, ((BinaryExpression)emittedOperand).OperatorToken!.Kind))
        {
            return true;
        }

        // If the operand has lower precedence, then it needs to be parenthesized to preserve the intent of the
        // expression. For example, if the operand is `a + b` and the operator is `*`, then we need to parenthesize the
        // operand to preserve the intended order of operations: `(a + b) * x`.
        //
        // If the operand has higher precedence, then it does not need to be parenthesized: `a * b + x` keeps its
        // meaning. If the operand has the same precedence, the associativity of the operator decides.
        var binaryOperatorPrecedence = Utilities.GetOperatorPrecedence(SyntaxKind.BinaryExpression, binaryOperator);
        var binaryOperatorAssociativity = Utilities.GetOperatorAssociativity(SyntaxKind.BinaryExpression, binaryOperator);
        if (!isLeftSideOfBinary && operand.Kind == SyntaxKind.ArrowFunction && binaryOperatorPrecedence > OperatorPrecedence.Assignment)
        {
            // We need to parenthesize arrow functions on the right side to avoid it being
            // parsed as parenthesized expression: `a && (() => {})`
            return true;
        }

        var operandPrecedence = Utilities.GetExpressionPrecedence(emittedOperand);
        switch (Core.CompareValues((int)operandPrecedence, (int)binaryOperatorPrecedence))
        {
            case Comparison.LessThan:
                // If the operand is the right side of a right-associative binary operation
                // and is a yield expression, then we do not need parentheses.
                if (!isLeftSideOfBinary
                    && binaryOperatorAssociativity == Associativity.Right
                    && operand.Kind == SyntaxKind.YieldExpression)
                {
                    return false;
                }

                return true;

            case Comparison.GreaterThan:
                return false;

            case Comparison.EqualTo:
                if (isLeftSideOfBinary)
                {
                    // No need to parenthesize the left operand when the binary operator is left associative:
                    //  (a*b)/x    -> a*b/x
                    //  (a**b)/x   -> a**b/x
                    //
                    // Parentheses are needed for the left operand when the binary operator is right associative:
                    //  (a/b)**x   -> (a/b)**x
                    //  (a**b)**x  -> (a**b)**x
                    return binaryOperatorAssociativity == Associativity.Right;
                }

                if (NodeTests.IsBinaryExpression(emittedOperand)
                    && ((BinaryExpression)emittedOperand).OperatorToken!.Kind == binaryOperator)
                {
                    // No need to parenthesize the right operand when the binary operator and operand are the same
                    // and one of the following:
                    //  x*(a*b)     => x*a*b
                    //  x|(a|b)     => x|a|b
                    //  x&(a&b)     => x&a&b
                    //  x^(a^b)     => x^a^b
                    if (OperatorHasAssociativeProperty(binaryOperator))
                    {
                        return false;
                    }

                    // No need to parenthesize the right operand when the binary operator is plus (+) if both the left
                    // and right operands consist solely of either literals of the same kind or binary plus (+)
                    // expressions for literals of the same kind (recursively).
                    //  "a"+(1+2)       => "a"+(1+2)
                    //  "a"+("b"+"c")   => "a"+"b"+"c"
                    if (binaryOperator == SyntaxKind.PlusToken)
                    {
                        var leftKind = Js.IsTruthy(leftOperand) ? GetLiteralKindOfBinaryPlusOperand(leftOperand!) : SyntaxKind.Unknown;
                        if (Utilities.IsLiteralKind(leftKind) && leftKind == GetLiteralKindOfBinaryPlusOperand(emittedOperand))
                        {
                            return false;
                        }
                    }
                }

                // No need to parenthesize the right operand when the operand is right associative:
                //  x/(a**b)    -> x/a**b
                //  x**(a**b)   -> x**a**b
                //
                // Parentheses are needed for the right operand when the operand is left associative:
                //  x/(a*b)     -> x/(a*b)
                //  x**(a/b)    -> x**(a/b)
                var operandAssociativity = Utilities.GetExpressionAssociativity(emittedOperand);
                return operandAssociativity == Associativity.Left;

            default:
                // Unreachable: `Comparison` has three members and all three are handled above.
                return false;
        }
    }

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:242 — <c>operatorHasAssociativeProperty</c>.</summary>
    /// <remarks>
    /// The operators below are associative in JavaScript. While addition is associative in mathematics, JavaScript's
    /// <c>+</c> is not guaranteed to be: it is overloaded with string concatenation.
    /// </remarks>
    private static bool OperatorHasAssociativeProperty(SyntaxKind binaryOperator)
        => binaryOperator is SyntaxKind.AsteriskToken
            or SyntaxKind.BarToken
            or SyntaxKind.AmpersandToken
            or SyntaxKind.CaretToken
            or SyntaxKind.CommaToken;

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:265 — <c>getLiteralKindOfBinaryPlusOperand</c>.</summary>
    /// <remarks>
    /// Answers whether an expression is a homogeneous set of literal expressions or binary plus expressions that all
    /// share the same literal kind, which is what lets the right-hand operand of a binary plus be emitted without
    /// parentheses. The answer is memoized — see <see cref="_cachedLiteralKind"/>.
    /// </remarks>
    private SyntaxKind GetLiteralKindOfBinaryPlusOperand(Expression node)
    {
        node = Utilities.SkipPartiallyEmittedExpressions(node);

        if (Utilities.IsLiteralKind(node.Kind))
        {
            return node.Kind;
        }

        if (node is BinaryExpression { OperatorToken.Kind: SyntaxKind.PlusToken } plus)
        {
            if (_cachedLiteralKind.TryGetValue(plus, out var cachedLiteralKind))
            {
                return cachedLiteralKind.Value;
            }

            var leftKind = GetLiteralKindOfBinaryPlusOperand(plus.Left!);
            var literalKind = Utilities.IsLiteralKind(leftKind) && leftKind == GetLiteralKindOfBinaryPlusOperand(plus.Right!)
                ? leftKind
                : SyntaxKind.Unknown;

            // PORT-NOTE: TS assigns the field (`node.cachedLiteralKind = literalKind`), which never throws; TryAdd is
            // the same "write the memo if it is not there yet" without the concurrent-writer exception Add raises.
            _cachedLiteralKind.TryAdd(plus, new StrongBox<SyntaxKind>(literalKind));
            return literalKind;
        }

        return SyntaxKind.Unknown;
    }

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:299 — <c>parenthesizeBinaryOperand</c>.</summary>
    private Expression ParenthesizeBinaryOperand(
        SyntaxKind binaryOperator,
        Expression operand,
        bool isLeftSideOfBinary,
        Expression? leftOperand = null)
    {
        var skipped = Utilities.SkipPartiallyEmittedExpressions(operand);

        // If the resulting expression is already parenthesized, we do not need to do any further processing.
        if (skipped.Kind == SyntaxKind.ParenthesizedExpression)
        {
            return operand;
        }

        return BinaryOperandNeedsParentheses(binaryOperator, operand, isLeftSideOfBinary, leftOperand)
            ? _factory.CreateParenthesizedExpression(operand)
            : operand;
    }

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:312 — <c>parenthesizeLeftSideOfBinary</c>.</summary>
    public Expression ParenthesizeLeftSideOfBinary(SyntaxKind binaryOperator, Expression? leftSide)
        => ParenthesizeBinaryOperand(binaryOperator, leftSide!, isLeftSideOfBinary: true);

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:316 — <c>parenthesizeRightSideOfBinary</c>.</summary>
    public Expression ParenthesizeRightSideOfBinary(SyntaxKind binaryOperator, Expression? leftSide, Expression? rightSide)
        => ParenthesizeBinaryOperand(binaryOperator, rightSide!, isLeftSideOfBinary: false, leftSide);

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:320 — <c>parenthesizeExpressionOfComputedPropertyName</c>.</summary>
    public Expression ParenthesizeExpressionOfComputedPropertyName(Expression? expression)
        => FactoryUtilities.IsCommaSequence(expression!)
            ? _factory.CreateParenthesizedExpression(expression)
            : expression!;

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:324 — <c>parenthesizeConditionOfConditionalExpression</c>.</summary>
    public Expression ParenthesizeConditionOfConditionalExpression(Expression? condition)
    {
        var conditionalPrecedence = Utilities.GetOperatorPrecedence(SyntaxKind.ConditionalExpression, SyntaxKind.QuestionToken);
        var emittedCondition = Utilities.SkipPartiallyEmittedExpressions(condition!);
        var conditionPrecedence = Utilities.GetExpressionPrecedence(emittedCondition);
        if (Core.CompareValues((int)conditionPrecedence, (int)conditionalPrecedence) != Comparison.GreaterThan)
        {
            return _factory.CreateParenthesizedExpression(condition);
        }

        return condition!;
    }

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:334 — <c>parenthesizeBranchOfConditionalExpression</c>.</summary>
    /// <remarks>
    /// Per the ES grammar both <c>whenTrue</c> and <c>whenFalse</c> of a conditional expression are assignment
    /// expressions, so a comma expression introduced by an earlier transformation has to be wrapped in parens: the
    /// comma operator has the lowest precedence.
    /// </remarks>
    public Expression ParenthesizeBranchOfConditionalExpression(Expression? branch)
    {
        var emittedExpression = Utilities.SkipPartiallyEmittedExpressions(branch!);
        return FactoryUtilities.IsCommaSequence(emittedExpression)
            ? _factory.CreateParenthesizedExpression(branch)
            : branch!;
    }

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:355 — <c>parenthesizeExpressionOfExportDefault</c>.</summary>
    /// <remarks>
    /// Per the spec, <c>export default</c> accepts an <em>AssignmentExpression</em> but has a lookahead restriction for
    /// <c>function</c>, <c>async function</c> and <c>class</c> — so a comma expression, a comma list, a function
    /// expression or a class expression (even one reached through property/element access) needs parentheses.
    /// </remarks>
    public Expression ParenthesizeExpressionOfExportDefault(Expression? expression)
    {
        var check = Utilities.SkipPartiallyEmittedExpressions(expression!);
        var needsParens = FactoryUtilities.IsCommaSequence(check);
        if (!needsParens)
        {
            switch (Utilities.GetLeftmostExpression(check, stopAtCallExpressions: false).Kind)
            {
                case SyntaxKind.ClassExpression:
                case SyntaxKind.FunctionExpression:
                    needsParens = true;
                    break;
            }
        }

        return needsParens ? _factory.CreateParenthesizedExpression(expression) : expression!;
    }

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:372 — <c>parenthesizeExpressionOfNew</c>.</summary>
    /// <remarks>
    /// Wraps an expression in parentheses where that is needed to use it as the expression of a <c>NewExpression</c>.
    /// </remarks>
    public LeftHandSideExpression ParenthesizeExpressionOfNew(Expression? expression)
    {
        var leftmostExpr = Utilities.GetLeftmostExpression(expression!, stopAtCallExpressions: true);
        switch (leftmostExpr.Kind)
        {
            case SyntaxKind.CallExpression:
                return _factory.CreateParenthesizedExpression(expression);

            case SyntaxKind.NewExpression:
                return !Js.IsTruthy(((NewExpression)leftmostExpr).Arguments)
                    ? _factory.CreateParenthesizedExpression(expression)
                    // TS asserts this narrows (see its TODO); a `new` with an argument list is a left-hand-side
                    // expression, which is what the caller requires.
                    : (LeftHandSideExpression)expression!;
        }

        return ParenthesizeLeftSideOfAccess(expression, null);
    }

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:391 — <c>parenthesizeLeftSideOfAccess</c>.</summary>
    /// <remarks>
    /// Wraps an expression in parentheses where that is needed to use it for property or element access.
    /// <c>isLeftHandSideExpression</c> is almost the right criterion for when a dot may follow without parentheses;
    /// the known exception is <c>new C.x</c>, which is not the same as <c>(new C).x</c>.
    /// </remarks>
    public LeftHandSideExpression ParenthesizeLeftSideOfAccess(Expression? expression, bool? optionalChain)
    {
        var emittedExpression = Utilities.SkipPartiallyEmittedExpressions(expression!);
        if (Utilities.IsLeftHandSideExpression(emittedExpression)
            && (emittedExpression.Kind != SyntaxKind.NewExpression || Js.IsTruthy(((NewExpression)emittedExpression).Arguments))
            && (optionalChain == true || !Utilities.IsOptionalChain(emittedExpression)))
        {
            // TS has a TODO on this assertion; the guard above is exactly its criterion.
            return (LeftHandSideExpression)expression!;
        }

        return Utilities.SetTextRange(_factory.CreateParenthesizedExpression(expression), expression);
    }

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:412 — <c>parenthesizeOperandOfPostfixUnary</c>.</summary>
    public LeftHandSideExpression ParenthesizeOperandOfPostfixUnary(Expression? operand)
        => Utilities.IsLeftHandSideExpression(operand!)
            ? (LeftHandSideExpression)operand!
            : Utilities.SetTextRange(_factory.CreateParenthesizedExpression(operand), operand);

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:417 — <c>parenthesizeOperandOfPrefixUnary</c>.</summary>
    public UnaryExpression ParenthesizeOperandOfPrefixUnary(Expression? operand)
        => Utilities.IsUnaryExpression(operand!)
            ? (UnaryExpression)operand!
            : Utilities.SetTextRange(_factory.CreateParenthesizedExpression(operand), operand);

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:422 — <c>parenthesizeExpressionsOfCommaDelimitedList</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types the parameter (and the emitted flag it reads off it) as <c>NodeArray&lt;Expression&gt;</c>, the
    /// generated interface as <see cref="List{T}"/>. The flag is therefore read only when the argument really is a
    /// <see cref="NodeArray{T}"/> — which every caller in the factory passes — and a plain list is treated as "no
    /// trailing comma given", the same thing TS answers for an array that carries no flag.
    /// </remarks>
    public NodeArray<Expression> ParenthesizeExpressionsOfCommaDelimitedList(List<Expression>? elements)
    {
        var source = elements as NodeArray<Expression>;
        var result = Core.SameMap(elements, (expression, _) => ParenthesizeExpressionForDisallowedComma(expression));
        return Utilities.SetTextRange(_factory.CreateNodeArray(result, source?.HasTrailingComma), source);
    }

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:427 — <c>parenthesizeExpressionForDisallowedComma</c>.</summary>
    public Expression ParenthesizeExpressionForDisallowedComma(Expression? expression)
    {
        var emittedExpression = Utilities.SkipPartiallyEmittedExpressions(expression!);
        var expressionPrecedence = Utilities.GetExpressionPrecedence(emittedExpression);
        var commaPrecedence = Utilities.GetOperatorPrecedence(SyntaxKind.BinaryExpression, SyntaxKind.CommaToken);
        return expressionPrecedence > commaPrecedence
            ? expression!
            : Utilities.SetTextRange(_factory.CreateParenthesizedExpression(expression), expression);
    }

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:435 — <c>parenthesizeExpressionOfExpressionStatement</c>.</summary>
    /// <remarks>
    /// Parenthesizes a callee of a call statement that starts with <c>function</c> or <c>() =&gt;</c>, and an expression
    /// statement whose leftmost token would be read as a block or as a function declaration.
    /// </remarks>
    public Expression ParenthesizeExpressionOfExpressionStatement(Expression? expression)
    {
        var emittedExpression = Utilities.SkipPartiallyEmittedExpressions(expression!);
        if (NodeTests.IsCallExpression(emittedExpression))
        {
            var call = (CallExpression)emittedExpression;
            var callee = call.Expression;
            var kind = Utilities.SkipPartiallyEmittedExpressions(callee!).Kind;
            if (kind is SyntaxKind.FunctionExpression or SyntaxKind.ArrowFunction)
            {
                var updated = _factory.UpdateCallExpression(
                    call,
                    Utilities.SetTextRange(_factory.CreateParenthesizedExpression(callee), callee),
                    call.TypeArguments,
                    call.Arguments);
                return _factory.RestoreOuterExpressions(expression, updated, OuterExpressionKinds.PartiallyEmittedExpressions);
            }
        }

        var leftmostExpressionKind = Utilities.GetLeftmostExpression(emittedExpression, stopAtCallExpressions: false).Kind;
        if (leftmostExpressionKind is SyntaxKind.ObjectLiteralExpression or SyntaxKind.FunctionExpression)
        {
            return Utilities.SetTextRange(_factory.CreateParenthesizedExpression(expression), expression);
        }

        return expression!;
    }

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:461-463 — <c>parenthesizeConciseBodyOfArrowFunction</c> (the <c>Expression</c> overload).</summary>
    /// <remarks>
    /// A concise body that would re-parse as something else — a comma expression, or an object literal read as a block
    /// — is parenthesized; a block body is not touched (see the <see cref="Node"/> overload).
    /// </remarks>
    public Expression ParenthesizeConciseBodyOfArrowFunction(Expression? body)
    {
        if (!NodeTests.IsBlock(body!)
            && (FactoryUtilities.IsCommaSequence(body!)
                || Utilities.GetLeftmostExpression(body!, stopAtCallExpressions: false).Kind == SyntaxKind.ObjectLiteralExpression))
        {
            return Utilities.SetTextRange(_factory.CreateParenthesizedExpression(body), body);
        }

        return body!;
    }

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:462 — <c>parenthesizeConciseBodyOfArrowFunction</c> (the <c>ConciseBody</c> overload).</summary>
    /// <remarks>
    /// TS's <c>ConciseBody</c> is <c>Expression | Block</c>. The port declares the two overloads the interface asks
    /// for, so a <see cref="Block"/> — which is not an <see cref="Expression"/> in the C# model either — is returned
    /// unchanged, which is all <c>isBlock(body)</c> amounts to; everything else is the expression rule.
    /// </remarks>
    public Node ParenthesizeConciseBodyOfArrowFunction(Node? body)
        => body is Expression expression ? ParenthesizeConciseBodyOfArrowFunction(expression) : body!;

    // Type[Extends] :
    //     FunctionOrConstructorType
    //     ConditionalType[?Extends]
    //
    // ConditionalType[Extends] :
    //     UnionType[?Extends]
    //     [~Extends] UnionType[~Extends] `extends` Type[+Extends] `?` Type[~Extends] `:` Type[~Extends]
    //
    // - The check type does not allow function, constructor, or conditional types (they must be parenthesized)
    // - The extends type does not allow conditional types (function and constructor types are fine)
    // - The true and false branch types allow any type

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:483 — <c>parenthesizeCheckTypeOfConditionalType</c>.</summary>
    public ITypeNode ParenthesizeCheckTypeOfConditionalType(ITypeNode? type)
    {
        switch (type!.Kind)
        {
            case SyntaxKind.FunctionType:
            case SyntaxKind.ConstructorType:
            case SyntaxKind.ConditionalType:
                return _factory.CreateParenthesizedType(type);
        }

        return type!;
    }

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:493 — <c>parenthesizeExtendsTypeOfConditionalType</c>.</summary>
    public ITypeNode ParenthesizeExtendsTypeOfConditionalType(ITypeNode? type)
    {
        switch (type!.Kind)
        {
            case SyntaxKind.ConditionalType:
                return _factory.CreateParenthesizedType(type);
        }

        return type!;
    }

    // UnionType[Extends] :
    //     `|`? IntersectionType[?Extends]
    //     UnionType[?Extends] `|` IntersectionType[?Extends]
    //
    // - A union type constituent has the same precedence as the check type of a conditional type

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:506 — <c>parenthesizeConstituentTypeOfUnionType</c>.</summary>
    public ITypeNode ParenthesizeConstituentTypeOfUnionType(ITypeNode? type)
    {
        switch (type!.Kind)
        {
            case SyntaxKind.UnionType: // Not strictly necessary, but a union containing a union should have been flattened
            case SyntaxKind.IntersectionType: // Not strictly necessary, but makes generated output more readable and avoids breaks in DT tests
                return _factory.CreateParenthesizedType(type);
        }

        return ParenthesizeCheckTypeOfConditionalType(type);
    }

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:515 — <c>parenthesizeConstituentTypesOfUnionType</c>.</summary>
    public NodeArray<ITypeNode> ParenthesizeConstituentTypesOfUnionType(List<ITypeNode>? constituents)
        => _factory.CreateNodeArray(
            Core.SameMap(constituents, (type, _) => ParenthesizeConstituentTypeOfUnionType(type)),
            null);

    // IntersectionType[Extends] :
    //     `&`? TypeOperator[?Extends]
    //     IntersectionType[?Extends] `&` TypeOperator[?Extends]
    //
    // - An intersection type constituent does not allow function, constructor, conditional, or union types (they must
    //   be parenthesized)

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:524 — <c>parenthesizeConstituentTypeOfIntersectionType</c>.</summary>
    public ITypeNode ParenthesizeConstituentTypeOfIntersectionType(ITypeNode? type)
    {
        switch (type!.Kind)
        {
            case SyntaxKind.UnionType:
            case SyntaxKind.IntersectionType: // Not strictly necessary, but an intersection containing an intersection should have been flattened
                return _factory.CreateParenthesizedType(type);
        }

        return ParenthesizeConstituentTypeOfUnionType(type);
    }

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:533 — <c>parenthesizeConstituentTypesOfIntersectionType</c>.</summary>
    public NodeArray<ITypeNode> ParenthesizeConstituentTypesOfIntersectionType(List<ITypeNode>? constituents)
        => _factory.CreateNodeArray(
            Core.SameMap(constituents, (type, _) => ParenthesizeConstituentTypeOfIntersectionType(type)),
            null);

    // TypeOperator[Extends] :
    //     PostfixType
    //     InferType[?Extends]
    //     `keyof` TypeOperator[?Extends]
    //     `unique` TypeOperator[?Extends]
    //     `readonly` TypeOperator[?Extends]

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:544 — <c>parenthesizeOperandOfTypeOperator</c>.</summary>
    public ITypeNode ParenthesizeOperandOfTypeOperator(ITypeNode? type)
    {
        switch (type!.Kind)
        {
            case SyntaxKind.IntersectionType:
                return _factory.CreateParenthesizedType(type);
        }

        return ParenthesizeConstituentTypeOfIntersectionType(type);
    }

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:552 — <c>parenthesizeOperandOfReadonlyTypeOperator</c>.</summary>
    public ITypeNode ParenthesizeOperandOfReadonlyTypeOperator(ITypeNode? type)
    {
        switch (type!.Kind)
        {
            case SyntaxKind.TypeOperator:
                return _factory.CreateParenthesizedType(type);
        }

        return ParenthesizeOperandOfTypeOperator(type);
    }

    // PostfixType :
    //     NonArrayType
    //     NonArrayType [no LineTerminator here] `!` // JSDoc
    //     NonArrayType [no LineTerminator here] `?` // JSDoc
    //     IndexedAccessType
    //     ArrayType
    //
    // IndexedAccessType :
    //     NonArrayType `[` Type[~Extends] `]`
    //
    // ArrayType :
    //     NonArrayType `[` `]`

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:573 — <c>parenthesizeNonArrayTypeOfPostfixType</c>.</summary>
    public ITypeNode ParenthesizeNonArrayTypeOfPostfixType(ITypeNode? type)
    {
        switch (type!.Kind)
        {
            case SyntaxKind.InferType:
            case SyntaxKind.TypeOperator:
            case SyntaxKind.TypeQuery: // Not strictly necessary, but makes generated output more readable and avoids breaks in DT tests
                return _factory.CreateParenthesizedType(type);
        }

        return ParenthesizeOperandOfTypeOperator(type);
    }

    // TupleType :
    //     `[` Elision? `]`
    //     `[` NamedTupleElementTypes `]`
    //     `[` NamedTupleElementTypes `,` Elision? `]`
    //     `[` TupleElementTypes `]`
    //     `[` TupleElementTypes `,` Elision? `]`
    //
    // TupleElementType :
    //     Type[~Extends] // NOTE: Needs cover grammar to disallow JSDoc postfix-optional
    //     OptionalType
    //     RestType

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:613 — <c>parenthesizeElementTypesOfTupleType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS maps the elements with <c>sameMap</c>, whose result may be the argument array itself, and hands
    /// that to <c>createNodeArray</c> with no trailing-comma argument — which returns the same <c>NodeArray</c> (flag,
    /// text range and transform flags intact) when nothing changed. The port has to build a <see cref="List{T}"/> of
    /// <see cref="ITypeNode"/> instead: TS's element type is the union <c>TypeNode | NamedTupleMember</c> and the
    /// port's is <see cref="Node"/>, and C# has no covariant view that would carry the mapped array across that
    /// widening. A fresh list is therefore the path TS takes too (its <c>NodeArray</c> identity is lost either way),
    /// so the trailing-comma flag is left unset exactly as TS leaves it for a freshly built array. The element objects
    /// are the same ones.
    /// </remarks>
    public NodeArray<ITypeNode> ParenthesizeElementTypesOfTupleType(List<Node>? types)
        => _factory.CreateNodeArray(
            Core.Map(types, (type, _) => (ITypeNode)ParenthesizeElementTypeOfTupleType(type)),
            null);

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:617 — <c>parenthesizeElementTypeOfTupleType</c>.</summary>
    public Node ParenthesizeElementTypeOfTupleType(Node? type)
        => HasJSDocPostfixQuestion((ITypeNode)type!)
            ? _factory.CreateParenthesizedType((ITypeNode)type!)
            : type!;

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:622 — <c>hasJSDocPostfixQuestion</c>.</summary>
    /// <remarks>
    /// Recurses to the rightmost position of a type to find out whether it ends in a JSDoc <c>?</c>, which the tuple
    /// and optional-type rules have to parenthesize so the <c>?</c> is not read as part of an outer construct.
    /// </remarks>
    private bool HasJSDocPostfixQuestion(ITypeNode type)
    {
        if (NodeTests.IsJSDocNullableType(AsNode(type))) return ((JSDocNullableType)type).Postfix;
        if (NodeTests.IsNamedTupleMember(AsNode(type))) return HasJSDocPostfixQuestion(((NamedTupleMember)type).Type!);
        if (NodeTests.IsFunctionTypeNode(AsNode(type)) || NodeTests.IsConstructorTypeNode(AsNode(type)))
        {
            return HasJSDocPostfixQuestion(((FunctionOrConstructorTypeNodeBase)type).Type!);
        }

        if (NodeTests.IsTypeOperatorNode(AsNode(type))) return HasJSDocPostfixQuestion(((TypeOperatorNode)type).Type!);
        if (NodeTests.IsConditionalTypeNode(AsNode(type))) return HasJSDocPostfixQuestion(((ConditionalTypeNode)type).FalseType!);
        if (NodeTests.IsUnionTypeNode(AsNode(type))) return HasJSDocPostfixQuestion(Core.Last(((UnionTypeNode)type).Types!));
        if (NodeTests.IsIntersectionTypeNode(AsNode(type))) return HasJSDocPostfixQuestion(Core.Last(((IntersectionTypeNode)type).Types!));
        if (NodeTests.IsInferTypeNode(AsNode(type)))
        {
            var typeParameter = ((InferTypeNode)type).TypeParameter!;
            return Js.IsTruthy(typeParameter.Constraint) && HasJSDocPostfixQuestion(typeParameter.Constraint!);
        }

        return false;
    }

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:633 — <c>parenthesizeTypeOfOptionalType</c>.</summary>
    public ITypeNode ParenthesizeTypeOfOptionalType(ITypeNode? type)
        => HasJSDocPostfixQuestion(type!)
            ? _factory.CreateParenthesizedType(type)
            : ParenthesizeNonArrayTypeOfPostfixType(type);

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:659 — <c>parenthesizeLeadingTypeArgument</c>.</summary>
    /// <remarks>
    /// A function or constructor type as the <em>first</em> type argument would be read as the type parameter list of
    /// the enclosing construct, so it needs parentheses; later positions are unambiguous (see
    /// <see cref="ParenthesizeOrdinalTypeArgument"/>).
    /// </remarks>
    public ITypeNode ParenthesizeLeadingTypeArgument(ITypeNode? typeNode)
        => Utilities.IsFunctionOrConstructorTypeNode(AsNode(typeNode!)) && Js.IsTruthy(((FunctionOrConstructorTypeNodeBase)typeNode!).TypeParameters)
            ? _factory.CreateParenthesizedType(typeNode)
            : typeNode!;

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:663 — <c>parenthesizeOrdinalTypeArgument</c>.</summary>
    private ITypeNode ParenthesizeOrdinalTypeArgument(ITypeNode node, int i)
        => i == 0 ? ParenthesizeLeadingTypeArgument(node) : node;

    /// <summary>ported from: src/compiler/factory/parenthesizerRules.ts:667 — <c>parenthesizeTypeArguments</c>.</summary>
    public NodeArray<ITypeNode>? ParenthesizeTypeArguments(List<ITypeNode>? typeParameters)
    {
        if (Core.Some(typeParameters))
        {
            return _factory.CreateNodeArray(Core.SameMap(typeParameters, ParenthesizeOrdinalTypeArgument), null);
        }

        return null;
    }

    /// <summary>
    /// The bridge between <see cref="ITypeNode"/> and <see cref="Node"/> the type-node checks need.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>isXxx</c> predicates are declared over <c>Node</c>, while the type-node members this file
    /// reads them for are <c>ITypeNode</c> values; in C# those two types do not stand in an inheritance relation (both
    /// narrow <see cref="INode"/>, and <see cref="Node"/> is the one that implements it). Every implementation of
    /// <see cref="ITypeNode"/> in the model is a <see cref="Node"/> subclass, so the bridge is a checked downcast: a
    /// type node that broke that rule would fail loudly here rather than be misjudged.
    /// </remarks>
    private static Node AsNode(ITypeNode type) => (Node)type;
}
