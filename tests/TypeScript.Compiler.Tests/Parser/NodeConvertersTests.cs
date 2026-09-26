namespace TypeScript.Compiler.Tests;

/// <summary>
/// Acceptance for the node converters (docs/architecture.md §15.9) — the port of <c>factory/nodeConverters.ts</c>.
/// </summary>
/// <remarks>
/// Every conversion here turns a *binding* form into its *assignment* counterpart, which is the transformation the
/// destructuring transformer asks for. The element shapes (rest, property name, default value) are the part that is easy
/// to get subtly wrong, so each one is pinned.
/// </remarks>
public class NodeConvertersTests
{
    private readonly NodeFactory _factory = new();

    private INodeConverters Converters => _factory.Converters;

    private Identifier Id(string text) => _factory.CreateIdentifier(text, null, null);

    // ---------------------------------------------------------------------------------------------
    // Function and class declarations
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ConvertToFunctionBlock_WrapsAnExpressionInAReturnStatement()
    {
        var body = Id("a");
        body.Pos = 3;
        body.End = 4;

        var block = Converters.ConvertToFunctionBlock(body, null);

        var returnStatement = Assert.IsType<ReturnStatement>(Assert.Single(block.Statements!));
        Assert.Same(body, returnStatement.Expression);
        Assert.Equal(3, returnStatement.Pos);
        Assert.Equal(3, block.Pos);
        Assert.Equal(4, block.End);
    }

    [Fact]
    public void ConvertToFunctionBlock_KeepsABlockAsItIs()
    {
        var block = _factory.CreateBlock([], null);

        Assert.Same(block, Converters.ConvertToFunctionBlock(block, null));
    }

    [Fact]
    public void ConvertToFunctionExpression_DropsExportAndDefaultModifiers()
    {
        var declaration = _factory.CreateFunctionDeclaration(
            [_factory.CreateModifier(SyntaxKind.ExportKeyword), _factory.CreateModifier(SyntaxKind.DefaultKeyword), _factory.CreateModifier(SyntaxKind.AsyncKeyword)],
            null,
            Id("f"),
            null,
            [],
            null,
            _factory.CreateBlock([], null));

        var expression = Converters.ConvertToFunctionExpression(declaration);

        var modifier = Assert.Single(expression.Modifiers!);
        Assert.Equal(SyntaxKind.AsyncKeyword, modifier.Kind);
        Assert.Same(declaration, expression.Original);
    }

    [Fact]
    public void ConvertToClassExpression_DropsExportAndDefaultModifiers()
    {
        var declaration = _factory.CreateClassDeclaration(
            [_factory.CreateModifier(SyntaxKind.ExportKeyword), _factory.CreateModifier(SyntaxKind.AbstractKeyword)],
            Id("C"),
            null,
            null,
            null);

        var expression = Converters.ConvertToClassExpression(declaration);

        var modifier = Assert.Single(expression.Modifiers!);
        Assert.Equal(SyntaxKind.AbstractKeyword, modifier.Kind);
        Assert.Same(declaration, expression.Original);
    }

    // ---------------------------------------------------------------------------------------------
    // Array assignment elements
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ConvertToArrayAssignmentElement_TurnsARestBindingIntoASpread()
    {
        var element = _factory.CreateBindingElement(
            (PunctuationToken)_factory.CreateToken(SyntaxKind.DotDotDotToken),
            null,
            Id("a"),
            null);

        var expression = Converters.ConvertToArrayAssignmentElement(element);

        var spread = Assert.IsType<SpreadElement>(expression);
        Assert.Equal("a", Assert.IsType<Identifier>(spread.Expression).EscapedText);
        Assert.Same(element, spread.Original);
    }

    [Fact]
    public void ConvertToArrayAssignmentElement_TurnsADefaultIntoAnAssignment()
    {
        var element = _factory.CreateBindingElement(null, null, Id("a"), Id("b"));

        var expression = Converters.ConvertToArrayAssignmentElement(element);

        var assignment = Assert.IsType<BinaryExpression>(expression);
        Assert.Equal(SyntaxKind.EqualsToken, assignment.OperatorToken!.Kind);
        Assert.Equal("a", Assert.IsType<Identifier>(assignment.Left).EscapedText);
        Assert.Equal("b", Assert.IsType<Identifier>(assignment.Right).EscapedText);
    }

    [Fact]
    public void ConvertToArrayAssignmentElement_PassesANonBindingThrough()
    {
        var expression = Id("a");

        Assert.Same(expression, Converters.ConvertToArrayAssignmentElement(expression));
    }

    // ---------------------------------------------------------------------------------------------
    // Object assignment elements
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ConvertToObjectAssignmentElement_TurnsARestBindingIntoASpreadAssignment()
    {
        var element = _factory.CreateBindingElement(
            (PunctuationToken)_factory.CreateToken(SyntaxKind.DotDotDotToken),
            null,
            Id("a"),
            null);

        var spread = Assert.IsType<SpreadAssignment>(Converters.ConvertToObjectAssignmentElement(element));

        Assert.Equal("a", Assert.IsType<Identifier>(spread.Expression).EscapedText);
    }

    [Fact]
    public void ConvertToObjectAssignmentElement_KeepsThePropertyName()
    {
        var element = _factory.CreateBindingElement(null, Id("a"), Id("b"), null);

        var property = Assert.IsType<PropertyAssignment>(Converters.ConvertToObjectAssignmentElement(element));

        Assert.Equal("a", Assert.IsType<Identifier>(property.Name).EscapedText);
        Assert.Equal("b", Assert.IsType<Identifier>(property.Initializer).EscapedText);
    }

    [Fact]
    public void ConvertToObjectAssignmentElement_TurnsADefaultIntoAnAssignmentUnderThePropertyName()
    {
        var element = _factory.CreateBindingElement(null, Id("a"), Id("b"), Id("c"));

        var property = Assert.IsType<PropertyAssignment>(Converters.ConvertToObjectAssignmentElement(element));

        var assignment = Assert.IsType<BinaryExpression>(property.Initializer);
        Assert.Equal("b", Assert.IsType<Identifier>(assignment.Left).EscapedText);
        Assert.Equal("c", Assert.IsType<Identifier>(assignment.Right).EscapedText);
    }

    [Fact]
    public void ConvertToObjectAssignmentElement_WithoutAPropertyNameBecomesShorthand()
    {
        var element = _factory.CreateBindingElement(null, null, Id("a"), null);

        var shorthand = Assert.IsType<ShorthandPropertyAssignment>(Converters.ConvertToObjectAssignmentElement(element));

        Assert.Equal("a", Assert.IsType<Identifier>(shorthand.Name).EscapedText);
    }

    // ---------------------------------------------------------------------------------------------
    // Patterns
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ConvertToArrayAssignmentPattern_RebuildsThePatternAsAnArrayLiteral()
    {
        var element = _factory.CreateBindingElement(null, null, Id("a"), null);
        var pattern = _factory.CreateArrayBindingPattern([element]);

        var literal = Converters.ConvertToArrayAssignmentPattern(pattern);

        Assert.Same(element.Name, Assert.Single(literal.Elements!));
        Assert.Same(pattern, literal.Original);
    }

    [Fact]
    public void ConvertToArrayAssignmentPattern_KeepsAnArrayLiteralAsItIs()
    {
        var literal = _factory.CreateArrayLiteralExpression([Id("a")], null);

        Assert.Same(literal, Converters.ConvertToArrayAssignmentPattern(literal));
    }

    [Fact]
    public void ConvertToObjectAssignmentPattern_RebuildsThePatternAsAnObjectLiteral()
    {
        var element = _factory.CreateBindingElement(null, null, Id("a"), null);
        var pattern = _factory.CreateObjectBindingPattern([element]);

        var literal = Converters.ConvertToObjectAssignmentPattern(pattern);

        Assert.IsType<ShorthandPropertyAssignment>(Assert.Single(literal.Properties!));
        Assert.Same(pattern, literal.Original);
    }

    [Fact]
    public void ConvertToAssignmentPattern_DispatchesOnTheKind()
    {
        var arrayPattern = _factory.CreateArrayBindingPattern([]);
        var objectPattern = _factory.CreateObjectBindingPattern([]);

        Assert.IsType<ArrayLiteralExpression>(Converters.ConvertToAssignmentPattern(arrayPattern));
        Assert.IsType<ObjectLiteralExpression>(Converters.ConvertToAssignmentPattern(objectPattern));
    }

    [Fact]
    public void ConvertToAssignmentElementTarget_ConvertsAPatternAndPassesANameThrough()
    {
        var nested = _factory.CreateArrayBindingPattern([]);
        var name = Id("a");

        Assert.IsType<ArrayLiteralExpression>(Converters.ConvertToAssignmentElementTarget(nested));
        Assert.Same(name, Converters.ConvertToAssignmentElementTarget(name));
    }

    // ---------------------------------------------------------------------------------------------
    // The flag that turns the converters off
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void NoNodeConverters_ThrowsForEveryMember()
    {
        var converters = new NodeFactory(NodeFactoryFlags.NoNodeConverters).Converters;

        Assert.ThrowsAny<Exception>(() => converters.ConvertToFunctionBlock(Id("a"), null));
        Assert.ThrowsAny<Exception>(() => converters.ConvertToAssignmentPattern(_factory.CreateArrayBindingPattern([])));
    }
}
