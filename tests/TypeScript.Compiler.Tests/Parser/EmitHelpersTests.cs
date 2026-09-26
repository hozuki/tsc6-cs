namespace TypeScript.Compiler.Tests;

/// <summary>
/// <c>factory/emitHelpers.ts</c>: the descriptor each member of the emit-helper factory asks the context for, the call
/// it builds around it, and the two switches an option decides.
/// </summary>
/// <remarks>
/// PORT-NOTE: the expected values are transcribed from the reference's own answers, not from the port's source. The
/// descriptors were read off the objects <c>lib/typescript.js</c>'s <c>createEmitHelperFactory</c> passes to
/// <c>requestEmitHelper</c> (name, importName, scoped, priority, dependencies, text), and the call shapes and the two
/// option-selected texts off the expressions it returned, under <c>target: ES5</c> / <c>ES2015</c> / unset and
/// <c>jsx: Preserve</c> / <c>React</c> / unset. A wrong read of either option shows up as a changed argument list or a
/// changed text rather than as a compile error, which is why those two helpers' texts are pinned in full.
/// </remarks>
public class EmitHelpersTests
{
    /// <summary>The <c>context</c> slice the factory reads: a factory, options, and the log of requested helpers.</summary>
    private sealed class FakeContext : IEmitHelperFactoryContext
    {
        public FakeContext(ICompilerOptions? options = null) => Options = options ?? new FakeCompilerOptions();

        public NodeFactory Factory { get; } = new();

        public ICompilerOptions Options { get; }

        public List<Node> Requested { get; } = [];

        public ICompilerOptions GetCompilerOptions() => Options;

        public void RequestEmitHelper(Node? helper) => Requested.Add(helper!);
    }

    private static (EmitHelperFactory Factory, NodeFactory Nodes, FakeContext Context) NewFactory(ICompilerOptions? options = null)
    {
        var context = new FakeContext(options);
        return (EmitHelpers.CreateEmitHelperFactory(context), context.Factory, context);
    }

    /// <summary>The one helper a member asked for, checked to be the named one.</summary>
    private static EmitHelper Requested(FakeContext context, string name)
    {
        var helper = Assert.IsType<EmitHelper>(Assert.Single(context.Requested));
        Assert.Equal(name, helper.Name);
        return helper;
    }

    /// <summary>The helper names a member asked for, in request order.</summary>
    private static string[] RequestedNames(FakeContext context)
        => context.Requested.Cast<EmitHelper>().Select(helper => helper.Name!).ToArray();

    /// <summary>The call a member built, with the helper name the callee has to carry.</summary>
    /// <remarks>
    /// PORT-NOTE: the name checked here is the <em>escaped</em> one — <c>createIdentifier("__rest")</c> escapes its
    /// leading underscores to <c>"___rest"</c> precisely so no user identifier can collide with a helper name — which is
    /// also what the reference's <c>escapedText</c> holds.
    /// </remarks>
    private static CallExpression HelperCall(Node node, string escapedName)
    {
        var call = Assert.IsType<CallExpression>(node);
        var callee = Assert.IsType<Identifier>(call.Expression);
        Assert.Equal(escapedName, callee.EscapedText);
        Assert.Equal(EmitFlags.HelperName | EmitFlags.AdviseOnEmitNode, Utilities.GetEmitFlags(callee));
        // Every member passes `/*typeArguments*/ undefined`, so the slot stays absent rather than becoming an empty list.
        Assert.Null(call.TypeArguments);
        return call;
    }

    /// <summary>The <c>"get"</c>/<c>"set"</c>/<c>"has"</c> member of an ES decorator access object.</summary>
    private static ArrowFunction AccessMethod(Expression? accessObject, string name)
    {
        var property = Assert.IsType<ObjectLiteralExpression>(accessObject)
            .Properties!
            .OfType<PropertyAssignment>()
            .Single(p => ((Identifier)p.Name!).EscapedText == name);
        return Assert.IsType<ArrowFunction>(property.Initializer);
    }

    /// <summary>The initializer of a named property of an object literal.</summary>
    private static Expression? PropertyValue(Expression? objectLiteral, string name)
        => Assert.IsType<ObjectLiteralExpression>(objectLiteral)
            .Properties!
            .OfType<PropertyAssignment>()
            .Single(p => ((Identifier)p.Name!).EscapedText == name)
            .Initializer;

    /// <summary>The property names of an object literal, in order.</summary>
    private static string[] PropertyNames(Expression? objectLiteral)
        => Assert.IsType<ObjectLiteralExpression>(objectLiteral)
            .Properties!
            .OfType<PropertyAssignment>()
            .Select(p => ((Identifier)p.Name!).EscapedText!)
            .ToArray();

    // ---------------------------------------------------------------------------------------------
    // TypeScript helpers
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void GetUnscopedHelperName_MarksTheIdentifierAsAHelperName()
    {
        var (factory, _, _) = NewFactory();

        var name = factory.GetUnscopedHelperName("__decorate");

        // The escape of leading underscores is what keeps a helper name from colliding with a user identifier.
        Assert.Equal("___decorate", name.EscapedText);
        Assert.Equal(EmitFlags.HelperName | EmitFlags.AdviseOnEmitNode, Utilities.GetEmitFlags(name));
    }

    [Fact]
    public void CreateDecorateHelper_KeepsTheDescriptorOnlyWithAMemberName()
    {
        var (factory, nodes, context) = NewFactory();
        var d1 = nodes.CreateIdentifier("d1");
        var d2 = nodes.CreateIdentifier("d2");
        var target = nodes.CreateIdentifier("target");
        var descriptor = nodes.CreateIdentifier("desc");

        var withMember = HelperCall(
            factory.CreateDecorateHelper([d1, d2], target, nodes.CreateStringLiteral("member"), descriptor),
            "___decorate");
        Assert.Equal(4, withMember.Arguments!.Count);
        var decorators = Assert.IsType<ArrayLiteralExpression>(withMember.Arguments[0]);
        Assert.True(decorators.MultiLine);
        Assert.Equal(new Expression[] { d1, d2 }, decorators.Elements!);
        Assert.Same(target, withMember.Arguments[1]);

        // Without a member name there is no fourth argument, and the descriptor is dropped with it rather than
        // passed as a hole.
        context.Requested.Clear();
        var withoutMember = HelperCall(factory.CreateDecorateHelper([d1], target, null, descriptor), "___decorate");
        Assert.Equal(2, withoutMember.Arguments!.Count);
        Assert.Same(target, withoutMember.Arguments[1]);

        Requested(context, "typescript:decorate");
    }

    [Fact]
    public void CreateMetadataHelper_BuildsTheMetadataCall()
    {
        var (factory, nodes, context) = NewFactory();

        var call = HelperCall(factory.CreateMetadataHelper("design:type", nodes.CreateIdentifier("v")), "___metadata");

        Assert.Equal(2, call.Arguments!.Count);
        Assert.Equal("design:type", Assert.IsType<StringLiteral>(call.Arguments[0]).Text);
        Requested(context, "typescript:metadata");
    }

    [Fact]
    public void CreateParamHelper_LowersTheOffsetToALiteral()
    {
        var (factory, nodes, context) = NewFactory();

        var call = HelperCall(factory.CreateParamHelper(nodes.CreateIdentifier("p"), 3), "___param");

        Assert.Equal(2, call.Arguments!.Count);
        Assert.Equal("3", Assert.IsType<NumericLiteral>(call.Arguments[0]).Text);
        Requested(context, "typescript:param");
    }

    // ---------------------------------------------------------------------------------------------
    // ES Decorators helpers
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void CreateESDecorateHelper_BuildsTheClassContextObject()
    {
        var (factory, nodes, context) = NewFactory();
        var name = nodes.CreateIdentifier("classNameReference");
        var metadata = nodes.CreateIdentifier("metaReference");

        var call = HelperCall(
            factory.CreateESDecorateHelper(
                nodes.CreateNull(),
                nodes.CreateIdentifier("descIn"),
                nodes.CreateIdentifier("decorators"),
                new ESDecorateClassContext(name, metadata),
                nodes.CreateIdentifier("initializers"),
                nodes.CreateIdentifier("extraInitializers")),
            "___esDecorate");

        Assert.Equal(6, call.Arguments!.Count);
        var contextObject = call.Arguments[3];
        Assert.Equal(new[] { "kind", "name", "metadata" }, PropertyNames(contextObject));
        Assert.Equal("class", Assert.IsType<StringLiteral>(PropertyValue(contextObject, "kind")).Text);
        Assert.Same(name, PropertyValue(contextObject, "name"));
        Assert.Same(metadata, PropertyValue(contextObject, "metadata"));
        Requested(context, "typescript:esDecorate");
    }

    [Fact]
    public void CreateESDecorateHelper_BuildsTheElementContextObjectFromTheAccessFlags()
    {
        var (factory, nodes, context) = NewFactory();
        var name = new ESDecorateName(computed: false, nodes.CreateIdentifier("m"));
        var metadata = nodes.CreateIdentifier("metaReference");

        Expression? ElementContext(bool? get, bool? set)
            => HelperCall(
                factory.CreateESDecorateHelper(
                    nodes.CreateThis(),
                    nodes.CreateNull(),
                    nodes.CreateIdentifier("decorators"),
                    new ESDecorateClassElementContext("method", name, @static: true, @private: false, new ESDecorateClassElementAccess(get, set), metadata),
                    nodes.CreateNull(),
                    nodes.CreateIdentifier("extraInitializers")),
                "___esDecorate").Arguments![3];

        var getOnly = ElementContext(get: true, set: false);
        Assert.Equal(new[] { "kind", "name", "static", "private", "access", "metadata" }, PropertyNames(getOnly));
        Assert.Equal("method", Assert.IsType<StringLiteral>(PropertyValue(getOnly, "kind")).Text);
        Assert.Equal(SyntaxKind.TrueKeyword, PropertyValue(getOnly, "static")!.Kind);
        Assert.Equal(SyntaxKind.FalseKeyword, PropertyValue(getOnly, "private")!.Kind);
        Assert.Same(metadata, PropertyValue(getOnly, "metadata"));

        // `has` is always there; `get`/`set` follow the access flags, in that order.
        Assert.Equal(new[] { "has", "get" }, PropertyNames(PropertyValue(getOnly, "access")));
        Assert.Equal(new[] { "has", "get", "set" }, PropertyNames(PropertyValue(ElementContext(get: true, set: true), "access")));
        Assert.Equal(new[] { "has" }, PropertyNames(PropertyValue(ElementContext(get: null, set: null), "access")));

        Assert.Equal(new[] { "typescript:esDecorate" }, RequestedNames(context).Distinct().ToArray());
    }

    [Fact]
    public void CreateESDecorateHelper_QuotesTheElementNameUnlessItIsComputed()
    {
        var (factory, nodes, _) = NewFactory();

        Expression? ElementContext(ESDecorateName name, bool? get = true, bool? set = true)
            => HelperCall(
                factory.CreateESDecorateHelper(
                    nodes.CreateNull(),
                    nodes.CreateNull(),
                    nodes.CreateIdentifier("decorators"),
                    new ESDecorateClassElementContext("field", name, @static: false, @private: false, new ESDecorateClassElementAccess(get, set), nodes.CreateIdentifier("metaReference")),
                    nodes.CreateNull(),
                    nodes.CreateNull()),
                "___esDecorate").Arguments![3];

        // A non-computed identifier is quoted in both the context object and the `has` method.
        var identifier = nodes.CreateIdentifier("m");
        var quoted = ElementContext(new ESDecorateName(computed: false, identifier));
        Assert.Equal("m", Assert.IsType<StringLiteral>(PropertyValue(quoted, "name")).Text);
        var quotedHas = ((BinaryExpression)AccessMethod(PropertyValue(quoted, "access"), "has").Body!).Left;
        Assert.Equal("m", Assert.IsType<StringLiteral>(quotedHas).Text);

        // A computed name is quoted nowhere: the context object and `has` both take the expression itself.
        var computed = nodes.CreateIdentifier("computedName");
        var kept = ElementContext(new ESDecorateName(computed: true, computed));
        Assert.Same(computed, PropertyValue(kept, "name"));
        var computedHas = ((BinaryExpression)AccessMethod(PropertyValue(kept, "access"), "has").Body!).Left;
        Assert.Same(computed, computedHas);
        // The accessor methods are the ones that turn a computed name into `obj[<name>]`.
        Assert.IsType<ElementAccessExpression>(AccessMethod(PropertyValue(kept, "access"), "get").Body);

        // A `#private` name is not an `Identifier`, so `has` tests the PrivateIdentifier itself (`#p in obj`),
        // which is exactly what the reference does — `createStringLiteralFromNode` is only for identifiers.
        var asPrivate = ElementContext(new ESDecorateName(computed: false, nodes.CreatePrivateIdentifier("#p")));
        var privateHas = ((BinaryExpression)AccessMethod(PropertyValue(asPrivate, "access"), "has").Body!).Left;
        Assert.Equal("#p", Assert.IsType<PrivateIdentifier>(privateHas).EscapedText);

        // `get` reads `obj.<name>` (or `obj[<name>]`) and `set` assigns to the same place.
        var getter = AccessMethod(PropertyValue(quoted, "access"), "get");
        Assert.IsType<PropertyAccessExpression>(getter.Body);
        var setter = AccessMethod(PropertyValue(quoted, "access"), "set");
        var block = Assert.IsType<Block>(setter.Body);
        Assert.Equal(2, setter.Parameters!.Count);
        var assignment = Assert.IsType<BinaryExpression>(
            Assert.IsType<ExpressionStatement>(block.Statements![0]).Expression);
        Assert.Equal("m", ((Identifier)((PropertyAccessExpression)assignment.Left!).Name!).EscapedText);
        Assert.Equal("value", ((Identifier)assignment.Right!).EscapedText);
    }

    [Fact]
    public void CreateRunInitializersHelper_PassesTheValueOnlyWhenThereIsOne()
    {
        var (factory, nodes, context) = NewFactory();

        var withoutValue = HelperCall(factory.CreateRunInitializersHelper(nodes.CreateIdentifier("thisArg"), nodes.CreateIdentifier("initializers"), null), "___runInitializers");
        Assert.Equal(2, withoutValue.Arguments!.Count);

        var withValue = HelperCall(factory.CreateRunInitializersHelper(nodes.CreateIdentifier("thisArg"), nodes.CreateIdentifier("initializers"), nodes.CreateNumericLiteral(0)), "___runInitializers");
        Assert.Equal(3, withValue.Arguments!.Count);
        Assert.Equal("0", Assert.IsType<NumericLiteral>(withValue.Arguments![2]).Text);

        Assert.Equal(new[] { "typescript:runInitializers" }, RequestedNames(context).Distinct().ToArray());
    }

    // ---------------------------------------------------------------------------------------------
    // ES2018 helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from the reference's <c>__assign</c> text, read off <c>lib/typescript.js</c>.</summary>
    private const string AssignHelperText = @"
            var __assign = (this && this.__assign) || function () {
                __assign = Object.assign || function(t) {
                    for (var s, i = 1, n = arguments.length; i < n; i++) {
                        s = arguments[i];
                        for (var p in s) if (Object.prototype.hasOwnProperty.call(s, p))
                            t[p] = s[p];
                    }
                    return t;
                };
                return __assign.apply(this, arguments);
            };";

    [Fact]
    public void CreateAssignHelper_UsesObjectAssignFromES2015UpAndTheHelperBelow()
    {
        // The reference reads `getEmitScriptTarget(options) >= ES2015`, so ES2015 and every later target — and an
        // unset target, which defaults to the latest standard — call `Object.assign` and request no helper at all.
        foreach (var target in new ScriptTarget?[] { null, ScriptTarget.ES2015, ScriptTarget.ESNext })
        {
            var (factory, nodes, context) = NewFactory(new FakeCompilerOptions { Target = target });
            var attributes = new List<Expression> { nodes.CreateIdentifier("a"), nodes.CreateIdentifier("b") };

            var call = Assert.IsType<CallExpression>(factory.CreateAssignHelper(attributes));

            var property = Assert.IsType<PropertyAccessExpression>(call.Expression);
            Assert.Equal("Object", ((Identifier)property.Expression!).EscapedText);
            Assert.Equal("assign", ((Identifier)property.Name!).EscapedText);
            Assert.Equal(attributes, call.Arguments!);
            Assert.Empty(context.Requested);
        }

        // Below ES2015 the helper is pulled in instead, and this is its text.
        var (es5Factory, es5Nodes, es5Context) = NewFactory(new FakeCompilerOptions { Target = ScriptTarget.ES5 });
        var es5Attributes = new List<Expression> { es5Nodes.CreateIdentifier("a") };
        var es5Call = HelperCall(es5Factory.CreateAssignHelper(es5Attributes), "___assign");
        Assert.Equal(es5Attributes, es5Call.Arguments!);
        Assert.Equal(AssignHelperText, Requested(es5Context, "typescript:assign").Text);
    }

    [Fact]
    public void CreateAwaitHelper_AndTheAsyncFamily_RequestTheHelpersTheyReference()
    {
        var (factory, nodes, context) = NewFactory();

        var awaited = HelperCall(factory.CreateAwaitHelper(nodes.CreateIdentifier("e")), "___await");
        Assert.Single(awaited.Arguments!);
        Requested(context, "typescript:await");

        context.Requested.Clear();
        var delegator = HelperCall(factory.CreateAsyncDelegatorHelper(nodes.CreateIdentifier("e")), "___asyncDelegator");
        Assert.Single(delegator.Arguments!);
        Assert.Equal(new[] { "typescript:await", "typescript:asyncDelegator" }, RequestedNames(context));
        Assert.Equal("__asyncDelegator", Assert.IsType<EmitHelper>(context.Requested[1]).ImportName);

        context.Requested.Clear();
        var values = HelperCall(factory.CreateAsyncValuesHelper(nodes.CreateIdentifier("e")), "___asyncValues");
        Assert.Single(values.Arguments!);
        Requested(context, "typescript:asyncValues");
    }

    [Fact]
    public void CreateAsyncGeneratorHelper_MarksTheGeneratorBodyAndPassesThisForLexicalThis()
    {
        var (factory, nodes, context) = NewFactory();
        var generator = nodes.CreateFunctionExpression(null, null, null, null, [], null, nodes.CreateBlock([], null));

        var call = HelperCall(factory.CreateAsyncGeneratorHelper(generator, hasLexicalThis: true), "___asyncGenerator");

        Assert.Equal(3, call.Arguments!.Count);
        Assert.Equal(SyntaxKind.ThisKeyword, call.Arguments[0].Kind);
        Assert.Equal("arguments", ((Identifier)call.Arguments[1]).EscapedText);
        Assert.Same(generator, call.Arguments[2]);

        // The body is marked as an async function body that reuses the enclosing temp-variable scope.
        Assert.Equal(EmitFlags.AsyncFunctionBody | EmitFlags.ReuseTempVariableScope, generator.EmitNode!.Flags);
        Assert.Equal(new[] { "typescript:await", "typescript:asyncGenerator" }, RequestedNames(context));

        // Without a lexical `this` the first argument is `void 0`.
        var (voidFactory, voidNodes, _) = NewFactory();
        var voidGenerator = voidNodes.CreateFunctionExpression(null, null, null, null, [], null, voidNodes.CreateBlock([], null));
        var voidCall = HelperCall(voidFactory.CreateAsyncGeneratorHelper(voidGenerator, hasLexicalThis: false), "___asyncGenerator");
        Assert.Equal(SyntaxKind.VoidExpression, voidCall.Arguments![0].Kind);
    }

    [Fact]
    public void CreateRestHelper_QuotesThePropertyNamesAndKeepsTheComputedTemps()
    {
        var (factory, nodes, context) = NewFactory();
        var location = new TextRange { Pos = 5, End = 20 };

        var call = HelperCall(
            factory.CreateRestHelper(
                nodes.CreateIdentifier("o"),
                [
                    nodes.CreateBindingElement(null, nodes.CreateIdentifier("a"), nodes.CreateIdentifier("b"), null),
                    nodes.CreateBindingElement(null, nodes.CreateStringLiteral("c"), nodes.CreateIdentifier("d"), null),
                    nodes.CreateSpreadElement(nodes.CreateIdentifier("p")),
                ],
                computedTempVariables: null,
                location),
            "___rest");

        Assert.Equal(2, call.Arguments!.Count);
        var names = Assert.IsType<ArrayLiteralExpression>(call.Arguments[1]);
        Assert.Equal(new[] { "a", "c" }, names.Elements!.Select(e => Assert.IsType<StringLiteral>(e).Text).ToArray());
        // The array carries the pattern's range, which is what the reference's `setTextRange` does.
        Assert.Equal(5, names.Pos);
        Assert.Equal(20, names.End);
        Assert.Equal(new[] { "typescript:rest" }, RequestedNames(context).Distinct().ToArray());

        // A computed name goes through the temp the transformer handed over, guarded by a `typeof` test.
        var computedCall = HelperCall(
            factory.CreateRestHelper(
                nodes.CreateIdentifier("o"),
                [
                    nodes.CreateBindingElement(null, nodes.CreateComputedPropertyName(nodes.CreateIdentifier("k")), nodes.CreateIdentifier("a"), null),
                    nodes.CreateSpreadElement(nodes.CreateIdentifier("p")),
                ],
                [nodes.CreateIdentifier("temp")],
                location),
            "___rest");
        var conditional = Assert.IsType<ConditionalExpression>(
            Assert.IsType<ArrayLiteralExpression>(computedCall.Arguments![1]).Elements![0]);
        var condition = Assert.IsType<BinaryExpression>(conditional.Condition);
        Assert.Equal("symbol", Assert.IsType<StringLiteral>(condition.Right!).Text);
        Assert.Equal("temp", ((Identifier)conditional.WhenTrue!).EscapedText);
        Assert.Same(conditional.WhenTrue, ((BinaryExpression)conditional.WhenFalse!).Left);
    }

    // ---------------------------------------------------------------------------------------------
    // ES2017 helpers
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void CreateAwaiterHelper_BuildsTheGeneratorAndFallsBackToVoidZero()
    {
        var (factory, nodes, context) = NewFactory();
        var body = nodes.CreateBlock([], null);

        var call = HelperCall(
            factory.CreateAwaiterHelper(hasLexicalThis: true, nodes.CreateIdentifier("arguments"), nodes.CreateIdentifier("P"), null, body),
            "___awaiter");

        Assert.Equal(4, call.Arguments!.Count);
        Assert.Equal(SyntaxKind.ThisKeyword, call.Arguments[0].Kind);
        Assert.Equal("arguments", ((Identifier)call.Arguments[1]).EscapedText);
        Assert.Equal("P", ((Identifier)call.Arguments[2]).EscapedText);
        var generator = Assert.IsType<FunctionExpression>(call.Arguments[3]);
        Assert.Equal(SyntaxKind.AsteriskToken, generator.AsteriskToken!.Kind);
        Assert.Same(body, generator.Body);
        Assert.Equal(EmitFlags.AsyncFunctionBody | EmitFlags.ReuseTempVariableScope, generator.EmitNode!.Flags);
        Requested(context, "typescript:awaiter");

        // No `this`, no `arguments` and no promise constructor: three `void 0`s and an empty parameter list.
        var (bareFactory, bareNodes, _) = NewFactory();
        var bare = HelperCall(bareFactory.CreateAwaiterHelper(hasLexicalThis: false, null, null, null, bareNodes.CreateBlock([], null)), "___awaiter");
        Assert.Equal(4, bare.Arguments!.Count);
        Assert.Equal(SyntaxKind.VoidExpression, bare.Arguments[0].Kind);
        Assert.Equal(SyntaxKind.VoidExpression, bare.Arguments[1].Kind);
        Assert.Equal(SyntaxKind.VoidExpression, bare.Arguments[2].Kind);
        Assert.Empty(Assert.IsType<FunctionExpression>(bare.Arguments[3]).Parameters!);
    }

    // ---------------------------------------------------------------------------------------------
    // ES2015 helpers
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void CreateExtendsHelper_PassesAFileLevelSuperName()
    {
        var (factory, nodes, context) = NewFactory();

        var call = HelperCall(factory.CreateExtendsHelper(nodes.CreateIdentifier("C")), "___extends");

        Assert.Equal(2, call.Arguments!.Count);
        var superName = Assert.IsType<Identifier>(call.Arguments[1]);
        Assert.Equal("_super", superName.EscapedText);
        var autoGenerate = EmitNodes.GetIdentifierAutoGenerate(superName)!;
        Assert.Equal(
            GeneratedIdentifierFlags.Unique | GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel,
            autoGenerate.Flags);
        Requested(context, "typescript:extends");
    }

    [Fact]
    public void CreateTemplateObjectHelper_PassesBothArrays()
    {
        var (factory, nodes, context) = NewFactory();
        var cooked = nodes.CreateArrayLiteralExpression([nodes.CreateStringLiteral("a")], null);
        var raw = nodes.CreateArrayLiteralExpression([nodes.CreateStringLiteral("a")], null);

        var call = HelperCall(factory.CreateTemplateObjectHelper(cooked, raw), "___makeTemplateObject");

        Assert.Equal(new Expression[] { cooked, raw }, call.Arguments!);
        Requested(context, "typescript:makeTemplateObject");
    }

    [Fact]
    public void CreateSpreadArrayHelper_MarksThePackArgumentImmutable()
    {
        var (factory, nodes, context) = NewFactory();
        var to = nodes.CreateIdentifier("to");
        var from = nodes.CreateIdentifier("from");

        var packing = HelperCall(factory.CreateSpreadArrayHelper(to, from, packFrom: true), "___spreadArray");
        var packTrue = Assert.IsType<TrueLiteral>(packing.Arguments![2]);
        Assert.Equal(InternalEmitFlags.Immutable, packTrue.EmitNode!.InternalFlags);

        // The two literals are memoized per factory, so every call passes the same object.
        var again = HelperCall(factory.CreateSpreadArrayHelper(to, from, packFrom: true), "___spreadArray");
        Assert.Same(packTrue, again.Arguments![2]);

        var nonPacking = HelperCall(factory.CreateSpreadArrayHelper(to, from, packFrom: false), "___spreadArray");
        var packFalse = Assert.IsType<FalseLiteral>(nonPacking.Arguments![2]);
        Assert.Equal(InternalEmitFlags.Immutable, packFalse.EmitNode!.InternalFlags);
        Assert.NotSame(packTrue, packFalse);
        Assert.Equal(new[] { "typescript:spreadArray" }, RequestedNames(context).Distinct().ToArray());
    }

    [Fact]
    public void CreatePropKeyHelper_PassesTheExpression()
    {
        var (factory, nodes, context) = NewFactory();
        var expression = nodes.CreateIdentifier("expression");

        var call = HelperCall(factory.CreatePropKeyHelper(expression), "___propKey");

        Assert.Same(expression, Assert.Single(call.Arguments!));
        Requested(context, "typescript:propKey");
    }

    [Fact]
    public void CreateSetFunctionNameHelper_AddsThePrefixOnlyWhenThereIsOne()
    {
        var (factory, nodes, context) = NewFactory();

        var bare = HelperCall(factory.CreateSetFunctionNameHelper(nodes.CreateIdentifier("f"), nodes.CreateStringLiteral("name"), null), "___setFunctionName");
        Assert.Equal(2, bare.Arguments!.Count);

        var prefixed = HelperCall(factory.CreateSetFunctionNameHelper(nodes.CreateIdentifier("f"), nodes.CreateStringLiteral("name"), "get"), "___setFunctionName");
        Assert.Equal(3, prefixed.Arguments!.Count);
        Assert.Equal("get", Assert.IsType<StringLiteral>(prefixed.Arguments![2]).Text);

        Assert.Equal(new[] { "typescript:setFunctionName" }, RequestedNames(context).Distinct().ToArray());
    }

    // ---------------------------------------------------------------------------------------------
    // ES2015 destructuring and generator helpers
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void CreateValuesHelper_PassesTheExpression()
    {
        var (factory, nodes, context) = NewFactory();

        var call = HelperCall(factory.CreateValuesHelper(nodes.CreateIdentifier("e")), "___values");

        Assert.Single(call.Arguments!);
        Requested(context, "typescript:values");
    }

    [Fact]
    public void CreateReadHelper_CountsOnlyWhenACountIsGiven()
    {
        var (factory, nodes, context) = NewFactory();

        var counted = HelperCall(factory.CreateReadHelper(nodes.CreateIdentifier("iter"), 2), "___read");
        Assert.Equal("2", Assert.IsType<NumericLiteral>(counted.Arguments![1]).Text);

        var open = HelperCall(factory.CreateReadHelper(nodes.CreateIdentifier("iter"), null), "___read");
        Assert.Single(open.Arguments!);

        Assert.Equal(new[] { "typescript:read" }, RequestedNames(context).Distinct().ToArray());
    }

    [Fact]
    public void CreateGeneratorHelper_PassesThisAndTheBody()
    {
        var (factory, nodes, context) = NewFactory();
        var body = nodes.CreateFunctionExpression(null, null, null, null, [], null, nodes.CreateBlock([], null));

        var call = HelperCall(factory.CreateGeneratorHelper(body), "___generator");

        Assert.Equal(SyntaxKind.ThisKeyword, call.Arguments![0].Kind);
        Assert.Same(body, call.Arguments[1]);
        Requested(context, "typescript:generator");
    }

    // ---------------------------------------------------------------------------------------------
    // ES module helpers
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void CreateImportStarHelper_AndItsCallback_BothRequestTheSameHelper()
    {
        var (factory, nodes, context) = NewFactory();

        var call = HelperCall(factory.CreateImportStarHelper(nodes.CreateIdentifier("module")), "___importStar");
        Assert.Single(call.Arguments!);
        var helper = Requested(context, "typescript:commonjsimportstar");
        Assert.Equal("__importStar", helper.ImportName);

        // The callback form hands the same helper name out as a bare identifier rather than calling it.
        context.Requested.Clear();
        var callback = factory.CreateImportStarCallbackHelper();
        Assert.Equal("___importStar", Assert.IsType<Identifier>(callback).EscapedText);
        Assert.Equal(EmitFlags.HelperName | EmitFlags.AdviseOnEmitNode, Utilities.GetEmitFlags(callback));
        Assert.Same(helper, Requested(context, "typescript:commonjsimportstar"));
    }

    [Fact]
    public void CreateImportDefaultHelper_AndExportStarHelper_BuildTheirCalls()
    {
        var (factory, nodes, context) = NewFactory();

        var imported = HelperCall(factory.CreateImportDefaultHelper(nodes.CreateIdentifier("module")), "___importDefault");
        Assert.Single(imported.Arguments!);
        Requested(context, "typescript:commonjsimportdefault");

        // `exportsExpression` defaults to the `exports` identifier, and the binding helper comes along with it.
        context.Requested.Clear();
        var exported = HelperCall(factory.CreateExportStarHelper(nodes.CreateIdentifier("module"), null), "___exportStar");
        Assert.Equal("exports", ((Identifier)exported.Arguments![1]).EscapedText);
        Assert.Equal(new[] { "typescript:export-star", "typescript:commonjscreatebinding" }, RequestedNames(context));

        var targeted = HelperCall(factory.CreateExportStarHelper(nodes.CreateIdentifier("module"), nodes.CreateIdentifier("target")), "___exportStar");
        Assert.Equal("target", ((Identifier)targeted.Arguments![1]).EscapedText);
    }

    // ---------------------------------------------------------------------------------------------
    // Class fields helpers
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void CreateClassPrivateFieldHelpers_AddTheKindAndTheNameOnlyWhenTheyHaveOne()
    {
        var (factory, nodes, context) = NewFactory();

        var get = HelperCall(factory.CreateClassPrivateFieldGetHelper(nodes.CreateIdentifier("receiver"), nodes.CreateIdentifier("state"), PrivateIdentifierKind.Field, nodes.CreateIdentifier("f")), "___classPrivateFieldGet");
        Assert.Equal(4, get.Arguments!.Count);
        Assert.Equal("f", Assert.IsType<StringLiteral>(get.Arguments[2]).Text);

        var getWithoutName = HelperCall(factory.CreateClassPrivateFieldGetHelper(nodes.CreateIdentifier("receiver"), nodes.CreateIdentifier("state"), PrivateIdentifierKind.Method, null), "___classPrivateFieldGet");
        Assert.Equal(3, getWithoutName.Arguments!.Count);
        Assert.Equal("m", Assert.IsType<StringLiteral>(getWithoutName.Arguments[2]).Text);

        var set = HelperCall(factory.CreateClassPrivateFieldSetHelper(nodes.CreateIdentifier("receiver"), nodes.CreateIdentifier("state"), nodes.CreateIdentifier("value"), PrivateIdentifierKind.Accessor, null), "___classPrivateFieldSet");
        Assert.Equal(4, set.Arguments!.Count);
        Assert.Equal("a", Assert.IsType<StringLiteral>(set.Arguments[3]).Text);

        var setWithName = HelperCall(factory.CreateClassPrivateFieldSetHelper(nodes.CreateIdentifier("receiver"), nodes.CreateIdentifier("state"), nodes.CreateIdentifier("value"), PrivateIdentifierKind.Field, nodes.CreateIdentifier("f")), "___classPrivateFieldSet");
        Assert.Equal(5, setWithName.Arguments!.Count);

        var @in = HelperCall(factory.CreateClassPrivateFieldInHelper(nodes.CreateIdentifier("state"), nodes.CreateIdentifier("receiver")), "___classPrivateFieldIn");
        Assert.Equal(2, @in.Arguments!.Count);

        Assert.Equal(
            new[] { "typescript:classPrivateFieldGet", "typescript:classPrivateFieldSet", "typescript:classPrivateFieldIn" },
            RequestedNames(context).Distinct().ToArray());
    }

    // ---------------------------------------------------------------------------------------------
    // 'using' helpers
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void CreateDisposableResourceHelpers_PassTheAsyncFlagAndTheEnvironment()
    {
        var (factory, nodes, context) = NewFactory();

        var asyncResource = HelperCall(factory.CreateAddDisposableResourceHelper(nodes.CreateIdentifier("env"), nodes.CreateIdentifier("value"), @async: true), "___addDisposableResource");
        Assert.Equal(3, asyncResource.Arguments!.Count);
        Assert.Equal(SyntaxKind.TrueKeyword, asyncResource.Arguments[2].Kind);

        var syncResource = HelperCall(factory.CreateAddDisposableResourceHelper(nodes.CreateIdentifier("env"), nodes.CreateIdentifier("value"), @async: false), "___addDisposableResource");
        Assert.Equal(SyntaxKind.FalseKeyword, syncResource.Arguments![2].Kind);

        var dispose = HelperCall(factory.CreateDisposeResourcesHelper(nodes.CreateIdentifier("env")), "___disposeResources");
        Assert.Single(dispose.Arguments!);

        Assert.Equal(
            new[] { "typescript:addDisposableResource", "typescript:disposeResources" },
            RequestedNames(context).Distinct().ToArray());
    }

    // ---------------------------------------------------------------------------------------------
    // --rewriteRelativeImportExtensions helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from the reference's <c>__rewriteRelativeImportExtension</c> text.</summary>
    /// <remarks>
    /// PORT-NOTE: this is the one helper text whose template literal carries backslashes, so it also pins the escape
    /// handling: the reference's <c>\\</c> in the source is a single <c>\</c> in the value, and the two regular
    /// expressions below only make sense with that.
    /// </remarks>
    private const string RewriteHelperText = @"
        var __rewriteRelativeImportExtension = (this && this.__rewriteRelativeImportExtension) || function (path, preserveJsx) {
            if (typeof path === ""string"" && /^\.\.?\//.test(path)) {
                return path.replace(/\.(tsx)$|((?:\.d)?)((?:\.[^./]+?)?)\.([cm]?)ts$/i, function (m, tsx, d, ext, cm) {
                    return tsx ? preserveJsx ? "".jsx"" : "".js"" : d && (!ext || !cm) ? m : (d + ext + ""."" + cm.toLowerCase() + ""js"");
                });
            }
            return path;
        };";

    [Fact]
    public void CreateRewriteRelativeImportExtensionsHelper_TakesThePreserveArgumentOnlyUnderJsxPreserve()
    {
        var (preserveFactory, preserveNodes, preserveContext) = NewFactory(new FakeCompilerOptions { Jsx = JsxEmit.Preserve });
        var withPreserve = HelperCall(preserveFactory.CreateRewriteRelativeImportExtensionsHelper(preserveNodes.CreateIdentifier("path")), "___rewriteRelativeImportExtension");
        Assert.Equal(2, withPreserve.Arguments!.Count);
        Assert.Equal(SyntaxKind.TrueKeyword, withPreserve.Arguments[1].Kind);
        var helper = Requested(preserveContext, "typescript:rewriteRelativeImportExtensions");
        Assert.Equal("__rewriteRelativeImportExtension", helper.ImportName);
        Assert.Equal(RewriteHelperText, helper.Text);

        // An unset `jsx` is not `Preserve`, and neither is any other mode: the second argument is absent.
        foreach (var jsx in new JsxEmit?[] { null, JsxEmit.None, JsxEmit.React, JsxEmit.ReactJSX, JsxEmit.ReactNative, JsxEmit.ReactJSXDev })
        {
            var (factory, nodes, context) = NewFactory(new FakeCompilerOptions { Jsx = jsx });
            var call = HelperCall(factory.CreateRewriteRelativeImportExtensionsHelper(nodes.CreateIdentifier("path")), "___rewriteRelativeImportExtension");
            Assert.Single(call.Arguments!);
            Requested(context, "typescript:rewriteRelativeImportExtensions");
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The descriptor table
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void EveryMember_RequestsTheDescriptorTheReferenceDeclares()
    {
        // One row per member that requests exactly one helper. `createAssignHelper` only requests one below ES2015,
        // and `createAsyncGeneratorHelper`, `createAsyncDelegatorHelper` and `createExportStarHelper` request two, so
        // those four are covered by the family tests above.
        var calls = new (string Member, Action<EmitHelperFactory, NodeFactory> Invoke, string Name, string ImportName, int? Priority, string Dependencies)[]
        {
            ("createDecorateHelper", (h, f) => h.CreateDecorateHelper([f.CreateIdentifier("d")], f.CreateIdentifier("t"), null, null),
                "typescript:decorate", "__decorate", 2, ""),
            ("createMetadataHelper", (h, f) => h.CreateMetadataHelper("design:type", f.CreateIdentifier("v")),
                "typescript:metadata", "__metadata", 3, ""),
            ("createParamHelper", (h, f) => h.CreateParamHelper(f.CreateIdentifier("p"), 0),
                "typescript:param", "__param", 4, ""),
            ("createESDecorateHelper", (h, f) => h.CreateESDecorateHelper(f.CreateNull(), f.CreateNull(), f.CreateIdentifier("d"), new ESDecorateClassContext(f.CreateIdentifier("n"), f.CreateIdentifier("m")), f.CreateNull(), f.CreateNull()),
                "typescript:esDecorate", "__esDecorate", 2, ""),
            ("createRunInitializersHelper", (h, f) => h.CreateRunInitializersHelper(f.CreateIdentifier("t"), f.CreateIdentifier("i"), null),
                "typescript:runInitializers", "__runInitializers", 2, ""),
            ("createAwaitHelper", (h, f) => h.CreateAwaitHelper(f.CreateIdentifier("e")),
                "typescript:await", "__await", null, ""),
            ("createAsyncValuesHelper", (h, f) => h.CreateAsyncValuesHelper(f.CreateIdentifier("e")),
                "typescript:asyncValues", "__asyncValues", null, ""),
            ("createRestHelper", (h, f) => h.CreateRestHelper(f.CreateIdentifier("o"), [f.CreateBindingElement(null, f.CreateIdentifier("a"), f.CreateIdentifier("b"), null), f.CreateIdentifier("p")], null, null),
                "typescript:rest", "__rest", null, ""),
            ("createAwaiterHelper", (h, f) => h.CreateAwaiterHelper(true, null, null, null, f.CreateBlock([], null)),
                "typescript:awaiter", "__awaiter", 5, ""),
            ("createExtendsHelper", (h, f) => h.CreateExtendsHelper(f.CreateIdentifier("C")),
                "typescript:extends", "__extends", 0, ""),
            ("createTemplateObjectHelper", (h, f) => h.CreateTemplateObjectHelper(f.CreateArrayLiteralExpression(null, null), f.CreateArrayLiteralExpression(null, null)),
                "typescript:makeTemplateObject", "__makeTemplateObject", 0, ""),
            ("createSpreadArrayHelper", (h, f) => h.CreateSpreadArrayHelper(f.CreateIdentifier("t"), f.CreateIdentifier("f"), true),
                "typescript:spreadArray", "__spreadArray", null, ""),
            ("createPropKeyHelper", (h, f) => h.CreatePropKeyHelper(f.CreateIdentifier("e")),
                "typescript:propKey", "__propKey", null, ""),
            ("createSetFunctionNameHelper", (h, f) => h.CreateSetFunctionNameHelper(f.CreateIdentifier("f"), f.CreateIdentifier("n"), null),
                "typescript:setFunctionName", "__setFunctionName", null, ""),
            ("createValuesHelper", (h, f) => h.CreateValuesHelper(f.CreateIdentifier("e")),
                "typescript:values", "__values", null, ""),
            ("createReadHelper", (h, f) => h.CreateReadHelper(f.CreateIdentifier("i"), null),
                "typescript:read", "__read", null, ""),
            ("createGeneratorHelper", (h, f) => h.CreateGeneratorHelper(f.CreateFunctionExpression(null, null, null, null, [], null, f.CreateBlock([], null))),
                "typescript:generator", "__generator", 6, ""),
            ("createImportStarHelper", (h, f) => h.CreateImportStarHelper(f.CreateIdentifier("m")),
                "typescript:commonjsimportstar", "__importStar", 2, "typescript:commonjscreatebinding,typescript:commonjscreatevalue"),
            ("createImportStarCallbackHelper", (h, f) => h.CreateImportStarCallbackHelper(),
                "typescript:commonjsimportstar", "__importStar", 2, "typescript:commonjscreatebinding,typescript:commonjscreatevalue"),
            ("createImportDefaultHelper", (h, f) => h.CreateImportDefaultHelper(f.CreateIdentifier("m")),
                "typescript:commonjsimportdefault", "__importDefault", null, ""),
            ("createClassPrivateFieldGetHelper", (h, f) => h.CreateClassPrivateFieldGetHelper(f.CreateIdentifier("r"), f.CreateIdentifier("s"), PrivateIdentifierKind.Field, null),
                "typescript:classPrivateFieldGet", "__classPrivateFieldGet", null, ""),
            ("createClassPrivateFieldSetHelper", (h, f) => h.CreateClassPrivateFieldSetHelper(f.CreateIdentifier("r"), f.CreateIdentifier("s"), f.CreateIdentifier("v"), PrivateIdentifierKind.Field, null),
                "typescript:classPrivateFieldSet", "__classPrivateFieldSet", null, ""),
            ("createClassPrivateFieldInHelper", (h, f) => h.CreateClassPrivateFieldInHelper(f.CreateIdentifier("s"), f.CreateIdentifier("r")),
                "typescript:classPrivateFieldIn", "__classPrivateFieldIn", null, ""),
            ("createAddDisposableResourceHelper", (h, f) => h.CreateAddDisposableResourceHelper(f.CreateIdentifier("e"), f.CreateIdentifier("v"), true),
                "typescript:addDisposableResource", "__addDisposableResource", null, ""),
            ("createDisposeResourcesHelper", (h, f) => h.CreateDisposeResourcesHelper(f.CreateIdentifier("e")),
                "typescript:disposeResources", "__disposeResources", null, ""),
            ("createRewriteRelativeImportExtensionsHelper", (h, f) => h.CreateRewriteRelativeImportExtensionsHelper(f.CreateIdentifier("p")),
                "typescript:rewriteRelativeImportExtensions", "__rewriteRelativeImportExtension", null, ""),
        };

        foreach (var (member, invoke, name, importName, priority, dependencies) in calls)
        {
            var (factory, nodes, context) = NewFactory();
            invoke(factory, nodes);

            var helper = Assert.IsType<EmitHelper>(Assert.Single(context.Requested));
            var actualDependencies = helper.Dependencies is null
                ? ""
                : string.Join(",", helper.Dependencies.OfType<EmitHelper>().Select(d => d.Name));
            Assert.Equal(
                (member, name, importName, priority, dependencies),
                (member, helper.Name, helper.ImportName, helper.Priority, actualDependencies));
            Assert.False(helper.Scoped);
        }
    }

    [Fact]
    public void TheDescriptors_AreSharedInstancesWithUniqueHelperNames()
    {
        // The emitter deduplicates a node's helpers by identity, and a dependency is one of those instances, so the
        // graph is built from shared objects rather than from equal copies.
        Assert.Same(EmitHelpers.AwaitHelper, EmitHelpers.AsyncGeneratorHelper.Dependencies![0]);
        Assert.Same(EmitHelpers.AwaitHelper, EmitHelpers.AsyncDelegator.Dependencies![0]);
        Assert.Equal(
            new Node[] { EmitHelpers.CreateBindingHelper, EmitHelpers.SetModuleDefaultHelper },
            EmitHelpers.ImportStarHelper.Dependencies!);
        Assert.Same(EmitHelpers.CreateBindingHelper, EmitHelpers.ExportStarHelper.Dependencies![0]);

        // The two helpers the transformers add by hand are scoped — their text refers to `super` — and are not
        // importable; every other descriptor is an unscoped, importable `typescript:` helper.
        foreach (var helper in new[] { EmitHelpers.AsyncSuperHelper, EmitHelpers.AdvancedAsyncSuperHelper })
        {
            Assert.True(helper.Scoped);
            Assert.Null(helper.ImportName);
            Assert.StartsWith("typescript:", helper.Name);
        }

        var descriptors = new[]
        {
            EmitHelpers.DecorateHelper, EmitHelpers.MetadataHelper, EmitHelpers.ParamHelper, EmitHelpers.ESDecorateHelper,
            EmitHelpers.RunInitializersHelper, EmitHelpers.AssignHelper, EmitHelpers.AwaitHelper, EmitHelpers.AsyncGeneratorHelper,
            EmitHelpers.AsyncDelegator, EmitHelpers.AsyncValues, EmitHelpers.RestHelper, EmitHelpers.AwaiterHelper,
            EmitHelpers.ExtendsHelper, EmitHelpers.TemplateObjectHelper, EmitHelpers.ReadHelper, EmitHelpers.SpreadArrayHelper,
            EmitHelpers.PropKeyHelper, EmitHelpers.SetFunctionNameHelper, EmitHelpers.ValuesHelper, EmitHelpers.GeneratorHelper,
            EmitHelpers.CreateBindingHelper, EmitHelpers.SetModuleDefaultHelper, EmitHelpers.ImportStarHelper,
            EmitHelpers.ImportDefaultHelper, EmitHelpers.ExportStarHelper, EmitHelpers.ClassPrivateFieldGetHelper,
            EmitHelpers.ClassPrivateFieldSetHelper, EmitHelpers.ClassPrivateFieldInHelper, EmitHelpers.AddDisposableResourceHelper,
            EmitHelpers.DisposeResourcesHelper, EmitHelpers.RewriteRelativeImportExtensionsHelper,
        };

        Assert.Equal(descriptors.Length, descriptors.Select(h => h.Name).Distinct().Count());
        foreach (var helper in descriptors)
        {
            Assert.StartsWith("typescript:", helper.Name);
            Assert.False(helper.Scoped);
            Assert.NotNull(helper.ImportName);
        }
    }

    [Fact]
    public void TheTwoHelperStringHelpers_InterpolateTheUniqueName()
    {
        // `helperString` turns its template into a callback; each reference text has one interpolation,
        // `uniqueName("_superIndex")`, between the literal chunks.
        var simple = Assert.IsType<Func<EmitHelperUniqueNameCallback, string>>(EmitHelpers.AsyncSuperHelper.Text);
        Assert.Equal(
            "\n            const _superIndex = name => super[name];",
            simple(name => name));

        var advanced = Assert.IsType<Func<EmitHelperUniqueNameCallback, string>>(EmitHelpers.AdvancedAsyncSuperHelper.Text);
        var text = advanced(name => name);
        Assert.StartsWith("\n            const _superIndex = (function (geti, seti) {", text);
        Assert.EndsWith("})(name => super[name], (name, value) => super[name] = value);", text);
        Assert.Contains("return name => cache[name] || (cache[name] = { get value() { return geti(name); }, set value(v) { seti(name, v); } });", text);

        Assert.Equal("typescript:async-super", EmitHelpers.AsyncSuperHelper.Name);
        Assert.Equal("typescript:advanced-async-super", EmitHelpers.AdvancedAsyncSuperHelper.Name);
    }

    // ---------------------------------------------------------------------------------------------
    // compareEmitHelpers / isCallToHelper
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void CompareEmitHelpers_OrdersByPriorityWithThePrioritisedHelpersLast()
    {
        Assert.Equal(Comparison.EqualTo, EmitHelpers.CompareEmitHelpers(EmitHelpers.AwaitHelper, EmitHelpers.AwaitHelper));
        Assert.Equal(Comparison.EqualTo, EmitHelpers.CompareEmitHelpers(EmitHelpers.DecorateHelper, EmitHelpers.ESDecorateHelper));

        // A helper with no priority sorts after one that has a priority, whatever that priority is.
        Assert.Equal(Comparison.GreaterThan, EmitHelpers.CompareEmitHelpers(EmitHelpers.AwaitHelper, EmitHelpers.DecorateHelper));
        Assert.Equal(Comparison.LessThan, EmitHelpers.CompareEmitHelpers(EmitHelpers.DecorateHelper, EmitHelpers.AwaitHelper));

        // Two priorities compare numerically.
        Assert.Equal(Comparison.LessThan, EmitHelpers.CompareEmitHelpers(EmitHelpers.ExtendsHelper, EmitHelpers.AssignHelper));
        Assert.Equal(Comparison.GreaterThan, EmitHelpers.CompareEmitHelpers(EmitHelpers.AwaiterHelper, EmitHelpers.DecorateHelper));

        var ordered = new List<EmitHelper> { EmitHelpers.AwaitHelper, EmitHelpers.DecorateHelper, EmitHelpers.ExtendsHelper, EmitHelpers.AwaiterHelper };
        ordered.Sort((x, y) => (int)EmitHelpers.CompareEmitHelpers(x, y));
        Assert.Equal(
            new[] { EmitHelpers.ExtendsHelper, EmitHelpers.DecorateHelper, EmitHelpers.AwaiterHelper, EmitHelpers.AwaitHelper },
            ordered);
    }

    [Fact]
    public void IsCallToHelper_RequiresTheHelperNameFlagAndTheEscapedText()
    {
        var (helperFactory, nodes, _) = NewFactory();

        var helperCall = (CallExpression)nodes.CreateCallExpression(helperFactory.GetUnscopedHelperName("__rest"), null, [nodes.CreateIdentifier("o")]);
        Assert.True(EmitHelpers.IsCallToHelper(helperCall, "___rest"));
        Assert.False(EmitHelpers.IsCallToHelper(helperCall, "___read"));

        // The `HelperName` flag is what tells a helper call apart from an ordinary call of the same name.
        var plainCall = nodes.CreateCallExpression(nodes.CreateIdentifier("__rest"), null, [nodes.CreateIdentifier("o")]);
        Assert.False(EmitHelpers.IsCallToHelper(plainCall, "__rest"));

        // Not a call, and a call whose callee is not an identifier, both answer no.
        Assert.False(EmitHelpers.IsCallToHelper(nodes.CreateIdentifier("__rest"), "___rest"));
        Assert.False(EmitHelpers.IsCallToHelper(
            nodes.CreateCallExpression(nodes.CreatePropertyAccessExpression(nodes.CreateIdentifier("a"), "__rest"), null, []),
            "___rest"));
    }
}
