namespace TypeScript.Compiler;

// ported from: src/compiler/moduleNameResolver.ts:1114-1155 (ModeAwareCache, createModeAwareCacheKey)
/// <summary>
/// A cache keyed by module specifier that also distinguishes resolution modes.
/// </summary>
/// <remarks>
/// PORT-NOTE: this is a Layer 6 file's type (module resolution is M8), ported early because the generated
/// <c>IProgram</c> declares its members with it — <c>Program</c> was one of the three interfaces lost to
/// declaration merging, so nothing referenced this until the merge was fixed.
/// <para>
/// PORT-NOTE: TS builds the cache as an object literal over a single <c>Map</c> whose key is
/// <c>mode + "|" + specifier</c>, plus a reverse map so <c>forEach</c> can recover the specifier and mode. The port
/// keeps both dictionaries for the same reason. M8 should re-check this against its caller once module resolution
/// lands.
/// </para>
/// </remarks>
internal sealed class ModeAwareCache<T>
{
    private readonly Dictionary<string, T> _underlying = new(StringComparer.Ordinal);

    private readonly Dictionary<string, (string Specifier, ModuleKind? Mode)> _memoizedReverseKeys = new(StringComparer.Ordinal);

    /// <summary>The underlying key: a bare specifier when there is no mode, otherwise <c>mode|specifier</c>.</summary>
    public static string CreateKey(string specifier, ModuleKind? mode) =>
        mode is null ? specifier : $"{mode}|{specifier}";

    public T? Get(string specifier, ModuleKind? mode) => _underlying.GetValueOrDefault(Key(specifier, mode));

    public ModeAwareCache<T> Set(string specifier, ModuleKind? mode, T value)
    {
        _underlying[Key(specifier, mode)] = value;
        return this;
    }

    public ModeAwareCache<T> Delete(string specifier, ModuleKind? mode)
    {
        var key = Key(specifier, mode);
        _underlying.Remove(key);
        _memoizedReverseKeys.Remove(key);
        return this;
    }

    public bool Has(string specifier, ModuleKind? mode) => _underlying.ContainsKey(Key(specifier, mode));

    public void ForEach(Action<T, string, ModuleKind?> callback)
    {
        foreach (var (key, value) in _underlying)
        {
            var (specifier, mode) = _memoizedReverseKeys[key];
            callback(value, specifier, mode);
        }
    }

    public int Size() => _underlying.Count;

    private string Key(string specifier, ModuleKind? mode)
    {
        var key = CreateKey(specifier, mode);
        // TS's getUnderlyingCacheKey records the reverse mapping on every access, not only on set.
        _memoizedReverseKeys[key] = (specifier, mode);
        return key;
    }
}
