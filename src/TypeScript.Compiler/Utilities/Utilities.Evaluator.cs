namespace TypeScript.Compiler;

/// <summary>ported from: src/compiler/utilities.ts:11316-11320 — the function type <c>createEvaluator</c> returns.</summary>
/// <remarks>
/// PORT-NOTE: TS's <c>createEvaluator</c> returns an overloaded function (<c>(expr: TemplateExpression, location?)
/// =&gt; EvaluatorResult&lt;string | undefined&gt;</c> and <c>(expr: Expression, location?) =&gt; EvaluatorResult</c>);
/// C# delegates are nominal, so the two collapse into this one delegate, whose <c>location</c> keeps the reference's
/// optionality so the landed call sites (<c>Checker.Check.Operators.cs</c> passes one argument,
/// <c>Checker.Check.Classes.cs</c> passes two) both bind. The value slot is <c>object?</c> for the reason
/// <see cref="Utilities.EvaluatorResult{T}"/> records.
/// </remarks>
internal delegate IEvaluatorResult<object?> EvaluateDelegate(Expression expr, IDeclaration? location = null);

/// <summary>
/// The <c>createEvaluator</c> family (utilities.ts:11316-11446): the constant-expression evaluator the checker uses
/// for enum member values.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS's <c>evaluate</c> is a recursive closure over <c>isSyntacticallyString</c>/<c>resolvedOtherFiles</c>/
/// <c>hasExternalReferences</c>, which it reassigns as it descends; C# local functions capture by reference the same
/// way, so <c>Evaluate</c>/<c>EvaluateTemplateExpression</c> are local functions here and the three slots stay locals.
/// The <c>switch</c> arms follow the reference's order, including the JS numeric coercions of the bitwise operators —
/// see <see cref="Evaluate"/>.
/// </remarks>
internal static partial class Utilities
{
    /// <summary>ported from: src/compiler/utilities.ts:11316-11320, 11339-11446 — <c>createEvaluator</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS destructures the <c>EvaluationResolver</c> argument (<c>{ evaluateElementAccessExpression,
    /// evaluateEntityNameExpression }</c>, utilities.ts:10601-10604, built at checker.ts:1553-1556); the port passes the
    /// two as method groups, because the C# face of a two-method interface is the two delegates.
    /// <para>
    /// PORT-NOTE: JS's <c>"" + value</c> is not C# string concatenation for a <c>double</c> — the reference's two
    /// string-building arms (<c>"" + left.value + right.value</c> and <c>result += spanResult.value</c>) therefore go
    /// through <see cref="Js.NumberToString"/> for that arm, which is the port's JS Number-to-string.
    /// </para>
    /// </remarks>
    public static EvaluateDelegate CreateEvaluator(
        Func<ElementAccessExpression, IDeclaration?, IEvaluatorResult<object?>> evaluateElementAccessExpression,
        Func<MemberExpression, IDeclaration?, IEvaluatorResult<object?>> evaluateEntityNameExpression)
    {
        // JS's `"" + value` for the values the evaluator can hold.
        static string CoerceToString(object? value) => value switch
        {
            null => "",
            string s => s,
            double d => Js.NumberToString(d),
            bool b => b ? "true" : "false",
            _ => value.ToString() ?? "",
        };

        IEvaluatorResult<object?> EvaluateTemplateExpression(TemplateExpression expr, IDeclaration? location)
        {
            var result = expr.Head!.Text;
            var resolvedOtherFiles = false;
            var hasExternalReferences = false;
            foreach (var span in expr.TemplateSpans!)
            {
                var spanResult = Evaluate(span.Expression!, location);
                if (spanResult.Value is null)
                {
                    // checker.ts:11407 — the absent value short-circuits without the resolving bookkeeping.
                    return EvaluatorResult<object?>(/*value*/ null, /*isSyntacticallyString*/ true);
                }
                result += CoerceToString(spanResult.Value);
                result += ((ILiteralLikeNode)span.Literal!).Text;
                resolvedOtherFiles |= spanResult.ResolvedOtherFiles;
                hasExternalReferences |= spanResult.HasExternalReferences;
            }

            return EvaluatorResult<object?>(
                result,
                /*isSyntacticallyString*/ true,
                resolvedOtherFiles,
                hasExternalReferences);
        }

        IEvaluatorResult<object?> Evaluate(Expression expr, IDeclaration? location)
        {
            var isSyntacticallyString = false;
            var resolvedOtherFiles = false;
            var hasExternalReferences = false;

            expr = SkipParentheses(expr);
            switch (expr.Kind)
            {
                case SyntaxKind.PrefixUnaryExpression:
                    {
                        var prefix = (PrefixUnaryExpression)expr;
                        var result = Evaluate(prefix.Operand!, location);
                        resolvedOtherFiles = result.ResolvedOtherFiles;
                        hasExternalReferences = result.HasExternalReferences;
                        if (result.Value is double prefixValue)
                        {
                            // PORT-NOTE: JS's bitwise complement is an int32 operation on a Number; the port's
                            // Js.ToInt32 is that coercion (a plain (int) cast saturates instead of wrapping).
                            switch (prefix.Operator)
                            {
                                case SyntaxKind.PlusToken:
                                    return EvaluatorResult<object?>(prefixValue, isSyntacticallyString, resolvedOtherFiles, hasExternalReferences);
                                case SyntaxKind.MinusToken:
                                    return EvaluatorResult<object?>(-prefixValue, isSyntacticallyString, resolvedOtherFiles, hasExternalReferences);
                                case SyntaxKind.TildeToken:
                                    return EvaluatorResult<object?>((double)~Js.ToInt32(prefixValue), isSyntacticallyString, resolvedOtherFiles, hasExternalReferences);
                            }
                        }

                        break;
                    }

                case SyntaxKind.BinaryExpression:
                    {
                        var binary = (BinaryExpression)expr;
                        var left = Evaluate(binary.Left!, location);
                        var right = Evaluate(binary.Right!, location);
                        var operatorKind = binary.OperatorToken!.Kind;
                        isSyntacticallyString = (left.IsSyntacticallyString || right.IsSyntacticallyString) && operatorKind == SyntaxKind.PlusToken;
                        resolvedOtherFiles = left.ResolvedOtherFiles || right.ResolvedOtherFiles;
                        hasExternalReferences = left.HasExternalReferences || right.HasExternalReferences;
                        if (left.Value is double leftValue && right.Value is double rightValue)
                        {
                            // PORT-NOTE: the bitwise/shift arms are JS int32 (or uint32, for `>>>`) operations on
                            // Numbers; Js.ToInt32 is the coercion, and the result stays a double because JS has one
                            // number type. `>>>` keeps its uint32 value — the reference does not re-sign it.
                            switch (operatorKind)
                            {
                                case SyntaxKind.BarToken:
                                    return EvaluatorResult<object?>((double)(Js.ToInt32(leftValue) | Js.ToInt32(rightValue)), isSyntacticallyString, resolvedOtherFiles, hasExternalReferences);
                                case SyntaxKind.AmpersandToken:
                                    return EvaluatorResult<object?>((double)(Js.ToInt32(leftValue) & Js.ToInt32(rightValue)), isSyntacticallyString, resolvedOtherFiles, hasExternalReferences);
                                case SyntaxKind.GreaterThanGreaterThanToken:
                                    return EvaluatorResult<object?>((double)(Js.ToInt32(leftValue) >> Js.ToInt32(rightValue)), isSyntacticallyString, resolvedOtherFiles, hasExternalReferences);
                                case SyntaxKind.GreaterThanGreaterThanGreaterThanToken:
                                    return EvaluatorResult<object?>((double)((uint)Js.ToInt32(leftValue) >> Js.ToInt32(rightValue)), isSyntacticallyString, resolvedOtherFiles, hasExternalReferences);
                                case SyntaxKind.LessThanLessThanToken:
                                    return EvaluatorResult<object?>((double)(Js.ToInt32(leftValue) << Js.ToInt32(rightValue)), isSyntacticallyString, resolvedOtherFiles, hasExternalReferences);
                                case SyntaxKind.CaretToken:
                                    return EvaluatorResult<object?>((double)(Js.ToInt32(leftValue) ^ Js.ToInt32(rightValue)), isSyntacticallyString, resolvedOtherFiles, hasExternalReferences);
                                case SyntaxKind.AsteriskToken:
                                    return EvaluatorResult<object?>(leftValue * rightValue, isSyntacticallyString, resolvedOtherFiles, hasExternalReferences);
                                case SyntaxKind.SlashToken:
                                    return EvaluatorResult<object?>(leftValue / rightValue, isSyntacticallyString, resolvedOtherFiles, hasExternalReferences);
                                case SyntaxKind.PlusToken:
                                    return EvaluatorResult<object?>(leftValue + rightValue, isSyntacticallyString, resolvedOtherFiles, hasExternalReferences);
                                case SyntaxKind.MinusToken:
                                    return EvaluatorResult<object?>(leftValue - rightValue, isSyntacticallyString, resolvedOtherFiles, hasExternalReferences);
                                case SyntaxKind.PercentToken:
                                    return EvaluatorResult<object?>(leftValue % rightValue, isSyntacticallyString, resolvedOtherFiles, hasExternalReferences);
                                case SyntaxKind.AsteriskAsteriskToken:
                                    return EvaluatorResult<object?>(Math.Pow(leftValue, rightValue), isSyntacticallyString, resolvedOtherFiles, hasExternalReferences);
                            }
                        }
                        else if (
                            (left.Value is string or double) &&
                            (right.Value is string or double) &&
                            operatorKind == SyntaxKind.PlusToken)
                        {
                            return EvaluatorResult<object?>(
                                CoerceToString(left.Value) + CoerceToString(right.Value),
                                isSyntacticallyString,
                                resolvedOtherFiles,
                                hasExternalReferences);
                        }

                        break;
                    }

                case SyntaxKind.StringLiteral:
                case SyntaxKind.NoSubstitutionTemplateLiteral:
                    return EvaluatorResult<object?>(((ILiteralLikeNode)expr).Text, /*isSyntacticallyString*/ true);

                case SyntaxKind.TemplateExpression:
                    return EvaluateTemplateExpression((TemplateExpression)expr, location);

                case SyntaxKind.NumericLiteral:
                    return EvaluatorResult<object?>(Js.ToNumber(((NumericLiteral)expr).Text!));

                case SyntaxKind.Identifier:
                    return evaluateEntityNameExpression((Identifier)expr, location);

                case SyntaxKind.PropertyAccessExpression:
                    if (IsEntityNameExpression(expr))
                    {
                        return evaluateEntityNameExpression((PropertyAccessExpression)expr, location);
                    }

                    break;

                case SyntaxKind.ElementAccessExpression:
                    return evaluateElementAccessExpression((ElementAccessExpression)expr, location);
            }

            return EvaluatorResult<object?>(
                /*value*/ null,
                isSyntacticallyString,
                resolvedOtherFiles,
                hasExternalReferences);
        }

        return Evaluate;
    }
}
