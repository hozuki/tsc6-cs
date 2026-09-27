using System.Collections.Concurrent;

namespace TypeScript.Compiler;

// ported from: src/compiler/performance.ts:23-26
/// <summary>
/// A re-entrant stopwatch: marks are written when the outermost <see cref="Enter"/> /
/// <see cref="Exit"/> pair brackets the region.
/// </summary>
internal interface ITimer
{
    void Enter();

    void Exit();
}

// ported from: src/compiler/performance.ts:12-209
/// <summary>
/// Performance measurements for the compiler — marks, measures and the timers built on them.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS's module exports become static members here, and the three <c>Map</c>s that back the
/// counters are <see cref="Dictionary{TKey,TValue}"/>s. Measurement is off by default: nothing is recorded
/// until <see cref="Enable"/> is called, so the (small) bookkeeping cost is only paid when asked for.
/// </remarks>
internal static class Performance
{
    /// <summary>Shared no-op timer, returned when measurements are disabled.</summary>
    private sealed class NullTimer : ITimer
    {
        public static readonly NullTimer Instance = new();

        public void Enter()
        {
        }

        public void Exit()
        {
        }
    }

    private sealed class Timer : ITimer
    {
        private readonly string _measureName;
        private readonly string _startMarkName;
        private readonly string _endMarkName;
        private int _enterCount;

        public Timer(string measureName, string startMarkName, string endMarkName)
        {
            _measureName = measureName;
            _startMarkName = startMarkName;
            _endMarkName = endMarkName;
        }

        public void Enter()
        {
            if (++_enterCount == 1) Mark(_startMarkName);
        }

        public void Exit()
        {
            if (--_enterCount == 0)
            {
                Mark(_endMarkName);
                Measure(_measureName, _startMarkName, _endMarkName);
            }
            else if (_enterCount < 0)
            {
                Debug.Fail("enter/exit count does not match.");
            }
        }
    }

    private static volatile bool s_enabled;
    private static double s_timeOrigin = PerformanceCore.Timestamp();

    // PORT-NOTE (concurrency): TS's three maps are module-level and written by mark/measure on whichever thread runs
    // the phase — under the port's parallel phases that is several. They stay as they are in the reference (name →
    // timestamp / count / accumulated duration), but concurrent, and the two read-modify-write accumulations below go
    // through AddOrUpdate so a count is incremented once per mark even when two workers mark the same name. Readers
    // enumerate them directly, which ConcurrentDictionary supports while writers run. See docs/architecture.md §18.
    private static readonly ConcurrentDictionary<string, double> s_marks = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, int> s_counts = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, double> s_durations = new(StringComparer.Ordinal);

    private static IPerformanceHooks? s_perfHooks;

    /// <summary>
    /// The user-timing implementation to forward marks to, or <c>null</c> when user timing is unavailable
    /// or disabled.
    /// </summary>
    private static IPerformance? s_performanceImpl;

    /// <summary>Gets the no-op timer used when measurements are disabled.</summary>
    public static ITimer NullTimerInstance => NullTimer.Instance;

    // ported from: src/compiler/performance.ts:29-31
    public static ITimer CreateTimerIf(bool condition, string measureName, string startMarkName, string endMarkName)
        => condition ? CreateTimer(measureName, startMarkName, endMarkName) : NullTimer.Instance;

    // ported from: src/compiler/performance.ts:34-56
    public static ITimer CreateTimer(string measureName, string startMarkName, string endMarkName)
        => new Timer(measureName, startMarkName, endMarkName);

    // ported from: src/compiler/performance.ts:74-84
    /// <summary>Marks a performance event. Ignored when measurements are disabled.</summary>
    public static void Mark(string markName)
    {
        if (!s_enabled) return;

        s_counts.AddOrUpdate(markName, 1, static (_, count) => count + 1);
        s_marks[markName] = PerformanceCore.Timestamp();
        s_performanceImpl?.Mark(markName);
    }

    // ported from: src/compiler/performance.ts:97-105
    /// <summary>
    /// Accumulates a measurement. A missing mark falls back to the point at which the profiler was enabled
    /// (for the start) or to now (for the end).
    /// </summary>
    public static void Measure(string measureName, string? startMarkName = null, string? endMarkName = null)
    {
        if (!s_enabled) return;

        var end = (endMarkName is not null && s_marks.TryGetValue(endMarkName, out var endMark) ? endMark : (double?)null)
            ?? PerformanceCore.Timestamp();
        var start = (startMarkName is not null && s_marks.TryGetValue(startMarkName, out var startMark) ? startMark : (double?)null)
            ?? s_timeOrigin;

        s_durations.AddOrUpdate(measureName, end - start, (_, total) => total + (end - start));
        s_performanceImpl?.Measure(measureName, startMarkName, endMarkName);
    }

    // ported from: src/compiler/performance.ts:114-116
    /// <summary>Gets the number of times <paramref name="markName"/> was marked.</summary>
    public static int GetCount(string markName) => s_counts.GetValueOrDefault(markName);

    // ported from: src/compiler/performance.ts:125-127
    /// <summary>Gets the accumulated duration of every measurement under <paramref name="measureName"/>.</summary>
    public static double GetDuration(string measureName) => s_durations.GetValueOrDefault(measureName);

    // ported from: src/compiler/performance.ts:136-138
    public static void ForEachMeasure(Action<string, double> cb)
    {
        foreach (var entry in s_durations) cb(entry.Key, entry.Value);
    }

    // ported from: src/compiler/performance.ts:141-143
    public static void ForEachMark(Action<string> cb)
    {
        foreach (var name in s_marks.Keys) cb(name);
    }

    // ported from: src/compiler/performance.ts:146-150
    public static void ClearMeasures(string? name = null)
    {
        if (name is not null) s_durations.TryRemove(name, out _);
        else s_durations.Clear();

        s_performanceImpl?.ClearMeasures(name);
    }

    // ported from: src/compiler/performance.ts:153-163
    public static void ClearMarks(string? name = null)
    {
        if (name is not null)
        {
            s_counts.TryRemove(name, out _);
            s_marks.TryRemove(name, out _);
        }
        else
        {
            s_counts.Clear();
            s_marks.Clear();
        }

        s_performanceImpl?.ClearMarks(name);
    }

    // ported from: src/compiler/performance.ts:170-172
    /// <summary>Indicates whether the performance API is enabled.</summary>
    public static bool IsEnabled => s_enabled;

    // ported from: src/compiler/performance.ts:179-195
    /// <summary>
    /// Enables (and resets) performance measurements for the compiler.
    /// </summary>
    /// <param name="system">
    /// Consulted for <c>cpuProfilingEnabled</c>/<c>debugMode</c> to decide whether to also write native
    /// trace events. TS defaults this to the global <c>sys</c>; the port accepts <c>null</c> and then treats
    /// both flags as unset.
    /// </param>
    public static bool Enable(ISystem? system = null)
    {
        if (!s_enabled)
        {
            s_enabled = true;
            s_perfHooks ??= PerformanceCore.TryGetNativePerformanceHooks();
            if (s_perfHooks?.Performance is { } performance)
            {
                s_timeOrigin = performance.TimeOrigin;

                // Native trace events are only worth writing when someone is going to look at them.
                if (s_perfHooks.ShouldWriteNativeEvents || system?.CpuProfilingEnabled() == true || system?.DebugMode == true)
                {
                    s_performanceImpl = performance;
                }
            }
        }

        return true;
    }

    // ported from: src/compiler/performance.ts:202-209
    /// <summary>Disables performance measurements and drops everything collected so far.</summary>
    public static void Disable()
    {
        if (!s_enabled) return;

        s_marks.Clear();
        s_counts.Clear();
        s_durations.Clear();
        s_performanceImpl = null;
        s_enabled = false;
    }
}
