namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:26506-26512 (getTypeOfReverseMappedSymbol)
/// <summary>
/// The member resolvers the reference declares for mapped, reverse-mapped and anonymous types, and the
/// union/intersection property synthesis they feed.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: this file was chartered for the seven bands <c>checker.ts:14595-14912</c> and
/// <c>15600-15798</c> plus the <c>getTypeOfReverseMappedSymbol</c> tail. By the time it landed,
/// every member of the first three bands was ALREADY declared by <c>Checker/Checker.TypeSupport.cs</c>
/// (M6Generics.M6TypeSupport): <c>ResolveAnonymousTypeMembers</c>, <c>ReplaceIndexedAccess</c> (which carries the
/// <c>ReplaceableIndexedAccessType</c> alias as its parameter face),
/// <c>GetLimitedConstraint</c>, <c>ResolveReverseMappedTypeMembers</c>, <c>GetLowerBoundOfKeyType</c>,
/// <c>GetIsLateCheckFlag</c>, <c>ForEachMappedTypePropertyKeyTypeAndIndexSignatureKeyType</c>,
/// <c>ResolveMappedTypeMembers</c>, <c>GetTypeOfMappedSymbol</c>, <c>CreateUnionOrIntersectionProperty</c>,
/// <c>GetUnionOrIntersectionProperty</c> and <c>GetCommonDeclarationsOfSymbols</c>. Per the one-name-one-owner rule
/// (a duplicate is CS0111 and breaks every slice at once) none of them is declared a second time here; the only band
/// that has no declaration anywhere in the repository is declared below.
/// </para>
/// <para>
/// PORT-NOTE: the reference places <c>getTypeOfReverseMappedSymbol</c> at checker.ts:26506, inside the
/// reverse-mapped inference group (<c>inferReverseMappedTypeWorker</c> :26514, <c>inferReverseMappedType</c> :26521,
/// <c>getUnmatchedProperties</c> :26543 …). That group is not ported yet and is not declared here — it needs the
/// inference slice's <c>createInferenceInfo</c>/<c>inferTypes</c>/<c>getTypeFromInference</c>/<c>isDeeplyNestedType</c>
/// and its <c>reverseMappedCache</c>/<c>reverseMappedSourceStack</c>/<c>reverseMappedTargetStack</c>/
/// <c>reverseExpandingFlags</c> state, none of which exists in the port. This file therefore declares the accessor and
/// calls <c>InferReverseMappedType</c> unqualified with the reference's own argument order, exactly as the landed
/// <c>ResolveReverseMappedTypeMembers</c> in <c>Checker/Checker.TypeSupport.cs</c> already does.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // The reverse-mapped property accessor (checker.ts:26506-26512)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:26506-26512 — <c>getTypeOfReverseMappedSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference reads <c>symbol.links.propertyType</c>; <c>getSymbolLinks</c>'s transient arm returns
    /// exactly that object, so the port keeps the house spelling <c>GetSymbolLinks(symbol)</c> (checker.ts:2932-2936).
    /// </remarks>
    /// <remarks>
    /// PORT-NOTE: TS's <c>getSymbolLinks</c> is generic over the symbol it is handed and therefore returns the
    /// <c>ReverseMappedSymbolLinks</c> face, whose <c>propertyType</c>/<c>mappedType</c>/<c>constraintType</c> do not
    /// exist on the base <c>SymbolLinks</c> face. The port's one links class implements all four faces
    /// (<c>Model/SymbolLinks.cs</c>, which is what <c>createSymbol</c> and <c>getSymbolLinks</c> both assign), so the
    /// cast below is an upcast to the same object, not a conversion.
    /// </remarks>
    internal IType GetTypeOfReverseMappedSymbol(IReverseMappedSymbol symbol)
    {
        var links = (IReverseMappedSymbolLinks)GetSymbolLinks(symbol);
        if (links.Type is null)
        {
            links.Type = InferReverseMappedType(links.PropertyType!, links.MappedType!, links.ConstraintType!) ?? _unknownType;
        }

        return links.Type;
    }
}
