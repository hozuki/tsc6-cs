namespace TypeScript.Compiler.Tests;

// ported from: src/compiler/checker.ts:1134 (visitEachChildWorker), 1552 (syntacticNodeBuilder)
// ported from: src/compiler/checker.ts:6169/6202/6240/11175 (the four printer-gated string converters)
/// <summary>
/// The node builder's two loud, named blockers (docs/architecture.md §15.15 ruling 1) and the shape of the four
/// printer-gated string converters.
/// </summary>
/// <remarks>
/// PORT-NOTE: these facts pin the blockers rather than output, because both depend on modules this milestone does not
/// port: <c>visitorPublic.ts</c> (<c>visitEachChild</c>, aliased at checker.ts:1134, Layer 6/M7) and
/// <c>expressionToTypeNode.ts</c> (<c>createSyntacticTypeNodeBuilder</c>, M4f's remaining half). The four
/// <c>…ToString</c> members are printer-gated (<c>emitter.ts:1200-1211</c>, M7) and build their node before throwing,
/// so they cannot be exercised without checker state; the fact here pins what M7 has to keep — the member set and its
/// exact <see cref="ITypeChecker"/> signatures.
/// </remarks>
public sealed class CheckerNodeBuilderTests
{
    /// <summary>ported from: src/compiler/checker.ts:1134 — <c>visitEachChildWorker</c> (visitorPublic.ts).</summary>
    [Fact]
    public void VisitEachChildWorker_IsALoudNamedBlocker()
    {
        var node = new Identifier();

        var error = Assert.Throws<NotSupportedException>(
            () => Utilities.VisitEachChildWorker(node, n => n, /*context*/ null));

        Assert.Contains("visitEachChildWorker", error.Message, StringComparison.Ordinal);
        Assert.Contains("visitorPublic.ts", error.Message, StringComparison.Ordinal);
        Assert.Contains("not ported", error.Message, StringComparison.Ordinal);
    }

    /// <summary>ported from: src/compiler/checker.ts:1552 — <c>syntacticNodeBuilder</c>.</summary>
    [Fact]
    public void SyntacticNodeBuilder_IsALoudNamedBlocker()
    {
        var checker = new Checker(new FakeTypeCheckerHost());

        var error = Assert.Throws<NotSupportedException>(() => checker.SyntacticNodeBuilder);

        Assert.Contains("syntacticNodeBuilder", error.Message, StringComparison.Ordinal);
        Assert.Contains("expressionToTypeNode.ts", error.Message, StringComparison.Ordinal);
        Assert.Contains("M4f", error.Message, StringComparison.Ordinal);
    }

    /// <summary>The four printer-gated string converters keep the checker's declared shapes until M7 fills them.</summary>
    [Fact]
    public void PrinterGatedStringConverters_HaveTheirDeclaredShapes()
    {
        // The interface side: the exact shapes `symbolToString`/`signatureToString`/`typeToString`/
        // `typePredicateToString` were ported with (NodeInterfaces.cs:3571-3574).
        Assert.NotNull(typeof(ITypeChecker).GetMethod(
            nameof(ITypeChecker.TypeToString),
            [typeof(IType), typeof(Node), typeof(TypeFormatFlags?)]));
        Assert.NotNull(typeof(ITypeChecker).GetMethod(
            nameof(ITypeChecker.SymbolToString),
            [typeof(ISymbol), typeof(Node), typeof(SymbolFlags?), typeof(SymbolFormatFlags?)]));
        Assert.NotNull(typeof(ITypeChecker).GetMethod(
            nameof(ITypeChecker.SignatureToString),
            [typeof(ISignature), typeof(Node), typeof(TypeFormatFlags?), typeof(SignatureKind?)]));
        Assert.NotNull(typeof(ITypeChecker).GetMethod(
            nameof(ITypeChecker.TypePredicateToString),
            [typeof(ITypePredicateBase), typeof(Node), typeof(TypeFormatFlags?)]));

        // The class side: `Checker` is the implementation (§15.15 ruling 4), and every one of the four is on it.
        foreach (var name in new[] { "TypeToString", "SymbolToString", "SignatureToString", "TypePredicateToString" })
        {
            Assert.Contains(
                typeof(Checker).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance),
                method => method.Name == name);
        }
    }
}
