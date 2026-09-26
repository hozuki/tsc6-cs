#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/es2021.ts:1-106 — the whole ES2021 downlevel transform
/// <summary>
/// ported from: src/compiler/transformers/es2021.ts:25-106 — <c>transformES2021</c>: the transformer that rewrites
/// ES2021's logical assignment operators (<c>&amp;&amp;=</c>, <c>||=</c>, <c>??=</c>) into the logical expression plus
/// plain assignment the ES2020 emitter prints.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (home, §15.15): the reference exports one function, <c>transformES2021</c>, from
/// <c>transformers/es2021.ts</c>; its body is a table of closures over the factory's locals, so the port states the
/// factory as <see cref="ES2021Transform.TransformES2021"/> and the closures as the members of
/// <see cref="ES2021Transformer"/> — the same "closures → classes" treatment <c>ES2015Transform</c> records.
/// </para>
/// <para>
/// PORT-NOTE (the transformer face): TS's <c>Transformer&lt;SourceFile | Bundle&gt;</c> is
/// <c>(node: SourceFile | Bundle) =&gt; SourceFile | Bundle</c>; the port's transformer list spells its slots
/// <c>Func&lt;object?, object?&gt;</c>, which is the erased shape the reference's own <c>chainBundle</c> call is
/// stored as.
/// </para>
/// </remarks>
internal static class ES2021Transform
{
    /// <summary>ported from: src/compiler/transformers/es2021.ts:25-30 — <c>transformES2021</c>.</summary>
    /// <remarks>PORT-NOTE: the returned member is the reference's own <c>chainBundle(context, transformSourceFile)</c>
    /// call, staying a method group so no delegate is allocated per call beyond the one
    /// <see cref="TransformationUtilities.ChainBundle"/> stores.</remarks>
    public static Func<object?, object?> TransformES2021(ITransformerContext context)
        => new ES2021Transformer(context).TransformSourceFileOrBundle;
}

// ported from: src/compiler/transformers/es2021.ts:25-106 — the `transformES2021` factory's closures
/// <summary>
/// ported from: src/compiler/transformers/es2021.ts:25-106 — the closures <c>transformES2021</c> builds: its
/// <c>transformSourceFile</c>, <c>visitor</c> and <c>transformLogicalAssignment</c>.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (<c>hoistVariableDeclaration</c> and <c>factory</c>): the reference destructures both out of the context;
/// the port keeps the context and reads the same members through it (their bodies are
/// <c>TransformationContext&lt;T&gt;</c>'s, Transformer.cs), so the sequence is the same.
/// </para>
/// <para>
/// PORT-NOTE (the visitor slot): TS's <c>visitor</c> is the <c>Visitor</c> function type, which the port spells
/// <c>Func&lt;Node, object?&gt;</c> (Utilities/Utilities.Visitor.cs), and <c>VisitResult&lt;T&gt;</c> is
/// <c>T | readonly Node[] | undefined</c>, which the port leaves as <c>object?</c>.
/// </para>
/// <para>
/// PORT-NOTE (<c>AssignmentExpression&lt;Token&lt;LogicalOrCoalescingAssignmentOperator&gt;&gt;</c>): the reference's
/// parameter is a <c>BinaryExpression</c> whose operator is narrowed to the three logical assignment tokens; the model
/// carries no such refinement, so the port takes the <see cref="BinaryExpression"/> the visitor already proved to be a
/// logical-or-coalescing assignment expression (§12.5 records the intersection gap for the <c>Extract</c> family).
/// </para>
/// <para>
/// PORT-NOTE (<c>left.expression</c>): TS's <c>AccessExpression</c> is the union
/// <c>PropertyAccessExpression | ElementAccessExpression</c>, which share <c>expression</c> through their common
/// <c>MemberExpression</c> base in TS; the model states the slot on each class instead, so the port reads it through
/// the concrete kind (§12.5 again — the union has no C# spelling).
/// </para>
/// </remarks>
internal sealed class ES2021Transformer
{
    private readonly ITransformerContext _context;
    private readonly NodeFactory _factory;

    /// <summary>ported from: src/compiler/transformers/es2021.ts:26-30 — the factory's prologue.</summary>
    private readonly Func<Node, Node> _transformSourceFile;

    /// <summary>ported from: src/compiler/transformers/es2021.ts:26-30 — the factory's prologue.</summary>
    public ES2021Transformer(ITransformerContext context)
    {
        _context = context;
        _factory = context.Factory;
        _transformSourceFile = TransformationUtilities.ChainBundle(context, TransformSourceFile);
    }

    /// <summary>ported from: src/compiler/transformers/es2021.ts:32-38 — <c>transformSourceFile</c>.</summary>
    /// <remarks>PORT-NOTE: <c>double</c>: the reference chains this through <c>chainBundle</c>; this member is the
    /// <c>transformSourceFile</c> arm it wraps, and <see cref="TransformSourceFileOrBundle"/> is the chain's face.</remarks>
    private SourceFile TransformSourceFile(SourceFile node)
    {
        if (node.IsDeclarationFile)
        {
            return node;
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/es2021.ts:30 — the returned
    /// <c>chainBundle(context, transformSourceFile)</c>.</summary>
    public object? TransformSourceFileOrBundle(object? node) => _transformSourceFile((Node)node!);

    /// <summary>ported from: src/compiler/transformers/es2021.ts:40-48 — <c>visitor</c>.</summary>
    /// <remarks>PORT-NOTE: TS reads <c>node.transformFlags</c>, which the model states as
    /// <see cref="INode.TransformFlags"/>'s underlying <see cref="TransformFlags"/>.</remarks>
    public object? Visitor(Node node)
    {
        if ((node.TransformFlags & TransformFlags.ContainsES2021) == 0)
        {
            return node;
        }
        if (Utilities.IsLogicalOrCoalescingAssignmentExpression(node))
        {
            return TransformLogicalAssignment((BinaryExpression)node);
        }
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/es2021.ts:50-105 — <c>transformLogicalAssignment</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE (<c>left</c>/<c>assignmentTarget</c>): the reference's <c>left</c> is a
    /// <c>LeftHandSideExpression</c> (the guard <c>isLeftHandSideExpression</c> refines it) while
    /// <c>assignmentTarget</c> widens to <c>Expression</c> once the access arm runs; the port states both as
    /// <see cref="Expression"/>, which is the wider of the two and the type every factory call below accepts.
    /// </para>
    /// <para>
    /// PORT-NOTE (<c>isPropertyAccessExpression(left)</c>): the reference's guard narrows the union in place; the port
    /// states the pattern test <c>left is PropertyAccessExpression</c> once, before either arm reassigns <c>left</c>,
    /// so the <c>else</c> arm's <c>left.argumentExpression</c> reads the same element access the reference reads.
    /// </para>
    /// </remarks>
    private Expression TransformLogicalAssignment(BinaryExpression binaryExpression)
    {
        var operatorToken = binaryExpression.OperatorToken!;
        var nonAssignmentOperator = TransformationUtilities.GetNonAssignmentOperatorForCompoundAssignment(operatorToken.Kind);
        Expression left = Utilities.SkipParentheses(
            Utilities.VisitNode(binaryExpression.Left, Visitor, Utilities.IsLeftHandSideExpression)!);
        var assignmentTarget = left;
        var right = Utilities.SkipParentheses(Utilities.VisitNode(binaryExpression.Right, Visitor, Utilities.IsExpression)!);

        if (Utilities.IsAccessExpression(left))
        {
            var accessExpression = left is PropertyAccessExpression propertyAccess
                ? propertyAccess.Expression!
                : ((ElementAccessExpression)left).Expression!;
            var propertyAccessTargetSimpleCopiable = TransformationUtilities.IsSimpleCopiableExpression(accessExpression);
            Expression propertyAccessTarget = propertyAccessTargetSimpleCopiable
                ? accessExpression
                : _factory.CreateTempVariable(_context.HoistVariableDeclaration, null);
            Expression propertyAccessTargetAssignment = propertyAccessTargetSimpleCopiable
                ? accessExpression
                : _factory.CreateAssignment(propertyAccessTarget, accessExpression);

            if (left is PropertyAccessExpression propertyAccessExpression)
            {
                assignmentTarget = _factory.CreatePropertyAccessExpression(
                    propertyAccessTarget,
                    propertyAccessExpression.Name);
                left = _factory.CreatePropertyAccessExpression(
                    propertyAccessTargetAssignment,
                    propertyAccessExpression.Name);
            }
            else
            {
                var elementAccessExpression = (ElementAccessExpression)left;
                var elementAccessArgumentSimpleCopiable = TransformationUtilities.IsSimpleCopiableExpression(
                    elementAccessExpression.ArgumentExpression!);
                var elementAccessArgument = elementAccessArgumentSimpleCopiable
                    ? elementAccessExpression.ArgumentExpression
                    : _factory.CreateTempVariable(_context.HoistVariableDeclaration, null);

                assignmentTarget = _factory.CreateElementAccessExpression(
                    propertyAccessTarget,
                    elementAccessArgument);
                left = _factory.CreateElementAccessExpression(
                    propertyAccessTargetAssignment,
                    elementAccessArgumentSimpleCopiable
                        ? elementAccessExpression.ArgumentExpression
                        : _factory.CreateAssignment(
                            elementAccessArgument,
                            elementAccessExpression.ArgumentExpression));
            }
        }

        return _factory.CreateBinaryExpression(
            left,
            nonAssignmentOperator,
            _factory.CreateParenthesizedExpression(
                _factory.CreateAssignment(
                    assignmentTarget,
                    right)));
    }
}
