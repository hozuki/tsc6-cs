#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/es2015.ts:220-5066 — the whole ES2015 downlevel transform.
/// <summary>
/// ported from: src/compiler/transformers/es2015.ts:488-5066 — <c>transformES2015</c>: the transformer that rewrites
/// the ES2015 syntax a <see cref="SourceFile"/> may contain (classes, arrow functions, destructuring assignments,
/// template literals, spread elements, <c>for&hellip;of</c>, computed property names, block-scoped bindings captured by
/// a closure, <c>super</c>, <c>new.target</c>, &hellip;) into the shape the ES5 emitter prints.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (home, §15.15): the reference exports one function, <c>transformES2015</c>, from
/// <c>transformers/es2015.ts</c>; its body is a table of closures over the factory's locals, so the port states the
/// factory as <see cref="ES2015Transform.TransformES2015"/> and the closures as the members of
/// <see cref="ES2015Transformer"/> — the same "closures → classes" treatment <c>TypeScriptTransform</c> records for
/// <c>ts.ts</c>.
/// </para>
/// <para>
/// PORT-NOTE (the transformer face): TS's <c>Transformer&lt;SourceFile | Bundle&gt;</c> is
/// <c>(node: SourceFile | Bundle) =&gt; SourceFile | Bundle</c>; the port's transformer list spells its slots
/// <c>Func&lt;object?, object?&gt;</c>, and the two arms of <c>chainBundle</c> are the kind test
/// <see cref="TransformationUtilities.ChainBundle"/> makes.
/// </para>
/// <para>
/// PORT-NOTE (the visitor slots): TS's <c>visitor</c>/<c>nodesVisitor</c> are the <c>Visitor</c> function type, which
/// the port spells <c>Func&lt;Node, object?&gt;</c> (Utilities/Utilities.Visitor.cs), and <c>VisitResult&lt;T&gt;</c> is
/// <c>T | readonly Node[] | undefined</c>, which the port discriminates the same way <c>NodeFactory.CopyCustomPrologue</c>
/// does (<c>null</c>, then a node list, then a node).
/// </para>
/// <para>
/// PORT-NOTE (<c>factory</c> and the lexical-environment members): the reference destructures <c>factory</c>,
/// <c>getEmitHelperFactory</c> and the four lexical-environment members out of the context; the port keeps the context
/// itself and reads the same members through it (their bodies are <c>TransformationContext&lt;T&gt;</c>'s,
/// Transformer.cs), so the sequence is the same.
/// </para>
/// <para>
/// PORT-NOTE (<c>factory.getLocalName</c>/<c>getInternalName</c>): the <c>getName</c> family lives on
/// <see cref="TypeScriptTransformSupport"/> (the M5 residue that file records), so those calls carry the factory as
/// their first argument.
/// </para>
/// </remarks>
internal static class ES2015Transform
{
    /// <summary>ported from: src/compiler/transformers/es2015.ts:488-727 — <c>transformES2015</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>Transformer&lt;SourceFile | Bundle&gt;</c> is the port's erased
    /// <c>Func&lt;object?, object?&gt;</c>; the returned member is the reference's own
    /// <c>chainBundle(context, transformSourceFile)</c> call, staying a method group so no delegate is allocated per
    /// call beyond the one <see cref="TransformationUtilities.ChainBundle"/> stores.
    /// </remarks>
    public static Func<object?, object?> TransformES2015(ITransformerContext context)
        => new ES2015Transformer(context).TransformSourceFileOrBundle;
}

// ported from: src/compiler/transformers/es2015.ts:220-227 — the const enum `ES2015SubstitutionFlags`.
/// <summary>ported from: src/compiler/transformers/es2015.ts:220-227 — <c>ES2015SubstitutionFlags</c>.</summary>
/// <remarks>PORT-NOTE: the reference declares this const enum at the top of <c>transformers/es2015.ts</c>; the port
/// states it beside the transform with the same members in the same order, and <see cref="FlagsAttribute"/> because the
/// two members are combined with <c>|</c> and tested as a set.</remarks>
[Flags]
internal enum ES2015SubstitutionFlags
{
    /// <summary><c>ES2015SubstitutionFlags.None</c>.</summary>
    None = 0,

    /// <summary><c>ES2015SubstitutionFlags.CapturedThis</c> — enables substitutions for captured <c>this</c>.</summary>
    CapturedThis = 1 << 0,

    /// <summary><c>ES2015SubstitutionFlags.BlockScopedBindings</c> — enables substitutions for block-scoped bindings.</summary>
    BlockScopedBindings = 1 << 1,
}

// ported from: src/compiler/transformers/es2015.ts:283-288 — the const enum `Jump`.
/// <summary>ported from: src/compiler/transformers/es2015.ts:283-288 — <c>Jump</c>.</summary>
/// <remarks>PORT-NOTE: TS's <c>nonLocalJumps</c>/<c>allowedNonLabeledJumps</c> start out <c>undefined</c> and are
/// combined with <c>|=</c>, which JS reads as "0 for the unset case"; <see cref="None"/> states that zero so the port's
/// field can be a plain enum value (the reference's members keep their names and their values).</remarks>
[Flags]
internal enum Jump
{
    /// <summary>The unset state TS encodes as <c>undefined</c>.</summary>
    None = 0,

    /// <summary><c>Jump.Break</c>.</summary>
    Break = 1 << 1,

    /// <summary><c>Jump.Continue</c>.</summary>
    Continue = 1 << 2,

    /// <summary><c>Jump.Return</c>.</summary>
    Return = 1 << 3,
}

// ported from: src/compiler/transformers/es2015.ts:272-277 — the const enum `LoopOutParameterFlags`.
/// <summary>ported from: src/compiler/transformers/es2015.ts:272-277 — <c>LoopOutParameterFlags</c>.</summary>
/// <remarks>PORT-NOTE: as <see cref="Jump"/>; the two members are combined with <c>|</c>.</remarks>
[Flags]
internal enum LoopOutParameterFlags
{
    /// <summary><c>LoopOutParameterFlags.None</c>.</summary>
    None = 0,

    /// <summary><c>LoopOutParameterFlags.Body</c> — modified in the body of the iteration statement.</summary>
    Body = 1 << 0,

    /// <summary><c>LoopOutParameterFlags.Initializer</c> — set in the initializer of a <c>ForStatement</c>.</summary>
    Initializer = 1 << 1,
}

// ported from: src/compiler/transformers/es2015.ts:278-281 — the const enum `CopyDirection`.
/// <summary>ported from: src/compiler/transformers/es2015.ts:278-281 — <c>CopyDirection</c>.</summary>
internal enum CopyDirection
{
    /// <summary><c>CopyDirection.ToOriginal</c>.</summary>
    ToOriginal,

    /// <summary><c>CopyDirection.ToOutParameter</c>.</summary>
    ToOutParameter,
}

// ported from: src/compiler/transformers/es2015.ts:472-477 — the const enum `SpreadSegmentKind`.
/// <summary>ported from: src/compiler/transformers/es2015.ts:472-477 — <c>SpreadSegmentKind</c>.</summary>
internal enum SpreadSegmentKind
{
    /// <summary><c>SpreadSegmentKind.None</c> — not a spread segment.</summary>
    None,

    /// <summary><c>SpreadSegmentKind.UnpackedSpread</c> — must be packed (converting <c>[...[1, , 2]]</c> into
    /// <c>[1, undefined, 2]</c>).</summary>
    UnpackedSpread,

    /// <summary><c>SpreadSegmentKind.PackedSpread</c> — known to already be packed (<c>[...[1, 2]]</c> or
    /// <c>[...__read(a)]</c>).</summary>
    PackedSpread,
}

// ported from: src/compiler/transformers/es2015.ts:371-470 — the const enum `HierarchyFacts`.
/// <summary>ported from: src/compiler/transformers/es2015.ts:371-470 — <c>HierarchyFacts</c>.</summary>
/// <remarks>PORT-NOTE: the reference's composite members (<c>AncestorFactsMask</c>, <c>SubtreeFactsMask</c> and the
/// per-kind include/exclude masks) are stated by the same expressions, which are all constant in C#.</remarks>
[Flags]
internal enum HierarchyFacts
{
    /// <summary><c>HierarchyFacts.None</c>.</summary>
    None = 0,

    //
    // Ancestor facts
    //

    /// <summary><c>Function</c> — enclosed in a non-arrow function.</summary>
    Function = 1 << 0,

    /// <summary><c>ArrowFunction</c> — enclosed in an arrow function.</summary>
    ArrowFunction = 1 << 1,

    /// <summary><c>AsyncFunctionBody</c> — enclosed in an async function body.</summary>
    AsyncFunctionBody = 1 << 2,

    /// <summary><c>NonStaticClassElement</c> — enclosed in a non-static, non-async class element.</summary>
    NonStaticClassElement = 1 << 3,

    /// <summary><c>CapturesThis</c> — enclosed in a function that captures the lexical <c>this</c>.</summary>
    CapturesThis = 1 << 4,

    /// <summary><c>ExportedVariableStatement</c> — enclosed in an exported variable statement in the current scope.</summary>
    ExportedVariableStatement = 1 << 5,

    /// <summary><c>TopLevel</c> — enclosing block-scoped container is a top-level container.</summary>
    TopLevel = 1 << 6,

    /// <summary><c>Block</c> — enclosing block-scoped container is a Block.</summary>
    Block = 1 << 7,

    /// <summary><c>IterationStatement</c> — immediately enclosed in an IterationStatement.</summary>
    IterationStatement = 1 << 8,

    /// <summary><c>IterationStatementBlock</c> — enclosing Block is enclosed in an IterationStatement.</summary>
    IterationStatementBlock = 1 << 9,

    /// <summary><c>IterationContainer</c> — enclosed in an outer IterationStatement.</summary>
    IterationContainer = 1 << 10,

    /// <summary><c>ForStatement</c> — enclosing block-scoped container is a ForStatement.</summary>
    ForStatement = 1 << 11,

    /// <summary><c>ForInOrForOfStatement</c> — enclosing block-scoped container is a ForInStatement or
    /// ForOfStatement.</summary>
    ForInOrForOfStatement = 1 << 12,

    /// <summary><c>ConstructorWithSuperCall</c> — enclosed in a constructor that captures <c>this</c> for use with
    /// <c>super</c>.</summary>
    ConstructorWithSuperCall = 1 << 13,

    /// <summary><c>StaticInitializer</c> — enclosed in a static initializer.</summary>
    StaticInitializer = 1 << 14,

    //
    // Ancestor masks
    //

    /// <summary><c>AncestorFactsMask</c>.</summary>
    AncestorFactsMask = (StaticInitializer << 1) - 1,

    /// <summary><c>BlockScopeIncludes</c>.</summary>
    BlockScopeIncludes = None,

    /// <summary><c>BlockScopeExcludes</c>.</summary>
    BlockScopeExcludes = TopLevel | Block | IterationStatement | IterationStatementBlock | ForStatement | ForInOrForOfStatement,

    /// <summary><c>SourceFileIncludes</c>.</summary>
    SourceFileIncludes = TopLevel,

    /// <summary><c>SourceFileExcludes</c>.</summary>
    SourceFileExcludes = BlockScopeExcludes & ~TopLevel | IterationContainer,

    /// <summary><c>FunctionIncludes</c>.</summary>
    FunctionIncludes = Function | TopLevel,

    /// <summary><c>FunctionExcludes</c>.</summary>
    FunctionExcludes = BlockScopeExcludes & ~TopLevel | ArrowFunction | AsyncFunctionBody | CapturesThis | NonStaticClassElement | ConstructorWithSuperCall | IterationContainer | StaticInitializer,

    /// <summary><c>AsyncFunctionBodyIncludes</c>.</summary>
    AsyncFunctionBodyIncludes = FunctionIncludes | AsyncFunctionBody,

    /// <summary><c>AsyncFunctionBodyExcludes</c>.</summary>
    AsyncFunctionBodyExcludes = FunctionExcludes & ~NonStaticClassElement,

    /// <summary><c>ArrowFunctionIncludes</c>.</summary>
    ArrowFunctionIncludes = ArrowFunction | TopLevel,

    /// <summary><c>ArrowFunctionExcludes</c>.</summary>
    ArrowFunctionExcludes = BlockScopeExcludes & ~TopLevel | ConstructorWithSuperCall,

    /// <summary><c>ConstructorIncludes</c>.</summary>
    ConstructorIncludes = FunctionIncludes | NonStaticClassElement,

    /// <summary><c>ConstructorExcludes</c>.</summary>
    ConstructorExcludes = FunctionExcludes & ~NonStaticClassElement,

    /// <summary><c>DoOrWhileStatementIncludes</c>.</summary>
    DoOrWhileStatementIncludes = IterationStatement | IterationContainer,

    /// <summary><c>DoOrWhileStatementExcludes</c>.</summary>
    DoOrWhileStatementExcludes = None,

    /// <summary><c>ForStatementIncludes</c>.</summary>
    ForStatementIncludes = IterationStatement | ForStatement | IterationContainer,

    /// <summary><c>ForStatementExcludes</c>.</summary>
    ForStatementExcludes = BlockScopeExcludes & ~ForStatement,

    /// <summary><c>ForInOrForOfStatementIncludes</c>.</summary>
    ForInOrForOfStatementIncludes = IterationStatement | ForInOrForOfStatement | IterationContainer,

    /// <summary><c>ForInOrForOfStatementExcludes</c>.</summary>
    ForInOrForOfStatementExcludes = BlockScopeExcludes & ~ForInOrForOfStatement,

    /// <summary><c>BlockIncludes</c>.</summary>
    BlockIncludes = Block,

    /// <summary><c>BlockExcludes</c>.</summary>
    BlockExcludes = BlockScopeExcludes & ~Block,

    /// <summary><c>IterationStatementBlockIncludes</c>.</summary>
    IterationStatementBlockIncludes = IterationStatementBlock,

    /// <summary><c>IterationStatementBlockExcludes</c>.</summary>
    IterationStatementBlockExcludes = BlockScopeExcludes,

    /// <summary><c>StaticInitializerIncludes</c>.</summary>
    StaticInitializerIncludes = FunctionIncludes | StaticInitializer,

    /// <summary><c>StaticInitializerExcludes</c>.</summary>
    StaticInitializerExcludes = FunctionExcludes,

    //
    // Subtree facts
    //

    /// <summary><c>NewTarget</c> — contains a <c>new.target</c> meta-property.</summary>
    NewTarget = 1 << 15,

    /// <summary><c>LexicalThis</c> — contains a lexical <c>this</c> reference.</summary>
    LexicalThis = 1 << 16,

    /// <summary><c>CapturedLexicalThis</c> — contains a lexical <c>this</c> reference captured by an arrow function.</summary>
    CapturedLexicalThis = 1 << 17,

    //
    // Subtree masks
    //

    /// <summary><c>SubtreeFactsMask</c>.</summary>
    SubtreeFactsMask = ~AncestorFactsMask,

    /// <summary><c>ArrowFunctionSubtreeExcludes</c>.</summary>
    ArrowFunctionSubtreeExcludes = None,

    /// <summary><c>FunctionSubtreeExcludes</c>.</summary>
    FunctionSubtreeExcludes = NewTarget | LexicalThis | CapturedLexicalThis,
}

// ported from: src/compiler/transformers/es2015.ts:266-270 — the interface `LoopOutParameter`.
/// <summary>ported from: src/compiler/transformers/es2015.ts:266-270 — <c>LoopOutParameter</c>.</summary>
/// <remarks>PORT-NOTE: TS's interface has no constructible form; the port states the three slots as a small class (the
/// same treatment the batch's other object literals get).</remarks>
internal sealed class LoopOutParameter
{
    public required LoopOutParameterFlags Flags { get; init; }

    public required Identifier OriginalName { get; init; }

    public required Identifier OutParamName { get; init; }
}

// ported from: src/compiler/transformers/es2015.ts:289-365 — the interface `ConvertedLoopState`.
/// <summary>ported from: src/compiler/transformers/es2015.ts:289-365 — <c>ConvertedLoopState</c>.</summary>
/// <remarks>PORT-NOTE: TS's <c>Map&lt;string, X&gt;</c> is the port's <c>Dictionary&lt;string, X&gt;</c>
/// (<c>__String</c> is erased to <c>string</c> port-wide) and its <c>X | undefined</c> slots are <c>X?</c>; the two
/// <c>Jump</c> slots are non-optional enum values (<see cref="Jump.None"/>, see that enum's note).</remarks>
internal sealed class ConvertedLoopState
{
    /// <summary>ported from: es2015.ts:294 — <c>labels</c>.</summary>
    public Dictionary<string, bool>? Labels { get; set; }

    /// <summary>ported from: es2015.ts:300 — <c>labeledNonLocalBreaks</c>.</summary>
    public Dictionary<string, string>? LabeledNonLocalBreaks { get; set; }

    /// <summary>ported from: es2015.ts:301 — <c>labeledNonLocalContinues</c>.</summary>
    public Dictionary<string, string>? LabeledNonLocalContinues { get; set; }

    /// <summary>ported from: es2015.ts:306 — <c>nonLocalJumps</c>.</summary>
    public Jump NonLocalJumps { get; set; }

    /// <summary>ported from: es2015.ts:312 — <c>allowedNonLabeledJumps</c>.</summary>
    public Jump AllowedNonLabeledJumps { get; set; }

    /// <summary>ported from: es2015.ts:321 — <c>argumentsName</c>.</summary>
    public Identifier? ArgumentsName { get; set; }

    /// <summary>ported from: es2015.ts:326 — <c>thisName</c>.</summary>
    public Identifier? ThisName { get; set; }

    /// <summary>ported from: es2015.ts:331 — <c>containsLexicalThis</c>.</summary>
    public bool ContainsLexicalThis { get; set; }

    /// <summary>ported from: es2015.ts:345 — <c>hoistedLocalVariables</c>.</summary>
    public List<Identifier>? HoistedLocalVariables { get; set; }

    /// <summary>ported from: es2015.ts:347 — <c>conditionVariable</c>.</summary>
    public Identifier? ConditionVariable { get; set; }

    /// <summary>ported from: es2015.ts:349 — <c>loopParameters</c>.</summary>
    public required List<ParameterDeclaration> LoopParameters { get; init; }

    /// <summary>ported from: es2015.ts:354 — <c>loopOutParameters</c>.</summary>
    public required List<LoopOutParameter> LoopOutParameters { get; init; }
}

// ported from: src/compiler/transformers/es2015.ts:478-481 — the interface `SpreadSegment`.
/// <summary>ported from: src/compiler/transformers/es2015.ts:478-481 — <c>SpreadSegment</c>.</summary>
internal sealed class SpreadSegment(SpreadSegmentKind kind, Expression expression)
{
    /// <summary>ported from: es2015.ts:479 — <c>kind</c>.</summary>
    public SpreadSegmentKind Kind { get; } = kind;

    /// <summary>ported from: es2015.ts:480 — <c>expression</c>.</summary>
    public Expression Expression { get; } = expression;
}

// ported from: src/compiler/transformers/es2015.ts:367 — the type alias `LoopConverter`.
/// <summary>ported from: src/compiler/transformers/es2015.ts:367 — <c>LoopConverter</c>.</summary>
/// <remarks>PORT-NOTE: TS's generic function type over <c>T extends IterationStatement</c> is the port's generic
/// delegate with the same constraint.</remarks>
internal delegate IStatement LoopConverter<in T>(
    T node,
    LabeledStatement? outermostLabeledStatement,
    List<IStatement>? convertedLoopBodyStatements,
    HierarchyFacts ancestorFacts)
    where T : IterationStatement;

/// <summary>ported from: src/compiler/transformers/es2015.ts:3699-3704 — the interface
/// <c>IterationStatementPartFunction</c>.</summary>
internal sealed class IterationStatementPartFunction<T>
{
    /// <summary>ported from: es2015.ts:3700 — <c>functionName</c>.</summary>
    public required Identifier FunctionName { get; init; }

    /// <summary>ported from: es2015.ts:3701 — <c>functionDeclaration</c>.</summary>
    public required IStatement FunctionDeclaration { get; init; }

    /// <summary>ported from: es2015.ts:3702 — <c>containsYield</c>.</summary>
    public required bool ContainsYield { get; init; }

    /// <summary>ported from: es2015.ts:3703 — <c>part</c>.</summary>
    public required T Part { get; init; }
}

/// <summary>ported from: src/compiler/transformers/es2015.ts:488-5066 — the body of <c>transformES2015</c>.</summary>
/// <remarks>
/// PORT-NOTE (the state): the reference's <c>currentSourceFile</c>, <c>currentText</c>, <c>hierarchyFacts</c>,
/// <c>taggedTemplateStringDeclarations</c>, <c>convertedLoopState</c> and <c>enabledSubstitutions</c> are the factory
/// function's locals that the closures mutate; they are this class's fields.
/// </remarks>
internal sealed partial class ES2015Transformer
{
    private readonly ITransformerContext _context;
    private readonly NodeFactory _factory;
    private readonly IEmitHelperFactory _emitHelpers;
    private readonly IEmitResolver _resolver;
    private readonly ICompilerOptions _compilerOptions;
    private readonly Func<EmitHint, Node, Node> _previousOnSubstituteNode;
    private readonly Action<EmitHint, Node, Action<EmitHint, Node>> _previousOnEmitNode;

    /// <summary>ported from: src/compiler/transformers/es2015.ts:489-530 — <c>chainBundle(context, transformSourceFile)</c>.</summary>
    private readonly Func<Node, Node> _transformSourceFile;

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2594 — <c>factory.liftToBlock</c>.</summary>
    /// <remarks>PORT-NOTE: the port's <c>visitNode</c> lift slot is <c>Func&lt;IReadOnlyList&lt;Node&gt;, Node?&gt;</c>
    /// while <c>NodeFactory.LiftToBlock</c> takes a <c>List&lt;Node&gt;</c>; this is that adapter, built once.</remarks>
    private readonly Func<IReadOnlyList<Node>, Node?> _liftToBlock;

    // These variables contain state that changes as we descend into the tree.
    private SourceFile? _currentSourceFile;
    private string? _currentText;
    private HierarchyFacts _hierarchyFacts;
    private List<VariableDeclaration>? _taggedTemplateStringDeclarations;

    /// <summary>ported from: src/compiler/transformers/es2015.ts:520 — <c>convertedLoopState</c>: used to track if we
    /// are emitting the body of the converted loop.</summary>
    private ConvertedLoopState? _convertedLoopState;

    /// <summary>ported from: src/compiler/transformers/es2015.ts:523-527 — <c>enabledSubstitutions</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's comment records that this is persisted between each SourceFile
    /// transformation and is not reset; it is a field for that reason.</remarks>
    private ES2015SubstitutionFlags _enabledSubstitutions = ES2015SubstitutionFlags.None;

    /// <summary>ported from: src/compiler/transformers/es2015.ts:489-530 — the factory's prologue.</summary>
    public ES2015Transformer(ITransformerContext context)
    {
        _context = context;
        _factory = context.Factory;
        _emitHelpers = context.GetEmitHelperFactory();
        _resolver = context.GetEmitResolver();
        _compilerOptions = context.GetCompilerOptions();
        _liftToBlock = nodes => (Node)_factory.LiftToBlock(nodes as List<Node> ?? new List<Node>(nodes));
        _transformSourceFile = TransformationUtilities.ChainBundle(context, TransformSourceFile);

        var previousOnSubstituteNode = context.OnSubstituteNode;
        var previousOnEmitNode = context.OnEmitNode;
        _previousOnSubstituteNode = previousOnSubstituteNode!;
        _previousOnEmitNode = previousOnEmitNode!;
        context.OnEmitNode = OnEmitNode;
        context.OnSubstituteNode = OnSubstituteNode;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:510-515 — <c>recordTaggedTemplateString</c>.</summary>
    private void RecordTaggedTemplateString(Identifier temp)
        => _taggedTemplateStringDeclarations = (List<VariableDeclaration>?)Core.Append(
            _taggedTemplateStringDeclarations,
            _factory.CreateVariableDeclaration(temp, /*exclamationToken*/ null, /*type*/ null, /*initializer*/ null));

    /// <summary>ported from: src/compiler/transformers/es2015.ts:531-550 — <c>transformSourceFile</c>.</summary>
    /// <remarks>PORT-NOTE: <c>double</c>: the reference chains this through <c>chainBundle</c>; this member is the
    /// <c>transformSourceFile</c> arm it wraps, and <see cref="TransformSourceFileOrBundle"/> is the chain's face.</remarks>
    private SourceFile TransformSourceFile(SourceFile node)
    {
        if (node.IsDeclarationFile)
        {
            return node;
        }

        _currentSourceFile = node;
        _currentText = node.Text;

        var visited = VisitSourceFile(node);
        EmitNodes.AddEmitHelpers(visited, _context.ReadEmitHelpers());

        _currentSourceFile = null;
        _currentText = null;
        _taggedTemplateStringDeclarations = null;
        _hierarchyFacts = HierarchyFacts.None;
        return visited;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:531-550 — the returned
    /// <c>chainBundle(context, transformSourceFile)</c>.</summary>
    public object? TransformSourceFileOrBundle(object? node) => _transformSourceFile((Node)node!);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:554-559 — <c>enterSubtree</c>.</summary>
    private HierarchyFacts EnterSubtree(HierarchyFacts excludeFacts, HierarchyFacts includeFacts)
    {
        var ancestorFacts = _hierarchyFacts;
        _hierarchyFacts = (_hierarchyFacts & ~excludeFacts | includeFacts) & HierarchyFacts.AncestorFactsMask;
        return ancestorFacts;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:567-569 — <c>exitSubtree</c>.</summary>
    private void ExitSubtree(HierarchyFacts ancestorFacts, HierarchyFacts excludeFacts, HierarchyFacts includeFacts)
    {
        _hierarchyFacts = (_hierarchyFacts & ~excludeFacts | includeFacts) & HierarchyFacts.SubtreeFactsMask | ancestorFacts;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:571-575 — <c>isReturnVoidStatementInConstructorWithCapturedSuper</c>.</summary>
    private bool IsReturnVoidStatementInConstructorWithCapturedSuper(Node node)
        => (_hierarchyFacts & HierarchyFacts.ConstructorWithSuperCall) != 0
            && node.Kind == SyntaxKind.ReturnStatement
            && ((ReturnStatement)node).Expression is null;

    /// <summary>ported from: src/compiler/transformers/es2015.ts:577-591 — <c>isOrMayContainReturnCompletion</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>node.transformFlags &amp; TransformFlags.X</c> is truthiness of a number, so the port
    /// states the <c>!= 0</c> test; <c>isIterationStatement(node, false)</c> is
    /// <see cref="Utilities.IsIterationStatement"/>.</remarks>
    private static bool IsOrMayContainReturnCompletion(Node node)
        => (node.TransformFlags & TransformFlags.ContainsHoistedDeclarationOrCompletion) != 0
            && (NodeTests.IsReturnStatement(node)
                || NodeTests.IsIfStatement(node)
                || NodeTests.IsWithStatement(node)
                || NodeTests.IsSwitchStatement(node)
                || NodeTests.IsCaseBlock(node)
                || NodeTests.IsCaseClause(node)
                || NodeTests.IsDefaultClause(node)
                || NodeTests.IsTryStatement(node)
                || NodeTests.IsCatchClause(node)
                || NodeTests.IsLabeledStatement(node)
                || Utilities.IsIterationStatement(node, /*lookInLabeledStatements*/ false)
                || NodeTests.IsBlock(node));

    /// <summary>ported from: src/compiler/transformers/es2015.ts:593-599 — <c>shouldVisitNode</c>.</summary>
    private bool ShouldVisitNode(Node node)
        => (node.TransformFlags & TransformFlags.ContainsES2015) != 0
            || _convertedLoopState is not null
            || ((_hierarchyFacts & HierarchyFacts.ConstructorWithSuperCall) != 0 && IsOrMayContainReturnCompletion(node))
            || (Utilities.IsIterationStatement(node, /*lookInLabeledStatements*/ false) && ShouldConvertIterationStatement((IterationStatement)node))
            || (ES2015TransformSupport.GetInternalEmitFlags(node) & InternalEmitFlags.TypeScriptClassWrapper) != 0;

    /// <summary>ported from: src/compiler/transformers/es2015.ts:601-603 — <c>visitor</c>.</summary>
    private object? Visitor(Node node) => ShouldVisitNode(node) ? VisitorWorker(node, /*expressionResultIsUnused*/ false) : node;

    /// <summary>ported from: src/compiler/transformers/es2015.ts:605-607 — <c>visitorWithUnusedExpressionResult</c>.</summary>
    private object? VisitorWithUnusedExpressionResult(Node node)
        => ShouldVisitNode(node) ? VisitorWorker(node, /*expressionResultIsUnused*/ true) : node;

    /// <summary>ported from: src/compiler/transformers/es2015.ts:609-624 — <c>classWrapperStatementVisitor</c>.</summary>
    private object? ClassWrapperStatementVisitor(Node node)
    {
        if (ShouldVisitNode(node))
        {
            var original = Utilities.GetOriginalNode(node);
            if (original is not null && NodeTests.IsPropertyDeclaration(original) && Utilities.HasStaticModifier(original))
            {
                var ancestorFacts = EnterSubtree(
                    HierarchyFacts.StaticInitializerExcludes,
                    HierarchyFacts.StaticInitializerIncludes);
                var result = VisitorWorker(node, /*expressionResultIsUnused*/ false);
                ExitSubtree(ancestorFacts, HierarchyFacts.FunctionSubtreeExcludes, HierarchyFacts.None);
                return result;
            }
            return VisitorWorker(node, /*expressionResultIsUnused*/ false);
        }
        return node;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:626-631 — <c>callExpressionVisitor</c>.</summary>
    private object? CallExpressionVisitor(Node node)
        => node.Kind == SyntaxKind.SuperKeyword
            ? VisitSuperKeyword((SuperExpression)node, /*isExpressionOfCall*/ true)
            : Visitor(node);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:633-779 — <c>visitorWorker</c>.</summary>
    private object? VisitorWorker(Node node, bool expressionResultIsUnused)
    {
        switch (node.Kind)
        {
            case SyntaxKind.StaticKeyword:
                return null; // elide static keyword

            case SyntaxKind.ClassDeclaration:
                return VisitClassDeclaration((ClassDeclaration)node);

            case SyntaxKind.ClassExpression:
                return VisitClassExpression((ClassExpression)node);

            case SyntaxKind.Parameter:
                return VisitParameter((ParameterDeclaration)node);

            case SyntaxKind.FunctionDeclaration:
                return VisitFunctionDeclaration((FunctionDeclaration)node);

            case SyntaxKind.ArrowFunction:
                return VisitArrowFunction((ArrowFunction)node);

            case SyntaxKind.FunctionExpression:
                return VisitFunctionExpression((FunctionExpression)node);

            case SyntaxKind.VariableDeclaration:
                return VisitVariableDeclaration((VariableDeclaration)node);

            case SyntaxKind.Identifier:
                return VisitIdentifier((Identifier)node);

            case SyntaxKind.VariableDeclarationList:
                return VisitVariableDeclarationList((VariableDeclarationList)node);

            case SyntaxKind.SwitchStatement:
                return VisitSwitchStatement((SwitchStatement)node);

            case SyntaxKind.CaseBlock:
                return VisitCaseBlock((CaseBlock)node);

            case SyntaxKind.Block:
                return VisitBlock((Block)node, /*isFunctionBody*/ false);

            case SyntaxKind.BreakStatement:
            case SyntaxKind.ContinueStatement:
                return VisitBreakOrContinueStatement(node);

            case SyntaxKind.LabeledStatement:
                return VisitLabeledStatement((LabeledStatement)node);

            case SyntaxKind.DoStatement:
            case SyntaxKind.WhileStatement:
                return VisitDoOrWhileStatement((IterationStatement)node, /*outermostLabeledStatement*/ null);

            case SyntaxKind.ForStatement:
                return VisitForStatement((ForStatement)node, /*outermostLabeledStatement*/ null);

            case SyntaxKind.ForInStatement:
                return VisitForInStatement((ForInStatement)node, /*outermostLabeledStatement*/ null);

            case SyntaxKind.ForOfStatement:
                return VisitForOfStatement((ForOfStatement)node, /*outermostLabeledStatement*/ null);

            case SyntaxKind.ExpressionStatement:
                return VisitExpressionStatement((ExpressionStatement)node);

            case SyntaxKind.ObjectLiteralExpression:
                return VisitObjectLiteralExpression((ObjectLiteralExpression)node);

            case SyntaxKind.CatchClause:
                return VisitCatchClause((CatchClause)node);

            case SyntaxKind.ShorthandPropertyAssignment:
                return VisitShorthandPropertyAssignment((ShorthandPropertyAssignment)node);

            case SyntaxKind.ComputedPropertyName:
                return VisitComputedPropertyName((ComputedPropertyName)node);

            case SyntaxKind.ArrayLiteralExpression:
                return VisitArrayLiteralExpression((ArrayLiteralExpression)node);

            case SyntaxKind.CallExpression:
                return VisitCallExpression((CallExpression)node);

            case SyntaxKind.NewExpression:
                return VisitNewExpression((NewExpression)node);

            case SyntaxKind.ParenthesizedExpression:
                return VisitParenthesizedExpression((ParenthesizedExpression)node, expressionResultIsUnused);

            case SyntaxKind.BinaryExpression:
                return VisitBinaryExpression((BinaryExpression)node, expressionResultIsUnused);

            case SyntaxKind.CommaListExpression:
                return VisitCommaListExpression((CommaListExpression)node, expressionResultIsUnused);

            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.TemplateHead:
            case SyntaxKind.TemplateMiddle:
            case SyntaxKind.TemplateTail:
                return VisitTemplateLiteral((ITemplateLiteralLikeNode)node);

            case SyntaxKind.StringLiteral:
                return VisitStringLiteral((StringLiteral)node);

            case SyntaxKind.NumericLiteral:
                return VisitNumericLiteral((NumericLiteral)node);

            case SyntaxKind.TaggedTemplateExpression:
                return VisitTaggedTemplateExpression((TaggedTemplateExpression)node);

            case SyntaxKind.TemplateExpression:
                return VisitTemplateExpression((TemplateExpression)node);

            case SyntaxKind.YieldExpression:
                return VisitYieldExpression((YieldExpression)node);

            case SyntaxKind.SpreadElement:
                return VisitSpreadElement((SpreadElement)node);

            case SyntaxKind.SuperKeyword:
                return VisitSuperKeyword((SuperExpression)node, /*isExpressionOfCall*/ false);

            case SyntaxKind.ThisKeyword:
                return VisitThisKeyword(node);

            case SyntaxKind.MetaProperty:
                return VisitMetaProperty((MetaProperty)node);

            case SyntaxKind.MethodDeclaration:
                return VisitMethodDeclaration((MethodDeclaration)node);

            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
                return VisitAccessorDeclaration(node);

            case SyntaxKind.VariableStatement:
                return VisitVariableStatement((VariableStatement)node);

            case SyntaxKind.ReturnStatement:
                return VisitReturnStatement((ReturnStatement)node);

            case SyntaxKind.VoidExpression:
                return VisitVoidExpression((VoidExpression)node);

            default:
                return Utilities.VisitEachChildWorker(node, Visitor, _context);
        }
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:781-800 — <c>visitSourceFile</c>.</summary>
    /// <remarks>PORT-NOTE: <c>factory.copyPrologue(node.statements, prologue, false, visitor)</c> is
    /// <see cref="NodeFactory.CopyPrologue"/>; the reference's <c>addRange</c> is <see cref="Core.AddRange{T}"/>, whose
    /// <c>IList&lt;T&gt;?</c> answer is discarded here the same way TS discards the return value of a mutating call.</remarks>
    private SourceFile VisitSourceFile(SourceFile node)
    {
        var ancestorFacts = EnterSubtree(HierarchyFacts.SourceFileExcludes, HierarchyFacts.SourceFileIncludes);
        var prologue = new List<IStatement>();
        var statements = new List<IStatement>();
        _context.StartLexicalEnvironment();
        var statementOffset = _factory.CopyPrologue(node.Statements, prologue, /*ensureUseStrict*/ false, Visitor);
        Core.AddRange(statements, Utilities.VisitNodes(node.Statements, Visitor, Utilities.IsStatement, statementOffset));
        if (_taggedTemplateStringDeclarations is not null)
        {
            statements.Add(
                _factory.CreateVariableStatement(/*modifiers*/ null, _factory.CreateVariableDeclarationList(_taggedTemplateStringDeclarations, null)));
        }
        _factory.MergeLexicalEnvironment(prologue, _context.EndLexicalEnvironment());
        InsertCaptureThisForNodeIfNeeded(prologue, node);
        ExitSubtree(ancestorFacts, HierarchyFacts.None, HierarchyFacts.None);
        return _factory.UpdateSourceFile(
            node,
            Utilities.SetTextRange(_factory.CreateNodeArray(Core.Concatenate(prologue, statements), null), node.Statements),
            null,
            null,
            null,
            null,
            null);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:802-812 — <c>visitSwitchStatement</c>.</summary>
    private SwitchStatement VisitSwitchStatement(SwitchStatement node)
    {
        if (_convertedLoopState is not null)
        {
            var savedAllowedNonLabeledJumps = _convertedLoopState.AllowedNonLabeledJumps;
            // for switch statement allow only non-labeled break
            _convertedLoopState.AllowedNonLabeledJumps |= Jump.Break;
            var result = Utilities.VisitEachChildWorker(node, Visitor, _context);
            _convertedLoopState.AllowedNonLabeledJumps = savedAllowedNonLabeledJumps;
            return result;
        }
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:814-819 — <c>visitCaseBlock</c>.</summary>
    private CaseBlock VisitCaseBlock(CaseBlock node)
    {
        var ancestorFacts = EnterSubtree(HierarchyFacts.BlockScopeExcludes, HierarchyFacts.BlockScopeIncludes);
        var updated = Utilities.VisitEachChildWorker(node, Visitor, _context);
        ExitSubtree(ancestorFacts, HierarchyFacts.None, HierarchyFacts.None);
        return updated;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:821-823 — <c>returnCapturedThis</c>.</summary>
    private ReturnStatement ReturnCapturedThis(Node node)
        => NodeFactory.SetOriginalNode(_factory.CreateReturnStatement(CreateCapturedThis()), node);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:825-827 — <c>createCapturedThis</c>.</summary>
    private Identifier CreateCapturedThis()
        => _factory.CreateUniqueName("_this", GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:829-852 — <c>visitReturnStatement</c>.</summary>
    /// <remarks>PORT-NOTE: <c>node = returnCapturedThis(node)</c> re-assigns the parameter, which the reference's union
    /// arm allows and C# does too (both are <see cref="ReturnStatement"/>).</remarks>
    private IStatement VisitReturnStatement(ReturnStatement node)
    {
        if (_convertedLoopState is not null)
        {
            _convertedLoopState.NonLocalJumps |= Jump.Return;
            if (IsReturnVoidStatementInConstructorWithCapturedSuper(node))
            {
                node = ReturnCapturedThis(node);
            }
            return _factory.CreateReturnStatement(
                _factory.CreateObjectLiteralExpression(
                    [
                        _factory.CreatePropertyAssignment(
                            _factory.CreateIdentifier("value", null, null),
                            node.Expression is not null
                                ? Debug.CheckDefined(Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression))
                                : _factory.CreateVoidZero()),
                    ],
                    /*multiLine*/ null));
        }
        else if (IsReturnVoidStatementInConstructorWithCapturedSuper(node))
        {
            return ReturnCapturedThis(node);
        }
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:854-868 — <c>visitThisKeyword</c>.</summary>
    private Node VisitThisKeyword(Node node)
    {
        _hierarchyFacts |= HierarchyFacts.LexicalThis;
        if ((_hierarchyFacts & HierarchyFacts.ArrowFunction) != 0 && (_hierarchyFacts & HierarchyFacts.StaticInitializer) == 0)
        {
            _hierarchyFacts |= HierarchyFacts.CapturedLexicalThis;
        }
        if (_convertedLoopState is not null)
        {
            if ((_hierarchyFacts & HierarchyFacts.ArrowFunction) != 0)
            {
                // if the enclosing function is an ArrowFunction then we use the captured 'this' keyword.
                _convertedLoopState.ContainsLexicalThis = true;
                return node;
            }
            return _convertedLoopState.ThisName ??= _factory.CreateUniqueName("this", /*flags*/ null);
        }
        return node;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:870-872 — <c>visitVoidExpression</c>.</summary>
    private Expression VisitVoidExpression(VoidExpression node)
        => Utilities.VisitEachChildWorker(node, VisitorWithUnusedExpressionResult, _context);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:874-890 — <c>visitIdentifier</c>.</summary>
    /// <remarks>PORT-NOTE: <c>node.flags &amp; NodeFlags.IdentifierHasExtendedUnicodeEscape</c> is truthiness of a
    /// number, so the port states the <c>!= 0</c> test; <c>node.escapedText</c> is
    /// <see cref="Identifier.EscapedText"/> (<c>__String</c> is erased to <c>string</c>).</remarks>
    private Identifier VisitIdentifier(Identifier node)
    {
        if (_convertedLoopState is not null)
        {
            if (_resolver.IsArgumentsLocalBinding(node))
            {
                return _convertedLoopState.ArgumentsName ??= _factory.CreateUniqueName("arguments", /*flags*/ null);
            }
        }
        if ((node.Flags & NodeFlags.IdentifierHasExtendedUnicodeEscape) != 0)
        {
            return NodeFactory.SetOriginalNode(
                Utilities.SetTextRange(
                    _factory.CreateIdentifier(Utilities.UnescapeLeadingUnderscores(node.EscapedText!), null, null),
                    node),
                node);
        }
        return node;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:892-944 — <c>visitBreakOrContinueStatement</c>.</summary>
    /// <remarks>PORT-NOTE: <c>convertedLoopState.labels.get(x)</c> as a condition is the port's
    /// <c>TryGetValue</c>; the reference's <c>label &amp;&amp; state.labels &amp;&amp; state.labels.get(...)</c> chain is
    /// the same test since a node is never falsy.</remarks>
    private IStatement VisitBreakOrContinueStatement(Node node)
    {
        if (_convertedLoopState is not null)
        {
            // check if we can emit break/continue as is
            // it is possible if either
            //   - break/continue is labeled and label is located inside the converted loop
            //   - break/continue is non-labeled and located in non-converted loop/switch statement
            var jump = node.Kind == SyntaxKind.BreakStatement ? Jump.Break : Jump.Continue;
            var label = node is BreakStatement breakStatement ? breakStatement.Label : ((ContinueStatement)node).Label;
            var canUseBreakOrContinue =
                (label is not null && _convertedLoopState.Labels is not null && _convertedLoopState.Labels.ContainsKey(Utilities.IdText(label)))
                || (label is null && (_convertedLoopState.AllowedNonLabeledJumps & jump) != 0);

            if (!canUseBreakOrContinue)
            {
                string labelMarker;
                if (label is null)
                {
                    if (node.Kind == SyntaxKind.BreakStatement)
                    {
                        _convertedLoopState.NonLocalJumps |= Jump.Break;
                        labelMarker = "break";
                    }
                    else
                    {
                        _convertedLoopState.NonLocalJumps |= Jump.Continue;
                        // note: return value is emitted only to simplify debugging, call to converted loop body does not do any dispatching on it.
                        labelMarker = "continue";
                    }
                }
                else
                {
                    if (node.Kind == SyntaxKind.BreakStatement)
                    {
                        labelMarker = $"break-{label.EscapedText}";
                        SetLabeledJump(_convertedLoopState, /*isBreak*/ true, Utilities.IdText(label), labelMarker);
                    }
                    else
                    {
                        labelMarker = $"continue-{label.EscapedText}";
                        SetLabeledJump(_convertedLoopState, /*isBreak*/ false, Utilities.IdText(label), labelMarker);
                    }
                }
                Expression returnExpression = _factory.CreateStringLiteral(labelMarker, null, null);
                if (_convertedLoopState.LoopOutParameters.Count != 0)
                {
                    var outParams = _convertedLoopState.LoopOutParameters;
                    Expression? expr = null;
                    for (var i = 0; i < outParams.Count; i++)
                    {
                        var copyExpr = CopyOutParameter(outParams[i], CopyDirection.ToOutParameter);
                        if (i == 0)
                        {
                            expr = copyExpr;
                        }
                        else
                        {
                            expr = _factory.CreateBinaryExpression(expr!, SyntaxKind.CommaToken, copyExpr);
                        }
                    }
                    returnExpression = _factory.CreateBinaryExpression(expr!, SyntaxKind.CommaToken, returnExpression);
                }
                return _factory.CreateReturnStatement(returnExpression);
            }
        }
        return (IStatement)Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:483-486 — <c>createSpreadSegment</c>.</summary>
    private static SpreadSegment CreateSpreadSegment(SpreadSegmentKind kind, Expression expression)
        => new(kind, expression);
}
