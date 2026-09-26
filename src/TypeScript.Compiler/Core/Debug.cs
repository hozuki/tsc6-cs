using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace TypeScript.Compiler;

// ported from: src/compiler/debug.ts:98-380
/// <summary>
/// ported from: src/compiler/debug.ts:99-105 — TS: <c>enum LogLevel</c>.
/// </summary>
internal enum LogLevel
{
    Off,
    Error,
    Warning,
    Info,
    Verbose,
}

/// <summary>
/// ported from: src/compiler/debug.ts:108-110 — TS: <c>LoggingHost</c>.
/// </summary>
internal interface ILoggingHost
{
    void Log(LogLevel level, string s);
}

/// <summary>
/// Debug assertions and logging. Equivalent to TS's <c>Debug</c> namespace.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS implements assertion leveling by swapping assertion functions out of the
/// <c>Debug</c> object for <c>noop</c> and restoring them from an <c>assertionCache</c> when the level
/// is raised (see <c>shouldAssertFunction</c>). C# can't replace its own methods at runtime, so each
/// level-gated assertion instead tests <see cref="ShouldAssert"/> on entry. Behaviourally this is the
/// same call-site contract, minus the "method identity changes" side effect that no caller observes here.
/// <para>
/// PORT-NOTE: TS's <c>asserts x is T</c> / <c>Debug.type&lt;T&gt;()</c> are typespace-only. C# expresses the
/// same thing with <see cref="DoesNotReturnIfAttribute"/> on <see cref="Assert"/> plus explicit casts,
/// so no runtime counterpart to <c>Debug.type&lt;T&gt;()</c> is emitted.
/// </para>
/// <para>
/// Not yet ported (needs <c>Node</c>/<c>Symbol</c>/<c>SyntaxKind</c> from the model layer, Milestone M2):
/// <c>failBadSyntaxKind</c>, <c>assertEachNode</c>, <c>assertNode</c>, <c>assertNotNode</c>,
/// <c>assertOptionalNode</c>, <c>assertOptionalToken</c>, <c>assertMissingNode</c>,
/// <c>formatSymbol</c>, <c>formatEnum</c>, <c>formatSyntaxKind</c>.
/// </para>
/// </remarks>
internal static class Debug
{
    /// <summary>ported from: src/compiler/debug.ts:115.</summary>
    public static AssertionLevel CurrentAssertionLevel { get; private set; } = AssertionLevel.None;

    /// <summary>ported from: src/compiler/debug.ts:116.</summary>
    public static LogLevel CurrentLogLevel { get; set; } = LogLevel.Warning;

    /// <summary>ported from: src/compiler/debug.ts:117.</summary>
    public static bool IsDebugging { get; set; }

    /// <summary>ported from: src/compiler/debug.ts:118.</summary>
    public static ILoggingHost? LoggingHost { get; set; }

    // ported from: src/compiler/debug.ts:123-125

    public static bool ShouldLog(LogLevel level) => CurrentLogLevel <= level;

    // ported from: src/compiler/debug.ts:127-131

    public static void LogMessage(LogLevel level, string s)
    {
        if (LoggingHost is { } host && ShouldLog(level))
        {
            host.Log(level, s);
        }
    }

    // ported from: src/compiler/debug.ts:133-153

    public static void Log(string s) => LogMessage(LogLevel.Info, s);

    public static void LogError(string s) => LogMessage(LogLevel.Error, s);

    public static void LogWarning(string s) => LogMessage(LogLevel.Warning, s);

    public static void LogTrace(string s) => LogMessage(LogLevel.Verbose, s);

    // ported from: src/compiler/debug.ts:157-175

    public static AssertionLevel GetAssertionLevel() => CurrentAssertionLevel;

    public static void SetAssertionLevel(AssertionLevel level) => CurrentAssertionLevel = level;

    // ported from: src/compiler/debug.ts:177-179

    public static bool ShouldAssert(AssertionLevel level) => CurrentAssertionLevel >= level;

    // ported from: src/compiler/debug.ts:196-204

    // ported from: src/compiler/../debug.ts:196-204
    [DoesNotReturn]
    public static void Fail(string? message = null)
    {
        if (IsDebugging)
        {
            System.Diagnostics.Debugger.Break();
        }

        throw new InvalidOperationException(message is null ? "Debug Failure." : $"Debug Failure. {message}");
    }

    /// <summary>
    /// Generic form of <see cref="Fail"/> for use in expression position
    /// (TS's <c>fail</c> is declared <c>never</c>, so it can be returned from any function).
    /// </summary>
    [DoesNotReturn]
    public static T Fail<T>(string? message = null) => throw new InvalidOperationException(message is null ? "Debug Failure." : $"Debug Failure. {message}");

    // ported from: src/compiler/debug.ts:213-221

    public static void Assert([DoesNotReturnIf(false)] bool expression, string? message = null, Func<string>? verboseDebugInfo = null)
    {
        if (!expression)
        {
            var text = message is null ? "False expression." : $"False expression: {message}";
            if (verboseDebugInfo is not null)
            {
                text += "\r\nVerbose Debug Information: " + verboseDebugInfo();
            }

            Fail(text);
        }
    }

    // ported from: src/compiler/debug.ts:223-228

    public static void AssertEqual<T>(T a, T b, string? msg = null, string? msg2 = null)
    {
        if (!Core.EquateValues(a, b))
        {
            var message = msg is null ? "" : msg2 is null ? msg : $"{msg} {msg2}";
            Fail($"Expected {a} === {b}. {message}");
        }
    }

    // ported from: src/compiler/debug.ts:230-246

    public static void AssertLessThan(int a, int b, string? msg = null)
    {
        if (a >= b)
        {
            Fail($"Expected {a} < {b}. {msg ?? ""}");
        }
    }

    public static void AssertLessThanOrEqual(int a, int b)
    {
        if (a > b)
        {
            Fail($"Expected {a} <= {b}");
        }
    }

    public static void AssertGreaterThanOrEqual(int a, int b)
    {
        if (a < b)
        {
            Fail($"Expected {a} >= {b}");
        }
    }

    // ported from: src/compiler/debug.ts:248-258

    public static void AssertIsDefined<T>([NotNull] T? value, string? message = null)
    {
        if (value is null)
        {
            Fail(message);
        }
    }

    public static T CheckDefined<T>([NotNull] T? value, string? message = null)
        where T : class
    {
        AssertIsDefined(value, message);
        return value;
    }

    public static T CheckDefined<T>(T? value, string? message = null)
        where T : struct
    {
        if (value is null)
        {
            Fail(message);
        }

        return value.GetValueOrDefault();
    }

    // ported from: src/compiler/debug.ts:260-271

    public static void AssertEachIsDefined<T>(IReadOnlyList<T> value, string? message = null)
    {
        foreach (var v in value)
        {
            AssertIsDefined(v, message);
        }
    }

    public static IReadOnlyList<T> CheckEachDefined<T>(IReadOnlyList<T> value, string? message = null)
    {
        AssertEachIsDefined(value, message);
        return value;
    }

    // ported from: src/compiler/debug.ts:273-276

    // ported from: src/compiler/debug.ts:273-276
    // TODO(M2): TS special-cases `Node` members by formatting `member.kind` via formatSyntaxKind.
    // Reintroduce that branch once the model layer defines Node/SyntaxKind.
    [DoesNotReturn]
    public static void AssertNever(object? member, string message = "Illegal value:")
    {
        Fail($"{message} {member?.ToString() ?? "null"}");
    }

    // ported from: src/compiler/debug.ts:368-380

    public static string GetFunctionName(Delegate? func)
    {
        return func?.Method.Name ?? "";
    }
}
