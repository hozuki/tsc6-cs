namespace TypeScript.Compiler;

// ported from: src/compiler/emitter.ts:3036-3876 (statements, declarations, modules) and 3994-4639 (case clauses,
// heritage, property assignments, JSDoc, the source file and its prologue, the decorations/modifiers helpers and the
// parameter/type-argument helpers) — the printer's statement/declaration half.
//
// PORT-NOTE (the shape of these two files): the reference is one closure whose nested functions become methods of this
// class (see Printer.State.cs). Every function is a private member named after the reference's, in the reference's
// order; the helpers it calls are PrinterCore's members (`Emit`, `EmitExpression`, `EmitIdentifierName`, `EmitList` and
// friends, the `Write*` family, `WriteToken`/`WriteTokenText`/`WriteTokenNode`, `GetPipelinePhase` and the phase
// methods, `GetLinesBetweenNodes`, `GetLeadingLineTerminatorCount`/`GetClosingLineTerminatorCount`/
// `GetSeparatingLineTerminatorCount`, `WriteLinesAndIndent`, `WriteLineOrSpace`, `IncreaseIndent`/`DecreaseIndent`/
// `DecreaseIndentIf`, `WriteLineSeparatorsAndIndentBefore/After`, `GetInternalEmitFlags`, `RangeIsOnSingleLine`/
// `RangeStartPositionsAreOnSameLine`/`PositionsAreOnSameLine`, `MoveRangePastModifiers`-family, `IsEmptyBlock`,
// `GetLiteralTextOfNode`, `SetSourceFile`, `ShouldEmitComments`/`ShouldEmitSourceMaps`, `BeforeEmitNode`/
// `AfterEmitNode`, `OnBeforeEmitNodeArray`/`OnAfterEmitNodeArray`), PrinterComments' members
// (`EmitLeadingCommentsOfPosition`, `EmitTrailingCommentsOfPosition`, `EmitCommentsBeforeNode`,
// `EmitCommentsAfterNode`, `EmitSourceMapsBeforeNode`, `EmitSourceMapsAfterNode`, `EmitBodyWithDetachedComments`) and
// PrinterTypes' members (`EmitIdentifier`, `EmitLiteral`, `EmitNumericOrBigIntLiteral`, `EmitJsxAttributeValue`,
// `EmitRestOrJSDocVariadicType`, `EmitAccessorDeclaration`, `EmitMappedTypeParameter`, `EmitHelpers`,
// `GenerateNames`/`GenerateMemberNames`/`GenerateNameIfNeeded`, `PushNameGenerationScope`/
// `PopNameGenerationScope`). The parenthesizer-rule adapters (`RuleXxx`) and the `WorkArea` live in
// Printer.Expressions.cs.
//
// PORT-NOTE (unions): TS's unions with no single port class (`BlockLike`, `ModuleReference`, `NamedImportsOrExports`,
// `ImportOrExportSpecifier`, `WhileStatement | DoStatement`, `FunctionDeclaration | FunctionExpression`,
// `ClassDeclaration | ClassExpression`, the JSDoc tag unions) take the upper bound and read the union's members through
// the dispatch the reference's own single body implies (a `switch` on the concrete shapes, or the generated interface
// that carries the member). Each such function carries a PORT-NOTE naming its reference lines.
internal sealed partial class Printer
{
    //
    // Statements
    //

    /// <summary>ported from: src/compiler/emitter.ts:3036-3038 — <c>emitBlock</c>.</summary>
    private void EmitBlock(Block node)
    {
        EmitBlockStatements(node, /*forceSingleLine*/ node.MultiLine != true && IsEmptyBlock(node));
    }

    /// <summary>ported from: src/compiler/emitter.ts:3040-3045 — <c>emitBlockStatements</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:3040 — TS's <c>BlockLike</c> union (types.ts:3366-3370) has no C# name; the port takes the
    /// upper bound and reads the union's <c>statements</c> member with <see cref="StatementsOfBlockLike"/> (the only
    /// callers pass a <c>Block</c> or a <c>ModuleBlock</c>, per emitter.ts:3037/3653).
    /// </remarks>
    private void EmitBlockStatements(Node node, bool forceSingleLine)
    {
        EmitTokenWithComment(SyntaxKind.OpenBraceToken, node.Pos, WritePunctuation, /*contextNode*/ node);
        var statements = StatementsOfBlockLike(node)!;
        var format = forceSingleLine || (Utilities.GetEmitFlags(node) & EmitFlags.SingleLine) != 0
            ? ListFormat.SingleLineBlockStatements
            : ListFormat.MultiLineBlockStatements;
        EmitList(node, statements, format);
        EmitTokenWithComment(SyntaxKind.CloseBraceToken, statements.End, WritePunctuation, /*contextNode*/ node, /*indentLeading*/ (format & ListFormat.MultiLine) != 0);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3047-3051 — <c>emitVariableStatement</c>.</summary>
    private void EmitVariableStatement(VariableStatement node)
    {
        EmitDecoratorsAndModifiers(node, node.Modifiers, /*allowDecorators*/ false);
        Emit(node.DeclarationList);
        WriteTrailingSemicolon();
    }

    /// <summary>ported from: src/compiler/emitter.ts:3053-3061 — <c>emitEmptyStatement</c>.</summary>
    private void EmitEmptyStatement(bool isEmbeddedStatement)
    {
        // While most trailing semicolons are possibly insignificant, an embedded "empty"
        // statement is significant and cannot be elided by a trailing-semicolon-omitting writer.
        if (isEmbeddedStatement)
        {
            WritePunctuation(";");
        }
        else
        {
            WriteTrailingSemicolon();
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:3063-3070 — <c>emitExpressionStatement</c>.</summary>
    private void EmitExpressionStatement(ExpressionStatement node)
    {
        EmitExpression(node.Expression, RuleExpressionOfExpressionStatement);
        // Emit semicolon in non json files
        // or if json file that created synthesized expression(eg.define expression statement when --out and amd code generation)
        if (_currentSourceFile is null || !Utilities.IsJsonSourceFile(_currentSourceFile) || Utilities.NodeIsSynthesized(node.Expression!))
        {
            WriteTrailingSemicolon();
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:3072-3089 — <c>emitIfStatement</c>.</summary>
    private void EmitIfStatement(IfStatement node)
    {
        var openParenPos = EmitTokenWithComment(SyntaxKind.IfKeyword, node.Pos, WriteKeyword, node);
        WriteSpace();
        EmitTokenWithComment(SyntaxKind.OpenParenToken, openParenPos, WritePunctuation, node);
        EmitExpression(node.Expression);
        EmitTokenWithComment(SyntaxKind.CloseParenToken, node.Expression!.End, WritePunctuation, node);
        EmitEmbeddedStatement(node, node.ThenStatement!);
        if (node.ElseStatement is not null)
        {
            WriteLineOrSpace(node, (Node)node.ThenStatement!, (Node)node.ElseStatement);
            EmitTokenWithComment(SyntaxKind.ElseKeyword, ((Node)node.ThenStatement!).End, WriteKeyword, node);
            if (node.ElseStatement.Kind == SyntaxKind.IfStatement)
            {
                WriteSpace();
                Emit(node.ElseStatement);
            }
            else
            {
                EmitEmbeddedStatement(node, node.ElseStatement);
            }
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:3091-3098 — <c>emitWhileClause</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:3091 — TS's <c>WhileStatement | DoStatement</c> has no C# name; the port takes the upper
    /// bound and reads the union's <c>expression</c> member with <see cref="WhileClauseExpression"/> (the only two
    /// callers are <see cref="EmitDoStatement"/> and <see cref="EmitWhileStatement"/>).
    /// </remarks>
    private void EmitWhileClause(Node node, int startPos)
    {
        var openParenPos = EmitTokenWithComment(SyntaxKind.WhileKeyword, startPos, WriteKeyword, node);
        WriteSpace();
        EmitTokenWithComment(SyntaxKind.OpenParenToken, openParenPos, WritePunctuation, node);
        EmitExpression(WhileClauseExpression(node));
        EmitTokenWithComment(SyntaxKind.CloseParenToken, WhileClauseExpression(node)!.End, WritePunctuation, node);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3100-3111 — <c>emitDoStatement</c>.</summary>
    private void EmitDoStatement(DoStatement node)
    {
        EmitTokenWithComment(SyntaxKind.DoKeyword, node.Pos, WriteKeyword, node);
        EmitEmbeddedStatement(node, node.Statement!);
        if (NodeTests.IsBlock((Node)node.Statement!) && _preserveSourceNewlines != true)
        {
            WriteSpace();
        }
        else
        {
            WriteLineOrSpace(node, (Node)node.Statement!, node.Expression!);
        }

        EmitWhileClause(node, ((Node)node.Statement!).End);
        WriteTrailingSemicolon();
    }

    /// <summary>ported from: src/compiler/emitter.ts:3113-3116 — <c>emitWhileStatement</c>.</summary>
    private void EmitWhileStatement(WhileStatement node)
    {
        EmitWhileClause(node, node.Pos);
        EmitEmbeddedStatement(node, node.Statement!);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3118-3128 — <c>emitForStatement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:3123/3125/3127 — TS's `node.initializer ? node.initializer.end : pos` is the truthiness
    /// test for "the optional member is present"; `is not null` states the same two cases over the port's slots.
    /// </remarks>
    private void EmitForStatement(ForStatement node)
    {
        var openParenPos = EmitTokenWithComment(SyntaxKind.ForKeyword, node.Pos, WriteKeyword, node);
        WriteSpace();
        var pos = EmitTokenWithComment(SyntaxKind.OpenParenToken, openParenPos, WritePunctuation, /*contextNode*/ node);
        EmitForBinding(node.Initializer);
        pos = EmitTokenWithComment(SyntaxKind.SemicolonToken, node.Initializer is not null ? node.Initializer.End : pos, WritePunctuation, node);
        EmitExpressionWithLeadingSpace(node.Condition);
        pos = EmitTokenWithComment(SyntaxKind.SemicolonToken, node.Condition is not null ? node.Condition.End : pos, WritePunctuation, node);
        EmitExpressionWithLeadingSpace(node.Incrementor);
        EmitTokenWithComment(SyntaxKind.CloseParenToken, node.Incrementor is not null ? node.Incrementor.End : pos, WritePunctuation, node);
        EmitEmbeddedStatement(node, node.Statement!);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3130-3141 — <c>emitForInStatement</c>.</summary>
    private void EmitForInStatement(ForInStatement node)
    {
        var openParenPos = EmitTokenWithComment(SyntaxKind.ForKeyword, node.Pos, WriteKeyword, node);
        WriteSpace();
        EmitTokenWithComment(SyntaxKind.OpenParenToken, openParenPos, WritePunctuation, node);
        EmitForBinding(node.Initializer);
        WriteSpace();
        EmitTokenWithComment(SyntaxKind.InKeyword, node.Initializer!.End, WriteKeyword, node);
        WriteSpace();
        EmitExpression(node.Expression);
        EmitTokenWithComment(SyntaxKind.CloseParenToken, node.Expression!.End, WritePunctuation, node);
        EmitEmbeddedStatement(node, node.Statement!);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3143-3156 — <c>emitForOfStatement</c>.</summary>
    private void EmitForOfStatement(ForOfStatement node)
    {
        var openParenPos = EmitTokenWithComment(SyntaxKind.ForKeyword, node.Pos, WriteKeyword, node);
        WriteSpace();
        EmitWithTrailingSpace(node.AwaitModifier);
        EmitTokenWithComment(SyntaxKind.OpenParenToken, openParenPos, WritePunctuation, node);
        EmitForBinding(node.Initializer);
        WriteSpace();
        EmitTokenWithComment(SyntaxKind.OfKeyword, node.Initializer!.End, WriteKeyword, node);
        WriteSpace();
        EmitExpression(node.Expression);
        EmitTokenWithComment(SyntaxKind.CloseParenToken, node.Expression!.End, WritePunctuation, node);
        EmitEmbeddedStatement(node, node.Statement!);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3158-3167 — <c>emitForBinding</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:3158 — TS's <c>VariableDeclarationList | Expression</c> union has no C# name (the port's
    /// <c>Node</c> is a class, so the union widens to it); the reference's `node.kind === VariableDeclarationList`
    /// narrowing becomes the kind test plus the cast the comment licenses — the same shape the other ports use.
    /// </remarks>
    private void EmitForBinding(Node? node)
    {
        if (node is not null)
        {
            if (node.Kind == SyntaxKind.VariableDeclarationList)
            {
                Emit(node);
            }
            else
            {
                EmitExpression((Expression)node);
            }
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:3169-3173 — <c>emitContinueStatement</c>.</summary>
    private void EmitContinueStatement(ContinueStatement node)
    {
        EmitTokenWithComment(SyntaxKind.ContinueKeyword, node.Pos, WriteKeyword, node);
        EmitWithLeadingSpace(node.Label);
        WriteTrailingSemicolon();
    }

    /// <summary>ported from: src/compiler/emitter.ts:3175-3179 — <c>emitBreakStatement</c>.</summary>
    private void EmitBreakStatement(BreakStatement node)
    {
        EmitTokenWithComment(SyntaxKind.BreakKeyword, node.Pos, WriteKeyword, node);
        EmitWithLeadingSpace(node.Label);
        WriteTrailingSemicolon();
    }

    /// <summary>ported from: src/compiler/emitter.ts:3181-3215 — <c>emitTokenWithComment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:3183 — the reference's <c>node && node.kind === contextNode.kind</c> answers
    /// <c>boolean | undefined</c>; the port's null test on <c>getParseTreeNode</c>'s result is the same judgment.
    /// </remarks>
    private int EmitTokenWithComment(SyntaxKind token, int pos, Action<string> writer, Node contextNode, bool indentLeading = false)
    {
        var node = Utilities.GetParseTreeNode(contextNode);
        var isSimilarNode = node is not null && node.Kind == contextNode.Kind;
        var startPos = pos;
        if (isSimilarNode && _currentSourceFile is not null)
        {
            pos = ScannerUtilities.SkipTrivia(_currentSourceFile.Text!, pos);
        }

        if (isSimilarNode && contextNode.Pos != startPos)
        {
            var needsIndent = indentLeading && _currentSourceFile is not null && !PositionsAreOnSameLine(startPos, pos, _currentSourceFile);
            if (needsIndent)
            {
                IncreaseIndent();
            }

            EmitLeadingCommentsOfPosition(startPos);
            if (needsIndent)
            {
                DecreaseIndent();
            }
        }

        // We don't emit source positions for most tokens as it tends to be quite noisy, however
        // we need to emit source positions for open and close braces so that tools like istanbul
        // can map branches for code coverage. However, we still omit brace source positions when
        // the output is a declaration file.
        if (!_omitBraceSourcePositions && (token == SyntaxKind.OpenBraceToken || token == SyntaxKind.CloseBraceToken))
        {
            pos = WriteToken(token, pos, writer, contextNode);
        }
        else
        {
            pos = WriteTokenText(token, writer, pos);
        }

        if (isSimilarNode && contextNode.End != pos)
        {
            var isJsxExprContext = contextNode.Kind == SyntaxKind.JsxExpression;
            EmitTrailingCommentsOfPosition(pos, /*prefixSpace*/ !isJsxExprContext, /*forceNoNewline*/ isJsxExprContext);
        }

        return pos;
    }

    /// <summary>ported from: src/compiler/emitter.ts:3217-3219 — <c>commentWillEmitNewLine</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:3217 — TS's parameter is <c>CommentRange</c>, which both the scanner's ranges and the
    /// synthesized comments satisfy structurally; in the port those two are unrelated types sharing only
    /// <see cref="ICommentRange"/>, which is what this takes.
    /// </remarks>
    private static bool CommentWillEmitNewLine(ICommentRange node)
        => node.Kind == SyntaxKind.SingleLineCommentTrivia || node.HasTrailingNewLine == true;

    /// <summary>ported from: src/compiler/emitter.ts:3221-3235 — <c>willEmitLeadingNewLine</c>.</summary>
    private bool WillEmitLeadingNewLine(Expression node)
    {
        if (_currentSourceFile is null) return false;
        var leadingCommentRanges = ScannerUtilities.GetLeadingCommentRanges(_currentSourceFile.Text!, node.Pos);
        if (leadingCommentRanges is not null)
        {
            var parseNode = Utilities.GetParseTreeNode(node);
            if (parseNode is not null && parseNode.Parent is not null && NodeTests.IsParenthesizedExpression(parseNode.Parent))
            {
                return true;
            }
        }

        if (Core.Some(leadingCommentRanges, CommentWillEmitNewLine)) return true;
        if (Core.Some(EmitNodes.GetSyntheticLeadingComments(node), CommentWillEmitNewLine)) return true;
        if (node is PartiallyEmittedExpression partiallyEmittedExpression)
        {
            if (partiallyEmittedExpression.Pos != partiallyEmittedExpression.Expression!.Pos)
            {
                if (Core.Some(ScannerUtilities.GetTrailingCommentRanges(_currentSourceFile.Text!, partiallyEmittedExpression.Expression.Pos), CommentWillEmitNewLine)) return true;
            }

            return WillEmitLeadingNewLine(partiallyEmittedExpression.Expression);
        }

        return false;
    }

    /// <summary>ported from: src/compiler/emitter.ts:3237-3305 — <c>parenthesizeExpressionForNoAsi</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:3237 — TS's parameter is <c>Expression</c> and its switch narrows <c>node</c> by kind, so
    /// each arm's `<c>node as X</c>` casts are the ones the reference writes; the two `create*`/`update*` calls go to
    /// <c>NodeFactory.Factory</c> (the reference's `factory`).
    /// </remarks>
    private Expression ParenthesizeExpressionForNoAsi(Expression node)
    {
        if (!_commentsDisabled)
        {
            switch (node.Kind)
            {
                case SyntaxKind.PartiallyEmittedExpression:
                    if (WillEmitLeadingNewLine(node))
                    {
                        var parseNode = Utilities.GetParseTreeNode(node);
                        if (parseNode is not null && NodeTests.IsParenthesizedExpression(parseNode))
                        {
                            // If the original node was a parenthesized expression, restore it to preserve comment and source map emit
                            var parens = NodeFactory.Factory.CreateParenthesizedExpression(((PartiallyEmittedExpression)node).Expression);
                            NodeFactory.SetOriginalNode(parens, node);
                            Utilities.SetTextRange(parens, parseNode);
                            return parens;
                        }

                        return NodeFactory.Factory.CreateParenthesizedExpression(node);
                    }

                    return NodeFactory.Factory.UpdatePartiallyEmittedExpression(
                        (PartiallyEmittedExpression)node,
                        ParenthesizeExpressionForNoAsi(((PartiallyEmittedExpression)node).Expression!));
                case SyntaxKind.PropertyAccessExpression:
                    return NodeFactory.Factory.UpdatePropertyAccessExpression(
                        (PropertyAccessExpression)node,
                        ParenthesizeExpressionForNoAsi(((PropertyAccessExpression)node).Expression!),
                        ((PropertyAccessExpression)node).Name);
                case SyntaxKind.ElementAccessExpression:
                    return NodeFactory.Factory.UpdateElementAccessExpression(
                        (ElementAccessExpression)node,
                        ParenthesizeExpressionForNoAsi(((ElementAccessExpression)node).Expression!),
                        ((ElementAccessExpression)node).ArgumentExpression);
                case SyntaxKind.CallExpression:
                    return NodeFactory.Factory.UpdateCallExpression(
                        (CallExpression)node,
                        ParenthesizeExpressionForNoAsi(((CallExpression)node).Expression!),
                        ((CallExpression)node).TypeArguments,
                        ((CallExpression)node).Arguments);
                case SyntaxKind.TaggedTemplateExpression:
                    return NodeFactory.Factory.UpdateTaggedTemplateExpression(
                        (TaggedTemplateExpression)node,
                        ParenthesizeExpressionForNoAsi(((TaggedTemplateExpression)node).Tag!),
                        ((TaggedTemplateExpression)node).TypeArguments,
                        ((TaggedTemplateExpression)node).Template);
                case SyntaxKind.PostfixUnaryExpression:
                    return NodeFactory.Factory.UpdatePostfixUnaryExpression(
                        (PostfixUnaryExpression)node,
                        ParenthesizeExpressionForNoAsi(((PostfixUnaryExpression)node).Operand!));
                case SyntaxKind.BinaryExpression:
                    return NodeFactory.Factory.UpdateBinaryExpression(
                        (BinaryExpression)node,
                        ParenthesizeExpressionForNoAsi(((BinaryExpression)node).Left!),
                        ((BinaryExpression)node).OperatorToken,
                        ((BinaryExpression)node).Right);
                case SyntaxKind.ConditionalExpression:
                    return NodeFactory.Factory.UpdateConditionalExpression(
                        (ConditionalExpression)node,
                        ParenthesizeExpressionForNoAsi(((ConditionalExpression)node).Condition!),
                        ((ConditionalExpression)node).QuestionToken,
                        ((ConditionalExpression)node).WhenTrue,
                        ((ConditionalExpression)node).ColonToken,
                        ((ConditionalExpression)node).WhenFalse);
                case SyntaxKind.AsExpression:
                    return NodeFactory.Factory.UpdateAsExpression(
                        (AsExpression)node,
                        ParenthesizeExpressionForNoAsi(((AsExpression)node).Expression!),
                        ((AsExpression)node).Type);
                case SyntaxKind.SatisfiesExpression:
                    return NodeFactory.Factory.UpdateSatisfiesExpression(
                        (SatisfiesExpression)node,
                        ParenthesizeExpressionForNoAsi(((SatisfiesExpression)node).Expression!),
                        ((SatisfiesExpression)node).Type);
                case SyntaxKind.NonNullExpression:
                    return NodeFactory.Factory.UpdateNonNullExpression(
                        (NonNullExpression)node,
                        ParenthesizeExpressionForNoAsi(((NonNullExpression)node).Expression!));
            }
        }

        return node;
    }

    /// <summary>ported from: src/compiler/emitter.ts:3307-3309 — <c>parenthesizeExpressionForNoAsiAndDisallowedComma</c>.</summary>
    private Expression ParenthesizeExpressionForNoAsiAndDisallowedComma(Expression node)
        => ParenthesizeExpressionForNoAsi(_parenthesizer.ParenthesizeExpressionForDisallowedComma(node));

    /// <summary>ported from: src/compiler/emitter.ts:3311-3315 — <c>emitReturnStatement</c>.</summary>
    private void EmitReturnStatement(ReturnStatement node)
    {
        EmitTokenWithComment(SyntaxKind.ReturnKeyword, node.Pos, WriteKeyword, /*contextNode*/ node);
        EmitExpressionWithLeadingSpace(node.Expression is { } expression ? ParenthesizeExpressionForNoAsi(expression) : null, RuleNoAsi);
        WriteTrailingSemicolon();
    }

    /// <summary>ported from: src/compiler/emitter.ts:3317-3325 — <c>emitWithStatement</c>.</summary>
    private void EmitWithStatement(WithStatement node)
    {
        var openParenPos = EmitTokenWithComment(SyntaxKind.WithKeyword, node.Pos, WriteKeyword, node);
        WriteSpace();
        EmitTokenWithComment(SyntaxKind.OpenParenToken, openParenPos, WritePunctuation, node);
        EmitExpression(node.Expression);
        EmitTokenWithComment(SyntaxKind.CloseParenToken, node.Expression!.End, WritePunctuation, node);
        EmitEmbeddedStatement(node, node.Statement!);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3327-3336 — <c>emitSwitchStatement</c>.</summary>
    private void EmitSwitchStatement(SwitchStatement node)
    {
        var openParenPos = EmitTokenWithComment(SyntaxKind.SwitchKeyword, node.Pos, WriteKeyword, node);
        WriteSpace();
        EmitTokenWithComment(SyntaxKind.OpenParenToken, openParenPos, WritePunctuation, node);
        EmitExpression(node.Expression);
        EmitTokenWithComment(SyntaxKind.CloseParenToken, node.Expression!.End, WritePunctuation, node);
        WriteSpace();
        Emit(node.CaseBlock);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3338-3343 — <c>emitLabeledStatement</c>.</summary>
    private void EmitLabeledStatement(LabeledStatement node)
    {
        Emit(node.Label);
        EmitTokenWithComment(SyntaxKind.ColonToken, node.Label!.End, WritePunctuation, node);
        WriteSpace();
        Emit(node.Statement);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3345-3349 — <c>emitThrowStatement</c>.</summary>
    private void EmitThrowStatement(ThrowStatement node)
    {
        EmitTokenWithComment(SyntaxKind.ThrowKeyword, node.Pos, WriteKeyword, node);
        EmitExpressionWithLeadingSpace(ParenthesizeExpressionForNoAsi(node.Expression!), RuleNoAsi);
        WriteTrailingSemicolon();
    }

    /// <summary>ported from: src/compiler/emitter.ts:3351-3365 — <c>emitTryStatement</c>.</summary>
    private void EmitTryStatement(TryStatement node)
    {
        EmitTokenWithComment(SyntaxKind.TryKeyword, node.Pos, WriteKeyword, node);
        WriteSpace();
        Emit(node.TryBlock);
        if (node.CatchClause is not null)
        {
            WriteLineOrSpace(node, node.TryBlock!, node.CatchClause);
            Emit(node.CatchClause);
        }

        if (node.FinallyBlock is not null)
        {
            WriteLineOrSpace(node, node.CatchClause ?? (Node)node.TryBlock!, node.FinallyBlock);
            EmitTokenWithComment(SyntaxKind.FinallyKeyword, (node.CatchClause ?? (Node)node.TryBlock!).End, WriteKeyword, node);
            WriteSpace();
            Emit(node.FinallyBlock);
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:3367-3370 — <c>emitDebuggerStatement</c>.</summary>
    private void EmitDebuggerStatement(DebuggerStatement node)
    {
        WriteToken(SyntaxKind.DebuggerKeyword, node.Pos, WriteKeyword);
        WriteTrailingSemicolon();
    }

    //
    // Declarations
    //

    /// <summary>ported from: src/compiler/emitter.ts:3376-3381 — <c>emitVariableDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:3379 — TS reads <c>node.name.emitNode?.typeNode?.end</c> and <c>node.type?.end</c>; the port's
    /// <c>typeNode</c> slot is reached through <c>EmitNodes.GetTypeNode</c> and both are typed as the interface
    /// <see cref="ITypeNode"/>, which carries no text range, so each is read through a <c>(Node?)</c> cast — the port's
    /// standing treatment of interface-typed slots that must be read as ranges.
    /// </remarks>
    private void EmitVariableDeclaration(VariableDeclaration node)
    {
        Emit(node.Name);
        Emit(node.ExclamationToken);
        EmitTypeAnnotation(node.Type);
        EmitInitializer(
            node.Initializer,
            ((Node?)node.Type)?.End ?? ((Node?)EmitNodes.GetTypeNode(node.Name!))?.End ?? node.Name!.End,
            node,
            RuleDisallowedComma);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3383-3397 — <c>emitVariableDeclarationList</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:3384-3391 — <c>isVarAwaitUsing</c>/<c>isLet</c>/<c>isVarUsing</c> (utilities.ts:2661-2698)
    /// are not landed anywhere else in the port and nothing outside this method calls them, so they are ported beside
    /// their only caller (<c>isVarConst</c> is the one that does exist, in <c>Utilities</c>).
    /// </remarks>
    private void EmitVariableDeclarationList(VariableDeclarationList node)
    {
        if (IsVarAwaitUsing(node))
        {
            WriteKeyword("await");
            WriteSpace();
            WriteKeyword("using");
        }
        else
        {
            var head = IsLet(node) ? "let" :
                Utilities.IsVarConst(node) ? "const" :
                IsVarUsing(node) ? "using" :
                "var";
            WriteKeyword(head);
        }

        WriteSpace();
        EmitList(node, node.Declarations, ListFormat.VariableDeclarationList);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3399-3401 — <c>emitFunctionDeclaration</c>.</summary>
    private void EmitFunctionDeclaration(FunctionDeclaration node)
    {
        EmitFunctionDeclarationOrExpression(node);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3403-3410 — <c>emitFunctionDeclarationOrExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:3403 — TS's <c>FunctionDeclaration | FunctionExpression</c> union has no C# name, and
    /// <c>modifiers</c> is not on a shared generated interface (<c>HasModifiers</c> has no port counterpart), so the two
    /// declaration shapes are dispatched for that one member; <c>asteriskToken</c> and <c>name</c> come from
    /// <see cref="IFunctionLikeDeclarationBase"/> and <see cref="INamedDeclaration"/>, which both implement.
    /// </remarks>
    private void EmitFunctionDeclarationOrExpression(Node node)
    {
        switch (node)
        {
            case FunctionDeclaration declaration:
                EmitDecoratorsAndModifiers(declaration, declaration.Modifiers, /*allowDecorators*/ false);
                break;
            case FunctionExpression expression:
                EmitDecoratorsAndModifiers(expression, expression.Modifiers, /*allowDecorators*/ false);
                break;
        }

        WriteKeyword("function");
        Emit(((IFunctionLikeDeclarationBase)node).AsteriskToken);
        WriteSpace();
        EmitIdentifierName(((INamedDeclaration)node).Name);
        EmitSignatureAndBody(node, EmitSignatureHead, EmitFunctionBody);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3412-3426 — <c>emitSignatureAndBody</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:3412 — TS's <c>T extends SignatureDeclaration</c> has no C# bound (there is no
    /// <c>SignatureDeclaration</c> type in the port), so <c>T</c> is bounded by <c>Node</c> and the union's
    /// <c>parameters</c> member is read through <see cref="ISignatureDeclarationBase"/>, which every function-like node
    /// class implements.
    /// </remarks>
    private void EmitSignatureAndBody<T>(T node, Action<T> emitSignatureHead, Action<T> emitBody)
        where T : Node
    {
        var indentedFlag = (Utilities.GetEmitFlags(node) & EmitFlags.Indented) != 0;
        if (indentedFlag)
        {
            IncreaseIndent();
        }

        PushNameGenerationScope(node);
        Core.ForEach(((ISignatureDeclarationBase)node).Parameters, (ParameterDeclaration parameter, int _) =>
        {
            GenerateNames(parameter);
            return (object?)null;
        });
        emitSignatureHead(node);
        emitBody(node);
        PopNameGenerationScope(node);

        if (indentedFlag)
        {
            DecreaseIndent();
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:3428-3437 — <c>emitFunctionBody</c>.</summary>
    private void EmitFunctionBody(Node node)
    {
        var body = ((IFunctionLikeDeclarationBase)node).Body;
        if (body is not null)
        {
            EmitBlockFunctionBody((Block)body);
        }
        else
        {
            WriteTrailingSemicolon();
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:3439-3441 — <c>emitEmptyFunctionBody</c>.</summary>
    private void EmitEmptyFunctionBody(Node node)
    {
        WriteTrailingSemicolon();
    }

    /// <summary>ported from: src/compiler/emitter.ts:3443-3447 — <c>emitSignatureHead</c>.</summary>
    private void EmitSignatureHead(Node node)
    {
        var signature = (ISignatureDeclarationBase)node;
        EmitTypeParameters(node, signature.TypeParameters);
        EmitParameters(node, signature.Parameters);
        EmitTypeAnnotation(signature.Type);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3449-3482 — <c>shouldEmitBlockFunctionBodyOnSingleLine</c>.</summary>
    private bool ShouldEmitBlockFunctionBodyOnSingleLine(Block body)
    {
        // We must emit a function body as a single-line body in the following case:
        // * The body has NodeEmitFlags.SingleLine specified.

        // We must emit a function body as a multi-line body in the following cases:
        // * The body is explicitly marked as multi-line.
        // * A non-synthesized body's start and end position are on different lines.
        // * Any statement in the body starts on a new line.

        if ((Utilities.GetEmitFlags(body) & EmitFlags.SingleLine) != 0)
        {
            return true;
        }

        if (body.MultiLine == true)
        {
            return false;
        }

        if (!Utilities.NodeIsSynthesized(body) && _currentSourceFile is not null && !RangeIsOnSingleLine(body, _currentSourceFile))
        {
            return false;
        }

        if (GetLeadingLineTerminatorCount(body, (Node?)Core.FirstOrUndefined(body.Statements), ListFormat.PreserveLines) != 0
            || GetClosingLineTerminatorCount(body, (Node?)Core.LastOrUndefined(body.Statements), ListFormat.PreserveLines, body.Statements) != 0)
        {
            return false;
        }

        Node? previousStatement = null;
        foreach (var statement in body.Statements!)
        {
            if (GetSeparatingLineTerminatorCount(previousStatement, (Node)statement, ListFormat.PreserveLines) > 0)
            {
                return false;
            }

            previousStatement = (Node)statement;
        }

        return true;
    }

    /// <summary>ported from: src/compiler/emitter.ts:3484-3498 — <c>emitBlockFunctionBody</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:3491-3493 — the reference picks the body emitter with a ternary over two methods whose
    /// second parameter is optional (TS's <c>emitBlockFunctionBodyWorker(body, onSingleLine?)</c>); C# cannot convert a
    /// method with an optional parameter to an <c>Action&lt;Block&gt;</c>, so the worker arm states the callback.
    /// </remarks>
    private void EmitBlockFunctionBody(Block body)
    {
        GenerateNames(body);
        _handlers.OnBeforeEmitNode?.Invoke(body);
        WriteSpace();
        WritePunctuation("{");
        IncreaseIndent();

        if (ShouldEmitBlockFunctionBodyOnSingleLine(body))
        {
            EmitBodyWithDetachedComments(body, body.Statements!, EmitBlockFunctionBodyOnSingleLine);
        }
        else
        {
            EmitBodyWithDetachedComments(body, body.Statements!, (Block block) => EmitBlockFunctionBodyWorker(block));
        }

        DecreaseIndent();
        WriteToken(SyntaxKind.CloseBraceToken, body.Statements!.End, WritePunctuation, body);
        _handlers.OnAfterEmitNode?.Invoke(body);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3500-3502 — <c>emitBlockFunctionBodyOnSingleLine</c>.</summary>
    private void EmitBlockFunctionBodyOnSingleLine(Block body)
    {
        EmitBlockFunctionBodyWorker(body, /*emitBlockFunctionBodyOnSingleLine*/ true);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3504-3517 — <c>emitBlockFunctionBodyWorker</c>.</summary>
    private void EmitBlockFunctionBodyWorker(Block body, bool? emitBlockFunctionBodyOnSingleLine = null)
    {
        // Emit all the prologue directives (like "use strict").
        var statementOffset = EmitPrologueDirectives(body.Statements!);
        var pos = _writer.GetTextPos();
        EmitHelpers(body);
        if (statementOffset == 0 && pos == _writer.GetTextPos() && emitBlockFunctionBodyOnSingleLine == true)
        {
            DecreaseIndent();
            EmitList(body, body.Statements, ListFormat.SingleLineFunctionBodyStatements);
            IncreaseIndent();
        }
        else
        {
            EmitList(body, body.Statements, ListFormat.MultiLineFunctionBodyStatements, parenthesizerRule: null, start: statementOffset);
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:3519-3521 — <c>emitClassDeclaration</c>.</summary>
    private void EmitClassDeclaration(ClassDeclaration node)
    {
        EmitClassDeclarationOrExpression(node);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3523-3552 — <c>emitClassDeclarationOrExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:3523 — TS's <c>ClassDeclaration | ClassExpression</c> union has no C# name; the two shapes
    /// are dispatched for <c>modifiers</c> (not on a shared generated interface) and every other member comes from
    /// <see cref="IClassLikeDeclarationBase"/>/<see cref="INamedDeclaration"/>, which both implement.
    /// </remarks>
    private void EmitClassDeclarationOrExpression(Node node)
    {
        switch (node)
        {
            case ClassDeclaration classDeclaration:
                EmitDecoratorsAndModifiers(classDeclaration, classDeclaration.Modifiers, /*allowDecorators*/ true);
                break;
            case ClassExpression classExpression:
                EmitDecoratorsAndModifiers(classExpression, classExpression.Modifiers, /*allowDecorators*/ true);
                break;
        }

        var classLike = (IClassLikeDeclarationBase)node;
        EmitTokenWithComment(SyntaxKind.ClassKeyword, MoveRangePastModifiers(node).Pos, WriteKeyword, node);
        var name = ((INamedDeclaration)node).Name;
        if (name is not null)
        {
            WriteSpace();
            EmitIdentifierName(name);
        }

        var indentedFlag = (Utilities.GetEmitFlags(node) & EmitFlags.Indented) != 0;
        if (indentedFlag)
        {
            IncreaseIndent();
        }

        EmitTypeParameters(node, classLike.TypeParameters);
        EmitList(node, classLike.HeritageClauses, ListFormat.ClassHeritageClauses);
        WriteSpace();
        WritePunctuation("{");

        PushNameGenerationScope(node);
        Core.ForEach(classLike.Members, (IClassElement member, int _) =>
        {
            GenerateMemberNames((Node)member);
            return (object?)null;
        });
        EmitList(node, classLike.Members, ListFormat.ClassMembers);
        PopNameGenerationScope(node);

        WritePunctuation("}");

        if (indentedFlag)
        {
            DecreaseIndent();
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:3554-3576 — <c>emitInterfaceDeclaration</c>.</summary>
    private void EmitInterfaceDeclaration(InterfaceDeclaration node)
    {
        EmitDecoratorsAndModifiers(node, node.Modifiers, /*allowDecorators*/ false);
        WriteKeyword("interface");
        WriteSpace();
        Emit(node.Name);
        EmitTypeParameters(node, node.TypeParameters);
        EmitList(node, node.HeritageClauses, ListFormat.HeritageClauses);
        WriteSpace();
        WritePunctuation("{");

        PushNameGenerationScope(node);
        Core.ForEach(node.Members, (ITypeElement member, int _) =>
        {
            GenerateMemberNames((Node)member);
            return (object?)null;
        });
        EmitList(node, node.Members, ListFormat.InterfaceMembers);
        PopNameGenerationScope(node);

        WritePunctuation("}");
    }

    /// <summary>ported from: src/compiler/emitter.ts:3578-3590 — <c>emitTypeAliasDeclaration</c>.</summary>
    private void EmitTypeAliasDeclaration(TypeAliasDeclaration node)
    {
        EmitDecoratorsAndModifiers(node, node.Modifiers, /*allowDecorators*/ false);
        WriteKeyword("type");
        WriteSpace();
        Emit(node.Name);
        EmitTypeParameters(node, node.TypeParameters);
        WriteSpace();
        WritePunctuation("=");
        WriteSpace();
        Emit(node.Type);
        WriteTrailingSemicolon();
    }

    /// <summary>ported from: src/compiler/emitter.ts:3592-3604 — <c>emitEnumDeclaration</c>.</summary>
    private void EmitEnumDeclaration(EnumDeclaration node)
    {
        EmitDecoratorsAndModifiers(node, node.Modifiers, /*allowDecorators*/ false);
        WriteKeyword("enum");
        WriteSpace();
        Emit(node.Name);

        WriteSpace();
        WritePunctuation("{");
        EmitList(node, node.Members, ListFormat.EnumMembers);
        WritePunctuation("}");
    }

    /// <summary>ported from: src/compiler/emitter.ts:3606-3624 — <c>emitModuleDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:3608 — TS's `~node.flags &amp; NodeFlags.GlobalAugmentation` tests the complement for
    /// truthiness; the port states the same test as "not zero".
    /// </remarks>
    private void EmitModuleDeclaration(ModuleDeclaration node)
    {
        EmitDecoratorsAndModifiers(node, node.Modifiers, /*allowDecorators*/ false);
        if ((~node.Flags & NodeFlags.GlobalAugmentation) != 0)
        {
            WriteKeyword((node.Flags & NodeFlags.Namespace) != 0 ? "namespace" : "module");
            WriteSpace();
        }

        Emit(node.Name);

        var body = node.Body;
        if (body is null)
        {
            WriteTrailingSemicolon();
            return;
        }

        while (body is not null && NodeTests.IsModuleDeclaration(body))
        {
            WritePunctuation(".");
            Emit(((ModuleDeclaration)body).Name);
            body = ((ModuleDeclaration)body).Body;
        }

        WriteSpace();
        Emit(body);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3626-3631 — <c>emitModuleBlock</c>.</summary>
    private void EmitModuleBlock(ModuleBlock node)
    {
        PushNameGenerationScope(node);
        Core.ForEach(node.Statements, (IStatement statement, int _) =>
        {
            GenerateNames((Node)statement);
            return (object?)null;
        });
        EmitBlockStatements(node, /*forceSingleLine*/ IsEmptyBlockLike(node));
        PopNameGenerationScope(node);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3633-3637 — <c>emitCaseBlock</c>.</summary>
    private void EmitCaseBlock(CaseBlock node)
    {
        EmitTokenWithComment(SyntaxKind.OpenBraceToken, node.Pos, WritePunctuation, node);
        EmitList(node, node.Clauses, ListFormat.CaseBlockClauses);
        EmitTokenWithComment(SyntaxKind.CloseBraceToken, node.Clauses!.End, WritePunctuation, node, /*indentLeading*/ true);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3639-3660 — <c>emitImportEqualsDeclaration</c>.</summary>
    private void EmitImportEqualsDeclaration(ImportEqualsDeclaration node)
    {
        EmitDecoratorsAndModifiers(node, node.Modifiers, /*allowDecorators*/ false);
        EmitTokenWithComment(SyntaxKind.ImportKeyword, node.Modifiers is not null ? node.Modifiers.End : node.Pos, WriteKeyword, node);
        WriteSpace();
        if (node.IsTypeOnly)
        {
            EmitTokenWithComment(SyntaxKind.TypeKeyword, node.Pos, WriteKeyword, node);
            WriteSpace();
        }

        Emit(node.Name);
        WriteSpace();
        EmitTokenWithComment(SyntaxKind.EqualsToken, node.Name!.End, WritePunctuation, node);
        WriteSpace();
        EmitModuleReference(node.ModuleReference!);
        WriteTrailingSemicolon();
    }

    /// <summary>ported from: src/compiler/emitter.ts:3662-3669 — <c>emitModuleReference</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:3662 — TS's <c>ModuleReference</c> union (<c>EntityName | ExternalModuleReference</c>) has
    /// no C# name; the reference's <c>node.kind === Identifier</c> narrowing becomes the kind test plus the cast.
    /// </remarks>
    private void EmitModuleReference(Node node)
    {
        if (node.Kind == SyntaxKind.Identifier)
        {
            EmitExpression((Expression)node);
        }
        else
        {
            Emit(node);
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:3671-3684 — <c>emitImportDeclaration</c>.</summary>
    private void EmitImportDeclaration(ImportDeclaration node)
    {
        EmitDecoratorsAndModifiers(node, node.Modifiers, /*allowDecorators*/ false);
        EmitTokenWithComment(SyntaxKind.ImportKeyword, node.Modifiers is not null ? node.Modifiers.End : node.Pos, WriteKeyword, node);
        WriteSpace();
        if (node.ImportClause is not null)
        {
            Emit(node.ImportClause);
            WriteSpace();
            EmitTokenWithComment(SyntaxKind.FromKeyword, node.ImportClause.End, WriteKeyword, node);
            WriteSpace();
        }

        EmitExpression(node.ModuleSpecifier);
        if (node.Attributes is not null)
        {
            EmitWithLeadingSpace(node.Attributes);
        }

        WriteTrailingSemicolon();
    }

    /// <summary>ported from: src/compiler/emitter.ts:3686-3697 — <c>emitImportClause</c>.</summary>
    private void EmitImportClause(ImportClause node)
    {
        if (node.PhaseModifier is not null)
        {
            EmitTokenWithComment(node.PhaseModifier.Value, node.Pos, WriteKeyword, node);
            WriteSpace();
        }

        Emit(node.Name);
        if (node.Name is not null && node.NamedBindings is not null)
        {
            EmitTokenWithComment(SyntaxKind.CommaToken, node.Name.End, WritePunctuation, node);
            WriteSpace();
        }

        Emit(node.NamedBindings);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3699-3705 — <c>emitNamespaceImport</c>.</summary>
    private void EmitNamespaceImport(NamespaceImport node)
    {
        var asPos = EmitTokenWithComment(SyntaxKind.AsteriskToken, node.Pos, WritePunctuation, node);
        WriteSpace();
        EmitTokenWithComment(SyntaxKind.AsKeyword, asPos, WriteKeyword, node);
        WriteSpace();
        Emit(node.Name);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3707-3709 — <c>emitNamedImports</c>.</summary>
    private void EmitNamedImports(NamedImports node)
    {
        EmitNamedImportsOrExports(node);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3711-3713 — <c>emitImportSpecifier</c>.</summary>
    private void EmitImportSpecifier(ImportSpecifier node)
    {
        EmitImportOrExportSpecifier(node);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3715-3731 — <c>emitExportAssignment</c>.</summary>
    private void EmitExportAssignment(ExportAssignment node)
    {
        var nextPos = EmitTokenWithComment(SyntaxKind.ExportKeyword, node.Pos, WriteKeyword, node);
        WriteSpace();
        if (node.IsExportEquals == true)
        {
            EmitTokenWithComment(SyntaxKind.EqualsToken, nextPos, WriteOperator, node);
        }
        else
        {
            EmitTokenWithComment(SyntaxKind.DefaultKeyword, nextPos, WriteKeyword, node);
        }

        WriteSpace();
        EmitExpression(
            node.Expression,
            node.IsExportEquals == true
                ? AsParenthesizerRule(_parenthesizer.GetParenthesizeRightSideOfBinaryForOperator(SyntaxKind.EqualsToken))
                : RuleExpressionOfExportDefault);
        WriteTrailingSemicolon();
    }

    /// <summary>ported from: src/compiler/emitter.ts:3733-3759 — <c>emitExportDeclaration</c>.</summary>
    private void EmitExportDeclaration(ExportDeclaration node)
    {
        EmitDecoratorsAndModifiers(node, node.Modifiers, /*allowDecorators*/ false);
        var nextPos = EmitTokenWithComment(SyntaxKind.ExportKeyword, node.Pos, WriteKeyword, node);
        WriteSpace();
        if (node.IsTypeOnly)
        {
            nextPos = EmitTokenWithComment(SyntaxKind.TypeKeyword, nextPos, WriteKeyword, node);
            WriteSpace();
        }

        if (node.ExportClause is not null)
        {
            Emit(node.ExportClause);
        }
        else
        {
            nextPos = EmitTokenWithComment(SyntaxKind.AsteriskToken, nextPos, WritePunctuation, node);
        }

        if (node.ModuleSpecifier is not null)
        {
            WriteSpace();
            var fromPos = node.ExportClause is not null ? node.ExportClause.End : nextPos;
            EmitTokenWithComment(SyntaxKind.FromKeyword, fromPos, WriteKeyword, node);
            WriteSpace();
            EmitExpression(node.ModuleSpecifier);
        }

        if (node.Attributes is not null)
        {
            EmitWithLeadingSpace(node.Attributes);
        }

        WriteTrailingSemicolon();
    }

    /// <summary>ported from: src/compiler/emitter.ts:3761-3770 — <c>emitImportTypeNodeAttributes</c>.</summary>
    private void EmitImportTypeNodeAttributes(ImportAttributes node)
    {
        WritePunctuation("{");
        WriteSpace();
        WriteKeyword(node.Token == SyntaxKind.AssertKeyword ? "assert" : "with");
        WritePunctuation(":");
        WriteSpace();
        var elements = node.Elements;
        EmitList(node, elements, ListFormat.ImportAttributes);
        WriteSpace();
        WritePunctuation("}");
    }

    /// <summary>ported from: src/compiler/emitter.ts:3772-3777 — <c>emitImportAttributes</c>.</summary>
    private void EmitImportAttributes(ImportAttributes node)
    {
        EmitTokenWithComment(node.Token, node.Pos, WriteKeyword, node);
        WriteSpace();
        var elements = node.Elements;
        EmitList(node, elements, ListFormat.ImportAttributes);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3779-3793 — <c>emitImportAttribute</c>.</summary>
    /// <remarks>PORT-NOTE: emitter.ts:3788 — the reference's cast comment <c>@see {emitPropertyAssignment}</c> is kept;
    /// the trailing-comment emit is the one <see cref="EmitPropertyAssignment"/> performs.</remarks>
    private void EmitImportAttribute(ImportAttribute node)
    {
        Emit(node.Name);
        WritePunctuation(":");
        WriteSpace();

        var value = node.Value!;
        /** @see {emitPropertyAssignment} */
        if ((Utilities.GetEmitFlags(value) & EmitFlags.NoLeadingComments) == 0)
        {
            var commentRange = EmitNodes.GetCommentRange(value);
            EmitTrailingCommentsOfPosition(commentRange.Pos);
        }

        Emit(value);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3795-3806 — <c>emitNamespaceExportDeclaration</c>.</summary>
    private void EmitNamespaceExportDeclaration(NamespaceExportDeclaration node)
    {
        var nextPos = EmitTokenWithComment(SyntaxKind.ExportKeyword, node.Pos, WriteKeyword, node);
        WriteSpace();
        nextPos = EmitTokenWithComment(SyntaxKind.AsKeyword, nextPos, WriteKeyword, node);
        WriteSpace();
        nextPos = EmitTokenWithComment(SyntaxKind.NamespaceKeyword, nextPos, WriteKeyword, node);
        WriteSpace();
        Emit(node.Name);
        WriteTrailingSemicolon();
    }

    /// <summary>ported from: src/compiler/emitter.ts:3808-3814 — <c>emitNamespaceExport</c>.</summary>
    private void EmitNamespaceExport(NamespaceExport node)
    {
        var asPos = EmitTokenWithComment(SyntaxKind.AsteriskToken, node.Pos, WritePunctuation, node);
        WriteSpace();
        EmitTokenWithComment(SyntaxKind.AsKeyword, asPos, WriteKeyword, node);
        WriteSpace();
        Emit(node.Name);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3816-3818 — <c>emitNamedExports</c>.</summary>
    private void EmitNamedExports(NamedExports node)
    {
        EmitNamedImportsOrExports(node);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3820-3822 — <c>emitExportSpecifier</c>.</summary>
    private void EmitExportSpecifier(ExportSpecifier node)
    {
        EmitImportOrExportSpecifier(node);
    }

    /// <summary>ported from: src/compiler/emitter.ts:3824-3828 — <c>emitNamedImportsOrExports</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:3825 — TS's <c>NamedImports | NamedExports</c> union has no C# name and its <c>elements</c>
    /// member is a differently-typed array in each arm (<c>NodeArray&lt;ImportSpecifier&gt;</c> vs
    /// <c>NodeArray&lt;ExportSpecifier&gt;</c>), which C# cannot unify; the shared body is stated once per arm.
    /// </remarks>
    private void EmitNamedImportsOrExports(Node node)
    {
        WritePunctuation("{");
        switch (node)
        {
            case NamedImports namedImports:
                EmitList(node, namedImports.Elements, ListFormat.NamedImportsOrExportsElements);
                break;
            case NamedExports namedExports:
                EmitList(node, namedExports.Elements, ListFormat.NamedImportsOrExportsElements);
                break;
            default:
                Debug.Fail($"Unhandled SyntaxKind: {node.Kind}.");
                break;
        }

        WritePunctuation("}");
    }

    /// <summary>ported from: src/compiler/emitter.ts:3830-3844 — <c>emitImportOrExportSpecifier</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:3830 — TS's <c>ImportOrExportSpecifier</c> union has no C# name, and its three members are
    /// typed differently per arm (<c>name</c> is <c>Identifier</c> in one and <c>ModuleExportName</c> in the other), so
    /// the union's members are read once per arm into locals, which the shared body below then writes.
    /// </remarks>
    private void EmitImportOrExportSpecifier(Node node)
    {
        var (isTypeOnly, propertyName, name) = node switch
        {
            ImportSpecifier importSpecifier => (importSpecifier.IsTypeOnly, importSpecifier.PropertyName, (Node?)importSpecifier.Name),
            ExportSpecifier exportSpecifier => (exportSpecifier.IsTypeOnly, exportSpecifier.PropertyName, (Node?)exportSpecifier.Name),
            _ => Debug.Fail<(bool, MemberExpression?, Node?)>($"Unhandled SyntaxKind: {node.Kind}."),
        };

        if (isTypeOnly)
        {
            WriteKeyword("type");
            WriteSpace();
        }

        if (propertyName is not null)
        {
            Emit(propertyName);
            WriteSpace();
            EmitTokenWithComment(SyntaxKind.AsKeyword, propertyName.End, WriteKeyword, node);
            WriteSpace();
        }

        Emit(name);
    }

    //
    // Module references
    //

    /// <summary>ported from: src/compiler/emitter.ts:3850-3855 — <c>emitExternalModuleReference</c>.</summary>
    private void EmitExternalModuleReference(ExternalModuleReference node)
    {
        WriteKeyword("require");
        WritePunctuation("(");
        EmitExpression(node.Expression);
        WritePunctuation(")");
    }

    //
    // Clauses
    //

    /// <summary>ported from: src/compiler/emitter.ts:3994-4001 — <c>emitCaseClause</c>.</summary>
    private void EmitCaseClause(CaseClause node)
    {
        EmitTokenWithComment(SyntaxKind.CaseKeyword, node.Pos, WriteKeyword, node);
        WriteSpace();
        EmitExpression(node.Expression, RuleDisallowedComma);

        EmitCaseOrDefaultClauseRest(node, node.Statements!, node.Expression!.End);
    }

    /// <summary>ported from: src/compiler/emitter.ts:4003-4006 — <c>emitDefaultClause</c>.</summary>
    private void EmitDefaultClause(DefaultClause node)
    {
        var pos = EmitTokenWithComment(SyntaxKind.DefaultKeyword, node.Pos, WriteKeyword, node);
        EmitCaseOrDefaultClauseRest(node, node.Statements!, pos);
    }

    /// <summary>ported from: src/compiler/emitter.ts:4008-4028 — <c>emitCaseOrDefaultClauseRest</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:4008 — TS's <c>statements: NodeArray&lt;Statement&gt;</c> is the port's
    /// <c>NodeArray&lt;IStatement&gt;</c> (the generated model types a statement array by the interface), so the two
    /// element reads that follow a text range or a range test go through a <c>(Node)</c> cast.
    /// </remarks>
    private void EmitCaseOrDefaultClauseRest(Node parentNode, NodeArray<IStatement> statements, int colonPos)
    {
        var emitAsSingleStatement = statements.Count == 1
            && (
                // treat synthesized nodes as located on the same line for emit purposes
                _currentSourceFile is null
                || Utilities.NodeIsSynthesized(parentNode)
                || Utilities.NodeIsSynthesized((Node)statements[0])
                || RangeStartPositionsAreOnSameLine(parentNode, (Node)statements[0], _currentSourceFile)
            );

        var format = ListFormat.CaseOrDefaultClauseStatements;
        if (emitAsSingleStatement)
        {
            WriteToken(SyntaxKind.ColonToken, colonPos, WritePunctuation, parentNode);
            WriteSpace();
            format &= ~(ListFormat.MultiLine | ListFormat.Indented);
        }
        else
        {
            EmitTokenWithComment(SyntaxKind.ColonToken, colonPos, WritePunctuation, parentNode);
        }

        EmitList(parentNode, statements, format);
    }

    /// <summary>ported from: src/compiler/emitter.ts:4030-4035 — <c>emitHeritageClause</c>.</summary>
    private void EmitHeritageClause(HeritageClause node)
    {
        WriteSpace();
        WriteTokenText(node.Token, WriteKeyword);
        WriteSpace();
        EmitList(node, node.Types, ListFormat.HeritageClauseTypes);
    }

    /// <summary>ported from: src/compiler/emitter.ts:4037-4048 — <c>emitCatchClause</c>.</summary>
    private void EmitCatchClause(CatchClause node)
    {
        var openParenPos = EmitTokenWithComment(SyntaxKind.CatchKeyword, node.Pos, WriteKeyword, node);
        WriteSpace();
        if (node.VariableDeclaration is not null)
        {
            EmitTokenWithComment(SyntaxKind.OpenParenToken, openParenPos, WritePunctuation, node);
            Emit(node.VariableDeclaration);
            EmitTokenWithComment(SyntaxKind.CloseParenToken, node.VariableDeclaration.End, WritePunctuation, node);
            WriteSpace();
        }

        Emit(node.Block);
    }

    //
    // Property assignments
    //

    /// <summary>ported from: src/compiler/emitter.ts:4054-4070 — <c>emitPropertyAssignment</c>.</summary>
    private void EmitPropertyAssignment(PropertyAssignment node)
    {
        Emit(node.Name);
        WritePunctuation(":");
        WriteSpace();
        // This is to ensure that we emit comment in the following case:
        //      For example:
        //          obj = {
        //              id: /*comment1*/ ()=>void
        //          }
        // "comment1" is not considered to be leading comment for node.initializer
        // but rather a trailing comment on the previous node.
        var initializer = node.Initializer!;
        if ((Utilities.GetEmitFlags(initializer) & EmitFlags.NoLeadingComments) == 0)
        {
            var commentRange = EmitNodes.GetCommentRange(initializer);
            EmitTrailingCommentsOfPosition(commentRange.Pos);
        }

        EmitExpression(initializer, RuleDisallowedComma);
    }

    /// <summary>ported from: src/compiler/emitter.ts:4072-4080 — <c>emitShorthandPropertyAssignment</c>.</summary>
    private void EmitShorthandPropertyAssignment(ShorthandPropertyAssignment node)
    {
        Emit(node.Name);
        if (node.ObjectAssignmentInitializer is not null)
        {
            WriteSpace();
            WritePunctuation("=");
            WriteSpace();
            EmitExpression(node.ObjectAssignmentInitializer, RuleDisallowedComma);
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:4082-4087 — <c>emitSpreadAssignment</c>.</summary>
    private void EmitSpreadAssignment(SpreadAssignment node)
    {
        if (node.Expression is not null)
        {
            EmitTokenWithComment(SyntaxKind.DotDotDotToken, node.Pos, WritePunctuation, node);
            EmitExpression(node.Expression, RuleDisallowedComma);
        }
    }

    //
    // Enum
    //

    /// <summary>ported from: src/compiler/emitter.ts:4093-4096 — <c>emitEnumMember</c>.</summary>
    private void EmitEnumMember(EnumMember node)
    {
        Emit(node.Name);
        EmitInitializer(node.Initializer, node.Name!.End, node, RuleDisallowedComma);
    }

    //
    // JSDoc
    //

    /// <summary>ported from: src/compiler/emitter.ts:4103-4138 — <c>emitJSDoc</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:4106 — the reference's <c>text.split(/\r\n?|\n/)</c> is stated as the port's split (the
    /// same three line terminators, longest first) because <c>Regex.Split</c> is banned by the repository's analyzers;
    /// and <c>if (text)</c> is the truthiness test a nullable string cannot spell, so the port tests for an empty
    /// string as well as null.
    /// </remarks>
    private void EmitJSDoc(JSDoc node)
    {
        Write("/**");
        if (node.Comment is not null)
        {
            var text = Utilities.GetTextOfJSDocComment(node.Comment);
            if (!string.IsNullOrEmpty(text))
            {
                var lines = text!.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
                foreach (var line in lines)
                {
                    WriteLine();
                    WriteSpace();
                    WritePunctuation("*");
                    WriteSpace();
                    Write(line);
                }
            }
        }

        if (node.Tags is not null)
        {
            if (node.Tags.Count == 1 && node.Tags[0].Kind == SyntaxKind.JSDocTypeTag && node.Comment is null)
            {
                WriteSpace();
                Emit(node.Tags[0]);
            }
            else
            {
                EmitList(node, node.Tags, ListFormat.JSDocComment);
            }
        }

        WriteSpace();
        Write("*/");
    }

    /// <summary>ported from: src/compiler/emitter.ts:4140-4144 — <c>emitJSDocSimpleTypedTag</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:4140 — the reference's parameter is the union <c>JSDocTypeTag | JSDocThisTag | JSDocEnumTag
    /// | JSDocReturnTag | JSDocThrowsTag | JSDocSatisfiesTag</c>, which has no C# name; the port takes the upper bound
    /// and reads the one member the union does not share (they all derive from <c>JSDocTag</c>, but each declares its
    /// own <c>typeExpression</c>) through the dispatch below.
    /// </remarks>
    private void EmitJSDocSimpleTypedTag(Node node)
    {
        var tag = (JSDocTag)node;
        EmitJSDocTagName(tag.TagName!);
        EmitJSDocTypeExpression(node switch
        {
            JSDocTypeTag typeTag => typeTag.TypeExpression,
            JSDocThisTag thisTag => thisTag.TypeExpression,
            JSDocEnumTag enumTag => enumTag.TypeExpression,
            JSDocReturnTag returnTag => returnTag.TypeExpression,
            JSDocThrowsTag throwsTag => throwsTag.TypeExpression,
            JSDocSatisfiesTag satisfiesTag => satisfiesTag.TypeExpression,
            _ => null,
        });
        EmitJSDocComment(tag.Comment);
    }

    /// <summary>ported from: src/compiler/emitter.ts:4146-4150 — <c>emitJSDocSeeTag</c>.</summary>
    private void EmitJSDocSeeTag(JSDocSeeTag tag)
    {
        EmitJSDocTagName(tag.TagName!);
        Emit(tag.Name);
        EmitJSDocComment(tag.Comment);
    }

    /// <summary>ported from: src/compiler/emitter.ts:4152-4166 — <c>emitJSDocImportTag</c>.</summary>
    private void EmitJSDocImportTag(JSDocImportTag tag)
    {
        EmitJSDocTagName(tag.TagName!);
        WriteSpace();

        if (tag.ImportClause is not null)
        {
            Emit(tag.ImportClause);
            WriteSpace();

            EmitTokenWithComment(SyntaxKind.FromKeyword, tag.ImportClause.End, WriteKeyword, tag);
            WriteSpace();
        }

        EmitExpression(tag.ModuleSpecifier);
        if (tag.Attributes is not null)
        {
            EmitWithLeadingSpace(tag.Attributes);
        }

        EmitJSDocComment(tag.Comment);
    }

    /// <summary>ported from: src/compiler/emitter.ts:4168-4172 — <c>emitJSDocNameReference</c>.</summary>
    private void EmitJSDocNameReference(JSDocNameReference node)
    {
        WriteSpace();
        WritePunctuation("{");
        Emit(node.Name);
        WritePunctuation("}");
    }

    /// <summary>ported from: src/compiler/emitter.ts:4174-4180 — <c>emitJSDocHeritageTag</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:4174 — the reference's parameter is the union <c>JSDocImplementsTag | JSDocAugmentsTag</c>;
    /// the port takes the upper bound and reads the <c>class</c> member each of the two declares (from the shared
    /// <c>JSDocTag</c> base for the tag name and comment).
    /// </remarks>
    private void EmitJSDocHeritageTag(Node node)
    {
        var tag = (JSDocTag)node;
        EmitJSDocTagName(tag.TagName!);
        WriteSpace();
        WritePunctuation("{");
        Emit(node switch
        {
            JSDocImplementsTag implementsTag => implementsTag.Class,
            JSDocAugmentsTag augmentsTag => augmentsTag.Class,
            _ => null,
        });
        WritePunctuation("}");
        EmitJSDocComment(tag.Comment);
    }

    /// <summary>ported from: src/compiler/emitter.ts:4182-4188 — <c>emitJSDocTemplateTag</c>.</summary>
    private void EmitJSDocTemplateTag(JSDocTemplateTag tag)
    {
        EmitJSDocTagName(tag.TagName!);
        EmitJSDocTypeExpression(tag.Constraint);
        WriteSpace();
        EmitList(tag, tag.TypeParameters, ListFormat.CommaListElements);
        EmitJSDocComment(tag.Comment);
    }

    /// <summary>ported from: src/compiler/emitter.ts:4190-4210 — <c>emitJSDocTypedefTag</c>.</summary>
    private void EmitJSDocTypedefTag(JSDocTypedefTag tag)
    {
        EmitJSDocTagName(tag.TagName!);
        if (tag.TypeExpression is not null)
        {
            if (tag.TypeExpression.Kind == SyntaxKind.JSDocTypeExpression)
            {
                EmitJSDocTypeExpression((JSDocTypeExpression)tag.TypeExpression);
            }
            else
            {
                WriteSpace();
                WritePunctuation("{");
                Write("Object");
                if (((JSDocTypeLiteral)tag.TypeExpression).IsArrayType)
                {
                    WritePunctuation("[");
                    WritePunctuation("]");
                }

                WritePunctuation("}");
            }
        }

        if (tag.FullName is not null)
        {
            WriteSpace();
            Emit(tag.FullName);
        }

        EmitJSDocComment(tag.Comment);
        if (tag.TypeExpression is not null && tag.TypeExpression.Kind == SyntaxKind.JSDocTypeLiteral)
        {
            EmitJSDocTypeLiteral((JSDocTypeLiteral)tag.TypeExpression);
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:4212-4220 — <c>emitJSDocCallbackTag</c>.</summary>
    private void EmitJSDocCallbackTag(JSDocCallbackTag tag)
    {
        EmitJSDocTagName(tag.TagName!);
        if (tag.Name is not null)
        {
            WriteSpace();
            Emit(tag.Name);
        }

        EmitJSDocComment(tag.Comment);
        EmitJSDocSignature(tag.TypeExpression!);
    }

    /// <summary>ported from: src/compiler/emitter.ts:4222-4225 — <c>emitJSDocOverloadTag</c>.</summary>
    private void EmitJSDocOverloadTag(JSDocOverloadTag tag)
    {
        EmitJSDocComment(tag.Comment);
        EmitJSDocSignature(tag.TypeExpression!);
    }

    /// <summary>ported from: src/compiler/emitter.ts:4227-4230 — <c>emitJSDocSimpleTag</c>.</summary>
    private void EmitJSDocSimpleTag(JSDocTag tag)
    {
        EmitJSDocTagName(tag.TagName!);
        EmitJSDocComment(tag.Comment);
    }

    /// <summary>ported from: src/compiler/emitter.ts:4232-4234 — <c>emitJSDocTypeLiteral</c>.</summary>
    private void EmitJSDocTypeLiteral(JSDocTypeLiteral lit)
    {
        EmitList(lit, NodeFactory.Factory.CreateNodeArray(lit.JsDocPropertyTags, null), ListFormat.JSDocComment);
    }

    /// <summary>ported from: src/compiler/emitter.ts:4236-4253 — <c>emitJSDocSignature</c>.</summary>
    private void EmitJSDocSignature(JSDocSignature sig)
    {
        if (sig.TypeParameters is not null)
        {
            EmitList(sig, NodeFactory.Factory.CreateNodeArray(sig.TypeParameters, null), ListFormat.JSDocComment);
        }

        if (sig.Parameters is not null)
        {
            EmitList(sig, NodeFactory.Factory.CreateNodeArray(sig.Parameters, null), ListFormat.JSDocComment);
        }

        if (sig.Type is not null)
        {
            WriteLine();
            WriteSpace();
            WritePunctuation("*");
            WriteSpace();
            Emit(sig.Type);
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:4255-4269 — <c>emitJSDocPropertyLikeTag</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:4255 — the reference's parameter is the union <c>JSDocParameterTag | JSDocPropertyTag</c>,
    /// whose port is the abstract class both derive from; the upper bound is taken because the call site supplies a
    /// <c>Node</c>.
    /// </remarks>
    private void EmitJSDocPropertyLikeTag(Node node)
    {
        var param = (JSDocPropertyLikeTag)node;
        EmitJSDocTagName(param.TagName!);
        EmitJSDocTypeExpression(param.TypeExpression);
        WriteSpace();
        if (param.IsBracketed)
        {
            WritePunctuation("[");
        }

        Emit(param.Name);
        if (param.IsBracketed)
        {
            WritePunctuation("]");
        }

        EmitJSDocComment(param.Comment);
    }

    /// <summary>ported from: src/compiler/emitter.ts:4271-4274 — <c>emitJSDocTagName</c>.</summary>
    private void EmitJSDocTagName(Identifier tagName)
    {
        WritePunctuation("@");
        Emit(tagName);
    }

    /// <summary>ported from: src/compiler/emitter.ts:4276-4282 — <c>emitJSDocComment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:4276 — the reference's parameter is <c>string | NodeArray&lt;JSDocComment&gt; | undefined</c>;
    /// the port's <c>JSDocTag.Comment</c> slot is <c>object?</c> and <c>getTextOfJSDocComment</c> takes that bound, so the
    /// parameter states the same upper bound. <c>if (text)</c> becomes the empty-or-null test.
    /// </remarks>
    private void EmitJSDocComment(object? comment)
    {
        var text = Utilities.GetTextOfJSDocComment(comment);
        if (!string.IsNullOrEmpty(text))
        {
            WriteSpace();
            Write(text);
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:4284-4290 — <c>emitJSDocTypeExpression</c>.</summary>
    private void EmitJSDocTypeExpression(JSDocTypeExpression? typeExpression)
    {
        if (typeExpression is not null)
        {
            WriteSpace();
            WritePunctuation("{");
            Emit(typeExpression.Type);
            WritePunctuation("}");
        }
    }

    //
    // Top-level nodes
    //

    /// <summary>ported from: src/compiler/emitter.ts:4292-4306 — <c>emitSourceFile</c>.</summary>
    private void EmitSourceFile(SourceFile node)
    {
        WriteLine();
        var statements = node.Statements!;
        // Emit detached comment if there are no prologue directives or if the first node is synthesized.
        // The synthesized node will have no leading comment so some comments may be missed.
        var shouldEmitDetachedComment = statements.Count == 0
            || !Utilities.IsPrologueDirective((Node)statements[0])
            || Utilities.NodeIsSynthesized((Node)statements[0]);
        if (shouldEmitDetachedComment)
        {
            EmitBodyWithDetachedComments(node, statements, EmitSourceFileWorker);
            return;
        }

        EmitSourceFileWorker(node);
    }

    /// <summary>ported from: src/compiler/emitter.ts:4308-4310 — <c>emitSyntheticTripleSlashReferencesIfNeeded</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:4308 — the reference's parameter is <c>Bundle</c> while its callers hold
    /// <c>Bundle | SourceFile</c>; the port takes the upper bound (PrinterCore's call site supplies a <c>Node</c>) and
    /// casts here.
    /// </remarks>
    private void EmitSyntheticTripleSlashReferencesIfNeeded(Node node)
    {
        var bundle = (Bundle)node;
        EmitTripleSlashDirectives(
            bundle.SyntheticFileReferences ?? [],
            bundle.SyntheticTypeReferences ?? [],
            bundle.SyntheticLibReferences ?? []);
    }

    /// <summary>ported from: src/compiler/emitter.ts:4312-4314 — <c>emitTripleSlashDirectivesIfNeeded</c>.</summary>
    private void EmitTripleSlashDirectivesIfNeeded(SourceFile node)
    {
        if (node.IsDeclarationFile)
        {
            EmitTripleSlashDirectives(node.ReferencedFiles!, node.TypeReferenceDirectives!, node.LibReferenceDirectives!);
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:4316-4344 — <c>emitTripleSlashDirectives</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:4331-4333 — <c>directive.resolutionMode</c> / <c>directive.preserve</c> are the port's
    /// <c>object?</c>/<c>bool?</c> slots, so the two truthiness tests are <c>Js.IsTruthy</c> (a boxed
    /// <c>ModuleKind.None</c> is 0 and therefore falsy, exactly as the reference reads it).
    /// </remarks>
    private void EmitTripleSlashDirectives(IList<IFileReference> files, IList<IFileReference> types, IList<IFileReference> libs)
    {
        if (_currentSourceFile is not null && Js.IsTruthy(_currentSourceFile.ModuleName))
        {
            WriteComment($"/// <amd-module name=\"{_currentSourceFile.ModuleName}\" />");
            WriteLine();
        }

        if (_currentSourceFile is not null && _currentSourceFile.AmdDependencies is not null)
        {
            foreach (var dep in _currentSourceFile.AmdDependencies)
            {
                if (Js.IsTruthy(dep.Name))
                {
                    WriteComment($"/// <amd-dependency name=\"{dep.Name}\" path=\"{dep.Path}\" />");
                }
                else
                {
                    WriteComment($"/// <amd-dependency path=\"{dep.Path}\" />");
                }

                WriteLine();
            }
        }

        void WriteDirectives(string kind, IList<IFileReference> directives)
        {
            foreach (var directive in directives)
            {
                var resolutionMode = Js.IsTruthy(directive.ResolutionMode)
                    ? $"resolution-mode=\"{(directive.ResolutionMode is ModuleKind.ESNext ? "import" : "require")}\" "
                    : "";
                var preserve = Js.IsTruthy(directive.Preserve) ? "preserve=\"true\" " : "";
                WriteComment($"/// <reference {kind}=\"{directive.FileName}\" {resolutionMode}{preserve}/>");
                WriteLine();
            }
        }

        WriteDirectives("path", files);
        WriteDirectives("types", types);
        WriteDirectives("lib", libs);
    }

    /// <summary>ported from: src/compiler/emitter.ts:4346-4357 — <c>emitSourceFileWorker</c>.</summary>
    private void EmitSourceFileWorker(SourceFile node)
    {
        var statements = node.Statements!;
        PushNameGenerationScope(node);
        Core.ForEach(statements, (IStatement statement, int _) =>
        {
            GenerateNames((Node)statement);
            return (object?)null;
        });
        EmitHelpers(node);
        var index = Core.FindIndex(statements, (IStatement statement, int _) => !Utilities.IsPrologueDirective((Node)statement));
        EmitTripleSlashDirectivesIfNeeded(node);
        EmitList(node, statements, ListFormat.MultiLine, parenthesizerRule: null, start: index == -1 ? statements.Count : index);
        PopNameGenerationScope(node);
    }

    // Transformation nodes

    /// <summary>ported from: src/compiler/emitter.ts:4359-4368 — <c>emitPartiallyEmittedExpression</c>.</summary>
    private void EmitPartiallyEmittedExpression(PartiallyEmittedExpression node)
    {
        var expression = node.Expression!;
        var emitFlags = Utilities.GetEmitFlags(node);
        if ((emitFlags & EmitFlags.NoLeadingComments) == 0 && node.Pos != expression.Pos)
        {
            EmitTrailingCommentsOfPosition(expression.Pos);
        }

        EmitExpression(expression);
        if ((emitFlags & EmitFlags.NoTrailingComments) == 0 && node.End != expression.End)
        {
            EmitLeadingCommentsOfPosition(expression.End);
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:4370-4372 — <c>emitCommaList</c>.</summary>
    private void EmitCommaList(CommaListExpression node)
    {
        EmitExpressionList(node, node.Elements, ListFormat.CommaListElements, parenthesizerRule: null);
    }

    /// <summary>ported from: src/compiler/emitter.ts:4374-4401 — <c>emitPrologueDirectives</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:4374 — the reference's <c>statements: readonly Node[]</c> is narrowed to
    /// <c>IList&lt;IStatement&gt;</c> because both call sites pass a statement list and the port's arrays are invariant;
    /// the reference's <c>statement.expression.text</c> is the <c>PrologueDirective</c> narrowing (an
    /// <c>ExpressionStatement</c> whose expression is a <c>StringLiteral</c>), which the port states as the cast below.
    /// </remarks>
    private int EmitPrologueDirectives(IList<IStatement> statements, SourceFile? sourceFile = null, HashSet<string>? seenPrologueDirectives = null)
    {
        var needsToSetSourceFile = sourceFile is not null;
        for (var i = 0; i < statements.Count; i++)
        {
            var statement = statements[i];
            if (Utilities.IsPrologueDirective((Node)statement))
            {
                var text = ((StringLiteral)((ExpressionStatement)statement).Expression!).Text;
                var shouldEmitPrologueDirective = seenPrologueDirectives is null || !seenPrologueDirectives.Contains(text!);
                if (shouldEmitPrologueDirective)
                {
                    if (needsToSetSourceFile)
                    {
                        needsToSetSourceFile = false;
                        SetSourceFile(sourceFile);
                    }

                    WriteLine();
                    Emit((Node)statement);
                    if (seenPrologueDirectives is not null)
                    {
                        seenPrologueDirectives.Add(text!);
                    }
                }
            }
            else
            {
                // return index of the first non prologue directive
                return i;
            }
        }

        return statements.Count;
    }

    /// <summary>ported from: src/compiler/emitter.ts:4403-4415 — <c>emitPrologueDirectivesIfNeeded</c>.</summary>
    private void EmitPrologueDirectivesIfNeeded(Node sourceFileOrBundle)
    {
        if (NodeTests.IsSourceFile(sourceFileOrBundle))
        {
            var sourceFile = (SourceFile)sourceFileOrBundle;
            EmitPrologueDirectives(sourceFile.Statements!, sourceFile);
        }
        else
        {
            var seenPrologueDirectives = new HashSet<string>();
            foreach (var sourceFile in ((Bundle)sourceFileOrBundle).SourceFiles!)
            {
                EmitPrologueDirectives(sourceFile.Statements!, sourceFile, seenPrologueDirectives);
            }

            SetSourceFile(null);
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:4417-4438 — <c>emitShebangIfNeeded</c>.</summary>
    private bool EmitShebangIfNeeded(Node sourceFileOrBundle)
    {
        if (NodeTests.IsSourceFile(sourceFileOrBundle))
        {
            var shebang = ScannerUtilities.GetShebang(((SourceFile)sourceFileOrBundle).Text!);
            if (Js.IsTruthy(shebang))
            {
                WriteComment(shebang);
                WriteLine();
                return true;
            }
        }
        else
        {
            foreach (var sourceFile in ((Bundle)sourceFileOrBundle).SourceFiles!)
            {
                // Emit only the first encountered shebang
                if (EmitShebangIfNeeded(sourceFile))
                {
                    return true;
                }
            }
        }

        return false;
    }

    //
    // Helpers
    //

    /// <summary>ported from: src/compiler/emitter.ts:4441-4448 — <c>emitNodeWithWriter</c>.</summary>
    private void EmitNodeWithWriter(Node? node, Action<string?> writer)
    {
        if (node is null) return;
        var savedWrite = _write;
        _write = writer;
        Emit(node);
        _write = savedWrite;
    }

    /// <summary>ported from: src/compiler/emitter.ts:4449-4515 — <c>emitDecoratorsAndModifiers</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:4449 — the reference's <c>modifiers: NodeArray&lt;ModifierLike&gt;</c> and the two casts
    /// to <c>NodeArray&lt;Modifier&gt;</c>/<c>NodeArray&lt;Decorator&gt;</c> inside it have no C# counterpart (the model's
    /// modifier arrays are <c>NodeArray&lt;Node&gt;</c> or <c>NodeArray&lt;ModifierToken&gt;</c> and the port's arrays are
    /// invariant), so the parameter is generic over the element type and the same test decides each branch; the two
    /// mode strings become <see cref="ModifierEmitMode"/>, which is what TS's string comparison distinguishes.
    /// </remarks>
    private int EmitDecoratorsAndModifiers<T>(Node node, NodeArray<T>? modifiers, bool allowDecorators)
        where T : Node
    {
        if (modifiers is { Count: > 0 })
        {
            if (Core.Every(modifiers, (T modifier, int _) => Utilities.IsModifier(modifier)))
            {
                // if all modifier-likes are `Modifier`, simply emit the array as modifiers.
                return EmitModifierList(node, modifiers);
            }

            if (Core.Every(modifiers, (T modifier, int _) => NodeTests.IsDecorator(modifier)))
            {
                if (allowDecorators)
                {
                    // if all modifier-likes are `Decorator`, simply emit the array as decorators.
                    return EmitDecoratorList(node, modifiers);
                }

                return node.Pos;
            }

            OnBeforeEmitNodeArray(modifiers);

            // partition modifiers into contiguous chunks of `Modifier` or `Decorator`
            ModifierEmitMode? lastMode = null;
            ModifierEmitMode? mode = null;
            var start = 0;
            var pos = 0;
            T? lastModifier = null;
            while (start < modifiers.Count)
            {
                while (pos < modifiers.Count)
                {
                    lastModifier = modifiers[pos];
                    mode = NodeTests.IsDecorator(lastModifier) ? ModifierEmitMode.Decorators : ModifierEmitMode.Modifiers;
                    if (lastMode is null)
                    {
                        lastMode = mode;
                    }
                    else if (mode != lastMode)
                    {
                        break;
                    }

                    pos++;
                }

                var textRange = new TextRange { Pos = -1, End = -1 };
                if (start == 0) textRange.Pos = modifiers.Pos;
                if (pos == modifiers.Count - 1) textRange.End = modifiers.End;
                if (lastMode == ModifierEmitMode.Modifiers || allowDecorators)
                {
                    EmitNodeListItems(
                        (Node child, Func<Node, Node>? rule) => Emit(child, rule),
                        node,
                        modifiers,
                        lastMode == ModifierEmitMode.Modifiers ? ListFormat.Modifiers : ListFormat.Decorators,
                        /*parenthesizerRule*/ null,
                        /*parenthesizerRuleSelector*/ null,
                        start,
                        pos - start,
                        /*hasTrailingComma*/ false,
                        textRange);
                }

                start = pos;
                lastMode = mode;
                pos++;
            }

            OnAfterEmitNodeArray(modifiers);

            if (lastModifier is not null && !ScannerUtilities.PositionIsSynthesized(lastModifier.End))
            {
                return lastModifier.End;
            }
        }

        return node.Pos;
    }

    /// <summary>ported from: src/compiler/emitter.ts:4517-4521 — <c>emitModifierList</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:4517 — the reference's <c>NodeArray&lt;Modifier&gt;</c> is generic here for the same reason
    /// as <see cref="EmitDecoratorsAndModifiers{T}"/> (the callers hold <c>NodeArray&lt;ModifierToken&gt;</c> or
    /// <c>NodeArray&lt;Node&gt;</c>).
    /// </remarks>
    private int EmitModifierList<T>(Node node, NodeArray<T>? modifiers)
        where T : Node
    {
        EmitList(node, modifiers, ListFormat.Modifiers);
        var lastModifier = Core.LastOrUndefined(modifiers);
        return lastModifier is not null && !ScannerUtilities.PositionIsSynthesized(lastModifier.End) ? lastModifier.End : node.Pos;
    }

    /// <summary>ported from: src/compiler/emitter.ts:4523-4529 — <c>emitTypeAnnotation</c>.</summary>
    private void EmitTypeAnnotation(ITypeNode? node)
    {
        if (node is not null)
        {
            WritePunctuation(":");
            WriteSpace();
            Emit((Node)node);
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:4531-4538 — <c>emitInitializer</c>.</summary>
    private void EmitInitializer(Expression? node, int equalCommentStartPos, Node container, Func<Node, Node>? parenthesizerRule = null)
    {
        if (node is not null)
        {
            WriteSpace();
            EmitTokenWithComment(SyntaxKind.EqualsToken, equalCommentStartPos, WriteOperator, container);
            WriteSpace();
            EmitExpression(node, parenthesizerRule);
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:4540-4545 — <c>emitNodeWithPrefix</c>.</summary>
    private void EmitNodeWithPrefix<T>(string prefix, Action<string> prefixWriter, T? node, Action<T> emit)
        where T : Node
    {
        if (node is not null)
        {
            prefixWriter(prefix);
            emit(node);
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:4547-4552 — <c>emitWithLeadingSpace</c>.</summary>
    private void EmitWithLeadingSpace(Node? node)
    {
        if (node is not null)
        {
            WriteSpace();
            Emit(node);
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:4554-4559 — <c>emitExpressionWithLeadingSpace</c>.</summary>
    private void EmitExpressionWithLeadingSpace(Expression? node, Func<Node, Node>? parenthesizerRule = null)
    {
        if (node is not null)
        {
            WriteSpace();
            EmitExpression(node, parenthesizerRule);
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:4561-4566 — <c>emitWithTrailingSpace</c>.</summary>
    private void EmitWithTrailingSpace(Node? node)
    {
        if (node is not null)
        {
            Emit(node);
            WriteSpace();
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:4568-4584 — <c>emitEmbeddedStatement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:4568 — the reference's <c>node: Statement</c> is stated as the port's <c>IStatement</c> (the
    /// model's name for it, which every concrete statement class implements); the four reads and the emit go through the
    /// <c>(Node)</c> cast the port's model requires, and <c>preserveSourceNewlines &amp;&amp; !count</c> is the port's
    /// "no leading line terminator" test.
    /// </remarks>
    private void EmitEmbeddedStatement(Node parent, IStatement node)
    {
        if (NodeTests.IsBlock((Node)node)
            || (Utilities.GetEmitFlags(parent) & EmitFlags.SingleLine) != 0
            || (_preserveSourceNewlines == true && GetLeadingLineTerminatorCount(parent, (Node)node, ListFormat.None) == 0))
        {
            WriteSpace();
            Emit((Node)node);
        }
        else
        {
            WriteLine();
            IncreaseIndent();
            if (NodeTests.IsEmptyStatement((Node)node))
            {
                PipelineEmit(EmitHint.EmbeddedStatement, (Node)node);
            }
            else
            {
                Emit((Node)node);
            }

            DecreaseIndent();
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:4586-4590 — <c>emitDecoratorList</c>.</summary>
    /// <remarks>PORT-NOTE: generic for the same reason as <see cref="EmitModifierList{T}"/>.</remarks>
    private int EmitDecoratorList<T>(Node parentNode, NodeArray<T>? decorators)
        where T : Node
    {
        EmitList(parentNode, decorators, ListFormat.Decorators);
        var lastDecorator = Core.LastOrUndefined(decorators);
        return lastDecorator is not null && !ScannerUtilities.PositionIsSynthesized(lastDecorator.End) ? lastDecorator.End : parentNode.Pos;
    }

    /// <summary>ported from: src/compiler/emitter.ts:4592-4594 — <c>emitTypeArguments</c>.</summary>
    private void EmitTypeArguments(Node parentNode, NodeArray<ITypeNode>? typeArguments)
    {
        EmitList(parentNode, typeArguments, ListFormat.TypeArguments, _typeArgumentParenthesizerRuleSelector);
    }

    /// <summary>ported from: src/compiler/emitter.ts:4596-4605 — <c>emitTypeParameters</c>.</summary>
    private void EmitTypeParameters(Node parentNode, NodeArray<TypeParameterDeclaration>? typeParameters)
    {
        if (Utilities.IsFunctionLike(parentNode))
        {
            // Quick info uses type arguments in place of type parameters on instantiated signatures
            var typeArguments = ((ISignatureDeclarationBase)parentNode).TypeArguments;
            if (typeArguments is not null)
            {
                EmitTypeArguments(parentNode, typeArguments);
                return;
            }
        }

        EmitList(parentNode, typeParameters, ListFormat.TypeParameters | (NodeTests.IsArrowFunction(parentNode) ? ListFormat.AllowTrailingComma : ListFormat.None));
    }

    /// <summary>ported from: src/compiler/emitter.ts:4607-4609 — <c>emitParameters</c>.</summary>
    private void EmitParameters(Node parentNode, NodeArray<ParameterDeclaration>? parameters)
    {
        EmitList(parentNode, parameters, ListFormat.Parameters);
    }

    /// <summary>ported from: src/compiler/emitter.ts:4611-4625 — <c>canEmitSimpleArrowHead</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: emitter.ts:4611 — the reference's parameter is the union <c>FunctionTypeNode | ConstructorTypeNode |
    /// ArrowFunction</c> (no C# name) and its body narrows through <c>isArrowFunction</c>; the port states the upper bound
    /// and reads the arrow-only members after that test, which is what makes the reads safe (the short-circuit order is
    /// the reference's).
    /// </remarks>
    private static bool CanEmitSimpleArrowHead(Node parentNode, NodeArray<ParameterDeclaration>? parameters)
    {
        var parameter = Core.SingleOrUndefined(parameters);
        return parameter is not null
            && parameter.Pos == parentNode.Pos // may not have parsed tokens between parent and parameter
            && NodeTests.IsArrowFunction(parentNode) // only arrow functions may have simple arrow head
            && ((ArrowFunction)parentNode).Type is null // arrow function may not have return type annotation
            && !Core.Some(((ArrowFunction)parentNode).Modifiers) // parent may not have decorators or modifiers
            && !Core.Some(((ArrowFunction)parentNode).TypeParameters) // parent may not have type parameters
            && !Core.Some(parameter.Modifiers) // parameter may not have decorators or modifiers
            && parameter.DotDotDotToken is null // parameter may not be rest
            && parameter.QuestionToken is null // parameter may not be optional
            && parameter.Type is null // parameter may not have a type annotation
            && parameter.Initializer is null // parameter may not have an initializer
            && NodeTests.IsIdentifier(parameter.Name!); // parameter name must be identifier
    }

    /// <summary>ported from: src/compiler/emitter.ts:4627-4634 — <c>emitParametersForArrow</c>.</summary>
    private void EmitParametersForArrow(Node parentNode, NodeArray<ParameterDeclaration>? parameters)
    {
        if (CanEmitSimpleArrowHead(parentNode, parameters))
        {
            EmitList(parentNode, parameters, ListFormat.Parameters & ~ListFormat.Parenthesis);
        }
        else
        {
            EmitParameters(parentNode, parameters);
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:4636-4638 — <c>emitParametersForIndexSignature</c>.</summary>
    private void EmitParametersForIndexSignature(Node parentNode, NodeArray<ParameterDeclaration>? parameters)
    {
        EmitList(parentNode, parameters, ListFormat.IndexSignatureParameters);
    }

    //
    // Model bridges for the ranges above
    //

    /// <summary>The port of the union's <c>statements</c> member (emitter.ts:3041, 3653).</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>BlockLike</c> (types.ts:3366-3370) is the union <c>SourceFile | Block | ModuleBlock |
    /// CaseOrDefaultClause</c>, all four of which expose <c>statements</c>; the port has no name for the union, so the
    /// member is read through this dispatch — the same treatment the port gives the other inline unions.
    /// </remarks>
    private static NodeArray<IStatement>? StatementsOfBlockLike(Node node) => node switch
    {
        SourceFile sourceFile => sourceFile.Statements,
        Block block => block.Statements,
        ModuleBlock moduleBlock => moduleBlock.Statements,
        CaseClause caseClause => caseClause.Statements,
        DefaultClause defaultClause => defaultClause.Statements,
        _ => null,
    };

    /// <summary>The <c>isEmptyBlock</c> test of the union arms other than <c>Block</c> (emitter.ts:5228-5230).</summary>
    /// <remarks>
    /// PORT-NOTE: PrinterCore ports <c>isEmptyBlock</c> as <see cref="IsEmptyBlock(Block)"/> (its call site holds a
    /// <c>Block</c>), while the reference's parameter is the union above and this file's <c>ModuleBlock</c> call site
    /// needs the same test; the union's <c>statements</c> member is therefore read here.
    /// </remarks>
    private static bool IsEmptyBlockLike(Node block) => StatementsOfBlockLike(block)?.Count == 0;

    /// <summary>The port of the union's <c>expression</c> member (emitter.ts:3153-3162).</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>WhileStatement | DoStatement</c> parameter has no C# name; both arms expose <c>expression</c>,
    /// which is read here.
    /// </remarks>
    private static Expression? WhileClauseExpression(Node node) => node switch
    {
        WhileStatement whileStatement => whileStatement.Expression,
        DoStatement doStatement => doStatement.Expression,
        _ => Debug.Fail<Expression?>($"Unexpected node kind {node.Kind}"),
    };

    /// <summary>ported from: src/compiler/utilities.ts:7870-7872 — <c>moveRangePos</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>createRange(pos, end)</c> has no ported counterpart, so the range is constructed directly; the
    /// caller reads only <c>pos</c> off it.
    /// </remarks>
    private static ITextRange MoveRangePos(ITextRange range, int pos) => new TextRange { Pos = pos, End = range.End };

    /// <summary>ported from: src/compiler/utilities.ts:7879-7884 — <c>moveRangePastDecorators</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>node.modifiers</c> is read through <c>Utilities.ModifiersOf</c> — Main's ruling makes that the one
    /// home for the member (the model has no <c>HasModifiers</c> interface), and its answer is the list the reference's
    /// <c>findLast</c> walks.
    /// </remarks>
    private static ITextRange MoveRangePastDecorators(Node node)
    {
        var lastDecorator = Utilities.CanHaveModifiers(node)
            ? Utilities.ModifiersOf(node)?.LastOrDefault(NodeTests.IsDecorator)
            : null;
        return lastDecorator is not null && !ScannerUtilities.PositionIsSynthesized(lastDecorator.End)
            ? MoveRangePos(node, lastDecorator.End)
            : node;
    }

    /// <summary>ported from: src/compiler/utilities.ts:7891-7898 — <c>moveRangePastModifiers</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: this file's only caller (emitter.ts:3559) passes a class declaration or expression; the
    /// <c>node.name.pos</c> read of the first branch goes through <c>INamedDeclaration</c>, and the modifiers read through
    /// <see cref="MoveRangePastDecorators"/>'s note.
    /// </remarks>
    private static ITextRange MoveRangePastModifiers(Node node)
    {
        if (NodeTests.IsPropertyDeclaration(node) || NodeTests.IsMethodDeclaration(node))
        {
            return MoveRangePos(node, ((INamedDeclaration)node).Name!.Pos);
        }

        var lastModifier = Utilities.CanHaveModifiers(node) ? Utilities.ModifiersOf(node)?.LastOrDefault() : null;
        return lastModifier is not null && !ScannerUtilities.PositionIsSynthesized(lastModifier.End)
            ? MoveRangePos(node, lastModifier.End)
            : MoveRangePastDecorators(node);
    }

    /// <summary>ported from: src/compiler/utilities.ts:2661-2663 — <c>isVarAwaitUsing</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference declares this in <c>utilities.ts</c>; the printer's only use of the family
    /// (emitter.ts:3513-3518) is this file's <c>emitVariableDeclarationList</c>, so the three members no earlier slice
    /// landed live here beside their caller (<c>Utilities.IsVarConst</c> already exists and is used as-is).
    /// </remarks>
    private static bool IsVarAwaitUsing(Node node) => (Utilities.GetCombinedNodeFlags(node) & NodeFlags.BlockScoped) == NodeFlags.AwaitUsing;

    /// <summary>ported from: src/compiler/utilities.ts:2669-2671 — <c>isVarUsing</c>.</summary>
    private static bool IsVarUsing(Node node) => (Utilities.GetCombinedNodeFlags(node) & NodeFlags.BlockScoped) == NodeFlags.Using;

    /// <summary>ported from: src/compiler/utilities.ts:2696-2698 — <c>isLet</c>.</summary>
    private static bool IsLet(Node node) => (Utilities.GetCombinedNodeFlags(node) & NodeFlags.BlockScoped) == NodeFlags.Let;

    /// <summary>emitter.ts:4468-4498 — the reference's <c>"modifiers" | "decorators"</c> mode of the partition loop.</summary>
    /// <remarks>PORT-NOTE: TS spells the mode as a string literal union and compares it with <c>!==</c>; the port uses an
    /// enum, which is what that comparison distinguishes.</remarks>
    private enum ModifierEmitMode
    {
        Modifiers,
        Decorators,
    }
}
