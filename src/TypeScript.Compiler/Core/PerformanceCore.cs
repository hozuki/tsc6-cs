using System.Diagnostics;

namespace TypeScript.Compiler;

// ported from: src/compiler/performanceCore.ts:1-103
/// <summary>
/// The minimum compatible support for the Web Performance User Timings API that TS expects from a host.
/// </summary>
internal interface IPerformanceTime
{
    double Now();

    double TimeOrigin { get; }
}

internal interface IPerformance : IPerformanceTime
{
    void Mark(string name);

    void Measure(string name, string? startMark = null, string? endMark = null);

    void ClearMeasures(string? name = null);

    void ClearMarks(string? name = null);
}

internal interface IPerformanceHooks
{
    bool ShouldWriteNativeEvents { get; }

    IPerformance? Performance { get; }

    IPerformanceTime? PerformanceTime { get; }
}

/// <summary>
/// Native performance-hook discovery and the default timestamp source.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS probes for <c>perf_hooks</c> under Node and for the browser <c>performance</c> global,
/// then prefers whichever exposes the Web Performance User Timings API. The CLR exposes no such API, so
/// <see cref="TryGetNativePerformanceHooks"/> always returns <c>null</c> and callers fall back to
/// <see cref="Timestamp"/>.
/// <para>
/// PORT-NOTE: TS's fallback is <c>Date.now</c> (Unix epoch milliseconds); the port uses the monotonic
/// <see cref="Stopwatch"/>, which has the resolution the measurements need. Absolute values differ from
/// TS's, but only differences are ever used.
/// </para>
/// </remarks>
internal static class PerformanceCore
{
    private static readonly double s_stopwatchTicksToMilliseconds = 1000.0 / Stopwatch.Frequency;

    public static IPerformanceHooks? TryGetNativePerformanceHooks() => null;

    /// <summary>
    /// Gets a timestamp with (at least) millisecond resolution.
    /// </summary>
    public static double Timestamp() => Stopwatch.GetTimestamp() * s_stopwatchTicksToMilliseconds;
}
