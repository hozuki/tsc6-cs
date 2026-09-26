namespace TypeScript.Compiler;

// ported from: src/compiler/transformer.ts (696 lines): 78-116 (getModuleTransformer), 120-125 (getTransformers),
// 127-190 (getScriptTransformers), 192-197 (getDeclarationTransformers), 199-225 (the four wrap* helpers),
// 227-234 (noEmitSubstitution/noEmitNotification — owned by `Printer`, see below), 248-668 (transformNodes),
// 669-696 (nullTransformationContext).
//
// PORT-NOTE (the shape): TS's `TransformationContext` is the object literal `transformNodes` builds — a table of
// closures over that function's locals — and `TransformationResult<T>` is the second literal it returns, so the port
// states them as the classes `TransformationContext<T>` and `TransformationResult<T>` (docs/architecture.md §5 rule 6);
// `transformNodes` itself is `TransformationContext<T>.Transform`.
//
// PORT-NOTE (§15.9 — the model gap this slice cannot compile around): the generated `ITransformationContext.Factory` is
// `INodeFactory?` and the port's `NodeFactory` does not implement `INodeFactory` (that is M4d's step — `NodeFactory`'s
// own note), so NO type in the port can implement `ITransformationContext`. The reference's transformer list is
// `TransformerFactory<T>[]`, i.e. `((context: TransformationContext) => Transformer<T>)[]`, and the generated
// `IEmitTransformers` spells the element type `Func<ITransformationContext, Func<object?, object?>>` — a delegate no
// callable value can be built for. The port therefore states the two shapes it *can* build, under the same ruling
// `IVisitorTransformationContext` and `ITransformationUtilitiesContext` record for their modules:
// <list type="bullet">
// <item><description><see cref="ITransformerContext"/> — the transformer-facing slice of `TransformationContext`, which
// extends <see cref="IVisitorTransformationContext"/> (its `Factory` is the concrete `NodeFactory`) and adds the
// fifteen members the visitor slice does not carry; it is the context every transformer factory of M7-B2…B7 takes, and
// it also extends <see cref="ITransformationUtilitiesContext"/> so `chainBundle` accepts it as it stands;</description></item>
// <item><description><see cref="EmitTransformers"/> — the port's `EmitTransformers`, the shape `getTransformers`
// answers and `emitFiles` destructures. It is stated here rather than taken from the generated `IEmitTransformers` for
// the reason above; the generator's fix is recorded in this slice's report.</description></item>
// </list>
internal static class Transformer
{
    /// <summary>ported from: src/compiler/transformer.ts:120-125 — <c>getTransformers</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference returns the object literal
    /// <c>{ scriptTransformers, declarationTransformers }</c>; the port states the same two slots on
    /// <see cref="EmitTransformers"/>. The two list builders below are this member's own body split out exactly as the
    /// reference splits it.
    /// </remarks>
    internal static EmitTransformers GetTransformers(
        ICompilerOptions compilerOptions,
        ICustomTransformers? customTransformers = null,
        object? emitOnly = null)
        => new()
        {
            ScriptTransformers = GetScriptTransformers(compilerOptions, customTransformers, emitOnly),
            DeclarationTransformers = GetDeclarationTransformers(customTransformers),
        };

    /// <summary>ported from: src/compiler/transformer.ts:127-190 — <c>getScriptTransformers</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>if (emitOnly) return emptyArray</c> shares the module-level empty array with
    /// <c>noTransformers</c>; <see cref="NoTransformers"/>.ScriptTransformers is that same value here (a
    /// <c>List&lt;…&gt;</c> instead of a frozen array), and nothing mutates what this member answers.
    /// <para>
    /// PORT-NOTE: <c>map(customTransformers.before, wrapScriptTransformerFactory)</c> — and the <c>after</c> half below —
    /// is stated as the explicit loop because <c>WrapScriptTransformerFactory</c> reads the element as the reference's
    /// two-shaped union (a factory delegate or a <c>CustomTransformerFactory</c>) through the port's <c>object?</c>.
    /// </para>
    /// </remarks>
    private static List<Func<ITransformerContext, Func<object?, object?>>> GetScriptTransformers(
        ICompilerOptions compilerOptions,
        ICustomTransformers? customTransformers,
        object? emitOnly)
    {
        if (Js.IsTruthy(emitOnly))
        {
            // PORT-NOTE: the reference's `emptyArray` is a non-null frozen array; the port's shared empty list lives on
            // the nullable `EmitTransformers` slot, and `noTransformers` is its only writer, so the assertion is the
            // reference's own guarantee.
            return NoTransformers.ScriptTransformers!;
        }

        var languageVersion = Utilities.GetEmitScriptTarget(compilerOptions);
        var moduleKind = Utilities.GetEmitModuleKind(compilerOptions);
        var useDefineForClassFields = Utilities.GetUseDefineForClassFields(compilerOptions);
        var transformers = new List<Func<ITransformerContext, Func<object?, object?>>>();

        if (customTransformers?.Before is { } before)
        {
            foreach (var transformer in before)
            {
                transformers.Add(WrapScriptTransformerFactory(transformer));
            }
        }

        transformers.Add(TypeScriptTransform.TransformTypeScript);

        if (Js.IsTruthy(compilerOptions.ExperimentalDecorators))
        {
            transformers.Add(LegacyDecorators.TransformLegacyDecorators);
        }

        if (Utilities.GetJSXTransformEnabled(compilerOptions))
        {
            transformers.Add(JsxTransform.TransformJsx);
        }

        if (languageVersion < ScriptTarget.ESNext)
        {
            transformers.Add(ESNextTransform.TransformESNext);
        }

        if (!Js.IsTruthy(compilerOptions.ExperimentalDecorators)
            && (languageVersion < ScriptTarget.ESNext || !useDefineForClassFields))
        {
            transformers.Add(ESDecorators.TransformESDecorators);
        }

        transformers.Add(ClassFieldsTransform.TransformClassFields);

        if (languageVersion < ScriptTarget.ES2021)
        {
            transformers.Add(ES2021Transform.TransformES2021);
        }

        if (languageVersion < ScriptTarget.ES2020)
        {
            transformers.Add(ES2020Transform.TransformES2020);
        }

        if (languageVersion < ScriptTarget.ES2019)
        {
            transformers.Add(ES2019Transform.TransformES2019);
        }

        if (languageVersion < ScriptTarget.ES2018)
        {
            transformers.Add(ES2018Transform.TransformES2018);
        }

        if (languageVersion < ScriptTarget.ES2017)
        {
            transformers.Add(ES2017Transform.TransformES2017);
        }

        if (languageVersion < ScriptTarget.ES2016)
        {
            transformers.Add(ES2016Transform.TransformES2016);
        }

        if (languageVersion < ScriptTarget.ES2015)
        {
            transformers.Add(ES2015Transform.TransformES2015);
            transformers.Add(GeneratorsTransform.TransformGenerators);
        }

        transformers.Add(GetModuleTransformer(moduleKind));

        if (customTransformers?.After is { } after)
        {
            foreach (var transformer in after)
            {
                transformers.Add(WrapScriptTransformerFactory(transformer));
            }
        }

        return transformers;
    }

    /// <summary>ported from: src/compiler/transformer.ts:192-197 — <c>getDeclarationTransformers</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>addRange(…, map(customTransformers.afterDeclarations, …))</c> is the
    /// explicit loop, for the reason <see cref="GetScriptTransformers"/> records.</remarks>
    private static List<Func<ITransformerContext, Func<object?, object?>>> GetDeclarationTransformers(ICustomTransformers? customTransformers)
    {
        var transformers = new List<Func<ITransformerContext, Func<object?, object?>>>
        {
            DeclarationsTransform.TransformDeclarations,
        };

        if (customTransformers?.AfterDeclarations is { } afterDeclarations)
        {
            foreach (var transformer in afterDeclarations)
            {
                transformers.Add(WrapDeclarationTransformerFactory(transformer));
            }
        }

        return transformers;
    }

    /// <summary>ported from: src/compiler/transformer.ts:78-116 — <c>getModuleTransformer</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's switch is stated by the module slice itself — the selection belongs to
    /// <see cref="ImpliedNodeFormatDependent.GetModuleTransformer"/>, which quotes this member's line range — so this
    /// member defers to it rather than holding a second copy of the switch.</remarks>
    private static Func<ITransformerContext, Func<object?, object?>> GetModuleTransformer(ModuleKind moduleKind)
        => ImpliedNodeFormatDependent.GetModuleTransformer(moduleKind);

    /// <summary>ported from: src/compiler/transformer.ts:117 — <c>noTransformers</c>.</summary>
    /// <remarks>PORT-NOTE: TS shares <c>emptyArray</c> between the two slots; this instance is the same value, and
    /// <c>emitBuildInfo</c> is its only reader (it transforms nothing).</remarks>
    internal static readonly EmitTransformers NoTransformers = new()
    {
        ScriptTransformers = [],
        DeclarationTransformers = [],
    };

    /// <summary>ported from: src/compiler/transformer.ts:202-204 — <c>wrapCustomTransformer</c>.</summary>
    /// <remarks>PORT-NOTE: TS's body is <c>node =&gt; isBundle(node) ? transformer.transformBundle(node) :
    /// transformer.transformSourceFile(node)</c>, whose parameter is the union <c>Bundle | SourceFile</c>; the port's
    /// erased transformer face is <c>object?</c>, so the two arms narrow with the kind tests the reference's
    /// <c>isBundle</c> and the target methods' own types imply.</remarks>
    internal static Func<object?, object?> WrapCustomTransformer(ICustomTransformer transformer)
        => node => NodeTests.IsBundle((Node)node!)
            ? transformer.TransformBundle((Bundle?)node)
            : transformer.TransformSourceFile((SourceFile?)node);

    /// <summary>ported from: src/compiler/transformer.ts:209-216 — <c>wrapCustomTransformerFactory</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>typeof customTransformer === "function"</c> is the port's test for the transformer delegate;
    /// the reference's <c>T</c> (a <c>SourceFile | Bundle</c> instantiation) is erased to <c>object?</c> here because
    /// that is how the transformer face's slots are spelled, and the two <c>handleDefault</c> arms (<c>chainBundle</c>
    /// and <c>(_, node) =&gt; node</c>) are the ones the two callers below supply.
    /// </remarks>
    private static Func<ITransformerContext, Func<object?, object?>> WrapCustomTransformerFactory(
        object? transformer,
        Func<ITransformerContext, Func<object?, object?>, Func<object?, object?>> handleDefault)
        => context =>
        {
            var customTransformer = ((Func<ITransformerContext, object?>)transformer!)(context);
            return customTransformer is Func<object?, object?> customTransformerAsFunction
                ? handleDefault(context, customTransformerAsFunction)
                : WrapCustomTransformer((ICustomTransformer)customTransformer!);
        };

    /// <summary>ported from: src/compiler/transformer.ts:218-220 — <c>wrapScriptTransformerFactory</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's fallback is the module-level <c>chainBundle</c>
    /// (<c>transformers/utilities.ts:135</c>), which the port states as
    /// <see cref="TransformationUtilities.ChainBundle"/>; its delegate is the bundle-aware <c>Func&lt;Node, Node&gt;</c>,
    /// adapted here to the erased transformer face.
    /// </remarks>
    private static Func<ITransformerContext, Func<object?, object?>> WrapScriptTransformerFactory(object? transformer)
        => WrapCustomTransformerFactory(transformer, (context, tx) =>
        {
            var chained = TransformationUtilities.ChainBundle(context, sourceFile => (SourceFile)tx(sourceFile)!);
            return node => chained((Node)node!);
        });

    /// <summary>ported from: src/compiler/transformer.ts:222-224 — <c>wrapDeclarationTransformerFactory</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's fallback is <c>(_, node) =&gt; node</c> — the identity transformer.</remarks>
    private static Func<ITransformerContext, Func<object?, object?>> WrapDeclarationTransformerFactory(object? transformer)
        => WrapCustomTransformerFactory(transformer, (_, node) => node);

    /// <summary>ported from: src/compiler/transformer.ts:248-668 — <c>transformNodes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS states this as one function whose body builds the context literal and returns the result literal;
    /// the port's <see cref="TransformationContext{T}"/> is that first literal (its state included) and
    /// <see cref="TransformationContext{T}.Transform"/> is this member's body, for the same reason the reference's own
    /// closures exist (docs/architecture.md §5 rule 6).
    /// <para>
    /// PORT-NOTE: <c>noEmitSubstitution</c>/<c>noEmitNotification</c> (<c>transformer.ts:227-234</c>, the two helpers
    /// between <c>wrapDeclarationTransformerFactory</c> and this member) are owned by <c>Printer</c> — they are
    /// <see cref="Printer.NoEmitSubstitution"/>/<see cref="Printer.NoEmitNotification"/>, per that class's own header
    /// — so this file calls them rather than declaring a second copy.
    /// </para>
    /// </remarks>
    internal static ITransformationResult<T> TransformNodes<T>(
        IEmitResolver? resolver,
        IEmitHost? host,
        NodeFactory factory,
        ICompilerOptions options,
        List<T> nodes,
        List<Func<ITransformerContext, Func<object?, object?>>> transformers,
        bool allowDtsFiles)
        where T : Node
        => new TransformationContext<T>(resolver, host, factory, options, nodes, transformers, allowDtsFiles).Transform();

}

// ported from: src/compiler/transformer.ts:104-114 — the two const enums `transformNodes` closes over.
/// <summary>ported from: src/compiler/transformer.ts:104-109 — <c>TransformationState</c>.</summary>
internal enum TransformationState
{
    Uninitialized,
    Initialized,
    Completed,
    Disposed,
}

/// <summary>ported from: src/compiler/transformer.ts:111-114 — <c>SyntaxKindFeatureFlags</c>.</summary>
[Flags]
internal enum SyntaxKindFeatureFlags
{
    Substitution = 1 << 0,
    EmitNotifications = 1 << 1,
}

/// <summary>ported from: src/compiler/types.ts:9558-9584 — the transformer-facing slice of
/// <c>CoreTransformationContext</c>/<c>TransformationContext</c>.</summary>
/// <remarks>
/// PORT-NOTE: TS types every transformer factory's parameter as the full <c>TransformationContext</c>, which no port
/// type can implement (see the file header), so this is the same treatment <see cref="IVisitorTransformationContext"/>
/// and <see cref="ITransformationUtilitiesContext"/> record for their modules: the members the transformers read, with
/// the concrete <see cref="NodeFactory"/>. It extends both of those slices so one object satisfies all three faces.
/// </remarks>
internal interface ITransformerContext : IVisitorTransformationContext, ITransformationUtilitiesContext
{
    /// <summary>ported from: src/compiler/types.ts:9567 — <c>CoreTransformationContext.hoistFunctionDeclaration</c>.</summary>
    void HoistFunctionDeclaration(FunctionDeclaration? node);

    /// <summary>ported from: src/compiler/types.ts:9570 — <c>CoreTransformationContext.addBlockScopedVariable</c>.</summary>
    void AddBlockScopedVariable(Identifier? node);

    /// <summary>ported from: src/compiler/types.ts:9573 — <c>TransformationContext.getEmitHost</c>.</summary>
    IEmitHost GetEmitHost();

    /// <summary>ported from: src/compiler/types.ts:9575 — <c>TransformationContext.requestEmitHelper</c>.</summary>
    void RequestEmitHelper(Node? helper);

    /// <summary>ported from: src/compiler/types.ts:9576 — <c>TransformationContext.readEmitHelpers</c>.</summary>
    List<Node>? ReadEmitHelpers();

    /// <summary>ported from: src/compiler/types.ts:9577 — <c>TransformationContext.enableSubstitution</c>.</summary>
    void EnableSubstitution(SyntaxKind kind);

    /// <summary>ported from: src/compiler/types.ts:9578 — <c>TransformationContext.isSubstitutionEnabled</c>.</summary>
    bool IsSubstitutionEnabled(Node? node);

    /// <summary>ported from: src/compiler/types.ts:9579 — <c>TransformationContext.onSubstituteNode</c>.</summary>
    Func<EmitHint, Node, Node>? OnSubstituteNode { get; set; }

    /// <summary>ported from: src/compiler/types.ts:9580 — <c>TransformationContext.enableEmitNotification</c>.</summary>
    void EnableEmitNotification(SyntaxKind kind);

    /// <summary>ported from: src/compiler/types.ts:9581 — <c>TransformationContext.isEmitNotificationEnabled</c>.</summary>
    bool IsEmitNotificationEnabled(Node? node);

    /// <summary>ported from: src/compiler/types.ts:9582 — <c>TransformationContext.onEmitNode</c>.</summary>
    Action<EmitHint, Node, Action<EmitHint, Node>>? OnEmitNode { get; set; }

    /// <summary>ported from: src/compiler/types.ts:9584 — <c>TransformationContext.addDiagnostic</c>.</summary>
    void AddDiagnostic(IDiagnosticWithLocation? diag);
}

/// <summary>ported from: src/compiler/types.ts:8754-8757 — <c>EmitTransformers</c>.</summary>
/// <remarks>
/// PORT-NOTE: the generated <see cref="IEmitTransformers"/> spells its two slots
/// <c>List&lt;Func&lt;ITransformationContext, Func&lt;object?, object?&gt;&gt;&gt;?</c>, and no value of that type can be
/// built or invoked (see the file header). This class is the port's shape for the same two lists — the one
/// <c>getTransformers</c> answers, <c>noTransformers</c> is, and <c>emitFiles</c> destructures — with the context typed
/// by <see cref="ITransformerContext"/>. The generator's fix is recorded in this slice's report.
/// </remarks>
internal sealed class EmitTransformers
{
    /// <summary>ported from: src/compiler/types.ts:8755 — <c>scriptTransformers</c>.</summary>
    public List<Func<ITransformerContext, Func<object?, object?>>>? ScriptTransformers { get; set; }

    /// <summary>ported from: src/compiler/types.ts:8756 — <c>declarationTransformers</c>.</summary>
    public List<Func<ITransformerContext, Func<object?, object?>>>? DeclarationTransformers { get; set; }
}

/// <summary>ported from: src/compiler/transformer.ts:271-334 — the <c>TransformationContext</c> object literal.</summary>
/// <remarks>
/// PORT-NOTE: the reference's literal is a table of closures over <c>transformNodes</c>'s locals; this class holds the
/// same state and states the same members, so it is also the object the reference calls <c>context</c>. It implements
/// <see cref="IEmitHelperFactoryContext"/> because the reference's <c>getEmitHelperFactory</c> memoizes
/// <c>createEmitHelperFactory(context)</c> over this very object.
/// </remarks>
internal sealed class TransformationContext<T> : ITransformerContext, IEmitHelperFactoryContext
    where T : Node
{
    private readonly IEmitResolver? _resolver;
    private readonly IEmitHost? _host;
    private readonly ICompilerOptions _options;
    private readonly List<T> _nodes;
    private readonly List<Func<ITransformerContext, Func<object?, object?>>> _transformers;
    private readonly bool _allowDtsFiles;
    private List<Func<object?, object?>> _transformersWithContext = [];

    /// <summary>ported from: src/compiler/transformer.ts:249 — <c>enabledSyntaxKindFeatures</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>new Array(SyntaxKind.Count)</c> is a hole-filled array whose holes read as
    /// <c>undefined</c> and participate in <c>|=</c> as <c>0</c>; a C# array starts at the enum's zero member, so the
    /// two readings agree.</remarks>
    private readonly SyntaxKindFeatureFlags[] _enabledSyntaxKindFeatures = new SyntaxKindFeatureFlags[(int)SyntaxKind.Count];

    private List<VariableDeclaration>? _lexicalEnvironmentVariableDeclarations;
    private List<FunctionDeclaration>? _lexicalEnvironmentFunctionDeclarations;
    private List<IStatement>? _lexicalEnvironmentStatements;
    private LexicalEnvironmentFlags _lexicalEnvironmentFlags = LexicalEnvironmentFlags.None;

    /// <summary>ported from: src/compiler/transformer.ts:250-262 — the four environment/scope stacks.</summary>
    /// <remarks>PORT-NOTE: TS uses a hole-filled array indexed by a stack-offset variable that it moves up and down
    /// (<c>stack[offset] = x</c>, then <c>offset++</c>/<c>offset--</c>), so a slot can be written or re-written without
    /// the array growing. A <see cref="List{T}"/> has no such index assignment, so each push goes through
    /// <see cref="SetStackSlot{TItem}"/>, which appends when the offset is the current length and overwrites otherwise —
    /// the reference's two cases. The reference's <c>stacks = []</c> reset at offset 0 is <see cref="List{T}.Clear"/>
    /// via a fresh list, which is what the reference's own <c>[]</c> does.</remarks>
    private List<List<VariableDeclaration>?> _lexicalEnvironmentVariableDeclarationsStack = [];
    private List<List<FunctionDeclaration>?> _lexicalEnvironmentFunctionDeclarationsStack = [];
    private List<List<IStatement>?> _lexicalEnvironmentStatementsStack = [];
    private List<LexicalEnvironmentFlags> _lexicalEnvironmentFlagsStack = [];
    private int _lexicalEnvironmentStackOffset;
    private bool _lexicalEnvironmentSuspended;
    private List<List<Identifier>?> _blockScopedVariableDeclarationsStack = [];
    private int _blockScopeStackOffset;
    private List<Identifier>? _blockScopedVariableDeclarations;
    private List<EmitHelper>? _emitHelpers;
    private Func<EmitHint, Node, Node> _onSubstituteNode = Printer.NoEmitSubstitution;
    private Action<EmitHint, Node, Action<EmitHint, Node>> _onEmitNode = Printer.NoEmitNotification;
    private TransformationState _state = TransformationState.Uninitialized;
    private IEmitHelperFactory? _emitHelperFactory;

    public TransformationContext(
        IEmitResolver? resolver,
        IEmitHost? host,
        NodeFactory factory,
        ICompilerOptions options,
        List<T> nodes,
        List<Func<ITransformerContext, Func<object?, object?>>> transformers,
        bool allowDtsFiles)
    {
        _resolver = resolver;
        _host = host;
        Factory = factory;
        _options = options;
        _nodes = nodes;
        _transformers = transformers;
        _allowDtsFiles = allowDtsFiles;
    }

    /// <summary>ported from: src/compiler/transformer.ts:272 — <c>factory</c>.</summary>
    public NodeFactory Factory { get; }

    /// <summary>ported from: src/compiler/transformer.ts:273 — <c>getCompilerOptions: () =&gt; options</c>.</summary>
    public ICompilerOptions GetCompilerOptions() => _options;

    /// <summary>ported from: src/compiler/transformer.ts:274 — <c>getEmitResolver: () =&gt; resolver!</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>resolver!</c> is the same assertion; <c>emitFiles</c> is what supplies the value.</remarks>
    public IEmitResolver GetEmitResolver() => _resolver!;

    /// <summary>ported from: src/compiler/transformer.ts:275 — <c>getEmitHost: () =&gt; host!</c>.</summary>
    public IEmitHost GetEmitHost() => _host!;

    /// <summary>ported from: src/compiler/transformer.ts:276 — <c>getEmitHelperFactory</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>memoize(() =&gt; createEmitHelperFactory(context))</c> is stated as the nullable slot
    /// it fills in — the "memoize as a field" treatment <c>NodeFactory</c>'s parenthesizer rules record — and
    /// <c>createEmitHelperFactory</c> is <see cref="EmitHelpers.CreateEmitHelperFactory"/>.</remarks>
    public IEmitHelperFactory GetEmitHelperFactory() => _emitHelperFactory ??= EmitHelpers.CreateEmitHelperFactory(this);

    /// <summary>ported from: src/compiler/transformer.ts:480-499 — <c>startLexicalEnvironment</c>.</summary>
    public void StartLexicalEnvironment()
    {
        Debug.Assert(_state > TransformationState.Uninitialized, "Cannot modify the lexical environment during initialization.");
        Debug.Assert(_state < TransformationState.Completed, "Cannot modify the lexical environment after transformation has completed.");
        Debug.Assert(!_lexicalEnvironmentSuspended, "Lexical environment is suspended.");

        // Save the current lexical environment. Rather than resizing the array we adjust the
        // stack size variable. This allows us to reuse existing array slots we've
        // already allocated between transformations to avoid allocation and GC overhead during
        // transformation.
        SetStackSlot(_lexicalEnvironmentVariableDeclarationsStack, _lexicalEnvironmentStackOffset, _lexicalEnvironmentVariableDeclarations);
        SetStackSlot(_lexicalEnvironmentFunctionDeclarationsStack, _lexicalEnvironmentStackOffset, _lexicalEnvironmentFunctionDeclarations);
        SetStackSlot(_lexicalEnvironmentStatementsStack, _lexicalEnvironmentStackOffset, _lexicalEnvironmentStatements);
        SetStackSlot(_lexicalEnvironmentFlagsStack, _lexicalEnvironmentStackOffset, _lexicalEnvironmentFlags);
        _lexicalEnvironmentStackOffset++;
        _lexicalEnvironmentVariableDeclarations = null;
        _lexicalEnvironmentFunctionDeclarations = null;
        _lexicalEnvironmentStatements = null;
        _lexicalEnvironmentFlags = LexicalEnvironmentFlags.None;
    }

    /// <summary>ported from: src/compiler/transformer.ts:501-507 — <c>suspendLexicalEnvironment</c>.</summary>
    public void SuspendLexicalEnvironment()
    {
        Debug.Assert(_state > TransformationState.Uninitialized, "Cannot modify the lexical environment during initialization.");
        Debug.Assert(_state < TransformationState.Completed, "Cannot modify the lexical environment after transformation has completed.");
        Debug.Assert(!_lexicalEnvironmentSuspended, "Lexical environment is already suspended.");
        _lexicalEnvironmentSuspended = true;
    }

    /// <summary>ported from: src/compiler/transformer.ts:509-518 — <c>resumeLexicalEnvironment</c>.</summary>
    public void ResumeLexicalEnvironment()
    {
        Debug.Assert(_state > TransformationState.Uninitialized, "Cannot modify the lexical environment during initialization.");
        Debug.Assert(_state < TransformationState.Completed, "Cannot modify the lexical environment after transformation has completed.");
        Debug.Assert(_lexicalEnvironmentSuspended, "Lexical environment is not suspended.");
        _lexicalEnvironmentSuspended = false;
    }

    /// <summary>ported from: src/compiler/transformer.ts:520-574 — <c>endLexicalEnvironment</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>Statement</c> is a union the port models as the interface <see cref="IStatement"/>
    /// (there is no <c>Statement</c> class), which is also the slot's generated element type, so no widening is needed;
    /// <c>setEmitFlags</c> is <see cref="EmitNodes.SetEmitFlags{T}"/>, whose constraint is the concrete
    /// <see cref="Node"/> the interface is not, and the reference's one-argument
    /// <c>createVariableDeclarationList</c>/<c>createVariableStatement</c> calls are the port's two-argument forms with
    /// the absent arguments stated.</remarks>
    public List<IStatement>? EndLexicalEnvironment()
    {
        Debug.Assert(_state > TransformationState.Uninitialized, "Cannot modify the lexical environment during initialization.");
        Debug.Assert(_state < TransformationState.Completed, "Cannot modify the lexical environment after transformation has completed.");
        Debug.Assert(!_lexicalEnvironmentSuspended, "Lexical environment is suspended.");

        List<IStatement>? statements = null;
        if (
            _lexicalEnvironmentVariableDeclarations is not null
            || _lexicalEnvironmentFunctionDeclarations is not null
            || _lexicalEnvironmentStatements is not null
        )
        {
            if (_lexicalEnvironmentFunctionDeclarations is not null)
            {
                statements = [.. _lexicalEnvironmentFunctionDeclarations];
            }

            if (_lexicalEnvironmentVariableDeclarations is not null)
            {
                var statement = Factory.CreateVariableStatement(
                    /*modifiers*/ null,
                    Factory.CreateVariableDeclarationList(_lexicalEnvironmentVariableDeclarations));

                EmitNodes.SetEmitFlags(statement, EmitFlags.CustomPrologue);

                if (statements is null)
                {
                    statements = [statement];
                }
                else
                {
                    statements.Add(statement);
                }
            }

            if (_lexicalEnvironmentStatements is not null)
            {
                if (statements is null)
                {
                    statements = [.. _lexicalEnvironmentStatements];
                }
                else
                {
                    statements.AddRange(_lexicalEnvironmentStatements);
                }
            }
        }

        // Restore the previous lexical environment.
        _lexicalEnvironmentStackOffset--;
        _lexicalEnvironmentVariableDeclarations = _lexicalEnvironmentVariableDeclarationsStack[_lexicalEnvironmentStackOffset];
        _lexicalEnvironmentFunctionDeclarations = _lexicalEnvironmentFunctionDeclarationsStack[_lexicalEnvironmentStackOffset];
        _lexicalEnvironmentStatements = _lexicalEnvironmentStatementsStack[_lexicalEnvironmentStackOffset];
        _lexicalEnvironmentFlags = _lexicalEnvironmentFlagsStack[_lexicalEnvironmentStackOffset];
        if (_lexicalEnvironmentStackOffset == 0)
        {
            _lexicalEnvironmentVariableDeclarationsStack = [];
            _lexicalEnvironmentFunctionDeclarationsStack = [];
            _lexicalEnvironmentStatementsStack = [];
            _lexicalEnvironmentFlagsStack = [];
        }

        return statements is null ? null : [.. statements];
    }

    /// <summary>ported from: src/compiler/transformer.ts:576-580 — <c>setLexicalEnvironmentFlags</c>.</summary>
    public void SetLexicalEnvironmentFlags(LexicalEnvironmentFlags flags, bool value)
        => _lexicalEnvironmentFlags = value
            ? _lexicalEnvironmentFlags | flags
            : _lexicalEnvironmentFlags & ~flags;

    /// <summary>ported from: src/compiler/transformer.ts:582-584 — <c>getLexicalEnvironmentFlags</c>.</summary>
    public LexicalEnvironmentFlags GetLexicalEnvironmentFlags() => _lexicalEnvironmentFlags;

    /// <summary>ported from: src/compiler/transformer.ts:589-598 — <c>startBlockScope</c>.</summary>
    public void StartBlockScope()
    {
        Debug.Assert(_state > TransformationState.Uninitialized, "Cannot start a block scope during initialization.");
        Debug.Assert(_state < TransformationState.Completed, "Cannot start a block scope after transformation has completed.");
        SetStackSlot(_blockScopedVariableDeclarationsStack, _blockScopeStackOffset, _blockScopedVariableDeclarations);
        _blockScopeStackOffset++;
        _blockScopedVariableDeclarations = null;
    }

    /// <summary>ported from: src/compiler/transformer.ts:600-619 — <c>endBlockScope</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>some(blockScopedVariableDeclarations)</c> is <see cref="Core.Some{T}"/> and its
    /// <c>map</c> is <see cref="Core.Map{T,TU}"/> (whose selector takes the index — the reference's callback ignores
    /// it); <c>NodeFlags.Let</c> is the declaration list's flag. The reference's
    /// <c>createVariableDeclaration(identifier)</c> is the port's four-argument form.</remarks>
    public List<IStatement>? EndBlockScope()
    {
        Debug.Assert(_state > TransformationState.Uninitialized, "Cannot end a block scope during initialization.");
        Debug.Assert(_state < TransformationState.Completed, "Cannot end a block scope after transformation has completed.");
        var statements = Core.Some(_blockScopedVariableDeclarations)
            ? new List<IStatement>
            {
                Factory.CreateVariableStatement(
                    /*modifiers*/ null,
                    Factory.CreateVariableDeclarationList(
                        Core.Map(_blockScopedVariableDeclarations!, (identifier, _) => Factory.CreateVariableDeclaration(identifier, null, null, null))!,
                        NodeFlags.Let)),
            }
            : null;
        _blockScopeStackOffset--;
        _blockScopedVariableDeclarations = _blockScopedVariableDeclarationsStack[_blockScopeStackOffset];
        if (_blockScopeStackOffset == 0)
        {
            _blockScopedVariableDeclarationsStack = [];
        }

        return statements;
    }

    /// <summary>ported from: src/compiler/transformer.ts:621-624 — <c>addBlockScopedVariable</c>.</summary>
    public void AddBlockScopedVariable(Identifier? node)
    {
        Debug.Assert(_blockScopeStackOffset > 0, "Cannot add a block scoped variable outside of an iteration body.");
        (_blockScopedVariableDeclarations ??= []).Add(node!);
    }

    /// <summary>ported from: src/compiler/transformer.ts:431-445 — <c>hoistVariableDeclaration</c>.</summary>
    public void HoistVariableDeclaration(Identifier? node)
    {
        Debug.Assert(_state > TransformationState.Uninitialized, "Cannot modify the lexical environment during initialization.");
        Debug.Assert(_state < TransformationState.Completed, "Cannot modify the lexical environment after transformation has completed.");
        var decl = EmitNodes.SetEmitFlags(Factory.CreateVariableDeclaration(node, null, null, null), EmitFlags.NoNestedSourceMaps);
        if (_lexicalEnvironmentVariableDeclarations is null)
        {
            _lexicalEnvironmentVariableDeclarations = [decl];
        }
        else
        {
            _lexicalEnvironmentVariableDeclarations.Add(decl);
        }

        if ((_lexicalEnvironmentFlags & LexicalEnvironmentFlags.InParameters) != 0)
        {
            _lexicalEnvironmentFlags |= LexicalEnvironmentFlags.VariablesHoistedInParameters;
        }
    }

    /// <summary>ported from: src/compiler/transformer.ts:449-462 — <c>hoistFunctionDeclaration</c>.</summary>
    public void HoistFunctionDeclaration(FunctionDeclaration? node)
    {
        Debug.Assert(_state > TransformationState.Uninitialized, "Cannot modify the lexical environment during initialization.");
        Debug.Assert(_state < TransformationState.Completed, "Cannot modify the lexical environment after transformation has completed.");
        EmitNodes.SetEmitFlags(node!, EmitFlags.CustomPrologue);
        if (_lexicalEnvironmentFunctionDeclarations is null)
        {
            _lexicalEnvironmentFunctionDeclarations = [node!];
        }
        else
        {
            _lexicalEnvironmentFunctionDeclarations.Add(node!);
        }
    }

    /// <summary>ported from: src/compiler/transformer.ts:464-475 — <c>addInitializationStatement</c>.</summary>
    /// <remarks>PORT-NOTE: the slot's parameter is the generated <c>IStatement?</c> (TS's <c>Statement</c> union) while
    /// <c>setEmitFlags</c> is constrained to the concrete <see cref="Node"/>, so the descriptor is the same object seen
    /// through the class.</remarks>
    public void AddInitializationStatement(IStatement? node)
    {
        Debug.Assert(_state > TransformationState.Uninitialized, "Cannot modify the lexical environment during initialization.");
        Debug.Assert(_state < TransformationState.Completed, "Cannot modify the lexical environment after transformation has completed.");
        EmitNodes.SetEmitFlags((Node)node!, EmitFlags.CustomPrologue);
        if (_lexicalEnvironmentStatements is null)
        {
            _lexicalEnvironmentStatements = [node!];
        }
        else
        {
            _lexicalEnvironmentStatements.Add(node!);
        }
    }

    /// <summary>ported from: src/compiler/transformer.ts:626-636 — <c>requestEmitHelper</c>.</summary>
    /// <remarks>PORT-NOTE: the slot's parameter is <c>Node?</c> (the generated model's element type — see
    /// <see cref="EmitHelper"/>'s note), so the descriptor is recovered with a cast; <c>append</c> is
    /// <see cref="Core.Append{T}"/>, whose answer is the <c>IList</c> face.</remarks>
    public void RequestEmitHelper(Node? helper)
    {
        Debug.Assert(_state > TransformationState.Uninitialized, "Cannot modify the transformation context during initialization.");
        Debug.Assert(_state < TransformationState.Completed, "Cannot modify the transformation context after transformation has completed.");
        var emitHelper = (EmitHelper)helper!;
        Debug.Assert(!emitHelper.Scoped, "Cannot request a scoped emit helper.");
        if (emitHelper.Dependencies is not null)
        {
            foreach (var h in emitHelper.Dependencies)
            {
                RequestEmitHelper(h);
            }
        }

        _emitHelpers = (List<EmitHelper>)Core.Append(_emitHelpers, emitHelper)!;
    }

    /// <summary>ported from: src/compiler/transformer.ts:638-644 — <c>readEmitHelpers</c>.</summary>
    /// <remarks>PORT-NOTE: the reference hands the array out and clears the slot; the slot's generated element type is
    /// <c>Node</c>, so the helpers are widened once on return.</remarks>
    public List<Node>? ReadEmitHelpers()
    {
        Debug.Assert(_state > TransformationState.Uninitialized, "Cannot modify the transformation context during initialization.");
        Debug.Assert(_state < TransformationState.Completed, "Cannot modify the transformation context after transformation has completed.");
        var helpers = _emitHelpers;
        _emitHelpers = null;
        return helpers is null ? null : [.. helpers];
    }

    /// <summary>ported from: src/compiler/transformer.ts:365-371 — <c>enableSubstitution</c>.</summary>
    public void EnableSubstitution(SyntaxKind kind)
    {
        Debug.Assert(_state < TransformationState.Completed, "Cannot modify the transformation context after transformation has completed.");
        _enabledSyntaxKindFeatures[(int)kind] |= SyntaxKindFeatureFlags.Substitution;
    }

    /// <summary>ported from: src/compiler/transformer.ts:373-377 — <c>isSubstitutionEnabled</c>.</summary>
    public bool IsSubstitutionEnabled(Node? node)
        => (_enabledSyntaxKindFeatures[(int)node!.Kind] & SyntaxKindFeatureFlags.Substitution) != 0
            && (Utilities.GetEmitFlags(node) & EmitFlags.NoSubstitution) == 0;

    /// <summary>ported from: src/compiler/transformer.ts:385-388 — <c>substituteNode</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>node &amp;&amp; isSubstitutionEnabled(node) &amp;&amp; onSubstituteNode(hint, node) ||
    /// node</c> yields a value from a boolean chain (JS's <c>&amp;&amp;</c>/<c>||</c> return operands), so the port states
    /// the same three arms: a null node, the disabled case and the substituted case, each answering what the reference's
    /// expression answers. The trailing <c>?? node</c> is the reference's <c>|| node</c> for a substitute callback that
    /// answers nothing.</remarks>
    public Node SubstituteNode(EmitHint hint, Node? node)
    {
        Debug.Assert(_state < TransformationState.Disposed, "Cannot substitute a node after the result is disposed.");
        if (node is null) return node!;
        return IsSubstitutionEnabled(node) ? _onSubstituteNode(hint, node) ?? node : node;
    }

    /// <summary>ported from: src/compiler/transformer.ts:393-397 — <c>enableEmitNotification</c>.</summary>
    public void EnableEmitNotification(SyntaxKind kind)
    {
        Debug.Assert(_state < TransformationState.Completed, "Cannot modify the transformation context after transformation has completed.");
        _enabledSyntaxKindFeatures[(int)kind] |= SyntaxKindFeatureFlags.EmitNotifications;
    }

    /// <summary>ported from: src/compiler/transformer.ts:402-405 — <c>isEmitNotificationEnabled</c>.</summary>
    public bool IsEmitNotificationEnabled(Node? node)
        => (_enabledSyntaxKindFeatures[(int)node!.Kind] & SyntaxKindFeatureFlags.EmitNotifications) != 0
            || (Utilities.GetEmitFlags(node) & EmitFlags.AdviseOnEmitNode) != 0;

    /// <summary>ported from: src/compiler/transformer.ts:414-425 — <c>emitNodeWithNotification</c>.</summary>
    public void EmitNodeWithNotification(EmitHint hint, Node? node, Action<EmitHint, Node>? emitCallback)
    {
        Debug.Assert(_state < TransformationState.Disposed, "Cannot invoke TransformationResult callbacks after the result is disposed.");
        if (node is not null)
        {
            // TODO: Remove check and unconditionally use onEmitNode when API is breakingly changed
            // (see https://github.com/microsoft/TypeScript/pull/36248/files/5062623f39120171b98870c71344b3242eb03d23#r369766739)
            if (IsEmitNotificationEnabled(node))
            {
                _onEmitNode(hint, node, emitCallback!);
            }
            else
            {
                emitCallback!(hint, node);
            }
        }
    }

    /// <summary>ported from: src/compiler/transformer.ts:295-301 — <c>get onSubstituteNode</c>.</summary>
    public Func<EmitHint, Node, Node>? OnSubstituteNode
    {
        get => _onSubstituteNode;
        set
        {
            Debug.Assert(_state < TransformationState.Initialized, "Cannot modify transformation hooks after initialization has completed.");
            Debug.AssertIsDefined(value, "Value must not be 'undefined'");
            _onSubstituteNode = value;
        }
    }

    /// <summary>ported from: src/compiler/transformer.ts:303-309 — <c>get onEmitNode</c>.</summary>
    public Action<EmitHint, Node, Action<EmitHint, Node>>? OnEmitNode
    {
        get => _onEmitNode;
        set
        {
            Debug.Assert(_state < TransformationState.Initialized, "Cannot modify transformation hooks after initialization has completed.");
            Debug.AssertIsDefined(value, "Value must not be 'undefined'");
            _onEmitNode = value;
        }
    }

    /// <summary>ported from: src/compiler/transformer.ts:310-312 — <c>addDiagnostic</c>.</summary>
    public void AddDiagnostic(IDiagnosticWithLocation? diag) => Diagnostics.Add(diag!);

    /// <summary>ported from: src/compiler/transformer.ts:267 — <c>diagnostics</c>, the array the result hands out.</summary>
    internal List<IDiagnosticWithLocation> Diagnostics { get; } = [];

    /// <summary>ported from: src/compiler/transformer.ts:336-357 — the body of <c>transformNodes</c>.</summary>
    /// <remarks>PORT-NOTE: TS's local <c>transformation</c>/<c>transformRoot</c> pair is stated as the two private
    /// members below; the chained transformer list is a field rather than a local because the two of them read it.</remarks>
    public ITransformationResult<T> Transform()
    {
        // Ensure the parse tree is clean before applying transformations
        foreach (var node in _nodes)
        {
            EmitNodes.DisposeEmitNodes(Utilities.GetSourceFileOfNode(Utilities.GetParseTreeNode(node)));
        }

        Performance.Mark("beforeTransform");

        // Chain together and initialize each transformer.
        _transformersWithContext = Core.Map(_transformers, (t, _) => t(this))!;

        // prevent modification of transformation hooks.
        _state = TransformationState.Initialized;

        // Transform each node.
        var transformed = new List<T>();
        foreach (var node in _nodes)
        {
            Tracing.Current?.Push(
                Tracing.Phase.Emit,
                "transformNodes",
                node.Kind == SyntaxKind.SourceFile
                    ? TracingArgs.Of(("path", ((SourceFile)(object)node).Path))
                    : TracingArgs.Of(("kind", (double)node.Kind), ("pos", (double)node.Pos), ("end", (double)node.End)));
            transformed.Add(_allowDtsFiles ? Transformation(node) : TransformRoot(node));
            Tracing.Current?.Pop();
        }

        // prevent modification of the lexical environment.
        _state = TransformationState.Completed;

        Performance.Mark("afterTransform");
        Performance.Measure("transformTime", "beforeTransform", "afterTransform");

        return new TransformationResult<T>(this, transformed);
    }

    /// <summary>ported from: src/compiler/transformer.ts:339-345 — <c>transformation</c>.</summary>
    private T Transformation(T node)
    {
        foreach (var transform in _transformersWithContext)
        {
            node = (T)transform(node)!;
        }

        return node;
    }

    /// <summary>ported from: src/compiler/transformer.ts:358-360 — <c>transformRoot</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>node &amp;&amp;</c> guard is the port's <c>is not null</c>; its
    /// <c>!isSourceFile(node) || !node.isDeclarationFile</c> is the same two tests with
    /// <see cref="NodeTests.IsSourceFile"/>.</remarks>
    private T TransformRoot(T node)
        => node is not null && (!NodeTests.IsSourceFile(node) || !((SourceFile)(object)node).IsDeclarationFile)
            ? Transformation(node)
            : node!;

    /// <summary>ported from: src/compiler/transformer.ts:646-667 — <c>dispose</c>.</summary>
    /// <remarks>PORT-NOTE: TS assigns <c>undefined!</c> to the released slots, which is what the references it drops are
    /// for; the port's nullable slots take <c>null</c> and the two hook slots take the two defaults the reference's
    /// <c>undefined!</c> would break (they are non-optional in the generated interface, and the reference never calls
    /// them after disposal).</remarks>
    internal void Dispose()
    {
        if (_state < TransformationState.Disposed)
        {
            // Clean up emit nodes on parse tree
            foreach (var node in _nodes)
            {
                EmitNodes.DisposeEmitNodes(Utilities.GetSourceFileOfNode(Utilities.GetParseTreeNode(node)));
            }

            // Release references to external entries for GC purposes.
            _lexicalEnvironmentVariableDeclarations = null;
            _lexicalEnvironmentVariableDeclarationsStack = [];
            _lexicalEnvironmentFunctionDeclarations = null;
            _lexicalEnvironmentFunctionDeclarationsStack = [];
            _onSubstituteNode = Printer.NoEmitSubstitution;
            _onEmitNode = Printer.NoEmitNotification;
            _emitHelpers = null;

            // Prevent further use of the transformation result.
            _state = TransformationState.Disposed;
        }
    }

    /// <summary>TS's hole-filled stack write (<c>stack[offset] = value</c>); see the stack fields' note.</summary>
    private static void SetStackSlot<TItem>(List<TItem> stack, int offset, TItem value)
    {
        if (offset == stack.Count)
        {
            stack.Add(value);
        }
        else
        {
            stack[offset] = value;
        }
    }
}

/// <summary>ported from: src/compiler/transformer.ts:349-356 — the <c>TransformationResult</c> object literal.</summary>
/// <remarks>PORT-NOTE: the reference's literal hands out its four members plus the diagnostics array; this class holds
/// the context and forwards, which is what the reference's closures do.</remarks>
internal sealed class TransformationResult<T>(TransformationContext<T> context, List<T> transformed) : ITransformationResult<T>
    where T : Node
{
    /// <summary>ported from: src/compiler/transformer.ts:350 — <c>transformed</c>.</summary>
    public List<T>? Transformed { get; set; } = transformed;

    /// <summary>ported from: src/compiler/transformer.ts:355 — <c>diagnostics</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's literal hands its closure's array out and never assigns it; the generated
    /// slot is settable, so the port's slot starts at the context's array (the same one) and takes an assignment.</remarks>
    public List<IDiagnosticWithLocation>? Diagnostics { get; set; } = context.Diagnostics;

    /// <summary>ported from: src/compiler/transformer.ts:351 — <c>substituteNode</c>.</summary>
    public Node SubstituteNode(EmitHint hint, Node? node) => context.SubstituteNode(hint, node);

    /// <summary>ported from: src/compiler/transformer.ts:352 — <c>emitNodeWithNotification</c>.</summary>
    public void EmitNodeWithNotification(EmitHint hint, Node? node, Action<EmitHint, Node>? emitCallback)
        => context.EmitNodeWithNotification(hint, node, emitCallback);

    /// <summary>ported from: src/compiler/transformer.ts:353 — <c>isEmitNotificationEnabled</c>.</summary>
    public bool IsEmitNotificationEnabled(Node? node) => context.IsEmitNotificationEnabled(node);

    /// <summary>ported from: src/compiler/transformer.ts:354 — <c>dispose</c>.</summary>
    public void Dispose() => context.Dispose();
}
