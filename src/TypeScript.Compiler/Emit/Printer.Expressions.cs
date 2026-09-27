namespace TypeScript.Compiler;

// ported from: src/compiler/emitter.ts:2612-3035 (expressions) and 3877-3993 (JSX) — the printer's expression half.
//
// PORT-NOTE (the shape of these two files): the reference is one closure whose nested functions become methods of this
// class (see Printer.State.cs). Every function is a private member named after the reference's, in the reference's
// order, and the reference's `write*`/`emit*` helpers it calls are PrinterCore's members (see the message on the
// shared surface): `Emit`, `EmitExpression`, `EmitIdentifierName`, `EmitList`/`EmitExpressionList`, the `Write*`
// family, `WriteToken`/`WriteTokenText`/`WriteTokenNode`, `GetPipelinePhase`, `IncreaseIndent`/`DecreaseIndent`/
// `DecreaseIndentIf`, `WriteLinesAndIndent`, `WriteLineOrSpace`, `WriteLineSeparatorsAndIndentBefore/After`,
// `GetLinesBetweenNodes`, `ShouldEmitComments`/`ShouldEmitSourceMaps`, `BeforeEmitNode`/`AfterEmitNode`,
// `GenerateNames`/`GenerateMemberNames`/`GenerateNameIfNeeded`, `PushNameGenerationScope`/`PopNameGenerationScope`,
// `EmitHelpers`, `GetLiteralTextOfNode`, `IsEmptyBlock`, `EmitBodyWithDetachedComments`.
//
// PORT-NOTE (parenthesizer rules): the reference passes the `parenthesizer` object's rules (`(node: Expression) =>
// Expression`) straight into the emit family, whose `parenthesizerRule` slot the port types `Func<Node, Node>`
// (Printer.State.cs's `_currentParenthesizerRule`). C# cannot convert `Func<Expression, Expression>` to
// `Func<Node, Node>` (neither contravariance nor covariance applies), so each rule gets a one-line adapter here,
// named `Rule<ReferenceName>`. They are deliberately private and uniquely named so the parallel slices cannot collide.
internal sealed partial class Printer
{
    //
    // Expressions
    //

    /// <summary>ported from: src/compiler/emitter.ts:2612-2616 — <c>emitArrayLiteralExpression</c>.</summary>
    private void EmitArrayLiteralExpression(ArrayLiteralExpression node)
    {
        var elements = node.Elements;
        var preferNewLine = node.MultiLine == true ? ListFormat.PreferNewLine : ListFormat.None;
        EmitExpressionList(node, elements, ListFormat.ArrayLiteralExpressionElements | preferNewLine, RuleDisallowedComma);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2618-2635 — <c>emitObjectLiteralExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>forEach(node.properties, generateMemberNames)</c> (emitter.ts:2620) is stated with the callback
    /// inline because the port's <c>Core.ForEach</c> requires a <c>Func&lt;T, int, TU?&gt;</c> that answers a non-null
    /// value to stop — the reference's callback answers <c>void</c>.
    /// </remarks>
    private void EmitObjectLiteralExpression(ObjectLiteralExpression node)
    {
        PushNameGenerationScope(node);
        Core.ForEach(node.Properties, (Node property, int _) =>
        {
            GenerateMemberNames(property);
            return (object?)null;
        });

        var indentedFlag = (Utilities.GetEmitFlags(node) & EmitFlags.Indented) != 0;
        if (indentedFlag)
        {
            IncreaseIndent();
        }

        var preferNewLine = node.MultiLine == true ? ListFormat.PreferNewLine : ListFormat.None;
        var allowTrailingComma = _currentSourceFile is not null && !Utilities.IsJsonSourceFile(_currentSourceFile)
            ? ListFormat.AllowTrailingComma
            : ListFormat.None;
        EmitList(node, node.Properties, ListFormat.ObjectLiteralExpressionProperties | allowTrailingComma | preferNewLine);

        if (indentedFlag)
        {
            DecreaseIndent();
        }

        PopNameGenerationScope(node);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2637-2667 — <c>emitPropertyAccessExpression</c>.</summary>
    private void EmitPropertyAccessExpression(PropertyAccessExpression node)
    {
        EmitExpression(node.Expression, RuleLeftSideOfAccess);
        // PORT-NOTE: emitter.ts:2639 — TS's `node.questionDotToken || <the synthesized dot>` is a truthiness test on a
        // nullable token, which the two members below answer exactly like `??`.
        var token = node.QuestionDotToken ?? Utilities.SetTextRangePosEnd(NodeFactory.Factory.CreateToken(SyntaxKind.DotToken), node.Expression!.End, node.Name!.Pos);
        var linesBeforeDot = GetLinesBetweenNodes(node, node.Expression!, token);
        var linesAfterDot = GetLinesBetweenNodes(node, token, node.Name!);

        WriteLinesAndIndent(linesBeforeDot, /*writeSpaceIfNotIndenting*/ false);

        var shouldEmitDotDot = token.Kind != SyntaxKind.QuestionDotToken
            && MayNeedDotDotForPropertyAccess(node.Expression!)
            && !_writer.HasTrailingComment()
            && !_writer.HasTrailingWhitespace();

        if (shouldEmitDotDot)
        {
            WritePunctuation(".");
        }

        if (node.QuestionDotToken is not null)
        {
            Emit(token);
        }
        else
        {
            EmitTokenWithComment(token.Kind, node.Expression!.End, WritePunctuation, node);
        }

        WriteLinesAndIndent(linesAfterDot, /*writeSpaceIfNotIndenting*/ false);
        Emit(node.Name);
        DecreaseIndentIf(linesBeforeDot, linesAfterDot);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2669-2695 — <c>mayNeedDotDotForPropertyAccess</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:2684-2692 — the reference's `a || b` over a nullable token is <c>??</c>-equivalent; and its
    /// `typeof constantValue === "number" ...` is stated over the two numeric shapes the port's <c>object?</c> slot can
    /// hold (an <c>int</c> constant satisfies <c>isFinite(v) &amp;&amp; v &gt;= 0 &amp;&amp; floor(v) === v</c> by
    /// construction, so only the sign is left to test).
    /// </remarks>
    private bool MayNeedDotDotForPropertyAccess(Expression expression)
    {
        expression = Utilities.SkipPartiallyEmittedExpressions(expression);
        if (NodeTests.IsNumericLiteral(expression))
        {
            // check if numeric literal is a decimal literal that was originally written with a dot
            var text = GetLiteralTextOfNode((LiteralExpression)expression, /*sourceFile*/ null, /*neverAsciiEscape*/ true, /*jsxAttributeEscape*/ false);
            // If the number will be printed verbatim and it doesn't already contain a dot or an exponent indicator, add one
            // if the expression doesn't have any comments that will be emitted.
            return (((NumericLiteral)expression).NumericLiteralFlags & TokenFlags.WithSpecifier) == 0
                && !text.Contains(ScannerUtilities.TokenToString(SyntaxKind.DotToken)!)
                && !text.Contains((char)CharacterCodes.E)
                && !text.Contains((char)CharacterCodes.e);
        }

        if (Utilities.IsAccessExpression(expression))
        {
            // check if constant enum value is a non-negative integer
            var constantValue = EmitNodes.GetConstantValue(expression);
            // isFinite handles cases when constantValue is undefined
            return constantValue switch
            {
                double d => double.IsFinite(d) && d >= 0 && Math.Floor(d) == d,
                int i => i >= 0,
                _ => false,
            };
        }

        return false;
    }

    /// <summary>ported from: src/compiler/emitter.ts:2697-2703 — <c>emitElementAccessExpression</c>.</summary>
    private void EmitElementAccessExpression(ElementAccessExpression node)
    {
        EmitExpression(node.Expression, RuleLeftSideOfAccess);
        Emit(node.QuestionDotToken);
        EmitTokenWithComment(SyntaxKind.OpenBracketToken, node.Expression!.End, WritePunctuation, node);
        EmitExpression(node.ArgumentExpression);
        EmitTokenWithComment(SyntaxKind.CloseBracketToken, node.ArgumentExpression!.End, WritePunctuation, node);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2705-2720 — <c>emitCallExpression</c>.</summary>
    private void EmitCallExpression(CallExpression node)
    {
        var indirectCall = GetInternalEmitFlags(node) & InternalEmitFlags.IndirectCall;
        if (indirectCall != 0)
        {
            WritePunctuation("(");
            WriteLiteral("0");
            WritePunctuation(",");
            WriteSpace();
        }

        EmitExpression(node.Expression, RuleLeftSideOfAccess);
        if (indirectCall != 0)
        {
            WritePunctuation(")");
        }

        Emit(node.QuestionDotToken);
        EmitTypeArguments(node, node.TypeArguments);
        EmitExpressionList(node, node.Arguments, ListFormat.CallExpressionArguments, RuleDisallowedComma);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2722-2729 — <c>emitNewExpression</c>.</summary>
    private void EmitNewExpression(NewExpression node)
    {
        EmitTokenWithComment(SyntaxKind.NewKeyword, node.Pos, WriteKeyword, node);
        WriteSpace();
        EmitExpression(node.Expression, RuleExpressionOfNew);
        EmitTypeArguments(node, node.TypeArguments);
        EmitExpressionList(node, node.Arguments, ListFormat.NewExpressionArguments, RuleDisallowedComma);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2731-2744 — <c>emitTaggedTemplateExpression</c>.</summary>
    private void EmitTaggedTemplateExpression(TaggedTemplateExpression node)
    {
        var indirectCall = GetInternalEmitFlags(node) & InternalEmitFlags.IndirectCall;
        if (indirectCall != 0)
        {
            WritePunctuation("(");
            WriteLiteral("0");
            WritePunctuation(",");
            WriteSpace();
        }

        EmitExpression(node.Tag, RuleLeftSideOfAccess);
        if (indirectCall != 0)
        {
            WritePunctuation(")");
        }

        EmitTypeArguments(node, node.TypeArguments);
        WriteSpace();
        EmitExpression(node.Template);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2746-2751 — <c>emitTypeAssertionExpression</c>.</summary>
    private void EmitTypeAssertionExpression(TypeAssertion node)
    {
        WritePunctuation("<");
        Emit(node.Type);
        WritePunctuation(">");
        EmitExpression(node.Expression, RuleOperandOfPrefixUnary);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2753-2760 — <c>emitParenthesizedExpression</c>.</summary>
    private void EmitParenthesizedExpression(ParenthesizedExpression node)
    {
        var openParenPos = EmitTokenWithComment(SyntaxKind.OpenParenToken, node.Pos, WritePunctuation, node);
        var indented = WriteLineSeparatorsAndIndentBefore(node.Expression!, node);
        EmitExpression(node.Expression, /*parenthesizerRule*/ null);
        WriteLineSeparatorsAfter(node.Expression!, node);
        DecreaseIndentIf(indented);
        EmitTokenWithComment(SyntaxKind.CloseParenToken, node.Expression is not null ? node.Expression.End : openParenPos, WritePunctuation, node);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2762-2765 — <c>emitFunctionExpression</c>.</summary>
    private void EmitFunctionExpression(FunctionExpression node)
    {
        GenerateNameIfNeeded(node.Name);
        EmitFunctionDeclarationOrExpression(node);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2767-2770 — <c>emitArrowFunction</c>.</summary>
    private void EmitArrowFunction(ArrowFunction node)
    {
        EmitModifierList(node, node.Modifiers);
        EmitSignatureAndBody(node, EmitArrowFunctionHead, EmitArrowFunctionBody);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2772-2779 — <c>emitArrowFunctionHead</c>.</summary>
    private void EmitArrowFunctionHead(ArrowFunction node)
    {
        EmitTypeParameters(node, node.TypeParameters);
        EmitParametersForArrow(node, node.Parameters);
        EmitTypeAnnotation(node.Type);
        WriteSpace();
        Emit(node.EqualsGreaterThanToken);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2781-2789 — <c>emitArrowFunctionBody</c>.</summary>
    private void EmitArrowFunctionBody(ArrowFunction node)
    {
        if (node.Body is not null && NodeTests.IsBlock(node.Body))
        {
            EmitBlockFunctionBody((Block)node.Body);
        }
        else
        {
            WriteSpace();
            EmitExpression(node.Body, RuleConciseBodyOfArrowFunction);
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:2791-2795 — <c>emitDeleteExpression</c>.</summary>
    private void EmitDeleteExpression(DeleteExpression node)
    {
        EmitTokenWithComment(SyntaxKind.DeleteKeyword, node.Pos, WriteKeyword, node);
        WriteSpace();
        EmitExpression(node.Expression, RuleOperandOfPrefixUnary);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2797-2801 — <c>emitTypeOfExpression</c>.</summary>
    private void EmitTypeOfExpression(TypeOfExpression node)
    {
        EmitTokenWithComment(SyntaxKind.TypeOfKeyword, node.Pos, WriteKeyword, node);
        WriteSpace();
        EmitExpression(node.Expression, RuleOperandOfPrefixUnary);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2803-2807 — <c>emitVoidExpression</c>.</summary>
    private void EmitVoidExpression(VoidExpression node)
    {
        EmitTokenWithComment(SyntaxKind.VoidKeyword, node.Pos, WriteKeyword, node);
        WriteSpace();
        EmitExpression(node.Expression, RuleOperandOfPrefixUnary);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2809-2813 — <c>emitAwaitExpression</c>.</summary>
    private void EmitAwaitExpression(AwaitExpression node)
    {
        EmitTokenWithComment(SyntaxKind.AwaitKeyword, node.Pos, WriteKeyword, node);
        WriteSpace();
        EmitExpression(node.Expression, RuleOperandOfPrefixUnary);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2815-2821 — <c>emitPrefixUnaryExpression</c>.</summary>
    private void EmitPrefixUnaryExpression(PrefixUnaryExpression node)
    {
        WriteTokenText(node.Operator, WriteOperator);
        if (ShouldEmitWhitespaceBeforeOperand(node))
        {
            WriteSpace();
        }

        EmitExpression(node.Operand, RuleOperandOfPrefixUnary);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2823-2841 — <c>shouldEmitWhitespaceBeforeOperand</c>.</summary>
    private static bool ShouldEmitWhitespaceBeforeOperand(PrefixUnaryExpression node)
    {
        // In some cases, we need to emit a space between the operator and the operand. One obvious case
        // is when the operator is an identifier, like delete or typeof. We also need to do this for plus
        // and minus expressions in certain cases. Specifically, consider the following two cases (parens
        // are just for clarity of exposition, and not part of the source code):
        //
        //  (+(+1))
        //  (+(++1))
        //
        // We need to emit a space in both cases. In the first case, the absence of a space will make
        // the resulting expression a prefix increment operation. And in the second, it will make the resulting
        // expression a prefix increment whose operand is a plus expression - (++(+x))
        // The same is true of minus of course.
        var operand = node.Operand;
        return operand is not null
            && operand.Kind == SyntaxKind.PrefixUnaryExpression
            && ((node.Operator == SyntaxKind.PlusToken
                    && ((PrefixUnaryExpression)operand).Operator is SyntaxKind.PlusToken or SyntaxKind.PlusPlusToken)
                || (node.Operator == SyntaxKind.MinusToken
                    && ((PrefixUnaryExpression)operand).Operator is SyntaxKind.MinusToken or SyntaxKind.MinusMinusToken));
    }

    /// <summary>ported from: src/compiler/emitter.ts:2843-2846 — <c>emitPostfixUnaryExpression</c>.</summary>
    private void EmitPostfixUnaryExpression(PostfixUnaryExpression node)
    {
        EmitExpression(node.Operand, RuleOperandOfPostfixUnary);
        WriteTokenText(node.Operator, WriteOperator);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2848-2966 — <c>createEmitBinaryExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:2849-2858 — TS declares its <c>WorkArea</c> interface (and the five callbacks) inside
    /// <c>createEmitBinaryExpression</c>; the port states them as nested members of this class, and the arrays the
    /// reference grows by index become <c>List&lt;T&gt;</c> with <see cref="WorkArea.SetSlot"/>. The trampoline's user
    /// state slot is `WorkArea?` and its result slot is <c>object?</c> because the state machine requires a
    /// <c>TResult</c> while the reference's <c>onExit</c> answers nothing (TS's <c>void</c>) — the same treatment
    /// <c>Binder.FlowExpressions.cs</c> gives its own use of the trampoline. The reference's <c>"left" | "right"</c>
    /// string argument of <c>maybeEmitExpression</c> is a <c>bool</c> here.
    /// </remarks>
    private Action<BinaryExpression> CreateEmitBinaryExpression()
    {
        var trampoline = FactoryUtilities.CreateBinaryExpressionTrampoline<WorkArea, object?>(
            onEnter: OnEnterBinaryExpression,
            onLeft: OnLeftBinaryExpression,
            onOperator: OnOperatorBinaryExpression,
            onRight: OnRightBinaryExpression,
            onExit: OnExitBinaryExpression,
            foldState: null);

        // PORT-NOTE: emitter.ts:2860 — the reference hands the trampoline's `(node) => TResult` straight to the
        // `emitBinaryExpression` slot; that slot is an `Action<BinaryExpression>` (Printer.State.cs), so the result the
        // reference discards is discarded here too.
        return node => trampoline(node);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2862-2892 — the <c>onEnter</c> of <c>createEmitBinaryExpression</c>.</summary>
    private WorkArea OnEnterBinaryExpression(BinaryExpression node, WorkArea? state)
    {
        if (state is not null)
        {
            state.StackIndex++;
            WorkArea.SetSlot(state.PreserveSourceNewlinesStack, state.StackIndex, _preserveSourceNewlines);
            WorkArea.SetSlot(state.ContainerPosStack, state.StackIndex, _containerPos);
            WorkArea.SetSlot(state.ContainerEndStack, state.StackIndex, _containerEnd);
            WorkArea.SetSlot(state.DeclarationListContainerEndStack, state.StackIndex, _declarationListContainerEnd);
            var emitComments = ShouldEmitComments(node);
            var emitSourceMaps = ShouldEmitSourceMaps(node);
            WorkArea.SetSlot(state.ShouldEmitCommentsStack, state.StackIndex, emitComments);
            WorkArea.SetSlot(state.ShouldEmitSourceMapsStack, state.StackIndex, emitSourceMaps);
            _handlers.OnBeforeEmitNode?.Invoke(node);
            if (emitComments) EmitCommentsBeforeNode(node);
            if (emitSourceMaps) EmitSourceMapsBeforeNode(node);
            BeforeEmitNode(node);
        }
        else
        {
            state = new WorkArea
            {
                StackIndex = 0,
                PreserveSourceNewlinesStack = [null],
                ContainerPosStack = [-1],
                ContainerEndStack = [-1],
                DeclarationListContainerEndStack = [-1],
                ShouldEmitCommentsStack = [false],
                ShouldEmitSourceMapsStack = [false],
            };
        }

        return state;
    }

    /// <summary>ported from: src/compiler/emitter.ts:2894-2896 — the <c>onLeft</c> of <c>createEmitBinaryExpression</c>.</summary>
    private BinaryExpression? OnLeftBinaryExpression(Expression next, WorkArea _workArea, BinaryExpression parent)
        => MaybeEmitExpression(next, parent, /*isLeft*/ true);

    /// <summary>ported from: src/compiler/emitter.ts:2898-2910 — the <c>onOperator</c> of <c>createEmitBinaryExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:2899 — TS's `const isCommaOperator = operatorToken.kind !== SyntaxKind.CommaToken;` really
    /// does name the negation (it is the "write space if not indenting" flag the call below wants), so the name is kept.
    /// </remarks>
    private void OnOperatorBinaryExpression(Token operatorToken, WorkArea _state, BinaryExpression node)
    {
        var isCommaOperator = operatorToken.Kind != SyntaxKind.CommaToken;
        var linesBeforeOperator = GetLinesBetweenNodes(node, node.Left!, operatorToken);
        var linesAfterOperator = GetLinesBetweenNodes(node, operatorToken, node.Right!);
        WriteLinesAndIndent(linesBeforeOperator, isCommaOperator);
        EmitLeadingCommentsOfPosition(operatorToken.Pos);
        WriteTokenNode(operatorToken, operatorToken.Kind == SyntaxKind.InKeyword ? WriteKeyword : WriteOperator);
        EmitTrailingCommentsOfPosition(operatorToken.End, /*prefixSpace*/ true); // Binary operators should have a space before the comment starts
        WriteLinesAndIndent(linesAfterOperator, /*writeSpaceIfNotIndenting*/ true);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2912-2914 — the <c>onRight</c> of <c>createEmitBinaryExpression</c>.</summary>
    private BinaryExpression? OnRightBinaryExpression(Expression next, WorkArea _workArea, BinaryExpression parent)
        => MaybeEmitExpression(next, parent, /*isLeft*/ false);

    /// <summary>ported from: src/compiler/emitter.ts:2916-2935 — the <c>onExit</c> of <c>createEmitBinaryExpression</c>.</summary>
    private object? OnExitBinaryExpression(BinaryExpression node, WorkArea state)
    {
        var linesBeforeOperator = GetLinesBetweenNodes(node, node.Left!, node.OperatorToken!);
        var linesAfterOperator = GetLinesBetweenNodes(node, node.OperatorToken!, node.Right!);
        DecreaseIndentIf(linesBeforeOperator, linesAfterOperator);
        if (state.StackIndex > 0)
        {
            var savedPreserveSourceNewlines = state.PreserveSourceNewlinesStack[state.StackIndex];
            var savedContainerPos = state.ContainerPosStack[state.StackIndex];
            var savedContainerEnd = state.ContainerEndStack[state.StackIndex];
            var savedDeclarationListContainerEnd = state.DeclarationListContainerEndStack[state.StackIndex];
            var shouldEmitComments = state.ShouldEmitCommentsStack[state.StackIndex];
            var shouldEmitSourceMaps = state.ShouldEmitSourceMapsStack[state.StackIndex];
            AfterEmitNode(savedPreserveSourceNewlines);
            if (shouldEmitSourceMaps) EmitSourceMapsAfterNode(node);
            if (shouldEmitComments) EmitCommentsAfterNode(node, savedContainerPos, savedContainerEnd, savedDeclarationListContainerEnd);
            _handlers.OnAfterEmitNode?.Invoke(node);
            state.StackIndex--;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/emitter.ts:2937-2966 — the <c>maybeEmitExpression</c> of <c>createEmitBinaryExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:2962-2967 — TS compares the pipeline function by identity (`pipelinePhase ===
    /// pipelineEmitWithSubstitution`); C# cannot name a method group as the right operand of `==`, so the comparison
    /// states the delegate type explicitly. `side === "left"` is the <paramref name="isLeft"/> flag.
    /// </remarks>
    private BinaryExpression? MaybeEmitExpression(Expression next, BinaryExpression parent, bool isLeft)
    {
        var parenthesizerRule = isLeft
            ? _parenthesizer.GetParenthesizeLeftSideOfBinaryForOperator(parent.OperatorToken!.Kind)
            : _parenthesizer.GetParenthesizeRightSideOfBinaryForOperator(parent.OperatorToken!.Kind);

        var pipelinePhase = GetPipelinePhase(PipelinePhase.Notification, EmitHint.Expression, next);
        if (pipelinePhase == (Action<EmitHint, Node>)PipelineEmitWithSubstitution)
        {
            Debug.AssertIsDefined(_lastSubstitution);
            next = (Expression)parenthesizerRule!(Core.Cast<Expression, Node>(_lastSubstitution, Utilities.IsExpression))!;
            pipelinePhase = GetNextPipelinePhase(PipelinePhase.Substitution, EmitHint.Expression, next);
            _lastSubstitution = null;
        }

        if (pipelinePhase == (Action<EmitHint, Node>)PipelineEmitWithComments
            || pipelinePhase == (Action<EmitHint, Node>)PipelineEmitWithSourceMaps
            || pipelinePhase == (Action<EmitHint, Node>)PipelineEmitWithHint)
        {
            if (NodeTests.IsBinaryExpression(next))
            {
                return (BinaryExpression)next;
            }
        }

        _currentParenthesizerRule = AsParenthesizerRule(parenthesizerRule);
        pipelinePhase(EmitHint.Expression, next);
        return null;
    }

    /// <summary>ported from: src/compiler/emitter.ts:2968-2993 — <c>emitConditionalExpression</c>.</summary>
    private void EmitConditionalExpression(ConditionalExpression node)
    {
        var linesBeforeQuestion = GetLinesBetweenNodes(node, node.Condition!, node.QuestionToken!);
        var linesAfterQuestion = GetLinesBetweenNodes(node, node.QuestionToken!, node.WhenTrue!);
        var linesBeforeColon = GetLinesBetweenNodes(node, node.WhenTrue!, node.ColonToken!);
        var linesAfterColon = GetLinesBetweenNodes(node, node.ColonToken!, node.WhenFalse!);

        EmitExpression(node.Condition, RuleConditionOfConditionalExpression);
        WriteLinesAndIndent(linesBeforeQuestion, /*writeSpaceIfNotIndenting*/ true);
        Emit(node.QuestionToken);
        WriteLinesAndIndent(linesAfterQuestion, /*writeSpaceIfNotIndenting*/ true);
        EmitExpression(node.WhenTrue, RuleBranchOfConditionalExpression);
        DecreaseIndentIf(linesBeforeQuestion, linesAfterQuestion);

        WriteLinesAndIndent(linesBeforeColon, /*writeSpaceIfNotIndenting*/ true);
        Emit(node.ColonToken);
        WriteLinesAndIndent(linesAfterColon, /*writeSpaceIfNotIndenting*/ true);
        EmitExpression(node.WhenFalse, RuleBranchOfConditionalExpression);
        DecreaseIndentIf(linesBeforeColon, linesAfterColon);
    }

    /// <summary>ported from: src/compiler/emitter.ts:2995-2998 — <c>emitTemplateExpression</c>.</summary>
    private void EmitTemplateExpression(TemplateExpression node)
    {
        Emit(node.Head);
        EmitList(node, node.TemplateSpans, ListFormat.TemplateExpressionSpans);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3000-3004 — <c>emitYieldExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:3003 — TS's `node.expression &amp;&amp; parenthesizeExpressionForNoAsi(...)` short-circuits
    /// on an absent expression; the port states the same two cases with a pattern test.
    /// </remarks>
    private void EmitYieldExpression(YieldExpression node)
    {
        EmitTokenWithComment(SyntaxKind.YieldKeyword, node.Pos, WriteKeyword, node);
        Emit(node.AsteriskToken);
        EmitExpressionWithLeadingSpace(node.Expression is { } expression ? ParenthesizeExpressionForNoAsi(expression) : null, RuleNoAsiAndDisallowedComma);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3006-3009 — <c>emitSpreadElement</c>.</summary>
    private void EmitSpreadElement(SpreadElement node)
    {
        EmitTokenWithComment(SyntaxKind.DotDotDotToken, node.Pos, WritePunctuation, node);
        EmitExpression(node.Expression, RuleDisallowedComma);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3011-3014 — <c>emitClassExpression</c>.</summary>
    private void EmitClassExpression(ClassExpression node)
    {
        GenerateNameIfNeeded(node.Name);
        EmitClassDeclarationOrExpression(node);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3016-3019 — <c>emitExpressionWithTypeArguments</c>.</summary>
    private void EmitExpressionWithTypeArguments(ExpressionWithTypeArguments node)
    {
        EmitExpression(node.Expression, RuleLeftSideOfAccess);
        EmitTypeArguments(node, node.TypeArguments);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3021-3029 — <c>emitAsExpression</c>.</summary>
    private void EmitAsExpression(AsExpression node)
    {
        EmitExpression(node.Expression, /*parenthesizerRule*/ null);
        if (node.Type is not null)
        {
            WriteSpace();
            WriteKeyword("as");
            WriteSpace();
            Emit(node.Type);
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:3031-3034 — <c>emitNonNullExpression</c>.</summary>
    private void EmitNonNullExpression(NonNullExpression node)
    {
        EmitExpression(node.Expression, RuleLeftSideOfAccess);
        WriteOperator("!");
    }

    /// <summary>ported from: src/compiler/emitter.ts:3036-3044 — <c>emitSatisfiesExpression</c>.</summary>
    private void EmitSatisfiesExpression(SatisfiesExpression node)
    {
        EmitExpression(node.Expression, /*parenthesizerRule*/ null);
        if (node.Type is not null)
        {
            WriteSpace();
            WriteKeyword("satisfies");
            WriteSpace();
            Emit(node.Type);
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:3046-3050 — <c>emitMetaProperty</c>.</summary>
    private void EmitMetaProperty(MetaProperty node)
    {
        WriteToken(node.KeywordToken, node.Pos, WritePunctuation);
        WritePunctuation(".");
        Emit(node.Name);
    }

    //
    // Misc
    //

    /// <summary>ported from: src/compiler/emitter.ts:3056-3059 — <c>emitTemplateSpan</c>.</summary>
    private void EmitTemplateSpan(TemplateSpan node)
    {
        EmitExpression(node.Expression);
        Emit(node.Literal);
    }

    //
    // JSX
    //

    /// <summary>ported from: src/compiler/emitter.ts:3877-3881 — <c>emitJsxElement</c>.</summary>
    private void EmitJsxElement(JsxElement node)
    {
        Emit(node.OpeningElement);
        EmitList(node, node.Children, ListFormat.JsxElementOrFragmentChildren);
        Emit(node.ClosingElement);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3883-3889 — <c>emitJsxSelfClosingElement</c>.</summary>
    private void EmitJsxSelfClosingElement(JsxSelfClosingElement node)
    {
        WritePunctuation("<");
        EmitJsxTagName(node.TagName!);
        EmitTypeArguments(node, node.TypeArguments);
        WriteSpace();
        Emit(node.Attributes);
        WritePunctuation("/>");
    }

    /// <summary>ported from: src/compiler/emitter.ts:3891-3895 — <c>emitJsxFragment</c>.</summary>
    private void EmitJsxFragment(JsxFragment node)
    {
        Emit(node.OpeningFragment);
        EmitList(node, node.Children, ListFormat.JsxElementOrFragmentChildren);
        Emit(node.ClosingFragment);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3897-3914 — <c>emitJsxOpeningElementOrFragment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:3901-3909 — TS's <c>JsxOpeningElement | JsxOpeningFragment</c> has no C# name, so the
    /// parameter is the upper bound and the element-only body is the <c>is JsxOpeningElement</c> branch.
    /// </remarks>
    private void EmitJsxOpeningElementOrFragment(Node node)
    {
        WritePunctuation("<");

        if (node is JsxOpeningElement openingElement)
        {
            var indented = WriteLineSeparatorsAndIndentBefore(openingElement.TagName!, openingElement);
            EmitJsxTagName(openingElement.TagName!);
            EmitTypeArguments(openingElement, openingElement.TypeArguments);
            if (openingElement.Attributes?.Properties is { Count: > 0 })
            {
                WriteSpace();
            }

            Emit(openingElement.Attributes);
            WriteLineSeparatorsAfter(openingElement.Attributes!, openingElement);
            DecreaseIndentIf(indented);
        }

        WritePunctuation(">");
    }

    /// <summary>ported from: src/compiler/emitter.ts:3916-3918 — <c>emitJsxText</c>.</summary>
    private void EmitJsxText(JsxText node)
    {
        _writer.WriteLiteral(node.Text);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3920-3926 — <c>emitJsxClosingElementOrFragment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:3922-3924 — the union's element-only body is the <c>is JsxClosingElement</c> branch.
    /// </remarks>
    private void EmitJsxClosingElementOrFragment(Node node)
    {
        WritePunctuation("</");
        if (node is JsxClosingElement closingElement)
        {
            EmitJsxTagName(closingElement.TagName!);
        }

        WritePunctuation(">");
    }

    /// <summary>ported from: src/compiler/emitter.ts:3928-3930 — <c>emitJsxAttributes</c>.</summary>
    private void EmitJsxAttributes(JsxAttributes node)
    {
        EmitList(node, node.Properties, ListFormat.JsxElementAttributes);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3932-3935 — <c>emitJsxAttribute</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:3934 — the reference's <c>node.initializer</c> is the <c>JsxAttributeValue</c> union
    /// (<c>StringLiteral | JsxExpression | JsxElement | JsxSelfClosingElement | JsxFragment</c>), whose port type is its
    /// upper bound <c>Expression</c> (see PrinterCore's <c>EmitJsxAttributeValue(Node)</c>; the callback converts
    /// through <c>Action&lt;in T&gt;</c>'s contravariance).
    /// </remarks>
    private void EmitJsxAttribute(JsxAttribute node)
    {
        Emit(node.Name);
        EmitNodeWithPrefix("=", WritePunctuation, node.Initializer, EmitJsxAttributeValue);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3937-3941 — <c>emitJsxSpreadAttribute</c>.</summary>
    private void EmitJsxSpreadAttribute(JsxSpreadAttribute node)
    {
        WritePunctuation("{...");
        EmitExpression(node.Expression);
        WritePunctuation("}");
    }

    /// <summary>ported from: src/compiler/emitter.ts:3943-3947 — <c>hasTrailingCommentsAtPosition</c>.</summary>
    private bool HasTrailingCommentsAtPosition(int pos)
    {
        var result = false;
        ScannerUtilities.ForEachTrailingCommentRange(_currentSourceFile?.Text ?? "", pos + 1, (int _, int _, SyntaxKind _, bool _) => result = true);
        return result;
    }

    /// <summary>ported from: src/compiler/emitter.ts:3949-3953 — <c>hasLeadingCommentsAtPosition</c>.</summary>
    private bool HasLeadingCommentsAtPosition(int pos)
    {
        var result = false;
        ScannerUtilities.ForEachLeadingCommentRange(_currentSourceFile?.Text ?? "", pos + 1, (int _, int _, SyntaxKind _, bool _) => result = true);
        return result;
    }

    /// <summary>ported from: src/compiler/emitter.ts:3955-3957 — <c>hasCommentsAtPosition</c>.</summary>
    private bool HasCommentsAtPosition(int pos)
        => HasTrailingCommentsAtPosition(pos) || HasLeadingCommentsAtPosition(pos);

    /// <summary>ported from: src/compiler/emitter.ts:3959-3971 — <c>emitJsxExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:3960 — TS's <c>node.expression || (...)</c> is the truthiness test for "an expression is
    /// present"; <c>node.expression?.end || end</c> (3966) likewise falls back to the open brace's position when it is
    /// absent, which <c>??</c> states. <c>currentSourceFile &amp;&amp; ...</c> (3961) is the guard that keeps the two
    /// <c>getLineAndCharacterOfPosition</c> reads reachable only with a source file; the port's
    /// <c>ScannerUtilities.GetLineAndCharacterOfPosition</c> takes an <c>ISourceFileLike</c> that the port's
    /// <c>SourceFile</c> does not implement, so the line map comes from PrinterCore's <c>GetLineStarts</c> and
    /// <c>ComputeLineAndCharacterOfPosition</c> (the same memo, the same two reads).
    /// </remarks>
    private void EmitJsxExpression(JsxExpression node)
    {
        // preserve empty expressions if they contain comments!
        if (node.Expression is not null || (!_commentsDisabled && !Utilities.NodeIsSynthesized(node) && HasCommentsAtPosition(node.Pos)))
        {
            var lineMap = _currentSourceFile is not null ? GetLineStarts(_currentSourceFile) : null;
            var isMultiline = lineMap is not null
                && !Utilities.NodeIsSynthesized(node)
                && ScannerUtilities.ComputeLineAndCharacterOfPosition(lineMap, node.Pos).Line
                    != ScannerUtilities.ComputeLineAndCharacterOfPosition(lineMap, node.End).Line;
            if (isMultiline)
            {
                _writer.IncreaseIndent();
            }

            var end = EmitTokenWithComment(SyntaxKind.OpenBraceToken, node.Pos, WritePunctuation, node);
            Emit(node.DotDotDotToken);
            EmitExpression(node.Expression);
            EmitTokenWithComment(SyntaxKind.CloseBraceToken, node.Expression?.End ?? end, WritePunctuation, node);
            if (isMultiline)
            {
                _writer.DecreaseIndent();
            }
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:3973-3977 — <c>emitJsxNamespacedName</c>.</summary>
    private void EmitJsxNamespacedName(JsxNamespacedName node)
    {
        EmitIdentifierName(node.Namespace);
        WritePunctuation(":");
        EmitIdentifierName(node.Name);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3979-3986 — <c>emitJsxTagName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:3980-3985 — TS's <c>JsxTagNameExpression</c> union (<c>Identifier | ThisExpression |
    /// JsxTagNamePropertyAccess</c>) has no C# name, and the reference's only distinction is the <c>Identifier</c> case
    /// (everything else goes through <c>emit</c>), so the parameter is the upper bound.
    /// </remarks>
    private void EmitJsxTagName(Node node)
    {
        if (node.Kind == SyntaxKind.Identifier)
        {
            EmitExpression(node);
        }
        else
        {
            Emit(node);
        }
    }

    //
    // The work area of `createEmitBinaryExpression` and the parenthesizer-rule adapters
    //

    /// <summary>ported from: src/compiler/emitter.ts:2849-2858 — the <c>WorkArea</c> of <c>createEmitBinaryExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:2850-2857 — TS declares this interface inside <c>createEmitBinaryExpression</c> and grows
    /// its arrays with out-of-range index writes; the port states it as a nested class whose stacks are
    /// <c>List&lt;T&gt;</c>, grown by <see cref="SetSlot"/> — the same treatment
    /// <c>Binder.FlowExpressions.cs</c> gives the binder's work area. TS's initial <c>[undefined]</c>/<c>[-1]</c>/
    /// <c>[false]</c> elements are the initial items below.
    /// </remarks>
    private sealed class WorkArea
    {
        public int StackIndex;
        public List<bool?> PreserveSourceNewlinesStack = [null];
        public List<int> ContainerPosStack = [-1];
        public List<int> ContainerEndStack = [-1];
        public List<int> DeclarationListContainerEndStack = [-1];
        public List<bool> ShouldEmitCommentsStack = [false];
        public List<bool> ShouldEmitSourceMapsStack = [false];

        /// <summary>The port of the JavaScript array-index assignment in the walker's stacks.</summary>
        /// <remarks>
        /// PORT-NOTE: <c>stack[index] = value</c> past the end grows a JavaScript array; a <c>List&lt;T&gt;</c> throws,
        /// so the missing entries are added first — no read can see them before the matching write, and an entry that
        /// was never written answers the absent value, which is what <c>undefined</c> reads back as.
        /// </remarks>
        public static void SetSlot<T>(List<T> stack, int index, T value)
        {
            while (stack.Count <= index)
            {
                stack.Add(default!);
            }

            stack[index] = value;
        }
    }

    /// <summary>Wraps one of the reference's per-operator binary rules as the port's rule shape.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:2940-2942 — <c>parenthesizer.getParenthesize{Left,Right}SideOfBinaryForOperator</c>
    /// answers a <c>Func&lt;Expression, Expression&gt;</c> at run time; the emit family's slot is
    /// <c>Func&lt;Node, Node&gt;</c> (see the file header), so the two shapes meet here.
    /// </remarks>
    private static Func<Node, Node> AsParenthesizerRule(Func<Expression, Expression> rule)
        => node => rule((Expression)node);

    /// <summary>The <c>parenthesizer.parenthesizeExpressionForDisallowedComma</c> rule (emitter.ts:2615, 2716, ...).</summary>
    private Node RuleDisallowedComma(Node node) => _parenthesizer.ParenthesizeExpressionForDisallowedComma((Expression)node);

    /// <summary>The <c>parenthesizer.parenthesizeLeftSideOfAccess</c> rule (emitter.ts:2638, 2698, ...).</summary>
    /// <remarks>PORT-NOTE: the reference's second parameter (<c>optionalChain</c>) is optional there and every call site
    /// in these ranges omits it, so the port passes <c>null</c>.</remarks>
    private Node RuleLeftSideOfAccess(Node node) => _parenthesizer.ParenthesizeLeftSideOfAccess((Expression)node, null);

    /// <summary>The <c>parenthesizer.parenthesizeExpressionOfNew</c> rule (emitter.ts:2726).</summary>
    private Node RuleExpressionOfNew(Node node) => _parenthesizer.ParenthesizeExpressionOfNew((Expression)node);

    /// <summary>The <c>parenthesizer.parenthesizeOperandOfPrefixUnary</c> rule (emitter.ts:2749, 2794, ...).</summary>
    private Node RuleOperandOfPrefixUnary(Node node) => _parenthesizer.ParenthesizeOperandOfPrefixUnary((Expression)node);

    /// <summary>The <c>parenthesizer.parenthesizeOperandOfPostfixUnary</c> rule (emitter.ts:2844).</summary>
    private Node RuleOperandOfPostfixUnary(Node node) => _parenthesizer.ParenthesizeOperandOfPostfixUnary((Expression)node);

    /// <summary>The <c>parenthesizer.parenthesizeConciseBodyOfArrowFunction</c> rule (emitter.ts:2787).</summary>
    private Node RuleConciseBodyOfArrowFunction(Node node) => _parenthesizer.ParenthesizeConciseBodyOfArrowFunction((Expression)node);

    /// <summary>The <c>parenthesizer.parenthesizeConditionOfConditionalExpression</c> rule (emitter.ts:2975).</summary>
    private Node RuleConditionOfConditionalExpression(Node node) => _parenthesizer.ParenthesizeConditionOfConditionalExpression((Expression)node);

    /// <summary>The <c>parenthesizer.parenthesizeBranchOfConditionalExpression</c> rule (emitter.ts:2978, 2989).</summary>
    private Node RuleBranchOfConditionalExpression(Node node) => _parenthesizer.ParenthesizeBranchOfConditionalExpression((Expression)node);

    /// <summary>The <c>parenthesizer.parenthesizeExpressionOfExpressionStatement</c> rule (emitter.ts:3086).</summary>
    private Node RuleExpressionOfExpressionStatement(Node node) => _parenthesizer.ParenthesizeExpressionOfExpressionStatement((Expression)node);

    /// <summary>The <c>parenthesizer.parenthesizeExpressionOfExportDefault</c> rule (emitter.ts:3766).</summary>
    private Node RuleExpressionOfExportDefault(Node node) => _parenthesizer.ParenthesizeExpressionOfExportDefault((Expression)node);

    /// <summary>The <c>parenthesizeExpressionForNoAsi</c> rule (emitter.ts:3003, 3231, 3243).</summary>
    private Node RuleNoAsi(Node node) => ParenthesizeExpressionForNoAsi((Expression)node);

    /// <summary>The <c>parenthesizeExpressionForNoAsiAndDisallowedComma</c> rule (emitter.ts:3003).</summary>
    private Node RuleNoAsiAndDisallowedComma(Node node) => ParenthesizeExpressionForNoAsiAndDisallowedComma((Expression)node);
}