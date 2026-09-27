using System.Collections.Concurrent;
using System.Reflection;

namespace TypeScript.Compiler;

// NOT ported from TS — the port's stand-in for the piece of the reference that keeps the default libraries from being
// re-parsed and re-bound once per program: `documentRegistry.ts`'s `acquireDocument` (the language service hands the
// same `SourceFile` to every program whose parse+bind options are unchanged) and `program.ts`'s incremental reuse,
// which both rely on `bindSourceFile`'s `if (!file.locals)` guard (binder.ts:574 — the port has the same guard at
// `Binder/Binder.State.cs:253`).
/// <summary>
/// A process-wide cache of the parsed **and bound** default-library <see cref="SourceFile"/>s, keyed by file name and
/// by a signature of every compiler option.
/// </summary>
/// <remarks>
/// <para>
/// Why this exists: a fresh program parses and binds the ~93 default-library files, and the port pays that in full for
/// every program. Measured on this machine for a one-file program: 1224 ms total = 203 ms (CLR start + JIT) + ~385 ms
/// (parse + bind of the libs) + ~636 ms (check of the libs), and the libs' parse+bind is ~105 MiB of the 137 MiB the
/// run allocates — the GC profile of that run is 50% `Thread.PollGC`, i.e. the lib work is most of the allocation too
/// (docs/architecture.md §18.7). tsgo pays 27 ms for the same parse+bind because Go is fast; the port's answer to
/// "one process, many programs" (the differential harness, watch mode, a language service) is to pay it **once**
/// instead of once per program.
/// </para>
/// <para>
/// The three conditions a reused file has to satisfy, each of them the reference's own rule:
/// <list type="number">
/// <item>The file is a default-library file (under the host's <c>getDefaultLibLocation()</c>, ending in <c>.d.ts</c>) —
/// those are the files this cache is for; user files and <c>node_modules</c> are not cached, so a stale entry can never
/// reach a file the program was asked to compile.</item>
/// <item>Every compiler option is unchanged (<see cref="SignatureFrom"/>) — the conservative form of the reference's
/// <c>hasChangedCompilerOptions</c>: the signature covers *all* of <see cref="ICompilerOptions"/>, so any option that
/// could move a parse or a bind invalidates the entry, and a false "changed" only costs a re-parse.</item>
/// <item>The file's text is unchanged, which within one process is true by construction — the registry lives for the
/// process, so a run that edits a library on disk between programs is the host's business, not this cache's.</item>
/// </list>
/// </para>
/// <para>
/// PORT-NOTE: sharing a bound <see cref="SourceFile"/> across programs is what the reference's own language service
/// does, and it works only because of two properties the port also has: <c>bindSourceFile</c> skips a file whose
/// <c>locals</c> are already set (so the second program reuses the first's symbols instead of mutating them), and every
/// per-program table that could observe the sharing is keyed by the process-wide node/symbol ids rather than by object
/// identity (<c>PagedLinkStore</c>, §18.4c). A host that hands out files from here must therefore *not* keep two
/// programs alive that are expected to differ in their binding of the same library — the batch runner drops each
/// program before the next, exactly as the language service version-checks its documents.
/// </para>
/// </remarks>
internal sealed class LibSourceFileRegistry
{
    /// <summary>The parsed-and-bound library files. Keyed by <c>lower(path) \0 signature</c> so that the path
    /// comparison is case-insensitive (this platform's file system) and the signature comparison is ordinal.</summary>
    private readonly ConcurrentDictionary<string, SourceFile> _files = new(StringComparer.Ordinal);

    /// <summary>The directory the default libraries live in, normalized and lower-cased with a trailing slash — the
    /// only place this cache answers for.</summary>
    private readonly string _libDirectory;

    /// <summary>Whether the cache may answer at all; a run A/Bs the cache with <c>TSC_CS_LIB_CACHE=0</c>.</summary>
    private readonly bool _enabled;

    private long _hits;
    private long _misses;

    public LibSourceFileRegistry(string libDirectory, bool? enabled = null)
    {
        var normalized = Paths.NormalizePath(libDirectory);
        _libDirectory = (normalized.EndsWith('/') ? normalized : normalized + "/").ToLowerInvariant();
        _enabled = enabled ?? Environment.GetEnvironmentVariable("TSC_CS_LIB_CACHE") != "0";
    }

    /// <summary>Files served from the cache, and files that had to be produced — the numbers an A/B reports.</summary>
    public long Hits => Interlocked.Read(ref _hits);

    public long Misses => Interlocked.Read(ref _misses);

    public bool Enabled => _enabled;

    /// <summary>The library file with this name, when it is one and it was parsed+bound with these options.</summary>
    public bool TryGet(string fileName, string signature, out SourceFile file)
    {
        if (!_enabled || !IsLibFile(fileName))
        {
            file = null!;
            return false;
        }

        if (_files.TryGetValue(Key(fileName, signature), out var cached))
        {
            Interlocked.Increment(ref _hits);
            file = cached;
            return true;
        }

        Interlocked.Increment(ref _misses);
        file = null!;
        return false;
    }

    /// <summary>Remembers a library file as parsed and bound under these options.</summary>
    public void Add(string fileName, string signature, SourceFile file)
    {
        if (_enabled && IsLibFile(fileName))
        {
            _files[Key(fileName, signature)] = file;
        }
    }

    private bool IsLibFile(string fileName)
        => fileName.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase)
            && fileName.ToLowerInvariant().StartsWith(_libDirectory, StringComparison.Ordinal);

    private static string Key(string fileName, string signature)
        => string.Concat(fileName.ToLowerInvariant(), "\0", signature);

    /// <summary>
    /// A signature of every slot of <see cref="ICompilerOptions"/>, in declaration order.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: the reference answers the same question with <c>hasChangedCompilerOptions</c>' hundred-odd explicit
    /// comparisons; reflection over the generated interface is the port's equivalent and it cannot go stale — a slot a
    /// generator change adds is covered the day it appears, at the cost of the property reads (~0.3 ms, once per host).
    /// <see cref="List{T}"/> slots are compared by content and everything else by its own <c>ToString</c>, which for
    /// the generated enums is their name (aliases included) and for the booleans/numerics is exact.
    /// </remarks>
    public static string SignatureFrom(ICompilerOptions options)
    {
        var builder = new System.Text.StringBuilder(2048);
        foreach (var property in OptionProperties)
        {
            builder.Append(property.Name).Append('=');
            var value = property.GetValue(options);
            switch (value)
            {
                case null:
                    break;
                case System.Collections.IEnumerable enumerable and not string:
                    foreach (var item in enumerable)
                    {
                        builder.Append(item?.ToString()).Append(',');
                    }

                    break;
                default:
                    builder.Append(value.ToString());
                    break;
            }

            builder.Append('\u0001');
        }

        return builder.ToString();
    }

    private static readonly PropertyInfo[] OptionProperties =
        [.. typeof(ICompilerOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance)];
}
