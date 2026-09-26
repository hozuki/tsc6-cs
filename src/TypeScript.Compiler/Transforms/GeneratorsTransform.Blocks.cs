#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/generators.ts:2109-2711 — labels, code blocks, break/continue targets, and
// the operations that are appended to the function body.
/// <summary>ported from: src/compiler/transformers/generators.ts:2109-2711 — the members of <c>transformGenerators</c>
/// that hand out labels (<c>defineLabel</c>/<c>markLabel</c>), open and close code blocks (the <c>begin*Block</c>/
/// <c>end*Block</c> family), resolve a <c>break</c>/<c>continue</c> to its label, build the inline completion nodes, and
/// append an operation (<c>emitWorker</c> and its wrappers).</summary>
internal sealed partial class GeneratorsTransformer
{
    /// <summary>ported from: <c>Number.MAX_SAFE_INTEGER</c>, read by <c>createLabel</c> (generators.ts:2534).</summary>
    /// <remarks>PORT-NOTE: the reference passes the JS number to <c>factory.createNumericLiteral</c>, whose ported form
    /// takes the text directly (<c>object?</c>). The value is only ever observed for a label that no
    /// operation marked (see <c>updateLabelExpressions</c>).</remarks>
    private const string MaxSafeIntegerText = "9007199254740991";

    // ---------------------------------------------------------------------------------------------
    // generators.ts:2109-2182 — labels and the block list
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/generators.ts:2109-2121 — <c>defineLabel</c>: defines a label,
    /// used as the target of a Break operation.</summary>
    private int DefineLabel()
    {
        _labelOffsets ??= [];

        var label = _nextLabelId;
        _nextLabelId++;
        SetLabelOffset(label, -1);
        return label;
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:2123-2131 — <c>markLabel</c>: marks the current
    /// operation with the specified label.</summary>
    private void MarkLabel(int label)
    {
        Debug.Assert(_labelOffsets is not null, "No labels were defined.");
        _labelOffsets![label] = _operations is not null ? _operations.Count : 0;
    }

    /// <summary>The sparse write both label arrays need.</summary>
    /// <remarks>PORT-NOTE: TS's <c>labelOffsets[label] = …</c> writes into a hole-filled array; <c>defineLabel</c> hands
    /// labels out in increasing order, so the port grows the list to the index and the holes read <c>-1</c>, which is the
    /// value <c>defineLabel</c> itself writes and which no reader ever compares successfully against an operation index
    /// (they are all <c>&gt;= 0</c>).</remarks>
    private void SetLabelOffset(int label, int offset)
    {
        var offsets = _labelOffsets!;
        while (offsets.Count <= label) offsets.Add(-1);
        offsets[label] = offset;
    }

    /// <summary>The sparse write <c>createLabel</c> needs for <c>labelExpressions</c>.</summary>
    /// <remarks>PORT-NOTE: as <see cref="SetLabelOffset"/>, for the array of literal nodes bound to a label.</remarks>
    private void AddLabelExpression(int label, NumericLiteral expression)
    {
        var labelExpressions = _labelExpressions!;
        while (labelExpressions.Count <= label) labelExpressions.Add(null);
        (labelExpressions[label] ??= []).Add(expression);
    }

    /// <summary>TS's <c>stack.pop()</c>.</summary>
    /// <remarks>PORT-NOTE: <c>List&lt;T&gt;.RemoveAt</c> answers nothing, so the three block stacks read the last slot
    /// and then remove it.</remarks>
    private static T? Pop<T>(List<T>? stack)
    {
        if (stack is null || stack.Count == 0) return default;
        var index = stack.Count - 1;
        var value = stack[index];
        stack.RemoveAt(index);
        return value;
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:2133-2150 — <c>beginBlock</c>.</summary>
    private int BeginBlock(CodeBlock block)
    {
        if (_blocks is null)
        {
            _blocks = [];
            _blockActions = [];
            _blockOffsets = [];
            _blockStack = [];
        }

        var index = _blockActions!.Count;
        _blockActions.Add(BlockAction.Open);
        _blockOffsets!.Add(_operations is not null ? _operations.Count : 0);
        _blocks.Add(block);
        _blockStack!.Add(block);
        return index;
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:2152-2165 — <c>endBlock</c>.</summary>
    private CodeBlock EndBlock()
    {
        var block = PeekBlock();
        if (block is null) return Debug.Fail<CodeBlock>("beginBlock was never called.");

        var index = _blockActions!.Count;
        _blockActions.Add(BlockAction.Close);
        _blockOffsets!.Add(_operations is not null ? _operations.Count : 0);
        _blocks!.Add(block);
        Pop(_blockStack);
        return block;
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:2167-2172 — <c>peekBlock</c>: gets the current open
    /// block.</summary>
    private CodeBlock? PeekBlock() => Core.LastOrUndefined(_blockStack);

    /// <summary>ported from: src/compiler/transformers/generators.ts:2174-2182 — <c>peekBlockKind</c>: gets the kind of
    /// the current open block.</summary>
    private CodeBlockKind? PeekBlockKind()
    {
        var block = PeekBlock();
        return block is not null ? block.Kind : null;
    }

    // ---------------------------------------------------------------------------------------------
    // generators.ts:2184-2206 — with blocks
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/generators.ts:2184-2197 — <c>beginWithBlock</c>.</summary>
    private void BeginWithBlock(Identifier expression)
    {
        var startLabel = DefineLabel();
        var endLabel = DefineLabel();
        MarkLabel(startLabel);
        BeginBlock(new WithBlock
        {
            Expression = expression,
            StartLabel = startLabel,
            EndLabel = endLabel,
        });
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:2199-2206 — <c>endWithBlock</c>.</summary>
    private void EndWithBlock()
    {
        Debug.Assert(PeekBlockKind() == CodeBlockKind.With);
        var block = (WithBlock)EndBlock();
        MarkLabel(block.EndLabel);
    }

    // ---------------------------------------------------------------------------------------------
    // generators.ts:2208-2306 — exception blocks
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/generators.ts:2208-2225 — <c>beginExceptionBlock</c>.</summary>
    private int BeginExceptionBlock()
    {
        var startLabel = DefineLabel();
        var endLabel = DefineLabel();
        MarkLabel(startLabel);
        BeginBlock(new ExceptionBlock
        {
            State = ExceptionBlockState.Try,
            StartLabel = startLabel,
            EndLabel = endLabel,
        });
        EmitNop();
        return endLabel;
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:2227-2266 — <c>beginCatchBlock</c>.</summary>
    private void BeginCatchBlock(VariableDeclaration variable)
    {
        Debug.Assert(PeekBlockKind() == CodeBlockKind.Exception);

        // generated identifiers should already be unique within a file
        Identifier name;
        if (Utilities.IsGeneratedIdentifier(variable.Name!))
        {
            name = (Identifier)variable.Name!;
            _hoistVariableDeclaration((Identifier)variable.Name!);
        }
        else
        {
            var text = Utilities.IdText(variable.Name!);
            name = DeclareLocal(text);
            if (_renamedCatchVariables is null)
            {
                _renamedCatchVariables = [];
                _renamedCatchVariableDeclarations = [];
                _context.EnableSubstitution(SyntaxKind.Identifier);
            }

            _renamedCatchVariables[text] = true;
            WriteRenamedCatchVariableDeclaration(TransformationUtilities.GetOriginalNodeId(variable), name);
        }

        var exception = (ExceptionBlock)PeekBlock()!;
        Debug.Assert(exception.State < ExceptionBlockState.Catch);

        var endLabel = exception.EndLabel;
        EmitBreak(endLabel);

        var catchLabel = DefineLabel();
        MarkLabel(catchLabel);
        exception.State = ExceptionBlockState.Catch;
        exception.CatchVariable = name;
        exception.CatchLabel = catchLabel;

        EmitAssignment(name, _factory.CreateCallExpression(_factory.CreatePropertyAccessExpression(_state, "sent"), /*typeArguments*/ null, []));
        EmitNop();
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:2268-2284 — <c>beginFinallyBlock</c>.</summary>
    private void BeginFinallyBlock()
    {
        Debug.Assert(PeekBlockKind() == CodeBlockKind.Exception);

        var exception = (ExceptionBlock)PeekBlock()!;
        Debug.Assert(exception.State < ExceptionBlockState.Finally);

        var endLabel = exception.EndLabel;
        EmitBreak(endLabel);

        var finallyLabel = DefineLabel();
        MarkLabel(finallyLabel);
        exception.State = ExceptionBlockState.Finally;
        exception.FinallyLabel = finallyLabel;
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:2286-2306 — <c>endExceptionBlock</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's local <c>const state = exception.state</c> shadows the factory's <c>state</c>
    /// identifier (the state object's name); the port names the local <c>exceptionState</c> since the field here is
    /// <see cref="_state"/> and the two are different things.</remarks>
    private void EndExceptionBlock()
    {
        Debug.Assert(PeekBlockKind() == CodeBlockKind.Exception);
        var exception = (ExceptionBlock)EndBlock();
        var exceptionState = exception.State;
        if (exceptionState < ExceptionBlockState.Finally)
        {
            EmitBreak(exception.EndLabel);
        }
        else
        {
            EmitEndfinally();
        }

        MarkLabel(exception.EndLabel);
        EmitNop();
        exception.State = ExceptionBlockState.Done;
    }

    // ---------------------------------------------------------------------------------------------
    // generators.ts:2308-2413 — loop, switch and labeled blocks
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/generators.ts:2308-2323 — <c>beginScriptLoopBlock</c>.</summary>
    private void BeginScriptLoopBlock()
        => BeginBlock(new LoopBlock
        {
            IsScript = true,
            BreakLabel = -1,
            ContinueLabel = -1,
        });

    /// <summary>ported from: src/compiler/transformers/generators.ts:2325-2338 — <c>beginLoopBlock</c>.</summary>
    private int BeginLoopBlock(int continueLabel)
    {
        var breakLabel = DefineLabel();
        BeginBlock(new LoopBlock
        {
            IsScript = false,
            BreakLabel = breakLabel,
            ContinueLabel = continueLabel,
        });
        return breakLabel;
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:2340-2351 — <c>endLoopBlock</c>.</summary>
    /// <remarks>PORT-NOTE (a deviation with the reference's own reference line): the reference writes
    /// <c>const block = endBlock() as SwitchBlock</c> (generators.ts:2342), which is a mistake — the block it just
    /// asserted is <c>CodeBlockKind.Loop</c>. The assertion one line above makes it harmless there (TS's two interfaces
    /// share the <c>isScript</c>/<c>breakLabel</c> slots this body reads) and it is unrepresentable in C#, where the cast
    /// would throw; the shared base <see cref="BreakableCodeBlock"/> carries exactly those two slots.</remarks>
    private void EndLoopBlock()
    {
        Debug.Assert(PeekBlockKind() == CodeBlockKind.Loop);
        var block = (BreakableCodeBlock)EndBlock();
        var breakLabel = block.BreakLabel;
        if (!block.IsScript)
        {
            MarkLabel(breakLabel);
        }
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:2353-2364 — <c>beginScriptSwitchBlock</c>.</summary>
    private void BeginScriptSwitchBlock()
        => BeginBlock(new SwitchBlock
        {
            IsScript = true,
            BreakLabel = -1,
        });

    /// <summary>ported from: src/compiler/transformers/generators.ts:2366-2377 — <c>beginSwitchBlock</c>.</summary>
    private int BeginSwitchBlock()
    {
        var breakLabel = DefineLabel();
        BeginBlock(new SwitchBlock
        {
            IsScript = false,
            BreakLabel = breakLabel,
        });
        return breakLabel;
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:2379-2386 — <c>endSwitchBlock</c>.</summary>
    private void EndSwitchBlock()
    {
        Debug.Assert(PeekBlockKind() == CodeBlockKind.Switch);
        var block = (SwitchBlock)EndBlock();
        var breakLabel = block.BreakLabel;
        if (!block.IsScript)
        {
            MarkLabel(breakLabel);
        }
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:2388-2395 — <c>beginScriptLabeledBlock</c>.</summary>
    private void BeginScriptLabeledBlock(string labelText)
        => BeginBlock(new LabeledBlock
        {
            IsScript = true,
            LabelText = labelText,
            BreakLabel = -1,
        });

    /// <summary>ported from: src/compiler/transformers/generators.ts:2397-2405 — <c>beginLabeledBlock</c>.</summary>
    private void BeginLabeledBlock(string labelText)
    {
        var breakLabel = DefineLabel();
        BeginBlock(new LabeledBlock
        {
            IsScript = false,
            LabelText = labelText,
            BreakLabel = breakLabel,
        });
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:2407-2413 — <c>endLabeledBlock</c>.</summary>
    private void EndLabeledBlock()
    {
        Debug.Assert(PeekBlockKind() == CodeBlockKind.Labeled);
        var block = (LabeledBlock)EndBlock();
        if (!block.IsScript)
        {
            MarkLabel(block.BreakLabel);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // generators.ts:2420-2519 — the target predicates and the searches
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/generators.ts:2420-2423 — <c>supportsUnlabeledBreak</c>.</summary>
    /// <remarks>PORT-NOTE: TS declares the three <c>supports*</c> members as type guards (<c>block is SwitchBlock |
    /// LoopBlock</c>); C# cannot narrow through a <c>Func&lt;CodeBlock, bool&gt;</c>, so they answer <c>bool</c> and the
    /// caller casts at the read — the same treatment <c>Utilities.CanHaveModifiers</c> records.</remarks>
    private static bool SupportsUnlabeledBreak(CodeBlock block)
        => block.Kind == CodeBlockKind.Switch
            || block.Kind == CodeBlockKind.Loop;

    /// <summary>ported from: src/compiler/transformers/generators.ts:2430-2433 — <c>supportsLabeledBreakOrContinue</c>.</summary>
    private static bool SupportsLabeledBreakOrContinue(CodeBlock block)
        => block.Kind == CodeBlockKind.Labeled;

    /// <summary>ported from: src/compiler/transformers/generators.ts:2439-2441 — <c>supportsUnlabeledContinue</c>.</summary>
    private static bool SupportsUnlabeledContinue(CodeBlock block)
        => block.Kind == CodeBlockKind.Loop;

    /// <summary>ported from: src/compiler/transformers/generators.ts:2443-2457 — <c>hasImmediateContainingLabeledBlock</c>.</summary>
    private bool HasImmediateContainingLabeledBlock(string labelText, int start)
    {
        for (var j = start; j >= 0; j--)
        {
            var containingBlock = _blockStack![j];
            if (SupportsLabeledBreakOrContinue(containingBlock))
            {
                if (((LabeledBlock)containingBlock).LabelText == labelText)
                {
                    return true;
                }
            }
            else
            {
                break;
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:2464-2492 — <c>findBreakTarget</c>.</summary>
    private int FindBreakTarget(string? labelText = null)
    {
        if (_blockStack is not null)
        {
            if (Js.IsTruthy(labelText))
            {
                for (var i = _blockStack.Count - 1; i >= 0; i--)
                {
                    var block = _blockStack[i];
                    if (SupportsLabeledBreakOrContinue(block) && ((LabeledBlock)block).LabelText == labelText)
                    {
                        return ((LabeledBlock)block).BreakLabel;
                    }
                    else if (SupportsUnlabeledBreak(block) && HasImmediateContainingLabeledBlock(labelText, i - 1))
                    {
                        return ((BreakableCodeBlock)block).BreakLabel;
                    }
                }
            }
            else
            {
                for (var i = _blockStack.Count - 1; i >= 0; i--)
                {
                    var block = _blockStack[i];
                    if (SupportsUnlabeledBreak(block))
                    {
                        return ((BreakableCodeBlock)block).BreakLabel;
                    }
                }
            }
        }
        return 0;
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:2494-2519 — <c>findContinueTarget</c>.</summary>
    private int FindContinueTarget(string? labelText = null)
    {
        if (_blockStack is not null)
        {
            if (Js.IsTruthy(labelText))
            {
                for (var i = _blockStack.Count - 1; i >= 0; i--)
                {
                    var block = _blockStack[i];
                    if (SupportsUnlabeledContinue(block) && HasImmediateContainingLabeledBlock(labelText, i - 1))
                    {
                        return ((LoopBlock)block).ContinueLabel;
                    }
                }
            }
            else
            {
                for (var i = _blockStack.Count - 1; i >= 0; i--)
                {
                    var block = _blockStack[i];
                    if (SupportsUnlabeledContinue(block))
                    {
                        return ((LoopBlock)block).ContinueLabel;
                    }
                }
            }
        }
        return 0;
    }

    // ---------------------------------------------------------------------------------------------
    // generators.ts:2521-2610 — the inline node builders
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/generators.ts:2521-2542 — <c>createLabel</c>.</summary>
    private Expression CreateLabel(int? label)
    {
        if (label is not null && label > 0)
        {
            _labelExpressions ??= [];

            var expression = _factory.CreateNumericLiteral(MaxSafeIntegerText);
            AddLabelExpression(label.Value, expression);
            return expression;
        }

        return _factory.CreateOmittedExpression();
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:2544-2549 — <c>createInstruction</c>.</summary>
    private NumericLiteral CreateInstruction(Instruction instruction)
    {
        var literal = _factory.CreateNumericLiteral((int)instruction);
        EmitNodes.AddSyntheticTrailingComment(literal, SyntaxKind.MultiLineCommentTrivia, GeneratorsTransform.GetInstructionName(instruction)!);
        return literal;
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:2556-2570 — <c>createInlineBreak</c>.</summary>
    private ReturnStatement CreateInlineBreak(int label, ITextRange? location = null)
    {
        Debug.AssertLessThan(0, label, "Invalid label");
        return Utilities.SetTextRange(
            _factory.CreateReturnStatement(
                _factory.CreateArrayLiteralExpression([
                    CreateInstruction(Instruction.Break),
                    CreateLabel(label),
                ], null)),
            location);
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:2575-2592 — <c>createInlineReturn</c>.</summary>
    private ReturnStatement CreateInlineReturn(Expression? expression = null, ITextRange? location = null)
        => Utilities.SetTextRange(
            _factory.CreateReturnStatement(
                _factory.CreateArrayLiteralExpression(
                    (List<Expression>)(expression is not null
                        ? [CreateInstruction(Instruction.Return), expression]
                        : [CreateInstruction(Instruction.Return)]),
                    null)),
            location);

    /// <summary>ported from: src/compiler/transformers/generators.ts:2591-2610 — <c>createGeneratorResume</c>.</summary>
    private LeftHandSideExpression CreateGeneratorResume(ITextRange? location = null)
        => Utilities.SetTextRange(
            _factory.CreateCallExpression(
                _factory.CreatePropertyAccessExpression(_state, "sent"),
                /*typeArguments*/ null,
                []),
            location);

    // ---------------------------------------------------------------------------------------------
    // generators.ts:2605-2739 — the operations
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/generators.ts:2605-2607 — <c>emitNop</c>.</summary>
    private void EmitNop() => EmitWorker(OpCode.Nop);

    /// <summary>ported from: src/compiler/transformers/generators.ts:2614-2622 — <c>emitStatement</c>.</summary>
    private void EmitStatement(IStatement? node)
    {
        if (node is not null)
        {
            EmitWorker(OpCode.Statement, OperationArguments.ForStatement(node));
        }
        else
        {
            EmitNop();
        }
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:2630-2632 — <c>emitAssignment</c>.</summary>
    private void EmitAssignment(Expression left, Expression right, ITextRange? location = null)
        => EmitWorker(OpCode.Assign, OperationArguments.ForExpressions(left, right), location);

    /// <summary>ported from: src/compiler/transformers/generators.ts:2640-2642 — <c>emitBreak</c>.</summary>
    private void EmitBreak(int label, ITextRange? location = null)
        => EmitWorker(OpCode.Break, OperationArguments.ForLabel(label), location);

    /// <summary>ported from: src/compiler/transformers/generators.ts:2652-2654 — <c>emitBreakWhenTrue</c>.</summary>
    private void EmitBreakWhenTrue(int label, Expression condition, ITextRange? location = null)
        => EmitWorker(OpCode.BreakWhenTrue, OperationArguments.ForLabelAndExpression(label, condition), location);

    /// <summary>ported from: src/compiler/transformers/generators.ts:2664-2666 — <c>emitBreakWhenFalse</c>.</summary>
    private void EmitBreakWhenFalse(int label, Expression condition, ITextRange? location = null)
        => EmitWorker(OpCode.BreakWhenFalse, OperationArguments.ForLabelAndExpression(label, condition), location);

    /// <summary>ported from: src/compiler/transformers/generators.ts:2674-2676 — <c>emitYieldStar</c>.</summary>
    private void EmitYieldStar(Expression? expression = null, ITextRange? location = null)
        => EmitWorker(OpCode.YieldStar, OperationArguments.ForExpression(expression), location);

    /// <summary>ported from: src/compiler/transformers/generators.ts:2684-2686 — <c>emitYield</c>.</summary>
    private void EmitYield(Expression? expression = null, ITextRange? location = null)
        => EmitWorker(OpCode.Yield, OperationArguments.ForExpression(expression), location);

    /// <summary>ported from: src/compiler/transformers/generators.ts:2694-2696 — <c>emitReturn</c>.</summary>
    private void EmitReturn(Expression? expression = null, ITextRange? location = null)
        => EmitWorker(OpCode.Return, OperationArguments.ForExpression(expression), location);

    /// <summary>ported from: src/compiler/transformers/generators.ts:2704-2706 — <c>emitThrow</c>.</summary>
    private void EmitThrow(Expression expression, ITextRange? location = null)
        => EmitWorker(OpCode.Throw, OperationArguments.ForExpression(expression), location);

    /// <summary>ported from: src/compiler/transformers/generators.ts:2711-2713 — <c>emitEndfinally</c>.</summary>
    private void EmitEndfinally() => EmitWorker(OpCode.Endfinally);

    /// <summary>ported from: src/compiler/transformers/generators.ts:2721-2739 — <c>emitWorker</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>operations[operationIndex] = code</c> (and the two sibling arrays) write at
    /// the arrays' current length; the port appends, which is the same store.</remarks>
    private void EmitWorker(OpCode code, OperationArguments? args = null, ITextRange? location = null)
    {
        if (_operations is null)
        {
            _operations = [];
            _operationArguments = [];
            _operationLocations = [];
        }

        if (_labelOffsets is null)
        {
            // mark entry point
            MarkLabel(DefineLabel());
        }

        _operations.Add(code);
        _operationArguments!.Add(args);
        _operationLocations!.Add(location);
    }
}
