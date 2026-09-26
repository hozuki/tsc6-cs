using Xunit;

// xUnit runs test collections in parallel by default. The port keeps a few pieces of process-wide state because the
// reference implementation has them (the tracing singleton `Tracing.Current`, the flow-node id counter), and a test that
// enables tracing therefore interleaves with any other test whose code path calls `Tracing.Current?.Push/Pop` — the
// binder does exactly that on every `bindSourceFile`. The observed symptom was a *nondeterministic* failure set: the
// binder's `Pop` answered one of the tracing test's events and tripped the "results are not supported for events with
// separateBeginAndEnd" assertion. Serializing the assembly is the honest fix: these globals are faithful to TS, and no
// amount of per-test `finally` cleanup can isolate a *concurrent* reader.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
