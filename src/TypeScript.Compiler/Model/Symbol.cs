namespace TypeScript.Compiler;

// ported from: src/compiler/utilities.ts:8471-8488 (the Symbol constructor, declaration-merged with the interface)
/// <summary>
/// The symbol the binder creates for a declaration — TS's <c>Symbol</c> constructor.
/// </summary>
/// <remarks>
/// The port has no <c>objectAllocator</c> (§15.2), so TS's <c>objectAllocator.getSymbolConstructor()</c> is the
/// class itself and <c>binder.ts</c>'s <c>new Symbol(flags, name)</c> calls this constructor directly.
/// <para>
/// PORT-NOTE: the slots come from the generated <see cref="ISymbol"/> interface rather than from a declaration of
/// their own, so this class is the hand-written half that gives that interface an implementation (§9 rule 8).
/// Every slot is writable and reference-typed slots are nullable, matching the interface.
/// </para>
/// <para>
/// PORT-NOTE: TS's <c>Symbol</c> interface and constructor declaration merge. C# has no declaration merging, so
/// the two halves are the generated <see cref="ISymbol"/> and this class.
/// </para>
/// <para>
/// PORT-NOTE: <c>escapedName</c> is <c>__String</c> upstream and can never be <c>undefined</c> after the
/// constructor; the generated interface types it <c>string?</c> like every other reference slot, so the class does
/// too and the one place that needs the invariant (a symbol table's key) asserts it.
/// </para>
/// <para>
/// PORT-NOTE: <c>members</c>/<c>exports</c>/<c>globalExports</c> are <c>SymbolTable</c> upstream, which is a JS
/// <c>Map</c> and therefore ordered; the port types them by the ordered <see cref="SymbolTable"/> rather than by the
/// §12.3 <c>Dictionary</c> mapping (docs/architecture.md §5 rule 17, <c>Model/SymbolTable.cs</c>).
/// </para>
/// </remarks>
internal partial class Symbol : ISymbol
{
    /// <summary>ported from: src/compiler/utilities.ts:8471-8488 — the <c>Symbol</c> constructor.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: TS <c>Symbol</c> is a constructor function whose caller (<c>createSymbol</c>) always passes both
    /// arguments. The port keeps the two-parameter signature so a call site reads the same.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS also clears an own <c>links</c> property here because <c>TransientSymbol</c> reuses the same
    /// constructor; <see cref="Links"/> is a plain slot and starts <c>null</c> either way.
    /// <see cref="TransientSymbol"/> re-points the slot at a <c>TransientSymbolLinks</c> (M6, <c>Model/TransientSymbol.cs</c>).
    /// </para>
    /// </remarks>
    public Symbol(SymbolFlags flags, string name)
    {
        Flags = flags;
        EscapedName = name;
    }

    /// <summary>ported from: src/compiler/types.ts:6038 — <c>Symbol.flags</c>.</summary>
    public SymbolFlags Flags { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6039 — <c>Symbol.escapedName</c>.</summary>
    public string? EscapedName { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6040 — <c>Symbol.declarations</c>.</summary>
    public List<IDeclaration>? Declarations { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6041 — <c>Symbol.valueDeclaration</c>.</summary>
    public IDeclaration? ValueDeclaration { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6042 — <c>Symbol.members</c>.</summary>
    public SymbolTable? Members { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6043 — <c>Symbol.exports</c>.</summary>
    public SymbolTable? Exports { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6044 — <c>Symbol.globalExports</c>.</summary>
    public SymbolTable? GlobalExports { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6045 — <c>Symbol.id</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: assigned lazily by <c>Utilities.GetSymbolId</c>, which several workers can reach at once for a
    /// shared symbol, so the assignment is a compare-exchange (<c>tsc/internal/ast/utilities.go:31-40</c> does the
    /// same for <c>Symbol.Id</c>). TS's first id is <c>1</c> (<c>utilities.ts:1379</c>'s <c>nextSymbolId = 1</c>), so
    /// <c>0</c> means "unassigned" here; <see cref="IdSlot"/> is the <c>ref</c> form the CAS needs.
    /// </remarks>
    public int Id
    {
        get => Volatile.Read(ref _id);
        set => Volatile.Write(ref _id, value);
    }

    internal ref int IdSlot => ref _id;

    private int _id;

    /// <summary>ported from: src/compiler/types.ts:6046 — <c>Symbol.mergeId</c>.</summary>
    /// <remarks>PORT-NOTE: same lazy-assignment race as <see cref="Id"/>, so the same <c>ref</c> form.</remarks>
    public int MergeId
    {
        get => Volatile.Read(ref _mergeId);
        set => Volatile.Write(ref _mergeId, value);
    }

    internal ref int MergeIdSlot => ref _mergeId;

    private int _mergeId;

    /// <summary>ported from: src/compiler/types.ts:6047 — <c>Symbol.parent</c>.</summary>
    public ISymbol? Parent { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6048 — <c>Symbol.exportSymbol</c>.</summary>
    public ISymbol? ExportSymbol { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6049 — <c>Symbol.constEnumOnlyModule</c>.</summary>
    public bool? ConstEnumOnlyModule { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6050 — <c>Symbol.isReferenced</c>.</summary>
    public SymbolFlags? IsReferenced { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6051 — <c>Symbol.lastAssignmentPos</c>.</summary>
    public int? LastAssignmentPos { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6052 — <c>Symbol.isReplaceableByMethod</c>.</summary>
    public bool? IsReplaceableByMethod { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6053 — <c>Symbol.assignmentDeclarationMembers</c>.</summary>
    public Dictionary<int, IDeclaration>? AssignmentDeclarationMembers { get; set; }

    /// <summary>ported from: src/compiler/utilities.ts:8486 — the <c>links</c> slot the <c>Symbol</c> constructor clears.</summary>
    /// <remarks>
    /// PORT-NOTE: in TS the slot is declared by <c>TransientSymbol</c> only, while the constructor clears it on
    /// every symbol; the port declares it on the base class for the same reason and leaves the narrowing to a later
    /// class (a <c>TransientSymbol</c> would implement <see cref="ITransientSymbol.Links"/> explicitly when
    /// <c>SymbolLinks</c> lands, M6). Nothing the binder writes reads it.
    /// </remarks>
    public ISymbolLinks? Links { get; set; }
}
