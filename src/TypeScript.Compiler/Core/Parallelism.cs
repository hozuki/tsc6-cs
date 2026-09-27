namespace TypeScript.Compiler;

// NOT ported from TS — the reference is single-threaded, so it has no counterpart for this file.
/// <summary>
/// The degree of parallelism the compilation phases fan out with, and the one helper they fan out through.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: there is nothing to port here — TS runs on one thread by design, and its data structures are written for
/// that (see docs/architecture.md §18 for the audit). The Go port is the reference for the <em>shape</em>: it states a
/// <c>WorkGroup</c> (<c>tsc/internal/core/workgroup.go:11-76</c>) whose parallel implementation is "one goroutine per
/// work item" and whose sequential implementation runs the queued closures in a loop, and the compiler picks between
/// them from a single switch (<c>tsc/internal/compiler/checkerpool.go:308-310</c>, forced to
/// <c>singleThreaded</c> by an internal option).
/// </para>
/// <para>
/// The port states the same switch, with two differences that .NET forces:
/// </para>
/// <list type="bullet">
/// <item><description>
/// The fan-out is <em>bounded</em>. One goroutine per item is a Go idiom that .NET cannot copy: here it would be one
/// thread-pool work item per file per phase, and the pool would spend its time scheduling. <see cref="For"/> therefore
/// runs a capped <see cref="Parallel.For(int, int, ParallelOptions, Action{int})"/>.
/// </description></item>
/// <item><description>
/// The degree is read once, from <c>TSC_CS_PARALLEL</c>, defaulting to <see cref="Environment.ProcessorCount"/>. Go
/// exposes its own switches the same way (<c>TS_TEST_PROGRAM_SINGLE_THREADED</c>,
/// <c>tsc/internal/testutil/testutil.go:39</c>); the port does it through the environment because the compiler's
/// options object is the generated <c>ICompilerOptions</c>, which mirrors TS's and must not grow a member TS does not
/// have. <c>TSC_CS_PARALLEL=1</c> is the sequential path: every fan-out degenerates to a plain loop, which is what the
/// A/B runs measure against.
/// </description></item>
/// </list>
/// <para>
/// PORT-NOTE (nesting): the degree is public because a host that already fans out per file must pin it to 1 — the
/// diff tools run one program per worker (`tools/TypeDiff`, `tools/EmitDiff`'s <c>--parallel</c>), and a fan-out inside
/// each of those workers would multiply the worker count by the core count for no gain.
/// </para>
/// </remarks>
public static class Parallelism
{
    /// <summary>How many workers a phase may use. <c>1</c> means "no fan-out" — see <see cref="Enabled"/>.</summary>
    public static int Degree
    {
        get => Volatile.Read(ref s_degree);
        set => Volatile.Write(ref s_degree, value < 1 ? 1 : value);
    }

    /// <summary>Whether any phase fans out at all.</summary>
    internal static bool Enabled => Degree > 1;

    /// <summary>
    /// Runs <paramref name="body"/> for every index in <c>[0, count)</c> — sequentially when the degree is 1 or there is
    /// nothing to split, otherwise on at most <see cref="Degree"/> workers.
    /// </summary>
    /// <remarks>
    /// A body that throws propagates the aggregate exception, exactly as a plain loop would propagate the first one.
    /// Determinism is the caller's business: a caller whose output is compared (diagnostics, dumps, emitted text) must
    /// write into per-index slots and merge in index order afterwards, which is what the phases do.
    /// </remarks>
    internal static void For(int count, Action<int> body)
    {
        var degree = Degree;
        if (degree <= 1 || count <= 1)
        {
            for (var i = 0; i < count; i++)
            {
                body(i);
            }

            return;
        }

        Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = degree }, body);
    }

    /// <summary>
    /// Runs <paramref name="body"/> over <paramref name="items"/>, in parallel when the degree allows it, and answers
    /// the results in the collection's own order.
    /// </summary>
    internal static List<TResult> Select<TItem, TResult>(IList<TItem> items, Func<TItem, TResult> body)
    {
        var results = new TResult[items.Count];
        For(items.Count, i => results[i] = body(items[i]));
        return [.. results];
    }

    private static int s_degree = ReadDegree();

    private static int ReadDegree()
    {
        var raw = Environment.GetEnvironmentVariable("TSC_CS_PARALLEL");
        return int.TryParse(raw, out var parsed) && parsed >= 1 ? parsed : Environment.ProcessorCount;
    }
}
