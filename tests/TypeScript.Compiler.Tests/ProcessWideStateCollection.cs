using Xunit;

// xUnit runs test collections in parallel, and the port's parallel phases make that the *normal* case: the assembly no
// longer serializes itself (see `AssemblyInfo.cs`). A few tests, though, exist to exercise state the reference keeps
// **process-wide** — the performance mark/measure switch (`Performance.Enable`), the debug logger's level and host
// (`Debug.LoggingHost`/`CurrentLogLevel`/`SetAssertionLevel`), `sys`'s log sink (`SysCore.SetSysLog`), the UI locale
// (`Core.SetUILocale`) and tracing's legend. Those tests mutate that state and then assert on it, so running them
// beside the rest would make both sides read the other's writes: a concurrent test's binder marks would appear in a
// mark/measure assertion, and one test's `LoggingHost` would receive another test's messages.
//
// Serializing the whole assembly for that (docs/architecture.md §15.13) threw away the feedback loop's parallelism;
// xUnit's own mechanism is narrower: a collection marked non-parallel runs by itself, and only the tests that need it
// pay. A test class belongs here exactly when it *mutates* one of those process-wide slots; a test that merely reads
// compiled output does not (and must not, or the collection would grow to swallow the suite).
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessWideStateCollection
{
    /// <summary>The collection name the mutating test classes declare.</summary>
    public const string Name = "process-wide-state";
}
