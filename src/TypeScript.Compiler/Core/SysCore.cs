using System.Globalization;

namespace TypeScript.Compiler;

// ported from: src/compiler/sys.ts:59-65, 74-78, 107-112, 553-574
/// <summary>
/// The host-independent pieces of <c>sys.ts</c>: hashing, the modified-time fallback, watcher event
/// classification and the pluggable sys log.
/// </summary>
/// <remarks>
/// The rest of <c>sys.ts</c> is the Node host implementation (<c>fs</c>, <c>path</c>, <c>crypto</c>,
/// <c>fs.watch</c>), which does not port one-to-one — the CLR equivalents live in a separate
/// <see cref="ISystem"/> implementation instead.
/// </remarks>
internal static class SysCore
{
    /// <summary>
    /// Reported for a file that does not exist; any real modification happens after it. Ports TS's
    /// <c>new Date(0)</c>.
    /// </summary>
    public static readonly DateTimeOffset MissingFileModifiedTime = DateTimeOffset.UnixEpoch;

    // ported from: src/compiler/sys.ts:562
    /// <summary>Paths a recursive directory watcher never descends into.</summary>
    public static readonly IReadOnlyList<string> IgnoredPaths = ["/node_modules/.", "/.git", "/.#"];

    private static Action<string> s_curSysLog = static _ => { };

    // ported from: src/compiler/sys.ts:59-65
    /// <summary>
    /// The djb2 hash of <paramref name="data"/>, as a decimal string.
    /// </summary>
    /// <remarks>
    /// The arithmetic is subtler than the three lines suggest, and getting it wrong silently changes the
    /// hash. JS's <c>acc &lt;&lt; 5</c> coerces <c>acc</c> to a signed 32-bit integer, but the additions
    /// that follow are ordinary double-precision addition, so <c>acc</c> routinely leaves the int32 range
    /// again (for a 2,000-character input it does so on 1,894 of 2,000 steps). Accumulating in
    /// <see cref="int"/> would therefore truncate where JS does not, and accumulating in <see cref="long"/>
    /// would differ from JS once the value passes 2^53. Hence <see cref="double"/> plus
    /// <see cref="Js.ToInt32"/> per step.
    /// </remarks>
    public static string GenerateDjb2Hash(string data)
    {
        var acc = 5381d;
        for (var i = 0; i < data.Length; i++)
        {
            acc = (Js.ToInt32(acc) << 5) + acc + data[i];
        }

        return Js.NumberToString(acc);
    }

    // ported from: src/compiler/sys.ts:74-78
    /// <summary>
    /// Sets a high stack trace limit for richer errors in command-line and server use.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: this assigns Node's <c>Error.stackTraceLimit</c>, which has no CLR analogue — managed
    /// stack traces are already full and controlled by the debugger rather than a global knob. Kept as a
    /// no-op so call sites port unchanged.
    /// </remarks>
    public static void SetStackTraceLimit()
    {
    }

    // ported from: src/compiler/sys.ts:553-559
    /// <summary>
    /// Classifies a modification by its before/after timestamps, where <c>0</c> means "did not exist".
    /// </summary>
    public static FileWatcherEventKind GetFileWatcherEventKind(double oldTime, double newTime)
        => oldTime == 0
            ? FileWatcherEventKind.Created
            : newTime == 0
                ? FileWatcherEventKind.Deleted
                : FileWatcherEventKind.Changed;

    // ported from: src/compiler/sys.ts:110-112
    /// <summary>
    /// Reads a file's modified time from <paramref name="host"/>, substituting
    /// <see cref="MissingFileModifiedTime"/> when the host cannot report one.
    /// </summary>
    public static DateTimeOffset GetModifiedTime(IModifiedTimeHost host, string fileName)
        => host.GetModifiedTime(fileName) ?? MissingFileModifiedTime;

    // ported from: src/compiler/sys.ts:567-569
    /// <summary>Writes a line to the sys log (diagnostics about the host itself, not about user code).</summary>
    public static void SysLog(string s) => s_curSysLog(s);

    // ported from: src/compiler/sys.ts:572-574
    public static void SetSysLog(Action<string> logger) => s_curSysLog = logger;

    // ported from: src/compiler/sys.ts:1280
    /// <summary>
    /// Whether a failure to create a native watcher means the OS ran out of them.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS tests Node's <c>e.code === "ENOSPC"</c> and falls back to polling when the OS reports it.
    /// .NET surfaces the same condition as an IOException; on Unix the errno is preserved in
    /// <see cref="Exception.HResult"/> (28 is ENOSPC), and the message is checked as a fallback because not every
    /// BCL path preserves the code.
    /// </remarks>
    public static bool IsSystemWatcherLimitExceeded(Exception e)
        => e.HResult == 28 || e.Message.Contains("ENOSPC", StringComparison.Ordinal);
}
