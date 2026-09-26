namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:6270-6311 (the NodeLinks interface)
// ported from: src/compiler/checker.ts:1455-1457 (the NodeLinks constructor)
/// <summary>
/// The checker's per-node cache. Equivalent to TS's <c>NodeLinks</c>.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS reaches a node's links through the id-indexed table only
/// (<c>nodeLinks[getNodeId(node)]</c>, checker.ts:2938); there is no <c>links</c> slot on <c>Node</c> and the port
/// must not add one (M6-0 model_gaps 7).
/// <para>
/// PORT-NOTE: the constructor is TS's <c>function NodeLinks(this: NodeLinks) { this.flags = NodeCheckFlags.None; }</c>
/// (checker.ts:1455-1457) — a constructor function, called with <c>new (NodeLinks as any)()</c> so it can be adopted
/// by <c>objectAllocator</c>. The port keeps the constructor and sets <see cref="Flags"/>; the other two
/// non-nullable flag slots (<see cref="CalculatedFlags"/>, <see cref="JsxFlags"/>) start at their zero
/// (<c>None</c>) value, which is what TS's <c>undefined</c> behaves as in the <c>|</c>/<c>&amp;</c> reads at
/// checker.ts:50947-51035 — <c>undefined | flag</c> is <c>flag</c>.
/// </para>
/// <para>
/// PORT-NOTE: six slots are tri-state (<c>boolean | undefined</c> upstream, read as "has it been computed yet?"):
/// <see cref="IsVisible"/>, <see cref="ContainsArgumentsReference"/>,
/// <see cref="HasReportedStatementInAmbientContext"/>, <see cref="SkipDirectInference"/>,
/// <see cref="DeclarationRequiresScopeChange"/> and <see cref="ParameterInitializerContainsUndefined"/>. They stay
/// <c>bool?</c>; collapsing one to <c>bool</c> would turn "not computed" into "computed as false"
/// (docs/architecture.md §5 rule 1, M6-0 model_gaps 3).
/// </para>
/// </remarks>
internal class NodeLinks : INodeLinks
{
    /// <summary>ported from: src/compiler/checker.ts:1455-1457 — the <c>NodeLinks</c> constructor.</summary>
    public NodeLinks()
    {
        Flags = NodeCheckFlags.None;
    }

    /// <summary>ported from: src/compiler/types.ts:6273 — <c>NodeLinks.flags</c>.</summary>
    public NodeCheckFlags Flags { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6274 — <c>NodeLinks.calculatedFlags</c>.</summary>
    public NodeCheckFlags CalculatedFlags { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6275 — <c>NodeLinks.resolvedType</c>.</summary>
    public IType? ResolvedType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6276 — <c>NodeLinks.resolvedSignature</c>.</summary>
    public ISignature? ResolvedSignature { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6277 — <c>NodeLinks.resolvedSymbol</c>.</summary>
    public ISymbol? ResolvedSymbol { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6278 — <c>NodeLinks.effectsSignature</c>.</summary>
    public ISignature? EffectsSignature { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6279 — <c>NodeLinks.enumMemberValue</c>.</summary>
    public IEvaluatorResult<object?>? EnumMemberValue { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6280 — <c>NodeLinks.isVisible</c>.</summary>
    /// <remarks>PORT-NOTE: tri-state slot — <c>undefined</c> is "not computed" (checker.ts:11326).</remarks>
    public bool? IsVisible { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6281 — <c>NodeLinks.containsArgumentsReference</c>.</summary>
    /// <remarks>PORT-NOTE: tri-state slot — <c>undefined</c> is "not computed".</remarks>
    public bool? ContainsArgumentsReference { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6282 — <c>NodeLinks.hasReportedStatementInAmbientContext</c>.</summary>
    /// <remarks>PORT-NOTE: tri-state slot — <c>undefined</c> is "not computed".</remarks>
    public bool? HasReportedStatementInAmbientContext { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6283 — <c>NodeLinks.jsxFlags</c>.</summary>
    public JsxFlags JsxFlags { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6284 — <c>NodeLinks.resolvedJsxElementAttributesType</c>.</summary>
    public IType? ResolvedJsxElementAttributesType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6285 — <c>NodeLinks.resolvedJSDocType</c>.</summary>
    public IType? ResolvedJSDocType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6286 — <c>NodeLinks.switchTypes</c>.</summary>
    public List<IType>? SwitchTypes { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6287 — <c>NodeLinks.jsxNamespace</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types this <c>Symbol | false</c>: the <c>false</c> records "there is no implicit JSX namespace
    /// here", which is a resolved answer rather than "not resolved yet". <c>object?</c> keeps all three states
    /// (<c>null</c> = unresolved, <c>false</c>-boxed = absent, symbol = found) and the reader unwraps it
    /// (M6-4's <c>getJsxNamespace</c>).
    /// </remarks>
    public object? JsxNamespace { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6288 — <c>NodeLinks.jsxImplicitImportContainer</c>.</summary>
    /// <remarks>PORT-NOTE: as <see cref="JsxNamespace"/> — TS types it <c>Symbol | false</c>.</remarks>
    public object? JsxImplicitImportContainer { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6289 — <c>NodeLinks.jsxFragmentType</c>.</summary>
    public IType? JsxFragmentType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6290 — <c>NodeLinks.contextFreeType</c>.</summary>
    public IType? ContextFreeType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6291 — <c>NodeLinks.deferredNodes</c>.</summary>
    public HashSet<Node>? DeferredNodes { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6292 — <c>NodeLinks.capturedBlockScopeBindings</c>.</summary>
    public List<ISymbol>? CapturedBlockScopeBindings { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6293 — <c>NodeLinks.outerTypeParameters</c>.</summary>
    public List<ITypeParameter>? OuterTypeParameters { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6294 — <c>NodeLinks.isExhaustive</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types this <c>boolean | 0</c>, where <c>0</c> is the in-process marker
    /// (<c>isNodeExhaustive</c> stores <c>0</c> before resolving and <c>true</c>/<c>false</c> after). C# cannot put a
    /// <c>bool</c> and a zero in one nullable slot, so it stays <c>object?</c> and the reader distinguishes the same
    /// three states (M6-5's switch checking).
    /// </remarks>
    public object? IsExhaustive { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6295 — <c>NodeLinks.skipDirectInference</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: tri-state: TS types it <c>true</c> (<c>true | undefined</c>) — it is only ever set, never cleared,
    /// and read for truthiness. The port keeps the generated <c>bool?</c> rather than a <c>true</c>-only marker type.
    /// </remarks>
    public bool? SkipDirectInference { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6296 — <c>NodeLinks.declarationRequiresScopeChange</c>.</summary>
    /// <remarks>PORT-NOTE: tri-state slot — <c>undefined</c> is "not computed".</remarks>
    public bool? DeclarationRequiresScopeChange { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6297 — <c>NodeLinks.serializedTypes</c>.</summary>
    public Dictionary<string, ISerializedTypeEntry>? SerializedTypes { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6298 — <c>NodeLinks.decoratorSignature</c>.</summary>
    public ISignature? DecoratorSignature { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6299 — <c>NodeLinks.spreadIndices</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's anonymous <c>{ first: number | undefined, last: number | undefined }</c> has no C#
    /// counterpart; the generated slot is <c>object?</c> and the two readers/writers (M6-4's argument processing)
    /// name the shape they unwrap it as.
    /// </remarks>
    public object? SpreadIndices { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6300 — <c>NodeLinks.parameterInitializerContainsUndefined</c>.</summary>
    /// <remarks>PORT-NOTE: tri-state slot — <c>undefined</c> is "not computed".</remarks>
    public bool? ParameterInitializerContainsUndefined { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6301 — <c>NodeLinks.fakeScopeForSignatureDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS narrows this to <c>"params" | "typeParams"</c>; the generator maps a string-literal union to
    /// <c>string</c> (§12.3 rule 4), so a reader compares against those two spellings as <c>InternalSymbolName</c>
    /// does.
    /// </remarks>
    public string? FakeScopeForSignatureDeclaration { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6302 — <c>NodeLinks.assertionExpressionType</c>.</summary>
    public IType? AssertionExpressionType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6303 — <c>NodeLinks.potentialThisCollisions</c>.</summary>
    public List<Node>? PotentialThisCollisions { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6304 — <c>NodeLinks.potentialNewTargetCollisions</c>.</summary>
    public List<Node>? PotentialNewTargetCollisions { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6305 — <c>NodeLinks.potentialWeakMapSetCollisions</c>.</summary>
    public List<Node>? PotentialWeakMapSetCollisions { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6306 — <c>NodeLinks.potentialReflectCollisions</c>.</summary>
    public List<Node>? PotentialReflectCollisions { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6307 — <c>NodeLinks.potentialUnusedRenamedBindingElementsInTypes</c>.</summary>
    public List<BindingElement>? PotentialUnusedRenamedBindingElementsInTypes { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6308 — <c>NodeLinks.externalHelpersModule</c>.</summary>
    public ISymbol? ExternalHelpersModule { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6309 — <c>NodeLinks.instantiationExpressionTypes</c>.</summary>
    public Dictionary<int, IType>? InstantiationExpressionTypes { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6310 — <c>NodeLinks.nonExistentPropCheckCache</c>.</summary>
    public HashSet<string>? NonExistentPropCheckCache { get; set; }
}
