namespace TypeScript.Compiler.Tests;

/// <summary>
/// End-to-end acceptance for the parser (docs/architecture.md §15.10): real source text in, an AST out.
/// </summary>
/// <remarks>
/// The parser's members are private steps of one state machine, so this is the first place the whole path can be
/// exercised — text → scanner → <c>ParseSourceFile</c> → nodes with ranges, flags and diagnostics. Each test pins
/// something the later layers read rather than incidental shape: the kind chain, the text range, a transform flag, a
/// diagnostic, or a pragma field.
/// </remarks>
public class ParserSmokeTests
{
    private static SourceFile Parse(string text, ScriptKind scriptKind = ScriptKind.TS, bool setParentNodes = false)
        => Parser.CreateSourceFile("test.ts", text, ScriptTarget.ESNext, setParentNodes, scriptKind);

    // ---------------------------------------------------------------------------------------------
    // The file itself
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void AnEmptyFile_HasNoStatementsAndAnEndOfFileToken()
    {
        var file = Parse("");

        Assert.Empty(file.Statements!);
        Assert.NotNull(file.EndOfFileToken);
        Assert.Equal(SyntaxKind.EndOfFileToken, file.EndOfFileToken!.Kind);
        Assert.Equal(0, file.Pos);
        Assert.Equal(0, file.End);
        Assert.Empty(file.ParseDiagnostics!);
    }

    [Fact]
    public void TheSourceFile_CoversTheWholeTextAndCarriesItsInputs()
    {
        const string text = "const x = 1;\n";

        var file = Parse(text);

        Assert.Equal(text, file.Text);
        Assert.Equal("test.ts", file.FileName);
        Assert.Equal(ScriptTarget.ESNext, file.LanguageVersion);
        Assert.Equal(ScriptKind.TS, file.ScriptKind);
        Assert.Equal(text.Length, file.End);
        Assert.False(file.IsDeclarationFile);
    }

    // ---------------------------------------------------------------------------------------------
    // Statements, declarations and expressions
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void AVariableStatement_IsBuiltWithItsKindsRangesAndInitializer()
    {
        const string text = "const x = 1;";

        var file = Parse(text);

        var statement = Assert.IsType<VariableStatement>(Assert.Single(file.Statements!));
        Assert.Equal(0, statement.Pos);
        Assert.Equal(text.Length, statement.End);

        var declarationList = statement.DeclarationList!;
        Assert.NotEqual(NodeFlags.None, declarationList.Flags & NodeFlags.Const);
        var declaration = Assert.Single(declarationList.Declarations!);
        Assert.Equal("x", ((Identifier)declaration.Name!).EscapedText);
        Assert.Equal(SyntaxKind.NumericLiteral, declaration.Initializer!.Kind);
        Assert.Equal("1", ((NumericLiteral)declaration.Initializer).Text);
    }

    [Fact]
    public void AFunctionDeclaration_ParsesParametersTypeAnnotationAndReturn()
    {
        const string text = "function f(a: number, b: B) { return a; }";

        var file = Parse(text);

        var function = Assert.IsType<FunctionDeclaration>(Assert.Single(file.Statements!));
        Assert.Equal("f", ((Identifier)function.Name!).EscapedText);
        Assert.Equal(2, function.Parameters!.Count);

        var keywordParameter = function.Parameters[0];
        Assert.Equal("a", ((Identifier)keywordParameter.Name!).EscapedText);
        Assert.Equal(SyntaxKind.NumberKeyword, keywordParameter.Type!.Kind);

        var referenceParameter = function.Parameters[1];
        Assert.Equal("b", ((Identifier)referenceParameter.Name!).EscapedText);
        var reference = Assert.IsType<TypeReferenceNode>(referenceParameter.Type);
        Assert.Equal("B", ((Identifier)reference.TypeName!).EscapedText);

        var body = Assert.IsType<Block>(function.Body);
        var returnStatement = Assert.IsType<ReturnStatement>(Assert.Single(body.Statements!));
        Assert.Equal("a", ((Identifier)returnStatement.Expression!).EscapedText);
    }

    [Fact]
    public void AClassWithHeritageAndAMethod_ParsesIntoTheExpectedChain()
    {
        const string text = "class C extends B { m(): void {} }";

        var file = Parse(text);

        var declaration = Assert.IsType<ClassDeclaration>(Assert.Single(file.Statements!));
        Assert.Equal("C", ((Identifier)declaration.Name!).EscapedText);

        var heritage = Assert.Single(declaration.HeritageClauses!);
        Assert.Equal(SyntaxKind.ExtendsKeyword, heritage.Token);
        Assert.Equal(SyntaxKind.ExpressionWithTypeArguments, Assert.Single(heritage.Types!).Kind);

        var method = Assert.IsType<MethodDeclaration>(Assert.Single(declaration.Members!));
        Assert.Equal("m", ((Identifier)method.Name!).EscapedText);
        Assert.Equal(SyntaxKind.VoidKeyword, method.Type!.Kind);
    }

    [Fact]
    public void AnInterface_ParsesItsTypeMembers()
    {
        const string text = "interface I { x: string; y(): void; }";

        var file = Parse(text);

        var declaration = Assert.IsType<InterfaceDeclaration>(Assert.Single(file.Statements!));
        Assert.Equal(2, declaration.Members!.Count);
        Assert.Equal(SyntaxKind.PropertySignature, declaration.Members[0].Kind);
        Assert.Equal(SyntaxKind.MethodSignature, declaration.Members[1].Kind);
    }

    // ---------------------------------------------------------------------------------------------
    // Diagnostics
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ASyntaxError_IsReportedWithAPositionAndACode()
    {
        var file = Parse("const = 1;");

        // The reference compiler reports this text as two diagnostics, one at `=` and one at `1` (verified against
        // lib/typescript.js: `DIAG 1134 6 1` and `DIAG 1134 8 1`).
        Assert.Equal(2, file.ParseDiagnostics!.Count);

        foreach (var diagnostic in file.ParseDiagnostics)
        {
            Assert.Equal(1134, diagnostic.Code);
            Assert.NotNull(diagnostic.Start);
            Assert.NotNull(diagnostic.Length);
            Assert.Equal(DiagnosticCategory.Error, diagnostic.Category);
            Assert.IsType<string>(diagnostic.MessageText);
        }

        Assert.Equal(6, file.ParseDiagnostics[0].Start);
        Assert.Equal(8, file.ParseDiagnostics[1].Start);
    }

    [Fact]
    public void ARecoverableFile_StillProducesStatements()
    {
        // The parser must keep going: two statements, one of them broken.
        var file = Parse("const x = ;\nconst y = 2;");

        Assert.Equal(2, file.Statements!.Count);
        Assert.NotEmpty(file.ParseDiagnostics!);
    }

    // ---------------------------------------------------------------------------------------------
    // Pragmas, module detection and the JSON path
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ATsCheckPragma_BecomesTheCheckJsDirective()
    {
        var file = Parse("// @ts-check\nconst x = 1;");

        Assert.NotNull(file.CheckJsDirective);
        Assert.True(file.CheckJsDirective!.Enabled);
        Assert.Equal(0, file.CheckJsDirective.Pos);
    }

    [Fact]
    public void AReferencePragma_BecomesAReferencedFile()
    {
        var file = Parse("/// <reference path=\"other.ts\" />\nconst x = 1;");

        var reference = Assert.Single(file.ReferencedFiles!);
        Assert.Equal("other.ts", reference.FileName);
        Assert.True(reference.Pos >= 0);
        Assert.True(reference.End > reference.Pos);
    }

    [Fact]
    public void AnExport_MarksTheFileAsAnExternalModule()
    {
        Assert.True(Parser.IsExternalModule(Parse("export {};")));
        Assert.False(Parser.IsExternalModule(Parse("const x = 1;")));
    }

    [Fact]
    public void AJsonFile_ParsesItsExpressionIntoAnExpressionStatement()
    {
        var file = Parser.ParseJsonText("data.json", "{\"a\": 1}");

        var statement = Assert.Single(file.Statements!);
        Assert.Equal(SyntaxKind.ExpressionStatement, statement.Kind);
        var expression = ((ExpressionStatement)statement).Expression!;
        Assert.Equal(SyntaxKind.ObjectLiteralExpression, expression.Kind);
        Assert.Single(((ObjectLiteralExpression)expression).Properties!);
    }

    // ---------------------------------------------------------------------------------------------
    // Positions, parents and counters
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void EveryNode_IsSynthesizedUntilTheParserGivesItARange()
    {
        // `Node.Pos`/`End` start at -1 (see NodeFactoryRangeTests) and the parser is what fills them in; a node the
        // parse produced must therefore never be synthesized any more.
        var file = Parse("const x = 1;");
        var declaration = ((VariableStatement)file.Statements![0]).DeclarationList!.Declarations![0];

        Assert.False(Utilities.NodeIsSynthesized(declaration));
        Assert.True(declaration.Pos >= 0);
        Assert.True(declaration.End > declaration.Pos);
    }

    [Fact]
    public void SetParentNodes_LinksTheTreeUpwards()
    {
        var file = Parse("const x = 1;", setParentNodes: true);
        var declaration = ((VariableStatement)file.Statements![0]).DeclarationList!.Declarations![0];

        Assert.Same(file, Utilities.GetSourceFileOfNode(declaration));
        Assert.NotNull(((Identifier)declaration.Name!).Parent);
    }

    [Fact]
    public void TheIdentifierCount_CountsWhatTheParserCreated()
    {
        // `identifierCount` is the parser's own counter (TS increments it in `createIdentifier`); `nodeCount` is the one
        // the port leaves at 0, see the PORT-NOTE in `ParseSourceFileWorker`.
        var file = Parse("const alpha = beta;");

        Assert.Equal(2, file.IdentifierCount);
        Assert.Equal(0, file.NodeCount);
    }

    [Fact]
    public void ADeclarationFile_MarksTheFileAndItsNodesAsAmbient()
    {
        var file = Parser.CreateSourceFile("test.d.ts", "declare const x: number;", ScriptTarget.ESNext);

        Assert.True(file.IsDeclarationFile);
        Assert.NotEqual(NodeFlags.None, file.Statements![0].Flags & NodeFlags.Ambient);
    }

    [Fact]
    public void NonNullExpression_IsParsedAtEveryPosition()
    {
        // `y!` used to crash the parser: TS declares `parseMemberExpressionOrHigher` as returning a `MemberExpression`
        // while the `!` branch of the member expression rest loop produces a `NonNullExpression`, and TS's own type graph
        // makes that a sibling of `MemberExpression` rather than a subtype (both extend `LeftHandSideExpression`). The
        // cast the port inherited from that declaration failed on the first real source file it met, so both shapes are
        // pinned here: standalone, and followed by a member access.
        var file = Parse("const standalone = y!;\nconst chained = y!.length;\n");

        var standalone = (VariableStatement)file.Statements![0];
        var standaloneDeclaration = (VariableDeclaration)standalone.DeclarationList!.Declarations![0];
        Assert.IsType<NonNullExpression>(standaloneDeclaration.Initializer);

        var chained = (VariableStatement)file.Statements![1];
        var chainedDeclaration = (VariableDeclaration)chained.DeclarationList!.Declarations![0];
        var access = Assert.IsType<PropertyAccessExpression>(chainedDeclaration.Initializer);
        Assert.IsType<NonNullExpression>(access.Expression);
    }
}
