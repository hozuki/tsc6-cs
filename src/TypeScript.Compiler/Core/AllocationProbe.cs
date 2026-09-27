using System.Runtime.CompilerServices;

namespace TypeScript.Compiler;

// NOT ported from TS — this has no counterpart there.
/// <summary>
/// Exclusive allocation accounting for one thread, switched on with <c>TSC_CS_ALLOC_PROBE=1</c>.
/// </summary>
/// <remarks>
/// <para>
/// Why it exists: the profiler on these workloads says <c>Thread.PollGC</c> is 32–50% of the CPU and that *no other
/// single function* reaches 1% — the cost is the aggregate of many allocations rather than one hot spot
/// (docs/architecture.md §18.4j/§18.7). CPU sampling cannot name those call sites (its frames are leaves, and the
/// speedscope export carries no call tree), so the port needs its own instrument; this is the ad-hoc one §18.4i used,
/// kept afterwards because every allocation question here wants it again.
/// </para>
/// <para>
/// What it measures is <em>exclusive</em> bytes: <c>GC.GetAllocatedBytesForCurrentThread()</c> around a region, minus
/// what nested instrumented regions already accounted for. Exclusive accounting is what turns a recursive walk into a
/// readable list, and the call count is part of the report because one entry's 70 MiB over 1,000 calls and another's
/// over 1,000,000 calls are different problems.
/// </para>
/// <para>
/// PORT-NOTE: the state is <c>[ThreadStatic]</c>, which matches the workload (the checker runs on one thread, and the
/// report is read after the parallel phases finish) and keeps the probe out of the parallel phases' way. With the
/// environment variable unset every member is a no-op — <see cref="Enter"/> returns a default struct and
/// <see cref="Report"/> returns immediately — and a run that never writes the braces pays one predictable branch.
/// </para>
/// </remarks>
internal static class AllocationProbe
{
    private static readonly bool s_enabled = Environment.GetEnvironmentVariable("TSC_CS_ALLOC_PROBE") == "1";

    /// <summary>Every region ever entered on this thread, by name.</summary>
    [ThreadStatic]
    private static Dictionary<string, Entry>? _registry;

    /// <summary>The regions currently open, innermost last — the chain the exclusive arithmetic walks.</summary>
    [ThreadStatic]
    private static List<Entry>? _stack;

    public static bool Enabled => s_enabled;

    /// <summary>Opens a named region; dispose it to close. A no-op returning a default struct when the probe is off.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Scope Enter(string name)
        => s_enabled ? new Scope(name) : default;

    /// <summary>Writes the ranked report to <paramref name="write"/> (typically <c>Console.Error</c>).</summary>
    public static void Report(Action<string> write)
    {
        if (!s_enabled || _registry is null)
        {
            return;
        }

        write($"ALLOC cells={_registry.Count} total={GC.GetTotalAllocatedBytes(precise: true) / (1024.0 * 1024.0):F0} MiB");
        foreach (var (name, entry) in _registry.OrderByDescending(pair => pair.Value.ExclusiveBytes))
        {
            write($"ALLOC {entry.ExclusiveBytes / (1024.0 * 1024.0),9:F1} MiB  {entry.Calls,10} calls  {name}");
        }
    }

    /// <summary>One region's totals. Allocates only on first use of a name (so the arithmetic stays honest for the
    /// regions that are entered often).</summary>
    private sealed class Entry(string name)
    {
        public readonly string Name = name;
        public long ExclusiveBytes;
        public long Calls;
        public long ChildBytes;
        public long StartBytes;
    }

    /// <summary>One instrumented region.</summary>
    public struct Scope : IDisposable
    {
        private Entry? _entry;

        internal Scope(string name)
        {
            var registry = _registry ??= new Dictionary<string, Entry>(StringComparer.Ordinal);
            if (!registry.TryGetValue(name, out var entry))
            {
                registry[name] = entry = new Entry(name);
            }

            var stack = _stack ??= [];
            entry.StartBytes = GC.GetAllocatedBytesForCurrentThread();
            entry.Calls++;
            stack.Add(entry);
            _entry = entry;
        }

        public void Dispose()
        {
            var entry = _entry;
            if (entry is null)
            {
                return;
            }

            var spent = GC.GetAllocatedBytesForCurrentThread() - entry.StartBytes;
            var stack = _stack!;
            stack.RemoveAt(stack.Count - 1);
            entry.ExclusiveBytes += spent - entry.ChildBytes;
            entry.ChildBytes = 0;
            if (stack.Count != 0)
            {
                stack[^1].ChildBytes += spent;
            }

            _entry = null;
        }
    }
}
