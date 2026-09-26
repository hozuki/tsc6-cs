using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:6204-6205
/// <summary>
/// A table of symbols keyed by their escaped name. Equivalent to TS's
/// <c>SymbolTable = Map&lt;__String, Symbol&gt;</c>.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS's <c>SymbolTable</c> is a JS <c>Map</c>, whose iteration order is insertion order; every other
/// <c>Map</c> slot in the port keeps the §12.3 <c>Dictionary</c> mapping, but this one cannot. The checker derives
/// <c>properties</c>/<c>resolvedProperties</c>/<c>declaredMembers</c> from these tables
/// (<c>getNamedMembers</c>, <c>resolveDeclaredMembers</c>, <c>getPropertiesOfType</c> checker.ts:15101) and those
/// orders reach union reduction, member display and the <c>.types</c> baseline; a <c>Dictionary</c> would silently
/// reorder them (docs/architecture.md §5 rule 17, §15.13's dump sorted its keys and therefore could not see it).
/// The backing store is <see cref="OrderedDictionary{TKey,TValue}"/>, whose behaviour matches a JS <c>Map</c>
/// exactly: writes append, an update of an existing key keeps its position, a remove followed by a write appends.
/// <para>
/// PORT-NOTE: it composes rather than derives from <see cref="OrderedDictionary{TKey,TValue}"/>, the same choice
/// <c>Core.MultiMap</c> and <c>ModeAwareCache</c> made: inheriting would put BCL verbs (<c>Add</c>, …) next to the
/// port's, and TS's alias is not a container the port has to be substitutable for.
/// </para>
/// <para>
/// PORT-NOTE: the port keeps the BCL shape (<see cref="IReadOnlyDictionary{TKey,TValue}"/>, indexer,
/// <see cref="TryGetValue"/>, <see cref="Remove"/>, <see cref="Count"/>) because that is what the ported binder
/// already writes, and adds <see cref="ForEach"/> only because TS's <c>forEach((value, key) =&gt; …)</c> has no BCL
/// counterpart. A lookup that must answer "maybe" is <c>TryGetValue</c>/<c>GetValueOrDefault</c> as everywhere
/// else in the port, not a nullable <c>get</c>.
/// </para>
/// </remarks>
internal sealed class SymbolTable : IReadOnlyDictionary<string, ISymbol>
{
    private readonly OrderedDictionary<string, ISymbol> _entries = new(StringComparer.Ordinal);

    /// <summary>ported from: src/compiler/utilities.ts:644-652 — <c>createSymbolTable()</c> with no seed symbols.</summary>
    public SymbolTable()
    {
    }

    /// <summary>ported from: src/compiler/checker.ts:2688 — <c>new Map(symbol.members)</c>, the copy of a table.</summary>
    public SymbolTable(SymbolTable source)
    {
        foreach (var (name, symbol) in source._entries)
        {
            _entries.Add(name, symbol);
        }
    }

    /// <summary>ported from: src/compiler/types.ts:6205 — <c>Map.size</c>.</summary>
    public int Count => _entries.Count;

    /// <summary>ported from: src/compiler/types.ts:6205 — <c>Map.keys()</c>, in insertion order.</summary>
    public IEnumerable<string> Keys => _entries.Keys;

    /// <summary>ported from: src/compiler/types.ts:6205 — <c>Map.values()</c>, in insertion order.</summary>
    public IEnumerable<ISymbol> Values => _entries.Values;

    /// <summary>
    /// ported from: src/compiler/types.ts:6205 — <c>Map.get</c>/<c>Map.set</c>.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>get</c> answers <c>undefined</c> for a missing key and C#'s indexer throws; a lookup that
    /// may miss is written with <see cref="TryGetValue"/> or <c>GetValueOrDefault</c>, as everywhere else in the
    /// port. The setter is TS's <c>set</c>: a new key is appended, an existing one is overwritten in place
    /// (<c>symbolTable.set(name, symbol)</c> happens 46 times in the checker).
    /// </remarks>
    public ISymbol this[string name]
    {
        get => _entries[name];
        set => _entries[name] = value;
    }

    /// <summary>ported from: src/compiler/types.ts:6205 — <c>Map.has</c>.</summary>
    public bool ContainsKey(string name) => _entries.ContainsKey(name);

    /// <summary>ported from: src/compiler/types.ts:6205 — <c>Map.get</c> as a "maybe" lookup.</summary>
    public bool TryGetValue(string name, [MaybeNullWhen(false)] out ISymbol symbol) => _entries.TryGetValue(name, out symbol);

    /// <summary>ported from: src/compiler/types.ts:6205 — <c>Map.delete</c>.</summary>
    public bool Remove(string name) => _entries.Remove(name);

    /// <summary>ported from: src/compiler/types.ts:6205 — <c>Map.clear</c>.</summary>
    public void Clear() => _entries.Clear();

    /// <summary>
    /// ported from: src/compiler/types.ts:6205 — <c>Map.forEach((value, key) =&gt; …)</c>, in insertion order.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS's callback takes a third <c>map</c> argument and that argument is never used at any of the 33
    /// call sites, so the port leaves it out.
    /// </remarks>
    public void ForEach(Action<ISymbol, string> callback)
    {
        foreach (var (name, symbol) in _entries)
        {
            callback(symbol, name);
        }
    }

    /// <summary>ported from: src/compiler/types.ts:6205 — <c>Map.entries()</c>, in insertion order.</summary>
    /// <remarks>
    /// PORT-NOTE: the enumeration <em>is</em> TS's <c>entries()</c>, so <c>arrayFrom(table.entries())</c> is
    /// <c>table.ToList()</c> and <c>for (const [key, value] of table)</c> is a <c>foreach</c> over the table.
    /// </remarks>
    public IEnumerator<KeyValuePair<string, ISymbol>> GetEnumerator() => _entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
