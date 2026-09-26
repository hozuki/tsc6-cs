namespace TypeScript.Compiler.Tests;

// ported from: src/compiler/checker.ts:1134 (visitEachChildWorker), 1552 (syntacticNodeBuilder)
// ported from: src/compiler/checker.ts:6169/6202/6240/11175 (the four printer-gated string converters)
/// <summary>
/// The node builder's one remaining loud, named blocker (docs/architecture.md §15.15 ruling 1), the syntactic node
/// builder checker.ts:1552 assigns, and the shape of the four printer-gated string converters.
/// </summary>
/// <remarks>
/// PORT-NOTE: <c>VisitEachChildWorker_IsALoudNamedBlocker</c> pins a blocker rather than output because it depends on a module
/// this milestone does not port: <c>visitorPublic.ts</c> (<c>visitEachChild</c>, aliased at checker.ts:1134, Layer 6/M7).
/// The syntactic builder it used to sit beside is landed (<c>expressionToTypeNode.ts</c>, 1,330 lines) and is pinned by
/// the facts below instead. The four <c>…ToString</c> members are printer-gated (<c>emitter.ts:1200-1211</c>, M7) and
/// build their node before throwing, so they cannot be exercised without checker state; the fact here pins what M7 has
/// to keep — the member set and its exact <see cref="ITypeChecker"/> signatures.
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
    /// <remarks>
    /// PORT-NOTE: checker.ts:1552 evaluates
    /// <c>createSyntacticTypeNodeBuilder(compilerOptions, syntacticBuilderResolver)</c> once inside
    /// <c>createNodeBuilder</c>; the port states that as the property's lazy initializer, so the two facts pinned here
    /// are that the read answers the builder (not a blocker) and that it is the same object on every read. The five
    /// members are the reference's object literal (expressionToTypeNode.ts:178-189), declared in types.ts:10661-10667.
    /// </remarks>
    [Fact]
    public void SyntacticNodeBuilder_IsTheBuilderTheReferenceAssigns()
    {
        var checker = new Checker(new FakeTypeCheckerHost());

        var builder = Assert.IsAssignableFrom<ISyntacticNodeBuilder>(checker.SyntacticNodeBuilder);
        Assert.Same(builder, checker.SyntacticNodeBuilder);

        Assert.NotNull(builder.SerializeTypeOfDeclaration);
        Assert.NotNull(builder.SerializeReturnTypeForSignature);
        Assert.NotNull(builder.SerializeTypeOfExpression);
        Assert.NotNull(builder.TryReuseExistingTypeNode);
        Assert.NotNull(builder.SerializeTypeOfAccessor);
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:899-906 — <c>typeFromExpression</c>'s <c>NullKeyword</c> arm.</summary>
    /// <remarks>
    /// PORT-NOTE: the arm the factory's <c>strictNullChecks</c> case reads last is the one this exercises: a null
    /// literal serializes as the literal type <c>null</c> when <c>strictNullChecks</c> is on and as <c>any</c> when it is
    /// off (checker.ts:1552 reads the option once, so the two builders differ only in that bit). Nothing in the arm
    /// touches the resolver or the tracker, so a bare <see cref="NodeBuilderContext"/> is the whole fixture.
    /// </remarks>
    [Fact]
    public void SyntacticNodeBuilder_AnswersNullByStrictNullChecks()
    {
        var resolver = new SyntacticBuilderResolver(new Checker(new FakeTypeCheckerHost()));
        var context = new NodeBuilderContext();
        var nullExpression = NodeFactory.Factory.CreateNull();

        ISyntacticNodeBuilder strict = SyntacticNodeBuilder.CreateSyntacticTypeNodeBuilder(new FakeCompilerOptions { Strict = true }, resolver);
        ISyntacticNodeBuilder loose = SyntacticNodeBuilder.CreateSyntacticTypeNodeBuilder(new FakeCompilerOptions { StrictNullChecks = false }, resolver);

        Assert.Equal(SyntaxKind.LiteralType, strict.SerializeTypeOfExpression!(nullExpression, context, /*addUndefined*/ false, /*preserveLiterals*/ false).Kind);
        Assert.Equal(SyntaxKind.AnyKeyword, loose.SerializeTypeOfExpression!(nullExpression, context, /*addUndefined*/ false, /*preserveLiterals*/ false).Kind);
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
