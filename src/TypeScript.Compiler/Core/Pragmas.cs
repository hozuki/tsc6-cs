namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:10216-10237 (PragmaArgumentSpecification / PragmaDefinition)
/// <summary>
/// The two value types the pragma specification table (<see cref="Utilities.CommentPragmas"/>) is written with.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS declares both as interfaces and fills them with anonymous object literals, so the port needs one
/// constructible type per interface — the same treatment <see cref="DiagnosticMessage"/>, <see cref="TextRange"/> and
/// <c>LineAndCharacter</c> got (§12.3). <see cref="PragmaDefinition.Args"/> keeps the strict
/// <c>IList&lt;IPragmaArgumentSpecification&lt;string&gt;&gt;</c> type and bridges the generated
/// <see cref="IPragmaDefinition{T1,T2,T3,T4}"/>, whose <c>Args</c> slot is <c>object?</c> because TS's
/// tuple-of-specifications union has no C# counterpart — the reader casts it back.
/// </remarks>
internal sealed class PragmaArgumentSpecification : IPragmaArgumentSpecification<string>
{
    /// <summary>The name of the key this argument's value lands under in the parsed pragma.</summary>
    public string? Name { get; set; }

    public bool? Optional { get; set; }

    public bool? CaptureSpan { get; set; }
}

/// <summary>ported from: src/compiler/types.ts:10227-10237 — <c>PragmaDefinition</c>.</summary>
internal sealed class PragmaDefinition : IPragmaDefinition<string, string, string, string>
{
    /// <summary>The named arguments this pragma accepts, in the order they are matched positionally.</summary>
    public IList<IPragmaArgumentSpecification<string>>? Args { get; set; }

    /// <summary>Which comment forms this pragma may appear in; TS defaults a missing value to
    /// <see cref="PragmaKindFlags.Default"/>, which the table's entries spell out.</summary>
    public PragmaKindFlags? Kind { get; set; }

    object? IPragmaDefinition<string, string, string, string>.Args
    {
        get => Args;
        set => Args = (IList<IPragmaArgumentSpecification<string>>?)value;
    }
}

/// <summary>ported from: src/compiler/types.ts:10428-10433 — <c>PragmaMap</c>.</summary>
/// <remarks>
/// PORT-NOTE: TS's <c>PragmaMap</c> is an ES6 <c>Map</c> subclass keyed by pragma name, so the port backs it with a
/// dictionary. A stored value is a parsed pragma entry (<c>PragmaPseudoMap[keyof PragmaPseudoMap]</c>, or a list of
/// them when the same pragma appears more than once); <c>PragmaPseudoMap</c> is a mapped type with no C# counterpart,
/// so a value stays <c>object?</c> — which is what the generated <see cref="IReadonlyPragmaMap"/> declares for the
/// same slot. The pragma *processing* that fills this map (M5/M8) is the batch that gives the entry type a name.
/// <para>
/// PORT-NOTE: the generated <c>ForEach</c> takes its callback as <c>object?</c> (TS's generic callback signature has no
/// C# counterpart, so the generator could not type it); the port accepts TS's <c>(value, key, map)</c> shape as an
/// <see cref="Action{T1,T2,T3}"/> and fails loudly on any other value rather than silently doing nothing.
/// </para>
/// </remarks>
internal sealed class PragmaMap : IPragmaMap
{
    private readonly Dictionary<string, object?> _entries = new(StringComparer.Ordinal);

    /// <summary>The entry for a pragma name, or null when the file does not carry that pragma.</summary>
    public object? Get<TKey>(TKey? key)
        => key is string name && _entries.TryGetValue(name, out var value) ? value : null;

    public object Set<TKey>(TKey? key, object? value)
    {
        _entries[key as string ?? key?.ToString() ?? ""] = value;
        return this;
    }

    public void ForEach(object? action)
    {
        if (action is Action<object?, string, PragmaMap> callback)
        {
            foreach (var entry in _entries)
            {
                callback(entry.Value, entry.Key, this);
            }

            return;
        }

        Debug.Fail($"Unsupported pragma map callback: {action?.GetType().Name ?? "null"}");
    }
}
