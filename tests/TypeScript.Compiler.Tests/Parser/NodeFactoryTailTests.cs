namespace TypeScript.Compiler.Tests;

/// <summary>
/// Acceptance for the factory members that arrived with M4d (docs/architecture.md §15.9): the generated names, the
/// operator shortcuts, the call-binding builder, cloning, the prologue copiers and the union-typed replacers.
/// </summary>
/// <remarks>
/// These are the members of <c>factory/nodeFactory.ts</c> that the fixed-shape generator cannot express — the ones the
/// prologue copiers' splice order, the generated-name bookkeeping and <c>cloneNode</c>'s "fresh node" resets live in.
/// </remarks>
public class NodeFactoryTailTests
{
    private readonly NodeFactory _factory = new();

    private Identifier Id(string text) => _factory.CreateIdentifier(text, null, null);

    private static GeneratedIdentifierFlags AutoGenerateFlagsOf(Node node)
        => EmitNodes.GetIdentifierAutoGenerate(node)!.Flags;

    // ---------------------------------------------------------------------------------------------
    // Generated names
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void CreateTempVariable_UsesTheAutoFlagAndReportsTheVariable()
    {
        Identifier? recorded = null;

        var temp = _factory.CreateTempVariable(identifier => recorded = identifier, null);

        Assert.Same(temp, recorded);
        Assert.Equal(GeneratedIdentifierFlags.Auto, AutoGenerateFlagsOf(temp));
        Assert.Equal("", temp.EscapedText);
    }

    [Fact]
    public void CreateTempVariable_ReservingInNestedScopesAddsTheFlag()
    {
        var temp = _factory.CreateTempVariable(null, true);

        Assert.Equal(
            GeneratedIdentifierFlags.Auto | GeneratedIdentifierFlags.ReservedInNestedScopes,
            AutoGenerateFlagsOf(temp));
    }

    [Fact]
    public void CreateLoopVariable_UsesTheLoopFlag()
    {
        Assert.Equal(GeneratedIdentifierFlags.Loop, AutoGenerateFlagsOf(_factory.CreateLoopVariable(null)));
    }

    [Fact]
    public void CreateUniqueName_CombinesUniqueWithTheCallersFlags()
    {
        var name = _factory.CreateUniqueName("x", GeneratedIdentifierFlags.Optimistic);

        Assert.Equal(
            GeneratedIdentifierFlags.Unique | GeneratedIdentifierFlags.Optimistic,
            AutoGenerateFlagsOf(name));
    }

    [Fact]
    public void GeneratedNames_GetDistinctIds()
    {
        var first = _factory.CreateLoopVariable(null);
        var second = _factory.CreateLoopVariable(null);

        Assert.NotEqual(
            EmitNodes.GetIdentifierAutoGenerate(first)!.Id,
            EmitNodes.GetIdentifierAutoGenerate(second)!.Id);
    }

    [Fact]
    public void GetGeneratedNameForNode_UsesAMemberNameAndItsIdForAnythingElse()
    {
        var named = _factory.GetGeneratedNameForNode(Id("x"), null);
        var anonymous = _factory.GetGeneratedNameForNode(_factory.CreateBlock([], null), null);

        Assert.Equal("x", named.EscapedText);
        Assert.NotEqual(GeneratedIdentifierFlags.None, AutoGenerateFlagsOf(named) & GeneratedIdentifierFlags.Node);
        Assert.StartsWith("generated@", anonymous.EscapedText);
    }

    [Fact]
    public void GetGeneratedNameForNode_KeepsTheNodeAsItsOriginal()
    {
        var node = Id("x");

        Assert.Same(node, _factory.GetGeneratedNameForNode(node, null).Original);
    }

    [Fact]
    public void GetGeneratedPrivateNameForNode_MarksTheNamePrivate()
    {
        var name = _factory.GetGeneratedPrivateNameForNode(Id("x"));

        Assert.Equal("#x", name.EscapedText);
        Assert.NotEqual(GeneratedIdentifierFlags.None, AutoGenerateFlagsOf(name) & GeneratedIdentifierFlags.Node);
    }

    [Fact]
    public void CreateUniquePrivateName_RequiresTheHashPrefix()
    {
        Assert.Equal("#x", _factory.CreateUniquePrivateName("#x").EscapedText);

        // TS fails the factory here; the port's Debug.Fail throws, so a name without the prefix is not silently kept.
        Assert.ThrowsAny<Exception>(() => _factory.CreateUniquePrivateName("x"));
    }

    // ---------------------------------------------------------------------------------------------
    // Operator shortcuts
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void BinaryShortcuts_BuildTheirOwnOperator()
    {
        Assert.Equal(SyntaxKind.PlusToken, _factory.CreateAdd(Id("a"), Id("b")).OperatorToken!.Kind);
        Assert.Equal(SyntaxKind.MinusToken, _factory.CreateSubtract(Id("a"), Id("b")).OperatorToken!.Kind);
        Assert.Equal(SyntaxKind.AsteriskAsteriskToken, _factory.CreateExponent(Id("a"), Id("b")).OperatorToken!.Kind);
        Assert.Equal(SyntaxKind.EqualsEqualsEqualsToken, _factory.CreateStrictEquality(Id("a"), Id("b")).OperatorToken!.Kind);
        Assert.Equal(SyntaxKind.QuestionQuestionToken, _factory.CreateBinaryExpression(Id("a"), SyntaxKind.QuestionQuestionToken, Id("b")).OperatorToken!.Kind);
    }

    [Fact]
    public void CommaAndAssignment_AreTheBinaryFormsOfTheirOperators()
    {
        Assert.Equal(SyntaxKind.CommaToken, _factory.CreateComma(Id("a"), Id("b")).OperatorToken!.Kind);
        Assert.Equal(SyntaxKind.EqualsToken, _factory.CreateAssignment(Id("a"), Id("b")).OperatorToken!.Kind);
    }

    [Fact]
    public void UnaryShortcuts_BuildTheirOwnOperator()
    {
        Assert.Equal(SyntaxKind.PlusPlusToken, _factory.CreatePrefixIncrement(Id("a")).Operator);
        Assert.Equal(SyntaxKind.ExclamationToken, _factory.CreateLogicalNot(Id("a")).Operator);
        Assert.Equal(SyntaxKind.TildeToken, _factory.CreateBitwiseNot(Id("a")).Operator);
        Assert.Equal(SyntaxKind.MinusMinusToken, _factory.CreatePostfixDecrement(Id("a")).Operator);
    }

    [Fact]
    public void CreatePrefixIncrement_FlagsAnUpdateOfANameThatIsNotLocal()
    {
        // `++arguments` is an update of an identifier, so the flag has to be set — the earlier port's `IsLocalName`
        // answered "the name is `arguments`" and suppressed it, and the module transformer reads that flag to decide
        // whether to rewrite `exports.x++`.
        var increment = _factory.CreatePrefixIncrement(Id("arguments"));

        Assert.NotEqual(TransformFlags.None, increment.TransformFlags & TransformFlags.ContainsUpdateExpressionForIdentifier);

        // A name that really is local (the emit flag, set by `getLocalName`) is the case the flag must not be set for.
        var localName = Id("x");
        EmitNodes.AddEmitFlags(localName, EmitFlags.LocalName);

        Assert.Equal(TransformFlags.None, _factory.CreatePrefixIncrement(localName).TransformFlags & TransformFlags.ContainsUpdateExpressionForIdentifier);
    }

    // ---------------------------------------------------------------------------------------------
    // Binary expression transform flags
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void CreateBinaryExpression_SetsTheFlagsTheOperatorImplies()
    {
        Assert.NotEqual(TransformFlags.None, _factory.CreateBinaryExpression(Id("a"), SyntaxKind.QuestionQuestionToken, Id("b")).TransformFlags & TransformFlags.ContainsES2020);
        Assert.NotEqual(TransformFlags.None, _factory.CreateBinaryExpression(Id("a"), SyntaxKind.AsteriskAsteriskToken, Id("b")).TransformFlags & TransformFlags.ContainsES2016);
        Assert.NotEqual(TransformFlags.None, _factory.CreateBinaryExpression(Id("a"), SyntaxKind.AmpersandAmpersandEqualsToken, Id("b")).TransformFlags & TransformFlags.ContainsES2021);
    }

    [Fact]
    public void CreateBinaryExpression_FlagsADestructuringAssignmentOnItsLeft()
    {
        var assignment = _factory.CreateAssignment(_factory.CreateArrayLiteralExpression([Id("a")], null), Id("b"));

        Assert.NotEqual(TransformFlags.None, assignment.TransformFlags & TransformFlags.ContainsES2015);
        Assert.NotEqual(TransformFlags.None, assignment.TransformFlags & TransformFlags.ContainsDestructuringAssignment);
    }

    [Fact]
    public void UpdateBinaryExpression_KeepsTheNodeWhenNothingChanged()
    {
        var node = _factory.CreateAdd(Id("a"), Id("b"));

        Assert.Same(node, _factory.UpdateBinaryExpression(node, node.Left, node.OperatorToken, node.Right));
        Assert.NotSame(node, _factory.UpdateBinaryExpression(node, Id("c"), node.OperatorToken, node.Right));
    }

    [Fact]
    public void CreateTypeCheck_ComparesAgainstNullUndefinedAndTypeOf()
    {
        var value = Id("a");

        var againstNull = _factory.CreateTypeCheck(value, "null");
        var againstUndefined = _factory.CreateTypeCheck(value, "undefined");
        var againstString = _factory.CreateTypeCheck(value, "string");

        Assert.Equal(SyntaxKind.NullKeyword, ((BinaryExpression)againstNull).Right!.Kind);
        Assert.Equal(SyntaxKind.VoidExpression, ((BinaryExpression)againstUndefined).Right!.Kind);
        Assert.Equal(SyntaxKind.TypeOfExpression, ((BinaryExpression)againstString).Left!.Kind);
    }

    // ---------------------------------------------------------------------------------------------
    // Call bindings
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void CreateCallBinding_ForAPlainCallee_UsesVoidZeroAsThisArg()
    {
        var binding = _factory.CreateCallBinding(Id("a"), null, ScriptTarget.ES2015, true);

        Assert.Equal(SyntaxKind.Identifier, binding.Target!.Kind);
        Assert.Equal(SyntaxKind.VoidExpression, binding.ThisArg!.Kind);
    }

    [Fact]
    public void CreateCallBinding_CapturesAPropertyAccessReceiverInATemp()
    {
        var access = _factory.CreatePropertyAccessExpression(Id("a"), Id("b"));

        var binding = _factory.CreateCallBinding(access, null, ScriptTarget.ES2015, true);

        // for `a.b()` the target is `(_a = a).b` and `thisArg` is `_a`
        var target = Assert.IsType<PropertyAccessExpression>(binding.Target);
        var thisArg = Assert.IsType<Identifier>(binding.ThisArg);
        var parenthesized = Assert.IsType<ParenthesizedExpression>(target.Expression);
        var assignment = Assert.IsType<BinaryExpression>(parenthesized.Expression);

        Assert.Same(thisArg, assignment.Left);
        Assert.Same(access.Expression, assignment.Right);
        Assert.Equal(GeneratedIdentifierFlags.Auto, AutoGenerateFlagsOf(thisArg));
    }

    [Fact]
    public void CreateCallBinding_DoesNotCaptureALiteralReceiver()
    {
        var access = _factory.CreatePropertyAccessExpression(_factory.CreateStringLiteral("a", null, null), Id("b"));

        var binding = _factory.CreateCallBinding(access, null, ScriptTarget.ES2015, false);

        Assert.Same(access, binding.Target);
        Assert.Same(access.Expression, binding.ThisArg);
    }

    // ---------------------------------------------------------------------------------------------
    // Cloning
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void CloneNode_ResetsTheRangeAndRemembersTheOriginal()
    {
        var expression = Id("a");
        var original = _factory.CreatePropertyAccessExpression(expression, Id("b"));
        original.Pos = 10;
        original.End = 20;

        var clone = _factory.CloneNode(original);

        Assert.NotSame(original, clone);
        Assert.True(Utilities.NodeIsSynthesized(clone));
        Assert.Same(original, clone.Original);
        Assert.Same(expression, clone.Expression);
        Assert.Equal(10, original.Pos);
    }

    [Fact]
    public void CloneNode_DropsTheSynthesizedFlagAndKeepsTheTransformFlags()
    {
        var original = _factory.CreateAdd(Id("await"), Id("b"));
        original.Flags |= NodeFlags.Synthesized;

        var clone = _factory.CloneNode(original);

        Assert.Equal(original.Flags & ~NodeFlags.Synthesized, clone.Flags);
        Assert.Equal(original.TransformFlags, clone.TransformFlags);
    }

    [Fact]
    public void CloneNode_ResetsTheParentAndKeepsTheEmitNode()
    {
        var parent = _factory.CreateBlock([], null);
        var original = _factory.CreateAdd(Id("a"), Id("b"));
        original.Parent = parent;
        EmitNodes.AddEmitFlags(original, EmitFlags.NoComments);

        var clone = _factory.CloneNode(original);

        Assert.Null(clone.Parent);
        Assert.True(Utilities.NodeIsSynthesized(clone));
        Assert.Equal(EmitFlags.NoComments, Utilities.GetEmitFlags(clone));
    }

    [Fact]
    public void CloneNode_OfAnIdentifier_CopiesItsGeneratedNameInfo()
    {
        var original = _factory.CreateUniqueName("x", GeneratedIdentifierFlags.Optimistic);

        var clone = _factory.CloneNode(original);

        // TS spreads the auto-generate info, so the clone keeps the id (and therefore the same emitted name) but not the
        // same object.
        Assert.Same(original, clone.Original);
        Assert.NotSame(
            EmitNodes.GetIdentifierAutoGenerate(original),
            EmitNodes.GetIdentifierAutoGenerate(clone));
        Assert.Equal(
            EmitNodes.GetIdentifierAutoGenerate(original)!.Id,
            EmitNodes.GetIdentifierAutoGenerate(clone)!.Id);
    }

    [Fact]
    public void CloneNode_OfABlock_ReturnsANewBlockWithTheSameStatements()
    {
        var statement = _factory.CreateEmptyStatement();
        var original = _factory.CreateBlock([statement], null);

        var clone = _factory.CloneNode(original);

        Assert.Single(clone.Statements!);
        Assert.Same(statement, clone.Statements![0]);
    }

    // ---------------------------------------------------------------------------------------------
    // Prologues and the lexical environment
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void CopyStandardPrologue_CopiesDirectivesAndAddsUseStrictOnRequest()
    {
        var useStrict = _factory.CreateExpressionStatement(_factory.CreateStringLiteral("use strict", null, null));
        var otherDirective = _factory.CreateExpressionStatement(_factory.CreateStringLiteral("custom", null, null));
        var statement = _factory.CreateEmptyStatement();
        var source = new List<IStatement> { useStrict, otherDirective, statement };
        var target = new List<IStatement>();

        var offset = _factory.CopyStandardPrologue(source, target, null, true);

        Assert.Equal(2, offset);
        Assert.Equal(new IStatement[] { useStrict, otherDirective }, target);
    }

    [Fact]
    public void CopyStandardPrologue_AddsUseStrictWhenTheSourceHasNone()
    {
        var source = new List<IStatement> { _factory.CreateEmptyStatement() };
        var target = new List<IStatement>();

        Assert.Equal(0, _factory.CopyStandardPrologue(source, target, null, true));
        Assert.Single(target);
        Assert.Equal(SyntaxKind.ExpressionStatement, target[0].Kind);
    }

    [Fact]
    public void CopyStandardPrologue_DoesNotAddUseStrictWhenItIsAlreadyThere()
    {
        var useStrict = _factory.CreateExpressionStatement(_factory.CreateStringLiteral("use strict", null, null));
        var source = new List<IStatement> { useStrict };
        var target = new List<IStatement>();

        Assert.Equal(1, _factory.CopyStandardPrologue(source, target, null, true));
        Assert.Single(target);
    }

    [Fact]
    public void CopyCustomPrologue_CopiesOnlyCustomPrologueStatementsAndVisitsThem()
    {
        var custom = _factory.CreateEmptyStatement();
        EmitNodes.AddEmitFlags(custom, EmitFlags.CustomPrologue);
        var ordinary = _factory.CreateEmptyStatement();
        var source = new List<IStatement> { custom, ordinary };
        var target = new List<IStatement>();
        var visited = new List<Node>();

        var offset = _factory.CopyCustomPrologue(
            source,
            target,
            0,
            node =>
            {
                visited.Add(node);
                return node;
            },
            null);

        Assert.Equal(1, offset);
        Assert.Same(custom, Assert.Single(target));
        Assert.Same(custom, Assert.Single(visited));
    }

    [Fact]
    public void EnsureUseStrict_PrependsThePrologueAndKeepsTheRange()
    {
        var statements = _factory.CreateNodeArray(new List<IStatement> { _factory.CreateEmptyStatement() }, null);
        statements.Pos = 5;
        statements.End = 6;

        var merged = _factory.EnsureUseStrict(statements);

        Assert.Equal(2, merged.Count);
        Assert.Equal(SyntaxKind.ExpressionStatement, merged[0].Kind);
        Assert.Equal(5, merged.Pos);
        Assert.Equal(6, merged.End);
    }

    [Fact]
    public void EnsureUseStrict_ReturnsTheSameArrayWhenThePrologueIsPresent()
    {
        var useStrict = _factory.CreateExpressionStatement(_factory.CreateStringLiteral("use strict", null, null));
        var statements = _factory.CreateNodeArray(new List<IStatement> { useStrict }, null);

        Assert.Same(statements, _factory.EnsureUseStrict(statements));
    }

    [Fact]
    public void MergeLexicalEnvironment_PutsTheIncomingHoistedFunctionsFirst()
    {
        var useStrict = _factory.CreateExpressionStatement(_factory.CreateStringLiteral("use strict", null, null));
        var leftFunction = HoistedFunction("left");
        var rightFunction = HoistedFunction("right");
        var statements = _factory.CreateNodeArray(new List<IStatement> { useStrict, leftFunction }, null);

        var merged = _factory.MergeLexicalEnvironment(statements, [rightFunction]);

        Assert.Equal(new IStatement[] { useStrict, rightFunction, leftFunction }, merged);
        Assert.Equal(statements.Pos, merged.Pos);
    }

    [Fact]
    public void MergeLexicalEnvironment_ReturnsTheInputWhenThereIsNothingToMerge()
    {
        var statements = _factory.CreateNodeArray(new List<IStatement> { _factory.CreateEmptyStatement() }, null);

        Assert.Same(statements, _factory.MergeLexicalEnvironment(statements, null));
    }

    [Fact]
    public void MergeLexicalEnvironment_SplicesAPlainListInPlace()
    {
        var existing = _factory.CreateEmptyStatement();
        var statements = new List<IStatement> { existing };
        var hoistedVariable = _factory.CreateVariableStatement(
            null,
            _factory.CreateVariableDeclarationList([], NodeFlags.None));
        EmitNodes.AddEmitFlags(hoistedVariable, EmitFlags.CustomPrologue);

        var merged = _factory.MergeLexicalEnvironment(statements, [hoistedVariable]);

        // The right-hand prologue is spliced in before the left-hand statements, and a plain list is updated in place
        // (TS mutates the caller's array in that case; only a `NodeArray` is copied).
        Assert.Same(statements, merged);
        Assert.Equal(new IStatement[] { hoistedVariable, existing }, statements);
    }

    [Fact]
    public void LiftToBlock_ReturnsTheOnlyNodeOrWrapsInABlock()
    {
        var statement = _factory.CreateEmptyStatement();

        Assert.Same(statement, _factory.LiftToBlock([statement]));

        var block = _factory.LiftToBlock([statement, _factory.CreateEmptyStatement()]);

        Assert.Equal(SyntaxKind.Block, block.Kind);
        Assert.Equal(2, ((Block)block).Statements!.Count);
    }

    [Fact]
    public void InlineExpressions_UsesACommaListOnlyAboveTenExpressions()
    {
        var two = _factory.InlineExpressions([Id("a"), Id("b")]);
        List<Expression> many = [.. Enumerable.Range(0, 11).Select(index => (Expression)Id("x" + index))];
        var inlined = _factory.InlineExpressions(many);

        Assert.Equal(SyntaxKind.BinaryExpression, two.Kind);
        Assert.Equal(SyntaxKind.CommaToken, ((BinaryExpression)two).OperatorToken!.Kind);
        Assert.Equal(SyntaxKind.CommaListExpression, inlined.Kind);
        Assert.Equal(11, ((CommaListExpression)inlined).Elements!.Count);
    }

    [Fact]
    public void InlineExpressions_OfASingleExpressionReturnsIt()
    {
        var expression = Id("a");

        Assert.Same(expression, _factory.InlineExpressions([expression]));
    }

    // ---------------------------------------------------------------------------------------------
    // Restoring and replacing
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void RestoreOuterExpressions_ReattachesTheWrappers()
    {
        var inner = Id("a");
        var parenthesized = _factory.CreateParenthesizedExpression(inner);

        // A parenthesis the source wrote keeps its range, which is what makes it non-ignorable.
        parenthesized.Pos = 0;
        parenthesized.End = 3;

        var replacement = Id("b");
        var restored = _factory.RestoreOuterExpressions(parenthesized, replacement, null);

        var wrapper = Assert.IsType<ParenthesizedExpression>(restored);
        Assert.Same(replacement, wrapper.Expression);
    }

    [Fact]
    public void RestoreOuterExpressions_IgnoresASynthesizedParenthesis()
    {
        // An ignorable paren is one nothing asked for: synthesized, no ranges, no comments.
        var parenthesized = _factory.CreateParenthesizedExpression(Id("a"));
        parenthesized.Pos = -1;
        parenthesized.End = -1;
        var inner = Id("b");

        Assert.Same(inner, _factory.RestoreOuterExpressions(parenthesized, inner, null));
    }

    [Fact]
    public void RestoreEnclosingLabel_RebuildsTheLabelChain()
    {
        var statement = _factory.CreateEmptyStatement();
        var label = _factory.CreateLabeledStatement(Id("outer"), statement);

        var restored = _factory.RestoreEnclosingLabel(_factory.CreateEmptyStatement(), label, null);

        var updated = Assert.IsType<LabeledStatement>(restored);
        Assert.NotSame(label, updated);
        Assert.Equal(SyntaxKind.EmptyStatement, updated.Statement!.Kind);
    }

    [Fact]
    public void ReplacePropertyName_SwitchesOnTheNodeKind()
    {
        var method = _factory.CreateMethodDeclaration(null, null, Id("old"), null, null, [], null, _factory.CreateBlock([], null));

        var updated = _factory.ReplacePropertyName(method, Id("new"));

        Assert.NotSame(method, updated);
        Assert.Equal("new", ((Identifier)((MethodDeclaration)updated).Name!).EscapedText);
    }

    [Fact]
    public void ReplaceModifiers_ReplacesTheModifierList()
    {
        var method = _factory.CreateMethodDeclaration(null, null, Id("m"), null, null, [], null, _factory.CreateBlock([], null));
        var modifier = _factory.CreateModifier(SyntaxKind.StaticKeyword);

        var updated = _factory.ReplaceModifiers(method, new List<Node> { modifier });

        Assert.NotSame(method, updated);
        Assert.Same(modifier, Assert.Single(((MethodDeclaration)updated).Modifiers!));
    }

    [Fact]
    public void ReplaceModifiers_FromFlagsBuildsTheModifierArray()
    {
        var method = _factory.CreateMethodDeclaration(null, null, Id("m"), null, null, [], null, _factory.CreateBlock([], null));

        var updated = (MethodDeclaration)_factory.ReplaceModifiers(method, ModifierFlags.Static | ModifierFlags.Export);

        Assert.Equal(2, updated.Modifiers!.Count);
        Assert.Equal(SyntaxKind.StaticKeyword, updated.Modifiers[1].Kind);
    }

    [Fact]
    public void ReplaceDecoratorsAndModifiers_ReplacesTheListWithDecoratorsIncluded()
    {
        var property = _factory.CreatePropertyDeclaration(null, Id("p"), null, null, null);
        var decorator = _factory.CreateDecorator(Id("dec"));

        var updated = (PropertyDeclaration)_factory.ReplaceDecoratorsAndModifiers(property, new List<Node> { decorator });

        Assert.Same(decorator, Assert.Single(updated.Modifiers!));
    }

    private FunctionDeclaration HoistedFunction(string name)
    {
        var declaration = _factory.CreateFunctionDeclaration(
            null,
            null,
            Id(name),
            null,
            [],
            null,
            _factory.CreateBlock([], null));
        EmitNodes.AddEmitFlags(declaration, EmitFlags.CustomPrologue);
        return declaration;
    }
}
