namespace TypeScript.Compiler.Tests;

// ported from: src/compiler/transformers/classThis.ts:33-148
/// <summary>
/// Acceptance for the <c>_classThis</c> helper (M7-B): the <c>static {}</c> block the injector prepends, the two guards
/// that recognise one, and the source-map range the block's statement carries.
/// </summary>
/// <remarks>
/// These tests need nothing but the parser and the factory — every member here is a pure function of the class node and
/// the factory, so the whole module is observable before the class-field/ES2015 transforms that call it land. The
/// printed block is the reference's own <c>static { _classThis = this; }</c>; the negative cases pin the two guards,
/// which is what keeps a user-written block that merely looks like the helper from being treated as one.
/// </remarks>
public class ClassThisTests
{
    private static readonly NodeFactory Factory = NodeFactory.Factory;

    private static SourceFile Parse(string text)
        => Parser.CreateSourceFile("test.ts", text, ScriptTarget.ESNext, /*setParentNodes*/ true, ScriptKind.TS);

    private static string Print(Node node, SourceFile file)
        => PrinterFactory.CreatePrinterWithRemoveCommentsOmitTrailingSemicolon().PrintNode(EmitHint.Unspecified, node, file);

    private static ClassDeclaration ClassOf(SourceFile file) => (ClassDeclaration)file.Statements![0];

    /// <summary>The identifier the reference's own caller builds.</summary>
    /// <remarks><c>esDecorators.ts:596-601</c> creates it as
    /// <c>createUniqueName("_classThis", Optimistic | FileLevel)</c> (or <c>ReservedInNestedScopes</c> when static
    /// private/auto-accessor members force a per-class variable). The flags matter to the printed name: a <c>Unique</c>
    /// name without <c>Optimistic</c> is run through the "find the first unique <c>name_n</c>" loop unconditionally
    /// (emitter.ts:5626-5657), so it would print <c>_classThis_1</c>.</remarks>
    private static Identifier ClassThisIdentifier()
        => Factory.CreateUniqueName("_classThis", GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel);

    [Fact]
    public void InjectingABlock_PrependsTheMarkedStaticBlockAndKeepsTheOtherMembers()
    {
        var file = Parse("class C { m() { } }");
        var declaration = ClassOf(file);
        var classThis = ClassThisIdentifier();

        var updated = ClassThis.InjectClassThisAssignmentIfMissing(Factory, declaration, classThis);

        Assert.NotSame(declaration, updated);
        Assert.Equal(2, updated.Members!.Count);

        var block = (Node)updated.Members![0];
        Assert.Equal(SyntaxKind.ClassStaticBlockDeclaration, block.Kind);
        Assert.Equal("static { _classThis = this; }", Print(block, file));
        Assert.Same(declaration.Members![0], updated.Members![1]);

        // The marker the guard reads: the emit node of the block and of the class both name the variable.
        Assert.Same(classThis, EmitNodes.GetOrCreateEmitNode(block).ClassThis);
        Assert.Same(classThis, ((Node)updated).EmitNode?.ClassThis);
        Assert.True(ClassThis.IsClassThisAssignmentBlock(block));
        Assert.True(ClassThis.ClassHasClassThisAssignment(updated));
    }

    [Fact]
    public void InjectingTwice_AnswersTheSameNode()
    {
        var file = Parse("class C { }");
        var declaration = ClassOf(file);
        var classThis = ClassThisIdentifier();

        var once = ClassThis.InjectClassThisAssignmentIfMissing(Factory, declaration, classThis);
        var twice = ClassThis.InjectClassThisAssignmentIfMissing(Factory, once, classThis);

        Assert.Same(once, twice);
    }

    [Fact]
    public void AnUndecoratedClass_HasNoClassThisAssignment()
    {
        var file = Parse("class C { m() { } }");

        Assert.False(ClassThis.ClassHasClassThisAssignment(ClassOf(file)));
    }

    [Fact]
    public void TheBlocksSourceMapRange_IsTheClassName()
    {
        var file = Parse("class C { }");
        var declaration = ClassOf(file);

        var updated = ClassThis.InjectClassThisAssignmentIfMissing(
            Factory,
            declaration,
            ClassThisIdentifier());

        var statement = (Node)((ClassStaticBlockDeclaration)updated.Members![0]).Body!.Statements![0];
        var range = EmitNodes.GetSourceMapRange(statement);

        Assert.Equal(declaration.Name!.Pos, range.Pos);
        Assert.Equal(declaration.Name!.End, range.End);
    }

    [Fact]
    public void AClassExpression_IsInjectedInPlace()
    {
        var file = Parse("let k = class { m() { } };");
        var declaration = ((VariableStatement)file.Statements![0]).DeclarationList!.Declarations![0];
        var expression = (ClassExpression)declaration.Initializer!;

        var updated = ClassThis.InjectClassThisAssignmentIfMissing(
            Factory,
            expression,
            ClassThisIdentifier());

        Assert.IsType<ClassExpression>(updated);
        Assert.Equal(2, updated.Members!.Count);
        Assert.Equal("static { _classThis = this; }", Print((Node)updated.Members![0], file));
    }

    [Fact]
    public void AHeritageClauseAndTypeParameters_SurviveTheInjection()
    {
        var file = Parse("class C<T> extends B { m() { } }");
        var declaration = ClassOf(file);

        var updated = ClassThis.InjectClassThisAssignmentIfMissing(
            Factory,
            declaration,
            ClassThisIdentifier());

        Assert.Same(declaration.Name, updated.Name);
        Assert.Same(declaration.TypeParameters, updated.TypeParameters);
        Assert.Same(declaration.HeritageClauses, updated.HeritageClauses);
    }

    // ---------------------------------------------------------------------------------------------
    // classThis.ts:62-96 — the two guards
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("static { }")]
    [InlineData("static { a = this; b = this; }")]
    [InlineData("static { this.x = 1; }")]
    [InlineData("static { a += this; }")]
    [InlineData("static { _a = 1; }")]
    public void ABlockThatIsNotTheHelper_IsNotRecognised(string member)
    {
        var file = Parse($"class C {{ {member} }}");
        var block = (Node)ClassOf(file).Members![0];

        Assert.Equal(SyntaxKind.ClassStaticBlockDeclaration, block.Kind);
        Assert.False(ClassThis.IsClassThisAssignmentBlock(block));
        Assert.False(ClassThis.ClassHasClassThisAssignment(ClassOf(file)));
    }

    [Fact]
    public void AUserWrittenBlockShapedLikeTheHelper_IsNotRecognisedWithoutTheMarker()
    {
        // The block is exactly the shape the injector builds, but nothing put `_classThis` in its emit node, so the
        // identity test against `emitNode.classThis` refuses it — a plain `static { _classThis = this; }` in the source
        // is the user's block, not the helper.
        var file = Parse("class C { static { _classThis = this; } }");

        Assert.False(ClassThis.IsClassThisAssignmentBlock((Node)ClassOf(file).Members![0]));
        Assert.False(ClassThis.ClassHasClassThisAssignment(ClassOf(file)));
    }

    [Fact]
    public void ANonBlockNode_IsNotRecognised()
    {
        var file = Parse("class C { m() { } }");

        Assert.False(ClassThis.IsClassThisAssignmentBlock((Node)ClassOf(file)));
        Assert.False(ClassThis.IsClassThisAssignmentBlock((Node)ClassOf(file).Members![0]));
    }
}
