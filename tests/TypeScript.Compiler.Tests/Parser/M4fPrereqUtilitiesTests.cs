namespace TypeScript.Compiler.Tests;

/// <summary>
/// The M4f prerequisites: the helpers the two M4f files import, plus the types they are built from.
/// </summary>
/// <remarks>
/// Neither M4f file is ported yet — <c>factory/emitHelpers.ts</c> waits on M8's option model and
/// <c>expressionToTypeNode.ts</c> on M5's JSDoc family as well — so nothing here has a consumer inside the port, and the
/// only way to observe the new members is directly. Each test pins a behaviour the ported bodies state rather than an
/// incidental shape: which flag <c>getFunctionFlags</c> answers per function form, where the return-statement walk stops,
/// the boolean/<c>"quit"</c> protocol of <c>findAncestor</c>, the <c>type</c>-slot read behind the effective-annotation
/// trio (including the two JavaScript-file fallbacks, which are loud blockers until M5), and the descriptor that fits the
/// port's <c>Node</c>-typed emit-helper slots.
/// </remarks>
public class M4fPrereqUtilitiesTests
{
    private static SourceFile Parse(string text, ScriptKind scriptKind = ScriptKind.TS)
        => Parser.CreateSourceFile("test.ts", text, ScriptTarget.ESNext, setParentNodes: true, scriptKind);

    /// <summary>The first declaration of a one-statement file that declares a variable.</summary>
    private static Node FirstDeclaration(string text, ScriptKind scriptKind = ScriptKind.TS)
        => (Node)((VariableStatement)Parse(text, scriptKind).Statements![0]).DeclarationList!.Declarations![0];

    /// <summary>The single member of a one-class file.</summary>
    private static IClassElement FirstClassMember(string text)
        => ((ClassDeclaration)Parse(text).Statements![0]).Members![0];

    // ---------------------------------------------------------------------------------------------
    // getFunctionFlags
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void GetFunctionFlags_AnswersPerFunctionForm()
    {
        var statements = Parse(
            """
            function f() {}
            function* g() {}
            async function a() {}
            async function* ag() {}
            declare function noBody(): void;
            const arrow = async () => 1;
            """).Statements!;

        Assert.Equal(FunctionFlags.Normal, Utilities.GetFunctionFlags((Node)statements[0]));
        Assert.Equal(FunctionFlags.Generator, Utilities.GetFunctionFlags((Node)statements[1]));
        Assert.Equal(FunctionFlags.Async, Utilities.GetFunctionFlags((Node)statements[2]));
        Assert.Equal(FunctionFlags.AsyncGenerator, Utilities.GetFunctionFlags((Node)statements[3]));

        // A signature with no body is `Invalid` even though nothing else about it is unusual.
        Assert.Equal(FunctionFlags.Invalid, Utilities.GetFunctionFlags((Node)statements[4]));

        var arrow = ((VariableStatement)statements[5]).DeclarationList!.Declarations![0].Initializer!;
        Assert.Equal(FunctionFlags.Async, Utilities.GetFunctionFlags(arrow));

        Assert.Equal(FunctionFlags.Invalid, Utilities.GetFunctionFlags(null));
    }

    [Fact]
    public void GetFunctionFlags_AnswersForMethodDeclarations()
    {
        var members = ((ClassDeclaration)Parse("class C { async m() {} *gm() {} sync() {} }").Statements![0]).Members!;

        Assert.Equal(FunctionFlags.Async, Utilities.GetFunctionFlags((Node)members[0]));
        Assert.Equal(FunctionFlags.Generator, Utilities.GetFunctionFlags((Node)members[1]));
        Assert.Equal(FunctionFlags.Normal, Utilities.GetFunctionFlags((Node)members[2]));
    }

    // ---------------------------------------------------------------------------------------------
    // forEachReturnStatement
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ForEachReturnStatement_VisitsEveryReturnAndStopsOnATruthyAnswer()
    {
        var body = ((FunctionDeclaration)Parse(
            "function f() { if (a) { return 1; } try { return 2; } catch { return 3; } }").Statements![0]).Body!;

        var visited = new List<Node?>();
        Assert.Null(Utilities.ForEachReturnStatement<object>(body, statement =>
        {
            visited.Add(statement.Expression);
            return null;
        }));
        Assert.Equal(3, visited.Count);

        visited.Clear();
        var stopped = Utilities.ForEachReturnStatement(body, statement =>
        {
            visited.Add(statement.Expression);
            return true;
        });
        Assert.True(stopped);
        Assert.Single(visited);
    }

    // ---------------------------------------------------------------------------------------------
    // findAncestor
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void FindAncestor_WalksToTheNearestMatchAndQuitsOnNull()
    {
        var returnStatement = ((Block)((MethodDeclaration)FirstClassMember("class C { m() { return 1; } }")).Body!)
            .Statements![0];
        var parent = ((Node)returnStatement).Parent;

        Assert.IsType<MethodDeclaration>(Utilities.FindAncestor(parent, Utilities.IsFunctionLikeDeclaration));

        // The same predicate, answering "quit" instead of "found": the walk stops at the method and answers nothing.
        Assert.Null(Utilities.FindAncestor(
            parent,
            node => Utilities.IsFunctionLikeDeclaration(node) ? (bool?)null : false));

        Assert.Null(Utilities.FindAncestor(null, Utilities.IsFunctionLikeDeclaration));
    }

    // ---------------------------------------------------------------------------------------------
    // The function-like and new-scope predicates
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void FunctionLikeKind_AnswersForDeclarationAndSignatureKinds()
    {
        Assert.True(Utilities.IsFunctionLikeKind(SyntaxKind.ArrowFunction));
        Assert.True(Utilities.IsFunctionLikeKind(SyntaxKind.MethodSignature));
        Assert.True(Utilities.IsFunctionLikeKind(SyntaxKind.FunctionType));
        Assert.True(Utilities.IsFunctionLikeKind(SyntaxKind.JSDocSignature));
        Assert.False(Utilities.IsFunctionLikeKind(SyntaxKind.ClassDeclaration));
        Assert.False(Utilities.IsFunctionLikeKind(SyntaxKind.CallExpression));

        Assert.True(Utilities.IsFunctionLikeDeclaration((Node)Parse("function f() {}").Statements![0]));
        Assert.False(Utilities.IsFunctionLikeDeclaration(FirstDeclaration("const f = 1;")));
        Assert.True(Utilities.IsFunctionLike((Node)Parse("function f() {}").Statements![0]));
        Assert.False(Utilities.IsFunctionLike(null));
    }

    [Fact]
    public void IsNewScopeNode_AcceptsSignaturesAndMappedTypes()
    {
        Assert.True(Utilities.IsNewScopeNode((Node)Parse("function f() {}").Statements![0]));
        Assert.True(Utilities.IsNewScopeNode((Node)((TypeAliasDeclaration)Parse("type T = { [K in keyof U]?: V };").Statements![0]).Type!));
        Assert.False(Utilities.IsNewScopeNode((Node)Parse("interface I {}").Statements![0]));
    }

    // ---------------------------------------------------------------------------------------------
    // The effective type annotations
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void EffectiveTypeAnnotation_ReadsTheSlotOfAVariableParameterOrProperty()
    {
        Assert.Equal(SyntaxKind.NumberKeyword, Utilities.GetEffectiveTypeAnnotationNode(FirstDeclaration("var x: number;"))!.Kind);

        // A type alias' `.type` is the alias' target, not an annotation on it.
        Assert.Null(Utilities.GetEffectiveTypeAnnotationNode((Node)Parse("type T = number;").Statements![0]));

        // A function declaration's `.type` is a return type, which `getEffectiveReturnTypeNode` owns.
        var function = (Node)Parse("function f(): void {}").Statements![0];
        Assert.Null(Utilities.GetEffectiveTypeAnnotationNode(function));
        Assert.Equal(SyntaxKind.VoidKeyword, Utilities.GetEffectiveReturnTypeNode(function)!.Kind);

        var arrow = ((VariableStatement)Parse("const f = (p: string): boolean => true;").Statements![0])
            .DeclarationList!.Declarations![0].Initializer!;
        Assert.Equal(SyntaxKind.BooleanKeyword, Utilities.GetEffectiveReturnTypeNode(arrow)!.Kind);

        Assert.Equal(
            SyntaxKind.StringKeyword,
            Utilities.GetEffectiveTypeAnnotationNode(((ISignatureDeclarationBase)arrow).Parameters![0])!.Kind);
    }

    [Fact]
    public void EffectiveSetAccessorTypeAnnotation_ReadsTheValueParameter()
    {
        var accessor = (SetAccessorDeclaration)FirstClassMember("class C { set v(value: string) {} }");
        var parameter = Utilities.GetSetAccessorValueParameter(accessor)!;

        Assert.Equal("value", ((Identifier)parameter.Name!).EscapedText);
        Assert.Equal(SyntaxKind.StringKeyword, Utilities.GetEffectiveSetAccessorTypeAnnotationNode(accessor)!.Kind);

        // A set accessor may spell a `this` parameter first; the value parameter is then the second one.
        var withThis = (SetAccessorDeclaration)FirstClassMember("class C { set v(this: C, value: string) {} }");
        Assert.True(Utilities.ParameterIsThisKeyword(withThis.Parameters![0]));
        Assert.Equal("value", ((Identifier)Utilities.GetSetAccessorValueParameter(withThis)!.Name!).EscapedText);
    }

    /// <summary>
    /// The JSDoc fallbacks answer now that the family has landed (utilities.ts:6892-6898's
    /// <c>getEffectiveTypeAnnotationNode</c> and utilities.ts:6911-6915's <c>getEffectiveReturnTypeNode</c>): a
    /// JS-file node with no JSDoc around it reads as "no annotation", the reference's <c>undefined</c>.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: this test replaced the M4f blocker pin (<c>Assert.Throws&lt;NotImplementedException&gt;</c>) that the
    /// JSDoc family's absence required. The reference never throws on these two nodes — the fallbacks consult
    /// <c>getJSDocType</c>/<c>getJSDocReturnType</c> and answer <c>undefined</c> when there is no JSDoc — so the
    /// expectations are the reference's own answers, not a relaxation of the old ones.
    /// </remarks>
    [Fact]
    public void TheJavaScriptFallbacks_ReadTheJSDocFamily()
    {
        var jsDeclaration = FirstDeclaration("var x = 1;", ScriptKind.JS);
        Assert.True(Utilities.IsInJSFile(jsDeclaration));
        Assert.Null(Utilities.GetEffectiveTypeAnnotationNode(jsDeclaration));

        var jsFunction = (Node)Parse("function f() {}", ScriptKind.JS).Statements![0];
        Assert.Null(Utilities.GetEffectiveReturnTypeNode(jsFunction));

        // The same two nodes in a TypeScript file answer "no annotation" instead of asking JSDoc.
        var tsDeclaration = FirstDeclaration("var x = 1;");
        Assert.False(Utilities.IsInJSFile(tsDeclaration));
        Assert.Null(Utilities.GetEffectiveTypeAnnotationNode(tsDeclaration));
        Assert.Null(Utilities.GetEffectiveReturnTypeNode((Node)Parse("function f() {}").Statements![0]));
    }

    // ---------------------------------------------------------------------------------------------
    // Emit helpers
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void AnEmitHelper_FitsTheNodeTypedHelperSlots()
    {
        var decorate = new EmitHelper(
            "typescript:decorate",
            scoped: false,
            text: "var __decorate = 1;",
            priority: 2,
            importName: "__decorate");
        var metadata = new EmitHelper(
            "typescript:metadata",
            scoped: false,
            text: uniqueName => uniqueName("__metadata"),
            dependencies: [decorate]);

        Assert.Equal(SyntaxKind.Unknown, decorate.Kind);
        Assert.False(decorate.Scoped);
        Assert.Equal("__decorate", decorate.ImportName);
        Assert.Equal(2, decorate.Priority);
        Assert.Same(decorate, ((IEmitHelperBase)metadata).Dependencies![0]);

        // The callback form of `text` is told apart by its delegate type, and answers through the callback it is given.
        Assert.Same(decorate, Assert.IsType<EmitHelper>(((IEmitHelperBase)metadata).Dependencies![0]));

        var metadataText = (Func<EmitHelperUniqueNameCallback, string>)metadata.Text!;
        Assert.Equal("__metadata", metadataText(name => name));

        var node = (Node)Parse("var x = 1;").Statements![0];
        EmitNodes.AddEmitHelper(node, metadata);
        Assert.Same(metadata, EmitNodes.GetEmitHelpers(node)![0]);
        Assert.True(EmitNodes.RemoveEmitHelper(node, metadata));
    }
}
