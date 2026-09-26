#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/generators.ts:1-3284 — the whole generator/async downlevel transform.
/// <summary>
/// ported from: src/compiler/transformers/generators.ts:336-3284 — <c>transformGenerators</c>: it rewrites the body of
/// every generator function into the intermediate representation the reference's header comment describes (statements,
/// assignments, breaks, yields and completions), then compiles that representation into the `switch (state.label)` state
/// machine the <c>__generator</c> helper interprets.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (home, §15.15): the reference exports the single function <c>transformGenerators</c>, whose body is a table
/// of closures over the factory's locals. The port states the factory as <see cref="TransformGenerators"/> and the
/// closures as the members of <see cref="GeneratorsTransformer"/> — the same "closures → class" treatment
/// <c>Transformer.cs</c> records for <c>transformNodes</c>'s context literal (docs/architecture.md §5 rule 6).
/// </para>
/// <para>
/// PORT-NOTE (the correlated arrays): the reference stores its transformation state in hole-filled JS arrays
/// (<c>blocks</c>/<c>blockOffsets</c>/<c>blockActions</c>, <c>labelOffsets</c>, <c>labelExpressions</c> and
/// <c>operations</c>/<c>operationArguments</c>/<c>operationLocations</c>) and says why: "This approach is used over
/// allocating objects to store the same information to avoid GC overhead". The port keeps the arrays for that reason and
/// states the two kinds of holes with the sentinel each reader of a hole already tests against — an unset label/block
/// offset is <c>-1</c> (the reference's <c>defineLabel</c> writes <c>-1</c>, and every reader compares against a
/// non-negative operation index) and an unset label expression or label-number list is <c>null</c>.
/// </para>
/// <para>
/// PORT-NOTE (the visit result): TS's <c>visitNode(node, visitor, isStatement)</c> narrows its result through the
/// predicate's type guard. The port's single <see cref="Utilities.VisitNode{TNode}"/> keeps the argument's own type, so
/// a call whose argument is already an <c>IStatement</c>/<c>Expression</c> needs nothing while one over a bare
/// <see cref="Node"/> casts at that boundary (the §3 conversion the visitor layer states for the same reason).
/// </para>
/// <para>
/// PORT-NOTE (the named blocker): <c>visitObjectLiteralExpression</c> is the one member of the reference whose body this
/// slice cannot complete, because the <c>createExpressionForObjectLiteralElementLike</c> it calls
/// (factory/utilities.ts:657-693, itself blocked on <c>getAllAccessorDeclarations</c>) is not ported —
/// <c>FactoryUtilities</c>'s own header records the omission — and that member's home file is outside this slice. Its
/// call site is the loud <see cref="NotSupportedException"/> this slice's report names.
/// </para>
/// </remarks>
internal static class GeneratorsTransform
{
    /// <summary>ported from: src/compiler/transformers/generators.ts:336-400 — <c>transformGenerators</c>.</summary>
    /// <remarks>
    /// PORT-NOTE (the transformer face): TS's <c>Transformer&lt;SourceFile | Bundle&gt;</c> is the port's erased
    /// <c>Func&lt;object?, object?&gt;</c> (Transformer.cs's header records why), and the reference's own
    /// <c>chainBundle(context, transformSourceFile)</c> is <see cref="TransformationUtilities.ChainBundle"/>, whose
    /// <c>Func&lt;Node, Node&gt;</c> is adapted to that face — the shape
    /// <c>Transformer.WrapScriptTransformerFactory</c> states for the same pair.
    /// </remarks>
    public static Func<object?, object?> TransformGenerators(ITransformerContext context)
    {
        var transformer = new GeneratorsTransformer(context);
        var chained = TransformationUtilities.ChainBundle(context, transformer.TransformSourceFile);
        return node => chained((Node)node!);
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:318-330 — <c>getInstructionName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's parameter is never <c>Instruction.Next</c>/<c>Throw</c>/<c>Catch</c> — the only caller
    /// is <c>createInstruction</c>, which is only asked for the five completions below — so its <c>return undefined!</c>
    /// arm is the port's <c>null</c>, and the one call site passes the result on with the same assertion the reference's
    /// <c>!</c> makes.
    /// </remarks>
    public static string? GetInstructionName(Instruction instruction)
        => instruction switch
        {
            Instruction.Return => "return",
            Instruction.Break => "break",
            Instruction.Yield => "yield",
            Instruction.YieldStar => "yield*",
            Instruction.Endfinally => "endfinally",
            _ => null,
        };
}

// ported from: src/compiler/transformers/generators.ts:222-234 — the const enum `OpCode`.
/// <summary>ported from: src/compiler/transformers/generators.ts:222-234 — <c>OpCode</c>.</summary>
/// <remarks>PORT-NOTE: TS declares <c>Label</c> (generators.ts:219) as a plain alias of <c>number</c>; the port reads
/// <c>int</c> at every slot and records the alias in the member names that carry one.</remarks>
internal enum OpCode
{
    /// <summary><c>OpCode.Nop</c> — no operation, used to force a new case in the state machine.</summary>
    Nop,
    /// <summary><c>OpCode.Statement</c> — a regular JavaScript statement.</summary>
    Statement,
    /// <summary><c>OpCode.Assign</c> — an assignment.</summary>
    Assign,
    /// <summary><c>OpCode.Break</c> — a break instruction used to jump to a label.</summary>
    Break,
    /// <summary><c>OpCode.BreakWhenTrue</c> — a break instruction used to jump to a label if a condition is truthy.</summary>
    BreakWhenTrue,
    /// <summary><c>OpCode.BreakWhenFalse</c> — a break instruction used to jump to a label if a condition is falsey.</summary>
    BreakWhenFalse,
    /// <summary><c>OpCode.Yield</c> — a completion instruction for the <c>yield</c> keyword.</summary>
    Yield,
    /// <summary><c>OpCode.YieldStar</c> — a completion instruction for the <c>yield*</c> keyword.</summary>
    YieldStar,
    /// <summary><c>OpCode.Return</c> — a completion instruction for the <c>return</c> keyword.</summary>
    Return,
    /// <summary><c>OpCode.Throw</c> — a completion instruction for the <c>throw</c> keyword.</summary>
    Throw,
    /// <summary><c>OpCode.Endfinally</c> — marks the end of a <c>finally</c> block.</summary>
    Endfinally,
}

// ported from: src/compiler/transformers/generators.ts:239-243 — the const enum `BlockAction`.
/// <summary>ported from: src/compiler/transformers/generators.ts:239-243 — <c>BlockAction</c>: whether a generated code
/// block is opening or closing at the current operation for a <c>FunctionBuilder</c>.</summary>
internal enum BlockAction
{
    /// <summary><c>BlockAction.Open</c>.</summary>
    Open,
    /// <summary><c>BlockAction.Close</c>.</summary>
    Close,
}

// ported from: src/compiler/transformers/generators.ts:245-252 — the const enum `CodeBlockKind`.
/// <summary>ported from: src/compiler/transformers/generators.ts:245-252 — <c>CodeBlockKind</c>: the kind of a generated
/// code block in a <c>FunctionBuilder</c>.</summary>
internal enum CodeBlockKind
{
    /// <summary><c>CodeBlockKind.Exception</c>.</summary>
    Exception,
    /// <summary><c>CodeBlockKind.With</c>.</summary>
    With,
    /// <summary><c>CodeBlockKind.Switch</c>.</summary>
    Switch,
    /// <summary><c>CodeBlockKind.Loop</c>.</summary>
    Loop,
    /// <summary><c>CodeBlockKind.Labeled</c>.</summary>
    Labeled,
}

// ported from: src/compiler/transformers/generators.ts:254-260 — the const enum `ExceptionBlockState`.
/// <summary>ported from: src/compiler/transformers/generators.ts:254-260 — <c>ExceptionBlockState</c>: the state of a
/// generated code exception block.</summary>
internal enum ExceptionBlockState
{
    /// <summary><c>ExceptionBlockState.Try</c>.</summary>
    Try,
    /// <summary><c>ExceptionBlockState.Catch</c>.</summary>
    Catch,
    /// <summary><c>ExceptionBlockState.Finally</c>.</summary>
    Finally,
    /// <summary><c>ExceptionBlockState.Done</c>.</summary>
    Done,
}

// ported from: src/compiler/transformers/generators.ts:262-305 — `CodeBlock` and its five members.
/// <summary>ported from: src/compiler/transformers/generators.ts:262 — <c>CodeBlock</c>: a generated code block.</summary>
/// <remarks>
/// PORT-NOTE: TS declares <c>CodeBlock</c> as the union <c>ExceptionBlock | LabeledBlock | SwitchBlock | LoopBlock |
/// WithBlock</c> and discriminates it by its <c>kind</c> slot. C# states the union as a class hierarchy with the same
/// abstract <see cref="Kind"/> discriminator, so the reference's <c>block.kind</c> switches read the same way and its one
/// narrowing assertion (<c>endBlock() as WithBlock</c>) becomes the matching cast.
/// </remarks>
internal abstract class CodeBlock
{
    /// <summary><c>CodeBlock.kind</c>.</summary>
    public abstract CodeBlockKind Kind { get; }
}

/// <summary>ported from: src/compiler/transformers/generators.ts:262-291 — the three block kinds that carry the
/// <c>break</c> target (and, for a loop, the <c>continue</c> target).</summary>
/// <remarks>
/// PORT-NOTE: TS's <c>SwitchBlock</c>, <c>LoopBlock</c> and <c>LabeledBlock</c> each declare <c>isScript</c> and
/// <c>breakLabel</c>, and <c>findBreakTarget</c>/<c>findContinueTarget</c> read those two slots straight off whichever of
/// them its <c>supportsUnlabeledBreak</c>/<c>supportsLabeledBreakOrContinue</c>/<c>supportsUnlabeledContinue</c> tests
/// accepted. C# cannot read a slot off a union, so those two slots are stated here as their shared base — the same
/// reading the reference's own three predicates make.
/// </remarks>
internal abstract class BreakableCodeBlock : CodeBlock
{
    /// <summary><c>isScript</c>: whether the block's targets come from the source tree rather than generated code.</summary>
    public bool IsScript { get; init; }

    /// <summary><c>breakLabel</c>: the label a <c>break</c> targeting this block jumps to (<c>-1</c> for a script block,
    /// which has no generated target).</summary>
    public int BreakLabel { get; init; }
}

/// <summary>ported from: src/compiler/transformers/generators.ts:265-274 — <c>ExceptionBlock</c>: a generated exception
/// block, used for <c>try</c> statements.</summary>
internal sealed class ExceptionBlock : CodeBlock
{
    public override CodeBlockKind Kind => CodeBlockKind.Exception;

    /// <summary><c>state</c>.</summary>
    public ExceptionBlockState State { get; set; }

    /// <summary><c>startLabel</c>.</summary>
    public int StartLabel { get; init; }

    /// <summary><c>catchVariable</c>.</summary>
    public Identifier? CatchVariable { get; set; }

    /// <summary><c>catchLabel</c>.</summary>
    public int? CatchLabel { get; set; }

    /// <summary><c>finallyLabel</c>.</summary>
    public int? FinallyLabel { get; set; }

    /// <summary><c>endLabel</c>.</summary>
    public int EndLabel { get; init; }
}

/// <summary>ported from: src/compiler/transformers/generators.ts:276-282 — <c>LabeledBlock</c>: a generated code block
/// that tracks the target for <c>break</c> statements in a <c>LabeledStatement</c>.</summary>
internal sealed class LabeledBlock : BreakableCodeBlock
{
    public override CodeBlockKind Kind => CodeBlockKind.Labeled;

    /// <summary><c>labelText</c>.</summary>
    public required string LabelText { get; init; }
}

/// <summary>ported from: src/compiler/transformers/generators.ts:284-289 — <c>SwitchBlock</c>: a generated block that
/// tracks the target for <c>break</c> statements in a <c>switch</c> statement.</summary>
internal sealed class SwitchBlock : BreakableCodeBlock
{
    public override CodeBlockKind Kind => CodeBlockKind.Switch;
}

/// <summary>ported from: src/compiler/transformers/generators.ts:291-297 — <c>LoopBlock</c>: a generated block that
/// tracks the targets for <c>break</c> and <c>continue</c>, used for iteration statements.</summary>
internal sealed class LoopBlock : BreakableCodeBlock
{
    public override CodeBlockKind Kind => CodeBlockKind.Loop;

    /// <summary><c>continueLabel</c>.</summary>
    public int ContinueLabel { get; init; }
}

/// <summary>ported from: src/compiler/transformers/generators.ts:299-305 — <c>WithBlock</c>: a generated block associated
/// with a <c>with</c> statement.</summary>
internal sealed class WithBlock : CodeBlock
{
    public override CodeBlockKind Kind => CodeBlockKind.With;

    /// <summary><c>expression</c>.</summary>
    public required Identifier Expression { get; init; }

    /// <summary><c>startLabel</c>.</summary>
    public int StartLabel { get; init; }

    /// <summary><c>endLabel</c>.</summary>
    public int EndLabel { get; init; }
}

// ported from: src/compiler/transformers/generators.ts:307-316 — the const enum `Instruction`.
/// <summary>ported from: src/compiler/transformers/generators.ts:307-316 — <c>Instruction</c>.</summary>
/// <remarks>PORT-NOTE: the reference's note stands — changes to this enum must be reflected in the <c>__generator</c>
/// helper (<c>EmitHelpers.GeneratorHelper</c>).</remarks>
internal enum Instruction
{
    /// <summary><c>Instruction.Next</c>.</summary>
    Next = 0,
    /// <summary><c>Instruction.Throw</c>.</summary>
    Throw = 1,
    /// <summary><c>Instruction.Return</c>.</summary>
    Return = 2,
    /// <summary><c>Instruction.Break</c>.</summary>
    Break = 3,
    /// <summary><c>Instruction.Yield</c>.</summary>
    Yield = 4,
    /// <summary><c>Instruction.YieldStar</c>.</summary>
    YieldStar = 5,
    /// <summary><c>Instruction.Catch</c>.</summary>
    Catch = 6,
    /// <summary><c>Instruction.Endfinally</c>.</summary>
    Endfinally = 7,
}

// ported from: src/compiler/transformers/generators.ts:236 — the `OperationArguments` tuple union.
/// <summary>ported from: src/compiler/transformers/generators.ts:236 — <c>OperationArguments</c>.</summary>
/// <remarks>
/// PORT-NOTE: TS's type is the tuple union
/// <c>[Label] | [Label, Expression] | [Statement] | [Expression | undefined] | [Expression, Expression]</c>, whose arm is
/// selected by the <c>OpCode</c> of the operation the tuple belongs to. C# has no union, so the port states the four
/// positions as one readonly struct; <see cref="GeneratorsTransformer.WriteOperation"/> reads exactly the slots the
/// operation's own opcode fills, which is what the reference's positional <c>as</c> casts do. A struct (rather than a
/// class) keeps the reference's "one array element, no object" intent.
/// </remarks>
internal readonly struct OperationArguments
{
    /// <summary><c>args[0]</c> for <c>Break</c>/<c>BreakWhenTrue</c>/<c>BreakWhenFalse</c>.</summary>
    public readonly int Label;

    /// <summary><c>args[0]</c> of the one-argument expression arms and <c>args[1]</c> of the two-argument arms.</summary>
    public readonly Expression? Expression;

    /// <summary><c>args[1]</c> of the two-argument expression arm (<c>Assign</c>) and of <c>BreakWhenTrue</c>/<c>BreakWhenFalse</c>.</summary>
    public readonly Expression? SecondExpression;

    /// <summary><c>args[0]</c> for <c>Statement</c>.</summary>
    public readonly IStatement? Statement;

    private OperationArguments(int label, Expression? expression, Expression? secondExpression, IStatement? statement)
    {
        Label = label;
        Expression = expression;
        SecondExpression = secondExpression;
        Statement = statement;
    }

    /// <summary>The <c>[Label]</c> arm.</summary>
    public static OperationArguments ForLabel(int label) => new(label, null, null, null);

    /// <summary>The <c>[Label, Expression]</c> arm.</summary>
    public static OperationArguments ForLabelAndExpression(int label, Expression condition) => new(label, condition, null, null);

    /// <summary>The <c>[Statement]</c> arm.</summary>
    public static OperationArguments ForStatement(IStatement statement) => new(0, null, null, statement);

    /// <summary>The <c>[Expression | undefined]</c> arm.</summary>
    public static OperationArguments ForExpression(Expression? expression) => new(0, expression, null, null);

    /// <summary>The <c>[Expression, Expression]</c> arm.</summary>
    public static OperationArguments ForExpressions(Expression left, Expression right) => new(0, left, right, null);
}

// ported from: src/compiler/transformers/generators.ts:336-3284 — the body of `transformGenerators`.
/// <summary>ported from: src/compiler/transformers/generators.ts:336-3284 — the body of <c>transformGenerators</c>.</summary>
/// <remarks>
/// PORT-NOTE (the state): the reference's factory locals — <c>renamedCatchVariables</c>,
/// <c>renamedCatchVariableDeclarations</c>, <c>inGeneratorFunctionBody</c>, <c>inStatementContainingYield</c>, the four
/// block arrays, the two label arrays, the three operation arrays, <c>state</c> and the nine <c>build</c> locals — are
/// this class's fields, saved and restored exactly where the reference's <c>transformGeneratorFunctionBody</c> does it.
/// The reference's <c>Map&lt;string, boolean&gt;</c> is the port's <c>Dictionary&lt;string, bool&gt;</c> (<c>__String</c>
/// is erased to <c>string</c> port-wide).
/// <para>
/// PORT-NOTE (the visitor slot): the reference captures its <c>visitor</c> once and passes that same function value to
/// every <c>visitNode</c>/<c>visitNodes</c>/<c>visitEachChild</c>/<c>visitParameterList</c>/<c>copyPrologue</c> call; the
/// port caches the method group in <see cref="_visitor"/> for the same reason (a fresh method-group conversion per call
/// would allocate a delegate per node visited).
/// </para>
/// </remarks>
internal sealed partial class GeneratorsTransformer
{
    // ---------------------------------------------------------------------------------------------
    // generators.ts:337-398 — the factory's state
    // ---------------------------------------------------------------------------------------------

    private readonly ITransformerContext _context;
    private readonly NodeFactory _factory;
    private readonly IEmitHelperFactory _emitHelpers;
    private readonly IEmitResolver _resolver;
    private readonly Action<Identifier> _hoistVariableDeclaration;
    private readonly Func<EmitHint, Node, Node> _previousOnSubstituteNode;
    private readonly Func<Node, object?> _visitor;

    /// <summary>ported from: src/compiler/transformers/generators.ts:347 — <c>languageVersion</c>.</summary>
    private readonly ScriptTarget _languageVersion;

    /// <summary>ported from: src/compiler/transformers/generators.ts:352 — <c>renamedCatchVariables</c>.</summary>
    private Dictionary<string, bool>? _renamedCatchVariables;

    /// <summary>ported from: src/compiler/transformers/generators.ts:353 — <c>renamedCatchVariableDeclarations</c>.</summary>
    /// <remarks>PORT-NOTE: the reference indexes this array by <c>getOriginalNodeId</c>, so it is sparse; the port pads
    /// it with <c>null</c> to the index the same way <see cref="_labelOffsets"/> is padded.</remarks>
    private List<Identifier?>? _renamedCatchVariableDeclarations;

    /// <summary>ported from: src/compiler/transformers/generators.ts:355 — <c>inGeneratorFunctionBody</c>.</summary>
    private bool _inGeneratorFunctionBody;

    /// <summary>ported from: src/compiler/transformers/generators.ts:356 — <c>inStatementContainingYield</c>.</summary>
    private bool _inStatementContainingYield;

    /// <summary>ported from: src/compiler/transformers/generators.ts:362 — <c>blocks</c>.</summary>
    private List<CodeBlock>? _blocks;

    /// <summary>ported from: src/compiler/transformers/generators.ts:363 — <c>blockOffsets</c>.</summary>
    private List<int>? _blockOffsets;

    /// <summary>ported from: src/compiler/transformers/generators.ts:364 — <c>blockActions</c>.</summary>
    private List<BlockAction>? _blockActions;

    /// <summary>ported from: src/compiler/transformers/generators.ts:365 — <c>blockStack</c>.</summary>
    private List<CodeBlock>? _blockStack;

    /// <summary>ported from: src/compiler/transformers/generators.ts:372 — <c>labelOffsets</c>.</summary>
    private List<int>? _labelOffsets;

    /// <summary>ported from: src/compiler/transformers/generators.ts:373 — <c>labelExpressions</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>Mutable&lt;LiteralExpression&gt;[][]</c> holds the <c>NumericLiteral</c> nodes
    /// <c>createNumericLiteral</c> built, whose <c>text</c> <c>updateLabelExpressions</c> rewrites; the port's
    /// <c>Text</c> slot is writable the same way.</remarks>
    private List<List<NumericLiteral>?>? _labelExpressions;

    /// <summary>ported from: src/compiler/transformers/generators.ts:374 — <c>nextLabelId</c>.</summary>
    private int _nextLabelId = 1;

    /// <summary>ported from: src/compiler/transformers/generators.ts:381 — <c>operations</c>.</summary>
    private List<OpCode>? _operations;

    /// <summary>ported from: src/compiler/transformers/generators.ts:382 — <c>operationArguments</c>.</summary>
    private List<OperationArguments?>? _operationArguments;

    /// <summary>ported from: src/compiler/transformers/generators.ts:383 — <c>operationLocations</c>.</summary>
    private List<ITextRange?>? _operationLocations;

    /// <summary>ported from: src/compiler/transformers/generators.ts:385 — <c>state</c>: the name of the state object used
    /// by the generator at runtime.</summary>
    private Identifier _state = null!;

    /// <summary>ported from: src/compiler/transformers/generators.ts:389 — <c>blockIndex</c>.</summary>
    private int _blockIndex;

    /// <summary>ported from: src/compiler/transformers/generators.ts:390 — <c>labelNumber</c>.</summary>
    private int _labelNumber;

    /// <summary>ported from: src/compiler/transformers/generators.ts:391 — <c>labelNumbers</c>.</summary>
    private List<List<int>?>? _labelNumbers;

    /// <summary>ported from: src/compiler/transformers/generators.ts:392 — <c>lastOperationWasAbrupt</c>.</summary>
    private bool _lastOperationWasAbrupt;

    /// <summary>ported from: src/compiler/transformers/generators.ts:393 — <c>lastOperationWasCompletion</c>.</summary>
    private bool _lastOperationWasCompletion;

    /// <summary>ported from: src/compiler/transformers/generators.ts:394 — <c>clauses</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>CaseClause[]</c> holds only <c>case</c> clauses, and the one reader —
    /// <c>NodeFactory.CreateCaseBlock</c> — takes the union's erased element type <see cref="Node"/> (§3), so the port's
    /// list carries that element type and every element appended is a <c>CaseClause</c>.</remarks>
    private List<Node>? _clauses;

    /// <summary>ported from: src/compiler/transformers/generators.ts:395 — <c>statements</c>: the statements for the
    /// current label. (The reference shadows this name inside <c>transformGeneratorFunctionBody</c> with the function's
    /// own statement list; the port keeps those two separate.)</summary>
    private List<IStatement>? _statements;

    /// <summary>ported from: src/compiler/transformers/generators.ts:396 — <c>exceptionBlockStack</c>.</summary>
    /// <remarks>PORT-NOTE: <c>tryEnterOrLeaveBlock</c> pushes <c>currentExceptionBlock!</c>, which is <c>undefined</c> for
    /// the outermost <c>try</c>; a reference type is what carries that hole here (<c>NullReferenceException</c>-free
    /// because the stack's reader accepts <c>null</c>, exactly as TS's <c>pop()</c> accepts <c>undefined</c>).</remarks>
    private List<ExceptionBlock?>? _exceptionBlockStack;

    /// <summary>ported from: src/compiler/transformers/generators.ts:397 — <c>currentExceptionBlock</c>.</summary>
    private ExceptionBlock? _currentExceptionBlock;

    /// <summary>ported from: src/compiler/transformers/generators.ts:398 — <c>withBlockStack</c>.</summary>
    private List<WithBlock>? _withBlockStack;

    /// <summary>ported from: src/compiler/transformers/generators.ts:337-400 — the factory's prologue.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference destructures the context into <c>factory</c>, <c>getEmitHelperFactory</c>,
    /// <c>resumeLexicalEnvironment</c>, <c>endLexicalEnvironment</c>, <c>hoistFunctionDeclaration</c> and
    /// <c>hoistVariableDeclaration</c>, then saves and installs <c>onSubstituteNode</c>. The port keeps the context
    /// itself and reads the same members through it (their bodies are <c>TransformationContext&lt;T&gt;</c>'s,
    /// Transformer.cs), which is what <c>TypeScriptTransformer</c> does for the same six.
    /// </remarks>
    public GeneratorsTransformer(ITransformerContext context)
    {
        _context = context;
        _factory = context.Factory;
        _emitHelpers = context.GetEmitHelperFactory();
        _resolver = context.GetEmitResolver();
        _hoistVariableDeclaration = context.HoistVariableDeclaration;
        _languageVersion = Utilities.GetEmitScriptTarget(context.GetCompilerOptions());
        _previousOnSubstituteNode = context.OnSubstituteNode!;
        _visitor = Visitor;

        // Set the new transformation hook.
        context.OnSubstituteNode = OnSubstituteNode;
    }

    // ---------------------------------------------------------------------------------------------
    // generators.ts:402-547 — the entry point and the four visitors
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/generators.ts:402-415 — <c>transformSourceFile</c>.</summary>
    public SourceFile TransformSourceFile(SourceFile node)
    {
        if (node.IsDeclarationFile || (node.TransformFlags & TransformFlags.ContainsGenerator) == 0)
        {
            return node;
        }

        var visited = (SourceFile)Utilities.VisitEachChildWorker(node, _visitor, _context);
        EmitNodes.AddEmitHelpers(visited, _context.ReadEmitHelpers());
        return visited;
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:417-439 — <c>visitor</c>.</summary>
    private object? Visitor(Node node)
    {
        var transformFlags = node.TransformFlags;
        if (_inStatementContainingYield)
        {
            return VisitJavaScriptInStatementContainingYield(node);
        }
        else if (_inGeneratorFunctionBody)
        {
            return VisitJavaScriptInGeneratorFunctionBody(node);
        }
        else if (Utilities.IsFunctionLikeDeclaration(node) && ((IFunctionLikeDeclarationBase)node).AsteriskToken is not null)
        {
            return VisitGenerator(node);
        }
        else if ((transformFlags & TransformFlags.ContainsGenerator) != 0)
        {
            return Utilities.VisitEachChildWorker(node, _visitor, _context);
        }
        else
        {
            return node;
        }
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:441-459 — <c>visitJavaScriptInStatementContainingYield</c>.</summary>
    private object? VisitJavaScriptInStatementContainingYield(Node node)
        => node.Kind switch
        {
            SyntaxKind.DoStatement => VisitDoStatement((DoStatement)node),
            SyntaxKind.WhileStatement => VisitWhileStatement((WhileStatement)node),
            SyntaxKind.SwitchStatement => VisitSwitchStatement((SwitchStatement)node),
            SyntaxKind.LabeledStatement => VisitLabeledStatement((LabeledStatement)node),
            _ => VisitJavaScriptInGeneratorFunctionBody(node),
        };

    /// <summary>ported from: src/compiler/transformers/generators.ts:461-498 — <c>visitJavaScriptInGeneratorFunctionBody</c>.</summary>
    private object? VisitJavaScriptInGeneratorFunctionBody(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.FunctionDeclaration:
                return VisitFunctionDeclaration((FunctionDeclaration)node);
            case SyntaxKind.FunctionExpression:
                return VisitFunctionExpression((FunctionExpression)node);
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
                return VisitAccessorDeclaration(node);
            case SyntaxKind.VariableStatement:
                return VisitVariableStatement((VariableStatement)node);
            case SyntaxKind.ForStatement:
                return VisitForStatement((ForStatement)node);
            case SyntaxKind.ForInStatement:
                return VisitForInStatement((ForInStatement)node);
            case SyntaxKind.BreakStatement:
                return VisitBreakStatement((BreakStatement)node);
            case SyntaxKind.ContinueStatement:
                return VisitContinueStatement((ContinueStatement)node);
            case SyntaxKind.ReturnStatement:
                return VisitReturnStatement((ReturnStatement)node);
            default:
                if ((node.TransformFlags & TransformFlags.ContainsYield) != 0)
                {
                    return VisitJavaScriptContainingYield(node);
                }
                else if ((node.TransformFlags & (TransformFlags.ContainsGenerator | TransformFlags.ContainsHoistedDeclarationOrCompletion)) != 0)
                {
                    return Utilities.VisitEachChildWorker(node, _visitor, _context);
                }
                else
                {
                    return node;
                }
        }
    }

    /// <summary>ported from: src/compiler/transformers/generators.ts:500-528 — <c>visitJavaScriptContainingYield</c>.</summary>
    private object? VisitJavaScriptContainingYield(Node node)
        => node.Kind switch
        {
            SyntaxKind.BinaryExpression => VisitBinaryExpression((BinaryExpression)node),
            SyntaxKind.CommaListExpression => VisitCommaListExpression((CommaListExpression)node),
            SyntaxKind.ConditionalExpression => VisitConditionalExpression((ConditionalExpression)node),
            SyntaxKind.YieldExpression => VisitYieldExpression((YieldExpression)node),
            SyntaxKind.ArrayLiteralExpression => VisitArrayLiteralExpression((ArrayLiteralExpression)node),
            SyntaxKind.ObjectLiteralExpression => VisitObjectLiteralExpression((ObjectLiteralExpression)node),
            SyntaxKind.ElementAccessExpression => VisitElementAccessExpression((ElementAccessExpression)node),
            SyntaxKind.CallExpression => VisitCallExpression((CallExpression)node),
            SyntaxKind.NewExpression => VisitNewExpression((NewExpression)node),
            _ => Utilities.VisitEachChildWorker(node, _visitor, _context),
        };

    /// <summary>ported from: src/compiler/transformers/generators.ts:530-547 — <c>visitGenerator</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's default arm is <c>return Debug.failBadSyntaxKind(node)</c>, which returns <c>never</c>; the port's
    /// <see cref="Debug.FailBadSyntaxKind"/> is <c>void</c> (§10.4), so the value-producing form is
    /// <see cref="Debug.Fail{T}"/> with the message <c>debug.ts:206-211</c> builds — the shape the checker uses where
    /// the reference makes the same call.
    /// </remarks>
    private object? VisitGenerator(Node node)
        => node.Kind switch
        {
            SyntaxKind.FunctionDeclaration => VisitFunctionDeclaration((FunctionDeclaration)node),
            SyntaxKind.FunctionExpression => VisitFunctionExpression((FunctionExpression)node),
            _ => Debug.Fail<object?>($"Unexpected node.\r\nNode {node.Kind} was unexpected."),
        };
}
