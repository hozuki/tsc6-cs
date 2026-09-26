namespace TypeScript.Compiler;

// ported from: src/compiler/factory/nodeConverters.ts:41-193
/// <summary>
/// Turns a binding declaration into its assignment counterpart: a function or class declaration into the expression
/// form of the same thing, and a binding pattern (or binding element) into an assignment pattern (or assignment
/// element).
/// </summary>
/// <remarks>
/// PORT-NOTE: TS builds this as the closure returned from <c>createNodeConverters(factory)</c>; the port is a class
/// holding the factory, the same treatment <see cref="ParenthesizerRules"/> and <see cref="NodeFactory"/> got (§5
/// rule 6). The module's <c>nullNodeConverters</c> is <see cref="NullNodeConverters"/>.
/// <para>
/// PORT-NOTE: the generated interface widens some of the parameters this file takes (§12.5). The pattern parameters
/// (<c>BindingOrAssignmentPattern</c>, <c>ObjectBindingOrAssignmentPattern</c>, <c>ArrayBindingOrAssignmentPattern</c>,
/// <c>BindingOrAssignmentElementTarget</c>, <c>ConciseBody</c>) become <c>Node?</c>, and the element parameters become
/// <c>object?</c> — the array element union has an arm, <c>AssignmentExpression&lt;EqualsToken&gt;</c>, whose only
/// ported form is the abstract <c>AssignmentExpression&lt;TOperator&gt;</c>, so the union has nothing to converge on.
/// The casts below are the narrowing TS's declared types already provided; at runtime an assignment element is a
/// <see cref="BinaryExpression"/> of kind <c>EqualsToken</c>, so <c>isExpression</c> accepts it.
/// </para>
/// </remarks>
internal sealed class NodeConverters : INodeConverters
{
    private readonly NodeFactory _factory;

    /// <summary>ported from: src/compiler/factory/nodeConverters.ts:41 — <c>createNodeConverters</c>.</summary>
    public NodeConverters(NodeFactory factory) => _factory = factory;

    /// <summary>ported from: src/compiler/factory/nodeConverters.ts:54-61 — <c>convertToFunctionBlock</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>ConciseBody</c> (<c>FunctionBody | Expression</c>) and its return type
    /// <c>Block</c>, which is why the non-block branch can hand the value to <c>createReturnStatement</c> directly;
    /// the port's parameter is <c>Node?</c>, so the two branches cast to what <c>isBlock</c> leaves TS holding.
    /// </remarks>
    public Block ConvertToFunctionBlock(Node? node, bool? multiLine)
    {
        if (NodeTests.IsBlock(node!)) return (Block)node!;

        var returnStatement = _factory.CreateReturnStatement((Expression)node!);
        Utilities.SetTextRange(returnStatement, node);
        var body = _factory.CreateBlock([returnStatement], multiLine);
        Utilities.SetTextRange(body, node);
        return body;
    }

    /// <summary>ported from: src/compiler/factory/nodeConverters.ts:63-80 — <c>convertToFunctionExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>Debug.fail(message)</c> returns <c>never</c> and is used as this function's answer; the port's
    /// counterpart is <c>Debug.Fail&lt;T&gt;</c>, which throws and fits any return type. Its message is TS's.
    /// </remarks>
    public FunctionExpression ConvertToFunctionExpression(FunctionDeclaration? node)
    {
        var declaration = node!;
        if (declaration.Body is null) return Debug.Fail<FunctionExpression>("Cannot convert a FunctionDeclaration without a body");

        var updated = _factory.CreateFunctionExpression(
            GetModifiersForFunctionExpression(declaration.Modifiers),
            declaration.AsteriskToken,
            declaration.Name,
            declaration.TypeParameters,
            declaration.Parameters,
            declaration.Type,
            declaration.Body);
        NodeFactory.SetOriginalNode(updated, declaration);
        Utilities.SetTextRange(updated, declaration);
        if (Js.IsTruthy(EmitNodes.GetStartsOnNewLine(declaration)))
        {
            EmitNodes.SetStartsOnNewLine(updated, /*newLine*/ true);
        }

        return updated;
    }

    /// <summary>ported from: src/compiler/factory/nodeConverters.ts:82-96 — <c>convertToClassExpression</c>.</summary>
    public ClassExpression ConvertToClassExpression(ClassDeclaration? node)
    {
        var declaration = node!;
        var updated = _factory.CreateClassExpression(
            GetModifiersForClassExpression(declaration.Modifiers),
            declaration.Name,
            declaration.TypeParameters,
            declaration.HeritageClauses,
            declaration.Members);
        NodeFactory.SetOriginalNode(updated, declaration);
        Utilities.SetTextRange(updated, declaration);
        if (Js.IsTruthy(EmitNodes.GetStartsOnNewLine(declaration)))
        {
            EmitNodes.SetStartsOnNewLine(updated, /*newLine*/ true);
        }

        return updated;
    }

    /// <summary>ported from: src/compiler/factory/nodeConverters.ts:98-116 — <c>convertToArrayAssignmentElement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>Debug.assertNode(node, isIdentifier)</c> is not ported (§10.4); <see cref="AssertIdentifier"/>
    /// performs the same "defined and passes the test" assertion, and the cast after it is the narrowing that
    /// assertion gives TS. The assertion message is the port's own.
    /// </remarks>
    public Expression ConvertToArrayAssignmentElement(object? element)
    {
        var node = (Node)element!;
        if (NodeTests.IsBindingElement(node))
        {
            var bindingElement = (BindingElement)node;
            if (bindingElement.DotDotDotToken is not null)
            {
                AssertIdentifier(bindingElement.Name);
                return NodeFactory.SetOriginalNode(
                    Utilities.SetTextRange(_factory.CreateSpreadElement((Expression)bindingElement.Name!), bindingElement),
                    bindingElement);
            }

            var expression = ConvertToAssignmentElementTarget(bindingElement.Name);
            return bindingElement.Initializer is not null
                ? NodeFactory.SetOriginalNode(
                    Utilities.SetTextRange(CreateAssignment(expression, bindingElement.Initializer), bindingElement),
                    bindingElement)
                : expression;
        }

        return Core.Cast<Expression, Node>(node, Utilities.IsExpression);
    }

    /// <summary>ported from: src/compiler/factory/nodeConverters.ts:118-133 — <c>convertToObjectAssignmentElement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: as in <see cref="ConvertToArrayAssignmentElement"/>, <c>Debug.assertNode(node, isIdentifier)</c> is
    /// replaced by <see cref="AssertIdentifier"/>, and <c>cast(element, isObjectLiteralElementLike)</c> keeps TS's
    /// test while answering the <c>Node</c> the interface declares. <c>createShorthandPropertyAssignment</c> takes the
    /// name as <c>object?</c>, so only the spread branch needs the identifier's <see cref="Expression"/> type.
    /// </remarks>
    public Node ConvertToObjectAssignmentElement(object? element)
    {
        var node = (Node)element!;
        if (NodeTests.IsBindingElement(node))
        {
            var bindingElement = (BindingElement)node;
            if (bindingElement.DotDotDotToken is not null)
            {
                AssertIdentifier(bindingElement.Name);
                return NodeFactory.SetOriginalNode(
                    Utilities.SetTextRange(_factory.CreateSpreadAssignment((Expression)bindingElement.Name!), bindingElement),
                    bindingElement);
            }

            if (bindingElement.PropertyName is not null)
            {
                var expression = ConvertToAssignmentElementTarget(bindingElement.Name);
                return NodeFactory.SetOriginalNode(
                    Utilities.SetTextRange(
                        _factory.CreatePropertyAssignment(
                            bindingElement.PropertyName,
                            bindingElement.Initializer is not null ? CreateAssignment(expression, bindingElement.Initializer) : expression),
                        bindingElement),
                    bindingElement);
            }

            AssertIdentifier(bindingElement.Name);
            return NodeFactory.SetOriginalNode(
                Utilities.SetTextRange(
                    _factory.CreateShorthandPropertyAssignment(bindingElement.Name, bindingElement.Initializer),
                    bindingElement),
                bindingElement);
        }

        return Core.Cast<Node, Node>(node, Utilities.IsObjectLiteralElementLike);
    }

    /// <summary>ported from: src/compiler/factory/nodeConverters.ts:135-145 — <c>convertToAssignmentPattern</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's switch covers every member of <c>BindingOrAssignmentPattern</c> and so has no <c>default</c> —
    /// which TS's checker accepts because the function then cannot fall out of range. C# needs a terminating arm, and
    /// the generated parameter is the wider <c>Node?</c>; the arm fails rather than answer with a pattern that is not
    /// the one asked for.
    /// </remarks>
    public MemberExpression ConvertToAssignmentPattern(Node? node)
    {
        switch (node!.Kind)
        {
            case SyntaxKind.ArrayBindingPattern:
            case SyntaxKind.ArrayLiteralExpression:
                return ConvertToArrayAssignmentPattern(node);

            case SyntaxKind.ObjectBindingPattern:
            case SyntaxKind.ObjectLiteralExpression:
                return ConvertToObjectAssignmentPattern(node);

            default:
                return Debug.Fail<MemberExpression>($"Unexpected node kind: {node!.Kind}");
        }
    }

    /// <summary>ported from: src/compiler/factory/nodeConverters.ts:147-158 — <c>convertToObjectAssignmentPattern</c>.</summary>
    public ObjectLiteralExpression ConvertToObjectAssignmentPattern(Node? node)
    {
        if (NodeTests.IsObjectBindingPattern(node!))
        {
            var bindingPattern = (ObjectBindingPattern)node!;
            return NodeFactory.SetOriginalNode(
                Utilities.SetTextRange(
                    _factory.CreateObjectLiteralExpression(
                        Core.Map(bindingPattern.Elements, (element, _) => ConvertToObjectAssignmentElement(element)),
                        /*multiLine*/ null),
                    bindingPattern),
                bindingPattern);
        }

        return Core.Cast<ObjectLiteralExpression, Node>(node!, NodeTests.IsObjectLiteralExpression);
    }

    /// <summary>ported from: src/compiler/factory/nodeConverters.ts:160-171 — <c>convertToArrayAssignmentPattern</c>.</summary>
    public ArrayLiteralExpression ConvertToArrayAssignmentPattern(Node? node)
    {
        if (NodeTests.IsArrayBindingPattern(node!))
        {
            var bindingPattern = (ArrayBindingPattern)node!;
            return NodeFactory.SetOriginalNode(
                Utilities.SetTextRange(
                    _factory.CreateArrayLiteralExpression(
                        Core.Map(bindingPattern.Elements, (element, _) => ConvertToArrayAssignmentElement(element)),
                        /*multiLine*/ null),
                    bindingPattern),
                bindingPattern);
        }

        return Core.Cast<ArrayLiteralExpression, Node>(node!, NodeTests.IsArrayLiteralExpression);
    }

    /// <summary>ported from: src/compiler/factory/nodeConverters.ts:173-179 — <c>convertToAssignmentElementTarget</c>.</summary>
    public Expression ConvertToAssignmentElementTarget(Node? node)
    {
        if (Utilities.IsBindingPattern(node)) return ConvertToAssignmentPattern(node);

        return Core.Cast<Expression, Node>(node!, Utilities.IsExpression);
    }

    //
    // The helpers TS keeps as free functions of the module
    //

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1030-1034 — <c>getModifiers</c>, with the filter
    /// <c>convertToFunctionExpression</c> applies to its answer (nodeConverters.ts:65).</summary>
    /// <remarks>
    /// PORT-NOTE: <c>getModifiers</c> and the <c>isModifierKind</c>/<c>isModifier</c> pair it is built from
    /// (utilitiesPublic.ts:1612-1649) are not ported yet — they belong to M5's <c>utilitiesPublic.ts</c>. For this one
    /// call site the substitution is exact:
    /// <list type="bullet">
    /// <item><description>
    /// <c>hasSyntacticModifier(node, ModifierFlags.Modifier)</c> reads nothing but <c>modifiersToFlags(node.modifiers)</c>
    /// for a function declaration: the other input of <c>getSyntacticModifierFlagsNoCache</c> is a
    /// <c>NodeFlags.NestedNamespace</c>/<c>IdentifierIsInJSDocNamespace</c> flag, which the parser only ever sets on a
    /// module declaration or a namespace name, never on a function declaration.
    /// </description></item>
    /// <item><description>
    /// <c>filter(node.modifiers, isModifier)</c> keeps a <c>ModifierLike</c> exactly when it is not a decorator, and
    /// <see cref="Utilities.ModifierToFlag"/> is the ported <c>modifierToFlag</c>: the fifteen kinds
    /// <c>isModifierKind</c> lists are precisely the kinds it maps to a flag other than <c>Decorator</c>
    /// (<c>ModifierFlags.Modifier</c> is <c>ModifierFlags.All &amp; ~ModifierFlags.Decorator</c>, so the gate above
    /// rejects a decorator-only array as TS does).
    /// </description></item>
    /// </list>
    /// PORT-NOTE: TS chains <c>getModifiers(node)?.filter(…)</c>, and its <c>filter</c> hands the very same array back
    /// when it removed nothing — so <c>createFunctionExpression</c> can end up storing the declaration's own
    /// <c>NodeArray</c>. The port folds the two filters into one pass and always allocates, because that member is
    /// typed <c>List&lt;ModifierToken&gt;</c> while a declaration's <c>modifiers</c> is a <c>NodeArray&lt;Node&gt;</c>
    /// (§12.5 — <c>ModifierLike</c> collapses to <c>Node</c>); the elements and their order are the same either way.
    /// </remarks>
    private static List<ModifierToken>? GetModifiersForFunctionExpression(NodeArray<Node>? modifiers)
    {
        if (modifiers is null || (Utilities.ModifiersToFlags(modifiers) & ModifierFlags.Modifier) == 0) return null;

        var result = new List<ModifierToken>(modifiers.Count);
        foreach (var modifier in modifiers)
        {
            // filter(node.modifiers, isModifier), then the two kinds the call site drops.
            var flag = Utilities.ModifierToFlag(modifier.Kind);
            if (flag is ModifierFlags.None or ModifierFlags.Decorator) continue;
            if (NodeTests.IsExportModifier(modifier) || NodeTests.IsDefaultModifier(modifier)) continue;

            result.Add((ModifierToken)modifier);
        }

        return result;
    }

    /// <summary>ported from: src/compiler/factory/nodeConverters.ts:84-85 — the <c>node.modifiers?.filter(…)</c>
    /// handed to <c>createClassExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: unlike the function-expression path, TS filters <c>node.modifiers</c> directly here — no
    /// <c>getModifiers</c>, because <c>createClassExpression</c> takes <c>ModifierLike[]</c> and so keeps whatever
    /// decorators the declaration carries. <see cref="Core.Filter"/> reproduces TS's "hand back the same array when
    /// nothing was filtered out" behaviour; its answer is an <c>IList&lt;Node&gt;?</c> while
    /// <see cref="NodeFactory.CreateClassExpression"/> takes <c>List&lt;Node&gt;?</c>, and the cast cannot fail because
    /// <c>Filter</c> only ever returns its input (a <see cref="NodeArray{T}"/>, which <em>is</em> a
    /// <c>List&lt;Node&gt;</c>) or a freshly built <c>List&lt;Node&gt;</c>.
    /// </remarks>
    private static List<Node>? GetModifiersForClassExpression(NodeArray<Node>? modifiers)
        => Core.Filter(modifiers, modifier => !NodeTests.IsExportModifier(modifier) && !NodeTests.IsDefaultModifier(modifier)) as List<Node>;

    /// <summary>ported from: src/compiler/debug.ts — <c>Debug.assertNode(node, isIdentifier)</c> (§10.4: not ported).</summary>
    /// <remarks>
    /// PORT-NOTE: <c>assertNode</c> is an assertion function: it asserts the node is defined and passes the test, and
    /// then <em>narrows its type</em>, which the three call sites here rely on. C# has no assertion functions, so the
    /// check is a helper and each caller writes the cast for the narrowing. The check mirrors TS's
    /// <c>node !== undefined &amp;&amp; test(node)</c> (so a null argument fails the assertion instead of throwing from
    /// the test); TS passes no message, this one names the expectation.
    /// </remarks>
    private static void AssertIdentifier(Node? node)
        => Debug.Assert(node is not null && NodeTests.IsIdentifier(node), "Expected an Identifier");

    /// <summary>ported from: src/compiler/factory/nodeFactory.ts:1025-1027 — <c>createAssignment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>factory.createAssignment</c> is a lazily memoized alias, <c>getBinaryCreateFunction(SyntaxKind.EqualsToken)</c>
    /// (nodeFactory.ts:500), whose function is exactly <c>createBinaryExpression(left, SyntaxKind.EqualsToken, right)</c>.
    /// The port cannot call the alias yet: <c>createAssignment</c> belongs to M4d's <c>factory/utilities.ts</c> (§15.8).
    /// This calls the member the alias forwards to, with the same three arguments — the node the alias produces is that
    /// call's answer. <c>CreateBinaryExpression</c> is itself still a stub waiting on <c>forEachChild</c> (M4e), so this
    /// path throws until M4e lands; the call site is nonetheless the faithful one. The cast is the port's own: the
    /// created node is an <see cref="Expression"/>, which is what TS's <c>AssignmentExpression&lt;EqualsToken&gt;</c>
    /// returns in this model.
    /// </remarks>
    private Expression CreateAssignment(Expression left, Expression right)
        => (Expression)_factory.CreateBinaryExpression(left, SyntaxKind.EqualsToken, right)!;
}

// ported from: src/compiler/factory/nodeConverters.ts:183-193 — `nullNodeConverters`
/// <summary>
/// The converters that convert nothing: every method reports that it is not implemented.
/// </summary>
/// <remarks>
/// TS has the same object, used to build a factory whose converters are never reached. The port keeps it as the
/// counterpart rather than dropping it: a factory built with it behaves exactly as TS's does — it throws where a
/// converter is called.
/// <para>
/// PORT-NOTE: TS writes <c>notImplemented</c> (a function of type <c>never</c>) as the body of every member, and the
/// port's <c>Core.NotImplemented()</c> is its counterpart (<c>NotImplementedException("Not implemented")</c>) — but it
/// returns <c>void</c>, so it cannot stand where a member's value is expected. Each member therefore throws that same
/// exception itself.
/// </para>
/// </remarks>
internal sealed class NullNodeConverters : INodeConverters
{
    public static NullNodeConverters Instance { get; } = new();

    private NullNodeConverters()
    {
    }

    /// <summary>ported from: src/compiler/factory/nodeConverters.ts:184 — <c>convertToFunctionBlock</c>.</summary>
    public Block ConvertToFunctionBlock(Node? node, bool? multiLine) => throw new NotImplementedException("Not implemented");

    /// <summary>ported from: src/compiler/factory/nodeConverters.ts:185 — <c>convertToFunctionExpression</c>.</summary>
    public FunctionExpression ConvertToFunctionExpression(FunctionDeclaration? node) => throw new NotImplementedException("Not implemented");

    /// <summary>ported from: src/compiler/factory/nodeConverters.ts:186 — <c>convertToClassExpression</c>.</summary>
    public ClassExpression ConvertToClassExpression(ClassDeclaration? node) => throw new NotImplementedException("Not implemented");

    /// <summary>ported from: src/compiler/factory/nodeConverters.ts:187 — <c>convertToArrayAssignmentElement</c>.</summary>
    public Expression ConvertToArrayAssignmentElement(object? element) => throw new NotImplementedException("Not implemented");

    /// <summary>ported from: src/compiler/factory/nodeConverters.ts:188 — <c>convertToObjectAssignmentElement</c>.</summary>
    public Node ConvertToObjectAssignmentElement(object? element) => throw new NotImplementedException("Not implemented");

    /// <summary>ported from: src/compiler/factory/nodeConverters.ts:189 — <c>convertToAssignmentPattern</c>.</summary>
    public MemberExpression ConvertToAssignmentPattern(Node? node) => throw new NotImplementedException("Not implemented");

    /// <summary>ported from: src/compiler/factory/nodeConverters.ts:190 — <c>convertToObjectAssignmentPattern</c>.</summary>
    public ObjectLiteralExpression ConvertToObjectAssignmentPattern(Node? node) => throw new NotImplementedException("Not implemented");

    /// <summary>ported from: src/compiler/factory/nodeConverters.ts:191 — <c>convertToArrayAssignmentPattern</c>.</summary>
    public ArrayLiteralExpression ConvertToArrayAssignmentPattern(Node? node) => throw new NotImplementedException("Not implemented");

    /// <summary>ported from: src/compiler/factory/nodeConverters.ts:192 — <c>convertToAssignmentElementTarget</c>.</summary>
    public Expression ConvertToAssignmentElementTarget(Node? node) => throw new NotImplementedException("Not implemented");
}
