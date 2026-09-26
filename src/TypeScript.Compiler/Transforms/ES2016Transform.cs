#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/es2016.ts:1-122 — the whole ES2016 downlevel transform.
/// <summary>
/// ported from: src/compiler/transformers/es2016.ts:21-122 — <c>transformES2016</c>: the transformer that rewrites the
/// two ES2016 syntaxes a <see cref="SourceFile"/> may contain — the exponentiation operator (<c>a ** b</c> and
/// <c>a **= b</c>) — into the <c>Math.pow</c> calls the emitter prints.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (the shape): as <c>ES2015Transform</c> records for <c>es2015.ts</c>, the reference exports one function
/// whose body is a table of closures over the factory's locals; the port states the factory as
/// <see cref="ES2016Transform.TransformES2016"/> and the closures as the members of <see cref="ES2016Transformer"/>.
/// </para>
/// <para>
/// PORT-NOTE (<c>__assign</c>): the batch note for this slice names the object-rest/spread helper as part of ES2016's
/// surface. In the reference it is not — <c>es2016.ts</c> never mentions <c>__assign</c>; the helper is requested by
/// <c>es2018.ts</c>'s <c>visitObjectLiteralExpression</c> through
/// <c>emitHelpers().createAssignHelper</c> (<c>EmitHelpers.AssignHelper</c>, landed), which is the port's
/// <see cref="ES2018Transform"/>. This file therefore states exactly what <c>es2016.ts</c> declares.
/// </para>
/// </remarks>
internal static class ES2016Transform
{
    /// <summary>ported from: src/compiler/transformers/es2016.ts:21-122 — <c>transformES2016</c>.</summary>
    public static Func<object?, object?> TransformES2016(ITransformerContext context)
        => new ES2016Transformer(context).TransformSourceFileOrBundle;
}

/// <summary>ported from: src/compiler/transformers/es2016.ts:22-121 — the body of <c>transformES2016</c>.</summary>
internal sealed class ES2016Transformer
{
    private readonly ITransformerContext _context;
    private readonly NodeFactory _factory;

    /// <summary>ported from: src/compiler/transformers/es2016.ts:28 — <c>chainBundle(context, transformSourceFile)</c>.</summary>
    private readonly Func<Node, Node> _transformSourceFile;

    /// <summary>ported from: src/compiler/transformers/es2016.ts:22-27 — the factory's prologue.</summary>
    /// <remarks>PORT-NOTE: the reference destructures <c>factory</c> and <c>hoistVariableDeclaration</c> out of the
    /// context; the port keeps the context and reads the same two members through it (their bodies are
    /// <c>TransformationContext&lt;T&gt;</c>'s, Transformer.cs).</remarks>
    public ES2016Transformer(ITransformerContext context)
    {
        _context = context;
        _factory = context.Factory;
        _transformSourceFile = TransformationUtilities.ChainBundle(context, TransformSourceFile);
    }

    /// <summary>ported from: src/compiler/transformers/es2016.ts:28 — the returned
    /// <c>chainBundle(context, transformSourceFile)</c>.</summary>
    public object? TransformSourceFileOrBundle(object? node) => _transformSourceFile((Node)node!);

    /// <summary>ported from: src/compiler/transformers/es2016.ts:29-35 — <c>transformSourceFile</c>.</summary>
    private SourceFile TransformSourceFile(SourceFile node)
    {
        if (node.IsDeclarationFile)
        {
            return node;
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/es2016.ts:37-47 — <c>visitor</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>node.transformFlags &amp; TransformFlags.ContainsES2016</c> is truthiness of a
    /// number, so the port states the <c>!= 0</c> test; <c>visitEachChild</c> is
    /// <see cref="Utilities.VisitEachChildWorker"/>.</remarks>
    private object? Visitor(Node node)
    {
        if ((node.TransformFlags & TransformFlags.ContainsES2016) == 0)
        {
            return node;
        }

        return node.Kind switch
        {
            SyntaxKind.BinaryExpression => VisitBinaryExpression((BinaryExpression)node),
            _ => Utilities.VisitEachChildWorker(node, Visitor, _context),
        };
    }

    /// <summary>ported from: src/compiler/transformers/es2016.ts:49-58 — <c>visitBinaryExpression</c>.</summary>
    private Expression VisitBinaryExpression(BinaryExpression node)
        => node.OperatorToken!.Kind switch
        {
            SyntaxKind.AsteriskAsteriskEqualsToken => VisitExponentiationAssignmentExpression(node),
            SyntaxKind.AsteriskAsteriskToken => VisitExponentiationExpression(node),
            _ => (Expression)Utilities.VisitEachChildWorker(node, Visitor, _context)!,
        };

    /// <summary>ported from: src/compiler/transformers/es2016.ts:60-114 — <c>visitExponentiationAssignmentExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares <c>let target: Expression</c> / <c>let value: Expression</c> and assigns both in each arm,
    /// so the port states the two locals as definite-assigned locals the three arms fill in.
    /// </remarks>
    private Expression VisitExponentiationAssignmentExpression(BinaryExpression node)
    {
        Expression target;
        Expression value;
        var left = Utilities.VisitNode(node.Left, Visitor, Utilities.IsExpression)!;
        var right = Utilities.VisitNode(node.Right, Visitor, Utilities.IsExpression)!;
        if (NodeTests.IsElementAccessExpression(left))
        {
            // Transforms `a[x] **= b` into `(_a = a)[_x = x] = Math.pow(_a[_x], b)`
            var elementAccess = (ElementAccessExpression)left;
            var expressionTemp = _factory.CreateTempVariable(_context.HoistVariableDeclaration, /*reservedInNestedScopes*/ null);
            var argumentExpressionTemp = _factory.CreateTempVariable(_context.HoistVariableDeclaration, /*reservedInNestedScopes*/ null);
            target = Utilities.SetTextRange(
                _factory.CreateElementAccessExpression(
                    Utilities.SetTextRange(_factory.CreateAssignment(expressionTemp, elementAccess.Expression), elementAccess.Expression),
                    Utilities.SetTextRange(_factory.CreateAssignment(argumentExpressionTemp, elementAccess.ArgumentExpression), elementAccess.ArgumentExpression)),
                left);
            value = Utilities.SetTextRange(
                _factory.CreateElementAccessExpression(
                    expressionTemp,
                    argumentExpressionTemp),
                left);
        }
        else if (NodeTests.IsPropertyAccessExpression(left))
        {
            // Transforms `a.x **= b` into `(_a = a).x = Math.pow(_a.x, b)`
            var propertyAccess = (PropertyAccessExpression)left;
            var expressionTemp = _factory.CreateTempVariable(_context.HoistVariableDeclaration, /*reservedInNestedScopes*/ null);
            target = Utilities.SetTextRange(
                _factory.CreatePropertyAccessExpression(
                    Utilities.SetTextRange(_factory.CreateAssignment(expressionTemp, propertyAccess.Expression), propertyAccess.Expression),
                    propertyAccess.Name),
                left);
            value = Utilities.SetTextRange(
                _factory.CreatePropertyAccessExpression(
                    expressionTemp,
                    propertyAccess.Name),
                left);
        }
        else
        {
            // Transforms `a **= b` into `a = Math.pow(a, b)`
            target = left;
            value = left;
        }
        return Utilities.SetTextRange(
            _factory.CreateAssignment(
                target,
                Utilities.SetTextRange(_factory.CreateGlobalMethodCall("Math", "pow", [value, right]), node)),
            node);
    }

    /// <summary>ported from: src/compiler/transformers/es2016.ts:116-120 — <c>visitExponentiationExpression</c>.</summary>
    private Expression VisitExponentiationExpression(BinaryExpression node)
    {
        // Transforms `a ** b` into `Math.pow(a, b)`
        var left = Utilities.VisitNode(node.Left, Visitor, Utilities.IsExpression)!;
        var right = Utilities.VisitNode(node.Right, Visitor, Utilities.IsExpression)!;
        return Utilities.SetTextRange(_factory.CreateGlobalMethodCall("Math", "pow", [left, right]), node);
    }
}
