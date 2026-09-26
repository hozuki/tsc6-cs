namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:6314 — type TrackedSymbol
/// <summary>
/// One entry of <c>NodeBuilderContext.trackedSymbols</c>: the symbol the node builder had to name, where it was named
/// from, and what it meant.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS's <c>TrackedSymbol</c> is a labelled tuple <c>[symbol, enclosingDeclaration, meaning]</c>
/// (types.ts:6314), which C# has no counterpart for. The generated <see cref="ISerializedTypeEntry.TrackedSymbols"/>
/// therefore came out as <c>List&lt;object?&gt;?</c>; the port names the tuple here (the same move as
/// <c>Parser/CallBinding.cs</c> for an interface literal) and the checker pushes these instances into the context's
/// list. Copying them into a serialized type entry needs the box to be projected
/// (<c>trackedSymbols.Cast&lt;object?&gt;().ToList()</c>) because that generated slot is <c>List&lt;object?&gt;?</c>.
/// </remarks>
internal sealed record TrackedSymbol(ISymbol? Symbol, Node? EnclosingDeclaration, SymbolFlags Meaning);
