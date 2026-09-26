namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:7067-7073 (IndexInfo)
// ported from: src/compiler/checker.ts:16683-16685 (createIndexInfo)
/// <summary>
/// One index signature of a type — TS's <c>IndexInfo</c>.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS builds every instance as the object literal <c>{ keyType, type, isReadonly, declaration, components }</c>
/// (checker.ts:16683-16685), so the port's class is the same shape with settable slots and no constructor — the same
/// treatment <c>FlowSwitchClauseData</c> got. The checker's <c>CreateIndexInfo</c> (M6-2) builds it with an object
/// initializer.
/// </remarks>
internal partial class IndexInfo : IIndexInfo
{
    /// <summary>ported from: src/compiler/types.ts:7068 — <c>IndexInfo.keyType</c>.</summary>
    public IType? KeyType { get; set; }

    /// <summary>ported from: src/compiler/types.ts:7069 — <c>IndexInfo.type</c>.</summary>
    public IType? Type { get; set; }

    /// <summary>ported from: src/compiler/types.ts:7070 — <c>IndexInfo.isReadonly</c>.</summary>
    public bool IsReadonly { get; set; }

    /// <summary>ported from: src/compiler/types.ts:7071 — <c>IndexInfo.declaration</c>.</summary>
    public IndexSignatureDeclaration? Declaration { get; set; }

    /// <summary>ported from: src/compiler/types.ts:7072 — <c>IndexInfo.components</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's element type is the intersection <c>(ClassElement | ObjectLiteralElement) &amp; { name: ComputedPropertyName }</c>
    /// (types.ts:7065). The generator keeps an intersection's first member's common base, which is <c>Node</c> here —
    /// the same upper bound the checker's own guards imply. <c>getIndexInfoWithReadonly</c> (checker.ts:20272-20274)
    /// copies this list straight through.
    /// </remarks>
    public List<Node>? Components { get; set; }
}
