namespace TypeScript.Compiler.Tests;

/// <summary>
/// Pins the port's rule A″ (<c>docs/architecture.md</c> §15.16): a diagnostic message argument may carry a thunk
/// (<see cref="Diagnostic.DeferArg"/>) whose text — and node building — happens on the first read of the message.
/// </summary>
/// <remarks>
/// The reference has no such thing (it computes every argument at the call site), so what these tests have to pin is
/// that the deviation is observationally invisible: the same text, read at the same ordering-observable point, and a
/// throwing thunk (the printer gate) still throws for any reader that asks for the text. Reading
/// <c>MessageText</c> is itself the read, so every assertion here observes the argument through a counter rather than
/// by inspecting the payload.
/// </remarks>
public class DeferredDiagnosticArgumentTests
{
    [Fact]
    public void ADiagnosticArgument_IsNotEvaluatedUntilTheMessageIsRead()
    {
        var evaluations = 0;
        var diagnostic = Utilities.CreateCompilerDiagnostic(
            Diagnostics.Did_you_mean_0,
            Diagnostic.DeferArg(() =>
            {
                evaluations++;
                return "foo";
            }));

        Assert.Equal(0, evaluations);

        Assert.Equal("Did you mean 'foo'?", diagnostic.MessageText);
        Assert.Equal(1, evaluations);

        // Memoised: a second read neither recomputes nor re-reads.
        Assert.Equal("Did you mean 'foo'?", diagnostic.MessageText);
        Assert.Equal(1, evaluations);
    }

    [Fact]
    public void ADiagnosticArgument_FormatsToTheSameTextAsAnEagerArgument()
    {
        var deferred = Utilities.CreateCompilerDiagnostic(Diagnostics.Did_you_mean_0, Diagnostic.DeferArg(() => "foo"));
        var eager = Utilities.CreateCompilerDiagnostic(Diagnostics.Did_you_mean_0, "foo");

        Assert.Equal(eager.MessageText, deferred.MessageText);
        Assert.Equal(Utilities.FormatMessage(Diagnostics.Did_you_mean_0, "foo"), deferred.MessageText);
    }

    [Fact]
    public void AMessageChainArgument_IsNotEvaluatedUntilTheChainTextIsRead()
    {
        var evaluations = 0;
        var chain = Utilities.ChainDiagnosticMessages(
            /*details*/ null,
            Diagnostics.Did_you_mean_0,
            Diagnostic.DeferArg(() =>
            {
                evaluations++;
                return "foo";
            }));

        Assert.Equal(0, evaluations);
        Assert.Equal("Did you mean 'foo'?", chain.MessageText);
        Assert.Equal(1, evaluations);

        // A diagnostic built from a leaf chain carries that text (the reference reads it there, utilities.ts:2485).
        var diagnostic = Utilities.CreateCompilerDiagnosticFromMessageChain(chain);
        Assert.Equal("Did you mean 'foo'?", diagnostic.MessageText);
        Assert.Equal(1, evaluations);
    }

    [Fact]
    public void AThrowingArgument_ThrowsWhenTheTextIsReadAndNotBefore()
    {
        // The printer gates of the checker throw `NotSupportedException`; nothing about the deferral may swallow it.
        var evaluations = 0;
        var diagnostic = Utilities.CreateCompilerDiagnostic(
            Diagnostics.Did_you_mean_0,
            Diagnostic.DeferArg(() =>
            {
                evaluations++;
                throw new NotSupportedException("printer is M7's");
            }));

        Assert.Equal(0, evaluations);
        var thrown = Assert.Throws<NotSupportedException>(() => diagnostic.MessageText);
        Assert.Equal("printer is M7's", thrown.Message);
        Assert.Equal(1, evaluations);
    }

    [Fact]
    public void ATieOnCodeAndSpan_ReadsBothArgumentsWhenSorting()
    {
        // `compareDiagnosticsSkipRelatedInformation` (utilities.ts:8787-8794) reaches the message text only in the
        // tie-break after file/start/length/code compared equal — the reference's own point of use, so the deferred
        // read happens there too and the order is the text order.
        var first = 0;
        var second = 0;
        var diagnostics = new List<Diagnostic>
        {
            Utilities.CreateCompilerDiagnostic(Diagnostics.Did_you_mean_0, Diagnostic.DeferArg(() => { first++; return "b"; })),
            Utilities.CreateCompilerDiagnostic(Diagnostics.Did_you_mean_0, Diagnostic.DeferArg(() => { second++; return "a"; })),
        };

        var sorted = Utilities.SortAndDeduplicateDiagnostics(diagnostics);

        Assert.Equal(2, sorted.Count);
        Assert.Equal("Did you mean 'a'?", sorted[0].MessageText);
        Assert.Equal("Did you mean 'b'?", sorted[1].MessageText);
        Assert.Equal(1, first);
        Assert.Equal(1, second);
    }
}
