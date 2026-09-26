namespace TypeScript.Compiler.Tests;

/// <summary>
/// Acceptance for <c>factory/utilities.ts</c> (docs/architecture.md §15.9) — the helpers the factory, the
/// parenthesizer and the converters share.
/// </summary>
/// <remarks>
/// The binding/assignment element analysis is the part worth pinning: it answers "what is this element's target, its
/// property name, its initializer, its rest indicator" for six different node shapes, and every answer feeds a
/// transformer that rewrites a destructuring pattern.
/// </remarks>
public class FactoryUtilitiesTests
{
    private readonly NodeFactory _factory = new();

    private Identifier Id(string text) => _factory.CreateIdentifier(text, null, null);



    private ExpressionStatement Directive(string text)
        => _factory.CreateExpressionStatement(_factory.CreateStringLiteral(text, null, null));

    // ---------------------------------------------------------------------------------------------
    // BindingOrAssignmentElement analysis
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void GetInitializerOfBindingOrAssignmentElement_FindsTheDefaultOfEveryShape()
    {
        var declaration = _factory.CreateVariableDeclaration(Id("a"), null, null, Id("d"));
        var property = _factory.CreatePropertyAssignment(Id("a"), (Expression)_factory.CreateAssignment(Id("b"), Id("d")));
        var shorthand = _factory.CreateShorthandPropertyAssignment(Id("a"), Id("d"));
        var assignment = (Expression)_factory.CreateAssignment(Id("a"), Id("d"));
        var spread = _factory.CreateSpreadElement((Expression)_factory.CreateAssignment(Id("a"), Id("d")));

        Assert.Same(declaration.Initializer, FactoryUtilities.GetInitializerOfBindingOrAssignmentElement(declaration));
        Assert.Equal("d", Assert.IsType<Identifier>(FactoryUtilities.GetInitializerOfBindingOrAssignmentElement(property)).EscapedText);
        Assert.Same(shorthand.ObjectAssignmentInitializer, FactoryUtilities.GetInitializerOfBindingOrAssignmentElement(shorthand));
        Assert.Equal("d", Assert.IsType<Identifier>(FactoryUtilities.GetInitializerOfBindingOrAssignmentElement(assignment)).EscapedText);
        Assert.Equal("d", Assert.IsType<Identifier>(FactoryUtilities.GetInitializerOfBindingOrAssignmentElement(spread)).EscapedText);
    }

    [Fact]
    public void GetInitializerOfBindingOrAssignmentElement_OfALetWithoutDefaultIsNull()
    {
        Assert.Null(FactoryUtilities.GetInitializerOfBindingOrAssignmentElement(_factory.CreateVariableDeclaration(Id("a"), null, null, null)));
        Assert.Null(FactoryUtilities.GetInitializerOfBindingOrAssignmentElement(_factory.CreatePropertyAssignment(Id("a"), Id("b"))));
    }

    [Fact]
    public void GetTargetOfBindingOrAssignmentElement_UnwrapsEveryShape()
    {
        var declaration = _factory.CreateVariableDeclaration(Id("a"), null, null, null);
        var property = _factory.CreatePropertyAssignment(Id("a"), Id("b"));
        var spreadAssignment = _factory.CreateSpreadAssignment(Id("a"));
        var assignment = (Expression)_factory.CreateAssignment(Id("a"), Id("b"));
        var spread = _factory.CreateSpreadElement(Id("a"));
        var plain = Id("a");

        Assert.Same(declaration.Name, FactoryUtilities.GetTargetOfBindingOrAssignmentElement(declaration));
        Assert.Equal("b", Assert.IsType<Identifier>(FactoryUtilities.GetTargetOfBindingOrAssignmentElement(property)).EscapedText);
        Assert.Equal("a", Assert.IsType<Identifier>(FactoryUtilities.GetTargetOfBindingOrAssignmentElement(spreadAssignment)).EscapedText);
        Assert.Equal("a", Assert.IsType<Identifier>(FactoryUtilities.GetTargetOfBindingOrAssignmentElement(assignment)).EscapedText);
        Assert.Equal("a", Assert.IsType<Identifier>(FactoryUtilities.GetTargetOfBindingOrAssignmentElement(spread)).EscapedText);
        Assert.Same(plain, FactoryUtilities.GetTargetOfBindingOrAssignmentElement(plain));
    }

    [Fact]
    public void GetRestIndicatorOfBindingOrAssignmentElement_ReportsTheRestForm()
    {
        var dotDotDot = (PunctuationToken)_factory.CreateToken(SyntaxKind.DotDotDotToken);
        var parameter = _factory.CreateParameterDeclaration(null, dotDotDot, Id("a"), null, null, null);
        var spread = _factory.CreateSpreadElement(Id("a"));
        var property = _factory.CreatePropertyAssignment(Id("a"), Id("b"));

        Assert.Same(dotDotDot, FactoryUtilities.GetRestIndicatorOfBindingOrAssignmentElement(parameter));
        Assert.Same(spread, FactoryUtilities.GetRestIndicatorOfBindingOrAssignmentElement(spread));
        Assert.Null(FactoryUtilities.GetRestIndicatorOfBindingOrAssignmentElement(property));
    }

    [Fact]
    public void TryGetPropertyNameOfBindingOrAssignmentElement_UnwrapsAComputedLiteralKey()
    {
        // `let { ["a"]: b } = ...` — the key is the literal inside the computed name, not the computed name itself.
        var computed = _factory.CreateComputedPropertyName(_factory.CreateStringLiteral("a", null, null));
        var element = _factory.CreateBindingElement(null, computed, Id("b"), null);

        var propertyName = FactoryUtilities.TryGetPropertyNameOfBindingOrAssignmentElement(element);

        Assert.Equal(SyntaxKind.StringLiteral, propertyName!.Kind);
        Assert.Equal("a", ((StringLiteral)propertyName).Text);
    }

    [Fact]
    public void TryGetPropertyNameOfBindingOrAssignmentElement_KeepsANamedKey()
    {
        var element = _factory.CreateBindingElement(null, Id("a"), Id("b"), null);
        var property = _factory.CreatePropertyAssignment(Id("a"), Id("b"));
        var spread = _factory.CreateSpreadAssignment(Id("a"));

        Assert.Equal("a", Assert.IsType<Identifier>(FactoryUtilities.TryGetPropertyNameOfBindingOrAssignmentElement(element)).EscapedText);
        Assert.Equal("a", Assert.IsType<Identifier>(FactoryUtilities.TryGetPropertyNameOfBindingOrAssignmentElement(property)).EscapedText);
        // A spread assignment has no name of its own — TS asserts exactly that where the name is required.
        Assert.Null(FactoryUtilities.TryGetPropertyNameOfBindingOrAssignmentElement(spread));
    }

    [Fact]
    public void GetElementsOfBindingOrAssignmentPattern_ReadsElementsOrProperties()
    {
        var element = _factory.CreateBindingElement(null, null, Id("a"), null);
        var bindingPattern = _factory.CreateObjectBindingPattern([element]);
        var literal = _factory.CreateObjectLiteralExpression([_factory.CreateShorthandPropertyAssignment(Id("a"), null)], null);

        Assert.Same(element, Assert.Single(FactoryUtilities.GetElementsOfBindingOrAssignmentPattern(bindingPattern)));
        Assert.Single(FactoryUtilities.GetElementsOfBindingOrAssignmentPattern(literal));
    }

    [Fact]
    public void ContainsObjectRestOrSpread_WalksNestedPatternsWhenTheFlagsAreNotPropagated()
    {
        // `{ a: { ...b } }` — the object literal does not propagate `ContainsObjectRestOrSpread` at parse time, which is
        // why the helper walks the elements itself when it sees `ContainsES2018`.
        var nested = _factory.CreateObjectLiteralExpression([_factory.CreateSpreadAssignment(Id("b"))], null);
        var outer = _factory.CreateObjectLiteralExpression([_factory.CreatePropertyAssignment(Id("a"), nested)], null);
        outer.TransformFlags = TransformFlags.ContainsES2018;

        Assert.True(FactoryUtilities.ContainsObjectRestOrSpread(outer));

        // The same shape without a spread anywhere stays false.
        var withoutSpread = _factory.CreateObjectLiteralExpression([_factory.CreatePropertyAssignment(Id("a"), Id("b"))], null);
        withoutSpread.TransformFlags = TransformFlags.ContainsES2018;

        Assert.False(FactoryUtilities.ContainsObjectRestOrSpread(withoutSpread));
    }

    [Fact]
    public void ContainsObjectRestOrSpread_TrustsTheFlagWhenItIsSet()
    {
        var literal = _factory.CreateObjectLiteralExpression([], null);
        literal.TransformFlags |= TransformFlags.ContainsObjectRestOrSpread;

        Assert.True(FactoryUtilities.ContainsObjectRestOrSpread(literal));
    }

    // ---------------------------------------------------------------------------------------------
    // Flattening and elision
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void FlattenCommaList_UnwrapsNestedCommaExpressionsAndSyntheticParens()
    {
        var a = Id("a");
        var b = Id("b");
        var c = Id("c");
        var syntheticParen = _factory.CreateParenthesizedExpression(_factory.CreateComma(a, b));
        var nested = _factory.CreateComma(_factory.CreateComma(a, b), c);

        // A synthesized paren with no emit node is transparent…
        Assert.Equal(2, FactoryUtilities.FlattenCommaList(syntheticParen).Count);

        // …and a nested comma flattens to its leaves in order.
        var flattened = FactoryUtilities.FlattenCommaList(nested);

        Assert.Equal(3, flattened.Count);
        Assert.Same(a, flattened[0]);
        Assert.Same(c, flattened[2]);
    }

    [Fact]
    public void FlattenCommaList_KeepsAParenThatCarriesEmitState()
    {
        var parenthesized = _factory.CreateParenthesizedExpression(_factory.CreateComma(Id("a"), Id("b")));
        EmitNodes.AddEmitFlags(parenthesized, EmitFlags.NoComments);

        var flattened = FactoryUtilities.FlattenCommaList(parenthesized);

        Assert.Same(parenthesized, Assert.Single(flattened));
    }

    [Fact]
    public void ElideNodes_KeepsTheRangeAndTheTrailingCommaFlag()
    {
        var array = _factory.CreateNodeArray(new List<IStatement> { _factory.CreateEmptyStatement() }, true);
        array.Pos = 4;
        array.End = 5;

        var elided = FactoryUtilities.ElideNodes(_factory, array)!;

        Assert.Empty(elided!);
        Assert.True(elided.HasTrailingComma);
        Assert.Equal(4, elided.Pos);
        Assert.Equal(5, elided.End);
    }

    [Fact]
    public void ElideNodes_ReturnsAnEmptyArrayAsItIs()
    {
        var empty = _factory.CreateNodeArray(new List<IStatement>(), null);

        Assert.Same(empty, FactoryUtilities.ElideNodes(_factory, empty));
    }

    // ---------------------------------------------------------------------------------------------
    // Generated-name formatting
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void FormatGeneratedName_JoinsPrefixBaseAndSuffix()
    {
        Assert.Equal("x", FactoryUtilities.FormatGeneratedName(false, null, "x", null));
        Assert.Equal("prexpost", FactoryUtilities.FormatGeneratedName(false, "pre", "x", "post"));
        Assert.Equal("#x", FactoryUtilities.FormatGeneratedName(true, null, "x", null));
    }

    [Fact]
    public void FormatGeneratedName_DropsTheHashFromAHashPrefixedPart()
    {
        // TS's `formatGeneratedNamePart` strips a leading `#` from a string part, because a private name gets one added
        // back by the `privateName` flag — the rest of the prefix stays.
        Assert.Equal("#yx", FactoryUtilities.FormatGeneratedName(true, "#y", "x", null));
        Assert.Equal("yx", FactoryUtilities.FormatGeneratedName(false, "#y", "x", null));
    }

    [Fact]
    public void FormatGeneratedName_ResolvesAGeneratedNameThroughTheCallback()
    {
        var node = _factory.CreateUniqueName("inner", null);
        var part = new GeneratedNamePart(node, "pre", "post");

        var formatted = FactoryUtilities.FormatGeneratedName(
            false,
            part,
            "base",
            null,
            identifier => "[" + ((Identifier)identifier).EscapedText + "]");

        Assert.Equal("pre[inner]postbase", formatted);
    }

    [Fact]
    public void FormatGeneratedName_UsesIdTextForAPlainIdentifier()
    {
        // Only a *generated* identifier goes through the callback; a parsed one is formatted by `idText`.
        var node = Id("inner");
        var part = new GeneratedNamePart(node, "pre", "post");

        var formatted = FactoryUtilities.FormatGeneratedName(
            false,
            part,
            "base",
            null,
            identifier => "[" + ((Identifier)identifier).EscapedText + "]");

        Assert.Equal("preinnerpostbase", formatted);
    }

    [Fact]
    public void FormatGeneratedName_KeepsANonHashStringPartAsItIs()
    {
        Assert.Equal("plainx", FactoryUtilities.FormatGeneratedName(false, "plain", "x", null));
        Assert.Equal("x", FactoryUtilities.FormatGeneratedName(false, "", "x", null));
    }

    [Fact]
    public void GetNodeForGeneratedName_FollowsTheNodeFlagToTheOriginal()
    {
        var original = Id("x");
        var name = _factory.GetGeneratedNameForNode(original, null);
        var unique = _factory.CreateUniqueName("y", null);

        Assert.Same(original, FactoryUtilities.GetNodeForGeneratedName(name));
        Assert.Same(unique, FactoryUtilities.GetNodeForGeneratedName(unique));
    }

    // ---------------------------------------------------------------------------------------------
    // Outer expressions
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void SkipOuterExpressions_SkipsOnlyTheKindsItWasAskedFor()
    {
        var inner = Id("a");
        var partiallyEmitted = _factory.CreatePartiallyEmittedExpression(inner, null);
        var parenthesized = _factory.CreateParenthesizedExpression(partiallyEmitted);

        Assert.Same(parenthesized, FactoryUtilities.SkipOuterExpressions(parenthesized, OuterExpressionKinds.PartiallyEmittedExpressions));
        Assert.Same(inner, FactoryUtilities.SkipOuterExpressions(partiallyEmitted, OuterExpressionKinds.PartiallyEmittedExpressions));
        Assert.Same(partiallyEmitted, FactoryUtilities.SkipOuterExpressions(partiallyEmitted, OuterExpressionKinds.Parentheses));
    }

    [Fact]
    public void IsOuterExpression_AnswersByKind()
    {
        Assert.True(FactoryUtilities.IsOuterExpression(_factory.CreateParenthesizedExpression(Id("a")), OuterExpressionKinds.Parentheses));
        Assert.False(FactoryUtilities.IsOuterExpression(Id("a"), OuterExpressionKinds.All));
    }

    [Fact]
    public void WalkUpOuterExpressions_StopsAtTheFirstNonOuterParent()
    {
        var statement = _factory.CreateExpressionStatement(_factory.CreateParenthesizedExpression(_factory.CreateParenthesizedExpression(Id("a"))));
        var outerParen = (ParenthesizedExpression)((ExpressionStatement)statement).Expression!;
        var innerParen = (ParenthesizedExpression)outerParen.Expression!;
        innerParen.Parent = outerParen;
        outerParen.Parent = statement;

        Assert.Same(statement, FactoryUtilities.WalkUpOuterExpressions(innerParen, OuterExpressionKinds.Parentheses));
    }

    // ---------------------------------------------------------------------------------------------
    // Prologues
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void FindUseStrictPrologue_StopsAtTheFirstNonDirective()
    {
        var useStrict = Directive("use strict");
        var other = Directive("other");

        Assert.Same(useStrict, FactoryUtilities.FindUseStrictPrologue(new List<IStatement> { useStrict, other }));
        // A directive that is not `use strict` does not stop the scan — only a non-directive does.
        Assert.Same(useStrict, FactoryUtilities.FindUseStrictPrologue(new List<IStatement> { other, useStrict }));
        Assert.Null(FactoryUtilities.FindUseStrictPrologue(new List<IStatement> { _factory.CreateEmptyStatement(), useStrict }));
        Assert.Null(FactoryUtilities.FindUseStrictPrologue(new List<IStatement>()));
    }

    [Fact]
    public void StartsWithUseStrict_OnlyLooksAtTheFirstStatement()
    {
        Assert.True(FactoryUtilities.StartsWithUseStrict(new List<IStatement> { Directive("use strict") }));
        Assert.False(FactoryUtilities.StartsWithUseStrict(new List<IStatement> { _factory.CreateEmptyStatement(), Directive("use strict") }));
    }

    // ---------------------------------------------------------------------------------------------
    // The emission-name predicates
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void IsLocalName_ReadsTheEmitFlagRatherThanTheSpelling()
    {
        // The predicate TS uses is `(getEmitFlags(node) & EmitFlags.LocalName) !== 0`; an earlier port answered
        // "escapedText is `arguments` or `this`", which is a different question with a different answer for both of
        // these nodes.
        var arguments = Id("arguments");
        var flagged = Id("x");
        EmitNodes.AddEmitFlags(flagged, EmitFlags.LocalName);

        Assert.False(FactoryUtilities.IsLocalName(arguments));
        Assert.True(FactoryUtilities.IsLocalName(flagged));
        Assert.False(FactoryUtilities.IsLocalName(Id("x")));
    }

    [Fact]
    public void IsExportName_ReadsTheEmitFlag()
    {
        var flagged = Id("x");
        EmitNodes.AddEmitFlags(flagged, EmitFlags.ExportName);

        Assert.True(FactoryUtilities.IsExportName(flagged));
        Assert.False(FactoryUtilities.IsExportName(Id("x")));
    }

    // ---------------------------------------------------------------------------------------------
    // Operator classification
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData((int)SyntaxKind.PlusToken, true)]
    [InlineData((int)SyntaxKind.GreaterThanToken, true)]
    [InlineData((int)SyntaxKind.StaticKeyword, false)]
    public void IsBinaryOperatorToken_AnswersFromThePrecedenceChain(int kind, bool expected)
    {
        Assert.Equal(expected, FactoryUtilities.IsBinaryOperatorToken(_factory.CreateToken((SyntaxKind)kind)));
    }

    [Theory]
    [InlineData((int)SyntaxKind.LessThanLessThanToken, true)]
    [InlineData((int)SyntaxKind.PlusToken, true)]
    [InlineData((int)SyntaxKind.AsteriskAsteriskToken, true)]
    public void IsShiftOperatorOrHigher_FollowsThePrecedenceLadder(int kind, bool expected)
    {
        Assert.Equal(expected, FactoryUtilities.IsShiftOperatorOrHigher((SyntaxKind)kind));
    }

    [Fact]
    public void IsExportOrDefaultModifier_RecognisesTheTwoKeywords()
    {
        Assert.True(FactoryUtilities.IsExportOrDefaultModifier(_factory.CreateModifier(SyntaxKind.ExportKeyword)));
        Assert.True(FactoryUtilities.IsExportOrDefaultModifier(_factory.CreateModifier(SyntaxKind.DefaultKeyword)));
        Assert.False(FactoryUtilities.IsExportOrDefaultModifier(_factory.CreateModifier(SyntaxKind.StaticKeyword)));
    }

    // ---------------------------------------------------------------------------------------------
    // Compound builders and the trampoline
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void CreateEmptyExports_BuildsAnExportDeclarationWithNoSpecifiers()
    {
        var exports = FactoryUtilities.CreateEmptyExports(_factory);

        var namedExports = Assert.IsType<NamedExports>(exports.ExportClause);
        Assert.Empty(namedExports.Elements!);
    }

    [Fact]
    public void ExpandPreOrPostfixIncrement_TurnsTheOperandIntoATemp()
    {
        var operand = Id("x");
        var node = _factory.CreatePrefixUnaryExpression(SyntaxKind.PlusPlusToken, operand);
        Identifier? temp = null;

        var expanded = FactoryUtilities.ExpandPreOrPostfixIncrementOrDecrementExpression(
            _factory,
            node,
            operand,
            identifier => temp = identifier,
            null);

        // `++x` expands to `(_temp = x, ++_temp)`.
        var comma = Assert.IsType<BinaryExpression>(expanded);
        Assert.Equal(SyntaxKind.CommaToken, comma.OperatorToken!.Kind);
        Assert.NotNull(temp);
        var assignment = Assert.IsType<BinaryExpression>(comma.Left);
        Assert.Same(temp, assignment.Left);
        Assert.Same(operand, assignment.Right);
        var operation = Assert.IsType<PrefixUnaryExpression>(comma.Right);
        Assert.Same(temp, operation.Operand);
    }

    [Fact]
    public void CreateAccessorPropertyBackingField_NamesTheFieldAfterTheProperty()
    {
        var property = _factory.CreatePropertyDeclaration(null, Id("x"), null, null, null);

        var field = FactoryUtilities.CreateAccessorPropertyBackingField(_factory, property, null, null);

        Assert.Equal("#x_accessor_storage", ((PrivateIdentifier)field.Name!).EscapedText);
    }

    [Fact]
    public void FindComputedPropertyNameCacheAssignment_FindsTheGeneratedAssignment()
    {
        var name = _factory.CreateComputedPropertyName(Id("a"));
        var generated = _factory.CreateUniqueName("cache", null);
        var assignment = (Expression)_factory.CreateAssignment(generated, Id("b"));
        name.Expression = _factory.CreateComma(Id("a"), assignment);

        Assert.Same(assignment, FactoryUtilities.FindComputedPropertyNameCacheAssignment(name));
    }

    [Fact]
    public void FindComputedPropertyNameCacheAssignment_OfAPlainExpressionIsNull()
    {
        var name = _factory.CreateComputedPropertyName(_factory.CreateComma(Id("a"), Id("b")));

        Assert.Null(FactoryUtilities.FindComputedPropertyNameCacheAssignment(name));
    }

    [Fact]
    public void CreateBinaryExpressionTrampoline_WalksTheTreeWithoutRecursion()
    {
        // `(a + b) + c` — the trampoline visits both binary nodes and both leaves, in order, and folds the results.
        var tree = _factory.CreateAdd(_factory.CreateAdd(Id("a"), Id("b")), Id("c"));
        var entered = new List<SyntaxKind>();
        int leftVisits = 0;
        int rightVisits = 0;
        int exits = 0;

        var walk = FactoryUtilities.CreateBinaryExpressionTrampoline<int, string>(
            (node, prev) =>
            {
                entered.Add(node.Kind);
                return prev + 1;
            },
            (left, state, node) =>
            {
                leftVisits++;
                return left is BinaryExpression nested && nested != node ? nested : null;
            },
            null,
            (right, state, node) =>
            {
                rightVisits++;
                return right as BinaryExpression;
            },
            (node, state) =>
            {
                exits++;
                return state.ToString();
            },
            null);

        var result = walk(tree);

        // Only the outermost node's own state reaches the caller: a nested result is folded in only when `foldState` is
        // supplied.
        Assert.Equal("1", result);
        Assert.Equal(2, exits);
        Assert.Equal(2, leftVisits);
        Assert.Equal(2, rightVisits);
        Assert.Equal(2, entered.Count);
    }
}
