namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:6487-6490 (UniqueESSymbolType)
// ported from: src/compiler/checker.ts:20343-20347 (createUniqueESSymbolType)
/// <summary>
/// The type of a <c>unique symbol</c> declaration — TS's <c>UniqueESSymbolType</c>.
/// </summary>
/// <remarks>
/// PORT-NOTE: created by <c>createUniqueESSymbolType</c> (checker.ts:20343-20347) as
/// <c>createTypeWithSymbol(TypeFlags.UniqueESSymbol, symbol) as UniqueESSymbolType</c>; the <c>symbol</c> slot comes from
/// <see cref="Type"/>, and the constructor sets only <c>escapedName</c> (which TS builds from the symbol's own name
/// and id). <c>getESSymbolLikeTypeForNode</c> (checker.ts:20349-20364) caches one per symbol in
/// <c>SymbolLinks.uniqueESSymbolType</c>.
/// </remarks>
internal partial class UniqueESSymbolType : Type, IUniqueESSymbolType
{
    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — TS's one type constructor, forwarded so that the
    /// <c>TypeFlags → class</c> dispatch (M6-2) can construct this class.</summary>
    public UniqueESSymbolType(object? checker, TypeFlags flags) : base(checker, flags)
    {
    }

    /// <summary>ported from: src/compiler/types.ts:6489 — <c>UniqueESSymbolType.escapedName</c>.</summary>
    public string? EscapedName { get; set; }
}
