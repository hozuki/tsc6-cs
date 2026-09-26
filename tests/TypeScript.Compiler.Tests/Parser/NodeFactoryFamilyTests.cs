namespace TypeScript.Compiler.Tests;

/// <summary>
/// Acceptance for the hand-written half of the node factory (docs/architecture.md §15.8): the families ported after the
/// generated half was put under compilation. Each test pins a behaviour that the port had to decide rather than copy —
/// a truthiness, a defaulted token, a flag the parent aggregates.
/// </summary>
public class NodeFactoryFamilyTests
{
    private static NodeFactory Factory() => new();

    private static Identifier Id(string text) => new() { EscapedText = text };

    // ---------------------------------------------------------------------------------------------
    // Template literals
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// `getCookedText` re-scans the raw text through a scanner to get the cooked form; the port wires that scanner up
    /// itself, so this is the one place its work is visible.
    /// </summary>
    [Fact]
    public void TemplateHead_CooksTheRawTextWhenOnlyRawTextIsGiven()
    {
        var head = Factory().CreateTemplateHead(null, "\\n");

        Assert.Equal("\n", head.Text);
        Assert.Equal("\\n", head.RawText);
        Assert.Equal(SyntaxKind.TemplateHead, head.Kind);
    }

    [Fact]
    public void NoSubstitutionTemplateLiteral_KeepsThePieceKindsFlags()
    {
        var literal = Factory().CreateNoSubstitutionTemplateLiteral("a", "a", TokenFlags.ContainsInvalidEscape);

        Assert.Equal(SyntaxKind.NoSubstitutionTemplateLiteral, literal.Kind);
        Assert.Equal(TokenFlags.ContainsInvalidEscape, literal.TemplateFlags);
        Assert.Equal(
            TransformFlags.ContainsES2015 | TransformFlags.ContainsES2018,
            literal.TransformFlags);
    }

    // ---------------------------------------------------------------------------------------------
    // Expressions
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// TS writes `questionToken ?? createToken(SyntaxKind.QuestionToken)`, whose result is a `PunctuationToken`. The port
    /// names the token class directly, because its wide `CreateToken` answers `Node` (§16).
    /// </summary>
    [Fact]
    public void ConditionalExpression_FillsInTheTokensItWasNotGiven()
    {
        var node = Factory().CreateConditionalExpression(
            Id("condition"),
            null,
            Id("whenTrue"),
            null,
            Id("whenFalse"));

        Assert.Equal(SyntaxKind.QuestionToken, node.QuestionToken!.Kind);
        Assert.Equal(SyntaxKind.ColonToken, node.ColonToken!.Kind);
        Assert.IsType<PunctuationToken>(node.QuestionToken);
    }

    /// <summary>
    /// The flag is set only for a name that is neither generated nor "local" — the rule `visitPreOrPostfixUnaryExpression`
    /// in module.ts relies on.
    /// </summary>
    [Fact]
    public void PrefixUnaryExpression_MarksOnlyARealIdentifier()
    {
        var factory = Factory();

        var onIdentifier = factory.CreatePrefixUnaryExpression(SyntaxKind.PlusPlusToken, Id("x"));
        Assert.NotEqual(TransformFlags.None, onIdentifier.TransformFlags & TransformFlags.ContainsUpdateExpressionForIdentifier);

        var onLocalName = factory.CreatePrefixUnaryExpression(SyntaxKind.PlusPlusToken, Id("arguments"));
        Assert.Equal(TransformFlags.None, onLocalName.TransformFlags & TransformFlags.ContainsUpdateExpressionForIdentifier);

        var onOtherOperator = factory.CreatePrefixUnaryExpression(SyntaxKind.MinusToken, Id("x"));
        Assert.Equal(TransformFlags.None, onOtherOperator.TransformFlags & TransformFlags.ContainsUpdateExpressionForIdentifier);
    }

    [Fact]
    public void ArrayLiteralExpression_AddsATrailingCommaForATrailingOmittedElement()
    {
        var factory = Factory();
        var elements = new List<Expression> { Id("a"), new OmittedExpression() };

        var node = factory.CreateArrayLiteralExpression(elements, null);

        Assert.True(node.Elements!.HasTrailingComma);
    }

    /// <summary>
    /// `hasInvalidEscape` walks the template, and the head is one of the places the flag can sit.
    /// </summary>
    [Fact]
    public void TaggedTemplateExpression_ContainsES2018WhenTheTemplateHasAnInvalidEscape()
    {
        var factory = Factory();
        var template = new NoSubstitutionTemplateLiteral
        {
            Text = "a",
            TemplateFlags = TokenFlags.ContainsInvalidEscape,
        };

        var node = factory.CreateTaggedTemplateExpression(Id("tag"), null, template);

        Assert.NotEqual(TransformFlags.None, node.TransformFlags & TransformFlags.ContainsES2018);
        Assert.NotEqual(TransformFlags.None, node.TransformFlags & TransformFlags.ContainsES2015);
    }

    [Fact]
    public void SyntheticExpression_CarriesWhatTheCheckerAsksFor()
    {
        var node = Factory().CreateSyntheticExpression(null, isSpread: true);

        Assert.True(node.IsSpread);
        Assert.Equal(SyntaxKind.SyntheticExpression, node.Kind);
    }

    // ---------------------------------------------------------------------------------------------
    // Updating
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Every `update*` returns the node it was given when nothing changed — which is what makes the transform layer's
    /// structural sharing work.
    /// </summary>
    [Fact]
    public void Updates_ReturnTheSameNodeWhenNothingChanged()
    {
        var factory = Factory();
        var name = Id("x");
        var initializer = Id("y");
        var assignment = factory.CreatePropertyAssignment(name, initializer);

        Assert.Same(assignment, factory.UpdatePropertyAssignment(assignment, name, initializer));
        Assert.NotSame(assignment, factory.UpdatePropertyAssignment(assignment, Id("z"), initializer));
    }

    [Fact]
    public void Update_RecordsTheOriginalWhenSomethingChanged()
    {
        var factory = Factory();
        var statement = factory.CreateExpressionStatement(Id("a"));

        var updated = factory.UpdateExpressionStatement(statement, Id("b"));

        Assert.NotSame(statement, updated);
        Assert.Same(statement, updated.Original);
    }

    [Fact]
    public void UpdateJsxExpression_AcceptsTheTokenTheFieldHolds()
    {
        var factory = Factory();
        var expression = factory.CreateJsxExpression(null, Id("value"));

        Assert.Same(expression, factory.UpdateJsxExpression(expression, expression.Expression));
        Assert.NotSame(expression, factory.UpdateJsxExpression(expression, Id("other")));
    }

    // ---------------------------------------------------------------------------------------------
    // Declarations
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void HeritageClause_FlagsTheKeywordItWasGiven()
    {
        var factory = Factory();
        var types = new List<ExpressionWithTypeArguments> { factory.CreateExpressionWithTypeArguments(Id("A"), null) };

        var extends = factory.CreateHeritageClause(SyntaxKind.ExtendsKeyword, types);
        var implements = factory.CreateHeritageClause(SyntaxKind.ImplementsKeyword, types);

        Assert.NotEqual(TransformFlags.None, extends.TransformFlags & TransformFlags.ContainsES2015);
        Assert.Equal(TransformFlags.None, extends.TransformFlags & TransformFlags.ContainsTypeScript);
        Assert.NotEqual(TransformFlags.None, implements.TransformFlags & TransformFlags.ContainsTypeScript);
    }

    /// <summary>
    /// A class with an ambient modifier is TypeScript in its entirety; the port keeps TS's two branches.
    /// </summary>
    [Fact]
    public void ClassDeclaration_TreatsAnAmbientClassAsTypeScript()
    {
        var factory = Factory();
        var modifiers = new List<Node> { factory.CreateModifier(SyntaxKind.DeclareKeyword) };

        var ambient = factory.CreateClassDeclaration(modifiers, Id("C"), null, null, null);
        var plain = factory.CreateClassDeclaration(null, Id("C"), null, null, null);

        Assert.Equal(TransformFlags.ContainsTypeScript, ambient.TransformFlags & TransformFlags.ContainsTypeScript);
        Assert.Equal(TransformFlags.None, plain.TransformFlags & TransformFlags.ContainsTypeScript);
        Assert.NotEqual(TransformFlags.None, plain.TransformFlags & TransformFlags.ContainsES2015);
    }

    [Fact]
    public void ObjectBindingPattern_PropagatesRestOrSpreadFromItsChildren()
    {
        var factory = Factory();
        var element = new BindingElement
        {
            Name = Id("a"),
            TransformFlags = TransformFlags.ContainsRestOrSpread,
        };

        var node = factory.CreateObjectBindingPattern([element]);

        Assert.NotEqual(TransformFlags.None, node.TransformFlags & TransformFlags.ContainsObjectRestOrSpread);
        Assert.NotEqual(TransformFlags.None, node.TransformFlags & TransformFlags.ContainsES2018);
    }

    [Fact]
    public void SourceFile_StartsEmptyAndAggregatesItsChildren()
    {
        var factory = Factory();
        var statement = factory.CreateExpressionStatement(Id("a"));

        var sourceFile = factory.CreateSourceFile([statement], (Token)factory.CreateToken(SyntaxKind.EndOfFileToken), NodeFlags.None);

        Assert.Equal("", sourceFile.Text);
        Assert.Equal("", sourceFile.FileName);
        Assert.False(sourceFile.IsDeclarationFile);
        Assert.Single(sourceFile.Statements!);
        Assert.Equal(TransformFlags.None, sourceFile.TransformFlags & TransformFlags.ContainsYield);
    }

    /// <summary>
    /// The clone-based update: a changed statement list produces a *new* source file whose `original` is the old one.
    /// </summary>
    [Fact]
    public void UpdateSourceFile_ClonesAndRecordsTheOriginal()
    {
        var factory = Factory();
        var sourceFile = factory.CreateSourceFile([], (Token)factory.CreateToken(SyntaxKind.EndOfFileToken), NodeFlags.None);
        var statement = factory.CreateExpressionStatement(Id("a"));

        var updated = factory.UpdateSourceFile(sourceFile, [statement], null, null, null, null, null);

        Assert.NotSame(sourceFile, updated);
        Assert.Same(sourceFile, updated.Original);
        Assert.Single(updated.Statements!);
        Assert.Same(sourceFile, factory.UpdateSourceFile(updated, updated.Statements, null, null, null, null, null).Original);
    }

    [Fact]
    public void UseStrictPrologue_StartsOnANewLine()
    {
        var prologue = Factory().CreateUseStrictPrologue();

        Assert.Equal(SyntaxKind.ExpressionStatement, prologue.Kind);
        Assert.True(EmitNodes.GetStartsOnNewLine(prologue));
    }

    // ---------------------------------------------------------------------------------------------
    // JSDoc
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A JSDoc tag fills in its own default tag name when the caller gives none — `getDefaultTagNameForKind` is the table.
    /// </summary>
    [Fact]
    public void JSDocTags_FallBackToTheirDefaultName()
    {
        var factory = Factory();

        Assert.Equal("param", factory.CreateJSDocParameterTag(null, Id("x"), false).TagName!.EscapedText);
        Assert.Equal("prop", factory.CreateJSDocPropertyTag(null, Id("x"), false).TagName!.EscapedText);
        Assert.Equal("template", factory.CreateJSDocTemplateTag(null, null, null).TagName!.EscapedText);
        Assert.Equal("overload", factory.CreateJSDocOverloadTag(null, null).TagName!.EscapedText);
        Assert.Equal("import", factory.CreateJSDocImportTag(null, null, null, null).TagName!.EscapedText);
    }

    [Fact]
    public void JSDocUnknownTag_PinsTheJSDocTagKind()
    {
        var node = Factory().CreateJSDocUnknownTag(Id("mystery"));

        Assert.Equal(SyntaxKind.JSDocTag, node.Kind);
    }

    [Fact]
    public void JSDocParameterTag_KeepsItsTwoBooleans()
    {
        var node = Factory().CreateJSDocParameterTag(
            null,
            Id("[x]"),
            isBracketed: true,
            typeExpression: null,
            isNameFirst: true);

        Assert.True(node.IsBracketed);
        Assert.True(node.IsNameFirst);
    }

    /// <summary>
    /// A typedef's `name` is derived from its full name: an identifier is taken as it is, and a namespace declaration
    /// contributes its body.
    /// </summary>
    [Fact]
    public void JSDocTypedefTag_DerivesItsNameFromTheFullName()
    {
        var factory = Factory();
        var name = Id("Alias");

        var node = factory.CreateJSDocTypedefTag(null, null, name);
        Assert.Same(name, node.Name);

        var anonymous = factory.CreateJSDocTypedefTag(null, null, null);
        Assert.Null(anonymous.Name);
    }

    /// <summary>
    /// The update path defaults `tagName` to the node's default tag name, which is what a caller passing nothing means.
    /// </summary>
    [Fact]
    public void JSDocTagUpdate_KeepsTheNodeWhenOnlyTheDefaultNameWouldBeUsed()
    {
        var factory = Factory();
        var tag = factory.CreateJSDocEnumTag(null, null);

        Assert.Same(tag, factory.UpdateJSDocEnumTag(tag, null, tag.TypeExpression, null));
        Assert.NotSame(tag, factory.UpdateJSDocEnumTag(tag, Id("other"), tag.TypeExpression, null));
    }

    // ---------------------------------------------------------------------------------------------
    // The stubs
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The 11 members whose dependencies belong to later batches are generated as stubs. This is the promise §15.8 makes
    /// about them: a caller that reaches one fails loudly, rather than seeing a half-built node.
    /// </summary>
    [Fact]
    public void MembersWaitingOnLaterBatches_ThrowInsteadOfBuildingSomethingPartial()
    {
        var factory = Factory();

        Assert.Throws<NotImplementedException>(
            () => factory.CreateBinaryExpression(Id("a"), SyntaxKind.PlusToken, Id("b")));
        Assert.Throws<NotImplementedException>(
            () => factory.CreateTempVariable(null, null, null, null));
        Assert.Throws<NotImplementedException>(
            () => factory.CreateCallBinding(Id("a"), null, null, null));
    }
}
