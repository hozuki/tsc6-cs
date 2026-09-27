namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:25163-25175 (getRelationKey) — the *key type* has no TS counterpart
/// <summary>
/// The key of the checker's relation caches: the two type ids plus the intersection state for an ordinary pair, or
/// the reference's own structured string for a generic type reference.
/// </summary>
/// <remarks>
/// <para>
/// TS spells the key as a string — `` `${source.id},${target.id}${postFix}` `` — and the generic branch concatenates
/// type ids into one ("<c>111=0-12=1</c>", `GetTypeReferenceId`) with a <c>*</c> marker in front when constrained type
/// parameters were involved. In C# that is a formatted string per relation check: `IsTypeRelatedTo` alone runs 42,303
/// times on the p2000 corpus, and building plus hashing a string each time was 7.8 MiB of its own (probe,
/// docs/architecture.md §18.10). tsgo keys the same caches with a **128-bit hash built in a stack buffer**
/// (<c>CacheHashKey</c>, <c>checker.go:17695-17707</c>) — no allocation per check.
/// </para>
/// <para>
/// The ordinary pair does not need a hash at all: two ids and a small state enum fit in a value type, so the common
/// case becomes an <em>exact</em> key with no collision risk, which is strictly better than the reference's hash while
/// costing the same (one dictionary lookup, no allocation). Only the generic branch keeps its string, so the two
/// forms cannot collide and the generic case stays byte-identical to the reference's key.
/// </para>
/// </remarks>
internal readonly record struct RelationKey(int SourceId, int TargetId, int IntersectionState, string? GenericKey)
{
    /// <summary>The reference's "a key that starts with `*`" test (`structuredTypeRelatedTo`): only a generic key can
    /// carry the marker — the ordinary form has no place to put one.</summary>
    public bool HasConstraintMarker => GenericKey is not null && GenericKey.StartsWith('*');

    public override string ToString()
        => GenericKey is not null
            ? GenericKey
            : IntersectionState != 0 ? $"{SourceId},{TargetId}:{IntersectionState}" : $"{SourceId},{TargetId}";
}
