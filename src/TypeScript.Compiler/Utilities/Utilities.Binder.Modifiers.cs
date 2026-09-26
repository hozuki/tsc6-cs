namespace TypeScript.Compiler;

// ported from: src/compiler/utilitiesPublic.ts:662-664 (getCombinedModifierFlags)
// ported from: src/compiler/utilitiesPublic.ts:870-875 (symbolName)
/// <summary>
/// The two utilities the binder needs that no other M5 slice owned: the modifier flags of a declaration taken together
/// with the declaration list and statement that carry them, and an escaped symbol name as text.
/// </summary>
/// <remarks>
/// PORT-NOTE: the effective-modifier family is what these two members sat behind in M5-0's slice
/// (Utilities/Utilities.Binder.cs): <c>getCombinedNodeFlags</c> was stated there and the sibling
/// <c>getCombinedModifierFlags</c> was deferred. The deferral's stated blocker was <c>getEffectiveModifierFlags</c>
/// (utilities.ts:7276-7278), the JSDoc-inclusive reader whose own family (<c>getModifierFlagsWorker</c> and the
/// <c>getJSDoc*Tag</c> comment readers) belongs to the JSDoc slice (M5-3) and is still unported. The combined walk the
/// binder needs is <c>getCombinedFlags</c> over the per-node reader, so
/// <see cref="GetCombinedModifierFlags"/> states the walk in full with <see cref="GetSyntacticModifierFlags"/>, which the
/// port has; the only difference from the reference is the modifiers that come from JSDoc comments on a JS file's
/// declarations, and M5-3 can swap the reader in without touching the walk.
/// </remarks>
internal static partial class Utilities
{
    /// <summary>ported from: src/compiler/utilitiesPublic.ts:662-664 — <c>getCombinedModifierFlags</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's per-node reader here is <c>getEffectiveModifierFlags</c> and the port composes
    /// <see cref="GetSyntacticModifierFlags"/> instead (see the remarks on this file);
    /// <c>getCombinedFlags</c> itself is the private walk Utilities.Binder.cs states next to
    /// <c>getCombinedNodeFlags</c> (utilitiesPublic.ts:644-659), reused here rather than duplicated.
    /// <para>
    /// PORT-NOTE: TS types the parameter <c>Declaration</c>; the walk takes any node, and the port's callers hold nodes,
    /// so the parameter is the honest upper bound <see cref="Node"/> (§12.9).
    /// </para>
    /// </remarks>
    public static ModifierFlags GetCombinedModifierFlags(Node node)
        => (ModifierFlags)GetCombinedFlags(node, n => (uint)GetSyntacticModifierFlags(n));

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:870-875 — <c>symbolName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the contract that assigned this member to M5-2 recorded its body as
    /// <c>unescapeLeadingUnderscores(symbol.escapedName)</c> alone; the reference tests the symbol's value declaration for
    /// a private class element first and answers <c>idText</c> of that element's name for it. The port states the whole
    /// body, with that test as <see cref="GetPrivateIdentifierClassElementName"/>; the one call site (binder.ts:3636, the
    /// <c>prototype</c> symbol the binder has just created) has no value declaration, but the utility is not
    /// binder-private and its other callers are not.
    /// <para>
    /// PORT-NOTE: TS's <c>symbol.escapedName</c> is a non-nullable <c>__String</c>; the model's
    /// <see cref="ISymbol.EscapedName"/> is <c>string?</c>, so an absent name reads as the empty string rather than
    /// reaching <c>unescapeLeadingUnderscores</c>.
    /// </para>
    /// </remarks>
    public static string SymbolName(ISymbol symbol)
    {
        if (symbol.ValueDeclaration is { } valueDeclaration
            && GetPrivateIdentifierClassElementName((Node)valueDeclaration) is { } privateIdentifierName)
        {
            return IdText(privateIdentifierName);
        }

        return UnescapeLeadingUnderscores(symbol.EscapedName ?? "");
    }

    /// <summary>
    /// The port of TS's <c>isPrivateIdentifierClassElementDeclaration(symbol.valueDeclaration) &amp;&amp; idText(symbol.valueDeclaration.name)</c>
    /// read in <c>symbolName</c> (utilitiesPublic.ts:871-872).
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: the reference composes <c>isPropertyDeclaration(node) || isMethodOrAccessor(node)</c> with
    /// <c>isPrivateIdentifier(node.name)</c> (utilitiesPublic.ts:1600-1602) and then reads <c>idText(name)</c>. Neither
    /// predicate is ported, and both are the kind tests and the name test the patterns below state directly — the four
    /// classes are exactly <c>PrivateClassElementDeclaration</c>, and the answer is the name in the shape <c>idText</c>
    /// takes. A node that is not one of the four answers <c>null</c>, which is the reference's <c>false</c>.
    /// </remarks>
    private static PrivateIdentifier? GetPrivateIdentifierClassElementName(Node node) => node switch
    {
        PropertyDeclaration { Name: PrivateIdentifier name } => name,
        MethodDeclaration { Name: PrivateIdentifier name } => name,
        GetAccessorDeclaration { Name: PrivateIdentifier name } => name,
        SetAccessorDeclaration { Name: PrivateIdentifier name } => name,
        _ => null,
    };
}
