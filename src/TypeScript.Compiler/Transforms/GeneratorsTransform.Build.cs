#nullable enable

using System.Globalization;

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/generators.ts:2742-3284 — the compiler for the intermediate representation:
// it walks the operations, groups them into labels (case clauses) and writes each one as JavaScript.
/// <summary>ported from: src/compiler/transformers/generators.ts:2742-3284 — the members of <c>transformGenerators</c>
/// that build the generator function body: <c>build</c>/<c>buildStatements</c>, the label flushers, the block
/// enter/leave bookkeeping, and the <c>write*</c> family.</summary>
internal sealed partial class GeneratorsTransformer
{
    // ---------------------------------------------------------------------------------------------
    // generators.ts:2742-2803 — build
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/generators.ts:2742-2775 — <c>build</c>: builds the generator
    /// function body.</summary>
    private Expression Build()
    {
        _blockIndex = 0;
        _labelNumber = 0;
        _labelNumbers = null;
        _lastOperationWasAbrupt = false;
        _lastOperationWasCompletion = false;
        _clauses = null;
        _statements = null;
        _exceptionBlockStack = null;
        _currentExceptionBlock = null;
        _withBlockStack = null;

        var buildResult = BuildStatements();
        return _emitHelpers.CreateGeneratorHelper(
            EmitNodes.SetEmitFlags(
                _factory.CreateFunctionExpression(
                    /*modifiers*/ null,
                    /*asteriskToken*/ null,
                    /*name*/ null,
                    /*typeParameters*/ null,
                    [_factory.CreateParameterDeclaration(/*modifiers*/ null, /*dotDotDotToken*/ null, _state, null, null, null)],
                    /*type*/ null,
                    _factory.CreateBlock(
                        buildResult,
                        /*multiLine*/ buildResult.Count > 0)),
                EmitFlags.ReuseTempVariableScope));
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:2777-2803 — <c>buildStatements</c>: builds the
    /// statements for the generator function body.</summary>
    private List<IStatement> BuildStatements()
    {
        if (_operations is not null)
        {
            for (var operationIndex = 0; operationIndex < _operations.Count; operationIndex++)
            {
                WriteOperation(operationIndex);
            }

            FlushFinalLabel(_operations.Count);
        }
        else
        {
            FlushFinalLabel(0);
        }

        if (_clauses is not null)
        {
            var labelExpression = _factory.CreatePropertyAccessExpression(_state, "label");
            var switchStatement = _factory.CreateSwitchStatement(labelExpression, _factory.CreateCaseBlock(_clauses));
            return [FactoryUtilities.StartOnNewLine(switchStatement)];
        }

        if (_statements is not null)
        {
            return _statements;
        }

        return [];
    }

    // ---------------------------------------------------------------------------------------------
    // generators.ts:2805-2934 — label flushing
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/generators.ts:2805-2818 — <c>flushLabel</c>: flushes the current
    /// label and advances to a new label.</summary>
    private void FlushLabel()
    {
        if (_statements is null)
        {
            return;
        }

        AppendLabel(/*markLabelEnd*/ !_lastOperationWasAbrupt);

        _lastOperationWasAbrupt = false;
        _lastOperationWasCompletion = false;
        _labelNumber++;
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:2820-2836 — <c>flushFinalLabel</c>: flushes the final
    /// label of the generator function body.</summary>
    private void FlushFinalLabel(int operationIndex)
    {
        if (IsFinalLabelReachable(operationIndex))
        {
            TryEnterLabel(operationIndex);
            _withBlockStack = null;
            WriteReturn(/*expression*/ null, /*operationLocation*/ null);
        }

        if (_statements is not null && _clauses is not null)
        {
            AppendLabel(/*markLabelEnd*/ false);
        }

        UpdateLabelExpressions();
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:2838-2867 — <c>isFinalLabelReachable</c>: tests
    /// whether the final label of the generator function body is reachable by user code.</summary>
    /// <remarks>PORT-NOTE: TS's <c>labelExpressions[label]</c> reads a hole for an index past the array's end; the port's
    /// bounds test is that read (a hole is <c>undefined</c>, which is falsy).</remarks>
    private bool IsFinalLabelReachable(int operationIndex)
    {
        // if the last operation was *not* a completion (return/throw) then
        // the final label is reachable.
        if (!_lastOperationWasCompletion)
        {
            return true;
        }

        // if there are no labels defined or referenced, then the final label is
        // not reachable.
        if (_labelOffsets is null || _labelExpressions is null)
        {
            return false;
        }

        // if the label for this offset is referenced, then the final label
        // is reachable.
        for (var label = 0; label < _labelOffsets.Count; label++)
        {
            if (_labelOffsets[label] == operationIndex && label < _labelExpressions.Count && _labelExpressions[label] is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:2869-2934 — <c>appendLabel</c>: appends a case
    /// clause for the last label and sets the new label.</summary>
    private void AppendLabel(bool markLabelEnd)
    {
        _clauses ??= [];

        if (_statements is not null)
        {
            if (_withBlockStack is not null)
            {
                // The previous label was nested inside one or more `with` blocks, so we
                // surround the statements in generated `with` blocks to create the same environment.
                for (var i = _withBlockStack.Count - 1; i >= 0; i--)
                {
                    var withBlock = _withBlockStack[i];
                    _statements = [_factory.CreateWithStatement(withBlock.Expression, _factory.CreateBlock(_statements, null))];
                }
            }

            if (_currentExceptionBlock is not null)
            {
                // The previous label was nested inside of an exception block, so we must
                // indicate entry into a protected region by pushing the label numbers
                // for each block in the protected region.
                var startLabel = _currentExceptionBlock.StartLabel;
                var catchLabel = _currentExceptionBlock.CatchLabel;
                var finallyLabel = _currentExceptionBlock.FinallyLabel;
                var endLabel = _currentExceptionBlock.EndLabel;
                _statements.Insert(0,
                    _factory.CreateExpressionStatement(
                        _factory.CreateCallExpression(
                            _factory.CreatePropertyAccessExpression(_factory.CreatePropertyAccessExpression(_state, "trys"), "push"),
                            /*typeArguments*/ null,
                            [
                                _factory.CreateArrayLiteralExpression([
                                    CreateLabel(startLabel),
                                    CreateLabel(catchLabel),
                                    CreateLabel(finallyLabel),
                                    CreateLabel(endLabel),
                                ], null),
                            ])));

                _currentExceptionBlock = null;
            }

            if (markLabelEnd)
            {
                // The case clause for the last label falls through to this label, so we
                // add an assignment statement to reflect the change in labels.
                _statements.Add(
                    _factory.CreateExpressionStatement(
                        _factory.CreateAssignment(
                            _factory.CreatePropertyAccessExpression(_state, "label"),
                            _factory.CreateNumericLiteral(_labelNumber + 1))));
            }
        }

        _clauses.Add(
            _factory.CreateCaseClause(
                _factory.CreateNumericLiteral(_labelNumber),
                _statements ?? []));

        _statements = null;
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:2936-2958 — <c>tryEnterLabel</c>: tries to enter into
    /// a new label at the current operation index.</summary>
    private void TryEnterLabel(int operationIndex)
    {
        if (_labelOffsets is null)
        {
            return;
        }

        for (var label = 0; label < _labelOffsets.Count; label++)
        {
            if (_labelOffsets[label] == operationIndex)
            {
                FlushLabel();
                _labelNumbers ??= [];
                while (_labelNumbers.Count <= _labelNumber) _labelNumbers.Add(null);
                (_labelNumbers[_labelNumber] ??= []).Add(label);
            }
        }
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:2960-2979 — <c>updateLabelExpressions</c>: updates
    /// literal expressions for labels with actual label numbers.</summary>
    private void UpdateLabelExpressions()
    {
        if (_labelExpressions is not null && _labelNumbers is not null)
        {
            for (var labelNumber = 0; labelNumber < _labelNumbers.Count; labelNumber++)
            {
                var labels = _labelNumbers[labelNumber];
                if (labels is not null)
                {
                    foreach (var label in labels)
                    {
                        if (label < _labelExpressions.Count && _labelExpressions[label] is { } expressions)
                        {
                            foreach (var expression in expressions)
                            {
                                expression.Text = labelNumber.ToString(CultureInfo.InvariantCulture);
                            }
                        }
                    }
                }
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // generators.ts:2981-3076 — block bookkeeping and the operation reader
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/generators.ts:2981-3025 — <c>tryEnterOrLeaveBlock</c>: tries to enter
    /// or leave a code block.</summary>
    /// <remarks>PORT-NOTE: TS's <c>exceptionBlockStack</c> is hole-filled and is pushed with <c>currentExceptionBlock!</c>,
    /// which is <c>undefined</c> for the outermost <c>try</c>; <c>List&lt;ExceptionBlock&gt;</c> cannot hold that, so the
    /// field is <c>List&lt;ExceptionBlock?&gt;</c> and the popped hole reads back as <c>null</c> exactly as
    /// <c>undefined</c> does there.</remarks>
    private void TryEnterOrLeaveBlock(int operationIndex)
    {
        if (_blocks is not null)
        {
            for (; _blockIndex < _blockActions!.Count && _blockOffsets![_blockIndex] <= operationIndex; _blockIndex++)
            {
                var block = _blocks[_blockIndex];
                var blockAction = _blockActions[_blockIndex];
                switch (block.Kind)
                {
                    case CodeBlockKind.Exception:
                        if (blockAction == BlockAction.Open)
                        {
                            _exceptionBlockStack ??= [];

                            _statements ??= [];

                            _exceptionBlockStack.Add(_currentExceptionBlock);
                            _currentExceptionBlock = (ExceptionBlock)block;
                        }
                        else if (blockAction == BlockAction.Close)
                        {
                            _currentExceptionBlock = Pop(_exceptionBlockStack);
                        }
                        break;
                    case CodeBlockKind.With:
                        if (blockAction == BlockAction.Open)
                        {
                            _withBlockStack ??= [];

                            _withBlockStack.Add((WithBlock)block);
                        }
                        else if (blockAction == BlockAction.Close)
                        {
                            Pop(_withBlockStack);
                        }
                        break;
                        // default: do nothing
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:3027-3076 — <c>writeOperation</c>: writes an
    /// operation as a statement to the current label's statement list.</summary>
    private void WriteOperation(int operationIndex)
    {
        TryEnterLabel(operationIndex);
        TryEnterOrLeaveBlock(operationIndex);

        // early termination, nothing else to process in this label
        if (_lastOperationWasAbrupt)
        {
            return;
        }

        _lastOperationWasAbrupt = false;
        _lastOperationWasCompletion = false;

        var opcode = _operations![operationIndex];
        if (opcode == OpCode.Nop)
        {
            return;
        }
        else if (opcode == OpCode.Endfinally)
        {
            WriteEndfinally();
            return;
        }

        var args = _operationArguments![operationIndex]!.Value;
        if (opcode == OpCode.Statement)
        {
            WriteStatement(args.Statement);
            return;
        }

        var location = _operationLocations![operationIndex];
        switch (opcode)
        {
            case OpCode.Assign:
                WriteAssign(args.Expression!, args.SecondExpression!, location);
                return;
            case OpCode.Break:
                WriteBreak(args.Label, location);
                return;
            case OpCode.BreakWhenTrue:
                WriteBreakWhenTrue(args.Label, args.Expression!, location);
                return;
            case OpCode.BreakWhenFalse:
                WriteBreakWhenFalse(args.Label, args.Expression!, location);
                return;
            case OpCode.Yield:
                WriteYield(args.Expression, location);
                return;
            case OpCode.YieldStar:
                WriteYieldStar(args.Expression!, location);
                return;
            case OpCode.Return:
                WriteReturn(args.Expression, location);
                return;
            case OpCode.Throw:
                WriteThrow(args.Expression!, location);
                return;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // generators.ts:3078-3284 — the statement writers
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/generators.ts:3078-3094 — <c>writeStatement</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is declared <c>Statement</c> although its body tests it, because
    /// <c>writeOperation</c> hands it a positional cast that may be <c>undefined</c>; the port states the optionality the
    /// reference's own test implies.</remarks>
    private void WriteStatement(IStatement? statement)
    {
        if (statement is not null)
        {
            if (_statements is null)
            {
                _statements = [statement];
            }
            else
            {
                _statements.Add(statement);
            }
        }
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:3096-3104 — <c>writeAssign</c>.</summary>
    private void WriteAssign(Expression left, Expression right, ITextRange? operationLocation)
        => WriteStatement(Utilities.SetTextRange(_factory.CreateExpressionStatement(_factory.CreateAssignment(left, right)), operationLocation));

    /// <summary>ported from: src/compiler/transformers/generators.ts:3106-3116 — <c>writeThrow</c>.</summary>
    private void WriteThrow(Expression expression, ITextRange? operationLocation)
    {
        _lastOperationWasAbrupt = true;
        _lastOperationWasCompletion = true;
        WriteStatement(Utilities.SetTextRange(_factory.CreateThrowStatement(expression), operationLocation));
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:3118-3142 — <c>writeReturn</c>.</summary>
    private void WriteReturn(Expression? expression, ITextRange? operationLocation)
    {
        _lastOperationWasAbrupt = true;
        _lastOperationWasCompletion = true;
        WriteStatement(
            EmitNodes.SetEmitFlags(
                Utilities.SetTextRange(
                    _factory.CreateReturnStatement(
                        _factory.CreateArrayLiteralExpression(
                            (List<Expression>)(expression is not null
                                ? [CreateInstruction(Instruction.Return), expression]
                                : [CreateInstruction(Instruction.Return)]),
                            null)),
                    operationLocation),
                EmitFlags.NoTokenSourceMaps));
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:3144-3167 — <c>writeBreak</c>.</summary>
    private void WriteBreak(int label, ITextRange? operationLocation)
    {
        _lastOperationWasAbrupt = true;
        WriteStatement(
            EmitNodes.SetEmitFlags(
                Utilities.SetTextRange(
                    _factory.CreateReturnStatement(
                        _factory.CreateArrayLiteralExpression([
                            CreateInstruction(Instruction.Break),
                            CreateLabel(label),
                        ], null)),
                    operationLocation),
                EmitFlags.NoTokenSourceMaps));
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:3169-3197 — <c>writeBreakWhenTrue</c>.</summary>
    private void WriteBreakWhenTrue(int label, Expression condition, ITextRange? operationLocation)
        => WriteStatement(
            EmitNodes.SetEmitFlags(
                _factory.CreateIfStatement(
                    condition,
                    EmitNodes.SetEmitFlags(
                        Utilities.SetTextRange(
                            _factory.CreateReturnStatement(
                                _factory.CreateArrayLiteralExpression([
                                    CreateInstruction(Instruction.Break),
                                    CreateLabel(label),
                                ], null)),
                            operationLocation),
                        EmitFlags.NoTokenSourceMaps),
                    /*elseStatement*/ null),
                EmitFlags.SingleLine));

    /// <summary>ported from: src/compiler/transformers/generators.ts:3199-3226 — <c>writeBreakWhenFalse</c>.</summary>
    private void WriteBreakWhenFalse(int label, Expression condition, ITextRange? operationLocation)
        => WriteStatement(
            EmitNodes.SetEmitFlags(
                _factory.CreateIfStatement(
                    _factory.CreateLogicalNot(condition),
                    EmitNodes.SetEmitFlags(
                        Utilities.SetTextRange(
                            _factory.CreateReturnStatement(
                                _factory.CreateArrayLiteralExpression([
                                    CreateInstruction(Instruction.Break),
                                    CreateLabel(label),
                                ], null)),
                            operationLocation),
                        EmitFlags.NoTokenSourceMaps),
                    /*elseStatement*/ null),
                EmitFlags.SingleLine));

    /// <summary>ported from: src/compiler/transformers/generators.ts:3228-3251 — <c>writeYield</c>.</summary>
    private void WriteYield(Expression? expression, ITextRange? operationLocation)
    {
        _lastOperationWasAbrupt = true;
        WriteStatement(
            EmitNodes.SetEmitFlags(
                Utilities.SetTextRange(
                    _factory.CreateReturnStatement(
                        _factory.CreateArrayLiteralExpression(
                            (List<Expression>)(expression is not null
                                ? [CreateInstruction(Instruction.Yield), expression]
                                : [CreateInstruction(Instruction.Yield)]),
                            null)),
                    operationLocation),
                EmitFlags.NoTokenSourceMaps));
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:3253-3272 — <c>writeYieldStar</c>.</summary>
    private void WriteYieldStar(Expression expression, ITextRange? operationLocation)
    {
        _lastOperationWasAbrupt = true;
        WriteStatement(
            EmitNodes.SetEmitFlags(
                Utilities.SetTextRange(
                    _factory.CreateReturnStatement(
                        _factory.CreateArrayLiteralExpression([
                            CreateInstruction(Instruction.YieldStar),
                            expression,
                        ], null)),
                    operationLocation),
                EmitFlags.NoTokenSourceMaps));
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:3274-3284 — <c>writeEndfinally</c>.</summary>
    private void WriteEndfinally()
    {
        _lastOperationWasAbrupt = true;
        WriteStatement(
            _factory.CreateReturnStatement(
                _factory.CreateArrayLiteralExpression([
                    CreateInstruction(Instruction.Endfinally),
                ], null)));
    }
}
