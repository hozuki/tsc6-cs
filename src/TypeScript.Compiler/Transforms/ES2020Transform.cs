#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/es2020.ts:1-268 — the whole ES2020 downlevel transform
/// <summary>
/// ported from: src/compiler/transformers/es2020.ts:42-267 — <c>transformES2020</c>: the transformer that rewrites
/// ES2020's nullish coalescing (<c>??</c>) and optional chaining (<c>?.</c>, <c>?.[]</c>, <c>?.()</c>, and
/// <c>delete</c> of one) into the conditional/sequence shape the ES2019 emitter prints.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (home, §15.15): the reference exports one function, <c>transformES2020</c>, from
/// <c>transformers/es2020.ts</c>; its body is a table of closures over the factory's locals, so the port states the
/// factory as <see cref="ES2020Transform.TransformES2020"/> and the closures as the members of
/// <see cref="ES2020Transformer"/> — the same "closures → classes" treatment <c>ES2015Transform</c> records.
/// </para>
/// <para>
/// PORT-NOTE (the transformer face): TS's <c>Transformer&lt;SourceFile | Bundle&gt;</c> is
/// <c>(node: SourceFile | Bundle) =&gt; SourceFile | Bundle</c>; the port's transformer list spells its slots
/// <c>Func&lt;object?, object?&gt;</c>, which is the erased shape the returned <c>chainBundle</c> call is stored as.
/// </para>
/// <para>
/// PORT-NOTE (the chain union): TS's <c>OptionalChain</c> is
/// <c>PropertyAccessChain | ElementAccessChain | CallChain | NonNullChain</c> and its <c>chain.questionDotToken</c>/
/// <c>chain.expression</c> reads rely on all four declaring those members (the <c>NonNullChain</c> arm through
/// <c>NonNullExpression.expression</c>). The model declares <c>QuestionDotToken</c> and <c>Expression</c> on each chain
/// class rather than on a shared base, so both reads are stated per kind by <see cref="ChainQuestionDotToken"/> and
/// <see cref="ChainExpression"/> — the treatment <c>Utilities.IsSuperProperty</c>'s PORT-NOTE already records for the
/// same union (§12.5).
/// </para>
/// </remarks>
internal static class ES2020Transform
{
    /// <summary>ported from: src/compiler/transformers/es2020.ts:42-48 — <c>transformES2020</c>.</summary>
    /// <remarks>PORT-NOTE: the returned member is the reference's own <c>chainBundle(context, transformSourceFile)</c>
    /// call, staying a method group so no delegate is allocated per call beyond the one
    /// <see cref="TransformationUtilities.ChainBundle"/> stores.</remarks>
    public static Func<object?, object?> TransformES2020(ITransformerContext context)
        => new ES2020Transformer(context).TransformSourceFileOrBundle;
}

// ported from: src/compiler/transformers/es2020.ts:42-267 — the `transformES2020` factory's closures
/// <summary>
/// ported from: src/compiler/transformers/es2020.ts:42-267 — the closures <c>transformES2020</c> builds: its
/// <c>transformSourceFile</c>, <c>visitor</c>, the optional-chain rewriters (<c>flattenChain</c>,
/// <c>visitNonOptional*Expression</c>, <c>visitOptionalExpression</c>) and the two leaf rewriters
/// (<c>transformNullishCoalescingExpression</c>, <c>visitDeleteExpression</c>).
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (<c>factory</c> and <c>hoistVariableDeclaration</c>): the reference destructures both out of the context;
/// the port keeps the context and reads the same members through it (their bodies are
/// <c>TransformationContext&lt;T&gt;</c>'s, Transformer.cs), so the sequence is the same.
/// </para>
/// <para>
/// PORT-NOTE (<c>AccessExpression</c>): TS's parameter type is the union
/// <c>PropertyAccessExpression | ElementAccessExpression</c>; the model carries both through
/// <see cref="MemberExpression"/>, which is the port's spelling for it (the same base
/// <c>TaggedTemplate.cs</c> uses for the <c>CallExpression | TaggedTemplateExpression</c> union), and the factory
/// updates below cast to the kind the same way the reference's narrowed guards do.
/// </para>
/// <para>
/// PORT-NOTE (<c>isNonNullChain</c>): the reference's <c>Debug.assertNotNode(chain, isNonNullChain)</c> comes from
/// <c>utilitiesPublic.ts</c>; the port's <c>Utilities</c> carries the three sibling predicates
/// (<c>IsPropertyAccessChain</c>/<c>IsElementAccessChain</c>/<c>IsCallChain</c>) but not this one, so the port states it
/// privately below and the report names it as the cross-file need (its home is <c>Utilities</c>).
/// </para>
/// </remarks>
internal sealed class ES2020Transformer
{
    private readonly ITransformerContext _context;
    private readonly NodeFactory _factory;

    /// <summary>ported from: src/compiler/transformers/es2020.ts:42-48 — the factory's prologue.</summary>
    private readonly Func<Node, Node> _transformSourceFile;

    /// <summary>ported from: src/compiler/transformers/es2020.ts:42-48 — the factory's prologue.</summary>
    public ES2020Transformer(ITransformerContext context)
    {
        _context = context;
        _factory = context.Factory;
        _transformSourceFile = TransformationUtilities.ChainBundle(context, TransformSourceFile);
    }

    /// <summary>ported from: src/compiler/transformers/es2020.ts:50-56 — <c>transformSourceFile</c>.</summary>
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

    /// <summary>ported from: src/compiler/transformers/es2020.ts:48 — the returned
    /// <c>chainBundle(context, transformSourceFile)</c>.</summary>
    public object? TransformSourceFileOrBundle(object? node) => _transformSourceFile((Node)node!);

    /// <summary>ported from: src/compiler/transformers/es2020.ts:58-86 — <c>visitor</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>Debug.assertNotNode(updated, isSyntheticReference)</c> is
    /// <see cref="Debug.AssertNotNode"/>, which the port's default assertion level leaves compiled out exactly as the
    /// shipped reference build does (Debug.cs records the leveling convention).</remarks>
    public object? Visitor(Node node)
    {
        if ((node.TransformFlags & TransformFlags.ContainsES2020) == 0)
        {
            return node;
        }
        switch (node.Kind)
        {
            case SyntaxKind.CallExpression:
            {
                var updated = VisitNonOptionalCallExpression((CallExpression)node, /*captureThisArg*/ false);
                Debug.AssertNotNode(updated, NodeTests.IsSyntheticReference);
                return updated;
            }
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ElementAccessExpression:
                if (Utilities.IsOptionalChain(node))
                {
                    var updated = VisitOptionalExpression(node, /*captureThisArg*/ false, /*isDelete*/ false);
                    Debug.AssertNotNode(updated, NodeTests.IsSyntheticReference);
                    return updated;
                }
                return Utilities.VisitEachChildWorker(node, Visitor, _context);
            case SyntaxKind.BinaryExpression:
                if (((BinaryExpression)node).OperatorToken!.Kind == SyntaxKind.QuestionQuestionToken)
                {
                    return TransformNullishCoalescingExpression((BinaryExpression)node);
                }
                return Utilities.VisitEachChildWorker(node, Visitor, _context);
            case SyntaxKind.DeleteExpression:
                return VisitDeleteExpression((DeleteExpression)node);
            default:
                return Utilities.VisitEachChildWorker(node, Visitor, _context);
        }
    }

    /// <summary>ported from: src/compiler/transformers/es2020.ts:88-97 — <c>flattenChain</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE (the answer): TS returns the object literal <c>{ expression, chain }</c>; the port states the same two
    /// members as a tuple, which is what the one destructuring caller reads.
    /// </para>
    /// <para>
    /// PORT-NOTE (<c>cast(..., isOptionalChain)</c>): TS's <c>cast</c> fails with "Invalid cast" when the value is not
    /// an optional chain, which is the failure the reference produces for a tagged template in the middle of a chain
    /// (measured: <c>a?.b`t`.c</c> reports <c>Invalid cast … did not pass the test 'isOptionalChain'</c>). The port
    /// states the same checked cast with <see cref="Core.Cast{TOut, TIn}"/>.
    /// </para>
    /// <para>
    /// PORT-NOTE (<c>links</c>): <c>[chain]</c> and <c>unshift</c> are a <see cref="List{T}"/> with an O(n) head insert,
    /// exactly as the reference's array does for these short lists.
    /// </para>
    /// </remarks>
    private (Expression Expression, List<Node> Chain) FlattenChain(Node chain)
    {
        Debug.AssertNotNode(chain, Utilities.IsNonNullChain);
        var links = new List<Node> { chain };
        while (ChainQuestionDotToken(chain) is null && !NodeTests.IsTaggedTemplateExpression(chain))
        {
            chain = Core.Cast<Node, Node>(Utilities.SkipPartiallyEmittedExpressions(ChainExpression(chain)), Utilities.IsOptionalChain);
            Debug.AssertNotNode(chain, Utilities.IsNonNullChain);
            links.Insert(0, chain);
        }
        return (ChainExpression(chain), links);
    }

    /// <summary>ported from: src/compiler/types.ts:2967 — the <c>expression</c> member of the <c>AccessExpression</c>
    /// union.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads <c>node.expression</c> off the union <c>PropertyAccessExpression | ElementAccessExpression</c>,
    /// which both declare; the model declares it on each class and its nearest common base
    /// (<see cref="MemberExpression"/>) does not carry it, so the port states the read per kind — the same treatment
    /// <c>Utilities.IsSuperProperty</c>'s PORT-NOTE records for the sibling union. The default arm is the reference's
    /// unreachable one: <see cref="VisitNonOptionalPropertyOrElementAccessExpression"/> is entered only for those two
    /// kinds.
    /// </remarks>
    private static LeftHandSideExpression AccessExpression(Node node) => node.Kind switch
    {
        SyntaxKind.PropertyAccessExpression => ((PropertyAccessExpression)node).Expression!,
        SyntaxKind.ElementAccessExpression => ((ElementAccessExpression)node).Expression!,
        _ => Debug.Fail<LeftHandSideExpression>($"Unexpected node kind {Debug.FormatSyntaxKind(node.Kind)}."),
    };

    /// <summary>ported from: src/compiler/transformers/es2020.ts:99-107 — <c>visitNonOptionalParenthesizedExpression</c>.</summary>
    /// <remarks>PORT-NOTE (<c>expression.expression</c>/<c>expression.thisArg</c>): the reference reads both members
    /// straight off the guard's narrowed <c>SyntheticReferenceExpression</c>; the port's predicate answers <c>bool</c>,
    /// so the same two reads take the cast the guard would have narrowed (§12.5).</remarks>
    private Expression VisitNonOptionalParenthesizedExpression(ParenthesizedExpression node, bool captureThisArg, bool isDelete)
    {
        var expression = VisitNonOptionalExpression(node.Expression!, captureThisArg, isDelete);
        if (NodeTests.IsSyntheticReference(expression))
        {
            // `(a.b)` -> { expression `((_a = a).b)`, thisArg: `_a` }
            // `(a[b])` -> { expression `((_a = a)[b])`, thisArg: `_a` }
            var syntheticReference = (SyntheticReferenceExpression)expression;
            return _factory.CreateSyntheticReferenceExpression(
                _factory.UpdateParenthesizedExpression(node, syntheticReference.Expression),
                syntheticReference.ThisArg);
        }
        return _factory.UpdateParenthesizedExpression(node, expression);
    }

    /// <summary>ported from: src/compiler/transformers/es2020.ts:109-133 —
    /// <c>visitNonOptionalPropertyOrElementAccessExpression</c>.</summary>
    private Expression VisitNonOptionalPropertyOrElementAccessExpression(MemberExpression node, bool captureThisArg, bool isDelete)
    {
        if (Utilities.IsOptionalChain(node))
        {
            // If `node` is an optional chain, then it is the outermost chain of an optional expression.
            return VisitOptionalExpression(node, captureThisArg, isDelete);
        }

        Expression expression = Utilities.VisitNode(AccessExpression(node), Visitor, Utilities.IsExpression)!;
        Debug.AssertNotNode(expression, NodeTests.IsSyntheticReference);

        Expression? thisArg = null;
        if (captureThisArg)
        {
            if (!TransformationUtilities.IsSimpleCopiableExpression(expression))
            {
                thisArg = _factory.CreateTempVariable(_context.HoistVariableDeclaration, null);
                expression = _factory.CreateAssignment(thisArg, expression);
            }
            else
            {
                thisArg = expression;
            }
        }

        expression = node.Kind == SyntaxKind.PropertyAccessExpression
            ? _factory.UpdatePropertyAccessExpression(
                (PropertyAccessExpression)node,
                expression,
                Utilities.VisitNode(((PropertyAccessExpression)node).Name, Visitor, NodeTests.IsIdentifier))
            : _factory.UpdateElementAccessExpression(
                (ElementAccessExpression)node,
                expression,
                Utilities.VisitNode(((ElementAccessExpression)node).ArgumentExpression, Visitor, Utilities.IsExpression));
        return thisArg is not null ? _factory.CreateSyntheticReferenceExpression(expression, thisArg) : expression;
    }

    /// <summary>ported from: src/compiler/transformers/es2020.ts:135-150 — <c>visitNonOptionalCallExpression</c>.</summary>
    private Expression VisitNonOptionalCallExpression(CallExpression node, bool captureThisArg)
    {
        if (Utilities.IsOptionalChain(node))
        {
            // If `node` is an optional chain, then it is the outermost chain of an optional expression.
            return VisitOptionalExpression(node, captureThisArg, /*isDelete*/ false);
        }
        if (NodeTests.IsParenthesizedExpression(node.Expression!) && Utilities.IsOptionalChain(Utilities.SkipParentheses(node.Expression!)))
        {
            // capture thisArg for calls of parenthesized optional chains like `(foo?.bar)()`
            var expression = VisitNonOptionalParenthesizedExpression(
                (ParenthesizedExpression)node.Expression!,
                /*captureThisArg*/ true,
                /*isDelete*/ false);
            var args = Utilities.VisitNodes(node.Arguments, Visitor, Utilities.IsExpression);
            if (NodeTests.IsSyntheticReference(expression))
            {
                var syntheticReference = (SyntheticReferenceExpression)expression;
                return Utilities.SetTextRange(
                    _factory.CreateFunctionCallCall(syntheticReference.Expression, syntheticReference.ThisArg, args),
                    node);
            }
            return _factory.UpdateCallExpression(node, expression, /*typeArguments*/ null, args);
        }
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/es2020.ts:152-165 — <c>visitNonOptionalExpression</c>.</summary>
    private Expression VisitNonOptionalExpression(Expression node, bool captureThisArg, bool isDelete)
    {
        switch (node.Kind)
        {
            case SyntaxKind.ParenthesizedExpression:
                return VisitNonOptionalParenthesizedExpression((ParenthesizedExpression)node, captureThisArg, isDelete);
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ElementAccessExpression:
                return VisitNonOptionalPropertyOrElementAccessExpression((MemberExpression)node, captureThisArg, isDelete);
            case SyntaxKind.CallExpression:
                return VisitNonOptionalCallExpression((CallExpression)node, captureThisArg);
            default:
                return Utilities.VisitNode(node, Visitor, Utilities.IsExpression)!;
        }
    }

    /// <summary>ported from: src/compiler/transformers/es2020.ts:166-225 — <c>visitOptionalExpression</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE (<c>left</c>/<c>capturedLeft</c>): the reference reads <c>left.thisArg</c>/<c>left.expression</c> off its
    /// <c>isSyntheticReference</c> guard's narrowed type twice; the port separates the two ternary arms so each is a
    /// single cast, which is the same read (§12.5).
    /// </para>
    /// <para>
    /// PORT-NOTE (<c>segment.kind</c> arms): the port's switch reads the visited <c>segment.name</c>/
    /// <c>segment.argumentExpression</c> through the kind's own class, and the <c>CallExpression</c> arm hoists the
    /// segment once so the two factory calls below read the same node.
    /// </para>
    /// </remarks>
    private Expression VisitOptionalExpression(Node node, bool captureThisArg, bool isDelete)
    {
        var (expression, chain) = FlattenChain(node);
        var left = VisitNonOptionalExpression(
            Utilities.SkipPartiallyEmittedExpressions(expression),
            Utilities.IsCallChain(chain[0]),
            /*isDelete*/ false);
        var leftSyntheticReference = NodeTests.IsSyntheticReference(left) ? (SyntheticReferenceExpression)left : null;
        Expression? leftThisArg = leftSyntheticReference?.ThisArg;
        Expression capturedLeft = leftSyntheticReference?.Expression ?? left;
        var leftExpression = _factory.RestoreOuterExpressions(expression, capturedLeft, OuterExpressionKinds.PartiallyEmittedExpressions);
        if (!TransformationUtilities.IsSimpleCopiableExpression(capturedLeft))
        {
            capturedLeft = _factory.CreateTempVariable(_context.HoistVariableDeclaration, null);
            leftExpression = _factory.CreateAssignment(capturedLeft, leftExpression);
        }
        Expression rightExpression = capturedLeft;
        Expression? thisArg = null;
        for (var i = 0; i < chain.Count; i++)
        {
            var segment = chain[i];
            switch (segment.Kind)
            {
                case SyntaxKind.PropertyAccessExpression:
                case SyntaxKind.ElementAccessExpression:
                    if (i == chain.Count - 1 && captureThisArg)
                    {
                        if (!TransformationUtilities.IsSimpleCopiableExpression(rightExpression))
                        {
                            thisArg = _factory.CreateTempVariable(_context.HoistVariableDeclaration, null);
                            rightExpression = _factory.CreateAssignment(thisArg, rightExpression);
                        }
                        else
                        {
                            thisArg = rightExpression;
                        }
                    }
                    rightExpression = segment.Kind == SyntaxKind.PropertyAccessExpression
                        ? _factory.CreatePropertyAccessExpression(
                            rightExpression,
                            Utilities.VisitNode(((PropertyAccessExpression)segment).Name, Visitor, NodeTests.IsIdentifier))
                        : _factory.CreateElementAccessExpression(
                            rightExpression,
                            Utilities.VisitNode(((ElementAccessExpression)segment).ArgumentExpression, Visitor, Utilities.IsExpression));
                    break;
                case SyntaxKind.CallExpression:
                {
                    var callSegment = (CallExpression)segment;
                    if (i == 0 && leftThisArg is not null)
                    {
                        if (!Utilities.IsGeneratedIdentifier(leftThisArg))
                        {
                            leftThisArg = _factory.CloneNode(leftThisArg);
                            EmitNodes.AddEmitFlags(leftThisArg, EmitFlags.NoComments);
                        }
                        rightExpression = _factory.CreateFunctionCallCall(
                            rightExpression,
                            leftThisArg.Kind == SyntaxKind.SuperKeyword ? _factory.CreateThis() : leftThisArg,
                            Utilities.VisitNodes(callSegment.Arguments, Visitor, Utilities.IsExpression));
                    }
                    else
                    {
                        rightExpression = _factory.CreateCallExpression(
                            rightExpression,
                            /*typeArguments*/ null,
                            Utilities.VisitNodes(callSegment.Arguments, Visitor, Utilities.IsExpression));
                    }
                    break;
                }
            }
            NodeFactory.SetOriginalNode(rightExpression, segment);
        }

        var target = isDelete
            ? _factory.CreateConditionalExpression(
                CreateNotNullCondition(leftExpression, capturedLeft, /*invert*/ true),
                /*questionToken*/ null,
                _factory.CreateTrue(),
                /*colonToken*/ null,
                _factory.CreateDeleteExpression(rightExpression))
            : _factory.CreateConditionalExpression(
                CreateNotNullCondition(leftExpression, capturedLeft, /*invert*/ true),
                /*questionToken*/ null,
                _factory.CreateVoidZero(),
                /*colonToken*/ null,
                rightExpression);
        Utilities.SetTextRange(target, node);
        return thisArg is not null ? _factory.CreateSyntheticReferenceExpression(target, thisArg) : target;
    }

    /// <summary>ported from: src/compiler/transformers/es2020.ts:227-241 — <c>createNotNullCondition</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>invert?: boolean</c> is read for truth, so the port's <c>bool?</c> test is
    /// <c>invert == true</c> — the same reading <c>NodeFactory.UpdateExpression</c>'s callers give their nullable
    /// booleans.</remarks>
    private Expression CreateNotNullCondition(Expression left, Expression right, bool? invert = null)
    {
        return _factory.CreateBinaryExpression(
            _factory.CreateBinaryExpression(
                left,
                _factory.CreateToken(invert == true ? SyntaxKind.EqualsEqualsEqualsToken : SyntaxKind.ExclamationEqualsEqualsToken),
                _factory.CreateNull()),
            _factory.CreateToken(invert == true ? SyntaxKind.BarBarToken : SyntaxKind.AmpersandAmpersandToken),
            _factory.CreateBinaryExpression(
                right,
                _factory.CreateToken(invert == true ? SyntaxKind.EqualsEqualsEqualsToken : SyntaxKind.ExclamationEqualsEqualsToken),
                _factory.CreateVoidZero()));
    }

    /// <summary>ported from: src/compiler/transformers/es2020.ts:243-260 —
    /// <c>transformNullishCoalescingExpression</c>.</summary>
    private Expression TransformNullishCoalescingExpression(BinaryExpression node)
    {
        Expression left = Utilities.VisitNode(node.Left, Visitor, Utilities.IsExpression)!;
        Expression right = left;
        if (!TransformationUtilities.IsSimpleCopiableExpression(left))
        {
            right = _factory.CreateTempVariable(_context.HoistVariableDeclaration, null);
            left = _factory.CreateAssignment(right, left);
        }
        return Utilities.SetTextRange(
            _factory.CreateConditionalExpression(
                CreateNotNullCondition(left, right),
                /*questionToken*/ null,
                right,
                /*colonToken*/ null,
                Utilities.VisitNode(node.Right, Visitor, Utilities.IsExpression)),
            node);
    }

    /// <summary>ported from: src/compiler/transformers/es2020.ts:262-267 — <c>visitDeleteExpression</c>.</summary>
    private Expression VisitDeleteExpression(DeleteExpression node)
    {
        return Utilities.IsOptionalChain(Utilities.SkipParentheses(node.Expression!))
            ? NodeFactory.SetOriginalNode(VisitNonOptionalExpression(node.Expression!, /*captureThisArg*/ false, /*isDelete*/ true), node)
            : _factory.UpdateDeleteExpression(node, Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression));
    }

    /// <summary>ported from: src/compiler/types.ts:2981-3044 — the <c>questionDotToken</c> of the
    /// <c>OptionalChain</c> union, read the way <c>flattenChain</c> reads it.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads <c>chain.questionDotToken</c> off the union, whose four members all declare it; the model
    /// declares it on each chain class, so the port states the read per kind. The
    /// <see cref="TaggedTemplateExpression"/> arm is the same property read the reference performs on the node the
    /// grammar-error <c>?.</c> before a tagged template produces (types.ts:3125) — the loop's <c>cast</c> fails before
    /// that node can become <c>chain</c>, so it is stated for the read, not for a reachable state.
    /// </remarks>
    private static PunctuationToken? ChainQuestionDotToken(Node chain) => chain.Kind switch
    {
        SyntaxKind.PropertyAccessExpression => ((PropertyAccessExpression)chain).QuestionDotToken,
        SyntaxKind.ElementAccessExpression => ((ElementAccessExpression)chain).QuestionDotToken,
        SyntaxKind.CallExpression => ((CallExpression)chain).QuestionDotToken,
        SyntaxKind.TaggedTemplateExpression => ((TaggedTemplateExpression)chain).QuestionDotToken,
        _ => null,
    };

    /// <summary>ported from: src/compiler/types.ts:2982-3170 — the <c>expression</c> of the <c>OptionalChain</c>
    /// union, read the way <c>flattenChain</c> reads it.</summary>
    /// <remarks>
    /// PORT-NOTE (the default arm): the reference reads <c>chain.expression</c>, and its <c>isTaggedTemplateExpression</c>
    /// guard in the loop condition is what keeps a <c>TaggedTemplateExpression</c> — whose interface declares
    /// <c>tag</c>, not <c>expression</c> — out of this read (types.ts:3120-3126); the loop's <c>cast</c> already fails
    /// for such a node, so the reference never reaches the read either. The model cannot spell "a member that is not
    /// there", so the port states the failure that read would produce in the reference — the same
    /// <see cref="Debug.Fail{T}"/> text its <c>cast</c> raises — rather than inventing a value.
    /// </remarks>
    private static Expression ChainExpression(Node chain) => chain.Kind switch
    {
        SyntaxKind.PropertyAccessExpression => ((PropertyAccessExpression)chain).Expression!,
        SyntaxKind.ElementAccessExpression => ((ElementAccessExpression)chain).Expression!,
        SyntaxKind.CallExpression => ((CallExpression)chain).Expression!,
        SyntaxKind.NonNullExpression => ((NonNullExpression)chain).Expression!,
        _ => Debug.Fail<Expression>(
            $"Invalid cast. The supplied value {Debug.FormatSyntaxKind(chain.Kind)} did not pass the test 'isOptionalChain'."),
    };
}
