namespace TypeScript.Compiler.Tests;

// ported from: src/compiler/types.ts:6204-6205 (SymbolTable)
// ported from: src/compiler/types.ts:6056-6101, 6134-6136 (SymbolLinks, TransientSymbolLinks)
// ported from: src/compiler/types.ts:6138-6164 (TransientSymbol, MappedSymbol, ReverseMappedSymbol)
// ported from: src/compiler/types.ts:6270-6311 (NodeLinks)
/// <summary>
/// The ordered symbol table, the two link classes and the transient symbol — the M6-0b model half.
/// </summary>
/// <remarks>
/// The order assertions are the point of this file: TS's <c>SymbolTable</c> is a JS <c>Map</c> and the checker reads
/// member order out of it, so an unordered table would reorder properties, unions and <c>.types</c> output without
/// any test that compares a dump ever noticing (docs/architecture.md §5 rule 17). The rest pins the two invariants
/// the checker's casts rely on: one links object per symbol with all four link interfaces on it, and one storage for
/// <c>Symbol.Links</c>/<c>TransientSymbol.Links</c>.
/// </remarks>
public class SymbolTableAndLinksTests
{
    private static Symbol Symbol(string name) => new(SymbolFlags.BlockScopedVariable, name);

    [Fact]
    public void SymbolTable_IteratesInInsertionOrder()
    {
        var a = Symbol("a");
        var b = Symbol("b");
        var c = Symbol("c");
        var table = new SymbolTable();
        table["a"] = a;
        table["b"] = b;
        table["c"] = c;

        Assert.Equal(["a", "b", "c"], table.Keys.ToList());
        Assert.Equal([a, b, c], table.Values.ToList());
        Assert.Equal(["a", "b", "c"], table.Select(static entry => entry.Key));

        var visited = new List<string>();
        table.ForEach((_, name) => visited.Add(name));
        Assert.Equal(["a", "b", "c"], visited);
    }

    [Fact]
    public void SymbolTable_OverwritingKeepsThePositionAndReAddingMovesToTheEnd()
    {
        var a = Symbol("a");
        var b = Symbol("b");
        var replacement = Symbol("a");

        var table = new SymbolTable();
        table["a"] = a;
        table["b"] = b;

        // JS Map.set on an existing key updates the value in place.
        table["a"] = replacement;
        Assert.Equal(["a", "b"], table.Keys.ToList());
        Assert.Same(replacement, table["a"]);

        // JS Map.delete followed by set appends.
        Assert.True(table.Remove("a"));
        Assert.False(table.ContainsKey("a"));
        Assert.False(table.Remove("a"));
        table["a"] = a;
        Assert.Equal(["b", "a"], table.Keys.ToList());
        Assert.Equal(2, table.Count);
    }

    [Fact]
    public void SymbolTable_CopiesIndependently()
    {
        var a = Symbol("a");
        var b = Symbol("b");
        var table = new SymbolTable();
        table["a"] = a;
        table["b"] = b;

        // TS copies a table with `new Map(table)` (checker.ts:2688).
        var copy = new SymbolTable(table);
        Assert.Equal(["a", "b"], copy.Keys.ToList());
        Assert.Same(a, copy["a"]);

        copy["c"] = a;
        Assert.Equal(3, copy.Count);
        Assert.Equal(2, table.Count);
        Assert.False(table.ContainsKey("c"));
    }

    [Fact]
    public void CreateSymbolTable_SeedsInOrderAndLaterSymbolsKeepTheFirstPosition()
    {
        var a = Symbol("a");
        var b = Symbol("b");
        var replacement = Symbol("a");

        var table = Utilities.CreateSymbolTable(new List<ISymbol> { a, b });
        Assert.Equal(["a", "b"], table.Keys.ToList());

        var overwritten = Utilities.CreateSymbolTable(new List<ISymbol> { a, replacement });
        Assert.Single(overwritten);
        Assert.Equal(["a"], overwritten.Keys.ToList());
        Assert.Same(replacement, overwritten["a"]);

        Assert.Empty(Utilities.CreateSymbolTable());
    }

    [Fact]
    public void SymbolTable_IsAReadOnlyDictionary()
    {
        var a = Symbol("a");
        var table = new SymbolTable { ["a"] = a };

        Assert.True(table.TryGetValue("a", out var found));
        Assert.Same(a, found);
        Assert.False(table.TryGetValue("b", out var missing));
        Assert.Null(missing);
        Assert.Null(table.GetValueOrDefault("b"));
        Assert.Same(a, table.GetValueOrDefault("a"));
    }

    [Fact]
    public void NodeLinks_StartsAtNoneAndKeepsTheTriStatesUnset()
    {
        var links = new NodeLinks();

        // `function NodeLinks(this: NodeLinks) { this.flags = NodeCheckFlags.None; }` (checker.ts:1455-1457).
        Assert.Equal(NodeCheckFlags.None, links.Flags);
        Assert.Equal(NodeCheckFlags.None, links.CalculatedFlags);
        Assert.Equal(JsxFlags.None, links.JsxFlags);

        // The six tri-states read as "not computed" until something writes them, and `false` is a distinct answer.
        Assert.Null(links.IsVisible);
        Assert.Null(links.ContainsArgumentsReference);
        Assert.Null(links.HasReportedStatementInAmbientContext);
        Assert.Null(links.SkipDirectInference);
        Assert.Null(links.DeclarationRequiresScopeChange);
        Assert.Null(links.ParameterInitializerContainsUndefined);

        links.IsVisible = false;
        Assert.False(links.IsVisible);
    }

    [Fact]
    public void SymbolLinks_IsOneObjectForAllFourLinkShapes()
    {
        var links = new SymbolLinks();
        var symbol = Symbol("s");

        // The checker's `as MappedSymbolLinks`/`as ReverseMappedSymbolLinks` casts must not throw, and the slots the
        // four shapes add must be the same storage (checker.ts:14752, :14870, :6329).
        Assert.Same(links, (ITransientSymbolLinks)links);
        Assert.Same(links, (IMappedSymbolLinks)links);
        Assert.Same(links, (IReverseMappedSymbolLinks)links);

        ((ITransientSymbolLinks)links).CheckFlags = CheckFlags.Mapped;
        Assert.Equal(CheckFlags.Mapped, links.CheckFlags);

        ((IMappedSymbolLinks)links).KeyType = null;
        ((IReverseMappedSymbolLinks)links).ConstraintType = null;
        Assert.Null(links.KeyType);
        Assert.Null(links.ConstraintType);

        links.SyntheticOrigin = symbol;
        Assert.Same(symbol, links.SyntheticOrigin);
    }

    [Fact]
    public void TransientSymbol_NarrowsTheBaseLinksSlotWithoutASecondStorage()
    {
        var symbol = new TransientSymbol(SymbolFlags.Property | SymbolFlags.Transient, "t");
        var links = new SymbolLinks { CheckFlags = CheckFlags.Late };

        symbol.Links = links;

        // One `links` per symbol: the transient view and the `Symbol` view are the same object.
        Assert.Same(links, symbol.Links);
        Assert.Same(links, ((Symbol)symbol).Links);
        Assert.Equal(CheckFlags.Late, symbol.Links!.CheckFlags);

        // TS's `MappedSymbol`/`ReverseMappedSymbol` are the same object at run time, so the casts that reach them
        // have to succeed on a transient symbol.
        Assert.Same(symbol, (IMappedSymbol)symbol);
        Assert.Same(symbol, (IReverseMappedSymbol)symbol);
        Assert.True((symbol.Flags & SymbolFlags.Transient) != 0);

        ((Symbol)symbol).Links = new SymbolLinks();
        Assert.NotNull(symbol.Links);
        Assert.Equal(CheckFlags.None, symbol.Links!.CheckFlags);
    }
}
