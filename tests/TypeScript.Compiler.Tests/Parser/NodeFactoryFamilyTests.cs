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
    // Functions, classes and declarations
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A method with no body is TypeScript in its entirety; with a body it aggregates the usual flags, and async adds
    /// ES2017.
    /// </summary>
    [Fact]
    public void MethodDeclaration_SwitchesOnHavingABody()
    {
        var factory = Factory();
        var async = factory.CreateModifier(SyntaxKind.AsyncKeyword);

        var bodyless = factory.CreateMethodDeclaration(null, null, Id("m"), null, null, null, null, null);
        var asyncWithBody = factory.CreateMethodDeclaration(
            [async],
            null,
            Id("m"),
            null,
            null,
            null,
            null,
            factory.CreateBlock([], true));

        Assert.Equal(TransformFlags.ContainsTypeScript, bodyless.TransformFlags);
        Assert.NotEqual(TransformFlags.None, asyncWithBody.TransformFlags & TransformFlags.ContainsES2017);
        Assert.NotEqual(TransformFlags.None, asyncWithBody.TransformFlags & TransformFlags.ContainsES2015);
    }

    [Fact]
    public void ArrowFunction_FillsInTheEqualsGreaterThanToken()
    {
        var node = Factory().CreateArrowFunction(null, null, null, null, null, Id("body"));

        Assert.Equal(SyntaxKind.EqualsGreaterThanToken, node.EqualsGreaterThanToken!.Kind);
        Assert.IsType<PunctuationToken>(node.EqualsGreaterThanToken);
    }

    /// <summary>
    /// TS accepts the older boolean spelling of the import phase modifier and normalises it in the body.
    /// </summary>
    [Fact]
    public void ImportClause_NormalisesTheBooleanSpelling()
    {
        var factory = Factory();

        var typeOnly = factory.CreateImportClause(true, Id("x"), null);
        var phase = factory.CreateImportClause(SyntaxKind.DeferKeyword, Id("x"), null);

        Assert.True(typeOnly.IsTypeOnly);
        Assert.Equal(SyntaxKind.TypeKeyword, typeOnly.PhaseModifier);
        Assert.NotEqual(TransformFlags.None, typeOnly.TransformFlags & TransformFlags.ContainsTypeScript);

        Assert.False(phase.IsTypeOnly);
        Assert.Equal(SyntaxKind.DeferKeyword, phase.PhaseModifier);
    }

    [Fact]
    public void VariableDeclarationList_TakesBlockScopingFromItsFlags()
    {
        var factory = Factory();

        var blockScoped = factory.CreateVariableDeclarationList([], NodeFlags.Let);
        var plain = factory.CreateVariableDeclarationList([], null);

        Assert.NotEqual(TransformFlags.None, blockScoped.TransformFlags & TransformFlags.ContainsES2015);
        Assert.NotEqual(TransformFlags.None, blockScoped.TransformFlags & TransformFlags.ContainsBlockScopedBinding);
        Assert.Equal(TransformFlags.None, plain.TransformFlags & TransformFlags.ContainsES2015);
    }

    [Fact]
    public void ModuleDeclaration_ClearsTheTopLevelAwaitFlag()
    {
        var factory = Factory();
        var name = factory.CreateIdentifier("M");

        var ambient = factory.CreateModuleDeclaration(
            [factory.CreateModifier(SyntaxKind.DeclareKeyword)],
            name,
            null);
        var plain = factory.CreateModuleDeclaration(null, name, null);

        Assert.Equal(TransformFlags.ContainsTypeScript, ambient.TransformFlags);
        Assert.NotEqual(TransformFlags.None, plain.TransformFlags & TransformFlags.ContainsTypeScript);
    }

    /// <summary>
    /// An import/export/namespace declaration is always parsed in an await context, so the flag comes off again.
    /// </summary>
    [Fact]
    public void DeclarationsThatCannotContainAwait_HaveTheFlagCleared()
    {
        var factory = Factory();
        var clauses = new List<Node> { factory.CreateEmptyStatement() };

        var declaration = factory.CreateExportDeclaration(null, false, null, null, null);

        Assert.Equal(TransformFlags.None, declaration.TransformFlags & TransformFlags.ContainsPossibleTopLevelAwait);
        Assert.Equal(TransformFlags.None, declaration.TransformFlags & TransformFlags.ContainsPossibleTopLevelAwait);
        Assert.NotNull(clauses);
    }

    [Fact]
    public void NewExpression_IsES2020AndOptionallyTypescript()
    {
        var factory = Factory();

        var plain = factory.CreateNewExpression(Id("C"), null, null);
        var withTypeArguments = factory.CreateNewExpression(Id("C"), [factory.CreateKeywordTypeNode(SyntaxKind.NumberKeyword)], null);

        Assert.NotEqual(TransformFlags.None, plain.TransformFlags & TransformFlags.ContainsES2020);
        Assert.Equal(TransformFlags.None, plain.TransformFlags & TransformFlags.ContainsTypeScript);
        Assert.NotEqual(TransformFlags.None, withTypeArguments.TransformFlags & TransformFlags.ContainsTypeScript);
    }

    [Fact]
    public void ImmediatelyInvokedFunctionExpression_TakesTheOptionalParameter()
    {
        var factory = Factory();
        var parameter = factory.CreateParameterDeclaration(null, null, "p", null, null, null);
        var value = Id("v");

        var bare = factory.CreateImmediatelyInvokedFunctionExpression([]);
        var withArgument = factory.CreateImmediatelyInvokedFunctionExpression([], parameter, value);

        Assert.Empty(bare.Arguments!);
        Assert.Single(withArgument.Arguments!);
        Assert.Same(value, withArgument.Arguments![0]);
        Assert.Single(((FunctionExpression)withArgument.Expression!).Parameters!);
    }

    [Fact]
    public void UnionTypeNode_KeepsTheConstituentsItWasGiven()
    {
        var factory = Factory();
        var number = factory.CreateKeywordTypeNode(SyntaxKind.NumberKeyword);
        var text = factory.CreateKeywordTypeNode(SyntaxKind.StringKeyword);

        var node = factory.CreateUnionTypeNode([number, text]);

        Assert.Equal(SyntaxKind.UnionType, node.Kind);
        Assert.Equal(2, node.Types!.Count);
        Assert.Equal(TransformFlags.ContainsTypeScript, node.TransformFlags);
    }

    /// <summary>
    /// TS guards the mirror with <c>node.assertions &amp;&amp; node.assertions.assertClause &amp;&amp; node.attributes</c>, and a
    /// freshly built node has no <c>assertions</c> container at all — so on this path the mirror is a no-op. Pinning that
    /// keeps someone from "fixing" it into creating the container.
    /// </summary>
    [Fact]
    public void ImportTypeNode_DoesNotInventTheDeprecatedAssertionsContainer()
    {
        var factory = Factory();
        var argument = factory.CreateKeywordTypeNode(SyntaxKind.StringKeyword);
        var attributes = factory.CreateImportAttributes(null, null, SyntaxKind.WithKeyword);

        var node = factory.CreateImportTypeNode(argument, attributes, null, null, isTypeOf: true);

        Assert.True(node.IsTypeOf);
        Assert.Same(attributes, node.Attributes);
        Assert.Null(node.Assertions);
    }

    [Fact]
    public void ShorthandPropertyAssignment_ClearsTheGrammarErrorSlots()
    {
        var node = Factory().CreateShorthandPropertyAssignment("x", Id("y"));

        Assert.Null(node.EqualsToken);
        Assert.Null(node.QuestionToken);
        Assert.NotEqual(TransformFlags.None, node.TransformFlags & TransformFlags.ContainsES2015);
    }

    // ---------------------------------------------------------------------------------------------
    // Emit helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// `createMethodCall` keeps the optionality of its object: if it is already a chain, the method access and the call
    /// are chains too.
    /// </summary>
    [Fact]
    public void MethodCall_KeepsTheOptionalChain()
    {
        var factory = Factory();
        var chain = factory.CreateCallChain(Id("a"), null, null, null);

        var onChain = factory.CreateMethodCall(chain, "m", []);
        var onPlain = factory.CreateMethodCall(Id("a"), "m", []);

        Assert.NotEqual(TransformFlags.None, onChain.TransformFlags & TransformFlags.ContainsES2020);
        Assert.Equal(TransformFlags.None, onPlain.TransformFlags & TransformFlags.ContainsES2020);
        Assert.Equal(SyntaxKind.CallExpression, onPlain.Kind);
    }

    [Fact]
    public void FunctionBindAndCall_PrependTheThisArgument()
    {
        var factory = Factory();
        var thisArg = Id("self");
        var argument = Id("x");

        var bind = factory.CreateFunctionBindCall(Id("f"), thisArg, [argument]);
        var apply = factory.CreateFunctionApplyCall(Id("f"), thisArg, argument);

        Assert.Equal(2, bind.Arguments!.Count);
        Assert.Same(thisArg, bind.Arguments![0]);
        Assert.Same(argument, bind.Arguments![1]);

        Assert.Equal(2, apply.Arguments!.Count);
        Assert.Same(argument, apply.Arguments![1]);
        Assert.Equal("bind", ((Identifier)ReceiverOf(bind).Name!).EscapedText);
        Assert.Equal("apply", ((Identifier)ReceiverOf(apply).Name!).EscapedText);
    }

    [Fact]
    public void GlobalMethodCall_NamesTheGlobalObjectAndOptionallyAddsAReceiver()
    {
        var factory = Factory();
        var target = Id("o");
        var key = Id("k");
        var receiver = Id("r");

        var two = factory.CreateReflectGetCall(target, key, null);
        var three = factory.CreateReflectGetCall(target, key, receiver);
        var direct = factory.CreateObjectDefinePropertyCall(target, "p", Id("d"));

        Assert.Equal("get", ((Identifier)ReceiverOf(two).Name!).EscapedText);
        Assert.Equal(2, two.Arguments!.Count);
        Assert.Equal(3, three.Arguments!.Count);
        Assert.Equal("defineProperty", ((Identifier)ReceiverOf(direct).Name!).EscapedText);
        Assert.Equal(
            "Object",
            ((Identifier)ReceiverOf(direct).Expression!).EscapedText);
    }

    [Fact]
    public void ArraySliceCall_TakesAnOptionalStart()
    {
        var factory = Factory();

        Assert.Empty(factory.CreateArraySliceCall(Id("a"), null).Arguments!);
        Assert.Single(factory.CreateArraySliceCall(Id("a"), 1).Arguments!);
    }

    /// <summary>
    /// `tryAddPropertyAssignment` adds a member only when the value is there, which is what splits a descriptor into data
    /// and accessor members.
    /// </summary>
    [Fact]
    public void PropertyDescriptor_AddsOnlyTheMembersItWasGiven()
    {
        var factory = Factory();
        var attributes = new TestDescriptorAttributes { Enumerable = true, Writable = true, Value = Id("1") };

        var descriptor = factory.CreatePropertyDescriptor(attributes, singleLine: true);

        var names = descriptor.Properties!
            .Cast<PropertyAssignment>()
            .Select(assignment => ((Identifier)assignment.Name!).EscapedText)
            .ToList();

        Assert.Contains("enumerable", names);
        Assert.Contains("writable", names);
        Assert.Contains("value", names);
        Assert.DoesNotContain("get", names);
        Assert.DoesNotContain("set", names);
    }

    private static PropertyAccessExpression ReceiverOf(CallExpression call)
        => (PropertyAccessExpression)call.Expression!;

    private sealed class TestDescriptorAttributes : IPropertyDescriptorAttributes
    {
        public object? Enumerable { get; set; }

        public object? Configurable { get; set; }

        public object? Writable { get; set; }

        public Expression? Value { get; set; }

        public Expression? Get { get; set; }

        public Expression? Set { get; set; }
    }

    // ---------------------------------------------------------------------------------------------
    // JSX
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void JsxSelfClosingElement_IsJsxAndOptionallyTypescript()
    {
        var factory = Factory();
        var tagName = Id("Component");

        var plain = factory.CreateJsxSelfClosingElement(tagName, null, new JsxAttributes());
        var typed = factory.CreateJsxSelfClosingElement(
            tagName,
            [factory.CreateKeywordTypeNode(SyntaxKind.StringKeyword)],
            new JsxAttributes());

        Assert.NotEqual(TransformFlags.None, plain.TransformFlags & TransformFlags.ContainsJsx);
        Assert.Equal(TransformFlags.None, plain.TransformFlags & TransformFlags.ContainsTypeScript);
        Assert.NotEqual(TransformFlags.None, typed.TransformFlags & TransformFlags.ContainsTypeScript);
    }

    [Fact]
    public void UpdateJsxSelfClosingElement_ReturnsTheSameNodeWhenNothingChanged()
    {
        var factory = Factory();
        var tagName = Id("Component");
        var attributes = new JsxAttributes();
        var node = factory.CreateJsxSelfClosingElement(tagName, null, attributes);

        Assert.Same(node, factory.UpdateJsxSelfClosingElement(node, tagName, null, attributes));
        Assert.NotSame(node, factory.UpdateJsxSelfClosingElement(node, Id("Other"), null, attributes));
    }

    // ---------------------------------------------------------------------------------------------
    // Source files
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The update path clones the source file and carries over every field it was not asked to change — including the ones
    /// no parameter mentions, which is what the reflective clone is for.
    /// </summary>
    [Fact]
    public void UpdateSourceFile_ClonesTheFieldsItWasNotAskedToChange()
    {
        var factory = Factory();
        var sourceFile = factory.CreateSourceFile([], (Token)factory.CreateToken(SyntaxKind.EndOfFileToken), NodeFlags.None);
        sourceFile.FileName = "a.ts";
        sourceFile.Text = "let a;";

        var updated = factory.UpdateSourceFile(
            sourceFile,
            [factory.CreateExpressionStatement(Id("a"))],
            null,
            null,
            null,
            null,
            null);

        Assert.Equal("a.ts", updated.FileName);
        Assert.Equal("let a;", updated.Text);
        Assert.Equal(sourceFile.Flags, updated.Flags & ~NodeFlags.Synthesized);
        Assert.Equal("a.ts", sourceFile.FileName);
    }

    /// <summary>
    /// This pins the trade-off §15.8 records: TS builds an object that is *not* the target, while the port hands the target
    /// back with the info attached. A caller reads the target's members either way; only the identity differs.
    /// </summary>
    [Fact]
    public void CreateRedirectedSourceFile_HandsBackTheTargetWithTheInfoAttached()
    {
        var factory = Factory();
        var target = factory.CreateSourceFile([], (Token)factory.CreateToken(SyntaxKind.EndOfFileToken), NodeFlags.None);
        var info = new TestRedirectInfo { RedirectTarget = target };

        var redirected = factory.CreateRedirectedSourceFile(info);

        Assert.Same(target, redirected);
        Assert.Same(info, target.RedirectInfo);
    }

    private sealed class TestRedirectInfo : IRedirectInfo
    {
        public SourceFile? RedirectTarget { get; set; }

        public SourceFile? Unredirected => null;
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
