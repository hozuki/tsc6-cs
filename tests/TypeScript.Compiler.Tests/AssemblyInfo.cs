using Xunit;

// xUnit runs test collections in parallel, and the port used to forbid that: it kept a few pieces of process-wide
// mutable state because the reference implementation has them (the tracing singleton `Tracing.Current`, the flow-node
// id counter), so a test that enabled tracing interleaved with any other test whose code path calls
// `Tracing.Current?.Push/Pop` — the binder does exactly that on every `bindSourceFile`. The observed symptom was a
// *nondeterministic* failure set: the binder's `Pop` answered one of the tracing test's events and tripped the
// "results are not supported for events with separateBeginAndEnd" assertion.
//
// §18's concurrency substrate removed both hazards, so the assembly runs in parallel again (and any test that
// re-introduces a process-wide mutable slot will fail here rather than in the field):
//   * `Tracing.Current` is an `AsyncLocal<T>` and `Tracing.StartTracing` builds a tracer instance per trace, so a
//     test's event stack is its own. What stays process-wide — the legend and its record count — is locked
//     (`Core/Tracing.cs`).
//   * The id counters (node, symbol, flow, merge, auto-generate) are `Interlocked`, and the slots they fill are
//     compare-exchanges (`Utilities.GetNodeId` / `GetSymbolId`, `Checker.GetFlowNodeId`, `Checker.RecordMergedSymbol`).
